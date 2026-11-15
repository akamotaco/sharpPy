using System;
using System.Collections.Generic;
using System.Text;

namespace SharpPy
{
    /// <summary>
    /// The codec registry of Python/codecs.c (CPython 3.12.15): the search path and the cache keyed
    /// by the normalized name (_PyCodec_Lookup), the items of a codec (codec_getitem), the generic
    /// and the text encode and decode (_PyCodec_EncodeInternal, _PyCodec_LookupTextEncoding).
    /// _PyCodecRegistry_Init imports encodings, whose search_function is the first one registered.
    /// </summary>
    internal static class PyCodecRegistry
    {
        private static readonly List<PyObject> SearchPath = new List<PyObject>();
        private static readonly Dictionary<string, PyObject> Cache =
            new Dictionary<string, PyObject>(StringComparer.Ordinal);
        private static bool _initialized;

        /// <summary>
        /// _Py_normalize_encoding: alphanumerics and '.' are kept lowercased, a run of anything
        /// else becomes one '_', and none is written before the first kept character.
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

        // _PyCodecRegistry_Init: the search path starts with encodings.search_function.
        private static void Init()
        {
            if (_initialized)
                return;
            _initialized = true;
            PyImportSystem.Import("encodings");
        }

        /// <summary>PyCodec_Register</summary>
        internal static void Register(PyObject searchFunction)
        {
            Init();
            if (!searchFunction.IsCallable())
                throw PyTypeError.Create("argument must be callable");
            SearchPath.Add(searchFunction);
        }

        /// <summary>PyCodec_Unregister: the first match is removed, and the cache is cleared.</summary>
        internal static void Unregister(PyObject searchFunction)
        {
            if (!_initialized)
                return;
            int index = SearchPath.FindIndex(fn => ReferenceEquals(fn, searchFunction));
            if (index < 0)
                return;
            Cache.Clear();
            SearchPath.RemoveAt(index);
        }

        /// <summary>
        /// unicode_check_encoding_errors (Objects/unicodeobject.c): in development mode the codec
        /// and the error handler are looked up even when nothing needs them (an empty string), so
        /// a misspelt name fails at once; the common names skip the lookup.
        /// </summary>
        internal static void CheckEncodingErrors(string encoding, string errors)
        {
            if (encoding == null && errors == null || !SharpPyConfig.DevMode)
                return;
            if (encoding != null && encoding is not ("utf-8" or "utf8" or "ascii"))
                Lookup(encoding);
            if (errors != null && errors is not ("strict" or "ignore" or "replace" or "surrogateescape" or "surrogatepass"))
                PyCodecErrors.LookupError(errors);
        }

        /// <summary>
        /// _PyCodec_Lookup: the normalized name in the cache, else each search function in
        /// registration order is called with it; the first that answers a 4-tuple is cached.
        /// </summary>
        internal static PyObject Lookup(string encoding)
        {
            Init();
            string v = Normalize(encoding);
            if (Cache.TryGetValue(v, out var cached))
                return cached;
            if (SearchPath.Count == 0)
                throw PyLookupError.Create("no codec search functions registered: can't find encoding");
            PyObject result = null;
            int i;
            int len = SearchPath.Count;
            for (i = 0; i < len; i++)
            {
                var func = SearchPath[i];
                result = func.Call(new PyObject[] { PyString.From(v) }, null);
                if (result is PyNone)
                    continue;
                if (result is not PyTuple { Items.Length: 4 })
                    throw PyTypeError.Create("codec search functions must return 4-tuples");
                break;
            }
            if (i == len)
                throw PyLookupError.Create($"unknown encoding: {encoding}");
            Cache[v] = result;
            return result;
        }

        // PyTuple_GET_ITEM of a codec, which _PyCodec_Lookup made sure is a 4-tuple.
        private static PyObject Item(PyObject codec, int index) => ((PyTuple)codec).Items[index];

        /// <summary>
        /// _PyCodec_LookupTextEncoding: an exact tuple is a text encoding, as is anything without
        /// _is_text_encoding; a false _is_text_encoding is refused, naming alternateCommand.
        /// </summary>
        internal static PyObject LookupTextEncoding(string encoding, string alternateCommand)
        {
            var codec = Lookup(encoding);
            // Backwards compatibility: assume any raw tuple describes a text encoding, and the
            // same for anything lacking the private attribute.
            if (!codec.IsExactly(PyType.TupleType))
            {
                var attr = codec.LookupAttribute("_is_text_encoding");
                if (attr != null && !attr.IsTrue())
                    throw PyLookupError.Create(
                        $"'{encoding}' is not a text encoding; use {alternateCommand} to handle arbitrary codecs");
            }
            // This appears to be a valid text encoding
            return codec;
        }

        // args_tuple: (object, errors), or (object,) without errors.
        private static PyObject[] ArgsTuple(PyObject obj, string errors) =>
            errors == null ? new[] { obj } : new[] { obj, PyString.From(errors) };

        // _PyErr_FormatNote: the note goes on the exception being raised.
        private static void FormatNote(PythonException raised, string note)
        {
            var exc = raised.PyException;
            try
            {
                PyType.AddNote(exc, PyString.From(note));
            }
            catch (PythonException error)
            {
                // _PyErr_ChainExceptions1
                error.PyException.ContextSet(exc);
                throw;
            }
        }

        // _PyCodec_EncodeInternal and _PyCodec_DecodeInternal: item 0 of the (object, integer)
        // the codec function answers; its second entry is not checked or used.
        private static PyObject CallInternal(PyObject obj, PyObject coder, string encoding, string errors, bool encode)
        {
            PyObject result;
            try
            {
                result = coder.Call(ArgsTuple(obj, errors), null);
            }
            catch (PythonException raised)
            {
                FormatNote(raised, $"{(encode ? "encoding" : "decoding")} with '{encoding}' codec failed");
                throw;
            }
            if (result is not PyTuple { Items.Length: 2 } pair)
                throw PyTypeError.Create(encode ? "encoder must return a tuple (object, integer)" : "decoder must return a tuple (object,integer)");
            return pair.Items[0];
        }

        /// <summary>PyCodec_Encode: codec_getitem(encoding, 0), any codec.</summary>
        internal static PyObject Encode(PyObject obj, string encoding, string errors) =>
            CallInternal(obj, Item(Lookup(encoding), 0), encoding, errors, true);

        /// <summary>PyCodec_Decode: codec_getitem(encoding, 1), any codec.</summary>
        internal static PyObject Decode(PyObject obj, string encoding, string errors) =>
            CallInternal(obj, Item(Lookup(encoding), 1), encoding, errors, false);

        /// <summary>_PyCodec_EncodeText: a text encoding's encoder.</summary>
        internal static PyObject EncodeText(PyObject obj, string encoding, string errors) =>
            CallInternal(obj, Item(LookupTextEncoding(encoding, "codecs.encode()"), 0), encoding, errors, true);

        /// <summary>_PyCodec_DecodeText: a text encoding's decoder.</summary>
        internal static PyObject DecodeText(PyObject obj, string encoding, string errors) =>
            CallInternal(obj, Item(LookupTextEncoding(encoding, "codecs.decode()"), 1), encoding, errors, false);
    }
}
