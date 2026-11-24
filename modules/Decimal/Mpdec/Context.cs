// Modules/_decimal/libmpdec/context.c (CPython 3.12.15, libmpdec 2.5.1)
using System;

namespace SharpPy.Modules.Mpdecimal
{
    internal static partial class Mpd
    {
        public delegate void mpd_traphandler_t(ref mpd_context_t ctx);

        // mpd_dflt_traphandler: raise(SIGFPE) in C.
        public static void mpd_dflt_traphandler(ref mpd_context_t ctx)
        {
            throw new InvalidOperationException("libmpdec: SIGFPE (unhandled trap)");
        }

        public static mpd_traphandler_t mpd_traphandler = mpd_dflt_traphandler;

        // static int minalloc_is_set (in mpd_setminalloc)
        private static bool minalloc_is_set = false;

        /* Set guaranteed minimum number of coefficient words. The function may
           be used once at program start. Setting MPD_MINALLOC to out-of-bounds
           values is a catastrophic error, so in that case the function exits rather
           than relying on the user to check a return value. */
        public static void mpd_setminalloc(long n)
        {
            if (minalloc_is_set)
            {
                mpd_err_warn("mpd_setminalloc: ignoring request to set " +
                             "MPD_MINALLOC a second time\n");
                return;
            }
            if (n < MPD_MINALLOC_MIN || n > MPD_MINALLOC_MAX)
            {
                throw mpd_err_fatal("illegal value for MPD_MINALLOC"); /* GCOV_NOT_REACHED */
            }
            MPD_MINALLOC = n;
            minalloc_is_set = true;
        }

        // mpd_init
        public static void mpd_init(ref mpd_context_t ctx, long prec)
        {
            long ideal_minalloc;

            mpd_defaultcontext(ref ctx);

            if (!mpd_qsetprec(ref ctx, prec))
            {
                mpd_addstatus_raise(ref ctx, MPD_Invalid_context);
                return;
            }

            ideal_minalloc = 2 * ((prec + MPD_RDIGITS - 1) / MPD_RDIGITS);
            if (ideal_minalloc < MPD_MINALLOC_MIN) ideal_minalloc = MPD_MINALLOC_MIN;
            if (ideal_minalloc > MPD_MINALLOC_MAX) ideal_minalloc = MPD_MINALLOC_MAX;

            mpd_setminalloc(ideal_minalloc);
        }

        // mpd_maxcontext
        public static void mpd_maxcontext(ref mpd_context_t ctx)
        {
            ctx.prec = MPD_MAX_PREC;
            ctx.emax = MPD_MAX_EMAX;
            ctx.emin = MPD_MIN_EMIN;
            ctx.round = MPD_ROUND_HALF_EVEN;
            ctx.traps = MPD_Traps;
            ctx.status = 0;
            ctx.newtrap = 0;
            ctx.clamp = 0;
            ctx.allcr = 1;
        }

        // mpd_defaultcontext
        public static void mpd_defaultcontext(ref mpd_context_t ctx)
        {
            ctx.prec = 2 * MPD_RDIGITS;
            ctx.emax = MPD_MAX_EMAX;
            ctx.emin = MPD_MIN_EMIN;
            ctx.round = MPD_ROUND_HALF_UP;
            ctx.traps = MPD_Traps;
            ctx.status = 0;
            ctx.newtrap = 0;
            ctx.clamp = 0;
            ctx.allcr = 1;
        }

        // mpd_basiccontext
        public static void mpd_basiccontext(ref mpd_context_t ctx)
        {
            ctx.prec = 9;
            ctx.emax = MPD_MAX_EMAX;
            ctx.emin = MPD_MIN_EMIN;
            ctx.round = MPD_ROUND_HALF_UP;
            ctx.traps = MPD_Traps | MPD_Clamped;
            ctx.status = 0;
            ctx.newtrap = 0;
            ctx.clamp = 0;
            ctx.allcr = 1;
        }

        // mpd_ieee_context
        public static int mpd_ieee_context(ref mpd_context_t ctx, int bits)
        {
            if (bits <= 0 || bits > MPD_IEEE_CONTEXT_MAX_BITS || bits % 32 != 0)
            {
                return -1;
            }

            ctx.prec = 9 * (bits / 32) - 2;
            ctx.emax = 3 * ((long)1 << (bits / 16 + 3));
            ctx.emin = 1 - ctx.emax;
            ctx.round = MPD_ROUND_HALF_EVEN;
            ctx.traps = 0;
            ctx.status = 0;
            ctx.newtrap = 0;
            ctx.clamp = 1;
            ctx.allcr = 1;

            return 0;
        }

        // mpd_getprec
        public static long mpd_getprec(ref mpd_context_t ctx) => ctx.prec;

        // mpd_getemax
        public static long mpd_getemax(ref mpd_context_t ctx) => ctx.emax;

        // mpd_getemin
        public static long mpd_getemin(ref mpd_context_t ctx) => ctx.emin;

        // mpd_getround
        public static int mpd_getround(ref mpd_context_t ctx) => ctx.round;

        // mpd_gettraps
        public static uint mpd_gettraps(ref mpd_context_t ctx) => ctx.traps;

        // mpd_getstatus
        public static uint mpd_getstatus(ref mpd_context_t ctx) => ctx.status;

        // mpd_getclamp
        public static int mpd_getclamp(ref mpd_context_t ctx) => ctx.clamp;

        // mpd_getcr
        public static int mpd_getcr(ref mpd_context_t ctx) => ctx.allcr;

        // mpd_qsetprec
        public static bool mpd_qsetprec(ref mpd_context_t ctx, long prec)
        {
            if (prec <= 0 || prec > MPD_MAX_PREC)
            {
                return false;
            }
            ctx.prec = prec;
            return true;
        }

        // mpd_qsetemax
        public static bool mpd_qsetemax(ref mpd_context_t ctx, long emax)
        {
            if (emax < 0 || emax > MPD_MAX_EMAX)
            {
                return false;
            }
            ctx.emax = emax;
            return true;
        }

        // mpd_qsetemin
        public static bool mpd_qsetemin(ref mpd_context_t ctx, long emin)
        {
            if (emin > 0 || emin < MPD_MIN_EMIN)
            {
                return false;
            }
            ctx.emin = emin;
            return true;
        }

        // mpd_qsetround
        public static bool mpd_qsetround(ref mpd_context_t ctx, int round)
        {
            if (!(0 <= round && round < MPD_ROUND_GUARD))
            {
                return false;
            }
            ctx.round = round;
            return true;
        }

        // mpd_qsettraps
        public static bool mpd_qsettraps(ref mpd_context_t ctx, uint flags)
        {
            if (flags > MPD_Max_status)
            {
                return false;
            }
            ctx.traps = flags;
            return true;
        }

        // mpd_qsetstatus
        public static bool mpd_qsetstatus(ref mpd_context_t ctx, uint flags)
        {
            if (flags > MPD_Max_status)
            {
                return false;
            }
            ctx.status = flags;
            return true;
        }

        // mpd_qsetclamp
        public static bool mpd_qsetclamp(ref mpd_context_t ctx, int c)
        {
            if (c != 0 && c != 1)
            {
                return false;
            }
            ctx.clamp = c;
            return true;
        }

        // mpd_qsetcr
        public static bool mpd_qsetcr(ref mpd_context_t ctx, int c)
        {
            if (c != 0 && c != 1)
            {
                return false;
            }
            ctx.allcr = c;
            return true;
        }

        // mpd_addstatus_raise
        public static void mpd_addstatus_raise(ref mpd_context_t ctx, uint flags)
        {
            ctx.status |= flags;
            if ((flags & ctx.traps) != 0)
            {
                ctx.newtrap = (flags & ctx.traps);
                mpd_traphandler(ref ctx);
            }
        }
    }
}
