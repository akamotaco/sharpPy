using System;

namespace SharpPy
{
    /// <summary>
    /// Python/pyhash.c: siphash13 over the bytes of a str or bytes object, keyed by _Py_HashSecret.
    /// Dictionaries here use the CLR hash of the text; this is what hash() and __hash__ answer.
    /// </summary>
    internal static class PyHash
    {
        private static readonly ulong K0, K1;

        static PyHash()
        {
            var secret = new byte[24];
            string seed = Environment.GetEnvironmentVariable("PYTHONHASHSEED");
            if (string.IsNullOrEmpty(seed) || seed == "random")
                System.Security.Cryptography.RandomNumberGenerator.Fill(secret);
            else if (uint.TryParse(seed, out uint number) && number != 0)
            {
                // lcg_urandom
                uint x = number;
                for (int i = 0; i < secret.Length; i++)
                {
                    x = unchecked(x * 214013 + 2531011);
                    secret[i] = (byte)((x >> 16) & 0xff);
                }
            }
            K0 = BitConverter.ToUInt64(secret, 0);
            K1 = BitConverter.ToUInt64(secret, 8);
        }

        private static ulong Rotate(ulong value, int bits) => (value << bits) | (value >> (64 - bits));

        private static void Round(ref ulong v0, ref ulong v1, ref ulong v2, ref ulong v3)
        {
            unchecked
            {
                v0 += v1; v1 = Rotate(v1, 13); v1 ^= v0; v0 = Rotate(v0, 32);
                v2 += v3; v3 = Rotate(v3, 16); v3 ^= v2;
                v0 += v3; v3 = Rotate(v3, 21); v3 ^= v0;
                v2 += v1; v1 = Rotate(v1, 17); v1 ^= v2; v2 = Rotate(v2, 32);
            }
        }

        // _Py_HashBytes over pysiphash: empty is 0, and -1 is never a hash.
        internal static long Bytes(ReadOnlySpan<byte> data)
        {
            if (data.Length == 0)
                return 0;
            long hash = unchecked((long)SipHash13(K0, K1, data));
            return hash == -1 ? -2 : hash;
        }

        // _Py_KeyedHash: siphash13 keyed by (key, 0), which _imp.source_hash answers.
        internal static ulong Keyed(ulong key, ReadOnlySpan<byte> data) => SipHash13(key, 0, data);

        // siphash13
        private static ulong SipHash13(ulong k0, ulong k1, ReadOnlySpan<byte> data)
        {
            ulong v0 = k0 ^ 0x736f6d6570736575UL, v1 = k1 ^ 0x646f72616e646f6dUL;
            ulong v2 = k0 ^ 0x6c7967656e657261UL, v3 = k1 ^ 0x7465646279746573UL;
            int offset = 0;
            for (; data.Length - offset >= 8; offset += 8)
            {
                ulong word = BitConverter.ToUInt64(data.Slice(offset, 8));
                v3 ^= word;
                Round(ref v0, ref v1, ref v2, ref v3);
                v0 ^= word;
            }
            ulong last = (ulong)data.Length << 56;
            for (int i = 0; offset + i < data.Length; i++)
                last |= (ulong)data[offset + i] << (8 * i);
            v3 ^= last;
            Round(ref v0, ref v1, ref v2, ref v3);
            v0 ^= last;
            v2 ^= 0xff;
            Round(ref v0, ref v1, ref v2, ref v3);
            Round(ref v0, ref v1, ref v2, ref v3);
            Round(ref v0, ref v1, ref v2, ref v3);
            return v0 ^ v1 ^ v2 ^ v3;
        }

        // unicode_hash: the bytes of the compact form, one, two or four to a character.
        internal static long Text(string text)
        {
            int width = 1;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    width = 4;
                    break;
                }
                if (c > 0xFF)
                    width = 2;
            }
            if (text.Length == 0)
                return 0;
            byte[] data;
            if (width == 4)
            {
                var points = new System.Collections.Generic.List<byte>(text.Length * 4);
                for (int i = 0; i < text.Length; i++)
                {
                    int point = text[i];
                    if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                        point = char.ConvertToUtf32(text[i], text[++i]);
                    points.AddRange(BitConverter.GetBytes(point));
                }
                data = points.ToArray();
            }
            else
            {
                data = new byte[text.Length * width];
                for (int i = 0; i < text.Length; i++)
                {
                    data[i * width] = (byte)text[i];
                    if (width == 2)
                        data[i * 2 + 1] = (byte)(text[i] >> 8);
                }
            }
            return Bytes(data);
        }
    }
}
