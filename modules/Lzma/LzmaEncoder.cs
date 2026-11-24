using System;
using System.Numerics;

namespace SharpPy.Modules.Lzma
{
    /// <summary>
    /// lzma/lzma_common.h: the states, the length and distance constants, and the layout of the
    /// probabilities. The C structs keep them in named arrays; here encoder and decoder keep them in
    /// one ushort[] at these offsets, so the range coder can hold an index where C holds a pointer.
    /// </summary>
    internal static class LzmaProbs
    {
        internal const int STATES = 12, LIT_STATES = 7, POS_STATES_MAX = 1 << 4;
        internal const uint LITERAL_CODER_SIZE = 0x300;
        internal const int LITERAL_CODERS_MAX = 1 << 4;

        internal const uint MATCH_LEN_MIN = 2;
        internal const int LEN_LOW_BITS = 3, LEN_LOW_SYMBOLS = 1 << LEN_LOW_BITS;
        internal const int LEN_MID_BITS = 3, LEN_MID_SYMBOLS = 1 << LEN_MID_BITS;
        internal const int LEN_HIGH_BITS = 8, LEN_HIGH_SYMBOLS = 1 << LEN_HIGH_BITS;
        internal const int LEN_SYMBOLS = LEN_LOW_SYMBOLS + LEN_MID_SYMBOLS + LEN_HIGH_SYMBOLS;
        internal const uint MATCH_LEN_MAX = MATCH_LEN_MIN + LEN_SYMBOLS - 1;

        internal const int DIST_STATES = 4, DIST_SLOT_BITS = 6, DIST_SLOTS = 1 << DIST_SLOT_BITS;
        internal const int DIST_MODEL_START = 4, DIST_MODEL_END = 14;
        internal const int FULL_DISTANCES_BITS = DIST_MODEL_END / 2, FULL_DISTANCES = 1 << FULL_DISTANCES_BITS;
        internal const int ALIGN_BITS = 4, ALIGN_SIZE = 1 << ALIGN_BITS, ALIGN_MASK = ALIGN_SIZE - 1;
        internal const int REPS = 4;

        // offsets in the probability array
        internal const int LITERAL = 0;
        internal const int IS_MATCH = LITERAL + (int)LITERAL_CODER_SIZE * LITERAL_CODERS_MAX;
        internal const int IS_REP = IS_MATCH + STATES * POS_STATES_MAX;
        internal const int IS_REP0 = IS_REP + STATES;
        internal const int IS_REP1 = IS_REP0 + STATES;
        internal const int IS_REP2 = IS_REP1 + STATES;
        internal const int IS_REP0_LONG = IS_REP2 + STATES;
        internal const int DIST_SLOT = IS_REP0_LONG + STATES * POS_STATES_MAX;
        internal const int DIST_SPECIAL = DIST_SLOT + DIST_STATES * DIST_SLOTS;
        internal const int DIST_ALIGN = DIST_SPECIAL + FULL_DISTANCES - DIST_MODEL_END;
        // a length coder: choice, choice2, low[16][8], mid[16][8], high[256]
        internal const int LEN_CHOICE = 0, LEN_CHOICE2 = 1, LEN_LOW = 2,
            LEN_MID = LEN_LOW + POS_STATES_MAX * LEN_LOW_SYMBOLS, LEN_HIGH = LEN_MID + POS_STATES_MAX * LEN_MID_SYMBOLS,
            LEN_CODER_SIZE = LEN_HIGH + LEN_HIGH_SYMBOLS;
        internal const int MATCH_LEN_CODER = DIST_ALIGN + ALIGN_SIZE;
        internal const int REP_LEN_CODER = MATCH_LEN_CODER + LEN_CODER_SIZE;
        internal const int TOTAL = REP_LEN_CODER + LEN_CODER_SIZE;

        // range_common.h
        internal const int RC_SHIFT_BITS = 8, RC_TOP_BITS = 24;
        internal const uint RC_TOP_VALUE = 1U << RC_TOP_BITS;
        internal const int RC_BIT_MODEL_TOTAL_BITS = 11;
        internal const uint RC_BIT_MODEL_TOTAL = 1U << RC_BIT_MODEL_TOTAL_BITS;
        internal const int RC_MOVE_BITS = 5;

        /// <summary>bit_reset</summary>
        internal const ushort BIT_RESET = (ushort)(RC_BIT_MODEL_TOTAL >> 1);

        /// <summary>update_literal</summary>
        internal static uint UpdateLiteral(uint state) => state <= 3 ? 0 : state <= 9 ? state - 3 : state - 6;
        /// <summary>update_literal_normal</summary>
        internal static uint UpdateLiteralNormal(uint state) => state <= 3 ? 0 : state - 3;
        /// <summary>update_literal_matched</summary>
        internal static uint UpdateLiteralMatched(uint state) => state <= 9 ? state - 3 : state - 6;
        /// <summary>update_match</summary>
        internal static uint UpdateMatch(uint state) => state < LIT_STATES ? 7u : 10u;
        /// <summary>update_long_rep</summary>
        internal static uint UpdateLongRep(uint state) => state < LIT_STATES ? 8u : 11u;
        /// <summary>update_short_rep</summary>
        internal static uint UpdateShortRep(uint state) => state < LIT_STATES ? 9u : 11u;
        /// <summary>is_literal_state</summary>
        internal static bool IsLiteralState(uint state) => state < LIT_STATES;

        /// <summary>get_dist_state</summary>
        internal static uint GetDistState(uint len) => len < DIST_STATES + MATCH_LEN_MIN ? len - MATCH_LEN_MIN : DIST_STATES - 1;

        /// <summary>literal_subcoder: the offset of the coder for this position and previous byte.</summary>
        internal static int LiteralSubcoder(uint lc, uint literal_mask, uint pos, uint prev_byte) =>
            LITERAL + (int)(3 * ((((pos << 8) + prev_byte) & literal_mask) << (int)lc));

        /// <summary>literal_mask_calc</summary>
        internal static uint LiteralMaskCalc(uint lc, uint lp) => (0x100u << (int)lp) - (0x100u >> (int)lc);

        /// <summary>is_lclppb_valid</summary>
        internal static bool IsLcLpPbValid(LzmaOptionsLzma options) =>
            options.lc <= LzmaConst.LCLP_MAX && options.lp <= LzmaConst.LCLP_MAX
            && options.lc + options.lp <= LzmaConst.LCLP_MAX && options.pb <= LzmaConst.PB_MAX;

        /// <summary>get_dist_slot (fastpos.h), with the bit scan its HAVE_SMALL variant uses.</summary>
        internal static uint GetDistSlot(uint dist)
        {
            if (dist <= 4)
                return dist;
            uint i = (uint)BitOperations.Log2(dist);
            return (i + i) + ((dist >> (int)(i - 1)) & 1);
        }

        /// <summary>lzma_lzma_lclppb_encode: true on invalid options.</summary>
        internal static bool LcLpPbEncode(LzmaOptionsLzma options, out byte b)
        {
            b = 0;
            if (!IsLcLpPbValid(options))
                return true;
            b = (byte)((options.pb * 5 + options.lp) * 9 + options.lc);
            return false;
        }

        /// <summary>lzma_lzma_lclppb_decode: true on invalid byte.</summary>
        internal static bool LcLpPbDecode(LzmaOptionsLzma options, byte b)
        {
            if (b > (4 * 5 + 4) * 9 + 8)
                return true;
            options.pb = (uint)(b / (9 * 5));
            b -= (byte)(options.pb * 9 * 5);
            options.lp = (uint)(b / 9);
            options.lc = b - options.lp * 9;
            return options.lc + options.lp > LzmaConst.LCLP_MAX;
        }

        /// <summary>lzma_lzma_preset (lzma_encoder_presets.c): true on an unsupported preset.</summary>
        internal static bool Preset(LzmaOptionsLzma options, uint preset)
        {
            uint level = preset & LzmaConst.PRESET_LEVEL_MASK;
            uint flags = preset & ~LzmaConst.PRESET_LEVEL_MASK;
            uint supported_flags = LzmaConst.PRESET_EXTREME;

            if (level > 9 || (flags & ~supported_flags) != 0)
                return true;

            options.preset_dict = null;
            options.preset_dict_size = 0;

            options.lc = LzmaConst.LC_DEFAULT;
            options.lp = LzmaConst.LP_DEFAULT;
            options.pb = LzmaConst.PB_DEFAULT;

            ReadOnlySpan<byte> dict_pow2 = new byte[] { 18, 20, 21, 22, 22, 23, 23, 24, 25, 26 };
            options.dict_size = 1U << dict_pow2[(int)level];

            if (level <= 3)
            {
                options.mode = LzmaMode.FAST;
                options.mf = level == 0 ? LzmaMatchFinder.HC3 : LzmaMatchFinder.HC4;
                options.nice_len = level <= 1 ? 128u : 273u;
                ReadOnlySpan<byte> depths = new byte[] { 4, 8, 24, 48 };
                options.depth = depths[(int)level];
            }
            else
            {
                options.mode = LzmaMode.NORMAL;
                options.mf = LzmaMatchFinder.BT4;
                options.nice_len = level == 4 ? 16u : level == 5 ? 32u : 64u;
                options.depth = 0;
            }

            if ((flags & LzmaConst.PRESET_EXTREME) != 0)
            {
                options.mode = LzmaMode.NORMAL;
                options.mf = LzmaMatchFinder.BT4;
                if (level == 3 || level == 5)
                {
                    options.nice_len = 192;
                    options.depth = 0;
                }
                else
                {
                    options.nice_len = 273;
                    options.depth = 512;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// lzma_range_encoder (rangecoder/range_encoder.h) and the prices of price.h with the table of
    /// price_tablegen.c.
    /// </summary>
    internal sealed class LzmaRangeEncoder
    {
        internal const int RC_SYMBOLS_MAX = 58;
        internal const int RC_BIT_0 = 0, RC_BIT_1 = 1, RC_DIRECT_0 = 2, RC_DIRECT_1 = 3, RC_FLUSH = 4;

        internal ulong low;
        internal ulong cache_size;
        internal uint range;
        internal byte cache;
        internal ulong out_total;
        internal int count;
        internal int pos;
        internal readonly int[] symbols = new int[RC_SYMBOLS_MAX];
        internal readonly int[] probs = new int[RC_SYMBOLS_MAX];
        internal ushort[] P;

        /// <summary>rc_reset</summary>
        internal void Reset()
        {
            low = 0;
            cache_size = 1;
            range = uint.MaxValue;
            cache = 0;
            out_total = 0;
            count = 0;
            pos = 0;
        }

        /// <summary>rc_forget</summary>
        internal void Forget() => count = 0;

        /// <summary>rc_bit</summary>
        internal void Bit(int prob, uint bit)
        {
            symbols[count] = (int)bit;
            probs[count] = prob;
            ++count;
        }

        /// <summary>rc_bittree</summary>
        internal void BitTree(int probs, int bit_count, uint symbol)
        {
            uint model_index = 1;
            do
            {
                uint bit = (symbol >> --bit_count) & 1;
                Bit(probs + (int)model_index, bit);
                model_index = (model_index << 1) + bit;
            } while (bit_count != 0);
        }

        /// <summary>rc_bittree_reverse</summary>
        internal void BitTreeReverse(int probs, int bit_count, uint symbol)
        {
            uint model_index = 1;
            do
            {
                uint bit = symbol & 1;
                symbol >>= 1;
                Bit(probs + (int)model_index, bit);
                model_index = (model_index << 1) + bit;
            } while (--bit_count != 0);
        }

        /// <summary>rc_direct</summary>
        internal void Direct(uint value, int bit_count)
        {
            do
            {
                symbols[count++] = RC_DIRECT_0 + (int)((value >> --bit_count) & 1);
            } while (bit_count != 0);
        }

        /// <summary>rc_flush</summary>
        internal void Flush()
        {
            for (int i = 0; i < 5; ++i)
                symbols[count++] = RC_FLUSH;
        }

        /// <summary>rc_shift_low: true when the output is full.</summary>
        private bool ShiftLow(byte[] output, ref int out_pos, int out_size)
        {
            if ((uint)low < 0xFF000000u || (uint)(low >> 32) != 0)
            {
                do
                {
                    if (out_pos == out_size)
                        return true;

                    output[out_pos] = (byte)(cache + (byte)(low >> 32));
                    ++out_pos;
                    ++out_total;
                    cache = 0xFF;
                } while (--cache_size != 0);

                cache = (byte)((low >> 24) & 0xFF);
            }

            ++cache_size;
            low = (low & 0x00FFFFFF) << RC_SHIFT_BITS;
            return false;
        }

        private const int RC_SHIFT_BITS = 8;

        /// <summary>rc_shift_low_dummy</summary>
        private static bool ShiftLowDummy(ref ulong low, ref ulong cache_size, ref byte cache, ref ulong out_pos, ulong out_size)
        {
            if ((uint)low < 0xFF000000u || (uint)(low >> 32) != 0)
            {
                do
                {
                    if (out_pos == out_size)
                        return true;
                    ++out_pos;
                    cache = 0xFF;
                } while (--cache_size != 0);

                cache = (byte)((low >> 24) & 0xFF);
            }

            ++cache_size;
            low = (low & 0x00FFFFFF) << RC_SHIFT_BITS;
            return false;
        }

        /// <summary>rc_encode: true when the output is full.</summary>
        internal bool Encode(byte[] output, ref int out_pos, int out_size)
        {
            var P = this.P;
            while (pos < count)
            {
                // Normalize
                if (range < LzmaProbs.RC_TOP_VALUE)
                {
                    if (ShiftLow(output, ref out_pos, out_size))
                        return true;
                    range <<= RC_SHIFT_BITS;
                }

                // Encode a bit
                switch (symbols[pos])
                {
                    case RC_BIT_0:
                        {
                            uint prob = P[probs[pos]];
                            range = (range >> LzmaProbs.RC_BIT_MODEL_TOTAL_BITS) * prob;
                            prob += (LzmaProbs.RC_BIT_MODEL_TOTAL - prob) >> LzmaProbs.RC_MOVE_BITS;
                            P[probs[pos]] = (ushort)prob;
                            break;
                        }
                    case RC_BIT_1:
                        {
                            uint prob = P[probs[pos]];
                            uint bound = prob * (range >> LzmaProbs.RC_BIT_MODEL_TOTAL_BITS);
                            low += bound;
                            range -= bound;
                            prob -= prob >> LzmaProbs.RC_MOVE_BITS;
                            P[probs[pos]] = (ushort)prob;
                            break;
                        }
                    case RC_DIRECT_0:
                        range >>= 1;
                        break;
                    case RC_DIRECT_1:
                        range >>= 1;
                        low += range;
                        break;
                    case RC_FLUSH:
                        // Prevent further normalizations.
                        range = uint.MaxValue;

                        // Flush the last five bytes (see rc_flush()).
                        do
                        {
                            if (ShiftLow(output, ref out_pos, out_size))
                                return true;
                        } while (++pos < count);

                        // Reset the range encoder so we are ready to continue encoding if we weren't finishing the stream.
                        Reset();
                        return false;
                }

                ++pos;
            }

            count = 0;
            pos = 0;

            return false;
        }

        /// <summary>rc_encode_dummy: true when the pending symbols would not fit in out_limit.</summary>
        internal bool EncodeDummy(ulong out_limit)
        {
            ulong low = this.low;
            ulong cache_size = this.cache_size;
            uint range = this.range;
            byte cache = this.cache;
            ulong out_pos = out_total;

            int pos = this.pos;

            while (true)
            {
                // Normalize
                if (range < LzmaProbs.RC_TOP_VALUE)
                {
                    if (ShiftLowDummy(ref low, ref cache_size, ref cache, ref out_pos, out_limit))
                        return true;
                    range <<= RC_SHIFT_BITS;
                }

                // This check is here because the normalization above must be done before flushing the last bytes.
                if (pos == count)
                    break;

                // Encode a bit
                switch (symbols[pos])
                {
                    case RC_BIT_0:
                        {
                            uint prob = P[probs[pos]];
                            range = (range >> LzmaProbs.RC_BIT_MODEL_TOTAL_BITS) * prob;
                            break;
                        }
                    case RC_BIT_1:
                        {
                            uint prob = P[probs[pos]];
                            uint bound = prob * (range >> LzmaProbs.RC_BIT_MODEL_TOTAL_BITS);
                            low += bound;
                            range -= bound;
                            break;
                        }
                    case RC_DIRECT_0:
                        range >>= 1;
                        break;
                    case RC_DIRECT_1:
                        range >>= 1;
                        low += range;
                        break;
                }

                ++pos;
            }

            // Flush the last bytes.
            for (pos = 0; pos < 5; ++pos)
            {
                if (ShiftLowDummy(ref low, ref cache_size, ref cache, ref out_pos, out_limit))
                    return true;
            }

            return false;
        }

        /// <summary>rc_pending</summary>
        internal ulong Pending => cache_size + 5 - 1;

        // ------------------------------------------------------------ price.h

        internal const int RC_MOVE_REDUCING_BITS = 4, RC_BIT_PRICE_SHIFT_BITS = 4;
        internal const int RC_PRICE_TABLE_SIZE = (int)(LzmaProbs.RC_BIT_MODEL_TOTAL >> RC_MOVE_REDUCING_BITS);
        internal const uint RC_INFINITY_PRICE = 1U << 30;

        /// <summary>lzma_rc_prices, computed as price_tablegen.c does.</summary>
        private static readonly byte[] rc_prices = MakePriceTable();

        private static byte[] MakePriceTable()
        {
            var t = new byte[RC_PRICE_TABLE_SIZE];
            for (uint i = (1U << RC_MOVE_REDUCING_BITS) / 2; i < LzmaProbs.RC_BIT_MODEL_TOTAL; i += 1U << RC_MOVE_REDUCING_BITS)
            {
                const int cycles_bits = RC_BIT_PRICE_SHIFT_BITS;
                uint w = i;
                uint bit_count = 0;

                for (int j = 0; j < cycles_bits; ++j)
                {
                    w *= w;
                    bit_count <<= 1;

                    while (w >= (1U << 16))
                    {
                        w >>= 1;
                        ++bit_count;
                    }
                }

                t[i >> RC_MOVE_REDUCING_BITS] = (byte)((LzmaProbs.RC_BIT_MODEL_TOTAL_BITS << cycles_bits) - 15 - bit_count);
            }
            return t;
        }

        /// <summary>rc_bit_price</summary>
        internal static uint BitPrice(uint prob, uint bit) =>
            rc_prices[(prob ^ ((0u - bit) & (LzmaProbs.RC_BIT_MODEL_TOTAL - 1))) >> RC_MOVE_REDUCING_BITS];

        /// <summary>rc_bit_0_price</summary>
        internal static uint Bit0Price(uint prob) => rc_prices[prob >> RC_MOVE_REDUCING_BITS];

        /// <summary>rc_bit_1_price</summary>
        internal static uint Bit1Price(uint prob) => rc_prices[(prob ^ (LzmaProbs.RC_BIT_MODEL_TOTAL - 1)) >> RC_MOVE_REDUCING_BITS];

        /// <summary>rc_bittree_price</summary>
        internal static uint BitTreePrice(ushort[] P, int probs, int bit_levels, uint symbol)
        {
            uint price = 0;
            symbol += 1U << bit_levels;

            do
            {
                uint bit = symbol & 1;
                symbol >>= 1;
                price += BitPrice(P[probs + (int)symbol], bit);
            } while (symbol != 1);

            return price;
        }

        /// <summary>rc_bittree_reverse_price</summary>
        internal static uint BitTreeReversePrice(ushort[] P, int probs, int bit_levels, uint symbol)
        {
            uint price = 0;
            uint model_index = 1;

            do
            {
                uint bit = symbol & 1;
                symbol >>= 1;
                price += BitPrice(P[probs + (int)model_index], bit);
                model_index = (model_index << 1) + bit;
            } while (--bit_levels != 0);

            return price;
        }

        /// <summary>rc_direct_price</summary>
        internal static uint DirectPrice(uint bits) => bits << RC_BIT_PRICE_SHIFT_BITS;
    }

    /// <summary>lzma_length_encoder: the probabilities live in the encoder's array at baseIndex.</summary>
    internal sealed class LzmaLengthEncoder
    {
        internal readonly int baseIndex;
        internal readonly uint[] prices = new uint[LzmaProbs.POS_STATES_MAX * LzmaProbs.LEN_SYMBOLS];
        internal uint table_size;
        internal readonly uint[] counters = new uint[LzmaProbs.POS_STATES_MAX];

        internal LzmaLengthEncoder(int baseIndex) => this.baseIndex = baseIndex;
    }

    /// <summary>lzma_optimal</summary>
    internal struct LzmaOptimal
    {
        internal uint state;
        internal bool prev_1_is_literal;
        internal bool prev_2;
        internal uint pos_prev_2;
        internal uint back_prev_2;
        internal uint price;
        internal uint pos_prev;
        internal uint back_prev;
        internal uint backs0, backs1, backs2, backs3;

        internal uint GetBack(uint i) => i switch { 0 => backs0, 1 => backs1, 2 => backs2, _ => backs3 };
    }

    /// <summary>
    /// lzma_lzma1_encoder (lzma/lzma_encoder.c, lzma_encoder_optimum_normal.c,
    /// lzma_encoder_optimum_fast.c) of xz 5.8.3.
    /// </summary>
    internal sealed class LzmaLzma1Encoder : ILzEncoder
    {
        private const int OPTS = 1 << 12;
        // LOOP_INPUT_MAX
        internal const uint LOOP_INPUT_MAX = OPTS + 1;
        internal const uint LZMA2_CHUNK_MAX = 1U << 16;

        internal readonly LzmaRangeEncoder rc = new();
        internal readonly ushort[] P = new ushort[LzmaProbs.TOTAL];

        internal uint state;
        internal readonly uint[] reps = new uint[LzmaProbs.REPS];
        internal readonly LzmaMatch[] matches = new LzmaMatch[LzmaProbs.MATCH_LEN_MAX + 1];
        internal uint matches_count;
        internal uint longest_match_length;
        internal bool fast_mode;
        internal bool is_initialized;
        internal bool is_flushed;
        internal bool use_eopm;
        internal ulong uncomp_size;
        internal ulong out_limit;
        // uncomp_size_ptr: unused by SharpPy (set_out_limit is for the .lz and microlzma encoders)

        internal uint pos_mask;
        internal uint literal_context_bits;
        internal uint literal_mask;

        internal readonly LzmaLengthEncoder match_len_encoder = new(LzmaProbs.MATCH_LEN_CODER);
        internal readonly LzmaLengthEncoder rep_len_encoder = new(LzmaProbs.REP_LEN_CODER);

        internal readonly uint[] dist_slot_prices = new uint[LzmaProbs.DIST_STATES * LzmaProbs.DIST_SLOTS];
        internal readonly uint[] dist_prices = new uint[LzmaProbs.DIST_STATES * LzmaProbs.FULL_DISTANCES];
        internal uint dist_table_size;
        internal uint match_price_count;

        internal readonly uint[] align_prices = new uint[LzmaProbs.ALIGN_SIZE];
        internal uint align_price_count;

        internal uint opts_end_index;
        internal uint opts_current_index;
        internal readonly LzmaOptimal[] opts = new LzmaOptimal[OPTS];

        internal LzmaLzma1Encoder() => rc.P = P;

        // ------------------------------------------------------------ Literal

        /// <summary>literal_matched</summary>
        private void LiteralMatched(int subcoder, uint match_byte, uint symbol)
        {
            uint offset = 0x100;
            symbol += 1U << 8;

            do
            {
                match_byte <<= 1;
                uint match_bit = match_byte & offset;
                uint subcoder_index = offset + match_bit + (symbol >> 8);
                uint bit = (symbol >> 7) & 1;
                rc.Bit(subcoder + (int)subcoder_index, bit);

                symbol <<= 1;
                offset &= ~(match_byte ^ symbol);
            } while (symbol < (1U << 16));
        }

        /// <summary>literal</summary>
        private void Literal(LzmaMF mf, uint position)
        {
            // Locate the literal byte to be encoded and the subcoder.
            byte cur_byte = mf.buffer[mf.read_pos - mf.read_ahead];
            int subcoder = LzmaProbs.LiteralSubcoder(literal_context_bits, literal_mask,
                position, mf.buffer[mf.read_pos - mf.read_ahead - 1]);

            if (LzmaProbs.IsLiteralState(state))
            {
                // Previous LZMA-symbol was a literal. Encode a normal literal without a match byte.
                state = LzmaProbs.UpdateLiteralNormal(state);
                rc.BitTree(subcoder, 8, cur_byte);
            }
            else
            {
                // Previous LZMA-symbol was a match. Use the last byte of the match as a "match byte".
                state = LzmaProbs.UpdateLiteralMatched(state);
                byte match_byte = mf.buffer[mf.read_pos - reps[0] - 1 - mf.read_ahead];
                LiteralMatched(subcoder, match_byte, cur_byte);
            }
        }

        // ------------------------------------------------------------ Match length

        /// <summary>length_update_prices</summary>
        private void LengthUpdatePrices(LzmaLengthEncoder lc, uint pos_state)
        {
            uint table_size = lc.table_size;
            lc.counters[pos_state] = table_size;

            int b = lc.baseIndex;
            uint a0 = LzmaRangeEncoder.Bit0Price(P[b + LzmaProbs.LEN_CHOICE]);
            uint a1 = LzmaRangeEncoder.Bit1Price(P[b + LzmaProbs.LEN_CHOICE]);
            uint b0 = a1 + LzmaRangeEncoder.Bit0Price(P[b + LzmaProbs.LEN_CHOICE2]);
            uint b1 = a1 + LzmaRangeEncoder.Bit1Price(P[b + LzmaProbs.LEN_CHOICE2]);
            var prices = lc.prices;
            int prow = (int)pos_state * LzmaProbs.LEN_SYMBOLS;

            uint i;
            for (i = 0; i < table_size && i < LzmaProbs.LEN_LOW_SYMBOLS; ++i)
                prices[prow + i] = a0 + LzmaRangeEncoder.BitTreePrice(P,
                    b + LzmaProbs.LEN_LOW + (int)pos_state * LzmaProbs.LEN_LOW_SYMBOLS, LzmaProbs.LEN_LOW_BITS, i);

            for (; i < table_size && i < LzmaProbs.LEN_LOW_SYMBOLS + LzmaProbs.LEN_MID_SYMBOLS; ++i)
                prices[prow + i] = b0 + LzmaRangeEncoder.BitTreePrice(P,
                    b + LzmaProbs.LEN_MID + (int)pos_state * LzmaProbs.LEN_MID_SYMBOLS, LzmaProbs.LEN_MID_BITS,
                    i - LzmaProbs.LEN_LOW_SYMBOLS);

            for (; i < table_size; ++i)
                prices[prow + i] = b1 + LzmaRangeEncoder.BitTreePrice(P, b + LzmaProbs.LEN_HIGH, LzmaProbs.LEN_HIGH_BITS,
                    i - LzmaProbs.LEN_LOW_SYMBOLS - LzmaProbs.LEN_MID_SYMBOLS);
        }

        /// <summary>length</summary>
        private void Length(LzmaLengthEncoder lc, uint pos_state, uint len, bool fast_mode)
        {
            int b = lc.baseIndex;
            len -= LzmaProbs.MATCH_LEN_MIN;

            if (len < LzmaProbs.LEN_LOW_SYMBOLS)
            {
                rc.Bit(b + LzmaProbs.LEN_CHOICE, 0);
                rc.BitTree(b + LzmaProbs.LEN_LOW + (int)pos_state * LzmaProbs.LEN_LOW_SYMBOLS, LzmaProbs.LEN_LOW_BITS, len);
            }
            else
            {
                rc.Bit(b + LzmaProbs.LEN_CHOICE, 1);
                len -= LzmaProbs.LEN_LOW_SYMBOLS;

                if (len < LzmaProbs.LEN_MID_SYMBOLS)
                {
                    rc.Bit(b + LzmaProbs.LEN_CHOICE2, 0);
                    rc.BitTree(b + LzmaProbs.LEN_MID + (int)pos_state * LzmaProbs.LEN_MID_SYMBOLS, LzmaProbs.LEN_MID_BITS, len);
                }
                else
                {
                    rc.Bit(b + LzmaProbs.LEN_CHOICE2, 1);
                    len -= LzmaProbs.LEN_MID_SYMBOLS;
                    rc.BitTree(b + LzmaProbs.LEN_HIGH, LzmaProbs.LEN_HIGH_BITS, len);
                }
            }

            // Only getoptimum uses the prices so don't update the table when in fast mode.
            if (!fast_mode)
                if (--lc.counters[pos_state] == 0)
                    LengthUpdatePrices(lc, pos_state);
        }

        // ------------------------------------------------------------ Match

        /// <summary>match</summary>
        private void Match(uint pos_state, uint distance, uint len)
        {
            state = LzmaProbs.UpdateMatch(state);

            Length(match_len_encoder, pos_state, len, fast_mode);

            uint dist_slot = LzmaProbs.GetDistSlot(distance);
            uint dist_state = LzmaProbs.GetDistState(len);
            rc.BitTree(LzmaProbs.DIST_SLOT + (int)dist_state * LzmaProbs.DIST_SLOTS, LzmaProbs.DIST_SLOT_BITS, dist_slot);

            if (dist_slot >= LzmaProbs.DIST_MODEL_START)
            {
                int footer_bits = (int)((dist_slot >> 1) - 1);
                uint @base = (2 | (dist_slot & 1)) << footer_bits;
                uint dist_reduced = distance - @base;

                if (dist_slot < LzmaProbs.DIST_MODEL_END)
                {
                    // base - dist_slot - 1 can be -1, but rc_bittree_reverse starts at probs[1].
                    rc.BitTreeReverse(LzmaProbs.DIST_SPECIAL + (int)@base - (int)dist_slot - 1, footer_bits, dist_reduced);
                }
                else
                {
                    rc.Direct(dist_reduced >> LzmaProbs.ALIGN_BITS, footer_bits - LzmaProbs.ALIGN_BITS);
                    rc.BitTreeReverse(LzmaProbs.DIST_ALIGN, LzmaProbs.ALIGN_BITS, dist_reduced & LzmaProbs.ALIGN_MASK);
                    ++align_price_count;
                }
            }

            reps[3] = reps[2];
            reps[2] = reps[1];
            reps[1] = reps[0];
            reps[0] = distance;
            ++match_price_count;
        }

        // ------------------------------------------------------------ Repeated match

        /// <summary>rep_match</summary>
        private void RepMatch(uint pos_state, uint rep, uint len)
        {
            if (rep == 0)
            {
                rc.Bit(LzmaProbs.IS_REP0 + (int)state, 0);
                rc.Bit(LzmaProbs.IS_REP0_LONG + (int)state * LzmaProbs.POS_STATES_MAX + (int)pos_state, len != 1 ? 1u : 0u);
            }
            else
            {
                uint distance = reps[rep];
                rc.Bit(LzmaProbs.IS_REP0 + (int)state, 1);

                if (rep == 1)
                {
                    rc.Bit(LzmaProbs.IS_REP1 + (int)state, 0);
                }
                else
                {
                    rc.Bit(LzmaProbs.IS_REP1 + (int)state, 1);
                    rc.Bit(LzmaProbs.IS_REP2 + (int)state, rep - 2);

                    if (rep == 3)
                        reps[3] = reps[2];

                    reps[2] = reps[1];
                }

                reps[1] = reps[0];
                reps[0] = distance;
            }

            if (len == 1)
            {
                state = LzmaProbs.UpdateShortRep(state);
            }
            else
            {
                Length(rep_len_encoder, pos_state, len, fast_mode);
                state = LzmaProbs.UpdateLongRep(state);
            }
        }

        // ------------------------------------------------------------ Main

        /// <summary>encode_symbol</summary>
        private void EncodeSymbol(LzmaMF mf, uint back, uint len, uint position)
        {
            uint pos_state = position & pos_mask;

            if (back == uint.MaxValue)
            {
                // Literal i.e. eight-bit byte
                rc.Bit(LzmaProbs.IS_MATCH + (int)state * LzmaProbs.POS_STATES_MAX + (int)pos_state, 0);
                Literal(mf, position);
            }
            else
            {
                // Some type of match
                rc.Bit(LzmaProbs.IS_MATCH + (int)state * LzmaProbs.POS_STATES_MAX + (int)pos_state, 1);

                if (back < LzmaProbs.REPS)
                {
                    // It's a repeated match i.e. the same distance has been used earlier.
                    rc.Bit(LzmaProbs.IS_REP + (int)state, 1);
                    RepMatch(pos_state, back, len);
                }
                else
                {
                    // Normal match
                    rc.Bit(LzmaProbs.IS_REP + (int)state, 0);
                    Match(pos_state, back - LzmaProbs.REPS, len);
                }
            }

            mf.read_ahead -= len;
        }

        /// <summary>encode_init</summary>
        private bool EncodeInit(LzmaMF mf)
        {
            if (mf.read_pos == mf.read_limit)
            {
                if (mf.action == LzmaAction.RUN)
                    return false; // We cannot do anything.

                // We are finishing (we cannot get here when flushing).
            }
            else
            {
                // Do the actual initialization. The first LZMA symbol must always be a literal.
                mf.Skip(1);
                mf.read_ahead = 0;
                rc.Bit(LzmaProbs.IS_MATCH, 0);
                rc.BitTree(LzmaProbs.LITERAL, 8, mf.buffer[0]);
                ++uncomp_size;
            }

            // Initialization is done (except if empty file).
            is_initialized = true;

            return true;
        }

        /// <summary>encode_eopm</summary>
        private void EncodeEopm(uint position)
        {
            uint pos_state = position & pos_mask;
            rc.Bit(LzmaProbs.IS_MATCH + (int)state * LzmaProbs.POS_STATES_MAX + (int)pos_state, 1);
            rc.Bit(LzmaProbs.IS_REP + (int)state, 0);
            Match(pos_state, uint.MaxValue, LzmaProbs.MATCH_LEN_MIN);
        }

        /// <summary>lzma_lzma_encode</summary>
        internal LzmaRet Encode(LzmaMF mf, byte[] output, ref int out_pos, int out_size, uint limit, int out_base = 0)
        {
            // Initialize the stream if no data has been encoded yet.
            if (!is_initialized && !EncodeInit(mf))
                return LzmaRet.OK;

            // Encode pending output bytes from the range encoder.
            if (rc.Encode(output, ref out_pos, out_size))
                return LzmaRet.OK;

            // If the range encoder was flushed, the stream has been finished.
            if (is_flushed)
                return LzmaRet.STREAM_END;

            while (true)
            {
                // With LZMA2 we need to take care that compressed size of a chunk doesn't get too big.
                if (limit != uint.MaxValue
                        && (mf.read_pos - mf.read_ahead >= limit
                            || (ulong)(out_pos - out_base) + rc.Pending >= LZMA2_CHUNK_MAX - LOOP_INPUT_MAX))
                    break;

                // Check that there is some input to process.
                if (mf.read_pos >= mf.read_limit)
                {
                    if (mf.action == LzmaAction.RUN)
                        return LzmaRet.OK;

                    if (mf.read_ahead == 0)
                        break;
                }

                // Get optimal match (repeat position and length).
                uint len;
                uint back;

                if (fast_mode)
                    OptimumFast(mf, out back, out len);
                else
                    OptimumNormal(mf, out back, out len, (uint)uncomp_size);

                EncodeSymbol(mf, back, len, (uint)uncomp_size);

                // If output size limiting is active (out_limit != 0), check if encoding this LZMA symbol would make the output size exceed the specified limit.
                if (out_limit != 0 && rc.EncodeDummy(out_limit))
                {
                    // The most recent LZMA symbol would make the output too big. Throw it away.
                    rc.Forget();
                    break;
                }

                // This symbol will be encoded so update the uncompressed size.
                uncomp_size += len;

                // Encode the LZMA symbol.
                if (rc.Encode(output, ref out_pos, out_size))
                    return LzmaRet.OK;
            }

            // Add EOPM if requested.
            if (use_eopm)
                EncodeEopm((uint)uncomp_size);

            // Flush the remaining bytes from the range encoder.
            rc.Flush();

            // Copy the remaining bytes to the output buffer. If there isn't enough output space, we
            // will copy out the remaining bytes on the next call to this function.
            if (rc.Encode(output, ref out_pos, out_size))
            {
                // Mark that the encoder state is flushed.
                is_flushed = true;
                return LzmaRet.OK;
            }

            return LzmaRet.STREAM_END;
        }

        /// <summary>lzma_encode (the plain LZMA lz.code)</summary>
        public LzmaRet Code(LzmaMF mf, byte[] output, ref int out_pos, int out_size)
        {
            // Plain LZMA has no support for sync-flushing.
            if (mf.action == LzmaAction.SYNC_FLUSH)
                return LzmaRet.OPTIONS_ERROR;

            return Encode(mf, output, ref out_pos, out_size, uint.MaxValue);
        }

        public LzmaRet OptionsUpdate(LzmaFilter filter) => LzmaRet.PROG_ERROR;

        // ------------------------------------------------------------ Initialization

        /// <summary>is_options_valid</summary>
        internal static bool IsOptionsValid(LzmaOptionsLzma options) =>
            LzmaProbs.IsLcLpPbValid(options)
            && options.nice_len >= LzmaProbs.MATCH_LEN_MIN
            && options.nice_len <= LzmaProbs.MATCH_LEN_MAX
            && (options.mode == LzmaMode.FAST || options.mode == LzmaMode.NORMAL);

        /// <summary>set_lz_options</summary>
        internal static void SetLzOptions(ref LzEncoderOptions lz_options, LzmaOptionsLzma options)
        {
            lz_options.before_size = OPTS;
            lz_options.dict_size = options.dict_size;
            lz_options.after_size = LOOP_INPUT_MAX;
            lz_options.match_len_max = LzmaProbs.MATCH_LEN_MAX;
            lz_options.nice_len = Math.Max(LzmaMF.GetHashBytes(options.mf), options.nice_len);
            lz_options.match_finder = options.mf;
            lz_options.depth = options.depth;
            lz_options.preset_dict = options.preset_dict;
            lz_options.preset_dict_size = options.preset_dict_size;
        }

        /// <summary>length_encoder_reset</summary>
        private void LengthEncoderReset(LzmaLengthEncoder lencoder, uint num_pos_states, bool fast_mode)
        {
            int b = lencoder.baseIndex;
            P[b + LzmaProbs.LEN_CHOICE] = LzmaProbs.BIT_RESET;
            P[b + LzmaProbs.LEN_CHOICE2] = LzmaProbs.BIT_RESET;

            for (int pos_state = 0; pos_state < num_pos_states; ++pos_state)
            {
                Array.Fill(P, LzmaProbs.BIT_RESET, b + LzmaProbs.LEN_LOW + pos_state * LzmaProbs.LEN_LOW_SYMBOLS, LzmaProbs.LEN_LOW_SYMBOLS);
                Array.Fill(P, LzmaProbs.BIT_RESET, b + LzmaProbs.LEN_MID + pos_state * LzmaProbs.LEN_MID_SYMBOLS, LzmaProbs.LEN_MID_SYMBOLS);
            }

            Array.Fill(P, LzmaProbs.BIT_RESET, b + LzmaProbs.LEN_HIGH, LzmaProbs.LEN_HIGH_SYMBOLS);

            if (!fast_mode)
                for (uint pos_state = 0; pos_state < num_pos_states; ++pos_state)
                    LengthUpdatePrices(lencoder, pos_state);
        }

        /// <summary>lzma_lzma_encoder_reset</summary>
        internal LzmaRet Reset(LzmaOptionsLzma options)
        {
            if (!IsOptionsValid(options))
                return LzmaRet.OPTIONS_ERROR;

            pos_mask = (1U << (int)options.pb) - 1;
            literal_context_bits = options.lc;
            literal_mask = LzmaProbs.LiteralMaskCalc(options.lc, options.lp);

            // Range coder
            rc.Reset();

            // State
            state = 0;
            for (int i = 0; i < LzmaProbs.REPS; ++i)
                reps[i] = 0;

            // literal_init
            Array.Fill(P, LzmaProbs.BIT_RESET, LzmaProbs.LITERAL, (int)LzmaProbs.LITERAL_CODER_SIZE << (int)(options.lc + options.lp));

            // Bit encoders
            for (int i = 0; i < LzmaProbs.STATES; ++i)
            {
                for (int j = 0; j <= pos_mask; ++j)
                {
                    P[LzmaProbs.IS_MATCH + i * LzmaProbs.POS_STATES_MAX + j] = LzmaProbs.BIT_RESET;
                    P[LzmaProbs.IS_REP0_LONG + i * LzmaProbs.POS_STATES_MAX + j] = LzmaProbs.BIT_RESET;
                }

                P[LzmaProbs.IS_REP + i] = LzmaProbs.BIT_RESET;
                P[LzmaProbs.IS_REP0 + i] = LzmaProbs.BIT_RESET;
                P[LzmaProbs.IS_REP1 + i] = LzmaProbs.BIT_RESET;
                P[LzmaProbs.IS_REP2 + i] = LzmaProbs.BIT_RESET;
            }

            Array.Fill(P, LzmaProbs.BIT_RESET, LzmaProbs.DIST_SPECIAL, LzmaProbs.FULL_DISTANCES - LzmaProbs.DIST_MODEL_END);

            // Bit tree encoders
            Array.Fill(P, LzmaProbs.BIT_RESET, LzmaProbs.DIST_SLOT, LzmaProbs.DIST_STATES * LzmaProbs.DIST_SLOTS);
            Array.Fill(P, LzmaProbs.BIT_RESET, LzmaProbs.DIST_ALIGN, LzmaProbs.ALIGN_SIZE);

            // Length encoders
            LengthEncoderReset(match_len_encoder, 1U << (int)options.pb, fast_mode);
            LengthEncoderReset(rep_len_encoder, 1U << (int)options.pb, fast_mode);

            // Price counts: UINT32_MAX / 2 makes sure the price tables are initialized before use.
            match_price_count = uint.MaxValue / 2;
            align_price_count = uint.MaxValue / 2;

            opts_end_index = 0;
            opts_current_index = 0;

            return LzmaRet.OK;
        }

        /// <summary>lzma_lzma_encoder_create</summary>
        internal static LzmaRet Create(ref LzmaLzma1Encoder coder, ulong id, LzmaOptionsLzma options, ref LzEncoderOptions lz_options)
        {
            // Allocate lzma_lzma1_encoder if it wasn't already allocated.
            coder ??= new LzmaLzma1Encoder();

            // Set compression mode.
            switch (options.mode)
            {
                case LzmaMode.FAST:
                    coder.fast_mode = true;
                    break;

                case LzmaMode.NORMAL:
                    {
                        coder.fast_mode = false;

                        // Avoid integer overflow in the loop below.
                        if (options.dict_size > (1U << 30) + (1U << 29))
                            return LzmaRet.OPTIONS_ERROR;

                        // Set dist_table_size: round the dictionary size up to next 2^n.
                        uint log_size = 0;
                        while ((1U << (int)log_size) < options.dict_size)
                            ++log_size;

                        coder.dist_table_size = log_size * 2;

                        // Length encoders' price table size
                        uint nice_len = Math.Max(LzmaMF.GetHashBytes(options.mf), options.nice_len);

                        coder.match_len_encoder.table_size = nice_len + 1 - LzmaProbs.MATCH_LEN_MIN;
                        coder.rep_len_encoder.table_size = nice_len + 1 - LzmaProbs.MATCH_LEN_MIN;
                        break;
                    }

                default:
                    return LzmaRet.OPTIONS_ERROR;
            }

            // We don't need to write the first byte as literal if there is a non-empty preset dictionary.
            coder.is_initialized = options.preset_dict != null && options.preset_dict_size > 0;
            coder.is_flushed = false;
            coder.uncomp_size = 0;
            coder.out_limit = 0;

            // Determine if end marker is wanted.
            coder.use_eopm = id == LzmaConst.FILTER_LZMA1;
            if (id == LzmaConst.FILTER_LZMA1EXT)
            {
                // Check if unsupported flags are present.
                if ((options.ext_flags & ~LzmaConst.LZMA1EXT_ALLOW_EOPM) != 0)
                    return LzmaRet.OPTIONS_ERROR;

                coder.use_eopm = (options.ext_flags & LzmaConst.LZMA1EXT_ALLOW_EOPM) != 0;
            }

            SetLzOptions(ref lz_options, options);

            return coder.Reset(options);
        }

        /// <summary>lzma_lzma_encoder_init</summary>
        internal static LzmaRet EncoderInit(ref LzmaNextCoder next, LzmaFilterInfo[] filters, int index)
        {
            return LzEncoderCoder.EncoderInit(ref next, filters, index, (lz, id, options) =>
            {
                // lzma_encoder_init
                var lz_options = new LzEncoderOptions();
                if (options == null)
                    return (LzmaRet.PROG_ERROR, lz_options);
                var coder = lz.lz as LzmaLzma1Encoder;
                var ret = Create(ref coder, id, (LzmaOptionsLzma)options, ref lz_options);
                lz.lz = coder;
                return (ret, lz_options);
            });
        }

        /// <summary>lzma_lzma_encoder_memusage</summary>
        internal static ulong MemUsage(object options)
        {
            if (options is not LzmaOptionsLzma opt || !IsOptionsValid(opt))
                return ulong.MaxValue;

            var lz_options = new LzEncoderOptions();
            SetLzOptions(ref lz_options, opt);

            ulong lz_memusage = LzEncoderCoder.MemUsage(lz_options);
            if (lz_memusage == ulong.MaxValue)
                return ulong.MaxValue;

            // sizeof(lzma_lzma1_encoder)
            return 300000 + lz_memusage;
        }

        /// <summary>lzma_lzma_props_encode</summary>
        internal static LzmaRet PropsEncode(object options, byte[] output, int at)
        {
            var opt = (LzmaOptionsLzma)options;
            if (LzmaProbs.LcLpPbEncode(opt, out byte b))
                return LzmaRet.PROG_ERROR;
            output[at] = b;
            LzmaCore.Write32LE(output, at + 1, opt.dict_size);
            return LzmaRet.OK;
        }

        // ------------------------------------------------------------ lzma_encoder_optimum_fast.c

        // change_pair
        private static bool ChangePair(uint small_dist, uint big_dist) => (big_dist >> 7) > small_dist;

        // not_equal_16
        private static bool NotEqual16(byte[] buf, int a, int b) => buf[a] != buf[b] || buf[a + 1] != buf[b + 1];

        /// <summary>lzma_lzma_optimum_fast</summary>
        private void OptimumFast(LzmaMF mf, out uint back_res, out uint len_res)
        {
            uint nice_len = mf.nice_len;

            uint len_main;
            uint matches_count;
            if (mf.read_ahead == 0)
            {
                len_main = mf.Find(out matches_count, matches);
            }
            else
            {
                len_main = longest_match_length;
                matches_count = this.matches_count;
            }

            var buffer = mf.buffer;
            int buf = (int)mf.read_pos - 1;
            uint buf_avail = Math.Min(mf.Avail + 1, LzmaProbs.MATCH_LEN_MAX);

            // Not enough input for a match.
            if (buf_avail < 2)
            {
                back_res = uint.MaxValue;
                len_res = 1;
                return;
            }

            // Look for repeated matches; scan the previous four match distances.
            uint rep_len = 0;
            uint rep_index = 0;

            for (uint i = 0; i < LzmaProbs.REPS; ++i)
            {
                // Pointer to the beginning of the match candidate
                int buf_back = buf - (int)reps[i] - 1;

                // If the first two bytes (2 == MATCH_LEN_MIN) do not match, this rep is not useful.
                if (NotEqual16(buffer, buf, buf_back))
                    continue;

                // The first two bytes matched. Calculate the length of the match.
                uint len = LzmaMF.MemCmpLen(buffer, buf, buf_back, 2, buf_avail);

                // If we have found a repeated match that is at least nice_len long, return it immediately.
                if (len >= nice_len)
                {
                    back_res = i;
                    len_res = len;
                    mf.Skip(len - 1);
                    return;
                }

                if (len > rep_len)
                {
                    rep_index = i;
                    rep_len = len;
                }
            }

            // We didn't find a long enough repeated match. Encode it as a normal match if the match length is at least nice_len.
            if (len_main >= nice_len)
            {
                back_res = matches[matches_count - 1].dist + LzmaProbs.REPS;
                len_res = len_main;
                mf.Skip(len_main - 1);
                return;
            }

            uint back_main = 0;
            if (len_main >= 2)
            {
                back_main = matches[matches_count - 1].dist;

                while (matches_count > 1 && len_main == matches[matches_count - 2].len + 1)
                {
                    if (!ChangePair(matches[matches_count - 2].dist, back_main))
                        break;

                    --matches_count;
                    len_main = matches[matches_count - 1].len;
                    back_main = matches[matches_count - 1].dist;
                }

                if (len_main == 2 && back_main >= 0x80)
                    len_main = 1;
            }

            if (rep_len >= 2)
            {
                if (rep_len + 1 >= len_main
                        || (rep_len + 2 >= len_main && back_main > (1U << 9))
                        || (rep_len + 3 >= len_main && back_main > (1U << 15)))
                {
                    back_res = rep_index;
                    len_res = rep_len;
                    mf.Skip(rep_len - 1);
                    return;
                }
            }

            if (len_main < 2 || buf_avail <= 2)
            {
                back_res = uint.MaxValue;
                len_res = 1;
                return;
            }

            // Get the matches for the next byte. If we find a better match, the current byte is encoded as a literal.
            longest_match_length = mf.Find(out this.matches_count, matches);

            if (longest_match_length >= 2)
            {
                uint new_dist = matches[this.matches_count - 1].dist;

                if ((longest_match_length >= len_main && new_dist < back_main)
                        || (longest_match_length == len_main + 1 && !ChangePair(back_main, new_dist))
                        || (longest_match_length > len_main + 1)
                        || (longest_match_length + 1 >= len_main && len_main >= 3 && ChangePair(new_dist, back_main)))
                {
                    back_res = uint.MaxValue;
                    len_res = 1;
                    return;
                }
            }

            // In contrast to LZMA SDK, dictionary could not have been moved between mf_find() calls, thus it is safe to just increment *buf after the mf_find() call.
            ++buf;

            uint limit = Math.Max(2, len_main - 1);

            for (uint i = 0; i < LzmaProbs.REPS; ++i)
            {
                int other = buf - (int)reps[i] - 1;
                if (buffer.AsSpan(buf, (int)limit).SequenceEqual(buffer.AsSpan(other, (int)limit)))
                {
                    back_res = uint.MaxValue;
                    len_res = 1;
                    return;
                }
            }

            back_res = back_main + LzmaProbs.REPS;
            len_res = len_main;
            mf.Skip(len_main - 2);
        }

        // ------------------------------------------------------------ lzma_encoder_optimum_normal.c

        /// <summary>get_literal_price</summary>
        private uint GetLiteralPrice(uint pos, uint prev_byte, bool match_mode, uint match_byte, uint symbol)
        {
            int subcoder = LzmaProbs.LiteralSubcoder(literal_context_bits, literal_mask, pos, prev_byte);

            uint price = 0;

            if (!match_mode)
            {
                price = LzmaRangeEncoder.BitTreePrice(P, subcoder, 8, symbol);
            }
            else
            {
                uint offset = 0x100;
                symbol += 1U << 8;

                do
                {
                    match_byte <<= 1;

                    uint match_bit = match_byte & offset;
                    uint subcoder_index = offset + match_bit + (symbol >> 8);
                    uint bit = (symbol >> 7) & 1;
                    price += LzmaRangeEncoder.BitPrice(P[subcoder + (int)subcoder_index], bit);

                    symbol <<= 1;
                    offset &= ~(match_byte ^ symbol);
                } while (symbol < (1U << 16));
            }

            return price;
        }

        /// <summary>get_len_price</summary>
        private static uint GetLenPrice(LzmaLengthEncoder lencoder, uint len, uint pos_state) =>
            lencoder.prices[pos_state * LzmaProbs.LEN_SYMBOLS + len - LzmaProbs.MATCH_LEN_MIN];

        /// <summary>get_short_rep_price</summary>
        private uint GetShortRepPrice(uint state, uint pos_state) =>
            LzmaRangeEncoder.Bit0Price(P[LzmaProbs.IS_REP0 + (int)state])
            + LzmaRangeEncoder.Bit0Price(P[LzmaProbs.IS_REP0_LONG + (int)state * LzmaProbs.POS_STATES_MAX + (int)pos_state]);

        /// <summary>get_pure_rep_price</summary>
        private uint GetPureRepPrice(uint rep_index, uint state, uint pos_state)
        {
            uint price;

            if (rep_index == 0)
            {
                price = LzmaRangeEncoder.Bit0Price(P[LzmaProbs.IS_REP0 + (int)state]);
                price += LzmaRangeEncoder.Bit1Price(P[LzmaProbs.IS_REP0_LONG + (int)state * LzmaProbs.POS_STATES_MAX + (int)pos_state]);
            }
            else
            {
                price = LzmaRangeEncoder.Bit1Price(P[LzmaProbs.IS_REP0 + (int)state]);

                if (rep_index == 1)
                {
                    price += LzmaRangeEncoder.Bit0Price(P[LzmaProbs.IS_REP1 + (int)state]);
                }
                else
                {
                    price += LzmaRangeEncoder.Bit1Price(P[LzmaProbs.IS_REP1 + (int)state]);
                    price += LzmaRangeEncoder.BitPrice(P[LzmaProbs.IS_REP2 + (int)state], rep_index - 2);
                }
            }

            return price;
        }

        /// <summary>get_rep_price</summary>
        private uint GetRepPrice(uint rep_index, uint len, uint state, uint pos_state) =>
            GetLenPrice(rep_len_encoder, len, pos_state) + GetPureRepPrice(rep_index, state, pos_state);

        /// <summary>get_dist_len_price</summary>
        private uint GetDistLenPrice(uint dist, uint len, uint pos_state)
        {
            uint dist_state = LzmaProbs.GetDistState(len);
            uint price;

            if (dist < LzmaProbs.FULL_DISTANCES)
            {
                price = dist_prices[dist_state * LzmaProbs.FULL_DISTANCES + dist];
            }
            else
            {
                uint dist_slot = LzmaProbs.GetDistSlot(dist);
                price = dist_slot_prices[dist_state * LzmaProbs.DIST_SLOTS + dist_slot] + align_prices[dist & LzmaProbs.ALIGN_MASK];
            }

            price += GetLenPrice(match_len_encoder, len, pos_state);

            return price;
        }

        /// <summary>fill_dist_prices</summary>
        private void FillDistPrices()
        {
            for (uint dist_state = 0; dist_state < LzmaProbs.DIST_STATES; ++dist_state)
            {
                int row = (int)dist_state * LzmaProbs.DIST_SLOTS;

                // Price to encode the dist_slot.
                for (uint dist_slot = 0; dist_slot < dist_table_size; ++dist_slot)
                    dist_slot_prices[row + dist_slot] = LzmaRangeEncoder.BitTreePrice(P,
                        LzmaProbs.DIST_SLOT + (int)dist_state * LzmaProbs.DIST_SLOTS, LzmaProbs.DIST_SLOT_BITS, dist_slot);

                // For matches with distance >= FULL_DISTANCES, add the price of the direct bits part of the match distance.
                for (uint dist_slot = LzmaProbs.DIST_MODEL_END; dist_slot < dist_table_size; ++dist_slot)
                    dist_slot_prices[row + dist_slot] += LzmaRangeEncoder.DirectPrice(((dist_slot >> 1) - 1) - LzmaProbs.ALIGN_BITS);

                // Distances in the range [0, 3] are fully encoded with dist_slot, so they are used for dist_prices as is.
                for (uint i = 0; i < LzmaProbs.DIST_MODEL_START; ++i)
                    dist_prices[dist_state * LzmaProbs.FULL_DISTANCES + i] = dist_slot_prices[row + i];
            }

            // Distances in the range [4, 127] depend on dist_slot and dist_special.
            for (uint i = LzmaProbs.DIST_MODEL_START; i < LzmaProbs.FULL_DISTANCES; ++i)
            {
                uint dist_slot = LzmaProbs.GetDistSlot(i);
                int footer_bits = (int)((dist_slot >> 1) - 1);
                uint @base = (2 | (dist_slot & 1)) << footer_bits;
                uint price = LzmaRangeEncoder.BitTreeReversePrice(P,
                    LzmaProbs.DIST_SPECIAL + (int)@base - (int)dist_slot - 1, footer_bits, i - @base);

                for (uint dist_state = 0; dist_state < LzmaProbs.DIST_STATES; ++dist_state)
                    dist_prices[dist_state * LzmaProbs.FULL_DISTANCES + i] = price + dist_slot_prices[dist_state * LzmaProbs.DIST_SLOTS + dist_slot];
            }

            match_price_count = 0;
        }

        /// <summary>fill_align_prices</summary>
        private void FillAlignPrices()
        {
            for (uint i = 0; i < LzmaProbs.ALIGN_SIZE; ++i)
                align_prices[i] = LzmaRangeEncoder.BitTreeReversePrice(P, LzmaProbs.DIST_ALIGN, LzmaProbs.ALIGN_BITS, i);

            align_price_count = 0;
        }

        // make_literal
        private static void MakeLiteral(ref LzmaOptimal optimal)
        {
            optimal.back_prev = uint.MaxValue;
            optimal.prev_1_is_literal = false;
        }

        // make_short_rep
        private static void MakeShortRep(ref LzmaOptimal optimal)
        {
            optimal.back_prev = 0;
            optimal.prev_1_is_literal = false;
        }

        /// <summary>backward</summary>
        private void Backward(out uint len_res, out uint back_res, uint cur)
        {
            opts_end_index = cur;

            uint pos_mem = opts[cur].pos_prev;
            uint back_mem = opts[cur].back_prev;

            do
            {
                if (opts[cur].prev_1_is_literal)
                {
                    MakeLiteral(ref opts[pos_mem]);
                    opts[pos_mem].pos_prev = pos_mem - 1;

                    if (opts[cur].prev_2)
                    {
                        opts[pos_mem - 1].prev_1_is_literal = false;
                        opts[pos_mem - 1].pos_prev = opts[cur].pos_prev_2;
                        opts[pos_mem - 1].back_prev = opts[cur].back_prev_2;
                    }
                }

                uint pos_prev = pos_mem;
                uint back_cur = back_mem;

                back_mem = opts[pos_prev].back_prev;
                pos_mem = opts[pos_prev].pos_prev;

                opts[pos_prev].back_prev = back_cur;
                opts[pos_prev].pos_prev = cur;
                cur = pos_prev;
            } while (cur != 0);

            opts_current_index = opts[0].pos_prev;
            len_res = opts[0].pos_prev;
            back_res = opts[0].back_prev;
        }

        /// <summary>helper1</summary>
        private uint Helper1(LzmaMF mf, out uint back_res, out uint len_res, uint position)
        {
            uint nice_len = mf.nice_len;

            uint len_main;
            uint matches_count;

            if (mf.read_ahead == 0)
            {
                len_main = mf.Find(out matches_count, matches);
            }
            else
            {
                len_main = longest_match_length;
                matches_count = this.matches_count;
            }

            uint buf_avail = Math.Min(mf.Avail + 1, LzmaProbs.MATCH_LEN_MAX);
            if (buf_avail < 2)
            {
                back_res = uint.MaxValue;
                len_res = 1;
                return uint.MaxValue;
            }

            var buffer = mf.buffer;
            int buf = (int)mf.read_pos - 1;

            Span<uint> rep_lens = stackalloc uint[LzmaProbs.REPS];
            uint rep_max_index = 0;

            for (uint i = 0; i < LzmaProbs.REPS; ++i)
            {
                int buf_back = buf - (int)reps[i] - 1;

                if (NotEqual16(buffer, buf, buf_back))
                {
                    rep_lens[(int)i] = 0;
                    continue;
                }

                rep_lens[(int)i] = LzmaMF.MemCmpLen(buffer, buf, buf_back, 2, buf_avail);

                if (rep_lens[(int)i] > rep_lens[(int)rep_max_index])
                    rep_max_index = i;
            }

            if (rep_lens[(int)rep_max_index] >= nice_len)
            {
                back_res = rep_max_index;
                len_res = rep_lens[(int)rep_max_index];
                mf.Skip(len_res - 1);
                return uint.MaxValue;
            }

            if (len_main >= nice_len)
            {
                back_res = matches[matches_count - 1].dist + LzmaProbs.REPS;
                len_res = len_main;
                mf.Skip(len_main - 1);
                return uint.MaxValue;
            }

            byte current_byte = buffer[buf];
            byte match_byte = buffer[buf - (int)reps[0] - 1];

            if (len_main < 2 && current_byte != match_byte && rep_lens[(int)rep_max_index] < 2)
            {
                back_res = uint.MaxValue;
                len_res = 1;
                return uint.MaxValue;
            }

            opts[0].state = state;

            uint pos_state = position & pos_mask;

            opts[1].price = LzmaRangeEncoder.Bit0Price(P[LzmaProbs.IS_MATCH + (int)state * LzmaProbs.POS_STATES_MAX + (int)pos_state])
                + GetLiteralPrice(position, buffer[buf - 1], !LzmaProbs.IsLiteralState(state), match_byte, current_byte);

            MakeLiteral(ref opts[1]);

            uint match_price = LzmaRangeEncoder.Bit1Price(P[LzmaProbs.IS_MATCH + (int)state * LzmaProbs.POS_STATES_MAX + (int)pos_state]);
            uint rep_match_price = match_price + LzmaRangeEncoder.Bit1Price(P[LzmaProbs.IS_REP + (int)state]);

            if (match_byte == current_byte)
            {
                uint short_rep_price = rep_match_price + GetShortRepPrice(state, pos_state);

                if (short_rep_price < opts[1].price)
                {
                    opts[1].price = short_rep_price;
                    MakeShortRep(ref opts[1]);
                }
            }

            uint len_end = Math.Max(len_main, rep_lens[(int)rep_max_index]);

            if (len_end < 2)
            {
                back_res = opts[1].back_prev;
                len_res = 1;
                return uint.MaxValue;
            }

            opts[1].pos_prev = 0;

            opts[0].backs0 = reps[0];
            opts[0].backs1 = reps[1];
            opts[0].backs2 = reps[2];
            opts[0].backs3 = reps[3];

            uint len = len_end;
            do
            {
                opts[len].price = LzmaRangeEncoder.RC_INFINITY_PRICE;
            } while (--len >= 2);

            for (uint i = 0; i < LzmaProbs.REPS; ++i)
            {
                uint rep_len = rep_lens[(int)i];
                if (rep_len < 2)
                    continue;

                uint price = rep_match_price + GetPureRepPrice(i, state, pos_state);

                do
                {
                    uint cur_and_len_price = price + GetLenPrice(rep_len_encoder, rep_len, pos_state);

                    if (cur_and_len_price < opts[rep_len].price)
                    {
                        opts[rep_len].price = cur_and_len_price;
                        opts[rep_len].pos_prev = 0;
                        opts[rep_len].back_prev = i;
                        opts[rep_len].prev_1_is_literal = false;
                    }
                } while (--rep_len >= 2);
            }

            uint normal_match_price = match_price + LzmaRangeEncoder.Bit0Price(P[LzmaProbs.IS_REP + (int)state]);

            len = rep_lens[0] >= 2 ? rep_lens[0] + 1 : 2;
            if (len <= len_main)
            {
                uint i = 0;
                while (len > matches[i].len)
                    ++i;

                for (; ; ++len)
                {
                    uint dist = matches[i].dist;
                    uint cur_and_len_price = normal_match_price + GetDistLenPrice(dist, len, pos_state);

                    if (cur_and_len_price < opts[len].price)
                    {
                        opts[len].price = cur_and_len_price;
                        opts[len].pos_prev = 0;
                        opts[len].back_prev = dist + LzmaProbs.REPS;
                        opts[len].prev_1_is_literal = false;
                    }

                    if (len == matches[i].len)
                        if (++i == matches_count)
                            break;
                }
            }

            back_res = 0;
            len_res = 0;
            return len_end;
        }

        /// <summary>helper2</summary>
        private uint Helper2(Span<uint> reps, byte[] buffer, int buf, uint len_end, uint position, uint cur,
            uint nice_len, uint buf_avail_full)
        {
            uint matches_count = this.matches_count;
            uint new_len = longest_match_length;
            uint pos_prev = opts[cur].pos_prev;
            uint state;

            if (opts[cur].prev_1_is_literal)
            {
                --pos_prev;

                if (opts[cur].prev_2)
                {
                    state = opts[opts[cur].pos_prev_2].state;

                    if (opts[cur].back_prev_2 < LzmaProbs.REPS)
                        state = LzmaProbs.UpdateLongRep(state);
                    else
                        state = LzmaProbs.UpdateMatch(state);
                }
                else
                {
                    state = opts[pos_prev].state;
                }

                state = LzmaProbs.UpdateLiteral(state);
            }
            else
            {
                state = opts[pos_prev].state;
            }

            if (pos_prev == cur - 1)
            {
                if (opts[cur].back_prev == 0) // is_short_rep
                    state = LzmaProbs.UpdateShortRep(state);
                else
                    state = LzmaProbs.UpdateLiteral(state);
            }
            else
            {
                uint pos;
                if (opts[cur].prev_1_is_literal && opts[cur].prev_2)
                {
                    pos_prev = opts[cur].pos_prev_2;
                    pos = opts[cur].back_prev_2;
                    state = LzmaProbs.UpdateLongRep(state);
                }
                else
                {
                    pos = opts[cur].back_prev;
                    if (pos < LzmaProbs.REPS)
                        state = LzmaProbs.UpdateLongRep(state);
                    else
                        state = LzmaProbs.UpdateMatch(state);
                }

                if (pos < LzmaProbs.REPS)
                {
                    reps[0] = opts[pos_prev].GetBack(pos);

                    uint i;
                    for (i = 1; i <= pos; ++i)
                        reps[(int)i] = opts[pos_prev].GetBack(i - 1);

                    for (; i < LzmaProbs.REPS; ++i)
                        reps[(int)i] = opts[pos_prev].GetBack(i);
                }
                else
                {
                    reps[0] = pos - LzmaProbs.REPS;

                    for (uint i = 1; i < LzmaProbs.REPS; ++i)
                        reps[(int)i] = opts[pos_prev].GetBack(i - 1);
                }
            }

            opts[cur].state = state;

            opts[cur].backs0 = reps[0];
            opts[cur].backs1 = reps[1];
            opts[cur].backs2 = reps[2];
            opts[cur].backs3 = reps[3];

            uint cur_price = opts[cur].price;

            byte current_byte = buffer[buf];
            byte match_byte = buffer[buf - (int)reps[0] - 1];

            uint pos_state = position & pos_mask;

            uint cur_and_1_price = cur_price
                + LzmaRangeEncoder.Bit0Price(P[LzmaProbs.IS_MATCH + (int)state * LzmaProbs.POS_STATES_MAX + (int)pos_state])
                + GetLiteralPrice(position, buffer[buf - 1], !LzmaProbs.IsLiteralState(state), match_byte, current_byte);

            bool next_is_literal = false;

            if (cur_and_1_price < opts[cur + 1].price)
            {
                opts[cur + 1].price = cur_and_1_price;
                opts[cur + 1].pos_prev = cur;
                MakeLiteral(ref opts[cur + 1]);
                next_is_literal = true;
            }

            uint match_price = cur_price + LzmaRangeEncoder.Bit1Price(P[LzmaProbs.IS_MATCH + (int)state * LzmaProbs.POS_STATES_MAX + (int)pos_state]);
            uint rep_match_price = match_price + LzmaRangeEncoder.Bit1Price(P[LzmaProbs.IS_REP + (int)state]);

            if (match_byte == current_byte && !(opts[cur + 1].pos_prev < cur && opts[cur + 1].back_prev == 0))
            {
                uint short_rep_price = rep_match_price + GetShortRepPrice(state, pos_state);

                if (short_rep_price <= opts[cur + 1].price)
                {
                    opts[cur + 1].price = short_rep_price;
                    opts[cur + 1].pos_prev = cur;
                    MakeShortRep(ref opts[cur + 1]);
                    next_is_literal = true;
                }
            }

            if (buf_avail_full < 2)
                return len_end;

            uint buf_avail = Math.Min(buf_avail_full, nice_len);

            if (!next_is_literal && match_byte != current_byte)
            { // speed optimization
                // try literal + rep0
                int buf_back = buf - (int)reps[0] - 1;
                uint limit = Math.Min(buf_avail_full, nice_len + 1);

                uint len_test = LzmaMF.MemCmpLen(buffer, buf, buf_back, 1, limit) - 1;

                if (len_test >= 2)
                {
                    uint state_2 = LzmaProbs.UpdateLiteral(state);

                    uint pos_state_next = (position + 1) & pos_mask;

                    uint next_rep_match_price = cur_and_1_price
                        + LzmaRangeEncoder.Bit1Price(P[LzmaProbs.IS_MATCH + (int)state_2 * LzmaProbs.POS_STATES_MAX + (int)pos_state_next])
                        + LzmaRangeEncoder.Bit1Price(P[LzmaProbs.IS_REP + (int)state_2]);

                    uint offset = cur + 1 + len_test;

                    while (len_end < offset)
                        opts[++len_end].price = LzmaRangeEncoder.RC_INFINITY_PRICE;

                    uint cur_and_len_price = next_rep_match_price + GetRepPrice(0, len_test, state_2, pos_state_next);

                    if (cur_and_len_price < opts[offset].price)
                    {
                        opts[offset].price = cur_and_len_price;
                        opts[offset].pos_prev = cur + 1;
                        opts[offset].back_prev = 0;
                        opts[offset].prev_1_is_literal = true;
                        opts[offset].prev_2 = false;
                    }
                }
            }

            uint start_len = 2; // speed optimization

            for (uint rep_index = 0; rep_index < LzmaProbs.REPS; ++rep_index)
            {
                int buf_back = buf - (int)reps[(int)rep_index] - 1;
                if (NotEqual16(buffer, buf, buf_back))
                    continue;

                uint len_test = LzmaMF.MemCmpLen(buffer, buf, buf_back, 2, buf_avail);

                while (len_end < cur + len_test)
                    opts[++len_end].price = LzmaRangeEncoder.RC_INFINITY_PRICE;

                uint len_test_temp = len_test;
                uint price = rep_match_price + GetPureRepPrice(rep_index, state, pos_state);

                do
                {
                    uint cur_and_len_price = price + GetLenPrice(rep_len_encoder, len_test, pos_state);

                    if (cur_and_len_price < opts[cur + len_test].price)
                    {
                        opts[cur + len_test].price = cur_and_len_price;
                        opts[cur + len_test].pos_prev = cur;
                        opts[cur + len_test].back_prev = rep_index;
                        opts[cur + len_test].prev_1_is_literal = false;
                    }
                } while (--len_test >= 2);

                len_test = len_test_temp;

                if (rep_index == 0)
                    start_len = len_test + 1;

                uint len_test_2 = len_test + 1;
                uint limit = Math.Min(buf_avail_full, len_test_2 + nice_len);

                // NOTE: len_test_2 may be greater than limit so the call to lzma_memcmplen() must be done conditionally.
                if (len_test_2 < limit)
                    len_test_2 = LzmaMF.MemCmpLen(buffer, buf, buf_back, len_test_2, limit);

                len_test_2 -= len_test + 1;

                if (len_test_2 >= 2)
                {
                    uint state_2 = LzmaProbs.UpdateLongRep(state);

                    uint pos_state_next = (position + len_test) & pos_mask;

                    uint cur_and_len_literal_price = price
                        + GetLenPrice(rep_len_encoder, len_test, pos_state)
                        + LzmaRangeEncoder.Bit0Price(P[LzmaProbs.IS_MATCH + (int)state_2 * LzmaProbs.POS_STATES_MAX + (int)pos_state_next])
                        + GetLiteralPrice(position + len_test, buffer[buf + (int)len_test - 1], true,
                            buffer[buf_back + (int)len_test], buffer[buf + (int)len_test]);

                    state_2 = LzmaProbs.UpdateLiteral(state_2);

                    pos_state_next = (position + len_test + 1) & pos_mask;

                    uint next_rep_match_price = cur_and_len_literal_price
                        + LzmaRangeEncoder.Bit1Price(P[LzmaProbs.IS_MATCH + (int)state_2 * LzmaProbs.POS_STATES_MAX + (int)pos_state_next])
                        + LzmaRangeEncoder.Bit1Price(P[LzmaProbs.IS_REP + (int)state_2]);

                    uint offset = cur + len_test + 1 + len_test_2;

                    while (len_end < offset)
                        opts[++len_end].price = LzmaRangeEncoder.RC_INFINITY_PRICE;

                    uint cur_and_len_price = next_rep_match_price + GetRepPrice(0, len_test_2, state_2, pos_state_next);

                    if (cur_and_len_price < opts[offset].price)
                    {
                        opts[offset].price = cur_and_len_price;
                        opts[offset].pos_prev = cur + len_test + 1;
                        opts[offset].back_prev = 0;
                        opts[offset].prev_1_is_literal = true;
                        opts[offset].prev_2 = true;
                        opts[offset].pos_prev_2 = cur;
                        opts[offset].back_prev_2 = rep_index;
                    }
                }
            }

            if (new_len > buf_avail)
            {
                new_len = buf_avail;

                matches_count = 0;
                while (new_len > matches[matches_count].len)
                    ++matches_count;

                matches[matches_count++].len = new_len;
            }

            if (new_len >= start_len)
            {
                uint normal_match_price = match_price + LzmaRangeEncoder.Bit0Price(P[LzmaProbs.IS_REP + (int)state]);

                while (len_end < cur + new_len)
                    opts[++len_end].price = LzmaRangeEncoder.RC_INFINITY_PRICE;

                uint i = 0;
                while (start_len > matches[i].len)
                    ++i;

                for (uint len_test = start_len; ; ++len_test)
                {
                    uint cur_back = matches[i].dist;
                    uint cur_and_len_price = normal_match_price + GetDistLenPrice(cur_back, len_test, pos_state);

                    if (cur_and_len_price < opts[cur + len_test].price)
                    {
                        opts[cur + len_test].price = cur_and_len_price;
                        opts[cur + len_test].pos_prev = cur;
                        opts[cur + len_test].back_prev = cur_back + LzmaProbs.REPS;
                        opts[cur + len_test].prev_1_is_literal = false;
                    }

                    if (len_test == matches[i].len)
                    {
                        // Try Match + Literal + Rep0
                        int buf_back = buf - (int)cur_back - 1;
                        uint len_test_2 = len_test + 1;
                        uint limit = Math.Min(buf_avail_full, len_test_2 + nice_len);

                        if (len_test_2 < limit)
                            len_test_2 = LzmaMF.MemCmpLen(buffer, buf, buf_back, len_test_2, limit);

                        len_test_2 -= len_test + 1;

                        if (len_test_2 >= 2)
                        {
                            uint state_2 = LzmaProbs.UpdateMatch(state);
                            uint pos_state_next = (position + len_test) & pos_mask;

                            uint cur_and_len_literal_price = cur_and_len_price
                                + LzmaRangeEncoder.Bit0Price(P[LzmaProbs.IS_MATCH + (int)state_2 * LzmaProbs.POS_STATES_MAX + (int)pos_state_next])
                                + GetLiteralPrice(position + len_test, buffer[buf + (int)len_test - 1], true,
                                    buffer[buf_back + (int)len_test], buffer[buf + (int)len_test]);

                            state_2 = LzmaProbs.UpdateLiteral(state_2);
                            pos_state_next = (pos_state_next + 1) & pos_mask;

                            uint next_rep_match_price = cur_and_len_literal_price
                                + LzmaRangeEncoder.Bit1Price(P[LzmaProbs.IS_MATCH + (int)state_2 * LzmaProbs.POS_STATES_MAX + (int)pos_state_next])
                                + LzmaRangeEncoder.Bit1Price(P[LzmaProbs.IS_REP + (int)state_2]);

                            uint offset = cur + len_test + 1 + len_test_2;

                            while (len_end < offset)
                                opts[++len_end].price = LzmaRangeEncoder.RC_INFINITY_PRICE;

                            cur_and_len_price = next_rep_match_price + GetRepPrice(0, len_test_2, state_2, pos_state_next);

                            if (cur_and_len_price < opts[offset].price)
                            {
                                opts[offset].price = cur_and_len_price;
                                opts[offset].pos_prev = cur + len_test + 1;
                                opts[offset].back_prev = 0;
                                opts[offset].prev_1_is_literal = true;
                                opts[offset].prev_2 = true;
                                opts[offset].pos_prev_2 = cur;
                                opts[offset].back_prev_2 = cur_back + LzmaProbs.REPS;
                            }
                        }

                        if (++i == matches_count)
                            break;
                    }
                }
            }

            return len_end;
        }

        /// <summary>lzma_lzma_optimum_normal</summary>
        private void OptimumNormal(LzmaMF mf, out uint back_res, out uint len_res, uint position)
        {
            // If we have symbols pending, return the next pending symbol.
            if (opts_end_index != opts_current_index)
            {
                len_res = opts[opts_current_index].pos_prev - opts_current_index;
                back_res = opts[opts_current_index].back_prev;
                opts_current_index = opts[opts_current_index].pos_prev;
                return;
            }

            // Update the price tables. In LZMA SDK <= 4.60 (and possibly later) this was done in both
            // initialization function and in the main loop. In liblzma they were moved into this single place.
            if (mf.read_ahead == 0)
            {
                if (match_price_count >= (1 << 7))
                    FillDistPrices();

                if (align_price_count >= LzmaProbs.ALIGN_SIZE)
                    FillAlignPrices();
            }

            // TODO: This needs quite a bit of cleaning still. But splitting the original function into two pieces makes it at least a little more readable, since those two parts don't share many variables.
            uint len_end = Helper1(mf, out back_res, out len_res, position);
            if (len_end == uint.MaxValue)
                return;

            Span<uint> reps = stackalloc uint[LzmaProbs.REPS];
            reps[0] = this.reps[0];
            reps[1] = this.reps[1];
            reps[2] = this.reps[2];
            reps[3] = this.reps[3];

            uint cur;
            for (cur = 1; cur < len_end; ++cur)
            {
                longest_match_length = mf.Find(out matches_count, matches);

                if (longest_match_length >= mf.nice_len)
                    break;

                len_end = Helper2(reps, mf.buffer, (int)mf.read_pos - 1, len_end, position + cur, cur, mf.nice_len,
                    Math.Min(mf.Avail + 1, OPTS - 1 - cur));
            }

            Backward(out len_res, out back_res, cur);
        }
    }

    /// <summary>lzma/lzma2_encoder.c</summary>
    internal sealed class LzmaLzma2Encoder : ILzEncoder
    {
        internal const int LZMA2_CHUNK_MAX = 1 << 16, LZMA2_UNCOMPRESSED_MAX = 1 << 21;
        internal const int LZMA2_HEADER_MAX = 6, LZMA2_HEADER_UNCOMPRESSED = 3;

        private const int SEQ_INIT = 0, SEQ_LZMA_ENCODE = 1, SEQ_LZMA_COPY = 2, SEQ_UNCOMPRESSED_HEADER = 3,
            SEQ_UNCOMPRESSED_COPY = 4;

        private int sequence;

        // LZMA encoder
        private LzmaLzma1Encoder lzma;

        // LZMA options currently in use.
        private LzmaOptionsLzma opt_cur;

        private bool need_properties;
        private bool need_state_reset;
        private bool need_dictionary_reset;

        // Uncompressed size of a chunk
        private uint uncompressed_size;

        // Compressed size of a chunk (excluding headers); this is also used to indicate the end of buf[] in SEQ_LZMA_COPY.
        private int compressed_size;

        // Read position in buf[]
        private int buf_pos;

        // Buffer to hold the chunk header and LZMA compressed data
        private readonly byte[] buf = new byte[LZMA2_HEADER_MAX + LZMA2_CHUNK_MAX];

        /// <summary>lzma2_header_lzma</summary>
        private void HeaderLzma()
        {
            int pos;

            if (need_properties)
            {
                pos = 0;

                if (need_dictionary_reset)
                    buf[pos] = 0x80 + (3 << 5);
                else
                    buf[pos] = 0x80 + (2 << 5);
            }
            else
            {
                pos = 1;

                if (need_state_reset)
                    buf[pos] = 0x80 + (1 << 5);
                else
                    buf[pos] = 0x80;
            }

            // Set the start position for copying.
            buf_pos = pos;

            // Uncompressed size
            uint size = uncompressed_size - 1;
            buf[pos++] += (byte)(size >> 16);
            buf[pos++] = (byte)((size >> 8) & 0xFF);
            buf[pos++] = (byte)(size & 0xFF);

            // Compressed size
            size = (uint)compressed_size - 1;
            buf[pos++] = (byte)(size >> 8);
            buf[pos++] = (byte)(size & 0xFF);

            // Properties, if needed
            if (need_properties)
            {
                LzmaProbs.LcLpPbEncode(opt_cur, out byte b);
                buf[pos] = b;
            }

            need_properties = false;
            need_state_reset = false;
            need_dictionary_reset = false;

            // The copying code uses coder->compressed_size to indicate the end of coder->buf[], so we need add the maximum size of the LZMA2 header here.
            compressed_size += LZMA2_HEADER_MAX;
        }

        /// <summary>lzma2_header_uncompressed</summary>
        private void HeaderUncompressed()
        {
            if (need_dictionary_reset)
                buf[0] = 1;
            else
                buf[0] = 2;

            need_dictionary_reset = false;

            // "Compressed" size
            buf[1] = (byte)((uncompressed_size - 1) >> 8);
            buf[2] = (byte)((uncompressed_size - 1) & 0xFF);

            // Set the start position for copying.
            buf_pos = 0;
        }

        /// <summary>lzma2_encode</summary>
        public LzmaRet Code(LzmaMF mf, byte[] output, ref int out_pos, int out_size)
        {
            while (out_pos < out_size)
            {
                switch (sequence)
                {
                    case SEQ_INIT:
                        // If there's no input left and we are flushing or finishing, don't start a new chunk.
                        if (mf.Unencoded == 0)
                        {
                            // Write end of payload marker if finishing.
                            if (mf.action == LzmaAction.FINISH)
                                output[out_pos++] = 0;

                            return mf.action == LzmaAction.RUN ? LzmaRet.OK : LzmaRet.STREAM_END;
                        }

                        if (need_state_reset)
                        {
                            var r = lzma.Reset(opt_cur);
                            if (r != LzmaRet.OK)
                                return r;
                        }

                        uncompressed_size = 0;
                        compressed_size = 0;
                        sequence = SEQ_LZMA_ENCODE;
                        goto case SEQ_LZMA_ENCODE;

                    case SEQ_LZMA_ENCODE:
                        {
                            // Calculate how much more uncompressed data this chunk could accept.
                            uint left = LZMA2_UNCOMPRESSED_MAX - uncompressed_size;
                            uint limit;

                            if (left < mf.match_len_max)
                            {
                                // Must flush immediately since the next LZMA symbol could make the uncompressed size of the chunk too big.
                                limit = 0;
                            }
                            else
                            {
                                // Calculate maximum read_limit that is OK from point of view of LZMA2 chunk size.
                                limit = mf.read_pos - mf.read_ahead + left - mf.match_len_max;
                            }

                            // Save the start position so that we can update coder->uncompressed_size.
                            uint read_start = mf.read_pos - mf.read_ahead;

                            // Call the LZMA encoder until the chunk is finished.
                            int cpos = LZMA2_HEADER_MAX + compressed_size;
                            var ret = lzma.Encode(mf, buf, ref cpos, LZMA2_HEADER_MAX + LZMA2_CHUNK_MAX, limit, LZMA2_HEADER_MAX);
                            compressed_size = cpos - LZMA2_HEADER_MAX;

                            uncompressed_size += mf.read_pos - mf.read_ahead - read_start;

                            if (ret != LzmaRet.STREAM_END)
                                return LzmaRet.OK;

                            // See if the chunk compressed. If it didn't, we encode it as uncompressed chunk.
                            if ((uint)compressed_size >= uncompressed_size)
                            {
                                uncompressed_size += mf.read_ahead;
                                mf.read_ahead = 0;
                                HeaderUncompressed();
                                need_state_reset = true;
                                sequence = SEQ_UNCOMPRESSED_HEADER;
                                break;
                            }

                            // The chunk did compress at least by one byte, so we store the chunk as LZMA.
                            HeaderLzma();

                            sequence = SEQ_LZMA_COPY;
                            goto case SEQ_LZMA_COPY;
                        }

                    case SEQ_LZMA_COPY:
                        // Copy the compressed chunk along its headers to the output buffer.
                        LzmaCore.BufCpy(buf, ref buf_pos, compressed_size, output, ref out_pos, out_size);
                        if (buf_pos != compressed_size)
                            return LzmaRet.OK;

                        sequence = SEQ_INIT;
                        break;

                    case SEQ_UNCOMPRESSED_HEADER:
                        // Copy the three-byte header to indicate uncompressed chunk.
                        LzmaCore.BufCpy(buf, ref buf_pos, LZMA2_HEADER_UNCOMPRESSED, output, ref out_pos, out_size);
                        if (buf_pos != LZMA2_HEADER_UNCOMPRESSED)
                            return LzmaRet.OK;

                        sequence = SEQ_UNCOMPRESSED_COPY;
                        goto case SEQ_UNCOMPRESSED_COPY;

                    case SEQ_UNCOMPRESSED_COPY:
                        // Copy the uncompressed data as is from the dictionary to the output buffer.
                        mf.Read(output, ref out_pos, out_size, ref uncompressed_size);
                        if (uncompressed_size != 0)
                            return LzmaRet.OK;

                        sequence = SEQ_INIT;
                        break;
                }
            }

            return LzmaRet.OK;
        }

        /// <summary>lzma2_encoder_options_update</summary>
        public LzmaRet OptionsUpdate(LzmaFilter filter)
        {
            // New options can be set only when there is no incomplete chunk.
            if (filter.options == null || sequence != SEQ_INIT)
                return LzmaRet.PROG_ERROR;

            // Look if there are new options. At least for now, only lc/lp/pb can be changed.
            var opt = (LzmaOptionsLzma)filter.options;
            if (opt_cur.lc != opt.lc || opt_cur.lp != opt.lp || opt_cur.pb != opt.pb)
            {
                // Validate the options.
                if (opt.lc > LzmaConst.LCLP_MAX || opt.lp > LzmaConst.LCLP_MAX
                        || opt.lc + opt.lp > LzmaConst.LCLP_MAX || opt.pb > LzmaConst.PB_MAX)
                    return LzmaRet.OPTIONS_ERROR;

                // The new options will be used when the encoder starts a new LZMA2 chunk.
                opt_cur.lc = opt.lc;
                opt_cur.lp = opt.lp;
                opt_cur.pb = opt.pb;
                need_properties = true;
                need_state_reset = true;
            }

            return LzmaRet.OK;
        }

        /// <summary>lzma2_encoder_init</summary>
        private static (LzmaRet, LzEncoderOptions) Init(LzEncoderCoder lz, object options)
        {
            var lz_options = new LzEncoderOptions();
            if (options == null)
                return (LzmaRet.PROG_ERROR, lz_options);

            if (lz.lz is not LzmaLzma2Encoder coder)
            {
                coder = new LzmaLzma2Encoder();
                lz.lz = coder;
            }

            coder.opt_cur = ((LzmaOptionsLzma)options).Clone();

            coder.sequence = SEQ_INIT;
            coder.need_properties = true;
            coder.need_state_reset = false;
            coder.need_dictionary_reset = coder.opt_cur.preset_dict == null || coder.opt_cur.preset_dict_size == 0;

            // Initialize LZMA encoder
            var ret = LzmaLzma1Encoder.Create(ref coder.lzma, LzmaConst.FILTER_LZMA2, coder.opt_cur, ref lz_options);
            if (ret != LzmaRet.OK)
                return (ret, lz_options);

            // Make sure that we will always have enough history available in case we need to use uncompressed chunks.
            if (lz_options.before_size + lz_options.dict_size < LZMA2_CHUNK_MAX)
                lz_options.before_size = LZMA2_CHUNK_MAX - lz_options.dict_size;

            return (LzmaRet.OK, lz_options);
        }

        /// <summary>lzma_lzma2_encoder_init</summary>
        internal static LzmaRet EncoderInit(ref LzmaNextCoder next, LzmaFilterInfo[] filters, int index) =>
            LzEncoderCoder.EncoderInit(ref next, filters, index, (lz, id, options) => Init(lz, options));

        /// <summary>lzma_lzma2_encoder_memusage</summary>
        internal static ulong MemUsage(object options)
        {
            if (options is not LzmaOptionsLzma opt)
                return ulong.MaxValue;
            if (!LzEncoderCoder.IsEncDictSizeValid(opt.dict_size))
                return ulong.MaxValue;
            ulong lzma_mem = LzmaLzma1Encoder.MemUsage(options);
            if (lzma_mem == ulong.MaxValue)
                return ulong.MaxValue;

            return LZMA2_HEADER_MAX + LZMA2_CHUNK_MAX + 256 + lzma_mem;
        }

        /// <summary>lzma_lzma2_props_encode</summary>
        internal static LzmaRet PropsEncode(object options, byte[] output, int at)
        {
            if (options == null)
                return LzmaRet.PROG_ERROR;

            var opt = (LzmaOptionsLzma)options;
            uint d = Math.Max(opt.dict_size, LzmaConst.DICT_SIZE_MIN);

            // Round up to the next 2^n - 1 or 2^n + 2^(n - 1) - 1 depending on which one is the next:
            --d;
            d |= d >> 2;
            d |= d >> 3;
            d |= d >> 4;
            d |= d >> 8;
            d |= d >> 16;

            // Get the highest two bits using the proper encoding:
            if (d == uint.MaxValue)
                output[at] = 40;
            else
                output[at] = (byte)(LzmaProbs.GetDistSlot(d + 1) - 24);

            return LzmaRet.OK;
        }
    }
}
