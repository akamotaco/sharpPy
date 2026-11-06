namespace SharpPy
{
    #region Python Exception Hierarchy (Pure PyObject)

    /// <summary>
    /// Python BaseException - 모든 예외의 최상위 클래스
    /// 순수한 PyObject로 유지
    /// </summary>
    public class PyBaseException : PyObject, IObjectDict
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

        // CPython BaseException.__init__/args setter path needs to rebind args
        // from descriptors outside the class family.
        internal void SetArgsInternal(PyObject[] args) => Args = args;

        /// <summary>Wraps the stored array into a PyTuple once and caches it back in _args.</summary>
        internal PyTuple ArgsTuple
        {
            get
            {
                if (_args is not PyTuple t)
                {
                    var items = (PyObject[])_args;
                    // The empty tuple is the one tuple: StopIteration().args is StopIteration().args.
                    t = items.Length == 0 ? PyTuple.Empty : new PyTuple(items);
                    _args = t;
                }
                return t;
            }
        }

        internal void SetArgsTuple(PyTuple tuple) => _args = tuple;

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

        // BaseException_str (Objects/exceptions.c:79) and BaseException_repr (:131) both read the
        // args slot, so they read it through PyExceptionSlots.ArgsOf like the member descriptor
        // does. Before this, repr used the CLR field while a Python subclass instance kept its value
        // elsewhere - which is how a raised object could answer .args with ('z',) and repr with K().
        protected internal override PyString Str() => BaseExceptionStr(this);

        /// <summary>BaseException_init: no keywords (named by the object's type), then args.</summary>
        internal static void BaseExceptionInit(PyObject self, PyObject[] args, PyDict kwargs)
        {
            if (kwargs != null && kwargs.Length() != 0)
                throw PyTypeError.Create($"{self.GetPyType().FullName}() takes no keyword arguments");
            PyExceptionSlots.StoreArgs(self, args);
        }

        /// <summary>BaseException_str itself, which BaseException.__str__ calls whatever the subtype.</summary>
        internal static PyString BaseExceptionStr(PyObject self)
        {
            var args = PyExceptionSlots.ArgsOf(self);
            if (args.Length == 0)
                return PyString.Empty;
            if (args.Length == 1)
                return args[0].ToStr();
            return new PyString($"({string.Join(", ", args.Select(a => a.ToRepr().Value))})");
        }

        protected internal override PyString Repr()
        {
            var args = PyExceptionSlots.ArgsOf(this);
            return new PyString($"{GetTypeName()}{PyType.ArgsReprForException(args)}");
        }

        public override string ToString()
        {
            // CPython behavior: str(exception) returns the message
            return AsString().Value;
        }

        // The dict every exception object has (PyException_HEAD): `e.note = 1`, __notes__.
        private PyDict _attributes;
        internal PyDict Attributes => _attributes ??= new PyDict();

        // PyObject_GenericSetDict
        internal void SetDict(PyObject value) =>
            _attributes = value as PyDict ?? throw PyTypeError.Create(
                $"__dict__ must be set to a dictionary, not a '{value.GetTypeName()}'");

        /// <summary>
        /// The struct slots a builtin exception type adds (Objects/exceptions.c, the *_members
        /// tables). Only the member descriptors in the type's dict read and write them, so the
        /// attribute and the descriptor give one answer. A null slot is NULL and reads as None.
        /// </summary>
        internal virtual bool TryGetMember(string name, out PyObject value)
        {
            value = null;
            return false;
        }

        /// <summary>member_set: value null is the NULL a delete stores.</summary>
        internal virtual bool TrySetMember(string name, PyObject value) => false;

        PyDict IObjectDict.ObjectDict(bool create) => create ? Attributes : _attributes;

        // BaseException_get_tb / BaseException_set_tb
        internal PyObject TracebackGet() => (PyObject?)__traceback__ ?? PyNone.Instance;
        internal void TracebackSet(PyObject value)
        {
            if (value == null)
                throw PyTypeError.Create("__traceback__ may not be deleted");
            if (value is PyTraceback tb) __traceback__ = tb;
            else if (value is PyNone) __traceback__ = null;
            else throw PyTypeError.Create("__traceback__ must be a traceback or None");
        }

        // BaseException_get_context / BaseException_set_context
        internal PyObject ContextGet() => PyExceptionSlots.Visible(__context__) ?? PyNone.Instance;
        internal void ContextSet(PyObject value)
        {
            if (value == null)
                throw PyTypeError.Create("__context__ may not be deleted");
            if (value is PyNone) __context__ = null;
            else if (PyExceptionSlots.ToTransport(value) is PyBaseException exc) __context__ = exc;
            else throw PyTypeError.Create("exception context must be None or derive from BaseException");
        }

        // BaseException_get_cause / BaseException_set_cause; PyException_SetCause sets suppress_context.
        internal PyObject CauseGet() => PyExceptionSlots.Visible(__cause__) ?? PyNone.Instance;
        internal void CauseSet(PyObject value)
        {
            if (value == null)
                throw PyTypeError.Create("__cause__ may not be deleted");
            if (value is PyNone) __cause__ = null;
            else if (PyExceptionSlots.ToTransport(value) is PyBaseException exc) __cause__ = exc;
            else throw PyTypeError.Create("exception cause must be None or derive from BaseException");
            __suppress_context__ = true;
        }

        // {"__suppress_context__", T_BOOL}: PyMember_SetOne takes a bool only, and deletes nothing.
        internal PyObject SuppressContextGet() => __suppress_context__ ? PyBool.True : PyBool.False;
        internal void SuppressContextSet(PyObject value)
        {
            if (value == null)
                throw PyTypeError.Create("can't delete numeric/char attribute");
            if (value is not PyBool flag)
                throw PyTypeError.Create("attribute value type must be bool");
            __suppress_context__ = flag.Value;
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
            if (originalInstance != null)
            {
                Args = PyExceptionSlots.ArgsOf(originalInstance);
            }
        }

        public override PyType GetPyType() => OriginalClass ?? PyType.ExceptionType;
        public override string GetTypeName() => OriginalClass?.Name ?? "Exception";

        // CPython 3.12: Objects/exceptions.c:79-105 (BaseException_str)
        // For user-defined exceptions, delegate ToStr() to OriginalInstance
        // This ensures BaseException.__str__ descriptor is properly invoked
        protected internal override PyString Str()
        {
            // CPython 3.12: User-defined exception instances use __str__ method from class hierarchy
            if (OriginalInstance != null)
            {
                return OriginalInstance.ToStr();
            }

            // CPython 3.12: C-level exceptions use Args-based string representation
            return (PyString)base.Str();
        }

        // CPython 3.12: Objects/exceptions.c:42-849
        // Exception attribute lookup follows standard descriptor protocol
        // User-defined exception instances delegate ALL attribute access to OriginalInstance
        // This ensures descriptors (like BaseException.args) are properly invoked
        protected internal override PyObject TpGetattro(string name)
        {
            // CPython 3.12: Exception chaining attributes are stored on the exception wrapper,
            // not on the user-defined instance. Check these first before delegating.
            // Corresponds to BaseException attributes in Objects/exceptions.c:785-795
            switch (name)
            {
                case "args":
                    // One slot, on the instance the wrapper carries. There is no second store on the
                    // wrapper that could disagree with it.
                    if (OriginalInstance != null)
                    {
                        return PyExceptionSlots.ArgsTuple(this);
                    }
                    return base.TpGetattro(name);
                case "__cause__":
                case "__context__":
                case "__suppress_context__":
                case "__traceback__":
                    // These are exception-specific attributes stored on the wrapper
                    return base.TpGetattro(name);
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
            return base.TpGetattro(name);
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

        // set_attribute_error_context
        internal static System.Exception CreateFor(PyObject target, string name, string message)
        {
            var error = new PyAttributeError(message);
            error.SetContextIfUnset(name, target);
            return new PythonException(error);
        }

        internal void SetContextIfUnset(string name, PyObject target)
        {
            if (_name != null || _obj != null) return;
            _name = new PyString(name);
            _obj = target;
        }

        internal PyAttributeError(PyObject[] args, PyDict kwargs) : base(args) => AttributeErrorInit(this, args, kwargs);

        // AttributeError_init: BaseException_init without the keywords, then "|$OO" name and obj.
        internal void AttributeErrorInit(PyObject self, PyObject[] args, PyDict kwargs)
        {
            BaseExceptionInit(self, args, null);
            PyObject name = null, obj = null;
            if (kwargs != null && kwargs.Length() != 0)
            {
                if (kwargs.Length() > 2)
                    throw PyTypeError.Create($"AttributeError() takes at most 2 keyword arguments ({kwargs.Length()} given)");
                foreach (var pair in kwargs.InternalDict)
                {
                    if (pair.Key is PyString { Value: "name" }) name = pair.Value;
                    else if (pair.Key is PyString { Value: "obj" }) obj = pair.Value;
                    else throw PyTypeError.Create($"'{pair.Key.ToStr().Value}' is an invalid keyword argument for AttributeError()");
                }
            }
            _name = name;
            _obj = obj;
        }

        public PyAttributeError(string message) : base(message) { }
        // AttributeError_members
        internal override bool TryGetMember(string name, out PyObject value)
        {
            switch (name)
            {
                case "name": value = _name; return true;
                case "obj": value = _obj; return true;
            }
            return base.TryGetMember(name, out value);
        }

        internal override bool TrySetMember(string name, PyObject value)
        {
            switch (name)
            {
                case "name": _name = value; return true;
                case "obj": _obj = value; return true;
            }
            return base.TrySetMember(name, value);
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
        internal PyNameError(PyObject[] args, PyDict kwargs) : base(args) => NameErrorInit(this, args, kwargs);

        // NameError_init: BaseException_init without the keywords, then "|$O" name.
        internal void NameErrorInit(PyObject self, PyObject[] args, PyDict kwargs)
        {
            BaseExceptionInit(self, args, null);
            PyObject name = null;
            if (kwargs != null && kwargs.Length() != 0)
            {
                if (kwargs.Length() > 1)
                    throw PyTypeError.Create($"NameError() takes at most 1 keyword argument ({kwargs.Length()} given)");
                foreach (var pair in kwargs.InternalDict)
                {
                    if (pair.Key is PyString { Value: "name" }) name = pair.Value;
                    else throw PyTypeError.Create($"'{pair.Key.ToStr().Value}' is an invalid keyword argument for NameError()");
                }
            }
            _name = name;
        }

        public PyNameError(string message) : base(message) { }

        // NameError_members
        internal override bool TryGetMember(string name, out PyObject value)
        {
            if (name == "name") { value = _name; return true; }
            return base.TryGetMember(name, out value);
        }

        internal override bool TrySetMember(string name, PyObject value)
        {
            if (name == "name") { _name = value; return true; }
            return base.TrySetMember(name, value);
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
        protected internal override PyString Str() => KeyErrorStr(this);

        // KeyError_str (Objects/exceptions.c:2603)
        // KeyError has no fields of its own: it reads the args of whatever object it is given.
        internal static PyString KeyErrorStr(PyObject self)
        {
            var args = PyExceptionSlots.ArgsOf(self);
            if (args.Length != 1) return BaseExceptionStr(self);
            return args[0].ToRepr();
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
        private PyObject _name, _path, _msg, _nameFrom;

        public string ModuleName => (_name as PyString)?.Value;

        public PyImportError(string message, string moduleName = null) : base(message)
        {
            _msg = new PyString(message);
            _name = moduleName == null ? null : new PyString(moduleName);
        }

        // ImportError_init (Objects/exceptions.c:1538): keywords name and path, msg from a single argument.
        protected internal override PyString Str() => ImportErrorStr(this);

        // ImportError_str: msg when it is exactly a str, otherwise BaseException_str of the object.
        internal PyString ImportErrorStr(PyObject self) =>
            _msg is PyString text && ReferenceEquals(text.GetPyType(), PyType.StrType) ? text : BaseExceptionStr(self);

        internal PyImportError(PyObject[] args, PyDict kwargs) : base(args) => ImportErrorInit(this, args, kwargs);

        // ImportError_init: BaseException_init without the keywords, "|$OOO:ImportError" name, path,
        // name_from, and msg the one argument there is.
        internal void ImportErrorInit(PyObject self, PyObject[] args, PyDict kwargs)
        {
            BaseExceptionInit(self, args, null);
            PyObject name = null, path = null, nameFrom = null;
            if (kwargs != null)
                foreach (var pair in kwargs.InternalDict)
                {
                    if (pair.Key is PyString { Value: "name" }) name = pair.Value;
                    else if (pair.Key is PyString { Value: "path" }) path = pair.Value;
                    else if (pair.Key is PyString { Value: "name_from" }) nameFrom = pair.Value;
                    else throw PyTypeError.Create(
                        $"'{pair.Key.ToStr().Value}' is an invalid keyword argument for ImportError()");
                }
            _name = name;
            _path = path;
            _nameFrom = nameFrom;
            _msg = args.Length == 1 ? args[0] : null;
        }

        public override PyType GetPyType() => PyType.ImportErrorType;
        public override string GetTypeName() => "ImportError";

        // ImportError_members
        internal override bool TryGetMember(string name, out PyObject value)
        {
            switch (name)
            {
                case "name": value = _name; return true;
                case "path": value = _path; return true;
                case "msg": value = _msg; return true;
                case "name_from": value = _nameFrom; return true;
            }
            return base.TryGetMember(name, out value);
        }

        internal override bool TrySetMember(string name, PyObject value)
        {
            switch (name)
            {
                case "name": _name = value; return true;
                case "path": _path = value; return true;
                case "msg": _msg = value; return true;
                case "name_from": _nameFrom = value; return true;
            }
            return base.TrySetMember(name, value);
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

        internal PyModuleNotFoundError(PyObject[] args, PyDict kwargs) : base(args, kwargs) { }
        
        public override PyType GetPyType() => PyType.ModuleNotFoundErrorType;
        public override string GetTypeName() => "ModuleNotFoundError";

        public new static System.Exception Create(string message, string moduleName = null)
        {
            var pyException = new PyModuleNotFoundError(message, moduleName);
            return new PythonException(pyException);
        }
    }

    // 시스템 예외들
    /// <summary>
    /// Python SystemExit. The argument is carried, never decoded here: CPython keeps whatever
    /// sys.exit() was handed (Objects/exceptions.c SystemExit_init) and turns it into a process
    /// status only where the exception reaches the top of the interpreter
    /// (Python/pythonrun.c:708 _Py_HandleSystemExit, reached from _PyErr_PrintEx at :786).
    /// Measured on the reference interpreter: SystemExit(3).args == (3,), .code == 3,
    /// str(SystemExit()) == '', repr(SystemExit('m')) == "SystemExit('m')".
    /// </summary>
    public class PySystemExit : PyBaseException
    {
        /// <summary>
        /// PySystemExitObject.code (Objects/exceptions.c:674 {"code", T_OBJECT, offsetof(...)}).
        /// A stored member, not a view of args: e.args = (9,) leaves the code alone and e.code = 5
        /// leaves args alone (measured, probes/l23_exit_fields.py).
        /// </summary>
        private PyObject _code;

        internal PySystemExit(PyObject[] args, PyDict kwargs = null) : base(args, kwargs)
        {
            _code = CodeForArgs(args);
        }

        public PySystemExit(int code = 0) : base(new PyObject[] { new PyInt(code) })
        {
            _code = new PyInt(code);
        }

        /// <summary>
        /// SystemExit_init (Objects/exceptions.c:633): no arguments leaves None (:640), one argument
        /// is the code (:643), and more than one argument makes the argument tuple the code (:646).
        /// The last rule is visible through sys.exit((1, 2)), which constructs with two arguments.
        /// </summary>
        internal static PyObject CodeForArgs(PyObject[] args)
        {
            if (args.Length == 0) return PyNone.Instance;
            if (args.Length == 1) return args[0];
            return new PyTuple(args);
        }

        public PyObject Code => _code ?? PyNone.Instance;

        /// <summary>member_set with NULL (what `del e.code` does) leaves the field NULL, and
        /// PyMember_GetOne reads a NULL T_OBJECT as None (Objects/structmember.c).</summary>
        internal void SetCode(PyObject value) => _code = value is PyNone ? null : value;

        internal void ClearCode() => _code = null;

        /// <summary>
        /// PyErr_GivenExceptionMatches(exc, PyExc_SystemExit), which is the test
        /// _Py_HandleSystemExit makes (Python/pythonrun.c:717) and the reason a subclass of
        /// SystemExit exits instead of printing a traceback. Type-based, not CLR-type based.
        /// </summary>
        internal static bool Matches(PyObject error)
        {
            if (error == null) return false;
            var type = error.GetPyType();
            return type != null && type.IsSubclassOf(PyType.SystemExitType);
        }

        public override PyType GetPyType() => PyType.SystemExitType;
        public override string GetTypeName() => "SystemExit";


        /// <summary>
        /// The one place that decides a process status from a SystemExit code value, copied from
        /// _Py_HandleSystemExit (Python/pythonrun.c:708):
        ///   None                     -> 0                        (:735)
        ///   PyLong_Check             -> (int)PyLong_AsLong       (:743)
        ///   anything else            -> written, then status 1   (:746-:762)
        /// tooLarge is the C long conversion failing: PyLong_AsLong returns -1 and leaves
        /// OverflowError raised, and that pending error - not a written string - is what reaches the
        /// report (probes/l24_write_huge.py).
        /// </summary>
        internal static int StatusOfCode(PyObject code, out PyObject toWrite, out bool tooLarge)
        {
            toWrite = null;
            tooLarge = false;
            if (code == null) code = PyNone.Instance;
            if (code is PyNone) return 0;
            // PyLong_Check accepts bool and int subclasses. In this CLR both are siblings of PyInt
            // rather than subclasses, so the family has to be spelled out here.
            System.Numerics.BigInteger value;
            if (code is PyInt exact) value = exact.Value;
            else if (code is PyBool flag) value = flag.Value ? 1 : 0;
            else if (code is PyIntSubclass subclass) value = subclass.GetIntValue().Value;
            else { toWrite = code; return 1; }
            // C long is 32 bits on Windows, so sys.exit(2**40) overflows even though a CLR long
            // would hold it (measured: rc 4294967295 and
            // "OverflowError: Python int too large to convert to C long" on stderr).
            if (value < int.MinValue || value > int.MaxValue)
            {
                tooLarge = true;
                return -1;
            }
            return (int)value;
        }

        public new static System.Exception Create(int code = 0)
        {
            var pyException = new PySystemExit(code);
            return new PythonException(pyException);
        }
    }

    public class PyKeyboardInterrupt : PyBaseException
    {
        /// <summary>
        /// KeyboardInterrupt has no constructor of its own - it uses BaseException_init
        /// (Objects/exceptions.c:42) - so KeyboardInterrupt().args is () and not a fixed phrase.
        /// The invented "keyboard interrupt" message is what made L2.2's new builtin name observable.
        /// </summary>
        public PyKeyboardInterrupt(PyObject[] args = null, PyDict kwargs = null)
            : base(args ?? new PyObject[0], kwargs) { }

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
        private static readonly string[] FieldNames =
            { "msg", "filename", "lineno", "offset", "text", "end_lineno", "end_offset", "print_file_and_line" };
        private readonly PyObject[] _fields = new PyObject[FieldNames.Length];

        // compiler_error and PyErr_RangedSyntaxLocationObject: msg, (filename, lineno, offset, text, end_lineno, end_offset)
        internal static PythonException At(string message, string filename, int line, int offset, int endLine, int endOffset,
            List<string> sourceLines, bool indentation = false, bool lastLineHasNewline = true)
        {
            var error = indentation ? new PyIndentationError(message) : new PySyntaxError(message, filename, line);
            PyObject text = sourceLines != null && line >= 1 && line <= sourceLines.Count
                ? new PyString(sourceLines[line - 1] + (lastLineHasNewline ? "\n" : ""))
                : PyNone.Instance;
            return Located(error, message, filename, line, offset, endLine, endOffset, text);
        }

        // _PyPegen_raise_tokenizer_init_error
        internal static PythonException TokenizerInit(string message, string filename)
        {
            var error = new PySyntaxError(message);
            var details = new PyTuple(new PyString(filename), new PyInt(0), new PyInt(-1), PyNone.Instance);
            error.ReadDetails(details);
            error.Args = new PyObject[] { new PyString(message), details };
            return new PythonException(error);
        }

        // SyntaxError(msg, (filename, lineno, offset, text, end_lineno, end_offset))
        internal static PythonException Located(PySyntaxError error, string message, string filename, int line, int offset,
            int endLine, int endOffset, PyObject text)
        {
            var details = new PyTuple(new PyString(filename), new PyInt(line), new PyInt(offset), text,
                new PyInt(endLine), new PyInt(endOffset));
            error.ReadDetails(details);
            error.Args = new PyObject[] { new PyString(message), details };
            return new PythonException(error);
        }

        internal static PythonException IntegerLiteralLimit(string message, string filename, int line, string source)
        {
            var error = new PySyntaxError(message, filename, line);
            var details = new PyTuple(new PyString(filename), new PyInt(line), SmallIntCache.GetOrCreate(0),
                new PyString((source ?? "") + "\n"), new PyInt(line), SmallIntCache.GetOrCreate(0));
            error.ReadDetails(details);
            error.Args = new PyObject[] { new PyString(message), details };
            return new PythonException(error);
        }

        public string FileName => (_fields[1] as PyString)?.Value;
        public int LineNumber => _fields[2] is PyInt line ? line.ToInt() : 0;

        public PySyntaxError(string message, string fileName = "<unknown>", int lineNumber = 0)
            : base(message)
        {
            _fields[0] = new PyString(message);
            if (fileName != null && fileName != "<unknown>") _fields[1] = new PyString(fileName);
            if (lineNumber > 0) _fields[2] = new PyInt(lineNumber);
        }

        // SyntaxError_init (Objects/exceptions.c:2340).
        internal PySyntaxError(PyObject[] args, PyDict kwargs) : base(args, kwargs) => SyntaxErrorInit(this, args, kwargs);

        // SyntaxError_init: BaseException_init; msg from one or more arguments, and from exactly
        // two the info sequence read by "OOOO|OO". A field the call does not give keeps its value.
        internal void SyntaxErrorInit(PyObject self, PyObject[] args, PyDict kwargs)
        {
            BaseExceptionInit(self, args, kwargs);
            if (args.Length >= 1) _fields[0] = args[0];
            if (args.Length != 2) return;
            var info = args[1] is PyTuple tuple ? tuple : (PyTuple)PyType.TupleType.Call(new[] { args[1] }, null);
            _fields[5] = _fields[6] = null;
            if (info.Items.Length < 4)
                throw PyTypeError.Create($"function takes at least 4 arguments ({info.Items.Length} given)");
            if (info.Items.Length > 6)
                throw PyTypeError.Create($"function takes at most 6 arguments ({info.Items.Length} given)");
            ReadDetails(info);
            if (info.Items.Length == 5)
                throw PyTypeError.Create("end_offset must be provided when end_lineno is provided");
        }

        private void ReadDetails(PyTuple info) => Array.Copy(info.Items, 0, _fields, 1, info.Items.Length);

        public override PyType GetPyType() => PyType.SyntaxErrorType;
        public override string GetTypeName() => "SyntaxError";

        protected internal override PyString Str() => SyntaxErrorStr();

        // SyntaxError_str (Objects/exceptions.c:2420).
        internal PyString SyntaxErrorStr()
        {
            string text = (_fields[0] ?? PyNone.Instance).ToStr().Value;
            string file = (_fields[1] as PyString)?.Value;
            bool hasLine = _fields[2] is PyInt;
            if (file != null) file = file.Substring(file.LastIndexOfAny(new[] { '/', '\\' }) + 1);
            if (file != null && hasLine) return new PyString($"{text} ({file}, line {LineNumber})");
            if (hasLine) return new PyString($"{text} (line {LineNumber})");
            return new PyString(file != null ? $"{text} ({file})" : text);
        }

        // SyntaxError_members
        internal override bool TryGetMember(string name, out PyObject value)
        {
            int field = Array.IndexOf(FieldNames, name);
            if (field < 0) return base.TryGetMember(name, out value);
            value = _fields[field];
            return true;
        }

        internal override bool TrySetMember(string name, PyObject value)
        {
            int field = Array.IndexOf(FieldNames, name);
            if (field < 0) return base.TrySetMember(name, value);
            _fields[field] = value;
            return true;
        }

        public new static System.Exception Create(string message, string fileName = "<unknown>", int lineNumber = 0)
        {
            var pyException = new PySyntaxError(message, fileName, lineNumber);
            return new PythonException(pyException);
        }
    }

    public class PyIndentationError : PySyntaxError
    {
        internal PyIndentationError(PyObject[] args, PyDict kwargs) : base(args, kwargs) { }

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

        internal PyStopIteration(PyObject[] args) : base(args) => StopIterationInit(this, args, null);

        // StopIteration_init: BaseException_init, then value the first argument or None.
        internal void StopIterationInit(PyObject self, PyObject[] args, PyDict kwargs)
        {
            BaseExceptionInit(self, args, kwargs);
            Value = args.Length > 0 ? args[0] : PyNone.Instance;
        }

        public override PyType GetPyType() => PyType.StopIterationType;
        public override string GetTypeName() => "StopIteration";

        // StopIteration_members. Value stays non-null for the iteration protocol; NULL reads as None.
        internal override bool TryGetMember(string name, out PyObject value)
        {
            if (name == "value") { value = Value; return true; }
            return base.TryGetMember(name, out value);
        }

        internal override bool TrySetMember(string name, PyObject value)
        {
            if (name == "value") { Value = value ?? PyNone.Instance; return true; }
            return base.TrySetMember(name, value);
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
    /// Python ReferenceError - Objects/exceptions.c PyExc_ReferenceError. Raised when a weak reference
    /// is used where the referent is expected and it is gone. Registered in builtins with
    /// Exception, BaseException, object beneath it (measured on the reference interpreter).
    /// </summary>
    public class PyReferenceError : PyException
    {
        internal PyReferenceError(PyObject[] args, PyDict kwargs) : base(args, kwargs) { }

        public PyReferenceError(string message = "") : base(message) { }

        public override string GetTypeName() => "ReferenceError";
        public override PyType GetPyType() => PyType.ReferenceErrorType;

        public new static PythonException Create(string message = "")
        {
            var pyException = new PyReferenceError(message);
            return new PythonException(pyException);
        }
    }

    /// <summary>
    /// Python BufferError - buffer protocol violation
    /// CPython 3.12: Raised when buffer operations fail
    /// </summary>
    public class PyBufferError : PyException
    {
        internal PyBufferError(PyObject[] args, PyDict kwargs) : base(args, kwargs) { }

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

            // Every errnomap key is a distinct small positive int, so its hash equals its value;
            // PyDict_GetItemWithError compares the stored key with the asked one, once.
            bool equal = PyObject.CompareBool(entry.Key, number, PyObject.CompareOp.EQ);
            return equal ? (PyOSError)entry.Type.Call(args, null) : new PyOSError(args, null, true);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        /// <summary>
        /// Windows host error formatted the way CPython's msvcrt-backed os
        /// module reports it: "[WinError n] &lt;localized message&gt;: 'path'".
        /// Godot builds keep the POSIX-style [Errno n] text (documented per host).
        /// </summary>
        internal static PythonException CreateWindowsFileError(int winCode, string path, string path2 = null)
        {
            string message;
            try
            {
                message = new System.ComponentModel.Win32Exception(winCode).Message;
                // FormatMessage text ends with ".\r\n"; CPython trims to the bare sentence.
                message = message.TrimEnd();
                if (message.EndsWith(".", StringComparison.Ordinal))
                    message = message.Substring(0, message.Length - 1);
            }
            catch
            {
                message = "Unknown error";
            }
            int errnoCode = WindowsErrorToErrno(winCode);
            PyObject[] args = path2 == null
                ? new PyObject[] { new PyInt(errnoCode), new PyString(message), new PyString(path), new PyInt(winCode) }
                : new PyObject[] { new PyInt(errnoCode), new PyString(message), new PyString(path), new PyInt(winCode), new PyString(path2) };
            return new PythonException(FromStructuredArguments(args));
        }

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
            => OSErrorFields(this, args, normalized);

        /// <summary>
        /// oserror_parse_args and oserror_init: from 2 to 5 arguments are errno, strerror, filename,
        /// winerror and filename2 (winerror rewriting errno on Windows); a filename takes the args
        /// down to the first two. self is the object whose args are set.
        /// </summary>
        internal void OSErrorFields(PyObject self, PyObject[] args, bool normalized = false)
        {
            if (!normalized && args.Length >= 4 && args.Length <= 5)
                args = NormalizeWindowsArguments(args);
            PyObject myerrno = null, strerror = null, filename = null, winerror = null, filename2 = null;
            if (args.Length >= 2 && args.Length <= 5)
            {
                myerrno = args[0];
                strerror = args[1];
                if (args.Length >= 3) filename = args[2];
                if (args.Length >= 4) winerror = args[3];
                if (args.Length == 5) filename2 = args[4];
            }
            if (filename != null && filename != PyNone.Instance)
            {
                if (ReferenceEquals(self.GetPyType(), PyType.BlockingIOErrorType) && NumberCheck(filename))
                    SetWritten(AsWrittenCount(filename));
                else
                {
                    _filename = filename;
                    if (filename2 != null && filename2 != PyNone.Instance) Filename2 = filename2;
                    args = new[] { args[0], args[1] };
                }
            }
            _errno = myerrno;
            _strerror = strerror;
            if (OperatingSystem.IsWindows()) _winerror = winerror;
            PyExceptionSlots.StoreArgs(self, args);
        }

        /// <summary>
        /// oserror_use_init: a subclass with an __init__ of its own and OSError's __new__ parses
        /// its arguments in __init__, and __new__ leaves the args empty.
        /// </summary>
        internal static bool UseInit(PyType type) =>
            !(type.FindNameInMro("__init__") is PyWrapperDescriptor init && ReferenceEquals(init._implementation, InitSlot))
            && type.FindNameInMro("__new__") is PyNewMethodDescriptor created && ReferenceEquals(created._implementation, NewSlot);

        // OSError_init: everything was done in OSError_new, unless use_init.
        internal static readonly Func<PyObject, PyObject[], PyDict, PyObject> InitSlot = (self, args, kwargs) =>
        {
            if (!UseInit(self.GetPyType()))
                return PyNone.Instance;
            if (kwargs != null && kwargs.Length() != 0)
                throw PyTypeError.Create($"{self.GetPyType().FullName}() takes no keyword arguments");
            ((PyOSError)PyExceptionSlots.StructOf(self)).OSErrorFields(self, args);
            return PyNone.Instance;
        };

        // OSError_new: without use_init the arguments are parsed here, and OSError itself becomes the
        // subclass errno names; with it the args stay empty for __init__.
        internal static readonly Func<PyObject, PyObject[], PyDict, PyObject> NewSlot = (cls, args, kwargs) =>
        {
            var type = (PyType)cls;
            bool useInit = UseInit(type);
            if (!useInit && kwargs != null && kwargs.Length() != 0)
                throw PyTypeError.Create($"{type.FullName}() takes no keyword arguments");
            if (type is not SharpPy.PyClass)
            {
                if (ReferenceEquals(type, PyType.OSErrorType))
                    return FromArguments(args, null);
                return type.CreateInstance(args, null);
            }
            var forwarded = new PyObject[args.Length + 1];
            forwarded[0] = cls;
            Array.Copy(args, 0, forwarded, 1, args.Length);
            var created = ((PyNewMethodDescriptor)PyType.BaseExceptionType.TypeDict["__new__"]).CallTpNew(forwarded, kwargs);
            if (useInit)
                PyExceptionSlots.StoreArgs(created, Array.Empty<PyObject>());
            else
                ((PyOSError)PyExceptionSlots.StructOf(created)).OSErrorFields(created, args);
            return created;
        };

        public PyOSError(string message = "") : base(message) { }
        // OSError_members
        internal override bool TryGetMember(string name, out PyObject value)
        {
            switch (name)
            {
                case "errno": value = _errno; return true;
                case "strerror": value = _strerror; return true;
                case "filename": value = _filename; return true;
                case "filename2": value = Filename2; return true;
                case "winerror" when OperatingSystem.IsWindows(): value = _winerror; return true;
            }
            return base.TryGetMember(name, out value);
        }

        internal override bool TrySetMember(string name, PyObject value)
        {
            switch (name)
            {
                case "errno": _errno = value; return true;
                case "strerror": _strerror = value; return true;
                case "filename": _filename = value; return true;
                case "filename2": Filename2 = value; return true;
                case "winerror" when OperatingSystem.IsWindows(): _winerror = value; return true;
            }
            return base.TrySetMember(name, value);
        }

        // OSError_getset: OSError_written_get and OSError_written_set.
        internal PyObject CharactersWritten =>
            _filename2 is WrittenState state ? SmallIntCache.GetOrCreate(state.Value) : throw PyAttributeError.Create("characters_written");

        internal void SetCharactersWritten(PyObject value)
        {
            if (value == null)
            {
                if (_filename2 is not WrittenState) throw PyAttributeError.Create("characters_written");
                SetWritten(-1);
                return;
            }
            SetWritten(AsWrittenCount(value));
        }

        protected internal override PyString Str() => OSErrorStr(this);

        // OSError_str (Objects/exceptions.c); self is the object whose args BaseException_str reads.
        internal PyString OSErrorStr(PyObject self)
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
            return BaseExceptionStr(self);
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
    /// <summary>
    /// CPython 3.12: io.UnsupportedOperation(OSError, ValueError) - raised by
    /// streams for operations the mode forbids (Modules/_io/iobmodule.c).
    /// </summary>
    public class PyUnsupportedOperation : PyOSError
    {
        public PyUnsupportedOperation(string message = "operation not supported") : base(message) { }

        internal PyUnsupportedOperation(PyObject[] args, PyDict kwargs) : base(args, kwargs) { }

        public static readonly PyType UnsupportedOperationPyType =
            new PyType("UnsupportedOperation", new[] { PyType.OSErrorType, PyType.ValueErrorType }, "io");

        static PyUnsupportedOperation()
        {
            // CPython reports io.UnsupportedOperation with __module__ == "io". It gets there by
            // executing UnsupportedOperation.__module__ = "io" in Lib/io.py, which works because
            // _io builds this exception as a heap type; the same assignment on ValueError or
            // _io._IOBase is rejected (measured). Opting in keeps that line runnable here.
            UnsupportedOperationPyType.AllowsModuleAssignment = true;
            UnsupportedOperationPyType.TypeDict["__module__"] = new PyString("io");
        }

        public override string GetTypeName() => "UnsupportedOperation";
        public override PyType GetPyType() => UnsupportedOperationPyType;

        public new static PythonException Create(string message = "operation not supported")
        {
            return new PythonException(new PyUnsupportedOperation(message));
        }
    }

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

        internal PyEOFError(PyObject[] args, PyDict kwargs) : base(args, kwargs) { }

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
        // encoding, object, start, end, reason (Objects/exceptions.c PyUnicodeErrorObject).
        private protected readonly PyObject[] Fields = new PyObject[5];
        private static readonly string[] FieldNames = { "encoding", "object", "start", "end", "reason" };

        [ThreadStatic] internal static bool AllocatingBlank;

        // BaseException_new: tp_new only allocates, tp_init reads the arguments.
        internal static void InstallNew()
        {
            foreach (var type in new[] { PyType.UnicodeEncodeErrorType, PyType.UnicodeDecodeErrorType, PyType.UnicodeTranslateErrorType })
            {
                var owner = type;
                var inherited = (PyNewMethodDescriptor)PyType.BaseExceptionType.TypeDict["__new__"];
                owner.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", owner, (cls, args, kwargs) =>
                {
                    // A subclass allocates as every exception class does.
                    if (!ReferenceEquals(cls, owner))
                    {
                        var forwarded = new PyObject[args.Length + 1];
                        forwarded[0] = cls;
                        Array.Copy(args, 0, forwarded, 1, args.Length);
                        return inherited.CallTpNew(forwarded, kwargs);
                    }
                    AllocatingBlank = true;
                    PyObject created;
                    try
                    {
                        created = ((PyType)cls).Call(new PyObject[0], null);
                    }
                    finally
                    {
                        AllocatingBlank = false;
                    }
                    // BaseException_new keeps the arguments; the fields wait for __init__.
                    PyExceptionSlots.StoreArgs(created, args);
                    return created;
                }, acceptsKwargs: true) { TpNew = inherited };
            }
        }

        internal PyUnicodeError(PyObject[] args, PyDict kwargs) : base(args, kwargs) { }

        public PyUnicodeError(string message = "") : base(message) { }

        /// <summary>UnicodeDecodeError_init and UnicodeEncodeError_init: "UOnnU" or "UUnnU".</summary>
        /// <summary>
        /// UnicodeEncodeError_init, UnicodeDecodeError_init and UnicodeTranslateError_init:
        /// BaseException_init, the str fields cleared, then PyArg_ParseTuple with "UUnnU", "UOnnU" or
        /// "UnnU" (the format starts at the field the type reads first). On failure they stay NULL.
        /// </summary>
        private protected void UnicodeErrorInit(PyObject self, PyObject[] args, PyDict kwargs, string format)
        {
            BaseExceptionInit(self, args, kwargs);
            int first = 5 - format.Length;
            for (int i = first; i < 5; i++)
                if (i != 2 && i != 3) Fields[i] = null;
            if (args.Length != format.Length)
                throw PyTypeError.Create($"function takes exactly {format.Length} arguments ({args.Length} given)");
            var parsed = new PyObject[format.Length];
            for (int i = 0; i < format.Length; i++)
            {
                var value = args[i];
                switch (format[i])
                {
                    case 'U':
                        if (value is not PyString && !value.GetPyType().IsSubclassOf(PyType.StrType))
                            throw PyTypeError.Create($"argument {i + 1} must be str, not {value.GetTypeName()}");
                        parsed[i] = value;
                        break;
                    case 'n':
                        parsed[i] = new PyInt(PyArgs.Ssize(value));
                        break;
                    default:
                        parsed[i] = value;
                        break;
                }
            }
            Array.Copy(parsed, 0, Fields, first, parsed.Length);
        }

        private protected int Start => Fields[2].ToInt();
        private protected int End => Fields[3].ToInt();
        private protected string Encoding => Fields[0].ToStr().Value;
        private protected string Reason => Fields[4].ToStr().Value;
        // The bounds UnicodeEncodeError_str and the others test before they read one item.
        private protected bool OneItem
        {
            get
            {
                int length = Fields[1].Length();
                return Start >= 0 && Start < length && End >= 0 && End <= length && End == Start + 1;
            }
        }

        /// <summary>The character at start as UnicodeEncodeError_str writes it.</summary>
        private protected string BadCharacter()
        {
            string item = ((PyString)Fields[1].GetItem(new PyInt(Start))).Value;
            int code = item.Length == 2 ? char.ConvertToUtf32(item, 0) : item[0];
            return code <= 0xFF ? $"\\x{code:x2}" : code <= 0xFFFF ? $"\\u{code:x4}" : $"\\U{code:x8}";
        }

        public override string GetTypeName() => "UnicodeError";
        public override PyType GetPyType() => PyType.UnicodeErrorType;

        // UnicodeError_members, which the three concrete types carry: start and end are T_PYSSIZET.
        internal override bool TryGetMember(string name, out PyObject value)
        {
            int field = Array.IndexOf(FieldNames, name);
            if (field < 0) return base.TryGetMember(name, out value);
            value = Fields[field] ?? (field is 2 or 3 ? SmallIntCache.GetOrCreate(0) : null);
            return true;
        }

        internal override bool TrySetMember(string name, PyObject value)
        {
            int field = Array.IndexOf(FieldNames, name);
            if (field < 0) return base.TrySetMember(name, value);
            if (field is 2 or 3)
            {
                // PyMember_SetOne: a numeric slot can't be NULL, and PyLong_AsSsize_t takes only an int.
                if (value == null)
                    throw PyTypeError.Create("can't delete numeric/char attribute");
                if (value is not PyInt number)
                    throw PyTypeError.Create("an integer is required");
                value = new PyInt(number.Value);
            }
            Fields[field] = value;
            return true;
        }

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
        // UnicodeDecodeError_init: the object is kept as bytes, whatever buffer it came as.
        internal PyUnicodeDecodeError(PyObject[] args, PyDict kwargs) : base(args, kwargs)
        {
            if (!AllocatingBlank) UnicodeDecodeErrorInit(this, args, kwargs);
        }

        internal void UnicodeDecodeErrorInit(PyObject self, PyObject[] args, PyDict kwargs)
        {
            UnicodeErrorInit(self, args, kwargs, "UOnnU");
            if (Fields[1] is not PyBytes)
            {
                var bytes = BytesMethods.Buffer(Fields[1]);
                if (bytes == null)
                {
                    var given = Fields[1];
                    Fields[0] = Fields[1] = Fields[4] = null;
                    throw PyTypeError.Create($"a bytes-like object is required, not '{given.GetTypeName()}'");
                }
                Fields[1] = new PyBytes(bytes);
            }
        }

        public override string GetTypeName() => "UnicodeDecodeError";
        public override PyType GetPyType() => PyType.UnicodeDecodeErrorType;

        protected internal override PyString Str() => UnicodeDecodeErrorStr();

        // UnicodeDecodeError_str (Objects/exceptions.c:3012).
        internal PyString UnicodeDecodeErrorStr() => new PyString(Fields[1] == null ? "" : OneItem
            ? $"'{Encoding}' codec can't decode byte 0x{((PyBytes)Fields[1]).Value[Start]:x2} in position {Start}: {Reason}"
            : $"'{Encoding}' codec can't decode bytes in position {Start}-{End - 1}: {Reason}");

        /// <summary>What a codec raises: the fields, from which the message follows.</summary>
        internal static PythonException Failure(string encoding, byte[] data, int start, int end, string reason)
            => new PythonException(new PyUnicodeDecodeError(new PyObject[]
            {
                new PyString(encoding), new PyBytes(data), new PyInt(start), new PyInt(end), new PyString(reason),
            }, null));
    }

    /// <summary>
    /// Python UnicodeEncodeError - 유니코드 인코딩 에러
    /// CPython 3.12: Raised when a Unicode-related error occurs during encoding
    /// </summary>
    public class PyUnicodeEncodeError : PyUnicodeError
    {
        internal PyUnicodeEncodeError(PyObject[] args, PyDict kwargs) : base(args, kwargs)
        {
            if (!AllocatingBlank) UnicodeEncodeErrorInit(this, args, kwargs);
        }

        internal void UnicodeEncodeErrorInit(PyObject self, PyObject[] args, PyDict kwargs) =>
            UnicodeErrorInit(self, args, kwargs, "UUnnU");

        public override string GetTypeName() => "UnicodeEncodeError";
        public override PyType GetPyType() => PyType.UnicodeEncodeErrorType;

        protected internal override PyString Str() => UnicodeEncodeErrorStr();

        // UnicodeEncodeError_str (Objects/exceptions.c:2891).
        internal PyString UnicodeEncodeErrorStr() => new PyString(Fields[1] == null ? "" : OneItem
            ? $"'{Encoding}' codec can't encode character '{BadCharacter()}' in position {Start}: {Reason}"
            : $"'{Encoding}' codec can't encode characters in position {Start}-{End - 1}: {Reason}");

        /// <summary>What a codec raises: the fields, from which the message follows.</summary>
        internal static PythonException Failure(string encoding, string text, int start, int end, string reason)
            => new PythonException(new PyUnicodeEncodeError(new PyObject[]
            {
                new PyString(encoding), new PyString(text), new PyInt(start), new PyInt(end), new PyString(reason),
            }, null));
    }

    /// <summary>Python UnicodeTranslateError. CPython 3.12: Objects/exceptions.c UnicodeTranslateError_init.</summary>
    public class PyUnicodeTranslateError : PyUnicodeError
    {
        internal PyUnicodeTranslateError(PyObject[] args, PyDict kwargs) : base(args, kwargs)
        {
            if (!AllocatingBlank) UnicodeTranslateErrorInit(this, args, kwargs);
        }

        internal void UnicodeTranslateErrorInit(PyObject self, PyObject[] args, PyDict kwargs) =>
            UnicodeErrorInit(self, args, kwargs, "UnnU");

        public override string GetTypeName() => "UnicodeTranslateError";
        public override PyType GetPyType() => PyType.UnicodeTranslateErrorType;

        protected internal override PyString Str() => UnicodeTranslateErrorStr();

        // UnicodeTranslateError_str
        internal PyString UnicodeTranslateErrorStr() => new PyString(Fields[1] == null ? "" : OneItem
            ? $"can't translate character '{BadCharacter()}' in position {Start}: {Reason}"
            : $"can't translate characters in position {Start}-{End - 1}: {Reason}");
    }

    public class PyTabError : PyIndentationError
    {
        internal PyTabError(PyObject[] args, PyDict kwargs) : base(args, kwargs) { }

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

    public class PyEncodingWarning : PyWarning
    {
        public PyEncodingWarning(PyObject[] args, PyDict kwargs = null) : base(args, kwargs) { }
        public PyEncodingWarning(string message = "") : base(message) { }
        public override string GetTypeName() => "EncodingWarning";
        public override PyType GetPyType() => PyType.EncodingWarningType;
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

        internal static readonly PyType TracebackPyType = TracebackType();

        // tb_next_set: another traceback or None, and never a loop.
        internal void SetNext(PyObject value)
        {
            if (value is PyNone)
            {
                Next = null;
                return;
            }
            if (value is not PyTraceback next)
                throw PyTypeError.Create($"expected traceback object or None, got '{value.GetTypeName()}'");
            for (var cursor = next; cursor != null; cursor = cursor.Next)
                if (ReferenceEquals(cursor, this))
                    throw PyValueError.Create("traceback loop detected");
            Next = next;
        }

        private static PyType TracebackType()
        {
            var type = new PyType("traceback", new[] { PyType.ObjectType });
            // tb_dir
            type.TypeDict["__dir__"] = new PyMethodDescriptor("__dir__", type, (self, args, kwargs) => new PyList(
                new PyObject[] { new PyString("tb_frame"), new PyString("tb_next"), new PyString("tb_lasti"), new PyString("tb_lineno") }), 0, 0);
            return type;
        }

        public override PyType GetPyType() => TracebackPyType;
        public override string GetTypeName() => "traceback";

        protected internal override PyString Str()
        {
            return new PyString($"<traceback object at {GetHashCode():X}>");
        }

        protected internal override PyString Repr() => AsString();

        internal PyObject Read(string name)
        {
            return name switch
            {
                "tb_frame" => Frame,
                "tb_next" => (PyObject?)Next ?? PyNone.Instance,
                "tb_lasti" => new PyInt(LastI * 2),
                "tb_lineno" => LineNo < 0 ? PyNone.Instance : new PyInt(LineNo),
                _ => base.TpGetattro(name)
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

        protected internal override PyString Str()
        {
            return new PyString($"ExceptionInfo(type={ExcType}, value={ExcValue}, traceback={ExcTraceback}, lasti={Lasti})");
        }

        protected internal override PyString Repr() => AsString();
    }

    #endregion
}
