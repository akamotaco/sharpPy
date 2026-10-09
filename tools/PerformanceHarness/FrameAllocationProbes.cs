using SharpPy;

internal static class FrameAllocationProbes
{
    public static object Run()
    {
        const int iterations = 1000;
        var interpreter = new IntegratedPythonInterpreter();
        interpreter.Execute("def empty():\n    return None\ndef add(a, b):\n    return a + b\n");
        var empty = (PyFunction)interpreter.GetGlobalVariable("empty");
        var add = (PyFunction)interpreter.GetGlobalVariable("add");
        var noArgs = Array.Empty<PyObject>();
        var twoArgs = new PyObject[] { SmallIntCache.One, new PyInt(2) };
        for (int i = 0; i < 10; i++) { empty.Call(noArgs, null); add.Call(twoArgs, null); }
        var globals = empty.GlobalsDict;
        var results = new List<object>();
        void Measure(string name, Func<object> factory)
        {
            var retained = new object[iterations];
            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < iterations; i++) retained[i] = factory();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
            GC.KeepAlive(retained);
            results.Add(new { name, allocated_bytes = allocated, bytes_per_operation = (double)allocated / iterations });
        }
        Measure("argument_array_2", () => new PyObject[2]);
        Measure("empty_cells_array", () => new PyCell[0]);
        Measure("legacy_handler_stack", () => new Stack<int>());
        Measure("value_stack", () => new PyStack());
        Measure("local_scope_empty", () => new PyScope(ScopeType.Local, null, "probe"));
        Measure("local_scope_2_parameters", () =>
        {
            var scope = new PyScope(ScopeType.Local, null, "probe");
            scope.SetVariable("a", twoArgs[0]);
            scope.SetVariable("b", twoArgs[1]);
            return scope;
        });
        Measure("global_scope_chain", () => new PyScopeChain(globals, "probe"));
        var scopes = new PyScopeChain[iterations];
        for (int i = 0; i < iterations; i++) scopes[i] = new PyScopeChain(globals, "probe");
        int index = 0;
        Measure("frame_empty_precreated_scope", () => new PyFrame(empty.CodeObject, noArgs, scopes[index++]));
        for (int i = 0; i < iterations; i++) scopes[i] = new PyScopeChain(globals, "probe");
        index = 0;
        Measure("frame_add_precreated_scope", () => new PyFrame(add.CodeObject, twoArgs, scopes[index++]));
        Measure("host_call_empty", () => empty.Call(noArgs, null));
        Measure("host_call_add", () => add.Call(twoArgs, null));
        return new { iterations, results, scope = "Synchronous thread allocations; setup/output arrays outside measurement; frame probes include local scope creation and binding; overlapping components must not be summed" };
    }
}
