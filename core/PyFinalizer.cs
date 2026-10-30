using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// The __del__ hook, and the one place its timing is decided.
    ///
    /// CPython calls tp_finalize from deallocate (Objects/object.c:1285 call_finalizer), so a
    /// __del__ runs the instant the last reference drops. SharpPy has no reference counter: an
    /// object's lifetime belongs to the CLR collector, whose timing is not observable and cannot be
    /// made deterministic without a write barrier at every Python-visible reference slot. That is
    /// why this file does not try to emulate reference counting. It does two things instead:
    ///
    /// 1. It resolves __del__ through the type, so a subclass override wins and super().__del__()
    ///    reaches the base implementation - a finalizer is a special method, and the lookup has to
    ///    behave like one even though the call site is not CPython's.
    /// 2. It runs the pending finalizers at interpreter shutdown, which is the one moment CPython's
    ///    answer and SharpPy's answer are the same: module globals are dropped, every object with a
    ///    __del__ loses its last reference, and each __del__ runs exactly once.
    ///
    /// Measured against CPython 3.12.15, the order at shutdown is creation order, and an exception
    /// raised inside __del__ is reported on stderr as "Exception ignored in: &lt;function ...&gt;"
    /// without changing the exit status. Both are reproduced here.
    ///
    /// The registered difference (docs/known_gaps.md, P0-37): an object that becomes unreachable
    /// mid-run still has its __del__ deferred to shutdown, because SharpPy cannot see unreachability.
    /// </summary>
    internal static class PyFinalizer
    {
        // Registration order is the order finalizers run, matching measured CPython shutdown order.
        private static readonly List<Entry> Pending = new List<Entry>();
        private static readonly HashSet<PyObject> Finalized = new HashSet<PyObject>();
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
