using System;
#if GODOT
using IOHelper = Godot_IO.Helper;
#else
using IOHelper = DotNet_IO.Helper;
#endif

namespace SharpPy.Modules
{
    /// <summary>
    /// Minimal native importlib facade.
    /// CPython 3.12: Lib/importlib/__init__.py (import_module/reload surface).
    /// SharpPy's import machinery lives in PyImportSystem, so this module is a
    /// thin native adapter instead of the pure-Python package.
    /// </summary>
    public static class ImportlibModule
    {
        public static PyModule CreateImportlibModule()
        {
            var module = new PyModule("_importlib_native");

            module.ModuleDict["import_module"] = new PyBuiltinFunction("import_module", (args, kwargs) =>
            {
                if (args.Length == 0)
                {
                    throw PyTypeError.Create("import_module() missing 1 required positional argument: 'name'");
                }
                if (args.Length > 2)
                {
                    throw PyTypeError.Create($"import_module() takes from 1 to 2 positional arguments but {args.Length} were given");
                }
                if (args[0] is not PyString nameStr)
                {
                    throw PyTypeError.Create($"__import__() argument 1 must be str, not {args[0].GetTypeName()}");
                }
                // import_module and _resolve_name: the module named, not its top package.
                string name = nameStr.Value;
                if (name.StartsWith("."))
                {
                    if (args.Length < 2 || args[1] is not PyString package)
                        throw PyTypeError.Create($"the 'package' argument is required to perform a relative import for {nameStr.ToRepr().Value}");
                    int level = 0;
                    while (level < name.Length && name[level] == '.')
                        level++;
                    var parts = package.Value.Split('.');
                    if (level > parts.Length)
                        throw PyImportError.Create("attempted relative import beyond top-level package");
                    string parent = string.Join(".", parts, 0, parts.Length - level + 1);
                    name = level == name.Length ? parent : parent + "." + name.Substring(level);
                }
                PyImportSystem.Import(name, 0, Array.Empty<string>(), null);
                return PyImportSystem.TryGetModule(name, out var leaf) ? leaf : PyImportSystem.Import(name, 0, Array.Empty<string>(), null);
            });

            module.ModuleDict["reload"] = new PyBuiltinFunction("reload", (args, kwargs) =>
            {
                // CPython Lib/importlib/__init__.py reload(module): positional-only.
                if (kwargs != null && kwargs.InternalDict.Count > 0)
                {
                    string kw = "";
                    foreach (var e in kwargs.GetInternalDict()) { kw = ((PyString)e.Key).Value; break; }
                    throw PyTypeError.Create($"reload() got an unexpected keyword argument '{kw}'");
                }
                if (args.Length == 0)
                    throw PyTypeError.Create("reload() missing 1 required positional argument: 'module'");
                if (args.Length > 1)
                    throw PyTypeError.Create($"reload() takes 1 positional argument but {args.Length} were given");
                if (args[0] is not PyModule m)
                    throw PyTypeError.Create("reload() argument must be a module");

                // Re-execute only file-backed modules. Builtin/native modules
                // (no real __file__) return unchanged, matching CPython reload(sys).
                // The module keeps its identity and sys.modules registration, and the
                // source runs against the existing __dict__ (stale attributes remain).
                if (m.Bundle != null)
                {
                    m.ExecuteCode(m.Bundle.Code(m.Name));
                }
                else if (m.HasSourceFile)
                {
                    var source = Generated.Tokenizer.DecodeFile(IOHelper.ReadAllBytes(m.FileName), m.FileName);
                    m.ExecuteForReload(source);
                }
                return m;
            });

            module.ModuleDict["invalidate_caches"] = new PyBuiltinFunction("invalidate_caches", (args, kwargs) =>
                PyNone.Instance);

            return module;
        }
    }
}
