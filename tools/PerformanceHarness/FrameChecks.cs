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
        if (frame.Closure.Length != 0 || frame.LocalsPlus.Length != 0)
            throw new InvalidOperationException("Empty frame storage changed");

        interpreter.Execute("def scope_probe(value):\n    return value\n");
        var probe = (PyFunction)interpreter.GetGlobalVariable("scope_probe");
        var probeCode = probe.CodeObject ?? throw new InvalidOperationException("Missing probe code");
        var scopeFrame = new PyFrame(probeCode, new PyObject[] { PyInt.One },
            new PyScopeChain(probe.GlobalsDict, "probe"));
        scopeFrame.LocalsPlus[0] = PyInt.One;
        var observed = (PyDict)scopeFrame.GetAttribute("f_locals");
        if (((PyInt)observed.GetItem(new PyString("value"))).Value != 1)
            throw new InvalidOperationException("Initial locals key cache changed values");
    }
}
