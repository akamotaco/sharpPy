using SharpPy;

internal static class FrameChecks
{
    public static void Run()
    {
        var interpreter = new IntegratedPythonInterpreter();
        interpreter.Execute("def empty():\n    return None\n");
        var function = (PyFunction)interpreter.GetGlobalVariable("empty");
        var frame = new PyFrame(function.CodeObject, Array.Empty<PyObject>(),
            new PyScopeChain(function.GlobalsDict, "probe"));
        var other = new PyFrame(function.CodeObject, Array.Empty<PyObject>(),
            new PyScopeChain(function.GlobalsDict, "probe"));
        if (frame.Cells.Length != 0 || frame.Closure.Length != 0 || frame.LocalsPlus.Length != 0)
            throw new InvalidOperationException("Empty frame storage changed");
        frame.PopExceptionHandler();
        if (frame.GetExceptionHandler() != null) throw new InvalidOperationException("Empty handler result changed");
        var handlers = frame.ExceptionHandlers;
        frame.PushExceptionHandler(7);
        frame.PushExceptionHandler(11);
        if (!ReferenceEquals(handlers, frame.ExceptionHandlers) || frame.GetExceptionHandler() != 11)
            throw new InvalidOperationException("Legacy handler stack changed");
        if (other.ExceptionHandlers.Count != 0) throw new InvalidOperationException("Handler state leaked between frames");
        frame.PopExceptionHandler();
        if (frame.GetExceptionHandler() != 7) throw new InvalidOperationException("Legacy handler pop changed");
        frame.PopExceptionHandler();
        frame.PopExceptionHandler();
        if (frame.GetExceptionHandler() != null) throw new InvalidOperationException("Legacy handler cleanup changed");

        interpreter.Execute("def scope_probe(value):\n    return value\n");
        var probe = (PyFunction)interpreter.GetGlobalVariable("scope_probe");
        var scopeFrame = new PyFrame(probe.CodeObject, new PyObject[] { SmallIntCache.One },
            new PyScopeChain(probe.GlobalsDict, "probe"));
        var localScope = scopeFrame.LocalScope ?? throw new InvalidOperationException("Missing function scope");
        var variables = localScope.Variables;
        if (!ReferenceEquals(variables["value"], SmallIntCache.One))
            throw new InvalidOperationException("C# scope parameter lookup changed");
        scopeFrame.LocalsPlus[0] = new PyInt(2);
        if (!ReferenceEquals(variables, localScope.Variables) ||
            ((PyInt)variables["value"]).Value != 2)
            throw new InvalidOperationException("C# scope refresh or identity changed");
        variables["extra"] = SmallIntCache.One;
        scopeFrame.LocalsPlus[0] = PyNull.Instance;
        if (localScope.Variables.ContainsKey("value") || !variables.ContainsKey("extra"))
            throw new InvalidOperationException("C# scope deletion or extra key retention changed");

        scopeFrame.LocalsPlus[0] = SmallIntCache.One;
        var observed = (PyDict)scopeFrame.GetAttribute("f_locals");
        if (((PyInt)observed.GetItem(new PyString("value"))).Value != 1)
            throw new InvalidOperationException("Initial locals key cache changed values");
        try
        {
            probe.CodeObject.VarNames[0] = "renamed";
            observed = (PyDict)scopeFrame.GetAttribute("f_locals");
            if (((PyInt)observed.GetItem(new PyString("renamed"))).Value != 1)
                throw new InvalidOperationException("Locals key cache retained a stale metadata name");
            probe.CodeObject.VarNames.Add("later");
            var resized = new PyFrame(probe.CodeObject, new PyObject[] { SmallIntCache.One },
                new PyScopeChain(probe.GlobalsDict, "probe"));
            resized.LocalsPlus[1] = new PyInt(2);
            observed = (PyDict)resized.GetAttribute("f_locals");
            if (((PyInt)observed.GetItem(new PyString("later"))).Value != 2)
                throw new InvalidOperationException("Locals key cache did not resize with metadata");
        }
        finally
        {
            probe.CodeObject.VarNames[0] = "value";
            if (probe.CodeObject.VarNames.Count > 1) probe.CodeObject.VarNames.RemoveAt(1);
        }
    }
}
