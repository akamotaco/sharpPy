#if GODOT
using IOHelper = Godot_IO.Helper;
#else
using IOHelper = DotNet_IO.Helper;
#endif
using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// The import route of Lib/importlib/_bootstrap.py, with the searches in C#: _bootstrap.py itself
    /// runs as _frozen_importlib (ModuleSpec, BuiltinImporter, module_from_spec, _init_module_attrs,
    /// _load_unlocked are CPython's code) over the _imp module (Modules/ImpModule.cs); PathFinder and
    /// its loaders are C#, so files, SharpPy bundles and Godot's res:// and user:// paths are found
    /// and read by the code that always did. Every finder of sys.meta_path answers a ModuleSpec, and
    /// every module is loaded from one through _load_unlocked.
    /// </summary>
    public partial class PyImportSystem
    {
        private static bool _bootstrapped, _bootstrapping;

        /// <summary>_frozen_importlib: Lib/importlib/_bootstrap.py, after _setup.</summary>
        internal static PyModule Bootstrap { get; private set; }

        /// <summary>_frozen_importlib_external: the globals the C# finder and loader belong to, whatever sys.modules holds.</summary>
        internal static PyModule External { get; private set; }

        /// <summary>
        /// remove_importlib_frames (Python/import.c): the chunks of a failed import's traceback that
        /// are importlib's own frames go - all of them for an ImportError, else the chunks that end in
        /// _bootstrap._call_with_frames_removed.
        /// </summary>
        internal static void RemoveImportlibFrames(PyBaseException exception)
        {
            bool alwaysTrim = exception.GetPyType().IsSubclassOf(PyType.ImportErrorType);
            PyTraceback outer = null;
            bool outerIsHead = false, inImportlib = false;
            PyTraceback previous = null;
            for (var traceback = exception.__traceback__; traceback != null;)
            {
                var next = traceback.Next;
                string file = traceback.Frame?.Code?.FileName;
                bool nowInImportlib = file == "<frozen importlib._bootstrap>" || file == "<frozen importlib._bootstrap_external>";
                if (nowInImportlib && !inImportlib)
                {
                    // the link to this chunk: the exception's own, or the previous entry's tb_next
                    outer = previous;
                    outerIsHead = previous == null;
                }
                inImportlib = nowInImportlib;
                if (inImportlib && (alwaysTrim || traceback.Frame?.Code?.Name == "_call_with_frames_removed"))
                {
                    if (outerIsHead)
                        exception.__traceback__ = next;
                    else
                        outer.Next = next;
                    previous = outer;
                }
                else
                    previous = traceback;
                traceback = next;
            }
        }

        internal static bool IsBuiltinModule(string name) => _builtinModules.ContainsKey(name) && !OtherPlatform(name);

        // nt and posix are one module, named for the platform.
        private static bool OtherPlatform(string name) =>
            name == (OperatingSystem.IsWindows() ? "posix" : "nt");

        /// <summary>sys.builtin_module_names: the table's names, sorted.</summary>
        internal static List<string> BuiltinModuleNames()
        {
            var names = new List<string>();
            foreach (var name in _builtinModules.Keys)
                if (!OtherPlatform(name))
                    names.Add(name);
            names.Sort(string.CompareOrdinal);
            return names;
        }

        /// <summary>_imp.create_builtin: the module the interpreter makes for name, not yet in sys.modules.</summary>
        internal static PyModule CreateBuiltin(string name)
        {
            if ((name == "sys" || name == "builtins") && TryGetModule(name, out var existing))
                return existing;
            if (OtherPlatform(name) || !_builtinModules.TryGetValue(name, out var factory))
                return null;
            var module = factory();
            PyDocs.SetTypeDocs();
            foreach (var value in module.ModuleDict.Values)
                if (value is PyBuiltinFunction function)
                    function.OwnerModule = name;
            return module;
        }

        /// <summary>
        /// pycore_interp_init's init_importlib: _frozen_importlib executed, _setup(sys, _imp), and
        /// sys.meta_path = [BuiltinImporter, FrozenImporter, PathFinder]. Run before the first module
        /// that is not yet in sys.modules is found.
        /// </summary>
        private static void EnsureBootstrapped()
        {
            if (_bootstrapped || _bootstrapping)
                return;
            _bootstrapping = true;
            try
            {
                var sys = Import("sys");
                Import("builtins");
                var imp = Import("_imp");
                // _setup takes _warnings from sys.modules; SharpPy's is Lib/_warnings.py.
                Import("_warnings");
                string path = LocateInSysPath("importlib/_bootstrap.py");
                string externalPath = LocateInSysPath("importlib/_external_functions.py");
                if (path == null || externalPath == null)
                    return;
                // Frozen modules' code is named "<frozen importlib._bootstrap>" and
                // "<frozen importlib._bootstrap_external>", as Python/frozen.c names it.
                var boot = new PyModule("_frozen_importlib");
                SetModule("_frozen_importlib", boot);
                boot.ExecuteCode(Compiler.CompileSource(ReadSource(path), "<frozen importlib._bootstrap>"));
                // _bootstrap_external's functions run in the module that holds the C# finder and loaders;
                // _install_external_importers's _set_bootstrap_module, then _bootstrap._bootstrap_external.
                var external = ExternalModule();
                External = external;
                external.ModuleDict["_bootstrap"] = boot;
                SetModule("_frozen_importlib_external", external);
                external.ExecuteCode(Compiler.CompileSource(ReadSource(externalPath), "<frozen importlib._bootstrap_external>"));
                // _install(_bootstrap_module): _set_bootstrap_module first, as the module's own global is None.
                external.GetAttribute("_set_bootstrap_module").Call(new PyObject[] { boot }, null);
                boot.ModuleDict["_bootstrap_external"] = external;
                boot.GetAttribute("_setup").Call(new PyObject[] { sys, imp }, null);
                // _install: sys.path_hooks gets FileFinder's hook for the supported loaders, and
                // sys.meta_path PathFinder; _PyImportZip_Init's zipimporter has its counterpart in
                // the bundle finder, put first as zipimporter is.
                var pathFinder = external.GetAttribute("PathFinder");
                if (sys.ModuleDict.TryGetValue("meta_path", out var metaPath) && metaPath is PyList finders)
                {
                    finders.Clear();
                    finders.Append(boot.GetAttribute("BuiltinImporter"));
                    finders.Append(boot.GetAttribute("FrozenImporter"));
                    finders.Append(pathFinder);
                }
                if (sys.ModuleDict.TryGetValue("path_hooks", out var pathHooks) && pathHooks is PyList hooks)
                {
                    hooks.Clear();
                    hooks.Append(PyBundleFinder.FinderType);
                    hooks.Append(PyFileFinder.PathHook(PyFileFinder.FinderType, new PyObject[]
                        { PyTuple.From(PySourceFileLoader.LoaderType, external.GetAttribute("SOURCE_SUFFIXES")) }));
                }
                Bootstrap = boot;
                _bootstrapped = true;
                // _setup gave the builtins their specs; a source module read before it gets its own.
                var findSpec = pathFinder.GetAttribute("find_spec");
                foreach (var pair in SysModules.InternalDict)
                    if (pair.Key is PyString moduleName && pair.Value is PyModule { HasSourceFile: true } early
                        && !early.ModuleDict.ContainsKey("__spec__") && findSpec.Call(new PyObject[] { moduleName }, null) is { } found and not PyNone)
                        boot.GetAttribute("_init_module_attrs").Call(new[] { found, early }, null);
            }
            finally
            {
                _bootstrapping = false;
            }
        }

        internal static string LocateSource(string relative) => LocateInSysPath(relative);

        internal static string ReadSource(string path) => Generated.Tokenizer.DecodeFile(IOHelper.ReadAllBytes(path), path);

        private static string LocateInSysPath(string relative)
        {
            var sysPath = GetSysPath();
            if (sysPath == null)
                return null;
            foreach (var entry in sysPath.Items)
                if (entry is PyString directory && !PyBundle.IsBundlePath(directory.Value))
                {
                    var candidate = IOHelper.CombinePath(directory.Value, relative);
                    if (IOHelper.FileExists(candidate))
                        return candidate;
                }
            return null;
        }


        // While _frozen_importlib is being made: the interpreter's own modules only.
        private static PyModule LoadBeforeBootstrap(string name)
        {
            var module = CreateBuiltin(name);
            if (module != null)
                SetModule(name, module);
            return module ?? (name.Contains('.') ? null : LoadSourceWithoutSpec(name));
        }

        // A source module needed before _frozen_importlib exists (its own path's modules): loaded
        // straight from sys.path, then given its spec by _setup's pass over sys.modules if builtin.
        private static PyModule LoadSourceWithoutSpec(string name)
        {
            string path = LocateInSysPath(name + ".py");
            if (path == null)
                return null;
            var module = new PyModule(name, path) { HasSourceFile = true };
            SetModule(name, module);
            try
            {
                module.ExecuteCode(PyBytecodeCache.TryLoad(path)
                    ?? Compiler.CompileSource(Generated.Tokenizer.DecodeFile(IOHelper.ReadAllBytes(path), path), path));
                return module;
            }
            catch
            {
                RemoveModule(name);
                throw;
            }
        }

        // _frozen_importlib_external: the C# half of Lib/importlib/_bootstrap_external.py, what finds
        // and reads files; Lib/importlib/_external_functions.py, run in it, is CPython's rest.
        private static PyModule ExternalModule()
        {
            var module = new PyModule("_frozen_importlib_external");
            module.ModuleDict["FileFinder"] = PyFileFinder.FinderType;
            module.ModuleDict["SourceFileLoader"] = PySourceFileLoader.LoaderType;
            module.ModuleDict["SOURCE_SUFFIXES"] = new PyList(new PyObject[] { PyString.From(".py") });
            module.ModuleDict["BYTECODE_SUFFIXES"] = new PyList(new PyObject[] { PyString.From(".pyc") });
            module.ModuleDict["EXTENSION_SUFFIXES"] = new PyList();
            module.ModuleDict["MAGIC_NUMBER"] = PyBytes.From(new byte[] { 0xcb, 0x0d, 0x0d, 0x0a });
            return module;
        }

        private static PyObject NewSpec(string name, PyObject loader, string origin, bool isPackage) =>
            Bootstrap.GetAttribute("ModuleSpec").Call(new PyObject[] { PyString.From(name), loader },
                Kwargs(("origin", origin == null ? PyNone.Instance : PyString.From(origin)), ("is_package", PyBool.FromBool(isPackage))));

        internal static PyDict Kwargs(params (string Name, PyObject Value)[] pairs)
        {
            var kwargs = new PyDict();
            foreach (var (key, value) in pairs)
                kwargs.SetItem(PyString.From(key), value);
            return kwargs;
        }

        // spec_from_file_location for SourceFileLoader: has_location, the cache's path, a package's
        // own directory as its submodule_search_locations.
        internal static PyObject FileSpec(string name, string file, string packageDir, PyBundle bundle, string bundleName = null)
        {
            var loader = new PySourceFileLoader(name, file, bundle, bundleName);
            var spec = NewSpec(name, loader, file, packageDir != null);
            spec.SetAttribute("has_location", PyBool.True);
            string cached = bundle == null ? PyBytecodeCache.CachePathFor(file) : null;
            spec.SetAttribute("cached", cached == null ? PyNone.Instance : PyString.From(cached));
            if (packageDir != null)
                spec.SetAttribute("submodule_search_locations", new PyList(new PyObject[] { PyString.From(packageDir) }));
            return spec;
        }
    }

    /// <summary>
    /// FileFinder (Lib/importlib/_bootstrap_external.py) over IOHelper, so that res:// and user://
    /// are directories like any other: the names of its directory, listed again when its mtime
    /// changes (_fill_cache), are what a module name must match exactly; then a package directory
    /// with __init__ and a suffix, a module file, and last a namespace portion.
    /// </summary>
    internal sealed class PyFileFinder : PyObject
    {
        internal static readonly PyType FinderType = MakeType();

        internal string Path = "";
        private readonly List<(string Suffix, PyObject Loader)> _loaders = new();
        private double _pathMtime = -1;
        private HashSet<string> _pathCache = new();

        private PyFileFinder(PyType type)
        {
            if (!ReferenceEquals(type, FinderType))
                SetCustomType(type);
        }

        public override PyType GetPyType() => _customType ?? FinderType;

        private static PyModule External => PyImportSystem.External;

        private static bool IsGodotPath(string path) =>
            path.StartsWith("res://", StringComparison.Ordinal) || path.StartsWith("user://", StringComparison.Ordinal);

        private static string Getcwd() =>
            ((PyString)PyImportSystem.Import(OperatingSystem.IsWindows() ? "nt" : "posix").GetAttribute("getcwd").Call(Array.Empty<PyObject>(), null)).Value;

        // _path_join: Godot's paths join with /, the rest as CPython's helper does.
        private static string Join(string directory, string name) => IsGodotPath(directory)
            ? IOHelper.CombinePath(directory, name)
            : ((PyString)External.GetAttribute("_path_join").Call(new PyObject[] { PyString.From(directory), PyString.From(name) }, null)).Value;

        // FileFinder.__init__
        private void Init(string path, PyObject[] loaderDetails)
        {
            _loaders.Clear();
            foreach (var detail in loaderDetails)
            {
                var pair = new List<PyObject>(PySet.IterableItems(detail));
                if (pair.Count != 2)
                    throw PyValueError.Create(pair.Count < 2 ? $"not enough values to unpack (expected 2, got {pair.Count})" : "too many values to unpack (expected 2)");
                foreach (var suffix in PySet.IterableItems(pair[1]))
                    _loaders.Add((((PyString)suffix).Value, pair[0]));
            }
            if (string.IsNullOrEmpty(path) || path == ".")
                Path = Getcwd();
            else if (IsGodotPath(path))
                Path = path;
            else
                Path = ((PyString)External.GetAttribute("_path_abspath").Call(new PyObject[] { PyString.From(path) }, null)).Value;
            _pathMtime = -1;
            _pathCache = new HashSet<string>();
        }

        // _path_stat(self.path or _os.getcwd()).st_mtime, -1 when it cannot be read; Godot's
        // directories have no mtime, so res:// (read-only) is listed once and user:// every time.
        private double DirectoryMtime()
        {
            if (Path.StartsWith("res://", StringComparison.Ordinal))
                return 0;
            if (Path.StartsWith("user://", StringComparison.Ordinal))
                return double.NaN;
            try
            {
                var info = new System.IO.DirectoryInfo(Path.Length == 0 ? Getcwd() : Path);
                return info.Exists ? (info.LastWriteTimeUtc - DateTime.UnixEpoch).TotalSeconds : -1;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        // _fill_cache: the directory's names; none when it cannot be listed.
        private void FillCache()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                foreach (var entry in IOHelper.GetFileSystemEntries(Path.Length == 0 ? Getcwd() : Path))
                    names.Add(System.IO.Path.GetFileName(entry.TrimEnd('/', '\\')));
            }
            catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException or ArgumentException)
            {
            }
            _pathCache = names;
        }

        // FileFinder.find_spec
        private PyObject FindSpec(string fullName)
        {
            bool isNamespace = false;
            string tailModule = fullName.Substring(fullName.LastIndexOf('.') + 1);
            double mtime = DirectoryMtime();
            if (double.IsNaN(mtime) || mtime != _pathMtime)
            {
                FillCache();
                _pathMtime = mtime;
            }
            string basePath = null;
            if (_pathCache.Contains(tailModule))
            {
                basePath = Join(Path, tailModule);
                foreach (var (suffix, loader) in _loaders)
                {
                    string fullPath = Join(basePath, "__init__" + suffix);
                    if (IOHelper.FileExists(fullPath))
                        return GetSpec(loader, fullName, fullPath, basePath);
                }
                isNamespace = IOHelper.DirExists(basePath);
            }
            foreach (var (suffix, loader) in _loaders)
            {
                string fullPath;
                try
                {
                    fullPath = Join(Path, tailModule + suffix);
                }
                catch (PythonException error) when (error.PyException is PyValueError)
                {
                    // an embedded NUL: no such module here
                    return PyNone.Instance;
                }
                if (_pathCache.Contains(tailModule + suffix) && IOHelper.FileExists(fullPath))
                    return GetSpec(loader, fullName, fullPath, null);
            }
            if (isNamespace)
            {
                var spec = PyImportSystem.Bootstrap.GetAttribute("ModuleSpec").Call(new PyObject[] { PyString.From(fullName), PyNone.Instance }, null);
                spec.SetAttribute("submodule_search_locations", new PyList(new PyObject[] { PyString.From(basePath) }));
                return spec;
            }
            return PyNone.Instance;
        }

        // FileFinder._get_spec: loader_class(fullname, path), then spec_from_file_location; SharpPy's
        // own SourceFileLoader makes its spec in C#, which keeps res:// paths as they are.
        private static PyObject GetSpec(PyObject loaderClass, string fullName, string path, string packageDir)
        {
            if (ReferenceEquals(loaderClass, PySourceFileLoader.LoaderType))
                return PyImportSystem.FileSpec(fullName, path, packageDir, null);
            var loader = loaderClass.Call(new PyObject[] { PyString.From(fullName), PyString.From(path) }, null);
            return External.GetAttribute("spec_from_file_location").Call(new PyObject[] { PyString.From(fullName), PyString.From(path) },
                PyImportSystem.Kwargs(("loader", loader), ("submodule_search_locations",
                    packageDir == null ? PyNone.Instance : new PyList(new PyObject[] { PyString.From(packageDir) }))));
        }

        /// <summary>
        /// FileFinder.path_hook: path_hook_for_FileFinder, which takes a directory only
        /// (ImportError 'only directories are supported' otherwise) and makes cls(path, *loader_details).
        /// </summary>
        internal static PyObject PathHook(PyObject cls, PyObject[] loaderDetails) =>
            new PyBuiltinFunction("path_hook_for_FileFinder", (args, kwargs) =>
            {
                var parsed = PyArgs.Unpack("path_hook_for_FileFinder", args, kwargs, new[] { "path" }, 0, 1, 1);
                var path = parsed[0];
                if (path is not PyString text || !IOHelper.DirExists(text.Value.Length == 0 ? Getcwd() : text.Value))
                    throw ImportErrorWithPath("only directories are supported", path);
                var arguments = new PyObject[loaderDetails.Length + 1];
                arguments[0] = path;
                loaderDetails.CopyTo(arguments, 1);
                return cls.Call(arguments, null);
            });

        internal static Exception ImportErrorWithPath(string message, PyObject path) =>
            new PythonException((PyBaseException)PyType.ImportErrorType.Call(new PyObject[] { PyString.From(message) },
                PyImportSystem.Kwargs(("path", path))));

        private static PyType MakeType()
        {
            var type = new PyType("FileFinder", new[] { PyType.ObjectType }, "_frozen_importlib_external");
            type.TypeDict["__module__"] = PyString.From("_frozen_importlib_external");
            type.TypeDict["__doc__"] = PyString.From("File-based finder.\n\n    Interactions with the file system are cached for performance, being\n    refreshed when the directory the finder is handling has been modified.\n\n    ");
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) => new PyFileFinder((PyType)cls), 0, int.MaxValue, acceptsKwargs: true);
            type.TypeDict["__init__"] = new PyWrapperDescriptor("__init__", type, (self, args, kwargs) =>
            {
                if (args.Length < 1)
                    throw PyTypeError.Create("FileFinder.__init__() missing 1 required positional argument: 'path'");
                var rest = new PyObject[args.Length - 1];
                Array.Copy(args, 1, rest, 0, rest.Length);
                ((PyFileFinder)self).Init(PyArgs.FSDecoder(args[0]), rest);
                return PyNone.Instance;
            });
            type.TypeDict["path"] = new PyMemberDescriptor("path", type, self => PyString.From(((PyFileFinder)self).Path),
                (self, value) => ((PyFileFinder)self).Path = PyArgs.FSDecoder(value));
            type.TypeDict["_loaders"] = new PyMemberDescriptor("_loaders", type, self =>
            {
                var list = new PyList();
                foreach (var (suffix, loader) in ((PyFileFinder)self)._loaders)
                    list.Append(PyTuple.From(PyString.From(suffix), loader));
                return list;
            }, readOnly: true);
            type.TypeDict["find_spec"] = new PyMethodDescriptor("find_spec", type, (self, args, kwargs) =>
            {
                var parsed = PyArgs.Unpack("find_spec", args, kwargs, new[] { "fullname", "target" }, 0, 1, 2);
                return ((PyFileFinder)self).FindSpec(((PyString)parsed[0]).Value);
            }, acceptsKwargs: true);
            type.TypeDict["invalidate_caches"] = new PyMethodDescriptor("invalidate_caches", type, (self, args, kwargs) =>
            {
                ((PyFileFinder)self)._pathMtime = -1;
                return PyNone.Instance;
            }, 0, 0);
            type.TypeDict["path_hook"] = new PyClassMethodDescriptor("path_hook", type,
                new PyBuiltinFunction("path_hook", (args, kwargs) =>
                {
                    var details = new PyObject[args.Length - 1];
                    Array.Copy(args, 1, details, 0, details.Length);
                    return PathHook(args[0], details);
                }));
            type.TypeDict["__repr__"] = new PyWrapperDescriptor("__repr__", type, (self, args, kwargs) =>
                PyString.From($"FileFinder({PyString.From(((PyFileFinder)self).Path).ToRepr().Value})"));
            return type;
        }
    }

    /// <summary>
    /// The path entry finder of a SharpPy bundle, on sys.path_hooks as zipimport.zipimporter is: it
    /// takes "x.spk" and "x.spk/pkg/sub" (a bundled package's __path__), and ImportError for any
    /// other entry, so that the next hook is asked.
    /// </summary>
    internal sealed class PyBundleFinder : PyObject
    {
        internal static readonly PyType FinderType = MakeType();

        private readonly PyBundle _bundle;
        private readonly string _entry, _prefix;

        private PyBundleFinder(PyBundle bundle, string entry, string prefix)
        {
            _bundle = bundle;
            _entry = entry;
            _prefix = prefix;
        }

        public override PyType GetPyType() => FinderType;

        private static PyObject New(PyObject path)
        {
            string entry = path is PyString text ? text.Value : null;
            int at = entry == null ? -1 : entry.IndexOf(PyBundle.Extension, StringComparison.Ordinal);
            int end = at + PyBundle.Extension.Length;
            if (at < 0 || (end < entry.Length && entry[end] != '/'))
                throw PyFileFinder.ImportErrorWithPath("not a SharpPy bundle", path);
            var bundle = PyBundle.Open(entry.Substring(0, end))
                ?? throw PyFileFinder.ImportErrorWithPath("no such bundle", path);
            string prefix = end < entry.Length ? entry.Substring(end + 1).Trim('/').Replace('/', '.') : "";
            return new PyBundleFinder(bundle, entry, prefix);
        }

        private PyObject FindSpec(string fullName)
        {
            string tail = fullName.Substring(fullName.LastIndexOf('.') + 1);
            string name = _prefix.Length == 0 ? tail : _prefix + "." + tail;
            if (!_bundle.Contains(name))
                return PyNone.Instance;
            string relative = name.Replace('.', '/');
            bool isPackage = _bundle.IsPackage(name);
            string file = $"{_bundle.Path}/{relative}{(isPackage ? "/__init__.py" : ".py")}";
            return PyImportSystem.FileSpec(fullName, file, isPackage ? $"{_bundle.Path}/{relative}" : null, _bundle, name);
        }

        private static PyType MakeType()
        {
            var type = new PyType("BundleFinder", new[] { PyType.ObjectType }, "sharppy");
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) =>
            {
                var parsed = PyArgs.Unpack("BundleFinder", args, kwargs, new[] { "path" }, 1, 1, 1);
                return New(parsed[0]);
            }, 0, int.MaxValue, acceptsKwargs: true);
            type.TypeDict["find_spec"] = new PyMethodDescriptor("find_spec", type, (self, args, kwargs) =>
            {
                var parsed = PyArgs.Unpack("find_spec", args, kwargs, new[] { "fullname", "target" }, 0, 1, 2);
                return ((PyBundleFinder)self).FindSpec(((PyString)parsed[0]).Value);
            }, acceptsKwargs: true);
            type.TypeDict["invalidate_caches"] = new PyMethodDescriptor("invalidate_caches", type, (self, args, kwargs) => PyNone.Instance, 0, 0);
            type.TypeDict["__repr__"] = new PyWrapperDescriptor("__repr__", type, (self, args, kwargs) =>
                PyString.From($"<BundleFinder object \"{((PyBundleFinder)self)._entry}\">"));
            return type;
        }
    }

    /// <summary>
    /// SourceFileLoader (Lib/importlib/_bootstrap_external.py) over SharpPy's reading of a file: the
    /// source decoded by the tokenizer's rules, the __pycache__ code when it is current, a module of a
    /// SharpPy bundle read from the bundle; Godot's res:// and user:// go through IOHelper.
    /// </summary>
    internal sealed class PySourceFileLoader : PyObject
    {
        internal static readonly PyType LoaderType = MakeType("SourceFileLoader", "_frozen_importlib_external");
        internal static readonly PyType BundleLoaderType = MakeType("BundleLoader", "sharppy");

        internal readonly string Name, Path;
        internal readonly PyBundle Bundle;
        // the module's name in the bundle, which a bundle entry with a prefix may give another name
        private readonly string _bundleName;

        internal PySourceFileLoader(string name, string path, PyBundle bundle, string bundleName = null)
        {
            Name = name;
            Path = path;
            Bundle = bundle;
            _bundleName = bundleName ?? name;
        }

        public override PyType GetPyType() => Bundle == null ? LoaderType : BundleLoaderType;

        private bool IsPackage => System.IO.Path.GetFileNameWithoutExtension(Path) == "__init__";

        private static PyType MakeType(string name, string module)
        {
            var type = new PyType(name, new[] { PyType.ObjectType }, module);
            PySourceFileLoader Self(PyObject self) => (PySourceFileLoader)self;
            // FileLoader.__init__(fullname, path)
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) =>
            {
                var parsed = PyArgs.Unpack(name, args, kwargs, new[] { "fullname", "path" }, 0, 2, 2);
                return new PySourceFileLoader(((PyString)parsed[0]).Value, PyArgs.FSDecoder(parsed[1]), null);
            }, 0, int.MaxValue, acceptsKwargs: true);
            type.TypeDict["name"] = new PyGetSetDescriptor("name", type, self => PyString.From(Self(self).Name));
            type.TypeDict["path"] = new PyGetSetDescriptor("path", type, self => PyString.From(Self(self).Path));
            // _LoaderBasics.create_module: None would do; the module SharpPy's tracebacks name its file by.
            type.TypeDict["create_module"] = new PyMethodDescriptor("create_module", type, (self, args, kwargs) =>
                new PyModule(Self(self).Name, Self(self).Path) { HasSourceFile = true, Bundle = Self(self).Bundle }, 1, 1);
            type.TypeDict["exec_module"] = new PyMethodDescriptor("exec_module", type, (self, args, kwargs) =>
            {
                Self(self).Exec((PyModule)args[0]);
                return PyNone.Instance;
            }, 1, 1);
            type.TypeDict["get_filename"] = new PyMethodDescriptor("get_filename", type, (self, args, kwargs) => PyString.From(Self(self).Path), 0, 1);
            type.TypeDict["is_package"] = new PyMethodDescriptor("is_package", type, (self, args, kwargs) => PyBool.FromBool(Self(self).IsPackage), 1, 1);
            type.TypeDict["get_source"] = new PyMethodDescriptor("get_source", type, (self, args, kwargs) =>
                Self(self).Bundle != null ? PyNone.Instance : PyString.From(Self(self).Source()), 1, 1);
            type.TypeDict["get_code"] = new PyMethodDescriptor("get_code", type, (self, args, kwargs) => Self(self).Code(), 1, 1);
            type.TypeDict["get_data"] = new PyMethodDescriptor("get_data", type, (self, args, kwargs) =>
                PyBytes.From(IOHelper.ReadAllBytes(PyArgs.FSDecoder(args[0]))), 1, 1);
            type.TypeDict["__repr__"] = new PyWrapperDescriptor("__repr__", type, (self, args, kwargs) =>
                PyString.From($"<{type.FullName} object at {PyObjectId.Pointer(self)}>"));
            return type;
        }

        private string Source() => Generated.Tokenizer.DecodeFile(IOHelper.ReadAllBytes(Path), Path);

        // SourceLoader.get_code: the cache, or the source compiled and then cached.
        private PyCodeObject Code()
        {
            if (Bundle != null)
                return Bundle.Code(_bundleName);
            var cached = PyBytecodeCache.TryLoad(Path);
            if (cached != null)
                return cached;
            var code = Compiler.CompileSource(Source(), Path);
            PyBytecodeCache.Store(Path, code);
            return code;
        }

        // _LoaderBasics.exec_module: get_code, then exec in the module's dict.
        private void Exec(PyModule module)
        {
            PyCodeObject code;
            try
            {
                code = Code();
            }
            catch (PySyntaxErrorException syntax)
            {
                // An import is Python code calling: its SyntaxError can be caught there.
                throw PySyntaxError.Create(syntax.Message.Replace("SyntaxError: ", ""), Path, Math.Max(syntax.LineNumber, 0));
            }
            // _LoaderBasics.exec_module: _bootstrap._call_with_frames_removed(exec, code, module.__dict__).
            var boot = PyImportSystem.Bootstrap;
            if (boot == null)
            {
                module.ExecuteCode(code);
                return;
            }
            boot.GetAttribute("_call_with_frames_removed").Call(new PyObject[]
                { PyImportSystem.Import("builtins").GetAttribute("exec"), code, module.DictObject }, null);
        }
    }
}
