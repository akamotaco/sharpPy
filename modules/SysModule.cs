using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace SharpPy.Modules
{
    /// <summary>
    /// Python sys 모듈 구현 - 시스템 특정 매개변수 및 함수들
    /// </summary>
    public static class SysModule
    {
        public static PyModule CreateSysModule()
        {
            var module = new PyModule("sys", "C:\\Users\\m11\\Desktop\\work\\sharpPy\\modules\\sys.py");

            // sys.path - 모듈 검색 경로 리스트
            var sysPath = CreateSysPath();
            module.ModuleDict["path"] = sysPath;

            // sys.modules - 로드된 모듈들의 캐시 (동적으로 업데이트)
            module.ModuleDict["modules"] = new PySysModules();

            // 플랫폼 정보
            module.ModuleDict["platform"] = new PyString(GetPlatformName());
            module.ModuleDict["version"] = new PyString(GetPythonVersion());
            module.ModuleDict["version_info"] = CreateVersionInfo();
            
            // 실행 경로
            module.ModuleDict["executable"] = new PyString(GetExecutablePath());
            module.ModuleDict["prefix"] = new PyString(GetPrefixPath());
            module.ModuleDict["exec_prefix"] = new PyString(GetPrefixPath());

            // 바이트 순서
            module.ModuleDict["byteorder"] = new PyString(BitConverter.IsLittleEndian ? "little" : "big");

            // 최대 정수 크기 (int 범위로 제한)
            module.ModuleDict["maxsize"] = new PyInt(int.MaxValue);

            // 기본 인코딩
            module.ModuleDict["getdefaultencoding"] = new PySysFunction("getdefaultencoding");
            module.ModuleDict["getfilesystemencoding"] = new PySysFunction("getfilesystemencoding");

            // 종료 함수
            module.ModuleDict["exit"] = new PySysFunction("exit");

            // stdin, stdout, stderr (간단한 구현)
            module.ModuleDict["stdin"] = new PyFile("stdin");
            module.ModuleDict["stdout"] = new PyFile("stdout");
            module.ModuleDict["stderr"] = new PyFile("stderr");

            // 명령줄 인수 (빈 리스트로 초기화)
            module.ModuleDict["argv"] = new PyList();

            // 함수들
            module.ModuleDict["getsizeof"] = new PySysFunction("getsizeof");
            module.ModuleDict["getrefcount"] = new PySysFunction("getrefcount");

            return module;
        }

        private static PyList CreateSysPath()
        {
            var pathList = new List<PyObject>();

            // 1. 현재 디렉토리
            pathList.Add(new PyString("."));

            // 2. 실행 파일 디렉토리
            var exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";

            // 프로젝트 루트 디렉토리 (exe는 bin/Debug/net8.0/에 있으므로 3단계 위로)
            var projectRoot = Path.GetFullPath(Path.Combine(exeDir, "..", "..", ".."));

            // 3. 표준 라이브러리 경로들 (CPython 호환 순서)
            // 1순위: Lib 디렉토리 (CPython 호환 표준 라이브러리) - 프로젝트 루트에서
            pathList.Add(new PyString(Path.Combine(projectRoot, "Lib")));

            // 2순위: modules 디렉토리 (SharpPy 전용 C# 구현 모듈) - 프로젝트 루트에서
            pathList.Add(new PyString(Path.Combine(projectRoot, "modules")));

            // 3순위: 실행 파일 디렉토리
            pathList.Add(new PyString(exeDir));

            // 4. 환경 변수 PYTHONPATH
            var pythonPath = Environment.GetEnvironmentVariable("PYTHONPATH");
            if (!string.IsNullOrEmpty(pythonPath))
            {
                var paths = pythonPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
                foreach (var path in paths)
                {
                    pathList.Add(new PyString(path.Trim()));
                }
            }

            return new PyList(pathList.ToArray());
        }

        private static string GetPlatformName()
        {
            return Environment.OSVersion.Platform switch
            {
                PlatformID.Win32NT => "win32",
                PlatformID.Unix => "linux",
                PlatformID.MacOSX => "darwin",
                _ => "unknown"
            };
        }

        private static string GetPythonVersion()
        {
            return "3.12.0 (SharpPy 0.9.1, " + DateTime.Now.ToString("MMM dd yyyy HH:mm:ss") + ")";
        }

        private static PyTuple CreateVersionInfo()
        {
            return new PyTuple(
                new PyInt(3),      // major
                new PyInt(12),     // minor  
                new PyInt(0),      // micro
                new PyString("final"), // releaselevel
                new PyInt(0)       // serial
            );
        }

        private static string GetExecutablePath()
        {
            return Assembly.GetExecutingAssembly().Location;
        }

        private static string GetPrefixPath()
        {
            return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
        }
    }

    /// <summary>
    /// sys 모듈의 함수들 구현
    /// </summary>
    public class PySysFunction : PyBuiltinFunction
    {
        public PySysFunction(string name) : base(name) { }

        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            return Name switch
            {
                "getdefaultencoding" => CallGetDefaultEncoding(args),
                "getfilesystemencoding" => CallGetFilesystemEncoding(args),
                "exit" => CallExit(args),
                "getsizeof" => CallGetSizeOf(args),
                "getrefcount" => CallGetRefCount(args),
                _ => throw PyAttributeError.Create($"sys module has no function '{Name}'")
            };
        }

        private PyObject CallGetDefaultEncoding(PyObject[] args)
        {
            if (args.Length != 0)
                throw PyTypeError.Create($"getdefaultencoding() takes no arguments ({args.Length} given)");
            
            return new PyString("utf-8");
        }

        private PyObject CallGetFilesystemEncoding(PyObject[] args)
        {
            if (args.Length != 0)
                throw PyTypeError.Create($"getfilesystemencoding() takes no arguments ({args.Length} given)");
            
            return new PyString("utf-8");
        }

        private PyObject CallExit(PyObject[] args)
        {
            int exitCode = 0;
            if (args.Length > 0 && args[0] is PyInt code)
            {
                exitCode = (int)code.Value;
            }

            throw PySystemExit.Create(exitCode);
        }

        private PyObject CallGetSizeOf(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"getsizeof() takes exactly one argument ({args.Length} given)");

            // 간단한 크기 추정
            var obj = args[0];
            int size = obj switch
            {
                PyInt => 32,
                PyFloat => 32,
                PyBool => 16,
                PyString str => 48 + str.Value.Length * 2,
                PyList list => 64 + list.Items.Length * 8,
                PyDict dict => 128 + dict.Length() * 16,
                PyTuple tuple => 48 + tuple.Items.Length * 8,
                _ => 64 // 기본 객체 크기
            };

            return new PyInt(size);
        }

        private PyObject CallGetRefCount(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"getrefcount() takes exactly one argument ({args.Length} given)");

            // C#에서는 참조 카운팅을 직접 구현하지 않으므로 더미 값 반환
            return new PyInt(1);
        }
    }

    /// <summary>
    /// sys.modules의 동적 딕셔너리 구현
    /// </summary>
    public class PySysModules : PyObject
    {
        public override string GetTypeName() => "dict";
        public override string ToRepr() => "<sys.modules dict>";
        public override string ToString() => ToRepr();
        
        public override int Length()
        {
            return PyImportSystem.SysModules.Count;
        }

        public override PyObject GetAttribute(string name)
        {
            if (name == "keys")
                return new PySysModulesMethod("keys", this);
            if (name == "values")
                return new PySysModulesMethod("values", this);
            if (name == "items")
                return new PySysModulesMethod("items", this);
            if (name == "get")
                return new PySysModulesMethod("get", this);

            return base.GetAttribute(name);
        }

        // 딕셔너리처럼 동작
        public PyObject GetItem(PyObject key)
        {
            if (key is PyString keyStr && PyImportSystem.SysModules.TryGetValue(keyStr.Value, out var module))
                return module;
            throw PyKeyError.Create(key.ToRepr());
        }

        public void SetItem(PyObject key, PyObject value)
        {
            if (key is PyString keyStr && value is PyModule module)
                PyImportSystem.SysModules[keyStr.Value] = module;
            else
                throw PyTypeError.Create("sys.modules keys must be strings and values must be modules");
        }
    }

    /// <summary>
    /// sys.modules의 메서드들 구현
    /// </summary>
    public class PySysModulesMethod : PyBuiltinFunction
    {
        private readonly PySysModules _modules;

        public PySysModulesMethod(string name, PySysModules modules) : base(name)
        {
            _modules = modules;
        }

        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            return Name switch
            {
                "keys" => GetKeys(),
                "values" => GetValues(),
                "items" => GetItems(),
                "get" => GetItem(args),
                _ => throw PyAttributeError.Create($"sys.modules has no method '{Name}'")
            };
        }

        private PyObject GetKeys()
        {
            var keys = new List<PyObject>();
            foreach (var key in PyImportSystem.SysModules.Keys)
            {
                keys.Add(new PyString(key));
            }
            return new PyList(keys.ToArray());
        }

        private PyObject GetValues()
        {
            var values = new List<PyObject>();
            foreach (var value in PyImportSystem.SysModules.Values)
            {
                values.Add(value);
            }
            return new PyList(values.ToArray());
        }

        private PyObject GetItems()
        {
            var items = new List<PyObject>();
            foreach (var kvp in PyImportSystem.SysModules)
            {
                items.Add(new PyTuple(new PyString(kvp.Key), kvp.Value));
            }
            return new PyList(items.ToArray());
        }

        private PyObject GetItem(PyObject[] args)
        {
            if (args.Length == 0 || args.Length > 2)
                throw PyTypeError.Create($"get() takes 1 or 2 arguments ({args.Length} given)");

            var key = args[0];
            var defaultValue = args.Length > 1 ? args[1] : PyNone.Instance;

            if (key is PyString keyStr && PyImportSystem.SysModules.TryGetValue(keyStr.Value, out var module))
                return module;
            
            return defaultValue;
        }
    }

    /// <summary>
    /// 간단한 파일 객체 구현
    /// </summary>
    public class PyFile : PyObject
    {
        public string Name { get; }

        public PyFile(string name)
        {
            Name = name;
        }

        public override string GetTypeName() => "TextIOWrapper";
        public override string ToRepr() => $"<_io.{GetTypeName()} name='{Name}' mode='w' encoding='utf-8'>";
        public override string ToString() => ToRepr();
    }
}