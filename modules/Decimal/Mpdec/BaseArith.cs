// Modules/_decimal/libmpdec/basearith.h and basearith.c (CPython 3.12.15, libmpdec 2.5.1), CONFIG_64.
// Pointers into coefficients are spans; "u+j" is u.Slice(j). Source and destination may be the
// same memory where the C allows it (spans over one array alias as the pointers do).
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace SharpPy.Modules.Mpdecimal
{
    internal static partial class Mpd
    {
        /*********************************************************************/
        /*                          basearith.h                              */
        /*********************************************************************/

        /*
         * Algorithm from: Division by Invariant Integers using Multiplication,
         * T. Granlund and P. L. Montgomery, Proceedings of the SIGPLAN '94
         * Conference on Programming Language Design and Implementation.
         *
         * http://gmplib.org/~tege/divcnst-pldi94.pdf
         *
         * Variables from the paper and their translations (See section 8):
         *
         *  N := 64
         *  d := MPD_RADIX
         *  l := 64
         *  m' := floor((2**(64+64) - 1)/MPD_RADIX) - 2**64
         *
         * Since N-l == 0:
         *
         *  dnorm := d
         *  n2 := hi
         *  n10 := lo
         *
         * ACL2 proof: mpd-div-words-r-correct
         */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void _mpd_div_words_r(out ulong q, out ulong r, ulong hi, ulong lo)
        {
            ulong n_adj, h, l, t;
            ulong n1_neg;

            /* n1_neg = if lo >= 2**63 then MPD_UINT_MAX else 0 */
            n1_neg = (lo & (1UL << 63)) != 0 ? MPD_UINT_MAX : 0;
            /* n_adj = if lo >= 2**63 then lo+MPD_RADIX else lo */
            n_adj = lo + (n1_neg & MPD_RADIX);

            /* (h, l) = if lo >= 2**63 then m'*(hi+1) else m'*hi */
            _mpd_mul_words(out h, out l, mprime_rdx, hi - n1_neg);
            l = l + n_adj;
            if (l < n_adj) h++;
            t = h + hi;
            /* At this point t == qest, with q == qest or q == qest+1:
             *   1) 0 <= 2**64*hi + lo - qest*MPD_RADIX < 2*MPD_RADIX
             */

            /* t = 2**64-1 - qest = 2**64 - (qest+1) */
            t = MPD_UINT_MAX - t;

            /* (h, l) = 2**64*MPD_RADIX - (qest+1)*MPD_RADIX */
            _mpd_mul_words(out h, out l, t, MPD_RADIX);
            l = l + lo;
            if (l < lo) h++;
            h += hi;
            h -= MPD_RADIX;
            /* (h, l) = 2**64*hi + lo - (qest+1)*MPD_RADIX (mod 2**128)
             * Case q == qest+1:
             *     a) h == 0, l == r
             *     b) q := h - t == qest+1
             *     c) r := l
             * Case q == qest:
             *     a) h == MPD_UINT_MAX, l == 2**64-(MPD_RADIX-r)
             *     b) q := h - t == qest
             *     c) r := l + MPD_RADIX = r
             */

            q = (h - t);
            r = l + (MPD_RADIX & h);
        }

        /* Multiply two single base MPD_RADIX words, store result in array w[2]. */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void _mpd_singlemul(Span<ulong> w, ulong u, ulong v)
        {
            _mpd_mul_words(out ulong hi, out ulong lo, u, v);
            _mpd_div_words_r(out w[1], out w[0], hi, lo);
        }

        /* Multiply u (len 2) and v (len m, 1 <= m <= 2). */
        public static void _mpd_mul_2_le2(Span<ulong> w, ReadOnlySpan<ulong> u, ReadOnlySpan<ulong> v, long m)
        {
            ulong hi, lo;

            _mpd_mul_words(out hi, out lo, u[0], v[0]);
            _mpd_div_words_r(out w[1], out w[0], hi, lo);

            _mpd_mul_words(out hi, out lo, u[1], v[0]);
            lo = w[1] + lo;
            if (lo < w[1]) hi++;
            _mpd_div_words_r(out w[2], out w[1], hi, lo);
            if (m == 1) return;

            _mpd_mul_words(out hi, out lo, u[0], v[1]);
            lo = w[1] + lo;
            if (lo < w[1]) hi++;
            _mpd_div_words_r(out w[3], out w[1], hi, lo);

            _mpd_mul_words(out hi, out lo, u[1], v[1]);
            lo = w[2] + lo;
            if (lo < w[2]) hi++;
            lo = w[3] + lo;
            if (lo < w[3]) hi++;
            _mpd_div_words_r(out w[3], out w[2], hi, lo);
        }

        /*
         * Test if all words from data[len-1] to data[0] are zero. If len is 0, nothing
         * is tested and the coefficient is regarded as "all zero".
         */
        public static bool _mpd_isallzero(ReadOnlySpan<ulong> data, long len)
        {
            while (--len >= 0)
            {
                if (data[(int)len] != 0) return false;
            }
            return true;
        }

        /*
         * Test if all full words from data[len-1] to data[0] are MPD_RADIX-1
         * (all nines). Return true if len == 0.
         */
        public static bool _mpd_isallnine(ReadOnlySpan<ulong> data, long len)
        {
            while (--len >= 0)
            {
                if (data[(int)len] != MPD_RADIX - 1) return false;
            }
            return true;
        }

        /*********************************************************************/
        /*                   Calculations in base MPD_RADIX                  */
        /*********************************************************************/

        /*
         * Knuth, TAOCP, Volume 2, 4.3.1:
         *    w := sum of u (len m) and v (len n)
         *    n > 0 and m >= n
         * The calling function has to handle a possible final carry.
         */
        public static ulong _mpd_baseadd(Span<ulong> w, ReadOnlySpan<ulong> u, ReadOnlySpan<ulong> v, ulong m, ulong n)
        {
            ulong s;
            ulong carry = 0;
            ulong i;

            Debug.Assert(n > 0 && m >= n);

            /* add n members of u and v */
            for (i = 0; i < n; i++)
            {
                s = u[(int)i] + (v[(int)i] + carry);
                carry = (s < u[(int)i] ? 1UL : 0UL) | (s >= MPD_RADIX ? 1UL : 0UL);
                w[(int)i] = carry != 0 ? s - MPD_RADIX : s;
            }
            /* if there is a carry, propagate it */
            for (; carry != 0 && i < m; i++)
            {
                s = u[(int)i] + carry;
                carry = (s == MPD_RADIX) ? 1UL : 0UL;
                w[(int)i] = carry != 0 ? 0 : s;
            }
            /* copy the rest of u */
            for (; i < m; i++)
            {
                w[(int)i] = u[(int)i];
            }

            return carry;
        }

        /*
         * Add the contents of u to w. Carries are propagated further. The caller
         * has to make sure that w is big enough.
         */
        public static void _mpd_baseaddto(Span<ulong> w, ReadOnlySpan<ulong> u, ulong n)
        {
            ulong s;
            ulong carry = 0;
            ulong i;

            if (n == 0) return;

            /* add n members of u to w */
            for (i = 0; i < n; i++)
            {
                s = w[(int)i] + (u[(int)i] + carry);
                carry = (s < w[(int)i] ? 1UL : 0UL) | (s >= MPD_RADIX ? 1UL : 0UL);
                w[(int)i] = carry != 0 ? s - MPD_RADIX : s;
            }
            /* if there is a carry, propagate it */
            for (; carry != 0; i++)
            {
                s = w[(int)i] + carry;
                carry = (s == MPD_RADIX) ? 1UL : 0UL;
                w[(int)i] = carry != 0 ? 0 : s;
            }
        }

        /*
         * Add v to w (len m). The calling function has to handle a possible
         * final carry. Assumption: m > 0.
         */
        public static ulong _mpd_shortadd(Span<ulong> w, ulong m, ulong v)
        {
            ulong s;
            ulong carry;
            ulong i;

            Debug.Assert(m > 0);

            /* add v to w */
            s = w[0] + v;
            carry = (s < v ? 1UL : 0UL) | (s >= MPD_RADIX ? 1UL : 0UL);
            w[0] = carry != 0 ? s - MPD_RADIX : s;

            /* if there is a carry, propagate it */
            for (i = 1; carry != 0 && i < m; i++)
            {
                s = w[(int)i] + carry;
                carry = (s == MPD_RADIX) ? 1UL : 0UL;
                w[(int)i] = carry != 0 ? 0 : s;
            }

            return carry;
        }

        /* Increment u. The calling function has to handle a possible carry. */
        public static ulong _mpd_baseincr(Span<ulong> u, ulong n)
        {
            ulong s;
            ulong carry = 1;
            ulong i;

            Debug.Assert(n > 0);

            /* if there is a carry, propagate it */
            for (i = 0; carry != 0 && i < n; i++)
            {
                s = u[(int)i] + carry;
                carry = (s == MPD_RADIX) ? 1UL : 0UL;
                u[(int)i] = carry != 0 ? 0 : s;
            }

            return carry;
        }

        /*
         * Knuth, TAOCP, Volume 2, 4.3.1:
         *     w := difference of u (len m) and v (len n).
         *     number in u >= number in v;
         */
        public static void _mpd_basesub(Span<ulong> w, ReadOnlySpan<ulong> u, ReadOnlySpan<ulong> v, ulong m, ulong n)
        {
            ulong d;
            ulong borrow = 0;
            ulong i;

            Debug.Assert(m > 0 && n > 0);

            /* subtract n members of v from u */
            for (i = 0; i < n; i++)
            {
                d = u[(int)i] - (v[(int)i] + borrow);
                borrow = (u[(int)i] < d) ? 1UL : 0UL;
                w[(int)i] = borrow != 0 ? d + MPD_RADIX : d;
            }
            /* if there is a borrow, propagate it */
            for (; borrow != 0 && i < m; i++)
            {
                d = u[(int)i] - borrow;
                borrow = (u[(int)i] == 0) ? 1UL : 0UL;
                w[(int)i] = borrow != 0 ? MPD_RADIX - 1 : d;
            }
            /* copy the rest of u */
            for (; i < m; i++)
            {
                w[(int)i] = u[(int)i];
            }
        }

        /*
         * Subtract the contents of u from w. w is larger than u. Borrows are
         * propagated further, but eventually w can absorb the final borrow.
         */
        public static void _mpd_basesubfrom(Span<ulong> w, ReadOnlySpan<ulong> u, ulong n)
        {
            ulong d;
            ulong borrow = 0;
            ulong i;

            if (n == 0) return;

            /* subtract n members of u from w */
            for (i = 0; i < n; i++)
            {
                d = w[(int)i] - (u[(int)i] + borrow);
                borrow = (w[(int)i] < d) ? 1UL : 0UL;
                w[(int)i] = borrow != 0 ? d + MPD_RADIX : d;
            }
            /* if there is a borrow, propagate it */
            for (; borrow != 0; i++)
            {
                d = w[(int)i] - borrow;
                borrow = (w[(int)i] == 0) ? 1UL : 0UL;
                w[(int)i] = borrow != 0 ? MPD_RADIX - 1 : d;
            }
        }

        /* w := product of u (len n) and v (single word) */
        public static void _mpd_shortmul(Span<ulong> w, ReadOnlySpan<ulong> u, ulong n, ulong v)
        {
            ulong hi, lo;
            ulong carry = 0;
            ulong i;

            Debug.Assert(n > 0);

            for (i = 0; i < n; i++)
            {
                _mpd_mul_words(out hi, out lo, u[(int)i], v);
                lo = carry + lo;
                if (lo < carry) hi++;

                _mpd_div_words_r(out carry, out w[(int)i], hi, lo);
            }
            w[(int)i] = carry;
        }

        /*
         * Knuth, TAOCP, Volume 2, 4.3.1:
         *     w := product of u (len m) and v (len n)
         *     w must be initialized to zero
         */
        public static void _mpd_basemul(Span<ulong> w, ReadOnlySpan<ulong> u, ReadOnlySpan<ulong> v, ulong m, ulong n)
        {
            ulong hi, lo;
            ulong carry;
            ulong i, j;

            Debug.Assert(m > 0 && n > 0);

            for (j = 0; j < n; j++)
            {
                carry = 0;
                for (i = 0; i < m; i++)
                {
                    _mpd_mul_words(out hi, out lo, u[(int)i], v[(int)j]);
                    lo = w[(int)(i + j)] + lo;
                    if (lo < w[(int)(i + j)]) hi++;
                    lo = carry + lo;
                    if (lo < carry) hi++;

                    _mpd_div_words_r(out carry, out w[(int)(i + j)], hi, lo);
                }
                w[(int)(j + m)] = carry;
            }
        }

        /*
         * Knuth, TAOCP Volume 2, 4.3.1, exercise 16:
         *     w := quotient of u (len n) divided by a single word v
         */
        public static ulong _mpd_shortdiv(Span<ulong> w, ReadOnlySpan<ulong> u, ulong n, ulong v)
        {
            ulong hi, lo;
            ulong rem = 0;
            ulong i;

            Debug.Assert(n > 0);

            for (i = n - 1; i != MPD_SIZE_MAX; i--)
            {
                _mpd_mul_words(out hi, out lo, rem, MPD_RADIX);
                lo = u[(int)i] + lo;
                if (lo < u[(int)i]) hi++;

                _mpd_div_words(out w[(int)i], out rem, hi, lo, v);
            }

            return rem;
        }

        /*
         * Knuth, TAOCP Volume 2, 4.3.1:
         *     q, r := quotient and remainder of uconst (len nplusm)
         *             divided by vconst (len n)
         *     nplusm >= n
         *
         * If r is not NULL, r will contain the remainder. If r is NULL, the
         * return value indicates if there is a remainder: 1 for true, 0 for
         * false.  A return value of -1 indicates an error.
         *
         * (r == NULL is an empty span here.)
         */
        public static int _mpd_basedivmod(Span<ulong> q, Span<ulong> r, ReadOnlySpan<ulong> uconst,
            ReadOnlySpan<ulong> vconst, ulong nplusm, ulong n)
        {
            Span<ulong> ustatic = stackalloc ulong[(int)MPD_MINALLOC_MAX];
            Span<ulong> vstatic = stackalloc ulong[(int)MPD_MINALLOC_MAX];
            Span<ulong> u = ustatic;
            Span<ulong> v = vstatic;
            ulong d, qhat, rhat;
            Span<ulong> w2 = stackalloc ulong[2];
            ulong hi, lo, x;
            ulong carry;
            ulong i, j, m;
            int retval = 0;

            Debug.Assert(n > 1 && nplusm >= n);
            m = sub_size_t(nplusm, n);

            /* D1: normalize */
            d = MPD_RADIX / (vconst[(int)(n - 1)] + 1);

            if (nplusm >= (ulong)MPD_MINALLOC_MAX)
            {
                ulong[] ua;
                if ((ua = mpd_alloc(nplusm + 1)) == null)
                    return -1;
                u = ua;
            }
            if (n >= (ulong)MPD_MINALLOC_MAX)
            {
                ulong[] va;
                if ((va = mpd_alloc(n + 1)) == null)
                {
                    return -1;
                }
                v = va;
            }

            _mpd_shortmul(u, uconst, nplusm, d);
            _mpd_shortmul(v, vconst, n, d);

            /* D2: loop */
            for (j = m; j != MPD_SIZE_MAX; j--)
            {
                Debug.Assert(2 <= j + n && j + n <= nplusm); /* annotation for scan-build */

                /* D3: calculate qhat and rhat */
                rhat = _mpd_shortdiv(w2, u.Slice((int)(j + n - 1)), 2, v[(int)(n - 1)]);
                qhat = w2[1] * MPD_RADIX + w2[0];

                while (true)
                {
                    if (qhat < MPD_RADIX)
                    {
                        _mpd_singlemul(w2, qhat, v[(int)(n - 2)]);
                        if (w2[1] <= rhat)
                        {
                            if (w2[1] != rhat || w2[0] <= u[(int)(j + n - 2)])
                                break;
                        }
                    }
                    qhat -= 1;
                    rhat += v[(int)(n - 1)];
                    if (rhat < v[(int)(n - 1)] || rhat >= MPD_RADIX)
                        break;
                }
                /* D4: multiply and subtract */
                carry = 0;
                for (i = 0; i <= n; i++)
                {
                    _mpd_mul_words(out hi, out lo, qhat, v[(int)i]);

                    lo = carry + lo;
                    if (lo < carry) hi++;

                    _mpd_div_words_r(out hi, out lo, hi, lo);

                    x = u[(int)(i + j)] - lo;
                    carry = (u[(int)(i + j)] < x) ? 1UL : 0UL;
                    u[(int)(i + j)] = carry != 0 ? x + MPD_RADIX : x;
                    carry += hi;
                }
                q[(int)j] = qhat;
                /* D5: test remainder */
                if (carry != 0)
                {
                    q[(int)j] -= 1;
                    /* D6: add back */
                    _ = _mpd_baseadd(u.Slice((int)j), u.Slice((int)j), v, n + 1, n);
                }
            }

            /* D8: unnormalize */
            if (!r.IsEmpty)
            {
                _mpd_shortdiv(r, u, n, d);
                /* we are not interested in the return value here */
                retval = 0;
            }
            else
            {
                retval = !_mpd_isallzero(u, (long)n) ? 1 : 0;
            }

            return retval;
        }

        /*
         * Left shift of src by 'shift' digits; src may equal dest.
         *
         *  dest := area of n mpd_uint_t with space for srcdigits+shift digits.
         *  src  := coefficient with length m.
         *
         * The case splits in the function are non-obvious. The following
         * equations might help:
         *
         *  Let msdigits denote the number of digits in the most significant
         *  word of src. Then 1 <= msdigits <= rdigits.
         *
         *   1) shift = q * rdigits + r
         *   2) srcdigits = qsrc * rdigits + msdigits
         *   3) destdigits = shift + srcdigits
         *                 = q * rdigits + r + qsrc * rdigits + msdigits
         *                 = q * rdigits + (qsrc * rdigits + (r + msdigits))
         *
         *  The result has q zero words, followed by the coefficient that
         *  is left-shifted by r. The case r == 0 is trivial. For r > 0, it
         *  is important to keep in mind that we always read m source words,
         *  but write m+1 destination words if r + msdigits > rdigits, m words
         *  otherwise.
         */
        public static void _mpd_baseshiftl(Span<ulong> dest, Span<ulong> src, ulong n, ulong m, ulong shift)
        {
            ulong l, lprev, h;
            ulong q, r;
            ulong ph;

            Debug.Assert(m > 0 && n >= m);

            _mpd_div_word(out q, out r, shift, MPD_RDIGITS);

            if (r != 0)
            {
                ph = mpd_pow10[r];

                --m; --n;
                _mpd_divmod_pow10(out h, out lprev, src[(int)m--], MPD_RDIGITS - r);
                if (h != 0)
                { /* r + msdigits > rdigits <==> h != 0 */
                    dest[(int)n--] = h;
                }
                /* write m-1 shifted words */
                for (; m != MPD_SIZE_MAX; m--, n--)
                {
                    _mpd_divmod_pow10(out h, out l, src[(int)m], MPD_RDIGITS - r);
                    dest[(int)n] = ph * lprev + h;
                    lprev = l;
                }
                /* write least significant word */
                dest[(int)q] = ph * lprev;
            }
            else
            {
                while (--m != MPD_SIZE_MAX)
                {
                    dest[(int)(m + q)] = src[(int)m];
                }
            }

            mpd_uint_zero(dest, q);
        }

        /*
         * Right shift of src by 'shift' digits; src may equal dest.
         * Assumption: srcdigits-shift > 0.
         *
         *  dest := area with space for srcdigits-shift digits.
         *  src  := coefficient with length 'slen'.
         *
         * The case splits in the function rely on the following equations:
         *
         *  Let msdigits denote the number of digits in the most significant
         *  word of src. Then 1 <= msdigits <= rdigits.
         *
         *  1) shift = q * rdigits + r
         *  2) srcdigits = qsrc * rdigits + msdigits
         *  3) destdigits = srcdigits - shift
         *                = qsrc * rdigits + msdigits - (q * rdigits + r)
         *                = (qsrc - q) * rdigits + msdigits - r
         *
         * Since destdigits > 0 and 1 <= msdigits <= rdigits:
         *
         *  4) qsrc >= q
         *  5) qsrc == q  ==>  msdigits > r
         *
         * The result has slen-q words if msdigits > r, slen-q-1 words otherwise.
         */
        public static ulong _mpd_baseshiftr(Span<ulong> dest, Span<ulong> src, ulong slen, ulong shift)
        {
            ulong l, h, hprev; /* low, high, previous high */
            ulong rnd, rest;   /* rounding digit, rest */
            ulong q, r;
            ulong i, j;
            ulong ph;

            Debug.Assert(slen > 0);

            _mpd_div_word(out q, out r, shift, MPD_RDIGITS);

            rnd = rest = 0;
            if (r != 0)
            {
                ph = mpd_pow10[MPD_RDIGITS - r];

                _mpd_divmod_pow10(out hprev, out rest, src[(int)q], r);
                _mpd_divmod_pow10(out rnd, out rest, rest, r - 1);

                if (rest == 0 && q > 0)
                {
                    rest = !_mpd_isallzero(src, (long)q) ? 1UL : 0UL;
                }
                /* write slen-q-1 words */
                for (j = 0, i = q + 1; i < slen; i++, j++)
                {
                    _mpd_divmod_pow10(out h, out l, src[(int)i], r);
                    dest[(int)j] = ph * l + hprev;
                    hprev = h;
                }
                /* write most significant word */
                if (hprev != 0)
                { /* always the case if slen==q-1 */
                    dest[(int)j] = hprev;
                }
            }
            else
            {
                if (q > 0)
                {
                    _mpd_divmod_pow10(out rnd, out rest, src[(int)(q - 1)], MPD_RDIGITS - 1);
                    /* is there any non-zero digit below rnd? */
                    if (rest == 0) rest = !_mpd_isallzero(src, (long)(q - 1)) ? 1UL : 0UL;
                }
                for (j = 0; j < slen - q; j++)
                {
                    dest[(int)j] = src[(int)(q + j)];
                }
            }

            /* 0-4  ==> rnd+rest < 0.5   */
            /* 5    ==> rnd+rest == 0.5  */
            /* 6-9  ==> rnd+rest > 0.5   */
            return (rnd == 0 || rnd == 5) ? rnd + (rest != 0 ? 1UL : 0UL) : rnd;
        }

        /*********************************************************************/
        /*                      Calculations in base b                       */
        /*********************************************************************/

        /*
         * Add v to w (len m). The calling function has to handle a possible
         * final carry. Assumption: m > 0.
         */
        public static ulong _mpd_shortadd_b(Span<ulong> w, ulong m, ulong v, ulong b)
        {
            ulong s;
            ulong carry;
            ulong i;

            Debug.Assert(m > 0);

            /* add v to w */
            s = w[0] + v;
            carry = (s < v ? 1UL : 0UL) | (s >= b ? 1UL : 0UL);
            w[0] = carry != 0 ? s - b : s;

            /* if there is a carry, propagate it */
            for (i = 1; carry != 0 && i < m; i++)
            {
                s = w[(int)i] + carry;
                carry = (s == b) ? 1UL : 0UL;
                w[(int)i] = carry != 0 ? 0 : s;
            }

            return carry;
        }

        /* w := product of u (len n) and v (single word). Return carry. */
        public static ulong _mpd_shortmul_c(Span<ulong> w, ReadOnlySpan<ulong> u, ulong n, ulong v)
        {
            ulong hi, lo;
            ulong carry = 0;
            ulong i;

            Debug.Assert(n > 0);

            for (i = 0; i < n; i++)
            {
                _mpd_mul_words(out hi, out lo, u[(int)i], v);
                lo = carry + lo;
                if (lo < carry) hi++;

                _mpd_div_words_r(out carry, out w[(int)i], hi, lo);
            }

            return carry;
        }

        /* w := product of u (len n) and v (single word) */
        public static ulong _mpd_shortmul_b(Span<ulong> w, ReadOnlySpan<ulong> u, ulong n, ulong v, ulong b)
        {
            ulong hi, lo;
            ulong carry = 0;
            ulong i;

            Debug.Assert(n > 0);

            for (i = 0; i < n; i++)
            {
                _mpd_mul_words(out hi, out lo, u[(int)i], v);
                lo = carry + lo;
                if (lo < carry) hi++;

                _mpd_div_words(out carry, out w[(int)i], hi, lo, b);
            }

            return carry;
        }

        /*
         * Knuth, TAOCP Volume 2, 4.3.1, exercise 16:
         *     w := quotient of u (len n) divided by a single word v
         */
        public static ulong _mpd_shortdiv_b(Span<ulong> w, ReadOnlySpan<ulong> u, ulong n, ulong v, ulong b)
        {
            ulong hi, lo;
            ulong rem = 0;
            ulong i;

            Debug.Assert(n > 0);

            for (i = n - 1; i != MPD_SIZE_MAX; i--)
            {
                _mpd_mul_words(out hi, out lo, rem, b);
                lo = u[(int)i] + lo;
                if (lo < u[(int)i]) hi++;

                _mpd_div_words(out w[(int)i], out rem, hi, lo, v);
            }

            return rem;
        }
    }
}
