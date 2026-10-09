using System.Diagnostics;
using SharpPy;

internal static class ByteArraySliceMutationProbe
{
    public static object Run(string onlyOperation = null)
    {
        if (onlyOperation != null && onlyOperation != "assign" && onlyOperation != "delete")
            throw new ArgumentException("Expected assign or delete", nameof(onlyOperation));
        var results = new List<object>();
        foreach (int size in new[] { 16, 1024 })
        foreach (int step in new[] { 2, -2 })
        foreach (string operation in new[] { "assign", "delete" })
        {
            if (onlyOperation != null && operation != onlyOperation) continue;
            var data = Enumerable.Repeat((byte)97, size).ToArray();
            var replacement = new PyBytes(Enumerable.Repeat((byte)98, size / 2).ToArray());
            var slice = new PySlice(PyNone.Instance, PyNone.Instance, new PyInt(step));
            var target = new PyByteArray(data);
            int count = size == 16 ? 1000 : 100;
            void Batch()
            {
                for (int i = 0; i < count; i++)
                {
                    if (operation == "assign") target.SetItem(slice, replacement);
                    else
                    {
                        target = new PyByteArray(data);
                        target.DelItem(slice);
                    }
                }
                if (target.Length() != (operation == "assign" ? size : size / 2))
                    throw new InvalidOperationException("bytearray mutation result");
                GC.KeepAlive(target);
            }
            for (int i = 0; i < 100; i++) Batch();
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
            results.Add(new { size, step, operation, count, milliseconds, allocated_bytes = allocatedBytes });
        }
        return new { warmups = 100, deletion_includes_target_creation = true, results };
    }
}
