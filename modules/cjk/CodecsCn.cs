using static SharpPy.Modules.Cjk.Trymap;

namespace SharpPy.Modules.Cjk
{
    /// <summary>
    /// Modules/cjkcodecs/_codecs_cn.c: gb2312, gbk, gb18030 and hz over mappings_cn.h's gb2312,
    /// gbkext, gbcommon, gb18030ext and gb18030's ranges.
    /// </summary>
    internal static class CodecsCn
    {
        private const int CN_STATE_OFFSET = 0;
        private static CjkIndex<ushort> _gb2312Dec, _gbkextDec, _gbcommonEnc, _gb18030extEnc, _gb18030extDec;
        private static Gb18030Range[] _ranges;

        internal static PyModule Create()
        {
            var tables = CjkTables.Of("cn");
            _gb2312Dec = tables.Map("gb2312_decmap");
            _gbkextDec = tables.Map("gbkext_decmap");
            _gbcommonEnc = tables.Map("gbcommon_encmap");
            _gb18030extEnc = tables.Map("gb18030ext_encmap");
            _gb18030extDec = tables.Map("gb18030ext_decmap");
            _ranges = tables.Get<Gb18030Range[]>("gb18030_to_unibmp_ranges");
            var state = new CjkModule("cn");
            state.Mappings.Add(new DbcsMap("gb2312", null, _gb2312Dec));
            state.Mappings.Add(new DbcsMap("gbkext", null, _gbkextDec));
            state.Mappings.Add(new DbcsMap("gbcommon", _gbcommonEnc, null));
            state.Mappings.Add(new DbcsMap("gb18030ext", _gb18030extEnc, _gb18030extDec));
            state.Codecs.Add(new Gb2312 { Encoding = "gb2312" });
            state.Codecs.Add(new Gbk { Encoding = "gbk" });
            state.Codecs.Add(new Gb18030 { Encoding = "gb18030" });
            state.Codecs.Add(new Hz { Encoding = "hz" });
            return state.Create();
        }

        // GBK_DECODE
        private static bool GbkDecode(int dc1, int dc2, MbWriter w)
        {
            if (dc1 == 0xa1 && dc2 == 0xaa)
                w.Write(0x2014);
            else if (dc1 == 0xa8 && dc2 == 0x44)
                w.Write(0x2015);
            else if (dc1 == 0xa1 && dc2 == 0xa4)
                w.Write(0x00b7);
            else if (TryDec(_gb2312Dec, dc1 ^ 0x80, dc2 ^ 0x80, out ushort decoded))
                w.Write(decoded);
            else if (TryDec(_gbkextDec, dc1, dc2, out decoded))
                w.Write(decoded);
            else
                return false;
            return true;
        }

        // GBK_ENCODE
        private static bool GbkEncode(int code, out ushort assi)
        {
            assi = 0;
            if (code == 0x2014)
                assi = 0xa1aa;
            else if (code == 0x2015)
                assi = 0xa844;
            else if (code == 0x00b7)
                assi = 0xa1a4;
            else if (code != 0x30fb && TryEnc(_gbcommonEnc, code, out assi))
            {
            }
            else
                return false;
            return true;
        }

        private sealed class Gb2312 : MbCodec
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
                    if (outleft < 2) return MBERR_TOOSMALL;
                    if (!TryEnc(_gbcommonEnc, c, out ushort code))
                        return 1;
                    if ((code & 0x8000) != 0)
                        return 1;
                    b[o.Pos] = (byte)((code >> 8) | 0x80);
                    b[o.Pos + 1] = (byte)((code & 0xFF) | 0x80);
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
                    {
                        w.Write(c);
                        i.Pos += 1; inleft -= 1;
                        continue;
                    }
                    if (inleft < 2) return MBERR_TOOFEW;
                    if (TryDec(_gb2312Dec, c ^ 0x80, b[i.Pos + 1] ^ 0x80, out ushort decoded))
                    {
                        w.Write(decoded);
                        i.Pos += 2; inleft -= 2;
                    }
                    else
                        return 1;
                }
                return 0;
            }
        }

        private sealed class Gbk : MbCodec
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
                    if (outleft < 2) return MBERR_TOOSMALL;
                    if (!GbkEncode(c, out ushort code))
                        return 1;
                    b[o.Pos] = (byte)((code >> 8) | 0x80);
                    b[o.Pos + 1] = (code & 0x8000) != 0 ? (byte)(code & 0xFF) : (byte)((code & 0xFF) | 0x80);
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
                    {
                        w.Write(c);
                        i.Pos += 1; inleft -= 1;
                        continue;
                    }
                    if (inleft < 2) return MBERR_TOOFEW;
                    if (!GbkDecode(c, b[i.Pos + 1], w))
                        return 1;
                    i.Pos += 2; inleft -= 2;
                }
                return 0;
            }
        }

        private sealed class Gb18030 : MbCodec
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
                    if (c >= 0x10000)
                    {
                        int tc = c - 0x10000;
                        if (outleft < 4) return MBERR_TOOSMALL;
                        b[o.Pos + 3] = (byte)(tc % 10 + 0x30);
                        tc /= 10;
                        b[o.Pos + 2] = (byte)(tc % 126 + 0x81);
                        tc /= 126;
                        b[o.Pos + 1] = (byte)(tc % 10 + 0x30);
                        tc /= 10;
                        b[o.Pos] = (byte)(tc + 0x90);
                        inpos += 1; o.Pos += 4; outleft -= 4;
                        continue;
                    }
                    if (outleft < 2) return MBERR_TOOSMALL;
                    ushort code;
                    if (GbkEncode(c, out code))
                    {
                    }
                    else if (TryEnc(_gb18030extEnc, c, out code))
                    {
                    }
                    else
                    {
                        if (outleft < 4) return MBERR_TOOSMALL;
                        int r = 0;
                        for (; _ranges[r].First != 0; r++)
                            if (_ranges[r].First <= c && c <= _ranges[r].Last)
                            {
                                int tc = (int)(c - _ranges[r].First + _ranges[r].Base);
                                b[o.Pos + 3] = (byte)(tc % 10 + 0x30);
                                tc /= 10;
                                b[o.Pos + 2] = (byte)(tc % 126 + 0x81);
                                tc /= 126;
                                b[o.Pos + 1] = (byte)(tc % 10 + 0x30);
                                tc /= 10;
                                b[o.Pos] = (byte)((byte)tc + 0x81);
                                inpos += 1; o.Pos += 4; outleft -= 4;
                                break;
                            }
                        if (_ranges[r].First == 0)
                            return 1;
                        continue;
                    }
                    b[o.Pos] = (byte)((code >> 8) | 0x80);
                    b[o.Pos + 1] = (code & 0x8000) != 0 ? (byte)(code & 0xFF) : (byte)((code & 0xFF) | 0x80);
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
                    {
                        w.Write(c);
                        i.Pos += 1; inleft -= 1;
                        continue;
                    }
                    if (inleft < 2) return MBERR_TOOFEW;
                    int c2 = b[i.Pos + 1];
                    if (c2 >= 0x30 && c2 <= 0x39)
                    {
                        // 4 bytes seq
                        if (inleft < 4) return MBERR_TOOFEW;
                        int c3 = b[i.Pos + 2], c4 = b[i.Pos + 3];
                        if (c < 0x81 || c > 0xFE || c3 < 0x81 || c3 > 0xFE || c4 < 0x30 || c4 > 0x39)
                            return 1;
                        c -= 0x81; c2 -= 0x30;
                        c3 -= 0x81; c4 -= 0x30;
                        if (c < 4)
                        {
                            // U+0080 - U+FFFF
                            int lseq = (c * 10 + c2) * 1260 + c3 * 10 + c4;
                            if (lseq < 39420)
                            {
                                int u = 0;
                                while (lseq >= _ranges[u + 1].Base)
                                    u++;
                                w.Write((int)(_ranges[u].First - _ranges[u].Base + lseq));
                                i.Pos += 4; inleft -= 4;
                                continue;
                            }
                        }
                        else if (c >= 15)
                        {
                            // U+10000 - U+10FFFF
                            int lseq = 0x10000 + ((c - 15) * 10 + c2) * 1260 + c3 * 10 + c4;
                            if (lseq <= 0x10FFFF)
                            {
                                w.Write(lseq);
                                i.Pos += 4; inleft -= 4;
                                continue;
                            }
                        }
                        return 1;
                    }
                    if (GbkDecode(c, c2, w))
                    {
                    }
                    else if (TryDec(_gb18030extDec, c, c2, out ushort decoded))
                        w.Write(decoded);
                    else
                        return 1;
                    i.Pos += 2; inleft -= 2;
                }
                return 0;
            }
        }

        private sealed class Hz : MbCodec
        {
            internal override bool HasEncInit => true;
            internal override int EncInit(MbState state)
            {
                state.C[CN_STATE_OFFSET] = 0;
                return 0;
            }

            internal override bool HasEncReset => true;
            internal override long EncReset(MbState state, MbOut o, long outleft)
            {
                if (state.C[CN_STATE_OFFSET] != 0)
                {
                    if (outleft < 2) return MBERR_TOOSMALL;
                    o.Buf[o.Pos] = (byte)'~';
                    o.Buf[o.Pos + 1] = (byte)'}';
                    state.C[CN_STATE_OFFSET] = 0;
                    o.Pos += 2;
                }
                return 0;
            }

            internal override long Encode(MbState state, int[] data, ref long inpos, long inlen, MbOut o, long outleft, int flags)
            {
                var b = o.Buf;
                while (inpos < inlen)
                {
                    int c = data[inpos];
                    if (c < 0x80)
                    {
                        if (state.C[CN_STATE_OFFSET] != 0)
                        {
                            if (outleft < 2) return MBERR_TOOSMALL;
                            b[o.Pos] = (byte)'~';
                            b[o.Pos + 1] = (byte)'}';
                            o.Pos += 2; outleft -= 2;
                            state.C[CN_STATE_OFFSET] = 0;
                        }
                        if (outleft < 1) return MBERR_TOOSMALL;
                        b[o.Pos] = (byte)c;
                        inpos += 1; o.Pos += 1; outleft -= 1;
                        if (c == '~')
                        {
                            if (outleft < 1) return MBERR_TOOSMALL;
                            b[o.Pos] = (byte)'~';
                            o.Pos += 1; outleft -= 1;
                        }
                        continue;
                    }
                    if (c > 0xFFFF)
                        return 1;
                    if (!TryEnc(_gbcommonEnc, c, out ushort code))
                        return 1;
                    if ((code & 0x8000) != 0)
                        return 1;
                    if (state.C[CN_STATE_OFFSET] == 0)
                    {
                        if (outleft < 4) return MBERR_TOOSMALL;
                        b[o.Pos] = (byte)'~';
                        b[o.Pos + 1] = (byte)'{';
                        b[o.Pos + 2] = (byte)(code >> 8);
                        b[o.Pos + 3] = (byte)(code & 0xff);
                        inpos += 1; o.Pos += 4; outleft -= 4;
                        state.C[CN_STATE_OFFSET] = 1;
                    }
                    else
                    {
                        if (outleft < 2) return MBERR_TOOSMALL;
                        b[o.Pos] = (byte)(code >> 8);
                        b[o.Pos + 1] = (byte)(code & 0xff);
                        inpos += 1; o.Pos += 2; outleft -= 2;
                    }
                }
                return 0;
            }

            internal override bool HasDecInit => true;
            internal override int DecInit(MbState state)
            {
                state.C[CN_STATE_OFFSET] = 0;
                return 0;
            }

            internal override bool HasDecReset => true;
            internal override long DecReset(MbState state)
            {
                state.C[CN_STATE_OFFSET] = 0;
                return 0;
            }

            internal override long Decode(MbState state, MbIn i, long inleft, MbWriter w)
            {
                var b = i.Buf;
                while (inleft > 0)
                {
                    int c = b[i.Pos];
                    if (c == '~')
                    {
                        if (inleft < 2) return MBERR_TOOFEW;
                        int c2 = b[i.Pos + 1];
                        if (c2 == '~' && state.C[CN_STATE_OFFSET] == 0)
                            w.Write('~');
                        else if (c2 == '{' && state.C[CN_STATE_OFFSET] == 0)
                            state.C[CN_STATE_OFFSET] = 1;
                        else if (c2 == '\n' && state.C[CN_STATE_OFFSET] == 0)
                        {
                            // line-continuation
                        }
                        else if (c2 == '}' && state.C[CN_STATE_OFFSET] == 1)
                            state.C[CN_STATE_OFFSET] = 0;
                        else
                            return 1;
                        i.Pos += 2; inleft -= 2;
                        continue;
                    }
                    if ((c & 0x80) != 0)
                        return 1;
                    if (state.C[CN_STATE_OFFSET] == 0)
                    {
                        w.Write(c);
                        i.Pos += 1; inleft -= 1;
                    }
                    else
                    {
                        if (inleft < 2) return MBERR_TOOFEW;
                        if (TryDec(_gb2312Dec, c, b[i.Pos + 1], out ushort decoded))
                        {
                            w.Write(decoded);
                            i.Pos += 2; inleft -= 2;
                        }
                        else
                            return 1;
                    }
                }
                return 0;
            }
        }
    }
}
