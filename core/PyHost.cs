using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// What a host such as Godot needs around Python, as PyGILState_Ensure, Py_AddPendingCall and
    /// Py_FinalizeEx give an embedding C program. Calling into Python goes through Enter, so that
    /// worker threads run while the host is in its own code.
    /// </summary>
    public static class PyHost
    {
        private static readonly Queue<(PyObject Function, PyObject[] Arguments, PyDict Keywords)> MainQueue = new();

        [ThreadStatic] private static int _depth;

        /// <summary>
        /// Give the GIL back when the outermost Enter ends. A host needs that: its own frame loop
        /// would otherwise hold every Python thread. The command line never leaves Python.
        /// </summary>
#if GODOT
        public static bool ReleaseOnLeave { get; set; } = true;
#else
        public static bool ReleaseOnLeave { get; set; }
#endif

        /// <summary>The scope of one call into Python: <c>using (PyHost.Enter()) { ... }</c></summary>
        public readonly struct Scope : IDisposable
        {
            public void Dispose() => Leave();
        }

        // PyGILState_Ensure
        public static Scope Enter()
        {
            if (_depth++ == 0 && PyGil.Enabled && !PyGil.HeldByThisThread)
                PyGil.Take(PyGil.Current ?? PyGil.AttachNew());
            return new Scope();
        }

        // PyGILState_Release
        private static void Leave()
        {
            if (--_depth == 0 && PyGil.Enabled && ReleaseOnLeave && PyGil.HeldByThisThread)
                PyGil.Drop(PyGil.Current);
        }

        /// <summary>
        /// Py_AddPendingCall: the function runs on the main thread, at its next eval breaker, or at
        /// RunMainQueue while the host is in its own code. Scene tree work belongs here.
        /// </summary>
        public static void CallOnMain(PyObject function, PyObject[] arguments, PyDict keywords = null)
        {
            lock (MainQueue)
                MainQueue.Enqueue((function, arguments, keywords));
            PyGil.InterruptPending = true;
            PyPending.Flag = true;
        }

        /// <summary>For the host's frame loop. Answers how many calls ran.</summary>
        public static int RunMainQueue()
        {
            using (Enter())
                return DrainMainQueue();
        }

        internal static int DrainMainQueue()
        {
            int count = 0;
            while (true)
            {
                (PyObject Function, PyObject[] Arguments, PyDict Keywords) call;
                lock (MainQueue)
                {
                    if (MainQueue.Count == 0)
                        return count;
                    call = MainQueue.Dequeue();
                }
                count++;
                try
                {
                    call.Function.Call(call.Arguments, call.Keywords);
                }
                catch (PythonException error)
                {
                    PyUnraisable.Report(error, call.Function, "Exception ignored in a call on the main thread");
                }
            }
        }

        /// <summary>Py_FinalizeEx: threading joins what is no daemon, then atexit and finalizers run.</summary>
        public static void Shutdown()
        {
            using (Enter())
                PyFinalizer.Shutdown();
        }
    }
}
