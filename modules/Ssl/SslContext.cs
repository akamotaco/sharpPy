using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace SharpPy.Modules.Ssl
{
    /// <summary>SSL_CTX_sess_* counters.</summary>
    internal sealed class SessionStats
    {
        public long number, connect, connect_good, connect_renegotiate, accept, accept_good, accept_renegotiate,
            hits, misses, timeouts, cache_full;
    }

    /// <summary>
    /// PySSLContext: what SSL_CTX holds for the connections made from it — the method's version,
    /// options, verify mode and flags, the version bounds, the cipher list, ALPN, the certificate
    /// and key, the trust store — and the Python-level fields of the C struct.
    /// </summary>
    internal sealed class PySSLContext : PyObject
    {
        // PySSLContext fields
        internal byte[] AlpnProtocols;
        internal PyObject set_sni_cb;
        internal bool CheckHostname;
        internal uint HostFlags;
        internal int Protocol;
        internal bool PostHandshakeAuth;
        internal PyObject msg_cb;
        internal PyObject keylog_filename;

        // SSL_CTX
        internal ulong Options;
        internal int VerifyMode;                     // PY_SSL_CERT_*
        internal ulong VerifyFlags;
        internal int MinProto, MaxProto;             // 0: no bound
        internal int MethodVersion;                  // TLSv1_method & co.: the one version, else 0
        internal List<SslCipher> Ciphers;
        internal int SecurityLevel = 1;
        internal int NumTickets = 2;
        internal readonly SslStore Store = new();
        internal X509Certificate2 Certificate;
        internal X509Cert CertificateX509;
        internal readonly List<X509Certificate2> ChainCertificates = new();
        internal readonly SessionStats Stats = new();

        internal PySSLContext(PyType type)
        {
            if (!ReferenceEquals(type, SslModule.PySSLContext_Type))
                SetCustomType(type);
        }

        public override PyType GetPyType() => _customType ?? SslModule.PySSLContext_Type;

        private static PySSLContext Of(PyObject self) => (PySSLContext)self;

        internal static PyType MakeType()
        {
            var type = SslModule.SpecType("_SSLContext");
            // _ssl__SSLContext (tp_new)
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) =>
            {
                var t = (PyType)cls;
                if (kwargs != null && kwargs.Length() != 0)
                    throw PyTypeError.Create("_SSLContext() takes no keyword arguments");
                if (args.Length != 1)
                    throw PyTypeError.Create($"_SSLContext expected 1 argument, got {args.Length}");
                return _ssl__SSLContext_impl(t, PyArgs.ParseInt(args[0]));
            }, 0, int.MaxValue, acceptsKwargs: true);

            // context_methods
            type.TypeDict["_wrap_socket"] = new PyMethodDescriptor("_wrap_socket", type, (self, args, kwargs) =>
            {
                var a = PyArgs.Unpack("_wrap_socket", args, kwargs, new[] { "sock", "server_side", "server_hostname", "owner", "session" }, 0, 2, 3);
                if (!a[0].GetPyType().IsSubclassOf(SslModule.Sock_Type))
                    throw PyTypeError.Create($"_wrap_socket() argument 'sock' must be _socket.socket, not {a[0].GetTypeName()}");
                return Of(self)._ssl__SSLContext__wrap_socket_impl(a[0], a[1].IsTrue(), a[2] ?? PyNone.Instance, a[3] ?? PyNone.Instance, a[4] ?? PyNone.Instance);
            }, minArgs: 0, maxArgs: int.MaxValue, acceptsKwargs: true);
            type.TypeDict["_wrap_bio"] = new PyMethodDescriptor("_wrap_bio", type, (self, args, kwargs) =>
            {
                var a = PyArgs.Unpack("_wrap_bio", args, kwargs, new[] { "incoming", "outgoing", "server_side", "server_hostname", "owner", "session" }, 0, 3, 4);
                if (a[0] is not PySSLMemoryBIO incoming)
                    throw PyTypeError.Create($"_wrap_bio() argument 'incoming' must be _ssl.MemoryBIO, not {a[0].GetTypeName()}");
                if (a[1] is not PySSLMemoryBIO outgoing)
                    throw PyTypeError.Create($"_wrap_bio() argument 'outgoing' must be _ssl.MemoryBIO, not {a[1].GetTypeName()}");
                return Of(self)._ssl__SSLContext__wrap_bio_impl(incoming, outgoing, a[2].IsTrue(), a[3] ?? PyNone.Instance, a[4] ?? PyNone.Instance, a[5] ?? PyNone.Instance);
            }, minArgs: 0, maxArgs: int.MaxValue, acceptsKwargs: true);
            PyArgs.Define(type, "set_ciphers", PyCall.One, (self, args) =>
                Of(self)._ssl__SSLContext_set_ciphers_impl(PyArgs.ClinicStr("set_ciphers", "argument", "str", args[0])));
            PyArgs.Define(type, "_set_alpn_protocols", PyCall.One, (self, args) =>
                Of(self)._ssl__SSLContext__set_alpn_protocols_impl(BytesMethods.RequireBuffer(args[0])));
            type.TypeDict["load_cert_chain"] = new PyMethodDescriptor("load_cert_chain", type, (self, args, kwargs) =>
            {
                var a = PyArgs.Unpack("load_cert_chain", args, kwargs, new[] { "certfile", "keyfile", "password" }, 0, 1, 3);
                return Of(self)._ssl__SSLContext_load_cert_chain_impl(a[0], a[1] ?? PyNone.Instance, a[2] ?? PyNone.Instance);
            }, minArgs: 0, maxArgs: int.MaxValue, acceptsKwargs: true);
            PyArgs.Define(type, "load_dh_params", PyCall.One, (self, args) => Of(self)._ssl__SSLContext_load_dh_params(args[0]));
            type.TypeDict["load_verify_locations"] = new PyMethodDescriptor("load_verify_locations", type, (self, args, kwargs) =>
            {
                var a = PyArgs.Unpack("load_verify_locations", args, kwargs, new[] { "cafile", "capath", "cadata" }, 0, 0, 3);
                return Of(self)._ssl__SSLContext_load_verify_locations_impl(a[0] ?? PyNone.Instance, a[1] ?? PyNone.Instance, a[2] ?? PyNone.Instance);
            }, minArgs: 0, maxArgs: int.MaxValue, acceptsKwargs: true);
            PyArgs.Define(type, "session_stats", PyCall.NoArgs, (self, args) => Of(self)._ssl__SSLContext_session_stats_impl());
            PyArgs.Define(type, "set_default_verify_paths", PyCall.NoArgs, (self, args) => Of(self)._ssl__SSLContext_set_default_verify_paths_impl());
            PyArgs.Define(type, "set_ecdh_curve", PyCall.One, (self, args) => Of(self)._ssl__SSLContext_set_ecdh_curve(args[0]));
            PyArgs.Define(type, "cert_store_stats", PyCall.NoArgs, (self, args) => Of(self)._ssl__SSLContext_cert_store_stats_impl());
            type.TypeDict["get_ca_certs"] = new PyMethodDescriptor("get_ca_certs", type, (self, args, kwargs) =>
            {
                var a = PyArgs.Unpack("get_ca_certs", args, kwargs, new[] { "binary_form" }, 0, 0, 1);
                return Of(self)._ssl__SSLContext_get_ca_certs_impl(a[0] != null && a[0].IsTrue());
            }, minArgs: 0, maxArgs: int.MaxValue, acceptsKwargs: true);
            PyArgs.Define(type, "get_ciphers", PyCall.NoArgs, (self, args) => Of(self)._ssl__SSLContext_get_ciphers_impl());

            // context_getsetlist
            void GetSet(string name, Func<PySSLContext, PyObject> get, Action<PySSLContext, PyObject> set, string doc = null) =>
                type.TypeDict[name] = new PyGetSetDescriptor(name, type, self => get(Of(self)),
                    set == null ? null : (self, value) => set(Of(self), value), doc,
                    set == null ? null : self => throw PyAttributeError.Create("cannot delete attribute"));
            GetSet("check_hostname", s => PyBool.FromBool(s.CheckHostname), (s, v) => s.set_check_hostname(v));
            GetSet("_host_flags", s => PyInt.From(s.HostFlags), (s, v) => s.set_host_flags(v));
            GetSet("minimum_version", s => s.get_minimum_version(), (s, v) => s.set_min_max_proto_version(v, 0));
            GetSet("maximum_version", s => s.get_maximum_version(), (s, v) => s.set_min_max_proto_version(v, 1));
            GetSet("keylog_filename", s => s.keylog_filename ?? PyNone.Instance, (s, v) => s._PySSLContext_set_keylog_filename(v));
            GetSet("_msg_callback", s => s.msg_cb ?? PyNone.Instance, (s, v) => s._PySSLContext_set_msg_callback(v));
            GetSet("sni_callback", s => s.set_sni_cb ?? PyNone.Instance, (s, v) => s.set_sni_callback(v),
                "Set a callback that will be called when a server name is provided by the SSL/TLS client in the SNI extension.\n\nIf the argument is None then the callback is disabled. The method is called\nwith the SSLSocket, the server name as a string, and the SSLContext object.\nSee RFC 6066 for details of the SNI extension.");
            GetSet("num_tickets", s => PyInt.From(s.NumTickets), (s, v) => s.set_num_tickets(v), "Control the number of TLSv1.3 session tickets");
            GetSet("options", s => PyInt.From(new System.Numerics.BigInteger(s.Options)), (s, v) => s.set_options(v));
            GetSet("post_handshake_auth", s => PyBool.FromBool(s.PostHandshakeAuth), (s, v) => s.PostHandshakeAuth = v.IsTrue());
            GetSet("protocol", s => PyInt.From(s.Protocol), null);
            GetSet("verify_flags", s => PyInt.From(new System.Numerics.BigInteger(s.VerifyFlags)), (s, v) => s.set_verify_flags(v));
            GetSet("verify_mode", s => PyInt.From(s.VerifyMode), (s, v) => s.set_verify_mode(v));
            GetSet("security_level", s => PyInt.From(s.SecurityLevel), null, "The current security level");
            return type;
        }

        // ---- construction ----

        /// <summary>_set_verify_mode</summary>
        private void _set_verify_mode(int n)
        {
            if (n < SslModule.PY_SSL_CERT_NONE || n > SslModule.PY_SSL_CERT_REQUIRED)
                throw PyValueError.Create("invalid value for verify_mode");
            VerifyMode = n;
        }

        /// <summary>_ssl__SSLContext_impl: SSL_CTX_new for the method, and the defaults CPython sets.</summary>
        private static PyObject _ssl__SSLContext_impl(PyType type, int proto_version)
        {
            int method;
            switch (proto_version)
            {
                case SslModule.PY_SSL_VERSION_TLS1:
                    SslModule._ssl_deprecated("ssl.PROTOCOL_TLSv1 is deprecated", 2);
                    method = SslModule.PY_PROTO_TLSv1;
                    break;
                case SslModule.PY_SSL_VERSION_TLS1_1:
                    SslModule._ssl_deprecated("ssl.PROTOCOL_TLSv1_1 is deprecated", 2);
                    method = SslModule.PY_PROTO_TLSv1_1;
                    break;
                case SslModule.PY_SSL_VERSION_TLS1_2:
                    SslModule._ssl_deprecated("ssl.PROTOCOL_TLSv1_2 is deprecated", 2);
                    method = SslModule.PY_PROTO_TLSv1_2;
                    break;
                case SslModule.PY_SSL_VERSION_TLS:
                    SslModule._ssl_deprecated("ssl.PROTOCOL_TLS is deprecated", 2);
                    method = 0;
                    break;
                case SslModule.PY_SSL_VERSION_TLS_CLIENT:
                case SslModule.PY_SSL_VERSION_TLS_SERVER:
                    method = 0;
                    break;
                default:
                    throw PyValueError.Create($"invalid or unsupported protocol version {proto_version}");
            }
            var self = new PySSLContext(type)
            {
                HostFlags = SslModule.X509_CHECK_FLAG_NO_PARTIAL_WILDCARDS,
                Protocol = proto_version,
                MethodVersion = method,
            };
            /* Don't check host name by default */
            if (proto_version == SslModule.PY_SSL_VERSION_TLS_CLIENT)
            {
                self.CheckHostname = true;
                self._set_verify_mode(SslModule.PY_SSL_CERT_REQUIRED);
            }
            else
            {
                self.CheckHostname = false;
                self._set_verify_mode(SslModule.PY_SSL_CERT_NONE);
            }
            /* Defaults */
            ulong options = SslModule.SSL_OP_ALL & ~SslModule.SSL_OP_DONT_INSERT_EMPTY_FRAGMENTS;
            options |= SslModule.SSL_OP_NO_SSLv2;
            options |= SslModule.SSL_OP_NO_SSLv3;
            options |= SslModule.SSL_OP_NO_COMPRESSION;
            options |= SslModule.SSL_OP_CIPHER_SERVER_PREFERENCE;
            options |= SslModule.SSL_OP_SINGLE_DH_USE;
            options |= SslModule.SSL_OP_SINGLE_ECDH_USE;
            // SSL_CTX_new: OpenSSL 3 turns on SSL_OP_ENABLE_MIDDLEBOX_COMPAT itself.
            options |= SslModule.SSL_OP_ENABLE_MIDDLEBOX_COMPAT;
            self.Options = options;

            /* A bare minimum cipher list without completely broken cipher suites. */
            int seclevel = self.SecurityLevel;
            var list = SslCiphers.ssl_create_cipher_list("@SECLEVEL=2:ECDH+AESGCM:ECDH+CHACHA20:ECDH+AES:DHE+AES:!aNULL:!eNULL:!aDSS:!SHA1:!AESCCM",
                SslCiphers.Tls13Default, ref seclevel);
            if (list == null)
                throw SslModule.SetString(SslModule.PySSLErrorObject, "No cipher can be selected.");
            self.Ciphers = list;
            self.SecurityLevel = seclevel;
            switch (proto_version)
            {
                case SslModule.PY_SSL_VERSION_TLS:
                case SslModule.PY_SSL_VERSION_TLS_CLIENT:
                case SslModule.PY_SSL_VERSION_TLS_SERVER:
                    self.MinProto = SslModule.PY_SSL_MIN_PROTOCOL;
                    break;
            }
            /* Improve trust chain building when cross-signed intermediate
               certificates are present. See https://bugs.python.org/issue23476. */
            self.VerifyFlags = SslModule.X509_V_FLAG_TRUSTED_FIRST;
            self.PostHandshakeAuth = false;
            return self;
        }

        // ---- ciphers and ALPN ----

        /// <summary>_ssl._SSLContext.set_ciphers(cipherlist, /)</summary>
        private PyObject _ssl__SSLContext_set_ciphers_impl(string cipherlist)
        {
            int seclevel = SecurityLevel;
            var list = SslCiphers.ssl_create_cipher_list(cipherlist, SslCiphers.Tls13Default, ref seclevel);
            if (list == null)
            {
                ErrQueue.Clear();
                throw SslModule.SetString(SslModule.PySSLErrorObject, "No cipher can be selected.");
            }
            Ciphers = list;
            SecurityLevel = seclevel;
            return PyNone.Instance;
        }

        /// <summary>cipher_to_tuple: (name, protocol, strength_bits).</summary>
        internal static PyObject cipher_to_tuple(SslCipher cipher) => PyTuple.From(new PyObject[]
        {
            PyString.From(cipher.Name), PyString.From(cipher.Protocol), PyInt.From(cipher.StrengthBits),
        });

        /// <summary>cipher_to_dict</summary>
        private static PyObject cipher_to_dict(SslCipher cipher)
        {
            var d = new PyDict();
            void Set(string key, PyObject value) => d.SetItem(PyString.From(key), value);
            PyObject Str(string s) => s == null ? PyNone.Instance : PyString.From(s);
            Set("id", PyInt.From(cipher.Id));
            Set("name", Str(cipher.Name));
            Set("protocol", Str(cipher.Protocol));
            Set("description", Str(cipher.Description));
            Set("strength_bits", PyInt.From(cipher.StrengthBits));
            Set("alg_bits", PyInt.From(cipher.AlgBits));
            Set("aead", PyBool.FromBool(cipher.Aead));
            Set("symmetric", Str(cipher.Symmetric));
            Set("digest", Str(cipher.Digest));
            Set("kea", Str(cipher.Kea));
            Set("auth", Str(cipher.Auth));
            return d;
        }

        /// <summary>_ssl._SSLContext.get_ciphers()</summary>
        private PyObject _ssl__SSLContext_get_ciphers_impl() =>
            new PyList(Ciphers.Select(cipher_to_dict).ToList());

        /// <summary>_ssl._SSLContext._set_alpn_protocols(protos, /)</summary>
        private PyObject _ssl__SSLContext__set_alpn_protocols_impl(byte[] protos)
        {
            AlpnProtocols = (byte[])protos.Clone();
            return PyNone.Instance;
        }

        // ---- getters and setters ----

        /// <summary>set_verify_mode</summary>
        private void set_verify_mode(PyObject arg)
        {
            int n = PyArgs.ParseInt(arg);
            if (n == SslModule.PY_SSL_CERT_NONE && CheckHostname)
                throw PyValueError.Create("Cannot set verify_mode to CERT_NONE when check_hostname is enabled.");
            _set_verify_mode(n);
        }

        /// <summary>set_verify_flags: "k" (unsigned long, wrapping).</summary>
        private void set_verify_flags(PyObject arg)
        {
            var value = PyObject.IndexOrThrow(arg).Value;
            ulong new_flags = (ulong)(value & uint.MaxValue);
            VerifyFlags = new_flags;
        }

        /// <summary>set_min_max_proto_version</summary>
        private void set_min_max_proto_version(PyObject arg, int what)
        {
            long v = PyArgs.CLong(arg);
            if (v > int.MaxValue)
                throw PyOverflowError.Create("Option is too long");
            switch (Protocol)
            {
                case SslModule.PY_SSL_VERSION_TLS_CLIENT:
                case SslModule.PY_SSL_VERSION_TLS_SERVER:
                case SslModule.PY_SSL_VERSION_TLS:
                    break;
                default:
                    throw PyValueError.Create("The context's protocol doesn't support modification of highest and lowest version.");
            }
            /* check for deprecations and supported values */
            switch (v)
            {
                case SslModule.PY_PROTO_SSLv3:
                    SslModule._ssl_deprecated("ssl.TLSVersion.SSLv3 is deprecated", 2);
                    break;
                case SslModule.PY_PROTO_TLSv1:
                    SslModule._ssl_deprecated("ssl.TLSVersion.TLSv1 is deprecated", 2);
                    break;
                case SslModule.PY_PROTO_TLSv1_1:
                    SslModule._ssl_deprecated("ssl.TLSVersion.TLSv1_1 is deprecated", 2);
                    break;
                case SslModule.PY_PROTO_MINIMUM_SUPPORTED:
                case SslModule.PY_PROTO_MAXIMUM_SUPPORTED:
                case SslModule.PY_PROTO_TLSv1_2:
                case SslModule.PY_PROTO_TLSv1_3:
                    /* ok */
                    break;
                default:
                    throw PyValueError.Create($"Unsupported TLS/SSL version 0x{v:x}");
            }
            if (what == 0)
            {
                switch (v)
                {
                    case SslModule.PY_PROTO_MINIMUM_SUPPORTED: v = 0; break;
                    /* Emulate max for set_min_proto_version */
                    case SslModule.PY_PROTO_MAXIMUM_SUPPORTED: v = SslModule.PY_PROTO_MAXIMUM_AVAILABLE; break;
                }
                // SSL_CTX_set_min_proto_version
                MinProto = (int)v;
            }
            else
            {
                switch (v)
                {
                    case SslModule.PY_PROTO_MAXIMUM_SUPPORTED: v = 0; break;
                    /* Emulate max for set_min_proto_version */
                    case SslModule.PY_PROTO_MINIMUM_SUPPORTED: v = SslModule.PY_PROTO_MINIMUM_AVAILABLE; break;
                }
                MaxProto = (int)v;
            }
        }

        /// <summary>get_minimum_version</summary>
        private PyObject get_minimum_version() => PyInt.From(MinProto == 0 ? SslModule.PY_PROTO_MINIMUM_SUPPORTED : MinProto);

        /// <summary>get_maximum_version</summary>
        private PyObject get_maximum_version() => PyInt.From(MaxProto == 0 ? SslModule.PY_PROTO_MAXIMUM_SUPPORTED : MaxProto);

        /// <summary>The versions a connection may use: the bounds, the method's version, the OP_NO_* options.</summary>
        internal (int Min, int Max) EffectiveVersions()
        {
            int min = MinProto == 0 ? SslModule.PY_PROTO_TLSv1 : MinProto;
            int max = MaxProto == 0 ? SslModule.PY_PROTO_TLSv1_3 : MaxProto;
            if (MethodVersion != 0)
            {
                min = Math.Max(min, MethodVersion);
                max = Math.Min(max, MethodVersion);
            }
            return (min, max);
        }

        /// <summary>set_num_tickets</summary>
        private void set_num_tickets(PyObject arg)
        {
            long num = PyArgs.CLong(arg);
            if (num < 0)
                throw PyValueError.Create("value must be non-negative");
            if (Protocol != SslModule.PY_SSL_VERSION_TLS_SERVER)
                throw PyValueError.Create("SSLContext is not a server context.");
            NumTickets = (int)Math.Min(num, int.MaxValue);
        }

        /// <summary>set_options: "O!" int, as unsigned long long; OP_NO_* turned on warns.</summary>
        private void set_options(PyObject arg)
        {
            if (arg is not PyInt)
                throw PyTypeError.Create($"argument must be int, not {arg.GetTypeName()}");
            var value = ((PyInt)arg).Value;
            if (value.Sign < 0)
                throw PyOverflowError.Create("can't convert negative int to unsigned");
            if (value > ulong.MaxValue)
                throw PyOverflowError.Create("int too big to convert");
            ulong new_opts = (ulong)value;
            ulong opt_no = SslModule.SSL_OP_NO_SSLv2 | SslModule.SSL_OP_NO_SSLv3 | SslModule.SSL_OP_NO_TLSv1 |
                SslModule.SSL_OP_NO_TLSv1_1 | SslModule.SSL_OP_NO_TLSv1_2 | SslModule.SSL_OP_NO_TLSv1_3;
            ulong opts = Options;
            ulong clear = opts & ~new_opts;
            ulong set = ~opts & new_opts;
            if ((set & opt_no) != 0)
                SslModule._ssl_deprecated("ssl.OP_NO_SSL*/ssl.OP_NO_TLS* options are deprecated", 2);
            if (clear != 0)
                Options &= ~clear;
            if (set != 0)
                Options |= set;
        }

        /// <summary>set_host_flags: "I" (unsigned int, wrapping).</summary>
        private void set_host_flags(PyObject arg)
        {
            var value = PyObject.IndexOrThrow(arg).Value;
            HostFlags = (uint)(value & uint.MaxValue);
        }

        /// <summary>set_check_hostname: True also makes verify_mode CERT_REQUIRED when it was CERT_NONE.</summary>
        private void set_check_hostname(PyObject arg)
        {
            bool check_hostname = arg.IsTrue();
            if (check_hostname && VerifyMode == SslModule.PY_SSL_CERT_NONE)
                _set_verify_mode(SslModule.PY_SSL_CERT_REQUIRED);
            CheckHostname = check_hostname;
        }

        /// <summary>set_sni_callback</summary>
        private void set_sni_callback(PyObject arg)
        {
            if (Protocol == SslModule.PY_SSL_VERSION_TLS_CLIENT)
                throw PyValueError.Create("sni_callback cannot be set on TLS_CLIENT context");
            set_sni_cb = null;
            if (arg is PyNone)
                return;
            if (!arg.IsCallable())
                throw PyTypeError.Create("not a callable object");
            set_sni_cb = arg;
        }

        /// <summary>
        /// _PySSLContext_set_msg_callback: stored; SslStream reports no TLS messages, so it is never
        /// called (docs/todo.md 5-29).
        /// </summary>
        private void _PySSLContext_set_msg_callback(PyObject arg)
        {
            msg_cb = null;
            if (arg is PyNone)
                return;
            if (!arg.IsCallable())
                throw PyTypeError.Create("not a callable object");
            msg_cb = arg;
        }

        /// <summary>
        /// _PySSLContext_set_keylog_filename: the file is opened for appending and gets OpenSSL's
        /// header line; SslStream does not hand out the TLS secrets, so no key lines follow
        /// (docs/todo.md 5-29).
        /// </summary>
        private void _PySSLContext_set_keylog_filename(PyObject arg)
        {
            keylog_filename = null;
            if (arg is PyNone)
                return;
            // _Py_fopen_obj: a str path on Windows
            if (OperatingSystem.IsWindows() && arg is not PyString)
                throw PyTypeError.Create($"str file path expected under Windows, got {arg.GetPyType().ToRepr().Value}");
            string path = SslModule.FSConverter(arg);
            try
            {
                using var stream = new System.IO.FileStream(path, System.IO.FileMode.Append, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite);
                if (stream.Position == 0)
                {
                    var header = Encoding.ASCII.GetBytes("# TLS secrets log file, generated by OpenSSL / Python" + Environment.NewLine);
                    stream.Write(header, 0, header.Length);
                }
            }
            catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
            {
                throw PyFileIO.OSErrorOf(error, arg);
            }
            keylog_filename = arg;
        }

        // ---- certificates ----

        /// <summary>_pwinfo_set: a str (UTF-8), bytes or bytearray password, at most INT_MAX bytes.</summary>
        private static byte[] _pwinfo_set(PyObject password, string bad_type_error)
        {
            if (password is PyString s)
                return s.Encode("utf-8").Value;
            if (password is PyBytes b)
                return b.Value;
            if (password is PyByteArray ba)
                return BytesMethods.Buffer(ba);
            throw PyTypeError.Create(bad_type_error);
        }

        /// <summary>_ssl._SSLContext.load_cert_chain(certfile, keyfile=None, password=None)</summary>
        private PyObject _ssl__SSLContext_load_cert_chain_impl(PyObject certfile, PyObject keyfile, PyObject password)
        {
            ErrQueue.Clear();
            if (keyfile is PyNone)
                keyfile = null;
            string certfile_bytes, keyfile_bytes = null;
            try
            {
                certfile_bytes = SslModule.FSConverter(certfile);
            }
            catch (PythonException e) when (e.PyException.GetPyType().IsSubclassOf(PyType.TypeErrorType))
            {
                throw PyTypeError.Create("certfile should be a valid filesystem path");
            }
            if (keyfile != null)
            {
                try
                {
                    keyfile_bytes = SslModule.FSConverter(keyfile);
                }
                catch (PythonException e) when (e.PyException.GetPyType().IsSubclassOf(PyType.TypeErrorType))
                {
                    throw PyTypeError.Create("keyfile should be a valid filesystem path");
                }
            }
            // _PySSLPasswordInfo and _password_callback
            byte[] pw = null;
            PyObject callable = null;
            if (password is not PyNone)
            {
                if (password.IsCallable())
                    callable = password;
                else
                    pw = _pwinfo_set(password, "password should be a string or callable");
            }
            Func<int, byte[]> callback = null;
            if (password is not PyNone)
            {
                callback = size =>
                {
                    if (callable != null)
                    {
                        var fn_ret = callable.Call(Array.Empty<PyObject>(), null);
                        pw = _pwinfo_set(fn_ret, "password callback must return a string");
                    }
                    if (pw.Length > size)
                        throw PyValueError.Create($"password cannot be longer than {size} bytes");
                    return pw;
                };
            }

            // SSL_CTX_use_certificate_chain_file
            var data = SslModule.ReadFile(certfile_bytes, out int errno);
            if (data == null)
            {
                ErrQueue.Clear();
                throw SslModule.FromErrno(errno);
            }
            var certs = new List<X509Cert>();
            try
            {
                int pos = 0;
                X509Cert x;
                while ((x = Pem.ReadCertificate(data, ref pos)) != null)
                    certs.Add(x);
            }
            catch (Asn1Exception)
            {
                if (certs.Count == 0)
                    certs.Clear();
            }
            if (certs.Count == 0)
            {
                ErrQueue.Raise(SslErr.PemLib(SslErr.LIB_SSL));
                throw SslModule._setSSLError(null, 0, 3858);
            }

            // SSL_CTX_use_PrivateKey_file
            var keyData = keyfile_bytes == null ? data : SslModule.ReadFile(keyfile_bytes, out errno);
            if (keyData == null)
            {
                ErrQueue.Clear();
                throw SslModule.FromErrno(errno);
            }
            var key = SslKeys.ReadPrivateKey(keyData, callback);
            if (key == null)
                throw SslModule._setSSLError(null, 0, 3879);
            if (!SslKeys.Matches(certs[0], key))
            {
                ErrQueue.Raise(SslErr.Of(SslErr.LIB_X509, "KEY_VALUES_MISMATCH"));
                throw SslModule._setSSLError(null, 0, 3879);
            }
            try
            {
                Certificate = SslKeys.WithKey(certs[0], key);
            }
            catch (CryptographicException)
            {
                ErrQueue.Raise(SslErr.PemLib(SslErr.LIB_SSL));
                throw SslModule._setSSLError(null, 0, 3887);
            }
            CertificateX509 = certs[0];
            ChainCertificates.Clear();
            foreach (var extra in certs.Skip(1))
                ChainCertificates.Add(new X509Certificate2(extra.Der));
            return PyNone.Instance;
        }

        /// <summary>_add_ca_certs: PEM (SSL_FILETYPE_PEM) or DER (SSL_FILETYPE_ASN1) certificates into the store.</summary>
        private void _add_ca_certs(byte[] data, bool pem)
        {
            if (data.Length <= 0)
                throw PyValueError.Create("Empty certificate data");
            int loaded = 0;
            bool was_bio_eof = false;
            SslErr err = null;
            int pos = 0;
            while (true)
            {
                X509Cert cert;
                try
                {
                    if (!pem)
                    {
                        if (pos >= data.Length)
                        {
                            was_bio_eof = true;
                            break;
                        }
                        var tlv = Asn1.Read(data, pos, data.Length);
                        cert = X509Cert.Parse(tlv);
                        pos = tlv.End;
                    }
                    else
                    {
                        cert = Pem.ReadCertificate(data, ref pos);
                        if (cert == null)
                        {
                            err = SslErr.Of(SslErr.LIB_PEM, "NO_START_LINE");
                            break;
                        }
                    }
                }
                catch (Asn1Exception e)
                {
                    err = e.Reason is "BAD_BASE64_DECODE" or "BAD_END_LINE" ? SslErr.Of(SslErr.LIB_PEM, e.Reason) : SslErr.Of(SslErr.LIB_ASN1, e.Reason);
                    break;
                }
                Store.Add(cert);
                loaded++;
            }
            if (loaded == 0)
            {
                string msg = pem ? "no start line: cadata does not contain a certificate"
                    : "not enough data: cadata does not contain a certificate";
                throw SslModule._setSSLError(msg, 0, 3977);
            }
            if (!pem && was_bio_eof)
                return;
            if (pem && err != null && err.Lib == SslErr.LIB_PEM && err.ReasonName == "NO_START_LINE")
                return;
            if (err != null)
            {
                ErrQueue.Raise(err);
                throw SslModule._setSSLError(null, 0, 3990);
            }
        }

        /// <summary>X509_load_cert_crl_file: every certificate and CRL of a PEM file.</summary>
        private bool LoadCertCrlFile(byte[] data)
        {
            int pos = 0, count = 0;
            try
            {
                while (Pem.Next(data, ref pos) is { } block)
                {
                    if (Pem.IsCertificateLabel(block.Label))
                    {
                        Store.Add(X509Cert.Parse(block.Label == "TRUSTED CERTIFICATE" ? Asn1.Parse(block.Der).Encoded() : block.Der));
                        count++;
                    }
                    else if (block.Label == "X509 CRL")
                    {
                        Store.AddCrl(X509Crl.Parse(block.Der));
                        count++;
                    }
                }
            }
            catch (Asn1Exception)
            {
                ErrQueue.Raise(SslErr.PemLib(SslErr.LIB_X509));
                return false;
            }
            if (count == 0)
            {
                ErrQueue.Raise(SslErr.Of(SslErr.LIB_X509, "NO_CERTIFICATE_OR_CRL_FOUND"));
                return false;
            }
            return true;
        }

        /// <summary>_ssl._SSLContext.load_verify_locations(cafile=None, capath=None, cadata=None)</summary>
        private PyObject _ssl__SSLContext_load_verify_locations_impl(PyObject cafile, PyObject capath, PyObject cadata)
        {
            if (cafile is PyNone) cafile = null;
            if (capath is PyNone) capath = null;
            if (cadata is PyNone) cadata = null;
            if (cafile == null && capath == null && cadata == null)
                throw PyTypeError.Create("cafile, capath and cadata cannot be all omitted");
            string cafile_bytes = null, capath_bytes = null;
            if (cafile != null)
            {
                try { cafile_bytes = SslModule.FSConverter(cafile); }
                catch (PythonException e) when (e.PyException.GetPyType().IsSubclassOf(PyType.TypeErrorType))
                {
                    throw PyTypeError.Create("cafile should be a valid filesystem path");
                }
            }
            if (capath != null)
            {
                try { capath_bytes = SslModule.FSConverter(capath); }
                catch (PythonException e) when (e.PyException.GetPyType().IsSubclassOf(PyType.TypeErrorType))
                {
                    throw PyTypeError.Create("capath should be a valid filesystem path");
                }
            }

            /* validate cadata type and load cadata */
            if (cadata != null)
            {
                if (cadata is PyString text)
                {
                    if (text.Value.Any(c => c > 127))
                        throw PyTypeError.Create("cadata should be an ASCII string or a bytes-like object");
                    _add_ca_certs(Encoding.ASCII.GetBytes(text.Value), true);
                }
                else if (cadata.SupportsBuffer() || cadata is PyBytes)
                {
                    if (cadata is PyMemoryView view && (!view.IsCContiguous || PyArgs.Int(view.GetAttribute("ndim")) > 1))
                        throw PyTypeError.Create("cadata should be a contiguous buffer with a single dimension");
                    _add_ca_certs(BytesMethods.Buffer(cadata), false);
                }
                else
                    throw PyTypeError.Create("cadata should be an ASCII string or a bytes-like object");
            }

            /* load cafile or capath */
            if (cafile_bytes != null || capath_bytes != null)
            {
                // SSL_CTX_load_verify_locations
                if (cafile_bytes != null)
                {
                    var data = SslModule.ReadFile(cafile_bytes, out int errno);
                    if (data == null)
                    {
                        ErrQueue.Clear();
                        throw SslModule.FromErrno(errno);
                    }
                    if (!LoadCertCrlFile(data))
                        throw SslModule._setSSLError(null, 0, 4109);
                }
                if (capath_bytes != null)
                    Store.Dirs.Add(capath_bytes);
            }
            return PyNone.Instance;
        }

        /// <summary>
        /// _ssl._SSLContext.load_dh_params(path, /): the DH parameters are read and checked; SslStream
        /// picks its own groups, so they are not used (docs/todo.md 5-29).
        /// </summary>
        private PyObject _ssl__SSLContext_load_dh_params(PyObject filepath)
        {
            string path = SslModule.FSConverter(filepath);
            var data = SslModule.ReadFile(path, out int errno);
            if (data == null)
            {
                var error = SslModule.FromErrno(errno);
                throw new PythonException(PyOSError.FromArguments(new PyObject[] { PyInt.From(errno), PyString.From(errno == 2 ? "No such file or directory" : "Permission denied"), filepath }, null));
            }
            int pos = 0;
            Pem.Block block = null;
            try
            {
                while ((block = Pem.Next(data, ref pos)) != null && block.Label != "DH PARAMETERS" && block.Label != "X9.42 DH PARAMETERS")
                {
                }
            }
            catch (Asn1Exception)
            {
                block = null;
            }
            if (block == null)
            {
                ErrQueue.Raise(SslErr.Of(SslErr.LIB_PEM, "NO_START_LINE"));
                throw SslModule._setSSLError(null, 0, 4163);
            }
            return PyNone.Instance;
        }

        /// <summary>_ssl._SSLContext._wrap_socket</summary>
        private PyObject _ssl__SSLContext__wrap_socket_impl(PyObject sock, bool server_side, PyObject hostname_obj, PyObject owner, PyObject session)
        {
            string hostname = hostname_obj is PyNone ? null : ParseHostname(hostname_obj);
            return PySSLSocket.newPySSLSocket(this, (PySocketObject)sock, server_side ? SslModule.PY_SSL_SERVER : SslModule.PY_SSL_CLIENT,
                hostname, owner, session, null, null);
        }

        /// <summary>_ssl._SSLContext._wrap_bio</summary>
        private PyObject _ssl__SSLContext__wrap_bio_impl(PySSLMemoryBIO incoming, PySSLMemoryBIO outgoing, bool server_side,
            PyObject hostname_obj, PyObject owner, PyObject session)
        {
            string hostname = hostname_obj is PyNone ? null : ParseHostname(hostname_obj);
            return PySSLSocket.newPySSLSocket(this, null, server_side ? SslModule.PY_SSL_SERVER : SslModule.PY_SSL_CLIENT,
                hostname, owner, session, incoming, outgoing);
        }

        // PyArg_Parse(hostname_obj, "es", "ascii", &hostname)
        private static string ParseHostname(PyObject obj)
        {
            if (obj is not PyString text)
                throw PyTypeError.Create($"argument must be str, not {obj.GetTypeName()}");
            var encoded = text.Encode("ascii").Value;
            if (Array.IndexOf(encoded, (byte)0) >= 0)
                throw PyTypeError.Create("argument must be encoded string without null bytes, not str");
            return Encoding.ASCII.GetString(encoded);
        }

        /// <summary>_ssl._SSLContext.session_stats()</summary>
        private PyObject _ssl__SSLContext_session_stats_impl()
        {
            var stats = new PyDict();
            void Add(string key, long value) => stats.SetItem(PyString.From(key), PyInt.From(value));
            Add("number", Stats.number);
            Add("connect", Stats.connect);
            Add("connect_good", Stats.connect_good);
            Add("connect_renegotiate", Stats.connect_renegotiate);
            Add("accept", Stats.accept);
            Add("accept_good", Stats.accept_good);
            Add("accept_renegotiate", Stats.accept_renegotiate);
            Add("accept", Stats.accept);
            Add("hits", Stats.hits);
            Add("misses", Stats.misses);
            Add("timeouts", Stats.timeouts);
            Add("cache_full", Stats.cache_full);
            return stats;
        }

        /// <summary>
        /// _ssl._SSLContext.set_default_verify_paths(): X509_STORE_set_default_paths, the file named
        /// by SSL_CERT_FILE (else the default) and the directory named by SSL_CERT_DIR (else the default).
        /// </summary>
        private PyObject _ssl__SSLContext_set_default_verify_paths_impl()
        {
            string file = Environment.GetEnvironmentVariable(SslStore.CertFileEnv) ?? SslStore.DefaultCertFile;
            var data = SslModule.ReadFile(file, out _);
            if (data != null)
            {
                LoadCertCrlFile(data);
                ErrQueue.Clear();
            }
            Store.Dirs.Add(Environment.GetEnvironmentVariable(SslStore.CertDirEnv) ?? SslStore.DefaultCertDir);
            return PyNone.Instance;
        }

        /// <summary>
        /// _ssl._SSLContext.set_ecdh_curve(name, /): OBJ_sn2nid of the name; the group is not passed to
        /// SslStream, which negotiates its own (docs/todo.md 5-29).
        /// </summary>
        private PyObject _ssl__SSLContext_set_ecdh_curve(PyObject name)
        {
            var path = NtModule.FsPathObject(name);
            string text = path is PyBytes b ? Encoding.UTF8.GetString(b.Value) : ((PyString)path).Value;
            int nid = SslObjects.Sn2Nid(text);
            if (nid == 0)
                throw PyValueError.Create($"unknown elliptic curve name {name.ToRepr().Value}");
            return PyNone.Instance;
        }

        /// <summary>_ssl._SSLContext.cert_store_stats()</summary>
        private PyObject _ssl__SSLContext_cert_store_stats_impl()
        {
            int x509 = 0, ca = 0, crl;
            foreach (var cert in Store.SortedCerts())
            {
                x509++;
                if (cert.CheckCA() != 0)
                    ca++;
            }
            lock (Store)
                crl = Store.Crls.Count;
            var d = new PyDict();
            d.SetItem(PyString.From("x509"), PyInt.From(x509));
            d.SetItem(PyString.From("crl"), PyInt.From(crl));
            d.SetItem(PyString.From("x509_ca"), PyInt.From(ca));
            return d;
        }

        /// <summary>_ssl._SSLContext.get_ca_certs(binary_form=False)</summary>
        private PyObject _ssl__SSLContext_get_ca_certs_impl(bool binary_form)
        {
            var rlist = new List<PyObject>();
            foreach (var cert in Store.SortedCerts())
            {
                /* CA for any purpose */
                if (cert.CheckCA() == 0)
                    continue;
                rlist.Add(binary_form ? SslModule._certificate_to_der(cert) : SslModule._decode_certificate(cert));
            }
            return new PyList(rlist);
        }
    }
}
