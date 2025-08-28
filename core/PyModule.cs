namespace SharpPy
{
    #region Module and Import System

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
            ["sys"] = () => SharpPy.Modules.SysModule.CreateSysModule()
        };


        // import module_name
        public static PyModule Import(string moduleName)
        {
            // 1. sys.modules 캐시 확인
            if (SysModules.TryGetValue(moduleName, out PyModule cachedModule))
            {
                return cachedModule;
            }

            // 2. 내장 모듈 확인
            if (_builtinModules.TryGetValue(moduleName, out Func<PyModule> moduleFactory))
            {
                var module = moduleFactory();
                SysModules[moduleName] = module;
                return module;
            }

            // 3. sys.path를 사용한 파일 시스템 검색
            var foundModule = SearchModuleInPath(moduleName);
            if (foundModule != null)
            {
                return foundModule;
            }

            // 4. 모듈을 찾을 수 없음
            throw PyModuleNotFoundError.Create($"No module named '{moduleName}'");
        }

        // sys.path에서 모듈 검색
        private static PyModule SearchModuleInPath(string moduleName)
        {
            var sysPath = GetSysPath();
            if (sysPath == null) return null;

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
        private static PyModule LoadModuleFromFile(string moduleName, string filePath)
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

        // from module_name import item1, item2
        public static Dictionary<string, PyObject> FromImport(string moduleName, params string[] itemNames)
        {
            var module = Import(moduleName);
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
    }
#endregion
}