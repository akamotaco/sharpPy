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
        /// <summary>The sys module of this interpreter: print reads sys.stdout from it.</summary>
        internal static PyModule Current;

        public static PyModule CreateSysModule()
        {
            // CPython 3.12: sys is a builtin module implemented in C (Python/sysmodule.c)
            // SharpPy: implemented in C# as builtin module (no .py file)
            var module = new PyModule("sys", "<builtin sys module>");

            // sys.path - 모듈 검색 경로 리스트
            var sysPath = CreateSysPath();
            module.ModuleDict["get_coroutine_origin_tracking_depth"] = new PyBuiltinFunction("get_coroutine_origin_tracking_depth",
                args => new PyInt(SharpPy.Core.PyCoroutine.OriginDepth));
            module.ModuleDict["set_coroutine_origin_tracking_depth"] = new PyBuiltinFunction("set_coroutine_origin_tracking_depth", args =>
            {
                int depth = PyObject.TryGetIndex(args[0])?.ToInt() ?? throw PyTypeError.Create(
                    $"'{args[0].GetTypeName()}' object cannot be interpreted as an integer");
                if (depth < 0) throw PyValueError.Create("depth must be >= 0");
                SharpPy.Core.PyCoroutine.OriginDepth = depth;
                return PyNone.Instance;
            });
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
            module.ModuleDict["platlibdir"] = new PyString("DLLs");
            module.ModuleDict["exec_prefix"] = new PyString(GetPrefixPath());

            // 바이트 순서
            module.ModuleDict["byteorder"] = new PyString(BitConverter.IsLittleEndian ? "little" : "big");

            // 최대 정수 크기 (int 범위로 제한)
            module.ModuleDict["maxsize"] = new PyInt((long)nint.MaxValue);
            module.ModuleDict["float_info"] = FloatInfo();
            module.ModuleDict["int_info"] = new PyStructSequence(new PyStructSequenceType("sys", "int_info", new[]
            {
                "bits_per_digit", "sizeof_digit", "default_max_str_digits", "str_digits_check_threshold",
            }), new PyObject[] { new PyInt(30), new PyInt(4), new PyInt(4300), new PyInt(640) });
            module.ModuleDict["hash_info"] = new PyStructSequence(new PyStructSequenceType("sys", "hash_info", new[]
            {
                "width", "modulus", "inf", "nan", "imag", "algorithm", "hash_bits", "seed_bits", "cutoff",
            }), new PyObject[]
            {
                new PyInt(64), new PyInt((1L << 61) - 1), new PyInt(314159), new PyInt(0), new PyInt(1000003),
                new PyString("siphash13"), new PyInt(64), new PyInt(128), new PyInt(0),
            });
            bool windows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
            // PyThread_GetInfo
            module.ModuleDict["thread_info"] = new PyStructSequence(new PyStructSequenceType("sys", "thread_info", new[] { "name", "lock", "version" }),
                new PyObject[] { new PyString(windows ? "nt" : "pthread"), PyNone.Instance, PyNone.Instance });
            module.ModuleDict["meta_path"] = new PyList(new PyObject[] { PyImportSystem.BuiltinFinder, PyImportSystem.PathFinder });
            module.ModuleDict["path_hooks"] = new PyList();
            module.ModuleDict["path_importer_cache"] = new PyDict();
            module.ModuleDict["hexversion"] = new PyInt(HexVersion);
            module.ModuleDict["api_version"] = new PyInt(1013);
            module.ModuleDict["copyright"] = new PyString("Copyright (c) 2001-2026 Python Software Foundation.\nAll Rights Reserved.");
            module.ModuleDict["pycache_prefix"] = PyNone.Instance;
            module.ModuleDict["dont_write_bytecode"] = PyBool.True;
            module.ModuleDict["_git"] = new PyTuple(new PyObject[] { new PyString("SharpPy"), new PyString(""), new PyString("") });
            module.ModuleDict["_xoptions"] = new PyDict();
            module.ModuleDict["_home"] = PyNone.Instance;
            module.ModuleDict["is_finalizing"] = new PyBuiltinFunction("is_finalizing", args => PyBool.FromBool(PyGil.Finalizing));
            module.ModuleDict["get_asyncgen_hooks"] = new PyBuiltinFunction("get_asyncgen_hooks", args => AsyncgenHooks());
            module.ModuleDict["set_asyncgen_hooks"] = new PyBuiltinFunction("set_asyncgen_hooks", SetAsyncgenHooks);
            module.ModuleDict["call_tracing"] = new PyBuiltinFunction("call_tracing", args =>
            {
                if (args.Length != 2)
                    throw PyTypeError.Create($"call_tracing expected 2 arguments, got {args.Length}");
                if (args[1] is not PyTuple arguments)
                    throw PyTypeError.Create($"call_tracing() argument 2 must be tuple, not {args[1].GetTypeName()}");
                return args[0].Call(arguments.Items, null);
            });
            module.ModuleDict["_clear_type_cache"] = new PyBuiltinFunction("_clear_type_cache", args => PyNone.Instance);
            module.ModuleDict["_current_frames"] = new PyBuiltinFunction("_current_frames", args => CurrentFrames());
            module.ModuleDict["_getframemodulename"] = new PyBuiltinFunction("_getframemodulename", GetFrameModuleName);
            if (windows)
            {
                module.ModuleDict["winver"] = new PyString("3.12");
                module.ModuleDict["getwindowsversion"] = new PyBuiltinFunction("getwindowsversion", args => WindowsVersion());
            }

            // 기본 인코딩
            module.ModuleDict["getdefaultencoding"] = new PySysFunction("getdefaultencoding");
            module.ModuleDict["getfilesystemencoding"] = new PySysFunction("getfilesystemencoding");

            // 종료 함수
            module.ModuleDict["exit"] = new PySysFunction("exit");

            // stdin, stdout, stderr (간단한 구현)
            module.ModuleDict["stdin"] = new PyFile("stdin");
            module.ModuleDict["stdout"] = new PyFile("stdout");
            Current = module;
            foreach (var prefix in new[] { "prefix", "exec_prefix", "base_prefix", "base_exec_prefix" })
                module.ModuleDict[prefix] = new PyString("");
            module.ModuleDict["stderr"] = new PyFile("stderr");
            foreach (var stream in new[] { "stdin", "stdout", "stderr" })
                module.ModuleDict[$"__{stream}__"] = module.ModuleDict[stream];

            // sys.argv is a copy of what the host put in PyConfig.argv
            // (Python/sysmodule.c:3612 COPY_LIST("argv", config->argv)). Program.Main fills it for the
            // CLI; an embedded host such as the Godot probe leaves PyArgv at its empty default, which
            // is what an embedding that sets no argv produces in CPython as well.
            var argvValues = SharpPy.Core.PyArgv.Values;
            var argvItems = new PyObject[argvValues.Length];
            for (int i = 0; i < argvValues.Length; i++) argvItems[i] = new PyString(argvValues[i]);
            module.ModuleDict["argv"] = new PyList(argvItems);
            var original = new List<PyObject> { new PyString(GetExecutablePath()) };
            original.AddRange(argvItems);
            module.ModuleDict["orig_argv"] = new PyList(original.ToArray());

            // CPython 3.12: sys.builtin_module_names (tuple of builtin C module names)
            module.ModuleDict["builtin_module_names"] = CreateBuiltinModuleNames();
            module.ModuleDict["float_repr_style"] = new PyString("short");
            module.ModuleDict["maxunicode"] = new PyInt(0x10FFFF);

            // CPython 3.12: sys.implementation (namespace with interpreter details)
            module.ModuleDict["implementation"] = CreateImplementation();

            // 함수들
            module.ModuleDict["getsizeof"] = new PySysFunction("getsizeof");
            module.ModuleDict["getrefcount"] = new PySysFunction("getrefcount");
            module.ModuleDict["get_int_max_str_digits"] = new PySysFunction("get_int_max_str_digits");
            module.ModuleDict["set_int_max_str_digits"] = new PySysFunction("set_int_max_str_digits");
            module.ModuleDict["exc_info"] = new PySysFunction("exc_info");
            module.ModuleDict["getrecursionlimit"] = new PySysFunction("getrecursionlimit");
            module.ModuleDict["gettrace"] = new PyBuiltinFunction("gettrace", args => PyVM.Instance.TraceFunction ?? PyNone.Instance);
            module.ModuleDict["audit"] = new PyBuiltinFunction("audit", args => PyNone.Instance);
            module.ModuleDict["addaudithook"] = new PyBuiltinFunction("addaudithook", args => PyNone.Instance);
            module.ModuleDict["settrace"] = new PyBuiltinFunction("settrace", args =>
            {
                PyVM.Instance.TraceFunction = args[0] is PyNone ? null : args[0];
                return PyNone.Instance;
            });
            module.ModuleDict["setprofile"] = new PyBuiltinFunction("setprofile", args =>
            {
                PyVM.Instance.ProfileFunction = args[0] is PyNone ? null : args[0];
                return PyNone.Instance;
            });
            module.ModuleDict["getprofile"] = new PyBuiltinFunction("getprofile", args => PyVM.Instance.ProfileFunction ?? PyNone.Instance);
            module.ModuleDict["_settraceallthreads"] = new PyBuiltinFunction("_settraceallthreads", args =>
            {
                var function = args[0] is PyNone ? null : args[0];
                PyGil.ForAllThreads(state => state.TraceFunction = function);
                return PyNone.Instance;
            });
            module.ModuleDict["_setprofileallthreads"] = new PyBuiltinFunction("_setprofileallthreads", args =>
            {
                var function = args[0] is PyNone ? null : args[0];
                PyGil.ForAllThreads(state => state.ProfileFunction = function);
                return PyNone.Instance;
            });
            module.ModuleDict["setrecursionlimit"] = new PySysFunction("setrecursionlimit");
            module.ModuleDict["getfilesystemencodeerrors"] = new PyBuiltinFunction("getfilesystemencodeerrors", args =>
                new PyString(System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows) ? "surrogatepass" : "surrogateescape"));
            module.ModuleDict["intern"] = new PySysFunction("intern");
            module.ModuleDict["getswitchinterval"] = new PyBuiltinFunction("getswitchinterval", args => new PyFloat(PyGil.SwitchInterval));
            module.ModuleDict["setswitchinterval"] = new PyBuiltinFunction("setswitchinterval", args =>
            {
                double interval = args[0].AsFloat().Value;
                if (interval <= 0)
                    throw PyValueError.Create("switch interval must be strictly positive");
                PyGil.SwitchInterval = interval;
                return PyNone.Instance;
            });
            module.ModuleDict["flags"] = Flags();
            module.ModuleDict["warnoptions"] = new PyList();

            // CPython 3.12: Frame and exception introspection
            module.ModuleDict["_getframe"] = new PySysFunction("_getframe");
            module.ModuleDict["exception"] = new PySysFunction("exception");

            // CPython 3.12: sys.displayhook - REPL expression output hook
            // Reference: Python/sysmodule.c:460-520 (sys_displayhook)
            module.ModuleDict["displayhook"] = new PySysFunction("displayhook");
            module.ModuleDict["excepthook"] = new PySysFunction("excepthook");
            module.ModuleDict["__excepthook__"] = module.ModuleDict["excepthook"];
            module.ModuleDict["breakpointhook"] = new PyBuiltinFunction("breakpointhook", PySysFunction.BreakpointHook);
            module.ModuleDict["__breakpointhook__"] = module.ModuleDict["breakpointhook"];
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
                new PyString(System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows) ? "nt" : "posix"),
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
            kwargs.SetItem(new PyString("hexversion"), new PyInt(HexVersion));
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

            // getpath.py: PYTHONPATH comes before the standard library.
            var pythonPath = Environment.GetEnvironmentVariable("PYTHONPATH");
            if (!string.IsNullOrEmpty(pythonPath))
            {
                var paths = pythonPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
                foreach (var path in paths)
                {
                    pathList.Add(new PyString(path.Trim()));
                }
            }

            // 3. 표준 라이브러리 경로들 (CPython 호환 순서)
            // CPython 3.12: Lib 디렉토리만 사용 (stdlib 폴더 삭제됨)
            pathList.Add(new PyString(IOHelper.CombinePath(projectRoot, "Lib")));

            // 3순위: modules 디렉토리 (SharpPy 전용 C# 구현 모듈) - 프로젝트 루트에서
            pathList.Add(new PyString(IOHelper.CombinePath(projectRoot, "modules")));

            // 4순위: 실행 파일 디렉토리
            pathList.Add(new PyString(exeDir));

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
            return "3.12.15 (SharpPy 0.9.1, " + DateTime.Now.ToString("MMM dd yyyy HH:mm:ss") + ")";
        }

        // 3.12.15 final
        private const int HexVersion = 0x030c0ff0;

        private static readonly PyStructSequenceType VersionInfoType = new("sys", "version_info", new[]
        {
            "major", "minor", "micro", "releaselevel", "serial",
        });

        private static PyObject CreateVersionInfo() => new PyStructSequence(VersionInfoType, new PyObject[]
        {
            new PyInt(3), new PyInt(12), new PyInt(15), new PyString("final"), new PyInt(0),
        });

        // PyFloat_GetInfo
        private static PyObject FloatInfo() => new PyStructSequence(new PyStructSequenceType("sys", "float_info", new[]
        {
            "max", "max_exp", "max_10_exp", "min", "min_exp", "min_10_exp", "dig", "mant_dig", "epsilon", "radix", "rounds",
        }), new PyObject[]
        {
            new PyFloat(double.MaxValue), new PyInt(1024), new PyInt(308), new PyFloat(2.2250738585072014e-308), new PyInt(-1021),
            new PyInt(-307), new PyInt(15), new PyInt(53), new PyFloat(2.220446049250313e-16), new PyInt(2), new PyInt(1),
        });

        // make_flags. SharpPy starts one way, so these are what that way would have set.
        private static PyObject Flags()
        {
            var fields = new[]
            {
                "debug", "inspect", "interactive", "optimize", "dont_write_bytecode", "no_user_site", "no_site",
                "ignore_environment", "verbose", "bytes_warning", "quiet", "hash_randomization", "isolated", "dev_mode",
                "utf8_mode", "warn_default_encoding", "safe_path", "int_max_str_digits",
            };
            var values = new PyObject[fields.Length];
            for (int i = 0; i < fields.Length; i++)
                values[i] = new PyInt(fields[i] switch
                {
                    "dont_write_bytecode" or "no_site" or "hash_randomization" => 1,
                    "int_max_str_digits" => 4300,
                    _ => 0,
                });
            values[Array.IndexOf(fields, "dev_mode")] = PyBool.False;
            values[Array.IndexOf(fields, "safe_path")] = PyBool.False;
            return new PyStructSequence(new PyStructSequenceType("sys", "flags", fields), values);
        }

        private static readonly PyStructSequenceType AsyncgenHooksType = new(null, "asyncgen_hooks", new[] { "firstiter", "finalizer" });

        private static PyObject AsyncgenHooks() => new PyStructSequence(AsyncgenHooksType, new[]
        {
            SharpPy.Core.PyAsyncGenerator.FirstIterHook ?? PyNone.Instance,
            SharpPy.Core.PyAsyncGenerator.FinalizerHook ?? PyNone.Instance,
        });

        // sys_set_asyncgen_hooks
        private static PyObject SetAsyncgenHooks(PyObject[] args, PyDict kwargs)
        {
            if (args.Length > 2)
                throw PyTypeError.Create($"set_asyncgen_hooks() takes at most 2 arguments ({args.Length} given)");
            PyObject firstiter = args.Length > 0 ? args[0] : null, finalizer = args.Length > 1 ? args[1] : null;
            if (kwargs != null)
                foreach (var pair in kwargs.InternalDict)
                {
                    string keyword = (pair.Key as PyString)?.Value;
                    if (keyword == "firstiter") firstiter = pair.Value;
                    else if (keyword == "finalizer") finalizer = pair.Value;
                    else throw PyTypeError.Create("'" + keyword + "' is an invalid keyword argument for this function");
                }
            if (finalizer != null && finalizer is not PyNone && !finalizer.IsCallable())
                throw PyTypeError.Create($"callable finalizer expected, got {finalizer.GetTypeName()}");
            if (firstiter != null && firstiter is not PyNone && !firstiter.IsCallable())
                throw PyTypeError.Create($"callable firstiter expected, got {firstiter.GetTypeName()}");
            if (finalizer != null)
                SharpPy.Core.PyAsyncGenerator.FinalizerHook = finalizer is PyNone ? null : finalizer;
            if (firstiter != null)
                SharpPy.Core.PyAsyncGenerator.FirstIterHook = firstiter is PyNone ? null : firstiter;
            return PyNone.Instance;
        }

        // _PyThread_CurrentFrames: the top frame of every thread, by thread id.
        private static PyObject CurrentFrames()
        {
            var result = new PyDict();
            PyGil.ForAllThreads(state =>
            {
                if (state.Frames.Count > 0)
                    result.SetItem(new PyInt(state.Ident), state.Frames.Peek());
            });
            return result;
        }

        // sys__getframemodulename_impl
        private static PyObject GetFrameModuleName(PyObject[] args, PyDict kwargs)
        {
            int depth = args.Length > 0 ? args[0].ToInt() : 0;
            if (kwargs != null)
                foreach (var pair in kwargs.InternalDict)
                    if ((pair.Key as PyString)?.Value == "depth")
                        depth = pair.Value.ToInt();
            var frame = PyVM.CurrentFrame;
            while (frame != null && depth-- > 0)
                frame = frame.ParentFrame;
            if (frame == null)
                return PyNone.Instance;
            var globals = frame.GetAttribute("f_globals");
            var name = new PyString("__name__");
            return globals is PyDict names && names.ContainsKey(name) ? names.GetItem(name) : PyNone.Instance;
        }

        private static readonly PyStructSequenceType WindowsVersionType = new("sys", "getwindowsversion", new[]
        {
            "major", "minor", "build", "platform", "service_pack", "service_pack_major", "service_pack_minor",
            "suite_mask", "product_type", "platform_version",
        }, 5);

        private static PyObject WindowsVersion()
        {
            var version = Environment.OSVersion.Version;
            return new PyStructSequence(WindowsVersionType, new PyObject[]
            {
                new PyInt(version.Major), new PyInt(version.Minor), new PyInt(version.Build), new PyInt(2),
                new PyString(Environment.OSVersion.ServicePack), new PyInt(0), new PyInt(0), new PyInt(0x100), new PyInt(1),
                new PyTuple(new PyObject[] { new PyInt(version.Major), new PyInt(version.Minor), new PyInt(version.Build) }),
            });
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
                "exit" => CallExit(args, kwargs),
                "getsizeof" => CallGetSizeOf(args),
                "getrefcount" => CallGetRefCount(args),
                "exc_info" => CallExcInfo(args),
                "getrecursionlimit" => CallGetRecursionLimit(args),
                "setrecursionlimit" => CallSetRecursionLimit(args),
                "intern" => CallIntern(args),
                "_getframe" => CallGetFrame(args),
                "exception" => CallException(args),
                "displayhook" => CallDisplayHook(args),
                "excepthook" => CallExceptHook(args),
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

        /// <summary>
        /// sys.exit(code=None, /). The object is handed to SystemExit untouched - the reference
        /// interpreter keeps 'bye', 1.5 and [1, 2] and writes them to stderr at exit
        /// (Python/pythonrun.c:746-762) instead of quietly turning them into 0.
        /// Measured refusals: "exit expected at most 1 argument, got 2" and
        /// "sys.exit() takes no keyword arguments" (positional-only).
        /// </summary>
        private PyObject CallExit(PyObject[] args, PyDict kwargs)
        {
            if (kwargs != null && kwargs.Length() != 0)
                throw PyTypeError.Create("sys.exit() takes no keyword arguments");
            if (args.Length > 1)
                throw PyTypeError.Create($"exit expected at most 1 argument, got {args.Length}");
            // sys.exit is sys_exit_impl (Python/sysmodule.c:947), which ends in
            // PyErr_SetObject(PyExc_SystemExit, status) (:951). That normalizes through
            // _PyErr_CreateException (Python/errors.c:39): None or missing builds the exception with
            // no arguments (:43), a tuple IS the argument list (:46), anything else is one argument
            // (:50). Measured: sys.exit(None).args == () while SystemExit(None).args == (None,), and
            // sys.exit((1, 2)).args == (1, 2) with code (1, 2).
            if (args.Length == 0 || args[0] is PyNone)
                throw new PythonException(new PySystemExit(new PyObject[0]));
            if (args.Length == 1 && args[0] is PyTuple splat)
                throw new PythonException(new PySystemExit(splat.Items));
            throw new PythonException(new PySystemExit(args));
        }

        private PyObject CallGetSizeOf(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"getsizeof() takes exactly one argument ({args.Length} given)");

            // _PySys_GetSizeOf: a class's own __sizeof__ answers.
            var obj = args[0];
            var declared = obj.LookupUserSpecial("__sizeof__");
            if (declared != null)
                return new PyInt(((PyInt)obj.CallSpecial(declared)).Value + 16);

            // 간단한 크기 추정
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
            PyObject excValue = PyExceptionSlots.Visible(exception);

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
        internal static int RecursionLimit => RecursionLimitValue;
        private static readonly Dictionary<string, PyString> InternTable = new();

        // sys.excepthook(type, value, traceback) prints the report on sys.stderr.
        // sys_breakpointhook: $PYTHONBREAKPOINT names the hook, and what cannot be imported is ignored with a warning.
        internal static PyObject BreakpointHook(PyObject[] args, PyDict kwargs)
        {
            string setting = Environment.GetEnvironmentVariable("PYTHONBREAKPOINT");
            if (string.IsNullOrEmpty(setting))
                setting = "pdb.set_trace";
            else if (setting == "0")
                return PyNone.Instance;
            int dot = setting.LastIndexOf('.');
            string modulePath = dot < 0 ? "builtins" : setting.Substring(0, dot);
            string attribute = dot < 0 ? setting : setting.Substring(dot + 1);
            PyObject function = null;
            if (dot != 0)
            {
                try
                {
                    function = ImportForHook(modulePath).GetAttribute(attribute);
                }
                catch (PythonException error) when (error.PyException is PyImportError or PyAttributeError)
                {
                }
            }
            if (function != null)
                return function.Call(args, kwargs);
            PyImportSystem.Import("warnings").GetAttribute("warn").Call(new PyObject[]
                { new PyString($"Ignoring unimportable $PYTHONBREAKPOINT: \"{setting}\""), PyType.RuntimeWarningType }, null);
            return PyNone.Instance;
        }

        private static PyObject ImportForHook(string path) =>
            PyImportSystem.Import("importlib").GetAttribute("import_module").Call(new PyObject[] { new PyString(path) }, null);

        private PyObject CallExceptHook(PyObject[] args)
        {
            if (args.Length != 3)
                throw PyTypeError.Create($"excepthook expected 3 arguments, got {args.Length}");
            // PyErr_Display. The format is traceback.py's, as the C writer's is (and is in 3.13).
            var stderr = PyImportSystem.Import("sys").GetAttribute("stderr");
            var print = PyImportSystem.Import("traceback").GetAttribute("print_exception");
            var keywords = new PyDict();
            keywords.SetItem(new PyString("file"), stderr);
            print.Call(new[] { args[0], args[1], args[2] }, keywords);
            return PyNone.Instance;
        }

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
            int limit = (int)Math.Min((long)idx.Value, (long)int.MaxValue);
            // sys_setrecursionlimit_impl: the limit may not fall below the depth in use.
            int depth = PyVM.Instance.FrameDepth;
            if (depth >= limit)
                throw PyRecursionError.Create($"cannot set the recursion limit to {limit} at the recursion depth {depth}: the limit is too low");
            RecursionLimitValue = limit;
            return PyNone.Instance;
        }

        private PyObject CallIntern(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"intern() takes exactly one argument ({args.Length} given)");
            if (args[0] is not PyString s)
                throw PyTypeError.Create($"intern() argument must be str, not {args[0].GetTypeName()}");
            return Intern(s);
        }

        // PyUnicode_InternInPlace
        internal static PyString Intern(PyString text)
        {
            lock (InternTable)
            {
                if (!InternTable.TryGetValue(text.Value, out var canon))
                {
                    canon = text;
                    InternTable[text.Value] = canon;
                }
                return canon;
            }
        }

        // PyUnicode_CHECK_INTERNED
        internal static bool IsInterned(PyString text)
        {
            lock (InternTable)
                return InternTable.TryGetValue(text.Value, out var canon) && ReferenceEquals(canon, text);
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
            return PyExceptionSlots.Visible(exception);
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

            // sys_displayhook, Python/sysmodule.c
            var value = args[0];
            if (value is PyNone)
                return PyNone.Instance;
            var builtins = PyBuiltinsModule.Instance;
            builtins.SetAttribute("_", PyNone.Instance);
            if (!PyImportSystem.TryGetModule("sys", out var sys) || sys.GetAttribute("stdout") is not { } stdout || stdout is PyNone)
                throw PyRuntimeError.Create("lost sys.stdout");
            var write = stdout.GetAttribute("write");
            write.Call(new PyObject[] { value.ToRepr() }, null);
            write.Call(new PyObject[] { new PyString("\n") }, null);
            builtins.SetAttribute("_", value);
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

        public override PyType GetPyType() => PyFileContextManager.TextType;
        public override string GetTypeName() => "_io.TextIOWrapper";
        public override PyString ToRepr() => new PyString($"<{GetTypeName()} name='{DisplayName}' mode='{Mode}' encoding='utf-8'>");

        private PyObject _buffer;

        // The bytes under the text: unbuffered, so that it keeps its place among the text writes.
        private PyObject Buffer() => _buffer ??= new PyFileContextManager(Name switch { "stdin" => 0, "stderr" => 2, _ => 1 },
            Name == "stdin" ? "rb" : "wb", null, null, null, 0, false);

        private void CheckOpen()
        {
            if (_closed)
                throw PyValueError.Create("I/O operation on closed file.");
        }

        private void CheckReadable()
        {
            CheckOpen();
            if (Name != "stdin")
                throw PyUnsupportedOperation.Create("not readable");
        }

        private string ReadLine()
        {
            CheckReadable();
            var line = Console.In.ReadLine();
            return line == null ? "" : line + '\n';
        }

        public override PyObject GetIterator() => this;

        public override PyObject Next()
        {
            string line = ReadLine();
            return line.Length != 0 ? new PyString(line) : throw PyStopIteration.Create();
        }

        private PyObject Method(string name, Func<PyObject[], PyObject> body) =>
            new PyBoundBuiltinMethod(this, new PyBuiltinMethod(name, (self, args) => body(args), -1));
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
                        CheckOpen();
                        if (Name == "stdin")
                            throw PyUnsupportedOperation.Create("not writable");
                        if (args[0] is not PyString && args[0] is not PyStrSubclass)
                            throw PyTypeError.Create($"write() argument must be str, not {args[0].GetTypeName()}");
                        string text = args[0].ToStr().Value;
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
                case "buffer":
                    return Buffer();
                case "detach":
                    return Method(name, args => Buffer());
                case "write_through":
                    return PyBool.False;
                case "__enter__":
                    return Method(name, args => this);
                case "__exit__":
                    return Method(name, args =>
                    {
                        _closed = true;
                        return PyNone.Instance;
                    });
                case "__iter__":
                    return Method(name, args => this);
                case "__next__":
                    return Method(name, args => Next());
                case "reconfigure":
                    return Method(name, args => PyNone.Instance);
                case "read":
                    return Method(name, args =>
                    {
                        CheckReadable();
                        int size = args.Length > 0 && args[0] is not PyNone ? args[0].ToInt() : -1;
                        if (size < 0)
                            return new PyString(Console.In.ReadToEnd());
                        var chars = new char[size];
                        return new PyString(new string(chars, 0, Console.In.Read(chars, 0, size)));
                    });
                case "readline":
                    return Method(name, args => new PyString(ReadLine()));
                case "readlines":
                    return Method(name, args =>
                    {
                        var lines = new List<PyObject>();
                        for (string line = ReadLine(); line.Length != 0; line = ReadLine())
                            lines.Add(new PyString(line));
                        return new PyList(lines.ToArray());
                    });
                case "seek":
                case "tell":
                case "truncate":
                    return Method(name, args => throw PyUnsupportedOperation.Create($"underlying stream is not seekable"));
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
}
