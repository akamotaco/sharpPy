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
                external.ModuleDict["_bootstrap"] = boot;
                SetModule("_frozen_importlib_external", external);
                external.ExecuteCode(Compiler.CompileSource(ReadSource(externalPath), "<frozen importlib._bootstrap_external>"));
                boot.ModuleDict["_bootstrap_external"] = external;
                boot.GetAttribute("_setup").Call(new PyObject[] { sys, imp }, null);
                if (sys.ModuleDict.TryGetValue("meta_path", out var metaPath) && metaPath is PyList finders)
                {
                    finders.Clear();
                    finders.Append(boot.GetAttribute("BuiltinImporter"));
                    finders.Append(boot.GetAttribute("FrozenImporter"));
                    finders.Append(PathFinderType);
                }
                Bootstrap = boot;
                _bootstrapped = true;
                // _setup gave the builtins their specs; a source module read before it gets its own.
                foreach (var pair in SysModules.InternalDict)
                    if (pair.Key is PyString moduleName && pair.Value is PyModule { HasSourceFile: true } early
                        && !early.ModuleDict.ContainsKey("__spec__") && FindSpec(moduleName.Value, PyNone.Instance, null) is { } found)
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

        // ---- PathFinder (Lib/importlib/_bootstrap_external.py) ----

        /// <summary>_frozen_importlib_external.PathFinder: the class on sys.meta_path.</summary>
        internal static readonly PyType PathFinderType = MakePathFinderType();

        private static PyType MakePathFinderType()
        {
            var type = new PyType("PathFinder", new[] { PyType.ObjectType }, "_frozen_importlib_external");
            // PathFinder.find_spec(fullname, path=None, target=None), a classmethod there.
            type.TypeDict["find_spec"] = new PyBuiltinFunction("find_spec", (args, kwargs) =>
            {
                var parsed = PyArgs.Unpack("find_spec", args, kwargs, new[] { "fullname", "path", "target" }, 0, 1, 3);
                // A Python classmethod in CPython: any str is a name to look for (an embedded NUL just
                // finds nothing), not a clinic argument.
                string name = (parsed[0] as PyString)?.Value
                    ?? throw PyTypeError.Create($"find_spec() argument 'fullname' must be str, not {parsed[0].GetTypeName()}");
                // A package that lives in a bundle answers for its submodules.
                int dot = name.LastIndexOf('.');
                PyModule parent = null;
                if (dot > 0) TryGetModule(name.Substring(0, dot), out parent);
                return FindSpec(name, parsed[1] ?? PyNone.Instance, parent) ?? PyNone.Instance;
            });
            type.TypeDict["invalidate_caches"] = new PyBuiltinFunction("invalidate_caches", args => PyNone.Instance);
            return type;
        }

        // _frozen_importlib_external: the C# half of Lib/importlib/_bootstrap_external.py.
        private static PyModule ExternalModule()
        {
            var module = new PyModule("_frozen_importlib_external");
            module.ModuleDict["PathFinder"] = PathFinderType;
            module.ModuleDict["SourceFileLoader"] = PySourceFileLoader.LoaderType;
            module.ModuleDict["NamespaceLoader"] = PyNamespaceLoader.LoaderType;
            module.ModuleDict["SOURCE_SUFFIXES"] = new PyList(new PyObject[] { PyString.From(".py") });
            module.ModuleDict["BYTECODE_SUFFIXES"] = new PyList(new PyObject[] { PyString.From(".pyc") });
            module.ModuleDict["EXTENSION_SUFFIXES"] = new PyList();
            module.ModuleDict["MAGIC_NUMBER"] = PyBytes.From(new byte[] { 0xcb, 0x0d, 0x0d, 0x0a });
            return module;
        }

        private static PyObject NewSpec(string name, PyObject loader, string origin, bool isPackage) =>
            Bootstrap.GetAttribute("ModuleSpec").Call(new PyObject[] { PyString.From(name), loader },
                Kwargs(("origin", origin == null ? PyNone.Instance : PyString.From(origin)), ("is_package", PyBool.FromBool(isPackage))));

        private static PyDict Kwargs(params (string Name, PyObject Value)[] pairs)
        {
            var kwargs = new PyDict();
            foreach (var (key, value) in pairs)
                kwargs.SetItem(PyString.From(key), value);
            return kwargs;
        }

        // spec_from_file_location: has_location, the cache's path, a package's own directory.
        private static PyObject FileSpec(string name, string file, string packageDir, PyBundle bundle)
        {
            var loader = new PySourceFileLoader(name, file, bundle);
            var spec = NewSpec(name, loader, file, packageDir != null);
            spec.SetAttribute("has_location", PyBool.True);
            string cached = bundle == null ? PyBytecodeCache.CachePathFor(file) : null;
            spec.SetAttribute("cached", cached == null ? PyNone.Instance : PyString.From(cached));
            if (packageDir != null)
                spec.SetAttribute("submodule_search_locations", new PyList(new PyObject[] { PyString.From(packageDir) }));
            return spec;
        }

        /// <summary>
        /// PathFinder.find_spec over sys.path (searchPath None) or a package's __path__: a module
        /// file, a regular package, a module of a SharpPy bundle, and last a namespace package of
        /// every directory of the name (PEP 420).
        /// </summary>
        internal static PyObject FindSpec(string fullName, PyObject searchPath, PyModule parent)
        {
            string name = fullName.Substring(fullName.LastIndexOf('.') + 1);
            if (parent?.Bundle != null)
                return parent.Bundle.Contains(fullName) ? BundleSpec(parent.Bundle, fullName) : null;
            IEnumerable<PyObject> directories = searchPath is PyNone ? (IEnumerable<PyObject>)GetSysPath()?.Items ?? Array.Empty<PyObject>()
                : PySet.IterableItems(searchPath);
            var namespaceDirs = new List<PyObject>();
            foreach (var entry in directories)
            {
                if (entry is not PyString pathText)
                    continue;
                string directory = pathText.Value;
                if (PyBundle.IsBundlePath(directory))
                {
                    var bundle = PyBundle.Open(directory);
                    if (bundle != null && bundle.Contains(fullName))
                        return BundleSpec(bundle, fullName);
                    continue;
                }
                string packageDir = IOHelper.CombinePath(directory, name);
                string init = IOHelper.CombinePath(packageDir, "__init__.py");
                bool isDirectory = IOHelper.DirExists(packageDir);
                if (isDirectory && IOHelper.FileExists(init))
                    return FileSpec(fullName, init, packageDir, null);
                string file = IOHelper.CombinePath(directory, name + ".py");
                if (IOHelper.FileExists(file))
                    return FileSpec(fullName, file, null, null);
                if (isDirectory)
                    namespaceDirs.Add(PyString.From(packageDir));
            }
            if (namespaceDirs.Count == 0)
                return null;
            // _bootstrap_external.PathFinder: NamespaceLoader, no origin, the directories as locations.
            var spec = NewSpec(fullName, new PyNamespaceLoader(fullName), null, true);
            spec.SetAttribute("submodule_search_locations", new PyList(namespaceDirs.ToArray()));
            return spec;
        }

        private static PyObject BundleSpec(PyBundle bundle, string fullName)
        {
            string relative = fullName.Replace('.', '/');
            bool isPackage = bundle.IsPackage(fullName);
            string file = $"{bundle.Path}/{relative}{(isPackage ? "/__init__.py" : ".py")}";
            return FileSpec(fullName, file, isPackage ? $"{bundle.Path}/{relative}" : null, bundle);
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

        internal PySourceFileLoader(string name, string path, PyBundle bundle)
        {
            Name = name;
            Path = path;
            Bundle = bundle;
        }

        public override PyType GetPyType() => Bundle == null ? LoaderType : BundleLoaderType;

        private bool IsPackage => System.IO.Path.GetFileNameWithoutExtension(Path) == "__init__";

        private static PyType MakeType(string name, string module)
        {
            var type = new PyType(name, new[] { PyType.ObjectType }, module);
            PySourceFileLoader Self(PyObject self) => (PySourceFileLoader)self;
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
                return Bundle.Code(Name);
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

    /// <summary>NamespaceLoader (Lib/importlib/_bootstrap_external.py): nothing to run.</summary>
    internal sealed class PyNamespaceLoader : PyObject
    {
        internal static readonly PyType LoaderType = MakeType();
        private readonly string _name;

        internal PyNamespaceLoader(string name) => _name = name;

        public override PyType GetPyType() => LoaderType;

        private static PyType MakeType()
        {
            var type = new PyType("NamespaceLoader", new[] { PyType.ObjectType }, "_frozen_importlib_external");
            type.TypeDict["create_module"] = new PyMethodDescriptor("create_module", type, (self, args, kwargs) => PyNone.Instance, 1, 1);
            type.TypeDict["exec_module"] = new PyMethodDescriptor("exec_module", type, (self, args, kwargs) => PyNone.Instance, 1, 1);
            type.TypeDict["is_package"] = new PyMethodDescriptor("is_package", type, (self, args, kwargs) => PyBool.True, 1, 1);
            type.TypeDict["get_source"] = new PyMethodDescriptor("get_source", type, (self, args, kwargs) => PyString.From(""), 1, 1);
            return type;
        }
    }
}
