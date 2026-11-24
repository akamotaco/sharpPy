using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;

namespace SharpPy.Modules.Ssl
{
    /// <summary>_PySSLError: the last error of SSL_get_error and of the socket.</summary>
    internal struct _PySSLError
    {
        public int ssl, c, ws;
    }

    /// <summary>PySSLSocket: an SSL connection over a socket (weakly referenced) or a pair of memory BIOs.</summary>
    internal sealed class PySSLSocket : PyObject
    {
        internal PyWeakRef Socket;          // weakref to socket on which we're layered
        internal SslConnection Conn;        // SSL *
        internal PySSLContext ctx;
        internal bool shutdown_seen_zero;
        internal int socket_type;
        internal PyWeakRef owner;
        internal PyObject server_hostname;
        internal _PySSLError err;
        internal PythonException exc;

        private PySSLSocket() { }

        public override PyType GetPyType() => SslModule.PySSLSocket_Type;

        private static PySSLSocket Of(PyObject self) => (PySSLSocket)self;

        // timeout_state
        private const int SOCKET_IS_NONBLOCKING = 0, SOCKET_IS_BLOCKING = 1, SOCKET_HAS_TIMED_OUT = 2,
            SOCKET_HAS_BEEN_CLOSED = 3, SOCKET_TOO_LARGE_FOR_SELECT = 4, SOCKET_OPERATION_OK = 5;

        internal static PyType MakeType()
        {
            var type = SslModule.SpecType("_SSLSocket");
            // PySSLMethods
            PyArgs.Define(type, "do_handshake", PyCall.NoArgs, (self, args) => Of(self)._ssl__SSLSocket_do_handshake_impl());
            PyArgs.Define(type, "write", PyCall.One, (self, args) => Of(self)._ssl__SSLSocket_write_impl(BytesMethods.RequireBuffer(args[0])));
            type.TypeDict["read"] = new PyMethodDescriptor("read", type, (self, args, kwargs) =>
            {
                if (kwargs != null && kwargs.Length() != 0)
                    throw PyTypeError.Create("read() takes no keyword arguments");
                switch (args.Length)
                {
                    case 1:
                        return Of(self)._ssl__SSLSocket_read_impl(PyArgs.Ssize(args[0]), false, null);
                    case 2:
                        {
                            var buffer = PyIOBaseSlots.WritableBytes(args[1], "read");
                            try
                            {
                                return Of(self)._ssl__SSLSocket_read_impl(PyArgs.Ssize(args[0]), true, buffer);
                            }
                            finally
                            {
                                buffer.Release();
                            }
                        }
                    default:
                        throw PyTypeError.Create("_ssl._SSLSocket.read requires 1 to 2 arguments");
                }
            }, minArgs: 0, maxArgs: int.MaxValue, acceptsKwargs: true);
            PyArgs.Define(type, "pending", PyCall.NoArgs, (self, args) => Of(self)._ssl__SSLSocket_pending_impl());
            PyArgs.Define(type, "getpeercert", PyCall.Fast, (self, args) =>
                Of(self)._ssl__SSLSocket_getpeercert_impl(args.Length > 0 && args[0].IsTrue()), 0, 1);
            type.TypeDict["get_channel_binding"] = new PyMethodDescriptor("get_channel_binding", type, (self, args, kwargs) =>
            {
                var a = PyArgs.Unpack("get_channel_binding", args, kwargs, new[] { "cb_type" }, 0, 0, 1);
                string cb_type = a[0] == null ? "tls-unique" : PyArgs.ClinicStr("get_channel_binding", "argument 'cb_type'", "str", a[0]);
                return Of(self)._ssl__SSLSocket_get_channel_binding_impl(cb_type);
            }, minArgs: 0, maxArgs: int.MaxValue, acceptsKwargs: true);
            PyArgs.Define(type, "cipher", PyCall.NoArgs, (self, args) => Of(self)._ssl__SSLSocket_cipher_impl());
            PyArgs.Define(type, "shared_ciphers", PyCall.NoArgs, (self, args) => Of(self)._ssl__SSLSocket_shared_ciphers_impl());
            PyArgs.Define(type, "version", PyCall.NoArgs, (self, args) => Of(self)._ssl__SSLSocket_version_impl());
            PyArgs.Define(type, "selected_alpn_protocol", PyCall.NoArgs, (self, args) => Of(self)._ssl__SSLSocket_selected_alpn_protocol_impl());
            PyArgs.Define(type, "compression", PyCall.NoArgs, (self, args) => PyNone.Instance);
            PyArgs.Define(type, "shutdown", PyCall.NoArgs, (self, args) => Of(self)._ssl__SSLSocket_shutdown_impl());
            PyArgs.Define(type, "verify_client_post_handshake", PyCall.NoArgs, (self, args) => Of(self)._ssl__SSLSocket_verify_client_post_handshake_impl());
            PyArgs.Define(type, "get_unverified_chain", PyCall.NoArgs, (self, args) => Of(self)._ssl__SSLSocket_get_unverified_chain_impl());
            PyArgs.Define(type, "get_verified_chain", PyCall.NoArgs, (self, args) => Of(self)._ssl__SSLSocket_get_verified_chain_impl());
            // ssl_getsetlist
            type.TypeDict["context"] = new PyGetSetDescriptor("context", type, self => Of(self).ctx,
                (self, value) => Of(self).PySSL_set_context(value),
                "_setter_context(ctx)\nThis changes the context associated with the SSLSocket. This is typically\nused from within a callback function set by the sni_callback\non the SSLContext to change the certificate information associated with the\nSSLSocket before the cryptographic exchange handshake messages\n");
            type.TypeDict["server_side"] = new PyGetSetDescriptor("server_side", type,
                self => PyBool.FromBool(Of(self).socket_type == SslModule.PY_SSL_SERVER), null, "Whether this is a server-side socket.");
            type.TypeDict["server_hostname"] = new PyGetSetDescriptor("server_hostname", type,
                self => Of(self).server_hostname ?? PyNone.Instance, null, "The currently set server hostname (for SNI).");
            type.TypeDict["owner"] = new PyGetSetDescriptor("owner", type, self => Of(self).PySSL_get_owner(),
                (self, value) => Of(self).PySSL_set_owner(value),
                "The Python-level owner of this object.Passed as \"self\" in servername callback.");
            type.TypeDict["session"] = new PyGetSetDescriptor("session", type, self => Of(self).PySSL_get_session(),
                (self, value) => Of(self).PySSL_set_session(value), "_setter_session(session)\nGet / set SSLSession.");
            type.TypeDict["session_reused"] = new PyGetSetDescriptor("session_reused", type,
                self => PyBool.FromBool(Of(self).Conn.SessionReused), null, "Was the client session reused during handshake?");
            return type;
        }

        /// <summary>GET_SOCKET: the socket (null for a BIO pair); gone when its weakref is dead (Py_None).</summary>
        internal PyObject GetSocket(out bool gone)
        {
            gone = false;
            if (Socket == null)
                return null;
            var s = Socket.Referent;
            if (s == null)
            {
                gone = true;
                return null;
            }
            return s;
        }

        // ---- newPySSLSocket ----

        /// <summary>_ssl_configure_hostname: server_hostname, SNI for a name, and the host or IP to check.</summary>
        private void _ssl_configure_hostname(string hostname)
        {
            /* Disable OpenSSL's special mode with leading dot in hostname */
            if (hostname.Length == 0 || hostname[0] == '.')
                throw PyValueError.Create("server_hostname cannot be an empty string or start with a leading dot.");
            server_hostname = PyString.From(hostname);
        }

        internal static PyObject newPySSLSocket(PySSLContext sslctx, PySocketObject sock, int socket_type, string server_hostname,
            PyObject owner, PyObject session, PySSLMemoryBIO inbio, PySSLMemoryBIO outbio)
        {
            if (socket_type == SslModule.PY_SSL_SERVER && sslctx.Protocol == SslModule.PY_SSL_VERSION_TLS_CLIENT)
                throw SslModule._setSSLError("Cannot create a server socket with a PROTOCOL_TLS_CLIENT context", 0, 809);
            if (socket_type == SslModule.PY_SSL_CLIENT && sslctx.Protocol == SslModule.PY_SSL_VERSION_TLS_SERVER)
                throw SslModule._setSSLError("Cannot create a client socket with a PROTOCOL_TLS_SERVER context", 0, 816);
            var self = new PySSLSocket { ctx = sslctx, socket_type = socket_type };
            ErrQueue.Clear();
            self.Conn = new SslConnection(self, socket_type == SslModule.PY_SSL_SERVER);
            if (sock != null)
            {
                var bio = new SocketBio(self);
                self.Conn.Rbio = self.Conn.Wbio = bio;
            }
            else
            {
                self.Conn.Rbio = new MemoryBioAdapter(inbio);
                self.Conn.Wbio = new MemoryBioAdapter(outbio);
            }
            if (server_hostname != null)
                self._ssl_configure_hostname(server_hostname);
            /* If the socket is in non-blocking mode or timeout mode, set the BIO to non-blocking mode */
            if (sock != null && sock.Timeout >= 0 && self.Conn.Rbio is SocketBio sb)
                sb.NonBlocking = true;
            if (sock != null)
                self.Socket = new PyWeakRef(sock, null);
            if (owner != null && owner is not PyNone)
                self.PySSL_set_owner(owner);
            if (session != null && session is not PyNone)
                self.PySSL_set_session(session);
            return self;
        }

        /// <summary>The socket's blocking mode for its BIO (BIO_set_nbio), as each method sets it.</summary>
        private void SyncBlocking(PySocketObject sock)
        {
            if (sock != null && Conn.Rbio is SocketBio bio)
                bio.NonBlocking = sock.Timeout >= 0;
        }

        private static long DeadlineInit(long timeout) => PyTime.GetMonotonicClock() + timeout;
        private static long DeadlineGet(long deadline) => deadline - PyTime.GetMonotonicClock();

        /// <summary>
        /// PySSL_select: with a timeout, wait for the socket to be readable (writable); else say
        /// whether it is non-blocking or blocking.
        /// </summary>
        private static int PySSL_select(PySocketObject s, bool writing, long timeout)
        {
            /* Nothing to do unless we're in timeout mode (not non-blocking) */
            if (s == null || timeout == 0)
                return SOCKET_IS_NONBLOCKING;
            else if (timeout < 0)
            {
                if (s.Timeout > 0)
                    return SOCKET_HAS_TIMED_OUT;
                else
                    return SOCKET_IS_BLOCKING;
            }
            /* Guard against closed socket */
            var socket = s.Sock;
            if (s.Fd == PySocketObject.InvalidSocket || socket == null)
                return SOCKET_HAS_BEEN_CLOSED;
            long ms = PyTime.AsMilliseconds(timeout, PyTimeRound.Ceiling);
            bool ready = false;
            PyGil.AllowThreads(() =>
            {
                try
                {
                    ready = socket.Poll((int)Math.Min(ms * 1000, int.MaxValue), writing ? SelectMode.SelectWrite : SelectMode.SelectRead);
                }
                catch (Exception error) when (error is SocketException or ObjectDisposedException)
                {
                    ready = true;
                }
            });
            return ready ? SOCKET_OPERATION_OK : SOCKET_HAS_TIMED_OUT;
        }

        private PySocketObject TakeSocket()
        {
            var s = GetSocket(out bool gone);
            if (gone)
                throw SslModule._setSSLError("Underlying socket connection gone", SslModule.PY_SSL_ERROR_NO_SOCKET, 951);
            return s as PySocketObject;
        }

        private void SetErr(bool failed, int sslError)
        {
            err = default;
            if (failed)
            {
                err.ssl = sslError;
                // OpenSSL's socket BIO leaves the Winsock error in errno (WSAGetLastError is reset
                // by then), so PySSL_SetError raises it as PyErr_SetFromErrno does.
                err.c = (Conn.Rbio as SocketBio)?.Error ?? 0;
                if (Conn.Rbio is SocketBio b)
                    b.Error = 0;
            }
        }

        /// <summary>_ssl._SSLSocket.do_handshake()</summary>
        private PyObject _ssl__SSLSocket_do_handshake_impl()
        {
            var sock = TakeSocket();
            SyncBlocking(sock);
            long timeout = sock?.Timeout ?? 0;
            bool has_timeout = timeout > 0;
            long deadline = has_timeout ? DeadlineInit(timeout) : 0;
            int ret;
            int sockstate;
            /* Actually negotiate SSL connection */
            do
            {
                ret = Conn.DoHandshake(out int e);
                SetErr(ret < 1, e);
                if (has_timeout)
                    timeout = DeadlineGet(deadline);
                if (err.ssl == SslConnection.SSL_ERROR_WANT_READ)
                    sockstate = PySSL_select(sock, false, timeout);
                else if (err.ssl == SslConnection.SSL_ERROR_WANT_WRITE)
                    sockstate = PySSL_select(sock, true, timeout);
                else
                    sockstate = SOCKET_OPERATION_OK;
                if (sockstate == SOCKET_HAS_TIMED_OUT)
                    throw SslModule.PySSL_ChainExceptions(this, SslModule.SetString(PyType.TimeoutErrorType, "_ssl.c:993: The handshake operation timed out"));
                else if (sockstate == SOCKET_HAS_BEEN_CLOSED)
                    throw SslModule.PySSL_ChainExceptions(this, SslModule.SetString(SslModule.PySSLErrorObject, "_ssl.c:997: Underlying socket has been closed."));
                else if (sockstate == SOCKET_TOO_LARGE_FOR_SELECT)
                    throw SslModule.PySSL_ChainExceptions(this, SslModule.SetString(SslModule.PySSLErrorObject, "_ssl.c:1001: Underlying socket too large for select()."));
                else if (sockstate == SOCKET_IS_NONBLOCKING)
                    break;
            } while (err.ssl == SslConnection.SSL_ERROR_WANT_READ || err.ssl == SslConnection.SSL_ERROR_WANT_WRITE);
            if (ret < 1)
                throw SslModule.PySSL_SetError(this, ret, 1010);
            if (exc != null)
                throw SslModule.PySSL_ChainExceptions(this, null);
            return PyNone.Instance;
        }

        /// <summary>_ssl._SSLSocket.getpeercert(der=False, /)</summary>
        private PyObject _ssl__SSLSocket_getpeercert_impl(bool binary_mode)
        {
            if (!Conn.InitFinished)
                throw PyValueError.Create("handshake not done yet");
            var peer_cert = Conn.PeerCert;
            if (peer_cert == null)
                return PyNone.Instance;
            if (binary_mode)
                /* return cert in DER-encoded format */
                return SslModule._certificate_to_der(peer_cert);
            if (ctx.VerifyMode == SslModule.PY_SSL_CERT_NONE)
                return new PyDict();
            return SslModule._decode_certificate(peer_cert);
        }

        /// <summary>_ssl._SSLSocket.get_verified_chain()</summary>
        private PyObject _ssl__SSLSocket_get_verified_chain_impl()
        {
            var chain = Conn.VerifiedChain;
            if (chain == null)
                return PyNone.Instance;
            return new PyList(chain.Select(c => (PyObject)new PySSLCertificate(c)).ToList());
        }

        /// <summary>_ssl._SSLSocket.get_unverified_chain(): the peer's chain (a server's peer is put first).</summary>
        private PyObject _ssl__SSLSocket_get_unverified_chain_impl()
        {
            var chain = Conn.PeerChain;
            if (chain == null)
                return PyNone.Instance;
            return new PyList(chain.Select(c => (PyObject)new PySSLCertificate(c)).ToList());
        }

        /// <summary>_ssl._SSLSocket.shared_ciphers(): the context's ciphers the client also offered (server side).</summary>
        private PyObject _ssl__SSLSocket_shared_ciphers_impl()
        {
            var server_ciphers = ctx.Ciphers;
            var client_ciphers = Conn.ClientCiphers;
            if (server_ciphers == null || client_ciphers == null)
                return PyNone.Instance;
            var res = new List<PyObject>();
            foreach (var cipher in server_ciphers)
                if (client_ciphers.Contains(cipher.Iana))
                    res.Add(PySSLContext.cipher_to_tuple(cipher));
            return new PyList(res);
        }

        /// <summary>_ssl._SSLSocket.cipher()</summary>
        private PyObject _ssl__SSLSocket_cipher_impl()
        {
            var current = Conn?.Cipher;
            if (current == null)
                return PyNone.Instance;
            return PySSLContext.cipher_to_tuple(current);
        }

        /// <summary>_ssl._SSLSocket.version()</summary>
        private PyObject _ssl__SSLSocket_version_impl()
        {
            if (Conn == null || !Conn.InitFinished || Conn.Version == null)
                return PyNone.Instance;
            return PyString.From(Conn.Version);
        }

        /// <summary>_ssl._SSLSocket.selected_alpn_protocol()</summary>
        private PyObject _ssl__SSLSocket_selected_alpn_protocol_impl()
        {
            var output = Conn.Alpn;
            if (output == null)
                return PyNone.Instance;
            return SslModule.Utf8String(output);
        }

        /// <summary>PySSL_set_context: the context of the connection (typically from sni_callback).</summary>
        private void PySSL_set_context(PyObject value)
        {
            if (value is PySSLContext context)
                ctx = context;
            else
                throw PyTypeError.Create("The value must be a SSLContext");
        }

        private PyObject PySSL_get_owner()
        {
            if (owner == null)
                return PyNone.Instance;
            return owner.Referent ?? PyNone.Instance;
        }

        private void PySSL_set_owner(PyObject value)
        {
            if (!value.GetPyType().SupportsWeakReference())
                throw PyTypeError.Create($"cannot create weak reference to '{value.GetTypeName()}' object");
            owner = new PyWeakRef(value, null);
        }

        /// <summary>_servername_callback: the context's sni_callback(sslsocket, server_name, ctx).</summary>
        internal void ServerNameCallback(string servername)
        {
            var sslctx = ctx;
            var sni_cb = sslctx.set_sni_cb;
            if (sni_cb == null)
                return;
            PyObject ssl_socket;
            if (owner != null)
                ssl_socket = owner.Referent;
            else if (Socket != null)
                ssl_socket = Socket.Referent;
            else
                ssl_socket = this;
            if (ssl_socket == null)
                throw new SslConnection.SniAlert(80);
            PyObject result;
            try
            {
                result = sni_cb.Call(new PyObject[] { ssl_socket, servername == null ? PyNone.Instance : PyString.From(servername), sslctx }, null);
            }
            catch (PythonException error)
            {
                PyUnraisable.Report(error, sni_cb);
                throw new SslConnection.SniAlert(40);
            }
            if (result is PyNone)
                return;
            int al;
            try
            {
                al = (int)PyArgs.CLong(result);
            }
            catch (PythonException error)
            {
                PyUnraisable.Report(error, result);
                al = 80;
            }
            throw new SslConnection.SniAlert(al);
        }

        /// <summary>_ssl._SSLSocket.write(b, /)</summary>
        private PyObject _ssl__SSLSocket_write_impl(byte[] b)
        {
            var sock = TakeSocket();
            SyncBlocking(sock);
            long timeout = sock?.Timeout ?? 0;
            bool has_timeout = timeout > 0;
            long deadline = has_timeout ? DeadlineInit(timeout) : 0;
            int sockstate = PySSL_select(sock, true, timeout);
            if (sockstate == SOCKET_HAS_TIMED_OUT)
                throw SslModule.PySSL_ChainExceptions(this, SslModule.SetString(PyType.TimeoutErrorType, "The write operation timed out"));
            else if (sockstate == SOCKET_HAS_BEEN_CLOSED)
                throw SslModule.PySSL_ChainExceptions(this, SslModule.SetString(SslModule.PySSLErrorObject, "Underlying socket has been closed."));
            else if (sockstate == SOCKET_TOO_LARGE_FOR_SELECT)
                throw SslModule.PySSL_ChainExceptions(this, SslModule.SetString(SslModule.PySSLErrorObject, "Underlying socket too large for select()."));
            int retval, count;
            do
            {
                retval = Conn.WriteEx(b, out count, out int e);
                SetErr(retval == 0, e);
                if (has_timeout)
                    timeout = DeadlineGet(deadline);
                if (err.ssl == SslConnection.SSL_ERROR_WANT_READ)
                    sockstate = PySSL_select(sock, false, timeout);
                else if (err.ssl == SslConnection.SSL_ERROR_WANT_WRITE)
                    sockstate = PySSL_select(sock, true, timeout);
                else
                    sockstate = SOCKET_OPERATION_OK;
                if (sockstate == SOCKET_HAS_TIMED_OUT)
                    throw SslModule.PySSL_ChainExceptions(this, SslModule.SetString(PyType.TimeoutErrorType, "The write operation timed out"));
                else if (sockstate == SOCKET_HAS_BEEN_CLOSED)
                    throw SslModule.PySSL_ChainExceptions(this, SslModule.SetString(SslModule.PySSLErrorObject, "Underlying socket has been closed."));
                else if (sockstate == SOCKET_IS_NONBLOCKING)
                    break;
            } while (err.ssl == SslConnection.SSL_ERROR_WANT_READ || err.ssl == SslConnection.SSL_ERROR_WANT_WRITE);
            if (retval == 0)
                throw SslModule.PySSL_SetError(this, retval, 2427);
            if (exc != null)
                throw SslModule.PySSL_ChainExceptions(this, null);
            return PyInt.From(count);
        }

        /// <summary>_ssl._SSLSocket.pending()</summary>
        private PyObject _ssl__SSLSocket_pending_impl()
        {
            err = default;
            return PyInt.From(Conn.Pending);
        }

        /// <summary>_ssl._SSLSocket.read(size, [buffer])</summary>
        private PyObject _ssl__SSLSocket_read_impl(long len, bool group_right_1, PyMemoryView buffer)
        {
            if (!group_right_1 && len < 0)
                throw PyValueError.Create("size should not be negative");
            var sock = TakeSocket();
            if (!group_right_1)
            {
                if (len == 0)
                    return PyBytes.From(Array.Empty<byte>());
            }
            else
            {
                long buflen = buffer.Length();
                if (len <= 0 || len > buflen)
                {
                    len = buflen;
                    if (len > int.MaxValue)
                        throw PyOverflowError.Create("maximum length can't fit in a C 'int'");
                    if (len == 0)
                        return PyInt.From(0);
                }
            }
            if (len > int.MaxValue)
                len = int.MaxValue;
            var mem = new byte[Math.Min(len, 1 << 20)];
            SyncBlocking(sock);
            long timeout = sock?.Timeout ?? 0;
            bool has_timeout = timeout > 0;
            long deadline = has_timeout ? DeadlineInit(timeout) : 0;
            int retval, count = 0;
            int sockstate;
            do
            {
                retval = Conn.ReadEx(mem, 0, mem.Length, out count, out int e);
                SetErr(retval == 0, e);
                if (has_timeout)
                    timeout = DeadlineGet(deadline);
                if (err.ssl == SslConnection.SSL_ERROR_WANT_READ)
                    sockstate = PySSL_select(sock, false, timeout);
                else if (err.ssl == SslConnection.SSL_ERROR_WANT_WRITE)
                    sockstate = PySSL_select(sock, true, timeout);
                else if (err.ssl == SslConnection.SSL_ERROR_ZERO_RETURN && Conn.Shutdown == SslConnection.SSL_RECEIVED_SHUTDOWN)
                {
                    count = 0;
                    goto done;
                }
                else
                    sockstate = SOCKET_OPERATION_OK;
                if (sockstate == SOCKET_HAS_TIMED_OUT)
                    throw SslModule.PySSL_ChainExceptions(this, SslModule.SetString(PyType.TimeoutErrorType, "The read operation timed out"));
                else if (sockstate == SOCKET_IS_NONBLOCKING)
                    break;
            } while (err.ssl == SslConnection.SSL_ERROR_WANT_READ || err.ssl == SslConnection.SSL_ERROR_WANT_WRITE);
            if (retval == 0)
                throw SslModule.PySSL_SetError(this, retval, 2580);
            if (exc != null)
                throw SslModule.PySSL_ChainExceptions(this, null);
            done:
            if (!group_right_1)
                return PyBytes.From(mem.AsSpan(0, count).ToArray());
            buffer.WriteContiguous(mem.AsSpan(0, count));
            return PyInt.From(count);
        }

        /// <summary>_ssl._SSLSocket.shutdown(): the close_notify exchange; the socket (or None) back.</summary>
        private PyObject _ssl__SSLSocket_shutdown_impl()
        {
            var s = GetSocket(out bool gone);
            var sock = s as PySocketObject;
            if (Socket != null && (gone || sock == null || sock.Fd == PySocketObject.InvalidSocket))
                throw SslModule._setSSLError("Underlying socket connection gone", SslModule.PY_SSL_ERROR_NO_SOCKET, 2626);
            SyncBlocking(sock);
            long timeout = sock?.Timeout ?? 0;
            bool has_timeout = timeout > 0;
            long deadline = has_timeout ? DeadlineInit(timeout) : 0;
            int zeros = 0, ret;
            while (true)
            {
                ret = Conn.DoShutdown(out int e);
                SetErr(ret < 0, e);
                /* If err == 1, a secure shutdown with SSL_shutdown() is complete */
                if (ret > 0)
                    break;
                if (ret == 0)
                {
                    /* Don't loop endlessly; instead preserve legacy
                       behaviour of trying SSL_shutdown() only twice.
                       This looks necessary for OpenSSL < 0.9.8m */
                    if (++zeros > 1)
                        break;
                    /* Shutdown was sent, now try receiving */
                    shutdown_seen_zero = true;
                    continue;
                }
                if (has_timeout)
                    timeout = DeadlineGet(deadline);
                /* Possibly retry shutdown until timeout or failure */
                int sockstate;
                if (err.ssl == SslConnection.SSL_ERROR_WANT_READ)
                    sockstate = PySSL_select(sock, false, timeout);
                else if (err.ssl == SslConnection.SSL_ERROR_WANT_WRITE)
                    sockstate = PySSL_select(sock, true, timeout);
                else
                    break;
                if (sockstate == SOCKET_HAS_TIMED_OUT)
                {
                    if (err.ssl == SslConnection.SSL_ERROR_WANT_READ)
                        throw SslModule.PySSL_ChainExceptions(this, SslModule.SetString(PyType.TimeoutErrorType, "The read operation timed out"));
                    else
                        throw SslModule.PySSL_ChainExceptions(this, SslModule.SetString(PyType.TimeoutErrorType, "The write operation timed out"));
                }
                else if (sockstate == SOCKET_TOO_LARGE_FOR_SELECT)
                    throw SslModule.PySSL_ChainExceptions(this, SslModule.SetString(SslModule.PySSLErrorObject, "Underlying socket too large for select()."));
                else if (sockstate != SOCKET_OPERATION_OK)
                    /* Retain the SSL error code */
                    break;
            }
            if (ret < 0)
                throw SslModule.PySSL_SetError(this, ret, 2706);
            if (exc != null)
                throw SslModule.PySSL_ChainExceptions(this, null);
            return (PyObject)sock ?? PyNone.Instance;
        }

        /// <summary>_ssl._SSLSocket.get_channel_binding(cb_type="tls-unique")</summary>
        private PyObject _ssl__SSLSocket_get_channel_binding_impl(string cb_type)
        {
            if (cb_type != "tls-unique")
                throw PyValueError.Create($"'{cb_type}' channel binding type not implemented");
            var data = Conn.InitFinished ? Conn.FinishedPeer : null;
            if (data == null || data.Length == 0)
                return PyNone.Instance;
            return PyBytes.From(data);
        }

        /// <summary>
        /// _ssl._SSLSocket.verify_client_post_handshake(): SSL_verify_client_post_handshake. It needs a
        /// TLS 1.3 server connection whose client offered post-handshake authentication; SslStream
        /// cannot send a CertificateRequest after the handshake (docs/todo.md 5-29).
        /// </summary>
        private PyObject _ssl__SSLSocket_verify_client_post_handshake_impl()
        {
            SslErr Reason(string mnemonic, int code, string text)
            {
                var e = SslErr.Of(SslErr.LIB_SSL, mnemonic);
                e.Reason = code;
                e.Text = text;
                return e;
            }
            // SSL_verify_client_post_handshake's checks, in its order
            if (Conn.Version != "TLSv1.3")
                ErrQueue.Raise(SslErr.Of(SslErr.LIB_SSL, "WRONG_SSL_VERSION"));
            else if (!Conn.Server)
                ErrQueue.Raise(Reason("NOT_SERVER", 284, "not server"));
            else if (!Conn.InitFinished)
                ErrQueue.Raise(Reason("STILL_IN_INIT", 121, "still in init"));
            else if (!Conn.ClientOfferedPha)
                ErrQueue.Raise(Reason("EXTENSION_NOT_RECEIVED", 279, "extension not received"));
            else
                // SslStream cannot send a CertificateRequest after the handshake (docs/todo.md 5-29); the
                // certificate was asked for in the handshake instead.
                throw SslModule._setSSLError("post-handshake authentication is not supported by SslStream", 0, 2781);
            throw SslModule._setSSLError(null, 0, 2781);
        }

        /// <summary>PySSL_get_session: SslStream exposes no session, so there is none (docs/todo.md 5-29).</summary>
        private PyObject PySSL_get_session() => PyNone.Instance;

        /// <summary>PySSL_set_session</summary>
        private void PySSL_set_session(PyObject value)
        {
            if (value is not PySSLSession pysess)
                throw PyTypeError.Create("Value is not a SSLSession.");
            if (!ReferenceEquals(ctx, pysess.ctx))
                throw PyValueError.Create("Session refers to a different SSLContext.");
            if (socket_type != SslModule.PY_SSL_CLIENT)
                throw PyValueError.Create("Cannot set session for server-side SSLSocket.");
            if (Conn.InitFinished)
                throw PyValueError.Create("Cannot set session after handshake.");
            // SSL_set_session: SslStream resumes (or not) on its own; the session is not passed on.
        }
    }

    /// <summary>PySSLMemoryBIO: BIO_s_mem.</summary>
    internal sealed class PySSLMemoryBIO : PyObject
    {
        private readonly List<byte> _data = new();
        internal bool eof_written;

        private PySSLMemoryBIO() { }

        public override PyType GetPyType() => SslModule.PySSLMemoryBIO_Type;

        private static PySSLMemoryBIO Of(PyObject self) => (PySSLMemoryBIO)self;

        internal static PyType MakeType()
        {
            var type = SslModule.SpecType("MemoryBIO");
            // _ssl_MemoryBIO (tp_new)
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) =>
            {
                if (args.Length != 0)
                    throw PyTypeError.Create("MemoryBIO() takes no positional arguments");
                if (kwargs != null && kwargs.Length() != 0)
                    throw PyTypeError.Create("MemoryBIO() takes no keyword arguments");
                return new PySSLMemoryBIO();
            }, 0, int.MaxValue, acceptsKwargs: true);
            PyArgs.Define(type, "read", PyCall.Fast, (self, args) =>
                Of(self)._ssl_MemoryBIO_read_impl(args.Length > 0 ? PyArgs.ParseInt(args[0]) : -1), 0, 1);
            PyArgs.Define(type, "write", PyCall.One, (self, args) => Of(self)._ssl_MemoryBIO_write_impl(BytesMethods.RequireBuffer(args[0])));
            PyArgs.Define(type, "write_eof", PyCall.NoArgs, (self, args) => Of(self)._ssl_MemoryBIO_write_eof_impl());
            type.TypeDict["pending"] = new PyGetSetDescriptor("pending", type, self => PyInt.From(Of(self).Pending), null,
                "The number of bytes pending in the memory BIO.");
            type.TypeDict["eof"] = new PyGetSetDescriptor("eof", type,
                self => PyBool.FromBool(Of(self).Pending == 0 && Of(self).eof_written), null, "Whether the memory BIO is at EOF.");
            return type;
        }

        internal int Pending
        {
            get { lock (_data) return _data.Count; }
        }

        internal int ReadInto(byte[] buffer, int offset, int count)
        {
            lock (_data)
            {
                int n = Math.Min(count, _data.Count);
                _data.CopyTo(0, buffer, offset, n);
                _data.RemoveRange(0, n);
                return n;
            }
        }

        internal void Append(byte[] buffer, int offset, int count)
        {
            lock (_data)
                _data.AddRange(new ArraySegment<byte>(buffer, offset, count));
        }

        /// <summary>_ssl.MemoryBIO.read(size=-1, /)</summary>
        private PyObject _ssl_MemoryBIO_read_impl(int len)
        {
            lock (_data)
            {
                int avail = _data.Count;
                if (len < 0 || len > avail)
                    len = avail;
                var result = new byte[len];
                ReadInto(result, 0, len);
                return PyBytes.From(result);
            }
        }

        /// <summary>_ssl.MemoryBIO.write(b, /)</summary>
        private PyObject _ssl_MemoryBIO_write_impl(byte[] b)
        {
            if (eof_written)
                throw SslModule.SetString(SslModule.PySSLErrorObject, "cannot write() after write_eof()");
            Append(b, 0, b.Length);
            return PyInt.From(b.Length);
        }

        /// <summary>_ssl.MemoryBIO.write_eof()</summary>
        private PyObject _ssl_MemoryBIO_write_eof_impl()
        {
            eof_written = true;
            return PyNone.Instance;
        }
    }

    /// <summary>PySSLSession: never made here, since SslStream exposes no session (docs/todo.md 5-29).</summary>
    internal sealed class PySSLSession : PyObject
    {
        internal PySSLContext ctx;
        internal byte[] Id = Array.Empty<byte>();

        public override PyType GetPyType() => SslModule.PySSLSession_Type;

        internal static PyType MakeType()
        {
            var type = SslModule.SpecType("SSLSession");
            type.TypeDict["has_ticket"] = new PyGetSetDescriptor("has_ticket", type, self => PyBool.False, null, "Does the session contain a ticket?");
            type.TypeDict["id"] = new PyGetSetDescriptor("id", type, self => PyBytes.From(((PySSLSession)self).Id), null, "Session id");
            type.TypeDict["ticket_lifetime_hint"] = new PyGetSetDescriptor("ticket_lifetime_hint", type, self => PyInt.From(0), null, "Ticket life time hint.");
            type.TypeDict["time"] = new PyGetSetDescriptor("time", type, self => PyInt.From(0), null, "Session creation time (seconds since epoch).");
            type.TypeDict["timeout"] = new PyGetSetDescriptor("timeout", type, self => PyInt.From(0), null, "Session timeout (delta in seconds).");
            return type;
        }

        /// <summary>PySSLSession_richcompare: == and != by session id.</summary>
        protected internal override PyObject TpRichCompare(PyObject other, CompareOp op)
        {
            if (other is not PySSLSession right)
                return PyNotImplemented.Instance;
            int result = ReferenceEquals(this, right) ? 0 : Id.AsSpan().SequenceEqual(right.Id) ? 0 : 1;
            return op switch
            {
                CompareOp.EQ => PyBool.FromBool(result == 0),
                CompareOp.NE => PyBool.FromBool(result != 0),
                _ => PyNotImplemented.Instance,
            };
        }
    }

    /// <summary>PySSLCertificate (_ssl/cert.c): an X509 with public_bytes and get_info.</summary>
    internal sealed class PySSLCertificate : PyObject
    {
        internal readonly X509Cert cert;
        private long hash = -1;

        internal PySSLCertificate(X509Cert x509) => cert = x509;

        public override PyType GetPyType() => SslModule.PySSLCertificate_Type;

        internal static PyType MakeType()
        {
            var type = SslModule.SpecType("Certificate");
            type.TypeDict["public_bytes"] = new PyMethodDescriptor("public_bytes", type, (self, args, kwargs) =>
            {
                var a = PyArgs.Unpack("public_bytes", args, kwargs, new[] { "format" }, 0, 0, 1);
                int format = a[0] == null ? SslModule.PY_SSL_ENCODING_PEM : PyArgs.ParseInt(a[0]);
                return ((PySSLCertificate)self)._ssl_Certificate_public_bytes_impl(format);
            }, minArgs: 0, maxArgs: int.MaxValue, acceptsKwargs: true);
            PyArgs.Define(type, "get_info", PyCall.NoArgs, (self, args) => SslModule._decode_certificate(((PySSLCertificate)self).cert));
            type.TypeDict["__repr__"] = new PyWrapperDescriptor("__repr__", type,
                (self, args, kwargs) => ((PySSLCertificate)self).Repr(), minArgs: 0, maxArgs: 0);
            return type;
        }

        /// <summary>_ssl.Certificate.public_bytes(format=Encoding.PEM)</summary>
        private PyObject _ssl_Certificate_public_bytes_impl(int format)
        {
            switch (format)
            {
                case SslModule.PY_SSL_ENCODING_PEM:
                case SslModule.PY_SSL_ENCODING_PEM_AUX:
                    return PyString.From(cert.ToPem());
                case SslModule.PY_SSL_ENCODING_DER:
                    return PyBytes.From(cert.Der);
                default:
                    throw PyValueError.Create("Unsupported format");
            }
        }

        /// <summary>certificate_repr: "&lt;_ssl.Certificate 'RFC 2253 subject'&gt;"</summary>
        protected internal override PyString Repr() =>
            PyString.From($"<{GetPyType().FullName} '{cert.Subject.PrintRfc2253()}'>");

        /// <summary>certificate_hash: X509_subject_name_hash (-1 made -2).</summary>
        protected internal override long Hash()
        {
            if (hash == -1)
            {
                long h = cert.Subject.Hash();
                hash = h == -1 ? -2 : h;
            }
            return hash;
        }

        /// <summary>certificate_richcompare: == and != by X509_cmp.</summary>
        protected internal override PyObject TpRichCompare(PyObject other, CompareOp op)
        {
            if (other is not PySSLCertificate o)
                return PyNotImplemented.Instance;
            if (op != CompareOp.EQ && op != CompareOp.NE)
                return PyNotImplemented.Instance;
            bool same = X509Cert.SameCert(cert, o.cert);
            return PyBool.FromBool(op == CompareOp.EQ ? same : !same);
        }
    }
}
