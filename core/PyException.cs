namespace SharpPy
{
    #region Python Exception Hierarchy (Pure PyObject)

    /// <summary>
    /// Python BaseException - 모든 예외의 최상위 클래스
    /// 순수한 PyObject로 유지
    /// </summary>
    public class PyBaseException : PyObject
    {
        public string Message { get; protected set; }
        // Keep the original array until Python observes args; then retain its
        // tuple identity in the same slot, without a second field per exception.
        private object _args;
        public PyObject[] Args
        {
            get => _args is PyTuple tuple ? tuple.Items : (PyObject[])_args;
            protected set => _args = value;
        }

        // CPython 3.12: Exception chaining attributes
        public PyBaseException? __cause__ { get; set; }
        public PyBaseException? __context__ { get; set; }
        public bool __suppress_context__ { get; set; }

        // CPython 3.12: Traceback information
        public PyTraceback? __traceback__ { get; set; }

        public PyBaseException(string message = "", params PyObject[] args)
        {
            Message = message ?? "";
            Args = args.Length > 0 ? args : new PyObject[] { new PyString(Message) };
            __cause__ = null;
            __context__ = null;
            __suppress_context__ = false;
            __traceback__ = null;
        }

        // Preserve explicit Python arguments, including the empty tuple, without
        // manufacturing a message argument or calling user __str__ methods.
        protected internal PyBaseException(PyObject[] args, PyDict kwargs = null)
        {
            if (kwargs != null && kwargs.Length() != 0)
                throw PyTypeError.Create($"{GetTypeName()}() takes no keyword arguments");
            Args = args;
            Message = args.Length > 0 && args[0] is PyString text ? text.Value : "";
        }

        public override PyType GetPyType() => PyType.BaseExceptionType;
        public override string GetTypeName() => "BaseException";

        public override PyString ToStr()
        {
            if (Args.Length == 0)
                return PyString.Empty;
            if (Args.Length == 1)
                return Args[0].ToStr();
            return new PyString($"({string.Join(", ", Args.Select(a => a.ToRepr().Value))})");
        }

        public override PyString ToRepr()
        {
            if (Args.Length == 1)
                return new PyString($"{GetTypeName()}({Args[0].ToRepr().Value})");
            return new PyString($"{GetTypeName()}({string.Join(", ", Args.Select(a => a.ToRepr().Value))})");
        }

        public override string ToString()
        {
            // CPython behavior: str(exception) returns the message
            return ToStr().Value;
        }

        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "args":
                    if (_args is not PyTuple argsTuple)
                    {
                        var items = Args;
                        argsTuple = items.Length == 0 ? TupleCache.Empty : new PyTuple(items);
                        _args = argsTuple;
                    }
                    return argsTuple;
                case "__cause__":
                    return (PyObject?)__cause__ ?? PyNone.Instance;
                case "__context__":
                    return (PyObject?)__context__ ?? PyNone.Instance;
                case "__suppress_context__":
                    return __suppress_context__ ? PyBool.True : PyBool.False;
                case "__traceback__":
                    return (PyObject?)__traceback__ ?? PyNone.Instance;
                case "with_traceback":
                    // CPython 3.12: Objects/exceptions.c BaseException.with_traceback
                    return new PyBoundBuiltinMethod(this, new PyBuiltinMethod("with_traceback", (self, a) =>
                    {
                        if (a.Length != 1)
                        {
                            throw PyTypeError.Create($"BaseException.with_traceback() takes exactly one argument ({a.Length} given)");
                        }
                        var excSelf = (PyException)self;
                        if (a[0] is PyTraceback tb)
                        {
                            excSelf.__traceback__ = tb;
                        }
                        else if (a[0] is PyNone)
                        {
                            excSelf.__traceback__ = null;
                        }
                        else
                        {
                            throw PyTypeError.Create("__traceback__ must be a traceback or None");
                        }
                        return self;
                    }, 1));
                default:
                    return base.GetAttribute(name);
            }
        }

        public override void SetAttribute(string name, PyObject value)
        {
            switch (name)
            {
                case "args":
                    PyTuple replacement;
                    if (value.GetType() == typeof(PyTuple)) replacement = (PyTuple)value;
                    else if (value.GetType() == typeof(PyList))
                    {
                        var items = ((PyList)value).Items;
                        replacement = items.Length == 0 ? TupleCache.Empty : new PyTuple(items);
                    }
                    else
                    {
                        var iterator = value.GetIterator();
                        // Use CLR List growth when no hint is available; CPython's
                        // initial tuple capacity of ten is an allocation strategy.
                        long hint = value.LengthHint(0);
                        if (hint > Array.MaxLength) throw PyMemoryError.Create();
                        var items = new List<PyObject>((int)hint);
                        while (true)
                        {
                            PyObject item;
                            try { item = iterator.Next(); }
                            catch (PythonException ex) when (ex.PyException is PyStopIteration) { break; }
                            if (item == null) break;
                            items.Add(item);
                        }
                        replacement = items.Count == 0 ? TupleCache.Empty : new PyTuple(items.ToArray());
                    }
                    // Commit only after conversion; callbacks may observe or
                    // replace the previous args while the iterable is consumed.
                    _args = replacement;
                    break;
                case "__cause__":
                    if (value is PyNone)
                        __cause__ = null;
                    else if (value is PyBaseException exc)
                        __cause__ = exc;
                    else
                        throw PyTypeError.Create($"exception cause must be None or derive from BaseException");
                    break;
                case "__context__":
                    if (value is PyNone)
                        __context__ = null;
                    else if (value is PyBaseException exc)
                        __context__ = exc;
                    else
                        throw PyTypeError.Create($"exception context must be None or derive from BaseException");
                    break;
                case "__suppress_context__":
                    __suppress_context__ = value is PyBool b ? b.Value : !(value is PyNone || (value is PyInt i && i.Value == 0));
                    break;
                case "__traceback__":
                    if (value is PyNone)
                        __traceback__ = null;
                    else if (value is PyTraceback tb)
                        __traceback__ = tb;
                    else
                        throw PyTypeError.Create($"__traceback__ must be a traceback object or None");
                    break;
                default:
                    base.SetAttribute(name, value);
                    break;
            }
        }

        public override void DelAttribute(string name)
        {
            if (name == "args") throw PyTypeError.Create("args may not be deleted");
            base.DelAttribute(name);
        }

        /// <summary>
        /// C# throw를 위한 System.Exception 생성
        /// </summary>
        public static System.Exception Create(string message = "")
        {
            var pyException = new PyBaseException(message);
            return new PythonException(pyException);
        }
    }

    /// <summary>
    /// Exception - 일반적인 예외들의 기본 클래스
    /// </summary>
    public class PyException : PyBaseException
    {
        public PyClass? OriginalClass { get; set; } // Store original user-defined class
        public PyClassInstance? OriginalInstance { get; set; } // Store original user-defined instance

        public PyException(string message = "", params PyObject[] args) : base(message, args) { }

        protected internal PyException(PyObject[] args, PyDict kwargs = null) : base(args, kwargs) { }

        // Constructor for wrapping user-defined exceptions with class info
        public PyException(string message, PyClass? originalClass) : base(message)
        {
            OriginalClass = originalClass;
        }

        // Constructor for wrapping user-defined exception instances
        public PyException(string message, PyClass? originalClass, PyClassInstance? originalInstance) : base(message)
        {
            OriginalClass = originalClass;
            OriginalInstance = originalInstance;
            // CPython 3.12: BaseException.__init__ stores the *constructor* arguments as
            // __args__ (raise E -> (), raise E("x") -> ("x",)), not the instance repr.
            // str()/repr() still delegate to OriginalInstance below.
            if (originalInstance != null && originalInstance.ConstructorArgs != null)
            {
                Args = originalInstance.ConstructorArgs;
            }
        }

        public override PyType GetPyType() => OriginalClass ?? PyType.ExceptionType;
        public override string GetTypeName() => OriginalClass?.Name ?? "Exception";

        // CPython 3.12: Objects/exceptions.c:79-105 (BaseException_str)
        // For user-defined exceptions, delegate ToStr() to OriginalInstance
        // This ensures BaseException.__str__ descriptor is properly invoked
        public override PyString ToStr()
        {
            // CPython 3.12: User-defined exception instances use __str__ method from class hierarchy
            if (OriginalInstance != null)
            {
                return OriginalInstance.ToStr();
            }

            // CPython 3.12: C-level exceptions use Args-based string representation
            return base.ToStr();
        }

        // CPython 3.12: Objects/exceptions.c:42-849
        // Exception attribute lookup follows standard descriptor protocol
        // User-defined exception instances delegate ALL attribute access to OriginalInstance
        // This ensures descriptors (like BaseException.args) are properly invoked
        public override PyObject GetAttribute(string name)
        {
            // CPython 3.12: Exception chaining attributes are stored on the exception wrapper,
            // not on the user-defined instance. Check these first before delegating.
            // Corresponds to BaseException attributes in Objects/exceptions.c:785-795
            switch (name)
            {
                case "args":
                case "__cause__":
                case "__context__":
                case "__suppress_context__":
                case "__traceback__":
                    // These are exception-specific attributes stored on the wrapper
                    return base.GetAttribute(name);
            }

            // CPython 3.12: Objects/exceptions.c:42-849
            // For user-defined exceptions (Python classes inheriting from Exception),
            // delegate remaining attribute access to the original PyClassInstance
            // This ensures proper descriptor protocol and MRO traversal for custom attributes
            if (OriginalInstance != null)
            {
                return OriginalInstance.GetAttribute(name);
            }

            // CPython 3.12: Objects/exceptions.c:785-795
            // For C-level exceptions (PyBaseException), use standard attribute lookup
            // This includes built-in exception types like TypeError, ValueError, etc.
            return base.GetAttribute(name);
        }

        public new static System.Exception Create(string message = "")
        {
            var pyException = new PyException(message);
            return new PythonException(pyException);
        }
    }

    #endregion

    #region Core Exception Types

    // 타입 관련 예외들
    public class PyTypeError : PyException
    {
        internal PyTypeError(PyObject[] args, PyDict kwargs) : base(args, kwargs) { }

        public PyTypeError(string message) : base(message) { }
        public override PyType GetPyType() => PyType.TypeErrorType;
        public override string GetTypeName() => "TypeError";

        public new static System.Exception Create(string message)
        {
            var pyException = new PyTypeError(message);
            return new PythonException(pyException);
        }
    }

    public class PyValueError : PyException
    {
        internal PyValueError(PyObject[] args, PyDict kwargs) : base(args, kwargs) { }

        public PyValueError(string message) : base(message) { }
        public override PyType GetPyType() => PyType.ValueErrorType;
        public override string GetTypeName() => "ValueError";

        public new static System.Exception Create(string message)
        {
            var pyException = new PyValueError(message);
            return new PythonException(pyException);
        }
    }

    public class PyAttributeError : PyException
    {
        private PyObject _name;
        private PyObject _obj;

        internal PyAttributeError(PyObject[] args, PyDict kwargs) : base(args)
        {
            if (kwargs == null || kwargs.Length() == 0) return;
            if (kwargs.Length() > 2)
                throw PyTypeError.Create($"AttributeError() takes at most 2 keyword arguments ({kwargs.Length()} given)");
            foreach (var pair in kwargs.InternalDict)
            {
                if (pair.Key is PyString { Value: "name" }) _name = pair.Value;
                else if (pair.Key is PyString { Value: "obj" }) _obj = pair.Value;
                else throw PyTypeError.Create($"'{pair.Key.ToStr().Value}' is an invalid keyword argument for AttributeError()");
            }
        }

        public PyAttributeError(string message) : base(message) { }
        public override PyObject GetAttribute(string name) => name switch
        {
            "name" => _name ?? PyNone.Instance,
            "obj" => _obj ?? PyNone.Instance,
            _ => base.GetAttribute(name)
        };

        public override void SetAttribute(string name, PyObject value)
        {
            if (name == "name") _name = value;
            else if (name == "obj") _obj = value;
            else base.SetAttribute(name, value);
        }

        public override void DelAttribute(string name)
        {
            if (name == "name") _name = null;
            else if (name == "obj") _obj = null;
            else base.DelAttribute(name);
        }

        public override PyType GetPyType() => PyType.AttributeErrorType;
        public override string GetTypeName() => "AttributeError";

        public new static System.Exception Create(string message)
        {
            var pyException = new PyAttributeError(message);
            return new PythonException(pyException);
        }
    }

    public class PyNameError : PyException
    {
        private PyObject _name = PyNone.Instance;
        internal PyNameError(PyObject[] args, PyDict kwargs) : base(args)
        {
            if (kwargs == null || kwargs.Length() == 0) return;
            if (kwargs.Length() > 1)
                throw PyTypeError.Create($"NameError() takes at most 1 keyword argument ({kwargs.Length()} given)");
            foreach (var pair in kwargs.InternalDict)
            {
                if (pair.Key is PyString { Value: "name" }) _name = pair.Value;
                else throw PyTypeError.Create($"'{pair.Key.ToStr().Value}' is an invalid keyword argument for NameError()");
            }
        }

        public PyNameError(string message) : base(message) { }

        public override PyObject GetAttribute(string name) =>
            name == "name" ? _name : base.GetAttribute(name);

        public override void SetAttribute(string name, PyObject value)
        {
            if (name == "name") _name = value;
            else base.SetAttribute(name, value);
        }

        public override void DelAttribute(string name)
        {
            if (name == "name") _name = PyNone.Instance;
            else base.DelAttribute(name);
        }

        public override PyType GetPyType() => PyType.NameErrorType;
        public override string GetTypeName() => "NameError";

        public new static System.Exception Create(string message)
        {
            var pyException = new PyNameError(message);
            return new PythonException(pyException);
        }
        public static System.Exception Create(string message, string name)
        {
            var pyException = new PyNameError(message);
            pyException.SetAttribute("name", new PyString(name));
            return new PythonException(pyException);
        }
    }

    public class PyUnboundLocalError : PyNameError
    {
        internal PyUnboundLocalError(PyObject[] args, PyDict kwargs) : base(args, kwargs) { }

        public PyUnboundLocalError(string message) : base(message) { }
        public override PyType GetPyType() => PyType.UnboundLocalErrorType;
        public override string GetTypeName() => "UnboundLocalError";

        public new static System.Exception Create(string message)
        {
            var pyException = new PyUnboundLocalError(message);
            return new PythonException(pyException);
        }
    }

    // 산술 예외들
    public class PyArithmeticError : PyException
    {
        internal PyArithmeticError(PyObject[] args, PyDict kwargs) : base(args, kwargs) { }

        public PyArithmeticError(string message = "") : base(message) { }
        public override PyType GetPyType() => PyType.ArithmeticErrorType;
        public override string GetTypeName() => "ArithmeticError";

        public new static System.Exception Create(string message = "")
        {
            var pyException = new PyArithmeticError(message);
            return new PythonException(pyException);
        }
    }

    public class PyFloatingPointError : PyArithmeticError
    {
        internal PyFloatingPointError(PyObject[] args, PyDict kwargs) : base(args, kwargs) { }
        public PyFloatingPointError(string message = "") : base(message) { }
        public override PyType GetPyType() => PyType.FloatingPointErrorType;
        public override string GetTypeName() => "FloatingPointError";
    }

    public class PyZeroDivisionError : PyArithmeticError
    {
        internal PyZeroDivisionError(PyObject[] args, PyDict kwargs) : base(args, kwargs) { }

        public PyZeroDivisionError(string message = "division by zero") : base(message) { }
        public override PyType GetPyType() => PyType.ZeroDivisionErrorType;
        public override string GetTypeName() => "ZeroDivisionError";

        public new static System.Exception Create(string message = "division by zero")
        {
            var pyException = new PyZeroDivisionError(message);
            return new PythonException(pyException);
        }
    }

    public class PyOverflowError : PyArithmeticError
    {
        internal PyOverflowError(PyObject[] args, PyDict kwargs) : base(args, kwargs) { }

        public PyOverflowError(string message = "math range error") : base(message) { }
        public override PyType GetPyType() => PyType.OverflowErrorType;
        public override string GetTypeName() => "OverflowError";

        public new static System.Exception Create(string message = "math range error")
        {
            var pyException = new PyOverflowError(message);
            return new PythonException(pyException);
        }
    }

    // 룩업 예외들
    public class PyLookupError : PyException
    {
        internal PyLookupError(PyObject[] args, PyDict kwargs) : base(args, kwargs) { }

        public PyLookupError(string message = "") : base(message) { }
        public override PyType GetPyType() => PyType.LookupErrorType;
        public override string GetTypeName() => "LookupError";

        public new static System.Exception Create(string message = "")
        {
            var pyException = new PyLookupError(message);
            return new PythonException(pyException);
        }
    }

    public class PyIndexError : PyLookupError
    {
        internal PyIndexError(PyObject[] args, PyDict kwargs) : base(args, kwargs) { }

        public PyIndexError(string message = "list index out of range") : base(message) { }
        public override PyType GetPyType() => PyType.IndexErrorType;
        public override string GetTypeName() => "IndexError";

        public new static System.Exception Create(string message = "list index out of range")
        {
            var pyException = new PyIndexError(message);
            return new PythonException(pyException);
        }
    }

    public class PyKeyError : PyLookupError
    {
        public PyKeyError(PyObject key) : base(new[] { key }, null) { }
        internal PyKeyError(PyObject[] args, PyDict kwargs) : base(args, kwargs) { }
        public PyKeyError(string message) : base(message) { }
        public override PyString ToStr()
        {
            if (Args.Length != 1) return base.ToStr();
            var key = Args[0];
            if (key is PyClassInstance)
            {
                var method = key.LookupSpecialMethodUncached("__repr__");
                if (method != null)
                {
                    var result = key.BindSpecialMethod(method).Call(Array.Empty<PyObject>(), null);
                    if (result is PyString text) return text;
                    throw PyTypeError.Create($"__repr__ returned non-string (type {result.GetTypeName()})");
                }
            }
            return key.ToRepr();
        }
        public override PyType GetPyType() => PyType.KeyErrorType;
        public override string GetTypeName() => "KeyError";

        public static System.Exception Create(string key)
        {
            var pyException = new PyKeyError(key);
            return new PythonException(pyException);
        }

        public static System.Exception Create(PyObject key)
        {
            var pyException = new PyKeyError(key);
            return new PythonException(pyException);
        }
    }

    // 런타임 예외들
    public class PyRuntimeError : PyException
    {
        internal PyRuntimeError(PyObject[] args, PyDict kwargs) : base(args, kwargs) { }

        public PyRuntimeError(string message = "") : base(message) { }
        public override PyType GetPyType() => PyType.RuntimeErrorType;
        public override string GetTypeName() => "RuntimeError";

        public new static System.Exception Create(string message = "")
        {
            var pyException = new PyRuntimeError(message);
            return new PythonException(pyException);
        }
    }

    // CPython 3.12: Objects/exceptions.c - SystemError
    // Internal error in the interpreter
    public class PySystemError : PyException
    {
        internal PySystemError(PyObject[] args, PyDict kwargs) : base(args, kwargs) { }

        public PySystemError(string message = "") : base(message) { }
        public override PyType GetPyType() => PyType.SystemErrorType;
        public override string GetTypeName() => "SystemError";

        public new static System.Exception Create(string message = "")
        {
            var pyException = new PySystemError(message);
            return new PythonException(pyException);
        }
    }

    public class PyNotImplementedError : PyRuntimeError
    {
        internal PyNotImplementedError(PyObject[] args, PyDict kwargs) : base(args, kwargs) { }

        public PyNotImplementedError(string message = "method not implemented") : base(message) { }
        public override PyType GetPyType() => PyType.NotImplementedErrorType;
        public override string GetTypeName() => "NotImplementedError";

        public new static System.Exception Create(string message = "method not implemented")
        {
            var pyException = new PyNotImplementedError(message);
            return new PythonException(pyException);
        }
    }

    public class PyRecursionError : PyRuntimeError
    {
        internal PyRecursionError(PyObject[] args, PyDict kwargs) : base(args, kwargs) { }

        public PyRecursionError(string message = "maximum recursion depth exceeded") : base(message) { }
        public override PyType GetPyType() => PyType.RecursionErrorType;
        public override string GetTypeName() => "RecursionError";

        public new static System.Exception Create(string message = "maximum recursion depth exceeded")
        {
            var pyException = new PyRecursionError(message);
            return new PythonException(pyException);
        }
    }

    // Import 예외들
    public class PyImportError : PyException
    {
        public string ModuleName { get; }

        public PyImportError(string message, string moduleName = null) : base(message)
        {
            ModuleName = moduleName;
        }

        public override PyType GetPyType() => PyType.ImportErrorType;
        public override string GetTypeName() => "ImportError";

        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "name":
                    return ModuleName != null ? new PyString(ModuleName) : PyNone.Instance;
                default:
                    return base.GetAttribute(name);
            }
        }

        public new static System.Exception Create(string message, string moduleName = null)
        {
            var pyException = new PyImportError(message, moduleName);
            return new PythonException(pyException);
        }
    }

    public class PyModuleNotFoundError : PyImportError
    {
        public PyModuleNotFoundError(string message, string moduleName = null) 
            : base(message, moduleName) { }
        
        public override PyType GetPyType() => PyType.ModuleNotFoundErrorType;
        public override string GetTypeName() => "ModuleNotFoundError";

        public new static System.Exception Create(string message, string moduleName = null)
        {
            var pyException = new PyModuleNotFoundError(message, moduleName);
            return new PythonException(pyException);
        }
    }

    // 시스템 예외들
    public class PySystemExit : PyBaseException
    {
        public int ExitCode { get; }

        public PySystemExit(int code = 0) : base($"exit code {code}")
        {
            ExitCode = code;
        }

        public override PyType GetPyType() => PyType.SystemExitType;
        public override string GetTypeName() => "SystemExit";

        public new static System.Exception Create(int code = 0)
        {
            var pyException = new PySystemExit(code);
            return new PythonException(pyException);
        }
    }

    public class PyKeyboardInterrupt : PyBaseException
    {
        public PyKeyboardInterrupt() : base("keyboard interrupt") { }
        public override PyType GetPyType() => PyType.KeyboardInterruptType;
        public override string GetTypeName() => "KeyboardInterrupt";

        public new static System.Exception Create()
        {
            var pyException = new PyKeyboardInterrupt();
            return new PythonException(pyException);
        }
    }

    // 구문 예외
    public class PySyntaxError : PyException
    {
        private PyTuple _details;

        internal static PythonException IntegerLiteralLimit(string message, string filename, int line, string source)
        {
            var error = new PySyntaxError(message, filename, line);
            error._details = new PyTuple(new PyString(filename), new PyInt(line), SmallIntCache.GetOrCreate(0),
                new PyString((source ?? "") + "\n"), new PyInt(line), SmallIntCache.GetOrCreate(0));
            error.Args = new PyObject[] { new PyString(message), error._details };
            return new PythonException(error);
        }

        public string FileName { get; }
        public int LineNumber { get; }

        public PySyntaxError(string message, string fileName = "<unknown>", int lineNumber = 0) 
            : base(message)
        {
            FileName = fileName;
            LineNumber = lineNumber;
        }

        public override PyType GetPyType() => PyType.SyntaxErrorType;
        public override string GetTypeName() => "SyntaxError";

        public override PyString ToStr()
        {
            return new PyString(LineNumber > 0 ? $"{Message} ({FileName}, line {LineNumber})" : Message);
        }

        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "msg": return new PyString(Message);
                case "offset": return _details == null ? PyNone.Instance : _details.Items[2];
                case "text": return _details == null ? PyNone.Instance : _details.Items[3];
                case "end_lineno": return _details == null ? PyNone.Instance : _details.Items[4];
                case "end_offset": return _details == null ? PyNone.Instance : _details.Items[5];
                case "filename":
                    return new PyString(FileName);
                case "lineno":
                    return new PyInt(LineNumber);
                default:
                    return base.GetAttribute(name);
            }
        }

        public new static System.Exception Create(string message, string fileName = "<unknown>", int lineNumber = 0)
        {
            var pyException = new PySyntaxError(message, fileName, lineNumber);
            return new PythonException(pyException);
        }
    }

    public class PyIndentationError : PySyntaxError
    {
        public PyIndentationError(string message, string fileName = null, int lineNumber = 0) 
            : base(message, fileName, lineNumber) { }
        
        public override PyType GetPyType() => PyType.IndentationErrorType;
        public override string GetTypeName() => "IndentationError";

        public new static System.Exception Create(string message, string fileName = "<unknown>", int lineNumber = 0)
        {
            var pyException = new PyIndentationError(message, fileName, lineNumber);
            return new PythonException(pyException);
        }
    }

    // Iterator 예외
    public class PyStopIteration : PyException
    {
        public PyObject Value { get; private set; }

        public PyStopIteration(PyObject value = null)
            : this(value == null ? Array.Empty<PyObject>() : new[] { value }) { }

        internal PyStopIteration(PyObject[] args) : base(args)
        {
            Value = args.Length > 0 ? args[0] : PyNone.Instance;
        }

        public override PyType GetPyType() => PyType.StopIterationType;
        public override string GetTypeName() => "StopIteration";

        public override PyObject GetAttribute(string name)
        {
            if (name == "value")
                return Value;
            return base.GetAttribute(name);
        }

        public override void SetAttribute(string name, PyObject value)
        {
            if (name == "value") Value = value;
            else base.SetAttribute(name, value);
        }

        public new static System.Exception Create(PyObject value = null)
        {
            var pyException = new PyStopIteration(value is PyNone ? null : value);
            return new PythonException(pyException);
        }
    }

    /// <summary>
    /// CPython 3.12: StopAsyncIteration - used for async iterator protocol
    /// </summary>
    public class PyStopAsyncIteration : PyException
    {
        public PyStopAsyncIteration(PyObject value = null) : base(value == null ? Array.Empty<PyObject>() : new[] { value }, null) { }
        public PyStopAsyncIteration(PyObject[] args, PyDict kwargs) : base(args, kwargs) { }

        public override PyType GetPyType() => PyType.StopAsyncIterationType;
        public override string GetTypeName() => "StopAsyncIteration";

        public new static System.Exception Create(PyObject value = null)
        {
            var pyException = new PyStopAsyncIteration(value);
            return new PythonException(pyException);
        }
    }

    /// <summary>
    /// CPython 3.12: GeneratorExit exception - used for generator cleanup
    /// </summary>
    public class PyGeneratorExit : PyBaseException
    {
        internal PyGeneratorExit(PyObject[] args, PyDict kwargs) : base(args, kwargs) { }

        public PyGeneratorExit() : base(Array.Empty<PyObject>()) { }

        public PyGeneratorExit(string message) : base(message)
        {
        }

        public override PyType GetPyType() => PyType.GeneratorExitType;
        public override string GetTypeName() => "GeneratorExit";

        public new static System.Exception Create(string message = "generator exit")
        {
            var pyException = new PyGeneratorExit(message);
            return new PythonException(pyException);
        }
    }

    #endregion

    #region C# Exception Bridge

    /// <summary>
    /// Python 예외를 C# Exception 시스템과 연결하는 브릿지
    /// </summary>
    public class PythonException : System.Exception
    {
        public PyBaseException PyException { get; }

        // CPython-style error location information
        public string? FileName { get; set; }
        public int LineNumber { get; set; } = -1;
        public int ColumnOffset { get; set; } = -1;
        public List<string>? SourceLines { get; set; } // Source code lines for context display

        // CPython 3.12: Track if exception is from RERAISE to preserve traceback
        public bool FromReraise { get; set; } = false;

        public PythonException(PyBaseException pyException)
            : base(pyException.Message)
        {
            PyException = pyException;
        }

        public PythonException(PyBaseException pyException, string? fileName, int lineNumber, int columnOffset = -1)
            : base(pyException.Message)
        {
            PyException = pyException;
            FileName = fileName;
            LineNumber = lineNumber;
            ColumnOffset = columnOffset;
        }

        // CPython 3.12: Constructor for RERAISE - preserves existing traceback
        public PythonException(PyBaseException pyException, bool fromReraise)
            : base(pyException.Message)
        {
            PyException = pyException;
            FromReraise = fromReraise;
        }

        public override string Message => PyException.ToStr().Value;

        public override string ToString()
        {
            var baseStr = PyException.ToRepr().Value;

            // CPython-style location information with source context
            if (!string.IsNullOrEmpty(FileName) && LineNumber > 0)
            {
                var result = new System.Text.StringBuilder();
                result.AppendLine("Traceback (most recent call last):");

                // File location info
                var locationStr = $"  File \"{FileName}\", line {LineNumber}, in <module>";
                result.AppendLine(locationStr);

                // Source code context (if available)
                if (SourceLines != null && LineNumber > 0 && LineNumber <= SourceLines.Count)
                {
                    var sourceLine = SourceLines[LineNumber - 1]; // Convert to 0-based index
                    result.AppendLine($"    {sourceLine}");

                    // Add position marker if column offset is available
                    if (ColumnOffset >= 0 && ColumnOffset < sourceLine.Length)
                    {
                        var spaces = new string(' ', 4 + ColumnOffset); // 4 spaces for indentation + column offset
                        result.AppendLine($"{spaces}^");
                    }
                }

                // Exception type and message
                result.Append($"{PyException.GetTypeName()}: {PyException.ToStr().Value}");

                return result.ToString();
            }

            return baseStr;
        }
    }

    #endregion

    #region AssertionError

    /// <summary>
    /// Python AssertionError - assertion failed
    /// </summary>
    public class PyAssertionError : PyException
    {
        internal PyAssertionError(PyObject[] args, PyDict kwargs) : base(args, kwargs) { }

        public PyAssertionError(string message = "") : base(message) { }
        
        public override PyType GetPyType() => PyType.AssertionErrorType;
        public override string GetTypeName() => "AssertionError";
        
        public new static PythonException Create(string message = "")
        {
            var pyException = new PyAssertionError(message);
            return new PythonException(pyException);
        }
    }

    #endregion

    #region Buffer Exception

    /// <summary>
    /// Python BufferError - buffer protocol violation
    /// CPython 3.12: Raised when buffer operations fail
    /// </summary>
    public class PyBufferError : PyException
    {
        public PyBufferError(string message = "") : base(message) { }

        public override string GetTypeName() => "BufferError";
        public override PyType GetPyType() => PyType.BufferErrorType;

        public new static PythonException Create(string message = "")
        {
            var pyException = new PyBufferError(message);
            return new PythonException(pyException);
        }
    }

    #endregion

    #region OS Exceptions

    /// <summary>
    /// Python OSError - 운영 체제 관련 에러
    /// </summary>
    public class PyOSError : PyException
    {
        private PyObject _errno, _strerror, _filename, _winerror;
        // Most errors have no characters_written. Share the filename2 slot so
        // ordinary OSError instances keep their existing size and allocations.
        private object _filename2;
        private sealed class WrittenState
        {
            internal PyObject Filename2;
            internal long Value;
        }
        private PyObject Filename2
        {
            get => _filename2 is WrittenState state ? state.Filename2 : (PyObject)_filename2;
            set { if (_filename2 is WrittenState state) state.Filename2 = value; else _filename2 = value; }
        }
        private void SetWritten(long value)
        {
            if (value == -1) _filename2 = Filename2;
            else if (_filename2 is WrittenState state) state.Value = value;
            else _filename2 = new WrittenState { Filename2 = Filename2, Value = value };
        }
        private static long AsWrittenCount(PyObject value)
        {
            var index = TryGetIndex(value);
            if (index == null)
                throw PyTypeError.Create($"'{value.GetTypeName()}' object cannot be interpreted as an integer");
            if (index.Value < nint.MinValue || index.Value > nint.MaxValue)
                throw PyValueError.Create($"cannot fit '{value.GetTypeName()}' into an index-sized integer");
            return (long)index.Value;
        }
        private static bool IsNumber(PyObject value) =>
            value is PyInt or PyBool or PyIntSubclass or PyFloat or PyComplex ||
            value.LookupSpecialMethodUncached("__index__") != null ||
            value.LookupSpecialMethodUncached("__int__") != null ||
            value.LookupSpecialMethodUncached("__float__") != null;

        private static bool IsBlockingErrno(System.Numerics.BigInteger code)
        {
            if (OperatingSystem.IsWindows())
                return code == 11 || (code >= 10035 && code <= 10037);
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsIOS() ||
                OperatingSystem.IsTvOS() || OperatingSystem.IsMacCatalyst() || OperatingSystem.IsFreeBSD())
                return code == 35 || code == 36 || code == 37;
            return code == 11 || code == 114 || code == 115; // Linux / Android
        }

        // CPython 3.12 PC/errmap.h and oserror_parse_args. Windows C long is
        // 32-bit even on a 64-bit host. Never mutate the caller's argument array.
        private static PyObject[] NormalizeWindowsArguments(PyObject[] args)
        {
            if (!OperatingSystem.IsWindows() || args.Length < 4 || args.Length > 5)
                return args;
            int code;
            // PyLong_AsLong reads int-subclass storage without invoking conversion hooks.
            var number = args[3] as PyInt ?? (args[3] as PyIntSubclass)?.GetIntValue();
            if (number != null)
            {
                if (number.Value < int.MinValue || number.Value > int.MaxValue)
                    throw PyOverflowError.Create("Python int too large to convert to C long");
                code = (int)number.Value;
            }
            else if (args[3] is PyBool boolean) code = boolean.Value ? 1 : 0;
            else return args;
            var translated = (PyObject[])args.Clone();
            translated[0] = new PyInt(WindowsErrorToErrno(code));
            return translated;
        }

        private static int WindowsErrorToErrno(int code)
        {
            // FACILITY_WIN32 HRESULT and Winsock errno aliases.
            if ((code & 0xffff0000L) == 0x80070000L) code &= 0xffff;
            if (code >= 10000 && code < 12000)
                return code is 10004 or 10009 or 10013 or 10014 or 10022 or 10024
                    ? code - 10000 : code;
            return code switch
            {
                2 or 3 or 15 or 18 or 53 or 67 or 161 or 206 => 2, // ENOENT
                10 => 7, // E2BIG
                11 or >= 188 and <= 202 => 8, // ENOEXEC
                6 or 114 or 130 => 9, // EBADF
                128 or 129 => 10, // ECHILD
                89 or 164 or 215 => 11, // EAGAIN
                7 or 8 or 9 or 1816 => 12, // ENOMEM
                5 or 16 or >= 19 and <= 36 or 65 or 82 or 83 or 108 or 132 or 158 or 167 => 13,
                80 or 183 => 17, // EEXIST
                17 => 18, // EXDEV
                267 => 20, // ENOTDIR
                4 => 24, // EMFILE
                112 => 28, // ENOSPC
                109 or 232 => 32, // EPIPE
                145 => 41, // ENOTEMPTY (Windows CRT)
                1113 => 42, // EILSEQ (Windows CRT)
                _ => 22 // EINVAL, including unknown errors
            };
        }

        // Remaining entries from CPython 3.12 exceptions.c errnomap. Windows
        // uses Winsock values for socket errors, not the CRT errno constants.
        private static PyOSError FromRemainingErrno(System.Numerics.BigInteger value, PyObject[] args)
        {
            if (value < 3 || value > 10061) return new PyOSError(args, null, true);
            int code = (int)value;
            switch (code)
            {
                case 3: return new PyProcessLookupError(args, null, true);
                case 4: return new PyInterruptedError(args, null, true);
                case 10: return new PyChildProcessError(args, null, true);
                case 32: return new PyBrokenPipeError(args, null, true);
            }
            bool windows = OperatingSystem.IsWindows();
            bool bsd = OperatingSystem.IsMacOS() || OperatingSystem.IsIOS() ||
                OperatingSystem.IsTvOS() || OperatingSystem.IsMacCatalyst() || OperatingSystem.IsFreeBSD();
            if (code == (windows ? 10058 : bsd ? 58 : 108)) return new PyBrokenPipeError(args, null, true);
            if (code == (windows ? 10053 : bsd ? 53 : 103)) return new PyConnectionAbortedError(args, null, true);
            if (code == (windows ? 10061 : bsd ? 61 : 111)) return new PyConnectionRefusedError(args, null, true);
            if (code == (windows ? 10054 : bsd ? 54 : 104)) return new PyConnectionResetError(args, null, true);
            if (code == (windows ? 10060 : bsd ? 60 : 110)) return new PyTimeoutError(args, null, true);
            return new PyOSError(args, null, true);
        }

        // Only int subclasses need Python hash/equality dispatch. Keep this table
        // lazy so ordinary int construction retains its allocation-free lookup.
        private static class SubclassErrnoMap
        {
            internal static readonly System.Collections.Generic.Dictionary<long, (PyInt Key, PyType Type)> Entries = Build();
            private static System.Collections.Generic.Dictionary<long, (PyInt Key, PyType Type)> Build()
            {
                var map = new System.Collections.Generic.Dictionary<long, (PyInt Key, PyType Type)>();
                void Add(long code, PyType type) => map[code] = (SmallIntCache.GetOrCreate(code), type);
                Add(1, PyType.PermissionErrorType);
                Add(13, PyType.PermissionErrorType);
                Add(2, PyType.FileNotFoundErrorType);
                Add(17, PyType.FileExistsErrorType);
                Add(20, PyType.NotADirectoryErrorType);
                Add(21, PyType.IsADirectoryErrorType);
                Add(3, PyType.ProcessLookupErrorType);
                Add(4, PyType.InterruptedErrorType);
                Add(10, PyType.ChildProcessErrorType);
                Add(32, PyType.BrokenPipeErrorType);
                bool windows = OperatingSystem.IsWindows();
                bool bsd = OperatingSystem.IsMacOS() || OperatingSystem.IsIOS() ||
                    OperatingSystem.IsTvOS() || OperatingSystem.IsMacCatalyst() || OperatingSystem.IsFreeBSD();
                Add(bsd ? 35 : 11, PyType.BlockingIOErrorType);
                Add(windows ? 10035 : bsd ? 35 : 11, PyType.BlockingIOErrorType);
                Add(windows ? 10036 : bsd ? 36 : 115, PyType.BlockingIOErrorType);
                Add(windows ? 10037 : bsd ? 37 : 114, PyType.BlockingIOErrorType);
                Add(windows ? 10058 : bsd ? 58 : 108, PyType.BrokenPipeErrorType);
                Add(windows ? 10053 : bsd ? 53 : 103, PyType.ConnectionAbortedErrorType);
                Add(windows ? 10061 : bsd ? 61 : 111, PyType.ConnectionRefusedErrorType);
                Add(windows ? 10054 : bsd ? 54 : 104, PyType.ConnectionResetErrorType);
                Add(windows ? 10060 : bsd ? 60 : 110, PyType.TimeoutErrorType);
                return map;
            }
        }

        private static PyOSError FromIntegerSubclass(PyIntSubclass number, PyObject[] args)
        {
            long hash = number.GetPythonHash();
            if (!SubclassErrnoMap.Entries.TryGetValue(hash, out var entry))
                return new PyOSError(args, null, true);

            // Every errnomap key is a distinct small positive int, so its hash
            // equals its value. At most one equality call can be observable.
            var method = number.LookupSpecialMethodUncached("__eq__");
            // slot_tp_richcompare clears lookup/binding errors and returns
            // NotImplemented, letting the builtin int comparison take over.
            try { if (method != null) method = number.BindSpecialMethod(method); }
            catch (PythonException) { method = null; }
            var result = method == null ? PyNotImplemented.Instance
                : method.Call(new PyObject[] { entry.Key }, null);
            bool equal = result == PyNotImplemented.Instance
                ? number.GetIntValue().Value == hash : result.IsTrue();
            return equal ? (PyOSError)entry.Type.Call(args, null) : new PyOSError(args, null, true);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal static PyOSError FromArguments(PyObject[] args, PyDict kwargs)
        {
            if (kwargs != null && kwargs.Length() != 0)
                throw PyTypeError.Create("OSError() takes no keyword arguments");
            if ((uint)(args.Length - 2) > 3)
                return new PyOSError(args, null, true);
            return FromStructuredArguments(args);
        }

        // One-argument construction does not inspect errno. Keep its caller
        // free of the BigInteger spills needed by the subtype selection below.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static PyOSError FromStructuredArguments(PyObject[] args)
        {
            if (args.Length >= 4)
                args = NormalizeWindowsArguments(args);
            {
                if (args[0] is PyInt number)
                {
                    if (number.Value == 2) return new PyFileNotFoundError(args, null, true);
                    if (number.Value == 1 || number.Value == 13) return new PyPermissionError(args, null, true);
                    if (number.Value == 17) return new PyFileExistsError(args, null, true);
                    if (number.Value == 20) return new PyNotADirectoryError(args, null, true);
                    if (number.Value == 21) return new PyIsADirectoryError(args, null, true);
                    if (IsBlockingErrno(number.Value)) return new PyBlockingIOError(args, null, true);
                    return FromRemainingErrno(number.Value, args);
                }
                // bool is a Python int subclass, but has a separate CLR representation.
                else if (args[0] is PyBool { Value: true })
                    return new PyPermissionError(args, null, true);
                else if (args[0] is PyIntSubclass subclass)
                    return FromIntegerSubclass(subclass, args);
            }
            return new PyOSError(args, null, true);
        }

        internal PyOSError(PyObject[] args, PyDict kwargs, bool normalized = false) : base(args, kwargs)
        {
            if (!normalized && args.Length >= 4 && args.Length <= 5)
            {
                args = NormalizeWindowsArguments(args);
                Args = args;
            }
            if (args.Length < 2 || args.Length > 5) return;
            _errno = args[0];
            _strerror = args[1];
            if (OperatingSystem.IsWindows() && args.Length >= 4) _winerror = args[3];
            if (args.Length >= 3 && args[2] != PyNone.Instance)
            {
                if (GetType() == typeof(PyBlockingIOError) && IsNumber(args[2]))
                {
                    SetWritten(AsWrittenCount(args[2]));
                    return;
                }
                _filename = args[2];
                if (args.Length == 5 && args[4] != PyNone.Instance) Filename2 = args[4];
                Args = new[] { args[0], args[1] };
            }
        }

        public PyOSError(string message = "") : base(message) { }
        public override PyObject GetAttribute(string name) => name switch
        {
            "errno" => _errno ?? PyNone.Instance,
            "strerror" => _strerror ?? PyNone.Instance,
            "filename" => _filename ?? PyNone.Instance,
            "filename2" => Filename2 ?? PyNone.Instance,
            "characters_written" => _filename2 is WrittenState state ? SmallIntCache.GetOrCreate(state.Value) : throw PyAttributeError.Create("characters_written"),
            "winerror" when OperatingSystem.IsWindows() => _winerror ?? PyNone.Instance,
            _ => base.GetAttribute(name)
        };

        public override void SetAttribute(string name, PyObject value)
        {
            switch (name)
            {
                case "errno": _errno = value; break;
                case "strerror": _strerror = value; break;
                case "filename": _filename = value; break;
                case "filename2": Filename2 = value; break;
                case "characters_written": SetWritten(AsWrittenCount(value)); break;
                case "winerror" when OperatingSystem.IsWindows(): _winerror = value; break;
                default: base.SetAttribute(name, value); break;
            }
        }

        public override void DelAttribute(string name)
        {
            switch (name)
            {
                case "errno": _errno = null; break;
                case "strerror": _strerror = null; break;
                case "filename": _filename = null; break;
                case "filename2": Filename2 = null; break;
                case "characters_written":
                    if (_filename2 is not WrittenState) throw PyAttributeError.Create("characters_written");
                    SetWritten(-1);
                    break;
                case "winerror" when OperatingSystem.IsWindows(): _winerror = null; break;
                default: base.DelAttribute(name); break;
            }
        }

        public override PyString ToStr()
        {
            bool windowsError = OperatingSystem.IsWindows() && _winerror != null;
            var code = windowsError ? _winerror : _errno;
            if (_filename != null || (code != null && _strerror != null))
            {
                var text = $"[{(windowsError ? "WinError" : "Errno")} {(code ?? PyNone.Instance).ToStr().Value}] {(_strerror ?? PyNone.Instance).ToStr().Value}";
                if (_filename != null)
                {
                    text += ": " + _filename.ToRepr().Value;
                    if (Filename2 != null) text += " -> " + Filename2.ToRepr().Value;
                }
                return new PyString(text);
            }
            return base.ToStr();
        }


        public override string GetTypeName() => "OSError";
        public override PyType GetPyType() => PyType.OSErrorType;

        public new static PythonException Create(string message = "")
        {
            var pyException = new PyOSError(message);
            return new PythonException(pyException);
        }
    }

    public class PyConnectionError : PyOSError
    {
        internal PyConnectionError(PyObject[] args, PyDict kwargs, bool normalized = false) : base(args, kwargs, normalized) { }
        public override string GetTypeName() => "ConnectionError";
        public override PyType GetPyType() => PyType.ConnectionErrorType;
    }

    public class PyBrokenPipeError : PyConnectionError
    {
        internal PyBrokenPipeError(PyObject[] args, PyDict kwargs, bool normalized = false) : base(args, kwargs, normalized) { }
        public override string GetTypeName() => "BrokenPipeError";
        public override PyType GetPyType() => PyType.BrokenPipeErrorType;
    }

    public class PyConnectionAbortedError : PyConnectionError
    {
        internal PyConnectionAbortedError(PyObject[] args, PyDict kwargs, bool normalized = false) : base(args, kwargs, normalized) { }
        public override string GetTypeName() => "ConnectionAbortedError";
        public override PyType GetPyType() => PyType.ConnectionAbortedErrorType;
    }

    public class PyConnectionRefusedError : PyConnectionError
    {
        internal PyConnectionRefusedError(PyObject[] args, PyDict kwargs, bool normalized = false) : base(args, kwargs, normalized) { }
        public override string GetTypeName() => "ConnectionRefusedError";
        public override PyType GetPyType() => PyType.ConnectionRefusedErrorType;
    }

    public class PyConnectionResetError : PyConnectionError
    {
        internal PyConnectionResetError(PyObject[] args, PyDict kwargs, bool normalized = false) : base(args, kwargs, normalized) { }
        public override string GetTypeName() => "ConnectionResetError";
        public override PyType GetPyType() => PyType.ConnectionResetErrorType;
    }

    public class PyChildProcessError : PyOSError
    {
        internal PyChildProcessError(PyObject[] args, PyDict kwargs, bool normalized = false) : base(args, kwargs, normalized) { }
        public override string GetTypeName() => "ChildProcessError";
        public override PyType GetPyType() => PyType.ChildProcessErrorType;
    }

    public class PyInterruptedError : PyOSError
    {
        internal PyInterruptedError(PyObject[] args, PyDict kwargs, bool normalized = false) : base(args, kwargs, normalized) { }
        public override string GetTypeName() => "InterruptedError";
        public override PyType GetPyType() => PyType.InterruptedErrorType;
    }

    public class PyProcessLookupError : PyOSError
    {
        internal PyProcessLookupError(PyObject[] args, PyDict kwargs, bool normalized = false) : base(args, kwargs, normalized) { }
        public override string GetTypeName() => "ProcessLookupError";
        public override PyType GetPyType() => PyType.ProcessLookupErrorType;
    }

    public class PyTimeoutError : PyOSError
    {
        internal PyTimeoutError(PyObject[] args, PyDict kwargs, bool normalized = false) : base(args, kwargs, normalized) { }
        public override string GetTypeName() => "TimeoutError";
        public override PyType GetPyType() => PyType.TimeoutErrorType;
    }

    public class PyBlockingIOError : PyOSError
    {
        internal PyBlockingIOError(PyObject[] args, PyDict kwargs, bool normalized = false) : base(args, kwargs, normalized) { }
        public override string GetTypeName() => "BlockingIOError";
        public override PyType GetPyType() => PyType.BlockingIOErrorType;
    }

    public class PyNotADirectoryError : PyOSError
    {
        internal PyNotADirectoryError(PyObject[] args, PyDict kwargs, bool normalized = false) : base(args, kwargs, normalized) { }
        public override string GetTypeName() => "NotADirectoryError";
        public override PyType GetPyType() => PyType.NotADirectoryErrorType;
    }

    public class PyIsADirectoryError : PyOSError
    {
        internal PyIsADirectoryError(PyObject[] args, PyDict kwargs, bool normalized = false) : base(args, kwargs, normalized) { }
        public override string GetTypeName() => "IsADirectoryError";
        public override PyType GetPyType() => PyType.IsADirectoryErrorType;
    }

    public class PyPermissionError : PyOSError
    {
        internal PyPermissionError(PyObject[] args, PyDict kwargs, bool normalized = false) : base(args, kwargs, normalized) { }
        public override string GetTypeName() => "PermissionError";
        public override PyType GetPyType() => PyType.PermissionErrorType;
    }

    public class PyFileExistsError : PyOSError
    {
        internal PyFileExistsError(PyObject[] args, PyDict kwargs, bool normalized = false) : base(args, kwargs, normalized) { }
        public override string GetTypeName() => "FileExistsError";
        public override PyType GetPyType() => PyType.FileExistsErrorType;
    }

    /// <summary>
    /// Python FileNotFoundError - 파일이나 디렉토리를 찾을 수 없음
    /// </summary>
    public class PyFileNotFoundError : PyOSError
    {
        internal PyFileNotFoundError(PyObject[] args, PyDict kwargs, bool normalized = false) : base(args, kwargs, normalized) { }

        public PyFileNotFoundError(string message = "") : base(message) { }

        public override string GetTypeName() => "FileNotFoundError";
        public override PyType GetPyType() => PyType.FileNotFoundErrorType;

        public new static PythonException Create(string message = "")
        {
            var pyException = new PyFileNotFoundError(message);
            return new PythonException(pyException);
        }
    }

    /// <summary>
    /// Python EOFError - 입력에서 예상치 못한 EOF 발생
    /// CPython 3.12: Raised when input() hits EOF (Ctrl+D/Ctrl+Z)
    /// </summary>
    public class PyEOFError : PyException
    {
        public PyEOFError(string message = "EOF when reading a line") : base(message) { }

        public override string GetTypeName() => "EOFError";
        public override PyType GetPyType() => PyType.EOFErrorType;

        public new static PythonException Create(string message = "EOF when reading a line")
        {
            var pyException = new PyEOFError(message);
            return new PythonException(pyException);
        }
    }

    /// <summary>
    /// Python MemoryError - 메모리 부족 에러
    /// CPython 3.12: Raised when an operation runs out of memory
    /// </summary>
    public class PyMemoryError : PyException
    {
        internal PyMemoryError(PyObject[] args, PyDict kwargs) : base(args, kwargs) { }

        public PyMemoryError(string message = "") : base(message) { }

        public override string GetTypeName() => "MemoryError";
        public override PyType GetPyType() => PyType.MemoryErrorType;

        public new static PythonException Create(string message = "")
        {
            var pyException = new PyMemoryError(message);
            return new PythonException(pyException);
        }
    }

    /// <summary>
    /// Python UnicodeError - 유니코드 관련 에러의 기본 클래스
    /// CPython 3.12: Base class for Unicode-related errors
    /// </summary>
    public class PyUnicodeError : PyValueError
    {
        internal PyUnicodeError(PyObject[] args, PyDict kwargs) : base(args, kwargs) { }

        public PyUnicodeError(string message = "") : base(message) { }

        public override string GetTypeName() => "UnicodeError";
        public override PyType GetPyType() => PyType.UnicodeErrorType;

        public new static PythonException Create(string message = "")
        {
            var pyException = new PyUnicodeError(message);
            return new PythonException(pyException);
        }
    }

    /// <summary>
    /// Python UnicodeDecodeError - 유니코드 디코딩 에러
    /// CPython 3.12: Raised when a Unicode-related error occurs during decoding
    /// </summary>
    public class PyUnicodeDecodeError : PyUnicodeError
    {
        internal PyUnicodeDecodeError(PyObject[] args, PyDict kwargs) : base(args, kwargs) { }

        public PyUnicodeDecodeError(string message = "") : base(message) { }

        public override string GetTypeName() => "UnicodeDecodeError";
        public override PyType GetPyType() => PyType.UnicodeDecodeErrorType;

        public new static PythonException Create(string message = "")
        {
            var pyException = new PyUnicodeDecodeError(message);
            return new PythonException(pyException);
        }
    }

    /// <summary>
    /// Python UnicodeEncodeError - 유니코드 인코딩 에러
    /// CPython 3.12: Raised when a Unicode-related error occurs during encoding
    /// </summary>
    public class PyUnicodeEncodeError : PyUnicodeError
    {
        internal PyUnicodeEncodeError(PyObject[] args, PyDict kwargs) : base(args, kwargs) { }

        public PyUnicodeEncodeError(string message = "") : base(message) { }

        public override string GetTypeName() => "UnicodeEncodeError";
        public override PyType GetPyType() => PyType.UnicodeEncodeErrorType;

        public new static PythonException Create(string message = "")
        {
            var pyException = new PyUnicodeEncodeError(message);
            return new PythonException(pyException);
        }
    }

    public class PyTabError : PyIndentationError
    {
        public PyTabError(string message = "") : base(message) { }
        public override PyType GetPyType() => PyType.TabErrorType;
        public override string GetTypeName() => "TabError";
    }

    #region Warning Hierarchy (CPython 3.12)

    /// <summary>
    /// Base class for warning categories
    /// </summary>
    public class PyWarning : PyException
    {
        public PyWarning(PyObject[] args, PyDict kwargs = null) : base(args, kwargs) { }
        public PyWarning(string message = "") : base(message) { }
        public override string GetTypeName() => "Warning";
        public override PyType GetPyType() => PyType.WarningType;

        public new static PythonException Create(string message = "")
        {
            return new PythonException(new PyWarning(message));
        }
    }

    public class PyUserWarning : PyWarning
    {
        public PyUserWarning(PyObject[] args, PyDict kwargs = null) : base(args, kwargs) { }
        public PyUserWarning(string message = "") : base(message) { }
        public override string GetTypeName() => "UserWarning";
        public override PyType GetPyType() => PyType.UserWarningType;

        public new static PythonException Create(string message = "")
        {
            return new PythonException(new PyUserWarning(message));
        }
    }

    public class PyDeprecationWarning : PyWarning
    {
        public PyDeprecationWarning(PyObject[] args, PyDict kwargs = null) : base(args, kwargs) { }
        public PyDeprecationWarning(string message = "") : base(message) { }
        public override string GetTypeName() => "DeprecationWarning";
        public override PyType GetPyType() => PyType.DeprecationWarningType;

        public new static PythonException Create(string message = "")
        {
            return new PythonException(new PyDeprecationWarning(message));
        }
    }

    public class PyPendingDeprecationWarning : PyWarning
    {
        public PyPendingDeprecationWarning(PyObject[] args, PyDict kwargs = null) : base(args, kwargs) { }
        public PyPendingDeprecationWarning(string message = "") : base(message) { }
        public override string GetTypeName() => "PendingDeprecationWarning";
        public override PyType GetPyType() => PyType.PendingDeprecationWarningType;

        public new static PythonException Create(string message = "")
        {
            return new PythonException(new PyPendingDeprecationWarning(message));
        }
    }

    public class PySyntaxWarning : PyWarning
    {
        public PySyntaxWarning(PyObject[] args, PyDict kwargs = null) : base(args, kwargs) { }
        public PySyntaxWarning(string message = "") : base(message) { }
        public override string GetTypeName() => "SyntaxWarning";
        public override PyType GetPyType() => PyType.SyntaxWarningType;

        public new static PythonException Create(string message = "")
        {
            return new PythonException(new PySyntaxWarning(message));
        }
    }

    public class PyRuntimeWarning : PyWarning
    {
        public PyRuntimeWarning(PyObject[] args, PyDict kwargs = null) : base(args, kwargs) { }
        public PyRuntimeWarning(string message = "") : base(message) { }
        public override string GetTypeName() => "RuntimeWarning";
        public override PyType GetPyType() => PyType.RuntimeWarningType;

        public new static PythonException Create(string message = "")
        {
            return new PythonException(new PyRuntimeWarning(message));
        }
    }

    public class PyFutureWarning : PyWarning
    {
        public PyFutureWarning(PyObject[] args, PyDict kwargs = null) : base(args, kwargs) { }
        public PyFutureWarning(string message = "") : base(message) { }
        public override string GetTypeName() => "FutureWarning";
        public override PyType GetPyType() => PyType.FutureWarningType;

        public new static PythonException Create(string message = "")
        {
            return new PythonException(new PyFutureWarning(message));
        }
    }

    public class PyImportWarning : PyWarning
    {
        public PyImportWarning(PyObject[] args, PyDict kwargs = null) : base(args, kwargs) { }
        public PyImportWarning(string message = "") : base(message) { }
        public override string GetTypeName() => "ImportWarning";
        public override PyType GetPyType() => PyType.ImportWarningType;

        public new static PythonException Create(string message = "")
        {
            return new PythonException(new PyImportWarning(message));
        }
    }

    public class PyUnicodeWarning : PyWarning
    {
        public PyUnicodeWarning(PyObject[] args, PyDict kwargs = null) : base(args, kwargs) { }
        public PyUnicodeWarning(string message = "") : base(message) { }
        public override string GetTypeName() => "UnicodeWarning";
        public override PyType GetPyType() => PyType.UnicodeWarningType;

        public new static PythonException Create(string message = "")
        {
            return new PythonException(new PyUnicodeWarning(message));
        }
    }

    public class PyBytesWarning : PyWarning
    {
        public PyBytesWarning(PyObject[] args, PyDict kwargs = null) : base(args, kwargs) { }
        public PyBytesWarning(string message = "") : base(message) { }
        public override string GetTypeName() => "BytesWarning";
        public override PyType GetPyType() => PyType.BytesWarningType;

        public new static PythonException Create(string message = "")
        {
            return new PythonException(new PyBytesWarning(message));
        }
    }

    public class PyResourceWarning : PyWarning
    {
        public PyResourceWarning(PyObject[] args, PyDict kwargs = null) : base(args, kwargs) { }
        public PyResourceWarning(string message = "") : base(message) { }
        public override string GetTypeName() => "ResourceWarning";
        public override PyType GetPyType() => PyType.ResourceWarningType;

        public new static PythonException Create(string message = "")
        {
            return new PythonException(new PyResourceWarning(message));
        }
    }

    #endregion

    #endregion

    #region Exception Utilities

    /// <summary>
    /// 예외 시스템 유틸리티
    /// </summary>
    public static class ExceptionSystem
    {
        /// <summary>
        /// C# catch에서 Python 예외 타입 확인
        /// </summary>
        public static bool IsInstance(System.Exception csharpException, PyType pythonExceptionType)
        {
            if (csharpException is PythonException pythonException)
            {
                return pythonException.PyException.GetPyType().IsSubclassOf(pythonExceptionType);
            }
            return false;
        }

        /// <summary>
        /// C# Exception을 Python 예외로 변환 (interop용)
        /// </summary>
        public static PyBaseException FromCSharpException(System.Exception exception)
        {
            if (exception is PythonException pythonException)
                return pythonException.PyException;

            return exception switch
            {
                ArgumentNullException _ => new PyTypeError("argument cannot be None"),
                ArgumentOutOfRangeException _ => new PyIndexError("index out of range"),
                InvalidOperationException _ => new PyRuntimeError(exception.Message),
                _ => new PyException(exception.Message)
            };
        }

        /// <summary>
        /// 예외 계층 구조 출력
        /// </summary>
        public static void PrintHierarchy()
        {
            Console.WriteLine("=== Python Exception Hierarchy ===");
            Console.WriteLine("BaseException");
            Console.WriteLine("  SystemExit");
            Console.WriteLine("  KeyboardInterrupt");
            Console.WriteLine("  Exception");
            Console.WriteLine("    ArithmeticError");
            Console.WriteLine("      ZeroDivisionError");
            Console.WriteLine("      OverflowError");
            Console.WriteLine("    AttributeError");
            Console.WriteLine("    ImportError");
            Console.WriteLine("      ModuleNotFoundError");
            Console.WriteLine("    LookupError");
            Console.WriteLine("      IndexError");
            Console.WriteLine("      KeyError");
            Console.WriteLine("    NameError");
            Console.WriteLine("      UnboundLocalError");
            Console.WriteLine("    RuntimeError");
            Console.WriteLine("      NotImplementedError");
            Console.WriteLine("      RecursionError");
            Console.WriteLine("    SyntaxError");
            Console.WriteLine("      IndentationError");
            Console.WriteLine("    TypeError");
            Console.WriteLine("    ValueError");
            Console.WriteLine("    StopIteration");
        }
    }

    #endregion

    #region CPython 3.12 Exception Handling Support

    /// <summary>
    /// CPython 3.12: Traceback object - stores execution frame info for error reporting
    /// Corresponds to PyTracebackObject in CPython (_traceback struct in traceback.h)
    /// </summary>
    public class PyTraceback : PyObject
    {
        public PyTraceback? Next { get; set; }     // tb_next: linked list of traceback frames
        public PyFrame Frame { get; set; }         // tb_frame: execution frame snapshot
        public int LastI { get; set; }             // tb_lasti: last instruction index
        public int LineNo { get; set; }            // tb_lineno: line number
        public int ColNo { get; set; }             // Column offset (CPython 3.12)
        public int EndColNo { get; set; }          // End column offset (CPython 3.12)

        public PyTraceback(PyFrame frame, int lasti, int lineno, PyTraceback? next = null, int colno = -1, int endcolno = -1)
        {
            Frame = frame;
            LastI = lasti;
            LineNo = lineno;
            ColNo = colno;
            EndColNo = endcolno;
            Next = next;
        }

        public override PyType GetPyType() => PyType.ObjectType;
        public override string GetTypeName() => "traceback";

        public override PyString ToStr()
        {
            return new PyString($"<traceback object at {GetHashCode():X}>");
        }

        public override PyString ToRepr() => ToStr();

        public override PyObject GetAttribute(string name)
        {
            return name switch
            {
                "tb_frame" => Frame,
                "tb_next" => (PyObject?)Next ?? PyNone.Instance,
                "tb_lasti" => new PyInt(LastI),
                "tb_lineno" => new PyInt(LineNo),
                _ => base.GetAttribute(name)
            };
        }
    }

    /// <summary>
    /// CPython 3.12 compatible exception info composite object
    /// Used by PUSH_EXC_INFO and POP_EXCEPT for stack management
    /// Stack effect: PUSH_EXC_INFO (+1), POP_EXCEPT (-1)
    /// </summary>
    public class PyExceptionInfo : PyObject
    {
        public PyObject ExcType { get; set; }
        public PyObject ExcValue { get; set; }
        public PyObject ExcTraceback { get; set; }
        public PyObject Lasti { get; set; }

        public PyExceptionInfo(PyObject excType, PyObject excValue, PyObject excTraceback, PyObject lasti)
        {
            ExcType = excType;
            ExcValue = excValue;
            ExcTraceback = excTraceback;
            Lasti = lasti;
        }

        public override PyType GetPyType() => PyType.ObjectType;
        public override string GetTypeName() => "ExceptionInfo";

        public override PyString ToStr()
        {
            return new PyString($"ExceptionInfo(type={ExcType}, value={ExcValue}, traceback={ExcTraceback}, lasti={Lasti})");
        }

        public override PyString ToRepr() => ToStr();
    }

    #endregion
}
