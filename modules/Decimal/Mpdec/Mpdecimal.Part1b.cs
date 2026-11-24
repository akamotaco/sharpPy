// Modules/_decimal/libmpdec/mpdecimal.c (CPython 3.12.15, libmpdec 2.5.1), lines 2126-3286:
// comparisons, shifting the coefficient, miscellaneous operations. CONFIG_64.
using System;
using System.Diagnostics;

namespace SharpPy.Modules.Mpdecimal
{
    internal static partial class Mpd
    {
        /******************************************************************************/
        /*                                Comparisons                                 */
        /******************************************************************************/

        /*
         * For all functions that compare two operands and return an int the usual
         * convention applies to the return value:
         *
         * -1 if op1 < op2
         *  0 if op1 == op2
         *  1 if op1 > op2
         *
         *  INT_MAX for error
         */

        /* Convenience macro CMP_EQUAL_OR_RETURN(a, b): if a and b are not equal, return from the
         * calling function with the correct comparison value. Written out at each use. */

        /*
         * Compare the data of big and small. This function does the equivalent
         * of first shifting small to the left and then comparing the data of
         * big and small, except that no allocation for the left shift is needed.
         */
        private static int _mpd_basecmp(ReadOnlySpan<ulong> big, ReadOnlySpan<ulong> small, ulong n, ulong m,
                                        ulong shift)
        {
            ulong l, lprev, h;
            ulong q, r;
            ulong ph, x;

            Debug.Assert(m > 0 && n >= m && shift > 0);

            _mpd_div_word(out q, out r, (ulong)shift, MPD_RDIGITS);

            if (r != 0)
            {
                ph = mpd_pow10[r];

                --m; --n;
                _mpd_divmod_pow10(out h, out lprev, small[(int)m--], MPD_RDIGITS - r);
                if (h != 0)
                {
                    if (big[(int)n] != h) return big[(int)n] < h ? -1 : 1; /* CMP_EQUAL_OR_RETURN */
                    --n;
                }
                for (; m != MPD_SIZE_MAX; m--, n--)
                {
                    _mpd_divmod_pow10(out h, out l, small[(int)m], MPD_RDIGITS - r);
                    x = ph * lprev + h;
                    if (big[(int)n] != x) return big[(int)n] < x ? -1 : 1; /* CMP_EQUAL_OR_RETURN */
                    lprev = l;
                }
                x = ph * lprev;
                if (big[(int)q] != x) return big[(int)q] < x ? -1 : 1; /* CMP_EQUAL_OR_RETURN */
            }
            else
            {
                while (--m != MPD_SIZE_MAX)
                {
                    if (big[(int)(m + q)] != small[(int)m]) /* CMP_EQUAL_OR_RETURN */
                        return big[(int)(m + q)] < small[(int)m] ? -1 : 1;
                }
            }

            return _mpd_isallzero(big, (long)q) ? 0 : 1;
        }

        /* Compare two decimals with the same adjusted exponent. */
        private static int _mpd_cmp_same_adjexp(mpd_t a, mpd_t b)
        {
            long shift, i;

            if (a.exp != b.exp)
            {
                /* Cannot wrap: a->exp + a->digits = b->exp + b->digits, so
                 * a->exp - b->exp = b->digits - a->digits. */
                shift = a.exp - b.exp;
                if (shift > 0)
                    return -1 * _mpd_basecmp(b.data, a.data, (ulong)b.len, (ulong)a.len, (ulong)shift);
                else
                    return _mpd_basecmp(a.data, b.data, (ulong)a.len, (ulong)b.len, (ulong)(-shift));
            }

            /*
             * At this point adjexp(a) == adjexp(b) and a->exp == b->exp,
             * so a->digits == b->digits, therefore a->len == b->len.
             */
            for (i = a.len - 1; i >= 0; --i)
            {
                if (a.data[i] != b.data[i]) /* CMP_EQUAL_OR_RETURN */
                    return a.data[i] < b.data[i] ? -1 : 1;
            }

            return 0;
        }

        /* Compare two numerical values. */
        private static int _mpd_cmp(mpd_t a, mpd_t b)
        {
            long adjexp_a, adjexp_b;

            /* equal pointers */
            if (ReferenceEquals(a, b))
                return 0;

            /* infinities */
            if (mpd_isinfinite(a))
            {
                if (mpd_isinfinite(b))
                    return (mpd_isnegative(b) ? 1 : 0) - (mpd_isnegative(a) ? 1 : 0);
                return mpd_arith_sign(a);
            }
            if (mpd_isinfinite(b))
                return -mpd_arith_sign(b);

            /* zeros */
            if (mpd_iszerocoeff(a))
            {
                if (mpd_iszerocoeff(b))
                    return 0;
                return -mpd_arith_sign(b);
            }
            if (mpd_iszerocoeff(b))
                return mpd_arith_sign(a);

            /* different signs */
            if (mpd_sign(a) != mpd_sign(b))
                return mpd_sign(b) - mpd_sign(a);

            /* different adjusted exponents */
            adjexp_a = mpd_adjexp(a);
            adjexp_b = mpd_adjexp(b);
            if (adjexp_a != adjexp_b)
            {
                if (adjexp_a < adjexp_b)
                    return -1 * mpd_arith_sign(a);
                return mpd_arith_sign(a);
            }

            /* same adjusted exponents */
            return _mpd_cmp_same_adjexp(a, b) * mpd_arith_sign(a);
        }

        /* Compare the absolutes of two numerical values. */
        private static int _mpd_cmp_abs(mpd_t a, mpd_t b)
        {
            long adjexp_a, adjexp_b;

            /* equal pointers */
            if (ReferenceEquals(a, b))
                return 0;

            /* infinities */
            if (mpd_isinfinite(a))
            {
                if (mpd_isinfinite(b))
                    return 0;
                return 1;
            }
            if (mpd_isinfinite(b))
                return -1;

            /* zeros */
            if (mpd_iszerocoeff(a))
            {
                if (mpd_iszerocoeff(b))
                    return 0;
                return -1;
            }
            if (mpd_iszerocoeff(b))
                return 1;

            /* different adjusted exponents */
            adjexp_a = mpd_adjexp(a);
            adjexp_b = mpd_adjexp(b);
            if (adjexp_a != adjexp_b)
            {
                if (adjexp_a < adjexp_b)
                    return -1;
                return 1;
            }

            /* same adjusted exponents */
            return _mpd_cmp_same_adjexp(a, b);
        }

        /* Compare two values and return an integer result. */
        public static int mpd_qcmp(mpd_t a, mpd_t b, ref uint status)
        {
            if (mpd_isspecial(a) || mpd_isspecial(b))
            {
                if (mpd_isnan(a) || mpd_isnan(b))
                {
                    status |= MPD_Invalid_operation;
                    return int.MaxValue;
                }
            }

            return _mpd_cmp(a, b);
        }

        /*
         * Compare a and b, convert the usual integer result to a decimal and
         * store it in 'result'. For convenience, the integer result of the comparison
         * is returned. Comparisons involving NaNs return NaN/INT_MAX.
         */
        public static int mpd_qcompare(mpd_t result, mpd_t a, mpd_t b, ref mpd_context_t ctx, ref uint status)
        {
            int c;

            if (mpd_isspecial(a) || mpd_isspecial(b))
            {
                if (mpd_qcheck_nans(result, a, b, ref ctx, ref status))
                    return int.MaxValue;
            }

            c = _mpd_cmp(a, b);
            _settriple(result, (byte)(c < 0 ? 1 : 0), (ulong)(c != 0 ? 1 : 0), 0);
            return c;
        }

        /* Same as mpd_compare(), but signal for all NaNs, i.e. also for quiet NaNs. */
        public static int mpd_qcompare_signal(mpd_t result, mpd_t a, mpd_t b, ref mpd_context_t ctx, ref uint status)
        {
            int c;

            if (mpd_isspecial(a) || mpd_isspecial(b))
            {
                if (mpd_qcheck_nans(result, a, b, ref ctx, ref status))
                {
                    status |= MPD_Invalid_operation;
                    return int.MaxValue;
                }
            }

            c = _mpd_cmp(a, b);
            _settriple(result, (byte)(c < 0 ? 1 : 0), (ulong)(c != 0 ? 1 : 0), 0);
            return c;
        }

        /* Compare the operands using a total order. */
        public static int mpd_cmp_total(mpd_t a, mpd_t b)
        {
            mpd_t aa = new mpd_t(), bb = new mpd_t();
            int nan_a, nan_b;
            int c;

            if (mpd_sign(a) != mpd_sign(b))
                return mpd_sign(b) - mpd_sign(a);

            if (mpd_isnan(a))
            {
                c = 1;
                if (mpd_isnan(b))
                {
                    nan_a = (mpd_isqnan(a)) ? 1 : 0;
                    nan_b = (mpd_isqnan(b)) ? 1 : 0;
                    if (nan_b == nan_a)
                    {
                        if (a.len > 0 && b.len > 0)
                        {
                            _mpd_copy_shared(aa, a);
                            _mpd_copy_shared(bb, b);
                            aa.exp = bb.exp = 0;
                            /* compare payload */
                            c = _mpd_cmp_abs(aa, bb);
                        }
                        else
                        {
                            c = (a.len > 0 ? 1 : 0) - (b.len > 0 ? 1 : 0);
                        }
                    }
                    else
                    {
                        c = nan_a - nan_b;
                    }
                }
            }
            else if (mpd_isnan(b))
            {
                c = -1;
            }
            else
            {
                c = _mpd_cmp_abs(a, b);
                if (c == 0 && a.exp != b.exp)
                    c = (a.exp < b.exp) ? -1 : 1;
            }

            return c * mpd_arith_sign(a);
        }

        /*
         * Compare a and b according to a total order, convert the usual integer result
         * to a decimal and store it in 'result'. For convenience, the integer result
         * of the comparison is returned.
         */
        public static int mpd_compare_total(mpd_t result, mpd_t a, mpd_t b)
        {
            int c;

            c = mpd_cmp_total(a, b);
            _settriple(result, (byte)(c < 0 ? 1 : 0), (ulong)(c != 0 ? 1 : 0), 0);
            return c;
        }

        /* Compare the magnitude of the operands using a total order. */
        public static int mpd_cmp_total_mag(mpd_t a, mpd_t b)
        {
            mpd_t aa = new mpd_t(), bb = new mpd_t();

            _mpd_copy_shared(aa, a);
            _mpd_copy_shared(bb, b);

            mpd_set_positive(aa);
            mpd_set_positive(bb);

            return mpd_cmp_total(aa, bb);
        }

        /*
         * Compare the magnitude of a and b according to a total order, convert the
         * the usual integer result to a decimal and store it in 'result'.
         * For convenience, the integer result of the comparison is returned.
         */
        public static int mpd_compare_total_mag(mpd_t result, mpd_t a, mpd_t b)
        {
            int c;

            c = mpd_cmp_total_mag(a, b);
            _settriple(result, (byte)(c < 0 ? 1 : 0), (ulong)(c != 0 ? 1 : 0), 0);
            return c;
        }

        /* Determine an ordering for operands that are numerically equal. */
        private static int _mpd_cmp_numequal(mpd_t a, mpd_t b)
        {
            int sign_a, sign_b;
            int c;

            sign_a = mpd_sign(a);
            sign_b = mpd_sign(b);
            if (sign_a != sign_b)
            {
                c = sign_b - sign_a;
            }
            else
            {
                c = (a.exp < b.exp) ? -1 : 1;
                c *= mpd_arith_sign(a);
            }

            return c;
        }

        /******************************************************************************/
        /*                         Shifting the coefficient                           */
        /******************************************************************************/

        /*
         * Shift the coefficient of the operand to the left, no check for specials.
         * Both operands may be the same pointer. If the result length has to be
         * increased, mpd_qresize() might fail with MPD_Malloc_error.
         */
        public static bool mpd_qshiftl(mpd_t result, mpd_t a, long n, ref uint status)
        {
            long size;

            Debug.Assert(!mpd_isspecial(a));
            Debug.Assert(n >= 0);

            if (mpd_iszerocoeff(a) || n == 0)
                return mpd_qcopy(result, a, ref status);

            size = mpd_digits_to_size(a.digits + n);
            if (!mpd_qresize(result, size, ref status))
                return false; /* result is NaN */

            _mpd_baseshiftl(result.data, a.data, (ulong)size, (ulong)a.len, (ulong)n);

            mpd_copy_flags(result, a);
            result.exp = a.exp;
            result.digits = a.digits + n;
            result.len = size;

            return true;
        }

        /* Determine the rounding indicator if all digits of the coefficient are shifted
         * out of the picture. */
        private static ulong _mpd_get_rnd(ReadOnlySpan<ulong> data, long len, int use_msd)
        {
            ulong rnd = 0, rest = 0, word;

            word = data[(int)(len - 1)];
            /* special treatment for the most significant digit if shift == digits */
            if (use_msd != 0)
            {
                _mpd_divmod_pow10(out rnd, out rest, word, (ulong)(mpd_word_digits(word) - 1));
                if (len > 1 && rest == 0)
                    rest = _mpd_isallzero(data, len - 1) ? 0UL : 1UL;
            }
            else
            {
                rest = _mpd_isallzero(data, len) ? 0UL : 1UL;
            }

            return (rnd == 0 || rnd == 5) ? rnd + (rest != 0 ? 1UL : 0UL) : rnd;
        }

        /*
         * Same as mpd_qshiftr(), but 'result' is an mpd_t with a static coefficient.
         * It is the caller's responsibility to ensure that the coefficient is big
         * enough. The function cannot fail.
         */
        private static ulong mpd_qsshiftr(mpd_t result, mpd_t a, long n)
        {
            ulong rnd;
            long size;

            Debug.Assert(!mpd_isspecial(a));
            Debug.Assert(n >= 0);

            if (mpd_iszerocoeff(a) || n == 0)
            {
                mpd_qcopy_static(result, a);
                return 0;
            }

            if (n >= a.digits)
            {
                rnd = _mpd_get_rnd(a.data, a.len, (n == a.digits) ? 1 : 0);
                mpd_zerocoeff(result);
            }
            else
            {
                result.digits = a.digits - n;
                size = mpd_digits_to_size(result.digits);
                rnd = _mpd_baseshiftr(result.data, a.data, (ulong)a.len, (ulong)n);
                result.len = size;
            }

            mpd_copy_flags(result, a);
            result.exp = a.exp;

            return rnd;
        }

        /*
         * Inplace shift of the coefficient to the right, no check for specials.
         * Returns the rounding indicator for mpd_rnd_incr().
         * The function cannot fail.
         */
        public static ulong mpd_qshiftr_inplace(mpd_t result, long n)
        {
            uint dummy = 0;
            ulong rnd;
            long size;

            Debug.Assert(!mpd_isspecial(result));
            Debug.Assert(n >= 0);

            if (mpd_iszerocoeff(result) || n == 0)
                return 0;

            if (n >= result.digits)
            {
                rnd = _mpd_get_rnd(result.data, result.len, (n == result.digits) ? 1 : 0);
                mpd_zerocoeff(result);
            }
            else
            {
                rnd = _mpd_baseshiftr(result.data, result.data, (ulong)result.len, (ulong)n);
                result.digits -= n;
                size = mpd_digits_to_size(result.digits);
                /* reducing the size cannot fail */
                mpd_qresize(result, size, ref dummy);
                result.len = size;
            }

            return rnd;
        }

        /*
         * Shift the coefficient of the operand to the right, no check for specials.
         * Both operands may be the same pointer. Returns the rounding indicator to
         * be used by mpd_rnd_incr(). If the result length has to be increased,
         * mpd_qcopy() or mpd_qresize() might fail with MPD_Malloc_error. In those
         * cases, MPD_UINT_MAX is returned.
         */
        public static ulong mpd_qshiftr(mpd_t result, mpd_t a, long n, ref uint status)
        {
            ulong rnd;
            long size;

            Debug.Assert(!mpd_isspecial(a));
            Debug.Assert(n >= 0);

            if (mpd_iszerocoeff(a) || n == 0)
            {
                if (!mpd_qcopy(result, a, ref status))
                    return MPD_UINT_MAX;
                return 0;
            }

            if (n >= a.digits)
            {
                rnd = _mpd_get_rnd(a.data, a.len, (n == a.digits) ? 1 : 0);
                mpd_zerocoeff(result);
            }
            else
            {
                result.digits = a.digits - n;
                size = mpd_digits_to_size(result.digits);
                if (ReferenceEquals(result, a))
                {
                    rnd = _mpd_baseshiftr(result.data, a.data, (ulong)a.len, (ulong)n);
                    /* reducing the size cannot fail */
                    mpd_qresize(result, size, ref status);
                }
                else
                {
                    if (!mpd_qresize(result, size, ref status))
                        return MPD_UINT_MAX;
                    rnd = _mpd_baseshiftr(result.data, a.data, (ulong)a.len, (ulong)n);
                }
                result.len = size;
            }

            mpd_copy_flags(result, a);
            result.exp = a.exp;

            return rnd;
        }

        /******************************************************************************/
        /*                         Miscellaneous operations                           */
        /******************************************************************************/

        /* Logical And */
        public static void mpd_qand(mpd_t result, mpd_t a, mpd_t b, ref mpd_context_t ctx, ref uint status)
        {
            mpd_t big = a, small = b;
            ulong x, y, z, xbit, ybit;
            int k, mswdigits;
            long i;

            if (mpd_isspecial(a) || mpd_isspecial(b) ||
                mpd_isnegative(a) || mpd_isnegative(b) ||
                a.exp != 0 || b.exp != 0)
            {
                mpd_seterror(result, MPD_Invalid_operation, ref status);
                return;
            }
            if (b.digits > a.digits)
            {
                big = b;
                small = a;
            }
            if (!mpd_qresize(result, big.len, ref status))
                return;

            /* full words */
            for (i = 0; i < small.len - 1; i++)
            {
                x = small.data[i];
                y = big.data[i];
                z = 0;
                for (k = 0; k < MPD_RDIGITS; k++)
                {
                    xbit = x % 10;
                    x /= 10;
                    ybit = y % 10;
                    y /= 10;
                    if (xbit > 1 || ybit > 1)
                        goto invalid_operation;
                    z += (xbit & ybit) != 0 ? mpd_pow10[k] : 0;
                }
                result.data[i] = z;
            }
            /* most significant word of small */
            x = small.data[i];
            y = big.data[i];
            z = 0;
            mswdigits = mpd_word_digits(x);
            for (k = 0; k < mswdigits; k++)
            {
                xbit = x % 10;
                x /= 10;
                ybit = y % 10;
                y /= 10;
                if (xbit > 1 || ybit > 1)
                    goto invalid_operation;
                z += (xbit & ybit) != 0 ? mpd_pow10[k] : 0;
            }
            result.data[i++] = z;

            /* scan the rest of y for digits > 1 */
            for (; k < MPD_RDIGITS; k++)
            {
                ybit = y % 10;
                y /= 10;
                if (ybit > 1)
                    goto invalid_operation;
            }
            /* scan the rest of big for digits > 1 */
            for (; i < big.len; i++)
            {
                y = big.data[i];
                for (k = 0; k < MPD_RDIGITS; k++)
                {
                    ybit = y % 10;
                    y /= 10;
                    if (ybit > 1)
                        goto invalid_operation;
                }
            }

            mpd_clear_flags(result);
            result.exp = 0;
            result.len = _mpd_real_size(result.data, small.len);
            mpd_qresize(result, result.len, ref status);
            mpd_setdigits(result);
            _mpd_cap(result, ref ctx);
            return;

        invalid_operation:
            mpd_seterror(result, MPD_Invalid_operation, ref status);
        }

        /* Class of an operand. Returns a pointer to the constant name. */
        public static string mpd_class(mpd_t a, ref mpd_context_t ctx)
        {
            if (mpd_isnan(a))
            {
                if (mpd_isqnan(a))
                    return "NaN";
                else
                    return "sNaN";
            }
            else if (mpd_ispositive(a))
            {
                if (mpd_isinfinite(a))
                    return "+Infinity";
                else if (mpd_iszero(a))
                    return "+Zero";
                else if (mpd_isnormal(a, ref ctx))
                    return "+Normal";
                else
                    return "+Subnormal";
            }
            else
            {
                if (mpd_isinfinite(a))
                    return "-Infinity";
                else if (mpd_iszero(a))
                    return "-Zero";
                else if (mpd_isnormal(a, ref ctx))
                    return "-Normal";
                else
                    return "-Subnormal";
            }
        }

        /* Logical Xor */
        public static void mpd_qinvert(mpd_t result, mpd_t a, ref mpd_context_t ctx, ref uint status)
        {
            ulong x, z, xbit;
            long i, digits, len;
            long q, r;
            int k;

            if (mpd_isspecial(a) || mpd_isnegative(a) || a.exp != 0)
            {
                mpd_seterror(result, MPD_Invalid_operation, ref status);
                return;
            }

            digits = (a.digits < ctx.prec) ? ctx.prec : a.digits;
            _mpd_idiv_word(out q, out r, digits, MPD_RDIGITS);
            len = (r == 0) ? q : q + 1;
            if (!mpd_qresize(result, len, ref status))
                return;

            for (i = 0; i < len; i++)
            {
                x = (i < a.len) ? a.data[i] : 0;
                z = 0;
                for (k = 0; k < MPD_RDIGITS; k++)
                {
                    xbit = x % 10;
                    x /= 10;
                    if (xbit > 1)
                        goto invalid_operation;
                    z += xbit == 0 ? mpd_pow10[k] : 0;
                }
                result.data[i] = z;
            }

            mpd_clear_flags(result);
            result.exp = 0;
            result.len = _mpd_real_size(result.data, len);
            mpd_qresize(result, result.len, ref status);
            mpd_setdigits(result);
            _mpd_cap(result, ref ctx);
            return;

        invalid_operation:
            mpd_seterror(result, MPD_Invalid_operation, ref status);
        }

        /* Exponent of the magnitude of the most significant digit of the operand. */
        public static void mpd_qlogb(mpd_t result, mpd_t a, ref mpd_context_t ctx, ref uint status)
        {
            if (mpd_isspecial(a))
            {
                if (mpd_qcheck_nan(result, a, ref ctx, ref status))
                    return;
                mpd_setspecial(result, MPD_POS, MPD_INF);
            }
            else if (mpd_iszerocoeff(a))
            {
                mpd_setspecial(result, MPD_NEG, MPD_INF);
                status |= MPD_Division_by_zero;
            }
            else
            {
                mpd_qset_ssize(result, mpd_adjexp(a), ref ctx, ref status);
            }
        }

        /* Logical Or */
        public static void mpd_qor(mpd_t result, mpd_t a, mpd_t b, ref mpd_context_t ctx, ref uint status)
        {
            mpd_t big = a, small = b;
            ulong x, y, z, xbit, ybit;
            int k, mswdigits;
            long i;

            if (mpd_isspecial(a) || mpd_isspecial(b) ||
                mpd_isnegative(a) || mpd_isnegative(b) ||
                a.exp != 0 || b.exp != 0)
            {
                mpd_seterror(result, MPD_Invalid_operation, ref status);
                return;
            }
            if (b.digits > a.digits)
            {
                big = b;
                small = a;
            }
            if (!mpd_qresize(result, big.len, ref status))
                return;

            /* full words */
            for (i = 0; i < small.len - 1; i++)
            {
                x = small.data[i];
                y = big.data[i];
                z = 0;
                for (k = 0; k < MPD_RDIGITS; k++)
                {
                    xbit = x % 10;
                    x /= 10;
                    ybit = y % 10;
                    y /= 10;
                    if (xbit > 1 || ybit > 1)
                        goto invalid_operation;
                    z += (xbit | ybit) != 0 ? mpd_pow10[k] : 0;
                }
                result.data[i] = z;
            }
            /* most significant word of small */
            x = small.data[i];
            y = big.data[i];
            z = 0;
            mswdigits = mpd_word_digits(x);
            for (k = 0; k < mswdigits; k++)
            {
                xbit = x % 10;
                x /= 10;
                ybit = y % 10;
                y /= 10;
                if (xbit > 1 || ybit > 1)
                    goto invalid_operation;
                z += (xbit | ybit) != 0 ? mpd_pow10[k] : 0;
            }

            /* scan for digits > 1 and copy the rest of y */
            for (; k < MPD_RDIGITS; k++)
            {
                ybit = y % 10;
                y /= 10;
                if (ybit > 1)
                    goto invalid_operation;
                z += ybit * mpd_pow10[k];
            }
            result.data[i++] = z;
            /* scan for digits > 1 and copy the rest of big */
            for (; i < big.len; i++)
            {
                y = big.data[i];
                for (k = 0; k < MPD_RDIGITS; k++)
                {
                    ybit = y % 10;
                    y /= 10;
                    if (ybit > 1)
                        goto invalid_operation;
                }
                result.data[i] = big.data[i];
            }

            mpd_clear_flags(result);
            result.exp = 0;
            result.len = _mpd_real_size(result.data, big.len);
            mpd_qresize(result, result.len, ref status);
            mpd_setdigits(result);
            _mpd_cap(result, ref ctx);
            return;

        invalid_operation:
            mpd_seterror(result, MPD_Invalid_operation, ref status);
        }

        /*
         * Rotate the coefficient of 'a' by 'b' digits. 'b' must be an integer with
         * exponent 0.
         */
        public static void mpd_qrotate(mpd_t result, mpd_t a, mpd_t b, ref mpd_context_t ctx, ref uint status)
        {
            uint workstatus = 0;
            mpd_t tmp = MPD_NEW_STATIC(0, 0, 0, 0);
            mpd_t big = MPD_NEW_STATIC(0, 0, 0, 0);
            mpd_t small = MPD_NEW_STATIC(0, 0, 0, 0);
            long n, lshift, rshift;

            if (mpd_isspecial(a) || mpd_isspecial(b))
            {
                if (mpd_qcheck_nans(result, a, b, ref ctx, ref status))
                    return;
            }
            if (b.exp != 0 || mpd_isinfinite(b))
            {
                mpd_seterror(result, MPD_Invalid_operation, ref status);
                return;
            }

            n = mpd_qget_ssize(b, ref workstatus);
            if ((workstatus & MPD_Invalid_operation) != 0)
            {
                mpd_seterror(result, MPD_Invalid_operation, ref status);
                return;
            }
            if (n > ctx.prec || n < -ctx.prec)
            {
                mpd_seterror(result, MPD_Invalid_operation, ref status);
                return;
            }
            if (mpd_isinfinite(a))
            {
                mpd_qcopy(result, a, ref status);
                return;
            }

            if (n >= 0)
            {
                lshift = n;
                rshift = ctx.prec - n;
            }
            else
            {
                lshift = ctx.prec + n;
                rshift = -n;
            }

            if (a.digits > ctx.prec)
            {
                if (!mpd_qcopy(tmp, a, ref status))
                {
                    mpd_seterror(result, MPD_Malloc_error, ref status);
                    goto finish;
                }
                _mpd_cap(tmp, ref ctx);
                a = tmp;
            }

            if (!mpd_qshiftl(big, a, lshift, ref status))
            {
                mpd_seterror(result, MPD_Malloc_error, ref status);
                goto finish;
            }
            _mpd_cap(big, ref ctx);

            if (mpd_qshiftr(small, a, rshift, ref status) == MPD_UINT_MAX)
            {
                mpd_seterror(result, MPD_Malloc_error, ref status);
                goto finish;
            }
            _mpd_qadd(result, big, small, ref ctx, ref status);

        finish:
            mpd_del(tmp);
            mpd_del(big);
            mpd_del(small);
        }

        /*
         * b must be an integer with exponent 0 and in the range +-2*(emax + prec).
         * XXX: In my opinion +-(2*emax + prec) would be more sensible.
         * The result is a with the value of b added to its exponent.
         */
        public static void mpd_qscaleb(mpd_t result, mpd_t a, mpd_t b, ref mpd_context_t ctx, ref uint status)
        {
            uint workstatus = 0;
            ulong n, maxjump;
            long exp;

            if (mpd_isspecial(a) || mpd_isspecial(b))
            {
                if (mpd_qcheck_nans(result, a, b, ref ctx, ref status))
                    return;
            }
            if (b.exp != 0 || mpd_isinfinite(b))
            {
                mpd_seterror(result, MPD_Invalid_operation, ref status);
                return;
            }

            n = mpd_qabs_uint(b, ref workstatus);
            /* the spec demands this */
            maxjump = 2 * (ulong)(ctx.emax + ctx.prec);

            if (n > maxjump || (workstatus & MPD_Invalid_operation) != 0)
            {
                mpd_seterror(result, MPD_Invalid_operation, ref status);
                return;
            }
            if (mpd_isinfinite(a))
            {
                mpd_qcopy(result, a, ref status);
                return;
            }

            exp = a.exp + (long)n * mpd_arith_sign(b);
            exp = (exp > MPD_EXP_INF) ? MPD_EXP_INF : exp;
            exp = (exp < MPD_EXP_CLAMP) ? MPD_EXP_CLAMP : exp;

            mpd_qcopy(result, a, ref status);
            result.exp = (long)exp;

            mpd_qfinalize(result, ref ctx, ref status);
        }

        /*
         * Shift the coefficient by n digits, positive n is a left shift. In the case
         * of a left shift, the result is decapitated to fit the context precision. If
         * you don't want that, use mpd_shiftl().
         */
        public static void mpd_qshiftn(mpd_t result, mpd_t a, long n, ref mpd_context_t ctx, ref uint status)
        {
            if (mpd_isspecial(a))
            {
                if (mpd_qcheck_nan(result, a, ref ctx, ref status))
                    return;
                mpd_qcopy(result, a, ref status);
                return;
            }

            if (n >= 0 && n <= ctx.prec)
            {
                mpd_qshiftl(result, a, n, ref status);
                _mpd_cap(result, ref ctx);
            }
            else if (n < 0 && n >= -ctx.prec)
            {
                if (!mpd_qcopy(result, a, ref status))
                    return;
                _mpd_cap(result, ref ctx);
                mpd_qshiftr_inplace(result, -n);
            }
            else
            {
                mpd_seterror(result, MPD_Invalid_operation, ref status);
            }
        }

        /*
         * Same as mpd_shiftn(), but the shift is specified by the decimal b, which
         * must be an integer with a zero exponent. Infinities remain infinities.
         */
        public static void mpd_qshift(mpd_t result, mpd_t a, mpd_t b, ref mpd_context_t ctx, ref uint status)
        {
            uint workstatus = 0;
            long n;

            if (mpd_isspecial(a) || mpd_isspecial(b))
            {
                if (mpd_qcheck_nans(result, a, b, ref ctx, ref status))
                    return;
            }
            if (b.exp != 0 || mpd_isinfinite(b))
            {
                mpd_seterror(result, MPD_Invalid_operation, ref status);
                return;
            }

            n = mpd_qget_ssize(b, ref workstatus);
            if ((workstatus & MPD_Invalid_operation) != 0)
            {
                mpd_seterror(result, MPD_Invalid_operation, ref status);
                return;
            }
            if (n > ctx.prec || n < -ctx.prec)
            {
                mpd_seterror(result, MPD_Invalid_operation, ref status);
                return;
            }
            if (mpd_isinfinite(a))
            {
                mpd_qcopy(result, a, ref status);
                return;
            }

            if (n >= 0)
            {
                mpd_qshiftl(result, a, n, ref status);
                _mpd_cap(result, ref ctx);
            }
            else
            {
                if (!mpd_qcopy(result, a, ref status))
                    return;
                _mpd_cap(result, ref ctx);
                mpd_qshiftr_inplace(result, -n);
            }
        }

        /* Logical Xor */
        public static void mpd_qxor(mpd_t result, mpd_t a, mpd_t b, ref mpd_context_t ctx, ref uint status)
        {
            mpd_t big = a, small = b;
            ulong x, y, z, xbit, ybit;
            int k, mswdigits;
            long i;

            if (mpd_isspecial(a) || mpd_isspecial(b) ||
                mpd_isnegative(a) || mpd_isnegative(b) ||
                a.exp != 0 || b.exp != 0)
            {
                mpd_seterror(result, MPD_Invalid_operation, ref status);
                return;
            }
            if (b.digits > a.digits)
            {
                big = b;
                small = a;
            }
            if (!mpd_qresize(result, big.len, ref status))
                return;

            /* full words */
            for (i = 0; i < small.len - 1; i++)
            {
                x = small.data[i];
                y = big.data[i];
                z = 0;
                for (k = 0; k < MPD_RDIGITS; k++)
                {
                    xbit = x % 10;
                    x /= 10;
                    ybit = y % 10;
                    y /= 10;
                    if (xbit > 1 || ybit > 1)
                        goto invalid_operation;
                    z += (xbit ^ ybit) != 0 ? mpd_pow10[k] : 0;
                }
                result.data[i] = z;
            }
            /* most significant word of small */
            x = small.data[i];
            y = big.data[i];
            z = 0;
            mswdigits = mpd_word_digits(x);
            for (k = 0; k < mswdigits; k++)
            {
                xbit = x % 10;
                x /= 10;
                ybit = y % 10;
                y /= 10;
                if (xbit > 1 || ybit > 1)
                    goto invalid_operation;
                z += (xbit ^ ybit) != 0 ? mpd_pow10[k] : 0;
            }

            /* scan for digits > 1 and copy the rest of y */
            for (; k < MPD_RDIGITS; k++)
            {
                ybit = y % 10;
                y /= 10;
                if (ybit > 1)
                    goto invalid_operation;
                z += ybit * mpd_pow10[k];
            }
            result.data[i++] = z;
            /* scan for digits > 1 and copy the rest of big */
            for (; i < big.len; i++)
            {
                y = big.data[i];
                for (k = 0; k < MPD_RDIGITS; k++)
                {
                    ybit = y % 10;
                    y /= 10;
                    if (ybit > 1)
                        goto invalid_operation;
                }
                result.data[i] = big.data[i];
            }

            mpd_clear_flags(result);
            result.exp = 0;
            result.len = _mpd_real_size(result.data, big.len);
            mpd_qresize(result, result.len, ref status);
            mpd_setdigits(result);
            _mpd_cap(result, ref ctx);
            return;

        invalid_operation:
            mpd_seterror(result, MPD_Invalid_operation, ref status);
        }
    }
}
