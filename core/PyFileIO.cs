using System;
using System.IO;

namespace SharpPy;

/// <summary>
/// Modules/_io/fileio.c: a raw file over a descriptor of PyFileDescriptors. The descriptor table is
/// where the platform is: a path of the host file system is a FileStream, a Godot res:// or user://
/// path is Godot.FileAccess, and 0, 1 and 2 are the console writers a host may redirect.
/// </summary>
public sealed class PyFileIO : PyIOBaseInstance
{
    private const int O_RDONLY = 0, O_WRONLY = 1, O_RDWR = 2, O_APPEND = 8, O_CREAT = 256, O_TRUNC = 512, O_EXCL = 1024;
    private const int SmallChunk = 8192;

    internal int Fd = -1;
    private bool _created, _readable, _writable, _appending, _closefd = true;
    private int _seekable = -1;
    private int _blksize;
    private bool _finalizing;

    public static readonly PyIOBaseType FileIOType = MakeType();

    internal PyFileIO(PyType type) : base(type) { }

    private static PythonException ErrMode(string action) => PyUnsupportedOperation.Create($"File not open for {action}");

    private void CheckOpen()
    {
        if (Fd < 0)
            throw PyValueError.Create("I/O operation on closed file");
    }

    // PyErr_SetFromErrno with what .NET reported.
    internal static PythonException OSErrorOf(Exception error, PyObject filename = null)
    {
        int errno = error switch
        {
            FileNotFoundException or DirectoryNotFoundException => 2,
            UnauthorizedAccessException => 13,
            IOException when (error.HResult & 0xffff) is 80 or 183 => 17,
            IOException when (error.HResult & 0xffff) is > 0 and < 200 && error.HResult > 0 => error.HResult,
            _ => 5,
        };
        string message = errno switch
        {
            2 => "No such file or directory",
            13 => "Permission denied",
            17 => "File exists",
            29 => "Illegal seek",
            22 => "Invalid argument",
            _ => error.Message,
        };
        var args = filename == null
            ? new PyObject[] { PyInt.From(errno), PyString.From(message) }
            : new PyObject[] { PyInt.From(errno), PyString.From(message), filename };
        return new PythonException(PyOSError.FromArguments(args, null));
    }

    private Stream StreamOf()
    {
        CheckOpen();
        return PyFileDescriptors.Get(Fd);
    }

    // _io_FileIO___init___impl
    private void Initialize(PyObject[] args, PyDict kwargs)
    {
        PyObject nameobj = null, modeobj = null, closefdobj = null, opener = null;
        if (args.Length > 4)
            throw PyTypeError.Create($"FileIO() takes at most 4 arguments ({args.Length} given)");
        if (args.Length > 0) nameobj = args[0];
        if (args.Length > 1) modeobj = args[1];
        if (args.Length > 2) closefdobj = args[2];
        if (args.Length > 3) opener = args[3];
        if (kwargs != null)
            foreach (var pair in kwargs.InternalDict)
            {
                string key = (pair.Key as PyString)?.Value;
                switch (key)
                {
                    case "file" when nameobj == null: nameobj = pair.Value; break;
                    case "mode" when modeobj == null: modeobj = pair.Value; break;
                    case "closefd" when closefdobj == null: closefdobj = pair.Value; break;
                    case "opener" when opener == null: opener = pair.Value; break;
                    case "file" or "mode" or "closefd" or "opener":
                        throw PyTypeError.Create($"argument for FileIO() given by name ('{key}') and position");
                    default:
                        throw PyTypeError.Create($"'{key}' is an invalid keyword argument for FileIO()");
                }
            }
        if (nameobj == null)
            throw PyTypeError.Create("FileIO() missing required argument 'file' (pos 1)");
        string mode = modeobj == null ? "r" : modeobj is PyString text ? text.Value
            : throw PyTypeError.Create($"FileIO() argument 'mode' must be str, not {modeobj.GetTypeName()}");
        bool closefd = closefdobj == null || closefdobj.IsTrue();
        opener ??= PyNone.Instance;

        if (Fd >= 0)
        {
            if (_closefd)
                InternalClose();
            else
                Fd = -1;
        }

        int fd = -1;
        if (PyInt.LongCheck(nameobj) || (nameobj is not PyString && nameobj is not PyBytes && PyObject.TryGetIndex(nameobj) != null))
        {
            var number = PyObject.TryGetIndex(nameobj).Value;
            if (number < 0)
                throw PyValueError.Create("negative file descriptor");
            fd = (int)number;
        }
        string path = null;
        if (fd < 0)
        {
            var fspath = PyImportSystem.Import("os").GetAttribute("fspath").Call(new[] { nameobj }, null);
            path = fspath is PyBytes bytes ? System.Text.Encoding.UTF8.GetString(bytes.Value) : ((PyString)fspath).Value;
            if (path.Contains('\0'))
                throw PyValueError.Create("embedded null character");
        }

        _created = _readable = _writable = _appending = false;
        bool rwa = false, plus = false;
        int flags = 0;
        foreach (char c in mode)
        {
            switch (c)
            {
                case 'x':
                    if (rwa) throw BadMode();
                    rwa = true; _created = true; _writable = true; flags |= O_EXCL | O_CREAT;
                    break;
                case 'r':
                    if (rwa) throw BadMode();
                    rwa = true; _readable = true;
                    break;
                case 'w':
                    if (rwa) throw BadMode();
                    rwa = true; _writable = true; flags |= O_CREAT | O_TRUNC;
                    break;
                case 'a':
                    if (rwa) throw BadMode();
                    rwa = true; _writable = true; _appending = true; flags |= O_APPEND | O_CREAT;
                    break;
                case 'b':
                    break;
                case '+':
                    if (plus) throw BadMode();
                    _readable = _writable = true; plus = true;
                    break;
                default:
                    throw PyValueError.Create($"invalid mode: {mode}");
            }
        }
        if (!rwa)
            throw BadMode();
        flags |= _readable && _writable ? O_RDWR : _readable ? O_RDONLY : O_WRONLY;

        bool own = false;
        try
        {
            if (fd >= 0)
            {
                Fd = fd;
                _closefd = closefd;
                PyFileDescriptors.Get(Fd);
            }
            else
            {
                _closefd = true;
                if (!closefd)
                    throw PyValueError.Create("Cannot use closefd=False with file name");
                if (opener is PyNone)
                {
                    try
                    {
                        PyFileDescriptors.ReleaseUnreachable(path);
                        Fd = PyFileDescriptors.Open(path, flags);
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    {
                        throw OSErrorOf(error, nameobj);
                    }
                }
                else
                {
                    var result = opener.Call(new[] { nameobj, PyInt.From(flags) }, null);
                    if (!PyInt.LongCheck(result))
                        throw PyTypeError.Create("expected integer from opener");
                    Fd = PyObject.TryGetIndex(result).ToInt();
                    if (Fd < 0)
                        throw PyValueError.Create($"opener returned {Fd}");
                }
                own = true;
            }
            _blksize = 8192;
            // PyObject_SetAttr: a subclass's own __setattr__ is the slot it reaches.
            SetAttribute("name", nameobj);
            if (_appending)
                Seek(0, 2, true);
            Register();
        }
        catch
        {
            if (!own)
                Fd = -1;
            if (Fd >= 0)
            {
                try { InternalClose(); }
                catch (PythonException) { }
            }
            throw;
        }
    }

    private static PythonException BadMode() =>
        PyValueError.Create("Must have exactly one of create/read/write/append mode and at most one plus") as PythonException;

    // An abandoned file is closed when the collector finds it: tp_finalize (iobase_finalize)
    // calls close(), which warns first because _finalizing is set.
    private void Register() => PyFinalizer.Attach(this);

    // internal_close
    private void InternalClose()
    {
        if (Fd < 0)
            return;
        int fd = Fd;
        Fd = -1;
        try
        {
            PyFileDescriptors.Close(fd);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw OSErrorOf(error);
        }
    }

    // portable_lseek
    private PyObject Seek(long position, int whence, bool suppressPipeError)
    {
        var stream = PyFileDescriptors.Get(Fd);
        if (!stream.CanSeek)
        {
            if (_seekable < 0)
                _seekable = 0;
            if (suppressPipeError)
                return PyInt.From(0);
            throw new PythonException(PyOSError.FromArguments(new PyObject[] { PyInt.From(29), PyString.From("Invalid seek") }, null));
        }
        long result;
        try
        {
            result = stream.Seek(position, whence switch { 0 => SeekOrigin.Begin, 1 => SeekOrigin.Current, _ => SeekOrigin.End });
        }
        catch (Exception error) when (error is IOException or ArgumentException)
        {
            if (_seekable < 0)
                _seekable = 1;
            throw new PythonException(PyOSError.FromArguments(new PyObject[] { PyInt.From(22), PyString.From("Invalid argument") }, null));
        }
        if (_seekable < 0)
            _seekable = 1;
        return PyInt.From(result);
    }

    private static long Offset(PyObject value)
    {
        var index = PyObject.TryGetIndex(value) ?? throw PyTypeError.Create($"'{value.GetTypeName()}' object cannot be interpreted as an integer");
        if (index.Value > long.MaxValue || index.Value < long.MinValue)
            throw PyOverflowError.Create("Python int too large to convert to C long");
        return (long)index.Value;
    }

    private string ModeString() => _created ? (_readable ? "xb+" : "xb")
        : _appending ? (_readable ? "ab+" : "ab")
        : _readable ? (_writable ? "rb+" : "rb") : "wb";

    private int ReadInto(byte[] buffer, int offset, int count)
    {
        var stream = StreamOf();
        int n = 0;
        try
        {
            PyGil.AllowThreads(() => n = stream.Read(buffer, offset, count));
        }
        catch (Exception error) when (error is IOException or NotSupportedException)
        {
            throw OSErrorOf(error);
        }
        return n;
    }

    // _io_FileIO_readall_impl
    private PyObject ReadAll()
    {
        CheckOpen();
        var stream = PyFileDescriptors.Get(Fd);
        long bufsize = SmallChunk;
        if (stream.CanSeek)
        {
            long remaining = stream.Length - stream.Position;
            if (remaining > 0)
                bufsize = remaining + 1;
        }
        var result = new byte[bufsize];
        int read = 0;
        while (true)
        {
            if (read >= result.Length)
            {
                long grown = read > 65536 ? read + (read >> 3) : read + 256 + read;
                Array.Resize(ref result, (int)Math.Max(grown, read + SmallChunk));
            }
            int n = ReadInto(result, read, result.Length - read);
            if (n == 0)
                break;
            read += n;
        }
        Array.Resize(ref result, read);
        return PyBytes.From(result);
    }

    // fileio_repr
    protected internal override PyString Repr()
    {
        string typeName = GetPyType() is PyClass ? $"{GetPyType().Module}.{GetPyType().Name}" : "_io.FileIO";
        if (Fd < 0)
            return PyString.From($"<{typeName} [closed]>");
        PyObject name = null;
        try
        {
            name = GetAttribute("name");
        }
        catch (PythonException error) when (error.PyException is PyAttributeError)
        {
        }
        string closefd = _closefd ? "True" : "False";
        if (name == null)
            return PyString.From($"<{typeName} fd={Fd} mode='{ModeString()}' closefd={closefd}>");
        if (!PyReprGuard.Enter(this))
            throw PyRuntimeError.Create($"reentrant call inside {typeName}.__repr__");
        try
        {
            return PyString.From($"<{typeName} name={name.ToRepr().Value} mode='{ModeString()}' closefd={closefd}>");
        }
        finally
        {
            PyReprGuard.Leave(this);
        }
    }

    private static PyDict PyDictOf(string key, PyObject value)
    {
        var dict = new PyDict();
        dict.SetItem(PyString.From(key), value);
        return dict;
    }

    private static PyFileIO Self(PyObject self, string name) => self as PyFileIO
        ?? throw PyTypeError.Create($"descriptor '{name}' requires a '_io.FileIO' object but received a '{self.GetTypeName()}'");

    // The methods that take the defining class go through _PyArg_UnpackKeywords; seek is plain
    // METH_FASTCALL and _dealloc_warn METH_O; the rest are METH_NOARGS.
    private static PyCall KindOf(string name) => name switch
    {
        "close" or "readinto" or "read" or "write" or "truncate" => PyCall.Defining,
        "seek" => PyCall.Fast,
        "_dealloc_warn" => PyCall.One,
        _ => PyCall.NoArgs,
    };

    private static void Method(PyType type, string name, Func<PyFileIO, PyObject[], PyObject> body, int min = 0, int max = 0) =>
        PyArgs.Define(type, name, KindOf(name), (self, args) => body(Self(self, name), args), min, max);

    private static PyIOBaseType MakeType()
    {
        var type = new PyIOBaseType("FileIO", new PyType[] { PyIOBaseSlots.RawIOBaseType });
        type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) => new PyFileIO((PyType)cls),
            acceptsKwargs: true);
        type.TypeDict["__init__"] = new PyWrapperDescriptor("__init__", type, (self, args, kwargs) =>
        {
            Self(self, "__init__").Initialize(args, kwargs);
            return PyNone.Instance;
        }, 0, int.MaxValue, acceptsKwargs: true);
        type.TypeDict["__repr__"] = new PyWrapperDescriptor("__repr__", type, (self, args, kwargs) => self.Repr());

        Method(type, "read", (self, args) =>
        {
            self.CheckOpen();
            if (!self._readable)
                throw ErrMode("reading");
            long size = args.Length == 0 || args[0] is PyNone ? -1 : Offset(args[0]);
            if (size < 0)
                return self.ReadAll();
            var buffer = new byte[Math.Min(size, int.MaxValue)];
            int n = self.ReadInto(buffer, 0, buffer.Length);
            if (n != buffer.Length)
                Array.Resize(ref buffer, n);
            return PyBytes.From(buffer);
        }, 0, 1);
        Method(type, "readall", (self, args) => self.ReadAll());
        Method(type, "readinto", (self, args) =>
        {
            self.CheckOpen();
            if (!self._readable)
                throw ErrMode("reading");
            var bytes = PyIOBaseSlots.WritableBytes(args[0], "readinto");
            try
            {
                int length = bytes.Length();
                var data = new byte[length];
                int n = self.ReadInto(data, 0, length);
                bytes.WriteContiguous(data.AsSpan(0, n));
                return PyInt.From(n);
            }
            finally
            {
                bytes.Release();
            }
        }, 1, 1);
        Method(type, "write", (self, args) =>
        {
            self.CheckOpen();
            if (!self._writable)
                throw ErrMode("writing");
            var data = BytesMethods.Buffer(args[0])
                ?? throw PyTypeError.Create($"a bytes-like object is required, not '{args[0].GetTypeName()}'");
            var stream = PyFileDescriptors.Get(self.Fd);
            try
            {
                PyGil.AllowThreads(() =>
                {
                    stream.Write(data, 0, data.Length);
                    stream.Flush();
                });
            }
            catch (Exception error) when (error is IOException or NotSupportedException)
            {
                throw OSErrorOf(error);
            }
            return PyInt.From(data.Length);
        }, 1, 1);
        Method(type, "seek", (self, args) =>
        {
            self.CheckOpen();
            int whence = args.Length > 1 ? args[1].ToInt() : 0;
            return self.Seek(Offset(args[0]), whence, false);
        }, 1, 2);
        Method(type, "tell", (self, args) =>
        {
            self.CheckOpen();
            return self.Seek(0, 1, false);
        });
        Method(type, "truncate", (self, args) =>
        {
            self.CheckOpen();
            if (!self._writable)
                throw ErrMode("writing");
            var position = args.Length == 0 || args[0] is PyNone ? self.Seek(0, 1, false) : args[0];
            try
            {
                // _chsize_s and ftruncate leave the position where it was.
                var stream = PyFileDescriptors.Get(self.Fd);
                long at = stream.CanSeek ? stream.Position : -1;
                stream.SetLength(Offset(position));
                if (at >= 0)
                    stream.Position = at;
            }
            catch (Exception error) when (error is IOException or NotSupportedException)
            {
                throw OSErrorOf(error);
            }
            return position;
        }, 0, 1);
        Method(type, "close", (self, args) =>
        {
            // RawIOBase.close first: the flush and the closed flag of iobase.
            PythonException failure = null;
            try
            {
                PyIOBaseSlots.IOBaseType.TypeDict["close"].Call(new PyObject[] { self }, null);
            }
            catch (PythonException error)
            {
                failure = error;
            }
            if (!self._closefd)
            {
                self.Fd = -1;
                if (failure != null)
                    throw failure;
                return PyNone.Instance;
            }
            if (self._finalizing)
            {
                try { self.GetAttribute("_dealloc_warn").Call(new PyObject[] { self }, null); }
                catch (PythonException) { }
            }
            self.InternalClose();
            if (failure != null)
                throw failure;
            return PyNone.Instance;
        });
        Method(type, "fileno", (self, args) =>
        {
            self.CheckOpen();
            return PyInt.From(self.Fd);
        });
        Method(type, "readable", (self, args) =>
        {
            self.CheckOpen();
            return PyBool.FromBool(self._readable);
        });
        Method(type, "writable", (self, args) =>
        {
            self.CheckOpen();
            return PyBool.FromBool(self._writable);
        });
        Method(type, "seekable", (self, args) =>
        {
            self.CheckOpen();
            if (self._seekable < 0)
            {
                try { self.Seek(0, 1, false); }
                catch (PythonException) { }
            }
            return PyBool.FromBool(self._seekable == 1);
        });
        Method(type, "isatty", (self, args) =>
        {
            self.CheckOpen();
            // isatty() of a descriptor that is gone is 0, not an error.
            try
            {
                return PyBool.FromBool(PyFileDescriptors.Get(self.Fd) is PyConsoleStream { IsTerminal: true });
            }
            catch (PythonException)
            {
                return PyBool.False;
            }
        });
        Method(type, "_dealloc_warn", (self, args) =>
        {
            if (self.Fd >= 0 && self._closefd)
                PyImportSystem.Import("warnings").GetAttribute("warn").Call(new PyObject[] { PyString.From($"unclosed file {args[0].ToRepr().Value}"), PyType.ResourceWarningType, PyInt.From(1) }, PyDictOf("source", args[0]));
            return PyNone.Instance;
        }, 1, 1);
        Method(type, "__getstate__", (self, args) => throw PyTypeError.Create($"cannot pickle '{self.GetPyType().FullName}' instances"));

        type.TypeDict["closed"] = new PyGetSetDescriptor("closed", type, self => PyBool.FromBool(Self(self, "closed").Fd < 0));
        type.TypeDict["closefd"] = new PyGetSetDescriptor("closefd", type, self => PyBool.FromBool(Self(self, "closefd")._closefd));
        type.TypeDict["mode"] = new PyGetSetDescriptor("mode", type, self => PyString.From(Self(self, "mode").ModeString()));
        type.TypeDict["_blksize"] = new PyMemberDescriptor("_blksize", type, self => PyInt.From(Self(self, "_blksize")._blksize));
        type.TypeDict["_finalizing"] = new PyMemberDescriptor("_finalizing", type, self => PyBool.FromBool(Self(self, "_finalizing")._finalizing),
            (self, value) => Self(self, "_finalizing")._finalizing = value.IsTrue());
        return type;
    }
}
