// Modules/_decimal/libmpdec/mpdecimal.h (CPython 3.12.15, libmpdec 2.5.1), CONFIG_64 as the
// reference Windows x64 build (PCbuild/_decimal.vcxproj: CONFIG_64;MASM) configures it.
//
// Porting conventions for every file of this folder (one C# file per C file, one C# method per C
// function, same names):
//   mpd_uint_t -> ulong, mpd_ssize_t -> long, mpd_size_t -> ulong, uint32_t -> uint, uint8_t -> byte.
//   mpd_t *            -> mpd_t (a class: pointer semantics).
//   mpd_context_t *    -> ref mpd_context_t (a struct: "workctx = *ctx" is a value copy).
//   uint32_t *status   -> ref uint status.
//   mpd_uint_t * arrays (basearith, the number-theoretic transform) -> Span<ulong>; "p + n" is
//   p.Slice((int)n). Indexes are cast to int: a CLR array has fewer than 2^31 elements.
//   MPD_NEW_STATIC/MPD_NEW_CONST/MPD_NEW_SHARED -> the helpers of the same names below.
//   mpd_alloc/mpd_calloc/mpd_realloc return null (or set err) where malloc fails: requests beyond
//   Array.MaxLength, or an OutOfMemoryException, become MPD_Malloc_error as a failed malloc does.
//   Predicates (mpd_isnan, ...) return bool; success/failure ints (mpd_qresize, mpd_qcopy, ...) bool.
using System;
using System.Diagnostics;

namespace SharpPy.Modules.Mpdecimal
{
    /// <summary>mpd_context_t</summary>
    internal struct mpd_context_t
    {
        public long prec;    /* precision */
        public long emax;    /* max positive exp */
        public long emin;    /* min negative exp */
        public uint traps;   /* status events that should be trapped */
        public uint status;  /* status flags */
        public uint newtrap; /* set by mpd_addstatus_raise() */
        public int round;    /* rounding mode */
        public int clamp;    /* clamp mode */
        public int allcr;    /* all functions correctly rounded */
    }

    /// <summary>mpd_t</summary>
    internal sealed class mpd_t
    {
        public byte flags;
        public long exp;
        public long digits;
        public long len;
        public long alloc;
        public ulong[] data;

        public mpd_t() { }

        public mpd_t(byte flags, long exp, long digits, long len, long alloc, ulong[] data)
        {
            this.flags = flags;
            this.exp = exp;
            this.digits = digits;
            this.len = len;
            this.alloc = alloc;
            this.data = data;
        }
    }

    /// <summary>mpd_spec_t: format specification (io.c). Strings hold the UTF-8 text of the C strings.</summary>
    internal sealed class mpd_spec_t
    {
        public long min_width; /* minimum field width */
        public long prec;      /* fraction digits or significant digits */
        public char type;      /* conversion specifier */
        public char align;     /* alignment */
        public char sign;      /* sign printing/alignment */
        public byte[] fill = new byte[5]; /* fill character (UTF-8, NUL-terminated) */
        public byte[] dot;       /* decimal point (UTF-8, no NUL) */
        public byte[] sep;       /* thousands separator (UTF-8, no NUL) */
        public byte[] grouping;  /* grouping of digits (bytes as in localeconv, no NUL) */
    }

    /// <summary>enum mpd_triple_class</summary>
    internal enum mpd_triple_class
    {
        MPD_TRIPLE_NORMAL,
        MPD_TRIPLE_INF,
        MPD_TRIPLE_QNAN,
        MPD_TRIPLE_SNAN,
        MPD_TRIPLE_ERROR,
    }

    /// <summary>mpd_uint128_triple_t</summary>
    internal struct mpd_uint128_triple_t
    {
        public mpd_triple_class tag;
        public byte sign;
        public ulong hi;
        public ulong lo;
        public long exp;
    }

    internal static partial class Mpd
    {
        /******************************************************************************/
        /*                                  Version                                   */
        /******************************************************************************/

        public const int MPD_MAJOR_VERSION = 2;
        public const int MPD_MINOR_VERSION = 5;
        public const int MPD_MICRO_VERSION = 1;
        public const string MPD_VERSION = "2.5.1";
        public const int MPD_VERSION_HEX = (MPD_MAJOR_VERSION << 24) | (MPD_MINOR_VERSION << 16) | (MPD_MICRO_VERSION << 8);

        /******************************************************************************/
        /*                              Configuration (CONFIG_64)                     */
        /******************************************************************************/

        public const ulong MPD_UINT_MAX = ulong.MaxValue;
        public const int MPD_BITS_PER_UINT = 64;
        public const ulong MPD_SIZE_MAX = ulong.MaxValue;
        public const long MPD_SSIZE_MAX = long.MaxValue;
        public const long MPD_SSIZE_MIN = long.MinValue;

        public const ulong MPD_RADIX = 10000000000000000000UL; /* 10**19 */
        public const int MPD_RDIGITS = 19;
        public const int MPD_MAX_POW10 = 19;
        public const int MPD_EXPDIGITS = 19; /* MPD_EXPDIGITS <= MPD_RDIGITS+1 */

        public const ulong MPD_MAXTRANSFORM_2N = 4294967296UL; /* 2**32 */
        public const long MPD_MAX_PREC = 999999999999999999L;
        public const int MPD_MAX_PREC_LOG2 = 64;
        public const long MPD_ELIMIT = 1000000000000000000L;
        public const long MPD_MAX_EMAX = 999999999999999999L;   /* ELIMIT-1 */
        public const long MPD_MIN_EMIN = -999999999999999999L;  /* -EMAX */
        public const long MPD_MIN_ETINY = MPD_MIN_EMIN - (MPD_MAX_PREC - 1);
        public const long MPD_EXP_INF = 2000000000000000001L;
        public const long MPD_EXP_CLAMP = -4000000000000000001L;
        public const long MPD_MAXIMPORT = 105263157894736842L; /* ceil((2*MPD_MAX_PREC)/MPD_RDIGITS) */
        public const int MPD_IEEE_CONTEXT_MAX_BITS = 512;      /* 16*(log2(MPD_MAX_EMAX / 3)-3) */

        /******************************************************************************/
        /*                                Context                                     */
        /******************************************************************************/

        public const int MPD_ROUND_UP = 0;        /* round away from 0               */
        public const int MPD_ROUND_DOWN = 1;      /* round toward 0 (truncate)       */
        public const int MPD_ROUND_CEILING = 2;   /* round toward +infinity          */
        public const int MPD_ROUND_FLOOR = 3;     /* round toward -infinity          */
        public const int MPD_ROUND_HALF_UP = 4;   /* 0.5 is rounded up               */
        public const int MPD_ROUND_HALF_DOWN = 5; /* 0.5 is rounded down             */
        public const int MPD_ROUND_HALF_EVEN = 6; /* 0.5 is rounded to even          */
        public const int MPD_ROUND_05UP = 7;      /* round zero or five away from 0  */
        public const int MPD_ROUND_TRUNC = 8;     /* truncate, but set infinity      */
        public const int MPD_ROUND_GUARD = 9;

        public const int MPD_CLAMP_DEFAULT = 0;
        public const int MPD_CLAMP_IEEE_754 = 1;
        public const int MPD_CLAMP_GUARD = 2;

        /* Status flags */
        public const uint MPD_Clamped = 0x00000001U;
        public const uint MPD_Conversion_syntax = 0x00000002U;
        public const uint MPD_Division_by_zero = 0x00000004U;
        public const uint MPD_Division_impossible = 0x00000008U;
        public const uint MPD_Division_undefined = 0x00000010U;
        public const uint MPD_Fpu_error = 0x00000020U;
        public const uint MPD_Inexact = 0x00000040U;
        public const uint MPD_Invalid_context = 0x00000080U;
        public const uint MPD_Invalid_operation = 0x00000100U;
        public const uint MPD_Malloc_error = 0x00000200U;
        public const uint MPD_Not_implemented = 0x00000400U;
        public const uint MPD_Overflow = 0x00000800U;
        public const uint MPD_Rounded = 0x00001000U;
        public const uint MPD_Subnormal = 0x00002000U;
        public const uint MPD_Underflow = 0x00004000U;
        public const uint MPD_Max_status = 0x00008000U - 1U;

        /* Conditions that result in an IEEE 754 exception */
        public const uint MPD_IEEE_Invalid_operation = MPD_Conversion_syntax | MPD_Division_impossible |
            MPD_Division_undefined | MPD_Fpu_error | MPD_Invalid_context | MPD_Invalid_operation | MPD_Malloc_error;

        /* Errors that require the result of an operation to be set to NaN */
        public const uint MPD_Errors = MPD_IEEE_Invalid_operation | MPD_Division_by_zero;

        /* Default traps */
        public const uint MPD_Traps = MPD_IEEE_Invalid_operation | MPD_Division_by_zero | MPD_Overflow | MPD_Underflow;

        /* Official name */
        public const uint MPD_Insufficient_storage = MPD_Malloc_error;

        /* IEEE 754 interchange format contexts */
        public const int MPD_DECIMAL32 = 32;
        public const int MPD_DECIMAL64 = 64;
        public const int MPD_DECIMAL128 = 128;

        public const long MPD_MINALLOC_MIN = 2;
        public const long MPD_MINALLOC_MAX = 64;

        /******************************************************************************/
        /*                           Decimal Arithmetic                               */
        /******************************************************************************/

        /* mpd_t flags */
        public const byte MPD_POS = 0;
        public const byte MPD_NEG = 1;
        public const byte MPD_INF = 2;
        public const byte MPD_NAN = 4;
        public const byte MPD_SNAN = 8;
        public const byte MPD_SPECIAL = MPD_INF | MPD_NAN | MPD_SNAN;
        public const byte MPD_STATIC = 16;
        public const byte MPD_STATIC_DATA = 32;
        public const byte MPD_SHARED_DATA = 64;
        public const byte MPD_CONST_DATA = 128;
        public const byte MPD_DATAFLAGS = MPD_STATIC_DATA | MPD_SHARED_DATA | MPD_CONST_DATA;

        /* io.c */
        public const int MPD_NUM_FLAGS = 15;
        public const int MPD_MAX_FLAG_STRING = 208;
        public const int MPD_MAX_FLAG_LIST = MPD_MAX_FLAG_STRING + 18;
        public const int MPD_MAX_SIGNAL_LIST = 121;

        /******************************************************************************/
        /*                              Error Macros                                  */
        /******************************************************************************/

        // mpd_err_fatal: abort() in C; an internal error here.
        public static Exception mpd_err_fatal(string message) =>
            new InvalidOperationException("libmpdec: error: " + message);

        // mpd_err_warn: a message on stderr.
        public static void mpd_err_warn(string message) =>
            Console.Error.WriteLine("libmpdec: warning: " + message);

        /******************************************************************************/
        /*                 mpdecimal.c: MPD_NEW_STATIC, MPD_NEW_CONST, MPD_NEW_SHARED  */
        /******************************************************************************/

        // #define MPD_NEW_STATIC(name, flags, exp, digits, len): a static decimal with
        // MPD_MINALLOC_MAX words of static data.
        public static mpd_t MPD_NEW_STATIC(byte flags, long exp, long digits, long len) =>
            new mpd_t((byte)(flags | MPD_STATIC | MPD_STATIC_DATA), exp, digits, len, MPD_MINALLOC_MAX,
                new ulong[MPD_MINALLOC_MAX]);

        // #define MPD_NEW_CONST(name, flags, exp, digits, len, alloc, initval)
        public static mpd_t MPD_NEW_CONST(byte flags, long exp, long digits, long len, long alloc, ulong initval)
        {
            var data = new ulong[alloc];
            data[0] = initval;
            return new mpd_t((byte)(flags | MPD_STATIC | MPD_CONST_DATA), exp, digits, len, alloc, data);
        }

        // #define MPD_NEW_SHARED(name, a): shares the coefficient of a.
        public static mpd_t MPD_NEW_SHARED(mpd_t a) =>
            new mpd_t((byte)((a.flags & ~MPD_DATAFLAGS) | MPD_STATIC | MPD_SHARED_DATA), a.exp, a.digits, a.len,
                a.alloc, a.data);
    }
}
