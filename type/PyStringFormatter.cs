using System.Globalization;
using System.Text;

namespace SharpPy;

// CPython Objects/stringlib/unicode_format.h: parse_field, get_field_object,
// output_markup. Numbering state is shared with recursively expanded specs.
internal static class PyStringFormatter
{
    internal static PyString Format(string format, PyObject[]? args, PyObject? mapping)
    {
        int numbering = 0, next = 0;
        return new PyString(Expand(format, args, mapping, 2, ref numbering, ref next));
    }

    private static string Expand(string text, PyObject[]? args, PyObject? mapping,
        int depth, ref int numbering, ref int next)
    {
        if (depth <= 0) throw PyValueError.Create("Max string recursion exceeded");
        var output = new StringBuilder(text.Length);
        for (int p = 0; p < text.Length;)
        {
            char c = text[p++];
            if (c != '{' && c != '}') { output.Append(c); continue; }
            if (p < text.Length && text[p] == c) { output.Append(c); p++; continue; }
            if (c == '}' || p == text.Length)
                throw PyValueError.Create($"Single '{c}' encountered in format string");
            int start = p;
            while (p < text.Length)
            {
                c = text[p++];
                if (c == '{') throw PyValueError.Create("unexpected '{' in field name");
                if (c == '[')
                {
                    while (p < text.Length && text[p] != ']') p++;
                    continue;
                }
                if (c == '}' || c == ':' || c == '!') break;
            }
            string field = text.Substring(start, p - start - 1);
            string spec = "";
            int conversion = 0;
            bool expand = false;
            if (c == ':' || c == '!')
            {
                if (c == '!')
                {
                    if (p == text.Length)
                        throw PyValueError.Create("end of string while looking for conversion specifier");
                    conversion = char.IsSurrogatePair(text, p) ? char.ConvertToUtf32(text, p) : text[p];
                    p += conversion > 0xffff ? 2 : 1;
                    if (p < text.Length)
                    {
                        c = text[p++];
                        if (c != '}' && c != ':')
                            throw PyValueError.Create("expected ':' after conversion specifier");
                    }
                }
                if (c != '}')
                {
                    start = p;
                    int braces = 1;
                    while (p < text.Length)
                    {
                        c = text[p++];
                        if (c == '{') { braces++; expand = true; }
                        else if (c == '}' && --braces == 0) break;
                    }
                    if (braces != 0) throw PyValueError.Create("unmatched '{' in format spec");
                    spec = text.Substring(start, p - start - 1);
                }
            }
            else if (c != '}') throw PyValueError.Create("expected '}' before end of string");

            PyObject value = Resolve(field, args, mapping, ref numbering, ref next);
            value = conversion switch
            {
                0 => value,
                's' => value.ToStr(),
                'r' => value.ToRepr(),
                'a' => Ascii(value.ToRepr()),
                _ => throw PyValueError.Create("Unknown conversion specifier " +
                    (conversion <= 32 || conversion > 126 ? "\\x" + conversion.ToString("x") : ((char)conversion).ToString()))
            };
            if (expand) spec = Expand(spec, args, mapping, depth - 1, ref numbering, ref next);
            output.Append(value.Format(new PyString(spec)).Value);
        }
        return output.ToString();
    }

    private static PyObject Resolve(string field, PyObject[]? args, PyObject? mapping,
        ref int numbering, ref int next)
    {
        int p = 0;
        while (p < field.Length && field[p] != '.' && field[p] != '[') p++;
        string first = field.Substring(0, p);
        long index = Index(first);
        bool automatic = first.Length == 0;
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
        else value = mapping != null ? mapping.GetItem(new PyString(first)) : throw PyKeyError.Create(first);
        while (p < field.Length)
        {
            char kind = field[p++];
            int start = p;
            if (kind == '.')
            {
                while (p < field.Length && field[p] != '.' && field[p] != '[') p++;
                if (p == start) throw PyValueError.Create("Empty attribute in format string");
                value = value.GetAttribute(field.Substring(start, p - start));
            }
            else if (kind == '[')
            {
                while (p < field.Length && field[p] != ']') p++;
                if (p == field.Length) throw PyValueError.Create("Missing ']' in format string");
                if (p == start) throw PyValueError.Create("Empty attribute in format string");
                string key = field.Substring(start, p++ - start);
                index = Index(key);
                value = value.GetItem(index < 0 ? new PyString(key) : new PyInt(index));
            }
            else throw PyValueError.Create("Only '.' or '[' may follow ']' in format field specifier");
        }
        return value;
    }

    private static long Index(string text)
    {
        if (text.Length == 0) return -1;
        long result = 0;
        for (int p = 0; p < text.Length; p++)
        {
            int digit = CharUnicodeInfo.GetDecimalDigitValue(text, p);
            if (digit < 0) return -1;
            if (result > (long.MaxValue - digit) / 10)
                throw PyValueError.Create("Too many decimal digits in format string");
            result = result * 10 + digit;
            if (char.IsSurrogatePair(text, p)) p++;
        }
        return result;
    }

    private static PyString Ascii(PyString repr)
    {
        var result = new StringBuilder(repr.Value.Length);
        for (int p = 0; p < repr.Value.Length; p++)
        {
            int c = repr.Value[p];
            if (char.IsSurrogatePair(repr.Value, p)) c = char.ConvertToUtf32(repr.Value, p++);
            if (c < 128) result.Append((char)c);
            else result.Append(c <= 255 ? "\\x" + c.ToString("x2") :
                c <= 65535 ? "\\u" + c.ToString("x4") : "\\U" + c.ToString("x8"));
        }
        return new PyString(result.ToString());
    }
}
