// Modules/_decimal/libmpdec/bits.h (CPython 3.12.15, libmpdec 2.5.1), CONFIG_64.
// MASM: _BitScanReverse64/_BitScanForward64, which BitOperations gives.
using System.Numerics;
using System.Runtime.CompilerServices;

namespace SharpPy.Modules.Mpdecimal
{
    internal static partial class Mpd
    {
        /* Check if n is a power of 2. */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ispower2(ulong n) => n != 0 && (n & (n - 1)) == 0;

        /*
         * Bit scan reverse. Assumptions: a != 0.
         */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int mpd_bsr(ulong a) => 63 - BitOperations.LeadingZeroCount(a);

        /*
         * Bit scan forward. Assumptions: a != 0.
         */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int mpd_bsf(ulong a) => BitOperations.TrailingZeroCount(a);
    }
}
