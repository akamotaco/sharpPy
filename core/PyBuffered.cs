using System;
using System.Collections.Generic;
using System.Threading;

namespace SharpPy;

/// <summary>
/// Modules/_io/bufferedio.c: BufferedReader, BufferedWriter and BufferedRandom are one C struct
/// with flags, and so they are one class here. The buffer and its positions live in a State the
/// object points to, so that the cleanup of an abandoned file (bufferedio's tp_dealloc calls
/// close(), which flushes) can reach them without keeping the file object alive.
/// </summary>
public sealed class PyBuffered : PyIOBaseInstance
{
    internal sealed class State
    {
        internal PyObject Raw;
        internal bool Ok, Detached, Readable, Writable, Finalizing;
        internal long AbsPos = -1;
        internal byte[] Buffer;
        internal long Pos, RawPos, ReadEnd = -1, WritePos, WriteEnd = -1;
        internal int BufferSize;
        internal long BufferMask;
        internal readonly object Lock = new();
        internal int Owner;
    }

    private readonly State _s = new();

    internal enum Kind { Reader, Writer, Random }

    public static readonly PyIOBaseType ReaderType = MakeType("BufferedReader", Kind.Reader);
    public static readonly PyIOBaseType WriterType = MakeType("BufferedWriter", Kind.Writer);
    public static readonly PyIOBaseType RandomType = MakeType("BufferedRandom", Kind.Random);

    internal const int DefaultBufferSize = 8192;

    internal PyBuffered(PyType type) : base(type) { }

    internal PyObject Raw => _s.Raw;

    // CHECK_INITIALIZED
    private void CheckInitialized()
    {
        if (!_s.Ok)
            throw PyValueError.Create(_s.Detached ? "raw stream has been detached" : "I/O operation on uninitialized object");
    }

    private bool ValidReadBuffer => _s.Readable && _s.ReadEnd != -1;
    private bool ValidWriteBuffer => _s.Writable && _s.WriteEnd != -1;

    // ADJUST_POSITION
    private void AdjustPosition(long position)
    {
        _s.Pos = position;
        if (ValidReadBuffer && _s.ReadEnd < _s.Pos)
            _s.ReadEnd = _s.Pos;
    }

    private long ReadAhead => _s.Readable && ValidReadBuffer ? _s.ReadEnd - _s.Pos : 0;

    private long RawOffset => (ValidReadBuffer || ValidWriteBuffer) && _s.RawPos >= 0 ? _s.RawPos - _s.Pos : 0;

    private long RawTell() => _s.AbsPos != -1 ? _s.AbsPos : BufferedRawTell();

    private long MinusLastBlock(long size) => _s.BufferMask != 0 ? size & ~_s.BufferMask : _s.BufferSize * (size / _s.BufferSize);

    private static PyObject Call(PyObject target, string name, params PyObject[] args) =>
        target.GetAttribute(name).Call(args, null);

    // buffered_closed
    private bool Closed() => Call0Truth(_s.Raw, "closed");

    private static bool Call0Truth(PyObject target, string attribute) => target.GetAttribute(attribute).IsTrue();

    // IS_CLOSED
    private bool IsClosed => _s.Buffer == null || (_s.Raw is PyFileIO file ? file.Fd < 0 : Closed());

    // CHECK_CLOSED
    private void CheckClosed(string message)
    {
        if (IsClosed && ReadAhead == 0)
            throw PyValueError.Create(message);
    }

    private static long ShutdownGraceEnds;

    // ENTER_BUFFERED and LEAVE_BUFFERED
    private void Enter()
    {
        int me = Environment.CurrentManagedThreadId;
        if (_s.Owner == me)
            throw PyRuntimeError.Create($"reentrant call inside {ToRepr().Value}");
        if (!Monitor.TryEnter(_s.Lock))
        {
            // _enter_buffered_busy: at shutdown a daemon thread may have stopped holding the lock,
            // so only a grace period of a second is waited. CPython aborts after the first; here
            // the close that wanted the lock fails, its finalizer lets it go, and the second is
            // shared by the whole shutdown rather than paid per stream.
            if (PyGil.Finalizing)
            {
                bool acquired = false;
                if (ShutdownGraceEnds == 0)
                    ShutdownGraceEnds = Environment.TickCount64 + 1000;
                int wait = (int)Math.Max(0, ShutdownGraceEnds - Environment.TickCount64);
                PyGil.AllowThreads(() => acquired = Monitor.TryEnter(_s.Lock, wait));
                if (!acquired)
                    throw PyRuntimeError.Create($"could not acquire lock for {ToRepr().Value} at interpreter shutdown, possibly due to daemon threads");
            }
            else
                PyGil.AllowThreads(() => Monitor.Enter(_s.Lock));
        }
        _s.Owner = me;
    }

    private void Leave()
    {
        _s.Owner = 0;
        Monitor.Exit(_s.Lock);
    }

    private T Locked<T>(Func<T> body)
    {
        Enter();
        try
        {
            return body();
        }
        finally
        {
            Leave();
        }
    }

    // PyNumber_AsOff_t
    private static long Offset(PyObject value, bool overflowIsValueError = true)
    {
        var index = PyObject.IndexOrThrow(value);
        if (index.Value > long.MaxValue || index.Value < long.MinValue)
            throw overflowIsValueError ? PyValueError.Create("cannot fit 'int' into an offset-sized integer") : PyOverflowError.Create("Python int too large");
        return (long)index.Value;
    }

    // _buffered_raw_tell
    private long BufferedRawTell()
    {
        long n = Offset(Call(_s.Raw, "tell"));
        if (n < 0)
            throw new PythonException(PyOSError.FromArguments(new PyObject[] { PyString.From($"Raw stream returned invalid position {n}") }, null));
        _s.AbsPos = n;
        return n;
    }

    // _buffered_raw_seek
    private long BufferedRawSeek(long target, int whence)
    {
        long n = Offset(Call(_s.Raw, "seek", PyInt.From(target), PyInt.From(whence)));
        if (n < 0)
            throw new PythonException(PyOSError.FromArguments(new PyObject[] { PyString.From($"Raw stream returned invalid position {n}") }, null));
        _s.AbsPos = n;
        return n;
    }

    // _buffered_init
    private void BufferedInit()
    {
        if (_s.BufferSize <= 0)
            throw PyValueError.Create("buffer size must be strictly positive");
        _s.Buffer = new byte[_s.BufferSize];
        _s.Owner = 0;
        long n = _s.BufferSize - 1;
        while ((n & 1) != 0)
            n >>= 1;
        _s.BufferMask = n == 0 ? _s.BufferSize - 1 : 0;
        try
        {
            BufferedRawTell();
        }
        catch (PythonException)
        {
        }
    }

    private void ResetReadBuffer() => _s.ReadEnd = -1;

    private void ResetWriteBuffer()
    {
        _s.WritePos = 0;
        _s.WriteEnd = -1;
    }

    // _PyIOBase_check_readable and friends
    private static void CheckRaw(PyObject raw, string method, string message)
    {
        if (!ReferenceEquals(Call(raw, method), PyBool.True))
            throw PyUnsupportedOperation.Create(message);
    }

    private static int BufferSizeArgument(PyObject value) => value == null ? DefaultBufferSize : PyArgs.SsizeCount(value);

    // _io_BufferedReader___init___impl and the writer's and random's
    private void Initialize(Kind kind, PyObject[] args, PyDict kwargs)
    {
        string label = GetPyType().Name;
        PyObject raw = args.Length > 0 ? args[0] : null, size = args.Length > 1 ? args[1] : null;
        if (args.Length > 2)
            throw PyTypeError.Create($"{label}() takes at most 2 arguments ({args.Length} given)");
        if (kwargs != null)
            foreach (var pair in kwargs.InternalDict)
            {
                string key = (pair.Key as PyString)?.Value;
                if (key == "raw" && raw == null) raw = pair.Value;
                else if (key == "buffer_size" && size == null) size = pair.Value;
                else throw PyTypeError.Create($"'{key}' is an invalid keyword argument for {label}()");
            }
        if (raw == null)
            throw PyArgs.MissingArgument(label, "raw", 1);
        _s.Ok = false;
        _s.Detached = false;
        if (kind == Kind.Random)
            CheckRaw(raw, "seekable", "File or stream is not seekable.");
        if (kind != Kind.Writer)
            CheckRaw(raw, "readable", "File or stream is not readable.");
        if (kind != Kind.Reader)
            CheckRaw(raw, "writable", "File or stream is not writable.");
        // A stream initialized again lets go of the raw stream it had.
        if (_s.Raw != null && !ReferenceEquals(_s.Raw, raw))
            PyFinalizer.FinalizeAfter(_s.Raw, null);
        _s.Raw = raw;
        _s.BufferSize = BufferSizeArgument(size);
        _s.Readable = kind != Kind.Writer;
        _s.Writable = kind != Kind.Reader;
        BufferedInit();
        ResetReadBuffer();
        ResetWriteBuffer();
        _s.Pos = 0;
        _s.Ok = true;
        RegisterCleanup();
    }

    // buffered_dealloc and iobase_finalize: an abandoned stream is closed, which flushes it,
    // before its raw stream is.
    private void RegisterCleanup()
    {
        PyFinalizer.Attach(this);
        PyFinalizer.FinalizeAfter(_s.Raw, this);
    }

    // _bufferedreader_raw_read: -2 when the raw stream would block.
    private long RawRead(byte[] target, long offset, long length)
    {
        PyObject result;
        if (_s.Raw is PyFileIO && ReferenceEquals(target, _s.Buffer))
        {
            var view = new PyMemoryView(target, false).GetItem(new PySlice(PyInt.From(offset), PyInt.From(offset + length), PyNone.Instance));
            result = Call(_s.Raw, "readinto", view);
        }
        else
        {
            var view = new PyMemoryView(target, false).GetItem(new PySlice(PyInt.From(offset), PyInt.From(offset + length), PyNone.Instance));
            result = Call(_s.Raw, "readinto", view);
        }
        if (result is PyNone)
            return -2;
        long n;
        try
        {
            n = PyArgs.AsSsize(result, PyArgs.SsizeError.Value);
        }
        catch (PythonException error)
        {
            var failure = new PythonException(PyOSError.FromArguments(new PyObject[] { PyString.From("raw readinto() failed") }, null));
            failure.PyException.SetAttribute("__cause__", error.PyException);
            throw failure;
        }
        if (n < 0 || n > length)
            throw new PythonException(PyOSError.FromArguments(new PyObject[]
            {
                PyString.From($"raw readinto() returned invalid length {n} (should have been between 0 and {length})"),
            }, null));
        if (n > 0 && _s.AbsPos != -1)
            _s.AbsPos += n;
        return n;
    }

    // _bufferedreader_fill_buffer
    private long FillBuffer()
    {
        long start = ValidReadBuffer ? _s.ReadEnd : 0;
        long n = RawRead(_s.Buffer, start, _s.BufferSize - start);
        if (n <= 0)
            return n;
        _s.ReadEnd = start + n;
        _s.RawPos = start + n;
        return n;
    }

    private PyBytes Slice(long start, long count)
    {
        var data = new byte[count];
        Array.Copy(_s.Buffer, start, data, 0, count);
        return PyBytes.From(data);
    }

    // _bufferedreader_read_all
    private PyObject ReadAll()
    {
        long currentSize = ReadAhead;
        PyBytes data = null;
        if (currentSize > 0)
        {
            data = Slice(_s.Pos, currentSize);
            _s.Pos += currentSize;
        }
        if (_s.Writable)
            FlushAndRewind();
        ResetReadBuffer();
        var readall = _s.Raw.LookupAttribute("readall");
        if (readall != null)
        {
            var all = readall.Call(Array.Empty<PyObject>(), null);
            if (all is not PyNone && all is not PyBytes)
                throw PyTypeError.Create("readall() should return bytes");
            if (currentSize == 0)
                return all;
            if (all is PyBytes more)
                return PyBytes.From(Join(new List<byte[]> { data.Value, more.Value }));
            return data;
        }
        var chunks = new List<byte[]>();
        while (true)
        {
            if (data != null)
            {
                chunks.Add(data.Value);
                data = null;
            }
            var read = Call(_s.Raw, "read");
            if (read is not PyNone && read is not PyBytes)
                throw PyTypeError.Create("read() should return bytes");
            if (read is PyNone || ((PyBytes)read).Value.Length == 0)
                return currentSize == 0 ? read : PyBytes.From(Join(chunks));
            data = (PyBytes)read;
            currentSize += data.Value.Length;
            if (_s.AbsPos != -1)
                _s.AbsPos += data.Value.Length;
        }
    }

    private static byte[] Join(List<byte[]> chunks)
    {
        int total = 0;
        foreach (var chunk in chunks)
            total += chunk.Length;
        var result = new byte[total];
        int at = 0;
        foreach (var chunk in chunks)
        {
            Array.Copy(chunk, 0, result, at, chunk.Length);
            at += chunk.Length;
        }
        return result;
    }

    // _bufferedreader_read_fast: null where read_generic is needed.
    private PyBytes ReadFast(long n)
    {
        if (n > ReadAhead)
            return null;
        var result = Slice(_s.Pos, n);
        _s.Pos += n;
        return result;
    }

    // _bufferedreader_read_generic
    private PyObject ReadGeneric(long n)
    {
        long currentSize = ReadAhead;
        if (n <= currentSize)
            return ReadFast(n);
        var output = new byte[n];
        long remaining = n, written = 0;
        if (currentSize > 0)
        {
            Array.Copy(_s.Buffer, _s.Pos, output, 0, currentSize);
            remaining -= currentSize;
            written += currentSize;
            _s.Pos += currentSize;
        }
        if (_s.Writable)
            FlushAndRewind();
        ResetReadBuffer();
        PyObject Short()
        {
            Array.Resize(ref output, (int)written);
            return PyBytes.From(output);
        }
        while (remaining > 0)
        {
            long r = MinusLastBlock(remaining);
            if (r == 0)
                break;
            r = RawRead(output, written, r);
            if (r == 0 || r == -2)
                return r == 0 || written > 0 ? Short() : PyNone.Instance;
            remaining -= r;
            written += r;
        }
        _s.Pos = 0;
        _s.RawPos = 0;
        _s.ReadEnd = 0;
        while (remaining > 0 && _s.ReadEnd < _s.BufferSize)
        {
            long r = FillBuffer();
            if (r == 0 || r == -2)
                return r == 0 || written > 0 ? Short() : PyNone.Instance;
            long take = Math.Min(remaining, r);
            Array.Copy(_s.Buffer, _s.Pos, output, written, take);
            written += take;
            _s.Pos += take;
            remaining -= take;
            if (remaining == 0)
                break;
        }
        return PyBytes.From(output);
    }

    // _bufferedreader_peek_unlocked
    private PyObject PeekUnlocked()
    {
        long have = ReadAhead;
        if (have > 0)
            return Slice(_s.Pos, have);
        ResetReadBuffer();
        long r = FillBuffer();
        if (r == -2)
            r = 0;
        _s.Pos = 0;
        return Slice(0, r);
    }

    // _bufferedwriter_raw_write: -2 when the raw stream would block.
    private long RawWrite(byte[] source, long offset, long length)
    {
        var view = new PyMemoryView(source, true).GetItem(new PySlice(PyInt.From(offset), PyInt.From(offset + length), PyNone.Instance));
        var result = Call(_s.Raw, "write", view);
        if (result is PyNone)
            return -2;
        long n = PyArgs.AsSsize(result, PyArgs.SsizeError.Value);
        if (n < 0 || n > length)
            throw new PythonException(PyOSError.FromArguments(new PyObject[]
            {
                PyString.From($"raw write() returned invalid length {n} (should have been between 0 and {length})"),
            }, null));
        if (n > 0 && _s.AbsPos != -1)
            _s.AbsPos += n;
        return n;
    }

    private static PythonException BlockingError(long written) => new PythonException(PyType.BlockingIOErrorType.Call(new PyObject[]
    {
        PyInt.From(11), PyString.From("write could not complete without blocking"), PyInt.From(written),
    }, null) as PyBaseException ?? throw PyRuntimeError.Create("BlockingIOError"));

    // _bufferedwriter_flush_unlocked
    private void FlushUnlocked()
    {
        if (!ValidWriteBuffer || _s.WritePos == _s.WriteEnd)
        {
            ResetWriteBuffer();
            return;
        }
        long rewind = RawOffset + (_s.Pos - _s.WritePos);
        if (rewind != 0)
        {
            BufferedRawSeek(-rewind, 1);
            _s.RawPos -= rewind;
        }
        while (_s.WritePos < _s.WriteEnd)
        {
            long n = RawWrite(_s.Buffer, _s.WritePos, _s.WriteEnd - _s.WritePos);
            if (n == -2)
                throw BlockingError(0);
            _s.WritePos += n;
            _s.RawPos = _s.WritePos;
        }
        ResetWriteBuffer();
    }

    // buffered_flush_and_rewind_unlocked
    private void FlushAndRewind()
    {
        FlushUnlocked();
        if (_s.Readable)
        {
            try
            {
                BufferedRawSeek(-RawOffset, 1);
            }
            finally
            {
                ResetReadBuffer();
            }
        }
    }

    // _io_BufferedWriter_write_impl
    private PyObject Write(PyObject argument)
    {
        CheckInitialized();
        var data = BytesMethods.RequireBuffer(argument);
        return Locked<PyObject>(() =>
        {
            if (IsClosed)
                throw PyValueError.Create("write to closed file");
            if (!ValidReadBuffer && !ValidWriteBuffer)
            {
                _s.Pos = 0;
                _s.RawPos = 0;
            }
            long avail = _s.BufferSize - _s.Pos;
            if (data.Length <= avail)
            {
                Array.Copy(data, 0, _s.Buffer, _s.Pos, data.Length);
                if (!ValidWriteBuffer || _s.WritePos > _s.Pos)
                    _s.WritePos = _s.Pos;
                AdjustPosition(_s.Pos + data.Length);
                if (_s.Pos > _s.WriteEnd)
                    _s.WriteEnd = _s.Pos;
                return PyInt.From(data.Length);
            }
            try
            {
                FlushUnlocked();
            }
            catch (PythonException error) when (error.PyException.IsInstance(PyType.BlockingIOErrorType))
            {
                if (_s.Readable)
                    ResetReadBuffer();
                long pending = _s.WriteEnd - _s.WritePos;
                Array.Copy(_s.Buffer, _s.WritePos, _s.Buffer, 0, pending);
                _s.WriteEnd -= _s.WritePos;
                _s.RawPos -= _s.WritePos;
                _s.Pos -= _s.WritePos;
                _s.WritePos = 0;
                avail = _s.BufferSize - _s.WriteEnd;
                if (data.Length <= avail)
                {
                    Array.Copy(data, 0, _s.Buffer, _s.WriteEnd, data.Length);
                    _s.WriteEnd += data.Length;
                    _s.Pos += data.Length;
                    return PyInt.From(data.Length);
                }
                Array.Copy(data, 0, _s.Buffer, _s.WriteEnd, avail);
                _s.WriteEnd += avail;
                _s.Pos += avail;
                throw BlockingError(avail);
            }
            long offset = RawOffset;
            if (offset != 0)
            {
                BufferedRawSeek(-offset, 1);
                _s.RawPos -= offset;
            }
            long remaining = data.Length, written = 0;
            while (remaining > _s.BufferSize)
            {
                long n = RawWrite(data, written, data.Length - written);
                if (n == -2)
                {
                    if (remaining > _s.BufferSize)
                    {
                        Array.Copy(data, written, _s.Buffer, 0, _s.BufferSize);
                        _s.RawPos = 0;
                        AdjustPosition(_s.BufferSize);
                        _s.WriteEnd = _s.BufferSize;
                        written += _s.BufferSize;
                        throw BlockingError(written);
                    }
                    break;
                }
                written += n;
                remaining -= n;
            }
            if (_s.Readable)
                ResetReadBuffer();
            if (remaining > 0)
            {
                Array.Copy(data, written, _s.Buffer, 0, remaining);
                written += remaining;
            }
            _s.WritePos = 0;
            _s.WriteEnd = remaining;
            AdjustPosition(remaining);
            _s.RawPos = 0;
            return PyInt.From(written);
        });
    }

    // _buffered_readline
    private PyObject ReadLine(long limit)
    {
        CheckClosed("readline of closed file");
        long n = ReadAhead;
        if (limit >= 0 && n > limit)
            n = limit;
        long start = _s.Pos;
        int newline = n > 0 ? Array.IndexOf(_s.Buffer, (byte)'\n', (int)start, (int)n) : -1;
        if (newline >= 0)
        {
            var line = Slice(start, newline - start + 1);
            _s.Pos += newline - start + 1;
            return line;
        }
        if (n == limit)
        {
            var line = Slice(start, n);
            _s.Pos += n;
            return line;
        }
        return Locked<PyObject>(() =>
        {
            var chunks = new List<byte[]>();
            if (n > 0)
            {
                chunks.Add(Slice(start, n).Value);
                _s.Pos += n;
                if (limit >= 0)
                    limit -= n;
            }
            if (_s.Writable)
                FlushAndRewind();
            while (true)
            {
                ResetReadBuffer();
                long filled = FillBuffer();
                if (filled <= 0)
                    break;
                if (limit >= 0 && filled > limit)
                    filled = limit;
                int found = Array.IndexOf(_s.Buffer, (byte)'\n', 0, (int)filled);
                if (found >= 0)
                {
                    chunks.Add(Slice(0, found + 1).Value);
                    _s.Pos = found + 1;
                    break;
                }
                chunks.Add(Slice(0, filled).Value);
                if (filled == limit)
                {
                    _s.Pos = filled;
                    break;
                }
                if (limit >= 0)
                    limit -= filled;
            }
            return PyBytes.From(Join(chunks));
        });
    }

    // _buffered_readinto_generic
    private PyObject ReadInto(PyObject argument, bool one)
    {
        CheckInitialized();
        CheckClosed("readinto of closed file");
        var bytes = PyIOBaseSlots.WritableBytes(argument, one ? "readinto1" : "readinto");
        try
        {
            return ReadInto(bytes, one);
        }
        finally
        {
            bytes.Release();
        }
    }

    private PyObject ReadInto(PyMemoryView bytes, bool one)
    {
        long length = bytes.Length();
        var target = new byte[length];
        long written = 0;
        long n = ReadAhead;
        PyObject Copy(long count)
        {
            bytes.WriteContiguous(target.AsSpan(0, (int)count));
            return PyInt.From(count);
        }
        if (n > 0)
        {
            if (n >= length)
            {
                Array.Copy(_s.Buffer, _s.Pos, target, 0, length);
                _s.Pos += length;
                return Copy(length);
            }
            Array.Copy(_s.Buffer, _s.Pos, target, 0, n);
            _s.Pos += n;
            written = n;
        }
        var result = Locked<PyObject>(() =>
        {
            if (_s.Writable)
                FlushAndRewind();
            ResetReadBuffer();
            _s.Pos = 0;
            for (long remaining = length - written; remaining > 0; written += n, remaining -= n)
            {
                if (remaining > _s.BufferSize)
                    n = RawRead(target, written, remaining);
                else if (!(one && written > 0))
                {
                    n = FillBuffer();
                    if (n > 0)
                    {
                        if (n > remaining)
                            n = remaining;
                        Array.Copy(_s.Buffer, _s.Pos, target, written, n);
                        _s.Pos += n;
                    }
                }
                else
                    n = 0;
                if (n == 0 || (n == -2 && written > 0))
                    break;
                if (n < 0)
                    return PyNone.Instance;
                if (one)
                {
                    written += n;
                    break;
                }
            }
            return null;
        });
        return result ?? Copy(written);
    }

    private PyObject Read(PyObject[] args)
    {
        CheckInitialized();
        long n = PyArgs.OptionalSsize(args.Length > 0 ? args[0] : null);
        if (n < -1)
            throw PyValueError.Create("read length must be non-negative or -1");
        CheckClosed("read of closed file");
        if (n == -1)
            return Locked(ReadAll);
        var fast = ReadFast(n);
        if (fast != null)
            return fast;
        return Locked(() => ReadGeneric(n));
    }

    private PyObject Read1(PyObject[] args)
    {
        CheckInitialized();
        long n = PyArgs.OptionalSsize(args.Length > 0 ? args[0] : null);
        if (n < 0)
            n = _s.BufferSize;
        CheckClosed("read of closed file");
        if (n == 0)
            return PyBytes.From(Array.Empty<byte>());
        long have = ReadAhead;
        if (have > 0)
            return ReadFast(Math.Min(have, n));
        var result = new byte[n];
        long r = Locked(() =>
        {
            if (_s.Writable)
                FlushAndRewind();
            ResetReadBuffer();
            return RawRead(result, 0, n);
        });
        if (r == -2)
            r = 0;
        if (n > r)
            Array.Resize(ref result, (int)r);
        return PyBytes.From(result);
    }

    private PyObject Peek()
    {
        CheckInitialized();
        CheckClosed("peek of closed file");
        return Locked(() =>
        {
            if (_s.Writable)
                FlushAndRewind();
            return PeekUnlocked();
        });
    }

    private PyObject Flush()
    {
        CheckInitialized();
        CheckClosed("flush of closed file");
        return Locked<PyObject>(() =>
        {
            FlushAndRewind();
            return PyNone.Instance;
        });
    }

    // _io__Buffered_tell_impl
    private PyObject Tell()
    {
        CheckInitialized();
        long position = BufferedRawTell() - RawOffset;
        return PyInt.From(position < 0 ? 0 : position);
    }

    // _io__Buffered_seek_impl
    private PyObject Seek(PyObject[] args)
    {
        CheckInitialized();
        int whence = args.Length > 1 ? args[1].ToInt() : 0;
        if (whence is < 0 or > 2 && whence != 3 && whence != 4)
            throw PyValueError.Create($"whence value {whence} unsupported");
        CheckClosed("seek of closed file");
        CheckRaw(_s.Raw, "seekable", "File or stream is not seekable.");
        long target = Offset(args[0]);
        if ((whence == 0 || whence == 1) && _s.Readable)
        {
            long current = RawTell(), avail = ReadAhead;
            if (avail > 0)
            {
                long offset = whence == 0 ? target - (current - RawOffset) : target;
                if (offset >= -_s.Pos && offset <= avail)
                {
                    _s.Pos += offset;
                    return PyInt.From(Math.Max(0, current - avail + offset));
                }
            }
        }
        return Locked<PyObject>(() =>
        {
            if (_s.Writable)
                FlushUnlocked();
            if (whence == 1)
                target -= RawOffset;
            long n = BufferedRawSeek(target, whence);
            _s.RawPos = -1;
            if (_s.Readable)
                ResetReadBuffer();
            return PyInt.From(n);
        });
    }

    private PyObject Truncate(PyObject[] args)
    {
        CheckInitialized();
        CheckClosed("truncate of closed file");
        if (!_s.Writable)
            throw PyUnsupportedOperation.Create("truncate");
        return Locked(() =>
        {
            FlushAndRewind();
            var result = Call(_s.Raw, "truncate", args.Length > 0 ? args[0] : PyNone.Instance);
            try { BufferedRawTell(); }
            catch (PythonException) { }
            return result;
        });
    }

    // _io__Buffered_close_impl
    private PyObject Close()
    {
        CheckInitialized();
        Enter();
        bool locked = true;
        try
        {
            if (Closed())
                return PyNone.Instance;
            if (_s.Finalizing)
            {
                try { DeallocWarn(this); }
                catch (PythonException) { }
            }
            Leave();
            locked = false;
            PythonException failure = null;
            try
            {
                Call(this, "flush");
            }
            catch (PythonException error)
            {
                failure = error;
            }
            Enter();
            locked = true;
            PyObject result;
            try
            {
                result = Call(_s.Raw, "close");
            }
            catch (PythonException error) when (failure != null)
            {
                error.PyException.SetAttribute("__context__", failure.PyException);
                throw;
            }
            _s.Buffer = null;
            _s.ReadEnd = 0;
            _s.Pos = 0;
            if (failure != null)
                throw failure;
            return result;
        }
        finally
        {
            if (locked)
                Leave();
        }
    }

    private PyObject Detach()
    {
        CheckInitialized();
        Call(this, "flush");
        var raw = _s.Raw;
        _s.Raw = null;
        _s.Detached = true;
        _s.Ok = false;
        PyFinalizer.FinalizeAfter(raw, null);
        return raw;
    }

    private PyObject DeallocWarn(PyObject source)
    {
        if (_s.Ok && _s.Raw != null)
        {
            try { Call(_s.Raw, "_dealloc_warn", source); }
            catch (PythonException) { }
        }
        return PyNone.Instance;
    }

    // buffered_repr
    protected internal override PyString Repr()
    {
        string typeName = GetPyType() is PyClass ? GetPyType().Name : GetPyType().FullName;
        PyObject name = null;
        try
        {
            name = GetAttribute("name");
        }
        catch (PythonException error) when (error.PyException is PyValueError or PyAttributeError)
        {
        }
        if (name == null)
            return PyString.From($"<{typeName}>");
        if (!PyReprGuard.Enter(this))
            throw PyRuntimeError.Create($"reentrant call inside {typeName}.__repr__");
        try
        {
            return PyString.From($"<{typeName} name={name.ToRepr().Value}>");
        }
        finally
        {
            PyReprGuard.Leave(this);
        }
    }

    // buffered_iternext
    protected internal override PyObject IterNext()
    {
        CheckInitialized();
        PyObject line;
        if (ReferenceEquals(GetPyType(), ReaderType) || ReferenceEquals(GetPyType(), RandomType))
            line = ReadLine(-1);
        else
        {
            line = Call(this, "readline");
            if (line is not PyBytes)
                throw new PythonException(PyOSError.FromArguments(new PyObject[]
                {
                    PyString.From($"readline() should have returned a bytes object, not '{line.GetTypeName()}'"),
                }, null));
        }
        if (((PyBytes)line).Value.Length == 0)
            throw PyStopIteration.Create();
        return line;
    }


    private static PyIOBaseType MakeType(string name, Kind kind)
    {
        var type = new PyIOBaseType(name, new PyType[] { PyIOBaseSlots.BufferedIOBaseType });
        type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) => new PyBuffered((PyType)cls),
            acceptsKwargs: true);
        type.TypeDict["__init__"] = new PyWrapperDescriptor("__init__", type, (self, args, kwargs) =>
        {
            ((PyBuffered)self).Initialize(kind, args, kwargs);
            return PyNone.Instance;
        }, 0, int.MaxValue, acceptsKwargs: true);
        type.TypeDict["__repr__"] = new PyWrapperDescriptor("__repr__", type, (self, args, kwargs) => self.Repr());

        void Method(string method, Func<PyBuffered, PyObject[], PyObject> body, int min = 0, int max = 0) =>
            type.TypeDict[method] = new PyMethodDescriptor(method, type, (self, args, kwargs) =>
            {
                if (args.Length < min || args.Length > max)
                    throw PyTypeError.Create(min == max
                        ? max == 0 ? $"{name}.{method}() takes no arguments ({args.Length} given)" : $"{name}.{method}() takes exactly one argument ({args.Length} given)"
                        : args.Length < min ? $"{method} expected at least {min} argument{(min == 1 ? "" : "s")}, got {args.Length}"
                        : $"{method} expected at most {max} argument{(max == 1 ? "" : "s")}, got {args.Length}");
                return body(((PyBuffered)self), args);
            }, minArgs: 0, maxArgs: int.MaxValue);

        PyObject Forward(PyBuffered self, string method)
        {
            self.CheckInitialized();
            return Call(self._s.Raw, method);
        }

        Method("detach", (self, args) => self.Detach());
        Method("close", (self, args) => self.Close());
        Method("seekable", (self, args) => Forward(self, "seekable"));
        Method("fileno", (self, args) => Forward(self, "fileno"));
        Method("isatty", (self, args) => Forward(self, "isatty"));
        Method("_dealloc_warn", (self, args) => self.DeallocWarn(args[0]), 1, 1);
        Method("seek", (self, args) => self.Seek(args), 1, 2);
        Method("tell", (self, args) => self.Tell());
        Method("truncate", (self, args) => self.Truncate(args), 0, 1);
        Method("__sizeof__", (self, args) => PyInt.From(64 + (self._s.Buffer?.Length ?? 0)));
        foreach (var refuse in new[] { "__reduce__", "__reduce_ex__" })
            type.TypeDict[refuse] = new PyMethodDescriptor(refuse, type, (self, args, kwargs) =>
                throw PyTypeError.Create($"cannot pickle '{self.GetPyType().Name}' instances"));
        if (kind != Kind.Writer)
        {
            Method("readable", (self, args) => Forward(self, "readable"));
            Method("read", (self, args) => self.Read(args), 0, 1);
            Method("peek", (self, args) => self.Peek(), 0, 1);
            Method("read1", (self, args) => self.Read1(args), 0, 1);
            Method("readinto", (self, args) => self.ReadInto(args[0], false), 1, 1);
            Method("readinto1", (self, args) => self.ReadInto(args[0], true), 1, 1);
            Method("readline", (self, args) =>
            {
                self.CheckInitialized();
                return self.ReadLine(PyArgs.OptionalSsize(args.Length > 0 ? args[0] : null));
            }, 0, 1);
        }
        if (kind != Kind.Reader)
        {
            Method("writable", (self, args) => Forward(self, "writable"));
            Method("write", (self, args) => self.Write(args[0]), 1, 1);
        }
        // BufferedReader.flush is simple_flush: the raw stream's.
        if (kind == Kind.Reader)
            Method("flush", (self, args) => Forward(self, "flush"));
        else
            Method("flush", (self, args) => self.Flush());

        type.TypeDict["raw"] = new PyMemberDescriptor("raw", type, self => ((PyBuffered)self)._s.Raw ?? PyNone.Instance);
        type.TypeDict["_finalizing"] = new PyMemberDescriptor("_finalizing", type, self => PyBool.FromBool(((PyBuffered)self)._s.Finalizing),
            (self, value) => ((PyBuffered)self)._s.Finalizing = value.IsTrue());
        type.TypeDict["closed"] = new PyGetSetDescriptor("closed", type, self =>
        {
            var buffered = ((PyBuffered)self);
            buffered.CheckInitialized();
            return buffered._s.Raw.GetAttribute("closed");
        });
        type.TypeDict["name"] = new PyGetSetDescriptor("name", type, self =>
        {
            var buffered = ((PyBuffered)self);
            buffered.CheckInitialized();
            return buffered._s.Raw.GetAttribute("name");
        });
        type.TypeDict["mode"] = new PyGetSetDescriptor("mode", type, self =>
        {
            var buffered = ((PyBuffered)self);
            buffered.CheckInitialized();
            return buffered._s.Raw.GetAttribute("mode");
        });
        return type;
    }
}

/// <summary>bufferedio.c BufferedRWPair: a reader and a writer over two raw streams.</summary>
public sealed class PyBufferedRWPair : PyIOBaseInstance
{
    private PyObject _reader, _writer;

    public static readonly PyIOBaseType PairType = MakeType();

    internal PyBufferedRWPair(PyType type) : base(type) { }

    // _forward_call
    private static PyObject Forward(PyObject target, string name, PyObject[] args)
    {
        if (target == null)
            throw PyValueError.Create("I/O operation on uninitialized object");
        return target.GetAttribute(name).Call(args, null);
    }

    private static PyIOBaseType MakeType()
    {
        var type = new PyIOBaseType("BufferedRWPair", new PyType[] { PyIOBaseSlots.BufferedIOBaseType });
        type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) => new PyBufferedRWPair((PyType)cls),
            acceptsKwargs: true);
        type.TypeDict["__init__"] = new PyWrapperDescriptor("__init__", type, (self, args, kwargs) =>
        {
            var pair = (PyBufferedRWPair)self;
            if (kwargs != null && kwargs.Length() != 0)
                throw PyTypeError.Create("BufferedRWPair() takes no keyword arguments");
            if (args.Length is < 2 or > 3)
                throw PyTypeError.Create(args.Length < 2 ? $"BufferedRWPair expected at least 2 arguments, got {args.Length}" : $"BufferedRWPair expected at most 3 arguments, got {args.Length}");
            if (!ReferenceEquals(args[0].GetAttribute("readable").Call(Array.Empty<PyObject>(), null), PyBool.True))
                throw PyUnsupportedOperation.Create("File or stream is not readable.");
            if (!ReferenceEquals(args[1].GetAttribute("writable").Call(Array.Empty<PyObject>(), null), PyBool.True))
                throw PyUnsupportedOperation.Create("File or stream is not writable.");
            var size = args.Length > 2 ? args[2] : PyInt.From(PyBuffered.DefaultBufferSize);
            pair._reader = PyBuffered.ReaderType.Call(new[] { args[0], size }, null);
            pair._writer = PyBuffered.WriterType.Call(new[] { args[1], size }, null);
            return PyNone.Instance;
        }, 0, int.MaxValue, acceptsKwargs: true);
        foreach (var name in new[] { "read", "peek", "read1", "readinto", "readinto1", "readable" })
            type.TypeDict[name] = new PyMethodDescriptor(name, type, (self, args, kwargs) => Forward(((PyBufferedRWPair)self)._reader, name, args));
        foreach (var name in new[] { "write", "flush", "writable" })
            type.TypeDict[name] = new PyMethodDescriptor(name, type, (self, args, kwargs) => Forward(((PyBufferedRWPair)self)._writer, name, args));
        type.TypeDict["close"] = new PyMethodDescriptor("close", type, (self, args, kwargs) =>
        {
            var pair = (PyBufferedRWPair)self;
            PythonException failure = null;
            try
            {
                Forward(pair._writer, "close", Array.Empty<PyObject>());
            }
            catch (PythonException error)
            {
                failure = error;
            }
            PyObject result;
            try
            {
                result = Forward(pair._reader, "close", Array.Empty<PyObject>());
            }
            catch (PythonException error) when (failure != null)
            {
                error.PyException.SetAttribute("__context__", failure.PyException);
                throw;
            }
            if (failure != null)
                throw failure;
            return result;
        });
        type.TypeDict["isatty"] = new PyMethodDescriptor("isatty", type, (self, args, kwargs) =>
        {
            var pair = (PyBufferedRWPair)self;
            var result = Forward(pair._writer, "isatty", Array.Empty<PyObject>());
            return !ReferenceEquals(result, PyBool.False) ? result : Forward(pair._reader, "isatty", Array.Empty<PyObject>());
        });
        type.TypeDict["closed"] = new PyGetSetDescriptor("closed", type, self =>
        {
            var pair = (PyBufferedRWPair)self;
            if (pair._writer == null)
                throw PyRuntimeError.Create("the BufferedRWPair object is being garbage-collected");
            return pair._writer.GetAttribute("closed");
        });
        return type;
    }
}
