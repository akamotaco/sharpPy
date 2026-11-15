using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace SharpPy.Modules
{
    /// <summary>
    /// zlib's deflate (deflate.c) as the base library has it: deflateInit2's checks, the zlib and gzip
    /// containers (header, Adler-32 or CRC-32 and length) written as deflate.c writes them, and the
    /// raw deflate data from DeflateStream. What DeflateStream cannot do raises (docs/todo.md 5-21):
    /// a window other than 32K, a preset dictionary, and copying; memLevel and strategy are checked
    /// and then have no effect, and levels between those of CompressionLevel take the nearest. The
    /// compressed bytes are a valid stream; they need not be zlib's.
    /// </summary>
    internal sealed class ZDeflater
    {
        private const int INIT_STATE = 42, BUSY_STATE = 113, FINISH_STATE = 666;
        private int _status;
        private readonly int _level, _wrap, _strategy;
        private DeflateStream _stream;
        private MemoryStream _sink;
        // what the current DeflateStream has written; one that wrote nothing writes no last block
        private long _streamOutput;
        private readonly Queue<byte[]> _pending = new();
        private int _pendingStart;
        private uint _adler = 1, _crc;
        private uint _length;
        private int _lastFlush;

        private ZDeflater(int level, int wrap, int strategy)
        {
            _level = level;
            _wrap = wrap;
            _strategy = strategy;
            _status = INIT_STATE;
            _lastFlush = -2;
            NewStream();
        }

        private void NewStream()
        {
            _sink = new MemoryStream();
            _streamOutput = 0;
            _stream = new DeflateStream(_sink, _level == 0 ? CompressionLevel.NoCompression
                : _level <= 3 ? CompressionLevel.Fastest
                : _level == 9 ? CompressionLevel.SmallestSize : CompressionLevel.Optimal, leaveOpen: true);
        }

        /// <summary>deflateInit2: Z_STREAM_ERROR for what zlib refuses; NotImplementedError for what SharpPy cannot.</summary>
        internal static int Init2(ZStream strm, int level, int method, int windowBits, int memLevel, int strategy, out ZDeflater deflater)
        {
            deflater = null;
            strm.Msg = null;
            int wrap = 1;
            if (level == -1) level = 6;
            if (windowBits < 0)
            {
                wrap = 0;
                if (windowBits < -15)
                    return Z.STREAM_ERROR;
                windowBits = -windowBits;
            }
            else if (windowBits > 15)
            {
                wrap = 2;
                windowBits -= 16;
            }
            if (memLevel < 1 || memLevel > 9 || method != Z.DEFLATED || windowBits < 8 || windowBits > 15 ||
                level < 0 || level > 9 || strategy < 0 || strategy > 4 || (windowBits == 8 && wrap != 1))
                return Z.STREAM_ERROR;
            if (windowBits != 15)
                throw PyNotImplementedError.Create("SharpPy compresses with a 32K window only (wbits 15, -15 or 31): the compressor is .NET's DeflateStream");
            deflater = new ZDeflater(level, wrap, strategy);
            return Z.OK;
        }

        internal void End()
        {
            _stream?.Dispose();
            _stream = null;
        }

        private void Put(byte[] bytes)
        {
            if (bytes.Length > 0)
                _pending.Enqueue(bytes);
        }

        private void TakeSink()
        {
            if (_sink.Length > 0)
            {
                _streamOutput += _sink.Length;
                Put(_sink.ToArray());
                _sink.SetLength(0);
            }
        }

        // RANK: the flush values in order of strength, Z_BLOCK between Z_NO_FLUSH and Z_PARTIAL_FLUSH.
        private static int Rank(int f) => (f * 2) - (f > 4 ? 9 : 0);

        /// <summary>deflate(strm, flush).</summary>
        internal int Run(ZStream strm, int flush)
        {
            if (_stream == null && _status != FINISH_STATE || flush > Z.BLOCK || flush < 0)
                return Z.STREAM_ERROR;
            if (strm.OutBuf == null || (_status == FINISH_STATE && flush != Z.FINISH))
            {
                strm.Msg = "stream error";
                return Z.STREAM_ERROR;
            }
            if (strm.AvailOut == 0)
            {
                strm.Msg = "buffer error";
                return Z.BUF_ERROR;
            }
            int oldFlush = _lastFlush;
            _lastFlush = flush;
            if (_status == FINISH_STATE && strm.AvailIn != 0)
            {
                strm.Msg = "buffer error";
                return Z.BUF_ERROR;
            }
            if (_status == INIT_STATE)
            {
                Put(Header());
                _status = BUSY_STATE;
            }
            else if (_pending.Count == 0 && strm.AvailIn == 0 && Rank(flush) <= Rank(oldFlush) && flush != Z.FINISH)
            {
                strm.Msg = "buffer error";
                return Z.BUF_ERROR;
            }
            if (strm.AvailIn > 0 && _status != FINISH_STATE)
            {
                var input = strm.InBuf.AsSpan(strm.NextIn, strm.AvailIn);
                _stream.Write(input);
                if (_wrap == 1) _adler = Z.Adler32(_adler, input);
                else if (_wrap == 2) _crc = PyBinascii.Crc32(input, _crc);
                _length += (uint)input.Length;
                strm.NextIn += strm.AvailIn;
                strm.TotalIn += strm.AvailIn;
                strm.AvailIn = 0;
                TakeSink();
            }
            if (_status != FINISH_STATE)
            {
                if (flush == Z.FINISH)
                {
                    _stream.Dispose();
                    _stream = null;
                    TakeSink();
                    // deflate's last block when there is nothing more: an empty fixed block, 03 00.
                    if (_streamOutput == 0)
                        Put(new byte[] { 3, 0 });
                    Put(Trailer());
                    _status = FINISH_STATE;
                }
                else if (flush == Z.FULL_FLUSH)
                {
                    // a sync point the next data does not reach back past: a fresh deflater, the old
                    // one's last block left unwritten
                    _stream.Flush();
                    TakeSink();
                    _stream.Dispose();
                    NewStream();
                }
                else if (flush != Z.NO_FLUSH)
                {
                    _stream.Flush();
                    TakeSink();
                }
            }
            // flush_pending
            while (_pending.Count > 0 && strm.AvailOut > 0)
            {
                var block = _pending.Peek();
                int n = Math.Min(block.Length - _pendingStart, strm.AvailOut);
                Array.Copy(block, _pendingStart, strm.OutBuf, strm.NextOut, n);
                strm.NextOut += n;
                strm.AvailOut -= n;
                strm.TotalOut += n;
                _pendingStart += n;
                if (_pendingStart == block.Length)
                {
                    _pending.Dequeue();
                    _pendingStart = 0;
                }
            }
            if (_pending.Count > 0)
                return Z.OK;
            return _status == FINISH_STATE ? Z.STREAM_END : Z.OK;
        }

        // deflate's header: zlib's CMF and FLG with the level flags, or gzip's ten bytes with no name.
        private byte[] Header()
        {
            if (_wrap == 2)
            {
                byte xfl = (byte)(_level == 9 ? 2 : _strategy >= 2 || _level < 2 ? 4 : 0);
                byte os = (byte)(OperatingSystem.IsWindows() ? 10 : OperatingSystem.IsMacOS() || OperatingSystem.IsIOS() ? 19 : 3);
                return new byte[] { 31, 139, 8, 0, 0, 0, 0, 0, xfl, os };
            }
            if (_wrap == 0)
                return Array.Empty<byte>();
            int header = (Z.DEFLATED + ((15 - 8) << 4)) << 8;
            int levelFlags = _strategy >= 2 || _level < 2 ? 0 : _level < 6 ? 1 : _level == 6 ? 2 : 3;
            header |= levelFlags << 6;
            header += 31 - (header % 31);
            return new[] { (byte)(header >> 8), (byte)header };
        }

        private byte[] Trailer() => _wrap switch
        {
            1 => new[] { (byte)(_adler >> 24), (byte)(_adler >> 16), (byte)(_adler >> 8), (byte)_adler },
            2 => new[] { (byte)_crc, (byte)(_crc >> 8), (byte)(_crc >> 16), (byte)(_crc >> 24),
                         (byte)_length, (byte)(_length >> 8), (byte)(_length >> 16), (byte)(_length >> 24) },
            _ => Array.Empty<byte>(),
        };
    }

    /// <summary>
    /// _BlocksOutputBuffer (Include/internal/pycore_blocks_output_buffer.h): the output of one call
    /// in blocks of growing size, none past max_length, each handed to the z_stream in turn.
    /// </summary>
    internal sealed class ZOutput
    {
        private static readonly int[] BlockSize =
        {
            32 * 1024, 64 * 1024, 256 * 1024, 1024 * 1024, 4 << 20, 8 << 20, 16 << 20, 16 << 20,
            32 << 20, 32 << 20, 32 << 20, 32 << 20, 64 << 20, 64 << 20, 128 << 20, 128 << 20, 256 << 20,
        };

        private readonly List<byte[]> _blocks = new();
        private long _allocated, _maxLength = -1;
        private readonly ZStream _z;

        internal ZOutput(ZStream z) => _z = z;

        private void Hand(byte[] block)
        {
            _blocks.Add(block);
            _allocated += block.Length;
            _z.OutBuf = block;
            _z.NextOut = 0;
            _z.AvailOut = block.Length;
        }

        internal void InitAndGrow(long maxLength)
        {
            _maxLength = maxLength;
            int size = BlockSize[0];
            if (maxLength >= 0 && maxLength < size)
                size = (int)maxLength;
            Hand(new byte[size]);
        }

        internal void InitWithSize(long size)
        {
            if (size > Array.MaxLength)
                throw PyMemoryError.Create();
            Hand(new byte[size]);
        }

        internal void Grow()
        {
            long size = _blocks.Count < BlockSize.Length ? BlockSize[_blocks.Count] : BlockSize[^1];
            if (_maxLength >= 0)
                size = Math.Min(size, _maxLength - _allocated);
            Hand(new byte[size]);
        }

        internal long DataSize => _allocated - _z.AvailOut;

        internal byte[] Finish()
        {
            var result = new byte[DataSize];
            int at = 0;
            for (int i = 0; i < _blocks.Count; i++)
            {
                int n = i == _blocks.Count - 1 ? _blocks[i].Length - _z.AvailOut : _blocks[i].Length;
                Array.Copy(_blocks[i], 0, result, at, n);
                at += n;
            }
            return result;
        }
    }

    /// <summary>
    /// Modules/zlibmodule.c over zlib's inflate in C# (ZInflater, a port of zlib 1.3.1) and the base
    /// library's deflate (ZDeflater): the functions, Compress, Decompress and _ZlibDecompressor.
    /// </summary>
    internal static class ZlibModule
    {
        private const int MAX_WBITS = 15, DEF_MEM_LEVEL = 8, DEF_BUF_SIZE = 16 * 1024, DEF_MAX_INITIAL_BUF_SIZE = 16 * 1024 * 1024;
        private const string Version = "1.3.1";

        private static readonly PyType ErrorType = PyExceptionSlots.NewException("zlib", "error", PyType.ExceptionType);
        private static readonly PyType CompType = MakeCompType();
        private static readonly PyType DecompType = MakeDecompType();
        private static readonly PyType DecompressorType = MakeDecompressorType();

        // zlib_error
        private static Exception Error(ZStream zst, int err, string msg)
        {
            string zmsg = err == Z.VERSION_ERROR ? "library version mismatch" : zst.Msg;
            zmsg ??= err switch
            {
                Z.BUF_ERROR => "incomplete or truncated stream",
                Z.STREAM_ERROR => "inconsistent stream state",
                Z.DATA_ERROR => "invalid input data",
                _ => null,
            };
            return PyExceptionSlots.Raise(ErrorType, zmsg == null ? $"Error {err} {msg}" : $"Error {err} {msg}: {Truncate(zmsg)}");
        }

        private static string Truncate(string text) => text.Length > 200 ? text.Substring(0, 200) : text;

        private static byte[] Buffer(PyObject value) => BytesMethods.RequireBuffer(value);

        private static PyObject Int(int value) => PyInt.From(value);

        internal static PyModule CreateZlibModule()
        {
            var module = new PyModule("zlib");
            var d = module.ModuleDict;
            d["adler32"] = PyArgs.FastCall("zlib", "adler32", 1, 2, args =>
            {
                var data = Buffer(args[0]);
                uint value = args.Length > 1 ? PyArgs.UnsignedIntMask(args[1]) : 1;
                return PyInt.From((long)Z.Adler32(value, data));
            });
            d["compress"] = new PyBuiltinFunction("compress", (args, kwargs) =>
            {
                var parsed = PyArgs.Unpack("compress", args, kwargs, new[] { "", "level", "wbits" }, 1, 1, 3);
                var data = Buffer(parsed[0]);
                int level = parsed[1] == null ? -1 : PyArgs.Int(parsed[1]);
                int wbits = parsed[2] == null ? MAX_WBITS : PyArgs.Int(parsed[2]);
                return Compress(data, level, wbits);
            });
            d["compressobj"] = new PyBuiltinFunction("compressobj", (args, kwargs) =>
            {
                var parsed = PyArgs.Unpack("compressobj", args, kwargs, new[] { "level", "method", "wbits", "memLevel", "strategy", "zdict" }, 0, 0, 6);
                int level = parsed[0] == null ? -1 : PyArgs.Int(parsed[0]);
                int method = parsed[1] == null ? Z.DEFLATED : PyArgs.Int(parsed[1]);
                int wbits = parsed[2] == null ? MAX_WBITS : PyArgs.Int(parsed[2]);
                int memLevel = parsed[3] == null ? DEF_MEM_LEVEL : PyArgs.Int(parsed[3]);
                int strategy = parsed[4] == null ? 0 : PyArgs.Int(parsed[4]);
                byte[] zdict = parsed[5] == null || parsed[5] is PyNone ? null : Buffer(parsed[5]);
                return CompressObj(level, method, wbits, memLevel, strategy, zdict);
            });
            d["crc32"] = PyArgs.FastCall("zlib", "crc32", 1, 2, args =>
            {
                var data = Buffer(args[0]);
                return PyInt.From((long)PyBinascii.Crc32(data, args.Length > 1 ? PyArgs.UnsignedIntMask(args[1]) : 0));
            });
            d["decompress"] = new PyBuiltinFunction("decompress", (args, kwargs) =>
            {
                var parsed = PyArgs.Unpack("decompress", args, kwargs, new[] { "", "wbits", "bufsize" }, 1, 1, 3);
                var data = Buffer(parsed[0]);
                int wbits = parsed[1] == null ? MAX_WBITS : PyArgs.Int(parsed[1]);
                long bufsize = parsed[2] == null ? DEF_BUF_SIZE : PyArgs.Ssize(parsed[2]);
                return Decompress(data, wbits, bufsize);
            });
            d["decompressobj"] = new PyBuiltinFunction("decompressobj", (args, kwargs) =>
            {
                var parsed = PyArgs.Unpack("decompressobj", args, kwargs, new[] { "wbits", "zdict" }, 0, 0, 2);
                int wbits = parsed[0] == null ? MAX_WBITS : PyArgs.Int(parsed[0]);
                return DecompressObj(wbits, parsed[1] ?? PyBytes.From(Array.Empty<byte>()));
            });
            d["error"] = ErrorType;
            d["_ZlibDecompressor"] = DecompressorType;
            d["MAX_WBITS"] = Int(MAX_WBITS);
            d["DEFLATED"] = Int(Z.DEFLATED);
            d["DEF_MEM_LEVEL"] = Int(DEF_MEM_LEVEL);
            d["DEF_BUF_SIZE"] = Int(DEF_BUF_SIZE);
            d["Z_NO_COMPRESSION"] = Int(0);
            d["Z_BEST_SPEED"] = Int(1);
            d["Z_BEST_COMPRESSION"] = Int(9);
            d["Z_DEFAULT_COMPRESSION"] = Int(-1);
            d["Z_FILTERED"] = Int(1);
            d["Z_HUFFMAN_ONLY"] = Int(2);
            d["Z_RLE"] = Int(3);
            d["Z_FIXED"] = Int(4);
            d["Z_DEFAULT_STRATEGY"] = Int(0);
            d["Z_NO_FLUSH"] = Int(Z.NO_FLUSH);
            d["Z_PARTIAL_FLUSH"] = Int(Z.PARTIAL_FLUSH);
            d["Z_SYNC_FLUSH"] = Int(Z.SYNC_FLUSH);
            d["Z_FULL_FLUSH"] = Int(Z.FULL_FLUSH);
            d["Z_FINISH"] = Int(Z.FINISH);
            d["Z_BLOCK"] = Int(Z.BLOCK);
            d["Z_TREES"] = Int(Z.TREES);
            // The inflate is a port of zlib 1.3.1's.
            d["ZLIB_VERSION"] = PyString.From(Version);
            d["ZLIB_RUNTIME_VERSION"] = PyString.From(Version);
            d["__version__"] = PyString.From("1.0");
            return module;
        }

        // zlib_compress_impl
        private static PyObject Compress(byte[] data, int level, int wbits)
        {
            var zst = new ZStream();
            var buffer = new ZOutput(zst);
            buffer.InitAndGrow(-1);
            zst.InBuf = data;
            zst.NextIn = 0;
            int err = ZDeflater.Init2(zst, level, Z.DEFLATED, wbits, DEF_MEM_LEVEL, 0, out var deflater);
            if (err == Z.STREAM_ERROR)
                throw PyExceptionSlots.Raise(ErrorType, "Bad compression level");
            int flush;
            long ibuflen = data.Length;
            do
            {
                zst.AvailIn = (int)ibuflen;
                ibuflen = 0;
                flush = Z.FINISH;
                do
                {
                    if (zst.AvailOut == 0)
                        buffer.Grow();
                    err = deflater.Run(zst, flush);
                    if (err == Z.STREAM_ERROR)
                    {
                        deflater.End();
                        throw Error(zst, err, "while compressing data");
                    }
                } while (zst.AvailOut == 0);
            } while (flush != Z.FINISH);
            deflater.End();
            return PyBytes.From(buffer.Finish());
        }

        /// <summary>zlib.decompress(data): the module's own inflate, for SharpPy's compressed tables.</summary>
        internal static byte[] Inflate(byte[] data) => ((PyBytes)Decompress(data, MAX_WBITS, DEF_BUF_SIZE)).Value;

        // zlib_decompress_impl
        private static PyObject Decompress(byte[] data, int wbits, long bufsize)
        {
            if (bufsize < 0)
                throw PyValueError.Create("bufsize must be non-negative");
            if (bufsize == 0)
                bufsize = 1;
            var zst = new ZStream();
            var buffer = new ZOutput(zst);
            buffer.InitWithSize(bufsize);
            zst.InBuf = data;
            zst.AvailIn = 0;
            zst.NextIn = 0;
            int err = ZInflater.Init2(zst, wbits);
            if (err != Z.OK)
            {
                ZInflater.End(zst);
                throw Error(zst, err, "while preparing to decompress data");
            }
            long ibuflen = data.Length;
            do
            {
                zst.AvailIn = (int)ibuflen;
                ibuflen = 0;
                int flush = Z.FINISH;
                do
                {
                    if (zst.AvailOut == 0)
                        buffer.Grow();
                    err = ZInflater.Run(zst, flush);
                    switch (err)
                    {
                        case Z.OK:
                        case Z.BUF_ERROR:
                        case Z.STREAM_END:
                            break;
                        case Z.MEM_ERROR:
                            ZInflater.End(zst);
                            throw PyMemoryError.Create("Out of memory while decompressing data");
                        default:
                            ZInflater.End(zst);
                            throw Error(zst, err, "while decompressing data");
                    }
                } while (zst.AvailOut == 0);
            } while (err != Z.STREAM_END && ibuflen != 0);
            if (err != Z.STREAM_END)
            {
                ZInflater.End(zst);
                throw Error(zst, err, "while decompressing data");
            }
            ZInflater.End(zst);
            return PyBytes.From(buffer.Finish());
        }

        // compobject: a Compress or a Decompress.
        private sealed class CompObject : PyObject
        {
            internal readonly PyType Type;
            internal ZStream Zst = new();
            internal ZDeflater Deflater;
            internal PyObject UnusedData = PyBytes.From(Array.Empty<byte>());
            internal PyObject UnconsumedTail = PyBytes.From(Array.Empty<byte>());
            internal bool Eof, IsInitialised;
            internal PyObject Zdict;
            internal readonly object Lock = new();

            internal CompObject(PyType type) => Type = type;
            public override PyType GetPyType() => Type;
        }

        // zlib_compressobj_impl
        private static PyObject CompressObj(int level, int method, int wbits, int memLevel, int strategy, byte[] zdict)
        {
            var self = new CompObject(CompType);
            int err = ZDeflater.Init2(self.Zst, level, method, wbits, memLevel, strategy, out self.Deflater);
            if (err == Z.STREAM_ERROR)
                throw PyValueError.Create("Invalid initialization option");
            self.IsInitialised = true;
            if (zdict != null)
                throw PyNotImplementedError.Create("SharpPy cannot compress with a preset dictionary (zdict): the compressor is .NET's DeflateStream");
            return self;
        }

        // set_inflate_zdict
        private static void SetInflateZdict(ZStream zst, PyObject zdict)
        {
            var dictionary = Buffer(zdict);
            int err = ZInflater.SetDictionary(zst, dictionary);
            if (err != Z.OK)
                throw Error(zst, err, "while setting zdict");
        }

        // zlib_decompressobj_impl
        private static PyObject DecompressObj(int wbits, PyObject zdict)
        {
            if (zdict != null && BytesMethods.Buffer(zdict) == null)
                throw PyTypeError.Create("zdict argument must support the buffer protocol");
            var self = new CompObject(DecompType) { Zdict = zdict };
            int err = ZInflater.Init2(self.Zst, wbits);
            if (err == Z.STREAM_ERROR)
                throw PyValueError.Create("Invalid initialization option");
            if (err != Z.OK)
                throw Error(self.Zst, err, "while creating decompression object");
            self.IsInitialised = true;
            if (self.Zdict != null && wbits < 0)
                SetInflateZdict(self.Zst, self.Zdict);
            return self;
        }

        private static CompObject Self(PyObject self) => (CompObject)self;

        private static PyType MakeCompType()
        {
            var type = new PyType("Compress", new[] { PyType.ObjectType }, "zlib");
            type.MarkSpecHeapType();
            // zlib_Compress_compress_impl
            PyArgs.Define(type, "compress", PyCall.Defining, (o, args) =>
            {
                var self = Self(o);
                var data = Buffer(args[0]);
                lock (self.Lock)
                {
                    var zst = self.Zst;
                    zst.InBuf = data;
                    zst.NextIn = 0;
                    var buffer = new ZOutput(zst);
                    buffer.InitAndGrow(-1);
                    zst.AvailIn = data.Length;
                    do
                    {
                        if (zst.AvailOut == 0)
                            buffer.Grow();
                        int err = self.Deflater == null || !self.IsInitialised ? Z.STREAM_ERROR : self.Deflater.Run(zst, Z.NO_FLUSH);
                        if (err == Z.STREAM_ERROR)
                            throw Error(zst, err, "while compressing data");
                    } while (zst.AvailOut == 0);
                    return PyBytes.From(buffer.Finish());
                }
            }, 1, 1);
            // zlib_Compress_flush_impl
            PyArgs.Define(type, "flush", PyCall.Defining, (o, args) =>
            {
                var self = Self(o);
                int mode = args.Length > 0 ? PyArgs.Int(args[0]) : Z.FINISH;
                if (mode == Z.NO_FLUSH)
                    return PyBytes.From(Array.Empty<byte>());
                lock (self.Lock)
                {
                    var zst = self.Zst;
                    zst.AvailIn = 0;
                    var buffer = new ZOutput(zst);
                    buffer.InitAndGrow(-1);
                    int err;
                    do
                    {
                        if (zst.AvailOut == 0)
                            buffer.Grow();
                        err = !self.IsInitialised ? Z.STREAM_ERROR : self.Deflater.Run(zst, mode);
                        if (err == Z.STREAM_ERROR)
                            throw Error(zst, err, "while flushing");
                    } while (zst.AvailOut == 0);
                    if (err == Z.STREAM_END && mode == Z.FINISH)
                    {
                        self.Deflater.End();
                        self.IsInitialised = false;
                    }
                    else if (err != Z.OK && err != Z.BUF_ERROR)
                        throw Error(zst, err, "while flushing");
                    return PyBytes.From(buffer.Finish());
                }
            }, 0, 1);
            PyObject CopyComp(PyObject o) =>
                throw PyNotImplementedError.Create("SharpPy cannot copy a compression object: the compressor is .NET's DeflateStream");
            PyArgs.Define(type, "copy", PyCall.Defining, (o, args) => CopyComp(o), 0, 0);
            PyArgs.Define(type, "__copy__", PyCall.Defining, (o, args) => CopyComp(o), 0, 0);
            PyArgs.Define(type, "__deepcopy__", PyCall.Defining, (o, args) => CopyComp(o), 1, 1);
            return type;
        }

        // save_unconsumed_input
        private static void SaveUnconsumedInput(CompObject self, byte[] data, int err)
        {
            var zst = self.Zst;
            if (err == Z.STREAM_END && zst.AvailIn > 0)
            {
                var old = ((PyBytes)self.UnusedData).Value;
                int leftSize = data.Length - zst.NextIn;
                var joined = new byte[old.Length + leftSize];
                old.CopyTo(joined, 0);
                Array.Copy(data, zst.NextIn, joined, old.Length, leftSize);
                self.UnusedData = PyBytes.From(joined);
                zst.AvailIn = 0;
            }
            if (zst.AvailIn > 0 || ((PyBytes)self.UnconsumedTail).Value.Length > 0)
            {
                int leftSize = data.Length - zst.NextIn;
                self.UnconsumedTail = PyBytes.From(data.AsSpan(zst.NextIn, leftSize).ToArray());
            }
        }

        private static PyType MakeDecompType()
        {
            var type = new PyType("Decompress", new[] { PyType.ObjectType }, "zlib");
            type.MarkSpecHeapType();
            // zlib_Decompress_decompress_impl: (data, /, max_length=0)
            type.TypeDict["decompress"] = new PyMethodDescriptor("decompress", type, (o, args, kwargs) =>
            {
                var parsed = PyArgs.Unpack("decompress", args, kwargs, new[] { "", "max_length" }, 1, 1, 2);
                var self = Self(o);
                var data = Buffer(parsed[0]);
                long maxLength = parsed[1] == null ? 0 : PyArgs.Ssize(parsed[1]);
                if (maxLength < 0)
                    throw PyValueError.Create("max_length must be non-negative");
                if (maxLength == 0)
                    maxLength = -1;
                lock (self.Lock)
                {
                    var zst = self.Zst;
                    int err = Z.OK;
                    zst.InBuf = data;
                    zst.NextIn = 0;
                    long ibuflen = data.Length;
                    var buffer = new ZOutput(zst);
                    buffer.InitAndGrow(maxLength);
                    do
                    {
                        zst.AvailIn = (int)ibuflen;
                        ibuflen = 0;
                        do
                        {
                            if (zst.AvailOut == 0)
                            {
                                if (buffer.DataSize == maxLength)
                                    goto save;
                                buffer.Grow();
                            }
                            err = ZInflater.Run(zst, Z.SYNC_FLUSH);
                            switch (err)
                            {
                                case Z.OK:
                                case Z.BUF_ERROR:
                                case Z.STREAM_END:
                                    break;
                                default:
                                    if (err == Z.NEED_DICT && self.Zdict != null)
                                    {
                                        SetInflateZdict(zst, self.Zdict);
                                        break;
                                    }
                                    goto save;
                            }
                        } while (zst.AvailOut == 0 || err == Z.NEED_DICT);
                    } while (err != Z.STREAM_END && ibuflen != 0);
                save:
                    SaveUnconsumedInput(self, data, err);
                    if (err == Z.STREAM_END)
                        self.Eof = true;
                    else if (err != Z.OK && err != Z.BUF_ERROR)
                        throw Error(zst, err, "while decompressing data");
                    return PyBytes.From(buffer.Finish());
                }
            }, acceptsKwargs: true);
            // zlib_Decompress_flush_impl: (length=DEF_BUF_SIZE, /)
            PyArgs.Define(type, "flush", PyCall.Defining, (o, args) =>
            {
                var self = Self(o);
                long length = args.Length > 0 ? PyArgs.Ssize(args[0]) : DEF_BUF_SIZE;
                if (length <= 0)
                    throw PyValueError.Create("length must be greater than zero");
                lock (self.Lock)
                {
                    var zst = self.Zst;
                    var data = Buffer(self.UnconsumedTail);
                    zst.InBuf = data;
                    zst.NextIn = 0;
                    long ibuflen = data.Length;
                    var buffer = new ZOutput(zst);
                    buffer.InitWithSize(length);
                    int err;
                    do
                    {
                        zst.AvailIn = (int)ibuflen;
                        ibuflen = 0;
                        int flush = Z.FINISH;
                        do
                        {
                            if (zst.AvailOut == 0)
                                buffer.Grow();
                            err = ZInflater.Run(zst, flush);
                            switch (err)
                            {
                                case Z.OK:
                                case Z.BUF_ERROR:
                                case Z.STREAM_END:
                                    break;
                                default:
                                    goto save;
                            }
                        } while (zst.AvailOut == 0 || err == Z.NEED_DICT);
                    } while (err != Z.STREAM_END && ibuflen != 0);
                save:
                    SaveUnconsumedInput(self, data, err);
                    if (err == Z.STREAM_END)
                    {
                        self.Eof = true;
                        self.IsInitialised = false;
                        ZInflater.End(zst);
                    }
                    return PyBytes.From(buffer.Finish());
                }
            }, 0, 1);
            // zlib_Decompress_copy_impl
            PyObject CopyDecomp(PyObject o)
            {
                var self = Self(o);
                var copy = new CompObject(DecompType);
                lock (self.Lock)
                {
                    if (ZInflater.Copy(copy.Zst, self.Zst) != Z.OK)
                        throw PyValueError.Create("Inconsistent stream state");
                    copy.UnusedData = self.UnusedData;
                    copy.UnconsumedTail = self.UnconsumedTail;
                    copy.Zdict = self.Zdict;
                    copy.Eof = self.Eof;
                    copy.IsInitialised = true;
                }
                return copy;
            }
            PyArgs.Define(type, "copy", PyCall.Defining, (o, args) => CopyDecomp(o), 0, 0);
            PyArgs.Define(type, "__copy__", PyCall.Defining, (o, args) => CopyDecomp(o), 0, 0);
            PyArgs.Define(type, "__deepcopy__", PyCall.Defining, (o, args) => CopyDecomp(o), 1, 1);
            type.TypeDict["unused_data"] = new PyMemberDescriptor("unused_data", type, o => Self(o).UnusedData, readOnly: true);
            type.TypeDict["unconsumed_tail"] = new PyMemberDescriptor("unconsumed_tail", type, o => Self(o).UnconsumedTail, readOnly: true);
            type.TypeDict["eof"] = new PyMemberDescriptor("eof", type, o => PyBool.FromBool(Self(o).Eof), readOnly: true);
            return type;
        }

        // ZlibDecompressor
        private sealed class Decompressor : PyObject
        {
            internal ZStream Zst = new();
            internal PyObject Zdict;
            internal readonly object Lock = new();
            internal PyObject UnusedData = PyBytes.From(Array.Empty<byte>());
            // zst.next_in != NULL: input not yet consumed, kept from the last call
            internal byte[] Input;
            internal long AvailInReal;
            internal bool IsInitialised, Eof, NeedsInput = true;

            public override PyType GetPyType() => DecompressorType;
        }

        // decompress_buf
        private static byte[] DecompressBuf(Decompressor self, long maxLength)
        {
            var zst = self.Zst;
            long hardLimit, obuflen;
            int err = Z.OK;
            if (maxLength < 0 || maxLength == long.MaxValue)
            {
                hardLimit = long.MaxValue;
                obuflen = DEF_BUF_SIZE;
            }
            else
            {
                hardLimit = maxLength;
                obuflen = Math.Min(maxLength, DEF_MAX_INITIAL_BUF_SIZE);
            }
            byte[] result = null;
            do
            {
                zst.AvailIn = (int)Math.Min(self.AvailInReal, int.MaxValue);
                self.AvailInReal -= zst.AvailIn;
                do
                {
                    // arrange_output_buffer_with_maximum
                    if (result == null)
                    {
                        result = new byte[obuflen];
                        zst.OutBuf = result;
                        zst.NextOut = 0;
                    }
                    else if (zst.NextOut == result.Length)
                    {
                        if (result.Length == hardLimit)
                            break;
                        long newLength = result.Length <= (hardLimit >> 1) ? (long)result.Length << 1 : hardLimit;
                        if (newLength > Array.MaxLength)
                            throw PyMemoryError.Create("Insufficient memory for buffer allocation");
                        Array.Resize(ref result, (int)newLength);
                        zst.OutBuf = result;
                    }
                    zst.AvailOut = result.Length - zst.NextOut;
                    err = ZInflater.Run(zst, Z.SYNC_FLUSH);
                    if (err == Z.NEED_DICT)
                        throw PySystemError.Create("error return without exception set");
                } while (zst.AvailOut == 0);
            } while (err != Z.STREAM_END && self.AvailInReal != 0);
            if (err == Z.STREAM_END)
            {
                self.Eof = true;
                self.IsInitialised = false;
                ZInflater.End(zst);
            }
            else if (err != Z.OK && err != Z.BUF_ERROR)
                throw Error(zst, err, "while decompressing data");
            self.AvailInReal += zst.AvailIn;
            Array.Resize(ref result, zst.NextOut);
            return result;
        }

        // decompress: what is left of the input before the new data, then decompress_buf.
        private static byte[] DecompressorRun(Decompressor self, byte[] data, long maxLength)
        {
            var zst = self.Zst;
            if (self.Input != null)
            {
                var joined = new byte[self.AvailInReal + data.Length];
                Array.Copy(self.Input, zst.NextIn, joined, 0, self.AvailInReal);
                data.CopyTo(joined, self.AvailInReal);
                zst.InBuf = joined;
                zst.NextIn = 0;
                self.AvailInReal = joined.Length;
            }
            else
            {
                zst.InBuf = data;
                zst.NextIn = 0;
                self.AvailInReal = data.Length;
            }
            byte[] result;
            try
            {
                result = DecompressBuf(self, maxLength);
            }
            catch
            {
                self.Input = null;
                throw;
            }
            if (self.Eof)
            {
                self.NeedsInput = false;
                if (self.AvailInReal > 0)
                    self.UnusedData = PyBytes.From(zst.InBuf.AsSpan(zst.NextIn, (int)self.AvailInReal).ToArray());
            }
            else if (self.AvailInReal == 0)
            {
                self.Input = null;
                self.NeedsInput = true;
            }
            else
            {
                self.NeedsInput = false;
                self.Input = zst.InBuf;
            }
            return result;
        }

        private static PyType MakeDecompressorType()
        {
            var type = new PyType("_ZlibDecompressor", new[] { PyType.ObjectType }, "zlib");
            type.MarkSpecHeapType();
            // tp_doc: the signature line goes to __text_signature__, the rest is __doc__.
            type.TypeDict["__doc__"] = PyString.From(
                "Create a decompressor object for decompressing data incrementally.\n\n" +
                "  wbits = 15\n" +
                "  zdict\n" +
                "     The predefined compression dictionary. This is a sequence of bytes\n" +
                "     (such as a bytes object) containing subsequences that are expected\n" +
                "     to occur frequently in the data that is to be compressed. Those\n" +
                "     subsequences that are expected to be most common should come at the\n" +
                "     end of the dictionary. This must be the same dictionary as used by the\n" +
                "     compressor that produced the input data.\n\n");
            // ZlibDecompressor__new__: "|iO:_ZlibDecompressor", wbits and zdict
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) =>
            {
                var parsed = PyArgs.Unpack("_ZlibDecompressor", args, kwargs, new[] { "wbits", "zdict" }, 0, 0, 2);
                int wbits = parsed[0] == null ? MAX_WBITS : PyArgs.Int(parsed[0]);
                var self = new Decompressor { Zdict = parsed[1] };
                int err = ZInflater.Init2(self.Zst, wbits);
                if (err == Z.STREAM_ERROR)
                    throw PyValueError.Create("Invalid initialization option");
                if (err != Z.OK)
                    throw Error(self.Zst, err, "while creating decompression object");
                self.IsInitialised = true;
                if (self.Zdict != null && wbits < 0)
                    SetInflateZdict(self.Zst, self.Zdict);
                return self;
            }, acceptsKwargs: true);
            // zlib_ZlibDecompressor_decompress_impl: (data, max_length=-1)
            type.TypeDict["decompress"] = new PyMethodDescriptor("decompress", type, (o, args, kwargs) =>
            {
                var parsed = PyArgs.Unpack("decompress", args, kwargs, new[] { "data", "max_length" }, 0, 1, 2);
                var self = (Decompressor)o;
                var data = Buffer(parsed[0]);
                long maxLength = parsed[1] == null ? -1 : PyArgs.Ssize(parsed[1]);
                lock (self.Lock)
                {
                    if (self.Eof)
                        throw PyEOFError.Create("End of stream already reached");
                    return PyBytes.From(DecompressorRun(self, data, maxLength));
                }
            }, acceptsKwargs: true);
            type.TypeDict["eof"] = new PyMemberDescriptor("eof", type, o => PyBool.FromBool(((Decompressor)o).Eof), readOnly: true);
            type.TypeDict["unused_data"] = new PyMemberDescriptor("unused_data", type, o => ((Decompressor)o).UnusedData, readOnly: true);
            type.TypeDict["needs_input"] = new PyMemberDescriptor("needs_input", type, o => PyBool.FromBool(((Decompressor)o).NeedsInput), readOnly: true);
            return type;
        }
    }
}
