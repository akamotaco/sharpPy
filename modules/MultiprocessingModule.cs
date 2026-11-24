using System;
using System.Net.Sockets;
using System.Threading;

namespace SharpPy.Modules
{
    /// <summary>
    /// _multiprocessing (Modules/_multiprocessing/multiprocessing.c and semaphore.c of CPython v3.12.15),
    /// the Windows build: closesocket/recv/send on the sockets of the _socket handle table, sem_unlink
    /// (SEM_UNLINK is 0 on Windows), the empty flags dict, and SemLock over a semaphore. One C# method
    /// per C function, named after it. CreateSemaphore is System.Threading.Semaphore (unnamed, as
    /// SEM_CREATE passes NULL for the name), its HANDLE a number of ManagedHandles. What the CLR does not
    /// give: the SIGINT event WaitForMultipleObjectsEx also waits on in the main thread (a Ctrl+C does
    /// not interrupt a blocked acquire with EINTR), and a semaphore handle cannot cross into another
    /// process (SharpPy starts none: _winapi.CreateProcess raises), so _rebuild only finds handles of
    /// this process.
    /// </summary>
    internal static class MultiprocessingModule
    {
        // MP_STANDARD_ERROR etc.
        private const int RECURSIVE_MUTEX = 0, SEMAPHORE = 1;
        // SEM_VALUE_MAX on Windows is LONG_MAX
        private const int SEM_VALUE_MAX = int.MaxValue;
        private const uint INFINITE = 0xFFFFFFFF;

        private static PyType SemLockType;

        // HANDLE_converter: PyLong_AsVoidPtr
        private static ulong AsHandle(PyObject value)
        {
            if (PyInt.LongValue(value) is not { } number)
                throw new PythonException(new PySystemError("bad argument to internal function"));
            if (number.Value.Sign < 0)
                return number.Value < long.MinValue ? throw PyOverflowError.Create("int too big to convert") : unchecked((ulong)(long)number.Value);
            return number.Value > ulong.MaxValue ? throw PyOverflowError.Create("int too big to convert") : (ulong)number.Value;
        }

        // PyErr_SetExcFromWindowsErr(PyExc_OSError, WSAGetLastError())
        private static PythonException SocketError(int code) => PyOSError.CreateWindowsFileError(code, null);

        // _multiprocessing_closesocket_impl
        private static PyObject _multiprocessing_closesocket_impl(ulong handle)
        {
            int ret = SocketHandles.Close((long)handle);
            if (ret != 0)
                throw SocketError(ret);
            return PyNone.Instance;
        }

        // _multiprocessing_recv_impl
        private static PyObject _multiprocessing_recv_impl(ulong handle, int size)
        {
            if (size < 0)
                throw PyValueError.Create("negative count");   // PyBytes_FromStringAndSize(NULL, size)
            var sock = SocketHandles.Get((long)handle) ?? throw SocketError(SocketModule.WSAENOTSOCK);
            var buf = new byte[size];
            int nread = 0;
            SocketException failure = null;
            PyGil.AllowThreads(() =>
            {
                try { nread = sock.Receive(buf, 0, size, SocketFlags.None); }
                catch (SocketException error) { failure = error; }
            });
            if (failure != null)
                throw SocketError(SocketModule.ErrorCodeOf(failure));
            return PyBytes.From(buf.AsSpan(0, nread).ToArray());
        }

        // _multiprocessing_send_impl
        private static PyObject _multiprocessing_send_impl(ulong handle, byte[] buf)
        {
            var sock = SocketHandles.Get((long)handle) ?? throw SocketError(SocketModule.WSAENOTSOCK);
            int length = buf.Length;
            int ret = 0;
            SocketException failure = null;
            PyGil.AllowThreads(() =>
            {
                try { ret = sock.Send(buf, 0, length, SocketFlags.None); }
                catch (SocketException error) { failure = error; }
            });
            if (failure != null)
                throw SocketError(SocketModule.ErrorCodeOf(failure));
            return PyInt.From(ret);
        }

        // _multiprocessing_sem_unlink_impl: _PyMp_sem_unlink, SEM_UNLINK(name) is 0 on Windows.
        private static PyObject _multiprocessing_sem_unlink_impl(string name) => PyNone.Instance;

        // ------------------------------------------------------------------ SemLock

        // PyThread_get_thread_ident / GetCurrentThreadId, as _thread.get_ident answers it
        private static ulong CurrentThread() => (ulong)Environment.CurrentManagedThreadId;

        // ISMINE(o)
        private static bool ISMINE(SemLockObject o) => o.count > 0 && CurrentThread() == o.last_tid;

        // _GetSemaphoreValue
        private static int _GetSemaphoreValue(Semaphore handle)
        {
            if (handle.WaitOne(0))
            {
                int previous = handle.Release();
                return previous + 1;
            }
            return 0;
        }

        // _multiprocessing_SemLock_acquire_impl
        private static PyObject _multiprocessing_SemLock_acquire_impl(SemLockObject self, bool blocking, PyObject timeout_obj)
        {
            uint full_msecs;
            /* calculate timeout */
            if (!blocking)
            {
                full_msecs = 0;
            }
            else if (timeout_obj is PyNone)
            {
                full_msecs = INFINITE;
            }
            else
            {
                double timeout = timeout_obj.ToFloat();
                timeout *= 1000.0;      /* convert to millisecs */
                if (timeout < 0.0)
                    timeout = 0.0;
                else if (timeout >= 0.5 * INFINITE)
                    throw PyOverflowError.Create("timeout is too large");
                full_msecs = (uint)(timeout + 0.5);
            }

            /* check whether we already own the lock */
            if (self.kind == RECURSIVE_MUTEX && ISMINE(self))
            {
                ++self.count;
                return PyBool.True;
            }

            var sem = self.Semaphore;
            /* check whether we can acquire without releasing the GIL and blocking */
            if (sem.WaitOne(0))
            {
                self.last_tid = CurrentThread();
                ++self.count;
                return PyBool.True;
            }

            /* do the wait */
            bool acquired = false;
            int wait = full_msecs == INFINITE ? Timeout.Infinite : (int)full_msecs;
            PyGil.AllowThreads(() => acquired = sem.WaitOne(wait));

            /* handle result */
            if (!acquired)
                return PyBool.False;          // WAIT_TIMEOUT
            self.last_tid = CurrentThread();
            ++self.count;
            return PyBool.True;
        }

        // _multiprocessing_SemLock_release_impl
        private static PyObject _multiprocessing_SemLock_release_impl(SemLockObject self)
        {
            if (self.kind == RECURSIVE_MUTEX)
            {
                if (!ISMINE(self))
                    throw new PythonException(new PyAssertionError("attempt to release recursive lock not owned by thread"));
                if (self.count > 1)
                {
                    --self.count;
                    return PyNone.Instance;
                }
            }

            try
            {
                self.Semaphore.Release();
            }
            catch (SemaphoreFullException)
            {
                // ERROR_TOO_MANY_POSTS
                throw PyValueError.Create("semaphore or lock released too many times");
            }
            --self.count;
            return PyNone.Instance;
        }

        // newsemlockobject
        private static PyObject newsemlockobject(PyType type, ulong handle, int kind, int maxvalue, string name) =>
            new SemLockObject(type, SemLockType) { handle = handle, kind = kind, count = 0, last_tid = 0, maxvalue = maxvalue, name = name };

        // _multiprocessing_SemLock_impl
        private static PyObject _multiprocessing_SemLock_impl(PyType type, int kind, int value, int maxvalue, string name, bool unlink)
        {
            if (kind != RECURSIVE_MUTEX && kind != SEMAPHORE)
                throw PyValueError.Create("unrecognized kind");
            string name_copy = unlink ? null : name;
            Semaphore sem;
            try
            {
                // SEM_CREATE(name, val, max): CreateSemaphore(NULL, val, max, NULL)
                sem = new Semaphore(value, maxvalue);
            }
            catch (ArgumentException)
            {
                // CreateSemaphore with an initial count above the maximum, or a maximum below 1.
                throw PyOSError.CreateWindowsFileError(87, null);    // ERROR_INVALID_PARAMETER
            }
            ulong handle = (ulong)ManagedHandles.Add(sem);
            return newsemlockobject(type, handle, kind, maxvalue, name_copy);
        }

        // _multiprocessing_SemLock__rebuild_impl
        private static PyObject _multiprocessing_SemLock__rebuild_impl(PyType type, ulong handle, int kind, int maxvalue, string name) =>
            newsemlockobject(type, handle, kind, maxvalue, name);

        // _multiprocessing_SemLock__count_impl
        private static PyObject _multiprocessing_SemLock__count_impl(SemLockObject self) => PyInt.From(self.count);

        // _multiprocessing_SemLock__is_mine_impl
        private static PyObject _multiprocessing_SemLock__is_mine_impl(SemLockObject self) => PyBool.FromBool(ISMINE(self));

        // _multiprocessing_SemLock__get_value_impl
        private static PyObject _multiprocessing_SemLock__get_value_impl(SemLockObject self)
        {
            int sval = _GetSemaphoreValue(self.Semaphore);
            if (sval < 0)
                sval = 0;
            return PyInt.From(sval);
        }

        // _multiprocessing_SemLock__is_zero_impl
        private static PyObject _multiprocessing_SemLock__is_zero_impl(SemLockObject self) =>
            PyBool.FromBool(_GetSemaphoreValue(self.Semaphore) == 0);

        // _multiprocessing_SemLock__after_fork_impl
        private static PyObject _multiprocessing_SemLock__after_fork_impl(SemLockObject self)
        {
            self.count = 0;
            return PyNone.Instance;
        }

        // _multiprocessing_SemLock___enter___impl
        private static PyObject _multiprocessing_SemLock___enter___impl(SemLockObject self) =>
            _multiprocessing_SemLock_acquire_impl(self, true, PyNone.Instance);

        // _multiprocessing_SemLock___exit___impl
        private static PyObject _multiprocessing_SemLock___exit___impl(SemLockObject self) =>
            _multiprocessing_SemLock_release_impl(self);

        private static SemLockObject S(PyObject o) => (SemLockObject)o;

        // The "s" converter: a str without embedded NUL.
        private static string CString(string fname, string displayname, PyObject arg)
        {
            if (arg is not PyString text)
                throw PyArgs.BadArgument(fname, displayname, "str", arg);
            if (text.Value.IndexOf('\0') >= 0)
                throw PyValueError.Create("embedded null character");
            return text.Value;
        }

        // _PyMp_SemLockType_spec
        private static PyType MakeSemLockType()
        {
            var type = new PyType("SemLock", new[] { PyType.ObjectType }, "_multiprocessing");
            type.MarkSpecHeapType();
            type.MarkImmutableType();
            type.SetStaticLayout(new TypeLayout(48, 0, 0, 0, true, Spec: true));
            type.TypeDict["__module__"] = PyString.From("_multiprocessing");
            // _multiprocessing_SemLock: (kind, value, maxvalue, name, unlink)
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) =>
            {
                var a = PyArgs.Unpack("SemLock", args, kwargs, new[] { "kind", "value", "maxvalue", "name", "unlink" }, 0, 5, 5);
                int kind = PyArgs.Int(a[0]), value = PyArgs.Int(a[1]), maxvalue = PyArgs.Int(a[2]);
                string name = CString("SemLock", "argument 'name'", a[3]);
                bool unlink = a[4].IsTrue();
                return _multiprocessing_SemLock_impl((PyType)cls, kind, value, maxvalue, name, unlink);
            }, 0, int.MaxValue, acceptsKwargs: true);
            TypeTables.GenericAttributes(type);
            TypeTables.GenericSetAttributes(type);
            // semlock_methods
            type.TypeDict["acquire"] = new PyMethodDescriptor("acquire", type, (self, args, kwargs) =>
            {
                var a = PyArgs.Unpack("acquire", args, kwargs, new[] { "block", "timeout" }, 0, 0, 2);
                bool blocking = a[0] == null || a[0].IsTrue();
                return _multiprocessing_SemLock_acquire_impl(S(self), blocking, a[1] ?? PyNone.Instance);
            }, acceptsKwargs: true);
            PyArgs.Define(type, "release", PyCall.NoArgs, (self, args) => _multiprocessing_SemLock_release_impl(S(self)));
            PyArgs.Define(type, "__enter__", PyCall.NoArgs, (self, args) => _multiprocessing_SemLock___enter___impl(S(self)));
            PyArgs.Define(type, "__exit__", PyCall.Fast, (self, args) => _multiprocessing_SemLock___exit___impl(S(self)), 0, 3);
            PyArgs.Define(type, "_count", PyCall.NoArgs, (self, args) => _multiprocessing_SemLock__count_impl(S(self)));
            PyArgs.Define(type, "_is_mine", PyCall.NoArgs, (self, args) => _multiprocessing_SemLock__is_mine_impl(S(self)));
            PyArgs.Define(type, "_get_value", PyCall.NoArgs, (self, args) => _multiprocessing_SemLock__get_value_impl(S(self)));
            PyArgs.Define(type, "_is_zero", PyCall.NoArgs, (self, args) => _multiprocessing_SemLock__is_zero_impl(S(self)));
            // @classmethod _rebuild(handle, kind, maxvalue, name, /)
            type.TypeDict["_rebuild"] = new PyClassMethodDescriptor("_rebuild", type, new PyBuiltinFunction("_rebuild", (all, kwargs) =>
            {
                var cls = all[0];
                var args = all[1..];
                PyArgs.Positional("_rebuild", args, kwargs, 4, 4);
                ulong handle = AsHandle(args[0]);
                int kind = PyArgs.Int(args[1]), maxvalue = PyArgs.Int(args[2]);
                string name = args[3] is PyNone ? null : CString("_rebuild", "argument 4", args[3]);
                return _multiprocessing_SemLock__rebuild_impl((PyType)cls, handle, kind, maxvalue, name);
            }));
            PyArgs.Define(type, "_after_fork", PyCall.NoArgs, (self, args) => _multiprocessing_SemLock__after_fork_impl(S(self)));
            // semlock_members
            type.TypeDict["handle"] = new PyMemberDescriptor("handle", type, self => PyInt.From(new System.Numerics.BigInteger(S(self).handle)), readOnly: true);
            type.TypeDict["kind"] = new PyMemberDescriptor("kind", type, self => PyInt.From(S(self).kind), readOnly: true);
            type.TypeDict["maxvalue"] = new PyMemberDescriptor("maxvalue", type, self => PyInt.From(S(self).maxvalue), readOnly: true);
            type.TypeDict["name"] = new PyMemberDescriptor("name", type,
                self => S(self).name == null ? PyNone.Instance : PyString.From(S(self).name), readOnly: true);
            type.TypeDict["SEM_VALUE_MAX"] = PyInt.From(SEM_VALUE_MAX);
            return type;
        }

        /// <summary>PyInit__multiprocessing and multiprocessing_exec.</summary>
        internal static PyModule Create()
        {
            var module = new PyModule("_multiprocessing");
            var d = module.ModuleDict;
            // module_methods
            d["closesocket"] = PyArgs.MethO("_multiprocessing", "closesocket", a => _multiprocessing_closesocket_impl(AsHandle(a[0])));
            d["recv"] = PyArgs.FastCall("_multiprocessing", "recv", 2, 2, a => _multiprocessing_recv_impl(AsHandle(a[0]), PyArgs.Int(a[1])));
            d["send"] = PyArgs.FastCall("_multiprocessing", "send", 2, 2, a => _multiprocessing_send_impl(AsHandle(a[0]), BytesMethods.RequireBuffer(a[1])));
            d["sem_unlink"] = PyArgs.MethO("_multiprocessing", "sem_unlink", a => _multiprocessing_sem_unlink_impl(CString("sem_unlink", "argument", a[0])));
            SemLockType ??= MakeSemLockType();
            d["SemLock"] = SemLockType;
            /* Add configuration macros: none of them is defined on Windows. */
            d["flags"] = new PyDict();
            return module;
        }
    }

    /// <summary>SemLockObject</summary>
    internal sealed class SemLockObject : PyObject
    {
        private readonly PyType _type;
        internal ulong handle;
        internal ulong last_tid;
        internal int count;
        internal int maxvalue;
        internal int kind;
        internal string name;

        internal SemLockObject(PyType type, PyType semLockType)
        {
            _type = type;
            if (!ReferenceEquals(type, semLockType))
                SetCustomType(type);
        }

        public override PyType GetPyType() => _customType ?? _type;

        internal Semaphore Semaphore =>
            ManagedHandles.Get<Semaphore>((long)handle) ?? throw PyOSError.CreateWindowsFileError(6, null);
    }
}
