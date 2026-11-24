// Modules/_decimal/libmpdec/umodarith.h (CPython 3.12.15, libmpdec 2.5.1), CONFIG_64.
using System.Runtime.CompilerServices;

namespace SharpPy.Modules.Mpdecimal
{
    internal static partial class Mpd
    {
        /* Bignum: Low level routines for unsigned modular arithmetic. These are
           used in the fast convolution functions for very large coefficients. */

        /**************************************************************************/
        /*                        ANSI modular arithmetic                         */
        /**************************************************************************/

        /*
         * Restrictions: a < m and b < m
         * ACL2 proof: umodarith.lisp: addmod-correct
         */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong addmod(ulong a, ulong b, ulong m)
        {
            ulong s;

            s = a + b;
            s = (s < a) ? s - m : s;
            s = (s >= m) ? s - m : s;

            return s;
        }

        /*
         * Restrictions: a < m and b < m
         * ACL2 proof: umodarith.lisp: submod-2-correct
         */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong submod(ulong a, ulong b, ulong m)
        {
            ulong d;

            d = a - b;
            d = (a < b) ? d + m : d;

            return d;
        }

        /*
         * Restrictions: a < 2m and b < 2m
         * ACL2 proof: umodarith.lisp: section ext-submod
         */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ext_submod(ulong a, ulong b, ulong m)
        {
            ulong d;

            a = (a >= m) ? a - m : a;
            b = (b >= m) ? b - m : b;

            d = a - b;
            d = (a < b) ? d + m : d;

            return d;
        }

        /*
         * Reduce double word modulo m.
         * Restrictions: m != 0
         * ACL2 proof: umodarith.lisp: section dw-reduce
         */
        public static ulong dw_reduce(ulong hi, ulong lo, ulong m)
        {
            ulong r1, r2, w;

            _mpd_div_word(out w, out r1, hi, m);
            _mpd_div_words(out w, out r2, r1, lo, m);

            return r2;
        }

        /*
         * Subtract double word from a.
         * Restrictions: a < m
         * ACL2 proof: umodarith.lisp: section dw-submod
         */
        public static ulong dw_submod(ulong a, ulong hi, ulong lo, ulong m)
        {
            ulong d, r;

            r = dw_reduce(hi, lo, m);
            d = a - r;
            d = (a < r) ? d + m : d;

            return d;
        }

        /**************************************************************************/
        /*                        64-bit modular arithmetic                       */
        /**************************************************************************/

        /*
         * A proof of the algorithm is in literature/mulmod-64.txt. An ACL2
         * proof is in umodarith.lisp: section "Fast modular reduction".
         *
         * Algorithm: calculate (a * b) % p:
         *
         *   a) hi, lo <- a * b       # Calculate a * b.
         *
         *   b) hi, lo <-  R(hi, lo)  # Reduce modulo p.
         *
         *   c) Repeat step b) until 0 <= hi * 2**64 + lo < 2*p.
         *
         *   d) If the result is less than p, return lo. Otherwise return lo - p.
         */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong x64_mulmod(ulong a, ulong b, ulong m)
        {
            ulong hi, lo, x, y;

            _mpd_mul_words(out hi, out lo, a, b);

            if ((m & (1UL << 32)) != 0)
            { /* P1 */

                /* first reduction */
                x = y = hi;
                hi >>= 32;

                x = lo - x;
                if (x > lo) hi--;

                y <<= 32;
                lo = y + x;
                if (lo < y) hi++;

                /* second reduction */
                x = y = hi;
                hi >>= 32;

                x = lo - x;
                if (x > lo) hi--;

                y <<= 32;
                lo = y + x;
                if (lo < y) hi++;

                return (hi != 0 || lo >= m ? lo - m : lo);
            }
            else if ((m & (1UL << 34)) != 0)
            { /* P2 */

                /* first reduction */
                x = y = hi;
                hi >>= 30;

                x = lo - x;
                if (x > lo) hi--;

                y <<= 34;
                lo = y + x;
                if (lo < y) hi++;

                /* second reduction */
                x = y = hi;
                hi >>= 30;

                x = lo - x;
                if (x > lo) hi--;

                y <<= 34;
                lo = y + x;
                if (lo < y) hi++;

                /* third reduction */
                x = y = hi;
                hi >>= 30;

                x = lo - x;
                if (x > lo) hi--;

                y <<= 34;
                lo = y + x;
                if (lo < y) hi++;

                return (hi != 0 || lo >= m ? lo - m : lo);
            }
            else
            { /* P3 */

                /* first reduction */
                x = y = hi;
                hi >>= 24;

                x = lo - x;
                if (x > lo) hi--;

                y <<= 40;
                lo = y + x;
                if (lo < y) hi++;

                /* second reduction */
                x = y = hi;
                hi >>= 24;

                x = lo - x;
                if (x > lo) hi--;

                y <<= 40;
                lo = y + x;
                if (lo < y) hi++;

                /* third reduction */
                x = y = hi;
                hi >>= 24;

                x = lo - x;
                if (x > lo) hi--;

                y <<= 40;
                lo = y + x;
                if (lo < y) hi++;

                return (hi != 0 || lo >= m ? lo - m : lo);
            }
        }

        // x64_mulmod2c
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void x64_mulmod2c(ref ulong a, ref ulong b, ulong w, ulong m)
        {
            a = x64_mulmod(a, w, m);
            b = x64_mulmod(b, w, m);
        }

        // x64_mulmod2
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void x64_mulmod2(ref ulong a0, ulong b0, ref ulong a1, ulong b1, ulong m)
        {
            a0 = x64_mulmod(a0, b0, m);
            a1 = x64_mulmod(a1, b1, m);
        }

        // x64_powmod
        public static ulong x64_powmod(ulong @base, ulong exp, ulong umod)
        {
            ulong r = 1;

            while (exp > 0)
            {
                if ((exp & 1) != 0)
                    r = x64_mulmod(r, @base, umod);
                @base = x64_mulmod(@base, @base, umod);
                exp >>= 1;
            }

            return r;
        }
    }
}
