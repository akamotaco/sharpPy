using System;
using System.Numerics;
using SharpPy.Modules.Lzma;

namespace SharpPy.Modules
{
    /// <summary>
    /// Modules/_lzmamodule.c over liblzma 5.8 in C# (modules/Lzma): LZMACompressor,
    /// LZMADecompressor, LZMAError, the filter specifier conversions and the module functions and
    /// constants, with the C file's functions one for one.
    /// </summary>
    internal static class LzmaModule
    {
        // Container formats
        private const int FORMAT_AUTO = 0, FORMAT_XZ = 1, FORMAT_ALONE = 2, FORMAT_RAW = 3;

        // LZMA_CHECK_UNKNOWN
        private const int LZMA_CHECK_UNKNOWN = LzmaConst.CHECK_ID_MAX + 1;

        private static readonly PyType ErrorType = MakeErrorType();
        private static readonly PyType CompressorType = MakeCompressorType();
        private static readonly PyType DecompressorType = MakeDecompressorType();

        /// <summary>Compressor</summary>
        private sealed class Compressor : PyObject
        {
            internal readonly LzmaStream lzs = new();
            internal bool flushed;
            internal readonly object Lock = new();

            public override PyType GetPyType() => CompressorType;
        }

        /// <summary>Decompressor</summary>
        private sealed class Decompressor : PyObject
        {
            internal readonly LzmaStream lzs = new();
            internal int check;
            internal bool eof;
            internal PyObject unused_data;
            internal bool needs_input;
            internal byte[] input_buffer;
            // lzs->next_in != NULL
            internal bool next_in_set;
            internal readonly object Lock = new();

            public override PyType GetPyType() => DecompressorType;
        }

        // ------------------------------------------------------------ Helper functions

        /// <summary>catch_lzma_error: null when lzret is not an error.</summary>
        private static Exception CatchLzmaError(LzmaRet lzret)
        {
            switch (lzret)
            {
                case LzmaRet.OK:
                case LzmaRet.GET_CHECK:
                case LzmaRet.NO_CHECK:
                case LzmaRet.STREAM_END:
                    return null;
                case LzmaRet.UNSUPPORTED_CHECK:
                    return Error("Unsupported integrity check");
                case LzmaRet.MEM_ERROR:
                    return PyMemoryError.Create();
                case LzmaRet.MEMLIMIT_ERROR:
                    return Error("Memory usage limit exceeded");
                case LzmaRet.FORMAT_ERROR:
                    return Error("Input format not supported by decoder");
                case LzmaRet.OPTIONS_ERROR:
                    return Error("Invalid or unsupported options");
                case LzmaRet.DATA_ERROR:
                    return Error("Corrupt input data");
                case LzmaRet.BUF_ERROR:
                    return Error("Insufficient buffer space");
                case LzmaRet.PROG_ERROR:
                    return Error("Internal error");
                default:
                    return Error($"Unrecognized error from liblzma: {(int)lzret}");
            }
        }

        private static Exception Error(string message) => PyExceptionSlots.Raise(ErrorType, message);

        /// <summary>PyLong_AsUnsignedLongLong: an int only, in the range of a C unsigned long long.</summary>
        private static ulong AsUnsignedLongLong(PyObject obj)
        {
            if (PyInt.LongValue(obj) is not { } number)
                throw PyTypeError.Create("an integer is required");
            BigInteger value = number.Value;
            if (value.Sign < 0)
                throw PyOverflowError.Create("can't convert negative int to unsigned");
            if (value > ulong.MaxValue)
                throw PyOverflowError.Create("int too big to convert");
            return (ulong)value;
        }

        /// <summary>uint32_converter (INT_TYPE_CONVERTER_FUNC)</summary>
        private static uint Uint32Converter(PyObject obj)
        {
            ulong val = AsUnsignedLongLong(obj);
            if (val > uint.MaxValue)
                throw PyOverflowError.Create("Value too large for uint32_t type");
            return (uint)val;
        }

        /// <summary>lzma_vli_converter: lzma_vli is a uint64_t.</summary>
        private static ulong LzmaVliConverter(PyObject obj) => AsUnsignedLongLong(obj);

        /// <summary>lzma_mode_converter: lzma_mode is an enum, an unsigned int here.</summary>
        private static LzmaMode LzmaModeConverter(PyObject obj)
        {
            ulong val = AsUnsignedLongLong(obj);
            if (val > uint.MaxValue)
                throw PyOverflowError.Create("Value too large for lzma_mode type");
            return (LzmaMode)(uint)val;
        }

        /// <summary>lzma_mf_converter: lzma_match_finder is an enum, an unsigned int here.</summary>
        private static LzmaMatchFinder LzmaMfConverter(PyObject obj)
        {
            ulong val = AsUnsignedLongLong(obj);
            if (val > uint.MaxValue)
                throw PyOverflowError.Create("Value too large for lzma_match_finder type");
            return (LzmaMatchFinder)(uint)val;
        }

        /// <summary>
        /// PyArg_ParseTupleAndKeywords(state->empty_tuple, spec, "|O...", optnames, ...): the
        /// options of a filter specifier, each through its converter. Any failure (a key that is not
        /// an option, a value its converter refuses, a spec that is not a dict) is reported by the
        /// caller as the ValueError naming the filter.
        /// </summary>
        private static bool ParseSpecKeywords(PyObject spec, string[] optnames, Action<int, PyObject> convert)
        {
            if (spec is not PyDict dict)
                return false;
            try
            {
                foreach (var entry in dict.GetInternalDict())
                {
                    if (entry.Key is not PyString key)
                        return false;
                    int index = Array.IndexOf(optnames, key.Value);
                    if (index < 0)
                        return false;
                }
                for (int i = 0; i < optnames.Length; i++)
                {
                    var value = (dict.TryGetStoredValue(PyString.From(optnames[i]), out var v0) ? v0 : null);
                    if (value != null)
                        convert(i, value);
                }
            }
            catch (PythonException)
            {
                return false;
            }
            return true;
        }

        /// <summary>PyMapping_GetItemString: null on KeyError.</summary>
        private static PyObject MappingGetItemString(PyObject spec, string key)
        {
            try
            {
                return PyAbstract.GetItem(spec, PyString.From(key));
            }
            catch (PythonException e) when (e.PyException.GetPyType().IsSubclassOf(PyType.KeyErrorType))
            {
                return null;
            }
        }

        // ------------------------------------------------------------ Filter specifier parsing

        /// <summary>parse_filter_spec_lzma</summary>
        private static LzmaOptionsLzma ParseFilterSpecLzma(PyObject spec)
        {
            string[] optnames = { "id", "preset", "dict_size", "lc", "lp", "pb", "mode", "nice_len", "mf", "depth" };
            uint preset = LzmaConst.PRESET_DEFAULT;

            // First, fill in default values for all the options using a preset. Then, override the defaults with any values given by the caller.
            var preset_obj = MappingGetItemString(spec, "preset");
            if (preset_obj != null)
                preset = Uint32Converter(preset_obj);

            var options = new LzmaOptionsLzma();

            if (LzmaProbs.Preset(options, preset))
                throw Error($"Invalid compression preset: {preset}");

            bool ok = ParseSpecKeywords(spec, optnames, (i, value) =>
            {
                switch (i)
                {
                    case 2: options.dict_size = Uint32Converter(value); break;
                    case 3: options.lc = Uint32Converter(value); break;
                    case 4: options.lp = Uint32Converter(value); break;
                    case 5: options.pb = Uint32Converter(value); break;
                    case 6: options.mode = LzmaModeConverter(value); break;
                    case 7: options.nice_len = Uint32Converter(value); break;
                    case 8: options.mf = LzmaMfConverter(value); break;
                    case 9: options.depth = Uint32Converter(value); break;
                }
            });
            if (!ok)
                throw PyValueError.Create("Invalid filter specifier for LZMA filter");

            return options;
        }

        /// <summary>parse_filter_spec_delta</summary>
        private static LzmaOptionsDelta ParseFilterSpecDelta(PyObject spec)
        {
            string[] optnames = { "id", "dist" };
            uint dist = 1;

            bool ok = ParseSpecKeywords(spec, optnames, (i, value) =>
            {
                if (i == 1)
                    dist = Uint32Converter(value);
            });
            if (!ok)
                throw PyValueError.Create("Invalid filter specifier for delta filter");

            return new LzmaOptionsDelta { type = 0, dist = dist };
        }

        /// <summary>parse_filter_spec_bcj</summary>
        private static LzmaOptionsBcj ParseFilterSpecBcj(PyObject spec)
        {
            string[] optnames = { "id", "start_offset" };
            uint start_offset = 0;

            bool ok = ParseSpecKeywords(spec, optnames, (i, value) =>
            {
                if (i == 1)
                    start_offset = Uint32Converter(value);
            });
            if (!ok)
                throw PyValueError.Create("Invalid filter specifier for BCJ filter");

            return new LzmaOptionsBcj { start_offset = start_offset };
        }

        /// <summary>lzma_filter_converter</summary>
        private static LzmaFilter LzmaFilterConverter(PyObject spec)
        {
            if (!PyObject.MappingCheck(spec))
                throw PyTypeError.Create("Filter specifier must be a dict or dict-like object");

            var id_obj = MappingGetItemString(spec, "id")
                ?? throw PyValueError.Create("Filter specifier must have an \"id\" entry");

            var f = new LzmaFilter();
            f.id = AsUnsignedLongLong(id_obj);

            switch (f.id)
            {
                case LzmaConst.FILTER_LZMA1:
                case LzmaConst.FILTER_LZMA2:
                    f.options = ParseFilterSpecLzma(spec);
                    return f;
                case LzmaConst.FILTER_DELTA:
                    f.options = ParseFilterSpecDelta(spec);
                    return f;
                case LzmaConst.FILTER_X86:
                case LzmaConst.FILTER_POWERPC:
                case LzmaConst.FILTER_IA64:
                case LzmaConst.FILTER_ARM:
                case LzmaConst.FILTER_ARMTHUMB:
                case LzmaConst.FILTER_SPARC:
                    f.options = ParseFilterSpecBcj(spec);
                    return f;
                default:
                    throw PyValueError.Create($"Invalid filter ID: {f.id}");
            }
        }

        /// <summary>parse_filter_chain_spec</summary>
        private static LzmaFilter[] ParseFilterChainSpec(PyObject filterspecs)
        {
            long num_filters = PyAbstract.SequenceSize(filterspecs);
            if (num_filters > LzmaConst.FILTERS_MAX)
                throw PyValueError.Create($"Too many filters - liblzma supports a maximum of {LzmaConst.FILTERS_MAX}");

            var filters = new LzmaFilter[LzmaConst.FILTERS_MAX + 1];
            for (int i = 0; i < num_filters; i++)
            {
                var spec = PyAbstract.SequenceGetItem(filterspecs, i);
                filters[i] = LzmaFilterConverter(spec);
            }
            for (long i = num_filters; i <= LzmaConst.FILTERS_MAX; i++)
                filters[i].id = LzmaConst.VLI_UNKNOWN;
            return filters;
        }

        // ------------------------------------------------------------ Filter specifier construction

        /// <summary>spec_add_field</summary>
        private static void SpecAddField(PyDict spec, string key, ulong value) =>
            spec.SetItem(PyString.From(key), PyInt.From(new BigInteger(value)));

        /// <summary>build_filter_spec</summary>
        private static PyObject BuildFilterSpec(LzmaFilter f)
        {
            var spec = new PyDict();

            SpecAddField(spec, "id", f.id);

            switch (f.id)
            {
                // For LZMA1 filters, lzma_properties_{encode,decode}() only look at the lc, lp, pb, and dict_size fields. For LZMA2 filters, only the dict_size field is used.
                case LzmaConst.FILTER_LZMA1:
                    {
                        var options = (LzmaOptionsLzma)f.options;
                        SpecAddField(spec, "lc", options.lc);
                        SpecAddField(spec, "lp", options.lp);
                        SpecAddField(spec, "pb", options.pb);
                        SpecAddField(spec, "dict_size", options.dict_size);
                        break;
                    }
                case LzmaConst.FILTER_LZMA2:
                    {
                        var options = (LzmaOptionsLzma)f.options;
                        SpecAddField(spec, "dict_size", options.dict_size);
                        break;
                    }
                case LzmaConst.FILTER_DELTA:
                    {
                        var options = (LzmaOptionsDelta)f.options;
                        SpecAddField(spec, "dist", options.dist);
                        break;
                    }
                case LzmaConst.FILTER_X86:
                case LzmaConst.FILTER_POWERPC:
                case LzmaConst.FILTER_IA64:
                case LzmaConst.FILTER_ARM:
                case LzmaConst.FILTER_ARMTHUMB:
                case LzmaConst.FILTER_SPARC:
                    {
                        if (f.options is LzmaOptionsBcj options)
                            SpecAddField(spec, "start_offset", options.start_offset);
                        break;
                    }
                default:
                    throw PyValueError.Create($"Invalid filter ID: {f.id}");
            }

            return spec;
        }

        // ------------------------------------------------------------ LZMACompressor

        /// <summary>compress</summary>
        private static byte[] Compress(Compressor c, byte[] data, long len, LzmaAction action)
        {
            var buffer = new BlocksOutputBuffer();
            var lzs = c.lzs;

            lzs.avail_out = buffer.InitAndGrow(-1, out lzs.next_out_buf);
            lzs.next_out = 0;
            lzs.next_in_buf = data;
            lzs.next_in = 0;
            lzs.avail_in = len;

            while (true)
            {
                var lzret = LzmaCore.Code(lzs, action);

                if (lzret == LzmaRet.BUF_ERROR && len == 0 && lzs.avail_out > 0)
                    lzret = LzmaRet.OK; // That wasn't a real error
                var error = CatchLzmaError(lzret);
                if (error != null)
                    throw error;
                if ((action == LzmaAction.RUN && lzs.avail_in == 0)
                        || (action == LzmaAction.FINISH && lzret == LzmaRet.STREAM_END))
                {
                    break;
                }
                else if (lzs.avail_out == 0)
                {
                    lzs.avail_out = buffer.Grow(out lzs.next_out_buf, lzs.avail_out);
                    lzs.next_out = 0;
                }
            }

            return buffer.Finish(lzs.avail_out);
        }

        /// <summary>_lzma_LZMACompressor_compress_impl</summary>
        private static PyObject CompressorCompress(Compressor self, byte[] data)
        {
            lock (self.Lock)
            {
                if (self.flushed)
                    throw PyValueError.Create("Compressor has been flushed");
                return PyBytes.From(Compress(self, data, data.Length, LzmaAction.RUN));
            }
        }

        /// <summary>_lzma_LZMACompressor_flush_impl</summary>
        private static PyObject CompressorFlush(Compressor self)
        {
            lock (self.Lock)
            {
                if (self.flushed)
                    throw PyValueError.Create("Repeated call to flush()");
                self.flushed = true;
                var result = PyBytes.From(Compress(self, Array.Empty<byte>(), 0, LzmaAction.FINISH));
                // The stream is finished and the compressor cannot be used again: lzma_end() now rather
                // than at dealloc, so the buffers go back to the pool (see CodecBufferPool).
                LzmaCore.End(self.lzs);
                return result;
            }
        }

        /// <summary>Compressor_init_xz</summary>
        private static void CompressorInitXz(LzmaStream lzs, int check, uint preset, PyObject filterspecs)
        {
            LzmaRet lzret;

            if (filterspecs is PyNone)
            {
                lzret = LzmaContainer.EasyEncoder(lzs, preset, check);
            }
            else
            {
                var filters = ParseFilterChainSpec(filterspecs);
                lzret = LzmaStreamEncoder.Create(lzs, filters, check);
            }
            var error = CatchLzmaError(lzret);
            if (error != null)
                throw error;
        }

        /// <summary>Compressor_init_alone</summary>
        private static void CompressorInitAlone(LzmaStream lzs, uint preset, PyObject filterspecs)
        {
            LzmaRet lzret;

            if (filterspecs is PyNone)
            {
                var options = new LzmaOptionsLzma();

                if (LzmaProbs.Preset(options, preset))
                    throw Error($"Invalid compression preset: {preset}");
                lzret = LzmaAloneEncoder.Create(lzs, options);
            }
            else
            {
                var filters = ParseFilterChainSpec(filterspecs);
                if (filters[0].id == LzmaConst.FILTER_LZMA1 && filters[1].id == LzmaConst.VLI_UNKNOWN)
                    lzret = LzmaAloneEncoder.Create(lzs, (LzmaOptionsLzma)filters[0].options);
                else
                    throw PyValueError.Create("Invalid filter chain for FORMAT_ALONE - must be a single LZMA1 filter");
            }
            var error = CatchLzmaError(lzret);
            if (error != null)
                throw error;
        }

        /// <summary>Compressor_init_raw</summary>
        private static void CompressorInitRaw(LzmaStream lzs, PyObject filterspecs)
        {
            if (filterspecs is PyNone)
                throw PyValueError.Create("Must specify filters for FORMAT_RAW");
            var filters = ParseFilterChainSpec(filterspecs);
            var lzret = LzmaFilters.RawEncoder(lzs, filters);
            var error = CatchLzmaError(lzret);
            if (error != null)
                throw error;
        }

        /// <summary>
        /// PyArg_ParseTupleAndKeywords's argument matching (vgetargskeywords): one slot per name, null
        /// where nothing was given.
        /// </summary>
        private static PyObject[] ParseTupleAndKeywords(string fname, PyObject[] args, PyDict kwargs, string[] names)
        {
            int nargs = args.Length;
            int nkwargs = kwargs?.Length() ?? 0;
            int len = names.Length;
            if (nargs + nkwargs > len)
                throw PyTypeError.Create($"{fname}() takes at most {len} {(nargs == 0 ? "keyword " : "")}argument{(len == 1 ? "" : "s")} ({nargs + nkwargs} given)");
            var result = new PyObject[len];
            int used = 0;
            for (int i = 0; i < len; i++)
            {
                PyObject byName = (kwargs != null && kwargs.TryGetStoredValue(PyString.From(names[i]), out var v1) ? v1 : null);
                if (i < nargs)
                {
                    if (byName != null)
                        throw PyTypeError.Create($"argument for {fname}() given by name ('{names[i]}') and position ({i + 1})");
                    result[i] = args[i];
                }
                else if (byName != null)
                {
                    result[i] = byName;
                    used++;
                }
            }
            if (used < nkwargs)
            {
                foreach (var entry in kwargs.GetInternalDict())
                {
                    if (entry.Key is not PyString key)
                        throw PyTypeError.Create("keywords must be strings");
                    if (Array.IndexOf(names, key.Value) < 0)
                        throw PyTypeError.Create($"'{key.Value}' is an invalid keyword argument for {fname}()");
                }
            }
            return result;
        }

        /// <summary>Compressor_new</summary>
        private static PyObject CompressorNew(PyType type, PyObject[] args, PyDict kwargs)
        {
            string[] arg_names = { "format", "check", "preset", "filters" };
            int format = FORMAT_XZ;
            int check = -1;
            uint preset = LzmaConst.PRESET_DEFAULT;

            var parsed = ParseTupleAndKeywords("LZMACompressor", args, kwargs, arg_names);
            if (parsed[0] != null) format = PyArgs.ParseInt(parsed[0]);
            if (parsed[1] != null) check = PyArgs.ParseInt(parsed[1]);
            PyObject preset_obj = parsed[2] ?? PyNone.Instance;
            PyObject filterspecs = parsed[3] ?? PyNone.Instance;

            if (format != FORMAT_XZ && check != -1 && check != LzmaConst.CHECK_NONE)
                throw PyValueError.Create("Integrity checks are only supported by FORMAT_XZ");

            if (preset_obj is not PyNone && filterspecs is not PyNone)
                throw PyValueError.Create("Cannot specify both preset and filter chain");

            if (preset_obj is not PyNone)
                preset = Uint32Converter(preset_obj);

            var self = new Compressor();
            self.flushed = false;
            switch (format)
            {
                case FORMAT_XZ:
                    if (check == -1)
                        check = LzmaConst.CHECK_CRC64;
                    CompressorInitXz(self.lzs, check, preset, filterspecs);
                    break;

                case FORMAT_ALONE:
                    CompressorInitAlone(self.lzs, preset, filterspecs);
                    break;

                case FORMAT_RAW:
                    CompressorInitRaw(self.lzs, filterspecs);
                    break;

                default:
                    throw PyValueError.Create($"Invalid container format: {format}");
            }

            return self;
        }

        private static PyType MakeCompressorType()
        {
            var type = new PyType("LZMACompressor", new[] { PyType.ObjectType }, "_lzma");
            type.MarkSpecHeapType();
            // Compressor_doc (written by hand in the C file, signature line included)
            type.TypeDict["__doc__"] = PyString.From(
                "LZMACompressor(format=FORMAT_XZ, check=-1, preset=None, filters=None)\n" +
                "\n" +
                "Create a compressor object for compressing data incrementally.\n" +
                "\n" +
                "format specifies the container format to use for the output. This can\n" +
                "be FORMAT_XZ (default), FORMAT_ALONE, or FORMAT_RAW.\n" +
                "\n" +
                "check specifies the integrity check to use. For FORMAT_XZ, the default\n" +
                "is CHECK_CRC64. FORMAT_ALONE and FORMAT_RAW do not support integrity\n" +
                "checks; for these formats, check must be omitted, or be CHECK_NONE.\n" +
                "\n" +
                "The settings used by the compressor can be specified either as a\n" +
                "preset compression level (with the 'preset' argument), or in detail\n" +
                "as a custom filter chain (with the 'filters' argument). For FORMAT_XZ\n" +
                "and FORMAT_ALONE, the default is to use the PRESET_DEFAULT preset\n" +
                "level. For FORMAT_RAW, the caller must always specify a filter chain;\n" +
                "the raw compressor does not support preset compression levels.\n" +
                "\n" +
                "preset (if provided) should be an integer in the range 0-9, optionally\n" +
                "OR-ed with the constant PRESET_EXTREME.\n" +
                "\n" +
                "filters (if provided) should be a sequence of dicts. Each dict should\n" +
                "have an entry for \"id\" indicating the ID of the filter, plus\n" +
                "additional entries for options to the filter.\n" +
                "\n" +
                "For one-shot compression, use the compress() function instead.\n");
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type,
                (cls, args, kwargs) => CompressorNew((PyType)cls, args, kwargs), acceptsKwargs: true);
            // _lzma_LZMACompressor_compress: (data, /)
            PyArgs.Define(type, "compress", PyCall.One, (o, args) =>
                CompressorCompress((Compressor)o, BytesMethods.RequireBuffer(args[0])));
            // _lzma_LZMACompressor_flush
            PyArgs.Define(type, "flush", PyCall.NoArgs, (o, args) => CompressorFlush((Compressor)o));
            return type;
        }

        // ------------------------------------------------------------ LZMADecompressor

        /// <summary>decompress_buf</summary>
        private static byte[] DecompressBuf(Decompressor d, long max_length)
        {
            var lzs = d.lzs;
            var buffer = new BlocksOutputBuffer();

            lzs.avail_out = buffer.InitAndGrow(max_length, out lzs.next_out_buf);
            lzs.next_out = 0;

            while (true)
            {
                var lzret = LzmaCore.Code(lzs, LzmaAction.RUN);

                if (lzret == LzmaRet.BUF_ERROR && lzs.avail_in == 0 && lzs.avail_out > 0)
                    lzret = LzmaRet.OK; // That wasn't a real error
                var error = CatchLzmaError(lzret);
                if (error != null)
                    throw error;
                if (lzret == LzmaRet.GET_CHECK || lzret == LzmaRet.NO_CHECK)
                    d.check = LzmaCore.GetCheck(lzs);
                if (lzret == LzmaRet.STREAM_END)
                {
                    d.eof = true;
                    break;
                }
                else if (lzs.avail_out == 0)
                {
                    // Need to check lzs->avail_out before lzs->avail_in. Maybe lzs's internal state still have a few bytes can be output, grow the output buffer and continue if max_lengh < 0.
                    if (buffer.GetDataSize(lzs.avail_out) == max_length)
                        break;
                    lzs.avail_out = buffer.Grow(out lzs.next_out_buf, lzs.avail_out);
                    lzs.next_out = 0;
                }
                else if (lzs.avail_in == 0)
                {
                    break;
                }
            }

            return buffer.Finish(lzs.avail_out);
        }

        /// <summary>decompress</summary>
        private static byte[] Decompress(Decompressor d, byte[] data, long max_length)
        {
            bool input_buffer_in_use;
            var lzs = d.lzs;
            long len = data.Length;

            // Prepend unconsumed input if necessary
            if (d.next_in_set)
            {
                // Number of bytes we can append to input buffer
                long avail_now = d.input_buffer.Length - (lzs.next_in + lzs.avail_in);

                // Number of bytes we can append if we move existing contents to beginning of buffer (overwriting consumed input)
                long avail_total = d.input_buffer.Length - lzs.avail_in;

                if (avail_total < len)
                {
                    long new_size = d.input_buffer.Length + len - avail_now;
                    var tmp = new byte[PyAllocation.ArrayLength(new_size)];
                    Buffer.BlockCopy(d.input_buffer, 0, tmp, 0, d.input_buffer.Length);
                    d.input_buffer = tmp;
                    lzs.next_in_buf = tmp;
                }
                else if (avail_now < len)
                {
                    Buffer.BlockCopy(d.input_buffer, lzs.next_in, d.input_buffer, 0, (int)lzs.avail_in);
                    lzs.next_in = 0;
                }
                Buffer.BlockCopy(data, 0, d.input_buffer, (int)(lzs.next_in + lzs.avail_in), (int)len);
                lzs.avail_in += len;
                input_buffer_in_use = true;
            }
            else
            {
                lzs.next_in_buf = data;
                lzs.next_in = 0;
                lzs.avail_in = len;
                d.next_in_set = true;
                input_buffer_in_use = false;
            }

            byte[] result;
            try
            {
                result = DecompressBuf(d, max_length);
            }
            catch
            {
                d.next_in_set = false;
                throw;
            }

            if (d.eof)
            {
                d.needs_input = false;
                if (lzs.avail_in > 0)
                    d.unused_data = PyBytes.From(lzs.next_in_buf.AsSpan(lzs.next_in, (int)lzs.avail_in).ToArray());
                // Nothing can be decompressed after the end: lzma_end() now rather than at dealloc.
                LzmaCore.End(lzs);
            }
            else if (lzs.avail_in == 0)
            {
                d.next_in_set = false;

                if (lzs.avail_out == 0)
                {
                    // (avail_in==0 && avail_out==0) Maybe lzs's internal state still have a few bytes can be output, try to output them next time.
                    d.needs_input = false;
                }
                else
                {
                    // Input buffer exhausted, output buffer has space.
                    d.needs_input = true;
                }
            }
            else
            {
                d.needs_input = false;

                // If we did not use the input buffer, we now have to copy the tail from the caller's buffer into the input buffer
                if (!input_buffer_in_use)
                {
                    // Discard buffer if it's too small (resizing it may needlessly copy the current contents)
                    if (d.input_buffer != null && d.input_buffer.Length < lzs.avail_in)
                        d.input_buffer = null;

                    // Allocate if necessary
                    d.input_buffer ??= new byte[lzs.avail_in];

                    // Copy tail
                    Buffer.BlockCopy(lzs.next_in_buf, lzs.next_in, d.input_buffer, 0, (int)lzs.avail_in);
                    lzs.next_in_buf = d.input_buffer;
                    lzs.next_in = 0;
                }
            }

            return result;
        }

        /// <summary>_lzma_LZMADecompressor_decompress_impl</summary>
        private static PyObject DecompressorDecompress(Decompressor self, byte[] data, long max_length)
        {
            lock (self.Lock)
            {
                if (self.eof)
                    throw PyEOFError.Create("Already at end of stream");
                return PyBytes.From(Decompress(self, data, max_length));
            }
        }

        /// <summary>Decompressor_init_raw</summary>
        private static void DecompressorInitRaw(LzmaStream lzs, PyObject filterspecs)
        {
            var filters = ParseFilterChainSpec(filterspecs);
            var lzret = LzmaFilters.RawDecoder(lzs, filters);
            var error = CatchLzmaError(lzret);
            if (error != null)
                throw error;
        }

        /// <summary>_lzma_LZMADecompressor_impl</summary>
        private static PyObject DecompressorNew(PyType type, int format, PyObject memlimit, PyObject filters)
        {
            const uint decoder_flags = LzmaConst.TELL_ANY_CHECK | LzmaConst.TELL_NO_CHECK;
            ulong memlimit_ = ulong.MaxValue;
            LzmaRet lzret;

            if (memlimit is not PyNone)
            {
                if (format == FORMAT_RAW)
                    throw PyValueError.Create("Cannot specify memory limit with FORMAT_RAW");
                memlimit_ = AsUnsignedLongLong(memlimit);
            }

            if (format == FORMAT_RAW && filters is PyNone)
                throw PyValueError.Create("Must specify filters for FORMAT_RAW");
            else if (format != FORMAT_RAW && filters is not PyNone)
                throw PyValueError.Create("Cannot specify filters except with FORMAT_RAW");

            var self = new Decompressor
            {
                check = LZMA_CHECK_UNKNOWN,
                needs_input = true,
                input_buffer = null,
                unused_data = PyBytes.From(Array.Empty<byte>()),
            };

            switch (format)
            {
                case FORMAT_AUTO:
                    lzret = LzmaAutoDecoder.Create(self.lzs, memlimit_, decoder_flags);
                    break;

                case FORMAT_XZ:
                    lzret = LzmaStreamDecoder.Create(self.lzs, memlimit_, decoder_flags);
                    break;

                case FORMAT_ALONE:
                    self.check = LzmaConst.CHECK_NONE;
                    lzret = LzmaAloneDecoder.Create(self.lzs, memlimit_);
                    break;

                case FORMAT_RAW:
                    self.check = LzmaConst.CHECK_NONE;
                    DecompressorInitRaw(self.lzs, filters);
                    return self;

                default:
                    throw PyValueError.Create($"Invalid container format: {format}");
            }

            var error = CatchLzmaError(lzret);
            if (error != null)
                throw error;
            return self;
        }

        private static PyType MakeDecompressorType()
        {
            var type = new PyType("LZMADecompressor", new[] { PyType.ObjectType }, "_lzma");
            type.MarkSpecHeapType();
            // _lzma_LZMADecompressor__doc__: the signature line goes to __text_signature__.
            type.TypeDict["__doc__"] = PyString.From(
                "Create a decompressor object for decompressing data incrementally.\n" +
                "\n" +
                "  format\n" +
                "    Specifies the container format of the input stream.  If this is\n" +
                "    FORMAT_AUTO (the default), the decompressor will automatically detect\n" +
                "    whether the input is FORMAT_XZ or FORMAT_ALONE.  Streams created with\n" +
                "    FORMAT_RAW cannot be autodetected.\n" +
                "  memlimit\n" +
                "    Limit the amount of memory used by the decompressor.  This will cause\n" +
                "    decompression to fail if the input cannot be decompressed within the\n" +
                "    given limit.\n" +
                "  filters\n" +
                "    A custom filter chain.  This argument is required for FORMAT_RAW, and\n" +
                "    not accepted with any other format.  When provided, this should be a\n" +
                "    sequence of dicts, each indicating the ID and options for a single\n" +
                "    filter.\n" +
                "\n" +
                "For one-shot decompression, use the decompress() function instead.");
            // _lzma_LZMADecompressor (clinic): (format=FORMAT_AUTO, memlimit=None, filters=None)
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) =>
            {
                var parsed = PyArgs.Unpack("LZMADecompressor", args, kwargs, new[] { "format", "memlimit", "filters" }, 0, 0, 3);
                int format = parsed[0] == null ? FORMAT_AUTO : PyArgs.Int(parsed[0]);
                var memlimit = parsed[1] ?? PyNone.Instance;
                var filters = parsed[2] ?? PyNone.Instance;
                return DecompressorNew((PyType)cls, format, memlimit, filters);
            }, acceptsKwargs: true);
            // _lzma_LZMADecompressor_decompress: (data, max_length=-1)
            type.TypeDict["decompress"] = new PyMethodDescriptor("decompress", type, (o, args, kwargs) =>
            {
                var parsed = PyArgs.Unpack("decompress", args, kwargs, new[] { "data", "max_length" }, 0, 1, 2);
                var data = BytesMethods.RequireBuffer(parsed[0]);
                long max_length = parsed[1] == null ? -1 : PyArgs.Ssize(parsed[1]);
                return DecompressorDecompress((Decompressor)o, data, max_length);
            }, acceptsKwargs: true);
            // Decompressor_members
            type.TypeDict["check"] = new PyMemberDescriptor("check", type, o => PyInt.From(((Decompressor)o).check), readOnly: true);
            type.TypeDict["eof"] = new PyMemberDescriptor("eof", type, o => PyBool.FromBool(((Decompressor)o).eof), readOnly: true);
            type.TypeDict["needs_input"] = new PyMemberDescriptor("needs_input", type, o => PyBool.FromBool(((Decompressor)o).needs_input), readOnly: true);
            type.TypeDict["unused_data"] = new PyMemberDescriptor("unused_data", type, o => ((Decompressor)o).unused_data, readOnly: true);
            return type;
        }

        // ------------------------------------------------------------ Module-level functions

        /// <summary>_lzma_is_check_supported_impl</summary>
        private static PyObject IsCheckSupported(int check_id) => PyBool.FromBool(LzmaCheckState.IsSupported(check_id));

        /// <summary>_lzma__encode_filter_properties (and its _impl)</summary>
        private static PyObject EncodeFilterProperties(PyObject arg)
        {
            var filter = LzmaFilterConverter(arg);

            var lzret = LzmaFilters.PropertiesSize(out uint encoded_size, filter);
            var error = CatchLzmaError(lzret);
            if (error != null)
                throw error;

            var result = new byte[encoded_size];

            lzret = LzmaFilters.PropertiesEncode(filter, result, 0);
            error = CatchLzmaError(lzret);
            if (error != null)
                throw error;

            return PyBytes.From(result);
        }

        /// <summary>_lzma__decode_filter_properties_impl</summary>
        private static PyObject DecodeFilterProperties(ulong filter_id, byte[] encoded_props)
        {
            var filter = new LzmaFilter { id = filter_id };

            var lzret = LzmaFilters.PropertiesDecode(ref filter, encoded_props, 0, encoded_props.Length);
            var error = CatchLzmaError(lzret);
            if (error != null)
                throw error;

            return BuildFilterSpec(filter);
        }

        private static PyType MakeErrorType()
        {
            // PyErr_NewExceptionWithDoc("_lzma.LZMAError", "Call to liblzma failed.", NULL, NULL)
            var namespaceDict = new PyDict();
            namespaceDict.SetItem(PyString.From("__module__"), PyString.From("_lzma"));
            namespaceDict.SetItem(PyString.From("__doc__"), PyString.From("Call to liblzma failed."));
            return (PyType)PyType.TypeType.Call(
                new PyObject[] { PyString.From("LZMAError"), PyTuple.From(new PyObject[] { PyType.ExceptionType }), namespaceDict }, null);
        }

        /// <summary>lzma_exec</summary>
        internal static PyModule Create()
        {
            var module = new PyModule("_lzma");
            var d = module.ModuleDict;

            // lzma_methods
            d["is_check_supported"] = PyArgs.MethO("_lzma", "is_check_supported", args => IsCheckSupported(PyArgs.Int(args[0])));
            d["_encode_filter_properties"] = PyArgs.MethO("_lzma", "_encode_filter_properties", args => EncodeFilterProperties(args[0]));
            d["_decode_filter_properties"] = PyArgs.FastCall("_lzma", "_decode_filter_properties", 2, 2, args =>
            {
                ulong filter_id = LzmaVliConverter(args[0]);
                return DecodeFilterProperties(filter_id, BytesMethods.RequireBuffer(args[1]));
            });

            void AddInt(string name, BigInteger value) => d[name] = PyInt.From(value);

            AddInt("FORMAT_AUTO", FORMAT_AUTO);
            AddInt("FORMAT_XZ", FORMAT_XZ);
            AddInt("FORMAT_ALONE", FORMAT_ALONE);
            AddInt("FORMAT_RAW", FORMAT_RAW);
            AddInt("CHECK_NONE", LzmaConst.CHECK_NONE);
            AddInt("CHECK_CRC32", LzmaConst.CHECK_CRC32);
            AddInt("CHECK_CRC64", LzmaConst.CHECK_CRC64);
            AddInt("CHECK_SHA256", LzmaConst.CHECK_SHA256);
            AddInt("CHECK_ID_MAX", LzmaConst.CHECK_ID_MAX);
            AddInt("CHECK_UNKNOWN", LZMA_CHECK_UNKNOWN);
            AddInt("FILTER_LZMA1", LzmaConst.FILTER_LZMA1);
            AddInt("FILTER_LZMA2", LzmaConst.FILTER_LZMA2);
            AddInt("FILTER_DELTA", LzmaConst.FILTER_DELTA);
            AddInt("FILTER_X86", LzmaConst.FILTER_X86);
            AddInt("FILTER_IA64", LzmaConst.FILTER_IA64);
            AddInt("FILTER_ARM", LzmaConst.FILTER_ARM);
            AddInt("FILTER_ARMTHUMB", LzmaConst.FILTER_ARMTHUMB);
            AddInt("FILTER_SPARC", LzmaConst.FILTER_SPARC);
            AddInt("FILTER_POWERPC", LzmaConst.FILTER_POWERPC);
            AddInt("MF_HC3", (uint)LzmaMatchFinder.HC3);
            AddInt("MF_HC4", (uint)LzmaMatchFinder.HC4);
            AddInt("MF_BT2", (uint)LzmaMatchFinder.BT2);
            AddInt("MF_BT3", (uint)LzmaMatchFinder.BT3);
            AddInt("MF_BT4", (uint)LzmaMatchFinder.BT4);
            AddInt("MODE_FAST", (uint)LzmaMode.FAST);
            AddInt("MODE_NORMAL", (uint)LzmaMode.NORMAL);
            AddInt("PRESET_DEFAULT", LzmaConst.PRESET_DEFAULT);
            AddInt("PRESET_EXTREME", LzmaConst.PRESET_EXTREME);

            d["LZMAError"] = ErrorType;
            d["LZMACompressor"] = CompressorType;
            d["LZMADecompressor"] = DecompressorType;

            return module;
        }
    }
}
