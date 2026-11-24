// Modules/_decimal/libmpdec/mpdecimal.c (CPython 3.12.15, libmpdec 2.5.1), lines 5419-7040:
// Karatsuba and number-theoretic-transform multiplication, mpd_qmul and its variants,
// minus/plus/next_*, the power function, powmod and quantize. CONFIG_64 only; PPRO (x87 control
// word, mpd_set_fenv/mpd_restore_fenv) is not compiled on x64.
using System;
using System.Diagnostics;

namespace SharpPy.Modules.Mpdecimal
{
    internal static partial class Mpd
    {
        /* Minimum space needed for the result array in _karatsuba_rec(). */
        private static ulong _kmul_resultsize(ulong la, ulong lb)
        {
            ulong n, m;

            n = add_size_t(la, lb);
            n = add_size_t(n, 1);

            m = (la + 1) / 2 + 1;
            m = mul_size_t(m, 3);

            return (m > n) ? m : n;
        }

        /* Work space needed in _karatsuba_rec(). lim >= 4 */
        private static ulong _kmul_worksize(ulong n, ulong lim)
        {
            ulong m;

            if (n <= lim)
                return 0;

            m = (n + 1) / 2 + 1;

            return add_size_t(mul_size_t(m, 2), _kmul_worksize(m, lim));
        }

        private const ulong MPD_KARATSUBA_BASECASE = 16;  /* must be >= 4 */

        /*
         * Add the product of a and b to c.
         * c must be _kmul_resultsize(la, lb) in size.
         * w is used as a work array and must be _kmul_worksize(a, lim) in size.
         * Roman E. Maeder, Storage Allocation for the Karatsuba Integer Multiplication
         * Algorithm. In "Design and implementation of symbolic computation systems",
         * Springer, 1993, ISBN 354057235X, 9783540572350.
         */
        private static void _karatsuba_rec(Span<ulong> c, ReadOnlySpan<ulong> a, ReadOnlySpan<ulong> b,
                                           Span<ulong> w, ulong la, ulong lb)
        {
            ulong m, lt;

            Debug.Assert(la >= lb && lb > 0);
            Debug.Assert(la <= MPD_KARATSUBA_BASECASE || w.Length > 0);

            if (la <= MPD_KARATSUBA_BASECASE)
            {
                _mpd_basemul(c, a, b, la, lb);
                return;
            }

            m = (la + 1) / 2;  /* ceil(la/2) */

            /* lb <= m < la */
            if (lb <= m)
            {
                /* lb can now be larger than la-m */
                if (lb > la - m)
                {
                    lt = lb + lb + 1;       /* space needed for result array */
                    mpd_uint_zero(w, lt);   /* clear result array */
                    _karatsuba_rec(w, b, a.Slice((int)m), w.Slice((int)lt), lb, la - m); /* b*ah */
                }
                else
                {
                    lt = (la - m) + (la - m) + 1;  /* space needed for result array */
                    mpd_uint_zero(w, lt);      /* clear result array */
                    _karatsuba_rec(w, a.Slice((int)m), b, w.Slice((int)lt), la - m, lb); /* ah*b */
                }
                _mpd_baseaddto(c.Slice((int)m), w, (la - m) + lb);      /* add ah*b*B**m */

                lt = m + m + 1;         /* space needed for the result array */
                mpd_uint_zero(w, lt);   /* clear result array */
                _karatsuba_rec(w, a, b, w.Slice((int)lt), m, lb);  /* al*b */
                _mpd_baseaddto(c, w, m + lb);    /* add al*b */

                return;
            }

            /* la >= lb > m */
            a.Slice(0, (int)m).CopyTo(w);
            w[(int)m] = 0;
            _mpd_baseaddto(w, a.Slice((int)m), la - m);

            b.Slice(0, (int)m).CopyTo(w.Slice((int)(m + 1)));
            w[(int)(m + 1 + m)] = 0;
            _mpd_baseaddto(w.Slice((int)(m + 1)), b.Slice((int)m), lb - m);

            _karatsuba_rec(c.Slice((int)m), w, w.Slice((int)(m + 1)), w.Slice((int)(2 * (m + 1))), m + 1, m + 1);

            lt = (la - m) + (la - m) + 1;
            mpd_uint_zero(w, lt);

            _karatsuba_rec(w, a.Slice((int)m), b.Slice((int)m), w.Slice((int)lt), la - m, lb - m);

            _mpd_baseaddto(c.Slice((int)(2 * m)), w, (la - m) + (lb - m));
            _mpd_basesubfrom(c.Slice((int)m), w, (la - m) + (lb - m));

            lt = m + m + 1;
            mpd_uint_zero(w, lt);

            _karatsuba_rec(w, a, b, w.Slice((int)lt), m, m);
            _mpd_baseaddto(c, w, m + m);
            _mpd_basesubfrom(c.Slice((int)m), w, m + m);

            return;
        }

        /*
         * Multiply u and v, using Karatsuba multiplication. Returns a pointer
         * to the result or NULL in case of failure (malloc error).
         * Conditions: ulen >= vlen, ulen >= 4
         */
        private static ulong[] _mpd_kmul(ReadOnlySpan<ulong> u, ReadOnlySpan<ulong> v,
                                         ulong ulen, ulong vlen,
                                         out ulong rsize)
        {
            ulong[] result = null, w = null;
            ulong m;

            Debug.Assert(ulen >= 4);
            Debug.Assert(ulen >= vlen);

            rsize = _kmul_resultsize(ulen, vlen);
            if ((result = mpd_calloc(rsize)) == null)
                return null;

            m = _kmul_worksize(ulen, MPD_KARATSUBA_BASECASE);
            if (m != 0 && ((w = mpd_calloc(m)) == null))
            {
                mpd_free(result);
                return null;
            }

            _karatsuba_rec(result, u, v, w, ulen, vlen);


            if (w != null) mpd_free(w);
            return result;
        }


        /*
         * Determine the minimum length for the number theoretic transform. Valid
         * transform lengths are 2**n or 3*2**n, where 2**n <= MPD_MAXTRANSFORM_2N.
         * The function finds the shortest length m such that rsize <= m.
         */
        private static ulong _mpd_get_transform_len(ulong rsize)
        {
            ulong log2rsize;
            ulong x, step;

            Debug.Assert(rsize >= 4);
            log2rsize = (ulong)mpd_bsr(rsize);

            if (rsize <= 1024)
            {
                /* 2**n is faster in this range. */
                x = ((ulong)1) << (int)log2rsize;
                return (rsize == x) ? x : x << 1;
            }
            else if (rsize <= MPD_MAXTRANSFORM_2N)
            {
                x = ((ulong)1) << (int)log2rsize;
                if (rsize == x) return x;
                step = x >> 1;
                x += step;
                return (rsize <= x) ? x : x + step;
            }
            else if (rsize <= MPD_MAXTRANSFORM_2N + MPD_MAXTRANSFORM_2N / 2)
            {
                return MPD_MAXTRANSFORM_2N + MPD_MAXTRANSFORM_2N / 2;
            }
            else if (rsize <= 3 * MPD_MAXTRANSFORM_2N)
            {
                return 3 * MPD_MAXTRANSFORM_2N;
            }
            else
            {
                return MPD_SIZE_MAX;
            }
        }

        // #ifdef PPRO: _mpd_get_control87, _mpd_set_control87, mpd_set_fenv, mpd_restore_fenv
        // are not compiled in the CONFIG_64 (x64) build.

        // u == v in _mpd_fntmul: the two operands start at the same address.
        private static bool _mpd_same_start(ReadOnlySpan<ulong> u, ReadOnlySpan<ulong> v) =>
            !u.IsEmpty && !v.IsEmpty && u.Overlaps(v, out int offset) && offset == 0;

        /*
         * Multiply u and v, using the fast number theoretic transform. Returns
         * a pointer to the result or NULL in case of failure (malloc error).
         */
        private static ulong[] _mpd_fntmul(ReadOnlySpan<ulong> u, ReadOnlySpan<ulong> v,
                                           ulong ulen, ulong vlen,
                                           out ulong rsize)
        {
            ulong[] c1 = null, c2 = null, c3 = null, vtmp = null;
            ulong n;

            rsize = add_size_t(ulen, vlen);
            if ((n = _mpd_get_transform_len(rsize)) == MPD_SIZE_MAX)
                goto malloc_error;

            if ((c1 = mpd_calloc(n)) == null)
                goto malloc_error;
            if ((c2 = mpd_calloc(n)) == null)
                goto malloc_error;
            if ((c3 = mpd_calloc(n)) == null)
                goto malloc_error;

            u.Slice(0, (int)ulen).CopyTo(c1);
            u.Slice(0, (int)ulen).CopyTo(c2);
            u.Slice(0, (int)ulen).CopyTo(c3);

            if (_mpd_same_start(u, v))
            {
                if (!fnt_autoconvolute(c1, n, P1) ||
                    !fnt_autoconvolute(c2, n, P2) ||
                    !fnt_autoconvolute(c3, n, P3))
                {
                    goto malloc_error;
                }
            }
            else
            {
                if ((vtmp = mpd_calloc(n)) == null)
                    goto malloc_error;

                v.Slice(0, (int)vlen).CopyTo(vtmp);
                if (!fnt_convolute(c1, vtmp, n, P1))
                {
                    mpd_free(vtmp);
                    goto malloc_error;
                }

                v.Slice(0, (int)vlen).CopyTo(vtmp);
                mpd_uint_zero(vtmp.AsSpan((int)vlen), n - vlen);
                if (!fnt_convolute(c2, vtmp, n, P2))
                {
                    mpd_free(vtmp);
                    goto malloc_error;
                }

                v.Slice(0, (int)vlen).CopyTo(vtmp);
                mpd_uint_zero(vtmp.AsSpan((int)vlen), n - vlen);
                if (!fnt_convolute(c3, vtmp, n, P3))
                {
                    mpd_free(vtmp);
                    goto malloc_error;
                }

                mpd_free(vtmp);
            }

            crt3(c1, c2, c3, rsize);

        @out:
            if (c2 != null) mpd_free(c2);
            if (c3 != null) mpd_free(c3);
            return c1;

        malloc_error:
            if (c1 != null) mpd_free(c1);
            c1 = null;
            goto @out;
        }


        /*
         * Karatsuba multiplication with FNT/basemul as the base case.
         */
        private static bool _karatsuba_rec_fnt(Span<ulong> c, ReadOnlySpan<ulong> a, ReadOnlySpan<ulong> b,
                                               Span<ulong> w, ulong la, ulong lb)
        {
            ulong m, lt;

            Debug.Assert(la >= lb && lb > 0);
            Debug.Assert(la <= 3 * (MPD_MAXTRANSFORM_2N / 2) || w.Length > 0);

            if (la <= 3 * (MPD_MAXTRANSFORM_2N / 2))
            {
                if (lb <= 192)
                {
                    _mpd_basemul(c, b, a, lb, la);
                }
                else
                {
                    ulong[] result;
                    ulong dummy;

                    if ((result = _mpd_fntmul(a, b, la, lb, out dummy)) == null)
                        return false;
                    result.AsSpan(0, (int)(la + lb)).CopyTo(c);
                    mpd_free(result);
                }
                return true;
            }

            m = (la + 1) / 2;  /* ceil(la/2) */

            /* lb <= m < la */
            if (lb <= m)
            {
                /* lb can now be larger than la-m */
                if (lb > la - m)
                {
                    lt = lb + lb + 1;       /* space needed for result array */
                    mpd_uint_zero(w, lt);   /* clear result array */
                    if (!_karatsuba_rec_fnt(w, b, a.Slice((int)m), w.Slice((int)lt), lb, la - m))
                    { /* b*ah */
                        return false; /* GCOV_UNLIKELY */
                    }
                }
                else
                {
                    lt = (la - m) + (la - m) + 1;  /* space needed for result array */
                    mpd_uint_zero(w, lt);      /* clear result array */
                    if (!_karatsuba_rec_fnt(w, a.Slice((int)m), b, w.Slice((int)lt), la - m, lb))
                    { /* ah*b */
                        return false; /* GCOV_UNLIKELY */
                    }
                }
                _mpd_baseaddto(c.Slice((int)m), w, (la - m) + lb); /* add ah*b*B**m */

                lt = m + m + 1;         /* space needed for the result array */
                mpd_uint_zero(w, lt);   /* clear result array */
                if (!_karatsuba_rec_fnt(w, a, b, w.Slice((int)lt), m, lb))
                {  /* al*b */
                    return false; /* GCOV_UNLIKELY */
                }
                _mpd_baseaddto(c, w, m + lb);       /* add al*b */

                return true;
            }

            /* la >= lb > m */
            a.Slice(0, (int)m).CopyTo(w);
            w[(int)m] = 0;
            _mpd_baseaddto(w, a.Slice((int)m), la - m);

            b.Slice(0, (int)m).CopyTo(w.Slice((int)(m + 1)));
            w[(int)(m + 1 + m)] = 0;
            _mpd_baseaddto(w.Slice((int)(m + 1)), b.Slice((int)m), lb - m);

            if (!_karatsuba_rec_fnt(c.Slice((int)m), w, w.Slice((int)(m + 1)), w.Slice((int)(2 * (m + 1))), m + 1, m + 1))
            {
                return false; /* GCOV_UNLIKELY */
            }

            lt = (la - m) + (la - m) + 1;
            mpd_uint_zero(w, lt);

            if (!_karatsuba_rec_fnt(w, a.Slice((int)m), b.Slice((int)m), w.Slice((int)lt), la - m, lb - m))
            {
                return false; /* GCOV_UNLIKELY */
            }

            _mpd_baseaddto(c.Slice((int)(2 * m)), w, (la - m) + (lb - m));
            _mpd_basesubfrom(c.Slice((int)m), w, (la - m) + (lb - m));

            lt = m + m + 1;
            mpd_uint_zero(w, lt);

            if (!_karatsuba_rec_fnt(w, a, b, w.Slice((int)lt), m, m))
            {
                return false; /* GCOV_UNLIKELY */
            }
            _mpd_baseaddto(c, w, m + m);
            _mpd_basesubfrom(c.Slice((int)m), w, m + m);

            return true;
        }

        /*
         * Multiply u and v, using Karatsuba multiplication with the FNT as the
         * base case. Returns a pointer to the result or NULL in case of failure
         * (malloc error). Conditions: ulen >= vlen, ulen >= 4.
         */
        private static ulong[] _mpd_kmul_fnt(ReadOnlySpan<ulong> u, ReadOnlySpan<ulong> v,
                                             ulong ulen, ulong vlen,
                                             out ulong rsize)
        {
            ulong[] result = null, w = null;
            ulong m;

            Debug.Assert(ulen >= 4);
            Debug.Assert(ulen >= vlen);

            rsize = _kmul_resultsize(ulen, vlen);
            if ((result = mpd_calloc(rsize)) == null)
                return null;

            m = _kmul_worksize(ulen, 3 * (MPD_MAXTRANSFORM_2N / 2));
            if (m != 0 && ((w = mpd_calloc(m)) == null))
            {
                mpd_free(result); /* GCOV_UNLIKELY */
                return null; /* GCOV_UNLIKELY */
            }

            if (!_karatsuba_rec_fnt(result, u, v, w, ulen, vlen))
            {
                mpd_free(result);
                result = null;
            }


            if (w != null) mpd_free(w);
            return result;
        }


        /* Deal with the special cases of multiplying infinities. */
        private static void _mpd_qmul_inf(mpd_t result, mpd_t a, mpd_t b, ref uint status)
        {
            if (mpd_isinfinite(a))
            {
                if (mpd_iszero(b))
                    mpd_seterror(result, MPD_Invalid_operation, ref status);
                else
                    mpd_setspecial(result, (byte)(mpd_sign(a) ^ mpd_sign(b)), MPD_INF);
                return;
            }
            Debug.Assert(mpd_isinfinite(b));
            if (mpd_iszero(a))
                mpd_seterror(result, MPD_Invalid_operation, ref status);
            else
                mpd_setspecial(result, (byte)(mpd_sign(a) ^ mpd_sign(b)), MPD_INF);
        }

        /*
         * Internal function: Multiply a and b. _mpd_qmul deals with specials but
         * does NOT finalize the result. This is for use in mpd_fma().
         */
        private static void _mpd_qmul(mpd_t result, mpd_t a, mpd_t b,
                                      ref mpd_context_t ctx, ref uint status)
        {
            mpd_t big = a, small = b;
            ulong[] rdata = null;
            Span<ulong> rbuf = stackalloc ulong[(int)MPD_MINALLOC_MAX];
            ulong rsize, i;


            if (mpd_isspecial(a) || mpd_isspecial(b))
            {
                if (mpd_qcheck_nans(result, a, b, ref ctx, ref status))
                    return;
                _mpd_qmul_inf(result, a, b, ref status);
                return;
            }

            if (small.len > big.len)
                _mpd_ptrswap(ref big, ref small);

            rsize = (ulong)(big.len + small.len);

            if (big.len == 1)
            {
                _mpd_singlemul(result.data, big.data[0], small.data[0]);
                goto finish;
            }
            if (rsize <= (ulong)MPD_MINALLOC_MAX)
            {
                if (big.len == 2)
                {
                    _mpd_mul_2_le2(rbuf, big.data, small.data, small.len);
                }
                else
                {
                    mpd_uint_zero(rbuf, rsize);
                    if (small.len == 1)
                        _mpd_shortmul(rbuf, big.data, (ulong)big.len, small.data[0]);
                    else
                        _mpd_basemul(rbuf, small.data, big.data, (ulong)small.len, (ulong)big.len);
                }
                if (!mpd_qresize(result, (long)rsize, ref status))
                    return;
                for (i = 0; i < rsize; i++)
                    result.data[i] = rbuf[(int)i];
                goto finish;
            }


            if (small.len <= 256)
            {
                rdata = mpd_calloc(rsize);
                if (rdata != null)
                {
                    if (small.len == 1)
                        _mpd_shortmul(rdata, big.data, (ulong)big.len, small.data[0]);
                    else
                        _mpd_basemul(rdata, small.data, big.data, (ulong)small.len, (ulong)big.len);
                }
            }
            else if (rsize <= 1024)
            {
                rdata = _mpd_kmul(big.data, small.data, (ulong)big.len, (ulong)small.len, out rsize);
            }
            else if (rsize <= 3 * MPD_MAXTRANSFORM_2N)
            {
                rdata = _mpd_fntmul(big.data, small.data, (ulong)big.len, (ulong)small.len, out rsize);
            }
            else
            {
                rdata = _mpd_kmul_fnt(big.data, small.data, (ulong)big.len, (ulong)small.len, out rsize);
            }

            if (rdata == null)
            {
                mpd_seterror(result, MPD_Malloc_error, ref status);
                return;
            }

            if (mpd_isdynamic_data(result))
                mpd_free(result.data);
            result.data = rdata;
            result.alloc = (long)rsize;
            mpd_set_dynamic_data(result);


        finish:
            mpd_set_flags(result, (byte)(mpd_sign(a) ^ mpd_sign(b)));
            result.exp = big.exp + small.exp;
            result.len = _mpd_real_size(result.data, (long)rsize);
            /* resize to smaller cannot fail */
            mpd_qresize(result, result.len, ref status);
            mpd_setdigits(result);
        }

        /* Multiply a and b. */
        public static void mpd_qmul(mpd_t result, mpd_t a, mpd_t b,
                                    ref mpd_context_t ctx, ref uint status)
        {
            _mpd_qmul(result, a, b, ref ctx, ref status);
            mpd_qfinalize(result, ref ctx, ref status);
        }

        /* Multiply a and b. Set NaN/Invalid_operation if the result is inexact. */
        private static void _mpd_qmul_exact(mpd_t result, mpd_t a, mpd_t b,
                                            ref mpd_context_t ctx, ref uint status)
        {
            uint workstatus = 0;

            mpd_qmul(result, a, b, ref ctx, ref workstatus);
            status |= workstatus;
            if ((workstatus & (MPD_Inexact | MPD_Rounded | MPD_Clamped)) != 0)
                mpd_seterror(result, MPD_Invalid_operation, ref status);
        }

        /* Multiply decimal and mpd_ssize_t. */
        public static void mpd_qmul_ssize(mpd_t result, mpd_t a, long b,
                                          ref mpd_context_t ctx, ref uint status)
        {
            mpd_context_t maxcontext = default;
            mpd_t bb = MPD_NEW_STATIC(0, 0, 0, 0);

            mpd_maxcontext(ref maxcontext);
            mpd_qsset_ssize(bb, b, ref maxcontext, ref status);
            mpd_qmul(result, a, bb, ref ctx, ref status);
            mpd_del(bb);
        }

        /* Multiply decimal and mpd_uint_t. */
        public static void mpd_qmul_uint(mpd_t result, mpd_t a, ulong b,
                                         ref mpd_context_t ctx, ref uint status)
        {
            mpd_context_t maxcontext = default;
            mpd_t bb = MPD_NEW_STATIC(0, 0, 0, 0);

            mpd_maxcontext(ref maxcontext);
            mpd_qsset_uint(bb, b, ref maxcontext, ref status);
            mpd_qmul(result, a, bb, ref ctx, ref status);
            mpd_del(bb);
        }

        // mpd_qmul_i32
        public static void mpd_qmul_i32(mpd_t result, mpd_t a, int b,
                                        ref mpd_context_t ctx, ref uint status)
        {
            mpd_qmul_ssize(result, a, b, ref ctx, ref status);
        }

        // mpd_qmul_u32
        public static void mpd_qmul_u32(mpd_t result, mpd_t a, uint b,
                                        ref mpd_context_t ctx, ref uint status)
        {
            mpd_qmul_uint(result, a, b, ref ctx, ref status);
        }

        // mpd_qmul_i64 (CONFIG_64)
        public static void mpd_qmul_i64(mpd_t result, mpd_t a, long b,
                                        ref mpd_context_t ctx, ref uint status)
        {
            mpd_qmul_ssize(result, a, b, ref ctx, ref status);
        }

        // mpd_qmul_u64 (CONFIG_64)
        public static void mpd_qmul_u64(mpd_t result, mpd_t a, ulong b,
                                        ref mpd_context_t ctx, ref uint status)
        {
            mpd_qmul_uint(result, a, b, ref ctx, ref status);
        }

        /* Like the minus operator. */
        public static void mpd_qminus(mpd_t result, mpd_t a, ref mpd_context_t ctx,
                                      ref uint status)
        {
            if (mpd_isspecial(a))
            {
                if (mpd_qcheck_nan(result, a, ref ctx, ref status))
                    return;
            }

            if (mpd_iszero(a) && ctx.round != MPD_ROUND_FLOOR)
                mpd_qcopy_abs(result, a, ref status);
            else
                mpd_qcopy_negate(result, a, ref status);

            mpd_qfinalize(result, ref ctx, ref status);
        }

        /* Like the plus operator. */
        public static void mpd_qplus(mpd_t result, mpd_t a, ref mpd_context_t ctx,
                                     ref uint status)
        {
            if (mpd_isspecial(a))
            {
                if (mpd_qcheck_nan(result, a, ref ctx, ref status))
                    return;
            }

            if (mpd_iszero(a) && ctx.round != MPD_ROUND_FLOOR)
                mpd_qcopy_abs(result, a, ref status);
            else
                mpd_qcopy(result, a, ref status);

            mpd_qfinalize(result, ref ctx, ref status);
        }

        /* The largest representable number that is smaller than the operand. */
        public static void mpd_qnext_minus(mpd_t result, mpd_t a, ref mpd_context_t ctx,
                                           ref uint status)
        {
            mpd_context_t workctx = default;
            mpd_t tiny = MPD_NEW_CONST(MPD_POS, mpd_etiny(ref ctx) - 1, 1, 1, 1, 1);

            if (mpd_isspecial(a))
            {
                if (mpd_qcheck_nan(result, a, ref ctx, ref status))
                    return;

                Debug.Assert(mpd_isinfinite(a));
                if (mpd_isnegative(a))
                {
                    mpd_qcopy(result, a, ref status);
                    return;
                }
                else
                {
                    mpd_clear_flags(result);
                    mpd_qmaxcoeff(result, ref ctx, ref status);
                    if (mpd_isnan(result))
                        return;
                    result.exp = mpd_etop(ref ctx);
                    return;
                }
            }

            mpd_workcontext(ref workctx, ref ctx);
            workctx.round = MPD_ROUND_FLOOR;

            if (!mpd_qcopy(result, a, ref status))
                return;

            mpd_qfinalize(result, ref workctx, ref workctx.status);
            if ((workctx.status & (MPD_Inexact | MPD_Errors)) != 0)
            {
                status |= (workctx.status & MPD_Errors);
                return;
            }

            workctx.status = 0;
            mpd_qsub(result, a, tiny, ref workctx, ref workctx.status);
            status |= (workctx.status & MPD_Errors);
        }

        /* The smallest representable number that is larger than the operand. */
        public static void mpd_qnext_plus(mpd_t result, mpd_t a, ref mpd_context_t ctx,
                                          ref uint status)
        {
            mpd_context_t workctx = default;
            mpd_t tiny = MPD_NEW_CONST(MPD_POS, mpd_etiny(ref ctx) - 1, 1, 1, 1, 1);

            if (mpd_isspecial(a))
            {
                if (mpd_qcheck_nan(result, a, ref ctx, ref status))
                    return;

                Debug.Assert(mpd_isinfinite(a));
                if (mpd_ispositive(a))
                {
                    mpd_qcopy(result, a, ref status);
                }
                else
                {
                    mpd_clear_flags(result);
                    mpd_qmaxcoeff(result, ref ctx, ref status);
                    if (mpd_isnan(result))
                        return;
                    mpd_set_flags(result, MPD_NEG);
                    result.exp = mpd_etop(ref ctx);
                }
                return;
            }

            mpd_workcontext(ref workctx, ref ctx);
            workctx.round = MPD_ROUND_CEILING;

            if (!mpd_qcopy(result, a, ref status))
                return;

            mpd_qfinalize(result, ref workctx, ref workctx.status);
            if ((workctx.status & (MPD_Inexact | MPD_Errors)) != 0)
            {
                status |= (workctx.status & MPD_Errors);
                return;
            }

            workctx.status = 0;
            mpd_qadd(result, a, tiny, ref workctx, ref workctx.status);
            status |= (workctx.status & MPD_Errors);
        }

        /*
         * The number closest to the first operand that is in the direction towards
         * the second operand.
         */
        public static void mpd_qnext_toward(mpd_t result, mpd_t a, mpd_t b,
                                            ref mpd_context_t ctx, ref uint status)
        {
            int c;

            if (mpd_qcheck_nans(result, a, b, ref ctx, ref status))
                return;

            c = _mpd_cmp(a, b);
            if (c == 0)
            {
                mpd_qcopy_sign(result, a, b, ref status);
                return;
            }

            if (c < 0)
                mpd_qnext_plus(result, a, ref ctx, ref status);
            else
                mpd_qnext_minus(result, a, ref ctx, ref status);

            if (mpd_isinfinite(result))
            {
                status |= (MPD_Overflow | MPD_Rounded | MPD_Inexact);
            }
            else if (mpd_adjexp(result) < ctx.emin)
            {
                status |= (MPD_Underflow | MPD_Subnormal | MPD_Rounded | MPD_Inexact);
                if (mpd_iszero(result))
                    status |= MPD_Clamped;
            }
        }

        /*
         * Internal function: Integer power with mpd_uint_t exponent. The function
         * can fail with MPD_Malloc_error.
         *
         * The error is equal to the error incurred in k-1 multiplications. Assuming
         * the upper bound for the relative error in each operation:
         *
         *   abs(err) = 5 * 10**-prec
         *   result = x**k * (1 + err)**(k-1)
         */
        private static void _mpd_qpow_uint(mpd_t result, mpd_t @base, ulong exp,
                                           byte resultsign, ref mpd_context_t ctx, ref uint status)
        {
            uint workstatus = 0;
            ulong n;

            if (exp == 0)
            {
                _settriple(result, resultsign, 1, 0); /* GCOV_NOT_REACHED */
                return; /* GCOV_NOT_REACHED */
            }

            if (!mpd_qcopy(result, @base, ref status))
                return;

            n = mpd_bits[mpd_bsr(exp)];
            while ((n >>= 1) != 0)
            {
                mpd_qmul(result, result, result, ref ctx, ref workstatus);
                if ((exp & n) != 0)
                    mpd_qmul(result, result, @base, ref ctx, ref workstatus);
                if (mpd_isspecial(result) ||
                    (mpd_iszerocoeff(result) && (workstatus & MPD_Clamped) != 0))
                {
                    break;
                }
            }

            status |= workstatus;
            mpd_set_sign(result, resultsign);
        }

        /*
         * Internal function: Integer power with mpd_t exponent, tbase and texp
         * are modified!! Function can fail with MPD_Malloc_error.
         *
         * The error is equal to the error incurred in k multiplications. Assuming
         * the upper bound for the relative error in each operation:
         *
         *   abs(err) = 5 * 10**-prec
         *   result = x**k * (1 + err)**k
         */
        private static void _mpd_qpow_mpd(mpd_t result, mpd_t tbase, mpd_t texp, byte resultsign,
                                          ref mpd_context_t ctx, ref uint status)
        {
            uint workstatus = 0;
            mpd_context_t maxctx = default;
            mpd_t two = MPD_NEW_CONST(0, 0, 1, 1, 1, 2);


            mpd_maxcontext(ref maxctx);

            /* resize to smaller cannot fail */
            mpd_qcopy(result, one, ref status);

            while (!mpd_iszero(texp))
            {
                if (mpd_isodd(texp))
                {
                    mpd_qmul(result, result, tbase, ref ctx, ref workstatus);
                    status |= workstatus;
                    if (mpd_isspecial(result) ||
                        (mpd_iszerocoeff(result) && (workstatus & MPD_Clamped) != 0))
                    {
                        break;
                    }
                }
                mpd_qmul(tbase, tbase, tbase, ref ctx, ref workstatus);
                mpd_qdivint(texp, texp, two, ref maxctx, ref workstatus);
                if (mpd_isnan(tbase) || mpd_isnan(texp))
                {
                    mpd_seterror(result, workstatus & MPD_Errors, ref status);
                    return;
                }
            }
            mpd_set_sign(result, resultsign);
        }

        /*
         * The power function for integer exponents. Relative error _before_ the
         * final rounding to prec:
         *   abs(result - base**exp) < 0.1 * 10**-prec * abs(base**exp)
         */
        private static void _mpd_qpow_int(mpd_t result, mpd_t @base, mpd_t exp,
                                          byte resultsign,
                                          ref mpd_context_t ctx, ref uint status)
        {
            mpd_context_t workctx = default;
            mpd_t tbase = MPD_NEW_STATIC(0, 0, 0, 0);
            mpd_t texp = MPD_NEW_STATIC(0, 0, 0, 0);
            ulong n;


            mpd_workcontext(ref workctx, ref ctx);
            workctx.prec += (exp.digits + exp.exp + 2);
            workctx.round = MPD_ROUND_HALF_EVEN;
            workctx.clamp = 0;
            if (mpd_isnegative(exp))
            {
                uint workstatus = 0;
                workctx.prec += 1;
                mpd_qdiv(tbase, one, @base, ref workctx, ref workstatus);
                status |= workstatus;
                if ((workstatus & MPD_Errors) != 0)
                {
                    mpd_setspecial(result, MPD_POS, MPD_NAN);
                    goto finish;
                }
            }
            else
            {
                if (!mpd_qcopy(tbase, @base, ref status))
                {
                    mpd_setspecial(result, MPD_POS, MPD_NAN);
                    goto finish;
                }
            }

            n = mpd_qabs_uint(exp, ref workctx.status);
            if ((workctx.status & MPD_Invalid_operation) != 0)
            {
                if (!mpd_qcopy(texp, exp, ref status))
                {
                    mpd_setspecial(result, MPD_POS, MPD_NAN); /* GCOV_UNLIKELY */
                    goto finish; /* GCOV_UNLIKELY */
                }
                _mpd_qpow_mpd(result, tbase, texp, resultsign, ref workctx, ref status);
            }
            else
            {
                _mpd_qpow_uint(result, tbase, n, resultsign, ref workctx, ref status);
            }

            if (mpd_isinfinite(result))
            {
                /* for ROUND_DOWN, ROUND_FLOOR, etc. */
                _settriple(result, resultsign, 1, MPD_EXP_INF);
            }

        finish:
            mpd_del(tbase);
            mpd_del(texp);
            mpd_qfinalize(result, ref ctx, ref status);
        }

        /*
         * If the exponent is infinite and base equals one, the result is one
         * with a coefficient of length prec. Otherwise, result is undefined.
         * Return the value of the comparison against one.
         */
        private static int _qcheck_pow_one_inf(mpd_t result, mpd_t @base, byte resultsign,
                                               ref mpd_context_t ctx, ref uint status)
        {
            long shift;
            int cmp;

            if ((cmp = _mpd_cmp(@base, one)) == 0)
            {
                shift = ctx.prec - 1;
                mpd_qshiftl(result, one, shift, ref status);
                result.exp = -shift;
                mpd_set_flags(result, resultsign);
                status |= (MPD_Inexact | MPD_Rounded);
            }

            return cmp;
        }

        /*
         * If abs(base) equals one, calculate the correct power of one result.
         * Otherwise, result is undefined. Return the value of the comparison
         * against 1.
         *
         * This is an internal function that does not check for specials.
         */
        private static int _qcheck_pow_one(mpd_t result, mpd_t @base, mpd_t exp,
                                           byte resultsign,
                                           ref mpd_context_t ctx, ref uint status)
        {
            uint workstatus = 0;
            long shift;
            int cmp;

            if ((cmp = _mpd_cmp_abs(@base, one)) == 0)
            {
                if (_mpd_isint(exp))
                {
                    if (mpd_isnegative(exp))
                    {
                        _settriple(result, resultsign, 1, 0);
                        return 0;
                    }
                    /* 1.000**3 = 1.000000000 */
                    mpd_qmul_ssize(result, exp, -@base.exp, ref ctx, ref workstatus);
                    if ((workstatus & MPD_Errors) != 0)
                    {
                        status |= (workstatus & MPD_Errors);
                        return 0;
                    }
                    /* digits-1 after exponentiation */
                    shift = mpd_qget_ssize(result, ref workstatus);
                    /* shift is MPD_SSIZE_MAX if result is too large */
                    if (shift > ctx.prec - 1)
                    {
                        shift = ctx.prec - 1;
                        status |= MPD_Rounded;
                    }
                }
                else if (mpd_ispositive(@base))
                {
                    shift = ctx.prec - 1;
                    status |= (MPD_Inexact | MPD_Rounded);
                }
                else
                {
                    return -2; /* GCOV_NOT_REACHED */
                }
                if (!mpd_qshiftl(result, one, shift, ref status))
                    return 0;
                result.exp = -shift;
                mpd_set_flags(result, resultsign);
            }

            return cmp;
        }

        /*
         * Detect certain over/underflow of x**y.
         * ACL2 proof: pow-bounds.lisp.
         *
         *   Symbols:
         *
         *     e: EXP_INF or EXP_CLAMP
         *     x: base
         *     y: exponent
         *
         *     omega(e) = log10(abs(e))
         *     zeta(x)  = log10(abs(log10(x)))
         *     theta(y) = log10(abs(y))
         *
         *   Upper and lower bounds:
         *
         *     ub_omega(e) = ceil(log10(abs(e)))
         *     lb_theta(y) = floor(log10(abs(y)))
         *
         *                  | floor(log10(floor(abs(log10(x))))) if x < 1/10 or x >= 10
         *     lb_zeta(x) = | floor(log10(abs(x-1)/10)) if 1/10 <= x < 1
         *                  | floor(log10(abs((x-1)/100))) if 1 < x < 10
         *
         *   ub_omega(e) and lb_theta(y) are obviously upper and lower bounds
         *   for omega(e) and theta(y).
         *
         *   lb_zeta is a lower bound for zeta(x):
         *
         *     x < 1/10 or x >= 10:
         *
         *       abs(log10(x)) >= 1, so the outer log10 is well defined. Since log10
         *       is strictly increasing, the end result is a lower bound.
         *
         *     1/10 <= x < 1:
         *
         *       We use: log10(x) <= (x-1)/log(10)
         *               abs(log10(x)) >= abs(x-1)/log(10)
         *               abs(log10(x)) >= abs(x-1)/10
         *
         *     1 < x < 10:
         *
         *       We use: (x-1)/(x*log(10)) < log10(x)
         *               abs((x-1)/100) < abs(log10(x))
         *
         *       XXX: abs((x-1)/10) would work, need ACL2 proof.
         *
         *
         *   Let (0 < x < 1 and y < 0) or (x > 1 and y > 0).                  (H1)
         *   Let ub_omega(exp_inf) < lb_zeta(x) + lb_theta(y)                 (H2)
         *
         *   Then:
         *       log10(abs(exp_inf)) < log10(abs(log10(x))) + log10(abs(y)).   (1)
         *                   exp_inf < log10(x) * y                            (2)
         *               10**exp_inf < x**y                                    (3)
         *
         *   Let (0 < x < 1 and y > 0) or (x > 1 and y < 0).                  (H3)
         *   Let ub_omega(exp_clamp) < lb_zeta(x) + lb_theta(y)               (H4)
         *
         *   Then:
         *     log10(abs(exp_clamp)) < log10(abs(log10(x))) + log10(abs(y)).   (4)
         *              log10(x) * y < exp_clamp                               (5)
         *                      x**y < 10**exp_clamp                           (6)
         *
         */
        private static long _lower_bound_zeta(mpd_t x, ref uint status)
        {
            mpd_context_t maxctx = default;
            mpd_t scratch = MPD_NEW_STATIC(0, 0, 0, 0);
            long t, u;

            t = mpd_adjexp(x);
            if (t > 0)
            {
                /* x >= 10 -> floor(log10(floor(abs(log10(x))))) */
                return mpd_exp_digits(t) - 1;
            }
            else if (t < -1)
            {
                /* x < 1/10 -> floor(log10(floor(abs(log10(x))))) */
                return mpd_exp_digits(t + 1) - 1;
            }
            else
            {
                mpd_maxcontext(ref maxctx);
                mpd_qsub(scratch, x, one, ref maxctx, ref status);
                if (mpd_isspecial(scratch))
                {
                    mpd_del(scratch);
                    return MPD_SSIZE_MAX;
                }
                u = mpd_adjexp(scratch);
                mpd_del(scratch);

                /* t == -1, 1/10 <= x < 1 -> floor(log10(abs(x-1)/10))
                 * t == 0,  1 < x < 10    -> floor(log10(abs(x-1)/100)) */
                return (t == 0) ? u - 2 : u - 1;
            }
        }

        /*
         * Detect cases of certain overflow/underflow in the power function.
         * Assumptions: x != 1, y != 0. The proof above is for positive x.
         * If x is negative and y is an odd integer, x**y == -(abs(x)**y),
         * so the analysis does not change.
         */
        private static bool _qcheck_pow_bounds(mpd_t result, mpd_t x, mpd_t y,
                                               byte resultsign,
                                               ref mpd_context_t ctx, ref uint status)
        {
            mpd_t abs_x = MPD_NEW_SHARED(x);
            long ub_omega, lb_zeta, lb_theta;
            byte sign;

            mpd_set_positive(abs_x);

            lb_theta = mpd_adjexp(y);
            lb_zeta = _lower_bound_zeta(abs_x, ref status);
            if (lb_zeta == MPD_SSIZE_MAX)
            {
                mpd_seterror(result, MPD_Malloc_error, ref status);
                return true;
            }

            sign = (byte)((mpd_adjexp(abs_x) < 0 ? 1 : 0) ^ mpd_sign(y));
            if (sign == 0)
            {
                /* (0 < |x| < 1 and y < 0) or (|x| > 1 and y > 0) */
                ub_omega = mpd_exp_digits(ctx.emax);
                if (ub_omega < lb_zeta + lb_theta)
                {
                    _settriple(result, resultsign, 1, MPD_EXP_INF);
                    mpd_qfinalize(result, ref ctx, ref status);
                    return true;
                }
            }
            else
            {
                /* (0 < |x| < 1 and y > 0) or (|x| > 1 and y < 0). */
                ub_omega = mpd_exp_digits(mpd_etiny(ref ctx));
                if (ub_omega < lb_zeta + lb_theta)
                {
                    _settriple(result, resultsign, 1, mpd_etiny(ref ctx) - 1);
                    mpd_qfinalize(result, ref ctx, ref status);
                    return true;
                }
            }

            return false;
        }

        /*
         * TODO: Implement algorithm for computing exact powers from decimal.py.
         * In order to prevent infinite loops, this has to be called before
         * using Ziv's strategy for correct rounding.
         */
        /*
        static int
        _mpd_qpow_exact(mpd_t *result, const mpd_t *base, const mpd_t *exp,
                        const mpd_context_t *ctx, uint32_t *status)
        {
            return 0;
        }
        */

        /*
         * The power function for real exponents.
         *   Relative error: abs(result - e**y) < e**y * 1/5 * 10**(-prec - 1)
         */
        private static void _mpd_qpow_real(mpd_t result, mpd_t @base, mpd_t exp,
                                           ref mpd_context_t ctx, ref uint status)
        {
            mpd_context_t workctx = default;
            mpd_t texp = MPD_NEW_STATIC(0, 0, 0, 0);

            if (!mpd_qcopy(texp, exp, ref status))
            {
                mpd_seterror(result, MPD_Malloc_error, ref status);
                return;
            }

            mpd_maxcontext(ref workctx);
            workctx.prec = (@base.digits > ctx.prec) ? @base.digits : ctx.prec;
            workctx.prec += (4 + MPD_EXPDIGITS);
            workctx.round = MPD_ROUND_HALF_EVEN;
            workctx.allcr = ctx.allcr;

            /*
             * extra := MPD_EXPDIGITS = MPD_EXP_MAX_T
             * wp := prec + 4 + extra
             * abs(err) < 5 * 10**-wp
             * y := log(base) * exp
             * Calculate:
             *   1)   e**(y * (1 + err)**2) * (1 + err)
             *      = e**y * e**(y * (2*err + err**2)) * (1 + err)
             *        ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
             * Relative error of the underlined term:
             *   2) abs(e**(y * (2*err + err**2)) - 1)
             * Case abs(y) >= 10**extra:
             *   3) adjexp(y)+1 > log10(abs(y)) >= extra
             *   This triggers the Overflow/Underflow shortcut in _mpd_qexp(),
             *   so no further analysis is necessary.
             * Case abs(y) < 10**extra:
             *   4) abs(y * (2*err + err**2)) < 1/5 * 10**(-prec - 2)
             *   Use (see _mpd_qexp):
             *     5) abs(x) <= 9/10 * 10**-p ==> abs(e**x - 1) < 10**-p
             *   With 2), 4) and 5):
             *     6) abs(e**(y * (2*err + err**2)) - 1) < 10**(-prec - 2)
             *   The complete relative error of 1) is:
             *     7) abs(result - e**y) < e**y * 1/5 * 10**(-prec - 1)
             */
            mpd_qln(result, @base, ref workctx, ref workctx.status);
            mpd_qmul(result, result, texp, ref workctx, ref workctx.status);
            mpd_qexp(result, result, ref workctx, ref status);

            mpd_del(texp);
            status |= (workctx.status & MPD_Errors);
            status |= (MPD_Inexact | MPD_Rounded);
        }

        /* The power function: base**exp */
        public static void mpd_qpow(mpd_t result, mpd_t @base, mpd_t exp,
                                    ref mpd_context_t ctx, ref uint status)
        {
            byte resultsign = 0;
            bool intexp = false;
            int cmp;

            if (mpd_isspecial(@base) || mpd_isspecial(exp))
            {
                if (mpd_qcheck_nans(result, @base, exp, ref ctx, ref status))
                    return;
            }
            if (mpd_isinteger(exp))
            {
                intexp = true;
                resultsign = (byte)((mpd_isnegative(@base) && mpd_isodd(exp)) ? 1 : 0);
            }

            if (mpd_iszero(@base))
            {
                if (mpd_iszero(exp))
                    mpd_seterror(result, MPD_Invalid_operation, ref status);
                else if (mpd_isnegative(exp))
                    mpd_setspecial(result, resultsign, MPD_INF);
                else
                    _settriple(result, resultsign, 0, 0);
                return;
            }
            if (mpd_isnegative(@base))
            {
                if (!intexp || mpd_isinfinite(exp))
                {
                    mpd_seterror(result, MPD_Invalid_operation, ref status);
                    return;
                }
            }
            if (mpd_isinfinite(exp))
            {
                /* power of one */
                cmp = _qcheck_pow_one_inf(result, @base, resultsign, ref ctx, ref status);
                if (cmp == 0)
                {
                    return;
                }
                else
                {
                    cmp *= mpd_arith_sign(exp);
                    if (cmp < 0)
                        _settriple(result, resultsign, 0, 0);
                    else
                        mpd_setspecial(result, resultsign, MPD_INF);
                }
                return;
            }
            if (mpd_isinfinite(@base))
            {
                if (mpd_iszero(exp))
                    _settriple(result, resultsign, 1, 0);
                else if (mpd_isnegative(exp))
                    _settriple(result, resultsign, 0, 0);
                else
                    mpd_setspecial(result, resultsign, MPD_INF);
                return;
            }
            if (mpd_iszero(exp))
            {
                _settriple(result, resultsign, 1, 0);
                return;
            }
            if (_qcheck_pow_one(result, @base, exp, resultsign, ref ctx, ref status) == 0)
                return;
            if (_qcheck_pow_bounds(result, @base, exp, resultsign, ref ctx, ref status))
                return;

            if (intexp)
            {
                _mpd_qpow_int(result, @base, exp, resultsign, ref ctx, ref status);
            }
            else
            {
                _mpd_qpow_real(result, @base, exp, ref ctx, ref status);
                if (!mpd_isspecial(result) && _mpd_cmp(result, one) == 0)
                {
                    long shift = ctx.prec - 1;
                    mpd_qshiftl(result, one, shift, ref status);
                    result.exp = -shift;
                }
                if (mpd_isinfinite(result))
                {
                    /* for ROUND_DOWN, ROUND_FLOOR, etc. */
                    _settriple(result, MPD_POS, 1, MPD_EXP_INF);
                }
                mpd_qfinalize(result, ref ctx, ref status);
            }
        }

        /*
         * Internal function: Integer powmod with mpd_uint_t exponent, base is modified!
         * Function can fail with MPD_Malloc_error.
         */
        private static void _mpd_qpowmod_uint(mpd_t result, mpd_t @base, ulong exp,
                                              mpd_t mod, ref uint status)
        {
            mpd_context_t maxcontext = default;

            mpd_maxcontext(ref maxcontext);

            /* resize to smaller cannot fail */
            mpd_qcopy(result, one, ref status);

            while (exp > 0)
            {
                if ((exp & 1) != 0)
                {
                    _mpd_qmul_exact(result, result, @base, ref maxcontext, ref status);
                    mpd_qrem(result, result, mod, ref maxcontext, ref status);
                }
                _mpd_qmul_exact(@base, @base, @base, ref maxcontext, ref status);
                mpd_qrem(@base, @base, mod, ref maxcontext, ref status);
                exp >>= 1;
            }
        }

        /* The powmod function: (base**exp) % mod */
        public static void mpd_qpowmod(mpd_t result, mpd_t @base, mpd_t exp,
                                       mpd_t mod,
                                       ref mpd_context_t ctx, ref uint status)
        {
            mpd_context_t maxcontext = default;
            mpd_t tbase = MPD_NEW_STATIC(0, 0, 0, 0);
            mpd_t texp = MPD_NEW_STATIC(0, 0, 0, 0);
            mpd_t tmod = MPD_NEW_STATIC(0, 0, 0, 0);
            mpd_t tmp = MPD_NEW_STATIC(0, 0, 0, 0);
            mpd_t two = MPD_NEW_CONST(0, 0, 1, 1, 1, 2);
            long tbase_exp, texp_exp;
            long i;
            mpd_t t;
            ulong r;
            byte sign;


            if (mpd_isspecial(@base) || mpd_isspecial(exp) || mpd_isspecial(mod))
            {
                if (mpd_qcheck_3nans(result, @base, exp, mod, ref ctx, ref status))
                    return;
                mpd_seterror(result, MPD_Invalid_operation, ref status);
                return;
            }


            if (!_mpd_isint(@base) || !_mpd_isint(exp) || !_mpd_isint(mod))
            {
                mpd_seterror(result, MPD_Invalid_operation, ref status);
                return;
            }
            if (mpd_iszerocoeff(mod))
            {
                mpd_seterror(result, MPD_Invalid_operation, ref status);
                return;
            }
            if (mod.digits + mod.exp > ctx.prec)
            {
                mpd_seterror(result, MPD_Invalid_operation, ref status);
                return;
            }

            sign = (byte)(((mpd_isnegative(@base)) && (mpd_isodd(exp))) ? 1 : 0);
            if (mpd_iszerocoeff(exp))
            {
                if (mpd_iszerocoeff(@base))
                {
                    mpd_seterror(result, MPD_Invalid_operation, ref status);
                    return;
                }
                r = (_mpd_cmp_abs(mod, one) == 0) ? 0UL : 1UL;
                _settriple(result, sign, r, 0);
                return;
            }
            if (mpd_isnegative(exp))
            {
                mpd_seterror(result, MPD_Invalid_operation, ref status);
                return;
            }
            if (mpd_iszerocoeff(@base))
            {
                _settriple(result, sign, 0, 0);
                return;
            }

            mpd_maxcontext(ref maxcontext);

            mpd_qrescale(tmod, mod, 0, ref maxcontext, ref maxcontext.status);
            if ((maxcontext.status & MPD_Errors) != 0)
            {
                mpd_seterror(result, maxcontext.status & MPD_Errors, ref status);
                goto @out;
            }
            maxcontext.status = 0;
            mpd_set_positive(tmod);

            mpd_qround_to_int(tbase, @base, ref maxcontext, ref status);
            mpd_set_positive(tbase);
            tbase_exp = tbase.exp;
            tbase.exp = 0;

            mpd_qround_to_int(texp, exp, ref maxcontext, ref status);
            texp_exp = texp.exp;
            texp.exp = 0;

            /* base = (base.int % modulo * pow(10, base.exp, modulo)) % modulo */
            mpd_qrem(tbase, tbase, tmod, ref maxcontext, ref status);
            mpd_qshiftl(result, one, tbase_exp, ref status);
            mpd_qrem(result, result, tmod, ref maxcontext, ref status);
            _mpd_qmul_exact(tbase, tbase, result, ref maxcontext, ref status);
            mpd_qrem(tbase, tbase, tmod, ref maxcontext, ref status);
            if (mpd_isspecial(tbase) ||
                mpd_isspecial(texp) ||
                mpd_isspecial(tmod))
            {
                goto mpd_errors;
            }

            for (i = 0; i < texp_exp; i++)
            {
                _mpd_qpowmod_uint(tmp, tbase, 10, tmod, ref status);
                // t = tmp; tmp = tbase; tbase = t; (the structs are swapped)
                t = tmp;
                tmp = tbase;
                tbase = t;
            }
            if (mpd_isspecial(tbase))
            {
                goto mpd_errors; /* GCOV_UNLIKELY */
            }

            /* resize to smaller cannot fail */
            mpd_qcopy(result, one, ref status);
            while (mpd_isfinite(texp) && !mpd_iszero(texp))
            {
                if (mpd_isodd(texp))
                {
                    _mpd_qmul_exact(result, result, tbase, ref maxcontext, ref status);
                    mpd_qrem(result, result, tmod, ref maxcontext, ref status);
                }
                _mpd_qmul_exact(tbase, tbase, tbase, ref maxcontext, ref status);
                mpd_qrem(tbase, tbase, tmod, ref maxcontext, ref status);
                mpd_qdivint(texp, texp, two, ref maxcontext, ref status);
            }
            if (mpd_isspecial(texp) || mpd_isspecial(tbase) ||
                mpd_isspecial(tmod) || mpd_isspecial(result))
            {
                /* MPD_Malloc_error */
                goto mpd_errors;
            }
            else
            {
                mpd_set_sign(result, sign);
            }

        @out:
            mpd_del(tbase);
            mpd_del(texp);
            mpd_del(tmod);
            mpd_del(tmp);
            return;

        mpd_errors:
            mpd_setspecial(result, MPD_POS, MPD_NAN);
            goto @out;
        }

        // mpd_qquantize
        public static void mpd_qquantize(mpd_t result, mpd_t a, mpd_t b,
                                         ref mpd_context_t ctx, ref uint status)
        {
            uint workstatus = 0;
            long b_exp = b.exp;
            long expdiff, shift;
            ulong rnd;

            if (mpd_isspecial(a) || mpd_isspecial(b))
            {
                if (mpd_qcheck_nans(result, a, b, ref ctx, ref status))
                    return;
                if (mpd_isinfinite(a) && mpd_isinfinite(b))
                {
                    mpd_qcopy(result, a, ref status);
                    return;
                }
                mpd_seterror(result, MPD_Invalid_operation, ref status);
                return;
            }

            if (b.exp > ctx.emax || b.exp < mpd_etiny(ref ctx))
            {
                mpd_seterror(result, MPD_Invalid_operation, ref status);
                return;
            }

            if (mpd_iszero(a))
            {
                _settriple(result, mpd_sign(a), 0, b.exp);
                mpd_qfinalize(result, ref ctx, ref status);
                return;
            }


            expdiff = a.exp - b.exp;
            if (a.digits + expdiff > ctx.prec)
            {
                mpd_seterror(result, MPD_Invalid_operation, ref status);
                return;
            }

            if (expdiff >= 0)
            {
                shift = expdiff;
                if (!mpd_qshiftl(result, a, shift, ref status))
                    return;
                result.exp = b_exp;
            }
            else
            {
                /* At this point expdiff < 0 and a->digits+expdiff <= prec,
                 * so the shift before an increment will fit in prec. */
                shift = -expdiff;
                rnd = mpd_qshiftr(result, a, shift, ref status);
                if (rnd == MPD_UINT_MAX)
                    return;
                result.exp = b_exp;
                if (!_mpd_apply_round_fit(result, rnd, ref ctx, ref status))
                    return;
                workstatus |= MPD_Rounded;
                if (rnd != 0)
                    workstatus |= MPD_Inexact;
            }

            if (mpd_adjexp(result) > ctx.emax ||
                mpd_adjexp(result) < mpd_etiny(ref ctx))
            {
                mpd_seterror(result, MPD_Invalid_operation, ref status);
                return;
            }

            status |= workstatus;
            mpd_qfinalize(result, ref ctx, ref status);
        }
    }
}
