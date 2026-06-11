using SharpPy.Tools;

#if GODOT
using IOHelper = Godot_IO.Helper;
#else
using IOHelper = DotNet_IO.Helper;
#endif

namespace SharpPy
{
    #region Import Monitoring

    /// <summary>
    /// Import 진행 단계 — 게임 로딩 화면에서 피드백 용도
    /// </summary>
    public enum ImportPhase
    {
        /// <summary>sys.path에서 모듈 파일 탐색 중</summary>
        Searching,
        /// <summary>파일에서 소스 코드 읽는 중</summary>
        Loading,
        /// <summary>소스 → AST 파싱 중</summary>
        Parsing,
        /// <summary>AST → 바이트코드 컴파일 중</summary>
        Compiling,
        /// <summary>모듈 코드 실행 중</summary>
        Executing,
        /// <summary>C# 빌트인 모듈 초기화 중</summary>
        BuiltinInit,
        /// <summary>모듈 로드 완료</summary>
        Done,
        /// <summary>sys.modules 캐시 히트 (로드 불필요)</summary>
        CacheHit,
    }

    /// <summary>
    /// Import 이벤트 데이터 — 게임에서 로딩 상태를 모니터링할 수 있는 구조체
    /// </summary>
    public readonly struct ImportEvent
    {
        /// <summary>현재 진행 단계</summary>
        public readonly ImportPhase Phase;
        /// <summary>모듈 이름 (예: "collections.abc")</summary>
        public readonly string ModuleName;
        /// <summary>파일 경로 (빌트인이면 null)</summary>
        public readonly string FilePath;

        public ImportEvent(ImportPhase phase, string moduleName, string filePath = null)
        {
            Phase = phase;
            ModuleName = moduleName;
            FilePath = filePath;
        }

        public override string ToString() =>
            FilePath != null
                ? $"[{Phase}] {ModuleName} ({FilePath})"
                : $"[{Phase}] {ModuleName}";
    }

    #endregion

    #region Module and Import System

// PEP 420 네임스페이스 패키지 (PyModule을 상속받아 확장)
public class PyNamespaceModule : PyModule
{
    public List<string> NamespaceDirs { get; }
    
    public PyNamespaceModule(string name, List<string> namespaceDirs) : base(name)
    {
        NamespaceDirs = new List<string>(namespaceDirs);

        // __path__ 속성 설정 (PEP 420 요구사항)
        // Performance: Eliminated LINQ - manual conversion instead of Select + ToArray
        var pathList = new PyObject[namespaceDirs.Count];
        for (int i = 0; i < namespaceDirs.Count; i++)
        {
            pathList[i] = new PyStr(namespaceDirs[i]);
        }
        ModuleDict["__path__"] = new PyList(pathList);

        // 네임스페이스 패키지 표시
        ModuleDict["__file__"] = PyNone.Instance; // 네임스페이스 패키지는 __file__이 None
        ModuleDict["__doc__"] = new PyStr($"Namespace package {Name}");
    }
    
    /// <summary>
    /// 네임스페이스 패키지 내에서 서브모듈 검색
    /// </summary>
    public PyModule FindSubmodule(string submoduleName)
    {
        foreach (var namespaceDir in NamespaceDirs)
        {
            // 서브모듈 파일 검색
            var submoduleFile = IOHelper.CombinePath(namespaceDir, submoduleName + ".py");
            if (IOHelper.FileExists(submoduleFile))
            {
                var fullName = $"{Name}.{submoduleName}";
                return PyImportSystem.LoadModuleFromFile(fullName, submoduleFile);
            }

            // 서브패키지 검색
            var subpackageDir = IOHelper.CombinePath(namespaceDir, submoduleName);
            if (IOHelper.DirExists(subpackageDir))
            {
                var initFile = IOHelper.CombinePath(subpackageDir, "__init__.py");
                var fullName = $"{Name}.{submoduleName}";

                if (IOHelper.FileExists(initFile))
                {
                    // 일반 패키지
                    return PyImportSystem.LoadModuleFromFile(fullName, initFile);
                }
                else
                {
                    // 중첩된 네임스페이스 패키지
                    var nestedNamespaceDirs = new List<string> { subpackageDir };

                    // 다른 네임스페이스 디렉토리에서도 동일한 서브패키지 검색
                    // Performance: Eliminated LINQ - manual filtering instead of Where
                    for (int i = 0; i < NamespaceDirs.Count; i++)
                    {
                        var otherDir = NamespaceDirs[i];
                        if (otherDir == namespaceDir)
                            continue;

                        var otherSubpackageDir = IOHelper.CombinePath(otherDir, submoduleName);
                        if (IOHelper.DirExists(otherSubpackageDir) &&
                            !IOHelper.FileExists(IOHelper.CombinePath(otherSubpackageDir, "__init__.py")))
                        {
                            nestedNamespaceDirs.Add(otherSubpackageDir);
                        }
                    }

                    return PyImportSystem.CreateNamespacePackage(fullName, nestedNamespaceDirs);
                }
            }
        }
        
        return null;
    }
    
    public override string ToString() => $"<module '{Name}' (namespace)>";
}

// Python 모듈 객체
public class PyModule : PyObject
{
    public string Name { get; }
    public string FileName { get; }
    public Dictionary<string, PyObject> ModuleDict { get; }  // __dict__ (Variable == Attribute)
    public List<string> All { get; set; }  // __all__
    public bool IsInitialized { get; private set; }

    // CPython 3.12 requirement: module.__dict__ is module.__dict__ must be True
    // Cache the PyDict wrapper to ensure identity consistency
    private PyDict _cachedDict;

    public PyModule(string name, string fileName = null)
    {
        Name = name;
        FileName = fileName ?? $"{name}.py";
        ModuleDict = new Dictionary<string, PyObject>();
        All = new List<string>();

        InitializeBuiltinAttributes();
    }
    
    private void InitializeBuiltinAttributes()
    {
        ModuleDict["__name__"] = new PyStr(Name);
        ModuleDict["__file__"] = new PyStr(FileName);
        ModuleDict["__doc__"] = new PyStr($"Module {Name}");

        // CPython 3.12: Set __package__
        // For packages (__init__.py): __package__ = __name__
        // For modules: __package__ = parent package name (or "" for top-level)
        bool isPackage = FileName != null && FileName.EndsWith("__init__.py", StringComparison.Ordinal);

        if (isPackage)
        {
            // Package: __package__ == __name__
            ModuleDict["__package__"] = new PyStr(Name);
        }
        else
        {
            // Module: __package__ is parent package
            int lastDot = Name.LastIndexOf('.');
            string packageName = lastDot >= 0 ? Name.Substring(0, lastDot) : "";
            ModuleDict["__package__"] = string.IsNullOrEmpty(packageName)
                ? PyNone.Instance
                : new PyStr(packageName);
        }
    }
    
    public override PyType GetPyType() => PyType.ModuleType;
    public override string GetTypeName() => "module";
    
    // 모듈 attribute 접근 (Variable == Attribute)
    public override PyObject GetAttribute(string name)
    {
        // CPython 3.12: Objects/moduleobject.c:783 - _Py_module_getattro_impl
        // Special handling for __dict__ - return same object every time
        if (name == "__dict__")
        {
            if (_cachedDict == null)
            {
                // CPython 3.12: Python/ceval.c:2392 - frame->f_globals is module->md_dict
                // Use PyGlobalsDict which synchronizes bidirectionally with ModuleDict
                // This ensures dict.update() modifies ModuleDict, and LOAD_NAME sees the changes
                _cachedDict = new PyGlobalsDict(ModuleDict);
            }
            return _cachedDict;
        }

        if (ModuleDict.TryGetValue(name, out PyObject value))
            return value;

        throw PyAttributeError.Create($"module '{Name}' has no attribute '{name}'");
    }
    
    public override void SetAttribute(string name, PyObject value)
    {
        ModuleDict[name] = value;
    }
    
    // Variable 접근 (LEGB의 G 스코프)
    public PyObject GetVariable(string name)
    {
        return ModuleDict.TryGetValue(name, out PyObject value) ? value : null;
    }
    
    public void SetVariable(string name, PyObject value)
    {
        ModuleDict[name] = value;
    }
    
    // 모듈 실행 (소스 코드 실행) - 전체 Python 인터프리터 파이프라인 사용
    public void Execute(string sourceCode)
    {
#if DEBUG_MODULE_LOG
        Console.WriteLine($"📄 모듈 '{Name}' 실행 중...");
#endif

        try
        {
            PyCodeObject codeObject;

            // 캐시 확인 — 유효하면 Parse+Compile 스킵
            string cachePath = null;
            if (FileName != null && PyImportSystem.CacheEnabled)
            {
                cachePath = SharpPyCache.GetCachePath(FileName);
                if (SharpPyCache.IsCacheValid(cachePath, FileName))
                {
                    PyImportSystem.EmitImportEvent(ImportPhase.CacheHit, Name, FileName);
                    codeObject = SharpPyCache.ReadCache(cachePath);
                    goto executeCode;
                }
            }

            {
                // 1단계: 파싱 (소스 → AST)
                PyImportSystem.EmitImportEvent(ImportPhase.Parsing, Name, FileName);
                var tokens = SharpPy.Generated.PyParserRuntime.LexerSource(sourceCode);
                var statements = SharpPy.Generated.PyParserRuntime.ParseSource(tokens, sourceCode, FileName);

                // 2단계: 컴파일 (AST → 바이트코드)
                // CPython 3.12 호환: 모듈 코드 객체 이름은 항상 "<module>"
                PyImportSystem.EmitImportEvent(ImportPhase.Compiling, Name, FileName);
                var compiler = new PythonCompiler();
                codeObject = compiler.Compile(statements, "<module>", new List<string>(), FileName);

                // 캐시 저장 (다음 로드 시 Parse+Compile 스킵)
                if (cachePath != null)
                {
                    try { SharpPyCache.WriteCache(cachePath, codeObject, FileName); } catch { }
                }
            }

        executeCode:
            // 3단계: 모듈 전용 글로벌 스코프 생성
            var moduleGlobalScope = CreateModuleGlobalScope();

            // 4단계: VM 실행 (모듈 네임스페이스에서 실행)
            PyImportSystem.EmitImportEvent(ImportPhase.Executing, Name, FileName);
            var vm = PyVM.Instance;
            vm.ExecuteModule(codeObject, moduleGlobalScope);

            // CPython 3.12 호환: 모듈 딕셔너리가 직접 사용되므로 별도 업데이트 불필요

            IsInitialized = true;
#if DEBUG_MODULE_LOG
            Console.WriteLine($"✅ 모듈 '{Name}' 초기화 완료");
#endif
        }
        catch (PySyntaxErrorException)
        {
            // SyntaxError는 그대로 throw (line number 정보 보존)
            throw;
        }
        catch (System.Exception ex)
        {
#if DEBUG_MODULE_LOG
            Console.WriteLine($"❌ 모듈 '{Name}' 실행 실패: {ex.Message}");
#endif
            throw PyImportError.Create($"Failed to execute module '{Name}': {ex.Message}");
        }
    }

    /// <summary>
    /// 모듈 전용 글로벌 스코프 생성 (모듈의 __dict__를 직접 글로벌 스코프로 사용)
    /// </summary>
    private PyScopeChain CreateModuleGlobalScope()
    {
        // CPython 3.12 호환: 모듈 딕셔너리를 직접 글로벌 스코프로 사용
        // CPython 3.12: Python/ceval.c - frame->f_globals references module->md_dict
        // Pass module reference so globals() can return module.__dict__
        return new PyScopeChain(ModuleDict, Name, this);
    }

    private void ExecuteLine(string line)
    {
        if (line.StartsWith("def "))
        {
            var funcName = line.Substring(4).Split('(')[0].Trim();
            var func = new PyFunction(funcName, null, this);
            SetAttribute(funcName, func);
#if DEBUG_MODULE_LOG
            Console.WriteLine($"  정의됨: 함수 {funcName}");
#endif
        }
        else if (line.Contains(" = "))
        {
            var parts = line.Split('=', 2);
            var varName = parts[0].Trim();
            var valueStr = parts[1].Trim().TrimQuotes();

            PyObject value;
            if (int.TryParse(valueStr, out int intVal))
                value = new PyInt(intVal);
            else
                value = new PyStr(valueStr);

            SetAttribute(varName, value);
#if DEBUG_MODULE_LOG
            Console.WriteLine($"  정의됨: 변수 {varName} = {value}");
#endif
        }
        else if (line.StartsWith("__all__ = "))
        {
            var allStr = line.Substring(10).Trim();
            if (allStr.StartsWith("[") && allStr.EndsWith("]"))
            {
                // Performance: Eliminated LINQ - manual parsing instead of Select + Where
                var parts = allStr.Substring(1, allStr.Length - 2).Split(',');
                All.Clear();
                for (int i = 0; i < parts.Length; i++)
                {
                    var trimmed = parts[i].Trim().TrimQuotes();
                    if (!string.IsNullOrEmpty(trimmed))
                    {
                        All.Add(trimmed);
                    }
                }
#if DEBUG_MODULE_LOG
                Console.WriteLine($"  정의됨: __all__ = [{string.Join(", ", All)}]");
#endif
            }
        }
    }
    
    public override string ToString() => $"<module '{Name}' from '{FileName}'>";
    
    /// <summary>
    /// 모듈에 함수 추가 (표준 라이브러리 모듈용)
    /// </summary>
    protected void AddFunction(string name, Func<PyObject[], PyObject> implementation)
    {
        ModuleDict[name] = new PyBuiltinFunction(name, implementation);
    }
    
    /// <summary>
    /// 모듈에 클래스 추가 (표준 라이브러리 모듈용)
    /// </summary>
    protected void AddClass(string name, Func<PyType> typeFactory)
    {
        ModuleDict[name] = typeFactory();
    }
}

    // Python import 시스템
    public class PyImportSystem
    {
        // ─── Import Monitoring ───
        // 게임 로딩 화면 등에서 import 진행 상태를 모니터링
        // 구독: PyImportSystem.OnImportEvent += (e) => { label.Text = e.ModuleName; };
        // 해제: PyImportSystem.OnImportEvent -= handler;
        public static event Action<ImportEvent> OnImportEvent;

        // ─── Bytecode Cache ───
        // .spyc 캐시 활성화 여부 (기본 활성화)
        // Parse+Compile 스킵하여 import 속도 ~50% 향상
        public static bool CacheEnabled { get; set; } = true;

        /// <summary>이벤트 발행 (구독자가 없으면 no-op)</summary>
        private static void EmitEvent(ImportPhase phase, string moduleName, string filePath = null)
        {
            OnImportEvent?.Invoke(new ImportEvent(phase, moduleName, filePath));
        }

        /// <summary>PyModule.Execute 등 외부에서 호출 가능한 이벤트 발행</summary>
        internal static void EmitImportEvent(ImportPhase phase, string moduleName, string filePath = null)
        {
            EmitEvent(phase, moduleName, filePath);
        }

        // ─── Path Search Cache ───
        // 모듈명 → 파일 경로 캐시 (반복 FileExists/DirExists 호출 제거)
        // key: moduleName, value: (filePath, packageDir or null)
        private static readonly Dictionary<string, (string filePath, string packageDir)> _pathCache
            = new Dictionary<string, (string, string)>();

        /// <summary>Path 캐시 초기화 (테스트 또는 sys.path 변경 시)</summary>
        public static void ClearPathCache()
        {
            _pathCache.Clear();
        }

        // CPython 3.12: Python/import.c:185 - PyDict_New()
        // sys.modules 캐시 - CPython과 동일하게 PyDict 사용
        public static PyDict SysModules { get; } = new PyDict();

        // CPython 3.12: Helper methods for SysModules access with string keys
        // CPython uses PyUnicode_FromString + PyDict_GetItem pattern
        public static bool TryGetModule(string name, out PyModule module)
        {
            var key = new PyStr(name);
            if (SysModules.InternalDict.TryGetValue(key, out var value) && value is PyModule pyModule)
            {
                module = pyModule;
                return true;
            }
            module = null;
            return false;
        }

        public static void SetModule(string name, PyModule module)
        {
            // CPython 3.12: Python/import.c:630 - PyDict_SetItemString
            SysModules.SetItem(new PyStr(name), module);
        }

        public static void RemoveModule(string name)
        {
            // CPython 3.12: Python/import.c - PyDict_DelItemString
            var key = new PyStr(name);
            SysModules.InternalDict.Remove(key);
        }

        public static bool ContainsModule(string name)
        {
            var key = new PyStr(name);
            return SysModules.InternalDict.ContainsKey(key);
        }

        // C# 구현 모듈들 (CPython 3.12 C 확장 모듈만)
        private static Dictionary<string, Func<PyModule>> _builtinModules = new Dictionary<string, Func<PyModule>>
        {
            // CPython 3.12 Built-in 모듈
            ["builtins"] = () => SharpPy.Modules.BuiltinsModule.CreateBuiltinsModule(),

            // CPython 3.12 C 확장 모듈 (Python 모듈의 백엔드)
            ["_operator"] = () => SharpPy.Modules._OperatorModule.CreateOperatorModule(),  // operator.py가 사용
            ["_abc"] = () => SharpPy.Modules.AbcModule.CreateAbcModule(),  // abc.py가 사용

            // CPython 3.12 Built-in C 모듈 (성능 중요)
            ["math"] = () => SharpPy.Modules.MathModule.CreateMathModule(),
            ["time"] = () => new TimeModule(),
            ["itertools"] = () => ItertoolsModule.Instance,
            ["_collections"] = () => SharpPy.Modules._CollectionsModule.CreateCollectionsModule(),
            ["_functools"] = () => SharpPy.Modules._FunctoolsModule.CreateFunctoolsModule(),
            ["statistics"] = () => SharpPy.Modules.StatisticsModule.CreateStatisticsModule(),

            // CPython 3.12 C 확장 모듈 (Python 모듈의 백엔드)
            ["_random"] = () => SharpPy.Modules.RandomModule.CreateRandomModule(),  // random.py가 사용
            ["_codecs"] = () => SharpPy.Modules._CodecsModule.CreateCodecsModule(),  // codecs.py가 사용

            // OS 인터페이스 C 모듈 (cross-platform)
            ["nt"] = () => SharpPy.Modules.NtModule.CreateNtModule(),  // os.py가 사용 (Windows/Linux/Mac)
            ["posix"] = () => SharpPy.Modules.NtModule.CreateNtModule(),  // os.py가 사용 (alias)

            // 시스템 인터페이스 Built-in 모듈
            ["sys"] = () => SharpPy.Modules.SysModule.CreateSysModule(),

            // CPython 3.12 C 확장 모듈 (Python 모듈의 백엔드)
            ["_datetime"] = () => SharpPy.Modules.Stdlib.DatetimeModule.CreateDatetimeModule(),  // datetime.py가 사용

            // CPython 3.12: 다음 모듈들은 순수 Python으로 Lib/에서 로드됨:
            // - types (Lib/types.py)
            // - random (Lib/random.py + _random C# 모듈)
            // - os (Lib/os.py + nt C# 모듈)
            // - datetime (Lib/datetime.py + _datetime C# 모듈)

            // TODO: CPython 호환을 위해 Python으로 전환 필요:
            // - urllib → Lib/urllib/ Python 모듈로 전환 (완료)
            // - asyncio → Lib/asyncio/ Python 모듈로 전환
            ["_sre"] = () => SharpPy.Modules._SreModule.CreateSreModule(),  // re.py가 사용
            // urllib는 Lib/urllib/ 디렉토리의 Python 모듈로 전환됨 (CPython 3.12 호환)
            ["asyncio"] = () => CreateAsyncioModule()

            // 주석: 다음 모듈들은 순수 Python 모듈로 Lib/ 디렉토리에서 로드됨:
            // functools, contextlib, typing, abc, collections, json, traceback, pathlib
        };


        // import module_name (dotted import 지원)
        /// <summary>
        /// CPython 3.12 compatible import
        /// Implements: __import__(name, globals, locals, fromlist, level)
        /// </summary>
        public static PyModule Import(string moduleName, int level = 0, string[] fromlist = null, Dictionary<string, PyObject> globals = null)
        {
            // CPython 3.12: level parameter for relative imports
            // level=0: absolute import (default)
            // level>0: relative import (1=., 2=.., etc)

            // CPython 3.12: Use globals dict to resolve relative imports
            if (level > 0)
            {
                // Resolve relative import using globals['__package__'] or globals['__name__']
                moduleName = ResolveRelativeImport(moduleName, level, globals);
            }

            // CPython 3.12: fromlist parameter affects what is returned
            // If fromlist is empty/null AND module name has dots, return top-level package
            // If fromlist has items, return the actual module with those attributes
            bool hasFrom = fromlist != null && fromlist.Length > 0;

            if (!hasFrom && moduleName.Contains('.'))
            {
                // CPython behavior: import collections.abc → return collections (not collections.abc)
                // First, load the full module path (ensures all submodules are loaded)
                var fullModule = Import(moduleName);

                // Then return only the top-level package
                // CPython 3.12: Python/import.c:2905 — return the top-level package
                // If the top-level module was removed from sys.modules (e.g., due to a prior
                // import failure), re-import it to ensure it's available.
                var firstPart = moduleName.Split('.')[0];
                if (TryGetModule(firstPart, out var topModule))
                    return topModule;
                // Top-level package missing from sys.modules — re-import it
                return Import(firstPart);
            }

            // Load the main module
            var module = Import(moduleName);

            // CPython 3.12: Python/import.c:2938-2947 (_handle_fromlist)
            // IMPORTANT: Even if module was cached, we must still handle fromlist!
            // The fromlist items (submodules) need to be imported regardless of
            // whether the parent module was newly loaded or from cache.
            if (hasFrom)
            {
                HandleFromList(module, fromlist);
            }

            return module;
        }

        public static PyModule Import(string moduleName)
        {
            // CPython 3.12: Python/import.c:1673 - Check sys.modules cache first
            if (TryGetModule(moduleName, out PyModule cachedModule))
            {
                EmitEvent(ImportPhase.CacheHit, moduleName);
                return cachedModule;
            }

            // 2. 점으로 구분된 모듈명 처리 (예: package.submodule)
            if (moduleName.Contains('.'))
            {
                return ImportDottedModule(moduleName);
            }

            // CPython 3.12: Python/import.c:2158 - Check builtin modules
            if (_builtinModules.TryGetValue(moduleName, out Func<PyModule> moduleFactory))
            {
                EmitEvent(ImportPhase.BuiltinInit, moduleName);
                var module = moduleFactory();
                SetModule(moduleName, module);
                EmitEvent(ImportPhase.Done, moduleName);
                return module;
            }

            // 4. sys.path를 사용한 파일 시스템 검색
            EmitEvent(ImportPhase.Searching, moduleName);
            var foundModule = SearchModuleInPath(moduleName);
            if (foundModule != null)
            {
                return foundModule;
            }

            // 5. 모듈을 찾을 수 없음
            throw PyModuleNotFoundError.Create($"No module named '{moduleName}'");
        }
        
        /// <summary>
        /// 점으로 구분된 모듈 import (예: package.submodule)
        /// </summary>
        private static PyModule ImportDottedModule(string dottedName)
        {
            var parts = dottedName.Split('.');
            PyModule currentModule = null;
            string currentPath = "";
            
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                currentPath = i == 0 ? part : $"{currentPath}.{part}";
                
                // CPython 3.12: Check sys.modules cache for each step
                if (TryGetModule(currentPath, out PyModule existingModule))
                {
                    currentModule = existingModule;
                    continue;
                }
                
                // 서브모듈 검색
                PyModule subModule;
                if (i == 0)
                {
                    // 최상위 패키지
                    subModule = SearchModuleInPath(part);
                }
                else
                {
                    // 서브모듈 - 부모 패키지 내에서 검색
                    subModule = SearchSubmodule(currentModule, part, currentPath);
                }
                
                if (subModule == null)
                {
                    throw PyModuleNotFoundError.Create($"No module named '{currentPath}'");
                }
                
                // CPython 3.12: Register in sys.modules
                SetModule(currentPath, subModule);
                
                // 부모 모듈에 서브모듈 attribute 설정
                if (currentModule != null)
                {
                    currentModule.SetAttribute(part, subModule);
                }
                
                currentModule = subModule;
            }
            
            return currentModule;
        }
        
        /// <summary>
        /// 부모 패키지 내에서 서브모듈 검색
        /// </summary>
        private static PyModule SearchSubmodule(PyModule parentModule, string submoduleName, string fullName)
        {
            if (parentModule == null) return null;
            
            // 네임스페이스 패키지인 경우 특별 처리
            if (parentModule is PyNamespaceModule namespacePackage)
            {
                return namespacePackage.FindSubmodule(submoduleName);
            }
            
            // 일반 패키지의 경우 기존 로직
            var parentDir = IOHelper.GetDirectoryName(parentModule.FileName);
            if (string.IsNullOrEmpty(parentDir)) return null;

            // 서브모듈 파일 검색
            var submoduleFile = IOHelper.CombinePath(parentDir, submoduleName + ".py");
            if (IOHelper.FileExists(submoduleFile))
            {
                return LoadModuleFromFile(fullName, submoduleFile);
            }

            // 서브패키지 검색
            var subpackageDir = IOHelper.CombinePath(parentDir, submoduleName);
            var subpackageInit = IOHelper.CombinePath(subpackageDir, "__init__.py");
            if (IOHelper.DirExists(subpackageDir) && IOHelper.FileExists(subpackageInit))
            {
                // CPython 3.12: Pass package directory to LoadModuleFromFile
                // __path__ will be set BEFORE executing the module code
                var module = LoadModuleFromFile(fullName, subpackageInit, subpackageDir);
                return module;
            }

            // PEP 420: 네임스페이스 서브패키지 검색 (__init__.py 없는 디렉토리)
            if (IOHelper.DirExists(subpackageDir) && !IOHelper.FileExists(subpackageInit))
            {
                var namespaceDirs = new List<string> { subpackageDir };
                return CreateNamespacePackage(fullName, namespaceDirs);
            }
            
            return null;
        }

        // sys.path에서 모듈 검색
        private static PyModule SearchModuleInPath(string moduleName)
        {
            // Path 캐시 히트 — 이전에 찾은 경로로 바로 로드
            if (_pathCache.TryGetValue(moduleName, out var cached))
            {
                return LoadModuleFromFile(moduleName, cached.filePath, cached.packageDir);
            }

            var sysPath = GetSysPath();
            if (sysPath == null) return null;

            List<string> namespaceDirs = new List<string>(); // PEP 420 네임스페이스 패키지용

            foreach (var pathObj in sysPath.Items)
            {
                if (!(pathObj is PyStr pathStr)) continue;
                var searchPath = pathStr.Value;

                // .py 파일 검색
                var pyFile = IOHelper.CombinePath(searchPath, moduleName + ".py");
                if (IOHelper.FileExists(pyFile))
                {
                    _pathCache[moduleName] = (pyFile, null);
                    return LoadModuleFromFile(moduleName, pyFile);
                }

                // 패키지 디렉토리 검색 (moduleName/__init__.py)
                var packageDir = IOHelper.CombinePath(searchPath, moduleName);
                var initFile = IOHelper.CombinePath(packageDir, "__init__.py");
                if (IOHelper.DirExists(packageDir) && IOHelper.FileExists(initFile))
                {
                    // CPython 3.12: Pass package directory to LoadModuleFromFile
                    // __path__ will be set BEFORE executing the module code
                    _pathCache[moduleName] = (initFile, packageDir);
                    var module = LoadModuleFromFile(moduleName, initFile, packageDir);
                    return module;
                }

                // PEP 420: 네임스페이스 패키지 검색 (__init__.py 없는 디렉토리)
                if (IOHelper.DirExists(packageDir) && !IOHelper.FileExists(initFile))
                {
                    namespaceDirs.Add(packageDir);
                }
            }

            // PEP 420 네임스페이스 패키지 생성
            if (namespaceDirs.Count > 0)
            {
                return CreateNamespacePackage(moduleName, namespaceDirs);
            }

            return null;
        }

        // sys.path 가져오기
        private static PyList GetSysPath()
        {
            // CPython 3.12: Use sys.path if sys module is loaded
            if (TryGetModule("sys", out PyModule sysModule))
            {
                if (sysModule.ModuleDict.TryGetValue("path", out PyObject pathObj) && pathObj is PyList pathList)
                {
                    return pathList;
                }
            }

            // sys 모듈이 없으면 기본 경로 생성
            return CreateDefaultSysPath();
        }

        // 기본 sys.path 생성 (CPython 호환 구조)
        private static PyList CreateDefaultSysPath()
        {
            var pathList = new List<PyObject>();

            // 현재 디렉토리 (사용자 모듈)
            pathList.Add(new PyStr("."));

            // 실행 파일 디렉토리 기준 경로
            var exeDir = IOHelper.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";

            // 프로젝트 루트 디렉토리 (exe는 bin/Debug/net8.0/에 있으므로 3단계 위로)
            var projectRoot = IOHelper.GetFullPath(IOHelper.CombinePath(exeDir, "..", "..", ".."));

            // CPython 3.12: Lib 디렉토리만 사용 (stdlib 폴더 삭제됨)
            pathList.Add(new PyStr(IOHelper.CombinePath(projectRoot, "Lib")));

            // 3순위: modules 디렉토리 (SharpPy 전용 C# 구현 모듈) - 프로젝트 루트에서
            pathList.Add(new PyStr(IOHelper.CombinePath(projectRoot, "modules")));

            // 3순위: 실행 파일 디렉토리
            pathList.Add(new PyStr(exeDir));

            // Performance: Eliminated LINQ - direct array conversion
            return new PyList(pathList.ToArray());
        }

        // 파일에서 모듈 로드
        public static PyModule LoadModuleFromFile(string moduleName, string filePath, string packageDir = null)
        {
            try
            {
                EmitEvent(ImportPhase.Loading, moduleName, filePath);
                var sourceCode = IOHelper.ReadAllText(filePath);
                var module = new PyModule(moduleName, filePath);

                // CPython 3.12: Set __path__ BEFORE executing module code (if it's a package)
                // importlib/_bootstrap.py:782-788 - Sets module.__path__ in _init_module_attrs
                // This must happen before exec_module() so that the module code can see __path__
                if (packageDir != null)
                {
                    module.ModuleDict["__path__"] = new PyList(new[] { new PyStr(packageDir) });
                }

                // CPython 3.12: Register in sys.modules (prevents circular import)
                SetModule(moduleName, module);

                // 모듈 실행 (초기화) — Execute 내부에서 Parsing/Compiling/Executing 이벤트 발생
                module.Execute(sourceCode);

                // CPython 3.12: importlib/_bootstrap.py — 모듈 코드 실행 후 sys.modules에서
                // 다시 조회. 모듈 코드가 sys.modules[__name__]을 교체했을 수 있음.
                // (예: sys.modules[__name__] = other_module)
                PyModule finalModule;
                if (!TryGetModule(moduleName, out finalModule) || finalModule == null)
                    finalModule = module;

                EmitEvent(ImportPhase.Done, moduleName, filePath);
#if DEBUG_MODULE_LOG
                Console.WriteLine($"📦 모듈 '{moduleName}' 파일에서 로드됨: {filePath}");
#endif
                return finalModule;
            }
            catch (PySyntaxErrorException)
            {
                // SyntaxError는 그대로 throw (line number 정보 보존)
                RemoveModule(moduleName);
                throw;
            }
            catch (System.Exception ex)
            {
                // CPython 3.12: Remove from sys.modules on load failure
                RemoveModule(moduleName);
                throw PyImportError.Create($"Failed to load module '{moduleName}' from '{filePath}': {ex.Message}");
            }
        }

        /// <summary>
        /// PEP 420 네임스페이스 패키지 생성
        /// </summary>
        public static PyModule CreateNamespacePackage(string moduleName, List<string> namespaceDirs)
        {
            var namespaceModule = new PyNamespaceModule(moduleName, namespaceDirs);

            // CPython 3.12: Register namespace package in sys.modules
            SetModule(moduleName, namespaceModule);

#if DEBUG_MODULE_LOG
            Console.WriteLine($"📂 네임스페이스 패키지 '{moduleName}' 생성됨: [{string.Join(", ", namespaceDirs)}]");
#endif
            return namespaceModule;
        }


        /// <summary>
        /// <summary>
        /// CPython 3.12: Handle fromlist items by ensuring they are loaded as submodules
        /// Python/import.c:2940-2948 - Calls _handle_fromlist
        /// importlib/_bootstrap.py:1020-1040 - _handle_fromlist implementation
        /// </summary>
        private static void HandleFromList(PyModule module, string[] fromlist)
        {
            if (fromlist == null || fromlist.Length == 0)
            {
                return;
            }

            // CPython 3.12: Check if this is a package (has __path__)
            bool hasPath = module.ModuleDict.ContainsKey("__path__");
            if (!hasPath)
            {
                // Not a package, fromlist items should already be attributes
                return;
            }

            string packageName = module.Name;

            foreach (var itemName in fromlist)
            {
                // CPython 3.12: Skip special names like '*'
                if (itemName == "*")
                    continue;

                // Check if the attribute already exists in the module
                if (module.ModuleDict.ContainsKey(itemName))
                {
                    continue;
                }

                // Try to import as submodule: package.item
                // CPython 3.12: This is done by calling __import__(package.item)
                string fullName = $"{packageName}.{itemName}";
                try
                {
                    var subModule = Import(fullName);
                    // Add to parent module's namespace
                    // CPython 3.12: This is done automatically by the import system
                    // but we need to ensure it's in the module dict
                    module.ModuleDict[itemName] = subModule;
                }
                catch (Exception ex)
                {
                    // If import fails, the item might be a regular attribute
                    // CPython 3.12: Silently ignore if it's not a submodule
                    // The IMPORT_FROM instruction will handle the error
                    //
                    // Store the exception so IMPORT_FROM can include root cause in error message
                    module.ModuleDict[$"__import_error:{itemName}"] = new PyStr(ex.Message);
                }
            }
        }

        /// 상대 import 경로 해석 (.module, ..module 등)
        /// </summary>
        /// <summary>
        /// CPython 3.12 compatible relative import resolution
        /// Based on CPython's resolve_name() in Python/import.c
        /// </summary>
        private static string ResolveRelativeImport(string moduleName, int level, Dictionary<string, PyObject> globals)
        {
            if (globals == null)
            {
                throw PyImportError.Create("attempted relative import with no known parent package");
            }

            // CPython 3.12 resolution algorithm:
            // 1. Try __package__
            string package = null;
            if (globals.TryGetValue("__package__", out PyObject pkgObj) &&
                pkgObj != PyNone.Instance && pkgObj is PyStr pkgStr)
            {
                package = pkgStr.Value;
            }

            // 2. Try __spec__.parent (TODO: implement when PyModuleSpec is added)
            if (string.IsNullOrEmpty(package) &&
                globals.TryGetValue("__spec__", out PyObject specObj) &&
                specObj != PyNone.Instance)
            {
                // TODO: Access spec.parent when PyModuleSpec is implemented
                // For now, skip this step
            }

            // 3. Fallback: derive from __name__ and __path__
            if (string.IsNullOrEmpty(package))
            {
                if (!globals.TryGetValue("__name__", out PyObject nameObj) ||
                    !(nameObj is PyStr nameStr))
                {
                    throw PyImportError.Create("'__name__' not in globals or not a string");
                }

                package = nameStr.Value;

                // If module (not package), strip last component
                if (!globals.ContainsKey("__path__"))
                {
                    int lastDot = package.LastIndexOf('.');
                    if (lastDot >= 0)
                    {
                        package = package.Substring(0, lastDot);
                    }
                    else
                    {
                        // Top-level module cannot do relative import
                        package = "";
                    }
                }
            }

            if (string.IsNullOrEmpty(package))
            {
                throw PyImportError.Create("attempted relative import with no known parent package");
            }

            // 4. Walk up 'level - 1' times
            string[] parts = package.Split('.');
            int levelsUp = level - 1;

            if (levelsUp >= parts.Length)
            {
                throw PyImportError.Create("attempted relative import beyond top-level package");
            }

            // Performance: Eliminated LINQ - manual array copy instead of Take + ToArray
            int targetLength = parts.Length - levelsUp;
            string[] targetParts = new string[targetLength];
            for (int i = 0; i < targetLength; i++)
            {
                targetParts[i] = parts[i];
            }
            string targetPackage = string.Join(".", targetParts);

            // 5. Combine with relative name
            if (string.IsNullOrEmpty(moduleName))
            {
                return targetPackage;  // from .. import something
            }
            else
            {
                return $"{targetPackage}.{moduleName}";  // from ..module import something
            }
        }

        private static Dictionary<string, PyObject> ImportAll(PyModule module)
        {
            var result = new Dictionary<string, PyObject>();

            if (module.All.Count > 0)
            {
                foreach (var itemName in module.All)
                {
                    if (module.ModuleDict.TryGetValue(itemName, out PyObject item))
                    {
                        result[itemName] = item;
                    }
                }
            }
            else
            {
                foreach (var kvp in module.ModuleDict)
                {
                    if (!kvp.Key.StartsWith("_"))
                    {
                        result[kvp.Key] = kvp.Value;
                    }
                }
            }

            return result;
        }

        // abc and contextlib modules are now loaded as .py files from Lib/ directory

        // asyncio 모듈 생성
        private static PyModule CreateAsyncioModule()
        {
            var module = new PyModule("asyncio", "<asyncio module>");

            // Core asyncio functions - for now without kwargs support
            // TODO: Add proper kwargs support later
            module.ModuleDict["run"] = new PyBuiltinFunction("run", (args) =>
            {
                return SharpPy.Modules.AsyncioModule.Run(PyVM.Instance, args, null);
            });

            module.ModuleDict["sleep"] = new PyBuiltinFunction("sleep", (args) =>
            {
                return SharpPy.Modules.AsyncioModule.Sleep(PyVM.Instance, args, null);
            });

            module.ModuleDict["create_task"] = new PyBuiltinFunction("create_task", (args) =>
            {
                return SharpPy.Modules.AsyncioModule.CreateTask(PyVM.Instance, args, null);
            });

            module.ModuleDict["get_event_loop"] = new PyBuiltinFunction("get_event_loop", (args) =>
            {
                return SharpPy.Modules.AsyncioModule.GetEventLoop(PyVM.Instance, args, null);
            });

            return module;
        }
    }
#endregion
}