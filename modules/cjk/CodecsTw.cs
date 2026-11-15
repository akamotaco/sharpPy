using static SharpPy.Modules.Cjk.Trymap;

namespace SharpPy.Modules.Cjk
{
    /// <summary>Modules/cjkcodecs/_codecs_tw.c: big5 and cp950 over mappings_tw.h's big5 and cp950ext.</summary>
    internal static class CodecsTw
    {
        private static CjkIndex<ushort> _big5Enc, _big5Dec, _cp950extEnc, _cp950extDec;

        internal static PyModule Create()
        {
            var tables = CjkTables.Of("tw");
            _big5Enc = tables.Map("big5_encmap");
            _big5Dec = tables.Map("big5_decmap");
            _cp950extEnc = tables.Map("cp950ext_encmap");
            _cp950extDec = tables.Map("cp950ext_decmap");
            var state = new CjkModule("tw");
            state.Mappings.Add(new DbcsMap("big5", _big5Enc, _big5Dec));
            state.Mappings.Add(new DbcsMap("cp950ext", _cp950extEnc, _cp950extDec));
            state.Codecs.Add(new Big5 { Encoding = "big5" });
            state.Codecs.Add(new Cp950 { Encoding = "cp950" });
            return state.Create();
        }

        private sealed class Big5 : MbCodec
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
                    if (!TryEnc(_big5Enc, c, out ushort code))
                        return 1;
                    b[o.Pos] = (byte)(code >> 8);
                    b[o.Pos + 1] = (byte)(code & 0xFF);
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
                    if (TryDec(_big5Dec, c, b[i.Pos + 1], out ushort decoded))
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

        private sealed class Cp950 : MbCodec
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
                    if (!TryEnc(_cp950extEnc, c, out ushort code) && !TryEnc(_big5Enc, c, out code))
                        return 1;
                    b[o.Pos] = (byte)(code >> 8);
                    b[o.Pos + 1] = (byte)(code & 0xFF);
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
                    if (TryDec(_cp950extDec, c, b[i.Pos + 1], out ushort decoded))
                        w.Write(decoded);
                    else if (TryDec(_big5Dec, c, b[i.Pos + 1], out decoded))
                        w.Write(decoded);
                    else
                        return 1;
                    i.Pos += 2; inleft -= 2;
                }
                return 0;
            }
        }
    }
}
