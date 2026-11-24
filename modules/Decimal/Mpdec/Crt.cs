// Modules/_decimal/libmpdec/crt.c (CPython 3.12.15, libmpdec 2.5.1), CONFIG_64.
using System;
using System.Diagnostics;

namespace SharpPy.Modules.Mpdecimal
{
    internal static partial class Mpd
    {
        /* Bignum: Chinese Remainder Theorem, extends the maximum transform length. */

        /* Multiply P1P2 by v, store result in w. */
        private static void _crt_mulP1P2_3(Span<ulong> w, ulong v)
        {
            ulong hi1, hi2, lo;

            _mpd_mul_words(out hi1, out lo, LH_P1P2, v);
            w[0] = lo;

            _mpd_mul_words(out hi2, out lo, UH_P1P2, v);
            lo = hi1 + lo;
            if (lo < hi1) hi2++;

            w[1] = lo;
            w[2] = hi2;
        }

        /* Add 3 words from v to w. The result is known to fit in w. */
        private static void _crt_add3(Span<ulong> w, ReadOnlySpan<ulong> v)
        {
            ulong carry;

            w[0] = w[0] + v[0];
            carry = (w[0] < v[0]) ? 1UL : 0UL;

            w[1] = w[1] + v[1];
            if (w[1] < v[1]) w[2]++;

            w[1] = w[1] + carry;
            if (w[1] < carry) w[2]++;

            w[2] += v[2];
        }

        /* Divide 3 words in u by v, store result in w, return remainder. */
        private static ulong _crt_div3(Span<ulong> w, ReadOnlySpan<ulong> u, ulong v)
        {
            ulong r1 = u[2];
            ulong r2;

            if (r1 < v)
            {
                w[2] = 0;
            }
            else
            {
                _mpd_div_word(out w[2], out r1, u[2], v); /* GCOV_NOT_REACHED */
            }

            _mpd_div_words(out w[1], out r2, r1, u[1], v);
            _mpd_div_words(out w[0], out r1, r2, u[0], v);

            return r1;
        }

        /*
         * Chinese Remainder Theorem:
         * Algorithm from Joerg Arndt, "Matters Computational",
         * Chapter 37.4.1 [http://www.jjj.de/fxt/]
         *
         * See also Knuth, TAOCP, Volume 2, 4.3.2, exercise 7.
         */

        /*
         * CRT with carry: x1, x2, x3 contain numbers modulo p1, p2, p3. For each
         * triple of members of the arrays, find the unique z modulo p1*p2*p3, with
         * zmax = p1*p2*p3 - 1.
         *
         * In each iteration of the loop, split z into result[i] = z % MPD_RADIX
         * and carry = z / MPD_RADIX. Let N be the size of carry[] and cmax the
         * maximum carry.
         *
         * Limits for the 64 bit build:
         *
         *   N    = 2**192
         *   cmax = 627710135393475385904124401220046371710
         *
         *   1) cmax + zmax < N, so the addition does not overflow.
         *
         *   2) (cmax + zmax) / MPD_RADIX == cmax.
         *
         *   3) If c <= cmax, then c_next = (c + zmax) / MPD_RADIX <= cmax.
         */
        public static void crt3(Span<ulong> x1, Span<ulong> x2, Span<ulong> x3, ulong rsize)
        {
            ulong p1 = mpd_moduli[P1];
            ulong umod;
            ulong a1, a2, a3;
            ulong s;
            Span<ulong> z = stackalloc ulong[3];
            Span<ulong> t = stackalloc ulong[3];
            Span<ulong> carry = stackalloc ulong[3]; /* {0,0,0} */
            ulong hi, lo;
            ulong i;

            for (i = 0; i < rsize; i++)
            {
                a1 = x1[(int)i];
                a2 = x2[(int)i];
                a3 = x3[(int)i];

                std_setmodulus(P2, out umod);
                s = ext_submod(a2, a1, umod);
                s = x64_mulmod(s, INV_P1_MOD_P2, umod);

                _mpd_mul_words(out hi, out lo, s, p1);
                lo = lo + a1;
                if (lo < a1) hi++;

                std_setmodulus(P3, out umod);
                s = dw_submod(a3, hi, lo, umod);
                s = x64_mulmod(s, INV_P1P2_MOD_P3, umod);

                z[0] = lo;
                z[1] = hi;
                z[2] = 0;

                _crt_mulP1P2_3(t, s);
                _crt_add3(z, t);
                _crt_add3(carry, z);

                x1[(int)i] = _crt_div3(carry, carry, MPD_RADIX);
            }

            Debug.Assert(carry[0] == 0 && carry[1] == 0 && carry[2] == 0);
        }
    }
}
