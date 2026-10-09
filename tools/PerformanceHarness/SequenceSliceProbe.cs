using System.Diagnostics;
using SharpPy;

internal static class SequenceSliceProbe
{
    public static object Run()
    {
        var results = new List<object>();
        foreach (int size in new[] { 16, 1024 })
        {
            var items = Enumerable.Repeat<PyObject>(SmallIntCache.GetOrCreate(7), size).ToArray();
            var bytes = Enumerable.Repeat((byte)97, size).ToArray();
            foreach (PyObject source in new PyObject[] { new PyBytes(bytes), new PyByteArray(bytes),
                new PyString(new string('a', size)), new PyList(items), new PyTuple(items) })
            foreach (int step in new[] { 1, 2, -1 })
            {
                var slice = new PySlice(PyNone.Instance, PyNone.Instance, new PyInt(step));
                int expectedLength = step == 2 ? size / 2 : size;
                int count = size == 16 ? 1000 : 100;
                void Batch()
                {
                    PyObject last = source;
                    for (int i = 0; i < count; i++) last = source.GetItem(slice);
                    if (last.Length() != expectedLength) throw new InvalidOperationException("slice length");
                    GC.KeepAlive(last);
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
                results.Add(new { type = source.GetTypeName(), size, step, count, milliseconds, allocated_bytes = allocatedBytes });
            }
        }
        return new { warmups = 100, results };
    }
}
