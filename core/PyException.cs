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
        public PyObject[] Args { get; protected set; }

        public PyBaseException(string message = "", params PyObject[] args)
        {
            Message = message ?? "";
            Args = args.Length > 0 ? args : new PyObject[] { new PyString(Message) };
        }

        public override PyType GetPyType() => PyType.BaseExceptionType;
        public override string GetTypeName() => "BaseException";

        public override string ToStr()
        {
            if (Args.Length == 0)
                return "";
            if (Args.Length == 1 && Args[0] is PyString str)
                return str.Value;
            return $"({string.Join(", ", Args.Select(a => a.ToRepr()))})";
        }

        public override string ToRepr()
        {
            if (Args.Length == 1)
                return $"{GetTypeName()}({Args[0].ToRepr()})";
            return $"{GetTypeName()}({string.Join(", ", Args.Select(a => a.ToRepr()))})";
        }

        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "args":
                    return new PyTuple(Args);
                default:
                    return base.GetAttribute(name);
            }
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
        public PyException(string message = "", params PyObject[] args) : base(message, args) { }
        public override PyType GetPyType() => PyType.ExceptionType;
        public override string GetTypeName() => "Exception";

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
        public PyAttributeError(string message) : base(message) { }
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
        public PyNameError(string message) : base(message) { }
        public override PyType GetPyType() => PyType.NameErrorType;
        public override string GetTypeName() => "NameError";

        public new static System.Exception Create(string message)
        {
            var pyException = new PyNameError(message);
            return new PythonException(pyException);
        }
    }

    public class PyUnboundLocalError : PyNameError
    {
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
        public PyArithmeticError(string message = "") : base(message) { }
        public override PyType GetPyType() => PyType.ArithmeticErrorType;
        public override string GetTypeName() => "ArithmeticError";

        public new static System.Exception Create(string message = "")
        {
            var pyException = new PyArithmeticError(message);
            return new PythonException(pyException);
        }
    }

    public class PyZeroDivisionError : PyArithmeticError
    {
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
        public PyKeyError(PyObject key) : base($"'{key}'") { }
        public PyKeyError(string message) : base(message) { }
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
        public PyRuntimeError(string message = "") : base(message) { }
        public override PyType GetPyType() => PyType.RuntimeErrorType;
        public override string GetTypeName() => "RuntimeError";

        public new static System.Exception Create(string message = "")
        {
            var pyException = new PyRuntimeError(message);
            return new PythonException(pyException);
        }
    }

    public class PyNotImplementedError : PyRuntimeError
    {
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

        public override string ToStr()
        {
            return LineNumber > 0 ? $"{Message} ({FileName}, line {LineNumber})" : Message;
        }

        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
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
        public PyObject Value { get; }

        public PyStopIteration(PyObject value = null) : base("StopIteration")
        {
            Value = value ?? PyNone.Instance;
        }

        public override PyType GetPyType() => PyType.StopIterationType;
        public override string GetTypeName() => "StopIteration";

        public override PyObject GetAttribute(string name)
        {
            if (name == "value")
                return Value;
            return base.GetAttribute(name);
        }

        public new static System.Exception Create(PyObject value = null)
        {
            var pyException = new PyStopIteration(value);
            return new PythonException(pyException);
        }
    }

    /// <summary>
    /// CPython 3.12: GeneratorExit exception - used for generator cleanup
    /// </summary>
    public class PyGeneratorExit : PyBaseException
    {
        public PyGeneratorExit(string message = "generator exit") : base(message)
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

        public PythonException(PyBaseException pyException) 
            : base(pyException.ToStr())
        {
            PyException = pyException;
        }
        
        public PythonException(PyBaseException pyException, string? fileName, int lineNumber, int columnOffset = -1)
            : base(pyException.ToStr())
        {
            PyException = pyException;
            FileName = fileName;
            LineNumber = lineNumber;
            ColumnOffset = columnOffset;
        }

        public override string ToString()
        {
            var baseStr = PyException.ToRepr();
            
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
                result.Append($"{PyException.GetTypeName()}: {PyException.ToStr()}");
                
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
        public PyAssertionError(string message = "") : base(message) { }
        
        public override string GetTypeName() => "AssertionError";
        
        public new static PythonException Create(string message = "")
        {
            var pyException = new PyAssertionError(message);
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
        public PyOSError(string message = "") : base(message) { }

        public override string GetTypeName() => "OSError";
        public override PyType GetPyType() => PyType.OSErrorType;

        public new static PythonException Create(string message = "")
        {
            var pyException = new PyOSError(message);
            return new PythonException(pyException);
        }
    }

    /// <summary>
    /// Python FileNotFoundError - 파일이나 디렉토리를 찾을 수 없음
    /// </summary>
    public class PyFileNotFoundError : PyOSError
    {
        public PyFileNotFoundError(string message = "") : base(message) { }

        public override string GetTypeName() => "FileNotFoundError";
        public override PyType GetPyType() => PyType.FileNotFoundErrorType;

        public new static PythonException Create(string message = "")
        {
            var pyException = new PyFileNotFoundError(message);
            return new PythonException(pyException);
        }
    }

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

        public override string ToStr()
        {
            return $"ExceptionInfo(type={ExcType}, value={ExcValue}, traceback={ExcTraceback}, lasti={Lasti})";
        }

        public override string ToRepr() => ToStr();
    }

    #endregion
}