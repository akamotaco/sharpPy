using System.Diagnostics;
using SharpPy;

internal static class MemoryViewProbe
{
    public static object Run(bool copyOnly = false)
    {
        var data = new byte[1024];
        var exporter = new PyByteArray(data);
        var view = exporter.GetBuffer(0);
        var slice = new PySlice(new PyInt(0), new PyInt(1024));
        var index = new PyInt(0);
        var lastIndex = new PyInt(1023);
        var writtenValue = new PyInt(97);
        var source = new PyMemoryView(Enumerable.Repeat((byte)97, 1024).ToArray(), true);
        var results = new List<object>();
        foreach (string operation in copyOnly ? new[] { "copy" } : new[] { "create", "slice", "read", "write" })
        {
            int count = operation == "read" || operation == "write" ? 100000 : 10000;
            void RunBatch()
            {
                PyObject last = view;
                for (int i = 0; i < count; i++)
                {
                    if (operation == "copy") view.SetItem(slice, source);
                    else if (operation == "write") view.SetItem(index, writtenValue);
                    else
                        last = operation == "create" ? exporter.GetBuffer(0)
                            : operation == "slice" ? view.GetItem(slice) : view.GetItem(index);
                }
                if (operation == "read" ? last.ToInt() != 0 : last.Length() != 1024)
                    throw new InvalidOperationException("memoryview probe result");
                if (operation == "write" && view.GetItem(index).ToInt() != 97)
                    throw new InvalidOperationException("memoryview write result");
                if (operation == "copy" && (view.GetItem(index).ToInt() != 97 || view.GetItem(lastIndex).ToInt() != 97))
                    throw new InvalidOperationException("memoryview copy result");
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
