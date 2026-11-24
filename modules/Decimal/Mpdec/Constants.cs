// Modules/_decimal/libmpdec/constants.c and constants.h (CPython 3.12.15, libmpdec 2.5.1), CONFIG_64.
// The MULMOD/POWMOD/SETMODULUS/SIZE3_NTT macros of constants.h choose the x64 functions of
// umodarith.h (x64_mulmod, ...) and numbertheory.h (std_setmodulus, std_size3_ntt); the ports call
// those directly.
namespace SharpPy.Modules.Mpdecimal
{
    internal static partial class Mpd
    {
        // enum {P1, P2, P3};
        public const int P1 = 0;
        public const int P2 = 1;
        public const int P3 = 2;

        /* number-theory.c */
        public static readonly ulong[] mpd_moduli = {
            18446744069414584321UL, 18446744056529682433UL, 18446742974197923841UL
        };
        public static readonly ulong[] mpd_roots = { 7UL, 10UL, 19UL };

        /* crt.c */
        public const ulong INV_P1_MOD_P2 = 18446744055098026669UL;
        public const ulong INV_P1P2_MOD_P3 = 287064143708160UL;
        public const ulong LH_P1P2 = 18446744052234715137UL;     /* (P1*P2) % 2^64 */
        public const ulong UH_P1P2 = 18446744052234715141UL;     /* (P1*P2) / 2^64 */

        /* transpose.c */
        public static readonly ulong[] mpd_bits = {
            1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384,
            32768, 65536, 131072, 262144, 524288, 1048576, 2097152, 4194304, 8388608,
            16777216, 33554432, 67108864, 134217728, 268435456, 536870912, 1073741824,
            2147483648UL, 4294967296UL, 8589934592UL, 17179869184UL, 34359738368UL,
            68719476736UL, 137438953472UL, 274877906944UL, 549755813888UL,
            1099511627776UL, 2199023255552UL, 4398046511104, 8796093022208UL,
            17592186044416UL, 35184372088832UL, 70368744177664UL, 140737488355328UL,
            281474976710656UL, 562949953421312UL, 1125899906842624UL,
            2251799813685248UL, 4503599627370496UL, 9007199254740992UL,
            18014398509481984UL, 36028797018963968UL, 72057594037927936UL,
            144115188075855872UL, 288230376151711744UL, 576460752303423488UL,
            1152921504606846976UL, 2305843009213693952UL, 4611686018427387904UL,
            9223372036854775808UL
        };

        /* mpdecimal.c */
        public static readonly ulong[] mpd_pow10 = {
            1, 10, 100, 1000, 10000, 100000, 1000000, 10000000, 100000000, 1000000000,
            10000000000UL, 100000000000UL, 1000000000000UL, 10000000000000UL,
            100000000000000UL, 1000000000000000UL, 10000000000000000UL,
            100000000000000000UL, 1000000000000000000UL, 10000000000000000000UL
        };

        /* magic number for constant division by MPD_RADIX */
        public const ulong mprime_rdx = 15581492618384294730UL;

        public static readonly string[] mpd_round_string = {
            "ROUND_UP",          /* round away from 0               */
            "ROUND_DOWN",        /* round toward 0 (truncate)       */
            "ROUND_CEILING",     /* round toward +infinity          */
            "ROUND_FLOOR",       /* round toward -infinity          */
            "ROUND_HALF_UP",     /* 0.5 is rounded up               */
            "ROUND_HALF_DOWN",   /* 0.5 is rounded down             */
            "ROUND_HALF_EVEN",   /* 0.5 is rounded to even          */
            "ROUND_05UP",        /* round zero or five away from 0  */
            "ROUND_TRUNC",       /* truncate, but set infinity      */
        };

        public static readonly string[] mpd_clamp_string = {
            "CLAMP_DEFAULT",
            "CLAMP_IEEE_754"
        };
    }
}
