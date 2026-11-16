using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace SharpPy
{
    /// <summary>
    /// The storage of a str (PEP 393, Include/cpython/unicodeobject.h). CPython keeps a string in
    /// the width of its widest character: kind 1 and 2 are one byte or one 16-bit unit per code
    /// point, kind 4 a 32-bit unit per code point. A code point is never more than one unit, so
    /// U+D800 followed by U+DC00 is two characters, unlike U+10000.
    ///
    /// SharpPy keeps kinds 1 and 2 as a .NET string with one unit per code point (lone surrogates
    /// included) and kind 4 as an int[]. <see cref="Value"/> is PyUnicode_AsWideChar: the wchar_t
    /// (UTF-16) text a .NET API or the host reads; for kind 4 it is made once and kept. A .NET
    /// string becomes a str through PyUnicode_FromWideChar (<see cref="From(string)"/>), which reads
    /// a surrogate pair as one character, as CPython does where wchar_t is 16 bits wide.
    /// What Python sees - lengths, indices, characters, order, equality - is the code points.
    /// </summary>
    public partial class PyString
    {
        // kind 0 and 2: the units. kind 4: the wchar_t text, null until asked for.
        private string _text;
        // kind 4: the code points.
        private int[] _ucs4;
        // 0: wchar_t text not read yet (PyUnicode_FromWideChar decides on first use),
        // 2: one unit per code point (kinds 1 and 2), 4: UCS-4.
        private byte _kind;

        /// <summary>PyUnicode_AsWideChar: the text as UTF-16, an astral character as a surrogate pair.</summary>
        public string Value => _kind == 4 ? (_text ??= WideCharOf(_ucs4)) : _text;

        /// <summary>PyUnicode_FromWideChar: a surrogate pair of the text is one character.</summary>
        public PyString(string value) => _text = value ?? "";

        // _PyUnicode_FromUCS2: each unit is a code point.
        private PyString(string units, bool unitsAreCodePoints)
        {
            _text = units;
            _kind = 2;
        }

        // _PyUnicode_FromUCS4 of text with a character above U+FFFF.
        private PyString(int[] ucs4)
        {
            _ucs4 = ucs4;
            _kind = 4;
        }

        /// <summary>unicode_subtype_new: the data of the exact str, copied.</summary>
        protected PyString(PyString source)
        {
            source.Resolve();
            _text = source._text;
            _ucs4 = source._ucs4;
            _kind = source._kind;
        }

        // PyUnicode_FromWideChar's work, done the first time the characters are needed.
        private void Resolve()
        {
            if (_kind != 0)
                return;
            if (HasSurrogatePairs(_text))
            {
                _ucs4 = CombineSurrogates(_text);
                Volatile.Write(ref _kind, 4);
            }
            else
                Volatile.Write(ref _kind, 2);
        }

        private static int[] CombineSurrogates(string wide)
        {
            var points = new int[CodePointCount(wide)];
            int n = 0;
            for (int i = 0; i < wide.Length; i += PairWidth(wide, i))
                points[n++] = CodePointAtUnit(wide, i);
            return points;
        }

        private static string WideCharOf(int[] points)
        {
            var sb = new StringBuilder(points.Length + 8);
            foreach (int cp in points)
            {
                if (cp >= 0x10000)
                    sb.Append(char.ConvertFromUtf32(cp));
                else
                    sb.Append((char)cp);
            }
            return sb.ToString();
        }

        /// <summary>PyUnicode_KIND above 2: the characters are <see cref="Ucs4"/>, else <see cref="Ucs2"/>.</summary>
        internal bool IsUcs4
        {
            get
            {
                Resolve();
                return _kind == 4;
            }
        }

        /// <summary>The characters of a kind 1 or 2 str, one unit each; null for kind 4.</summary>
        internal string Ucs2
        {
            get
            {
                Resolve();
                return _kind == 2 ? _text : null;
            }
        }

        /// <summary>The characters of a kind 4 str; null for kinds 1 and 2.</summary>
        internal int[] Ucs4
        {
            get
            {
                Resolve();
                return _ucs4;
            }
        }

        /// <summary>
        /// PyUnicode_GET_LENGTH: the text's length in code points, which a subclass's __len__ does
        /// not change (that is PyObject_Size, Length()).
        /// </summary>
        internal int CodePointLength()
        {
            Resolve();
            return _kind == 4 ? _ucs4.Length : _text.Length;
        }

        /// <summary>PyUnicode_READ_CHAR: the code point at a Python index.</summary>
        internal int ReadChar(int index)
        {
            Resolve();
            return _kind == 4 ? _ucs4[index] : _text[index];
        }

        /// <summary>The value of the character at a Python index (ord() and the like).</summary>
        internal int CodePointAt(int index) => ReadChar(index);

        /// <summary>The characters, one per Python character.</summary>
        internal IEnumerable<int> CodePoints()
        {
            Resolve();
            if (_kind == 4)
            {
                foreach (int cp in _ucs4)
                    yield return cp;
            }
            else
            {
                foreach (char c in _text)
                    yield return c;
            }
        }

        /// <summary>The characters as an array (PyUnicode_AsUCS4Copy without the terminator).</summary>
        internal int[] ToCodePoints()
        {
            Resolve();
            if (_kind == 4)
                return _ucs4;
            var points = new int[_text.Length];
            for (int i = 0; i < points.Length; i++)
                points[i] = _text[i];
            return points;
        }

        // PyUnicode_KIND, worked out once: 1, 2 or 4 (0 until asked for).
        private byte _pepKind;

        /// <summary>
        /// PyUnicode_KIND: the width CPython would store the text in, 1 up to U+00FF, 2 up to U+FFFF,
        /// else 4. SharpPy keeps kinds 1 and 2 in one storage; where CPython compares kinds, this is
        /// the value it compares.
        /// </summary>
        internal int Kind
        {
            get
            {
                if (_pepKind == 0)
                {
                    int max = MaxChar();
                    _pepKind = (byte)(max < 0x100 ? 1 : max < 0x10000 ? 2 : 4);
                }
                return _pepKind;
            }
        }

        /// <summary>PyUnicode_MAX_CHAR_VALUE in the sense of the widest character present.</summary>
        internal int MaxChar()
        {
            Resolve();
            int max = 0;
            if (_kind == 4)
            {
                foreach (int cp in _ucs4)
                    if (cp > max) max = cp;
            }
            else
            {
                foreach (char c in _text)
                    if (c > max) max = c;
            }
            return max;
        }

        // 0 not worked out, 1 yes, 2 no.
        private byte _wideRoundTrip;

        /// <summary>
        /// Whether PyUnicode_FromWideChar(PyUnicode_AsWideChar(s)) gives s back: false when a high
        /// surrogate character is followed by a low one, which the wchar_t text (<see cref="Value"/>)
        /// cannot tell from the one character the pair would be. A table keyed by .NET strings holds
        /// only names for which this is true.
        /// </summary>
        internal bool WideCharRoundTrips
        {
            get
            {
                if (_wideRoundTrip == 0)
                {
                    Resolve();
                    bool kept = true;
                    if (_kind == 4)
                    {
                        for (int i = 0; i + 1 < _ucs4.Length; i++)
                            if (_ucs4[i] is >= 0xD800 and <= 0xDBFF && _ucs4[i + 1] is >= 0xDC00 and <= 0xDFFF)
                            {
                                kept = false;
                                break;
                            }
                    }
                    else
                        kept = !HasSurrogatePairs(_text);
                    _wideRoundTrip = kept ? (byte)1 : (byte)2;
                }
                return _wideRoundTrip == 1;
            }
        }

        // ---- constructors that keep characters apart ----

        /// <summary>
        /// A str of units that are code points (_PyUnicode_FromUCS1/2 then unicode_result): a lone
        /// surrogate stays one character whatever follows it. The empty str and the latin-1
        /// characters are the shared objects.
        /// </summary>
        internal static PyString FromUnits(string units)
        {
            if (string.IsNullOrEmpty(units)) return Empty;
            if (units.Length == 1 && units[0] < 256) return Latin1[units[0]];
            return new PyString(units, true);
        }

        /// <summary>A fresh str of units that are code points (PyUnicode_New and filled): only the empty str is shared.</summary>
        internal static PyString FreshUnits(string units) =>
            string.IsNullOrEmpty(units) ? Empty : new PyString(units, true);

        /// <summary>
        /// _PyUnicode_FromUCS4: the kind of the widest character. The array is kept when it is
        /// kind 4, so the caller hands it over.
        /// </summary>
        internal static PyString FromCodePoints(int[] points, bool fresh = false)
        {
            int max = 0;
            foreach (int cp in points)
                if (cp > max) max = cp;
            if (max >= 0x10000)
                return new PyString(points);
            var units = new char[points.Length];
            for (int i = 0; i < units.Length; i++)
                units[i] = (char)points[i];
            return fresh ? FreshUnits(new string(units)) : FromUnits(new string(units));
        }

        /// <summary>_PyUnicode_Copy: a new exact str of the same characters and kind.</summary>
        internal PyString Copy() => new PyString(this);

        /// <summary>unicode_char: the str of one code point (chr(), str[i]).</summary>
        internal static PyString FromCodePoint(int cp)
        {
            if (cp < 256) return Latin1[cp];
            if (cp < 0x10000) return new PyString(((char)cp).ToString(), true);
            return new PyString(new[] { cp });
        }

        /// <summary>
        /// PyUnicode_Substring: the characters [start, end), clamped; the whole text is the object
        /// itself when it is an exact str (unicode_result_unchanged).
        /// </summary>
        internal PyString Substring(int start, int end)
        {
            int length = CodePointLength();
            if (start < 0) start = 0;
            if (end > length) end = length;
            if (start == 0 && end == length)
                return ResultUnchanged();
            if (start >= end)
                return Empty;
            if (_kind == 4)
            {
                var part = new int[end - start];
                Array.Copy(_ucs4, start, part, 0, part.Length);
                return FromCodePoints(part);
            }
            return FromUnits(_text.Substring(start, end - start));
        }

        /// <summary>PyUnicode_Concat of two non-empty texts: the wider kind of the two.</summary>
        internal static PyString Concat(PyString left, PyString right)
        {
            if (!left.IsUcs4 && !right.IsUcs4)
                return FreshUnits(left._text + right._text);
            var points = new int[left.CodePointLength() + right.CodePointLength()];
            left.CopyCodePoints(points, 0);
            right.CopyCodePoints(points, left.CodePointLength());
            return new PyString(points);
        }

        /// <summary>The characters written into a code point array at an offset.</summary>
        internal void CopyCodePoints(int[] target, int offset)
        {
            Resolve();
            if (_kind == 4)
                Array.Copy(_ucs4, 0, target, offset, _ucs4.Length);
            else
                for (int i = 0; i < _text.Length; i++)
                    target[offset + i] = _text[i];
        }

        // ---- comparison on characters ----

        /// <summary>Equal characters (unicode_eq): different kinds are different texts.</summary>
        internal bool ContentEquals(PyString other)
        {
            if (ReferenceEquals(this, other))
                return true;
            Resolve();
            other.Resolve();
            if (_kind != other._kind)
                return false;
            if (_kind == 4)
                return _ucs4.AsSpan().SequenceEqual(other._ucs4);
            return string.Equals(_text, other._text, StringComparison.Ordinal);
        }

        /// <summary>unicode_compare: the characters in order, a shorter prefix first.</summary>
        internal int CompareCodePoints(PyString other)
        {
            Resolve();
            other.Resolve();
            if (_kind == 2 && other._kind == 2)
            {
                int c = string.CompareOrdinal(_text, other._text);
                return c < 0 ? -1 : c > 0 ? 1 : 0;
            }
            int n = Math.Min(CodePointLength(), other.CodePointLength());
            for (int i = 0; i < n; i++)
            {
                int a = ReadChar(i), b = other.ReadChar(i);
                if (a != b)
                    return a < b ? -1 : 1;
            }
            int la = CodePointLength(), lb = other.CodePointLength();
            return la == lb ? 0 : la < lb ? -1 : 1;
        }

        /// <summary>
        /// _PyUnicode_JoinArray: the items, which must be str, with the separator between them;
        /// nothing is the empty str and one exact str is itself.
        /// </summary>
        internal static PyString JoinArray(PyString separator, PyObject[] items)
        {
            if (items.Length == 0)
                return Empty;
            if (items.Length == 1 && items[0].IsExactly(PyType.StrType))
                return (PyString)items[0];
            int seplen = items.Length > 1 ? separator.CodePointLength() : 0;
            bool wide = items.Length > 1 && separator.IsUcs4;
            long size = 0;
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] is not PyString item)
                    throw PyTypeError.Create($"sequence item {i}: expected str instance, {items[i].GetTypeName()} found");
                size += item.CodePointLength();
                wide |= item.IsUcs4;
                if (i > 0)
                    size += seplen;
            }
            PyAllocation.StringLength(size);
            if (!wide)
            {
                var sb = new StringBuilder((int)size);
                for (int i = 0; i < items.Length; i++)
                {
                    if (i > 0 && seplen > 0)
                        sb.Append(separator._text);
                    sb.Append(((PyString)items[i])._text);
                }
                return FreshUnits(sb.ToString());
            }
            var points = new int[size];
            int at = 0;
            for (int i = 0; i < items.Length; i++)
            {
                if (i > 0 && seplen > 0)
                {
                    separator.CopyCodePoints(points, at);
                    at += seplen;
                }
                var item = (PyString)items[i];
                item.CopyCodePoints(points, at);
                at += item.CodePointLength();
            }
            return FromCodePoints(points, fresh: true);
        }

        /// <summary>Equality of str contents for .NET dictionaries (the interned dict compares str by value).</summary>
        internal static readonly IEqualityComparer<PyString> ContentComparer = new ContentEquality();

        private sealed class ContentEquality : IEqualityComparer<PyString>
        {
            public bool Equals(PyString x, PyString y) => x.ContentEquals(y);
            public int GetHashCode(PyString obj) => StringComparer.Ordinal.GetHashCode(obj.Value);
        }

        /// <summary>unicode_hash: the bytes of the kind's data.</summary>
        private long HashCodePoints()
        {
            Resolve();
            return _kind == 4 ? PyHash.Ucs4(_ucs4) : PyHash.Ucs2(_text);
        }
    }

    /// <summary>
    /// _PyUnicodeWriter: characters appended one text or one code point at a time, kept in the
    /// width of the widest so far, and made into a str of that kind at the end.
    /// </summary>
    internal sealed class PyUnicodeWriter
    {
        private StringBuilder _units = new();
        private List<int> _wide;

        internal int Length => _wide?.Count ?? _units.Length;

        private void Widen()
        {
            if (_wide != null)
                return;
            _wide = new List<int>(_units.Length + 16);
            for (int i = 0; i < _units.Length; i++)
                _wide.Add(_units[i]);
            _units = null;
        }

        /// <summary>writer.pos moved back: the characters from length on are dropped.</summary>
        internal void Truncate(int length)
        {
            if (_wide != null)
                _wide.RemoveRange(length, _wide.Count - length);
            else
                _units.Length = length;
        }

        /// <summary>_PyUnicodeWriter_WriteChar.</summary>
        internal void WriteChar(int cp)
        {
            if (_wide == null && cp < 0x10000)
                _units.Append((char)cp);
            else
            {
                Widen();
                _wide.Add(cp);
            }
        }

        /// <summary>_PyUnicodeWriter_WriteStr.</summary>
        internal void WriteStr(PyString text) => WriteSubstring(text, 0, text.CodePointLength());

        /// <summary>_PyUnicodeWriter_WriteSubstring: characters [start, end) of text.</summary>
        internal void WriteSubstring(PyString text, int start, int end)
        {
            if (end <= start)
                return;
            if (text.IsUcs4)
            {
                Widen();
                var points = text.Ucs4;
                for (int i = start; i < end; i++)
                    _wide.Add(points[i]);
            }
            else if (_wide != null)
            {
                string units = text.Ucs2;
                for (int i = start; i < end; i++)
                    _wide.Add(units[i]);
            }
            else
                _units.Append(text.Ucs2, start, end - start);
        }

        /// <summary>
        /// _PyUnicodeWriter_WriteASCIIString and _PyUnicodeWriter_WriteLatin1String: text whose
        /// units are its characters, as an escape or a number is.
        /// </summary>
        internal void WriteUnits(string units)
        {
            if (_wide == null)
                _units.Append(units);
            else
                foreach (char c in units)
                    _wide.Add(c);
        }

        /// <summary>wchar_t text (PyUnicode_FromWideChar): a surrogate pair is one character.</summary>
        internal void WriteWideChar(string wide)
        {
            for (int i = 0; i < wide.Length; i += PyString.PairWidth(wide, i))
                WriteChar(PyString.CodePointAtUnit(wide, i));
        }

        /// <summary>_PyUnicodeWriter_Finish.</summary>
        internal PyString Finish(bool fresh = false)
        {
            if (_wide != null)
                return PyString.FromCodePoints(_wide.ToArray(), fresh);
            string units = _units.ToString();
            return fresh ? PyString.FreshUnits(units) : PyString.FromUnits(units);
        }
    }
}
