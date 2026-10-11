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
        return new { iterations, construction_bytes = constructionBytes,
            first_comparison_bytes = firstBytes, repeated_comparison_bytes = repeatBytes };
    }
}
