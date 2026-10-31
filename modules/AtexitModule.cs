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
            // PyObject_RichCompareBool at :280 and Py_RETURN_NONE at :288). Measured: the call answers
            // None and _ncallbacks() does not move.
            module.ModuleDict["unregister"] = new PyBuiltinFunction("unregister", (args, kwargs) =>
            {
                // METH_O. Keywords are refused before the argument is counted, and CPython's parser
                // names the function by its qualified name (measured: probes/l2b_atexit_edges.py).
                if (kwargs != null && kwargs.Length() != 0)
                    throw PyTypeError.Create("atexit.unregister() takes no keyword arguments");
                if (args.Length != 1)
                    throw PyTypeError.Create($"atexit.unregister() takes exactly one argument ({args.Length} given)");
                var func = args[0];
                lock (Callbacks)
                {
                    for (int i = 0; i < Callbacks.Count; i++)
                    {
                        var entry = Callbacks[i];
                        if (entry == null) continue;
                        if (RichCompareBool(entry.Func, func)) Callbacks[i] = null;
                    }
                }
                return PyNone.Instance;
            });

            module.ModuleDict["_clear"] = new PyBuiltinFunction("_clear", (args, kwargs) =>
            {
                NoArguments("atexit._clear", args, kwargs);
                // atexit_cleanup, Modules/atexitmodule.c:67 - drop without calling.
                lock (Callbacks) Callbacks.Clear();
                return PyNone.Instance;
            });

            module.ModuleDict["_ncallbacks"] = new PyBuiltinFunction("_ncallbacks", (args, kwargs) =>
            {
                NoArguments("atexit._ncallbacks", args, kwargs);
                lock (Callbacks) return new PyInt(Callbacks.Count); // :255
            });

            module.ModuleDict["_run_exitfuncs"] = new PyBuiltinFunction("_run_exitfuncs", (args, kwargs) =>
            {
                NoArguments("atexit._run_exitfuncs", args, kwargs);
                RunCallbacks();
                return PyNone.Instance;
            });

            return module;
        }

        /// <summary>
        /// PyObject_RichCompareBool, the comparison atexit_unregister actually uses: identity first, then
        /// the VM's own == so both operands are resolved (a NotImplemented on the left reaches the right
        /// operand's __eq__), then the truth value of whatever came back - measured with __eq__ that
        /// returns 1, that returns [], and that raises. An __eq__ that raises is not swallowed here:
        /// unregister propagates it, and the callback stays registered.
        /// </summary>
        private static bool RichCompareBool(PyObject left, PyObject right)
        {
            if (ReferenceEquals(left, right)) return true;
            var result = PyObject.Compare(left, right, PyObject.CompareOp.EQ);
            return result != null && result.IsTrue();
        }

        /// <summary>
        /// METH_NOARGS, with the module-qualified name CPython puts in the message (measured:
        /// "atexit._clear() takes no arguments (1 given)"). Keywords are refused first, the way the
        /// parser does it before anything is counted.
        /// </summary>
        private static void NoArguments(string qualified, PyObject[] args, PyDict kwargs)
        {
            if (kwargs != null && kwargs.Length() != 0)
                throw PyTypeError.Create($"{qualified}() takes no keyword arguments");
            if (args.Length != 0)
                throw PyTypeError.Create($"{qualified}() takes no arguments ({args.Length} given)");
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
        /// (_PyErr_WriteUnraisableMsg("in atexit callback", func) at :139).
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
                    // The slot is read, not emptied: atexit_callfuncs (:129) leaves callbacks[i] alone
                    // until atexit_cleanup runs after the loop, so a callback that calls
                    // _run_exitfuncs() again reaches itself a second time (measured: "again 0" then
                    // "again 1"), and only the inner round's cleanup empties the list. The bounds check
                    // stays because that inner round can shrink the list under this loop.
                    if (i >= Callbacks.Count) continue;
                    entry = Callbacks[i];
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
