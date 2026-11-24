using System;

namespace SharpPy.Modules.Lzma
{
    /// <summary>lzma_dict (lz/lz_decoder.h): the history buffer the LZ-based decoders write into.</summary>
    internal sealed class LzDict
    {
        internal const int LZ_DICT_REPEAT_MAX = 288;
        internal const int LZ_DICT_INIT_POS = 2 * LZ_DICT_REPEAT_MAX;

        internal byte[] buf;
        internal int pos;
        internal int full;
        internal int limit;
        internal int size;
        internal bool has_wrapped;
        internal bool need_reset;

        /// <summary>dict_get</summary>
        internal byte Get(uint distance) =>
            buf[pos - (int)distance - 1 + (distance < (uint)pos ? 0 : size - LZ_DICT_REPEAT_MAX)];

        /// <summary>dict_get0</summary>
        internal byte Get0() => buf[pos - 1];

        /// <summary>dict_is_distance_valid</summary>
        internal bool IsDistanceValid(uint distance) => (uint)full > distance;

        /// <summary>dict_repeat: true when the output buffer filled before len bytes were copied.</summary>
        internal bool Repeat(uint distance, ref uint len)
        {
            // Don't write past the end of the dictionary.
            int dict_avail = limit - pos;
            uint left = Math.Min((uint)dict_avail, len);
            len -= left;

            int back = pos - (int)distance - 1;
            if (distance >= (uint)pos)
                back += size - LZ_DICT_REPEAT_MAX;

            if (distance < left)
            {
                // Source and target areas overlap, thus we can't use memcpy() nor even memmove() safely.
                var b = buf;
                int p = pos;
                do
                {
                    b[p++] = b[back++];
                } while (--left > 0);
                pos = p;
            }
            else
            {
                Buffer.BlockCopy(buf, back, buf, pos, (int)left);
                pos += (int)left;
            }

            // Update how full the dictionary is.
            if (!has_wrapped)
                full = pos - LZ_DICT_INIT_POS;

            return len != 0;
        }

        /// <summary>dict_put</summary>
        internal void Put(byte b)
        {
            buf[pos++] = b;
            if (!has_wrapped)
                full = pos - LZ_DICT_INIT_POS;
        }

        /// <summary>dict_write: copies uncompressed data (LZMA2's uncompressed chunks).</summary>
        internal void Write(byte[] input, ref int in_pos, int in_size, ref int left)
        {
            // Don't write past the end of the dictionary.
            if (in_size - in_pos > left)
                in_size = in_pos + left;

            left -= LzmaCore.BufCpy(input, ref in_pos, in_size, buf, ref pos, limit);

            if (!has_wrapped)
                full = pos - LZ_DICT_INIT_POS;
        }

        /// <summary>dict_reset</summary>
        internal void Reset() => need_reset = true;
    }

    /// <summary>lzma_lz_decoder: the LZ-based decoder (LZMA, LZMA2) behind the dictionary.</summary>
    internal interface ILzDecoder
    {
        LzmaRet Code(LzDict dict, byte[] input, ref int in_pos, int in_size);
        void Reset(LzmaOptionsLzma options);
        void SetUncompressed(ulong uncompressed_size, bool allow_eopm);
    }

    /// <summary>The lzma_coder of lz/lz_decoder.c.</summary>
    internal sealed class LzDecoderCoder : LzmaNextCoder
    {
        // Dictionary (history buffer)
        internal readonly LzDict dict = new();

        // The actual LZ-based decoder e.g. LZMA
        internal ILzDecoder lz;

        // Next filter in the chain, if any.
        internal LzmaNextCoder next;

        /// <summary>lz_encoder_end / lz_decoder_end: the buffers go back to the pool.</summary>
        internal override void End()
        {
            next?.End();
            next = null;
            ReleaseBuffers();
        }

        // True if the next filter in the chain has returned LZMA_STREAM_END.
        private bool next_finished;

        // True if the LZ decoder (e.g. LZMA) has detected end of payload marker.
        private bool this_finished;

        // Temporary buffer needed when the LZ-based filter is not the last filter in the chain.
        private int temp_pos;
        private int temp_size;
        private readonly byte[] temp_buffer = new byte[LzmaConst.BUFFER_SIZE];

        // lzma_free(coder->dict.buf)
        private void ReleaseBuffers()
        {
            CodecBufferPool.Return(dict.buf);
            dict.buf = null;
            dict.size = 0;
        }

        /// <summary>lz_decoder_reset</summary>
        private void DecoderReset()
        {
            dict.pos = LzDict.LZ_DICT_INIT_POS;
            dict.full = 0;
            dict.buf[LzDict.LZ_DICT_INIT_POS - 1] = 0;
            dict.has_wrapped = false;
            dict.need_reset = false;
        }

        /// <summary>decode_buffer</summary>
        private LzmaRet DecodeBuffer(byte[] input, ref int in_pos, int in_size, byte[] output, ref int out_pos, int out_size)
        {
            while (true)
            {
                // Wrap the dictionary if needed.
                if (dict.pos == dict.size)
                {
                    // See the comment of #define LZ_DICT_REPEAT_MAX.
                    dict.pos = LzDict.LZ_DICT_REPEAT_MAX;
                    dict.has_wrapped = true;
                    Buffer.BlockCopy(dict.buf, dict.size - LzDict.LZ_DICT_REPEAT_MAX, dict.buf, 0, LzDict.LZ_DICT_REPEAT_MAX);
                }

                // Store the current dictionary position. It is needed to know where to start copying to the out[] buffer.
                int dict_start = dict.pos;

                // Calculate how much we allow coder->lz.code() to decode.
                dict.limit = dict.pos + Math.Min(out_size - out_pos, dict.size - dict.pos);

                // Call the coder->lz.code() to do the actual decoding.
                var ret = lz.Code(dict, input, ref in_pos, in_size);

                // Copy the decoded data from the dictionary to the out[] buffer.
                int copy_size = dict.pos - dict_start;
                if (copy_size > 0)
                    Buffer.BlockCopy(dict.buf, dict_start, output, out_pos, copy_size);

                out_pos += copy_size;

                // Reset the dictionary if so requested by coder->lz.code().
                if (dict.need_reset)
                {
                    DecoderReset();

                    // Since we reset dictionary, we don't check if dictionary became full.
                    if (ret != LzmaRet.OK || out_pos == out_size)
                        return ret;
                }
                else
                {
                    // Return if everything got decoded or an error occurred, or if there's not enough data in the dictionary.
                    if (ret != LzmaRet.OK || out_pos == out_size || dict.pos < dict.size)
                        return ret;
                }
            }
        }

        /// <summary>lz_decode</summary>
        internal override LzmaRet Code(byte[] input, ref int in_pos, int in_size,
            byte[] output, ref int out_pos, int out_size, LzmaAction action)
        {
            if (next == null)
                return DecodeBuffer(input, ref in_pos, in_size, output, ref out_pos, out_size);

            // We aren't the last coder in the chain, we need to decode our input to a temporary buffer.
            while (out_pos < out_size)
            {
                // Fill the temporary buffer if it is empty.
                if (!next_finished && temp_pos == temp_size)
                {
                    temp_pos = 0;
                    temp_size = 0;

                    var r = next.Code(input, ref in_pos, in_size, temp_buffer, ref temp_size, LzmaConst.BUFFER_SIZE, action);

                    if (r == LzmaRet.STREAM_END)
                        next_finished = true;
                    else if (r != LzmaRet.OK || temp_size == 0)
                        return r;
                }

                if (this_finished)
                {
                    if (temp_size != 0)
                        return LzmaRet.DATA_ERROR;

                    if (next_finished)
                        return LzmaRet.STREAM_END;

                    return LzmaRet.OK;
                }

                var ret = DecodeBuffer(temp_buffer, ref temp_pos, temp_size, output, ref out_pos, out_size);

                if (ret == LzmaRet.STREAM_END)
                    this_finished = true;
                else if (ret != LzmaRet.OK)
                    return ret;
                else if (next_finished && out_pos < out_size)
                    return LzmaRet.DATA_ERROR;
            }

            return LzmaRet.OK;
        }

        /// <summary>lzma_lz_decoder_init</summary>
        internal static LzmaRet DecoderInit(ref LzmaNextCoder next, LzmaFilterInfo[] filters, int index,
            Func<LzDecoderCoder, ulong, object, (LzmaRet ret, uint dict_size, byte[] preset_dict, uint preset_dict_size)> lz_init)
        {
            if (next is not LzDecoderCoder coder)
            {
                coder = new LzDecoderCoder();
                next = coder;
            }
            coder.InitKey = filters[index].init;

            // Allocate and initialize the LZ-based decoder.
            var (ret, dict_size, preset_dict, preset_dict_size) = lz_init(coder, filters[index].id, filters[index].options);
            if (ret != LzmaRet.OK)
                return ret;

            // If the dictionary size is very small, increase it to 4096 bytes.
            if (dict_size < 4096)
                dict_size = 4096;

            // Make dictionary size a multiple of 16.
            if (dict_size > int.MaxValue - 15 - 2 * LzDict.LZ_DICT_REPEAT_MAX)
                return LzmaRet.MEM_ERROR;

            dict_size = (dict_size + 15) & ~15u;

            // Reserve 2 * LZ_DICT_REPEAT_MAX bytes of extra space as explained in the comments of #define LZ_DICT_REPEAT_MAX.
            int alloc_size = (int)dict_size + 2 * LzDict.LZ_DICT_REPEAT_MAX;

            // Allocate and initialize the dictionary.
            if (coder.dict.size != alloc_size)
            {
                CodecBufferPool.Return(coder.dict.buf);
                coder.dict.buf = null;
                try
                {
                    coder.dict.buf = CodecBufferPool.RentBytes(alloc_size);
                }
                catch (OutOfMemoryException)
                {
                    return LzmaRet.MEM_ERROR;
                }
                coder.dict.size = alloc_size;
            }

            coder.DecoderReset();

            // Use the preset dictionary if it was given to us.
            if (preset_dict != null && preset_dict_size > 0)
            {
                // If the preset dictionary is bigger than the actual dictionary, copy only the tail.
                int copy_size = (int)Math.Min(preset_dict_size, dict_size);
                int offset = (int)preset_dict_size - copy_size;
                Buffer.BlockCopy(preset_dict, offset, coder.dict.buf, coder.dict.pos, copy_size);
                coder.dict.pos += copy_size;
                coder.dict.full = copy_size;
            }

            // Miscellaneous initializations
            coder.next_finished = false;
            coder.this_finished = false;
            coder.temp_pos = 0;
            coder.temp_size = 0;

            // Initialize the next filter in the chain, if any.
            return LzmaCore.NextFilterInit(ref coder.next, filters, index + 1);
        }

        /// <summary>lzma_lz_decoder_memusage</summary>
        internal static ulong MemUsage(ulong dictionary_size) =>
            // sizeof(lzma_coder) + dictionary_size + 2 * LZ_DICT_REPEAT_MAX + LZ_DICT_EXTRA
            4096 + 160 + dictionary_size + 2 * LzDict.LZ_DICT_REPEAT_MAX;
    }

    /// <summary>
    /// The LZMA decoder (lzma/lzma_decoder.c of xz 5.8.3): the same probabilities, states, range
    /// decoder and checks. liblzma resumes inside a symbol when the input runs out, through a
    /// sequence enum and a case label per range-decoder call; C# cannot jump into a loop, so here a
    /// symbol is decoded whole: when fewer than LZMA_IN_REQUIRED bytes are left, the probabilities
    /// it changes are logged and put back if the input ends inside it, and the unconsumed bytes wait
    /// in a small buffer for the next call. As in liblzma, the next symbol is decoded even when the
    /// output is full (SEQ_LITERAL_WRITE, SEQ_SHORTREP, SEQ_COPY keep what it still has to write),
    /// so the output and the input consumed by each call are the same.
    /// </summary>
    internal sealed class LzmaLzma1Decoder : ILzDecoder
    {
        // Maximum number of input bytes one LZMA symbol can take.
        private const int LZMA_IN_REQUIRED = 20;

        private const int SYM_LITERAL = 0, SYM_MATCH = 1, SYM_SHORTREP = 2, SYM_REP = 3, SYM_EOPM = 4, SYM_ERROR = 5;

        private readonly ushort[] P = new ushort[LzmaProbs.TOTAL];

        // lzma_range_decoder
        private uint range;
        private uint code;
        private uint init_bytes_left;

        // State
        private uint state, rep0, rep1, rep2, rep3;
        private uint pos_mask;
        private uint literal_context_bits;
        private uint literal_mask;

        // Uncompressed size as bytes, or LZMA_VLI_UNKNOWN if end of payload marker is expected.
        private ulong uncompressed_size;

        // True if end of payload marker (EOPM) is allowed even when uncompressed_size is known.
        private bool allow_eopm;

        // SEQ_COPY: what is left of a match the output had no room for
        private uint len;

        // SEQ_LITERAL_WRITE / SEQ_SHORTREP: the byte of a decoded symbol the output had no room for
        private bool pending_byte;
        private byte pending_value;

        // The input of a symbol that ended inside it, kept for the next call.
        private readonly byte[] temp = new byte[LZMA_IN_REQUIRED];
        private int temp_len;

        // The source of the symbol being decoded and whether it ran out.
        private byte[] src;
        private int spos, send;
        private bool starved;
        private bool log_undo;
        private readonly int[] undo_index = new int[64];
        private readonly ushort[] undo_value = new ushort[64];
        private int undo_count;

        // The decoded symbol, committed only when it was complete.
        private uint s_state, s_rep0, s_rep1, s_rep2, s_rep3, s_len;
        private byte s_byte;

        /// <summary>rc_reset</summary>
        private void RcReset()
        {
            range = uint.MaxValue;
            code = 0;
            init_bytes_left = 5;
        }

        /// <summary>rc_read_init</summary>
        private LzmaRet RcReadInit(byte[] input, ref int in_pos, int in_size)
        {
            while (init_bytes_left > 0)
            {
                if (in_pos == in_size)
                    return LzmaRet.OK;

                // The first byte is always 0x00.
                if (init_bytes_left == 5 && input[in_pos] != 0x00)
                    return LzmaRet.DATA_ERROR;

                code = (code << 8) | input[in_pos];
                ++in_pos;
                --init_bytes_left;
            }

            return LzmaRet.STREAM_END;
        }

        // The next input byte of the symbol; zero, and the symbol marked incomplete, past the end.
        private uint NextByte()
        {
            if (spos < send)
                return src[spos++];
            starved = true;
            return 0;
        }

        // rc_normalize
        private void Normalize()
        {
            if (range < LzmaProbs.RC_TOP_VALUE)
            {
                range <<= LzmaProbs.RC_SHIFT_BITS;
                code = (code << LzmaProbs.RC_SHIFT_BITS) | NextByte();
            }
        }

        // rc_bit: one bit with the probability at P[index], which it updates.
        private uint Bit(int index)
        {
            Normalize();
            uint prob = P[index];
            uint bound = (range >> LzmaProbs.RC_BIT_MODEL_TOTAL_BITS) * prob;
            if (log_undo)
            {
                undo_index[undo_count] = index;
                undo_value[undo_count++] = (ushort)prob;
            }
            if (code < bound)
            {
                range = bound;
                P[index] = (ushort)(prob + ((LzmaProbs.RC_BIT_MODEL_TOTAL - prob) >> LzmaProbs.RC_MOVE_BITS));
                return 0;
            }
            range -= bound;
            code -= bound;
            P[index] = (ushort)(prob - (prob >> LzmaProbs.RC_MOVE_BITS));
            return 1;
        }

        // rc_bittree
        private uint BitTree(int probs, int bit_levels)
        {
            uint symbol = 1;
            for (int i = 0; i < bit_levels; i++)
                symbol = (symbol << 1) | Bit(probs + (int)symbol);
            return symbol - (1U << bit_levels);
        }

        // rc_bittree_reverse
        private uint BitTreeReverse(int probs, int bit_levels)
        {
            uint symbol = 1;
            uint result = 0;
            for (int i = 0; i < bit_levels; i++)
            {
                uint b = Bit(probs + (int)symbol);
                symbol = (symbol << 1) | b;
                result |= b << i;
            }
            return result;
        }

        // rc_direct
        private uint Direct(uint dest, int count)
        {
            do
            {
                Normalize();
                range >>= 1;
                code -= range;
                uint t = 0U - (code >> 31);
                code += range & t;
                dest = (dest << 1) + (t + 1);
            } while (--count > 0);
            return dest;
        }

        // len_decode
        private uint LenDecode(int coder, uint pos_state)
        {
            if (Bit(coder + LzmaProbs.LEN_CHOICE) == 0)
                return BitTree(coder + LzmaProbs.LEN_LOW + (int)pos_state * LzmaProbs.LEN_LOW_SYMBOLS, LzmaProbs.LEN_LOW_BITS)
                    + LzmaProbs.MATCH_LEN_MIN;
            if (Bit(coder + LzmaProbs.LEN_CHOICE2) == 0)
                return BitTree(coder + LzmaProbs.LEN_MID + (int)pos_state * LzmaProbs.LEN_MID_SYMBOLS, LzmaProbs.LEN_MID_BITS)
                    + LzmaProbs.MATCH_LEN_MIN + LzmaProbs.LEN_LOW_SYMBOLS;
            return BitTree(coder + LzmaProbs.LEN_HIGH, LzmaProbs.LEN_HIGH_BITS)
                + LzmaProbs.MATCH_LEN_MIN + LzmaProbs.LEN_LOW_SYMBOLS + LzmaProbs.LEN_MID_SYMBOLS;
        }

        /// <summary>
        /// One LZMA symbol into the s_* fields. eopm_is_valid as in lzma_decode; an EOPM is
        /// followed by the normalization that checks the range decoder is finished.
        /// </summary>
        private int DecodeSymbol(LzDict dict, bool eopm_is_valid)
        {
            uint pos_state = (uint)dict.pos & pos_mask;
            s_state = state; s_rep0 = rep0; s_rep1 = rep1; s_rep2 = rep2; s_rep3 = rep3;

            if (Bit(LzmaProbs.IS_MATCH + (int)s_state * LzmaProbs.POS_STATES_MAX + (int)pos_state) == 0)
            {
                // Decode literal.
                int probs = LzmaProbs.LiteralSubcoder(literal_context_bits, literal_mask, (uint)dict.pos, dict.Get0());
                uint symbol = 1;

                if (LzmaProbs.IsLiteralState(s_state))
                {
                    s_state = LzmaProbs.UpdateLiteralNormal(s_state);
                    do
                        symbol = (symbol << 1) | Bit(probs + (int)symbol);
                    while (symbol < (1U << 8));
                }
                else
                {
                    // Decode literal with match byte.
                    s_state = LzmaProbs.UpdateLiteralMatched(s_state);
                    uint match_byte = dict.Get(s_rep0);
                    uint offset = 0x100;
                    do
                    {
                        match_byte <<= 1;
                        uint match_bit = match_byte & offset;
                        uint b = Bit(probs + (int)(offset + match_bit + symbol));
                        symbol = (symbol << 1) | b;
                        offset &= b != 0 ? match_bit : ~match_bit;
                    } while (symbol < (1U << 8));
                }

                s_byte = (byte)symbol;
                return SYM_LITERAL;
            }

            if (Bit(LzmaProbs.IS_REP + (int)s_state) == 0)
            {
                // Match: the distance is a new one.
                s_state = LzmaProbs.UpdateMatch(s_state);

                s_rep3 = s_rep2;
                s_rep2 = s_rep1;
                s_rep1 = s_rep0;

                s_len = LenDecode(LzmaProbs.MATCH_LEN_CODER, pos_state);

                uint dist_state = LzmaProbs.GetDistState(s_len);
                uint symbol = BitTree(LzmaProbs.DIST_SLOT + (int)dist_state * LzmaProbs.DIST_SLOTS, LzmaProbs.DIST_SLOT_BITS);

                if (symbol < LzmaProbs.DIST_MODEL_START)
                {
                    // Match distances [0, 3] have only two bits.
                    s_rep0 = symbol;
                }
                else
                {
                    // Decode the lowest [1, 29] bits of the match distance.
                    int limit = (int)((symbol >> 1) - 1);
                    s_rep0 = 2 + (symbol & 1);

                    if (symbol < LzmaProbs.DIST_MODEL_END)
                    {
                        // Prepare to decode the low bits for a distance of [4, 127].
                        s_rep0 <<= limit;
                        s_rep0 += BitTreeReverse(LzmaProbs.DIST_SPECIAL + (int)s_rep0 - (int)symbol - 1, limit);
                    }
                    else
                    {
                        // The distance is >= 128. Decode the lower bits without probabilities except the lowest four bits.
                        s_rep0 = Direct(s_rep0, limit - LzmaProbs.ALIGN_BITS);
                        s_rep0 <<= LzmaProbs.ALIGN_BITS;
                        s_rep0 += BitTreeReverse(LzmaProbs.DIST_ALIGN, LzmaProbs.ALIGN_BITS);

                        if (s_rep0 == uint.MaxValue)
                        {
                            // End of payload marker was found.
                            if (!eopm_is_valid)
                                return SYM_ERROR;
                            // LZMA1 stream with end-of-payload marker.
                            Normalize();
                            return SYM_EOPM;
                        }
                    }
                }

                // Validate the distance we just decoded.
                if (!dict.IsDistanceValid(s_rep0))
                    return SYM_ERROR;

                return SYM_MATCH;
            }

            // Repeated match
            if (!dict.IsDistanceValid(0))
                return SYM_ERROR;

            if (Bit(LzmaProbs.IS_REP0 + (int)s_state) == 0)
            {
                if (Bit(LzmaProbs.IS_REP0_LONG + (int)s_state * LzmaProbs.POS_STATES_MAX + (int)pos_state) == 0)
                {
                    // Short repeated match: a single byte at distance rep0.
                    s_state = LzmaProbs.UpdateShortRep(s_state);
                    return SYM_SHORTREP;
                }
            }
            else
            {
                if (Bit(LzmaProbs.IS_REP1 + (int)s_state) == 0)
                {
                    uint distance = s_rep1;
                    s_rep1 = s_rep0;
                    s_rep0 = distance;
                }
                else if (Bit(LzmaProbs.IS_REP2 + (int)s_state) == 0)
                {
                    uint distance = s_rep2;
                    s_rep2 = s_rep1;
                    s_rep1 = s_rep0;
                    s_rep0 = distance;
                }
                else
                {
                    uint distance = s_rep3;
                    s_rep3 = s_rep2;
                    s_rep2 = s_rep1;
                    s_rep1 = s_rep0;
                    s_rep0 = distance;
                }
            }

            s_state = LzmaProbs.UpdateLongRep(s_state);
            s_len = LenDecode(LzmaProbs.REP_LEN_CODER, pos_state);
            return SYM_REP;
        }

        // Put back what the logged symbol changed.
        private void Undo()
        {
            for (int i = undo_count - 1; i >= 0; i--)
                P[undo_index[i]] = undo_value[i];
        }

        /// <summary>
        /// One symbol from the kept bytes and the input, or from the input alone. -1 when the input
        /// ended inside it (all of it then waits in temp and in_pos is at in_size).
        /// </summary>
        private int DecodeFromInput(LzDict dict, bool eopm_is_valid, byte[] input, ref int in_pos, int in_size)
        {
            int sym;
            if (temp_len > 0)
            {
                int add = Math.Min(LZMA_IN_REQUIRED - temp_len, in_size - in_pos);
                Buffer.BlockCopy(input, in_pos, temp, temp_len, add);
                int avail = temp_len + add;
                uint save_range = range, save_code = code;
                src = temp; spos = 0; send = avail;
                starved = false;
                log_undo = avail < LZMA_IN_REQUIRED;
                undo_count = 0;
                sym = DecodeSymbol(dict, eopm_is_valid);
                if (starved)
                {
                    Undo();
                    range = save_range;
                    code = save_code;
                    temp_len = avail;
                    in_pos += add;
                    return -1;
                }
                int consumed = spos;
                if (consumed >= temp_len)
                {
                    in_pos += consumed - temp_len;
                    temp_len = 0;
                }
                else
                {
                    Buffer.BlockCopy(temp, consumed, temp, 0, temp_len - consumed);
                    temp_len -= consumed;
                }
            }
            else
            {
                uint save_range = range, save_code = code;
                src = input; spos = in_pos; send = in_size;
                starved = false;
                log_undo = in_size - in_pos < LZMA_IN_REQUIRED;
                undo_count = 0;
                sym = DecodeSymbol(dict, eopm_is_valid);
                if (starved)
                {
                    Undo();
                    range = save_range;
                    code = save_code;
                    temp_len = in_size - in_pos;
                    Buffer.BlockCopy(input, in_pos, temp, 0, temp_len);
                    in_pos = in_size;
                    return -1;
                }
                in_pos = spos;
            }
            return sym;
        }

        /// <summary>
        /// rc_normalize_safe at the point where the stream may end: false when the input has no byte
        /// for it yet.
        /// </summary>
        private bool NormalizeSafe(byte[] input, ref int in_pos, int in_size)
        {
            if (range >= LzmaProbs.RC_TOP_VALUE)
                return true;
            byte b;
            if (temp_len > 0)
            {
                b = temp[0];
                Buffer.BlockCopy(temp, 1, temp, 0, temp_len - 1);
                temp_len--;
            }
            else if (in_pos < in_size)
            {
                b = input[in_pos++];
            }
            else
            {
                return false;
            }
            range <<= LzmaProbs.RC_SHIFT_BITS;
            code = (code << LzmaProbs.RC_SHIFT_BITS) | b;
            return true;
        }

        /// <summary>lzma_decode</summary>
        public LzmaRet Code(LzDict dict, byte[] input, ref int in_pos, int in_size)
        {
            // Initialization
            {
                var r = RcReadInit(input, ref in_pos, in_size);
                if (r != LzmaRet.STREAM_END)
                    return r;
            }

            int dict_start = dict.pos;
            int saved_limit = dict.limit;
            LzmaRet ret = LzmaRet.OK;

            // This is true when the next LZMA symbol is allowed to be EOPM.
            bool eopm_is_valid = uncompressed_size == LzmaConst.VLI_UNKNOWN;

            // If uncompressed size is known and there is enough output space to decode all the data,
            // limit the available buffer space so that the main loop won't try to decode past the end.
            bool might_finish_without_eopm = false;
            if (uncompressed_size != LzmaConst.VLI_UNKNOWN && uncompressed_size <= (ulong)(dict.limit - dict.pos))
            {
                dict.limit = dict.pos + (int)uncompressed_size;
                might_finish_without_eopm = true;
            }

            while (true)
            {
                // SEQ_LITERAL_WRITE, SEQ_SHORTREP: a decoded byte the output had no room for.
                if (pending_byte)
                {
                    if (dict.pos == dict.limit)
                        break;
                    dict.Put(pending_value);
                    pending_byte = false;
                }

                // SEQ_COPY: repeat from history buffer.
                if (len > 0)
                {
                    if (dict.Repeat(rep0, ref len))
                        break;
                }

                // Like liblzma, the next symbol is decoded even when the output is full; it is
                // written on the next call.
                if (might_finish_without_eopm && dict.pos == dict.limit)
                {
                    // In rare cases there is a useless byte that needs to be read anyway.
                    if (!NormalizeSafe(input, ref in_pos, in_size))
                        break;

                    // If the range decoder state is such that we can be at the end of the LZMA stream, then the decoding is finished.
                    if (code == 0)
                    {
                        ret = LzmaRet.STREAM_END;
                        break;
                    }

                    // If the caller hasn't allowed EOPM to be present together with known uncompressed size, then the LZMA stream is corrupt.
                    if (!allow_eopm)
                    {
                        ret = LzmaRet.DATA_ERROR;
                        break;
                    }

                    // Otherwise continue decoding with the expectation that the next LZMA symbol is EOPM.
                    eopm_is_valid = true;
                }

                int sym = DecodeFromInput(dict, eopm_is_valid, input, ref in_pos, in_size);
                if (sym < 0)
                    break;

                if (sym == SYM_ERROR)
                {
                    ret = LzmaRet.DATA_ERROR;
                    break;
                }

                if (sym == SYM_EOPM)
                {
                    ret = code == 0 ? LzmaRet.STREAM_END : LzmaRet.DATA_ERROR;
                    break;
                }

                state = s_state;
                rep0 = s_rep0; rep1 = s_rep1; rep2 = s_rep2; rep3 = s_rep3;

                switch (sym)
                {
                    case SYM_LITERAL:
                        pending_value = s_byte;
                        pending_byte = true;
                        break;
                    case SYM_SHORTREP:
                        pending_value = dict.Get(rep0);
                        pending_byte = true;
                        break;
                    default:
                        len = s_len;
                        break;
                }
            }

            dict.limit = saved_limit;

            // Update the remaining amount of uncompressed data if uncompressed size was known.
            if (uncompressed_size != LzmaConst.VLI_UNKNOWN)
            {
                uncompressed_size -= (ulong)(dict.pos - dict_start);

                // If we have gotten all the output but the decoder wants to write more output, the file is corrupt.
                if (uncompressed_size == 0 && ret == LzmaRet.OK && (len > 0 || pending_byte))
                    ret = LzmaRet.DATA_ERROR;
            }

            if (ret == LzmaRet.STREAM_END)
            {
                // Reset the range decoder so that it is ready to reinitialize for a new LZMA2 chunk.
                RcReset();
                temp_len = 0;
            }

            return ret;
        }

        /// <summary>lzma_decoder_uncompressed</summary>
        public void SetUncompressed(ulong uncompressed_size, bool allow_eopm)
        {
            this.uncompressed_size = uncompressed_size;
            this.allow_eopm = allow_eopm;
        }

        /// <summary>lzma_decoder_reset</summary>
        public void Reset(LzmaOptionsLzma options)
        {
            // NOTE: We assume that lc/lp/pb are valid since they were successfully decoded with lzma_lzma_decode_properties().
            pos_mask = (1U << (int)options.pb) - 1;

            // Initialize the literal decoder.
            Array.Fill(P, LzmaProbs.BIT_RESET, LzmaProbs.LITERAL, (int)LzmaProbs.LITERAL_CODER_SIZE << (int)(options.lc + options.lp));

            literal_context_bits = options.lc;
            literal_mask = LzmaProbs.LiteralMaskCalc(options.lc, options.lp);

            // State
            state = 0;
            rep0 = 0;
            rep1 = 0;
            rep2 = 0;
            rep3 = 0;

            // Range decoder
            RcReset();

            // Bit and bittree decoders
            Array.Fill(P, LzmaProbs.BIT_RESET, LzmaProbs.IS_MATCH, LzmaProbs.TOTAL - LzmaProbs.IS_MATCH);

            // Initialize the next decoder state.
            len = 0;
            pending_byte = false;
            temp_len = 0;
        }

        /// <summary>lzma_lzma_decoder_create</summary>
        internal static (LzmaRet, uint, byte[], uint) Create(ref LzmaLzma1Decoder coder, LzmaOptionsLzma options)
        {
            coder ??= new LzmaLzma1Decoder();
            return (LzmaRet.OK, options.dict_size, options.preset_dict, options.preset_dict_size);
        }

        /// <summary>lzma_decoder_init</summary>
        private static (LzmaRet, uint, byte[], uint) Init(LzDecoderCoder lz, ulong id, object options)
        {
            var opt = (LzmaOptionsLzma)options;
            if (!LzmaProbs.IsLcLpPbValid(opt))
                return (LzmaRet.PROG_ERROR, 0, null, 0);

            // If the id is LZMA_FILTER_LZMA1EXT, the uncompressed size and EOPM come from the options.
            ulong uncomp_size = LzmaConst.VLI_UNKNOWN;
            bool allow_eopm = true;

            if (id == LzmaConst.FILTER_LZMA1EXT)
            {
                // Extended flags must be known.
                if ((opt.ext_flags & ~LzmaConst.LZMA1EXT_ALLOW_EOPM) != 0)
                    return (LzmaRet.OPTIONS_ERROR, 0, null, 0);

                // FIXME? Using lzma_vli instead of uint64_t.
                uncomp_size = opt.ext_size_low + ((ulong)opt.ext_size_high << 32);
                allow_eopm = (opt.ext_flags & LzmaConst.LZMA1EXT_ALLOW_EOPM) != 0 || uncomp_size == LzmaConst.VLI_UNKNOWN;
            }

            var coder = lz.lz as LzmaLzma1Decoder;
            var result = Create(ref coder, opt);
            lz.lz = coder;
            coder.Reset(opt);
            coder.SetUncompressed(uncomp_size, allow_eopm);
            return result;
        }

        /// <summary>lzma_lzma_decoder_init</summary>
        internal static LzmaRet DecoderInit(ref LzmaNextCoder next, LzmaFilterInfo[] filters, int index) =>
            LzDecoderCoder.DecoderInit(ref next, filters, index, Init);

        /// <summary>lzma_lzma_decoder_memusage_nocheck</summary>
        internal static ulong MemUsageNoCheck(LzmaOptionsLzma opt) =>
            // sizeof(lzma_lzma1_decoder)
            28672 + LzDecoderCoder.MemUsage(opt.dict_size);

        /// <summary>lzma_lzma_decoder_memusage</summary>
        internal static ulong MemUsage(object options)
        {
            if (options is not LzmaOptionsLzma opt || !LzmaProbs.IsLcLpPbValid(opt))
                return ulong.MaxValue;
            return MemUsageNoCheck(opt);
        }

        /// <summary>lzma_lzma_props_decode</summary>
        internal static LzmaRet PropsDecode(out object options, byte[] props, int at, int props_size)
        {
            options = null;
            if (props_size != 5)
                return LzmaRet.OPTIONS_ERROR;

            var opt = new LzmaOptionsLzma();
            if (LzmaProbs.LcLpPbDecode(opt, props[at]))
                return LzmaRet.OPTIONS_ERROR;

            // All dictionary sizes are accepted, including zero.
            opt.dict_size = LzmaCore.Read32LE(props, at + 1);

            opt.preset_dict = null;
            opt.preset_dict_size = 0;

            options = opt;
            return LzmaRet.OK;
        }
    }

    /// <summary>lzma/lzma2_decoder.c</summary>
    internal sealed class LzmaLzma2Decoder : ILzDecoder
    {
        private const int SEQ_CONTROL = 0, SEQ_UNCOMPRESSED_1 = 1, SEQ_UNCOMPRESSED_2 = 2, SEQ_COMPRESSED_0 = 3,
            SEQ_COMPRESSED_1 = 4, SEQ_PROPERTIES = 5, SEQ_LZMA = 6, SEQ_COPY = 7;

        private int sequence;

        // Sequence after the size fields have been decoded.
        private int next_sequence;

        // LZMA decoder
        private LzmaLzma1Decoder lzma;

        // Uncompressed size of LZMA chunk
        private uint uncompressed_size;

        // Compressed size of the chunk (naturally equals to uncompressed size of uncompressed chunk)
        private int compressed_size;

        // True if properties are needed. This is false before the first LZMA chunk.
        private bool need_properties;

        // True if dictionary reset is needed. This is false before the first chunk (LZMA or uncompressed).
        private bool need_dictionary_reset;

        private LzmaOptionsLzma options = new();

        /// <summary>lzma2_decode</summary>
        public LzmaRet Code(LzDict dict, byte[] input, ref int in_pos, int in_size)
        {
            // With SEQ_LZMA it is possible that no new input is needed to do some progress.
            while (in_pos < in_size || sequence == SEQ_LZMA)
            {
                switch (sequence)
                {
                    case SEQ_CONTROL:
                        {
                            uint control = input[in_pos];
                            ++in_pos;

                            // End marker
                            if (control == 0x00)
                                return LzmaRet.STREAM_END;

                            if (control >= 0xE0 || control == 1)
                            {
                                // Dictionary reset implies that next LZMA chunk has to set new properties.
                                need_properties = true;
                                need_dictionary_reset = true;
                            }
                            else if (need_dictionary_reset)
                            {
                                return LzmaRet.DATA_ERROR;
                            }

                            if (control >= 0x80)
                            {
                                // LZMA chunk. The highest five bits of the uncompressed size are taken from the control byte.
                                uncompressed_size = (control & 0x1F) << 16;
                                sequence = SEQ_UNCOMPRESSED_1;

                                // See if there are new properties or if we need to reset the state.
                                if (control >= 0xC0)
                                {
                                    // When there are new properties, state reset is done at SEQ_PROPERTIES.
                                    need_properties = false;
                                    next_sequence = SEQ_PROPERTIES;
                                }
                                else if (need_properties)
                                {
                                    return LzmaRet.DATA_ERROR;
                                }
                                else
                                {
                                    next_sequence = SEQ_LZMA;

                                    // If only state reset is wanted with old properties, do the resetting here for simplicity.
                                    if (control >= 0xA0)
                                        lzma.Reset(options);
                                }
                            }
                            else
                            {
                                // Invalid control values
                                if (control > 2)
                                    return LzmaRet.DATA_ERROR;

                                // It's uncompressed chunk
                                sequence = SEQ_COMPRESSED_0;
                                next_sequence = SEQ_COPY;
                            }

                            if (need_dictionary_reset)
                            {
                                // Finish the dictionary reset and let the caller flush the dictionary to the actual output buffer.
                                need_dictionary_reset = false;
                                dict.Reset();
                                return LzmaRet.OK;
                            }

                            break;
                        }

                    case SEQ_UNCOMPRESSED_1:
                        uncompressed_size += (uint)input[in_pos++] << 8;
                        sequence = SEQ_UNCOMPRESSED_2;
                        break;

                    case SEQ_UNCOMPRESSED_2:
                        uncompressed_size += input[in_pos++] + 1U;
                        sequence = SEQ_COMPRESSED_0;
                        lzma.SetUncompressed(uncompressed_size, false);
                        break;

                    case SEQ_COMPRESSED_0:
                        compressed_size = input[in_pos++] << 8;
                        sequence = SEQ_COMPRESSED_1;
                        break;

                    case SEQ_COMPRESSED_1:
                        compressed_size += input[in_pos++] + 1;
                        sequence = next_sequence;
                        break;

                    case SEQ_PROPERTIES:
                        if (LzmaProbs.LcLpPbDecode(options, input[in_pos++]))
                            return LzmaRet.DATA_ERROR;

                        lzma.Reset(options);

                        sequence = SEQ_LZMA;
                        break;

                    case SEQ_LZMA:
                        {
                            // Store the start offset so that we can update coder->compressed_size later.
                            int in_start = in_pos;

                            // Decode from in[] to *dict.
                            var ret = lzma.Code(dict, input, ref in_pos, in_size);

                            // Validate and update coder->compressed_size.
                            int in_used = in_pos - in_start;
                            if (in_used > compressed_size)
                                return LzmaRet.DATA_ERROR;

                            compressed_size -= in_used;

                            // Return if we didn't finish the chunk, or an error occurred.
                            if (ret != LzmaRet.STREAM_END)
                                return ret;

                            // The LZMA decoder must have consumed the whole chunk now.
                            if (compressed_size != 0)
                                return LzmaRet.DATA_ERROR;

                            sequence = SEQ_CONTROL;
                            break;
                        }

                    case SEQ_COPY:
                        {
                            // Copy from input to the dictionary as is.
                            dict.Write(input, ref in_pos, in_size, ref compressed_size);
                            if (compressed_size != 0)
                                return LzmaRet.OK;

                            sequence = SEQ_CONTROL;
                            break;
                        }

                    default:
                        return LzmaRet.PROG_ERROR;
                }
            }

            return LzmaRet.OK;
        }

        public void Reset(LzmaOptionsLzma options) { }

        public void SetUncompressed(ulong uncompressed_size, bool allow_eopm) { }

        /// <summary>lzma2_decoder_init</summary>
        private static (LzmaRet, uint, byte[], uint) Init(LzDecoderCoder lz, object opt)
        {
            if (lz.lz is not LzmaLzma2Decoder coder)
            {
                coder = new LzmaLzma2Decoder();
                lz.lz = coder;
            }

            var options = (LzmaOptionsLzma)opt;

            coder.sequence = SEQ_CONTROL;
            coder.need_properties = true;
            coder.need_dictionary_reset = options.preset_dict == null || options.preset_dict_size == 0;

            return LzmaLzma1Decoder.Create(ref coder.lzma, options);
        }

        /// <summary>lzma_lzma2_decoder_init</summary>
        internal static LzmaRet DecoderInit(ref LzmaNextCoder next, LzmaFilterInfo[] filters, int index) =>
            LzDecoderCoder.DecoderInit(ref next, filters, index, (lz, id, options) => Init(lz, options));

        /// <summary>lzma_lzma2_decoder_memusage</summary>
        internal static ulong MemUsage(object options) =>
            options is LzmaOptionsLzma opt ? 64 + LzmaLzma1Decoder.MemUsageNoCheck(opt) : ulong.MaxValue;

        /// <summary>lzma_lzma2_props_decode</summary>
        internal static LzmaRet PropsDecode(out object options, byte[] props, int at, int props_size)
        {
            options = null;
            if (props_size != 1)
                return LzmaRet.OPTIONS_ERROR;

            // Check that reserved bits are unset.
            if ((props[at] & 0xC0) != 0)
                return LzmaRet.OPTIONS_ERROR;

            // Decode the dictionary size.
            if (props[at] > 40)
                return LzmaRet.OPTIONS_ERROR;

            var opt = new LzmaOptionsLzma();

            if (props[at] == 40)
            {
                opt.dict_size = uint.MaxValue;
            }
            else
            {
                opt.dict_size = 2 | (props[at] & 1U);
                opt.dict_size <<= props[at] / 2 + 11;
            }

            opt.preset_dict = null;
            opt.preset_dict_size = 0;

            options = opt;
            return LzmaRet.OK;
        }
    }
}
