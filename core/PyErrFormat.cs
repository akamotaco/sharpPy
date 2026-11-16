using System;

namespace SharpPy
{
    /// <summary>
    /// PyUnicode_FromFormat (Objects/unicodeobject.c, unicode_fromformat_arg) and PyErr_Format
    /// (Python/errors.c): a message built as a str from the objects themselves, so a name keeps its
    /// characters - a lone surrogate stays one, where a .NET string would pair it with the next.
    ///
    /// The conversions are the ones of the C function: %U (a str object, as it is), %S (str()),
    /// %R (repr()), %A (ascii()), %V (a str object, else the text), %s (text), %c (a code point),
    /// %d %i %u %x (with the l, ll, z modifiers), %p and %%. A precision limits a str conversion to
    /// that many characters; a width pads it on the left.
    /// </summary>
    internal static class PyErrFormat
    {
        /// <summary>PyUnicode_FromFormat.</summary>
        internal static PyString FromFormat(string format, params object[] args)
        {
            var writer = new PyUnicodeWriter();
            int next = 0;
            int literal = 0;
            for (int i = 0; i < format.Length; i++)
            {
                if (format[i] != '%')
                    continue;
                if (i > literal)
                    writer.WriteWideChar(format.Substring(literal, i - literal));
                int start = i++;
                if (i < format.Length && format[i] == '%')
                {
                    writer.WriteChar('%');
                    literal = i + 1;
                    continue;
                }
                // width and precision
                int width = -1, precision = -1;
                if (i < format.Length && char.IsAsciiDigit(format[i]))
                {
                    width = 0;
                    while (i < format.Length && char.IsAsciiDigit(format[i]))
                        width = width * 10 + (format[i++] - '0');
                }
                if (i < format.Length && format[i] == '.')
                {
                    i++;
                    precision = 0;
                    while (i < format.Length && char.IsAsciiDigit(format[i]))
                        precision = precision * 10 + (format[i++] - '0');
                }
                // the size modifiers say nothing to an object argument
                while (i < format.Length && format[i] is 'l' or 'z' or 't' or 'j')
                    i++;
                if (i >= format.Length)
                    throw new ArgumentException($"incomplete format: {format.Substring(start)}");
                char conversion = format[i];
                if (next >= args.Length)
                    throw new ArgumentException("too few arguments for format");
                object arg = args[next++];
                switch (conversion)
                {
                    case 'U':
                        WriteText(writer, (PyString)arg, width, precision);
                        break;
                    case 'V':
                        WriteText(writer, arg as PyString ?? PyString.From((string)arg), width, precision);
                        break;
                    case 'S':
                        WriteText(writer, ((PyObject)arg).ToStr(), width, precision);
                        break;
                    case 'R':
                        WriteText(writer, ((PyObject)arg).ToRepr(), width, precision);
                        break;
                    case 'A':
                        WriteText(writer, PyString.AsciiOf((PyObject)arg), width, precision);
                        break;
                    case 's':
                        WriteText(writer, PyString.From((string)arg), width, precision);
                        break;
                    case 'c':
                        writer.WriteChar(Convert.ToInt32(arg));
                        break;
                    case 'd':
                    case 'i':
                    case 'u':
                        WriteText(writer, PyString.From(Convert.ToInt64(arg).ToString(System.Globalization.CultureInfo.InvariantCulture)), width, -1);
                        break;
                    case 'x':
                        WriteText(writer, PyString.From(Convert.ToInt64(arg).ToString("x", System.Globalization.CultureInfo.InvariantCulture)), width, -1);
                        break;
                    case 'p':
                        writer.WriteWideChar(PyObjectId.Pointer((PyObject)arg));
                        break;
                    default:
                        throw new ArgumentException($"unsupported format: %{conversion}");
                }
                literal = i + 1;
            }
            if (literal < format.Length)
                writer.WriteWideChar(format.Substring(literal));
            return writer.Finish();
        }

        // unicode_fromformat_write_str: at most precision characters, padded to width on the left.
        private static void WriteText(PyUnicodeWriter writer, PyString text, int width, int precision)
        {
            int length = text.CodePointLength();
            if (precision >= 0 && precision < length)
                length = precision;
            for (int pad = width - length; pad > 0; pad--)
                writer.WriteChar(' ');
            writer.WriteSubstring(text, 0, length);
        }

        /// <summary>
        /// PyErr_Format: the exception type called with the message (_PyErr_CreateException), ready
        /// to throw.
        /// </summary>
        internal static Exception Format(PyType type, string format, params object[] args) =>
            SetObject(type, FromFormat(format, args));

        /// <summary>PyErr_SetObject with a str value: type(value) is the exception raised.</summary>
        internal static Exception SetObject(PyType type, PyObject value)
        {
            // A builtin exception type's tp_new holds the args, which BaseException_init would set again.
            var args = new[] { value };
            var exception = type.Allocator != null ? type.Allocator(args, null) : type.Call(args, null);
            return new PythonException((PyBaseException)exception);
        }
    }
}
