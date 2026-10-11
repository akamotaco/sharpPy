using System.Diagnostics;
using SharpPy;

internal static class ExceptionFactoryProbe
{
    internal static object Run(bool includeMappedErrors = false, bool includeDirectoryErrors = false)
    {
        const int iterations = 100000;
        var builtin = PyBuiltinsModule.Instance;
        var results = new List<object>();
        foreach (var name in new[] { "OSError", "FileNotFoundError", "ValueError" })
        foreach (var arity in new[] { 1, 2 })
        foreach (var code in arity == 1 ? new[] { 0 } : includeDirectoryErrors ? new[] { 999, 1, 2, 13, 17, 20, 21 } : includeMappedErrors ? new[] { 999, 1, 2, 13, 17 } : new[] { 999 })
        foreach (var path in new[] { "create", "call", "lookup_call" })
        {
            var type = name == "OSError" ? PyType.OSErrorType :
                name == "FileNotFoundError" ? PyType.FileNotFoundErrorType : PyType.ValueErrorType;
            PyObject[] args = arity == 1 ? new PyObject[] { new PyString("message") } :
                new PyObject[] { new PyInt(code), new PyString("message") };
            string expectedType = name == "OSError" && arity == 2 ? code switch
            {
                2 => "FileNotFoundError", 1 or 13 => "PermissionError", 17 => "FileExistsError",
                20 => "NotADirectoryError", 21 => "IsADirectoryError", _ => "OSError"
            } : name;
            PyObject RunBatch()
            {
                PyObject result = PyNone.Instance;
                for (int i = 0; i < iterations; i++)
                    result = path == "create" ? type.CreateInstance(args, null) :
                        path == "call" ? type.Call(args, null) : builtin.GetBuiltin(name).Call(args, null);
                if (result is not PyBaseException exception || exception.Args.Length != arity ||
                    exception.GetTypeName() != expectedType)
                    throw new InvalidOperationException("exception factory probe result mismatch");
                return result;
            }
            for (int i = 0; i < 5; i++) RunBatch();
            var times = new double[10];
            var allocations = new long[10];
            for (int i = 0; i < times.Length; i++)
            {
                long bytes = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                RunBatch();
                times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                allocations[i] = GC.GetAllocatedBytesForCurrentThread() - bytes;
            }
            results.Add(new { name, arity, code, path, milliseconds = times, allocated_bytes = allocations });
        }
        return new { iterations, warmups = 5, samples = 10, results };
    }
}
