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
            long position = PyArgs.IndexSsize(value);
            if (position < 0)
                position += length;
            if (position < 0 || position > length)
                throw PyIndexError.Create($"position {position} from error handler out of bounds");
            return (int)position;
        }

        // ---- the builtin handlers, each on the exception it is given (Python/codecs.c)

        private enum Kind { Encode, Decode, Translate }

        private static Kind KindOf(PyObject exc, bool encode = true, bool decode = true, bool translate = true)
        {
            var type = exc.GetPyType();
            if (encode && type.IsSubclassOf(PyType.UnicodeEncodeErrorType)) return Kind.Encode;
            if (decode && type.IsSubclassOf(PyType.UnicodeDecodeErrorType)) return Kind.Decode;
            if (translate && type.IsSubclassOf(PyType.UnicodeTranslateErrorType)) return Kind.Translate;
            throw WrongException(exc);
        }

        private static PythonException WrongException(PyObject exc) =>
            exc.GetPyType().IsSubclassOf(PyType.BaseExceptionType)
                ? (PythonException)PyTypeError.Create($"don't know how to handle {exc.GetPyType().Name} in error callback")
                : (PythonException)PyTypeError.Create("codec must pass exception instance");

        private static int Field(PyObject exc, string name) => (int)PyArgs.IndexSsize(exc.GetAttribute(name));

        private static PyObject Answer(string replacement, int end) =>
            PyTuple.From(new PyObject[] { PyString.From(replacement), PyInt.From(end) });

        // The characters [start, end) of a str object, as code points.
        private static int[] CodePoints(PyObject exc, int start, int end)
        {
            var text = ((PyString)exc.GetAttribute("object")).Value;
            var points = new List<int>();
            int index = 0;
            for (int unit = 0; unit < text.Length; index++)
            {
                int cp = text[unit];
                int width = 1;
                if (char.IsHighSurrogate(text[unit]) && unit + 1 < text.Length && char.IsLowSurrogate(text[unit + 1]))
                {
                    cp = char.ConvertToUtf32(text[unit], text[unit + 1]);
                    width = 2;
                }
                if (index >= start && index < end) points.Add(cp);
                unit += width;
            }
            return points.ToArray();
        }

        private static void Escape(StringBuilder text, int cp)
        {
            if (cp <= 0xFF) text.Append($"\\x{cp:x2}");
            else if (cp <= 0xFFFF) text.Append($"\\u{cp:x4}");
            else text.Append($"\\U{cp:x8}");
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
            KindOf(exc);
            return Answer("", Field(exc, "end"));
        }

        // PyCodec_ReplaceErrors
        private static PyObject Replace(PyObject exc)
        {
            var kind = KindOf(exc);
            int start = Field(exc, "start"), end = Field(exc, "end");
            int count = Math.Max(end - start, 0);
            return kind switch
            {
                Kind.Encode => Answer(new string('?', count), end),
                Kind.Decode => Answer("�", end),
                _ => Answer(new string('�', count), end),
            };
        }

        // PyCodec_XMLCharRefReplaceErrors
        private static PyObject XmlCharRefReplace(PyObject exc)
        {
            KindOf(exc, decode: false, translate: false);
            int start = Field(exc, "start"), end = Field(exc, "end");
            var text = new StringBuilder();
            foreach (var cp in CodePoints(exc, start, end)) text.Append("&#").Append(cp).Append(';');
            return Answer(text.ToString(), end);
        }

        // PyCodec_BackslashReplaceErrors
        private static PyObject BackslashReplace(PyObject exc)
        {
            var kind = KindOf(exc);
            int start = Field(exc, "start"), end = Field(exc, "end");
            var text = new StringBuilder();
            if (kind == Kind.Decode)
            {
                var data = ((PyBytes)exc.GetAttribute("object")).Value;
                for (int i = start; i < end && i < data.Length; i++) text.Append($"\\x{data[i]:x2}");
            }
            else
                foreach (var cp in CodePoints(exc, start, end)) Escape(text, cp);
            return Answer(text.ToString(), end);
        }

        // PyCodec_NameReplaceErrors
        private static PyObject NameReplace(PyObject exc)
        {
            KindOf(exc, decode: false, translate: false);
            int start = Field(exc, "start"), end = Field(exc, "end");
            var text = new StringBuilder();
            foreach (var cp in CodePoints(exc, start, end))
            {
                // A control character has no name of its own, only an alias: it is escaped.
                bool control = cp <= 0xFFFF && char.GetUnicodeCategory((char)cp) == System.Globalization.UnicodeCategory.Control;
                var name = control || cp > 0x10FFFF ? null : UnicodeNames.Name(cp);
                if (name != null) text.Append("\\N{").Append(name).Append('}');
                else Escape(text, cp);
            }
            return Answer(text.ToString(), end);
        }

        // PyCodec_SurrogateEscapeErrors
        private static PyObject SurrogateEscape(PyObject exc)
        {
            var kind = KindOf(exc, translate: false);
            int start = Field(exc, "start"), end = Field(exc, "end");
            if (kind == Kind.Decode)
            {
                var data = ((PyBytes)exc.GetAttribute("object")).Value;
                var text = new StringBuilder();
                int consumed = 0;
                for (int i = start; i < end && i < data.Length && consumed < 4; i++, consumed++)
                {
                    if (data[i] < 128) break;
                    text.Append((char)(0xDC00 + data[i]));
                }
                if (consumed == 0) Strict(exc);
                return Answer(text.ToString(), start + consumed);
            }
            var bytes = new List<byte>();
            foreach (var cp in CodePoints(exc, start, end))
            {
                if (cp < 0xDC80 || cp > 0xDCFF) Strict(exc);
                bytes.Add((byte)(cp - 0xDC00));
            }
            return PyTuple.From(new PyObject[] { PyBytes.From(bytes.ToArray()), PyInt.From(end) });
        }

        // PyCodec_SurrogatePassErrors, for the UTF-8 codec.
        private static PyObject SurrogatePass(PyObject exc)
        {
            var kind = KindOf(exc, translate: false);
            int start = Field(exc, "start"), end = Field(exc, "end");
            if (kind == Kind.Encode)
            {
                var bytes = new List<byte>();
                foreach (var cp in CodePoints(exc, start, end))
                {
                    if (cp < 0xD800 || cp > 0xDFFF) Strict(exc);
                    bytes.Add((byte)(0xE0 | (cp >> 12)));
                    bytes.Add((byte)(0x80 | ((cp >> 6) & 0x3F)));
                    bytes.Add((byte)(0x80 | (cp & 0x3F)));
                }
                return PyTuple.From(new PyObject[] { PyBytes.From(bytes.ToArray()), PyInt.From(end) });
            }
            var data = ((PyBytes)exc.GetAttribute("object")).Value;
            if (start + 2 < data.Length && (data[start] & 0xF0) == 0xE0 && (data[start + 1] & 0xC0) == 0x80 &&
                (data[start + 2] & 0xC0) == 0x80)
            {
                int cp = ((data[start] & 0x0F) << 12) | ((data[start + 1] & 0x3F) << 6) | (data[start + 2] & 0x3F);
                if (cp >= 0xD800 && cp <= 0xDFFF)
                    return Answer(((char)cp).ToString(), start + 3);
            }
            return Strict(exc);
        }
    }
}
