// Modules/_decimal/libmpdec/difradix2.c (CPython 3.12.15, libmpdec 2.5.1), CONFIG_64.
using System;
using System.Diagnostics;

namespace SharpPy.Modules.Mpdecimal
{
    internal static partial class Mpd
    {
        /* Bignum: The actual transform routine (decimation in frequency). */

        /*
         * Generate index pairs (x, bitreverse(x)) and carry out the permutation.
         * n must be a power of two.
         * Algorithm due to Brent/Lehmann, see Joerg Arndt, "Matters Computational",
         * Chapter 1.14.4. [http://www.jjj.de/fxt/]
         */
        private static void bitreverse_permute(Span<ulong> a, ulong n)
        {
            ulong x = 0;
            ulong r = 0;
            ulong t;

            do
            { /* Invariant: r = bitreverse(x) */
                if (r > x)
                {
                    t = a[(int)x];
                    a[(int)x] = a[(int)r];
                    a[(int)r] = t;
                }
                /* Flip trailing consecutive 1 bits and the first zero bit
                 * that absorbs a possible carry. */
                x += 1;
                /* Mirror the operation on r: Flip n_trailing_zeros(x)+1
                   high bits of r. */
                r ^= (n - (n >> (mpd_bsf(x) + 1)));
                /* The loop invariant is preserved. */
            } while (x < n);
        }

        /* Fast Number Theoretic Transform, decimation in frequency. */
        public static void fnt_dif2(Span<ulong> a, ulong n, fnt_params tparams)
        {
            ulong[] wtable = tparams.wtable;
            ulong umod;
            ulong u0, u1, v0, v1;
            ulong w, w0, w1, wstep;
            ulong m, mhalf;
            ulong j, r;

            Debug.Assert(ispower2(n));
            Debug.Assert(n >= 4);

            std_setmodulus(tparams.modnum, out umod);

            /* m == n */
            mhalf = n / 2;
            for (j = 0; j < mhalf; j += 2)
            {
                w0 = wtable[j];
                w1 = wtable[j + 1];

                u0 = a[(int)j];
                v0 = a[(int)(j + mhalf)];

                u1 = a[(int)(j + 1)];
                v1 = a[(int)(j + 1 + mhalf)];

                a[(int)j] = addmod(u0, v0, umod);
                v0 = submod(u0, v0, umod);

                a[(int)(j + 1)] = addmod(u1, v1, umod);
                v1 = submod(u1, v1, umod);

                x64_mulmod2(ref v0, w0, ref v1, w1, umod);

                a[(int)(j + mhalf)] = v0;
                a[(int)(j + 1 + mhalf)] = v1;
            }

            wstep = 2;
            for (m = n / 2; m >= 2; m >>= 1, wstep <<= 1)
            {
                mhalf = m / 2;

                /* j == 0 */
                for (r = 0; r < n; r += 2 * m)
                {
                    u0 = a[(int)r];
                    v0 = a[(int)(r + mhalf)];

                    u1 = a[(int)(m + r)];
                    v1 = a[(int)(m + r + mhalf)];

                    a[(int)r] = addmod(u0, v0, umod);
                    v0 = submod(u0, v0, umod);

                    a[(int)(m + r)] = addmod(u1, v1, umod);
                    v1 = submod(u1, v1, umod);

                    a[(int)(r + mhalf)] = v0;
                    a[(int)(m + r + mhalf)] = v1;
                }

                for (j = 1; j < mhalf; j++)
                {
                    w = wtable[j * wstep];

                    for (r = 0; r < n; r += 2 * m)
                    {
                        u0 = a[(int)(r + j)];
                        v0 = a[(int)(r + j + mhalf)];

                        u1 = a[(int)(m + r + j)];
                        v1 = a[(int)(m + r + j + mhalf)];

                        a[(int)(r + j)] = addmod(u0, v0, umod);
                        v0 = submod(u0, v0, umod);

                        a[(int)(m + r + j)] = addmod(u1, v1, umod);
                        v1 = submod(u1, v1, umod);

                        x64_mulmod2c(ref v0, ref v1, w, umod);

                        a[(int)(r + j + mhalf)] = v0;
                        a[(int)(m + r + j + mhalf)] = v1;
                    }
                }
            }

            bitreverse_permute(a, n);
        }
    }
}
