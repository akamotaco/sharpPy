using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace SharpPy.Modules
{
    /// <summary>
    /// The SOCKET values of _socket: CPython on Windows hands out the Winsock handle as fileno(),
    /// and every function that takes a descriptor (socket(fileno=), _socket.close, _socket.dup,
    /// select.select) uses it. Here the value is a number of this process-wide table, which maps it
    /// to the managed Socket: Socket.Handle is the OS handle on Windows but reading it on Unix
    /// changes how .NET drives the socket, and .NET cannot duplicate a handle within the process
    /// (WSADuplicateSocket only through DuplicateAndClose, which closes the original). So a number
    /// is a SharpPy number, as SOCKETs look (multiples of 4 from 0x100), never reused; dup() gives
    /// a new number for the same Socket, which is closed when its last number is (as a duplicated
    /// handle keeps the socket open). A number no socket has is WSAENOTSOCK, as an invalid handle is.
    /// </summary>
    internal static class SocketHandles
    {
        private sealed class Shared
        {
            internal readonly Socket Socket;
            internal int Count = 1;
            internal Shared(Socket socket) => Socket = socket;
        }

        private static readonly Dictionary<long, Shared> Table = new();
        private static long _next = 0x100 - 4;

        internal static long Add(Socket socket)
        {
            lock (Table)
            {
                _next += 4;
                Table[_next] = new Shared(socket);
                return _next;
            }
        }

        internal static Socket Get(long fd)
        {
            lock (Table)
                return Table.TryGetValue(fd, out var shared) ? shared.Socket : null;
        }

        /// <summary>closesocket: 0, or WSAENOTSOCK for a number that is not open.</summary>
        internal static int Close(long fd)
        {
            Socket last = null;
            lock (Table)
            {
                if (!Table.Remove(fd, out var shared))
                    return SocketModule.WSAENOTSOCK;
                Inheritable.Remove(fd);
                if (--shared.Count == 0)
                    last = shared.Socket;
            }
            if (last != null)
            {
                try
                {
                    last.Close();
                }
                catch (SocketException error)
                {
                    return SocketModule.ErrorCodeOf(error);
                }
            }
            return 0;
        }

        // HANDLE_FLAG_INHERIT of each number: off when it is made (WSA_FLAG_NO_HANDLE_INHERIT), and
        // for a duplicate. No child process can be started here, so it is only kept and read back.
        private static readonly HashSet<long> Inheritable = new();

        internal static bool? GetInheritable(long fd)
        {
            lock (Table)
                return Table.ContainsKey(fd) ? Inheritable.Contains(fd) : null;
        }

        internal static bool SetInheritable(long fd, bool inheritable)
        {
            lock (Table)
            {
                if (!Table.ContainsKey(fd))
                    return false;
                if (inheritable)
                    Inheritable.Add(fd);
                else
                    Inheritable.Remove(fd);
                return true;
            }
        }

        /// <summary>A second number for the socket of fd, or -1.</summary>
        internal static long Dup(long fd)
        {
            lock (Table)
            {
                if (!Table.TryGetValue(fd, out var shared))
                    return -1;
                shared.Count++;
                _next += 4;
                Table[_next] = shared;
                return _next;
            }
        }
    }

    /// <summary>
    /// SOCKADDR_HV (hvsocket.h): Family, Reserved, VmId, ServiceId. .NET has no Hyper-V endpoint;
    /// an EndPoint serializes to the raw sockaddr that Socket.Bind/Connect pass to Winsock.
    /// </summary>
    internal sealed class HyperVEndPoint : EndPoint
    {
        private const int Size = 36;
        internal readonly Guid VmId, ServiceId;

        internal HyperVEndPoint(Guid vmId, Guid serviceId)
        {
            VmId = vmId;
            ServiceId = serviceId;
        }

        public override AddressFamily AddressFamily => (AddressFamily)SocketModule.AF_HYPERV;

        public override SocketAddress Serialize()
        {
            var address = new SocketAddress(AddressFamily, Size);
            var vm = VmId.ToByteArray();
            var service = ServiceId.ToByteArray();
            for (int i = 0; i < 16; i++)
            {
                address[4 + i] = vm[i];
                address[20 + i] = service[i];
            }
            return address;
        }

        public override EndPoint Create(SocketAddress address)
        {
            var vm = new byte[16];
            var service = new byte[16];
            for (int i = 0; i < 16 && 20 + i < address.Size; i++)
            {
                vm[i] = address[4 + i];
                service[i] = address[20 + i];
            }
            return new HyperVEndPoint(new Guid(vm), new Guid(service));
        }
    }

    /// <summary>PySocketSockObject: the _socket.socket type (sock_spec), subclassable.</summary>
    internal sealed class PySocketObject : PyObject
    {
        internal const long InvalidSocket = -1;

        // sock_fd, sock_family, sock_type, sock_proto, sock_timeout
        internal long Fd = InvalidSocket;
        internal int Family, Type, Proto;
        internal long Timeout = -SocketModule.SecondNs;

        private PySocketObject(PyType type)
        {
            if (!ReferenceEquals(type, SocketModule.SocketType))
                SetCustomType(type);
        }

        public override PyType GetPyType() => _customType ?? SocketModule.SocketType;

        /// <summary>
        /// A plain _socket.socket has no __del__ hook of SharpPy's (that is for classes): when the
        /// collector takes one that is still open, its socket is closed, as sock_dealloc's
        /// finalizer does, without the warning.
        /// </summary>
        ~PySocketObject()
        {
            if (_customType == null && Fd != InvalidSocket)
            {
                long fd = Fd;
                Fd = InvalidSocket;
                try { SocketHandles.Close(fd); } catch (Exception) { }
            }
        }

        internal Socket Sock => SocketHandles.Get(Fd);

        internal static PyType MakeType()
        {
            // sock_spec: "_socket.socket", Py_TPFLAGS_BASETYPE | Py_TPFLAGS_IMMUTABLETYPE
            var type = new PyType("socket", new[] { PyType.ObjectType }, "_socket");
            type.SetSpecLayout(new TypeLayout(64, 0, 0, 0, true));
            type.MarkSpecHeapType();
            type.MarkImmutableType();
            type.TypeDict["__module__"] = PyString.From("_socket");
            // sock_new
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type,
                (cls, args, kwargs) => New((PyType)cls), 0, int.MaxValue, acceptsKwargs: true);
            // sock_initobj
            type.TypeDict["__init__"] = new PyWrapperDescriptor("__init__", type, (self, args, kwargs) =>
            {
                var a = PyArgs.Unpack("socket", args, kwargs, new[] { "family", "type", "proto", "fileno" }, 0, 0, 4);
                int family = a[0] == null ? -1 : PyArgs.Int(a[0]);
                int type_ = a[1] == null ? -1 : PyArgs.Int(a[1]);
                int proto = a[2] == null ? -1 : PyArgs.Int(a[2]);
                ((PySocketObject)self).Init(family, type_, proto, a[3]);
                return PyNone.Instance;
            });
            // sock_repr, sock_finalize
            type.TypeDict["__repr__"] = new PyWrapperDescriptor("__repr__", type,
                (self, args, kwargs) => ((PySocketObject)self).Repr(), minArgs: 0, maxArgs: 0);
            type.TypeDict["__del__"] = new PyWrapperDescriptor("__del__", type,
                (self, args, kwargs) => { ((PySocketObject)self).SockFinalize(); return PyNone.Instance; }, minArgs: 0, maxArgs: 0);

            // sock_methods
            PyArgs.Define(type, "_accept", PyCall.NoArgs, (self, args) => Of(self).Accept());
            PyArgs.Define(type, "bind", PyCall.One, (self, args) => Of(self).Bind(args[0]));
            PyArgs.Define(type, "close", PyCall.NoArgs, (self, args) => Of(self).CloseMethod());
            PyArgs.Define(type, "connect", PyCall.One, (self, args) => Of(self).Connect(args[0]));
            PyArgs.Define(type, "connect_ex", PyCall.One, (self, args) => Of(self).ConnectEx(args[0]));
            PyArgs.Define(type, "detach", PyCall.NoArgs, (self, args) => Of(self).Detach());
            PyArgs.Define(type, "fileno", PyCall.NoArgs, (self, args) => PyInt.From(Of(self).Fd));
            PyArgs.Define(type, "getpeername", PyCall.NoArgs, (self, args) => Of(self).GetPeerName());
            PyArgs.Define(type, "getsockname", PyCall.NoArgs, (self, args) => Of(self).GetSockName());
            VarArgs(type, "getsockopt", (self, args) => Of(self).GetSockOpt(args));
            VarArgs(type, "ioctl", (self, args) => Of(self).Ioctl(args));
            VarArgs(type, "share", (self, args) => Of(self).Share(args));
            VarArgs(type, "listen", (self, args) => Of(self).Listen(args));
            VarArgs(type, "recv", (self, args) => Of(self).Recv(args));
            Keywords(type, "recv_into", (self, args, kwargs) => Of(self).RecvInto(args, kwargs));
            VarArgs(type, "recvfrom", (self, args) => Of(self).RecvFrom(args));
            Keywords(type, "recvfrom_into", (self, args, kwargs) => Of(self).RecvFromInto(args, kwargs));
            VarArgs(type, "send", (self, args) => Of(self).Send(args));
            VarArgs(type, "sendall", (self, args) => Of(self).SendAll(args));
            VarArgs(type, "sendto", (self, args) => Of(self).SendTo(args));
            PyArgs.Define(type, "setblocking", PyCall.One, (self, args) => Of(self).SetBlocking(args[0]));
            PyArgs.Define(type, "getblocking", PyCall.NoArgs, (self, args) => PyBool.FromBool(Of(self).Timeout != 0));
            PyArgs.Define(type, "settimeout", PyCall.One, (self, args) => Of(self).SetTimeout(args[0]));
            PyArgs.Define(type, "gettimeout", PyCall.NoArgs, (self, args) => SocketModule.TimeoutObject(Of(self).Timeout));
            VarArgs(type, "setsockopt", (self, args) => Of(self).SetSockOpt(args));
            PyArgs.Define(type, "shutdown", PyCall.One, (self, args) => Of(self).Shutdown(args[0]));

            // sock_memberlist (T_INT, READONLY) and sock_getsetlist
            type.TypeDict["family"] = new PyMemberDescriptor("family", type, self => PyInt.From(Of(self).Family), readOnly: true);
            type.TypeDict["type"] = new PyMemberDescriptor("type", type, self => PyInt.From(Of(self).Type), readOnly: true);
            type.TypeDict["proto"] = new PyMemberDescriptor("proto", type, self => PyInt.From(Of(self).Proto), readOnly: true);
            type.TypeDict["timeout"] = new PyGetSetDescriptor("timeout", type, self => SocketModule.TimeoutObject(Of(self).Timeout), doc: "the socket timeout");
            if (PyDocs.Lookup("_socket.socket") is { } doc)
                type.TypeDict["__doc__"] = PyDocs.DocFromInternal("socket", doc);
            return type;
        }

        private static PySocketObject Of(PyObject self) => (PySocketObject)self;

        // METH_VARARGS: the parse takes no keywords ("recv() takes no keyword arguments").
        private static void VarArgs(PyType type, string name, Func<PyObject, PyObject[], PyObject> body) =>
            type.TypeDict[name] = new PyMethodDescriptor(name, type, (self, args, kwargs) =>
            {
                if (kwargs != null && kwargs.Length() != 0)
                    throw PyTypeError.Create($"{name}() takes no keyword arguments");
                return body(self, args);
            }, minArgs: 0, maxArgs: int.MaxValue, acceptsKwargs: true);

        // METH_VARARGS | METH_KEYWORDS
        private static void Keywords(PyType type, string name, Func<PyObject, PyObject[], PyDict, PyObject> body) =>
            type.TypeDict[name] = new PyMethodDescriptor(name, type, body, minArgs: 0, maxArgs: int.MaxValue, acceptsKwargs: true);

        // sock_new: an unopened socket, blocking, until __init__.
        private static PyObject New(PyType type) => new PySocketObject(type);

        // ---- sock_initobj ----

        private const int ProtocolInfoSize = 628; // sizeof(WSAPROTOCOL_INFOW)

        // sock_initobj_impl
        private void Init(int family, int type, int proto, PyObject fdobj)
        {
            long fd;
            if (fdobj != null && fdobj is not PyNone)
            {
                if (fdobj is PyBytes shared)
                {
                    // recreate a socket that was duplicated: WSASocketW(FROM_PROTOCOL_INFO).
                    var info = shared.Value;
                    if (info.Length != ProtocolInfoSize)
                        throw PyValueError.Create($"socket descriptor string has wrong size, should be {ProtocolInfoSize} bytes.");
                    Socket created;
                    try
                    {
                        created = SocketModule.PyGilCall(() =>
                        {
                            var fromInfo = new Socket(new SocketInformation { ProtocolInformation = (byte[])info.Clone() });
                            // That Socket knows nothing of the shared socket's state (listening,
                            // connected); one made over its handle asks Winsock, and takes it over.
                            var handle = fromInfo.Handle;
                            var adopted = new Socket(new SafeSocketHandle(handle, ownsHandle: true));
                            fromInfo.SafeHandle.SetHandleAsInvalid();
                            return adopted;
                        });
                    }
                    catch (SocketException error)
                    {
                        throw SocketModule.SetError(SocketModule.ErrorCodeOf(error));
                    }
                    catch (PlatformNotSupportedException)
                    {
                        throw SocketModule.SetError(SocketModule.WSAEOPNOTSUPP);
                    }
                    fd = SocketHandles.Add(created);
                    family = (int)created.AddressFamily;
                    type = (int)created.SocketType;
                    proto = (int)created.ProtocolType;
                }
                else
                {
                    fd = SocketModule.AsSocket(fdobj);
                    if (fd == -1)
                        throw PyValueError.Create("negative file descriptor");
                    // validate that passed file descriptor is valid and a socket.
                    var sock = SocketHandles.Get(fd);
                    if (sock == null)
                        throw SocketModule.SetError(SocketModule.WSAENOTSOCK);
                    // getsockname() on an unbound socket is an error on Windows.
                    if (sock.LocalEndPoint is { } local)
                    {
                        if (family == -1)
                            family = (int)local.AddressFamily;
                    }
                    else if (family == -1)
                    {
                        throw SocketModule.SetError(SocketModule.WSAEINVAL);
                    }
                    // SO_TYPE
                    if (type == -1)
                        type = (int)sock.SocketType;
                    // No SO_PROTOCOL on Windows.
                    proto = 0;
                }
            }
            else
            {
                // No fd, default to AF_INET and SOCK_STREAM
                if (family == -1)
                    family = SocketModule.AF_INET;
                if (type == -1)
                    type = SocketModule.SOCK_STREAM;
                if (proto == -1)
                    proto = 0;
                Socket created;
                try
                {
                    created = SocketModule.PyGilCall(() => new Socket((AddressFamily)family, (SocketType)type, (ProtocolType)proto));
                }
                catch (SocketException error)
                {
                    throw SocketModule.SetError(SocketModule.ErrorCodeOf(error));
                }
                catch (Exception error) when (error is ArgumentException or NotSupportedException)
                {
                    throw SocketModule.SetError(SocketModule.WSAEAFNOSUPPORT);
                }
                fd = SocketHandles.Add(created);
            }
            try
            {
                InitSockObject(fd, family, type, proto);
            }
            catch
            {
                SocketHandles.Close(fd);
                throw;
            }
        }

        // init_sockobject
        private void InitSockObject(long fd, int family, int type, int proto)
        {
            Fd = fd;
            Family = family;
            Type = type;
            Proto = proto;
            Timeout = SocketModule.DefaultTimeout;
            if (SocketModule.DefaultTimeout >= 0)
                InternalSetBlocking(false);
        }

        // internal_setblocking: FIONBIO.
        private void InternalSetBlocking(bool block)
        {
            var sock = Sock;
            int code = sock == null ? SocketModule.WSAENOTSOCK : Run(s => { s.Blocking = block; return 0; }, sock);
            if (code != 0)
                throw SocketModule.SetError(code);
        }

        // ---- sock_call ----

        /// <summary>The body of a socket call: 0 when it worked, else the WSA error.</summary>
        private delegate int SockFunc(Socket sock);

        /// <summary>A .NET call as the Winsock call it makes: its error code instead of an exception.</summary>
        private static int Run(SockFunc func, Socket sock)
        {
            try
            {
                return func(sock);
            }
            catch (SocketException error)
            {
                return SocketModule.ErrorCodeOf(error);
            }
            catch (ObjectDisposedException)
            {
                // The socket was closed through another descriptor: an invalid handle.
                return SocketModule.WSAENOTSOCK;
            }
            catch (InvalidOperationException)
            {
                // .NET's own state checks (listening, not bound) stand where Winsock says WSAEINVAL.
                return SocketModule.WSAEINVAL;
            }
            catch (PlatformNotSupportedException)
            {
                return SocketModule.WSAEOPNOTSUPP;
            }
        }

        private static int CodeOf(SocketError error) => (int)error;

        // PyErr_SetString(PyExc_TimeoutError, "timed out")
        private static PythonException TimedOut() =>
            new((PyBaseException)PyType.TimeoutErrorType.Call(new PyObject[] { PyString.From("timed out") }, null));

        /// <summary>
        /// internal_select: wait for the socket to be readable (or writable), and with connect for
        /// the error set too. 0 ready, 1 timed out, -1 failed (error set).
        /// </summary>
        private int InternalSelect(bool writing, long interval, bool connect, out int error)
        {
            error = 0;
            // Guard against closed socket
            var sock = Sock;
            if (sock == null)
                return 0;
            long deadline = interval >= 0 ? PyTime.GetMonotonicClock() + interval : 0;
            while (true)
            {
                int micro;
                bool more = false;
                if (interval >= 0)
                {
                    long remaining = deadline - PyTime.GetMonotonicClock();
                    if (remaining < 0)
                        remaining = 0;
                    long us = PyTime.AsMicroseconds(remaining, PyTimeRound.Ceiling);
                    // Socket.Select counts in an int; a longer wait is made of several.
                    if (us > int.MaxValue)
                    {
                        us = int.MaxValue;
                        more = true;
                    }
                    micro = (int)us;
                }
                else
                    micro = -1;
                var fds = new List<Socket> { sock };
                var efds = connect ? new List<Socket> { sock } : null;
                int code = 0;
                PyGil.AllowThreads(() => code = Run(s =>
                {
                    if (writing)
                        Socket.Select(null, fds, efds, micro);
                    else
                        Socket.Select(fds, null, efds, micro);
                    return 0;
                }, sock));
                if (code != 0)
                {
                    error = code;
                    return -1;
                }
                if (fds.Count + (efds?.Count ?? 0) > 0)
                    return 0;
                if (!more)
                    return 1;
            }
        }

        /// <summary>
        /// sock_call_ex. With raise, an error is raised; without it the WSA code is returned (and 0
        /// for success). A socket with a timeout waits in select before the call, and retries the
        /// call when it would block; WSAEINTR retries it (no signal handler can run here).
        /// </summary>
        private int SockCallEx(bool writing, SockFunc func, bool connect, bool raise, long timeout)
        {
            bool hasTimeout = timeout > 0;
            long deadline = 0;
            bool deadlineInitialized = false;
            while (true)
            {
                // For connect(), poll even for blocking socket. The connection runs asynchronously.
                if (hasTimeout || connect)
                {
                    int res, selectError = 0;
                    if (hasTimeout)
                    {
                        long interval;
                        if (deadlineInitialized)
                        {
                            // recompute the timeout
                            interval = deadline - PyTime.GetMonotonicClock();
                        }
                        else
                        {
                            deadlineInitialized = true;
                            deadline = PyTime.GetMonotonicClock() + timeout;
                            interval = timeout;
                        }
                        res = interval >= 0 ? InternalSelect(writing, interval, connect, out selectError) : 1;
                    }
                    else
                    {
                        res = InternalSelect(writing, timeout, connect, out selectError);
                    }
                    if (res == -1)
                    {
                        if (selectError == SocketModule.WSAEINTR)
                            continue;
                        // select() failed
                        if (raise)
                            throw SocketModule.SetError(selectError);
                        return selectError;
                    }
                    if (res == 1)
                    {
                        if (raise)
                            throw TimedOut();
                        return SocketModule.WSAEWOULDBLOCK;
                    }
                    // the socket is ready
                }
                int code;
                // inner loop to retry sock_func() when sock_func() is interrupted by a signal
                while (true)
                {
                    var sock = Sock;
                    code = 0;
                    if (sock == null)
                        code = SocketModule.WSAENOTSOCK;
                    else
                        PyGil.AllowThreads(() => code = Run(func, sock));
                    if (code == 0)
                        return 0;
                    if (code != SocketModule.WSAEINTR)
                        break;
                }
                if (Timeout > 0 && code == SocketModule.WSAEWOULDBLOCK)
                {
                    // False positive: select() said ready, the call would block. Loop on select().
                    continue;
                }
                // sock_func() failed
                if (raise)
                    throw SocketModule.SetError(code);
                return code;
            }
        }

        // sock_call
        private void SockCall(bool writing, SockFunc func) => SockCallEx(writing, func, false, true, Timeout);

        // ---- addresses ----

        /// <summary>
        /// getsockaddrarg: the address argument for the socket's family. The host goes through
        /// idna_converter and setipaddr; the port is checked after the host is resolved.
        /// </summary>
        private EndPoint GetSockAddrArg(PyObject args, string caller)
        {
            switch (Family)
            {
                case SocketModule.AF_INET:
                {
                    if (args is not PyTuple tuple)
                        throw PyTypeError.Create($"{caller}(): AF_INET address must be tuple, not {args.GetPyType().FullName}");
                    if (tuple.Items.Length != 2)
                        throw PyTypeError.Create("AF_INET address must be a pair (host, port)");
                    var host = IdnaConverter(tuple.Items[0]);
                    int port = PortArgument(tuple.Items[1], caller);
                    var address = SocketModule.SetIpAddr(host, SocketModule.AF_INET);
                    if (port < 0 || port > 0xffff)
                        throw PyOverflowError.Create($"{caller}(): port must be 0-65535.");
                    return new IPEndPoint(address, port);
                }
                case SocketModule.AF_INET6:
                {
                    if (args is not PyTuple tuple)
                        throw PyTypeError.Create($"{caller}(): AF_INET6 address must be tuple, not {args.GetPyType().FullName}");
                    var items = tuple.Items;
                    if (items.Length < 2 || items.Length > 4)
                        throw PyTypeError.Create("AF_INET6 address must be a tuple (host, port[, flowinfo[, scopeid]])");
                    var host = IdnaConverter(items[0]);
                    int port = PortArgument(items[1], caller);
                    uint flowinfo = items.Length > 2 ? PyArgs.UnsignedIntMask(items[2]) : 0;
                    uint scopeId = items.Length > 3 ? PyArgs.UnsignedIntMask(items[3]) : 0;
                    var address = SocketModule.SetIpAddr(host, SocketModule.AF_INET6);
                    if (port < 0 || port > 0xffff)
                        throw PyOverflowError.Create($"{caller}(): port must be 0-65535.");
                    if (flowinfo > 0xfffff)
                        throw PyOverflowError.Create($"{caller}(): flowinfo must be 0-1048575.");
                    if (flowinfo != 0)
                        throw SocketModule.FlowInfoUnsupported(caller);
                    return new IPEndPoint(new IPAddress(address.GetAddressBytes(), scopeId), port);
                }
                case SocketModule.AF_HYPERV:
                {
                    switch (Proto)
                    {
                        case SocketModule.HV_PROTOCOL_RAW:
                        {
                            if (args is not PyTuple tuple)
                                throw PyTypeError.Create($"{caller}(): AF_HYPERV address must be tuple, not {args.GetPyType().FullName}");
                            // "UU;AF_HYPERV address must be a str tuple (vm_id, service_id)"
                            if (tuple.Items.Length != 2 || tuple.Items[0] is not PyString vmId || tuple.Items[1] is not PyString serviceId)
                                throw PyTypeError.Create("AF_HYPERV address must be a str tuple (vm_id, service_id)");
                            // UuidFromStringW: the 36-character form, no braces.
                            if (!Guid.TryParseExact(vmId.Value, "D", out var vm))
                                throw PyValueError.Create($"{caller}(): AF_HYPERV address vm_id is not a valid UUID string");
                            if (!Guid.TryParseExact(serviceId.Value, "D", out var service))
                                throw PyValueError.Create($"{caller}(): AF_HYPERV address service_id is not a valid UUID string");
                            return new HyperVEndPoint(vm, service);
                        }
                        default:
                            throw SocketModule.OSErrorText($"{caller}(): unsupported AF_HYPERV protocol: {Proto}");
                    }
                }
                case SocketModule.AF_BLUETOOTH:
                    throw SocketModule.OSErrorText($"{caller}(): AF_BLUETOOTH addresses are not supported (no Bluetooth socket endpoint in .NET)");
                default:
                    throw SocketModule.OSErrorText($"{caller}(): bad family");
            }
        }

        // The "i" of the address tuple: its OverflowError is the port message.
        private static int PortArgument(PyObject value, string caller)
        {
            try
            {
                return PyArgs.ParseInt(value);
            }
            catch (PythonException error) when (error.PyException is PyOverflowError)
            {
                throw PyOverflowError.Create($"{caller}(): port must be 0-65535.");
            }
        }

        /// <summary>idna_converter: ASCII str as it is, other str through "idna", bytes, bytearray.</summary>
        private static string IdnaConverter(PyObject obj)
        {
            byte[] bytes;
            if (obj is PyBytes raw)
                bytes = raw.Value;
            else if (obj is PyByteArray array)
                bytes = BytesMethods.RequireBuffer(array);
            else if (obj is PyString text)
            {
                bool ascii = true;
                foreach (char c in text.Value)
                    if (c > 0x7f) { ascii = false; break; }
                if (ascii)
                    bytes = System.Text.Encoding.ASCII.GetBytes(text.Value);
                else
                {
                    try
                    {
                        bytes = text.Encode("idna").Value;
                    }
                    catch (PythonException)
                    {
                        throw PyTypeError.Create("encoding of hostname failed");
                    }
                }
            }
            else
                throw PyTypeError.Create($"str, bytes or bytearray expected, not {obj.GetPyType().FullName}");
            if (Array.IndexOf(bytes, (byte)0) >= 0)
                throw PyTypeError.Create("host name must not contain null character");
            return System.Text.Encoding.Latin1.GetString(bytes);
        }

        // getsockaddrlen: the families with an address format.
        private void GetSockAddrLen()
        {
            if (Family != SocketModule.AF_INET && Family != SocketModule.AF_INET6
                && Family != SocketModule.AF_HYPERV && Family != SocketModule.AF_BLUETOOTH)
                throw SocketModule.OSErrorText("getsockaddrlen: bad family");
        }

        // ---- methods ----

        // sock_accept: s._accept() -> (fd, address)
        private PyObject Accept()
        {
            GetSockAddrLen();
            Socket accepted = null;
            SockCall(false, sock =>
            {
                accepted = sock.Accept();
                // An accepted socket has the listening socket's properties, its blocking mode too.
                accepted.Blocking = sock.Blocking;
                return 0;
            });
            long newfd = SocketHandles.Add(accepted);
            PyObject addr;
            try
            {
                addr = SocketModule.MakeSockAddr(accepted.RemoteEndPoint);
            }
            catch (SocketException)
            {
                addr = PyNone.Instance;
            }
            return PyTuple.From(PyInt.From(newfd), addr);
        }

        // sock_setblocking
        private PyObject SetBlocking(PyObject arg)
        {
            bool block = arg.IsTrue();
            Timeout = block ? -SocketModule.SecondNs : 0;
            InternalSetBlocking(block);
            return PyNone.Instance;
        }

        // sock_settimeout
        private PyObject SetTimeout(PyObject arg)
        {
            long timeout = SocketModule.ParseTimeout(arg);
            Timeout = timeout;
            InternalSetBlocking(timeout < 0);
            return PyNone.Instance;
        }

        // sock_setsockopt
        private PyObject SetSockOpt(PyObject[] args)
        {
            int level, optname, res;
            // setsockopt(level, opt, flag)
            if (TryParse(() => { PyArgs.ParseTuple("setsockopt", args, 3, 3); return (PyArgs.ParseInt(args[0]), PyArgs.ParseInt(args[1]), PyArgs.ParseInt(args[2])); }, out var asInt))
            {
                (level, optname, int flag) = asInt;
                res = RawSetSockOpt(level, optname, BitConverter.GetBytes(flag));
            }
            // setsockopt(level, opt, None, flag)
            else if (TryParse(() =>
            {
                PyArgs.ParseTuple("setsockopt", args, 4, 4);
                int l = PyArgs.ParseInt(args[0]), o = PyArgs.ParseInt(args[1]);
                if (args[2] is not PyNone)
                    throw PyTypeError.Create("must be NoneType");
                return (l, o, PyArgs.UnsignedIntMask(args[3]));
            }, out var asNone))
            {
                (level, optname, uint optlen) = asNone;
                // setsockopt(fd, level, optname, NULL, optlen): a NULL value with a length is WSAEFAULT.
                res = optlen == 0 ? RawSetSockOpt(level, optname, Array.Empty<byte>()) : SocketModule.WSAEFAULT;
            }
            else
            {
                // setsockopt(level, opt, buffer)
                PyArgs.ParseTuple("setsockopt", args, 3, 3);
                level = PyArgs.ParseInt(args[0]);
                optname = PyArgs.ParseInt(args[1]);
                var optval = BytesMethods.RequireBuffer(args[2]);
                res = RawSetSockOpt(level, optname, optval);
            }
            if (res != 0)
                throw SocketModule.SetError(res);
            return PyNone.Instance;
        }

        private static bool TryParse<T>(Func<T> parse, out T value)
        {
            try
            {
                value = parse();
                return true;
            }
            catch (PythonException)
            {
                value = default;
                return false;
            }
        }

        /// <summary>
        /// setsockopt: Winsock's level and name as they are on Windows (SetRawSocketOption);
        /// elsewhere through .NET's option enums, whose values are Winsock's and which .NET
        /// translates to the platform's.
        /// </summary>
        private int RawSetSockOpt(int level, int optname, byte[] value)
        {
            var sock = Sock;
            if (sock == null)
                return SocketModule.WSAENOTSOCK;
            return Run(s =>
            {
                if (OperatingSystem.IsWindows())
                    s.SetRawSocketOption(level, optname, value);
                else
                    s.SetSocketOption((SocketOptionLevel)level, (SocketOptionName)optname, value);
                return 0;
            }, sock);
        }

        private int RawGetSockOpt(int level, int optname, byte[] buffer, out int length)
        {
            length = 0;
            var sock = Sock;
            if (sock == null)
                return SocketModule.WSAENOTSOCK;
            int got = 0;
            int code = Run(s =>
            {
                if (OperatingSystem.IsWindows())
                    got = s.GetRawSocketOption(level, optname, buffer);
                else
                {
                    s.GetSocketOption((SocketOptionLevel)level, (SocketOptionName)optname, buffer);
                    got = buffer.Length;
                }
                return 0;
            }, sock);
            length = got;
            return code;
        }

        // sock_getsockopt: "ii|i"
        private PyObject GetSockOpt(PyObject[] args)
        {
            PyArgs.ParseTuple("getsockopt", args, 2, 3);
            int level = PyArgs.ParseInt(args[0]);
            int optname = PyArgs.ParseInt(args[1]);
            int buflen = args.Length > 2 ? PyArgs.ParseInt(args[2]) : 0;
            if (buflen == 0)
            {
                var flag = new byte[4];
                int res = RawGetSockOpt(level, optname, flag, out _);
                if (res != 0)
                    throw SocketModule.SetError(res);
                return PyInt.From(BitConverter.ToInt32(flag, 0));
            }
            if (buflen <= 0 || buflen > 1024)
                throw SocketModule.OSErrorText("getsockopt buflen out of range");
            var buf = new byte[buflen];
            int code = RawGetSockOpt(level, optname, buf, out int length);
            if (code != 0)
                throw SocketModule.SetError(code);
            if (length != buflen)
                Array.Resize(ref buf, Math.Max(0, Math.Min(length, buflen)));
            return PyBytes.From(buf);
        }

        // sock_bind
        private PyObject Bind(PyObject addro)
        {
            var addr = GetSockAddrArg(addro, "bind");
            var sock = Sock;
            int res = sock == null ? SocketModule.WSAENOTSOCK : 0;
            if (sock != null)
                PyGil.AllowThreads(() => res = Run(s => { s.Bind(addr); return 0; }, sock));
            if (res != 0)
                throw SocketModule.SetError(res);
            return PyNone.Instance;
        }

        // sock_close: the descriptor goes to -1 first; ECONNRESET on close is ignored.
        private PyObject CloseMethod()
        {
            long fd = Fd;
            if (fd != InvalidSocket)
            {
                Fd = InvalidSocket;
                int res = SocketModule.PyGilCall(() => SocketHandles.Close(fd));
                if (res != 0 && res != 10054)
                    throw SocketModule.SetError(res);
            }
            return PyNone.Instance;
        }

        // sock_detach
        private PyObject Detach()
        {
            long fd = Fd;
            Fd = InvalidSocket;
            return PyInt.From(fd);
        }

        // sock_connect_impl: the outcome of the connection, from SO_ERROR.
        private static int ConnectImpl(Socket sock)
        {
            int err = (int)sock.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Error);
            return err;
        }

        // internal_connect
        private int InternalConnect(EndPoint addr, bool raise)
        {
            var sock = Sock;
            int res = sock == null ? SocketModule.WSAENOTSOCK : 0;
            if (sock != null)
                PyGil.AllowThreads(() => res = Run(s => { s.Connect(addr); return 0; }, sock));
            if (res == 0)
                return 0;
            // connect() failed
            int err = res;
            bool waitConnect;
            if (err == SocketModule.WSAEINTR)
                waitConnect = Timeout != 0;
            else
                waitConnect = Timeout > 0 && err == SocketModule.WSAEWOULDBLOCK;
            if (!waitConnect)
            {
                if (raise)
                    throw SocketModule.SetError(err);
                return err;
            }
            // socket.connect() raises an exception on error; connect_ex() returns the error code.
            return SockCallEx(true, ConnectImpl, true, raise, Timeout);
        }

        // sock_connect
        private PyObject Connect(PyObject addro)
        {
            var addr = GetSockAddrArg(addro, "connect");
            InternalConnect(addr, true);
            return PyNone.Instance;
        }

        // sock_connect_ex
        private PyObject ConnectEx(PyObject addro)
        {
            var addr = GetSockAddrArg(addro, "connect_ex");
            return PyInt.From(InternalConnect(addr, false));
        }

        // sock_getsockname
        private PyObject GetSockName()
        {
            GetSockAddrLen();
            var sock = Sock;
            EndPoint local = null;
            int res = sock == null ? SocketModule.WSAENOTSOCK : Run(s => { local = s.LocalEndPoint; return local == null ? SocketModule.WSAEINVAL : 0; }, sock);
            if (res != 0)
                throw SocketModule.SetError(res);
            return SocketModule.MakeSockAddr(local);
        }

        // sock_getpeername
        private PyObject GetPeerName()
        {
            GetSockAddrLen();
            var sock = Sock;
            EndPoint remote = null;
            int res = sock == null ? SocketModule.WSAENOTSOCK : Run(s => { remote = s.RemoteEndPoint; return remote == null ? SocketModule.WSAENOTCONN : 0; }, sock);
            if (res != 0)
                throw SocketModule.SetError(res);
            return SocketModule.MakeSockAddr(remote);
        }

        // sock_listen: "|i", SOMAXCONN capped at 128 by default, a negative backlog is 0.
        private PyObject Listen(PyObject[] args)
        {
            PyArgs.ParseTuple("listen", args, 0, 1);
            int backlog = args.Length > 0 ? PyArgs.ParseInt(args[0]) : 128;
            if (backlog < 0)
                backlog = 0;
            var sock = Sock;
            int res = sock == null ? SocketModule.WSAENOTSOCK : 0;
            if (sock != null)
                PyGil.AllowThreads(() => res = Run(s => { s.Listen(backlog); return 0; }, sock));
            if (res != 0)
                throw SocketModule.SetError(res);
            return PyNone.Instance;
        }

        // sock_recv_guts: the count read into buffer[0..len).
        private int RecvGuts(byte[] buffer, int len, int flags)
        {
            if (len == 0)
                return 0;
            int result = 0;
            SockCall(false, sock =>
            {
                result = sock.Receive(buffer, 0, len, (SocketFlags)flags, out SocketError error);
                return error == SocketError.Success ? 0 : CodeOf(error);
            });
            return result;
        }

        // sock_recv: "n|i"
        private PyObject Recv(PyObject[] args)
        {
            PyArgs.ParseTuple("recv", args, 1, 2);
            long recvlen = PyArgs.Ssize(args[0]);
            int flags = args.Length > 1 ? PyArgs.ParseInt(args[1]) : 0;
            if (recvlen < 0)
                throw PyValueError.Create("negative buffersize in recv");
            // MS_WINDOWS: at most INT_MAX at a time.
            int len = (int)Math.Min(recvlen, int.MaxValue);
            var buf = new byte[len];
            int outlen = RecvGuts(buf, len, flags);
            if (outlen != buf.Length)
                Array.Resize(ref buf, outlen);
            return PyBytes.From(buf);
        }

        // The "w*" converter of PyArg_ParseTuple: argument 1 must be a writable buffer.
        private static PyMemoryView WritableArgument(string fname, PyObject arg)
        {
            if (!arg.SupportsBuffer())
                throw PyTypeError.Create($"{fname}() argument 1 must be read-write bytes-like object, not {arg.GetPyType().FullName}");
            var view = arg.GetBuffer(0);
            if (view.ReadOnly)
            {
                view.Release();
                throw PyTypeError.Create($"{fname}() argument 1 must be read-write bytes-like object, not {arg.GetPyType().FullName}");
            }
            view.Release();
            return PyIOBaseSlots.WritableBytes(arg, fname);
        }

        private static readonly string[] IntoKeywords = { "buffer", "nbytes", "flags" };

        // sock_recv_into: "w*|ni"
        private PyObject RecvInto(PyObject[] args, PyDict kwargs)
        {
            var a = SocketModule.ParseTupleAndKeywords("recv_into", args, kwargs, IntoKeywords, 1);
            var pbuf = WritableArgument("recv_into", a[0]);
            try
            {
                long recvlen = a[1] == null ? 0 : PyArgs.Ssize(a[1]);
                int flags = a[2] == null ? 0 : PyArgs.ParseInt(a[2]);
                long buflen = pbuf.Length();
                if (recvlen < 0)
                    throw PyValueError.Create("negative buffersize in recv_into");
                if (recvlen == 0)
                    recvlen = buflen;
                if (buflen < recvlen)
                    throw PyValueError.Create("buffer too small for requested bytes");
                int len = (int)Math.Min(recvlen, int.MaxValue);
                var data = new byte[len];
                int readlen = RecvGuts(data, len, flags);
                pbuf.WriteContiguous(data.AsSpan(0, readlen));
                return PyInt.From(readlen);
            }
            finally
            {
                pbuf.Release();
            }
        }

        // sock_recvfrom_guts
        private int RecvFromGuts(byte[] buffer, int len, int flags, out PyObject addr)
        {
            GetSockAddrLen();
            int result = 0;
            EndPoint from = null;
            bool stream = Type == SocketModule.SOCK_STREAM || Type == SocketModule.SOCK_SEQPACKET;
            SockCall(false, sock =>
            {
                if (stream)
                {
                    // A connection-oriented socket: recvfrom leaves the address alone.
                    result = sock.Receive(buffer, 0, len, (SocketFlags)flags, out SocketError error);
                    return error == SocketError.Success ? 0 : CodeOf(error);
                }
                EndPoint any = sock.AddressFamily == AddressFamily.InterNetworkV6
                    ? new IPEndPoint(IPAddress.IPv6Any, 0) : new IPEndPoint(IPAddress.Any, 0);
                result = sock.ReceiveFrom(buffer, 0, len, (SocketFlags)flags, ref any);
                from = any;
                return 0;
            });
            addr = stream ? SocketModule.ZeroSockAddr() : SocketModule.MakeSockAddr(from);
            return result;
        }

        // sock_recvfrom: "n|i"
        private PyObject RecvFrom(PyObject[] args)
        {
            PyArgs.ParseTuple("recvfrom", args, 1, 2);
            long recvlen = PyArgs.Ssize(args[0]);
            int flags = args.Length > 1 ? PyArgs.ParseInt(args[1]) : 0;
            if (recvlen < 0)
                throw PyValueError.Create("negative buffersize in recvfrom");
            int len = (int)Math.Min(recvlen, int.MaxValue);
            var buf = new byte[len];
            int outlen = RecvFromGuts(buf, len, flags, out var addr);
            if (outlen != buf.Length)
                Array.Resize(ref buf, outlen);
            return PyTuple.From(PyBytes.From(buf), addr);
        }

        // sock_recvfrom_into: "w*|ni"
        private PyObject RecvFromInto(PyObject[] args, PyDict kwargs)
        {
            var a = SocketModule.ParseTupleAndKeywords("recvfrom_into", args, kwargs, IntoKeywords, 1);
            var pbuf = WritableArgument("recvfrom_into", a[0]);
            try
            {
                long recvlen = a[1] == null ? 0 : PyArgs.Ssize(a[1]);
                int flags = a[2] == null ? 0 : PyArgs.ParseInt(a[2]);
                long buflen = pbuf.Length();
                if (recvlen < 0)
                    throw PyValueError.Create("negative buffersize in recvfrom_into");
                if (recvlen == 0)
                    recvlen = buflen;
                else if (recvlen > buflen)
                    throw PyValueError.Create("nbytes is greater than the length of the buffer");
                int len = (int)Math.Min(recvlen, int.MaxValue);
                var data = new byte[len];
                int readlen = RecvFromGuts(data, len, flags, out var addr);
                pbuf.WriteContiguous(data.AsSpan(0, readlen));
                return PyTuple.From(PyInt.From(readlen), addr);
            }
            finally
            {
                pbuf.Release();
            }
        }

        // sock_send_impl
        private static SockFunc SendImpl(byte[] buf, int offset, int len, int flags, Action<int> result) => sock =>
        {
            int n = sock.Send(buf, offset, len, (SocketFlags)flags, out SocketError error);
            if (error != SocketError.Success)
                return CodeOf(error);
            result(n);
            return 0;
        };

        // sock_send: "y*|i"
        private PyObject Send(PyObject[] args)
        {
            PyArgs.ParseTuple("send", args, 1, 2);
            var pbuf = BytesMethods.RequireBuffer(args[0]);
            int flags = args.Length > 1 ? PyArgs.ParseInt(args[1]) : 0;
            int result = 0;
            SockCall(true, SendImpl(pbuf, 0, pbuf.Length, flags, n => result = n));
            return PyInt.From(result);
        }

        // sock_sendall: "y*|i", send() again until all is sent, within one deadline.
        private PyObject SendAll(PyObject[] args)
        {
            PyArgs.ParseTuple("sendall", args, 1, 2);
            var pbuf = BytesMethods.RequireBuffer(args[0]);
            int flags = args.Length > 1 ? PyArgs.ParseInt(args[1]) : 0;
            bool hasTimeout = Timeout > 0;
            long timeout = Timeout;
            long deadline = 0;
            bool deadlineInitialized = false;
            int offset = 0, len = pbuf.Length;
            do
            {
                if (hasTimeout)
                {
                    if (deadlineInitialized)
                    {
                        // recompute the timeout
                        timeout = deadline - PyTime.GetMonotonicClock();
                    }
                    else
                    {
                        deadlineInitialized = true;
                        deadline = PyTime.GetMonotonicClock() + timeout;
                    }
                    if (timeout <= 0)
                        throw TimedOut();
                }
                int n = 0;
                SockCallEx(true, SendImpl(pbuf, offset, len, flags, sent => n = sent), false, true, timeout);
                offset += n;
                len -= n;
                // PyErr_CheckSignals: the pending calls run at the next safe point.
            } while (len > 0);
            return PyNone.Instance;
        }

        // sock_sendto: "y*O" or "y*iO"
        private PyObject SendTo(PyObject[] args)
        {
            byte[] pbuf;
            int flags = 0;
            PyObject addro;
            switch (args.Length)
            {
                case 2:
                    pbuf = BytesMethods.RequireBuffer(args[0]);
                    addro = args[1];
                    break;
                case 3:
                    pbuf = BytesMethods.RequireBuffer(args[0]);
                    flags = PyArgs.ParseInt(args[1]);
                    addro = args[2];
                    break;
                default:
                    throw PyTypeError.Create($"sendto() takes 2 or 3 arguments ({args.Length} given)");
            }
            var addr = GetSockAddrArg(addro, "sendto");
            int result = 0;
            SockCall(true, sock =>
            {
                result = sock.SendTo(pbuf, 0, pbuf.Length, (SocketFlags)flags, addr);
                return 0;
            });
            return PyInt.From(result);
        }

        // sock_shutdown: _PyLong_AsInt
        private PyObject Shutdown(PyObject arg)
        {
            int how = PyArgs.Int(arg);
            var sock = Sock;
            int res = sock == null ? SocketModule.WSAENOTSOCK : 0;
            if (sock != null)
                PyGil.AllowThreads(() => res = Run(s => { s.Shutdown((SocketShutdown)how); return 0; }, sock));
            if (res != 0)
                throw SocketModule.SetError(res);
            return PyNone.Instance;
        }

        // The "k" converter: an int (no __index__), its low bits as an unsigned long.
        private static uint UnsignedLongMask(string fname, int position, PyObject value)
        {
            if (value is not PyInt number)
                throw PyTypeError.Create($"{fname}() argument {position} must be int, not {value.GetPyType().FullName}");
            return (uint)(number.Value & uint.MaxValue);
        }

        // sock_ioctl: "kO", then per command.
        private PyObject Ioctl(PyObject[] args)
        {
            PyArgs.ParseTuple("ioctl", args, 2, 2);
            uint cmd = UnsignedLongMask("ioctl", 1, args[0]);
            byte[] input;
            switch (cmd)
            {
                case SocketModule.SIO_RCVALL:
                case SocketModule.SIO_LOOPBACK_FAST_PATH:
                    input = BitConverter.GetBytes(PyArgs.UnsignedIntMask(args[1]));
                    break;
                case SocketModule.SIO_KEEPALIVE_VALS:
                {
                    // "(kkk)": struct tcp_keepalive { onoff, keepalivetime, keepaliveinterval }
                    if (args[1] is not PyTuple tuple || tuple.Items.Length != 3)
                        throw PyTypeError.Create($"ioctl() argument 2 must be sequence of length 3, not {args[1].GetPyType().FullName}");
                    input = new byte[12];
                    for (int i = 0; i < 3; i++)
                        BitConverter.GetBytes(UnsignedLongMask("ioctl", 2, tuple.Items[i])).CopyTo(input, 4 * i);
                    break;
                }
                default:
                    throw PyValueError.Create($"invalid ioctl command {cmd}");
            }
            var sock = Sock;
            int recv = 0;
            int res = sock == null ? SocketModule.WSAENOTSOCK : 0;
            if (sock != null)
                PyGil.AllowThreads(() => res = Run(s => { recv = s.IOControl(unchecked((int)cmd), input, null); return 0; }, sock));
            if (res != 0)
                throw SocketModule.SetError(res);
            return PyInt.From(recv);
        }

        /// <summary>
        /// sock_share: WSADuplicateSocketW for another process. .NET makes the protocol info only
        /// through DuplicateAndClose, which closes the socket it is called on; so it is called on a
        /// second Socket over the same OS handle that does not own it, and the original stays open.
        /// Where that cannot be done (not Windows), OSError WSAEOPNOTSUPP (docs/todo.md 5-29).
        /// </summary>
        private PyObject Share(PyObject[] args)
        {
            PyArgs.ParseTuple("share", args, 1, 1);
            uint processId = PyArgs.UnsignedIntMask(args[0]);
            var sock = Sock;
            byte[] info = null;
            int res = sock == null ? SocketModule.WSAENOTSOCK : 0;
            if (sock != null)
            {
                if (!OperatingSystem.IsWindows())
                    res = SocketModule.WSAEOPNOTSUPP;
                else
                    PyGil.AllowThreads(() => res = Run(s =>
                    {
                        using var alias = new Socket(new SafeSocketHandle(s.Handle, ownsHandle: false));
                        info = alias.DuplicateAndClose(unchecked((int)processId)).ProtocolInformation;
                        return 0;
                    }, sock));
            }
            if (res != 0)
                throw SocketModule.SetError(res);
            return PyBytes.From(info);
        }

        // sock_finalize: ResourceWarning "unclosed %R", then the descriptor is closed.
        private void SockFinalize()
        {
            if (Fd == InvalidSocket)
                return;
            try
            {
                PyWarnings.ResourceWarning(this, $"unclosed {this.ToRepr().Value}", 1);
            }
            catch (PythonException error)
            {
                // Spurious errors can appear at shutdown
                if (error.PyException.GetPyType().IsSubclassOf(PyType.WarningType))
                    PyUnraisable.Report(error, this);
            }
            long fd = Fd;
            Fd = InvalidSocket;
            SocketModule.PyGilCall(() => SocketHandles.Close(fd));
        }

        // sock_repr
        protected internal override PyObject Repr() =>
            PyString.From($"<socket object, fd={Fd}, family={Family}, type={Type}, proto={Proto}>");
    }
}
