using System;
using System.IO;

#if GODOT
using Godot_IO;
#endif

namespace SharpPy
{
    /// <summary>
    /// Base class for Python context managers (__enter__/__exit__ protocol)
    /// </summary>
    public abstract class PyContextManager : PyObject
    {
        public override PyType GetPyType() => PyType.ObjectType;

        /// <summary>
        /// Python __enter__ method - called when entering with block
        /// </summary>
        public abstract PyObject Enter();

        /// <summary>
        /// Python __exit__ method - called when exiting with block
        /// </summary>
        /// <param name="excType">Exception type (None if no exception)</param>
        /// <param name="excValue">Exception value (None if no exception)</param>
        /// <param name="traceback">Traceback (None if no exception)</param>
        /// <returns>True to suppress exception, False to propagate</returns>
        public abstract PyObject Exit(PyObject excType, PyObject excValue, PyObject traceback);

        public override PyObject GetAttribute(string name)
        {
            return name switch
            {
                "__enter__" => new PyBuiltinFunction("__enter__", args =>
                {
                    if (args.Length != 0)
                        throw PyTypeError.Create("__enter__() takes no arguments");
                    return Enter();
                }),
                "__exit__" => new PyBuiltinFunction("__exit__", args =>
                {
                    if (args.Length != 3)
                        throw PyTypeError.Create("__exit__() takes exactly 3 arguments");
                    return Exit(args[0], args[1], args[2]);
                }),
                _ => base.GetAttribute(name)
            };
        }
    }

    /// <summary>
    /// File context manager implementation (CPython compatible)
    /// Godot 환경에서는 res://, user:// 경로를 지원
    /// </summary>
    public class PyFileContextManager : PyContextManager
    {
        private readonly string _filename;
        private readonly string _mode;
        private StreamReader? _reader;
        private StreamWriter? _writer;
        private Stream? _stream;
        private bool _closed = false;
#if GODOT
        private string? _godotContent;  // Godot 전용: 읽기 모드에서 파일 내용 저장
        private bool _isGodotPath = false;
#endif

        public PyFileContextManager(string filename, string mode = "r")
        {
            _filename = filename ?? throw new ArgumentNullException(nameof(filename));
            _mode = mode ?? "r";
        }

        public override string GetTypeName() => "file";
        public override PyString ToRepr() => new PyString($"<_io.TextIOWrapper name='{_filename}' mode='{_mode}'>");

        public override PyObject Enter()
        {
            try
            {
#if GODOT
                // Godot 경로 (res://, user://) 처리
                if (Helper.IsGodotPath(_filename))
                {
                    _isGodotPath = true;
                    switch (_mode.ToLower())
                    {
                        case "r":
                        case "rt":
                            _godotContent = Helper.ReadAllText(_filename);
                            _reader = new StreamReader(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(_godotContent)));
                            break;
                        case "w":
                        case "wt":
                            // 쓰기 모드: MemoryStream 사용, Exit 시 Godot에 저장
                            _stream = new MemoryStream();
                            _writer = new StreamWriter(_stream, System.Text.Encoding.UTF8);
                            break;
                        default:
                            throw PyValueError.Create($"invalid mode for Godot path: '{_mode}'");
                    }
                    return this;
                }
#endif
                // 기본 .NET 파일 처리
                switch (_mode.ToLower())
                {
                    case "r":
                    case "rt":
                        _stream = File.OpenRead(_filename);
                        _reader = new StreamReader(_stream);
                        break;
                    case "w":
                    case "wt":
                        _stream = File.OpenWrite(_filename);
                        _writer = new StreamWriter(_stream);
                        break;
                    case "a":
                    case "at":
                        _stream = File.OpenWrite(_filename);
                        _stream.Seek(0, SeekOrigin.End);
                        _writer = new StreamWriter(_stream);
                        break;
                    default:
                        throw PyValueError.Create($"invalid mode: '{_mode}'");
                }

                return this; // Return self as per CPython
            }
            catch (Exception ex)
            {
                throw PyOSError.Create($"[Errno 2] No such file or directory: '{_filename}': {ex.Message}");
            }
        }

        public override PyObject Exit(PyObject excType, PyObject excValue, PyObject traceback)
        {
            Close();
            return PyBool.False; // Don't suppress exceptions
        }

        private void Close()
        {
            if (!_closed)
            {
                try
                {
#if GODOT
                    // Godot 쓰기 모드: MemoryStream 내용을 파일로 저장
                    if (_isGodotPath && _writer != null && _stream is MemoryStream ms)
                    {
                        _writer.Flush();
                        ms.Position = 0;
                        var content = System.Text.Encoding.UTF8.GetString(ms.ToArray());
                        Helper.WriteAllText(_filename, content);
                    }
#endif
                    _reader?.Dispose();
                    _writer?.Dispose();
                    _stream?.Dispose();
                }
                catch
                {
                    // Ignore errors during close
                }
                finally
                {
                    _closed = true;
                    _reader = null;
                    _writer = null;
                    _stream = null;
                }
            }
        }

        // File operations for testing
        public PyObject Read()
        {
            if (_closed || _reader == null)
                throw PyValueError.Create("I/O operation on closed file.");

            try
            {
                var content = _reader.ReadToEnd();
                return new PyString(content);
            }
            catch (Exception ex)
            {
                throw PyOSError.Create($"Error reading file: {ex.Message}");
            }
        }

        public PyObject Write(PyObject text)
        {
            if (_closed || _writer == null)
                throw PyValueError.Create("I/O operation on closed file.");

            try
            {
                var content = text.ToStr();
                _writer.Write(content.Value);
                _writer.Flush();
                return new PyInt(content.Value.Length);
            }
            catch (Exception ex)
            {
                throw PyOSError.Create($"Error writing file: {ex.Message}");
            }
        }

        public PyObject ReadLines()
        {
            if (_closed || _reader == null)
                throw PyValueError.Create("I/O operation on closed file.");

            try
            {
                var lines = new List<PyObject>();
                string? line;
                while ((line = _reader.ReadLine()) != null)
                {
                    lines.Add(new PyString(line + "\n"));
                }
                return new PyList(lines.ToArray());
            }
            catch (Exception ex)
            {
                throw PyOSError.Create($"Error reading file: {ex.Message}");
            }
        }

        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "read":
                    {
                        var method = new PyMethodDescriptor("read", PyType.ObjectType, (self, args, kwargs) => Read());
                        return new PyBoundMethodDescriptor(this, method);
                    }
                case "readlines":
                    {
                        var method = new PyMethodDescriptor("readlines", PyType.ObjectType, (self, args, kwargs) => ReadLines());
                        return new PyBoundMethodDescriptor(this, method);
                    }
                case "write":
                    {
                        var method = new PyMethodDescriptor("write", PyType.ObjectType, (self, args, kwargs) =>
                        {
                            if (args.Length != 1)
                                throw PyTypeError.Create("write() takes exactly 1 argument");
                            return Write(args[0]);
                        });
                        return new PyBoundMethodDescriptor(this, method);
                    }
                case "close":
                    {
                        var method = new PyMethodDescriptor("close", PyType.ObjectType, (self, args, kwargs) =>
                        {
                            Close();
                            return PyNone.Instance;
                        });
                        return new PyBoundMethodDescriptor(this, method);
                    }
                case "closed":
                    return PyBool.FromBool(_closed);
                default:
                    return base.GetAttribute(name);
            }
        }
    }

    /// <summary>
    /// Simple test context manager for demonstration
    /// </summary>
    public class PyTestContextManager : PyContextManager
    {
        private readonly string _name;

        public PyTestContextManager(string name)
        {
            _name = name;
        }

        public override string GetTypeName() => "test_context_manager";
        public override PyString ToRepr() => new PyString($"<TestContextManager '{_name}'>");

        public override PyObject Enter()
        {
            Console.WriteLine($"Entering context: {_name}");
            return this;
        }

        public override PyObject Exit(PyObject excType, PyObject excValue, PyObject traceback)
        {
            Console.WriteLine($"Exiting context: {_name}");

            // Return True to suppress ValueError, False otherwise
            if (excValue is PyValueError)
            {
                Console.WriteLine($"Suppressing ValueError in context: {_name}");
                return PyBool.True;
            }

            return PyBool.False;
        }
    }
}
