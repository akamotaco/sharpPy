using System.Diagnostics;
using SharpPy;

internal static class ListSliceProbe
{
    public static object Run()
    {
        var results = new List<object>();
        foreach (int size in new[] { 16, 1024 })
        {
            var items = new PyObject[size];
            Array.Fill(items, SmallIntCache.GetOrCreate(0));
            var target = new PyList(items);
            var values = new PyObject[size / 2];
            Array.Fill(values, SmallIntCache.GetOrCreate(7));
            var replacement = new PyList(values);
            var slice = new PySlice(new PyInt(0), new PyInt(size), new PyInt(2));
            var zero = new PyInt(0);
            var one = new PyInt(1);
            int count = size == 16 ? 10000 : 100;
            void Batch()
            {
                for (int i = 0; i < count; i++) target.SetItem(slice, replacement);
                if (target.GetItem(zero).ToInt() != 7 || target.GetItem(one).ToInt() != 0)
                    throw new InvalidOperationException("list slice assignment result");
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
            results.Add(new { size, count, milliseconds, allocated_bytes = allocatedBytes });
        }
        return new { warmups = 10, results };
    }
}
