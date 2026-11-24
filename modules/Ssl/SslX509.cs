using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SharpPy.Modules.Ssl
{
    /// <summary>X509_NAME_ENTRY: the attribute's OID, the value's tag and content, and its RDN (set).</summary>
    internal sealed class X509NameEntry
    {
        public string Oid;
        public int Tag;
        public byte[] Value;
        public int Set;
    }

    /// <summary>X509_NAME with its DER and the canonical encoding (x509_name_canon) libcrypto keeps.</summary>
    internal sealed class X509Name
    {
        public readonly List<X509NameEntry> Entries = new();
        public byte[] Der;
        private byte[] _canon;

        internal static X509Name Parse(Tlv tlv)
        {
            if (!tlv.Is(0, Asn1.SEQUENCE))
                throw new Asn1Exception("WRONG_TAG");
            var name = new X509Name { Der = tlv.Encoded() };
            int set = 0;
            foreach (var rdn in tlv.Children())
            {
                if (!rdn.Is(0, Asn1.SET))
                    throw new Asn1Exception("WRONG_TAG");
                foreach (var atv in rdn.Children())
                {
                    var parts = atv.Children();
                    if (!atv.Is(0, Asn1.SEQUENCE) || parts.Count != 2 || !parts[0].Is(0, Asn1.OBJECT))
                        throw new Asn1Exception("WRONG_TAG");
                    var value = parts[1];
                    byte[] content = value.ContentBytes();
                    if (value.Is(0, Asn1.BIT_STRING) && content.Length > 0)
                        content = content.AsSpan(1).ToArray();
                    name.Entries.Add(new X509NameEntry
                    {
                        Oid = Asn1.OidText(parts[0].Content),
                        Tag = value.Class == 0 ? value.Tag : -1,
                        Value = content,
                        Set = set,
                    });
                }
                set++;
            }
            return name;
        }

        // ASN1_MASK_CANON: the string types x509_name_canon folds to UTF8String.
        private static bool CanonType(int tag) => tag is Asn1.UTF8STRING or Asn1.BMPSTRING or Asn1.UNIVERSALSTRING
            or Asn1.PRINTABLESTRING or Asn1.T61STRING or Asn1.IA5STRING or Asn1.VISIBLESTRING;

        private static bool IsSpace(byte c) => c is (byte)' ' or (byte)'\t' or (byte)'\n' or 0x0b or 0x0c or (byte)'\r';

        /// <summary>asn1_string_canon: UTF-8, ASCII lower case, outer white space off, inner runs one space.</summary>
        private static byte[] StringCanon(int tag, byte[] value)
        {
            var utf8 = Asn1.StringToUtf8(tag, value) ?? value;
            int from = 0, to = utf8.Length;
            while (from < to && IsSpace(utf8[from]))
                from++;
            while (to > from && IsSpace(utf8[to - 1]))
                to--;
            var output = new List<byte>(to - from);
            for (int i = from; i < to; i++)
            {
                byte c = utf8[i];
                if (IsSpace(c))
                {
                    output.Add((byte)' ');
                    while (i + 1 < to && IsSpace(utf8[i + 1]))
                        i++;
                }
                else if (c < 0x80 && c >= 'A' && c <= 'Z')
                    output.Add((byte)(c + 32));
                else
                    output.Add(c);
            }
            return output.ToArray();
        }

        /// <summary>x509_name_canon: each RDN as a DER SET OF its canonical entries, concatenated.</summary>
        internal byte[] Canon
        {
            get
            {
                if (_canon != null)
                    return _canon;
                var output = new List<byte>();
                foreach (var group in Entries.GroupBy(e => e.Set).OrderBy(g => g.Key))
                {
                    var encoded = new List<byte[]>();
                    foreach (var e in group)
                    {
                        byte[] valueDer;
                        if (e.Tag >= 0 && CanonType(e.Tag))
                            valueDer = Asn1.Encode(Asn1.UTF8STRING, StringCanon(e.Tag, e.Value));
                        else
                            valueDer = Asn1.Encode(e.Tag >= 0 ? e.Tag : 0, e.Value);
                        var oid = Asn1.Encode(Asn1.OBJECT, Asn1.OidEncode(e.Oid));
                        encoded.Add(Asn1.Encode(0x30, oid.Concat(valueDer).ToArray()));
                    }
                    // DER SET OF: the elements in the order of their encodings.
                    encoded.Sort(CompareDer);
                    output.AddRange(Asn1.Encode(0x31, encoded.SelectMany(x => x).ToArray()));
                }
                return _canon = output.ToArray();
            }
        }

        private static int CompareDer(byte[] a, byte[] b)
        {
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++)
                if (a[i] != b[i])
                    return a[i] - b[i];
            return a.Length - b.Length;
        }

        /// <summary>X509_NAME_cmp: the canonical lengths, then the canonical bytes.</summary>
        internal static int Compare(X509Name a, X509Name b)
        {
            var x = a.Canon;
            var y = b.Canon;
            if (x.Length != y.Length)
                return x.Length - y.Length;
            for (int i = 0; i < x.Length; i++)
                if (x[i] != y[i])
                    return x[i] - y[i];
            return 0;
        }

        /// <summary>X509_NAME_hash_ex: the first four bytes of SHA-1 of the canonical encoding, little end first.</summary>
        internal uint Hash()
        {
            var md = SHA1.HashData(Canon);
            return (uint)(md[0] | (md[1] << 8) | (md[2] << 16) | (md[3] << 24));
        }

        /// <summary>
        /// X509_NAME_print_ex with XN_FLAG_RFC2253: the RDNs last first, "," between them and "+"
        /// inside one, short names, values escaped as ASN1_STRFLGS_RFC2253 does (an unknown
        /// attribute's value as "#" and its DER in hexadecimal).
        /// </summary>
        internal string PrintRfc2253()
        {
            var text = new StringBuilder();
            int prevSet = -1;
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                var e = Entries[i];
                if (prevSet != -1)
                    text.Append(prevSet == e.Set ? "+" : ",");
                prevSet = e.Set;
                var obj = SslObjects.ByOidText(e.Oid);
                text.Append(obj?.Sn ?? e.Oid).Append('=');
                if (obj == null || e.Tag < 0)
                {
                    // ASN1_STRFLGS_DUMP_UNKNOWN | DUMP_DER
                    var der = Asn1.Encode(e.Tag >= 0 ? e.Tag : 0, e.Tag == Asn1.BIT_STRING ? new byte[] { 0 }.Concat(e.Value).ToArray() : e.Value);
                    text.Append('#');
                    foreach (var b in der)
                        text.Append(b.ToString("X2"));
                    continue;
                }
                var utf8 = Asn1.StringToUtf8(e.Tag, e.Value) ?? e.Value;
                for (int j = 0; j < utf8.Length; j++)
                {
                    byte c = utf8[j];
                    bool first = j == 0, last = j == utf8.Length - 1;
                    if (c > 0x7f || c < 0x20 || c == 0x7f)
                        text.Append('\\').Append(c.ToString("X2"));
                    else if (",+\"\\<>;".IndexOf((char)c) >= 0 || (first && (c == '#' || c == ' ')) || (last && c == ' '))
                        text.Append('\\').Append((char)c);
                    else
                        text.Append((char)c);
                }
            }
            return text.ToString();
        }
    }

    /// <summary>X509_EXTENSION</summary>
    internal sealed class X509Extension
    {
        public string Oid;
        public bool Critical;
        public byte[] Value; // the OCTET STRING's content: the extension's DER
    }

    /// <summary>GENERAL_NAME: its choice (GEN_*) and its parsed value.</summary>
    internal sealed class GeneralName
    {
        internal const int GEN_OTHERNAME = 0, GEN_EMAIL = 1, GEN_DNS = 2, GEN_X400 = 3, GEN_DIRNAME = 4,
            GEN_EDIPARTY = 5, GEN_URI = 6, GEN_IPADD = 7, GEN_RID = 8;
        public int Type;
        public byte[] Bytes;          // IA5 strings, the IP address, an OID's content
        public X509Name DirName;
        public string OtherOid;       // otherName type-id
        public int OtherTag = -1;     // otherName value's tag
        public byte[] OtherValue;

        internal static GeneralName Parse(Tlv tlv)
        {
            if (tlv.Class != 2)
                throw new Asn1Exception("WRONG_TAG");
            var g = new GeneralName { Type = tlv.Tag };
            switch (tlv.Tag)
            {
                case GEN_OTHERNAME:
                    {
                        var parts = tlv.Children();
                        if (parts.Count < 2 || !parts[0].Is(0, Asn1.OBJECT))
                            throw new Asn1Exception("WRONG_TAG");
                        g.OtherOid = Asn1.OidText(parts[0].Content);
                        // value [0] EXPLICIT ANY
                        var inner = parts[1].Children();
                        if (inner.Count == 1 && inner[0].Class == 0)
                        {
                            g.OtherTag = inner[0].Tag;
                            g.OtherValue = inner[0].ContentBytes();
                        }
                        break;
                    }
                case GEN_DIRNAME:
                    {
                        var inner = tlv.Children();
                        if (inner.Count != 1)
                            throw new Asn1Exception("WRONG_TAG");
                        g.DirName = X509Name.Parse(inner[0]);
                        break;
                    }
                case GEN_X400:
                case GEN_EDIPARTY:
                    break;
                default:
                    g.Bytes = tlv.ContentBytes();
                    break;
            }
            return g;
        }

        internal static List<GeneralName> ParseNames(Tlv seq)
        {
            if (!seq.Is(0, Asn1.SEQUENCE))
                throw new Asn1Exception("WRONG_TAG");
            return seq.Children().Select(Parse).ToList();
        }
    }

    /// <summary>
    /// X509 as libcrypto decodes it (d2i_X509) with the extension flags ossl_x509v3_cache_extensions
    /// computes from it.
    /// </summary>
    internal sealed class X509Cert
    {
        public byte[] Der, TbsDer;
        public int Version;               // X509_get_version (0 for v1)
        public byte[] Serial;             // INTEGER content octets
        public X509Name Issuer, Subject;
        public int NotBeforeTag, NotAfterTag;
        public byte[] NotBefore, NotAfter;
        public byte[] SubjectPublicKeyInfo;
        public string PublicKeyOid;
        public string SignatureOid;
        public byte[] SignatureParams;
        public byte[] Signature;
        public readonly List<X509Extension> Extensions = new();

        // ex_flags
        public bool HasBasicConstraints, IsCA, HasKeyUsage, HasExtKeyUsage, HasNsCertType, ExtensionsInvalid;
        public int PathLength = -1;
        public int KeyUsage;
        public int NsCertType;
        public readonly List<string> ExtKeyUsage = new();
        public byte[] SubjectKeyId, AuthorityKeyId;

        internal const int KU_DIGITAL_SIGNATURE = 0x80, KU_KEY_ENCIPHERMENT = 0x20, KU_KEY_AGREEMENT = 0x08,
            KU_KEY_CERT_SIGN = 0x04, KU_CRL_SIGN = 0x02;
        internal const int NS_SSL_CLIENT = 0x80, NS_SSL_SERVER = 0x40, NS_ANY_CA = 0x07;

        /// <summary>d2i_X509, which also refuses trailing garbage inside the outer SEQUENCE.</summary>
        internal static X509Cert Parse(byte[] der)
        {
            var outer = Asn1.Parse(der);
            if (outer.End != der.Length)
                der = outer.Encoded();
            return Parse(outer);
        }

        internal static X509Cert Parse(Tlv outer)
        {
            if (!outer.Is(0, Asn1.SEQUENCE))
                throw new Asn1Exception("WRONG_TAG");
            var parts = outer.Children();
            if (parts.Count != 3 || !parts[0].Is(0, Asn1.SEQUENCE) || !parts[1].Is(0, Asn1.SEQUENCE) || !parts[2].Is(0, Asn1.BIT_STRING))
                throw new Asn1Exception("WRONG_TAG");
            var cert = new X509Cert { Der = outer.Encoded(), TbsDer = parts[0].Encoded() };
            var alg = parts[1].Children();
            cert.SignatureOid = Asn1.OidText(alg[0].Content);
            cert.SignatureParams = alg.Count > 1 ? alg[1].Encoded() : null;
            var sig = parts[2].Content;
            cert.Signature = sig.Length > 0 ? sig.Slice(1).ToArray() : Array.Empty<byte>();

            var tbs = parts[0].Children();
            int i = 0;
            if (tbs.Count > 0 && tbs[0].Is(2, 0))
            {
                var v = tbs[0].Children();
                cert.Version = v.Count == 1 && v[0].Is(0, Asn1.INTEGER) && v[0].Length > 0 ? v[0].Content[v[0].Length - 1] : 0;
                i++;
            }
            if (tbs.Count < i + 6)
                throw new Asn1Exception("NOT_ENOUGH_DATA");
            if (!tbs[i].Is(0, Asn1.INTEGER))
                throw new Asn1Exception("WRONG_TAG");
            cert.Serial = tbs[i++].ContentBytes();
            i++; // signature AlgorithmIdentifier
            cert.Issuer = X509Name.Parse(tbs[i++]);
            var validity = tbs[i++].Children();
            if (validity.Count != 2)
                throw new Asn1Exception("WRONG_TAG");
            cert.NotBeforeTag = validity[0].Tag;
            cert.NotBefore = validity[0].ContentBytes();
            cert.NotAfterTag = validity[1].Tag;
            cert.NotAfter = validity[1].ContentBytes();
            cert.Subject = X509Name.Parse(tbs[i++]);
            cert.SubjectPublicKeyInfo = tbs[i].Encoded();
            var spki = tbs[i++].Children();
            if (spki.Count > 0 && spki[0].Is(0, Asn1.SEQUENCE))
            {
                var keyAlg = spki[0].Children();
                if (keyAlg.Count > 0 && keyAlg[0].Is(0, Asn1.OBJECT))
                    cert.PublicKeyOid = Asn1.OidText(keyAlg[0].Content);
            }
            for (; i < tbs.Count; i++)
            {
                if (tbs[i].Is(2, 3))
                {
                    var list = tbs[i].Children();
                    if (list.Count != 1 || !list[0].Is(0, Asn1.SEQUENCE))
                        throw new Asn1Exception("WRONG_TAG");
                    foreach (var ext in list[0].Children())
                    {
                        var e = ext.Children();
                        if (e.Count < 2 || !e[0].Is(0, Asn1.OBJECT))
                            throw new Asn1Exception("WRONG_TAG");
                        bool critical = e.Count == 3 && e[1].Is(0, Asn1.BOOLEAN) && e[1].Length == 1 && e[1].Content[0] != 0;
                        var value = e[e.Count - 1];
                        if (!value.Is(0, Asn1.OCTET_STRING))
                            throw new Asn1Exception("WRONG_TAG");
                        cert.Extensions.Add(new X509Extension { Oid = Asn1.OidText(e[0].Content), Critical = critical, Value = value.ContentBytes() });
                    }
                }
            }
            cert.CacheExtensions();
            return cert;
        }

        internal X509Extension Ext(string oid)
        {
            X509Extension found = null;
            foreach (var e in Extensions)
            {
                if (e.Oid == oid)
                {
                    if (found != null)
                        return null; // X509_get_ext_d2i: a repeated extension is an error
                    found = e;
                }
            }
            return found;
        }

        internal const string OID_BASIC_CONSTRAINTS = "2.5.29.19", OID_KEY_USAGE = "2.5.29.15", OID_EXT_KEY_USAGE = "2.5.29.37",
            OID_SAN = "2.5.29.17", OID_CRLDP = "2.5.29.31", OID_AIA = "1.3.6.1.5.5.7.1.1", OID_NS_CERT_TYPE = "2.16.840.1.113730.1.1",
            OID_SKID = "2.5.29.14", OID_AKID = "2.5.29.35", OID_AD_OCSP = "1.3.6.1.5.5.7.48.1", OID_AD_CA_ISSUERS = "1.3.6.1.5.5.7.48.2",
            OID_SERVER_AUTH = "1.3.6.1.5.5.7.3.1", OID_CLIENT_AUTH = "1.3.6.1.5.5.7.3.2", OID_ANY_EKU = "2.5.29.37.0",
            OID_NS_SGC = "2.16.840.1.113730.4.1", OID_MS_SGC = "1.3.6.1.4.1.311.10.3.3";

        /// <summary>ossl_x509v3_cache_extensions: the flags X509_check_ca and the purpose checks read.</summary>
        private void CacheExtensions()
        {
            try
            {
                if (Ext(OID_BASIC_CONSTRAINTS) is { } bc)
                {
                    HasBasicConstraints = true;
                    foreach (var item in Asn1.Parse(bc.Value).Children())
                    {
                        if (item.Is(0, Asn1.BOOLEAN))
                            IsCA = item.Length > 0 && item.Content[0] != 0;
                        else if (item.Is(0, Asn1.INTEGER))
                            PathLength = item.Length is > 0 and <= 4 ? (int)item.Content.ToArray().Aggregate(0L, (a, b) => (a << 8) | b) : int.MaxValue;
                    }
                }
                if (Ext(OID_KEY_USAGE) is { } ku)
                {
                    HasKeyUsage = true;
                    var bits = Asn1.Parse(ku.Value).Content;
                    KeyUsage = bits.Length > 1 ? bits[1] : 0;
                    if (bits.Length > 2)
                        KeyUsage |= bits[2] << 8;
                }
                if (Ext(OID_EXT_KEY_USAGE) is { } eku)
                {
                    HasExtKeyUsage = true;
                    foreach (var item in Asn1.Parse(eku.Value).Children())
                        ExtKeyUsage.Add(Asn1.OidText(item.Content));
                }
                if (Ext(OID_NS_CERT_TYPE) is { } ns)
                {
                    HasNsCertType = true;
                    var bits = Asn1.Parse(ns.Value).Content;
                    NsCertType = bits.Length > 1 ? bits[1] : 0;
                }
                if (Ext(OID_SKID) is { } skid)
                    SubjectKeyId = Asn1.Parse(skid.Value).ContentBytes();
                if (Ext(OID_AKID) is { } akid)
                {
                    foreach (var item in Asn1.Parse(akid.Value).Children())
                        if (item.Is(2, 0))
                            AuthorityKeyId = item.ContentBytes();
                }
            }
            catch (Asn1Exception)
            {
                ExtensionsInvalid = true;
            }
        }

        /// <summary>EXFLAG_SI: issued by itself (names match, the key ids agree).</summary>
        internal bool SelfIssued =>
            X509Name.Compare(Subject, Issuer) == 0 &&
            (AuthorityKeyId == null || SubjectKeyId == null || AuthorityKeyId.AsSpan().SequenceEqual(SubjectKeyId));

        /// <summary>EXFLAG_SS: self-issued, and a key usage (if any) that allows certificate signing.</summary>
        internal bool SelfSigned => SelfIssued && (!HasKeyUsage || (KeyUsage & KU_KEY_CERT_SIGN) != 0);

        /// <summary>X509_check_ca (check_ca in v3_purp.c).</summary>
        internal int CheckCA()
        {
            // keyUsage if present should allow cert signing
            if (HasKeyUsage && (KeyUsage & KU_KEY_CERT_SIGN) == 0)
                return 0;
            if (HasBasicConstraints)
                return IsCA ? 1 : 0;
            // we support V1 roots for...  uh, I don't really know why.
            if (Version == 0 && SelfSigned)
                return 3;
            // If key usage present it must have certSign so tolerate it
            if (HasKeyUsage)
                return 4;
            // Older certificates could have Netscape-specific CA types
            if (HasNsCertType && (NsCertType & NS_ANY_CA) != 0)
                return 5;
            return 0;
        }

        /// <summary>X509_cmp: the encodings (CPython only asks equal or not).</summary>
        internal static bool SameCert(X509Cert a, X509Cert b) => a.Der.AsSpan().SequenceEqual(b.Der);

        /// <summary>The subjectAltName names (X509_get_ext_d2i NID_subject_alt_name), or null.</summary>
        internal List<GeneralName> SubjectAltNames()
        {
            if (Ext(OID_SAN) is not { } san)
                return null;
            try
            {
                return GeneralName.ParseNames(Asn1.Parse(san.Value));
            }
            catch (Asn1Exception)
            {
                return null;
            }
        }

        /// <summary>The public key's strength in bits, as EVP_PKEY_get_security_bits sees it.</summary>
        internal int PublicKeySecurityBits()
        {
            try
            {
                if (PublicKeyOid == "1.2.840.113549.1.1.1" || PublicKeyOid == "1.2.840.113549.1.1.10")
                {
                    using var rsa = RSA.Create();
                    rsa.ImportSubjectPublicKeyInfo(SubjectPublicKeyInfo, out _);
                    return RsaSecurityBits(rsa.KeySize);
                }
                if (PublicKeyOid == "1.2.840.10045.2.1")
                {
                    using var ec = ECDsa.Create();
                    ec.ImportSubjectPublicKeyInfo(SubjectPublicKeyInfo, out _);
                    return ec.KeySize / 2;
                }
                if (PublicKeyOid == "1.2.840.10040.4.1")
                {
                    using var dsa = DSA.Create();
                    dsa.ImportSubjectPublicKeyInfo(SubjectPublicKeyInfo, out _);
                    return RsaSecurityBits(dsa.KeySize);
                }
                if (PublicKeyOid == "1.3.101.112")
                    return 128;
                if (PublicKeyOid == "1.3.101.113")
                    return 224;
            }
            catch (CryptographicException)
            {
            }
            catch (PlatformNotSupportedException)
            {
            }
            return -1;
        }

        /// <summary>ossl_ifc_ffc_compute_security_bits: the strength of an RSA/DH/DSA modulus.</summary>
        internal static int RsaSecurityBits(int n)
        {
            if (n >= 15360) return 256;
            if (n >= 7680) return 192;
            if (n >= 3072) return 128;
            if (n >= 2048) return 112;
            if (n >= 1024) return 80;
            return 0;
        }

        /// <summary>The hash a signature algorithm OID names, and its security bits.</summary>
        internal static (HashAlgorithmName Hash, int Bits, string Kind) SignatureAlgorithm(string oid) => oid switch
        {
            "1.2.840.113549.1.1.4" => (HashAlgorithmName.MD5, 39, "rsa"),
            "1.2.840.113549.1.1.5" => (HashAlgorithmName.SHA1, 63, "rsa"),
            "1.2.840.113549.1.1.14" => (new HashAlgorithmName("SHA224"), 112, "rsa"),
            "1.2.840.113549.1.1.11" => (HashAlgorithmName.SHA256, 128, "rsa"),
            "1.2.840.113549.1.1.12" => (HashAlgorithmName.SHA384, 192, "rsa"),
            "1.2.840.113549.1.1.13" => (HashAlgorithmName.SHA512, 256, "rsa"),
            "1.2.840.113549.1.1.10" => (default, 128, "pss"),
            "1.2.840.10045.4.1" => (HashAlgorithmName.SHA1, 63, "ecdsa"),
            "1.2.840.10045.4.3.1" => (new HashAlgorithmName("SHA224"), 112, "ecdsa"),
            "1.2.840.10045.4.3.2" => (HashAlgorithmName.SHA256, 128, "ecdsa"),
            "1.2.840.10045.4.3.3" => (HashAlgorithmName.SHA384, 192, "ecdsa"),
            "1.2.840.10045.4.3.4" => (HashAlgorithmName.SHA512, 256, "ecdsa"),
            "1.2.840.10040.4.3" => (HashAlgorithmName.SHA1, 63, "dsa"),
            "2.16.840.1.101.3.4.3.2" => (HashAlgorithmName.SHA256, 128, "dsa"),
            "1.3.101.112" => (default, 128, "ed25519"),
            _ => (default, -1, null),
        };

        /// <summary>X509_verify / ASN1_item_verify: the signature over tbs made with the issuer's key.</summary>
        internal static bool VerifySignature(byte[] signedDer, string sigOid, byte[] sigParams, byte[] signature, X509Cert issuer)
        {
            var (hash, _, kind) = SignatureAlgorithm(sigOid);
            try
            {
                switch (kind)
                {
                    case "rsa":
                        {
                            using var rsa = RSA.Create();
                            rsa.ImportSubjectPublicKeyInfo(issuer.SubjectPublicKeyInfo, out _);
                            return rsa.VerifyData(signedDer, signature, hash, RSASignaturePadding.Pkcs1);
                        }
                    case "pss":
                        {
                            using var rsa = RSA.Create();
                            rsa.ImportSubjectPublicKeyInfo(issuer.SubjectPublicKeyInfo, out _);
                            return rsa.VerifyData(signedDer, signature, PssHash(sigParams), RSASignaturePadding.Pss);
                        }
                    case "ecdsa":
                        {
                            using var ec = ECDsa.Create();
                            ec.ImportSubjectPublicKeyInfo(issuer.SubjectPublicKeyInfo, out _);
                            return ec.VerifyData(signedDer, signature, hash, DSASignatureFormat.Rfc3279DerSequence);
                        }
                    case "dsa":
                        {
                            using var dsa = DSA.Create();
                            dsa.ImportSubjectPublicKeyInfo(issuer.SubjectPublicKeyInfo, out _);
                            return dsa.VerifyData(signedDer, signature, hash, DSASignatureFormat.Rfc3279DerSequence);
                        }
                }
            }
            catch (CryptographicException)
            {
            }
            catch (PlatformNotSupportedException)
            {
            }
            return false;
        }

        // RSASSA-PSS-params: hashAlgorithm [0] (SHA-1 by default)
        private static HashAlgorithmName PssHash(byte[] parameters)
        {
            try
            {
                if (parameters != null)
                {
                    foreach (var item in Asn1.Parse(parameters).Children())
                    {
                        if (item.Is(2, 0))
                        {
                            var alg = item.Children()[0].Children()[0];
                            return Asn1.OidText(alg.Content) switch
                            {
                                "2.16.840.1.101.3.4.2.1" => HashAlgorithmName.SHA256,
                                "2.16.840.1.101.3.4.2.2" => HashAlgorithmName.SHA384,
                                "2.16.840.1.101.3.4.2.3" => HashAlgorithmName.SHA512,
                                _ => HashAlgorithmName.SHA1,
                            };
                        }
                    }
                }
            }
            catch (Asn1Exception)
            {
            }
            return HashAlgorithmName.SHA1;
        }

        internal bool VerifiedBy(X509Cert issuer) => VerifySignature(TbsDer, SignatureOid, SignatureParams, Signature, issuer);

        internal DateTime? NotBeforeTime => Asn1.TimeValue(NotBeforeTag, NotBefore);
        internal DateTime? NotAfterTime => Asn1.TimeValue(NotAfterTag, NotAfter);

        /// <summary>PEM_write_bio_X509</summary>
        internal string ToPem() => Pem.Write("CERTIFICATE", Der);
    }

    /// <summary>X509_CRL: issuer, update times, the revoked serial numbers and the signature.</summary>
    internal sealed class X509Crl
    {
        public byte[] Der, TbsDer;
        public X509Name Issuer;
        public int ThisUpdateTag, NextUpdateTag = -1;
        public byte[] ThisUpdate, NextUpdate;
        public readonly List<byte[]> Revoked = new();
        public string SignatureOid;
        public byte[] SignatureParams, Signature;

        internal static X509Crl Parse(byte[] der)
        {
            var outer = Asn1.Parse(der);
            var parts = outer.Children();
            if (!outer.Is(0, Asn1.SEQUENCE) || parts.Count != 3)
                throw new Asn1Exception("WRONG_TAG");
            var crl = new X509Crl { Der = outer.Encoded(), TbsDer = parts[0].Encoded() };
            var alg = parts[1].Children();
            crl.SignatureOid = Asn1.OidText(alg[0].Content);
            crl.SignatureParams = alg.Count > 1 ? alg[1].Encoded() : null;
            crl.Signature = parts[2].Content.Length > 0 ? parts[2].Content.Slice(1).ToArray() : Array.Empty<byte>();
            var tbs = parts[0].Children();
            int i = 0;
            if (tbs[i].Is(0, Asn1.INTEGER))
                i++;
            i++; // signature
            crl.Issuer = X509Name.Parse(tbs[i++]);
            crl.ThisUpdateTag = tbs[i].Tag;
            crl.ThisUpdate = tbs[i++].ContentBytes();
            if (i < tbs.Count && (tbs[i].Is(0, Asn1.UTCTIME) || tbs[i].Is(0, Asn1.GENERALIZEDTIME)))
            {
                crl.NextUpdateTag = tbs[i].Tag;
                crl.NextUpdate = tbs[i++].ContentBytes();
            }
            if (i < tbs.Count && tbs[i].Is(0, Asn1.SEQUENCE))
            {
                foreach (var entry in tbs[i].Children())
                    crl.Revoked.Add(entry.Children()[0].ContentBytes());
            }
            return crl;
        }

        internal bool IsRevoked(X509Cert cert) => Revoked.Any(s => s.AsSpan().SequenceEqual(cert.Serial));
    }

    /// <summary>The PEM layer (crypto/pem): reading labelled blocks and writing one.</summary>
    internal static class Pem
    {
        internal sealed class Block
        {
            public string Label;
            public Dictionary<string, string> Headers = new();
            public byte[] Der;
        }

        /// <summary>
        /// PEM_read_bio: the next block from pos, skipping text outside blocks. Null at the end
        /// (PEM_R_NO_START_LINE); a block whose base64 is broken throws (PEM_R_BAD_BASE64_DECODE).
        /// </summary>
        internal static Block Next(byte[] data, ref int pos)
        {
            const string begin = "-----BEGIN ";
            while (pos < data.Length)
            {
                int lineEnd = Array.IndexOf(data, (byte)'\n', pos);
                if (lineEnd < 0)
                    lineEnd = data.Length;
                string line = Encoding.Latin1.GetString(data, pos, lineEnd - pos).TrimEnd('\r');
                pos = Math.Min(lineEnd + 1, data.Length);
                if (!line.StartsWith(begin, StringComparison.Ordinal) || !line.EndsWith("-----", StringComparison.Ordinal) || line.Length < begin.Length + 5)
                    continue;
                string label = line.Substring(begin.Length, line.Length - begin.Length - 5);
                string end = "-----END " + label + "-----";
                var block = new Block { Label = label };
                var body = new StringBuilder();
                bool inHeaders = true, ended = false;
                while (pos < data.Length)
                {
                    lineEnd = Array.IndexOf(data, (byte)'\n', pos);
                    if (lineEnd < 0)
                        lineEnd = data.Length;
                    line = Encoding.Latin1.GetString(data, pos, lineEnd - pos).TrimEnd('\r');
                    pos = Math.Min(lineEnd + 1, data.Length);
                    if (line.StartsWith("-----END ", StringComparison.Ordinal))
                    {
                        if (line != end)
                            throw new Asn1Exception("BAD_END_LINE");
                        ended = true;
                        break;
                    }
                    if (inHeaders && line.Contains(':'))
                    {
                        int colon = line.IndexOf(':');
                        block.Headers[line.Substring(0, colon)] = line.Substring(colon + 1).Trim();
                        continue;
                    }
                    if (inHeaders && line.Length == 0 && block.Headers.Count > 0)
                    {
                        inHeaders = false;
                        continue;
                    }
                    inHeaders = false;
                    body.Append(line.Trim());
                }
                if (!ended)
                    throw new Asn1Exception("BAD_END_LINE");
                try
                {
                    block.Der = Convert.FromBase64String(body.ToString());
                }
                catch (FormatException)
                {
                    throw new Asn1Exception("BAD_BASE64_DECODE");
                }
                return block;
            }
            return null;
        }

        internal static bool IsCertificateLabel(string label) =>
            label is "CERTIFICATE" or "X509 CERTIFICATE" or "TRUSTED CERTIFICATE";

        internal static bool IsPrivateKeyLabel(string label) =>
            label is "PRIVATE KEY" or "ENCRYPTED PRIVATE KEY" or "RSA PRIVATE KEY" or "EC PRIVATE KEY" or "DSA PRIVATE KEY" or "ANY PRIVATE KEY";

        /// <summary>
        /// PEM_read_bio_X509: the next certificate block (other blocks skipped). A TRUSTED
        /// CERTIFICATE carries trust settings after the certificate, which are dropped.
        /// </summary>
        internal static X509Cert ReadCertificate(byte[] data, ref int pos)
        {
            while (true)
            {
                var block = Next(data, ref pos);
                if (block == null)
                    return null;
                if (IsCertificateLabel(block.Label))
                    return X509Cert.Parse(block.Label == "TRUSTED CERTIFICATE" ? Asn1.Parse(block.Der).Encoded() : block.Der);
            }
        }

        internal static string Write(string label, byte[] der)
        {
            var text = new StringBuilder();
            text.Append("-----BEGIN ").Append(label).Append("-----\n");
            var b64 = Convert.ToBase64String(der);
            for (int i = 0; i < b64.Length; i += 64)
                text.Append(b64, i, Math.Min(64, b64.Length - i)).Append('\n');
            text.Append("-----END ").Append(label).Append("-----\n");
            return text.ToString();
        }
    }
}
