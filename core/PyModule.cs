using System.Linq;

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
    
    // 모듈 실행 (소스 코드 실행)
    public void Execute(string sourceCode)
    {
        Console.WriteLine($"📄 모듈 '{Name}' 실행 중...");
        
        var lines = sourceCode.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                continue;
                
            ExecuteLine(trimmed);
        }
        
        IsInitialized = true;
        Console.WriteLine($"✅ 모듈 '{Name}' 초기화 완료");
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
            var valueStr = parts[1].Trim().Trim('"', '\'');
            
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
                    .Select(s => s.Trim().Trim('"', '\''))
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

        // 내장 모듈들
        private static Dictionary<string, Func<PyModule>> _builtinModules = new Dictionary<string, Func<PyModule>>
        {
            ["math"] = () => SharpPy.Modules.MathModule.CreateMathModule(),
            ["random"] = () => SharpPy.Modules.RandomModule.CreateRandomModule(),
            ["sys"] = () => SharpPy.Modules.SysModule.CreateSysModule(),
            ["time"] = () => new TimeModule(),
            ["itertools"] = () => ItertoolsModule.Instance,
            ["functools"] = () => FunctoolsModule.Instance,
            ["collections"] = () => CollectionsModule.Instance,
            ["typing"] = () => TypingModule.Instance,
            ["os"] = () => SharpPy.Modules.Stdlib.OsModule.CreateOsModule(),
            ["pathlib"] = () => SharpPy.Modules.Stdlib.PathlibModule.CreatePathlibModule(),
            ["json"] = () => SharpPy.Modules.Stdlib.JsonModule.CreateJsonModule(),
            ["re"] = () => SharpPy.Modules.Stdlib.RegexModule.CreateRegexModule(),
            ["datetime"] = () => SharpPy.Modules.Stdlib.DatetimeModule.CreateDatetimeModule(),
            ["urllib"] = () => SharpPy.Modules.Stdlib.UrllibModule.CreateUrllibModule(),
            ["abc"] = () => CreateAbcModule(),
            ["contextlib"] = () => CreateContextlibModule(),
            ["traceback"] = () => SharpPy.Modules.Stdlib.TracebackModule.CreateModule()
        };


        // import module_name (dotted import 지원)
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
                return LoadModuleFromFile(fullName, subpackageInit);
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
                    return LoadModuleFromFile(moduleName, initFile);
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

        // 기본 sys.path 생성
        private static PyList CreateDefaultSysPath()
        {
            var pathList = new List<PyObject>();

            // 현재 디렉토리
            pathList.Add(new PyString("."));

            // 실행 파일 디렉토리
            var exeDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
            pathList.Add(new PyString(exeDir));

            // modules 디렉토리
            pathList.Add(new PyString(System.IO.Path.Combine(exeDir, "modules")));

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

        // from module_name import item1, item2
        public static Dictionary<string, PyObject> FromImport(string moduleName, params string[] itemNames)
        {
            // 상대 import 처리
            string resolvedModuleName = ResolveRelativeImport(moduleName);
            var module = PyImportSystem.Import(resolvedModuleName);
            var result = new Dictionary<string, PyObject>();

            foreach (var itemName in itemNames)
            {
                if (itemName == "*")
                {
                    return ImportAll(module);
                }

                try
                {
                    var item = module.GetAttribute(itemName);
                    result[itemName] = item;
                }
                catch (PythonException pe)
                {
                    var pae = (PyAttributeError)pe.PyException;
                    throw PyImportError.Create($"cannot import name '{itemName}' from '{moduleName}'");
                }
            }

            return result;
        }

        /// <summary>
        /// 상대 import 경로 해석 (.module, ..module 등)
        /// </summary>
        private static string ResolveRelativeImport(string moduleName)
        {
            if (!moduleName.StartsWith("."))
                return moduleName; // 절대 import
            
            // 현재 패키지 컨텍스트 가져오기
            string currentPackage = GetCurrentPackage();
            
            // 상대 import 레벨 계산
            int level = 0;
            while (level < moduleName.Length && moduleName[level] == '.')
                level++;
            
            string relativeModule = moduleName.Substring(level);
            
            if (string.IsNullOrEmpty(currentPackage))
            {
                throw PyImportError.Create("attempted relative import with no known parent package");
            }
            
            // 패키지 경로를 레벨만큼 올라가기
            string[] packageParts = currentPackage.Split('.');
            if (level - 1 > packageParts.Length)
            {
                throw PyImportError.Create("attempted relative import beyond top-level package");
            }
            
            string[] targetParts = packageParts.Take(packageParts.Length - (level - 1)).ToArray();
            string targetPackage = string.Join(".", targetParts);
            
            if (string.IsNullOrEmpty(relativeModule))
                return targetPackage; // from .. import something
            else
                return $"{targetPackage}.{relativeModule}"; // from ..module import something
        }
        
        /// <summary>
        /// 현재 실행 중인 패키지 컨텍스트 가져오기 (간단한 구현)
        /// </summary>
        private static string GetCurrentPackage()
        {
            // 현재는 간단하게 구현. 실제로는 execution context에서 가져와야 함
            return "testpackage"; // TODO: 실제 패키지 컨텍스트 구현
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

        // abc 모듈 생성
        private static PyModule CreateAbcModule()
        {
            var module = new PyModule("abc", "<abc module>");
            
            // abc 모듈의 내용을 AbcModule에서 가져오기
            var abcContent = SharpPy.Modules.AbcModule.GetModule();
            foreach (var item in abcContent)
            {
                module.ModuleDict[item.Key] = item.Value;
            }
            
            return module;
        }

        // contextlib 모듈 생성
        private static PyModule CreateContextlibModule()
        {
            return SharpPy.ContextlibModule.Create();
        }
    }
#endregion
}