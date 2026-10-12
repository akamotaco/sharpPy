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
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
    private static readonly PyType TextType = new("TextIOWrapper", new[] { PyType.ObjectType });
    private static readonly PyType ReaderType = new("BufferedReader", new[] { PyType.ObjectType });
    private static readonly PyType WriterType = new("BufferedWriter", new[] { PyType.ObjectType });
    private static readonly PyType RandomType = new("BufferedRandom", new[] { PyType.ObjectType });

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
                if ((encoding != null && encoding != "utf-8") || (errors != null && errors != "strict"))
                {
                    try { codec = Encoding.GetEncoding(encoding ?? "utf-8"); }
                    catch (ArgumentException) { throw PyLookupError.Create("unknown encoding: " + encoding); }
                    codec = (Encoding)(codec.CodePage == 65001 ? new UTF8Encoding(false) : codec).Clone();
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
        if (!readable) throw PyOSError.Create("not readable");
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
        if (!writable) throw PyOSError.Create("not writable");
        try
        {
            if (append) stream!.Seek(0, SeekOrigin.End);
            if (binary)
            {
                ReadOnlySpan<byte> bytes = value is PyBytes b ? b.Value : value is PyByteArray a ? a.BufferSpan
                    : value is PyMemoryView view ? view.ContiguousSpan()
                    : throw PyTypeError.Create($"a bytes-like object is required, not '{value.GetTypeName()}'");
                stream!.Write(bytes);
                return new PyInt(bytes.Length);
            }
            if (value is not PyString text) throw PyTypeError.Create($"write() argument must be str, not {value.GetTypeName()}");
            string output = text.Value;
            string replacement = newline ?? Environment.NewLine;
            if (replacement != "" && replacement != "\n") output = output.Replace("\n", replacement);
            writer!.Write(output);
            int length = 0;
            for (int p = 0; p < text.Value.Length; p++, length++) if (char.IsSurrogatePair(text.Value, p)) p++;
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
    public override PyObject GetAttribute(string name)
    {
        if (name == "closed") return PyBool.FromBool(closed);
        if (name == "name") return new PyString(filename);
        if (name == "mode") return new PyString(mode);
        if (Methods.TryGetValue(name, out var method)) return new PyBoundMethodDescriptor(this, method);
        return base.GetAttribute(name);
    }
    private static readonly System.Collections.Generic.Dictionary<string, PyMethodDescriptor> Methods = CreateMethods();
    private static System.Collections.Generic.Dictionary<string, PyMethodDescriptor> CreateMethods()
    {
        var methods = new System.Collections.Generic.Dictionary<string, PyMethodDescriptor>();
        foreach (string name in new[] { "read", "readline", "readlines", "write", "close", "flush", "seek", "tell" })
            methods[name] = new PyMethodDescriptor(name, PyType.ObjectType,
                (self, args, kwargs) => ((PyFileContextManager)self).Invoke(name, args));
        return methods;
    }
    private PyObject Invoke(string name, PyObject[] args)
    {
        int minimum = name is "write" or "seek" ? 1 : 0;
        int maximum = name == "seek" ? 2 : name is "write" or "read" or "readline" ? 1 : 0;
        if (args.Length < minimum || args.Length > maximum) throw PyTypeError.Create($"{name}() takes {maximum} argument{(maximum == 1 ? "" : "s")} ({args.Length} given)");
        if (name == "read" || name == "readline") return Read(Size(args.Length == 0 ? null : args[0]), name == "readline");
        if (name == "readlines") return ReadLines();
        if (name == "write") return Write(args[0]);
        if (name == "close") { Close(); return PyNone.Instance; }
        if (name == "flush") { Flush(); return PyNone.Instance; }
        CheckClosed();
        Flush();
        if (name == "tell") return new PyInt(stream!.Position);
        long offset = Size(args[0]), origin = args.Length == 2 ? Size(args[1]) : 0;
        if (origin < 0 || origin > 2) throw PyValueError.Create($"invalid whence ({origin}, should be 0, 1 or 2)");
        if (!binary && (origin != 0 && offset != 0)) throw PyOSError.Create("can't do nonzero cur-relative seeks");
        try
        {
            long position = stream!.Seek(offset, (SeekOrigin)origin);
            reader?.DiscardBufferedData();
            return new PyInt(position);
        }
        catch (IOException ex) { throw FileError(ex); }
    }

}
