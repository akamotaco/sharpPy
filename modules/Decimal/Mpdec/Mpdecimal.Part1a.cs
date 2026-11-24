// Modules/_decimal/libmpdec/mpdecimal.c (CPython 3.12.15, libmpdec 2.5.1), lines 771-2125:
// getting and setting parts of decimals, gathering information about a decimal, getting and
// setting decimals, filtering input and finalizing output of functions, copying. CONFIG_64.
using System;
using System.Diagnostics;

namespace SharpPy.Modules.Mpdecimal
{
    internal static partial class Mpd
    {
        /******************************************************************************/
        /*                  Getting and setting parts of decimals                     */
        /******************************************************************************/

        /* Flip the sign of a decimal */
        private static void _mpd_negate(mpd_t dec)
        {
            dec.flags ^= MPD_NEG;
        }

        /* Set coefficient to zero */
        public static void mpd_zerocoeff(mpd_t result)
        {
            mpd_minalloc(result);
            result.digits = 1;
            result.len = 1;
            result.data[0] = 0;
        }

        /* Set the coefficient to all nines. */
        public static void mpd_qmaxcoeff(mpd_t result, ref mpd_context_t ctx, ref uint status)
        {
            long len, r;

            _mpd_idiv_word(out len, out r, ctx.prec, MPD_RDIGITS);
            len = (r == 0) ? len : len + 1;

            if (!mpd_qresize(result, len, ref status))
                return;

            result.len = len;
            result.digits = ctx.prec;

            --len;
            if (r > 0)
                result.data[len--] = mpd_pow10[r] - 1;
            for (; len >= 0; --len)
                result.data[len] = MPD_RADIX - 1;
        }

        /*
         * Cut off the most significant digits so that the rest fits in ctx->prec.
         * Cannot fail.
         */
        private static void _mpd_cap(mpd_t result, ref mpd_context_t ctx)
        {
            uint dummy = 0;
            long len, r;

            if (result.len > 0 && result.digits > ctx.prec)
            {
                _mpd_idiv_word(out len, out r, ctx.prec, MPD_RDIGITS);
                len = (r == 0) ? len : len + 1;

                if (r != 0)
                    result.data[len - 1] %= mpd_pow10[r];

                len = _mpd_real_size(result.data, len);
                /* resize to fewer words cannot fail */
                mpd_qresize(result, len, ref dummy);
                result.len = len;
                mpd_setdigits(result);
            }
            if (mpd_iszero(result))
                _settriple(result, mpd_sign(result), 0, result.exp);
        }

        /*
         * Cut off the most significant digits of a NaN payload so that the rest
         * fits in ctx->prec - ctx->clamp. Cannot fail.
         */
        private static void _mpd_fix_nan(mpd_t result, ref mpd_context_t ctx)
        {
            uint dummy = 0;
            long prec;
            long len, r;

            prec = ctx.prec - ctx.clamp;
            if (result.len > 0 && result.digits > prec)
            {
                if (prec == 0)
                {
                    mpd_minalloc(result);
                    result.len = result.digits = 0;
                }
                else
                {
                    _mpd_idiv_word(out len, out r, prec, MPD_RDIGITS);
                    len = (r == 0) ? len : len + 1;

                    if (r != 0)
                        result.data[len - 1] %= mpd_pow10[r];

                    len = _mpd_real_size(result.data, len);
                    /* resize to fewer words cannot fail */
                    mpd_qresize(result, len, ref dummy);
                    result.len = len;
                    mpd_setdigits(result);
                    if (mpd_iszerocoeff(result))
                    {
                        /* NaN0 is not a valid representation */
                        result.len = result.digits = 0;
                    }
                }
            }
        }

        /*
         * Get n most significant digits from a decimal, where 0 < n <= MPD_UINT_DIGITS.
         * Assumes MPD_UINT_DIGITS == MPD_RDIGITS+1, which is true for 32 and 64 bit
         * machines.
         *
         * The result of the operation will be in lo. If the operation is impossible,
         * hi will be nonzero. This is used to indicate an error.
         */
        private static void _mpd_get_msdigits(out ulong hi, out ulong lo, mpd_t dec, uint n)
        {
            ulong r, tmp;

            Debug.Assert(0 < n && n <= MPD_RDIGITS + 1);

            _mpd_div_word(out tmp, out r, (ulong)dec.digits, MPD_RDIGITS);
            r = (r == 0) ? MPD_RDIGITS : r; /* digits in the most significant word */

            hi = 0;
            lo = dec.data[dec.len - 1];
            if (n <= r)
            {
                lo /= mpd_pow10[r - n];
            }
            else if (dec.len > 1)
            {
                /* at this point 1 <= r < n <= MPD_RDIGITS+1 */
                _mpd_mul_words(out hi, out lo, lo, mpd_pow10[n - r]);
                tmp = dec.data[dec.len - 2] / mpd_pow10[MPD_RDIGITS - (n - r)];
                lo = lo + tmp;
                if (lo < tmp) hi++;
            }
        }

        /******************************************************************************/
        /*                   Gathering information about a decimal                    */
        /******************************************************************************/

        /* The real size of the coefficient without leading zero words. */
        private static long _mpd_real_size(ReadOnlySpan<ulong> data, long size)
        {
            while (size > 1 && data[(int)(size - 1)] == 0)
                size--;

            return size;
        }

        /* Return number of trailing zeros. No errors are possible. */
        public static long mpd_trail_zeros(mpd_t dec)
        {
            ulong word;
            long i, tz = 0;

            for (i = 0; i < dec.len; ++i)
            {
                if (dec.data[i] != 0)
                {
                    word = dec.data[i];
                    tz = i * MPD_RDIGITS;
                    while (word % 10 == 0)
                    {
                        word /= 10;
                        tz++;
                    }
                    break;
                }
            }

            return tz;
        }

        /* Integer: Undefined for specials */
        private static bool _mpd_isint(mpd_t dec)
        {
            long tz;

            if (mpd_iszerocoeff(dec))
                return true;

            tz = mpd_trail_zeros(dec);
            return (dec.exp + tz >= 0);
        }

        /* Integer */
        public static bool mpd_isinteger(mpd_t dec)
        {
            if (mpd_isspecial(dec))
                return false;
            return _mpd_isint(dec);
        }

        /* Word is a power of 10 */
        private static bool mpd_word_ispow10(ulong word)
        {
            int n;

            n = mpd_word_digits(word);
            if (word == mpd_pow10[n - 1])
                return true;

            return false;
        }

        /* Coefficient is a power of 10 */
        private static bool mpd_coeff_ispow10(mpd_t dec)
        {
            if (mpd_word_ispow10(mpd_msword(dec)))
            {
                if (_mpd_isallzero(dec.data, dec.len - 1))
                    return true;
            }

            return false;
        }

        /* All digits of a word are nines */
        private static bool mpd_word_isallnine(ulong word)
        {
            int n;

            n = mpd_word_digits(word);
            if (word == mpd_pow10[n] - 1)
                return true;

            return false;
        }

        /* All digits of the coefficient are nines */
        private static bool mpd_coeff_isallnine(mpd_t dec)
        {
            if (mpd_word_isallnine(mpd_msword(dec)))
            {
                if (_mpd_isallnine(dec.data, dec.len - 1))
                    return true;
            }

            return false;
        }

        /* Odd decimal: Undefined for non-integers! */
        public static bool mpd_isodd(mpd_t dec)
        {
            ulong q, r;
            Debug.Assert(mpd_isinteger(dec));
            if (mpd_iszerocoeff(dec)) return false;
            if (dec.exp < 0)
            {
                _mpd_div_word(out q, out r, (ulong)(-dec.exp), MPD_RDIGITS);
                q = dec.data[q] / mpd_pow10[r];
                return mpd_isoddword(q);
            }
            return dec.exp == 0 && mpd_isoddword(dec.data[0]);
        }

        /* Even: Undefined for non-integers! */
        public static bool mpd_iseven(mpd_t dec)
        {
            return !mpd_isodd(dec);
        }

        /******************************************************************************/
        /*                      Getting and setting decimals                          */
        /******************************************************************************/

        /* Internal function: Set a static decimal from a triple, no error checking. */
        private static void _ssettriple(mpd_t result, byte sign, ulong a, long exp)
        {
            mpd_set_flags(result, sign);
            result.exp = exp;
            _mpd_div_word(out result.data[1], out result.data[0], a, MPD_RADIX);
            result.len = (result.data[1] == 0) ? 1 : 2;
            mpd_setdigits(result);
        }

        /* Internal function: Set a decimal from a triple, no error checking. */
        private static void _settriple(mpd_t result, byte sign, ulong a, long exp)
        {
            mpd_minalloc(result);
            mpd_set_flags(result, sign);
            result.exp = exp;
            _mpd_div_word(out result.data[1], out result.data[0], a, MPD_RADIX);
            result.len = (result.data[1] == 0) ? 1 : 2;
            mpd_setdigits(result);
        }

        /* Set a special number from a triple */
        public static void mpd_setspecial(mpd_t result, byte sign, byte type)
        {
            mpd_minalloc(result);
            result.flags &= unchecked((byte)~(MPD_NEG | MPD_SPECIAL));
            result.flags |= (byte)(sign | type);
            result.exp = result.digits = result.len = 0;
        }

        /* Set result of NaN with an error status */
        public static void mpd_seterror(mpd_t result, uint flags, ref uint status)
        {
            mpd_minalloc(result);
            mpd_set_qnan(result);
            mpd_set_positive(result);
            result.exp = result.digits = result.len = 0;
            status |= flags;
        }

        /* quietly set a static decimal from an mpd_ssize_t */
        public static void mpd_qsset_ssize(mpd_t result, long a, ref mpd_context_t ctx, ref uint status)
        {
            ulong u;
            byte sign = MPD_POS;

            if (a < 0)
            {
                if (a == MPD_SSIZE_MIN)
                {
                    u = (ulong)MPD_SSIZE_MAX +
                        unchecked((ulong)(-(MPD_SSIZE_MIN + MPD_SSIZE_MAX)));
                }
                else
                {
                    u = (ulong)(-a);
                }
                sign = MPD_NEG;
            }
            else
            {
                u = (ulong)a;
            }
            _ssettriple(result, sign, u, 0);
            mpd_qfinalize(result, ref ctx, ref status);
        }

        /* quietly set a static decimal from an mpd_uint_t */
        public static void mpd_qsset_uint(mpd_t result, ulong a, ref mpd_context_t ctx, ref uint status)
        {
            _ssettriple(result, MPD_POS, a, 0);
            mpd_qfinalize(result, ref ctx, ref status);
        }

        /* quietly set a static decimal from an int32_t */
        public static void mpd_qsset_i32(mpd_t result, int a, ref mpd_context_t ctx, ref uint status)
        {
            mpd_qsset_ssize(result, a, ref ctx, ref status);
        }

        /* quietly set a static decimal from a uint32_t */
        public static void mpd_qsset_u32(mpd_t result, uint a, ref mpd_context_t ctx, ref uint status)
        {
            mpd_qsset_uint(result, a, ref ctx, ref status);
        }

        /* quietly set a static decimal from an int64_t */
        public static void mpd_qsset_i64(mpd_t result, long a, ref mpd_context_t ctx, ref uint status)
        {
            mpd_qsset_ssize(result, a, ref ctx, ref status);
        }

        /* quietly set a static decimal from a uint64_t */
        public static void mpd_qsset_u64(mpd_t result, ulong a, ref mpd_context_t ctx, ref uint status)
        {
            mpd_qsset_uint(result, a, ref ctx, ref status);
        }

        /* quietly set a decimal from an mpd_ssize_t */
        public static void mpd_qset_ssize(mpd_t result, long a, ref mpd_context_t ctx, ref uint status)
        {
            mpd_minalloc(result);
            mpd_qsset_ssize(result, a, ref ctx, ref status);
        }

        /* quietly set a decimal from an mpd_uint_t */
        public static void mpd_qset_uint(mpd_t result, ulong a, ref mpd_context_t ctx, ref uint status)
        {
            _settriple(result, MPD_POS, a, 0);
            mpd_qfinalize(result, ref ctx, ref status);
        }

        /* quietly set a decimal from an int32_t */
        public static void mpd_qset_i32(mpd_t result, int a, ref mpd_context_t ctx, ref uint status)
        {
            mpd_qset_ssize(result, a, ref ctx, ref status);
        }

        /* quietly set a decimal from a uint32_t */
        public static void mpd_qset_u32(mpd_t result, uint a, ref mpd_context_t ctx, ref uint status)
        {
            mpd_qset_uint(result, a, ref ctx, ref status);
        }

        /* quietly set a decimal from an int64_t */
        public static void mpd_qset_i64(mpd_t result, long a, ref mpd_context_t ctx, ref uint status)
        {
            mpd_qset_ssize(result, a, ref ctx, ref status);
        }

        /* quietly set a decimal from an int64_t, use a maxcontext for conversion */
        public static void mpd_qset_i64_exact(mpd_t result, long a, ref uint status)
        {
            mpd_context_t maxcontext = default;

            mpd_maxcontext(ref maxcontext);
            mpd_qset_ssize(result, a, ref maxcontext, ref status);

            if ((status & (MPD_Inexact | MPD_Rounded | MPD_Clamped)) != 0)
            {
                /* we want exact results */
                mpd_seterror(result, MPD_Invalid_operation, ref status);
            }
            status &= MPD_Errors;
        }

        /* quietly set a decimal from a uint64_t */
        public static void mpd_qset_u64(mpd_t result, ulong a, ref mpd_context_t ctx, ref uint status)
        {
            mpd_qset_uint(result, a, ref ctx, ref status);
        }

        /* quietly set a decimal from a uint64_t, use a maxcontext for conversion */
        public static void mpd_qset_u64_exact(mpd_t result, ulong a, ref uint status)
        {
            mpd_context_t maxcontext = default;

            mpd_maxcontext(ref maxcontext);
            mpd_qset_uint(result, a, ref maxcontext, ref status);

            if ((status & (MPD_Inexact | MPD_Rounded | MPD_Clamped)) != 0)
            {
                /* we want exact results */
                mpd_seterror(result, MPD_Invalid_operation, ref status);
            }
            status &= MPD_Errors;
        }

        /*
         * Quietly get an mpd_uint_t from a decimal. Assumes
         * MPD_UINT_DIGITS == MPD_RDIGITS+1, which is true for
         * 32 and 64 bit machines.
         *
         * If the operation is impossible, MPD_Invalid_operation is set.
         */
        private static ulong _mpd_qget_uint(int use_sign, mpd_t a, ref uint status)
        {
            mpd_t tmp;
            ulong lo, hi;

            if (mpd_isspecial(a))
            {
                status |= MPD_Invalid_operation;
                return MPD_UINT_MAX;
            }
            if (mpd_iszero(a))
                return 0;
            if (use_sign != 0 && mpd_isnegative(a))
            {
                status |= MPD_Invalid_operation;
                return MPD_UINT_MAX;
            }

            if (a.digits + a.exp > MPD_RDIGITS + 1)
            {
                status |= MPD_Invalid_operation;
                return MPD_UINT_MAX;
            }

            if (a.exp < 0)
            {
                if (!_mpd_isint(a))
                {
                    status |= MPD_Invalid_operation;
                    return MPD_UINT_MAX;
                }
                /* At this point a->digits+a->exp <= MPD_RDIGITS+1,
                 * so the shift fits. */
                tmp = new mpd_t();
                tmp.data = new ulong[2];
                tmp.flags = MPD_STATIC | MPD_STATIC_DATA;
                tmp.alloc = 2;
                mpd_qsshiftr(tmp, a, -a.exp);
                tmp.exp = 0;
                a = tmp;
            }

            _mpd_get_msdigits(out hi, out lo, a, MPD_RDIGITS + 1);
            if (hi != 0)
            {
                status |= MPD_Invalid_operation;
                return MPD_UINT_MAX;
            }

            if (a.exp > 0)
            {
                _mpd_mul_words(out hi, out lo, lo, mpd_pow10[a.exp]);
                if (hi != 0)
                {
                    status |= MPD_Invalid_operation;
                    return MPD_UINT_MAX;
                }
            }

            return lo;
        }

        /*
         * Sets Invalid_operation for:
         *   - specials
         *   - negative numbers (except negative zero)
         *   - non-integers
         *   - overflow
         */
        public static ulong mpd_qget_uint(mpd_t a, ref uint status)
        {
            return _mpd_qget_uint(1, a, ref status);
        }

        /* Same as above, but gets the absolute value, i.e. the sign is ignored. */
        public static ulong mpd_qabs_uint(mpd_t a, ref uint status)
        {
            return _mpd_qget_uint(0, a, ref status);
        }

        /* quietly get an mpd_ssize_t from a decimal */
        public static long mpd_qget_ssize(mpd_t a, ref uint status)
        {
            uint workstatus = 0;
            ulong u;
            bool isneg;

            u = mpd_qabs_uint(a, ref workstatus);
            if ((workstatus & MPD_Invalid_operation) != 0)
            {
                status |= workstatus;
                return MPD_SSIZE_MAX;
            }

            isneg = mpd_isnegative(a);
            if (u <= MPD_SSIZE_MAX)
            {
                return isneg ? -((long)u) : (long)u;
            }
            else if (isneg && unchecked(u + (ulong)(MPD_SSIZE_MIN + MPD_SSIZE_MAX)) == MPD_SSIZE_MAX)
            {
                return MPD_SSIZE_MIN;
            }

            status |= MPD_Invalid_operation;
            return MPD_SSIZE_MAX;
        }

        /* quietly get a uint64_t from a decimal */
        public static ulong mpd_qget_u64(mpd_t a, ref uint status)
        {
            return mpd_qget_uint(a, ref status);
        }

        /* quietly get an int64_t from a decimal */
        public static long mpd_qget_i64(mpd_t a, ref uint status)
        {
            return mpd_qget_ssize(a, ref status);
        }

        /* quietly get a uint32_t from a decimal */
        public static uint mpd_qget_u32(mpd_t a, ref uint status)
        {
            uint workstatus = 0;
            ulong x = mpd_qget_uint(a, ref workstatus);

            if ((workstatus & MPD_Invalid_operation) != 0)
            {
                status |= workstatus;
                return uint.MaxValue;
            }
            if (x > uint.MaxValue)
            {
                status |= MPD_Invalid_operation;
                return uint.MaxValue;
            }

            return (uint)x;
        }

        /* quietly get an int32_t from a decimal */
        public static int mpd_qget_i32(mpd_t a, ref uint status)
        {
            uint workstatus = 0;
            long x = mpd_qget_ssize(a, ref workstatus);

            if ((workstatus & MPD_Invalid_operation) != 0)
            {
                status |= workstatus;
                return int.MaxValue;
            }
            if (x < int.MinValue || x > int.MaxValue)
            {
                status |= MPD_Invalid_operation;
                return int.MaxValue;
            }

            return (int)x;
        }

        /******************************************************************************/
        /*         Filtering input of functions, finalizing output of functions       */
        /******************************************************************************/

        /*
         * Check if the operand is NaN, copy to result and return 1 if this is
         * the case. Copying can fail since NaNs are allowed to have a payload that
         * does not fit in MPD_MINALLOC.
         */
        public static bool mpd_qcheck_nan(mpd_t result, mpd_t a, ref mpd_context_t ctx, ref uint status)
        {
            if (mpd_isnan(a))
            {
                status |= mpd_issnan(a) ? MPD_Invalid_operation : 0;
                mpd_qcopy(result, a, ref status);
                mpd_set_qnan(result);
                _mpd_fix_nan(result, ref ctx);
                return true;
            }
            return false;
        }

        /*
         * Check if either operand is NaN, copy to result and return 1 if this
         * is the case. Copying can fail since NaNs are allowed to have a payload
         * that does not fit in MPD_MINALLOC.
         */
        public static bool mpd_qcheck_nans(mpd_t result, mpd_t a, mpd_t b, ref mpd_context_t ctx, ref uint status)
        {
            if (((a.flags | b.flags) & (MPD_NAN | MPD_SNAN)) != 0)
            {
                mpd_t choice = b;
                if (mpd_issnan(a))
                {
                    choice = a;
                    status |= MPD_Invalid_operation;
                }
                else if (mpd_issnan(b))
                {
                    status |= MPD_Invalid_operation;
                }
                else if (mpd_isqnan(a))
                {
                    choice = a;
                }
                mpd_qcopy(result, choice, ref status);
                mpd_set_qnan(result);
                _mpd_fix_nan(result, ref ctx);
                return true;
            }
            return false;
        }

        /*
         * Check if one of the operands is NaN, copy to result and return 1 if this
         * is the case. Copying can fail since NaNs are allowed to have a payload
         * that does not fit in MPD_MINALLOC.
         */
        private static bool mpd_qcheck_3nans(mpd_t result, mpd_t a, mpd_t b, mpd_t c, ref mpd_context_t ctx, ref uint status)
        {
            if (((a.flags | b.flags | c.flags) & (MPD_NAN | MPD_SNAN)) != 0)
            {
                mpd_t choice = c;
                if (mpd_issnan(a))
                {
                    choice = a;
                    status |= MPD_Invalid_operation;
                }
                else if (mpd_issnan(b))
                {
                    choice = b;
                    status |= MPD_Invalid_operation;
                }
                else if (mpd_issnan(c))
                {
                    status |= MPD_Invalid_operation;
                }
                else if (mpd_isqnan(a))
                {
                    choice = a;
                }
                else if (mpd_isqnan(b))
                {
                    choice = b;
                }
                mpd_qcopy(result, choice, ref status);
                mpd_set_qnan(result);
                _mpd_fix_nan(result, ref ctx);
                return true;
            }
            return false;
        }

        /* Check if rounding digit 'rnd' leads to an increment. */
        private static bool _mpd_rnd_incr(mpd_t dec, ulong rnd, ref mpd_context_t ctx)
        {
            int ld;

            switch (ctx.round)
            {
                case MPD_ROUND_DOWN:
                case MPD_ROUND_TRUNC:
                    return false;
                case MPD_ROUND_HALF_UP:
                    return (rnd >= 5);
                case MPD_ROUND_HALF_EVEN:
                    return (rnd > 5) || ((rnd == 5) && mpd_isoddcoeff(dec));
                case MPD_ROUND_CEILING:
                    return !(rnd == 0 || mpd_isnegative(dec));
                case MPD_ROUND_FLOOR:
                    return !(rnd == 0 || mpd_ispositive(dec));
                case MPD_ROUND_HALF_DOWN:
                    return (rnd > 5);
                case MPD_ROUND_UP:
                    return !(rnd == 0);
                case MPD_ROUND_05UP:
                    ld = (int)mpd_lsd(dec.data[0]);
                    return (!(rnd == 0) && (ld == 0 || ld == 5));
                default:
                    /* Without a valid context, further results will be undefined. */
                    return false; /* GCOV_NOT_REACHED */
            }
        }

        /*
         * Apply rounding to a decimal that has been right-shifted into a full
         * precision decimal. If an increment leads to an overflow of the precision,
         * adjust the coefficient and the exponent and check the new exponent for
         * overflow.
         */
        private static void _mpd_apply_round(mpd_t dec, ulong rnd, ref mpd_context_t ctx, ref uint status)
        {
            if (_mpd_rnd_incr(dec, rnd, ref ctx))
            {
                /* We have a number with exactly ctx->prec digits. The increment
                 * can only lead to an overflow if the decimal is all nines. In
                 * that case, the result is a power of ten with prec+1 digits.
                 *
                 * If the precision is a multiple of MPD_RDIGITS, this situation is
                 * detected by _mpd_baseincr returning a carry.
                 * If the precision is not a multiple of MPD_RDIGITS, we have to
                 * check if the result has one digit too many.
                 */
                ulong carry = _mpd_baseincr(dec.data, (ulong)dec.len);
                if (carry != 0)
                {
                    dec.data[dec.len - 1] = mpd_pow10[MPD_RDIGITS - 1];
                    dec.exp += 1;
                    _mpd_check_exp(dec, ref ctx, ref status);
                    return;
                }
                mpd_setdigits(dec);
                if (dec.digits > ctx.prec)
                {
                    mpd_qshiftr_inplace(dec, 1);
                    dec.exp += 1;
                    dec.digits = ctx.prec;
                    _mpd_check_exp(dec, ref ctx, ref status);
                }
            }
        }

        /*
         * Apply rounding to a decimal. Allow overflow of the precision.
         */
        private static void _mpd_apply_round_excess(mpd_t dec, ulong rnd, ref mpd_context_t ctx, ref uint status)
        {
            if (_mpd_rnd_incr(dec, rnd, ref ctx))
            {
                ulong carry = _mpd_baseincr(dec.data, (ulong)dec.len);
                if (carry != 0)
                {
                    if (!mpd_qresize(dec, dec.len + 1, ref status))
                        return;
                    dec.data[dec.len] = 1;
                    dec.len += 1;
                }
                mpd_setdigits(dec);
            }
        }

        /*
         * Apply rounding to a decimal that has been right-shifted into a decimal
         * with full precision or less. Return failure if an increment would
         * overflow the precision.
         */
        private static bool _mpd_apply_round_fit(mpd_t dec, ulong rnd, ref mpd_context_t ctx, ref uint status)
        {
            if (_mpd_rnd_incr(dec, rnd, ref ctx))
            {
                ulong carry = _mpd_baseincr(dec.data, (ulong)dec.len);
                if (carry != 0)
                {
                    if (!mpd_qresize(dec, dec.len + 1, ref status))
                        return false;
                    dec.data[dec.len] = 1;
                    dec.len += 1;
                }
                mpd_setdigits(dec);
                if (dec.digits > ctx.prec)
                {
                    mpd_seterror(dec, MPD_Invalid_operation, ref status);
                    return false;
                }
            }
            return true;
        }

        /* Check a normal number for overflow, underflow, clamping. If the operand
           is modified, it will be zero, special or (sub)normal with a coefficient
           that fits into the current context precision. */
        private static void _mpd_check_exp(mpd_t dec, ref mpd_context_t ctx, ref uint status)
        {
            long adjexp, etiny, shift;
            int rnd;

            adjexp = mpd_adjexp(dec);
            if (adjexp > ctx.emax)
            {
                if (mpd_iszerocoeff(dec))
                {
                    dec.exp = ctx.emax;
                    if (ctx.clamp != 0)
                        dec.exp -= (ctx.prec - 1);
                    mpd_zerocoeff(dec);
                    status |= MPD_Clamped;
                    return;
                }

                switch (ctx.round)
                {
                    case MPD_ROUND_HALF_UP:
                    case MPD_ROUND_HALF_EVEN:
                    case MPD_ROUND_HALF_DOWN:
                    case MPD_ROUND_UP:
                    case MPD_ROUND_TRUNC:
                        mpd_setspecial(dec, mpd_sign(dec), MPD_INF);
                        break;
                    case MPD_ROUND_DOWN:
                    case MPD_ROUND_05UP:
                        mpd_qmaxcoeff(dec, ref ctx, ref status);
                        dec.exp = ctx.emax - ctx.prec + 1;
                        break;
                    case MPD_ROUND_CEILING:
                        if (mpd_isnegative(dec))
                        {
                            mpd_qmaxcoeff(dec, ref ctx, ref status);
                            dec.exp = ctx.emax - ctx.prec + 1;
                        }
                        else
                        {
                            mpd_setspecial(dec, MPD_POS, MPD_INF);
                        }
                        break;
                    case MPD_ROUND_FLOOR:
                        if (mpd_ispositive(dec))
                        {
                            mpd_qmaxcoeff(dec, ref ctx, ref status);
                            dec.exp = ctx.emax - ctx.prec + 1;
                        }
                        else
                        {
                            mpd_setspecial(dec, MPD_NEG, MPD_INF);
                        }
                        break;
                    default: /* debug */
                        throw mpd_err_fatal("_mpd_check_exp: invalid rounding mode"); /* GCOV_NOT_REACHED */
                }

                status |= MPD_Overflow | MPD_Inexact | MPD_Rounded;

            } /* fold down */
            else if (ctx.clamp != 0 && dec.exp > mpd_etop(ref ctx))
            {
                /* At this point adjexp=exp+digits-1 <= emax and exp > etop=emax-prec+1:
                 *   (1) shift = exp -emax+prec-1 > 0
                 *   (2) digits+shift = exp+digits-1 - emax + prec <= prec */
                shift = dec.exp - mpd_etop(ref ctx);
                if (!mpd_qshiftl(dec, dec, shift, ref status))
                    return;
                dec.exp -= shift;
                status |= MPD_Clamped;
                if (!mpd_iszerocoeff(dec) && adjexp < ctx.emin)
                {
                    /* Underflow is impossible, since exp < etiny=emin-prec+1
                     * and exp > etop=emax-prec+1 would imply emax < emin. */
                    status |= MPD_Subnormal;
                }
            }
            else if (adjexp < ctx.emin)
            {
                etiny = mpd_etiny(ref ctx);

                if (mpd_iszerocoeff(dec))
                {
                    if (dec.exp < etiny)
                    {
                        dec.exp = etiny;
                        mpd_zerocoeff(dec);
                        status |= MPD_Clamped;
                    }
                    return;
                }

                status |= MPD_Subnormal;
                if (dec.exp < etiny)
                {
                    /* At this point adjexp=exp+digits-1 < emin and exp < etiny=emin-prec+1:
                     *   (1) shift = emin-prec+1 - exp > 0
                     *   (2) digits-shift = exp+digits-1 - emin + prec < prec */
                    shift = etiny - dec.exp;
                    rnd = (int)mpd_qshiftr_inplace(dec, shift);
                    dec.exp = etiny;
                    /* We always have a spare digit in case of an increment. */
                    _mpd_apply_round_excess(dec, (ulong)rnd, ref ctx, ref status);
                    status |= MPD_Rounded;
                    if (rnd != 0)
                    {
                        status |= (MPD_Inexact | MPD_Underflow);
                        if (mpd_iszerocoeff(dec))
                        {
                            mpd_zerocoeff(dec);
                            status |= MPD_Clamped;
                        }
                    }
                }
                /* Case exp >= etiny=emin-prec+1:
                 *   (1) adjexp=exp+digits-1 < emin
                 *   (2) digits < emin-exp+1 <= prec */
            }
        }

        /* Transcendental functions do not always set Underflow reliably,
         * since they only use as much precision as is necessary for correct
         * rounding. If a result like 1.0000000000e-101 is finalized, there
         * is no rounding digit that would trigger Underflow. But we can
         * assume Inexact, so a short check suffices. */
        private static void mpd_check_underflow(mpd_t dec, ref mpd_context_t ctx, ref uint status)
        {
            if (mpd_adjexp(dec) < ctx.emin && !mpd_iszero(dec) &&
                dec.exp < mpd_etiny(ref ctx))
            {
                status |= MPD_Underflow;
            }
        }

        /* Check if a normal number must be rounded after the exponent has been checked. */
        private static void _mpd_check_round(mpd_t dec, ref mpd_context_t ctx, ref uint status)
        {
            ulong rnd;
            long shift;

            /* must handle specials: _mpd_check_exp() can produce infinities or NaNs */
            if (mpd_isspecial(dec))
                return;

            if (dec.digits > ctx.prec)
            {
                shift = dec.digits - ctx.prec;
                rnd = mpd_qshiftr_inplace(dec, shift);
                dec.exp += shift;
                _mpd_apply_round(dec, rnd, ref ctx, ref status);
                status |= MPD_Rounded;
                if (rnd != 0)
                    status |= MPD_Inexact;
            }
        }

        /* Finalize all operations. */
        public static void mpd_qfinalize(mpd_t result, ref mpd_context_t ctx, ref uint status)
        {
            if (mpd_isspecial(result))
            {
                if (mpd_isnan(result))
                    _mpd_fix_nan(result, ref ctx);
                return;
            }

            _mpd_check_exp(result, ref ctx, ref status);
            _mpd_check_round(result, ref ctx, ref status);
        }

        /******************************************************************************/
        /*                                 Copying                                    */
        /******************************************************************************/

        /* Internal function: Copy a decimal, share data with src: USE WITH CARE! */
        private static void _mpd_copy_shared(mpd_t dest, mpd_t src)
        {
            dest.flags = src.flags;
            dest.exp = src.exp;
            dest.digits = src.digits;
            dest.len = src.len;
            dest.alloc = src.alloc;
            dest.data = src.data;

            mpd_set_shared_data(dest);
        }

        /*
         * Copy a decimal. In case of an error, status is set to MPD_Malloc_error.
         */
        public static bool mpd_qcopy(mpd_t result, mpd_t a, ref uint status)
        {
            if (ReferenceEquals(result, a)) return true;

            if (!mpd_qresize(result, a.len, ref status))
                return false;

            mpd_copy_flags(result, a);
            result.exp = a.exp;
            result.digits = a.digits;
            result.len = a.len;
            Array.Copy(a.data, result.data, a.len);

            return true;
        }

        /* Same as mpd_qcopy, but do not set the result to NaN on failure. */
        public static bool mpd_qcopy_cxx(mpd_t result, mpd_t a)
        {
            if (ReferenceEquals(result, a)) return true;

            if (!mpd_qresize_cxx(result, a.len))
                return false;

            mpd_copy_flags(result, a);
            result.exp = a.exp;
            result.digits = a.digits;
            result.len = a.len;
            Array.Copy(a.data, result.data, a.len);

            return true;
        }

        /*
         * Copy to a decimal with a static buffer. The caller has to make sure that
         * the buffer is big enough. Cannot fail.
         */
        private static void mpd_qcopy_static(mpd_t result, mpd_t a)
        {
            if (ReferenceEquals(result, a)) return;

            Array.Copy(a.data, result.data, a.len);

            mpd_copy_flags(result, a);
            result.exp = a.exp;
            result.digits = a.digits;
            result.len = a.len;
        }

        /*
         * Return a newly allocated copy of the operand. In case of an error,
         * status is set to MPD_Malloc_error and the return value is NULL.
         */
        public static mpd_t mpd_qncopy(mpd_t a)
        {
            mpd_t result;

            if ((result = mpd_qnew_size(a.len)) == null)
                return null;
            Array.Copy(a.data, result.data, a.len);
            mpd_copy_flags(result, a);
            result.exp = a.exp;
            result.digits = a.digits;
            result.len = a.len;

            return result;
        }

        /*
         * Copy a decimal and set the sign to positive. In case of an error, the
         * status is set to MPD_Malloc_error.
         */
        public static bool mpd_qcopy_abs(mpd_t result, mpd_t a, ref uint status)
        {
            if (!mpd_qcopy(result, a, ref status))
                return false;
            mpd_set_positive(result);
            return true;
        }

        /*
         * Copy a decimal and negate the sign. In case of an error, the
         * status is set to MPD_Malloc_error.
         */
        public static bool mpd_qcopy_negate(mpd_t result, mpd_t a, ref uint status)
        {
            if (!mpd_qcopy(result, a, ref status))
                return false;
            _mpd_negate(result);
            return true;
        }

        /*
         * Copy a decimal, setting the sign of the first operand to the sign of the
         * second operand. In case of an error, the status is set to MPD_Malloc_error.
         */
        public static bool mpd_qcopy_sign(mpd_t result, mpd_t a, mpd_t b, ref uint status)
        {
            byte sign_b = mpd_sign(b); /* result may equal b! */

            if (!mpd_qcopy(result, a, ref status))
                return false;
            mpd_set_sign(result, sign_b);
            return true;
        }
    }
}
