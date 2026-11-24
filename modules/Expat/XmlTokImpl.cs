using System.Runtime.CompilerServices;
using static SharpPy.Modules.Expat.XmlTok;

namespace SharpPy.Modules.Expat
{
    /// <summary>
    /// The macros xmltok_impl.c is compiled with (MINBPC, BYTE_TYPE, BYTE_TO_ASCII, CHAR_MATCHES,
    /// IS_NAME_CHAR, IS_NMSTRT_CHAR, IS_INVALID_CHAR and the _MINBPC forms). xmltok.c includes
    /// xmltok_impl.c three times (normal_, little2_, big2_); here XmlTokImpl is generic over a struct
    /// implementing this interface, so the JIT makes the same three specialised copies.
    /// </summary>
    internal interface IEncPolicy
    {
        int MINBPC { get; }
        int ByteType(NormalEncoding enc, byte[] b, int p);
        int ByteToAscii(byte[] b, int p);
        bool CharMatches(byte[] b, int p, int c);
        bool IsNameChar(NormalEncoding enc, byte[] b, int p, int n);
        bool IsNmstrtChar(NormalEncoding enc, byte[] b, int p, int n);
        bool IsInvalidChar(NormalEncoding enc, byte[] b, int p, int n);
        bool IsNameCharMinbpc(byte[] b, int p);
        bool IsNmstrtCharMinbpc(byte[] b, int p);
    }

    /// <summary>The single-byte macros of xmltok.c (normal_ prefix).</summary>
    internal struct NormalPolicy : IEncPolicy
    {
        public int MINBPC { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => 1; }
        // SB_BYTE_TYPE
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ByteType(NormalEncoding enc, byte[] b, int p) => enc.type[b[p]];
        // BYTE_TO_ASCII: *(p), a (signed) char
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ByteToAscii(byte[] b, int p) => (sbyte)b[p];
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CharMatches(byte[] b, int p, int c) => b[p] == c;
        public bool IsNameChar(NormalEncoding enc, byte[] b, int p, int n) => enc.IsName(n, b, p);
        public bool IsNmstrtChar(NormalEncoding enc, byte[] b, int p, int n) => enc.IsNmstrt(n, b, p);
        public bool IsInvalidChar(NormalEncoding enc, byte[] b, int p, int n) => enc.IsInvalid(n, b, p);
        public bool IsNameCharMinbpc(byte[] b, int p) => false;
        public bool IsNmstrtCharMinbpc(byte[] b, int p) => false;
    }

    /// <summary>LITTLE2_* macros of xmltok.c (little2_ prefix).</summary>
    internal struct Little2Policy : IEncPolicy
    {
        public int MINBPC { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => 2; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ByteType(NormalEncoding enc, byte[] b, int p) =>
            b[p + 1] == 0 ? enc.type[b[p]] : XmlTok.unicode_byte_type(b[p + 1], b[p]);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ByteToAscii(byte[] b, int p) => b[p + 1] == 0 ? (sbyte)b[p] : -1;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CharMatches(byte[] b, int p, int c) => b[p + 1] == 0 && b[p] == c;
        public bool IsNameChar(NormalEncoding enc, byte[] b, int p, int n) => false;
        public bool IsNmstrtChar(NormalEncoding enc, byte[] b, int p, int n) => false;
        public bool IsInvalidChar(NormalEncoding enc, byte[] b, int p, int n) => n == 4 && (b[p + 3] & 0xFC) != 0xDC;
        public bool IsNameCharMinbpc(byte[] b, int p) => XmlTok.UCS2_GET_NAMING(XmlTok.namePages, b[p + 1], b[p]);
        public bool IsNmstrtCharMinbpc(byte[] b, int p) => XmlTok.UCS2_GET_NAMING(XmlTok.nmstrtPages, b[p + 1], b[p]);
    }

    /// <summary>BIG2_* macros of xmltok.c (big2_ prefix).</summary>
    internal struct Big2Policy : IEncPolicy
    {
        public int MINBPC { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => 2; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ByteType(NormalEncoding enc, byte[] b, int p) =>
            b[p] == 0 ? enc.type[b[p + 1]] : XmlTok.unicode_byte_type(b[p], b[p + 1]);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ByteToAscii(byte[] b, int p) => b[p] == 0 ? (sbyte)b[p + 1] : -1;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CharMatches(byte[] b, int p, int c) => b[p] == 0 && b[p + 1] == c;
        public bool IsNameChar(NormalEncoding enc, byte[] b, int p, int n) => false;
        public bool IsNmstrtChar(NormalEncoding enc, byte[] b, int p, int n) => false;
        public bool IsInvalidChar(NormalEncoding enc, byte[] b, int p, int n) => n == 4 && (b[p + 2] & 0xFC) != 0xDC;
        public bool IsNameCharMinbpc(byte[] b, int p) => XmlTok.UCS2_GET_NAMING(XmlTok.namePages, b[p], b[p + 1]);
        public bool IsNmstrtCharMinbpc(byte[] b, int p) => XmlTok.UCS2_GET_NAMING(XmlTok.nmstrtPages, b[p], b[p + 1]);
    }

    /// <summary>
    /// Modules/expat/xmltok_impl.c (expat 2.8.5): the tokenizer, one method per PREFIX(function).
    /// Pointers are offsets into the buffer b. The case macros (INVALID_CASES, CHECK_NAME_CASES,
    /// CHECK_NMSTRT_CASES) are helper methods returning NOMATCH when the byte type is not one of
    /// their cases, OK when they advanced ptr, or the token the C macro returns.
    /// </summary>
    internal static class XmlTokImpl<P> where P : struct, IEncPolicy
    {
        private static P E => default;
        private static int MINBPC => default(P).MINBPC;

        private const int NOMATCH = int.MinValue, OK = int.MaxValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool HAS_CHARS(int ptr, int end, int count) => end - ptr >= count * MINBPC;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool HAS_CHAR(int ptr, int end) => end - ptr >= MINBPC;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int BYTE_TYPE(NormalEncoding enc, byte[] b, int p) => E.ByteType(enc, b, p);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool CHAR_MATCHES(byte[] b, int p, int c) => E.CharMatches(b, p, c);

        // INVALID_CASES (with INVALID_LEAD_CASE 2..4)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int InvalidCases(NormalEncoding enc, byte[] b, int bt, ref int ptr, int end, ref int nextTokPtr)
        {
            switch (bt)
            {
                case BT_LEAD2:
                case BT_LEAD3:
                case BT_LEAD4:
                    {
                        int n = bt - BT_LEAD2 + 2;
                        if (end - ptr < n)
                            return XML_TOK_PARTIAL_CHAR;
                        if (E.IsInvalidChar(enc, b, ptr, n))
                        {
                            nextTokPtr = ptr;
                            return XML_TOK_INVALID;
                        }
                        ptr += n;
                        return OK;
                    }
                case BT_NONXML:
                case BT_MALFORM:
                case BT_TRAIL:
                    nextTokPtr = ptr;
                    return XML_TOK_INVALID;
            }
            return NOMATCH;
        }

        // CHECK_NAME_CASES (with CHECK_NAME_CASE 2..4)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CheckNameCases(NormalEncoding enc, byte[] b, int bt, ref int ptr, int end, ref int nextTokPtr)
        {
            switch (bt)
            {
                case BT_NONASCII:
                    if (!E.IsNameCharMinbpc(b, ptr))
                    {
                        nextTokPtr = ptr;
                        return XML_TOK_INVALID;
                    }
                    goto case BT_NMSTRT;
                case BT_NMSTRT:
                case BT_HEX:
                case BT_DIGIT:
                case BT_NAME:
                case BT_MINUS:
                    ptr += MINBPC;
                    return OK;
                case BT_LEAD2:
                case BT_LEAD3:
                case BT_LEAD4:
                    {
                        int n = bt - BT_LEAD2 + 2;
                        if (end - ptr < n)
                            return XML_TOK_PARTIAL_CHAR;
                        if (E.IsInvalidChar(enc, b, ptr, n) || !E.IsNameChar(enc, b, ptr, n))
                        {
                            nextTokPtr = ptr;
                            return XML_TOK_INVALID;
                        }
                        ptr += n;
                        return OK;
                    }
            }
            return NOMATCH;
        }

        // CHECK_NMSTRT_CASES (with CHECK_NMSTRT_CASE 2..4)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CheckNmstrtCases(NormalEncoding enc, byte[] b, int bt, ref int ptr, int end, ref int nextTokPtr)
        {
            switch (bt)
            {
                case BT_NONASCII:
                    if (!E.IsNmstrtCharMinbpc(b, ptr))
                    {
                        nextTokPtr = ptr;
                        return XML_TOK_INVALID;
                    }
                    goto case BT_NMSTRT;
                case BT_NMSTRT:
                case BT_HEX:
                    ptr += MINBPC;
                    return OK;
                case BT_LEAD2:
                case BT_LEAD3:
                case BT_LEAD4:
                    {
                        int n = bt - BT_LEAD2 + 2;
                        if (end - ptr < n)
                            return XML_TOK_PARTIAL_CHAR;
                        if (E.IsInvalidChar(enc, b, ptr, n) || !E.IsNmstrtChar(enc, b, ptr, n))
                        {
                            nextTokPtr = ptr;
                            return XML_TOK_INVALID;
                        }
                        ptr += n;
                        return OK;
                    }
            }
            return NOMATCH;
        }

        /// <summary>scanComment: ptr points to character following "&lt;!-"</summary>
        internal static int scanComment(NormalEncoding enc, byte[] b, int ptr, int end, ref int nextTokPtr)
        {
            if (HAS_CHAR(ptr, end))
            {
                if (!CHAR_MATCHES(b, ptr, '-'))
                {
                    nextTokPtr = ptr;
                    return XML_TOK_INVALID;
                }
                ptr += MINBPC;
                while (HAS_CHAR(ptr, end))
                {
                    int bt = BYTE_TYPE(enc, b, ptr);
                    int r = InvalidCases(enc, b, bt, ref ptr, end, ref nextTokPtr);
                    if (r != NOMATCH)
                    {
                        if (r != OK)
                            return r;
                        continue;
                    }
                    if (bt == BT_MINUS)
                    {
                        ptr += MINBPC;
                        if (!HAS_CHAR(ptr, end))
                            return XML_TOK_PARTIAL;
                        if (CHAR_MATCHES(b, ptr, '-'))
                        {
                            ptr += MINBPC;
                            if (!HAS_CHAR(ptr, end))
                                return XML_TOK_PARTIAL;
                            if (!CHAR_MATCHES(b, ptr, '>'))
                            {
                                nextTokPtr = ptr;
                                return XML_TOK_INVALID;
                            }
                            nextTokPtr = ptr + MINBPC;
                            return XML_TOK_COMMENT;
                        }
                    }
                    else
                        ptr += MINBPC;
                }
            }
            return XML_TOK_PARTIAL;
        }

        /// <summary>scanDecl: ptr points to character following "&lt;!"</summary>
        internal static int scanDecl(NormalEncoding enc, byte[] b, int ptr, int end, ref int nextTokPtr)
        {
            if (!HAS_CHAR(ptr, end))
                return XML_TOK_PARTIAL;
            switch (BYTE_TYPE(enc, b, ptr))
            {
                case BT_MINUS:
                    return scanComment(enc, b, ptr + MINBPC, end, ref nextTokPtr);
                case BT_LSQB:
                    nextTokPtr = ptr + MINBPC;
                    return XML_TOK_COND_SECT_OPEN;
                case BT_NMSTRT:
                case BT_HEX:
                    ptr += MINBPC;
                    break;
                default:
                    nextTokPtr = ptr;
                    return XML_TOK_INVALID;
            }
            while (HAS_CHAR(ptr, end))
            {
                switch (BYTE_TYPE(enc, b, ptr))
                {
                    case BT_PERCNT:
                        if (!HAS_CHARS(ptr, end, 2))
                            return XML_TOK_PARTIAL;
                        /* don't allow <!ENTITY% foo "whatever"> */
                        switch (BYTE_TYPE(enc, b, ptr + MINBPC))
                        {
                            case BT_S:
                            case BT_CR:
                            case BT_LF:
                            case BT_PERCNT:
                                nextTokPtr = ptr;
                                return XML_TOK_INVALID;
                        }
                        nextTokPtr = ptr;
                        return XML_TOK_DECL_OPEN;
                    case BT_S:
                    case BT_CR:
                    case BT_LF:
                        nextTokPtr = ptr;
                        return XML_TOK_DECL_OPEN;
                    case BT_NMSTRT:
                    case BT_HEX:
                        ptr += MINBPC;
                        break;
                    default:
                        nextTokPtr = ptr;
                        return XML_TOK_INVALID;
                }
            }
            return XML_TOK_PARTIAL;
        }

        /// <summary>checkPiTarget</summary>
        private static bool checkPiTarget(byte[] b, int ptr, int end, out int tokPtr)
        {
            bool upper = false;
            tokPtr = XML_TOK_PI;
            if (end - ptr != MINBPC * 3)
                return true;
            switch (E.ByteToAscii(b, ptr))
            {
                case 'x': break;
                case 'X': upper = true; break;
                default: return true;
            }
            ptr += MINBPC;
            switch (E.ByteToAscii(b, ptr))
            {
                case 'm': break;
                case 'M': upper = true; break;
                default: return true;
            }
            ptr += MINBPC;
            switch (E.ByteToAscii(b, ptr))
            {
                case 'l': break;
                case 'L': upper = true; break;
                default: return true;
            }
            if (upper)
                return false;
            tokPtr = XML_TOK_XML_DECL;
            return true;
        }

        /// <summary>scanPi: ptr points to character following "&lt;?"</summary>
        internal static int scanPi(NormalEncoding enc, byte[] b, int ptr, int end, ref int nextTokPtr)
        {
            int tok;
            int target = ptr;
            if (!HAS_CHAR(ptr, end))
                return XML_TOK_PARTIAL;
            {
                int r = CheckNmstrtCases(enc, b, BYTE_TYPE(enc, b, ptr), ref ptr, end, ref nextTokPtr);
                if (r == NOMATCH)
                {
                    nextTokPtr = ptr;
                    return XML_TOK_INVALID;
                }
                if (r != OK)
                    return r;
            }
            while (HAS_CHAR(ptr, end))
            {
                int bt = BYTE_TYPE(enc, b, ptr);
                int r = CheckNameCases(enc, b, bt, ref ptr, end, ref nextTokPtr);
                if (r != NOMATCH)
                {
                    if (r != OK)
                        return r;
                    continue;
                }
                switch (bt)
                {
                    case BT_S:
                    case BT_CR:
                    case BT_LF:
                        if (!checkPiTarget(b, target, ptr, out tok))
                        {
                            nextTokPtr = ptr;
                            return XML_TOK_INVALID;
                        }
                        ptr += MINBPC;
                        while (HAS_CHAR(ptr, end))
                        {
                            int bt2 = BYTE_TYPE(enc, b, ptr);
                            int r2 = InvalidCases(enc, b, bt2, ref ptr, end, ref nextTokPtr);
                            if (r2 != NOMATCH)
                            {
                                if (r2 != OK)
                                    return r2;
                                continue;
                            }
                            if (bt2 == BT_QUEST)
                            {
                                ptr += MINBPC;
                                if (!HAS_CHAR(ptr, end))
                                    return XML_TOK_PARTIAL;
                                if (CHAR_MATCHES(b, ptr, '>'))
                                {
                                    nextTokPtr = ptr + MINBPC;
                                    return tok;
                                }
                            }
                            else
                                ptr += MINBPC;
                        }
                        return XML_TOK_PARTIAL;
                    case BT_QUEST:
                        if (!checkPiTarget(b, target, ptr, out tok))
                        {
                            nextTokPtr = ptr;
                            return XML_TOK_INVALID;
                        }
                        ptr += MINBPC;
                        if (!HAS_CHAR(ptr, end))
                            return XML_TOK_PARTIAL;
                        if (CHAR_MATCHES(b, ptr, '>'))
                        {
                            nextTokPtr = ptr + MINBPC;
                            return tok;
                        }
                        nextTokPtr = ptr;
                        return XML_TOK_INVALID;
                    default:
                        nextTokPtr = ptr;
                        return XML_TOK_INVALID;
                }
            }
            return XML_TOK_PARTIAL;
        }

        private static readonly byte[] CDATA_LSQB = { (byte)'C', (byte)'D', (byte)'A', (byte)'T', (byte)'A', (byte)'[' };

        /// <summary>scanCdataSection</summary>
        internal static int scanCdataSection(byte[] b, int ptr, int end, ref int nextTokPtr)
        {
            /* CDATA[ */
            if (!HAS_CHARS(ptr, end, 6))
                return XML_TOK_PARTIAL;
            for (int i = 0; i < 6; i++, ptr += MINBPC)
            {
                if (!CHAR_MATCHES(b, ptr, CDATA_LSQB[i]))
                {
                    nextTokPtr = ptr;
                    return XML_TOK_INVALID;
                }
            }
            nextTokPtr = ptr;
            return XML_TOK_CDATA_SECT_OPEN;
        }

        /// <summary>cdataSectionTok</summary>
        internal static int cdataSectionTok(NormalEncoding enc, byte[] b, int ptr, int end, ref int nextTokPtr)
        {
            if (ptr >= end)
                return XML_TOK_NONE;
            if (MINBPC > 1)
            {
                int n = end - ptr;
                if ((n & (MINBPC - 1)) != 0)
                {
                    n &= ~(MINBPC - 1);
                    if (n == 0)
                        return XML_TOK_PARTIAL;
                    end = ptr + n;
                }
            }
            {
                int bt = BYTE_TYPE(enc, b, ptr);
                switch (bt)
                {
                    case BT_RSQB:
                        ptr += MINBPC;
                        if (!HAS_CHAR(ptr, end))
                            return XML_TOK_PARTIAL;
                        if (!CHAR_MATCHES(b, ptr, ']'))
                            break;
                        ptr += MINBPC;
                        if (!HAS_CHAR(ptr, end))
                            return XML_TOK_PARTIAL;
                        if (!CHAR_MATCHES(b, ptr, '>'))
                        {
                            ptr -= MINBPC;
                            break;
                        }
                        nextTokPtr = ptr + MINBPC;
                        return XML_TOK_CDATA_SECT_CLOSE;
                    case BT_CR:
                        ptr += MINBPC;
                        if (!HAS_CHAR(ptr, end))
                            return XML_TOK_PARTIAL;
                        if (BYTE_TYPE(enc, b, ptr) == BT_LF)
                            ptr += MINBPC;
                        nextTokPtr = ptr;
                        return XML_TOK_DATA_NEWLINE;
                    case BT_LF:
                        nextTokPtr = ptr + MINBPC;
                        return XML_TOK_DATA_NEWLINE;
                    default:
                        {
                            int r = InvalidCases(enc, b, bt, ref ptr, end, ref nextTokPtr);
                            if (r == NOMATCH)
                                ptr += MINBPC;
                            else if (r != OK)
                                return r;
                            break;
                        }
                }
            }
            while (HAS_CHAR(ptr, end))
            {
                switch (BYTE_TYPE(enc, b, ptr))
                {
                    case BT_LEAD2:
                        if (end - ptr < 2 || E.IsInvalidChar(enc, b, ptr, 2))
                        {
                            nextTokPtr = ptr;
                            return XML_TOK_DATA_CHARS;
                        }
                        ptr += 2;
                        break;
                    case BT_LEAD3:
                        if (end - ptr < 3 || E.IsInvalidChar(enc, b, ptr, 3))
                        {
                            nextTokPtr = ptr;
                            return XML_TOK_DATA_CHARS;
                        }
                        ptr += 3;
                        break;
                    case BT_LEAD4:
                        if (end - ptr < 4 || E.IsInvalidChar(enc, b, ptr, 4))
                        {
                            nextTokPtr = ptr;
                            return XML_TOK_DATA_CHARS;
                        }
                        ptr += 4;
                        break;
                    case BT_NONXML:
                    case BT_MALFORM:
                    case BT_TRAIL:
                    case BT_CR:
                    case BT_LF:
                    case BT_RSQB:
                        nextTokPtr = ptr;
                        return XML_TOK_DATA_CHARS;
                    default:
                        ptr += MINBPC;
                        break;
                }
            }
            nextTokPtr = ptr;
            return XML_TOK_DATA_CHARS;
        }

        /// <summary>scanEndTag: ptr points to character following "&lt;/"</summary>
        internal static int scanEndTag(NormalEncoding enc, byte[] b, int ptr, int end, ref int nextTokPtr)
        {
            if (!HAS_CHAR(ptr, end))
                return XML_TOK_PARTIAL;
            {
                int r = CheckNmstrtCases(enc, b, BYTE_TYPE(enc, b, ptr), ref ptr, end, ref nextTokPtr);
                if (r == NOMATCH)
                {
                    nextTokPtr = ptr;
                    return XML_TOK_INVALID;
                }
                if (r != OK)
                    return r;
            }
            while (HAS_CHAR(ptr, end))
            {
                int bt = BYTE_TYPE(enc, b, ptr);
                int r = CheckNameCases(enc, b, bt, ref ptr, end, ref nextTokPtr);
                if (r != NOMATCH)
                {
                    if (r != OK)
                        return r;
                    continue;
                }
                switch (bt)
                {
                    case BT_S:
                    case BT_CR:
                    case BT_LF:
                        for (ptr += MINBPC; HAS_CHAR(ptr, end); ptr += MINBPC)
                        {
                            switch (BYTE_TYPE(enc, b, ptr))
                            {
                                case BT_S:
                                case BT_CR:
                                case BT_LF:
                                    break;
                                case BT_GT:
                                    nextTokPtr = ptr + MINBPC;
                                    return XML_TOK_END_TAG;
                                default:
                                    nextTokPtr = ptr;
                                    return XML_TOK_INVALID;
                            }
                        }
                        return XML_TOK_PARTIAL;
                    case BT_COLON:
                        /* no need to check qname syntax here,
                           since end-tag must match exactly */
                        ptr += MINBPC;
                        break;
                    case BT_GT:
                        nextTokPtr = ptr + MINBPC;
                        return XML_TOK_END_TAG;
                    default:
                        nextTokPtr = ptr;
                        return XML_TOK_INVALID;
                }
            }
            return XML_TOK_PARTIAL;
        }

        /// <summary>scanHexCharRef: ptr points to character following "&amp;#X"</summary>
        private static int scanHexCharRef(NormalEncoding enc, byte[] b, int ptr, int end, ref int nextTokPtr)
        {
            if (HAS_CHAR(ptr, end))
            {
                switch (BYTE_TYPE(enc, b, ptr))
                {
                    case BT_DIGIT:
                    case BT_HEX:
                        break;
                    default:
                        nextTokPtr = ptr;
                        return XML_TOK_INVALID;
                }
                for (ptr += MINBPC; HAS_CHAR(ptr, end); ptr += MINBPC)
                {
                    switch (BYTE_TYPE(enc, b, ptr))
                    {
                        case BT_DIGIT:
                        case BT_HEX:
                            break;
                        case BT_SEMI:
                            nextTokPtr = ptr + MINBPC;
                            return XML_TOK_CHAR_REF;
                        default:
                            nextTokPtr = ptr;
                            return XML_TOK_INVALID;
                    }
                }
            }
            return XML_TOK_PARTIAL;
        }

        /// <summary>scanCharRef: ptr points to character following "&amp;#"</summary>
        private static int scanCharRef(NormalEncoding enc, byte[] b, int ptr, int end, ref int nextTokPtr)
        {
            if (HAS_CHAR(ptr, end))
            {
                if (CHAR_MATCHES(b, ptr, 'x'))
                    return scanHexCharRef(enc, b, ptr + MINBPC, end, ref nextTokPtr);
                switch (BYTE_TYPE(enc, b, ptr))
                {
                    case BT_DIGIT:
                        break;
                    default:
                        nextTokPtr = ptr;
                        return XML_TOK_INVALID;
                }
                for (ptr += MINBPC; HAS_CHAR(ptr, end); ptr += MINBPC)
                {
                    switch (BYTE_TYPE(enc, b, ptr))
                    {
                        case BT_DIGIT:
                            break;
                        case BT_SEMI:
                            nextTokPtr = ptr + MINBPC;
                            return XML_TOK_CHAR_REF;
                        default:
                            nextTokPtr = ptr;
                            return XML_TOK_INVALID;
                    }
                }
            }
            return XML_TOK_PARTIAL;
        }

        /// <summary>scanRef: ptr points to character following "&amp;"</summary>
        internal static int scanRef(NormalEncoding enc, byte[] b, int ptr, int end, ref int nextTokPtr)
        {
            if (!HAS_CHAR(ptr, end))
                return XML_TOK_PARTIAL;
            {
                int bt = BYTE_TYPE(enc, b, ptr);
                int r = CheckNmstrtCases(enc, b, bt, ref ptr, end, ref nextTokPtr);
                if (r == NOMATCH)
                {
                    if (bt == BT_NUM)
                        return scanCharRef(enc, b, ptr + MINBPC, end, ref nextTokPtr);
                    nextTokPtr = ptr;
                    return XML_TOK_INVALID;
                }
                if (r != OK)
                    return r;
            }
            while (HAS_CHAR(ptr, end))
            {
                int bt = BYTE_TYPE(enc, b, ptr);
                int r = CheckNameCases(enc, b, bt, ref ptr, end, ref nextTokPtr);
                if (r != NOMATCH)
                {
                    if (r != OK)
                        return r;
                    continue;
                }
                if (bt == BT_SEMI)
                {
                    nextTokPtr = ptr + MINBPC;
                    return XML_TOK_ENTITY_REF;
                }
                nextTokPtr = ptr;
                return XML_TOK_INVALID;
            }
            return XML_TOK_PARTIAL;
        }

        /// <summary>scanAtts: ptr points to character following first character of attribute name</summary>
        private static int scanAtts(NormalEncoding enc, byte[] b, int ptr, int end, ref int nextTokPtr)
        {
            bool hadColon = false;
            while (HAS_CHAR(ptr, end))
            {
                int bt = BYTE_TYPE(enc, b, ptr);
                int r = CheckNameCases(enc, b, bt, ref ptr, end, ref nextTokPtr);
                if (r != NOMATCH)
                {
                    if (r != OK)
                        return r;
                    continue;
                }
                switch (bt)
                {
                    case BT_COLON:
                        {
                            if (hadColon)
                            {
                                nextTokPtr = ptr;
                                return XML_TOK_INVALID;
                            }
                            hadColon = true;
                            ptr += MINBPC;
                            if (!HAS_CHAR(ptr, end))
                                return XML_TOK_PARTIAL;
                            int r2 = CheckNmstrtCases(enc, b, BYTE_TYPE(enc, b, ptr), ref ptr, end, ref nextTokPtr);
                            if (r2 == NOMATCH)
                            {
                                nextTokPtr = ptr;
                                return XML_TOK_INVALID;
                            }
                            if (r2 != OK)
                                return r2;
                            break;
                        }
                    case BT_S:
                    case BT_CR:
                    case BT_LF:
                    case BT_EQUALS:
                        {
                            if (bt != BT_EQUALS)
                            {
                                for (; ; )
                                {
                                    ptr += MINBPC;
                                    if (!HAS_CHAR(ptr, end))
                                        return XML_TOK_PARTIAL;
                                    int t = BYTE_TYPE(enc, b, ptr);
                                    if (t == BT_EQUALS)
                                        break;
                                    switch (t)
                                    {
                                        case BT_S:
                                        case BT_LF:
                                        case BT_CR:
                                            break;
                                        default:
                                            nextTokPtr = ptr;
                                            return XML_TOK_INVALID;
                                    }
                                }
                            }
                            // case BT_EQUALS:
                            int open;
                            hadColon = false;
                            for (; ; )
                            {
                                ptr += MINBPC;
                                if (!HAS_CHAR(ptr, end))
                                    return XML_TOK_PARTIAL;
                                open = BYTE_TYPE(enc, b, ptr);
                                if (open == BT_QUOT || open == BT_APOS)
                                    break;
                                switch (open)
                                {
                                    case BT_S:
                                    case BT_LF:
                                    case BT_CR:
                                        break;
                                    default:
                                        nextTokPtr = ptr;
                                        return XML_TOK_INVALID;
                                }
                            }
                            ptr += MINBPC;
                            /* in attribute value */
                            for (; ; )
                            {
                                if (!HAS_CHAR(ptr, end))
                                    return XML_TOK_PARTIAL;
                                int t = BYTE_TYPE(enc, b, ptr);
                                if (t == open)
                                    break;
                                int r3 = InvalidCases(enc, b, t, ref ptr, end, ref nextTokPtr);
                                if (r3 != NOMATCH)
                                {
                                    if (r3 != OK)
                                        return r3;
                                    continue;
                                }
                                switch (t)
                                {
                                    case BT_AMP:
                                        {
                                            int tok = scanRef(enc, b, ptr + MINBPC, end, ref ptr);
                                            if (tok <= 0)
                                            {
                                                if (tok == XML_TOK_INVALID)
                                                    nextTokPtr = ptr;
                                                return tok;
                                            }
                                            break;
                                        }
                                    case BT_LT:
                                        nextTokPtr = ptr;
                                        return XML_TOK_INVALID;
                                    default:
                                        ptr += MINBPC;
                                        break;
                                }
                            }
                            ptr += MINBPC;
                            if (!HAS_CHAR(ptr, end))
                                return XML_TOK_PARTIAL;
                            switch (BYTE_TYPE(enc, b, ptr))
                            {
                                case BT_S:
                                case BT_CR:
                                case BT_LF:
                                    break;
                                case BT_SOL:
                                    goto sol;
                                case BT_GT:
                                    goto gt;
                                default:
                                    nextTokPtr = ptr;
                                    return XML_TOK_INVALID;
                            }
                            /* ptr points to closing quote */
                            for (; ; )
                            {
                                ptr += MINBPC;
                                if (!HAS_CHAR(ptr, end))
                                    return XML_TOK_PARTIAL;
                                int bt4 = BYTE_TYPE(enc, b, ptr);
                                int r4 = CheckNmstrtCases(enc, b, bt4, ref ptr, end, ref nextTokPtr);
                                if (r4 != NOMATCH)
                                {
                                    if (r4 != OK)
                                        return r4;
                                    break;
                                }
                                switch (bt4)
                                {
                                    case BT_S:
                                    case BT_CR:
                                    case BT_LF:
                                        continue;
                                    case BT_GT:
                                        goto gt;
                                    case BT_SOL:
                                        goto sol;
                                    default:
                                        nextTokPtr = ptr;
                                        return XML_TOK_INVALID;
                                }
                            }
                            break;
                        gt:
                            nextTokPtr = ptr + MINBPC;
                            return XML_TOK_START_TAG_WITH_ATTS;
                        sol:
                            ptr += MINBPC;
                            if (!HAS_CHAR(ptr, end))
                                return XML_TOK_PARTIAL;
                            if (!CHAR_MATCHES(b, ptr, '>'))
                            {
                                nextTokPtr = ptr;
                                return XML_TOK_INVALID;
                            }
                            nextTokPtr = ptr + MINBPC;
                            return XML_TOK_EMPTY_ELEMENT_WITH_ATTS;
                        }
                    default:
                        nextTokPtr = ptr;
                        return XML_TOK_INVALID;
                }
            }
            return XML_TOK_PARTIAL;
        }

        /// <summary>scanLt: ptr points to character following "&lt;"</summary>
        private static int scanLt(NormalEncoding enc, byte[] b, int ptr, int end, ref int nextTokPtr)
        {
            bool hadColon;
            if (!HAS_CHAR(ptr, end))
                return XML_TOK_PARTIAL;
            {
                int bt = BYTE_TYPE(enc, b, ptr);
                int r = CheckNmstrtCases(enc, b, bt, ref ptr, end, ref nextTokPtr);
                if (r == NOMATCH)
                {
                    switch (bt)
                    {
                        case BT_EXCL:
                            ptr += MINBPC;
                            if (!HAS_CHAR(ptr, end))
                                return XML_TOK_PARTIAL;
                            switch (BYTE_TYPE(enc, b, ptr))
                            {
                                case BT_MINUS:
                                    return scanComment(enc, b, ptr + MINBPC, end, ref nextTokPtr);
                                case BT_LSQB:
                                    return scanCdataSection(b, ptr + MINBPC, end, ref nextTokPtr);
                            }
                            nextTokPtr = ptr;
                            return XML_TOK_INVALID;
                        case BT_QUEST:
                            return scanPi(enc, b, ptr + MINBPC, end, ref nextTokPtr);
                        case BT_SOL:
                            return scanEndTag(enc, b, ptr + MINBPC, end, ref nextTokPtr);
                        default:
                            nextTokPtr = ptr;
                            return XML_TOK_INVALID;
                    }
                }
                if (r != OK)
                    return r;
            }
            hadColon = false;
            /* we have a start-tag */
            while (HAS_CHAR(ptr, end))
            {
                int bt = BYTE_TYPE(enc, b, ptr);
                int r = CheckNameCases(enc, b, bt, ref ptr, end, ref nextTokPtr);
                if (r != NOMATCH)
                {
                    if (r != OK)
                        return r;
                    continue;
                }
                switch (bt)
                {
                    case BT_COLON:
                        {
                            if (hadColon)
                            {
                                nextTokPtr = ptr;
                                return XML_TOK_INVALID;
                            }
                            hadColon = true;
                            ptr += MINBPC;
                            if (!HAS_CHAR(ptr, end))
                                return XML_TOK_PARTIAL;
                            int r2 = CheckNmstrtCases(enc, b, BYTE_TYPE(enc, b, ptr), ref ptr, end, ref nextTokPtr);
                            if (r2 == NOMATCH)
                            {
                                nextTokPtr = ptr;
                                return XML_TOK_INVALID;
                            }
                            if (r2 != OK)
                                return r2;
                            break;
                        }
                    case BT_S:
                    case BT_CR:
                    case BT_LF:
                        {
                            ptr += MINBPC;
                            while (HAS_CHAR(ptr, end))
                            {
                                int bt2 = BYTE_TYPE(enc, b, ptr);
                                int r2 = CheckNmstrtCases(enc, b, bt2, ref ptr, end, ref nextTokPtr);
                                if (r2 != NOMATCH)
                                {
                                    if (r2 != OK)
                                        return r2;
                                    return scanAtts(enc, b, ptr, end, ref nextTokPtr);
                                }
                                switch (bt2)
                                {
                                    case BT_GT:
                                        nextTokPtr = ptr + MINBPC;
                                        return XML_TOK_START_TAG_NO_ATTS;
                                    case BT_SOL:
                                        goto sol;
                                    case BT_S:
                                    case BT_CR:
                                    case BT_LF:
                                        ptr += MINBPC;
                                        continue;
                                    default:
                                        nextTokPtr = ptr;
                                        return XML_TOK_INVALID;
                                }
                            }
                            return XML_TOK_PARTIAL;
                        }
                    case BT_GT:
                        nextTokPtr = ptr + MINBPC;
                        return XML_TOK_START_TAG_NO_ATTS;
                    case BT_SOL:
                        goto sol;
                    default:
                        nextTokPtr = ptr;
                        return XML_TOK_INVALID;
                }
            }
            return XML_TOK_PARTIAL;
        sol:
            ptr += MINBPC;
            if (!HAS_CHAR(ptr, end))
                return XML_TOK_PARTIAL;
            if (!CHAR_MATCHES(b, ptr, '>'))
            {
                nextTokPtr = ptr;
                return XML_TOK_INVALID;
            }
            nextTokPtr = ptr + MINBPC;
            return XML_TOK_EMPTY_ELEMENT_NO_ATTS;
        }

        /// <summary>contentTok</summary>
        internal static int contentTok(NormalEncoding enc, byte[] b, int ptr, int end, ref int nextTokPtr)
        {
            if (ptr >= end)
                return XML_TOK_NONE;
            if (MINBPC > 1)
            {
                int n = end - ptr;
                if ((n & (MINBPC - 1)) != 0)
                {
                    n &= ~(MINBPC - 1);
                    if (n == 0)
                        return XML_TOK_PARTIAL;
                    end = ptr + n;
                }
            }
            {
                int bt = BYTE_TYPE(enc, b, ptr);
                switch (bt)
                {
                    case BT_LT:
                        return scanLt(enc, b, ptr + MINBPC, end, ref nextTokPtr);
                    case BT_AMP:
                        return scanRef(enc, b, ptr + MINBPC, end, ref nextTokPtr);
                    case BT_CR:
                        ptr += MINBPC;
                        if (!HAS_CHAR(ptr, end))
                            return XML_TOK_TRAILING_CR;
                        if (BYTE_TYPE(enc, b, ptr) == BT_LF)
                            ptr += MINBPC;
                        nextTokPtr = ptr;
                        return XML_TOK_DATA_NEWLINE;
                    case BT_LF:
                        nextTokPtr = ptr + MINBPC;
                        return XML_TOK_DATA_NEWLINE;
                    case BT_RSQB:
                        ptr += MINBPC;
                        if (!HAS_CHAR(ptr, end))
                            return XML_TOK_TRAILING_RSQB;
                        if (!CHAR_MATCHES(b, ptr, ']'))
                            break;
                        ptr += MINBPC;
                        if (!HAS_CHAR(ptr, end))
                            return XML_TOK_TRAILING_RSQB;
                        if (!CHAR_MATCHES(b, ptr, '>'))
                        {
                            ptr -= MINBPC;
                            break;
                        }
                        nextTokPtr = ptr;
                        return XML_TOK_INVALID;
                    default:
                        {
                            int r = InvalidCases(enc, b, bt, ref ptr, end, ref nextTokPtr);
                            if (r == NOMATCH)
                                ptr += MINBPC;
                            else if (r != OK)
                                return r;
                            break;
                        }
                }
            }
            while (HAS_CHAR(ptr, end))
            {
                switch (BYTE_TYPE(enc, b, ptr))
                {
                    case BT_LEAD2:
                        if (end - ptr < 2 || E.IsInvalidChar(enc, b, ptr, 2))
                        {
                            nextTokPtr = ptr;
                            return XML_TOK_DATA_CHARS;
                        }
                        ptr += 2;
                        break;
                    case BT_LEAD3:
                        if (end - ptr < 3 || E.IsInvalidChar(enc, b, ptr, 3))
                        {
                            nextTokPtr = ptr;
                            return XML_TOK_DATA_CHARS;
                        }
                        ptr += 3;
                        break;
                    case BT_LEAD4:
                        if (end - ptr < 4 || E.IsInvalidChar(enc, b, ptr, 4))
                        {
                            nextTokPtr = ptr;
                            return XML_TOK_DATA_CHARS;
                        }
                        ptr += 4;
                        break;
                    case BT_RSQB:
                        if (HAS_CHARS(ptr, end, 2))
                        {
                            if (!CHAR_MATCHES(b, ptr + MINBPC, ']'))
                            {
                                ptr += MINBPC;
                                break;
                            }
                            if (HAS_CHARS(ptr, end, 3))
                            {
                                if (!CHAR_MATCHES(b, ptr + 2 * MINBPC, '>'))
                                {
                                    ptr += MINBPC;
                                    break;
                                }
                                nextTokPtr = ptr + 2 * MINBPC;
                                return XML_TOK_INVALID;
                            }
                        }
                        nextTokPtr = ptr;
                        return XML_TOK_DATA_CHARS;
                    case BT_AMP:
                    case BT_LT:
                    case BT_NONXML:
                    case BT_MALFORM:
                    case BT_TRAIL:
                    case BT_CR:
                    case BT_LF:
                        nextTokPtr = ptr;
                        return XML_TOK_DATA_CHARS;
                    default:
                        ptr += MINBPC;
                        break;
                }
            }
            nextTokPtr = ptr;
            return XML_TOK_DATA_CHARS;
        }

        /// <summary>scanPercent: ptr points to character following "%"</summary>
        private static int scanPercent(NormalEncoding enc, byte[] b, int ptr, int end, ref int nextTokPtr)
        {
            if (!HAS_CHAR(ptr, end))
                return XML_TOK_PARTIAL;
            {
                int bt = BYTE_TYPE(enc, b, ptr);
                int r = CheckNmstrtCases(enc, b, bt, ref ptr, end, ref nextTokPtr);
                if (r == NOMATCH)
                {
                    switch (bt)
                    {
                        case BT_S:
                        case BT_LF:
                        case BT_CR:
                        case BT_PERCNT:
                            nextTokPtr = ptr;
                            return XML_TOK_PERCENT;
                        default:
                            nextTokPtr = ptr;
                            return XML_TOK_INVALID;
                    }
                }
                if (r != OK)
                    return r;
            }
            while (HAS_CHAR(ptr, end))
            {
                int bt = BYTE_TYPE(enc, b, ptr);
                int r = CheckNameCases(enc, b, bt, ref ptr, end, ref nextTokPtr);
                if (r != NOMATCH)
                {
                    if (r != OK)
                        return r;
                    continue;
                }
                if (bt == BT_SEMI)
                {
                    nextTokPtr = ptr + MINBPC;
                    return XML_TOK_PARAM_ENTITY_REF;
                }
                nextTokPtr = ptr;
                return XML_TOK_INVALID;
            }
            return XML_TOK_PARTIAL;
        }

        /// <summary>scanPoundName</summary>
        private static int scanPoundName(NormalEncoding enc, byte[] b, int ptr, int end, ref int nextTokPtr)
        {
            if (!HAS_CHAR(ptr, end))
                return XML_TOK_PARTIAL;
            {
                int r = CheckNmstrtCases(enc, b, BYTE_TYPE(enc, b, ptr), ref ptr, end, ref nextTokPtr);
                if (r == NOMATCH)
                {
                    nextTokPtr = ptr;
                    return XML_TOK_INVALID;
                }
                if (r != OK)
                    return r;
            }
            while (HAS_CHAR(ptr, end))
            {
                int bt = BYTE_TYPE(enc, b, ptr);
                int r = CheckNameCases(enc, b, bt, ref ptr, end, ref nextTokPtr);
                if (r != NOMATCH)
                {
                    if (r != OK)
                        return r;
                    continue;
                }
                switch (bt)
                {
                    case BT_CR:
                    case BT_LF:
                    case BT_S:
                    case BT_RPAR:
                    case BT_GT:
                    case BT_PERCNT:
                    case BT_VERBAR:
                        nextTokPtr = ptr;
                        return XML_TOK_POUND_NAME;
                    default:
                        nextTokPtr = ptr;
                        return XML_TOK_INVALID;
                }
            }
            return -XML_TOK_POUND_NAME;
        }

        /// <summary>scanLit</summary>
        private static int scanLit(int open, NormalEncoding enc, byte[] b, int ptr, int end, ref int nextTokPtr)
        {
            while (HAS_CHAR(ptr, end))
            {
                int t = BYTE_TYPE(enc, b, ptr);
                int r = InvalidCases(enc, b, t, ref ptr, end, ref nextTokPtr);
                if (r != NOMATCH)
                {
                    if (r != OK)
                        return r;
                    continue;
                }
                switch (t)
                {
                    case BT_QUOT:
                    case BT_APOS:
                        ptr += MINBPC;
                        if (t != open)
                            break;
                        if (!HAS_CHAR(ptr, end))
                            return -XML_TOK_LITERAL;
                        nextTokPtr = ptr;
                        switch (BYTE_TYPE(enc, b, ptr))
                        {
                            case BT_S:
                            case BT_CR:
                            case BT_LF:
                            case BT_GT:
                            case BT_PERCNT:
                            case BT_LSQB:
                                return XML_TOK_LITERAL;
                            default:
                                return XML_TOK_INVALID;
                        }
                    default:
                        ptr += MINBPC;
                        break;
                }
            }
            return XML_TOK_PARTIAL;
        }

        /// <summary>prologTok</summary>
        internal static int prologTok(NormalEncoding enc, byte[] b, int ptr, int end, ref int nextTokPtr)
        {
            int tok;
            if (ptr >= end)
                return XML_TOK_NONE;
            if (MINBPC > 1)
            {
                int n = end - ptr;
                if ((n & (MINBPC - 1)) != 0)
                {
                    n &= ~(MINBPC - 1);
                    if (n == 0)
                        return XML_TOK_PARTIAL;
                    end = ptr + n;
                }
            }
            int bt0 = BYTE_TYPE(enc, b, ptr);
            switch (bt0)
            {
                case BT_QUOT:
                    return scanLit(BT_QUOT, enc, b, ptr + MINBPC, end, ref nextTokPtr);
                case BT_APOS:
                    return scanLit(BT_APOS, enc, b, ptr + MINBPC, end, ref nextTokPtr);
                case BT_LT:
                    {
                        ptr += MINBPC;
                        if (!HAS_CHAR(ptr, end))
                            return XML_TOK_PARTIAL;
                        switch (BYTE_TYPE(enc, b, ptr))
                        {
                            case BT_EXCL:
                                return scanDecl(enc, b, ptr + MINBPC, end, ref nextTokPtr);
                            case BT_QUEST:
                                return scanPi(enc, b, ptr + MINBPC, end, ref nextTokPtr);
                            case BT_NMSTRT:
                            case BT_HEX:
                            case BT_NONASCII:
                            case BT_LEAD2:
                            case BT_LEAD3:
                            case BT_LEAD4:
                                nextTokPtr = ptr - MINBPC;
                                return XML_TOK_INSTANCE_START;
                        }
                        nextTokPtr = ptr;
                        return XML_TOK_INVALID;
                    }
                case BT_CR:
                case BT_S:
                case BT_LF:
                    if (bt0 == BT_CR && ptr + MINBPC == end)
                    {
                        nextTokPtr = end;
                        /* indicate that this might be part of a CR/LF pair */
                        return -XML_TOK_PROLOG_S;
                    }
                    for (; ; )
                    {
                        ptr += MINBPC;
                        if (!HAS_CHAR(ptr, end))
                            break;
                        bool stop = false;
                        switch (BYTE_TYPE(enc, b, ptr))
                        {
                            case BT_S:
                            case BT_LF:
                                break;
                            case BT_CR:
                                /* don't split CR/LF pair */
                                if (ptr + MINBPC != end)
                                    break;
                                stop = true;
                                break;
                            default:
                                stop = true;
                                break;
                        }
                        if (stop)
                        {
                            nextTokPtr = ptr;
                            return XML_TOK_PROLOG_S;
                        }
                    }
                    nextTokPtr = ptr;
                    return XML_TOK_PROLOG_S;
                case BT_PERCNT:
                    return scanPercent(enc, b, ptr + MINBPC, end, ref nextTokPtr);
                case BT_COMMA:
                    nextTokPtr = ptr + MINBPC;
                    return XML_TOK_COMMA;
                case BT_LSQB:
                    nextTokPtr = ptr + MINBPC;
                    return XML_TOK_OPEN_BRACKET;
                case BT_RSQB:
                    ptr += MINBPC;
                    if (!HAS_CHAR(ptr, end))
                        return -XML_TOK_CLOSE_BRACKET;
                    if (CHAR_MATCHES(b, ptr, ']'))
                    {
                        if (!HAS_CHARS(ptr, end, 2))
                            return XML_TOK_PARTIAL;
                        if (CHAR_MATCHES(b, ptr + MINBPC, '>'))
                        {
                            nextTokPtr = ptr + 2 * MINBPC;
                            return XML_TOK_COND_SECT_CLOSE;
                        }
                    }
                    nextTokPtr = ptr;
                    return XML_TOK_CLOSE_BRACKET;
                case BT_LPAR:
                    nextTokPtr = ptr + MINBPC;
                    return XML_TOK_OPEN_PAREN;
                case BT_RPAR:
                    ptr += MINBPC;
                    if (!HAS_CHAR(ptr, end))
                        return -XML_TOK_CLOSE_PAREN;
                    switch (BYTE_TYPE(enc, b, ptr))
                    {
                        case BT_AST:
                            nextTokPtr = ptr + MINBPC;
                            return XML_TOK_CLOSE_PAREN_ASTERISK;
                        case BT_QUEST:
                            nextTokPtr = ptr + MINBPC;
                            return XML_TOK_CLOSE_PAREN_QUESTION;
                        case BT_PLUS:
                            nextTokPtr = ptr + MINBPC;
                            return XML_TOK_CLOSE_PAREN_PLUS;
                        case BT_CR:
                        case BT_LF:
                        case BT_S:
                        case BT_GT:
                        case BT_COMMA:
                        case BT_VERBAR:
                        case BT_RPAR:
                            nextTokPtr = ptr;
                            return XML_TOK_CLOSE_PAREN;
                    }
                    nextTokPtr = ptr;
                    return XML_TOK_INVALID;
                case BT_VERBAR:
                    nextTokPtr = ptr + MINBPC;
                    return XML_TOK_OR;
                case BT_GT:
                    nextTokPtr = ptr + MINBPC;
                    return XML_TOK_DECL_CLOSE;
                case BT_NUM:
                    return scanPoundName(enc, b, ptr + MINBPC, end, ref nextTokPtr);
                case BT_LEAD2:
                case BT_LEAD3:
                case BT_LEAD4:
                    {
                        int n = bt0 - BT_LEAD2 + 2;
                        if (end - ptr < n)
                            return XML_TOK_PARTIAL_CHAR;
                        if (E.IsInvalidChar(enc, b, ptr, n))
                        {
                            nextTokPtr = ptr;
                            return XML_TOK_INVALID;
                        }
                        if (E.IsNmstrtChar(enc, b, ptr, n))
                        {
                            ptr += n;
                            tok = XML_TOK_NAME;
                            break;
                        }
                        if (E.IsNameChar(enc, b, ptr, n))
                        {
                            ptr += n;
                            tok = XML_TOK_NMTOKEN;
                            break;
                        }
                        nextTokPtr = ptr;
                        return XML_TOK_INVALID;
                    }
                case BT_NMSTRT:
                case BT_HEX:
                    tok = XML_TOK_NAME;
                    ptr += MINBPC;
                    break;
                case BT_DIGIT:
                case BT_NAME:
                case BT_MINUS:
                case BT_COLON:
                    tok = XML_TOK_NMTOKEN;
                    ptr += MINBPC;
                    break;
                case BT_NONASCII:
                    if (E.IsNmstrtCharMinbpc(b, ptr))
                    {
                        ptr += MINBPC;
                        tok = XML_TOK_NAME;
                        break;
                    }
                    if (E.IsNameCharMinbpc(b, ptr))
                    {
                        ptr += MINBPC;
                        tok = XML_TOK_NMTOKEN;
                        break;
                    }
                    nextTokPtr = ptr;
                    return XML_TOK_INVALID;
                default:
                    nextTokPtr = ptr;
                    return XML_TOK_INVALID;
            }
            while (HAS_CHAR(ptr, end))
            {
                int bt = BYTE_TYPE(enc, b, ptr);
                int r = CheckNameCases(enc, b, bt, ref ptr, end, ref nextTokPtr);
                if (r != NOMATCH)
                {
                    if (r != OK)
                        return r;
                    continue;
                }
                switch (bt)
                {
                    case BT_GT:
                    case BT_RPAR:
                    case BT_COMMA:
                    case BT_VERBAR:
                    case BT_LSQB:
                    case BT_PERCNT:
                    case BT_S:
                    case BT_CR:
                    case BT_LF:
                        nextTokPtr = ptr;
                        return tok;
                    case BT_COLON:
                        ptr += MINBPC;
                        switch (tok)
                        {
                            case XML_TOK_NAME:
                                {
                                    if (!HAS_CHAR(ptr, end))
                                        return XML_TOK_PARTIAL;
                                    tok = XML_TOK_PREFIXED_NAME;
                                    int r2 = CheckNameCases(enc, b, BYTE_TYPE(enc, b, ptr), ref ptr, end, ref nextTokPtr);
                                    if (r2 == NOMATCH)
                                        tok = XML_TOK_NMTOKEN;
                                    else if (r2 != OK)
                                        return r2;
                                    break;
                                }
                            case XML_TOK_PREFIXED_NAME:
                                tok = XML_TOK_NMTOKEN;
                                break;
                        }
                        break;
                    case BT_PLUS:
                        if (tok == XML_TOK_NMTOKEN)
                        {
                            nextTokPtr = ptr;
                            return XML_TOK_INVALID;
                        }
                        nextTokPtr = ptr + MINBPC;
                        return XML_TOK_NAME_PLUS;
                    case BT_AST:
                        if (tok == XML_TOK_NMTOKEN)
                        {
                            nextTokPtr = ptr;
                            return XML_TOK_INVALID;
                        }
                        nextTokPtr = ptr + MINBPC;
                        return XML_TOK_NAME_ASTERISK;
                    case BT_QUEST:
                        if (tok == XML_TOK_NMTOKEN)
                        {
                            nextTokPtr = ptr;
                            return XML_TOK_INVALID;
                        }
                        nextTokPtr = ptr + MINBPC;
                        return XML_TOK_NAME_QUESTION;
                    default:
                        nextTokPtr = ptr;
                        return XML_TOK_INVALID;
                }
            }
            return -tok;
        }

        /// <summary>attributeValueTok</summary>
        internal static int attributeValueTok(NormalEncoding enc, byte[] b, int ptr, int end, ref int nextTokPtr)
        {
            int start;
            if (ptr >= end)
                return XML_TOK_NONE;
            else if (!HAS_CHAR(ptr, end))
                return XML_TOK_PARTIAL; /* LCOV_EXCL_LINE */
            start = ptr;
            while (HAS_CHAR(ptr, end))
            {
                switch (BYTE_TYPE(enc, b, ptr))
                {
                    case BT_LEAD2: ptr += 2; break;
                    case BT_LEAD3: ptr += 3; break;
                    case BT_LEAD4: ptr += 4; break;
                    case BT_AMP:
                        if (ptr == start)
                            return scanRef(enc, b, ptr + MINBPC, end, ref nextTokPtr);
                        nextTokPtr = ptr;
                        return XML_TOK_DATA_CHARS;
                    case BT_LT:
                        /* this is for inside entity references */
                        nextTokPtr = ptr;
                        return XML_TOK_INVALID;
                    case BT_LF:
                        if (ptr == start)
                        {
                            nextTokPtr = ptr + MINBPC;
                            return XML_TOK_DATA_NEWLINE;
                        }
                        nextTokPtr = ptr;
                        return XML_TOK_DATA_CHARS;
                    case BT_CR:
                        if (ptr == start)
                        {
                            ptr += MINBPC;
                            if (!HAS_CHAR(ptr, end))
                                return XML_TOK_TRAILING_CR;
                            if (BYTE_TYPE(enc, b, ptr) == BT_LF)
                                ptr += MINBPC;
                            nextTokPtr = ptr;
                            return XML_TOK_DATA_NEWLINE;
                        }
                        nextTokPtr = ptr;
                        return XML_TOK_DATA_CHARS;
                    case BT_S:
                        if (ptr == start)
                        {
                            nextTokPtr = ptr + MINBPC;
                            return XML_TOK_ATTRIBUTE_VALUE_S;
                        }
                        nextTokPtr = ptr;
                        return XML_TOK_DATA_CHARS;
                    default:
                        ptr += MINBPC;
                        break;
                }
            }
            nextTokPtr = ptr;
            return XML_TOK_DATA_CHARS;
        }

        /// <summary>entityValueTok</summary>
        internal static int entityValueTok(NormalEncoding enc, byte[] b, int ptr, int end, ref int nextTokPtr)
        {
            int start;
            if (ptr >= end)
                return XML_TOK_NONE;
            else if (!HAS_CHAR(ptr, end))
                return XML_TOK_PARTIAL; /* LCOV_EXCL_LINE */
            start = ptr;
            while (HAS_CHAR(ptr, end))
            {
                switch (BYTE_TYPE(enc, b, ptr))
                {
                    case BT_LEAD2: ptr += 2; break;
                    case BT_LEAD3: ptr += 3; break;
                    case BT_LEAD4: ptr += 4; break;
                    case BT_AMP:
                        if (ptr == start)
                            return scanRef(enc, b, ptr + MINBPC, end, ref nextTokPtr);
                        nextTokPtr = ptr;
                        return XML_TOK_DATA_CHARS;
                    case BT_PERCNT:
                        if (ptr == start)
                        {
                            int tok = scanPercent(enc, b, ptr + MINBPC, end, ref nextTokPtr);
                            return (tok == XML_TOK_PERCENT) ? XML_TOK_INVALID : tok;
                        }
                        nextTokPtr = ptr;
                        return XML_TOK_DATA_CHARS;
                    case BT_LF:
                        if (ptr == start)
                        {
                            nextTokPtr = ptr + MINBPC;
                            return XML_TOK_DATA_NEWLINE;
                        }
                        nextTokPtr = ptr;
                        return XML_TOK_DATA_CHARS;
                    case BT_CR:
                        if (ptr == start)
                        {
                            ptr += MINBPC;
                            if (!HAS_CHAR(ptr, end))
                                return XML_TOK_TRAILING_CR;
                            if (BYTE_TYPE(enc, b, ptr) == BT_LF)
                                ptr += MINBPC;
                            nextTokPtr = ptr;
                            return XML_TOK_DATA_NEWLINE;
                        }
                        nextTokPtr = ptr;
                        return XML_TOK_DATA_CHARS;
                    default:
                        ptr += MINBPC;
                        break;
                }
            }
            nextTokPtr = ptr;
            return XML_TOK_DATA_CHARS;
        }

        /// <summary>ignoreSectionTok</summary>
        internal static int ignoreSectionTok(NormalEncoding enc, byte[] b, int ptr, int end, ref int nextTokPtr)
        {
            int level = 0;
            if (MINBPC > 1)
            {
                int n = end - ptr;
                if ((n & (MINBPC - 1)) != 0)
                {
                    n &= ~(MINBPC - 1);
                    end = ptr + n;
                }
            }
            while (HAS_CHAR(ptr, end))
            {
                int bt = BYTE_TYPE(enc, b, ptr);
                int r = InvalidCases(enc, b, bt, ref ptr, end, ref nextTokPtr);
                if (r != NOMATCH)
                {
                    if (r != OK)
                        return r;
                    continue;
                }
                switch (bt)
                {
                    case BT_LT:
                        ptr += MINBPC;
                        if (!HAS_CHAR(ptr, end))
                            return XML_TOK_PARTIAL;
                        if (CHAR_MATCHES(b, ptr, '!'))
                        {
                            ptr += MINBPC;
                            if (!HAS_CHAR(ptr, end))
                                return XML_TOK_PARTIAL;
                            if (CHAR_MATCHES(b, ptr, '['))
                            {
                                ++level;
                                ptr += MINBPC;
                            }
                        }
                        break;
                    case BT_RSQB:
                        ptr += MINBPC;
                        if (!HAS_CHAR(ptr, end))
                            return XML_TOK_PARTIAL;
                        if (CHAR_MATCHES(b, ptr, ']'))
                        {
                            ptr += MINBPC;
                            if (!HAS_CHAR(ptr, end))
                                return XML_TOK_PARTIAL;
                            if (CHAR_MATCHES(b, ptr, '>'))
                            {
                                ptr += MINBPC;
                                if (level == 0)
                                {
                                    nextTokPtr = ptr;
                                    return XML_TOK_IGNORE_SECT;
                                }
                                --level;
                            }
                        }
                        break;
                    default:
                        ptr += MINBPC;
                        break;
                }
            }
            return XML_TOK_PARTIAL;
        }

        /// <summary>isPublicId</summary>
        internal static bool isPublicId(NormalEncoding enc, byte[] b, int ptr, int end, ref int badPtr)
        {
            ptr += MINBPC;
            end -= MINBPC;
            for (; HAS_CHAR(ptr, end); ptr += MINBPC)
            {
                int bt = BYTE_TYPE(enc, b, ptr);
                switch (bt)
                {
                    case BT_DIGIT:
                    case BT_HEX:
                    case BT_MINUS:
                    case BT_APOS:
                    case BT_LPAR:
                    case BT_RPAR:
                    case BT_PLUS:
                    case BT_COMMA:
                    case BT_SOL:
                    case BT_EQUALS:
                    case BT_QUEST:
                    case BT_CR:
                    case BT_LF:
                    case BT_SEMI:
                    case BT_EXCL:
                    case BT_AST:
                    case BT_PERCNT:
                    case BT_NUM:
                    case BT_COLON:
                        break;
                    case BT_S:
                        if (CHAR_MATCHES(b, ptr, '\t'))
                        {
                            badPtr = ptr;
                            return false;
                        }
                        break;
                    default:
                        if ((bt == BT_NAME || bt == BT_NMSTRT) && (E.ByteToAscii(b, ptr) & ~0x7f) == 0)
                            break;
                        switch (E.ByteToAscii(b, ptr))
                        {
                            case 0x24: /* $ */
                            case 0x40: /* @ */
                                break;
                            default:
                                badPtr = ptr;
                                return false;
                        }
                        break;
                }
            }
            return true;
        }

        /// <summary>
        /// getAtts: this must only be called for a well-formed start-tag or empty element tag.
        /// Returns the number of attributes. Pointers to the first attsMax attributes are stored in atts.
        /// </summary>
        internal static int getAtts(NormalEncoding enc, byte[] b, int ptr, int attsMax, ATTRIBUTE[] atts)
        {
            const int other = 0, inName = 1, inValue = 2;
            int state = inName;
            int nAtts = 0;
            int open = 0; /* defined when state == inValue */

            for (ptr += MINBPC; ; ptr += MINBPC)
            {
                int bt = BYTE_TYPE(enc, b, ptr);
                switch (bt)
                {
                    case BT_LEAD2:
                    case BT_LEAD3:
                    case BT_LEAD4:
                    case BT_NONASCII:
                    case BT_NMSTRT:
                    case BT_HEX:
                        // START_NAME
                        if (state == other)
                        {
                            if (nAtts < attsMax)
                            {
                                atts[nAtts].name = ptr;
                                atts[nAtts].normalized = true;
                            }
                            state = inName;
                        }
                        if (bt >= BT_LEAD2 && bt <= BT_LEAD4)
                            ptr += (bt - BT_LEAD2 + 2) - MINBPC;
                        break;
                    case BT_QUOT:
                        if (state != inValue)
                        {
                            if (nAtts < attsMax)
                                atts[nAtts].valuePtr = ptr + MINBPC;
                            state = inValue;
                            open = BT_QUOT;
                        }
                        else if (open == BT_QUOT)
                        {
                            state = other;
                            if (nAtts < attsMax)
                                atts[nAtts].valueEnd = ptr;
                            nAtts++;
                        }
                        break;
                    case BT_APOS:
                        if (state != inValue)
                        {
                            if (nAtts < attsMax)
                                atts[nAtts].valuePtr = ptr + MINBPC;
                            state = inValue;
                            open = BT_APOS;
                        }
                        else if (open == BT_APOS)
                        {
                            state = other;
                            if (nAtts < attsMax)
                                atts[nAtts].valueEnd = ptr;
                            nAtts++;
                        }
                        break;
                    case BT_AMP:
                        if (nAtts < attsMax)
                            atts[nAtts].normalized = false;
                        break;
                    case BT_S:
                        if (state == inName)
                            state = other;
                        else if (state == inValue && nAtts < attsMax && atts[nAtts].normalized
                                 && (ptr == atts[nAtts].valuePtr
                                     || E.ByteToAscii(b, ptr) != ' '
                                     || E.ByteToAscii(b, ptr + MINBPC) == ' '
                                     || BYTE_TYPE(enc, b, ptr + MINBPC) == open))
                            atts[nAtts].normalized = false;
                        break;
                    case BT_CR:
                    case BT_LF:
                        /* This case ensures that the first attribute name is counted
                           Apart from that we could just change state on the quote. */
                        if (state == inName)
                            state = other;
                        else if (state == inValue && nAtts < attsMax)
                            atts[nAtts].normalized = false;
                        break;
                    case BT_GT:
                    case BT_SOL:
                        if (state != inValue)
                            return nAtts;
                        break;
                    default:
                        break;
                }
            }
            /* not reached */
        }

        /// <summary>charRefNumber</summary>
        internal static int charRefNumber(byte[] b, int ptr)
        {
            int result = 0;
            /* skip &# */
            ptr += 2 * MINBPC;
            if (CHAR_MATCHES(b, ptr, 'x'))
            {
                for (ptr += MINBPC; !CHAR_MATCHES(b, ptr, ';'); ptr += MINBPC)
                {
                    int c = E.ByteToAscii(b, ptr);
                    if (c >= '0' && c <= '9')
                    {
                        result <<= 4;
                        result |= (c - '0');
                    }
                    else if (c >= 'A' && c <= 'F')
                    {
                        result <<= 4;
                        result += 10 + (c - 'A');
                    }
                    else if (c >= 'a' && c <= 'f')
                    {
                        result <<= 4;
                        result += 10 + (c - 'a');
                    }
                    if (result >= 0x110000)
                        return -1;
                }
            }
            else
            {
                for (; !CHAR_MATCHES(b, ptr, ';'); ptr += MINBPC)
                {
                    int c = E.ByteToAscii(b, ptr);
                    result *= 10;
                    result += (c - '0');
                    if (result >= 0x110000)
                        return -1;
                }
            }
            return XmlTok.checkCharRefNumber(result);
        }

        /// <summary>predefinedEntityName</summary>
        internal static int predefinedEntityName(byte[] b, int ptr, int end)
        {
            switch ((end - ptr) / MINBPC)
            {
                case 2:
                    if (CHAR_MATCHES(b, ptr + MINBPC, 't'))
                    {
                        switch (E.ByteToAscii(b, ptr))
                        {
                            case 'l':
                                return '<';
                            case 'g':
                                return '>';
                        }
                    }
                    break;
                case 3:
                    if (CHAR_MATCHES(b, ptr, 'a'))
                    {
                        ptr += MINBPC;
                        if (CHAR_MATCHES(b, ptr, 'm'))
                        {
                            ptr += MINBPC;
                            if (CHAR_MATCHES(b, ptr, 'p'))
                                return '&';
                        }
                    }
                    break;
                case 4:
                    switch (E.ByteToAscii(b, ptr))
                    {
                        case 'q':
                            ptr += MINBPC;
                            if (CHAR_MATCHES(b, ptr, 'u'))
                            {
                                ptr += MINBPC;
                                if (CHAR_MATCHES(b, ptr, 'o'))
                                {
                                    ptr += MINBPC;
                                    if (CHAR_MATCHES(b, ptr, 't'))
                                        return '"';
                                }
                            }
                            break;
                        case 'a':
                            ptr += MINBPC;
                            if (CHAR_MATCHES(b, ptr, 'p'))
                            {
                                ptr += MINBPC;
                                if (CHAR_MATCHES(b, ptr, 'o'))
                                {
                                    ptr += MINBPC;
                                    if (CHAR_MATCHES(b, ptr, 's'))
                                        return '\'';
                                }
                            }
                            break;
                    }
                    break;
            }
            return 0;
        }

        /// <summary>nameMatchesAscii: ptr2 is an ASCII keyword</summary>
        internal static bool nameMatchesAscii(byte[] b, int ptr1, int end1, byte[] ptr2)
        {
            int i = 0;
            for (; i < ptr2.Length; ptr1 += MINBPC, i++)
            {
                if (end1 - ptr1 < MINBPC)
                    return false; /* LCOV_EXCL_LINE */
                if (!CHAR_MATCHES(b, ptr1, ptr2[i]))
                    return false;
            }
            return ptr1 == end1;
        }

        /// <summary>nameLength</summary>
        internal static int nameLength(NormalEncoding enc, byte[] b, int ptr)
        {
            int start = ptr;
            for (; ; )
            {
                switch (BYTE_TYPE(enc, b, ptr))
                {
                    case BT_LEAD2: ptr += 2; break;
                    case BT_LEAD3: ptr += 3; break;
                    case BT_LEAD4: ptr += 4; break;
                    case BT_NONASCII:
                    case BT_NMSTRT:
                    case BT_COLON:
                    case BT_HEX:
                    case BT_DIGIT:
                    case BT_NAME:
                    case BT_MINUS:
                        ptr += MINBPC;
                        break;
                    default:
                        return ptr - start;
                }
            }
        }

        /// <summary>skipS</summary>
        internal static int skipS(NormalEncoding enc, byte[] b, int ptr)
        {
            for (; ; )
            {
                switch (BYTE_TYPE(enc, b, ptr))
                {
                    case BT_LF:
                    case BT_CR:
                    case BT_S:
                        ptr += MINBPC;
                        break;
                    default:
                        return ptr;
                }
            }
        }

        /// <summary>updatePosition</summary>
        internal static void updatePosition(NormalEncoding enc, byte[] b, int ptr, int end, ref POSITION pos)
        {
            while (HAS_CHAR(ptr, end))
            {
                switch (BYTE_TYPE(enc, b, ptr))
                {
                    case BT_LEAD2: ptr += 2; pos.columnNumber++; break;
                    case BT_LEAD3: ptr += 3; pos.columnNumber++; break;
                    case BT_LEAD4: ptr += 4; pos.columnNumber++; break;
                    case BT_LF:
                        pos.columnNumber = 0;
                        pos.lineNumber++;
                        ptr += MINBPC;
                        break;
                    case BT_CR:
                        pos.lineNumber++;
                        ptr += MINBPC;
                        if (HAS_CHAR(ptr, end) && BYTE_TYPE(enc, b, ptr) == BT_LF)
                            ptr += MINBPC;
                        pos.columnNumber = 0;
                        break;
                    default:
                        ptr += MINBPC;
                        pos.columnNumber++;
                        break;
                }
            }
        }
    }
}
