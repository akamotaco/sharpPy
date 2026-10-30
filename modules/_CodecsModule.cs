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
        private static Dictionary<string, PyObject> _errors = new Dictionary<string, PyObject>();

        public static PyModule CreateCodecsModule()
        {
            var module = new PyModule("_codecs", "Fast C implementation of codecs functions");

            // CPython 3.12: Modules/_codecsmodule.c:1077-1127
            module.ModuleDict["register"] = new PyBuiltinFunction("register", PyRegister);
            module.ModuleDict["lookup"] = new PyBuiltinFunction("lookup", PyLookup);
            module.ModuleDict["unregister"] = new PyBuiltinFunction("unregister", PyUnregister);
            module.ModuleDict["encode"] = new PyBuiltinFunction("encode", PyEncode);
            module.ModuleDict["decode"] = new PyBuiltinFunction("decode", PyDecode);
            module.ModuleDict["register_error"] = new PyBuiltinFunction("register_error", PyRegisterError);
            module.ModuleDict["lookup_error"] = new PyBuiltinFunction("lookup_error", PyLookupError);

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

            // Initialize default error handlers
            InitializeErrorHandlers();

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
            if (args.Length != 1)
                throw PyTypeError.Create($"lookup() takes exactly 1 argument ({args.Length} given)");

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

        private static byte[] ByteArrayOf(PyObject obj)
        {
            if (obj is PyBytes b) return b.Value;
            if (obj is PyByteArray ba) return ba.ToByteArrayCopy();
            throw PyTypeError.Create($"decode() argument must be bytes, not {obj.GetTypeName()}");
        }

        private static int Consumed(PyObject obj)
        {
            if (obj is PyBytes b) return b.Value.Length;
            if (obj is PyByteArray ba) return ba.ToByteArrayCopy().Length;
            return 0;
        }

        // CPython 3.12: Python/codecs.c:381-398 (PyCodec_RegisterError)
        private static PyObject PyRegisterError(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"register_error() takes exactly 2 arguments ({args.Length} given)");

            if (!(args[0] is PyString nameStr))
                throw PyTypeError.Create("first argument must be a string");

            var handler = args[1];
            if (!handler.IsCallable())
                throw PyTypeError.Create("second argument must be callable");

            _errors[nameStr.Value] = handler;
            return PyNone.Instance;
        }

        // CPython 3.12: Python/codecs.c:400-410 (PyCodec_LookupError)
        private static PyObject PyLookupError(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"lookup_error() takes exactly 1 argument ({args.Length} given)");

            if (!(args[0] is PyString nameStr))
                throw PyTypeError.Create("argument must be a string");

            if (_errors.TryGetValue(nameStr.Value, out var handler))
                return handler;

            throw SharpPy.PyLookupError.Create($"unknown error handler name '{nameStr.Value}'");
        }

        // Initialize default error handlers - CPython 3.12: Python/codecs.c:969-1012
        private static void InitializeErrorHandlers()
        {
            // strict handler
            _errors["strict"] = new PyBuiltinFunction("strict_errors", args =>
            {
                if (args.Length > 0 && args[0] is PyException exc)
                    throw new PythonException(exc);
                throw PyValueError.Create("strict error handler");
            });

            // ignore handler
            _errors["ignore"] = new PyBuiltinFunction("ignore_errors", args =>
            {
                // Return empty replacement and position to skip
                return new PyTuple(new PyObject[] { new PyString(""), new PyInt(0) });
            });

            // replace handler
            _errors["replace"] = new PyBuiltinFunction("replace_errors", args =>
            {
                // Return replacement character and position
                return new PyTuple(new PyObject[] { new PyString("?"), new PyInt(0) });
            });

            // xmlcharrefreplace handler
            _errors["xmlcharrefreplace"] = new PyBuiltinFunction("xmlcharrefreplace_errors", args =>
            {
                return new PyTuple(new PyObject[] { new PyString(""), new PyInt(0) });
            });

            // backslashreplace handler
            _errors["backslashreplace"] = new PyBuiltinFunction("backslashreplace_errors", args =>
            {
                return new PyTuple(new PyObject[] { new PyString(""), new PyInt(0) });
            });

            // namereplace handler
            _errors["namereplace"] = new PyBuiltinFunction("namereplace_errors", args =>
            {
                return new PyTuple(new PyObject[] { new PyString(""), new PyInt(0) });
            });
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
        private static PyObject Decoded(PyObject[] args, string codec, string name)
        {
            if (args.Length < 1)
                throw PyTypeError.Create($"{name} expected at least 1 argument, got 0");
            if (!(args[0] is PyBytes pyBytes))
                throw PyTypeError.Create($"{name}() argument 1 must be bytes, not {args[0].GetTypeName()}");
            string errors = args.Length > 1 && args[1] is PyString errorName ? errorName.Value : null;
            string decoded = PyStringNewArguments.DecodeBytes(pyBytes.Value, codec, errors);
            return new PyTuple(new PyObject[] { new PyString(decoded), new PyInt(pyBytes.Value.Length) });
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
            return new PyTuple(new PyObject[] { pyStr.Encode(codec), new PyInt(pyStr.Length()) });
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

            if (!(args[0] is PyBytes pyBytes))
                throw PyTypeError.Create("utf_8_decode() argument must be bytes");

            string str = Encoding.UTF8.GetString(pyBytes.Value);
            int length = pyBytes.Value.Length;
            return new PyTuple(new PyObject[] { new PyString(str), new PyInt(length) });
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
            return Decoded(args, "utf-16", "utf16decode");
        }

        private static PyObject PyUtf16LeEncode(PyObject[] args, PyDict kwargs = null)
        {
            if (!(args[0] is PyString pyStr))
                throw PyTypeError.Create("argument must be str");
            return Encoded(args, "utf-16-le", "utf_16_le_encode");
        }

        private static PyObject PyUtf16LeDecode(PyObject[] args, PyDict kwargs = null)
        {
            return Decoded(args, "utf-16-le", "utf16ledecode");
        }

        private static PyObject PyUtf16BeEncode(PyObject[] args, PyDict kwargs = null)
        {
            if (!(args[0] is PyString pyStr))
                throw PyTypeError.Create("argument must be str");
            return Encoded(args, "utf-16-be", "utf_16_be_encode");
        }

        private static PyObject PyUtf16BeDecode(PyObject[] args, PyDict kwargs = null)
        {
            return Decoded(args, "utf-16-be", "utf16bedecode");
        }

        private static PyObject PyUtf16ExDecode(PyObject[] args, PyDict kwargs = null)
        {
            // Extended UTF-16 decode with BOM detection
            return PyUtf16Decode(args, kwargs);
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
            return Decoded(args, "utf-32", "utf32decode");
        }

        private static PyObject PyUtf32LeEncode(PyObject[] args, PyDict kwargs = null)
        {
            return Encoded(args, "utf-32-le", "utf_32_le_encode");
        }

        private static PyObject PyUtf32LeDecode(PyObject[] args, PyDict kwargs = null)
        {
            return PyUtf32Decode(args, kwargs);
        }

        private static PyObject PyUtf32BeEncode(PyObject[] args, PyDict kwargs = null)
        {
            if (!(args[0] is PyString pyStr))
                throw PyTypeError.Create("argument must be str");
            return Encoded(args, "utf-32-be", "utf_32_be_encode");
        }

        private static PyObject PyUtf32BeDecode(PyObject[] args, PyDict kwargs = null)
        {
            return Decoded(args, "utf-32-be", "utf32bedecode");
        }

        private static PyObject PyUtf32ExDecode(PyObject[] args, PyDict kwargs = null)
        {
            return PyUtf32Decode(args, kwargs);
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
        private static PyObject PyCharmapEncode(PyObject[] args, PyDict kwargs = null)
        {
            throw PyNotImplementedError.Create("charmap_encode not yet implemented");
        }

        private static PyObject PyCharmapDecode(PyObject[] args, PyDict kwargs = null)
        {
            throw PyNotImplementedError.Create("charmap_decode not yet implemented");
        }

        private static PyObject PyCharmapBuild(PyObject[] args, PyDict kwargs = null)
        {
            throw PyNotImplementedError.Create("charmap_build not yet implemented");
        }

        // Escape encoding - stub implementations
        private static PyObject PyEscapeEncode(PyObject[] args, PyDict kwargs = null)
        {
            throw PyNotImplementedError.Create("escape_encode not yet implemented");
        }

        private static PyObject PyEscapeDecode(PyObject[] args, PyDict kwargs = null)
        {
            throw PyNotImplementedError.Create("escape_decode not yet implemented");
        }

        private static PyObject PyUnicodeEscapeEncode(PyObject[] args, PyDict kwargs = null)
        {
            throw PyNotImplementedError.Create("unicode_escape_encode not yet implemented");
        }

        private static PyObject PyUnicodeEscapeDecode(PyObject[] args, PyDict kwargs = null)
        {
            throw PyNotImplementedError.Create("unicode_escape_decode not yet implemented");
        }

        private static PyObject PyRawUnicodeEscapeEncode(PyObject[] args, PyDict kwargs = null)
        {
            throw PyNotImplementedError.Create("raw_unicode_escape_encode not yet implemented");
        }

        private static PyObject PyRawUnicodeEscapeDecode(PyObject[] args, PyDict kwargs = null)
        {
            throw PyNotImplementedError.Create("raw_unicode_escape_decode not yet implemented");
        }
    }
}
