using System;
using System.Text;

namespace SharpPy;

/// <summary>
/// Modules/_io/stringio.c. The text is kept as code points (Py_UCS4), so positions and sizes
/// count characters as Python does. While only appends happen the text accumulates in a
/// writer (STATE_ACCUMULATING); the first operation that needs random access realizes it into
/// the code point buffer (STATE_REALIZED). Newlines on the way in go through an
/// IncrementalNewlineDecoder, as in TextIOWrapper.
/// </summary>
public class PyStringIO : PyIOBaseInstance
{
    public static readonly PyIOBaseType StringType = MakeType();

    private int[] _buf = Array.Empty<int>();
    private long _pos;
    private int _size;
    private bool _accumulating;
    private readonly StringBuilder _writer = new();
    private bool _ok, _closed, _readUniversal, _readTranslate;
    private PyNewlineDecoder _decoder;
    private string _readnl, _writenl;

    public PyStringIO(PyType type) : base(type ?? StringType) { }

    private static PyStringIO Self(PyObject self, string name) => self as PyStringIO
        ?? throw PyTypeError.Create($"descriptor '{name}' requires a '_io.StringIO' object but received a '{self.GetTypeName()}'");

    // CHECK_INITIALIZED
    private void CheckInitialized()
    {
        if (!_ok)
            throw PyValueError.Create("I/O operation on uninitialized object");
    }

    // CHECK_CLOSED: stringio.c's wording has no period.
    private void CheckClosed()
    {
        if (_closed)
            throw PyValueError.Create("I/O operation on closed file");
    }

    private static int CodePoints(string text, Span<int> into)
    {
        int n = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                into[n++] = char.ConvertToUtf32(c, text[++i]);
            else
                into[n++] = c;
        }
        return n;
    }

    private static string Text(ReadOnlySpan<int> points)
    {
        var text = new StringBuilder(points.Length);
        foreach (int point in points)
        {
            if (point > 0xFFFF)
                text.Append(char.ConvertFromUtf32(point));
            else
                text.Append((char)point);
        }
        return text.ToString();
    }

    // resize_buffer: room for size code points and the terminating one.
    private void Resize(long size)
    {
        long alloc = _buf.Length;
        size += 1;
        if (size > Array.MaxLength)
            throw PyOverflowError.Create("new buffer size too large");
        if (size < alloc / 2)
            alloc = size + 1;
        else if (size < alloc)
            return;
        else if (size <= alloc * 1.125)
            alloc = size + (size >> 3) + (size < 9 ? 3 : 6);
        else
            alloc = size + 1;
        if (alloc > Array.MaxLength)
            throw PyOverflowError.Create("new buffer size too large");
        Array.Resize(ref _buf, (int)alloc);
    }

    // realize
    private void Realize()
    {
        if (!_accumulating)
            return;
        _accumulating = false;
        string intermediate = _writer.ToString();
        _writer.Clear();
        int len = PyString.CodePointCount(intermediate);
        Resize(len);
        CodePoints(intermediate, _buf);
    }

    // write_str
    private void WriteStr(string text)
    {
        string decoded = _decoder != null ? _decoder.Decode(PyString.From(text), true) : text;
        if (_writenl != null)
            decoded = decoded.Replace("\n", _writenl);
        int len = PyString.CodePointCount(decoded);
        if (_pos > long.MaxValue - len)
            throw PyOverflowError.Create("new position too large");
        if (_accumulating)
        {
            if (_size == _pos)
            {
                _writer.Append(decoded);
                _pos += len;
                _size = (int)_pos;
                return;
            }
            Realize();
        }
        if (_pos + len > _size)
            Resize(_pos + len);
        if (_pos > _size)
            Array.Clear(_buf, _size, (int)(_pos - _size));
        CodePoints(decoded, _buf.AsSpan((int)_pos));
        _pos += len;
        if (_size < _pos)
            _size = (int)_pos;
    }

    private string GetValue()
    {
        CheckInitialized();
        CheckClosed();
        return _accumulating ? _writer.ToString() : Text(_buf.AsSpan(0, _size));
    }

    private PyObject Read(long size)
    {
        CheckInitialized();
        CheckClosed();
        long n = _size - _pos;
        if (size < 0 || size > n)
        {
            size = n;
            if (size < 0)
                size = 0;
        }
        if (_accumulating && _pos == 0 && size == n)
        {
            _pos = _size;
            return PyString.From(_writer.ToString());
        }
        Realize();
        // Past the end, size is 0 and nothing is copied.
        var output = size == 0 ? "" : Text(_buf.AsSpan((int)_pos, (int)size));
        _pos += size;
        return PyString.From(output);
    }

    // _stringio_readline
    private string ReadLineRealized(long limit)
    {
        if (_pos >= _size)
            return "";
        int start = (int)_pos;
        if (limit < 0 || limit > _size - _pos)
            limit = _size - _pos;
        int len = PyLineEnding.Find<int>(_buf.AsSpan(start, (int)limit), _readTranslate, _readUniversal, _readnl, out _);
        if (len < 0)
            len = (int)limit;
        _pos += len;
        return Text(_buf.AsSpan(start, len));
    }

    private PyObject ReadLine(long limit)
    {
        CheckInitialized();
        CheckClosed();
        Realize();
        return PyString.From(ReadLineRealized(limit));
    }

    // stringio_iternext
    protected internal override PyObject IterNext()
    {
        CheckInitialized();
        CheckClosed();
        Realize();
        PyObject line;
        if (ReferenceEquals(GetPyType(), StringType))
            line = PyString.From(ReadLineRealized(-1));
        else
        {
            line = GetAttribute("readline").Call(Array.Empty<PyObject>(), null);
            if (line is not PyString)
                throw new PythonException(PyOSError.FromArguments(new PyObject[]
                {
                    PyString.From($"readline() should have returned a str object, not '{line.GetTypeName()}'"),
                }, null));
        }
        if (line.Length() == 0)
            throw PyStopIteration.Create();
        return line;
    }

    private PyObject Truncate(long size)
    {
        CheckInitialized();
        CheckClosed();
        if (size < 0)
            throw PyValueError.Create($"Negative size value {size}");
        if (size < _size)
        {
            Realize();
            Resize(size);
            _size = (int)size;
        }
        return new PyInt(size);
    }

    private PyObject Seek(long pos, int whence)
    {
        CheckInitialized();
        CheckClosed();
        if (whence != 0 && whence != 1 && whence != 2)
            throw PyValueError.Create($"Invalid whence ({whence}, should be 0, 1 or 2)");
        if (pos < 0 && whence == 0)
            throw PyValueError.Create($"Negative seek position {pos}");
        if (whence != 0 && pos != 0)
            throw new PythonException(PyOSError.FromArguments(new PyObject[] { PyString.From("Can't do nonzero cur-relative seeks") }, null));
        if (whence == 1)
            pos = _pos;
        else if (whence == 2)
            pos = _size;
        _pos = pos;
        return new PyInt(_pos);
    }

    private PyObject Write(PyObject value)
    {
        CheckInitialized();
        if (value is not PyString)
            throw PyTypeError.Create($"string argument expected, got '{value.GetTypeName()}'");
        CheckClosed();
        string text = value.ToStr().Value;
        int size = PyString.CodePointCount(text);
        if (size > 0)
            WriteStr(text);
        return new PyInt(size);
    }

    private PyObject Close()
    {
        _closed = true;
        Resize(0);
        _writer.Clear();
        _readnl = _writenl = null;
        _decoder = null;
        return PyNone.Instance;
    }

    private static readonly string[] InitNames = { "initial_value", "newline" };

    // _io_StringIO___init___impl
    internal void Initialize(PyObject[] args, PyDict kwargs)
    {
        var values = PyArgs.Unpack("StringIO", args, kwargs, InitNames, 0, 0, 2);
        PyObject value = values[0], newlineObject = values[1];
        string newline = "\n";
        if (newlineObject is PyNone)
            newline = null;
        else if (newlineObject != null)
        {
            if (newlineObject is not PyString text)
                throw PyTypeError.Create($"newline must be str or None, not {newlineObject.GetTypeName()}");
            // PyUnicode_AsUTF8 and a C string: an embedded null ends it.
            int nul = text.Value.IndexOf('\0');
            newline = nul < 0 ? text.Value : text.Value.Substring(0, nul);
        }
        if (newline != null && newline.Length != 0 && newline != "\n" && newline != "\r" && newline != "\r\n")
            throw PyValueError.Create($"illegal newline value: {newlineObject.ToRepr().Value}");
        if (value != null && value is not PyNone && value is not PyString)
            throw PyTypeError.Create($"initial_value must be str or None, not {value.GetTypeName()}");
        _ok = false;
        _writer.Clear();
        _readnl = _writenl = null;
        _decoder = null;
        if (newline != null)
            _readnl = newline;
        _readUniversal = newline == null || newline.Length == 0;
        _readTranslate = newline == null;
        if (newline != null && newline.StartsWith('\r'))
            _writenl = _readnl;
        if (_readUniversal)
            _decoder = (PyNewlineDecoder)PyNewlineDecoder.DecoderType.Call(new PyObject[] { PyNone.Instance, PyBool.FromBool(_readTranslate) }, null);
        _size = 0;
        string initial = value != null && value is not PyNone ? value.ToStr().Value : "";
        Resize(0);
        if (initial.Length > 0)
        {
            _accumulating = false;
            _pos = 0;
            WriteStr(initial);
        }
        else
            _accumulating = true;
        _pos = 0;
        _closed = false;
        _ok = true;
    }

    private PyObject GetState()
    {
        var value = PyString.From(GetValue());
        PyObject dict = ObjectDict(false) is { } own ? own.Copy() : PyNone.Instance;
        return new PyTuple(new PyObject[] { value, _readnl == null ? PyNone.Instance : PyString.From(_readnl), new PyInt(_pos), dict });
    }

    private static string TypeName(PyObject self) =>
        self.GetPyType() is PyIOBaseType native ? "_io." + native.Name : self.GetPyType().Name;

    // stringio_setstate
    private PyObject SetState(PyObject state)
    {
        CheckClosed();
        if (state is not PyTuple tuple || tuple.Items.Length < 4)
            throw PyTypeError.Create($"{TypeName(this)}.__setstate__ argument should be 4-tuple, got {state.GetTypeName()}");
        Initialize(new[] { tuple.Items[0], tuple.Items[1] }, null);
        var item = tuple.Items[0];
        if (item is PyString)
        {
            string text = item.ToStr().Value;
            int size = PyString.CodePointCount(text);
            Resize(size);
            CodePoints(text, _buf);
            _size = size;
        }
        else
            _size = 0;
        var position = tuple.Items[2];
        if (!PyInt.LongCheck(position))
            throw PyTypeError.Create($"third item of state must be an integer, got {position.GetTypeName()}");
        long pos = BytesMethods.AsSsize(position);
        if (pos < 0)
            throw PyValueError.Create("position value cannot be negative");
        _pos = pos;
        var dict = tuple.Items[3];
        if (dict is not PyNone)
        {
            if (dict is not PyDict entries)
                throw PyTypeError.Create($"fourth item of state should be a dict, got a {dict.GetTypeName()}");
            ObjectDict(true).Update(entries);
        }
        return PyNone.Instance;
    }

    public static void Register() { }

    private static PyIOBaseType MakeType()
    {
        var type = new PyIOBaseType("StringIO", new PyType[] { PyIOBaseSlots.TextIOBaseType });
        // stringio_new: not initialized until __init__ runs.
        type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) => new PyStringIO((PyType)cls),
            acceptsKwargs: true);
        type.TypeDict["__init__"] = new PyWrapperDescriptor("__init__", type, (self, args, kwargs) =>
        {
            Self(self, "__init__").Initialize(args, kwargs);
            return PyNone.Instance;
        }, 0, int.MaxValue, acceptsKwargs: true);

        void Def(string name, PyCall kind, Func<PyStringIO, PyObject[], PyObject> body, int min = 0, int max = 0) =>
            PyArgs.Define(type, name, kind, (self, args) => body(Self(self, name), args), min, max);
        PyObject Arg(PyObject[] args, int i) => args.Length > i ? args[i] : null;

        Def("close", PyCall.NoArgs, (self, args) => self.Close());
        Def("getvalue", PyCall.NoArgs, (self, args) => PyString.From(self.GetValue()));
        Def("read", PyCall.Fast, (self, args) => self.Read(PyArgs.OptionalSsize(Arg(args, 0))), 0, 1);
        Def("readline", PyCall.Fast, (self, args) => self.ReadLine(PyArgs.OptionalSsize(Arg(args, 0))), 0, 1);
        Def("tell", PyCall.NoArgs, (self, args) =>
        {
            self.CheckInitialized();
            self.CheckClosed();
            return new PyInt(self._pos);
        });
        Def("truncate", PyCall.Fast, (self, args) => self.Truncate(PyArgs.OptionalSsize(Arg(args, 0), self._pos)), 0, 1);
        Def("seek", PyCall.Fast, (self, args) =>
            self.Seek(PyArgs.Ssize(args[0]), args.Length > 1 ? PyArgs.Int(args[1]) : 0), 1, 2);
        Def("write", PyCall.One, (self, args) => self.Write(args[0]));
        foreach (var name in new[] { "seekable", "readable", "writable" })
            Def(name, PyCall.NoArgs, (self, args) =>
            {
                self.CheckInitialized();
                self.CheckClosed();
                return PyBool.True;
            });
        Def("__getstate__", PyCall.NoArgs, (self, args) => self.GetState());
        Def("__setstate__", PyCall.One, (self, args) => self.SetState(args[0]));
        type.TypeDict["__next__"] = new PyWrapperDescriptor("__next__", type, (self, args, kwargs) => Self(self, "__next__").IterNext(), 0, 0);
        type.TypeDict["closed"] = new PyGetSetDescriptor("closed", type, self =>
        {
            var stream = Self(self, "closed");
            stream.CheckInitialized();
            return PyBool.FromBool(stream._closed);
        });
        type.TypeDict["newlines"] = new PyGetSetDescriptor("newlines", type, self =>
        {
            var stream = Self(self, "newlines");
            stream.CheckInitialized();
            stream.CheckClosed();
            return stream._decoder == null ? PyNone.Instance : stream._decoder.GetAttribute("newlines");
        });
        type.TypeDict["line_buffering"] = new PyGetSetDescriptor("line_buffering", type, self =>
        {
            var stream = Self(self, "line_buffering");
            stream.CheckInitialized();
            stream.CheckClosed();
            return PyBool.False;
        });
        return type;
    }
}
