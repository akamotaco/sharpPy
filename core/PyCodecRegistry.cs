using System;
using System.Collections.Generic;
using System.Text;

namespace SharpPy
{
    /// <summary>
    /// The codec registry - CPython 3.12: Python/codecs.c (_Py_normalize_encoding in
    /// Objects/unicodeobject.c:3354, _PyCodec_Lookup in Python/codecs.c:128,
    /// _PyCodec_LookupTextEncoding in Python/codecs.c:508).
    ///
    /// CPython keeps a list of search functions and a cache keyed by the NORMALIZED name. SharpPy
    /// had the same two fields but normalised by deleting '-' and '_' (so "utf-8" and "utf8" became
    /// one key while "utf_8" and "utf-8" were separate lookups of a name the callbacks never saw -
    /// the callbacks got the caller's original spelling), had no builtin entries at all, and so
    /// could not answer codecs.lookup("utf-16") even though it encodes utf-16.
    ///
    /// Difference from CPython that is kept on purpose: CPython's first search function is
    /// encodings.search_function, which imports a module per codec from Lib/encodings. SharpPy has
    /// no Lib/encodings, so the builtin entries below are answered natively, first, before the
    /// user-registered functions - the position encodings.search_function holds. That is a
    /// mechanism difference; the names, canonical names and refusals are CPython's.
    /// </summary>
    internal static class PyCodecRegistry
    {
        private static readonly List<PyObject> SearchPath = new List<PyObject>();
        private static readonly Dictionary<string, PyObject> Cache =
            new Dictionary<string, PyObject>(StringComparer.Ordinal);
        private static bool _buildingCodecInfo;

        /// <summary>
        /// Python/codecs.c normalises with _Py_normalize_encoding: alphanumerics and '.' are kept
        /// lowercased, every other character is punctuation, a run of punctuation becomes ONE '_',
        /// and no separator is emitted before the first or after the last kept character. So
        /// "  utf-8  ", "utf__8" and "UTF-8" are all "utf_8" - deleting separators, as this file
        /// used to, would also have merged "utf_16_le" with "utf16le" and "utf16" wrongly.
        /// </summary>
        internal static string Normalize(string encoding)
        {
            var sb = new StringBuilder(encoding.Length);
            bool punctuation = false;
            foreach (char c in encoding)
            {
                bool keep = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') ||
                            (c >= 'A' && c <= 'Z') || c == '.';
                if (!keep)
                {
                    punctuation = true;
                    continue;
                }
                if (punctuation && sb.Length > 0) sb.Append('_');
                punctuation = false;
                sb.Append(c >= 'A' && c <= 'Z' ? (char)(c + 32) : c);
            }
            return sb.ToString();
        }

        /// <summary>Python/codecs.c:49 PyCodec_Register - "argument must be callable".</summary>
        internal static void Register(PyObject searchFunction)
        {
            if (!searchFunction.IsCallable())
                throw PyTypeError.Create("argument must be callable");
            SearchPath.Add(searchFunction);
        }

        /// <summary>
        /// Python/codecs.c:56 PyCodec_Unregister. Every match is removed (identity, like CPython)
        /// and the whole cache is dropped, because a removed search function can change the answer
        /// for any name.
        /// </summary>
        internal static void Unregister(PyObject searchFunction)
        {
            SearchPath.RemoveAll(fn => ReferenceEquals(fn, searchFunction));
            Cache.Clear();
        }

        /// <summary>
        /// _PyCodec_Lookup: normalise, cache, builtin entries, then the registered search functions
        /// IN REGISTRATION ORDER, then "unknown encoding" naming the caller's spelling.
        /// </summary>
        private static bool _encodingsImported;

        internal static PyObject Lookup(string original)
        {
            string key = Normalize(original);
            if (Cache.TryGetValue(key, out var cached)) return cached;

            // _PyCodecRegistry_Init imports encodings, whose search_function reads Lib/encodings.
            if (!_encodingsImported)
            {
                _encodingsImported = true;
                PyImportSystem.Import("encodings");
            }

            foreach (var searchFunction in SearchPath.ToArray())
            {
                // The callbacks receive the NORMALISED name. That is why registering a function
                // that tests name == "zzz-test" never fires: the key is "zzz_test".
                PyObject result = searchFunction.Call(new PyObject[] { PyString.From(key) }, null);
                if (result is PyNone) continue;
                if (!(result is PyTuple tuple) || tuple.Items.Length != 4)
                    throw PyTypeError.Create("codec search functions must return 4-tuples");
                Cache[key] = result;
                return result;
            }

            throw PyLookupError.Create($"unknown encoding: {original}");
        }

        /// <summary>
        /// str.encode()/bytes(str, enc)/_codecs.encode refuse a codec that is not a text encoding,
        /// and the refusal names the caller's spelling and the function to use instead. Measured:
        ///   'ab'.encode('rot13') -> LookupError: 'rot13' is not a text encoding;
        ///                            use codecs.encode() to handle arbitrary codecs
        /// </summary>
        internal static PyObject EncodeText(PyObject obj, string encoding, string errors)
        {
            PyObject entry = Lookup(encoding);
            RequireText(entry, encoding, "codecs.encode()");
            return CodecResult(entry, "encode", obj, errors);
        }

        /// <summary>The bytes.decode() side of the same gate.</summary>
        internal static PyObject DecodeText(PyObject obj, string encoding, string errors)
        {
            PyObject entry = Lookup(encoding);
            RequireText(entry, encoding, "codecs.decode()");
            return CodecResult(entry, "decode", obj, errors);
        }

        /// <summary>
        /// Python/codecs.c:508 _PyCodec_LookupTextEncoding: a codec says it is not about text by
        /// carrying _is_text_encoding = False (Lib/encodings/rot_13.py:45 does exactly that).
        /// </summary>
        private static void RequireText(PyObject entry, string encoding, string command)
        {
            if (!IsTextEncoding(entry))
                throw PyLookupError.Create(
                    $"'{encoding}' is not a text encoding; use {command} to handle arbitrary codecs");
        }

        /// <summary>
        /// The entry point for ARBITRARY codecs - _codecs.encode/decode and the codecs.encode/
        /// codecs.decode helpers. There is no text check here, which is exactly the difference:
        ///   codecs.encode(b'ab','hex') -> b'6162'      'ab'.encode('hex') -> LookupError
        /// Text codecs keep going through this interpreter's own encoder/decoder, because those are
        /// the ones that understand error handlers; non-text codecs are asked themselves.
        /// </summary>
        internal static PyObject EncodeAny(PyObject obj, string encoding, string errors)
        {
            PyObject entry = Lookup(encoding);
            if (!IsTextEncoding(entry)) return CodecResult(entry, "encode", obj, errors);
            if (!IsNativeTextCodec(encoding)) return CodecResult(entry, "encode", obj, errors);
            if (!(obj is PyString text))
                throw PyArgs.BadArgument(StatelessEncoderName(encoding), "argument 1", "str", obj);
            return text.Encode(encoding, errors);
        }

        /// <summary>The bytes side of EncodeAny.</summary>
        internal static PyObject DecodeAny(PyObject obj, string encoding, string errors)
        {
            PyObject entry = Lookup(encoding);
            if (!IsTextEncoding(entry) || !IsNativeTextCodec(encoding))
                return CodecResult(entry, "decode", obj, errors);
            byte[] data;
            if (obj is PyBytes b) data = b.Value;
            else if (obj is PyByteArray ba) data = ba.ToByteArrayCopy();
            else throw PyArgs.BadArgument("decode", "argument", "bytes", obj);
            string decoded = PyStringNewArguments.DecodeBytes(data, encoding, errors);
            return PyTuple.From(new PyObject[] { PyString.From(decoded), PyInt.From(data.Length) });
        }

        /// <summary>The _codecs entry point a text codec reports itself as, for its argcheck text.</summary>
        private static string StatelessEncoderName(string encoding)
        {
            string canonical = Aliases.TryGetValue(Normalize(encoding), out string found)
                ? found : Normalize(encoding);
            return TextCodecFunctions.TryGetValue(canonical, out string[] names)
                ? names[0] : "encode";
        }

        private static bool IsTextEncoding(PyObject entry)
        {
            PyObject flag = entry.GetAttribute("_is_text_encoding");
            return !(flag is PyBool bool_ && !bool_.IsTrue());
        }

        /// <summary>
        /// A codec function answers with a (result, consumed) pair; the callers of a codec on the
        /// value side want the result. CPython's str.encode and _codecsmodule.c both take item 0.
        /// </summary>
        private static PyObject CodecResult(PyObject entry, string attribute, PyObject obj, string errors)
        {
            PyObject pair = CallCodec(entry, attribute, obj, errors);
            if (pair is PyTuple two && two.Items.Length == 2) return two.Items[0];
            return pair;
        }

        private static PyObject CallCodec(PyObject entry, string attribute, PyObject obj, string errors)
        {
            PyObject function = entry.GetAttribute(attribute);
            return function.Call(
                new PyObject[] { obj, PyString.From(errors ?? "strict") }, null);
        }

        // --- builtin entries -------------------------------------------------------------------

        /// <summary>
        /// canonical name -> (_codecs encode function, _codecs decode function, is text).
        /// The non-text rows are answered by the native functions below instead of _codecs.
        /// </summary>
        private static readonly Dictionary<string, string[]> TextCodecFunctions =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["utf-8"] = new[] { "utf_8_encode", "utf_8_decode" },
                ["utf-16"] = new[] { "utf_16_encode", "utf_16_decode" },
                ["utf-16-le"] = new[] { "utf_16_le_encode", "utf_16_le_decode" },
                ["utf-16-be"] = new[] { "utf_16_be_encode", "utf_16_be_decode" },
                ["utf-32"] = new[] { "utf_32_encode", "utf_32_decode" },
                ["utf-32-le"] = new[] { "utf_32_le_encode", "utf_32_le_decode" },
                ["utf-32-be"] = new[] { "utf_32_be_encode", "utf_32_be_decode" },
                ["ascii"] = new[] { "ascii_encode", "ascii_decode" },
                ["iso8859-1"] = new[] { "latin_1_encode", "latin_1_decode" },
            };

        private static readonly HashSet<string> NonTextCodecs =
            new HashSet<string>(StringComparer.Ordinal) { "rot-13", "hex", "base64" };

        /// <summary>
        /// Text codecs this interpreter's encoder knows but that have no _codecs entry point of
        /// their own in CPython either, because CPython builds them in Python on top of another
        /// codec (Lib/encodings/utf_8_sig.py).
        /// </summary>
        private static readonly HashSet<string> EngineOnlyText =
            new HashSet<string>(StringComparer.Ordinal) { "utf-8-sig" };

        /// <summary>True when the name is a text codec the native encoder/decoder answers for.</summary>
        private static bool IsNativeTextCodec(string encoding)
        {
            string canonical = Aliases.TryGetValue(Normalize(encoding), out string found)
                ? found : Normalize(encoding);
            return TextCodecFunctions.ContainsKey(canonical) || EngineOnlyText.Contains(canonical);
        }

        /// <summary>
        /// alias -> canonical name, generated from CPython 3.12.15's own Lib/encodings/aliases.py,
        /// filtered to the codecs SharpPy implements (.artifacts/tmp/gen_aliases.py). Generating it
        /// rather than hand-listing aliases fixes both directions: "latin1" now reports the
        /// canonical "iso8859-1", and "koi8" - which is NOT an alias in CPython - correctly fails.
        /// </summary>
        private static readonly Dictionary<string, string> Aliases =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
        ["646"] = "ascii",
        ["8859"] = "iso8859-1",
        ["ansi_x3.4_1968"] = "ascii",
        ["ansi_x3.4_1986"] = "ascii",
        ["ansi_x3_4_1968"] = "ascii",
        ["ascii"] = "ascii",
        ["base64"] = "base64",
        ["base64_codec"] = "base64",
        ["base_64"] = "base64",
        // CPython has no alias rows for utf-8-sig: its search function imports the module named by
        // the normalized name itself (Lib/encodings/utf_8_sig.py), so the spelling of the codec is
        // itself the only key. Generated aliases above cannot cover that, so these two rows are the
        // module-name form and the dashed form of the same codec.
        ["utf_8_sig"] = "utf-8-sig",
        ["utf8_sig"] = "utf-8-sig",
        ["cp367"] = "ascii",
        ["cp65001"] = "utf-8",
        ["cp819"] = "iso8859-1",
        ["csascii"] = "ascii",
        ["csisolatin1"] = "iso8859-1",
        ["hex"] = "hex",
        ["hex_codec"] = "hex",
        ["ibm367"] = "ascii",
        ["ibm819"] = "iso8859-1",
        ["iso646_us"] = "ascii",
        ["iso8859"] = "iso8859-1",
        ["iso8859_1"] = "iso8859-1",
        ["iso_646.irv_1991"] = "ascii",
        ["iso_8859_1"] = "iso8859-1",
        ["iso_8859_1_1987"] = "iso8859-1",
        ["iso_ir_100"] = "iso8859-1",
        ["iso_ir_6"] = "ascii",
        ["l1"] = "iso8859-1",
        ["latin"] = "iso8859-1",
        ["latin1"] = "iso8859-1",
        ["latin_1"] = "iso8859-1",
        ["rot13"] = "rot-13",
        ["rot_13"] = "rot-13",
        ["u16"] = "utf-16",
        ["u32"] = "utf-32",
        ["u8"] = "utf-8",
        ["unicodebigunmarked"] = "utf-16-be",
        ["unicodelittleunmarked"] = "utf-16-le",
        ["us"] = "ascii",
        ["us_ascii"] = "ascii",
        ["utf"] = "utf-8",
        ["utf16"] = "utf-16",
        ["utf32"] = "utf-32",
        ["utf8"] = "utf-8",
        ["utf8_ucs2"] = "utf-8",
        ["utf8_ucs4"] = "utf-8",
        ["utf_16"] = "utf-16",
        ["utf_16_be"] = "utf-16-be",
        ["utf_16_le"] = "utf-16-le",
        ["utf_16be"] = "utf-16-be",
        ["utf_16le"] = "utf-16-le",
        ["utf_32"] = "utf-32",
        ["utf_32_be"] = "utf-32-be",
        ["utf_32_le"] = "utf-32-le",
        ["utf_32be"] = "utf-32-be",
        ["utf_32le"] = "utf-32-le",
        ["utf_8"] = "utf-8",
            };

        // --- the non-text codecs ---------------------------------------------------------------

        /// <summary>The codecs report how much of the INPUT they consumed.</summary>
        private static int ByteLength(PyObject data)
        {
            if (data is PyBytes b) return b.Value.Length;
            if (data is PyByteArray ba) return ba.ToByteArrayCopy().Length;
            if (data is PyString s) return s.Length();
            return 0;
        }
    }
}
