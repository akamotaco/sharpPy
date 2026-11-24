// Modules/_decimal/libmpdec/convolute.h and convolute.c (CPython 3.12.15, libmpdec 2.5.1), CONFIG_64.
using System;

namespace SharpPy.Modules.Mpdecimal
{
    internal static partial class Mpd
    {
        public const ulong SIX_STEP_THRESHOLD = 4096;

        // int (*fnt)(mpd_uint_t *, mpd_size_t, int)
        private delegate bool fnt_func(Span<ulong> a, ulong n, int modnum);

        private static readonly fnt_func s_six_step_fnt = six_step_fnt;
        private static readonly fnt_func s_inv_six_step_fnt = inv_six_step_fnt;
        private static readonly fnt_func s_std_fnt = std_fnt;
        private static readonly fnt_func s_std_inv_fnt = std_inv_fnt;
        private static readonly fnt_func s_four_step_fnt = four_step_fnt;
        private static readonly fnt_func s_inv_four_step_fnt = inv_four_step_fnt;

        /* Bignum: Fast convolution using the Number Theoretic Transform. Used for
           the multiplication of very large coefficients. */

        /* Convolute the data in c1 and c2. Result is in c1. */
        public static bool fnt_convolute(Span<ulong> c1, Span<ulong> c2, ulong n, int modnum)
        {
            fnt_func fnt;
            fnt_func inv_fnt;
            ulong n_inv, umod;
            ulong i;

            std_setmodulus(modnum, out umod);
            n_inv = x64_powmod(n, (umod - 2), umod);

            if (ispower2(n))
            {
                if (n > SIX_STEP_THRESHOLD)
                {
                    fnt = s_six_step_fnt;
                    inv_fnt = s_inv_six_step_fnt;
                }
                else
                {
                    fnt = s_std_fnt;
                    inv_fnt = s_std_inv_fnt;
                }
            }
            else
            {
                fnt = s_four_step_fnt;
                inv_fnt = s_inv_four_step_fnt;
            }

            if (!fnt(c1, n, modnum))
                return false;
            if (!fnt(c2, n, modnum))
                return false;
            for (i = 0; i < n - 1; i += 2)
            {
                ulong x0 = c1[(int)i];
                ulong y0 = c2[(int)i];
                ulong x1 = c1[(int)(i + 1)];
                ulong y1 = c2[(int)(i + 1)];
                x64_mulmod2(ref x0, y0, ref x1, y1, umod);
                c1[(int)i] = x0;
                c1[(int)(i + 1)] = x1;
            }

            if (!inv_fnt(c1, n, modnum))
                return false;
            for (i = 0; i < n - 3; i += 4)
            {
                ulong x0 = c1[(int)i];
                ulong x1 = c1[(int)(i + 1)];
                ulong x2 = c1[(int)(i + 2)];
                ulong x3 = c1[(int)(i + 3)];
                x64_mulmod2c(ref x0, ref x1, n_inv, umod);
                x64_mulmod2c(ref x2, ref x3, n_inv, umod);
                c1[(int)i] = x0;
                c1[(int)(i + 1)] = x1;
                c1[(int)(i + 2)] = x2;
                c1[(int)(i + 3)] = x3;
            }

            return true;
        }

        /* Autoconvolute the data in c1. Result is in c1. */
        public static bool fnt_autoconvolute(Span<ulong> c1, ulong n, int modnum)
        {
            fnt_func fnt;
            fnt_func inv_fnt;
            ulong n_inv, umod;
            ulong i;

            std_setmodulus(modnum, out umod);
            n_inv = x64_powmod(n, (umod - 2), umod);

            if (ispower2(n))
            {
                if (n > SIX_STEP_THRESHOLD)
                {
                    fnt = s_six_step_fnt;
                    inv_fnt = s_inv_six_step_fnt;
                }
                else
                {
                    fnt = s_std_fnt;
                    inv_fnt = s_std_inv_fnt;
                }
            }
            else
            {
                fnt = s_four_step_fnt;
                inv_fnt = s_inv_four_step_fnt;
            }

            if (!fnt(c1, n, modnum))
                return false;
            for (i = 0; i < n - 1; i += 2)
            {
                ulong x0 = c1[(int)i];
                ulong x1 = c1[(int)(i + 1)];
                x64_mulmod2(ref x0, x0, ref x1, x1, umod);
                c1[(int)i] = x0;
                c1[(int)(i + 1)] = x1;
            }

            if (!inv_fnt(c1, n, modnum))
                return false;
            for (i = 0; i < n - 3; i += 4)
            {
                ulong x0 = c1[(int)i];
                ulong x1 = c1[(int)(i + 1)];
                ulong x2 = c1[(int)(i + 2)];
                ulong x3 = c1[(int)(i + 3)];
                x64_mulmod2c(ref x0, ref x1, n_inv, umod);
                x64_mulmod2c(ref x2, ref x3, n_inv, umod);
                c1[(int)i] = x0;
                c1[(int)(i + 1)] = x1;
                c1[(int)(i + 2)] = x2;
                c1[(int)(i + 3)] = x3;
            }

            return true;
        }
    }
}
