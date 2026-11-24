using System;
using System.Buffers.Binary;
using System.Numerics;

namespace SharpPy.Modules.Lzma
{
    /// <summary>
    /// check/check.c with check/crc32_fast.c, crc64_fast.c (their tables computed as crc32_tablegen.c
    /// and crc64_tablegen.c do) and sha256.c: the integrity checks of .xz Blocks and the CRC32 of
    /// headers, Index and the LZ encoder's hash.
    /// </summary>
    internal sealed class LzmaCheckState
    {
        /// <summary>buffer: the finished check, little endian, as the Block stores it.</summary>
        internal readonly byte[] buffer = new byte[64];
        internal uint crc32;
        internal ulong crc64;
        // sha256 state
        private readonly uint[] h = new uint[8];
        private ulong size;
        private readonly byte[] block = new byte[64];

        internal static readonly uint[] Crc32Table = MakeCrc32Table();
        private static readonly ulong[] Crc64Table = MakeCrc64Table();

        // crc32_tablegen.c: the reversed polynomial 0xEDB88320
        private static uint[] MakeCrc32Table()
        {
            var t = new uint[256];
            for (uint b = 0; b < 256; ++b)
            {
                uint r = b;
                for (int i = 0; i < 8; ++i)
                    r = (r & 1) != 0 ? (r >> 1) ^ 0xEDB88320 : r >> 1;
                t[b] = r;
            }
            return t;
        }

        // crc64_tablegen.c: the reversed polynomial 0xC96C5795D7870F42 (ECMA-182)
        private static ulong[] MakeCrc64Table()
        {
            var t = new ulong[256];
            for (ulong b = 0; b < 256; ++b)
            {
                ulong r = b;
                for (int i = 0; i < 8; ++i)
                    r = (r & 1) != 0 ? (r >> 1) ^ 0xC96C5795D7870F42 : r >> 1;
                t[b] = r;
            }
            return t;
        }

        /// <summary>lzma_crc32</summary>
        internal static uint Crc32(ReadOnlySpan<byte> buf, uint crc)
        {
            crc = ~crc;
            var t = Crc32Table;
            foreach (byte b in buf)
                crc = t[(byte)(crc ^ b)] ^ (crc >> 8);
            return ~crc;
        }

        /// <summary>lzma_crc64</summary>
        internal static ulong Crc64(ReadOnlySpan<byte> buf, ulong crc)
        {
            crc = ~crc;
            var t = Crc64Table;
            foreach (byte b in buf)
                crc = t[(byte)(crc ^ b)] ^ (crc >> 8);
            return ~crc;
        }

        /// <summary>lzma_check_is_supported</summary>
        internal static bool IsSupported(int type) =>
            (uint)type <= LzmaConst.CHECK_ID_MAX
            && (type == LzmaConst.CHECK_NONE || type == LzmaConst.CHECK_CRC32
                || type == LzmaConst.CHECK_CRC64 || type == LzmaConst.CHECK_SHA256);

        private static readonly byte[] check_sizes = { 0, 4, 4, 4, 8, 8, 8, 16, 16, 16, 32, 32, 32, 64, 64, 64 };

        /// <summary>lzma_check_size</summary>
        internal static uint Size(int type) => (uint)type > LzmaConst.CHECK_ID_MAX ? uint.MaxValue : check_sizes[type];

        /// <summary>lzma_check_init</summary>
        internal void Init(int type)
        {
            switch (type)
            {
                case LzmaConst.CHECK_CRC32: crc32 = 0; break;
                case LzmaConst.CHECK_CRC64: crc64 = 0; break;
                case LzmaConst.CHECK_SHA256: Sha256Init(); break;
            }
        }

        /// <summary>lzma_check_update</summary>
        internal void Update(int type, ReadOnlySpan<byte> buf)
        {
            switch (type)
            {
                case LzmaConst.CHECK_CRC32: crc32 = Crc32(buf, crc32); break;
                case LzmaConst.CHECK_CRC64: crc64 = Crc64(buf, crc64); break;
                case LzmaConst.CHECK_SHA256: Sha256Update(buf); break;
            }
        }

        /// <summary>lzma_check_finish</summary>
        internal void Finish(int type)
        {
            switch (type)
            {
                case LzmaConst.CHECK_CRC32: BinaryPrimitives.WriteUInt32LittleEndian(buffer, crc32); break;
                case LzmaConst.CHECK_CRC64: BinaryPrimitives.WriteUInt64LittleEndian(buffer, crc64); break;
                case LzmaConst.CHECK_SHA256: Sha256Finish(); break;
            }
        }

        // ------------------------------------------------------------ sha256.c

        private static readonly uint[] SHA256_K =
        {
            0x428A2F98, 0x71374491, 0xB5C0FBCF, 0xE9B5DBA5, 0x3956C25B, 0x59F111F1, 0x923F82A4, 0xAB1C5ED5,
            0xD807AA98, 0x12835B01, 0x243185BE, 0x550C7DC3, 0x72BE5D74, 0x80DEB1FE, 0x9BDC06A7, 0xC19BF174,
            0xE49B69C1, 0xEFBE4786, 0x0FC19DC6, 0x240CA1CC, 0x2DE92C6F, 0x4A7484AA, 0x5CB0A9DC, 0x76F988DA,
            0x983E5152, 0xA831C66D, 0xB00327C8, 0xBF597FC7, 0xC6E00BF3, 0xD5A79147, 0x06CA6351, 0x14292967,
            0x27B70A85, 0x2E1B2138, 0x4D2C6DFC, 0x53380D13, 0x650A7354, 0x766A0ABB, 0x81C2C92E, 0x92722C85,
            0xA2BFE8A1, 0xA81A664B, 0xC24B8B70, 0xC76C51A3, 0xD192E819, 0xD6990624, 0xF40E3585, 0x106AA070,
            0x19A4C116, 0x1E376C08, 0x2748774C, 0x34B0BCB5, 0x391C0CB3, 0x4ED8AA4A, 0x5B9CCA4F, 0x682E6FF3,
            0x748F82EE, 0x78A5636F, 0x84C87814, 0x8CC70208, 0x90BEFFFA, 0xA4506CEB, 0xBEF9A3F7, 0xC67178F2,
        };

        /// <summary>lzma_sha256_init</summary>
        private void Sha256Init()
        {
            h[0] = 0x6A09E667; h[1] = 0xBB67AE85; h[2] = 0x3C6EF372; h[3] = 0xA54FF53A;
            h[4] = 0x510E527F; h[5] = 0x9B05688C; h[6] = 0x1F83D9AB; h[7] = 0x5BE0CD19;
            size = 0;
        }

        /// <summary>transform: one 64-byte block</summary>
        private void Transform(ReadOnlySpan<byte> data)
        {
            Span<uint> w = stackalloc uint[64];
            for (int i = 0; i < 16; i++)
                w[i] = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(i * 4));
            for (int i = 16; i < 64; i++)
            {
                uint s0 = BitOperations.RotateRight(w[i - 15], 7) ^ BitOperations.RotateRight(w[i - 15], 18) ^ (w[i - 15] >> 3);
                uint s1 = BitOperations.RotateRight(w[i - 2], 17) ^ BitOperations.RotateRight(w[i - 2], 19) ^ (w[i - 2] >> 10);
                w[i] = w[i - 16] + s0 + w[i - 7] + s1;
            }
            uint a = h[0], b = h[1], c = h[2], d = h[3], e = h[4], f = h[5], g = h[6], hh = h[7];
            for (int i = 0; i < 64; i++)
            {
                uint S1 = BitOperations.RotateRight(e, 6) ^ BitOperations.RotateRight(e, 11) ^ BitOperations.RotateRight(e, 25);
                uint ch = (e & f) ^ (~e & g);
                uint t1 = hh + S1 + ch + SHA256_K[i] + w[i];
                uint S0 = BitOperations.RotateRight(a, 2) ^ BitOperations.RotateRight(a, 13) ^ BitOperations.RotateRight(a, 22);
                uint maj = (a & b) ^ (a & c) ^ (b & c);
                uint t2 = S0 + maj;
                hh = g; g = f; f = e; e = d + t1; d = c; c = b; b = a; a = t1 + t2;
            }
            h[0] += a; h[1] += b; h[2] += c; h[3] += d; h[4] += e; h[5] += f; h[6] += g; h[7] += hh;
        }

        /// <summary>lzma_sha256_update</summary>
        private void Sha256Update(ReadOnlySpan<byte> buf)
        {
            while (buf.Length > 0)
            {
                int copy_start = (int)(size & 0x3F);
                int copy_size = Math.Min(64 - copy_start, buf.Length);
                buf.Slice(0, copy_size).CopyTo(block.AsSpan(copy_start));
                buf = buf.Slice(copy_size);
                size += (ulong)copy_size;
                if ((size & 0x3F) == 0)
                    Transform(block);
            }
        }

        /// <summary>lzma_sha256_finish</summary>
        private void Sha256Finish()
        {
            int pos = (int)(size & 0x3F);
            block[pos++] = 0x80;
            while (pos != 64 - 8)
            {
                if (pos == 64)
                {
                    Transform(block);
                    pos = 0;
                }
                block[pos++] = 0x00;
            }
            BinaryPrimitives.WriteUInt64BigEndian(block.AsSpan(64 - 8), size * 8);
            Transform(block);
            for (int i = 0; i < 8; ++i)
                BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(i * 4), h[i]);
        }
    }
}
