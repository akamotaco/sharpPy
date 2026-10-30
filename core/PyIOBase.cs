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
public class PyIOBaseInstance : PyObject, IInstanceDictAccessor
{
    private readonly PyType type;
    private Dictionary<string, PyObject> dict = new();

    /// <summary>CPython iobase.closed.</summary>
    public bool PyClosed;

    public PyIOBaseInstance(PyType type) => this.type = type;

    public override PyType GetPyType() => type;
    public override string GetTypeName() => type.Name;

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
        => PyAttributeError.Create(
            $"'{type.Module ?? "_io"}.{type.Name}' object has no attribute '{name}'");

    /// <summary>
    /// CPython drives io iteration through tp_iter / tp_iternext (iobase_iter returns self and
    /// iobase_iternext calls readline until it answers empty). The VM reaches native objects
    /// through these CLR virtuals rather than the TypeDict descriptors, so both shapes exist.
    /// </summary>
    public override PyObject GetIterator()
    {
        if (PyClosed)
            throw PyIOBaseSlots.ClosedError();
        return this;
    }

    public override PyObject Next()
    {
        var readline = GetAttribute("readline");
        var line = BindSpecialMethod(readline).Call(Array.Empty<PyObject>(), null);
        if (line is PyString s ? s.Length() == 0 : StaticSizeOf(line) <= 0)
            throw PyStopIteration.Create();
        return line;
    }

    private static int StaticSizeOf(PyObject o) => o switch
    {
        PyBytes b => b.Value.Length,
        PyList l => l.Length(),
        PyTuple t => t.Length(),
        _ => throw PyTypeError.Create($"object of type '{o.GetTypeName()}' has no len()"),
    };

    /// <summary>
    /// iobase carries __dict__ (iobase_members exposes __dictoffset__), so instance attributes
    /// are writable: io.IOBase().x = 5 works in CPython. PyObject.PySetAttribute only writes
    /// the dict of a PyClassInstance, so this CLR-side io object routes the write itself.
    /// </summary>
    protected override void PySetAttribute(string name, PyObject value)
        => dict[name] = value;

    protected override void PyDelAttribute(string name)
    {
        if (!dict.Remove(name))
            throw AttributeNotFound(name);
    }
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

    public static bool IsIOBase(PyObject self)
        => self is PyIOBaseInstance ||
           (self?.GetPyType() != null && self.GetPyType().DerivesFromType(IOBaseType));

    private static bool IsClosed(PyObject self)
    {
        if (self is PyIOBaseInstance native)
            return native.PyClosed;
        // A python subclass may override the "closed" property; CPython reads the same
        // observable through the struct field it exposes as that attribute.
        try
        {
            return self.GetAttribute("closed") is PyBool b && b.Value;
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    private static void CheckClosed(PyObject self)
    {
        if (IsClosed(self))
            throw PyValueError.Create(ClosedMessage);
    }

    private static void MarkClosed(PyObject self)
    {
        if (self is PyIOBaseInstance native)
            native.PyClosed = true;
    }

    /// <summary>Exposes the closed flag to stringio/bytesio and file streams.</summary>
    public static bool ClosedOf(PyObject self) => IsClosed(self);

    /// <summary>The iobase.c wording, with the trailing period.</summary>
    public static System.Exception ClosedError() => PyValueError.Create(ClosedMessage);

    private static int StaticSize(PyObject o) => o switch
    {
        PyString s => s.Length(),
        PyBytes b => b.Value.Length,
        PyByteArray ba => ba.Length(),
        PyList l => l.Length(),
        PyTuple t => t.Length(),
        _ => throw PyTypeError.Create($"object of type '{o.GetTypeName()}' has no len()"),
    };

    // CPython calls these back through PyObject_CallMethod(self, name, ...), which means a
    // python subclass overriding readline/write/flush is honoured. BindSpecialMethod is the
    // CLR-side counterpart of that lookup-plus-bind.
    private static void CallVoid(PyObject self, string name)
        => self.BindSpecialMethod(self.GetAttribute(name))
               .Call(Array.Empty<PyObject>(), null);

    private static PyObject CallWith(PyObject self, string name, PyObject[] args)
        => self.BindSpecialMethod(self.GetAttribute(name)).Call(args, null);

    private static PyObject[] One(PyObject value) => new PyObject[] { value };

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
                // iobase_flush does NOT call self.flush(): the C version flushes the *raw*
                // stream of concrete subclasses, and the base has nothing to flush. Calling
                // back into this descriptor recurses forever (measured the crash).
                // Measured here: open -> None, closed -> ValueError.
                CheckClosed(self);
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

        IOBaseType.TypeDict["readline"] = new PyMethodDescriptor(
            "readline", IOBaseType, (self, args, kwargs) =>
            {
                // iobase readline is a fallback built on peek()/read(); _IOBase has neither,
                // so the AttributeError from looking up read() is the observable result.
                CheckClosed(self);
                bool hasPeek;
                try
                {
                    self.GetAttribute("peek");
                    hasPeek = true;
                }
                catch (System.Exception)
                {
                    hasPeek = false;
                }
                if (hasPeek)
                    return CallWith(self, "readline", args);
                // Looking up read() on a type that has none is what raises, with CPython's
                // message: '_io._IOBase' object has no attribute 'read'.
                self.GetAttribute("read");
                return PyString.Empty;
            }, minArgs: 0, maxArgs: 1);

        IOBaseType.TypeDict["readlines"] = new PyMethodDescriptor(
            "readlines", IOBaseType, (self, args, kwargs) =>
            {
                // _io._IOBase.readlines(hint=-1): hint <= 0 collects the whole iterator,
                // otherwise it stops once a line overflows the remaining hint.
                System.Numerics.BigInteger hint = -1;
                if (args.Length > 0 && !(args[0] is PyNone))
                    hint = PyObject.TryGetIndex(args[0])?.Value ?? -1;
                var result = new PyList();
                if (hint <= 0)
                {
                    foreach (var line in IterateLines(self))
                        result.Append(line);
                    return result;
                }
                System.Numerics.BigInteger length = 0;
                foreach (var line in IterateLines(self))
                {
                    result.Append(line);
                    var lineLength = StaticSize(line);
                    if (lineLength > hint - length)
                        break;
                    length += lineLength;
                }
                return result;
            }, minArgs: 0, maxArgs: 1);

        IOBaseType.TypeDict["writelines"] = new PyMethodDescriptor(
            "writelines", IOBaseType, (self, args, kwargs) =>
            {
                CheckClosed(self);
                foreach (var line in Enumerate(args[0]))
                    CallWith(self, "write", One(line));
                return PyNone.Instance;
            }, minArgs: 1, maxArgs: 1);

        IOBaseType.TypeDict["close"] = new PyMethodDescriptor(
            "close", IOBaseType, (self, args, kwargs) =>
            {
                // iobase_close flushes the raw stream of concrete subclasses once, then marks
                // the object closed; the base has no raw stream, so it only flips the flag.
                // close() twice is a no-op (measured: close(); close() -> None, True).
                MarkClosed(self);
                return PyNone.Instance;
            }, minArgs: 0, maxArgs: 0);

        IOBaseType.TypeDict["__iter__"] = new PyMethodDescriptor(
            "__iter__", IOBaseType, (self, args, kwargs) =>
            {
                CheckClosed(self);
                return self;
            }, minArgs: 0, maxArgs: 0);

        IOBaseType.TypeDict["__next__"] = new PyMethodDescriptor(
            "__next__", IOBaseType, (self, args, kwargs) =>
            {
                // iobase_iternext: readline(); an empty line ends the iteration.
                var line = CallWith(self, "readline", Array.Empty<PyObject>());
                if (StaticSize(line) <= 0)
                    throw PyStopIteration.Create();
                return line;
            }, minArgs: 0, maxArgs: 0);

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

        // ---- _RawIOBase: read/readinto/write are the primitive operations, and the C
        // defaults raise bare NotImplementedError (measured: message is empty).
        void RawNotImplemented(string name, int minArgs, int maxArgs)
        {
            RawIOBaseType.TypeDict[name] = new PyMethodDescriptor(
                name, RawIOBaseType, (self, args, kw) =>
                {
                    throw PyNotImplementedError.Create("");
                }, minArgs: minArgs, maxArgs: maxArgs);
        }

        RawNotImplemented("read", 0, 1);
        RawNotImplemented("readall", 0, 0);
        RawNotImplemented("readinto", 1, 1);
        RawNotImplemented("write", 1, 1);

        // ---- _BufferedIOBase: same shape, the C file raises NotImplementedError too.
        void BufferedNotImplemented(string name, int minArgs, int maxArgs)
        {
            BufferedIOBaseType.TypeDict[name] = new PyMethodDescriptor(
                name, BufferedIOBaseType, (self, args, kw) =>
                {
                    throw PyNotImplementedError.Create("");
                }, minArgs: minArgs, maxArgs: maxArgs);
        }

        // dir(_io._BufferedIOBase) in CPython lists exactly these; peek and raw are NOT among
        // them (measured against SharpPy, which had them by assumption), so io.BytesIO does not
        // inherit a peek/raw attribute at all.
        BufferedNotImplemented("read", 0, 1);
        BufferedNotImplemented("read1", 0, 1);
        BufferedNotImplemented("readinto", 1, 1);
        BufferedNotImplemented("readinto1", 1, 1);
        BufferedNotImplemented("write", 1, 1);
        BufferedNotImplemented("detach", 0, 0);

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

    private static IEnumerable<PyObject> IterateLines(PyObject self)
    {
        while (true)
        {
            var line = CallWith(self, "readline", Array.Empty<PyObject>());
            if (StaticSize(line) <= 0)
                yield break;
            yield return line;
        }
    }
}
