using System;
using System.Text;

namespace SharpPy;

/// <summary>
/// _io.StringIO implemented in C# - the counterpart of Modules/_io/stringio.c.
///
/// Under the library rule (C-only implementations are replaced in C#) this type is what removes
/// the 93 python lines Lib/io.py had been carrying. It derives from the native _TextIOBase added
/// in B1, so the MRO becomes StringIO -> _TextIOBase -> _IOBase -> object like CPython instead of
/// a python ABC chain, and the inherited defaults (readlines, writelines, __iter__/__next__,
/// __enter__/__exit__, flush, fileno, isatty) come from PyIOBaseSlots rather than a python mixin.
///
/// Positions are counted in characters, matching CPython's use of Py_ssize_t over a str buffer.
/// </summary>
public class PyStringIOType : PyIOBaseType
{
    public PyStringIOType(PyType baseType) : base("StringIO", new[] { baseType })
    {
    }

    public override PyObject CreateInstance(PyObject[] args, PyDict kwargs = null)
        => PyStringIO.Construct(this, args, kwargs);
}

public class PyStringIO : PyIOBaseInstance
{
    public static readonly PyStringIOType StringType =
        new(PyIOBaseSlots.TextIOBaseType);

    private readonly StringBuilder buffer = new();
    private long pos;
    // null means newline=None (universal newlines on the initial value, none on writes).
    private readonly string? newline;

    public PyStringIO(string initial, string? newline) : base(StringType)
    {
        this.newline = newline;
        buffer.Append(TranslateIn(initial, newline));
    }

    /// <summary>
    /// CPython translates the initial value once at construction and each write separately:
    ///   newline=None -> '\r\n' and '\r' collapse to '\n'
    ///   newline='\\n' -> untouched
    ///   any other    -> '\n' becomes that newline
    /// Measured: initial "a\r\nb\rc" reads back as 'a\nb\rc' (None), 'a\r\nb\rc' ('\n') and
    /// 'a\r\rb\rc' ('\r'); writing "a\nb" yields 'a\r\nb' for newline='\r\n' but 'a\nb' for None.
    /// </summary>
    private static string TranslateIn(string text, string? newline)
    {
        if (newline == null)
            return text.Replace("\r\n", "\n").Replace("\r", "\n");
        if (newline == "\n")
            return text;
        return text.Replace("\n", newline);
    }

    private static string TranslateWrite(string text, string? newline)
    {
        if (newline == null || newline == "\n")
            return text;
        return text.Replace("\n", newline);
    }

    /// <summary>
    /// _io.StringIO(initial_string='', newline='\n'). The parameters are positional-only:
    /// StringIO(initial='x') raises "'initial' is an invalid keyword argument for StringIO()"
    /// (measured), while newline is the only keyword accepted.
    /// </summary>
    public static PyObject Construct(PyType type, PyObject[] args, PyDict kwargs)
    {
        if (args.Length > 1)
            throw PyTypeError.Create(
                $"__init__() takes at most 2 arguments ({args.Length + 1} given)");

        string initial = "";
        if (args.Length == 1 && !(args[0] is PyNone))
        {
            if (args[0] is PyString s)
                initial = s.Value;
            else
                throw PyTypeError.Create(
                    $"initial_value must be str or None, not {args[0].GetTypeName()}");
        }

        string? newline = "\n";
        if (kwargs != null && kwargs.Length() != 0)
        {
            // The Clinic signature accepts exactly one keyword, `newline`. Naming the offending
            // key is part of the observable: StringIO(initial='x') reports
            //   'initial' is an invalid keyword argument for StringIO()
            var keys = kwargs.Keys();
            for (var i = 0; i < keys.Length(); i++)
            {
                if (keys.GetItem(i) is PyString key && key.Value != "newline")
                    throw PyTypeError.Create(
                        $"'{key.Value}' is an invalid keyword argument for StringIO()");
            }

            var raw = PyObject.KwValue(kwargs, "newline");
            if (raw != null && !(raw is PyNone))
            {
                if (raw is not PyString nl)
                    throw PyTypeError.Create("newline argument must be str or None");
                newline = nl.Value;
            }
            else if (raw is PyNone)
            {
                newline = null;
            }
        }

        if (newline != null && newline != "" && newline != "\n" && newline != "\r"
            && newline != "\r\n")
            throw PyValueError.Create("illegal newline value: " + new PyString(newline).ToRepr().Value);

        return new PyStringIO(initial, newline);
    }

    private static PyStringIO Self(PyObject self)
    {
        if (self is PyStringIO sio)
            return sio;
        throw PyTypeError.Create(
            $"descriptor '__init__' requires a 'StringIO' object but received a '{self.GetTypeName()}'");
    }

    /// <summary>stringio.c raises with no trailing period, unlike iobase.c.</summary>
    private static void CheckClosed(PyStringIO self)
    {
        if (self.PyClosed)
            throw PyValueError.Create("I/O operation on closed file");
    }

    public static void Register()
    {
        StringType.TypeDict["getvalue"] = new PyMethodDescriptor(
            "getvalue", StringType, (self, args, kwargs) =>
            {
                var s = Self(self);
                CheckClosed(s);
                return new PyString(s.buffer.ToString());
            }, minArgs: 0, maxArgs: 0);

        StringType.TypeDict["write"] = new PyMethodDescriptor(
            "write", StringType, (self, args, kwargs) =>
            {
                var s = Self(self);
                CheckClosed(s);
                if (args[0] is not PyString text)
                    throw PyTypeError.Create(
                        $"string argument expected, got '{args[0].GetTypeName()}'");
                var value = TranslateWrite(text.Value, s.newline);
                s.buffer.Append(value);
                s.pos += value.Length;
                // CPython reports how many characters were handed in, not how many were stored:
                // writing "a\nb" into newline='\r\n' answers 3 while the buffer holds 'a\r\nb'..
                return new PyInt(text.Value.Length);
            }, minArgs: 1, maxArgs: 1);

        StringType.TypeDict["read"] = new PyMethodDescriptor(
            "read", StringType, (self, args, kwargs) =>
            {
                // read(size=-1): None and any negative size read to the end.
                var s = Self(self);
                CheckClosed(s);
                long size = -1;
                if (args.Length > 0 && !(args[0] is PyNone))
                {
                    var index = PyObject.TryGetIndex(args[0])
                        ?? throw PyTypeError.Create(
                            $"argument should be integer or None, not '{args[0].GetTypeName()}'");
                    size = index.Value > long.MaxValue ? long.MaxValue
                        : index.Value < long.MinValue ? long.MinValue : (long)index.Value;
                }
                var total = s.buffer.Length;
                var start = Math.Min(s.pos, total);
                var end = size < 0 ? total : Math.Min(start + size, total);
                var result = s.buffer.ToString((int)start, (int)(end - start));
                s.pos = start + (end - start);
                return new PyString(result);
            }, minArgs: 0, maxArgs: 1);

        StringType.TypeDict["readline"] = new PyMethodDescriptor(
            "readline", StringType, (self, args, kwargs) =>
            {
                var s = Self(self);
                CheckClosed(s);
                long limit = -1;
                if (args.Length > 0 && !(args[0] is PyNone))
                {
                    var index = PyObject.TryGetIndex(args[0])
                        ?? throw PyTypeError.Create(
                            "argument should be integer or None, not 'NoneType'");
                    limit = index.Value > long.MaxValue ? long.MaxValue
                        : index.Value < long.MinValue ? long.MinValue : (long)index.Value;
                }

                var text = s.buffer.ToString();
                var total = text.Length;
                if (s.pos >= total)
                    return PyString.Empty;

                long end;
                if (s.newline == null || s.newline == "\n")
                {
                    var found = text.IndexOf('\n', (int)s.pos);
                    end = found < 0 ? total : found + 1;
                }
                else
                {
                    var found = text.IndexOf(s.newline, (int)s.pos, StringComparison.Ordinal);
                    end = found < 0 ? total : found + s.newline.Length;
                }

                if (limit >= 0 && end - s.pos > limit)
                    end = s.pos + limit;

                var line = text.Substring((int)s.pos, (int)(end - s.pos));
                s.pos = end;
                return new PyString(line);
            }, minArgs: 0, maxArgs: 1);

        StringType.TypeDict["tell"] = new PyMethodDescriptor(
            "tell", StringType, (self, args, kwargs) =>
            {
                var s = Self(self);
                CheckClosed(s);
                return new PyInt(s.pos);
            }, minArgs: 0, maxArgs: 0);

        StringType.TypeDict["seek"] = new PyMethodDescriptor(
            "seek", StringType, (self, args, kwargs) =>
            {
                // stringio_seek rejects cur-relative seeks with a non-zero offset (OSError) and
                // any whence outside 0/1/2 with ValueError (both measured).
                var s = Self(self);
                CheckClosed(s);
                var cookie = PyObject.TryGetIndex(args[0])
                    ?? throw PyTypeError.Create("argument must be int");
                int whence = 0;
                if (args.Length > 1 && !(args[1] is PyNone))
                {
                    var w = PyObject.TryGetIndex(args[1]);
                    if (w == null)
                        throw PyTypeError.Create("argument must be int");
                    whence = (int)w.Value;
                }

                long offset = cookie.Value > long.MaxValue ? long.MaxValue
                    : cookie.Value < long.MinValue ? long.MinValue : (long)cookie.Value;
                long next = whence switch
                {
                    0 => offset,
                    1 when offset == 0 => s.pos,
                    1 => throw PyOSError.Create("Can't do nonzero cur-relative seeks"),
                    2 => s.buffer.Length + offset,
                    _ => throw PyValueError.Create(
                        $"Invalid whence ({whence}, should be 0, 1 or 2)"),
                };
                if (next < 0)
                    throw PyValueError.Create("negative seek position");
                s.pos = next;
                return new PyInt(next);
            }, minArgs: 1, maxArgs: 2);

        StringType.TypeDict["truncate"] = new PyMethodDescriptor(
            "truncate", StringType, (self, args, kwargs) =>
            {
                // truncate() cuts at the current position; an explicit size may not be negative
                // ("ValueError: Negative size value -1", measured with the capital N).
                var s = Self(self);
                CheckClosed(s);
                long size = s.pos;
                if (args.Length > 0 && !(args[0] is PyNone))
                {
                    var index = PyObject.TryGetIndex(args[0])
                        ?? throw PyTypeError.Create("integer argument expected");
                    if (index.Value < 0)
                        throw PyValueError.Create($"Negative size value {index.Value}");
                    size = index.Value > long.MaxValue ? long.MaxValue : (long)index.Value;
                }
                if (size < s.buffer.Length)
                    s.buffer.Remove((int)size, s.buffer.Length - (int)size);
                return new PyInt(size);
            }, minArgs: 0, maxArgs: 1);

        StringType.TypeDict["seekable"] = new PyMethodDescriptor(
            "seekable", StringType, (self, args, kwargs) =>
            {
                CheckClosed(Self(self));
                return PyBool.True;
            }, minArgs: 0, maxArgs: 0);

        StringType.TypeDict["readable"] = new PyMethodDescriptor(
            "readable", StringType, (self, args, kwargs) =>
            {
                CheckClosed(Self(self));
                return PyBool.True;
            }, minArgs: 0, maxArgs: 0);

        StringType.TypeDict["writable"] = new PyMethodDescriptor(
            "writable", StringType, (self, args, kwargs) =>
            {
                CheckClosed(Self(self));
                return PyBool.True;
            }, minArgs: 0, maxArgs: 0);

        // stringio exposes newlines and line_buffering as read-only attributes. Measured on
        // CPython 3.12.15: newlines stays None for these paths and line_buffering is False.
        StringType.TypeDict["newlines"] = new PyGetSetDescriptor(
            "newlines", StringType, self => PyNone.Instance);
        StringType.TypeDict["line_buffering"] = new PyGetSetDescriptor(
            "line_buffering", StringType, self => PyBool.False);
    }
}
