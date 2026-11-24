// Modules/_decimal/libmpdec/fourstep.c (CPython 3.12.15, libmpdec 2.5.1), CONFIG_64 (not PPRO).
using System;
using System.Diagnostics;

namespace SharpPy.Modules.Mpdecimal
{
    internal static partial class Mpd
    {
        /* Bignum: Cache efficient Matrix Fourier Transform for arrays of the
           form 3 * 2**n (See literature/matrix-transform.txt). */

        // std_size3_ntt
        private static void std_size3_ntt(ref ulong x1, ref ulong x2, ref ulong x3, ReadOnlySpan<ulong> w3table, ulong umod)
        {
            ulong r1, r2;
            ulong w;
            ulong s, tmp;

            /* k = 0 -> w = 1 */
            s = x1;
            s = addmod(s, x2, umod);
            s = addmod(s, x3, umod);

            r1 = s;

            /* k = 1 */
            s = x1;

            w = w3table[1];
            tmp = x64_mulmod(x2, w, umod);
            s = addmod(s, tmp, umod);

            w = w3table[2];
            tmp = x64_mulmod(x3, w, umod);
            s = addmod(s, tmp, umod);

            r2 = s;

            /* k = 2 */
            s = x1;

            w = w3table[2];
            tmp = x64_mulmod(x2, w, umod);
            s = addmod(s, tmp, umod);

            w = w3table[1];
            tmp = x64_mulmod(x3, w, umod);
            s = addmod(s, tmp, umod);

            x3 = s;
            x2 = r2;
            x1 = r1;
        }

        /* forward transform, sign = -1; transform length = 3 * 2**n */
        public static bool four_step_fnt(Span<ulong> a, ulong n, int modnum)
        {
            ulong R = 3; /* number of rows */
            ulong C = n / 3; /* number of columns */
            Span<ulong> w3table = stackalloc ulong[3];
            ulong kernel, w0, w1, wstep;
            ulong s, p0; /* offsets into a; p1 = p0+C, p2 = p0+2*C */
            ulong umod;
            ulong i, k;

            Debug.Assert(n >= 48);
            Debug.Assert(n <= 3 * MPD_MAXTRANSFORM_2N);

            /* Length R transform on the columns. */
            std_setmodulus(modnum, out umod);
            _mpd_init_w3table(w3table, -1, modnum);
            for (p0 = 0; p0 < C; p0++)
            {
                std_size3_ntt(ref a[(int)p0], ref a[(int)(p0 + C)], ref a[(int)(p0 + 2 * C)], w3table, umod);
            }

            /* Multiply each matrix element (addressed by i*C+k) by r**(i*k). */
            kernel = _mpd_getkernel(n, -1, modnum);
            for (i = 1; i < R; i++)
            {
                w0 = 1;                            /* r**(i*0): initial value for k=0 */
                w1 = x64_powmod(kernel, i, umod);  /* r**(i*1): initial value for k=1 */
                wstep = x64_mulmod(w1, w1, umod);  /* r**(2*i) */
                for (k = 0; k < C - 1; k += 2)
                {
                    ulong x0 = a[(int)(i * C + k)];
                    ulong x1 = a[(int)(i * C + k + 1)];
                    x64_mulmod2(ref x0, w0, ref x1, w1, umod);
                    x64_mulmod2c(ref w0, ref w1, wstep, umod);  /* r**(i*(k+2)) = r**(i*k) * r**(2*i) */
                    a[(int)(i * C + k)] = x0;
                    a[(int)(i * C + k + 1)] = x1;
                }
            }

            /* Length C transform on the rows. */
            for (s = 0; s < n; s += C)
            {
                if (!six_step_fnt(a.Slice((int)s), C, modnum))
                    return false;
            }

            /* #if 0: An unordered transform is sufficient for convolution. */

            return true;
        }

        /* backward transform, sign = 1; transform length = 3 * 2**n */
        public static bool inv_four_step_fnt(Span<ulong> a, ulong n, int modnum)
        {
            ulong R = 3; /* number of rows */
            ulong C = n / 3; /* number of columns */
            Span<ulong> w3table = stackalloc ulong[3];
            ulong kernel, w0, w1, wstep;
            ulong s, p0; /* offsets into a; p1 = p0+C, p2 = p0+2*C */
            ulong umod;
            ulong i, k;

            Debug.Assert(n >= 48);
            Debug.Assert(n <= 3 * MPD_MAXTRANSFORM_2N);

            /* #if 0: An unordered transform is sufficient for convolution. */

            /* Length C transform on the rows. */
            for (s = 0; s < n; s += C)
            {
                if (!inv_six_step_fnt(a.Slice((int)s), C, modnum))
                    return false;
            }

            /* Multiply each matrix element (addressed by i*C+k) by r**(i*k). */
            std_setmodulus(modnum, out umod);
            kernel = _mpd_getkernel(n, 1, modnum);
            for (i = 1; i < R; i++)
            {
                w0 = 1;
                w1 = x64_powmod(kernel, i, umod);
                wstep = x64_mulmod(w1, w1, umod);
                for (k = 0; k < C; k += 2)
                {
                    ulong x0 = a[(int)(i * C + k)];
                    ulong x1 = a[(int)(i * C + k + 1)];
                    x64_mulmod2(ref x0, w0, ref x1, w1, umod);
                    x64_mulmod2c(ref w0, ref w1, wstep, umod);
                    a[(int)(i * C + k)] = x0;
                    a[(int)(i * C + k + 1)] = x1;
                }
            }

            /* Length R transform on the columns. */
            _mpd_init_w3table(w3table, 1, modnum);
            for (p0 = 0; p0 < C; p0++)
            {
                std_size3_ntt(ref a[(int)p0], ref a[(int)(p0 + C)], ref a[(int)(p0 + 2 * C)], w3table, umod);
            }

            return true;
        }
    }
}
