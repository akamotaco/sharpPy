using System.Linq;
using SharpPy.Utils;

namespace SharpPy
{
    #region Module and Import System

// PEP 420 네임스페이스 패키지 (PyModule을 상속받아 확장)
public class PyNamespaceModule : PyModule
{
    public List<string> NamespaceDirs { get; }
    
    public PyNamespaceModule(string name, List<string> namespaceDirs) : base(name)
    {
        NamespaceDirs = new List<string>(namespaceDirs);
        
        // __path__ 속성 설정 (PEP 420 요구사항)
        var pathList = namespaceDirs.Select(dir => new PyString(dir)).ToArray();
        ModuleDict["__path__"] = new PyList(pathList);
        
        // 네임스페이스 패키지 표시
        ModuleDict["__file__"] = PyNone.Instance; // 네임스페이스 패키지는 __file__이 None
        ModuleDict["__doc__"] = new PyString($"Namespace package {Name}");
    }
    
    /// <summary>
    /// 네임스페이스 패키지 내에서 서브모듈 검색
    /// </summary>
    public PyModule FindSubmodule(string submoduleName)
    {
        foreach (var namespaceDir in NamespaceDirs)
        {
            // 서브모듈 파일 검색
            var submoduleFile = System.IO.Path.Combine(namespaceDir, submoduleName + ".py");
            if (System.IO.File.Exists(submoduleFile))
            {
                var fullName = $"{Name}.{submoduleName}";
                return PyImportSystem.LoadModuleFromFile(fullName, submoduleFile);
            }
            
            // 서브패키지 검색
            var subpackageDir = System.IO.Path.Combine(namespaceDir, submoduleName);
            if (System.IO.Directory.Exists(subpackageDir))
            {
                var initFile = System.IO.Path.Combine(subpackageDir, "__init__.py");
                var fullName = $"{Name}.{submoduleName}";
                
                if (System.IO.File.Exists(initFile))
                {
                    // 일반 패키지
                    return PyImportSystem.LoadModuleFromFile(fullName, initFile);
                }
                else
                {
                    // 중첩된 네임스페이스 패키지
                    var nestedNamespaceDirs = new List<string> { subpackageDir };
                    
                    // 다른 네임스페이스 디렉토리에서도 동일한 서브패키지 검색
                    foreach (var otherDir in NamespaceDirs.Where(d => d != namespaceDir))
                    {
                        var otherSubpackageDir = System.IO.Path.Combine(otherDir, submoduleName);
                        if (System.IO.Directory.Exists(otherSubpackageDir) && 
                            !System.IO.File.Exists(System.IO.Path.Combine(otherSubpackageDir, "__init__.py")))
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
        ModuleDict["__name__"] = new PyString(Name);
        ModuleDict["__file__"] = new PyString(FileName);
        ModuleDict["__doc__"] = new PyString($"Module {Name}");

        // CPython 3.12: Set __package__
        // For packages (__init__.py): __package__ = __name__
        // For modules: __package__ = parent package name (or "" for top-level)
        bool isPackage = FileName != null && FileName.EndsWith("__init__.py");

        if (isPackage)
        {
            // Package: __package__ == __name__
            ModuleDict["__package__"] = new PyString(Name);
        }
        else
        {
            // Module: __package__ is parent package
            int lastDot = Name.LastIndexOf('.');
            string packageName = lastDot >= 0 ? Name.Substring(0, lastDot) : "";
            ModuleDict["__package__"] = string.IsNullOrEmpty(packageName)
                ? PyNone.Instance
                : new PyString(packageName);
        }
    }
    
    public override PyType GetPyType() => PyType.ModuleType;
    public override string GetTypeName() => "module";
    
    // 모듈 attribute 접근 (Variable == Attribute)
    public override PyObject GetAttribute(string name)
    {
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
        Console.WriteLine($"📄 모듈 '{Name}' 실행 중...");

        try
        {
            // 1단계: 파싱 (소스 → AST)
            var tokens = SharpPy.Generated.PyParserRuntime.LexerSource(sourceCode);

            var statements = SharpPy.Generated.PyParserRuntime.ParseSource(tokens, sourceCode, FileName);

            // 2단계: 컴파일 (AST → 바이트코드)
            // CPython 3.12 호환: 모듈 코드 객체 이름은 항상 "<module>"
            var compiler = new PythonCompiler();
            var codeObject = compiler.Compile(statements, "<module>", new List<string>(), FileName);

            // 3단계: 모듈 전용 글로벌 스코프 생성
            var moduleGlobalScope = CreateModuleGlobalScope();

            // 4단계: VM 실행 (모듈 네임스페이스에서 실행)
            var vm = PyVM.Instance;
            vm.ExecuteModule(codeObject, moduleGlobalScope);

            // CPython 3.12 호환: 모듈 딕셔너리가 직접 사용되므로 별도 업데이트 불필요

            IsInitialized = true;
            Console.WriteLine($"✅ 모듈 '{Name}' 초기화 완료");
        }
        catch (PySyntaxErrorException)
        {
            // SyntaxError는 그대로 throw (line number 정보 보존)
            throw;
        }
        catch (System.Exception ex)
        {
            Console.WriteLine($"❌ 모듈 '{Name}' 실행 실패: {ex.Message}");
            throw PyImportError.Create($"Failed to execute module '{Name}': {ex.Message}");
        }
    }

    /// <summary>
    /// 모듈 전용 글로벌 스코프 생성 (모듈의 __dict__를 직접 글로벌 스코프로 사용)
    /// </summary>
    private PyScopeChain CreateModuleGlobalScope()
    {
        // CPython 3.12 호환: 모듈 딕셔너리를 직접 글로벌 스코프로 사용
        return new PyScopeChain(ModuleDict, Name);
    }

    private void ExecuteLine(string line)
    {
        if (line.StartsWith("def "))
        {
            var funcName = line.Substring(4).Split('(')[0].Trim();
            var func = new PyFunction(funcName, null, this);
            SetAttribute(funcName, func);
            Console.WriteLine($"  정의됨: 함수 {funcName}");
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
                value = new PyString(valueStr);
            
            SetAttribute(varName, value);
            Console.WriteLine($"  정의됨: 변수 {varName} = {value}");
        }
        else if (line.StartsWith("__all__ = "))
        {
            var allStr = line.Substring(10).Trim();
            if (allStr.StartsWith("[") && allStr.EndsWith("]"))
            {
                var items = allStr.Substring(1, allStr.Length - 2)
                    .Split(',')
                    .Select(s => s.Trim().TrimQuotes())
                    .Where(s => !string.IsNullOrEmpty(s));
                
                All.Clear();
                All.AddRange(items);
                Console.WriteLine($"  정의됨: __all__ = [{string.Join(", ", All)}]");
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
        // sys.modules 캐시
        public static Dictionary<string, PyModule> SysModules { get; } = new Dictionary<string, PyModule>();

        // C# 구현 모듈들 (CPython 3.12 C 확장 모듈만)
        private static Dictionary<string, Func<PyModule>> _builtinModules = new Dictionary<string, Func<PyModule>>
        {
            // CPython 3.12 Built-in 모듈
            ["builtins"] = () => SharpPy.Modules.BuiltinsModule.CreateBuiltinsModule(),

            // CPython 3.12 C 확장 모듈 (Python 모듈의 백엔드)
            ["_operator"] = () => SharpPy.Modules._OperatorModule.CreateOperatorModule(),  // operator.py가 사용

            // CPython 3.12 Built-in C 모듈 (성능 중요)
            ["math"] = () => SharpPy.Modules.MathModule.CreateMathModule(),
            ["time"] = () => new TimeModule(),
            ["itertools"] = () => ItertoolsModule.Instance,
            ["_collections"] = () => SharpPy.Modules._CollectionsModule.CreateCollectionsModule(),
            ["_functools"] = () => SharpPy.Modules._FunctoolsModule.CreateFunctoolsModule(),
            ["statistics"] = () => SharpPy.Modules.StatisticsModule.CreateStatisticsModule(),

            // CPython 3.12 C 확장 모듈 (Python 모듈의 백엔드)
            ["_random"] = () => SharpPy.Modules.RandomModule.CreateRandomModule(),  // random.py가 사용

            // OS 인터페이스 C 모듈 (cross-platform)
            ["nt"] = () => SharpPy.Modules.NtModule.CreateNtModule(),  // os.py가 사용 (Windows/Linux/Mac)
            ["posix"] = () => SharpPy.Modules.NtModule.CreateNtModule(),  // os.py가 사용 (alias)

            // 시스템 인터페이스 Built-in 모듈
            ["sys"] = () => SharpPy.Modules.SysModule.CreateSysModule(),

            // CPython 3.12 C 확장 모듈 (Python 모듈의 백엔드)
            ["_datetime"] = () => SharpPy.Modules.Stdlib.DatetimeModule.CreateDatetimeModule(),  // datetime.py가 사용

            // CPython 3.12: 다음 모듈들은 순수 Python으로 stdlib/에서 로드됨:
            // - types (stdlib/types.py)
            // - random (stdlib/random.py + _random C# 모듈)
            // - os (stdlib/os.py + nt C# 모듈)
            // - datetime (stdlib/datetime.py + _datetime C# 모듈)

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
            // If fromlist is empty/null, return top-level package
            // If fromlist has items, return the actual module with those attributes

            return Import(moduleName);
        }

        public static PyModule Import(string moduleName)
        {
            // 1. sys.modules 캐시 확인
            if (SysModules.TryGetValue(moduleName, out PyModule cachedModule))
            {
                return cachedModule;
            }

            // 2. 점으로 구분된 모듈명 처리 (예: package.submodule)
            if (moduleName.Contains('.'))
            {
                return ImportDottedModule(moduleName);
            }

            // 3. 내장 모듈 확인
            if (_builtinModules.TryGetValue(moduleName, out Func<PyModule> moduleFactory))
            {
                var module = moduleFactory();
                SysModules[moduleName] = module;
                return module;
            }

            // 4. sys.path를 사용한 파일 시스템 검색
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
                
                // sys.modules에서 확인
                if (SysModules.TryGetValue(currentPath, out PyModule existingModule))
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
                
                // sys.modules에 등록
                SysModules[currentPath] = subModule;
                
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
            var parentDir = System.IO.Path.GetDirectoryName(parentModule.FileName);
            if (string.IsNullOrEmpty(parentDir)) return null;
            
            // 서브모듈 파일 검색
            var submoduleFile = System.IO.Path.Combine(parentDir, submoduleName + ".py");
            if (System.IO.File.Exists(submoduleFile))
            {
                return LoadModuleFromFile(fullName, submoduleFile);
            }
            
            // 서브패키지 검색
            var subpackageDir = System.IO.Path.Combine(parentDir, submoduleName);
            var subpackageInit = System.IO.Path.Combine(subpackageDir, "__init__.py");
            if (System.IO.Directory.Exists(subpackageDir) && System.IO.File.Exists(subpackageInit))
            {
                // CPython 3.12: Set __path__ for packages
                var module = LoadModuleFromFile(fullName, subpackageInit);
                module.ModuleDict["__path__"] = new PyList(new[] { new PyString(subpackageDir) });
                return module;
            }
            
            // PEP 420: 네임스페이스 서브패키지 검색 (__init__.py 없는 디렉토리)
            if (System.IO.Directory.Exists(subpackageDir) && !System.IO.File.Exists(subpackageInit))
            {
                var namespaceDirs = new List<string> { subpackageDir };
                return CreateNamespacePackage(fullName, namespaceDirs);
            }
            
            return null;
        }

        // sys.path에서 모듈 검색
        private static PyModule SearchModuleInPath(string moduleName)
        {
            var sysPath = GetSysPath();
            if (sysPath == null) return null;

            List<string> namespaceDirs = new List<string>(); // PEP 420 네임스페이스 패키지용

            foreach (var pathObj in sysPath.Items)
            {
                if (!(pathObj is PyString pathStr)) continue;
                var searchPath = pathStr.Value;

                // .py 파일 검색
                var pyFile = System.IO.Path.Combine(searchPath, moduleName + ".py");
                if (System.IO.File.Exists(pyFile))
                {
                    return LoadModuleFromFile(moduleName, pyFile);
                }

                // 패키지 디렉토리 검색 (moduleName/__init__.py)
                var packageDir = System.IO.Path.Combine(searchPath, moduleName);
                var initFile = System.IO.Path.Combine(packageDir, "__init__.py");
                if (System.IO.Directory.Exists(packageDir) && System.IO.File.Exists(initFile))
                {
                    // CPython 3.12: Pass package directory for __path__ attribute
                    var module = LoadModuleFromFile(moduleName, initFile);
                    module.ModuleDict["__path__"] = new PyList(new[] { new PyString(packageDir) });
                    return module;
                }

                // PEP 420: 네임스페이스 패키지 검색 (__init__.py 없는 디렉토리)
                if (System.IO.Directory.Exists(packageDir) && !System.IO.File.Exists(initFile))
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
            // sys 모듈이 이미 로드되어 있으면 그것의 path 사용
            if (SysModules.TryGetValue("sys", out PyModule sysModule))
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
            pathList.Add(new PyString("."));

            // 실행 파일 디렉토리 기준 경로
            var exeDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";

            // 프로젝트 루트 디렉토리 (exe는 bin/Debug/net8.0/에 있으므로 3단계 위로)
            var projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(exeDir, "..", "..", ".."));

            // 1순위: stdlib 디렉토리 (SharpPy 내장 Python 모듈) - 프로젝트 루트에서
            pathList.Add(new PyString(System.IO.Path.Combine(projectRoot, "stdlib")));

            // 2순위: Lib 디렉토리 (CPython 호환 표준 라이브러리) - 프로젝트 루트에서
            pathList.Add(new PyString(System.IO.Path.Combine(projectRoot, "Lib")));

            // 3순위: modules 디렉토리 (SharpPy 전용 C# 구현 모듈) - 프로젝트 루트에서
            pathList.Add(new PyString(System.IO.Path.Combine(projectRoot, "modules")));

            // 3순위: 실행 파일 디렉토리
            pathList.Add(new PyString(exeDir));

            return new PyList(pathList.ToArray());
        }

        // 파일에서 모듈 로드
        public static PyModule LoadModuleFromFile(string moduleName, string filePath)
        {
            try
            {
                var sourceCode = System.IO.File.ReadAllText(filePath);
                var module = new PyModule(moduleName, filePath);

                // sys.modules에 등록 (순환 import 방지)
                SysModules[moduleName] = module;

                // 모듈 실행 (초기화)
                module.Execute(sourceCode);

                Console.WriteLine($"📦 모듈 '{moduleName}' 파일에서 로드됨: {filePath}");
                return module;
            }
            catch (PySyntaxErrorException)
            {
                // SyntaxError는 그대로 throw (line number 정보 보존)
                SysModules.Remove(moduleName);
                throw;
            }
            catch (System.Exception ex)
            {
                // 로드 실패 시 sys.modules에서 제거
                SysModules.Remove(moduleName);
                throw PyImportError.Create($"Failed to load module '{moduleName}' from '{filePath}': {ex.Message}");
            }
        }

        /// <summary>
        /// PEP 420 네임스페이스 패키지 생성
        /// </summary>
        public static PyModule CreateNamespacePackage(string moduleName, List<string> namespaceDirs)
        {
            var namespaceModule = new PyNamespaceModule(moduleName, namespaceDirs);
            
            // sys.modules에 등록
            SysModules[moduleName] = namespaceModule;
            
            Console.WriteLine($"📂 네임스페이스 패키지 '{moduleName}' 생성됨: [{string.Join(", ", namespaceDirs)}]");
            return namespaceModule;
        }


        /// <summary>
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
                pkgObj != PyNone.Instance && pkgObj is PyString pkgStr)
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
                    !(nameObj is PyString nameStr))
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

            string[] targetParts = parts.Take(parts.Length - levelsUp).ToArray();
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