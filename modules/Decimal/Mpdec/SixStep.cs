// Modules/_decimal/libmpdec/sixstep.c (CPython 3.12.15, libmpdec 2.5.1), CONFIG_64.
using System;
using System.Diagnostics;

namespace SharpPy.Modules.Mpdecimal
{
    internal static partial class Mpd
    {
        /* Bignum: Cache efficient Matrix Fourier Transform for arrays of the
           form 2**n (See literature/six-step.txt). */

        /* forward transform with sign = -1 */
        public static bool six_step_fnt(Span<ulong> a, ulong n, int modnum)
        {
            fnt_params tparams;
            ulong log2n, C, R;
            ulong kernel;
            ulong umod;
            ulong w0, w1, wstep;
            ulong x; /* mpd_uint_t *x: an offset into a */
            ulong i, k;

            Debug.Assert(ispower2(n));
            Debug.Assert(n >= 16);
            Debug.Assert(n <= MPD_MAXTRANSFORM_2N);

            log2n = (ulong)mpd_bsr(n);
            C = ((ulong)1) << (int)(log2n / 2);  /* number of columns */
            R = ((ulong)1) << (int)(log2n - (log2n / 2)); /* number of rows */

            /* Transpose the matrix. */
            if (!transpose_pow2(a, R, C))
                return false;

            /* Length R transform on the rows. */
            if ((tparams = _mpd_init_fnt_params(R, -1, modnum)) == null)
                return false;
            for (x = 0; x < n; x += R)
                fnt_dif2(a.Slice((int)x), R, tparams);

            /* Transpose the matrix. */
            if (!transpose_pow2(a, C, R))
            {
                mpd_free(tparams);
                return false;
            }

            /* Multiply each matrix element (addressed by i*C+k) by r**(i*k). */
            std_setmodulus(modnum, out umod);
            kernel = _mpd_getkernel(n, -1, modnum);
            for (i = 1; i < R; i++)
            {
                w0 = 1;                            /* r**(i*0): initial value for k=0 */
                w1 = x64_powmod(kernel, i, umod);  /* r**(i*1): initial value for k=1 */
                wstep = x64_mulmod(w1, w1, umod);  /* r**(2*i) */
                for (k = 0; k < C; k += 2)
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
            if (C != R)
            {
                mpd_free(tparams);
                if ((tparams = _mpd_init_fnt_params(C, -1, modnum)) == null)
                    return false;
            }
            for (x = 0; x < n; x += C)
                fnt_dif2(a.Slice((int)x), C, tparams);
            mpd_free(tparams);

            /* #if 0: An unordered transform is sufficient for convolution. */

            return true;
        }

        /* reverse transform, sign = 1 */
        public static bool inv_six_step_fnt(Span<ulong> a, ulong n, int modnum)
        {
            fnt_params tparams;
            ulong log2n, C, R;
            ulong kernel;
            ulong umod;
            ulong w0, w1, wstep;
            ulong x; /* mpd_uint_t *x: an offset into a */
            ulong i, k;

            Debug.Assert(ispower2(n));
            Debug.Assert(n >= 16);
            Debug.Assert(n <= MPD_MAXTRANSFORM_2N);

            log2n = (ulong)mpd_bsr(n);
            C = ((ulong)1) << (int)(log2n / 2); /* number of columns */
            R = ((ulong)1) << (int)(log2n - (log2n / 2)); /* number of rows */

            /* #if 0: An unordered transform is sufficient for convolution. */

            /* Length C transform on the rows. */
            if ((tparams = _mpd_init_fnt_params(C, 1, modnum)) == null)
                return false;
            for (x = 0; x < n; x += C)
                fnt_dif2(a.Slice((int)x), C, tparams);

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

            /* Transpose the matrix. */
            if (!transpose_pow2(a, R, C))
            {
                mpd_free(tparams);
                return false;
            }

            /* Length R transform on the rows. */
            if (R != C)
            {
                mpd_free(tparams);
                if ((tparams = _mpd_init_fnt_params(R, 1, modnum)) == null)
                    return false;
            }
            for (x = 0; x < n; x += R)
                fnt_dif2(a.Slice((int)x), R, tparams);
            mpd_free(tparams);

            /* Transpose the matrix. */
            if (!transpose_pow2(a, C, R))
                return false;

            return true;
        }
    }
}
