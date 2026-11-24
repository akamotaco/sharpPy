using System;

namespace SharpPy.Modules.Lzma
{
    /// <summary>lzma_ret (api/lzma/base.h)</summary>
    internal enum LzmaRet
    {
        OK = 0, STREAM_END = 1, NO_CHECK = 2, UNSUPPORTED_CHECK = 3, GET_CHECK = 4, MEM_ERROR = 5,
        MEMLIMIT_ERROR = 6, FORMAT_ERROR = 7, OPTIONS_ERROR = 8, DATA_ERROR = 9, BUF_ERROR = 10, PROG_ERROR = 11,
        SEEK_NEEDED = 12,
    }

    /// <summary>lzma_action (api/lzma/base.h)</summary>
    internal enum LzmaAction { RUN = 0, SYNC_FLUSH = 1, FULL_FLUSH = 2, FINISH = 3, FULL_BARRIER = 4 }

    /// <summary>The constants of api/lzma/*.h and common/common.h that SharpPy's liblzma uses.</summary>
    internal static class LzmaConst
    {
        internal const int ACTION_MAX = 4;

        internal const ulong VLI_MAX = ulong.MaxValue / 2;
        internal const ulong VLI_UNKNOWN = ulong.MaxValue;
        internal const int VLI_BYTES_MAX = 9;

        internal const ulong FILTER_X86 = 0x04, FILTER_POWERPC = 0x05, FILTER_IA64 = 0x06, FILTER_ARM = 0x07,
            FILTER_ARMTHUMB = 0x08, FILTER_SPARC = 0x09, FILTER_ARM64 = 0x0A, FILTER_RISCV = 0x0B,
            FILTER_DELTA = 0x03, FILTER_LZMA1 = 0x4000000000000001, FILTER_LZMA1EXT = 0x4000000000000002,
            FILTER_LZMA2 = 0x21;
        internal const ulong FILTER_RESERVED_START = 1UL << 62;
        internal const int FILTERS_MAX = 4;

        internal const int CHECK_NONE = 0, CHECK_CRC32 = 1, CHECK_CRC64 = 4, CHECK_SHA256 = 10, CHECK_ID_MAX = 15;
        internal const int CHECK_BEST = CHECK_SHA256;

        internal const uint TELL_NO_CHECK = 0x01, TELL_UNSUPPORTED_CHECK = 0x02, TELL_ANY_CHECK = 0x04,
            IGNORE_CHECK = 0x10, CONCATENATED = 0x08, FAIL_FAST = 0x20;
        internal const uint SUPPORTED_FLAGS = TELL_NO_CHECK | TELL_UNSUPPORTED_CHECK | TELL_ANY_CHECK
            | IGNORE_CHECK | CONCATENATED | FAIL_FAST;

        internal const uint PRESET_DEFAULT = 6, PRESET_LEVEL_MASK = 0x1F, PRESET_EXTREME = 0x80000000;

        internal const uint DICT_SIZE_MIN = 4096;
        internal const uint LCLP_MAX = 4, PB_MAX = 4, LC_DEFAULT = 3, LP_DEFAULT = 0, PB_DEFAULT = 2;

        internal const uint DELTA_DIST_MIN = 1, DELTA_DIST_MAX = 256;

        internal const int STREAM_HEADER_SIZE = 12;
        internal const int BLOCK_HEADER_SIZE_MIN = 8, BLOCK_HEADER_SIZE_MAX = 1024;
        internal const ulong BACKWARD_SIZE_MIN = 4, BACKWARD_SIZE_MAX = 1UL << 34;

        internal const int BUFFER_SIZE = 4096;
        internal const ulong MEMUSAGE_BASE = 1UL << 15;

        internal const uint LZMA1EXT_ALLOW_EOPM = 0x01;
    }

    /// <summary>lzma_mode</summary>
    internal enum LzmaMode : uint { FAST = 1, NORMAL = 2 }

    /// <summary>lzma_match_finder</summary>
    internal enum LzmaMatchFinder : uint { HC3 = 0x03, HC4 = 0x04, BT2 = 0x12, BT3 = 0x13, BT4 = 0x14 }

    /// <summary>lzma_options_lzma</summary>
    internal sealed class LzmaOptionsLzma
    {
        internal uint dict_size;
        internal byte[] preset_dict;
        internal uint preset_dict_size;
        internal uint lc, lp, pb;
        internal LzmaMode mode;
        internal uint nice_len;
        internal LzmaMatchFinder mf;
        internal uint depth;
        internal uint ext_flags, ext_size_low, ext_size_high;

        internal LzmaOptionsLzma Clone() => (LzmaOptionsLzma)MemberwiseClone();
    }

    /// <summary>lzma_options_delta</summary>
    internal sealed class LzmaOptionsDelta
    {
        internal int type; // LZMA_DELTA_TYPE_BYTE = 0
        internal uint dist;
    }

    /// <summary>lzma_options_bcj</summary>
    internal sealed class LzmaOptionsBcj
    {
        internal uint start_offset;
    }

    /// <summary>lzma_filter</summary>
    internal struct LzmaFilter
    {
        internal ulong id;
        internal object options;

        internal LzmaFilter(ulong id, object options)
        {
            this.id = id;
            this.options = options;
        }
    }

    /// <summary>lzma_init_function</summary>
    internal delegate LzmaRet LzmaInitFunction(ref LzmaNextCoder next, LzmaFilterInfo[] filters, int index);

    /// <summary>lzma_filter_info</summary>
    internal struct LzmaFilterInfo
    {
        internal ulong id;
        internal LzmaInitFunction init;
        internal object options;
    }

    /// <summary>
    /// lzma_next_coder: one coder of a chain. code() takes the caller's buffers as arrays with
    /// absolute positions (in_pos .. in_size, out_pos .. out_size), as the C pointers do.
    /// </summary>
    internal abstract class LzmaNextCoder
    {
        /// <summary>The init function that made this coder (lzma_next_coder.init), for lzma_next_coder_init.</summary>
        internal object InitKey;

        internal abstract LzmaRet Code(byte[] input, ref int in_pos, int in_size,
            byte[] output, ref int out_pos, int out_size, LzmaAction action);

        /// <summary>get_check; -1 when the coder has none (NULL).</summary>
        internal virtual int GetCheck() => -1;

        /// <summary>end: let go of the coders behind this one and of the big buffers.</summary>
        internal virtual void End() { }

        internal virtual bool HasMemConfig => false;

        /// <summary>memconfig</summary>
        internal virtual LzmaRet MemConfig(out ulong memusage, out ulong old_memlimit, ulong new_memlimit)
        {
            memusage = 0;
            old_memlimit = 0;
            return LzmaRet.PROG_ERROR;
        }
    }

    /// <summary>
    /// lzma_stream with its lzma_internal: the caller's windows (next_in/avail_in, next_out/avail_out)
    /// and the coder chain with lzma_code()'s sequence checks.
    /// </summary>
    internal sealed class LzmaStream
    {
        internal byte[] next_in_buf = Array.Empty<byte>();
        internal int next_in;
        internal long avail_in;
        internal ulong total_in;
        internal byte[] next_out_buf = Array.Empty<byte>();
        internal int next_out;
        internal long avail_out;
        internal ulong total_out;

        // lzma_internal
        internal LzmaNextCoder next;
        internal int sequence;
        internal long internal_avail_in;
        internal readonly bool[] supported_actions = new bool[LzmaConst.ACTION_MAX + 1];
        internal bool allow_buf_error;
        internal bool initialized;
    }

    /// <summary>common/common.c and the helpers of common/common.h.</summary>
    internal static class LzmaCore
    {
        private const int ISEQ_RUN = 0, ISEQ_SYNC_FLUSH = 1, ISEQ_FULL_FLUSH = 2, ISEQ_FINISH = 3,
            ISEQ_FULL_BARRIER = 4, ISEQ_END = 5, ISEQ_ERROR = 6;

        /// <summary>lzma_bufcpy: copies what fits; returns the count.</summary>
        internal static int BufCpy(byte[] input, ref int in_pos, int in_size, byte[] output, ref int out_pos, int out_size)
        {
            int in_avail = in_size - in_pos;
            int out_avail = out_size - out_pos;
            int copy_size = Math.Min(in_avail, out_avail);
            if (copy_size > 0)
                Buffer.BlockCopy(input, in_pos, output, out_pos, copy_size);
            in_pos += copy_size;
            out_pos += copy_size;
            return copy_size;
        }

        /// <summary>lzma_next_end</summary>
        internal static void NextEnd(ref LzmaNextCoder next) => next = null;

        /// <summary>
        /// lzma_next_filter_init: initialize the coder of filters[index], or nothing at the end of
        /// the chain. A coder made by another init function is replaced (lzma_next_coder_init).
        /// </summary>
        internal static LzmaRet NextFilterInit(ref LzmaNextCoder next, LzmaFilterInfo[] filters, int index)
        {
            if (filters[index].init == null)
            {
                next = null;
                return LzmaRet.OK;
            }
            if (next != null && !ReferenceEquals(next.InitKey, filters[index].init))
                next = null;
            return filters[index].init(ref next, filters, index);
        }

        /// <summary>lzma_strm_init</summary>
        internal static LzmaRet StrmInit(LzmaStream strm)
        {
            if (strm == null)
                return LzmaRet.PROG_ERROR;
            Array.Clear(strm.supported_actions);
            strm.sequence = ISEQ_RUN;
            strm.allow_buf_error = false;
            strm.total_in = 0;
            strm.total_out = 0;
            strm.initialized = true;
            return LzmaRet.OK;
        }

        /// <summary>lzma_next_strm_init: lzma_strm_init, then the coder's init; lzma_end on failure.</summary>
        internal static LzmaRet NextStrmInit(LzmaStream strm, Func<LzmaNextCoder, (LzmaRet, LzmaNextCoder)> init)
        {
            var ret = StrmInit(strm);
            if (ret != LzmaRet.OK)
                return ret;
            (ret, strm.next) = init(strm.next);
            if (ret != LzmaRet.OK)
                End(strm);
            return ret;
        }

        /// <summary>lzma_code</summary>
        internal static LzmaRet Code(LzmaStream strm, LzmaAction action)
        {
            // Sanity checks
            if (!strm.initialized || strm.next == null || (uint)action > LzmaConst.ACTION_MAX
                    || !strm.supported_actions[(int)action])
                return LzmaRet.PROG_ERROR;

            switch (strm.sequence)
            {
                case ISEQ_RUN:
                    switch (action)
                    {
                        case LzmaAction.RUN: break;
                        case LzmaAction.SYNC_FLUSH: strm.sequence = ISEQ_SYNC_FLUSH; break;
                        case LzmaAction.FULL_FLUSH: strm.sequence = ISEQ_FULL_FLUSH; break;
                        case LzmaAction.FINISH: strm.sequence = ISEQ_FINISH; break;
                        case LzmaAction.FULL_BARRIER: strm.sequence = ISEQ_FULL_BARRIER; break;
                    }
                    break;
                case ISEQ_SYNC_FLUSH:
                    // The same action must be used until LZMA_STREAM_END, and the amount of input must not change.
                    if (action != LzmaAction.SYNC_FLUSH || strm.internal_avail_in != strm.avail_in)
                        return LzmaRet.PROG_ERROR;
                    break;
                case ISEQ_FULL_FLUSH:
                    if (action != LzmaAction.FULL_FLUSH || strm.internal_avail_in != strm.avail_in)
                        return LzmaRet.PROG_ERROR;
                    break;
                case ISEQ_FINISH:
                    if (action != LzmaAction.FINISH || strm.internal_avail_in != strm.avail_in)
                        return LzmaRet.PROG_ERROR;
                    break;
                case ISEQ_FULL_BARRIER:
                    if (action != LzmaAction.FULL_BARRIER || strm.internal_avail_in != strm.avail_in)
                        return LzmaRet.PROG_ERROR;
                    break;
                case ISEQ_END:
                    return LzmaRet.STREAM_END;
                default:
                    return LzmaRet.PROG_ERROR;
            }

            // The coders work with int positions: a window past 2 GiB is fed in pieces by the caller.
            int in_start = strm.next_in;
            int out_start = strm.next_out;
            int in_pos = in_start;
            int out_pos = out_start;
            int in_size = in_start + (int)Math.Min(strm.avail_in, int.MaxValue - in_start);
            int out_size = out_start + (int)Math.Min(strm.avail_out, int.MaxValue - out_start);
            var ret = strm.next.Code(strm.next_in_buf, ref in_pos, in_size,
                strm.next_out_buf, ref out_pos, out_size, action);

            int in_used = in_pos - in_start;
            int out_used = out_pos - out_start;
            strm.next_in += in_used;
            strm.avail_in -= in_used;
            strm.total_in += (ulong)in_used;
            strm.next_out += out_used;
            strm.avail_out -= out_used;
            strm.total_out += (ulong)out_used;

            strm.internal_avail_in = strm.avail_in;

            switch (ret)
            {
                case LzmaRet.OK:
                    // Don't return LZMA_BUF_ERROR when it happens the first time.
                    if (out_used == 0 && in_used == 0)
                    {
                        if (strm.allow_buf_error)
                            ret = LzmaRet.BUF_ERROR;
                        else
                            strm.allow_buf_error = true;
                    }
                    else
                    {
                        strm.allow_buf_error = false;
                    }
                    break;
                case LzmaRet.STREAM_END:
                    if (strm.sequence == ISEQ_SYNC_FLUSH || strm.sequence == ISEQ_FULL_FLUSH
                            || strm.sequence == ISEQ_FULL_BARRIER)
                        strm.sequence = ISEQ_RUN;
                    else
                        strm.sequence = ISEQ_END;
                    strm.allow_buf_error = false;
                    break;
                case LzmaRet.NO_CHECK:
                case LzmaRet.UNSUPPORTED_CHECK:
                case LzmaRet.GET_CHECK:
                case LzmaRet.MEMLIMIT_ERROR:
                    // Something else than LZMA_OK, but not a fatal error.
                    strm.allow_buf_error = false;
                    break;
                default:
                    // All the other errors are fatal; coding cannot be continued.
                    strm.sequence = ISEQ_ERROR;
                    break;
            }
            return ret;
        }

        /// <summary>lzma_end</summary>
        internal static void End(LzmaStream strm)
        {
            if (strm != null)
            {
                strm.next?.End();
                strm.next = null;
                strm.initialized = false;
            }
        }

        /// <summary>lzma_get_check</summary>
        internal static int GetCheck(LzmaStream strm)
        {
            // LZMA_CHECK_NONE if we cannot know the check type.
            int check = strm.next?.GetCheck() ?? -1;
            return check < 0 ? LzmaConst.CHECK_NONE : check;
        }

        /// <summary>lzma_memusage</summary>
        internal static ulong MemUsage(LzmaStream strm)
        {
            if (strm?.next == null || !strm.next.HasMemConfig
                    || strm.next.MemConfig(out ulong memusage, out _, 0) != LzmaRet.OK)
                return 0;
            return memusage;
        }

        // ------------------------------------------------------------ vli

        /// <summary>lzma_vli_size</summary>
        internal static uint VliSize(ulong vli)
        {
            if (vli > LzmaConst.VLI_MAX)
                return 0;
            uint i = 0;
            do
            {
                vli >>= 7;
                ++i;
            } while (vli != 0);
            return i;
        }

        /// <summary>lzma_vli_is_valid</summary>
        internal static bool VliIsValid(ulong vli) => vli <= LzmaConst.VLI_MAX || vli == LzmaConst.VLI_UNKNOWN;

        /// <summary>lzma_vli_encode in single-call mode (vli_pos == NULL).</summary>
        internal static LzmaRet VliEncode(ulong vli, byte[] output, ref int out_pos, int out_size)
        {
            int vli_pos_internal = 0;
            return VliEncode(vli, ref vli_pos_internal, true, output, ref out_pos, out_size);
        }

        /// <summary>lzma_vli_encode</summary>
        internal static LzmaRet VliEncode(ulong vli, ref int vli_pos, bool single, byte[] output, ref int out_pos, int out_size)
        {
            if (single)
            {
                if (out_pos >= out_size)
                    return LzmaRet.PROG_ERROR;
            }
            else if (out_pos >= out_size)
            {
                return LzmaRet.BUF_ERROR;
            }

            if (vli_pos >= LzmaConst.VLI_BYTES_MAX || vli > LzmaConst.VLI_MAX)
                return LzmaRet.PROG_ERROR;

            vli >>= vli_pos * 7;

            while (vli >= 0x80)
            {
                ++vli_pos;
                output[out_pos] = (byte)((byte)vli | 0x80);
                vli >>= 7;
                if (++out_pos == out_size)
                    return single ? LzmaRet.PROG_ERROR : LzmaRet.OK;
            }

            output[out_pos] = (byte)vli;
            ++out_pos;
            ++vli_pos;
            return single ? LzmaRet.OK : LzmaRet.STREAM_END;
        }

        /// <summary>lzma_vli_decode in single-call mode (vli_pos == NULL).</summary>
        internal static LzmaRet VliDecode(out ulong vli, byte[] input, ref int in_pos, int in_size)
        {
            vli = 0;
            int vli_pos_internal = 0;
            return VliDecode(ref vli, ref vli_pos_internal, true, input, ref in_pos, in_size);
        }

        /// <summary>lzma_vli_decode</summary>
        internal static LzmaRet VliDecode(ref ulong vli, ref int vli_pos, bool single, byte[] input, ref int in_pos, int in_size)
        {
            if (single)
            {
                vli = 0;
                if (in_pos >= in_size)
                    return LzmaRet.DATA_ERROR;
            }
            else
            {
                if (vli_pos == 0)
                    vli = 0;
                if (vli_pos >= LzmaConst.VLI_BYTES_MAX || (vli >> (vli_pos * 7)) != 0)
                    return LzmaRet.PROG_ERROR;
                if (in_pos >= in_size)
                    return LzmaRet.BUF_ERROR;
            }

            do
            {
                byte b = input[in_pos];
                ++in_pos;
                vli += (ulong)(b & 0x7F) << (vli_pos * 7);
                ++vli_pos;

                if ((b & 0x80) == 0)
                {
                    // Don't allow superfluous leading zeros.
                    if (b == 0x00 && vli_pos > 1)
                        return LzmaRet.DATA_ERROR;
                    return single ? LzmaRet.OK : LzmaRet.STREAM_END;
                }

                if (vli_pos == LzmaConst.VLI_BYTES_MAX)
                    return LzmaRet.DATA_ERROR;
            } while (in_pos < in_size);

            return single ? LzmaRet.DATA_ERROR : LzmaRet.OK;
        }

        internal static uint Read32LE(byte[] b, int at) =>
            (uint)(b[at] | (b[at + 1] << 8) | (b[at + 2] << 16) | (b[at + 3] << 24));

        internal static void Write32LE(byte[] b, int at, uint v)
        {
            b[at] = (byte)v;
            b[at + 1] = (byte)(v >> 8);
            b[at + 2] = (byte)(v >> 16);
            b[at + 3] = (byte)(v >> 24);
        }
    }
}
