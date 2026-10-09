using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using SharpPy;

if (args.Length > 0 && args[0] == "--dict-view-probe")
{
    var probeDict = new PyDict();
    for (int i = 0; i < 100; i++) probeDict.SetItem(new PyInt(i), new PyInt(i));
    var keysMethod = probeDict.GetAttribute("keys");
    PyObject? RunViews()
    {
        PyObject? last = null;
        for (int i = 0; i < 10000; i++) last = keysMethod.Call(Array.Empty<PyObject>(), null);
        if (last?.Length() != 100) throw new InvalidOperationException("dict view probe failed");
        return last;
    }
    for (int i = 0; i < 10; i++) GC.KeepAlive(RunViews());
    var viewTimes = new double[10];
    var viewAllocations = new long[10];
    for (int i = 0; i < 10; i++)
    {
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        var last = RunViews();
        viewTimes[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        viewAllocations[i] = GC.GetAllocatedBytesForCurrentThread() - allocated;
        GC.KeepAlive(last);
    }
    Console.WriteLine(JsonSerializer.Serialize(new { iterations = 10000, dict_size = 100, warmups = 10,
        scope = "Native bound dict.keys calls; excludes iteration and Python VM",
        milliseconds = viewTimes, allocated_bytes = viewAllocations }));
    return;
}

if (args.Length > 0 && args[0] == "--dict-storage-probe")
{
    var key = new PyInt(1000);
    var value = new PyInt(7);
    PyDict? Run(int count)
    {
        PyDict? last = null;
        for (int i = 0; i < count; i++)
        {
            last = new PyDict();
            last.SetItem(key, value);
            if (!ReferenceEquals(last.Pop(key), value) || last.Length() != 0)
                throw new InvalidOperationException("dict storage probe failed");
        }
        return last;
    }
    const int probeIterations = 10000;
    for (int i = 0; i < 10; i++) GC.KeepAlive(Run(probeIterations));
    var durations = new double[10];
    var allocations = new long[10];
    for (int i = 0; i < 10; i++)
    {
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        var last = Run(probeIterations);
        durations[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        allocations[i] = GC.GetAllocatedBytesForCurrentThread() - allocated;
        GC.KeepAlive(last);
    }
    Console.WriteLine(JsonSerializer.Serialize(new { iterations = probeIterations, warmups = 10,
        scope = "Native PyDict construction, one insertion and pop of identical key; excludes Python VM",
        milliseconds = durations, allocated_bytes = allocations }));
    return;
}

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
if (args.Length > 0 && args[0] == "--frame-allocations")
{
    Console.WriteLine(JsonSerializer.Serialize(FrameAllocationProbes.Run()));
    return;
}
if (args.Length > 0 && args[0] == "--frame-check")
{
    FrameChecks.Run();
    Console.WriteLine("C# frame checks: PASS");
    return;
}
if (args.Length > 0 && args[0] == "--class-closure-inspect")
{
    Console.WriteLine(JsonSerializer.Serialize(ClassClosureInspection.Inspect(args[1])));
    return;
}
if (args.Length > 0 && args[0] == "--exception-table-inspect")
{
    Console.WriteLine(JsonSerializer.Serialize(ExceptionTableInspection.Inspect(args[1])));
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
var workloads = new[] { "addition", "inplace_addition", "arithmetic", "function_call", "attributes", "dictionary", "comprehension", "bound_builtin", "locals_observation", "frame_observation", "class_locals_observation", "exception_handling" };
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
