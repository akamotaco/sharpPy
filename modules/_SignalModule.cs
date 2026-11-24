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
                module.ModuleDict[name] = PyInt.From(number);
            module.ModuleDict["SIG_DFL"] = PyInt.From(SIG_DFL);
            module.ModuleDict["SIG_IGN"] = PyInt.From(SIG_IGN);
            module.ModuleDict["NSIG"] = PyInt.From(NSIG);
            if (Windows)
            {
                module.ModuleDict["CTRL_C_EVENT"] = PyInt.From(0);
                module.ModuleDict["CTRL_BREAK_EVENT"] = PyInt.From(1);
            }
            module.ModuleDict["default_int_handler"] = DefaultIntHandler;
            module.ModuleDict["signal"] = new PyBuiltinFunction("signal", Signal);
            module.ModuleDict["getsignal"] = new PyBuiltinFunction("getsignal", args => Handler(Number(args[0])));
            // signal_raise_signal_impl: raise(signalnum), whose failure for a signal the C runtime does
            // not have is OSError from errno (EINVAL).
            module.ModuleDict["raise_signal"] = new PyBuiltinFunction("raise_signal", args =>
            {
                int number = PyArgs.Int(args[0]);
                if (!IsSignal(number))
                    throw new PythonException(PyOSError.FromArguments(new PyObject[] { PyInt.From(22), PyString.From("Invalid argument") }, null));
                Trip(number);
                return PyNone.Instance;
            });
            // signal_strsignal's clinic: signalnum through the int converter.
            module.ModuleDict["strsignal"] = new PyBuiltinFunction("strsignal", args =>
            {
                PyArgs.Int(args[0]);
                return PyNone.Instance;
            });
            module.ModuleDict["valid_signals"] = new PyBuiltinFunction("valid_signals", args =>
            {
                var numbers = new List<PyObject>();
                foreach (var (_, number) in Signals)
                    numbers.Add(PyInt.From(number));
                return new PySet(numbers);
            });
            module.ModuleDict["set_wakeup_fd"] = new PyBuiltinFunction("set_wakeup_fd", SetWakeupFd);
            return module;
        }

        // The signals of the C runtime: on Windows the seven of its signal.h, elsewhere 1 to NSIG - 1.
        private static bool IsSignal(int number)
        {
            if (!Windows)
                return number >= 1 && number < NSIG;
            foreach (var (_, signal) in Signals)
                if (signal == number)
                    return true;
            return false;
        }

        private static int Number(PyObject value)
        {
            var index = PyObject.IndexOrThrow(value);
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
            return number == SIGINT ? DefaultIntHandler : PyInt.From(SIG_DFL);
        }

        // signal_signal_impl
        private static PyObject Signal(PyObject[] args)
        {
            // MS_WINDOWS: only the signals Windows' signal() takes.
            if (Windows && !IsSignal(PyArgs.Int(args[0])))
                throw PyValueError.Create("invalid signal value");
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

        // wakeup: the fd (on Windows a socket of the _socket table, or a C runtime descriptor), and
        // whether trip_signal sends to it.
        private static long _wakeupFd = -1;
        private static bool _wakeupUseSend;
        private static bool _wakeupWarnOnFullBuffer = true;

        // signal_set_wakeup_fd: "O|$p:set_wakeup_fd" (Windows) or "i|$p:set_wakeup_fd".
        private static PyObject SetWakeupFd(PyObject[] args, PyDict kwargs)
        {
            var a = PyArgs.Unpack("set_wakeup_fd", args, kwargs, new[] { "", "warn_on_full_buffer" }, 1, 1, 1);
            bool warnOnFullBuffer = a[1] == null || a[1].IsTrue();
            long fd = Windows ? SocketModule.AsSocket(a[0]) : PyArgs.Int(a[0]);
            if (!PyVM.Instance.BoundState.IsMain)
                throw PyValueError.Create("set_wakeup_fd only works in main thread of the main interpreter");
            bool isSocket = false;
            if (fd != -1)
            {
                if (Windows && SocketHandles.Get(fd) != null)
                {
                    isSocket = true;
                }
                else
                {
                    if (fd > int.MaxValue || fd < int.MinValue)
                        throw PyValueError.Create("invalid fd");
                    // _Py_fstat: OSError for a descriptor that is not open.
                    PyFileDescriptors.Get((int)fd);
                }
            }
            long old = _wakeupFd;
            _wakeupFd = fd;
            _wakeupWarnOnFullBuffer = warnOnFullBuffer;
            _wakeupUseSend = isSocket;
            return PyInt.From(old);
        }

        // trip_signal's write of the signal number to the wakeup fd. A failure is not reported
        // (CPython reports it through a pending call, report_wakeup_send_error).
        private static void WriteWakeup(int number)
        {
            long fd = _wakeupFd;
            if (fd == -1)
                return;
            var data = new[] { (byte)number };
            try
            {
                if (_wakeupUseSend)
                    SocketHandles.Get(fd)?.Send(data, System.Net.Sockets.SocketFlags.None);
                else
                    PyFileDescriptors.Get((int)fd).Write(data, 0, 1);
            }
            catch (Exception)
            {
            }
        }

        // trip_signal: the handler runs in the main thread, at its next eval breaker; the wakeup fd is
        // written after the flags are set (bpo-30038).
        internal static void Trip(int number)
        {
            if (number < 1 || number >= NSIG)
                throw PyValueError.Create("signal number out of range");
            lock (Tripped)
                Tripped.Enqueue(number);
            PyGil.InterruptPending = true;
            PyPending.Flag = true;
            WriteWakeup(number);
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
                    handler.Call(new PyObject[] { PyInt.From(number), (PyObject)PyVM.CurrentFrame ?? PyNone.Instance }, null);
            }
        }
    }
}
