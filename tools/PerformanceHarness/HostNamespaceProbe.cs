using SharpPy;

internal static class HostNamespaceProbe
{
    public static object Run()
    {
        var source = new Dictionary<string, PyObject> { ["value"] = new PyInt(1) };
        var view = new PyGlobalsDict(source);
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
        return new { scope = "Native host Dictionary mutations versus PyGlobalsDict; mismatches remain explicit", cases = rows };
    }
}
