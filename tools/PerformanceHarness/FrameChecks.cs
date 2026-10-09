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
    }
}
