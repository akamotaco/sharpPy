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

    // CPython 3.12 compatible: Builtin module (singleton)
    // This wraps the PyModule created by BuiltinsModule.CreateBuiltinsModule()
    public class PyBuiltinsModule : PyModule
    {
        public static PyBuiltinsModule Instance { get; private set; }

        // CPython 3.12: BuiltinDict is alias for ModuleDict
        public IDictionary<string, PyObject> BuiltinDict => ModuleDict;

        static PyBuiltinsModule()
        {
            // CPython 3.12 방식: 단 하나의 builtins 모듈만 생성
            // BuiltinsModule.CreateBuiltinsModule()이 유일한 초기화 지점
            Instance = CreateInstance();
        }

        private static PyBuiltinsModule CreateInstance()
        {
            // BuiltinsModule에서 생성한 모듈을 기반으로 초기화
            var builtinsModule = SharpPy.Modules.BuiltinsModule.CreateBuiltinsModule();
                var pyBuiltinsModule = new PyBuiltinsModule(builtinsModule);
            
    #if DEBUG_VM_LOG
            Console.WriteLine($"[PYBUILTINS] Created from BuiltinsModule");
            Console.WriteLine($"[PYBUILTINS] ModuleDict count: {pyBuiltinsModule.ModuleDict.Count}");
            Console.WriteLine($"[PYBUILTINS] Has 'type': {pyBuiltinsModule.ModuleDict.ContainsKey("type")}");
    #endif
            if (pyBuiltinsModule.ModuleDict.ContainsKey("type"))
            {
                var typeObj = pyBuiltinsModule.ModuleDict["type"];
    #if DEBUG_VM_LOG
                Console.WriteLine($"[PYBUILTINS] type = {typeObj.GetType().Name}");
    #endif
            }

            return pyBuiltinsModule;
        }

        private PyBuiltinsModule(PyModule sourceModule)
            : base("builtins", "<builtins module>")
        {
            // BuiltinsModule에서 생성한 ModuleDict를 직접 사용 (복사 아님!)
            // CPython 방식: 하나의 dict, 모든 곳에서 같은 객체 사용
            ModuleDict.Clear();  // 기본 초기화된 __name__, __file__ 등 제거

            // BuiltinsModule의 dict를 그대로 사용
            foreach (var kvp in sourceModule.ModuleDict)
            {
                ModuleDict[kvp.Key] = kvp.Value;
            }

            SharpPyConfig.DebugWriteInternal($"🏗️ PyBuiltinsModule 초기화: {ModuleDict.Count}개 내장 객체 (from BuiltinsModule)");
        }

        public PyObject GetBuiltin(string name)
        {
            return ModuleDict.TryGetValue(name, out PyObject value) ? value : null;
        }

        public IEnumerable<string> GetAllBuiltinNames()
        {
            return ModuleDict.Keys;
        }

        public override string ToString() => "<module 'builtins' (built-in)>";
    }

    // 일반 스코프 (G, E, L만)
    public class PyScope
    {
        public ScopeType Type { get; }
        private IDictionary<string, PyObject> _variables;
        public virtual IDictionary<string, PyObject> Variables => _variables ??= CreateVariables();
        public PyScope EnclosingScope { get; }
        public string Name { get; }
        // CPython 3.12: frame->f_globals references module->md_dict
        // Store module reference for global scopes to support globals() builtin
        public PyModule Module { get; }

        public PyScope(ScopeType type, PyScope enclosingScope = null, string name = "")
            : this(type, enclosingScope, name, false)
        {
        }

        protected PyScope(ScopeType type, PyScope enclosingScope, string name, bool deferVariables)
        {
            Type = type;
            if (!deferVariables) _variables = CreateVariables();
            EnclosingScope = enclosingScope;
            Name = name;
            Module = null;
        }

        /// <summary>
        /// 모듈용 생성자: 기존 딕셔너리를 Variables로 사용
        /// CPython 3.12: Python/ceval.c - frame->f_globals points to module->md_dict
        /// </summary>
        public PyScope(ScopeType type, IDictionary<string, PyObject> existingVariables, PyScope enclosingScope = null, string name = "", PyModule module = null)
        {
            Type = type;
            _variables = existingVariables ?? CreateVariables();
            EnclosingScope = enclosingScope;
            Name = name;
            Module = module; // Store module for globals() to return module.__dict__
        }

        private IDictionary<string, PyObject> CreateVariables() => Type == ScopeType.Global || Type == ScopeType.Module
            ? new PyNamespace() : new Dictionary<string, PyObject>();

        public void SetVariable(string name, PyObject value)
        {
            if (Type == ScopeType.Global || Type == ScopeType.Module) PyGlobalsDict.Store(Variables, name, value);
            else Variables[name] = value;
        }

        public bool DeleteVariable(string name) => Type == ScopeType.Global || Type == ScopeType.Module
            ? PyGlobalsDict.Delete(Variables, name) : Variables.Remove(name);

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

    // Optimized function scopes materialize their C# dictionary only on access.
    internal sealed class PyFastLocalScope : PyScope
    {
        private readonly PyFrame _frame;
        internal PyFastLocalScope(PyFrame frame, PyScope enclosingScope, string name)
            : base(ScopeType.Local, enclosingScope, name, true) => _frame = frame;

        public override IDictionary<string, PyObject> Variables
        {
            get
            {
                var variables = base.Variables;
                _frame.RefreshScopeLocals(variables);
                return variables;
            }
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

        // LOAD_NAME's locals and globals, for a frame whose builtins are its own.
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
        // CPython 3.12: Python/ceval.c - frame->f_globals references module->md_dict
        // Constructor for module scope - requires module reference
        public PyScopeChain(IDictionary<string, PyObject> moduleDict, string moduleName, PyModule module = null)
        {
            // Builtin은 전역 싱글톤 모듈 (특별!)
            _builtinModule = PyBuiltinsModule.Instance;

            // 일반 스코프들 (모듈 딕셔너리를 Global로 시작)
            _normalScopes = new List<PyScope>();
            var globalScope = new PyScope(ScopeType.Global, moduleDict, null, moduleName, module);
            _normalScopes.Add(globalScope);

            // Global 스코프에 __builtins__ 참조 추가 (Python과 동일) - moduleDict가 이미 포함할 수 있음
            // exec_code_in_module: only __main__ holds the module, other modules hold its dict.
            if (moduleDict is not PyDictVariables && !moduleDict.ContainsKey("__builtins__"))
            {
                globalScope.SetVariable("__builtins__", moduleName == "__main__" ? _builtinModule : _builtinModule.GetAttribute("__dict__"));
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

        internal PyScope PushFastLocalScope(PyFrame frame, string name)
        {
            var scope = new PyFastLocalScope(frame, CurrentScope, name);
            _normalScopes.Add(scope);
            return scope;
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

            // CPython 3.12: raise a plain message plus the .name attribute.
            // "Did you mean" suggestions are produced by the traceback display layer
            // (Python/suggestions.c offer_suggestions_for_name_error / Lib/traceback.py),
            // never baked into the caught exception's args.
            throw PyNameError.Create($"name '{name}' is not defined", name);
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
            // Scope ownership is explicit; a function name must not redirect locals
            // to globals (e.g. functions named "module", "abc" or "typing").
            // CPython v3.12.15, Python/ceval.c: initialize_locals uses frame localsplus.
            else if (CurrentScope != null &&
                     (CurrentScope.Type == ScopeType.Global || CurrentScope.Type == ScopeType.Module))
            {
                GlobalScope.SetVariable(name, value);
#if DEBUG_LOG
                Console.WriteLine($"📝 Module → Global 변수 할당: {name} = {value}");
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
