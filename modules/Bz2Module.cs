using System;
using SharpPy.Modules.Bz2;

namespace SharpPy.Modules
{
    /// <summary>
    /// Modules/_bz2module.c over bzip2 1.0.8 in C# (modules/Bz2): BZ2Compressor and BZ2Decompressor,
    /// final immutable types made from their specs, with the C file's helpers one for one.
    /// </summary>
    internal static class Bz2Module
    {
        // UINT_MAX: bz_stream's avail_in/avail_out are unsigned int.
        private const long UINT_MAX = uint.MaxValue;

        private static readonly PyType CompressorType = MakeCompressorType();
        private static readonly PyType DecompressorType = MakeDecompressorType();

        /// <summary>BZ2Compressor</summary>
        private sealed class BZ2Compressor : PyObject
        {
            internal readonly BzStream bzs = new();
            internal bool flushed;
            internal readonly object Lock = new();

            public override PyType GetPyType() => CompressorType;
        }

        /// <summary>BZ2Decompressor</summary>
        private sealed class BZ2Decompressor : PyObject
        {
            internal readonly BzStream bzs = new();
            internal int bzerror;
            internal bool eof;
            internal PyObject unused_data;
            internal bool needs_input;
            // input_buffer: the unconsumed tail kept between calls; bzs.InBuf points into it while
            // next_in != NULL (InputKept)
            internal byte[] input_buffer;
            internal bool InputKept;
            // bzs->avail_in is only 32 bit, so the true length is stored separately.
            internal long bzs_avail_in_real;
            internal readonly object Lock = new();

            public override PyType GetPyType() => DecompressorType;
        }

        internal static PyModule Create()
        {
            // _bz2_exec
            var module = new PyModule("_bz2");
            module.ModuleDict["BZ2Compressor"] = CompressorType;
            module.ModuleDict["BZ2Decompressor"] = DecompressorType;
            return module;
        }

        /// <summary>catch_bz2_error: null when bzerror is not an error.</summary>
        private static Exception CatchBz2Error(int bzerror)
        {
            switch (bzerror)
            {
                case Bz.OK:
                case Bz.RUN_OK:
                case Bz.FLUSH_OK:
                case Bz.FINISH_OK:
                case Bz.STREAM_END:
                    return null;
                case Bz.CONFIG_ERROR:
                    return PySystemError.Create("libbzip2 was not compiled correctly");
                case Bz.PARAM_ERROR:
                    return PyValueError.Create("Internal error - invalid parameters passed to libbzip2");
                case Bz.MEM_ERROR:
                    return PyMemoryError.Create();
                case Bz.DATA_ERROR:
                case Bz.DATA_ERROR_MAGIC:
                    return PyOSError.Create("Invalid data stream");
                case Bz.IO_ERROR:
                    return PyOSError.Create("Unknown I/O error");
                case Bz.UNEXPECTED_EOF:
                    return PyEOFError.Create("Compressed file ended before the logical end-of-stream was detected");
                case Bz.SEQUENCE_ERROR:
                    return PyRuntimeError.Create("Internal error - Invalid sequence of commands sent to libbzip2");
                default:
                    return PyOSError.Create($"Unrecognized error from libbzip2: {bzerror}");
            }
        }

        // ---------------------------------------------------------------- BZ2Compressor

        /// <summary>compress</summary>
        private static byte[] Compress(BZ2Compressor c, byte[] data, long len, int action)
        {
            var buffer = new BlocksOutputBuffer();
            var bzs = c.bzs;
            bzs.AvailOut = (uint)buffer.InitAndGrow(-1, out bzs.OutBuf);
            bzs.NextOut = 0;
            bzs.InBuf = data;
            bzs.NextIn = 0;
            bzs.AvailIn = 0;

            while (true)
            {
                // Do compression in chunks of no more than UINT_MAX bytes each.
                if (bzs.AvailIn == 0 && len > 0)
                {
                    bzs.AvailIn = (uint)Math.Min(len, UINT_MAX);
                    len -= bzs.AvailIn;
                }

                // In regular compression mode, stop when input data is exhausted.
                if (action == Bz.RUN && bzs.AvailIn == 0)
                    break;

                if (bzs.AvailOut == 0)
                {
                    bzs.AvailOut = (uint)buffer.Grow(out bzs.OutBuf, bzs.AvailOut);
                    bzs.NextOut = 0;
                }

                int bzerror = BzEState.Compress(bzs, action);
                var error = CatchBz2Error(bzerror);
                if (error != null)
                    throw error;

                // In flushing mode, stop when all buffered data has been flushed.
                if (action == Bz.FINISH && bzerror == Bz.STREAM_END)
                    break;
            }

            return buffer.Finish(bzs.AvailOut);
        }

        /// <summary>_bz2_BZ2Compressor_compress_impl</summary>
        private static PyObject CompressorCompress(BZ2Compressor self, byte[] data)
        {
            lock (self.Lock)
            {
                if (self.flushed)
                    throw PyValueError.Create("Compressor has been flushed");
                return PyBytes.From(Compress(self, data, data.Length, Bz.RUN));
            }
        }

        /// <summary>_bz2_BZ2Compressor_flush_impl</summary>
        private static PyObject CompressorFlush(BZ2Compressor self)
        {
            lock (self.Lock)
            {
                if (self.flushed)
                    throw PyValueError.Create("Repeated call to flush()");
                self.flushed = true;
                var result = PyBytes.From(Compress(self, Array.Empty<byte>(), 0, Bz.FINISH));
                // The compressor cannot be used again: BZ2_bzCompressEnd() now rather than at dealloc,
                // so the buffers go back to the pool (CodecBufferPool).
                BzEState.CompressEnd(self.bzs);
                return result;
            }
        }

        /// <summary>_bz2_BZ2Compressor_impl</summary>
        private static PyObject CompressorNew(PyType type, int compresslevel)
        {
            if (!(1 <= compresslevel && compresslevel <= 9))
                throw PyValueError.Create("compresslevel must be between 1 and 9");
            var self = new BZ2Compressor();
            int bzerror = BzEState.CompressInit(self.bzs, compresslevel, 0, 0);
            var error = CatchBz2Error(bzerror);
            if (error != null)
                throw error;
            return self;
        }

        private static PyType MakeCompressorType()
        {
            var type = new PyType("BZ2Compressor", new[] { PyType.ObjectType }, "_bz2");
            type.MarkSpecHeapType();
            type.TypeDict["__doc__"] = PyString.From(
                "Create a compressor object for compressing data incrementally.\n\n" +
                "  compresslevel\n" +
                "    Compression level, as a number between 1 and 9.\n\n" +
                "For one-shot compression, use the compress() function instead.");
            // _bz2_BZ2Compressor (clinic): no keywords, at most one positional int
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) =>
            {
                PyArgs.NoKeywords("BZ2Compressor", kwargs);
                PyArgs.Positional("BZ2Compressor", args, null, 0, 1);
                int compresslevel = args.Length < 1 ? 9 : PyArgs.Int(args[0]);
                return CompressorNew((PyType)cls, compresslevel);
            }, acceptsKwargs: true);
            // _bz2_BZ2Compressor_compress: (data, /)
            PyArgs.Define(type, "compress", PyCall.One, (o, args) =>
                CompressorCompress((BZ2Compressor)o, BytesMethods.RequireBuffer(args[0])));
            // _bz2_BZ2Compressor_flush
            PyArgs.Define(type, "flush", PyCall.NoArgs, (o, args) => CompressorFlush((BZ2Compressor)o));
            return type;
        }

        // ---------------------------------------------------------------- BZ2Decompressor

        /// <summary>
        /// decompress_buf: decompress d->bzs_avail_in_real bytes at bzs.next_in, at most max_length
        /// of output; next_in and bzs_avail_in_real follow what was consumed.
        /// </summary>
        private static byte[] DecompressBuf(BZ2Decompressor d, long max_length)
        {
            var buffer = new BlocksOutputBuffer();
            var bzs = d.bzs;
            bzs.AvailOut = (uint)buffer.InitAndGrow(max_length, out bzs.OutBuf);
            bzs.NextOut = 0;

            while (true)
            {
                // Decompression in chunks of no more than UINT_MAX bytes each.
                bzs.AvailIn = (uint)Math.Min(d.bzs_avail_in_real, UINT_MAX);
                d.bzs_avail_in_real -= bzs.AvailIn;

                int bzret = BzDState.Decompress(bzs);

                d.bzs_avail_in_real += bzs.AvailIn;

                var error = CatchBz2Error(bzret);
                if (error != null)
                {
                    d.bzerror = bzret;
                    d.needs_input = false;
                    throw error;
                }
                if (bzret == Bz.STREAM_END)
                {
                    d.eof = true;
                    break;
                }
                else if (d.bzs_avail_in_real == 0)
                {
                    break;
                }
                else if (bzs.AvailOut == 0)
                {
                    if (buffer.GetDataSize(bzs.AvailOut) == max_length)
                        break;
                    bzs.AvailOut = (uint)buffer.Grow(out bzs.OutBuf, bzs.AvailOut);
                    bzs.NextOut = 0;
                }
            }

            return buffer.Finish(bzs.AvailOut);
        }

        /// <summary>decompress</summary>
        private static byte[] Decompress(BZ2Decompressor d, byte[] data, long max_length)
        {
            bool input_buffer_in_use;
            var bzs = d.bzs;
            long len = data.Length;

            // Prepend unconsumed input if necessary
            if (d.InputKept)
            {
                // Number of bytes we can append to input buffer
                long avail_now = d.input_buffer.Length - (bzs.NextIn + d.bzs_avail_in_real);
                // Number of bytes we can append if we move existing contents to beginning of buffer
                long avail_total = d.input_buffer.Length - d.bzs_avail_in_real;

                if (avail_total < len)
                {
                    long new_size = d.input_buffer.Length + len - avail_now;
                    var tmp = new byte[PyAllocation.ArrayLength(new_size)];
                    Buffer.BlockCopy(d.input_buffer, 0, tmp, 0, d.input_buffer.Length);
                    d.input_buffer = tmp;
                    bzs.InBuf = tmp;
                }
                else if (avail_now < len)
                {
                    Buffer.BlockCopy(d.input_buffer, bzs.NextIn, d.input_buffer, 0, (int)d.bzs_avail_in_real);
                    bzs.NextIn = 0;
                }
                Buffer.BlockCopy(data, 0, d.input_buffer, (int)(bzs.NextIn + d.bzs_avail_in_real), (int)len);
                d.bzs_avail_in_real += len;
                input_buffer_in_use = true;
            }
            else
            {
                bzs.InBuf = data;
                bzs.NextIn = 0;
                d.bzs_avail_in_real = len;
                input_buffer_in_use = false;
                d.InputKept = true;
            }

            byte[] result;
            try
            {
                result = DecompressBuf(d, max_length);
            }
            catch
            {
                d.InputKept = false;
                throw;
            }

            if (d.eof)
            {
                d.needs_input = false;
                if (d.bzs_avail_in_real > 0)
                    d.unused_data = PyBytes.From(bzs.InBuf.AsSpan(bzs.NextIn, (int)d.bzs_avail_in_real).ToArray());
                // Nothing can be decompressed after the end: BZ2_bzDecompressEnd() now rather than at
                // dealloc, so the buffer goes back to the pool (CodecBufferPool).
                BzDState.DecompressEnd(bzs);
            }
            else if (d.bzs_avail_in_real == 0)
            {
                d.InputKept = false;
                d.needs_input = true;
            }
            else
            {
                d.needs_input = false;

                // If we did not use the input buffer, we now have to copy the tail from the
                // caller's buffer into the input buffer
                if (!input_buffer_in_use)
                {
                    // Discard buffer if it's too small
                    if (d.input_buffer != null && d.input_buffer.Length < d.bzs_avail_in_real)
                        d.input_buffer = null;

                    // Allocate if necessary
                    d.input_buffer ??= new byte[d.bzs_avail_in_real];

                    // Copy tail
                    Buffer.BlockCopy(bzs.InBuf, bzs.NextIn, d.input_buffer, 0, (int)d.bzs_avail_in_real);
                    bzs.InBuf = d.input_buffer;
                    bzs.NextIn = 0;
                }
            }

            return result;
        }

        /// <summary>_bz2_BZ2Decompressor_decompress_impl</summary>
        private static PyObject DecompressorDecompress(BZ2Decompressor self, byte[] data, long max_length)
        {
            lock (self.Lock)
            {
                if (self.eof)
                    throw PyEOFError.Create("End of stream already reached");
                if (self.bzerror != 0)
                    // Re-entering BZ2_bzDecompress() after an error can write out of bounds.
                    throw PyValueError.Create("Decompressor is unusable after a previous error");
                return PyBytes.From(Decompress(self, data, max_length));
            }
        }

        /// <summary>_bz2_BZ2Decompressor_impl</summary>
        private static PyObject DecompressorNew(PyType type)
        {
            var self = new BZ2Decompressor
            {
                bzerror = 0,
                needs_input = true,
                bzs_avail_in_real = 0,
                input_buffer = null,
                unused_data = PyBytes.From(Array.Empty<byte>()),
            };
            int bzerror = BzDState.DecompressInit(self.bzs, 0, 0);
            var error = CatchBz2Error(bzerror);
            if (error != null)
                throw error;
            return self;
        }

        private static PyType MakeDecompressorType()
        {
            var type = new PyType("BZ2Decompressor", new[] { PyType.ObjectType }, "_bz2");
            type.MarkSpecHeapType();
            type.TypeDict["__doc__"] = PyString.From(
                "Create a decompressor object for decompressing data incrementally.\n\n" +
                "For one-shot decompression, use the decompress() function instead.");
            // _bz2_BZ2Decompressor (clinic): no positional arguments, no keywords
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) =>
            {
                if (args.Length > 0)
                    throw PyTypeError.Create("BZ2Decompressor() takes no positional arguments");
                PyArgs.NoKeywords("BZ2Decompressor", kwargs);
                return DecompressorNew((PyType)cls);
            }, acceptsKwargs: true);
            // _bz2_BZ2Decompressor_decompress: (data, max_length=-1)
            type.TypeDict["decompress"] = new PyMethodDescriptor("decompress", type, (o, args, kwargs) =>
            {
                var parsed = PyArgs.Unpack("decompress", args, kwargs, new[] { "data", "max_length" }, 0, 1, 2);
                var data = BytesMethods.RequireBuffer(parsed[0]);
                long max_length = parsed[1] == null ? -1 : PyArgs.Ssize(parsed[1]);
                return DecompressorDecompress((BZ2Decompressor)o, data, max_length);
            }, acceptsKwargs: true);
            // BZ2Decompressor_members
            type.TypeDict["eof"] = new PyMemberDescriptor("eof", type, o => PyBool.FromBool(((BZ2Decompressor)o).eof), readOnly: true);
            type.TypeDict["unused_data"] = new PyMemberDescriptor("unused_data", type, o => ((BZ2Decompressor)o).unused_data, readOnly: true);
            type.TypeDict["needs_input"] = new PyMemberDescriptor("needs_input", type, o => PyBool.FromBool(((BZ2Decompressor)o).needs_input), readOnly: true);
            return type;
        }
    }
}
