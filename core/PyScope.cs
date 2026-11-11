namespace SharpPy
{
    #region Globals and the builtins module


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

    /// <summary>
    /// A globals namespace: a module's dict (md_dict) or the dict exec and eval were given, read
    /// and written as PyGlobalsDict keeps the Python dict and the host view in step.
    /// </summary>
    public class PyScope
    {
        public IDictionary<string, PyObject> Variables { get; }
        public string Name { get; }
        // CPython 3.12: frame->f_globals references module->md_dict; globals() returns its dict.
        public PyModule Module { get; }

        public PyScope(string name) : this(null, name) { }

        public PyScope(IDictionary<string, PyObject> existingVariables, string name, PyModule module = null)
        {
            Variables = existingVariables ?? new PyNamespace();
            Name = name;
            Module = module;
        }

        public void SetVariable(string name, PyObject value) => PyGlobalsDict.Store(Variables, name, value);

        public bool DeleteVariable(string name) => PyGlobalsDict.Delete(Variables, name);

        public PyObject GetVariable(string name) => Variables.TryGetValue(name, out PyObject value) ? value : null;

        public bool HasVariable(string name) => Variables.ContainsKey(name);

        public override string ToString() => $"Globals '{Name}': {Variables.Count} variables";
    }

    /// <summary>
    /// A frame's f_globals: the module (or exec) namespace and the builtins module beside it. The
    /// locals of a function are its localsplus slots, of a class body or exec its mapping
    /// (PyFrame.ClassLocalsDict); this holds no other scope.
    /// </summary>
    public class PyScopeChain
    {
        public PyScope GlobalScope { get; }
        public PyBuiltinsModule BuiltinModule { get; } = PyBuiltinsModule.Instance;

        /// <summary>A fresh namespace with the builtins module as its __builtins__.</summary>
        public PyScopeChain()
        {
            GlobalScope = new PyScope("global");
            GlobalScope.SetVariable("__builtins__", BuiltinModule);
        }

        // CPython 3.12: frame->f_globals references module->md_dict.
        public PyScopeChain(IDictionary<string, PyObject> moduleDict, string moduleName, PyModule module = null)
        {
            GlobalScope = new PyScope(moduleDict, moduleName, module);
            // exec_code_in_module: only __main__ holds the module, other modules hold its dict.
            if (moduleDict is not PyDictVariables && !moduleDict.ContainsKey("__builtins__"))
                GlobalScope.SetVariable("__builtins__", moduleName == "__main__" ? BuiltinModule : BuiltinModule.GetAttribute("__dict__"));
        }

        // The host's lookup of a name: the globals, then the builtins.
        public PyObject LookupVariable(string name)
        {
            return GlobalScope?.GetVariable(name) ?? BuiltinModule.GetBuiltin(name)
                ?? throw PyNameError.Create($"name '{name}' is not defined", name);
        }

        public void AssignVariable(string name, PyObject value) => GlobalScope.SetVariable(name, value);
    }

    #endregion
}
