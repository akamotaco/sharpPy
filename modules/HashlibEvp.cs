using System;
using System.Collections.Generic;
using static System.Numerics.BitOperations;

namespace SharpPy.Modules
{
    /// <summary>
    /// The part of libcrypto that Modules/_hashopenssl.c calls, as the reference CPython links it
    /// (OpenSSL 3.x, default provider), written over SharpPy's own digest states (HashModules.cs)
    /// so that every platform - Windows, Linux, macOS, Godot - gives the same answers:
    /// EVP_MD_fetch by provider name, EVP_MD_CTX, HMAC_CTX and HMAC(), PKCS5_PBKDF2_HMAC,
    /// EVP_PBE_scrypt (RFC 7914) and CRYPTO_memcmp, with the error queue that raise_ssl_error reads.
    /// </summary>
    internal static class Evp
    {
        // ---- ERR: the thread's error queue, of which _hashopenssl.c reads the last entry ----

        internal const string ERR_LIB_EVP = "digital envelope routines";
        internal const string ERR_LIB_PROV = "Provider routines";

        [ThreadStatic] private static List<(string Lib, string Reason)> _errors;

        /// <summary>ERR_raise(lib, reason)</summary>
        internal static void ErrRaise(string lib, string reason) => (_errors ??= new()).Add((lib, reason));

        /// <summary>ERR_peek_last_error: false when the queue is empty.</summary>
        internal static bool ErrPeekLastError(out string lib, out string reason)
        {
            if (_errors is { Count: > 0 } errors)
            {
                (lib, reason) = errors[^1];
                return true;
            }
            lib = reason = null;
            return false;
        }

        /// <summary>ERR_clear_error</summary>
        internal static void ErrClearError() => _errors?.Clear();

        // ---- EVP_MD: the digests of the default provider ----

        /// <summary>
        /// EVP_MD: one algorithm of the provider. Names are what EVP_MD_fetch matches (the
        /// provider's names and OID, without regard to case); Sn and Ln are OBJ_nid2sn and
        /// OBJ_nid2ln of its NID, null for one without a NID (NID_undef: SHA2-256/192, KECCAK-*,
        /// KECCAK-KMAC-*, NULL). Size is EVP_MD_get_size (-1 for SHAKE, which has no default
        /// output length), BlockSize EVP_MD_get_block_size, Xof EVP_MD_FLAG_XOF.
        /// </summary>
        internal sealed class Md
        {
            internal readonly string[] Names;
            internal readonly string Sn, Ln;
            internal readonly int Size, BlockSize;
            internal readonly bool Xof;
            private readonly Func<DigestState> _init;

            internal Md(string[] names, string sn, string ln, int size, int blockSize, bool xof, Func<DigestState> init)
            {
                Names = names;
                Sn = sn;
                Ln = ln;
                Size = size;
                BlockSize = blockSize;
                Xof = xof;
                _init = init;
            }

            /// <summary>The digest's init function: a fresh state.</summary>
            internal DigestState Init() => _init();
        }

        private static Md Keccak(string[] names, string sn, string ln, int size, int rate, bool xof, byte suffix) =>
            new(names, sn, ln, size, rate, xof, () => new KeccakState(rate, Math.Max(size, 0), suffix));

        private static Md MdFamilyDigest(string[] names, string sn, string ln, MdFamily family, int size, byte[] iv) =>
            new(names, sn, ln, size, family == MdFamily.SHA512 ? 128 : 64, false, () => new MdState(family, size, iv));

        /// <summary>
        /// The digests EVP_MD_do_all_provided(NULL, ...) reaches in the reference's default
        /// provider (`openssl list -digest-algorithms`, OpenSSL 3.6), with their names.
        /// </summary>
        internal static readonly Md[] Provided =
        {
            Keccak(new[] { "SHA3-512", "2.16.840.1.101.3.4.2.10" }, "SHA3-512", "sha3-512", 64, 72, false, 0x06),
            MdFamilyDigest(new[] { "SHA2-512", "SHA-512", "SHA512", "2.16.840.1.101.3.4.2.3" }, "SHA512", "sha512", MdFamily.SHA512, 64, SharpPy.Modules.Md.SHA512_IV),
            MdFamilyDigest(new[] { "SHA2-512/256", "SHA-512/256", "SHA512-256", "2.16.840.1.101.3.4.2.6" }, "SHA512-256", "sha512-256", MdFamily.SHA512, 32, SharpPy.Modules.Md.SHA512_256_IV),
            MdFamilyDigest(new[] { "SHA2-224", "SHA-224", "SHA224", "2.16.840.1.101.3.4.2.4" }, "SHA224", "sha224", MdFamily.SHA256, 28, SharpPy.Modules.Md.SHA224_IV),
            MdFamilyDigest(new[] { "SHA1", "SHA-1", "SSL3-SHA1", "1.3.14.3.2.26" }, "SHA1", "sha1", MdFamily.SHA1, 20, SharpPy.Modules.Md.SHA1_IV),
            Keccak(new[] { "SHA3-224", "2.16.840.1.101.3.4.2.7" }, "SHA3-224", "sha3-224", 28, 144, false, 0x06),
            Keccak(new[] { "SHA3-384", "2.16.840.1.101.3.4.2.9" }, "SHA3-384", "sha3-384", 48, 104, false, 0x06),
            MdFamilyDigest(new[] { "RIPEMD-160", "RIPEMD160", "RIPEMD", "RMD160", "1.3.36.3.2.1" }, "RIPEMD160", "ripemd160", MdFamily.RIPEMD160, 20, SharpPy.Modules.Md.RIPEMD160_IV),
            MdFamilyDigest(new[] { "SHA2-384", "SHA-384", "SHA384", "2.16.840.1.101.3.4.2.2" }, "SHA384", "sha384", MdFamily.SHA512, 48, SharpPy.Modules.Md.SHA384_IV),
            MdFamilyDigest(new[] { "SHA2-512/224", "SHA-512/224", "SHA512-224", "2.16.840.1.101.3.4.2.5" }, "SHA512-224", "sha512-224", MdFamily.SHA512, 28, SharpPy.Modules.Md.SHA512_224_IV),
            Keccak(new[] { "SHAKE-256", "SHAKE256", "2.16.840.1.101.3.4.2.12" }, "SHAKE256", "shake256", -1, 136, true, 0x1f),
            MdFamilyDigest(new[] { "SM3", "1.2.156.10197.1.401" }, "SM3", "sm3", MdFamily.SM3, 32, SharpPy.Modules.Md.SM3_IV),
            Keccak(new[] { "SHA3-256", "2.16.840.1.101.3.4.2.8" }, "SHA3-256", "sha3-256", 32, 136, false, 0x06),
            MdFamilyDigest(new[] { "MD5", "SSL3-MD5", "1.2.840.113549.2.5" }, "MD5", "md5", MdFamily.MD5, 16, SharpPy.Modules.Md.MD5_IV),
            new(new[] { "BLAKE2S-256", "BLAKE2s256", "1.3.6.1.4.1.1722.12.2.2.8" }, "BLAKE2s256", "blake2s256", 32, 64, false, () => Blake2State.Plain(false)),
            MdFamilyDigest(new[] { "SHA2-256", "SHA-256", "SHA256", "2.16.840.1.101.3.4.2.1" }, "SHA256", "sha256", MdFamily.SHA256, 32, SharpPy.Modules.Md.SHA256_IV),
            new(new[] { "BLAKE2B-512", "BLAKE2b512", "1.3.6.1.4.1.1722.12.2.1.16" }, "BLAKE2b512", "blake2b512", 64, 128, false, () => Blake2State.Plain(true)),
            new(new[] { "MD5-SHA1" }, "MD5-SHA1", "md5-sha1", 36, 64, false, () => new Md5Sha1State()),
            Keccak(new[] { "SHAKE-128", "SHAKE128", "2.16.840.1.101.3.4.2.11" }, "SHAKE128", "shake128", -1, 168, true, 0x1f),
            // SP 800-208's SHA-256/192: SHA-256 cut to 24 bytes.
            MdFamilyDigest(new[] { "SHA2-256/192", "SHA-256/192", "SHA256-192" }, null, null, MdFamily.SHA256, 24, SharpPy.Modules.Md.SHA256_IV),
            Keccak(new[] { "KECCAK-224" }, null, null, 28, 144, false, 0x01),
            Keccak(new[] { "KECCAK-256" }, null, null, 32, 136, false, 0x01),
            Keccak(new[] { "KECCAK-384" }, null, null, 48, 104, false, 0x01),
            Keccak(new[] { "KECCAK-512" }, null, null, 64, 72, false, 0x01),
            // The Keccak of KMAC (cSHAKE's 0x04 padding), whose default length is KMAC's: 2 * bits / 8.
            Keccak(new[] { "KECCAK-KMAC-128", "KECCAK-KMAC128" }, null, null, 32, 168, true, 0x04),
            Keccak(new[] { "KECCAK-KMAC-256", "KECCAK-KMAC256" }, null, null, 64, 136, true, 0x04),
            new(new[] { "NULL" }, null, null, 0, 0, false, () => new NullState()),
        };

        /// <summary>
        /// EVP_MD_fetch(NULL, algorithm, properties): a name the provider registered, without regard
        /// to case (ossl_namemap). An unknown one queues EVP_R_UNSUPPORTED ("unsupported"). The
        /// "-fips" property of usedforsecurity=False selects the same default provider.
        /// </summary>
        internal static Md Fetch(string algorithm)
        {
            foreach (var md in Provided)
                foreach (var name in md.Names)
                    if (string.Equals(name, algorithm, StringComparison.OrdinalIgnoreCase))
                        return md;
            ErrRaise(ERR_LIB_EVP, "unsupported");
            return null;
        }

        /// <summary>MD5-SHA1 (libcrypto's md5_sha1.c): both digests of the same input, MD5's first.</summary>
        private sealed class Md5Sha1State : DigestState
        {
            private readonly DigestState _md5, _sha1;

            internal Md5Sha1State() : this(new MdState(MdFamily.MD5, 16, SharpPy.Modules.Md.MD5_IV), new MdState(MdFamily.SHA1, 20, SharpPy.Modules.Md.SHA1_IV)) { }

            private Md5Sha1State(DigestState md5, DigestState sha1)
            {
                _md5 = md5;
                _sha1 = sha1;
            }

            internal override int DigestSize => 36;
            internal override int BlockSize => 64;
            internal override DigestState Clone() => new Md5Sha1State(_md5.Clone(), _sha1.Clone());

            internal override void Update(ReadOnlySpan<byte> data)
            {
                _md5.Update(data);
                _sha1.Update(data);
            }

            internal override byte[] Digest()
            {
                var digest = new byte[36];
                _md5.Digest().CopyTo(digest, 0);
                _sha1.Digest().CopyTo(digest, 16);
                return digest;
            }
        }

        /// <summary>The NULL digest (libcrypto's null_prov): no output, no block.</summary>
        private sealed class NullState : DigestState
        {
            internal override int DigestSize => 0;
            internal override int BlockSize => 0;
            internal override DigestState Clone() => new NullState();
            internal override void Update(ReadOnlySpan<byte> data) { }
            internal override byte[] Digest() => Array.Empty<byte>();
        }

        // ---- EVP_MD_CTX ----

        /// <summary>EVP_MD_CTX: the digest it was initialised with and the algorithm's state.</summary>
        internal sealed class MdCtx
        {
            internal Md Digest;
            internal DigestState State;

            /// <summary>EVP_DigestInit_ex</summary>
            internal bool DigestInit(Md md)
            {
                Digest = md;
                State = md.Init();
                return true;
            }

            /// <summary>EVP_DigestUpdate</summary>
            internal bool DigestUpdate(ReadOnlySpan<byte> data)
            {
                State.Update(data);
                return true;
            }

            /// <summary>EVP_MD_CTX_copy(out, in)</summary>
            internal bool CopyFrom(MdCtx other)
            {
                Digest = other.Digest;
                State = other.State.Clone();
                return true;
            }

            /// <summary>
            /// EVP_DigestFinal: EVP_MD_CTX_get_size first, and a digest without one (SHAKE) fails
            /// with nothing queued. The context is not used again.
            /// </summary>
            internal byte[] DigestFinal()
            {
                if (Digest.Size < 0)
                    return null;
                var digest = State.Digest();
                return digest.Length == Digest.Size ? digest : digest.AsSpan(0, Digest.Size).ToArray();
            }

            /// <summary>EVP_DigestFinalXOF</summary>
            internal byte[] DigestFinalXOF(int length) => ((KeccakState)State).Squeeze(length);
        }

        // ---- HMAC ----

        // HMAC_MAX_MD_CBLOCK_SIZE: the longest block, SHA3-224's.
        private const int HMAC_MAX_MD_CBLOCK_SIZE = 144;

        /// <summary>HMAC_CTX: the inner and outer contexts after the padded key, and the running one.</summary>
        internal sealed class HmacCtx
        {
            internal Md Digest;
            private MdCtx _iCtx, _oCtx, _mdCtx;

            /// <summary>
            /// HMAC_Init_ex(ctx, key, len, md, NULL): an XOF is refused (with nothing queued); a key
            /// longer than the block is digested first; the key, zero-padded, xor 0x36 and 0x5c.
            /// </summary>
            internal bool Init(ReadOnlySpan<byte> key, Md md)
            {
                if (md.Xof)
                    return false;
                Digest = md;
                int j = md.BlockSize;
                var keytmp = new byte[HMAC_MAX_MD_CBLOCK_SIZE];
                if (j < key.Length)
                {
                    var ctx = new MdCtx();
                    ctx.DigestInit(md);
                    ctx.DigestUpdate(key);
                    var digested = ctx.DigestFinal();
                    if (digested == null)
                        return false;
                    digested.CopyTo(keytmp, 0);
                }
                else
                    key.CopyTo(keytmp);
                var pad = new byte[HMAC_MAX_MD_CBLOCK_SIZE];
                for (int i = 0; i < pad.Length; i++)
                    pad[i] = (byte)(0x36 ^ keytmp[i]);
                _iCtx = new MdCtx();
                _iCtx.DigestInit(md);
                _iCtx.DigestUpdate(pad.AsSpan(0, j));
                for (int i = 0; i < pad.Length; i++)
                    pad[i] = (byte)(0x5c ^ keytmp[i]);
                _oCtx = new MdCtx();
                _oCtx.DigestInit(md);
                _oCtx.DigestUpdate(pad.AsSpan(0, j));
                _mdCtx = new MdCtx();
                _mdCtx.CopyFrom(_iCtx);
                return true;
            }

            /// <summary>HMAC_Update</summary>
            internal bool Update(ReadOnlySpan<byte> data) => _mdCtx.DigestUpdate(data);

            /// <summary>HMAC_CTX_copy(dctx, sctx)</summary>
            internal bool CopyFrom(HmacCtx other)
            {
                Digest = other.Digest;
                _iCtx = new MdCtx();
                _iCtx.CopyFrom(other._iCtx);
                _oCtx = new MdCtx();
                _oCtx.CopyFrom(other._oCtx);
                _mdCtx = new MdCtx();
                _mdCtx.CopyFrom(other._mdCtx);
                return true;
            }

            /// <summary>HMAC_Final: the inner digest, then the outer context over it.</summary>
            internal byte[] Final()
            {
                var inner = _mdCtx.DigestFinal();
                if (inner == null)
                    return null;
                _mdCtx.CopyFrom(_oCtx);
                _mdCtx.DigestUpdate(inner);
                return _mdCtx.DigestFinal();
            }

            /// <summary>
            /// Init followed by Final for each block of PBKDF2: the keyed inner and outer contexts are
            /// copied rather than rebuilt (HMAC_Init_ex(ctx, NULL, 0, NULL, NULL)).
            /// </summary>
            internal void Reset() => _mdCtx.CopyFrom(_iCtx);
        }

        /// <summary>
        /// HMAC(evp_md, key, key_len, data, data_len, md, md_len): EVP_Q_mac's HMAC, and nothing
        /// (with nothing queued) for a digest without a positive size or an XOF.
        /// </summary>
        internal static byte[] Hmac(Md md, ReadOnlySpan<byte> key, ReadOnlySpan<byte> data)
        {
            if (md.Size <= 0)
                return null;
            var ctx = new HmacCtx();
            if (!ctx.Init(key, md))
                return null;
            ctx.Update(data);
            return ctx.Final();
        }

        // ---- PBKDF2 (crypto/evp/p5_crpt2.c over providers/implementations/kdfs/pbkdf2.c) ----

        /// <summary>
        /// PKCS5_PBKDF2_HMAC: the PBKDF2 KDF with pkcs5 mode (no lower bounds). An XOF digest is
        /// PROV_R_XOF_DIGESTS_NOT_ALLOWED; one without a positive size fails with nothing queued.
        /// </summary>
        internal static bool Pkcs5Pbkdf2Hmac(ReadOnlySpan<byte> pass, ReadOnlySpan<byte> salt, int iter, Md digest, byte[] output)
        {
            if (digest.Xof)
            {
                ErrRaise(ERR_LIB_PROV, "xof digests not allowed");
                return false;
            }
            int mdlen = digest.Size;
            if (mdlen <= 0)
                return false;
            var hctxTpl = new HmacCtx();
            if (!hctxTpl.Init(pass, digest))
                return false;
            var hctx = new HmacCtx();
            var counter = new byte[4];
            int tkeylen = output.Length, offset = 0;
            uint i = 1;
            while (tkeylen > 0)
            {
                int cplen = Math.Min(tkeylen, mdlen);
                // T_i = U_1 ^ U_2 ^ ... ^ U_iter, U_1 = PRF(P, S || INT(i)), U_j = PRF(P, U_{j-1})
                counter[0] = (byte)(i >> 24);
                counter[1] = (byte)(i >> 16);
                counter[2] = (byte)(i >> 8);
                counter[3] = (byte)i;
                hctx.CopyFrom(hctxTpl);
                hctx.Update(salt);
                hctx.Update(counter);
                var digtmp = hctx.Final();
                Array.Copy(digtmp, 0, output, offset, cplen);
                for (int j = 1; j < iter; j++)
                {
                    hctx.Reset();
                    hctx.Update(digtmp);
                    digtmp = hctx.Final();
                    for (int k = 0; k < cplen; k++)
                        output[offset + k] ^= digtmp[k];
                }
                tkeylen -= cplen;
                i++;
                offset += cplen;
            }
            return true;
        }

        // ---- scrypt (providers/implementations/kdfs/scrypt.c, RFC 7914) ----

        // SCRYPT_PR_MAX, LOG2_UINT64_MAX and SCRYPT_MAX_MEM (EVP_PBE_scrypt's default maxmem)
        private const ulong SCRYPT_PR_MAX = (1UL << 30) - 1;
        private const int LOG2_UINT64_MAX = 63;
        private const ulong SCRYPT_MAX_MEM = 1024 * 1024 * 32;

        /// <summary>
        /// EVP_PBE_scrypt: r and p past UINT32_MAX are EVP_R_PARAMETER_TOO_LARGE; maxmem 0 is 32 MiB;
        /// N, r and p are checked by the KDF's parameters (silently), then scrypt_alg's memory checks
        /// (EVP_R_MEMORY_LIMIT_EXCEEDED). With no key, valid parameters are all that is asked.
        /// </summary>
        internal static bool PbeScrypt(ReadOnlySpan<byte> pass, ReadOnlySpan<byte> salt, ulong N, ulong r, ulong p, ulong maxmem, byte[] key)
        {
            if (r > uint.MaxValue || p > uint.MaxValue)
            {
                ErrRaise(ERR_LIB_EVP, "parameter too large");
                return false;
            }
            if (maxmem == 0)
                maxmem = SCRYPT_MAX_MEM;
            // kdf_scrypt_set_ctx_params: N > 1 and a power of 2, r >= 1, p >= 1
            if (N <= 1 || (N & (N - 1)) != 0 || r < 1 || p < 1)
                return false;
            return ScryptAlg(pass, salt, N, r, p, maxmem, key);
        }

        /// <summary>scrypt_alg</summary>
        private static bool ScryptAlg(ReadOnlySpan<byte> pass, ReadOnlySpan<byte> salt, ulong N, ulong r, ulong p, ulong maxmem, byte[] key)
        {
            /* Sanity check parameters */
            /* initial check, r,p must be non zero, N >= 2 and a power of 2 */
            if (r == 0 || p == 0 || N < 2 || (N & (N - 1)) != 0)
                return false;
            /* Check p * r < SCRYPT_PR_MAX avoiding overflow */
            if (p > SCRYPT_PR_MAX / r)
            {
                ErrRaise(ERR_LIB_EVP, "memory limit exceeded");
                return false;
            }
            /* Need to check N: if 2^(128 * r / 8) overflows limit this is automatically satisfied since N <= UINT64_MAX. */
            if (16 * r <= LOG2_UINT64_MAX)
            {
                if (N >= 1UL << (int)(16 * r))
                {
                    ErrRaise(ERR_LIB_EVP, "memory limit exceeded");
                    return false;
                }
            }
            /* B size in section 5 step 1.S; we know p * 128 * r < UINT64_MAX because p * r < SCRYPT_PR_MAX */
            ulong Blen = p * 128 * r;
            /* Yet we pass it as integer to PKCS5_PBKDF2_HMAC */
            if (Blen > int.MaxValue)
            {
                ErrRaise(ERR_LIB_EVP, "memory limit exceeded");
                return false;
            }
            /* Check 32 * r * (N + 2) * sizeof(uint32_t) fits in uint64_t: the combined size of V, X and T */
            ulong i = ulong.MaxValue / (32 * sizeof(uint));
            if (N + 2 > i / r)
            {
                ErrRaise(ERR_LIB_EVP, "memory limit exceeded");
                return false;
            }
            ulong Vlen = 32 * r * (N + 2) * sizeof(uint);
            /* check total allocated size fits in uint64_t */
            if (Blen > ulong.MaxValue - Vlen)
            {
                ErrRaise(ERR_LIB_EVP, "memory limit exceeded");
                return false;
            }
            if (Blen + Vlen > maxmem)
            {
                ErrRaise(ERR_LIB_EVP, "memory limit exceeded");
                return false;
            }
            /* If no key return to indicate parameters are OK */
            if (key == null)
                return true;

            // B, then X, T and V as 32-bit words (one allocation of Blen + Vlen in libcrypto).
            ulong vWords = 32 * r * N;
            var B = new byte[Blen];
            var sha256 = Fetch("SHA256");
            if (!Pkcs5Pbkdf2Hmac(pass, salt, 1, sha256, B))
                goto err;
            var X = new uint[32 * r];
            var T = new uint[32 * r];
            var V = new uint[vWords];
            for (ulong k = 0; k < p; k++)
                ScryptROMix(B, (int)(128 * r * k), (int)r, N, X, T, V);
            if (!Pkcs5Pbkdf2Hmac(pass, B, 1, sha256, key))
                goto err;
            Array.Clear(B);
            Array.Clear(V);
            return true;
        err:
            ErrRaise(ERR_LIB_EVP, "pbkdf2 error");
            return false;
        }

        /// <summary>salsa208_word_specification: the Salsa20/8 core over sixteen words, in place.</summary>
        private static void Salsa208WordSpecification(Span<uint> inout)
        {
            Span<uint> x = stackalloc uint[16];
            inout.CopyTo(x);
            for (int i = 8; i > 0; i -= 2)
            {
                x[4] ^= RotateLeft(x[0] + x[12], 7);
                x[8] ^= RotateLeft(x[4] + x[0], 9);
                x[12] ^= RotateLeft(x[8] + x[4], 13);
                x[0] ^= RotateLeft(x[12] + x[8], 18);
                x[9] ^= RotateLeft(x[5] + x[1], 7);
                x[13] ^= RotateLeft(x[9] + x[5], 9);
                x[1] ^= RotateLeft(x[13] + x[9], 13);
                x[5] ^= RotateLeft(x[1] + x[13], 18);
                x[14] ^= RotateLeft(x[10] + x[6], 7);
                x[2] ^= RotateLeft(x[14] + x[10], 9);
                x[6] ^= RotateLeft(x[2] + x[14], 13);
                x[10] ^= RotateLeft(x[6] + x[2], 18);
                x[3] ^= RotateLeft(x[15] + x[11], 7);
                x[7] ^= RotateLeft(x[3] + x[15], 9);
                x[11] ^= RotateLeft(x[7] + x[3], 13);
                x[15] ^= RotateLeft(x[11] + x[7], 18);
                x[1] ^= RotateLeft(x[0] + x[3], 7);
                x[2] ^= RotateLeft(x[1] + x[0], 9);
                x[3] ^= RotateLeft(x[2] + x[1], 13);
                x[0] ^= RotateLeft(x[3] + x[2], 18);
                x[6] ^= RotateLeft(x[5] + x[4], 7);
                x[7] ^= RotateLeft(x[6] + x[5], 9);
                x[4] ^= RotateLeft(x[7] + x[6], 13);
                x[5] ^= RotateLeft(x[4] + x[7], 18);
                x[11] ^= RotateLeft(x[10] + x[9], 7);
                x[8] ^= RotateLeft(x[11] + x[10], 9);
                x[9] ^= RotateLeft(x[8] + x[11], 13);
                x[10] ^= RotateLeft(x[9] + x[8], 18);
                x[12] ^= RotateLeft(x[15] + x[14], 7);
                x[13] ^= RotateLeft(x[12] + x[15], 9);
                x[14] ^= RotateLeft(x[13] + x[12], 13);
                x[15] ^= RotateLeft(x[14] + x[13], 18);
            }
            for (int i = 0; i < 16; ++i)
                inout[i] += x[i];
        }

        /// <summary>scryptBlockMix: B' from B, Y_i = Salsa(X ^ B_i), evens then odds.</summary>
        private static void ScryptBlockMix(Span<uint> B_, ReadOnlySpan<uint> B, int r)
        {
            Span<uint> X = stackalloc uint[16];
            B.Slice((2 * r - 1) * 16, 16).CopyTo(X);
            for (int i = 0; i < r * 2; i++)
            {
                for (int j = 0; j < 16; j++)
                    X[j] ^= B[16 * i + j];
                Salsa208WordSpecification(X);
                X.CopyTo(B_.Slice(16 * (i / 2 + (i & 1) * r), 16));
            }
        }

        /// <summary>scryptROMix: one 128*r-byte chunk of B, through X, T and V (N blocks of 32*r words).</summary>
        private static void ScryptROMix(byte[] B, int offset, int r, ulong N, uint[] X, uint[] T, uint[] V)
        {
            int words = 32 * r;
            /* Convert from little endian input */
            for (int k = 0; k < words; k++)
                V[k] = (uint)B[offset + 4 * k] | ((uint)B[offset + 4 * k + 1] << 8)
                     | ((uint)B[offset + 4 * k + 2] << 16) | ((uint)B[offset + 4 * k + 3] << 24);
            int n = (int)N;   // Blen + Vlen <= maxmem <= INT_MAX: V's words fit an int
            for (int i = 1; i < n; i++)
                ScryptBlockMix(V.AsSpan(i * words, words), V.AsSpan((i - 1) * words, words), r);
            ScryptBlockMix(X, V.AsSpan((n - 1) * words, words), r);
            for (int i = 0; i < n; i++)
            {
                uint j = (uint)(X[16 * (2 * r - 1)] % N);
                int pV = (int)j * words;
                for (int k = 0; k < words; k++)
                    T[k] = X[k] ^ V[pV + k];
                ScryptBlockMix(X, T, r);
            }
            /* Convert output to little endian */
            for (int k = 0; k < words; k++)
            {
                uint xtmp = X[k];
                B[offset + 4 * k] = (byte)xtmp;
                B[offset + 4 * k + 1] = (byte)(xtmp >> 8);
                B[offset + 4 * k + 2] = (byte)(xtmp >> 16);
                B[offset + 4 * k + 3] = (byte)(xtmp >> 24);
            }
        }

        // ---- CRYPTO_memcmp ----

        /// <summary>CRYPTO_memcmp: every byte is read whatever the earlier ones held.</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining | System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)]
        internal static int CryptoMemcmp(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, int len)
        {
            int x = 0;
            for (int i = 0; i < len; i++)
                x |= a[i] ^ b[i];
            return x;
        }
    }
}
