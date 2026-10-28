using System;
using System.IO;
using System.Text;

namespace SharpPy;

public class PyFileContextManager : PyContextManager
{
    private readonly string filename, mode;
    private readonly bool binary, readable, writable, append;
    private readonly string? newline;
    private Stream? stream;
    private StreamReader? reader;
    private StreamWriter? writer;
    private Encoding? textEncoding;
    private bool closed;
    // CPython text-mode tell() reports decoded character offsets (opaque
    // cookies that equal char counts for the common aligned cases).
    private long textPos;
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
    // CPython: the concrete buffered/text file classes live in the native _io module
    // (__module__ == "_io"), so repr(type(open(...))) renders <class '_io.TextIOWrapper'>.
    // Exposed so the native _io module can bind these exact type objects (identity),
    // making isinstance(open(f), io.TextIOWrapper) hold.
    public static readonly PyType TextType = new("TextIOWrapper", new[] { PyType.ObjectType }, "_io");
    public static readonly PyType ReaderType = new("BufferedReader", new[] { PyType.ObjectType }, "_io");
    public static readonly PyType WriterType = new("BufferedWriter", new[] { PyType.ObjectType }, "_io");
    public static readonly PyType RandomType = new("BufferedRandom", new[] { PyType.ObjectType }, "_io");

    static PyFileContextManager()
    {
        // Native PyTypes resolve __module__ from their own TypeDict (matching _sre);
        // bind "_io" so open(f) reports <class '_io.TextIOWrapper'> even before _io is
        // imported into Python.
        foreach (var t in new[] { TextType, ReaderType, WriterType, RandomType })
            t.TypeDict["__module__"] = new PyString("_io");
    }

    public PyFileContextManager(string filename, string mode = "r", string? encoding = null,
        string? errors = null, string? newline = null, int buffering = -1, bool closefd = true)
    {
        this.filename = filename;
        this.mode = mode;
        this.newline = newline;
        char operation = '\0';
        bool update = false, text = false;
        int seen = 0;
        foreach (char c in mode)
        {
            int bit = c switch { 'r' => 1, 'w' => 2, 'a' => 4, 'x' => 8, '+' => 16, 'b' => 32, 't' => 64, _ => 0 };
            if (bit == 0 || (seen & bit) != 0) throw PyValueError.Create($"invalid mode: '{mode}'");
            seen |= bit;
            if (c is 'r' or 'w' or 'a' or 'x')
            {
                if (operation != '\0') throw PyValueError.Create("must have exactly one of create/read/write/append mode");
                operation = c;
            }
            else if (c == '+') update = true;
            else if (c == 'b') binary = true;
            else text = true;
        }
        if (binary && text) throw PyValueError.Create("can't have text and binary mode at once");
        if (operation == '\0') throw PyValueError.Create("Must have exactly one of create/read/write/append mode and at most one plus");
        if (binary && encoding != null) throw PyValueError.Create("binary mode doesn't take an encoding argument");
        if (binary && errors != null) throw PyValueError.Create("binary mode doesn't take an errors argument");
        if (binary && newline != null) throw PyValueError.Create("binary mode doesn't take a newline argument");
        if (!closefd) throw PyValueError.Create("Cannot use closefd=False with file name");
        readable = operation == 'r' || update;
        writable = operation != 'r' || update;
        append = operation == 'a';
        try
        {
#if GODOT
            if (Godot_IO.Helper.IsGodotPath(filename)) stream = new GodotFileStream(filename, operation, update);
            else
#endif
                stream = new FileStream(filename,
                    operation == 'r' ? FileMode.Open : operation == 'w' ? FileMode.Create : operation == 'x' ? FileMode.CreateNew : FileMode.OpenOrCreate,
                    readable && writable ? FileAccess.ReadWrite : readable ? FileAccess.Read : FileAccess.Write,
                    FileShare.ReadWrite, !binary && !writable ? 1 : 4096);
            if (append) stream.Seek(0, SeekOrigin.End);
            // Text wrapper validation follows raw opening, as in _io.open.
            if (!binary)
            {
                if (buffering == 0) throw PyValueError.Create("can't have unbuffered text I/O");
                if (newline != null && newline != "" && newline != "\n" && newline != "\r" && newline != "\r\n")
                    throw PyValueError.Create("illegal newline value: " + new PyString(newline).ToRepr().Value);
                Encoding codec = StrictUtf8;
                string? normalizedEncoding = encoding == null ? null : NormalizeEncodingName(encoding);
                if ((normalizedEncoding != null && normalizedEncoding != "utf-8") || (errors != null && errors != "strict"))
                {
                    if (normalizedEncoding == "utf-8-sig")
                    {
                        codec = new UTF8Encoding(true, true);
                    }
                    else
                    {
                        try { codec = Encoding.GetEncoding(normalizedEncoding ?? "utf-8"); }
                        catch (ArgumentException) { throw PyLookupError.Create("unknown encoding: " + encoding); }
                    }
                    if (codec.CodePage == 65001) codec = new UTF8Encoding(false);
                    else codec = (Encoding)codec.Clone();
                    codec.EncoderFallback = errors == "ignore" ? new EncoderReplacementFallback("") : errors == "replace" ? EncoderFallback.ReplacementFallback : EncoderFallback.ExceptionFallback;
                    codec.DecoderFallback = errors == "ignore" ? new DecoderReplacementFallback("") : errors == "replace" ? DecoderFallback.ReplacementFallback : DecoderFallback.ExceptionFallback;
                }
                textEncoding = codec;
                if (writable) writer = new StreamWriter(stream, codec, 1024, true);
            }
        }
        catch (Exception ex)
        {
            stream?.Dispose();
            if (ex is IOException || ex is UnauthorizedAccessException) throw FileError(ex, true);
            throw;
        }
    }

    // CPython codecs accept these aliases for the built-in Windows codepages.
    private static string NormalizeEncodingName(string name)
    {
        switch (name.Trim().ToLowerInvariant())
        {
            case "latin-1":
            case "latin1":
            case "latin":
            case "l1":
            case "iso-8859-1":
            case "iso8859-1":
            case "8859":
            case "cp819":
                return "ISO-8859-1";
            case "ascii":
            case "us-ascii":
            case "646":
                return "us-ASCII";
            case "utf8":
            case "u8":
                return "utf-8";
            case "utf16":
            case "utf-16le":
                return "utf-16LE";
            case "utf-16be":
                return "utf-16BE";
            case "utf-8-sig":
                return "utf-8-sig";
            default:
                return name;
        }
    }

    internal static long CountChars(string s)
    {
        long count = 0;
        for (int p = 0; p < s.Length; p++, count++)
            if (char.IsSurrogatePair(s, p)) p++;
        return count;
    }

    private PythonException FileError(Exception error, bool opening = false)
    {
        int errno = error is FileNotFoundException || error is DirectoryNotFoundException ? 2
            : error is UnauthorizedAccessException ? 13 : (error.HResult & 0xffff) is 80 or 183 ? 17 : 5;
        string message = errno == 2 ? "No such file or directory" : errno == 13 ? "Permission denied" : errno == 17 ? "File exists" : error.Message;
        var args = opening ? new PyObject[] { new PyInt(errno), new PyString(message), new PyString(filename) }
            : new PyObject[] { new PyInt(errno), new PyString(message) };
        return new PythonException(PyOSError.FromArguments(args, null));
    }
    private void CheckClosed()
    {
        if (closed) throw PyValueError.Create("I/O operation on closed file.");
    }
    public override PyType GetPyType() => !binary ? TextType : readable && writable ? RandomType : readable ? ReaderType : WriterType;
    public override string GetTypeName() => GetPyType().Name;
    public override PyString ToRepr() => new($"<_io.{GetTypeName()} name={new PyString(filename).ToRepr().Value}>");
    public override PyObject Enter() { CheckClosed(); return this; }
    public override PyObject Exit(PyObject excType, PyObject excValue, PyObject traceback) { Close(); return PyNone.Instance; }

    // CPython: iterating a text/binary file yields lines; the file is its own iterator.
    public override PyObject GetIterator() => this;
    public override PyObject Next()
    {
        var line = Read(-1, true);
        if (line is PyBytes b ? b.Value.Length == 0 : ((PyString)line).Value.Length == 0)
            throw PyStopIteration.Create();
        return line;
    }

    private void Close()
    {
        if (closed) return;
        try { writer?.Flush(); }
        catch (IOException ex) { throw FileError(ex); }
        finally
        {
            closed = true;
            try { stream?.Dispose(); }
            catch (IOException ex) { throw FileError(ex); }
            finally { stream = null; reader = null; writer = null; }
        }
    }
    private void Flush()
    {
        CheckClosed();
        try { writer?.Flush(); stream!.Flush(); }
        catch (IOException ex) { throw FileError(ex); }
    }
    private static long Size(PyObject? value)
    {
        if (value == null || value is PyNone) return -1;
        var index = PyObject.TryGetIndex(value);
        if (index == null) throw PyTypeError.Create($"'{value.GetTypeName()}' object cannot be interpreted as an integer");
        if (index.Value < long.MinValue || index.Value > long.MaxValue)
            throw PyOverflowError.Create("cannot fit 'int' into an index-sized integer");
        return (long)index.Value;
    }
    public PyObject Read() => Read(-1);
    private PyObject Read(long size, bool line = false)
    {
        if (closed && binary) throw PyValueError.Create("read of closed file");
        CheckClosed();
        if (!readable) throw PyUnsupportedOperation.Create("not readable");
        try
        {
            writer?.Flush();
            if (binary)
            {
                if (line)
                {
                    using var data = new MemoryStream();
                    while (size < 0 || data.Length < size)
                    {
                        int b = stream!.ReadByte();
                        if (b < 0) break;
                        data.WriteByte((byte)b);
                        if (b == 10) break;
                    }
                    return new PyBytes(data.ToArray());
                }
                if (size < 0) return new PyBytes(ReadRemainingBytes());
                if (size > Array.MaxLength) throw PyMemoryError.Create("");
                byte[] buffer = new byte[(int)size];
                int count = 0, read;
                while (count < buffer.Length && (read = stream!.Read(buffer, count, buffer.Length - count)) > 0) count += read;
                if (count != buffer.Length) Array.Resize(ref buffer, count);
                return new PyBytes(buffer);
            }
            if (size < 0 && !line)
            {
                string content = reader != null ? reader.ReadToEnd() : textEncoding!.GetString(ReadRemainingBytes());
                if (newline == null) content = content.Replace("\r\n", "\n").Replace('\r', '\n');
                textPos += CountChars(content);
                return new PyString(content);
            }
            reader ??= new StreamReader(stream!, textEncoding!, false, 1024, true);
            var result = new StringBuilder();
            for (long count = 0; size < 0 || count < size; count++)
            {
                int c = reader!.Read();
                if (c < 0) break;
                if (c == '\r' && newline == null)
                {
                    if (reader.Peek() == '\n') reader.Read();
                    c = '\n';
                }
                result.Append((char)c);
                textPos++;
                if (char.IsHighSurrogate((char)c) && reader.Peek() >= 0 && char.IsLowSurrogate((char)reader.Peek()))
                    result.Append((char)reader.Read());
                if (line && newline == "" && c == '\r')
                {
                    if ((size < 0 || count + 1 < size) && reader.Peek() == '\n') result.Append((char)reader.Read());
                    break;
                }
                if (line && (newline == null || newline == "" || newline == "\n") && c == '\n') break;
                if (line && newline == "\r" && c == '\r') break;
                if (line && newline == "\r\n" && result.Length >= 2 && result[^2] == '\r' && c == '\n') break;
            }
            return new PyString(result.ToString());
        }
        catch (IOException ex) { throw FileError(ex); }
    }
    private byte[] ReadRemainingBytes()
    {
        long available = Math.Max(0, stream!.Length - stream.Position);
        if (available > Array.MaxLength) throw PyMemoryError.Create("");
        byte[] data = available == 0 ? Array.Empty<byte>() : new byte[(int)available];
        int count = 0;
        while (true)
        {
            if (count == data.Length)
            {
                int extra = stream.ReadByte();
                if (extra < 0) break;
                if (data.Length == Array.MaxLength) throw PyMemoryError.Create("");
                int capacity = (int)Math.Min(Array.MaxLength, Math.Max(4096L, (long)data.Length * 2));
                Array.Resize(ref data, capacity);
                data[count++] = (byte)extra;
            }
            int read = stream.Read(data, count, data.Length - count);
            if (read == 0) break;
            count += read;
        }
        if (count != data.Length) Array.Resize(ref data, count);
        return data;
    }
    public PyObject Write(PyObject value)
    {
        if (closed && binary) throw PyValueError.Create("write to closed file");
        CheckClosed();
        if (!writable) throw PyUnsupportedOperation.Create("not writable");
        try
        {
            if (append) stream!.Seek(0, SeekOrigin.End);
            if (binary)
            {
                ReadOnlySpan<byte> bytes = value is PyBytes b ? b.Value : value is PyByteArray a ? a.BufferSpan
                    : value is PyMemoryView view ? view.ContiguousSpan()
                    : throw PyTypeError.Create($"a bytes-like object is required, not '{value.GetTypeName()}'");
                stream!.Write(bytes);
                // The span roots storage, not the view's export ownership.
                GC.KeepAlive(value);
                // SharpPy has no refcount to flush an abandoned temp open(); writing
                // through keeps read-back after `open(f,'wb').write(...)` observable.
                stream.Flush();
                return new PyInt(bytes.Length);
            }
            if (value is not PyString text) throw PyTypeError.Create($"write() argument must be str, not {value.GetTypeName()}");
            string output = text.Value;
            string replacement = newline ?? Environment.NewLine;
            if (replacement != "" && replacement != "\n") output = output.Replace("\n", replacement);
            writer!.Write(output);
            writer.Flush();
            int length = 0;
            for (int p = 0; p < text.Value.Length; p++, length++) if (char.IsSurrogatePair(text.Value, p)) p++;
            textPos += length;
            return new PyInt(length);
        }
        catch (IOException ex) { throw FileError(ex); }
    }
    public PyObject ReadLines()
    {
        var lines = new System.Collections.Generic.List<PyObject>();
        while (true)
        {
            var line = Read(-1, true);
            if (line is PyBytes b ? b.Value.Length == 0 : ((PyString)line).Value.Length == 0) break;
            lines.Add(line);
        }
        return new PyList(lines.ToArray());
    }
    private void WriteLines(PyObject linesIterable)
    {
        var iterator = linesIterable.GetIterator();
        try
        {
            while (true)
            {
                var item = iterator.Next();
                Write(item);
            }
        }
        catch (PythonException ex) when (ex.PyException is PyStopIteration)
        {
        }
    }

    private PyObject Truncate(PyObject? sizeValue)
    {
        if (!writable) throw PyUnsupportedOperation.Create("not writable");
        CheckClosed();
        Flush();
        long target;
        if (sizeValue == null || sizeValue is PyNone)
        {
            target = binary ? stream!.Position : textPos;
            if (!binary)
                target = stream!.Position; // truncate() to the current raw write position
        }
        else
        {
            // CPython io.IOBase.truncate(size): size is a raw byte offset from
            // the file start, identical for text and binary wrappers.
            target = Size(sizeValue);
            if (target < 0) throw PyValueError.Create("negative length is not allowed");
        }
        try { stream!.SetLength(Math.Max(0, target)); }
        catch (IOException ex) { throw FileError(ex); }
        return new PyInt(target);
    }

    public override PyObject GetAttribute(string name)
    {
        if (name == "closed") return PyBool.FromBool(closed);
        if (name == "name") return new PyString(filename);
        if (name == "mode") return new PyString(mode);
        if (name == "readable") return new PyBoundMethodDescriptor(this, ReadableMethod);
        if (name == "writable") return new PyBoundMethodDescriptor(this, WritableMethod);
        if (name == "seekable") return new PyBoundMethodDescriptor(this, SeekableMethod);
        if (name == "__iter__") return new PyBoundMethodDescriptor(this, IterMethod);
        if (name == "__next__") return new PyBoundMethodDescriptor(this, NextMethod);
        if (Methods.TryGetValue(name, out var method)) return new PyBoundMethodDescriptor(this, method);
        return base.GetAttribute(name);
    }

    private static readonly PyMethodDescriptor ReadableMethod = new("readable", PyType.ObjectType, (self, args, kwargs) => PyBool.FromBool(((PyFileContextManager)self).readable), minArgs: 1, maxArgs: 1);
    private static readonly PyMethodDescriptor WritableMethod = new("writable", PyType.ObjectType, (self, args, kwargs) => PyBool.FromBool(((PyFileContextManager)self).writable), minArgs: 1, maxArgs: 1);
    private static readonly PyMethodDescriptor SeekableMethod = new("seekable", PyType.ObjectType, (self, args, kwargs) => PyBool.True, minArgs: 1, maxArgs: 1);
    private static readonly PyMethodDescriptor IterMethod = new("__iter__", PyType.ObjectType, (self, args, kwargs) => ((PyFileContextManager)self).GetIterator(), minArgs: 1, maxArgs: 1);
    private static readonly PyMethodDescriptor NextMethod = new("__next__", PyType.ObjectType, (self, args, kwargs) => ((PyFileContextManager)self).Next(), minArgs: 1, maxArgs: 1);
    private static readonly System.Collections.Generic.Dictionary<string, PyMethodDescriptor> Methods = CreateMethods();
    private static System.Collections.Generic.Dictionary<string, PyMethodDescriptor> CreateMethods()
    {
        var methods = new System.Collections.Generic.Dictionary<string, PyMethodDescriptor>();
        foreach (string name in new[] { "read", "readline", "readlines", "write", "writelines", "close", "flush", "seek", "tell", "truncate" })
            methods[name] = new PyMethodDescriptor(name, PyType.ObjectType,
                (self, args, kwargs) => ((PyFileContextManager)self).Invoke(name, args));
        return methods;
    }
    private PyObject Invoke(string name, PyObject[] args)
    {
        int minimum = name is "write" or "seek" or "writelines" ? 1 : 0;
        int maximum = name == "seek" ? 2 : name is "write" or "read" or "readline" or "writelines" or "truncate" ? 1 : 0;
        if (args.Length < minimum || args.Length > maximum) throw PyTypeError.Create($"{name}() takes {maximum} argument{(maximum == 1 ? "" : "s")} ({args.Length} given)");
        if (name == "read" || name == "readline") return Read(Size(args.Length == 0 ? null : args[0]), name == "readline");
        if (name == "readlines") return ReadLines();
        if (name == "write") return Write(args[0]);
        if (name == "writelines") { CheckClosed(); WriteLines(args[0]); return PyNone.Instance; }
        if (name == "close") { Close(); return PyNone.Instance; }
        if (name == "flush") { Flush(); return PyNone.Instance; }
        if (name == "truncate") return Truncate(args.Length == 0 ? null : args[0]);
        CheckClosed();
        Flush();
        if (name == "tell") return new PyInt(binary ? stream!.Position : textPos);
        long offset = Size(args[0]), origin = args.Length == 2 ? Size(args[1]) : 0;
        if (origin < 0 || origin > 2) throw PyValueError.Create($"whence value {origin} unsupported");
        if (!binary && origin != 0 && offset != 0) throw PyOSError.Create("can't do nonzero cur-relative seeks");
        if (binary)
        {
            long target = origin switch
            {
                0 => offset,
                1 => stream!.Position + offset,
                _ => stream!.Length + offset
            };
            if (target < 0)
                throw new PythonException((PyOSError)PyOSError.FromArguments(new PyObject[] { new PyInt(22), new PyString("Invalid argument") }, null));
            try
            {
                long position = stream!.Seek(target, SeekOrigin.Begin);
                reader?.DiscardBufferedData();
                return new PyInt(position);
            }
            catch (IOException ex) { throw FileError(ex); }
        }
        // CPython text seek decodes a cookie into a character offset: replay
        // from the start so tell()/subsequent reads line up in characters.
        try
        {
            stream!.Seek(0, SeekOrigin.Begin);
            reader?.DiscardBufferedData();
            textPos = 0;
            if (origin == 2)
            {
                Read(-1);
            }
            else if (offset > 0)
            {
                Read(offset);
            }
            return new PyInt(textPos);
        }
        catch (IOException ex) { throw FileError(ex); }
    }

}
