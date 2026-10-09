using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using SharpPy;

if (args.Length > 0 && args[0] == "--inspect")
{
    Console.WriteLine(JsonSerializer.Serialize(IlInspection.Inspect()));
    return;
}
if (args.Length > 0 && args[0] == "--descriptor-check")
{
    DescriptorCallChecks.Run();
    Console.WriteLine("C# descriptor checks: PASS");
    return;
}

// Compile once, warm up, then time calls to the same function/code object.
// Allocation counters measure this synchronous thread; retained memory is not measured.
var source = File.ReadAllText(args[0]);
int iterations = args.Length > 1 ? int.Parse(args[1]) : 10000;
int samples = args.Length > 2 ? int.Parse(args[2]) : 10;
if (iterations < 1 || samples < 1) throw new ArgumentOutOfRangeException();
var interpreter = new IntegratedPythonInterpreter();
var setupStart = Stopwatch.GetTimestamp();
interpreter.Execute(source, args[0], false, false, false);
double setupMilliseconds = Stopwatch.GetElapsedTime(setupStart).TotalMilliseconds;
var workloads = new[] { "addition", "inplace_addition", "arithmetic", "function_call", "attributes", "dictionary", "comprehension", "bound_builtin" };
long sum = (long)iterations * (iterations - 1) / 2;
var callArgs = new PyObject[] { new PyInt(iterations) };
var results = new List<object>();
foreach (string name in workloads)
{
    var function = interpreter.GetGlobalVariable(name);
    long expected = name == "arithmetic" ? sum - iterations : sum;
    void Check(PyObject value)
    {
        if (value is not PyInt number || number.Value != expected)
            throw new InvalidOperationException($"{name}: expected {expected}, got {value}");
    }
    for (int warmup = 0; warmup < 10; warmup++) Check(function.Call(callArgs, null));
    var durations = new double[samples];
    var allocations = new long[samples];
    var collections = new int[samples][];
    for (int sample = 0; sample < samples; sample++)
    {
        var gcBefore = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        var value = function.Call(callArgs, null);
        durations[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        allocations[sample] = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        collections[sample] = new[] { GC.CollectionCount(0) - gcBefore[0], GC.CollectionCount(1) - gcBefore[1], GC.CollectionCount(2) - gcBefore[2] };
        Check(value);
    }
    results.Add(new { name, expected, milliseconds = durations, allocated_bytes = allocations, gc_collections = collections });
}
Console.WriteLine(JsonSerializer.Serialize(new
{
    framework = RuntimeInformation.FrameworkDescription,
    architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    os = RuntimeInformation.OSDescription,
    iterations, samples, warmups = 10, setup_milliseconds = setupMilliseconds, results
}));
