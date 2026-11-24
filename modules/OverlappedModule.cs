using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace SharpPy.Modules
{
    /// <summary>
    /// The kernel objects _overlapped and _winapi hand out as HANDLE values, emulated over managed
    /// primitives: I/O completion ports (a queue and a semaphore), events (EventWaitHandle) and
    /// registered waits (ThreadPool.RegisterWaitForSingleObject). SharpPy calls no Win32 API, so these
    /// numbers mean something only to _overlapped and to _winapi.CloseHandle/WaitForSingleObject, as
    /// the socket numbers of SocketHandles mean something only to _socket and select.
    /// </summary>
    internal static class ManagedHandles
    {
        private static readonly Dictionary<long, object> Table = new();
        // A range of its own, apart from the socket numbers (0x100 and up, by 4).
        private static long _next = 0x10000 - 4;

        internal static long Add(object value)
        {
            lock (Table)
            {
                _next += 4;
                Table[_next] = value;
                return _next;
            }
        }

        internal static T Get<T>(long handle) where T : class
        {
            lock (Table)
                return Table.TryGetValue(handle, out var value) ? value as T : null;
        }

        /// <summary>CloseHandle: false for a number that is not open.</summary>
        internal static bool Close(long handle)
        {
            object value;
            lock (Table)
            {
                if (!Table.Remove(handle, out value))
                    return false;
            }
            switch (value)
            {
                case CompletionPort port:
                    port.Close();
                    break;
                case IDisposable disposable:
                    // An event, a semaphore (_multiprocessing.SemLock).
                    disposable.Dispose();
                    break;
            }
            return true;
        }

        /// <summary>WaitForSingleObject on an emulated event: WAIT_OBJECT_0, WAIT_TIMEOUT, or -1 when the
        /// number is not an event.</summary>
        internal static long Wait(long handle, uint milliseconds)
        {
            var ev = Get<EventWaitHandle>(handle);
            if (ev == null)
                return -1;
            int timeout = milliseconds == OverlappedModule.INFINITE ? Timeout.Infinite : (int)Math.Min(milliseconds, int.MaxValue);
            bool signalled = false;
            PyGil.AllowThreads(() => signalled = ev.WaitOne(timeout));
            return signalled ? 0 : OverlappedModule.WAIT_TIMEOUT;
        }
    }

    /// <summary>An I/O completion port: the packets GetQueuedCompletionStatus dequeues, in order.</summary>
    internal sealed class CompletionPort
    {
        internal readonly struct Packet
        {
            internal readonly uint Error;
            internal readonly uint Bytes;
            internal readonly ulong Key;
            internal readonly ulong Overlapped;

            internal Packet(uint error, uint bytes, ulong key, ulong overlapped)
            {
                Error = error;
                Bytes = bytes;
                Key = key;
                Overlapped = overlapped;
            }
        }

        private readonly ConcurrentQueue<Packet> _queue = new();
        private readonly SemaphoreSlim _count = new(0);
        private volatile bool _closed;

        internal bool Closed => _closed;

        internal void Post(Packet packet)
        {
            if (_closed)
                return;
            _queue.Enqueue(packet);
            _count.Release();
        }

        /// <summary>false on timeout; a closed port answers ERROR_ABANDONED_WAIT_0 through the caller.</summary>
        internal bool TryTake(int milliseconds, out Packet packet)
        {
            packet = default;
            if (!_count.Wait(milliseconds))
                return false;
            return _queue.TryDequeue(out packet);
        }

        internal void Close()
        {
            _closed = true;
            // Wake a thread blocked in GetQueuedCompletionStatus.
            _count.Release();
        }
    }

    /// <summary>
    /// _overlapped (Modules/overlapped.c of CPython v3.12.15): the Windows overlapped I/O asyncio's
    /// IocpProactor runs on. One C# method per C function, named after it. The kernel's part is
    /// emulated with .NET's own asynchronous socket operations: an operation on a socket of the
    /// _socket handle table starts the matching Task-based Socket method (ReceiveAsync, SendAsync,
    /// AcceptAsync with the accepting socket, ConnectAsync, ReceiveFromAsync, SendToAsync,
    /// DisconnectAsync); when it finishes, the OVERLAPPED's status is set, its event is signalled and,
    /// when the socket is associated with a completion port, a packet with the operation's address is
    /// queued there, as the kernel does. Cancellation (CancelIoEx) is the operation's
    /// CancellationToken, which completes it with ERROR_OPERATION_ABORTED. .NET reports Winsock codes;
    /// GetOverlappedResult reports the Win32 code of the NTSTATUS, so the codes are mapped back
    /// (Win32ErrorOfSocketError). File and pipe handles (ReadFile, WriteFile, TransmitFile,
    /// ConnectNamedPipe, ConnectPipe) do not exist in SharpPy and raise OSError
    /// ERROR_INVALID_HANDLE, as Windows does for a value that is not a handle.
    /// </summary>
    internal static class OverlappedModule
    {
        internal const uint ERROR_SUCCESS = 0;
        internal const uint ERROR_FILE_NOT_FOUND = 2;
        internal const uint ERROR_INVALID_HANDLE = 6;
        internal const uint ERROR_NOT_SUPPORTED = 50;
        internal const uint ERROR_NETNAME_DELETED = 64;
        internal const uint ERROR_INVALID_PARAMETER = 87;
        internal const uint ERROR_BROKEN_PIPE = 109;
        internal const uint ERROR_SEM_TIMEOUT = 121;
        internal const uint ERROR_MORE_DATA = 234;
        internal const uint WAIT_TIMEOUT = 258;
        internal const uint ERROR_PIPE_BUSY = 231;
        internal const uint ERROR_PIPE_CONNECTED = 535;
        internal const uint ERROR_ABANDONED_WAIT_0 = 735;
        internal const uint ERROR_OPERATION_ABORTED = 995;
        internal const uint ERROR_IO_INCOMPLETE = 996;
        internal const uint ERROR_IO_PENDING = 997;
        internal const uint ERROR_NOT_FOUND = 1168;
        internal const uint ERROR_CONNECTION_REFUSED = 1225;
        internal const uint ERROR_ADDRESS_ALREADY_ASSOCIATED = 1227;
        internal const uint ERROR_NETWORK_UNREACHABLE = 1231;
        internal const uint ERROR_HOST_UNREACHABLE = 1232;
        internal const uint ERROR_PORT_UNREACHABLE = 1234;
        internal const uint ERROR_CONNECTION_ABORTED = 1236;
        internal const uint INFINITE = 0xFFFFFFFF;
        internal const ulong INVALID_HANDLE_VALUE = ulong.MaxValue;
        internal const uint SO_UPDATE_ACCEPT_CONTEXT = 0x700B;
        internal const uint SO_UPDATE_CONNECT_CONTEXT = 0x7010;
        internal const uint TF_REUSE_SOCKET = 0x02;

        internal enum OpType
        {
            TYPE_NONE, TYPE_NOT_STARTED, TYPE_READ, TYPE_READINTO, TYPE_WRITE,
            TYPE_ACCEPT, TYPE_CONNECT, TYPE_DISCONNECT, TYPE_CONNECT_NAMED_PIPE,
            TYPE_WAIT_NAMED_PIPE_AND_CONNECT, TYPE_TRANSMIT_FILE, TYPE_READ_FROM,
            TYPE_WRITE_TO, TYPE_READ_FROM_INTO,
        }

        // The handles associated with a port: CreateIoCompletionPort(handle, port, key).
        private static readonly ConcurrentDictionary<long, (CompletionPort Port, ulong Key)> Associations = new();

        private static PyType OverlappedType;

        // ------------------------------------------------------------------ argument converters

        // HANDLE_converter / ULONG_PTR_converter / OVERLAPPED_converter: PyLong_AsVoidPtr
        private static ulong AsVoidPtr(PyObject value)
        {
            if (value is not PyInt && PyInt.LongValue(value) is null)
                throw new PythonException(new PySystemError("bad argument to internal function"));
            var number = PyInt.LongValue(value).Value;
            if (number.Sign < 0)
            {
                if (number < long.MinValue)
                    throw PyOverflowError.Create("int too big to convert");
                return unchecked((ulong)(long)number);
            }
            if (number > ulong.MaxValue)
                throw PyOverflowError.Create("int too big to convert");
            return (ulong)number;
        }

        // DWORD_converter: _PyLong_UnsignedLong_Converter on a 32-bit unsigned long
        private static uint AsDword(PyObject value)
        {
            if (PyInt.LongValue(value) is { } number && number.Value.Sign < 0)
                throw PyValueError.Create("value must be positive");
            return (uint)PyArgs.AsUnsignedLong(value);
        }

        // BOOL_converter: int
        private static int AsBool(PyObject value) => PyArgs.Int(value);

        private static PyObject FromVoidPtr(ulong value) => PyInt.From(new BigInteger(value));

        private static void CheckArgs(string name, PyObject[] args, PyDict kwargs, int min, int max) =>
            PyArgs.Positional(name, args, kwargs, min, max);

        // ------------------------------------------------------------------ errors

        // SetFromWindowsErr
        internal static PythonException SetFromWindowsErr(uint err)
        {
            PythonException error = PyOSError.CreateWindowsFileError((int)err, null);
            PyType exception_type = err switch
            {
                ERROR_CONNECTION_REFUSED => PyType.ConnectionRefusedErrorType,
                ERROR_CONNECTION_ABORTED => PyType.ConnectionAbortedErrorType,
                _ => null,
            };
            if (exception_type == null)
                return error;
            // PyErr_SetExcFromWindowsErr(exception_type, err): the same arguments, that class.
            var args = ((PyBaseException)error.PyException).Args;
            return new PythonException((PyBaseException)exception_type.Call(args, null));
        }

        /// <summary>
        /// The Win32 error GetOverlappedResult would report for an operation .NET finished with this
        /// Winsock error: the inverse of the NTSTATUS mapping .NET applied (the status of a socket
        /// IRP becomes ERROR_NETNAME_DELETED for a reset, ERROR_CONNECTION_REFUSED for a refused
        /// connection, ERROR_PORT_UNREACHABLE for a datagram's ICMP port unreachable).
        /// </summary>
        private static uint Win32ErrorOfSocketError(int wsa, bool datagram) => wsa switch
        {
            0 => ERROR_SUCCESS,
            10054 => datagram ? ERROR_PORT_UNREACHABLE : ERROR_NETNAME_DELETED,   // WSAECONNRESET
            10061 => ERROR_CONNECTION_REFUSED,      // WSAECONNREFUSED
            10053 => ERROR_CONNECTION_ABORTED,      // WSAECONNABORTED
            10051 => ERROR_NETWORK_UNREACHABLE,     // WSAENETUNREACH
            10065 => ERROR_HOST_UNREACHABLE,        // WSAEHOSTUNREACH
            10060 => ERROR_SEM_TIMEOUT,             // WSAETIMEDOUT
            10048 => ERROR_ADDRESS_ALREADY_ASSOCIATED, // WSAEADDRINUSE
            10040 => ERROR_MORE_DATA,               // WSAEMSGSIZE
            995 => ERROR_OPERATION_ABORTED,         // WSA_OPERATION_ABORTED
            _ => (uint)wsa,
        };

        private static Socket SocketOf(ulong handle) => SocketHandles.Get((long)handle);

        // ------------------------------------------------------------------ completion port functions

        // _overlapped_CreateIoCompletionPort_impl
        private static PyObject _overlapped_CreateIoCompletionPort_impl(ulong FileHandle, ulong ExistingCompletionPort,
            ulong CompletionKey, uint NumberOfConcurrentThreads)
        {
            if (FileHandle == INVALID_HANDLE_VALUE)
            {
                if (ExistingCompletionPort != 0)
                    throw SetFromWindowsErr(ERROR_INVALID_PARAMETER);
                return FromVoidPtr((ulong)ManagedHandles.Add(new CompletionPort()));
            }
            var port = ManagedHandles.Get<CompletionPort>((long)ExistingCompletionPort);
            if (SocketOf(FileHandle) == null)
                throw SetFromWindowsErr(ERROR_INVALID_HANDLE);
            if (ExistingCompletionPort == 0)
            {
                // A new port with the handle associated to it.
                var created = new CompletionPort();
                long h = ManagedHandles.Add(created);
                if (!Associations.TryAdd((long)FileHandle, (created, CompletionKey)))
                    throw SetFromWindowsErr(ERROR_INVALID_PARAMETER);
                return FromVoidPtr((ulong)h);
            }
            if (port == null)
                throw SetFromWindowsErr(ERROR_INVALID_HANDLE);
            // A handle can be associated with one port only.
            if (!Associations.TryAdd((long)FileHandle, (port, CompletionKey)))
                throw SetFromWindowsErr(ERROR_INVALID_PARAMETER);
            return FromVoidPtr(ExistingCompletionPort);
        }

        // _overlapped_GetQueuedCompletionStatus_impl
        private static PyObject _overlapped_GetQueuedCompletionStatus_impl(ulong CompletionPort, uint Milliseconds)
        {
            var port = ManagedHandles.Get<CompletionPort>((long)CompletionPort);
            if (port == null)
                throw SetFromWindowsErr(ERROR_INVALID_HANDLE);
            int timeout = Milliseconds == INFINITE ? Timeout.Infinite : (int)Math.Min(Milliseconds, int.MaxValue);
            bool got = false;
            CompletionPort.Packet packet = default;
            PyGil.AllowThreads(() => got = port.TryTake(timeout, out packet));
            if (port.Closed)
                throw SetFromWindowsErr(ERROR_ABANDONED_WAIT_0);
            if (!got)
                return PyNone.Instance;    // WAIT_TIMEOUT with no OVERLAPPED
            return PyTuple.From(new PyObject[]
            {
                PyInt.From(packet.Error), PyInt.From(packet.Bytes), FromVoidPtr(packet.Key), FromVoidPtr(packet.Overlapped),
            });
        }

        // _overlapped_PostQueuedCompletionStatus_impl
        private static PyObject _overlapped_PostQueuedCompletionStatus_impl(ulong CompletionPort, uint NumberOfBytes,
            ulong CompletionKey, ulong Overlapped)
        {
            var port = ManagedHandles.Get<CompletionPort>((long)CompletionPort);
            if (port == null)
                throw SetFromWindowsErr(ERROR_INVALID_HANDLE);
            port.Post(new CompletionPort.Packet(ERROR_SUCCESS, NumberOfBytes, CompletionKey, Overlapped));
            return PyNone.Instance;
        }

        // ------------------------------------------------------------------ waits

        /// <summary>A RegisterWaitForSingleObject registration.</summary>
        private sealed class WaitRegistration
        {
            internal RegisteredWaitHandle Handle;
        }

        // PostToQueueCallback: the packet's NumberOfBytes is TimerOrWaitFired, its key 0.
        private static void PostToQueueCallback(CompletionPort port, ulong overlapped, bool timedOut) =>
            port.Post(new CompletionPort.Packet(ERROR_SUCCESS, timedOut ? 1u : 0u, 0, overlapped));

        // _overlapped_RegisterWaitWithQueue_impl
        private static PyObject _overlapped_RegisterWaitWithQueue_impl(ulong Object, ulong CompletionPort,
            ulong Overlapped, uint Milliseconds)
        {
            var ev = ManagedHandles.Get<EventWaitHandle>((long)Object);
            var port = ManagedHandles.Get<CompletionPort>((long)CompletionPort);
            if (ev == null || port == null)
                throw SetFromWindowsErr(ERROR_INVALID_HANDLE);
            var registration = new WaitRegistration();
            int timeout = Milliseconds == INFINITE ? Timeout.Infinite : (int)Math.Min(Milliseconds, int.MaxValue);
            // WT_EXECUTEINWAITTHREAD | WT_EXECUTEONLYONCE
            registration.Handle = ThreadPool.RegisterWaitForSingleObject(ev,
                (state, timedOut) => PostToQueueCallback(port, Overlapped, timedOut), null, timeout, executeOnlyOnce: true);
            return FromVoidPtr((ulong)ManagedHandles.Add(registration));
        }

        // _overlapped_UnregisterWait_impl
        private static PyObject _overlapped_UnregisterWait_impl(ulong WaitHandle)
        {
            var registration = ManagedHandles.Get<WaitRegistration>((long)WaitHandle);
            if (registration == null)
                throw SetFromWindowsErr(ERROR_INVALID_HANDLE);
            ManagedHandles.Close((long)WaitHandle);
            registration.Handle.Unregister(null);
            return PyNone.Instance;
        }

        // _overlapped_UnregisterWaitEx_impl: Event is signalled when the callbacks are done.
        private static PyObject _overlapped_UnregisterWaitEx_impl(ulong WaitHandle, ulong Event)
        {
            var registration = ManagedHandles.Get<WaitRegistration>((long)WaitHandle);
            if (registration == null)
                throw SetFromWindowsErr(ERROR_INVALID_HANDLE);
            WaitHandle completion = null;
            if (Event != 0 && Event != INVALID_HANDLE_VALUE)
            {
                completion = ManagedHandles.Get<EventWaitHandle>((long)Event);
                if (completion == null)
                    throw SetFromWindowsErr(ERROR_INVALID_HANDLE);
            }
            ManagedHandles.Close((long)WaitHandle);
            if (Event == INVALID_HANDLE_VALUE)
            {
                // Wait for the callbacks to finish.
                using var done = new ManualResetEvent(false);
                registration.Handle.Unregister(done);
                PyGil.AllowThreads(() => done.WaitOne());
            }
            else
            {
                registration.Handle.Unregister(completion);
            }
            return PyNone.Instance;
        }

        // ------------------------------------------------------------------ events

        // _overlapped_CreateEvent_impl
        private static PyObject _overlapped_CreateEvent_impl(PyObject EventAttributes, int ManualReset, int InitialState, string Name)
        {
            if (EventAttributes is not PyNone)
                throw PyValueError.Create("EventAttributes must be None");
            var mode = ManualReset != 0 ? EventResetMode.ManualReset : EventResetMode.AutoReset;
            EventWaitHandle ev = Name == null ? new EventWaitHandle(InitialState != 0, mode) : new EventWaitHandle(InitialState != 0, mode, Name);
            return FromVoidPtr((ulong)ManagedHandles.Add(ev));
        }

        // _overlapped_SetEvent_impl
        private static PyObject _overlapped_SetEvent_impl(ulong Handle)
        {
            var ev = ManagedHandles.Get<EventWaitHandle>((long)Handle) ?? throw SetFromWindowsErr(ERROR_INVALID_HANDLE);
            ev.Set();
            return PyNone.Instance;
        }

        // _overlapped_ResetEvent_impl
        private static PyObject _overlapped_ResetEvent_impl(ulong Handle)
        {
            var ev = ManagedHandles.Get<EventWaitHandle>((long)Handle) ?? throw SetFromWindowsErr(ERROR_INVALID_HANDLE);
            ev.Reset();
            return PyNone.Instance;
        }

        // ------------------------------------------------------------------ BindLocal, FormatMessage

        // _overlapped_BindLocal_impl
        private static PyObject _overlapped_BindLocal_impl(ulong Socket, int Family)
        {
            EndPoint addr;
            if (Family == (int)AddressFamily.InterNetwork)
                addr = new IPEndPoint(IPAddress.Any, 0);
            else if (Family == (int)AddressFamily.InterNetworkV6)
                addr = new IPEndPoint(IPAddress.IPv6Any, 0);
            else
                throw PyValueError.Create("expected tuple of length 2 or 4");
            var sock = SocketOf(Socket) ?? throw SetFromWindowsErr((uint)SocketModule.WSAENOTSOCK);
            try
            {
                sock.Bind(addr);
            }
            catch (SocketException error)
            {
                throw SetFromWindowsErr((uint)SocketModule.ErrorCodeOf(error));
            }
            catch (ObjectDisposedException)
            {
                throw SetFromWindowsErr((uint)SocketModule.WSAENOTSOCK);
            }
            return PyNone.Instance;
        }

        // _overlapped_FormatMessage_impl
        private static PyObject _overlapped_FormatMessage_impl(uint code)
        {
            string text = null;
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    text = new System.ComponentModel.Win32Exception((int)code).Message;
                }
                catch
                {
                    text = null;
                }
                // Win32Exception says "Unknown error (0x...)" for a code FormatMessage does not know.
                if (text != null && text.StartsWith("Unknown error", StringComparison.Ordinal))
                    text = null;
            }
            if (text == null)
                return PyString.From($"unknown error code {code}");
            // while (iswspace(lpMsgBuf[n-1])) --n;
            return PyString.From(text.TrimEnd());
        }

        // ------------------------------------------------------------------ address helpers

        // unparse_address
        private static PyObject unparse_address(EndPoint address)
        {
            if (address is IPEndPoint ip)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                    return PyTuple.From(new PyObject[] { PyString.From(SocketAddresses.AddressText(ip.Address)), PyInt.From(ip.Port) });
                if (ip.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    var bare = new IPAddress(ip.Address.GetAddressBytes());
                    return PyTuple.From(new PyObject[]
                    {
                        PyString.From(SocketAddresses.AddressText(bare)), PyInt.From(ip.Port), PyInt.From(0),
                        PyInt.From((uint)ip.Address.ScopeId),
                    });
                }
            }
            throw PyValueError.Create("recvfrom returned unsupported address family");
        }

        // parse_address: (host, port) or (host, port, flowinfo, scope_id), numeric hosts only
        // (WSAStringToAddressW).
        private static IPEndPoint parse_address(PyObject obj)
        {
            var tuple = (PyTuple)obj;
            switch (tuple.Items.Length)
            {
                case 2:
                {
                    // PyArg_ParseTuple(obj, "UH", ...)
                    var host = tuple.Items[0] as PyString
                        ?? throw PyTypeError.Create($"argument 1 must be str, not {tuple.Items[0].GetPyType().FullName}");
                    ushort port = UnsignedShort(tuple.Items[1]);
                    var addr = SocketAddresses.NumericHost(host.Value);
                    if (addr == null || addr.AddressFamily != AddressFamily.InterNetwork)
                        throw SetFromWindowsErr(10022);   // WSAEINVAL
                    return new IPEndPoint(addr, port);
                }
                case 4:
                {
                    var host = tuple.Items[0] as PyString
                        ?? throw PyTypeError.Create($"ConnectEx(): illegal address_as_bytes argument");
                    ushort port = UnsignedShort(tuple.Items[1]);
                    uint flowinfo = (uint)PyArgs.AsUnsignedLong(tuple.Items[2]);
                    uint scope = (uint)PyArgs.AsUnsignedLong(tuple.Items[3]);
                    var addr = SocketAddresses.NumericHost(host.Value);
                    if (addr == null || addr.AddressFamily != AddressFamily.InterNetworkV6)
                        throw SetFromWindowsErr(10022);
                    if (flowinfo != 0)
                        throw SocketModule.FlowInfoUnsupported("ConnectEx");
                    if (scope != 0)
                        addr = new IPAddress(addr.GetAddressBytes(), scope);
                    return new IPEndPoint(addr, port);
                }
                default:
                    throw PyValueError.Create("illegal address_as_bytes argument");
            }
        }

        // The "H" format: an unsigned short, masked as C does without overflow checking.
        private static ushort UnsignedShort(PyObject value)
        {
            if (value is PyFloat)
                throw PyTypeError.Create("integer argument expected, got float");
            return unchecked((ushort)(long)(PyObject.IndexOrThrow(value).Value & 0xFFFF));
        }

        // ------------------------------------------------------------------ the Overlapped type

        // _overlapped_Overlapped_impl
        private static PyObject _overlapped_Overlapped_impl(PyType type, ulong @event)
        {
            if (@event == INVALID_HANDLE_VALUE)
                @event = (ulong)ManagedHandles.Add(new EventWaitHandle(false, EventResetMode.ManualReset));
            return new OverlappedObject(type, @event);
        }

        // Overlapped_clear
        private static void Overlapped_clear(OverlappedObject self)
        {
            switch (self.type)
            {
                case OpType.TYPE_READ:
                case OpType.TYPE_ACCEPT:
                    self.allocated_buffer = null;
                    break;
                case OpType.TYPE_READ_FROM:
                    self.read_from_result = null;
                    self.allocated_buffer = null;
                    break;
                case OpType.TYPE_READ_FROM_INTO:
                    self.read_from_result = null;
                    ReleaseUserBuffer(self);
                    break;
                case OpType.TYPE_WRITE:
                case OpType.TYPE_WRITE_TO:
                case OpType.TYPE_READINTO:
                    ReleaseUserBuffer(self);
                    break;
            }
            self.type = OpType.TYPE_NOT_STARTED;
        }

        private static void ReleaseUserBuffer(OverlappedObject self)
        {
            self.user_buffer?.Release();
            self.user_buffer = null;
            self.write_data = null;
        }

        // _overlapped_Overlapped_cancel_impl
        private static PyObject _overlapped_Overlapped_cancel_impl(OverlappedObject self)
        {
            if (self.type == OpType.TYPE_NOT_STARTED || self.type == OpType.TYPE_WAIT_NAMED_PIPE_AND_CONNECT)
                return PyNone.Instance;
            // CancelIoEx returns ERROR_NOT_FOUND if the I/O completed in-between, which is not an error.
            if (!self.HasCompleted)
                self.Cancel();
            return PyNone.Instance;
        }

        // _overlapped_Overlapped_getresult_impl
        private static PyObject _overlapped_Overlapped_getresult_impl(OverlappedObject self, bool wait)
        {
            if (self.type == OpType.TYPE_NONE)
                throw PyValueError.Create("operation not yet attempted");
            if (self.type == OpType.TYPE_NOT_STARTED)
                throw PyValueError.Create("operation failed to start");

            // GetOverlappedResult
            uint err;
            if (!self.HasCompleted)
            {
                if (wait)
                    PyGil.AllowThreads(() => self.Done.Wait());
                else
                    err = ERROR_IO_INCOMPLETE;
            }
            uint transferred = self.transferred;
            err = self.HasCompleted ? self.status : ERROR_IO_INCOMPLETE;

            self.error = err;
            switch (err)
            {
                case ERROR_SUCCESS:
                case ERROR_MORE_DATA:
                    break;
                case ERROR_BROKEN_PIPE:
                    if (self.type == OpType.TYPE_READ || self.type == OpType.TYPE_READINTO)
                        break;
                    if (self.type == OpType.TYPE_READ_FROM && (self.read_from_result != null || self.allocated_buffer != null))
                        break;
                    if (self.type == OpType.TYPE_READ_FROM_INTO && self.read_from_result != null)
                        break;
                    throw SetFromWindowsErr(err);
                default:
                    throw SetFromWindowsErr(err);
            }

            switch (self.type)
            {
                case OpType.TYPE_READ:
                    return PyBytes.From(self.allocated_buffer.AsSpan(0, (int)transferred).ToArray());
                case OpType.TYPE_READ_FROM:
                {
                    var buffer = PyBytes.From(self.allocated_buffer.AsSpan(0, (int)transferred).ToArray());
                    // unparse the address
                    var addr = unparse_address(self.from_address);
                    // The result is a two item tuple: (message, address)
                    self.read_from_result = PyTuple.From(new PyObject[] { buffer, addr });
                    return self.read_from_result;
                }
                case OpType.TYPE_READ_FROM_INTO:
                {
                    self.CopyIntoUserBuffer();
                    var addr = unparse_address(self.from_address);
                    // The result is a two item tuple: (number of bytes read, address)
                    self.read_from_result = PyTuple.From(new PyObject[] { PyInt.From(transferred), addr });
                    return self.read_from_result;
                }
                case OpType.TYPE_READINTO:
                    self.CopyIntoUserBuffer();
                    return PyInt.From(transferred);
                default:
                    return PyInt.From(transferred);
            }
        }

        // do_ReadFile / ReadFile on a handle: there are no file or pipe handles in SharpPy.
        private static PyObject do_ReadFile(OverlappedObject self, ulong handle)
        {
            Overlapped_clear(self);
            throw SetFromWindowsErr(ERROR_INVALID_HANDLE);
        }

        private static void CheckNotAttempted(OverlappedObject self)
        {
            if (self.type != OpType.TYPE_NONE)
                throw PyValueError.Create("operation already attempted");
        }

        // _overlapped_Overlapped_ReadFile_impl
        private static PyObject _overlapped_Overlapped_ReadFile_impl(OverlappedObject self, ulong handle, uint size)
        {
            CheckNotAttempted(self);
            self.type = OpType.TYPE_READ;
            self.handle = handle;
            self.allocated_buffer = new byte[Math.Max(size, 1)];
            return do_ReadFile(self, handle);
        }

        // _overlapped_Overlapped_ReadFileInto_impl
        private static PyObject _overlapped_Overlapped_ReadFileInto_impl(OverlappedObject self, ulong handle, PyMemoryView bufobj)
        {
            CheckNotAttempted(self);
            self.user_buffer = bufobj;
            self.type = OpType.TYPE_READINTO;
            self.handle = handle;
            return do_ReadFile(self, handle);
        }

        // do_WSARecv
        private static PyObject do_WSARecv(OverlappedObject self, ulong handle, int buflen, uint flags)
        {
            var sock = SocketOf(handle);
            if (sock == null)
            {
                Overlapped_clear(self);
                throw SetFromWindowsErr((uint)SocketModule.WSAENOTSOCK);
            }
            var buffer = self.allocated_buffer ?? (self.into_buffer = new byte[buflen]);
            return Start(self, sock, token => Received(sock.ReceiveAsync(buffer.AsMemory(0, buflen), (SocketFlags)flags, token)));
        }

        private static async Task<(int, EndPoint)> Received(ValueTask<int> receive) => (await receive.ConfigureAwait(false), null);

        // _overlapped_Overlapped_WSARecv_impl
        private static PyObject _overlapped_Overlapped_WSARecv_impl(OverlappedObject self, ulong handle, uint size, uint flags)
        {
            CheckNotAttempted(self);
            self.allocated_buffer = new byte[Math.Max(size, 1)];
            self.type = OpType.TYPE_READ;
            self.handle = handle;
            return do_WSARecv(self, handle, (int)Math.Min(size, int.MaxValue), flags);
        }

        // _overlapped_Overlapped_WSARecvInto_impl
        private static PyObject _overlapped_Overlapped_WSARecvInto_impl(OverlappedObject self, ulong handle, PyMemoryView bufobj, uint flags)
        {
            CheckNotAttempted(self);
            long len = bufobj.Length();
            if (len > uint.MaxValue)
            {
                bufobj.Release();
                throw PyValueError.Create("buffer too large");
            }
            self.user_buffer = bufobj;
            self.type = OpType.TYPE_READINTO;
            self.handle = handle;
            return do_WSARecv(self, handle, (int)Math.Min(len, int.MaxValue), flags);
        }

        // _overlapped_Overlapped_WriteFile_impl
        private static PyObject _overlapped_Overlapped_WriteFile_impl(OverlappedObject self, ulong handle, byte[] bufobj)
        {
            CheckNotAttempted(self);
            self.write_data = bufobj;
            self.type = OpType.TYPE_WRITE;
            self.handle = handle;
            Overlapped_clear(self);
            throw SetFromWindowsErr(ERROR_INVALID_HANDLE);
        }

        // _overlapped_Overlapped_WSASend_impl
        private static PyObject _overlapped_Overlapped_WSASend_impl(OverlappedObject self, ulong handle, byte[] bufobj, uint flags)
        {
            CheckNotAttempted(self);
            self.write_data = bufobj;
            self.type = OpType.TYPE_WRITE;
            self.handle = handle;
            var sock = SocketOf(handle);
            if (sock == null)
            {
                Overlapped_clear(self);
                throw SetFromWindowsErr((uint)SocketModule.WSAENOTSOCK);
            }
            return Start(self, sock, token => Sent(sock.SendAsync(bufobj.AsMemory(), (SocketFlags)flags, token)));
        }

        private static async Task<(int, EndPoint)> Sent(ValueTask<int> send) => (await send.ConfigureAwait(false), null);

        // _overlapped_Overlapped_AcceptEx_impl
        private static PyObject _overlapped_Overlapped_AcceptEx_impl(OverlappedObject self, ulong ListenSocket, ulong AcceptSocket)
        {
            CheckNotAttempted(self);
            // size = sizeof(struct sockaddr_in6) + 16; the buffer of the two addresses.
            self.allocated_buffer = new byte[(28 + 16) * 2];
            self.type = OpType.TYPE_ACCEPT;
            self.handle = ListenSocket;
            var listener = SocketOf(ListenSocket);
            var accepted = SocketOf(AcceptSocket);
            if (listener == null || accepted == null)
            {
                Overlapped_clear(self);
                throw SetFromWindowsErr((uint)SocketModule.WSAENOTSOCK);
            }
            return Start(self, listener, token => Accepted(listener.AcceptAsync(accepted, token)));
        }

        private static async Task<(int, EndPoint)> Accepted(ValueTask<Socket> accept)
        {
            await accept.ConfigureAwait(false);
            return (0, null);
        }

        // _overlapped_Overlapped_ConnectEx_impl
        private static PyObject _overlapped_Overlapped_ConnectEx_impl(OverlappedObject self, ulong ConnectSocket, PyObject AddressObj)
        {
            CheckNotAttempted(self);
            var address = parse_address(AddressObj);
            self.type = OpType.TYPE_CONNECT;
            self.handle = ConnectSocket;
            var sock = SocketOf(ConnectSocket);
            if (sock == null)
            {
                Overlapped_clear(self);
                throw SetFromWindowsErr((uint)SocketModule.WSAENOTSOCK);
            }
            // ConnectEx needs a bound socket; WSAEINVAL otherwise.
            if (sock.LocalEndPoint == null)
            {
                Overlapped_clear(self);
                throw SetFromWindowsErr(10022);
            }
            return Start(self, sock, token => Connected(sock.ConnectAsync(address, token)));
        }

        private static async Task<(int, EndPoint)> Connected(ValueTask connect)
        {
            await connect.ConfigureAwait(false);
            return (0, null);
        }

        // _overlapped_Overlapped_DisconnectEx_impl
        private static PyObject _overlapped_Overlapped_DisconnectEx_impl(OverlappedObject self, ulong Socket, uint flags)
        {
            CheckNotAttempted(self);
            self.type = OpType.TYPE_DISCONNECT;
            self.handle = Socket;
            var sock = SocketOf(Socket);
            if (sock == null)
            {
                Overlapped_clear(self);
                throw SetFromWindowsErr((uint)SocketModule.WSAENOTSOCK);
            }
            bool reuse = (flags & TF_REUSE_SOCKET) != 0;
            return Start(self, sock, token => Connected(sock.DisconnectAsync(reuse, token)));
        }

        // _overlapped_Overlapped_TransmitFile_impl. The file's HANDLE is what msvcrt.get_osfhandle gives,
        // which in SharpPy is the descriptor number itself; its file is read at the OVERLAPPED's offset
        // without moving the file pointer (RandomAccess), and sent, count_to_write bytes or to the end
        // when it is 0. count_per_send and flags (TF_DISCONNECT, TF_REUSE_SOCKET) are not used.
        private static PyObject _overlapped_Overlapped_TransmitFile_impl(OverlappedObject self, ulong Socket, ulong File,
            uint offset, uint offset_high, uint count_to_write, uint count_per_send, uint flags)
        {
            CheckNotAttempted(self);
            self.type = OpType.TYPE_TRANSMIT_FILE;
            self.handle = Socket;
            var sock = SocketOf(Socket);
            if (sock == null)
            {
                Overlapped_clear(self);
                throw SetFromWindowsErr((uint)SocketModule.WSAENOTSOCK);
            }
            Microsoft.Win32.SafeHandles.SafeFileHandle file = null;
            try
            {
                if (File <= int.MaxValue && PyFileDescriptors.Get((int)File) is System.IO.FileStream stream)
                {
                    stream.Flush();
                    file = stream.SafeFileHandle;
                }
            }
            catch (PythonException)
            {
            }
            if (file == null)
            {
                Overlapped_clear(self);
                throw SetFromWindowsErr(ERROR_INVALID_HANDLE);
            }
            long position = ((long)offset_high << 32) | offset;
            return Start(self, sock, token => Transmitted(sock, file, position, count_to_write, token));
        }

        private static async Task<(int, EndPoint)> Transmitted(Socket sock, Microsoft.Win32.SafeHandles.SafeFileHandle file,
            long offset, uint count, CancellationToken token)
        {
            var buffer = new byte[65536];
            long total = 0;
            long remaining = count == 0 ? long.MaxValue : count;
            while (remaining > 0)
            {
                int n = await System.IO.RandomAccess.ReadAsync(file, buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)),
                    offset + total, token).ConfigureAwait(false);
                if (n == 0)
                    break;
                int sent = 0;
                while (sent < n)
                    sent += await sock.SendAsync(buffer.AsMemory(sent, n - sent), SocketFlags.None, token).ConfigureAwait(false);
                total += n;
                remaining -= n;
            }
            return ((int)total, null);
        }

        // _overlapped_Overlapped_ConnectNamedPipe_impl
        private static PyObject _overlapped_Overlapped_ConnectNamedPipe_impl(OverlappedObject self, ulong Pipe)
        {
            CheckNotAttempted(self);
            self.type = OpType.TYPE_CONNECT_NAMED_PIPE;
            self.handle = Pipe;
            Overlapped_clear(self);
            throw SetFromWindowsErr(ERROR_INVALID_HANDLE);
        }

        // _overlapped_Overlapped_ConnectPipe_impl: CreateFileW on the pipe's name. SharpPy has no named
        // pipes (_winapi.CreateNamedPipe raises), so no pipe of that name can exist.
        private static PyObject _overlapped_Overlapped_ConnectPipe_impl(string Address) =>
            throw SetFromWindowsErr(ERROR_FILE_NOT_FOUND);

        // Overlapped_getaddress
        private static PyObject Overlapped_getaddress(OverlappedObject self) => FromVoidPtr(self.address);

        // Overlapped_getpending
        private static PyObject Overlapped_getpending(OverlappedObject self) =>
            PyBool.FromBool(!self.HasCompleted && self.type != OpType.TYPE_NOT_STARTED);

        // _overlapped_WSAConnect_impl: connectionless sockets only; it completes at once.
        private static PyObject _overlapped_WSAConnect_impl(ulong ConnectSocket, PyObject AddressObj)
        {
            var address = parse_address(AddressObj);
            var sock = SocketOf(ConnectSocket) ?? throw SetFromWindowsErr((uint)SocketModule.WSAENOTSOCK);
            try
            {
                PyGil.AllowThreads(() => sock.Connect(address));
            }
            catch (SocketException error)
            {
                throw SetFromWindowsErr((uint)SocketModule.ErrorCodeOf(error));
            }
            return PyNone.Instance;
        }

        // _overlapped_Overlapped_WSASendTo_impl
        private static PyObject _overlapped_Overlapped_WSASendTo_impl(OverlappedObject self, ulong handle, byte[] bufobj,
            uint flags, PyObject AddressObj)
        {
            // Parse the "to" address
            var address = parse_address(AddressObj);
            CheckNotAttempted(self);
            self.write_data = bufobj;
            self.type = OpType.TYPE_WRITE_TO;
            self.handle = handle;
            var sock = SocketOf(handle);
            if (sock == null)
            {
                self.type = OpType.TYPE_NOT_STARTED;
                throw SetFromWindowsErr((uint)SocketModule.WSAENOTSOCK);
            }
            return Start(self, sock, token => Sent(sock.SendToAsync(bufobj.AsMemory(), (SocketFlags)flags, address, token)));
        }

        // _overlapped_Overlapped_WSARecvFrom_impl
        private static PyObject _overlapped_Overlapped_WSARecvFrom_impl(OverlappedObject self, ulong handle, uint size, uint flags)
        {
            CheckNotAttempted(self);
            var buffer = new byte[Math.Max(size, 1)];
            self.type = OpType.TYPE_READ_FROM;
            self.handle = handle;
            self.allocated_buffer = buffer;
            var sock = SocketOf(handle);
            if (sock == null)
            {
                self.type = OpType.TYPE_NOT_STARTED;
                throw SetFromWindowsErr((uint)SocketModule.WSAENOTSOCK);
            }
            int len = (int)Math.Min(size, int.MaxValue);
            return Start(self, sock, token => ReceivedFrom(sock, buffer, len, flags, token));
        }

        private static async Task<(int, EndPoint)> ReceivedFrom(Socket sock, byte[] buffer, int len, uint flags, CancellationToken token)
        {
            EndPoint any = sock.AddressFamily == AddressFamily.InterNetworkV6
                ? new IPEndPoint(IPAddress.IPv6Any, 0) : new IPEndPoint(IPAddress.Any, 0);
            var result = await sock.ReceiveFromAsync(buffer.AsMemory(0, len), (SocketFlags)flags, any, token).ConfigureAwait(false);
            return (result.ReceivedBytes, result.RemoteEndPoint);
        }

        // _overlapped_Overlapped_WSARecvFromInto_impl
        private static PyObject _overlapped_Overlapped_WSARecvFromInto_impl(OverlappedObject self, ulong handle, PyMemoryView bufobj,
            uint size, uint flags)
        {
            CheckNotAttempted(self);
            long buflen = bufobj.Length();
            if (buflen > uint.MaxValue)
            {
                bufobj.Release();
                throw PyValueError.Create("buffer too large");
            }
            if (buflen < size)
            {
                bufobj.Release();
                throw PyValueError.Create("nbytes is greater than the length of the buffer");
            }
            self.type = OpType.TYPE_READ_FROM_INTO;
            self.handle = handle;
            self.user_buffer = bufobj;
            var sock = SocketOf(handle);
            if (sock == null)
            {
                self.type = OpType.TYPE_NOT_STARTED;
                throw SetFromWindowsErr((uint)SocketModule.WSAENOTSOCK);
            }
            int len = (int)Math.Min(size, int.MaxValue);
            var buffer = self.into_buffer = new byte[len];
            return Start(self, sock, token => ReceivedFrom(sock, buffer, len, flags, token));
        }

        /// <summary>
        /// The kernel's side of an overlapped call: the operation runs; an immediate failure is the
        /// call's error (nothing is queued, the OVERLAPPED is cleared); otherwise ERROR_SUCCESS or
        /// ERROR_IO_PENDING, and the completion sets the status, the event and the port's packet.
        /// </summary>
        private static PyObject Start(OverlappedObject self, Socket sock, Func<CancellationToken, Task<(int, EndPoint)>> operation)
        {
            bool datagram = sock.SocketType == SocketType.Dgram;
            self.Begin();
            Task<(int, EndPoint)> task;
            try
            {
                task = operation(self.Token);
            }
            catch (Exception error) when (error is SocketException or ObjectDisposedException or ArgumentException or InvalidOperationException or NotSupportedException)
            {
                task = Task.FromException<(int, EndPoint)>(error);
            }
            if (task.IsCompleted && !task.IsCompletedSuccessfully)
            {
                // The call itself failed: WSAGetLastError() is not ERROR_IO_PENDING, and the error is
                // the Winsock code itself (no IRP completed, so no NTSTATUS mapping).
                uint err = ErrorOf(task, datagram, synchronous: true);
                self.Abandon();
                self.error = err;
                Overlapped_clear(self);
                throw SetFromWindowsErr(err);
            }
            self.error = task.IsCompleted ? ERROR_SUCCESS : ERROR_IO_PENDING;
            if (task.IsCompleted)
                self.Complete(task, datagram);
            else
                task.ContinueWith(t => self.Complete(t, datagram), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return PyNone.Instance;
        }

        // The Win32 error of a finished operation.
        internal static uint ErrorOf(Task task, bool datagram, bool synchronous = false)
        {
            if (task.IsCompletedSuccessfully)
                return ERROR_SUCCESS;
            if (task.IsCanceled)
                return ERROR_OPERATION_ABORTED;
            var error = task.Exception?.GetBaseException();
            return error switch
            {
                SocketException socketError => synchronous ? (uint)SocketModule.ErrorCodeOf(socketError)
                    : Win32ErrorOfSocketError(SocketModule.ErrorCodeOf(socketError), datagram),
                OperationCanceledException => ERROR_OPERATION_ABORTED,
                // closesocket on a socket with pending operations aborts them.
                ObjectDisposedException => ERROR_OPERATION_ABORTED,
                ArgumentException => (uint)10022,                     // WSAEINVAL
                InvalidOperationException => (uint)10022,
                NotSupportedException => (uint)10045,                 // WSAEOPNOTSUPP
                _ => ERROR_OPERATION_ABORTED,
            };
        }

        // The socket's association, for the completion packet.
        internal static bool TryAssociation(ulong handle, out CompletionPort port, out ulong key)
        {
            if (Associations.TryGetValue((long)handle, out var entry) && !entry.Port.Closed)
            {
                port = entry.Port;
                key = entry.Key;
                return true;
            }
            port = null;
            key = 0;
            return false;
        }

        /// <summary>CloseHandle of a socket number forgets its association (closesocket).</summary>
        internal static void ForgetSocket(long handle) => Associations.TryRemove(handle, out _);

        // ------------------------------------------------------------------ module

        internal static PyModule Create()
        {
            var module = new PyModule("_overlapped");
            /* Ensure WSAStartup() called before initializing function pointers */
            PyImportSystem.Import("_socket");

            OverlappedType = MakeOverlappedType();
            var d = module.ModuleDict;
            d["Overlapped"] = OverlappedType;

            // overlapped_functions
            d["CreateIoCompletionPort"] = PyArgs.FastCall("_overlapped", "CreateIoCompletionPort", 4, 4, a =>
                _overlapped_CreateIoCompletionPort_impl(AsVoidPtr(a[0]), AsVoidPtr(a[1]), AsVoidPtr(a[2]), AsDword(a[3])));
            d["GetQueuedCompletionStatus"] = PyArgs.FastCall("_overlapped", "GetQueuedCompletionStatus", 2, 2, a =>
                _overlapped_GetQueuedCompletionStatus_impl(AsVoidPtr(a[0]), AsDword(a[1])));
            d["PostQueuedCompletionStatus"] = PyArgs.FastCall("_overlapped", "PostQueuedCompletionStatus", 4, 4, a =>
                _overlapped_PostQueuedCompletionStatus_impl(AsVoidPtr(a[0]), AsDword(a[1]), AsVoidPtr(a[2]), AsVoidPtr(a[3])));
            d["FormatMessage"] = PyArgs.MethO("_overlapped", "FormatMessage", a => _overlapped_FormatMessage_impl(AsDword(a[0])));
            d["BindLocal"] = PyArgs.FastCall("_overlapped", "BindLocal", 2, 2, a =>
                _overlapped_BindLocal_impl(AsVoidPtr(a[0]), PyArgs.Int(a[1])));
            d["RegisterWaitWithQueue"] = PyArgs.FastCall("_overlapped", "RegisterWaitWithQueue", 4, 4, a =>
                _overlapped_RegisterWaitWithQueue_impl(AsVoidPtr(a[0]), AsVoidPtr(a[1]), AsVoidPtr(a[2]), AsDword(a[3])));
            d["UnregisterWait"] = PyArgs.MethO("_overlapped", "UnregisterWait", a => _overlapped_UnregisterWait_impl(AsVoidPtr(a[0])));
            d["UnregisterWaitEx"] = PyArgs.FastCall("_overlapped", "UnregisterWaitEx", 2, 2, a =>
                _overlapped_UnregisterWaitEx_impl(AsVoidPtr(a[0]), AsVoidPtr(a[1])));
            d["CreateEvent"] = PyArgs.FastCall("_overlapped", "CreateEvent", 4, 4, a =>
            {
                string name = a[3] switch
                {
                    PyNone => null,
                    PyString s => s.Value,
                    _ => throw PyArgs.BadArgument("CreateEvent", "argument 4", "str or None", a[3]),
                };
                return _overlapped_CreateEvent_impl(a[0], AsBool(a[1]), AsBool(a[2]), name);
            });
            d["SetEvent"] = PyArgs.MethO("_overlapped", "SetEvent", a => _overlapped_SetEvent_impl(AsVoidPtr(a[0])));
            d["ResetEvent"] = PyArgs.MethO("_overlapped", "ResetEvent", a => _overlapped_ResetEvent_impl(AsVoidPtr(a[0])));
            d["ConnectPipe"] = PyArgs.MethO("_overlapped", "ConnectPipe", a =>
                _overlapped_Overlapped_ConnectPipe_impl(a[0] is PyString s ? s.Value : throw PyArgs.BadArgument("ConnectPipe", "argument", "str", a[0])));
            d["WSAConnect"] = PyArgs.FastCall("_overlapped", "WSAConnect", 2, 2, a =>
                _overlapped_WSAConnect_impl(AsVoidPtr(a[0]), TupleArg("WSAConnect", "argument 2", a[1])));

            // WINAPI_CONSTANT
            d["ERROR_IO_PENDING"] = PyInt.From(ERROR_IO_PENDING);
            d["ERROR_NETNAME_DELETED"] = PyInt.From(ERROR_NETNAME_DELETED);
            d["ERROR_OPERATION_ABORTED"] = PyInt.From(ERROR_OPERATION_ABORTED);
            d["ERROR_SEM_TIMEOUT"] = PyInt.From(ERROR_SEM_TIMEOUT);
            d["ERROR_PIPE_BUSY"] = PyInt.From(ERROR_PIPE_BUSY);
            d["ERROR_PORT_UNREACHABLE"] = PyInt.From(ERROR_PORT_UNREACHABLE);
            d["INFINITE"] = PyInt.From(INFINITE);
            d["INVALID_HANDLE_VALUE"] = FromVoidPtr(INVALID_HANDLE_VALUE);
            d["NULL"] = PyInt.From(0);
            d["SO_UPDATE_ACCEPT_CONTEXT"] = PyInt.From(SO_UPDATE_ACCEPT_CONTEXT);
            d["SO_UPDATE_CONNECT_CONTEXT"] = PyInt.From(SO_UPDATE_CONNECT_CONTEXT);
            d["TF_REUSE_SOCKET"] = PyInt.From(TF_REUSE_SOCKET);
            return module;
        }

        // object(subclass_of='&PyTuple_Type')
        private static PyObject TupleArg(string fname, string displayname, PyObject arg) =>
            arg is PyTuple ? arg : throw PyArgs.BadArgument(fname, displayname, "tuple", arg);

        // Py_buffer: a C-contiguous buffer, read at the call.
        private static byte[] ReadBuffer(PyObject arg) => BytesMethods.RequireBuffer(arg);

        // Py_buffer for the calls the kernel writes into.
        private static PyMemoryView WritableBuffer(string fname, PyObject arg)
        {
            if (!arg.SupportsBuffer())
                throw PyTypeError.Create($"a bytes-like object is required, not '{arg.GetTypeName()}'");
            return PyIOBaseSlots.WritableBytes(arg, fname);
        }

        private static OverlappedObject O(PyObject o) => (OverlappedObject)o;

        // overlapped_type_spec
        private static PyType MakeOverlappedType()
        {
            var type = new PyType("Overlapped", new[] { PyType.ObjectType }, "_overlapped");
            type.MarkSpecHeapType();
            type.MarkImmutableType();
            type.SetStaticLayout(new TypeLayout(184, 0, 0, 0, false, Final: true, Spec: true));
            type.TypeDict["__module__"] = PyString.From("_overlapped");
            // _overlapped_Overlapped: (event=_overlapped.INVALID_HANDLE_VALUE)
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) =>
            {
                var a = PyArgs.Unpack("Overlapped", args, kwargs, new[] { "event" }, 0, 0, 1);
                return _overlapped_Overlapped_impl((PyType)cls, a[0] == null ? INVALID_HANDLE_VALUE : AsVoidPtr(a[0]));
            }, 0, int.MaxValue, acceptsKwargs: true);

            // Overlapped_methods
            type.TypeDict["getresult"] = PyArgs.Method(type, "getresult", PyCall.Fast, (self, a) =>
                _overlapped_Overlapped_getresult_impl(O(self), a.Length > 0 && AsBool(a[0]) != 0), 0, 1);
            PyArgs.Define(type, "cancel", PyCall.NoArgs, (self, a) => _overlapped_Overlapped_cancel_impl(O(self)));
            PyArgs.Define(type, "ReadFile", PyCall.Fast, (self, a) =>
                _overlapped_Overlapped_ReadFile_impl(O(self), AsVoidPtr(a[0]), AsDword(a[1])), 2, 2);
            PyArgs.Define(type, "ReadFileInto", PyCall.Fast, (self, a) =>
            {
                var handle = AsVoidPtr(a[0]);
                return _overlapped_Overlapped_ReadFileInto_impl(O(self), handle, WritableBuffer("ReadFileInto", a[1]));
            }, 2, 2);
            PyArgs.Define(type, "WSARecv", PyCall.Fast, (self, a) =>
                _overlapped_Overlapped_WSARecv_impl(O(self), AsVoidPtr(a[0]), AsDword(a[1]), a.Length > 2 ? AsDword(a[2]) : 0), 2, 3);
            PyArgs.Define(type, "WSARecvInto", PyCall.Fast, (self, a) =>
            {
                var handle = AsVoidPtr(a[0]);
                var buf = WritableBuffer("WSARecvInto", a[1]);
                uint flags;
                try { flags = AsDword(a[2]); }
                catch { buf.Release(); throw; }
                return _overlapped_Overlapped_WSARecvInto_impl(O(self), handle, buf, flags);
            }, 3, 3);
            PyArgs.Define(type, "WSARecvFrom", PyCall.Fast, (self, a) =>
                _overlapped_Overlapped_WSARecvFrom_impl(O(self), AsVoidPtr(a[0]), AsDword(a[1]), a.Length > 2 ? AsDword(a[2]) : 0), 2, 3);
            PyArgs.Define(type, "WSARecvFromInto", PyCall.Fast, (self, a) =>
            {
                var handle = AsVoidPtr(a[0]);
                var buf = WritableBuffer("WSARecvFromInto", a[1]);
                uint size, flags;
                try
                {
                    size = AsDword(a[2]);
                    flags = a.Length > 3 ? AsDword(a[3]) : 0;
                }
                catch { buf.Release(); throw; }
                return _overlapped_Overlapped_WSARecvFromInto_impl(O(self), handle, buf, size, flags);
            }, 3, 4);
            PyArgs.Define(type, "WriteFile", PyCall.Fast, (self, a) =>
                _overlapped_Overlapped_WriteFile_impl(O(self), AsVoidPtr(a[0]), ReadBuffer(a[1])), 2, 2);
            PyArgs.Define(type, "WSASend", PyCall.Fast, (self, a) =>
            {
                var handle = AsVoidPtr(a[0]);
                var data = ReadBuffer(a[1]);
                return _overlapped_Overlapped_WSASend_impl(O(self), handle, data, AsDword(a[2]));
            }, 3, 3);
            PyArgs.Define(type, "AcceptEx", PyCall.Fast, (self, a) =>
                _overlapped_Overlapped_AcceptEx_impl(O(self), AsVoidPtr(a[0]), AsVoidPtr(a[1])), 2, 2);
            PyArgs.Define(type, "ConnectEx", PyCall.Fast, (self, a) =>
                _overlapped_Overlapped_ConnectEx_impl(O(self), AsVoidPtr(a[0]), TupleArg("ConnectEx", "argument 2", a[1])), 2, 2);
            PyArgs.Define(type, "DisconnectEx", PyCall.Fast, (self, a) =>
                _overlapped_Overlapped_DisconnectEx_impl(O(self), AsVoidPtr(a[0]), AsDword(a[1])), 2, 2);
            PyArgs.Define(type, "TransmitFile", PyCall.Fast, (self, a) =>
                _overlapped_Overlapped_TransmitFile_impl(O(self), AsVoidPtr(a[0]), AsVoidPtr(a[1]), AsDword(a[2]), AsDword(a[3]),
                    AsDword(a[4]), AsDword(a[5]), AsDword(a[6])), 7, 7);
            PyArgs.Define(type, "ConnectNamedPipe", PyCall.Fast, (self, a) =>
                _overlapped_Overlapped_ConnectNamedPipe_impl(O(self), AsVoidPtr(a[0])), 1, 1);
            PyArgs.Define(type, "WSASendTo", PyCall.Fast, (self, a) =>
            {
                var handle = AsVoidPtr(a[0]);
                var data = ReadBuffer(a[1]);
                var flags = AsDword(a[2]);
                return _overlapped_Overlapped_WSASendTo_impl(O(self), handle, data, flags, TupleArg("WSASendTo", "argument 4", a[3]));
            }, 4, 4);

            // Overlapped_members
            type.TypeDict["error"] = new PyMemberDescriptor("error", type, self => PyInt.From(O(self).error), readOnly: true);
            type.TypeDict["event"] = new PyMemberDescriptor("event", type, self => FromVoidPtr(O(self).@event), readOnly: true);
            // Overlapped_getsets
            type.TypeDict["address"] = new PyGetSetDescriptor("address", type, self => Overlapped_getaddress(O(self)));
            type.TypeDict["pending"] = new PyGetSetDescriptor("pending", type, self => Overlapped_getpending(O(self)));
            return type;
        }
    }

    /// <summary>OverlappedObject: the OVERLAPPED, its handle, the last error, and the operation's buffers.</summary>
    internal sealed class OverlappedObject : PyObject
    {
        private static long _nextAddress;

        private readonly PyType _type;
        // &self->overlapped: a number unique to this object, as the address of its OVERLAPPED is.
        internal readonly ulong address;
        internal readonly ulong @event;
        internal ulong handle;
        internal uint error;
        internal OverlappedModule.OpType type = OverlappedModule.OpType.TYPE_NONE;

        // TYPE_READ, TYPE_ACCEPT, TYPE_READ_FROM: the buffer allocated by us
        internal byte[] allocated_buffer;
        // TYPE_WRITE, TYPE_WRITE_TO: the data of the user's buffer
        internal byte[] write_data;
        // TYPE_READINTO, TYPE_READ_FROM_INTO: the user's buffer and what the kernel received for it
        internal PyMemoryView user_buffer;
        internal byte[] into_buffer;
        private bool _copied;
        // TYPE_READ_FROM, TYPE_READ_FROM_INTO
        internal PyObject read_from_result;
        internal EndPoint from_address;

        // OVERLAPPED.Internal and InternalHigh: written by the completion, read by the interpreter.
        private volatile bool _completed = true;
        internal uint status;
        internal uint transferred;
        private CancellationTokenSource _cancel;
        internal readonly ManualResetEventSlim Done = new(true);

        internal OverlappedObject(PyType type, ulong ev)
        {
            _type = type;
            @event = ev;
            address = 0x10000000000UL + (ulong)Interlocked.Increment(ref _nextAddress) * 0x40;
        }

        public override PyType GetPyType() => _type;

        internal bool HasCompleted => _completed;

        internal CancellationToken Token => _cancel.Token;

        // The call begins: Internal = STATUS_PENDING.
        internal void Begin()
        {
            _cancel = new CancellationTokenSource();
            Done.Reset();
            _completed = false;
            if (@event != 0)
                ManagedHandles.Get<EventWaitHandle>((long)@event)?.Reset();
        }

        // The call failed to start: nothing is pending.
        internal void Abandon()
        {
            _completed = true;
            Done.Set();
        }

        // CancelIoEx
        internal void Cancel()
        {
            try
            {
                _cancel?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        // The kernel finishing the I/O: status, bytes, the event, then the port's packet.
        internal void Complete(Task<(int, EndPoint)> task, bool datagram)
        {
            if (_completed)
                return;
            uint err = OverlappedModule.ErrorOf(task, datagram);
            if (task.IsCompletedSuccessfully)
            {
                var (count, from) = task.Result;
                transferred = (uint)count;
                from_address = from;
            }
            status = err;
            _completed = true;
            Done.Set();
            if (@event != 0)
                ManagedHandles.Get<EventWaitHandle>((long)@event)?.Set();
            if (OverlappedModule.TryAssociation(handle, out var port, out var key))
                port.Post(new CompletionPort.Packet(err, transferred, key, address));
        }

        // What the kernel wrote into the user's buffer becomes visible to Python.
        internal void CopyIntoUserBuffer()
        {
            if (_copied || user_buffer == null || into_buffer == null)
                return;
            _copied = true;
            user_buffer.WriteContiguous(into_buffer.AsSpan(0, (int)Math.Min(transferred, (uint)into_buffer.Length)));
        }
    }
}
