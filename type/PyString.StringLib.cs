using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// The str methods over characters (Objects/unicodeobject.c and Objects/stringlib of CPython
    /// v3.12.15). Positions are code point indices into the str's own storage; a search runs on
    /// the units of a kind 1 or 2 str or on the code points of a kind 4 one, the narrower operand
    /// widened to the wider kind (unicode_askind), and a sub of a wider kind than the text is never
    /// in it. Results are PyUnicode_Substring of the text or written by a PyUnicodeWriter, so a
    /// lone surrogate stays one character whatever is next to it.
    /// </summary>
    public partial class PyString
    {
        // ---- the search (any_find_slice, anylib_count, tailmatch) ----

        private static int FindIn<T>(ReadOnlySpan<T> text, ReadOnlySpan<T> sub, bool backward)
            where T : IEquatable<T>, IComparable<T> =>
            backward ? text.LastIndexOf(sub) : PyFastSearch.IndexOf(text, sub);

        // stringlib count: non-overlapping matches, at most maxcount.
        private static long CountIn<T>(ReadOnlySpan<T> text, ReadOnlySpan<T> sub, long maxcount)
            where T : IEquatable<T>, IComparable<T>
        {
            long count = 0;
            while (count < maxcount)
            {
                int i = PyFastSearch.IndexOf(text, sub);
                if (i < 0)
                    break;
                count++;
                text = text.Slice(i + sub.Length);
            }
            return count;
        }

        /// <summary>
        /// The first (or, backward, the last) index in [start, end) where sub's characters are, or
        /// -1; the window is already adjusted and sub is not empty.
        /// </summary>
        private static int Search(PyString text, PyString sub, int start, int end, bool backward)
        {
            if (end - start < sub.CodePointLength())
                return -1;
            int i;
            if (!text.IsUcs4)
            {
                if (sub.IsUcs4)
                    return -1;
                i = FindIn<char>(text._text.AsSpan(start, end - start), sub._text.AsSpan(), backward);
            }
            else
                i = FindIn<int>(text._ucs4.AsSpan(start, end - start), sub.ToCodePoints().AsSpan(), backward);
            return i < 0 ? -1 : start + i;
        }

        /// <summary>any_find_slice's forward search: the first index in [start, end) of sub, or -1.</summary>
        internal static int FindCodePoints(PyString text, PyString sub, int start, int end)
        {
            if (end - start < sub.CodePointLength())
                return -1;
            return sub.CodePointLength() == 0 ? start : Search(text, sub, start, end, false);
        }

        /// <summary>anylib_count over [start, end), at most maxcount.</summary>
        internal static long CountCodePoints(PyString text, PyString sub, int start, int end, long maxcount)
        {
            int n = sub.CodePointLength();
            if (end - start < n)
                return 0;
            if (n == 0)
                return Math.Min((long)(end - start) + 1, maxcount);
            if (!text.IsUcs4)
                return sub.IsUcs4 ? 0 : CountIn<char>(text._text.AsSpan(start, end - start), sub._text.AsSpan(), maxcount);
            return CountIn<int>(text._ucs4.AsSpan(start, end - start), sub.ToCodePoints().AsSpan(), maxcount);
        }

        /// <summary>
        /// tailmatch on an adjusted window: end -= len(sub); end &lt; start fails, an empty sub then
        /// matches, else sub's characters at start (or ending at the end) are compared.
        /// </summary>
        private static bool TailMatch(PyString text, PyString sub, int start, int end, bool fromEnd)
        {
            int n = sub.CodePointLength();
            end -= n;
            if (end < start)
                return false;
            if (n == 0)
                return true;
            int offset = fromEnd ? end : start;
            if (!text.IsUcs4)
                return !sub.IsUcs4 && text._text.AsSpan(offset, n).SequenceEqual(sub._text.AsSpan());
            return text._ucs4.AsSpan(offset, n).SequenceEqual(sub.ToCodePoints().AsSpan());
        }

        /// <summary>
        /// stringlib_parse_args_finds then ADJUST_INDICES (Objects/unicodeobject.c) on
        /// PyUnicode_GET_LENGTH, not a subclass's __len__: end is clamped to the length and to zero,
        /// a negative start is shifted, and start is deliberately not clamped down to end, which is
        /// why 'abc'.find('', 4) is -1 while 'abc'.find('', 3) is 3.
        /// </summary>
        internal void AdjustIndices(PyObject[] args, out int start, out int end)
        {
            int length = CodePointLength();
            ParseStrStartStop(args, length, out start, out end);
            if (end > length)
                end = length;
            else if (end < 0)
            {
                end += length;
                if (end < 0)
                    end = 0;
            }
            if (start < 0)
            {
                start += length;
                if (start < 0)
                    start = 0;
            }
        }

        // ensure_unicode: "must be str, not ...".
        private static PyString EnsureUnicode(PyObject value) =>
            value as PyString ?? throw PyTypeError.Create("must be str, not " + value.GetTypeName());

        /// <summary>
        /// unicode_find, unicode_index, unicode_rfind and unicode_rindex over any_find_slice: the
        /// window is read before the substring, which must then be a str.
        /// </summary>
        private static PyObject FindIndexOf(PyString str, PyObject subArg, PyObject[] args,
                                            bool backward, bool raiseWhenMissing)
        {
            str.AdjustIndices(args, out int start, out int end);
            var sub = EnsureUnicode(subArg);
            int found;
            if (end - start < sub.CodePointLength())
                found = -1;
            else if (sub.CodePointLength() == 0)
                found = backward ? end : start;
            else
                found = Search(str, sub, start, end, backward);
            if (found >= 0)
                return PyInt.From(found);
            if (raiseWhenMissing)
                throw PyValueError.Create("substring not found");
            return PyInt.From(-1);
        }

        /// <summary>unicode_count_impl: anylib_count over the adjusted window.</summary>
        private static PyObject CountOf(PyString str, PyObject subArg, PyObject[] args)
        {
            str.AdjustIndices(args, out int start, out int end);
            var sub = EnsureUnicode(subArg);
            return PyInt.From(CountCodePoints(str, sub, start, end, long.MaxValue));
        }

        /// <summary>
        /// unicode_startswith and unicode_endswith: a str or a tuple of str, each member checked as
        /// it is reached, so a match ends the walk before a later non-str is seen.
        /// </summary>
        private static PyObject StartsOrEndsWith(PyString str, PyObject subobj, PyObject[] args, string what, bool fromEnd)
        {
            str.AdjustIndices(args, out int start, out int end);
            if (subobj is PyTuple tuple)
            {
                foreach (var item in tuple.Items)
                {
                    if (item is not PyString part)
                        throw PyTypeError.Create($"tuple for {what} must only contain str, not {item.GetTypeName()}");
                    if (TailMatch(str, part, start, end, fromEnd))
                        return PyBool.True;
                }
                return PyBool.False;
            }
            if (subobj is not PyString sub)
                throw PyTypeError.Create($"{what} first arg must be str or a tuple of str, not {subobj.GetTypeName()}");
            return PyBool.FromBool(TailMatch(str, sub, start, end, fromEnd));
        }

        // ---- strip (do_strip, _PyUnicode_XStrip) ----

        internal const int LeftStrip = 0, RightStrip = 1, BothStrip = 2;

        /// <summary>
        /// do_strip and _PyUnicode_XStrip: Py_UNICODE_ISSPACE characters when chars is null, else
        /// those of chars; the result is PyUnicode_Substring, the text unchanged when nothing went.
        /// </summary>
        internal PyString StripText(int striptype, PyString chars)
        {
            HashSet<int> set = chars == null ? null : new HashSet<int>(chars.CodePoints());
            bool Stripped(int cp) => set == null ? IsWhiteSpaceCp(cp) : set.Contains(cp);
            int len = CodePointLength();
            int i = 0, j = len;
            if (striptype != RightStrip)
                while (i < j && Stripped(ReadChar(i)))
                    i++;
            if (striptype != LeftStrip)
                while (j > i && Stripped(ReadChar(j - 1)))
                    j--;
            return Substring(i, j);
        }

        // do_argstrip: None or nothing is whitespace, else the characters of a str.
        private static PyObject ArgStrip(PyString str, PyObject[] args, int striptype, string name)
        {
            if (args.Length == 0 || args[0] is PyNone)
                return str.StripText(striptype, null);
            if (args[0] is not PyString chars)
                throw PyTypeError.Create($"{name} arg must be None or str");
            return str.StripText(striptype, chars);
        }

        // ---- replace ----

        /// <summary>
        /// replace (Objects/unicodeobject.c): nothing to do is the text unchanged; otherwise the
        /// matches are counted, the size checked ("replace string is too long") and the result
        /// written once. An empty old goes before every character and after the last one.
        /// </summary>
        public PyString Replace(PyString str1, PyString str2, long maxcount = -1)
        {
            int slen = CodePointLength(), len1 = str1.CodePointLength(), len2 = str2.CodePointLength();
            if (slen < len1)
                return ResultUnchanged();
            if (maxcount < 0)
                maxcount = long.MaxValue;
            else if (maxcount == 0)
                return ResultUnchanged();
            if (ReferenceEquals(str1, str2))
                return ResultUnchanged();
            if (len1 == len2 && len1 == 0)
                return ResultUnchanged();
            long n = CountCodePoints(this, str1, 0, slen, maxcount);
            if (n == 0)
                return ResultUnchanged();
            long newSize = len2 > len1
                ? slen + PyAllocation.Product(len2 - len1, n, "replace string is too long")
                : slen + (long)(len2 - len1) * n;
            PyAllocation.StringLength(newSize);
            var writer = new PyUnicodeWriter();
            if (len1 == 0)
            {
                int i = 0;
                writer.WriteStr(str2);
                while (--n > 0)
                {
                    writer.WriteChar(ReadChar(i++));
                    writer.WriteStr(str2);
                }
                writer.WriteSubstring(this, i, slen);
                return writer.Finish(fresh: true);
            }
            int position = 0;
            while (n-- > 0)
            {
                int index = Search(this, str1, position, slen, false);
                writer.WriteSubstring(this, position, index);
                writer.WriteStr(str2);
                position = index + len1;
            }
            writer.WriteSubstring(this, position, slen);
            return writer.Finish(fresh: true);
        }

        // ---- split, rsplit, splitlines, partition (Objects/stringlib/split.h, partition.h) ----

        private static (PyString, long) SplitArguments(PyObject[] arguments)
        {
            long limit = -1;
            if (arguments.Length >= 2 && arguments[1] != null)
                limit = PyArgs.Ssize(arguments[1]);
            PyString separator = null;
            if (arguments.Length >= 1 && arguments[0] != null && arguments[0] is not PyNone)
                separator = arguments[0] as PyString
                    ?? throw PyTypeError.Create($"must be str or None, not {arguments[0].GetTypeName()}");
            return (separator, limit);
        }

        /// <summary>
        /// split and rsplit (Objects/unicodeobject.c over Objects/stringlib/split.h): a negative
        /// maxcount is PY_SSIZE_T_MAX; a separator wider or longer than the text is the list of the
        /// text itself; a piece is PyUnicode_Substring, so the whole text of an exact str is itself.
        /// </summary>
        public PyList Split(PyString sep, long maxcount, bool fromRight = false)
        {
            int len = CodePointLength();
            if (maxcount < 0)
                maxcount = long.MaxValue;
            var parts = new List<PyObject>();
            if (sep == null)
            {
                if (!fromRight)
                {
                    // split_whitespace
                    int i = 0, j;
                    while (maxcount-- > 0)
                    {
                        while (i < len && IsWhiteSpaceCp(ReadChar(i))) i++;
                        if (i == len) break;
                        j = i; i++;
                        while (i < len && !IsWhiteSpaceCp(ReadChar(i))) i++;
                        parts.Add(Substring(j, i));
                    }
                    if (i < len)
                    {
                        // only when maxcount was reached: the rest, without its leading whitespace
                        while (i < len && IsWhiteSpaceCp(ReadChar(i))) i++;
                        if (i != len) parts.Add(Substring(i, len));
                    }
                }
                else
                {
                    // rsplit_whitespace
                    int i = len - 1, j;
                    while (maxcount-- > 0)
                    {
                        while (i >= 0 && IsWhiteSpaceCp(ReadChar(i))) i--;
                        if (i < 0) break;
                        j = i; i--;
                        while (i >= 0 && !IsWhiteSpaceCp(ReadChar(i))) i--;
                        parts.Add(Substring(i + 1, j + 1));
                    }
                    if (i >= 0)
                    {
                        while (i >= 0 && IsWhiteSpaceCp(ReadChar(i))) i--;
                        if (i >= 0) parts.Add(Substring(0, i + 1));
                    }
                    parts.Reverse();
                }
                return new PyList(parts);
            }
            int seplen = sep.CodePointLength();
            if (Kind < sep.Kind || len < seplen)
            {
                parts.Add(this);
                return new PyList(parts);
            }
            if (seplen == 0)
                throw PyValueError.Create("empty separator");
            if (!fromRight)
            {
                int i = 0;
                while (maxcount-- > 0)
                {
                    int pos = Search(this, sep, i, len, false);
                    if (pos < 0) break;
                    parts.Add(Substring(i, pos));
                    i = pos + seplen;
                }
                parts.Add(Substring(i, len));
            }
            else
            {
                int j = len;
                while (maxcount-- > 0)
                {
                    int pos = Search(this, sep, 0, j, true);
                    if (pos < 0) break;
                    parts.Add(Substring(pos + seplen, j));
                    j = pos;
                }
                parts.Add(Substring(0, j));
                parts.Reverse();
            }
            return new PyList(parts);
        }

        // Py_UNICODE_ISLINEBREAK
        private static bool IsLineBreak(int c) =>
            c == '\n' || c == '\r' || c == '\v' || c == '\f' ||
            c == '\x1c' || c == '\x1d' || c == '\x1e' || c == '\x85' ||
            c == 0x2028 || c == 0x2029;

        /// <summary>stringlib splitlines: \r\n is one break; keepends keeps it on the line.</summary>
        internal static List<PyObject> SplitLinesCore(PyString s, bool keepends)
        {
            var items = new List<PyObject>();
            int len = s.CodePointLength();
            int i = 0;
            while (i < len)
            {
                int j = i;
                while (i < len && !IsLineBreak(s.ReadChar(i)))
                    i++;
                int eol = i;
                if (i < len)
                {
                    if (s.ReadChar(i) == '\r' && i + 1 < len && s.ReadChar(i + 1) == '\n')
                        i += 2;
                    else
                        i++;
                    if (keepends)
                        eol = i;
                }
                items.Add(s.Substring(j, eol));
            }
            return items;
        }

        /// <summary>
        /// PyUnicode_Partition and PyUnicode_RPartition over stringlib partition: a separator wider
        /// or longer than the text, or one that is not there, gives the text itself with two empty
        /// strs; an empty separator is refused.
        /// </summary>
        private static PyObject Partition(PyString str, PyObject sepArg, bool fromRight)
        {
            var sep = EnsureUnicode(sepArg);
            int len = str.CodePointLength(), seplen = sep.CodePointLength();
            PyObject NotThere() => fromRight
                ? PyTuple.From(new PyObject[] { Empty, Empty, str })
                : PyTuple.From(new PyObject[] { str, Empty, Empty });
            if (str.Kind < sep.Kind || len < seplen)
                return NotThere();
            if (seplen == 0)
                throw PyValueError.Create("empty separator");
            int pos = Search(str, sep, 0, len, fromRight);
            if (pos < 0)
                return NotThere();
            return PyTuple.From(new PyObject[] { str.Substring(0, pos), sep, str.Substring(pos + seplen, len) });
        }

        // ---- padding (pad, unicode_zfill_impl, convert_uc) ----

        /// <summary>convert_uc: the fill of ljust, rjust and center is one character.</summary>
        private static int FillCharacter(PyObject fillArg)
        {
            if (fillArg is not PyString fill)
                throw PyTypeError.Create(
                    $"The fill character must be a unicode character, not {fillArg.GetTypeName()}");
            if (fill.CodePointLength() != 1)
                throw PyTypeError.Create("The fill character must be exactly one character long");
            return fill.ReadChar(0);
        }

        /// <summary>pad: left and right fill characters around the text, a new str; none is the text unchanged.</summary>
        private PyString Pad(int left, int right, int fill)
        {
            if (left < 0) left = 0;
            if (right < 0) right = 0;
            if (left == 0 && right == 0)
                return ResultUnchanged();
            PyAllocation.StringLength((long)left + CodePointLength() + right);
            var writer = new PyUnicodeWriter();
            for (int i = 0; i < left; i++)
                writer.WriteChar(fill);
            writer.WriteStr(this);
            for (int i = 0; i < right; i++)
                writer.WriteChar(fill);
            return writer.Finish(fresh: true);
        }

        /// <summary>
        /// unicode_center_impl (stringlib/transmogrify.h): left = marg/2 + (marg &amp; width &amp; 1).
        /// </summary>
        private PyString Center(int width, int fill)
        {
            int len = CodePointLength();
            if (len >= width)
                return ResultUnchanged();
            int marg = width - len;
            int left = marg / 2 + (marg & width & 1);
            return Pad(left, marg - left, fill);
        }

        /// <summary>unicode_zfill_impl: zeros on the left, a leading sign moved in front of them.</summary>
        private PyString ZFill(int width)
        {
            int len = CodePointLength();
            if (len >= width)
                return ResultUnchanged();
            int fill = width - len;
            var writer = new PyUnicodeWriter();
            int first = len > 0 ? ReadChar(0) : 0;
            bool sign = first == '+' || first == '-';
            if (sign)
                writer.WriteChar(first);
            for (int i = 0; i < fill; i++)
                writer.WriteChar('0');
            writer.WriteSubstring(this, sign ? 1 : 0, len);
            return writer.Finish(fresh: true);
        }

        /// <summary>
        /// unicode_expandtabs_impl: columns count characters, a newline or carriage return starts
        /// a new one, the size is checked before anything is written ("new string is too long"),
        /// and no tab at all is the text unchanged.
        /// </summary>
        private PyString ExpandTabs(int tabsize)
        {
            int len = CodePointLength();
            long size = 0, column = 0;
            bool found = false;
            for (int i = 0; i < len; i++)
            {
                int c = ReadChar(i);
                if (c == '\t')
                {
                    found = true;
                    if (tabsize > 0)
                    {
                        long incr = tabsize - column % tabsize;
                        if (size > long.MaxValue - incr)
                            throw PyOverflowError.Create("new string is too long");
                        size += incr;
                        column += incr;
                    }
                }
                else
                {
                    size++;
                    column = c == '\n' || c == '\r' ? 0 : column + 1;
                }
            }
            if (!found)
                return ResultUnchanged();
            PyAllocation.StringLength(size);
            var writer = new PyUnicodeWriter();
            column = 0;
            for (int i = 0; i < len; i++)
            {
                int c = ReadChar(i);
                if (c == '\t')
                {
                    if (tabsize > 0)
                    {
                        long spaces = tabsize - column % tabsize;
                        for (long k = 0; k < spaces; k++)
                            writer.WriteChar(' ');
                        column += spaces;
                    }
                }
                else
                {
                    writer.WriteChar(c);
                    column = c == '\n' || c == '\r' ? 0 : column + 1;
                }
            }
            return writer.Finish(fresh: true);
        }

        // ---- maketrans, translate ----

        /// <summary>
        /// unicode_maketrans_impl: with y, the characters of x map to those of y and those of z to
        /// None; alone, x is an exact dict whose one-character str keys become their ordinals.
        /// </summary>
        private static PyDict MakeTrans(PyObject[] args)
        {
            var table = new PyDict();
            if (args.Length >= 2)
            {
                var y = args[1] as PyString ?? throw PyArgs.BadArgument("maketrans", "argument 2", "str", args[1]);
                PyString z = null;
                if (args.Length >= 3)
                    z = args[2] as PyString ?? throw PyArgs.BadArgument("maketrans", "argument 3", "str", args[2]);
                if (args[0] is not PyString x)
                    throw PyTypeError.Create("first maketrans argument must be a string if there is a second argument");
                if (x.CodePointLength() != y.CodePointLength())
                    throw PyValueError.Create("the first two maketrans arguments must have equal length");
                for (int i = 0; i < x.CodePointLength(); i++)
                    table.SetItem(PyInt.From(x.ReadChar(i)), PyInt.From(y.ReadChar(i)));
                if (z != null)
                    for (int i = 0; i < z.CodePointLength(); i++)
                        table.SetItem(PyInt.From(z.ReadChar(i)), PyNone.Instance);
                return table;
            }
            if (!args[0].IsExactly(PyType.DictType))
                throw PyTypeError.Create("if you give only one argument to maketrans it must be a dict");
            foreach (PyObject item in ((PyDict)args[0]).Items().Items)
            {
                var pair = (PyTuple)item;
                var key = pair.Items[0];
                var value = pair.Items[1];
                if (key is PyString keyStr)
                {
                    if (keyStr.CodePointLength() != 1)
                        throw PyValueError.Create("string keys in translate table must be of length 1");
                    table.SetItem(PyInt.From(keyStr.ReadChar(0)), value);
                }
                else if (PyInt.LongCheck(key))
                    table.SetItem(key, value);
                else
                    throw PyTypeError.Create("keys in translate table must be strings or integers");
            }
            return table;
        }

        /// <summary>
        /// _PyUnicode_TranslateCharmap: each character is looked up in the table
        /// (charmaptranslate_lookup, PyObject_GetItem); a LookupError keeps it, None drops it, an
        /// int is a character in range(0x110000) and a str is written as it is.
        /// </summary>
        private PyString Translate(PyObject table)
        {
            int length = CodePointLength();
            if (length == 0)
                return ResultUnchanged();
            var writer = new PyUnicodeWriter();
            for (int i = 0; i < length; i++)
            {
                int cp = ReadChar(i);
                PyObject value;
                try
                {
                    value = table.GetItem(PyInt.From(cp));
                }
                catch (PythonException error) when (error.PyException is PyLookupError)
                {
                    writer.WriteChar(cp);
                    continue;
                }
                if (value is PyNone)
                    continue;
                if (PyInt.LongCheck(value))
                {
                    // PyLong_AS_LONG: an int past a C long reads as -1, out of range too.
                    var big = PyObject.IndexOrThrow(value).Value;
                    if (big < 0 || big > 0x10FFFF)
                        throw PyValueError.Create("character mapping must be in range(0x110000)");
                    writer.WriteChar((int)big);
                }
                else if (value is PyString text)
                    writer.WriteStr(text);
                else
                    throw PyTypeError.Create("character mapping must return integer, None or str");
            }
            return writer.Finish();
        }

        // ---- ascii() ----

        /// <summary>
        /// PyObject_ASCII: the repr, given back as it is when it is ASCII, else encoded to ASCII
        /// with backslashreplace (\xhh, \uxxxx, \Uxxxxxxxx per character) and decoded.
        /// </summary>
        internal static PyString AsciiOf(PyObject value)
        {
            var repr = value.ToRepr();
            if (repr.MaxChar() < 128)
                return repr;
            var writer = new PyUnicodeWriter();
            int n = repr.CodePointLength();
            for (int i = 0; i < n; i++)
            {
                int c = repr.ReadChar(i);
                if (c < 128)
                    writer.WriteChar(c);
                else if (c < 0x100)
                    writer.WriteUnits($"\\x{c:x2}");
                else if (c < 0x10000)
                    writer.WriteUnits($"\\u{c:x4}");
                else
                    writer.WriteUnits($"\\U{c:x8}");
            }
            return writer.Finish();
        }

        // ---- removeprefix, removesuffix ----

        /// <summary>unicode_removeprefix_impl and unicode_removesuffix_impl over tailmatch.</summary>
        private PyString RemoveAffix(PyString affix, bool suffix)
        {
            int len = CodePointLength();
            if (!TailMatch(this, affix, 0, len, suffix))
                return ResultUnchanged();
            return suffix ? Substring(0, len - affix.CodePointLength()) : Substring(affix.CodePointLength(), len);
        }
    }
}
