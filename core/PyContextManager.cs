using System;
using System.IO;

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
    /// </summary>
    public class PyFileContextManager : PyContextManager
    {
        private readonly string _filename;
        private readonly string _mode;
        private StreamReader? _reader;
        private StreamWriter? _writer;
        private Stream? _stream;
        private bool _closed = false;

        public PyFileContextManager(string filename, string mode = "r")
        {
            _filename = filename ?? throw new ArgumentNullException(nameof(filename));
            _mode = mode ?? "r";
        }

        public override string GetTypeName() => "file";
        public override string ToRepr() => $"<_io.TextIOWrapper name='{_filename}' mode='{_mode}'>";

        public override PyObject Enter()
        {
            try
            {
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
                _writer.Write(content);
                _writer.Flush();
                return new PyInt(content.Length);
            }
            catch (Exception ex)
            {
                throw PyOSError.Create($"Error writing file: {ex.Message}");
            }
        }

        public override PyObject GetAttribute(string name)
        {
            return name switch
            {
                "read" => new PyBuiltinFunction("read", args => Read()),
                "write" => new PyBuiltinFunction("write", args =>
                {
                    if (args.Length != 1)
                        throw PyTypeError.Create("write() takes exactly 1 argument");
                    return Write(args[0]);
                }),
                "close" => new PyBuiltinFunction("close", args =>
                {
                    Close();
                    return PyNone.Instance;
                }),
                "closed" => PyBool.FromBool(_closed),
                _ => base.GetAttribute(name)
            };
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
        public override string ToRepr() => $"<TestContextManager '{_name}'>";

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