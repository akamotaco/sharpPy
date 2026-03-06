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
                throw PyTypeError.Create($"register() takes exactly 1 argument ({args.Length} given)");

            var searchFunction = args[0];
            if (!searchFunction.IsCallable())
                throw PyTypeError.Create("argument must be callable");

            _searchPath.Add(searchFunction);
            return PyNone.Instance;
        }

        // CPython 3.12: Python/codecs.c:110-186 (PyCodec_Lookup)
        private static PyObject PyLookup(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"lookup() takes exactly 1 argument ({args.Length} given)");

            if (!(args[0] is PyStr encodingStr))
                throw PyTypeError.Create("encoding must be a string");

            string encoding = encodingStr.Value.ToLowerInvariant().Replace("-", "").Replace("_", "");

            // Check cache first
            if (_cache.TryGetValue(encoding, out var cached))
                return cached;

            // Search through registered search functions
            foreach (var searchFunc in _searchPath)
            {
                var result = searchFunc.Call(new[] { args[0] }, null);
                if (result != PyNone.Instance)
                {
                    _cache[encoding] = result;
                    return result;
                }
            }

            throw SharpPy.PyLookupError.Create($"unknown encoding: {encodingStr.Value}");
        }

        // CPython 3.12: Modules/_codecsmodule.c:82-107 (codec_encode)
        private static PyObject PyEncode(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length < 1 || args.Length > 2)
                throw PyTypeError.Create($"encode() takes 1 or 2 arguments ({args.Length} given)");

            var obj = args[0];
            string encoding = args.Length > 1 && args[1] is PyStr encStr ? encStr.Value : "utf-8";

            // For now, simple UTF-8 encoding
            if (obj is PyStr pyStr)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(pyStr.Value);
                return new PyBytes(bytes);
            }

            throw PyTypeError.Create($"encode() argument must be str, not {obj.GetTypeName()}");
        }

        // CPython 3.12: Modules/_codecsmodule.c:109-134 (codec_decode)
        private static PyObject PyDecode(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length < 1 || args.Length > 2)
                throw PyTypeError.Create($"decode() takes 1 or 2 arguments ({args.Length} given)");

            var obj = args[0];
            string encoding = args.Length > 1 && args[1] is PyStr encStr ? encStr.Value : "utf-8";

            // For now, simple UTF-8 decoding
            if (obj is PyBytes pyBytes)
            {
                string str = Encoding.UTF8.GetString(pyBytes.Value);
                return new PyStr(str);
            }

            throw PyTypeError.Create($"decode() argument must be bytes, not {obj.GetTypeName()}");
        }

        // CPython 3.12: Python/codecs.c:381-398 (PyCodec_RegisterError)
        private static PyObject PyRegisterError(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"register_error() takes exactly 2 arguments ({args.Length} given)");

            if (!(args[0] is PyStr nameStr))
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

            if (!(args[0] is PyStr nameStr))
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
                return new PyTuple(new PyObject[] { new PyStr(""), new PyInt(0) });
            });

            // replace handler
            _errors["replace"] = new PyBuiltinFunction("replace_errors", args =>
            {
                // Return replacement character and position
                return new PyTuple(new PyObject[] { new PyStr("?"), new PyInt(0) });
            });

            // xmlcharrefreplace handler
            _errors["xmlcharrefreplace"] = new PyBuiltinFunction("xmlcharrefreplace_errors", args =>
            {
                return new PyTuple(new PyObject[] { new PyStr(""), new PyInt(0) });
            });

            // backslashreplace handler
            _errors["backslashreplace"] = new PyBuiltinFunction("backslashreplace_errors", args =>
            {
                return new PyTuple(new PyObject[] { new PyStr(""), new PyInt(0) });
            });

            // namereplace handler
            _errors["namereplace"] = new PyBuiltinFunction("namereplace_errors", args =>
            {
                return new PyTuple(new PyObject[] { new PyStr(""), new PyInt(0) });
            });
        }

        // UTF-8 encoding - CPython 3.12: Modules/_codecsmodule.c:488-520
        private static PyObject PyUtf8Encode(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length < 1 || args.Length > 2)
                throw PyTypeError.Create($"utf_8_encode() takes 1 or 2 arguments ({args.Length} given)");

            if (!(args[0] is PyStr pyStr))
                throw PyTypeError.Create("utf_8_encode() argument must be str");

            byte[] bytes = Encoding.UTF8.GetBytes(pyStr.Value);
            int length = pyStr.Value.Length;
            return new PyTuple(new PyObject[] { new PyBytes(bytes), new PyInt(length) });
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
            return new PyTuple(new PyObject[] { new PyStr(str), new PyInt(length) });
        }

        // UTF-16 encoding
        private static PyObject PyUtf16Encode(PyObject[] args, PyDict kwargs = null)
        {
            if (!(args[0] is PyStr pyStr))
                throw PyTypeError.Create("argument must be str");
            byte[] bytes = Encoding.Unicode.GetBytes(pyStr.Value);
            return new PyTuple(new PyObject[] { new PyBytes(bytes), new PyInt(pyStr.Value.Length) });
        }

        private static PyObject PyUtf16Decode(PyObject[] args, PyDict kwargs = null)
        {
            if (!(args[0] is PyBytes pyBytes))
                throw PyTypeError.Create("argument must be bytes");
            string str = Encoding.Unicode.GetString(pyBytes.Value);
            return new PyTuple(new PyObject[] { new PyStr(str), new PyInt(pyBytes.Value.Length) });
        }

        private static PyObject PyUtf16LeEncode(PyObject[] args, PyDict kwargs = null)
        {
            if (!(args[0] is PyStr pyStr))
                throw PyTypeError.Create("argument must be str");
            byte[] bytes = Encoding.Unicode.GetBytes(pyStr.Value);
            return new PyTuple(new PyObject[] { new PyBytes(bytes), new PyInt(pyStr.Value.Length) });
        }

        private static PyObject PyUtf16LeDecode(PyObject[] args, PyDict kwargs = null)
        {
            if (!(args[0] is PyBytes pyBytes))
                throw PyTypeError.Create("argument must be bytes");
            string str = Encoding.Unicode.GetString(pyBytes.Value);
            return new PyTuple(new PyObject[] { new PyStr(str), new PyInt(pyBytes.Value.Length) });
        }

        private static PyObject PyUtf16BeEncode(PyObject[] args, PyDict kwargs = null)
        {
            if (!(args[0] is PyStr pyStr))
                throw PyTypeError.Create("argument must be str");
            byte[] bytes = Encoding.BigEndianUnicode.GetBytes(pyStr.Value);
            return new PyTuple(new PyObject[] { new PyBytes(bytes), new PyInt(pyStr.Value.Length) });
        }

        private static PyObject PyUtf16BeDecode(PyObject[] args, PyDict kwargs = null)
        {
            if (!(args[0] is PyBytes pyBytes))
                throw PyTypeError.Create("argument must be bytes");
            string str = Encoding.BigEndianUnicode.GetString(pyBytes.Value);
            return new PyTuple(new PyObject[] { new PyStr(str), new PyInt(pyBytes.Value.Length) });
        }

        private static PyObject PyUtf16ExDecode(PyObject[] args, PyDict kwargs = null)
        {
            // Extended UTF-16 decode with BOM detection
            return PyUtf16Decode(args, kwargs);
        }

        // UTF-32 encoding
        private static PyObject PyUtf32Encode(PyObject[] args, PyDict kwargs = null)
        {
            if (!(args[0] is PyStr pyStr))
                throw PyTypeError.Create("argument must be str");
            byte[] bytes = Encoding.UTF32.GetBytes(pyStr.Value);
            return new PyTuple(new PyObject[] { new PyBytes(bytes), new PyInt(pyStr.Value.Length) });
        }

        private static PyObject PyUtf32Decode(PyObject[] args, PyDict kwargs = null)
        {
            if (!(args[0] is PyBytes pyBytes))
                throw PyTypeError.Create("argument must be bytes");
            string str = Encoding.UTF32.GetString(pyBytes.Value);
            return new PyTuple(new PyObject[] { new PyStr(str), new PyInt(pyBytes.Value.Length) });
        }

        private static PyObject PyUtf32LeEncode(PyObject[] args, PyDict kwargs = null)
        {
            return PyUtf32Encode(args, kwargs);
        }

        private static PyObject PyUtf32LeDecode(PyObject[] args, PyDict kwargs = null)
        {
            return PyUtf32Decode(args, kwargs);
        }

        private static PyObject PyUtf32BeEncode(PyObject[] args, PyDict kwargs = null)
        {
            if (!(args[0] is PyStr pyStr))
                throw PyTypeError.Create("argument must be str");
            byte[] bytes = Encoding.GetEncoding("utf-32BE").GetBytes(pyStr.Value);
            return new PyTuple(new PyObject[] { new PyBytes(bytes), new PyInt(pyStr.Value.Length) });
        }

        private static PyObject PyUtf32BeDecode(PyObject[] args, PyDict kwargs = null)
        {
            if (!(args[0] is PyBytes pyBytes))
                throw PyTypeError.Create("argument must be bytes");
            string str = Encoding.GetEncoding("utf-32BE").GetString(pyBytes.Value);
            return new PyTuple(new PyObject[] { new PyStr(str), new PyInt(pyBytes.Value.Length) });
        }

        private static PyObject PyUtf32ExDecode(PyObject[] args, PyDict kwargs = null)
        {
            return PyUtf32Decode(args, kwargs);
        }

        // ASCII encoding
        private static PyObject PyAsciiEncode(PyObject[] args, PyDict kwargs = null)
        {
            if (!(args[0] is PyStr pyStr))
                throw PyTypeError.Create("argument must be str");
            byte[] bytes = Encoding.ASCII.GetBytes(pyStr.Value);
            return new PyTuple(new PyObject[] { new PyBytes(bytes), new PyInt(pyStr.Value.Length) });
        }

        private static PyObject PyAsciiDecode(PyObject[] args, PyDict kwargs = null)
        {
            if (!(args[0] is PyBytes pyBytes))
                throw PyTypeError.Create("argument must be bytes");
            string str = Encoding.ASCII.GetString(pyBytes.Value);
            return new PyTuple(new PyObject[] { new PyStr(str), new PyInt(pyBytes.Value.Length) });
        }

        // Latin-1 encoding
        private static PyObject PyLatin1Encode(PyObject[] args, PyDict kwargs = null)
        {
            if (!(args[0] is PyStr pyStr))
                throw PyTypeError.Create("argument must be str");
            byte[] bytes = Encoding.GetEncoding("iso-8859-1").GetBytes(pyStr.Value);
            return new PyTuple(new PyObject[] { new PyBytes(bytes), new PyInt(pyStr.Value.Length) });
        }

        private static PyObject PyLatin1Decode(PyObject[] args, PyDict kwargs = null)
        {
            if (!(args[0] is PyBytes pyBytes))
                throw PyTypeError.Create("argument must be bytes");
            string str = Encoding.GetEncoding("iso-8859-1").GetString(pyBytes.Value);
            return new PyTuple(new PyObject[] { new PyStr(str), new PyInt(pyBytes.Value.Length) });
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
