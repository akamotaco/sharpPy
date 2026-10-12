namespace SharpPy
{
    internal static class PyWarnings
    {
        private const string LegacyThrowMessage = "the (type, exc, tb) signature of throw() is deprecated, use the single-arg signature instead.";

        internal static void DeprecatedThrow(bool asynchronous = false)
        {
            string message = asynchronous ? "the (type, exc, tb) signature of athrow() is deprecated, use the single-arg signature instead." : LegacyThrowMessage;
            var frame = PyVM.CurrentFrame;
            if (frame == null) return;
            var globals = frame.ScopeChain.GlobalScope.Variables;
            string moduleName = globals.TryGetValue("__name__", out var name) && name is PyString text ? text.Value : "<unknown>";
            int line = frame.CurrentLineNumber;
            string action = moduleName == "__main__" ? "default" : "ignore";
            long version = 0;
            if (PyImportSystem.TryGetModule("warnings", out var warnings) &&
                warnings.ModuleDict.TryGetValue("warn_explicit", out var warn))
            {
                if (!globals.TryGetValue("__warningregistry__", out var registry) || registry is not PyDict)
                {
                    registry = new PyDict();
                    PyGlobalsDict.Store(globals, "__warningregistry__", registry);
                }
                warn.Call(new PyObject[] { new PyString(message), PyType.DeprecationWarningType,
                    new PyString(frame.Code.FileName), new PyInt(line), new PyString(moduleName), registry,
                    PyGlobalsDict.For(globals) }, null);
                return;
            }
            if (action == "ignore") return;
            {
                if (!globals.TryGetValue("__warningregistry__", out var registryObject) || registryObject is not PyDict)
                {
                    registryObject = new PyDict();
                    PyGlobalsDict.Store(globals, "__warningregistry__", registryObject);
                }
                var registry = (PyDict)registryObject;
                var versionKey = StringCache.GetOrCreate("version");
                if (registry.Get(versionKey) is not PyInt oldVersion || oldVersion.Value != version)
                {
                    registry.Clear();
                    registry.SetItem(versionKey, new PyInt(version));
                }
                var key = new PyTuple(new PyString(message), PyType.DeprecationWarningType,
                    new PyInt(line));
                if (registry.ContainsKey(key)) return;
                registry.SetItem(key, PyBool.True);
            }
            string output = $"{frame.Code.FileName}:{line}: DeprecationWarning: {message}\n";
            var source = frame.Code.SourceLines;
            if (source != null && line > 0 && line <= source.Count) output += "  " + source[line - 1].Trim() + "\n";
            PyObject stream = null;
            if (PyImportSystem.TryGetModule("sys", out var sys)) sys.ModuleDict.TryGetValue("stderr", out stream);
            if (stream is PyNone) return;
            if (stream == null || stream is Modules.PyFile file && file.Name == "stderr") Console.Error.Write(output);
            else if (stream is Modules.PyFile stdout && stdout.Name == "stdout") Console.Out.Write(output);
            else stream.GetAttribute("write").Call(new PyObject[] { new PyString(output) }, null);
        }
    }
}
