using System;

namespace SharpPy.Modules
{
    /// <summary>
    /// _imp (Python/import.c): what Lib/importlib/_bootstrap.py (run as _frozen_importlib) asks the
    /// interpreter. Every C# module of PyImportSystem is a builtin; nothing is frozen and there are no
    /// extension modules.
    /// </summary>
    internal static class ImpModule
    {
        internal static PyModule Create()
        {
            var module = new PyModule("_imp");
            var d = module.ModuleDict;
            module.ModuleDict["__doc__"] = PyString.From("(Extremely) low-level import machinery bits as used by importlib.");
            // _imp_is_builtin: 1 for a module made by the interpreter, 0 otherwise (the unicode
            // converter: no check for an embedded NUL, which no builtin's name has).
            d["is_builtin"] = PyArgs.MethO("_imp", "is_builtin", args =>
                PyInt.From(PyImportSystem.IsBuiltinModule((args[0] as PyString ?? throw PyArgs.BadArgument("is_builtin", "argument", "str", args[0])).Value) ? 1 : 0));
            // _imp_create_builtin: the module of spec.name; sys and builtins are the ones there are.
            d["create_builtin"] = PyArgs.MethO("_imp", "create_builtin", args =>
            {
                var name = args[0].GetAttribute("name");
                if (name is not PyString text)
                    throw PyTypeError.Create($"name must be string, not {name.GetPyType().FullName}");
                return PyImportSystem.CreateBuiltin(text.Value)
                    ?? throw PyImportError.Create($"no built-in module named {text.Value}");
            });
            // _imp_exec_builtin: a C# module is complete when it is made.
            d["exec_builtin"] = PyArgs.MethO("_imp", "exec_builtin", args => PyInt.From(0));
            d["exec_dynamic"] = PyArgs.MethO("_imp", "exec_dynamic", args => PyInt.From(0));
            d["create_dynamic"] = new PyBuiltinFunction("create_dynamic", args =>
                throw PyImportError.Create("SharpPy has no extension modules"));
            d["is_frozen"] = PyArgs.MethO("_imp", "is_frozen", args => PyBool.False);
            d["is_frozen_package"] = PyArgs.MethO("_imp", "is_frozen_package", args => PyBool.False);
            d["find_frozen"] = new PyBuiltinFunction("find_frozen", (args, kwargs) => PyNone.Instance);
            d["get_frozen_object"] = new PyBuiltinFunction("get_frozen_object", (args, kwargs) =>
                throw PyImportError.Create($"No such frozen object named {args[0].ToRepr().Value}"));
            d["init_frozen"] = PyArgs.MethO("_imp", "init_frozen", args => PyNone.Instance);
            // The global import lock: SharpPy's imports hold their own.
            d["acquire_lock"] = new PyBuiltinFunction("acquire_lock", args => PyNone.Instance);
            d["release_lock"] = new PyBuiltinFunction("release_lock", args => PyNone.Instance);
            d["lock_held"] = new PyBuiltinFunction("lock_held", args => PyBool.False);
            d["extension_suffixes"] = new PyBuiltinFunction("extension_suffixes", args => new PyList());
            d["_fix_co_filename"] = new PyBuiltinFunction("_fix_co_filename", args => PyNone.Instance);
            d["_override_frozen_modules_for_tests"] = new PyBuiltinFunction("_override_frozen_modules_for_tests", args => PyNone.Instance);
            d["_override_multi_interp_extensions_check"] = new PyBuiltinFunction("_override_multi_interp_extensions_check", args => PyInt.From(-1));
            d["check_hash_based_pycs"] = PyString.From("default");
            return module;
        }
    }
}
