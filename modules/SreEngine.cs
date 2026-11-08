using System.Globalization;
using System;
using System.Collections.Generic;
using System.Text;

namespace SharpPy.Modules
{
    internal sealed class SreState
    {
        // Constants from sre_constants.h
        internal const int SRE_MAGIC = 20221023;
        internal const int SRE_OP_FAILURE = 0;
        internal const int SRE_OP_SUCCESS = 1;
        internal const int SRE_OP_ANY = 2;
        internal const int SRE_OP_ANY_ALL = 3;
        internal const int SRE_OP_ASSERT = 4;
        internal const int SRE_OP_ASSERT_NOT = 5;
        internal const int SRE_OP_AT = 6;
        internal const int SRE_OP_BRANCH = 7;
        internal const int SRE_OP_CATEGORY = 8;
        internal const int SRE_OP_CHARSET = 9;
        internal const int SRE_OP_BIGCHARSET = 10;
        internal const int SRE_OP_GROUPREF = 11;
        internal const int SRE_OP_GROUPREF_EXISTS = 12;
        internal const int SRE_OP_IN = 13;
        internal const int SRE_OP_INFO = 14;
        internal const int SRE_OP_JUMP = 15;
        internal const int SRE_OP_LITERAL = 16;
        internal const int SRE_OP_MARK = 17;
        internal const int SRE_OP_MAX_UNTIL = 18;
        internal const int SRE_OP_MIN_UNTIL = 19;
        internal const int SRE_OP_NOT_LITERAL = 20;
        internal const int SRE_OP_NEGATE = 21;
        internal const int SRE_OP_RANGE = 22;
        internal const int SRE_OP_REPEAT = 23;
        internal const int SRE_OP_REPEAT_ONE = 24;
        internal const int SRE_OP_SUBPATTERN = 25;
        internal const int SRE_OP_MIN_REPEAT_ONE = 26;
        internal const int SRE_OP_ATOMIC_GROUP = 27;
        internal const int SRE_OP_POSSESSIVE_REPEAT = 28;
        internal const int SRE_OP_POSSESSIVE_REPEAT_ONE = 29;
        internal const int SRE_OP_GROUPREF_IGNORE = 30;
        internal const int SRE_OP_IN_IGNORE = 31;
        internal const int SRE_OP_LITERAL_IGNORE = 32;
        internal const int SRE_OP_NOT_LITERAL_IGNORE = 33;
        internal const int SRE_OP_GROUPREF_LOC_IGNORE = 34;
        internal const int SRE_OP_IN_LOC_IGNORE = 35;
        internal const int SRE_OP_LITERAL_LOC_IGNORE = 36;
        internal const int SRE_OP_NOT_LITERAL_LOC_IGNORE = 37;
        internal const int SRE_OP_GROUPREF_UNI_IGNORE = 38;
        internal const int SRE_OP_IN_UNI_IGNORE = 39;
        internal const int SRE_OP_LITERAL_UNI_IGNORE = 40;
        internal const int SRE_OP_NOT_LITERAL_UNI_IGNORE = 41;
        internal const int SRE_OP_RANGE_UNI_IGNORE = 42;

        internal const int SRE_AT_BEGINNING = 0;
        internal const int SRE_AT_BEGINNING_LINE = 1;
        internal const int SRE_AT_BEGINNING_STRING = 2;
        internal const int SRE_AT_BOUNDARY = 3;
        internal const int SRE_AT_NON_BOUNDARY = 4;
        internal const int SRE_AT_END = 5;
        internal const int SRE_AT_END_LINE = 6;
        internal const int SRE_AT_END_STRING = 7;
        internal const int SRE_AT_LOC_BOUNDARY = 8;
        internal const int SRE_AT_LOC_NON_BOUNDARY = 9;
        internal const int SRE_AT_UNI_BOUNDARY = 10;
        internal const int SRE_AT_UNI_NON_BOUNDARY = 11;

        internal const int SRE_CATEGORY_DIGIT = 0;
        internal const int SRE_CATEGORY_NOT_DIGIT = 1;
        internal const int SRE_CATEGORY_SPACE = 2;
        internal const int SRE_CATEGORY_NOT_SPACE = 3;
        internal const int SRE_CATEGORY_WORD = 4;
        internal const int SRE_CATEGORY_NOT_WORD = 5;
        internal const int SRE_CATEGORY_LINEBREAK = 6;
        internal const int SRE_CATEGORY_NOT_LINEBREAK = 7;
        internal const int SRE_CATEGORY_LOC_WORD = 8;
        internal const int SRE_CATEGORY_LOC_NOT_WORD = 9;
        internal const int SRE_CATEGORY_UNI_DIGIT = 10;
        internal const int SRE_CATEGORY_UNI_NOT_DIGIT = 11;
        internal const int SRE_CATEGORY_UNI_SPACE = 12;
        internal const int SRE_CATEGORY_UNI_NOT_SPACE = 13;
        internal const int SRE_CATEGORY_UNI_WORD = 14;
        internal const int SRE_CATEGORY_UNI_NOT_WORD = 15;
        internal const int SRE_CATEGORY_UNI_LINEBREAK = 16;
        internal const int SRE_CATEGORY_UNI_NOT_LINEBREAK = 17;

        internal const int SRE_INFO_PREFIX = 1;
        internal const int SRE_INFO_LITERAL = 2;
        internal const int SRE_INFO_CHARSET = 4;

        private const int SRE_MAXREPEAT = -1; // ~(SRE_CODE)0 as int
        private const int SRE_CODE_BITS = 32;

        // Jump labels
        private const int JUMP_NONE = 0;
        private const int JUMP_MAX_UNTIL_1 = 1;
        private const int JUMP_MAX_UNTIL_2 = 2;
        private const int JUMP_MAX_UNTIL_3 = 3;
        private const int JUMP_MIN_UNTIL_1 = 4;
        private const int JUMP_MIN_UNTIL_2 = 5;
        private const int JUMP_MIN_UNTIL_3 = 6;
        private const int JUMP_REPEAT = 7;
        private const int JUMP_REPEAT_ONE_1 = 8;
        private const int JUMP_REPEAT_ONE_2 = 9;
        private const int JUMP_MIN_REPEAT_ONE = 10;
        private const int JUMP_BRANCH = 11;
        private const int JUMP_ASSERT = 12;
        private const int JUMP_ASSERT_NOT = 13;
        private const int JUMP_POSS_REPEAT_1 = 14;
        private const int JUMP_POSS_REPEAT_2 = 15;
        private const int JUMP_ATOMIC_GROUP = 16;

        // State fields
        internal int Start;
        internal int Ptr;
        internal int LastIndex;
        internal int LastMark;
        internal int[] Mark;
        internal bool MustAdvance;
        internal bool MatchAll;
        internal int[] Text;
        internal int End;
        internal int Beginning;

        private readonly uint[] _code;
        private readonly int _groups;
        private readonly bool _ascii;
        private readonly bool _unicode;
        private SreRepeat? _repeat;

        internal readonly int Pos;
        internal readonly int EndPos;

        internal SreState(int[] text, int start, int end, uint[] code, int groups, bool ascii, bool unicode)
        {
            Pos = start;
            EndPos = end;
            Text = text;
            Start = start;
            End = end;
            _code = code;
            _groups = groups;
            _ascii = ascii;
            _unicode = unicode;
            Beginning = 0;
            Mark = new int[2 * groups];
            Reset();
        }

        internal void Reset()
        {
            LastMark = -1;
            LastIndex = -1;
            _repeat = null;
            for (int i = 0; i < Mark.Length; i++)
                Mark[i] = -1;
        }

        // --- Character helpers ---

        private static bool IsAsciiDigit(int c) => c >= '0' && c <= '9';
        private static bool IsAsciiSpace(int c) => c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f' || c == '\v';
        private static bool IsAsciiWord(int c) => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
        // SRE_IS_LINEBREAK (Modules/_sre/sre.c): '\n' alone, for ANY, AT_*_LINE and the ASCII category.
        private static bool IsSreLinebreak(int c) => c == '\n';

        // SRE_UNI_IS_LINEBREAK, Py_UNICODE_ISLINEBREAK: the unicode categories' line breaks.
        private static bool IsLinebreak(int c) => c == 0x0A || c == 0x0B || c == 0x0C || c == 0x0D || c == 0x1C || c == 0x1D || c == 0x1E || c == 0x85 || c == 0x2028 || c == 0x2029;

        private static bool IsUniDigit(int c)
        {
            if (!Rune.IsValid(c)) return false;
            return Rune.GetUnicodeCategory(new Rune(c)) == UnicodeCategory.DecimalDigitNumber;
        }

        private static bool IsUniSpace(int c)
        {
            if (c == 0x1C || c == 0x1D || c == 0x1E || c == 0x1F || c == 0x85) return true;
            if (!Rune.IsValid(c)) return false;
            var cat = Rune.GetUnicodeCategory(new Rune(c));
            return cat == UnicodeCategory.SpaceSeparator ||
                   cat == UnicodeCategory.LineSeparator ||
                   cat == UnicodeCategory.ParagraphSeparator ||
                   cat == UnicodeCategory.Control;
        }

        private static bool IsUniAlnum(int c)
        {
            if (!Rune.IsValid(c)) return false;
            var cat = Rune.GetUnicodeCategory(new Rune(c));
            return cat == UnicodeCategory.UppercaseLetter ||
                   cat == UnicodeCategory.LowercaseLetter ||
                   cat == UnicodeCategory.TitlecaseLetter ||
                   cat == UnicodeCategory.ModifierLetter ||
                   cat == UnicodeCategory.OtherLetter ||
                   cat == UnicodeCategory.DecimalDigitNumber ||
                   cat == UnicodeCategory.LetterNumber ||
                   cat == UnicodeCategory.OtherNumber;
        }

        private static bool IsUniWord(int c) => IsUniAlnum(c) || c == '_';

        private static bool SreCategory(int category, int ch)
        {
            switch (category)
            {
                case SRE_CATEGORY_DIGIT: return IsAsciiDigit(ch);
                case SRE_CATEGORY_NOT_DIGIT: return !IsAsciiDigit(ch);
                case SRE_CATEGORY_SPACE: return IsAsciiSpace(ch);
                case SRE_CATEGORY_NOT_SPACE: return !IsAsciiSpace(ch);
                case SRE_CATEGORY_WORD: return IsAsciiWord(ch);
                case SRE_CATEGORY_NOT_WORD: return !IsAsciiWord(ch);
                case SRE_CATEGORY_LINEBREAK: return IsSreLinebreak(ch);
                case SRE_CATEGORY_NOT_LINEBREAK: return !IsSreLinebreak(ch);
                case SRE_CATEGORY_LOC_WORD: return IsAsciiWord(ch);
                case SRE_CATEGORY_LOC_NOT_WORD: return !IsAsciiWord(ch);
                case SRE_CATEGORY_UNI_DIGIT: return IsUniDigit(ch);
                case SRE_CATEGORY_UNI_NOT_DIGIT: return !IsUniDigit(ch);
                case SRE_CATEGORY_UNI_SPACE: return IsUniSpace(ch);
                case SRE_CATEGORY_UNI_NOT_SPACE: return !IsUniSpace(ch);
                case SRE_CATEGORY_UNI_WORD: return IsUniWord(ch);
                case SRE_CATEGORY_UNI_NOT_WORD: return !IsUniWord(ch);
                case SRE_CATEGORY_UNI_LINEBREAK: return IsLinebreak(ch);
                case SRE_CATEGORY_UNI_NOT_LINEBREAK: return !IsLinebreak(ch);
                default: return false;
            }
        }

        private static int SreLowerAscii(int c)
        {
            if (c >= 'A' && c <= 'Z') return c + 32;
            return c;
        }

        private static int SreUpperAscii(int c)
        {
            if (c >= 'a' && c <= 'z') return c - 32;
            return c;
        }

        private static int SreLowerLocale(int c) => SreLowerAscii(c);
        private static int SreUpperLocale(int c) => SreUpperAscii(c);

        // sre_lower_unicode and sre_upper_unicode: Py_UNICODE_TOLOWER and Py_UNICODE_TOUPPER.
        internal static int SreLowerUnicode(int c) => UnicodeCaseData.ToLowercase(c);
        internal static int SreUpperUnicode(int c) => UnicodeCaseData.ToUppercase(c);

        private static bool CharLocIgnore(int chr, int c)
        {
            return SreLowerLocale(c) == chr || SreUpperLocale(c) == chr;
        }

        // --- SRE(at) ---

        private bool SreAt(int ptr, int at)
        {
            switch (at)
            {
                case SRE_AT_BEGINNING:
                case SRE_AT_BEGINNING_STRING:
                    return ptr == Beginning;

                case SRE_AT_BEGINNING_LINE:
                    return ptr == Beginning || IsSreLinebreak(Text[ptr - 1]);

                case SRE_AT_END:
                    return (End - ptr == 1 && IsSreLinebreak(Text[ptr])) || ptr == End;

                case SRE_AT_END_LINE:
                    return ptr == End || IsSreLinebreak(Text[ptr]);

                case SRE_AT_END_STRING:
                    return ptr == End;

                case SRE_AT_BOUNDARY:
                    if (Beginning == End) return false;
                    {
                        int thatp = ptr > Beginning ? (IsAsciiWord(Text[ptr - 1]) ? 1 : 0) : 0;
                        int thisp = ptr < End ? (IsAsciiWord(Text[ptr]) ? 1 : 0) : 0;
                        return thisp != thatp;
                    }

                case SRE_AT_NON_BOUNDARY:
                    if (Beginning == End) return false;
                    {
                        int thatp = ptr > Beginning ? (IsAsciiWord(Text[ptr - 1]) ? 1 : 0) : 0;
                        int thisp = ptr < End ? (IsAsciiWord(Text[ptr]) ? 1 : 0) : 0;
                        return thisp == thatp;
                    }

                case SRE_AT_LOC_BOUNDARY:
                    if (Beginning == End) return false;
                    {
                        int thatp = ptr > Beginning ? (IsAsciiWord(Text[ptr - 1]) ? 1 : 0) : 0;
                        int thisp = ptr < End ? (IsAsciiWord(Text[ptr]) ? 1 : 0) : 0;
                        return thisp != thatp;
                    }

                case SRE_AT_LOC_NON_BOUNDARY:
                    if (Beginning == End) return false;
                    {
                        int thatp = ptr > Beginning ? (IsAsciiWord(Text[ptr - 1]) ? 1 : 0) : 0;
                        int thisp = ptr < End ? (IsAsciiWord(Text[ptr]) ? 1 : 0) : 0;
                        return thisp == thatp;
                    }

                case SRE_AT_UNI_BOUNDARY:
                    if (Beginning == End) return false;
                    {
                        int thatp = ptr > Beginning ? (IsUniWord(Text[ptr - 1]) ? 1 : 0) : 0;
                        int thisp = ptr < End ? (IsUniWord(Text[ptr]) ? 1 : 0) : 0;
                        return thisp != thatp;
                    }

                case SRE_AT_UNI_NON_BOUNDARY:
                    if (Beginning == End) return false;
                    {
                        int thatp = ptr > Beginning ? (IsUniWord(Text[ptr - 1]) ? 1 : 0) : 0;
                        int thisp = ptr < End ? (IsUniWord(Text[ptr]) ? 1 : 0) : 0;
                        return thisp == thatp;
                    }
            }
            return false;
        }

        // --- SRE(charset) ---

        private bool SreCharset(int set, int ch)
        {
            bool ok = true;
            for (;;)
            {
                int op = (int)_code[set++];
                switch (op)
                {
                    case SRE_OP_FAILURE:
                        return !ok;

                    case SRE_OP_LITERAL:
                        if (ch == (int)_code[set])
                            return ok;
                        set++;
                        break;

                    case SRE_OP_CATEGORY:
                        if (SreCategory((int)_code[set], ch))
                            return ok;
                        set++;
                        break;

                    case SRE_OP_CHARSET:
                        if (ch < 256 &&
                            (_code[set + ch / SRE_CODE_BITS] & (1u << (ch & (SRE_CODE_BITS - 1)))) != 0)
                            return ok;
                        set += 256 / SRE_CODE_BITS;
                        break;

                    case SRE_OP_RANGE:
                        if ((int)_code[set] <= ch && ch <= (int)_code[set + 1])
                            return ok;
                        set += 2;
                        break;

                    case SRE_OP_RANGE_UNI_IGNORE:
                    {
                        if ((int)_code[set] <= ch && ch <= (int)_code[set + 1])
                            return ok;
                        int uch = SreUpperUnicode(ch);
                        if ((int)_code[set] <= uch && uch <= (int)_code[set + 1])
                            return ok;
                        set += 2;
                        break;
                    }

                    case SRE_OP_NEGATE:
                        ok = !ok;
                        break;

                    case SRE_OP_BIGCHARSET:
                    {
                        int count = (int)_code[set++];
                        int block;
                        if (ch < 0x10000)
                        {
                            int chHi = ch >> 8;
                            block = (int)((byte)(_code[set + chHi / 4] >> ((chHi % 4) * 8)));
                        }
                        else
                        {
                            block = -1;
                        }
                        set += 256 / 4; // 256 bytes / 4 bytes per code word = 64 code words
                        if (block >= 0)
                        {
                            int bitIndex = block * 256 + (ch & 255);
                            if ((_code[set + bitIndex / SRE_CODE_BITS] & (1u << (ch & (SRE_CODE_BITS - 1)))) != 0)
                                return ok;
                        }
                        set += count * (256 / SRE_CODE_BITS);
                        break;
                    }

                    default:
                        return false;
                }
            }
        }

        private bool SreCharsetLocIgnore(int set, int ch)
        {
            int lo = SreLowerLocale(ch);
            if (SreCharset(set, lo))
                return true;
            int up = SreUpperLocale(ch);
            return up != lo && SreCharset(set, up);
        }

        // --- SRE(count) ---

        private int SreCount(int pattern, int maxcount)
        {
            int ptr = Ptr;
            int end = End;

            // adjust end
            if (maxcount < end - ptr && maxcount != SRE_MAXREPEAT)
                end = ptr + maxcount;

            int op = (int)_code[pattern];
            switch (op)
            {
                case SRE_OP_IN:
                    while (ptr < end && SreCharset(pattern + 2, Text[ptr]))
                        ptr++;
                    break;

                case SRE_OP_ANY:
                    while (ptr < end && !IsSreLinebreak(Text[ptr]))
                        ptr++;
                    break;

                case SRE_OP_ANY_ALL:
                    ptr = end;
                    break;

                case SRE_OP_LITERAL:
                {
                    int chr = (int)_code[pattern + 1];
                    while (ptr < end && Text[ptr] == chr)
                        ptr++;
                    break;
                }

                case SRE_OP_LITERAL_IGNORE:
                {
                    int chr = (int)_code[pattern + 1];
                    while (ptr < end && SreLowerAscii(Text[ptr]) == chr)
                        ptr++;
                    break;
                }

                case SRE_OP_LITERAL_UNI_IGNORE:
                {
                    int chr = (int)_code[pattern + 1];
                    while (ptr < end && SreLowerUnicode(Text[ptr]) == chr)
                        ptr++;
                    break;
                }

                case SRE_OP_LITERAL_LOC_IGNORE:
                {
                    int chr = (int)_code[pattern + 1];
                    while (ptr < end && CharLocIgnore(chr, Text[ptr]))
                        ptr++;
                    break;
                }

                case SRE_OP_NOT_LITERAL:
                {
                    int chr = (int)_code[pattern + 1];
                    while (ptr < end && Text[ptr] != chr)
                        ptr++;
                    break;
                }

                case SRE_OP_NOT_LITERAL_IGNORE:
                {
                    int chr = (int)_code[pattern + 1];
                    while (ptr < end && SreLowerAscii(Text[ptr]) != chr)
                        ptr++;
                    break;
                }

                case SRE_OP_NOT_LITERAL_UNI_IGNORE:
                {
                    int chr = (int)_code[pattern + 1];
                    while (ptr < end && SreLowerUnicode(Text[ptr]) != chr)
                        ptr++;
                    break;
                }

                case SRE_OP_NOT_LITERAL_LOC_IGNORE:
                {
                    int chr = (int)_code[pattern + 1];
                    while (ptr < end && !CharLocIgnore(chr, Text[ptr]))
                        ptr++;
                    break;
                }

                default:
                {
                    // repeated single character pattern
                    while (Ptr < end)
                    {
                        if (!Match(pattern, false)) break;
                    }
                    return Ptr - ptr;
                }
            }

            return ptr - Ptr;
        }

        // --- Context class ---

        private sealed class Context
        {
            public int Pattern;
            public int Ptr;
            public int Count;
            public int LastMark;
            public int LastIndex;
            public int JumpLabel;
            public SreRepeat? Rep;
            public int Chr;
            public bool Toplevel;
            public int[]? SavedMarks;
            public int SavedLastPtr;
            public bool HasSavedLastPtr;
        }

        // --- SRE_REPEAT ---

        private sealed class SreRepeat
        {
            public int Count;
            public int Pattern; // index into code, -1 for null
            public int LastPtr; // -1 for null
            public SreRepeat? Prev;
        }

        // --- MARK_PUSH / MARK_POP helpers ---

        private void MarkPush(Context ctx)
        {
            if (ctx.LastMark >= 0)
            {
                int size = ctx.LastMark + 1;
                if (ctx.SavedMarks == null || ctx.SavedMarks.Length < size)
                    ctx.SavedMarks = new int[size];
                Array.Copy(Mark, ctx.SavedMarks, size);
            }
        }

        private void MarkPop(Context ctx)
        {
            if (ctx.LastMark >= 0 && ctx.SavedMarks != null)
            {
                int size = ctx.LastMark + 1;
                Array.Copy(ctx.SavedMarks, Mark, size);
                ctx.SavedMarks = null;
            }
        }

        private void MarkPopKeep(Context ctx)
        {
            if (ctx.LastMark >= 0 && ctx.SavedMarks != null)
            {
                int size = ctx.LastMark + 1;
                Array.Copy(ctx.SavedMarks, Mark, size);
            }
        }

        private void MarkPopDiscard(Context ctx)
        {
            if (ctx.LastMark >= 0)
            {
                ctx.SavedMarks = null;
            }
        }

        // --- Main Match engine ---

        internal bool Match(int pattern, bool toplevel)
        {
            var ctxStack = new List<Context>();
            var ctx = new Context
            {
                Pattern = pattern,
                Toplevel = toplevel,
                JumpLabel = JUMP_NONE
            };
            ctxStack.Add(ctx);

            int ptr = Ptr;
            int ret = 0;
            bool resuming = false;
            bool needInfoCheck = true;
            int resumeLabel = JUMP_NONE;

            while (true)
            {
                if (resuming)
                {
                    resuming = false;
                    pattern = ctx.Pattern;
                    ptr = ctx.Ptr;

                    switch (resumeLabel)
                    {
                        case JUMP_MAX_UNTIL_1:
                            if (ret > 0) { ret = 1; goto Exit; }
                            ctx.Rep!.Count = ctx.Count - 1;
                            Ptr = ptr;
                            ret = 0; goto Exit;

                        case JUMP_MAX_UNTIL_2:
                            ctx.Rep!.LastPtr = ctx.SavedLastPtr;
                            if (ret > 0) { MarkPopDiscard(ctx); ret = 1; goto Exit; }
                            MarkPop(ctx);
                            LastMark = ctx.LastMark;
                            LastIndex = ctx.LastIndex;
                            ctx.Rep!.Count = ctx.Count - 1;
                            Ptr = ptr;
                            // fall through to tail
                            _repeat = ctx.Rep.Prev;
                            // DO_JUMP(JUMP_MAX_UNTIL_3, pattern)
                            ctx.Pattern = pattern;
                            ctx.Ptr = ptr;
                            {
                                var nextCtx = new Context
                                {
                                    Pattern = pattern,
                                    Toplevel = ctx.Toplevel,
                                    JumpLabel = JUMP_MAX_UNTIL_3
                                };
                                ctxStack.Add(nextCtx);
                                ctx = nextCtx;
                                pattern = ctx.Pattern;
                                needInfoCheck = true;
                                continue;
                            }

                        case JUMP_MAX_UNTIL_3:
                            _repeat = ctx.Rep;
                            if (ret > 0) { ret = 1; goto Exit; }
                            Ptr = ptr;
                            ret = 0; goto Exit;

                        case JUMP_MIN_UNTIL_1:
                            if (ret > 0) { ret = 1; goto Exit; }
                            ctx.Rep!.Count = ctx.Count - 1;
                            Ptr = ptr;
                            ret = 0; goto Exit;

                        case JUMP_MIN_UNTIL_2:
                        {
                            SreRepeat? repeatOfTail = _repeat;
                            _repeat = ctx.Rep;
                            if (ret > 0)
                            {
                                if (repeatOfTail != null) MarkPopDiscard(ctx);
                                ret = 1; goto Exit;
                            }
                            if (repeatOfTail != null) MarkPop(ctx);
                            LastMark = ctx.LastMark;
                            LastIndex = ctx.LastIndex;
                            Ptr = ptr;
                            if ((ctx.Count >= (int)_code[ctx.Rep!.Pattern + 2] &&
                                 (int)_code[ctx.Rep.Pattern + 2] != SRE_MAXREPEAT) ||
                                Ptr == ctx.Rep.LastPtr)
                            {
                                ret = 0; goto Exit;
                            }
                            ctx.Rep.Count = ctx.Count;
                            // zero-width match protection
                            ctx.SavedLastPtr = ctx.Rep.LastPtr;
                            ctx.HasSavedLastPtr = true;
                            ctx.Rep.LastPtr = Ptr;
                            // DO_JUMP(JUMP_MIN_UNTIL_3, ctx.Rep.Pattern+3)
                            ctx.Pattern = pattern;
                            ctx.Ptr = ptr;
                            {
                                int bodyStart = ctx.Rep.Pattern + 3;
                                var nextCtx = new Context
                                {
                                    Pattern = bodyStart,
                                    Toplevel = ctx.Toplevel,
                                    JumpLabel = JUMP_MIN_UNTIL_3
                                };
                                ctxStack.Add(nextCtx);
                                ctx = nextCtx;
                                pattern = ctx.Pattern;
                                needInfoCheck = true;
                                continue;
                            }
                        }

                        case JUMP_MIN_UNTIL_3:
                            ctx.Rep!.LastPtr = ctx.SavedLastPtr;
                            if (ret > 0) { ret = 1; goto Exit; }
                            ctx.Rep.Count = ctx.Count - 1;
                            Ptr = ptr;
                            ret = 0; goto Exit;

                        case JUMP_REPEAT:
                            _repeat = ctx.Rep!.Prev;
                            if (ret > 0) { ret = 1; goto Exit; }
                            ret = 0; goto Exit;

                        case JUMP_REPEAT_ONE_1:
                            if (ret > 0)
                            {
                                if (_repeat != null) MarkPopDiscard(ctx);
                                ret = 1; goto Exit;
                            }
                            if (_repeat != null) MarkPopKeep(ctx);
                            LastMark = ctx.LastMark;
                            LastIndex = ctx.LastIndex;
                            ptr--;
                            ctx.Count--;
                            // continue the for loop
                            if (ctx.Count < (int)_code[pattern + 1])
                            {
                                if (_repeat != null) MarkPopDiscard(ctx);
                                ret = 0; goto Exit;
                            }
                            Ptr = ptr;
                            // DO_JUMP(JUMP_REPEAT_ONE_1, pattern+pattern[0])
                            ctx.Pattern = pattern;
                            ctx.Ptr = ptr;
                            {
                                var nextCtx = new Context
                                {
                                    Pattern = pattern + (int)_code[pattern],
                                    Toplevel = ctx.Toplevel,
                                    JumpLabel = JUMP_REPEAT_ONE_1
                                };
                                ctxStack.Add(nextCtx);
                                ctx = nextCtx;
                                pattern = ctx.Pattern;
                                needInfoCheck = true;
                                continue;
                            }

                        case JUMP_REPEAT_ONE_2:
                            if (ret > 0)
                            {
                                if (_repeat != null) MarkPopDiscard(ctx);
                                ret = 1; goto Exit;
                            }
                            if (_repeat != null) MarkPopKeep(ctx);
                            LastMark = ctx.LastMark;
                            LastIndex = ctx.LastIndex;
                            ptr--;
                            ctx.Count--;
                            if (ctx.Count < (int)_code[pattern + 1])
                            {
                                if (_repeat != null) MarkPopDiscard(ctx);
                                ret = 0; goto Exit;
                            }
                            Ptr = ptr;
                            // DO_JUMP(JUMP_REPEAT_ONE_2, pattern+pattern[0])
                            ctx.Pattern = pattern;
                            ctx.Ptr = ptr;
                            {
                                var nextCtx = new Context
                                {
                                    Pattern = pattern + (int)_code[pattern],
                                    Toplevel = ctx.Toplevel,
                                    JumpLabel = JUMP_REPEAT_ONE_2
                                };
                                ctxStack.Add(nextCtx);
                                ctx = nextCtx;
                                pattern = ctx.Pattern;
                                needInfoCheck = true;
                                continue;
                            }

                        case JUMP_MIN_REPEAT_ONE:
                            if (ret > 0)
                            {
                                if (_repeat != null) MarkPopDiscard(ctx);
                                ret = 1; goto Exit;
                            }
                            if (_repeat != null) MarkPopKeep(ctx);
                            LastMark = ctx.LastMark;
                            LastIndex = ctx.LastIndex;
                            Ptr = ptr;
                            ret = SreCount(pattern + 3, 1);
                            if (ret < 0) goto Exit;
                            if (ret == 0)
                            {
                                if (_repeat != null) MarkPopDiscard(ctx);
                                ret = 0; goto Exit;
                            }
                            ptr++;
                            ctx.Count++;
                            // continue while loop
                            if ((int)_code[pattern + 2] != SRE_MAXREPEAT && ctx.Count > (int)_code[pattern + 2])
                            {
                                if (_repeat != null) MarkPopDiscard(ctx);
                                ret = 0; goto Exit;
                            }
                            Ptr = ptr;
                            // DO_JUMP(JUMP_MIN_REPEAT_ONE, pattern+pattern[0])
                            ctx.Pattern = pattern;
                            ctx.Ptr = ptr;
                            {
                                var nextCtx = new Context
                                {
                                    Pattern = pattern + (int)_code[pattern],
                                    Toplevel = ctx.Toplevel,
                                    JumpLabel = JUMP_MIN_REPEAT_ONE
                                };
                                ctxStack.Add(nextCtx);
                                ctx = nextCtx;
                                pattern = ctx.Pattern;
                                needInfoCheck = true;
                                continue;
                            }

                        case JUMP_BRANCH:
                            if (ret > 0)
                            {
                                if (_repeat != null) MarkPopDiscard(ctx);
                                ret = 1; goto Exit;
                            }
                            if (_repeat != null) MarkPopKeep(ctx);
                            LastMark = ctx.LastMark;
                            LastIndex = ctx.LastIndex;
                            // continue the for loop: pattern += pattern[0]
                            pattern += (int)_code[pattern];
                            // check loop condition
                            if ((int)_code[pattern] == 0)
                            {
                                if (_repeat != null) MarkPopDiscard(ctx);
                                ret = 0; goto Exit;
                            }
                            // try next branch
                            if ((int)_code[pattern + 1] == SRE_OP_LITERAL &&
                                (ptr >= End || Text[ptr] != (int)_code[pattern + 2]))
                            {
                                pattern += (int)_code[pattern];
                                if ((int)_code[pattern] == 0)
                                {
                                    if (_repeat != null) MarkPopDiscard(ctx);
                                    ret = 0; goto Exit;
                                }
                            }
                            if ((int)_code[pattern + 1] == SRE_OP_IN &&
                                (ptr >= End || !SreCharset(pattern + 3, Text[ptr])))
                            {
                                pattern += (int)_code[pattern];
                                if ((int)_code[pattern] == 0)
                                {
                                    if (_repeat != null) MarkPopDiscard(ctx);
                                    ret = 0; goto Exit;
                                }
                            }
                            Ptr = ptr;
                            // DO_JUMP(JUMP_BRANCH, pattern+1)
                            ctx.Pattern = pattern;
                            ctx.Ptr = ptr;
                            {
                                var nextCtx = new Context
                                {
                                    Pattern = pattern + 1,
                                    Toplevel = ctx.Toplevel,
                                    JumpLabel = JUMP_BRANCH
                                };
                                ctxStack.Add(nextCtx);
                                ctx = nextCtx;
                                pattern = ctx.Pattern;
                                needInfoCheck = true;
                                continue;
                            }

                        case JUMP_ASSERT:
                            if (ret == 0) { ret = 0; goto Exit; }
                            pattern += (int)_code[pattern];
                            needInfoCheck = false;
                            continue;

                        case JUMP_ASSERT_NOT:
                            if (ret > 0)
                            {
                                if (_repeat != null) MarkPopDiscard(ctx);
                                ret = 0; goto Exit;
                            }
                            if (_repeat != null) MarkPop(ctx);
                            LastMark = ctx.LastMark;
                            LastIndex = ctx.LastIndex;
                            pattern += (int)_code[pattern];
                            needInfoCheck = false;
                            continue;

                        case JUMP_POSS_REPEAT_1:
                            if (ret > 0)
                            {
                                ctx.Count++;
                                // continue while loop
                                if (ctx.Count < (int)_code[pattern + 1])
                                {
                                    // DO_JUMP0(JUMP_POSS_REPEAT_1, pattern[3])
                                    ctx.Pattern = pattern;
                                    ctx.Ptr = ptr;
                                    {
                                        var nextCtx = new Context
                                        {
                                            Pattern = pattern + 3,
                                            Toplevel = false,
                                            JumpLabel = JUMP_POSS_REPEAT_1
                                        };
                                        ctxStack.Add(nextCtx);
                                        ctx = nextCtx;
                                        pattern = ctx.Pattern;
                                        needInfoCheck = true;
                                        continue;
                                    }
                                }
                                // min satisfied, set ptr = -1 to enter second loop
                                ptr = -1;
                                // fall through to second while loop
                                // LASTMARK_SAVE
                                ctx.LastMark = LastMark;
                                ctx.LastIndex = LastIndex;
                                MarkPush(ctx);
                                ptr = Ptr;
                                // DO_JUMP0(JUMP_POSS_REPEAT_2, pattern[3])
                                ctx.Pattern = pattern;
                                ctx.Ptr = ptr;
                                {
                                    var nextCtx = new Context
                                    {
                                        Pattern = pattern + 3,
                                        Toplevel = false,
                                        JumpLabel = JUMP_POSS_REPEAT_2
                                    };
                                    ctxStack.Add(nextCtx);
                                    ctx = nextCtx;
                                    pattern = ctx.Pattern;
                                    needInfoCheck = true;
                                    continue;
                                }
                            }
                            else
                            {
                                Ptr = ptr;
                                _repeat = ctx.Rep!.Prev;
                                ret = 0; goto Exit;
                            }

                        case JUMP_POSS_REPEAT_2:
                            if (ret > 0)
                            {
                                MarkPopDiscard(ctx);
                                ctx.Count++;
                                // continue while loop
                                if ((ctx.Count < (int)_code[pattern + 2] ||
                                     (int)_code[pattern + 2] == SRE_MAXREPEAT) &&
                                    Ptr != ptr)
                                {
                                    ctx.LastMark = LastMark;
                                    ctx.LastIndex = LastIndex;
                                    MarkPush(ctx);
                                    ptr = Ptr;
                                    // DO_JUMP0(JUMP_POSS_REPEAT_2, pattern[3])
                                    ctx.Pattern = pattern;
                                    ctx.Ptr = ptr;
                                    {
                                        var nextCtx = new Context
                                        {
                                            Pattern = pattern + 3,
                                            Toplevel = false,
                                            JumpLabel = JUMP_POSS_REPEAT_2
                                        };
                                        ctxStack.Add(nextCtx);
                                        ctx = nextCtx;
                                        pattern = ctx.Pattern;
                                        needInfoCheck = true;
                                        continue;
                                    }
                                }
                                // exit loop
                                _repeat = ctx.Rep!.Prev;
                                pattern += (int)_code[pattern] + 1;
                                ptr = Ptr;
                                needInfoCheck = false;
                                continue;
                            }
                            else
                            {
                                MarkPop(ctx);
                                LastMark = ctx.LastMark;
                                LastIndex = ctx.LastIndex;
                                Ptr = ptr;
                                // exit loop
                                _repeat = ctx.Rep!.Prev;
                                pattern += (int)_code[pattern] + 1;
                                ptr = Ptr;
                                needInfoCheck = false;
                                continue;
                            }

                        case JUMP_ATOMIC_GROUP:
                            if (ret < 0) goto Exit;
                            if (ret == 0)
                            {
                                Ptr = ptr;
                                ret = 0; goto Exit;
                            }
                            pattern += (int)_code[pattern];
                            ptr = Ptr;
                            needInfoCheck = false;
                            continue;

                        case JUMP_NONE:
                            goto Exit;
                    }
                }

                // entrance: every frame starts from state->ptr.
                if (needInfoCheck)
                {
                    needInfoCheck = false;
                    ptr = Ptr;
                    if ((int)_code[pattern] == SRE_OP_INFO)
                    {
                        int minLen = (int)_code[pattern + 3];
                        if (minLen != 0 && (End - ptr) < minLen)
                        {
                            ret = 0; goto Exit;
                        }
                        pattern += (int)_code[pattern + 1] + 1;
                    }
                }

                // Dispatch
                int opcode = (int)_code[pattern++];

                switch (opcode)
                {
                    case SRE_OP_MARK:
                    {
                        int i = (int)_code[pattern];
                        if ((i & 1) != 0)
                            LastIndex = i / 2 + 1;
                        if (i > LastMark)
                        {
                            int j = LastMark + 1;
                            while (j < i)
                                Mark[j++] = -1;
                            LastMark = i;
                        }
                        Mark[i] = ptr;
                        pattern++;
                        continue;
                    }

                    case SRE_OP_LITERAL:
                        if (ptr >= End || Text[ptr] != (int)_code[pattern])
                        { ret = 0; goto Exit; }
                        pattern++;
                        ptr++;
                        continue;

                    case SRE_OP_NOT_LITERAL:
                        if (ptr >= End || Text[ptr] == (int)_code[pattern])
                        { ret = 0; goto Exit; }
                        pattern++;
                        ptr++;
                        continue;

                    case SRE_OP_SUCCESS:
                        if (ctx.Toplevel &&
                            ((MatchAll && ptr != End) ||
                             (MustAdvance && ptr == Start)))
                        {
                            ret = 0; goto Exit;
                        }
                        Ptr = ptr;
                        ret = 1; goto Exit;

                    case SRE_OP_AT:
                        if (!SreAt(ptr, (int)_code[pattern]))
                        { ret = 0; goto Exit; }
                        pattern++;
                        continue;

                    case SRE_OP_CATEGORY:
                        if (ptr >= End || !SreCategory((int)_code[pattern], Text[ptr]))
                        { ret = 0; goto Exit; }
                        pattern++;
                        ptr++;
                        continue;

                    case SRE_OP_ANY:
                        if (ptr >= End || IsSreLinebreak(Text[ptr]))
                        { ret = 0; goto Exit; }
                        ptr++;
                        continue;

                    case SRE_OP_ANY_ALL:
                        if (ptr >= End)
                        { ret = 0; goto Exit; }
                        ptr++;
                        continue;

                    case SRE_OP_IN:
                        if (ptr >= End || !SreCharset(pattern + 1, Text[ptr]))
                        { ret = 0; goto Exit; }
                        pattern += (int)_code[pattern];
                        ptr++;
                        continue;

                    case SRE_OP_LITERAL_IGNORE:
                        if (ptr >= End || SreLowerAscii(Text[ptr]) != (int)_code[pattern])
                        { ret = 0; goto Exit; }
                        pattern++;
                        ptr++;
                        continue;

                    case SRE_OP_LITERAL_UNI_IGNORE:
                        if (ptr >= End || SreLowerUnicode(Text[ptr]) != (int)_code[pattern])
                        { ret = 0; goto Exit; }
                        pattern++;
                        ptr++;
                        continue;

                    case SRE_OP_LITERAL_LOC_IGNORE:
                        if (ptr >= End || !CharLocIgnore((int)_code[pattern], Text[ptr]))
                        { ret = 0; goto Exit; }
                        pattern++;
                        ptr++;
                        continue;

                    case SRE_OP_NOT_LITERAL_IGNORE:
                        if (ptr >= End || SreLowerAscii(Text[ptr]) == (int)_code[pattern])
                        { ret = 0; goto Exit; }
                        pattern++;
                        ptr++;
                        continue;

                    case SRE_OP_NOT_LITERAL_UNI_IGNORE:
                        if (ptr >= End || SreLowerUnicode(Text[ptr]) == (int)_code[pattern])
                        { ret = 0; goto Exit; }
                        pattern++;
                        ptr++;
                        continue;

                    case SRE_OP_NOT_LITERAL_LOC_IGNORE:
                        if (ptr >= End || CharLocIgnore((int)_code[pattern], Text[ptr]))
                        { ret = 0; goto Exit; }
                        pattern++;
                        ptr++;
                        continue;

                    case SRE_OP_IN_IGNORE:
                        if (ptr >= End || !SreCharset(pattern + 1, SreLowerAscii(Text[ptr])))
                        { ret = 0; goto Exit; }
                        pattern += (int)_code[pattern];
                        ptr++;
                        continue;

                    case SRE_OP_IN_UNI_IGNORE:
                        if (ptr >= End || !SreCharset(pattern + 1, SreLowerUnicode(Text[ptr])))
                        { ret = 0; goto Exit; }
                        pattern += (int)_code[pattern];
                        ptr++;
                        continue;

                    case SRE_OP_IN_LOC_IGNORE:
                        if (ptr >= End || !SreCharsetLocIgnore(pattern + 1, Text[ptr]))
                        { ret = 0; goto Exit; }
                        pattern += (int)_code[pattern];
                        ptr++;
                        continue;

                    case SRE_OP_JUMP:
                    case SRE_OP_INFO:
                        pattern += (int)_code[pattern];
                        continue;

                    case SRE_OP_BRANCH:
                    {
                        ctx.LastMark = LastMark;
                        ctx.LastIndex = LastIndex;
                        if (_repeat != null) MarkPush(ctx);

                        // try branches
                        bool branchMatched = false;
                        while ((int)_code[pattern] != 0)
                        {
                            if ((int)_code[pattern + 1] == SRE_OP_LITERAL &&
                                (ptr >= End || Text[ptr] != (int)_code[pattern + 2]))
                            {
                                pattern += (int)_code[pattern];
                                continue;
                            }
                            if ((int)_code[pattern + 1] == SRE_OP_IN &&
                                (ptr >= End || !SreCharset(pattern + 3, Text[ptr])))
                            {
                                pattern += (int)_code[pattern];
                                continue;
                            }
                            Ptr = ptr;
                            // DO_JUMP(JUMP_BRANCH, pattern+1)
                            ctx.Pattern = pattern;
                            ctx.Ptr = ptr;
                            {
                                var nextCtx = new Context
                                {
                                    Pattern = pattern + 1,
                                    Toplevel = ctx.Toplevel,
                                    JumpLabel = JUMP_BRANCH
                                };
                                ctxStack.Add(nextCtx);
                                ctx = nextCtx;
                                pattern = ctx.Pattern;
                                needInfoCheck = true;
                                goto NextIteration;
                            }
                        }
                        // all branches exhausted
                        if (_repeat != null) MarkPopDiscard(ctx);
                        ret = 0; goto Exit;
                    }

                    case SRE_OP_REPEAT_ONE:
                    {
                        int min = (int)_code[pattern + 1];
                        int max = (int)_code[pattern + 2];
                        if (min > End - ptr)
                        { ret = 0; goto Exit; }

                        Ptr = ptr;
                        ret = SreCount(pattern + 3, max);
                        if (ret < 0) goto Exit;
                        ctx.Count = ret;
                        ptr += ctx.Count;

                        if (ctx.Count < min)
                        { ret = 0; goto Exit; }

                        int tailOp = (int)_code[pattern + (int)_code[pattern]];
                        if (tailOp == SRE_OP_SUCCESS && ptr == End &&
                            !(ctx.Toplevel && MustAdvance && ptr == Start))
                        {
                            Ptr = ptr;
                            ret = 1; goto Exit;
                        }

                        ctx.LastMark = LastMark;
                        ctx.LastIndex = LastIndex;
                        if (_repeat != null) MarkPush(ctx);

                        if (tailOp == SRE_OP_LITERAL)
                        {
                            ctx.Chr = (int)_code[pattern + (int)_code[pattern] + 1];
                            // skip positions where rest cannot match
                            while (ctx.Count >= min && (ptr >= End || Text[ptr] != ctx.Chr))
                            {
                                ptr--;
                                ctx.Count--;
                            }
                            if (ctx.Count < min)
                            {
                                if (_repeat != null) MarkPopDiscard(ctx);
                                ret = 0; goto Exit;
                            }
                            Ptr = ptr;
                            // DO_JUMP(JUMP_REPEAT_ONE_1, pattern+pattern[0])
                            ctx.Pattern = pattern;
                            ctx.Ptr = ptr;
                            {
                                var nextCtx = new Context
                                {
                                    Pattern = pattern + (int)_code[pattern],
                                    Toplevel = ctx.Toplevel,
                                    JumpLabel = JUMP_REPEAT_ONE_1
                                };
                                ctxStack.Add(nextCtx);
                                ctx = nextCtx;
                                pattern = ctx.Pattern;
                                needInfoCheck = true;
                                goto NextIteration;
                            }
                        }
                        else
                        {
                            // general case
                            if (ctx.Count < min)
                            {
                                if (_repeat != null) MarkPopDiscard(ctx);
                                ret = 0; goto Exit;
                            }
                            Ptr = ptr;
                            // DO_JUMP(JUMP_REPEAT_ONE_2, pattern+pattern[0])
                            ctx.Pattern = pattern;
                            ctx.Ptr = ptr;
                            {
                                var nextCtx = new Context
                                {
                                    Pattern = pattern + (int)_code[pattern],
                                    Toplevel = ctx.Toplevel,
                                    JumpLabel = JUMP_REPEAT_ONE_2
                                };
                                ctxStack.Add(nextCtx);
                                ctx = nextCtx;
                                pattern = ctx.Pattern;
                                needInfoCheck = true;
                                goto NextIteration;
                            }
                        }
                    }

                    case SRE_OP_MIN_REPEAT_ONE:
                    {
                        int min = (int)_code[pattern + 1];
                        int max = (int)_code[pattern + 2];
                        if (min > End - ptr)
                        { ret = 0; goto Exit; }

                        Ptr = ptr;

                        if (min == 0)
                        {
                            ctx.Count = 0;
                        }
                        else
                        {
                            ret = SreCount(pattern + 3, min);
                            if (ret < 0) goto Exit;
                            if (ret < min)
                            { ret = 0; goto Exit; }
                            ctx.Count = ret;
                            ptr += ctx.Count;
                        }

                        int tailOp2 = (int)_code[pattern + (int)_code[pattern]];
                        if (tailOp2 == SRE_OP_SUCCESS &&
                            !(ctx.Toplevel &&
                              ((MatchAll && ptr != End) ||
                               (MustAdvance && ptr == Start))))
                        {
                            Ptr = ptr;
                            ret = 1; goto Exit;
                        }
                        else
                        {
                            ctx.LastMark = LastMark;
                            ctx.LastIndex = LastIndex;
                            if (_repeat != null) MarkPush(ctx);

                            if (max != SRE_MAXREPEAT && ctx.Count > max)
                            {
                                if (_repeat != null) MarkPopDiscard(ctx);
                                ret = 0; goto Exit;
                            }

                            Ptr = ptr;
                            // DO_JUMP(JUMP_MIN_REPEAT_ONE, pattern+pattern[0])
                            ctx.Pattern = pattern;
                            ctx.Ptr = ptr;
                            {
                                var nextCtx = new Context
                                {
                                    Pattern = pattern + (int)_code[pattern],
                                    Toplevel = ctx.Toplevel,
                                    JumpLabel = JUMP_MIN_REPEAT_ONE
                                };
                                ctxStack.Add(nextCtx);
                                ctx = nextCtx;
                                pattern = ctx.Pattern;
                                needInfoCheck = true;
                                goto NextIteration;
                            }
                        }
                    }

                    case SRE_OP_POSSESSIVE_REPEAT_ONE:
                    {
                        int min = (int)_code[pattern + 1];
                        int max = (int)_code[pattern + 2];
                        if (ptr + min > End)
                        { ret = 0; goto Exit; }

                        Ptr = ptr;
                        ret = SreCount(pattern + 3, max);
                        if (ret < 0) goto Exit;
                        ctx.Count = ret;
                        ptr += ctx.Count;

                        if (ctx.Count < min)
                        { ret = 0; goto Exit; }

                        pattern += (int)_code[pattern];

                        if ((int)_code[pattern] == SRE_OP_SUCCESS &&
                            ptr == End &&
                            !(ctx.Toplevel && MustAdvance && ptr == Start))
                        {
                            Ptr = ptr;
                            ret = 1; goto Exit;
                        }

                        // DISPATCH: continue with tail
                        needInfoCheck = false;
                        continue;
                    }

                    case SRE_OP_REPEAT:
                    {
                        ctx.Rep = new SreRepeat
                        {
                            Count = -1,
                            Pattern = pattern - 1, // pattern[0] is the opcode, we need the full pattern start
                            LastPtr = -1,
                            Prev = _repeat
                        };
                        ctx.Rep.Pattern = pattern; // points to skip
                        _repeat = ctx.Rep;

                        Ptr = ptr;
                        // DO_JUMP(JUMP_REPEAT, pattern+pattern[0])
                        ctx.Pattern = pattern;
                        ctx.Ptr = ptr;
                        {
                            var nextCtx = new Context
                            {
                                Pattern = pattern + (int)_code[pattern],
                                Toplevel = ctx.Toplevel,
                                JumpLabel = JUMP_REPEAT
                            };
                            ctxStack.Add(nextCtx);
                            ctx = nextCtx;
                            pattern = ctx.Pattern;
                            needInfoCheck = true;
                            goto NextIteration;
                        }
                    }

                    case SRE_OP_MAX_UNTIL:
                    {
                        ctx.Rep = _repeat;
                        if (ctx.Rep == null)
                            throw new InvalidOperationException("SRE_ERROR_STATE");

                        Ptr = ptr;
                        ctx.Count = ctx.Rep.Count + 1;

                        int repMin = (int)_code[ctx.Rep.Pattern + 1];
                        int repMax = (int)_code[ctx.Rep.Pattern + 2];

                        if (ctx.Count < repMin)
                        {
                            ctx.Rep.Count = ctx.Count;
                            // DO_JUMP(JUMP_MAX_UNTIL_1, rep.Pattern+3)
                            ctx.Pattern = pattern;
                            ctx.Ptr = ptr;
                            {
                                var nextCtx = new Context
                                {
                                    Pattern = ctx.Rep.Pattern + 3,
                                    Toplevel = ctx.Toplevel,
                                    JumpLabel = JUMP_MAX_UNTIL_1
                                };
                                ctxStack.Add(nextCtx);
                                ctx = nextCtx;
                                pattern = ctx.Pattern;
                                needInfoCheck = true;
                                goto NextIteration;
                            }
                        }

                        if ((ctx.Count < repMax || repMax == SRE_MAXREPEAT) &&
                            Ptr != ctx.Rep.LastPtr)
                        {
                            ctx.Rep.Count = ctx.Count;
                            ctx.LastMark = LastMark;
                            ctx.LastIndex = LastIndex;
                            MarkPush(ctx);
                            // zero-width match protection
                            ctx.SavedLastPtr = ctx.Rep.LastPtr;
                            ctx.HasSavedLastPtr = true;
                            ctx.Rep.LastPtr = Ptr;
                            // DO_JUMP(JUMP_MAX_UNTIL_2, rep.Pattern+3)
                            ctx.Pattern = pattern;
                            ctx.Ptr = ptr;
                            {
                                var nextCtx = new Context
                                {
                                    Pattern = ctx.Rep.Pattern + 3,
                                    Toplevel = ctx.Toplevel,
                                    JumpLabel = JUMP_MAX_UNTIL_2
                                };
                                ctxStack.Add(nextCtx);
                                ctx = nextCtx;
                                pattern = ctx.Pattern;
                                needInfoCheck = true;
                                goto NextIteration;
                            }
                        }

                        // tail
                        _repeat = ctx.Rep.Prev;
                        // DO_JUMP(JUMP_MAX_UNTIL_3, pattern)
                        ctx.Pattern = pattern;
                        ctx.Ptr = ptr;
                        {
                            var nextCtx = new Context
                            {
                                Pattern = pattern,
                                Toplevel = ctx.Toplevel,
                                JumpLabel = JUMP_MAX_UNTIL_3
                            };
                            ctxStack.Add(nextCtx);
                            ctx = nextCtx;
                            needInfoCheck = true;
                            goto NextIteration;
                        }
                    }

                    case SRE_OP_MIN_UNTIL:
                    {
                        ctx.Rep = _repeat;
                        if (ctx.Rep == null)
                            throw new InvalidOperationException("SRE_ERROR_STATE");

                        Ptr = ptr;
                        ctx.Count = ctx.Rep.Count + 1;

                        int repMin = (int)_code[ctx.Rep.Pattern + 1];
                        int repMax = (int)_code[ctx.Rep.Pattern + 2];

                        if (ctx.Count < repMin)
                        {
                            ctx.Rep.Count = ctx.Count;
                            // DO_JUMP(JUMP_MIN_UNTIL_1, rep.Pattern+3)
                            ctx.Pattern = pattern;
                            ctx.Ptr = ptr;
                            {
                                var nextCtx = new Context
                                {
                                    Pattern = ctx.Rep.Pattern + 3,
                                    Toplevel = ctx.Toplevel,
                                    JumpLabel = JUMP_MIN_UNTIL_1
                                };
                                ctxStack.Add(nextCtx);
                                ctx = nextCtx;
                                pattern = ctx.Pattern;
                                needInfoCheck = true;
                                goto NextIteration;
                            }
                        }

                        // see if the tail matches
                        _repeat = ctx.Rep.Prev;

                        ctx.LastMark = LastMark;
                        ctx.LastIndex = LastIndex;
                        if (_repeat != null) MarkPush(ctx);

                        // DO_JUMP(JUMP_MIN_UNTIL_2, pattern)
                        ctx.Pattern = pattern;
                        ctx.Ptr = ptr;
                        {
                            var nextCtx = new Context
                            {
                                Pattern = pattern,
                                Toplevel = ctx.Toplevel,
                                JumpLabel = JUMP_MIN_UNTIL_2
                            };
                            ctxStack.Add(nextCtx);
                            ctx = nextCtx;
                            needInfoCheck = true;
                            goto NextIteration;
                        }
                    }

                    case SRE_OP_POSSESSIVE_REPEAT:
                    {
                        Ptr = ptr;

                        ctx.Rep = new SreRepeat
                        {
                            Count = -1,
                            Pattern = -1,
                            LastPtr = -1,
                            Prev = _repeat
                        };
                        _repeat = ctx.Rep;
                        ctx.Count = 0;

                        int min = (int)_code[pattern + 1];
                        int max = (int)_code[pattern + 2];

                        // Check for minimum required matches
                        if (ctx.Count < min)
                        {
                            // DO_JUMP0(JUMP_POSS_REPEAT_1, pattern[3])
                            ctx.Pattern = pattern;
                            ctx.Ptr = ptr;
                            {
                                var nextCtx = new Context
                                {
                                    Pattern = pattern + 3,
                                    Toplevel = false,
                                    JumpLabel = JUMP_POSS_REPEAT_1
                                };
                                ctxStack.Add(nextCtx);
                                ctx = nextCtx;
                                pattern = ctx.Pattern;
                                needInfoCheck = true;
                                goto NextIteration;
                            }
                        }

                        // min == 0, go to second loop
                        ptr = -1; // clear ptr so loop condition works

                        if ((ctx.Count < max || max == SRE_MAXREPEAT) && Ptr != ptr)
                        {
                            ctx.LastMark = LastMark;
                            ctx.LastIndex = LastIndex;
                            MarkPush(ctx);
                            ptr = Ptr;
                            // DO_JUMP0(JUMP_POSS_REPEAT_2, pattern[3])
                            ctx.Pattern = pattern;
                            ctx.Ptr = ptr;
                            {
                                var nextCtx = new Context
                                {
                                    Pattern = pattern + 3,
                                    Toplevel = false,
                                    JumpLabel = JUMP_POSS_REPEAT_2
                                };
                                ctxStack.Add(nextCtx);
                                ctx = nextCtx;
                                pattern = ctx.Pattern;
                                needInfoCheck = true;
                                goto NextIteration;
                            }
                        }

                        // no iterations needed
                        _repeat = ctx.Rep.Prev;
                        pattern += (int)_code[pattern] + 1;
                        ptr = Ptr;
                        needInfoCheck = false;
                        continue;
                    }

                    case SRE_OP_ATOMIC_GROUP:
                    {
                        Ptr = ptr;
                        // DO_JUMP0(JUMP_ATOMIC_GROUP, pattern[1])
                        ctx.Pattern = pattern;
                        ctx.Ptr = ptr;
                        {
                            var nextCtx = new Context
                            {
                                Pattern = pattern + 1,
                                Toplevel = false,
                                JumpLabel = JUMP_ATOMIC_GROUP
                            };
                            ctxStack.Add(nextCtx);
                            ctx = nextCtx;
                            pattern = ctx.Pattern;
                            needInfoCheck = true;
                            goto NextIteration;
                        }
                    }

                    case SRE_OP_GROUPREF:
                    {
                        int groupref = (int)_code[pattern] * 2;
                        if (groupref >= LastMark)
                        { ret = 0; goto Exit; }
                        int p = Mark[groupref];
                        int e = Mark[groupref + 1];
                        if (p < 0 || e < 0 || e < p)
                        { ret = 0; goto Exit; }
                        while (p < e)
                        {
                            if (ptr >= End || Text[ptr] != Text[p])
                            { ret = 0; goto Exit; }
                            p++;
                            ptr++;
                        }
                        pattern++;
                        continue;
                    }

                    case SRE_OP_GROUPREF_IGNORE:
                    {
                        int groupref = (int)_code[pattern] * 2;
                        if (groupref >= LastMark)
                        { ret = 0; goto Exit; }
                        int p = Mark[groupref];
                        int e = Mark[groupref + 1];
                        if (p < 0 || e < 0 || e < p)
                        { ret = 0; goto Exit; }
                        while (p < e)
                        {
                            if (ptr >= End || SreLowerAscii(Text[ptr]) != SreLowerAscii(Text[p]))
                            { ret = 0; goto Exit; }
                            p++;
                            ptr++;
                        }
                        pattern++;
                        continue;
                    }

                    case SRE_OP_GROUPREF_UNI_IGNORE:
                    {
                        int groupref = (int)_code[pattern] * 2;
                        if (groupref >= LastMark)
                        { ret = 0; goto Exit; }
                        int p = Mark[groupref];
                        int e = Mark[groupref + 1];
                        if (p < 0 || e < 0 || e < p)
                        { ret = 0; goto Exit; }
                        while (p < e)
                        {
                            if (ptr >= End || SreLowerUnicode(Text[ptr]) != SreLowerUnicode(Text[p]))
                            { ret = 0; goto Exit; }
                            p++;
                            ptr++;
                        }
                        pattern++;
                        continue;
                    }

                    case SRE_OP_GROUPREF_LOC_IGNORE:
                    {
                        int groupref = (int)_code[pattern] * 2;
                        if (groupref >= LastMark)
                        { ret = 0; goto Exit; }
                        int p = Mark[groupref];
                        int e = Mark[groupref + 1];
                        if (p < 0 || e < 0 || e < p)
                        { ret = 0; goto Exit; }
                        while (p < e)
                        {
                            if (ptr >= End || SreLowerLocale(Text[ptr]) != SreLowerLocale(Text[p]))
                            { ret = 0; goto Exit; }
                            p++;
                            ptr++;
                        }
                        pattern++;
                        continue;
                    }

                    case SRE_OP_GROUPREF_EXISTS:
                    {
                        int groupref = (int)_code[pattern] * 2;
                        if (groupref >= LastMark)
                        {
                            pattern += (int)_code[pattern + 1];
                            continue;
                        }
                        int p = Mark[groupref];
                        int e = Mark[groupref + 1];
                        if (p < 0 || e < 0 || e < p)
                        {
                            pattern += (int)_code[pattern + 1];
                            continue;
                        }
                        pattern += 2;
                        continue;
                    }

                    case SRE_OP_ASSERT:
                    {
                        int back = (int)_code[pattern + 1];
                        if ((ptr - Beginning) < back)
                        { ret = 0; goto Exit; }
                        Ptr = ptr - back;
                        // DO_JUMP0(JUMP_ASSERT, pattern+2)
                        ctx.Pattern = pattern;
                        ctx.Ptr = ptr;
                        {
                            var nextCtx = new Context
                            {
                                Pattern = pattern + 2,
                                Toplevel = false,
                                JumpLabel = JUMP_ASSERT
                            };
                            ctxStack.Add(nextCtx);
                            ctx = nextCtx;
                            pattern = ctx.Pattern;
                            needInfoCheck = true;
                            goto NextIteration;
                        }
                    }

                    case SRE_OP_ASSERT_NOT:
                    {
                        int back = (int)_code[pattern + 1];
                        if ((ptr - Beginning) >= back)
                        {
                            Ptr = ptr - back;
                            ctx.LastMark = LastMark;
                            ctx.LastIndex = LastIndex;
                            if (_repeat != null) MarkPush(ctx);
                            // DO_JUMP0(JUMP_ASSERT_NOT, pattern+2)
                            ctx.Pattern = pattern;
                            ctx.Ptr = ptr;
                            {
                                var nextCtx = new Context
                                {
                                    Pattern = pattern + 2,
                                    Toplevel = false,
                                    JumpLabel = JUMP_ASSERT_NOT
                                };
                                ctxStack.Add(nextCtx);
                                ctx = nextCtx;
                                pattern = ctx.Pattern;
                                needInfoCheck = true;
                                goto NextIteration;
                            }
                        }
                        pattern += (int)_code[pattern];
                        continue;
                    }

                    case SRE_OP_FAILURE:
                        ret = 0; goto Exit;

                    default:
                        throw new InvalidOperationException("SRE_ERROR_ILLEGAL");
                }

                NextIteration:
                continue;

                Exit:
                // SRE(match) exit: the frame that ended says where its parent resumes.
                resumeLabel = ctx.JumpLabel;
                ctxStack.RemoveAt(ctxStack.Count - 1);
                if (ctxStack.Count == 0)
                    return ret > 0;
                ctx = ctxStack[ctxStack.Count - 1];
                resuming = true;
                continue;
            }
        }

        // --- Search ---

        internal bool Search(int pattern)
        {
            int ptr = Start;
            int end = End;
            int status = 0;
            int prefixLen = 0;
            int prefixSkip = 0;
            int prefix = -1;
            int charset = -1;
            int overlap = -1;
            int flags = 0;

            if (ptr > end)
                return false;

            if ((int)_code[pattern] == SRE_OP_INFO)
            {
                flags = (int)_code[pattern + 2];

                int minLen = (int)_code[pattern + 3];
                if (minLen != 0 && (end - ptr) < minLen)
                {
                    return false;
                }
                if (minLen > 1)
                {
                    end -= minLen - 1;
                    if (end <= ptr)
                        end = ptr;
                }

                if ((flags & SRE_INFO_PREFIX) != 0)
                {
                    prefixLen = (int)_code[pattern + 5];
                    prefixSkip = (int)_code[pattern + 6];
                    prefix = pattern + 7;
                    overlap = prefix + prefixLen - 1;
                }
                else if ((flags & SRE_INFO_CHARSET) != 0)
                {
                    charset = pattern + 5;
                }

                pattern += 1 + (int)_code[pattern + 1];
            }

            if (prefixLen == 1)
            {
                int c = (int)_code[prefix];
                end = End;
                MustAdvance = false;
                while (ptr < end)
                {
                    while (Text[ptr] != c)
                    {
                        if (++ptr >= end)
                            return false;
                    }
                    Start = ptr;
                    Ptr = ptr + prefixSkip;
                    if ((flags & SRE_INFO_LITERAL) != 0)
                        return true;
                    status = Match(pattern + 2 * prefixSkip, false) ? 1 : 0;
                    if (status != 0)
                        return true;
                    ++ptr;
                    ResetCaptureGroup();
                }
                return false;
            }

            if (prefixLen > 1)
            {
                int i = 0;
                end = End;
                if (prefixLen > end - ptr)
                    return false;

                while (ptr < end)
                {
                    int c = (int)_code[prefix];
                    while (Text[ptr++] != c)
                    {
                        if (ptr >= end)
                            return false;
                    }
                    if (ptr >= end)
                        return false;

                    i = 1;
                    MustAdvance = false;
                    do
                    {
                        if (Text[ptr] == (int)_code[prefix + i])
                        {
                            if (++i != prefixLen)
                            {
                                if (++ptr >= end)
                                    return false;
                                continue;
                            }
                            Start = ptr - (prefixLen - 1);
                            Ptr = ptr - (prefixLen - prefixSkip - 1);
                            if ((flags & SRE_INFO_LITERAL) != 0)
                                return true;
                            status = Match(pattern + 2 * prefixSkip, false) ? 1 : 0;
                            if (status != 0)
                                return true;
                            if (++ptr >= end)
                                return false;
                            ResetCaptureGroup();
                        }
                        i = (int)_code[overlap + i];
                    } while (i != 0);
                }
                return false;
            }

            if (charset >= 0)
            {
                end = End;
                MustAdvance = false;
                for (;;)
                {
                    while (ptr < end && !SreCharset(charset, Text[ptr]))
                        ptr++;
                    if (ptr >= end)
                        return false;
                    Start = ptr;
                    Ptr = ptr;
                    status = Match(pattern, false) ? 1 : 0;
                    if (status != 0)
                        break;
                    ptr++;
                    ResetCaptureGroup();
                }
            }
            else
            {
                Start = Ptr = ptr;
                status = Match(pattern, true) ? 1 : 0;
                MustAdvance = false;
                if (status == 0 && (int)_code[pattern] == SRE_OP_AT &&
                    ((int)_code[pattern + 1] == SRE_AT_BEGINNING ||
                     (int)_code[pattern + 1] == SRE_AT_BEGINNING_STRING))
                {
                    Start = Ptr = ptr = End;
                    return false;
                }
                while (status == 0 && ptr < End)
                {
                    ptr++;
                    ResetCaptureGroup();
                    Start = Ptr = ptr;
                    status = Match(pattern, false) ? 1 : 0;
                }
            }

            return status > 0;
        }

        private void ResetCaptureGroup()
        {
            LastMark = -1;
            LastIndex = -1;
        }
    }
}