using System;
using System.Collections.Generic;

namespace SharpPy;

/// <summary>
/// The native abstract stream types of CPython's _io module: _IOBase, _RawIOBase,
/// _BufferedIOBase and _TextIOBase (Modules/_io/iobase.c: iobase_methods / iobase_getset /
/// iobase_slots, plus the raw, buffered and text sections of the same file).
///
/// CPython declares the *ABC-ness* of these in python - Lib/io.py wraps them with
/// abc.ABCMeta and virtually registers the concrete classes (RawIOBase.register(FileIO),
/// TextIOBase.register(StringIO, TextIOWrapper), ...). Every implementation, however, lives
/// in this C layer. SharpPy had no counterpart, which is why Lib/io.py grew about 200 python
/// lines standing in for a layer that CPython keeps in C. Registering them here is what lets
/// that python file return to CPython's shape, and it is also what gives _io.StringIO a
/// _TextIOBase -> _IOBase chain instead of a python ABC chain.
///
/// Two C-to-CLR translation notes:
/// - CPython keeps the closed flag in the C struct (iobase.closed). Arbitrary python
///   subclasses of these types are PyClassInstance objects with no such field, so closed
///   state is read polymorphically: the CLR field for native instances, the python-visible
///   "closed" attribute otherwise. That matches what CPython observes for both cases.
/// - The closed-check wording is *not* shared across the C files: iobase.c reports
///   "I/O operation on closed file." with a trailing period while stringio.c reports it
///   without one (both measured). Keeping the two strings separate here is parity, not noise.
/// </summary>
public class PyIOBaseInstance : PyObject, IDictBackedInstance
{
    private readonly PyType _builtin;
    private Dictionary<string, PyObject> dict = new();

    /// <summary>The closed state of bytesio.c and stringio.c, which keep their own.</summary>
    public bool PyClosed;

    public PyIOBaseInstance(PyType type) => _builtin = Allocated(type);

    public override PyType GetPyType() => _customType ?? _builtin;
    public override string GetTypeName() => GetPyType().Name;

    public Dictionary<string, PyObject> InstanceDict
    {
        get => dict;
        set => dict = value;
    }

    /// <summary>
    /// CPython stores these tp_names dotted ("_io._IOBase") and the attribute error prints
    /// tp_name, so the message reads
    ///   AttributeError: '_io._IOBase' object has no attribute 'read'
    /// (measured). _IOBase really has no read/write, which is why read(), readline(),
    /// readlines(), next() and writelines() all surface that AttributeError.
    /// </summary>
    protected override System.Exception AttributeNotFound(string name)
        => PyAttributeError.Create($"'{GetPyType().FullName}' object has no attribute '{name}'");

    /// <summary>
    /// CPython drives io iteration through tp_iter / tp_iternext (iobase_iter returns self and
    /// iobase_iternext calls readline until it answers empty). The VM reaches native objects
    /// through these CLR virtuals rather than the TypeDict descriptors, so both shapes exist.
    /// </summary>
    protected internal override PyObject Iter()
    {
        // iobase_iter: iobase_check_closed.
        PyIOBaseSlots.CheckClosedOf(this);
        return this;
    }

    protected internal override PyObject IterNext() => PyIOBaseSlots.IterNext(this);
}

/// <summary>
/// A native io base type. CPython's "abstract" io classes are instantiable: io.IOBase()
/// answers &lt;io.IOBase object at 0x...&gt; (measured), so CreateInstance returns a plain
/// state holder instead of refusing.
/// </summary>
public class PyIOBaseType : PyType
{
    public PyIOBaseType(string name, PyType[] baseTypes) : base(name, baseTypes, "_io")
    {
        TypeDict["__module__"] = new PyString("_io");
    }

    public override PyObject CreateInstance(PyObject[] args, PyDict kwargs = null)
    {
        if (args.Length != 0)
            throw PyTypeError.Create($"{Name}() takes no arguments");
        if (kwargs != null && kwargs.Length() != 0)
            throw PyTypeError.Create($"{Name}() takes no keyword arguments");
        return new PyIOBaseInstance(this);
    }
}

/// <summary>
/// Registers the default behaviour of the four native io bases. Called once from the native
/// _io module so the types behave identically whether or not _io was imported.
/// </summary>
public static class PyIOBaseSlots
{
    public static readonly PyIOBaseType IOBaseType =
        new("_IOBase", new[] { PyType.ObjectType });
    public static readonly PyIOBaseType RawIOBaseType =
        new("_RawIOBase", new PyType[] { IOBaseType });
    public static readonly PyIOBaseType BufferedIOBaseType =
        new("_BufferedIOBase", new PyType[] { IOBaseType });
    public static readonly PyIOBaseType TextIOBaseType =
        new("_TextIOBase", new PyType[] { IOBaseType });

    // iobase.c reports the closed error with a trailing period.
    private const string ClosedMessage = "I/O operation on closed file.";

    // iobase_is_closed: iobase_close leaves __IOBase_closed in the instance dict.
    private static bool IsClosed(PyObject self) => self.LookupAttribute("__IOBase_closed") != null;

    internal static void CheckClosedOf(PyObject self) => CheckClosed(self);

    // iobase_check_closed: the derived "closed" attribute, which is not __IOBase_closed in most cases.
    private static void CheckClosed(PyObject self)
    {
        var closed = self.LookupAttribute("closed");
        if (closed != null && closed.IsTrue())
            throw PyValueError.Create(ClosedMessage);
    }

    private static void MarkClosed(PyObject self) => self.SetAttribute("__IOBase_closed", PyBool.True);


    // PyObject_CallMethod: the attribute as getattr finds it (an instance attribute included),
    // called; a python subclass overriding readline/write/flush is honoured that way.

    private static PyObject CallWith(PyObject self, string name, PyObject[] args) => self.GetAttribute(name).Call(args, null);

    // iobase_iternext: readline(); an empty line, by PyObject_Size, ends the iteration.
    internal static PyObject IterNext(PyObject self)
    {
        var line = CallWith(self, "readline", Array.Empty<PyObject>());
        if (line.Length() <= 0)
            throw PyStopIteration.Create();
        return line;
    }

    private static PyObject[] One(PyObject value) => new PyObject[] { value };

    // The clinic "w*" converter: a writable buffer, as a byte view the caller releases.
    internal static PyMemoryView WritableBytes(PyObject argument, string function)
    {
        if (!argument.SupportsBuffer())
            throw PyTypeError.Create($"{function}() argument must be read-write bytes-like object, not {argument.GetTypeName()}");
        var view = argument.GetBuffer(0);
        if (view.ReadOnly)
        {
            view.Release();
            throw PyTypeError.Create($"{function}() argument must be read-write bytes-like object, not {argument.GetTypeName()}");
        }
        if (view.ItemSize == 1)
            return view;
        var bytes = (PyMemoryView)view.GetAttribute("cast").Call(new PyObject[] { new PyString("B") }, null);
        view.Release();
        return bytes;
    }

    /// <summary>Iterates with CPython's PyIter_Next shape: Next() until StopIteration.</summary>
    private static IEnumerable<PyObject> Enumerate(PyObject iterable)
    {
        var iterator = iterable.GetIterator();
        while (true)
        {
            PyObject item;
            try
            {
                item = iterator.Next();
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration)
            {
                yield break;
            }
            yield return item;
        }
    }

    private static bool registered;

    public static void Register()
    {
        if (registered)
            return;
        registered = true;

        // ---- _IOBase (Modules/_io/iobase.c) --------------------------------------
        IOBaseType.TypeDict["closed"] = new PyGetSetDescriptor(
            "closed", IOBaseType, self => PyBool.FromBool(IsClosed(self)));

        // _IOBase.readable / writable / seekable answer False in C, with no closed check.
        foreach (var flag in new[] { "readable", "writable", "seekable" })
        {
            IOBaseType.TypeDict[flag] = new PyMethodDescriptor(
                flag, IOBaseType, (self, args, kwargs) => PyBool.False,
                minArgs: 0, maxArgs: 0);
        }

        IOBaseType.TypeDict["fileno"] = new PyMethodDescriptor(
            "fileno", IOBaseType, (self, args, kwargs) =>
            {
                // _IOBase.fileno always reports unsupported: there is no descriptor.
                throw PyUnsupportedOperation.Create("fileno");
            }, minArgs: 0, maxArgs: 0);

        IOBaseType.TypeDict["isatty"] = new PyMethodDescriptor(
            "isatty", IOBaseType, (self, args, kwargs) =>
            {
                CheckClosed(self);
                return PyBool.False;
            }, minArgs: 0, maxArgs: 0);

        IOBaseType.TypeDict["flush"] = new PyMethodDescriptor(
            "flush", IOBaseType, (self, args, kwargs) =>
            {
                // _io__IOBase_flush_impl: only __IOBase_closed is asked, not the derived closed.
                if (IsClosed(self))
                    throw PyValueError.Create(ClosedMessage);
                return PyNone.Instance;
            }, minArgs: 0, maxArgs: 0);

        IOBaseType.TypeDict["seek"] = new PyMethodDescriptor(
            "seek", IOBaseType, (self, args, kwargs) =>
            {
                CheckClosed(self);
                throw PyUnsupportedOperation.Create("seek");
            }, minArgs: 1, maxArgs: 2);

        // _IOBase.tell reports the *seek* operation as unsupported (measured).
        IOBaseType.TypeDict["tell"] = new PyMethodDescriptor(
            "tell", IOBaseType, (self, args, kwargs) =>
            {
                CheckClosed(self);
                throw PyUnsupportedOperation.Create("seek");
            }, minArgs: 0, maxArgs: 0);

        IOBaseType.TypeDict["truncate"] = new PyMethodDescriptor(
            "truncate", IOBaseType, (self, args, kwargs) =>
            {
                CheckClosed(self);
                throw PyUnsupportedOperation.Create("truncate");
            }, minArgs: 0, maxArgs: 1);

        // _io__IOBase_readline_impl: peek() says how far to read ahead, read() reads it.
        IOBaseType.TypeDict["readline"] = new PyMethodDescriptor(
            "readline", IOBaseType, (self, args, kwargs) =>
            {
                long limit = args.Length > 0 && args[0] is not PyNone ? BytesMethods.AsSsize(args[0]) : -1;
                CheckClosed(self);
                PyObject peek = null;
                try { peek = self.GetAttribute("peek"); }
                catch (PythonException error) when (error.PyException is PyAttributeError) { }
                var buffer = new List<byte>();
                while (limit < 0 || buffer.Count < limit)
                {
                    long ahead = 1;
                    if (peek != null)
                    {
                        var readahead = peek.Call(new PyObject[] { new PyInt(1) }, null);
                        if (readahead is not PyBytes peeked)
                            throw new PythonException(PyOSError.FromArguments(new PyObject[] { new PyString(
                                $"peek() should have returned a bytes object, not '{readahead.GetTypeName()}'") }, null));
                        if (peeked.Value.Length > 0)
                        {
                            int n = 0;
                            while (n < peeked.Value.Length && (limit < 0 || n < limit))
                                if (peeked.Value[n++] == (byte)'\n')
                                    break;
                            ahead = n;
                        }
                    }
                    var read = CallWith(self, "read", One(new PyInt(ahead)));
                    if (read is not PyBytes chunk)
                        throw new PythonException(PyOSError.FromArguments(new PyObject[] { new PyString(
                            $"read() should have returned a bytes object, not '{read.GetTypeName()}'") }, null));
                    if (chunk.Value.Length == 0)
                        break;
                    buffer.AddRange(chunk.Value);
                    if (buffer[buffer.Count - 1] == (byte)'\n')
                        break;
                }
                return new PyBytes(buffer.ToArray());
            }, minArgs: 0, maxArgs: 1);

        IOBaseType.TypeDict["readlines"] = new PyMethodDescriptor(
            "readlines", IOBaseType, (self, args, kwargs) =>
            {
                // _io__IOBase_readlines_impl: the lines are self iterated; hint <= 0 takes them
                // all, otherwise it stops once a line overflows the remaining hint.
                long hint = args.Length > 0 && args[0] is not PyNone ? BytesMethods.AsSsize(args[0]) : -1;
                var result = new PyList();
                if (hint <= 0)
                {
                    foreach (var line in Enumerate(self))
                        result.Append(line);
                    return result;
                }
                long length = 0;
                foreach (var line in Enumerate(self))
                {
                    result.Append(line);
                    long lineLength = line.Length();
                    if (lineLength > hint - length)
                        break;
                    length += lineLength;
                }
                return result;
            }, minArgs: 0, maxArgs: 1);

        IOBaseType.TypeDict["writelines"] = new PyMethodDescriptor(
            "writelines", IOBaseType, (self, args, kwargs) =>
            {
                // METH_O: the count is checked before the file is.
                if (args.Length != 1)
                    throw PyTypeError.Create($"_IOBase.writelines() takes exactly one argument ({args.Length} given)");
                CheckClosed(self);
                foreach (var line in Enumerate(args[0]))
                    CallWith(self, "write", One(line));
                return PyNone.Instance;
            }, minArgs: 1, maxArgs: 1);

        IOBaseType.TypeDict["close"] = new PyMethodDescriptor(
            "close", IOBaseType, (self, args, kwargs) =>
            {
                // _io__IOBase_close_impl: flush() once, then __IOBase_closed whatever flush did.
                if (IsClosed(self))
                    return PyNone.Instance;
                try
                {
                    CallWith(self, "flush", Array.Empty<PyObject>());
                }
                finally
                {
                    MarkClosed(self);
                }
                return PyNone.Instance;
            }, minArgs: 0, maxArgs: 0);

        // iobase_finalize, the tp_finalize that a subclass inherits and that __del__ wraps: an open
        // object is closed, with _finalizing set first, and nothing it raises escapes.
        IOBaseType.TypeDict["__del__"] = new PyWrapperDescriptor("__del__", IOBaseType, (self, args, kwargs) =>
        {
            int closed;
            try
            {
                var value = self.LookupAttribute("closed");
                closed = value == null ? -1 : value.IsTrue() ? 1 : 0;
            }
            catch (PythonException)
            {
                closed = -1;
            }
            if (closed == 0)
            {
                try { self.SetAttribute("_finalizing", PyBool.True); }
                catch (PythonException) { }
                try { CallWith(self, "close", Array.Empty<PyObject>()); }
                catch (PythonException) { }
            }
            return PyNone.Instance;
        }, 0, 0);

        // tp_iter: a slot wrapper, iobase_iter.
        IOBaseType.TypeDict["__iter__"] = new PyWrapperDescriptor(
            "__iter__", IOBaseType, (self, args, kwargs) =>
            {
                CheckClosed(self);
                return self;
            });

        // tp_iternext: a slot wrapper.
        IOBaseType.TypeDict["__next__"] = new PyWrapperDescriptor("__next__", IOBaseType, (self, args, kwargs) => IterNext(self));

        IOBaseType.TypeDict["__enter__"] = new PyMethodDescriptor(
            "__enter__", IOBaseType, (self, args, kwargs) =>
            {
                CheckClosed(self);
                return self;
            }, minArgs: 0, maxArgs: 0);

        IOBaseType.TypeDict["__exit__"] = new PyMethodDescriptor(
            "__exit__", IOBaseType, (self, args, kwargs) =>
                CallWith(self, "close", Array.Empty<PyObject>()),
            minArgs: 0, maxArgs: 3);

        IOBaseType.TypeDict["_checkClosed"] = new PyMethodDescriptor(
            "_checkClosed", IOBaseType, (self, args, kwargs) =>
            {
                CheckClosed(self);
                return PyNone.Instance;
            }, minArgs: 0, maxArgs: 0);

        IOBaseType.TypeDict["_checkReadable"] = new PyMethodDescriptor(
            "_checkReadable", IOBaseType, (self, args, kwargs) =>
            {
                if (!(CallWith(self, "readable", Array.Empty<PyObject>()) is PyBool r) || !r.Value)
                    throw PyUnsupportedOperation.Create("File or stream is not readable.");
                return PyNone.Instance;
            }, minArgs: 0, maxArgs: 0);

        IOBaseType.TypeDict["_checkWritable"] = new PyMethodDescriptor(
            "_checkWritable", IOBaseType, (self, args, kwargs) =>
            {
                if (!(CallWith(self, "writable", Array.Empty<PyObject>()) is PyBool w) || !w.Value)
                    throw PyUnsupportedOperation.Create("File or stream is not writable.");
                return PyNone.Instance;
            }, minArgs: 0, maxArgs: 0);

        IOBaseType.TypeDict["_checkSeekable"] = new PyMethodDescriptor(
            "_checkSeekable", IOBaseType, (self, args, kwargs) =>
            {
                if (!(CallWith(self, "seekable", Array.Empty<PyObject>()) is PyBool s) || !s.Value)
                    throw PyUnsupportedOperation.Create("File or stream is not seekable.");
                return PyNone.Instance;
            }, minArgs: 0, maxArgs: 0);

        // ---- _RawIOBase (iobase.c): readinto and write are the primitives and raise a bare
        // NotImplementedError; read is readinto into a bytearray, readall is read until EOF.
        foreach (var name in new[] { "readinto", "write" })
            RawIOBaseType.TypeDict[name] = new PyMethodDescriptor(
                name, RawIOBaseType, (self, args, kw) => throw new PythonException(new PyNotImplementedError(Array.Empty<PyObject>(), null)), minArgs: 1, maxArgs: 1);
        RawIOBaseType.TypeDict["read"] = new PyMethodDescriptor(
            "read", RawIOBaseType, (self, args, kw) =>
            {
                long n = args.Length > 0 && args[0] is not PyNone ? BytesMethods.AsSsize(args[0]) : -1;
                if (n < 0)
                    return CallWith(self, "readall", Array.Empty<PyObject>());
                var b = new PyByteArray(new byte[n]);
                var res = CallWith(self, "readinto", One(b));
                if (res is PyNone)
                    return res;
                long got = BytesMethods.AsSsize(res);
                var data = b.ToByteArrayCopy();
                if (got < 0 || got > data.Length)
                    throw PyValueError.Create($"readinto returned {got} outside buffer size {data.Length}");
                Array.Resize(ref data, (int)got);
                return new PyBytes(data);
            }, minArgs: 0, maxArgs: 1);
        RawIOBaseType.TypeDict["readall"] = new PyMethodDescriptor(
            "readall", RawIOBaseType, (self, args, kw) =>
            {
                var chunks = new List<byte>();
                bool any = false;
                while (true)
                {
                    var data = CallWith(self, "read", One(new PyInt(8192)));
                    if (data is PyNone)
                    {
                        if (!any)
                            return data;
                        break;
                    }
                    if (data is not PyBytes bytes)
                        throw PyTypeError.Create("read() should return bytes");
                    if (bytes.Value.Length == 0)
                        break;
                    chunks.AddRange(bytes.Value);
                    any = true;
                }
                return new PyBytes(chunks.ToArray());
            }, minArgs: 0, maxArgs: 0);

        // ---- _BufferedIOBase (bufferedio.c): read, read1, write and detach are unsupported;
        // readinto and readinto1 are read and read1 copied into the buffer. peek and raw are
        // not among them, so io.BytesIO inherits neither.
        foreach (var (name, max) in new[] { ("read", 1), ("read1", 1), ("write", 1), ("detach", 0) })
            PyArgs.Define(BufferedIOBaseType, name, PyCall.Defining, (self, args) => throw PyUnsupportedOperation.Create(name),
                name == "write" ? 1 : 0, max);
        foreach (var name in new[] { "readinto", "readinto1" })
        {
            string reader = name == "readinto" ? "read" : "read1";
            BufferedIOBaseType.TypeDict[name] = new PyMethodDescriptor(
                name, BufferedIOBaseType, (self, args, kw) =>
                {
                    // _bufferediobase_readinto_generic
                    var view = WritableBytes(args[0], name);
                    try
                    {
                        int length = view.Length();
                        var data = CallWith(self, reader, One(new PyInt(length)));
                        if (data is not PyBytes bytes)
                            throw PyTypeError.Create("read() should return bytes");
                        if (bytes.Value.Length > length)
                            throw PyValueError.Create($"read() returned too much data: {length} bytes requested, {bytes.Value.Length} returned");
                        view.WriteContiguous(bytes.Value);
                        return new PyInt(bytes.Value.Length);
                    }
                    finally
                    {
                        view.Release();
                    }
                }, minArgs: 1, maxArgs: 1);
        }

        // dir(_io._TextIOBase) lists detach as well (measured); it reports unsupported.
        TextIOBaseType.TypeDict["detach"] = new PyMethodDescriptor(
            "detach", TextIOBaseType, (self, args, kwargs) =>
                throw PyUnsupportedOperation.Create("detach"),
            minArgs: 0, maxArgs: 0);

        // ---- _TextIOBase: read/write report unsupported rather than unimplemented.
        TextIOBaseType.TypeDict["read"] = new PyMethodDescriptor(
            "read", TextIOBaseType, (self, args, kwargs) =>
            {
                CheckClosed(self);
                throw PyUnsupportedOperation.Create("read");
            }, minArgs: 0, maxArgs: 1);

        TextIOBaseType.TypeDict["write"] = new PyMethodDescriptor(
            "write", TextIOBaseType, (self, args, kwargs) =>
            {
                CheckClosed(self);
                throw PyUnsupportedOperation.Create("write");
            }, minArgs: 1, maxArgs: 1);

        foreach (var name in new[] { "encoding", "errors", "newlines" })
        {
            var captured = name;
            TextIOBaseType.TypeDict[name] = new PyGetSetDescriptor(
                captured, TextIOBaseType, self =>
                {
                    // _TextIOBase exposes them; only concrete streams give them a value.
                    if (self is PyIOBaseInstance)
                        return PyNone.Instance;
                    try
                    {
                        return self.GetAttribute(captured);
                    }
                    catch (System.Exception)
                    {
                        return PyNone.Instance;
                    }
                });
        }
    }
}
