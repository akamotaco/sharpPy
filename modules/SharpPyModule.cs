using System;

namespace SharpPy.Modules
{
    /// <summary>
    /// What Python code on a worker thread needs from the host, and the C# half of the import
    /// system: FileFinder, which _frozen_importlib_external takes (it lists directories through
    /// Godot's library), and BundleFinder. CPython has no such module.
    /// </summary>
    internal static class SharpPyModule
    {
        internal static PyModule CreateSharpPyModule()
        {
            var module = new PyModule("sharppy");
            module.ModuleDict["call_on_main"] = new PyBuiltinFunction("call_on_main", (args, kwargs) =>
            {
                if (args.Length == 0 || !args[0].IsCallable())
                    throw PyTypeError.Create("call_on_main() needs a callable");
                var rest = new PyObject[args.Length - 1];
                Array.Copy(args, 1, rest, 0, rest.Length);
                PyHost.CallOnMain(args[0], rest, kwargs);
                return PyNone.Instance;
            });
            module.ModuleDict["is_main_thread"] = new PyBuiltinFunction("is_main_thread", args =>
                PyBool.FromBool(PyVM.Instance.BoundState.IsMain));
            module.ModuleDict["FileFinder"] = PyFileFinder.FinderType;
            module.ModuleDict["BundleFinder"] = PyBundleFinder.FinderType;
            return module;
        }
    }
}
