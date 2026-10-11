using System.Diagnostics;
using SharpPy;

internal static class ComparisonCacheProbe
{
    public static object Run()
    {
        const int iterations = 1000;
        var bases = new PyType[] { PyType.IntType };
        var value = new PyInt(1);
        void Check(PyIntSubclass instance)
        {
            if (instance.RichCompare(value, PyObject.CompareOp.EQ) != PyBool.True)
                throw new InvalidOperationException("Inherited comparison changed its result");
        }
        // Warm construction, comparison and JIT before measuring first use on new types.
        for (int i = 0; i < 100; i++)
            Check(new PyIntSubclass(new PyClass("Probe", bases), value));

        long constructionBytes = 0, firstBytes = 0, repeatBytes = 0;
        for (int i = 0; i < iterations; i++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            var cls = new PyClass("Probe", bases);
            constructionBytes += GC.GetAllocatedBytesForCurrentThread() - before;
            var instance = new PyIntSubclass(cls, value);
            before = GC.GetAllocatedBytesForCurrentThread();
            Check(instance);
            firstBytes += GC.GetAllocatedBytesForCurrentThread() - before;
            before = GC.GetAllocatedBytesForCurrentThread();
            Check(instance);
            repeatBytes += GC.GetAllocatedBytesForCurrentThread() - before;
        }
        // Invalid native enum values must still fail before a cached bit is used.
        var receivers = new PyObject[] {
            new PyIntSubclass(new PyClass("Number", bases), value),
            new PyClassInstance(new PyClass("Object", new[] { PyType.ObjectType }))
        };
        int invalidChecks = 0;
        foreach (var receiver in receivers)
        {
            receiver.RichCompare(value, PyObject.CompareOp.LT);
            foreach (int invalid in new[] { -1, 6, 32, 64 })
            {
                try
                {
                    receiver.RichCompare(value, (PyObject.CompareOp)invalid);
                    throw new InvalidOperationException("Invalid comparison operator was accepted");
                }
                catch (PythonException error) when (error.PyException is PyValueError &&
                    error.PyException.Message == $"Invalid comparison operator: {invalid}")
                {
                    invalidChecks++;
                }
            }
        }
        // Host subclasses must retain virtual comparison dispatch in both directions.
        var host = new HostNumber(new PyClass("HostNumber", bases), value);
        int hostChecks = 0;
        foreach (PyObject.CompareOp op in Enum.GetValues<PyObject.CompareOp>())
        {
            if (!ReferenceEquals(PyObject.Compare(host, value, op), host.Result) ||
                !ReferenceEquals(PyObject.Compare(value, host, op), host.Result))
                throw new InvalidOperationException("Host comparison override was bypassed");
            hostChecks += 2;
        }
        return new { iterations, construction_bytes = constructionBytes,
            first_comparison_bytes = firstBytes, repeated_comparison_bytes = repeatBytes,
            invalid_operator_checks = invalidChecks, host_override_checks = hostChecks };
    }

    private sealed class HostNumber : PyIntSubclass
    {
        internal readonly PyObject Result = new PyString("host comparison");
        internal HostNumber(PyClass cls, PyInt value) : base(cls, value) { }
        public override PyObject RichCompare(PyObject other, CompareOp op) => Result;
    }
}
