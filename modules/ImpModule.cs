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
        // The stdlib modules CPython 3.12 freezes (_imp._frozen_module_names(), in its order), with the
        // module each one's code is ("os.path" is posixpath's, as there). The import bootstrap runs
        // _frozen_importlib and _frozen_importlib_external itself, and _setup gives them FrozenImporter's
        // spec, as CPython's.
        private static readonly System.Collections.Generic.Dictionary<string, string> Frozen = new()
        {
            ["_frozen_importlib"] = "importlib._bootstrap", ["_frozen_importlib_external"] = "importlib._bootstrap_external",
            ["zipimport"] = "zipimport", ["abc"] = "abc", ["codecs"] = "codecs", ["io"] = "io",
            ["_collections_abc"] = "_collections_abc", ["_sitebuiltins"] = "_sitebuiltins", ["genericpath"] = "genericpath",
            ["ntpath"] = "ntpath", ["posixpath"] = "posixpath", ["os.path"] = "posixpath", ["os"] = "os",
            ["site"] = "site", ["stat"] = "stat", ["importlib.util"] = "importlib.util",
            ["importlib.machinery"] = "importlib.machinery", ["runpy"] = "runpy",
        };

        private static string Name(PyObject value) =>
            (value as PyString ?? throw PyArgs.BadArgument("find_frozen", "argument 1", "str", value)).Value;

        // The file of a frozen module's code: _bootstrap_external's Python part is the generated file
        // (core/PyImportSpecs.cs has FileFinder).
        private static string SourceFile(string origin) =>
            origin == "importlib._bootstrap_external" ? "importlib/_external_functions.py" : origin.Replace('.', '/') + ".py";

        // The module frozen under name: its origname, when Lib has its source.
        private static string FrozenOrigin(string name) =>
            Frozen.TryGetValue(name, out var origin) && PyImportSystem.LocateSource(SourceFile(origin)) != null ? origin : null;

        private static PyCodeObject FrozenCode(string name)
        {
            string origin = FrozenOrigin(name);
            if (origin == null)
                return null;
            string path = PyImportSystem.LocateSource(SourceFile(origin));
            return Compiler.CompileSource(PyImportSystem.ReadSource(path), "<frozen " + origin + ">");
        }
        internal static PyModule Create()
        {
            var module = new PyModule("_imp");
            var d = module.ModuleDict;
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
            // Python/frozen.c's stdlib modules: their source is Lib's, their code "<frozen name>".
            d["is_frozen"] = PyArgs.MethO("_imp", "is_frozen", args => PyBool.FromBool(FrozenOrigin(Name(args[0])) != null));
            d["is_frozen_package"] = PyArgs.MethO("_imp", "is_frozen_package", args => PyBool.False);
            // _imp_find_frozen: (data, ispkg, origname), None for a module that is not frozen.
            d["find_frozen"] = new PyBuiltinFunction("find_frozen", (args, kwargs) =>
            {
                var parsed = PyArgs.Unpack("find_frozen", args, kwargs, new[] { "", "withdata" }, 1, 1, 1);
                string origin = FrozenOrigin(Name(parsed[0]));
                return origin == null ? PyNone.Instance : PyTuple.From(PyNone.Instance, PyBool.False, PyString.From(origin));
            });
            d["get_frozen_object"] = new PyBuiltinFunction("get_frozen_object", (args, kwargs) =>
            {
                string name = Name(args[0]);
                return FrozenCode(name) ?? throw PyImportError.Create($"No such frozen object named {args[0].ToRepr().Value}");
            });
            d["_frozen_module_names"] = new PyBuiltinFunction("_frozen_module_names", args =>
            {
                var names = new System.Collections.Generic.List<PyObject>();
                foreach (var name in Frozen.Keys)
                    names.Add(PyString.From(name));
                return PyTuple.From(names.ToArray());
            });
            d["init_frozen"] = PyArgs.MethO("_imp", "init_frozen", args => PyNone.Instance);
            // The global import lock: SharpPy's imports hold their own.
            d["acquire_lock"] = new PyBuiltinFunction("acquire_lock", args => PyNone.Instance);
            d["release_lock"] = new PyBuiltinFunction("release_lock", args => PyNone.Instance);
            d["lock_held"] = new PyBuiltinFunction("lock_held", args => PyBool.False);
            d["extension_suffixes"] = new PyBuiltinFunction("extension_suffixes", args => new PyList());
            // _imp__fix_co_filename_impl: update_compiled_module's update_code_filenames - the code and
            // the code among its constants name the file it was imported from.
            d["_fix_co_filename"] = PyArgs.FastCall("_imp", "_fix_co_filename", 2, 2, args =>
            {
                if (args[0] is not PyCodeObject code)
                    throw PyArgs.BadArgument("_fix_co_filename", "argument 1", "code", args[0]);
                if (args[1] is not PyString path)
                    throw PyArgs.BadArgument("_fix_co_filename", "argument 2", "str", args[1]);
                code.FixFileName(path.Value);
                return PyNone.Instance;
            });
            d["_override_frozen_modules_for_tests"] = new PyBuiltinFunction("_override_frozen_modules_for_tests", args => PyNone.Instance);
            d["_override_multi_interp_extensions_check"] = new PyBuiltinFunction("_override_multi_interp_extensions_check", args => PyInt.From(-1));
            d["check_hash_based_pycs"] = PyString.From("default");
            // _imp_source_hash_impl: _Py_KeyedHash((uint64_t)key, source), little-endian.
            d["source_hash"] = new PyBuiltinFunction("source_hash", (args, kwargs) =>
            {
                var parsed = PyArgs.Unpack("source_hash", args, kwargs, new[] { "key", "source" }, 0, 2, 2);
                long key = PyArgs.CLong(parsed[0]);
                var source = BytesMethods.RequireBuffer(parsed[1]);
                var hash = new byte[8];
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(hash, PyHash.Keyed(unchecked((ulong)key), source));
                return PyBytes.From(hash);
            });
            return module;
        }
    }
}
