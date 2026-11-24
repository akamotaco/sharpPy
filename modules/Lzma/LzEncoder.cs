using System;

namespace SharpPy.Modules.Lzma
{
    /// <summary>lzma_match</summary>
    internal struct LzmaMatch
    {
        internal uint len;
        internal uint dist;
    }

    /// <summary>lzma_lz_options (encoder side)</summary>
    internal struct LzEncoderOptions
    {
        internal uint before_size;
        internal uint dict_size;
        internal uint after_size;
        internal uint match_len_max;
        internal uint nice_len;
        internal LzmaMatchFinder match_finder;
        internal uint depth;
        internal byte[] preset_dict;
        internal uint preset_dict_size;
    }

    /// <summary>lzma_lz_encoder: the LZ-based encoder (LZMA, LZMA2) behind the match finder.</summary>
    internal interface ILzEncoder
    {
        /// <summary>code</summary>
        LzmaRet Code(LzmaMF mf, byte[] output, ref int out_pos, int out_size);

        /// <summary>options_update; PROG_ERROR when the coder has none.</summary>
        LzmaRet OptionsUpdate(LzmaFilter filter);
    }

    /// <summary>
    /// lzma_mf (lz/lz_encoder.h) with the match finders of lz/lz_encoder_mf.c (HC3, HC4, BT2,
    /// BT3, BT4) and the inline helpers of lz_encoder.h. Pointers into buffer are indices.
    /// </summary>
    internal sealed class LzmaMF
    {
        // LZMA_MEMCMPLEN_EXTRA: the zeroed bytes after the data that the 64-bit memcmplen may read
        internal const int MEMCMPLEN_EXTRA = 8;

        internal const uint HASH_2_SIZE = 1U << 10, HASH_3_SIZE = 1U << 16, HASH_4_SIZE = 1U << 20;
        internal const uint HASH_2_MASK = HASH_2_SIZE - 1, HASH_3_MASK = HASH_3_SIZE - 1;
        internal const uint FIX_3_HASH_SIZE = HASH_2_SIZE, FIX_4_HASH_SIZE = HASH_2_SIZE + HASH_3_SIZE;

        private const uint EMPTY_HASH_VALUE = 0;
        private const uint MUST_NORMALIZE_POS = uint.MaxValue;

        internal byte[] buffer;
        internal uint size;
        internal uint keep_size_before;
        internal uint keep_size_after;
        internal uint offset;
        internal uint read_pos;
        internal uint read_ahead;
        internal uint read_limit;
        internal uint write_pos;
        internal uint pending;
        internal LzmaMatchFinder finder;
        internal uint[] hash;
        internal uint[] son;
        internal uint cyclic_pos;
        internal uint cyclic_size;
        internal uint hash_mask;
        internal uint depth;
        internal uint nice_len;
        internal uint match_len_max;
        internal LzmaAction action;
        internal uint hash_count;
        internal uint sons_count;

        private static readonly uint[] hash_table = LzmaCheckState.Crc32Table;

        /// <summary>mf_avail</summary>
        internal uint Avail => write_pos - read_pos;

        /// <summary>mf_unencoded</summary>
        internal uint Unencoded => write_pos - read_pos + read_ahead;

        /// <summary>mf_position</summary>
        internal uint Position => read_pos - read_ahead;

        /// <summary>mf_get_hash_bytes</summary>
        internal static uint GetHashBytes(LzmaMatchFinder match_finder) => (uint)match_finder & 0x0F;

        /// <summary>mf->find</summary>
        private uint FindRaw(LzmaMatch[] matches)
        {
            switch (finder)
            {
                case LzmaMatchFinder.HC3: return Hc3Find(matches);
                case LzmaMatchFinder.HC4: return Hc4Find(matches);
                case LzmaMatchFinder.BT2: return Bt2Find(matches);
                case LzmaMatchFinder.BT3: return Bt3Find(matches);
                default: return Bt4Find(matches);
            }
        }

        /// <summary>mf->skip</summary>
        internal void SkipRaw(uint amount)
        {
            switch (finder)
            {
                case LzmaMatchFinder.HC3: Hc3Skip(amount); break;
                case LzmaMatchFinder.HC4: Hc4Skip(amount); break;
                case LzmaMatchFinder.BT2: Bt2Skip(amount); break;
                case LzmaMatchFinder.BT3: Bt3Skip(amount); break;
                default: Bt4Skip(amount); break;
            }
        }

        /// <summary>mf_skip</summary>
        internal void Skip(uint amount)
        {
            if (amount != 0)
            {
                SkipRaw(amount);
                read_ahead += amount;
            }
        }

        /// <summary>mf_read: copies uncompressed data (LZMA2's uncompressed chunks).</summary>
        internal void Read(byte[] output, ref int out_pos, int out_size, ref uint left)
        {
            int out_avail = out_size - out_pos;
            int copy_size = (int)Math.Min((uint)out_avail, left);
            Buffer.BlockCopy(buffer, (int)(read_pos - left), output, out_pos, copy_size);
            out_pos += copy_size;
            left -= (uint)copy_size;
        }

        /// <summary>lzma_memcmplen: the length of the common prefix, from len up to limit.</summary>
        internal static uint MemCmpLen(byte[] buf, int a, int b, uint len, uint limit)
        {
            if (len >= limit)
                return limit;
            int n = (int)(limit - len);
            int common = buf.AsSpan(a + (int)len, n).CommonPrefixLength(buf.AsSpan(b + (int)len, n));
            return len + (uint)common;
        }

        /// <summary>lzma_mf_find</summary>
        internal uint Find(out uint count_ptr, LzmaMatch[] matches)
        {
            uint count = FindRaw(matches);
            uint len_best = 0;

            if (count > 0)
            {
                // The last used element in the array contains the longest match.
                len_best = matches[count - 1].len;

                // If a match of maximum search length was found, try to extend the match to maximum possible length.
                if (len_best == nice_len)
                {
                    uint limit = Avail + 1;
                    if (limit > match_len_max)
                        limit = match_len_max;

                    int p1 = (int)read_pos - 1;
                    int p2 = p1 - (int)matches[count - 1].dist - 1;

                    len_best = MemCmpLen(buffer, p1, p2, len_best, limit);
                }
            }

            count_ptr = count;

            // Finally update the read position to indicate that match finder was run for this dictionary offset.
            ++read_ahead;

            return len_best;
        }

        /// <summary>normalize</summary>
        private void Normalize()
        {
            uint subvalue = MUST_NORMALIZE_POS - cyclic_size;

            for (uint i = 0; i < hash_count; ++i)
            {
                if (hash[i] <= subvalue)
                    hash[i] = EMPTY_HASH_VALUE;
                else
                    hash[i] -= subvalue;
            }

            for (uint i = 0; i < sons_count; ++i)
            {
                if (son[i] <= subvalue)
                    son[i] = EMPTY_HASH_VALUE;
                else
                    son[i] -= subvalue;
            }

            offset -= subvalue;
        }

        /// <summary>move_pos</summary>
        private void MovePos()
        {
            if (++cyclic_pos == cyclic_size)
                cyclic_pos = 0;
            ++read_pos;
            if (read_pos + offset == uint.MaxValue)
                Normalize();
        }

        /// <summary>move_pending</summary>
        private void MovePending()
        {
            ++read_pos;
            ++pending;
        }

        /// <summary>
        /// The "header" macro: the length limit and whether the finder runs at all here. False
        /// (after move_pending) when there is too little data to hash.
        /// </summary>
        private bool Header(bool is_bt, uint len_min, out uint len_limit)
        {
            len_limit = Avail;
            if (nice_len <= len_limit)
            {
                len_limit = nice_len;
            }
            else if (len_limit < len_min || (is_bt && action == LzmaAction.SYNC_FLUSH))
            {
                MovePending();
                return false;
            }
            return true;
        }

        /// <summary>hc_find_func</summary>
        private static int HcFindFunc(uint len_limit, uint pos, byte[] buf, int cur, uint cur_match, uint depth,
            uint[] son, uint cyclic_pos, uint cyclic_size, LzmaMatch[] matches, int m, uint len_best)
        {
            son[cyclic_pos] = cur_match;

            while (true)
            {
                uint delta = pos - cur_match;
                if (depth-- == 0 || delta >= cyclic_size)
                    return m;

                int pb = cur - (int)delta;
                cur_match = son[cyclic_pos - delta + (delta > cyclic_pos ? cyclic_size : 0)];

                if (buf[pb + len_best] == buf[cur + len_best] && buf[pb] == buf[cur])
                {
                    uint len = MemCmpLen(buf, pb, cur, 1, len_limit);

                    if (len_best < len)
                    {
                        len_best = len;
                        matches[m].len = len;
                        matches[m].dist = delta - 1;
                        ++m;

                        if (len == len_limit)
                            return m;
                    }
                }
            }
        }

        // hc_skip
        private void HcSkip(uint cur_match)
        {
            son[cyclic_pos] = cur_match;
            MovePos();
        }

        /// <summary>lzma_mf_hc3_find</summary>
        private uint Hc3Find(LzmaMatch[] matches)
        {
            if (!Header(false, 3, out uint len_limit))
                return 0;
            int cur = (int)read_pos;
            uint pos = read_pos + offset;
            uint matches_count = 0;

            // hash_3_calc
            uint temp = hash_table[buffer[cur]] ^ buffer[cur + 1];
            uint hash_2_value = temp & HASH_2_MASK;
            uint hash_value = (temp ^ ((uint)buffer[cur + 2] << 8)) & hash_mask;

            uint delta2 = pos - hash[hash_2_value];
            uint cur_match = hash[FIX_3_HASH_SIZE + hash_value];

            hash[hash_2_value] = pos;
            hash[FIX_3_HASH_SIZE + hash_value] = pos;

            uint len_best = 2;

            if (delta2 < cyclic_size && buffer[cur - (int)delta2] == buffer[cur])
            {
                len_best = MemCmpLen(buffer, cur - (int)delta2, cur, len_best, len_limit);

                matches[0].len = len_best;
                matches[0].dist = delta2 - 1;
                matches_count = 1;

                if (len_best == len_limit)
                {
                    HcSkip(cur_match);
                    return 1;
                }
            }

            // hc_find
            matches_count = (uint)HcFindFunc(len_limit, pos, buffer, cur, cur_match, depth, son,
                cyclic_pos, cyclic_size, matches, (int)matches_count, len_best);
            MovePos();
            return matches_count;
        }

        /// <summary>lzma_mf_hc3_skip</summary>
        private void Hc3Skip(uint amount)
        {
            do
            {
                if (Avail < 3)
                {
                    MovePending();
                    continue;
                }

                int cur = (int)read_pos;
                uint pos = read_pos + offset;

                uint temp = hash_table[buffer[cur]] ^ buffer[cur + 1];
                uint hash_2_value = temp & HASH_2_MASK;
                uint hash_value = (temp ^ ((uint)buffer[cur + 2] << 8)) & hash_mask;

                uint cur_match = hash[FIX_3_HASH_SIZE + hash_value];

                hash[hash_2_value] = pos;
                hash[FIX_3_HASH_SIZE + hash_value] = pos;

                HcSkip(cur_match);
            } while (--amount != 0);
        }

        /// <summary>lzma_mf_hc4_find</summary>
        private uint Hc4Find(LzmaMatch[] matches)
        {
            if (!Header(false, 4, out uint len_limit))
                return 0;
            int cur = (int)read_pos;
            uint pos = read_pos + offset;
            uint matches_count = 0;

            // hash_4_calc
            uint temp = hash_table[buffer[cur]] ^ buffer[cur + 1];
            uint hash_2_value = temp & HASH_2_MASK;
            uint hash_3_value = (temp ^ ((uint)buffer[cur + 2] << 8)) & HASH_3_MASK;
            uint hash_value = (temp ^ ((uint)buffer[cur + 2] << 8) ^ (hash_table[buffer[cur + 3]] << 5)) & hash_mask;

            uint delta2 = pos - hash[hash_2_value];
            uint delta3 = pos - hash[FIX_3_HASH_SIZE + hash_3_value];
            uint cur_match = hash[FIX_4_HASH_SIZE + hash_value];

            hash[hash_2_value] = pos;
            hash[FIX_3_HASH_SIZE + hash_3_value] = pos;
            hash[FIX_4_HASH_SIZE + hash_value] = pos;

            uint len_best = 1;

            if (delta2 < cyclic_size && buffer[cur - (int)delta2] == buffer[cur])
            {
                len_best = 2;
                matches[0].len = 2;
                matches[0].dist = delta2 - 1;
                matches_count = 1;
            }

            if (delta2 != delta3 && delta3 < cyclic_size && buffer[cur - (int)delta3] == buffer[cur])
            {
                len_best = 3;
                matches[matches_count++].dist = delta3 - 1;
                delta2 = delta3;
            }

            if (matches_count != 0)
            {
                len_best = MemCmpLen(buffer, cur - (int)delta2, cur, len_best, len_limit);

                matches[matches_count - 1].len = len_best;

                if (len_best == len_limit)
                {
                    HcSkip(cur_match);
                    return matches_count;
                }
            }

            if (len_best < 3)
                len_best = 3;

            matches_count = (uint)HcFindFunc(len_limit, pos, buffer, cur, cur_match, depth, son,
                cyclic_pos, cyclic_size, matches, (int)matches_count, len_best);
            MovePos();
            return matches_count;
        }

        /// <summary>lzma_mf_hc4_skip</summary>
        private void Hc4Skip(uint amount)
        {
            do
            {
                if (Avail < 4)
                {
                    MovePending();
                    continue;
                }

                int cur = (int)read_pos;
                uint pos = read_pos + offset;

                uint temp = hash_table[buffer[cur]] ^ buffer[cur + 1];
                uint hash_2_value = temp & HASH_2_MASK;
                uint hash_3_value = (temp ^ ((uint)buffer[cur + 2] << 8)) & HASH_3_MASK;
                uint hash_value = (temp ^ ((uint)buffer[cur + 2] << 8) ^ (hash_table[buffer[cur + 3]] << 5)) & hash_mask;

                uint cur_match = hash[FIX_4_HASH_SIZE + hash_value];

                hash[hash_2_value] = pos;
                hash[FIX_3_HASH_SIZE + hash_3_value] = pos;
                hash[FIX_4_HASH_SIZE + hash_value] = pos;

                HcSkip(cur_match);
            } while (--amount != 0);
        }

        /// <summary>bt_find_func</summary>
        private static int BtFindFunc(uint len_limit, uint pos, byte[] buf, int cur, uint cur_match, uint depth,
            uint[] son, uint cyclic_pos, uint cyclic_size, LzmaMatch[] matches, int m, uint len_best)
        {
            uint ptr0 = (cyclic_pos << 1) + 1;
            uint ptr1 = cyclic_pos << 1;

            uint len0 = 0;
            uint len1 = 0;

            while (true)
            {
                uint delta = pos - cur_match;
                if (depth-- == 0 || delta >= cyclic_size)
                {
                    son[ptr0] = EMPTY_HASH_VALUE;
                    son[ptr1] = EMPTY_HASH_VALUE;
                    return m;
                }

                uint pair = (cyclic_pos - delta + (delta > cyclic_pos ? cyclic_size : 0)) << 1;

                int pb = cur - (int)delta;
                uint len = Math.Min(len0, len1);

                if (buf[pb + len] == buf[cur + len])
                {
                    len = MemCmpLen(buf, pb, cur, len + 1, len_limit);

                    if (len_best < len)
                    {
                        len_best = len;
                        matches[m].len = len;
                        matches[m].dist = delta - 1;
                        ++m;

                        if (len == len_limit)
                        {
                            son[ptr1] = son[pair];
                            son[ptr0] = son[pair + 1];
                            return m;
                        }
                    }
                }

                if (buf[pb + len] < buf[cur + len])
                {
                    son[ptr1] = cur_match;
                    ptr1 = pair + 1;
                    cur_match = son[ptr1];
                    len1 = len;
                }
                else
                {
                    son[ptr0] = cur_match;
                    ptr0 = pair;
                    cur_match = son[ptr0];
                    len0 = len;
                }
            }
        }

        /// <summary>bt_skip_func</summary>
        private static void BtSkipFunc(uint len_limit, uint pos, byte[] buf, int cur, uint cur_match, uint depth,
            uint[] son, uint cyclic_pos, uint cyclic_size)
        {
            uint ptr0 = (cyclic_pos << 1) + 1;
            uint ptr1 = cyclic_pos << 1;

            uint len0 = 0;
            uint len1 = 0;

            while (true)
            {
                uint delta = pos - cur_match;
                if (depth-- == 0 || delta >= cyclic_size)
                {
                    son[ptr0] = EMPTY_HASH_VALUE;
                    son[ptr1] = EMPTY_HASH_VALUE;
                    return;
                }

                uint pair = (cyclic_pos - delta + (delta > cyclic_pos ? cyclic_size : 0)) << 1;
                int pb = cur - (int)delta;
                uint len = Math.Min(len0, len1);

                if (buf[pb + len] == buf[cur + len])
                {
                    len = MemCmpLen(buf, pb, cur, len + 1, len_limit);

                    if (len == len_limit)
                    {
                        son[ptr1] = son[pair];
                        son[ptr0] = son[pair + 1];
                        return;
                    }
                }

                if (buf[pb + len] < buf[cur + len])
                {
                    son[ptr1] = cur_match;
                    ptr1 = pair + 1;
                    cur_match = son[ptr1];
                    len1 = len;
                }
                else
                {
                    son[ptr0] = cur_match;
                    ptr0 = pair;
                    cur_match = son[ptr0];
                    len0 = len;
                }
            }
        }

        // bt_find
        private uint BtFind(uint len_limit, uint pos, int cur, uint cur_match, LzmaMatch[] matches, uint matches_count, uint len_best)
        {
            matches_count = (uint)BtFindFunc(len_limit, pos, buffer, cur, cur_match, depth, son,
                cyclic_pos, cyclic_size, matches, (int)matches_count, len_best);
            MovePos();
            return matches_count;
        }

        // bt_skip
        private void BtSkip(uint len_limit, uint pos, int cur, uint cur_match)
        {
            BtSkipFunc(len_limit, pos, buffer, cur, cur_match, depth, son, cyclic_pos, cyclic_size);
            MovePos();
        }

        /// <summary>lzma_mf_bt2_find</summary>
        private uint Bt2Find(LzmaMatch[] matches)
        {
            if (!Header(true, 2, out uint len_limit))
                return 0;
            int cur = (int)read_pos;
            uint pos = read_pos + offset;

            // hash_2_calc
            uint hash_value = buffer[cur] | ((uint)buffer[cur + 1] << 8);
            uint cur_match = hash[hash_value];
            hash[hash_value] = pos;

            return BtFind(len_limit, pos, cur, cur_match, matches, 0, 1);
        }

        /// <summary>lzma_mf_bt2_skip</summary>
        private void Bt2Skip(uint amount)
        {
            do
            {
                if (!Header(true, 2, out uint len_limit))
                    continue;
                int cur = (int)read_pos;
                uint pos = read_pos + offset;

                uint hash_value = buffer[cur] | ((uint)buffer[cur + 1] << 8);
                uint cur_match = hash[hash_value];
                hash[hash_value] = pos;

                BtSkip(len_limit, pos, cur, cur_match);
            } while (--amount != 0);
        }

        /// <summary>lzma_mf_bt3_find</summary>
        private uint Bt3Find(LzmaMatch[] matches)
        {
            if (!Header(true, 3, out uint len_limit))
                return 0;
            int cur = (int)read_pos;
            uint pos = read_pos + offset;
            uint matches_count = 0;

            uint temp = hash_table[buffer[cur]] ^ buffer[cur + 1];
            uint hash_2_value = temp & HASH_2_MASK;
            uint hash_value = (temp ^ ((uint)buffer[cur + 2] << 8)) & hash_mask;

            uint delta2 = pos - hash[hash_2_value];
            uint cur_match = hash[FIX_3_HASH_SIZE + hash_value];

            hash[hash_2_value] = pos;
            hash[FIX_3_HASH_SIZE + hash_value] = pos;

            uint len_best = 2;

            if (delta2 < cyclic_size && buffer[cur - (int)delta2] == buffer[cur])
            {
                len_best = MemCmpLen(buffer, cur, cur - (int)delta2, len_best, len_limit);

                matches[0].len = len_best;
                matches[0].dist = delta2 - 1;
                matches_count = 1;

                if (len_best == len_limit)
                {
                    BtSkip(len_limit, pos, cur, cur_match);
                    return 1;
                }
            }

            return BtFind(len_limit, pos, cur, cur_match, matches, matches_count, len_best);
        }

        /// <summary>lzma_mf_bt3_skip</summary>
        private void Bt3Skip(uint amount)
        {
            do
            {
                if (!Header(true, 3, out uint len_limit))
                    continue;
                int cur = (int)read_pos;
                uint pos = read_pos + offset;

                uint temp = hash_table[buffer[cur]] ^ buffer[cur + 1];
                uint hash_2_value = temp & HASH_2_MASK;
                uint hash_value = (temp ^ ((uint)buffer[cur + 2] << 8)) & hash_mask;

                uint cur_match = hash[FIX_3_HASH_SIZE + hash_value];

                hash[hash_2_value] = pos;
                hash[FIX_3_HASH_SIZE + hash_value] = pos;

                BtSkip(len_limit, pos, cur, cur_match);
            } while (--amount != 0);
        }

        /// <summary>lzma_mf_bt4_find</summary>
        private uint Bt4Find(LzmaMatch[] matches)
        {
            if (!Header(true, 4, out uint len_limit))
                return 0;
            int cur = (int)read_pos;
            uint pos = read_pos + offset;
            uint matches_count = 0;

            uint temp = hash_table[buffer[cur]] ^ buffer[cur + 1];
            uint hash_2_value = temp & HASH_2_MASK;
            uint hash_3_value = (temp ^ ((uint)buffer[cur + 2] << 8)) & HASH_3_MASK;
            uint hash_value = (temp ^ ((uint)buffer[cur + 2] << 8) ^ (hash_table[buffer[cur + 3]] << 5)) & hash_mask;

            uint delta2 = pos - hash[hash_2_value];
            uint delta3 = pos - hash[FIX_3_HASH_SIZE + hash_3_value];
            uint cur_match = hash[FIX_4_HASH_SIZE + hash_value];

            hash[hash_2_value] = pos;
            hash[FIX_3_HASH_SIZE + hash_3_value] = pos;
            hash[FIX_4_HASH_SIZE + hash_value] = pos;

            uint len_best = 1;

            if (delta2 < cyclic_size && buffer[cur - (int)delta2] == buffer[cur])
            {
                len_best = 2;
                matches[0].len = 2;
                matches[0].dist = delta2 - 1;
                matches_count = 1;
            }

            if (delta2 != delta3 && delta3 < cyclic_size && buffer[cur - (int)delta3] == buffer[cur])
            {
                len_best = 3;
                matches[matches_count++].dist = delta3 - 1;
                delta2 = delta3;
            }

            if (matches_count != 0)
            {
                len_best = MemCmpLen(buffer, cur, cur - (int)delta2, len_best, len_limit);

                matches[matches_count - 1].len = len_best;

                if (len_best == len_limit)
                {
                    BtSkip(len_limit, pos, cur, cur_match);
                    return matches_count;
                }
            }

            if (len_best < 3)
                len_best = 3;

            return BtFind(len_limit, pos, cur, cur_match, matches, matches_count, len_best);
        }

        /// <summary>lzma_mf_bt4_skip</summary>
        private void Bt4Skip(uint amount)
        {
            do
            {
                if (!Header(true, 4, out uint len_limit))
                    continue;
                int cur = (int)read_pos;
                uint pos = read_pos + offset;

                uint temp = hash_table[buffer[cur]] ^ buffer[cur + 1];
                uint hash_2_value = temp & HASH_2_MASK;
                uint hash_3_value = (temp ^ ((uint)buffer[cur + 2] << 8)) & HASH_3_MASK;
                uint hash_value = (temp ^ ((uint)buffer[cur + 2] << 8) ^ (hash_table[buffer[cur + 3]] << 5)) & hash_mask;

                uint cur_match = hash[FIX_4_HASH_SIZE + hash_value];

                hash[hash_2_value] = pos;
                hash[FIX_3_HASH_SIZE + hash_3_value] = pos;
                hash[FIX_4_HASH_SIZE + hash_value] = pos;

                BtSkip(len_limit, pos, cur, cur_match);
            } while (--amount != 0);
        }
    }

    /// <summary>The lzma_coder of lz/lz_encoder.c: the match finder's window filled from the input (or the next filter).</summary>
    internal sealed class LzEncoderCoder : LzmaNextCoder
    {
        internal ILzEncoder lz;
        internal readonly LzmaMF mf = new();
        internal LzmaNextCoder next;

        /// <summary>lz_encoder_end / lz_decoder_end: the buffers go back to the pool.</summary>
        internal override void End()
        {
            next?.End();
            next = null;
            ReleaseBuffers();
        }

        // lzma_free(coder->mf.son), (coder->mf.hash), (coder->mf.buffer)
        private void ReleaseBuffers()
        {
            CodecBufferPool.Return(mf.son);
            CodecBufferPool.Return(mf.hash);
            CodecBufferPool.Return(mf.buffer);
            mf.son = null;
            mf.hash = null;
            mf.buffer = null;
            mf.size = 0;
            mf.hash_count = 0;
            mf.sons_count = 0;
        }

        /// <summary>move_window</summary>
        private static void MoveWindow(LzmaMF mf)
        {
            // Align the move to a multiple of 16 bytes.
            uint move_offset = (mf.read_pos - mf.keep_size_before) & ~15u;
            uint move_size = mf.write_pos - move_offset;
            Buffer.BlockCopy(mf.buffer, (int)move_offset, mf.buffer, 0, (int)move_size);
            mf.offset += move_offset;
            mf.read_pos -= move_offset;
            mf.read_limit -= move_offset;
            mf.write_pos -= move_offset;
        }

        /// <summary>fill_window</summary>
        private LzmaRet FillWindow(byte[] input, ref int in_pos, int in_size, LzmaAction action)
        {
            // Move the sliding window if needed.
            if (mf.read_pos >= mf.size - mf.keep_size_after)
                MoveWindow(mf);

            int write_pos = (int)mf.write_pos;
            LzmaRet ret;
            if (next == null)
            {
                LzmaCore.BufCpy(input, ref in_pos, in_size, mf.buffer, ref write_pos, (int)mf.size);
                ret = action != LzmaAction.RUN && in_pos == in_size ? LzmaRet.STREAM_END : LzmaRet.OK;
            }
            else
            {
                ret = next.Code(input, ref in_pos, in_size, mf.buffer, ref write_pos, (int)mf.size, action);
            }

            mf.write_pos = (uint)write_pos;

            // Silence errors about uninitialized memory in memcmplen.
            Array.Clear(mf.buffer, write_pos, LzmaMF.MEMCMPLEN_EXTRA);

            if (ret == LzmaRet.STREAM_END)
            {
                ret = LzmaRet.OK;
                mf.action = action;
                mf.read_limit = mf.write_pos;
            }
            else if (mf.write_pos > mf.keep_size_after)
            {
                mf.read_limit = mf.write_pos - mf.keep_size_after;
            }

            // Restart the match finder after finished LZMA_SYNC_FLUSH.
            if (mf.pending > 0 && mf.read_pos < mf.read_limit)
            {
                uint pending = mf.pending;
                mf.pending = 0;
                mf.read_pos -= pending;
                mf.SkipRaw(pending);
            }

            return ret;
        }

        /// <summary>lz_encode</summary>
        internal override LzmaRet Code(byte[] input, ref int in_pos, int in_size,
            byte[] output, ref int out_pos, int out_size, LzmaAction action)
        {
            while (out_pos < out_size && (in_pos < in_size || action != LzmaAction.RUN))
            {
                // Read more data to coder->mf.buffer if needed.
                if (mf.action == LzmaAction.RUN && mf.read_pos >= mf.read_limit)
                {
                    var r = FillWindow(input, ref in_pos, in_size, action);
                    if (r != LzmaRet.OK)
                        return r;
                }

                // Encode
                var ret = lz.Code(mf, output, ref out_pos, out_size);
                if (ret != LzmaRet.OK)
                {
                    // Setting this to LZMA_RUN for cases when we are flushing.
                    mf.action = LzmaAction.RUN;
                    return ret;
                }
            }

            return LzmaRet.OK;
        }

        /// <summary>lz_encoder_prepare: true on invalid options.</summary>
        internal static bool Prepare(LzmaMF mf, in LzEncoderOptions lz_options)
        {
            // Verify that the dictionary size and nice_len are valid.
            if (!IsEncDictSizeValid(lz_options.dict_size) || lz_options.nice_len > lz_options.match_len_max)
                return true;

            mf.keep_size_before = lz_options.before_size + lz_options.dict_size;
            mf.keep_size_after = lz_options.after_size + lz_options.match_len_max;

            // Memory to reserve for the moving window.
            uint reserve = lz_options.dict_size / 2;
            if (reserve > (1U << 30))
                reserve /= 2;

            reserve += (lz_options.before_size + lz_options.match_len_max + lz_options.after_size) / 2 + (1U << 19);

            uint old_size = mf.size;
            mf.size = mf.keep_size_before + reserve + mf.keep_size_after;

            if (mf.buffer != null && old_size != mf.size)
                { CodecBufferPool.Return(mf.buffer); mf.buffer = null; }

            mf.match_len_max = lz_options.match_len_max;
            mf.nice_len = lz_options.nice_len;

            mf.cyclic_size = lz_options.dict_size + 1;

            switch (lz_options.match_finder)
            {
                case LzmaMatchFinder.HC3:
                case LzmaMatchFinder.HC4:
                case LzmaMatchFinder.BT2:
                case LzmaMatchFinder.BT3:
                case LzmaMatchFinder.BT4:
                    mf.finder = lz_options.match_finder;
                    break;
                default:
                    return true;
            }

            // Calculate the sizes of mf->hash and mf->son.
            uint hash_bytes = LzmaMF.GetHashBytes(lz_options.match_finder);
            bool is_bt = ((uint)lz_options.match_finder & 0x10) != 0;
            uint hs;

            if (hash_bytes == 2)
            {
                hs = 0xFFFF;
            }
            else
            {
                hs = lz_options.dict_size - 1;
                hs |= hs >> 1;
                hs |= hs >> 2;
                hs |= hs >> 4;
                hs |= hs >> 8;
                hs >>= 1;
                hs |= 0xFFFF;

                if (hs > (1U << 24))
                {
                    if (hash_bytes == 3)
                        hs = (1U << 24) - 1;
                    else
                        hs >>= 1;
                }
            }

            mf.hash_mask = hs;

            ++hs;
            if (hash_bytes > 2)
                hs += LzmaMF.HASH_2_SIZE;
            if (hash_bytes > 3)
                hs += LzmaMF.HASH_3_SIZE;

            uint old_hash_count = mf.hash_count;
            uint old_sons_count = mf.sons_count;
            mf.hash_count = hs;
            mf.sons_count = mf.cyclic_size;
            if (is_bt)
                mf.sons_count *= 2;

            if (old_hash_count != mf.hash_count || old_sons_count != mf.sons_count)
            {
                CodecBufferPool.Return(mf.hash); mf.hash = null;
                CodecBufferPool.Return(mf.son); mf.son = null;
            }

            // Maximum number of match finder cycles
            mf.depth = lz_options.depth;
            if (mf.depth == 0)
            {
                if (is_bt)
                    mf.depth = 16 + mf.nice_len / 2;
                else
                    mf.depth = 4 + mf.nice_len / 4;
            }

            return false;
        }

        /// <summary>IS_ENC_DICT_SIZE_VALID</summary>
        internal static bool IsEncDictSizeValid(uint size) =>
            size >= LzmaConst.DICT_SIZE_MIN && size <= (1U << 30) + (1U << 29);

        /// <summary>lz_encoder_init: true on allocation failure.</summary>
        private static bool Init(LzmaMF mf, in LzEncoderOptions lz_options)
        {
            // Allocate the history buffer.
            if (mf.buffer == null)
            {
                try
                {
                    mf.buffer = CodecBufferPool.RentBytes((int)mf.size + LzmaMF.MEMCMPLEN_EXTRA);
                }
                catch (OutOfMemoryException)
                {
                    return true;
                }
                Array.Clear(mf.buffer, (int)mf.size, LzmaMF.MEMCMPLEN_EXTRA);
            }

            // Use cyclic_size as initial mf->offset (it is zero when hash and son are empty).
            mf.offset = mf.cyclic_size;
            mf.read_pos = 0;
            mf.read_ahead = 0;
            mf.read_limit = 0;
            mf.write_pos = 0;
            mf.pending = 0;

            if (mf.hash_count > int.MaxValue || mf.sons_count > int.MaxValue)
                return true;

            try
            {
                if (mf.hash == null)
                {
                    mf.hash = CodecBufferPool.RentUInts((int)mf.hash_count);
                    Array.Clear(mf.hash);
                    mf.son = CodecBufferPool.RentUInts((int)mf.sons_count);
                }
                else
                {
                    Array.Clear(mf.hash);
                }
            }
            catch (OutOfMemoryException)
            {
                mf.hash = null;
                mf.son = null;
                return true;
            }

            mf.cyclic_pos = 0;

            // Handle preset dictionary.
            if (lz_options.preset_dict != null && lz_options.preset_dict_size > 0)
            {
                mf.write_pos = Math.Min(lz_options.preset_dict_size, mf.size);
                Buffer.BlockCopy(lz_options.preset_dict, (int)(lz_options.preset_dict_size - mf.write_pos),
                    mf.buffer, 0, (int)mf.write_pos);
                mf.action = LzmaAction.SYNC_FLUSH;
                mf.SkipRaw(mf.write_pos);
            }

            mf.action = LzmaAction.RUN;

            return false;
        }

        /// <summary>lzma_lz_encoder_memusage</summary>
        internal static ulong MemUsage(in LzEncoderOptions lz_options)
        {
            var mf = new LzmaMF();
            if (Prepare(mf, lz_options))
                return ulong.MaxValue;
            // + sizeof(lzma_coder)
            return ((ulong)mf.hash_count + mf.sons_count) * 4 + mf.size + 256;
        }

        /// <summary>lz_encoder_update</summary>
        internal LzmaRet Update(LzmaFilter[] reversed_filters)
        {
            var ret = lz.OptionsUpdate(reversed_filters[0]);
            return ret;
        }

        /// <summary>lzma_lz_encoder_init</summary>
        internal static LzmaRet EncoderInit(ref LzmaNextCoder next, LzmaFilterInfo[] filters, int index,
            Func<LzEncoderCoder, ulong, object, (LzmaRet, LzEncoderOptions)> lz_init)
        {
            if (next is not LzEncoderCoder coder)
            {
                coder = new LzEncoderCoder();
                next = coder;
            }
            coder.InitKey = filters[index].init;

            var (ret, lz_options) = lz_init(coder, filters[index].id, filters[index].options);
            if (ret != LzmaRet.OK)
                return ret;

            // Setup the size information into coder->mf and deallocate old buffers if the size changed.
            if (Prepare(coder.mf, lz_options))
                return LzmaRet.OPTIONS_ERROR;

            // Allocate new buffers if needed, and do the rest of the initialization.
            if (Init(coder.mf, lz_options))
                return LzmaRet.MEM_ERROR;

            // Initialize the next filter in the chain, if any.
            return LzmaCore.NextFilterInit(ref coder.next, filters, index + 1);
        }

        /// <summary>lzma_mf_is_supported</summary>
        internal static bool MfIsSupported(LzmaMatchFinder mf) =>
            mf is LzmaMatchFinder.HC3 or LzmaMatchFinder.HC4 or LzmaMatchFinder.BT2 or LzmaMatchFinder.BT3 or LzmaMatchFinder.BT4;
    }
}
