using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy.Modules.Ssl
{
    /// <summary>SSL_CIPHER: one entry of SslData.Ciphers with the algorithm masks ssl_ciph.c matches on.</summary>
    internal sealed class SslCipher
    {
        public uint Id;
        public string Name, Protocol, Description, Symmetric, Digest, Kea, Auth;
        public int StrengthBits, AlgBits;
        public bool Aead;
        public uint Mkey, AuthMask, Enc, Mac, Strength;
        public int MinTls;
        public bool Tls13 => Protocol == "TLSv1.3";
        public ushort Iana => (ushort)(Id & 0xffff);
    }

    /// <summary>
    /// ssl/ssl_ciph.c: the cipher list a rule string ("ECDH+AESGCM:!aNULL") selects
    /// (ssl_create_cipher_list, ssl_cipher_process_rulestr, ssl_cipher_apply_rule,
    /// ssl_cipher_strength_sort) and the TLS 1.3 suites in front of it.
    /// </summary>
    internal static class SslCiphers
    {
        // algorithm_mkey
        private const uint SSL_kRSA = 0x1, SSL_kDHE = 0x2, SSL_kECDHE = 0x4, SSL_kPSK = 0x8, SSL_kGOST = 0x10,
            SSL_kSRP = 0x20, SSL_kRSAPSK = 0x40, SSL_kECDHEPSK = 0x80, SSL_kDHEPSK = 0x100, SSL_kGOST18 = 0x200, SSL_kANY = 0;
        private const uint SSL_PSK = SSL_kPSK | SSL_kRSAPSK | SSL_kECDHEPSK | SSL_kDHEPSK;
        // algorithm_auth
        private const uint SSL_aRSA = 0x1, SSL_aDSS = 0x2, SSL_aNULL = 0x4, SSL_aECDSA = 0x8, SSL_aPSK = 0x10,
            SSL_aGOST01 = 0x20, SSL_aSRP = 0x40, SSL_aGOST12 = 0x80, SSL_aANY = 0;
        // algorithm_enc
        private const uint SSL_DES = 0x1, SSL_3DES = 0x2, SSL_RC4 = 0x4, SSL_RC2 = 0x8, SSL_IDEA = 0x10, SSL_eNULL = 0x20,
            SSL_AES128 = 0x40, SSL_AES256 = 0x80, SSL_CAMELLIA128 = 0x100, SSL_CAMELLIA256 = 0x200,
            SSL_eGOST2814789CNT = 0x400, SSL_SEED = 0x800, SSL_AES128GCM = 0x1000, SSL_AES256GCM = 0x2000,
            SSL_AES128CCM = 0x4000, SSL_AES256CCM = 0x8000, SSL_AES128CCM8 = 0x10000, SSL_AES256CCM8 = 0x20000,
            SSL_eGOST2814789CNT12 = 0x40000, SSL_CHACHA20POLY1305 = 0x80000, SSL_ARIA128GCM = 0x100000,
            SSL_ARIA256GCM = 0x200000, SSL_MAGMA = 0x400000, SSL_KUZNYECHIK = 0x800000;
        private const uint SSL_AESGCM = SSL_AES128GCM | SSL_AES256GCM;
        private const uint SSL_AESCCM = SSL_AES128CCM | SSL_AES256CCM | SSL_AES128CCM8 | SSL_AES256CCM8;
        private const uint SSL_AES = SSL_AES128 | SSL_AES256 | SSL_AESGCM | SSL_AESCCM;
        private const uint SSL_CAMELLIA = SSL_CAMELLIA128 | SSL_CAMELLIA256;
        private const uint SSL_CHACHA20 = SSL_CHACHA20POLY1305;
        private const uint SSL_ARIAGCM = SSL_ARIA128GCM | SSL_ARIA256GCM;
        private const uint SSL_ARIA = SSL_ARIAGCM;
        private const uint SSL_CBC = SSL_DES | SSL_3DES | SSL_RC2 | SSL_IDEA | SSL_AES128 | SSL_AES256 | SSL_CAMELLIA128
            | SSL_CAMELLIA256 | SSL_SEED;
        // algorithm_mac
        private const uint SSL_MD5 = 0x1, SSL_SHA1 = 0x2, SSL_GOST94 = 0x4, SSL_GOST89MAC = 0x8, SSL_SHA256 = 0x10,
            SSL_SHA384 = 0x20, SSL_AEAD = 0x40, SSL_GOST12_256 = 0x80, SSL_GOST89MAC12 = 0x100;
        // algo_strength
        private const uint SSL_LOW = 0x2, SSL_MEDIUM = 0x4, SSL_HIGH = 0x8, SSL_FIPS = 0x10, SSL_NOT_DEFAULT = 0x20;
        private const uint SSL_STRONG_MASK = 0x1F, SSL_DEFAULT_MASK = 0x20;

        private const int SSL3_VERSION = 0x300, TLS1_VERSION = 0x301, TLS1_2_VERSION = 0x303;

        internal static readonly SslCipher[] All;
        internal static readonly SslCipher[] Tls13Default;

        static SslCiphers()
        {
            All = SslData.Ciphers.Select(c => new SslCipher
            {
                Id = c.Id, Name = c.Name, Protocol = c.Protocol, Description = c.Description,
                StrengthBits = c.StrengthBits, AlgBits = c.AlgBits, Aead = c.Aead, Symmetric = c.Symmetric,
                Digest = c.Digest, Kea = c.Kea, Auth = c.Auth,
                Mkey = c.Kea switch
                {
                    "kx-rsa" => SSL_kRSA, "kx-dhe" => SSL_kDHE, "kx-ecdhe" => SSL_kECDHE, "kx-psk" => SSL_kPSK,
                    "kx-rsa-psk" => SSL_kRSAPSK, "kx-ecdhe-psk" => SSL_kECDHEPSK, "kx-dhe-psk" => SSL_kDHEPSK,
                    "kx-srp" => SSL_kSRP, _ => SSL_kANY,
                },
                AuthMask = c.Auth switch
                {
                    "auth-rsa" => SSL_aRSA, "auth-dss" => SSL_aDSS, "auth-null" => SSL_aNULL, "auth-ecdsa" => SSL_aECDSA,
                    "auth-psk" => SSL_aPSK, "auth-srp" => SSL_aSRP, _ => SSL_aANY,
                },
                Enc = (c.Symmetric, c.Name.Contains("CCM8")) switch
                {
                    (null, _) => SSL_eNULL,
                    ("aes-128-cbc", _) => SSL_AES128, ("aes-256-cbc", _) => SSL_AES256,
                    ("aes-128-gcm", _) => SSL_AES128GCM, ("aes-256-gcm", _) => SSL_AES256GCM,
                    ("aes-128-ccm", true) => SSL_AES128CCM8, ("aes-256-ccm", true) => SSL_AES256CCM8,
                    ("aes-128-ccm", false) => SSL_AES128CCM, ("aes-256-ccm", false) => SSL_AES256CCM,
                    ("chacha20-poly1305", _) => SSL_CHACHA20POLY1305,
                    ("camellia-128-cbc", _) => SSL_CAMELLIA128, ("camellia-256-cbc", _) => SSL_CAMELLIA256,
                    ("aria-128-gcm", _) => SSL_ARIA128GCM, ("aria-256-gcm", _) => SSL_ARIA256GCM,
                    ("des-ede3-cbc", _) => SSL_3DES, ("seed-cbc", _) => SSL_SEED,
                    _ => 0,
                },
                Mac = c.Digest switch
                {
                    null => SSL_AEAD, "md5" => SSL_MD5, "sha1" => SSL_SHA1, "sha256" => SSL_SHA256, "sha384" => SSL_SHA384, _ => 0,
                },
                MinTls = c.Protocol switch { "SSLv3" => SSL3_VERSION, "TLSv1.0" => TLS1_VERSION, "TLSv1.2" => TLS1_2_VERSION, _ => 0x304 },
                Strength = (c.Strength.Contains('H') ? SSL_HIGH : 0) | (c.Strength.Contains('M') ? SSL_MEDIUM : 0)
                    | (c.Strength.Contains('F') ? SSL_FIPS : 0) | (c.Strength.Contains('N') ? SSL_NOT_DEFAULT : 0),
            }).ToArray();
            // TLS_DEFAULT_CIPHERSUITES: TLS_AES_256_GCM_SHA384:TLS_CHACHA20_POLY1305_SHA256:TLS_AES_128_GCM_SHA256
            Tls13Default = All.Where(c => c.Tls13).ToArray();
        }

        internal static SslCipher ByIana(int iana) => All.FirstOrDefault(c => c.Iana == iana);

        // SSL_CIPHER aliases (cipher_aliases)
        private sealed record Alias(string Name, uint Mkey = 0, uint Auth = 0, uint Enc = 0, uint Mac = 0, int MinTls = 0, uint Strength = 0);

        private static readonly Alias[] cipher_aliases =
        {
            new("ALL", Enc: ~SSL_eNULL),
            new("COMPLEMENTOFALL", Enc: SSL_eNULL),
            new("COMPLEMENTOFDEFAULT", Strength: SSL_NOT_DEFAULT),
            new("kRSA", Mkey: SSL_kRSA),
            new("kEDH", Mkey: SSL_kDHE),
            new("kDHE", Mkey: SSL_kDHE),
            new("DH", Mkey: SSL_kDHE),
            new("kEECDH", Mkey: SSL_kECDHE),
            new("kECDHE", Mkey: SSL_kECDHE),
            new("ECDH", Mkey: SSL_kECDHE),
            new("kPSK", Mkey: SSL_kPSK),
            new("kRSAPSK", Mkey: SSL_kRSAPSK),
            new("kECDHEPSK", Mkey: SSL_kECDHEPSK),
            new("kDHEPSK", Mkey: SSL_kDHEPSK),
            new("kSRP", Mkey: SSL_kSRP),
            new("kGOST", Mkey: SSL_kGOST),
            new("kGOST18", Mkey: SSL_kGOST18),
            new("aRSA", Auth: SSL_aRSA),
            new("aDSS", Auth: SSL_aDSS),
            new("DSS", Auth: SSL_aDSS),
            new("aNULL", Auth: SSL_aNULL),
            new("aECDSA", Auth: SSL_aECDSA),
            new("ECDSA", Auth: SSL_aECDSA),
            new("aPSK", Auth: SSL_aPSK),
            new("aGOST01", Auth: SSL_aGOST01),
            new("aGOST12", Auth: SSL_aGOST12),
            new("aGOST", Auth: SSL_aGOST01 | SSL_aGOST12),
            new("aSRP", Auth: SSL_aSRP),
            new("EDH", Mkey: SSL_kDHE, Auth: ~SSL_aNULL),
            new("DHE", Mkey: SSL_kDHE, Auth: ~SSL_aNULL),
            new("EECDH", Mkey: SSL_kECDHE, Auth: ~SSL_aNULL),
            new("ECDHE", Mkey: SSL_kECDHE, Auth: ~SSL_aNULL),
            new("NULL", Enc: SSL_eNULL),
            new("RSA", Mkey: SSL_kRSA, Auth: SSL_aRSA),
            new("ADH", Mkey: SSL_kDHE, Auth: SSL_aNULL),
            new("AECDH", Mkey: SSL_kECDHE, Auth: SSL_aNULL),
            new("PSK", Mkey: SSL_PSK),
            new("SRP", Mkey: SSL_kSRP),
            new("3DES", Enc: SSL_3DES),
            new("RC4", Enc: SSL_RC4),
            new("RC2", Enc: SSL_RC2),
            new("IDEA", Enc: SSL_IDEA),
            new("SEED", Enc: SSL_SEED),
            new("eNULL", Enc: SSL_eNULL),
            new("GOST89", Enc: SSL_eGOST2814789CNT | SSL_eGOST2814789CNT12 | SSL_MAGMA | SSL_KUZNYECHIK),
            new("AES128", Enc: SSL_AES128 | SSL_AES128GCM | SSL_AES128CCM | SSL_AES128CCM8),
            new("AES256", Enc: SSL_AES256 | SSL_AES256GCM | SSL_AES256CCM | SSL_AES256CCM8),
            new("AES", Enc: SSL_AES),
            new("AESGCM", Enc: SSL_AES128GCM | SSL_AES256GCM),
            new("AESCCM", Enc: SSL_AES128CCM | SSL_AES256CCM | SSL_AES128CCM8 | SSL_AES256CCM8),
            new("AESCCM8", Enc: SSL_AES128CCM8 | SSL_AES256CCM8),
            new("CAMELLIA128", Enc: SSL_CAMELLIA128),
            new("CAMELLIA256", Enc: SSL_CAMELLIA256),
            new("CAMELLIA", Enc: SSL_CAMELLIA),
            new("CHACHA20", Enc: SSL_CHACHA20),
            new("GOST2012-GOST8912-GOST8912", Enc: SSL_eGOST2814789CNT12),
            new("ARIA", Enc: SSL_ARIA),
            new("ARIAGCM", Enc: SSL_ARIA128GCM | SSL_ARIA256GCM),
            new("ARIA128", Enc: SSL_ARIA128GCM),
            new("ARIA256", Enc: SSL_ARIA256GCM),
            new("CBC", Enc: SSL_CBC),
            new("MD5", Mac: SSL_MD5),
            new("SHA1", Mac: SSL_SHA1),
            new("SHA", Mac: SSL_SHA1),
            new("GOST94", Mac: SSL_GOST94),
            new("GOST89MAC", Mac: SSL_GOST89MAC | SSL_GOST89MAC12),
            new("SHA256", Mac: SSL_SHA256),
            new("SHA384", Mac: SSL_SHA384),
            new("GOST12", Mac: SSL_GOST12_256),
            new("SSLv3", MinTls: SSL3_VERSION),
            new("TLSv1", MinTls: TLS1_VERSION),
            new("TLSv1.0", MinTls: TLS1_VERSION),
            new("TLSv1.2", MinTls: TLS1_2_VERSION),
            new("LOW", Strength: SSL_LOW),
            new("MEDIUM", Strength: SSL_MEDIUM),
            new("HIGH", Strength: SSL_HIGH),
            new("FIPS", Enc: ~SSL_eNULL, Strength: SSL_FIPS),
        };

        private enum Rule { Add, Kill, Del, Ord, Bump, Special }

        private sealed class Node
        {
            public SslCipher Cipher;
            public bool Active;
        }

        /// <summary>
        /// ssl_cipher_apply_rule: the ciphers that match move (ADD: inactive to the end, ORD: active to
        /// the end, DEL: active off to the front, KILL: out, BUMP: active to the front).
        /// </summary>
        private static void ssl_cipher_apply_rule(uint cipher_id, uint alg_mkey, uint alg_auth, uint alg_enc, uint alg_mac,
            int min_tls, uint algo_strength, Rule rule, int strength_bits, List<Node> list)
        {
            bool reverse = rule == Rule.Del || rule == Rule.Bump;
            var order = reverse ? list.AsEnumerable().Reverse().ToList() : list.ToList();
            foreach (var curr in order)
            {
                var cp = curr.Cipher;
                if (strength_bits >= 0)
                {
                    if (strength_bits != cp.StrengthBits)
                        continue;
                }
                else
                {
                    if (cipher_id != 0 && cipher_id != cp.Id)
                        continue;
                    if (alg_mkey != 0 && (alg_mkey & cp.Mkey) == 0)
                        continue;
                    if (alg_auth != 0 && (alg_auth & cp.AuthMask) == 0)
                        continue;
                    if (alg_enc != 0 && (alg_enc & cp.Enc) == 0)
                        continue;
                    if (alg_mac != 0 && (alg_mac & cp.Mac) == 0)
                        continue;
                    if (min_tls != 0 && min_tls != cp.MinTls)
                        continue;
                    if ((algo_strength & SSL_STRONG_MASK) != 0 && (algo_strength & SSL_STRONG_MASK & cp.Strength) == 0)
                        continue;
                    if ((algo_strength & SSL_DEFAULT_MASK) != 0 && (algo_strength & SSL_DEFAULT_MASK & cp.Strength) == 0)
                        continue;
                }
                switch (rule)
                {
                    case Rule.Add:
                        if (!curr.Active)
                        {
                            list.Remove(curr);
                            list.Add(curr);
                            curr.Active = true;
                        }
                        break;
                    case Rule.Ord:
                        if (curr.Active)
                        {
                            list.Remove(curr);
                            list.Add(curr);
                        }
                        break;
                    case Rule.Del:
                        if (curr.Active)
                        {
                            list.Remove(curr);
                            list.Insert(0, curr);
                            curr.Active = false;
                        }
                        break;
                    case Rule.Bump:
                        if (curr.Active)
                        {
                            list.Remove(curr);
                            list.Insert(0, curr);
                        }
                        break;
                    case Rule.Kill:
                        list.Remove(curr);
                        break;
                }
            }
        }

        /// <summary>ssl_cipher_strength_sort: the active ciphers by descending strength_bits.</summary>
        private static void ssl_cipher_strength_sort(List<Node> list)
        {
            int max = 0;
            foreach (var n in list)
                if (n.Active && n.Cipher.StrengthBits > max)
                    max = n.Cipher.StrengthBits;
            for (int i = max; i >= 0; i--)
                ssl_cipher_apply_rule(0, 0, 0, 0, 0, 0, 0, Rule.Ord, i, list);
        }

        private static bool IsRuleChar(char ch) =>
            (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'z') || ch == '-' || ch == '_' || ch == '.' || ch == '=';

        private static bool IsSeparator(char ch) => ch == ':' || ch == ' ' || ch == ';' || ch == ',';

        /// <summary>ssl_cipher_process_rulestr: false on a malformed rule string (SSL_R_INVALID_COMMAND).</summary>
        private static bool ssl_cipher_process_rulestr(string rule_str, List<Node> list, ref int seclevel)
        {
            int l = 0;
            bool retval = true;
            while (true)
            {
                if (l >= rule_str.Length)
                    break;
                char ch = rule_str[l];
                Rule rule;
                if (ch == '-') { rule = Rule.Del; l++; }
                else if (ch == '+') { rule = Rule.Ord; l++; }
                else if (ch == '!') { rule = Rule.Kill; l++; }
                else if (ch == '@') { rule = Rule.Special; l++; }
                else rule = Rule.Add;

                if (l < rule_str.Length && IsSeparator(rule_str[l]))
                {
                    l++;
                    continue;
                }

                uint alg_mkey = 0, alg_auth = 0, alg_enc = 0, alg_mac = 0, algo_strength = 0, cipher_id = 0;
                int min_tls = 0;
                bool found = false;
                string buf = null;
                while (true)
                {
                    int start = l;
                    while (l < rule_str.Length && IsRuleChar(rule_str[l]))
                        l++;
                    int buflen = l - start;
                    if (buflen == 0)
                    {
                        // We hit something we cannot deal with, it is no command or separator nor
                        // alphanumeric, so we call this an error.
                        return false;
                    }
                    buf = rule_str.Substring(start, buflen);
                    if (rule == Rule.Special)
                    {
                        found = false; /* unused -- avoid compiler warning */
                        break; /* special treatment */
                    }
                    bool multi = l < rule_str.Length && rule_str[l] == '+';
                    if (multi)
                        l++;

                    // Now search for the cipher alias in the ca_list.
                    SslCipher exact = All.FirstOrDefault(c => !c.Tls13 && c.Name == buf);
                    Alias alias = exact == null ? cipher_aliases.FirstOrDefault(a => a.Name == buf) : null;
                    if (exact == null && alias == null)
                    {
                        found = false;
                        // ignore rest of the elements of this multi-part rule
                        if (multi)
                        {
                            while (l < rule_str.Length && (IsRuleChar(rule_str[l]) || rule_str[l] == '+'))
                                l++;
                        }
                        break;
                    }
                    found = true;
                    if (exact != null)
                    {
                        // a cipher's own name: its id (and nothing else of this element is combined)
                        cipher_id = exact.Id;
                    }
                    else
                    {
                        if (alias.Mkey != 0)
                        {
                            if (alg_mkey != 0) { alg_mkey &= alias.Mkey; if (alg_mkey == 0) { found = false; break; } }
                            else alg_mkey = alias.Mkey;
                        }
                        if (alias.Auth != 0)
                        {
                            if (alg_auth != 0) { alg_auth &= alias.Auth; if (alg_auth == 0) { found = false; break; } }
                            else alg_auth = alias.Auth;
                        }
                        if (alias.Enc != 0)
                        {
                            if (alg_enc != 0) { alg_enc &= alias.Enc; if (alg_enc == 0) { found = false; break; } }
                            else alg_enc = alias.Enc;
                        }
                        if (alias.Mac != 0)
                        {
                            if (alg_mac != 0) { alg_mac &= alias.Mac; if (alg_mac == 0) { found = false; break; } }
                            else alg_mac = alias.Mac;
                        }
                        if ((alias.Strength & SSL_STRONG_MASK) != 0)
                        {
                            if ((algo_strength & SSL_STRONG_MASK) != 0)
                            {
                                algo_strength &= (alias.Strength & SSL_STRONG_MASK) | ~SSL_STRONG_MASK;
                                if ((algo_strength & SSL_STRONG_MASK) == 0) { found = false; break; }
                            }
                            else algo_strength = alias.Strength & SSL_STRONG_MASK;
                        }
                        if ((alias.Strength & SSL_DEFAULT_MASK) != 0)
                        {
                            if ((algo_strength & SSL_DEFAULT_MASK) != 0)
                            {
                                algo_strength &= (alias.Strength & SSL_DEFAULT_MASK) | ~SSL_DEFAULT_MASK;
                                if ((algo_strength & SSL_DEFAULT_MASK) == 0) { found = false; break; }
                            }
                            else algo_strength |= alias.Strength & SSL_DEFAULT_MASK;
                        }
                        if (alias.MinTls != 0)
                        {
                            if (min_tls != 0 && min_tls != alias.MinTls) { found = false; break; }
                            min_tls = alias.MinTls;
                        }
                    }
                    if (!multi)
                        break;
                }

                if (rule == Rule.Special)
                {
                    bool ok = false;
                    if (buf == "STRENGTH")
                    {
                        ssl_cipher_strength_sort(list);
                        ok = true;
                    }
                    else if (buf.StartsWith("SECLEVEL=", StringComparison.Ordinal))
                    {
                        string level = buf.Substring(9);
                        if (level.Length == 1 && level[0] >= '0' && level[0] <= '5')
                        {
                            seclevel = level[0] - '0';
                            ok = true;
                        }
                    }
                    if (!ok)
                        retval = false;
                    // We do not support any "multi" options together with "@", so throw away the rest.
                    while (l < rule_str.Length && !IsSeparator(rule_str[l]))
                        l++;
                }
                else if (found)
                {
                    ssl_cipher_apply_rule(cipher_id, alg_mkey, alg_auth, alg_enc, alg_mac, min_tls, algo_strength, rule, -1, list);
                }
                else
                {
                    while (l < rule_str.Length && !IsSeparator(rule_str[l]))
                        l++;
                }
                if (l < rule_str.Length && IsSeparator(rule_str[l]))
                    l++;
            }
            return retval;
        }

        /// <summary>
        /// ssl_create_cipher_list: the TLS 1.3 suites, then the ciphers the rule string selects,
        /// or null when the string is malformed or selects no TLS 1.2-and-below cipher
        /// (SSL_CTX_set_cipher_list: SSL_R_NO_CIPHER_MATCH).
        /// </summary>
        internal static List<SslCipher> ssl_create_cipher_list(string rule_str, IReadOnlyList<SslCipher> tls13, ref int seclevel)
        {
            // The starting order: ssl_cipher_collect_ciphers and the preference rules leave the
            // ciphers in the order of SslData (all of them, inactive).
            var list = All.Where(c => !c.Tls13).Select(c => new Node { Cipher = c }).ToList();
            string rule_p = rule_str;
            int newSeclevel = seclevel;
            if (rule_p.StartsWith("DEFAULT", StringComparison.Ordinal))
            {
                // OSSL_default_cipher_list
                if (!ssl_cipher_process_rulestr("ALL:!COMPLEMENTOFDEFAULT:!eNULL", list, ref newSeclevel))
                    return null;
                rule_p = rule_p.Substring(7);
                if (rule_p.Length > 0 && rule_p[0] == ':')
                    rule_p = rule_p.Substring(1);
            }
            if (rule_p.Length > 0 && !ssl_cipher_process_rulestr(rule_p, list, ref newSeclevel))
                return null;
            var selected = list.Where(n => n.Active).Select(n => n.Cipher).ToList();
            if (selected.Count == 0)
                return null;
            seclevel = newSeclevel;
            var result = new List<SslCipher>(tls13);
            result.AddRange(selected);
            return result;
        }
    }
}
