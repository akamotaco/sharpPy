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
        
        throw new AttributeError($"module '{Name}' has no attribute '{name}'");
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

        // 가상 모듈 파일들
        private static Dictionary<string, string> _moduleFiles = new Dictionary<string, string>
        {
            ["math"] = @"
PI = 3.14159
E = 2.71828
def sqrt(x):
    return x ** 0.5
def sin(x):
    return 0.0
__all__ = ['PI', 'E', 'sqrt', 'sin']
",
            ["os"] = @"
name = 'posix'
sep = '/'
def getcwd():
    return '/current/path'
def listdir(path):
    return []
__all__ = ['name', 'sep', 'getcwd', 'listdir']
",
            ["mymodule"] = @"
VERSION = '1.0.0'
DEBUG = True
def helper_function():
    return 'helper'
def public_function():
    return 'public'
def _private_function():
    return 'private'
__all__ = ['VERSION', 'public_function']
"
        };

        // import module_name
        public static PyModule Import(string moduleName)
        {
            // 1. sys.modules 캐시 확인
            if (SysModules.TryGetValue(moduleName, out PyModule cachedModule))
            {
                return cachedModule;
            }

            // 2. 모듈 파일 찾기
            if (!_moduleFiles.TryGetValue(moduleName, out string sourceCode))
            {
                throw new ModuleNotFoundError($"No module named '{moduleName}'");
            }

            // 3. 새로운 모듈 객체 생성
            var module = new PyModule(moduleName, $"{moduleName}.py");

            // 4. sys.modules에 등록 (순환 import 방지)
            SysModules[moduleName] = module;

            // 5. 모듈 실행 (초기화)
            module.Execute(sourceCode);

            return module;
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
                catch (AttributeError)
                {
                    throw new ImportError($"cannot import name '{itemName}' from '{moduleName}'");
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