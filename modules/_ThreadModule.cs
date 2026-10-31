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
            var lockType = new PyType("lock", new[] { PyType.ObjectType }, "_thread");
            lockType.TypeDict["acquire"] = new PyMethodDescriptor("acquire", lockType, LockAcquire, 0, 2);
            lockType.TypeDict["release"] = new PyMethodDescriptor("release", lockType, LockRelease, 0, 0);
            lockType.TypeDict["locked"] = new PyMethodDescriptor("locked", lockType, LockLocked, 0, 0);
            lockType.TypeDict["__enter__"] = new PyMethodDescriptor("__enter__", lockType, LockEnter, 0, 0);
            lockType.TypeDict["__exit__"] = new PyMethodDescriptor("__exit__", lockType, LockExit, 0, 3);
            lockType.TypeDict["_at_fork_reinit"] = new PyMethodDescriptor("_at_fork_reinit", lockType, LockAtForkReinit, 0, 0);
            module.ModuleDict["LockType"] = lockType;

            // RLock type
            var rlockType = new PyType("RLock", new[] { PyType.ObjectType }, "_thread");
            rlockType.ConstructedByNewSlot = true;
            rlockType.TypeDict["__new__"] = new PyMethodDescriptor("__new__", rlockType,
                (cls, args, kwargs) => new PyRLock(rlockType), 0, int.MaxValue);
            rlockType.TypeDict["acquire"] = new PyMethodDescriptor("acquire", rlockType, RLockAcquire, 0, 2);
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
            module.ModuleDict["_count"] = new PyBuiltinFunction("_count", _ => new PyInt(1));
            module.ModuleDict["stack_size"] = new PyBuiltinFunction("stack_size", _ => new PyInt(0));
            module.ModuleDict["interrupt_main"] = new PyBuiltinFunction("interrupt_main", _ => PyNone.Instance);
            module.ModuleDict["exit"] = new PyBuiltinFunction("exit", _ => throw new PythonException(new PySystemExit()));
            module.ModuleDict["_set_sentinel"] = new PyBuiltinFunction("_set_sentinel", _ => new PyLock(lockType));
            module.ModuleDict["daemon_threads_allowed"] = new PyBuiltinFunction("daemon_threads_allowed", _ => PyBool.True);
            module.ModuleDict["start_new_thread"] = new PyBuiltinFunction("start_new_thread", _ =>
                throw PyRuntimeError.Create("can't start new thread"));
            module.ModuleDict["start_new"] = new PyBuiltinFunction("start_new", _ =>
                throw PyRuntimeError.Create("can't start new thread"));

            // _local: plain object with attributes (single-threaded, no TLS needed)
            var localType = new PyType("_local", new[] { PyType.ObjectType }, "_thread");
            module.ModuleDict["_local"] = localType;

            return module;
        }

        // --- Lock methods ---

        private static PyObject LockAcquire(PyObject self, PyObject[] args, PyDict kwargs)
        {
            var held = (PyLock)self;
            bool blocking = true;
            double timeout = -1;

            if (args.Length >= 1)
                blocking = args[0].ToBool();
            if (args.Length >= 2)
                timeout = args[1].ToFloat();

            if (!held.Locked)
            {
                held.Locked = true;
                return PyBool.True;
            }

            // Held
            if (!blocking || timeout >= 0)
                return PyBool.False;

            // blocking with no timeout, but single-threaded: deadlock
            throw PyRuntimeError.Create("deadlock");
        }

        private static PyObject LockRelease(PyObject self, PyObject[] args, PyDict kwargs)
        {
            var held = (PyLock)self;
            if (!held.Locked)
                throw PyRuntimeError.Create("release unlocked lock");
            held.Locked = false;
            return PyNone.Instance;
        }

        private static PyObject LockLocked(PyObject self, PyObject[] args, PyDict kwargs)
        {
            var held = (PyLock)self;
            return held.Locked ? PyBool.True : PyBool.False;
        }

        private static PyObject LockEnter(PyObject self, PyObject[] args, PyDict kwargs)
        {
            var held = (PyLock)self;
            if (!held.Locked)
            {
                held.Locked = true;
                return self;
            }
            throw PyRuntimeError.Create("deadlock");
        }

        private static PyObject LockExit(PyObject self, PyObject[] args, PyDict kwargs)
        {
            var held = (PyLock)self;
            held.Locked = false;
            return PyBool.False;
        }

        private static PyObject LockAtForkReinit(PyObject self, PyObject[] args, PyDict kwargs)
        {
            var held = (PyLock)self;
            held.Locked = false;
            return PyNone.Instance;
        }

        // --- RLock methods ---

        private static PyObject RLockAcquire(PyObject self, PyObject[] args, PyDict kwargs)
        {
            var rlock = (PyRLock)self;
            bool blocking = true;
            double timeout = -1;

            if (args.Length >= 1)
                blocking = args[0].ToBool();
            if (args.Length >= 2)
                timeout = args[1].ToFloat();

            if (rlock.Owner == Environment.CurrentManagedThreadId)
            {
                rlock.Count++;
                return PyBool.True;
            }

            if (!rlock.Locked)
            {
                rlock.Locked = true;
                rlock.Owner = Environment.CurrentManagedThreadId;
                rlock.Count = 1;
                return PyBool.True;
            }

            if (!blocking || timeout >= 0)
                return PyBool.False;

            throw PyRuntimeError.Create("deadlock");
        }

        private static PyObject RLockRelease(PyObject self, PyObject[] args, PyDict kwargs)
        {
            var rlock = (PyRLock)self;
            if (!rlock.Locked || rlock.Owner != Environment.CurrentManagedThreadId)
                throw PyRuntimeError.Create("cannot release un-acquired lock");
            rlock.Count--;
            if (rlock.Count == 0)
            {
                rlock.Locked = false;
                rlock.Owner = -1;
            }
            return PyNone.Instance;
        }

        private static PyObject RLockIsOwned(PyObject self, PyObject[] args, PyDict kwargs)
        {
            var rlock = (PyRLock)self;
            return (rlock.Locked && rlock.Owner == Environment.CurrentManagedThreadId) ? PyBool.True : PyBool.False;
        }

        private static PyObject RLockAcquireRestore(PyObject self, PyObject[] args, PyDict kwargs)
        {
            var rlock = (PyRLock)self;
            if (args.Length != 1 || args[0] is not PyTuple tuple || tuple.Items.Length != 2)
                throw PyTypeError.Create("_acquire_restore() takes a tuple of (count, owner)");
            rlock.Count = (int)((PyInt)tuple.Items[0]).Value;
            rlock.Owner = (int)((PyInt)tuple.Items[1]).Value;
            rlock.Locked = true;
            return PyNone.Instance;
        }

        private static PyObject RLockReleaseSave(PyObject self, PyObject[] args, PyDict kwargs)
        {
            var rlock = (PyRLock)self;
            if (!rlock.Locked || rlock.Owner != Environment.CurrentManagedThreadId)
                throw PyRuntimeError.Create("cannot release un-acquired lock");
            int savedCount = rlock.Count;
            int savedOwner = rlock.Owner;
            rlock.Count = 0;
            rlock.Locked = false;
            rlock.Owner = -1;
            return new PyTuple(new PyObject[] { new PyInt(savedCount), new PyInt(savedOwner) });
        }

        private static PyObject RLockEnter(PyObject self, PyObject[] args, PyDict kwargs)
        {
            var rlock = (PyRLock)self;
            if (rlock.Owner == Environment.CurrentManagedThreadId)
            {
                rlock.Count++;
                return self;
            }
            if (!rlock.Locked)
            {
                rlock.Locked = true;
                rlock.Owner = Environment.CurrentManagedThreadId;
                rlock.Count = 1;
                return self;
            }
            throw PyRuntimeError.Create("deadlock");
        }

        private static PyObject RLockExit(PyObject self, PyObject[] args, PyDict kwargs)
        {
            var rlock = (PyRLock)self;
            rlock.Count--;
            if (rlock.Count == 0)
            {
                rlock.Locked = false;
                rlock.Owner = -1;
            }
            return PyBool.False;
        }

        private static PyObject RLockRecursionCount(PyObject self, PyObject[] args, PyDict kwargs)
        {
            var rlock = (PyRLock)self;
            return new PyInt(rlock.Count);
        }
    }

    /// <summary>
    /// Lock instance for _thread module.
    /// </summary>
    internal class PyLock : PyObject
    {
        public bool Locked { get; set; }
        private readonly PyType _type;

        public PyLock(PyType type)
        {
            _type = type;
        }

        public override PyType GetPyType() => _type;
        public override string GetTypeName() => "lock";
    }

    /// <summary>
    /// RLock instance for _thread module.
    /// </summary>
    internal class PyRLock : PyObject
    {
        public bool Locked { get; set; }
        public int Owner { get; set; } = -1;
        public int Count { get; set; }
        private readonly PyType _type;

        public PyRLock(PyType type)
        {
            _type = type;
        }

        public override PyType GetPyType() => _type;
        public override string GetTypeName() => "RLock";
    }
}
