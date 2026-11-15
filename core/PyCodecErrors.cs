using System;
using System.Collections.Generic;
using System.Text;

namespace SharpPy
{
    /// <summary>
    /// The error-handler half of Python/codecs.c: one registry (PyCodec_RegisterError,
    /// PyCodec_LookupError) holding the eight builtin handlers (PyCodec_StrictErrors ...
    /// PyCodec_SurrogateEscapeErrors), and the two routes a codec takes when its own fast path does
    /// not know the handler name: unicode_decode_call_errorhandler_writer and
    /// unicode_encode_call_errorhandler (Objects/unicodeobject.c).
    /// </summary>
    internal static class PyCodecErrors
    {
        private static readonly Dictionary<string, PyObject> Registry = new();

        static PyCodecErrors()
        {
            Register("strict", "strict_errors", Strict);
            Register("ignore", "ignore_errors", Ignore);
            Register("replace", "replace_errors", Replace);
            Register("xmlcharrefreplace", "xmlcharrefreplace_errors", XmlCharRefReplace);
            Register("backslashreplace", "backslashreplace_errors", BackslashReplace);
            Register("namereplace", "namereplace_errors", NameReplace);
            Register("surrogatepass", "surrogatepass", SurrogatePass);
            Register("surrogateescape", "surrogateescape", SurrogateEscape);
        }

        private static void Register(string name, string function, Func<PyObject, PyObject> handler) =>
            Registry[name] = new PyBuiltinFunction(function, args =>
            {
                if (args.Length != 1)
                    throw PyTypeError.Create($"{function}() takes exactly one argument ({args.Length} given)");
                return handler(args[0]);
            });

        /// <summary>PyCodec_RegisterError</summary>
        internal static void RegisterError(string name, PyObject handler)
        {
            if (!handler.IsCallable())
                throw PyTypeError.Create("handler must be callable");
            Registry[name] = handler;
        }

        /// <summary>PyCodec_LookupError: the name as given; there is no normalization.</summary>
        internal static PyObject LookupError(string name) =>
            Registry.TryGetValue(name ?? "strict", out var handler)
                ? handler
                : throw PyLookupError.Create($"unknown error handler name '{name}'");

        /// <summary>
        /// unicode_decode_call_errorhandler_writer: the handler gets a UnicodeDecodeError for
        /// input[start:end], must answer (str, int), and may have replaced exc.object, which is the
        /// input from then on. A negative position counts from the end. The answer is the position
        /// decoding continues at.
        /// </summary>
        internal static int CallDecode(string errors, string encoding, string reason, ref byte[] input,
            int start, int end, StringBuilder output)
        {
            var handler = LookupError(errors);
            var exc = PyType.UnicodeDecodeErrorType.Call(new PyObject[]
            {
                PyString.From(encoding), PyBytes.From(input), PyInt.From(start), PyInt.From(end), PyString.From(reason),
            }, null);
            var answer = handler.Call(new[] { exc }, null);
            if (answer is not PyTuple { Items.Length: 2 } pair || pair.Items[0] is not PyString replacement ||
                !PyObject.IndexCheck(pair.Items[1]))
                throw PyTypeError.Create("decoding error handler must return (str, int) tuple");
            input = exc.GetAttribute("object") is PyBytes changed
                ? changed.Value
                : throw PyTypeError.Create("exception attribute object must be bytes");
            output.Append(replacement.Value);
            return NewPosition(pair.Items[1], input.Length);
        }

        /// <summary>
        /// unicode_encode_call_errorhandler: the handler gets a UnicodeEncodeError for the characters
        /// [start, end) of text and must answer (str or bytes, int). Positions are characters.
        /// </summary>
        internal static PyObject CallEncode(string errors, string encoding, string reason, string text,
            int start, int end, int length, out int newPosition)
        {
            var handler = LookupError(errors);
            var exc = PyType.UnicodeEncodeErrorType.Call(new PyObject[]
            {
                PyString.From(encoding), PyString.From(text), PyInt.From(start), PyInt.From(end), PyString.From(reason),
            }, null);
            var answer = handler.Call(new[] { exc }, null);
            if (answer is not PyTuple { Items.Length: 2 } pair || pair.Items[0] is not (PyString or PyBytes) ||
                !PyObject.IndexCheck(pair.Items[1]))
                throw PyTypeError.Create("encoding error handler must return (str/bytes, int) tuple");
            newPosition = NewPosition(pair.Items[1], length);
            return pair.Items[0];
        }

        private static int NewPosition(PyObject value, int length)
        {
            long position = PyArgs.AsSsize(value, PyArgs.SsizeError.Overflow);
            if (position < 0)
                position += length;
            if (position < 0 || position > length)
                throw PyIndexError.Create($"position {position} from error handler out of bounds");
            return (int)position;
        }

        // ---- the builtin handlers of Python/codecs.c, each on the exception it is given

        private enum Kind { Encode, Decode, Translate, Other }

        // PyObject_TypeCheck against the three UnicodeError subclasses, in the handlers' order.
        private static Kind KindOf(PyObject exc)
        {
            var type = exc.GetPyType();
            if (type.IsSubclassOf(PyType.UnicodeEncodeErrorType)) return Kind.Encode;
            if (type.IsSubclassOf(PyType.UnicodeDecodeErrorType)) return Kind.Decode;
            if (type.IsSubclassOf(PyType.UnicodeTranslateErrorType)) return Kind.Translate;
            return Kind.Other;
        }

        // wrong_exception_type
        private static System.Exception WrongExceptionType(PyObject exc) =>
            PyTypeError.Create($"don't know how to handle {exc.GetPyType().FullName} in error callback");

        // PyErr_SetObject(PyExceptionInstance_Class(exc), exc): fail with the original exception.
        private static PythonException Original(PyObject exc) => new PythonException((PyBaseException)exc);

        private static PyUnicodeError Error(PyObject exc) => (PyUnicodeError)exc;

        private static PyObject Answer(PyObject replacement, long end) =>
            PyTuple.From(new PyObject[] { replacement, PyInt.From(end) });

        private static PyObject Answer(string replacement, long end) => Answer(PyString.From(replacement), end);

        // PyUnicode_READ_CHAR over the exception's str object.
        private static int[] Chars(PyObject exc) => UnicodeEncoders.CodePoints(((PyString)Error(exc).GetObject(false)).Value);

        private const string Hexdigits = "0123456789abcdef";

        // PyUnicode_New and PyBytes_FromStringAndSize refuse a negative size (end before start).
        private static int Size(long size, string allocator) =>
            size < 0 ? throw PySystemError.Create($"Negative size passed to {allocator}") : (int)size;

        // \xHH, \uHHHH or \UHHHHHHHH
        private static void Escape(StringBuilder outp, int c)
        {
            outp.Append('\\');
            if (c >= 0x00010000)
            {
                outp.Append('U');
                for (int shift = 28; shift >= 8; shift -= 4)
                    outp.Append(Hexdigits[(c >> shift) & 0xf]);
            }
            else if (c >= 0x100)
            {
                outp.Append('u');
                outp.Append(Hexdigits[(c >> 12) & 0xf]);
                outp.Append(Hexdigits[(c >> 8) & 0xf]);
            }
            else
                outp.Append('x');
            outp.Append(Hexdigits[(c >> 4) & 0xf]);
            outp.Append(Hexdigits[c & 0xf]);
        }

        // PyCodec_StrictErrors
        private static PyObject Strict(PyObject exc)
        {
            if (exc is PyBaseException raised)
                throw new PythonException(raised);
            throw PyTypeError.Create("codec must pass exception instance");
        }

        // PyCodec_IgnoreErrors
        private static PyObject Ignore(PyObject exc)
        {
            var kind = KindOf(exc);
            if (kind == Kind.Other)
                throw WrongExceptionType(exc);
            return Answer("", Error(exc).GetEnd(kind == Kind.Decode));
        }

        // PyCodec_ReplaceErrors
        private static PyObject Replace(PyObject exc)
        {
            switch (KindOf(exc))
            {
                case Kind.Encode:
                    {
                        long start = Error(exc).GetStart(false), end = Error(exc).GetEnd(false);
                        return Answer(new string('?', Size(end - start, "PyUnicode_New")), end);
                    }
                case Kind.Decode:
                    return Answer("�", Error(exc).GetEnd(true));
                case Kind.Translate:
                    {
                        long start = Error(exc).GetStart(false), end = Error(exc).GetEnd(false);
                        return Answer(new string('�', Size(end - start, "PyUnicode_New")), end);
                    }
                default:
                    throw WrongExceptionType(exc);
            }
        }

        // PyCodec_XMLCharRefReplaceErrors
        private static PyObject XmlCharRefReplace(PyObject exc)
        {
            if (KindOf(exc) != Kind.Encode)
                throw WrongExceptionType(exc);
            long start = Error(exc).GetStart(false), end = Error(exc).GetEnd(false);
            var obj = Chars(exc);
            var res = new StringBuilder();
            for (long i = start; i < end; ++i)
                res.Append("&#").Append(obj[i]).Append(';');
            return Answer(res.ToString(), end);
        }

        // PyCodec_BackslashReplaceErrors
        private static PyObject BackslashReplace(PyObject exc)
        {
            var kind = KindOf(exc);
            var res = new StringBuilder();
            long start, end;
            if (kind == Kind.Decode)
            {
                start = Error(exc).GetStart(true);
                end = Error(exc).GetEnd(true);
                var p = ((PyBytes)Error(exc).GetObject(true)).Value;
                Size(4 * (end - start), "PyUnicode_New");
                for (long i = start; i < end; i++)
                {
                    int c = p[i];
                    res.Append('\\').Append('x').Append(Hexdigits[(c >> 4) & 0xf]).Append(Hexdigits[c & 0xf]);
                }
                return Answer(res.ToString(), end);
            }
            if (kind is not (Kind.Encode or Kind.Translate))
                throw WrongExceptionType(exc);
            start = Error(exc).GetStart(false);
            end = Error(exc).GetEnd(false);
            var obj = Chars(exc);
            for (long i = start; i < end; ++i)
                Escape(res, obj[i]);
            return Answer(res.ToString(), end);
        }

        // PyCodec_NameReplaceErrors: ucnhash's getname, else the \x, \u or \U escape.
        private static PyObject NameReplace(PyObject exc)
        {
            if (KindOf(exc) != Kind.Encode)
                throw WrongExceptionType(exc);
            long start = Error(exc).GetStart(false), end = Error(exc).GetEnd(false);
            var obj = Chars(exc);
            var res = new StringBuilder();
            long i;
            for (i = start; i < end; ++i)
            {
                int c = obj[i];
                // A control character has no name of its own, only an alias: it is escaped.
                bool control = c <= 0xFFFF && char.GetUnicodeCategory((char)c) == System.Globalization.UnicodeCategory.Control;
                var name = control ? null : UnicodeNames.Name(c);
                if (name != null)
                    res.Append("\\N{").Append(name).Append('}');
                else
                    Escape(res, c);
            }
            end = i;
            return Answer(res.ToString(), end);
        }

        private const int ENC_UNKNOWN = -1, ENC_UTF8 = 0, ENC_UTF16BE = 1, ENC_UTF16LE = 2, ENC_UTF32BE = 3, ENC_UTF32LE = 4;

        // get_standard_encoding: the UTF codecs surrogatepass knows, by name, on a little-endian host.
        private static int GetStandardEncoding(string encoding, out int bytelength)
        {
            bytelength = 0;
            int at = 0;
            char Char(int i) => i < encoding.Length ? encoding[i] : '\0';
            if (char.ToLowerInvariant(Char(0)) == 'u' && char.ToLowerInvariant(Char(1)) == 't' && char.ToLowerInvariant(Char(2)) == 'f')
            {
                at = 3;
                if (Char(at) == '-' || Char(at) == '_')
                    at++;
                if (Char(at) == '8' && Char(at + 1) == '\0')
                {
                    bytelength = 3;
                    return ENC_UTF8;
                }
                bool sixteen = Char(at) == '1' && Char(at + 1) == '6';
                bool thirtyTwo = Char(at) == '3' && Char(at + 1) == '2';
                if (sixteen || thirtyTwo)
                {
                    at += 2;
                    bytelength = sixteen ? 2 : 4;
                    if (Char(at) == '\0')
                        return sixteen ? ENC_UTF16LE : ENC_UTF32LE;
                    if (Char(at) == '-' || Char(at) == '_')
                        at++;
                    if (char.ToLowerInvariant(Char(at + 1)) == 'e' && Char(at + 2) == '\0')
                    {
                        if (char.ToLowerInvariant(Char(at)) == 'b')
                            return sixteen ? ENC_UTF16BE : ENC_UTF32BE;
                        if (char.ToLowerInvariant(Char(at)) == 'l')
                            return sixteen ? ENC_UTF16LE : ENC_UTF32LE;
                    }
                }
            }
            else if (encoding == "CP_UTF8")
            {
                bytelength = 3;
                return ENC_UTF8;
            }
            return ENC_UNKNOWN;
        }

        private static bool IsSurrogate(int ch) => ch >= 0xD800 && ch <= 0xDFFF;

        // PyCodec_SurrogatePassErrors
        private static PyObject SurrogatePass(PyObject exc)
        {
            var kind = KindOf(exc);
            if (kind == Kind.Encode)
            {
                long start = Error(exc).GetStart(false), end = Error(exc).GetEnd(false);
                var obj = Chars(exc);
                int code = GetStandardEncoding(Error(exc).GetEncoding(), out int bytelength);
                if (code == ENC_UNKNOWN)
                    // Not supported, fail with original exception
                    throw Original(exc);
                Size(bytelength * (end - start), "PyBytes_FromStringAndSize");
                var outp = new List<byte>();
                for (long i = start; i < end; i++)
                {
                    int ch = obj[i];
                    if (!IsSurrogate(ch))
                        // Not a surrogate, fail with original exception
                        throw Original(exc);
                    switch (code)
                    {
                        case ENC_UTF8:
                            outp.Add((byte)(0xe0 | (ch >> 12)));
                            outp.Add((byte)(0x80 | ((ch >> 6) & 0x3f)));
                            outp.Add((byte)(0x80 | (ch & 0x3f)));
                            break;
                        case ENC_UTF16LE:
                            outp.Add((byte)ch);
                            outp.Add((byte)(ch >> 8));
                            break;
                        case ENC_UTF16BE:
                            outp.Add((byte)(ch >> 8));
                            outp.Add((byte)ch);
                            break;
                        case ENC_UTF32LE:
                            outp.Add((byte)ch);
                            outp.Add((byte)(ch >> 8));
                            outp.Add((byte)(ch >> 16));
                            outp.Add((byte)(ch >> 24));
                            break;
                        case ENC_UTF32BE:
                            outp.Add((byte)(ch >> 24));
                            outp.Add((byte)(ch >> 16));
                            outp.Add((byte)(ch >> 8));
                            outp.Add((byte)ch);
                            break;
                    }
                }
                return Answer(PyBytes.From(outp.ToArray()), end);
            }
            if (kind == Kind.Decode)
            {
                long start = Error(exc).GetStart(true);
                Error(exc).GetEnd(true);
                var p = ((PyBytes)Error(exc).GetObject(true)).Value;
                int code = GetStandardEncoding(Error(exc).GetEncoding(), out int bytelength);
                if (code == ENC_UNKNOWN)
                    // Not supported, fail with original exception
                    throw Original(exc);
                // Try decoding a single surrogate character. If there are more, let the codec
                // call us again.
                int ch = 0;
                long s = start;
                if (p.Length - start >= bytelength)
                {
                    switch (code)
                    {
                        case ENC_UTF8:
                            if ((p[s] & 0xf0) == 0xe0 && (p[s + 1] & 0xc0) == 0x80 && (p[s + 2] & 0xc0) == 0x80)
                                // it's a three-byte code
                                ch = ((p[s] & 0x0f) << 12) + ((p[s + 1] & 0x3f) << 6) + (p[s + 2] & 0x3f);
                            break;
                        case ENC_UTF16LE:
                            ch = p[s + 1] << 8 | p[s];
                            break;
                        case ENC_UTF16BE:
                            ch = p[s] << 8 | p[s + 1];
                            break;
                        case ENC_UTF32LE:
                            ch = (p[s + 3] << 24) | (p[s + 2] << 16) | (p[s + 1] << 8) | p[s];
                            break;
                        case ENC_UTF32BE:
                            ch = (p[s] << 24) | (p[s + 1] << 16) | (p[s + 2] << 8) | p[s + 3];
                            break;
                    }
                }
                if (!IsSurrogate(ch))
                    // it's not a surrogate - fail
                    throw Original(exc);
                return Answer(((char)ch).ToString(), start + bytelength);
            }
            throw WrongExceptionType(exc);
        }

        // PyCodec_SurrogateEscapeErrors
        private static PyObject SurrogateEscape(PyObject exc)
        {
            var kind = KindOf(exc);
            if (kind == Kind.Encode)
            {
                long start = Error(exc).GetStart(false), end = Error(exc).GetEnd(false);
                var obj = Chars(exc);
                Size(end - start, "PyBytes_FromStringAndSize");
                var outp = new List<byte>();
                for (long i = start; i < end; i++)
                {
                    int ch = obj[i];
                    if (ch < 0xdc80 || ch > 0xdcff)
                        // Not a UTF-8b surrogate, fail with original exception
                        throw Original(exc);
                    outp.Add((byte)(ch - 0xdc00));
                }
                return Answer(PyBytes.From(outp.ToArray()), end);
            }
            if (kind == Kind.Decode)
            {
                long start = Error(exc).GetStart(true), end = Error(exc).GetEnd(true);
                var p = ((PyBytes)Error(exc).GetObject(true)).Value;
                var str = new StringBuilder(4);
                int consumed = 0;
                // decode up to 4 bad bytes.
                while (consumed < 4 && consumed < end - start)
                {
                    // Refuse to escape ASCII bytes.
                    if (p[start + consumed] < 128)
                        break;
                    str.Append((char)(0xdc00 + p[start + consumed]));
                    consumed++;
                }
                if (consumed == 0)
                    // codec complained about ASCII byte.
                    throw Original(exc);
                return Answer(str.ToString(), start + consumed);
            }
            throw WrongExceptionType(exc);
        }
    }
}
