using System;

namespace SharpPy.Modules.Expat
{
    /// <summary>POSITION (xmltok.h): first line and first column are 0 not 1.</summary>
    internal struct POSITION
    {
        public ulong lineNumber;
        public ulong columnNumber;
    }

    /// <summary>ATTRIBUTE (xmltok.h): offsets into the buffer that was tokenized.</summary>
    internal struct ATTRIBUTE
    {
        public int name;
        public int valuePtr;
        public int valueEnd;
        public bool normalized;
    }

    internal enum XML_Convert_Result
    {
        XML_CONVERT_COMPLETED = 0,
        XML_CONVERT_INPUT_INCOMPLETE = 1,
        XML_CONVERT_OUTPUT_EXHAUSTED = 2 /* and therefore potentially input remaining as well */
    }

    /// <summary>
    /// struct encoding (xmltok.h). The C struct is a vtable of function pointers; here the entries are
    /// virtual methods, the Xml* macros of xmltok.h. Pointers are offsets into the byte array b.
    /// </summary>
    internal abstract class ENCODING
    {
        internal int minBytesPerChar;
        internal bool isUtf8;
        internal bool isUtf16;

        // scanners[XML_N_STATES]: XmlTok(enc, state, ...)
        internal abstract int Tok(int state, byte[] b, int ptr, int end, ref int nextTokPtr);
        // literalScanners[XML_N_LITERAL_TYPES]: XmlLiteralTok(enc, literalType, ...)
        internal abstract int XmlLiteralTok(int literalType, byte[] b, int ptr, int end, ref int nextTokPtr);
        internal abstract bool NameMatchesAscii(byte[] b, int ptr1, int end1, byte[] ptr2);
        internal abstract int NameLength(byte[] b, int ptr);
        internal abstract int SkipS(byte[] b, int ptr);
        internal abstract int GetAtts(byte[] b, int ptr, int attsMax, ATTRIBUTE[] atts);
        internal abstract int CharRefNumber(byte[] b, int ptr);
        internal abstract int PredefinedEntityName(byte[] b, int ptr, int end);
        internal abstract void UpdatePosition(byte[] b, int ptr, int end, ref POSITION pos);
        internal abstract bool IsPublicId(byte[] b, int ptr, int end, ref int badPtr);
        internal abstract XML_Convert_Result Utf8Convert(byte[] from, ref int fromP, int fromLim, byte[] to, ref int toP, int toLim);

        internal int XmlPrologTok(byte[] b, int ptr, int end, ref int nextTokPtr) =>
            Tok(XmlTok_.XML_PROLOG_STATE, b, ptr, end, ref nextTokPtr);
        internal int XmlContentTok(byte[] b, int ptr, int end, ref int nextTokPtr) =>
            Tok(XmlTok_.XML_CONTENT_STATE, b, ptr, end, ref nextTokPtr);
        internal int XmlCdataSectionTok(byte[] b, int ptr, int end, ref int nextTokPtr) =>
            Tok(XmlTok_.XML_CDATA_SECTION_STATE, b, ptr, end, ref nextTokPtr);
        internal int XmlIgnoreSectionTok(byte[] b, int ptr, int end, ref int nextTokPtr) =>
            Tok(XmlTok_.XML_IGNORE_SECTION_STATE, b, ptr, end, ref nextTokPtr);
        internal int XmlAttributeValueTok(byte[] b, int ptr, int end, ref int nextTokPtr) =>
            XmlLiteralTok(XmlTok_.XML_ATTRIBUTE_VALUE_LITERAL, b, ptr, end, ref nextTokPtr);
        internal int XmlEntityValueTok(byte[] b, int ptr, int end, ref int nextTokPtr) =>
            XmlLiteralTok(XmlTok_.XML_ENTITY_VALUE_LITERAL, b, ptr, end, ref nextTokPtr);
    }

    // short alias used by ENCODING's helpers (XmlTok is also a method name there)
    internal static class XmlTok_
    {
        internal const int XML_PROLOG_STATE = 0, XML_CONTENT_STATE = 1, XML_CDATA_SECTION_STATE = 2,
            XML_IGNORE_SECTION_STATE = 3;
        internal const int XML_ATTRIBUTE_VALUE_LITERAL = 0, XML_ENTITY_VALUE_LITERAL = 1;
    }

    /// <summary>
    /// struct normal_encoding: the byte type table and the isName2..isInvalid4 entries
    /// (NORMAL_VTABLE(utf8_) by default; latin1/ascii/UTF-16 tables never produce BT_LEAD2..3, so
    /// their NULL_VTABLE entries are never reached).
    /// </summary>
    internal abstract class NormalEncoding : ENCODING
    {
        internal readonly byte[] type = new byte[256];

        internal virtual bool IsName(int n, byte[] b, int p) => n switch
        {
            2 => XmlTok.UTF8_GET_NAMING2(XmlTok.namePages, b, p),   // utf8_isName2
            3 => XmlTok.UTF8_GET_NAMING3(XmlTok.namePages, b, p),   // utf8_isName3
            _ => false,                                              // utf8_isName4 = isNever
        };

        internal virtual bool IsNmstrt(int n, byte[] b, int p) => n switch
        {
            2 => XmlTok.UTF8_GET_NAMING2(XmlTok.nmstrtPages, b, p), // utf8_isNmstrt2
            3 => XmlTok.UTF8_GET_NAMING3(XmlTok.nmstrtPages, b, p), // utf8_isNmstrt3
            _ => false,                                              // utf8_isNmstrt4 = isNever
        };

        internal virtual bool IsInvalid(int n, byte[] b, int p) => n switch
        {
            2 => XmlTok.UTF8_INVALID2(b, p),
            3 => XmlTok.UTF8_INVALID3(b, p),
            _ => XmlTok.UTF8_INVALID4(b, p),
        };
    }

    internal enum ConvKind { Utf8, Latin1, Ascii, Little2, Big2 }

    /// <summary>A normal_encoding whose vtable is VTABLE1 of PREFIX = normal_/little2_/big2_.</summary>
    internal class NormalEncodingImpl<P> : NormalEncoding where P : struct, IEncPolicy
    {
        internal ConvKind conv;

        internal override int Tok(int state, byte[] b, int ptr, int end, ref int nextTokPtr)
        {
            switch (state)
            {
                case 0: return XmlTokImpl<P>.prologTok(this, b, ptr, end, ref nextTokPtr);
                case 1: return XmlTokImpl<P>.contentTok(this, b, ptr, end, ref nextTokPtr);
                case 2: return XmlTokImpl<P>.cdataSectionTok(this, b, ptr, end, ref nextTokPtr);
                default: return XmlTokImpl<P>.ignoreSectionTok(this, b, ptr, end, ref nextTokPtr);
            }
        }

        internal override int XmlLiteralTok(int literalType, byte[] b, int ptr, int end, ref int nextTokPtr) =>
            literalType == 0
                ? XmlTokImpl<P>.attributeValueTok(this, b, ptr, end, ref nextTokPtr)
                : XmlTokImpl<P>.entityValueTok(this, b, ptr, end, ref nextTokPtr);

        internal override bool NameMatchesAscii(byte[] b, int ptr1, int end1, byte[] ptr2) =>
            XmlTokImpl<P>.nameMatchesAscii(b, ptr1, end1, ptr2);
        internal override int NameLength(byte[] b, int ptr) => XmlTokImpl<P>.nameLength(this, b, ptr);
        internal override int SkipS(byte[] b, int ptr) => XmlTokImpl<P>.skipS(this, b, ptr);
        internal override int GetAtts(byte[] b, int ptr, int attsMax, ATTRIBUTE[] atts) =>
            XmlTokImpl<P>.getAtts(this, b, ptr, attsMax, atts);
        internal override int CharRefNumber(byte[] b, int ptr) => XmlTokImpl<P>.charRefNumber(b, ptr);
        internal override int PredefinedEntityName(byte[] b, int ptr, int end) =>
            XmlTokImpl<P>.predefinedEntityName(b, ptr, end);
        internal override void UpdatePosition(byte[] b, int ptr, int end, ref POSITION pos) =>
            XmlTokImpl<P>.updatePosition(this, b, ptr, end, ref pos);
        internal override bool IsPublicId(byte[] b, int ptr, int end, ref int badPtr) =>
            XmlTokImpl<P>.isPublicId(this, b, ptr, end, ref badPtr);

        internal override XML_Convert_Result Utf8Convert(byte[] from, ref int fromP, int fromLim, byte[] to, ref int toP, int toLim)
        {
            switch (conv)
            {
                case ConvKind.Utf8: return Expat.XmlTok.utf8_toUtf8(from, ref fromP, fromLim, to, ref toP, toLim);
                case ConvKind.Latin1: return Expat.XmlTok.latin1_toUtf8(from, ref fromP, fromLim, to, ref toP, toLim);
                case ConvKind.Ascii: return Expat.XmlTok.ascii_toUtf8(from, ref fromP, fromLim, to, ref toP, toLim);
                case ConvKind.Little2: return Expat.XmlTok.utf16_toUtf8(false, from, ref fromP, fromLim, to, ref toP, toLim);
                default: return Expat.XmlTok.utf16_toUtf8(true, from, ref fromP, fromLim, to, ref toP, toLim);
            }
        }
    }

    /// <summary>CONVERTER: int (*)(void *userData, const char *p)</summary>
    internal delegate int CONVERTER(object userData, byte[] b, int p);

    /// <summary>struct unknown_encoding</summary>
    internal sealed class UnknownEncoding : NormalEncodingImpl<NormalPolicy>
    {
        internal CONVERTER convert;
        internal object userData;
        internal readonly ushort[] utf16 = new ushort[256];
        internal readonly byte[][] utf8 = new byte[256][];

        // unknown_isName
        internal override bool IsName(int n, byte[] b, int p)
        {
            if (convert == null)
                return base.IsName(n, b, p);
            int c = convert(userData, b, p);
            if ((c & ~0xFFFF) != 0)
                return false;
            return Expat.XmlTok.UCS2_GET_NAMING(Expat.XmlTok.namePages, c >> 8, c & 0xFF);
        }

        // unknown_isNmstrt
        internal override bool IsNmstrt(int n, byte[] b, int p)
        {
            if (convert == null)
                return base.IsNmstrt(n, b, p);
            int c = convert(userData, b, p);
            if ((c & ~0xFFFF) != 0)
                return false;
            return Expat.XmlTok.UCS2_GET_NAMING(Expat.XmlTok.nmstrtPages, c >> 8, c & 0xFF);
        }

        // unknown_isInvalid
        internal override bool IsInvalid(int n, byte[] b, int p)
        {
            if (convert == null)
                return base.IsInvalid(n, b, p);
            int c = convert(userData, b, p);
            return (c & ~0xFFFF) != 0 || Expat.XmlTok.checkCharRefNumber(c) < 0;
        }

        // unknown_toUtf8
        internal override XML_Convert_Result Utf8Convert(byte[] from, ref int fromP, int fromLim, byte[] to, ref int toP, int toLim)
        {
            byte[] buf = new byte[XmlTok.XML_UTF8_ENCODE_MAX];
            for (; ; )
            {
                byte[] utf8b;
                int off;
                int n;
                if (fromP == fromLim)
                    return XML_Convert_Result.XML_CONVERT_COMPLETED;
                utf8b = utf8[from[fromP]];
                n = utf8b[0];
                off = 1;
                if (n == 0)
                {
                    int c = convert(userData, from, fromP);
                    n = Expat.XmlTok.XmlUtf8Encode(c, buf, 0);
                    if (n > toLim - toP)
                        return XML_Convert_Result.XML_CONVERT_OUTPUT_EXHAUSTED;
                    utf8b = buf;
                    off = 0;
                    fromP += (type[from[fromP]] - (XmlTok.BT_LEAD2 - 2));
                }
                else
                {
                    if (n > toLim - toP)
                        return XML_Convert_Result.XML_CONVERT_OUTPUT_EXHAUSTED;
                    fromP++;
                }
                Array.Copy(utf8b, off, to, toP, n);
                toP += n;
            }
        }
    }

    /// <summary>
    /// INIT_ENCODING: the encoding used before the encoding is known. Its prolog and content scanners
    /// are initScanProlog/initScanContent, which detect the encoding and store it through encPtr.
    /// </summary>
    internal sealed class INIT_ENCODING : ENCODING
    {
        internal bool ns;
        internal int encIndex;               // INIT_ENC_INDEX (stored in isUtf16 in C)
        internal Action<ENCODING> encPtr;    // *encPtr = ...

        internal override int Tok(int state, byte[] b, int ptr, int end, ref int nextTokPtr)
        {
            if (state == XmlTok_.XML_PROLOG_STATE || state == XmlTok_.XML_CONTENT_STATE)
                return Expat.XmlTok.initScan(ns ? Expat.XmlTok.encodingsNS : Expat.XmlTok.encodings, this, state, b, ptr, end, ref nextTokPtr);
            throw new InvalidOperationException("INIT_ENCODING scanner");
        }

        // initUpdatePosition
        internal override void UpdatePosition(byte[] b, int ptr, int end, ref POSITION pos) =>
            XmlTokImpl<NormalPolicy>.updatePosition(Expat.XmlTok.utf8_encoding, b, ptr, end, ref pos);

        // The other entries are never used before the encoding is detected.
        internal override int XmlLiteralTok(int literalType, byte[] b, int ptr, int end, ref int nextTokPtr) => throw new InvalidOperationException();
        internal override bool NameMatchesAscii(byte[] b, int ptr1, int end1, byte[] ptr2) => throw new InvalidOperationException();
        internal override int NameLength(byte[] b, int ptr) => throw new InvalidOperationException();
        internal override int SkipS(byte[] b, int ptr) => throw new InvalidOperationException();
        internal override int GetAtts(byte[] b, int ptr, int attsMax, ATTRIBUTE[] atts) => throw new InvalidOperationException();
        internal override int CharRefNumber(byte[] b, int ptr) => throw new InvalidOperationException();
        internal override int PredefinedEntityName(byte[] b, int ptr, int end) => throw new InvalidOperationException();
        internal override bool IsPublicId(byte[] b, int ptr, int end, ref int badPtr) => throw new InvalidOperationException();
        internal override XML_Convert_Result Utf8Convert(byte[] from, ref int fromP, int fromLim, byte[] to, ref int toP, int toLim) => throw new InvalidOperationException();
    }

    /// <summary>
    /// Modules/expat/xmltok.c and xmltok_ns.c (expat 2.8.5): the encodings, the conversions to UTF-8,
    /// the XML declaration parser and the encoding detection. The library is compiled with XML_NS
    /// and XML_DTD as CPython builds it; the _ns encodings are the ones whose ':' is BT_COLON.
    /// </summary>
    internal static partial class XmlTok
    {
        // xmltok.h token codes
        internal const int XML_TOK_TRAILING_RSQB = -5;
        internal const int XML_TOK_NONE = -4;
        internal const int XML_TOK_TRAILING_CR = -3;
        internal const int XML_TOK_PARTIAL_CHAR = -2;
        internal const int XML_TOK_PARTIAL = -1;
        internal const int XML_TOK_INVALID = 0;
        internal const int XML_TOK_START_TAG_WITH_ATTS = 1;
        internal const int XML_TOK_START_TAG_NO_ATTS = 2;
        internal const int XML_TOK_EMPTY_ELEMENT_WITH_ATTS = 3;
        internal const int XML_TOK_EMPTY_ELEMENT_NO_ATTS = 4;
        internal const int XML_TOK_END_TAG = 5;
        internal const int XML_TOK_DATA_CHARS = 6;
        internal const int XML_TOK_DATA_NEWLINE = 7;
        internal const int XML_TOK_CDATA_SECT_OPEN = 8;
        internal const int XML_TOK_ENTITY_REF = 9;
        internal const int XML_TOK_CHAR_REF = 10;
        internal const int XML_TOK_PI = 11;
        internal const int XML_TOK_XML_DECL = 12;
        internal const int XML_TOK_COMMENT = 13;
        internal const int XML_TOK_BOM = 14;
        internal const int XML_TOK_PROLOG_S = 15;
        internal const int XML_TOK_DECL_OPEN = 16;
        internal const int XML_TOK_DECL_CLOSE = 17;
        internal const int XML_TOK_NAME = 18;
        internal const int XML_TOK_NMTOKEN = 19;
        internal const int XML_TOK_POUND_NAME = 20;
        internal const int XML_TOK_OR = 21;
        internal const int XML_TOK_PERCENT = 22;
        internal const int XML_TOK_OPEN_PAREN = 23;
        internal const int XML_TOK_CLOSE_PAREN = 24;
        internal const int XML_TOK_OPEN_BRACKET = 25;
        internal const int XML_TOK_CLOSE_BRACKET = 26;
        internal const int XML_TOK_LITERAL = 27;
        internal const int XML_TOK_PARAM_ENTITY_REF = 28;
        internal const int XML_TOK_INSTANCE_START = 29;
        internal const int XML_TOK_NAME_QUESTION = 30;
        internal const int XML_TOK_NAME_ASTERISK = 31;
        internal const int XML_TOK_NAME_PLUS = 32;
        internal const int XML_TOK_COND_SECT_OPEN = 33;
        internal const int XML_TOK_COND_SECT_CLOSE = 34;
        internal const int XML_TOK_CLOSE_PAREN_QUESTION = 35;
        internal const int XML_TOK_CLOSE_PAREN_ASTERISK = 36;
        internal const int XML_TOK_CLOSE_PAREN_PLUS = 37;
        internal const int XML_TOK_COMMA = 38;
        internal const int XML_TOK_ATTRIBUTE_VALUE_S = 39;
        internal const int XML_TOK_CDATA_SECT_CLOSE = 40;
        internal const int XML_TOK_PREFIXED_NAME = 41;
        internal const int XML_TOK_IGNORE_SECT = 42;

        internal const int XML_N_STATES = 4;
        internal const int XML_PROLOG_STATE = 0;
        internal const int XML_CONTENT_STATE = 1;
        internal const int XML_CDATA_SECTION_STATE = 2;
        internal const int XML_IGNORE_SECTION_STATE = 3;

        internal const int XML_UTF8_ENCODE_MAX = 4;
        internal const int XML_UTF16_ENCODE_MAX = 2;

        // xmltok_impl.h byte types
        internal const int BT_NONXML = 0, BT_MALFORM = 1, BT_LT = 2, BT_AMP = 3, BT_RSQB = 4, BT_LEAD2 = 5,
            BT_LEAD3 = 6, BT_LEAD4 = 7, BT_TRAIL = 8, BT_CR = 9, BT_LF = 10, BT_GT = 11, BT_QUOT = 12,
            BT_APOS = 13, BT_EQUALS = 14, BT_QUEST = 15, BT_EXCL = 16, BT_SOL = 17, BT_SEMI = 18, BT_NUM = 19,
            BT_LSQB = 20, BT_S = 21, BT_NMSTRT = 22, BT_COLON = 23, BT_HEX = 24, BT_DIGIT = 25, BT_NAME = 26,
            BT_MINUS = 27, BT_OTHER = 28, BT_NONASCII = 29, BT_PERCNT = 30, BT_LPAR = 31, BT_RPAR = 32,
            BT_AST = 33, BT_PLUS = 34, BT_COMMA = 35, BT_VERBAR = 36;

        // UCS2_GET_NAMING
        internal static bool UCS2_GET_NAMING(byte[] pages, int hi, int lo) =>
            (namingBitmap[(pages[hi] << 3) + (lo >> 5)] & (1u << (lo & 0x1F))) != 0;

        // UTF8_GET_NAMING2
        internal static bool UTF8_GET_NAMING2(byte[] pages, byte[] b, int p) =>
            (namingBitmap[(pages[(b[p] >> 2) & 7] << 3) + ((b[p] & 3) << 1) + ((b[p + 1] >> 5) & 1)]
             & (1u << (b[p + 1] & 0x1F))) != 0;

        // UTF8_GET_NAMING3
        internal static bool UTF8_GET_NAMING3(byte[] pages, byte[] b, int p) =>
            (namingBitmap[(pages[((b[p] & 0xF) << 4) + ((b[p + 1] >> 2) & 0xF)] << 3)
                          + ((b[p + 1] & 3) << 1) + ((b[p + 2] >> 5) & 1)]
             & (1u << (b[p + 2] & 0x1F))) != 0;

        // UTF8_INVALID2
        internal static bool UTF8_INVALID2(byte[] b, int p) =>
            b[p] < 0xC2 || (b[p + 1] & 0x80) == 0 || (b[p + 1] & 0xC0) == 0xC0;

        // UTF8_INVALID3
        internal static bool UTF8_INVALID3(byte[] b, int p) =>
            (b[p + 2] & 0x80) == 0
            || (b[p] == 0xEF && b[p + 1] == 0xBF ? b[p + 2] > 0xBD : (b[p + 2] & 0xC0) == 0xC0)
            || (b[p] == 0xE0
                ? b[p + 1] < 0xA0 || (b[p + 1] & 0xC0) == 0xC0
                : (b[p + 1] & 0x80) == 0 || (b[p] == 0xED ? b[p + 1] > 0x9F : (b[p + 1] & 0xC0) == 0xC0));

        // UTF8_INVALID4
        internal static bool UTF8_INVALID4(byte[] b, int p) =>
            (b[p + 3] & 0x80) == 0 || (b[p + 3] & 0xC0) == 0xC0 || (b[p + 2] & 0x80) == 0
            || (b[p + 2] & 0xC0) == 0xC0
            || (b[p] == 0xF0
                ? b[p + 1] < 0x90 || (b[p + 1] & 0xC0) == 0xC0
                : (b[p + 1] & 0x80) == 0 || (b[p] == 0xF4 ? b[p + 1] > 0x8F : (b[p + 1] & 0xC0) == 0xC0));

        /// <summary>unicode_byte_type</summary>
        internal static int unicode_byte_type(byte hi, byte lo)
        {
            switch (hi)
            {
                /* 0xD800-0xDBFF first 16-bit code unit or high surrogate (W1) */
                case 0xD8:
                case 0xD9:
                case 0xDA:
                case 0xDB:
                    return BT_LEAD4;
                /* 0xDC00-0xDFFF second 16-bit code unit or low surrogate (W2) */
                case 0xDC:
                case 0xDD:
                case 0xDE:
                case 0xDF:
                    return BT_TRAIL;
                case 0xFF:
                    switch (lo)
                    {
                        case 0xFF: /* noncharacter-FFFF */
                        case 0xFE: /* noncharacter-FFFE */
                            return BT_NONXML;
                    }
                    break;
            }
            return BT_NONASCII;
        }

        // ------------------------------------------------------------------ the encodings

        private static byte[] MakeTable(byte[] low, byte[] high, bool ns)
        {
            var t = new byte[256];
            Array.Copy(low, t, 128);
            if (!ns)
                t[':'] = BT_NMSTRT; // #define BT_COLON BT_NMSTRT
            if (high != null)
                Array.Copy(high, 0, t, 128, 128);
            // else: BT_NONXML == 0
            return t;
        }

        private static NormalEncodingImpl<P> Make<P>(byte[] low, byte[] high, bool ns, ConvKind conv, int minBpc, bool isUtf8, bool isUtf16)
            where P : struct, IEncPolicy
        {
            var e = new NormalEncodingImpl<P> { conv = conv, minBytesPerChar = minBpc, isUtf8 = isUtf8, isUtf16 = isUtf16 };
            Array.Copy(MakeTable(low, high, ns), e.type, 256);
            return e;
        }

        // BYTEORDER == 1234: little2 is the native UTF-16

        internal static NormalEncoding utf8_encoding_ns => Encodings.utf8_encoding_ns;
        internal static NormalEncoding utf8_encoding => Encodings.utf8_encoding;
        internal static NormalEncoding internal_utf8_encoding_ns => Encodings.internal_utf8_encoding_ns;
        internal static NormalEncoding internal_utf8_encoding => Encodings.internal_utf8_encoding;
        internal static NormalEncoding latin1_encoding_ns => Encodings.latin1_encoding_ns;
        internal static NormalEncoding latin1_encoding => Encodings.latin1_encoding;
        internal static NormalEncoding ascii_encoding_ns => Encodings.ascii_encoding_ns;
        internal static NormalEncoding ascii_encoding => Encodings.ascii_encoding;
        internal static NormalEncoding little2_encoding_ns => Encodings.little2_encoding_ns;
        internal static NormalEncoding little2_encoding => Encodings.little2_encoding;
        internal static NormalEncoding big2_encoding_ns => Encodings.big2_encoding_ns;
        internal static NormalEncoding big2_encoding => Encodings.big2_encoding;
        internal static ENCODING[] encodings => Encodings.encodings;
        internal static ENCODING[] encodingsNS => Encodings.encodingsNS;

        // The encodings are made after the byte type tables (XmlTokTables.g.cs) exist.
        private static class Encodings
        {
            internal static readonly NormalEncoding utf8_encoding_ns = Make<NormalPolicy>(AsciiTab, Utf8Tab, true, ConvKind.Utf8, 1, true, false);
            internal static readonly NormalEncoding utf8_encoding = Make<NormalPolicy>(AsciiTab, Utf8Tab, false, ConvKind.Utf8, 1, true, false);
            internal static readonly NormalEncoding internal_utf8_encoding_ns = Make<NormalPolicy>(IAsciiTab, Utf8Tab, true, ConvKind.Utf8, 1, true, false);
            internal static readonly NormalEncoding internal_utf8_encoding = Make<NormalPolicy>(IAsciiTab, Utf8Tab, false, ConvKind.Utf8, 1, true, false);
            internal static readonly NormalEncoding latin1_encoding_ns = Make<NormalPolicy>(AsciiTab, Latin1Tab, true, ConvKind.Latin1, 1, false, false);
            internal static readonly NormalEncoding latin1_encoding = Make<NormalPolicy>(AsciiTab, Latin1Tab, false, ConvKind.Latin1, 1, false, false);
            internal static readonly NormalEncoding ascii_encoding_ns = Make<NormalPolicy>(AsciiTab, null, true, ConvKind.Ascii, 1, true, false);
            internal static readonly NormalEncoding ascii_encoding = Make<NormalPolicy>(AsciiTab, null, false, ConvKind.Ascii, 1, true, false);
            internal static readonly NormalEncoding little2_encoding_ns = Make<Little2Policy>(AsciiTab, Latin1Tab, true, ConvKind.Little2, 2, false, true);
            internal static readonly NormalEncoding little2_encoding = Make<Little2Policy>(AsciiTab, Latin1Tab, false, ConvKind.Little2, 2, false, true);
            internal static readonly NormalEncoding big2_encoding_ns = Make<Big2Policy>(AsciiTab, Latin1Tab, true, ConvKind.Big2, 2, false, false);
            internal static readonly NormalEncoding big2_encoding = Make<Big2Policy>(AsciiTab, Latin1Tab, false, ConvKind.Big2, 2, false, false);

        // NS(encodings)
            internal static readonly ENCODING[] encodings =
            {
                latin1_encoding, ascii_encoding, utf8_encoding, big2_encoding, big2_encoding, little2_encoding,
                utf8_encoding /* NO_ENC */
            };

            internal static readonly ENCODING[] encodingsNS =
            {
                latin1_encoding_ns, ascii_encoding_ns, utf8_encoding_ns, big2_encoding_ns, big2_encoding_ns,
                little2_encoding_ns, utf8_encoding_ns /* NO_ENC */
            };
        }

        /* If this enumeration is changed, getEncodingIndex and encodings must also be changed. */
        internal const int UNKNOWN_ENC = -1, ISO_8859_1_ENC = 0, US_ASCII_ENC = 1, UTF_8_ENC = 2, UTF_16_ENC = 3,
            UTF_16BE_ENC = 4, UTF_16LE_ENC = 5, NO_ENC = 6;


        /// <summary>NS(XmlGetUtf8InternalEncoding)</summary>
        internal static ENCODING XmlGetUtf8InternalEncoding() => internal_utf8_encoding;
        internal static ENCODING XmlGetUtf8InternalEncodingNS() => internal_utf8_encoding_ns;

        // ------------------------------------------------------------------ conversions

        /// <summary>_INTERNAL_trim_to_complete_utf8_characters</summary>
        internal static void _INTERNAL_trim_to_complete_utf8_characters(byte[] b, int from, ref int fromLimRef)
        {
            int fromLim = fromLimRef;
            int walked = 0;
            for (; fromLim > from; fromLim--, walked++)
            {
                byte prev = b[fromLim - 1];
                if ((prev & 0xf8u) == 0xf0u)
                { /* 4-byte character, lead by 0b11110xxx byte */
                    if (walked + 1 >= 4)
                    {
                        fromLim += 4 - 1;
                        break;
                    }
                    else
                        walked = 0;
                }
                else if ((prev & 0xf0u) == 0xe0u)
                { /* 3-byte character, lead by 0b1110xxxx byte */
                    if (walked + 1 >= 3)
                    {
                        fromLim += 3 - 1;
                        break;
                    }
                    else
                        walked = 0;
                }
                else if ((prev & 0xe0u) == 0xc0u)
                { /* 2-byte character, lead by 0b110xxxxx byte */
                    if (walked + 1 >= 2)
                    {
                        fromLim += 2 - 1;
                        break;
                    }
                    else
                        walked = 0;
                }
                else if ((prev & 0x80u) == 0x00u)
                { /* 1-byte character, matching 0b0xxxxxxx */
                    break;
                }
            }
            fromLimRef = fromLim;
        }

        /// <summary>utf8_toUtf8</summary>
        internal static XML_Convert_Result utf8_toUtf8(byte[] from, ref int fromP, int fromLim, byte[] to, ref int toP, int toLim)
        {
            bool input_incomplete = false;
            bool output_exhausted = false;

            /* Avoid copying partial characters (due to limited space). */
            int bytesAvailable = fromLim - fromP;
            int bytesStorable = toLim - toP;
            if (bytesAvailable > bytesStorable)
            {
                fromLim = fromP + bytesStorable;
                output_exhausted = true;
            }

            /* Avoid copying partial characters (from incomplete input). */
            {
                int fromLimBefore = fromLim;
                _INTERNAL_trim_to_complete_utf8_characters(from, fromP, ref fromLim);
                if (fromLim < fromLimBefore)
                    input_incomplete = true;
            }

            {
                int bytesToCopy = fromLim - fromP;
                Buffer.BlockCopy(from, fromP, to, toP, bytesToCopy);
                fromP += bytesToCopy;
                toP += bytesToCopy;
            }

            if (output_exhausted) /* needs to go first */
                return XML_Convert_Result.XML_CONVERT_OUTPUT_EXHAUSTED;
            else if (input_incomplete)
                return XML_Convert_Result.XML_CONVERT_INPUT_INCOMPLETE;
            else
                return XML_Convert_Result.XML_CONVERT_COMPLETED;
        }

        /// <summary>latin1_toUtf8</summary>
        internal static XML_Convert_Result latin1_toUtf8(byte[] from, ref int fromP, int fromLim, byte[] to, ref int toP, int toLim)
        {
            for (; ; )
            {
                byte c;
                if (fromP == fromLim)
                    return XML_Convert_Result.XML_CONVERT_COMPLETED;
                c = from[fromP];
                if ((c & 0x80) != 0)
                {
                    if (toLim - toP < 2)
                        return XML_Convert_Result.XML_CONVERT_OUTPUT_EXHAUSTED;
                    to[toP++] = (byte)((c >> 6) | 0xc0);
                    to[toP++] = (byte)((c & 0x3f) | 0x80);
                    fromP++;
                }
                else
                {
                    if (toP == toLim)
                        return XML_Convert_Result.XML_CONVERT_OUTPUT_EXHAUSTED;
                    to[toP++] = from[fromP++];
                }
            }
        }

        /// <summary>ascii_toUtf8</summary>
        internal static XML_Convert_Result ascii_toUtf8(byte[] from, ref int fromP, int fromLim, byte[] to, ref int toP, int toLim)
        {
            while (fromP < fromLim && toP < toLim)
                to[toP++] = from[fromP++];

            if ((toP == toLim) && (fromP < fromLim))
                return XML_Convert_Result.XML_CONVERT_OUTPUT_EXHAUSTED;
            else
                return XML_Convert_Result.XML_CONVERT_COMPLETED;
        }

        /// <summary>DEFINE_UTF16_TO_UTF8(little2_) / (big2_)</summary>
        internal static XML_Convert_Result utf16_toUtf8(bool big, byte[] b, ref int fromP, int fromLim, byte[] to, ref int toP, int toLim)
        {
            int from = fromP;
            int loOff = big ? 1 : 0, hiOff = big ? 0 : 1;
            fromLim = from + (((fromLim - from) >> 1) << 1); /* shrink to even */
            for (; from < fromLim; from += 2)
            {
                int plane;
                byte lo2;
                byte lo = b[from + loOff];
                byte hi = b[from + hiOff];
                switch (hi)
                {
                    case 0:
                        if (lo < 0x80)
                        {
                            if (toP == toLim)
                            {
                                fromP = from;
                                return XML_Convert_Result.XML_CONVERT_OUTPUT_EXHAUSTED;
                            }
                            to[toP++] = lo;
                            break;
                        }
                        goto case 0x1;
                    case 0x1:
                    case 0x2:
                    case 0x3:
                    case 0x4:
                    case 0x5:
                    case 0x6:
                    case 0x7:
                        if (toLim - toP < 2)
                        {
                            fromP = from;
                            return XML_Convert_Result.XML_CONVERT_OUTPUT_EXHAUSTED;
                        }
                        to[toP++] = (byte)((lo >> 6) | (hi << 2) | 0xc0);
                        to[toP++] = (byte)((lo & 0x3f) | 0x80);
                        break;
                    default:
                        if (toLim - toP < 3)
                        {
                            fromP = from;
                            return XML_Convert_Result.XML_CONVERT_OUTPUT_EXHAUSTED;
                        }
                        /* 16 bits divided 4, 6, 6 amongst 3 bytes */
                        to[toP++] = (byte)((hi >> 4) | 0xe0);
                        to[toP++] = (byte)(((hi & 0xf) << 2) | (lo >> 6) | 0x80);
                        to[toP++] = (byte)((lo & 0x3f) | 0x80);
                        break;
                    case 0xD8:
                    case 0xD9:
                    case 0xDA:
                    case 0xDB:
                        if (toLim - toP < 4)
                        {
                            fromP = from;
                            return XML_Convert_Result.XML_CONVERT_OUTPUT_EXHAUSTED;
                        }
                        if (fromLim - from < 4)
                        {
                            fromP = from;
                            return XML_Convert_Result.XML_CONVERT_INPUT_INCOMPLETE;
                        }
                        plane = (((hi & 0x3) << 2) | ((lo >> 6) & 0x3)) + 1;
                        to[toP++] = (byte)((plane >> 2) | 0xf0);
                        to[toP++] = (byte)(((lo >> 2) & 0xF) | ((plane & 0x3) << 4) | 0x80);
                        from += 2;
                        lo2 = b[from + loOff];
                        to[toP++] = (byte)(((lo & 0x3) << 4) | ((b[from + hiOff] & 0x3) << 2) | (lo2 >> 6) | 0x80);
                        to[toP++] = (byte)((lo2 & 0x3f) | 0x80);
                        break;
                }
            }
            fromP = from;
            if (from < fromLim)
                return XML_Convert_Result.XML_CONVERT_INPUT_INCOMPLETE;
            else
                return XML_Convert_Result.XML_CONVERT_COMPLETED;
        }

        /// <summary>checkCharRefNumber</summary>
        internal static int checkCharRefNumber(int result)
        {
            switch (result >> 8)
            {
                case 0xD8:
                case 0xD9:
                case 0xDA:
                case 0xDB:
                case 0xDC:
                case 0xDD:
                case 0xDE:
                case 0xDF:
                    return -1;
                case 0:
                    if (latin1_encoding.type[result] == BT_NONXML)
                        return -1;
                    break;
                case 0xFF:
                    if (result == 0xFFFE || result == 0xFFFF)
                        return -1;
                    break;
            }
            return result;
        }

        /// <summary>XmlUtf8Encode</summary>
        internal static int XmlUtf8Encode(int c, byte[] buf, int off)
        {
            const int min2 = 0x80, min3 = 0x800, min4 = 0x10000;
            if (c < 0)
                return 0; /* LCOV_EXCL_LINE */
            if (c < min2)
            {
                buf[off] = (byte)c;
                return 1;
            }
            if (c < min3)
            {
                buf[off] = (byte)((c >> 6) | 0xc0);
                buf[off + 1] = (byte)((c & 0x3f) | 0x80);
                return 2;
            }
            if (c < min4)
            {
                buf[off] = (byte)((c >> 12) | 0xe0);
                buf[off + 1] = (byte)(((c >> 6) & 0x3f) | 0x80);
                buf[off + 2] = (byte)((c & 0x3f) | 0x80);
                return 3;
            }
            if (c < 0x110000)
            {
                buf[off] = (byte)((c >> 18) | 0xf0);
                buf[off + 1] = (byte)(((c >> 12) & 0x3f) | 0x80);
                buf[off + 2] = (byte)(((c >> 6) & 0x3f) | 0x80);
                buf[off + 3] = (byte)((c & 0x3f) | 0x80);
                return 4;
            }
            return 0; /* LCOV_EXCL_LINE */
        }

        /// <summary>XmlInitUnknownEncoding: null when the table is not acceptable.</summary>
        internal static ENCODING XmlInitUnknownEncoding(int[] table, CONVERTER convert, object userData)
        {
            var e = new UnknownEncoding { conv = ConvKind.Latin1, minBytesPerChar = 1, isUtf8 = false, isUtf16 = false };
            // memcpy(mem, &latin1_encoding, sizeof(struct normal_encoding))
            Array.Copy(latin1_encoding.type, e.type, 256);
            for (int i = 0; i < 128; i++)
                if (latin1_encoding.type[i] != BT_OTHER && latin1_encoding.type[i] != BT_NONXML && table[i] != i)
                    return null;
            for (int i = 0; i < 256; i++)
            {
                int c = table[i];
                if (c == -1)
                {
                    e.type[i] = BT_MALFORM;
                    /* This shouldn't really get used. */
                    e.utf16[i] = 0xFFFF;
                    e.utf8[i] = new byte[] { 1, 0 };
                }
                else if (c < 0)
                {
                    if (c < -4)
                        return null;
                    /* Multi-byte sequences need a converter function */
                    if (convert == null)
                        return null;
                    e.type[i] = (byte)(BT_LEAD2 - (c + 2));
                    e.utf8[i] = new byte[] { 0 };
                    e.utf16[i] = 0;
                }
                else if (c < 0x80)
                {
                    if (latin1_encoding.type[c] != BT_OTHER && latin1_encoding.type[c] != BT_NONXML && c != i)
                        return null;
                    e.type[i] = latin1_encoding.type[c];
                    e.utf8[i] = new byte[] { 1, (byte)c };
                    e.utf16[i] = (ushort)(c == 0 ? 0xFFFF : c);
                }
                else if (checkCharRefNumber(c) < 0)
                {
                    e.type[i] = BT_NONXML;
                    /* This shouldn't really get used. */
                    e.utf16[i] = 0xFFFF;
                    e.utf8[i] = new byte[] { 1, 0 };
                }
                else
                {
                    if (c > 0xFFFF)
                        return null;
                    if (UCS2_GET_NAMING(nmstrtPages, c >> 8, c & 0xff))
                        e.type[i] = BT_NMSTRT;
                    else if (UCS2_GET_NAMING(namePages, c >> 8, c & 0xff))
                        e.type[i] = BT_NAME;
                    else
                        e.type[i] = BT_OTHER;
                    var u = new byte[5];
                    u[0] = (byte)XmlUtf8Encode(c, u, 1);
                    e.utf8[i] = u;
                    e.utf16[i] = (ushort)c;
                }
            }
            e.userData = userData;
            e.convert = convert;
            return e;
        }

        /// <summary>XmlInitUnknownEncodingNS</summary>
        internal static ENCODING XmlInitUnknownEncodingNS(int[] table, CONVERTER convert, object userData)
        {
            var enc = XmlInitUnknownEncoding(table, convert, userData);
            if (enc != null)
                ((NormalEncoding)enc).type[':'] = BT_COLON;
            return enc;
        }

        // ------------------------------------------------------------------ encoding names

        private static readonly byte[] KW_ISO_8859_1 = Ascii("ISO-8859-1");
        private static readonly byte[] KW_US_ASCII = Ascii("US-ASCII");
        private static readonly byte[] KW_UTF_8 = Ascii("UTF-8");
        private static readonly byte[] KW_UTF_16 = Ascii("UTF-16");
        private static readonly byte[] KW_UTF_16BE = Ascii("UTF-16BE");
        private static readonly byte[] KW_UTF_16LE = Ascii("UTF-16LE");

        internal static byte[] Ascii(string s)
        {
            var r = new byte[s.Length];
            for (int i = 0; i < s.Length; i++)
                r[i] = (byte)s[i];
            return r;
        }

        /// <summary>streqci: s1 is a NUL-terminated (or whole-array) name, s2 an upper-case keyword.</summary>
        private static bool streqci(byte[] s1, byte[] s2)
        {
            int i = 0;
            for (; ; i++)
            {
                int c1 = i < s1.Length ? (sbyte)s1[i] : 0;
                int c2 = i < s2.Length ? s2[i] : 0;
                if ('a' <= c1 && c1 <= 'z')
                    c1 += 'A' - 'a';
                if ('a' <= c2 && c2 <= 'z')
                    c2 += 'A' - 'a'; /* LCOV_EXCL_LINE */
                if (c1 != c2)
                    return false;
                if (c1 == 0)
                    break;
            }
            return true;
        }

        /// <summary>getEncodingIndex: name is a NUL-free UTF-8 string or null.</summary>
        internal static int getEncodingIndex(byte[] name)
        {
            byte[][] encodingNames = { KW_ISO_8859_1, KW_US_ASCII, KW_UTF_8, KW_UTF_16, KW_UTF_16BE, KW_UTF_16LE };
            if (name == null)
                return NO_ENC;
            for (int i = 0; i < encodingNames.Length; i++)
                if (streqci(name, encodingNames[i]))
                    return i;
            return UNKNOWN_ENC;
        }

        /// <summary>
        /// initScan: this is what detects the encoding. encodingTable maps from encoding indices to
        /// encodings; INIT_ENC_INDEX(enc) is the index of the external (protocol) specified encoding;
        /// state is XML_CONTENT_STATE if we're parsing an external text entity, and XML_PROLOG_STATE
        /// otherwise.
        /// </summary>
        internal static int initScan(ENCODING[] encodingTable, INIT_ENCODING enc, int state, byte[] b, int ptr, int end, ref int nextTokPtr)
        {
            if (ptr >= end)
                return XML_TOK_NONE;
            var encPtr = enc.encPtr;
            if (ptr + 1 == end)
            {
                /* only a single byte available for auto-detection */
                /* so we're parsing an external text entity... */
                /* if UTF-16 was externally specified, then we need at least 2 bytes */
                switch (enc.encIndex)
                {
                    case UTF_16_ENC:
                    case UTF_16LE_ENC:
                    case UTF_16BE_ENC:
                        return XML_TOK_PARTIAL;
                }
                switch (b[ptr])
                {
                    case 0xFE:
                    case 0xFF:
                    case 0xEF: /* possibly first byte of UTF-8 BOM */
                        if (enc.encIndex == ISO_8859_1_ENC && state == XML_CONTENT_STATE)
                            break;
                        return XML_TOK_PARTIAL;
                    case 0x00:
                    case 0x3C:
                        return XML_TOK_PARTIAL;
                }
            }
            else
            {
                switch ((b[ptr] << 8) | b[ptr + 1])
                {
                    case 0xFEFF:
                        if (enc.encIndex == ISO_8859_1_ENC && state == XML_CONTENT_STATE)
                            break;
                        nextTokPtr = ptr + 2;
                        encPtr(encodingTable[UTF_16BE_ENC]);
                        return XML_TOK_BOM;
                    /* 00 3C is handled in the default case */
                    case 0x3C00:
                        if ((enc.encIndex == UTF_16BE_ENC || enc.encIndex == UTF_16_ENC) && state == XML_CONTENT_STATE)
                            break;
                        encPtr(encodingTable[UTF_16LE_ENC]);
                        return encodingTable[UTF_16LE_ENC].Tok(state, b, ptr, end, ref nextTokPtr);
                    case 0xFFFE:
                        if (enc.encIndex == ISO_8859_1_ENC && state == XML_CONTENT_STATE)
                            break;
                        nextTokPtr = ptr + 2;
                        encPtr(encodingTable[UTF_16LE_ENC]);
                        return XML_TOK_BOM;
                    case 0xEFBB:
                        /* Maybe a UTF-8 BOM (EF BB BF) */
                        if (state == XML_CONTENT_STATE)
                        {
                            int e = enc.encIndex;
                            if (e == ISO_8859_1_ENC || e == UTF_16BE_ENC || e == UTF_16LE_ENC || e == UTF_16_ENC)
                                break;
                        }
                        if (ptr + 2 == end)
                            return XML_TOK_PARTIAL;
                        if (b[ptr + 2] == 0xBF)
                        {
                            nextTokPtr = ptr + 3;
                            encPtr(encodingTable[UTF_8_ENC]);
                            return XML_TOK_BOM;
                        }
                        break;
                    default:
                        if (b[ptr] == 0)
                        {
                            /* 0 isn't a legal data character. Furthermore a document entity can only
                               start with ASCII characters.  So the only way this can fail to be
                               big-endian UTF-16 if it it's an external parsed general entity that's
                               labelled as UTF-16LE. */
                            if (state == XML_CONTENT_STATE && enc.encIndex == UTF_16LE_ENC)
                                break;
                            encPtr(encodingTable[UTF_16BE_ENC]);
                            return encodingTable[UTF_16BE_ENC].Tok(state, b, ptr, end, ref nextTokPtr);
                        }
                        else if (b[ptr + 1] == 0)
                        {
                            if (state == XML_CONTENT_STATE)
                                break;
                            encPtr(encodingTable[UTF_16LE_ENC]);
                            return encodingTable[UTF_16LE_ENC].Tok(state, b, ptr, end, ref nextTokPtr);
                        }
                        break;
                }
            }
            encPtr(encodingTable[enc.encIndex]);
            return encodingTable[enc.encIndex].Tok(state, b, ptr, end, ref nextTokPtr);
        }

        /// <summary>NS(XmlInitEncoding): false for an unknown name.</summary>
        internal static bool XmlInitEncoding(INIT_ENCODING p, Action<ENCODING> encPtr, byte[] name, bool ns)
        {
            int i = getEncodingIndex(name);
            if (i == UNKNOWN_ENC)
                return false;
            p.encIndex = i;
            p.ns = ns;
            p.encPtr = encPtr;
            encPtr(p);
            return true;
        }

        /// <summary>toAscii</summary>
        private static int toAscii(ENCODING enc, byte[] b, int ptr, int end)
        {
            byte[] buf = new byte[1];
            int p = 0;
            enc.Utf8Convert(b, ref ptr, end, buf, ref p, 1);
            if (p == 0)
                return -1;
            else
                return (sbyte)buf[0];
        }

        /// <summary>isSpace</summary>
        private static bool isSpace(int c)
        {
            switch (c)
            {
                case 0x20:
                case 0xD:
                case 0xA:
                case 0x9:
                    return true;
            }
            return false;
        }

        /// <summary>
        /// parsePseudoAttribute: return true if there's just optional white space or there's an S
        /// followed by name=val. namePtr is -1 for "no name" (NULL).
        /// </summary>
        private static bool parsePseudoAttribute(ENCODING enc, byte[] b, int ptr, int end, out int namePtr,
            ref int nameEndPtr, ref int valPtr, ref int nextTokPtr)
        {
            int c;
            int open;
            if (ptr == end)
            {
                namePtr = -1;
                return true;
            }
            namePtr = -1;
            if (!isSpace(toAscii(enc, b, ptr, end)))
            {
                nextTokPtr = ptr;
                return false;
            }
            do
            {
                ptr += enc.minBytesPerChar;
            } while (isSpace(toAscii(enc, b, ptr, end)));
            if (ptr == end)
            {
                namePtr = -1;
                return true;
            }
            namePtr = ptr;
            for (; ; )
            {
                c = toAscii(enc, b, ptr, end);
                if (c == -1)
                {
                    nextTokPtr = ptr;
                    return false;
                }
                if (c == '=')
                {
                    nameEndPtr = ptr;
                    break;
                }
                if (isSpace(c))
                {
                    nameEndPtr = ptr;
                    do
                    {
                        ptr += enc.minBytesPerChar;
                    } while (isSpace(c = toAscii(enc, b, ptr, end)));
                    if (c != '=')
                    {
                        nextTokPtr = ptr;
                        return false;
                    }
                    break;
                }
                ptr += enc.minBytesPerChar;
            }
            if (ptr == namePtr)
            {
                nextTokPtr = ptr;
                return false;
            }
            ptr += enc.minBytesPerChar;
            c = toAscii(enc, b, ptr, end);
            while (isSpace(c))
            {
                ptr += enc.minBytesPerChar;
                c = toAscii(enc, b, ptr, end);
            }
            if (c != '"' && c != '\'')
            {
                nextTokPtr = ptr;
                return false;
            }
            open = c;
            ptr += enc.minBytesPerChar;
            valPtr = ptr;
            for (; ; ptr += enc.minBytesPerChar)
            {
                c = toAscii(enc, b, ptr, end);
                if (c == open)
                    break;
                if (!('a' <= c && c <= 'z') && !('A' <= c && c <= 'Z') && !('0' <= c && c <= '9') && c != '.'
                    && c != '-' && c != '_')
                {
                    nextTokPtr = ptr;
                    return false;
                }
            }
            nextTokPtr = ptr + enc.minBytesPerChar;
            return true;
        }

        private static readonly byte[] KW_version = Ascii("version");
        private static readonly byte[] KW_encoding = Ascii("encoding");
        private static readonly byte[] KW_standalone = Ascii("standalone");
        private static readonly byte[] KW_yes = Ascii("yes");
        private static readonly byte[] KW_no = Ascii("no");
        private static readonly byte[] KW_1_dot = Ascii("1.");

        /// <summary>
        /// checkXmlDeclVersionNum: checks a version pseudo-attribute value against the VersionNum
        /// production ("1." followed by one or more digits).
        /// </summary>
        private static bool checkXmlDeclVersionNum(ENCODING enc, byte[] b, int val, int valEnd)
        {
            if (valEnd - val < 2 * enc.minBytesPerChar
                || !enc.NameMatchesAscii(b, val, val + 2 * enc.minBytesPerChar, KW_1_dot))
                return false;
            val += 2 * enc.minBytesPerChar;
            if (val == valEnd)
                return false;
            for (; val != valEnd; val += enc.minBytesPerChar)
            {
                int c = toAscii(enc, b, val, valEnd);
                if (c < '0' || c > '9')
                    return false;
            }
            return true;
        }

        /// <summary>NS(findEncoding)</summary>
        private static ENCODING findEncoding(bool ns, ENCODING enc, byte[] b, int ptr, int end)
        {
            const int ENCODING_MAX = 128;
            byte[] buf = new byte[ENCODING_MAX];
            int p = 0;
            enc.Utf8Convert(b, ref ptr, end, buf, ref p, ENCODING_MAX - 1);
            if (ptr != end)
                return null;
            var name = new byte[p];
            Array.Copy(buf, name, p);
            if (streqci(name, KW_UTF_16) && enc.minBytesPerChar == 2)
                return enc;
            int i = getEncodingIndex(name);
            if (i == UNKNOWN_ENC)
                return null;
            return (ns ? encodingsNS : encodings)[i];
        }

        /// <summary>
        /// doParseXmlDecl / NS(XmlParseXmlDecl). Pointers that C leaves untouched stay as given;
        /// encodingName is -1 when there is none.
        /// </summary>
        internal static bool XmlParseXmlDecl(bool ns, bool isGeneralTextEntity, ENCODING enc, byte[] b, int ptr,
            int end, ref int badPtr, ref int versionPtr, ref int versionEndPtr, ref int encodingName,
            ref ENCODING encoding, ref int standalone)
        {
            int val = -1;
            int name;
            int nameEnd = -1;
            ptr += 5 * enc.minBytesPerChar;
            end -= 2 * enc.minBytesPerChar;
            if (!parsePseudoAttribute(enc, b, ptr, end, out name, ref nameEnd, ref val, ref ptr) || name < 0)
            {
                badPtr = ptr;
                return false;
            }
            if (!enc.NameMatchesAscii(b, name, nameEnd, KW_version))
            {
                if (!isGeneralTextEntity)
                {
                    badPtr = name;
                    return false;
                }
            }
            else
            {
                versionPtr = val;
                versionEndPtr = ptr;
                /* The version number must not be empty */
                if (val == ptr - enc.minBytesPerChar)
                {
                    badPtr = val;
                    return false;
                }
                if (!checkXmlDeclVersionNum(enc, b, val, ptr - enc.minBytesPerChar))
                {
                    badPtr = val;
                    return false;
                }
                if (!parsePseudoAttribute(enc, b, ptr, end, out name, ref nameEnd, ref val, ref ptr))
                {
                    badPtr = ptr;
                    return false;
                }
                if (name < 0)
                {
                    if (isGeneralTextEntity)
                    {
                        /* a TextDecl must have an EncodingDecl */
                        badPtr = ptr;
                        return false;
                    }
                    return true;
                }
            }
            if (enc.NameMatchesAscii(b, name, nameEnd, KW_encoding))
            {
                int c = toAscii(enc, b, val, end);
                if (!('a' <= c && c <= 'z') && !('A' <= c && c <= 'Z'))
                {
                    badPtr = val;
                    return false;
                }
                encodingName = val;
                encoding = findEncoding(ns, enc, b, val, ptr - enc.minBytesPerChar);
                if (!parsePseudoAttribute(enc, b, ptr, end, out name, ref nameEnd, ref val, ref ptr))
                {
                    badPtr = ptr;
                    return false;
                }
                if (name < 0)
                    return true;
            }
            if (!enc.NameMatchesAscii(b, name, nameEnd, KW_standalone) || isGeneralTextEntity)
            {
                badPtr = name;
                return false;
            }
            if (enc.NameMatchesAscii(b, val, ptr - enc.minBytesPerChar, KW_yes))
                standalone = 1;
            else if (enc.NameMatchesAscii(b, val, ptr - enc.minBytesPerChar, KW_no))
                standalone = 0;
            else
            {
                badPtr = val;
                return false;
            }
            while (isSpace(toAscii(enc, b, ptr, end)))
                ptr += enc.minBytesPerChar;
            if (ptr != end)
            {
                badPtr = ptr;
                return false;
            }
            return true;
        }
    }
}
