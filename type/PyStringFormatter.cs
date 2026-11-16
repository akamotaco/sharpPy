using System.Collections.Generic;

namespace SharpPy;

/// <summary>
/// Objects/stringlib/unicode_format.h of CPython v3.12.15: MarkupIterator, parse_field,
/// field_name_split, FieldNameIterator, get_field_object and do_markup over a str's characters.
/// str.format and format_map (build_string) and _string.formatter_parser and
/// formatter_field_name_split all read a format string through these. A field name, key or spec
/// is a substring of the format string; the result is written by one PyUnicodeWriter.
/// </summary>
internal static class PyStringFormatter
{
    /// <summary>SubString: [Start, End) of Str; Str null is "not present".</summary>
    internal readonly struct SubString
    {
        internal readonly PyString Str;
        internal readonly int Start, End;

        internal SubString(PyString str, int start, int end)
        {
            Str = str;
            Start = start;
            End = end;
        }

        internal bool IsEmpty => Start >= End;

        // SubString_new_object: None when not present, else the substring.
        internal PyObject NewObject() => Str == null ? PyNone.Instance : Str.Substring(Start, End);

        // SubString_new_object_or_empty
        internal PyString NewObjectOrEmpty() => Str == null ? PyString.Empty : Str.Substring(Start, End);
    }

    /// <summary>MarkupIterator: literal text, then at most one replacement field, per step.</summary>
    internal struct MarkupIterator
    {
        private readonly PyString _str;
        private int _start;
        private readonly int _end;

        internal MarkupIterator(PyString str, int start, int end)
        {
            _str = str;
            _start = start;
            _end = end;
        }

        internal bool AtEnd => _start >= _end;

        /// <summary>MarkupIterator_next: false at the end of the input; errors are raised.</summary>
        internal bool Next(out SubString literal, out bool fieldPresent, out SubString fieldName,
            out SubString formatSpec, out int conversion, out bool formatSpecNeedsExpanding)
        {
            literal = default;
            fieldName = default;
            formatSpec = default;
            conversion = 0;
            formatSpecNeedsExpanding = false;
            fieldPresent = false;
            if (_start >= _end)
                return false;
            int start = _start;
            int c = 0;
            bool markupFollows = false;
            while (_start < _end)
            {
                c = _str.ReadChar(_start++);
                if (c == '{' || c == '}')
                {
                    markupFollows = true;
                    break;
                }
            }
            bool atEnd = _start >= _end;
            int len = _start - start;
            if (c == '}' && (atEnd || c != _str.ReadChar(_start)))
                throw PyValueError.Create("Single '}' encountered in format string");
            if (atEnd && c == '{')
                throw PyValueError.Create("Single '{' encountered in format string");
            if (!atEnd)
            {
                if (c == _str.ReadChar(_start))
                {
                    // an escaped { or }: no markup follows, just this literal text
                    _start++;
                    markupFollows = false;
                }
                else
                    len--;
            }
            literal = new SubString(_str, start, start + len);
            if (!markupFollows)
                return true;
            fieldPresent = true;
            ParseField(out fieldName, out formatSpec, out formatSpecNeedsExpanding, out conversion);
            return true;
        }

        // parse_field
        private void ParseField(out SubString fieldName, out SubString formatSpec,
            out bool formatSpecNeedsExpanding, out int conversion)
        {
            conversion = 0;
            formatSpec = default;
            formatSpecNeedsExpanding = false;
            int c = 0;
            int nameStart = _start;
            while (_start < _end)
            {
                c = _str.ReadChar(_start++);
                if (c == '{')
                    throw PyValueError.Create("unexpected '{' in field name");
                if (c == '[')
                {
                    for (; _start < _end; _start++)
                        if (_str.ReadChar(_start) == ']')
                            break;
                    continue;
                }
                if (c == '}' || c == ':' || c == '!')
                    break;
            }
            fieldName = new SubString(_str, nameStart, _start - 1);
            if (c == '!' || c == ':')
            {
                if (c == '!')
                {
                    if (_start >= _end)
                        throw PyValueError.Create("end of string while looking for conversion specifier");
                    conversion = _str.ReadChar(_start++);
                    if (_start < _end)
                    {
                        c = _str.ReadChar(_start++);
                        if (c == '}')
                            return;
                        if (c != ':')
                            throw PyValueError.Create("expected ':' after conversion specifier");
                    }
                }
                int specStart = _start;
                int count = 1;
                while (_start < _end)
                {
                    c = _str.ReadChar(_start++);
                    if (c == '{')
                    {
                        formatSpecNeedsExpanding = true;
                        count++;
                    }
                    else if (c == '}' && --count == 0)
                    {
                        formatSpec = new SubString(_str, specStart, _start - 1);
                        return;
                    }
                }
                throw PyValueError.Create("unmatched '{' in format spec");
            }
            if (c != '}')
                throw PyValueError.Create("expected '}' before end of string");
        }
    }

    /// <summary>FieldNameIterator: the ".name" and "[key]" parts after the first part.</summary>
    internal struct FieldNameIterator
    {
        private readonly PyString _str;
        private int _start;
        private readonly int _end;

        internal FieldNameIterator(PyString str, int start, int end)
        {
            _str = str;
            _start = start;
            _end = end;
        }

        /// <summary>FieldNameIterator_next: false when there is nothing more.</summary>
        internal bool Next(out bool isAttribute, out long nameIndex, out SubString name)
        {
            isAttribute = false;
            nameIndex = -1;
            name = default;
            if (_start >= _end)
                return false;
            int c = _str.ReadChar(_start++);
            if (c == '.')
            {
                isAttribute = true;
                // _FieldNameIterator_attr: everything until '.' or '['
                int nameStart = _start;
                while (_start < _end)
                {
                    int d = _str.ReadChar(_start++);
                    if (d == '[' || d == '.')
                    {
                        _start--;
                        break;
                    }
                }
                name = new SubString(_str, nameStart, _start);
            }
            else if (c == '[')
            {
                // _FieldNameIterator_item: everything until ']', which must be there
                int nameStart = _start;
                bool bracketSeen = false;
                while (_start < _end)
                {
                    if (_str.ReadChar(_start++) == ']')
                    {
                        bracketSeen = true;
                        break;
                    }
                }
                if (!bracketSeen)
                    throw PyValueError.Create("Missing ']' in format string");
                name = new SubString(_str, nameStart, _start - 1);
                nameIndex = GetInteger(name);
            }
            else
                throw PyValueError.Create("Only '.' or '[' may follow ']' in format field specifier");
            if (name.Start == name.End)
                throw PyValueError.Create("Empty attribute in format string");
            return true;
        }
    }

    /// <summary>get_integer: Py_UNICODE_TODECIMAL of each character; -1 when one is not a digit.</summary>
    internal static long GetInteger(SubString str)
    {
        if (str.Start >= str.End)
            return -1;
        long accumulator = 0;
        for (int i = str.Start; i < str.End; i++)
        {
            int digit = UnicodeType.ToDecimalDigit(str.Str.ReadChar(i));
            if (digit < 0)
                return -1;
            if (accumulator > (long.MaxValue - digit) / 10)
                throw PyValueError.Create("Too many decimal digits in format string");
            accumulator = accumulator * 10 + digit;
        }
        return accumulator;
    }

    /// <summary>field_name_split: the part up to the first '.' or '[', its index, and the rest.</summary>
    internal static SubString FieldNameSplit(PyString str, int start, int end, out long firstIndex, out FieldNameIterator rest)
    {
        int i = start;
        while (i < end)
        {
            int c = str.ReadChar(i++);
            if (c == '[' || c == '.')
            {
                i--;
                break;
            }
        }
        var first = new SubString(str, start, i);
        rest = new FieldNameIterator(str, i, end);
        firstIndex = GetInteger(first);
        return first;
    }

    // AutoNumber: 0 unknown, 1 automatic, 2 manual.
    private sealed class AutoNumber
    {
        internal int State;
        internal long Number;
    }

    internal static PyString Format(PyString format, PyObject[]? args, PyObject? mapping) =>
        BuildString(format, 0, format.CodePointLength(), args, mapping, 2, new AutoNumber());

    // build_string: recursion_depth runs out at 0.
    private static PyString BuildString(PyString str, int start, int end, PyObject[]? args, PyObject? mapping,
        int depth, AutoNumber autoNumber)
    {
        if (depth <= 0) throw PyValueError.Create("Max string recursion exceeded");
        var writer = new PyUnicodeWriter();
        var iter = new MarkupIterator(str, start, end);
        while (iter.Next(out var literal, out bool fieldPresent, out var fieldName, out var formatSpec,
                         out int conversion, out bool needsExpanding))
        {
            // do_markup: the last piece is written without overallocation, so a field (or literal)
            // that is the whole result is that str object itself.
            bool last = iter.AtEnd;
            if (!literal.IsEmpty)
                writer.WriteSubstring(literal.Str, literal.Start, literal.End);
            if (!fieldPresent)
                continue;
            PyString rendered = RenderField(fieldName, formatSpec, needsExpanding, conversion, args, mapping, depth, autoNumber);
            if (last && writer.Length == 0 && rendered.CodePointLength() > 0)
                return rendered;
            writer.WriteStr(rendered);
        }
        return writer.Finish();
    }

    // output_markup and render_field
    private static PyString RenderField(SubString fieldName, SubString formatSpec, bool needsExpanding, int conversion,
        PyObject[]? args, PyObject? mapping, int depth, AutoNumber autoNumber)
    {
        PyObject value = GetFieldObject(fieldName, args, mapping, autoNumber);
        if (conversion != 0)
            value = conversion switch
            {
                'r' => value.ToRepr(),
                's' => value.ToStr(),
                'a' => PyString.AsciiOf(value),
                _ => throw PyValueError.Create("Unknown conversion specifier " +
                    (conversion > 32 && conversion < 127 ? ((char)conversion).ToString() : "\\x" + conversion.ToString("x")))
            };
        PyString spec = needsExpanding
            ? BuildString(formatSpec.Str, formatSpec.Start, formatSpec.End, args, mapping, depth - 1, autoNumber)
            : formatSpec.NewObjectOrEmpty();
        return value.Format(spec);
    }

    // get_field_object with field_name_split's auto numbering
    private static PyObject GetFieldObject(SubString input, PyObject[]? args, PyObject? mapping, AutoNumber autoNumber)
    {
        var first = FieldNameSplit(input.Str, input.Start, input.End, out long index, out var rest);
        bool empty = first.IsEmpty;
        if (empty || index != -1)
        {
            int mode = empty ? 1 : 2;
            if (autoNumber.State == 0)
                autoNumber.State = mode;
            else if (autoNumber.State != mode)
                throw PyValueError.Create(empty
                    ? "cannot switch from manual field specification to automatic field numbering"
                    : "cannot switch from automatic field numbering to manual field specification");
            if (empty)
                index = autoNumber.Number++;
        }
        PyObject obj;
        if (index == -1)
        {
            var key = first.NewObject();
            obj = mapping != null ? mapping.GetItem(key) : throw PyKeyError.Create(key);
        }
        else
        {
            if (args == null) throw PyValueError.Create("Format string contains positional fields");
            if (index >= args.Length)
                throw PyIndexError.Create($"Replacement index {index} out of range for positional args tuple");
            obj = args[(int)index];
        }
        while (rest.Next(out bool isAttribute, out long nameIndex, out var name))
        {
            if (isAttribute)
                obj = obj.GetAttribute((PyString)name.NewObject());
            else
                obj = obj.GetItem(nameIndex == -1 ? name.NewObject() : PyInt.From(nameIndex));
        }
        return obj;
    }

    // ---- _string ----

    /// <summary>formatteriter_next: (literal, field_name, format_spec, conversion) per step.</summary>
    internal sealed class FormatterIterator : PyIterator
    {
        private MarkupIterator _markup;

        internal FormatterIterator(PyString str) => _markup = new MarkupIterator(str, 0, str.CodePointLength());

        protected internal override PyObject IterNext()
        {
            if (!_markup.Next(out var literal, out bool fieldPresent, out var fieldName, out var formatSpec,
                              out int conversion, out _))
                throw PyStopIteration.Create();
            PyObject spec = fieldPresent ? formatSpec.NewObjectOrEmpty() : formatSpec.NewObject();
            PyObject conv = conversion == 0 ? PyNone.Instance : PyString.FromCodePoint(conversion);
            return PyTuple.From(new[] { literal.NewObject(), fieldName.NewObject(), spec, conv });
        }
    }

    /// <summary>fieldnameiter_next: (is_attr, int or str) per step.</summary>
    internal sealed class FieldNameIter : PyIterator
    {
        private FieldNameIterator _field;

        internal FieldNameIter(FieldNameIterator field) => _field = field;

        protected internal override PyObject IterNext()
        {
            if (!_field.Next(out bool isAttribute, out long index, out var name))
                throw PyStopIteration.Create();
            PyObject obj = index != -1 ? PyInt.From(index) : name.NewObject();
            return PyTuple.From(new[] { PyBool.FromBool(isAttribute), obj });
        }
    }

    /// <summary>formatter_field_name_split: (first, iterator of the rest).</summary>
    internal static PyObject FieldNameSplitObject(PyString str)
    {
        var first = FieldNameSplit(str, 0, str.CodePointLength(), out long index, out var rest);
        PyObject firstObj = index != -1 ? PyInt.From(index) : first.NewObject();
        return PyTuple.From(new[] { firstObj, new FieldNameIter(rest) });
    }
}
