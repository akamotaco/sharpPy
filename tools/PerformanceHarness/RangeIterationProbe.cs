using System.Diagnostics;
using SharpPy;

internal static class RangeIterationProbe
{
    public static void VerifyBoundaries()
    {
        long lo = long.MinValue, hi = long.MaxValue;
        foreach (var (start, stop, step, expected) in new[] {
            (hi - 1, hi, 2L, new[] { hi - 1 }),
            (lo + 1, lo, -2L, new[] { lo + 1 }),
            (0L, lo, lo, new[] { 0L }),
            (lo, hi, hi, new[] { lo, -1L, hi - 1 }),
            (hi, lo, lo, new[] { hi, -1L }) })
        {
            var values = new PyRange(start, stop, step).GetValues()
                .Take(expected.Length + 1).Select(value => (long)value.Value);
            if (!values.SequenceEqual(expected))
                throw new InvalidOperationException("GetValues boundary mismatch");
        }
    }

    public static object Run()
    {
        var results = new List<object>();
        foreach (int size in new[] { 0, 1, 16, 1024 })
        foreach (int step in new[] { 1, -1 })
        foreach (string operation in new[] { "iterator", "values", "length" })
        {
            var range = new PyRange(0, size * step, step);
            int count = operation == "length" ? 10000 : size <= 16 ? 1000 : 100;
            long expectedSum = (long)step * size * (size - 1) / 2 * count;
            void Batch()
            {
                long sum = 0;
                for (int i = 0; i < count; i++)
                {
                    if (operation == "length") sum += range.LengthLong();
                    else if (operation == "values")
                    {
                        foreach (var value in range.GetValues()) sum += (long)value.Value;
                    }
                    else
                    {
                        var iterator = range.GetIterator();
                        for (int j = 0; j < size; j++) sum += (long)((PyInt)iterator.Next()).Value;
                        // Exception construction is outside this throughput probe.
                        GC.KeepAlive(iterator);
                    }
                }
                if (sum != (operation == "length" ? (long)size * count : expectedSum))
                    throw new InvalidOperationException("range probe result");
            }
            for (int i = 0; i < 10; i++) Batch();
            var milliseconds = new double[10];
            var allocations = new long[10];
            for (int i = 0; i < 10; i++)
            {
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                long started = Stopwatch.GetTimestamp();
                Batch();
                milliseconds[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                allocations[i] = GC.GetAllocatedBytesForCurrentThread() - allocated;
            }
            results.Add(new { size, step, operation, count, milliseconds, allocated_bytes = allocations });
        }
        return new { warmups = 10, scope = "Native range iteration and length; excludes VM and StopIteration construction", results };
    }
}
