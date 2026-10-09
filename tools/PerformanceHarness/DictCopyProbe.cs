using System.Diagnostics;
using SharpPy;

internal static class DictCopyProbe
{
    // Count native hash dispatch separately; Python-defined __hash__ dispatch is
    // an independent compatibility issue and is not inferred from this check.
    private sealed class HashKey : PyObject
    {
        public int Calls;
        public override int ToHash() { Calls++; return 17; }
    }

    public static object Run()
    {
        var key = new HashKey();
        var source = new PyDict();
        source.SetItem(key, PyNone.Instance);
        key.Calls = 0;
        var clone = source.Copy();
        int copyHashCalls = key.Calls;
        if (clone.Length() != 1) throw new InvalidOperationException("copy lost key");
        var results = new List<object>();
        foreach (int size in new[] { 0, 5, 100, 1000 })
        foreach (bool deleted in new[] { false, true })
        {
            var dictionary = new PyDict();
            for (int i = 0; i < size; i++) dictionary.SetItem(new PyInt(i), new PyInt(i));
            if (deleted)
            {
                for (int i = 0; i < size; i += 2) dictionary.DelItem(new PyInt(i));
                dictionary.SetItem(new PyInt(-1), PyNone.Instance);
            }
            const int iterations = 1000;
            void Copy()
            {
                PyDict last = dictionary;
                for (int i = 0; i < iterations; i++) last = dictionary.Copy();
                if (last.Length() != dictionary.Length()) throw new InvalidOperationException("copy size");
                GC.KeepAlive(last);
            }
            for (int i = 0; i < 10; i++) Copy();
            var times = new double[10];
            var allocations = new long[10];
            for (int i = 0; i < 10; i++)
            {
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                Copy();
                times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                allocations[i] = GC.GetAllocatedBytesForCurrentThread() - allocated;
            }
            results.Add(new { size, deleted, iterations, milliseconds = times, allocated_bytes = allocations });
        }
        return new { copy_hash_calls = copyHashCalls, warmups = 10, results };
    }
}
