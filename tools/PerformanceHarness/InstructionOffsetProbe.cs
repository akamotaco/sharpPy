using System.Diagnostics;
using SharpPy;

internal static class InstructionOffsetProbe
{
    public static object Run()
    {
        var rows = new List<object>();
        foreach (int size in new[] { 16, 1024, 16384 })
        {
            var words = Enumerable.Range(0, size).Select(i => new ByteCodeInstruction(
                i % 3 == 0 ? ByteCodeOp.CACHE : i % 3 == 1 ? ByteCodeOp.EXTENDED_ARG : ByteCodeOp.NOP)).ToList();
            var code = new PyCodeObject("offset_probe", words, new(), new(), new());
            for (int i = 0; i < size; i++)
                if (code.ByteOffsetToInstructionIndex(i * 2) != i || code.InstructionIndexToByteOffset(i) != i * 2)
                    throw new Exception("offset round trip");
            foreach (int invalid in new[] { -1, size * 2, size * 2 + 1 })
            {
                try { code.ByteOffsetToInstructionIndex(invalid); throw new Exception("accepted invalid offset"); }
                catch (ArgumentOutOfRangeException) { }
            }
            // Odd in-range offsets are invalid even beside CACHE/EXTENDED_ARG.
            var output = Console.Out;
            try
            {
                Console.SetOut(TextWriter.Null);
                try { code.ByteOffsetToInstructionIndex(3); throw new Exception("accepted odd offset"); }
                catch (ArgumentException error) when (error is not ArgumentOutOfRangeException) { }
            }
            finally { Console.SetOut(output); }
            const int count = 10000;
            int sum = 0;
            void Batch()
            {
                for (int i = 0; i < count; i++) sum ^= code.ByteOffsetToInstructionIndex((size - 1) * 2);
            }
            for (int i = 0; i < 5; i++) Batch();
            var times = new double[10];
            var bytes = new long[10];
            for (int i = 0; i < 10; i++)
            {
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                long started = Stopwatch.GetTimestamp();
                Batch();
                times[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                bytes[i] = GC.GetAllocatedBytesForCurrentThread() - allocated;
            }
            GC.KeepAlive(sum);
            rows.Add(new { size, count, milliseconds = times, allocated_bytes = bytes });
        }
        return new { results = rows };
    }
}
