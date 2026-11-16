using SharpPy;

internal static class HostNamespaceProbe
{
    public static object Run()
    {
        // The host's namespace and the dict it is a view of: one store.
        var source = new PyNamespace { ["value"] = new PyInt(1) };
        var view = source.PythonDictionary;
        var key = new PyString("value");
        var rows = new List<object>();
        void Check(string name, long expected, PyObject actual) => rows.Add(new {
            name, expected, actual = actual.ToStr().Value,
            matches = actual is PyInt integer && integer.Value == expected
        });
        view.SetItem(key, new PyInt(2));
        Check("mapping write reaches host", 2, source["value"]);
        Check("mapping write reaches copy", 2, view.Copy().GetItem(key));
        source["value"] = new PyInt(3);
        Check("direct host replacement reaches lookup", 3, view.GetItem(key));
        Check("direct host replacement reaches copy", 3, view.Copy().GetItem(key));
        source["added"] = new PyInt(4);
        Check("direct host insertion reaches length", 2, new PyInt(view.Length()));
        Check("direct host insertion reaches copy length", 2, new PyInt(view.Copy().Length()));
        source.Remove("value");
        Check("direct host deletion reaches membership", 0, new PyInt(view.ContainsKey(key) ? 1 : 0));
        Check("direct host deletion reaches copied membership", 0, new PyInt(view.Copy().ContainsKey(key) ? 1 : 0));
        source["value"] = new PyInt(5);
        Check("explicit synchronization reaches values", 5, view.Values().Items[1]);
        var nonString = new PyInt(99);
        view.SetItem(nonString, new PyInt(6));
        source.Clear();
        Check("host clear preserves Python non-string keys", 1, new PyInt(view.Copy().Length()));
        Check("non-string value survives host clear", 6, view.Copy().GetItem(nonString));
        var module = new PyModule("host_namespace_probe");
        var mapping = (PyDict)module.GetAttribute("__dict__");
        mapping.Clear();
        var host = module.ModuleDict;
        var retainedKeys = host.Keys;
        host["a"] = new PyInt(1);
        host["b"] = new PyInt(2);
        mapping.SetItem(nonString, new PyInt(3));
        host.Remove("a");
        host["a"] = new PyInt(4);
        void Text(string name, string expected, string actual) => rows.Add(new { name, expected, actual, matches = expected == actual });
        Text("host remove/reinsert preserves Python order", "['b', 99, 'a']", mapping.Keys().ToRepr().Value);
        Text("retained host keys are live and ordered", "b,a", string.Join(",", retainedKeys));
        Check("host replacement reaches retained values", 4, mapping.Values().Items[2]);
        Check("host replacement reaches snapshot", 4, mapping.Copy().GetItem(new PyString("a")));
        var iterator = mapping.GetIterator();
        iterator.Next();
        host["c"] = new PyInt(5);
        string error = "no error";
        try { iterator.Next(); }
        catch (PythonException ex) { error = ex.PyException.GetTypeName() + ": " + ex.PyException.ToStr().Value; }
        Text("host insertion invalidates Python iterator", "RuntimeError: dictionary changed size during iteration", error);
        host.Clear();
        Text("host clear preserves only non-string Python keys", "[99]", mapping.Keys().ToRepr().Value);
        Check("host clear updates retained key collection", 0, new PyInt(retainedKeys.Count));
        mapping.Clear();
        Check("Python clear reaches host", 0, new PyInt(host.Count));
        return new { scope = "Host namespace views and module host writes", cases = rows };
    }
}
