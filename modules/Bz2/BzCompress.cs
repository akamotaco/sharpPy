using System;

namespace SharpPy.Modules.Bz2
{
    /// <summary>
    /// EState and the compression side of bzip2 1.0.8: bzlib.c (BZ2_bzCompressInit, BZ2_bzCompress
    /// and its helpers), blocksort.c (BZ2_blockSort), compress.c (BZ2_compressBlock) and huffman.c
    /// (BZ2_hbMakeCodeLengths, BZ2_hbAssignCodes). The C code aliases its arrays (the block lives in
    /// arr2, fallbackSort's eclass overwrites it, the quadrant follows it, the MTF values overwrite
    /// arr1, the output follows the block); here each has its own array, which gives the same bytes:
    /// every aliased area is read before the area that overlaps it is written.
    /// </summary>
    internal sealed class BzEState
    {
        // mode this stream is in, and whether inputting or outputting data
        private const int M_IDLE = 1, M_RUNNING = 2, M_FLUSHING = 3, M_FINISHING = 4;
        private const int S_OUTPUT = 1, S_INPUT = 2;

        private BzStream strm;
        private int mode, state;
        private uint avail_in_expect;

        // for doing the block sorting
        private uint[] ptr;          // arr1: the sorted order
        private byte[] block;        // arr2 as bytes: the block, then N_OVERSHOOT bytes
        private ushort[] quadrant;
        private uint[] ftab;
        private uint[] eclass;       // fallbackSort's arr2 as UInt32
        private int origPtr;
        private ushort[] mtfv;
        private byte[] zbits;

        private int workFactor;

        // run-length-encoding of the input
        private uint state_in_ch;
        private int state_in_len;

        // input and output limits and current posns
        private int nblock, nblockMAX, numZ, state_out_pos;

        // map of bytes used in block
        private int nInUse;
        private readonly bool[] inUse = new bool[256];
        private readonly byte[] unseqToSeq = new byte[256];

        // the buffer for bit stream creation
        private uint bsBuff;
        private int bsLive;

        // block and combined CRCs
        private uint blockCRC, combinedCRC;

        private int blockNo, blockSize100k;

        // stuff for coding the MTF values
        private int nMTF;
        private readonly int[] mtfFreq = new int[Bz.MAX_ALPHA_SIZE];
        private readonly byte[] selector = new byte[Bz.MAX_SELECTORS];
        private readonly byte[] selectorMtf = new byte[Bz.MAX_SELECTORS];
        private readonly byte[][] len = Jagged<byte>(Bz.N_GROUPS, Bz.MAX_ALPHA_SIZE);
        private readonly int[][] code = Jagged<int>(Bz.N_GROUPS, Bz.MAX_ALPHA_SIZE);
        private readonly int[][] rfreq = Jagged<int>(Bz.N_GROUPS, Bz.MAX_ALPHA_SIZE);
        // second dimension: only 3 needed
        private readonly uint[] len_pack = new uint[Bz.MAX_ALPHA_SIZE * 3];

        private static T[][] Jagged<T>(int n, int m)
        {
            var a = new T[n][];
            for (int i = 0; i < n; i++) a[i] = new T[m];
            return a;
        }

        // ---------------------------------------------------------------- bzlib.c

        /// <summary>prepare_new_block</summary>
        private void PrepareNewBlock()
        {
            nblock = 0;
            numZ = 0;
            state_out_pos = 0;
            blockCRC = 0xffffffff;
            Array.Clear(inUse);
            blockNo++;
        }

        /// <summary>init_RL</summary>
        private void InitRL()
        {
            state_in_ch = 256;
            state_in_len = 0;
        }

        /// <summary>isempty_RL</summary>
        private bool IsEmptyRL() => !(state_in_ch < 256 && state_in_len > 0);

        /// <summary>BZ2_bzCompressInit (verbosity is not kept: nothing is printed).</summary>
        internal static int CompressInit(BzStream strm, int blockSize100k, int verbosity, int workFactor)
        {
            if (blockSize100k < 1 || blockSize100k > 9 || workFactor < 0 || workFactor > 250)
                return Bz.PARAM_ERROR;
            if (workFactor == 0) workFactor = 30;
            var s = new BzEState();
            s.strm = strm;
            int n = 100000 * blockSize100k;
            try
            {
                s.ptr = CodecBufferPool.Rent<uint>(n);
                s.block = CodecBufferPool.Rent<byte>(n + Bz.N_OVERSHOOT);
                s.ftab = new uint[65537];
            }
            catch (OutOfMemoryException)
            {
                return Bz.MEM_ERROR;
            }
            s.blockNo = 0;
            s.state = S_INPUT;
            s.mode = M_RUNNING;
            s.combinedCRC = 0;
            s.blockSize100k = blockSize100k;
            s.nblockMAX = 100000 * blockSize100k - 19;
            s.workFactor = workFactor;
            strm.CState = s;
            strm.TotalIn = 0;
            strm.TotalOut = 0;
            s.InitRL();
            s.PrepareNewBlock();
            return Bz.OK;
        }

        /// <summary>add_pair_to_block</summary>
        private void AddPairToBlock()
        {
            byte ch = (byte)state_in_ch;
            for (int i = 0; i < state_in_len; i++)
                blockCRC = (blockCRC << 8) ^ Bz.Crc32Table[(blockCRC >> 24) ^ ch];
            inUse[state_in_ch] = true;
            switch (state_in_len)
            {
                case 1:
                    block[nblock++] = ch;
                    break;
                case 2:
                    block[nblock++] = ch;
                    block[nblock++] = ch;
                    break;
                case 3:
                    block[nblock++] = ch;
                    block[nblock++] = ch;
                    block[nblock++] = ch;
                    break;
                default:
                    inUse[state_in_len - 4] = true;
                    block[nblock++] = ch;
                    block[nblock++] = ch;
                    block[nblock++] = ch;
                    block[nblock++] = ch;
                    block[nblock++] = (byte)(state_in_len - 4);
                    break;
            }
        }

        /// <summary>flush_RL</summary>
        private void FlushRL()
        {
            if (state_in_ch < 256) AddPairToBlock();
            InitRL();
        }

        /// <summary>ADD_CHAR_TO_BLOCK</summary>
        private void AddCharToBlock(uint zchh)
        {
            // fast track the common case
            if (zchh != state_in_ch && state_in_len == 1)
            {
                byte ch = (byte)state_in_ch;
                blockCRC = (blockCRC << 8) ^ Bz.Crc32Table[(blockCRC >> 24) ^ ch];
                inUse[state_in_ch] = true;
                block[nblock++] = ch;
                state_in_ch = zchh;
            }
            // general, uncommon cases
            else if (zchh != state_in_ch || state_in_len == 255)
            {
                if (state_in_ch < 256)
                    AddPairToBlock();
                state_in_ch = zchh;
                state_in_len = 1;
            }
            else
            {
                state_in_len++;
            }
        }

        /// <summary>copy_input_until_stop</summary>
        private bool CopyInputUntilStop()
        {
            bool progress_in = false;
            if (mode == M_RUNNING)
            {
                // fast track the common case
                while (true)
                {
                    if (nblock >= nblockMAX) break;
                    if (strm.AvailIn == 0) break;
                    progress_in = true;
                    AddCharToBlock(strm.InBuf[strm.NextIn]);
                    strm.NextIn++;
                    strm.AvailIn--;
                    strm.TotalIn++;
                }
            }
            else
            {
                // general, uncommon case
                while (true)
                {
                    if (nblock >= nblockMAX) break;
                    if (strm.AvailIn == 0) break;
                    if (avail_in_expect == 0) break;
                    progress_in = true;
                    AddCharToBlock(strm.InBuf[strm.NextIn]);
                    strm.NextIn++;
                    strm.AvailIn--;
                    strm.TotalIn++;
                    avail_in_expect--;
                }
            }
            return progress_in;
        }

        /// <summary>copy_output_until_stop</summary>
        private bool CopyOutputUntilStop()
        {
            int n = (int)Math.Min(strm.AvailOut, (uint)(numZ - state_out_pos));
            if (numZ - state_out_pos <= 0 || strm.AvailOut == 0)
                return false;
            Buffer.BlockCopy(zbits, state_out_pos, strm.OutBuf, strm.NextOut, n);
            state_out_pos += n;
            strm.AvailOut -= (uint)n;
            strm.NextOut += n;
            strm.TotalOut += (ulong)n;
            return true;
        }

        /// <summary>handle_compress</summary>
        private bool HandleCompress()
        {
            bool progress_in = false;
            bool progress_out = false;
            while (true)
            {
                if (state == S_OUTPUT)
                {
                    progress_out |= CopyOutputUntilStop();
                    if (state_out_pos < numZ) break;
                    if (mode == M_FINISHING && avail_in_expect == 0 && IsEmptyRL()) break;
                    PrepareNewBlock();
                    state = S_INPUT;
                    if (mode == M_FLUSHING && avail_in_expect == 0 && IsEmptyRL()) break;
                }
                if (state == S_INPUT)
                {
                    progress_in |= CopyInputUntilStop();
                    if (mode != M_RUNNING && avail_in_expect == 0)
                    {
                        FlushRL();
                        CompressBlock(mode == M_FINISHING);
                        state = S_OUTPUT;
                    }
                    else if (nblock >= nblockMAX)
                    {
                        CompressBlock(false);
                        state = S_OUTPUT;
                    }
                    else if (strm.AvailIn == 0)
                    {
                        break;
                    }
                }
            }
            return progress_in || progress_out;
        }

        /// <summary>BZ2_bzCompress</summary>
        internal static int Compress(BzStream strm, int action)
        {
            var s = strm?.CState;
            if (s == null || s.strm != strm) return Bz.PARAM_ERROR;
            bool progress;
        preswitch:
            switch (s.mode)
            {
                case M_IDLE:
                    return Bz.SEQUENCE_ERROR;
                case M_RUNNING:
                    if (action == Bz.RUN)
                    {
                        progress = s.HandleCompress();
                        return progress ? Bz.RUN_OK : Bz.PARAM_ERROR;
                    }
                    else if (action == Bz.FLUSH)
                    {
                        s.avail_in_expect = strm.AvailIn;
                        s.mode = M_FLUSHING;
                        goto preswitch;
                    }
                    else if (action == Bz.FINISH)
                    {
                        s.avail_in_expect = strm.AvailIn;
                        s.mode = M_FINISHING;
                        goto preswitch;
                    }
                    return Bz.PARAM_ERROR;
                case M_FLUSHING:
                    if (action != Bz.FLUSH) return Bz.SEQUENCE_ERROR;
                    if (s.avail_in_expect != s.strm.AvailIn) return Bz.SEQUENCE_ERROR;
                    s.HandleCompress();
                    if (s.avail_in_expect > 0 || !s.IsEmptyRL() || s.state_out_pos < s.numZ) return Bz.FLUSH_OK;
                    s.mode = M_RUNNING;
                    return Bz.RUN_OK;
                case M_FINISHING:
                    if (action != Bz.FINISH) return Bz.SEQUENCE_ERROR;
                    if (s.avail_in_expect != s.strm.AvailIn) return Bz.SEQUENCE_ERROR;
                    progress = s.HandleCompress();
                    if (!progress) return Bz.SEQUENCE_ERROR;
                    if (s.avail_in_expect > 0 || !s.IsEmptyRL() || s.state_out_pos < s.numZ) return Bz.FINISH_OK;
                    s.mode = M_IDLE;
                    return Bz.STREAM_END;
            }
            return Bz.OK; // not reached
        }

        /// <summary>BZ2_bzCompressEnd</summary>
        internal static int CompressEnd(BzStream strm)
        {
            var s = strm?.CState;
            if (s == null || s.strm != strm) return Bz.PARAM_ERROR;
            // BZFREE(s->arr1), (s->arr2), (s->ftab): back to the pool (CodecBufferPool)
            CodecBufferPool.Return(s.ptr);
            CodecBufferPool.Return(s.block);
            CodecBufferPool.Return(s.quadrant);
            CodecBufferPool.Return(s.mtfv);
            CodecBufferPool.Return(s.zbits);
            CodecBufferPool.Return(s.eclass);
            s.ptr = null; s.block = null; s.quadrant = null; s.mtfv = null; s.zbits = null; s.eclass = null;
            strm.CState = null;
            return Bz.OK;
        }

        // ---------------------------------------------------------------- compress.c

        /// <summary>BZ2_bsInitWrite</summary>
        private void BsInitWrite()
        {
            bsLive = 0;
            bsBuff = 0;
        }

        /// <summary>bsFinishWrite</summary>
        private void BsFinishWrite()
        {
            while (bsLive > 0)
            {
                zbits[numZ++] = (byte)(bsBuff >> 24);
                bsBuff <<= 8;
                bsLive -= 8;
            }
        }

        /// <summary>bsW (with bsNEEDW)</summary>
        private void BsW(int n, uint v)
        {
            while (bsLive >= 8)
            {
                zbits[numZ++] = (byte)(bsBuff >> 24);
                bsBuff <<= 8;
                bsLive -= 8;
            }
            bsBuff |= v << (32 - bsLive - n);
            bsLive += n;
        }

        /// <summary>bsPutUInt32</summary>
        private void BsPutUInt32(uint u)
        {
            BsW(8, (u >> 24) & 0xff);
            BsW(8, (u >> 16) & 0xff);
            BsW(8, (u >> 8) & 0xff);
            BsW(8, u & 0xff);
        }

        /// <summary>bsPutUChar</summary>
        private void BsPutUChar(byte c) => BsW(8, c);

        /// <summary>makeMaps_e</summary>
        private void MakeMapsE()
        {
            nInUse = 0;
            for (int i = 0; i < 256; i++)
                if (inUse[i])
                {
                    unseqToSeq[i] = (byte)nInUse;
                    nInUse++;
                }
        }

        /// <summary>generateMTFValues</summary>
        private void GenerateMTFValues()
        {
            Span<byte> yy = stackalloc byte[256];
            int i, j, zPend, wr, EOB;
            var ptr = this.ptr;
            var block = this.block;
            var mtfv = this.mtfv;

            MakeMapsE();
            EOB = nInUse + 1;
            for (i = 0; i <= EOB; i++) mtfFreq[i] = 0;
            wr = 0;
            zPend = 0;
            for (i = 0; i < nInUse; i++) yy[i] = (byte)i;

            for (i = 0; i < nblock; i++)
            {
                byte ll_i;
                j = (int)ptr[i] - 1; if (j < 0) j += nblock;
                ll_i = unseqToSeq[block[j]];
                if (yy[0] == ll_i)
                {
                    zPend++;
                }
                else
                {
                    if (zPend > 0)
                    {
                        zPend--;
                        while (true)
                        {
                            if ((zPend & 1) != 0)
                            {
                                mtfv[wr++] = Bz.RUNB;
                                mtfFreq[Bz.RUNB]++;
                            }
                            else
                            {
                                mtfv[wr++] = Bz.RUNA;
                                mtfFreq[Bz.RUNA]++;
                            }
                            if (zPend < 2) break;
                            zPend = (zPend - 2) / 2;
                        }
                        zPend = 0;
                    }
                    {
                        byte rtmp = yy[1];
                        yy[1] = yy[0];
                        int ryy_j = 1;
                        byte rll_i = ll_i;
                        while (rll_i != rtmp)
                        {
                            ryy_j++;
                            byte rtmp2 = rtmp;
                            rtmp = yy[ryy_j];
                            yy[ryy_j] = rtmp2;
                        }
                        yy[0] = rtmp;
                        j = ryy_j;
                        mtfv[wr++] = (ushort)(j + 1);
                        mtfFreq[j + 1]++;
                    }
                }
            }

            if (zPend > 0)
            {
                zPend--;
                while (true)
                {
                    if ((zPend & 1) != 0)
                    {
                        mtfv[wr++] = Bz.RUNB;
                        mtfFreq[Bz.RUNB]++;
                    }
                    else
                    {
                        mtfv[wr++] = Bz.RUNA;
                        mtfFreq[Bz.RUNA]++;
                    }
                    if (zPend < 2) break;
                    zPend = (zPend - 2) / 2;
                }
            }

            mtfv[wr++] = (ushort)EOB;
            mtfFreq[EOB]++;
            nMTF = wr;
        }

        private const int LESSER_ICOST = 0, GREATER_ICOST = 15;

        /// <summary>sendMTFValues</summary>
        private void SendMTFValues()
        {
            int v, t, i, j, gs, ge, totc, bt, bc, iter;
            int nSelectors = 0, alphaSize, minLen, maxLen, selCtr;
            int nGroups;
            Span<ushort> cost = stackalloc ushort[Bz.N_GROUPS];
            Span<int> fave = stackalloc int[Bz.N_GROUPS];
            var mtfv = this.mtfv;

            alphaSize = nInUse + 2;
            for (t = 0; t < Bz.N_GROUPS; t++)
                for (v = 0; v < alphaSize; v++)
                    len[t][v] = GREATER_ICOST;

            // Decide how many coding tables to use
            if (nMTF <= 0) throw new InvalidOperationException("bzip2 internal error 3001");
            if (nMTF < 200) nGroups = 2;
            else if (nMTF < 600) nGroups = 3;
            else if (nMTF < 1200) nGroups = 4;
            else if (nMTF < 2400) nGroups = 5;
            else nGroups = 6;

            // Generate an initial set of coding tables
            {
                int nPart, remF, tFreq, aFreq;
                nPart = nGroups;
                remF = nMTF;
                gs = 0;
                while (nPart > 0)
                {
                    tFreq = remF / nPart;
                    ge = gs - 1;
                    aFreq = 0;
                    while (aFreq < tFreq && ge < alphaSize - 1)
                    {
                        ge++;
                        aFreq += mtfFreq[ge];
                    }
                    if (ge > gs && nPart != nGroups && nPart != 1 && ((nGroups - nPart) % 2 == 1))
                    {
                        aFreq -= mtfFreq[ge];
                        ge--;
                    }
                    for (v = 0; v < alphaSize; v++)
                        len[nPart - 1][v] = (byte)(v >= gs && v <= ge ? LESSER_ICOST : GREATER_ICOST);
                    nPart--;
                    gs = ge + 1;
                    remF -= aFreq;
                }
            }

            // Iterate up to BZ_N_ITERS times to improve the tables.
            for (iter = 0; iter < Bz.N_ITERS; iter++)
            {
                for (t = 0; t < nGroups; t++) fave[t] = 0;
                for (t = 0; t < nGroups; t++)
                    Array.Clear(rfreq[t], 0, alphaSize);

                // an auxiliary length table which fast-tracks the common case (nGroups == 6)
                if (nGroups == 6)
                {
                    for (v = 0; v < alphaSize; v++)
                    {
                        len_pack[v * 3 + 0] = ((uint)len[1][v] << 16) | len[0][v];
                        len_pack[v * 3 + 1] = ((uint)len[3][v] << 16) | len[2][v];
                        len_pack[v * 3 + 2] = ((uint)len[5][v] << 16) | len[4][v];
                    }
                }

                nSelectors = 0;
                totc = 0;
                gs = 0;
                while (true)
                {
                    // Set group start & end marks.
                    if (gs >= nMTF) break;
                    ge = gs + Bz.G_SIZE - 1;
                    if (ge >= nMTF) ge = nMTF - 1;

                    // the cost of this group as coded by each of the coding tables
                    for (t = 0; t < nGroups; t++) cost[t] = 0;

                    if (nGroups == 6 && 50 == ge - gs + 1)
                    {
                        // fast track the common case
                        uint cost01 = 0, cost23 = 0, cost45 = 0;
                        for (int nn = 0; nn < 50; nn++)
                        {
                            int icv = mtfv[gs + nn] * 3;
                            cost01 += len_pack[icv];
                            cost23 += len_pack[icv + 1];
                            cost45 += len_pack[icv + 2];
                        }
                        cost[0] = (ushort)(cost01 & 0xffff); cost[1] = (ushort)(cost01 >> 16);
                        cost[2] = (ushort)(cost23 & 0xffff); cost[3] = (ushort)(cost23 >> 16);
                        cost[4] = (ushort)(cost45 & 0xffff); cost[5] = (ushort)(cost45 >> 16);
                    }
                    else
                    {
                        // slow version which correctly handles all situations
                        for (i = gs; i <= ge; i++)
                        {
                            int icv = mtfv[i];
                            for (t = 0; t < nGroups; t++) cost[t] += len[t][icv];
                        }
                    }

                    // Find the coding table which is best for this group, and record its identity.
                    bc = 999999999; bt = -1;
                    for (t = 0; t < nGroups; t++)
                        if (cost[t] < bc) { bc = cost[t]; bt = t; }
                    totc += bc;
                    fave[bt]++;
                    selector[nSelectors] = (byte)bt;
                    nSelectors++;

                    // Increment the symbol frequencies for the selected table.
                    var rf = rfreq[bt];
                    for (i = gs; i <= ge; i++)
                        rf[mtfv[i]]++;

                    gs = ge + 1;
                }

                // Recompute the tables based on the accumulated frequencies.
                // maxLen was changed from 20 to 17 in bzip2-1.0.3.
                for (t = 0; t < nGroups; t++)
                    HbMakeCodeLengths(len[t], rfreq[t], alphaSize, 17);
            }

            if (!(nGroups < 8)) throw new InvalidOperationException("bzip2 internal error 3002");
            if (!(nSelectors < 32768 && nSelectors <= Bz.MAX_SELECTORS)) throw new InvalidOperationException("bzip2 internal error 3003");

            // Compute MTF values for the selectors.
            {
                Span<byte> pos = stackalloc byte[Bz.N_GROUPS];
                byte ll_i, tmp2, tmp;
                for (i = 0; i < nGroups; i++) pos[i] = (byte)i;
                for (i = 0; i < nSelectors; i++)
                {
                    ll_i = selector[i];
                    j = 0;
                    tmp = pos[j];
                    while (ll_i != tmp)
                    {
                        j++;
                        tmp2 = tmp;
                        tmp = pos[j];
                        pos[j] = tmp2;
                    }
                    pos[0] = tmp;
                    selectorMtf[i] = (byte)j;
                }
            }

            // Assign actual codes for the tables.
            for (t = 0; t < nGroups; t++)
            {
                minLen = 32;
                maxLen = 0;
                for (i = 0; i < alphaSize; i++)
                {
                    if (len[t][i] > maxLen) maxLen = len[t][i];
                    if (len[t][i] < minLen) minLen = len[t][i];
                }
                if (maxLen > 17) throw new InvalidOperationException("bzip2 internal error 3004");
                if (minLen < 1) throw new InvalidOperationException("bzip2 internal error 3005");
                HbAssignCodes(code[t], len[t], minLen, maxLen, alphaSize);
            }

            // Transmit the mapping table.
            {
                Span<bool> inUse16 = stackalloc bool[16];
                for (i = 0; i < 16; i++)
                {
                    inUse16[i] = false;
                    for (j = 0; j < 16; j++)
                        if (inUse[i * 16 + j]) inUse16[i] = true;
                }
                for (i = 0; i < 16; i++)
                    BsW(1, inUse16[i] ? 1u : 0u);
                for (i = 0; i < 16; i++)
                    if (inUse16[i])
                        for (j = 0; j < 16; j++)
                            BsW(1, inUse[i * 16 + j] ? 1u : 0u);
            }

            // Now the selectors.
            BsW(3, (uint)nGroups);
            BsW(15, (uint)nSelectors);
            for (i = 0; i < nSelectors; i++)
            {
                for (j = 0; j < selectorMtf[i]; j++) BsW(1, 1);
                BsW(1, 0);
            }

            // Now the coding tables.
            for (t = 0; t < nGroups; t++)
            {
                int curr = len[t][0];
                BsW(5, (uint)curr);
                for (i = 0; i < alphaSize; i++)
                {
                    while (curr < len[t][i]) { BsW(2, 2); curr++; /* 10 */ }
                    while (curr > len[t][i]) { BsW(2, 3); curr--; /* 11 */ }
                    BsW(1, 0);
                }
            }

            // And finally, the block data proper
            selCtr = 0;
            gs = 0;
            while (true)
            {
                if (gs >= nMTF) break;
                ge = gs + Bz.G_SIZE - 1;
                if (ge >= nMTF) ge = nMTF - 1;
                if (!(selector[selCtr] < nGroups)) throw new InvalidOperationException("bzip2 internal error 3006");
                var lenSel = len[selector[selCtr]];
                var codeSel = code[selector[selCtr]];
                for (i = gs; i <= ge; i++)
                    BsW(lenSel[mtfv[i]], (uint)codeSel[mtfv[i]]);
                gs = ge + 1;
                selCtr++;
            }
            if (selCtr != nSelectors) throw new InvalidOperationException("bzip2 internal error 3007");
        }

        /// <summary>BZ2_compressBlock</summary>
        private void CompressBlock(bool is_last_block)
        {
            if (nblock > 0)
            {
                blockCRC = ~blockCRC;
                combinedCRC = (combinedCRC << 1) | (combinedCRC >> 31);
                combinedCRC ^= blockCRC;
                if (blockNo > 1) numZ = 0;
                BlockSort();
            }

            // the output area: what the C code finds after the block in arr2
            if (zbits == null)
                // at most 17 bits per MTF value plus the tables and selectors
                zbits = CodecBufferPool.Rent<byte>(100000 * blockSize100k / 4 * 9 + 65536);

            // If this is the first block, create the stream header.
            if (blockNo == 1)
            {
                BsInitWrite();
                BsPutUChar(Bz.HDR_B);
                BsPutUChar(Bz.HDR_Z);
                BsPutUChar(Bz.HDR_h);
                BsPutUChar((byte)(Bz.HDR_0 + blockSize100k));
            }

            if (nblock > 0)
            {
                BsPutUChar(0x31); BsPutUChar(0x41);
                BsPutUChar(0x59); BsPutUChar(0x26);
                BsPutUChar(0x53); BsPutUChar(0x59);

                // Now the block's CRC, so it is in a known place.
                BsPutUInt32(blockCRC);

                // A single bit indicating (non-)randomisation: never randomised since 0.9.5.
                BsW(1, 0);

                BsW(24, (uint)origPtr);
                mtfv ??= CodecBufferPool.Rent<ushort>(100000 * blockSize100k + 1);
                GenerateMTFValues();
                SendMTFValues();
            }

            // If this is the last block, add the stream trailer.
            if (is_last_block)
            {
                BsPutUChar(0x17); BsPutUChar(0x72);
                BsPutUChar(0x45); BsPutUChar(0x38);
                BsPutUChar(0x50); BsPutUChar(0x90);
                BsPutUInt32(combinedCRC);
                BsFinishWrite();
            }
        }

        // ---------------------------------------------------------------- huffman.c

        /// <summary>BZ2_hbMakeCodeLengths</summary>
        internal static void HbMakeCodeLengths(byte[] len, int[] freq, int alphaSize, int maxLen)
        {
            // Nodes and heap entries run from 1. Entry 0 for both the heap and nodes is a sentinel.
            int nNodes, nHeap, n1, n2, i, j, k;
            bool tooLong;
            Span<int> heap = stackalloc int[Bz.MAX_ALPHA_SIZE + 2];
            Span<int> weight = stackalloc int[Bz.MAX_ALPHA_SIZE * 2];
            Span<int> parent = stackalloc int[Bz.MAX_ALPHA_SIZE * 2];

            for (i = 0; i < alphaSize; i++)
                weight[i + 1] = (freq[i] == 0 ? 1 : freq[i]) << 8;

            while (true)
            {
                nNodes = alphaSize;
                nHeap = 0;
                heap[0] = 0;
                weight[0] = 0;
                parent[0] = -2;

                for (i = 1; i <= alphaSize; i++)
                {
                    parent[i] = -1;
                    nHeap++;
                    heap[nHeap] = i;
                    UpHeap(heap, weight, nHeap);
                }

                while (nHeap > 1)
                {
                    n1 = heap[1]; heap[1] = heap[nHeap]; nHeap--; DownHeap(heap, weight, nHeap, 1);
                    n2 = heap[1]; heap[1] = heap[nHeap]; nHeap--; DownHeap(heap, weight, nHeap, 1);
                    nNodes++;
                    parent[n1] = parent[n2] = nNodes;
                    // ADDWEIGHTS
                    int w1 = weight[n1], w2 = weight[n2];
                    weight[nNodes] = (int)(((uint)w1 & 0xffffff00) + ((uint)w2 & 0xffffff00))
                        | (1 + Math.Max(w1 & 0xff, w2 & 0xff));
                    parent[nNodes] = -1;
                    nHeap++;
                    heap[nHeap] = nNodes;
                    UpHeap(heap, weight, nHeap);
                }

                tooLong = false;
                for (i = 1; i <= alphaSize; i++)
                {
                    j = 0;
                    k = i;
                    while (parent[k] >= 0) { k = parent[k]; j++; }
                    len[i - 1] = (byte)j;
                    if (j > maxLen) tooLong = true;
                }

                if (!tooLong) break;

                for (i = 1; i <= alphaSize; i++)
                {
                    j = weight[i] >> 8;
                    j = 1 + (j / 2);
                    weight[i] = j << 8;
                }
            }
        }

        // UPHEAP
        private static void UpHeap(Span<int> heap, Span<int> weight, int z)
        {
            int zz = z, tmp = heap[zz];
            while (weight[tmp] < weight[heap[zz >> 1]])
            {
                heap[zz] = heap[zz >> 1];
                zz >>= 1;
            }
            heap[zz] = tmp;
        }

        // DOWNHEAP
        private static void DownHeap(Span<int> heap, Span<int> weight, int nHeap, int z)
        {
            int zz = z, yy, tmp = heap[zz];
            while (true)
            {
                yy = zz << 1;
                if (yy > nHeap) break;
                if (yy < nHeap && weight[heap[yy + 1]] < weight[heap[yy]])
                    yy++;
                if (weight[tmp] < weight[heap[yy]]) break;
                heap[zz] = heap[yy];
                zz = yy;
            }
            heap[zz] = tmp;
        }

        /// <summary>BZ2_hbAssignCodes</summary>
        internal static void HbAssignCodes(int[] code, byte[] length, int minLen, int maxLen, int alphaSize)
        {
            int vec = 0;
            for (int n = minLen; n <= maxLen; n++)
            {
                for (int i = 0; i < alphaSize; i++)
                    if (length[i] == n) { code[i] = vec; vec++; }
                vec <<= 1;
            }
        }

        // ---------------------------------------------------------------- blocksort.c

        /// <summary>BZ2_blockSort</summary>
        private void BlockSort()
        {
            int i;
            if (nblock < 10000)
            {
                FallbackSort();
            }
            else
            {
                quadrant ??= CodecBufferPool.Rent<ushort>(100000 * blockSize100k + Bz.N_OVERSHOOT);
                int wfact = workFactor;
                if (wfact < 1) wfact = 1;
                if (wfact > 100) wfact = 100;
                int budgetInit = nblock * ((wfact - 1) / 3);
                int budget = budgetInit;

                MainSort(ref budget);
                if (budget < 0)
                    FallbackSort();
            }

            origPtr = -1;
            for (i = 0; i < nblock; i++)
                if (ptr[i] == 0)
                {
                    origPtr = i;
                    break;
                }
            if (origPtr == -1) throw new InvalidOperationException("bzip2 internal error 1003");
        }

        /// <summary>fallbackSimpleSort</summary>
        private static void FallbackSimpleSort(uint[] fmap, uint[] eclass, int lo, int hi)
        {
            int i, j, tmp;
            uint ec_tmp;
            if (lo == hi) return;
            if (hi - lo > 3)
            {
                for (i = hi - 4; i >= lo; i--)
                {
                    tmp = (int)fmap[i];
                    ec_tmp = eclass[tmp];
                    for (j = i + 4; j <= hi && ec_tmp > eclass[fmap[j]]; j += 4)
                        fmap[j - 4] = fmap[j];
                    fmap[j - 4] = (uint)tmp;
                }
            }
            for (i = hi - 1; i >= lo; i--)
            {
                tmp = (int)fmap[i];
                ec_tmp = eclass[tmp];
                for (j = i + 1; j <= hi && ec_tmp > eclass[fmap[j]]; j++)
                    fmap[j - 1] = fmap[j];
                fmap[j - 1] = (uint)tmp;
            }
        }

        private const int FALLBACK_QSORT_SMALL_THRESH = 10, FALLBACK_QSORT_STACK_SIZE = 100;

        /// <summary>fallbackQSort3</summary>
        private static void FallbackQSort3(uint[] fmap, uint[] eclass, int loSt, int hiSt)
        {
            int unLo, unHi, ltLo, gtHi, n, m;
            int sp, lo, hi;
            uint med, r, r3;
            Span<int> stackLo = stackalloc int[FALLBACK_QSORT_STACK_SIZE];
            Span<int> stackHi = stackalloc int[FALLBACK_QSORT_STACK_SIZE];

            r = 0;
            sp = 0;
            stackLo[sp] = loSt; stackHi[sp] = hiSt; sp++;

            while (sp > 0)
            {
                if (!(sp < FALLBACK_QSORT_STACK_SIZE - 1)) throw new InvalidOperationException("bzip2 internal error 1004");
                sp--; lo = stackLo[sp]; hi = stackHi[sp];
                if (hi - lo < FALLBACK_QSORT_SMALL_THRESH)
                {
                    FallbackSimpleSort(fmap, eclass, lo, hi);
                    continue;
                }

                // Random partitioning (Sedgewick's constants 7621 and 32768).
                r = ((r * 7621) + 1) % 32768;
                r3 = r % 3;
                if (r3 == 0) med = eclass[fmap[lo]];
                else if (r3 == 1) med = eclass[fmap[(lo + hi) >> 1]];
                else med = eclass[fmap[hi]];

                unLo = ltLo = lo;
                unHi = gtHi = hi;

                while (true)
                {
                    while (true)
                    {
                        if (unLo > unHi) break;
                        n = (int)eclass[fmap[unLo]] - (int)med;
                        if (n == 0)
                        {
                            (fmap[unLo], fmap[ltLo]) = (fmap[ltLo], fmap[unLo]);
                            ltLo++; unLo++;
                            continue;
                        }
                        if (n > 0) break;
                        unLo++;
                    }
                    while (true)
                    {
                        if (unLo > unHi) break;
                        n = (int)eclass[fmap[unHi]] - (int)med;
                        if (n == 0)
                        {
                            (fmap[unHi], fmap[gtHi]) = (fmap[gtHi], fmap[unHi]);
                            gtHi--; unHi--;
                            continue;
                        }
                        if (n < 0) break;
                        unHi--;
                    }
                    if (unLo > unHi) break;
                    (fmap[unLo], fmap[unHi]) = (fmap[unHi], fmap[unLo]);
                    unLo++; unHi--;
                }

                if (gtHi < ltLo) continue;

                n = Math.Min(ltLo - lo, unLo - ltLo); VSwap(fmap, lo, unLo - n, n);
                m = Math.Min(hi - gtHi, gtHi - unHi); VSwap(fmap, unLo, hi - m + 1, m);

                n = lo + unLo - ltLo - 1;
                m = hi - (gtHi - unHi) + 1;

                if (n - lo > hi - m)
                {
                    stackLo[sp] = lo; stackHi[sp] = n; sp++;
                    stackLo[sp] = m; stackHi[sp] = hi; sp++;
                }
                else
                {
                    stackLo[sp] = m; stackHi[sp] = hi; sp++;
                    stackLo[sp] = lo; stackHi[sp] = n; sp++;
                }
            }
        }

        // fvswap / mvswap
        private static void VSwap(uint[] a, int p1, int p2, int n)
        {
            while (n > 0)
            {
                (a[p1], a[p2]) = (a[p2], a[p1]);
                p1++; p2++; n--;
            }
        }

        /// <summary>
        /// fallbackSort: fmap is arr1, eclass is arr2 as UInt32 (its own array here, so the block
        /// need not be rebuilt from ftabCopy afterwards), bhtab is ftab.
        /// </summary>
        private void FallbackSort()
        {
            var fmap = ptr;
            if (this.eclass == null || this.eclass.Length < nblock) { CodecBufferPool.Return(this.eclass); this.eclass = CodecBufferPool.Rent<uint>(nblock); }
            var eclass = this.eclass;
            var bhtab = ftab;
            var eclass8 = block;
            Span<int> ftabL = stackalloc int[257];
            int H, i, j, k, l, r, cc, cc1;
            int nNotDone;
            int nBhtab;

            // Initial 1-char radix sort to generate initial fmap and initial BH bits.
            for (i = 0; i < 257; i++) ftabL[i] = 0;
            for (i = 0; i < nblock; i++) ftabL[eclass8[i]]++;
            for (i = 1; i < 257; i++) ftabL[i] += ftabL[i - 1];
            for (i = 0; i < nblock; i++)
            {
                j = eclass8[i];
                k = ftabL[j] - 1;
                ftabL[j] = k;
                fmap[k] = (uint)i;
            }

            nBhtab = 2 + (nblock / 32);
            for (i = 0; i < nBhtab; i++) bhtab[i] = 0;
            for (i = 0; i < 256; i++) SetBH(bhtab, ftabL[i]);

            // set sentinel bits for block-end detection
            for (i = 0; i < 32; i++)
            {
                SetBH(bhtab, nblock + 2 * i);
                ClearBH(bhtab, nblock + 2 * i + 1);
            }

            // the log(N) loop
            H = 1;
            while (true)
            {
                j = 0;
                for (i = 0; i < nblock; i++)
                {
                    if (IsSetBH(bhtab, i)) j = i;
                    k = (int)fmap[i] - H; if (k < 0) k += nblock;
                    eclass[k] = (uint)j;
                }

                nNotDone = 0;
                r = -1;
                while (true)
                {
                    // find the next non-singleton bucket
                    k = r + 1;
                    while (IsSetBH(bhtab, k) && (k & 0x1f) != 0) k++;
                    if (IsSetBH(bhtab, k))
                    {
                        while (bhtab[k >> 5] == 0xffffffff) k += 32;
                        while (IsSetBH(bhtab, k)) k++;
                    }
                    l = k - 1;
                    if (l >= nblock) break;
                    while (!IsSetBH(bhtab, k) && (k & 0x1f) != 0) k++;
                    if (!IsSetBH(bhtab, k))
                    {
                        while (bhtab[k >> 5] == 0x00000000) k += 32;
                        while (!IsSetBH(bhtab, k)) k++;
                    }
                    r = k - 1;
                    if (r >= nblock) break;

                    // now [l, r] bracket current bucket
                    if (r > l)
                    {
                        nNotDone += (r - l + 1);
                        FallbackQSort3(fmap, eclass, l, r);

                        // scan bucket and generate header bits
                        cc = -1;
                        for (i = l; i <= r; i++)
                        {
                            cc1 = (int)eclass[fmap[i]];
                            if (cc != cc1) { SetBH(bhtab, i); cc = cc1; }
                        }
                    }
                }

                H *= 2;
                if (H > nblock || nNotDone == 0) break;
            }
            // The C code rebuilds the block in eclass8 here; this block was never overwritten.
        }

        private static void SetBH(uint[] bhtab, int zz) => bhtab[zz >> 5] |= 1u << (zz & 31);
        private static void ClearBH(uint[] bhtab, int zz) => bhtab[zz >> 5] &= ~(1u << (zz & 31));
        private static bool IsSetBH(uint[] bhtab, int zz) => (bhtab[zz >> 5] & (1u << (zz & 31))) != 0;

        /// <summary>mainGtU</summary>
        private static bool MainGtU(uint i1, uint i2, byte[] block, ushort[] quadrant, uint nblock, ref int budget)
        {
            int k;
            byte c1, c2;
            ushort s1, s2;

            // 12 leading bytes
            for (int n = 0; n < 12; n++)
            {
                c1 = block[i1]; c2 = block[i2];
                if (c1 != c2) return c1 > c2;
                i1++; i2++;
            }

            k = (int)nblock + 8;

            do
            {
                for (int n = 0; n < 8; n++)
                {
                    c1 = block[i1]; c2 = block[i2];
                    if (c1 != c2) return c1 > c2;
                    s1 = quadrant[i1]; s2 = quadrant[i2];
                    if (s1 != s2) return s1 > s2;
                    i1++; i2++;
                }

                if (i1 >= nblock) i1 -= nblock;
                if (i2 >= nblock) i2 -= nblock;

                k -= 8;
                budget--;
            }
            while (k >= 0);

            return false;
        }

        // Knuth's increments
        private static readonly int[] incs = { 1, 4, 13, 40, 121, 364, 1093, 3280, 9841, 29524, 88573, 265720, 797161, 2391484 };

        /// <summary>mainSimpleSort</summary>
        private static void MainSimpleSort(uint[] ptr, byte[] block, ushort[] quadrant, int nblock, int lo, int hi, int d, ref int budget)
        {
            int i, j, h, bigN, hp;
            uint v;

            bigN = hi - lo + 1;
            if (bigN < 2) return;

            hp = 0;
            while (incs[hp] < bigN) hp++;
            hp--;

            for (; hp >= 0; hp--)
            {
                h = incs[hp];

                i = lo + h;
                while (true)
                {
                    // copy 1, 2, 3
                    for (int copy = 0; copy < 3; copy++)
                    {
                        if (i > hi) goto next_h;
                        v = ptr[i];
                        j = i;
                        while (MainGtU((uint)(ptr[j - h] + d), (uint)(v + d), block, quadrant, (uint)nblock, ref budget))
                        {
                            ptr[j] = ptr[j - h];
                            j = j - h;
                            if (j <= (lo + h - 1)) break;
                        }
                        ptr[j] = v;
                        i++;
                    }
                    if (budget < 0) return;
                }
            next_h:;
            }
        }

        /// <summary>mmed3</summary>
        private static byte Mmed3(byte a, byte b, byte c)
        {
            byte t;
            if (a > b) { t = a; a = b; b = t; }
            if (b > c)
            {
                b = c;
                if (a > b) b = a;
            }
            return b;
        }

        private const int MAIN_QSORT_SMALL_THRESH = 20, MAIN_QSORT_DEPTH_THRESH = Bz.N_RADIX + Bz.N_QSORT, MAIN_QSORT_STACK_SIZE = 100;

        /// <summary>mainQSort3</summary>
        private static void MainQSort3(uint[] ptr, byte[] block, ushort[] quadrant, int nblock, int loSt, int hiSt, int dSt, ref int budget)
        {
            int unLo, unHi, ltLo, gtHi, n, m, med;
            int sp, lo, hi, d;

            Span<int> stackLo = stackalloc int[MAIN_QSORT_STACK_SIZE];
            Span<int> stackHi = stackalloc int[MAIN_QSORT_STACK_SIZE];
            Span<int> stackD = stackalloc int[MAIN_QSORT_STACK_SIZE];

            Span<int> nextLo = stackalloc int[3];
            Span<int> nextHi = stackalloc int[3];
            Span<int> nextD = stackalloc int[3];

            sp = 0;
            stackLo[sp] = loSt; stackHi[sp] = hiSt; stackD[sp] = dSt; sp++;

            while (sp > 0)
            {
                if (!(sp < MAIN_QSORT_STACK_SIZE - 2)) throw new InvalidOperationException("bzip2 internal error 1001");

                sp--; lo = stackLo[sp]; hi = stackHi[sp]; d = stackD[sp];
                if (hi - lo < MAIN_QSORT_SMALL_THRESH || d > MAIN_QSORT_DEPTH_THRESH)
                {
                    MainSimpleSort(ptr, block, quadrant, nblock, lo, hi, d, ref budget);
                    if (budget < 0) return;
                    continue;
                }

                med = Mmed3(block[ptr[lo] + d], block[ptr[hi] + d], block[ptr[(lo + hi) >> 1] + d]);

                unLo = ltLo = lo;
                unHi = gtHi = hi;

                while (true)
                {
                    while (true)
                    {
                        if (unLo > unHi) break;
                        n = block[ptr[unLo] + d] - med;
                        if (n == 0)
                        {
                            (ptr[unLo], ptr[ltLo]) = (ptr[ltLo], ptr[unLo]);
                            ltLo++; unLo++; continue;
                        }
                        if (n > 0) break;
                        unLo++;
                    }
                    while (true)
                    {
                        if (unLo > unHi) break;
                        n = block[ptr[unHi] + d] - med;
                        if (n == 0)
                        {
                            (ptr[unHi], ptr[gtHi]) = (ptr[gtHi], ptr[unHi]);
                            gtHi--; unHi--; continue;
                        }
                        if (n < 0) break;
                        unHi--;
                    }
                    if (unLo > unHi) break;
                    (ptr[unLo], ptr[unHi]) = (ptr[unHi], ptr[unLo]);
                    unLo++; unHi--;
                }

                if (gtHi < ltLo)
                {
                    stackLo[sp] = lo; stackHi[sp] = hi; stackD[sp] = d + 1; sp++;
                    continue;
                }

                n = Math.Min(ltLo - lo, unLo - ltLo); VSwap(ptr, lo, unLo - n, n);
                m = Math.Min(hi - gtHi, gtHi - unHi); VSwap(ptr, unLo, hi - m + 1, m);

                n = lo + unLo - ltLo - 1;
                m = hi - (gtHi - unHi) + 1;

                nextLo[0] = lo; nextHi[0] = n; nextD[0] = d;
                nextLo[1] = m; nextHi[1] = hi; nextD[1] = d;
                nextLo[2] = n + 1; nextHi[2] = m - 1; nextD[2] = d + 1;

                if (NextSize(nextLo, nextHi, 0) < NextSize(nextLo, nextHi, 1)) NextSwap(nextLo, nextHi, nextD, 0, 1);
                if (NextSize(nextLo, nextHi, 1) < NextSize(nextLo, nextHi, 2)) NextSwap(nextLo, nextHi, nextD, 1, 2);
                if (NextSize(nextLo, nextHi, 0) < NextSize(nextLo, nextHi, 1)) NextSwap(nextLo, nextHi, nextD, 0, 1);

                for (int z = 0; z < 3; z++)
                {
                    stackLo[sp] = nextLo[z]; stackHi[sp] = nextHi[z]; stackD[sp] = nextD[z]; sp++;
                }
            }
        }

        // mnextsize / mnextswap
        private static int NextSize(Span<int> lo, Span<int> hi, int az) => hi[az] - lo[az];

        private static void NextSwap(Span<int> lo, Span<int> hi, Span<int> d, int az, int bz)
        {
            (lo[az], lo[bz]) = (lo[bz], lo[az]);
            (hi[az], hi[bz]) = (hi[bz], hi[az]);
            (d[az], d[bz]) = (d[bz], d[az]);
        }

        private const uint SETMASK = 1 << 21, CLEARMASK = ~SETMASK;

        /// <summary>mainSort</summary>
        private void MainSort(ref int budget)
        {
            var ptr = this.ptr;
            var block = this.block;
            var quadrant = this.quadrant;
            var ftab = this.ftab;
            int nblock = this.nblock;
            int i, j, k, ss, sb;
            Span<int> runningOrder = stackalloc int[256];
            Span<bool> bigDone = stackalloc bool[256];
            Span<int> copyStart = stackalloc int[256];
            Span<int> copyEnd = stackalloc int[256];
            byte c1;
            int numQSorted;
            ushort s;

            // set up the 2-byte frequency table
            for (i = 65536; i >= 0; i--) ftab[i] = 0;

            j = block[0] << 8;
            i = nblock - 1;
            for (; i >= 0; i--)
            {
                quadrant[i] = 0;
                j = (j >> 8) | (block[i] << 8);
                ftab[j]++;
            }

            // (emphasises close relationship of block & quadrant)
            for (i = 0; i < Bz.N_OVERSHOOT; i++)
            {
                block[nblock + i] = block[i];
                quadrant[nblock + i] = 0;
            }

            // Complete the initial radix sort
            for (i = 1; i <= 65536; i++) ftab[i] += ftab[i - 1];

            s = (ushort)(block[0] << 8);
            i = nblock - 1;
            for (; i >= 0; i--)
            {
                s = (ushort)((s >> 8) | (block[i] << 8));
                j = (int)ftab[s] - 1;
                ftab[s] = (uint)j;
                ptr[j] = (uint)i;
            }

            // the running order, from smallest to largest big bucket
            for (i = 0; i <= 255; i++)
            {
                bigDone[i] = false;
                runningOrder[i] = i;
            }

            {
                int vv;
                int h = 1;
                do h = 3 * h + 1; while (h <= 256);
                do
                {
                    h = h / 3;
                    for (i = h; i <= 255; i++)
                    {
                        vv = runningOrder[i];
                        j = i;
                        while (BigFreq(ftab, runningOrder[j - h]) > BigFreq(ftab, vv))
                        {
                            runningOrder[j] = runningOrder[j - h];
                            j = j - h;
                            if (j <= (h - 1)) break;
                        }
                        runningOrder[j] = vv;
                    }
                } while (h != 1);
            }

            // The main sorting loop.
            numQSorted = 0;

            for (i = 0; i <= 255; i++)
            {
                // Process big buckets, starting with the least full.
                ss = runningOrder[i];

                // Step 1: complete the big bucket [ss] by quicksorting any unsorted small buckets [ss, j], j != ss.
                for (j = 0; j <= 255; j++)
                {
                    if (j != ss)
                    {
                        sb = (ss << 8) + j;
                        if ((ftab[sb] & SETMASK) == 0)
                        {
                            int lo = (int)(ftab[sb] & CLEARMASK);
                            int hi = (int)(ftab[sb + 1] & CLEARMASK) - 1;
                            if (hi > lo)
                            {
                                MainQSort3(ptr, block, quadrant, nblock, lo, hi, Bz.N_RADIX, ref budget);
                                numQSorted += (hi - lo + 1);
                                if (budget < 0) return;
                            }
                        }
                        ftab[sb] |= SETMASK;
                    }
                }

                if (bigDone[ss]) throw new InvalidOperationException("bzip2 internal error 1006");

                // Step 2: scan this big bucket [ss] to synthesise the sorted order for small buckets [t, ss].
                {
                    for (j = 0; j <= 255; j++)
                    {
                        copyStart[j] = (int)(ftab[(j << 8) + ss] & CLEARMASK);
                        copyEnd[j] = (int)(ftab[(j << 8) + ss + 1] & CLEARMASK) - 1;
                    }
                    for (j = (int)(ftab[ss << 8] & CLEARMASK); j < copyStart[ss]; j++)
                    {
                        k = (int)ptr[j] - 1; if (k < 0) k += nblock;
                        c1 = block[k];
                        if (!bigDone[c1])
                            ptr[copyStart[c1]++] = (uint)k;
                    }
                    for (j = (int)(ftab[(ss + 1) << 8] & CLEARMASK) - 1; j > copyEnd[ss]; j--)
                    {
                        k = (int)ptr[j] - 1; if (k < 0) k += nblock;
                        c1 = block[k];
                        if (!bigDone[c1])
                            ptr[copyEnd[c1]--] = (uint)k;
                    }
                }

                if (!((copyStart[ss] - 1 == copyEnd[ss]) || (copyStart[ss] == 0 && copyEnd[ss] == nblock - 1)))
                    throw new InvalidOperationException("bzip2 internal error 1007");

                for (j = 0; j <= 255; j++) ftab[(j << 8) + ss] |= SETMASK;

                // Step 3: the [ss] big bucket is now done; update the quadrant descriptors.
                bigDone[ss] = true;

                if (i < 255)
                {
                    int bbStart = (int)(ftab[ss << 8] & CLEARMASK);
                    int bbSize = (int)(ftab[(ss + 1) << 8] & CLEARMASK) - bbStart;
                    int shifts = 0;

                    while ((bbSize >> shifts) > 65534) shifts++;

                    for (j = bbSize - 1; j >= 0; j--)
                    {
                        int a2update = (int)ptr[bbStart + j];
                        ushort qVal = (ushort)(j >> shifts);
                        quadrant[a2update] = qVal;
                        if (a2update < Bz.N_OVERSHOOT)
                            quadrant[a2update + nblock] = qVal;
                    }
                    if (!(((bbSize - 1) >> shifts) <= 65535)) throw new InvalidOperationException("bzip2 internal error 1002");
                }
            }
        }

        // BIGFREQ
        private static int BigFreq(uint[] ftab, int b) => (int)(ftab[(b + 1) << 8] - ftab[b << 8]);
    }
}
