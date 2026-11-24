// Modules/_decimal/libmpdec/typearith.h (CPython 3.12.15, libmpdec 2.5.1), CONFIG_64.
// The reference Windows x64 build (MASM) multiplies with _umul128 (Math.BigMul here) and divides
// with vcdiv64.asm's divq; the division is the ANSI branch (Hacker's Delight divlu), which gives the
// same quotient and remainder as divq for every input the library passes (hi < d).
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace SharpPy.Modules.Mpdecimal
{
    internal static partial class Mpd
    {
        /** ------------------------------------------------------------
         **           Double width multiplication and division
         ** ------------------------------------------------------------
         */

        // _mpd_mul_words (MASM: *lo = _umul128(a, b, hi))
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void _mpd_mul_words(out ulong hi, out ulong lo, ulong a, ulong b)
        {
            hi = Math.BigMul(a, b, out lo);
        }

        /*
         * By Henry S. Warren: http://www.hackersdelight.org/HDcode/divlu.c.txt
         * http://www.hackersdelight.org/permissions.htm:
         * "You are free to use, copy, and distribute any of the code on this web
         *  site, whether modified by you or not. You need not give attribution."
         *
         * Slightly modified, comments are mine.
         */
        // nlz
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int nlz(ulong x)
        {
            int n;

            if (x == 0) return (64);

            n = 0;
            if (x <= 0x00000000FFFFFFFF) { n = n + 32; x = x << 32; }
            if (x <= 0x0000FFFFFFFFFFFF) { n = n + 16; x = x << 16; }
            if (x <= 0x00FFFFFFFFFFFFFF) { n = n + 8; x = x << 8; }
            if (x <= 0x0FFFFFFFFFFFFFFF) { n = n + 4; x = x << 4; }
            if (x <= 0x3FFFFFFFFFFFFFFF) { n = n + 2; x = x << 2; }
            if (x <= 0x7FFFFFFFFFFFFFFF) { n = n + 1; }

            return n;
        }

        // _mpd_div_words (ANSI, no uint128)
        public static void _mpd_div_words(out ulong q, out ulong r, ulong u1, ulong u0, ulong v)
        {
            const ulong b = 4294967296;
            ulong un1, un0,
                  vn1, vn0,
                  q1, q0,
                  un32, un21, un10,
                  rhat, t;
            int s;

            Debug.Assert(u1 < v);

            s = nlz(v);
            v = v << s;
            vn1 = v >> 32;
            vn0 = v & 0xFFFFFFFF;

            t = (s == 0) ? 0 : u0 >> (64 - s);
            un32 = (u1 << s) | t;
            un10 = u0 << s;

            un1 = un10 >> 32;
            un0 = un10 & 0xFFFFFFFF;

            q1 = un32 / vn1;
            rhat = un32 - q1 * vn1;
        again1:
            if (q1 >= b || q1 * vn0 > b * rhat + un1)
            {
                q1 = q1 - 1;
                rhat = rhat + vn1;
                if (rhat < b) goto again1;
            }

            /*
             *  Before again1 we had:
             *      (1) q1*vn1   + rhat         = un32
             *      (2) q1*vn1*b + rhat*b + un1 = un32*b + un1
             *
             *  The statements inside the if-clause do not change the value
             *  of the left-hand side of (2), and the loop is only exited
             *  if q1*vn0 <= rhat*b + un1, so:
             *
             *      (3) q1*vn1*b + q1*vn0 <= un32*b + un1
             *      (4)              q1*v <= un32*b + un1
             *      (5)                 0 <= un32*b + un1 - q1*v
             *
             *  By (5) we are certain that the possible add-back step from
             *  Knuth's algorithm D is never required.
             *
             *  Since the final quotient is less than 2**64, the following
             *  must be true:
             *
             *      (6) un32*b + un1 - q1*v <= UINT64_MAX
             *
             *  This means that in the following line, the high words
             *  of un32*b and q1*v can be discarded without any effect
             *  on the result.
             */
            un21 = un32 * b + un1 - q1 * v;

            q0 = un21 / vn1;
            rhat = un21 - q0 * vn1;
        again2:
            if (q0 >= b || q0 * vn0 > b * rhat + un0)
            {
                q0 = q0 - 1;
                rhat = rhat + vn1;
                if (rhat < b) goto again2;
            }

            q = q1 * b + q0;
            r = (un21 * b + un0 - q0 * v) >> s;
        }

        // _mpd_divmod_pow10: #define DIVMOD(q, r, v, d) *q = v / d; *r = v - *q * d
        public static void _mpd_divmod_pow10(out ulong q, out ulong r, ulong v, ulong exp)
        {
            Debug.Assert(exp <= 19);

            ulong d;
            switch (exp)
            {
                case 0: q = v; r = 0; return;
                case 1: d = 10UL; break;
                case 2: d = 100UL; break;
                case 3: d = 1000UL; break;
                case 4: d = 10000UL; break;
                case 5: d = 100000UL; break;
                case 6: d = 1000000UL; break;
                case 7: d = 10000000UL; break;
                case 8: d = 100000000UL; break;
                case 9: d = 1000000000UL; break;
                case 10: d = 10000000000UL; break;
                case 11: d = 100000000000UL; break;
                case 12: d = 1000000000000UL; break;
                case 13: d = 10000000000000UL; break;
                case 14: d = 100000000000000UL; break;
                case 15: d = 1000000000000000UL; break;
                case 16: d = 10000000000000000UL; break;
                case 17: d = 100000000000000000UL; break;
                case 18: d = 1000000000000000000UL; break;
                default: d = 10000000000000000000UL; break; /* GCOV_NOT_REACHED */
            }
            // The divisor is a constant in each case of the C switch; here it is a variable.
            q = v / d;
            r = v - q * d;
        }

        // _mpd_div_word
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void _mpd_div_word(out ulong q, out ulong r, ulong v, ulong d)
        {
            q = v / d;
            r = v - q * d;
        }

        // _mpd_idiv_word
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void _mpd_idiv_word(out long q, out long r, long v, long d)
        {
            q = v / d;
            r = v - q * d;
        }

        /** ------------------------------------------------------------
         **              Arithmetic with overflow checking
         ** ------------------------------------------------------------
         */

        /* The following macros do call exit() in case of an overflow.
           If the library is used correctly (i.e. with valid context
           parameters), such overflows cannot occur. The macros are used
           as sanity checks in a couple of strategic places and should
           be viewed as a handwritten version of gcc's -ftrapv option. */

        // add_size_t
        public static ulong add_size_t(ulong a, ulong b)
        {
            if (a > MPD_SIZE_MAX - b)
                throw mpd_err_fatal("add_size_t(): overflow: check the context"); /* GCOV_NOT_REACHED */
            return a + b;
        }

        // sub_size_t
        public static ulong sub_size_t(ulong a, ulong b)
        {
            if (b > a)
                throw mpd_err_fatal("sub_size_t(): overflow: check the context"); /* GCOV_NOT_REACHED */
            return a - b;
        }

        // mul_size_t
        public static ulong mul_size_t(ulong a, ulong b)
        {
            _mpd_mul_words(out ulong hi, out ulong lo, a, b);
            if (hi != 0)
                throw mpd_err_fatal("mul_size_t(): overflow: check the context"); /* GCOV_NOT_REACHED */
            return lo;
        }

        // add_size_t_overflow
        public static ulong add_size_t_overflow(ulong a, ulong b, out ulong overflow)
        {
            ulong ret;

            overflow = 0;
            ret = a + b;
            if (ret < a) overflow = 1;
            return ret;
        }

        // mul_size_t_overflow
        public static ulong mul_size_t_overflow(ulong a, ulong b, out ulong overflow)
        {
            _mpd_mul_words(out ulong hi, out ulong lo, a, b);
            overflow = hi;
            return lo;
        }

        // mod_mpd_ssize_t
        public static long mod_mpd_ssize_t(long a, long m)
        {
            long r = a % m;
            return (r < 0) ? r + m : r;
        }

        // mulmod_size_t
        public static ulong mulmod_size_t(ulong a, ulong b, ulong m)
        {
            _mpd_mul_words(out ulong hi, out ulong lo, a, b);
            _mpd_div_words(out ulong q, out ulong r, hi, lo, m);

            return r;
        }
    }
}
