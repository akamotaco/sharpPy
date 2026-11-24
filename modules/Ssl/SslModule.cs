#if GODOT
using IOHelper = Godot_IO.Helper;
#else
using IOHelper = DotNet_IO.Helper;
#endif
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace SharpPy.Modules.Ssl
{
    /// <summary>
    /// One OpenSSL error queue entry (ERR_peek_last_error): the library and reason codes with the
    /// names err_codes_to_names / lib_codes_to_names give them and ERR_reason_error_string's text.
    /// A reason with no name (ERR_R_PEM_LIB and the other common reasons) has a null ReasonName.
    /// </summary>
    internal sealed class SslErr
    {
        public int Lib;
        public string LibName;
        public int Reason;          // ERR_GET_REASON: the 23 low bits (ERR_RFLAG_COMMON included)
        public string ReasonName;
        public string Text;

        internal const int LIB_SYS = 2, LIB_EVP = 6, LIB_PEM = 9, LIB_X509 = 11, LIB_ASN1 = 13, LIB_SSL = 20, LIB_X509V3 = 34;
        internal const int ERR_R_PEM_LIB = 0x80000 | 9, ERR_R_ASN1_LIB = 0x80000 | 13, ERR_R_X509_LIB = 0x80000 | 11,
            ERR_R_SYS_LIB = 0x80000 | 2, ERR_R_INTERNAL_ERROR = 0x80000 | 0x40000 | 4;

        private static string LibraryName(int lib) => lib switch
        {
            LIB_SYS => "SYS", LIB_EVP => "EVP", LIB_PEM => "PEM", LIB_X509 => "X509", LIB_ASN1 => "ASN1",
            LIB_SSL => "SSL", LIB_X509V3 => "X509V3", _ => null,
        };

        /// <summary>ERR_raise(lib, reason) with a reason err_codes_to_names knows.</summary>
        internal static SslErr Of(int lib, string mnemonic)
        {
            var (code, text) = Reasons.TryGetValue((lib, mnemonic), out var r) ? r : (0, mnemonic.ToLowerInvariant().Replace('_', ' '));
            return new SslErr { Lib = lib, LibName = LibraryName(lib), Reason = code, ReasonName = mnemonic, Text = text };
        }

        /// <summary>ERR_raise(lib, ERR_R_*): a common reason, which has no name in the table.</summary>
        internal static SslErr Common(int lib, int reason, string text) =>
            new() { Lib = lib, LibName = LibraryName(lib), Reason = reason, ReasonName = null, Text = text };

        internal static SslErr PemLib(int lib) => Common(lib, ERR_R_PEM_LIB, "PEM lib");

        /// <summary>An alert the peer sent (SSL_AD_REASON_OFFSET + its description).</summary>
        internal static SslErr Alert(int description)
        {
            var (mnemonic, text) = AlertReason(description);
            return new SslErr { Lib = LIB_SSL, LibName = "SSL", Reason = 1000 + description, ReasonName = mnemonic, Text = text };
        }

        internal static (string, string) AlertReason(int d) => d switch
        {
            0 => ("SSLV3_ALERT_CLOSE_NOTIFY", "ssl/tls alert close notify"), // not in the table, never raised
            10 => ("SSLV3_ALERT_UNEXPECTED_MESSAGE", "ssl/tls alert unexpected message"),
            20 => ("SSLV3_ALERT_BAD_RECORD_MAC", "ssl/tls alert bad record mac"),
            21 => ("TLSV1_ALERT_DECRYPTION_FAILED", "tlsv1 alert decryption failed"),
            22 => ("TLSV1_ALERT_RECORD_OVERFLOW", "tlsv1 alert record overflow"),
            30 => ("SSLV3_ALERT_DECOMPRESSION_FAILURE", "ssl/tls alert decompression failure"),
            40 => ("SSLV3_ALERT_HANDSHAKE_FAILURE", "ssl/tls alert handshake failure"),
            41 => ("SSLV3_ALERT_NO_CERTIFICATE", "ssl/tls alert no certificate"),
            42 => ("SSLV3_ALERT_BAD_CERTIFICATE", "ssl/tls alert bad certificate"),
            43 => ("SSLV3_ALERT_UNSUPPORTED_CERTIFICATE", "ssl/tls alert unsupported certificate"),
            44 => ("SSLV3_ALERT_CERTIFICATE_REVOKED", "ssl/tls alert certificate revoked"),
            45 => ("SSLV3_ALERT_CERTIFICATE_EXPIRED", "ssl/tls alert certificate expired"),
            46 => ("SSLV3_ALERT_CERTIFICATE_UNKNOWN", "ssl/tls alert certificate unknown"),
            47 => ("SSLV3_ALERT_ILLEGAL_PARAMETER", "ssl/tls alert illegal parameter"),
            48 => ("TLSV1_ALERT_UNKNOWN_CA", "tlsv1 alert unknown ca"),
            49 => ("TLSV1_ALERT_ACCESS_DENIED", "tlsv1 alert access denied"),
            50 => ("TLSV1_ALERT_DECODE_ERROR", "tlsv1 alert decode error"),
            51 => ("TLSV1_ALERT_DECRYPT_ERROR", "tlsv1 alert decrypt error"),
            60 => ("TLSV1_ALERT_EXPORT_RESTRICTION", "tlsv1 alert export restriction"),
            70 => ("TLSV1_ALERT_PROTOCOL_VERSION", "tlsv1 alert protocol version"),
            71 => ("TLSV1_ALERT_INSUFFICIENT_SECURITY", "tlsv1 alert insufficient security"),
            80 => ("TLSV1_ALERT_INTERNAL_ERROR", "tlsv1 alert internal error"),
            86 => ("TLSV1_ALERT_INAPPROPRIATE_FALLBACK", "tlsv1 alert inappropriate fallback"),
            90 => ("TLSV1_ALERT_USER_CANCELLED", "tlsv1 alert user cancelled"),
            100 => ("TLSV1_ALERT_NO_RENEGOTIATION", "tlsv1 alert no renegotiation"),
            109 => ("TLSV13_ALERT_MISSING_EXTENSION", "tlsv13 alert missing extension"),
            110 => ("TLSV1_UNSUPPORTED_EXTENSION", "tlsv1 unsupported extension"),
            111 => ("TLSV1_CERTIFICATE_UNOBTAINABLE", "tlsv1 certificate unobtainable"),
            112 => ("TLSV1_UNRECOGNIZED_NAME", "tlsv1 unrecognized name"),
            113 => ("TLSV1_BAD_CERTIFICATE_STATUS_RESPONSE", "tlsv1 bad certificate status response"),
            114 => ("TLSV1_BAD_CERTIFICATE_HASH_VALUE", "tlsv1 bad certificate hash value"),
            115 => (null, "tlsv1 alert unknown psk identity"),
            116 => ("TLSV13_ALERT_CERTIFICATE_REQUIRED", "tlsv13 alert certificate required"),
            120 => (null, "tlsv1 alert no application protocol"),
            _ => (null, "unknown alert"),
        };

        // _ssl_data_31.h error_codes (the entries raised here) and crypto's reason strings.
        private static readonly Dictionary<(int, string), (int, string)> Reasons = new()
        {
            [(LIB_SSL, "CERTIFICATE_VERIFY_FAILED")] = (134, "certificate verify failed"),
            [(LIB_SSL, "UNEXPECTED_EOF_WHILE_READING")] = (294, "unexpected eof while reading"),
            [(LIB_SSL, "WRONG_VERSION_NUMBER")] = (267, "wrong version number"),
            [(LIB_SSL, "NO_CIPHER_MATCH")] = (185, "no cipher match"),
            [(LIB_SSL, "NO_PROTOCOLS_AVAILABLE")] = (191, "no protocols available"),
            [(LIB_SSL, "UNSUPPORTED_PROTOCOL")] = (258, "unsupported protocol"),
            [(LIB_SSL, "PEER_DID_NOT_RETURN_A_CERTIFICATE")] = (199, "peer did not return a certificate"),
            [(LIB_SSL, "NO_SHARED_CIPHER")] = (193, "no shared cipher"),
            [(LIB_SSL, "SHUTDOWN_WHILE_IN_INIT")] = (407, "shutdown while in init"),
            [(LIB_SSL, "WRONG_SSL_VERSION")] = (266, "wrong ssl version"),
            [(LIB_SSL, "APPLICATION_DATA_AFTER_CLOSE_NOTIFY")] = (291, "application data after close notify"),
            [(LIB_SSL, "HTTP_REQUEST")] = (156, "http request"),
            [(LIB_SSL, "UNKNOWN_PROTOCOL")] = (252, "unknown protocol"),
            [(LIB_SSL, "RECORD_LAYER_FAILURE")] = (313, "record layer failure"),
            [(LIB_SSL, "NO_CERTIFICATE_ASSIGNED")] = (177, "no certificate assigned"),
            [(LIB_SSL, "NO_SUITABLE_SIGNATURE_ALGORITHM")] = (118, "no suitable signature algorithm"),
            [(LIB_SSL, "CA_MD_TOO_WEAK")] = (398, "ca md too weak"),
            [(LIB_SSL, "EE_KEY_TOO_SMALL")] = (399, "ee key too small"),
            [(LIB_SSL, "X509_LIB")] = (268, "X509 lib"),
            [(LIB_SSL, "INTERNAL_ERROR")] = (0x80000 | 0x40000 | 4, "internal error"),
            [(LIB_SSL, "SSL_HANDSHAKE_FAILURE")] = (229, "ssl handshake failure"),
            [(LIB_SSL, "PEM_LIB")] = (9, "PEM lib"),
            [(LIB_X509, "KEY_VALUES_MISMATCH")] = (116, "key values mismatch"),
            [(LIB_X509, "NO_CERTIFICATE_OR_CRL_FOUND")] = (136, "no certificate or crl found"),
            [(LIB_X509, "CERT_ALREADY_IN_HASH_TABLE")] = (101, "cert already in hash table"),
            [(LIB_PEM, "NO_START_LINE")] = (108, "no start line"),
            [(LIB_PEM, "BAD_BASE64_DECODE")] = (100, "bad base64 decode"),
            [(LIB_PEM, "BAD_END_LINE")] = (102, "bad end line"),
            [(LIB_PEM, "BAD_DECRYPT")] = (101, "bad decrypt"),
            [(LIB_PEM, "BAD_PASSWORD_READ")] = (104, "bad password read"),
            [(LIB_ASN1, "NOT_ENOUGH_DATA")] = (142, "not enough data"),
            [(LIB_ASN1, "HEADER_TOO_LONG")] = (123, "header too long"),
            [(LIB_ASN1, "WRONG_TAG")] = (168, "wrong tag"),
            [(LIB_EVP, "BAD_DECRYPT")] = (100, "bad decrypt"),
        };
    }

    /// <summary>
    /// The per-thread OpenSSL error queue: ERR_raise, ERR_peek_last_error, ERR_clear_error.
    /// </summary>
    internal static class ErrQueue
    {
        [ThreadStatic] private static List<SslErr> _queue;

        private static List<SslErr> Queue => _queue ??= new List<SslErr>();

        internal static void Raise(SslErr e) => Queue.Add(e);
        internal static SslErr PeekLast() => Queue.Count > 0 ? Queue[^1] : null;
        internal static void Clear() => Queue.Clear();
    }

    /// <summary>
    /// Modules/_ssl.c (CPython 3.12.15, linked to OpenSSL 3) as the native module _ssl, over
    /// System.Net.Security.SslStream (the TLS record and handshake layer, SChannel on Windows and
    /// the platform library elsewhere) and managed X.509 code in place of libcrypto (SslAsn1,
    /// SslX509, SslVerify: certificate decoding, the trust store, chain verification as
    /// X509_verify_cert does it, host name checks). One C# method per C function, named after it.
    /// What SslStream cannot express is listed in docs/todo.md 5-29 and raises where it is asked for.
    /// </summary>
    internal static partial class SslModule
    {
        // ---- _sslmodulestate (one per process, as other ports keep theirs) ----
        internal static PyType PySSLContext_Type, PySSLSocket_Type, PySSLMemoryBIO_Type, PySSLSession_Type, PySSLCertificate_Type;
        internal static PyType PySSLErrorObject, PySSLCertVerificationErrorObject, PySSLZeroReturnErrorObject,
            PySSLWantReadErrorObject, PySSLWantWriteErrorObject, PySSLSyscallErrorObject, PySSLEOFErrorObject;
        internal static PyType Sock_Type;
        private static readonly object KeylogLock = new();

        // enum py_ssl_error
        internal const int PY_SSL_ERROR_NONE = 0, PY_SSL_ERROR_SSL = 1, PY_SSL_ERROR_WANT_READ = 2, PY_SSL_ERROR_WANT_WRITE = 3,
            PY_SSL_ERROR_WANT_X509_LOOKUP = 4, PY_SSL_ERROR_SYSCALL = 5, PY_SSL_ERROR_ZERO_RETURN = 6,
            PY_SSL_ERROR_WANT_CONNECT = 7, PY_SSL_ERROR_EOF = 8, PY_SSL_ERROR_NO_SOCKET = 9, PY_SSL_ERROR_INVALID_ERROR_CODE = 10;

        // enum py_ssl_server_or_client
        internal const int PY_SSL_CLIENT = 0, PY_SSL_SERVER = 1;

        // enum py_ssl_cert_requirements
        internal const int PY_SSL_CERT_NONE = 0, PY_SSL_CERT_OPTIONAL = 1, PY_SSL_CERT_REQUIRED = 2;

        // enum py_ssl_version
        internal const int PY_SSL_VERSION_SSL2 = 0, PY_SSL_VERSION_SSL3 = 1, PY_SSL_VERSION_TLS = 2, PY_SSL_VERSION_TLS1 = 3,
            PY_SSL_VERSION_TLS1_1 = 4, PY_SSL_VERSION_TLS1_2 = 5, PY_SSL_VERSION_TLS_CLIENT = 0x10, PY_SSL_VERSION_TLS_SERVER = 0x11;

        // enum py_proto_version
        internal const int PY_PROTO_MINIMUM_SUPPORTED = -2, PY_PROTO_SSLv3 = 0x300, PY_PROTO_TLSv1 = 0x301,
            PY_PROTO_TLSv1_1 = 0x302, PY_PROTO_TLSv1_2 = 0x303, PY_PROTO_TLSv1_3 = 0x304, PY_PROTO_MAXIMUM_SUPPORTED = -1,
            PY_PROTO_MINIMUM_AVAILABLE = PY_PROTO_TLSv1, PY_PROTO_MAXIMUM_AVAILABLE = PY_PROTO_TLSv1_3;

        internal const int PY_SSL_ENCODING_PEM = 1, PY_SSL_ENCODING_DER = 2, PY_SSL_ENCODING_PEM_AUX = 0x101;

        // SSL_OP_* (OpenSSL 3), as the reference build has them.
        internal const ulong SSL_OP_ENABLE_KTLS = 1UL << 3, SSL_OP_LEGACY_SERVER_CONNECT = 1UL << 2,
            SSL_OP_IGNORE_UNEXPECTED_EOF = 1UL << 7, SSL_OP_NO_TICKET = 1UL << 14, SSL_OP_NO_COMPRESSION = 1UL << 17,
            SSL_OP_ENABLE_MIDDLEBOX_COMPAT = 1UL << 20, SSL_OP_CIPHER_SERVER_PREFERENCE = 1UL << 22,
            SSL_OP_NO_SSLv3 = 1UL << 25, SSL_OP_NO_TLSv1 = 1UL << 26, SSL_OP_NO_TLSv1_2 = 1UL << 27,
            SSL_OP_NO_TLSv1_1 = 1UL << 28, SSL_OP_NO_TLSv1_3 = 1UL << 29, SSL_OP_NO_RENEGOTIATION = 1UL << 30,
            SSL_OP_NO_SSLv2 = 0, SSL_OP_SINGLE_DH_USE = 0, SSL_OP_SINGLE_ECDH_USE = 0,
            SSL_OP_DONT_INSERT_EMPTY_FRAGMENTS = 1UL << 11,
            // SSL_OP_ALL: SSL_OP_CRYPTOPRO_TLSEXT_BUG | SSL_OP_DONT_INSERT_EMPTY_FRAGMENTS | SSL_OP_TLSEXT_PADDING | SSL_OP_SAFARI_ECDHE_ECDSA_BUG
            SSL_OP_ALL = (1UL << 31) | (1UL << 11) | (1UL << 4) | (1UL << 6);

        // X509_V_FLAG_*
        internal const ulong X509_V_FLAG_CRL_CHECK = 0x4, X509_V_FLAG_CRL_CHECK_ALL = 0x8, X509_V_FLAG_X509_STRICT = 0x20,
            X509_V_FLAG_ALLOW_PROXY_CERTS = 0x40, X509_V_FLAG_TRUSTED_FIRST = 0x8000, X509_V_FLAG_PARTIAL_CHAIN = 0x80000;

        // X509_CHECK_FLAG_*
        internal const uint X509_CHECK_FLAG_ALWAYS_CHECK_SUBJECT = 0x1, X509_CHECK_FLAG_NO_WILDCARDS = 0x2,
            X509_CHECK_FLAG_NO_PARTIAL_WILDCARDS = 0x4, X509_CHECK_FLAG_MULTI_LABEL_WILDCARDS = 0x8,
            X509_CHECK_FLAG_SINGLE_LABEL_SUBDOMAINS = 0x10, X509_CHECK_FLAG_NEVER_CHECK_SUBJECT = 0x20;

        // The version the module reports: the OpenSSL release the reference build links (its API and
        // behaviour are what this port follows), named as this port.
        private const ulong OPENSSL_VERSION_NUMBER = 0x30600050;
        private const ulong OPENSSL_API_VERSION_NUMBER = 0x30500090;
        private const string OPENSSL_VERSION_TEXT = "OpenSSL 3.6.5 29 Sep 2026 (SharpPy _ssl port over .NET SslStream)";

        private const string PY_SSL_DEFAULT_CIPHER_STRING =
            "@SECLEVEL=2:ECDH+AESGCM:ECDH+CHACHA20:ECDH+AES:DHE+AES:!aNULL:!eNULL:!aDSS:!SHA1:!AESCCM";
        internal const int PY_SSL_MIN_PROTOCOL = 0x303; // TLS1_2_VERSION

        private static PyModule _module;

        // PyInit__ssl / sslmodule_slots
        internal static PyModule Create()
        {
            var module = new PyModule("_ssl");
            if (PyDocs.Lookup("_ssl") is { } moduleDoc)
                module.ModuleDict["__doc__"] = PyString.From(moduleDoc);
            else
                module.ModuleDict["__doc__"] = PyString.From("Implementation module for SSL socket operations.  See the socket module\nfor documentation.");
            sslmodule_init_types(module);
            sslmodule_init_exceptions(module);
            sslmodule_init_socketapi(module);
            sslmodule_init_constants(module);
            sslmodule_init_versioninfo(module);
            // PySSL_methods
            var d = module.ModuleDict;
            d["_test_decode_cert"] = PyArgs.MethO("_ssl", "_test_decode_cert", args => _ssl__test_decode_cert_impl(args[0]));
            d["RAND_add"] = PyArgs.FastCall("_ssl", "RAND_add", 2, 2, args => _ssl_RAND_add(args));
            d["RAND_bytes"] = PyArgs.MethO("_ssl", "RAND_bytes", args => _ssl_RAND_bytes_impl(PyArgs.ParseInt(args[0])));
            d["RAND_status"] = PyArgs.NoArgsFunction("_ssl", "RAND_status", _ssl_RAND_status_impl);
            d["get_default_verify_paths"] = PyArgs.NoArgsFunction("_ssl", "get_default_verify_paths", _ssl_get_default_verify_paths_impl);
            if (OperatingSystem.IsWindows())
            d["enum_certificates"] = new PyBuiltinFunction("enum_certificates", (args, kwargs) =>
            {
                var a = PyArgs.Unpack("enum_certificates", args, kwargs, new[] { "store_name" }, 0, 1, 1);
                return _ssl_enum_certificates_impl(PyArgs.ClinicStr("enum_certificates", "argument 'store_name'", "str", a[0]));
            }) { OwnerModule = "_ssl" };
            if (OperatingSystem.IsWindows())
            d["enum_crls"] = new PyBuiltinFunction("enum_crls", (args, kwargs) =>
            {
                var a = PyArgs.Unpack("enum_crls", args, kwargs, new[] { "store_name" }, 0, 1, 1);
                return _ssl_enum_crls_impl(PyArgs.ClinicStr("enum_crls", "argument 'store_name'", "str", a[0]));
            }) { OwnerModule = "_ssl" };
            d["txt2obj"] = new PyBuiltinFunction("txt2obj", (args, kwargs) =>
            {
                var a = PyArgs.Unpack("txt2obj", args, kwargs, new[] { "txt", "name" }, 0, 1, 2);
                var txt = PyArgs.ClinicStr("txt2obj", "argument 'txt'", "str", a[0]);
                bool name = a[1] != null && a[1].IsTrue();
                return _ssl_txt2obj_impl(txt, name);
            }) { OwnerModule = "_ssl" };
            d["nid2obj"] = PyArgs.MethO("_ssl", "nid2obj", args => _ssl_nid2obj_impl(PyArgs.ParseInt(args[0])));
            _module = module;
            return module;
        }

        // ---- sslmodule_init_types ----

        private static void sslmodule_init_types(PyModule module)
        {
            PySSLContext_Type ??= PySSLContext.MakeType();
            PySSLSocket_Type ??= PySSLSocket.MakeType();
            PySSLMemoryBIO_Type ??= PySSLMemoryBIO.MakeType();
            PySSLSession_Type ??= PySSLSession.MakeType();
            PySSLCertificate_Type ??= PySSLCertificate.MakeType();
            module.ModuleDict["_SSLContext"] = PySSLContext_Type;
            module.ModuleDict["_SSLSocket"] = PySSLSocket_Type;
            module.ModuleDict["MemoryBIO"] = PySSLMemoryBIO_Type;
            module.ModuleDict["SSLSession"] = PySSLSession_Type;
            module.ModuleDict["Certificate"] = PySSLCertificate_Type;
        }

        /// <summary>A spec type of this module: heap, immutable, __module__ "_ssl".</summary>
        internal static PyType SpecType(string name)
        {
            var type = new PyType(name, new[] { PyType.ObjectType }, "_ssl");
            type.MarkSpecHeapType();
            type.MarkImmutableType();
            type.TypeDict["__module__"] = PyString.From("_ssl");
            if (PyDocs.Lookup("_ssl." + name) is { } doc)
                type.TypeDict["__doc__"] = PyDocs.DocFromInternal(name, doc);
            return type;
        }

        // ---- sslmodule_init_exceptions ----

        private const string SSLError_doc = "An error occurred in the SSL implementation.";
        private const string SSLCertVerificationError_doc = "A certificate could not be verified.";
        private const string SSLZeroReturnError_doc = "SSL/TLS session closed cleanly.";
        private const string SSLWantReadError_doc = "Non-blocking SSL socket needs to read more data\nbefore the requested operation can be completed.";
        private const string SSLWantWriteError_doc = "Non-blocking SSL socket needs to write more data\nbefore the requested operation can be completed.";
        private const string SSLSyscallError_doc = "System error when attempting SSL operation.";
        private const string SSLEOFError_doc = "SSL/TLS connection terminated abruptly.";

        // PyErr_NewExceptionWithDoc("ssl." name, doc, base, NULL)
        private static PyType NewExceptionWithDoc(string name, string doc, PyObject[] bases)
        {
            var dict = new PyDict();
            dict.SetItem(PyString.From("__module__"), PyString.From("ssl"));
            dict.SetItem(PyString.From("__doc__"), PyString.From(doc));
            return (PyType)PyType.TypeType.Call(new PyObject[] { PyString.From(name), PyTuple.From(bases), dict }, null);
        }

        private static void sslmodule_init_exceptions(PyModule module)
        {
            if (PySSLErrorObject == null)
            {
                // PyType_FromSpecWithBases(&sslerror_type_spec, PyExc_OSError): "ssl.SSLError" with
                // tp_str SSLError_str.
                PySSLErrorObject = NewExceptionWithDoc("SSLError", SSLError_doc, new PyObject[] { PyType.OSErrorType });
                PySSLErrorObject.SetAttribute("__str__", new PyWrapperDescriptor("__str__", PySSLErrorObject,
                    (self, args, kwargs) => SSLError_str(self), minArgs: 0, maxArgs: 0));
                PySSLErrorObject.MarkImmutableType();
                // ssl.CertificateError used to be a subclass of ValueError
                PySSLCertVerificationErrorObject = NewExceptionWithDoc("SSLCertVerificationError", SSLCertVerificationError_doc,
                    new PyObject[] { PySSLErrorObject, PyType.ValueErrorType });
                PySSLZeroReturnErrorObject = NewExceptionWithDoc("SSLZeroReturnError", SSLZeroReturnError_doc, new PyObject[] { PySSLErrorObject });
                PySSLWantWriteErrorObject = NewExceptionWithDoc("SSLWantWriteError", SSLWantWriteError_doc, new PyObject[] { PySSLErrorObject });
                PySSLWantReadErrorObject = NewExceptionWithDoc("SSLWantReadError", SSLWantReadError_doc, new PyObject[] { PySSLErrorObject });
                PySSLSyscallErrorObject = NewExceptionWithDoc("SSLSyscallError", SSLSyscallError_doc, new PyObject[] { PySSLErrorObject });
                PySSLEOFErrorObject = NewExceptionWithDoc("SSLEOFError", SSLEOFError_doc, new PyObject[] { PySSLErrorObject });
            }
            var d = module.ModuleDict;
            d["SSLError"] = PySSLErrorObject;
            d["SSLCertVerificationError"] = PySSLCertVerificationErrorObject;
            d["SSLZeroReturnError"] = PySSLZeroReturnErrorObject;
            d["SSLWantWriteError"] = PySSLWantWriteErrorObject;
            d["SSLWantReadError"] = PySSLWantReadErrorObject;
            d["SSLSyscallError"] = PySSLSyscallErrorObject;
            d["SSLEOFError"] = PySSLEOFErrorObject;
        }

        /// <summary>SSLError_str: strerror when it is a str, else str(args).</summary>
        private static PyObject SSLError_str(PyObject self)
        {
            if (self.GetAttribute("strerror") is PyString text)
                return text;
            return ((PyBaseException)self).ArgsTuple.ToStr();
        }

        // sslmodule_init_socketapi: PySocketModule_ImportModuleAndAPI
        private static void sslmodule_init_socketapi(PyModule module)
        {
            PyImportSystem.Import("_socket");
            Sock_Type = SocketModule.SocketType;
        }

        // ---- sslmodule_init_constants ----

        private static void sslmodule_add_option(PyModule m, string name, ulong value) =>
            m.ModuleDict[name] = PyInt.From(new System.Numerics.BigInteger(value));

        private static void sslmodule_init_constants(PyModule m)
        {
            var d = m.ModuleDict;
            void Int(string name, long value) => d[name] = PyInt.From(value);
            d["_DEFAULT_CIPHERS"] = PyString.From(PY_SSL_DEFAULT_CIPHER_STRING);
            Int("SSL_ERROR_ZERO_RETURN", PY_SSL_ERROR_ZERO_RETURN);
            Int("SSL_ERROR_WANT_READ", PY_SSL_ERROR_WANT_READ);
            Int("SSL_ERROR_WANT_WRITE", PY_SSL_ERROR_WANT_WRITE);
            Int("SSL_ERROR_WANT_X509_LOOKUP", PY_SSL_ERROR_WANT_X509_LOOKUP);
            Int("SSL_ERROR_SYSCALL", PY_SSL_ERROR_SYSCALL);
            Int("SSL_ERROR_SSL", PY_SSL_ERROR_SSL);
            Int("SSL_ERROR_WANT_CONNECT", PY_SSL_ERROR_WANT_CONNECT);
            /* non ssl.h errorcodes */
            Int("SSL_ERROR_EOF", PY_SSL_ERROR_EOF);
            Int("SSL_ERROR_INVALID_ERROR_CODE", PY_SSL_ERROR_INVALID_ERROR_CODE);
            /* cert requirements */
            Int("CERT_NONE", PY_SSL_CERT_NONE);
            Int("CERT_OPTIONAL", PY_SSL_CERT_OPTIONAL);
            Int("CERT_REQUIRED", PY_SSL_CERT_REQUIRED);
            /* CRL verification for verification_flags */
            Int("VERIFY_DEFAULT", 0);
            Int("VERIFY_CRL_CHECK_LEAF", (long)X509_V_FLAG_CRL_CHECK);
            Int("VERIFY_CRL_CHECK_CHAIN", (long)(X509_V_FLAG_CRL_CHECK | X509_V_FLAG_CRL_CHECK_ALL));
            Int("VERIFY_X509_STRICT", (long)X509_V_FLAG_X509_STRICT);
            Int("VERIFY_ALLOW_PROXY_CERTS", (long)X509_V_FLAG_ALLOW_PROXY_CERTS);
            Int("VERIFY_X509_TRUSTED_FIRST", (long)X509_V_FLAG_TRUSTED_FIRST);
            Int("VERIFY_X509_PARTIAL_CHAIN", (long)X509_V_FLAG_PARTIAL_CHAIN);
            /* Alert Descriptions from ssl.h */
            foreach (var (name, value) in new (string, int)[]
            {
                ("CLOSE_NOTIFY", 0), ("UNEXPECTED_MESSAGE", 10), ("BAD_RECORD_MAC", 20), ("RECORD_OVERFLOW", 22),
                ("DECOMPRESSION_FAILURE", 30), ("HANDSHAKE_FAILURE", 40), ("BAD_CERTIFICATE", 42),
                ("UNSUPPORTED_CERTIFICATE", 43), ("CERTIFICATE_REVOKED", 44), ("CERTIFICATE_EXPIRED", 45),
                ("CERTIFICATE_UNKNOWN", 46), ("ILLEGAL_PARAMETER", 47), ("UNKNOWN_CA", 48), ("ACCESS_DENIED", 49),
                ("DECODE_ERROR", 50), ("DECRYPT_ERROR", 51), ("PROTOCOL_VERSION", 70), ("INSUFFICIENT_SECURITY", 71),
                ("INTERNAL_ERROR", 80), ("USER_CANCELLED", 90), ("NO_RENEGOTIATION", 100), ("UNSUPPORTED_EXTENSION", 110),
                ("CERTIFICATE_UNOBTAINABLE", 111), ("UNRECOGNIZED_NAME", 112), ("BAD_CERTIFICATE_STATUS_RESPONSE", 113),
                ("BAD_CERTIFICATE_HASH_VALUE", 114), ("UNKNOWN_PSK_IDENTITY", 115),
            })
                Int("ALERT_DESCRIPTION_" + name, value);
            /* protocol versions */
            Int("PROTOCOL_SSLv23", PY_SSL_VERSION_TLS);
            Int("PROTOCOL_TLS", PY_SSL_VERSION_TLS);
            Int("PROTOCOL_TLS_CLIENT", PY_SSL_VERSION_TLS_CLIENT);
            Int("PROTOCOL_TLS_SERVER", PY_SSL_VERSION_TLS_SERVER);
            Int("PROTOCOL_TLSv1", PY_SSL_VERSION_TLS1);
            Int("PROTOCOL_TLSv1_1", PY_SSL_VERSION_TLS1_1);
            Int("PROTOCOL_TLSv1_2", PY_SSL_VERSION_TLS1_2);
            /* protocol options */
            sslmodule_add_option(m, "OP_ALL", SSL_OP_ALL & ~SSL_OP_DONT_INSERT_EMPTY_FRAGMENTS);
            sslmodule_add_option(m, "OP_NO_SSLv2", SSL_OP_NO_SSLv2);
            sslmodule_add_option(m, "OP_NO_SSLv3", SSL_OP_NO_SSLv3);
            sslmodule_add_option(m, "OP_NO_TLSv1", SSL_OP_NO_TLSv1);
            sslmodule_add_option(m, "OP_NO_TLSv1_1", SSL_OP_NO_TLSv1_1);
            sslmodule_add_option(m, "OP_NO_TLSv1_2", SSL_OP_NO_TLSv1_2);
            sslmodule_add_option(m, "OP_NO_TLSv1_3", SSL_OP_NO_TLSv1_3);
            sslmodule_add_option(m, "OP_CIPHER_SERVER_PREFERENCE", SSL_OP_CIPHER_SERVER_PREFERENCE);
            sslmodule_add_option(m, "OP_SINGLE_DH_USE", SSL_OP_SINGLE_DH_USE);
            sslmodule_add_option(m, "OP_NO_TICKET", SSL_OP_NO_TICKET);
            sslmodule_add_option(m, "OP_LEGACY_SERVER_CONNECT", SSL_OP_LEGACY_SERVER_CONNECT);
            sslmodule_add_option(m, "OP_SINGLE_ECDH_USE", SSL_OP_SINGLE_ECDH_USE);
            sslmodule_add_option(m, "OP_NO_COMPRESSION", SSL_OP_NO_COMPRESSION);
            sslmodule_add_option(m, "OP_ENABLE_MIDDLEBOX_COMPAT", SSL_OP_ENABLE_MIDDLEBOX_COMPAT);
            sslmodule_add_option(m, "OP_NO_RENEGOTIATION", SSL_OP_NO_RENEGOTIATION);
            sslmodule_add_option(m, "OP_IGNORE_UNEXPECTED_EOF", SSL_OP_IGNORE_UNEXPECTED_EOF);
            sslmodule_add_option(m, "OP_ENABLE_KTLS", SSL_OP_ENABLE_KTLS);
            Int("HOSTFLAG_ALWAYS_CHECK_SUBJECT", X509_CHECK_FLAG_ALWAYS_CHECK_SUBJECT);
            Int("HOSTFLAG_NEVER_CHECK_SUBJECT", X509_CHECK_FLAG_NEVER_CHECK_SUBJECT);
            Int("HOSTFLAG_NO_WILDCARDS", X509_CHECK_FLAG_NO_WILDCARDS);
            Int("HOSTFLAG_NO_PARTIAL_WILDCARDS", X509_CHECK_FLAG_NO_PARTIAL_WILDCARDS);
            Int("HOSTFLAG_MULTI_LABEL_WILDCARDS", X509_CHECK_FLAG_MULTI_LABEL_WILDCARDS);
            Int("HOSTFLAG_SINGLE_LABEL_SUBDOMAINS", X509_CHECK_FLAG_SINGLE_LABEL_SUBDOMAINS);
            /* file types */
            Int("ENCODING_PEM", PY_SSL_ENCODING_PEM);
            Int("ENCODING_DER", PY_SSL_ENCODING_DER);
            /* protocol versions */
            Int("PROTO_MINIMUM_SUPPORTED", PY_PROTO_MINIMUM_SUPPORTED);
            Int("PROTO_MAXIMUM_SUPPORTED", PY_PROTO_MAXIMUM_SUPPORTED);
            Int("PROTO_SSLv3", PY_PROTO_SSLv3);
            Int("PROTO_TLSv1", PY_PROTO_TLSv1);
            Int("PROTO_TLSv1_1", PY_PROTO_TLSv1_1);
            Int("PROTO_TLSv1_2", PY_PROTO_TLSv1_2);
            Int("PROTO_TLSv1_3", PY_PROTO_TLSv1_3);
            d["HAS_SNI"] = PyBool.True;
            d["HAS_TLS_UNIQUE"] = PyBool.True;
            d["HAS_ECDH"] = PyBool.True;
            d["HAS_NPN"] = PyBool.False;
            d["HAS_ALPN"] = PyBool.True;
            d["HAS_SSLv2"] = PyBool.False;
            d["HAS_SSLv3"] = PyBool.False;
            d["HAS_TLSv1"] = PyBool.True;
            d["HAS_TLSv1_1"] = PyBool.True;
            d["HAS_TLSv1_2"] = PyBool.True;
            d["HAS_TLSv1_3"] = PyBool.True;
        }

        // parse_openssl_version
        private static PyObject parse_openssl_version(ulong libver)
        {
            uint status = (uint)(libver & 0xF);
            libver >>= 4;
            uint patch = (uint)(libver & 0xFF);
            libver >>= 8;
            uint fix = (uint)(libver & 0xFF);
            libver >>= 8;
            uint minor = (uint)(libver & 0xFF);
            libver >>= 8;
            uint major = (uint)(libver & 0xFF);
            return PyTuple.From(new PyObject[] { PyInt.From(major), PyInt.From(minor), PyInt.From(fix), PyInt.From(patch), PyInt.From(status) });
        }

        // sslmodule_init_versioninfo
        private static void sslmodule_init_versioninfo(PyModule m)
        {
            m.ModuleDict["OPENSSL_VERSION_NUMBER"] = PyInt.From((long)OPENSSL_VERSION_NUMBER);
            m.ModuleDict["OPENSSL_VERSION_INFO"] = parse_openssl_version(OPENSSL_VERSION_NUMBER);
            m.ModuleDict["OPENSSL_VERSION"] = PyString.From(OPENSSL_VERSION_TEXT);
            m.ModuleDict["_OPENSSL_API_VERSION"] = parse_openssl_version(OPENSSL_API_VERSION_NUMBER);
        }

        // ---- errors ----

        /// <summary>
        /// fill_and_set_sslerror: the message "[lib: reason] errstr[: verify] (_ssl.c:lineno)", the
        /// errno argument ERR_GET_REASON(ssl_errno), and the reason, library and (for
        /// SSLCertVerificationError) verify_code / verify_message attributes.
        /// </summary>
        internal static PythonException fill_and_set_sslerror(PySSLSocket sslsock, PyType type, int ssl_errno,
            string errstr, int lineno, SslErr e)
        {
            string reason_obj = null, lib_obj = null;
            PyObject verify_obj = null, verify_code_obj = null;
            if (e != null)
            {
                reason_obj = e.ReasonName;
                lib_obj = e.LibName;
                errstr ??= e.Text;
            }
            errstr ??= "unknown error";

            /* verify code for cert validation error */
            if (sslsock != null && ReferenceEquals(type, PySSLCertVerificationErrorObject))
            {
                long verify_code = sslsock.Conn?.VerifyResult ?? 0;
                verify_code_obj = PyInt.From(verify_code);
                switch (verify_code)
                {
                    case SslVerify.X509_V_ERR_HOSTNAME_MISMATCH:
                        verify_obj = PyString.From($"Hostname mismatch, certificate is not valid for '{sslsock.server_hostname?.ToStr().Value}'.");
                        break;
                    case SslVerify.X509_V_ERR_IP_ADDRESS_MISMATCH:
                        verify_obj = PyString.From($"IP address mismatch, certificate is not valid for '{sslsock.server_hostname?.ToStr().Value}'.");
                        break;
                    default:
                        var verify_str = SslVerify.X509_verify_cert_error_string((int)verify_code);
                        verify_obj = verify_str != null ? PyString.From(verify_str) : PyNone.Instance;
                        break;
                }
            }

            string msg;
            if (verify_obj != null && reason_obj != null && lib_obj != null)
                msg = $"[{lib_obj}: {reason_obj}] {errstr}: {verify_obj.ToStr().Value} (_ssl.c:{lineno})";
            else if (reason_obj != null && lib_obj != null)
                msg = $"[{lib_obj}: {reason_obj}] {errstr} (_ssl.c:{lineno})";
            else if (lib_obj != null)
                msg = $"[{lib_obj}] {errstr} (_ssl.c:{lineno})";
            else
                msg = $"{errstr} (_ssl.c:{lineno})";

            var err_value = type.Call(new PyObject[] { PyInt.From(ssl_errno & 0x7FFFFF), PyString.From(msg) }, null);
            err_value.SetAttribute("reason", reason_obj != null ? PyString.From(reason_obj) : PyNone.Instance);
            err_value.SetAttribute("library", lib_obj != null ? PyString.From(lib_obj) : PyNone.Instance);
            if (sslsock != null && ReferenceEquals(type, PySSLCertVerificationErrorObject))
            {
                /* Only set verify code / message for SSLCertVerificationError */
                err_value.SetAttribute("verify_code", verify_code_obj);
                err_value.SetAttribute("verify_message", verify_obj);
            }
            return new PythonException((PyBaseException)err_value);
        }

        /// <summary>PySSL_ChainExceptions: an exception a callback stored becomes the context of the current one.</summary>
        internal static PythonException PySSL_ChainExceptions(PySSLSocket sslsock, PythonException current)
        {
            if (sslsock.exc == null)
                return current;
            var stored = sslsock.exc;
            sslsock.exc = null;
            if (current == null)
                return stored;
            // _PyErr_ChainExceptions1: the stored exception is raised, the current one is its context.
            if (stored.PyException.__context__ == null)
                stored.PyException.__context__ = current.PyException;
            return stored;
        }

        /// <summary>PySSL_SetError: the exception for the last SSL_get_error of the socket.</summary>
        internal static PythonException PySSL_SetError(PySSLSocket sslsock, int ret, int lineno)
        {
            PyType type = PySSLErrorObject;
            string errstr = null;
            int p = PY_SSL_ERROR_NONE;
            var conn = sslsock.Conn;
            SslErr e = conn?.LastError;

            if (conn != null)
            {
                var err = sslsock.err;
                switch (err.ssl)
                {
                    case SslConnection.SSL_ERROR_ZERO_RETURN:
                        errstr = "TLS/SSL connection has been closed (EOF)";
                        type = PySSLZeroReturnErrorObject;
                        p = PY_SSL_ERROR_ZERO_RETURN;
                        break;
                    case SslConnection.SSL_ERROR_WANT_READ:
                        errstr = "The operation did not complete (read)";
                        type = PySSLWantReadErrorObject;
                        p = PY_SSL_ERROR_WANT_READ;
                        break;
                    case SslConnection.SSL_ERROR_WANT_WRITE:
                        p = PY_SSL_ERROR_WANT_WRITE;
                        type = PySSLWantWriteErrorObject;
                        errstr = "The operation did not complete (write)";
                        break;
                    case SslConnection.SSL_ERROR_WANT_X509_LOOKUP:
                        p = PY_SSL_ERROR_WANT_X509_LOOKUP;
                        errstr = "The operation did not complete (X509 lookup)";
                        break;
                    case SslConnection.SSL_ERROR_WANT_CONNECT:
                        p = PY_SSL_ERROR_WANT_CONNECT;
                        errstr = "The operation did not complete (connect)";
                        break;
                    case SslConnection.SSL_ERROR_SYSCALL:
                        if (e == null)
                        {
                            var s = sslsock.GetSocket(out bool gone);
                            if (ret == 0 || gone)
                            {
                                p = PY_SSL_ERROR_EOF;
                                type = PySSLEOFErrorObject;
                                errstr = "EOF occurred in violation of protocol";
                            }
                            else if (s != null && ret == -1)
                            {
                                /* underlying BIO reported an I/O error */
                                ErrQueue.Clear();
                                if (err.ws != 0)
                                    return SocketModule.SetError(err.ws);
                                if (err.c != 0)
                                    return FromErrno(err.c);
                                p = PY_SSL_ERROR_EOF;
                                type = PySSLEOFErrorObject;
                                errstr = "EOF occurred in violation of protocol";
                            }
                            else
                            {
                                /* possible? */
                                p = PY_SSL_ERROR_SYSCALL;
                                type = PySSLSyscallErrorObject;
                                errstr = "Some I/O error occurred";
                            }
                        }
                        else
                        {
                            if (e.Lib == SslErr.LIB_SSL && e.ReasonName == "CERTIFICATE_VERIFY_FAILED")
                                type = PySSLCertVerificationErrorObject;
                            p = PY_SSL_ERROR_SYSCALL;
                        }
                        break;
                    case SslConnection.SSL_ERROR_SSL:
                        p = PY_SSL_ERROR_SSL;
                        if (e == null)
                            /* possible? */
                            errstr = "A failure in the SSL library occurred";
                        if (e != null && e.Lib == SslErr.LIB_SSL && e.ReasonName == "CERTIFICATE_VERIFY_FAILED")
                            type = PySSLCertVerificationErrorObject;
                        /* OpenSSL 3.0 changed transport EOF from SSL_ERROR_SYSCALL with
                         * zero return value to SSL_ERROR_SSL with a special error code. */
                        if (e != null && e.Lib == SslErr.LIB_SSL && e.ReasonName == "UNEXPECTED_EOF_WHILE_READING")
                        {
                            p = PY_SSL_ERROR_EOF;
                            type = PySSLEOFErrorObject;
                            errstr = "EOF occurred in violation of protocol";
                        }
                        break;
                    default:
                        p = PY_SSL_ERROR_INVALID_ERROR_CODE;
                        errstr = "Invalid error code";
                        break;
                }
            }
            var error = fill_and_set_sslerror(sslsock, type, p, errstr, lineno, e);
            ErrQueue.Clear();
            if (conn != null)
                conn.LastError = null;
            return PySSL_ChainExceptions(sslsock, error);
        }

        /// <summary>_setSSLError: errstr, or the last queued error when errstr is null.</summary>
        internal static PythonException _setSSLError(string errstr, int errcode, int lineno)
        {
            SslErr e = null;
            if (errstr == null)
            {
                e = ErrQueue.PeekLast();
                errcode = e?.Reason ?? 0;
            }
            else
                errcode = 0;
            var error = fill_and_set_sslerror(null, PySSLErrorObject, errcode, errstr, lineno, e);
            ErrQueue.Clear();
            return error;
        }

        /// <summary>_ssl_deprecated: PyErr_WarnEx(PyExc_DeprecationWarning, msg, stacklevel).</summary>
        internal static void _ssl_deprecated(string msg, int stacklevel) =>
            PyWarnings.Warn(PyType.DeprecationWarningType, msg, stacklevel);

        /// <summary>PyErr_SetString(type, message)</summary>
        internal static PythonException SetString(PyType type, string message) =>
            new((PyBaseException)type.Call(new PyObject[] { PyString.From(message) }, null));

        /// <summary>PyErr_SetFromErrno for a failed fopen: OSError(errno, strerror), the subclass by errno.</summary>
        internal static PythonException FromErrno(int errno)
        {
            string message = errno switch
            {
                2 => "No such file or directory",
                13 => "Permission denied",
                21 => "Is a directory",
                _ => null,
            };
            if (message == null)
            {
                // PyErr_SetFromErrno on Windows: FormatMessage for a number past the C runtime's table.
                try
                {
                    message = new System.ComponentModel.Win32Exception(errno).Message.TrimEnd();
                    if (message.EndsWith(".", StringComparison.Ordinal))
                        message = message.Substring(0, message.Length - 1);
                }
                catch (Exception)
                {
                    message = "Unknown error";
                }
            }
            return new PythonException(PyOSError.FromArguments(new PyObject[] { PyInt.From(errno), PyString.From(message) }, null));
        }

        /// <summary>
        /// fopen(path, "rb") through the host's file layer (Godot's res:// and user:// too): the bytes,
        /// or the errno it fails with.
        /// </summary>
        internal static byte[] ReadFile(string path, out int errno)
        {
            errno = 0;
            try
            {
                if (IOHelper.DirExists(path))
                {
                    errno = 13; // fopen of a directory on Windows: EACCES
                    return null;
                }
                if (!IOHelper.FileExists(path))
                {
                    errno = 2;
                    return null;
                }
                return IOHelper.ReadAllBytes(path);
            }
            catch (UnauthorizedAccessException)
            {
                errno = 13;
            }
            catch (System.IO.IOException)
            {
                errno = 2;
            }
            return null;
        }

        /// <summary>PyUnicode_FSConverter: the path, str or bytes or os.PathLike, as a string.</summary>
        internal static string FSConverter(PyObject arg)
        {
            var path = NtModule.FsPathObject(arg);
            string text = path switch
            {
                PyString s => s.Value,
                PyBytes b => PyStringNewArguments.DecodeBytes(b.Value, "utf-8", OperatingSystem.IsWindows() ? "surrogatepass" : "surrogateescape").Value,
                _ => throw PyTypeError.Create($"expected str, bytes or os.PathLike object, not {arg.GetTypeName()}"),
            };
            if (text.IndexOf('\0') >= 0)
                throw PyValueError.Create("embedded null byte");
            return text;
        }

        // ---- certificate decoding ----

        /// <summary>_asn1obj2py: OBJ_obj2txt of the object; None for an empty no_name text.</summary>
        internal static PyObject _asn1obj2py(string oid, bool no_name)
        {
            var text = oid == null ? "" : SslObjects.Obj2Txt(oid, no_name);
            if (text.Length == 0 && no_name)
                return PyNone.Instance;
            return PyString.From(text);
        }

        /// <summary>PyUnicode_FromStringAndSize: UTF-8, strict.</summary>
        internal static PyObject Utf8String(byte[] data) =>
            PyStringNewArguments.DecodeBytes(data, "utf-8", "strict");

        /// <summary>_create_tuple_for_attribute: (name, value), a BIT STRING value as bytes.</summary>
        private static PyObject _create_tuple_for_attribute(X509NameEntry entry)
        {
            var pyname = _asn1obj2py(entry.Oid, false);
            if (entry.Tag == Asn1.BIT_STRING)
                return PyTuple.From(new PyObject[] { pyname, PyBytes.From(entry.Value) });
            var valuebuf = Asn1.StringToUtf8(entry.Tag, entry.Value);
            if (valuebuf == null)
                throw _setSSLError(null, 0, 1083);
            return PyTuple.From(new PyObject[] { pyname, Utf8String(valuebuf) });
        }

        /// <summary>_create_tuple_for_X509_NAME: a tuple of RDNs, each a tuple of (name, value) pairs.</summary>
        internal static PyObject _create_tuple_for_X509_NAME(X509Name xname)
        {
            var dn = new List<PyObject>();
            var rdn = new List<PyObject>();
            int rdn_level = -1;
            foreach (var entry in xname.Entries)
            {
                /* check to see if we've gotten to a new RDN */
                if (rdn_level >= 0 && rdn_level != entry.Set)
                {
                    dn.Add(PyTuple.From(rdn.ToArray()));
                    rdn = new List<PyObject>();
                }
                rdn_level = entry.Set;
                rdn.Add(_create_tuple_for_attribute(entry));
            }
            /* now, there's typically a dangling RDN */
            if (rdn.Count > 0)
                dn.Add(PyTuple.From(rdn.ToArray()));
            return PyTuple.From(dn.ToArray());
        }

        /// <summary>GENERAL_NAME_print of the kinds _get_peer_alt_names leaves to it.</summary>
        private static string GENERAL_NAME_print(GeneralName name)
        {
            switch (name.Type)
            {
                case GeneralName.GEN_OTHERNAME:
                    {
                        int nid = SslObjects.Oid2Nid(name.OtherOid);
                        string sn = SslObjects.Nid2Sn(nid);
                        bool srv = sn == "SRVName";
                        if ((srv && name.OtherTag != Asn1.IA5STRING) || (!srv && name.OtherTag != Asn1.UTF8STRING))
                            return "othername:<unsupported>";
                        string value = Encoding.UTF8.GetString(name.OtherValue ?? Array.Empty<byte>());
                        return sn switch
                        {
                            "id-on-SmtpUTF8Mailbox" => "othername:SmtpUTF8Mailbox:" + value,
                            "id-on-xmppAddr" => "othername:XmppAddr:" + value,
                            "SRVName" => "othername:SRVName:" + value,
                            "msUPN" => "othername:UPN:" + value,
                            "id-on-NAIRealm" => "othername:NAIRealm:" + value,
                            _ => "othername:<unsupported>",
                        };
                    }
                case GeneralName.GEN_X400:
                    return "X400Name:<unsupported>";
                case GeneralName.GEN_EDIPARTY:
                    return "EdiPartyName:<unsupported>";
                case GeneralName.GEN_RID:
                    return "Registered ID:" + SslObjects.Obj2Txt(Asn1.OidText(name.Bytes), false);
                default:
                    return "<unknown>:";
            }
        }

        /// <summary>_get_peer_alt_names: the subjectAltName entries as (kind, value) tuples, or None.</summary>
        internal static PyObject _get_peer_alt_names(X509Cert certificate)
        {
            var names = certificate.SubjectAltNames();
            if (names == null)
                return PyNone.Instance;
            var peer_alt_names = new List<PyObject>();
            foreach (var name in names)
            {
                PyObject t;
                switch (name.Type)
                {
                    case GeneralName.GEN_DIRNAME:
                        /* we special-case DirName as a tuple of tuples of attributes */
                        t = PyTuple.From(new PyObject[] { PyString.From("DirName"), _create_tuple_for_X509_NAME(name.DirName) });
                        break;
                    case GeneralName.GEN_EMAIL:
                    case GeneralName.GEN_DNS:
                    case GeneralName.GEN_URI:
                        {
                            string kind = name.Type == GeneralName.GEN_EMAIL ? "email" : name.Type == GeneralName.GEN_DNS ? "DNS" : "URI";
                            t = PyTuple.From(new PyObject[] { PyString.From(kind), Utf8String(name.Bytes) });
                            break;
                        }
                    case GeneralName.GEN_RID:
                        {
                            string text = SslObjects.Obj2Txt(Asn1.OidText(name.Bytes), false);
                            t = PyTuple.From(new PyObject[] { PyString.From("Registered ID"), PyString.From(text.Length >= 2047 ? "<INVALID>" : text) });
                            break;
                        }
                    case GeneralName.GEN_IPADD:
                        {
                            /* OpenSSL < 3.0.0 adds a trailing \n to IPv6. 3.0.0 removed
                             * the trailing newline. Remove it in all versions */
                            var p = name.Bytes;
                            string v;
                            if (p.Length == 4)
                                v = $"{p[0]}.{p[1]}.{p[2]}.{p[3]}";
                            else if (p.Length == 16)
                            {
                                var parts = new string[8];
                                for (int i = 0; i < 8; i++)
                                    parts[i] = ((p[2 * i] << 8) | p[2 * i + 1]).ToString("X");
                                v = string.Join(":", parts);
                            }
                            else
                                v = "<invalid>";
                            t = PyTuple.From(new PyObject[] { PyString.From("IP Address"), PyString.From(v) });
                            break;
                        }
                    default:
                        {
                            /* for everything else, we use the OpenSSL print form */
                            switch (name.Type)
                            {
                                case GeneralName.GEN_OTHERNAME:
                                case GeneralName.GEN_X400:
                                case GeneralName.GEN_EDIPARTY:
                                case GeneralName.GEN_RID:
                                    break;
                                default:
                                    PyWarnings.Warn(PyType.RuntimeWarningType, $"Unknown general name type {name.Type}", 1);
                                    break;
                            }
                            string buf = GENERAL_NAME_print(name);
                            int vptr = buf.IndexOf(':');
                            if (vptr < 0)
                                throw PyValueError.Create($"Invalid value {buf}");
                            t = PyTuple.From(new PyObject[] { PyString.From(buf.Substring(0, vptr)), PyString.From(buf.Substring(vptr + 1)) });
                            break;
                        }
                }
                peer_alt_names.Add(t);
            }
            return PyTuple.From(peer_alt_names.ToArray());
        }

        /// <summary>_get_aia_uri: the URIs of the authority information access entries of one method.</summary>
        private static PyObject _get_aia_uri(X509Cert certificate, string method)
        {
            if (certificate.Ext(X509Cert.OID_AIA) is not { } ext)
                return PyNone.Instance;
            var lst = new List<PyObject>();
            try
            {
                foreach (var ad in Asn1.Parse(ext.Value).Children())
                {
                    var parts = ad.Children();
                    if (parts.Count != 2 || Asn1.OidText(parts[0].Content) != method || !parts[1].Is(2, GeneralName.GEN_URI))
                        continue;
                    lst.Add(Utf8String(parts[1].ContentBytes()));
                }
            }
            catch (Asn1Exception)
            {
                return PyNone.Instance;
            }
            return lst.Count == 0 ? PyNone.Instance : PyTuple.From(lst.ToArray());
        }

        /// <summary>_get_crl_dp: the URIs of the fullName distribution points.</summary>
        private static PyObject _get_crl_dp(X509Cert certificate)
        {
            if (certificate.Ext(X509Cert.OID_CRLDP) is not { } ext)
                return PyNone.Instance;
            var lst = new List<PyObject>();
            try
            {
                foreach (var dp in Asn1.Parse(ext.Value).Children())
                {
                    foreach (var field in dp.Children())
                    {
                        if (!field.Is(2, 0))
                            continue;
                        /* Ignore empty DP value, CVE-2019-5010 */
                        foreach (var dpname in field.Children())
                        {
                            if (!dpname.Is(2, 0))
                                continue;
                            foreach (var gn in dpname.Children())
                                if (gn.Is(2, GeneralName.GEN_URI))
                                    lst.Add(Utf8String(gn.ContentBytes()));
                        }
                    }
                }
            }
            catch (Asn1Exception)
            {
                return PyNone.Instance;
            }
            return lst.Count > 0 ? PyTuple.From(lst.ToArray()) : PyNone.Instance;
        }

        private static void Set(PyDict dict, string key, PyObject value) => dict.SetItem(PyString.From(key), value);

        /// <summary>_decode_certificate: the dict getpeercert() and get_ca_certs() give.</summary>
        internal static PyObject _decode_certificate(X509Cert certificate)
        {
            var retval = new PyDict();
            Set(retval, "subject", _create_tuple_for_X509_NAME(certificate.Subject));
            Set(retval, "issuer", _create_tuple_for_X509_NAME(certificate.Issuer));
            Set(retval, "version", PyInt.From(certificate.Version + 1));
            Set(retval, "serialNumber", PyString.From(Asn1.IntegerHex(certificate.Serial)));
            Set(retval, "notBefore", PyString.From(Asn1.TimePrint(certificate.NotBeforeTag, certificate.NotBefore)));
            Set(retval, "notAfter", PyString.From(Asn1.TimePrint(certificate.NotAfterTag, certificate.NotAfter)));
            /* Now look for subjectAltName */
            var peer_alt_names = _get_peer_alt_names(certificate);
            if (peer_alt_names is not PyNone)
                Set(retval, "subjectAltName", peer_alt_names);
            /* Authority Information Access: OCSP URIs */
            var obj = _get_aia_uri(certificate, X509Cert.OID_AD_OCSP);
            if (obj is not PyNone)
                Set(retval, "OCSP", obj);
            obj = _get_aia_uri(certificate, X509Cert.OID_AD_CA_ISSUERS);
            if (obj is not PyNone)
                Set(retval, "caIssuers", obj);
            /* CDP (CRL distribution points) */
            obj = _get_crl_dp(certificate);
            if (obj is not PyNone)
                Set(retval, "crlDistributionPoints", obj);
            return retval;
        }

        /// <summary>_certificate_to_der: i2d_X509</summary>
        internal static PyObject _certificate_to_der(X509Cert certificate) => PyBytes.From(certificate.Der);

        /// <summary>_ssl._test_decode_cert(path, /)</summary>
        private static PyObject _ssl__test_decode_cert_impl(PyObject pathArg)
        {
            string path = FSConverter(pathArg);
            var data = ReadFile(path, out _);
            if (data == null)
                throw SetString(PySSLErrorObject, "Can't open file");
            X509Cert x;
            try
            {
                int pos = 0;
                x = Pem.ReadCertificate(data, ref pos);
            }
            catch (Asn1Exception)
            {
                x = null;
            }
            if (x == null)
                throw SetString(PySSLErrorObject, "Error decoding PEM-encoded file");
            return _decode_certificate(x);
        }

        // ---- RAND ----

        /// <summary>_ssl.RAND_add(string, entropy, /): mixes nothing into the system CSPRNG (RandomNumberGenerator needs no seeding).</summary>
        private static PyObject _ssl_RAND_add(PyObject[] args)
        {
            BytesMethods.StrOrBuffer(args[0]);
            args[1].ToFloat();
            return PyNone.Instance;
        }

        /// <summary>PySSL_RAND: n bytes of RAND_bytes (the system CSPRNG).</summary>
        private static PyObject PySSL_RAND(int len)
        {
            if (len < 0)
                throw PyValueError.Create("num must be positive");
            var bytes = new byte[len];
            RandomNumberGenerator.Fill(bytes);
            return PyBytes.From(bytes);
        }

        private static PyObject _ssl_RAND_bytes_impl(int n) => PySSL_RAND(n);

        /// <summary>_ssl.RAND_status: the system generator is always seeded.</summary>
        private static PyObject _ssl_RAND_status_impl() => PyBool.True;

        /// <summary>
        /// _ssl.get_default_verify_paths: X509_get_default_cert_file_env and friends. The paths are
        /// the reference Windows build's OPENSSLDIR ones; set_default_verify_paths reads them (and
        /// the environment variables) as OpenSSL does.
        /// </summary>
        private static PyObject _ssl_get_default_verify_paths_impl() => PyTuple.From(new PyObject[]
        {
            PyString.From(SslStore.CertFileEnv), PyString.From(SslStore.DefaultCertFile),
            PyString.From(SslStore.CertDirEnv), PyString.From(SslStore.DefaultCertDir),
        });

        /// <summary>asn1obj2py: (nid, sn, ln, oid) of a known object.</summary>
        private static PyObject asn1obj2py(SslObjects.Obj obj)
        {
            if (obj.Nid == SslObjects.NID_undef)
                throw PyValueError.Create("Unknown object");
            return PyTuple.From(new PyObject[]
            {
                PyInt.From(obj.Nid), PyString.From(obj.Sn), PyString.From(obj.Ln), _asn1obj2py(obj.Oid, true),
            });
        }

        /// <summary>_ssl.txt2obj(txt, name=False)</summary>
        private static PyObject _ssl_txt2obj_impl(string txt, bool name)
        {
            var obj = SslObjects.Txt2Obj(txt, !name);
            if (obj == null)
                throw PyValueError.Create($"unknown object '{(txt.Length > 100 ? txt.Substring(0, 100) : txt)}'");
            return asn1obj2py(obj);
        }

        /// <summary>_ssl.nid2obj(nid, /)</summary>
        private static PyObject _ssl_nid2obj_impl(int nid)
        {
            if (nid < SslObjects.NID_undef)
                throw PyValueError.Create("NID must be positive.");
            if (nid == SslObjects.NID_undef)
                throw PyValueError.Create("Unknown object");
            var obj = SslObjects.Nid2Obj(nid);
            if (obj == null)
                throw PyValueError.Create($"unknown NID {nid}");
            return asn1obj2py(obj);
        }

        // ---- Windows certificate stores ----

        /// <summary>certEncodingType: X509_ASN_ENCODING or PKCS_7_ASN_ENCODING.</summary>
        private static PyObject certEncodingType(int encodingType) => encodingType switch
        {
            1 => PyString.From("x509_asn"),
            0x10000 => PyString.From("pkcs_7_asn"),
            _ => PyInt.From(encodingType),
        };

        /// <summary>
        /// One element of a system store as the registry provider keeps it: the "Blob" value of
        /// ...\SystemCertificates\{store}\{Certificates|CRLs}\{hash}, a list of properties
        /// (id, reserved, length, data); the encoded certificate (CERT_CERT_PROP_ID 32) or CRL
        /// (CERT_CRL_PROP_ID 33) and its other properties.
        /// </summary>
        private sealed class StoreElement
        {
            public byte[] Encoded;
            public int EncodingType = 1;
            public byte[] EnhancedKeyUsage; // CERT_ENHKEY_USAGE_PROP_ID 9
        }

        private static StoreElement ParseBlob(byte[] blob, int elementProp)
        {
            var element = new StoreElement();
            int p = 0;
            while (p + 12 <= blob.Length)
            {
                int id = BitConverter.ToInt32(blob, p);
                int len = BitConverter.ToInt32(blob, p + 8);
                p += 12;
                if (len < 0 || p + len > blob.Length)
                    break;
                if (id == elementProp)
                {
                    element.Encoded = blob.AsSpan(p, len).ToArray();
                }
                else if (id == 9)
                    element.EnhancedKeyUsage = blob.AsSpan(p, len).ToArray();
                p += len;
            }
            return element.Encoded != null ? element : null;
        }

        // ssl_collect_certificates: the system stores of these locations that are readable.
        private static IEnumerable<(Microsoft.Win32.RegistryKey Root, string Path)> SystemStoreKeys(string store_name)
        {
            // CERT_SYSTEM_STORE_LOCAL_MACHINE (its .Default and, for ROOT, .AuthRoot physical stores),
            // _LOCAL_MACHINE_ENTERPRISE, _LOCAL_MACHINE_GROUP_POLICY, CURRENT_USER, _CURRENT_USER_GROUP_POLICY
            var lm = Microsoft.Win32.Registry.LocalMachine;
            var cu = Microsoft.Win32.Registry.CurrentUser;
            yield return (lm, @"SOFTWARE\Microsoft\SystemCertificates\" + store_name);
            if (store_name.Equals("ROOT", StringComparison.OrdinalIgnoreCase))
                yield return (lm, @"SOFTWARE\Microsoft\SystemCertificates\AuthRoot");
            yield return (lm, @"SOFTWARE\Microsoft\EnterpriseCertificates\" + store_name);
            yield return (lm, @"SOFTWARE\Policies\Microsoft\SystemCertificates\" + store_name);
            yield return (cu, @"Software\Microsoft\SystemCertificates\" + store_name);
            yield return (cu, @"Software\Policies\Microsoft\SystemCertificates\" + store_name);
        }

        private static List<StoreElement> ssl_collect_certificates(string store_name, bool crls)
        {
            if (store_name.Length == 0)
            {
                // CertOpenStore refuses an empty name: E_INVALIDARG
                const int E_INVALIDARG = unchecked((int)0x80070057);
                string message = new System.ComponentModel.Win32Exception(E_INVALIDARG).Message.TrimEnd();
                if (message.EndsWith(".", StringComparison.Ordinal))
                    message = message.Substring(0, message.Length - 1);
                throw new PythonException(PyOSError.FromArguments(new PyObject[]
                    { PyInt.From(22), PyString.From(message), PyNone.Instance, PyInt.From(E_INVALIDARG) }, null));
            }
            var result = new List<StoreElement>();
            if (!OperatingSystem.IsWindows())
                return result;
            foreach (var (root, path) in SystemStoreKeys(store_name))
            {
                try
                {
                    using var key = root.OpenSubKey(path + (crls ? @"\CRLs" : @"\Certificates"));
                    if (key == null)
                        continue;
                    foreach (var name in key.GetSubKeyNames())
                    {
                        using var entry = key.OpenSubKey(name);
                        if (entry?.GetValue("Blob") is byte[] blob && ParseBlob(blob, crls ? 33 : 32) is { } element)
                            result.Add(element);
                    }
                }
                catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
                {
                }
            }
            // CURRENT_USER keeps its elements as files too (the user's roaming SystemCertificates).
            try
            {
                string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft", "SystemCertificates", store_name, crls ? "CRLs" : "Certificates");
                if (System.IO.Directory.Exists(dir))
                    foreach (var file in System.IO.Directory.GetFiles(dir))
                        if (ParseBlob(System.IO.File.ReadAllBytes(file), crls ? 33 : 32) is { } element)
                            result.Add(element);
            }
            catch (Exception error) when (error is UnauthorizedAccessException or System.IO.IOException)
            {
            }
            return result;
        }

        /// <summary>
        /// parseKeyUsage: the enhanced key usage OIDs of the store's property
        /// (CERT_FIND_PROP_ONLY_ENHKEY_USAGE_FLAG), else of the extension, or True when neither is there.
        /// </summary>
        private static PyObject parseKeyUsage(StoreElement element)
        {
            byte[] encoded = element.EnhancedKeyUsage;
            if (encoded == null)
            {
                try
                {
                    encoded = X509Cert.Parse(element.Encoded).Ext(X509Cert.OID_EXT_KEY_USAGE)?.Value;
                }
                catch (Asn1Exception)
                {
                    encoded = null;
                }
            }
            if (encoded == null)
                return PyBool.True;
            var set = new List<PyObject>();
            try
            {
                foreach (var oid in Asn1.Parse(encoded).Children())
                    set.Add(PyString.From(Asn1.OidText(oid.Content)));
            }
            catch (Asn1Exception)
            {
                return PyBool.True;
            }
            return new PyFrozenSet(set);
        }

        /// <summary>_ssl.enum_certificates(store_name): (bytes, encoding_type, trust) of each certificate.</summary>
        private static PyObject _ssl_enum_certificates_impl(string store_name)
        {
            var result = new PySet();
            foreach (var element in ssl_collect_certificates(store_name, false))
            {
                var tup = PyTuple.From(new PyObject[] { PyBytes.From(element.Encoded), certEncodingType(element.EncodingType), parseKeyUsage(element) });
                result.Add(tup);
            }
            return new PyList(new List<PyObject>(result.Items));
        }

        /// <summary>_ssl.enum_crls(store_name): (bytes, encoding_type) of each CRL.</summary>
        private static PyObject _ssl_enum_crls_impl(string store_name)
        {
            var result = new PySet();
            foreach (var element in ssl_collect_certificates(store_name, true))
                result.Add(PyTuple.From(new PyObject[] { PyBytes.From(element.Encoded), certEncodingType(element.EncodingType) }));
            return new PyList(new List<PyObject>(result.Items));
        }
    }
}
