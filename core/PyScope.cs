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
        BuiltinDict["type"] = PyTypeMetaclass.Instance;
        BuiltinDict["id"] = new PyBuiltinFunction("id");
        BuiltinDict["hash"] = new PyBuiltinFunction("hash");
        BuiltinDict["super"] = new PyBuiltinFunction("super");
        BuiltinDict["property"] = new PyBuiltinFunction("property");
        BuiltinDict["classmethod"] = new PyBuiltinFunction("classmethod");
        BuiltinDict["staticmethod"] = new PyBuiltinFunction("staticmethod");
        
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
        BuiltinDict["object"] = new PyBuiltinType("object");
        
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
        // Handle exception type constructors
        switch (Name)
        {
            case "ValueError":
                string message = args.Length > 0 ? args[0].ToStr() : "";
                return new PyValueError(message);
            case "TypeError":
                message = args.Length > 0 ? args[0].ToStr() : "";
                return new PyTypeError(message);
            case "AttributeError":
                message = args.Length > 0 ? args[0].ToStr() : "";
                return new PyAttributeError(message);
            case "KeyError":
                message = args.Length > 0 ? args[0].ToStr() : "";
                return new PyKeyError(message);
            case "IndexError":
                message = args.Length > 0 ? args[0].ToStr() : "";
                return new PyIndexError(message);
            case "RuntimeError":
                message = args.Length > 0 ? args[0].ToStr() : "";
                return new PyRuntimeError(message);
            case "ZeroDivisionError":
                message = args.Length > 0 ? args[0].ToStr() : "";
                return new PyZeroDivisionError(message);
            case "NameError":
                message = args.Length > 0 ? args[0].ToStr() : "";
                return new PyNameError(message);
            case "AssertionError":
                message = args.Length > 0 ? args[0].ToStr() : "";
                return new PyAssertionError(message);
            case "SyntaxError":
                message = args.Length > 0 ? args[0].ToStr() : "";
                return new PySyntaxError(message);
            case "BaseException":
                message = args.Length > 0 ? args[0].ToStr() : "";
                return new PyBaseException(message);
            case "Exception":
                message = args.Length > 0 ? args[0].ToStr() : "";
                return new PyException(message);
            
            // Handle Exception Groups (PEP 654)
            case "ExceptionGroup":
                if (args.Length < 2)
                    throw PyTypeError.Create("ExceptionGroup() missing required arguments");
                string groupMessage = args[0].ToStr();
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
                groupMessage = args[0].ToStr();
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
        // CPython 3.12: object type의 기본 속성들
        if (Name == "object")
        {
            switch (name)
            {
                case "__name__":
                    return new PyString(Name);
                case "__init__":
                    return new PyBuiltinMethod("__init__", (self, args) =>
                    {
                        // object.__init__() does nothing and returns None
                        return PyNone.Instance;
                    });
            }
        }

        // 기본 type 속성들
        switch (name)
        {
            case "__name__":
                return new PyString(Name);
            default:
                return base.GetAttribute(name);
        }
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
            // **핵심 수정**: 모듈 레벨에서는 GlobalScope에 저장
            else if (CurrentScope != null && CurrentScope.Name == "<module>")
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
        /// 글로벌 스코프의 모든 변수를 반환 (모듈 실행 후 변수 추출용)
        /// </summary>
        public Dictionary<string, PyObject> GetGlobalVariables()
        {
            if (GlobalScope?.Variables != null)
            {
                return new Dictionary<string, PyObject>(GlobalScope.Variables);
            }
            return new Dictionary<string, PyObject>();
        }

    }
    #endregion
}