using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace SharpPy.Modules.Ssl
{
    /// <summary>
    /// X509_STORE with its file and hashed-directory lookups (X509_LOOKUP_file, X509_LOOKUP_hash_dir):
    /// the certificates and CRLs a context trusts. A capath directory is read only when a lookup asks
    /// for a subject hash, and what it finds stays in the store, as by_dir does.
    /// </summary>
    internal sealed class SslStore
    {
        internal const string CertFileEnv = "SSL_CERT_FILE";
        internal const string CertDirEnv = "SSL_CERT_DIR";
        internal static readonly string DefaultCertFile = OperatingSystem.IsWindows()
            ? @"C:\Program Files\Common Files\ssl\cert.pem" : "/usr/local/ssl/cert.pem";
        internal static readonly string DefaultCertDir = OperatingSystem.IsWindows()
            ? @"C:\Program Files\Common Files\ssl\certs" : "/usr/local/ssl/certs";

        internal readonly List<X509Cert> Certs = new();
        internal readonly List<X509Crl> Crls = new();
        internal readonly List<string> Dirs = new();
        private readonly HashSet<string> _dirLoaded = new();

        /// <summary>X509_STORE_add_cert: a certificate already there is not added again.</summary>
        internal void Add(X509Cert cert)
        {
            lock (this)
            {
                if (!Certs.Any(c => X509Cert.SameCert(c, cert)))
                    Certs.Add(cert);
            }
        }

        internal void AddCrl(X509Crl crl)
        {
            lock (this)
            {
                if (!Crls.Any(c => c.Der.AsSpan().SequenceEqual(crl.Der)))
                    Crls.Add(crl);
            }
        }

        /// <summary>X509_STORE_get1_objects: the certificates in X509_OBJECT order (by subject name).</summary>
        internal List<X509Cert> SortedCerts()
        {
            lock (this)
            {
                var copy = Certs.ToList();
                return copy.Select((c, i) => (c, i)).OrderBy(p => p.c.Subject, Comparer<X509Name>.Create(X509Name.Compare))
                    .ThenBy(p => p.i).Select(p => p.c).ToList();
            }
        }

        /// <summary>by_dir get_cert_by_subject: "hash.N" (certificates) or "hash.rN" (CRLs) files.</summary>
        private void LoadDirectoryEntries(X509Name name, bool crl)
        {
            if (Dirs.Count == 0)
                return;
            uint h = name.Hash();
            foreach (var dir in Dirs.ToList())
            {
                for (int k = 0; ; k++)
                {
                    string file = System.IO.Path.Combine(dir, $"{h:x8}.{(crl ? "r" : "")}{k}");
                    lock (this)
                    {
                        if (!_dirLoaded.Add(file))
                            continue;
                    }
                    var data = SslModule.ReadFile(file, out _);
                    if (data == null)
                        break;
                    try
                    {
                        LoadPemOrDer(data, crl);
                    }
                    catch (Asn1Exception)
                    {
                    }
                }
            }
        }

        private void LoadPemOrDer(byte[] data, bool crl)
        {
            int pos = 0;
            bool any = false;
            while (Pem.Next(data, ref pos) is { } block)
            {
                any = true;
                if (Pem.IsCertificateLabel(block.Label))
                    Add(X509Cert.Parse(block.Der));
                else if (block.Label == "X509 CRL")
                    AddCrl(X509Crl.Parse(block.Der));
            }
            if (!any)
            {
                if (crl)
                    AddCrl(X509Crl.Parse(data));
                else
                    Add(X509Cert.Parse(data));
            }
        }

        /// <summary>X509_STORE_CTX_get_by_subject for certificates.</summary>
        internal List<X509Cert> BySubject(X509Name name)
        {
            LoadDirectoryEntries(name, false);
            lock (this)
                return Certs.Where(c => X509Name.Compare(c.Subject, name) == 0).ToList();
        }

        internal List<X509Crl> CrlsByIssuer(X509Name name)
        {
            LoadDirectoryEntries(name, true);
            lock (this)
                return Crls.Where(c => X509Name.Compare(c.Issuer, name) == 0).ToList();
        }

        internal bool Contains(X509Cert cert)
        {
            lock (this)
                return Certs.Any(c => X509Cert.SameCert(c, cert));
        }
    }

    /// <summary>
    /// crypto/x509/x509_vfy.c (X509_verify_cert) and v3_utl.c (X509_check_host, X509_check_ip):
    /// the chain from the peer's certificate to a trusted one, the extensions, key strength, host
    /// name, revocation and signature checks, with OpenSSL's error codes and their texts.
    /// </summary>
    internal static class SslVerify
    {
        internal const int X509_V_OK = 0, X509_V_ERR_UNABLE_TO_GET_ISSUER_CERT = 2, X509_V_ERR_UNABLE_TO_GET_CRL = 3,
            X509_V_ERR_CERT_SIGNATURE_FAILURE = 7, X509_V_ERR_CRL_SIGNATURE_FAILURE = 8, X509_V_ERR_CERT_NOT_YET_VALID = 9,
            X509_V_ERR_CERT_HAS_EXPIRED = 10, X509_V_ERR_CRL_NOT_YET_VALID = 11, X509_V_ERR_CRL_HAS_EXPIRED = 12,
            X509_V_ERR_ERROR_IN_CERT_NOT_BEFORE_FIELD = 13, X509_V_ERR_ERROR_IN_CERT_NOT_AFTER_FIELD = 14,
            X509_V_ERR_DEPTH_ZERO_SELF_SIGNED_CERT = 18, X509_V_ERR_SELF_SIGNED_CERT_IN_CHAIN = 19,
            X509_V_ERR_UNABLE_TO_GET_ISSUER_CERT_LOCALLY = 20, X509_V_ERR_UNABLE_TO_VERIFY_LEAF_SIGNATURE = 21,
            X509_V_ERR_CERT_REVOKED = 23, X509_V_ERR_INVALID_CA = 24, X509_V_ERR_PATH_LENGTH_EXCEEDED = 25,
            X509_V_ERR_INVALID_PURPOSE = 26, X509_V_ERR_UNHANDLED_CRITICAL_EXTENSION = 34,
            X509_V_ERR_HOSTNAME_MISMATCH = 62, X509_V_ERR_IP_ADDRESS_MISMATCH = 64,
            X509_V_ERR_EE_KEY_TOO_SMALL = 66, X509_V_ERR_CA_KEY_TOO_SMALL = 67, X509_V_ERR_CA_MD_TOO_WEAK = 68;

        /// <summary>X509_verify_cert_error_string</summary>
        internal static string X509_verify_cert_error_string(int n) => n switch
        {
            0 => "ok",
            2 => "unable to get issuer certificate",
            3 => "unable to get certificate CRL",
            4 => "unable to decrypt certificate's signature",
            5 => "unable to decrypt CRL's signature",
            6 => "unable to decode issuer public key",
            7 => "certificate signature failure",
            8 => "CRL signature failure",
            9 => "certificate is not yet valid",
            10 => "certificate has expired",
            11 => "CRL is not yet valid",
            12 => "CRL has expired",
            13 => "format error in certificate's notBefore field",
            14 => "format error in certificate's notAfter field",
            15 => "format error in CRL's lastUpdate field",
            16 => "format error in CRL's nextUpdate field",
            17 => "out of memory",
            18 => "self-signed certificate",
            19 => "self-signed certificate in certificate chain",
            20 => "unable to get local issuer certificate",
            21 => "unable to verify the first certificate",
            22 => "certificate chain too long",
            23 => "certificate revoked",
            24 => "invalid CA certificate",
            25 => "path length constraint exceeded",
            26 => "unsupported certificate purpose",
            27 => "certificate not trusted",
            28 => "certificate rejected",
            34 => "unhandled critical extension",
            62 => "hostname mismatch",
            63 => "email address mismatch",
            64 => "IP address mismatch",
            66 => "EE certificate key too weak",
            67 => "CA certificate key too weak",
            68 => "CA signature digest algorithm too weak",
            _ => $"error number {n}",
        };

        // The extensions OpenSSL handles (X509_supported_extension): a critical one of another kind fails.
        private static readonly HashSet<string> SupportedExtensions = new()
        {
            "2.16.840.1.113730.1.1", "2.5.29.15", "2.5.29.17", "2.5.29.19", "2.5.29.37", "1.3.6.1.5.5.7.1.1",
            "2.5.29.30", "2.5.29.32", "2.5.29.36", "2.5.29.54", "1.3.6.1.5.5.7.1.14", "1.3.6.1.5.5.7.1.7",
            "1.3.6.1.5.5.7.1.8", "1.3.6.1.5.5.7.1.24", "2.5.29.14", "2.5.29.35", "2.5.29.31", "2.5.29.46",
        };

        internal sealed class Params
        {
            public SslStore Store;
            public ulong Flags;
            public int SecurityLevel;
            public bool ServerPurpose;      // verifying a server's certificate (X509_PURPOSE_SSL_SERVER)
            public string Host;             // X509_VERIFY_PARAM_set1_host
            public byte[] Ip;               // X509_VERIFY_PARAM_set1_ip
            public uint HostFlags;
            public DateTime Now = DateTime.UtcNow;
        }

        /// <summary>X509_verify_cert: X509_V_OK, or the first error with the chain as far as it was built.</summary>
        internal static int X509_verify_cert(X509Cert leaf, IList<X509Cert> untrusted, Params p, out List<X509Cert> chain)
        {
            chain = new List<X509Cert> { leaf };
            int num_untrusted = 1;
            bool anchored = false;
            var store = p.Store;
            bool partial = (p.Flags & SslModule.X509_V_FLAG_PARTIAL_CHAIN) != 0;

            // build_chain
            if (store.Contains(leaf) && (leaf.SelfSigned || partial))
            {
                anchored = true;
                num_untrusted = 0;
            }
            var pool = untrusted.Where(c => !X509Cert.SameCert(c, leaf)).ToList();
            while (!anchored && chain.Count < 100)
            {
                var curr = chain[^1];
                if (curr.SelfSigned && chain.Count > 1 && store.Contains(curr))
                {
                    anchored = true;
                    break;
                }
                // X509_V_FLAG_TRUSTED_FIRST (and the default search): the trusted store first
                X509Cert issuer = FindIssuer(store.BySubject(curr.Issuer), curr);
                if (issuer != null)
                {
                    if (X509Cert.SameCert(issuer, curr))
                    {
                        anchored = true;
                        break;
                    }
                    chain.Add(issuer);
                    if (issuer.SelfSigned || partial)
                    {
                        anchored = true;
                        break;
                    }
                    continue;
                }
                issuer = FindIssuer(pool.Where(c => X509Name.Compare(c.Subject, curr.Issuer) == 0), curr);
                if (issuer == null || curr.SelfSigned)
                    break;
                pool.Remove(issuer);
                chain.Add(issuer);
                num_untrusted++;
            }
            if (!anchored)
            {
                var last = chain[^1];
                if (last.SelfSigned && last.VerifiedBy(last))
                    return chain.Count == 1 ? X509_V_ERR_DEPTH_ZERO_SELF_SIGNED_CERT : X509_V_ERR_SELF_SIGNED_CERT_IN_CHAIN;
                if (num_untrusted < chain.Count)
                    return X509_V_ERR_UNABLE_TO_GET_ISSUER_CERT;
                return chain.Count == 1 ? X509_V_ERR_UNABLE_TO_GET_ISSUER_CERT_LOCALLY : X509_V_ERR_UNABLE_TO_GET_ISSUER_CERT_LOCALLY;
            }

            // check_chain_extensions
            int plen = 0;
            for (int i = 0; i < chain.Count; i++)
            {
                var x = chain[i];
                if (x.ExtensionsInvalid)
                    return X509_V_ERR_INVALID_CA;
                foreach (var e in x.Extensions)
                    if (e.Critical && !SupportedExtensions.Contains(e.Oid))
                        return X509_V_ERR_UNHANDLED_CRITICAL_EXTENSION;
                if (i > 0)
                {
                    bool selfSignedRoot = i == chain.Count - 1 && x.SelfSigned;
                    int ca = x.CheckCA();
                    if (ca == 0 || (ca != 1 && ca != 3 && (p.Flags & SslModule.X509_V_FLAG_X509_STRICT) != 0 && !selfSignedRoot))
                        return X509_V_ERR_INVALID_CA;
                    if (x.HasBasicConstraints && x.PathLength >= 0 && plen > x.PathLength + 1)
                        return X509_V_ERR_PATH_LENGTH_EXCEEDED;
                }
                if (!CheckPurpose(x, i == 0, p.ServerPurpose))
                    return X509_V_ERR_INVALID_PURPOSE;
                if (i == 0 || !x.SelfIssued)
                    plen++;
            }

            // check_auth_level
            if (p.SecurityLevel > 0)
            {
                int minbits = MinBits(p.SecurityLevel);
                for (int i = 0; i < chain.Count; i++)
                {
                    int bits = chain[i].PublicKeySecurityBits();
                    if (bits >= 0 && bits < minbits)
                        return i == 0 ? X509_V_ERR_EE_KEY_TOO_SMALL : X509_V_ERR_CA_KEY_TOO_SMALL;
                    // check_sig_level: not the trust anchor's own (self-)signature
                    if (i < chain.Count - 1 || !chain[i].SelfSigned)
                    {
                        int sigbits = X509Cert.SignatureAlgorithm(chain[i].SignatureOid).Bits;
                        if (sigbits >= 0 && sigbits < minbits)
                            return X509_V_ERR_CA_MD_TOO_WEAK;
                    }
                }
            }

            // check_id
            if (p.Host != null && !X509_check_host(leaf, p.Host, p.HostFlags))
                return X509_V_ERR_HOSTNAME_MISMATCH;
            if (p.Ip != null && !X509_check_ip(leaf, p.Ip))
                return X509_V_ERR_IP_ADDRESS_MISMATCH;

            // check_revocation
            if ((p.Flags & SslModule.X509_V_FLAG_CRL_CHECK) != 0)
            {
                int last = (p.Flags & SslModule.X509_V_FLAG_CRL_CHECK_ALL) != 0 ? chain.Count - 1 : 0;
                for (int i = 0; i <= last; i++)
                {
                    var x = chain[i];
                    if (i == chain.Count - 1 && x.SelfSigned && i > 0)
                        break;
                    var crls = store.CrlsByIssuer(x.Issuer);
                    var issuer = i + 1 < chain.Count ? chain[i + 1] : x;
                    X509Crl crl = null;
                    foreach (var c in crls)
                    {
                        if (X509Cert.VerifySignature(c.TbsDer, c.SignatureOid, c.SignatureParams, c.Signature, issuer))
                        {
                            crl = c;
                            break;
                        }
                    }
                    if (crl == null)
                        return crls.Count == 0 ? X509_V_ERR_UNABLE_TO_GET_CRL : X509_V_ERR_CRL_SIGNATURE_FAILURE;
                    var thisUpdate = Asn1.TimeValue(crl.ThisUpdateTag, crl.ThisUpdate);
                    if (thisUpdate != null && thisUpdate > p.Now)
                        return X509_V_ERR_CRL_NOT_YET_VALID;
                    if (crl.NextUpdate != null && Asn1.TimeValue(crl.NextUpdateTag, crl.NextUpdate) is { } next && next < p.Now)
                        return X509_V_ERR_CRL_HAS_EXPIRED;
                    if (crl.IsRevoked(x))
                        return X509_V_ERR_CERT_REVOKED;
                }
            }

            // internal_verify: from the trust anchor down
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                var x = chain[i];
                var issuer = i + 1 < chain.Count ? chain[i + 1] : x;
                bool anchor = i == chain.Count - 1;
                if (!anchor || (x.SelfSigned && false))
                {
                    if (!x.VerifiedBy(issuer))
                        return X509_V_ERR_CERT_SIGNATURE_FAILURE;
                }
                var nb = x.NotBeforeTime;
                if (nb == null)
                    return X509_V_ERR_ERROR_IN_CERT_NOT_BEFORE_FIELD;
                if (nb > p.Now)
                    return X509_V_ERR_CERT_NOT_YET_VALID;
                var na = x.NotAfterTime;
                if (na == null)
                    return X509_V_ERR_ERROR_IN_CERT_NOT_AFTER_FIELD;
                if (na < p.Now)
                    return X509_V_ERR_CERT_HAS_EXPIRED;
            }
            return X509_V_OK;
        }

        private static X509Cert FindIssuer(IEnumerable<X509Cert> candidates, X509Cert subject)
        {
            X509Cert fallback = null;
            foreach (var c in candidates)
            {
                if (X509Name.Compare(c.Subject, subject.Issuer) != 0)
                    continue;
                if (subject.AuthorityKeyId != null && c.SubjectKeyId != null && !subject.AuthorityKeyId.AsSpan().SequenceEqual(c.SubjectKeyId))
                    continue;
                // check_issued prefers a time-valid issuer
                var now = DateTime.UtcNow;
                if (c.NotBeforeTime <= now && c.NotAfterTime >= now)
                    return c;
                fallback ??= c;
            }
            return fallback;
        }

        // SSL_get_security_level's minimum bits (ssl_security_default_callback: level 1..5).
        internal static int MinBits(int level) => level switch
        {
            <= 0 => 0, 1 => 80, 2 => 112, 3 => 128, 4 => 192, _ => 256,
        };

        /// <summary>X509_check_purpose(X509_PURPOSE_SSL_SERVER / SSL_CLIENT), the leaf or a CA.</summary>
        private static bool CheckPurpose(X509Cert x, bool leaf, bool server)
        {
            string want = server ? X509Cert.OID_SERVER_AUTH : X509Cert.OID_CLIENT_AUTH;
            if (x.HasExtKeyUsage && !x.ExtKeyUsage.Contains(want) && !x.ExtKeyUsage.Contains(X509Cert.OID_ANY_EKU)
                && !(server && (x.ExtKeyUsage.Contains(X509Cert.OID_NS_SGC) || x.ExtKeyUsage.Contains(X509Cert.OID_MS_SGC))))
                return false;
            if (x.HasNsCertType)
            {
                int bit = leaf ? (server ? X509Cert.NS_SSL_SERVER : X509Cert.NS_SSL_CLIENT) : (server ? 0x04 : 0x04);
                if ((x.NsCertType & bit) == 0 && leaf)
                    return false;
            }
            if (leaf && x.HasKeyUsage)
            {
                int need = server ? (X509Cert.KU_DIGITAL_SIGNATURE | X509Cert.KU_KEY_ENCIPHERMENT | X509Cert.KU_KEY_AGREEMENT)
                    : (X509Cert.KU_DIGITAL_SIGNATURE | X509Cert.KU_KEY_AGREEMENT);
                if ((x.KeyUsage & need) == 0)
                    return false;
            }
            return true;
        }

        // ---- host names (v3_utl.c) ----

        /// <summary>a2i_IPADDRESS: the address bytes of an IPv4 or IPv6 literal, or null.</summary>
        internal static byte[] a2i_IPADDRESS(string text)
        {
            if (text.Contains(':'))
            {
                if (IPAddress.TryParse(text, out var v6) && v6.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 && !text.Contains('%'))
                    return v6.GetAddressBytes();
                return null;
            }
            var parts = text.Split('.');
            if (parts.Length != 4)
                return null;
            var result = new byte[4];
            for (int i = 0; i < 4; i++)
            {
                if (parts[i].Length == 0 || parts[i].Length > 3 || !parts[i].All(char.IsAsciiDigit))
                    return null;
                int v = int.Parse(parts[i]);
                if (v > 255)
                    return null;
                result[i] = (byte)v;
            }
            return result;
        }

        private static bool EqualNoCase(string pattern, string subject)
        {
            // equal_nocase: a trailing dot of the subject is ignored
            if (pattern.Length == subject.Length + 1 && pattern.EndsWith('.'))
                pattern = pattern.Substring(0, pattern.Length - 1);
            else if (subject.Length == pattern.Length + 1 && subject.EndsWith('.'))
                subject = subject.Substring(0, subject.Length - 1);
            return string.Equals(pattern, subject, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>wildcard_match / equal_wildcard of v3_utl.c</summary>
        private static bool EqualWildcard(string pattern, string subject, uint flags)
        {
            int star = -1;
            if ((flags & SslModule.X509_CHECK_FLAG_NO_WILDCARDS) == 0)
                star = ValidStar(pattern, flags);
            if (star < 0)
                return EqualNoCase(pattern, subject);
            string prefix = pattern.Substring(0, star);
            string suffix = pattern.Substring(star + 1);
            // wildcard_match
            if (subject.Length < prefix.Length + suffix.Length)
                return false;
            if (!string.Equals(prefix, subject.Substring(0, prefix.Length), StringComparison.OrdinalIgnoreCase))
                return false;
            string tail = subject.Substring(subject.Length - suffix.Length);
            if (!EqualNoCase(suffix, tail))
                return false;
            // IDNA labels cannot match partial wildcards
            if (prefix.Length > 0 && prefix.StartsWith("xn--", StringComparison.OrdinalIgnoreCase))
                return false;
            string wildcardPart = subject.Substring(prefix.Length, subject.Length - suffix.Length - prefix.Length);
            // The wildcard may match a literal '*' only when partial wildcards are allowed? no: the label must be non-empty
            if (wildcardPart.Length == 0 && prefix.Length == 0 && suffix.StartsWith('.'))
                return false;
            bool allowMulti = (flags & SslModule.X509_CHECK_FLAG_MULTI_LABEL_WILDCARDS) != 0;
            foreach (var ch in wildcardPart)
            {
                if (ch == '.' && !allowMulti)
                    return false;
                if (!(char.IsAsciiLetterOrDigit(ch) || ch == '-' || (allowMulti && ch == '.')))
                    return false;
            }
            return true;
        }

        /// <summary>valid_star: the position of the one '*' that may act as a wildcard, or -1.</summary>
        private static int ValidStar(string p, uint flags)
        {
            int star = -1;
            int dots = 0;
            bool atStart = true, atEnd = true;
            int state = 1; // LABEL_START
            for (int i = 0; i < p.Length; i++)
            {
                char c = p[i];
                if (c == '*')
                {
                    bool labelEnd = i + 1 == p.Length || p[i + 1] == '.';
                    if (star >= 0 || (state & 2) != 0 || dots > 0)
                        return -1;
                    // Only full-label '*.example.com' wildcards?
                    if ((flags & SslModule.X509_CHECK_FLAG_NO_PARTIAL_WILDCARDS) != 0 && (!atStart || !labelEnd))
                        return -1;
                    // No 'xn--foo*' internationalized labels
                    if (!atStart && i >= 4 && p.Substring(0, 4).Equals("xn--", StringComparison.OrdinalIgnoreCase))
                        return -1;
                    star = i;
                    atStart = false;
                    atEnd = labelEnd;
                }
                else if (char.IsAsciiLetterOrDigit(c))
                {
                    atStart = false;
                    state = 0;
                }
                else if (c == '.')
                {
                    if (i == 0 || p[i - 1] == '.')
                        return -1;
                    atStart = true;
                    dots++;
                    state = 1;
                }
                else if (c == '-')
                {
                    atStart = false;
                }
                else
                    return -1;
            }
            // At least two dots after the star: "*.example.com", not "*.com".
            if (star < 0 || dots < 2)
                return -1;
            return star;
        }

        /// <summary>X509_check_host: the DNS names of subjectAltName, else (no DNS name) the subject's CNs.</summary>
        internal static bool X509_check_host(X509Cert x, string host, uint flags)
        {
            if (host.Length > 0 && host[0] == '.')
                return false;
            var san = x.SubjectAltNames();
            bool sawDns = false;
            if (san != null)
            {
                foreach (var g in san)
                {
                    if (g.Type != GeneralName.GEN_DNS)
                        continue;
                    sawDns = true;
                    var name = Encoding.Latin1.GetString(g.Bytes);
                    if (name.IndexOf('\0') >= 0)
                        continue;
                    if (EqualWildcard(name, host, flags))
                        return true;
                }
            }
            if ((flags & SslModule.X509_CHECK_FLAG_NEVER_CHECK_SUBJECT) != 0)
                return false;
            if (sawDns && (flags & SslModule.X509_CHECK_FLAG_ALWAYS_CHECK_SUBJECT) == 0)
                return false;
            foreach (var e in x.Subject.Entries)
            {
                if (e.Oid != "2.5.4.3")
                    continue;
                var utf8 = Asn1.StringToUtf8(e.Tag, e.Value);
                if (utf8 == null)
                    continue;
                var name = Encoding.UTF8.GetString(utf8);
                if (name.IndexOf('\0') >= 0)
                    continue;
                if (EqualWildcard(name, host, flags))
                    return true;
            }
            return false;
        }

        /// <summary>X509_check_ip: an iPAddress of subjectAltName with exactly these bytes.</summary>
        internal static bool X509_check_ip(X509Cert x, byte[] ip)
        {
            var san = x.SubjectAltNames();
            if (san == null)
                return false;
            foreach (var g in san)
                if (g.Type == GeneralName.GEN_IPADD && g.Bytes.AsSpan().SequenceEqual(ip))
                    return true;
            return false;
        }
    }
}
