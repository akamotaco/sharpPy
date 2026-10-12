namespace SharpPy
{
    internal static class PyWarnings
    {
        private const string LegacyThrowMessage = "the (type, exc, tb) signature of throw() is deprecated, use the single-arg signature instead.";
        private static HashSet<string> onceMessages;

        internal static void DeprecatedThrow()
        {
            var frame = PyVM.CurrentFrame;
            if (frame == null) return;
            var globals = frame.ScopeChain.GlobalScope.Variables;
            string moduleName = globals.TryGetValue("__name__", out var name) && name is PyString text ? text.Value : "<unknown>";
            int line = frame.CurrentLineNumber;
            string action = moduleName == "__main__" ? "default" : "ignore";
            long version = 0;
            if (PyImportSystem.TryGetModule("warnings", out var warnings))
            {
                action = "default";
                if (warnings.ModuleDict.TryGetValue("_filters_version", out var stamp) && stamp is PyInt integer)
                    version = (long)integer.Value;
                if (warnings.ModuleDict.TryGetValue("filters", out var filters) && filters is PyList list)
                {
                    foreach (var item in list.Items)
                    {
                        if (item is not PyTuple filter || filter.Items.Length != 5) continue;
                        if (filter.Items[2] is not PyType category || !PyType.DeprecationWarningType.IsSubclassOf(category)) continue;
                        if (filter.Items[4] is PyInt number && number.Value != 0 && number.Value != line) continue;
                        if (filter.Items[3] is PyString module && module.Value != moduleName) continue;
                        if (filter.Items[1] is not PyNone && !filter.Items[1].GetAttribute("match").Call(new PyObject[] { new PyString(LegacyThrowMessage) }, null).IsTrue()) continue;
                        if (filter.Items[3] is not PyNone && filter.Items[3] is not PyString && !filter.Items[3].GetAttribute("match").Call(new PyObject[] { new PyString(moduleName) }, null).IsTrue()) continue;
                        action = filter.Items[0].ToStr().Value;
                        break;
                    }
                }
            }
            if (action == "ignore") return;
            if (action == "error") throw PyDeprecationWarning.Create(LegacyThrowMessage);
            if (action != "always")
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
                var key = new PyTuple(new PyString(LegacyThrowMessage), PyType.DeprecationWarningType,
                    new PyInt(action == "module" || action == "once" ? 0 : line));
                if (registry.ContainsKey(key)) return;
                registry.SetItem(key, PyBool.True);
                if (action == "once" && !(onceMessages ??= new HashSet<string>()).Add(LegacyThrowMessage)) return;
            }
            string output = $"{frame.Code.FileName}:{line}: DeprecationWarning: {LegacyThrowMessage}\n";
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
