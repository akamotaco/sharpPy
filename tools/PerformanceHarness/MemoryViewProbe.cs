using System.Diagnostics;
using SharpPy;

internal static class MemoryViewProbe
{
    public static object Run()
    {
        var data = new byte[1024];
        var exporter = new PyByteArray(data);
        var view = exporter.GetBuffer(0);
        var slice = new PySlice(new PyInt(0), new PyInt(1024));
        var index = new PyInt(0);
        var results = new List<object>();
        foreach (string operation in new[] { "create", "slice", "read" })
        {
            int count = operation == "read" ? 100000 : 10000;
            void RunBatch()
            {
                PyObject last = view;
                for (int i = 0; i < count; i++)
                    last = operation == "create" ? exporter.GetBuffer(0)
                        : operation == "slice" ? view.GetItem(slice) : view.GetItem(index);
                if (operation == "read" ? last.ToInt() != 0 : last.Length() != 1024)
                    throw new InvalidOperationException("memoryview probe result");
                GC.KeepAlive(last);
            }
            for (int i = 0; i < 10; i++) RunBatch();
            var times = new double[10];
            var allocations = new long[10];
            for (int i = 0; i < 10; i++)
            {
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                RunBatch();
                times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                allocations[i] = GC.GetAllocatedBytesForCurrentThread() - allocated;
            }
            results.Add(new { operation, count, milliseconds = times, allocated_bytes = allocations });
        }
        return new { buffer_size = 1024, warmups = 10, results };
    }
}
