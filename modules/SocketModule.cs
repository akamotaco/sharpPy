using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace SharpPy.Modules
{
    /// <summary>
    /// Modules/socketmodule.c (CPython 3.12.15, Windows build) as the native module _socket, over
    /// System.Net.Sockets. Lib/socket.py is CPython's own and sits on top of it.
    ///
    /// What the CLR has and Winsock gives CPython:
    /// - A SOCKET is an integer of SocketHandles' table, not the OS handle (see there).
    /// - The calls are Socket's synchronous methods; the blocking mode is Socket.Blocking (FIONBIO),
    ///   and the select() of internal_select is Socket.Select. sock_call_ex is ported as it is: a
    ///   socket with a timeout is non-blocking, waits in select and retries on WSAEWOULDBLOCK.
    /// - Errors are the WSA codes of SocketException, raised as PyErr_SetExcFromWindowsErr does
    ///   (OSError(errno, FormatMessage text, None, winerror), its subclass by errno).
    /// - Name resolution is System.Net.Dns (the system's getaddrinfo); the hints, the service and
    ///   numeric hosts are read as ws2_32's getaddrinfo reads them (SocketAddresses).
    /// What it cannot do raises (docs/todo.md 5-29): AF_BLUETOOTH addresses, a
    /// non-zero IPv6 flowinfo, socket.share where the duplicate cannot be made, the sys.audit hooks
    /// (SharpPy has no audit hooks: nothing is raised, nothing is reported).
    /// </summary>
    internal static partial class SocketModule
    {
        // Winsock's values (ws2def.h, winerror.h), which the constants of SocketModule.g.cs also have.
        internal const int AF_UNSPEC = 0, AF_INET = 2, AF_INET6 = 23, AF_BLUETOOTH = 32, AF_HYPERV = 34;
        internal const int SOCK_STREAM = 1, SOCK_DGRAM = 2, SOCK_SEQPACKET = 5, HV_PROTOCOL_RAW = 1;
        internal const int SOL_SOCKET = 0xffff, SO_TYPE = 0x1008, SO_ERROR = 0x1007;
        internal const int WSAEINTR = 10004, WSAEFAULT = 10014, WSAEINVAL = 10022, WSAEWOULDBLOCK = 10035,
            WSAENOTSOCK = 10038, WSAEOPNOTSUPP = 10045, WSAEAFNOSUPPORT = 10047, WSAENOTCONN = 10057;
        internal const uint SIO_RCVALL = 0x98000001, SIO_KEEPALIVE_VALS = 0x98000004, SIO_LOOPBACK_FAST_PATH = 0x98000010;

        // socket_state
        internal static PyType SocketType;
        internal static PyType HerrorType;
        internal static PyType GaierrorType;
        // defaulttimeout: _PyTime_t, -1 second when there is none.
        internal static long DefaultTimeout = -SecondNs;
        internal const long SecondNs = 1_000_000_000;

        // PyInit__socket / socket_exec
        internal static PyModule Create()
        {
            var module = new PyModule("_socket");
            var d = module.ModuleDict;
            // socket_exec: os_init (WSAStartup) needs nothing here.
            d["error"] = PyType.OSErrorType;
            HerrorType ??= PyExceptionSlots.NewException("socket", "herror", PyType.OSErrorType);
            d["herror"] = HerrorType;
            GaierrorType ??= PyExceptionSlots.NewException("socket", "gaierror", PyType.OSErrorType);
            d["gaierror"] = GaierrorType;
            d["timeout"] = PyType.TimeoutErrorType;
            SocketType ??= PySocketObject.MakeType();
            d["SocketType"] = SocketType;
            d["socket"] = SocketType;
            d["has_ipv6"] = PyBool.True;
            // The C API capsule: what a C extension imports; here it carries the module's types.
            d["CAPI"] = new PyCapsule("_socket.CAPI", new object[] { SocketType, PyType.OSErrorType, PyType.TimeoutErrorType });
            foreach (var (name, value) in IntConstants)
                d[name] = PyInt.From(value);
            foreach (var (name, value) in TextConstants)
                d[name] = PyString.From(value);

            // socket_methods
            d["gethostbyname"] = Function("gethostbyname", (args, kwargs) => GetHostByName(args, kwargs));
            d["gethostbyname_ex"] = Function("gethostbyname_ex", (args, kwargs) => GetHostByNameEx(args, kwargs));
            d["gethostbyaddr"] = Function("gethostbyaddr", (args, kwargs) => GetHostByAddr(args, kwargs));
            d["gethostname"] = Function("gethostname", (args, kwargs) => { PyArgs.NoArgs("_socket.gethostname", args, kwargs); return GetHostname(); });
            d["getservbyname"] = Function("getservbyname", (args, kwargs) => GetServByName(args, kwargs));
            d["getservbyport"] = Function("getservbyport", (args, kwargs) => GetServByPort(args, kwargs));
            d["getprotobyname"] = Function("getprotobyname", (args, kwargs) => GetProtoByName(args, kwargs));
            d["close"] = PyArgs.MethO("_socket", "close", args => Close(args[0]));
            d["dup"] = PyArgs.MethO("_socket", "dup", args => Dup(args[0]));
            d["ntohs"] = Function("ntohs", (args, kwargs) => Htons("ntohs", args, kwargs));
            d["ntohl"] = PyArgs.MethO("_socket", "ntohl", args => Htonl(args[0]));
            d["htons"] = Function("htons", (args, kwargs) => Htons("htons", args, kwargs));
            d["htonl"] = PyArgs.MethO("_socket", "htonl", args => Htonl(args[0]));
            d["inet_aton"] = Function("inet_aton", (args, kwargs) => InetAton(args, kwargs));
            d["inet_ntoa"] = Function("inet_ntoa", (args, kwargs) => InetNtoa(args, kwargs));
            d["inet_pton"] = Function("inet_pton", (args, kwargs) => InetPton(args, kwargs));
            d["inet_ntop"] = Function("inet_ntop", (args, kwargs) => InetNtop(args, kwargs));
            d["getaddrinfo"] = Function("getaddrinfo", (args, kwargs) => GetAddrInfo(args, kwargs));
            d["getnameinfo"] = Function("getnameinfo", (args, kwargs) => GetNameInfo(args, kwargs));
            d["getdefaulttimeout"] = Function("getdefaulttimeout", (args, kwargs) => { PyArgs.NoArgs("_socket.getdefaulttimeout", args, kwargs); return GetDefaultTimeout(); });
            d["setdefaulttimeout"] = PyArgs.MethO("_socket", "setdefaulttimeout", args => SetDefaultTimeout(args[0]));
            d["if_nameindex"] = Function("if_nameindex", (args, kwargs) => { PyArgs.NoArgs("_socket.if_nameindex", args, kwargs); return IfNameIndex(); });
            d["if_nametoindex"] = Function("if_nametoindex", (args, kwargs) => IfNameToIndex(args, kwargs));
            d["if_indextoname"] = PyArgs.MethO("_socket", "if_indextoname", args => IfIndexToName(args[0]));
            return module;
        }

        private static PyBuiltinFunction Function(string name, Func<PyObject[], PyDict, PyObject> body) =>
            new(name, body) { OwnerModule = "_socket" };

        // METH_VARARGS: PyArg_ParseTuple takes no keywords.
        internal static void VarArgs(string name, PyObject[] args, PyDict kwargs, int min, int max)
        {
            if (kwargs != null && kwargs.Length() != 0)
                throw PyTypeError.Create($"{name}() takes no keyword arguments");
            PyArgs.ParseTuple(name, args, min, max);
        }

        // ---- errors ----

        /// <summary>The WSA code of a SocketException (its native code on Windows).</summary>
        internal static int ErrorCodeOf(SocketException error) =>
            OperatingSystem.IsWindows() ? error.ErrorCode : (int)error.SocketErrorCode;

        /// <summary>set_error: PyErr_SetExcFromWindowsErr(PyExc_OSError, WSAGetLastError()).</summary>
        internal static PythonException SetError(int code)
        {
            if (OperatingSystem.IsWindows())
                return PyOSError.CreateWindowsFileError(code, null);
            var message = new SocketException(code).Message;
            return new PythonException(PyOSError.FromArguments(new PyObject[] { PyInt.From(code), PyString.From(message) }, null));
        }

        // set_herror: no hstrerror on Windows.
        private static PythonException SetHerror(int code) =>
            new((PyBaseException)HerrorType.Call(new PyObject[] { PyInt.From(code), PyString.From("host not found") }, null));

        // set_gaierror: no gai_strerror in the Windows build.
        internal static PythonException SetGaierror(int code) =>
            new((PyBaseException)GaierrorType.Call(new PyObject[] { PyInt.From(code), PyString.From("getaddrinfo failed") }, null));

        internal static PythonException OSErrorText(string message) =>
            new(PyOSError.FromArguments(new PyObject[] { PyString.From(message) }, null));

        // ---- timeouts ----

        /// <summary>socket_parse_timeout</summary>
        internal static long ParseTimeout(PyObject timeoutObj)
        {
            if (timeoutObj is PyNone)
                return -SecondNs;
            long timeout = PyTime.FromSecondsObject(timeoutObj, PyTimeRound.Timeout);
            if (timeout < 0)
                throw PyValueError.Create("Timeout value out of range");
            bool overflow = false;
            // MS_WINDOWS: _PyTime_AsTimeval; and without poll, the milliseconds as an int.
            try
            {
                PyTime.AsTimeval(timeout, PyTimeRound.Timeout);
            }
            catch (PythonException error) when (error.PyException is PyOverflowError)
            {
                overflow = true;
            }
            long ms = PyTime.AsMilliseconds(timeout, PyTimeRound.Timeout);
            overflow |= ms > int.MaxValue;
            if (overflow)
                throw PyOverflowError.Create("timeout doesn't fit into C timeval");
            return timeout;
        }

        internal static PyObject TimeoutObject(long timeout) =>
            timeout < 0 ? PyNone.Instance : new PyFloat(PyTime.AsSecondsDouble(timeout));

        // socket_getdefaulttimeout
        private static PyObject GetDefaultTimeout() => TimeoutObject(DefaultTimeout);

        // socket_setdefaulttimeout
        private static PyObject SetDefaultTimeout(PyObject arg)
        {
            DefaultTimeout = ParseTimeout(arg);
            return PyNone.Instance;
        }

        // ---- host names ----

        /// <summary>
        /// The "et" converter with the "idna" encoding: a str is encoded, bytes and bytearray are
        /// taken as they are. The C string is the bytes read as Latin-1 up to the first NUL.
        /// </summary>
        internal static string IdnaArgument(string fname, PyObject arg)
        {
            byte[] bytes;
            if (arg is PyString text)
                bytes = text.Encode("idna").Value;
            else if (arg is PyBytes raw)
                bytes = raw.Value;
            else if (arg is PyByteArray array)
                bytes = BytesMethods.RequireBuffer(array);
            else
                throw PyTypeError.Create($"{fname}() argument 1 must be str, bytes or bytearray, not {(arg is PyNone ? "None" : arg.GetPyType().FullName)}");
            // "et" without "#": a NUL inside is converterr's message.
            if (Array.IndexOf(bytes, (byte)0) >= 0)
                throw PyTypeError.Create($"{fname}() argument 1 must be encoded string without null bytes, not {arg.GetPyType().FullName}");
            return Encoding.Latin1.GetString(bytes);
        }

        /// <summary>
        /// setipaddr: "" is the wildcard, "255.255.255.255" and "&lt;broadcast&gt;" the broadcast
        /// address, a numeric address is read without resolution, anything else is resolved.
        /// </summary>
        internal static IPAddress SetIpAddr(string name, int af)
        {
            if (name.Length == 0)
            {
                List<SocketAddresses.AddrInfo> res;
                try
                {
                    res = SocketAddresses.GetAddrInfo(null, "0", af, SOCK_DGRAM, 0, SocketAddresses.AI_PASSIVE);
                }
                catch (SocketAddresses.GaiException error)
                {
                    throw SetGaierror(error.Code);
                }
                if (res.Count > 1)
                    throw OSErrorText("wildcard resolved to multiple address");
                return res[0].Address;
            }
            if (name == "255.255.255.255" || name == "<broadcast>")
            {
                if (af != AF_INET && af != AF_UNSPEC)
                    throw OSErrorText("address family mismatched");
                return IPAddress.Broadcast;
            }
            if (af == AF_UNSPEC || af == AF_INET)
            {
                var v4 = SocketAddresses.ParseIPv4Strict(name);
                if (v4 != null)
                    return new IPAddress(v4);
            }
            if ((af == AF_UNSPEC || af == AF_INET6) && name.IndexOf('%') < 0)
            {
                var v6 = SocketAddresses.ParseIPv6Strict(name);
                if (v6 != null)
                    return new IPAddress(v6);
            }
            List<SocketAddresses.AddrInfo> found;
            try
            {
                found = PyGilCall(() => SocketAddresses.GetAddrInfo(name, null, af, 0, 0, 0));
            }
            catch (SocketAddresses.GaiException error)
            {
                throw SetGaierror(error.Code);
            }
            return found[0].Address;
        }

        /// <summary>Py_BEGIN_ALLOW_THREADS around a call that touches no Python object.</summary>
        internal static T PyGilCall<T>(Func<T> call)
        {
            T result = default;
            PyGil.AllowThreads(() => result = call());
            return result;
        }

        /// <summary>makesockaddr for the IP families: (host, port) or (host, port, flowinfo, scope_id).</summary>
        internal static PyObject MakeSockAddr(EndPoint endPoint)
        {
            if (endPoint == null)
                return PyNone.Instance;
            if (endPoint is IPEndPoint ip)
            {
                var host = PyString.From(SocketAddresses.AddressText(ip.Address));
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                    return PyTuple.From(host, PyInt.From(ip.Port));
                // .NET keeps no flowinfo: it is 0, as the kernel reports it unless IPV6_FLOWINFO is on.
                return PyTuple.From(host, PyInt.From(ip.Port), PyInt.From(0), PyInt.From(ip.Address.ScopeId));
            }
            // AF_HYPERV: (vm_id, service_id) as UuidToStringW writes them.
            if (endPoint is HyperVEndPoint hv)
                return PyTuple.From(PyString.From(hv.VmId.ToString("D")), PyString.From(hv.ServiceId.ToString("D")));
            // The default case: (sa_family, sa_data).
            var serialized = endPoint.Serialize();
            var data = new byte[14];
            for (int i = 0; i < 14 && i + 2 < serialized.Size; i++)
                data[i] = serialized[i + 2];
            return PyTuple.From(PyInt.From((int)serialized.Family), PyBytes.From(data));
        }

        /// <summary>
        /// The address a connection-oriented recvfrom leaves: Winsock does not fill it, so it is the
        /// zeroed buffer, read by makesockaddr's default case as (0, 14 zero bytes).
        /// </summary>
        internal static PyObject ZeroSockAddr() => PyTuple.From(PyInt.From(0), PyBytes.From(new byte[14]));

        // socket_gethostname: GetComputerNameExW(ComputerNamePhysicalDnsHostname).
        private static PyObject GetHostname()
        {
            try
            {
                return PyString.From(Dns.GetHostName());
            }
            catch (SocketException error)
            {
                throw SetError(ErrorCodeOf(error));
            }
        }

        // socket_gethostbyname
        private static PyObject GetHostByName(PyObject[] args, PyDict kwargs)
        {
            VarArgs("gethostbyname", args, kwargs, 1, 1);
            var name = IdnaArgument("gethostbyname", args[0]);
            var address = SetIpAddr(name, AF_INET);
            return PyString.From(SocketAddresses.AddressText(address));
        }

        /// <summary>
        /// gethost_common over a resolved entry: (name, aliases, addresses) of the family af. A
        /// numeric name is answered as gethostbyname answers one: itself, no aliases, no lookup.
        /// </summary>
        private static PyObject GetHostCommon(string hostName, string[] aliases, IEnumerable<IPAddress> addresses, int af)
        {
            var nameList = new PyList();
            foreach (var alias in aliases)
                nameList.Append(PyString.From(alias));
            var addrList = new PyList();
            foreach (var address in addresses)
            {
                int family = address.AddressFamily == AddressFamily.InterNetwork ? AF_INET : AF_INET6;
                if (family == af)
                    addrList.Append(PyString.From(SocketAddresses.AddressText(address)));
            }
            return PyTuple.From(PyString.From(hostName), nameList, addrList);
        }

        // socket_gethostbyname_ex
        private static PyObject GetHostByNameEx(PyObject[] args, PyDict kwargs)
        {
            VarArgs("gethostbyname_ex", args, kwargs, 1, 1);
            var name = IdnaArgument("gethostbyname_ex", args[0]);
            var address = SetIpAddr(name, AF_INET);
            // gethostbyname(name): a dotted address comes back as itself.
            if (SocketAddresses.NumericHost(name) != null || name.Length == 0)
                return GetHostCommon(name.Length == 0 ? SocketAddresses.AddressText(address) : name, Array.Empty<string>(), new[] { address }, AF_INET);
            IPHostEntry entry;
            try
            {
                entry = PyGilCall(() => Dns.GetHostEntry(name, AddressFamily.InterNetwork));
            }
            catch (SocketException error)
            {
                throw SetHerror(ErrorCodeOf(error));
            }
            return GetHostCommon(entry.HostName, entry.Aliases, entry.AddressList, AF_INET);
        }

        // socket_gethostbyaddr
        private static PyObject GetHostByAddr(PyObject[] args, PyDict kwargs)
        {
            VarArgs("gethostbyaddr", args, kwargs, 1, 1);
            var ipNum = IdnaArgument("gethostbyaddr", args[0]);
            var address = SetIpAddr(ipNum, AF_UNSPEC);
            int af = address.AddressFamily == AddressFamily.InterNetwork ? AF_INET : AF_INET6;
            IPHostEntry entry;
            try
            {
                entry = PyGilCall(() => Dns.GetHostEntry(address));
            }
            catch (SocketException error)
            {
                throw SetHerror(ErrorCodeOf(error));
            }
            // gethostbyaddr's h_addr_list is the address asked about.
            return GetHostCommon(entry.HostName, entry.Aliases, new[] { address }, af);
        }

        // socket_getservbyname: "s|s"
        private static PyObject GetServByName(PyObject[] args, PyDict kwargs)
        {
            VarArgs("getservbyname", args, kwargs, 1, 2);
            var name = PyArgs.ClinicStr("getservbyname", "argument 1", "str", args[0]);
            var proto = args.Length > 1 ? PyArgs.ClinicStr("getservbyname", "argument 2", "str", args[1]) : null;
            int port = SocketAddresses.ServicePort(name, proto);
            if (port < 0)
                throw OSErrorText("service/proto not found");
            return PyInt.From(port);
        }

        // socket_getservbyport: "i|s"
        private static PyObject GetServByPort(PyObject[] args, PyDict kwargs)
        {
            VarArgs("getservbyport", args, kwargs, 1, 2);
            int port = PyArgs.ParseInt(args[0]);
            var proto = args.Length > 1 ? PyArgs.ClinicStr("getservbyport", "argument 2", "str", args[1]) : null;
            if (port < 0 || port > 0xffff)
                throw PyOverflowError.Create("getservbyport: port must be 0-65535.");
            var name = SocketAddresses.ServiceName(port, proto);
            if (name == null)
                throw OSErrorText("port/proto not found");
            return PyString.From(name);
        }

        // socket_getprotobyname: "s"
        private static PyObject GetProtoByName(PyObject[] args, PyDict kwargs)
        {
            VarArgs("getprotobyname", args, kwargs, 1, 1);
            var name = PyArgs.ClinicStr("getprotobyname", "argument 1", "str", args[0]);
            int number = SocketAddresses.ProtocolNumber(name);
            if (number < 0)
                throw OSErrorText("protocol not found");
            return PyInt.From(number);
        }

        /// <summary>PyLong_AsSocket_t: PyLong_AsLongLong (an index), SOCKET_T being unsigned on Windows.</summary>
        internal static long AsSocket(PyObject value)
        {
            var number = PyObject.IndexOrThrow(value).Value;
            if (number < long.MinValue || number > long.MaxValue)
                throw PyOverflowError.Create("Python int too large to convert to C long long");
            return (long)number;
        }

        // socket_close
        private static PyObject Close(PyObject fdobj)
        {
            long fd = AsSocket(fdobj);
            int res = PyGilCall(() => SocketHandles.Close(fd));
            if (res != 0)
                throw SetError(res);
            return PyNone.Instance;
        }

        // socket_dup: WSADuplicateSocketW + WSASocketW, a new descriptor for the same socket.
        private static PyObject Dup(PyObject fdobj)
        {
            long fd = AsSocket(fdobj);
            long newfd = SocketHandles.Dup(fd);
            if (newfd == -1)
                throw SetError(WSAENOTSOCK);
            return PyInt.From(newfd);
        }

        // socket_ntohs / socket_htons: "i", then the 16-bit range.
        private static PyObject Htons(string name, PyObject[] args, PyDict kwargs)
        {
            VarArgs(name, args, kwargs, 1, 1);
            int x = PyArgs.ParseInt(args[0]);
            if (x < 0)
                throw PyOverflowError.Create($"{name}: can't convert negative Python int to C 16-bit unsigned integer");
            if (x > 0xffff)
                throw PyOverflowError.Create($"{name}: Python int too large to convert to C 16-bit unsigned integer");
            return PyInt.From((long)(ushort)((x >> 8) | (x << 8)));
        }

        // socket_ntohl / socket_htonl: an int, the unsigned long of Windows (32 bits).
        private static PyObject Htonl(PyObject arg)
        {
            if (arg is not PyInt number)
                throw PyTypeError.Create($"expected int, {arg.GetPyType().FullName} found");
            if (number.Value < 0)
                throw PyOverflowError.Create("can't convert negative value to unsigned int");
            if (number.Value > uint.MaxValue)
                throw PyOverflowError.Create("Python int too large to convert to C unsigned long");
            uint x = (uint)number.Value;
            return PyInt.From((long)System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(x));
        }

        // socket_inet_aton: inet_addr on Windows.
        private static PyObject InetAton(PyObject[] args, PyDict kwargs)
        {
            VarArgs("inet_aton", args, kwargs, 1, 1);
            var ip = PyArgs.ClinicStr("inet_aton", "argument 1", "str", args[0]);
            byte[] packed;
            if (ip == "255.255.255.255")
                packed = new byte[] { 255, 255, 255, 255 };
            else
                packed = SocketAddresses.InetAddr(ip);
            if (packed == null)
                throw OSErrorText("illegal IP address string passed to inet_aton");
            return PyBytes.From(packed);
        }

        // socket_inet_ntoa: "y*"
        private static PyObject InetNtoa(PyObject[] args, PyDict kwargs)
        {
            VarArgs("inet_ntoa", args, kwargs, 1, 1);
            var packed = BytesMethods.RequireBuffer(args[0]);
            if (packed.Length != 4)
                throw OSErrorText("packed IP wrong length for inet_ntoa");
            return PyString.From(SocketAddresses.IPv4ToString(packed));
        }

        // socket_inet_pton: "is"
        private static PyObject InetPton(PyObject[] args, PyDict kwargs)
        {
            VarArgs("inet_pton", args, kwargs, 2, 2);
            int af = PyArgs.ParseInt(args[0]);
            var ip = PyArgs.ClinicStr("inet_pton", "argument 2", "str", args[1]);
            byte[] packed;
            if (af == AF_INET)
                packed = SocketAddresses.ParseIPv4Strict(ip);
            else if (af == AF_INET6)
                packed = SocketAddresses.ParseIPv6Strict(ip);
            else
                // InetPtonA fails with WSAEAFNOSUPPORT and errno is not set: PyErr_SetFromErrno with 0.
                throw new PythonException(PyOSError.FromArguments(new PyObject[] { PyInt.From(0), PyString.From("Error") }, null));
            if (packed == null)
                throw OSErrorText("illegal IP address string passed to inet_pton");
            return PyBytes.From(packed);
        }

        // socket_inet_ntop: "iy*"
        private static PyObject InetNtop(PyObject[] args, PyDict kwargs)
        {
            VarArgs("inet_ntop", args, kwargs, 2, 2);
            int af = PyArgs.ParseInt(args[0]);
            var packed = BytesMethods.RequireBuffer(args[1]);
            if (af == AF_INET)
            {
                if (packed.Length != 4)
                    throw PyValueError.Create("invalid length of packed IP address string");
                return PyString.From(SocketAddresses.IPv4ToString(packed));
            }
            if (af == AF_INET6)
            {
                if (packed.Length != 16)
                    throw PyValueError.Create("invalid length of packed IP address string");
                return PyString.From(SocketAddresses.IPv6ToString(packed));
            }
            throw PyValueError.Create($"unknown address family {af}");
        }

        private static readonly string[] AddrInfoKeywords = { "host", "port", "family", "type", "proto", "flags" };

        // socket_getaddrinfo: "OO|iiii"
        private static PyObject GetAddrInfo(PyObject[] args, PyDict kwargs)
        {
            var a = ParseTupleAndKeywords("getaddrinfo", args, kwargs, AddrInfoKeywords, 2);
            var hobj = a[0];
            var pobj = a[1];
            int family = a[2] == null ? AF_UNSPEC : PyArgs.ParseInt(a[2]);
            int socktype = a[3] == null ? 0 : PyArgs.ParseInt(a[3]);
            int protocol = a[4] == null ? 0 : PyArgs.ParseInt(a[4]);
            int flags = a[5] == null ? 0 : PyArgs.ParseInt(a[5]);
            string hptr;
            if (hobj is PyNone)
                hptr = null;
            else if (hobj is PyString text)
                hptr = CString(text.Encode("idna").Value);
            else if (hobj is PyBytes bytes)
                hptr = CString(bytes.Value);
            else
                throw PyTypeError.Create("getaddrinfo() argument 1 must be string or None");
            string pptr;
            if (pobj is PyInt && pobj.GetPyType() == PyType.IntType && pobj is not PyBool)
                pptr = pobj.ToStr().Value;
            else if (pobj is PyString ptext)
                pptr = PyArgs.AsUtf8CString(ptext);
            else if (pobj is PyBytes pbytes)
                pptr = CString(pbytes.Value);
            else if (pobj is PyNone)
                pptr = null;
            else
                throw OSErrorText("Int or String expected");
            List<SocketAddresses.AddrInfo> res;
            try
            {
                res = PyGilCall(() => SocketAddresses.GetAddrInfo(hptr, pptr, family, socktype, protocol, flags));
            }
            catch (SocketAddresses.GaiException error)
            {
                throw SetGaierror(error.Code);
            }
            var all = new PyList();
            foreach (var entry in res)
            {
                var addr = MakeSockAddr(new IPEndPoint(entry.Address, entry.Port));
                all.Append(PyTuple.From(PyInt.From(entry.Family), PyInt.From(entry.SockType), PyInt.From(entry.Protocol),
                    PyString.From(entry.CanonName ?? ""), addr));
            }
            return all;
        }

        // A char* read from bytes: up to the first NUL, each byte one Latin-1 character.
        private static string CString(byte[] bytes)
        {
            int nul = Array.IndexOf(bytes, (byte)0);
            return Encoding.Latin1.GetString(bytes, 0, nul < 0 ? bytes.Length : nul);
        }

        /// <summary>
        /// PyArg_ParseTupleAndKeywords for a format whose required arguments come first; the
        /// result has a slot per keyword, null for what was not given.
        /// </summary>
        internal static PyObject[] ParseTupleAndKeywords(string fname, PyObject[] args, PyDict kwargs, string[] kwlist, int min)
        {
            int max = kwlist.Length;
            int nkw = kwargs?.Length() ?? 0;
            int total = args.Length + nkw;
            if (total > max)
                throw PyTypeError.Create($"{fname}() takes at most {max} {(args.Length == 0 ? "keyword " : "")}argument{(max == 1 ? "" : "s")} ({total} given)");
            var result = new PyObject[max];
            Array.Copy(args, result, args.Length);
            if (nkw > 0)
            {
                foreach (var pair in kwargs.InternalDict)
                {
                    if (pair.Key is not PyString key)
                        throw PyTypeError.Create("keywords must be strings");
                    int index = Array.IndexOf(kwlist, key.Value);
                    if (index < 0)
                        throw PyTypeError.Create($"'{key.Value}' is an invalid keyword argument for {fname}()");
                    if (index < args.Length)
                        throw PyTypeError.Create($"argument for {fname}() given by name ('{key.Value}') and position ({index + 1})");
                    result[index] = pair.Value;
                }
            }
            for (int i = 0; i < min; i++)
                if (result[i] == null)
                    throw PyTypeError.Create($"{fname}() missing required argument '{kwlist[i]}' (pos {i + 1})");
            return result;
        }

        // socket_getnameinfo: "Oi", the sockaddr "si|II".
        private static PyObject GetNameInfo(PyObject[] args, PyDict kwargs)
        {
            VarArgs("getnameinfo", args, kwargs, 2, 2);
            var sa = args[0];
            int flags = PyArgs.ParseInt(args[1]);
            if (sa is not PyTuple tuple)
                throw PyTypeError.Create("getnameinfo() argument 1 must be a tuple");
            var items = tuple.Items;
            if (items.Length < 2 || items.Length > 4)
                throw PyTypeError.Create("getnameinfo(): illegal sockaddr argument");
            var hostp = PyArgs.ClinicStr("getnameinfo", "argument 1", "str", items[0]);
            int port = PyArgs.ParseInt(items[1]);
            uint flowinfo = items.Length > 2 ? PyArgs.UnsignedIntMask(items[2]) : 0;
            uint scopeId = items.Length > 3 ? PyArgs.UnsignedIntMask(items[3]) : 0;
            if (flowinfo > 0xfffff)
                throw PyOverflowError.Create("getnameinfo(): flowinfo must be 0-1048575.");
            var pbuf = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            List<SocketAddresses.AddrInfo> res;
            try
            {
                res = PyGilCall(() => SocketAddresses.GetAddrInfo(hostp, pbuf, AF_UNSPEC, SOCK_DGRAM, 0, SocketAddresses.AI_NUMERICHOST));
            }
            catch (SocketAddresses.GaiException error)
            {
                throw SetGaierror(error.Code);
            }
            if (res.Count > 1)
                throw OSErrorText("sockaddr resolved to multiple addresses");
            var address = res[0].Address;
            if (res[0].Family == AF_INET)
            {
                if (items.Length != 2)
                    throw OSErrorText("IPv4 sockaddr must be 2 tuple");
            }
            else
            {
                if (flowinfo != 0)
                    throw FlowInfoUnsupported("getnameinfo");
                address = new IPAddress(address.GetAddressBytes(), scopeId);
            }
            (string Host, string Service) result;
            try
            {
                result = PyGilCall(() => SocketAddresses.GetNameInfo(address, res[0].Port, flags));
            }
            catch (SocketAddresses.GaiException error)
            {
                throw SetGaierror(error.Code);
            }
            return PyTuple.From(PyString.From(result.Host), PyString.From(result.Service));
        }

        /// <summary>
        /// A sockaddr_in6 with a non-zero sin6_flowinfo: .NET's IPEndPoint has no flow label to
        /// carry it (docs/todo.md 5-29), so it is refused rather than dropped.
        /// </summary>
        internal static PythonException FlowInfoUnsupported(string caller) =>
            OSErrorText($"{caller}(): a non-zero IPv6 flowinfo is not supported (no flow label in .NET's IPEndPoint)");

        // ---- interfaces ----

        /// <summary>
        /// The interfaces with an IP index, from NetworkInterface. CPython lists GetIfTable2Ex's rows
        /// by their LUID names ("ethernet_32768"), which .NET does not expose: the names here are
        /// .NET's (NetworkInterface.Name), consistent among the three functions (docs/todo.md 5-29).
        /// </summary>
        private static List<(int Index, string Name)> Interfaces()
        {
            var list = new List<(int, string)>();
            NetworkInterface[] all;
            try
            {
                all = NetworkInterface.GetAllNetworkInterfaces();
            }
            catch (NetworkInformationException error)
            {
                throw SetError(error.ErrorCode);
            }
            foreach (var nic in all)
            {
                int index = -1;
                try
                {
                    var props = nic.GetIPProperties();
                    index = props.GetIPv4Properties()?.Index ?? -1;
                    if (index < 0)
                        index = props.GetIPv6Properties()?.Index ?? -1;
                }
                catch (Exception error) when (error is NetworkInformationException or PlatformNotSupportedException or NotImplementedException)
                {
                    index = -1;
                }
                if (index > 0)
                    list.Add((index, nic.Name));
            }
            list.Sort((x, y) => x.Item1.CompareTo(y.Item1));
            return list;
        }

        // socket_if_nameindex
        private static PyObject IfNameIndex()
        {
            var list = new PyList();
            foreach (var (index, name) in Interfaces())
                list.Append(PyTuple.From(PyInt.From(index), PyString.From(name)));
            return list;
        }

        // socket_if_nametoindex: "O&" PyUnicode_FSConverter
        private static PyObject IfNameToIndex(PyObject[] args, PyDict kwargs)
        {
            VarArgs("if_nametoindex", args, kwargs, 1, 1);
            var name = PyArgs.FSDecoder(args[0]);
            foreach (var (index, ifname) in Interfaces())
                if (ifname == name)
                    return PyInt.From(index);
            throw OSErrorText("no interface with this name");
        }

        // socket_if_indextoname
        private static PyObject IfIndexToName(PyObject arg)
        {
            ulong indexLong = PyArgs.AsUnsignedLong(arg);
            if (indexLong > uint.MaxValue)
                throw PyOverflowError.Create("index is too large");
            foreach (var (index, name) in Interfaces())
                if ((ulong)index == indexLong)
                    return PyString.From(name);
            // if_indextoname fails without setting errno: PyErr_SetFromErrno with errno 0.
            throw new PythonException(PyOSError.FromArguments(new PyObject[] { PyInt.From(0), PyString.From("Error") }, null));
        }
    }
}
