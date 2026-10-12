using System.Diagnostics;
using System.Runtime.CompilerServices;

// An isolated layout/lookup experiment, not a runtime ownership implementation.
internal static class OwnershipCostProbe
{
    private class Plain { public object? Payload; }
    private sealed class Counted : Plain { public int Owners = 1; }
    private sealed class Counter { public int Owners = 1; }
    internal static object Run()
    {
        const int count = 100_000;
        var keep = new Plain[count];
        var table = new ConditionalWeakTable<Plain, Counter>();
        var plain = new Plain();
        var counted = new Counted();
        table.Add(plain, new Counter());
        var operations = new Dictionary<string, Action> {
            ["plain_allocation"] = () => { for (int i = 0; i < count; i++) keep[i] = new Plain(); },
            ["counter_field_allocation"] = () => { for (int i = 0; i < count; i++) keep[i] = new Counted(); },
            ["side_table_registration"] = () => {
                table = new ConditionalWeakTable<Plain, Counter>();
                table.Add(plain, new Counter());
                for (int i = 0; i < count; i++) { var value = new Plain(); keep[i] = value; table.Add(value, new Counter()); }
            },
            ["field_acquire_release"] = () => { for (int i = 0; i < count; i++) FieldPair(counted); },
            ["side_table_acquire_release"] = () => { for (int i = 0; i < count; i++) SidePair(table, plain); },
        };
        var results = new Dictionary<string, object>();
        foreach (var (name, operation) in operations)
        {
            for (int warmup = 0; warmup < 3; warmup++) operation();
            var times = new double[11];
            var allocations = new long[11];
            for (int sample = 0; sample < times.Length; sample++)
            {
                long bytes = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                operation();
                times[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                allocations[sample] = GC.GetAllocatedBytesForCurrentThread() - bytes;
            }
            results[name] = new { milliseconds = times, allocated_bytes = allocations };
        }
        if (counted.Owners != 1 || table.GetValue(plain, _ => throw new InvalidOperationException()).Owners != 1)
            throw new InvalidOperationException("Unbalanced candidate ownership pair");
        GC.KeepAlive(keep);
        return new { count, samples = 11, warmups = 3,
            scope = "Synthetic field layout and paired ownership operations; excludes VM integration, graph traversal, cycles and destructor callbacks", results };
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void FieldPair(Counted value) { value.Owners++; Release(value); }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Release(Counted value) { value.Owners--; }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SidePair(ConditionalWeakTable<Plain, Counter> table, Plain value)
    {
        table.TryGetValue(value, out var first); first!.Owners++;
        table.TryGetValue(value, out var second); second!.Owners--;
    }
}
