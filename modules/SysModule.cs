using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

#if GODOT
using IOHelper = Godot_IO.Helper;
#else
using IOHelper = DotNet_IO.Helper;
#endif

namespace SharpPy.Modules
{
    /// <summary>
    /// Python sys 모듈 구현 - 시스템 특정 매개변수 및 함수들
    /// </summary>
    public static class SysModule
    {
        public static PyModule CreateSysModule()
        {
            // CPython 3.12: sys is a builtin module implemented in C (Python/sysmodule.c)
            // SharpPy: implemented in C# as builtin module (no .py file)
            var module = new PyModule("sys", "<builtin sys module>");

            // sys.path - 모듈 검색 경로 리스트
            var sysPath = CreateSysPath();
            module.ModuleDict["path"] = sysPath;

            // CPython 3.12: Python/sysmodule.c:3716 - sys.modules is PyDict
            // sys.modules - 로드된 모듈들의 캐시 (동적으로 업데이트)
            module.ModuleDict["modules"] = PyImportSystem.SysModules;

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

            // CPython 3.12: sys.builtin_module_names (tuple of builtin C module names)
            module.ModuleDict["builtin_module_names"] = CreateBuiltinModuleNames();

            // CPython 3.12: sys.implementation (namespace with interpreter details)
            module.ModuleDict["implementation"] = CreateImplementation();

            // 함수들
            module.ModuleDict["getsizeof"] = new PySysFunction("getsizeof");
            module.ModuleDict["getrefcount"] = new PySysFunction("getrefcount");
            module.ModuleDict["exc_info"] = new PySysFunction("exc_info");

            // CPython 3.12: Frame and exception introspection
            module.ModuleDict["_getframe"] = new PySysFunction("_getframe");
            module.ModuleDict["exception"] = new PySysFunction("exception");

            // CPython 3.12: sys.displayhook - REPL expression output hook
            // Reference: Python/sysmodule.c:460-520 (sys_displayhook)
            module.ModuleDict["displayhook"] = new PySysFunction("displayhook");
            module.ModuleDict["__displayhook__"] = new PySysFunction("displayhook"); // Original hook

            return module;
        }

        private static PyTuple CreateBuiltinModuleNames()
        {
            // CPython 3.12: Names of C modules compiled into this interpreter
            // SharpPy: Names of C# modules in _builtinModules
            var names = new List<PyObject>
            {
                new PyString("sys"),
                new PyString("builtins"),
                new PyString("math"),
                new PyString("time"),
                new PyString("itertools"),
                new PyString("_collections"),
                new PyString("_functools"),
                new PyString("_random"),
                new PyString("nt"),       // OS interface (Windows/Linux/Mac)
                new PyString("posix"),    // Alias for nt in SharpPy
            };
            return new PyTuple(names.ToArray());
        }

        private static PyObject CreateImplementation()
        {
            // CPython 3.12: Python/sysmodule.c:3193-3246 (make_impl_info)
            // sys.implementation is a SimpleNamespace object with interpreter details
            // Created via _PyNamespace_New(impl_info) in CPython
            var kwargs = new PyDict();
            kwargs.SetItem(new PyString("name"), new PyString("sharppy"));
            kwargs.SetItem(new PyString("version"), CreateVersionInfo());
            kwargs.SetItem(new PyString("hexversion"), new PyInt(0x030c0000)); // 3.12.0
            kwargs.SetItem(new PyString("cache_tag"), new PyString("sharppy-312"));

            return new PySimpleNamespace(kwargs);
        }

        private static PyList CreateSysPath()
        {
            var pathList = new List<PyObject>();

            // 1. 현재 디렉토리
            pathList.Add(new PyString("."));

            // 2. 실행 파일 디렉토리
            var exeDir = IOHelper.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";

            // 프로젝트 루트 디렉토리 (exe는 bin/Debug/net8.0/에 있으므로 3단계 위로)
            var projectRoot = IOHelper.GetFullPath(IOHelper.CombinePath(exeDir, "..", "..", ".."));

            // 3. 표준 라이브러리 경로들 (CPython 호환 순서)
            // 1순위: stdlib 디렉토리 (SharpPy Python 표준 라이브러리) - 프로젝트 루트에서
            pathList.Add(new PyString(IOHelper.CombinePath(projectRoot, "stdlib")));

            // 2순위: Lib 디렉토리 (CPython 호환 표준 라이브러리) - 프로젝트 루트에서
            pathList.Add(new PyString(IOHelper.CombinePath(projectRoot, "Lib")));

            // 3순위: modules 디렉토리 (SharpPy 전용 C# 구현 모듈) - 프로젝트 루트에서
            pathList.Add(new PyString(IOHelper.CombinePath(projectRoot, "modules")));

            // 4순위: 실행 파일 디렉토리
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
            return IOHelper.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
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
                "exc_info" => CallExcInfo(args),
                "_getframe" => CallGetFrame(args),
                "exception" => CallException(args),
                "displayhook" => CallDisplayHook(args),
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

        private PyObject CallExcInfo(PyObject[] args)
        {
            if (args.Length != 0)
                throw PyTypeError.Create($"exc_info() takes no arguments ({args.Length} given)");

            // Get current frame from VM
            var currentFrame = PyVM.GetCurrentFrame();

            if (currentFrame == null)
            {
                // No frame context: return (None, None, None)
                return new PyTuple(PyNone.Instance, PyNone.Instance, PyNone.Instance);
            }

            // Check for current exception in frame
            var exception = currentFrame.CurrentException ?? currentFrame.LastException;

            if (exception == null)
            {
                // No exception: return (None, None, None)
                return new PyTuple(PyNone.Instance, PyNone.Instance, PyNone.Instance);
            }

            // CPython 3.12: exc_info() returns (type, value, traceback)
            var excType = exception.GetPyType();  // Exception type (class)
            var excValue = exception;              // Exception instance
            var excTraceback = PyNone.Instance;    // TODO: Implement traceback objects

            return new PyTuple(excType, excValue, excTraceback);
        }

        /// <summary>
        /// CPython 3.12: sys._getframe(depth=0)
        /// Return a frame object from the call stack.
        /// </summary>
        private PyObject CallGetFrame(PyObject[] args)
        {
            // Parse optional depth argument (default 0)
            int depth = 0;
            if (args.Length > 0)
            {
                if (args[0] is PyInt depthInt)
                {
                    depth = (int)depthInt.Value;
                }
                else
                {
                    throw PyTypeError.Create("_getframe() argument must be an integer");
                }
            }

            if (args.Length > 1)
            {
                throw PyTypeError.Create($"_getframe() takes at most 1 argument ({args.Length} given)");
            }

            if (depth < 0)
            {
                throw PyValueError.Create("_getframe() argument must be >= 0");
            }

            // Get current frame from VM
            var currentFrame = PyVM.GetCurrentFrame();

            if (currentFrame == null)
            {
                throw PyRuntimeError.Create("no current frame");
            }

            // Walk back 'depth' frames
            var targetFrame = currentFrame;
            for (int i = 0; i < depth; i++)
            {
                if (targetFrame.ParentFrame == null)
                {
                    throw PyValueError.Create($"call stack is not deep enough (depth {depth})");
                }
                targetFrame = targetFrame.ParentFrame;
            }

            return targetFrame;
        }

        /// <summary>
        /// CPython 3.12: sys.exception()
        /// Return the current exception being handled (CPython 3.12 new function).
        /// This is preferred over sys.exc_info() in Python 3.12+
        /// </summary>
        private PyObject CallException(PyObject[] args)
        {
            if (args.Length != 0)
            {
                throw PyTypeError.Create($"exception() takes no arguments ({args.Length} given)");
            }

            // Get current frame from VM
            var currentFrame = PyVM.GetCurrentFrame();

            if (currentFrame == null)
            {
                // CPython 3.12: No frame context → return None (sysmodule.c:885: Py_RETURN_NONE)
                return PyNone.Instance;
            }

            // Check for current exception in frame
            var exception = currentFrame.CurrentException ?? currentFrame.LastException;

            if (exception == null)
            {
                // CPython 3.12: No exception being handled → return None (sysmodule.c:885)
                // Reference: sys_exception_impl() returns None when err_info->exc_value == NULL
                return PyNone.Instance;
            }

            return exception;
        }

        /// <summary>
        /// CPython 3.12: sys.displayhook(value)
        /// Hook called by REPL to display expression results
        /// Reference: Python/sysmodule.c:460-520 (sys_displayhook)
        /// </summary>
        private PyObject CallDisplayHook(PyObject[] args)
        {
            if (args.Length != 1)
            {
                throw PyTypeError.Create($"displayhook() takes exactly one argument ({args.Length} given)");
            }

            var value = args[0];

            // CPython: if (o == Py_None) { Py_RETURN_NONE; }
            // Python/sysmodule.c:467-469
            if (value == PyNone.Instance || value is PyNone)
            {
                // Don't print None values
                return PyNone.Instance;
            }

            // CPython: Set builtins._ to the value
            // Python/sysmodule.c:470-471
            // TODO: Implement builtins._ = value
            // For now, we skip this as builtins module needs enhancement

            // CPython: Get sys.stdout
            // Python/sysmodule.c:472-478
            // For now, we use Console.WriteLine directly

            // CPython: Print repr(value) to stdout
            // Python/sysmodule.c:479-499
            var reprValue = value.ToRepr().Value;
            Console.WriteLine(reprValue);

            return PyNone.Instance;
        }
    }

    // CPython 3.12: sys.modules is a regular PyDict (Python/import.c:185)
    // No need for custom PySysModules class - use PyDict directly

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
        public override PyString ToRepr() => new PyString($"<_io.{GetTypeName()} name='{Name}' mode='w' encoding='utf-8'>");
        public override string ToString() => $"<_io.{GetTypeName()} name='{Name}' mode='w' encoding='utf-8'>";
    }
}