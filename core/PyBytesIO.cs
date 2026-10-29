using System;
using System.Collections.Generic;

namespace SharpPy;

/// <summary>
/// _io.BytesIO implemented in C# - the counterpart of Modules/_io/bytesio.c.
///
/// Steps B1 and B2 did the same for the abstract bases and StringIO. BytesIO derives from the
/// native _BufferedIOBase, so its MRO is BytesIO -> _BufferedIOBase -> _IOBase -> object like
/// CPython, and io.BufferedIOBase recognises it through the register() call that step B4 puts
/// back into Lib/io.py.
///
/// Measured differences from StringIO that look like inconsistencies in CPython and have to be
/// kept anyway:
/// - initial value and writes accept any bytes-like object, and the parameter name
///   initial_bytes IS an accepted keyword (StringIO's initial is positional-only)
/// - a negative truncate size says "negative size value -1" (lowercase) where StringIO says
///   "Negative size value -1"
/// - a negative seek says "negative seek value -5" where StringIO says "negative seek position"
/// - a bad whence says "invalid whence (...)" lowercase where StringIO capitalises it
/// - closed operations raise "I/O operation on closed file." WITH the trailing period, i.e. the
///   iobase.c wording, while StringIO raises the stringio.c wording without it
/// - cur-relative seeks are supported here (stringio refuses a non-zero SEEK_CUR offset)
/// </summary>
public class PyBytesIOType : PyIOBaseType
{
    public PyBytesIOType(PyType baseType) : base("BytesIO", new[] { baseType })
    {
    }

    public override PyObject CreateInstance(PyObject[] args, PyDict kwargs)
        => PyBytesIO.Construct(this, args, kwargs);
}

public class PyBytesIO : PyIOBaseInstance
{
    public static readonly PyBytesIOType BytesType =
        new(PyIOBaseSlots.BufferedIOBaseType);

    private readonly List<byte> buffer = new();
    private long pos;

    public PyBytesIO(byte[] initial) : base(BytesType)
    {
        if (initial != null)
            buffer.AddRange(initial);
    }

    /// <summary>
    /// BytesIO(initial_bytes=b'', /). Only one positional argument; a second argument - keyword or
    /// not - reports "BytesIO() takes at most 1 argument (2 given)" (measured).
    /// </summary>
    public static PyObject Construct(PyType type, PyObject[] args, PyDict kwargs)
    {
        byte[] initial = null;
        int given = args.Length + (kwargs != null ? kwargs.Length() : 0);
        if (given > 1)
            throw PyTypeError.Create($"BytesIO() takes at most 1 argument ({given} given)");

        if (args.Length == 1)
            initial = BytesLike(args[0]);
        else if (kwargs != null && kwargs.Length() == 1)
        {
            var keys = kwargs.Keys();
            if (keys.Length() != 1 || !(keys.GetItem(0) is PyString key) || key.Value != "initial_bytes")
                throw PyTypeError.Create(
                    $"BytesIO() takes at most 1 argument ({given} given)");
            var raw = PyObject.KwValue(kwargs, "initial_bytes");
            if (raw != null && !(raw is PyNone))
                initial = BytesLike(raw);
        }

        return new PyBytesIO(initial);
    }

    /// <summary>
    /// bytes(), bytearray() and memoryview() all work; anything else answers CPython's
    /// "a bytes-like object is required, not 'str'", which is what PyObject.GetBuffer already
    /// raises for the default case.
    /// </summary>
    private static byte[] BytesLike(PyObject value)
    {
        if (value is PyNone)
            return System.Array.Empty<byte>();
        if (value is PyBytes b)
            return b.Value;
        if (value is PyByteArray ba)
            return ba.ToByteArrayCopy();
        return value.GetBuffer(0).Data;
    }

    private static PyBytesIO Self(PyObject self)
    {
        if (self is PyBytesIO bio)
            return bio;
        throw PyTypeError.Create(
            $"descriptor 'getvalue' requires a 'BytesIO' object but received a '{self.GetTypeName()}'");
    }

    /// <summary>bytesio.c uses the iobase wording, with the trailing period.</summary>
    private static void CheckClosed(PyBytesIO self)
    {
        if (self.PyClosed)
            throw PyValueError.Create("I/O operation on closed file.");
    }

    private static long Index(PyObject value, string label)
    {
        var index = PyObject.TryGetIndex(value)
            ?? throw PyTypeError.Create($"{label} must be an integer");
        return index.Value > long.MaxValue ? long.MaxValue
            : index.Value < long.MinValue ? long.MinValue : (long)index.Value;
    }

    public static void Register()
    {
        BytesType.TypeDict["getvalue"] = new PyMethodDescriptor(
            "getvalue", BytesType, (self, args, kwargs) =>
            {
                var s = Self(self);
                CheckClosed(s);
                return new PyBytes(s.buffer.ToArray());
            }, minArgs: 0, maxArgs: 0);

        BytesType.TypeDict["read"] = new PyMethodDescriptor(
            "read", BytesType, (self, args, kwargs) =>
            {
                var s = Self(self);
                CheckClosed(s);
                long size = -1;
                if (args.Length > 0 && !(args[0] is PyNone))
                    size = Index(args[0], "argument");
                var start = Math.Min(s.pos, s.buffer.Count);
                var end = size < 0 ? s.buffer.Count : Math.Min(start + size, s.buffer.Count);
                var data = s.buffer.GetRange((int)start, (int)(end - start)).ToArray();
                s.pos = end;
                return new PyBytes(data);
            }, minArgs: 0, maxArgs: 1);

        // read1 is BufferedReader vocabulary; BytesIO answers it like read (measured).
        BytesType.TypeDict["read1"] = new PyMethodDescriptor(
            "read1", BytesType, (self, args, kwargs) =>
            {
                var s = Self(self);
                CheckClosed(s);
                long size = args.Length > 0 && !(args[0] is PyNone) ? Index(args[0], "argument") : -1;
                var start = Math.Min(s.pos, s.buffer.Count);
                var end = size < 0 ? s.buffer.Count : Math.Min(start + size, s.buffer.Count);
                var data = s.buffer.GetRange((int)start, (int)(end - start)).ToArray();
                s.pos = end;
                return new PyBytes(data);
            }, minArgs: 0, maxArgs: 1);

        BytesType.TypeDict["readinto"] = new PyMethodDescriptor(
            "readinto", BytesType, (self, args, kwargs) =>
            {
                // Fills the writable buffer and answers how many bytes were written; the target
                // keeps its original tail (measured: bytearray(9) over b"ab" -> (2, b'ab\0...')).
                var s = Self(self);
                CheckClosed(s);
                if (args[0] is not PyByteArray target)
                    throw PyTypeError.Create(
                        $"readinto() argument must be writable buffer, not '{args[0].GetTypeName()}'");
                var backing = target.ToByteArrayCopy();
                var start = (int)Math.Min(s.pos, s.buffer.Count);
                var count = System.Math.Min(backing.Length, s.buffer.Count - start);
                // PyByteArray exposes only Append/Clear here, so rebuild the buffer: the filled
                // prefix from this stream, then the target's untouched tail.
                target.Clear();
                for (var i = 0; i < count; i++)
                    target.Append(s.buffer[start + i]);
                for (var i = count; i < backing.Length; i++)
                    target.Append(backing[i]);
                s.pos = start + count;
                return new PyInt(count);
            }, minArgs: 1, maxArgs: 1);

        BytesType.TypeDict["readline"] = new PyMethodDescriptor(
            "readline", BytesType, (self, args, kwargs) =>
            {
                var s = Self(self);
                CheckClosed(s);
                long limit = args.Length > 0 && !(args[0] is PyNone) ? Index(args[0], "argument") : -1;
                if (s.pos >= s.buffer.Count)
                    return new PyBytes(System.Array.Empty<byte>());
                long end = s.buffer.Count;
                for (long i = s.pos; i < s.buffer.Count; i++)
                {
                    if (s.buffer[(int)i] == (byte)'\n')
                    {
                        end = i + 1;
                        break;
                    }
                }
                if (limit >= 0 && end - s.pos > limit)
                    end = s.pos + limit;
                var line = s.buffer.GetRange((int)s.pos, (int)(end - s.pos)).ToArray();
                s.pos = end;
                return new PyBytes(line);
            }, minArgs: 0, maxArgs: 1);

        BytesType.TypeDict["write"] = new PyMethodDescriptor(
            "write", BytesType, (self, args, kwargs) =>
            {
                var s = Self(self);
                CheckClosed(s);
                var data = BytesLike(args[0]);
                // Writing past the end appends; writing inside overwrites (bytesio semantics).
                for (long i = 0; i < data.Length; i++)
                {
                    var at = s.pos + i;
                    if (at < s.buffer.Count)
                        s.buffer[(int)at] = data[i];
                    else
                        s.buffer.Add(data[i]);
                }
                s.pos += data.Length;
                return new PyInt(data.Length);
            }, minArgs: 1, maxArgs: 1);

        BytesType.TypeDict["seek"] = new PyMethodDescriptor(
            "seek", BytesType, (self, args, kwargs) =>
            {
                var s = Self(self);
                CheckClosed(s);
                long offset = Index(args[0], "argument");
                int whence = 0;
                if (args.Length > 1 && !(args[1] is PyNone))
                    whence = (int)Index(args[1], "whence");
                long next = whence switch
                {
                    0 => offset,
                    1 => s.pos + offset,
                    2 => s.buffer.Count + offset,
                    _ => throw PyValueError.Create(
                        $"invalid whence ({whence}, should be 0, 1 or 2)"),
                };
                if (next < 0)
                    throw PyValueError.Create($"negative seek value {next}");
                s.pos = next;
                return new PyInt(next);
            }, minArgs: 1, maxArgs: 2);

        BytesType.TypeDict["tell"] = new PyMethodDescriptor(
            "tell", BytesType, (self, args, kwargs) =>
            {
                var s = Self(self);
                CheckClosed(s);
                return new PyInt(s.pos);
            }, minArgs: 0, maxArgs: 0);

        BytesType.TypeDict["truncate"] = new PyMethodDescriptor(
            "truncate", BytesType, (self, args, kwargs) =>
            {
                var s = Self(self);
                CheckClosed(s);
                long size = s.pos;
                if (args.Length > 0 && !(args[0] is PyNone))
                {
                    size = Index(args[0], "size");
                    if (size < 0)
                        throw PyValueError.Create($"negative size value {size}");
                }
                if (size < s.buffer.Count)
                    s.buffer.RemoveRange((int)size, s.buffer.Count - (int)size);
                if (s.pos > size)
                    s.pos = size;
                return new PyInt(size);
            }, minArgs: 0, maxArgs: 1);

        BytesType.TypeDict["seekable"] = new PyMethodDescriptor(
            "seekable", BytesType, (self, args, kwargs) =>
            {
                CheckClosed(Self(self));
                return PyBool.True;
            }, minArgs: 0, maxArgs: 0);

        BytesType.TypeDict["readable"] = new PyMethodDescriptor(
            "readable", BytesType, (self, args, kwargs) =>
            {
                CheckClosed(Self(self));
                return PyBool.True;
            }, minArgs: 0, maxArgs: 0);

        BytesType.TypeDict["writable"] = new PyMethodDescriptor(
            "writable", BytesType, (self, args, kwargs) =>
            {
                CheckClosed(Self(self));
                return PyBool.True;
            }, minArgs: 0, maxArgs: 0);

        BytesType.TypeDict["detach"] = new PyMethodDescriptor(
            "detach", BytesType, (self, args, kwargs) =>
                throw PyUnsupportedOperation.Create("detach"),
            minArgs: 0, maxArgs: 0);

        // __getstate__ answers (bytes, position, None) and __setstate__ validates each item with
        // its own message (measured: a bool third item gives
        // "third item of state should be a dict, got a bool").
        BytesType.TypeDict["__getstate__"] = new PyMethodDescriptor(
            "__getstate__", BytesType, (self, args, kwargs) =>
            {
                var s = Self(self);
                return new PyTuple(new PyObject[] {
                    new PyBytes(s.buffer.ToArray()),
                    new PyInt(s.pos),
                    PyNone.Instance,
                });
            }, minArgs: 0, maxArgs: 0);

        BytesType.TypeDict["__setstate__"] = new PyMethodDescriptor(
            "__setstate__", BytesType, (self, args, kwargs) =>
            {
                var s = Self(self);
                if (args[0] is not PyTuple state || state.Length() != 3)
                    throw PyValueError.Create("invalid state tuple");
                if (state.GetItem(0) is not PyBytes data)
                    throw PyTypeError.Create(
                        $"first item of state must be bytes, got a {state.GetItem(0).GetTypeName()}");
                if (state.GetItem(1) is not PyInt position)
                    throw PyTypeError.Create(
                        $"second item of state must be an int, got a {state.GetItem(1).GetTypeName()}");
                if (!(state.GetItem(2) is PyNone) && state.GetItem(2) is not PyDict)
                    throw PyTypeError.Create(
                        $"third item of state should be a dict, got a {state.GetItem(2).GetTypeName()}");
                s.buffer.Clear();
                s.buffer.AddRange(data.Value);
                s.pos = position.Value > long.MaxValue ? long.MaxValue
                    : position.Value < 0 ? 0 : (long)position.Value;
                return PyNone.Instance;
            }, minArgs: 1, maxArgs: 1);

        // getbuffer() hands out a memoryview over the stream contents. CPython's view stays live
        // against later writes; this one is taken at the call, which is recorded as a deviation in
        // docs/library_surface_policy.md rather than left as a missing attribute.
        BytesType.TypeDict["getbuffer"] = new PyMethodDescriptor(
            "getbuffer", BytesType, (self, args, kwargs) =>
            {
                var s = Self(self);
                CheckClosed(s);
                return new PyMemoryView(s.buffer.ToArray());
            }, minArgs: 0, maxArgs: 0);
    }
}
