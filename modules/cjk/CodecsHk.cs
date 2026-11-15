using static SharpPy.Modules.Cjk.Trymap;

namespace SharpPy.Modules.Cjk
{
    /// <summary>
    /// Modules/cjkcodecs/_codecs_hk.c: big5hkscs over mappings_hk.h and _codecs_tw's big5, which
    /// codecinit imports (USING_IMPORTED_MAPS).
    /// </summary>
    internal static class CodecsHk
    {
        private static CjkIndex<ushort> _big5hkscsDec, _bmpEnc, _nonbmpEnc;
        private static byte[] _phint0, _phint12130, _phint21924;

        internal static PyModule Create()
        {
            var tables = CjkTables.Of("hk");
            _big5hkscsDec = tables.Map("big5hkscs_decmap");
            _bmpEnc = tables.Map("big5hkscs_bmp_encmap");
            _nonbmpEnc = tables.Map("big5hkscs_nonbmp_encmap");
            _phint0 = tables.Get<byte[]>("big5hkscs_phint_0");
            _phint12130 = tables.Get<byte[]>("big5hkscs_phint_12130");
            _phint21924 = tables.Get<byte[]>("big5hkscs_phint_21924");
            var state = new CjkModule("hk");
            state.Mappings.Add(new DbcsMap("big5hkscs", null, _big5hkscsDec));
            state.Mappings.Add(new DbcsMap("big5hkscs_bmp", _bmpEnc, null));
            state.Mappings.Add(new DbcsMap("big5hkscs_nonbmp", _nonbmpEnc, null));
            state.Codecs.Add(new Big5hkscs { Encoding = "big5hkscs" });
            return state.Create();
        }

        private sealed class Big5hkscs : MbCodec
        {
            // CJK_MOD_SPECIFIC_STATE: big5_encmap and big5_decmap
            private CjkIndex<ushort> _big5Enc, _big5Dec;

            private static readonly ushort[] PairencTable = { 0x8862, 0x8864, 0x88a3, 0x88a5 };

            internal override int CodecInit()
            {
                var map = CjkModule.ImportMap("_codecs_tw", "big5");
                _big5Enc = (CjkIndex<ushort>)map.EncMap;
                _big5Dec = (CjkIndex<ushort>)map.DecMap;
                return 0;
            }

            internal override long Encode(MbState state, int[] data, ref long inpos, long inlen, MbOut o, long outleft, int flags)
            {
                var b = o.Buf;
                while (inpos < inlen)
                {
                    int c = data[inpos];
                    ushort code;
                    int insize;
                    if (c < 0x80)
                    {
                        if (outleft < 1) return MBERR_TOOSMALL;
                        b[o.Pos] = (byte)c;
                        inpos += 1; o.Pos += 1; outleft -= 1;
                        continue;
                    }
                    insize = 1;
                    if (outleft < 2) return MBERR_TOOSMALL;
                    if (c < 0x10000)
                    {
                        if (TryEnc(_bmpEnc, c, out code))
                        {
                            if (code == CjkTables.MULTIC)
                            {
                                int c2 = inlen - inpos >= 2 ? data[inpos + 1] : 0;
                                if (inlen - inpos >= 2 && (c & 0xffdf) == 0x00ca && (c2 & 0xfff7) == 0x0304)
                                {
                                    code = PairencTable[((c >> 4) | (c2 >> 3)) & 3];
                                    insize = 2;
                                }
                                else if (inlen - inpos < 2 && (flags & MBENC_FLUSH) == 0)
                                    return MBERR_TOOFEW;
                                else
                                    code = c == 0xca ? (ushort)0x8866 : (ushort)0x88a7;
                            }
                        }
                        else if (TryEnc(_big5Enc, c, out code))
                        {
                        }
                        else
                            return 1;
                    }
                    else if (c < 0x20000)
                        return insize;
                    else if (c < 0x30000)
                    {
                        if (!TryEnc(_nonbmpEnc, c & 0xffff, out code))
                            return insize;
                    }
                    else
                        return insize;
                    b[o.Pos] = (byte)(code >> 8);
                    b[o.Pos + 1] = (byte)(code & 0xFF);
                    inpos += insize; o.Pos += 2; outleft -= 2;
                }
                return 0;
            }

            // BH2S
            private static int Bh2s(int c1, int c2) => (c1 - 0x87) * (0xfe - 0x40 + 1) + (c2 - 0x40);

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
                    ushort decoded;
                    if (0xc6 > c || c > 0xc8 || (c < 0xc7 && c2 < 0xa1))
                    {
                        if (TryDec(_big5Dec, c, c2, out decoded))
                        {
                            w.Write(decoded);
                            i.Pos += 2; inleft -= 2;
                            continue;
                        }
                    }
                    if (TryDec(_big5hkscsDec, c, c2, out decoded))
                    {
                        int s = Bh2s(c, c2);
                        byte[] hintbase;
                        if (Bh2s(0x87, 0x40) <= s && s <= Bh2s(0xa0, 0xfe))
                        {
                            hintbase = _phint0;
                            s -= Bh2s(0x87, 0x40);
                        }
                        else if (Bh2s(0xc6, 0xa1) <= s && s <= Bh2s(0xc8, 0xfe))
                        {
                            hintbase = _phint12130;
                            s -= Bh2s(0xc6, 0xa1);
                        }
                        else if (Bh2s(0xf9, 0xd6) <= s && s <= Bh2s(0xfe, 0xfe))
                        {
                            hintbase = _phint21924;
                            s -= Bh2s(0xf9, 0xd6);
                        }
                        else
                            return MBERR_INTERNAL;
                        if ((hintbase[s >> 3] & (1 << (s & 7))) != 0)
                            w.Write(decoded | 0x20000);
                        else
                            w.Write(decoded);
                        i.Pos += 2; inleft -= 2;
                        continue;
                    }
                    switch ((c << 8) | c2)
                    {
                        case 0x8862: w.Write(0x00ca); w.Write(0x0304); break;
                        case 0x8864: w.Write(0x00ca); w.Write(0x030c); break;
                        case 0x88a3: w.Write(0x00ea); w.Write(0x0304); break;
                        case 0x88a5: w.Write(0x00ea); w.Write(0x030c); break;
                        default: return 1;
                    }
                    i.Pos += 2; inleft -= 2;
                }
                return 0;
            }
        }
    }
}
