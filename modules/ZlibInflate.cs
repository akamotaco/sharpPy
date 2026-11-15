using System;

namespace SharpPy.Modules
{
    /// <summary>
    /// A z_stream (zlib.h): the input and output windows of one inflate() or deflate() call and what
    /// they say back. The buffers are arrays with an index where zlib has pointers.
    /// </summary>
    internal sealed class ZStream
    {
        internal byte[] InBuf = Array.Empty<byte>();
        internal int NextIn, AvailIn;
        internal byte[] OutBuf;
        internal int NextOut, AvailOut;
        internal long TotalIn, TotalOut;
        internal string Msg;
        internal uint Adler;
        internal ZInflater Inflate;
    }

    /// <summary>zlib.h's return codes and flush values.</summary>
    internal static class Z
    {
        internal const int OK = 0, STREAM_END = 1, NEED_DICT = 2, ERRNO = -1, STREAM_ERROR = -2,
            DATA_ERROR = -3, MEM_ERROR = -4, BUF_ERROR = -5, VERSION_ERROR = -6;
        internal const int NO_FLUSH = 0, PARTIAL_FLUSH = 1, SYNC_FLUSH = 2, FULL_FLUSH = 3, FINISH = 4,
            BLOCK = 5, TREES = 6;
        internal const int DEFLATED = 8;

        // adler32.c: adler32_z, with NMAX the most bytes before the sums must be reduced.
        internal static uint Adler32(uint adler, ReadOnlySpan<byte> data)
        {
            const uint Base = 65521;
            const int NMax = 5552;
            uint low = adler & 0xffff, high = adler >> 16;
            while (data.Length > 0)
            {
                int n = Math.Min(data.Length, NMax);
                for (int i = 0; i < n; i++)
                {
                    low += data[i];
                    high += low;
                }
                low %= Base;
                high %= Base;
                data = data.Slice(n);
            }
            return (high << 16) | low;
        }
    }

    // inftrees.h: one entry of a decoding table - op (literal, table link, length or distance
    // with its extra bits, end of block, invalid), the bits it uses, and the value.
    internal struct ZCode
    {
        internal byte Op, Bits;
        internal ushort Val;
        internal ZCode(byte op, byte bits, ushort val) { Op = op; Bits = bits; Val = val; }
    }

    /// <summary>
    /// zlib 1.3.1's inflate (inflate.c, inftrees.c): the state machine that takes any amount of
    /// input and output in each call and resumes where it stopped. inflate_fast, the same decoding
    /// for long stretches of input and output, is not taken: every symbol goes through LEN.
    /// </summary>
    internal sealed class ZInflater
    {
        private enum Mode
        {
            HEAD = 16180, FLAGS, TIME, OS, EXLEN, EXTRA, NAME, COMMENT, HCRC, DICTID, DICT,
            TYPE, TYPEDO, STORED, COPY_, COPY, TABLE, LENLENS, CODELENS,
            LEN_, LEN, LENEXT, DIST, DISTEXT, MATCH, LIT,
            CHECK, LENGTH, DONE, BAD, MEM, SYNC,
        }

        private const int ENOUGH_LENS = 852, ENOUGH_DISTS = 592, ENOUGH = ENOUGH_LENS + ENOUGH_DISTS;
        private const int CODES = 0, LENS = 1, DISTS = 2;
        private const int MAXBITS = 15;

        private Mode _mode;
        private bool _last;
        private int _wrap;
        private bool _havedict;
        private int _flags;
        private uint _dmax;
        private uint _check;
        private uint _total;
        // sliding window
        private int _wbits;
        private int _wsize, _whave, _wnext;
        private byte[] _window;
        // bit accumulator
        private ulong _hold;
        private int _bits;
        // string and stored block copying
        private uint _length, _offset;
        private int _extra;
        // the decoding tables: each an array and the index of its root table
        private ZCode[] _lencode, _distcode;
        private int _lenoff, _distoff;
        private int _lenbits, _distbits;
        // dynamic table building
        private int _ncode, _nlen, _ndist, _have;
        private int _next;
        private ushort[] _lens = new ushort[320];
        private ushort[] _work = new ushort[288];
        private ZCode[] _codes = new ZCode[ENOUGH];
        private bool _sane;
        private int _back;
        private uint _was;

        // Built by the static constructor, after the base and extra tables below are set.
        private static readonly ZCode[] FixedTable;
        private static readonly int FixedDistOffset;

        static ZInflater()
        {
            FixedTable = BuildFixed(out FixedDistOffset);
        }

        private static readonly ushort[] Order = { 16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15 };

        // fixedtables, as BUILDFIXED builds them: the literal/length table first, then the distances.
        private static ZCode[] BuildFixed(out int distOffset)
        {
            var lens = new ushort[320];
            var work = new ushort[288];
            var table = new ZCode[544];
            int sym = 0;
            while (sym < 144) lens[sym++] = 8;
            while (sym < 256) lens[sym++] = 9;
            while (sym < 280) lens[sym++] = 7;
            while (sym < 288) lens[sym++] = 8;
            int next = 0, bits = 9;
            InflateTable(LENS, lens, 0, 288, table, ref next, ref bits, work);
            sym = 0;
            while (sym < 32) lens[sym++] = 5;
            distOffset = next;
            bits = 5;
            InflateTable(DISTS, lens, 0, 32, table, ref next, ref bits, work);
            return table;
        }

        // inflateStateCheck
        private static bool StateCheck(ZStream strm) => strm?.Inflate == null;

        internal static int ResetKeep(ZStream strm)
        {
            if (StateCheck(strm)) return Z.STREAM_ERROR;
            var state = strm.Inflate;
            strm.TotalIn = strm.TotalOut = state._total = 0;
            strm.Msg = null;
            if (state._wrap != 0)
                strm.Adler = (uint)(state._wrap & 1);
            state._mode = Mode.HEAD;
            state._last = false;
            state._havedict = false;
            state._flags = -1;
            state._dmax = 32768U;
            state._hold = 0;
            state._bits = 0;
            state._lencode = state._distcode = state._codes;
            state._lenoff = state._distoff = state._next = 0;
            state._sane = true;
            state._back = -1;
            return Z.OK;
        }

        internal static int Reset(ZStream strm)
        {
            if (StateCheck(strm)) return Z.STREAM_ERROR;
            var state = strm.Inflate;
            state._wsize = 0;
            state._whave = 0;
            state._wnext = 0;
            return ResetKeep(strm);
        }

        internal static int Reset2(ZStream strm, int windowBits)
        {
            if (StateCheck(strm)) return Z.STREAM_ERROR;
            var state = strm.Inflate;
            int wrap;
            if (windowBits < 0)
            {
                if (windowBits < -15)
                    return Z.STREAM_ERROR;
                wrap = 0;
                windowBits = -windowBits;
            }
            else
            {
                wrap = (windowBits >> 4) + 5;
                if (windowBits < 48)
                    windowBits &= 15;
            }
            if (windowBits != 0 && (windowBits < 8 || windowBits > 15))
                return Z.STREAM_ERROR;
            if (state._window != null && state._wbits != windowBits)
                state._window = null;
            state._wrap = wrap;
            state._wbits = windowBits;
            return Reset(strm);
        }

        // inflateInit2_
        internal static int Init2(ZStream strm, int windowBits)
        {
            strm.Msg = null;
            var state = new ZInflater { _window = null, _mode = Mode.HEAD };
            strm.Inflate = state;
            int ret = Reset2(strm, windowBits);
            if (ret != Z.OK)
                strm.Inflate = null;
            return ret;
        }

        internal static int End(ZStream strm)
        {
            if (StateCheck(strm)) return Z.STREAM_ERROR;
            strm.Inflate = null;
            return Z.OK;
        }

        // updatewindow: the last wsize bytes written, end being one past the last of them in src.
        private void UpdateWindow(byte[] src, int end, int copy)
        {
            _window ??= new byte[1 << _wbits];
            if (_wsize == 0)
            {
                _wsize = 1 << _wbits;
                _wnext = 0;
                _whave = 0;
            }
            if (copy >= _wsize)
            {
                Array.Copy(src, end - _wsize, _window, 0, _wsize);
                _wnext = 0;
                _whave = _wsize;
            }
            else
            {
                int dist = _wsize - _wnext;
                if (dist > copy) dist = copy;
                Array.Copy(src, end - copy, _window, _wnext, dist);
                copy -= dist;
                if (copy != 0)
                {
                    Array.Copy(src, end - copy, _window, 0, copy);
                    _wnext = copy;
                    _whave = _wsize;
                }
                else
                {
                    _wnext += dist;
                    if (_wnext == _wsize) _wnext = 0;
                    if (_whave < _wsize) _whave += dist;
                }
            }
        }

        private uint UpdateCheck(uint check, byte[] buf, int start, int len) =>
            _flags != 0 ? PyBinascii.Crc32(buf.AsSpan(start, len), check) : Z.Adler32(check, buf.AsSpan(start, len));

        private static uint Crc2(uint check, ulong word) =>
            PyBinascii.Crc32(stackalloc byte[] { (byte)word, (byte)(word >> 8) }, check);

        private static uint Crc4(uint check, ulong word) =>
            PyBinascii.Crc32(stackalloc byte[] { (byte)word, (byte)(word >> 8), (byte)(word >> 16), (byte)(word >> 24) }, check);

        private static uint Swap32(ulong q) =>
            (uint)(((q >> 24) & 0xff) + ((q >> 8) & 0xff00) + ((q & 0xff00) << 8) + ((q & 0xff) << 24));

        /// <summary>inflate(strm, flush).</summary>
        internal static int Run(ZStream strm, int flush)
        {
            if (StateCheck(strm) || strm.OutBuf == null)
                return Z.STREAM_ERROR;
            var state = strm.Inflate;
            if (state._mode == Mode.TYPE) state._mode = Mode.TYPEDO;
            // LOAD()
            byte[] input = strm.InBuf, output = strm.OutBuf;
            int put = strm.NextOut, left = strm.AvailOut, next = strm.NextIn, have = strm.AvailIn;
            ulong hold = state._hold;
            int bits = state._bits;
            int @in = have, @out = left;
            int ret = Z.OK;
            int copy, len, from;
            byte[] fromBuf;
            ZCode here, last;

            for (;;)
            {
                switch (state._mode)
                {
                    case Mode.HEAD:
                        if (state._wrap == 0)
                        {
                            state._mode = Mode.TYPEDO;
                            break;
                        }
                        while (bits < 16) { if (have == 0) goto inf_leave; have--; hold += (ulong)input[next++] << bits; bits += 8; }
                        if ((state._wrap & 2) != 0 && hold == 0x8b1f)
                        {
                            if (state._wbits == 0)
                                state._wbits = 15;
                            state._check = PyBinascii.Crc32(ReadOnlySpan<byte>.Empty, 0);
                            state._check = Crc2(state._check, hold);
                            hold = 0; bits = 0;
                            state._mode = Mode.FLAGS;
                            break;
                        }
                        if ((state._wrap & 1) == 0 || ((((uint)hold & 0xff) << 8) + (uint)(hold >> 8)) % 31 != 0)
                        {
                            strm.Msg = "incorrect header check";
                            state._mode = Mode.BAD;
                            break;
                        }
                        if (((uint)hold & 0xf) != Z.DEFLATED)
                        {
                            strm.Msg = "unknown compression method";
                            state._mode = Mode.BAD;
                            break;
                        }
                        hold >>= 4; bits -= 4;
                        len = (int)((uint)hold & 0xf) + 8;
                        if (state._wbits == 0)
                            state._wbits = len;
                        if (len > 15 || len > state._wbits)
                        {
                            strm.Msg = "invalid window size";
                            state._mode = Mode.BAD;
                            break;
                        }
                        state._dmax = 1U << len;
                        state._flags = 0;
                        strm.Adler = state._check = Z.Adler32(1, ReadOnlySpan<byte>.Empty);
                        state._mode = (hold & 0x200) != 0 ? Mode.DICTID : Mode.TYPE;
                        hold = 0; bits = 0;
                        break;
                    case Mode.FLAGS:
                        while (bits < 16) { if (have == 0) goto inf_leave; have--; hold += (ulong)input[next++] << bits; bits += 8; }
                        state._flags = (int)hold;
                        if ((state._flags & 0xff) != Z.DEFLATED)
                        {
                            strm.Msg = "unknown compression method";
                            state._mode = Mode.BAD;
                            break;
                        }
                        if ((state._flags & 0xe000) != 0)
                        {
                            strm.Msg = "unknown header flags set";
                            state._mode = Mode.BAD;
                            break;
                        }
                        if ((state._flags & 0x0200) != 0 && (state._wrap & 4) != 0)
                            state._check = Crc2(state._check, hold);
                        hold = 0; bits = 0;
                        state._mode = Mode.TIME;
                        goto case Mode.TIME;
                    case Mode.TIME:
                        while (bits < 32) { if (have == 0) goto inf_leave; have--; hold += (ulong)input[next++] << bits; bits += 8; }
                        if ((state._flags & 0x0200) != 0 && (state._wrap & 4) != 0)
                            state._check = Crc4(state._check, hold);
                        hold = 0; bits = 0;
                        state._mode = Mode.OS;
                        goto case Mode.OS;
                    case Mode.OS:
                        while (bits < 16) { if (have == 0) goto inf_leave; have--; hold += (ulong)input[next++] << bits; bits += 8; }
                        if ((state._flags & 0x0200) != 0 && (state._wrap & 4) != 0)
                            state._check = Crc2(state._check, hold);
                        hold = 0; bits = 0;
                        state._mode = Mode.EXLEN;
                        goto case Mode.EXLEN;
                    case Mode.EXLEN:
                        if ((state._flags & 0x0400) != 0)
                        {
                            while (bits < 16) { if (have == 0) goto inf_leave; have--; hold += (ulong)input[next++] << bits; bits += 8; }
                            state._length = (uint)hold;
                            if ((state._flags & 0x0200) != 0 && (state._wrap & 4) != 0)
                                state._check = Crc2(state._check, hold);
                            hold = 0; bits = 0;
                        }
                        state._mode = Mode.EXTRA;
                        goto case Mode.EXTRA;
                    case Mode.EXTRA:
                        if ((state._flags & 0x0400) != 0)
                        {
                            copy = (int)Math.Min(state._length, (uint)have);
                            if (copy != 0)
                            {
                                if ((state._flags & 0x0200) != 0 && (state._wrap & 4) != 0)
                                    state._check = PyBinascii.Crc32(input.AsSpan(next, copy), state._check);
                                have -= copy;
                                next += copy;
                                state._length -= (uint)copy;
                            }
                            if (state._length != 0) goto inf_leave;
                        }
                        state._length = 0;
                        state._mode = Mode.NAME;
                        goto case Mode.NAME;
                    case Mode.NAME:
                        if ((state._flags & 0x0800) != 0)
                        {
                            if (have == 0) goto inf_leave;
                            copy = 0;
                            do
                            {
                                len = input[next + copy++];
                            } while (len != 0 && copy < have);
                            if ((state._flags & 0x0200) != 0 && (state._wrap & 4) != 0)
                                state._check = PyBinascii.Crc32(input.AsSpan(next, copy), state._check);
                            have -= copy;
                            next += copy;
                            if (len != 0) goto inf_leave;
                        }
                        state._length = 0;
                        state._mode = Mode.COMMENT;
                        goto case Mode.COMMENT;
                    case Mode.COMMENT:
                        if ((state._flags & 0x1000) != 0)
                        {
                            if (have == 0) goto inf_leave;
                            copy = 0;
                            do
                            {
                                len = input[next + copy++];
                            } while (len != 0 && copy < have);
                            if ((state._flags & 0x0200) != 0 && (state._wrap & 4) != 0)
                                state._check = PyBinascii.Crc32(input.AsSpan(next, copy), state._check);
                            have -= copy;
                            next += copy;
                            if (len != 0) goto inf_leave;
                        }
                        state._mode = Mode.HCRC;
                        goto case Mode.HCRC;
                    case Mode.HCRC:
                        if ((state._flags & 0x0200) != 0)
                        {
                            while (bits < 16) { if (have == 0) goto inf_leave; have--; hold += (ulong)input[next++] << bits; bits += 8; }
                            if ((state._wrap & 4) != 0 && hold != (state._check & 0xffff))
                            {
                                strm.Msg = "header crc mismatch";
                                state._mode = Mode.BAD;
                                break;
                            }
                            hold = 0; bits = 0;
                        }
                        strm.Adler = state._check = 0;
                        state._mode = Mode.TYPE;
                        break;
                    case Mode.DICTID:
                        while (bits < 32) { if (have == 0) goto inf_leave; have--; hold += (ulong)input[next++] << bits; bits += 8; }
                        strm.Adler = state._check = Swap32(hold);
                        hold = 0; bits = 0;
                        state._mode = Mode.DICT;
                        goto case Mode.DICT;
                    case Mode.DICT:
                        if (!state._havedict)
                        {
                            // RESTORE()
                            strm.NextOut = put; strm.AvailOut = left; strm.NextIn = next; strm.AvailIn = have;
                            state._hold = hold; state._bits = bits;
                            return Z.NEED_DICT;
                        }
                        strm.Adler = state._check = 1;
                        state._mode = Mode.TYPE;
                        goto case Mode.TYPE;
                    case Mode.TYPE:
                        if (flush == Z.BLOCK || flush == Z.TREES) goto inf_leave;
                        goto case Mode.TYPEDO;
                    case Mode.TYPEDO:
                        if (state._last)
                        {
                            hold >>= bits & 7; bits -= bits & 7;
                            state._mode = Mode.CHECK;
                            break;
                        }
                        while (bits < 3) { if (have == 0) goto inf_leave; have--; hold += (ulong)input[next++] << bits; bits += 8; }
                        state._last = ((uint)hold & 1) != 0;
                        hold >>= 1; bits -= 1;
                        switch ((uint)hold & 3)
                        {
                            case 0:
                                state._mode = Mode.STORED;
                                break;
                            case 1:
                                state._lencode = FixedTable;
                                state._lenoff = 0;
                                state._lenbits = 9;
                                state._distcode = FixedTable;
                                state._distoff = FixedDistOffset;
                                state._distbits = 5;
                                state._mode = Mode.LEN_;
                                if (flush == Z.TREES)
                                {
                                    hold >>= 2; bits -= 2;
                                    goto inf_leave;
                                }
                                break;
                            case 2:
                                state._mode = Mode.TABLE;
                                break;
                            case 3:
                                strm.Msg = "invalid block type";
                                state._mode = Mode.BAD;
                                break;
                        }
                        hold >>= 2; bits -= 2;
                        break;
                    case Mode.STORED:
                        hold >>= bits & 7; bits -= bits & 7;
                        while (bits < 32) { if (have == 0) goto inf_leave; have--; hold += (ulong)input[next++] << bits; bits += 8; }
                        if ((hold & 0xffff) != ((hold >> 16) ^ 0xffff))
                        {
                            strm.Msg = "invalid stored block lengths";
                            state._mode = Mode.BAD;
                            break;
                        }
                        state._length = (uint)hold & 0xffff;
                        hold = 0; bits = 0;
                        state._mode = Mode.COPY_;
                        if (flush == Z.TREES) goto inf_leave;
                        goto case Mode.COPY_;
                    case Mode.COPY_:
                        state._mode = Mode.COPY;
                        goto case Mode.COPY;
                    case Mode.COPY:
                        copy = (int)state._length;
                        if (copy != 0)
                        {
                            if (copy > have) copy = have;
                            if (copy > left) copy = left;
                            if (copy == 0) goto inf_leave;
                            Array.Copy(input, next, output, put, copy);
                            have -= copy;
                            next += copy;
                            left -= copy;
                            put += copy;
                            state._length -= (uint)copy;
                            break;
                        }
                        state._mode = Mode.TYPE;
                        break;
                    case Mode.TABLE:
                        while (bits < 14) { if (have == 0) goto inf_leave; have--; hold += (ulong)input[next++] << bits; bits += 8; }
                        state._nlen = (int)((uint)hold & 0x1f) + 257;
                        hold >>= 5; bits -= 5;
                        state._ndist = (int)((uint)hold & 0x1f) + 1;
                        hold >>= 5; bits -= 5;
                        state._ncode = (int)((uint)hold & 0xf) + 4;
                        hold >>= 4; bits -= 4;
                        if (state._nlen > 286 || state._ndist > 30)
                        {
                            strm.Msg = "too many length or distance symbols";
                            state._mode = Mode.BAD;
                            break;
                        }
                        state._have = 0;
                        state._mode = Mode.LENLENS;
                        goto case Mode.LENLENS;
                    case Mode.LENLENS:
                        while (state._have < state._ncode)
                        {
                            while (bits < 3) { if (have == 0) goto inf_leave; have--; hold += (ulong)input[next++] << bits; bits += 8; }
                            state._lens[Order[state._have++]] = (ushort)((uint)hold & 7);
                            hold >>= 3; bits -= 3;
                        }
                        while (state._have < 19)
                            state._lens[Order[state._have++]] = 0;
                        state._next = 0;
                        state._lencode = state._codes;
                        state._lenoff = 0;
                        state._lenbits = 7;
                        ret = InflateTable(CODES, state._lens, 0, 19, state._codes, ref state._next, ref state._lenbits, state._work);
                        if (ret != 0)
                        {
                            strm.Msg = "invalid code lengths set";
                            state._mode = Mode.BAD;
                            break;
                        }
                        state._have = 0;
                        state._mode = Mode.CODELENS;
                        goto case Mode.CODELENS;
                    case Mode.CODELENS:
                        while (state._have < state._nlen + state._ndist)
                        {
                            for (;;)
                            {
                                here = state._lencode[state._lenoff + (int)((uint)hold & ((1U << state._lenbits) - 1))];
                                if (here.Bits <= bits) break;
                                if (have == 0) goto inf_leave; have--; hold += (ulong)input[next++] << bits; bits += 8;
                            }
                            if (here.Val < 16)
                            {
                                hold >>= here.Bits; bits -= here.Bits;
                                state._lens[state._have++] = here.Val;
                            }
                            else
                            {
                                if (here.Val == 16)
                                {
                                    while (bits < here.Bits + 2) { if (have == 0) goto inf_leave; have--; hold += (ulong)input[next++] << bits; bits += 8; }
                                    hold >>= here.Bits; bits -= here.Bits;
                                    if (state._have == 0)
                                    {
                                        strm.Msg = "invalid bit length repeat";
                                        state._mode = Mode.BAD;
                                        break;
                                    }
                                    len = state._lens[state._have - 1];
                                    copy = 3 + (int)((uint)hold & 3);
                                    hold >>= 2; bits -= 2;
                                }
                                else if (here.Val == 17)
                                {
                                    while (bits < here.Bits + 3) { if (have == 0) goto inf_leave; have--; hold += (ulong)input[next++] << bits; bits += 8; }
                                    hold >>= here.Bits; bits -= here.Bits;
                                    len = 0;
                                    copy = 3 + (int)((uint)hold & 7);
                                    hold >>= 3; bits -= 3;
                                }
                                else
                                {
                                    while (bits < here.Bits + 7) { if (have == 0) goto inf_leave; have--; hold += (ulong)input[next++] << bits; bits += 8; }
                                    hold >>= here.Bits; bits -= here.Bits;
                                    len = 0;
                                    copy = 11 + (int)((uint)hold & 0x7f);
                                    hold >>= 7; bits -= 7;
                                }
                                if (state._have + copy > state._nlen + state._ndist)
                                {
                                    strm.Msg = "invalid bit length repeat";
                                    state._mode = Mode.BAD;
                                    break;
                                }
                                while (copy-- != 0)
                                    state._lens[state._have++] = (ushort)len;
                            }
                        }
                        if (state._mode == Mode.BAD) break;
                        if (state._lens[256] == 0)
                        {
                            strm.Msg = "invalid code -- missing end-of-block";
                            state._mode = Mode.BAD;
                            break;
                        }
                        state._next = 0;
                        state._lencode = state._codes;
                        state._lenoff = 0;
                        state._lenbits = 9;
                        ret = InflateTable(LENS, state._lens, 0, state._nlen, state._codes, ref state._next, ref state._lenbits, state._work);
                        if (ret != 0)
                        {
                            strm.Msg = "invalid literal/lengths set";
                            state._mode = Mode.BAD;
                            break;
                        }
                        state._distcode = state._codes;
                        state._distoff = state._next;
                        state._distbits = 6;
                        ret = InflateTable(DISTS, state._lens, state._nlen, state._ndist, state._codes, ref state._next, ref state._distbits, state._work);
                        if (ret != 0)
                        {
                            strm.Msg = "invalid distances set";
                            state._mode = Mode.BAD;
                            break;
                        }
                        state._mode = Mode.LEN_;
                        if (flush == Z.TREES) goto inf_leave;
                        goto case Mode.LEN_;
                    case Mode.LEN_:
                        state._mode = Mode.LEN;
                        goto case Mode.LEN;
                    case Mode.LEN:
                        state._back = 0;
                        for (;;)
                        {
                            here = state._lencode[state._lenoff + (int)((uint)hold & ((1U << state._lenbits) - 1))];
                            if (here.Bits <= bits) break;
                            if (have == 0) goto inf_leave; have--; hold += (ulong)input[next++] << bits; bits += 8;
                        }
                        if (here.Op != 0 && (here.Op & 0xf0) == 0)
                        {
                            last = here;
                            for (;;)
                            {
                                here = state._lencode[state._lenoff + last.Val +
                                    (int)(((uint)hold & ((1U << (last.Bits + last.Op)) - 1)) >> last.Bits)];
                                if (last.Bits + here.Bits <= bits) break;
                                if (have == 0) goto inf_leave; have--; hold += (ulong)input[next++] << bits; bits += 8;
                            }
                            hold >>= last.Bits; bits -= last.Bits;
                            state._back += last.Bits;
                        }
                        hold >>= here.Bits; bits -= here.Bits;
                        state._back += here.Bits;
                        state._length = here.Val;
                        if (here.Op == 0)
                        {
                            state._mode = Mode.LIT;
                            break;
                        }
                        if ((here.Op & 32) != 0)
                        {
                            state._back = -1;
                            state._mode = Mode.TYPE;
                            break;
                        }
                        if ((here.Op & 64) != 0)
                        {
                            strm.Msg = "invalid literal/length code";
                            state._mode = Mode.BAD;
                            break;
                        }
                        state._extra = here.Op & 15;
                        state._mode = Mode.LENEXT;
                        goto case Mode.LENEXT;
                    case Mode.LENEXT:
                        if (state._extra != 0)
                        {
                            while (bits < state._extra) { if (have == 0) goto inf_leave; have--; hold += (ulong)input[next++] << bits; bits += 8; }
                            state._length += (uint)hold & ((1U << state._extra) - 1);
                            hold >>= state._extra; bits -= state._extra;
                            state._back += state._extra;
                        }
                        state._was = state._length;
                        state._mode = Mode.DIST;
                        goto case Mode.DIST;
                    case Mode.DIST:
                        for (;;)
                        {
                            here = state._distcode[state._distoff + (int)((uint)hold & ((1U << state._distbits) - 1))];
                            if (here.Bits <= bits) break;
                            if (have == 0) goto inf_leave; have--; hold += (ulong)input[next++] << bits; bits += 8;
                        }
                        if ((here.Op & 0xf0) == 0)
                        {
                            last = here;
                            for (;;)
                            {
                                here = state._distcode[state._distoff + last.Val +
                                    (int)(((uint)hold & ((1U << (last.Bits + last.Op)) - 1)) >> last.Bits)];
                                if (last.Bits + here.Bits <= bits) break;
                                if (have == 0) goto inf_leave; have--; hold += (ulong)input[next++] << bits; bits += 8;
                            }
                            hold >>= last.Bits; bits -= last.Bits;
                            state._back += last.Bits;
                        }
                        hold >>= here.Bits; bits -= here.Bits;
                        state._back += here.Bits;
                        if ((here.Op & 64) != 0)
                        {
                            strm.Msg = "invalid distance code";
                            state._mode = Mode.BAD;
                            break;
                        }
                        state._offset = here.Val;
                        state._extra = here.Op & 15;
                        state._mode = Mode.DISTEXT;
                        goto case Mode.DISTEXT;
                    case Mode.DISTEXT:
                        if (state._extra != 0)
                        {
                            while (bits < state._extra) { if (have == 0) goto inf_leave; have--; hold += (ulong)input[next++] << bits; bits += 8; }
                            state._offset += (uint)hold & ((1U << state._extra) - 1);
                            hold >>= state._extra; bits -= state._extra;
                            state._back += state._extra;
                        }
                        state._mode = Mode.MATCH;
                        goto case Mode.MATCH;
                    case Mode.MATCH:
                        if (left == 0) goto inf_leave;
                        copy = @out - left;
                        if (state._offset > copy)
                        {
                            // copy from window
                            copy = (int)state._offset - copy;
                            if (copy > state._whave)
                            {
                                if (state._sane)
                                {
                                    strm.Msg = "invalid distance too far back";
                                    state._mode = Mode.BAD;
                                    break;
                                }
                            }
                            fromBuf = state._window;
                            if (copy > state._wnext)
                            {
                                copy -= state._wnext;
                                from = state._wsize - copy;
                            }
                            else
                                from = state._wnext - copy;
                            if (copy > state._length) copy = (int)state._length;
                        }
                        else
                        {
                            // copy from output
                            fromBuf = output;
                            from = put - (int)state._offset;
                            copy = (int)state._length;
                        }
                        if (copy > left) copy = left;
                        left -= copy;
                        state._length -= (uint)copy;
                        do
                        {
                            output[put++] = fromBuf[from++];
                        } while (--copy != 0);
                        if (state._length == 0) state._mode = Mode.LEN;
                        break;
                    case Mode.LIT:
                        if (left == 0) goto inf_leave;
                        output[put++] = (byte)state._length;
                        left--;
                        state._mode = Mode.LEN;
                        break;
                    case Mode.CHECK:
                        if (state._wrap != 0)
                        {
                            while (bits < 32) { if (have == 0) goto inf_leave; have--; hold += (ulong)input[next++] << bits; bits += 8; }
                            @out -= left;
                            strm.TotalOut += @out;
                            state._total += (uint)@out;
                            if ((state._wrap & 4) != 0 && @out != 0)
                                strm.Adler = state._check = state.UpdateCheck(state._check, output, put - @out, @out);
                            @out = left;
                            if ((state._wrap & 4) != 0 && (state._flags != 0 ? (uint)hold : Swap32(hold)) != state._check)
                            {
                                strm.Msg = "incorrect data check";
                                state._mode = Mode.BAD;
                                break;
                            }
                            hold = 0; bits = 0;
                        }
                        state._mode = Mode.LENGTH;
                        goto case Mode.LENGTH;
                    case Mode.LENGTH:
                        if (state._wrap != 0 && state._flags != 0)
                        {
                            while (bits < 32) { if (have == 0) goto inf_leave; have--; hold += (ulong)input[next++] << bits; bits += 8; }
                            if ((state._wrap & 4) != 0 && hold != state._total)
                            {
                                strm.Msg = "incorrect length check";
                                state._mode = Mode.BAD;
                                break;
                            }
                            hold = 0; bits = 0;
                        }
                        state._mode = Mode.DONE;
                        goto case Mode.DONE;
                    case Mode.DONE:
                        ret = Z.STREAM_END;
                        goto inf_leave;
                    case Mode.BAD:
                        ret = Z.DATA_ERROR;
                        goto inf_leave;
                    case Mode.MEM:
                        return Z.MEM_ERROR;
                    default:
                        return Z.STREAM_ERROR;
                }
            }

        inf_leave:
            // RESTORE()
            strm.NextOut = put; strm.AvailOut = left; strm.NextIn = next; strm.AvailIn = have;
            state._hold = hold; state._bits = bits;
            if (state._wsize != 0 || (@out != strm.AvailOut && state._mode < Mode.BAD &&
                    (state._mode < Mode.CHECK || flush != Z.FINISH)))
                state.UpdateWindow(output, strm.NextOut, @out - strm.AvailOut);
            @in -= strm.AvailIn;
            @out -= strm.AvailOut;
            strm.TotalIn += @in;
            strm.TotalOut += @out;
            state._total += (uint)@out;
            if ((state._wrap & 4) != 0 && @out != 0)
                strm.Adler = state._check = state.UpdateCheck(state._check, output, strm.NextOut - @out, @out);
            if (((@in == 0 && @out == 0) || flush == Z.FINISH) && ret == Z.OK)
                ret = Z.BUF_ERROR;
            return ret;
        }

        // inflateSetDictionary
        internal static int SetDictionary(ZStream strm, byte[] dictionary)
        {
            if (StateCheck(strm)) return Z.STREAM_ERROR;
            var state = strm.Inflate;
            if (state._wrap != 0 && state._mode != Mode.DICT)
                return Z.STREAM_ERROR;
            if (state._mode == Mode.DICT && Z.Adler32(1, dictionary) != state._check)
                return Z.DATA_ERROR;
            state.UpdateWindow(dictionary, dictionary.Length, dictionary.Length);
            state._havedict = true;
            return Z.OK;
        }

        // inflateCopy: the state and window, the tables pointing into the copy's own codes.
        internal static int Copy(ZStream dest, ZStream source)
        {
            if (StateCheck(source) || dest == null)
                return Z.STREAM_ERROR;
            var state = source.Inflate;
            var copy = (ZInflater)state.MemberwiseClone();
            copy._lens = (ushort[])state._lens.Clone();
            copy._work = (ushort[])state._work.Clone();
            copy._codes = (ZCode[])state._codes.Clone();
            if (ReferenceEquals(state._lencode, state._codes)) copy._lencode = copy._codes;
            if (ReferenceEquals(state._distcode, state._codes)) copy._distcode = copy._codes;
            if (state._window != null) copy._window = (byte[])state._window.Clone();
            dest.InBuf = source.InBuf; dest.NextIn = source.NextIn; dest.AvailIn = source.AvailIn;
            dest.OutBuf = source.OutBuf; dest.NextOut = source.NextOut; dest.AvailOut = source.AvailOut;
            dest.TotalIn = source.TotalIn; dest.TotalOut = source.TotalOut;
            dest.Msg = source.Msg; dest.Adler = source.Adler;
            dest.Inflate = copy;
            return Z.OK;
        }

        private static readonly ushort[] LBase = { 3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31,
            35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258, 0, 0 };
        private static readonly ushort[] LExt = { 16, 16, 16, 16, 16, 16, 16, 16, 17, 17, 17, 17, 18, 18, 18, 18,
            19, 19, 19, 19, 20, 20, 20, 20, 21, 21, 21, 21, 16, 203, 77 };
        private static readonly ushort[] DBase = { 1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193,
            257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577, 0, 0 };
        private static readonly ushort[] DExt = { 16, 16, 16, 16, 17, 17, 18, 18, 19, 19, 20, 20, 21, 21, 22, 22,
            23, 23, 24, 24, 25, 25, 26, 26, 27, 27, 28, 28, 29, 29, 64, 64 };

        /// <summary>
        /// inflate_table: the decoding tables of the canonical Huffman code whose lengths are
        /// lens[lensOff..lensOff+codes-1], written at table[next..], next moved past them. 0 is
        /// success, -1 an invalid code, 1 not enough room.
        /// </summary>
        private static int InflateTable(int type, ushort[] lens, int lensOff, int codes, ZCode[] table, ref int next,
                                        ref int bits, ushort[] work)
        {
            int len, sym, min, max, root, curr, drop, left;
            uint used, huff, incr, fill, low, mask;
            ZCode here;
            ushort[] @base, extra;
            int match;
            var count = new ushort[MAXBITS + 1];
            var offs = new ushort[MAXBITS + 1];
            int tableStart = next;

            for (len = 0; len <= MAXBITS; len++)
                count[len] = 0;
            for (sym = 0; sym < codes; sym++)
                count[lens[lensOff + sym]]++;

            root = bits;
            for (max = MAXBITS; max >= 1; max--)
                if (count[max] != 0) break;
            if (root > max) root = max;
            if (max == 0)
            {
                here = new ZCode(64, 1, 0);
                table[next++] = here;
                table[next++] = here;
                bits = 1;
                return 0;
            }
            for (min = 1; min < max; min++)
                if (count[min] != 0) break;
            if (root < min) root = min;

            left = 1;
            for (len = 1; len <= MAXBITS; len++)
            {
                left <<= 1;
                left -= count[len];
                if (left < 0) return -1;
            }
            if (left > 0 && (type == CODES || max != 1))
                return -1;

            offs[1] = 0;
            for (len = 1; len < MAXBITS; len++)
                offs[len + 1] = (ushort)(offs[len] + count[len]);
            for (sym = 0; sym < codes; sym++)
                if (lens[lensOff + sym] != 0) work[offs[lens[lensOff + sym]]++] = (ushort)sym;

            switch (type)
            {
                case CODES:
                    @base = extra = work;
                    match = 20;
                    break;
                case LENS:
                    @base = LBase;
                    extra = LExt;
                    match = 257;
                    break;
                default:
                    @base = DBase;
                    extra = DExt;
                    match = 0;
                    break;
            }

            huff = 0;
            sym = 0;
            len = min;
            int nextTable = tableStart;
            curr = root;
            drop = 0;
            low = uint.MaxValue;
            used = 1U << root;
            mask = used - 1;

            if ((type == LENS && used > ENOUGH_LENS) || (type == DISTS && used > ENOUGH_DISTS))
                return 1;

            for (;;)
            {
                here.Bits = (byte)(len - drop);
                if (work[sym] + 1U < (uint)match)
                {
                    here.Op = 0;
                    here.Val = work[sym];
                }
                else if (work[sym] >= match)
                {
                    here.Op = (byte)extra[work[sym] - match];
                    here.Val = @base[work[sym] - match];
                }
                else
                {
                    here.Op = 32 + 64;
                    here.Val = 0;
                }

                incr = 1U << (len - drop);
                fill = 1U << curr;
                min = (int)fill;
                do
                {
                    fill -= incr;
                    table[nextTable + (int)((huff >> drop) + fill)] = here;
                } while (fill != 0);

                incr = 1U << (len - 1);
                while ((huff & incr) != 0)
                    incr >>= 1;
                if (incr != 0)
                {
                    huff &= incr - 1;
                    huff += incr;
                }
                else
                    huff = 0;

                sym++;
                if (--count[len] == 0)
                {
                    if (len == max) break;
                    len = lens[lensOff + work[sym]];
                }

                if (len > root && (huff & mask) != low)
                {
                    if (drop == 0)
                        drop = root;
                    nextTable += min;
                    curr = len - drop;
                    left = 1 << curr;
                    while (curr + drop < max)
                    {
                        left -= count[curr + drop];
                        if (left <= 0) break;
                        curr++;
                        left <<= 1;
                    }
                    used += 1U << curr;
                    if ((type == LENS && used > ENOUGH_LENS) || (type == DISTS && used > ENOUGH_DISTS))
                        return 1;
                    low = huff & mask;
                    table[tableStart + (int)low] = new ZCode((byte)curr, (byte)root, (ushort)(nextTable - tableStart));
                }
            }

            if (huff != 0)
                table[nextTable + (int)huff] = new ZCode(64, (byte)(len - drop), 0);

            next = tableStart + (int)used;
            bits = root;
            return 0;
        }
    }
}
