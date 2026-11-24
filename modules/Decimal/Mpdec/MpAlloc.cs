// Modules/_decimal/libmpdec/mpalloc.c (CPython 3.12.15, libmpdec 2.5.1)
//
// malloc/calloc/realloc are CLR array allocations. A request that malloc would refuse (size
// overflow) or that the CLR cannot satisfy (more than Array.MaxLength elements, or an
// OutOfMemoryException) returns null exactly where malloc returns NULL, so the callers set
// MPD_Malloc_error and Python sees MemoryError as with CPython.
using System;
using System.Diagnostics;

namespace SharpPy.Modules.Mpdecimal
{
    internal static partial class Mpd
    {
        /* Guaranteed minimum allocation for a coefficient. May be changed once
           at program start using mpd_setminalloc(). */
        public static long MPD_MINALLOC = MPD_MINALLOC_MIN;

        // malloc(nmemb * size) for an array of T: null where malloc would fail.
        private static T[] mpd_mallocfunc<T>(ulong nmemb)
        {
            if (nmemb > (ulong)Array.MaxLength)
                return null;
            try
            {
                return GC.AllocateUninitializedArray<T>((int)nmemb);
            }
            catch (OutOfMemoryException)
            {
                return null;
            }
        }

        // calloc(nmemb, size)
        private static T[] mpd_callocfunc<T>(ulong nmemb)
        {
            if (nmemb > (ulong)Array.MaxLength)
                return null;
            try
            {
                return new T[(int)nmemb];
            }
            catch (OutOfMemoryException)
            {
                return null;
            }
        }

        // realloc(ptr, nmemb * size): the contents up to the smaller size are kept.
        private static T[] mpd_reallocfunc<T>(T[] ptr, ulong nmemb)
        {
            var n = mpd_mallocfunc<T>(nmemb);
            if (n == null)
                return null;
            if (ptr != null)
                Array.Copy(ptr, n, Math.Min(ptr.Length, n.Length));
            return n;
        }

        // free(): the collector reclaims the array.
        public static void mpd_free(object ptr) { }

        /* emulate calloc if it is not available */
        public static T[] mpd_callocfunc_em<T>(ulong nmemb)
        {
            var ptr = mpd_mallocfunc<T>(nmemb);
            if (ptr == null)
                return null;
            Array.Clear(ptr);
            return ptr;
        }

        /* malloc with overflow checking */
        public static T[] mpd_alloc<T>(ulong nmemb) => mpd_mallocfunc<T>(nmemb);

        public static ulong[] mpd_alloc(ulong nmemb) => mpd_mallocfunc<ulong>(nmemb);

        /* calloc with overflow checking */
        public static T[] mpd_calloc<T>(ulong nmemb) => mpd_callocfunc<T>(nmemb);

        public static ulong[] mpd_calloc(ulong nmemb) => mpd_callocfunc<ulong>(nmemb);

        /* realloc with overflow checking */
        public static T[] mpd_realloc<T>(T[] ptr, ulong nmemb, ref byte err)
        {
            var n = mpd_reallocfunc(ptr, nmemb);
            if (n == null)
            {
                err = 1;
                return ptr;
            }
            return n;
        }

        // mpd_sh_alloc: the struct hack (a header and its data in one block) is not needed with
        // CLR objects; the one user, the decimal objects of _decimal.c, embeds an array instead.

        /* Allocate a new decimal with a coefficient of length 'nwords'. In case
           of an error the return value is NULL. */
        public static mpd_t mpd_qnew_size(long nwords)
        {
            nwords = (nwords < MPD_MINALLOC) ? MPD_MINALLOC : nwords;
            var data = mpd_alloc((ulong)nwords);
            if (data == null)
                return null;
            return new mpd_t(0, 0, 0, 0, nwords, data);
        }

        /* Allocate a new decimal with a coefficient of length MPD_MINALLOC.
           In case of an error the return value is NULL. */
        public static mpd_t mpd_qnew() => mpd_qnew_size(MPD_MINALLOC);

        /* Allocate new decimal. Caller can check for NULL or MPD_Malloc_error.
           Raises on error. */
        public static mpd_t mpd_new(ref mpd_context_t ctx)
        {
            var result = mpd_qnew();
            if (result == null)
                mpd_addstatus_raise(ref ctx, MPD_Malloc_error);
            return result;
        }

        /*
         * Input: 'result' is a static mpd_t with a static coefficient.
         * Assumption: 'nwords' >= result->alloc.
         *
         * Resize the static coefficient to a larger dynamic one and copy the
         * existing data. If successful, the value of 'result' is unchanged.
         * Otherwise, set 'result' to NaN and update 'status' with MPD_Malloc_error.
         */
        public static bool mpd_switch_to_dyn(mpd_t result, long nwords, ref uint status)
        {
            var p = result.data;
            Debug.Assert(nwords >= result.alloc);
            result.data = mpd_alloc((ulong)nwords);
            if (result.data == null)
            {
                result.data = p;
                mpd_set_qnan(result);
                mpd_set_positive(result);
                result.exp = result.digits = result.len = 0;
                status |= MPD_Malloc_error;
                return false;
            }
            Array.Copy(p, result.data, result.alloc);
            result.alloc = nwords;
            mpd_set_dynamic_data(result);
            return true;
        }

        /*
         * Input: 'result' is a static mpd_t with a static coefficient.
         *
         * Convert the coefficient to a dynamic one that is initialized to zero. If
         * malloc fails, set 'result' to NaN and update 'status' with MPD_Malloc_error.
         */
        public static bool mpd_switch_to_dyn_zero(mpd_t result, long nwords, ref uint status)
        {
            var p = result.data;
            result.data = mpd_calloc((ulong)nwords);
            if (result.data == null)
            {
                result.data = p;
                mpd_set_qnan(result);
                mpd_set_positive(result);
                result.exp = result.digits = result.len = 0;
                status |= MPD_Malloc_error;
                return false;
            }
            result.alloc = nwords;
            mpd_set_dynamic_data(result);
            return true;
        }

        /*
         * Input: 'result' is a static or a dynamic mpd_t with a dynamic coefficient.
         * Resize the coefficient to length 'nwords':
         *   Case nwords > result->alloc:
         *     If realloc is successful:
         *       'result' has a larger coefficient but the same value. Return 1.
         *     Otherwise:
         *       Set 'result' to NaN, update status with MPD_Malloc_error and return 0.
         *   Case nwords < result->alloc:
         *     If realloc is successful:
         *       'result' has a smaller coefficient. result->len is undefined. Return 1.
         *     Otherwise (unlikely):
         *       'result' is unchanged. Reuse the now oversized coefficient. Return 1.
         */
        public static bool mpd_realloc_dyn(mpd_t result, long nwords, ref uint status)
        {
            byte err = 0;
            result.data = mpd_realloc(result.data, (ulong)nwords, ref err);
            if (err == 0)
            {
                result.alloc = nwords;
            }
            else if (nwords > result.alloc)
            {
                mpd_set_qnan(result);
                mpd_set_positive(result);
                result.exp = result.digits = result.len = 0;
                status |= MPD_Malloc_error;
                return false;
            }
            return true;
        }

        /*
         * Input: 'result' is a static mpd_t with a static coefficient.
         * Assumption: 'nwords' >= result->alloc.
         *
         * Resize the static coefficient to a larger dynamic one and copy the
         * existing data.
         *
         * On failure the value of 'result' is unchanged.
         */
        public static bool mpd_switch_to_dyn_cxx(mpd_t result, long nwords)
        {
            Debug.Assert(nwords >= result.alloc);
            var data = mpd_alloc((ulong)nwords);
            if (data == null)
                return false;
            Array.Copy(result.data, data, result.alloc);
            result.data = data;
            result.alloc = nwords;
            mpd_set_dynamic_data(result);
            return true;
        }

        /*
         * Input: 'result' is a static or a dynamic mpd_t with a dynamic coefficient.
         * Resize the coefficient to length 'nwords':
         *   Case nwords > result->alloc:
         *     If realloc is successful:
         *       'result' has a larger coefficient but the same value. Return 1.
         *     Otherwise:
         *       'result' has a the same coefficient. Return 0.
         *   Case nwords < result->alloc:
         *     If realloc is successful:
         *       'result' has a smaller coefficient. result->len is undefined. Return 1.
         *     Otherwise (unlikely):
         *       'result' is unchanged. Reuse the now oversized coefficient. Return 1.
         */
        public static bool mpd_realloc_dyn_cxx(mpd_t result, long nwords)
        {
            byte err = 0;
            var p = mpd_realloc(result.data, (ulong)nwords, ref err);
            if (err == 0)
            {
                result.data = p;
                result.alloc = nwords;
            }
            else if (nwords > result.alloc)
            {
                return false;
            }
            return true;
        }
    }
}
