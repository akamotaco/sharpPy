using System.Diagnostics;
using SharpPy;

internal static class RangeConstructionProbe
{
    public static void VerifyExtended()
    {
        var huge = System.Numerics.BigInteger.Pow(10, 100);
        var builtin = new PyBuiltinFunction("range");
        var range = (PyRange)builtin.Call(new PyObject[] {new PyInt(huge), new PyInt(huge + 7), new PyInt(2)}, null);
        var expected = new[] { huge, huge + 2, huge + 4, huge + 6 };
        if (!range.GetValues().Select(v => v.Value).SequenceEqual(expected) ||
            !range.GetSlice(step: -1).GetValues().Select(v => v.Value).SequenceEqual(expected.Reverse()) ||
            range.GetItem(-1L).Value != huge + 6 || range.Min().Value != huge || range.Max().Value != huge + 6)
            throw new InvalidOperationException("big range C# helpers");
        var native = new PyRange(1, 8, 2);
        if (!native.GetSlice(step: -1).GetValues().Select(v => (long)v.Value).SequenceEqual(new long[] {7, 5, 3, 1}))
            throw new InvalidOperationException("native GetSlice reverse");
    }

    public static object Run()
    {
        var results = new List<object>();
        var builtin = new PyBuiltinFunction("range");
        var arguments = new PyObject[] { new PyInt(1), new PyInt(1025), new PyInt(2) };
        var source = new PyRange(1, 1025, 2);
        var slice = new PySlice(PyNone.Instance, PyNone.Instance, new PyInt(2));
        var index = new PyInt(3);
        foreach (string operation in new[] { "construct", "builtin", "slice", "index" })
        {
            const int count = 10000;
            void Batch()
            {
                PyObject result = source;
                for (int i = 0; i < count; i++)
                    result = operation switch
                    {
                        "construct" => new PyRange(1, 1025, 2),
                        "builtin" => builtin.Call(arguments, null),
                        "slice" => source.GetItem(slice),
                        _ => source.GetItem(index)
                    };
                if (operation == "index")
                {
                    if (((PyInt)result).Value != 7) throw new InvalidOperationException("range index");
                }
                else if (result.Length() != (operation == "slice" ? 256 : 512))
                    throw new InvalidOperationException("range construction");
                GC.KeepAlive(result);
            }
            for (int i = 0; i < 20; i++) Batch();
            var times = new double[10];
            var allocations = new long[10];
            for (int i = 0; i < 10; i++)
            {
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                long started = Stopwatch.GetTimestamp();
                Batch();
                times[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                allocations[i] = GC.GetAllocatedBytesForCurrentThread() - allocated;
            }
            results.Add(new { operation, count, milliseconds = times, allocated_bytes = allocations });
        }
        return new { warmups = 20, scope = "Native range creation, builtin call, slicing and indexing; excludes VM", results };
    }
}
