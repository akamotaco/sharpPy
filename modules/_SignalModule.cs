using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SharpPy.Modules
{
    /// <summary>
    /// Modules/signalmodule.c for what a program can see without the OS: the table of handlers and
    /// tripped signals. Nothing is installed in the process, because a host such as Godot owns its
    /// signals, so only raise_signal and _thread.interrupt_main trip one.
    /// </summary>
    internal static class _SignalModule
    {
        private const int SIG_DFL = 0, SIG_IGN = 1, SIGINT = 2;
        private static readonly bool Windows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        private static readonly int NSIG = Windows ? 23 : 65;
        private static readonly Dictionary<int, PyObject> Handlers = new();
        private static readonly Queue<int> Tripped = new();
        private static readonly PyBuiltinFunction DefaultIntHandler = new("default_int_handler", args =>
            throw new PythonException(new PyKeyboardInterrupt()));

        private static readonly (string Name, int Number)[] Signals = Windows
            ? new[] { ("SIGINT", 2), ("SIGILL", 4), ("SIGFPE", 8), ("SIGSEGV", 11), ("SIGTERM", 15), ("SIGBREAK", 21), ("SIGABRT", 22) }
            : new[] { ("SIGHUP", 1), ("SIGINT", 2), ("SIGQUIT", 3), ("SIGILL", 4), ("SIGABRT", 6), ("SIGFPE", 8), ("SIGKILL", 9),
                      ("SIGUSR1", 10), ("SIGSEGV", 11), ("SIGUSR2", 12), ("SIGPIPE", 13), ("SIGALRM", 14), ("SIGTERM", 15), ("SIGCHLD", 17) };

        internal static PyModule CreateSignalModule()
        {
            var module = new PyModule("_signal");
            foreach (var (name, number) in Signals)
                module.ModuleDict[name] = new PyInt(number);
            module.ModuleDict["SIG_DFL"] = new PyInt(SIG_DFL);
            module.ModuleDict["SIG_IGN"] = new PyInt(SIG_IGN);
            module.ModuleDict["NSIG"] = new PyInt(NSIG);
            if (Windows)
            {
                module.ModuleDict["CTRL_C_EVENT"] = new PyInt(0);
                module.ModuleDict["CTRL_BREAK_EVENT"] = new PyInt(1);
            }
            module.ModuleDict["default_int_handler"] = DefaultIntHandler;
            module.ModuleDict["signal"] = new PyBuiltinFunction("signal", Signal);
            module.ModuleDict["getsignal"] = new PyBuiltinFunction("getsignal", args => Handler(Number(args[0])));
            module.ModuleDict["raise_signal"] = new PyBuiltinFunction("raise_signal", args =>
            {
                Trip(Number(args[0]));
                return PyNone.Instance;
            });
            module.ModuleDict["strsignal"] = new PyBuiltinFunction("strsignal", args => PyNone.Instance);
            module.ModuleDict["valid_signals"] = new PyBuiltinFunction("valid_signals", args =>
            {
                var numbers = new List<PyObject>();
                foreach (var (_, number) in Signals)
                    numbers.Add(new PyInt(number));
                return new PySet(numbers);
            });
            module.ModuleDict["set_wakeup_fd"] = new PyBuiltinFunction("set_wakeup_fd", args => new PyInt(-1));
            return module;
        }

        private static int Number(PyObject value)
        {
            var index = PyObject.TryGetIndex(value) ??
                throw PyTypeError.Create($"'{value.GetTypeName()}' object cannot be interpreted as an integer");
            if (index.Value < 1 || index.Value >= NSIG)
                throw PyValueError.Create("signal number out of range");
            return (int)index.Value;
        }

        private static PyObject Handler(int number)
        {
            lock (Handlers)
            {
                if (Handlers.TryGetValue(number, out var handler))
                    return handler;
            }
            return number == SIGINT ? DefaultIntHandler : new PyInt(SIG_DFL);
        }

        // signal_signal_impl
        private static PyObject Signal(PyObject[] args)
        {
            if (!PyVM.Instance.BoundState.IsMain)
                throw PyValueError.Create("signal only works in main thread of the main interpreter");
            int number = Number(args[0]);
            var handler = args[1];
            if (!handler.IsCallable() && !(PyObject.TryGetIndex(handler)?.Value is { } value && (value == SIG_DFL || value == SIG_IGN)))
                throw PyTypeError.Create("signal handler must be signal.SIG_IGN, signal.SIG_DFL, or a callable object");
            var previous = Handler(number);
            lock (Handlers)
                Handlers[number] = handler;
            return previous;
        }

        // trip_signal: the handler runs in the main thread, at its next eval breaker.
        internal static void Trip(int number)
        {
            if (number < 1 || number >= NSIG)
                throw PyValueError.Create("signal number out of range");
            lock (Tripped)
                Tripped.Enqueue(number);
            PyGil.InterruptPending = true;
            PyPending.Flag = true;
            if (PyVM.Instance.BoundState.IsMain)
                RunPending();
        }

        // _PyErr_CheckSignalsTstate
        internal static void RunPending()
        {
            PyGil.InterruptPending = false;
            while (true)
            {
                int number;
                lock (Tripped)
                {
                    if (Tripped.Count == 0)
                        return;
                    number = Tripped.Dequeue();
                }
                var handler = Handler(number);
                if (handler.IsCallable())
                    handler.Call(new PyObject[] { new PyInt(number), (PyObject)PyVM.CurrentFrame ?? PyNone.Instance }, null);
            }
        }
    }
}
