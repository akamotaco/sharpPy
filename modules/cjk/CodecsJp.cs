using static SharpPy.Modules.Cjk.Trymap;

namespace SharpPy.Modules.Cjk
{
    /// <summary>
    /// Modules/cjkcodecs/_codecs_jp.c: shift_jis, cp932, euc_jp, shift_jis_2004, euc_jis_2004 and
    /// the JIS X 0213:2000 variants euc_jisx0213 and shift_jisx0213 (config 2000), over
    /// mappings_jp.h and mappings_jisx0213_pair.h; alg_jisx0201.h and emu_jisx0213_2000.h.
    /// </summary>
    internal static class CodecsJp
    {
        private const int EMPBASE = 0x20000;
        private const ushort NOCHAR = CjkTables.NOCHAR, MULTIC = CjkTables.MULTIC, DBCINV = CjkTables.DBCINV;

        private static CjkIndex<ushort> _jisx0208Dec, _jisx0212Dec, _jisxcommonEnc, _jisx0213_1BmpDec, _jisx0213_2BmpDec,
            _jisx0213BmpEnc, _jisx0213_1EmpDec, _jisx0213_2EmpDec, _jisx0213EmpEnc, _cp932extEnc, _cp932extDec;
        private static CjkIndex<uint> _pairDec;
        private static CjkPair[] _pairEnc;

        internal static PyModule Create()
        {
            var tables = CjkTables.Of("jp");
            _jisx0208Dec = tables.Map("jisx0208_decmap");
            _jisx0212Dec = tables.Map("jisx0212_decmap");
            _jisxcommonEnc = tables.Map("jisxcommon_encmap");
            _jisx0213_1BmpDec = tables.Map("jisx0213_1_bmp_decmap");
            _jisx0213_2BmpDec = tables.Map("jisx0213_2_bmp_decmap");
            _jisx0213BmpEnc = tables.Map("jisx0213_bmp_encmap");
            _jisx0213_1EmpDec = tables.Map("jisx0213_1_emp_decmap");
            _jisx0213_2EmpDec = tables.Map("jisx0213_2_emp_decmap");
            _jisx0213EmpEnc = tables.Map("jisx0213_emp_encmap");
            _pairDec = tables.WideMap("jisx0213_pair_decmap");
            _pairEnc = tables.Get<CjkPair[]>("jisx0213_pair_encmap");
            _cp932extEnc = tables.Map("cp932ext_encmap");
            _cp932extDec = tables.Map("cp932ext_decmap");
            var state = new CjkModule("jp");
            state.Mappings.Add(new DbcsMap("jisx0208", null, _jisx0208Dec));
            state.Mappings.Add(new DbcsMap("jisx0212", null, _jisx0212Dec));
            state.Mappings.Add(new DbcsMap("jisxcommon", _jisxcommonEnc, null));
            state.Mappings.Add(new DbcsMap("jisx0213_1_bmp", null, _jisx0213_1BmpDec));
            state.Mappings.Add(new DbcsMap("jisx0213_2_bmp", null, _jisx0213_2BmpDec));
            state.Mappings.Add(new DbcsMap("jisx0213_bmp", _jisx0213BmpEnc, null));
            state.Mappings.Add(new DbcsMap("jisx0213_1_emp", null, _jisx0213_1EmpDec));
            state.Mappings.Add(new DbcsMap("jisx0213_2_emp", null, _jisx0213_2EmpDec));
            state.Mappings.Add(new DbcsMap("jisx0213_emp", _jisx0213EmpEnc, null));
            state.Mappings.Add(new DbcsMap("jisx0213_pair", _pairEnc, _pairDec));
            state.Mappings.Add(new DbcsMap("cp932ext", _cp932extEnc, _cp932extDec));
            state.Codecs.Add(new ShiftJis { Encoding = "shift_jis" });
            state.Codecs.Add(new Cp932 { Encoding = "cp932" });
            state.Codecs.Add(new EucJp { Encoding = "euc_jp" });
            state.Codecs.Add(new ShiftJis2004 { Encoding = "shift_jis_2004" });
            state.Codecs.Add(new EucJis2004 { Encoding = "euc_jis_2004" });
            state.Codecs.Add(new EucJis2004 { Encoding = "euc_jisx0213", Config = 2000 });
            state.Codecs.Add(new ShiftJis2004 { Encoding = "shift_jisx0213", Config = 2000 });
            return state.Create();
        }

        // emu_jisx0213_2000.h: EMULATE_JISX0213_2000_ENCODE_BMP's first test, the code points JIS X
        // 0213:2000 does not have.
        internal static bool Emu2000EncodeBmpInvalid(int config, int c) =>
            config == 2000 && (c == 0x9B1C || c == 0x4FF1 || c == 0x525D || c == 0x541E || c == 0x5653 ||
                               c == 0x59F8 || c == 0x5C5B || c == 0x5E77 || c == 0x7626 || c == 0x7E6B);

        // EMULATE_JISX0213_2000_DECODE_PLANE1's test
        internal static bool Emu2000DecodePlane1Invalid(int config, int c1, int c2) =>
            config == 2000 && ((c1 == 0x2E && c2 == 0x21) || (c1 == 0x2F && c2 == 0x7E) || (c1 == 0x4F && c2 == 0x54) ||
                               (c1 == 0x4F && c2 == 0x7E) || (c1 == 0x74 && c2 == 0x27) || (c1 == 0x7E && c2 == 0x7A) ||
                               (c1 == 0x7E && c2 == 0x7B) || (c1 == 0x7E && c2 == 0x7C) || (c1 == 0x7E && c2 == 0x7D) ||
                               (c1 == 0x7E && c2 == 0x7E));

        // The pair encodings of euc_jis_2004 and shift_jis_2004 for a MULTIC code point.
        private static long PairCode(int[] data, long inpos, long inlen, int flags, int c, ref ushort code, ref int insize)
        {
            if (inlen - inpos < 2)
            {
                if ((flags & MbCodec.MBENC_FLUSH) != 0)
                {
                    code = FindPairEncMap(c, 0, _pairEnc);
                    if (code == DBCINV)
                        return 1;
                }
                else
                    return MbCodec.MBERR_TOOFEW;
            }
            else
            {
                int c2 = data[inpos + 1];
                code = FindPairEncMap(c, c2, _pairEnc);
                if (code == DBCINV)
                {
                    code = FindPairEncMap(c, 0, _pairEnc);
                    if (code == DBCINV)
                        return 1;
                }
                else
                    insize = 2;
            }
            return 0;
        }

        private sealed class Cp932 : MbCodec
        {
            internal override long Encode(MbState state, int[] data, ref long inpos, long inlen, MbOut o, long outleft, int flags)
            {
                var b = o.Buf;
                while (inpos < inlen)
                {
                    int c = data[inpos];
                    if (c <= 0x80)
                    {
                        if (outleft < 1) return MBERR_TOOSMALL;
                        b[o.Pos] = (byte)c;
                        inpos += 1; o.Pos += 1; outleft -= 1;
                        continue;
                    }
                    else if (c >= 0xff61 && c <= 0xff9f)
                    {
                        if (outleft < 1) return MBERR_TOOSMALL;
                        b[o.Pos] = (byte)(c - 0xfec0);
                        inpos += 1; o.Pos += 1; outleft -= 1;
                        continue;
                    }
                    else if (c >= 0xf8f0 && c <= 0xf8f3)
                    {
                        // Windows compatibility
                        if (outleft < 1) return MBERR_TOOSMALL;
                        b[o.Pos] = c == 0xf8f0 ? (byte)0xa0 : (byte)(c - 0xf8f1 + 0xfd);
                        inpos += 1; o.Pos += 1; outleft -= 1;
                        continue;
                    }
                    if (c > 0xFFFF)
                        return 1;
                    if (outleft < 2) return MBERR_TOOSMALL;
                    if (TryEnc(_cp932extEnc, c, out ushort code))
                    {
                        b[o.Pos] = (byte)(code >> 8);
                        b[o.Pos + 1] = (byte)(code & 0xff);
                    }
                    else if (TryEnc(_jisxcommonEnc, c, out code))
                    {
                        if ((code & 0x8000) != 0)
                            return 1;
                        // JIS X 0208
                        int c1 = code >> 8, c2 = code & 0xff;
                        c2 = (byte)((((c1 - 0x21) & 1) != 0 ? 0x5e : 0) + (c2 - 0x21));
                        c1 = (byte)((c1 - 0x21) >> 1);
                        b[o.Pos] = (byte)(c1 < 0x1f ? c1 + 0x81 : c1 + 0xc1);
                        b[o.Pos + 1] = (byte)(c2 < 0x3f ? c2 + 0x40 : c2 + 0x41);
                    }
                    else if (c >= 0xe000 && c < 0xe758)
                    {
                        // User-defined area
                        int c1 = (byte)((c - 0xe000) / 188), c2 = (byte)((c - 0xe000) % 188);
                        b[o.Pos] = (byte)(c1 + 0xf0);
                        b[o.Pos + 1] = (byte)(c2 < 0x3f ? c2 + 0x40 : c2 + 0x41);
                    }
                    else
                        return 1;
                    inpos += 1; o.Pos += 2; outleft -= 2;
                }
                return 0;
            }

            internal override long Decode(MbState state, MbIn i, long inleft, MbWriter w)
            {
                var b = i.Buf;
                while (inleft > 0)
                {
                    int c = b[i.Pos];
                    if (c <= 0x80)
                    {
                        w.Write(c);
                        i.Pos += 1; inleft -= 1;
                        continue;
                    }
                    else if (c >= 0xa0 && c <= 0xdf)
                    {
                        w.Write(c == 0xa0 ? 0xf8f0 : 0xfec0 + c);
                        i.Pos += 1; inleft -= 1;
                        continue;
                    }
                    else if (c >= 0xfd)
                    {
                        // Windows compatibility
                        w.Write(0xf8f1 - 0xfd + c);
                        i.Pos += 1; inleft -= 1;
                        continue;
                    }
                    if (inleft < 2) return MBERR_TOOFEW;
                    int c2 = b[i.Pos + 1];
                    if (TryDec(_cp932extDec, c, c2, out ushort decoded))
                        w.Write(decoded);
                    else if ((c >= 0x81 && c <= 0x9f) || (c >= 0xe0 && c <= 0xea))
                    {
                        if (c2 < 0x40 || (c2 > 0x7e && c2 < 0x80) || c2 > 0xfc)
                            return 1;
                        c = (byte)(c < 0xe0 ? c - 0x81 : c - 0xc1);
                        c2 = (byte)(c2 < 0x80 ? c2 - 0x40 : c2 - 0x41);
                        c = (byte)(2 * c + (c2 < 0x5e ? 0 : 1) + 0x21);
                        c2 = (byte)((c2 < 0x5e ? c2 : c2 - 0x5e) + 0x21);
                        if (TryDec(_jisx0208Dec, c, c2, out decoded))
                            w.Write(decoded);
                        else
                            return 1;
                    }
                    else if (c >= 0xf0 && c <= 0xf9)
                    {
                        if ((c2 >= 0x40 && c2 <= 0x7e) || (c2 >= 0x80 && c2 <= 0xfc))
                            w.Write(0xe000 + 188 * (c - 0xf0) + (c2 < 0x80 ? c2 - 0x40 : c2 - 0x41));
                        else
                            return 1;
                    }
                    else
                        return 1;
                    i.Pos += 2; inleft -= 2;
                }
                return 0;
            }
        }

        private sealed class EucJis2004 : MbCodec
        {
            internal int Config;

            internal override long Encode(MbState state, int[] data, ref long inpos, long inlen, MbOut o, long outleft, int flags)
            {
                var b = o.Buf;
                while (inpos < inlen)
                {
                    int c = data[inpos];
                    ushort code = 0;
                    int insize;
                    if (c < 0x80)
                    {
                        if (outleft < 1) return MBERR_TOOSMALL;
                        b[o.Pos] = (byte)c;
                        inpos += 1; o.Pos += 1; outleft -= 1;
                        continue;
                    }
                    insize = 1;
                    if (c <= 0xFFFF)
                    {
                        if (Emu2000EncodeBmpInvalid(Config, c))
                            return 1;
                        else if (Config == 2000 && c == 0x9B1D)
                            code = 0x8000 | 0x7d3b;
                        else if (TryEnc(_jisx0213BmpEnc, c, out code))
                        {
                            if (code == MULTIC)
                            {
                                long r = PairCode(data, inpos, inlen, flags, c, ref code, ref insize);
                                if (r != 0)
                                    return r;
                            }
                        }
                        else if (TryEnc(_jisxcommonEnc, c, out code))
                        {
                        }
                        else if (c >= 0xff61 && c <= 0xff9f)
                        {
                            // JIS X 0201 half-width katakana
                            if (outleft < 2) return MBERR_TOOSMALL;
                            b[o.Pos] = 0x8e;
                            b[o.Pos + 1] = (byte)(c - 0xfec0);
                            inpos += 1; o.Pos += 2; outleft -= 2;
                            continue;
                        }
                        else if (c == 0xff3c)
                            code = 0x2140; // F/W REVERSE SOLIDUS
                        else if (c == 0xff5e)
                            code = 0x2232; // F/W TILDE
                        else
                            return 1;
                    }
                    else if (c >> 16 == EMPBASE >> 16)
                    {
                        if (Config == 2000 && c == 0x20B9F)
                            return 1;
                        else if (!TryEnc(_jisx0213EmpEnc, c & 0xffff, out code))
                            return insize;
                    }
                    else
                        return insize;
                    if ((code & 0x8000) != 0)
                    {
                        // Codeset 2
                        if (outleft < 3) return MBERR_TOOSMALL;
                        b[o.Pos] = 0x8f;
                        b[o.Pos + 1] = (byte)(code >> 8);
                        b[o.Pos + 2] = (byte)((code & 0xFF) | 0x80);
                        inpos += insize; o.Pos += 3; outleft -= 3;
                    }
                    else
                    {
                        // Codeset 1
                        if (outleft < 2) return MBERR_TOOSMALL;
                        b[o.Pos] = (byte)((code >> 8) | 0x80);
                        b[o.Pos + 1] = (byte)((code & 0xFF) | 0x80);
                        inpos += insize; o.Pos += 2; outleft -= 2;
                    }
                }
                return 0;
            }

            internal override long Decode(MbState state, MbIn i, long inleft, MbWriter w)
            {
                var b = i.Buf;
                while (inleft > 0)
                {
                    int c = b[i.Pos];
                    if (c < 0x80)
                    {
                        w.Write(c);
                        i.Pos += 1; inleft -= 1;
                        continue;
                    }
                    if (c == 0x8e)
                    {
                        // JIS X 0201 half-width katakana
                        if (inleft < 2) return MBERR_TOOFEW;
                        int c2 = b[i.Pos + 1];
                        if (c2 >= 0xa1 && c2 <= 0xdf)
                        {
                            w.Write(0xfec0 + c2);
                            i.Pos += 2; inleft -= 2;
                        }
                        else
                            return 1;
                    }
                    else if (c == 0x8f)
                    {
                        if (inleft < 3) return MBERR_TOOFEW;
                        int c2 = b[i.Pos + 1] ^ 0x80, c3 = b[i.Pos + 2] ^ 0x80;
                        // JIS X 0213 Plane 2 or JIS X 0212
                        if (Config == 2000 && c2 == 0x7D && c3 == 0x3B)
                            w.Write(0x9B1D);
                        else if (TryDec(_jisx0213_2BmpDec, c2, c3, out ushort decoded))
                            w.Write(decoded);
                        else if (TryDec(_jisx0213_2EmpDec, c2, c3, out ushort code))
                        {
                            w.Write(EMPBASE | code);
                            i.Pos += 3; inleft -= 3;
                            continue;
                        }
                        else if (TryDec(_jisx0212Dec, c2, c3, out decoded))
                            w.Write(decoded);
                        else
                            return 1;
                        i.Pos += 3; inleft -= 3;
                    }
                    else
                    {
                        if (inleft < 2) return MBERR_TOOFEW;
                        c ^= 0x80;
                        int c2 = b[i.Pos + 1] ^ 0x80;
                        // JIS X 0213 Plane 1
                        if (Emu2000DecodePlane1Invalid(Config, c, c2))
                            return 2;
                        else if (c == 0x21 && c2 == 0x40)
                            w.Write(0xff3c);
                        else if (c == 0x22 && c2 == 0x32)
                            w.Write(0xff5e);
                        else if (TryDec(_jisx0208Dec, c, c2, out ushort decoded))
                            w.Write(decoded);
                        else if (TryDec(_jisx0213_1BmpDec, c, c2, out decoded))
                            w.Write(decoded);
                        else if (TryDec(_jisx0213_1EmpDec, c, c2, out ushort code))
                        {
                            w.Write(EMPBASE | code);
                            i.Pos += 2; inleft -= 2;
                            continue;
                        }
                        else if (TryDecWide(_pairDec, c, c2, out uint pair))
                        {
                            w.Write((int)(pair >> 16));
                            w.Write((int)(pair & 0xffff));
                            i.Pos += 2; inleft -= 2;
                            continue;
                        }
                        else
                            return 1;
                        i.Pos += 2; inleft -= 2;
                    }
                }
                return 0;
            }
        }

        private sealed class EucJp : MbCodec
        {
            internal override long Encode(MbState state, int[] data, ref long inpos, long inlen, MbOut o, long outleft, int flags)
            {
                var b = o.Buf;
                while (inpos < inlen)
                {
                    int c = data[inpos];
                    if (c < 0x80)
                    {
                        if (outleft < 1) return MBERR_TOOSMALL;
                        b[o.Pos] = (byte)c;
                        inpos += 1; o.Pos += 1; outleft -= 1;
                        continue;
                    }
                    if (c > 0xFFFF)
                        return 1;
                    ushort code;
                    if (TryEnc(_jisxcommonEnc, c, out code))
                    {
                    }
                    else if (c >= 0xff61 && c <= 0xff9f)
                    {
                        // JIS X 0201 half-width katakana
                        if (outleft < 2) return MBERR_TOOSMALL;
                        b[o.Pos] = 0x8e;
                        b[o.Pos + 1] = (byte)(c - 0xfec0);
                        inpos += 1; o.Pos += 2; outleft -= 2;
                        continue;
                    }
                    else if (c == 0xff3c)
                        code = 0x2140; // FULL-WIDTH REVERSE SOLIDUS
                    else if (c == 0xa5)
                    {
                        // YEN SIGN
                        if (outleft < 1) return MBERR_TOOSMALL;
                        b[o.Pos] = 0x5c;
                        inpos += 1; o.Pos += 1; outleft -= 1;
                        continue;
                    }
                    else if (c == 0x203e)
                    {
                        // OVERLINE
                        if (outleft < 1) return MBERR_TOOSMALL;
                        b[o.Pos] = 0x7e;
                        inpos += 1; o.Pos += 1; outleft -= 1;
                        continue;
                    }
                    else
                        return 1;
                    if ((code & 0x8000) != 0)
                    {
                        // JIS X 0212
                        if (outleft < 3) return MBERR_TOOSMALL;
                        b[o.Pos] = 0x8f;
                        b[o.Pos + 1] = (byte)(code >> 8);
                        b[o.Pos + 2] = (byte)((code & 0xFF) | 0x80);
                        inpos += 1; o.Pos += 3; outleft -= 3;
                    }
                    else
                    {
                        // JIS X 0208
                        if (outleft < 2) return MBERR_TOOSMALL;
                        b[o.Pos] = (byte)((code >> 8) | 0x80);
                        b[o.Pos + 1] = (byte)((code & 0xFF) | 0x80);
                        inpos += 1; o.Pos += 2; outleft -= 2;
                    }
                }
                return 0;
            }

            internal override long Decode(MbState state, MbIn i, long inleft, MbWriter w)
            {
                var b = i.Buf;
                while (inleft > 0)
                {
                    int c = b[i.Pos];
                    if (c < 0x80)
                    {
                        w.Write(c);
                        i.Pos += 1; inleft -= 1;
                        continue;
                    }
                    if (c == 0x8e)
                    {
                        if (inleft < 2) return MBERR_TOOFEW;
                        int c2 = b[i.Pos + 1];
                        if (c2 >= 0xa1 && c2 <= 0xdf)
                        {
                            w.Write(0xfec0 + c2);
                            i.Pos += 2; inleft -= 2;
                        }
                        else
                            return 1;
                    }
                    else if (c == 0x8f)
                    {
                        if (inleft < 3) return MBERR_TOOFEW;
                        int c2 = b[i.Pos + 1], c3 = b[i.Pos + 2];
                        // JIS X 0212
                        if (TryDec(_jisx0212Dec, c2 ^ 0x80, c3 ^ 0x80, out ushort decoded))
                        {
                            w.Write(decoded);
                            i.Pos += 3; inleft -= 3;
                        }
                        else
                            return 1;
                    }
                    else
                    {
                        if (inleft < 2) return MBERR_TOOFEW;
                        int c2 = b[i.Pos + 1];
                        // JIS X 0208
                        if (c == 0xa1 && c2 == 0xc0)
                            w.Write(0xff3c); // FULL-WIDTH REVERSE SOLIDUS
                        else if (TryDec(_jisx0208Dec, c ^ 0x80, c2 ^ 0x80, out ushort decoded))
                            w.Write(decoded);
                        else
                            return 1;
                        i.Pos += 2; inleft -= 2;
                    }
                }
                return 0;
            }
        }

        private sealed class ShiftJis : MbCodec
        {
            internal override long Encode(MbState state, int[] data, ref long inpos, long inlen, MbOut o, long outleft, int flags)
            {
                var b = o.Buf;
                while (inpos < inlen)
                {
                    int c = data[inpos];
                    ushort code;
                    if (c < 0x80)
                        code = (ushort)c;
                    else if (c == 0x00a5)
                        code = 0x5c; // YEN SIGN
                    else if (c == 0x203e)
                        code = 0x7e; // OVERLINE
                    else if (c >= 0xff61 && c <= 0xff9f)
                        code = (ushort)(c - 0xfec0);
                    else if (c > 0xFFFF)
                        return 1;
                    else
                        code = NOCHAR;
                    if (code < 0x80 || (code >= 0xa1 && code <= 0xdf))
                    {
                        if (outleft < 1) return MBERR_TOOSMALL;
                        b[o.Pos] = (byte)code;
                        inpos += 1; o.Pos += 1; outleft -= 1;
                        continue;
                    }
                    if (outleft < 2) return MBERR_TOOSMALL;
                    if (code == NOCHAR)
                    {
                        if (TryEnc(_jisxcommonEnc, c, out code))
                        {
                        }
                        else if (c == 0xff3c)
                            code = 0x2140; // FULL-WIDTH REVERSE SOLIDUS
                        else
                            return 1;
                        if ((code & 0x8000) != 0)
                            return 1;
                    }
                    int c1 = code >> 8, c2 = code & 0xff;
                    c2 = (byte)((((c1 - 0x21) & 1) != 0 ? 0x5e : 0) + (c2 - 0x21));
                    c1 = (byte)((c1 - 0x21) >> 1);
                    b[o.Pos] = (byte)(c1 < 0x1f ? c1 + 0x81 : c1 + 0xc1);
                    b[o.Pos + 1] = (byte)(c2 < 0x3f ? c2 + 0x40 : c2 + 0x41);
                    inpos += 1; o.Pos += 2; outleft -= 2;
                }
                return 0;
            }

            internal override long Decode(MbState state, MbIn i, long inleft, MbWriter w)
            {
                var b = i.Buf;
                while (inleft > 0)
                {
                    int c = b[i.Pos];
                    if (c < 0x80)
                        w.Write(c);
                    else if (c >= 0xa1 && c <= 0xdf)
                        w.Write(0xfec0 + c);
                    else if ((c >= 0x81 && c <= 0x9f) || (c >= 0xe0 && c <= 0xea))
                    {
                        if (inleft < 2) return MBERR_TOOFEW;
                        int c2 = b[i.Pos + 1];
                        if (c2 < 0x40 || (c2 > 0x7e && c2 < 0x80) || c2 > 0xfc)
                            return 1;
                        int c1 = (byte)(c < 0xe0 ? c - 0x81 : c - 0xc1);
                        c2 = (byte)(c2 < 0x80 ? c2 - 0x40 : c2 - 0x41);
                        c1 = (byte)(2 * c1 + (c2 < 0x5e ? 0 : 1) + 0x21);
                        c2 = (byte)((c2 < 0x5e ? c2 : c2 - 0x5e) + 0x21);
                        if (c1 == 0x21 && c2 == 0x40)
                        {
                            // FULL-WIDTH REVERSE SOLIDUS
                            w.Write(0xff3c);
                            i.Pos += 2; inleft -= 2;
                            continue;
                        }
                        if (TryDec(_jisx0208Dec, c1, c2, out ushort decoded))
                        {
                            w.Write(decoded);
                            i.Pos += 2; inleft -= 2;
                            continue;
                        }
                        return 1;
                    }
                    else
                        return 1;
                    i.Pos += 1; inleft -= 1; // JIS X 0201
                }
                return 0;
            }
        }

        private sealed class ShiftJis2004 : MbCodec
        {
            internal int Config;

            internal override long Encode(MbState state, int[] data, ref long inpos, long inlen, MbOut o, long outleft, int flags)
            {
                var b = o.Buf;
                while (inpos < inlen)
                {
                    int c = data[inpos];
                    ushort code = NOCHAR;
                    int insize;
                    // JISX0201_ENCODE
                    if (c < 0x80 && c != 0x5c && c != 0x7e)
                        code = (ushort)c;
                    else if (c == 0x00a5)
                        code = 0x5c;
                    else if (c == 0x203e)
                        code = 0x7e;
                    else if (c >= 0xff61 && c <= 0xff9f)
                        code = (ushort)(c - 0xfec0);
                    if (code < 0x80 || (code >= 0xa1 && code <= 0xdf))
                    {
                        if (outleft < 1) return MBERR_TOOSMALL;
                        b[o.Pos] = (byte)code;
                        inpos += 1; o.Pos += 1; outleft -= 1;
                        continue;
                    }
                    if (outleft < 2) return MBERR_TOOSMALL;
                    insize = 1;
                    if (code == NOCHAR)
                    {
                        if (c <= 0xffff)
                        {
                            if (Emu2000EncodeBmpInvalid(Config, c))
                                return 1;
                            else if (Config == 2000 && c == 0x9B1D)
                                code = 0x8000 | 0x7d3b;
                            else if (TryEnc(_jisx0213BmpEnc, c, out code))
                            {
                                if (code == MULTIC)
                                {
                                    long r = PairCode(data, inpos, inlen, flags, c, ref code, ref insize);
                                    if (r != 0)
                                        return r;
                                }
                            }
                            else if (TryEnc(_jisxcommonEnc, c, out code))
                            {
                                // abandon JIS X 0212 codes
                                if ((code & 0x8000) != 0)
                                    return 1;
                            }
                            else
                                return 1;
                        }
                        else if (c >> 16 == EMPBASE >> 16)
                        {
                            if (Config == 2000 && c == 0x20B9F)
                                return 1;
                            else if (!TryEnc(_jisx0213EmpEnc, c & 0xffff, out code))
                                return insize;
                        }
                        else
                            return insize;
                    }
                    int c1 = code >> 8;
                    int c2 = (code & 0xff) - 0x21;
                    if ((c1 & 0x80) != 0)
                    {
                        // Plane 2
                        if (c1 >= 0xee)
                            c1 -= 0x87;
                        else if (c1 >= 0xac || c1 == 0xa8)
                            c1 -= 0x49;
                        else
                            c1 -= 0x43;
                    }
                    else
                        c1 -= 0x21; // Plane 1
                    if ((c1 & 1) != 0)
                        c2 += 0x5e;
                    c1 >>= 1;
                    b[o.Pos] = (byte)(c1 + (c1 < 0x1f ? 0x81 : 0xc1));
                    b[o.Pos + 1] = (byte)(c2 + (c2 < 0x3f ? 0x40 : 0x41));
                    inpos += insize; o.Pos += 2; outleft -= 2;
                }
                return 0;
            }

            internal override long Decode(MbState state, MbIn i, long inleft, MbWriter w)
            {
                var b = i.Buf;
                while (inleft > 0)
                {
                    int c = b[i.Pos];
                    // JISX0201_DECODE
                    if (c < 0x5c)
                        w.Write(c);
                    else if (c == 0x5c)
                        w.Write(0x00a5);
                    else if (c < 0x7e)
                        w.Write(c);
                    else if (c == 0x7e)
                        w.Write(0x203e);
                    else if (c == 0x7f)
                        w.Write(0x7f);
                    else if (c >= 0xa1 && c <= 0xdf)
                        w.Write(0xfec0 + c);
                    else if ((c >= 0x81 && c <= 0x9f) || (c >= 0xe0 && c <= 0xfc))
                    {
                        if (inleft < 2) return MBERR_TOOFEW;
                        int c2 = b[i.Pos + 1];
                        if (c2 < 0x40 || (c2 > 0x7e && c2 < 0x80) || c2 > 0xfc)
                            return 1;
                        int c1 = (byte)(c < 0xe0 ? c - 0x81 : c - 0xc1);
                        c2 = (byte)(c2 < 0x80 ? c2 - 0x40 : c2 - 0x41);
                        c1 = (byte)(2 * c1 + (c2 < 0x5e ? 0 : 1));
                        c2 = (byte)((c2 < 0x5e ? c2 : c2 - 0x5e) + 0x21);
                        if (c1 < 0x5e)
                        {
                            // Plane 1
                            c1 += 0x21;
                            if (Emu2000DecodePlane1Invalid(Config, c1, c2))
                                return 2;
                            else if (TryDec(_jisx0208Dec, c1, c2, out ushort decoded))
                                w.Write(decoded);
                            else if (TryDec(_jisx0213_1BmpDec, c1, c2, out decoded))
                                w.Write(decoded);
                            else if (TryDec(_jisx0213_1EmpDec, c1, c2, out ushort code))
                                w.Write(EMPBASE | code);
                            else if (TryDecWide(_pairDec, c1, c2, out uint pair))
                            {
                                w.Write((int)(pair >> 16));
                                w.Write((int)(pair & 0xffff));
                            }
                            else
                                return 1;
                            i.Pos += 2; inleft -= 2;
                        }
                        else
                        {
                            // Plane 2
                            if (c1 >= 0x67)
                                c1 += 0x07;
                            else if (c1 >= 0x63 || c1 == 0x5f)
                                c1 -= 0x37;
                            else
                                c1 -= 0x3d;
                            if (Config == 2000 && c1 == 0x7D && c2 == 0x3B)
                                w.Write(0x9B1D);
                            else if (TryDec(_jisx0213_2BmpDec, c1, c2, out ushort decoded))
                                w.Write(decoded);
                            else if (TryDec(_jisx0213_2EmpDec, c1, c2, out ushort code))
                            {
                                w.Write(EMPBASE | code);
                                i.Pos += 2; inleft -= 2;
                                continue;
                            }
                            else
                                return 1;
                            i.Pos += 2; inleft -= 2;
                        }
                        continue;
                    }
                    else
                        return 1;
                    i.Pos += 1; inleft -= 1; // JIS X 0201
                }
                return 0;
            }
        }
    }
}
