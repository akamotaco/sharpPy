using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// The __del__ hook, and the one place its timing is decided.
    /// POLICY: docs/lifetime_and_gc_policy.md (Korean) - lifetime belongs to the CLR collector,
    /// release is explicit, and only the *moment of unreachability* is an accepted divergence.
    ///
    /// CPython's mechanism, in reading order:
    ///   Objects/typeobject.c:9499  TPSLOT(__del__, tp_finalize, slot_tp_finalize, ...) maps the
    ///                              Python-level method onto a C slot at type-creation time.
    ///   Objects/typeobject.c:9071  slot_tp_finalize looks the method up on the type, which is why
    ///                              a subclass override wins and super().__del__() reaches the base.
    ///   Objects/object.c:357       PyObject_CallFinalizer runs it, with the comment at :362
    ///                              "tp_finalize should only be called once", a _PyGC_FINALIZED flag
    ///                              test, and _PyGC_SET_FINALIZED afterwards.
    ///   Objects/object.c:374       PyObject_CallFinalizerFromDealloc handles resurrection; :398
    ///                              "tp_finalize resurrected it!".
    ///   Objects/typeobject.c:1901  subtype_dealloc: "Maybe call finalizer; exit early if resurrected".
    /// The point of the chain: in CPython the *time* of the call is a function of the reference
    /// count, and that is the only part SharpPy cannot reproduce.
    ///
    /// SharpPy has no reference counter, and adding one (a write barrier at every Python-visible
    /// reference slot) is rejected in the policy document. It does two things instead:
    ///
    /// 1. It resolves __del__ through the type, exactly as slot_tp_finalize does, so a subclass
    ///    override wins and super().__del__() reaches the base implementation. This half matches
    ///    CPython behaviourally and is pinned by tests/bughunt/p76_gc_and_finalizers.py.
    /// 2. It runs the pending finalizers at interpreter shutdown. That is the one moment the two
    ///    answers coincide: CPython's own shutdown collects twice and clears weakref lists
    ///    (Python/pylifecycle.c:1573 finalize_modules, :1609 and :1649 _PyGC_CollectNoFail,
    ///    :1630 finalize_modules_clear_weaklist), so every remaining object with a __del__ loses
    ///    its last reference and must be finalized before the process ends.
    ///
    /// Measured against CPython 3.12.15: the order at shutdown is creation order, and an exception
    /// raised inside __del__ is reported on stderr as "Exception ignored in: &lt;function ...&gt;"
    /// plus "Traceback (most recent call last):" and the error line, without changing the exit
    /// status. Both are reproduced here.
    ///
    /// DELIBERATE DIVERGENCE (docs/known_gaps.md, P0-37 remainder 1): an object that becomes
    /// unreachable mid-run keeps its __del__ deferred to shutdown. Making that immediate would
    /// require either reference counting (rejected) or a CLR finalizer, which is forbidden:
    /// a finalizer runs on its own thread where a Python exception terminates the process, the
    /// module globals and stdout may already be gone, Godot's native context may be destroyed, and
    /// pending finalizers may be skipped at exit. See policy section 4.
    ///
    /// NOT IMPLEMENTED, on purpose: resurrection. CPython re-runs __del__ if the finalizer revives
    /// the object, because it can see the reference count go back up (Objects/object.c:398).
    /// Here a finalized object stays finalized (policy: P0-37 remainder 6).
    /// </summary>
    internal static class PyFinalizer
    {
        // Registration order is the order finalizers run, matching measured CPython shutdown order.
        private static readonly List<Entry> Pending = new List<Entry>();

        // "Only once" per Objects/object.c:362. CPython implements it with a flag in the GC header
        // (_PyGC_FINALIZED / _PyGC_SET_FINALIZED): reference identity, never a hash lookup. The same
        // meaning needs a comparer here, because PyObject.GetHashCode returns the Python hash
        // (core/PyObject.cs:130) - hashing by it would let a user-defined __hash__ choose the bucket
        // and the cost of registering an instance, and any user code reachable from it would run from
        // inside the construction path. With the reference comparer Contains is a pointer comparison.
        private static readonly HashSet<PyObject> Finalized =
            new HashSet<PyObject>(ReferenceEqualityComparer.Instance);
        private static bool running;

        private struct Entry
        {
            internal PyClass Class;
            internal WeakReference<PyObject> Target;
        }

        /// <summary>
        /// Called for every instance produced by PyClass.CreateInstance. Only classes whose MRO
        /// declares __del__ are recorded, so ordinary objects pay nothing beyond a dictionary-free
        /// MRO lookup that CreateInstance already made for other reasons.
        /// </summary>
        // One instance of a plain class must not pay for an MRO walk, so the class remembers the
        // answer and the lookup runs once per class (PyClass._declaresFinalizer).
        internal static void RegisterIfDeclared(PyClass cls, PyObject instance)
        {
            if (cls == null || instance == null) return;
            if (!cls.DeclaresFinalizer()) return;
            if (Finalized.Contains(instance)) return;
            Pending.Add(new Entry { Class = cls, Target = new WeakReference<PyObject>(instance) });
        }

        /// <summary>gc.is_finalized - CPython answers False for anything never finalized.</summary>
        internal static bool IsFinalized(PyObject obj) => obj != null && Finalized.Contains(obj);

        /// <summary>
        /// Interpreter shutdown: run every pending __del__ that still has a live target.
        /// </summary>
        internal static void RunAtShutdown()
        {
            if (running) return;
            running = true;
            try
            {
                var sweep = Pending.ToArray();
                Pending.Clear();
                foreach (var entry in sweep)
                {
                    if (!entry.Target.TryGetTarget(out var target)) continue;
                    Invoke(entry.Class, target);
                }
                // Finalizers first, external resources second: a __del__ may still write to a file it
                // kept open. CPython's own shutdown likewise flushes the standard streams only at the
                // end (Python/pylifecycle.c:1673 flush_std_files, reached from :1896/:1925/:2952).
                PyResourceBus.ReleaseAllAtShutdown();
            }
            finally
            {
                running = false;
            }
        }

        /// <summary>
        /// One object, at most once. CPython re-runs __del__ only if the object resurrects itself,
        /// which needs a reference count to notice; here a finalized object stays finalized.
        /// </summary>
        private static void Invoke(PyClass cls, PyObject target)
        {
            if (!Finalized.Add(target)) return;
            PyObject hook = LookupHook(cls);
            if (hook == null) return;
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
        }

        /// <summary>
        /// The MRO lookup, so a subclass __del__ wins over a base one. An inherited hook has to be
        /// found here rather than by consulting only this class' dict: CPython stores tp_finalize
        /// on the subtype, and a subclass of a class with __del__ is finalized too.
        /// </summary>
        private static PyObject LookupHook(PyClass cls)
        {
            var hook = cls == null ? null : cls.LookupInMRO("__del__");
            if (hook == null || hook is PyNone) return null;
            return hook;
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
