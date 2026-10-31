using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace SharpPy
{
    internal enum GuardState { Armed, Finalized, Cleared }

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
        /// <summary>Index into PyFinalizer.Live, or -1 when the guard is not listed.</summary>
        internal int Slot = -1;

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
        private static readonly List<WeakReference<PyGuard>> Live = new();

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
            if (Guards.TryGetValue(obj, out var existing)) return existing;
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
            guard.State = GuardState.Cleared;
            guard.Owner = null;
            GC.SuppressFinalize(guard);
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
            return Guards.TryGetValue(obj, out var guard) && guard.State != GuardState.Armed;
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
                while (Queue.TryDequeue(out var guard))
                {
                    Step(guard);
                    handled++;
                }
            }
            finally
            {
                drainRunning = false;
            }
            return handled;
        }

        /// <summary>
        /// One guard, one step. State changes first, because __del__ can reach a safe point and come
        /// back through here; checking after calling would run a finalizer twice.
        ///
        /// Armed    -> run __del__, then re-arm the guard so the object's second death is noticed.
        /// Finalized -> release the owner. This is the phase that also fires weakref callbacks in Q4.
        /// </summary>
        internal static void Step(PyGuard guard)
        {
            if (guard == null) return;
            switch (guard.State)
            {
                case GuardState.Armed:
                    guard.State = GuardState.Finalized;
                    RemoveFromLive(guard);
                    RunFinalizer(guard);
                    GC.ReRegisterForFinalize(guard);
                    break;
                case GuardState.Finalized:
                    guard.State = GuardState.Cleared;
                    guard.Owner = null;
                    break;
            }
        }

        private static void RunFinalizer(PyGuard guard)
        {
            var target = guard.Owner;
            if (target == null) return;
            var hook = LookupHook(target);
            if (hook == null) return;

            // slot_tp_finalize saves the exception in flight (Objects/typeobject.c:9076) and puts it
            // back at :9090, so a finalizer that raises - or that raises and catches - leaves the
            // handler that was running untouched. In SharpPy the in-flight exception lives on the
            // frames, so the frames that hold one are restored by hand.
            var saved = PyVM.SnapshotExceptionStates();
            try
            {
                hook.Call(new PyObject[] { target }, null);
            }
            catch (PythonException ex)
            {
                ReportIgnored(hook, ex);
            }
            catch (Exception ex)
            {
                ReportIgnored(hook, null, ex.Message);
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
            if (!(target.GetPyType() is PyClass cls)) return null;
            return cls.LookupFinalizerHook();
        }

        /// <summary>
        /// gc.collect()'s anchor. A real collection is what turns "unreachable" into a queued guard,
        /// so one pass may not be enough: a finalizer can drop more objects, and those need another
        /// round. Bounded, like CPython's collect which also works in bounded rounds.
        /// </summary>
        internal static void CollectAndDrain()
        {
            for (int round = 0; round < 10; round++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                if (Drain() == 0) break;
            }
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
            foreach (var guard in snapshot)
            {
                if (guard.State == GuardState.Armed)
                {
                    Step(guard);
                    handled++;
                }
            }
        }

        private static PyGuard[] SnapshotLive()
        {
            List<PyGuard> alive = null;
            lock (Live)
            {
                for (int i = Live.Count - 1; i >= 0; i--)
                {
                    var guard = Target(Live[i]);
                    if (guard != null) (alive ??= new List<PyGuard>()).Add(guard);
                    else Live.RemoveAt(i);
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
                guard.Slot = Live.Count;
                Live.Add(new WeakReference<PyGuard>(guard));
            }
        }

        // O(1) removal. Sequence still orders shutdown, so the list does not have to stay sorted.
        private static void RemoveFromLive(PyGuard guard)
        {
            if (guard == null) return;
            lock (Live)
            {
                int slot = guard.Slot;
                if (slot < 0 || slot >= Live.Count) { guard.Slot = -1; return; }
                int last = Live.Count - 1;
                if (slot != last)
                {
                    Live[slot] = Live[last];
                    var moved = Target(Live[slot]);
                    if (moved != null) moved.Slot = slot;
                }
                Live.RemoveAt(last);
                guard.Slot = -1;
            }
        }

        /// <summary>
        /// Measured shape on stderr, exit status unchanged:
        ///   Exception ignored in: &lt;function B.__del__ at 0x...&gt;
        ///   Traceback (most recent call last):
        ///   ValueError: boom in del
        /// </summary>
        private static void ReportIgnored(PyObject hook, PythonException pythonError, string message = null)
        {
            try
            {
                string context;
                try { context = hook == null ? "__del__" : hook.ToRepr().Value; }
                catch { context = "__del__"; }
                Console.Error.WriteLine("Exception ignored in: " + context);
                Console.Error.WriteLine("Traceback (most recent call last):");
                if (pythonError != null)
                {
                    var value = pythonError.PyException;
                    Console.Error.WriteLine(value == null
                        ? pythonError.Message
                        : value.GetTypeName() + ": " + value.ToStr().Value);
                }
                else if (message != null)
                {
                    Console.Error.WriteLine(message);
                }
            }
            catch
            {
                // A failing report must not replace the program's own outcome.
            }
        }
    }
}
