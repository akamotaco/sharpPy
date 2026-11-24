using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace SharpPy.Modules.Ssl
{
    /// <summary>
    /// A BIO as SSL* uses it: Read gives bytes, 0 at end of file, or -1 to retry later
    /// (BIO_should_retry); Write takes bytes or -1 to retry. An I/O error sets Error (the WSA code).
    /// </summary>
    internal interface IBio
    {
        int Read(byte[] buffer, int offset, int count);
        int Write(byte[] buffer, int offset, int count);
        /// <summary>Whether a Read would not retry: bytes or the end are there (waiting for them on a blocking socket).</summary>
        bool Poll();
        int Error { get; set; }
    }

    /// <summary>
    /// SSL* over SslStream. SslStream runs the TLS state machine on an inner Stream (Pump); the pump
    /// takes the TLS records from the read BIO only when SslStream asks for them, one record at a
    /// time and no further (OpenSSL's read_ahead 0, so an unwrap leaves the clear text after
    /// close_notify on the socket), and writes what SslStream produces to the write BIO. An
    /// operation runs until it finishes or SslStream waits for bytes the BIO does not have, which
    /// is SSL_ERROR_WANT_READ, as with OpenSSL's non-blocking BIOs; the next call continues it.
    /// SslStream's continuations may run on pool threads; the driver (the Python thread) waits for
    /// one of those states, and Python callbacks (sni_callback) are run by the driver.
    /// </summary>
    internal sealed class SslConnection
    {
        // SSL_get_error
        internal const int SSL_ERROR_NONE = 0, SSL_ERROR_SSL = 1, SSL_ERROR_WANT_READ = 2, SSL_ERROR_WANT_WRITE = 3,
            SSL_ERROR_WANT_X509_LOOKUP = 4, SSL_ERROR_SYSCALL = 5, SSL_ERROR_ZERO_RETURN = 6, SSL_ERROR_WANT_CONNECT = 7;
        // SSL_get_shutdown
        internal const int SSL_SENT_SHUTDOWN = 1, SSL_RECEIVED_SHUTDOWN = 2;

        internal readonly bool Server;
        internal readonly PySSLSocket Owner;
        internal IBio Rbio, Wbio;
        private readonly Pump _pump;
        private SslStream _stream;
        private Task _handshake;
        private Task<int> _read;
        private readonly byte[] _readBuffer = new byte[16384];
        private byte[] _plain = Array.Empty<byte>();
        private int _plainOffset;
        private int _pendingWriteCount = -1;
        internal bool InitFinished;
        internal int Shutdown;
        internal long VerifyResult;
        internal SslErr LastError;
        internal bool SessionReused => false;

        // what the handshake found
        internal string Version;
        internal SslCipher Cipher;
        internal byte[] Alpn;
        internal X509Cert PeerCert;
        internal List<X509Cert> PeerChain;
        internal List<X509Cert> VerifiedChain;
        internal List<ushort> ClientCiphers;
        internal byte[] FinishedOwn, FinishedPeer;
        private bool _verifyFailed;
        private bool _alertSent;
        private int _pendingAlert = -1;

        /// <summary>ssl_x509err2alert: the alert a verification error is reported to the peer with.</summary>
        private static int ssl_x509err2alert(long x509err) => x509err switch
        {
            SslVerify.X509_V_ERR_CA_KEY_TOO_SMALL or SslVerify.X509_V_ERR_CA_MD_TOO_WEAK or SslVerify.X509_V_ERR_CERT_NOT_YET_VALID
                or SslVerify.X509_V_ERR_CRL_NOT_YET_VALID or SslVerify.X509_V_ERR_EE_KEY_TOO_SMALL
                or SslVerify.X509_V_ERR_ERROR_IN_CERT_NOT_AFTER_FIELD or SslVerify.X509_V_ERR_ERROR_IN_CERT_NOT_BEFORE_FIELD
                or SslVerify.X509_V_ERR_HOSTNAME_MISMATCH or SslVerify.X509_V_ERR_IP_ADDRESS_MISMATCH => 42,
            SslVerify.X509_V_ERR_CERT_HAS_EXPIRED or SslVerify.X509_V_ERR_CRL_HAS_EXPIRED => 45,
            SslVerify.X509_V_ERR_CERT_REVOKED => 44,
            SslVerify.X509_V_ERR_CERT_SIGNATURE_FAILURE or SslVerify.X509_V_ERR_CRL_SIGNATURE_FAILURE => 51,
            SslVerify.X509_V_ERR_DEPTH_ZERO_SELF_SIGNED_CERT or SslVerify.X509_V_ERR_INVALID_CA or SslVerify.X509_V_ERR_PATH_LENGTH_EXCEEDED
                or SslVerify.X509_V_ERR_SELF_SIGNED_CERT_IN_CHAIN or SslVerify.X509_V_ERR_UNABLE_TO_GET_CRL
                or SslVerify.X509_V_ERR_UNABLE_TO_GET_ISSUER_CERT or SslVerify.X509_V_ERR_UNABLE_TO_GET_ISSUER_CERT_LOCALLY
                or SslVerify.X509_V_ERR_UNABLE_TO_VERIFY_LEAF_SIGNATURE => 48,
            SslVerify.X509_V_ERR_INVALID_PURPOSE => 43,
            _ => 46,
        };
        // A fatal error was reported: the connection is dead, and later calls see SSL_ERROR_SYSCALL
        // with an empty error queue (EOF), as OpenSSL's do after its state machine failed.
        private bool _dead;

        private bool Dead(out int sslError)
        {
            sslError = SSL_ERROR_NONE;
            if (!_dead)
                return false;
            LastError = null;
            sslError = SSL_ERROR_SYSCALL;
            return true;
        }
        private bool _peerNoCertificate;

        internal SslConnection(PySSLSocket owner, bool server)
        {
            Owner = owner;
            Server = server;
            _pump = new Pump(this);
        }

        private PySSLContext Ctx => Owner.ctx;

        // ---- the inner stream ----

        /// <summary>The Stream SslStream reads its records from and writes its records to.</summary>
        private sealed class Pump : Stream
        {
            internal readonly object Gate = new();
            private readonly SslConnection _conn;
            private TaskCompletionSource<int> _pending;
            private Memory<byte> _pendingBuffer;
            internal readonly List<byte> Out = new();
            internal bool Eof;
            internal Action CallbackAction;   // a request for the driver (sni_callback)

            // the record layer as read: header bytes so far, body bytes left
            private readonly byte[] _header = new byte[5];
            private int _headerLength;
            private int _bodyLeft;
            private int _recordType;
            private readonly List<byte> _body = new();
            internal int RecordsSeen;
            internal bool Tls13 => _conn._protocolSeen is { } p ? p == SslProtocols.Tls13 : _conn._stream != null && _conn.StreamProtocolIs13();
            internal int Alert = -1;
            internal bool NotTls;
            internal byte[] FirstBytes = Array.Empty<byte>();

            internal Pump(SslConnection conn) => _conn = conn;
            internal static readonly bool Debug = Environment.GetEnvironmentVariable("SHARPPY_SSL_DEBUG") != null;

            public override bool CanRead => true;
            public override bool CanWrite => true;
            public override bool CanSeek => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();

            public override int Read(byte[] buffer, int offset, int count) =>
                ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                lock (Gate)
                {
                    if (Eof)
                        return new ValueTask<int>(0);
                    _pending = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _pendingBuffer = buffer;
                    if (Debug) Console.Error.WriteLine($"[{(_conn.Server ? "S" : "C")}] ReadAsync {buffer.Length}");
                    Monitor.PulseAll(Gate);
                    return new ValueTask<int>(_pending.Task);
                }
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                lock (Gate)
                {
                    Out.AddRange(new ArraySegment<byte>(buffer, offset, count));
                    Monitor.PulseAll(Gate);
                }
            }

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                Write(buffer, offset, count);
                return Task.CompletedTask;
            }

            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                lock (Gate)
                {
                    Out.AddRange(buffer.ToArray());
                    Monitor.PulseAll(Gate);
                }
                return ValueTask.CompletedTask;
            }

            internal bool ReadPending
            {
                get { lock (Gate) return _pending != null; }
            }

            /// <summary>How many bytes the read BIO may give now: what is left of the record being read.</summary>
            private int Limit(int wanted)
            {
                if (NotTls)
                    return wanted;
                if (_headerLength < 5)
                    return Math.Min(wanted, 5 - _headerLength);
                return Math.Min(wanted, _bodyLeft);
            }

            private void Account(ReadOnlySpan<byte> data)
            {
                if (FirstBytes.Length < 8)
                    FirstBytes = FirstBytes.Concat(data.Slice(0, Math.Min(data.Length, 8 - FirstBytes.Length)).ToArray()).ToArray();
                int i = 0;
                while (i < data.Length && !NotTls)
                {
                    if (_headerLength < 5)
                    {
                        _header[_headerLength++] = data[i++];
                        if (_headerLength == 5)
                        {
                            _recordType = _header[0];
                            _bodyLeft = (_header[3] << 8) | _header[4];
                            _body.Clear();
                            if (_recordType < 20 || _recordType > 24 || _header[1] != 3)
                            {
                                NotTls = true;
                                return;
                            }
                            if (_bodyLeft == 0)
                                RecordDone();
                        }
                        continue;
                    }
                    int n = Math.Min(_bodyLeft, data.Length - i);
                    if (_recordType == 21 || (_recordType == 22 && RecordsSeen == 0))
                        _body.AddRange(data.Slice(i, n).ToArray());
                    i += n;
                    _bodyLeft -= n;
                    if (_bodyLeft == 0)
                        RecordDone();
                }
            }

            private void RecordDone()
            {
                if (_recordType == 21 && _body.Count == 2)
                    Alert = _body[1];
                if (_recordType == 22 && RecordsSeen == 0 && _conn.Server)
                    _conn.ParseClientHello(_body.ToArray());
                RecordsSeen++;
                _headerLength = 0;
                _body.Clear();
            }

            /// <summary>
            /// The read BIO's bytes for the waiting SslStream read: a count, 0 for its end, or -1
            /// when it has none now.
            /// </summary>
            internal int Fill()
            {
                Memory<byte> buffer;
                lock (Gate)
                {
                    if (_pending == null)
                        return 1;
                    buffer = _pendingBuffer;
                }
                if (buffer.Length == 0)
                {
                    // A zero-byte read: SslStream waits for input without a buffer; it completes
                    // with 0 (not the end of the stream) once the BIO has something.
                    if (!_conn.Rbio.Poll())
                        return -1;
                    TaskCompletionSource<int> waiting;
                    lock (Gate)
                    {
                        waiting = _pending;
                        _pending = null;
                    }
                    waiting.SetResult(0);
                    return 1;
                }
                int want = Limit(buffer.Length);
                var temp = new byte[Math.Max(want, 1)];
                int n = _conn.Rbio.Read(temp, 0, want);
                if (Debug) Console.Error.WriteLine($"[{(_conn.Server ? "S" : "C")}] Fill want {want} got {n}");
                if (n < 0)
                    return n;
                TaskCompletionSource<int> pending;
                lock (Gate)
                {
                    if (n == 0)
                        Eof = true;
                    else
                    {
                        temp.AsSpan(0, n).CopyTo(buffer.Span);
                        Account(temp.AsSpan(0, n));
                    }
                    pending = _pending;
                    _pending = null;
                }
                pending.SetResult(n);
                return n == 0 ? 0 : n;
            }

            internal byte[] TakeOutput()
            {
                lock (Gate)
                {
                    var data = Out.ToArray();
                    Out.Clear();
                    return data;
                }
            }

            internal void PutBack(byte[] data, int offset)
            {
                lock (Gate)
                    Out.InsertRange(0, new ArraySegment<byte>(data, offset, data.Length - offset));
            }

            internal int OutputLength
            {
                get { lock (Gate) return Out.Count; }
            }

            /// <summary>
            /// Wait until the task is done, or SslStream waits for input, or a callback needs the
            /// driver (run here, on the driver's thread).
            /// </summary>
            internal void Settle(Task task)
            {
                task.ContinueWith(_ => { lock (Gate) Monitor.PulseAll(Gate); }, TaskContinuationOptions.ExecuteSynchronously);
                while (true)
                {
                    Action callback = null;
                    lock (Gate)
                    {
                        while (!task.IsCompleted && _pending == null && CallbackAction == null)
                            Monitor.Wait(Gate, 50);
                        if (CallbackAction != null)
                        {
                            callback = CallbackAction;
                            CallbackAction = null;
                        }
                        else
                            return;
                    }
                    callback();
                }
            }

            internal void Request(Action action)
            {
                lock (Gate)
                {
                    CallbackAction = action;
                    Monitor.PulseAll(Gate);
                }
            }
        }

        private SslProtocols? _protocolSeen;

        /// <summary>The protocol SChannel negotiated, while the verify callback runs (SslStream knows it by then).</summary>
        private void NoteProtocol(SslStream stream)
        {
            try
            {
                _protocolSeen = stream?.SslProtocol;
            }
            catch (Exception)
            {
            }
        }

        internal bool StreamProtocolIs13()
        {
            // The negotiated protocol is readable once SChannel has the ServerHello; before the
            // handshake completed SslStream refuses, so the configured range decides.
            try
            {
                return _stream.SslProtocol == SslProtocols.Tls13;
            }
            catch (Exception)
            {
                var (min, max) = Ctx.EffectiveVersions();
                return max >= SslModule.PY_PROTO_TLSv1_3 && (Ctx.Options & SslModule.SSL_OP_NO_TLSv1_3) == 0;
            }
        }

        internal List<byte[]> ClientAlpn;
        internal bool ClientOfferedPha;
        internal bool ClientOffers13;
        private bool _deferredClientCert;

        /// <summary>
        /// What the server reads from the ClientHello: the cipher suites (SSL_get_client_ciphers),
        /// the ALPN list and the post_handshake_auth extension.
        /// </summary>
        internal void ParseClientHello(byte[] msg)
        {
            try
            {
                if (msg.Length < 4 || msg[0] != 1)
                    return;
                int p = 4 + 2 + 32;
                int sid = msg[p];
                p += 1 + sid;
                int n = (msg[p] << 8) | msg[p + 1];
                p += 2;
                var list = new List<ushort>();
                for (int i = 0; i + 1 < n; i += 2)
                    list.Add((ushort)((msg[p + i] << 8) | msg[p + i + 1]));
                ClientCiphers = list;
                p += n;
                p += 1 + msg[p]; // compression methods
                if (p + 2 > msg.Length)
                    return;
                int end = p + 2 + ((msg[p] << 8) | msg[p + 1]);
                p += 2;
                while (p + 4 <= end)
                {
                    int type = (msg[p] << 8) | msg[p + 1];
                    int len = (msg[p + 2] << 8) | msg[p + 3];
                    p += 4;
                    if (type == 16 && len >= 2)
                    {
                        var alpn = new List<byte[]>();
                        int q = p + 2, stop = p + len;
                        while (q < stop)
                        {
                            int l = msg[q++];
                            alpn.Add(msg.AsSpan(q, l).ToArray());
                            q += l;
                        }
                        ClientAlpn = alpn;
                    }
                    else if (type == 49)
                        ClientOfferedPha = true;
                    else if (type == 43 && len >= 1)
                    {
                        for (int q = p + 1; q + 1 < p + len; q += 2)
                            if (msg[q] == 3 && msg[q + 1] == 4)
                                ClientOffers13 = true;
                    }
                    p += len;
                }
            }
            catch (Exception error) when (error is IndexOutOfRangeException or ArgumentOutOfRangeException)
            {
            }
        }

        // ---- driving ----

        /// <summary>
        /// Run the operation until it completes (SSL_ERROR_NONE) or needs I/O the BIOs cannot do now
        /// (SSL_ERROR_WANT_READ / WANT_WRITE), or a BIO fails (SSL_ERROR_SYSCALL).
        /// </summary>
        private int Drive(Task task)
        {
            while (true)
            {
                _pump.Settle(task);
                if (task.IsFaulted && (_verifyFailed || _peerNoCertificate || _pendingAlert >= 0) && !_alertSent)
                {
                    // OpenSSL fails the handshake the moment verification fails and sends its alert
                    // (ssl_x509err2alert) instead of the rest of its flight; SslStream decides after
                    // its flight is out, so that flight is dropped and the alert goes in its place.
                    _alertSent = true;
                    _pump.TakeOutput();
                    int alert = _pendingAlert >= 0 ? _pendingAlert
                        : _peerNoCertificate ? (_pump.Tls13 ? 116 : 40) : ssl_x509err2alert(VerifyResult);
                    _pump.Write(new byte[] { 21, 3, 3, 0, 2, 2, (byte)alert }, 0, 7);
                }
                int flushed = FlushOutput();
                if (flushed != SSL_ERROR_NONE)
                    return flushed;
                if (task.IsCompleted)
                    return SSL_ERROR_NONE;
                int n = _pump.Fill();
                if (n < 0)
                    return Rbio.Error != 0 ? SSL_ERROR_SYSCALL : SSL_ERROR_WANT_READ;
            }
        }

        /// <summary>What SslStream wrote, to the write BIO.</summary>
        private int FlushOutput()
        {
            var data = _pump.TakeOutput();
            int offset = 0;
            while (offset < data.Length)
            {
                int n = Wbio.Write(data, offset, data.Length - offset);
                if (n < 0)
                {
                    _pump.PutBack(data, offset);
                    return Wbio.Error != 0 ? SSL_ERROR_SYSCALL : SSL_ERROR_WANT_WRITE;
                }
                offset += n;
            }
            return SSL_ERROR_NONE;
        }

        // ---- the handshake ----

        private static SslProtocols Protocols(PySSLContext ctx, out bool none)
        {
            var (min, max) = ctx.EffectiveVersions();
            var result = SslProtocols.None;
#pragma warning disable SYSLIB0039 // TLS 1.0 and 1.1 are what the context asks for
            void Add(int version, ulong noOption, SslProtocols flag)
            {
                if (version >= min && version <= max && (ctx.Options & noOption) == 0)
                    result |= flag;
            }
            Add(SslModule.PY_PROTO_TLSv1, SslModule.SSL_OP_NO_TLSv1, SslProtocols.Tls);
            Add(SslModule.PY_PROTO_TLSv1_1, SslModule.SSL_OP_NO_TLSv1_1, SslProtocols.Tls11);
#pragma warning restore SYSLIB0039
            Add(SslModule.PY_PROTO_TLSv1_2, SslModule.SSL_OP_NO_TLSv1_2, SslProtocols.Tls12);
            Add(SslModule.PY_PROTO_TLSv1_3, SslModule.SSL_OP_NO_TLSv1_3, SslProtocols.Tls13);
            none = result == SslProtocols.None;
            return result;
        }

        private List<SslApplicationProtocol> AlpnList(PySSLContext ctx)
        {
            if (ctx.AlpnProtocols == null || ctx.AlpnProtocols.Length == 0)
                return null;
            var list = new List<SslApplicationProtocol>();
            var p = ctx.AlpnProtocols;
            for (int i = 0; i < p.Length;)
            {
                int n = p[i++];
                if (i + n > p.Length)
                    break;
                list.Add(new SslApplicationProtocol(p.AsSpan(i, n).ToArray()));
                i += n;
            }
            return list.Count > 0 ? list : null;
        }

        /// <summary>
        /// _selectALPN_cb / do_protocol_selection: the first of the server's protocols the client
        /// offered (SSL_select_next_proto); none acknowledges no ALPN, the handshake goes on.
        /// </summary>
        private List<SslApplicationProtocol> SelectAlpn(PySSLContext ctx)
        {
            var server = AlpnList(ctx);
            if (server == null || ClientAlpn == null)
                return null;
            foreach (var protocol in server)
                foreach (var offered in ClientAlpn)
                    if (protocol.Protocol.Span.SequenceEqual(offered))
                        return new List<SslApplicationProtocol> { protocol };
            return null;
        }

        private CipherSuitesPolicy CipherPolicy(PySSLContext ctx)
        {
            // SChannel takes no per-connection suite list (CipherSuitesPolicy is Linux/macOS only).
            if (OperatingSystem.IsWindows() || ctx.Ciphers == null)
                return null;
            try
            {
                return new CipherSuitesPolicy(ctx.Ciphers.Select(c => (TlsCipherSuite)c.Iana));
            }
            catch (PlatformNotSupportedException)
            {
                return null;
            }
        }

        /// <summary>The verify callback: X509_verify_cert under the context's verify mode.</summary>
        private bool Verify(X509Certificate certificate, X509Chain chain)
        {
            var ctx = Ctx;
            var leafDer = certificate?.GetRawCertData();
            var untrusted = new List<X509Cert>();
            try
            {
                if (leafDer != null)
                    PeerCert = X509Cert.Parse(leafDer);
                if (chain != null)
                {
                    foreach (var extra in chain.ChainPolicy.ExtraStore)
                        untrusted.Add(X509Cert.Parse(extra.RawData));
                }
            }
            catch (Asn1Exception)
            {
                PeerCert ??= null;
            }
            PeerChain = new List<X509Cert>();
            if (PeerCert != null)
                PeerChain.Add(PeerCert);
            PeerChain.AddRange(untrusted.Where(c => PeerCert == null || !X509Cert.SameCert(c, PeerCert)));
            if (PeerCert == null && ctx.VerifyMode == SslModule.PY_SSL_CERT_NONE)
                return true;
            if (PeerCert == null)
            {
                if (Server && ctx.VerifyMode == SslModule.PY_SSL_CERT_REQUIRED && !_deferredClientCert)
                {
                    _peerNoCertificate = true;
                    return false;
                }
                // a client always gets a certificate from a server (anonymous suites are not offered)
                return Server;
            }
            var p = new SslVerify.Params
            {
                Store = ctx.Store,
                Flags = ctx.VerifyFlags,
                SecurityLevel = ctx.SecurityLevel,
                ServerPurpose = !Server,
                HostFlags = ctx.HostFlags,
            };
            if (!Server && ctx.CheckHostname && Owner.server_hostname is PyString host)
            {
                var ip = SslVerify.a2i_IPADDRESS(host.Value);
                if (ip != null)
                    p.Ip = ip;
                else
                    p.Host = host.Value;
            }
            int result = SslVerify.X509_verify_cert(PeerCert, PeerChain.Skip(1).ToList(), p, out var built);
            VerifyResult = result;
            // SSL_VERIFY_NONE still verifies (SSL_get_verify_result, the verified chain) and goes on.
            VerifiedChain = built;
            if (result == SslVerify.X509_V_OK || ctx.VerifyMode == SslModule.PY_SSL_CERT_NONE)
                return true;
            _verifyFailed = true;
            return false;
        }

        /// <summary>
        /// The certificate and the chain sent with it: the extra certificates of the chain file, or
        /// (none there) the chain the context's trust store builds for it, as OpenSSL's automatic
        /// chain building (ssl_add_cert_chain) sends it.
        /// </summary>
        private static SslStreamCertificateContext OwnCertificateContext(PySSLContext ctx)
        {
            var extra = new X509Certificate2Collection(ctx.ChainCertificates.ToArray());
            if (extra.Count == 0 && ctx.CertificateX509 != null)
            {
                var p = new SslVerify.Params { Store = ctx.Store, Flags = ctx.VerifyFlags, SecurityLevel = 0, ServerPurpose = true };
                SslVerify.X509_verify_cert(ctx.CertificateX509, new List<X509Cert>(), p, out var chain);
                foreach (var c in chain.Skip(1))
                    extra.Add(new X509Certificate2(c.Der));
            }
            return SslStreamCertificateContext.Create(ctx.Certificate, extra, offline: true);
        }

        private SslServerAuthenticationOptions ServerOptions(PySSLContext ctx)
        {
            // newPySSLSocket sets SSL_VERIFY_POST_HANDSHAKE on a server with post_handshake_auth:
            // OpenSSL then sends no CertificateRequest in a TLS 1.3 handshake.
            _deferredClientCert = ctx.PostHandshakeAuth && ctx.VerifyMode != SslModule.PY_SSL_CERT_NONE && ClientOffers13
                && ctx.EffectiveVersions().Max >= SslModule.PY_PROTO_TLSv1_3 && (ctx.Options & SslModule.SSL_OP_NO_TLSv1_3) == 0;
            var protocols = Protocols(ctx, out _);
            var options = new SslServerAuthenticationOptions
            {
                EnabledSslProtocols = protocols,
                ClientCertificateRequired = ctx.VerifyMode != SslModule.PY_SSL_CERT_NONE && !_deferredClientCert,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => { NoteProtocol(sender as SslStream); return Verify(certificate, chain); },
                ApplicationProtocols = SelectAlpn(ctx),
                AllowRenegotiation = false,
                EncryptionPolicy = EncryptionPolicy.RequireEncryption,
            };
            if (ctx.Certificate != null)
                options.ServerCertificateContext = OwnCertificateContext(ctx);
            if (CipherPolicy(ctx) is { } policy)
                options.CipherSuitesPolicy = policy;
            return options;
        }

        private Task StartHandshake()
        {
            var ctx = Ctx;
            _stream = new SslStream(_pump, leaveInnerStreamOpen: true);
            Protocols(ctx, out bool none);
            if (none)
            {
                LastError = SslErr.Of(SslErr.LIB_SSL, "NO_PROTOCOLS_AVAILABLE");
                return Task.FromException(new AuthenticationException("no protocols available"));
            }
            if (Server)
            {
                ctx.Stats.accept++;
                return _stream.AuthenticateAsServerAsync((stream, hello, state, token) =>
                {
                    // _servername_callback, run by the driver
                    var tcs = new TaskCompletionSource<SslServerAuthenticationOptions>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _pump.Request(() =>
                    {
                        try
                        {
                            Owner.ServerNameCallback(string.IsNullOrEmpty(hello.ServerName) ? null : hello.ServerName);
                            var current = Ctx;
                            if (current.Certificate == null)
                            {
                                LastError = SslErr.Of(SslErr.LIB_SSL, "NO_CERTIFICATE_ASSIGNED");
                                tcs.SetException(new AuthenticationException("no certificate assigned"));
                                return;
                            }
                            tcs.SetResult(ServerOptions(current));
                        }
                        catch (SniAlert alert)
                        {
                            // SSL_TLSEXT_ERR_ALERT_FATAL: the server fails with "callback failed" and
                            // sends the alert the callback chose.
                            LastError = SslErr.Of(SslErr.LIB_SSL, "CALLBACK_FAILED");
                            LastError.Text = "callback failed";
                            LastError.Reason = 234;
                            _pendingAlert = alert.Description;
                            tcs.SetException(new AuthenticationException("sni callback failed"));
                        }
                        catch (Exception error)
                        {
                            tcs.SetException(error);
                        }
                    });
                    return new ValueTask<SslServerAuthenticationOptions>(tcs.Task);
                }, null, CancellationToken.None);
            }
            ctx.Stats.connect++;
            var options = new SslClientAuthenticationOptions
            {
                TargetHost = Owner.server_hostname is PyString h && SslVerify.a2i_IPADDRESS(h.Value) == null ? h.Value : "",
                EnabledSslProtocols = ctx.Protocol == SslModule.PY_SSL_VERSION_TLS_CLIENT || true ? Protocols(ctx, out _) : SslProtocols.None,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => { NoteProtocol(sender as SslStream); return Verify(certificate, chain); },

                ApplicationProtocols = AlpnList(ctx),
                AllowRenegotiation = (ctx.Options & SslModule.SSL_OP_NO_RENEGOTIATION) == 0,
                EncryptionPolicy = EncryptionPolicy.RequireEncryption,
            };
            if (ctx.Certificate != null)
                options.ClientCertificateContext = OwnCertificateContext(ctx);
            if (CipherPolicy(ctx) is { } policy)
                options.CipherSuitesPolicy = policy;
            return _stream.AuthenticateAsClientAsync(options);
        }

        /// <summary>The SslErr of a failed handshake or record operation.</summary>
        private SslErr ErrorOf(Exception error)
        {
            if (LastError != null)
                return LastError;
            if (_verifyFailed)
                return SslErr.Of(SslErr.LIB_SSL, "CERTIFICATE_VERIFY_FAILED");
            if (_peerNoCertificate)
                return SslErr.Of(SslErr.LIB_SSL, Version == "TLSv1.3" ? "PEER_DID_NOT_RETURN_A_CERTIFICATE" : "PEER_DID_NOT_RETURN_A_CERTIFICATE");
            if (_pump.NotTls)
            {
                var first = System.Text.Encoding.ASCII.GetString(_pump.FirstBytes);
                if (Server && (first.StartsWith("GET ") || first.StartsWith("POST ") || first.StartsWith("HEAD ") || first.StartsWith("PUT ")))
                    return SslErr.Of(SslErr.LIB_SSL, "HTTP_REQUEST");
                return SslErr.Of(SslErr.LIB_SSL, "WRONG_VERSION_NUMBER");
            }
            if (_pump.Alert > 0)
                return SslErr.Alert(_pump.Alert);
            if (_pump.Eof)
                return SslErr.Of(SslErr.LIB_SSL, "UNEXPECTED_EOF_WHILE_READING");
            int code = 0;
            for (var e = error; e != null; e = e.InnerException)
                if (e is System.ComponentModel.Win32Exception w)
                    code = w.NativeErrorCode;
            return (uint)code switch
            {
                0x80090331 => SslErr.Of(SslErr.LIB_SSL, "NO_SHARED_CIPHER"),          // SEC_E_ALGORITHM_MISMATCH
                0x80090302 => SslErr.Of(SslErr.LIB_SSL, "UNSUPPORTED_PROTOCOL"),      // SEC_E_UNSUPPORTED_FUNCTION
                0x80090327 => SslErr.Alert(46),                                         // SEC_E_CERT_UNKNOWN
                0x80090349 => SslErr.Alert(42),                                         // SEC_E_CERT_WRONG_USAGE
                0x80090328 => SslErr.Alert(45),                                         // SEC_E_CERT_EXPIRED
                0x80090325 => SslErr.Alert(48),                                         // SEC_E_UNTRUSTED_ROOT
                0x80090367 => SslErr.Alert(120),                                        // SEC_E_APPLICATION_PROTOCOL_MISMATCH
                0x80090326 => SslErr.Alert(40),                                         // SEC_E_ILLEGAL_MESSAGE
                _ => SslErr.Of(SslErr.LIB_SSL, "SSL_HANDSHAKE_FAILURE"),
            };
        }

        /// <summary>SSL_do_handshake: 1, or ≤ 0 with the SSL_get_error code.</summary>
        internal int DoHandshake(out int sslError)
        {
            sslError = SSL_ERROR_NONE;
            if (InitFinished)
                return 1;
            if (Dead(out sslError))
                return 0;
            if (Shutdown != 0 && _handshake == null)
            {
                LastError = SslErr.Of(SslErr.LIB_SSL, "PROTOCOL_IS_SHUTDOWN");
                sslError = SSL_ERROR_SSL;
                return -1;
            }
            _handshake ??= StartHandshake();
            int r = Drive(_handshake);
            if (r != SSL_ERROR_NONE)
            {
                sslError = r;
                return -1;
            }
            if (_handshake.IsFaulted || _handshake.IsCanceled)
            {
                LastError = ErrorOf(_handshake.Exception);
                _dead = true;
                sslError = SSL_ERROR_SSL;
                return -1;
            }
            Finish();
            return 1;
        }

        private void Finish()
        {
            InitFinished = true;
            var ctx = Ctx;
            if (Server) ctx.Stats.accept_good++;
            else ctx.Stats.connect_good++;
#pragma warning disable SYSLIB0039
            Version = _stream.SslProtocol switch
            {
                SslProtocols.Tls13 => "TLSv1.3",
                SslProtocols.Tls12 => "TLSv1.2",
                SslProtocols.Tls11 => "TLSv1.1",
                SslProtocols.Tls => "TLSv1",
                _ => null,
            };
#pragma warning restore SYSLIB0039
            try
            {
                Cipher = SslCiphers.ByIana((int)_stream.NegotiatedCipherSuite);
            }
            catch (Exception error) when (error is PlatformNotSupportedException or InvalidOperationException)
            {
                Cipher = null;
            }
            var alpn = _stream.NegotiatedApplicationProtocol.Protocol;
            Alpn = alpn.Length > 0 ? alpn.ToArray() : null;
            if (PeerCert == null && _stream.RemoteCertificate is { } remote)
            {
                try
                {
                    PeerCert = X509Cert.Parse(remote.GetRawCertData());
                    PeerChain = new List<X509Cert> { PeerCert };
                }
                catch (Asn1Exception)
                {
                }
            }
            try
            {
                var binding = _stream.TransportContext?.GetChannelBinding(System.Security.Authentication.ExtendedProtection.ChannelBindingKind.Unique);
                if (binding != null && !binding.IsInvalid && binding.Size > 0)
                {
                    var raw = new byte[binding.Size];
                    System.Runtime.InteropServices.Marshal.Copy(binding.DangerousGetHandle(), raw, 0, raw.Length);
                    // SEC_CHANNEL_BINDINGS (Windows) or the bare "tls-unique:" prefixed data
                    FinishedPeer = ChannelBindingData(raw);
                }
            }
            catch (Exception error) when (error is NotSupportedException or PlatformNotSupportedException or InvalidOperationException)
            {
            }
        }

        private static byte[] ChannelBindingData(byte[] raw)
        {
            const string prefix = "tls-unique:";
            if (raw.Length >= 32 && OperatingSystem.IsWindows())
            {
                int length = BitConverter.ToInt32(raw, 24);
                int offset = BitConverter.ToInt32(raw, 28);
                if (offset >= 0 && length >= 0 && offset + length <= raw.Length)
                    raw = raw.AsSpan(offset, length).ToArray();
            }
            var text = System.Text.Encoding.ASCII.GetString(raw, 0, Math.Min(raw.Length, prefix.Length));
            if (text == prefix)
                raw = raw.AsSpan(prefix.Length).ToArray();
            return raw;
        }

        // ---- data ----

        /// <summary>SSL_pending</summary>
        internal int Pending => _plain.Length - _plainOffset;

        /// <summary>SSL_read_ex: 1 with count, or 0 with the SSL_get_error code.</summary>
        internal int ReadEx(byte[] buffer, int offset, int len, out int count, out int sslError)
        {
            count = 0;
            sslError = SSL_ERROR_NONE;
            if (Pending == 0 && Dead(out sslError))
                return 0;
            if (!InitFinished)
            {
                if (DoHandshake(out sslError) <= 0)
                    return 0;
            }
            while (Pending == 0)
            {
                if ((Shutdown & SSL_RECEIVED_SHUTDOWN) != 0)
                {
                    sslError = SSL_ERROR_ZERO_RETURN;
                    return 0;
                }
                int r = ReadRecord(out sslError);
                if (r <= 0)
                    return 0;
            }
            count = Math.Min(len, Pending);
            Array.Copy(_plain, _plainOffset, buffer, offset, count);
            _plainOffset += count;
            return 1;
        }

        /// <summary>One SslStream read: 1 with clear text buffered, else 0 with the error.</summary>
        private int ReadRecord(out int sslError)
        {
            sslError = SSL_ERROR_NONE;
            _read ??= _stream.ReadAsync(_readBuffer, 0, _readBuffer.Length);
            int r = Drive(_read);
            if (r != SSL_ERROR_NONE)
            {
                sslError = r;
                return 0;
            }
            var task = _read;
            _read = null;
            if (task.IsFaulted || task.IsCanceled)
            {
                LastError = ErrorOf(task.Exception);
                _dead = true;
                sslError = SSL_ERROR_SSL;
                return 0;
            }
            int n = task.Result;
            if (n == 0)
            {
                if (_pump.Eof && (Ctx.Options & SslModule.SSL_OP_IGNORE_UNEXPECTED_EOF) == 0)
                {
                    LastError = SslErr.Of(SslErr.LIB_SSL, "UNEXPECTED_EOF_WHILE_READING");
                    sslError = SSL_ERROR_SSL;
                    return 0;
                }
                Shutdown |= SSL_RECEIVED_SHUTDOWN;
                sslError = SSL_ERROR_ZERO_RETURN;
                return 0;
            }
            if ((Shutdown & SSL_SENT_SHUTDOWN) != 0)
            {
                // data after our close_notify is discarded while the peer's is awaited
                return 1;
            }
            _plain = _readBuffer.AsSpan(0, n).ToArray();
            _plainOffset = 0;
            return 1;
        }

        /// <summary>SSL_write_ex: 1 with count, or 0 with the SSL_get_error code.</summary>
        internal int WriteEx(byte[] data, out int count, out int sslError)
        {
            count = 0;
            sslError = SSL_ERROR_NONE;
            if (Dead(out sslError))
                return 0;
            if ((Shutdown & SSL_SENT_SHUTDOWN) != 0)
            {
                LastError = SslErr.Of(SslErr.LIB_SSL, "PROTOCOL_IS_SHUTDOWN");
                LastError.Reason = 207;
                LastError.Text = "protocol is shutdown";
                sslError = SSL_ERROR_SSL;
                return 0;
            }
            if (!InitFinished)
            {
                if (DoHandshake(out sslError) <= 0)
                    return 0;
            }
            if (_pendingWriteCount >= 0)
            {
                // SSL_MODE_ACCEPT_MOVING_WRITE_BUFFER: the retry of a write whose records are out already
                int f = FlushOutput();
                if (f != SSL_ERROR_NONE)
                {
                    sslError = f;
                    return 0;
                }
                count = _pendingWriteCount;
                _pendingWriteCount = -1;
                return 1;
            }
            if (data.Length == 0)
                return 1;
            var task = _stream.WriteAsync(data, 0, data.Length);
            int r = Drive(task);
            if (r != SSL_ERROR_NONE)
            {
                if (r == SSL_ERROR_WANT_WRITE && task.IsCompleted)
                    _pendingWriteCount = data.Length;
                sslError = r;
                return 0;
            }
            if (task.IsFaulted || task.IsCanceled)
            {
                LastError = ErrorOf(task.Exception);
                sslError = SSL_ERROR_SSL;
                return 0;
            }
            count = data.Length;
            return 1;
        }

        /// <summary>SSL_shutdown: 1 when both close_notify alerts are through, 0 when ours is sent, -1 on error.</summary>
        internal int DoShutdown(out int sslError)
        {
            sslError = SSL_ERROR_NONE;
            if (!InitFinished)
            {
                LastError = SslErr.Of(SslErr.LIB_SSL, "SHUTDOWN_WHILE_IN_INIT");
                sslError = SSL_ERROR_SSL;
                return -1;
            }
            if ((Shutdown & SSL_SENT_SHUTDOWN) == 0)
            {
                var task = _stream.ShutdownAsync();
                int r = Drive(task);
                Shutdown |= SSL_SENT_SHUTDOWN;
                if (r != SSL_ERROR_NONE)
                {
                    sslError = r;
                    return -1;
                }
                return (Shutdown & SSL_RECEIVED_SHUTDOWN) != 0 ? 1 : 0;
            }
            if (_pump.OutputLength > 0)
            {
                int f = FlushOutput();
                if (f != SSL_ERROR_NONE)
                {
                    sslError = f;
                    return -1;
                }
            }
            if ((Shutdown & SSL_RECEIVED_SHUTDOWN) != 0)
                return 1;
            // the peer's close_notify
            while (true)
            {
                int ok = ReadRecord(out sslError);
                if (ok > 0)
                    continue;
                if (sslError == SSL_ERROR_ZERO_RETURN)
                {
                    sslError = SSL_ERROR_NONE;
                    return 1;
                }
                return -1;
            }
        }

        /// <summary>_servername_callback's SSL_TLSEXT_ERR_ALERT_FATAL with the alert (*al) to send.</summary>
        internal sealed class SniAlert : Exception
        {
            internal readonly int Description;
            internal SniAlert(int description) => Description = description;
        }
    }

    /// <summary>The memory BIO pair of _wrap_bio: BIO_s_mem with mem_eof_return -1 until write_eof.</summary>
    internal sealed class MemoryBioAdapter : IBio
    {
        private readonly PySSLMemoryBIO _bio;
        internal MemoryBioAdapter(PySSLMemoryBIO bio) => _bio = bio;
        public int Error { get; set; }

        public bool Poll() => _bio.Pending > 0 || _bio.eof_written;

        public int Read(byte[] buffer, int offset, int count)
        {
            int n = _bio.ReadInto(buffer, offset, count);
            if (n > 0)
                return n;
            return _bio.eof_written ? 0 : -1;
        }

        public int Write(byte[] buffer, int offset, int count)
        {
            _bio.Append(buffer, offset, count);
            return count;
        }
    }

    /// <summary>
    /// The socket BIO of _wrap_socket (BIO_s_socket over the fd): recv/send on the socket,
    /// blocking unless the socket has a timeout or is non-blocking (BIO_set_nbio).
    /// </summary>
    internal sealed class SocketBio : IBio
    {
        private readonly PySSLSocket _owner;
        internal bool NonBlocking;
        public int Error { get; set; }

        internal SocketBio(PySSLSocket owner) => _owner = owner;

        private Socket Sock => (_owner.GetSocket(out _) as PySocketObject)?.Sock;

        public bool Poll()
        {
            var sock = Sock;
            if (sock == null)
                return true;
            bool ready = false;
            try
            {
                if (NonBlocking)
                    ready = sock.Poll(0, SelectMode.SelectRead);
                else
                    PyGil.AllowThreads(() => ready = sock.Poll(-1, SelectMode.SelectRead));
            }
            catch (Exception error) when (error is SocketException or ObjectDisposedException)
            {
                ready = true;
            }
            return ready;
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            var sock = Sock;
            if (sock == null)
            {
                Error = SocketModule.WSAENOTSOCK;
                return -1;
            }
            int n = 0;
            SocketError code = SocketError.Success;
            void Receive() => n = sock.Receive(buffer, offset, count, SocketFlags.None, out code);
            try
            {
                if (NonBlocking)
                    Receive();
                else
                    PyGil.AllowThreads(Receive);
            }
            catch (ObjectDisposedException)
            {
                Error = SocketModule.WSAENOTSOCK;
                return -1;
            }
            if (code == SocketError.WouldBlock)
                return -1;
            if (code != SocketError.Success)
            {
                Error = OperatingSystem.IsWindows() ? (int)code : (int)code;
                return -1;
            }
            return n;
        }

        public int Write(byte[] buffer, int offset, int count)
        {
            var sock = Sock;
            if (sock == null)
            {
                Error = SocketModule.WSAENOTSOCK;
                return -1;
            }
            int n = 0;
            SocketError code = SocketError.Success;
            void Send() => n = sock.Send(buffer, offset, count, SocketFlags.None, out code);
            try
            {
                if (NonBlocking)
                    Send();
                else
                    PyGil.AllowThreads(Send);
            }
            catch (ObjectDisposedException)
            {
                Error = SocketModule.WSAENOTSOCK;
                return -1;
            }
            if (code == SocketError.WouldBlock)
                return n > 0 ? n : -1;
            if (code != SocketError.Success)
            {
                Error = (int)code;
                return -1;
            }
            return n;
        }
    }
}
