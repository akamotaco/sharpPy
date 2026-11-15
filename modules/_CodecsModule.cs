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
            var module = new PyModule("_codecs");

            // CPython 3.12: Modules/_codecsmodule.c:1077-1127
            module.ModuleDict["register"] = new PyBuiltinFunction("register", PyRegister);
            module.ModuleDict["lookup"] = PyArgs.MethO("_codecs", "lookup", args => PyLookup(args));
            module.ModuleDict["unregister"] = new PyBuiltinFunction("unregister", PyUnregister);
            module.ModuleDict["encode"] = new PyBuiltinFunction("encode", PyEncode);
            module.ModuleDict["decode"] = new PyBuiltinFunction("decode", PyDecode);
            module.ModuleDict["register_error"] = PyArgs.FastCall("_codecs", "register_error", 2, 2, args => PyRegisterError(args));
            module.ModuleDict["lookup_error"] = PyArgs.MethO("_codecs", "lookup_error", args => PyLookupError(args));

            // Encoding functions - UTF-8

            // _codecs_utf_8_decode_impl and the utf_16/utf_32 ones: (data: Py_buffer, errors=None,
            // final: bool = False, /), codec_tuple(decoded, consumed); the _ex forms take byteorder
            // and answer it as the BOM left it.
            module.ModuleDict["utf_8_decode"] = PyArgs.FastCall("_codecs", "utf_8_decode", 1, 3, args =>
            {
                var data = BufferArgument(args, "utf_8_decode");
                string errors = ErrorsArgument("utf_8_decode", args, 1);
                var consumed = Final(args, 2) ? null : new[] { data.Length };
                string decoded = UnicodeDecoders.DecodeUtf8(data, errors, consumed);
                return Result(PyString.From(decoded), consumed?[0] ?? data.Length);
            });
            UtfDecodeFunction(module, "utf_16_decode", 0, 2);
            UtfDecodeFunction(module, "utf_16_le_decode", -1, 2);
            UtfDecodeFunction(module, "utf_16_be_decode", 1, 2);
            UtfExDecodeFunction(module, "utf_16_ex_decode", 2);
            UtfDecodeFunction(module, "utf_32_decode", 0, 4);
            UtfDecodeFunction(module, "utf_32_le_decode", -1, 4);
            UtfDecodeFunction(module, "utf_32_be_decode", 1, 4);
            UtfExDecodeFunction(module, "utf_32_ex_decode", 4);
            // _codecs_ascii_decode_impl and _codecs_latin_1_decode_impl: (data: Py_buffer, errors=None, /).
            module.ModuleDict["ascii_decode"] = PyArgs.FastCall("_codecs", "ascii_decode", 1, 2, args =>
            {
                var data = BufferArgument(args, "ascii_decode");
                string errors = ErrorsArgument("ascii_decode", args, 1);
                return Result(PyString.From(UnicodeDecoders.DecodeAscii(data, errors)), data.Length);
            });
            module.ModuleDict["latin_1_decode"] = PyArgs.FastCall("_codecs", "latin_1_decode", 1, 2, args =>
            {
                var data = BufferArgument(args, "latin_1_decode");
                ErrorsArgument("latin_1_decode", args, 1);
                return Result(PyString.From(UnicodeDecoders.DecodeLatin1(data)), data.Length);
            });

            // _codecs_*_encode_impl: codec_tuple(the encoder's bytes, PyUnicode_GET_LENGTH(str)).
            EncodeFunction(module, "utf_8_encode", 2, (s, e, a) => UnicodeEncoders.EncodeUtf8(s, e));
            EncodeFunction(module, "utf_16_encode", 3, (s, e, a) => UnicodeEncoders.EncodeUtf16Or32(s, e, ByteOrder(a), 2));
            EncodeFunction(module, "utf_16_le_encode", 2, (s, e, a) => UnicodeEncoders.EncodeUtf16Or32(s, e, -1, 2));
            EncodeFunction(module, "utf_16_be_encode", 2, (s, e, a) => UnicodeEncoders.EncodeUtf16Or32(s, e, 1, 2));
            EncodeFunction(module, "utf_32_encode", 3, (s, e, a) => UnicodeEncoders.EncodeUtf16Or32(s, e, ByteOrder(a), 4));
            EncodeFunction(module, "utf_32_le_encode", 2, (s, e, a) => UnicodeEncoders.EncodeUtf16Or32(s, e, -1, 4));
            EncodeFunction(module, "utf_32_be_encode", 2, (s, e, a) => UnicodeEncoders.EncodeUtf16Or32(s, e, 1, 4));
            EncodeFunction(module, "ascii_encode", 2, (s, e, a) => UnicodeEncoders.EncodeUcs1(s, e, 128));
            EncodeFunction(module, "latin_1_encode", 2, (s, e, a) => UnicodeEncoders.EncodeUcs1(s, e, 256));

            // _codecs_utf_7_decode_impl and _codecs_utf_7_encode_impl
            module.ModuleDict["utf_7_encode"] = PyArgs.FastCall("_codecs", "utf_7_encode", 1, 2, args =>
            {
                string text = TextOf(args[0], "utf_7_encode");
                ErrorsArgument("utf_7_encode", args, 1);
                return Result(PyBytes.From(Utf7Codec.Encode(text, false, false)), ((PyString)args[0]).CodePointLength());
            });
            module.ModuleDict["utf_7_decode"] = PyArgs.FastCall("_codecs", "utf_7_decode", 1, 3, args =>
            {
                var data = BufferArgument(args, "utf_7_decode");
                string errors = ErrorsArgument("utf_7_decode", args, 1);
                bool final = args.Length > 2 && args[2].IsTrue();
                var consumed = final ? null : new[] { data.Length };
                string decoded = Utf7Codec.Decode(data, errors, consumed);
                return Result(PyString.From(decoded), consumed?[0] ?? data.Length);
            });

            // Encoding functions - UTF-16

            // Encoding functions - UTF-32

            // Encoding functions - ASCII

            // Encoding functions - Latin-1

            // Encoding functions - other
            module.ModuleDict["charmap_encode"] = PyArgs.FastCall("_codecs", "charmap_encode", 1, 3, PyCharmapEncode);
            module.ModuleDict["charmap_decode"] = PyArgs.FastCall("_codecs", "charmap_decode", 1, 3, PyCharmapDecode);
            // _codecs_charmap_build_impl: PyUnicode_BuildEncodingMap.
            module.ModuleDict["charmap_build"] = PyArgs.MethO("_codecs", "charmap_build", a => CharmapCodec.BuildEncodingMap(TextOf(a[0], "charmap_build", "argument")));
            // MS_WINDOWS: the code page codecs (mbcs is CP_ACP, oem CP_OEMCP).
            if (OperatingSystem.IsWindows())
            {
                module.ModuleDict["mbcs_encode"] = CodePageFunction("mbcs_encode", 1, 2, a => CodePageEncode("mbcs_encode", PyCodePage.CP_ACP, a, 0));
                module.ModuleDict["mbcs_decode"] = CodePageFunction("mbcs_decode", 1, 3, a => CodePageDecode("mbcs_decode", PyCodePage.CP_ACP, a, 0));
                module.ModuleDict["oem_encode"] = CodePageFunction("oem_encode", 1, 2, a => CodePageEncode("oem_encode", PyCodePage.CP_OEMCP, a, 0));
                module.ModuleDict["oem_decode"] = CodePageFunction("oem_decode", 1, 3, a => CodePageDecode("oem_decode", PyCodePage.CP_OEMCP, a, 0));
                module.ModuleDict["code_page_encode"] = CodePageFunction("code_page_encode", 2, 3, a => CodePageEncode("code_page_encode", PyArgs.Int(a[0]), a, 1));
                module.ModuleDict["code_page_decode"] = CodePageFunction("code_page_decode", 2, 4, a => CodePageDecode("code_page_decode", PyArgs.Int(a[0]), a, 1));
            }
            // _codecs_readbuffer_encode_impl: data is Py_buffer(accept={str, buffer}).
            module.ModuleDict["readbuffer_encode"] = PyArgs.FastCall("_codecs", "readbuffer_encode", 1, 2, args =>
            {
                var data = BytesMethods.StrOrBuffer(args[0]);
                ErrorsArgument("readbuffer_encode", args, 1);
                return Result(PyBytes.From((byte[])data.Clone()), data.Length);
            });
            module.ModuleDict["escape_encode"] = new PyBuiltinFunction("escape_encode", PyEscapeEncode);
            // _codecs_escape_decode_impl: PyBytes_DecodeEscape.
            module.ModuleDict["escape_decode"] = PyArgs.FastCall("_codecs", "escape_decode", 1, 2, args =>
            {
                var data = BytesMethods.StrOrBuffer(args[0]);
                string errors = ErrorsArgument("escape_decode", args, 1);
                return Result(PyBytes.From(EscapeCodecs.DecodeEscape(data, errors)), data.Length);
            });
            EncodeFunction(module, "unicode_escape_encode", 2, (s, e, a) => EscapeCodecs.AsUnicodeEscapeString(s));
            EncodeFunction(module, "raw_unicode_escape_encode", 2, (s, e, a) => EscapeCodecs.AsRawUnicodeEscapeString(s));
            // _codecs_unicode_escape_decode_impl and _codecs_raw_unicode_escape_decode_impl:
            // (data: Py_buffer(accept={str, buffer}), errors=None, final: bool = True, /).
            EscapeDecodeFunction(module, "unicode_escape_decode", EscapeCodecs.DecodeUnicodeEscapeStateful);
            EscapeDecodeFunction(module, "raw_unicode_escape_decode", EscapeCodecs.DecodeRawUnicodeEscapeStateful);

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
            PyCodecRegistry.Unregister(args[0]);
            return PyNone.Instance;
        }

        // CPython 3.12: Python/codecs.c:110-186 (PyCodec_Lookup)
        private static PyObject PyLookup(PyObject[] args, PyDict kwargs = null)
        {

            if (!(args[0] is PyString encodingStr))
                throw PyArgs.BadArgument("lookup", "argument", "str", args[0]);

            return PyCodecRegistry.Lookup(encodingStr.Value);
        }

        // CPython 3.12: Modules/_codecsmodule.c:82-107 (codec_encode)
        private static PyObject PyEncode(PyObject[] args, PyDict kwargs = null)
        {
            // _codecs_encode's clinic: encode(obj, encoding='utf-8', errors='strict'), both by the str converter.
            var parsed = PyArgs.Unpack("encode", args, kwargs, new[] { "obj", "encoding", "errors" }, 0, 1, 3);
            var obj = parsed[0];
            // c_default NULL: no encoding is PyUnicode_GetDefaultEncoding, no errors is not passed.
            string encoding = PyArgs.ClinicStr("encode", "argument 'encoding'", "str", parsed[1]) ?? "utf-8";
            string errors = PyArgs.ClinicStr("encode", "argument 'errors'", "str", parsed[2]);
            return PyCodecRegistry.Encode(obj, encoding, errors);
        }

        // CPython 3.12: Modules/_codecsmodule.c:109-134 (codec_decode)
        private static PyObject PyDecode(PyObject[] args, PyDict kwargs = null)
        {
            // _codecs_decode's clinic: decode(obj, encoding='utf-8', errors='strict').
            var parsed = PyArgs.Unpack("decode", args, kwargs, new[] { "obj", "encoding", "errors" }, 0, 1, 3);
            var obj = parsed[0];
            string encoding = PyArgs.ClinicStr("decode", "argument 'encoding'", "str", parsed[1]) ?? "utf-8";
            string errors = PyArgs.ClinicStr("decode", "argument 'errors'", "str", parsed[2]);
            return PyCodecRegistry.Decode(obj, encoding, errors);
        }


        // Py_buffer
        private static byte[] ByteArrayOf(PyObject obj) => BytesMethods.RequireBuffer(obj);

        // CPython 3.12: Python/codecs.c:381-398 (PyCodec_RegisterError)
        private static PyObject PyRegisterError(PyObject[] args, PyDict kwargs = null)
        {

            // _codecs_register_error: errors through the str converter, then the callable check.
            string name = PyArgs.ClinicStr("register_error", "argument 1", "str", args[0]);
            if (!args[1].IsCallable())
                throw PyTypeError.Create("handler must be callable");
            PyCodecErrors.RegisterError(name, args[1]);
            return PyNone.Instance;
        }

        // CPython 3.12: Python/codecs.c:400-410 (PyCodec_LookupError)
        private static PyObject PyLookupError(PyObject[] args, PyDict kwargs = null)
        {

            // _codecs_lookup_error: METH_O with the str converter.
            return PyCodecErrors.LookupError(PyArgs.ClinicStr("lookup_error", "argument", "str", args[0]));
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



























        // The clinic of _codecs_*_encode: (str: unicode, errors: str(accept={str, NoneType}) = None, /)
        // and, for utf_16_encode and utf_32_encode, byteorder: int = 0.
        private static void EncodeFunction(PyModule module, string name, int max, Func<string, string, PyObject[], byte[]> encoder) =>
            module.ModuleDict[name] = PyArgs.FastCall("_codecs", name, 1, max, args =>
            {
                string text = TextOf(args[0], name);
                string errors = ErrorsArgument(name, args, 1);
                return Result(PyBytes.From(encoder(text, errors, args)), ((PyString)args[0]).CodePointLength());
            });

        private static void EscapeDecodeFunction(PyModule module, string name, Func<byte[], string, int[], string> decoder) =>
            module.ModuleDict[name] = PyArgs.FastCall("_codecs", name, 1, 3, args =>
            {
                var data = BytesMethods.StrOrBuffer(args[0]);
                string errors = ErrorsArgument(name, args, 1);
                bool final = args.Length <= 2 || args[2].IsTrue();
                var consumed = final ? null : new[] { data.Length };
                string decoded = decoder(data, errors, consumed);
                return Result(PyString.From(decoded), consumed?[0] ?? data.Length);
            });

        // final: bool = False
        private static bool Final(PyObject[] args, int index) => args.Length > index && args[index].IsTrue();

        // PyUnicode_DecodeUTF16Stateful or PyUnicode_DecodeUTF32Stateful by the unit width.
        private static string DecodeUtfWide(byte[] data, string errors, ref int byteorder, int[] consumed, int width) =>
            width == 2
                ? UnicodeDecoders.DecodeUtf16(data, errors, ref byteorder, consumed)
                : UnicodeDecoders.DecodeUtf32(data, errors, ref byteorder, consumed);

        private static void UtfDecodeFunction(PyModule module, string name, int byteorder, int width) =>
            module.ModuleDict[name] = PyArgs.FastCall("_codecs", name, 1, 3, args =>
            {
                var data = BufferArgument(args, name);
                string errors = ErrorsArgument(name, args, 1);
                var consumed = Final(args, 2) ? null : new[] { data.Length };
                int order = byteorder;
                string decoded = DecodeUtfWide(data, errors, ref order, consumed, width);
                return Result(PyString.From(decoded), consumed?[0] ?? data.Length);
            });

        // _codecs_utf_16_ex_decode_impl: (data, errors=None, byteorder: int = 0, final: bool = False, /)
        // -> (decoded, consumed, byteorder).
        private static void UtfExDecodeFunction(PyModule module, string name, int width) =>
            module.ModuleDict[name] = PyArgs.FastCall("_codecs", name, 1, 4, args =>
            {
                var data = BufferArgument(args, name);
                string errors = ErrorsArgument(name, args, 1);
                int order = ByteOrder(args);
                var consumed = Final(args, 3) ? null : new[] { data.Length };
                string decoded = DecodeUtfWide(data, errors, ref order, consumed, width);
                return PyTuple.From(new PyObject[] { PyString.From(decoded), PyInt.From(consumed?[0] ?? data.Length), PyInt.From(order) });
            });

        // byteorder: int = 0
        private static int ByteOrder(PyObject[] args) => args.Length > 2 ? PyArgs.Int(args[2]) : 0;

        // The positional-only clinic functions of the code page codecs.
        private static PyBuiltinFunction CodePageFunction(string name, int min, int max, Func<PyObject[], PyObject> body) =>
            new(name, (args, kwargs) =>
            {
                if (kwargs != null && kwargs.Length() != 0)
                    throw PyTypeError.Create($"{name}() takes no keyword arguments");
                PyArgs.ParseTuple(name, args, min, max);
                return body(args);
            });

        // errors: str(accept={str, NoneType}) = None
        private static string ErrorsArgument(string name, PyObject[] args, int index) =>
            index >= args.Length || args[index] is PyNone ? null
                : args[index] is PyString text ? text.Value
                : throw PyArgs.BadArgument(name, $"argument {index + 1}", "str or None", args[index]);

        // _codecs_code_page_encode_impl and the mbcs/oem ones: (bytes, len(str)).
        private static PyObject CodePageEncode(string name, int codePage, PyObject[] args, int first)
        {
            string text = args[first] is PyString str ? str.Value : throw PyArgs.BadArgument(name, $"argument {first + 1}", "str", args[first]);
            var bytes = PyCodePage.Encode(codePage, text, ErrorsArgument(name, args, first + 1));
            return Result(PyBytes.From(bytes), (int)((PyString)args[first]).Length());
        }

        // _codecs_code_page_decode_impl: final is PyUnicode_DecodeCodePageStateful's consumed == NULL.
        private static PyObject CodePageDecode(string name, int codePage, PyObject[] args, int first)
        {
            var data = BytesMethods.RequireBuffer(args[first]);
            string errors = ErrorsArgument(name, args, first + 1);
            bool final = args.Length > first + 2 && args[first + 2].IsTrue();
            string text = PyCodePage.Decode(codePage, data, errors, final, out int consumed);
            return Result(PyString.From(text), final ? data.Length : consumed);
        }

        // Charmap encoding - stub implementations
        private static string ErrorsOf(PyObject[] args, int index) =>
            args.Length > index && args[index] is PyString text ? text.Value : "strict";

        // The unicode converter: "argument 1" for METH_FASTCALL, "argument" for METH_O.
        private static string TextOf(PyObject obj, string name, string displayname = "argument 1") =>
            obj is PyString text ? text.Value
                : throw PyArgs.BadArgument(name, displayname, "str", obj);

        private static PyObject Result(PyObject value, int consumed) => PyTuple.From(new[] { value, PyInt.From(consumed) });

        // _codecs_charmap_decode_impl: a None mapping is Latin-1.
        private static PyObject PyCharmapDecode(PyObject[] args)
        {
            var data = BufferArgument(args, "charmap_decode");
            string errors = ErrorsArgument("charmap_decode", args, 1);
            PyObject mapping = args.Length > 2 && args[2] is not PyNone ? args[2] : null;
            return Result(PyString.From(CharmapCodec.Decode(data, mapping, errors)), data.Length);
        }

        // _codecs_charmap_encode_impl: a None mapping is Latin-1 (unicode_encode_ucs1).
        private static PyObject PyCharmapEncode(PyObject[] args)
        {
            var str = args[0] as PyString ?? throw PyArgs.BadArgument("charmap_encode", "argument 1", "str", args[0]);
            string errors = ErrorsArgument("charmap_encode", args, 1);
            PyObject mapping = args.Length > 2 && args[2] is not PyNone ? args[2] : null;
            var encoded = mapping == null ? str.Encode("latin-1", errors) : PyBytes.From(CharmapCodec.Encode(str.Value, mapping, errors));
            return Result(encoded, str.CodePointLength());
        }

        // PyBytes_Repr without the quotes around it
        private static PyObject PyEscapeEncode(PyObject[] args, PyDict kwargs = null)
        {
            var data = (args[0] as PyBytes)?.Value
                ?? throw PyArgs.BadArgument("escape_encode", "argument 1", "bytes", args[0]);
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






    }
}
