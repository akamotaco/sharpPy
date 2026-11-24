using System;
using System.Collections.Generic;

namespace SharpPy.Modules.Lzma
{
    /// <summary>lzma_block (api/lzma/block.h)</summary>
    internal sealed class LzmaBlock
    {
        internal uint version;
        internal uint header_size;
        internal int check;
        internal ulong compressed_size;
        internal ulong uncompressed_size;
        internal LzmaFilter[] filters;
        internal readonly byte[] raw_check = new byte[64];
        internal bool ignore_check;
    }

    /// <summary>lzma_stream_flags (api/lzma/stream_flags.h)</summary>
    internal struct LzmaStreamFlags
    {
        internal uint version;
        internal ulong backward_size;
        internal int check;
    }

    /// <summary>
    /// The .xz container: common/block_util.c, block_header_encoder.c, block_header_decoder.c,
    /// block_encoder.c, block_decoder.c, stream_flags_*.c, index_encoder.c (over the list of Records
    /// the single-threaded encoder appends to; index.c's tree serves seeking, which _lzma does not
    /// use), index_hash.c, stream_encoder.c, stream_decoder.c, alone_encoder.c, alone_decoder.c,
    /// auto_decoder.c, easy_preset.c and easy_encoder.c.
    /// </summary>
    internal static class LzmaContainer
    {
        internal static readonly byte[] lzma_header_magic = { 0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00 };
        internal static readonly byte[] lzma_footer_magic = { 0x59, 0x5A };
        internal const int LZMA_STREAM_FLAGS_SIZE = 2;

        internal const ulong UNPADDED_SIZE_MIN = 5;
        internal const ulong UNPADDED_SIZE_MAX = LzmaConst.VLI_MAX & ~3UL;
        internal const byte INDEX_INDICATOR = 0;

        // ------------------------------------------------------------ index.h

        /// <summary>vli_ceil4</summary>
        internal static ulong VliCeil4(ulong vli) => (vli + 3) & ~3UL;

        /// <summary>index_size_unpadded</summary>
        internal static ulong IndexSizeUnpadded(ulong count, ulong index_list_size) =>
            1 + LzmaCore.VliSize(count) + index_list_size + 4;

        /// <summary>index_size</summary>
        internal static ulong IndexSize(ulong count, ulong index_list_size) => VliCeil4(IndexSizeUnpadded(count, index_list_size));

        /// <summary>index_stream_size</summary>
        internal static ulong IndexStreamSize(ulong blocks_size, ulong count, ulong index_list_size) =>
            LzmaConst.STREAM_HEADER_SIZE + blocks_size + IndexSize(count, index_list_size) + LzmaConst.STREAM_HEADER_SIZE;

        // ------------------------------------------------------------ block_util.c

        /// <summary>lzma_block_unpadded_size</summary>
        internal static ulong BlockUnpaddedSize(LzmaBlock block)
        {
            // Validate the values that we are interested in i.e. all but Uncompressed Size and the filters.
            if (block == null || block.version > 1
                    || block.header_size < LzmaConst.BLOCK_HEADER_SIZE_MIN
                    || block.header_size > LzmaConst.BLOCK_HEADER_SIZE_MAX
                    || (block.header_size & 3) != 0
                    || !LzmaCore.VliIsValid(block.compressed_size)
                    || block.compressed_size == 0
                    || (uint)block.check > LzmaConst.CHECK_ID_MAX)
                return 0;

            // If Compressed Size is unknown, return that we cannot know size of the Block either.
            if (block.compressed_size == LzmaConst.VLI_UNKNOWN)
                return LzmaConst.VLI_UNKNOWN;

            // Calculate Unpadded Size and validate it.
            ulong unpadded_size = block.compressed_size + block.header_size + LzmaCheckState.Size(block.check);

            if (unpadded_size > UNPADDED_SIZE_MAX)
                return 0;

            return unpadded_size;
        }

        // ------------------------------------------------------------ block_header_encoder.c

        /// <summary>lzma_block_header_size</summary>
        internal static LzmaRet BlockHeaderSize(LzmaBlock block)
        {
            if (block.version > 1)
                return LzmaRet.OPTIONS_ERROR;

            // Block Header Size + Block Flags + CRC32.
            uint size = 1 + 1 + 4;

            // Compressed Size
            if (block.compressed_size != LzmaConst.VLI_UNKNOWN)
            {
                uint add = LzmaCore.VliSize(block.compressed_size);
                if (add == 0 || block.compressed_size == 0)
                    return LzmaRet.PROG_ERROR;

                size += add;
            }

            // Uncompressed Size
            if (block.uncompressed_size != LzmaConst.VLI_UNKNOWN)
            {
                uint add = LzmaCore.VliSize(block.uncompressed_size);
                if (add == 0)
                    return LzmaRet.PROG_ERROR;

                size += add;
            }

            // List of Filter Flags
            if (block.filters == null || LzmaFilters.Count(block.filters) == 0)
                return LzmaRet.PROG_ERROR;

            for (int i = 0; i < LzmaFilters.Count(block.filters); ++i)
            {
                // Don't allow too many filters.
                if (i == LzmaConst.FILTERS_MAX)
                    return LzmaRet.PROG_ERROR;

                var ret = LzmaFilters.FilterFlagsSize(out uint add, block.filters[i]);
                if (ret != LzmaRet.OK)
                    return ret;

                size += add;
            }

            // Pad to a multiple of four bytes.
            block.header_size = (size + 3) & ~3u;

            return LzmaRet.OK;
        }

        /// <summary>lzma_block_header_encode</summary>
        internal static LzmaRet BlockHeaderEncode(LzmaBlock block, byte[] output)
        {
            // Validate everything but filters.
            if (BlockUnpaddedSize(block) == 0 || !LzmaCore.VliIsValid(block.uncompressed_size))
                return LzmaRet.PROG_ERROR;

            // Indicate the size of the buffer _excluding_ the CRC32 field.
            int out_size = (int)block.header_size - 4;

            // Store the Block Header Size.
            output[0] = (byte)(out_size / 4);

            // We write Block Flags in pieces.
            output[1] = 0x00;
            int out_pos = 2;

            // Compressed Size
            if (block.compressed_size != LzmaConst.VLI_UNKNOWN)
            {
                var r = LzmaCore.VliEncode(block.compressed_size, output, ref out_pos, out_size);
                if (r != LzmaRet.OK)
                    return r;

                output[1] |= 0x40;
            }

            // Uncompressed Size
            if (block.uncompressed_size != LzmaConst.VLI_UNKNOWN)
            {
                var r = LzmaCore.VliEncode(block.uncompressed_size, output, ref out_pos, out_size);
                if (r != LzmaRet.OK)
                    return r;

                output[1] |= 0x80;
            }

            // Filter Flags
            if (block.filters == null || LzmaFilters.Count(block.filters) == 0)
                return LzmaRet.PROG_ERROR;

            int filter_count = 0;
            do
            {
                // There can be a maximum of four filters.
                if (filter_count == LzmaConst.FILTERS_MAX)
                    return LzmaRet.PROG_ERROR;

                var r = LzmaFilters.FilterFlagsEncode(block.filters[filter_count], output, ref out_pos, out_size);
                if (r != LzmaRet.OK)
                    return r;
            } while (++filter_count < LzmaFilters.Count(block.filters));

            output[1] |= (byte)(filter_count - 1);

            // Padding
            Array.Clear(output, out_pos, out_size - out_pos);

            // CRC32
            LzmaCore.Write32LE(output, out_size, LzmaCheckState.Crc32(output.AsSpan(0, out_size), 0));

            return LzmaRet.OK;
        }

        // ------------------------------------------------------------ block_header_decoder.c

        /// <summary>lzma_block_header_size_decode</summary>
        internal static uint BlockHeaderSizeDecode(byte b) => ((uint)b + 1) * 4;

        /// <summary>lzma_block_header_decode</summary>
        internal static LzmaRet BlockHeaderDecode(LzmaBlock block, byte[] input)
        {
            // Initialize the filter options array.
            for (int i = 0; i <= LzmaConst.FILTERS_MAX; ++i)
            {
                block.filters[i].id = LzmaConst.VLI_UNKNOWN;
                block.filters[i].options = null;
            }

            // Versions 0 and 1 are supported. If a newer version was specified, we need to downgrade it.
            if (block.version > 1)
                block.version = 1;

            // This isn't a Block Header option but since the decompressor will read it if version >= 1, it's better to initialize it here than to expect the caller to do it.
            block.ignore_check = false;

            // Validate Block Header Size and Check type.
            if (BlockHeaderSizeDecode(input[0]) != block.header_size || (uint)block.check > LzmaConst.CHECK_ID_MAX)
                return LzmaRet.PROG_ERROR;

            // Exclude the CRC32 field.
            int in_size = (int)block.header_size - 4;

            // Verify CRC32
            if (LzmaCheckState.Crc32(input.AsSpan(0, in_size), 0) != LzmaCore.Read32LE(input, in_size))
                return LzmaRet.DATA_ERROR;

            // Check for unsupported flags.
            if ((input[1] & 0x3C) != 0)
                return LzmaRet.OPTIONS_ERROR;

            // Start after the Block Header Size and Block Flags fields.
            int in_pos = 2;

            // Compressed Size
            if ((input[1] & 0x40) != 0)
            {
                var r = LzmaCore.VliDecode(out block.compressed_size, input, ref in_pos, in_size);
                if (r != LzmaRet.OK)
                    return r;

                // Validate Compressed Size. This checks that it isn't zero and that the total size of the Block is a valid VLI.
                if (BlockUnpaddedSize(block) == 0)
                    return LzmaRet.DATA_ERROR;
            }
            else
            {
                block.compressed_size = LzmaConst.VLI_UNKNOWN;
            }

            // Uncompressed Size
            if ((input[1] & 0x80) != 0)
            {
                var r = LzmaCore.VliDecode(out block.uncompressed_size, input, ref in_pos, in_size);
                if (r != LzmaRet.OK)
                    return r;
            }
            else
            {
                block.uncompressed_size = LzmaConst.VLI_UNKNOWN;
            }

            // Filter Flags
            int filter_count = (input[1] & 3) + 1;
            for (int i = 0; i < filter_count; ++i)
            {
                var ret = LzmaFilters.FilterFlagsDecode(ref block.filters[i], input, ref in_pos, in_size);
                if (ret != LzmaRet.OK)
                {
                    FiltersFree(block.filters);
                    return ret;
                }
            }

            // Padding
            while (in_pos < in_size)
            {
                if (input[in_pos++] != 0x00)
                {
                    FiltersFree(block.filters);

                    // Possibly some new field present so use LZMA_OPTIONS_ERROR instead of LZMA_DATA_ERROR.
                    return LzmaRet.OPTIONS_ERROR;
                }
            }

            return LzmaRet.OK;
        }

        /// <summary>lzma_filters_free</summary>
        internal static void FiltersFree(LzmaFilter[] filters)
        {
            if (filters == null)
                return;
            for (int i = 0; i < filters.Length && filters[i].id != LzmaConst.VLI_UNKNOWN; ++i)
            {
                filters[i].options = null;
                filters[i].id = LzmaConst.VLI_UNKNOWN;
            }
        }

        /// <summary>lzma_filters_copy: the options are copied, so the caller's may change.</summary>
        internal static LzmaFilter[] FiltersCopy(LzmaFilter[] src)
        {
            int n = LzmaFilters.Count(src);
            var dest = new LzmaFilter[LzmaConst.FILTERS_MAX + 1];
            for (int i = 0; i < n; i++)
            {
                object options = src[i].options switch
                {
                    LzmaOptionsLzma o => o.Clone(),
                    LzmaOptionsDelta d => new LzmaOptionsDelta { type = d.type, dist = d.dist },
                    LzmaOptionsBcj b => new LzmaOptionsBcj { start_offset = b.start_offset },
                    _ => src[i].options,
                };
                dest[i] = new LzmaFilter(src[i].id, options);
            }
            for (int i = n; i < dest.Length; i++)
                dest[i].id = LzmaConst.VLI_UNKNOWN;
            return dest;
        }

        // ------------------------------------------------------------ stream_flags_*.c

        /// <summary>stream_flags_encode: true on an invalid check.</summary>
        private static bool StreamFlagsEncode(in LzmaStreamFlags options, byte[] output, int at)
        {
            if ((uint)options.check > LzmaConst.CHECK_ID_MAX)
                return true;

            output[at] = 0x00;
            output[at + 1] = (byte)options.check;

            return false;
        }

        /// <summary>lzma_stream_header_encode</summary>
        internal static LzmaRet StreamHeaderEncode(in LzmaStreamFlags options, byte[] output)
        {
            if (options.version != 0)
                return LzmaRet.OPTIONS_ERROR;

            // Magic
            Buffer.BlockCopy(lzma_header_magic, 0, output, 0, lzma_header_magic.Length);

            // Stream Flags
            if (StreamFlagsEncode(options, output, lzma_header_magic.Length))
                return LzmaRet.PROG_ERROR;

            // CRC32 of the Stream Header
            uint crc = LzmaCheckState.Crc32(output.AsSpan(lzma_header_magic.Length, LZMA_STREAM_FLAGS_SIZE), 0);

            LzmaCore.Write32LE(output, lzma_header_magic.Length + LZMA_STREAM_FLAGS_SIZE, crc);

            return LzmaRet.OK;
        }

        /// <summary>is_backward_size_valid</summary>
        private static bool IsBackwardSizeValid(in LzmaStreamFlags options) =>
            options.backward_size >= LzmaConst.BACKWARD_SIZE_MIN
            && options.backward_size <= LzmaConst.BACKWARD_SIZE_MAX
            && (options.backward_size & 3) == 0;

        /// <summary>lzma_stream_footer_encode</summary>
        internal static LzmaRet StreamFooterEncode(in LzmaStreamFlags options, byte[] output)
        {
            if (options.version != 0)
                return LzmaRet.OPTIONS_ERROR;

            // Backward Size
            if (!IsBackwardSizeValid(options))
                return LzmaRet.PROG_ERROR;

            LzmaCore.Write32LE(output, 4, (uint)(options.backward_size / 4 - 1));

            // Stream Flags
            if (StreamFlagsEncode(options, output, 2 * 4))
                return LzmaRet.PROG_ERROR;

            // CRC32
            uint crc = LzmaCheckState.Crc32(output.AsSpan(4, 4 + LZMA_STREAM_FLAGS_SIZE), 0);

            LzmaCore.Write32LE(output, 0, crc);

            // Magic
            Buffer.BlockCopy(lzma_footer_magic, 0, output, 2 * 4 + LZMA_STREAM_FLAGS_SIZE, lzma_footer_magic.Length);

            return LzmaRet.OK;
        }

        /// <summary>stream_flags_decode: true on unsupported flags.</summary>
        private static bool StreamFlagsDecode(ref LzmaStreamFlags options, byte[] input, int at)
        {
            // Reserved bits must be unset.
            if (input[at] != 0x00 || (input[at + 1] & 0xF0) != 0)
                return true;

            options.version = 0;
            options.check = input[at + 1] & 0x0F;

            return false;
        }

        /// <summary>lzma_stream_header_decode</summary>
        internal static LzmaRet StreamHeaderDecode(ref LzmaStreamFlags options, byte[] input)
        {
            // Magic
            if (!input.AsSpan(0, lzma_header_magic.Length).SequenceEqual(lzma_header_magic))
                return LzmaRet.FORMAT_ERROR;

            // Verify the CRC32 so we can distinguish between corrupt and unsupported files.
            uint crc = LzmaCheckState.Crc32(input.AsSpan(lzma_header_magic.Length, LZMA_STREAM_FLAGS_SIZE), 0);
            if (crc != LzmaCore.Read32LE(input, lzma_header_magic.Length + LZMA_STREAM_FLAGS_SIZE))
                return LzmaRet.DATA_ERROR;

            // Stream Flags
            if (StreamFlagsDecode(ref options, input, lzma_header_magic.Length))
                return LzmaRet.OPTIONS_ERROR;

            // Set Backward Size to indicate unknown value.
            options.backward_size = LzmaConst.VLI_UNKNOWN;

            return LzmaRet.OK;
        }

        /// <summary>lzma_stream_footer_decode</summary>
        internal static LzmaRet StreamFooterDecode(ref LzmaStreamFlags options, byte[] input)
        {
            // Magic
            if (!input.AsSpan(4 * 2 + LZMA_STREAM_FLAGS_SIZE, lzma_footer_magic.Length).SequenceEqual(lzma_footer_magic))
                return LzmaRet.FORMAT_ERROR;

            // CRC32
            uint crc = LzmaCheckState.Crc32(input.AsSpan(4, 4 + LZMA_STREAM_FLAGS_SIZE), 0);
            if (crc != LzmaCore.Read32LE(input, 0))
                return LzmaRet.DATA_ERROR;

            // Stream Flags
            if (StreamFlagsDecode(ref options, input, 4 * 2))
                return LzmaRet.OPTIONS_ERROR;

            // Backward Size
            options.backward_size = LzmaCore.Read32LE(input, 4);
            options.backward_size = (options.backward_size + 1) * 4;

            return LzmaRet.OK;
        }

        /// <summary>lzma_stream_flags_compare</summary>
        internal static LzmaRet StreamFlagsCompare(in LzmaStreamFlags a, in LzmaStreamFlags b)
        {
            // We can compare only version 0 structures.
            if (a.version != 0 || b.version != 0)
                return LzmaRet.OPTIONS_ERROR;

            // Check type
            if ((uint)a.check > LzmaConst.CHECK_ID_MAX || (uint)b.check > LzmaConst.CHECK_ID_MAX)
                return LzmaRet.PROG_ERROR;

            if (a.check != b.check)
                return LzmaRet.DATA_ERROR;

            // Backward Sizes are compared only if both are known.
            if (a.backward_size != LzmaConst.VLI_UNKNOWN && b.backward_size != LzmaConst.VLI_UNKNOWN)
            {
                if (!IsBackwardSizeValid(a) || !IsBackwardSizeValid(b))
                    return LzmaRet.PROG_ERROR;

                if (a.backward_size != b.backward_size)
                    return LzmaRet.DATA_ERROR;
            }

            return LzmaRet.OK;
        }

        // ------------------------------------------------------------ easy_preset.c, easy_encoder.c

        /// <summary>lzma_easy_preset: true on an unsupported preset.</summary>
        internal static bool EasyPreset(out LzmaFilter[] filters, uint preset)
        {
            var opt_lzma = new LzmaOptionsLzma();
            filters = null;
            if (LzmaProbs.Preset(opt_lzma, preset))
                return true;

            filters = new LzmaFilter[LzmaConst.FILTERS_MAX + 1];
            filters[0] = new LzmaFilter(LzmaConst.FILTER_LZMA2, opt_lzma);
            for (int i = 1; i < filters.Length; i++)
                filters[i].id = LzmaConst.VLI_UNKNOWN;

            return false;
        }

        /// <summary>lzma_easy_encoder</summary>
        internal static LzmaRet EasyEncoder(LzmaStream strm, uint preset, int check)
        {
            if (EasyPreset(out var filters, preset))
                return LzmaRet.OPTIONS_ERROR;

            return LzmaStreamEncoder.Create(strm, filters, check);
        }
    }

    /// <summary>The lzma_block_coder of common/block_encoder.c</summary>
    internal sealed class LzmaBlockEncoder : LzmaNextCoder
    {
        // The maximum size of the Compressed Data field so that the Block stays a valid VLI.
        private const ulong COMPRESSED_SIZE_MAX = (LzmaConst.VLI_MAX - LzmaConst.BLOCK_HEADER_SIZE_MAX - 64) & ~3UL;

        private const int SEQ_CODE = 0, SEQ_PADDING = 1, SEQ_CHECK = 2;

        // The filters in the chain; initialized with lzma_raw_decoder_init().
        private LzmaNextCoder next;

        /// <summary>lzma_next_end of the coder behind this one.</summary>
        internal override void End()
        {
            next?.End();
            next = null;
        }

        // Encoding options; we also write Unpadded Size, Compressed Size, and Uncompressed Size back to this structure when the encoding has been finished.
        private LzmaBlock block;

        private int sequence;

        // Compressed Size calculated while encoding
        private ulong compressed_size;

        // Uncompressed Size calculated while encoding
        private ulong uncompressed_size;

        // Position in the Check field
        private int pos;

        // Check of the uncompressed data
        private readonly LzmaCheckState check = new();

        /// <summary>block_encode</summary>
        internal override LzmaRet Code(byte[] input, ref int in_pos, int in_size,
            byte[] output, ref int out_pos, int out_size, LzmaAction action)
        {
            // Check that our amount of input stays in proper limits.
            if (LzmaConst.VLI_MAX - uncompressed_size < (ulong)(in_size - in_pos))
                return LzmaRet.DATA_ERROR;

            switch (sequence)
            {
                case SEQ_CODE:
                    {
                        int in_start = in_pos;
                        int out_start = out_pos;

                        var ret = next.Code(input, ref in_pos, in_size, output, ref out_pos, out_size, action);

                        int in_used = in_pos - in_start;
                        int out_used = out_pos - out_start;

                        if (COMPRESSED_SIZE_MAX - compressed_size < (ulong)out_used)
                            return LzmaRet.DATA_ERROR;

                        compressed_size += (ulong)out_used;

                        // No need to check for overflow because we have already checked it at the beginning of this function.
                        uncompressed_size += (ulong)in_used;

                        // Call lzma_check_update() only if input was consumed.
                        if (in_used > 0)
                            check.Update(block.check, input.AsSpan(in_start, in_used));

                        if (ret != LzmaRet.STREAM_END || action == LzmaAction.SYNC_FLUSH)
                            return ret;

                        // Copy the values into coder->block.
                        block.compressed_size = compressed_size;
                        block.uncompressed_size = uncompressed_size;

                        sequence = SEQ_PADDING;
                        goto case SEQ_PADDING;
                    }

                case SEQ_PADDING:
                    // Pad Compressed Data to a multiple of four bytes.
                    while ((compressed_size & 3) != 0)
                    {
                        if (out_pos >= out_size)
                            return LzmaRet.OK;

                        output[out_pos] = 0x00;
                        ++out_pos;
                        ++compressed_size;
                    }

                    if (block.check == LzmaConst.CHECK_NONE)
                        return LzmaRet.STREAM_END;

                    check.Finish(block.check);

                    sequence = SEQ_CHECK;
                    goto case SEQ_CHECK;

                case SEQ_CHECK:
                    {
                        int check_size = (int)LzmaCheckState.Size(block.check);
                        LzmaCore.BufCpy(check.buffer, ref pos, check_size, output, ref out_pos, out_size);
                        if (pos < check_size)
                            return LzmaRet.OK;

                        Buffer.BlockCopy(check.buffer, 0, block.raw_check, 0, check_size);
                        return LzmaRet.STREAM_END;
                    }
            }

            return LzmaRet.PROG_ERROR;
        }

        /// <summary>lzma_block_encoder_init</summary>
        internal static LzmaRet Init(ref LzmaNextCoder next, LzmaBlock block)
        {
            if (block == null)
                return LzmaRet.PROG_ERROR;

            // The contents of the structure may depend on the version so check the version first.
            if (block.version > 1)
                return LzmaRet.OPTIONS_ERROR;

            // If the Check ID is not supported, we cannot calculate the check and thus not create a proper Block.
            if ((uint)block.check > LzmaConst.CHECK_ID_MAX)
                return LzmaRet.PROG_ERROR;

            if (!LzmaCheckState.IsSupported(block.check))
                return LzmaRet.UNSUPPORTED_CHECK;

            // Allocate and initialize *next->coder if needed.
            if (next is not LzmaBlockEncoder coder)
            {
                coder = new LzmaBlockEncoder();
                next = coder;
            }

            // Basic initializations
            coder.sequence = SEQ_CODE;
            coder.block = block;
            coder.compressed_size = 0;
            coder.uncompressed_size = 0;
            coder.pos = 0;

            // Initialize the check
            coder.check.Init(block.check);

            // Initialize the requested filters.
            return LzmaFilters.RawEncoderInit(ref coder.next, block.filters);
        }
    }

    /// <summary>The lzma_block_coder of common/block_decoder.c</summary>
    internal sealed class LzmaBlockDecoder : LzmaNextCoder
    {
        private const int SEQ_CODE = 0, SEQ_PADDING = 1, SEQ_CHECK = 2;

        private int sequence;

        // The filters in the chain; initialized with lzma_raw_decoder_init().
        private LzmaNextCoder next;

        /// <summary>lzma_next_end of the coder behind this one.</summary>
        internal override void End()
        {
            next?.End();
            next = null;
        }

        // Decoding options; we also write Compressed Size and Uncompressed Size back to this structure when the decoding has been finished.
        private LzmaBlock block;

        // Compressed Size calculated while decoding
        private ulong compressed_size;

        // Uncompressed Size calculated while decoding
        private ulong uncompressed_size;

        // Maximum allowed Compressed Size; this takes into account the size of the Block Header and Check fields when Compressed Size is unknown.
        private ulong compressed_limit;

        // Maximum allowed Uncompressed Size.
        private ulong uncompressed_limit;

        // Position when reading the Check field
        private int check_pos;

        // Check of the uncompressed data
        private readonly LzmaCheckState check = new();

        // True if the integrity check won't be calculated and verified.
        private bool ignore_check;

        // is_size_valid
        private static bool IsSizeValid(ulong size, ulong reference) => reference == LzmaConst.VLI_UNKNOWN || reference == size;

        /// <summary>block_decode</summary>
        internal override LzmaRet Code(byte[] input, ref int in_pos, int in_size,
            byte[] output, ref int out_pos, int out_size, LzmaAction action)
        {
            switch (sequence)
            {
                case SEQ_CODE:
                    {
                        int in_start = in_pos;
                        int out_start = out_pos;

                        // Limit the amount of input and output space that we pass to the filter chain to prevent decoding past the limits.
                        int in_stop = in_pos + (int)Math.Min((ulong)(in_size - in_pos), compressed_limit - compressed_size);
                        int out_stop = out_pos + (int)Math.Min((ulong)(out_size - out_pos), uncompressed_limit - uncompressed_size);

                        var ret = next.Code(input, ref in_pos, in_stop, output, ref out_pos, out_stop, action);

                        int in_used = in_pos - in_start;
                        int out_used = out_pos - out_start;

                        // Because we have limited the input and output sizes, we know that these cannot grow too big or overflow.
                        compressed_size += (ulong)in_used;
                        uncompressed_size += (ulong)out_used;

                        if (ret == LzmaRet.OK)
                        {
                            bool comp_done = compressed_size == block.compressed_size;
                            bool uncomp_done = uncompressed_size == block.uncompressed_size;

                            // If both input and output amounts match the sizes in Block Header but we still got LZMA_OK instead of LZMA_STREAM_END, the file is broken.
                            if (comp_done && uncomp_done)
                                return LzmaRet.DATA_ERROR;

                            // If the decoder has consumed all the input that it needs but it still couldn't fill the output buffer or return LZMA_STREAM_END, the file is broken.
                            if (comp_done && out_pos < out_size)
                                return LzmaRet.DATA_ERROR;

                            // If the decoder has produced all the output but it still didn't return LZMA_STREAM_END or consume more input, the file is broken.
                            if (uncomp_done && in_pos < in_size)
                                return LzmaRet.DATA_ERROR;
                        }

                        // Don't waste time updating the integrity check if it will be ignored.
                        if (!ignore_check && out_used > 0)
                            check.Update(block.check, output.AsSpan(out_start, out_used));

                        if (ret != LzmaRet.STREAM_END)
                            return ret;

                        // Compressed and Uncompressed Sizes are now at their final values. Verify that they match the values given to us.
                        if (!IsSizeValid(compressed_size, block.compressed_size) || !IsSizeValid(uncompressed_size, block.uncompressed_size))
                            return LzmaRet.DATA_ERROR;

                        // Copy the values into coder->block.
                        block.compressed_size = compressed_size;
                        block.uncompressed_size = uncompressed_size;

                        sequence = SEQ_PADDING;
                        goto case SEQ_PADDING;
                    }

                case SEQ_PADDING:
                    // Compressed Data is padded to a multiple of four bytes.
                    while ((compressed_size & 3) != 0)
                    {
                        if (in_pos >= in_size)
                            return LzmaRet.OK;

                        // We use compressed_size here just get the Padding right.
                        ++compressed_size;

                        if (input[in_pos++] != 0x00)
                            return LzmaRet.DATA_ERROR;
                    }

                    if (block.check == LzmaConst.CHECK_NONE)
                        return LzmaRet.STREAM_END;

                    if (!ignore_check)
                        check.Finish(block.check);

                    sequence = SEQ_CHECK;
                    goto case SEQ_CHECK;

                case SEQ_CHECK:
                    {
                        int check_size = (int)LzmaCheckState.Size(block.check);
                        LzmaCore.BufCpy(input, ref in_pos, in_size, block.raw_check, ref check_pos, check_size);
                        if (check_pos < check_size)
                            return LzmaRet.OK;

                        // Validate the Check only if we support it.
                        if (!ignore_check && LzmaCheckState.IsSupported(block.check)
                                && !block.raw_check.AsSpan(0, check_size).SequenceEqual(check.buffer.AsSpan(0, check_size)))
                            return LzmaRet.DATA_ERROR;

                        return LzmaRet.STREAM_END;
                    }
            }

            return LzmaRet.PROG_ERROR;
        }

        /// <summary>lzma_block_decoder_init</summary>
        internal static LzmaRet Init(ref LzmaNextCoder next, LzmaBlock block)
        {
            // Validate the options. lzma_block_unpadded_size() does that for us except for Uncompressed Size and filters.
            if (LzmaContainer.BlockUnpaddedSize(block) == 0 || !LzmaCore.VliIsValid(block.uncompressed_size))
                return LzmaRet.PROG_ERROR;

            // Allocate *next->coder if needed.
            if (next is not LzmaBlockDecoder coder)
            {
                coder = new LzmaBlockDecoder();
                next = coder;
            }

            // Basic initializations
            coder.sequence = SEQ_CODE;
            coder.block = block;
            coder.compressed_size = 0;
            coder.uncompressed_size = 0;

            // If Compressed Size is not known, we calculate the maximum allowed value so that encoded size of the Block (including Block Padding) is still a valid VLI and a multiple of four.
            coder.compressed_limit = block.compressed_size == LzmaConst.VLI_UNKNOWN
                ? (LzmaConst.VLI_MAX & ~3UL) - block.header_size - LzmaCheckState.Size(block.check)
                : block.compressed_size;

            // With Uncompressed Size this is simpler.
            coder.uncompressed_limit = block.uncompressed_size == LzmaConst.VLI_UNKNOWN
                ? LzmaConst.VLI_MAX
                : block.uncompressed_size;

            // Initialize the check.
            coder.check_pos = 0;
            coder.check.Init(block.check);

            coder.ignore_check = block.version >= 1 && block.ignore_check;

            // Initialize the filter chain.
            return LzmaFilters.RawDecoderInit(ref coder.next, block.filters);
        }
    }

    /// <summary>The lzma_index_coder of common/index_encoder.c over the Records of one Stream.</summary>
    internal sealed class LzmaIndexEncoder : LzmaNextCoder
    {
        private const int SEQ_INDICATOR = 0, SEQ_COUNT = 1, SEQ_UNPADDED = 2, SEQ_UNCOMPRESSED = 3, SEQ_NEXT = 4,
            SEQ_PADDING = 5, SEQ_CRC32 = 6;

        private int sequence;

        // Index being encoded
        private LzmaIndexRecords index;

        // Iterator for the Index being encoded
        private int iter = -1;

        // Position in integers
        private int pos;

        // CRC32 of the List of Records field
        private uint crc32;

        /// <summary>index_encode</summary>
        internal override LzmaRet Code(byte[] input, ref int in_pos, int in_size,
            byte[] output, ref int out_pos, int out_size, LzmaAction action)
        {
            // Position where to start calculating CRC32.
            int out_start = out_pos;

            LzmaRet ret = LzmaRet.OK;

            while (out_pos < out_size)
            {
                switch (sequence)
                {
                    case SEQ_INDICATOR:
                        output[out_pos] = LzmaContainer.INDEX_INDICATOR;
                        ++out_pos;
                        sequence = SEQ_COUNT;
                        break;

                    case SEQ_COUNT:
                        {
                            ulong count = (ulong)index.records.Count;
                            ret = LzmaCore.VliEncode(count, ref pos, false, output, ref out_pos, out_size);
                            if (ret != LzmaRet.STREAM_END)
                                goto out_;

                            ret = LzmaRet.OK;
                            pos = 0;
                            sequence = SEQ_NEXT;
                            break;
                        }

                    case SEQ_NEXT:
                        if (++iter >= index.records.Count)
                        {
                            // Get the size of the Index Padding field.
                            pos = (int)((4 - LzmaContainer.IndexSizeUnpadded((ulong)index.records.Count, index.index_list_size)) & 3);
                            sequence = SEQ_PADDING;
                            break;
                        }

                        sequence = SEQ_UNPADDED;
                        goto case SEQ_UNPADDED;

                    case SEQ_UNPADDED:
                    case SEQ_UNCOMPRESSED:
                        {
                            ulong size = sequence == SEQ_UNPADDED
                                ? index.records[iter].unpadded_size
                                : index.records[iter].uncompressed_size;

                            ret = LzmaCore.VliEncode(size, ref pos, false, output, ref out_pos, out_size);
                            if (ret != LzmaRet.STREAM_END)
                                goto out_;

                            ret = LzmaRet.OK;
                            pos = 0;

                            // Advance to SEQ_UNCOMPRESSED or SEQ_NEXT.
                            ++sequence;
                            break;
                        }

                    case SEQ_PADDING:
                        if (pos > 0)
                        {
                            --pos;
                            output[out_pos++] = 0x00;
                            break;
                        }

                        // Finish the CRC32 calculation.
                        crc32 = LzmaCheckState.Crc32(output.AsSpan(out_start, out_pos - out_start), crc32);

                        sequence = SEQ_CRC32;
                        goto case SEQ_CRC32;

                    case SEQ_CRC32:
                        // We don't use the CRC32 field in the CRC32 calculation.
                        do
                        {
                            if (out_pos == out_size)
                                return LzmaRet.OK;

                            output[out_pos] = (byte)((crc32 >> (pos * 8)) & 0xFF);
                            ++out_pos;
                        } while (++pos < 4);

                        return LzmaRet.STREAM_END;

                    default:
                        return LzmaRet.PROG_ERROR;
                }
            }

        out_:
            // Update the CRC32.
            {
                int out_used = out_pos - out_start;
                if (out_used > 0)
                    crc32 = LzmaCheckState.Crc32(output.AsSpan(out_start, out_used), crc32);
            }

            return ret;
        }

        /// <summary>lzma_index_encoder_init</summary>
        internal static LzmaRet Init(ref LzmaNextCoder next, LzmaIndexRecords i)
        {
            if (i == null)
                return LzmaRet.PROG_ERROR;

            if (next is not LzmaIndexEncoder coder)
            {
                coder = new LzmaIndexEncoder();
                next = coder;
            }

            // index_encoder_reset
            coder.iter = -1;
            coder.sequence = SEQ_INDICATOR;
            coder.index = i;
            coder.pos = 0;
            coder.crc32 = 0;

            return LzmaRet.OK;
        }
    }

    /// <summary>
    /// The Records of a Stream's Index that the stream encoder appends to (lzma_index_append and the
    /// sizes lzma_index_size and lzma_index_padding_size compute).
    /// </summary>
    internal sealed class LzmaIndexRecords
    {
        internal readonly List<(ulong unpadded_size, ulong uncompressed_size)> records = new();
        internal ulong index_list_size;
        internal ulong total_size;
        internal ulong uncompressed_size;

        /// <summary>lzma_index_append</summary>
        internal LzmaRet Append(ulong unpadded_size, ulong uncompressed_size)
        {
            // Validate.
            if (unpadded_size < LzmaContainer.UNPADDED_SIZE_MIN || unpadded_size > LzmaContainer.UNPADDED_SIZE_MAX
                    || uncompressed_size > LzmaConst.VLI_MAX)
                return LzmaRet.PROG_ERROR;

            uint index_list_size_add = LzmaCore.VliSize(unpadded_size) + LzmaCore.VliSize(uncompressed_size);

            // Check that the new unpadded size won't overflow lzma_vli or the Index.
            if (this.uncompressed_size + uncompressed_size > LzmaConst.VLI_MAX)
                return LzmaRet.DATA_ERROR;

            ulong new_total = total_size + LzmaContainer.VliCeil4(unpadded_size);
            if (LzmaContainer.IndexStreamSize(new_total, (ulong)records.Count + 1, index_list_size + index_list_size_add) > LzmaConst.VLI_MAX
                    || LzmaContainer.IndexSize((ulong)records.Count + 1, index_list_size + index_list_size_add) > LzmaConst.BACKWARD_SIZE_MAX)
                return LzmaRet.DATA_ERROR;

            records.Add((unpadded_size, uncompressed_size));
            total_size = new_total;
            this.uncompressed_size += uncompressed_size;
            index_list_size += index_list_size_add;
            return LzmaRet.OK;
        }

        /// <summary>lzma_index_size</summary>
        internal ulong Size => LzmaContainer.IndexSize((ulong)records.Count, index_list_size);
    }

    /// <summary>common/index_hash.c</summary>
    internal sealed class LzmaIndexHash
    {
        private sealed class Info
        {
            // Sum of the Block sizes (including Block Padding)
            internal ulong blocks_size;

            // Sum of the Uncompressed Size fields
            internal ulong uncompressed_size;

            // Number of Records
            internal ulong count;

            // Size of the List of Index Records as bytes
            internal ulong index_list_size;

            // Check calculated from Unpadded Sizes and Uncompressed Sizes.
            internal readonly LzmaCheckState check = new();
        }

        private const int SEQ_BLOCK = 0, SEQ_COUNT = 1, SEQ_UNPADDED = 2, SEQ_UNCOMPRESSED = 3, SEQ_PADDING_INIT = 4,
            SEQ_PADDING = 5, SEQ_CRC32 = 6;

        private int sequence;

        // Information collected while decoding the actual Blocks.
        private Info blocks;

        // Information collected from the Index field.
        private Info records;

        // Number of Records not fully decoded
        private ulong remaining;

        // Unpadded Size currently being read from an Index Record.
        private ulong unpadded_size;

        // Uncompressed Size currently being read from an Index Record.
        private ulong uncompressed_size;

        // Position in variable-length integers when decoding them from the List of Records.
        private int pos;

        // CRC32 of the Index
        private uint crc32;

        /// <summary>lzma_index_hash_init</summary>
        internal static LzmaIndexHash Init(LzmaIndexHash index_hash)
        {
            index_hash ??= new LzmaIndexHash();

            index_hash.sequence = SEQ_BLOCK;
            index_hash.blocks = new Info();
            index_hash.records = new Info();
            index_hash.unpadded_size = 0;
            index_hash.uncompressed_size = 0;
            index_hash.pos = 0;
            index_hash.crc32 = 0;

            // These cannot fail because LZMA_CHECK_BEST is known to be supported.
            index_hash.blocks.check.Init(LzmaConst.CHECK_BEST);
            index_hash.records.check.Init(LzmaConst.CHECK_BEST);

            return index_hash;
        }

        /// <summary>lzma_index_hash_size</summary>
        internal ulong Size => LzmaContainer.IndexSize(blocks.count, blocks.index_list_size);

        /// <summary>hash_append</summary>
        private static void HashAppend(Info info, ulong unpadded_size, ulong uncompressed_size)
        {
            info.blocks_size += LzmaContainer.VliCeil4(unpadded_size);
            info.uncompressed_size += uncompressed_size;
            info.index_list_size += LzmaCore.VliSize(unpadded_size) + LzmaCore.VliSize(uncompressed_size);
            ++info.count;

            Span<byte> sizes = stackalloc byte[16];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(sizes, unpadded_size);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(sizes.Slice(8), uncompressed_size);
            info.check.Update(LzmaConst.CHECK_BEST, sizes);
        }

        /// <summary>lzma_index_hash_append</summary>
        internal LzmaRet Append(ulong unpadded_size, ulong uncompressed_size)
        {
            // Validate the arguments.
            if (sequence != SEQ_BLOCK
                    || unpadded_size < LzmaContainer.UNPADDED_SIZE_MIN
                    || unpadded_size > LzmaContainer.UNPADDED_SIZE_MAX
                    || uncompressed_size > LzmaConst.VLI_MAX)
                return LzmaRet.PROG_ERROR;

            // Update the hash.
            HashAppend(blocks, unpadded_size, uncompressed_size);

            // Validate the properties of *info are still in allowed limits.
            if (blocks.blocks_size > LzmaConst.VLI_MAX
                    || blocks.uncompressed_size > LzmaConst.VLI_MAX
                    || LzmaContainer.IndexSize(blocks.count, blocks.index_list_size) > LzmaConst.BACKWARD_SIZE_MAX
                    || LzmaContainer.IndexStreamSize(blocks.blocks_size, blocks.count, blocks.index_list_size) > LzmaConst.VLI_MAX)
                return LzmaRet.DATA_ERROR;

            return LzmaRet.OK;
        }

        /// <summary>lzma_index_hash_decode</summary>
        internal LzmaRet Decode(byte[] input, ref int in_pos, int in_size)
        {
            // Catch zero input buffer here, because in contrast to Index encoder and decoder functions, applications call this function directly instead of via lzma_code(), which does the buffer checking.
            if (in_pos >= in_size)
                return LzmaRet.BUF_ERROR;

            // NOTE: This function has many similarities to index_encode() and index_decode() functions found from index_encoder.c and index_decoder.c.
            int in_start = in_pos;
            LzmaRet ret = LzmaRet.OK;

            while (in_pos < in_size)
            {
                switch (sequence)
                {
                    case SEQ_BLOCK:
                        // Check the Index Indicator is present.
                        if (input[in_pos++] != LzmaContainer.INDEX_INDICATOR)
                            return LzmaRet.DATA_ERROR;

                        sequence = SEQ_COUNT;
                        break;

                    case SEQ_COUNT:
                        {
                            ret = LzmaCore.VliDecode(ref remaining, ref pos, false, input, ref in_pos, in_size);
                            if (ret != LzmaRet.STREAM_END)
                                goto out_;

                            // The count must match the count of the Blocks decoded.
                            if (remaining != blocks.count)
                                return LzmaRet.DATA_ERROR;

                            ret = LzmaRet.OK;
                            pos = 0;

                            // Handle the special case when there are no Blocks.
                            sequence = remaining == 0 ? SEQ_PADDING_INIT : SEQ_UNPADDED;
                            break;
                        }

                    case SEQ_UNPADDED:
                    case SEQ_UNCOMPRESSED:
                        {
                            if (sequence == SEQ_UNPADDED)
                                ret = LzmaCore.VliDecode(ref unpadded_size, ref pos, false, input, ref in_pos, in_size);
                            else
                                ret = LzmaCore.VliDecode(ref uncompressed_size, ref pos, false, input, ref in_pos, in_size);
                            if (ret != LzmaRet.STREAM_END)
                                goto out_;

                            ret = LzmaRet.OK;
                            pos = 0;

                            if (sequence == SEQ_UNPADDED)
                            {
                                if (unpadded_size < LzmaContainer.UNPADDED_SIZE_MIN || unpadded_size > LzmaContainer.UNPADDED_SIZE_MAX)
                                    return LzmaRet.DATA_ERROR;

                                sequence = SEQ_UNCOMPRESSED;
                            }
                            else
                            {
                                // Update the hash.
                                HashAppend(records, unpadded_size, uncompressed_size);

                                // Verify that we don't go over the known sizes. Note that this validation is simpler than the one used in lzma_index_hash_append(), because here we know that values in index_hash->blocks are already validated and we are fine as long as we don't exceed them in index_hash->records.
                                if (blocks.blocks_size < records.blocks_size
                                        || blocks.uncompressed_size < records.uncompressed_size
                                        || blocks.index_list_size < records.index_list_size)
                                    return LzmaRet.DATA_ERROR;

                                // Check if this was the last Record.
                                sequence = --remaining == 0 ? SEQ_PADDING_INIT : SEQ_UNPADDED;
                            }

                            break;
                        }

                    case SEQ_PADDING_INIT:
                        pos = (int)((4 - LzmaContainer.IndexSizeUnpadded(records.count, records.index_list_size)) & 3);
                        sequence = SEQ_PADDING;
                        goto case SEQ_PADDING;

                    case SEQ_PADDING:
                        if (pos > 0)
                        {
                            --pos;
                            if (input[in_pos++] != 0x00)
                                return LzmaRet.DATA_ERROR;

                            break;
                        }

                        // Compare the sizes.
                        if (blocks.blocks_size != records.blocks_size
                                || blocks.uncompressed_size != records.uncompressed_size
                                || blocks.index_list_size != records.index_list_size)
                            return LzmaRet.DATA_ERROR;

                        // Finish the hashes and compare them.
                        blocks.check.Finish(LzmaConst.CHECK_BEST);
                        records.check.Finish(LzmaConst.CHECK_BEST);
                        if (!blocks.check.buffer.AsSpan(0, 32).SequenceEqual(records.check.buffer.AsSpan(0, 32)))
                            return LzmaRet.DATA_ERROR;

                        // Finish the CRC32 calculation.
                        crc32 = LzmaCheckState.Crc32(input.AsSpan(in_start, in_pos - in_start), crc32);

                        sequence = SEQ_CRC32;
                        goto case SEQ_CRC32;

                    case SEQ_CRC32:
                        do
                        {
                            if (in_pos == in_size)
                                return LzmaRet.OK;

                            if (((crc32 >> (pos * 8)) & 0xFF) != input[in_pos++])
                                return LzmaRet.DATA_ERROR;
                        } while (++pos < 4);

                        return LzmaRet.STREAM_END;

                    default:
                        return LzmaRet.PROG_ERROR;
                }
            }

        out_:
            // Update the CRC32.
            {
                int in_used = in_pos - in_start;
                if (in_used > 0)
                    crc32 = LzmaCheckState.Crc32(input.AsSpan(in_start, in_used), crc32);
            }

            return ret;
        }
    }

    /// <summary>The lzma_stream_coder of common/stream_encoder.c</summary>
    internal sealed class LzmaStreamEncoder : LzmaNextCoder
    {
        private const int SEQ_STREAM_HEADER = 0, SEQ_BLOCK_INIT = 1, SEQ_BLOCK_HEADER = 2, SEQ_BLOCK_ENCODE = 3,
            SEQ_INDEX_ENCODE = 4, SEQ_STREAM_FOOTER = 5;

        private int sequence;

        // True if Block encoder has been initialized by stream_encoder_init() or stream_encoder_update() and thus doesn't need to be initialized in stream_encode().
        private bool block_encoder_is_initialized;

        // Block
        private LzmaNextCoder block_encoder;

        // Options for the Block encoder
        private readonly LzmaBlock block_options = new();

        // The filter chain currently in use
        private LzmaFilter[] filters = new LzmaFilter[LzmaConst.FILTERS_MAX + 1];

        // Index encoder. This is separate from Block encoder, because this doesn't take much memory, and when encoding multiple Streams with the same encoding options we avoid reallocating memory.
        private LzmaNextCoder index_encoder;

        /// <summary>stream_encoder_end</summary>
        internal override void End()
        {
            block_encoder?.End();
            block_encoder = null;
            index_encoder = null;
        }

        // Index to hold sizes of the Blocks
        private LzmaIndexRecords index;

        // Read position in buffer[]
        private int buffer_pos;

        // Total number of bytes in buffer[]
        private int buffer_size;

        // Buffer to hold Stream Header, Block Header, and Stream Footer. Block Header has biggest maximum size.
        private readonly byte[] buffer = new byte[LzmaConst.BLOCK_HEADER_SIZE_MAX];

        /// <summary>block_encoder_init</summary>
        private LzmaRet BlockEncoderInit()
        {
            // Prepare the Block options. Even though Block encoder doesn't need this, it's good to know the size of the Block Header if needed.
            block_options.compressed_size = LzmaConst.VLI_UNKNOWN;
            block_options.uncompressed_size = LzmaConst.VLI_UNKNOWN;

            var ret = LzmaContainer.BlockHeaderSize(block_options);
            if (ret != LzmaRet.OK)
                return ret;

            // Initialize the actual Block encoder.
            return LzmaBlockEncoder.Init(ref block_encoder, block_options);
        }

        /// <summary>stream_encode</summary>
        internal override LzmaRet Code(byte[] input, ref int in_pos, int in_size,
            byte[] output, ref int out_pos, int out_size, LzmaAction action)
        {
            // Main loop
            while (out_pos < out_size)
            {
                switch (sequence)
                {
                    case SEQ_STREAM_HEADER:
                    case SEQ_BLOCK_HEADER:
                    case SEQ_STREAM_FOOTER:
                        LzmaCore.BufCpy(buffer, ref buffer_pos, buffer_size, output, ref out_pos, out_size);
                        if (buffer_pos < buffer_size)
                            return LzmaRet.OK;

                        // Return if we just finished the Stream Footer field.
                        if (sequence == SEQ_STREAM_FOOTER)
                            return LzmaRet.STREAM_END;

                        buffer_pos = 0;
                        ++sequence;
                        break;

                    case SEQ_BLOCK_INIT:
                        {
                            if (in_pos == in_size)
                            {
                                // If we are requested to flush or finish the current Block, return LZMA_STREAM_END immediately since there's nothing to do.
                                if (action != LzmaAction.FINISH)
                                    return action == LzmaAction.RUN ? LzmaRet.OK : LzmaRet.STREAM_END;

                                // The application had used LZMA_FULL_FLUSH to finish the previous Block, but now wants to finish without encoding new data, or it is simply creating an empty Stream with no Blocks.
                                var r = LzmaIndexEncoder.Init(ref index_encoder, index);
                                if (r != LzmaRet.OK)
                                    return r;
                                sequence = SEQ_INDEX_ENCODE;
                                break;
                            }

                            // Initialize the Block encoder unless it was already initialized by stream_encoder_init() or stream_encoder_update().
                            if (!block_encoder_is_initialized)
                            {
                                var r = BlockEncoderInit();
                                if (r != LzmaRet.OK)
                                    return r;
                            }

                            // Make it false so that we don't skip the initialization with the next Block.
                            block_encoder_is_initialized = false;

                            // Encode the Block Header. This shouldn't fail since we have already initialized the Block encoder.
                            if (LzmaContainer.BlockHeaderEncode(block_options, buffer) != LzmaRet.OK)
                                return LzmaRet.PROG_ERROR;

                            buffer_size = (int)block_options.header_size;
                            sequence = SEQ_BLOCK_HEADER;
                            break;
                        }

                    case SEQ_BLOCK_ENCODE:
                        {
                            // Convert the action: LZMA_RUN, LZMA_SYNC_FLUSH, LZMA_FINISH, LZMA_FINISH, LZMA_FINISH
                            LzmaAction converted = action == LzmaAction.RUN ? LzmaAction.RUN
                                : action == LzmaAction.SYNC_FLUSH ? LzmaAction.SYNC_FLUSH : LzmaAction.FINISH;

                            var ret = block_encoder.Code(input, ref in_pos, in_size, output, ref out_pos, out_size, converted);
                            if (ret != LzmaRet.STREAM_END || action == LzmaAction.SYNC_FLUSH)
                                return ret;

                            // Add a new Index Record.
                            ulong unpadded_size = LzmaContainer.BlockUnpaddedSize(block_options);
                            var r = index.Append(unpadded_size, block_options.uncompressed_size);
                            if (r != LzmaRet.OK)
                                return r;

                            sequence = SEQ_BLOCK_INIT;
                            break;
                        }

                    case SEQ_INDEX_ENCODE:
                        {
                            // Call the Index encoder. It doesn't take any input, so those pointers can be NULL.
                            int zero = 0;
                            var ret = index_encoder.Code(Array.Empty<byte>(), ref zero, 0, output, ref out_pos, out_size, LzmaAction.RUN);
                            if (ret != LzmaRet.STREAM_END)
                                return ret;

                            // Encode the Stream Footer into coder->buffer.
                            var stream_flags = new LzmaStreamFlags
                            {
                                version = 0,
                                backward_size = index.Size,
                                check = block_options.check,
                            };

                            if (LzmaContainer.StreamFooterEncode(stream_flags, buffer) != LzmaRet.OK)
                                return LzmaRet.PROG_ERROR;

                            buffer_size = LzmaConst.STREAM_HEADER_SIZE;
                            sequence = SEQ_STREAM_FOOTER;
                            break;
                        }

                    default:
                        return LzmaRet.PROG_ERROR;
                }
            }

            return LzmaRet.OK;
        }

        /// <summary>stream_encoder_update</summary>
        private LzmaRet Update(LzmaFilter[] filters)
        {
            // Make a copy to a temporary buffer first. This way it is easier to keep the encoder state unchanged if an error occurs with lzma_filters_copy().
            var temp = LzmaContainer.FiltersCopy(filters);

            if (sequence <= SEQ_BLOCK_INIT)
            {
                // There is no incomplete Block waiting to be finished, thus we can change the whole filter chain. Start by trying to initialize the Block encoder with the new chain. This way we detect if the chain is valid.
                block_encoder_is_initialized = false;
                block_options.filters = temp;
                var ret = BlockEncoderInit();
                block_options.filters = this.filters;
                if (ret != LzmaRet.OK)
                    return ret;

                block_encoder_is_initialized = true;
            }
            else
            {
                return LzmaRet.PROG_ERROR;
            }

            // Free the options of the old chain and replace it with the new one.
            this.filters = temp;
            block_options.filters = this.filters;

            return LzmaRet.OK;
        }

        /// <summary>stream_encoder_init</summary>
        private static LzmaRet Init(ref LzmaNextCoder next, LzmaFilter[] filters, int check)
        {
            if (filters == null)
                return LzmaRet.PROG_ERROR;

            if (next is not LzmaStreamEncoder coder)
            {
                coder = new LzmaStreamEncoder();
                next = coder;
                coder.filters[0].id = LzmaConst.VLI_UNKNOWN;
            }

            // Basic initializations
            coder.sequence = SEQ_STREAM_HEADER;
            coder.block_options.version = 0;
            coder.block_options.check = check;

            // Initialize the Index
            coder.index = new LzmaIndexRecords();

            // Encode the Stream Header
            var stream_flags = new LzmaStreamFlags { version = 0, check = check };
            var r = LzmaContainer.StreamHeaderEncode(stream_flags, coder.buffer);
            if (r != LzmaRet.OK)
                return r;

            coder.buffer_pos = 0;
            coder.buffer_size = LzmaConst.STREAM_HEADER_SIZE;

            // Initialize the Block encoder. This way we detect unsupported filter chains when initializing the Stream encoder instead of giving an error after Stream Header has already been written out.
            return coder.Update(filters);
        }

        /// <summary>lzma_stream_encoder</summary>
        internal static LzmaRet Create(LzmaStream strm, LzmaFilter[] filters, int check)
        {
            var ret = LzmaCore.NextStrmInit(strm, next =>
            {
                var r = Init(ref next, filters, check);
                return (r, next);
            });
            if (ret != LzmaRet.OK)
                return ret;

            strm.supported_actions[(int)LzmaAction.RUN] = true;
            strm.supported_actions[(int)LzmaAction.SYNC_FLUSH] = true;
            strm.supported_actions[(int)LzmaAction.FULL_FLUSH] = true;
            strm.supported_actions[(int)LzmaAction.FULL_BARRIER] = true;
            strm.supported_actions[(int)LzmaAction.FINISH] = true;

            return LzmaRet.OK;
        }
    }

    /// <summary>The lzma_stream_coder of common/stream_decoder.c</summary>
    internal sealed class LzmaStreamDecoder : LzmaNextCoder
    {
        private const int SEQ_STREAM_HEADER = 0, SEQ_BLOCK_HEADER = 1, SEQ_BLOCK_INIT = 2, SEQ_BLOCK_RUN = 3,
            SEQ_INDEX = 4, SEQ_STREAM_FOOTER = 5, SEQ_STREAM_PADDING = 6;

        private int sequence;

        // Block decoder
        private LzmaNextCoder block_decoder;

        /// <summary>stream_decoder_end</summary>
        internal override void End()
        {
            block_decoder?.End();
            block_decoder = null;
        }

        // Block options decoded by the Block Header decoder and used by the Block decoder.
        private readonly LzmaBlock block_options = new();

        // Stream Flags from Stream Header
        private LzmaStreamFlags stream_flags;

        // Index is hashed so that it can be compared to the sizes of Blocks with O(1) memory usage.
        private LzmaIndexHash index_hash;

        // Memory usage limit
        private ulong memlimit;

        // Amount of memory actually needed (only an estimate)
        private ulong memusage;

        private bool tell_no_check;
        private bool tell_unsupported_check;
        private bool tell_any_check;
        private bool ignore_check;
        private bool concatenated;

        // When decoding concatenated Streams, this is true as long as we are decoding the first Stream.
        private bool first_stream;

        // Write position in buffer[] and position in Stream Padding
        private int pos;

        // Buffer to hold Stream Header, Block Header, and Stream Footer. Block Header has biggest maximum size.
        private readonly byte[] buffer = new byte[LzmaConst.BLOCK_HEADER_SIZE_MAX];

        /// <summary>stream_decoder_reset</summary>
        private LzmaRet Reset()
        {
            // Initialize the Index hash used to verify the Index.
            index_hash = LzmaIndexHash.Init(index_hash);

            // Reset the rest of the variables.
            sequence = SEQ_STREAM_HEADER;
            pos = 0;

            return LzmaRet.OK;
        }

        /// <summary>stream_decode</summary>
        internal override LzmaRet Code(byte[] input, ref int in_pos, int in_size,
            byte[] output, ref int out_pos, int out_size, LzmaAction action)
        {
            // When decoding the actual Block, it may be able to produce more output even if we don't give it any new input.
            while (true)
            {
                switch (sequence)
                {
                    case SEQ_STREAM_HEADER:
                        {
                            // Copy the Stream Header to the internal buffer.
                            LzmaCore.BufCpy(input, ref in_pos, in_size, buffer, ref pos, LzmaConst.STREAM_HEADER_SIZE);

                            // Return if we didn't get the whole Stream Header yet.
                            if (pos < LzmaConst.STREAM_HEADER_SIZE)
                                return LzmaRet.OK;

                            pos = 0;

                            // Decode the Stream Header.
                            var ret = LzmaContainer.StreamHeaderDecode(ref stream_flags, buffer);
                            if (ret != LzmaRet.OK)
                                return ret == LzmaRet.FORMAT_ERROR && !first_stream ? LzmaRet.DATA_ERROR : ret;

                            // If we are decoding concatenated Streams, and the later Streams have invalid Header Magic Bytes, we give LZMA_DATA_ERROR instead of LZMA_FORMAT_ERROR.
                            first_stream = false;

                            // Copy the type of the Check so that Block Header and Block decoders see it.
                            block_options.check = stream_flags.check;

                            // Even if we return LZMA_*_CHECK below, we want to continue from Block Header decoding.
                            sequence = SEQ_BLOCK_HEADER;

                            // Detect if there's no integrity check or if it is unsupported if those were requested by the application.
                            if (tell_no_check && stream_flags.check == LzmaConst.CHECK_NONE)
                                return LzmaRet.NO_CHECK;

                            if (tell_unsupported_check && !LzmaCheckState.IsSupported(stream_flags.check))
                                return LzmaRet.UNSUPPORTED_CHECK;

                            if (tell_any_check)
                                return LzmaRet.GET_CHECK;

                            goto case SEQ_BLOCK_HEADER;
                        }

                    case SEQ_BLOCK_HEADER:
                        {
                            if (in_pos >= in_size)
                                return LzmaRet.OK;

                            if (pos == 0)
                            {
                                // Detect if it's Index.
                                if (input[in_pos] == LzmaContainer.INDEX_INDICATOR)
                                {
                                    sequence = SEQ_INDEX;
                                    break;
                                }

                                // Calculate the size of the Block Header. Note that Block Header decoder wants to see this byte too so don't advance *in_pos.
                                block_options.header_size = LzmaContainer.BlockHeaderSizeDecode(input[in_pos]);
                            }

                            // Copy the Block Header to the internal buffer.
                            LzmaCore.BufCpy(input, ref in_pos, in_size, buffer, ref pos, (int)block_options.header_size);

                            // Return if we didn't get the whole Block Header yet.
                            if (pos < block_options.header_size)
                                return LzmaRet.OK;

                            pos = 0;
                            sequence = SEQ_BLOCK_INIT;
                            goto case SEQ_BLOCK_INIT;
                        }

                    case SEQ_BLOCK_INIT:
                        {
                            // Checking memusage and doing the initialization needs its own sequence point because we need to be able to retry if we return LZMA_MEMLIMIT_ERROR.

                            // Version 1 is needed to support the .ignore_check option.
                            block_options.version = 1;

                            // Set up a buffer to hold the filter chain. Block Header decoder will initialize all members of this array so we don't need to do it here.
                            var filters = new LzmaFilter[LzmaConst.FILTERS_MAX + 1];
                            block_options.filters = filters;

                            // Decode the Block Header.
                            var r = LzmaContainer.BlockHeaderDecode(block_options, buffer);
                            if (r != LzmaRet.OK)
                                return r;

                            // If LZMA_IGNORE_CHECK was used, this flag needs to be set. It has to be set after lzma_block_header_decode() because it always resets this to false.
                            block_options.ignore_check = ignore_check;

                            // Check the memory usage limit.
                            ulong memusage = LzmaFilters.RawDecoderMemUsage(filters);
                            LzmaRet ret;

                            if (memusage == ulong.MaxValue)
                            {
                                // One or more unknown Filter IDs.
                                ret = LzmaRet.OPTIONS_ERROR;
                            }
                            else
                            {
                                // Now we can set coder->memusage since we know that the filter chain is valid. We don't want lzma_memusage() to return UINT64_MAX in case of invalid filter chain.
                                this.memusage = memusage;

                                if (memusage > memlimit)
                                {
                                    // The chain would need too much memory.
                                    ret = LzmaRet.MEMLIMIT_ERROR;
                                }
                                else
                                {
                                    // Memory usage is OK. Initialize the Block decoder.
                                    ret = LzmaBlockDecoder.Init(ref block_decoder, block_options);
                                }
                            }

                            // Free the allocated filter options since they are needed only to initialize the Block decoder.
                            block_options.filters = null;

                            // Check if memory usage calculation and Block decoder initialization succeeded.
                            if (ret != LzmaRet.OK)
                                return ret;

                            sequence = SEQ_BLOCK_RUN;
                            goto case SEQ_BLOCK_RUN;
                        }

                    case SEQ_BLOCK_RUN:
                        {
                            var ret = block_decoder.Code(input, ref in_pos, in_size, output, ref out_pos, out_size, action);

                            if (ret != LzmaRet.STREAM_END)
                                return ret;

                            // Block decoded successfully. Add the new size pair to the Index hash.
                            var r = index_hash.Append(LzmaContainer.BlockUnpaddedSize(block_options), block_options.uncompressed_size);
                            if (r != LzmaRet.OK)
                                return r;

                            sequence = SEQ_BLOCK_HEADER;
                            break;
                        }

                    case SEQ_INDEX:
                        {
                            // If we don't have any input, don't call lzma_index_hash_decode() since it would return LZMA_BUF_ERROR, which we must not do here.
                            if (in_pos >= in_size)
                                return LzmaRet.OK;

                            // Decode the Index and compare it to the hash calculated from the sizes of the Blocks (if any).
                            var ret = index_hash.Decode(input, ref in_pos, in_size);
                            if (ret != LzmaRet.STREAM_END)
                                return ret;

                            sequence = SEQ_STREAM_FOOTER;
                            goto case SEQ_STREAM_FOOTER;
                        }

                    case SEQ_STREAM_FOOTER:
                        {
                            // Copy the Stream Footer to the internal buffer.
                            LzmaCore.BufCpy(input, ref in_pos, in_size, buffer, ref pos, LzmaConst.STREAM_HEADER_SIZE);

                            // Return if we didn't get the whole Stream Footer yet.
                            if (pos < LzmaConst.STREAM_HEADER_SIZE)
                                return LzmaRet.OK;

                            pos = 0;

                            // Decode the Stream Footer. The decoder gives LZMA_FORMAT_ERROR if the magic bytes don't match, so convert that return code to LZMA_DATA_ERROR.
                            var footer_flags = new LzmaStreamFlags();
                            var ret = LzmaContainer.StreamFooterDecode(ref footer_flags, buffer);
                            if (ret != LzmaRet.OK)
                                return ret == LzmaRet.FORMAT_ERROR ? LzmaRet.DATA_ERROR : ret;

                            // Check that Index Size stored in the Stream Footer matches the real size of the Index field.
                            if (index_hash.Size != footer_flags.backward_size)
                                return LzmaRet.DATA_ERROR;

                            // Compare that the Stream Flags fields are identical in both Stream Header and Stream Footer.
                            var r = LzmaContainer.StreamFlagsCompare(stream_flags, footer_flags);
                            if (r != LzmaRet.OK)
                                return r;

                            if (!concatenated)
                                return LzmaRet.STREAM_END;

                            sequence = SEQ_STREAM_PADDING;
                            goto case SEQ_STREAM_PADDING;
                        }

                    case SEQ_STREAM_PADDING:
                        // Skip over possible Stream Padding.
                        while (true)
                        {
                            if (in_pos >= in_size)
                            {
                                // Unless LZMA_FINISH was used, we cannot know if there's more input coming later.
                                if (action != LzmaAction.FINISH)
                                    return LzmaRet.OK;

                                // Stream Padding must be a multiple of four bytes.
                                return pos == 0 ? LzmaRet.STREAM_END : LzmaRet.DATA_ERROR;
                            }

                            // If the byte is not zero, it probably indicates beginning of a new Stream (or the file is corrupt).
                            if (input[in_pos] != 0x00)
                                break;

                            ++in_pos;
                            pos = (pos + 1) & 3;
                        }

                        // Stream Padding must be a multiple of four bytes (empty Stream Padding is OK).
                        if (pos != 0)
                        {
                            ++in_pos;
                            return LzmaRet.DATA_ERROR;
                        }

                        // Prepare to decode the next Stream.
                        {
                            var r = Reset();
                            if (r != LzmaRet.OK)
                                return r;
                        }
                        break;

                    default:
                        return LzmaRet.PROG_ERROR;
                }
            }
        }

        /// <summary>stream_decoder_get_check</summary>
        internal override int GetCheck() => stream_flags.check;

        internal override bool HasMemConfig => true;

        /// <summary>stream_decoder_memconfig</summary>
        internal override LzmaRet MemConfig(out ulong memusage, out ulong old_memlimit, ulong new_memlimit)
        {
            memusage = this.memusage;
            old_memlimit = memlimit;

            if (new_memlimit != 0)
            {
                if (new_memlimit < this.memusage)
                    return LzmaRet.MEMLIMIT_ERROR;

                memlimit = new_memlimit;
            }

            return LzmaRet.OK;
        }

        /// <summary>lzma_stream_decoder_init</summary>
        internal static LzmaRet Init(ref LzmaNextCoder next, ulong memlimit, uint flags)
        {
            // Initialize the Stream decoder.
            if ((flags & ~LzmaConst.SUPPORTED_FLAGS) != 0)
                return LzmaRet.OPTIONS_ERROR;

            if (next is not LzmaStreamDecoder coder)
            {
                coder = new LzmaStreamDecoder();
                next = coder;
            }

            coder.memlimit = Math.Max(1, memlimit);
            coder.memusage = LzmaConst.MEMUSAGE_BASE;
            coder.tell_no_check = (flags & LzmaConst.TELL_NO_CHECK) != 0;
            coder.tell_unsupported_check = (flags & LzmaConst.TELL_UNSUPPORTED_CHECK) != 0;
            coder.tell_any_check = (flags & LzmaConst.TELL_ANY_CHECK) != 0;
            coder.ignore_check = (flags & LzmaConst.IGNORE_CHECK) != 0;
            coder.concatenated = (flags & LzmaConst.CONCATENATED) != 0;
            coder.first_stream = true;

            return coder.Reset();
        }

        /// <summary>lzma_stream_decoder</summary>
        internal static LzmaRet Create(LzmaStream strm, ulong memlimit, uint flags)
        {
            var ret = LzmaCore.NextStrmInit(strm, next =>
            {
                var r = Init(ref next, memlimit, flags);
                return (r, next);
            });
            if (ret != LzmaRet.OK)
                return ret;

            strm.supported_actions[(int)LzmaAction.RUN] = true;
            strm.supported_actions[(int)LzmaAction.FINISH] = true;

            return LzmaRet.OK;
        }
    }

    /// <summary>The lzma_alone_coder of common/alone_encoder.c</summary>
    internal sealed class LzmaAloneEncoder : LzmaNextCoder
    {
        private const int ALONE_HEADER_SIZE = 1 + 4 + 8;
        private const int SEQ_HEADER = 0, SEQ_CODE = 1;

        private LzmaNextCoder next;

        /// <summary>lzma_next_end of the coder behind this one.</summary>
        internal override void End()
        {
            next?.End();
            next = null;
        }
        private int sequence;
        private int header_pos;
        private readonly byte[] header = new byte[ALONE_HEADER_SIZE];

        private static readonly LzmaInitFunction lzma1_init = LzmaLzma1Encoder.EncoderInit;

        /// <summary>alone_encode</summary>
        internal override LzmaRet Code(byte[] input, ref int in_pos, int in_size,
            byte[] output, ref int out_pos, int out_size, LzmaAction action)
        {
            while (out_pos < out_size)
            {
                switch (sequence)
                {
                    case SEQ_HEADER:
                        LzmaCore.BufCpy(header, ref header_pos, ALONE_HEADER_SIZE, output, ref out_pos, out_size);
                        if (header_pos < ALONE_HEADER_SIZE)
                            return LzmaRet.OK;

                        sequence = SEQ_CODE;
                        break;

                    case SEQ_CODE:
                        return next.Code(input, ref in_pos, in_size, output, ref out_pos, out_size, action);

                    default:
                        return LzmaRet.PROG_ERROR;
                }
            }

            return LzmaRet.OK;
        }

        /// <summary>alone_encoder_init</summary>
        private static LzmaRet Init(ref LzmaNextCoder next, LzmaOptionsLzma options)
        {
            if (next is not LzmaAloneEncoder coder)
            {
                coder = new LzmaAloneEncoder();
                next = coder;
            }

            // Basic initializations
            coder.sequence = SEQ_HEADER;
            coder.header_pos = 0;

            // Encode the header: Properties
            if (LzmaProbs.LcLpPbEncode(options, out byte b))
                return LzmaRet.OPTIONS_ERROR;
            coder.header[0] = b;

            // Dictionary size
            if (options.dict_size < LzmaConst.DICT_SIZE_MIN)
                return LzmaRet.OPTIONS_ERROR;

            // Round up to the next 2^n or 2^n + 2^(n - 1) depending on which one is the next.
            uint d = options.dict_size - 1;
            d |= d >> 2;
            d |= d >> 3;
            d |= d >> 4;
            d |= d >> 8;
            d |= d >> 16;
            if (d != uint.MaxValue)
                ++d;

            LzmaCore.Write32LE(coder.header, 1, d);

            // - Uncompressed size (always unknown and using EOPM)
            Array.Fill(coder.header, (byte)0xFF, 1 + 4, 8);

            // Initialize the LZMA encoder.
            var filters = new LzmaFilterInfo[2];
            filters[0].id = LzmaConst.FILTER_LZMA1;
            filters[0].init = lzma1_init;
            filters[0].options = options;
            filters[1].init = null;

            return LzmaCore.NextFilterInit(ref coder.next, filters, 0);
        }

        /// <summary>lzma_alone_encoder</summary>
        internal static LzmaRet Create(LzmaStream strm, LzmaOptionsLzma options)
        {
            var ret = LzmaCore.NextStrmInit(strm, next =>
            {
                var r = Init(ref next, options);
                return (r, next);
            });
            if (ret != LzmaRet.OK)
                return ret;

            strm.supported_actions[(int)LzmaAction.RUN] = true;
            strm.supported_actions[(int)LzmaAction.FINISH] = true;

            return LzmaRet.OK;
        }
    }

    /// <summary>The lzma_alone_coder of common/alone_decoder.c</summary>
    internal sealed class LzmaAloneDecoder : LzmaNextCoder
    {
        private const int SEQ_PROPERTIES = 0, SEQ_DICTIONARY_SIZE = 1, SEQ_UNCOMPRESSED_SIZE = 2, SEQ_CODER_INIT = 3,
            SEQ_CODE = 4;

        private LzmaNextCoder next;

        /// <summary>lzma_next_end of the coder behind this one.</summary>
        internal override void End()
        {
            next?.End();
            next = null;
        }
        private int sequence;

        // If true, reject files that are unlikely to be .lzma files. If false, more non-.lzma files get accepted and will give LZMA_DATA_ERROR either immediately or after a few output bytes.
        private bool picky;

        // Position in the header fields
        private int pos;

        // Uncompressed size decoded from the header
        private ulong uncompressed_size;

        // Memory usage limit
        private ulong memlimit;

        // Amount of memory actually needed (only an estimate)
        private ulong memusage;

        // Options decoded from the header needed to initialize the LZMA decoder
        private readonly LzmaOptionsLzma options = new();

        private static readonly LzmaInitFunction lzma1_init = LzmaLzma1Decoder.DecoderInit;

        /// <summary>alone_decode</summary>
        internal override LzmaRet Code(byte[] input, ref int in_pos, int in_size,
            byte[] output, ref int out_pos, int out_size, LzmaAction action)
        {
            while (out_pos < out_size && (sequence == SEQ_CODE || in_pos < in_size))
            {
                switch (sequence)
                {
                    case SEQ_PROPERTIES:
                        if (LzmaProbs.LcLpPbDecode(options, input[in_pos]))
                            return LzmaRet.FORMAT_ERROR;

                        sequence = SEQ_DICTIONARY_SIZE;
                        ++in_pos;
                        break;

                    case SEQ_DICTIONARY_SIZE:
                        options.dict_size |= (uint)input[in_pos] << (pos * 8);

                        if (++pos == 4)
                        {
                            if (picky && options.dict_size != uint.MaxValue)
                            {
                                // A hack to ditch tons of false positives: We allow only dictionary sizes that are 2^n or 2^n + 2^(n-1) or UINT32_MAX.
                                uint d = options.dict_size - 1;
                                d |= d >> 2;
                                d |= d >> 3;
                                d |= d >> 4;
                                d |= d >> 8;
                                d |= d >> 16;
                                ++d;

                                if (d != options.dict_size)
                                    return LzmaRet.FORMAT_ERROR;
                            }

                            pos = 0;
                            sequence = SEQ_UNCOMPRESSED_SIZE;
                        }

                        ++in_pos;
                        break;

                    case SEQ_UNCOMPRESSED_SIZE:
                        uncompressed_size |= (ulong)input[in_pos] << (pos * 8);
                        ++in_pos;
                        if (++pos < 8)
                            break;

                        // Another hack to ditch false positives: Assume that if the uncompressed size is known, it must be less than 256 GiB.
                        if (picky && uncompressed_size != LzmaConst.VLI_UNKNOWN && uncompressed_size >= (1UL << 38))
                            return LzmaRet.FORMAT_ERROR;

                        // Use LZMA_FILTER_LZMA1EXT features to specify the uncompressed size and that the end marker is allowed even when the uncompressed size is known.
                        options.ext_flags = LzmaConst.LZMA1EXT_ALLOW_EOPM;
                        // lzma_set_ext_size
                        options.ext_size_low = (uint)uncompressed_size;
                        options.ext_size_high = (uint)(uncompressed_size >> 32);

                        // Calculate the memory usage so that it is ready for SEQ_CODER_INIT.
                        memusage = LzmaLzma1Decoder.MemUsageNoCheck(options) + LzmaConst.MEMUSAGE_BASE;

                        pos = 0;
                        sequence = SEQ_CODER_INIT;
                        goto case SEQ_CODER_INIT;

                    case SEQ_CODER_INIT:
                        {
                            if (memusage > memlimit)
                                return LzmaRet.MEMLIMIT_ERROR;

                            var filters = new LzmaFilterInfo[2];
                            filters[0].id = LzmaConst.FILTER_LZMA1EXT;
                            filters[0].init = lzma1_init;
                            filters[0].options = options;
                            filters[1].init = null;

                            var r = LzmaCore.NextFilterInit(ref next, filters, 0);
                            if (r != LzmaRet.OK)
                                return r;

                            sequence = SEQ_CODE;
                            break;
                        }

                    case SEQ_CODE:
                        return next.Code(input, ref in_pos, in_size, output, ref out_pos, out_size, action);

                    default:
                        return LzmaRet.PROG_ERROR;
                }
            }

            return LzmaRet.OK;
        }

        internal override bool HasMemConfig => true;

        /// <summary>alone_decoder_memconfig</summary>
        internal override LzmaRet MemConfig(out ulong memusage, out ulong old_memlimit, ulong new_memlimit)
        {
            memusage = this.memusage;
            old_memlimit = memlimit;

            if (new_memlimit != 0)
            {
                if (new_memlimit < this.memusage)
                    return LzmaRet.MEMLIMIT_ERROR;

                memlimit = new_memlimit;
            }

            return LzmaRet.OK;
        }

        /// <summary>lzma_alone_decoder_init</summary>
        internal static LzmaRet Init(ref LzmaNextCoder next, ulong memlimit, bool picky)
        {
            if (next is not LzmaAloneDecoder coder)
            {
                coder = new LzmaAloneDecoder();
                next = coder;
            }

            coder.sequence = SEQ_PROPERTIES;
            coder.picky = picky;
            coder.pos = 0;
            coder.options.dict_size = 0;
            coder.options.preset_dict = null;
            coder.options.preset_dict_size = 0;
            coder.uncompressed_size = 0;
            coder.memlimit = Math.Max(1, memlimit);
            coder.memusage = LzmaConst.MEMUSAGE_BASE;

            return LzmaRet.OK;
        }

        /// <summary>lzma_alone_decoder</summary>
        internal static LzmaRet Create(LzmaStream strm, ulong memlimit)
        {
            var ret = LzmaCore.NextStrmInit(strm, next =>
            {
                var r = Init(ref next, memlimit, false);
                return (r, next);
            });
            if (ret != LzmaRet.OK)
                return ret;

            strm.supported_actions[(int)LzmaAction.RUN] = true;
            strm.supported_actions[(int)LzmaAction.FINISH] = true;

            return LzmaRet.OK;
        }
    }

    /// <summary>The lzma_auto_coder of common/auto_decoder.c</summary>
    internal sealed class LzmaAutoDecoder : LzmaNextCoder
    {
        private const int SEQ_INIT = 0, SEQ_CODE = 1, SEQ_FINISH = 2;

        // Stream decoder or LZMA_Alone decoder
        private LzmaNextCoder next;

        /// <summary>lzma_next_end of the coder behind this one.</summary>
        internal override void End()
        {
            next?.End();
            next = null;
        }

        private ulong memlimit;
        private uint flags;

        private int sequence;

        /// <summary>auto_decode</summary>
        internal override LzmaRet Code(byte[] input, ref int in_pos, int in_size,
            byte[] output, ref int out_pos, int out_size, LzmaAction action)
        {
            switch (sequence)
            {
                case SEQ_INIT:
                    {
                        if (in_pos >= in_size)
                            return LzmaRet.OK;

                        // Update the sequence now, because we want to continue from SEQ_CODE even if we return some LZMA_*_CHECK.
                        sequence = SEQ_CODE;

                        // Detect the file format. .xz files start with 0xFD which cannot be the first byte of .lzma (LZMA_Alone) format. The .lz format starts with 0x4C which could be the first byte of a .lzma file but luckily it would mean lc=3, lp=0, pb=2.
                        if (input[in_pos] == 0xFD)
                        {
                            var r = LzmaStreamDecoder.Init(ref next, memlimit, flags);
                            if (r != LzmaRet.OK)
                                return r;
                        }
                        else if (input[in_pos] == 0x4C)
                        {
                            var r = LzmaLzipDecoder.Init(ref next, memlimit, flags);
                            if (r != LzmaRet.OK)
                                return r;
                        }
                        else
                        {
                            var r = LzmaAloneDecoder.Init(ref next, memlimit, true);
                            if (r != LzmaRet.OK)
                                return r;

                            // If the application wants to know about missing integrity check or about the check in general, we need to handle it here, because LZMA_Alone decoder doesn't accept any flags.
                            if ((flags & LzmaConst.TELL_NO_CHECK) != 0)
                                return LzmaRet.NO_CHECK;

                            if ((flags & LzmaConst.TELL_ANY_CHECK) != 0)
                                return LzmaRet.GET_CHECK;
                        }

                        goto case SEQ_CODE;
                    }

                case SEQ_CODE:
                    {
                        var ret = next.Code(input, ref in_pos, in_size, output, ref out_pos, out_size, action);
                        if (ret != LzmaRet.STREAM_END || (flags & LzmaConst.CONCATENATED) == 0)
                            return ret;

                        sequence = SEQ_FINISH;
                        goto case SEQ_FINISH;
                    }

                case SEQ_FINISH:
                    // When LZMA_CONCATENATED was used and we were decoding a LZMA_Alone file, we need to check that there is no trailing garbage and wait for LZMA_FINISH.
                    if (in_pos < in_size)
                        return LzmaRet.DATA_ERROR;

                    return action == LzmaAction.FINISH ? LzmaRet.STREAM_END : LzmaRet.OK;

                default:
                    return LzmaRet.PROG_ERROR;
            }
        }

        /// <summary>auto_decoder_get_check</summary>
        internal override int GetCheck()
        {
            // It is LZMA_Alone if get_check is NULL.
            int c = next?.GetCheck() ?? -1;
            return c < 0 ? LzmaConst.CHECK_NONE : c;
        }

        internal override bool HasMemConfig => true;

        /// <summary>auto_decoder_memconfig</summary>
        internal override LzmaRet MemConfig(out ulong memusage, out ulong old_memlimit, ulong new_memlimit)
        {
            LzmaRet ret;

            if (next != null && next.HasMemConfig)
            {
                ret = next.MemConfig(out memusage, out old_memlimit, new_memlimit);
            }
            else
            {
                // No coder is configured yet. Use LZMA_MEMUSAGE_BASE as the current memory usage.
                memusage = LzmaConst.MEMUSAGE_BASE;
                old_memlimit = memlimit;

                ret = LzmaRet.OK;
                if (new_memlimit != 0 && new_memlimit < memusage)
                    ret = LzmaRet.MEMLIMIT_ERROR;
            }

            if (ret == LzmaRet.OK && new_memlimit != 0)
                memlimit = new_memlimit;

            return ret;
        }

        /// <summary>auto_decoder_init</summary>
        private static LzmaRet Init(ref LzmaNextCoder next, ulong memlimit, uint flags)
        {
            if ((flags & ~LzmaConst.SUPPORTED_FLAGS) != 0)
                return LzmaRet.OPTIONS_ERROR;

            if (next is not LzmaAutoDecoder coder)
            {
                coder = new LzmaAutoDecoder();
                next = coder;
            }

            coder.memlimit = Math.Max(1, memlimit);
            coder.flags = flags;
            coder.sequence = SEQ_INIT;

            return LzmaRet.OK;
        }

        /// <summary>lzma_auto_decoder</summary>
        internal static LzmaRet Create(LzmaStream strm, ulong memlimit, uint flags)
        {
            var ret = LzmaCore.NextStrmInit(strm, next =>
            {
                var r = Init(ref next, memlimit, flags);
                return (r, next);
            });
            if (ret != LzmaRet.OK)
                return ret;

            strm.supported_actions[(int)LzmaAction.RUN] = true;
            strm.supported_actions[(int)LzmaAction.FINISH] = true;

            return LzmaRet.OK;
        }
    }

    /// <summary>The lzma_lzip_coder of common/lzip_decoder.c (FORMAT_AUTO decodes .lz too since xz 5.4).</summary>
    internal sealed class LzmaLzipDecoder : LzmaNextCoder
    {
        // .lz member format version 0 does not have the member size field.
        private const int LZIP_V0_FOOTER_SIZE = 12, LZIP_V1_FOOTER_SIZE = 20, LZIP_FOOTER_SIZE_MAX = LZIP_V1_FOOTER_SIZE;

        // lc/lp/pb are hardcoded in the .lz format.
        private const uint LZIP_LC = 3, LZIP_LP = 0, LZIP_PB = 2;

        private const int SEQ_ID_STRING = 0, SEQ_VERSION = 1, SEQ_DICT_SIZE = 2, SEQ_CODER_INIT = 3,
            SEQ_LZMA_STREAM = 4, SEQ_MEMBER_FOOTER = 5;

        private static readonly byte[] lzip_id_string = { 0x4C, 0x5A, 0x49, 0x50 };
        private static readonly LzmaInitFunction lzma1_init = LzmaLzma1Decoder.DecoderInit;

        private int sequence;

        // .lz member format version
        private uint version;

        // CRC32 of the uncompressed data in the .lz member
        private uint crc32;

        // Uncompressed size of the .lz member
        private ulong uncompressed_size;

        // Compressed size of the .lz member
        private ulong member_size;

        // Memory usage limit
        private ulong memlimit;

        // Amount of memory actually needed
        private ulong memusage;

        private bool tell_any_check;
        private bool ignore_check;
        private bool concatenated;

        // When decoding concatenated .lz members, this is true as long as we are decoding the first .lz member.
        private bool first_member;

        // Reading position in the header and footer fields
        private int pos;

        // Buffer to hold the .lz footer fields
        private readonly byte[] buffer = new byte[LZIP_FOOTER_SIZE_MAX];

        // Options decoded from the .lz header that needed to initialize the LZMA1 decoder.
        private readonly LzmaOptionsLzma options = new();

        // LZMA1 decoder
        private LzmaNextCoder lzma_decoder;

        /// <summary>lzip_decoder_end</summary>
        internal override void End()
        {
            lzma_decoder?.End();
            lzma_decoder = null;
        }

        /// <summary>lzip_decode</summary>
        internal override LzmaRet Code(byte[] input, ref int in_pos, int in_size,
            byte[] output, ref int out_pos, int out_size, LzmaAction action)
        {
            while (true)
            {
                switch (sequence)
                {
                    case SEQ_ID_STRING:
                        {
                            // The "ID string" or magic bytes are "LZIP" in US-ASCII encoding.
                            while (pos < lzip_id_string.Length)
                            {
                                if (in_pos >= in_size)
                                {
                                    // If we are on the 2nd+ concatenated member and the input ends before we can read the magic bytes, we discard the bytes that were already read (up to 3) and finish.
                                    return !first_member && action == LzmaAction.FINISH ? LzmaRet.STREAM_END : LzmaRet.OK;
                                }

                                if (input[in_pos] != lzip_id_string[pos])
                                {
                                    // The .lz format allows putting non-.lz data at the end of the file.
                                    return !first_member ? LzmaRet.STREAM_END : LzmaRet.FORMAT_ERROR;
                                }

                                ++in_pos;
                                ++pos;
                            }

                            pos = 0;

                            crc32 = 0;
                            uncompressed_size = 0;
                            member_size = (ulong)lzip_id_string.Length;

                            sequence = SEQ_VERSION;
                            goto case SEQ_VERSION;
                        }

                    case SEQ_VERSION:
                        if (in_pos >= in_size)
                            return LzmaRet.OK;

                        version = input[in_pos++];

                        // We support version 0 and unextended version 1.
                        if (version > 1)
                            return LzmaRet.OPTIONS_ERROR;

                        ++member_size;
                        sequence = SEQ_DICT_SIZE;

                        // .lz versions 0 and 1 use CRC32 as the integrity check so if the application wanted to know that (like liblzma's own xz tool does) we tell it now.
                        if (tell_any_check)
                            return LzmaRet.GET_CHECK;

                        goto case SEQ_DICT_SIZE;

                    case SEQ_DICT_SIZE:
                        {
                            if (in_pos >= in_size)
                                return LzmaRet.OK;

                            uint ds = input[in_pos++];
                            ++member_size;

                            // The five lowest bits are for the base-2 logarithm of the dictionary size and the highest three bits are the fractional part (0/16 to 7/16) that will be subtracted to get the final value.
                            uint b2log = ds & 0x1F;
                            uint fracnum = ds >> 5;

                            // The format versions 0 and 1 allow dictionary size in the range [4 KiB, 512 MiB].
                            if (b2log < 12 || b2log > 29 || (b2log == 12 && fracnum > 0))
                                return LzmaRet.DATA_ERROR;

                            options.dict_size = (1U << (int)b2log) - (fracnum << (int)(b2log - 4));

                            options.preset_dict = null;
                            options.lc = LZIP_LC;
                            options.lp = LZIP_LP;
                            options.pb = LZIP_PB;

                            // Calculate the memory usage so that it is ready for SEQ_CODER_INIT.
                            memusage = LzmaLzma1Decoder.MemUsageNoCheck(options) + LzmaConst.MEMUSAGE_BASE;

                            sequence = SEQ_CODER_INIT;
                            goto case SEQ_CODER_INIT;
                        }

                    case SEQ_CODER_INIT:
                        {
                            if (memusage > memlimit)
                                return LzmaRet.MEMLIMIT_ERROR;

                            var filters = new LzmaFilterInfo[2];
                            filters[0].id = LzmaConst.FILTER_LZMA1;
                            filters[0].init = lzma1_init;
                            filters[0].options = options;
                            filters[1].init = null;

                            var r = LzmaCore.NextFilterInit(ref lzma_decoder, filters, 0);
                            if (r != LzmaRet.OK)
                                return r;

                            crc32 = 0;
                            sequence = SEQ_LZMA_STREAM;
                            goto case SEQ_LZMA_STREAM;
                        }

                    case SEQ_LZMA_STREAM:
                        {
                            int in_start = in_pos;
                            int out_start = out_pos;

                            var ret = lzma_decoder.Code(input, ref in_pos, in_size, output, ref out_pos, out_size, action);

                            int out_used = out_pos - out_start;

                            member_size += (ulong)(in_pos - in_start);
                            uncompressed_size += (ulong)out_used;

                            // Don't waste time updating the integrity check if it will be ignored.
                            if (!ignore_check && out_used > 0)
                                crc32 = LzmaCheckState.Crc32(output.AsSpan(out_start, out_used), crc32);

                            if (ret != LzmaRet.STREAM_END)
                                return ret;

                            sequence = SEQ_MEMBER_FOOTER;
                            goto case SEQ_MEMBER_FOOTER;
                        }

                    case SEQ_MEMBER_FOOTER:
                        {
                            // The footer of .lz version 0 lacks the Member size field.
                            int footer_size = version == 0 ? LZIP_V0_FOOTER_SIZE : LZIP_V1_FOOTER_SIZE;

                            // Copy the CRC32, Data size, and Member size fields to the internal buffer.
                            LzmaCore.BufCpy(input, ref in_pos, in_size, buffer, ref pos, footer_size);

                            // Return if we didn't get the whole footer yet.
                            if (pos < footer_size)
                                return LzmaRet.OK;

                            pos = 0;
                            member_size += (ulong)footer_size;

                            // Check that the CRC32 from the .lz file matches.
                            if (!ignore_check && crc32 != LzmaCore.Read32LE(buffer, 0))
                                return LzmaRet.DATA_ERROR;

                            // Check that the uncompressed size matches.
                            if (uncompressed_size != System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(4)))
                                return LzmaRet.DATA_ERROR;

                            // Check that the compressed size of the .lz member matches.
                            if (version > 0)
                            {
                                if (member_size != System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(12)))
                                    return LzmaRet.DATA_ERROR;
                            }

                            // Decoding is finished if we weren't requested to decode more than one .lz member.
                            if (!concatenated)
                                return LzmaRet.STREAM_END;

                            first_member = false;
                            sequence = SEQ_ID_STRING;
                            break;
                        }

                    default:
                        return LzmaRet.PROG_ERROR;
                }
            }
        }

        /// <summary>lzip_decoder_get_check</summary>
        internal override int GetCheck() => LzmaConst.CHECK_CRC32;

        internal override bool HasMemConfig => true;

        /// <summary>lzip_decoder_memconfig</summary>
        internal override LzmaRet MemConfig(out ulong memusage, out ulong old_memlimit, ulong new_memlimit)
        {
            memusage = this.memusage;
            old_memlimit = memlimit;

            if (new_memlimit != 0)
            {
                if (new_memlimit < this.memusage)
                    return LzmaRet.MEMLIMIT_ERROR;

                memlimit = new_memlimit;
            }

            return LzmaRet.OK;
        }

        /// <summary>lzma_lzip_decoder_init</summary>
        internal static LzmaRet Init(ref LzmaNextCoder next, ulong memlimit, uint flags)
        {
            if ((flags & ~LzmaConst.SUPPORTED_FLAGS) != 0)
                return LzmaRet.OPTIONS_ERROR;

            if (next is not LzmaLzipDecoder coder)
            {
                coder = new LzmaLzipDecoder();
                next = coder;
            }

            coder.sequence = SEQ_ID_STRING;
            coder.memlimit = Math.Max(1, memlimit);
            coder.memusage = LzmaConst.MEMUSAGE_BASE;
            coder.tell_any_check = (flags & LzmaConst.TELL_ANY_CHECK) != 0;
            coder.ignore_check = (flags & LzmaConst.IGNORE_CHECK) != 0;
            coder.concatenated = (flags & LzmaConst.CONCATENATED) != 0;
            coder.first_member = true;
            coder.pos = 0;

            return LzmaRet.OK;
        }
    }
}
