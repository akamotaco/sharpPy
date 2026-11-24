using System;
using System.Collections.Generic;

namespace SharpPy.Modules.Bz2
{
    /// <summary>
    /// DState and the decompression side of bzip2 1.0.8: bzlib.c (BZ2_bzDecompressInit,
    /// BZ2_bzDecompress, unRLE_obuf_to_output_FAST, BZ2_indexIntoF) and decompress.c
    /// (BZ2_decompress, makeMaps_d) with huffman.c's BZ2_hbCreateDecodeTables.
    ///
    /// BZ2_decompress is a resumable state machine: each GET_BITS is a case label the function
    /// re-enters at when the input ran out, with its locals saved in s->save_*. Here it is an
    /// iterator: running out of input yields BZ_OK and the next call resumes at the same point with
    /// the same locals, which is what the labels and the save area do. Only the "fast" (small == 0)
    /// layout is kept, the one Modules/_bz2module.c asks for with BZ2_bzDecompressInit(&amp;bzs, 0, 0).
    /// </summary>
    internal sealed class BzDState
    {
        // states for decompression
        private const int X_IDLE = 1, X_OUTPUT = 2, X_MAGIC_1 = 10;

        private const int MTFA_SIZE = 4096, MTFL_SIZE = 16;

        private BzStream strm;
        private int state;

        // for doing the final run-length decoding
        private byte state_out_ch;
        private int state_out_len;
        private bool blockRandomised;
        private int rNToGo, rTPos;

        // the buffer for bit stream reading
        private uint bsBuff;
        private int bsLive;

        private int blockSize100k;
        private int currBlockNo;

        // for undoing the Burrows-Wheeler transform
        private int origPtr;
        private uint tPos;
        private int k0;
        private readonly int[] unzftab = new int[256];
        private int nblock_used;
        private readonly int[] cftab = new int[257];

        // for undoing the Burrows-Wheeler transform (FAST)
        private uint[] tt;

        // stored and calculated CRCs
        private uint storedBlockCRC, storedCombinedCRC, calculatedBlockCRC, calculatedCombinedCRC;

        // map of bytes used in block
        private int nInUse;
        private readonly bool[] inUse = new bool[256];
        private readonly bool[] inUse16 = new bool[16];
        private readonly byte[] seqToUnseq = new byte[256];

        // for decoding the MTF values
        private readonly byte[] mtfa = new byte[MTFA_SIZE];
        private readonly int[] mtfbase = new int[256 / MTFL_SIZE];
        private readonly byte[] selector = new byte[Bz.MAX_SELECTORS];
        private readonly byte[] selectorMtf = new byte[Bz.MAX_SELECTORS];
        private readonly byte[][] len = Jagged<byte>(Bz.N_GROUPS, Bz.MAX_ALPHA_SIZE);
        private readonly int[][] limit = Jagged<int>(Bz.N_GROUPS, Bz.MAX_ALPHA_SIZE);
        private readonly int[][] @base = Jagged<int>(Bz.N_GROUPS, Bz.MAX_ALPHA_SIZE);
        private readonly int[][] perm = Jagged<int>(Bz.N_GROUPS, Bz.MAX_ALPHA_SIZE);
        private readonly int[] minLens = new int[Bz.N_GROUPS];

        // save_nblock: what unRLE_obuf_to_output_FAST compares nblock_used with
        private int save_nblock;

        // the suspended BZ2_decompress, and the value its last RETURN gave
        private IEnumerator<int> machine;

        private static T[][] Jagged<T>(int n, int m)
        {
            var a = new T[n][];
            for (int i = 0; i < n; i++) a[i] = new T[m];
            return a;
        }

        /// <summary>BZ2_bzDecompressInit (small is always 0 here; verbosity prints nothing).</summary>
        internal static int DecompressInit(BzStream strm, int verbosity, int small)
        {
            if (strm == null) return Bz.PARAM_ERROR;
            if (small != 0 && small != 1) return Bz.PARAM_ERROR;
            if (verbosity < 0 || verbosity > 4) return Bz.PARAM_ERROR;
            var s = new BzDState();
            s.strm = strm;
            strm.DState = s;
            s.state = X_MAGIC_1;
            s.bsLive = 0;
            s.bsBuff = 0;
            s.calculatedCombinedCRC = 0;
            strm.TotalIn = 0;
            strm.TotalOut = 0;
            s.tt = null;
            s.currBlockNo = 0;
            return Bz.OK;
        }

        // BZ_RAND_UPD_MASK, then BZ_RAND_MASK
        private int RandMask()
        {
            if (rNToGo == 0)
            {
                rNToGo = Bz.RNums[rTPos];
                rTPos++;
                if (rTPos == 512) rTPos = 0;
            }
            rNToGo--;
            return rNToGo == 1 ? 1 : 0;
        }

        /// <summary>unRLE_obuf_to_output_FAST: true iff data corruption is discovered.</summary>
        private bool UnRLEObufToOutputFast()
        {
            byte k1;
            uint nmax = (uint)(100000 * blockSize100k);

            if (blockRandomised)
            {
                while (true)
                {
                    // try to finish existing run
                    while (true)
                    {
                        if (strm.AvailOut == 0) return false;
                        if (state_out_len == 0) break;
                        strm.OutBuf[strm.NextOut] = state_out_ch;
                        calculatedBlockCRC = (calculatedBlockCRC << 8) ^ Bz.Crc32Table[(calculatedBlockCRC >> 24) ^ state_out_ch];
                        state_out_len--;
                        strm.NextOut++;
                        strm.AvailOut--;
                        strm.TotalOut++;
                    }

                    // can a new run be started?
                    if (nblock_used == save_nblock + 1) return false;

                    // Only caused by corrupt data stream?
                    if (nblock_used > save_nblock + 1)
                        return true;

                    state_out_len = 1;
                    state_out_ch = (byte)k0;
                    // BZ_GET_FAST(k1)
                    if (tPos >= nmax) return true;
                    tPos = tt[tPos]; k1 = (byte)(tPos & 0xff); tPos >>= 8;
                    k1 ^= (byte)RandMask(); nblock_used++;
                    if (nblock_used == save_nblock + 1) continue;
                    if (k1 != k0) { k0 = k1; continue; }

                    state_out_len = 2;
                    if (tPos >= nmax) return true;
                    tPos = tt[tPos]; k1 = (byte)(tPos & 0xff); tPos >>= 8;
                    k1 ^= (byte)RandMask(); nblock_used++;
                    if (nblock_used == save_nblock + 1) continue;
                    if (k1 != k0) { k0 = k1; continue; }

                    state_out_len = 3;
                    if (tPos >= nmax) return true;
                    tPos = tt[tPos]; k1 = (byte)(tPos & 0xff); tPos >>= 8;
                    k1 ^= (byte)RandMask(); nblock_used++;
                    if (nblock_used == save_nblock + 1) continue;
                    if (k1 != k0) { k0 = k1; continue; }

                    if (tPos >= nmax) return true;
                    tPos = tt[tPos]; k1 = (byte)(tPos & 0xff); tPos >>= 8;
                    k1 ^= (byte)RandMask(); nblock_used++;
                    state_out_len = k1 + 4;
                    if (tPos >= nmax) return true;
                    tPos = tt[tPos]; k0 = (byte)(tPos & 0xff); tPos >>= 8;
                    k0 ^= RandMask(); nblock_used++;
                }
            }
            else
            {
                // restore
                uint c_calculatedBlockCRC = calculatedBlockCRC;
                byte c_state_out_ch = state_out_ch;
                int c_state_out_len = state_out_len;
                int c_nblock_used = nblock_used;
                int c_k0 = k0;
                uint[] c_tt = tt;
                uint c_tPos = tPos;
                byte[] cs_out = strm.OutBuf;
                int cs_next_out = strm.NextOut;
                uint cs_avail_out = strm.AvailOut;
                var crcTable = Bz.Crc32Table;
                // end restore

                uint avail_out_INIT = cs_avail_out;
                int s_save_nblockPP = save_nblock + 1;
                bool corrupt = false;

                while (true)
                {
                    // try to finish existing run
                    if (c_state_out_len > 0)
                    {
                        while (true)
                        {
                            if (cs_avail_out == 0) goto return_notr;
                            if (c_state_out_len == 1) break;
                            cs_out[cs_next_out] = c_state_out_ch;
                            c_calculatedBlockCRC = (c_calculatedBlockCRC << 8) ^ crcTable[(c_calculatedBlockCRC >> 24) ^ c_state_out_ch];
                            c_state_out_len--;
                            cs_next_out++;
                            cs_avail_out--;
                        }
                    s_state_out_len_eq_one:
                        {
                            if (cs_avail_out == 0)
                            {
                                c_state_out_len = 1; goto return_notr;
                            }
                            cs_out[cs_next_out] = c_state_out_ch;
                            c_calculatedBlockCRC = (c_calculatedBlockCRC << 8) ^ crcTable[(c_calculatedBlockCRC >> 24) ^ c_state_out_ch];
                            cs_next_out++;
                            cs_avail_out--;
                        }
                    }
                    // Only caused by corrupt data stream?
                    if (c_nblock_used > s_save_nblockPP)
                    {
                        corrupt = true;
                        break;
                    }

                    // can a new run be started?
                    if (c_nblock_used == s_save_nblockPP)
                    {
                        c_state_out_len = 0; goto return_notr;
                    }
                    c_state_out_ch = (byte)c_k0;
                    // BZ_GET_FAST_C(k1)
                    if (c_tPos >= nmax) { corrupt = true; break; }
                    c_tPos = c_tt[c_tPos]; k1 = (byte)(c_tPos & 0xff); c_tPos >>= 8;
                    c_nblock_used++;
                    if (k1 != c_k0)
                    {
                        c_k0 = k1;
                        c_state_out_len = 1;
                        goto s_state_out_len_eq_one_outer;
                    }
                    if (c_nblock_used == s_save_nblockPP)
                    {
                        c_state_out_len = 1;
                        goto s_state_out_len_eq_one_outer;
                    }

                    c_state_out_len = 2;
                    if (c_tPos >= nmax) { corrupt = true; break; }
                    c_tPos = c_tt[c_tPos]; k1 = (byte)(c_tPos & 0xff); c_tPos >>= 8;
                    c_nblock_used++;
                    if (c_nblock_used == s_save_nblockPP) continue;
                    if (k1 != c_k0) { c_k0 = k1; continue; }

                    c_state_out_len = 3;
                    if (c_tPos >= nmax) { corrupt = true; break; }
                    c_tPos = c_tt[c_tPos]; k1 = (byte)(c_tPos & 0xff); c_tPos >>= 8;
                    c_nblock_used++;
                    if (c_nblock_used == s_save_nblockPP) continue;
                    if (k1 != c_k0) { c_k0 = k1; continue; }

                    if (c_tPos >= nmax) { corrupt = true; break; }
                    c_tPos = c_tt[c_tPos]; k1 = (byte)(c_tPos & 0xff); c_tPos >>= 8;
                    c_nblock_used++;
                    c_state_out_len = k1 + 4;
                    if (c_tPos >= nmax) { corrupt = true; break; }
                    c_tPos = c_tt[c_tPos]; c_k0 = (byte)(c_tPos & 0xff); c_tPos >>= 8;
                    c_nblock_used++;
                    continue;

                // The C code jumps into the middle of the run-finishing block (a run of length one);
                // C# cannot jump into a block, so the same step is spelled out here.
                s_state_out_len_eq_one_outer:
                    if (cs_avail_out == 0)
                    {
                        c_state_out_len = 1; goto return_notr;
                    }
                    cs_out[cs_next_out] = c_state_out_ch;
                    c_calculatedBlockCRC = (c_calculatedBlockCRC << 8) ^ crcTable[(c_calculatedBlockCRC >> 24) ^ c_state_out_ch];
                    cs_next_out++;
                    cs_avail_out--;
                    c_state_out_len = 0;
                }

                if (corrupt)
                    return true;

                return_notr:
                strm.TotalOut += avail_out_INIT - cs_avail_out;

                // save
                calculatedBlockCRC = c_calculatedBlockCRC;
                state_out_ch = c_state_out_ch;
                state_out_len = c_state_out_len;
                nblock_used = c_nblock_used;
                k0 = c_k0;
                tPos = c_tPos;
                strm.NextOut = cs_next_out;
                strm.AvailOut = cs_avail_out;
                // end save
            }
            return false;
        }

        /// <summary>BZ2_bzDecompress</summary>
        internal static int Decompress(BzStream strm)
        {
            var s = strm?.DState;
            if (s == null || s.strm != strm) return Bz.PARAM_ERROR;

            while (true)
            {
                if (s.state == X_IDLE) return Bz.SEQUENCE_ERROR;
                if (s.state == X_OUTPUT)
                {
                    bool corrupt = s.UnRLEObufToOutputFast();
                    if (corrupt) return Bz.DATA_ERROR;
                    if (s.nblock_used == s.save_nblock + 1 && s.state_out_len == 0)
                    {
                        s.calculatedBlockCRC = ~s.calculatedBlockCRC;
                        if (s.calculatedBlockCRC != s.storedBlockCRC)
                            return Bz.DATA_ERROR;
                        s.calculatedCombinedCRC = (s.calculatedCombinedCRC << 1) | (s.calculatedCombinedCRC >> 31);
                        s.calculatedCombinedCRC ^= s.calculatedBlockCRC;
                        s.state = X_MAGIC_1 + 4; // BZ_X_BLKHDR_1
                    }
                    else
                    {
                        return Bz.OK;
                    }
                }
                if (s.state >= X_MAGIC_1)
                {
                    int r = s.RunDecompress();
                    if (r == Bz.STREAM_END)
                    {
                        if (s.calculatedCombinedCRC != s.storedCombinedCRC)
                            return Bz.DATA_ERROR;
                        return r;
                    }
                    if (s.state != X_OUTPUT) return r;
                }
            }
        }

        /// <summary>BZ2_bzDecompressEnd</summary>
        internal static int DecompressEnd(BzStream strm)
        {
            var s = strm?.DState;
            if (s == null || s.strm != strm) return Bz.PARAM_ERROR;
            // BZFREE(s->tt): back to the pool (CodecBufferPool)
            CodecBufferPool.Return(s.tt);
            s.tt = null;
            s.machine = null;
            strm.DState = null;
            return Bz.OK;
        }

        /// <summary>makeMaps_d</summary>
        private void MakeMapsD()
        {
            nInUse = 0;
            for (int i = 0; i < 256; i++)
                if (inUse[i])
                {
                    seqToUnseq[nInUse] = (byte)i;
                    nInUse++;
                }
        }

        /// <summary>BZ2_decompress: resume the machine until its next RETURN.</summary>
        private int RunDecompress()
        {
            machine ??= DecompressMachine();
            machine.MoveNext();
            return machine.Current;
        }

        /// <summary>
        /// GET_BITS without the RETURN: true and the bits when the buffer holds them, false when the
        /// input ran out (the caller then RETURNs BZ_OK and comes back to the same GET_BITS).
        /// </summary>
        private bool GetBits(int nnn, out int vvv)
        {
            while (true)
            {
                if (bsLive >= nnn)
                {
                    vvv = (int)((bsBuff >> (bsLive - nnn)) & ((1u << nnn) - 1));
                    bsLive -= nnn;
                    return true;
                }
                if (strm.AvailIn == 0)
                {
                    vvv = 0;
                    return false;
                }
                bsBuff = (bsBuff << 8) | strm.InBuf[strm.NextIn];
                bsLive += 8;
                strm.NextIn++;
                strm.AvailIn--;
                strm.TotalIn++;
            }
        }

        /// <summary>
        /// The body of BZ2_decompress. "yield return v" is RETURN(v); a GET_BITS whose input ran out
        /// yields BZ_OK and loops back to itself, as the case label it stands for does.
        /// </summary>
        private IEnumerator<int> DecompressMachine()
        {
            int uc;
            int minLen, maxLen;
            int i, j, t;
            int alphaSize, nGroups, nSelectors, EOB;
            int groupNo, groupPos, nextSym, nblockMAX, nblock, es, N, curr;
            int zn, zvec, zj, gSel, gMinlen;
            int[] gLimit = null, gBase = null, gPerm = null;
            int v;

            // GET_UCHAR(BZ_X_MAGIC_1..4)
            while (!GetBits(8, out uc)) yield return Bz.OK;
            if (uc != Bz.HDR_B) { yield return Bz.DATA_ERROR_MAGIC; yield break; }
            while (!GetBits(8, out uc)) yield return Bz.OK;
            if (uc != Bz.HDR_Z) { yield return Bz.DATA_ERROR_MAGIC; yield break; }
            while (!GetBits(8, out uc)) yield return Bz.OK;
            if (uc != Bz.HDR_h) { yield return Bz.DATA_ERROR_MAGIC; yield break; }
            while (!GetBits(8, out v)) yield return Bz.OK;
            blockSize100k = v;
            if (blockSize100k < (Bz.HDR_0 + 1) || blockSize100k > (Bz.HDR_0 + 9)) { yield return Bz.DATA_ERROR_MAGIC; yield break; }
            blockSize100k -= Bz.HDR_0;

            tt = CodecBufferPool.Rent<uint>(blockSize100k * 100000);

            while (true)
            {
                while (!GetBits(8, out uc)) yield return Bz.OK;      // BZ_X_BLKHDR_1
                if (uc == 0x17) break;                                // goto endhdr_2
                if (uc != 0x31) { yield return Bz.DATA_ERROR; yield break; }
                while (!GetBits(8, out uc)) yield return Bz.OK;
                if (uc != 0x41) { yield return Bz.DATA_ERROR; yield break; }
                while (!GetBits(8, out uc)) yield return Bz.OK;
                if (uc != 0x59) { yield return Bz.DATA_ERROR; yield break; }
                while (!GetBits(8, out uc)) yield return Bz.OK;
                if (uc != 0x26) { yield return Bz.DATA_ERROR; yield break; }
                while (!GetBits(8, out uc)) yield return Bz.OK;
                if (uc != 0x53) { yield return Bz.DATA_ERROR; yield break; }
                while (!GetBits(8, out uc)) yield return Bz.OK;
                if (uc != 0x59) { yield return Bz.DATA_ERROR; yield break; }

                currBlockNo++;

                storedBlockCRC = 0;
                for (int b = 0; b < 4; b++)
                {
                    while (!GetBits(8, out uc)) yield return Bz.OK;  // BZ_X_BCRC_1..4
                    storedBlockCRC = (storedBlockCRC << 8) | (uint)uc;
                }

                while (!GetBits(1, out v)) yield return Bz.OK;       // BZ_X_RANDBIT
                blockRandomised = v != 0;

                origPtr = 0;
                for (int b = 0; b < 3; b++)
                {
                    while (!GetBits(8, out uc)) yield return Bz.OK;  // BZ_X_ORIGPTR_1..3
                    origPtr = (origPtr << 8) | uc;
                }

                if (origPtr < 0) { yield return Bz.DATA_ERROR; yield break; }
                if (origPtr > 10 + 100000 * blockSize100k) { yield return Bz.DATA_ERROR; yield break; }

                // Receive the mapping table
                for (i = 0; i < 16; i++)
                {
                    while (!GetBits(1, out uc)) yield return Bz.OK;  // BZ_X_MAPPING_1
                    inUse16[i] = uc == 1;
                }

                for (i = 0; i < 256; i++) inUse[i] = false;

                for (i = 0; i < 16; i++)
                    if (inUse16[i])
                        for (j = 0; j < 16; j++)
                        {
                            while (!GetBits(1, out uc)) yield return Bz.OK;  // BZ_X_MAPPING_2
                            if (uc == 1) inUse[i * 16 + j] = true;
                        }
                MakeMapsD();
                if (nInUse == 0) { yield return Bz.DATA_ERROR; yield break; }
                alphaSize = nInUse + 2;

                // Now the selectors
                while (!GetBits(3, out nGroups)) yield return Bz.OK;      // BZ_X_SELECTOR_1
                if (nGroups < 2 || nGroups > Bz.N_GROUPS) { yield return Bz.DATA_ERROR; yield break; }
                while (!GetBits(15, out nSelectors)) yield return Bz.OK;  // BZ_X_SELECTOR_2
                if (nSelectors < 1) { yield return Bz.DATA_ERROR; yield break; }
                for (i = 0; i < nSelectors; i++)
                {
                    j = 0;
                    while (true)
                    {
                        while (!GetBits(1, out uc)) yield return Bz.OK;  // BZ_X_SELECTOR_3
                        if (uc == 0) break;
                        j++;
                        if (j >= nGroups) { yield return Bz.DATA_ERROR; yield break; }
                    }
                    // More than BZ_MAX_SELECTORS are never used; some implementations round up.
                    if (i < Bz.MAX_SELECTORS)
                        selectorMtf[i] = (byte)j;
                }
                if (nSelectors > Bz.MAX_SELECTORS)
                    nSelectors = Bz.MAX_SELECTORS;

                // Undo the MTF values for the selectors.
                {
                    Span<byte> pos = stackalloc byte[Bz.N_GROUPS];
                    byte tmp, vv;
                    for (vv = 0; vv < nGroups; vv++) pos[vv] = vv;

                    for (i = 0; i < nSelectors; i++)
                    {
                        vv = selectorMtf[i];
                        tmp = pos[vv];
                        while (vv > 0) { pos[vv] = pos[vv - 1]; vv--; }
                        pos[0] = tmp;
                        selector[i] = tmp;
                    }
                }

                // Now the coding tables
                for (t = 0; t < nGroups; t++)
                {
                    while (!GetBits(5, out curr)) yield return Bz.OK;  // BZ_X_CODING_1
                    for (i = 0; i < alphaSize; i++)
                    {
                        while (true)
                        {
                            if (curr < 1 || curr > 20) { yield return Bz.DATA_ERROR; yield break; }
                            while (!GetBits(1, out uc)) yield return Bz.OK;  // BZ_X_CODING_2
                            if (uc == 0) break;
                            while (!GetBits(1, out uc)) yield return Bz.OK;  // BZ_X_CODING_3
                            if (uc == 0) curr++; else curr--;
                        }
                        len[t][i] = (byte)curr;
                    }
                }

                // Create the Huffman decoding tables
                for (t = 0; t < nGroups; t++)
                {
                    minLen = 32;
                    maxLen = 0;
                    for (i = 0; i < alphaSize; i++)
                    {
                        if (len[t][i] > maxLen) maxLen = len[t][i];
                        if (len[t][i] < minLen) minLen = len[t][i];
                    }
                    HbCreateDecodeTables(limit[t], @base[t], perm[t], len[t], minLen, maxLen, alphaSize);
                    minLens[t] = minLen;
                }

                // Now the MTF values
                EOB = nInUse + 1;
                nblockMAX = 100000 * blockSize100k;
                groupNo = -1;
                groupPos = 0;

                for (i = 0; i <= 255; i++) unzftab[i] = 0;

                // MTF init
                {
                    int ii, jj, kk;
                    kk = MTFA_SIZE - 1;
                    for (ii = 256 / MTFL_SIZE - 1; ii >= 0; ii--)
                    {
                        for (jj = MTFL_SIZE - 1; jj >= 0; jj--)
                        {
                            mtfa[kk] = (byte)(ii * MTFL_SIZE + jj);
                            kk--;
                        }
                        mtfbase[ii] = kk + 1;
                    }
                }

                nblock = 0;
                gSel = 0; gMinlen = 0;

                // GET_MTF_VAL(BZ_X_MTF_1, BZ_X_MTF_2, nextSym), and the two below it
                bool first = true;
                es = 0; N = 0;
                while (true)
                {
                    // GET_MTF_VAL
                    if (groupPos == 0)
                    {
                        groupNo++;
                        if (groupNo >= nSelectors) { yield return Bz.DATA_ERROR; yield break; }
                        groupPos = Bz.G_SIZE;
                        gSel = selector[groupNo];
                        gMinlen = minLens[gSel];
                        gLimit = limit[gSel];
                        gPerm = perm[gSel];
                        gBase = @base[gSel];
                    }
                    groupPos--;
                    zn = gMinlen;
                    while (!GetBits(zn, out zvec)) yield return Bz.OK;
                    while (true)
                    {
                        if (zn > 20 /* the longest code */) { yield return Bz.DATA_ERROR; yield break; }
                        if (zvec <= gLimit[zn]) break;
                        zn++;
                        while (!GetBits(1, out zj)) yield return Bz.OK;
                        zvec = (zvec << 1) | zj;
                    }
                    if (zvec - gBase[zn] < 0 || zvec - gBase[zn] >= Bz.MAX_ALPHA_SIZE) { yield return Bz.DATA_ERROR; yield break; }
                    nextSym = gPerm[zvec - gBase[zn]];
                    // end GET_MTF_VAL

                    bool inRun = !first && N > 0;
                    first = false;

                    if (inRun)
                    {
                        // inside the RUNA/RUNB do-while: nextSym is the value after the last run symbol
                        if (nextSym == Bz.RUNA || nextSym == Bz.RUNB)
                        {
                            if (N >= 2 * 1024 * 1024) { yield return Bz.DATA_ERROR; yield break; }
                            if (nextSym == Bz.RUNA) es = es + (0 + 1) * N; else es = es + (1 + 1) * N;
                            N = N * 2;
                            continue;
                        }
                        es++;
                        uc = seqToUnseq[mtfa[mtfbase[0]]];
                        unzftab[uc] += es;
                        while (es > 0)
                        {
                            if (nblock >= nblockMAX) { yield return Bz.DATA_ERROR; yield break; }
                            tt[nblock] = (uint)uc;
                            nblock++;
                            es--;
                        }
                        N = 0;
                        // fall through to examine nextSym as a fresh symbol
                    }

                    if (nextSym == EOB) break;

                    if (nextSym == Bz.RUNA || nextSym == Bz.RUNB)
                    {
                        es = -1;
                        N = 1;
                        // Check that N doesn't get too big, so that es doesn't go negative.
                        if (N >= 2 * 1024 * 1024) { yield return Bz.DATA_ERROR; yield break; }
                        if (nextSym == Bz.RUNA) es = es + (0 + 1) * N; else es = es + (1 + 1) * N;
                        N = N * 2;
                        continue;
                    }

                    if (nblock >= nblockMAX) { yield return Bz.DATA_ERROR; yield break; }
                    // uc = MTF ( nextSym-1 )
                    {
                        int ii, jj, kk, pp, lno, off;
                        uint nn = (uint)(nextSym - 1);

                        if (nn < MTFL_SIZE)
                        {
                            // avoid general-case expense
                            pp = mtfbase[0];
                            uc = mtfa[pp + nn];
                            while (nn > 3)
                            {
                                int z = (int)(pp + nn);
                                mtfa[z] = mtfa[z - 1];
                                mtfa[z - 1] = mtfa[z - 2];
                                mtfa[z - 2] = mtfa[z - 3];
                                mtfa[z - 3] = mtfa[z - 4];
                                nn -= 4;
                            }
                            while (nn > 0)
                            {
                                mtfa[pp + nn] = mtfa[pp + nn - 1]; nn--;
                            }
                            mtfa[pp] = (byte)uc;
                        }
                        else
                        {
                            // general case
                            lno = (int)(nn / MTFL_SIZE);
                            off = (int)(nn % MTFL_SIZE);
                            pp = mtfbase[lno] + off;
                            uc = mtfa[pp];
                            while (pp > mtfbase[lno])
                            {
                                mtfa[pp] = mtfa[pp - 1]; pp--;
                            }
                            mtfbase[lno]++;
                            while (lno > 0)
                            {
                                mtfbase[lno]--;
                                mtfa[mtfbase[lno]] = mtfa[mtfbase[lno - 1] + MTFL_SIZE - 1];
                                lno--;
                            }
                            mtfbase[0]--;
                            mtfa[mtfbase[0]] = (byte)uc;
                            if (mtfbase[0] == 0)
                            {
                                kk = MTFA_SIZE - 1;
                                for (ii = 256 / MTFL_SIZE - 1; ii >= 0; ii--)
                                {
                                    for (jj = MTFL_SIZE - 1; jj >= 0; jj--)
                                    {
                                        mtfa[kk] = mtfa[mtfbase[ii] + jj];
                                        kk--;
                                    }
                                    mtfbase[ii] = kk + 1;
                                }
                            }
                        }
                    }
                    // end uc = MTF ( nextSym-1 )

                    unzftab[seqToUnseq[uc]]++;
                    tt[nblock] = seqToUnseq[uc];
                    nblock++;
                }

                // Now we know what nblock is, we can do a better sanity check on s->origPtr.
                if (origPtr < 0 || origPtr >= nblock) { yield return Bz.DATA_ERROR; yield break; }

                // Set up cftab to facilitate generation of T^(-1)
                // Check: unzftab entries in range.
                for (i = 0; i <= 255; i++)
                {
                    if (unzftab[i] < 0 || unzftab[i] > nblock) { yield return Bz.DATA_ERROR; yield break; }
                }
                // Actually generate cftab.
                cftab[0] = 0;
                for (i = 1; i <= 256; i++) cftab[i] = unzftab[i - 1];
                for (i = 1; i <= 256; i++) cftab[i] += cftab[i - 1];
                // Check: cftab entries in range.
                bool bad = false;
                for (i = 0; i <= 256; i++)
                    if (cftab[i] < 0 || cftab[i] > nblock) bad = true;
                // Check: cftab entries non-descending.
                for (i = 1; i <= 256; i++)
                    if (cftab[i - 1] > cftab[i]) bad = true;
                if (bad) { yield return Bz.DATA_ERROR; yield break; }

                state_out_len = 0;
                state_out_ch = 0;
                calculatedBlockCRC = 0xffffffff;
                state = X_OUTPUT;
                save_nblock = nblock;

                // compute the T^(-1) vector
                for (i = 0; i < nblock; i++)
                {
                    uc = (byte)(tt[i] & 0xff);
                    tt[cftab[uc]] |= (uint)(i << 8);
                    cftab[uc]++;
                }

                tPos = tt[origPtr] >> 8;
                nblock_used = 0;
                if (blockRandomised)
                {
                    rNToGo = 0; rTPos = 0;   // BZ_RAND_INIT_MASK
                    // BZ_GET_FAST(s->k0)
                    if (tPos >= (uint)(100000 * blockSize100k)) { yield return Bz.DATA_ERROR; yield break; }
                    tPos = tt[tPos]; k0 = (byte)(tPos & 0xff); tPos >>= 8;
                    nblock_used++;
                    k0 ^= RandMask();
                }
                else
                {
                    if (tPos >= (uint)(100000 * blockSize100k)) { yield return Bz.DATA_ERROR; yield break; }
                    tPos = tt[tPos]; k0 = (byte)(tPos & 0xff); tPos >>= 8;
                    nblock_used++;
                }

                yield return Bz.OK;
                // re-entered at BZ_X_BLKHDR_1 once the block has been written out
            }

            // endhdr_2:
            while (!GetBits(8, out uc)) yield return Bz.OK;
            if (uc != 0x72) { yield return Bz.DATA_ERROR; yield break; }
            while (!GetBits(8, out uc)) yield return Bz.OK;
            if (uc != 0x45) { yield return Bz.DATA_ERROR; yield break; }
            while (!GetBits(8, out uc)) yield return Bz.OK;
            if (uc != 0x38) { yield return Bz.DATA_ERROR; yield break; }
            while (!GetBits(8, out uc)) yield return Bz.OK;
            if (uc != 0x50) { yield return Bz.DATA_ERROR; yield break; }
            while (!GetBits(8, out uc)) yield return Bz.OK;
            if (uc != 0x90) { yield return Bz.DATA_ERROR; yield break; }

            storedCombinedCRC = 0;
            for (int b = 0; b < 4; b++)
            {
                while (!GetBits(8, out uc)) yield return Bz.OK;  // BZ_X_CCRC_1..4
                storedCombinedCRC = (storedCombinedCRC << 8) | (uint)uc;
            }

            state = X_IDLE;
            yield return Bz.STREAM_END;
        }

        /// <summary>BZ2_hbCreateDecodeTables (huffman.c)</summary>
        private static void HbCreateDecodeTables(int[] limit, int[] @base, int[] perm, byte[] length, int minLen, int maxLen, int alphaSize)
        {
            int pp, i, j, vec;

            pp = 0;
            for (i = minLen; i <= maxLen; i++)
                for (j = 0; j < alphaSize; j++)
                    if (length[j] == i) { perm[pp] = j; pp++; }

            for (i = 0; i < Bz.MAX_CODE_LEN; i++) @base[i] = 0;
            for (i = 0; i < alphaSize; i++) @base[length[i] + 1]++;

            for (i = 1; i < Bz.MAX_CODE_LEN; i++) @base[i] += @base[i - 1];

            for (i = 0; i < Bz.MAX_CODE_LEN; i++) limit[i] = 0;
            vec = 0;

            for (i = minLen; i <= maxLen; i++)
            {
                vec += (@base[i + 1] - @base[i]);
                limit[i] = vec - 1;
                vec <<= 1;
            }
            for (i = minLen + 1; i <= maxLen; i++)
                @base[i] = ((limit[i - 1] + 1) << 1) - @base[i];
        }
    }
}
