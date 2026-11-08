using System;
using System.Collections.Generic;
using System.Text;

namespace SharpPy.Modules
{
    /// <summary>
    /// CPython _codecs C 확장 모듈
    /// CPython 3.12 참조: Modules/_codecsmodule.c
    /// </summary>
    public static class _CodecsModule
    {
        // Codec registry - CPython 3.12: Python/codecs.c
        private static List<PyObject> _searchPath = new List<PyObject>();
        private static Dictionary<string, PyObject> _cache = new Dictionary<string, PyObject>();

        public static PyModule CreateCodecsModule()
        {
            var module = new PyModule("_codecs", "Fast C implementation of codecs functions");

            // CPython 3.12: Modules/_codecsmodule.c:1077-1127
            module.ModuleDict["register"] = new PyBuiltinFunction("register", PyRegister);
            module.ModuleDict["lookup"] = PyArgs.MethO("_codecs", "lookup", args => PyLookup(args));
            module.ModuleDict["unregister"] = new PyBuiltinFunction("unregister", PyUnregister);
            module.ModuleDict["encode"] = new PyBuiltinFunction("encode", PyEncode);
            module.ModuleDict["decode"] = new PyBuiltinFunction("decode", PyDecode);
            module.ModuleDict["register_error"] = PyArgs.FastCall("_codecs", "register_error", 2, 2, args => PyRegisterError(args));
            module.ModuleDict["lookup_error"] = PyArgs.MethO("_codecs", "lookup_error", args => PyLookupError(args));

            // Encoding functions - UTF-8
            module.ModuleDict["utf_8_encode"] = new PyBuiltinFunction("utf_8_encode", PyUtf8Encode);
            module.ModuleDict["utf_8_decode"] = new PyBuiltinFunction("utf_8_decode", PyUtf8Decode);

            // Encoding functions - UTF-16
            module.ModuleDict["utf_16_encode"] = new PyBuiltinFunction("utf_16_encode", PyUtf16Encode);
            module.ModuleDict["utf_16_decode"] = new PyBuiltinFunction("utf_16_decode", PyUtf16Decode);
            module.ModuleDict["utf_16_le_encode"] = new PyBuiltinFunction("utf_16_le_encode", PyUtf16LeEncode);
            module.ModuleDict["utf_16_le_decode"] = new PyBuiltinFunction("utf_16_le_decode", PyUtf16LeDecode);
            module.ModuleDict["utf_16_be_encode"] = new PyBuiltinFunction("utf_16_be_encode", PyUtf16BeEncode);
            module.ModuleDict["utf_16_be_decode"] = new PyBuiltinFunction("utf_16_be_decode", PyUtf16BeDecode);
            module.ModuleDict["utf_16_ex_decode"] = new PyBuiltinFunction("utf_16_ex_decode", PyUtf16ExDecode);

            // Encoding functions - UTF-32
            module.ModuleDict["utf_32_encode"] = new PyBuiltinFunction("utf_32_encode", PyUtf32Encode);
            module.ModuleDict["utf_32_decode"] = new PyBuiltinFunction("utf_32_decode", PyUtf32Decode);
            module.ModuleDict["utf_32_le_encode"] = new PyBuiltinFunction("utf_32_le_encode", PyUtf32LeEncode);
            module.ModuleDict["utf_32_le_decode"] = new PyBuiltinFunction("utf_32_le_decode", PyUtf32LeDecode);
            module.ModuleDict["utf_32_be_encode"] = new PyBuiltinFunction("utf_32_be_encode", PyUtf32BeEncode);
            module.ModuleDict["utf_32_be_decode"] = new PyBuiltinFunction("utf_32_be_decode", PyUtf32BeDecode);
            module.ModuleDict["utf_32_ex_decode"] = new PyBuiltinFunction("utf_32_ex_decode", PyUtf32ExDecode);

            // Encoding functions - ASCII
            module.ModuleDict["ascii_encode"] = new PyBuiltinFunction("ascii_encode", PyAsciiEncode);
            module.ModuleDict["ascii_decode"] = new PyBuiltinFunction("ascii_decode", PyAsciiDecode);

            // Encoding functions - Latin-1
            module.ModuleDict["latin_1_encode"] = new PyBuiltinFunction("latin_1_encode", PyLatin1Encode);
            module.ModuleDict["latin_1_decode"] = new PyBuiltinFunction("latin_1_decode", PyLatin1Decode);

            // Encoding functions - other
            module.ModuleDict["charmap_encode"] = new PyBuiltinFunction("charmap_encode", PyCharmapEncode);
            module.ModuleDict["charmap_decode"] = new PyBuiltinFunction("charmap_decode", PyCharmapDecode);
            module.ModuleDict["charmap_build"] = new PyBuiltinFunction("charmap_build", PyCharmapBuild);
            module.ModuleDict["escape_encode"] = new PyBuiltinFunction("escape_encode", PyEscapeEncode);
            module.ModuleDict["escape_decode"] = new PyBuiltinFunction("escape_decode", PyEscapeDecode);
            module.ModuleDict["unicode_escape_encode"] = new PyBuiltinFunction("unicode_escape_encode", PyUnicodeEscapeEncode);
            module.ModuleDict["unicode_escape_decode"] = new PyBuiltinFunction("unicode_escape_decode", PyUnicodeEscapeDecode);
            module.ModuleDict["raw_unicode_escape_encode"] = new PyBuiltinFunction("raw_unicode_escape_encode", PyRawUnicodeEscapeEncode);
            module.ModuleDict["raw_unicode_escape_decode"] = new PyBuiltinFunction("raw_unicode_escape_decode", PyRawUnicodeEscapeDecode);

            return module;
        }

        // CPython 3.12: Python/codecs.c:57-71 (PyCodec_Register)
        private static PyObject PyRegister(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
                throw PyTypeError.Create(
                    $"_codecs.register() takes exactly one argument ({args.Length} given)");

            PyCodecRegistry.Register(args[0]);
            return PyNone.Instance;
        }

        // CPython 3.12: Python/codecs.c:56 (PyCodec_Unregister)
        private static PyObject PyUnregister(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"unregister() takes exactly one argument ({args.Length} given)");
            PyCodecRegistry.Unregister(args[0]);
            return PyNone.Instance;
        }

        // CPython 3.12: Python/codecs.c:110-186 (PyCodec_Lookup)
        private static PyObject PyLookup(PyObject[] args, PyDict kwargs = null)
        {

            if (!(args[0] is PyString encodingStr))
                // CPython writes None here, not NoneType: measured
                // "lookup() argument must be str, not None".
                throw PyTypeError.Create("lookup() argument must be str, not " +
                    (args[0] is PyNone ? "None" : args[0].GetTypeName()));

            return PyCodecRegistry.Lookup(encodingStr.Value);
        }

        // CPython 3.12: Modules/_codecsmodule.c:82-107 (codec_encode)
        private static PyObject PyEncode(PyObject[] args, PyDict kwargs = null)
        {
            RequireArgs(args, "encode");
            var obj = args[0];
            string encoding = args.Length > 1 && args[1] is PyString encStr ? encStr.Value : "utf-8";
            string errors = args.Length > 2 && args[2] is PyString errStr ? errStr.Value : "strict";

            // Registry first, like _codecsmodule.c: the codec answers for the name, so
            // _codecs.encode(b'ab') reports utf_8_encode's own refusal and _codecs.encode('ab',
            // 'rot13') reaches the non-text codec instead of being rejected as "not str". Like
            // codec_encode in that file, the wrapper hands back the result and not the
            // (result, consumed) pair the codec function returns.
            return ResultOf(PyCodecRegistry.EncodeAny(obj, encoding, errors));
        }

        /// <summary>
        /// _codecsmodule.c:82-107 extracts item 0 of the codec's (result, consumed) pair. Measured:
        /// codecs.encode(b'ab','hex') is b'6162', not (b'6162', 2).
        /// </summary>
        private static PyObject ResultOf(PyObject codecResult)
        {
            if (codecResult is PyTuple pair && pair.Items.Length == 2) return pair.Items[0];
            return codecResult;
        }

        // CPython 3.12: Modules/_codecsmodule.c:109-134 (codec_decode)
        private static PyObject PyDecode(PyObject[] args, PyDict kwargs = null)
        {
            RequireArgs(args, "decode");
            var obj = args[0];
            string encoding = args.Length > 1 && args[1] is PyString encStr ? encStr.Value : "utf-8";
            string decErrors = args.Length > 2 && args[2] is PyString errStr ? errStr.Value : "strict";

            return ResultOf(PyCodecRegistry.DecodeAny(obj, encoding, decErrors));
        }

        /// <summary>
        /// The clinic wording, measured: encode() missing required argument 'obj' (pos 1) and
        /// encode() takes from 1 to 3 positional arguments but 4 were given.
        /// </summary>
        private static void RequireArgs(PyObject[] args, string name)
        {
            if (args.Length < 1)
                throw PyTypeError.Create($"{name}() missing required argument 'obj' (pos 1)");
            if (args.Length > 3)
                throw PyTypeError.Create(
                    $"{name}() takes from 1 to 3 positional arguments but {args.Length} were given");
        }

        // Py_buffer
        private static byte[] ByteArrayOf(PyObject obj) => BytesMethods.RequireBuffer(obj);

        // CPython 3.12: Python/codecs.c:381-398 (PyCodec_RegisterError)
        private static PyObject PyRegisterError(PyObject[] args, PyDict kwargs = null)
        {

            if (!(args[0] is PyString nameStr))
                throw PyTypeError.Create("first argument must be a string");

            if (!args[1].IsCallable())
                throw PyTypeError.Create("second argument must be callable");
            PyCodecErrors.RegisterError(nameStr.Value, args[1]);
            return PyNone.Instance;
        }

        // CPython 3.12: Python/codecs.c:400-410 (PyCodec_LookupError)
        private static PyObject PyLookupError(PyObject[] args, PyDict kwargs = null)
        {

            if (!(args[0] is PyString nameStr))
                throw PyTypeError.Create("argument must be a string");

            return PyCodecErrors.LookupError(nameStr.Value);
        }

        /// <summary>
        /// One shape for the _codecs.*_encode entry points: hand the text to PyString.Encode (the
        /// only strict encoder) and report the number of CHARACTERS consumed, which is what CPython
        /// returns. Each entry point used to pick an Encoding on its own - so they reproduced
        /// .NET's silent replacement instead of CPython's refusal - and utf_32_le_encode delegated
        /// to utf_32_encode, handing back a BOM for the codec that must not have one.
        /// </summary>
        /// <summary>
        /// Every *_decode entry point of this module used to call Encoding.GetString itself. That is
        /// a seventh, eighth, ... copy of the decoder, and the copies were the weak ones: they left
        /// a byte-order mark in the result of utf_16_decode and silently replaced undecodable bytes
        /// instead of consulting the error handler. They now all ask the one decoder
        /// PyStringNewArguments.DecodeBytes, which is the copy the str(bytes, enc, errors) built-in
        /// and bytes.decode() use. Measured: codecs.getdecoder("utf-16")(b"\xff\xfea\x00") is
        /// ("a", 4) - the mark is consumed, not decoded.
        /// </summary>
        private static byte[] BufferArgument(PyObject[] args, string name) =>
            BytesMethods.RequireBuffer(args[0]);

        private static PyObject Decoded(PyObject[] args, string codec, string name)
        {
            if (args.Length < 1)
                throw PyTypeError.Create($"{name} expected at least 1 argument, got 0");
            var data = BufferArgument(args, name);
            string decoded = PyStringNewArguments.DecodeBytes(data, codec, ErrorsOf(args, 1));
            return PyTuple.From(new PyObject[] { PyString.From(decoded), PyInt.From(data.Length) });
        }

        // utf_8_decode(data, errors=None, final=False): what the decoder consumed.
        private static PyObject DecodedUtf8(PyObject[] args, string name)
        {
            if (args.Length < 1)
                throw PyTypeError.Create($"{name} expected at least 1 argument, got 0");
            var data = BufferArgument(args, name);
            bool final = args.Length > 2 && args[2].IsTrue();
            string decoded = PyBytes.DecodeUtf8(data, ErrorsOf(args, 1), final, out int consumed);
            return PyTuple.From(new PyObject[] { PyString.From(decoded), PyInt.From(consumed) });
        }

        // utf_16/utf_32 (_le, _be, _ex) decoding: the byte order given, or the mark's when it is 0.
        private static (PyString Text, int Consumed, int Order) DecodedUtfWide(byte[] data, string errors, int order, bool final, int width)
        {
            int consumed;
            string text = width == 2
                ? PyBytes.DecodeUtf16(data, errors, ref order, final, out consumed)
                : PyBytes.DecodeUtf32(data, errors, ref order, final, out consumed);
            return (PyString.From(text), consumed, order);
        }

        private static PyObject DecodedWithOrder(PyObject[] args, string name, int width)
        {
            if (args.Length < 1)
                throw PyTypeError.Create($"{name} expected at least 1 argument, got 0");
            var data = BufferArgument(args, name);
            int order = args.Length > 2 ? args[2].ToInt() : 0;
            bool final = args.Length > 3 && args[3].IsTrue();
            var (text, consumed, found) = DecodedUtfWide(data, ErrorsOf(args, 1), order, final, width);
            return PyTuple.From(new PyObject[] { text, PyInt.From(consumed), PyInt.From(found) });
        }

        private static PyObject DecodedFixed(PyObject[] args, string name, int width, int order)
        {
            if (args.Length < 1)
                throw PyTypeError.Create($"{name} expected at least 1 argument, got 0");
            var data = BufferArgument(args, name);
            bool final = args.Length > 2 && args[2].IsTrue();
            var (text, consumed, _) = DecodedUtfWide(data, ErrorsOf(args, 1), order, final, width);
            return PyTuple.From(new PyObject[] { text, PyInt.From(consumed) });
        }

        private static PyObject Encoded(PyObject[] args, string codec, string name)
        {
            // getargs.c words the positional errors of these parsers without the () that the
            // clinic-generated ones carry: "utf_8_encode expected at most 2 arguments, got 3" and
            // "utf_8_encode() argument 1 must be str, not bytes" for the converter itself.
            if (args.Length < 1)
                throw PyTypeError.Create($"{name} expected at least 1 argument, got 0");
            if (!(args[0] is PyString pyStr))
                throw PyTypeError.Create($"{name}() argument 1 must be str, not {args[0].GetTypeName()}");
            string errors = args.Length > 1 && args[1] is PyString errorName ? errorName.Value : null;
            return PyTuple.From(new PyObject[] { pyStr.Encode(codec, errors), PyInt.From(pyStr.CodePointLength()) });
        }

        // UTF-8 encoding - CPython 3.12: Modules/_codecsmodule.c:488-520
        private static PyObject PyUtf8Encode(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length < 1 || args.Length > 2)
                throw PyTypeError.Create($"utf_8_encode expected at most 2 arguments, got {args.Length}");

            if (!(args[0] is PyString pyStr))
                throw PyTypeError.Create($"utf_8_encode() argument 1 must be str, not {args[0].GetTypeName()}");

            return Encoded(args, "utf-8", "utf_8_encode");
        }

        // UTF-8 decoding - CPython 3.12: Modules/_codecsmodule.c:522-565
        private static PyObject PyUtf8Decode(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length < 1 || args.Length > 3)
                throw PyTypeError.Create($"utf_8_decode() takes 1 to 3 arguments ({args.Length} given)");

            return DecodedUtf8(args, "utf_8_decode");
        }

        // UTF-16 encoding
        private static PyObject PyUtf16Encode(PyObject[] args, PyDict kwargs = null)
        {
            if (!(args[0] is PyString pyStr))
                throw PyTypeError.Create("argument must be str");
            return Encoded(args, "utf-16", "utf_16_encode");
        }

        private static PyObject PyUtf16Decode(PyObject[] args, PyDict kwargs = null)
        {
            return DecodedFixed(args, "utf_16_decode", 2, 0);
        }

        private static PyObject PyUtf16LeEncode(PyObject[] args, PyDict kwargs = null)
        {
            if (!(args[0] is PyString pyStr))
                throw PyTypeError.Create("argument must be str");
            return Encoded(args, "utf-16-le", "utf_16_le_encode");
        }

        private static PyObject PyUtf16LeDecode(PyObject[] args, PyDict kwargs = null)
        {
            return DecodedFixed(args, "utf_16_le_decode", 2, -1);
        }

        private static PyObject PyUtf16BeEncode(PyObject[] args, PyDict kwargs = null)
        {
            if (!(args[0] is PyString pyStr))
                throw PyTypeError.Create("argument must be str");
            return Encoded(args, "utf-16-be", "utf_16_be_encode");
        }

        private static PyObject PyUtf16BeDecode(PyObject[] args, PyDict kwargs = null)
        {
            return DecodedFixed(args, "utf_16_be_decode", 2, 1);
        }

        private static PyObject PyUtf16ExDecode(PyObject[] args, PyDict kwargs = null)
        {
            return DecodedWithOrder(args, "utf_16_ex_decode", 2);
        }

        // UTF-32 encoding
        private static PyObject PyUtf32Encode(PyObject[] args, PyDict kwargs = null)
        {
            if (!(args[0] is PyString pyStr))
                throw PyTypeError.Create("argument must be str");
            return Encoded(args, "utf-32", "utf_32_encode");
        }

        private static PyObject PyUtf32Decode(PyObject[] args, PyDict kwargs = null)
        {
            return DecodedFixed(args, "utf_32_decode", 4, 0);
        }

        private static PyObject PyUtf32LeEncode(PyObject[] args, PyDict kwargs = null)
        {
            return Encoded(args, "utf-32-le", "utf_32_le_encode");
        }

        private static PyObject PyUtf32LeDecode(PyObject[] args, PyDict kwargs = null)
        {
            return DecodedFixed(args, "utf_32_le_decode", 4, -1);
        }

        private static PyObject PyUtf32BeEncode(PyObject[] args, PyDict kwargs = null)
        {
            if (!(args[0] is PyString pyStr))
                throw PyTypeError.Create("argument must be str");
            return Encoded(args, "utf-32-be", "utf_32_be_encode");
        }

        private static PyObject PyUtf32BeDecode(PyObject[] args, PyDict kwargs = null)
        {
            return DecodedFixed(args, "utf_32_be_decode", 4, 1);
        }

        private static PyObject PyUtf32ExDecode(PyObject[] args, PyDict kwargs = null)
        {
            return DecodedWithOrder(args, "utf_32_ex_decode", 4);
        }

        // ASCII encoding
        private static PyObject PyAsciiEncode(PyObject[] args, PyDict kwargs = null)
        {
            if (!(args[0] is PyString pyStr))
                throw PyTypeError.Create("argument must be str");
            return Encoded(args, "ascii", "ascii_encode");
        }

        private static PyObject PyAsciiDecode(PyObject[] args, PyDict kwargs = null)
        {
            return Decoded(args, "ascii", "asciidecode");
        }

        // Latin-1 encoding
        private static PyObject PyLatin1Encode(PyObject[] args, PyDict kwargs = null)
        {
            if (!(args[0] is PyString pyStr))
                throw PyTypeError.Create("argument must be str");
            return Encoded(args, "latin-1", "latin_1_encode");
        }

        private static PyObject PyLatin1Decode(PyObject[] args, PyDict kwargs = null)
        {
            return Decoded(args, "iso8859-1", "latin1decode");
        }

        // Charmap encoding - stub implementations
        private static string ErrorsOf(PyObject[] args, int index) =>
            args.Length > index && args[index] is PyString text ? text.Value : "strict";

        private static string TextOf(PyObject obj, string name) =>
            obj is PyString text ? text.Value
                : throw PyTypeError.Create($"{name}() argument 1 must be str, not {obj.GetTypeName()}");

        private static PyObject Result(PyObject value, int consumed) => PyTuple.From(new[] { value, PyInt.From(consumed) });

        // PyUnicode_DecodeCharmap: a str maps by index, U+FFFE is undefined; anything else by lookup
        private static PyObject PyCharmapDecode(PyObject[] args, PyDict kwargs = null)
        {
            var data = ByteArrayOf(args[0]);
            string errors = ErrorsOf(args, 1);
            PyObject mapping = args.Length > 2 ? args[2] : PyNone.Instance;
            var result = new StringBuilder(data.Length);
            for (int i = 0; i < data.Length; i++)
            {
                int ch = -1;
                if (mapping is PyNone)
                    ch = data[i];
                else if (mapping is PyString table)
                {
                    if (i >= 0 && data[i] < table.Value.Length && table.Value[data[i]] != '￾')
                        ch = table.Value[data[i]];
                }
                else
                {
                    PyObject item = null;
                    try
                    {
                        item = mapping.GetItem(PyInt.From(data[i]));
                    }
                    catch (PythonException error) when (error.PyException is SharpPy.PyLookupError)
                    {
                    }
                    if (item is PyString text)
                    {
                        result.Append(text.Value);
                        continue;
                    }
                    if (item is PyInt number)
                        ch = number.ToInt();
                }
                if (ch >= 0)
                {
                    result.Append(PyString.CodePointText(ch));
                    continue;
                }
                switch (errors)
                {
                    case "ignore":
                        break;
                    case "replace":
                        result.Append('�');
                        break;
                    case "backslashreplace":
                        result.Append($"\\x{data[i]:x2}");
                        break;
                    case "surrogateescape":
                        result.Append((char)(0xDC00 + data[i]));
                        break;
                    case "strict":
                        throw PyUnicodeDecodeError.Failure("charmap", data, i, i + 1, "character maps to <undefined>");
                    default:
                        i = PyCodecErrors.CallDecode(errors, "charmap", "character maps to <undefined>", ref data, i, i + 1, result) - 1;
                        break;
                }
            }
            return Result(PyString.From(result.ToString()), data.Length);
        }

        // PyUnicode_EncodeCharmap
        private static PyObject PyCharmapEncode(PyObject[] args, PyDict kwargs = null)
        {
            string text = TextOf(args[0], "charmap_encode");
            string errors = ErrorsOf(args, 1);
            PyObject mapping = args.Length > 2 ? args[2] : PyNone.Instance;
            var result = new List<byte>(text.Length);
            int position = 0;

            bool Write(int cp)
            {
                if (mapping is PyNone)
                {
                    if (cp < 256)
                        result.Add((byte)cp);
                    return cp < 256;
                }
                PyObject item;
                try
                {
                    item = mapping.GetItem(PyInt.From(cp));
                }
                catch (PythonException error) when (error.PyException is SharpPy.PyLookupError)
                {
                    return false;
                }
                if (item is PyInt number)
                    result.Add((byte)number.ToInt());
                else if (item is PyBytes bytes)
                    result.AddRange(bytes.Value);
                else
                    return false;
                return true;
            }

            for (int i = 0; i < text.Length; position++)
            {
                int cp = PyString.CodePointAt(text, i, out int width);
                if (!Write(cp))
                {
                    switch (errors)
                    {
                        case "ignore":
                            break;
                        case "replace":
                            Write('?');
                            break;
                        case "xmlcharrefreplace":
                            result.AddRange(Encoding.ASCII.GetBytes($"&#{cp};"));
                            break;
                        case "backslashreplace":
                            result.AddRange(Encoding.ASCII.GetBytes(cp > 0xFFFF ? $"\\U{cp:x8}" : cp > 0xFF ? $"\\u{cp:x4}" : $"\\x{cp:x2}"));
                            break;
                        default:
                            throw PyUnicodeEncodeError.Failure("charmap", text, position, position + 1, "character maps to <undefined>");
                    }
                }
                i += width;
            }
            return Result(PyBytes.From(result.ToArray()), position);
        }

        // PyUnicode_BuildEncodingMap: the reverse table, a dict here
        private static PyObject PyCharmapBuild(PyObject[] args, PyDict kwargs = null)
        {
            string table = TextOf(args[0], "charmap_build");
            var map = new PyDict();
            int index = 0;
            for (int i = 0; i < table.Length; index++)
            {
                int cp = PyString.CodePointAt(table, i, out int width);
                if (cp != 0xFFFE)
                    map.SetItem(PyInt.From(cp), PyInt.From(index));
                i += width;
            }
            return map;
        }

        // PyBytes_Repr without the quotes around it
        private static PyObject PyEscapeEncode(PyObject[] args, PyDict kwargs = null)
        {
            var data = (args[0] as PyBytes)?.Value
                ?? throw PyTypeError.Create($"escape_encode() argument 1 must be bytes, not {args[0].GetTypeName()}");
            var text = new StringBuilder(data.Length);
            foreach (byte b in data)
            {
                text.Append(b switch
                {
                    (byte)'\\' => "\\\\",
                    (byte)'\'' => "\\'",
                    (byte)'\t' => "\\t",
                    (byte)'\n' => "\\n",
                    (byte)'\r' => "\\r",
                    < 32 or >= 127 => $"\\x{b:x2}",
                    _ => ((char)b).ToString(),
                });
            }
            return Result(PyBytes.From(Encoding.ASCII.GetBytes(text.ToString())), data.Length);
        }

        private static PyObject PyEscapeDecode(PyObject[] args, PyDict kwargs = null)
        {
            var data = BytesMethods.StrOrBuffer(args[0]);
            var decoded = SharpPy.Generated.EscapeDecoders.DecodeBytesEscape(Encoding.Latin1.GetString(data), out _, message => PyValueError.Create(message));
            return Result(PyBytes.From(decoded), data.Length);
        }

        // PyUnicode_AsUnicodeEscapeString
        private static PyObject PyUnicodeEscapeEncode(PyObject[] args, PyDict kwargs = null)
        {
            string text = TextOf(args[0], "unicode_escape_encode");
            var result = new StringBuilder(text.Length);
            int count = 0;
            for (int i = 0; i < text.Length; count++)
            {
                int cp = PyString.CodePointAt(text, i, out int width);
                i += width;
                if (cp == '\\')
                    result.Append("\\\\");
                else if (cp >= 32 && cp < 127)
                    result.Append((char)cp);
                else if (cp == '\t')
                    result.Append("\\t");
                else if (cp == '\n')
                    result.Append("\\n");
                else if (cp == '\r')
                    result.Append("\\r");
                else if (cp < 256)
                    result.Append($"\\x{cp:x2}");
                else if (cp < 0x10000)
                    result.Append($"\\u{cp:x4}");
                else
                    result.Append($"\\U{cp:x8}");
            }
            return Result(PyBytes.From(Encoding.ASCII.GetBytes(result.ToString())), count);
        }

        private static Exception EscapeFailure(string codec, byte[] data, int start, int end, string reason) =>
            PyUnicodeDecodeError.Failure(codec, data, start, end, reason);

        private static PyObject PyUnicodeEscapeDecode(PyObject[] args, PyDict kwargs = null)
        {
            var data = BytesMethods.StrOrBuffer(args[0]);
            string decoded = SharpPy.Generated.EscapeDecoders.DecodeUnicodeEscape(Encoding.Latin1.GetString(data), out _,
                (start, end, reason) => EscapeFailure("unicodeescape", data, start, end, reason));
            return Result(PyString.From(decoded), data.Length);
        }

        // PyUnicode_AsRawUnicodeEscapeString
        private static PyObject PyRawUnicodeEscapeEncode(PyObject[] args, PyDict kwargs = null)
        {
            string text = TextOf(args[0], "raw_unicode_escape_encode");
            var result = new List<byte>(text.Length);
            int count = 0;
            for (int i = 0; i < text.Length; count++)
            {
                int cp = PyString.CodePointAt(text, i, out int width);
                i += width;
                if (cp < 256)
                    result.Add((byte)cp);
                else
                    result.AddRange(Encoding.ASCII.GetBytes(cp < 0x10000 ? $"\\u{cp:x4}" : $"\\U{cp:x8}"));
            }
            return Result(PyBytes.From(result.ToArray()), count);
        }

        // _PyUnicode_DecodeRawUnicodeEscapeStateful: \\u and \\U are escapes when the backslash is itself
        // not escaped, and everything else is Latin-1.
        private static PyObject PyRawUnicodeEscapeDecode(PyObject[] args, PyDict kwargs = null)
        {
            var data = BytesMethods.StrOrBuffer(args[0]);
            var result = new StringBuilder(data.Length);
            int position = 0;
            while (position < data.Length)
            {
                byte c = data[position];
                if (c != (byte)'\\' || position + 1 >= data.Length || (data[position + 1] != (byte)'u' && data[position + 1] != (byte)'U'))
                {
                    result.Append((char)c);
                    position++;
                    // a backslash takes what follows with it
                    if (c == (byte)'\\' && position < data.Length)
                        result.Append((char)data[position++]);
                    continue;
                }
                int start = position;
                int count = data[position + 1] == (byte)'u' ? 4 : 8;
                string message = count == 4 ? "truncated \\uXXXX escape" : "truncated \\UXXXXXXXX escape";
                position += 2;
                long ch = 0;
                for (int i = 0; i < count; i++, position++)
                {
                    int digit = position < data.Length ? Convert.ToInt32(HexValue((char)data[position])) : -1;
                    if (digit < 0)
                        throw PyUnicodeDecodeError.Failure("rawunicodeescape", data, start, position, message);
                    ch = ch << 4 | (long)digit;
                }
                if (ch > 0x10FFFF)
                    throw PyUnicodeDecodeError.Failure("rawunicodeescape", data, start, position, "\\Uxxxxxxxx out of range");
                result.Append(PyString.CodePointText((int)ch));
            }
            return Result(PyString.From(result.ToString()), data.Length);
        }

        private static int HexValue(char c) =>
            c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
    }
}
