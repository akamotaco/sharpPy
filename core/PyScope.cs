namespace SharpPy
{
    #region LEGB Scope System with Special Builtin Management

// 스코프 타입
public enum ScopeType
{
    Builtin,    // B - Built-in (특별 관리!)
    Global,     // G - Global (모듈 레벨)
    Enclosing,  // E - Enclosing (중첩 함수의 바깥 스코프)
    Local       // L - Local (함수 내부)
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
        // 내장 함수들
        BuiltinDict["print"] = new PyBuiltinFunction("print");
        BuiltinDict["len"] = new PyBuiltinFunction("len");
        BuiltinDict["abs"] = new PyBuiltinFunction("abs");
        BuiltinDict["max"] = new PyBuiltinFunction("max");
        BuiltinDict["min"] = new PyBuiltinFunction("min");
        BuiltinDict["callable"] = new PyBuiltinFunction("callable");
        BuiltinDict["isinstance"] = new PyBuiltinFunction("isinstance");
        
        // 내장 상수들
        BuiltinDict["True"] = PyBool.True;
        BuiltinDict["False"] = PyBool.False;
        BuiltinDict["None"] = PyNone.Instance;
        
        // 내장 타입들
        BuiltinDict["int"] = new PyBuiltinType("int");
        BuiltinDict["str"] = new PyBuiltinType("str");
        BuiltinDict["bool"] = new PyBuiltinType("bool");
        BuiltinDict["object"] = new PyBuiltinType("object");
        
        Console.WriteLine($"🏗️ Builtin 모듈 초기화: {BuiltinDict.Count}개 내장 객체");
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
        Console.WriteLine($"⚠️ WARNING: builtin '{name}' 수정됨!");
        BuiltinDict[name] = value;
    }
    
    public PyObject GetBuiltin(string name)
    {
        return BuiltinDict.TryGetValue(name, out PyObject value) ? value : null;
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

            Console.WriteLine("🏗️ LEGB 시스템 초기화 (Builtin 특별 관리)");
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
            Console.WriteLine($"📁 스코프 추가: {newScope}");
            return newScope;
        }

        public void PopScope()
        {
            if (_normalScopes.Count > 1) // Global 유지
            {
                var removed = _normalScopes.Last();
                _normalScopes.RemoveAt(_normalScopes.Count - 1);
                Console.WriteLine($"🗑️ 스코프 제거: {removed}");
            }
        }

        // LEGB 순서로 변수 탐색 (Builtin 특별 처리!)
        public PyObject LookupVariable(string name, HashSet<string> globalVars = null, bool verbose = false)
        {
            if (verbose) Console.WriteLine($"🔍 LEGB 탐색: '{name}' (Builtin=특별관리)");

            var currentScope = CurrentScope;

            // L - Local
            if (currentScope?.Type == ScopeType.Local && globalVars?.Contains(name) != true)
            {
                if (verbose) Console.WriteLine($"  L (Local '{currentScope.Name}'): 탐색...");
                var localResult = currentScope.GetVariable(name);
                if (localResult != null)
                {
                    if (verbose) Console.WriteLine($"  ✅ L에서 발견: {localResult}");
                    return localResult;
                }
                if (verbose) Console.WriteLine($"  ❌ L에서 못 찾음");
            }

            // E - Enclosing
            if (currentScope?.Type == ScopeType.Local && currentScope.EnclosingScope != null && globalVars?.Contains(name) != true)
            {
                var enclosingScope = currentScope.EnclosingScope;
                var enclosingLevel = 1;

                while (enclosingScope != null && enclosingScope.Type != ScopeType.Global)
                {
                    if (verbose) Console.WriteLine($"  E{enclosingLevel} (Enclosing '{enclosingScope.Name}'): 탐색...");
                    var enclosingResult = enclosingScope.GetVariable(name);
                    if (enclosingResult != null)
                    {
                        if (verbose) Console.WriteLine($"  ✅ E{enclosingLevel}에서 발견: {enclosingResult}");
                        return enclosingResult;
                    }
                    if (verbose) Console.WriteLine($"  ❌ E{enclosingLevel}에서 못 찾음");

                    enclosingScope = enclosingScope.EnclosingScope;
                    enclosingLevel++;
                }
            }

            // G - Global
            if (verbose) Console.WriteLine($"  G (Global): 탐색...");
            var globalResult = GlobalScope?.GetVariable(name);
            if (globalResult != null)
            {
                if (verbose) Console.WriteLine($"  ✅ G에서 발견: {globalResult}");
                return globalResult;
            }
            if (verbose) Console.WriteLine($"  ❌ G에서 못 찾음");

            // B - Built-in (특별한 전역 모듈에서!)
            if (verbose) Console.WriteLine($"  B (Builtin 전역모듈): 탐색...");
            var builtinResult = _builtinModule.GetBuiltin(name);
            if (builtinResult != null)
            {
                if (verbose) Console.WriteLine($"  ✅ B(전역모듈)에서 발견: {builtinResult}");
                return builtinResult;
            }
            if (verbose) Console.WriteLine($"  ❌ B에서 못 찾음");

            throw PyNameError.Create($"name '{name}' is not defined");
        }

        public void AssignVariable(string name, PyObject value, HashSet<string> globalVars = null)
        {
            if (globalVars?.Contains(name) == true)
            {
                GlobalScope.SetVariable(name, value);
                Console.WriteLine($"📝 Global 변수 할당: {name} = {value}");
            }
            else if (CurrentScope != null)
            {
                CurrentScope.SetVariable(name, value);
                Console.WriteLine($"📝 {CurrentScope.Type} 변수 할당: {name} = {value}");
            }
            else
            {
                GlobalScope.SetVariable(name, value);
                Console.WriteLine($"📝 Default Global 변수 할당: {name} = {value}");
            }
        }

        // 시스템 상태 출력
        public void PrintSystemState()
        {
            Console.WriteLine($"\n📊 LEGB 시스템 상태:");
            Console.WriteLine($"  일반 스코프 수: {_normalScopes.Count}");
            Console.WriteLine($"  Builtin 모듈: {_builtinModule} (전역 싱글톤)");
            Console.WriteLine($"  Builtin 객체 수: {_builtinModule.BuiltinDict.Count}");

            Console.WriteLine($"📚 스코프 체인:");
            for (int i = _normalScopes.Count - 1; i >= 0; i--)
            {
                var scope = _normalScopes[i];
                var arrow = i == _normalScopes.Count - 1 ? "👉 " : "   ";
                Console.WriteLine($"  {arrow}{scope}");
            }
            Console.WriteLine($"  ⭐ Builtin Module (전역): {_builtinModule}");
        }
    }
    #endregion
}