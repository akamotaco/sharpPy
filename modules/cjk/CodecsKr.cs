using static SharpPy.Modules.Cjk.Trymap;

namespace SharpPy.Modules.Cjk
{
    /// <summary>
    /// Modules/cjkcodecs/_codecs_kr.c: euc_kr, cp949 and johab over mappings_kr.h's ksx1001,
    /// cp949 and cp949ext.
    /// </summary>
    internal static class CodecsKr
    {
        private const int EUCKR_JAMO_FIRSTBYTE = 0xA4, EUCKR_JAMO_FILLER = 0xD4;

        private static CjkIndex<ushort> _ksx1001Dec, _cp949Enc, _cp949extDec;

        private static readonly byte[] U2cgkChoseong = { 0xa1, 0xa2, 0xa4, 0xa7, 0xa8, 0xa9, 0xb1, 0xb2, 0xb3, 0xb5, 0xb6, 0xb7, 0xb8, 0xb9, 0xba, 0xbb, 0xbc, 0xbd, 0xbe };
        private static readonly byte[] U2cgkJungseong = { 0xbf, 0xc0, 0xc1, 0xc2, 0xc3, 0xc4, 0xc5, 0xc6, 0xc7, 0xc8, 0xc9, 0xca, 0xcb, 0xcc, 0xcd, 0xce, 0xcf, 0xd0, 0xd1, 0xd2, 0xd3 };
        private static readonly byte[] U2cgkJongseong = { 0xd4, 0xa1, 0xa2, 0xa3, 0xa4, 0xa5, 0xa6, 0xa7, 0xa9, 0xaa, 0xab, 0xac, 0xad, 0xae, 0xaf, 0xb0, 0xb1, 0xb2, 0xb4, 0xb5, 0xb6, 0xb7, 0xb8, 0xba, 0xbb, 0xbc, 0xbd, 0xbe };

        internal static PyModule Create()
        {
            var tables = CjkTables.Of("kr");
            _ksx1001Dec = tables.Map("ksx1001_decmap");
            _cp949Enc = tables.Map("cp949_encmap");
            _cp949extDec = tables.Map("cp949ext_decmap");
            var state = new CjkModule("kr");
            // BEGIN_MAPPINGS_LIST
            state.Mappings.Add(new DbcsMap("ksx1001", null, _ksx1001Dec));
            state.Mappings.Add(new DbcsMap("cp949", _cp949Enc, null));
            state.Mappings.Add(new DbcsMap("cp949ext", null, _cp949extDec));
            // BEGIN_CODECS_LIST
            state.Codecs.Add(new EucKr { Encoding = "euc_kr" });
            state.Codecs.Add(new Cp949 { Encoding = "cp949" });
            state.Codecs.Add(new Johab { Encoding = "johab" });
            return state.Create();
        }

        private sealed class EucKr : MbCodec
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
                    if (!TryEnc(_cp949Enc, c, out ushort code))
                        return 1;
                    if ((code & 0x8000) == 0)
                    {
                        // KS X 1001 coded character
                        b[o.Pos] = (byte)((code >> 8) | 0x80);
                        b[o.Pos + 1] = (byte)((code & 0xFF) | 0x80);
                        inpos += 1; o.Pos += 2; outleft -= 2;
                    }
                    else
                    {
                        // A CP949 extension, as KS X 1001:1998's make-up sequence.
                        if (outleft < 8) return MBERR_TOOSMALL;
                        b[o.Pos] = EUCKR_JAMO_FIRSTBYTE;
                        b[o.Pos + 1] = EUCKR_JAMO_FILLER;
                        c -= 0xac00;
                        b[o.Pos + 2] = EUCKR_JAMO_FIRSTBYTE;
                        b[o.Pos + 3] = U2cgkChoseong[c / 588];
                        o.Pos += 4; outleft -= 4;
                        b[o.Pos] = EUCKR_JAMO_FIRSTBYTE;
                        b[o.Pos + 1] = U2cgkJungseong[(c / 28) % 21];
                        b[o.Pos + 2] = EUCKR_JAMO_FIRSTBYTE;
                        b[o.Pos + 3] = U2cgkJongseong[c % 28];
                        inpos += 1; o.Pos += 4; outleft -= 4;
                    }
                }
                return 0;
            }

            private const byte NONE = 127;
            private static readonly byte[] Cgk2uChoseong = { 0, 1, NONE, 2, NONE, NONE, 3, 4, 5, NONE, NONE, NONE, NONE, NONE, NONE, NONE, 6, 7, 8, NONE, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18 };
            private static readonly byte[] Cgk2uJongseong = { 1, 2, 3, 4, 5, 6, 7, NONE, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, NONE, 18, 19, 20, 21, 22, NONE, 23, 24, 25, 26, 27 };

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
                    if (c == EUCKR_JAMO_FIRSTBYTE && b[i.Pos + 1] == EUCKR_JAMO_FILLER)
                    {
                        // KS X 1001:1998 make-up sequence
                        int cho, jung, jong;
                        if (inleft < 8) return MBERR_TOOFEW;
                        if (b[i.Pos + 2] != EUCKR_JAMO_FIRSTBYTE || b[i.Pos + 4] != EUCKR_JAMO_FIRSTBYTE || b[i.Pos + 6] != EUCKR_JAMO_FIRSTBYTE)
                            return 1;
                        c = b[i.Pos + 3];
                        cho = 0xa1 <= c && c <= 0xbe ? Cgk2uChoseong[c - 0xa1] : NONE;
                        c = b[i.Pos + 5];
                        jung = 0xbf <= c && c <= 0xd3 ? c - 0xbf : NONE;
                        c = b[i.Pos + 7];
                        if (c == EUCKR_JAMO_FILLER)
                            jong = 0;
                        else if (0xa1 <= c && c <= 0xbe)
                            jong = Cgk2uJongseong[c - 0xa1];
                        else
                            jong = NONE;
                        if (cho == NONE || jung == NONE || jong == NONE)
                            return 1;
                        w.Write(0xac00 + cho * 588 + jung * 28 + jong);
                        i.Pos += 8; inleft -= 8;
                    }
                    else if (TryDec(_ksx1001Dec, c ^ 0x80, b[i.Pos + 1] ^ 0x80, out ushort decoded))
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

        private sealed class Cp949 : MbCodec
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
                    if (!TryEnc(_cp949Enc, c, out ushort code))
                        return 1;
                    b[o.Pos] = (byte)((code >> 8) | 0x80);
                    // MSB set: CP949; unset: KS X 1001
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
                    if (TryDec(_ksx1001Dec, c ^ 0x80, b[i.Pos + 1] ^ 0x80, out ushort decoded))
                        w.Write(decoded);
                    else if (TryDec(_cp949extDec, c, b[i.Pos + 1], out decoded))
                        w.Write(decoded);
                    else
                        return 1;
                    i.Pos += 2; inleft -= 2;
                }
                return 0;
            }
        }

        private sealed class Johab : MbCodec
        {
            private static readonly byte[] U2johabidxChoseong = Pad32(0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10, 0x11, 0x12, 0x13, 0x14);
            private static readonly byte[] U2johabidxJungseong = Pad32(0x03, 0x04, 0x05, 0x06, 0x07, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x1a, 0x1b, 0x1c, 0x1d);
            private static readonly byte[] U2johabidxJongseong = Pad32(0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10, 0x11, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1a, 0x1b, 0x1c, 0x1d);
            private static readonly ushort[] U2johabjamo =
            {
                0x8841, 0x8c41, 0x8444, 0x9041, 0x8446, 0x8447, 0x9441, 0x9841, 0x9c41, 0x844a, 0x844b, 0x844c, 0x844d, 0x844e, 0x844f,
                0x8450, 0xa041, 0xa441, 0xa841, 0x8454, 0xac41, 0xb041, 0xb441, 0xb841, 0xbc41, 0xc041, 0xc441, 0xc841, 0xcc41, 0xd041, 0x8461,
                0x8481, 0x84a1, 0x84c1, 0x84e1, 0x8541, 0x8561, 0x8581, 0x85a1, 0x85c1, 0x85e1, 0x8641, 0x8661, 0x8681, 0x86a1, 0x86c1, 0x86e1,
                0x8741, 0x8761, 0x8781, 0x87a1,
            };

            // a C array of 32 initialized from the front, the rest zero
            private static byte[] Pad32(params byte[] values)
            {
                var array = new byte[32];
                values.CopyTo(array, 0);
                return array;
            }

            internal override long Encode(MbState state, int[] data, ref long inpos, long inlen, MbOut o, long outleft, int flags)
            {
                var b = o.Buf;
                while (inpos < inlen)
                {
                    int c = data[inpos];
                    int code;
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
                    if (c >= 0xac00 && c <= 0xd7a3)
                    {
                        c -= 0xac00;
                        code = 0x8000 | (U2johabidxChoseong[c / 588] << 10) | (U2johabidxJungseong[(c / 28) % 21] << 5) | U2johabidxJongseong[c % 28];
                    }
                    else if (c >= 0x3131 && c <= 0x3163)
                        code = U2johabjamo[c - 0x3131];
                    else if (TryEnc(_cp949Enc, c, out ushort mapped))
                    {
                        int c1 = mapped >> 8, c2 = mapped & 0xff;
                        if (((c1 >= 0x21 && c1 <= 0x2c) || (c1 >= 0x4a && c1 <= 0x7d)) && (c2 >= 0x21 && c2 <= 0x7e))
                        {
                            int t1 = c1 < 0x4a ? (c1 - 0x21 + 0x1b2) : (c1 - 0x21 + 0x197);
                            int t2 = (byte)(((t1 & 1) != 0 ? 0x5e : 0) + (c2 - 0x21));
                            b[o.Pos] = (byte)(t1 >> 1);
                            b[o.Pos + 1] = (byte)(t2 < 0x4e ? t2 + 0x31 : t2 + 0x43);
                            inpos += 1; o.Pos += 2; outleft -= 2;
                            continue;
                        }
                        return 1;
                    }
                    else
                        return 1;
                    b[o.Pos] = (byte)(code >> 8);
                    b[o.Pos + 1] = (byte)(code & 0xff);
                    inpos += 1; o.Pos += 2; outleft -= 2;
                }
                return 0;
            }

            private const byte FILL = 0xfd, NONE = 0xff;
            private static readonly byte[] JohabidxChoseong = { NONE, FILL, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10, 0x11, 0x12, NONE, NONE, NONE, NONE, NONE, NONE, NONE, NONE, NONE, NONE, NONE };
            private static readonly byte[] JohabidxJungseong = { NONE, NONE, FILL, 0x00, 0x01, 0x02, 0x03, 0x04, NONE, NONE, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0a, NONE, NONE, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10, NONE, NONE, 0x11, 0x12, 0x13, 0x14, NONE, NONE };
            private static readonly byte[] JohabidxJongseong = { NONE, FILL, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10, NONE, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1a, 0x1b, NONE, NONE };
            private static readonly byte[] JohabjamoChoseong = { NONE, FILL, 0x31, 0x32, 0x34, 0x37, 0x38, 0x39, 0x41, 0x42, 0x43, 0x45, 0x46, 0x47, 0x48, 0x49, 0x4a, 0x4b, 0x4c, 0x4d, 0x4e, NONE, NONE, NONE, NONE, NONE, NONE, NONE, NONE, NONE, NONE, NONE };
            private static readonly byte[] JohabjamoJungseong = { NONE, NONE, FILL, 0x4f, 0x50, 0x51, 0x52, 0x53, NONE, NONE, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, NONE, NONE, 0x5a, 0x5b, 0x5c, 0x5d, 0x5e, 0x5f, NONE, NONE, 0x60, 0x61, 0x62, 0x63, NONE, NONE };
            private static readonly byte[] JohabjamoJongseong = { NONE, FILL, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x39, 0x3a, 0x3b, 0x3c, 0x3d, 0x3e, 0x3f, 0x40, 0x41, NONE, 0x42, 0x44, 0x45, 0x46, 0x47, 0x48, 0x4a, 0x4b, 0x4c, 0x4d, 0x4e, NONE, NONE };

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
                    if (c < 0xd8)
                    {
                        // johab hangul
                        int cCho = (c >> 2) & 0x1f;
                        int cJung = ((c << 3) | c2 >> 5) & 0x1f;
                        int cJong = c2 & 0x1f;
                        int iCho = JohabidxChoseong[cCho], iJung = JohabidxJungseong[cJung], iJong = JohabidxJongseong[cJong];
                        if (iCho == NONE || iJung == NONE || iJong == NONE)
                            return 1;
                        if (iCho == FILL)
                        {
                            if (iJung == FILL)
                            {
                                if (iJong == FILL)
                                    w.Write(0x3000);
                                else
                                    w.Write(0x3100 | JohabjamoJongseong[cJong]);
                            }
                            else
                            {
                                if (iJong == FILL)
                                    w.Write(0x3100 | JohabjamoJungseong[cJung]);
                                else
                                    return 1;
                            }
                        }
                        else
                        {
                            if (iJung == FILL)
                            {
                                if (iJong == FILL)
                                    w.Write(0x3100 | JohabjamoChoseong[cCho]);
                                else
                                    return 1;
                            }
                            else
                                w.Write(0xac00 + iCho * 588 + iJung * 28 + (iJong == FILL ? 0 : iJong));
                        }
                        i.Pos += 2; inleft -= 2;
                    }
                    else
                    {
                        // KS X 1001 except hangul jamos and syllables
                        if (c == 0xdf || c > 0xf9 || c2 < 0x31 || (c2 >= 0x80 && c2 < 0x91) || (c2 & 0x7f) == 0x7f ||
                            (c == 0xda && (c2 >= 0xa1 && c2 <= 0xd3)))
                            return 1;
                        int t1 = (byte)(c < 0xe0 ? 2 * (c - 0xd9) : 2 * c - 0x197);
                        int t2 = (byte)(c2 < 0x91 ? c2 - 0x31 : c2 - 0x43);
                        t1 = (byte)(t1 + (t2 < 0x5e ? 0 : 1) + 0x21);
                        t2 = (byte)((t2 < 0x5e ? t2 : t2 - 0x5e) + 0x21);
                        if (TryDec(_ksx1001Dec, t1, t2, out ushort decoded))
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
