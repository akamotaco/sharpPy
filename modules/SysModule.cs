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
            // CPython 3.12: sys.stdlib_module_names (frozenset; used by traceback suggestions)
            module.ModuleDict["stdlib_module_names"] = StdlibModuleNames.FrozenSet();
            module.ModuleDict["version"] = new PyString(GetPythonVersion());
            module.ModuleDict["version_info"] = CreateVersionInfo();
            
            // 실행 경로
            module.ModuleDict["executable"] = new PyString(GetExecutablePath());
            module.ModuleDict["prefix"] = new PyString(GetPrefixPath());
            module.ModuleDict["exec_prefix"] = new PyString(GetPrefixPath());

            // 바이트 순서
            module.ModuleDict["byteorder"] = new PyString(BitConverter.IsLittleEndian ? "little" : "big");

            // 최대 정수 크기 (int 범위로 제한)
            module.ModuleDict["maxsize"] = new PyInt((long)nint.MaxValue);

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
            module.ModuleDict["get_int_max_str_digits"] = new PySysFunction("get_int_max_str_digits");
            module.ModuleDict["set_int_max_str_digits"] = new PySysFunction("set_int_max_str_digits");
            module.ModuleDict["exc_info"] = new PySysFunction("exc_info");
            module.ModuleDict["getrecursionlimit"] = new PySysFunction("getrecursionlimit");
            module.ModuleDict["setrecursionlimit"] = new PySysFunction("setrecursionlimit");
            module.ModuleDict["intern"] = new PySysFunction("intern");
            module.ModuleDict["flags"] = new PySysFlags();

            // CPython 3.12: Frame and exception introspection
            module.ModuleDict["_getframe"] = new PySysFunction("_getframe");
            module.ModuleDict["exception"] = new PySysFunction("exception");

            // CPython 3.12: sys.displayhook - REPL expression output hook
            // Reference: Python/sysmodule.c:460-520 (sys_displayhook)
            module.ModuleDict["displayhook"] = new PySysFunction("displayhook");
            module.ModuleDict["__displayhook__"] = new PySysFunction("displayhook"); // Original hook

            var unraisableHook = new PyBuiltinFunction("unraisablehook", PyUnraisable.DefaultHook);
            module.ModuleDict["unraisablehook"] = unraisableHook;
            module.ModuleDict["__unraisablehook__"] = unraisableHook;

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
            // Observable parity with the reference CPython 3.12.15 runtime.
            kwargs.SetItem(new PyString("name"), new PyString("cpython"));
            kwargs.SetItem(new PyString("version"), CreateVersionInfo());
            kwargs.SetItem(new PyString("hexversion"), new PyInt(0x030c0f00)); // 3.12.15
            kwargs.SetItem(new PyString("cache_tag"), new PyString("cpython-312"));

            return new PySimpleNamespace(kwargs);
        }

        internal static PyList CreateSysPath()
        {
            var pathList = new List<PyObject>();

            // Godot compiles sources into its own assembly/cache directory;
            // bundled Python modules live in the project resources instead.
#if GODOT
            pathList.Add(new PyString("res://"));
            var exeDir = "res://";
            var projectRoot = "res://";
#else
            pathList.Add(new PyString("."));
            var exeDir = IOHelper.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            var projectRoot = IOHelper.GetFullPath(IOHelper.CombinePath(exeDir, "..", "..", ".."));
#endif

            // 3. 표준 라이브러리 경로들 (CPython 호환 순서)
            // CPython 3.12: Lib 디렉토리만 사용 (stdlib 폴더 삭제됨)
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

        private static PyObject CreateVersionInfo()
        {
            // CPython 3.12.15 sys.version_info with named fields.
            return new PyVersionInfo();
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
            if (Name is "get_int_max_str_digits" or "set_int_max_str_digits")
            {
                if (Name == "get_int_max_str_digits")
                {
                    if (kwargs != null && kwargs.Length() != 0)
                        throw PyTypeError.Create("sys.get_int_max_str_digits() takes no keyword arguments");
                    if (args.Length != 0) throw PyTypeError.Create($"sys.get_int_max_str_digits() takes no arguments ({args.Length} given)");
                    return new PyInt(PyVM.Instance.IntMaxStrDigits);
                }
                int count = args.Length + (kwargs?.Length() ?? 0);
                if (count > 1) throw PyTypeError.Create($"set_int_max_str_digits() takes at most 1 argument ({count} given)");
                PyObject argument = args.Length == 1 ? args[0] : null;
                if (kwargs != null)
                {
                    foreach (var pair in kwargs.InternalDict)
                    {
                        if (pair.Key is not PyString key || key.Value != "maxdigits")
                            continue;
                        if (argument != null)
                            throw PyTypeError.Create("argument for set_int_max_str_digits() given by name ('maxdigits') and position (1)");
                        argument = pair.Value;
                    }
                }
                if (argument == null) throw PyTypeError.Create("set_int_max_str_digits() missing required argument 'maxdigits' (pos 1)");
                var index = PyObject.TryGetIndex(argument);
                if (index == null) throw PyTypeError.Create($"'{argument.GetTypeName()}' object cannot be interpreted as an integer");
                if (index.Value < int.MinValue || index.Value > int.MaxValue)
                    throw PyOverflowError.Create("Python int too large to convert to C int");
                int limit = (int)index.Value;
                if (limit != 0 && limit < 640) throw PyValueError.Create("maxdigits must be 0 or larger than 640");
                PyVM.Instance.IntMaxStrDigits = limit;
                return PyNone.Instance;
            }
            return Name switch
            {
                "getdefaultencoding" => CallGetDefaultEncoding(args),
                "getfilesystemencoding" => CallGetFilesystemEncoding(args),
                "exit" => CallExit(args),
                "getsizeof" => CallGetSizeOf(args),
                "getrefcount" => CallGetRefCount(args),
                "exc_info" => CallExcInfo(args),
                "getrecursionlimit" => CallGetRecursionLimit(args),
                "setrecursionlimit" => CallSetRecursionLimit(args),
                "intern" => CallIntern(args),
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

        /// <summary>
        /// CPython 3.12: Python/sysmodule.c:898-914 (sys_exc_info_impl)
        /// Return current exception information: (type, value, traceback).
        /// Traverses the frame stack to find the topmost exception.
        /// </summary>
        private PyObject CallExcInfo(PyObject[] args)
        {
            if (args.Length != 0)
                throw PyTypeError.Create($"exc_info() takes no arguments ({args.Length} given)");

            // CPython 3.12: Python/sysmodule.c:902
            // _PyErr_StackItem *err_info = _PyErr_GetTopmostException(_PyThreadState_GET());
            var exception = PyVM.GetTopmostException();

            if (exception == null)
            {
                // CPython 3.12: Python/sysmodule.c:903-905
                // No exception: return (None, None, None)
                return new PyTuple(PyNone.Instance, PyNone.Instance, PyNone.Instance);
            }

            // CPython 3.12: Python/sysmodule.c:907-913
            // exc_info() returns (type, value, traceback)
            var excType = exception.GetPyType();  // Exception type (class)
            PyObject excValue = exception;         // Exception instance (PyBaseException is PyObject)

            // Get traceback from exception's __traceback__ attribute
            PyObject excTraceback = PyNone.Instance;
            if (exception.__traceback__ != null)
            {
                excTraceback = exception.__traceback__;
            }

            return new PyTuple(excType, excValue, excTraceback);
        }

        // CPython 3.12: Python/sysmodule.c sys.getrecursionlimit / sys.setrecursionlimit.
        private static int RecursionLimitValue = 1000;
        private static readonly Dictionary<string, PyString> InternTable = new();

        private PyObject CallGetRecursionLimit(PyObject[] args)
        {
            if (args.Length != 0)
                throw PyTypeError.Create($"getrecursionlimit() takes no arguments ({args.Length} given)");
            return new PyInt(RecursionLimitValue);
        }

        private PyObject CallSetRecursionLimit(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"setrecursionlimit() takes exactly one argument ({args.Length} given)");
            var idx = PyObject.TryGetIndex(args[0]);
            if (idx == null)
                throw PyTypeError.Create($"'{args[0].GetTypeName()}' object cannot be interpreted as an integer");
            if (idx.Value <= 0)
                throw PyValueError.Create("recursion limit must be greater than zero");
            RecursionLimitValue = (int)Math.Min((long)idx.Value, (long)int.MaxValue);
            return PyNone.Instance;
        }

        private PyObject CallIntern(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"intern() takes exactly one argument ({args.Length} given)");
            if (args[0] is not PyString s)
                throw PyTypeError.Create($"intern() argument must be str, not {args[0].GetTypeName()}");
            lock (InternTable)
            {
                if (!InternTable.TryGetValue(s.Value, out var canon))
                {
                    canon = s;
                    InternTable[s.Value] = canon;
                }
                return canon;
            }
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
                    throw PyValueError.Create("call stack is not deep enough");
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
        /// <summary>
        /// CPython 3.12: Python/sysmodule.c:878-886 (sys_exception_impl)
        /// Return the current exception being handled.
        /// Traverses the frame stack to find the topmost exception.
        /// </summary>
        private PyObject CallException(PyObject[] args)
        {
            if (args.Length != 0)
            {
                throw PyTypeError.Create($"exception() takes no arguments ({args.Length} given)");
            }

            // CPython 3.12: Python/sysmodule.c:881
            // _PyErr_StackItem *err_info = _PyErr_GetTopmostException(_PyThreadState_GET());
            var exception = PyVM.GetTopmostException();

            if (exception == null)
            {
                // CPython 3.12: Python/sysmodule.c:885 - Py_RETURN_NONE
                return PyNone.Instance;
            }

            // CPython 3.12: Python/sysmodule.c:883 - return Py_NewRef(err_info->exc_value)
            // PyBaseException is a PyObject, so we can return it directly
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
    /// sys.stdin/stdout/stderr: TextIOWrapper-shaped stream object.
    /// CPython 3.12: Modules/_io/textio.c (public surface subset).
    /// </summary>
    public class PyFile : PyObject
    {
        public string Name { get; }
        private bool _closed;

        public PyFile(string name)
        {
            Name = name;
        }

        private string DisplayName => $"<{Name}>";
        private string Mode => Name == "stdin" ? "r" : "w";
        private System.IO.TextWriter Writer => Name switch
        {
            "stderr" => Console.Error,
            "stdout" => Console.Out,
            _ => Console.Out
        };

        public override string GetTypeName() => "TextIOWrapper";
        public override PyString ToRepr() => new PyString($"<_io.{GetTypeName()} name='{DisplayName}' mode='{Mode}' encoding='utf-8'>");
        public override string ToString() => ToRepr().Value;

        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "name":
                    return new PyString(DisplayName);
                case "mode":
                    return new PyString(Mode);
                case "encoding":
                    return new PyString("utf-8");
                case "errors":
                    return new PyString("strict");
                case "closed":
                    return PyBool.FromBool(_closed);
                case "line_buffering":
                    return PyBool.FromBool(Name != "stdin" && !Console.IsOutputRedirected);
                case "newlines":
                    return PyNone.Instance;
                case "write":
                    return new PyBoundBuiltinMethod(this, new PyBuiltinMethod("write", (self, args) =>
                    {
                        if (args.Length != 1)
                            throw PyTypeError.Create($"write() takes exactly one argument ({args.Length} given)");
                        string text = args[0] is PyString ps ? ps.Value : args[0].ToStr().Value;
                        var w = ((PyFile)self).Writer;
                        w.Write(text);
                        w.Flush();
                        return new PyInt(text.Length);
                    }, 1));
                case "writelines":
                    return new PyBoundBuiltinMethod(this, new PyBuiltinMethod("writelines", (self, args) =>
                    {
                        if (args.Length != 1)
                            throw PyTypeError.Create($"writelines() takes exactly one argument ({args.Length} given)");
                        var f = (PyFile)self;
                        var iter = args[0].GetIterator();
                        try
                        {
                            while (true)
                            {
                                var item = iter.Next();
                                string text = item is PyString ps ? ps.Value : item.ToStr().Value;
                                f.Writer.Write(text);
                            }
                        }
                        catch (PythonException ex) when (ex.PyException is PyStopIteration)
                        {
                        }
                        f.Writer.Flush();
                        return PyNone.Instance;
                    }, 1));
                case "flush":
                    return new PyBoundBuiltinMethod(this, new PyBuiltinMethod("flush", (self, args) =>
                    {
                        ((PyFile)self).Writer.Flush();
                        return PyNone.Instance;
                    }, 0));
                case "close":
                    return new PyBoundBuiltinMethod(this, new PyBuiltinMethod("close", (self, args) =>
                    {
                        ((PyFile)self)._closed = true;
                        return PyNone.Instance;
                    }, 0));
                case "isatty":
                    return new PyBoundBuiltinMethod(this, new PyBuiltinMethod("isatty", (self, args) =>
                    {
                        var f = (PyFile)self;
                        bool tty = f.Name switch
                        {
                            "stderr" => !Console.IsErrorRedirected,
                            "stdin" => !Console.IsInputRedirected,
                            _ => !Console.IsOutputRedirected
                        };
                        return PyBool.FromBool(tty);
                    }, 0));
                case "writable":
                    return new PyBoundBuiltinMethod(this, new PyBuiltinMethod("writable", (self, args) =>
                        PyBool.FromBool(((PyFile)self).Name != "stdin"), 0));
                case "readable":
                    return new PyBoundBuiltinMethod(this, new PyBuiltinMethod("readable", (self, args) =>
                        PyBool.FromBool(((PyFile)self).Name == "stdin"), 0));
                case "seekable":
                    return new PyBoundBuiltinMethod(this, new PyBuiltinMethod("seekable", (self, args) => PyBool.False, 0));
                case "fileno":
                    return new PyBoundBuiltinMethod(this, new PyBuiltinMethod("fileno", (self, args) =>
                        new PyInt(((PyFile)self).Name switch
                        {
                            "stdin" => 0,
                            "stderr" => 2,
                            _ => 1
                        }), 0));
                default:
                    return base.GetAttribute(name);
            }
        }
    }

    /// <summary>
    /// sys.version_info: tuple with named fields (CPython sys.version_info namedtuple).
    /// </summary>
    public class PyVersionInfo : PyTuple
    {
        private static readonly PyType VersionInfoPyType = new PyType("version_info", new[] { PyType.ObjectType });

        public PyVersionInfo()
            : base(new PyInt(3), new PyInt(12), new PyInt(15), new PyString("final"), new PyInt(0))
        {
        }

        public override PyType GetPyType() => VersionInfoPyType;
        public override string GetTypeName() => "version_info";

        public override PyObject GetAttribute(string name)
        {
            return name switch
            {
                "major" => Items[0],
                "minor" => Items[1],
                "micro" => Items[2],
                "releaselevel" => Items[3],
                "serial" => Items[4],
                "n_fields" => new PyInt(5),
                "n_sequence_fields" => new PyInt(5),
                "n_unnamed_fields" => new PyInt(0),
                _ => base.GetAttribute(name)
            };
        }
    }

    /// <summary>
    /// sys.flags: read-only interpreter flags namespace.
    /// CPython 3.12: Objects/sysmodule.c PyStructSequence 'flags'.
    /// </summary>
    public class PySysFlags : PyObject
    {
        private static readonly PyType FlagsPyType = new PyType("flags", new[] { PyType.ObjectType });
        public override PyType GetPyType() => FlagsPyType;
        public override string GetTypeName() => "flags";

        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "debug":
                case "inspect_arg":
                case "interactive":
                case "isolated":
                case "optimize":
                case "dont_write_bytecode":
                case "no_user_site":
                case "no_site":
                case "ignore_environment":
                case "verbose":
                case "bytes_warning":
                case "profiler":
                case "warn_default_encoding":
                    return new PyInt(0);
                case "unicode":
                    return new PyInt(1);
                case "hash_randomization":
                    return new PyInt(1);
                case "dev_mode":
                    return PyBool.False;
                case "utf8_mode":
                    return PyBool.False;
                case "xoptions":
                    return new PyTuple();
                default:
                    return base.GetAttribute(name);
            }
        }

        public override PyString ToRepr() => new PyString("sys.flags(debug=0, inspect_arg=0, ...)  # repr abbreviated for parity surface");
        public override string ToString() => ToRepr().Value;
    }
}
