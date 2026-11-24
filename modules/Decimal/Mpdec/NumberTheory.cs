// Modules/_decimal/libmpdec/numbertheory.h and numbertheory.c (CPython 3.12.15, libmpdec 2.5.1), CONFIG_64.
// MULMOD/POWMOD/SETMODULUS (constants.h) are x64_mulmod, x64_powmod and std_setmodulus.
using System.Diagnostics;

namespace SharpPy.Modules.Mpdecimal
{
    /* transform parameters (struct fnt_params; the flexible array member is an array) */
    internal sealed class fnt_params
    {
        public int modnum;
        public ulong modulus;
        public ulong kernel;
        public ulong[] wtable;
    }

    internal static partial class Mpd
    {
        // std_setmodulus
        public static void std_setmodulus(int modnum, out ulong umod)
        {
            umod = mpd_moduli[modnum];
        }

        /* Bignum: Initialize the Number Theoretic Transform. */

        /*
         * Return the nth root of unity in F(p). This corresponds to e**((2*pi*i)/n)
         * in the Fourier transform. We have w**n == 1 (mod p).
         *    n := transform length.
         *    sign := -1 for forward transform, 1 for backward transform.
         *    modnum := one of {P1, P2, P3}.
         */
        public static ulong _mpd_getkernel(ulong n, int sign, int modnum)
        {
            ulong umod, p, r, xi;

            std_setmodulus(modnum, out umod);
            r = mpd_roots[modnum]; /* primitive root of F(p) */
            p = umod;
            xi = (p - 1) / n;

            if (sign == -1)
                return x64_powmod(r, (p - 1 - xi), umod);
            else
                return x64_powmod(r, xi, umod);
        }

        /*
         * Initialize and return transform parameters.
         *    n := transform length.
         *    sign := -1 for forward transform, 1 for backward transform.
         *    modnum := one of {P1, P2, P3}.
         */
        public static fnt_params _mpd_init_fnt_params(ulong n, int sign, int modnum)
        {
            fnt_params tparams;
            ulong umod;
            ulong kernel, w;
            ulong i;
            ulong nhalf;

            Debug.Assert(ispower2(n));
            Debug.Assert(sign == -1 || sign == 1);
            Debug.Assert(P1 <= modnum && modnum <= P3);

            nhalf = n / 2;
            // mpd_sh_alloc(sizeof *tparams, nhalf, sizeof (mpd_uint_t))
            var wtable = mpd_alloc(nhalf);
            if (wtable == null)
                return null;
            tparams = new fnt_params { wtable = wtable };

            std_setmodulus(modnum, out umod);
            kernel = _mpd_getkernel(n, sign, modnum);

            tparams.modnum = modnum;
            tparams.modulus = umod;
            tparams.kernel = kernel;

            /* wtable[] := w**0, w**1, ..., w**(nhalf-1) */
            w = 1;
            for (i = 0; i < nhalf; i++)
            {
                tparams.wtable[i] = w;
                w = x64_mulmod(w, kernel, umod);
            }

            return tparams;
        }

        /* Initialize wtable of size three. */
        public static void _mpd_init_w3table(System.Span<ulong> w3table, int sign, int modnum)
        {
            ulong umod;
            ulong kernel;

            std_setmodulus(modnum, out umod);
            kernel = _mpd_getkernel(3, sign, modnum);

            w3table[0] = 1;
            w3table[1] = kernel;
            w3table[2] = x64_powmod(kernel, 2, umod);
        }
    }
}
