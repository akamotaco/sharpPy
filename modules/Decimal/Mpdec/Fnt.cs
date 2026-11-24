// Modules/_decimal/libmpdec/fnt.c (CPython 3.12.15, libmpdec 2.5.1)
using System;
using System.Diagnostics;

namespace SharpPy.Modules.Mpdecimal
{
    internal static partial class Mpd
    {
        /* Bignum: Fast transform for medium-sized coefficients. */

        /* forward transform, sign = -1 */
        public static bool std_fnt(Span<ulong> a, ulong n, int modnum)
        {
            fnt_params tparams;

            Debug.Assert(ispower2(n));
            Debug.Assert(n >= 4);
            Debug.Assert(n <= 3 * MPD_MAXTRANSFORM_2N);

            if ((tparams = _mpd_init_fnt_params(n, -1, modnum)) == null)
                return false;
            fnt_dif2(a, n, tparams);

            mpd_free(tparams);
            return true;
        }

        /* reverse transform, sign = 1 */
        public static bool std_inv_fnt(Span<ulong> a, ulong n, int modnum)
        {
            fnt_params tparams;

            Debug.Assert(ispower2(n));
            Debug.Assert(n >= 4);
            Debug.Assert(n <= 3 * MPD_MAXTRANSFORM_2N);

            if ((tparams = _mpd_init_fnt_params(n, 1, modnum)) == null)
                return false;
            fnt_dif2(a, n, tparams);

            mpd_free(tparams);
            return true;
        }
    }
}
