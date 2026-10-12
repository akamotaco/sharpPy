using System.Diagnostics;
using SharpPy;

internal static class ComparisonDispatchProbe
{
    internal static object Run(bool slotsOnly = false)
    {
        const int iterations = 1_000_000;
        var integer = new PyInt(10);
        var real = new PyFloat(20.5);
        var derived = new PyIntSubclass(new PyClass("Number", new[] { PyType.IntType }), integer);
        var operations = new Dictionary<string, Func<PyObject>> {
            ["control"] = () => PyBool.True,
            ["payload_slot"] = () => integer.RichCompare(real, PyObject.CompareOp.LT),
            ["subclass_slot"] = () => derived.RichCompare(real, PyObject.CompareOp.LT),
            ["payload_dispatch"] = () => PyObject.Compare(integer, real, PyObject.CompareOp.LT),
            ["subclass_dispatch"] = () => PyObject.Compare(derived, real, PyObject.CompareOp.LT),
        };
        var results = new Dictionary<string, object>();
        // Older saved runtimes predate the public Compare dispatch entry point.
        if (slotsOnly) { operations.Remove("payload_dispatch"); operations.Remove("subclass_dispatch"); }
        foreach (var (name, operation) in operations)
        {
            void Batch()
            {
                for (int n = 0; n < iterations; n++)
                    if (!ReferenceEquals(operation(), PyBool.True))
                        throw new InvalidOperationException("Comparison result changed: " + name);
            }
            for (int i = 0; i < 5; i++) Batch();
            var times = new double[15];
            var allocations = new long[15];
            for (int i = 0; i < times.Length; i++)
            {
                long bytes = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                Batch();
                times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                allocations[i] = GC.GetAllocatedBytesForCurrentThread() - bytes;
            }
            results[name] = new { milliseconds = times, allocated_bytes = allocations };
        }
        return new { iterations, warmups = 5, samples = 15, results };
    }
}
