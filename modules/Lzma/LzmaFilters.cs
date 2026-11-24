using System;

namespace SharpPy.Modules.Lzma
{
    /// <summary>
    /// common/filter_common.c, filter_encoder.c, filter_decoder.c, filter_flags_encoder.c and
    /// filter_flags_decoder.c: the filter tables, chain validation, raw coders and the properties.
    /// </summary>
    internal static class LzmaFilters
    {
        // filter_common.c features[]
        private readonly struct Feature
        {
            internal readonly ulong id;
            internal readonly bool non_last_ok, last_ok, changes_size;

            internal Feature(ulong id, bool non_last_ok, bool last_ok, bool changes_size)
            {
                this.id = id;
                this.non_last_ok = non_last_ok;
                this.last_ok = last_ok;
                this.changes_size = changes_size;
            }
        }

        private static readonly Feature[] features =
        {
            new(LzmaConst.FILTER_LZMA1, false, true, true),
            new(LzmaConst.FILTER_LZMA1EXT, false, true, true),
            new(LzmaConst.FILTER_LZMA2, false, true, true),
            new(LzmaConst.FILTER_X86, true, false, false),
            new(LzmaConst.FILTER_POWERPC, true, false, false),
            new(LzmaConst.FILTER_IA64, true, false, false),
            new(LzmaConst.FILTER_ARM, true, false, false),
            new(LzmaConst.FILTER_ARMTHUMB, true, false, false),
            new(LzmaConst.FILTER_SPARC, true, false, false),
            new(LzmaConst.FILTER_DELTA, true, false, false),
        };

        // lzma_filter_coder / lzma_filter_encoder / lzma_filter_decoder
        internal sealed class FilterCoder
        {
            internal ulong id;
            internal LzmaInitFunction init;
            internal Func<object, ulong> memusage;
            // encoder
            internal Func<object, uint> props_size_get;
            internal uint props_size_fixed;
            internal Func<object, byte[], int, LzmaRet> props_encode;
            // decoder
            internal PropsDecodeFunction props_decode;
        }

        internal delegate LzmaRet PropsDecodeFunction(out object options, byte[] props, int at, int size);

        // filter_encoder.c encoders[]
        private static readonly FilterCoder[] encoders =
        {
            new() { id = LzmaConst.FILTER_LZMA1, init = LzmaLzma1Encoder.EncoderInit, memusage = LzmaLzma1Encoder.MemUsage,
                props_size_fixed = 5, props_encode = LzmaLzma1Encoder.PropsEncode },
            new() { id = LzmaConst.FILTER_LZMA1EXT, init = LzmaLzma1Encoder.EncoderInit, memusage = LzmaLzma1Encoder.MemUsage,
                props_size_fixed = 5, props_encode = LzmaLzma1Encoder.PropsEncode },
            new() { id = LzmaConst.FILTER_LZMA2, init = LzmaLzma2Encoder.EncoderInit, memusage = LzmaLzma2Encoder.MemUsage,
                props_size_fixed = 1, props_encode = LzmaLzma2Encoder.PropsEncode },
            new() { id = LzmaConst.FILTER_X86, init = LzmaSimple.X86EncoderInit, props_size_get = LzmaSimple.PropsSize, props_encode = LzmaSimple.PropsEncode },
            new() { id = LzmaConst.FILTER_POWERPC, init = LzmaSimple.PowerPcEncoderInit, props_size_get = LzmaSimple.PropsSize, props_encode = LzmaSimple.PropsEncode },
            new() { id = LzmaConst.FILTER_IA64, init = LzmaSimple.Ia64EncoderInit, props_size_get = LzmaSimple.PropsSize, props_encode = LzmaSimple.PropsEncode },
            new() { id = LzmaConst.FILTER_ARM, init = LzmaSimple.ArmEncoderInit, props_size_get = LzmaSimple.PropsSize, props_encode = LzmaSimple.PropsEncode },
            new() { id = LzmaConst.FILTER_ARMTHUMB, init = LzmaSimple.ArmThumbEncoderInit, props_size_get = LzmaSimple.PropsSize, props_encode = LzmaSimple.PropsEncode },
            new() { id = LzmaConst.FILTER_SPARC, init = LzmaSimple.SparcEncoderInit, props_size_get = LzmaSimple.PropsSize, props_encode = LzmaSimple.PropsEncode },
            new() { id = LzmaConst.FILTER_DELTA, init = LzmaDelta.EncoderInit, memusage = LzmaDelta.CoderMemUsage,
                props_size_fixed = 1, props_encode = LzmaDelta.PropsEncode },
        };

        // filter_decoder.c decoders[]
        private static readonly FilterCoder[] decoders =
        {
            new() { id = LzmaConst.FILTER_LZMA1, init = LzmaLzma1Decoder.DecoderInit, memusage = LzmaLzma1Decoder.MemUsage,
                props_decode = LzmaLzma1Decoder.PropsDecode },
            new() { id = LzmaConst.FILTER_LZMA1EXT, init = LzmaLzma1Decoder.DecoderInit, memusage = LzmaLzma1Decoder.MemUsage,
                props_decode = LzmaLzma1Decoder.PropsDecode },
            new() { id = LzmaConst.FILTER_LZMA2, init = LzmaLzma2Decoder.DecoderInit, memusage = LzmaLzma2Decoder.MemUsage,
                props_decode = LzmaLzma2Decoder.PropsDecode },
            new() { id = LzmaConst.FILTER_X86, init = LzmaSimple.X86DecoderInit, props_decode = LzmaSimple.PropsDecode },
            new() { id = LzmaConst.FILTER_POWERPC, init = LzmaSimple.PowerPcDecoderInit, props_decode = LzmaSimple.PropsDecode },
            new() { id = LzmaConst.FILTER_IA64, init = LzmaSimple.Ia64DecoderInit, props_decode = LzmaSimple.PropsDecode },
            new() { id = LzmaConst.FILTER_ARM, init = LzmaSimple.ArmDecoderInit, props_decode = LzmaSimple.PropsDecode },
            new() { id = LzmaConst.FILTER_ARMTHUMB, init = LzmaSimple.ArmThumbDecoderInit, props_decode = LzmaSimple.PropsDecode },
            new() { id = LzmaConst.FILTER_SPARC, init = LzmaSimple.SparcDecoderInit, props_decode = LzmaSimple.PropsDecode },
            new() { id = LzmaConst.FILTER_DELTA, init = LzmaDelta.DecoderInit, memusage = LzmaDelta.CoderMemUsage,
                props_decode = LzmaDelta.PropsDecode },
        };

        /// <summary>encoder_find</summary>
        internal static FilterCoder EncoderFind(ulong id) => Array.Find(encoders, e => e.id == id);

        /// <summary>decoder_find</summary>
        internal static FilterCoder DecoderFind(ulong id) => Array.Find(decoders, e => e.id == id);

        /// <summary>The filter count of a chain: the entries before the LZMA_VLI_UNKNOWN terminator.</summary>
        internal static int Count(LzmaFilter[] filters)
        {
            int i = 0;
            while (i < filters.Length && filters[i].id != LzmaConst.VLI_UNKNOWN)
                i++;
            return i;
        }

        /// <summary>lzma_validate_chain</summary>
        internal static LzmaRet ValidateChain(LzmaFilter[] filters, out int count)
        {
            count = 0;
            // There must be at least one filter.
            if (filters == null || Count(filters) == 0)
                return LzmaRet.PROG_ERROR;

            // Number of non-last filters that may change the size of the data significantly.
            int changes_size_count = 0;

            // True if it is OK to add a new filter after the current filter.
            bool non_last_ok = true;

            // True if the last filter in the given chain is actually usable as the last filter.
            bool last_ok = false;

            int i = 0;
            int n = Count(filters);
            do
            {
                int j;
                for (j = 0; j < features.Length && filters[i].id != features[j].id; ++j) { }
                if (j == features.Length)
                    return LzmaRet.OPTIONS_ERROR;

                // If the previous filter in the chain cannot be a non-last filter, the chain is invalid.
                if (!non_last_ok)
                    return LzmaRet.OPTIONS_ERROR;

                non_last_ok = features[j].non_last_ok;
                last_ok = features[j].last_ok;
                changes_size_count += features[j].changes_size ? 1 : 0;
            } while (++i < n);

            // There must be 1-4 filters. The last filter must be usable as the last filter in the chain. A maximum of three filters are allowed to change the size of the data.
            if (i > LzmaConst.FILTERS_MAX || !last_ok || changes_size_count > 3)
                return LzmaRet.OPTIONS_ERROR;

            count = i;
            return LzmaRet.OK;
        }

        /// <summary>lzma_raw_coder_init</summary>
        internal static LzmaRet RawCoderInit(ref LzmaNextCoder next, LzmaFilter[] options, bool is_encoder)
        {
            // Do some basic validation and get the number of filters.
            var ret = ValidateChain(options, out int count);
            if (ret != LzmaRet.OK)
                return ret;

            // Set the filter functions and copy the options pointer.
            var filters = new LzmaFilterInfo[LzmaConst.FILTERS_MAX + 1];
            if (is_encoder)
            {
                for (int i = 0; i < count; ++i)
                {
                    // The order of the filters is reversed in the encoder.
                    int j = count - i - 1;

                    var fc = EncoderFind(options[i].id);
                    if (fc == null || fc.init == null)
                        return LzmaRet.OPTIONS_ERROR;

                    filters[j].id = options[i].id;
                    filters[j].init = fc.init;
                    filters[j].options = options[i].options;
                }
            }
            else
            {
                for (int i = 0; i < count; ++i)
                {
                    var fc = DecoderFind(options[i].id);
                    if (fc == null || fc.init == null)
                        return LzmaRet.OPTIONS_ERROR;

                    filters[i].id = options[i].id;
                    filters[i].init = fc.init;
                    filters[i].options = options[i].options;
                }
            }

            // Terminate the array.
            filters[count].id = LzmaConst.VLI_UNKNOWN;
            filters[count].init = null;

            // Initialize the filters.
            ret = LzmaCore.NextFilterInit(ref next, filters, 0);
            if (ret != LzmaRet.OK)
                next = null;

            return ret;
        }

        /// <summary>lzma_raw_coder_memusage</summary>
        internal static ulong RawCoderMemUsage(bool is_encoder, LzmaFilter[] filters)
        {
            // The chain has to have at least one filter.
            if (ValidateChain(filters, out int n) != LzmaRet.OK)
                return ulong.MaxValue;

            ulong total = 0;
            int i = 0;

            do
            {
                var fc = is_encoder ? EncoderFind(filters[i].id) : DecoderFind(filters[i].id);
                if (fc == null)
                    return ulong.MaxValue; // Unsupported Filter ID

                if (fc.memusage == null)
                {
                    // This filter doesn't have a function to calculate the memory usage and validate the options. Such filters need only little memory, so we use 1 KiB as a good estimate.
                    total += 1024;
                }
                else
                {
                    // Call the filter-specific memory usage calculation function.
                    ulong usage = fc.memusage(filters[i].options);
                    if (usage == ulong.MaxValue)
                        return ulong.MaxValue; // Invalid options

                    total += usage;
                }
            } while (++i < n);

            // Add some fixed amount of extra. It's to compensate memory usage of Stream, Block etc. coders, malloc() overhead, stack etc.
            return total + LzmaConst.MEMUSAGE_BASE;
        }

        /// <summary>lzma_raw_encoder_init</summary>
        internal static LzmaRet RawEncoderInit(ref LzmaNextCoder next, LzmaFilter[] filters) =>
            RawCoderInit(ref next, filters, true);

        /// <summary>lzma_raw_decoder_init</summary>
        internal static LzmaRet RawDecoderInit(ref LzmaNextCoder next, LzmaFilter[] filters) =>
            RawCoderInit(ref next, filters, false);

        /// <summary>lzma_raw_encoder</summary>
        internal static LzmaRet RawEncoder(LzmaStream strm, LzmaFilter[] filters)
        {
            var ret = LzmaCore.NextStrmInit(strm, next =>
            {
                var r = RawCoderInit(ref next, filters, true);
                return (r, next);
            });
            if (ret != LzmaRet.OK)
                return ret;
            strm.supported_actions[(int)LzmaAction.RUN] = true;
            strm.supported_actions[(int)LzmaAction.SYNC_FLUSH] = true;
            strm.supported_actions[(int)LzmaAction.FINISH] = true;
            return LzmaRet.OK;
        }

        /// <summary>lzma_raw_decoder</summary>
        internal static LzmaRet RawDecoder(LzmaStream strm, LzmaFilter[] filters)
        {
            var ret = LzmaCore.NextStrmInit(strm, next =>
            {
                var r = RawCoderInit(ref next, filters, false);
                return (r, next);
            });
            if (ret != LzmaRet.OK)
                return ret;
            strm.supported_actions[(int)LzmaAction.RUN] = true;
            strm.supported_actions[(int)LzmaAction.FINISH] = true;
            return LzmaRet.OK;
        }

        /// <summary>lzma_raw_decoder_memusage</summary>
        internal static ulong RawDecoderMemUsage(LzmaFilter[] filters) => RawCoderMemUsage(false, filters);

        /// <summary>lzma_properties_size</summary>
        internal static LzmaRet PropertiesSize(out uint size, LzmaFilter filter)
        {
            size = 0;
            var fe = EncoderFind(filter.id);
            if (fe == null)
            {
                // Unknown filter - if the Filter ID is a proper VLI, return LZMA_OPTIONS_ERROR instead of LZMA_PROG_ERROR.
                return filter.id <= LzmaConst.VLI_MAX ? LzmaRet.OPTIONS_ERROR : LzmaRet.PROG_ERROR;
            }

            if (fe.props_size_get == null)
            {
                // No props_size_get() function, use props_size_fixed.
                size = fe.props_size_fixed;
                return LzmaRet.OK;
            }

            size = fe.props_size_get(filter.options);
            return LzmaRet.OK;
        }

        /// <summary>lzma_properties_encode</summary>
        internal static LzmaRet PropertiesEncode(LzmaFilter filter, byte[] props, int at)
        {
            var fe = EncoderFind(filter.id);
            if (fe == null)
                return LzmaRet.PROG_ERROR;

            if (fe.props_encode == null)
                return LzmaRet.OK;

            return fe.props_encode(filter.options, props, at);
        }

        /// <summary>lzma_properties_decode</summary>
        internal static LzmaRet PropertiesDecode(ref LzmaFilter filter, byte[] props, int at, int props_size)
        {
            // Make it always NULL so that the caller can always safely free it.
            filter.options = null;

            var fd = DecoderFind(filter.id);
            if (fd == null)
                return LzmaRet.OPTIONS_ERROR;

            if (fd.props_decode == null)
                return props_size == 0 ? LzmaRet.OK : LzmaRet.OPTIONS_ERROR;

            var ret = fd.props_decode(out object options, props, at, props_size);
            filter.options = options;
            return ret;
        }

        /// <summary>lzma_filter_flags_size</summary>
        internal static LzmaRet FilterFlagsSize(out uint size, LzmaFilter filter)
        {
            size = 0;
            if (filter.id >= LzmaConst.FILTER_RESERVED_START)
                return LzmaRet.PROG_ERROR;

            var ret = PropertiesSize(out size, filter);
            if (ret != LzmaRet.OK)
                return ret;

            size += LzmaCore.VliSize(filter.id) + LzmaCore.VliSize(size);

            return LzmaRet.OK;
        }

        /// <summary>lzma_filter_flags_encode</summary>
        internal static LzmaRet FilterFlagsEncode(LzmaFilter filter, byte[] output, ref int out_pos, int out_size)
        {
            // Filter ID
            if (filter.id >= LzmaConst.FILTER_RESERVED_START)
                return LzmaRet.PROG_ERROR;

            var ret = LzmaCore.VliEncode(filter.id, output, ref out_pos, out_size);
            if (ret != LzmaRet.OK)
                return ret;

            // Size of Properties
            ret = PropertiesSize(out uint props_size, filter);
            if (ret != LzmaRet.OK)
                return ret;

            ret = LzmaCore.VliEncode(props_size, output, ref out_pos, out_size);
            if (ret != LzmaRet.OK)
                return ret;

            // Filter Properties
            if (out_size - out_pos < props_size)
                return LzmaRet.PROG_ERROR;

            ret = PropertiesEncode(filter, output, out_pos);
            if (ret != LzmaRet.OK)
                return ret;

            out_pos += (int)props_size;

            return LzmaRet.OK;
        }

        /// <summary>lzma_filter_flags_decode</summary>
        internal static LzmaRet FilterFlagsDecode(ref LzmaFilter filter, byte[] input, ref int in_pos, int in_size)
        {
            // Set the pointer to NULL so the caller can always safely free it.
            filter.options = null;

            // Filter ID
            var ret = LzmaCore.VliDecode(out filter.id, input, ref in_pos, in_size);
            if (ret != LzmaRet.OK)
                return ret;

            if (filter.id >= LzmaConst.FILTER_RESERVED_START)
                return LzmaRet.DATA_ERROR;

            // Size of Properties
            ret = LzmaCore.VliDecode(out ulong props_size, input, ref in_pos, in_size);
            if (ret != LzmaRet.OK)
                return ret;

            // Filter Properties
            if ((ulong)(in_size - in_pos) < props_size)
                return LzmaRet.DATA_ERROR;

            ret = PropertiesDecode(ref filter, input, in_pos, (int)props_size);

            in_pos += (int)props_size;

            return ret;
        }
    }

    /// <summary>delta/delta_common.c, delta_encoder.c and delta_decoder.c</summary>
    internal sealed class LzmaDelta : LzmaNextCoder
    {
        // Next coder in the chain
        private LzmaNextCoder next;

        /// <summary>lzma_next_end of the coder behind this one.</summary>
        internal override void End()
        {
            next?.End();
            next = null;
        }

        // Delta distance
        private int distance;

        // Position in history[]
        private byte pos;

        // Buffer to hold history of the original data
        private readonly byte[] history = new byte[LzmaConst.DELTA_DIST_MAX];

        private bool is_encoder;

        /// <summary>lzma_delta_coder_init</summary>
        private static LzmaRet CoderInit(ref LzmaNextCoder next, LzmaFilterInfo[] filters, int index, bool is_encoder)
        {
            // Allocate memory for the decoder if needed.
            if (next is not LzmaDelta coder)
            {
                coder = new LzmaDelta();
                next = coder;
            }
            coder.InitKey = filters[index].init;
            coder.is_encoder = is_encoder;

            // Validate the options.
            if (CoderMemUsage(filters[index].options) == ulong.MaxValue)
                return LzmaRet.OPTIONS_ERROR;

            // Set the delta distance.
            var opt = (LzmaOptionsDelta)filters[index].options;
            coder.distance = (int)opt.dist;

            // Initialize the rest of the variables.
            coder.pos = 0;
            Array.Clear(coder.history);

            // Initialize the next decoder in the chain, if any.
            return LzmaCore.NextFilterInit(ref coder.next, filters, index + 1);
        }

        /// <summary>lzma_delta_coder_memusage</summary>
        internal static ulong CoderMemUsage(object options)
        {
            if (options is not LzmaOptionsDelta opt || opt.type != 0
                    || opt.dist < LzmaConst.DELTA_DIST_MIN || opt.dist > LzmaConst.DELTA_DIST_MAX)
                return ulong.MaxValue;

            // sizeof(lzma_delta_coder)
            return 320;
        }

        /// <summary>copy_and_encode</summary>
        private void CopyAndEncode(byte[] input, int in_at, byte[] output, int out_at, int size)
        {
            for (int i = 0; i < size; ++i)
            {
                byte tmp = history[(distance + pos) & 0xFF];
                history[pos-- & 0xFF] = input[in_at + i];
                output[out_at + i] = (byte)(input[in_at + i] - tmp);
            }
        }

        /// <summary>encode_in_place</summary>
        private void EncodeInPlace(byte[] buffer, int at, int size)
        {
            for (int i = 0; i < size; ++i)
            {
                byte tmp = history[(distance + pos) & 0xFF];
                history[pos-- & 0xFF] = buffer[at + i];
                buffer[at + i] -= tmp;
            }
        }

        /// <summary>decode_buffer</summary>
        private void DecodeBuffer(byte[] buffer, int at, int size)
        {
            for (int i = 0; i < size; ++i)
            {
                buffer[at + i] += history[(distance + pos) & 0xFF];
                history[pos-- & 0xFF] = buffer[at + i];
            }
        }

        /// <summary>delta_encode / delta_decode</summary>
        internal override LzmaRet Code(byte[] input, ref int in_pos, int in_size,
            byte[] output, ref int out_pos, int out_size, LzmaAction action)
        {
            LzmaRet ret;
            if (is_encoder)
            {
                if (next == null)
                {
                    int in_avail = in_size - in_pos;
                    int out_avail = out_size - out_pos;
                    int size = Math.Min(in_avail, out_avail);

                    if (size > 0)
                        CopyAndEncode(input, in_pos, output, out_pos, size);

                    in_pos += size;
                    out_pos += size;

                    ret = action != LzmaAction.RUN && in_pos == in_size ? LzmaRet.STREAM_END : LzmaRet.OK;
                }
                else
                {
                    int out_start = out_pos;

                    ret = next.Code(input, ref in_pos, in_size, output, ref out_pos, out_size, action);

                    // Like above, avoid null pointer + 0 even though it should never happen.
                    int size = out_pos - out_start;
                    if (size > 0)
                        EncodeInPlace(output, out_start, size);
                }
                return ret;
            }

            {
                int out_start = out_pos;

                ret = next.Code(input, ref in_pos, in_size, output, ref out_pos, out_size, action);

                int size = out_pos - out_start;
                if (size > 0)
                    DecodeBuffer(output, out_start, size);

                return ret;
            }
        }

        /// <summary>lzma_delta_encoder_init</summary>
        internal static LzmaRet EncoderInit(ref LzmaNextCoder next, LzmaFilterInfo[] filters, int index) =>
            CoderInit(ref next, filters, index, true);

        /// <summary>lzma_delta_decoder_init</summary>
        internal static LzmaRet DecoderInit(ref LzmaNextCoder next, LzmaFilterInfo[] filters, int index) =>
            CoderInit(ref next, filters, index, false);

        /// <summary>lzma_delta_props_encode</summary>
        internal static LzmaRet PropsEncode(object options, byte[] output, int at)
        {
            // The caller must have already validated the options, so it's LZMA_PROG_ERROR if they are invalid.
            if (CoderMemUsage(options) == ulong.MaxValue)
                return LzmaRet.PROG_ERROR;

            var opt = (LzmaOptionsDelta)options;
            output[at] = (byte)(opt.dist - LzmaConst.DELTA_DIST_MIN);

            return LzmaRet.OK;
        }

        /// <summary>lzma_delta_props_decode</summary>
        internal static LzmaRet PropsDecode(out object options, byte[] props, int at, int props_size)
        {
            options = null;
            if (props_size != 1)
                return LzmaRet.OPTIONS_ERROR;

            options = new LzmaOptionsDelta { type = 0, dist = props[at] + 1U };
            return LzmaRet.OK;
        }
    }

    /// <summary>
    /// simple/simple_coder.c with the BCJ filters of simple/x86.c, powerpc.c, ia64.c, arm.c,
    /// armthumb.c and sparc.c, and simple_encoder.c / simple_decoder.c's properties.
    /// </summary>
    internal sealed class LzmaSimple : LzmaNextCoder
    {
        private delegate int FilterFunction(LzmaSimple simple, uint now_pos, bool is_encoder, byte[] buffer, int at, int size);

        // Next filter in the chain
        private LzmaNextCoder next;

        /// <summary>lzma_next_end of the coder behind this one.</summary>
        internal override void End()
        {
            next?.End();
            next = null;
        }

        // True if the next coder in the chain has returned LZMA_STREAM_END.
        private bool end_was_reached;

        // True if filter() should encode the data; false to decode.
        private bool is_encoder;

        // Pointer to filter-specific function, which does the actual filtering.
        private FilterFunction filter;

        // lzma_simple_x86's state (the only filter with one)
        private uint prev_mask, prev_pos;

        // The lowest 32 bits of the current position in the data.
        private uint now_pos;

        // Size of the memory allocated for the buffer.
        private int allocated;

        // Flushing position in the temporary buffer.
        private int pos;

        // buffer[filtered] is the first unfiltered byte.
        private int filtered;

        // Total number of bytes (both filtered and unfiltered) currently in the temporary buffer.
        private int size;

        // Temporary buffer
        private byte[] buffer;

        /// <summary>copy_or_code</summary>
        private LzmaRet CopyOrCode(byte[] input, ref int in_pos, int in_size,
            byte[] output, ref int out_pos, int out_size, LzmaAction action)
        {
            if (next == null)
            {
                LzmaCore.BufCpy(input, ref in_pos, in_size, output, ref out_pos, out_size);

                // Check if end of stream was reached.
                if (is_encoder && action == LzmaAction.FINISH && in_pos == in_size)
                    end_was_reached = true;
            }
            else
            {
                // Call the next coder in the chain to provide us some data.
                var ret = next.Code(input, ref in_pos, in_size, output, ref out_pos, out_size, action);

                if (ret == LzmaRet.STREAM_END)
                    end_was_reached = true;
                else if (ret != LzmaRet.OK)
                    return ret;
            }

            return LzmaRet.OK;
        }

        /// <summary>call_filter</summary>
        private int CallFilter(byte[] buf, int at, int size)
        {
            int filtered = filter(this, now_pos, is_encoder, buf, at, size);
            now_pos += (uint)filtered;
            return filtered;
        }

        /// <summary>simple_code</summary>
        internal override LzmaRet Code(byte[] input, ref int in_pos, int in_size,
            byte[] output, ref int out_pos, int out_size, LzmaAction action)
        {
            // TODO: Add partial support for LZMA_SYNC_FLUSH.
            if (action == LzmaAction.SYNC_FLUSH)
                return LzmaRet.OPTIONS_ERROR;

            // Flush already filtered data from coder->buffer[] to out[].
            if (pos < filtered)
            {
                LzmaCore.BufCpy(buffer, ref pos, filtered, output, ref out_pos, out_size);

                // If we couldn't flush all the filtered data, return to application immediately.
                if (pos < filtered)
                    return LzmaRet.OK;

                if (end_was_reached)
                    return LzmaRet.STREAM_END;
            }

            // If we get here, there is no filtered data left in the buffer.
            filtered = 0;

            // If there is more output space left than there is unfiltered data in coder->buffer[], flush coder->buffer[] to out[], and copy/code new data to out[].
            int out_avail = out_size - out_pos;
            int buf_avail = size - pos;
            if (out_avail > buf_avail || buf_avail == 0)
            {
                int out_start = out_pos;

                // Flush data from coder->buffer[] to out[], but don't reset coder->pos and coder->size yet.
                if (buf_avail > 0)
                    Buffer.BlockCopy(buffer, pos, output, out_pos, buf_avail);

                out_pos += buf_avail;

                // Copy/Encode/Decode more data to out[].
                {
                    var ret = CopyOrCode(input, ref in_pos, in_size, output, ref out_pos, out_size, action);
                    if (ret != LzmaRet.OK)
                        return ret;
                }

                // Filter out[] unless there is nothing to filter.
                int sz = out_pos - out_start;
                int flt = sz == 0 ? 0 : CallFilter(output, out_start, sz);

                int unfiltered = sz - flt;

                // Now we can update coder->pos and coder->size, because the remaining unfiltered data is going to be copied from out[] back to coder->buffer[].
                pos = 0;
                size = unfiltered;

                if (end_was_reached)
                {
                    // The last few bytes can be left unfiltered, so we ignore that unfiltered data.
                    size = 0;
                }
                else if (unfiltered > 0)
                {
                    // There is unfiltered data left in out[]. Copy it to coder->buffer[] and rewind *out_pos appropriately.
                    out_pos -= unfiltered;
                    Buffer.BlockCopy(output, out_pos, buffer, 0, unfiltered);
                }
            }
            else if (pos > 0)
            {
                Buffer.BlockCopy(buffer, pos, buffer, 0, buf_avail);
                size -= pos;
                pos = 0;
            }

            if (size > 0)
            {
                // Try to fill the coder->buffer[].
                {
                    var ret = CopyOrCode(input, ref in_pos, in_size, buffer, ref size, allocated, action);
                    if (ret != LzmaRet.OK)
                        return ret;
                }

                filtered = CallFilter(buffer, 0, size);

                // Everything is considered to be filtered if coder->buffer[] contains the last bytes of the data.
                if (end_was_reached)
                    filtered = size;

                // Flush as much as possible.
                LzmaCore.BufCpy(buffer, ref pos, filtered, output, ref out_pos, out_size);
            }

            // Check if we got everything done.
            if (end_was_reached && pos == size)
                return LzmaRet.STREAM_END;

            return LzmaRet.OK;
        }

        /// <summary>lzma_simple_coder_init</summary>
        private static LzmaRet CoderInit(ref LzmaNextCoder next, LzmaFilterInfo[] filters, int index,
            FilterFunction filter, int unfiltered_max, uint alignment, bool is_encoder)
        {
            // Allocate memory for the lzma_simple_coder structure if needed.
            if (next is not LzmaSimple coder || !ReferenceEquals(coder.filter, filter))
            {
                coder = new LzmaSimple();
                next = coder;
                coder.filter = filter;
                coder.allocated = 2 * unfiltered_max;
                coder.buffer = new byte[coder.allocated];
            }
            coder.InitKey = filters[index].init;

            if (filters[index].options is LzmaOptionsBcj simple)
            {
                coder.now_pos = simple.start_offset;
                if ((coder.now_pos & (alignment - 1)) != 0)
                    return LzmaRet.OPTIONS_ERROR;
            }
            else
            {
                coder.now_pos = 0;
            }

            // Reset variables.
            coder.is_encoder = is_encoder;
            coder.end_was_reached = false;
            coder.pos = 0;
            coder.filtered = 0;
            coder.size = 0;

            return LzmaCore.NextFilterInit(ref coder.next, filters, index + 1);
        }

        // ------------------------------------------------------------ x86.c

        private static bool Test86MSByte(byte b) => b == 0 || b == 0xFF;

        private static readonly uint[] MASK_TO_BIT_NUMBER = { 0, 1, 2, 2, 3 };

        /// <summary>x86_code</summary>
        private static int X86Code(LzmaSimple simple, uint now_pos, bool is_encoder, byte[] buffer, int at, int size)
        {
            uint prev_mask = simple.prev_mask;
            uint prev_pos = simple.prev_pos;

            if (size < 5)
                return 0;

            if (now_pos - prev_pos > 5)
                prev_pos = now_pos - 5;

            int limit = size - 5;
            int buffer_pos = 0;

            while (buffer_pos <= limit)
            {
                byte b = buffer[at + buffer_pos];
                if (b != 0xE8 && b != 0xE9)
                {
                    ++buffer_pos;
                    continue;
                }

                uint offset = now_pos + (uint)buffer_pos - prev_pos;
                prev_pos = now_pos + (uint)buffer_pos;

                if (offset > 5)
                {
                    prev_mask = 0;
                }
                else
                {
                    for (uint i = 0; i < offset; ++i)
                    {
                        prev_mask &= 0x77;
                        prev_mask <<= 1;
                    }
                }

                b = buffer[at + buffer_pos + 4];

                if (Test86MSByte(b) && (prev_mask >> 1) <= 4 && (prev_mask >> 1) != 3)
                {
                    uint src = ((uint)b << 24)
                        | ((uint)buffer[at + buffer_pos + 3] << 16)
                        | ((uint)buffer[at + buffer_pos + 2] << 8)
                        | buffer[at + buffer_pos + 1];

                    uint dest;
                    while (true)
                    {
                        if (is_encoder)
                            dest = src + (now_pos + (uint)buffer_pos + 5);
                        else
                            dest = src - (now_pos + (uint)buffer_pos + 5);

                        if (prev_mask == 0)
                            break;

                        uint i = MASK_TO_BIT_NUMBER[prev_mask >> 1];

                        b = (byte)(dest >> (int)(24 - i * 8));

                        if (!Test86MSByte(b))
                            break;

                        src = dest ^ ((1U << (int)(32 - i * 8)) - 1);
                    }

                    buffer[at + buffer_pos + 4] = (byte)~(((dest >> 24) & 1) - 1);
                    buffer[at + buffer_pos + 3] = (byte)(dest >> 16);
                    buffer[at + buffer_pos + 2] = (byte)(dest >> 8);
                    buffer[at + buffer_pos + 1] = (byte)dest;
                    buffer_pos += 5;
                    prev_mask = 0;
                }
                else
                {
                    ++buffer_pos;
                    prev_mask |= 1;
                    if (Test86MSByte(b))
                        prev_mask |= 0x10;
                }
            }

            simple.prev_mask = prev_mask;
            simple.prev_pos = prev_pos;

            return buffer_pos;
        }

        /// <summary>x86_coder_init</summary>
        private static LzmaRet X86CoderInit(ref LzmaNextCoder next, LzmaFilterInfo[] filters, int index, bool is_encoder)
        {
            var ret = CoderInit(ref next, filters, index, X86Code, 5, 1, is_encoder);

            if (ret == LzmaRet.OK)
            {
                var coder = (LzmaSimple)next;
                coder.prev_mask = 0;
                coder.prev_pos = unchecked((uint)-5);
            }

            return ret;
        }

        internal static LzmaRet X86EncoderInit(ref LzmaNextCoder next, LzmaFilterInfo[] filters, int index) =>
            X86CoderInit(ref next, filters, index, true);

        internal static LzmaRet X86DecoderInit(ref LzmaNextCoder next, LzmaFilterInfo[] filters, int index) =>
            X86CoderInit(ref next, filters, index, false);

        // ------------------------------------------------------------ powerpc.c

        /// <summary>powerpc_code</summary>
        private static int PowerPcCode(LzmaSimple simple, uint now_pos, bool is_encoder, byte[] buffer, int at, int size)
        {
            size &= ~3;

            int i;
            for (i = 0; i < size; i += 4)
            {
                // PowerPC branch 6(48) 24(Offset) 1(Abs) 1(Link)
                if ((buffer[at + i] >> 2) == 0x12 && (buffer[at + i + 3] & 3) == 1)
                {
                    uint src = (((uint)buffer[at + i + 0] & 3) << 24)
                        | ((uint)buffer[at + i + 1] << 16)
                        | ((uint)buffer[at + i + 2] << 8)
                        | ((uint)buffer[at + i + 3] & ~3u);

                    uint dest;
                    if (is_encoder)
                        dest = now_pos + (uint)i + src;
                    else
                        dest = src - (now_pos + (uint)i);

                    buffer[at + i + 0] = (byte)(0x48 | ((dest >> 24) & 0x03));
                    buffer[at + i + 1] = (byte)(dest >> 16);
                    buffer[at + i + 2] = (byte)(dest >> 8);
                    buffer[at + i + 3] &= 0x03;
                    buffer[at + i + 3] |= (byte)dest;
                }
            }

            return i;
        }

        internal static LzmaRet PowerPcEncoderInit(ref LzmaNextCoder next, LzmaFilterInfo[] filters, int index) =>
            CoderInit(ref next, filters, index, PowerPcCode, 4, 4, true);

        internal static LzmaRet PowerPcDecoderInit(ref LzmaNextCoder next, LzmaFilterInfo[] filters, int index) =>
            CoderInit(ref next, filters, index, PowerPcCode, 4, 4, false);

        // ------------------------------------------------------------ ia64.c

        private static readonly uint[] BRANCH_TABLE =
        {
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0,
            4, 4, 6, 6, 0, 0, 7, 7,
            4, 4, 0, 0, 4, 4, 0, 0,
        };

        /// <summary>ia64_code</summary>
        private static int Ia64Code(LzmaSimple simple, uint now_pos, bool is_encoder, byte[] buffer, int at, int size)
        {
            size &= ~15;

            int i;
            for (i = 0; i < size; i += 16)
            {
                uint instr_template = (uint)buffer[at + i] & 0x1F;
                uint mask = BRANCH_TABLE[instr_template];

                uint bit_pos = 5;
                for (int slot = 0; slot < 3; ++slot, bit_pos += 41)
                {
                    if (((mask >> slot) & 1) == 0)
                        continue;

                    int byte_pos = (int)(bit_pos >> 3);
                    int bit_res = (int)(bit_pos & 0x7);
                    ulong instruction = 0;

                    for (int j = 0; j < 6; ++j)
                        instruction += (ulong)buffer[at + i + j + byte_pos] << (8 * j);

                    ulong inst_norm = instruction >> bit_res;

                    if (((inst_norm >> 37) & 0xF) == 0x5 && ((inst_norm >> 9) & 0x7) == 0)
                    {
                        uint src = (uint)((inst_norm >> 13) & 0xFFFFF);
                        src |= (uint)(((inst_norm >> 36) & 1) << 20);

                        src <<= 4;

                        uint dest;
                        if (is_encoder)
                            dest = now_pos + (uint)i + src;
                        else
                            dest = src - (now_pos + (uint)i);

                        dest >>= 4;

                        inst_norm &= ~((ulong)0x8FFFFF << 13);
                        inst_norm |= (ulong)(dest & 0xFFFFF) << 13;
                        inst_norm |= (ulong)(dest & 0x100000) << (36 - 20);

                        instruction &= (1U << bit_res) - 1;
                        instruction |= inst_norm << bit_res;

                        for (int j = 0; j < 6; j++)
                            buffer[at + i + j + byte_pos] = (byte)(instruction >> (8 * j));
                    }
                }
            }

            return i;
        }

        internal static LzmaRet Ia64EncoderInit(ref LzmaNextCoder next, LzmaFilterInfo[] filters, int index) =>
            CoderInit(ref next, filters, index, Ia64Code, 16, 16, true);

        internal static LzmaRet Ia64DecoderInit(ref LzmaNextCoder next, LzmaFilterInfo[] filters, int index) =>
            CoderInit(ref next, filters, index, Ia64Code, 16, 16, false);

        // ------------------------------------------------------------ arm.c

        /// <summary>arm_code</summary>
        private static int ArmCode(LzmaSimple simple, uint now_pos, bool is_encoder, byte[] buffer, int at, int size)
        {
            size &= ~3;

            int i;
            for (i = 0; i < size; i += 4)
            {
                if (buffer[at + i + 3] == 0xEB)
                {
                    uint src = ((uint)buffer[at + i + 2] << 16)
                        | ((uint)buffer[at + i + 1] << 8)
                        | buffer[at + i + 0];
                    src <<= 2;

                    uint dest;
                    if (is_encoder)
                        dest = now_pos + (uint)i + 8 + src;
                    else
                        dest = src - (now_pos + (uint)i + 8);

                    dest >>= 2;
                    buffer[at + i + 2] = (byte)(dest >> 16);
                    buffer[at + i + 1] = (byte)(dest >> 8);
                    buffer[at + i + 0] = (byte)dest;
                }
            }

            return i;
        }

        internal static LzmaRet ArmEncoderInit(ref LzmaNextCoder next, LzmaFilterInfo[] filters, int index) =>
            CoderInit(ref next, filters, index, ArmCode, 4, 4, true);

        internal static LzmaRet ArmDecoderInit(ref LzmaNextCoder next, LzmaFilterInfo[] filters, int index) =>
            CoderInit(ref next, filters, index, ArmCode, 4, 4, false);

        // ------------------------------------------------------------ armthumb.c

        /// <summary>armthumb_code</summary>
        private static int ArmThumbCode(LzmaSimple simple, uint now_pos, bool is_encoder, byte[] buffer, int at, int size)
        {
            if (size < 4)
                return 0;

            size -= 4;

            int i;
            for (i = 0; i <= size; i += 2)
            {
                if ((buffer[at + i + 1] & 0xF8) == 0xF0 && (buffer[at + i + 3] & 0xF8) == 0xF8)
                {
                    uint src = (((uint)buffer[at + i + 1] & 7) << 19)
                        | ((uint)buffer[at + i + 0] << 11)
                        | (((uint)buffer[at + i + 3] & 7) << 8)
                        | buffer[at + i + 2];

                    src <<= 1;

                    uint dest;
                    if (is_encoder)
                        dest = now_pos + (uint)i + 4 + src;
                    else
                        dest = src - (now_pos + (uint)i + 4);

                    dest >>= 1;

                    buffer[at + i + 1] = (byte)(0xF0 | ((dest >> 19) & 0x7));
                    buffer[at + i + 0] = (byte)(dest >> 11);
                    buffer[at + i + 3] = (byte)(0xF8 | ((dest >> 8) & 0x7));
                    buffer[at + i + 2] = (byte)dest;
                    i += 2;
                }
            }

            return i;
        }

        internal static LzmaRet ArmThumbEncoderInit(ref LzmaNextCoder next, LzmaFilterInfo[] filters, int index) =>
            CoderInit(ref next, filters, index, ArmThumbCode, 4, 2, true);

        internal static LzmaRet ArmThumbDecoderInit(ref LzmaNextCoder next, LzmaFilterInfo[] filters, int index) =>
            CoderInit(ref next, filters, index, ArmThumbCode, 4, 2, false);

        // ------------------------------------------------------------ sparc.c

        /// <summary>sparc_code</summary>
        private static int SparcCode(LzmaSimple simple, uint now_pos, bool is_encoder, byte[] buffer, int at, int size)
        {
            size &= ~3;

            int i;
            for (i = 0; i < size; i += 4)
            {
                if ((buffer[at + i] == 0x40 && (buffer[at + i + 1] & 0xC0) == 0x00)
                        || (buffer[at + i] == 0x7F && (buffer[at + i + 1] & 0xC0) == 0xC0))
                {
                    uint src = ((uint)buffer[at + i + 0] << 24)
                        | ((uint)buffer[at + i + 1] << 16)
                        | ((uint)buffer[at + i + 2] << 8)
                        | buffer[at + i + 3];

                    src <<= 2;

                    uint dest;
                    if (is_encoder)
                        dest = now_pos + (uint)i + src;
                    else
                        dest = src - (now_pos + (uint)i);

                    dest >>= 2;

                    dest = (((0 - ((dest >> 22) & 1)) << 22) & 0x3FFFFFFF)
                        | (dest & 0x3FFFFF)
                        | 0x40000000;

                    buffer[at + i + 0] = (byte)(dest >> 24);
                    buffer[at + i + 1] = (byte)(dest >> 16);
                    buffer[at + i + 2] = (byte)(dest >> 8);
                    buffer[at + i + 3] = (byte)dest;
                }
            }

            return i;
        }

        internal static LzmaRet SparcEncoderInit(ref LzmaNextCoder next, LzmaFilterInfo[] filters, int index) =>
            CoderInit(ref next, filters, index, SparcCode, 4, 4, true);

        internal static LzmaRet SparcDecoderInit(ref LzmaNextCoder next, LzmaFilterInfo[] filters, int index) =>
            CoderInit(ref next, filters, index, SparcCode, 4, 4, false);

        // ------------------------------------------------------------ simple_encoder.c, simple_decoder.c

        /// <summary>lzma_simple_props_size</summary>
        internal static uint PropsSize(object options) =>
            options is not LzmaOptionsBcj opt || opt.start_offset == 0 ? 0u : 4u;

        /// <summary>lzma_simple_props_encode</summary>
        internal static LzmaRet PropsEncode(object options, byte[] output, int at)
        {
            if (options is not LzmaOptionsBcj opt || opt.start_offset == 0)
                return LzmaRet.OK;

            LzmaCore.Write32LE(output, at, opt.start_offset);
            return LzmaRet.OK;
        }

        /// <summary>lzma_simple_props_decode</summary>
        internal static LzmaRet PropsDecode(out object options, byte[] props, int at, int props_size)
        {
            options = null;
            if (props_size == 0)
                return LzmaRet.OK;

            if (props_size != 4)
                return LzmaRet.OPTIONS_ERROR;

            uint start_offset = LzmaCore.Read32LE(props, at);

            // Don't leave an options structure allocated if start_offset is zero.
            if (start_offset != 0)
                options = new LzmaOptionsBcj { start_offset = start_offset };

            return LzmaRet.OK;
        }
    }
}
