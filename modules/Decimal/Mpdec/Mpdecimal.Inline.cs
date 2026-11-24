// Modules/_decimal/libmpdec/mpdecimal.c (CPython 3.12.15, libmpdec 2.5.1), lines 1-770:
// version, the performance critical inline functions, inline memory handling and the setters of
// decimal attributes. The rest of mpdecimal.c is in Mpdecimal.*.cs.
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace SharpPy.Modules.Mpdecimal
{
    internal static partial class Mpd
    {
        public const long MPD_NEWTONDIV_CUTOFF = 1024L;

        // static mpd_uint_t data_one[1] = {1}; data_zero[1] = {0};
        // static const mpd_t one, minus_one, zero
        private static readonly mpd_t one = new mpd_t(MPD_STATIC | MPD_CONST_DATA, 0, 1, 1, 1, new ulong[] { 1 });
        private static readonly mpd_t minus_one = new mpd_t(MPD_NEG | MPD_STATIC | MPD_CONST_DATA, 0, 1, 1, 1, new ulong[] { 1 });
        private static readonly mpd_t zero = new mpd_t(MPD_STATIC | MPD_CONST_DATA, 0, 1, 1, 1, new ulong[] { 0 });

        /******************************************************************************/
        /*                                  Version                                   */
        /******************************************************************************/

        // mpd_version
        public static string mpd_version() => MPD_VERSION;

        /******************************************************************************/
        /*                  Performance critical inline functions                     */
        /******************************************************************************/

        /* Digits in a word, primarily useful for the most significant word. */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int mpd_word_digits(ulong word)
        {
            if (word < mpd_pow10[9])
            {
                if (word < mpd_pow10[4])
                {
                    if (word < mpd_pow10[2])
                        return (word < mpd_pow10[1]) ? 1 : 2;
                    return (word < mpd_pow10[3]) ? 3 : 4;
                }
                if (word < mpd_pow10[6])
                    return (word < mpd_pow10[5]) ? 5 : 6;
                if (word < mpd_pow10[8])
                    return (word < mpd_pow10[7]) ? 7 : 8;
                return 9;
            }
            if (word < mpd_pow10[14])
            {
                if (word < mpd_pow10[11])
                    return (word < mpd_pow10[10]) ? 10 : 11;
                if (word < mpd_pow10[13])
                    return (word < mpd_pow10[12]) ? 12 : 13;
                return 14;
            }
            if (word < mpd_pow10[18])
            {
                if (word < mpd_pow10[16])
                    return (word < mpd_pow10[15]) ? 15 : 16;
                return (word < mpd_pow10[17]) ? 17 : 18;
            }

            return (word < mpd_pow10[19]) ? 19 : 20;
        }

        /* Adjusted exponent */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long mpd_adjexp(mpd_t dec) => (dec.exp + dec.digits) - 1;

        /* Etiny */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long mpd_etiny(ref mpd_context_t ctx) => ctx.emin - (ctx.prec - 1);

        /* Etop: used for folding down in IEEE clamping */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long mpd_etop(ref mpd_context_t ctx) => ctx.emax - (ctx.prec - 1);

        /* Most significant word */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong mpd_msword(mpd_t dec)
        {
            Debug.Assert(dec.len > 0);
            return dec.data[dec.len - 1];
        }

        /* Most significant digit of a word */
        public static ulong mpd_msd(ulong word)
        {
            int n = mpd_word_digits(word);
            return word / mpd_pow10[n - 1];
        }

        /* Least significant digit of a word */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong mpd_lsd(ulong word) => word % 10;

        /* Coefficient size needed to store 'digits' */
        public static long mpd_digits_to_size(long digits)
        {
            _mpd_idiv_word(out long q, out long r, digits, MPD_RDIGITS);
            return (r == 0) ? q : q + 1;
        }

        /* Number of digits in the exponent. Not defined for MPD_SSIZE_MIN. */
        public static int mpd_exp_digits(long exp)
        {
            exp = (exp < 0) ? -exp : exp;
            return mpd_word_digits((ulong)exp);
        }

        /* Canonical */
        public static bool mpd_iscanonical(mpd_t dec) => true;

        /* Finite */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool mpd_isfinite(mpd_t dec) => (dec.flags & MPD_SPECIAL) == 0;

        /* Infinite */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool mpd_isinfinite(mpd_t dec) => (dec.flags & MPD_INF) != 0;

        /* NaN */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool mpd_isnan(mpd_t dec) => (dec.flags & (MPD_NAN | MPD_SNAN)) != 0;

        /* Negative */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool mpd_isnegative(mpd_t dec) => (dec.flags & MPD_NEG) != 0;

        /* Positive */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool mpd_ispositive(mpd_t dec) => (dec.flags & MPD_NEG) == 0;

        /* qNaN */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool mpd_isqnan(mpd_t dec) => (dec.flags & MPD_NAN) != 0;

        /* Signed */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool mpd_issigned(mpd_t dec) => (dec.flags & MPD_NEG) != 0;

        /* sNaN */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool mpd_issnan(mpd_t dec) => (dec.flags & MPD_SNAN) != 0;

        /* Special */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool mpd_isspecial(mpd_t dec) => (dec.flags & MPD_SPECIAL) != 0;

        /* Zero */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool mpd_iszero(mpd_t dec) => !mpd_isspecial(dec) && mpd_msword(dec) == 0;

        /* Test for zero when specials have been ruled out already */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool mpd_iszerocoeff(mpd_t dec) => mpd_msword(dec) == 0;

        /* Normal */
        public static bool mpd_isnormal(mpd_t dec, ref mpd_context_t ctx)
        {
            if (mpd_isspecial(dec)) return false;
            if (mpd_iszerocoeff(dec)) return false;

            return mpd_adjexp(dec) >= ctx.emin;
        }

        /* Subnormal */
        public static bool mpd_issubnormal(mpd_t dec, ref mpd_context_t ctx)
        {
            if (mpd_isspecial(dec)) return false;
            if (mpd_iszerocoeff(dec)) return false;

            return mpd_adjexp(dec) < ctx.emin;
        }

        /* Odd word */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool mpd_isoddword(ulong word) => (word & 1) != 0;

        /* Odd coefficient */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool mpd_isoddcoeff(mpd_t dec) => mpd_isoddword(dec.data[0]);

        /* 0 if dec is positive, 1 if dec is negative */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte mpd_sign(mpd_t dec) => (byte)(dec.flags & MPD_NEG);

        /* 1 if dec is positive, -1 if dec is negative */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int mpd_arith_sign(mpd_t dec) => 1 - 2 * (dec.flags & MPD_NEG);

        /* Radix */
        public static long mpd_radix() => 10;

        /* Dynamic decimal */
        public static bool mpd_isdynamic(mpd_t dec) => (dec.flags & MPD_STATIC) == 0;

        /* Static decimal */
        public static bool mpd_isstatic(mpd_t dec) => (dec.flags & MPD_STATIC) != 0;

        /* Data of decimal is dynamic */
        public static bool mpd_isdynamic_data(mpd_t dec) => (dec.flags & MPD_DATAFLAGS) == 0;

        /* Data of decimal is static */
        public static bool mpd_isstatic_data(mpd_t dec) => (dec.flags & MPD_STATIC_DATA) != 0;

        /* Data of decimal is shared */
        public static bool mpd_isshared_data(mpd_t dec) => (dec.flags & MPD_SHARED_DATA) != 0;

        /* Data of decimal is const */
        public static bool mpd_isconst_data(mpd_t dec) => (dec.flags & MPD_CONST_DATA) != 0;

        /******************************************************************************/
        /*                         Inline memory handling                             */
        /******************************************************************************/

        /* Fill destination with zeros */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void mpd_uint_zero(Span<ulong> dest, ulong len) => dest.Slice(0, (int)len).Clear();

        /* Free a decimal */
        public static void mpd_del(mpd_t dec)
        {
            // mpd_free of the data and of the struct: the collector reclaims both.
        }

        /*
         * Resize the coefficient. Existing data up to 'nwords' is left untouched.
         * Return 1 on success, 0 otherwise.
         *
         * Input invariant: MPD_MINALLOC <= result->alloc.
         *
         * Case nwords == result->alloc:
         *     'result' is unchanged. Return 1.
         *
         * Case nwords > result->alloc:
         *   Case realloc success:
         *     The value of 'result' does not change. Return 1.
         *   Case realloc failure:
         *     'result' is NaN, status is updated with MPD_Malloc_error. Return 0.
         *
         * Case nwords < result->alloc:
         *   Case is_static_data or realloc failure [1]:
         *     'result' is unchanged. Return 1.
         *   Case realloc success:
         *     The value of result is undefined (expected). Return 1.
         *
         *
         * [1] In that case the old (now oversized) area is still valid.
         */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool mpd_qresize(mpd_t result, long nwords, ref uint status)
        {
            Debug.Assert(!mpd_isconst_data(result)); /* illegal operation for a const */
            Debug.Assert(!mpd_isshared_data(result)); /* illegal operation for a shared */
            Debug.Assert(MPD_MINALLOC <= result.alloc);

            nwords = (nwords <= MPD_MINALLOC) ? MPD_MINALLOC : nwords;
            if (nwords == result.alloc)
                return true;
            if (mpd_isstatic_data(result))
            {
                if (nwords > result.alloc)
                    return mpd_switch_to_dyn(result, nwords, ref status);
                return true;
            }

            return mpd_realloc_dyn(result, nwords, ref status);
        }

        /* Same as mpd_qresize, but do not set the result no NaN on failure. */
        private static bool mpd_qresize_cxx(mpd_t result, long nwords)
        {
            Debug.Assert(!mpd_isconst_data(result)); /* illegal operation for a const */
            Debug.Assert(!mpd_isshared_data(result)); /* illegal operation for a shared */
            Debug.Assert(MPD_MINALLOC <= result.alloc);

            nwords = (nwords <= MPD_MINALLOC) ? MPD_MINALLOC : nwords;
            if (nwords == result.alloc)
                return true;
            if (mpd_isstatic_data(result))
            {
                if (nwords > result.alloc)
                    return mpd_switch_to_dyn_cxx(result, nwords);
                return true;
            }

            return mpd_realloc_dyn_cxx(result, nwords);
        }

        /* Same as mpd_qresize, but the complete coefficient (including the old
         * memory area!) is initialized to zero. */
        public static bool mpd_qresize_zero(mpd_t result, long nwords, ref uint status)
        {
            Debug.Assert(!mpd_isconst_data(result)); /* illegal operation for a const */
            Debug.Assert(!mpd_isshared_data(result)); /* illegal operation for a shared */
            Debug.Assert(MPD_MINALLOC <= result.alloc);

            nwords = (nwords <= MPD_MINALLOC) ? MPD_MINALLOC : nwords;
            if (nwords != result.alloc)
            {
                if (mpd_isstatic_data(result))
                {
                    if (nwords > result.alloc)
                        return mpd_switch_to_dyn_zero(result, nwords, ref status);
                }
                else if (!mpd_realloc_dyn(result, nwords, ref status))
                {
                    return false;
                }
            }

            mpd_uint_zero(result.data, (ulong)nwords);
            return true;
        }

        /*
         * Reduce memory size for the coefficient to MPD_MINALLOC. In theory,
         * realloc may fail even when reducing the memory size. But in that case
         * the old memory area is always big enough, so checking for MPD_Malloc_error
         * is not imperative.
         */
        public static void mpd_minalloc(mpd_t result)
        {
            Debug.Assert(!mpd_isconst_data(result)); /* illegal operation for a const */
            Debug.Assert(!mpd_isshared_data(result)); /* illegal operation for a shared */

            if (!mpd_isstatic_data(result) && result.alloc > MPD_MINALLOC)
            {
                byte err = 0;
                result.data = mpd_realloc(result.data, (ulong)MPD_MINALLOC, ref err);
                if (err == 0)
                    result.alloc = MPD_MINALLOC;
            }
        }

        // mpd_resize
        public static bool mpd_resize(mpd_t result, long nwords, ref mpd_context_t ctx)
        {
            uint status = 0;
            if (!mpd_qresize(result, nwords, ref status))
            {
                mpd_addstatus_raise(ref ctx, status);
                return false;
            }
            return true;
        }

        // mpd_resize_zero
        public static bool mpd_resize_zero(mpd_t result, long nwords, ref mpd_context_t ctx)
        {
            uint status = 0;
            if (!mpd_qresize_zero(result, nwords, ref status))
            {
                mpd_addstatus_raise(ref ctx, status);
                return false;
            }
            return true;
        }

        /******************************************************************************/
        /*                       Set attributes of a decimal                          */
        /******************************************************************************/

        /* Set digits. Assumption: result->len is initialized and > 0. */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void mpd_setdigits(mpd_t result)
        {
            long wdigits = mpd_word_digits(mpd_msword(result));
            result.digits = wdigits + (result.len - 1) * MPD_RDIGITS;
        }

        /* Set sign */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void mpd_set_sign(mpd_t result, byte sign)
        {
            result.flags &= unchecked((byte)~MPD_NEG);
            result.flags |= sign;
        }

        /* Copy sign from another decimal */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void mpd_signcpy(mpd_t result, mpd_t a)
        {
            byte sign = (byte)(a.flags & MPD_NEG);

            result.flags &= unchecked((byte)~MPD_NEG);
            result.flags |= sign;
        }

        /* Set infinity */
        public static void mpd_set_infinity(mpd_t result)
        {
            result.flags &= unchecked((byte)~MPD_SPECIAL);
            result.flags |= MPD_INF;
        }

        /* Set qNaN */
        public static void mpd_set_qnan(mpd_t result)
        {
            result.flags &= unchecked((byte)~MPD_SPECIAL);
            result.flags |= MPD_NAN;
        }

        /* Set sNaN */
        public static void mpd_set_snan(mpd_t result)
        {
            result.flags &= unchecked((byte)~MPD_SPECIAL);
            result.flags |= MPD_SNAN;
        }

        /* Set to negative */
        public static void mpd_set_negative(mpd_t result) => result.flags |= MPD_NEG;

        /* Set to positive */
        public static void mpd_set_positive(mpd_t result) => result.flags &= unchecked((byte)~MPD_NEG);

        /* Set to dynamic */
        public static void mpd_set_dynamic(mpd_t result) => result.flags &= unchecked((byte)~MPD_STATIC);

        /* Set to static */
        public static void mpd_set_static(mpd_t result) => result.flags |= MPD_STATIC;

        /* Set data to dynamic */
        public static void mpd_set_dynamic_data(mpd_t result) => result.flags &= unchecked((byte)~MPD_DATAFLAGS);

        /* Set data to static */
        public static void mpd_set_static_data(mpd_t result)
        {
            result.flags &= unchecked((byte)~MPD_DATAFLAGS);
            result.flags |= MPD_STATIC_DATA;
        }

        /* Set data to shared */
        public static void mpd_set_shared_data(mpd_t result)
        {
            result.flags &= unchecked((byte)~MPD_DATAFLAGS);
            result.flags |= MPD_SHARED_DATA;
        }

        /* Set data to const */
        public static void mpd_set_const_data(mpd_t result)
        {
            result.flags &= unchecked((byte)~MPD_DATAFLAGS);
            result.flags |= MPD_CONST_DATA;
        }

        /* Clear flags, preserving memory attributes. */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void mpd_clear_flags(mpd_t result) => result.flags &= (MPD_STATIC | MPD_DATAFLAGS);

        /* Set flags, preserving memory attributes. */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void mpd_set_flags(mpd_t result, byte flags)
        {
            result.flags &= (MPD_STATIC | MPD_DATAFLAGS);
            result.flags |= flags;
        }

        /* Copy flags, preserving memory attributes of result. */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void mpd_copy_flags(mpd_t result, mpd_t a)
        {
            byte aflags = a.flags;
            result.flags &= (MPD_STATIC | MPD_DATAFLAGS);
            result.flags |= (byte)(aflags & ~(MPD_STATIC | MPD_DATAFLAGS));
        }

        /* Initialize a workcontext from ctx. Set traps, flags and newtrap to 0. */
        private static void mpd_workcontext(ref mpd_context_t workctx, ref mpd_context_t ctx)
        {
            workctx.prec = ctx.prec;
            workctx.emax = ctx.emax;
            workctx.emin = ctx.emin;
            workctx.round = ctx.round;
            workctx.traps = 0;
            workctx.status = 0;
            workctx.newtrap = 0;
            workctx.clamp = ctx.clamp;
            workctx.allcr = ctx.allcr;
        }
    }
}
