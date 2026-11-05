using System;

namespace SharpPy
{
    /// <summary>
    /// PyFloat_Pack2/4/8 and PyFloat_Unpack2/4/8 of Objects/floatobject.c (CPython v3.12.15), on a
    /// platform whose float and double are IEEE 754, which is every one .NET runs on.
    /// </summary>
    internal static class PyFloatPack
    {
        // PyFloat_Pack2: round half to even into the binary16 format.
        internal static void Pack2(double x, Span<byte> p, bool le)
        {
            int sign;
            int e;
            ushort bits;
            if (x == 0.0)
            {
                sign = double.IsNegative(x) ? 1 : 0;
                e = 0;
                bits = 0;
            }
            else if (double.IsInfinity(x))
            {
                sign = x < 0.0 ? 1 : 0;
                e = 0x1f;
                bits = 0;
            }
            else if (double.IsNaN(x))
            {
                // The quiet NaN with the topmost fraction bit set and the sign of x.
                sign = double.IsNegative(x) ? 1 : 0;
                e = 0x1f;
                bits = 512;
            }
            else
            {
                sign = x < 0.0 ? 1 : 0;
                if (sign != 0)
                    x = -x;
                double f = Frexp(x, out e);
                // Normalize f to be in the range [1.0, 2.0)
                f *= 2.0;
                e--;
                if (e >= 16)
                    throw Overflow('e');
                if (e < -25)
                {
                    // |x| < 2**-25. Underflow to zero.
                    f = 0.0;
                    e = 0;
                }
                else if (e < -14)
                {
                    // |x| < 2**-14. Gradual underflow
                    f = Math.ScaleB(f, 14 + e);
                    e = 0;
                }
                else
                {
                    e += 15;
                    f -= 1.0;   // Get rid of leading 1
                }
                f *= 1024.0;    // 2**10
                // Round to even
                bits = (ushort)f;   // Note the truncation
                if (f - bits > 0.5 || (f - bits == 0.5 && bits % 2 == 1))
                {
                    ++bits;
                    if (bits == 1024)
                    {
                        // The carry propagated out of a string of 10 1 bits.
                        bits = 0;
                        ++e;
                        if (e == 31)
                            throw Overflow('e');
                    }
                }
            }
            bits = (ushort)(bits | (e << 10) | (sign << 15));
            p[le ? 1 : 0] = (byte)((bits >> 8) & 0xFF);
            p[le ? 0 : 1] = (byte)(bits & 0xFF);
        }

        // PyFloat_Pack4: the cast to float, which must not overflow a finite value.
        internal static void Pack4(double x, Span<byte> p, bool le)
        {
            float y = (float)x;
            if (float.IsInfinity(y) && !double.IsInfinity(x))
                throw Overflow('f');
            int bits = BitConverter.SingleToInt32Bits(y);
            for (int i = 0; i < 4; i++)
                p[le ? i : 3 - i] = (byte)(bits >> (8 * i));
        }

        // PyFloat_Pack8
        internal static void Pack8(double x, Span<byte> p, bool le)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            for (int i = 0; i < 8; i++)
                p[le ? i : 7 - i] = (byte)(bits >> (8 * i));
        }

        // PyFloat_Unpack2: a NaN keeps only its sign.
        internal static double Unpack2(ReadOnlySpan<byte> p, bool le)
        {
            byte first = p[le ? 1 : 0];
            byte second = p[le ? 0 : 1];
            int sign = (first >> 7) & 1;
            int e = (first & 0x7C) >> 2;
            int f = ((first & 0x03) << 8) | second;
            if (e == 0x1f)
            {
                if (f == 0)
                    return sign != 0 ? double.NegativeInfinity : double.PositiveInfinity;
                return sign != 0 ? -Math.Abs(double.NaN) : Math.Abs(double.NaN);
            }
            double x = f / 1024.0;
            if (e == 0)
                e = -14;
            else
            {
                x += 1.0;
                e -= 15;
            }
            x = Math.ScaleB(x, e);
            return sign != 0 ? -x : x;
        }

        // PyFloat_Unpack4
        internal static double Unpack4(ReadOnlySpan<byte> p, bool le)
        {
            int bits = 0;
            for (int i = 0; i < 4; i++)
                bits |= p[le ? i : 3 - i] << (8 * i);
            return BitConverter.Int32BitsToSingle(bits);
        }

        // PyFloat_Unpack8
        internal static double Unpack8(ReadOnlySpan<byte> p, bool le)
        {
            long bits = 0;
            for (int i = 0; i < 8; i++)
                bits |= (long)p[le ? i : 7 - i] << (8 * i);
            return BitConverter.Int64BitsToDouble(bits);
        }

        private static Exception Overflow(char format) =>
            PyOverflowError.Create($"float too large to pack with {format} format");

        // frexp: x = f * 2**e with 0.5 <= |f| < 1 for a finite nonzero x.
        private static double Frexp(double x, out int e)
        {
            e = Math.ILogB(x) + 1;
            return Math.ScaleB(x, -e);
        }
    }
}
