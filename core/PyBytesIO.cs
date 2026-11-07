using System;

namespace SharpPy;

/// <summary>
/// Modules/_io/bytesio.c. The buffer is a byte array that may be larger than the data
/// (string_size); null means closed. While a bytes object handed out shares the array
/// (SHARED_BUF), a write copies it first. getbuffer() exports the array through a
/// _BytesIOBuffer, and nothing may resize it while an export is alive.
/// </summary>
public class PyBytesIO : PyIOBaseInstance
{
    public static readonly PyIOBaseType BytesType = MakeType();

    private byte[] _buf = Array.Empty<byte>();
    // The bytes object that shares _buf, if one was handed out or taken in.
    private PyBytes _shared;
    private long _pos;
    private int _size;
    private int _exports;

    public PyBytesIO(byte[] initial, PyType type = null) : base(type ?? BytesType)
    {
        if (initial != null)
        {
            _buf = (byte[])initial.Clone();
            _size = _buf.Length;
        }
    }

    private static PyBytesIO Self(PyObject self, string name) => self as PyBytesIO
        ?? throw PyTypeError.Create($"descriptor '{name}' requires a '_io.BytesIO' object but received a '{self.GetTypeName()}'");

    private static PythonException BufferError() =>
        new(new PyBufferError("Existing exports of data: object cannot be re-sized"));

    // CHECK_CLOSED
    private void CheckClosed()
    {
        if (_buf == null)
            throw PyValueError.Create("I/O operation on closed file.");
    }

    // CHECK_EXPORTS
    private void CheckExports()
    {
        if (BufferExports.Held(() => System.Threading.Volatile.Read(ref _exports) > 0))
            throw BufferError();
    }

    private bool Shared => _shared != null;

    // scan_eol
    private long ScanEol(long len)
    {
        if (_pos >= _size)
            return 0;
        long maxlen = _size - _pos;
        if (len < 0 || len > maxlen)
            len = maxlen;
        if (len > 0)
        {
            int at = Array.IndexOf(_buf, (byte)'\n', (int)_pos, (int)len);
            if (at >= 0)
                len = at - _pos + 1;
        }
        return len;
    }

    // unshare_buffer
    private void Unshare(long size)
    {
        var copy = new byte[size];
        Array.Copy(_buf, copy, _size);
        _buf = copy;
        _shared = null;
    }

    // resize_buffer (Modules/_io/bytesio.c), in its size_t arithmetic: past PY_SSIZE_T_MAX is the
    // OverflowError; an alloc that wraps negative reaches PyBytes_FromStringAndSize or
    // _PyBytes_Resize as the SystemError it raises there; what the CLR cannot give is
    // PyErr_NoMemory.
    private void Resize(ulong size)
    {
        ulong alloc = (ulong)_buf.Length;
        if (size > long.MaxValue)
            throw PyOverflowError.Create("new buffer size too large");
        if (size < alloc / 2)
            alloc = size + 1;
        else if (size < alloc)
            return;
        else if (size <= alloc * 1.125)
            alloc = size + (size >> 3) + (size < 9UL ? 3UL : 6UL);
        else
            alloc = size + 1;
        // SHARED_BUF is a reference count above one; a BytesIO's first buffer is the empty bytes
        // object, which always is.
        if ((long)alloc < 0)
            throw PySystemError.Create(Shared || _buf.Length == 0 ? "Negative size passed to PyBytes_FromStringAndSize"
                : "bad argument to internal function");
        int length = PyAllocation.ArrayLength((long)alloc);
        if (Shared)
            Unshare(length);
        else
            Array.Resize(ref _buf, length);
    }

    private static byte[] Contiguous(PyObject value) => BytesMethods.Buffer(value)
        ?? throw PyTypeError.Create($"a bytes-like object is required, not '{value.GetTypeName()}'");

    // write_bytes
    private long WriteBytes(PyObject value)
    {
        CheckClosed();
        CheckExports();
        var data = Contiguous(value);
        int len = data.Length;
        if (len == 0)
            return 0;
        // write_bytes: endpos is a size_t; past the buffer it goes to resize_buffer, which only
        // returns when the buffer holds it.
        ulong wanted = (ulong)_pos + (ulong)len;
        if (wanted > (ulong)_buf.Length)
            Resize(wanted);
        long endpos = (long)wanted;
        if (Shared)
            Unshare(Math.Max(endpos, _size));
        if (_pos > _size)
            Array.Clear(_buf, _size, (int)(_pos - _size));
        Array.Copy(data, 0, _buf, _pos, len);
        _pos = endpos;
        if (_size < endpos)
            _size = (int)endpos;
        return len;
    }

    // The bytes object over the whole array, made once and then shared.
    private PyBytes SharedBytes() => _shared ??= PyBytes.From(_buf);

    // _io_BytesIO_getvalue_impl
    private PyObject GetValue()
    {
        CheckClosed();
        if (_size <= 1 || _exports > 0)
            return PyBytes.From(_buf.AsSpan(0, _size).ToArray());
        if (_size != _buf.Length)
        {
            if (Shared)
                Unshare(_size);
            else
                Array.Resize(ref _buf, _size);
        }
        return SharedBytes();
    }

    // read_bytes
    private PyObject ReadBytes(long size)
    {
        if (size > 1 && _pos == 0 && size == _buf.Length && _exports == 0)
        {
            _pos += size;
            return SharedBytes();
        }
        // Past the end, size is 0 and nothing is copied.
        var output = size == 0 ? Array.Empty<byte>() : _buf.AsSpan((int)_pos, (int)size).ToArray();
        _pos += size;
        return PyBytes.From(output);
    }

    private PyObject Read(long size)
    {
        CheckClosed();
        long n = _size - _pos;
        if (size < 0 || size > n)
        {
            size = n;
            if (size < 0)
                size = 0;
        }
        return ReadBytes(size);
    }

    private PyObject ReadLines(PyObject arg)
    {
        CheckClosed();
        long maxsize;
        if (PyInt.LongCheck(arg))
            maxsize = BytesMethods.AsSsize(arg);
        else if (arg is PyNone)
            maxsize = -1;
        else
            throw PyTypeError.Create($"integer argument expected, got '{arg.GetTypeName()}'");
        long size = 0, n;
        var result = new PyList();
        while ((n = ScanEol(-1)) != 0)
        {
            result.Append(PyBytes.From(_buf.AsSpan((int)_pos, (int)n).ToArray()));
            _pos += n;
            size += n;
            if (maxsize > 0 && size >= maxsize)
                break;
        }
        return result;
    }

    private PyObject ReadInto(PyObject argument)
    {
        CheckClosed();
        var view = PyIOBaseSlots.WritableBytes(argument, "readinto");
        try
        {
            long len = view.Length();
            long n = _size - _pos;
            if (len > n)
            {
                len = n;
                if (len < 0)
                    len = 0;
            }
            view.WriteContiguous(_buf.AsSpan((int)Math.Min(_pos, _buf.Length), (int)len));
            _pos += len;
            return PyInt.From(len);
        }
        finally
        {
            view.Release();
        }
    }

    private PyObject Truncate(long size)
    {
        CheckClosed();
        CheckExports();
        if (size < 0)
            throw PyValueError.Create($"negative size value {size}");
        if (size < _size)
        {
            _size = (int)size;
            Resize((ulong)size);
        }
        return PyInt.From(size);
    }

    private PyObject Seek(long pos, int whence)
    {
        CheckClosed();
        if (pos < 0 && whence == 0)
            throw PyValueError.Create($"negative seek value {pos}");
        if (whence == 1)
        {
            if (pos > long.MaxValue - _pos)
                throw PyOverflowError.Create("new position too large");
            pos += _pos;
        }
        else if (whence == 2)
        {
            if (pos > long.MaxValue - _size)
                throw PyOverflowError.Create("new position too large");
            pos += _size;
        }
        else if (whence != 0)
            throw PyValueError.Create($"invalid whence ({whence}, should be 0, 1 or 2)");
        if (pos < 0)
            pos = 0;
        _pos = pos;
        return PyInt.From(_pos);
    }

    // bytesio_iternext
    protected internal override PyObject IterNext()
    {
        CheckClosed();
        long n = ScanEol(-1);
        if (n == 0)
            throw PyStopIteration.Create();
        return ReadBytes(n);
    }

    protected internal override PyObject Iter() => this;

    // bytesio_getstate
    private PyObject GetState()
    {
        var value = GetValue();
        // a copy of the dict, None when there never was one
        PyObject dict = ObjectDict(false) is { } own ? own.Copy() : PyNone.Instance;
        return PyTuple.From(new[] { value, PyInt.From(_pos), dict });
    }

    private static string TypeName(PyObject self) =>
        self.GetPyType() is PyIOBaseType native ? "_io." + native.Name : self.GetPyType().Name;

    // bytesio_setstate
    private PyObject SetState(PyObject state)
    {
        if (state is not PyTuple tuple || tuple.Items.Length < 3)
            throw PyTypeError.Create($"{TypeName(this)}.__setstate__ argument should be 3-tuple, got {state.GetTypeName()}");
        CheckExports();
        _size = 0;
        _pos = 0;
        WriteBytes(tuple.Items[0]);
        var position = tuple.Items[1];
        if (!PyInt.LongCheck(position))
            throw PyTypeError.Create($"second item of state must be an integer, not {position.GetTypeName()}");
        long pos = BytesMethods.AsSsize(position);
        if (pos < 0)
            throw PyValueError.Create("position value cannot be negative");
        _pos = pos;
        var dict = tuple.Items[2];
        if (dict is not PyNone)
        {
            if (dict is not PyDict entries)
                throw PyTypeError.Create($"third item of state should be a dict, got a {dict.GetTypeName()}");
            ObjectDict(true).Update(entries);
        }
        return PyNone.Instance;
    }

    private static readonly string[] InitNames = { "initial_bytes" };

    // _io_BytesIO___init___impl
    internal void Initialize(PyObject[] args, PyDict kwargs)
    {
        var values = PyArgs.Unpack("BytesIO", args, kwargs, InitNames, 0, 0, 1);
        var initvalue = values[0];
        _size = 0;
        _pos = 0;
        if (BufferExports.Held(() => System.Threading.Volatile.Read(ref _exports) > 0))
            throw BufferError();
        if (initvalue != null && initvalue is not PyNone)
        {
            if (initvalue is PyBytes bytes && bytes.GetPyType() == PyType.BytesType)
            {
                _buf = bytes.Value;
                _shared = bytes;
                _size = bytes.Value.Length;
            }
            else
            {
                WriteBytes(initvalue);
                _pos = 0;
            }
        }
    }

    public static void Register() { }

    /// <summary>Modules/_io/bytesio.c _BytesIOBuffer: what getbuffer()'s view exports from.</summary>
    private sealed class ExportBuffer : PyObject, IBufferExporter
    {
        internal static readonly PyType BufferType = MakeBufferType();
        private readonly PyBytesIO _source;

        internal ExportBuffer(PyBytesIO source) => _source = source;

        private static PyType MakeBufferType()
        {
            var type = new PyType("_BytesIOBuffer", new[] { PyType.ObjectType }, "_io");
            type.TypeDict["__module__"] = PyString.From("_io");
            return type;
        }

        public override PyType GetPyType() => BufferType;
        public override string GetTypeName() => "_io._BytesIOBuffer";
        public override bool SupportsBuffer() => true;

        // bytesiobuf_getbuffer
        public override PyMemoryView GetBuffer(int flags)
        {
            var b = _source;
            if (b._exports == 0 && b.Shared)
                b.Unshare(b._size);
            return PyMemoryView.FromExporter(this, b._size);
        }

        public void AcquireBufferExport() => System.Threading.Interlocked.Increment(ref _source._exports);
        public void ReleaseBufferExport() => System.Threading.Interlocked.Decrement(ref _source._exports);
        public Span<byte> BufferSpan => _source._buf;
    }

    private static PyIOBaseType MakeType()
    {
        var type = new PyIOBaseType("BytesIO", new PyType[] { PyIOBaseSlots.BufferedIOBaseType });
        // bytesio_new: an empty buffer even before __init__.
        type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) => new PyBytesIO(null, (PyType)cls),
            acceptsKwargs: true);
        type.TypeDict["__init__"] = new PyWrapperDescriptor("__init__", type, (self, args, kwargs) =>
        {
            Self(self, "__init__").Initialize(args, kwargs);
            return PyNone.Instance;
        }, 0, int.MaxValue, acceptsKwargs: true);

        void Def(string name, PyCall kind, Func<PyBytesIO, PyObject[], PyObject> body, int min = 0, int max = 0) =>
            PyArgs.Define(type, name, kind, (self, args) => body(Self(self, name), args), min, max);
        PyObject Arg(PyObject[] args, int i) => args.Length > i ? args[i] : null;

        foreach (var name in new[] { "readable", "writable", "seekable" })
            Def(name, PyCall.NoArgs, (self, args) =>
            {
                self.CheckClosed();
                return PyBool.True;
            });
        Def("close", PyCall.NoArgs, (self, args) =>
        {
            self.CheckExports();
            self._buf = null;
            self._shared = null;
            return PyNone.Instance;
        });
        Def("flush", PyCall.NoArgs, (self, args) =>
        {
            self.CheckClosed();
            return PyNone.Instance;
        });
        Def("isatty", PyCall.NoArgs, (self, args) =>
        {
            self.CheckClosed();
            return PyBool.False;
        });
        Def("tell", PyCall.NoArgs, (self, args) =>
        {
            self.CheckClosed();
            return PyInt.From(self._pos);
        });
        Def("write", PyCall.One, (self, args) => PyInt.From(self.WriteBytes(args[0])));
        Def("writelines", PyCall.One, (self, args) =>
        {
            self.CheckClosed();
            var iterator = args[0].GetIterator();
            while (true)
            {
                PyObject item;
                try
                {
                    item = iterator.Next();
                }
                catch (PythonException error) when (error.PyException is PyStopIteration)
                {
                    break;
                }
                self.WriteBytes(item);
            }
            return PyNone.Instance;
        });
        Def("read1", PyCall.Fast, (self, args) => self.Read(PyArgs.OptionalSsize(Arg(args, 0))), 0, 1);
        Def("readinto", PyCall.One, (self, args) => self.ReadInto(args[0]));
        Def("readline", PyCall.Fast, (self, args) =>
        {
            long size = PyArgs.OptionalSsize(Arg(args, 0));
            self.CheckClosed();
            return self.ReadBytes(self.ScanEol(size));
        }, 0, 1);
        Def("readlines", PyCall.Fast, (self, args) => self.ReadLines(Arg(args, 0) ?? PyNone.Instance), 0, 1);
        Def("read", PyCall.Fast, (self, args) => self.Read(PyArgs.OptionalSsize(Arg(args, 0))), 0, 1);
        Def("getbuffer", PyCall.Defining, (self, args) =>
        {
            self.CheckClosed();
            return new ExportBuffer(self).GetBuffer(0);
        });
        Def("getvalue", PyCall.NoArgs, (self, args) => self.GetValue());
        Def("seek", PyCall.Fast, (self, args) =>
            self.Seek(PyArgs.Ssize(args[0]), args.Length > 1 ? PyArgs.Int(args[1]) : 0), 1, 2);
        Def("truncate", PyCall.Fast, (self, args) =>
        {
            // Py_ssize_t(accept={int, NoneType}, c_default="self->pos")
            long size = PyArgs.OptionalSsize(Arg(args, 0), self._pos);
            return self.Truncate(size);
        }, 0, 1);
        Def("__getstate__", PyCall.NoArgs, (self, args) => self.GetState());
        Def("__setstate__", PyCall.One, (self, args) => self.SetState(args[0]));
        Def("__sizeof__", PyCall.NoArgs, (self, args) =>
            PyInt.From(64 + (self._buf != null && !self.Shared ? 33 + self._buf.Length : 0)));
        // tp_iter is PyObject_SelfIter and tp_iternext is bytesio_iternext.
        type.TypeDict["__iter__"] = new PyWrapperDescriptor("__iter__", type, (self, args, kwargs) => self, 0, 0);
        type.TypeDict["__next__"] = new PyWrapperDescriptor("__next__", type, (self, args, kwargs) => Self(self, "__next__").IterNext(), 0, 0);
        type.TypeDict["closed"] = new PyGetSetDescriptor("closed", type, self => PyBool.FromBool(Self(self, "closed")._buf == null));
        return type;
    }
}
