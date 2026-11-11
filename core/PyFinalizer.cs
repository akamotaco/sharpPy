using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace SharpPy
{
    internal enum GuardState { Armed, Watched, Finalized, Cleared }

    /// <summary>
    /// What every weak reference to one object shares: the two views of the target, and the verdict.
    /// GCHandles and not WeakReference objects, because a WeakReference has a finalizer that frees its
    /// handle. A ref reachable only through a parent being finalized goes through the same collection
    /// as the parent, so its WeakReference was emptied before the parent's __del__ could read it.
    /// This class has no finalizer and does not hold the target; the guard frees the handles.
    ///
    /// Long follows the object through finalization, so a __del__ can still reach it.
    /// Short goes null at the collection that found the object unreachable. It is how a read notices
    /// that the object it is about to hand out is condemned (q4_weakref_state.md H1).
    /// </summary>
    internal sealed class PyWeakCell
    {
        internal GCHandle Long;
        internal GCHandle Short;
        /// <summary>The truth; the handles are only a means of noticing. Set before any callback runs.</summary>
        internal bool Dead;

        internal PyWeakCell(PyObject target)
        {
            Long = GCHandle.Alloc(target, GCHandleType.WeakTrackResurrection);
            Short = GCHandle.Alloc(target, GCHandleType.Weak);
        }

        internal void Release()
        {
            Dead = true;
            Long.Free();
            Short.Free();
        }
    }

    /// <summary>
    /// The signal that an object was let go, and the reason its owner can still be handed to
    /// __del__.
    ///
    /// In C the object notices its own death and calls the finalizer inline
    /// (Objects/object.c:357 PyObject_CallFinalizer, reached from subtype_dealloc at
    /// Objects/typeobject.c:1901). On the CLR only the collector notices, and it notices on the
    /// finalizer thread. So the notice is split in two: the guard's finalizer queues the guard,
    /// and the interpreter thread runs the Python half at its next safe point (PyFinalizer.Drain).
    ///
    /// The guard holds its owner STRONGLY. That is what makes `self` usable inside __del__: the
    /// guard is kept alive by ConditionalWeakTable's dependency on the key, so while the object is
    /// reachable the guard is too, and when the object becomes unreachable the pair dies together -
    /// the guard's own finalizer then runs, and by dequeuing it the interpreter brings both back.
    /// Verified with the isolated spike in docs/plans/lifetime_foundation/spike/ (300,000 handoffs,
    /// none lost; __del__ sees its own long weak reference alive, and a child's short one dead).
    /// </summary>
    internal sealed class PyGuard
    {
        /// <summary>Strong. This is how self survives until __del__ runs.</summary>
        internal PyObject Owner;
        internal GuardState State;
        /// <summary>Creation order, which is the order shutdown must follow for the leftovers.</summary>
        internal long Sequence;
        /// <summary>
        /// This guard's row in PyFinalizer.Live, or null when it is not listed. The number lives on the
        /// row, not on the guard: a swap-remove moves a row, and the guard that used to own it may
        /// already be unreachable, so its weak reference cannot be resolved to be retargeted. Measured
        /// on the Q2 code: with 1000 droppers the shutdown sweep lost two survivors, three runs out of
        /// three, because a stale number deleted a different guard's row (docs/plans/lifetime_foundation
        /// /local_ai_queue.md L1.1).
        /// </summary>
        internal PyFinalizer.LiveEntry Entry;
        /// <summary>
        /// The object's weakreflist, oldest first. Handles and not WeakReference objects: a
        /// WeakReference inside a guard is invalidated with the guard, while a WeakTrackResurrection
        /// handle still answers for a ref reachable only through the target, and goes null for a ref
        /// that died first (q4_weakref_state.md section 2, spike/guard_weak_list.cs.txt).
        /// </summary>
        internal List<GCHandle> WeakRefs;
        internal PyWeakCell Cell;
        /// <summary>tp_finalize ran, or was settled by a clean finish; gc.is_finalized reports it.</summary>
        internal bool FinalizerRan;
        /// <summary>
        /// The guard of the object that writes through this one, which goes first. Weak, or the
        /// inner object would keep the outer alive; a WeakTrackResurrection handle and not a
        /// WeakReference, which is finalizable itself and can be gone in the same collection.
        /// </summary>
        internal GCHandle Outer;

        internal PyGuard OuterGuard => Outer.IsAllocated ? Outer.Target as PyGuard : null;

        internal void SetOuter(PyGuard outer)
        {
            if (Outer.IsAllocated)
                Outer.Free();
            Outer = outer == null ? default : GCHandle.Alloc(outer, GCHandleType.WeakTrackResurrection);
        }

        internal PyGuard(PyObject owner) { Owner = owner; }

        ~PyGuard() { PyFinalizer.FromFinalizerThread(this); }
    }

    /// <summary>
    /// __del__ dispatch. The mechanism it copies, in reading order:
    ///
    ///   Objects/typeobject.c:9499  TPSLOT(__del__, tp_finalize, slot_tp_finalize, ...) binds the
    ///                              Python method to a C slot once, at type creation.
    ///   Objects/typeobject.c:9069  slot_tp_finalize looks the method up on the type - so a subclass
    ///                              override wins and super().__del__() reaches the base - and it
    ///                              saves the in-flight exception at :9076 (PyErr_GetRaisedException)
    ///                              and restores it at :9090 (PyErr_SetRaisedException).
    ///   Objects/object.c:357       PyObject_CallFinalizer runs the slot once; the comment at :362
    ///                              "tp_finalize should only be called once" and the _PyGC_FINALIZED
    ///                              flag are the "once" rule. :374/:398 handle resurrection.
    ///   Python/ceval_gil.c:898     pending calls are run by one caller at a time and never
    ///                              recursively, which is why Drain re-enters as a no-op.
    ///
    /// What is the same here: dispatch through the type, the once rule, the exception save/restore,
    /// the "Exception ignored in:" report, and now the delivery itself - an object dropped mid-run
    /// gets its __del__ at the next safe point, or at the latest during gc.collect(), which is the
    /// deterministic anchor this design offers instead of a death instant.
    ///
    /// What stays different (docs/lifetime_and_gc_policy.md, docs/known_gaps.md P0-37): *when*.
    /// CPython finalizes when the count hits zero; SharpPy finalizes after the CLR collector decides
    /// the object is unreachable. A program that prints around `del x` without gc.collect() sees the
    /// difference, and no part of this design pretends otherwise.
    /// </summary>
    internal static class PyFinalizer
    {
        // The table is the only place a guard is filed. ConditionalWeakTable keeps the value alive
        // exactly as long as the key, which is the mechanism the guard's strong Owner depends on.
        private static readonly ConditionalWeakTable<PyObject, PyGuard> Guards = new();

        // Enqueue is the only thing the finalizer thread does. ConcurrentQueue because the producer
        // is not the interpreter thread; no lock is taken while Python runs.
        private static readonly ConcurrentQueue<PyGuard> Queue = new();

        // Weak on purpose: the guards that are still waiting for their object to die. Short
        // references are enough here because the tracked object is the guard, not the Python object.
        /// <summary>
        /// The guards that still owe a __del__, in creation order for the shutdown sweep. Each row holds
        /// its own index so that removal stays O(1) without asking a possibly-dead guard to renumber
        /// itself; see PyGuard.Entry.
        /// </summary>
        internal sealed class LiveEntry
        {
            internal int Index;
            internal WeakReference<PyGuard> Ref;
        }

        private static readonly List<LiveEntry> Live = new();

        private static long _sequence;
        private static bool drainRunning;   // no recursive pending calls (Python/ceval_gil.c:898)
        private static bool running;        // shutdown runs once
        private static bool shutdownDone;   // after this, nothing is queued any more

        /// <summary>
        /// Finalizer thread. Enqueue and set the flag - nothing else may happen here. Calling
        /// Python, or anything that can reach Python such as ToRepr, runs on a thread where an
        /// exception terminates the process.
        /// </summary>
        internal static void FromFinalizerThread(PyGuard guard)
        {
            if (shutdownDone) return;
            Queue.Enqueue(guard);
            PyPending.Flag = true;
        }

        /// <summary>
        /// The guard for an object, created on first use. Idempotent, so more than one creation
        /// path may call it.
        /// </summary>
        internal static PyGuard Attach(PyObject obj)
        {
            if (obj == null) return null;
            if (Guards.TryGetValue(obj, out var existing))
            {
                // A weak reference got here first; the finalizer is owed all the same.
                if (existing.State == GuardState.Watched)
                {
                    existing.State = GuardState.Armed;
                    AddToLive(existing);
                }
                return existing;
            }
            var guard = new PyGuard(obj);
            guard.Sequence = Interlocked.Increment(ref _sequence);
            Guards.Add(obj, guard);
            AddToLive(guard);
            return guard;
        }

        /// <summary>
        /// Stop watching an object that finished cleanly (a generator that ran out, Q3). The guard
        /// is dropped from the list and its owner released, so neither a finalizer nor a weakref
        /// callback can be owed any more.
        /// </summary>
        internal static void Detach(PyGuard guard)
        {
            if (guard == null) return;
            RemoveFromLive(guard);
            guard.FinalizerRan = true;
            // Nothing is owed to close() any more, but a weak reference still is.
            if (guard.WeakRefs != null)
            {
                guard.State = GuardState.Finalized;
                return;
            }
            guard.State = GuardState.Cleared;
            guard.Owner = null;
            guard.SetOuter(null);
            GC.SuppressFinalize(guard);
        }

        /// <summary>
        /// The guard a weak reference lives on. An object with no finalizer owes nothing, so its guard
        /// stays out of Live, but it starts Watched and needs two reports, like a guard with a
        /// __del__. The finalizer thread queues a parent and its child one at a time, so a safe point
        /// can take the child alone and clear its references before the parent's __del__ reads them.
        /// At the second report that cannot happen: a parent still in the queue holds the child.
        /// A Cleared guard can only belong to a generator that ran out; it gets a fresh one, because
        /// the old one was taken off the finalizer queue and would never report this reference.
        /// </summary>
        private static PyGuard WeakGuard(PyObject obj)
        {
            if (Guards.TryGetValue(obj, out var existing))
            {
                if (existing.State != GuardState.Cleared) return existing;
                Guards.Remove(obj);
            }
            var guard = new PyGuard(obj) { State = GuardState.Watched };
            guard.Sequence = Interlocked.Increment(ref _sequence);
            Guards.Add(obj, guard);
            return guard;
        }

        /// <summary>The cell a new reference to this object shares.</summary>
        internal static PyWeakCell WeakCell(PyObject target)
        {
            var guard = WeakGuard(target);
            lock (guard) return guard.Cell ??= new PyWeakCell(target);
        }

        /// <summary>Links a new reference into the target's list (Objects/weakrefobject.c insert_head).</summary>
        internal static void TrackWeakReference(PyObject target, PyWeakRef reference)
        {
            var guard = WeakGuard(target);
            lock (guard)
            {
                var handles = guard.WeakRefs ??= new List<GCHandle>();
                // A long-lived target that keeps getting short-lived references must not grow forever.
                if (handles.Count >= 16 && (handles.Count & (handles.Count - 1)) == 0)
                    FreeDeadHandles(handles);
                handles.Insert(InsertionIndex(handles, reference), GCHandle.Alloc(reference, GCHandleType.WeakTrackResurrection));
            }
        }

        // The basic reference and the basic proxy (no callback, the exact types), which the list keeps
        // at its head in that order: get_basic_refs.
        private static bool IsBasicRef(PyWeakRef r) => r is not PyWeakProxy && r.Callback == null && r.IsExactly(PyType.WeakRefType);
        private static bool IsBasicProxy(PyWeakRef r) => r is PyWeakProxy && r.Callback == null;

        // weakref_new and PyWeakref_NewProxy: a basic reference goes first (insert_head), a basic
        // proxy after the basic reference, and any other after the basic proxy, else after the basic
        // reference, else first (insert_after(self, prev)).
        private static int InsertionIndex(List<GCHandle> handles, PyWeakRef reference)
        {
            int refAt = -1, proxyAt = -1;
            for (int i = 0; i < handles.Count && i < 2; i++)
            {
                if (handles[i].Target is not PyWeakRef existing) continue;
                if (IsBasicRef(existing)) refAt = i;
                else if (IsBasicProxy(existing)) proxyAt = i;
            }
            if (IsBasicRef(reference)) return 0;
            if (IsBasicProxy(reference)) return refAt + 1;
            return (proxyAt >= 0 ? proxyAt : refAt) + 1;
        }

        private static void FreeDeadHandles(List<GCHandle> handles)
        {
            for (int i = handles.Count - 1; i >= 0; i--)
            {
                if (handles[i].Target != null) continue;
                handles[i].Free();
                handles.RemoveAt(i);
            }
        }

        /// <summary>The live references to an object, oldest first.</summary>
        internal static List<PyWeakRef> WeakReferencesOf(PyObject obj)
        {
            var alive = new List<PyWeakRef>();
            if (obj == null || !Guards.TryGetValue(obj, out var guard)) return alive;
            lock (guard)
            {
                if (guard.WeakRefs == null) return alive;
                foreach (var handle in guard.WeakRefs)
                    if (handle.Target is PyWeakRef reference) alive.Add(reference);
            }
            return alive;
        }

        /// <summary>
        /// PyObject_ClearWeakRefs (Objects/weakrefobject.c:938): every reference goes dead first, so a
        /// callback that looks at any of them sees None, then callbacks run in the list's order. A reference
        /// whose handle went null died before its target and is owed nothing.
        /// </summary>
        private static void ClearWeakRefs(PyGuard guard)
        {
            List<GCHandle> handles;
            lock (guard)
            {
                handles = guard.WeakRefs;
                guard.WeakRefs = null;
                guard.Cell?.Release();
                guard.Cell = null;
            }
            if (handles == null) return;
            var references = new List<PyWeakRef>(handles.Count);
            foreach (var handle in handles)
            {
                if (handle.Target is PyWeakRef reference) references.Add(reference);
                handle.Free();
            }
            // In the list's order, which weakref_new and PyWeakref_NewProxy keep as CPython's.
            for (int i = 0; i < references.Count; i++)
            {
                var callback = references[i].Callback;
                if (callback == null || IsCondemned(references[i])) continue;
                var saved = PyVM.SnapshotExceptionStates();
                try
                {
                    callback.Call(new PyObject[] { references[i] }, null);
                }
                catch (PythonException ex)
                {
                    PyUnraisable.Report(ex, callback);
                }
                catch (Exception ex)
                {
                    PyUnraisable.Report(new PythonException(PyVM.ConvertToPythonException(ex)), callback);
                }
                finally
                {
                    PyVM.RestoreExceptionStates(saved);
                }
            }
        }

        /// <summary>
        /// A reference that has weak references of its own has a guard, and the guard brings it through
        /// finalization, so the handle above still finds it. Unreachable together with its target,
        /// it counts as having died first: in C it would be gone from the list.
        /// </summary>
        private static bool IsCondemned(PyWeakRef reference)
            => Guards.TryGetValue(reference, out var guard) && guard.Cell != null
               && guard.Cell.Short.Target == null;

        /// <summary>
        /// A reference saw its target condemned by a collection whose report may not have arrived.
        /// Waiting for the finalizer thread makes that collection's guards visible to Drain, so the
        /// answer the reference gives next agrees with what happens to the object. During a __del__
        /// Drain is a no-op, which keeps a parent's view of its child.
        /// </summary>
        internal static void SettleCondemned()
        {
            GC.WaitForPendingFinalizers();
            Drain();
        }

        /// <summary>
        /// Called for every object a creation path produced. Only objects whose own type declares
        /// __del__ are filed, so an ordinary instance pays one type test and one cached field read.
        /// The decision is made on the object's REAL type: a __new__ that returns something else
        /// must not get the called class' finalizer (probes P and H).
        /// </summary>
        internal static void RegisterIfDeclared(PyObject created)
        {
            if (created == null || shutdownDone) return;
            if (!DeclaresFinalizerOn(created)) return;
            Attach(created);
        }

        private static bool DeclaresFinalizerOn(PyObject obj)
            => obj.GetPyType() is PyClass cls && cls.DeclaresFinalizer();

        /// <summary>gc.is_finalized - CPython answers False for anything never finalized.</summary>
        internal static bool IsFinalized(PyObject obj)
        {
            if (obj == null) return false;
            return Guards.TryGetValue(obj, out var guard) && guard.FinalizerRan;
        }

        /// <summary>
        /// Safe point: run what the collector reported. CPython runs pending calls from one caller
        /// at a time and does not recurse (Python/ceval_gil.c:898-903), so a finalizer that reaches
        /// another safe point drains nothing and returns 0.
        /// The flag goes false BEFORE the queue is emptied: a __del__ that creates and drops another
        /// finalizable object sets it again, and the next safe point takes that one.
        /// </summary>
        internal static int Drain()
        {
            if (drainRunning) return 0;
            drainRunning = true;
            int handled = 0;
            try
            {
                PyPending.Flag = false;
                handled = DrainQueue();
            }
            finally
            {
                drainRunning = false;
            }
            return handled;
        }

        /// <summary>
        /// Finalizers run before any reference is cleared. A collection reports a parent and its child
        /// together in no useful order, and in C the child outlives the parent's __del__, so a
        /// __del__ may read a child's reference or even keep the child by resurrecting itself. Only
        /// the next collection can tell, so guards at the Finalized step wait for it whenever a
        /// __del__ ran in this batch: re-registered, they report again only if still unreachable.
        /// A separate method because a guard left in a local slot lives one collection longer
        /// (q4_weakref_state.md section 1).
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int DrainQueue()
        {
            int handled = 0;
            bool finalizerRan = false;
            List<PyGuard> releases = null, armed = null;
            while (Queue.TryDequeue(out var guard))
            {
                handled++;
                if (guard.State == GuardState.Armed)
                {
                    finalizerRan = true;
                    (armed ??= new List<PyGuard>()).Add(guard);
                }
                else if (guard.State == GuardState.Watched)
                {
                    Step(guard);
                }
                else
                {
                    (releases ??= new List<PyGuard>()).Add(guard);
                }
            }
            if (armed != null)
            {
                // The CLR finalizer thread hands guards over in no particular order. Oldest first:
                // an object made earlier and dropped by the same point would have been finalized
                // earlier by refcounting.
                armed.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
                StepOuterFirst(armed, Step);
            }
            if (releases == null) return handled;
            foreach (var guard in releases)
            {
                if (finalizerRan) GC.ReRegisterForFinalize(guard);
                else Step(guard);
            }
            return handled;
        }

        /// <summary>
        /// One guard, one step. State changes first, because __del__ can reach a safe point and come
        /// back through here; checking after calling would run a finalizer twice.
        ///
        /// Armed    -> run __del__, then re-arm the guard so the object's second death is noticed.
        /// Finalized -> clear the weak references and release the owner.
        /// A generator goes the other way round: gen_dealloc clears weak references before it
        /// finalizes (Objects/genobject.c:126), so its callback runs before its finally.
        /// </summary>
        internal static void Step(PyGuard guard)
        {
            if (guard == null) return;
            switch (guard.State)
            {
                case GuardState.Armed:
                    guard.State = GuardState.Finalized;
                    guard.FinalizerRan = true;
                    RemoveFromLive(guard);
                    if (guard.Owner is PyGenerator) ClearWeakRefs(guard);
                    RunFinalizer(guard);
                    GC.ReRegisterForFinalize(guard);
                    break;
                case GuardState.Watched:
                    guard.State = GuardState.Finalized;
                    GC.ReRegisterForFinalize(guard);
                    break;
                case GuardState.Finalized:
                    guard.State = GuardState.Cleared;
                    ClearWeakRefs(guard);
                    guard.Owner = null;
                    guard.SetOuter(null);
                    break;
            }
        }

        /// <summary>
        /// _PyGen_Finalize (Objects/genobject.c:70), the generator's tp_finalize and so its
        /// __del__: a coroutine that never ran says so, a suspended one is closed; what that raises
        /// is reported as unraisable, naming the generator.
        /// </summary>
        internal static void FinalizeGenerator(PyObject target)
        {
            var saved = PyVM.SnapshotExceptionStates();
            // The object the error names is the one that owns the frame (a coroutine or async
            // generator around a generator core), as _PyGen_Finalize names the gen object itself.
            var named = (target as PyGenerator)?.Frame?.OwnerGenerator ?? target;
            try
            {
                if (target is SharpPy.Core.PyCoroutine coroutine) coroutine.Finalize();
                else if (target is SharpPy.Core.PyAsyncGenerator asyncGenerator) asyncGenerator.Finalize();
                // The frame of an async generator runs in a generator; the decision is the async
                // generator's.
                else if (((PyGenerator)target).Frame?.OwnerGenerator is SharpPy.Core.PyAsyncGenerator owner) owner.Finalize();
                else if (((PyGenerator)target).Frame?.OwnerGenerator is SharpPy.Core.PyCoroutine coroutineOwner) coroutineOwner.Finalize();
                else ((PyGenerator)target).Close();
            }
            catch (PythonException ex)
            {
                PyUnraisable.Report(ex, named);
            }
            catch (Exception ex)
            {
                PyUnraisable.Report(new PythonException(PyVM.ConvertToPythonException(ex)), named);
            }
            finally
            {
                PyVM.RestoreExceptionStates(saved);
            }
        }

        private static void RunFinalizer(PyGuard guard)
        {
            var target = guard.Owner;
            if (target == null) return;

            // A suspended generator has a different debt: _PyGen_Finalize (Objects/genobject.c:70)
            // closes it, which is what runs the try/finally it is sitting in.
            if (target is SharpPy.Core.PyCoroutine or PyGenerator)
            {
                FinalizeGenerator(target);
                return;
            }

            var hook = LookupHook(target);
            if (hook == null) return;

            // slot_tp_finalize saves the exception in flight (Objects/typeobject.c:9076) and puts it
            // back at :9090, so a finalizer that raises - or that raises and catches - leaves the
            // handler that was running untouched. In SharpPy the in-flight exception lives on the
            // frames, so the frames that hold one are restored by hand.
            var saved = PyVM.SnapshotExceptionStates();
            try
            {
                // slot_tp_finalize does not call the value of the class dict. It looks the name up with
                // lookup_maybe_method (Objects/typeobject.c:9071, :2188), so the descriptor protocol runs
                // and then the result is called with no arguments (measured on the reference interpreter:
                // a plain def, a staticmethod, a classmethod, a custom __get__ and a bare callable object
                // all receive zero arguments). Calling the dict value directly passed self and skipped
                // binding, which broke every form except the plain def.
                // BindSpecialMethod (core/PyObject.cs:744) is the existing binder, so no second binding
                // path is added here. A hook that is not callable - __del__ = None - raises from here and
                // is reported below, which is also what CPython does.
                target.BindSpecialMethod(hook).Call(Array.Empty<PyObject>(), null);
            }
            catch (PythonException ex)
            {
                PyUnraisable.Report(ex, hook);
            }
            catch (Exception ex)
            {
                PyUnraisable.Report(new PythonException(PyVM.ConvertToPythonException(ex)), hook);
            }
            finally
            {
                PyVM.RestoreExceptionStates(saved);
            }
        }

        /// <summary>
        /// The hook is looked up on the object's own type through the MRO, which is what
        /// slot_tp_finalize does: a subclass override wins, an inherited hook is found, and a
        /// __new__ that returned a foreign object gets that object's hook (none), not the called
        /// class'.
        /// </summary>
        private static PyObject LookupHook(PyObject target)
        {
            var type = target.GetPyType();
            if (type is PyClass cls) return cls.LookupFinalizerHook();
            // A native type's tp_finalize, exposed as its __del__ and inherited (iobase_finalize).
            foreach (var t in type.MRO)
                if (t.TypeDict.TryGetValue("__del__", out var hook))
                    return hook;
            return null;
        }

        /// <summary>
        /// outer is a layer over inner: a buffered stream over its raw stream, a text stream over its
        /// buffer. In CPython outer's reference keeps inner alive until outer is finalized, so a
        /// collection that finds both at once finalizes outer first, and so does shutdown.
        /// </summary>
        internal static void FinalizeAfter(PyObject inner, PyObject outer)
        {
            // A null outer ends the edge: detach() hands the inner layer back.
            if (inner == null || !Guards.TryGetValue(inner, out var innerGuard)) return;
            innerGuard.SetOuter(outer != null && Guards.TryGetValue(outer, out var outerGuard) ? outerGuard : null);
        }

        // Steps the armed guards, each after the armed layers over it. An outer layer holds its inner
        // one, so when the inner is unreachable the outer is too, even if the finalizer thread has
        // not queued it yet; it is finalized first all the same.
        private static void StepOuterFirst(List<PyGuard> armed, Action<PyGuard> step)
        {
            var done = new HashSet<PyGuard>();
            void Run(PyGuard guard, int depth)
            {
                if (!done.Add(guard)) return;
                if (depth < 16 && guard.OuterGuard is { State: GuardState.Armed } outer)
                    Run(outer, depth + 1);
                step(guard);
            }
            foreach (var guard in armed)
                Run(guard, 0);
        }

        /// <summary>
        /// gc.collect()'s anchor. A real collection is what turns "unreachable" into a queued guard,
        /// so one pass may not be enough: a finalizer can drop more objects, and those need another
        /// round. Bounded, like CPython's collect which also works in bounded rounds.
        /// </summary>
        internal static int CollectAndDrain()
        {
            int total = 0;
            for (int round = 0; round < 10; round++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                int handled = Drain();
                total += handled;
                if (handled == 0) break;
            }
            return total;
        }

        /// <summary>
        /// The one teardown entry point, so a new exit path cannot forget a step. atexit runs first:
        /// CPython reaches _PyAtExit_Call (Python/pylifecycle.c:1845) before it finalizes modules, so a
        /// callback still has a working interpreter, and an object's __del__ arrives afterwards - the
        /// last line of probes/l2_atexit_reference_error.py is "__del__ after atexit".
        /// </summary>
        internal static void Shutdown()
        {
            // wait_for_thread_shutdown: threading joins what is not a daemon.
            if (PyImportSystem.TryGetModule("threading", out var threading))
            {
                try
                {
                    threading.GetAttribute("_shutdown").Call(Array.Empty<PyObject>(), null);
                }
                catch (PythonException error)
                {
                    PyUnraisable.Report(error, threading);
                }
            }
            try { Modules.AtexitModule.RunCallbacks(); }
            catch { /* a teardown step that fails must not stop the next one */ }
            // Py_FinalizeEx: flush_std_files before the interpreter is torn down, and again after.
            Modules.SysModule.FlushStdFiles();
            PyGil.BeginFinalizing();
            RunAtShutdown();
            Modules.SysModule.FlushStdFiles();
        }

        /// <summary>
        /// Interpreter shutdown. CPython tears modules down and collects twice
        /// (Python/pylifecycle.c:1573 finalize_modules, :1609 and :1649 _PyGC_CollectNoFail,
        /// :1630 finalize_modules_clear_weaklist), so everything still alive loses its last
        /// reference before the process ends. Order is measured, not assumed
        /// (docs/plans/lifetime_foundation/probes/q2_shutdown_order.py): module globals in
        /// insertion order first, then the rest in creation order.
        /// </summary>
        internal static void RunAtShutdown()
        {
            if (running) return;
            running = true;
            try
            {
                // 1. Whatever the collector already reported.
                Drain();

                for (int round = 0; round < 5; round++)
                {
                    int before = handled;
                    // 2. __main__'s namespace, in insertion order. An embedded host has no
                    //    __main__ and simply skips this step.
                    if (PyImportSystem.TryGetModule("__main__", out var main))
                        StepNamespaces(main);
                    // 3. Everything still being watched, in creation order.
                    StepRemaining();
                    // 4. A __del__ may have created more finalizable objects.
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    Drain();
                    if (handled == before) break;
                }

                // Finalizers first, external resources second: a __del__ may still write to a file it
                // kept open. CPython likewise flushes the standard files last
                // (Python/pylifecycle.c:1673 flush_std_files).
                PyResourceBus.ReleaseAllAtShutdown();
                shutdownDone = true;
            }
            finally
            {
                running = false;
            }
        }

        private static int handled;

        private static void StepNamespaces(PyModule main)
        {
            List<PyObject> values = null;
            try
            {
                // ModuleDict is append-only for the names a script binds, so the enumerator walks
                // them in the order the module assigned them - which is the order CPython's teardown
                // was measured to follow (probes q2_shutdown_order).
                foreach (var entry in main.ModuleDict)
                    (values ??= new List<PyObject>()).Add(entry.Value);
            }
            catch
            {
                // A module dict that cannot be enumerated is not a reason to stop finalizing.
            }
            if (values == null) return;
            foreach (var value in values)
            {
                if (value == null) continue;
                if (!Guards.TryGetValue(value, out var guard)) continue;
                if (guard.State != GuardState.Armed) continue;
                Step(guard);
                handled++;
            }
        }

        private static void StepRemaining()
        {
            PyGuard[] snapshot = SnapshotLive();
            Array.Sort(snapshot, (a, b) => a.Sequence.CompareTo(b.Sequence));
            var armed = new List<PyGuard>();
            foreach (var guard in snapshot)
                if (guard.State == GuardState.Armed)
                    armed.Add(guard);
            StepOuterFirst(armed, guard =>
            {
                if (guard.State != GuardState.Armed) return;
                Step(guard);
                handled++;
            });
        }

        private static PyGuard[] SnapshotLive()
        {
            List<PyGuard> alive = null;
            lock (Live)
            {
                // Dead rows are not pruned here. Removing a row from the middle would shift every row
                // after it and leave their stored indexes pointing somewhere else, which is the bug this
                // change removes. The guard's own Step drops its row, and this snapshot is only taken
                // once, at shutdown.
                for (int i = Live.Count - 1; i >= 0; i--)
                {
                    var guard = Target(Live[i].Ref);
                    if (guard != null) (alive ??= new List<PyGuard>()).Add(guard);
                }
            }
            return alive == null ? Array.Empty<PyGuard>() : alive.ToArray();
        }

        // Resolving a weak reference leaves the target in the caller's local slot until the method
        // ends, which can keep something alive that the same collection was meant to free. The
        // tier-0 JIT does not narrow those slots; the spike reproduced it. Keeping the read in a
        // non-inlined helper keeps the lifetime of the hidden slot inside this method.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static PyGuard Target(WeakReference<PyGuard> reference)
        {
            if (reference == null) return null;
            return reference.TryGetTarget(out var guard) ? guard : null;
        }

        private static void AddToLive(PyGuard guard)
        {
            lock (Live)
            {
                guard.Entry = new LiveEntry
                {
                    Index = Live.Count,
                    Ref = new WeakReference<PyGuard>(guard),
                };
                Live.Add(guard.Entry);
            }
        }

        // O(1) removal. Sequence still orders shutdown, so the list does not have to stay sorted.
        private static void RemoveFromLive(PyGuard guard)
        {
            if (guard == null) return;
            lock (Live)
            {
                var entry = guard.Entry;
                if (entry == null) return;
                int slot = entry.Index;
                int last = Live.Count - 1;
                if (slot < 0 || slot > last || !ReferenceEquals(Live[slot], entry))
                {
                    // Not our row any more; nothing to take out twice.
                    guard.Entry = null;
                    return;
                }
                if (slot != last)
                {
                    Live[slot] = Live[last];
                    Live[slot].Index = slot;
                }
                Live.RemoveAt(last);
                guard.Entry = null;
            }
        }

        /// <summary>
        /// Measured shape on stderr, exit status unchanged:
        ///   Exception ignored in: &lt;function B.__del__ at 0x...&gt;
        ///   Traceback (most recent call last):
        ///   ValueError: boom in del
        /// </summary>
    }
}
