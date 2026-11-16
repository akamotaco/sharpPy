namespace SharpPy;

// CPython Objects/stringlib/unicode_format.h: MarkupIterator_next, parse_field, get_field_object,
// render_field. The format string is read a character at a time and the result written by one
// _PyUnicodeWriter; field names, keys and specs are substrings of it. Numbering state is shared
// with recursively expanded specs.
internal static class PyStringFormatter
{
    internal static PyString Format(PyString format, PyObject[]? args, PyObject? mapping)
    {
        int numbering = 0, next = 0;
        return Expand(format, args, mapping, 2, ref numbering, ref next);
    }

    private static PyString Expand(PyString text, PyObject[]? args, PyObject? mapping,
        int depth, ref int numbering, ref int next)
    {
        if (depth <= 0) throw PyValueError.Create("Max string recursion exceeded");
        var output = new PyUnicodeWriter();
        int n = text.CodePointLength();
        for (int p = 0; p < n;)
        {
            int c = text.ReadChar(p++);
            if (c != '{' && c != '}') { output.WriteChar(c); continue; }
            if (p < n && text.ReadChar(p) == c) { output.WriteChar(c); p++; continue; }
            if (c == '}' || p == n)
                throw PyValueError.Create($"Single '{(char)c}' encountered in format string");
            int start = p;
            while (p < n)
            {
                c = text.ReadChar(p++);
                if (c == '{') throw PyValueError.Create("unexpected '{' in field name");
                if (c == '[')
                {
                    while (p < n && text.ReadChar(p) != ']') p++;
                    continue;
                }
                if (c == '}' || c == ':' || c == '!') break;
            }
            PyString field = text.Substring(start, p - 1);
            PyString spec = PyString.Empty;
            int conversion = 0;
            bool expand = false;
            if (c == ':' || c == '!')
            {
                if (c == '!')
                {
                    if (p == n)
                        throw PyValueError.Create("end of string while looking for conversion specifier");
                    conversion = text.ReadChar(p++);
                    if (p < n)
                    {
                        c = text.ReadChar(p++);
                        if (c != '}' && c != ':')
                            throw PyValueError.Create("expected ':' after conversion specifier");
                    }
                }
                if (c != '}')
                {
                    start = p;
                    int braces = 1;
                    while (p < n)
                    {
                        c = text.ReadChar(p++);
                        if (c == '{') { braces++; expand = true; }
                        else if (c == '}' && --braces == 0) break;
                    }
                    if (braces != 0) throw PyValueError.Create("unmatched '{' in format spec");
                    spec = text.Substring(start, p - 1);
                }
            }
            else if (c != '}') throw PyValueError.Create("expected '}' before end of string");

            PyObject value = Resolve(field, args, mapping, ref numbering, ref next);
            value = conversion switch
            {
                0 => value,
                's' => value.ToStr(),
                'r' => value.ToRepr(),
                'a' => PyString.AsciiOf(value),
                _ => throw PyValueError.Create("Unknown conversion specifier " +
                    (conversion <= 32 || conversion > 126 ? "\\x" + conversion.ToString("x") : ((char)conversion).ToString()))
            };
            if (expand) spec = Expand(spec, args, mapping, depth - 1, ref numbering, ref next);
            PyString rendered = value.Format(spec);
            // do_markup: the last piece is written without overallocation, so a field that is the
            // whole result is that str object itself.
            if (p == n && output.Length == 0 && rendered.CodePointLength() > 0)
                return rendered;
            output.WriteStr(rendered);
        }
        return output.Finish();
    }

    private static PyObject Resolve(PyString field, PyObject[]? args, PyObject? mapping,
        ref int numbering, ref int next)
    {
        int n = field.CodePointLength();
        int p = 0;
        while (p < n && field.ReadChar(p) != '.' && field.ReadChar(p) != '[') p++;
        PyString first = field.Substring(0, p);
        long index = Index(first);
        bool automatic = first.CodePointLength() == 0;
        PyObject value;
        if (automatic || index >= 0)
        {
            int mode = automatic ? 1 : 2;
            if (numbering != 0 && numbering != mode)
                throw PyValueError.Create(automatic
                    ? "cannot switch from manual field specification to automatic field numbering"
                    : "cannot switch from automatic field numbering to manual field specification");
            numbering = mode;
            if (automatic) index = next++;
            if (args == null) throw PyValueError.Create("Format string contains positional fields");
            if (index >= args.Length)
                throw PyIndexError.Create($"Replacement index {index} out of range for positional args tuple");
            value = args[(int)index];
        }
        else value = mapping != null ? mapping.GetItem(first) : throw PyKeyError.Create(first);
        while (p < n)
        {
            int kind = field.ReadChar(p++);
            int start = p;
            if (kind == '.')
            {
                while (p < n && field.ReadChar(p) != '.' && field.ReadChar(p) != '[') p++;
                if (p == start) throw PyValueError.Create("Empty attribute in format string");
                value = value.GetAttribute(field.Substring(start, p));
            }
            else if (kind == '[')
            {
                while (p < n && field.ReadChar(p) != ']') p++;
                if (p == n) throw PyValueError.Create("Missing ']' in format string");
                if (p == start) throw PyValueError.Create("Empty attribute in format string");
                PyString key = field.Substring(start, p++);
                index = Index(key);
                value = value.GetItem(index < 0 ? key : PyInt.From(index));
            }
            else throw PyValueError.Create("Only '.' or '[' may follow ']' in format field specifier");
        }
        return value;
    }

    // get_integer: Py_UNICODE_TODECIMAL of each character, -1 when one is not a digit.
    private static long Index(PyString text)
    {
        int n = text.CodePointLength();
        if (n == 0) return -1;
        long result = 0;
        for (int p = 0; p < n; p++)
        {
            int digit = UnicodeType.ToDecimalDigit(text.ReadChar(p));
            if (digit < 0) return -1;
            if (result > (long.MaxValue - digit) / 10)
                throw PyValueError.Create("Too many decimal digits in format string");
            result = result * 10 + digit;
        }
        return result;
    }
}
