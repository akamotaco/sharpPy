using System;
using System.Collections.Generic;

namespace SharpPy.Modules
{
    /// <summary>
    /// <c>atexit</c>. CPython implements this in C - Modules/atexitmodule.c - so there is no
    /// Lib/atexit.py to copy, and Lib/weakref.py:36 registers its reaper through this module.
    ///
    /// The shape of the registry is copied from the source rather than invented: one dense list whose
    /// slots are nulled on unregister (atexit_delete_cb, Modules/atexitmodule.c:53), and whose length is
    /// the high-water mark, not the number of live callbacks. That is why unregister leaves
    /// _ncallbacks() where it was (measured: atexit._ncallbacks() == 5 after two callbacks were removed).
    /// </summary>
    internal static class AtexitModule
    {
        private sealed class Callback
        {
            internal PyObject Func;
            internal PyObject[] Args;
            internal PyDict Kwargs;
        }

        // Slot i of Modules/atexitmodule.c state->callbacks. A null slot is a deleted callback that is
        // skipped, not a hole to be closed.
        private static readonly List<Callback> Callbacks = new();

        internal static PyModule CreateAtexitModule()
        {
            var module = new PyModule("atexit", "<atexit>");

            // register(func, /, *args, **kwargs) -> func. The return value is not a detail: it is what
            // makes @atexit.register work (atexit_register, Modules/atexitmodule.c:177, and the doc
            // string at :165 "func is returned to facilitate usage as a decorator").
            module.ModuleDict["register"] = new PyBuiltinFunction("register", (args, kwargs) =>
            {
                if (args.Length == 0)
                    throw PyTypeError.Create("register() takes at least 1 argument (0 given)"); // :181
                var func = args[0];
                if (func == null || !func.IsCallable())
                    throw PyTypeError.Create("the first argument must be callable"); // :188
                var rest = new PyObject[args.Length - 1];
                Array.Copy(args, 1, rest, 0, rest.Length);
                lock (Callbacks)
                {
                    Callbacks.Add(new Callback { Func = func, Args = rest, Kwargs = kwargs });
                }
                return func;
            });

            // unregister(func) -> None. Compares with == and removes every match; a func that was never
            // registered is not an error (atexit_unregister, Modules/atexitmodule.c:270, with
            // PyObject_RichCompareBool at :280 and Py_RETURN_NONE at :289). Measured: the call answers
            // None and _ncallbacks() does not move.
            module.ModuleDict["unregister"] = new PyBuiltinFunction("unregister", (args, kwargs) =>
            {
                if (args.Length != 1)
                    throw PyTypeError.Create($"unregister() takes exactly one argument ({args.Length} given)");
                var func = args[0];
                lock (Callbacks)
                {
                    for (int i = 0; i < Callbacks.Count; i++)
                    {
                        var entry = Callbacks[i];
                        if (entry == null) continue;
                        if (Equals(entry.Func, func)) Callbacks[i] = null;
                    }
                }
                return PyNone.Instance;
            });

            module.ModuleDict["_clear"] = new PyBuiltinFunction("_clear", (args, kwargs) =>
            {
                // atexit_cleanup, Modules/atexitmodule.c:67 - drop without calling.
                lock (Callbacks) Callbacks.Clear();
                return PyNone.Instance;
            });

            module.ModuleDict["_ncallbacks"] = new PyBuiltinFunction("_ncallbacks", (args, kwargs) =>
            {
                lock (Callbacks) return new PyInt(Callbacks.Count); // :255
            });

            module.ModuleDict["_run_exitfuncs"] = new PyBuiltinFunction("_run_exitfuncs", (args, kwargs) =>
            {
                RunCallbacks();
                return PyNone.Instance;
            });

            return module;
        }

        /// <summary>
        /// == on the registered function, which is what PyObject_RichCompareBool does: a comparison that
        /// returns NotImplemented falls back to identity, and a comparison that raises is reported
        /// instead of silently deciding.
        /// </summary>
        private static bool Equals(PyObject left, PyObject right)
        {
            if (ReferenceEquals(left, right)) return true;
            var result = left.RichCompare(right, PyObject.CompareOp.EQ);
            if (ReferenceEquals(result, PyNotImplemented.Instance)) return false;
            if (result is PyBool flag) return flag.Value;
            throw PyTypeError.Create(
                $"__eq__ returned non-bool (type {result.GetTypeName()})");
        }

        /// <summary>
        /// Interpreter shutdown, and atexit._run_exitfuncs(). CPython calls this from
        /// Python/pylifecycle.c:1845 (_PyAtExit_Call) before the interpreter finalizes modules, which
        /// is why an atexit callback can still use Python and why an object's __del__ runs afterwards.
        ///
        /// Newest first, and the range is fixed on entry (atexit_callfuncs,
        /// Modules/atexitmodule.c:121: for i = state->ncallbacks - 1 down to 0), so a callback that
        /// registers another one during the run is not called in that run - measured, the "registered
        /// during exit" line never appears. A callback that raises is reported and the rest still run
        /// (_PyErr_WriteUnraisableMsg("in atexit callback", func) at :138).
        /// </summary>
        internal static void RunCallbacks()
        {
            int count;
            lock (Callbacks) count = Callbacks.Count;
            for (int i = count - 1; i >= 0; i--)
            {
                Callback entry;
                lock (Callbacks)
                {
                    if (i >= Callbacks.Count) continue;
                    entry = Callbacks[i];
                    // The slot is released before the call: the callback may unregister itself
                    // (Modules/atexitmodule.c:134 holds a reference to func for exactly that reason).
                    Callbacks[i] = null;
                }
                if (entry == null) continue;
                try
                {
                    entry.Func.Call(entry.Args, entry.Kwargs);
                }
                catch (PythonException ex)
                {
                    ReportIgnored(entry.Func, ex);
                }
                catch (Exception ex)
                {
                    ReportIgnored(entry.Func, null, ex.Message);
                }
            }
            lock (Callbacks) Callbacks.Clear();
        }

        /// <summary>
        /// The measured shape on stderr; stdout and the exit status are untouched:
        ///   Exception ignored in atexit callback: &lt;function raising at 0x...&gt;
        ///   Traceback (most recent call last):
        ///   ValueError: from atexit
        /// </summary>
        private static void ReportIgnored(PyObject func, PythonException pythonError, string message = null)
        {
            try
            {
                string context;
                try { context = func.ToRepr().Value; }
                catch { context = func.GetTypeName(); }
                Console.Error.WriteLine("Exception ignored in atexit callback: " + context);
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
                // A failing report must not replace the program's outcome.
            }
        }
    }
}
