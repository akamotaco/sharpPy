namespace SharpPy
{
    #region LEGB Scope System with Special Builtin Management

// 스코프 타입
public enum ScopeType
{
    Builtin,    // B - Built-in (특별 관리!)
    Global,     // G - Global (모듈 레벨)
    Enclosing,  // E - Enclosing (중첩 함수의 바깥 스코프)
    Local,      // L - Local (함수 내부)
    Module,     // M - Module level (for annotations)
    Class       // C - Class body (for annotations)
}

// 특별한 Builtin 모듈 (전역 싱글톤)
public class PyBuiltinsModule : PyObject
{
    public Dictionary<string, PyObject> BuiltinDict { get; }
    public static PyBuiltinsModule Instance { get; private set; }
    
    static PyBuiltinsModule()
    {
        Instance = new PyBuiltinsModule();
    }
    
    private PyBuiltinsModule()
    {
        BuiltinDict = new Dictionary<string, PyObject>();
        InitializeBuiltins();
    }
    
    private void InitializeBuiltins()
    {
        // 기본 내장 함수들
        BuiltinDict["print"] = new PyBuiltinFunction("print");
        BuiltinDict["input"] = new PyBuiltinFunction("input");
        BuiltinDict["len"] = new PyBuiltinFunction("len");
        BuiltinDict["repr"] = new PyBuiltinFunction("repr");
        BuiltinDict["abs"] = new PyBuiltinFunction("abs");
        BuiltinDict["callable"] = new PyBuiltinFunction("callable");
        
        // 컬렉션 및 이터레이터 함수들
        BuiltinDict["range"] = new PyBuiltinFunction("range");
        BuiltinDict["enumerate"] = new PyBuiltinFunction("enumerate");
        BuiltinDict["zip"] = new PyBuiltinFunction("zip");
        BuiltinDict["map"] = new PyBuiltinFunction("map");
        BuiltinDict["filter"] = new PyBuiltinFunction("filter");
        BuiltinDict["sorted"] = new PyBuiltinFunction("sorted");
        BuiltinDict["reversed"] = new PyBuiltinFunction("reversed");
        BuiltinDict["iter"] = new PyBuiltinFunction("iter");
        BuiltinDict["next"] = new PyBuiltinFunction("next");
        
        // 집계 함수들
        BuiltinDict["sum"] = new PyBuiltinFunction("sum");
        BuiltinDict["min"] = new PyBuiltinFunction("min");
        BuiltinDict["max"] = new PyBuiltinFunction("max");
        BuiltinDict["any"] = new PyBuiltinFunction("any");
        BuiltinDict["all"] = new PyBuiltinFunction("all");
        
        // 타입 및 리플렉션 함수들
        BuiltinDict["isinstance"] = new PyBuiltinFunction("isinstance");
        BuiltinDict["issubclass"] = new PyBuiltinFunction("issubclass");
        BuiltinDict["hasattr"] = new PyBuiltinFunction("hasattr");
        BuiltinDict["getattr"] = new PyBuiltinFunction("getattr");
        BuiltinDict["setattr"] = new PyBuiltinFunction("setattr");
        BuiltinDict["delattr"] = new PyBuiltinFunction("delattr");
        BuiltinDict["dir"] = new PyBuiltinFunction("dir");
        BuiltinDict["type"] = PyType.TypeType;  // Use PyType.TypeType instead of PyTypeMetaclass.Instance
        BuiltinDict["id"] = new PyBuiltinFunction("id");
        BuiltinDict["hash"] = new PyBuiltinFunction("hash");
        BuiltinDict["super"] = new PyBuiltinFunction("super");
        // CPython 3.12: Register descriptor types (property, classmethod, staticmethod)
        // These are both types and callable (via PyType.Call)
        BuiltinDict["property"] = PyType.PropertyType;
        BuiltinDict["classmethod"] = PyType.ClassMethodType;
        BuiltinDict["staticmethod"] = PyType.StaticMethodType;
        
        // CPython 3.12 호환성: 타입들을 PyType으로 등록 (isinstance 지원)
        BuiltinDict["str"] = PyType.StrType;
        BuiltinDict["bytes"] = PyType.BytesType;
        BuiltinDict["bytearray"] = PyType.BytearrayType;
        BuiltinDict["memoryview"] = PyType.MemoryViewType;
        BuiltinDict["int"] = PyType.IntType;
        BuiltinDict["float"] = PyType.FloatType;
        BuiltinDict["bool"] = PyType.BoolType;
        BuiltinDict["list"] = PyType.ListType;
        BuiltinDict["tuple"] = PyType.TupleType;
        BuiltinDict["dict"] = PyType.DictType;
        BuiltinDict["set"] = PyType.SetType;
        BuiltinDict["frozenset"] = PyType.FrozenSetType;
        
        // 수학 및 기타 함수들
        BuiltinDict["round"] = new PyBuiltinFunction("round");
        BuiltinDict["pow"] = new PyBuiltinFunction("pow");
        BuiltinDict["divmod"] = new PyBuiltinFunction("divmod");
        BuiltinDict["ord"] = new PyBuiltinFunction("ord");
        BuiltinDict["chr"] = new PyBuiltinFunction("chr");
        BuiltinDict["open"] = new PyBuiltinFunction("open");
        
        // 내장 상수들
        BuiltinDict["True"] = PyBool.True;
        BuiltinDict["False"] = PyBool.False;
        BuiltinDict["None"] = PyNone.Instance;
        
        // 클래스 생성 함수
        BuiltinDict["__build_class__"] = new PyBuiltinFunction("__build_class__");
        
        // 스코프 및 네임스페이스 함수들
        BuiltinDict["globals"] = new PyBuiltinFunction("globals");
        BuiltinDict["locals"] = new PyBuiltinFunction("locals");
        
        // 내장 타입들 (타입 객체, 변환 함수와 별개)
        // CPython 3.12: Use PyType.ObjectType instead of PyBuiltinType for proper attribute access
        BuiltinDict["object"] = PyType.ObjectType;
        
        // Exception Groups (PEP 654)
        BuiltinDict["BaseExceptionGroup"] = new PyBuiltinType("BaseExceptionGroup");
        BuiltinDict["ExceptionGroup"] = new PyBuiltinType("ExceptionGroup");
        
        // Exception Types - Use actual PyType objects for isinstance() compatibility
        BuiltinDict["BaseException"] = PyType.BaseExceptionType;
        BuiltinDict["Exception"] = PyType.ExceptionType;
        BuiltinDict["ValueError"] = PyType.ValueErrorType;
        BuiltinDict["TypeError"] = PyType.TypeErrorType;
        BuiltinDict["AttributeError"] = PyType.AttributeErrorType;
        BuiltinDict["KeyError"] = PyType.KeyErrorType;
        BuiltinDict["IndexError"] = PyType.IndexErrorType;
        BuiltinDict["RuntimeError"] = PyType.RuntimeErrorType;
        BuiltinDict["OSError"] = PyType.OSErrorType;
        BuiltinDict["ZeroDivisionError"] = PyType.ZeroDivisionErrorType;
        BuiltinDict["NameError"] = PyType.NameErrorType;
        BuiltinDict["StopIteration"] = PyType.StopIterationType;
        BuiltinDict["AssertionError"] = PyType.AssertionErrorType;
        BuiltinDict["SyntaxError"] = new PyBuiltinType("SyntaxError");
        BuiltinDict["ImportError"] = PyType.ImportErrorType;
        BuiltinDict["ModuleNotFoundError"] = PyType.ModuleNotFoundErrorType;

        // Warning Types (CPython 3.12)
        BuiltinDict["Warning"] = PyType.WarningType;
        BuiltinDict["UserWarning"] = PyType.UserWarningType;
        BuiltinDict["DeprecationWarning"] = PyType.DeprecationWarningType;
        BuiltinDict["PendingDeprecationWarning"] = PyType.PendingDeprecationWarningType;
        BuiltinDict["SyntaxWarning"] = PyType.SyntaxWarningType;
        BuiltinDict["RuntimeWarning"] = PyType.RuntimeWarningType;
        BuiltinDict["FutureWarning"] = PyType.FutureWarningType;
        BuiltinDict["ImportWarning"] = PyType.ImportWarningType;
        BuiltinDict["UnicodeWarning"] = PyType.UnicodeWarningType;
        BuiltinDict["BytesWarning"] = PyType.BytesWarningType;
        BuiltinDict["ResourceWarning"] = PyType.ResourceWarningType;

        // Buffer Protocol (PEP 688) - Functions
        BuiltinDict["bytes"] = new PyBuiltinFunction("bytes");
        BuiltinDict["bytearray"] = new PyBuiltinFunction("bytearray");
        BuiltinDict["memoryview"] = new PyBuiltinFunction("memoryview");
        BuiltinDict["buffer"] = new PyBuiltinType("buffer");
        
        SharpPyConfig.DebugWriteInternal($"🏗️ Builtin 모듈 초기화: {BuiltinDict.Count}개 내장 객체");
    }
    
    public override string GetTypeName() => "module";
    
    public override PyObject GetAttribute(string name)
    {
        if (name == "__name__") return new PyString("builtins");
        if (name == "__dict__") return new PyDict(BuiltinDict);
        
        if (BuiltinDict.TryGetValue(name, out PyObject value))
            return value;
            
        throw PyAttributeError.Create($"module 'builtins' has no attribute '{name}'");
    }
    
    public override void SetAttribute(string name, PyObject value)
    {
#if DEBUG_LOG
        Console.WriteLine($"⚠️ WARNING: builtin '{name}' 수정됨!");
#endif
        BuiltinDict[name] = value;
    }
    
    public PyObject GetBuiltin(string name)
    {
        return BuiltinDict.TryGetValue(name, out PyObject value) ? value : null;
    }

    /// <summary>
    /// CPython 3.12 호환: __builtins__['name'] 형태의 subscript access 지원
    /// </summary>
    public override PyObject GetItem(PyObject key)
    {
        if (key is PyString keyStr)
        {
            string name = keyStr.Value;
            if (BuiltinDict.TryGetValue(name, out PyObject value))
            {
                return value;
            }
            else
            {
                throw PyKeyError.Create($"'{name}'");
            }
        }
        else
        {
            throw PyTypeError.Create($"string indices must be strings, not {key.GetTypeName()}");
        }
    }

    public override string ToString() => "<module 'builtins' (built-in)>";
}

// 내장 타입 (간단한 구현)
public class PyBuiltinType : PyObject
{
    public string Name { get; }
    
    public PyBuiltinType(string name)
    {
        Name = name;
    }
    
    public override string GetTypeName() => "type";
    public override string ToString() => $"<class '{Name}'>";
    
    public override PyObject Call(PyObject[] args, PyDict kwargs = null)
    {
        // Handle exception type constructors and object constructor
        switch (Name)
        {
            case "object":
                // object() creates a new basic object instance
                if (args.Length > 0)
                {
                    throw PyTypeError.Create("object() takes no arguments");
                }
                return new PyInstance(); // Create basic object instance
            case "ValueError":
                string message = args.Length > 0 ? args[0].ToStr().Value : "";
                return new PyValueError(message);
            case "TypeError":
                message = args.Length > 0 ? args[0].ToStr().Value : "";
                return new PyTypeError(message);
            case "AttributeError":
                message = args.Length > 0 ? args[0].ToStr().Value : "";
                return new PyAttributeError(message);
            case "KeyError":
                message = args.Length > 0 ? args[0].ToStr().Value : "";
                return new PyKeyError(message);
            case "IndexError":
                message = args.Length > 0 ? args[0].ToStr().Value : "";
                return new PyIndexError(message);
            case "RuntimeError":
                message = args.Length > 0 ? args[0].ToStr().Value : "";
                return new PyRuntimeError(message);
            case "ZeroDivisionError":
                message = args.Length > 0 ? args[0].ToStr().Value : "";
                return new PyZeroDivisionError(message);
            case "NameError":
                message = args.Length > 0 ? args[0].ToStr().Value : "";
                return new PyNameError(message);
            case "AssertionError":
                message = args.Length > 0 ? args[0].ToStr().Value : "";
                return new PyAssertionError(message);
            case "SyntaxError":
                message = args.Length > 0 ? args[0].ToStr().Value : "";
                return new PySyntaxError(message);
            case "StopIteration":
                return args.Length > 0 ? new PyStopIteration(args[0]) : new PyStopIteration();
            case "BaseException":
                message = args.Length > 0 ? args[0].ToStr().Value : "";
                return new PyBaseException(message);
            case "Exception":
                message = args.Length > 0 ? args[0].ToStr().Value : "";
                return new PyException(message);
            
            // Handle Exception Groups (PEP 654)
            case "ExceptionGroup":
                if (args.Length < 2)
                    throw PyTypeError.Create("ExceptionGroup() missing required arguments");
                string groupMessage = args[0].ToStr().Value;
                var exceptions = new List<PyException>();
                if (args[1] is PyList list)
                {
                    foreach (var item in list.Items)
                    {
                        if (item is PyException exc)
                            exceptions.Add(exc);
                        else
                            throw PyTypeError.Create("ExceptionGroup requires list of exceptions");
                    }
                }
                return new PyExceptionGroup(groupMessage, exceptions);

            case "BaseExceptionGroup":
                if (args.Length < 2)
                    throw PyTypeError.Create("BaseExceptionGroup() missing required arguments");
                groupMessage = args[0].ToStr().Value;
                exceptions = new List<PyException>();
                if (args[1] is PyList list2)
                {
                    foreach (var item in list2.Items)
                    {
                        if (item is PyException exc)
                            exceptions.Add(exc);
                        else
                            throw PyTypeError.Create("BaseExceptionGroup requires list of exceptions");
                    }
                }
                return new PyBaseExceptionGroup(groupMessage, exceptions);
                
            default:
                return base.Call(args, kwargs);
        }
    }

    public override PyObject GetAttribute(string name)
    {
        // CPython 3.12: Built-in type attributes (type, object, str, int, etc.)
        switch (name)
        {
            case "__name__":
                return new PyString(Name);

            case "__new__":
                // CPython 3.12: All built-in types have __new__
                // Return a builtin method that creates instances
                if (Name == "object")
                {
                    // object.__new__(cls) creates a new instance
                    return new PyBuiltinMethod("__new__", (self, args) =>
                    {
                        if (args.Length < 1)
                            throw PyTypeError.Create("__new__() missing 1 required positional argument: 'cls'");

                        var cls = args[0];

                        // If cls is object, create a basic PyInstance
                        if (cls is PyBuiltinType builtin && builtin.Name == "object")
                            return new PyInstance();

                        // For other types, delegate to their __new__ if available
                        // This is a simplified implementation
                        return new PyInstance();
                    });
                }
                else if (Name == "type")
                {
                    // type.__new__(mcs, name, bases, classdict) creates a new type
                    return new PyBuiltinMethod("__new__", (self, args) =>
                    {
                        // Simplified: delegate to PyTypeMetaclass
                        if (args.Length >= 4)
                        {
                            // type.__new__(mcs, name, bases, classdict)
                            // Skip first argument (mcs) and pass the rest
                            var newArgs = new PyObject[args.Length - 1];
                            Array.Copy(args, 1, newArgs, 0, args.Length - 1);
                            return PyTypeMetaclass.Instance.Call(newArgs, null);
                        }
                        else
                        {
                            throw PyTypeError.Create($"type.__new__() takes exactly 4 arguments ({args.Length} given)");
                        }
                    });
                }
                else
                {
                    // For other built-in types (str, int, etc.), return their __new__
                    return new PyBuiltinMethod("__new__", (self, args) =>
                    {
                        if (args.Length < 1)
                            throw PyTypeError.Create("__new__() missing 1 required positional argument: 'cls'");

                        // Default: create instance using object.__new__
                        return new PyInstance();
                    });
                }

            case "__init__":
                // CPython 3.12: All built-in types have __init__
                if (Name == "object")
                {
                    return new PyBuiltinMethod("__init__", (self, args) =>
                    {
                        // object.__init__() does nothing and returns None
                        return PyNone.Instance;
                    });
                }
                else if (Name == "type")
                {
                    return new PyBuiltinMethod("__init__", (self, args) =>
                    {
                        // type.__init__() does basic initialization
                        return PyNone.Instance;
                    });
                }
                else
                {
                    return new PyBuiltinMethod("__init__", (self, args) =>
                    {
                        // Default __init__ does nothing
                        return PyNone.Instance;
                    });
                }

            case "__call__":
                // Only type has __call__
                if (Name == "type")
                {
                    return new PyBuiltinMethod("__call__", (self, args) =>
                    {
                        // type.__call__ creates instances
                        if (args.Length < 1)
                            throw PyTypeError.Create("__call__() missing arguments");

                        var cls = args[0];
                        if (cls is PyObject pyObj)
                        {
                            // Skip first argument (cls) and pass the rest
                            var newArgs = new PyObject[args.Length - 1];
                            Array.Copy(args, 1, newArgs, 0, args.Length - 1);
                            return pyObj.Call(newArgs, null);
                        }
                        return PyNone.Instance;
                    });
                }
                break;

            case "__mro__":
                // Return MRO for the type
                if (Name == "type")
                {
                    return new PyTuple(new PyObject[] { PyTypeMetaclass.Instance, PyType.ObjectType });
                }
                else if (Name == "object")
                {
                    return new PyTuple(new PyObject[] { PyType.ObjectType });
                }
                else
                {
                    // For other built-in types, return appropriate MRO
                    return new PyTuple(new PyObject[] { this, PyType.ObjectType });
                }

            case "__bases__":
                // Return base classes
                if (Name == "object")
                {
                    return new PyTuple(new PyObject[0]); // object has no bases
                }
                else
                {
                    return new PyTuple(new PyObject[] { PyType.ObjectType });
                }
        }

        // Fall back to base implementation
        return base.GetAttribute(name);
    }

    /// <summary>
    /// Union type operator support: int | str -> Union[int, str]
    /// </summary>
    public override PyObject BitwiseOr(PyObject other)
    {
        // Support Union operator for builtin types
        if (other is PyBuiltinType otherBuiltinType)
        {
            return new PyUnionType(new PyObject[] { this, otherBuiltinType });
        }
        else if (other is PyType otherType)
        {
            return new PyUnionType(new PyObject[] { this, otherType });
        }
        else if (other is PyUnionType unionType)
        {
            // Type | Union -> extend Union
            var newTypes = new List<PyObject> { this };
            newTypes.AddRange(unionType.Args);
            return new PyUnionType(newTypes.ToArray());
        }

        // Fall back to base implementation for non-type objects
        return base.BitwiseOr(other);
    }
}

// 일반 스코프 (G, E, L만)
public class PyScope
{
    public ScopeType Type { get; }
    public Dictionary<string, PyObject> Variables { get; }
    public PyScope EnclosingScope { get; }
    public string Name { get; }

    public PyScope(ScopeType type, PyScope enclosingScope = null, string name = "")
    {
        Type = type;
        Variables = new Dictionary<string, PyObject>();
        EnclosingScope = enclosingScope;
        Name = name;
    }

    /// <summary>
    /// 모듈용 생성자: 기존 딕셔너리를 Variables로 사용
    /// </summary>
    public PyScope(ScopeType type, Dictionary<string, PyObject> existingVariables, PyScope enclosingScope = null, string name = "")
    {
        Type = type;
        Variables = existingVariables ?? new Dictionary<string, PyObject>();
        EnclosingScope = enclosingScope;
        Name = name;
    }

    public void SetVariable(string name, PyObject value)
    {
        Variables[name] = value;
    }

    public PyObject GetVariable(string name)
    {
        return Variables.TryGetValue(name, out PyObject value) ? value : null;
    }

    public bool HasVariable(string name)
    {
        return Variables.ContainsKey(name);
    }

    public override string ToString()
    {
        return $"{Type} Scope '{Name}': {Variables.Count} variables";
    }
}

    // LEGB 스코프 체인 관리자 (Builtin 특별 관리)
    public class PyScopeChain
    {
        // 일반 스코프들만 리스트로 관리 (G, E, L)
        private readonly List<PyScope> _normalScopes;

        // Builtin은 전역 싱글톤 모듈로 특별 관리!
        private readonly PyBuiltinsModule _builtinModule;

        public PyScope CurrentScope => _normalScopes.LastOrDefault();
        public PyScope GlobalScope => _normalScopes.FirstOrDefault();
        public PyBuiltinsModule BuiltinModule => _builtinModule;
        public int ScopeCount => _normalScopes.Count;

        public PyScopeChain()
        {
            // Builtin은 전역 싱글톤 모듈 (특별!)
            _builtinModule = PyBuiltinsModule.Instance;

            // 일반 스코프들 (Global부터 시작)
            _normalScopes = new List<PyScope>();
            var globalScope = new PyScope(ScopeType.Global, null, "global");
            _normalScopes.Add(globalScope);

            // Global 스코프에 __builtins__ 참조 추가 (Python과 동일)
            globalScope.SetVariable("__builtins__", _builtinModule);

            SharpPyConfig.DebugWriteInternal("🏗️ LEGB 시스템 초기화 (Builtin 특별 관리)");
        }

        /// <summary>
        /// 모듈용 생성자: 기존 모듈 딕셔너리를 글로벌 스코프로 사용
        /// </summary>
        public PyScopeChain(Dictionary<string, PyObject> moduleDict, string moduleName)
        {
            // Builtin은 전역 싱글톤 모듈 (특별!)
            _builtinModule = PyBuiltinsModule.Instance;

            // 일반 스코프들 (모듈 딕셔너리를 Global로 시작)
            _normalScopes = new List<PyScope>();
            var globalScope = new PyScope(ScopeType.Global, moduleDict, null, moduleName);
            _normalScopes.Add(globalScope);

            // Global 스코프에 __builtins__ 참조 추가 (Python과 동일) - moduleDict가 이미 포함할 수 있음
            if (!moduleDict.ContainsKey("__builtins__"))
            {
                globalScope.SetVariable("__builtins__", _builtinModule);
            }

            SharpPyConfig.DebugWriteInternal($"🏗️ 모듈용 LEGB 시스템 초기화: {moduleName}");
        }

        public PyScope PushScope(ScopeType type, string name = "", PyScope enclosingScope = null)
        {
            if (type == ScopeType.Builtin)
            {
                throw new InvalidOperationException("❌ Builtin 스코프는 전역 싱글톤입니다! 직접 생성 불가.");
            }

            PyScope enclosing = enclosingScope ?? (type == ScopeType.Local ? CurrentScope : null);
            var newScope = new PyScope(type, enclosing, name);
            _normalScopes.Add(newScope);
#if DEBUG_LOG
            Console.WriteLine($"📁 스코프 추가: {newScope}");
#endif
            return newScope;
        }

        public void PopScope()
        {
#if DEBUG_LOG
            Console.WriteLine($"🔍 PopScope called: Current scope = {CurrentScope?.Type} '{CurrentScope?.Name}'");
#endif
            if (_normalScopes.Count > 1) // Global 유지
            {
                var removed = _normalScopes.Last();
                _normalScopes.RemoveAt(_normalScopes.Count - 1);
#if DEBUG_LOG
                Console.WriteLine($"🗑️ 스코프 제거: {removed.Type} '{removed.Name}' ({removed.Variables.Count} vars)");
#endif
#if DEBUG_LOG
                Console.WriteLine($"📂 새 현재 스코프: {CurrentScope?.Type} '{CurrentScope?.Name}'");
#endif
            }
            else
            {
#if DEBUG_LOG
                Console.WriteLine($"📂 PopScope 스킵: 최소 스코프 수준 (count={_normalScopes.Count})");
#endif
            }
        }

        public void RestoreScopeDepth(int targetDepth)
        {
#if DEBUG_LOG
            Console.WriteLine($"🔧 RestoreScopeDepth: Current={_normalScopes.Count}, Target={targetDepth}");
#endif
            while (_normalScopes.Count > targetDepth && _normalScopes.Count > 1) // Keep at least Global
            {
                PopScope();
            }
        }

        // LEGB 순서로 변수 탐색 (Builtin 특별 처리!)
        public PyObject LookupVariable(string name, HashSet<string> globalVars = null, bool verbose = false)
        {
#if DEBUG_LOG
            if (verbose) Console.WriteLine($"🔍 LEGB 탐색: '{name}' (Builtin=특별관리)");
#endif

            var currentScope = CurrentScope;

            // L - Local
            if (currentScope?.Type == ScopeType.Local && globalVars?.Contains(name) != true)
            {
#if DEBUG_LOG
                if (verbose) Console.WriteLine($"  L (Local '{currentScope.Name}'): 탐색...");
#endif
                var localResult = currentScope.GetVariable(name);
                if (localResult != null)
                {
#if DEBUG_LOG
                    if (verbose) Console.WriteLine($"  ✅ L에서 발견: {localResult}");
#endif
                    return localResult;
                }
#if DEBUG_LOG
                if (verbose) Console.WriteLine($"  ❌ L에서 못 찾음");
#endif
            }

            // E - Enclosing
            if (currentScope?.Type == ScopeType.Local && currentScope.EnclosingScope != null && globalVars?.Contains(name) != true)
            {
                var enclosingScope = currentScope.EnclosingScope;
                var enclosingLevel = 1;

                while (enclosingScope != null && enclosingScope.Type != ScopeType.Global)
                {
#if DEBUG_LOG
                    if (verbose) Console.WriteLine($"  E{enclosingLevel} (Enclosing '{enclosingScope.Name}'): 탐색...");
#endif
                    var enclosingResult = enclosingScope.GetVariable(name);
                    if (enclosingResult != null)
                    {
#if DEBUG_LOG
                        if (verbose) Console.WriteLine($"  ✅ E{enclosingLevel}에서 발견: {enclosingResult}");
#endif
                        return enclosingResult;
                    }
#if DEBUG_LOG
                    if (verbose) Console.WriteLine($"  ❌ E{enclosingLevel}에서 못 찾음");
#endif

                    enclosingScope = enclosingScope.EnclosingScope;
                    enclosingLevel++;
                }
            }

            // G - Global
#if DEBUG_LOG
            if (verbose) Console.WriteLine($"  G (Global): 탐색...");
#endif
            var globalResult = GlobalScope?.GetVariable(name);
            if (globalResult != null)
            {
#if DEBUG_LOG
                if (verbose) Console.WriteLine($"  ✅ G에서 발견: {globalResult}");
#endif
                return globalResult;
            }
#if DEBUG_LOG
            if (verbose) Console.WriteLine($"  ❌ G에서 못 찾음");
#endif

            // B - Built-in (특별한 전역 모듈에서!)
#if DEBUG_LOG
            if (verbose) Console.WriteLine($"  B (Builtin 전역모듈): 탐색...");
#endif
            var builtinResult = _builtinModule.GetBuiltin(name);
            if (builtinResult != null)
            {
#if DEBUG_LOG
                if (verbose) Console.WriteLine($"  ✅ B(전역모듈)에서 발견: {builtinResult}");
#endif
                return builtinResult;
            }
#if DEBUG_LOG
            if (verbose) Console.WriteLine($"  ❌ B에서 못 찾음");
#endif

            throw PyNameError.Create($"name '{name}' is not defined");
        }

        public void AssignVariable(string name, PyObject value, HashSet<string> globalVars = null)
        {
#if DEBUG_LOG
            Console.WriteLine($"🔍 AssignVariable Debug: name={name}, CurrentScope={CurrentScope?.Name}, Type={CurrentScope?.Type}");
#endif
            
            if (globalVars?.Contains(name) == true)
            {
                GlobalScope.SetVariable(name, value);
#if DEBUG_LOG
                Console.WriteLine($"📝 Global 변수 할당: {name} = {value}");
#endif
            }
            // **핵심 수정**: 모듈 실행에서는 GlobalScope에 저장 (CPython 3.12 호환)
            else if (CurrentScope != null && IsModuleScope(CurrentScope.Name))
            {
                GlobalScope.SetVariable(name, value);
#if DEBUG_LOG
                Console.WriteLine($"📝 Module → Global 변수 할당: {name} = {value}");
#endif
            }
            // **FIXED**: 클래스 바디 스코프에서는 로컬 클래스 스코프에 저장 (property chaining을 위해)
            else if (CurrentScope != null && CurrentScope.Name.StartsWith("<class_body_") && CurrentScope.Type == ScopeType.Local)
            {
                // 클래스 바디에서 정의되는 변수들을 로컬 클래스 스코프에 저장
                CurrentScope.SetVariable(name, value);
#if DEBUG_LOG
                Console.WriteLine($"📝 ClassBody → Local 변수 할당: {name} = {value} (in scope: {CurrentScope.Name})");
#endif
            }
            else if (CurrentScope != null)
            {
                CurrentScope.SetVariable(name, value);
#if DEBUG_LOG
                Console.WriteLine($"📝 {CurrentScope.Type} 변수 할당: {name} = {value}");
#endif
            }
            else
            {
                GlobalScope.SetVariable(name, value);
#if DEBUG_LOG
                Console.WriteLine($"📝 Default Global 변수 할당: {name} = {value}");
#endif
            }
        }

        // 시스템 상태 출력
        public void PrintSystemState()
        {
#if DEBUG_LOG
            Console.WriteLine($"\n📊 LEGB 시스템 상태:");
#endif
#if DEBUG_LOG
            Console.WriteLine($"  일반 스코프 수: {_normalScopes.Count}");
#endif
#if DEBUG_LOG
            Console.WriteLine($"  Builtin 모듈: {_builtinModule} (전역 싱글톤)");
#endif
#if DEBUG_LOG
            Console.WriteLine($"  Builtin 객체 수: {_builtinModule.BuiltinDict.Count}");
#endif

#if DEBUG_LOG
            Console.WriteLine($"📚 스코프 체인:");
#endif
            for (int i = _normalScopes.Count - 1; i >= 0; i--)
            {
                var scope = _normalScopes[i];
                var arrow = i == _normalScopes.Count - 1 ? "👉 " : "   ";
#if DEBUG_LOG
                Console.WriteLine($"  {arrow}{scope}");
#endif
            }
#if DEBUG_LOG
            Console.WriteLine($"  ⭐ Builtin Module (전역): {_builtinModule}");
#endif
        }

        /// <summary>
        /// 스코프 이름으로 모듈 스코프인지 판단
        /// </summary>
        private static bool IsModuleScope(string scopeName)
        {
            return scopeName == "<module>" ||
                   scopeName == "contextlib" ||
                   scopeName == "abc" ||
                   scopeName == "functools" ||
                   scopeName == "typing" ||
                   scopeName.EndsWith(".py") ||
                   scopeName.Contains("module");
        }


        /// <summary>
        /// 글로벌 스코프의 모든 변수를 반환 (모듈 실행 후 변수 추출용)
        /// 모듈 실행 시에는 CurrentScope(Local)의 변수들이 실제로는 모듈의 global 변수들임
        /// </summary>
        public Dictionary<string, PyObject> GetGlobalVariables()
        {
            var result = new Dictionary<string, PyObject>();

            // 글로벌 스코프의 변수들 반환 (CPython 3.12 호환)
            if (GlobalScope?.Variables != null)
            {
                foreach (var kvp in GlobalScope.Variables)
                {
                    result[kvp.Key] = kvp.Value;
                }
            }

            return result;
        }

    }
    #endregion
}