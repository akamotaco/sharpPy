using System;
using static SharpPy.Modules.Cjk.Trymap;

namespace SharpPy.Modules.Cjk
{
    /// <summary>
    /// Modules/cjkcodecs/_codecs_iso2022.c: iso2022_kr, iso2022_jp, _jp_1, _jp_2, _jp_2004, _jp_3 and
    /// _jp_ext, over the maps of _codecs_kr, _codecs_jp and _codecs_cn, which the designations'
    /// initializers import (USING_IMPORTED_MAPS). state->c[0-3] are G0-G3, c[4] the flags.
    /// </summary>
    internal static class CodecsIso2022
    {
        private const byte ESC = 0x1B, SO = 0x0E, SI = 0x0F, LF = 0x0A;
        private const int MAX_ESCSEQLEN = 16;
        private const byte CHARSET_DBCS = 0x80;
        private const byte CHARSET_ISO8859_1 = (byte)'A', CHARSET_ASCII = (byte)'B', CHARSET_ISO8859_7 = (byte)'F',
            CHARSET_JISX0201_K = (byte)'I', CHARSET_JISX0201_R = (byte)'J',
            CHARSET_GB2312 = (byte)'A' | CHARSET_DBCS, CHARSET_JISX0208 = (byte)'B' | CHARSET_DBCS,
            CHARSET_KSX1001 = (byte)'C' | CHARSET_DBCS, CHARSET_JISX0212 = (byte)'D' | CHARSET_DBCS,
            CHARSET_JISX0213_2000_1 = (byte)'O' | CHARSET_DBCS, CHARSET_JISX0213_2 = (byte)'P' | CHARSET_DBCS,
            CHARSET_JISX0213_2004_1 = (byte)'Q' | CHARSET_DBCS, CHARSET_JISX0208_O = (byte)'@' | CHARSET_DBCS;
        private const uint MAP_UNMAPPABLE = 0xFFFF, MAP_MULTIPLE_AVAIL = 0xFFFE;
        private const byte F_SHIFTED = 0x01, F_ESCTHROUGHOUT = 0x02;
        private const int NO_SHIFT = 0x01, USE_G2 = 0x02, USE_JISX0208_EXT = 0x04;

        private static byte EscMark(byte mark) => (byte)(mark & 0x7f);
        private static bool IsEscEnd(int c) => (c >= 'A' && c <= 'Z') || c == '@';
        private static bool IsIso2022Esc(int c2) => c2 == '(' || c2 == ')' || c2 == '$' || c2 == '.' || c2 == '&';

        // CJK_MOD_SPECIFIC_STATE, and mappings_jisx0213_pair.h's EXTERN_JISX0213_PAIR maps
        private static CjkIndex<ushort> _cp949Enc, _ksx1001Dec, _jisxcommonEnc, _jisx0208Dec, _jisx0212Dec,
            _jisx0213BmpEnc, _jisx0213_1BmpDec, _jisx0213_2BmpDec, _jisx0213EmpEnc, _jisx0213_1EmpDec, _jisx0213_2EmpDec,
            _gbcommonEnc, _gb2312Dec;
        private static CjkPair[] _pairEnc;
        private static CjkIndex<uint> _pairDec;

        private delegate uint DecodeFunc(byte[] data, int at);
        private delegate uint EncodeFunc(int[] data, ref long length);

        // struct iso2022_designation
        private sealed record Designation(byte Mark, byte Plane, byte Width, Func<int> Initializer, DecodeFunc Decoder, EncodeFunc Encoder);

        private static T Import<T>(string locale, string charset, bool enc) where T : class
        {
            var map = CjkModule.ImportMap("_codecs_" + locale, charset);
            return (T)(enc ? map.EncMap : map.DecMap);
        }

        private static int Ksx1001Init()
        {
            _cp949Enc = Import<CjkIndex<ushort>>("kr", "cp949", true);
            _ksx1001Dec = Import<CjkIndex<ushort>>("kr", "ksx1001", false);
            return 0;
        }

        private static uint Ksx1001Decoder(byte[] d, int at) =>
            TryDec(_ksx1001Dec, d[at], d[at + 1], out ushort u) ? u : MAP_UNMAPPABLE;

        private static uint Ksx1001Encoder(int[] data, ref long length)
        {
            if (data[0] < 0x10000 && TryEnc(_cp949Enc, data[0], out ushort coded) && (coded & 0x8000) == 0)
                return coded;
            return MAP_UNMAPPABLE;
        }

        private static int Jisx0208Init()
        {
            _jisxcommonEnc = Import<CjkIndex<ushort>>("jp", "jisxcommon", true);
            _jisx0208Dec = Import<CjkIndex<ushort>>("jp", "jisx0208", false);
            return 0;
        }

        private static uint Jisx0208Decoder(byte[] d, int at)
        {
            if (d[at] == 0x21 && d[at + 1] == 0x40)
                return 0xff3c; // F/W REVERSE SOLIDUS
            return TryDec(_jisx0208Dec, d[at], d[at + 1], out ushort u) ? u : MAP_UNMAPPABLE;
        }

        private static uint Jisx0208Encoder(int[] data, ref long length)
        {
            if (data[0] < 0x10000)
            {
                if (data[0] == 0xff3c)
                    return 0x2140;
                if (TryEnc(_jisxcommonEnc, data[0], out ushort coded) && (coded & 0x8000) == 0)
                    return coded;
            }
            return MAP_UNMAPPABLE;
        }

        private static int Jisx0212Init()
        {
            _jisxcommonEnc = Import<CjkIndex<ushort>>("jp", "jisxcommon", true);
            _jisx0212Dec = Import<CjkIndex<ushort>>("jp", "jisx0212", false);
            return 0;
        }

        private static uint Jisx0212Decoder(byte[] d, int at) =>
            TryDec(_jisx0212Dec, d[at], d[at + 1], out ushort u) ? u : MAP_UNMAPPABLE;

        private static uint Jisx0212Encoder(int[] data, ref long length)
        {
            if (data[0] < 0x10000 && TryEnc(_jisxcommonEnc, data[0], out ushort coded) && (coded & 0x8000) != 0)
                return (uint)(coded & 0x7fff);
            return MAP_UNMAPPABLE;
        }

        private static int Jisx0213Init()
        {
            Jisx0208Init();
            _jisx0213BmpEnc = Import<CjkIndex<ushort>>("jp", "jisx0213_bmp", true);
            _jisx0213_1BmpDec = Import<CjkIndex<ushort>>("jp", "jisx0213_1_bmp", false);
            _jisx0213_2BmpDec = Import<CjkIndex<ushort>>("jp", "jisx0213_2_bmp", false);
            _jisx0213EmpEnc = Import<CjkIndex<ushort>>("jp", "jisx0213_emp", true);
            _jisx0213_1EmpDec = Import<CjkIndex<ushort>>("jp", "jisx0213_1_emp", false);
            _jisx0213_2EmpDec = Import<CjkIndex<ushort>>("jp", "jisx0213_2_emp", false);
            _pairEnc = Import<CjkPair[]>("jp", "jisx0213_pair", true);
            _pairDec = Import<CjkIndex<uint>>("jp", "jisx0213_pair", false);
            return 0;
        }

        private static uint Jisx0213_1Decoder(byte[] d, int at, int config)
        {
            int c1 = d[at], c2 = d[at + 1];
            uint u;
            if (CodecsJp.Emu2000DecodePlane1Invalid(config, c1, c2))
                return MAP_UNMAPPABLE;
            else if (c1 == 0x21 && c2 == 0x40)
                return 0xff3c; // F/W REVERSE SOLIDUS
            else if (TryDec(_jisx0208Dec, c1, c2, out ushort v))
                u = v;
            else if (TryDec(_jisx0213_1BmpDec, c1, c2, out v))
                u = v;
            else if (TryDec(_jisx0213_1EmpDec, c1, c2, out v))
                u = v | 0x20000u;
            else if (TryDecWide(_pairDec, c1, c2, out uint w))
                u = w;
            else
                return MAP_UNMAPPABLE;
            return u;
        }

        private static uint Jisx0213_2Decoder(byte[] d, int at, int config)
        {
            int c1 = d[at], c2 = d[at + 1];
            uint u = 0;
            // EMULATE_JISX0213_2000_DECODE_PLANE2_CHAR, whose assignment the next test overrides
            if (config == 2000 && c1 == 0x7D && c2 == 0x3B)
                u = 0x9B1D;
            if (TryDec(_jisx0213_2BmpDec, c1, c2, out ushort v))
                u = v;
            else if (TryDec(_jisx0213_2EmpDec, c1, c2, out v))
                u = v | 0x20000u;
            else
                return MAP_UNMAPPABLE;
            return u;
        }

        // jisx0213_encoder
        private static uint Jisx0213Encoder(int[] data, ref long length, int config)
        {
            ushort coded;
            switch (length)
            {
                case 1: // first character
                    if (data[0] >= 0x10000)
                    {
                        if (data[0] >> 16 == 0x20000 >> 16)
                        {
                            if (config == 2000 && data[0] == 0x20B9F)
                                return MAP_UNMAPPABLE;
                            else if (TryEnc(_jisx0213EmpEnc, data[0] & 0xffff, out coded))
                                return coded;
                        }
                        return MAP_UNMAPPABLE;
                    }
                    if (CodecsJp.Emu2000EncodeBmpInvalid(config, data[0]))
                        return MAP_UNMAPPABLE;
                    else if (config == 2000 && data[0] == 0x9B1D)
                        coded = 0x8000 | 0x7d3b;
                    else if (TryEnc(_jisx0213BmpEnc, data[0], out coded))
                    {
                        if (coded == CjkTables.MULTIC)
                            return MAP_MULTIPLE_AVAIL;
                    }
                    else if (TryEnc(_jisxcommonEnc, data[0], out coded))
                    {
                        if ((coded & 0x8000) != 0)
                            return MAP_UNMAPPABLE;
                    }
                    else
                        return MAP_UNMAPPABLE;
                    return coded;
                case 2: // second character of unicode pair
                    coded = FindPairEncMap(data[0], data[1], _pairEnc);
                    if (coded != CjkTables.DBCINV)
                        return coded;
                    goto case -1;
                case -1: // flush unterminated
                    length = 1;
                    coded = FindPairEncMap(data[0], 0, _pairEnc);
                    return coded == CjkTables.DBCINV ? MAP_UNMAPPABLE : coded;
                default:
                    return MAP_UNMAPPABLE;
            }
        }

        private static uint Plane1Of(uint coded) =>
            coded == MAP_UNMAPPABLE || coded == MAP_MULTIPLE_AVAIL ? coded : (coded & 0x8000) != 0 ? MAP_UNMAPPABLE : coded;

        private static uint Plane2Of(uint coded) =>
            coded == MAP_UNMAPPABLE || coded == MAP_MULTIPLE_AVAIL ? coded : (coded & 0x8000) != 0 ? coded & 0x7fff : MAP_UNMAPPABLE;

        // jisx0213_*_encoder_paironly
        private static uint PairOnly(int[] data, ref long length, int config)
        {
            long ilength = length;
            uint coded = Jisx0213Encoder(data, ref length, config);
            switch (ilength)
            {
                case 1:
                    return coded == MAP_MULTIPLE_AVAIL ? MAP_MULTIPLE_AVAIL : MAP_UNMAPPABLE;
                case 2:
                    return length != 2 ? MAP_UNMAPPABLE : coded;
                default:
                    return MAP_UNMAPPABLE;
            }
        }

        private static uint Jisx0201RDecoder(byte[] d, int at)
        {
            int c = d[at];
            if (c < 0x5c) return (uint)c;
            if (c == 0x5c) return 0x00a5;
            if (c < 0x7e) return (uint)c;
            if (c == 0x7e) return 0x203e;
            if (c == 0x7f) return 0x7f;
            return MAP_UNMAPPABLE;
        }

        private static uint Jisx0201REncoder(int[] data, ref long length)
        {
            int c = data[0];
            if (c < 0x80 && c != 0x5c && c != 0x7e) return (uint)c;
            if (c == 0x00a5) return 0x5c;
            if (c == 0x203e) return 0x7e;
            return MAP_UNMAPPABLE;
        }

        private static uint Jisx0201KDecoder(byte[] d, int at)
        {
            int c = d[at] ^ 0x80;
            return c >= 0xa1 && c <= 0xdf ? (uint)(0xfec0 + c) : MAP_UNMAPPABLE;
        }

        private static uint Jisx0201KEncoder(int[] data, ref long length)
        {
            int c = data[0];
            return c >= 0xff61 && c <= 0xff9f ? (uint)(c - 0xfec0 - 0x80) : MAP_UNMAPPABLE;
        }

        private static int Gb2312Init()
        {
            _gbcommonEnc = Import<CjkIndex<ushort>>("cn", "gbcommon", true);
            _gb2312Dec = Import<CjkIndex<ushort>>("cn", "gb2312", false);
            return 0;
        }

        private static uint Gb2312Decoder(byte[] d, int at) =>
            TryDec(_gb2312Dec, d[at], d[at + 1], out ushort u) ? u : MAP_UNMAPPABLE;

        private static uint Gb2312Encoder(int[] data, ref long length)
        {
            if (data[0] < 0x10000 && TryEnc(_gbcommonEnc, data[0], out ushort coded) && (coded & 0x8000) == 0)
                return coded;
            return MAP_UNMAPPABLE;
        }

        private static uint DummyDecoder(byte[] d, int at) => MAP_UNMAPPABLE;
        private static uint DummyEncoder(int[] data, ref long length) => MAP_UNMAPPABLE;

        private static readonly Designation KSX1001_G0 = new(CHARSET_KSX1001, 0, 2, Ksx1001Init, Ksx1001Decoder, Ksx1001Encoder);
        private static readonly Designation KSX1001_G1 = new(CHARSET_KSX1001, 1, 2, Ksx1001Init, Ksx1001Decoder, Ksx1001Encoder);
        private static readonly Designation JISX0201_R = new(CHARSET_JISX0201_R, 0, 1, null, Jisx0201RDecoder, Jisx0201REncoder);
        private static readonly Designation JISX0201_K = new(CHARSET_JISX0201_K, 0, 1, null, Jisx0201KDecoder, Jisx0201KEncoder);
        private static readonly Designation JISX0208 = new(CHARSET_JISX0208, 0, 2, Jisx0208Init, Jisx0208Decoder, Jisx0208Encoder);
        private static readonly Designation JISX0208_O = new(CHARSET_JISX0208_O, 0, 2, Jisx0208Init, Jisx0208Decoder, Jisx0208Encoder);
        private static readonly Designation JISX0212 = new(CHARSET_JISX0212, 0, 2, Jisx0212Init, Jisx0212Decoder, Jisx0212Encoder);
        private static readonly Designation JISX0213_2000_1 = new(CHARSET_JISX0213_2000_1, 0, 2, Jisx0213Init,
            (d, at) => Jisx0213_1Decoder(d, at, 2000), (int[] data, ref long length) => Plane1Of(Jisx0213Encoder(data, ref length, 2000)));
        private static readonly Designation JISX0213_2000_1_PAIRONLY = new(CHARSET_JISX0213_2000_1, 0, 2, Jisx0213Init,
            (d, at) => Jisx0213_1Decoder(d, at, 2000), (int[] data, ref long length) => PairOnly(data, ref length, 2000));
        private static readonly Designation JISX0213_2000_2 = new(CHARSET_JISX0213_2, 0, 2, Jisx0213Init,
            (d, at) => Jisx0213_2Decoder(d, at, 2000), (int[] data, ref long length) => Plane2Of(Jisx0213Encoder(data, ref length, 2000)));
        private static readonly Designation JISX0213_2004_1 = new(CHARSET_JISX0213_2004_1, 0, 2, Jisx0213Init,
            (d, at) => Jisx0213_1Decoder(d, at, 0), (int[] data, ref long length) => Plane1Of(Jisx0213Encoder(data, ref length, 0)));
        private static readonly Designation JISX0213_2004_1_PAIRONLY = new(CHARSET_JISX0213_2004_1, 0, 2, Jisx0213Init,
            (d, at) => Jisx0213_1Decoder(d, at, 0), (int[] data, ref long length) => PairOnly(data, ref length, 0));
        private static readonly Designation JISX0213_2004_2 = new(CHARSET_JISX0213_2, 0, 2, Jisx0213Init,
            (d, at) => Jisx0213_2Decoder(d, at, 0), (int[] data, ref long length) => Plane2Of(Jisx0213Encoder(data, ref length, 0)));
        private static readonly Designation GB2312 = new(CHARSET_GB2312, 0, 2, Gb2312Init, Gb2312Decoder, Gb2312Encoder);
        private static readonly Designation ISO8859_1 = new(CHARSET_ISO8859_1, 2, 1, null, DummyDecoder, DummyEncoder);
        private static readonly Designation ISO8859_7 = new(CHARSET_ISO8859_7, 2, 1, null, DummyDecoder, DummyEncoder);

        internal static PyModule Create()
        {
            var state = new CjkModule("iso2022");
            state.Codecs.Add(new Iso2022("iso2022_kr", 0, KSX1001_G1));
            state.Codecs.Add(new Iso2022("iso2022_jp", NO_SHIFT | USE_JISX0208_EXT, JISX0208, JISX0201_R, JISX0208_O));
            state.Codecs.Add(new Iso2022("iso2022_jp_1", NO_SHIFT | USE_JISX0208_EXT, JISX0208, JISX0212, JISX0201_R, JISX0208_O));
            state.Codecs.Add(new Iso2022("iso2022_jp_2", NO_SHIFT | USE_G2 | USE_JISX0208_EXT, JISX0208, JISX0212, KSX1001_G0,
                GB2312, JISX0201_R, JISX0208_O, ISO8859_1, ISO8859_7));
            state.Codecs.Add(new Iso2022("iso2022_jp_2004", NO_SHIFT | USE_JISX0208_EXT, JISX0213_2004_1_PAIRONLY, JISX0208,
                JISX0213_2004_1, JISX0213_2004_2));
            state.Codecs.Add(new Iso2022("iso2022_jp_3", NO_SHIFT | USE_JISX0208_EXT, JISX0213_2000_1_PAIRONLY, JISX0208,
                JISX0213_2000_1, JISX0213_2000_2));
            state.Codecs.Add(new Iso2022("iso2022_jp_ext", NO_SHIFT | USE_JISX0208_EXT, JISX0208, JISX0212, JISX0201_R,
                JISX0201_K, JISX0208_O));
            return state.Create();
        }

        private sealed class Iso2022 : MbCodec
        {
            // struct iso2022_config
            private readonly int _flags;
            private readonly Designation[] _designations;

            internal Iso2022(string encoding, int flags, params Designation[] designations)
            {
                Encoding = encoding;
                _flags = flags;
                _designations = designations;
            }

            private bool ConfigIsSet(int flag) => (_flags & flag) != 0;

            // iso2022_codec_init
            internal override int CodecInit()
            {
                foreach (var desig in _designations)
                    if (desig.Initializer != null && desig.Initializer() != 0)
                        return -1;
                return 0;
            }

            internal override bool HasEncInit => true;
            internal override int EncInit(MbState state)
            {
                state.C[4] = 0;
                state.C[0] = CHARSET_ASCII;
                state.C[1] = CHARSET_ASCII;
                return 0;
            }

            internal override bool HasEncReset => true;
            internal override long EncReset(MbState state, MbOut o, long outleft)
            {
                if ((state.C[4] & F_SHIFTED) != 0)
                {
                    if (outleft < 1) return MBERR_TOOSMALL;
                    o.Buf[o.Pos] = SI;
                    o.Pos += 1; outleft -= 1;
                    state.C[4] &= unchecked((byte)~F_SHIFTED);
                }
                if (state.C[0] != CHARSET_ASCII)
                {
                    if (outleft < 3) return MBERR_TOOSMALL;
                    o.Buf[o.Pos] = ESC;
                    o.Buf[o.Pos + 1] = (byte)'(';
                    o.Buf[o.Pos + 2] = (byte)'B';
                    o.Pos += 3; outleft -= 3;
                    state.C[0] = CHARSET_ASCII;
                }
                return 0;
            }

            private static bool Write(MbOut o, ref long outleft, params byte[] bytes)
            {
                if (outleft < bytes.Length)
                    return false;
                bytes.CopyTo(o.Buf, o.Pos);
                o.Pos += bytes.Length;
                outleft -= bytes.Length;
                return true;
            }

            internal override long Encode(MbState state, int[] data, ref long inpos, long inlen, MbOut o, long outleft, int flags)
            {
                while (inpos < inlen)
                {
                    int c = data[inpos];
                    long insize;
                    uint encoded;
                    if (c < 0x80)
                    {
                        if (state.C[0] != CHARSET_ASCII)
                        {
                            if (!Write(o, ref outleft, ESC, (byte)'(', (byte)'B')) return MBERR_TOOSMALL;
                            state.C[0] = CHARSET_ASCII;
                        }
                        if ((state.C[4] & F_SHIFTED) != 0)
                        {
                            if (!Write(o, ref outleft, SI)) return MBERR_TOOSMALL;
                            state.C[4] &= unchecked((byte)~F_SHIFTED);
                        }
                        if (!Write(o, ref outleft, (byte)c)) return MBERR_TOOSMALL;
                        inpos += 1;
                        continue;
                    }
                    insize = 1;
                    encoded = MAP_UNMAPPABLE;
                    Designation dsg = null;
                    foreach (var candidate in _designations)
                    {
                        var buf = new[] { c, 0 };
                        long length = 1;
                        encoded = candidate.Encoder(buf, ref length);
                        if (encoded == MAP_MULTIPLE_AVAIL)
                        {
                            // this implementation won't work for pair of non-bmp characters.
                            if (inlen - inpos < 2)
                            {
                                if ((flags & MBENC_FLUSH) == 0)
                                    return MBERR_TOOFEW;
                                length = -1;
                            }
                            else
                            {
                                buf[1] = data[inpos + 1];
                                length = 2;
                            }
                            encoded = candidate.Encoder(buf, ref length);
                            if (encoded != MAP_UNMAPPABLE)
                            {
                                insize = length;
                                dsg = candidate;
                                break;
                            }
                        }
                        else if (encoded != MAP_UNMAPPABLE)
                        {
                            dsg = candidate;
                            break;
                        }
                    }
                    if (dsg == null)
                        return 1;
                    switch (dsg.Plane)
                    {
                        case 0: // G0
                            if ((state.C[4] & F_SHIFTED) != 0)
                            {
                                if (!Write(o, ref outleft, SI)) return MBERR_TOOSMALL;
                                state.C[4] &= unchecked((byte)~F_SHIFTED);
                            }
                            if (state.C[0] != dsg.Mark)
                            {
                                if (dsg.Width == 1)
                                {
                                    if (!Write(o, ref outleft, ESC, (byte)'(', EscMark(dsg.Mark))) return MBERR_TOOSMALL;
                                }
                                else if (dsg.Mark == CHARSET_JISX0208)
                                {
                                    if (!Write(o, ref outleft, ESC, (byte)'$', EscMark(dsg.Mark))) return MBERR_TOOSMALL;
                                }
                                else
                                {
                                    if (!Write(o, ref outleft, ESC, (byte)'$', (byte)'(', EscMark(dsg.Mark))) return MBERR_TOOSMALL;
                                }
                                state.C[0] = dsg.Mark;
                            }
                            break;
                        case 1: // G1
                            if (state.C[1] != dsg.Mark)
                            {
                                if (dsg.Width == 1)
                                {
                                    if (!Write(o, ref outleft, ESC, (byte)')', EscMark(dsg.Mark))) return MBERR_TOOSMALL;
                                }
                                else
                                {
                                    if (!Write(o, ref outleft, ESC, (byte)'$', (byte)')', EscMark(dsg.Mark))) return MBERR_TOOSMALL;
                                }
                                state.C[1] = dsg.Mark;
                            }
                            if ((state.C[4] & F_SHIFTED) == 0)
                            {
                                if (!Write(o, ref outleft, SO)) return MBERR_TOOSMALL;
                                state.C[4] |= F_SHIFTED;
                            }
                            break;
                        default: // G2 and G3 are not supported
                            return MBERR_INTERNAL;
                    }
                    if (dsg.Width == 1)
                    {
                        if (!Write(o, ref outleft, (byte)encoded)) return MBERR_TOOSMALL;
                    }
                    else
                    {
                        if (!Write(o, ref outleft, (byte)(encoded >> 8), (byte)(encoded & 0xff))) return MBERR_TOOSMALL;
                    }
                    inpos += insize;
                }
                return 0;
            }

            internal override bool HasDecInit => true;
            internal override int DecInit(MbState state)
            {
                state.C[4] = 0;
                state.C[0] = CHARSET_ASCII;
                state.C[1] = CHARSET_ASCII;
                state.C[2] = CHARSET_ASCII;
                return 0;
            }

            internal override bool HasDecReset => true;
            internal override long DecReset(MbState state)
            {
                state.C[0] = CHARSET_ASCII;
                state.C[4] &= unchecked((byte)~F_SHIFTED);
                return 0;
            }

            // iso2022processesc
            private long ProcessEsc(MbState state, MbIn i, ref long inleft)
            {
                var b = i.Buf;
                int esclen = 0;
                byte charset, designation;
                for (int k = 1; k < MAX_ESCSEQLEN; k++)
                {
                    if (k >= inleft)
                        return MBERR_TOOFEW;
                    if (IsEscEnd(b[i.Pos + k]))
                    {
                        esclen = k + 1;
                        break;
                    }
                    else if (ConfigIsSet(USE_JISX0208_EXT) && k + 1 < inleft && b[i.Pos + k] == '&' && b[i.Pos + k + 1] == '@')
                        k += 2;
                }
                switch (esclen)
                {
                    case 0:
                        return 1; // unterminated escape sequence
                    case 3:
                        if (b[i.Pos + 1] == '$')
                        {
                            charset = (byte)(b[i.Pos + 2] | CHARSET_DBCS);
                            designation = 0;
                        }
                        else
                        {
                            charset = b[i.Pos + 2];
                            if (b[i.Pos + 1] == '(')
                                designation = 0;
                            else if (b[i.Pos + 1] == ')')
                                designation = 1;
                            else if (ConfigIsSet(USE_G2) && b[i.Pos + 1] == '.')
                                designation = 2;
                            else
                                return 3;
                        }
                        break;
                    case 4:
                        if (b[i.Pos + 1] != '$')
                            return 4;
                        charset = (byte)(b[i.Pos + 3] | CHARSET_DBCS);
                        if (b[i.Pos + 2] == '(')
                            designation = 0;
                        else if (b[i.Pos + 2] == ')')
                            designation = 1;
                        else
                            return 4;
                        break;
                    case 6: // designation with prefix
                        if (ConfigIsSet(USE_JISX0208_EXT) && b[i.Pos + 3] == ESC && b[i.Pos + 4] == '$' && b[i.Pos + 5] == 'B')
                        {
                            charset = (byte)('B' | CHARSET_DBCS);
                            designation = 0;
                        }
                        else
                            return 6;
                        break;
                    default:
                        return esclen;
                }
                // raise error when the charset is not designated for this encoding
                if (charset != CHARSET_ASCII && Array.FindIndex(_designations, d => d.Mark == charset) < 0)
                    return esclen;
                state.C[designation] = charset;
                inleft -= esclen;
                i.Pos += esclen;
                return 0;
            }

            // ISO8859_7_DECODE
            private static bool Iso8859_7Decode(int c, MbWriter w)
            {
                if (c < 0xa0)
                    w.Write(c);
                else if (c < 0xc0 && (0x288f3bc9L & (1L << (c - 0xa0))) != 0)
                    w.Write(c);
                else if (c >= 0xb4 && c <= 0xfe && (c >= 0xd4 || (0xbffffd77L & (1L << (c - 0xb4))) != 0))
                    w.Write(0x02d0 + c);
                else if (c == 0xa1)
                    w.Write(0x2018);
                else if (c == 0xa2)
                    w.Write(0x2019);
                else if (c == 0xaf)
                    w.Write(0x2015);
                else
                    return false;
                return true;
            }

            // iso2022processg2
            private static long ProcessG2(MbState state, MbIn i, ref long inleft, MbWriter w)
            {
                int c3 = i.Buf[i.Pos + 2];
                if (state.C[2] == CHARSET_ISO8859_1)
                {
                    if (c3 < 0x80)
                        w.Write(c3 + 0x80);
                    else
                        return 3;
                }
                else if (state.C[2] == CHARSET_ISO8859_7)
                {
                    if (!Iso8859_7Decode(c3 ^ 0x80, w))
                        return 3;
                }
                else if (state.C[2] == CHARSET_ASCII)
                {
                    if ((c3 & 0x80) != 0)
                        return 3;
                    w.Write(c3);
                }
                else
                    return MBERR_INTERNAL;
                i.Pos += 3;
                inleft -= 3;
                return 0;
            }

            internal override long Decode(MbState state, MbIn i, long inleft, MbWriter w)
            {
                var b = i.Buf;
                Designation dsgcache = null;
                while (inleft > 0)
                {
                    int c = b[i.Pos];
                    long err;
                    if ((state.C[4] & F_ESCTHROUGHOUT) != 0)
                    {
                        // ESC throughout mode: for non-iso2022 escape sequences
                        w.Write(c);
                        i.Pos += 1; inleft -= 1;
                        if (IsEscEnd(c))
                            state.C[4] &= unchecked((byte)~F_ESCTHROUGHOUT);
                        continue;
                    }
                    switch (c)
                    {
                        case ESC:
                            if (inleft < 2) return MBERR_TOOFEW;
                            if (IsIso2022Esc(b[i.Pos + 1]))
                            {
                                err = ProcessEsc(state, i, ref inleft);
                                if (err != 0)
                                    return err;
                            }
                            else if (ConfigIsSet(USE_G2) && b[i.Pos + 1] == 'N')
                            {
                                // SS2
                                if (inleft < 3) return MBERR_TOOFEW;
                                err = ProcessG2(state, i, ref inleft, w);
                                if (err != 0)
                                    return err;
                            }
                            else
                            {
                                w.Write(ESC);
                                state.C[4] |= F_ESCTHROUGHOUT;
                                i.Pos += 1; inleft -= 1;
                            }
                            break;
                        case SI:
                            if (ConfigIsSet(NO_SHIFT))
                                goto bypass;
                            state.C[4] &= unchecked((byte)~F_SHIFTED);
                            i.Pos += 1; inleft -= 1;
                            break;
                        case SO:
                            if (ConfigIsSet(NO_SHIFT))
                                goto bypass;
                            state.C[4] |= F_SHIFTED;
                            i.Pos += 1; inleft -= 1;
                            break;
                        case LF:
                            state.C[4] &= unchecked((byte)~F_SHIFTED);
                            w.Write(LF);
                            i.Pos += 1; inleft -= 1;
                            break;
                        default:
                            if (c < 0x20) // C0
                                goto bypass;
                            else if (c >= 0x80)
                                return 1;
                            else
                            {
                                byte charset = (state.C[4] & F_SHIFTED) != 0 ? state.C[1] : state.C[0];
                                if (charset == CHARSET_ASCII)
                                    goto bypass;
                                Designation dsg;
                                if (dsgcache != null && dsgcache.Mark == charset)
                                    dsg = dsgcache;
                                else
                                {
                                    dsg = Array.Find(_designations, d => d.Mark == charset);
                                    dsgcache = dsg;
                                }
                                if (inleft < dsg.Width) return MBERR_TOOFEW;
                                uint decoded = dsg.Decoder(b, i.Pos);
                                if (decoded == MAP_UNMAPPABLE)
                                    return dsg.Width;
                                if (decoded < 0x30000)
                                    w.Write((int)decoded);
                                else
                                {
                                    // JIS X 0213 pairs
                                    w.Write((int)(decoded >> 16));
                                    w.Write((int)(decoded & 0xffff));
                                }
                                i.Pos += dsg.Width; inleft -= dsg.Width;
                            }
                            break;
                    }
                    continue;
                bypass:
                    w.Write(c);
                    i.Pos += 1; inleft -= 1;
                }
                return 0;
            }
        }
    }
}
