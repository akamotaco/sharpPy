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
        // _PyEval_SetSwitchInterval's microseconds; the default is 5 ms.
        private static uint _switchMicroseconds = 5000;

        /// <summary>The sys module of this interpreter: print reads sys.stdout from it.</summary>
        internal static PyModule Current;

        private static bool _streamsReady;
        private static string stdioEncoding = "utf-8";

        /// <summary>
        /// Python/pylifecycle.c init_sys_streams and create_stdio: each standard stream is
        /// open(fd, mode, buffering=-1, closefd=False) under a TextIOWrapper, line buffered when
        /// it is a terminal and always for stderr, with "\n" translated on Windows. Runs once, after
        /// the import system can reach the codecs; the fd table maps 0, 1 and 2 to the console.
        /// </summary>
        internal static void InitStdStreams()
        {
            if (_streamsReady || Current == null)
                return;
            _streamsReady = true;
            PyObject Create(int fd, bool write, string name, string errors)
            {
                var kwargs = new PyDict();
                kwargs.SetItem(PyString.From("closefd"), PyBool.False);
                var buffer = IoModule.Open(new PyObject[] { PyInt.From(fd), PyString.From(write ? "wb" : "rb"), PyInt.From(-1) }, kwargs);
                var raw = buffer.GetAttribute("raw");
                raw.SetAttribute("name", PyString.From(name));
                bool isatty = raw.GetAttribute("isatty").Call(Array.Empty<PyObject>(), null).IsTrue();
                bool lineBuffering = isatty || fd == 2;
                PyObject newline = OperatingSystem.IsWindows() ? PyNone.Instance : PyString.From("\n");
                var stream = PyTextIOWrapper.WrapperType.Call(new PyObject[]
                {
                    buffer, PyString.From(stdioEncoding), PyString.From(errors), newline, PyBool.FromBool(lineBuffering), PyBool.False,
                }, null);
                stream.SetAttribute("mode", PyString.From(write ? "w" : "r"));
                // The interpreter keeps these to its very end (flush_std_files), so no finalizer
                // pass closes them under a __del__ that still prints.
                foreach (var layer in new[] { stream, buffer, raw })
                    PyFinalizer.Detach(PyFinalizer.Attach(layer));
                return stream;
            }
            // config_init_stdio_encoding: PYTHONIOENCODING is "encoding[:errors]"; the errors apply
            // to stdin and stdout, stderr keeps backslashreplace.
            string encoding = "utf-8", errors = "strict";
            var setting = Environment.GetEnvironmentVariable("PYTHONIOENCODING");
            if (!string.IsNullOrEmpty(setting))
            {
                int colon = setting.IndexOf(':');
                string name = colon < 0 ? setting : setting.Substring(0, colon);
                if (name.Length != 0)
                    encoding = name;
                if (colon >= 0 && colon + 1 < setting.Length)
                    errors = setting.Substring(colon + 1);
            }
            stdioEncoding = encoding;
            var dict = Current.ModuleDict;
            dict["stdin"] = dict["__stdin__"] = Create(0, false, "<stdin>", errors);
            dict["stdout"] = dict["__stdout__"] = Create(1, true, "<stdout>", errors);
            dict["stderr"] = dict["__stderr__"] = Create(2, true, "<stderr>", "backslashreplace");
        }

        /// <summary>
        /// flush_std_files: sys.stdout and sys.stderr, if they are still open. A failing stdout
        /// flush is reported; CPython also turns it into exit status 120.
        /// </summary>
        internal static bool FlushStdFiles()
        {
            bool ok = true;
            if (Current == null)
                return ok;
            foreach (var name in new[] { "stdout", "stderr" })
            {
                if (!Current.ModuleDict.TryGetValue(name, out var stream) || stream is PyNone)
                    continue;
                try
                {
                    if (stream.GetAttribute("closed").IsTrue())
                        continue;
                    stream.GetAttribute("flush").Call(Array.Empty<PyObject>(), null);
                }
                catch (PythonException error)
                {
                    if (name == "stdout")
                    {
                        PyUnraisable.Report(error, stream);
                        ok = false;
                    }
                }
            }
            return ok;
        }

        public static PyModule CreateSysModule()
        {
            // CPython 3.12: sys is a builtin module implemented in C (Python/sysmodule.c)
            // SharpPy: implemented in C# as builtin module (no .py file)
            var module = new PyModule("sys", "<builtin sys module>");

            // sys.path - 모듈 검색 경로 리스트
            var sysPath = CreateSysPath();
            module.ModuleDict["get_coroutine_origin_tracking_depth"] = new PyBuiltinFunction("get_coroutine_origin_tracking_depth",
                args => PyInt.From(SharpPy.Core.PyCoroutine.OriginDepth));
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
            module.ModuleDict["platform"] = PyString.From(GetPlatformName());
            // CPython 3.12: sys.stdlib_module_names (frozenset; used by traceback suggestions)
            module.ModuleDict["stdlib_module_names"] = StdlibModuleNames.FrozenSet();
            module.ModuleDict["version"] = PyString.From(GetPythonVersion());
            module.ModuleDict["version_info"] = CreateVersionInfo();
            
            // 실행 경로
            module.ModuleDict["executable"] = PyString.From(GetExecutablePath());
            module.ModuleDict["prefix"] = PyString.From(PrefixPath());
            module.ModuleDict["platlibdir"] = PyString.From("DLLs");
            module.ModuleDict["exec_prefix"] = PyString.From(PrefixPath());

            // 바이트 순서
            module.ModuleDict["byteorder"] = PyString.From(BitConverter.IsLittleEndian ? "little" : "big");

            // 최대 정수 크기 (int 범위로 제한)
            module.ModuleDict["maxsize"] = PyInt.From((long)nint.MaxValue);
            module.ModuleDict["float_info"] = FloatInfo();
            module.ModuleDict["int_info"] = new PyStructSequence(new PyStructSequenceType("sys", "int_info", new[]
            {
                "bits_per_digit", "sizeof_digit", "default_max_str_digits", "str_digits_check_threshold",
            }), new PyObject[] { PyInt.From(30), PyInt.From(4), PyInt.From(4300), PyInt.From(640) });
            module.ModuleDict["hash_info"] = new PyStructSequence(new PyStructSequenceType("sys", "hash_info", new[]
            {
                "width", "modulus", "inf", "nan", "imag", "algorithm", "hash_bits", "seed_bits", "cutoff",
            }), new PyObject[]
            {
                PyInt.From(64), PyInt.From((1L << 61) - 1), PyInt.From(314159), PyInt.From(0), PyInt.From(1000003),
                PyString.From("siphash13"), PyInt.From(64), PyInt.From(128), PyInt.From(0),
            });
            bool windows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
            // PyThread_GetInfo
            module.ModuleDict["thread_info"] = new PyStructSequence(new PyStructSequenceType("sys", "thread_info", new[] { "name", "lock", "version" }),
                new PyObject[] { PyString.From(windows ? "nt" : "pthread"), PyNone.Instance, PyNone.Instance });
            module.ModuleDict["meta_path"] = new PyList(new PyObject[] { PyImportSystem.BuiltinFinder, PyImportSystem.PathFinder });
            module.ModuleDict["path_hooks"] = new PyList();
            module.ModuleDict["path_importer_cache"] = new PyDict();
            module.ModuleDict["hexversion"] = PyInt.From(HexVersion);
            module.ModuleDict["api_version"] = PyInt.From(1013);
            module.ModuleDict["copyright"] = PyString.From("Copyright (c) 2001-2026 Python Software Foundation.\nAll Rights Reserved.");
            module.ModuleDict["pycache_prefix"] = PyNone.Instance;
            module.ModuleDict["dont_write_bytecode"] = PyBool.FromBool(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PYTHONDONTWRITEBYTECODE")));
            module.ModuleDict["_git"] = PyTuple.From(new PyObject[] { PyString.From("SharpPy"), PyString.From(""), PyString.From("") });
            // _PyConfig_CreateXOptionsDict: True for a bare name, else the text after '='.
            var xoptions = new PyDict();
            foreach (var (name, value) in SharpPyConfig.XOptions)
                xoptions.SetItem(PyString.From(name), value == null ? PyBool.True : PyString.From(value));
            module.ModuleDict["_xoptions"] = xoptions;
            module.ModuleDict["_home"] = PyNone.Instance;
            module.ModuleDict["_vpath"] = PyString.From("");
            module.ModuleDict["_framework"] = PyString.From("");
            module.ModuleDict["_base_executable"] = PyString.From(GetExecutablePath());
            // config->stdlib_dir: where getpath found the standard library
            module.ModuleDict["_stdlib_dir"] = PyString.From(IOHelper.CombinePath(PrefixPath(), "Lib"));
            module.ModuleDict["is_finalizing"] = new PyBuiltinFunction("is_finalizing", args => PyBool.FromBool(PyGil.Finalizing));
            module.ModuleDict["get_asyncgen_hooks"] = new PyBuiltinFunction("get_asyncgen_hooks", args => AsyncgenHooks());
            module.ModuleDict["set_asyncgen_hooks"] = new PyBuiltinFunction("set_asyncgen_hooks", SetAsyncgenHooks);
            module.ModuleDict["call_tracing"] = new PyBuiltinFunction("call_tracing", args =>
            {
                if (args[1] is not PyTuple arguments)
                    throw PyArgs.BadArgument("call_tracing", "argument 2", "tuple", args[1]);
                return args[0].Call(arguments.Items, null);
            });
            module.ModuleDict["_clear_type_cache"] = new PyBuiltinFunction("_clear_type_cache", args => PyNone.Instance);
            module.ModuleDict["_current_frames"] = new PyBuiltinFunction("_current_frames", args => CurrentFrames());
            module.ModuleDict["_getframemodulename"] = new PyBuiltinFunction("_getframemodulename", GetFrameModuleName);
            if (windows)
            {
                module.ModuleDict["winver"] = PyString.From("3.12");
                module.ModuleDict["getwindowsversion"] = new PyBuiltinFunction("getwindowsversion", args => WindowsVersion());
            }

            // 기본 인코딩
            module.ModuleDict["getdefaultencoding"] = new PySysFunction("getdefaultencoding");
            module.ModuleDict["getfilesystemencoding"] = new PySysFunction("getfilesystemencoding");

            // 종료 함수
            module.ModuleDict["exit"] = new PySysFunction("exit");

            // sys.stdin, stdout and stderr come later, from InitStdStreams (init_sys_streams).
            Current = module;
            // getpath: with no venv, base_prefix and base_exec_prefix are prefix and exec_prefix.
            foreach (var prefix in new[] { "base_prefix", "base_exec_prefix" })
                module.ModuleDict[prefix] = PyString.From(PrefixPath());

            // sys.argv is a copy of what the host put in PyConfig.argv
            // (Python/sysmodule.c:3612 COPY_LIST("argv", config->argv)). Program.Main fills it for the
            // CLI; an embedded host such as the Godot probe leaves PyArgv at its empty default, which
            // is what an embedding that sets no argv produces in CPython as well.
            var argvValues = SharpPy.Core.PyArgv.Values;
            var argvItems = new PyObject[argvValues.Length];
            for (int i = 0; i < argvValues.Length; i++) argvItems[i] = PyString.From(argvValues[i]);
            module.ModuleDict["argv"] = new PyList(argvItems);
            var original = new List<PyObject> { PyString.From(GetExecutablePath()) };
            original.AddRange(argvItems);
            module.ModuleDict["orig_argv"] = new PyList(original.ToArray());

            // CPython 3.12: sys.builtin_module_names (tuple of builtin C module names)
            module.ModuleDict["builtin_module_names"] = CreateBuiltinModuleNames();
            module.ModuleDict["float_repr_style"] = PyString.From("short");
            module.ModuleDict["maxunicode"] = PyInt.From(0x10FFFF);

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
                PyString.From(System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows) ? "surrogatepass" : "surrogateescape"));
            module.ModuleDict["intern"] = new PySysFunction("intern");
            // sys_getswitchinterval_impl: 1e-6 * _PyEval_GetSwitchInterval(), the microseconds kept.
            module.ModuleDict["getswitchinterval"] = new PyBuiltinFunction("getswitchinterval", args => new PyFloat(1e-6 * _switchMicroseconds));
            // sys_setswitchinterval (METH_O, PyFloat_AsDouble) and _PyEval_SetSwitchInterval, which
            // keeps (unsigned long)(1e6 * interval), 32 bits here.
            module.ModuleDict["setswitchinterval"] = new PyBuiltinFunction("setswitchinterval", args =>
            {
                double interval = args[0] is PyFloat { IsExact: true } exact ? exact.Value : SharpPy.Modules.PyMathFunction.RealNumber(args[0]);
                if (interval <= 0.0)
                    throw PyValueError.Create("switch interval must be strictly positive");
                _switchMicroseconds = unchecked((uint)(ulong)(1e6 * interval));
                PyGil.SwitchInterval = 1e-6 * _switchMicroseconds;
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
                PyString.From("sys"),
                PyString.From("builtins"),
                PyString.From("math"),
                PyString.From("time"),
                PyString.From("itertools"),
                PyString.From("_collections"),
                PyString.From("_functools"),
                PyString.From("_random"),
                PyString.From("_heapq"),
                PyString.From("_bisect"),
                PyString.From(System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows) ? "nt" : "posix"),
            };
            return PyTuple.From(names.ToArray());
        }

        private static PyObject CreateImplementation()
        {
            // CPython 3.12: Python/sysmodule.c:3193-3246 (make_impl_info)
            // sys.implementation is a SimpleNamespace object with interpreter details
            // Created via _PyNamespace_New(impl_info) in CPython
            var kwargs = new PyDict();
            // Observable parity with the reference CPython 3.12.15 runtime.
            kwargs.SetItem(PyString.From("name"), PyString.From("cpython"));
            kwargs.SetItem(PyString.From("version"), CreateVersionInfo());
            kwargs.SetItem(PyString.From("hexversion"), PyInt.From(HexVersion));
            // Its own tag, so a cache SharpPy writes never takes the name of one CPython reads.
            kwargs.SetItem(PyString.From("cache_tag"), PyString.From(PyBytecodeCache.Tag));

            return new PySimpleNamespace(kwargs);
        }

        internal static PyList CreateSysPath()
        {
            var pathList = new List<PyObject>();

            // Godot compiles sources into its own assembly/cache directory;
            // bundled Python modules live in the project resources instead.
#if GODOT
            pathList.Add(PyString.From("res://"));
            var exeDir = "res://";
            var projectRoot = "res://";
#else
            pathList.Add(PyString.From("."));
            var exeDir = IOHelper.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            var projectRoot = PrefixPath();
#endif

            // getpath.py: PYTHONPATH comes before the standard library.
            var pythonPath = Environment.GetEnvironmentVariable("PYTHONPATH");
            if (!string.IsNullOrEmpty(pythonPath))
            {
                var paths = pythonPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
                foreach (var path in paths)
                {
                    pathList.Add(PyString.From(path.Trim()));
                }
            }

            // 3. 표준 라이브러리 경로들 (CPython 호환 순서)
            // CPython 3.12: Lib 디렉토리만 사용 (stdlib 폴더 삭제됨)
            pathList.Add(PyString.From(IOHelper.CombinePath(projectRoot, "Lib")));

            // 3순위: modules 디렉토리 (SharpPy 전용 C# 구현 모듈) - 프로젝트 루트에서
            pathList.Add(PyString.From(IOHelper.CombinePath(projectRoot, "modules")));

            // 4순위: 실행 파일 디렉토리
            pathList.Add(PyString.From(exeDir));

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
            PyInt.From(3), PyInt.From(12), PyInt.From(15), PyString.From("final"), PyInt.From(0),
        });

        // PyFloat_GetInfo
        private static PyObject FloatInfo() => new PyStructSequence(new PyStructSequenceType("sys", "float_info", new[]
        {
            "max", "max_exp", "max_10_exp", "min", "min_exp", "min_10_exp", "dig", "mant_dig", "epsilon", "radix", "rounds",
        }), new PyObject[]
        {
            new PyFloat(double.MaxValue), PyInt.From(1024), PyInt.From(308), new PyFloat(2.2250738585072014e-308), PyInt.From(-1021),
            PyInt.From(-307), PyInt.From(15), PyInt.From(53), new PyFloat(2.220446049250313e-16), PyInt.From(2), PyInt.From(1),
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
                values[i] = PyInt.From(fields[i] switch
                {
                    "dont_write_bytecode" => string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PYTHONDONTWRITEBYTECODE")) ? 0 : 1,
                    "no_site" or "hash_randomization" => 1,
                    "int_max_str_digits" => 4300,
                    _ => 0,
                });
            values[Array.IndexOf(fields, "dev_mode")] = PyBool.FromBool(SharpPyConfig.DevMode);
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
                    result.SetItem(PyInt.From(state.Ident), state.Frames.Peek());
            });
            return result;
        }

        // sys__getframemodulename_impl
        private static PyObject GetFrameModuleName(PyObject[] args, PyDict kwargs)
        {
            // sys__getframemodulename's clinic: (depth=0), the int converter.
            var depthArgument = PyArgs.Unpack("_getframemodulename", args, kwargs, new[] { "depth" }, 0, 0, 1)[0];
            int depth = depthArgument == null ? 0 : PyArgs.Int(depthArgument);
            var frame = PyVM.CurrentFrame;
            while (frame != null && depth-- > 0)
                frame = frame.ParentFrame;
            if (frame == null)
                return PyNone.Instance;
            var globals = frame.GetAttribute("f_globals");
            var name = PyString.From("__name__");
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
                PyInt.From(version.Major), PyInt.From(version.Minor), PyInt.From(version.Build), PyInt.From(2),
                PyString.From(Environment.OSVersion.ServicePack), PyInt.From(0), PyInt.From(0), PyInt.From(0x100), PyInt.From(1),
                PyTuple.From(new PyObject[] { PyInt.From(version.Major), PyInt.From(version.Minor), PyInt.From(version.Build) }),
            });
        }

        private static string GetExecutablePath()
        {
            return Assembly.GetExecutingAssembly().Location;
        }

        // getpath's prefix: the directory whose Lib is the standard library.
        private static string PrefixPath()
        {
#if GODOT
            return "res://";
#else
            var exeDir = IOHelper.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            return IOHelper.GetFullPath(IOHelper.CombinePath(exeDir, "..", "..", ".."));
#endif
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
            CheckConvention(args, kwargs);
            if (Name is "get_int_max_str_digits" or "set_int_max_str_digits")
            {
                if (Name == "get_int_max_str_digits")
                {
                    if (kwargs != null && kwargs.Length() != 0)
                        throw PyTypeError.Create("sys.get_int_max_str_digits() takes no keyword arguments");
                    if (args.Length != 0) throw PyTypeError.Create($"sys.get_int_max_str_digits() takes no arguments ({args.Length} given)");
                    return PyInt.From(PyVM.Instance.IntMaxStrDigits);
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
                if (argument == null) throw PyArgs.MissingArgument("set_int_max_str_digits", "maxdigits", 1);
                var index = PyObject.IndexOrThrow(argument);
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
            
            return PyString.From("utf-8");
        }

        private PyObject CallGetFilesystemEncoding(PyObject[] args)
        {
            
            return PyString.From("utf-8");
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

        // sys_getsizeof and _PySys_GetSizeOf (Python/sysmodule.c): the object's __sizeof__, looked up
        // on its type, as a non-negative Py_ssize_t, and sizeof(PyGC_Head) for an object the collector
        // tracks the type of. With a default, a TypeError gives the default instead.
        private PyObject CallGetSizeOf(PyObject[] args)
        {
            if (args.Length < 1 || args.Length > 2)
                throw PyTypeError.Create(args.Length < 1 ? "getsizeof() missing required argument 'object' (pos 1)"
                    : $"getsizeof() takes at most 2 arguments ({args.Length} given)");
            var obj = args[0];
            try
            {
                var method = obj.GetPyType().LookupSpecial("__sizeof__")
                    ?? throw PyTypeError.Create($"Type {obj.GetTypeName()} doesn't define __sizeof__");
                var result = obj.CallSpecial(method);
                // _PySys_GetSizeOf: PyLong_AsSsize_t of what __sizeof__ answered.
                long size = PyArgs.LongAsSsize(result);
                if (size < 0)
                    throw PyValueError.Create("__sizeof__() should return >= 0");
                // _PyType_PreHeaderSize: sizeof(PyGC_Head) for what the collector tracks (a type
                // object always is: sys.getsizeof(int) is 432), and the two words of a managed dict.
                var kind = obj.GetPyType();
                bool gc = obj is PyType || kind is PyClass || kind.Layout.Gc;
                bool managedDict = obj is not PyType && kind is PyClass heap && heap.InstancesHaveDict();
                return PyInt.From(size + (gc ? 16 : 0) + (managedDict ? 16 : 0));
            }
            catch (PythonException error) when (args.Length == 2 && error.PyException is PyTypeError)
            {
                return args[1];
            }
        }

        private PyObject CallGetRefCount(PyObject[] args)
        {

            // C#에서는 참조 카운팅을 직접 구현하지 않으므로 더미 값 반환
            return PyInt.From(1);
        }

        /// <summary>
        /// CPython 3.12: Python/sysmodule.c:898-914 (sys_exc_info_impl)
        /// Return current exception information: (type, value, traceback).
        /// Traverses the frame stack to find the topmost exception.
        /// </summary>
        private PyObject CallExcInfo(PyObject[] args)
        {

            // CPython 3.12: Python/sysmodule.c:902
            // _PyErr_StackItem *err_info = _PyErr_GetTopmostException(_PyThreadState_GET());
            var exception = PyVM.GetTopmostException();

            if (exception == null)
            {
                // CPython 3.12: Python/sysmodule.c:903-905
                // No exception: return (None, None, None)
                return PyTuple.From(PyNone.Instance, PyNone.Instance, PyNone.Instance);
            }

            // CPython 3.12: Python/sysmodule.c:907-913
            // exc_info() returns (type, value, traceback)
            var excType = exception.GetPyType();  // Exception type (class)
            PyObject excValue = exception;

            // Get traceback from exception's __traceback__ attribute
            PyObject excTraceback = PyNone.Instance;
            if (exception.__traceback__ != null)
            {
                excTraceback = exception.__traceback__;
            }

            return PyTuple.From(excType, excValue, excTraceback);
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
                { PyString.From($"Ignoring unimportable $PYTHONBREAKPOINT: \"{setting}\""), PyType.RuntimeWarningType }, null);
            return PyNone.Instance;
        }

        private static PyObject ImportForHook(string path) =>
            PyImportSystem.Import("importlib").GetAttribute("import_module").Call(new PyObject[] { PyString.From(path) }, null);

        private PyObject CallExceptHook(PyObject[] args)
        {
            // sys_excepthook_impl: PyErr_Display(NULL, value, traceback), the C writer (traceback.py is
            // the library's formatter, and in 3.12 it draws some locations differently).
            PyErrorDisplay.Display(args[1], args[2]);
            return PyNone.Instance;
        }

        private PyObject CallGetRecursionLimit(PyObject[] args)
        {
            return PyInt.From(RecursionLimitValue);
        }

        private PyObject CallSetRecursionLimit(PyObject[] args)
        {
            var idx = PyObject.IndexOrThrow(args[0]);
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

        // sys_intern: METH_O, a str (clinic), and then an exact one (sys_intern_impl).
        private PyObject CallIntern(PyObject[] args)
        {
            if (args[0] is not PyString s)
                throw PyArgs.BadArgument("intern", "argument", "str", args[0]);
            if (!s.IsExact)
                throw PyTypeError.Create($"can't intern {s.GetPyType().FullName}");
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
            // sys__getframe's clinic: depth through the int converter.
            int depth = args.Length > 0 ? PyArgs.Int(args[0]) : 0;

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

            // sys_displayhook, Python/sysmodule.c
            var value = args[0];
            if (value is PyNone)
                return PyNone.Instance;
            var builtins = PyBuiltinsModule.Instance;
            builtins.SetAttribute("_", PyNone.Instance);
            // _PySys_GetAttr: a missing attribute is lost the same as None.
            if (!PyImportSystem.TryGetModule("sys", out var sys) || !sys.ModuleDict.TryGetValue("stdout", out var stdout) || stdout is PyNone)
                throw PyRuntimeError.Create("lost sys.stdout");
            var write = stdout.GetAttribute("write");
            write.Call(new PyObject[] { value.ToRepr() }, null);
            write.Call(new PyObject[] { PyString.From("\n") }, null);
            builtins.SetAttribute("_", value);
            return PyNone.Instance;
        }
    }

    // CPython 3.12: sys.modules is a regular PyDict (Python/import.c:185)
    // No need for custom PySysModules class - use PyDict directly

}
