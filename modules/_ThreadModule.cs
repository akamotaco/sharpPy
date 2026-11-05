using System;

namespace SharpPy.Modules
{
    /// <summary>
    /// _thread module - CPython 3.12: Modules/_threadmodule.c
    /// SharpPy's VM is single-threaded, so start_new_thread always raises.
    /// </summary>
    internal static class _ThreadModule
    {
        internal static PyModule CreateThreadModule()
        {
            var module = new PyModule("_thread");

            module.ModuleDict["error"] = PyType.TypeType; // RuntimeError is the error type
            // Actually error should be RuntimeError. Let's use the existing RuntimeError type.
            module.ModuleDict["error"] = PyType.RuntimeErrorType;

            module.ModuleDict["TIMEOUT_MAX"] = new PyFloat(4294967.0);

            // LockType
            var lockType = new PyLockType("lock");
            lockType.TypeDict["acquire"] = new PyMethodDescriptor("acquire", lockType, LockAcquire, 0, 2, acceptsKwargs: true);
            lockType.TypeDict["release"] = new PyMethodDescriptor("release", lockType, LockRelease, 0, 0);
            lockType.TypeDict["locked"] = new PyMethodDescriptor("locked", lockType, LockLocked, 0, 0);
            lockType.TypeDict["__enter__"] = new PyMethodDescriptor("__enter__", lockType, LockEnter, 0, 0);
            lockType.TypeDict["__exit__"] = new PyMethodDescriptor("__exit__", lockType, LockExit, 0, 3);
            lockType.TypeDict["_at_fork_reinit"] = new PyMethodDescriptor("_at_fork_reinit", lockType, LockAtForkReinit, 0, 0);
            module.ModuleDict["LockType"] = lockType;

            // RLock type
            var rlockType = new PyLockType("RLock");
            rlockType.ConstructedByNewSlot = true;
            rlockType.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", rlockType,
                (cls, args, kwargs) => new PyRLock(rlockType), 0, int.MaxValue);
            rlockType.TypeDict["acquire"] = new PyMethodDescriptor("acquire", rlockType, RLockAcquire, 0, 2, acceptsKwargs: true);
            rlockType.TypeDict["release"] = new PyMethodDescriptor("release", rlockType, RLockRelease, 0, 0);
            rlockType.TypeDict["_is_owned"] = new PyMethodDescriptor("_is_owned", rlockType, RLockIsOwned, 0, 0);
            rlockType.TypeDict["_acquire_restore"] = new PyMethodDescriptor("_acquire_restore", rlockType, RLockAcquireRestore, 0, 1);
            rlockType.TypeDict["_release_save"] = new PyMethodDescriptor("_release_save", rlockType, RLockReleaseSave, 0, 0);
            rlockType.TypeDict["__enter__"] = new PyMethodDescriptor("__enter__", rlockType, RLockEnter, 0, 0);
            rlockType.TypeDict["__exit__"] = new PyMethodDescriptor("__exit__", rlockType, RLockExit, 0, 3);
            rlockType.TypeDict["_recursion_count"] = new PyMethodDescriptor("_recursion_count", rlockType, RLockRecursionCount, 0, 0);
            module.ModuleDict["RLock"] = rlockType;

            module.ModuleDict["allocate_lock"] = new PyBuiltinFunction("allocate_lock", _ => new PyLock(lockType));
            module.ModuleDict["allocate"] = new PyBuiltinFunction("allocate", _ => new PyLock(lockType));
            module.ModuleDict["get_ident"] = new PyBuiltinFunction("get_ident", _ => new PyInt(Environment.CurrentManagedThreadId));
            module.ModuleDict["get_native_id"] = new PyBuiltinFunction("get_native_id", _ => new PyInt(Environment.CurrentManagedThreadId));
            module.ModuleDict["_count"] = new PyBuiltinFunction("_count", _ => new PyInt(_running));
            module.ModuleDict["stack_size"] = new PyBuiltinFunction("stack_size", StackSize);
            module.ModuleDict["interrupt_main"] = new PyBuiltinFunction("interrupt_main", InterruptMain);
            module.ModuleDict["exit"] = new PyBuiltinFunction("exit", _ => throw new PythonException(new PySystemExit()));
            module.ModuleDict["exit_thread"] = module.ModuleDict["exit"];
            // thread__set_sentinel: this lock is released when the thread state goes away.
            module.ModuleDict["_set_sentinel"] = new PyBuiltinFunction("_set_sentinel", _ =>
            {
                var sentinel = new PyLock(lockType);
                PyVM.Instance.BoundState.Sentinels.Add(sentinel);
                return sentinel;
            });
            // _thread._local has its one implementation in Lib/_threading_local.py.
            module.ModuleDict["__getattr__"] = new PyBuiltinFunction("__getattr__", args =>
            {
                if (args[0] is PyString { Value: "_local" })
                    return PyImportSystem.Import("_threading_local").GetAttribute("local");
                throw PyAttributeError.Create($"module '_thread' has no attribute '{args[0].AsString().Value}'");
            });
            module.ModuleDict["daemon_threads_allowed"] = new PyBuiltinFunction("daemon_threads_allowed", _ => PyBool.True);
            module.ModuleDict["start_new_thread"] = new PyBuiltinFunction("start_new_thread", StartNewThread);
            module.ModuleDict["start_new"] = module.ModuleDict["start_new_thread"];

            return module;
        }

        // A worker gets a stack the parser's and the VM's recursion can use.
#if GODOT
        private const int DefaultStackBytes = 16 * 1024 * 1024;
#else
        private const int DefaultStackBytes = 64 * 1024 * 1024;
#endif
        private static int _stackBytes;
        private static int _running = 1;

        // thread_PyThread_interrupt_main
        private static PyObject InterruptMain(PyObject[] args)
        {
            _SignalModule.Trip(args.Length > 0 ? args[0].ToInt() : 2);
            return PyNone.Instance;
        }

        // thread_stack_size
        private static PyObject StackSize(PyObject[] args)
        {
            int old = _stackBytes;
            if (args.Length > 0)
            {
                var size = PyObject.TryGetIndex(args[0]) ??
                    throw PyTypeError.Create($"'{args[0].GetTypeName()}' object cannot be interpreted as an integer");
                if (size.Value < 0)
                    throw PyValueError.Create("size must be 0 or at least 32 KiB");
                if (size.Value != 0 && size.Value < 32768)
                    throw PyValueError.Create("size not valid: " + size.Value + " bytes");
                _stackBytes = (int)size.Value;
            }
            return new PyInt(old);
        }

        // thread_PyThread_start_new_thread
        private static PyObject StartNewThread(PyObject[] args)
        {
            if (PyGil.Finalizing)
                throw PyRuntimeError.Create("can't create new thread at interpreter shutdown");
            if (args.Length < 2 || args.Length > 3)
                throw PyTypeError.Create($"start_new_thread expected at {(args.Length < 2 ? "least 2" : "most 3")} arguments, got {args.Length}");
            var function = args[0];
            if (!function.IsCallable())
                throw PyTypeError.Create("first arg must be callable");
            if (args[1] is not PyTuple arguments)
                throw PyTypeError.Create("2nd arg must be a tuple");
            if (args.Length == 3 && args[2] is not PyDict)
                throw PyTypeError.Create("optional 3rd arg must be a dictionary");
            var keywords = args.Length == 3 ? (PyDict)args[2] : null;
            PyGil.Enable();
            System.Threading.Interlocked.Increment(ref _running);
            var thread = new System.Threading.Thread(() => Run(function, arguments, keywords),
                _stackBytes != 0 ? _stackBytes : DefaultStackBytes);
            thread.IsBackground = true;
            try
            {
                thread.Start();
            }
            catch (OutOfMemoryException)
            {
                System.Threading.Interlocked.Decrement(ref _running);
                throw PyRuntimeError.Create("can't start new thread");
            }
            return new PyInt(thread.ManagedThreadId);
        }

        // thread_run
        private static void Run(PyObject function, PyTuple arguments, PyDict keywords)
        {
            var state = new PyThreadState();
            PyGil.Register(state);
            PyGil.Attach(state);
            PyGil.Take(state);
            try
            {
                function.Call(arguments.Items, keywords);
            }
            catch (PythonException error)
            {
                if (error.PyException is not PySystemExit)
                    PyUnraisable.Report(error, function, "Exception ignored in thread started by");
            }
            finally
            {
                // PyThreadState_Clear: on_delete releases what join waits on.
                foreach (var sentinel in state.Sentinels)
                    if (sentinel.Locked)
                        sentinel.Release();
                System.Threading.Interlocked.Decrement(ref _running);
                PyGil.Unregister(state);
                PyGil.Drop(state);
            }
        }

        // lock_acquire_parse_args
        private static (bool Blocking, double Timeout) AcquireArguments(PyObject[] args, PyDict kwargs)
        {
            PyObject blockingArgument = args.Length >= 1 ? args[0] : null;
            PyObject timeoutArgument = args.Length >= 2 ? args[1] : null;
            if (kwargs != null)
            {
                blockingArgument ??= kwargs.Get(new PyString("blocking"), null);
                timeoutArgument ??= kwargs.Get(new PyString("timeout"), null);
            }
            bool blocking = blockingArgument == null || blockingArgument is PyNone || blockingArgument.IsTrue();
            double timeout = -1;
            if (timeoutArgument != null && timeoutArgument is not PyNone)
            {
                timeout = timeoutArgument.AsFloat().Value;
                if (!blocking && timeout != -1)
                    throw PyValueError.Create("can't specify a timeout for a non-blocking call");
                if (timeout < 0 && timeout != -1)
                    throw PyValueError.Create("timeout value must be positive");
                if (timeout > 4294967.0)
                    throw PyOverflowError.Create("timeout value is too large");
            }
            return (blocking, timeout);
        }

        private static void Refuse()
        {
            // With one thread no one can release it. CPython blocks forever; a game must not.
            if (_running <= 1)
                throw PyRuntimeError.Create("deadlock");
        }

        private static PyObject LockAcquire(PyObject self, PyObject[] args, PyDict kwargs)
        {
            var (blocking, timeout) = AcquireArguments(args, kwargs);
            var held = (PyLock)self;
            if (held.TryAcquire())
                return PyBool.True;
            if (!blocking)
                return PyBool.False;
            if (timeout < 0)
                Refuse();
            return PyBool.FromBool(held.Acquire(timeout));
        }

        private static PyObject LockRelease(PyObject self, PyObject[] args, PyDict kwargs)
        {
            ((PyLock)self).Release();
            return PyNone.Instance;
        }

        private static PyObject LockLocked(PyObject self, PyObject[] args, PyDict kwargs) =>
            ((PyLock)self).Locked ? PyBool.True : PyBool.False;

        private static PyObject LockEnter(PyObject self, PyObject[] args, PyDict kwargs)
        {
            LockAcquire(self, Array.Empty<PyObject>(), null);
            return self;
        }

        private static PyObject LockExit(PyObject self, PyObject[] args, PyDict kwargs)
        {
            ((PyLock)self).Release();
            return PyBool.False;
        }

        private static PyObject LockAtForkReinit(PyObject self, PyObject[] args, PyDict kwargs)
        {
            ((PyLock)self).Reset();
            return PyNone.Instance;
        }

        // --- RLock methods ---

        private static PyObject RLockAcquire(PyObject self, PyObject[] args, PyDict kwargs)
        {
            var (blocking, timeout) = AcquireArguments(args, kwargs);
            var rlock = (PyRLock)self;
            if (rlock.Owner == Environment.CurrentManagedThreadId)
            {
                rlock.Count++;
                return PyBool.True;
            }
            if (!rlock.Lock.TryAcquire())
            {
                if (!blocking)
                    return PyBool.False;
                if (timeout < 0)
                    Refuse();
                if (!rlock.Lock.Acquire(timeout))
                    return PyBool.False;
            }
            rlock.Owner = Environment.CurrentManagedThreadId;
            rlock.Count = 1;
            return PyBool.True;
        }

        private static PyObject RLockRelease(PyObject self, PyObject[] args, PyDict kwargs)
        {
            var rlock = (PyRLock)self;
            if (rlock.Count == 0 || rlock.Owner != Environment.CurrentManagedThreadId)
                throw PyRuntimeError.Create("cannot release un-acquired lock");
            if (--rlock.Count == 0)
            {
                rlock.Owner = -1;
                rlock.Lock.Release();
            }
            return PyNone.Instance;
        }

        private static PyObject RLockIsOwned(PyObject self, PyObject[] args, PyDict kwargs)
        {
            var rlock = (PyRLock)self;
            return rlock.Count > 0 && rlock.Owner == Environment.CurrentManagedThreadId ? PyBool.True : PyBool.False;
        }

        private static PyObject RLockAcquireRestore(PyObject self, PyObject[] args, PyDict kwargs)
        {
            var rlock = (PyRLock)self;
            if (args.Length != 1 || args[0] is not PyTuple tuple || tuple.Items.Length != 2)
                throw PyTypeError.Create("_acquire_restore() takes a tuple of (count, owner)");
            if (!rlock.Lock.TryAcquire())
                rlock.Lock.Acquire(-1);
            rlock.Count = (int)((PyInt)tuple.Items[0]).Value;
            rlock.Owner = (int)((PyInt)tuple.Items[1]).Value;
            return PyNone.Instance;
        }

        private static PyObject RLockReleaseSave(PyObject self, PyObject[] args, PyDict kwargs)
        {
            var rlock = (PyRLock)self;
            if (rlock.Count == 0)
                throw PyRuntimeError.Create("cannot release un-acquired lock");
            var saved = new PyTuple(new PyObject[] { new PyInt(rlock.Count), new PyInt(rlock.Owner) });
            rlock.Count = 0;
            rlock.Owner = -1;
            rlock.Lock.Release();
            return saved;
        }

        private static PyObject RLockEnter(PyObject self, PyObject[] args, PyDict kwargs)
        {
            RLockAcquire(self, Array.Empty<PyObject>(), null);
            return self;
        }

        private static PyObject RLockExit(PyObject self, PyObject[] args, PyDict kwargs)
        {
            RLockRelease(self, Array.Empty<PyObject>(), null);
            return PyBool.False;
        }

        private static PyObject RLockRecursionCount(PyObject self, PyObject[] args, PyDict kwargs) =>
            new PyInt(((PyRLock)self).Owner == Environment.CurrentManagedThreadId ? ((PyRLock)self).Count : 0);
    }

    // Lock_Type and RLock_Type have tp_weaklistoffset.
    internal sealed class PyLockType : PyType
    {
        internal PyLockType(string name) : base(name, new[] { ObjectType }, "_thread") { }

        internal override bool SupportsWeakReference() => true;
    }

    /// <summary>Modules/_threadmodule.c lockobject over Python/thread_nt_gil.h's wait.</summary>
    internal class PyLock : PyObject
    {
        private readonly object _gate = new();
        private readonly PyType _type;
        private bool _locked;

        internal bool Locked => _locked;

        public PyLock(PyType type)
        {
            _type = type;
        }

        internal bool TryAcquire()
        {
            lock (_gate)
            {
                if (_locked)
                    return false;
                return _locked = true;
            }
        }

        // PyThread_acquire_lock_timed with the GIL given up. timeout < 0 waits for ever.
        internal bool Acquire(double timeout)
        {
            bool taken = false;
            PyGil.AllowThreads(() =>
            {
                var limit = timeout < 0 ? (System.Diagnostics.Stopwatch)null : System.Diagnostics.Stopwatch.StartNew();
                lock (_gate)
                {
                    while (_locked)
                    {
                        if (limit == null)
                        {
                            System.Threading.Monitor.Wait(_gate);
                            continue;
                        }
                        double left = timeout - limit.Elapsed.TotalSeconds;
                        if (left <= 0)
                            return;
                        System.Threading.Monitor.Wait(_gate, (int)Math.Min(Math.Ceiling(left * 1000), int.MaxValue));
                    }
                    taken = _locked = true;
                }
            });
            return taken;
        }

        internal void Release()
        {
            lock (_gate)
            {
                if (!_locked)
                    throw PyRuntimeError.Create("release unlocked lock");
                _locked = false;
                System.Threading.Monitor.Pulse(_gate);
            }
        }

        internal void Reset()
        {
            lock (_gate)
                _locked = false;
        }

        public override PyType GetPyType() => _type;
        public override string GetTypeName() => "lock";

        // lock_repr
        protected internal override PyString Repr() => new PyString($"<{(_locked ? "locked" : "unlocked")} _thread.lock object at 0x{GetHashCode():x}>");
    }

    /// <summary>
    /// RLock instance for _thread module.
    /// </summary>
    internal class PyRLock : PyObject
    {
        internal readonly PyLock Lock;
        public int Owner { get; set; } = -1;
        public int Count { get; set; }
        private readonly PyType _type;

        public PyRLock(PyType type)
        {
            _type = type;
            Lock = new PyLock(type);
        }

        public override PyType GetPyType() => _type;
        public override string GetTypeName() => "RLock";

        // rlock_repr
        protected internal override PyString Repr() =>
            new PyString($"<{(Count > 0 ? "locked" : "unlocked")} _thread.RLock object owner={(Count > 0 ? Owner : 0)} count={Count} at 0x{GetHashCode():x}>");
    }
}
