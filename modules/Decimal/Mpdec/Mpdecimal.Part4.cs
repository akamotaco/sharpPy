// Modules/_decimal/libmpdec/mpdecimal.c (CPython 3.12.15, libmpdec 2.5.1), lines 7041-9015:
// reduce, rem, rem_near, rescale, round to integral, same_quantum, reciprocal, _mpd_base_ndivmod,
// invroot, sqrt, base conversions and the uint128 triple. CONFIG_64; where mpdecimal.c chooses
// by __SIZEOF_INT128__ the MSVC branch (no __int128) of the reference Windows build is ported.
using System;
using System.Diagnostics;

namespace SharpPy.Modules.Mpdecimal
{
    internal static partial class Mpd
    {
        // mpd_qreduce
        public static void mpd_qreduce(mpd_t result, mpd_t a, ref mpd_context_t ctx, ref uint status)
        {
            long shift, maxexp, maxshift;
            byte sign_a = mpd_sign(a);

            if (mpd_isspecial(a))
            {
                if (mpd_qcheck_nan(result, a, ref ctx, ref status))
                    return;
                mpd_qcopy(result, a, ref status);
                return;
            }

            if (!mpd_qcopy(result, a, ref status))
                return;
            mpd_qfinalize(result, ref ctx, ref status);
            if (mpd_isspecial(result))
                return;
            if (mpd_iszero(result))
            {
                _settriple(result, sign_a, 0, 0);
                return;
            }

            shift = mpd_trail_zeros(result);
            maxexp = (ctx.clamp != 0) ? mpd_etop(ref ctx) : ctx.emax;
            /* After the finalizing above result->exp <= maxexp. */
            maxshift = maxexp - result.exp;
            shift = (shift > maxshift) ? maxshift : shift;

            mpd_qshiftr_inplace(result, shift);
            result.exp += shift;
        }

        // mpd_qrem
        public static void mpd_qrem(mpd_t r, mpd_t a, mpd_t b, ref mpd_context_t ctx, ref uint status)
        {
            mpd_t q = MPD_NEW_STATIC(0, 0, 0, 0);

            if (mpd_isspecial(a) || mpd_isspecial(b))
            {
                if (mpd_qcheck_nans(r, a, b, ref ctx, ref status))
                    return;
                if (mpd_isinfinite(a))
                {
                    mpd_seterror(r, MPD_Invalid_operation, ref status);
                    return;
                }
                if (mpd_isinfinite(b))
                {
                    mpd_qcopy(r, a, ref status);
                    mpd_qfinalize(r, ref ctx, ref status);
                    return;
                }
                /* debug */
                throw mpd_err_fatal("mpd_qrem: unreachable"); /* GCOV_NOT_REACHED */
            }
            if (mpd_iszerocoeff(b))
            {
                if (mpd_iszerocoeff(a))
                    mpd_seterror(r, MPD_Division_undefined, ref status);
                else
                    mpd_seterror(r, MPD_Invalid_operation, ref status);
                return;
            }

            _mpd_qdivmod(q, r, a, b, ref ctx, ref status);
            mpd_del(q);
            mpd_qfinalize(r, ref ctx, ref status);
        }

        // mpd_qrem_near
        public static void mpd_qrem_near(mpd_t r, mpd_t a, mpd_t b, ref mpd_context_t ctx, ref uint status)
        {
            mpd_context_t workctx = default;
            mpd_t btmp = MPD_NEW_STATIC(0, 0, 0, 0);
            mpd_t q = MPD_NEW_STATIC(0, 0, 0, 0);
            long expdiff, qdigits;
            int cmp;
            bool isodd, allnine;

            Debug.Assert(r != null); /* annotation for scan-build */

            if (mpd_isspecial(a) || mpd_isspecial(b))
            {
                if (mpd_qcheck_nans(r, a, b, ref ctx, ref status))
                    return;
                if (mpd_isinfinite(a))
                {
                    mpd_seterror(r, MPD_Invalid_operation, ref status);
                    return;
                }
                if (mpd_isinfinite(b))
                {
                    mpd_qcopy(r, a, ref status);
                    mpd_qfinalize(r, ref ctx, ref status);
                    return;
                }
                /* debug */
                throw mpd_err_fatal("mpd_qrem_near: unreachable"); /* GCOV_NOT_REACHED */
            }
            if (mpd_iszerocoeff(b))
            {
                if (mpd_iszerocoeff(a))
                    mpd_seterror(r, MPD_Division_undefined, ref status);
                else
                    mpd_seterror(r, MPD_Invalid_operation, ref status);
                return;
            }

            if (r == b)
            {
                if (!mpd_qcopy(btmp, b, ref status))
                {
                    mpd_seterror(r, MPD_Malloc_error, ref status);
                    return;
                }
                b = btmp;
            }

            _mpd_qdivmod(q, r, a, b, ref ctx, ref status);
            if (mpd_isnan(q) || mpd_isnan(r))
                goto finish;
            if (mpd_iszerocoeff(r))
                goto finish;

            expdiff = mpd_adjexp(b) - mpd_adjexp(r);
            if (-1 <= expdiff && expdiff <= 1)
            {
                allnine = mpd_coeff_isallnine(q);
                qdigits = q.digits;
                isodd = mpd_isodd(q);

                mpd_maxcontext(ref workctx);
                if (mpd_sign(a) == mpd_sign(b))
                {
                    /* sign(r) == sign(b) */
                    _mpd_qsub(q, r, b, ref workctx, ref workctx.status);
                }
                else
                {
                    /* sign(r) != sign(b) */
                    _mpd_qadd(q, r, b, ref workctx, ref workctx.status);
                }

                if ((workctx.status & MPD_Errors) != 0)
                {
                    mpd_seterror(r, workctx.status & MPD_Errors, ref status);
                    goto finish;
                }

                cmp = _mpd_cmp_abs(q, r);
                if (cmp < 0 || (cmp == 0 && isodd))
                {
                    /* abs(r) > abs(b)/2 or abs(r) == abs(b)/2 and isodd(quotient) */
                    if (allnine && qdigits == ctx.prec)
                    {
                        /* abs(quotient) + 1 == 10**prec */
                        mpd_seterror(r, MPD_Division_impossible, ref status);
                        goto finish;
                    }
                    mpd_qcopy(r, q, ref status);
                }
            }

        finish:
            mpd_del(btmp);
            mpd_del(q);
            mpd_qfinalize(r, ref ctx, ref status);
        }

        // _mpd_qrescale
        private static void _mpd_qrescale(mpd_t result, mpd_t a, long exp, ref mpd_context_t ctx, ref uint status)
        {
            long expdiff, shift;
            ulong rnd;

            if (mpd_isspecial(a))
            {
                mpd_qcopy(result, a, ref status);
                return;
            }

            if (mpd_iszero(a))
            {
                _settriple(result, mpd_sign(a), 0, exp);
                return;
            }

            expdiff = a.exp - exp;
            if (expdiff >= 0)
            {
                shift = expdiff;
                if (a.digits + shift > MPD_MAX_PREC + 1)
                {
                    mpd_seterror(result, MPD_Invalid_operation, ref status);
                    return;
                }
                if (!mpd_qshiftl(result, a, shift, ref status))
                    return;
                result.exp = exp;
            }
            else
            {
                shift = -expdiff;
                rnd = mpd_qshiftr(result, a, shift, ref status);
                if (rnd == MPD_UINT_MAX)
                    return;
                result.exp = exp;
                _mpd_apply_round_excess(result, rnd, ref ctx, ref status);
                status |= MPD_Rounded;
                if (rnd != 0)
                    status |= MPD_Inexact;
            }

            if (mpd_issubnormal(result, ref ctx))
                status |= MPD_Subnormal;
        }

        /*
         * Rescale a number so that it has exponent 'exp'. Does not regard context
         * precision, emax, emin, but uses the rounding mode. Special numbers are
         * quietly copied. Restrictions:
         *
         *     MPD_MIN_ETINY <= exp <= MPD_MAX_EMAX+1
         *     result->digits <= MPD_MAX_PREC+1
         */
        public static void mpd_qrescale(mpd_t result, mpd_t a, long exp, ref mpd_context_t ctx, ref uint status)
        {
            if (exp > MPD_MAX_EMAX + 1 || exp < MPD_MIN_ETINY)
            {
                mpd_seterror(result, MPD_Invalid_operation, ref status);
                return;
            }

            _mpd_qrescale(result, a, exp, ref ctx, ref status);
        }

        /*
         * Same as mpd_qrescale, but with relaxed restrictions. The result of this
         * function should only be used for formatting a number and never as input
         * for other operations.
         *
         *     MPD_MIN_ETINY-MPD_MAX_PREC <= exp <= MPD_MAX_EMAX+1
         *     result->digits <= MPD_MAX_PREC+1
         */
        public static void mpd_qrescale_fmt(mpd_t result, mpd_t a, long exp, ref mpd_context_t ctx, ref uint status)
        {
            if (exp > MPD_MAX_EMAX + 1 || exp < MPD_MIN_ETINY - MPD_MAX_PREC)
            {
                mpd_seterror(result, MPD_Invalid_operation, ref status);
                return;
            }

            _mpd_qrescale(result, a, exp, ref ctx, ref status);
        }

        /* Round to an integer according to 'action' and ctx->round. */
        // enum {TO_INT_EXACT, TO_INT_SILENT, TO_INT_TRUNC};
        private const int TO_INT_EXACT = 0;
        private const int TO_INT_SILENT = 1;
        private const int TO_INT_TRUNC = 2;

        // _mpd_qround_to_integral
        private static void _mpd_qround_to_integral(int action, mpd_t result, mpd_t a, ref mpd_context_t ctx, ref uint status)
        {
            ulong rnd;

            if (mpd_isspecial(a))
            {
                if (mpd_qcheck_nan(result, a, ref ctx, ref status))
                    return;
                mpd_qcopy(result, a, ref status);
                return;
            }
            if (a.exp >= 0)
            {
                mpd_qcopy(result, a, ref status);
                return;
            }
            if (mpd_iszerocoeff(a))
            {
                _settriple(result, mpd_sign(a), 0, 0);
                return;
            }

            rnd = mpd_qshiftr(result, a, -a.exp, ref status);
            if (rnd == MPD_UINT_MAX)
                return;
            result.exp = 0;

            if (action == TO_INT_EXACT || action == TO_INT_SILENT)
            {
                _mpd_apply_round_excess(result, rnd, ref ctx, ref status);
                if (action == TO_INT_EXACT)
                {
                    status |= MPD_Rounded;
                    if (rnd != 0)
                        status |= MPD_Inexact;
                }
            }
        }

        // mpd_qround_to_intx
        public static void mpd_qround_to_intx(mpd_t result, mpd_t a, ref mpd_context_t ctx, ref uint status) =>
            _mpd_qround_to_integral(TO_INT_EXACT, result, a, ref ctx, ref status);

        // mpd_qround_to_int
        public static void mpd_qround_to_int(mpd_t result, mpd_t a, ref mpd_context_t ctx, ref uint status) =>
            _mpd_qround_to_integral(TO_INT_SILENT, result, a, ref ctx, ref status);

        // mpd_qtrunc
        public static void mpd_qtrunc(mpd_t result, mpd_t a, ref mpd_context_t ctx, ref uint status)
        {
            if (mpd_isspecial(a))
            {
                mpd_seterror(result, MPD_Invalid_operation, ref status);
                return;
            }

            _mpd_qround_to_integral(TO_INT_TRUNC, result, a, ref ctx, ref status);
        }

        // mpd_qfloor
        public static void mpd_qfloor(mpd_t result, mpd_t a, ref mpd_context_t ctx, ref uint status)
        {
            mpd_context_t workctx = ctx;

            if (mpd_isspecial(a))
            {
                mpd_seterror(result, MPD_Invalid_operation, ref status);
                return;
            }

            workctx.round = MPD_ROUND_FLOOR;
            _mpd_qround_to_integral(TO_INT_SILENT, result, a, ref workctx, ref status);
        }

        // mpd_qceil
        public static void mpd_qceil(mpd_t result, mpd_t a, ref mpd_context_t ctx, ref uint status)
        {
            mpd_context_t workctx = ctx;

            if (mpd_isspecial(a))
            {
                mpd_seterror(result, MPD_Invalid_operation, ref status);
                return;
            }

            workctx.round = MPD_ROUND_CEILING;
            _mpd_qround_to_integral(TO_INT_SILENT, result, a, ref workctx, ref status);
        }

        // mpd_same_quantum
        public static bool mpd_same_quantum(mpd_t a, mpd_t b)
        {
            if (mpd_isspecial(a) || mpd_isspecial(b))
            {
                return ((mpd_isnan(a) && mpd_isnan(b)) ||
                        (mpd_isinfinite(a) && mpd_isinfinite(b)));
            }

            return a.exp == b.exp;
        }

        /* Schedule the increase in precision for the Newton iteration. */
        private static int recpr_schedule_prec(long[] klist, long maxprec, long initprec)
        {
            long k;
            int i;

            Debug.Assert(maxprec > 0 && initprec > 0);
            if (maxprec <= initprec) return -1;

            i = 0; k = maxprec;
            do
            {
                k = (k + 1) / 2;
                klist[i++] = k;
            } while (k > initprec);

            return i - 1;
        }

        /*
         * Initial approximation for the reciprocal:
         *    k_0 := MPD_RDIGITS-2
         *    z_0 := 10**(-k_0) * floor(10**(2*k_0 + 2) / floor(v * 10**(k_0 + 2)))
         * Absolute error:
         *    |1/v - z_0| < 10**(-k_0)
         * ACL2 proof: maxerror-inverse-approx
         */
        private static void _mpd_qreciprocal_approx(mpd_t z, mpd_t v, ref uint status)
        {
            ulong[] p10data = { 0, mpd_pow10[MPD_RDIGITS - 2] };
            ulong dummy, word;
            int n;

            Debug.Assert(v.exp == -v.digits);

            _mpd_get_msdigits(out dummy, out word, v, MPD_RDIGITS);
            n = mpd_word_digits(word);
            word *= mpd_pow10[MPD_RDIGITS - n];

            mpd_qresize(z, 2, ref status);
            _mpd_shortdiv(z.data, p10data, 2, word);

            mpd_clear_flags(z);
            z.exp = -(MPD_RDIGITS - 2);
            z.len = (z.data[1] == 0) ? 1 : 2;
            mpd_setdigits(z);
        }

        /*
         * Reciprocal, calculated with Newton's Method. Assumption: result != a.
         * NOTE: The comments in the function show that certain operations are
         * exact. The proof for the maximum error is too long to fit in here.
         * ACL2 proof: maxerror-inverse-complete
         */
        private static void _mpd_qreciprocal(mpd_t result, mpd_t a, ref mpd_context_t ctx, ref uint status)
        {
            mpd_context_t varcontext = default, maxcontext = default;
            mpd_t z = result;         /* current approximation */
            mpd_t v;                  /* a, normalized to a number between 0.1 and 1 */
            mpd_t vtmp = MPD_NEW_SHARED(a);   /* v shares data with a */
            mpd_t s = MPD_NEW_STATIC(0, 0, 0, 0); /* temporary variable */
            mpd_t t = MPD_NEW_STATIC(0, 0, 0, 0); /* temporary variable */
            mpd_t two = MPD_NEW_CONST(0, 0, 1, 1, 1, 2); /* const 2 */
            long[] klist = new long[MPD_MAX_PREC_LOG2];
            long adj, maxprec, initprec;
            byte sign = mpd_sign(a);
            int i;

            Debug.Assert(result != a);

            v = vtmp;
            mpd_clear_flags(v);
            adj = v.digits + v.exp;
            v.exp = -v.digits;

            /* Initial approximation */
            _mpd_qreciprocal_approx(z, v, ref status);

            mpd_maxcontext(ref varcontext);
            mpd_maxcontext(ref maxcontext);
            varcontext.round = maxcontext.round = MPD_ROUND_TRUNC;
            varcontext.emax = maxcontext.emax = MPD_MAX_EMAX + 100;
            varcontext.emin = maxcontext.emin = MPD_MIN_EMIN - 100;
            maxcontext.prec = MPD_MAX_PREC + 100;

            maxprec = ctx.prec;
            maxprec += 2;
            initprec = MPD_RDIGITS - 3;

            i = recpr_schedule_prec(klist, maxprec, initprec);
            for (; i >= 0; i--)
            {
                /* Loop invariant: z->digits <= klist[i]+7 */
                /* Let s := z**2, exact result */
                _mpd_qmul_exact(s, z, z, ref maxcontext, ref status);
                varcontext.prec = 2 * klist[i] + 5;
                if (v.digits > varcontext.prec)
                {
                    /* Let t := v, truncated to n >= 2*k+5 fraction digits */
                    mpd_qshiftr(t, v, v.digits - varcontext.prec, ref status);
                    t.exp = -varcontext.prec;
                    /* Let t := trunc(v)*s, truncated to n >= 2*k+1 fraction digits */
                    mpd_qmul(t, t, s, ref varcontext, ref status);
                }
                else
                { /* v->digits <= 2*k+5 */
                    /* Let t := v*s, truncated to n >= 2*k+1 fraction digits */
                    mpd_qmul(t, v, s, ref varcontext, ref status);
                }
                /* Let s := 2*z, exact result */
                _mpd_qmul_exact(s, z, two, ref maxcontext, ref status);
                /* s.digits < t.digits <= 2*k+5, |adjexp(s)-adjexp(t)| <= 1,
                 * so the subtraction generates at most 2*k+6 <= klist[i+1]+7
                 * digits. The loop invariant is preserved. */
                _mpd_qsub_exact(z, s, t, ref maxcontext, ref status);
            }

            if (!mpd_isspecial(z))
            {
                z.exp -= adj;
                mpd_set_flags(z, sign);
            }

            mpd_del(s);
            mpd_del(t);
            mpd_qfinalize(z, ref ctx, ref status);
        }

        /*
         * Internal function for large numbers:
         *
         *     q, r = divmod(coeff(a), coeff(b))
         *
         * Strategy: Multiply the dividend by the reciprocal of the divisor. The
         * inexact result is fixed by a small loop, using at most one iteration.
         *
         * ACL2 proofs:
         * ------------
         *    1) q is a natural number.  (ndivmod-quotient-natp)
         *    2) r is a natural number.  (ndivmod-remainder-natp)
         *    3) a = q * b + r           (ndivmod-q*b+r==a)
         *    4) r < b                   (ndivmod-remainder-<-b)
         */
        private static void _mpd_base_ndivmod(mpd_t q, mpd_t r, mpd_t a, mpd_t b, ref uint status)
        {
            mpd_context_t workctx = default;
            mpd_t qq = q, rr = r;
            mpd_t aa = new mpd_t(), bb = new mpd_t();
            int k;

            _mpd_copy_shared(aa, a);
            _mpd_copy_shared(bb, b);

            mpd_set_positive(aa);
            mpd_set_positive(bb);
            aa.exp = 0;
            bb.exp = 0;

            if (q == a || q == b)
            {
                if ((qq = mpd_qnew()) == null)
                {
                    status |= MPD_Malloc_error;
                    goto nanresult;
                }
            }
            if (r == a || r == b)
            {
                if ((rr = mpd_qnew()) == null)
                {
                    status |= MPD_Malloc_error;
                    goto nanresult;
                }
            }

            mpd_maxcontext(ref workctx);

            /* Let prec := adigits - bdigits + 4 */
            workctx.prec = a.digits - b.digits + 1 + 3;
            if (a.digits > MPD_MAX_PREC || workctx.prec > MPD_MAX_PREC)
            {
                status |= MPD_Division_impossible;
                goto nanresult;
            }

            /* Let x := _mpd_qreciprocal(b, prec)
             * Then x is bounded by:
             *    1) 1/b - 10**(-prec - bdigits) < x < 1/b + 10**(-prec - bdigits)
             *    2) 1/b - 10**(-adigits - 4) < x < 1/b + 10**(-adigits - 4)
             */
            _mpd_qreciprocal(rr, bb, ref workctx, ref workctx.status);

            /* Get an estimate for the quotient. Let q := a * x
             * Then q is bounded by:
             *    3) a/b - 10**-4 < q < a/b + 10**-4
             */
            _mpd_qmul(qq, aa, rr, ref workctx, ref workctx.status);
            /* Truncate q to an integer:
             *    4) a/b - 2 < trunc(q) < a/b + 1
             */
            mpd_qtrunc(qq, qq, ref workctx, ref workctx.status);

            workctx.prec = aa.digits + 3;
            workctx.emax = MPD_MAX_EMAX + 3;
            workctx.emin = MPD_MIN_EMIN - 3;
            /* Multiply the estimate for q by b:
             *    5) a - 2 * b < trunc(q) * b < a + b
             */
            _mpd_qmul(rr, bb, qq, ref workctx, ref workctx.status);
            /* Get the estimate for r such that a = q * b + r. */
            _mpd_qsub_exact(rr, aa, rr, ref workctx, ref workctx.status);

            /* Fix the result. At this point -b < r < 2*b, so the correction loop
               takes at most one iteration. */
            for (k = 0; ; k++)
            {
                if (mpd_isspecial(qq) || mpd_isspecial(rr))
                {
                    status |= (workctx.status & MPD_Errors);
                    goto nanresult;
                }
                if (k > 2)
                { /* Allow two iterations despite the proof. */
                    mpd_err_warn("libmpdec: internal error in " +       /* GCOV_NOT_REACHED */
                                 "_mpd_base_ndivmod: please report"); /* GCOV_NOT_REACHED */
                    status |= MPD_Invalid_operation;                 /* GCOV_NOT_REACHED */
                    goto nanresult;                                   /* GCOV_NOT_REACHED */
                }
                /* r < 0 */
                else if (_mpd_cmp(zero, rr) == 1)
                {
                    _mpd_qadd_exact(rr, rr, bb, ref workctx, ref workctx.status);
                    _mpd_qadd_exact(qq, qq, minus_one, ref workctx, ref workctx.status);
                }
                /* 0 <= r < b */
                else if (_mpd_cmp(rr, bb) == -1)
                {
                    break;
                }
                /* r >= b */
                else
                {
                    _mpd_qsub_exact(rr, rr, bb, ref workctx, ref workctx.status);
                    _mpd_qadd_exact(qq, qq, one, ref workctx, ref workctx.status);
                }
            }

            if (qq != q)
            {
                if (!mpd_qcopy(q, qq, ref status))
                    goto nanresult; /* GCOV_UNLIKELY */
                mpd_del(qq);
            }
            if (rr != r)
            {
                if (!mpd_qcopy(r, rr, ref status))
                    goto nanresult; /* GCOV_UNLIKELY */
                mpd_del(rr);
            }

            status |= (workctx.status & MPD_Errors);
            return;


        nanresult:
            if (qq != null && qq != q) mpd_del(qq);
            if (rr != null && rr != r) mpd_del(rr);
            mpd_setspecial(q, MPD_POS, MPD_NAN);
            mpd_setspecial(r, MPD_POS, MPD_NAN);
        }

        /* LIBMPDEC_ONLY */
        /*
         * Schedule the optimal precision increase for the Newton iteration.
         *   v := input operand
         *   z_0 := initial approximation
         *   initprec := natural number such that abs(sqrt(v) - z_0) < 10**-initprec
         *   maxprec := target precision
         *
         * For convenience the output klist contains the elements in reverse order:
         *   klist := [k_n-1, ..., k_0], where
         *     1) k_0 <= initprec and
         *     2) abs(sqrt(v) - result) < 10**(-2*k_n-1 + 2) <= 10**-maxprec.
         */
        private static int invroot_schedule_prec(long[] klist, long maxprec, long initprec)
        {
            long k;
            int i;

            Debug.Assert(maxprec >= 3 && initprec >= 3);
            if (maxprec <= initprec) return -1;

            i = 0; k = maxprec;
            do
            {
                k = (k + 3) / 2;
                klist[i++] = k;
            } while (k > initprec);

            return i - 1;
        }

        /*
         * Initial approximation for the inverse square root function.
         *   Input:
         *     v := rational number, with 1 <= v < 100
         *     vhat := floor(v * 10**6)
         *   Output:
         *     z := approximation to 1/sqrt(v), such that abs(z - 1/sqrt(v)) < 10**-3.
         */
        private static void _invroot_init_approx(mpd_t z, ulong vhat)
        {
            ulong lo = 1000;
            ulong hi = 10000;
            ulong a, sq;

            Debug.Assert(lo * lo <= vhat && vhat < (hi + 1) * (hi + 1));

            for (; ; )
            {
                a = (lo + hi) / 2;
                sq = a * a;
                if (vhat >= sq)
                {
                    if (vhat < sq + 2 * a + 1)
                        break;
                    lo = a + 1;
                }
                else
                {
                    hi = a - 1;
                }
            }

            /*
             * After the binary search we have:
             *  1) a**2 <= floor(v * 10**6) < (a + 1)**2
             * This implies:
             *  2) a**2 <= v * 10**6 < (a + 1)**2
             *  3) a <= sqrt(v) * 10**3 < a + 1
             * Since 10**3 <= a:
             *  4) 0 <= 10**prec/a - 1/sqrt(v) < 10**-prec
             * We have:
             *  5) 10**3/a - 10**-3 < floor(10**9/a) * 10**-6 <= 10**3/a
             * Merging 4) and 5):
             *  6) abs(floor(10**9/a) * 10**-6 - 1/sqrt(v)) < 10**-3
             */
            mpd_minalloc(z);
            mpd_clear_flags(z);
            z.data[0] = 1000000000UL / a;
            z.len = 1;
            z.exp = -6;
            mpd_setdigits(z);
        }

        /*
         * Set 'result' to 1/sqrt(a).
         *   Relative error: abs(result - 1/sqrt(a)) < 10**-prec * 1/sqrt(a)
         */
        private static void _mpd_qinvroot(mpd_t result, mpd_t a, ref mpd_context_t ctx, ref uint status)
        {
            uint workstatus = 0;
            mpd_context_t varcontext = default, maxcontext = default;
            mpd_t z = result;         /* current approximation */
            mpd_t v;                  /* a, normalized to a number between 1 and 100 */
            mpd_t vtmp = MPD_NEW_SHARED(a);   /* by default v will share data with a */
            mpd_t s = MPD_NEW_STATIC(0, 0, 0, 0); /* temporary variable */
            mpd_t t = MPD_NEW_STATIC(0, 0, 0, 0); /* temporary variable */
            mpd_t one_half = MPD_NEW_CONST(0, -1, 1, 1, 1, 5);
            mpd_t three = MPD_NEW_CONST(0, 0, 1, 1, 1, 3);
            long[] klist = new long[MPD_MAX_PREC_LOG2];
            long ideal_exp, shift;
            long adj, tz;
            long maxprec, fracdigits;
            ulong vhat, dummy;
            int i, n;


            ideal_exp = -(a.exp - (a.exp & 1)) / 2;

            v = vtmp;
            if (result == a)
            {
                if ((v = mpd_qncopy(a)) == null)
                {
                    mpd_seterror(result, MPD_Malloc_error, ref status);
                    return;
                }
            }

            /* normalize a to 1 <= v < 100 */
            if (((v.digits + v.exp) & 1) != 0)
            {
                fracdigits = v.digits - 1;
                v.exp = -fracdigits;
                n = (v.digits > 7) ? 7 : (int)v.digits;
                /* Let vhat := floor(v * 10**(2*initprec)) */
                _mpd_get_msdigits(out dummy, out vhat, v, (uint)n);
                if (n < 7)
                    vhat *= mpd_pow10[7 - n];
            }
            else
            {
                fracdigits = v.digits - 2;
                v.exp = -fracdigits;
                n = (v.digits > 8) ? 8 : (int)v.digits;
                /* Let vhat := floor(v * 10**(2*initprec)) */
                _mpd_get_msdigits(out dummy, out vhat, v, (uint)n);
                if (n < 8)
                    vhat *= mpd_pow10[8 - n];
            }
            adj = (a.exp - v.exp) / 2;

            /* initial approximation */
            _invroot_init_approx(z, vhat);

            mpd_maxcontext(ref maxcontext);
            mpd_maxcontext(ref varcontext);
            varcontext.round = MPD_ROUND_TRUNC;
            maxprec = ctx.prec + 1;

            /* initprec == 3 */
            i = invroot_schedule_prec(klist, maxprec, 3);
            for (; i >= 0; i--)
            {
                varcontext.prec = 2 * klist[i] + 2;
                mpd_qmul(s, z, z, ref maxcontext, ref workstatus);
                if (v.digits > varcontext.prec)
                {
                    shift = v.digits - varcontext.prec;
                    mpd_qshiftr(t, v, shift, ref workstatus);
                    t.exp += shift;
                    mpd_qmul(t, t, s, ref varcontext, ref workstatus);
                }
                else
                {
                    mpd_qmul(t, v, s, ref varcontext, ref workstatus);
                }
                mpd_qsub(t, three, t, ref maxcontext, ref workstatus);
                mpd_qmul(z, z, t, ref varcontext, ref workstatus);
                mpd_qmul(z, z, one_half, ref maxcontext, ref workstatus);
            }

            z.exp -= adj;

            tz = mpd_trail_zeros(result);
            shift = ideal_exp - result.exp;
            shift = (tz > shift) ? shift : tz;
            if (shift > 0)
            {
                mpd_qshiftr_inplace(result, shift);
                result.exp += shift;
            }


            mpd_del(s);
            mpd_del(t);
            if (v != vtmp) mpd_del(v);
            status |= (workstatus & MPD_Errors);
            status |= (MPD_Rounded | MPD_Inexact);
        }

        // mpd_qinvroot
        public static void mpd_qinvroot(mpd_t result, mpd_t a, ref mpd_context_t ctx, ref uint status)
        {
            mpd_context_t workctx;

            if (mpd_isspecial(a))
            {
                if (mpd_qcheck_nan(result, a, ref ctx, ref status))
                    return;
                if (mpd_isnegative(a))
                {
                    mpd_seterror(result, MPD_Invalid_operation, ref status);
                    return;
                }
                /* positive infinity */
                _settriple(result, MPD_POS, 0, mpd_etiny(ref ctx));
                status |= MPD_Clamped;
                return;
            }
            if (mpd_iszero(a))
            {
                mpd_setspecial(result, mpd_sign(a), MPD_INF);
                status |= MPD_Division_by_zero;
                return;
            }
            if (mpd_isnegative(a))
            {
                mpd_seterror(result, MPD_Invalid_operation, ref status);
                return;
            }

            workctx = ctx;
            workctx.prec += 2;
            workctx.round = MPD_ROUND_HALF_EVEN;
            _mpd_qinvroot(result, a, ref workctx, ref status);
            mpd_qfinalize(result, ref ctx, ref status);
        }
        /* END LIBMPDEC_ONLY */

        /* Algorithm from decimal.py */
        private static void _mpd_qsqrt(mpd_t result, mpd_t a, ref mpd_context_t ctx, ref uint status)
        {
            mpd_context_t maxcontext = default;
            mpd_t c = MPD_NEW_STATIC(0, 0, 0, 0);
            mpd_t q = MPD_NEW_STATIC(0, 0, 0, 0);
            mpd_t r = MPD_NEW_STATIC(0, 0, 0, 0);
            mpd_t two = MPD_NEW_CONST(0, 0, 1, 1, 1, 2);
            long prec, ideal_exp;
            long l, shift;
            bool exact = false;


            ideal_exp = (a.exp - (a.exp & 1)) / 2;

            if (mpd_isspecial(a))
            {
                if (mpd_qcheck_nan(result, a, ref ctx, ref status))
                    return;
                if (mpd_isnegative(a))
                {
                    mpd_seterror(result, MPD_Invalid_operation, ref status);
                    return;
                }
                mpd_setspecial(result, MPD_POS, MPD_INF);
                return;
            }
            if (mpd_iszero(a))
            {
                _settriple(result, mpd_sign(a), 0, ideal_exp);
                mpd_qfinalize(result, ref ctx, ref status);
                return;
            }
            if (mpd_isnegative(a))
            {
                mpd_seterror(result, MPD_Invalid_operation, ref status);
                return;
            }

            mpd_maxcontext(ref maxcontext);
            prec = ctx.prec + 1;

            if (!mpd_qcopy(c, a, ref status))
                goto malloc_error;
            c.exp = 0;

            if ((a.exp & 1) != 0)
            {
                if (!mpd_qshiftl(c, c, 1, ref status))
                    goto malloc_error;
                l = (a.digits >> 1) + 1;
            }
            else
            {
                l = (a.digits + 1) >> 1;
            }

            shift = prec - l;
            if (shift >= 0)
            {
                if (!mpd_qshiftl(c, c, 2 * shift, ref status))
                    goto malloc_error;
                exact = true;
            }
            else
            {
                exact = mpd_qshiftr_inplace(c, -2 * shift) == 0;
            }

            ideal_exp -= shift;

            /* find result = floor(sqrt(c)) using Newton's method */
            if (!mpd_qshiftl(result, one, prec, ref status))
                goto malloc_error;

            while (true)
            {
                _mpd_qdivmod(q, r, c, result, ref maxcontext, ref maxcontext.status);
                if (mpd_isspecial(result) || mpd_isspecial(q))
                {
                    mpd_seterror(result, maxcontext.status & MPD_Errors, ref status);
                    goto out_;
                }
                if (_mpd_cmp(result, q) <= 0)
                    break;
                _mpd_qadd_exact(result, result, q, ref maxcontext, ref maxcontext.status);
                if (mpd_isspecial(result))
                {
                    mpd_seterror(result, maxcontext.status & MPD_Errors, ref status);
                    goto out_;
                }
                _mpd_qdivmod(result, r, result, two, ref maxcontext, ref maxcontext.status);
            }

            if (exact)
            {
                _mpd_qmul_exact(r, result, result, ref maxcontext, ref maxcontext.status);
                if (mpd_isspecial(r))
                {
                    mpd_seterror(result, maxcontext.status & MPD_Errors, ref status);
                    goto out_;
                }
                exact = (_mpd_cmp(r, c) == 0);
            }

            if (exact)
            {
                if (shift >= 0)
                {
                    mpd_qshiftr_inplace(result, shift);
                }
                else
                {
                    if (!mpd_qshiftl(result, result, -shift, ref status))
                        goto malloc_error;
                }
                ideal_exp += shift;
            }
            else
            {
                int lsd = (int)mpd_lsd(result.data[0]);
                if (lsd == 0 || lsd == 5)
                    result.data[0] += 1;
            }

            result.exp = ideal_exp;


        out_:
            mpd_del(c);
            mpd_del(q);
            mpd_del(r);
            maxcontext = ctx;
            maxcontext.round = MPD_ROUND_HALF_EVEN;
            mpd_qfinalize(result, ref maxcontext, ref status);
            return;

        malloc_error:
            mpd_seterror(result, MPD_Malloc_error, ref status);
            goto out_;
        }

        // mpd_qsqrt
        public static void mpd_qsqrt(mpd_t result, mpd_t a, ref mpd_context_t ctx, ref uint status)
        {
            mpd_t aa = MPD_NEW_STATIC(0, 0, 0, 0);
            uint xstatus = 0;

            if (result == a)
            {
                if (!mpd_qcopy(aa, a, ref status))
                {
                    mpd_seterror(result, MPD_Malloc_error, ref status);
                    goto out_;
                }
                a = aa;
            }

            _mpd_qsqrt(result, a, ref ctx, ref xstatus);

            if ((xstatus & (MPD_Malloc_error | MPD_Division_impossible)) != 0)
            {
                /* The above conditions can occur at very high context precisions
                 * if intermediate values get too large. Retry the operation with
                 * a lower context precision in case the result is exact.
                 *
                 * If the result is exact, an upper bound for the number of digits
                 * is the number of digits in the input.
                 *
                 * NOTE: sqrt(40e9) = 2.0e+5 /\ digits(40e9) = digits(2.0e+5) = 2
                 */
                uint ystatus = 0;
                mpd_context_t workctx = ctx;

                workctx.prec = a.digits;
                if (workctx.prec >= ctx.prec)
                {
                    status |= (xstatus | MPD_Errors);
                    goto out_; /* No point in repeating this, keep the original error. */
                }

                _mpd_qsqrt(result, a, ref workctx, ref ystatus);
                if (ystatus != 0)
                {
                    ystatus = status | ((xstatus | ystatus) & MPD_Errors);
                    mpd_seterror(result, ystatus, ref status);
                }
            }
            else
            {
                status |= xstatus;
            }

        out_:
            mpd_del(aa);
        }


        /******************************************************************************/
        /*                              Base conversions                              */
        /******************************************************************************/

        /* Space needed to represent an integer mpd_t in base 'base'. */
        public static ulong mpd_sizeinbase(mpd_t a, uint @base)
        {
            double x;
            ulong digits;
            double upper_bound;

            Debug.Assert(mpd_isinteger(a));
            Debug.Assert(@base >= 2);

            if (mpd_iszero(a))
                return 1;

            digits = (ulong)(a.digits + a.exp);

            /* CONFIG_64: ceil(2711437152599294 / log10(2)) + 4 == 2**53 */
            if (digits > 2711437152599294UL)
                return MPD_SIZE_MAX;

            upper_bound = (double)((1UL << 53) - 1);

            x = (double)digits / Math.Log10(@base);
            return (x > upper_bound) ? MPD_SIZE_MAX : (ulong)x + 1;
        }

        /* Space needed to import a base 'base' integer of length 'srclen'. */
        private static long _mpd_importsize(ulong srclen, uint @base)
        {
            double x;
            double upper_bound;

            Debug.Assert(srclen > 0);
            Debug.Assert(@base >= 2);

            /* SIZE_MAX == UINT64_MAX */
            if (srclen > (1UL << 53))
                return MPD_SSIZE_MAX;

            Debug.Assert((1L << 53) <= MPD_MAXIMPORT);
            upper_bound = (double)((1UL << 53) - 1);

            x = (double)srclen * (Math.Log10(@base) / MPD_RDIGITS);
            return (x > upper_bound) ? MPD_SSIZE_MAX : (long)x + 1;
        }

        // mpd_resize_u16
        private static byte mpd_resize_u16(ref ushort[] w, ulong nmemb)
        {
            byte err = 0;
            w = mpd_realloc(w, nmemb, ref err);
            return (byte)(err == 0 ? 1 : 0);
        }

        // mpd_resize_u32
        private static byte mpd_resize_u32(ref uint[] w, ulong nmemb)
        {
            byte err = 0;
            w = mpd_realloc(w, nmemb, ref err);
            return (byte)(err == 0 ? 1 : 0);
        }

        // _baseconv_to_u16
        private static ulong _baseconv_to_u16(ref ushort[] w, ulong wlen, ulong wbase, ulong[] u, long ulen)
        {
            ulong n = 0;

            Debug.Assert(wlen > 0 && ulen > 0);
            Debug.Assert(wbase <= (1U << 16));

            do
            {
                if (n >= wlen)
                {
                    if (mpd_resize_u16(ref w, n + 1) == 0)
                        return MPD_SIZE_MAX;
                    wlen = n + 1;
                }
                w[n++] = (ushort)_mpd_shortdiv(u, u, (ulong)ulen, wbase);
                /* ulen is at least 1. u[ulen-1] can only be zero if ulen == 1. */
                ulen = _mpd_real_size(u, ulen);

            } while (u[ulen - 1] != 0);

            return n;
        }

        // _coeff_from_u16
        private static ulong _coeff_from_u16(mpd_t w, long wlen, ulong[] u, ulong ulen, uint ubase, ref uint status)
        {
            long n = 0;
            ulong carry;

            Debug.Assert(wlen > 0 && ulen > 0);
            Debug.Assert(ubase <= (1U << 16));

            w.data[n++] = u[--ulen];
            while (--ulen != MPD_SIZE_MAX)
            {
                carry = _mpd_shortmul_c(w.data, w.data, (ulong)n, ubase);
                if (carry != 0)
                {
                    if (n >= wlen)
                    {
                        if (!mpd_qresize(w, n + 1, ref status))
                            return MPD_SIZE_MAX;
                        wlen = n + 1;
                    }
                    w.data[n++] = carry;
                }
                carry = _mpd_shortadd(w.data, (ulong)n, u[ulen]);
                if (carry != 0)
                {
                    if (n >= wlen)
                    {
                        if (!mpd_qresize(w, n + 1, ref status))
                            return MPD_SIZE_MAX;
                        wlen = n + 1;
                    }
                    w.data[n++] = carry;
                }
            }

            return (ulong)n;
        }

        /* target base wbase < source base ubase */
        private static ulong _baseconv_to_smaller(ref uint[] w, ulong wlen, uint wbase, ulong[] u, long ulen, ulong ubase)
        {
            ulong n = 0;

            Debug.Assert(wlen > 0 && ulen > 0);
            Debug.Assert(wbase < ubase);

            do
            {
                if (n >= wlen)
                {
                    if (mpd_resize_u32(ref w, n + 1) == 0)
                        return MPD_SIZE_MAX;
                    wlen = n + 1;
                }
                w[n++] = (uint)_mpd_shortdiv_b(u, u, (ulong)ulen, wbase, ubase);
                /* ulen is at least 1. u[ulen-1] can only be zero if ulen == 1. */
                ulen = _mpd_real_size(u, ulen);

            } while (u[ulen - 1] != 0);

            return n;
        }

        // CONFIG_32 only: _copy_equal_base, _baseconv_to_larger, _coeff_from_larger_base.

        /* target base 'wbase' > source base 'ubase' */
        private static ulong _coeff_from_smaller_base(mpd_t w, long wlen, ulong wbase, ReadOnlySpan<uint> u, ulong ulen,
                                                      ulong ubase, ref uint status)
        {
            long n = 0;
            ulong carry;

            Debug.Assert(wlen > 0 && ulen > 0);
            Debug.Assert(wbase > ubase);

            w.data[n++] = u[(int)--ulen];
            while (--ulen != MPD_SIZE_MAX)
            {
                carry = _mpd_shortmul_b(w.data, w.data, (ulong)n, ubase, wbase);
                if (carry != 0)
                {
                    if (n >= wlen)
                    {
                        if (!mpd_qresize(w, n + 1, ref status))
                            return MPD_SIZE_MAX;
                        wlen = n + 1;
                    }
                    w.data[n++] = carry;
                }
                carry = _mpd_shortadd_b(w.data, (ulong)n, u[(int)ulen], wbase);
                if (carry != 0)
                {
                    if (n >= wlen)
                    {
                        if (!mpd_qresize(w, n + 1, ref status))
                            return MPD_SIZE_MAX;
                        wlen = n + 1;
                    }
                    w.data[n++] = carry;
                }
            }

            return (ulong)n;
        }

        /*
         * Convert an integer mpd_t to a multiprecision integer with base <= 2**16.
         * The least significant word of the result is (*rdata)[0].
         *
         * If rdata is NULL, space is allocated by the function and rlen is irrelevant.
         * In case of an error any allocated storage is freed and rdata is set back to
         * NULL.
         *
         * If rdata is non-NULL, it MUST be allocated by one of libmpdec's allocation
         * functions and rlen MUST be correct. If necessary, the function will resize
         * rdata. In case of an error the caller must free rdata.
         *
         * Return value: In case of success, the exact length of rdata, SIZE_MAX
         * otherwise.
         */
        public static ulong mpd_qexport_u16(ref ushort[] rdata, ulong rlen, uint rbase, mpd_t src, ref uint status)
        {
            mpd_t tsrc = MPD_NEW_STATIC(0, 0, 0, 0);
            bool alloc = false; /* rdata == NULL */
            ulong n;

            Debug.Assert(rbase <= (1U << 16));

            if (mpd_isspecial(src) || !_mpd_isint(src))
            {
                status |= MPD_Invalid_operation;
                return MPD_SIZE_MAX;
            }

            if (rdata == null)
            {
                rlen = mpd_sizeinbase(src, rbase);
                if (rlen == MPD_SIZE_MAX)
                {
                    status |= MPD_Invalid_operation;
                    return MPD_SIZE_MAX;
                }
                rdata = mpd_alloc<ushort>(rlen);
                if (rdata == null)
                    goto malloc_error;
                alloc = true;
            }

            if (mpd_iszero(src))
            {
                rdata[0] = 0;
                return 1;
            }

            if (src.exp >= 0)
            {
                if (!mpd_qshiftl(tsrc, src, src.exp, ref status))
                    goto malloc_error;
            }
            else
            {
                if (mpd_qshiftr(tsrc, src, -src.exp, ref status) == MPD_UINT_MAX)
                    goto malloc_error;
            }

            n = _baseconv_to_u16(ref rdata, rlen, rbase, tsrc.data, tsrc.len);
            if (n == MPD_SIZE_MAX)
                goto malloc_error;


        out_:
            mpd_del(tsrc);
            return n;

        malloc_error:
            if (alloc)
            {
                mpd_free(rdata);
                rdata = null;
            }
            n = MPD_SIZE_MAX;
            status |= MPD_Malloc_error;
            goto out_;
        }

        /*
         * Convert an integer mpd_t to a multiprecision integer with base<=UINT32_MAX.
         * The least significant word of the result is (*rdata)[0].
         *
         * If rdata is NULL, space is allocated by the function and rlen is irrelevant.
         * In case of an error any allocated storage is freed and rdata is set back to
         * NULL.
         *
         * If rdata is non-NULL, it MUST be allocated by one of libmpdec's allocation
         * functions and rlen MUST be correct. If necessary, the function will resize
         * rdata. In case of an error the caller must free rdata.
         *
         * Return value: In case of success, the exact length of rdata, SIZE_MAX
         * otherwise.
         */
        public static ulong mpd_qexport_u32(ref uint[] rdata, ulong rlen, uint rbase, mpd_t src, ref uint status)
        {
            mpd_t tsrc = MPD_NEW_STATIC(0, 0, 0, 0);
            bool alloc = false; /* rdata == NULL */
            ulong n;

            if (mpd_isspecial(src) || !_mpd_isint(src))
            {
                status |= MPD_Invalid_operation;
                return MPD_SIZE_MAX;
            }

            if (rdata == null)
            {
                rlen = mpd_sizeinbase(src, rbase);
                if (rlen == MPD_SIZE_MAX)
                {
                    status |= MPD_Invalid_operation;
                    return MPD_SIZE_MAX;
                }
                rdata = mpd_alloc<uint>(rlen);
                if (rdata == null)
                    goto malloc_error;
                alloc = true;
            }

            if (mpd_iszero(src))
            {
                rdata[0] = 0;
                return 1;
            }

            if (src.exp >= 0)
            {
                if (!mpd_qshiftl(tsrc, src, src.exp, ref status))
                    goto malloc_error;
            }
            else
            {
                if (mpd_qshiftr(tsrc, src, -src.exp, ref status) == MPD_UINT_MAX)
                    goto malloc_error;
            }

            /* CONFIG_64 */
            n = _baseconv_to_smaller(ref rdata, rlen, rbase, tsrc.data, tsrc.len, MPD_RADIX);

            if (n == MPD_SIZE_MAX)
                goto malloc_error;


        out_:
            mpd_del(tsrc);
            return n;

        malloc_error:
            if (alloc)
            {
                mpd_free(rdata);
                rdata = null;
            }
            n = MPD_SIZE_MAX;
            status |= MPD_Malloc_error;
            goto out_;
        }


        /*
         * Converts a multiprecision integer with base <= UINT16_MAX+1 to an mpd_t.
         * The least significant word of the source is srcdata[0].
         */
        public static void mpd_qimport_u16(mpd_t result, ReadOnlySpan<ushort> srcdata, ulong srclen, byte srcsign,
                                           uint srcbase, ref mpd_context_t ctx, ref uint status)
        {
            ulong[] usrc; /* uint16_t src copied to an mpd_uint_t array */
            long rlen; /* length of the result */
            ulong n;

            Debug.Assert(srclen > 0);
            Debug.Assert(srcbase <= (1U << 16));

            rlen = _mpd_importsize(srclen, srcbase);
            if (rlen == MPD_SSIZE_MAX)
            {
                mpd_seterror(result, MPD_Invalid_operation, ref status);
                return;
            }

            usrc = mpd_alloc(srclen);
            if (usrc == null)
            {
                mpd_seterror(result, MPD_Malloc_error, ref status);
                return;
            }
            for (n = 0; n < srclen; n++)
                usrc[n] = srcdata[(int)n];

            if (!mpd_qresize(result, rlen, ref status))
                goto finish;

            n = _coeff_from_u16(result, rlen, usrc, srclen, srcbase, ref status);
            if (n == MPD_SIZE_MAX)
                goto finish;

            mpd_set_flags(result, srcsign);
            result.exp = 0;
            result.len = (long)n;
            mpd_setdigits(result);

            mpd_qresize(result, result.len, ref status);
            mpd_qfinalize(result, ref ctx, ref status);


        finish:
            mpd_free(usrc);
        }

        /*
         * Converts a multiprecision integer with base <= UINT32_MAX to an mpd_t.
         * The least significant word of the source is srcdata[0].
         */
        public static void mpd_qimport_u32(mpd_t result, ReadOnlySpan<uint> srcdata, ulong srclen, byte srcsign,
                                           uint srcbase, ref mpd_context_t ctx, ref uint status)
        {
            long rlen; /* length of the result */
            ulong n;

            Debug.Assert(srclen > 0);

            rlen = _mpd_importsize(srclen, srcbase);
            if (rlen == MPD_SSIZE_MAX)
            {
                mpd_seterror(result, MPD_Invalid_operation, ref status);
                return;
            }

            if (!mpd_qresize(result, rlen, ref status))
                return;

            /* CONFIG_64 */
            n = _coeff_from_smaller_base(result, rlen, MPD_RADIX, srcdata, srclen, srcbase, ref status);

            if (n == MPD_SIZE_MAX)
                return;

            mpd_set_flags(result, srcsign);
            result.exp = 0;
            result.len = (long)n;
            mpd_setdigits(result);

            mpd_qresize(result, result.len, ref status);
            mpd_qfinalize(result, ref ctx, ref status);
        }


        /******************************************************************************/
        /*                                From triple                                 */
        /******************************************************************************/

        // The reference build is MSVC (no __SIZEOF_INT128__): the uint16_t paths.

        // _uint_from_u16
        private static ulong _uint_from_u16(ulong[] w, long wlen, ReadOnlySpan<ushort> u, ulong ulen)
        {
            const ulong ubase = 1U << 16;
            long n = 0;
            ulong carry;

            Debug.Assert(wlen > 0 && ulen > 0);

            w[n++] = u[(int)--ulen];
            while (--ulen != MPD_SIZE_MAX)
            {
                carry = _mpd_shortmul_c(w, w, (ulong)n, ubase);
                if (carry != 0)
                {
                    if (n >= wlen)
                        throw mpd_err_fatal("_uint_from_u16: overflow");  /* GCOV_NOT_REACHED */
                    w[n++] = carry;
                }
                carry = _mpd_shortadd(w, (ulong)n, u[(int)ulen]);
                if (carry != 0)
                {
                    if (n >= wlen)
                        throw mpd_err_fatal("_uint_from_u16: overflow");  /* GCOV_NOT_REACHED */
                    w[n++] = carry;
                }
            }

            return (ulong)n;
        }

        // _set_coeff
        private static long _set_coeff(ulong[] data, long len, ulong hi, ulong lo)
        {
            ushort[] u16 = new ushort[8];

            u16[7] = (ushort)((hi & 0xFFFF000000000000UL) >> 48);
            u16[6] = (ushort)((hi & 0x0000FFFF00000000UL) >> 32);
            u16[5] = (ushort)((hi & 0x00000000FFFF0000UL) >> 16);
            u16[4] = (ushort)(hi & 0x000000000000FFFFUL);

            u16[3] = (ushort)((lo & 0xFFFF000000000000UL) >> 48);
            u16[2] = (ushort)((lo & 0x0000FFFF00000000UL) >> 32);
            u16[1] = (ushort)((lo & 0x00000000FFFF0000UL) >> 16);
            u16[0] = (ushort)(lo & 0x000000000000FFFFUL);

            return (long)_uint_from_u16(data, len, u16, 8);
        }

        // _set_uint128_coeff_exp
        private static int _set_uint128_coeff_exp(mpd_t result, ulong hi, ulong lo, long exp)
        {
            ulong[] data = new ulong[5];
            uint status = 0;
            long len;

            len = _set_coeff(data, 5, hi, lo);

            if (!mpd_qresize(result, len, ref status))
                return -1;

            for (long i = 0; i < len; i++)
                result.data[i] = data[i];

            result.exp = exp;
            result.len = len;
            mpd_setdigits(result);

            return 0;
        }

        // mpd_from_uint128_triple
        public static int mpd_from_uint128_triple(mpd_t result, in mpd_uint128_triple_t triple, ref uint status)
        {
            // static const mpd_context_t maxcontext
            mpd_context_t maxcontext = new mpd_context_t
            {
                prec = MPD_MAX_PREC,
                emax = MPD_MAX_EMAX,
                emin = MPD_MIN_EMIN,
                round = MPD_ROUND_HALF_EVEN,
                traps = MPD_Traps,
                status = 0,
                newtrap = 0,
                clamp = 0,
                allcr = 1,
            };
            mpd_triple_class tag = triple.tag;
            byte sign = triple.sign;
            ulong hi = triple.hi;
            ulong lo = triple.lo;
            long exp;

            exp = triple.exp;

            switch (tag)
            {
                case mpd_triple_class.MPD_TRIPLE_QNAN:
                case mpd_triple_class.MPD_TRIPLE_SNAN:
                    {
                        if (sign > 1 || exp != 0)
                            goto conversion_error;

                        byte flags = tag == mpd_triple_class.MPD_TRIPLE_QNAN ? MPD_NAN : MPD_SNAN;
                        mpd_setspecial(result, sign, flags);

                        if (hi == 0 && lo == 0)
                        {  /* no payload */
                            return 0;
                        }

                        if (_set_uint128_coeff_exp(result, hi, lo, exp) < 0)
                            goto malloc_error;

                        return 0;
                    }

                case mpd_triple_class.MPD_TRIPLE_INF:
                    {
                        if (sign > 1 || hi != 0 || lo != 0 || exp != 0)
                            goto conversion_error;

                        mpd_setspecial(result, sign, MPD_INF);

                        return 0;
                    }

                case mpd_triple_class.MPD_TRIPLE_NORMAL:
                    {
                        if (sign > 1)
                            goto conversion_error;

                        byte flags = sign != 0 ? MPD_NEG : MPD_POS;
                        mpd_set_flags(result, flags);

                        if (exp > MPD_EXP_INF)
                            exp = MPD_EXP_INF;
                        if (exp == MPD_SSIZE_MIN)
                            exp = MPD_SSIZE_MIN + 1;

                        if (_set_uint128_coeff_exp(result, hi, lo, exp) < 0)
                            goto malloc_error;

                        uint workstatus = 0;
                        mpd_qfinalize(result, ref maxcontext, ref workstatus);
                        if ((workstatus & (MPD_Inexact | MPD_Rounded | MPD_Clamped)) != 0)
                            goto conversion_error;

                        return 0;
                    }

                default:
                    goto conversion_error;
            }

        conversion_error:
            mpd_seterror(result, MPD_Conversion_syntax, ref status);
            return -1;

        malloc_error:
            mpd_seterror(result, MPD_Malloc_error, ref status);
            return -1;
        }


        /******************************************************************************/
        /*                                  As triple                                 */
        /******************************************************************************/

        // _uint_to_u16
        private static ulong _uint_to_u16(ushort[] w, ulong[] u, long ulen)
        {
            const ulong wbase = 1U << 16;
            ulong n = 0;

            Debug.Assert(ulen > 0);

            do
            {
                if (n >= 8)
                    throw mpd_err_fatal("_uint_to_u16: overflow");  /* GCOV_NOT_REACHED */
                w[n++] = (ushort)_mpd_shortdiv(u, u, (ulong)ulen, wbase);
                /* ulen is at least 1. u[ulen-1] can only be zero if ulen == 1. */
                ulen = _mpd_real_size(u, ulen);

            } while (u[ulen - 1] != 0);

            return n;
        }

        // _get_coeff
        private static void _get_coeff(out ulong hi, out ulong lo, mpd_t a)
        {
            ushort[] u16 = new ushort[8];
            ulong[] data = new ulong[5];

            switch (a.len)
            {
                case 5:
                    data[4] = a.data[4]; /* fall through */
                    goto case 4;
                case 4:
                    data[3] = a.data[3]; /* fall through */
                    goto case 3;
                case 3:
                    data[2] = a.data[2]; /* fall through */
                    goto case 2;
                case 2:
                    data[1] = a.data[1]; /* fall through */
                    goto case 1;
                case 1:
                    data[0] = a.data[0];
                    break;
                default:
                    throw mpd_err_fatal("_get_coeff: invalid length");  /* GCOV_NOT_REACHED */
            }

            _uint_to_u16(u16, data, a.len);

            hi = (ulong)u16[7] << 48;
            hi |= (ulong)u16[6] << 32;
            hi |= (ulong)u16[5] << 16;
            hi |= (ulong)u16[4];

            lo = (ulong)u16[3] << 48;
            lo |= (ulong)u16[2] << 32;
            lo |= (ulong)u16[1] << 16;
            lo |= (ulong)u16[0];
        }

        // static mpd_uint_t uint128_max_data[3]; static const mpd_t uint128_max (CONFIG_64)
        private static readonly mpd_t uint128_max = new mpd_t(MPD_STATIC | MPD_CONST_DATA, 0, 39, 3, 3,
            new ulong[] { 3374607431768211455UL, 4028236692093846346UL, 3UL });

        // _coeff_as_uint128
        private static mpd_triple_class _coeff_as_uint128(out ulong hi, out ulong lo, mpd_t a)
        {
            mpd_triple_class ret = mpd_triple_class.MPD_TRIPLE_NORMAL;
            uint status = 0;
            mpd_t coeff = new mpd_t();

            hi = lo = 0UL;

            if (mpd_isspecial(a))
            {
                if (mpd_isinfinite(a))
                    return mpd_triple_class.MPD_TRIPLE_INF;

                ret = mpd_isqnan(a) ? mpd_triple_class.MPD_TRIPLE_QNAN : mpd_triple_class.MPD_TRIPLE_SNAN;
                if (a.len == 0)
                { /* no payload */
                    return ret;
                }
            }
            else if (mpd_iszero(a))
            {
                return ret;
            }

            _mpd_copy_shared(coeff, a);
            mpd_set_flags(coeff, 0);
            coeff.exp = 0;

            if (mpd_qcmp(coeff, uint128_max, ref status) > 0)
                return mpd_triple_class.MPD_TRIPLE_ERROR;

            _get_coeff(out hi, out lo, coeff);
            return ret;
        }

        // mpd_as_uint128_triple
        public static mpd_uint128_triple_t mpd_as_uint128_triple(mpd_t a)
        {
            mpd_uint128_triple_t triple = new mpd_uint128_triple_t
            {
                tag = mpd_triple_class.MPD_TRIPLE_ERROR, sign = 0, hi = 0, lo = 0, exp = 0
            };

            triple.tag = _coeff_as_uint128(out triple.hi, out triple.lo, a);
            if (triple.tag == mpd_triple_class.MPD_TRIPLE_ERROR)
                return triple;

            triple.sign = (byte)(mpd_isnegative(a) ? 1 : 0);
            if (triple.tag == mpd_triple_class.MPD_TRIPLE_NORMAL)
                triple.exp = a.exp;

            return triple;
        }
    }
}
