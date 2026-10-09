using System.Diagnostics;
using SharpPy;

internal static class BytesComparisonProbe
{
    public static object Run(bool protocol = false)
    {
        var results = new List<object>();
        foreach (int size in new[] { 32, 1024 })
        foreach (string scenario in new[] { "equal", "first_diff", "last_diff", "length_diff" })
        {
            var data = Enumerable.Repeat((byte)97, size).ToArray();
            var other = Enumerable.Repeat((byte)97, scenario == "length_diff" ? size - 1 : size).ToArray();
            if (scenario == "first_diff") other[0] = 98;
            if (scenario == "last_diff") other[^1] = 98;
            var left = new PyBytes(data);
            var right = new PyBytes(other);
            const int count = 100000;
            void Batch()
            {
                int equal = 0;
                for (int i = 0; i < count; i++)
                    if (protocol ? left.RichCompare(right, PyObject.CompareOp.EQ) == PyBool.True : left.Equals(right)) equal++;
                if (equal != (scenario == "equal" ? count : 0))
                    throw new InvalidOperationException("bytes equality result");
            }
            for (int i = 0; i < 10; i++) Batch();
            var milliseconds = new double[10];
            var allocatedBytes = new long[10];
            for (int i = 0; i < 10; i++)
            {
                long allocation = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                Batch();
                milliseconds[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                allocatedBytes[i] = GC.GetAllocatedBytesForCurrentThread() - allocation;
            }
            results.Add(new { size, scenario, count, milliseconds, allocated_bytes = allocatedBytes });
        }
        return new { scope = protocol ? "Python RichCompare: final implementation only" : "CLR Equals: both versions compare contents correctly", warmups = 10, results };
    }
}
