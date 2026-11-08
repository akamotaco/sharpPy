using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace SharpPy;

/// <summary>
/// Modules/_io/textio.c IncrementalNewlineDecoder: wraps a decoder, turns \r\n and \r into \n when
/// translating, and remembers which newlines it saw.
/// </summary>
public sealed class PyNewlineDecoder : PyIOBaseInstance
{
    private const int SeenCR = 1, SeenLF = 2, SeenCRLF = 4, SeenAll = 7;

    internal PyObject Decoder;
    private PyObject _errors;
    private bool _pendingCR, _translate;
    private int _seen;

    public static readonly PyIOBaseType DecoderType = MakeType();

    internal PyNewlineDecoder(PyType type) : base(type) { }

    private void CheckInitialized()
    {
        if (_errors == null)
            throw PyValueError.Create("IncrementalNewlineDecoder.__init__() not called");
    }

    internal static string CheckDecoded(PyObject decoded) => decoded is PyString text ? text.Value
        : throw PyTypeError.Create($"decoder should return a string result, not '{decoded.GetTypeName()}'");

    // _PyIncrementalNewlineDecoder_decode
    internal string Decode(PyObject input, bool final)
    {
        CheckInitialized();
        string output = Decoder is PyNone ? CheckDecoded(input)
            : CheckDecoded(Decoder.GetAttribute("decode").Call(new[] { input, PyBool.FromBool(final) }, null));
        if (_pendingCR && (final || output.Length > 0))
        {
            output = "\r" + output;
            _pendingCR = false;
        }
        if (!final && output.Length > 0 && output[output.Length - 1] == '\r')
        {
            output = output.Substring(0, output.Length - 1);
            _pendingCR = true;
        }
        if (output.Length == 0)
            return output;
        int seen = _seen;
        bool onlyLF = (seen == SeenLF || seen == 0) && output.IndexOf('\r') < 0;
        if (onlyLF)
        {
            if (seen == 0 && output.IndexOf('\n') >= 0)
                seen |= SeenLF;
        }
        else if (!_translate)
        {
            for (int i = 0; i < output.Length && seen != SeenAll; i++)
            {
                char c = output[i];
                if (c == '\n')
                    seen |= SeenLF;
                else if (c == '\r')
                {
                    if (i + 1 < output.Length && output[i + 1] == '\n')
                    {
                        seen |= SeenCRLF;
                        i++;
                    }
                    else
                        seen |= SeenCR;
                }
            }
        }
        else
        {
            var translated = new StringBuilder(output.Length);
            for (int i = 0; i < output.Length; i++)
            {
                char c = output[i];
                if (c == '\n')
                {
                    seen |= SeenLF;
                    translated.Append('\n');
                }
                else if (c == '\r')
                {
                    if (i + 1 < output.Length && output[i + 1] == '\n')
                    {
                        i++;
                        seen |= SeenCRLF;
                    }
                    else
                        seen |= SeenCR;
                    translated.Append('\n');
                }
                else
                    translated.Append(c);
            }
            output = translated.ToString();
        }
        _seen |= seen;
        return output;
    }

    private PyObject GetState()
    {
        CheckInitialized();
        PyObject buffer;
        BigInteger flag;
        if (Decoder is not PyNone)
        {
            if (Decoder.GetAttribute("getstate").Call(Array.Empty<PyObject>(), null) is not PyTuple { Items.Length: 2 } state)
                throw PyTypeError.Create("illegal decoder state");
            buffer = state.Items[0];
            flag = PyObject.TryGetIndex(state.Items[1])?.Value ?? throw PyTypeError.Create("illegal decoder state");
        }
        else
        {
            buffer = PyBytes.From(Array.Empty<byte>());
            flag = 0;
        }
        flag <<= 1;
        if (_pendingCR)
            flag |= 1;
        return PyTuple.From(new[] { buffer, PyInt.From(flag) });
    }

    private PyObject SetState(PyObject state)
    {
        CheckInitialized();
        if (state is not PyTuple tuple)
            throw PyTypeError.Create("state argument must be a tuple");
        if (tuple.Items.Length != 2 || PyObject.TryGetIndex(tuple.Items[1]) is not { } flagObject)
            throw PyTypeError.Create("setstate(): illegal state argument");
        var flag = flagObject.Value;
        _pendingCR = !(flag & 1).IsZero;
        flag >>= 1;
        if (Decoder is PyNone)
            return PyNone.Instance;
        return Decoder.GetAttribute("setstate").Call(new PyObject[] { PyTuple.From(new[] { tuple.Items[0], PyInt.From(flag) }) }, null);
    }

    private PyObject Reset()
    {
        CheckInitialized();
        _seen = 0;
        _pendingCR = false;
        return Decoder is PyNone ? PyNone.Instance : Decoder.GetAttribute("reset").Call(Array.Empty<PyObject>(), null);
    }

    private PyObject Newlines()
    {
        CheckInitialized();
        PyObject S(string text) => PyString.From(text);
        return _seen switch
        {
            SeenCR => S("\r"),
            SeenLF => S("\n"),
            SeenCRLF => S("\r\n"),
            SeenCR | SeenLF => PyTuple.From(new[] { S("\r"), S("\n") }),
            SeenCR | SeenCRLF => PyTuple.From(new[] { S("\r"), S("\r\n") }),
            SeenLF | SeenCRLF => PyTuple.From(new[] { S("\n"), S("\r\n") }),
            SeenAll => PyTuple.From(new[] { S("\r"), S("\n"), S("\r\n") }),
            _ => PyNone.Instance,
        };
    }

    private static PyObject Keyword(PyDict kwargs, string name)
    {
        if (kwargs != null)
            foreach (var pair in kwargs.InternalDict)
                if ((pair.Key as PyString)?.Value == name)
                    return pair.Value;
        return null;
    }

    private static PyIOBaseType MakeType()
    {
        var type = new PyIOBaseType("IncrementalNewlineDecoder", new[] { PyType.ObjectType });
        type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) => new PyNewlineDecoder((PyType)cls),
            acceptsKwargs: true);
        type.TypeDict["__init__"] = new PyWrapperDescriptor("__init__", type, (self, args, kwargs) =>
        {
            var decoder = (PyNewlineDecoder)self;
            PyObject inner = args.Length > 0 ? args[0] : null, translate = args.Length > 1 ? args[1] : null, errors = args.Length > 2 ? args[2] : null;
            if (kwargs != null)
                foreach (var pair in kwargs.InternalDict)
                    switch ((pair.Key as PyString)?.Value)
                    {
                        case "decoder": inner = pair.Value; break;
                        case "translate": translate = pair.Value; break;
                        case "errors": errors = pair.Value; break;
                        default: throw PyTypeError.Create($"'{pair.Key.ToStr().Value}' is an invalid keyword argument for IncrementalNewlineDecoder()");
                    }
            if (inner == null || translate == null)
                throw PyTypeError.Create($"IncrementalNewlineDecoder() missing required argument '{(inner == null ? "decoder" : "translate")}' (pos {(inner == null ? 1 : 2)})");
            decoder._errors = errors ?? PyString.From("strict");
            decoder.Decoder = inner;
            decoder._translate = translate.IsTrue();
            decoder._seen = 0;
            decoder._pendingCR = false;
            return PyNone.Instance;
        }, 0, int.MaxValue, acceptsKwargs: true);
        type.TypeDict["decode"] = new PyMethodDescriptor("decode", type, (self, args, kwargs) =>
        {
            PyObject input = args.Length > 0 ? args[0] : Keyword(kwargs, "input");
            PyObject final = args.Length > 1 ? args[1] : Keyword(kwargs, "final");
            if (input == null)
                throw PyTypeError.Create("decode() missing required argument 'input' (pos 1)");
            return PyString.From(((PyNewlineDecoder)self).Decode(input, final != null && final.IsTrue()));
        }, acceptsKwargs: true);
        type.TypeDict["getstate"] = new PyMethodDescriptor("getstate", type, (self, args, kwargs) => ((PyNewlineDecoder)self).GetState());
        type.TypeDict["setstate"] = new PyMethodDescriptor("setstate", type, (self, args, kwargs) => ((PyNewlineDecoder)self).SetState(args[0]), 1, 1);
        type.TypeDict["reset"] = new PyMethodDescriptor("reset", type, (self, args, kwargs) => ((PyNewlineDecoder)self).Reset());
        type.TypeDict["newlines"] = new PyGetSetDescriptor("newlines", type, self => ((PyNewlineDecoder)self).Newlines());
        return type;
    }
}

/// <summary>
/// Modules/_io/textio.c _PyIO_find_line_ending, for UTF-16 text and for StringIO's code points:
/// the length of the first line of text with its ending, or -1 with how much can be put aside.
/// </summary>
internal static class PyLineEnding
{
    internal static int Find<T>(ReadOnlySpan<T> text, bool translated, bool universal, ReadOnlySpan<char> readnl, out int consumed)
        where T : unmanaged, IBinaryInteger<T>
    {
        consumed = 0;
        int len = text.Length;
        T lf = T.CreateTruncating('\n'), cr = T.CreateTruncating('\r');
        if (translated)
        {
            int at = text.IndexOf(lf);
            if (at >= 0)
                return at + 1;
            consumed = len;
            return -1;
        }
        if (universal)
        {
            for (int s = 0; ; )
            {
                while (s < len && text[s] > cr)
                    s++;
                if (s >= len)
                {
                    consumed = len;
                    return -1;
                }
                T ch = text[s++];
                if (ch == lf)
                    return s;
                if (ch == cr)
                    return s < len && text[s] == lf ? s + 1 : s;
            }
        }
        T first = T.CreateTruncating(readnl[0]);
        if (readnl.Length == 1)
        {
            int at = text.IndexOf(first);
            if (at >= 0)
                return at + 1;
            consumed = len;
            return -1;
        }
        int e = Math.Max(len - (readnl.Length - 1), 0);
        for (int s = 0; s < e; )
        {
            int pos = text.Slice(s).IndexOf(first);
            if (pos < 0 || s + pos >= e)
                break;
            pos += s;
            int i = 1;
            while (i < readnl.Length && text[pos + i] == T.CreateTruncating(readnl[i]))
                i++;
            if (i == readnl.Length)
                return pos + readnl.Length;
            s = pos + 1;
        }
        int tail = text.Slice(e).IndexOf(first);
        consumed = tail < 0 ? len : e + tail;
        return -1;
    }
}

/// <summary>Modules/_io/textio.c TextIOWrapper.</summary>
public sealed class PyTextIOWrapper : PyIOBaseInstance
{
    private bool _ok, _detached;
    private int _chunkSize = 8192;
    private PyObject _buffer, _encoding, _encoder, _decoder, _readnl, _errors;
    private bool _lineBuffering, _writeThrough, _readUniversal, _readTranslate, _writeTranslate;
    private bool _seekable, _hasRead1, _telling, _finalizing, _encodingStartOfStream;
    private string _writenl;
    // decoded_chars, and how far it was read: in code points as Python counts, and in UTF-16 units.
    private string _decoded;
    private int _usedPoints, _usedUnits;
    private List<byte[]> _pending;
    private long _pendingCount;
    private PyTuple _snapshot;
    private double _b2cratio;
    private PyFileIO _raw;

    public static readonly PyIOBaseType WrapperType = MakeType();

    internal PyTextIOWrapper(PyType type) : base(type) { }

    private static PyObject Call(PyObject target, string name, params PyObject[] args) => target.GetAttribute(name).Call(args, null);

    private void CheckInitialized()
    {
        if (!_ok)
            throw PyValueError.Create("I/O operation on uninitialized object");
    }

    // CHECK_ATTACHED
    private void CheckAttached()
    {
        CheckInitialized();
        if (_detached)
            throw PyValueError.Create("underlying buffer has been detached");
    }

    // CHECK_CLOSED
    private void CheckClosed()
    {
        if (ReferenceEquals(GetPyType(), WrapperType))
        {
            bool closed = _raw != null ? _raw.Fd < 0 : ClosedValue().IsTrue();
            if (closed)
                throw PyValueError.Create("I/O operation on closed file.");
        }
        else if (GetAttribute("closed").IsTrue())
            throw PyValueError.Create("I/O operation on closed file.");
    }

    private PyObject ClosedValue()
    {
        CheckAttached();
        return _buffer.GetAttribute("closed");
    }

    private static PythonException Unsupported(string message) => PyUnsupportedOperation.Create(message);

    // validate_newline
    private static void ValidateNewline(string newline)
    {
        if (newline != null && newline.Length != 0 && newline != "\n" && newline != "\r" && newline != "\r\n")
            throw PyValueError.Create($"illegal newline value: {newline}");
    }

    // set_newline
    private void SetNewline(string newline)
    {
        _readnl = newline == null ? null : PyString.From(newline);
        _readUniversal = newline == null || newline.Length == 0;
        _readTranslate = newline == null;
        _writeTranslate = newline == null || newline.Length != 0;
        if (!_readUniversal && _readnl != null)
            _writenl = newline == "\n" ? null : newline;
        else
            _writenl = OperatingSystem.IsWindows() ? "\r\n" : null;
    }

    // _PyCodec_LookupTextEncoding
    private static PyObject CodecInfo(string encoding)
    {
        var info = PyImportSystem.Import("codecs").GetAttribute("lookup").Call(new PyObject[] { PyString.From(encoding) }, null);
        var isText = info.LookupAttribute("_is_text_encoding");
        if (isText != null && !isText.IsTrue())
            throw PyLookupError.Create($"'{encoding}' is not a text encoding; use codecs.open() to handle arbitrary codecs");
        return info;
    }

    private void SetDecoder(PyObject codecInfo, string errors)
    {
        if (!Call(_buffer, "readable").IsTrue())
            return;
        _decoder = codecInfo.GetAttribute("incrementaldecoder").Call(new PyObject[] { PyString.From(errors) }, null);
        if (_readUniversal)
            _decoder = PyNewlineDecoder.DecoderType.Call(new[] { _decoder, PyBool.FromBool(_readTranslate) }, null);
    }

    private void SetEncoder(PyObject codecInfo, string errors)
    {
        if (!Call(_buffer, "writable").IsTrue())
            return;
        _encoder = codecInfo.GetAttribute("incrementalencoder").Call(new PyObject[] { PyString.From(errors) }, null);
    }

    // _textiowrapper_fix_encoder_state
    private void FixEncoderState()
    {
        if (!_seekable || _encoder == null)
            return;
        _encodingStartOfStream = true;
        var cookie = Call(_buffer, "tell");
        if (!PyObject.CompareBool(cookie, PyInt.From(0), PyObject.CompareOp.EQ))
        {
            _encodingStartOfStream = false;
            Call(_encoder, "setstate", PyInt.From(0));
        }
    }

    private static string OptionalText(PyObject value, string what)
    {
        if (value == null || value is PyNone)
            return null;
        if (value is not PyString text)
            throw PyTypeError.Create($"TextIOWrapper() argument '{what}' must be str or None, not {value.GetTypeName()}");
        if (text.Value.Contains('\0'))
            throw PyValueError.Create("embedded null character");
        return text.Value;
    }

    // _io_TextIOWrapper___init___impl
    private void Initialize(PyObject[] args, PyDict kwargs)
    {
        var names = new[] { "buffer", "encoding", "errors", "newline", "line_buffering", "write_through" };
        var values = new PyObject[names.Length];
        if (args.Length > names.Length)
            throw PyTypeError.Create($"TextIOWrapper() takes at most 6 arguments ({args.Length} given)");
        Array.Copy(args, values, args.Length);
        if (kwargs != null)
            foreach (var pair in kwargs.InternalDict)
            {
                string key = (pair.Key as PyString)?.Value;
                int index = Array.IndexOf(names, key);
                if (index < 0)
                    throw PyTypeError.Create($"'{key}' is an invalid keyword argument for TextIOWrapper()");
                if (values[index] != null)
                    throw PyTypeError.Create($"argument for TextIOWrapper() given by name ('{key}') and position ({index + 1})");
                values[index] = pair.Value;
            }
        var buffer = values[0] ?? throw PyTypeError.Create("TextIOWrapper() missing required argument 'buffer' (pos 1)");
        string encoding = OptionalText(values[1], "encoding");
        var errorsObject = values[2] ?? PyNone.Instance;
        string newline = OptionalText(values[3], "newline");
        bool lineBuffering = values[4] != null && values[4].IsTrue();
        bool writeThrough = values[5] != null && values[5].IsTrue();

        _ok = false;
        _detached = false;
        if (errorsObject is PyNone)
            errorsObject = PyString.From("strict");
        else if (errorsObject is not PyString)
            throw PyTypeError.Create($"TextIOWrapper() argument 'errors' must be str or None, not {errorsObject.GetTypeName()}");
        string errors = ((PyString)errorsObject).Value;
        if (errors.Contains('\0'))
            throw PyValueError.Create("embedded null character");
        ValidateNewline(newline);

        if (_buffer != null && !ReferenceEquals(_buffer, buffer))
            PyFinalizer.FinalizeAfter(_buffer, null);
        _buffer = _encoding = _encoder = _decoder = _readnl = null;
        _decoded = null;
        _usedPoints = _usedUnits = 0;
        _pending = null;
        _pendingCount = 0;
        _snapshot = null;
        _raw = null;
        _b2cratio = 0.0;
        // _Py_GetLocaleEncodingObject: SharpPy's locale is UTF-8, on every platform Godot runs on.
        if (encoding == null || encoding == "locale")
            encoding = "utf-8";
        _encoding = PyString.From(encoding);
        var codecInfo = CodecInfo(encoding);
        _errors = errorsObject;
        _chunkSize = 8192;
        _lineBuffering = lineBuffering;
        _writeThrough = writeThrough;
        SetNewline(newline);
        _buffer = buffer;
        SetDecoder(codecInfo, errors);
        SetEncoder(codecInfo, errors);
        if (buffer is PyBuffered buffered && (ReferenceEquals(buffered.GetPyType(), PyBuffered.ReaderType) || ReferenceEquals(buffered.GetPyType(), PyBuffered.WriterType)
            || ReferenceEquals(buffered.GetPyType(), PyBuffered.RandomType)) && buffered.Raw is PyFileIO file && ReferenceEquals(file.GetPyType(), PyFileIO.FileIOType))
            _raw = file;
        _seekable = _telling = Call(buffer, "seekable").IsTrue();
        _hasRead1 = buffer.LookupAttribute("read1") != null;
        _encodingStartOfStream = false;
        FixEncoderState();
        _ok = true;
        // textiowrapper_dealloc and iobase_finalize: closed when abandoned, before its buffer.
        PyFinalizer.Attach(this);
        PyFinalizer.FinalizeAfter(buffer, this);
    }

    // textiowrapper_change_encoding
    private void ChangeEncoding(PyObject encoding, PyObject errors, bool newlineChanged)
    {
        if (encoding is PyNone && errors is PyNone && !newlineChanged)
            return;
        if (encoding is PyNone)
        {
            encoding = _encoding;
            if (errors is PyNone)
                errors = _errors;
        }
        else
        {
            if (((PyString)encoding).Value == "locale")
                encoding = PyString.From("utf-8");
            if (errors is PyNone)
                errors = PyString.From("strict");
        }
        var codecInfo = CodecInfo(((PyString)encoding).Value);
        SetDecoder(codecInfo, ((PyString)errors).Value);
        SetEncoder(codecInfo, ((PyString)errors).Value);
        _encoding = encoding;
        _errors = errors;
        FixEncoderState();
    }

    // _io_TextIOWrapper_reconfigure_impl
    private PyObject Reconfigure(PyObject[] args, PyDict kwargs)
    {
        if (args.Length != 0)
            throw PyTypeError.Create("reconfigure() takes no positional arguments");
        PyObject encoding = PyNone.Instance, errors = PyNone.Instance, newline = null, lineBuffering = PyNone.Instance, writeThrough = PyNone.Instance;
        if (kwargs != null)
            foreach (var pair in kwargs.InternalDict)
                switch ((pair.Key as PyString)?.Value)
                {
                    case "encoding": encoding = pair.Value; break;
                    case "errors": errors = pair.Value; break;
                    case "newline": newline = pair.Value; break;
                    case "line_buffering": lineBuffering = pair.Value; break;
                    case "write_through": writeThrough = pair.Value; break;
                    default: throw PyTypeError.Create($"'{pair.Key.ToStr().Value}' is an invalid keyword argument for reconfigure()");
                }
        if (encoding is not PyNone && encoding is not PyString)
            throw PyTypeError.Create($"reconfigure() argument 'encoding' must be str or None, not {encoding.GetTypeName()}");
        if (errors is not PyNone && errors is not PyString)
            throw PyTypeError.Create($"reconfigure() argument 'errors' must be str or None, not {errors.GetTypeName()}");
        if (newline != null && newline is not PyNone && newline is not PyString)
            throw PyTypeError.Create($"reconfigure() argument 'newline' must be str or None, not {newline.GetTypeName()}");
        if (_decoded != null && (encoding is not PyNone || errors is not PyNone || newline != null))
            throw Unsupported("It is not possible to set the encoding or newline of stream after the first read");
        string newlineText = newline is PyString text ? text.Value : null;
        if (newlineText != null)
            ValidateNewline(newlineText);
        bool line = lineBuffering is PyNone ? _lineBuffering : PyArgs.CLong(lineBuffering) != 0;
        bool through = writeThrough is PyNone ? _writeThrough : PyArgs.CLong(writeThrough) != 0;
        Call(this, "flush");
        _b2cratio = 0;
        if (newline != null)
            SetNewline(newlineText);
        ChangeEncoding(encoding, errors, newline != null);
        _lineBuffering = line;
        _writeThrough = through;
        return PyNone.Instance;
    }

    // _textiowrapper_writeflush
    private void WriteFlush()
    {
        if (_pending == null)
            return;
        var data = new byte[_pendingCount];
        int at = 0;
        foreach (var chunk in _pending)
        {
            Array.Copy(chunk, 0, data, at, chunk.Length);
            at += chunk.Length;
        }
        _pending = null;
        _pendingCount = 0;
        Call(_buffer, "write", PyBytes.From(data));
    }

    private static PyObject TextArgument(PyObject value, string method) => value is PyString ? value
        : throw PyTypeError.Create($"{method}() argument must be str, not {value.GetTypeName()}");

    // _io_TextIOWrapper_write_impl
    private PyObject Write(PyObject textObject)
    {
        CheckAttached();
        CheckClosed();
        if (_encoder == null)
            throw Unsupported("not writable");
        string text = textObject.ToStr().Value;
        long textLength = PyString.CodePointCount(text);
        bool hasLF = false, needFlush = false, textNeedFlush = false;
        if ((_writeTranslate && _writenl != null) || _lineBuffering)
            hasLF = text.IndexOf('\n') >= 0;
        if (hasLF && _writeTranslate && _writenl != null)
            text = text.Replace("\n", _writenl);
        if (_writeThrough)
            textNeedFlush = true;
        if (_lineBuffering && (hasLF || text.IndexOf('\r') >= 0))
            needFlush = true;
        var encoded = Call(_encoder, "encode", PyString.From(text));
        _encodingStartOfStream = false;
        if (encoded is not PyBytes bytes)
            throw PyTypeError.Create($"encoder should return a bytes object, not '{encoded.GetTypeName()}'");
        if (bytes.Value.Length >= _chunkSize)
            while (_pending != null)
                WriteFlush();
        (_pending ??= new List<byte[]>()).Add(bytes.Value);
        _pendingCount += bytes.Value.Length;
        if (_pendingCount >= _chunkSize || needFlush || textNeedFlush)
            WriteFlush();
        if (needFlush)
            Call(_buffer, "flush");
        if (_snapshot != null)
        {
            SetDecodedChars(null);
            _snapshot = null;
        }
        if (_decoder != null)
            Call(_decoder, "reset");
        return PyInt.From(textLength);
    }

    private void SetDecodedChars(string chars)
    {
        _decoded = chars;
        _usedPoints = _usedUnits = 0;
    }

    // Moves the read position of decoded_chars by code points.
    private int UnitsAfter(int start, int points)
    {
        int at = start;
        for (int i = 0; i < points && at < _decoded.Length; i++)
            at += char.IsHighSurrogate(_decoded[at]) && at + 1 < _decoded.Length && char.IsLowSurrogate(_decoded[at + 1]) ? 2 : 1;
        return at;
    }

    // textiowrapper_get_decoded_chars
    private string GetDecodedChars(long n)
    {
        if (_decoded == null)
            return "";
        int end = n < 0 ? _decoded.Length : UnitsAfter(_usedUnits, (int)Math.Min(n, int.MaxValue));
        string chars = _decoded.Substring(_usedUnits, end - _usedUnits);
        _usedPoints += PyString.CodePointCount(chars);
        _usedUnits = end;
        return chars;
    }

    private string Decode(PyObject bytes, bool eof) => _decoder is PyNewlineDecoder newline
        ? newline.Decode(bytes, eof)
        : PyNewlineDecoder.CheckDecoded(Call(_decoder, "decode", bytes, PyBool.FromBool(eof)));

    // textiowrapper_read_chunk: whether there is more to read.
    private bool ReadChunk(long sizeHint)
    {
        if (_decoder == null)
            throw Unsupported("not readable");
        PyObject decBuffer = null, decFlags = null;
        if (_telling)
        {
            if (Call(_decoder, "getstate") is not PyTuple { Items.Length: 2 } state)
                throw PyTypeError.Create("illegal decoder state");
            decBuffer = state.Items[0];
            decFlags = state.Items[1];
            if (decBuffer is not PyBytes)
                throw PyTypeError.Create($"illegal decoder state: the first item should be a bytes object, not '{decBuffer.GetTypeName()}'");
        }
        if (sizeHint > 0)
            sizeHint = (long)(Math.Max(_b2cratio, 1.0) * sizeHint);
        var input = Call(_buffer, _hasRead1 ? "read1" : "read", PyInt.From(Math.Max(_chunkSize, sizeHint)));
        var inputBytes = BytesMethods.Buffer(input)
            ?? throw PyTypeError.Create($"underlying {(_hasRead1 ? "read1" : "read")}() should have returned a bytes-like object, not '{input.GetTypeName()}'");
        int nbytes = inputBytes.Length;
        bool eof = nbytes == 0;
        string decoded = Decode(input, eof);
        SetDecodedChars(decoded);
        long nchars = PyString.CodePointCount(decoded);
        _b2cratio = nchars > 0 ? (double)nbytes / nchars : 0.0;
        if (nchars > 0)
            eof = false;
        if (_telling)
        {
            var previous = ((PyBytes)decBuffer).Value;
            var next = new byte[previous.Length + nbytes];
            Array.Copy(previous, next, previous.Length);
            Array.Copy(inputBytes, 0, next, previous.Length, nbytes);
            _snapshot = PyTuple.From(new[] { decFlags, PyBytes.From(next) });
        }
        return !eof;
    }

    // _io_TextIOWrapper_read_impl
    private PyObject Read(long n)
    {
        CheckAttached();
        CheckClosed();
        if (_decoder == null)
            throw Unsupported("not readable");
        WriteFlush();
        if (n < 0)
        {
            var bytes = Call(_buffer, "read");
            string decoded = Decode(bytes, true);
            string result = GetDecodedChars(-1) + decoded;
            if (_snapshot != null)
            {
                SetDecodedChars(null);
                _snapshot = null;
            }
            return PyString.From(result);
        }
        var first = GetDecodedChars(n);
        long remaining = n - PyString.CodePointCount(first);
        if (remaining <= 0)
            return PyString.From(first);
        var chunks = new StringBuilder(first);
        while (remaining > 0)
        {
            if (!ReadChunk(remaining))
                break;
            string piece = GetDecodedChars(remaining);
            chunks.Append(piece);
            remaining -= PyString.CodePointCount(piece);
        }
        return PyString.From(chunks.ToString());
    }

    // The end of the line after start, or -1 with how much was consumed.
    private int FindLineEnding(string text, int start, out int consumed)
    {
        int found = PyLineEnding.Find(text.AsSpan(start), _readTranslate, _readUniversal,
            _readnl is PyString nl ? nl.Value.AsSpan() : default, out int taken);
        consumed = start + taken;
        return found < 0 ? -1 : start + found;
    }

    // _textiowrapper_readline, with limit in code points.
    private PyObject ReadLine(long limit)
    {
        CheckClosed();
        WriteFlush();
        long chunked = 0;
        string line = null, remaining = null;
        int start = 0, endpos = 0, offsetToBuffer = 0;
        var chunks = new StringBuilder();
        bool haveChunks = false;
        while (true)
        {
            bool more = true;
            while (_decoded == null || _decoded.Length == 0)
            {
                more = ReadChunk(0);
                if (!more)
                    break;
            }
            if (!more)
            {
                SetDecodedChars(null);
                _snapshot = null;
                start = endpos = offsetToBuffer = 0;
                line = null;
                break;
            }
            if (remaining == null)
            {
                line = _decoded;
                start = _usedUnits;
                offsetToBuffer = 0;
            }
            else
            {
                line = remaining + _decoded;
                start = 0;
                offsetToBuffer = remaining.Length;
                remaining = null;
            }
            endpos = FindLineEnding(line, start, out int consumed);
            if (endpos >= 0)
            {
                if (limit >= 0 && CodePoints(line, start, endpos) + chunked >= limit)
                    endpos = AdvancePoints(line, start, limit - chunked);
                break;
            }
            endpos = consumed;
            if (limit >= 0 && CodePoints(line, start, endpos) + chunked >= limit)
            {
                endpos = AdvancePoints(line, start, limit - chunked);
                break;
            }
            if (endpos > start)
            {
                string piece = line.Substring(start, endpos - start);
                chunks.Append(piece);
                haveChunks = true;
                chunked += PyString.CodePointCount(piece);
            }
            if (endpos < line.Length)
                remaining = line.Substring(endpos);
            line = null;
            SetDecodedChars(null);
        }
        if (line != null)
        {
            _usedUnits = endpos - offsetToBuffer;
            _usedPoints = _decoded == null ? 0 : PyString.CodePointCount(_decoded.Substring(0, _usedUnits));
            if (start > 0 || endpos < line.Length)
                line = line.Substring(start, endpos - start);
        }
        if (remaining != null)
        {
            chunks.Append(remaining);
            haveChunks = true;
        }
        if (haveChunks)
        {
            if (line != null)
                chunks.Append(line);
            line = chunks.ToString();
        }
        return PyString.From(line ?? "");
    }

    private static long CodePoints(string text, int start, int end) => PyString.CodePointCount(text.Substring(start, end - start));

    private static int AdvancePoints(string text, int start, long points)
    {
        int at = start;
        for (long i = 0; i < points && at < text.Length; i++)
            at += char.IsHighSurrogate(text[at]) && at + 1 < text.Length && char.IsLowSurrogate(text[at + 1]) ? 2 : 1;
        return at;
    }

    // The cookie: start_pos (8 bytes), dec_flags, bytes_to_feed, chars_to_skip (4 each), need_eof (1),
    // as one little-endian unsigned integer.
    private struct Cookie
    {
        internal long StartPos;
        internal int DecFlags, BytesToFeed, CharsToSkip;
        internal bool NeedEof;
    }

    private static Cookie ParseCookie(PyObject cookieObject)
    {
        var value = (PyObject.TryGetIndex(PyType.IntType.Call(new[] { cookieObject }, null)) ?? throw PyTypeError.Create("an integer is required")).Value;
        var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: false);
        if (value.Sign < 0 || bytes.Length > 21)
            throw PyOverflowError.Create("int too big to convert");
        var buffer = new byte[21];
        Array.Copy(bytes, buffer, bytes.Length);
        return new Cookie
        {
            StartPos = BitConverter.ToInt64(buffer, 0),
            DecFlags = BitConverter.ToInt32(buffer, 8),
            BytesToFeed = BitConverter.ToInt32(buffer, 12),
            CharsToSkip = BitConverter.ToInt32(buffer, 16),
            NeedEof = buffer[20] != 0,
        };
    }

    private static PyObject BuildCookie(Cookie cookie)
    {
        var buffer = new byte[21];
        BitConverter.GetBytes(cookie.StartPos).CopyTo(buffer, 0);
        BitConverter.GetBytes(cookie.DecFlags).CopyTo(buffer, 8);
        BitConverter.GetBytes(cookie.BytesToFeed).CopyTo(buffer, 12);
        BitConverter.GetBytes(cookie.CharsToSkip).CopyTo(buffer, 16);
        buffer[20] = (byte)(cookie.NeedEof ? 1 : 0);
        return PyInt.From(new BigInteger(buffer, isUnsigned: true, isBigEndian: false));
    }

    private void DecoderSetState(Cookie cookie)
    {
        if (cookie.StartPos == 0 && cookie.DecFlags == 0)
            Call(_decoder, "reset");
        else
            Call(_decoder, "setstate", PyTuple.From(new PyObject[] { PyBytes.From(Array.Empty<byte>()), PyInt.From(cookie.DecFlags) }));
    }

    private void EncoderReset(bool startOfStream)
    {
        if (startOfStream)
        {
            Call(_encoder, "reset");
            _encodingStartOfStream = true;
        }
        else
        {
            Call(_encoder, "setstate", PyInt.From(0));
            _encodingStartOfStream = false;
        }
    }

    // _io_TextIOWrapper_seek_impl
    private PyObject Seek(PyObject cookieObject, int whence)
    {
        CheckAttached();
        CheckClosed();
        if (!_seekable)
            throw Unsupported("underlying stream is not seekable");
        var zero = PyInt.From(0);
        switch (whence)
        {
            case 1:
                if (!PyObject.CompareBool(cookieObject, zero, PyObject.CompareOp.EQ))
                    throw Unsupported("can't do nonzero cur-relative seeks");
                cookieObject = Call(this, "tell");
                break;
            case 2:
            {
                if (!PyObject.CompareBool(cookieObject, zero, PyObject.CompareOp.EQ))
                    throw Unsupported("can't do nonzero end-relative seeks");
                Call(this, "flush");
                SetDecodedChars(null);
                _snapshot = null;
                if (_decoder != null)
                    Call(_decoder, "reset");
                var result = Call(_buffer, "seek", zero, PyInt.From(2));
                if (_encoder != null)
                    EncoderReset(PyObject.CompareBool(result, zero, PyObject.CompareOp.EQ));
                return result;
            }
            case 0:
                break;
            default:
                throw PyValueError.Create($"invalid whence ({whence}, should be 0, 1 or 2)");
        }
        if (PyObject.CompareBool(cookieObject, zero, PyObject.CompareOp.LT))
            throw PyValueError.Create($"negative seek position {cookieObject.ToRepr().Value}");
        Call(this, "flush");
        var cookie = ParseCookie(cookieObject);
        Call(_buffer, "seek", PyInt.From(cookie.StartPos));
        SetDecodedChars(null);
        _snapshot = null;
        if (_decoder != null)
            DecoderSetState(cookie);
        if (cookie.CharsToSkip != 0)
        {
            var input = Call(_buffer, "read", PyInt.From(cookie.BytesToFeed));
            if (input is not PyBytes)
                throw PyTypeError.Create($"underlying read() should have returned a bytes object, not '{input.GetTypeName()}'");
            _snapshot = PyTuple.From(new[] { PyInt.From(cookie.DecFlags), input });
            string decoded = PyNewlineDecoder.CheckDecoded(Call(_decoder, "decode", input, PyBool.FromBool(cookie.NeedEof)));
            SetDecodedChars(decoded);
            if (PyString.CodePointCount(decoded) < cookie.CharsToSkip)
                throw new PythonException(PyOSError.FromArguments(new PyObject[] { PyString.From("can't restore logical file position") }, null));
            _usedPoints = cookie.CharsToSkip;
            _usedUnits = UnitsAfter(0, cookie.CharsToSkip);
        }
        else
            _snapshot = PyTuple.From(new PyObject[] { PyInt.From(cookie.DecFlags), PyBytes.From(Array.Empty<byte>()) });
        if (_encoder != null)
            EncoderReset(cookie.StartPos == 0 && cookie.DecFlags == 0);
        return cookieObject;
    }

    // DECODER_GETSTATE
    private (int Length, int Flags) DecoderState()
    {
        if (Call(_decoder, "getstate") is not PyTuple { Items.Length: 2 } state)
            throw PyTypeError.Create("illegal decoder state");
        if (state.Items[0] is not PyBytes buffer)
            throw PyTypeError.Create($"illegal decoder state: the first item should be a bytes object, not '{state.Items[0].GetTypeName()}'");
        return (buffer.Value.Length, (int)(PyObject.TryGetIndex(state.Items[1])?.Value ?? 0));
    }

    private long DecodeCount(byte[] input, long start, long length)
    {
        var part = new byte[length];
        Array.Copy(input, start, part, 0, length);
        return PyString.CodePointCount(PyNewlineDecoder.CheckDecoded(Call(_decoder, "decode", PyBytes.From(part))));
    }

    // _io_TextIOWrapper_tell_impl
    private PyObject Tell()
    {
        CheckAttached();
        CheckClosed();
        if (!_seekable)
            throw Unsupported("underlying stream is not seekable");
        if (!_telling)
            throw new PythonException(PyOSError.FromArguments(new PyObject[] { PyString.From("telling position disabled by next() call") }, null));
        WriteFlush();
        Call(this, "flush");
        var position = Call(_buffer, "tell");
        if (_decoder == null || _snapshot == null)
            return position;
        var cookie = new Cookie { StartPos = (long)PyObject.TryGetIndex(position).Value };
        cookie.DecFlags = (int)PyObject.TryGetIndex(_snapshot.Items[0]).Value;
        var nextInput = ((PyBytes)_snapshot.Items[1]).Value;
        cookie.StartPos -= nextInput.Length;
        if (_usedPoints == 0)
            return BuildCookie(cookie);
        long charsToSkip = _usedPoints;
        var saved = Call(_decoder, "getstate");
        try
        {
            long skipBytes = (long)(_b2cratio * charsToSkip), skipBack = 1;
            while (skipBytes > 0)
            {
                DecoderSetState(cookie);
                long decodedCount = DecodeCount(nextInput, 0, Math.Min(skipBytes, nextInput.Length));
                if (decodedCount <= charsToSkip)
                {
                    var (length, flags) = DecoderState();
                    if (length == 0)
                    {
                        cookie.DecFlags = flags;
                        charsToSkip -= decodedCount;
                        break;
                    }
                    skipBytes -= length;
                    skipBack = 1;
                }
                else
                {
                    skipBytes -= skipBack;
                    skipBack *= 2;
                }
            }
            if (skipBytes <= 0)
            {
                skipBytes = 0;
                DecoderSetState(cookie);
            }
            cookie.StartPos += skipBytes;
            cookie.CharsToSkip = (int)charsToSkip;
            if (charsToSkip != 0)
            {
                long charsDecoded = 0;
                long input = skipBytes;
                while (input < nextInput.Length)
                {
                    charsDecoded += DecodeCount(nextInput, input, 1);
                    cookie.BytesToFeed += 1;
                    var (length, flags) = DecoderState();
                    if (length == 0 && charsDecoded <= charsToSkip)
                    {
                        cookie.StartPos += cookie.BytesToFeed;
                        charsToSkip -= charsDecoded;
                        cookie.DecFlags = flags;
                        cookie.BytesToFeed = 0;
                        charsDecoded = 0;
                    }
                    if (charsDecoded >= charsToSkip)
                        break;
                    input++;
                }
                if (input == nextInput.Length)
                {
                    charsDecoded += PyString.CodePointCount(PyNewlineDecoder.CheckDecoded(
                        Call(_decoder, "decode", PyBytes.From(Array.Empty<byte>()), PyBool.True)));
                    cookie.NeedEof = true;
                    if (charsDecoded < charsToSkip)
                        throw new PythonException(PyOSError.FromArguments(new PyObject[] { PyString.From("can't reconstruct logical file position") }, null));
                }
            }
        }
        finally
        {
            Call(_decoder, "setstate", saved);
        }
        cookie.CharsToSkip = (int)charsToSkip;
        return BuildCookie(cookie);
    }

    private PyObject Close()
    {
        CheckAttached();
        if (ClosedValue().IsTrue())
            return PyNone.Instance;
        if (_finalizing)
        {
            try { Call(_buffer, "_dealloc_warn", this); }
            catch (PythonException) { }
        }
        PythonException failure = null;
        try
        {
            Call(this, "flush");
        }
        catch (PythonException error)
        {
            failure = error;
        }
        PyObject result;
        try
        {
            result = Call(_buffer, "close");
        }
        catch (PythonException error) when (failure != null)
        {
            error.PyException.SetAttribute("__context__", failure.PyException);
            throw;
        }
        if (failure != null)
            throw failure;
        return result;
    }

    // textiowrapper_repr
    protected internal override PyString Repr()
    {
        CheckInitialized();
        string typeName = "_io.TextIOWrapper";
        if (!PyReprGuard.Enter(this))
            throw PyRuntimeError.Create($"reentrant call inside {GetPyType().FullName}.__repr__");
        try
        {
            var result = new StringBuilder(typeName);
            result.Insert(0, '<');
            PyObject name = null;
            try { name = GetAttribute("name"); }
            catch (PythonException error) when (error.PyException is PyValueError or PyAttributeError) { }
            if (name != null)
                result.Append(" name=").Append(name.ToRepr().Value);
            var mode = LookupAttribute("mode");
            if (mode != null)
                result.Append(" mode=").Append(mode.ToRepr().Value);
            return PyString.From(result.Append(" encoding=").Append(_encoding.ToRepr().Value).Append('>').ToString());
        }
        finally
        {
            PyReprGuard.Leave(this);
        }
    }

    // textiowrapper_iternext
    protected internal override PyObject IterNext()
    {
        CheckAttached();
        _telling = false;
        PyObject line;
        if (ReferenceEquals(GetPyType(), WrapperType))
            line = ReadLine(-1);
        else
        {
            line = Call(this, "readline");
            if (line is not PyString)
                throw new PythonException(PyOSError.FromArguments(new PyObject[]
                {
                    PyString.From($"readline() should have returned a str object, not '{line.GetTypeName()}'"),
                }, null));
        }
        if (line.Length() == 0)
        {
            _snapshot = null;
            _telling = _seekable;
            throw PyStopIteration.Create();
        }
        return line;
    }

    private static long SizeArgument(PyObject[] args) =>
        PyArgs.OptionalSsize(args.Length > 0 ? args[0] : null);

    private static PyIOBaseType MakeType()
    {
        var type = new PyIOBaseType("TextIOWrapper", new PyType[] { PyIOBaseSlots.TextIOBaseType });
        type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) => new PyTextIOWrapper((PyType)cls),
            acceptsKwargs: true);
        type.TypeDict["__init__"] = new PyWrapperDescriptor("__init__", type, (self, args, kwargs) =>
        {
            ((PyTextIOWrapper)self).Initialize(args, kwargs);
            return PyNone.Instance;
        }, 0, int.MaxValue, acceptsKwargs: true);
        type.TypeDict["__repr__"] = new PyWrapperDescriptor("__repr__", type, (self, args, kwargs) => self.Repr());

        void Method(string name, Func<PyTextIOWrapper, PyObject[], PyObject> body, int min = 0, int max = 0) =>
            type.TypeDict[name] = new PyMethodDescriptor(name, type, (self, args, kwargs) =>
            {
                if (args.Length < min || args.Length > max)
                    throw PyTypeError.Create(min == max
                        ? max == 0 ? $"TextIOWrapper.{name}() takes no arguments ({args.Length} given)" : $"TextIOWrapper.{name}() takes exactly one argument ({args.Length} given)"
                        : args.Length < min ? $"{name} expected at least {min} argument{(min == 1 ? "" : "s")}, got {args.Length}"
                        : $"{name} expected at most {max} argument{(max == 1 ? "" : "s")}, got {args.Length}");
                return body((PyTextIOWrapper)self, args);
            }, minArgs: 0, maxArgs: int.MaxValue);

        PyObject Forward(PyTextIOWrapper self, string method)
        {
            self.CheckAttached();
            return Call(self._buffer, method);
        }

        Method("detach", (self, args) =>
        {
            self.CheckAttached();
            Call(self, "flush");
            var buffer = self._buffer;
            self._buffer = null;
            self._detached = true;
            PyFinalizer.FinalizeAfter(buffer, null);
            return buffer;
        });
        type.TypeDict["reconfigure"] = new PyMethodDescriptor("reconfigure", type,
            (self, args, kwargs) => ((PyTextIOWrapper)self).Reconfigure(args, kwargs), acceptsKwargs: true);
        Method("write", (self, args) => self.Write(TextArgument(args[0], "write")), 1, 1);
        Method("read", (self, args) => self.Read(SizeArgument(args)), 0, 1);
        Method("readline", (self, args) =>
        {
            self.CheckAttached();
            return self.ReadLine(SizeArgument(args));
        }, 0, 1);
        Method("flush", (self, args) =>
        {
            self.CheckAttached();
            self.CheckClosed();
            self._telling = self._seekable;
            self.WriteFlush();
            return Call(self._buffer, "flush");
        });
        Method("close", (self, args) => self.Close());
        Method("fileno", (self, args) => Forward(self, "fileno"));
        Method("seekable", (self, args) => Forward(self, "seekable"));
        Method("readable", (self, args) => Forward(self, "readable"));
        Method("writable", (self, args) => Forward(self, "writable"));
        Method("isatty", (self, args) => Forward(self, "isatty"));
        Method("seek", (self, args) => self.Seek(args[0], args.Length > 1 ? args[1].ToInt() : 0), 1, 2);
        Method("tell", (self, args) => self.Tell());
        Method("truncate", (self, args) =>
        {
            self.CheckAttached();
            Call(self, "flush");
            return Call(self._buffer, "truncate", args.Length > 0 ? args[0] : PyNone.Instance);
        }, 0, 1);
        foreach (var refuse in new[] { "__reduce__", "__reduce_ex__" })
            type.TypeDict[refuse] = new PyMethodDescriptor(refuse, type, (self, args, kwargs) =>
                throw PyTypeError.Create($"cannot pickle '{self.GetPyType().Name}' instances"));

        type.TypeDict["encoding"] = new PyMemberDescriptor("encoding", type, self => ((PyTextIOWrapper)self)._encoding ?? PyNone.Instance);
        type.TypeDict["buffer"] = new PyMemberDescriptor("buffer", type, self => ((PyTextIOWrapper)self)._buffer ?? PyNone.Instance);
        type.TypeDict["line_buffering"] = new PyMemberDescriptor("line_buffering", type, self => PyBool.FromBool(((PyTextIOWrapper)self)._lineBuffering));
        type.TypeDict["write_through"] = new PyMemberDescriptor("write_through", type, self => PyBool.FromBool(((PyTextIOWrapper)self)._writeThrough));
        type.TypeDict["_finalizing"] = new PyMemberDescriptor("_finalizing", type, self => PyBool.FromBool(((PyTextIOWrapper)self)._finalizing),
            (self, value) => ((PyTextIOWrapper)self)._finalizing = value.IsTrue());
        type.TypeDict["name"] = new PyGetSetDescriptor("name", type, self =>
        {
            var wrapper = (PyTextIOWrapper)self;
            wrapper.CheckAttached();
            return wrapper._buffer.GetAttribute("name");
        });
        type.TypeDict["closed"] = new PyGetSetDescriptor("closed", type, self => ((PyTextIOWrapper)self).ClosedValue());
        type.TypeDict["newlines"] = new PyGetSetDescriptor("newlines", type, self =>
        {
            var wrapper = (PyTextIOWrapper)self;
            wrapper.CheckAttached();
            return wrapper._decoder?.LookupAttribute("newlines") ?? PyNone.Instance;
        });
        type.TypeDict["errors"] = new PyGetSetDescriptor("errors", type, self =>
        {
            var wrapper = (PyTextIOWrapper)self;
            wrapper.CheckInitialized();
            return wrapper._errors;
        });
        type.TypeDict["_CHUNK_SIZE"] = new PyGetSetDescriptor("_CHUNK_SIZE", type, self =>
        {
            var wrapper = (PyTextIOWrapper)self;
            wrapper.CheckAttached();
            return PyInt.From(wrapper._chunkSize);
        }, (self, value) =>
        {
            var wrapper = (PyTextIOWrapper)self;
            wrapper.CheckAttached();
            long n = PyArgs.AsSsize(value, PyArgs.SsizeError.Value);
            if (n <= 0)
                throw PyValueError.Create("a strictly positive integer is required");
            wrapper._chunkSize = (int)n;
        }, deleter: self => throw PyAttributeError.Create("cannot delete attribute"));
        return type;
    }
}
