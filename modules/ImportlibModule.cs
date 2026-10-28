using System;

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
            var module = new PyModule("importlib");

            module.ModuleDict["import_module"] = new PyBuiltinFunction("import_module", (args, kwargs) =>
            {
                if (args.Length < 1 || args.Length > 2)
                {
                    throw PyTypeError.Create($"import_module() takes 1 or 2 positional arguments but {args.Length} were given");
                }
                if (args[0] is not PyString nameStr)
                {
                    throw PyTypeError.Create($"__import__() argument 1 must be str, not {args[0].GetTypeName()}");
                }
                int level = 0;
                if (args.Length == 2)
                {
                    var idx = PyObject.TryGetIndex(args[1]);
                    if (idx == null)
                    {
                        throw PyTypeError.Create($"'{args[1].GetTypeName()}' object cannot be interpreted as an integer");
                    }
                    level = (int)(long)idx.Value;
                }
                if (level != 0)
                {
                    throw PyTypeError.Create("the 'package' argument is required to perform a relative import");
                }
                return PyImportSystem.Import(nameStr.Value, 0, Array.Empty<string>(), null);
            });

            module.ModuleDict["reload"] = new PyBuiltinFunction("reload", (args, kwargs) =>
            {
                if (args.Length != 1)
                {
                    throw PyTypeError.Create($"reload() takes exactly one argument ({args.Length} given)");
                }
                if (args[0] is not PyModule m)
                {
                    throw PyTypeError.Create($"reload() argument must be module, not {args[0].GetTypeName()}");
                }
                // SharpPy: full re-execution of a module spec is not implemented;
                // the module object is returned unchanged (identity preserved).
                return m;
            });

            module.ModuleDict["invalidate_caches"] = new PyBuiltinFunction("invalidate_caches", (args, kwargs) =>
                PyNone.Instance);

            return module;
        }
    }
}
