using static System.Numerics.BitOperations;

namespace SharpPy.Modules
{
    /// <summary>
    /// The Merkle-Damgard compression functions and initial values of MD5, SHA-1 and SHA-2, which
    /// CPython's _md5, _sha1 and _sha2 take from HACL* (Modules/_hacl); HashModules feeds them.
    /// </summary>
    internal static class Md
    {
        private static PyType HashType;

        private enum HashFamily { MD5, SHA1, SHA256, SHA512 }

        #region Constants

        private static readonly uint[] MD5_K = {
            0xd76aa478,0xe8c7b756,0x242070db,0xc1bdceee,0xf57c0faf,0x4787c62a,0xa8304613,0xfd469501,
            0x698098d8,0x8b44f7af,0xffff5bb1,0x895cd7be,0x6b901122,0xfd987193,0xa679438e,0x49b40821,
            0xf61e2562,0xc040b340,0x265e5a51,0xe9b6c7aa,0xd62f105d,0x02441453,0xd8a1e681,0xe7d3fbc8,
            0x21e1cde6,0xc33707d6,0xf4d50d87,0x455a14ed,0xa9e3e905,0xfcefa3f8,0x676f02d9,0x8d2a4c8a,
            0xfffa3942,0x8771f681,0x6d9d6122,0xfde5380c,0xa4beea44,0x4bdecfa9,0xf6bb4b60,0xbebfbc70,
            0x289b7ec6,0xeaa127fa,0xd4ef3085,0x04881d05,0xd9d4d039,0xe6db99e5,0x1fa27cf8,0xc4ac5665,
            0xf4292244,0x432aff97,0xab9423a7,0xfc93a039,0x655b59c3,0x8f0ccc92,0xffeff47d,0x85845dd1,
            0x6fa87e4f,0xfe2ce6e0,0xa3014314,0x4e0811a1,0xf7537e82,0xbd3af235,0x2ad7d2bb,0xeb86d391
        };

        private static readonly int[] MD5_S = {
            7,12,17,22,7,12,17,22,7,12,17,22,7,12,17,22,
            5,9,14,20,5,9,14,20,5,9,14,20,5,9,14,20,
            4,11,16,23,4,11,16,23,4,11,16,23,4,11,16,23,
            6,10,15,21,6,10,15,21,6,10,15,21,6,10,15,21
        };

        private static readonly uint[] SHA256_K = {
            0x428a2f98,0x71374491,0xb5c0fbcf,0xe9b5dba5,0x3956c25b,0x59f111f1,0x923f82a4,0xab1c5ed5,
            0xd807aa98,0x12835b01,0x243185be,0x550c7dc3,0x72be5d74,0x80deb1fe,0x9bdc06a7,0xc19bf174,
            0xe49b69c1,0xefbe4786,0x0fc19dc6,0x240ca1cc,0x2de92c6f,0x4a7484aa,0x5cb0a9dc,0x76f988da,
            0x983e5152,0xa831c66d,0xb00327c8,0xbf597fc7,0xc6e00bf3,0xd5a79147,0x06ca6351,0x14292967,
            0x27b70a85,0x2e1b2138,0x4d2c6dfc,0x53380d13,0x650a7354,0x766a0abb,0x81c2c92e,0x92722c85,
            0xa2bfe8a1,0xa81a664b,0xc24b8b70,0xc76c51a3,0xd192e819,0xd6990624,0xf40e3585,0x106aa070,
            0x19a4c116,0x1e376c08,0x2748774c,0x34b0bcb5,0x391c0cb3,0x4ed8aa4a,0x5b9cca4f,0x682e6ff3,
            0x748f82ee,0x78a5636f,0x84c87814,0x8cc70208,0x90befffa,0xa4506ceb,0xbef9a3f7,0xc67178f2
        };

        private static readonly ulong[] SHA512_K = {
            0x428a2f98d728ae22UL,0x7137449123ef65cdUL,0xb5c0fbcfec4d3b2fUL,0xe9b5dba58189dbbcUL,
            0x3956c25bf348b538UL,0x59f111f1b605d019UL,0x923f82a4af194f9bUL,0xab1c5ed5da6d8118UL,
            0xd807aa98a3030242UL,0x12835b0145706fbeUL,0x243185be4ee4b28cUL,0x550c7dc3d5ffb4e2UL,
            0x72be5d74f27b896fUL,0x80deb1fe3b1696b1UL,0x9bdc06a725c71235UL,0xc19bf174cf692694UL,
            0xe49b69c19ef14ad2UL,0xefbe4786384f25e3UL,0x0fc19dc68b8cd5b5UL,0x240ca1cc77ac9c65UL,
            0x2de92c6f592b0275UL,0x4a7484aa6ea6e483UL,0x5cb0a9dcbd41fbd4UL,0x76f988da831153b5UL,
            0x983e5152ee66dfabUL,0xa831c66d2db43210UL,0xb00327c898fb213fUL,0xbf597fc7beef0ee4UL,
            0xc6e00bf33da88fc2UL,0xd5a79147930aa725UL,0x06ca6351e003826fUL,0x142929670a0e6e70UL,
            0x27b70a8546d22ffcUL,0x2e1b21385c26c926UL,0x4d2c6dfc5ac42aedUL,0x53380d139d95b3dfUL,
            0x650a73548baf63deUL,0x766a0abb3c77b2a8UL,0x81c2c92e47edaee6UL,0x92722c851482353bUL,
            0xa2bfe8a14cf10364UL,0xa81a664bbc423001UL,0xc24b8b70d0f89791UL,0xc76c51a30654be30UL,
            0xd192e819d6ef5218UL,0xd69906245565a910UL,0xf40e35855771202aUL,0x106aa07032bbd1b8UL,
            0x19a4c116b8d2d0c8UL,0x1e376c085141ab53UL,0x2748774cdf8eeb99UL,0x34b0bcb5e19b48a8UL,
            0x391c0cb3c5c95a63UL,0x4ed8aa4ae3418acbUL,0x5b9cca4f7763e373UL,0x682e6ff3d6b2b8a3UL,
            0x748f82ee5defb2fcUL,0x78a5636f43172f60UL,0x84c87814a1f0ab72UL,0x8cc702081a6439ecUL,
            0x90befffa23631e28UL,0xa4506cebde82bde9UL,0xbef9a3f7b2c67915UL,0xc67178f2e372532bUL,
            0xca273eceea26619cUL,0xd186b8c721c0c207UL,0xeada7dd6cde0eb1eUL,0xf57d4f7fee6ed178UL,
            0x06f067aa72176fbaUL,0x0a637dc5a2c898a6UL,0x113f9804bef90daeUL,0x1b710b35131c471bUL,
            0x28db77f523047d84UL,0x32caab7b40c72493UL,0x3c9ebe0a15c9bebcUL,0x431d67c49c100d4cUL,
            0x4cc5d4becb3e42b6UL,0x597f299cfc657e2aUL,0x5fcb6fab3ad6faecUL,0x6c44198c4a475817UL
        };

        internal static readonly byte[] MD5_IV = {
            0x01,0x23,0x45,0x67,0x89,0xab,0xcd,0xef,0xfe,0xdc,0xba,0x98,0x76,0x54,0x32,0x10
        };

        internal static readonly byte[] SHA1_IV = {
            0x67,0x45,0x23,0x01,0xef,0xcd,0xab,0x89,0x98,0xba,0xdc,0xfe,0x10,0x32,0x54,0x76,0xc3,0xd2,0xe1,0xf0
        };

        internal static readonly byte[] SHA256_IV = {
            0x6a,0x09,0xe6,0x67,0xbb,0x67,0xae,0x85,0x3c,0x6e,0xf3,0x72,0xa5,0x4f,0xf5,0x3a,
            0x51,0x0e,0x52,0x7f,0x9b,0x05,0x68,0x8c,0x1f,0x83,0xd9,0xab,0x5b,0xe0,0xcd,0x19
        };

        internal static readonly byte[] SHA224_IV = {
            0xc1,0x05,0x9e,0xd8,0x36,0x7c,0xd5,0x07,0x30,0x70,0xdd,0x17,0xf7,0x0e,0x59,0x39,
            0xff,0xc0,0x0b,0x31,0x68,0x58,0x15,0x11,0x64,0xf9,0x8f,0xa7,0xbe,0xfa,0x4f,0xa4
        };

        internal static readonly byte[] SHA512_IV = {
            0x6a,0x09,0xe6,0x67,0xf3,0xbc,0xc9,0x08,0xbb,0x67,0xae,0x85,0x84,0xca,0xa7,0x3b,
            0x3c,0x6e,0xf3,0x72,0xfe,0x94,0xf8,0x2b,0xa5,0x4f,0xf5,0x3a,0x5f,0x1d,0x36,0xf1,
            0x51,0x0e,0x52,0x7f,0xad,0xe6,0x82,0xd1,0x9b,0x05,0x68,0x8c,0x2b,0x3e,0x6c,0x1f,
            0x1f,0x83,0xd9,0xab,0xfb,0x41,0xbd,0x6b,0x5b,0xe0,0xcd,0x19,0x13,0x7e,0x21,0x79
        };

        internal static readonly byte[] SHA384_IV = {
            0xcb,0xbb,0x9d,0x5d,0xc1,0x05,0x9e,0xd8,0x62,0x9a,0x29,0x2a,0x36,0x7c,0xd5,0x07,
            0x91,0x59,0x01,0x5a,0x30,0x70,0xdd,0x17,0x15,0x2f,0xec,0xd8,0xf7,0x0e,0x59,0x39,
            0x67,0x33,0x26,0x67,0xff,0xc0,0x0b,0x31,0x8e,0xb4,0x4a,0x87,0x68,0x58,0x15,0x11,
            0xdb,0x0c,0x2e,0x0d,0x64,0xf9,0x8f,0xa7,0x47,0xb5,0x48,0x1d,0xbe,0xfa,0x4f,0xa4
        };

        // FIPS 180-4 5.3.6: the SHA-512/t initial values (libcrypto's sha512_224_init, sha512_256_init).
        internal static readonly byte[] SHA512_224_IV = {
            0x8c,0x3d,0x37,0xc8,0x19,0x54,0x4d,0xa2,0x73,0xe1,0x99,0x66,0x89,0xdc,0xd4,0xd6,
            0x1d,0xfa,0xb7,0xae,0x32,0xff,0x9c,0x82,0x67,0x9d,0xd5,0x14,0x58,0x2f,0x9f,0xcf,
            0x0f,0x6d,0x2b,0x69,0x7b,0xd4,0x4d,0xa8,0x77,0xe3,0x6f,0x73,0x04,0xc4,0x89,0x42,
            0x3f,0x9d,0x85,0xa8,0x6a,0x1d,0x36,0xc8,0x11,0x12,0xe6,0xad,0x91,0xd6,0x92,0xa1
        };

        internal static readonly byte[] SHA512_256_IV = {
            0x22,0x31,0x21,0x94,0xfc,0x2b,0xf7,0x2c,0x9f,0x55,0x5f,0xa3,0xc8,0x4c,0x64,0xc2,
            0x23,0x93,0xb8,0x6b,0x6f,0x53,0xb1,0x51,0x96,0x38,0x77,0x19,0x59,0x40,0xea,0xbd,
            0x96,0x28,0x3e,0xe2,0xa8,0x8e,0xff,0xe3,0xbe,0x5e,0x1e,0x25,0x53,0x86,0x39,0x92,
            0x2b,0x01,0x99,0xfc,0x2c,0x85,0xb8,0xaa,0x0e,0xb7,0x2d,0xdc,0x81,0xc5,0x2c,0xa2
        };

        // RIPEMD-160 (libcrypto's crypto/ripemd): h0..h4, little-endian like MD5's.
        internal static readonly byte[] RIPEMD160_IV = {
            0x01,0x23,0x45,0x67,0x89,0xab,0xcd,0xef,0xfe,0xdc,0xba,0x98,0x76,0x54,0x32,0x10,0xf0,0xe1,0xd2,0xc3
        };

        // SM3 (GB/T 32905-2016, libcrypto's crypto/sm3): eight big-endian words.
        internal static readonly byte[] SM3_IV = {
            0x73,0x80,0x16,0x6f,0x49,0x14,0xb2,0xb9,0x17,0x24,0x42,0xd7,0xda,0x8a,0x06,0x00,
            0xa9,0x6f,0x30,0xbc,0x16,0x31,0x38,0xaa,0xe3,0x8d,0xee,0x4d,0xb0,0xfb,0x0e,0x4e
        };

        private static readonly int[] RMD_RL = {
            0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15, 7,4,13,1,10,6,15,3,12,0,9,5,2,14,11,8,
            3,10,14,4,9,15,8,1,2,7,0,6,13,11,5,12, 1,9,11,10,0,8,12,4,13,3,7,15,14,5,6,2,
            4,0,5,9,7,12,2,10,14,1,3,8,11,6,15,13
        };
        private static readonly int[] RMD_RR = {
            5,14,7,0,9,2,11,4,13,6,15,8,1,10,3,12, 6,11,3,7,0,13,5,10,14,15,8,12,4,9,1,2,
            15,5,1,3,7,14,6,9,11,8,12,2,10,0,4,13, 8,6,4,1,3,11,15,0,5,12,2,13,9,7,10,14,
            12,15,10,4,1,5,8,7,6,2,13,14,0,3,9,11
        };
        private static readonly int[] RMD_SL = {
            11,14,15,12,5,8,7,9,11,13,14,15,6,7,9,8, 7,6,8,13,11,9,7,15,7,12,15,9,11,7,13,12,
            11,13,6,7,14,9,13,15,14,8,13,6,5,12,7,5, 11,12,14,15,14,15,9,8,9,14,5,6,8,6,5,12,
            9,15,5,11,6,8,13,12,5,12,13,14,11,8,5,6
        };
        private static readonly int[] RMD_SR = {
            8,9,9,11,13,15,15,5,7,7,8,11,14,14,12,6, 9,13,15,7,12,8,9,11,7,7,12,7,6,15,13,11,
            9,7,15,11,8,6,6,14,12,13,5,14,13,13,7,5, 15,5,8,11,14,14,6,14,6,9,12,9,12,5,15,8,
            8,5,12,9,12,5,14,6,8,13,6,5,15,13,11,11
        };
        private static readonly uint[] RMD_KL = { 0x00000000, 0x5A827999, 0x6ED9EBA1, 0x8F1BBCDC, 0xA953FD4E };
        private static readonly uint[] RMD_KR = { 0x50A28BE6, 0x5C4DD124, 0x6D703EF3, 0x7A6D76E9, 0x00000000 };

        #endregion

        #region Compression Functions

        private static uint ReadU32LE(byte[] b, int o) =>
            (uint)b[o] | ((uint)b[o+1] << 8) | ((uint)b[o+2] << 16) | ((uint)b[o+3] << 24);

        private static void WriteU32LE(byte[] b, int o, uint v) {
            b[o] = (byte)v; b[o+1] = (byte)(v >> 8); b[o+2] = (byte)(v >> 16); b[o+3] = (byte)(v >> 24);
        }

        private static uint ReadU32BE(byte[] b, int o) =>
            ((uint)b[o] << 24) | ((uint)b[o+1] << 16) | ((uint)b[o+2] << 8) | (uint)b[o+3];

        private static void WriteU32BE(byte[] b, int o, uint v) {
            b[o] = (byte)(v >> 24); b[o+1] = (byte)(v >> 16); b[o+2] = (byte)(v >> 8); b[o+3] = (byte)v;
        }

        private static ulong ReadU64BE(byte[] b, int o) =>
            ((ulong)b[o] << 56) | ((ulong)b[o+1] << 48) | ((ulong)b[o+2] << 40) | ((ulong)b[o+3] << 32) |
            ((ulong)b[o+4] << 24) | ((ulong)b[o+5] << 16) | ((ulong)b[o+6] << 8) | (ulong)b[o+7];

        private static void WriteU64BE(byte[] b, int o, ulong v) {
            b[o] = (byte)(v >> 56); b[o+1] = (byte)(v >> 48); b[o+2] = (byte)(v >> 40); b[o+3] = (byte)(v >> 32);
            b[o+4] = (byte)(v >> 24); b[o+5] = (byte)(v >> 16); b[o+6] = (byte)(v >> 8); b[o+7] = (byte)v;
        }

        internal static void MD5Compress(byte[] state, byte[] block) {
            uint a = ReadU32LE(state, 0), b = ReadU32LE(state, 4), c = ReadU32LE(state, 8), d = ReadU32LE(state, 12);
            uint AA = a, BB = b, CC = c, DD = d;
            uint[] M = new uint[16];
            for (int i = 0; i < 16; i++) M[i] = ReadU32LE(block, i * 4);
            for (int i = 0; i < 64; i++) {
                uint f, g;
                if (i < 16) { f = (b & c) | (~b & d); g = (uint)i; }
                else if (i < 32) { f = (d & b) | (~d & c); g = (uint)((5 * i + 1) & 15); }
                else if (i < 48) { f = b ^ c ^ d; g = (uint)((3 * i + 5) & 15); }
                else { f = c ^ (b | ~d); g = (uint)((7 * i) & 15); }
                uint tmp = d; d = c; c = b;
                b = b + RotateLeft(a + f + MD5_K[i] + M[g], MD5_S[i]);
                a = tmp;
            }
            WriteU32LE(state, 0, a + AA); WriteU32LE(state, 4, b + BB);
            WriteU32LE(state, 8, c + CC); WriteU32LE(state, 12, d + DD);
        }

        internal static void SHA1Compress(byte[] state, byte[] block) {
            uint h0 = ReadU32BE(state, 0), h1 = ReadU32BE(state, 4), h2 = ReadU32BE(state, 8),
                 h3 = ReadU32BE(state, 12), h4 = ReadU32BE(state, 16);
            uint[] w = new uint[80];
            for (int i = 0; i < 16; i++) w[i] = ReadU32BE(block, i * 4);
            for (int i = 16; i < 80; i++) w[i] = RotateLeft(w[i-3] ^ w[i-8] ^ w[i-14] ^ w[i-16], 1);
            uint a = h0, b = h1, c = h2, d = h3, e = h4;
            for (int i = 0; i < 80; i++) {
                uint f, k;
                if (i < 20) { f = (b & c) | (~b & d); k = 0x5A827999; }
                else if (i < 40) { f = b ^ c ^ d; k = 0x6ED9EBA1; }
                else if (i < 60) { f = (b & c) | (b & d) | (c & d); k = 0x8F1BBCDC; }
                else { f = b ^ c ^ d; k = 0xCA62C1D6; }
                uint tmp = RotateLeft(a, 5) + f + e + k + w[i];
                e = d; d = c; c = RotateLeft(b, 30); b = a; a = tmp;
            }
            WriteU32BE(state, 0, a + h0); WriteU32BE(state, 4, b + h1);
            WriteU32BE(state, 8, c + h2); WriteU32BE(state, 12, d + h3);
            WriteU32BE(state, 16, e + h4);
        }

        internal static void SHA256Compress(byte[] state, byte[] block) {
            uint[] h = new uint[8];
            for (int i = 0; i < 8; i++) h[i] = ReadU32BE(state, i * 4);
            uint[] w = new uint[64];
            for (int i = 0; i < 16; i++) w[i] = ReadU32BE(block, i * 4);
            for (int i = 16; i < 64; i++) {
                uint s0 = RotateRight(w[i-15], 7) ^ RotateRight(w[i-15], 18) ^ (w[i-15] >> 3);
                uint s1 = RotateRight(w[i-2], 17) ^ RotateRight(w[i-2], 19) ^ (w[i-2] >> 10);
                w[i] = w[i-16] + s0 + w[i-7] + s1;
            }
            uint a = h[0], b = h[1], c = h[2], d = h[3], e = h[4], f = h[5], g = h[6], hh = h[7];
            for (int i = 0; i < 64; i++) {
                uint S1 = RotateRight(e, 6) ^ RotateRight(e, 11) ^ RotateRight(e, 25);
                uint ch = (e & f) ^ (~e & g);
                uint t1 = hh + S1 + ch + SHA256_K[i] + w[i];
                uint S0 = RotateRight(a, 2) ^ RotateRight(a, 13) ^ RotateRight(a, 22);
                uint maj = (a & b) ^ (a & c) ^ (b & c);
                uint t2 = S0 + maj;
                hh = g; g = f; f = e; e = d + t1;
                d = c; c = b; b = a; a = t1 + t2;
            }
            WriteU32BE(state, 0, a + h[0]); WriteU32BE(state, 4, b + h[1]);
            WriteU32BE(state, 8, c + h[2]); WriteU32BE(state, 12, d + h[3]);
            WriteU32BE(state, 16, e + h[4]); WriteU32BE(state, 20, f + h[5]);
            WriteU32BE(state, 24, g + h[6]); WriteU32BE(state, 28, hh + h[7]);
        }

        internal static void SHA512Compress(byte[] state, byte[] block) {
            ulong[] h = new ulong[8];
            for (int i = 0; i < 8; i++) h[i] = ReadU64BE(state, i * 8);
            ulong[] w = new ulong[80];
            for (int i = 0; i < 16; i++) w[i] = ReadU64BE(block, i * 8);
            for (int i = 16; i < 80; i++) {
                ulong s0 = RotateRight(w[i-15], 1) ^ RotateRight(w[i-15], 8) ^ (w[i-15] >> 7);
                ulong s1 = RotateRight(w[i-2], 19) ^ RotateRight(w[i-2], 61) ^ (w[i-2] >> 6);
                w[i] = w[i-16] + s0 + w[i-7] + s1;
            }
            ulong a = h[0], b = h[1], c = h[2], d = h[3], e = h[4], f = h[5], g = h[6], hh = h[7];
            for (int i = 0; i < 80; i++) {
                ulong S1 = RotateRight(e, 14) ^ RotateRight(e, 18) ^ RotateRight(e, 41);
                ulong ch = (e & f) ^ (~e & g);
                ulong t1 = hh + S1 + ch + SHA512_K[i] + w[i];
                ulong S0 = RotateRight(a, 28) ^ RotateRight(a, 34) ^ RotateRight(a, 39);
                ulong maj = (a & b) ^ (a & c) ^ (b & c);
                ulong t2 = S0 + maj;
                hh = g; g = f; f = e; e = d + t1;
                d = c; c = b; b = a; a = t1 + t2;
            }
            WriteU64BE(state, 0, a + h[0]); WriteU64BE(state, 8, b + h[1]);
            WriteU64BE(state, 16, c + h[2]); WriteU64BE(state, 24, d + h[3]);
            WriteU64BE(state, 32, e + h[4]); WriteU64BE(state, 40, f + h[5]);
            WriteU64BE(state, 48, g + h[6]); WriteU64BE(state, 56, hh + h[7]);
        }

        // RIPEMD-160's five boolean functions, by round (f1..f5 of the left line; the right uses them reversed).
        private static uint RmdF(int round, uint x, uint y, uint z) => round switch
        {
            0 => x ^ y ^ z,
            1 => (x & y) | (~x & z),
            2 => (x | ~y) ^ z,
            3 => (x & z) | (y & ~z),
            _ => x ^ (y | ~z),
        };

        /// <summary>ripemd160_block_data_order: two parallel lines of 80 steps over a little-endian block.</summary>
        internal static void RIPEMD160Compress(byte[] state, byte[] block) {
            uint[] x = new uint[16];
            for (int i = 0; i < 16; i++) x[i] = ReadU32LE(block, i * 4);
            uint h0 = ReadU32LE(state, 0), h1 = ReadU32LE(state, 4), h2 = ReadU32LE(state, 8),
                 h3 = ReadU32LE(state, 12), h4 = ReadU32LE(state, 16);
            uint al = h0, bl = h1, cl = h2, dl = h3, el = h4;
            uint ar = h0, br = h1, cr = h2, dr = h3, er = h4;
            for (int j = 0; j < 80; j++) {
                int round = j / 16;
                uint t = RotateLeft(al + RmdF(round, bl, cl, dl) + x[RMD_RL[j]] + RMD_KL[round], RMD_SL[j]) + el;
                al = el; el = dl; dl = RotateLeft(cl, 10); cl = bl; bl = t;
                t = RotateLeft(ar + RmdF(4 - round, br, cr, dr) + x[RMD_RR[j]] + RMD_KR[round], RMD_SR[j]) + er;
                ar = er; er = dr; dr = RotateLeft(cr, 10); cr = br; br = t;
            }
            uint tmp = h1 + cl + dr;
            WriteU32LE(state, 4, h2 + dl + er);
            WriteU32LE(state, 8, h3 + el + ar);
            WriteU32LE(state, 12, h4 + al + br);
            WriteU32LE(state, 16, h0 + bl + cr);
            WriteU32LE(state, 0, tmp);
        }

        /// <summary>ossl_sm3_block_data_order: the SM3 message expansion and 64 rounds over big-endian words.</summary>
        internal static void SM3Compress(byte[] state, byte[] block) {
            uint[] w = new uint[68];
            for (int i = 0; i < 16; i++) w[i] = ReadU32BE(block, i * 4);
            for (int i = 16; i < 68; i++) {
                uint v = w[i-16] ^ w[i-9] ^ RotateLeft(w[i-3], 15);
                // P1(x) = x ^ (x <<< 15) ^ (x <<< 23)
                w[i] = (v ^ RotateLeft(v, 15) ^ RotateLeft(v, 23)) ^ RotateLeft(w[i-13], 7) ^ w[i-6];
            }
            uint[] h = new uint[8];
            for (int i = 0; i < 8; i++) h[i] = ReadU32BE(state, i * 4);
            uint a = h[0], b = h[1], c = h[2], d = h[3], e = h[4], f = h[5], g = h[6], hh = h[7];
            for (int j = 0; j < 64; j++) {
                uint tj = j < 16 ? 0x79cc4519u : 0x7a879d8au;
                uint ss1 = RotateLeft(RotateLeft(a, 12) + e + RotateLeft(tj, j % 32), 7);
                uint ss2 = ss1 ^ RotateLeft(a, 12);
                uint ff = j < 16 ? a ^ b ^ c : (a & b) | (a & c) | (b & c);
                uint gg = j < 16 ? e ^ f ^ g : (e & f) | (~e & g);
                uint tt1 = ff + d + ss2 + (w[j] ^ w[j + 4]);
                uint tt2 = gg + hh + ss1 + w[j];
                d = c; c = RotateLeft(b, 9); b = a; a = tt1;
                hh = g; g = RotateLeft(f, 19); f = e;
                // P0(x) = x ^ (x <<< 9) ^ (x <<< 17)
                e = tt2 ^ RotateLeft(tt2, 9) ^ RotateLeft(tt2, 17);
            }
            WriteU32BE(state, 0, a ^ h[0]); WriteU32BE(state, 4, b ^ h[1]);
            WriteU32BE(state, 8, c ^ h[2]); WriteU32BE(state, 12, d ^ h[3]);
            WriteU32BE(state, 16, e ^ h[4]); WriteU32BE(state, 20, f ^ h[5]);
            WriteU32BE(state, 24, g ^ h[6]); WriteU32BE(state, 28, hh ^ h[7]);
        }

        #endregion
    }
}
