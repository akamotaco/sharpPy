using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using SharpPy;

if (args.Length > 1 && args[0] == "--float-format-probe")
{
    Console.WriteLine(JsonSerializer.Serialize(FloatFormatProbe.Run(args[1])));
    return;
}

if (args.Length > 1 && args[0] == "--unicode-repr-probe")
{
    Console.WriteLine(JsonSerializer.Serialize(UnicodeReprProbe.Run(args[1])));
    return;
}

if (args.Length > 0 && args[0] == "--ownership-cost-probe")
{
    Console.WriteLine(JsonSerializer.Serialize(OwnershipCostProbe.Run()));
    return;
}

if (args.Length > 0 && args[0] == "--host-namespace-probe")
{
    Console.WriteLine(JsonSerializer.Serialize(HostNamespaceProbe.Run()));
    return;
}

if (args.Length > 0 && args[0] == "--comparison-dispatch-probe")
{
    Console.WriteLine(JsonSerializer.Serialize(ComparisonDispatchProbe.Run(args.Contains("--slots-only"))));
    return;
}

if (args.Length > 0 && args[0] == "--comparison-cache-probe")
{
    Console.WriteLine(JsonSerializer.Serialize(ComparisonCacheProbe.Run()));
    return;
}

if (args.Length > 0 && args[0] == "--exception-factory-probe")
{
    Console.WriteLine(JsonSerializer.Serialize(ExceptionFactoryProbe.Run(args.Contains("--mapped-errno"), args.Contains("--directory-errno"))));
    return;
}

if (args.Length > 0 && args[0] == "--cfg-reachability-check")
{
    Console.WriteLine(JsonSerializer.Serialize(CfgReachabilityProbe.Run()));
    return;
}

if (args.Length > 0 && args[0] == "--bytecode-inspect")
{
    Console.WriteLine(JsonSerializer.Serialize(BytecodeInspection.Inspect(args[1])));
    return;
}

if (args.Length > 0 && args[0] == "--cfg-retention-inspect")
{
    Console.WriteLine(JsonSerializer.Serialize(CfgRetentionInspection.Inspect(args[1],
        args.Length > 2 && args[2] == "--require-detached")));
    return;
}

if (args.Length > 0 && args[0] == "--instruction-offset-probe")
{
    Console.WriteLine(JsonSerializer.Serialize(InstructionOffsetProbe.Run()));
    return;
}

if (args.Length > 0 && args[0] == "--range-extended-check")
{
    RangeConstructionProbe.VerifyExtended();
    Console.WriteLine("range C# helpers: PASS");
    return;
}

if (args.Length > 0 && args[0] == "--range-construction-probe")
{
    Console.WriteLine(JsonSerializer.Serialize(RangeConstructionProbe.Run()));
    return;
}

if (args.Length > 0 && args[0] == "--range-boundary-check")
{
    RangeIterationProbe.VerifyBoundaries();
    Console.WriteLine("range native GetValues: PASS");
    return;
}

if (args.Length > 0 && args[0] == "--range-iteration-probe")
{
    Console.WriteLine(JsonSerializer.Serialize(RangeIterationProbe.Run()));
    return;
}

if (args.Length > 0 && args[0] == "--bytearray-slice-mutation-probe")
{
    Console.WriteLine(JsonSerializer.Serialize(ByteArraySliceMutationProbe.Run(args.Length > 1 ? args[1] : null)));
    return;
}

if (args.Length > 0 && args[0] == "--sequence-slice-probe")
{
    Console.WriteLine(JsonSerializer.Serialize(SequenceSliceProbe.Run()));
    return;
}

if (args.Length > 0 && args[0] == "--list-slice-probe")
{
    Console.WriteLine(JsonSerializer.Serialize(ListSliceProbe.Run()));
    return;
}

if (args.Length > 0 && (args[0] == "--bytes-comparison-probe" || args[0] == "--bytes-protocol-probe"))
{
    Console.WriteLine(JsonSerializer.Serialize(BytesComparisonProbe.Run(args[0] == "--bytes-protocol-probe")));
    return;
}

if (args.Length > 0 && (args[0] == "--memoryview-probe" || args[0] == "--memoryview-copy-probe"))
{
    Console.WriteLine(JsonSerializer.Serialize(MemoryViewProbe.Run(args[0] == "--memoryview-copy-probe")));
    return;
}

if (args.Length > 0 && args[0] == "--dict-copy-probe")
{
    Console.WriteLine(JsonSerializer.Serialize(DictCopyProbe.Run()));
    return;
}

if (args.Length > 0 && args[0] == "--globals-observation-probe")
{
    var observedBindings = new Dictionary<string, PyObject>();
    var observedValue = new PyInt(7);
    for (int i = 0; i < 100; i++) observedBindings["seed_" + i] = observedValue;
    PyDict observedGlobals = new PyGlobalsDict(observedBindings);
    if (args.Contains("--owned"))
    {
        var owned = new PyNamespace();
        foreach (var pair in observedBindings) owned[pair.Key] = pair.Value;
        observedGlobals = owned.PythonDictionary;
    }
    var observationResults = new List<object>();
    foreach (string operation in args.Length > 1 ? new[] { args[1] } : new[] { "length", "copy", "construct" })
    {
        if (operation != "length" && operation != "copy" && operation != "construct")
            throw new ArgumentException($"Unknown observation operation: {operation}");
        int operationCount = operation == "length" ? 10000 : 1000;
        void ObserveGlobals()
        {
            PyDict last = observedGlobals;
            for (int i = 0; i < operationCount; i++)
            {
                if (operation == "length")
                {
                    if (observedGlobals.Length() != 100) throw new InvalidOperationException("length probe failed");
                }
                else last = operation == "copy" ? observedGlobals.Copy() : new PyGlobalsDict(observedBindings);
            }
            if (last.Length() != 100) throw new InvalidOperationException("observation probe failed");
            GC.KeepAlive(last);
        }
        for (int i = 0; i < 10; i++) ObserveGlobals();
        var observationTimes = new double[10];
        var observationAllocations = new long[10];
        for (int i = 0; i < 10; i++)
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            ObserveGlobals();
            observationTimes[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            observationAllocations[i] = GC.GetAllocatedBytesForCurrentThread() - allocated;
        }
        observationResults.Add(new { operation, iterations = operationCount,
            milliseconds = observationTimes, allocated_bytes = observationAllocations });
    }
    Console.WriteLine(JsonSerializer.Serialize(new { namespace_size = 100, warmups = 10,
        scope = "Native length/copy/construction on a fixed namespace; excludes Python VM and external writes",
        results = observationResults }));
    return;
}

if (args.Length > 0 && args[0] == "--globals-read-probe")
{
    var readBindings = new Dictionary<string, PyObject>();
    var readValue = new PyInt(7);
    for (int i = 0; i < 100; i++) readBindings["seed_" + i] = readValue;
    var readGlobals = new PyGlobalsDict(readBindings);
    var readKey = new PyString("seed_50");
    void ReadGlobals()
    {
        for (int i = 0; i < 10000; i++)
        {
            if (!ReferenceEquals(readGlobals.GetItem(readKey), readValue) ||
                !ReferenceEquals(readGlobals.Get(readKey), readValue) || !readGlobals.Contains(readKey).Value)
                throw new InvalidOperationException("globals read probe failed");
        }
    }
    for (int i = 0; i < 10; i++) ReadGlobals();
    var readTimes = new double[10];
    var readAllocations = new long[10];
    for (int i = 0; i < 10; i++)
    {
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        ReadGlobals();
        readTimes[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        readAllocations[i] = GC.GetAllocatedBytesForCurrentThread() - allocated;
    }
    Console.WriteLine(JsonSerializer.Serialize(new { iterations = 10000, namespace_size = 100, warmups = 10,
        scope = "Native GetItem/Get/Contains and validation per iteration; excludes Python VM and missing-key exceptions",
        milliseconds = readTimes, allocated_bytes = readAllocations }));
    return;
}

if (args.Length > 0 && args[0] == "--globals-update-probe")
{
    var moduleBindings = new Dictionary<string, PyObject>();
    var probeValue = new PyInt(7);
    for (int i = 0; i < 100; i++) moduleBindings["seed_" + i] = probeValue;
    var globals = new PyGlobalsDict(moduleBindings);
    var changes = new PyDict();
    changes.SetItem(new PyString("updated"), probeValue);
    void UpdateGlobals()
    {
        for (int i = 0; i < 10000; i++) globals.Update(changes);
        if (!ReferenceEquals(moduleBindings["updated"], probeValue))
            throw new InvalidOperationException("globals update probe failed");
    }
    for (int i = 0; i < 10; i++) UpdateGlobals();
    var globalsTimes = new double[10];
    var globalsAllocations = new long[10];
    for (int i = 0; i < 10; i++)
    {
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        UpdateGlobals();
        globalsTimes[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        globalsAllocations[i] = GC.GetAllocatedBytesForCurrentThread() - allocated;
    }
    Console.WriteLine(JsonSerializer.Serialize(new { iterations = 10000, initial_namespace_size = 100,
        source_keys = 1, warmups = 10, scope = "Native PyGlobalsDict.Update; excludes Python VM",
        milliseconds = globalsTimes, allocated_bytes = globalsAllocations }));
    return;
}

if (args.Length > 0 && args[0] == "--dict-iterator-probe")
{
    bool traverse = args.Contains("--traverse");
    bool fresh = args.Contains("--fresh");
    int iteratorCount = traverse || fresh ? 1000 : 10000;
    var iteratorDict = new PyDict();
    for (int i = 0; i < 100; i++) iteratorDict.SetItem(new PyInt(i), new PyInt(i));
    var seed = iteratorDict.GetInternalDict().ToDictionary(entry => entry.Key, entry => entry.Value);
    PyDict[]? PrepareBatch()
    {
        if (!fresh) return null;
        var dictionaries = new PyDict[iteratorCount];
        for (int i = 0; i < dictionaries.Length; i++) dictionaries[i] = new PyDict(seed);
        return dictionaries;
    }
    var iteratorResults = new List<object>();
    foreach (string kind in new[] { "keys", "values", "items" })
    {
        PyObject RunIterators(PyDict[]? dictionaries)
        {
            PyObject last = PyNone.Instance;
            for (int i = 0; i < iteratorCount; i++)
            {
                var dictionary = dictionaries == null ? iteratorDict : dictionaries[i];
                PyIterator cursor = kind switch
                {
                    "keys" => new PyDictKeyIterator(dictionary),
                    "values" => new PyDictValueIterator(dictionary),
                    _ => new PyDictItemIterator(dictionary)
                };
                for (int step = 0; step < (traverse ? 100 : 1); step++) last = cursor.Next();
            }
            var number = last is PyTuple pair ? pair.Items[1] : last;
            if (number is not PyInt result || result.Value != (traverse ? 99 : 0))
                throw new InvalidOperationException("dict iterator probe failed");
            return last;
        }
        for (int i = 0; i < 10; i++) GC.KeepAlive(RunIterators(PrepareBatch()));
        var iteratorTimes = new double[10];
        var iteratorAllocations = new long[10];
        for (int i = 0; i < 10; i++)
        {
            var dictionaries = PrepareBatch(); // Preparation excluded from measurement.
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            var last = RunIterators(dictionaries);
            iteratorTimes[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            iteratorAllocations[i] = GC.GetAllocatedBytesForCurrentThread() - allocated;
            GC.KeepAlive(last);
            GC.KeepAlive(dictionaries);
        }
        iteratorResults.Add(new { kind, milliseconds = iteratorTimes, allocated_bytes = iteratorAllocations });
    }
    Console.WriteLine(JsonSerializer.Serialize(new { iterations = iteratorCount, dict_size = 100, warmups = 10,
        fresh_dictionaries = fresh, dictionary_preparation_measured = false,
        scope = traverse ? "Native iterator construction and 100 Next calls; excludes terminal StopIteration and Python VM"
            : "Native iterator construction and first Next only; excludes full traversal and Python VM",
        results = iteratorResults }));
    return;
}

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
// Diagnostic only: compare lookup-table contents without changing the runtime DLL.
if (args.Contains("--remove-os-subtype-names"))
{
    PyBuiltinsModule.Instance.BuiltinDict.Remove("PermissionError");
    PyBuiltinsModule.Instance.BuiltinDict.Remove("FileExistsError");
}
if (args.Contains("--add-os-subtype-names"))
{
    PyBuiltinsModule.Instance.BuiltinDict["PermissionError"] = PyType.OSErrorType;
    PyBuiltinsModule.Instance.BuiltinDict["FileExistsError"] = PyType.OSErrorType;
}

var workloads = new[] { "addition", "inplace_addition", "arithmetic", "function_call", "attributes", "dictionary", "dictionary_iteration", "dictionary_copy", "namespace_update", "namespace_read", "comprehension", "bound_builtin", "locals_observation", "frame_observation", "class_locals_observation", "exception_handling" };
if (args.Length > 3)
{
    string selected = args[3];
    if (!workloads.Contains(selected) && selected != "generator_iteration" && selected != "generator_close" && selected != "generator_delegation" && selected != "python_iterator" && selected != "iterator_acquisition" && selected != "metaclass_iteration" && selected != "classcell_binding" && selected != "super_lookup" && selected != "constant_return" && selected != "constant_cleanup" && selected != "closure_access" && selected != "nonlocal_update" && selected != "coroutine_send" && selected != "exception_construction" && selected != "lookup_exception_construction" && selected != "os_error_construction" && selected != "os_error_fallback" && selected != "os_error_winerror" && selected != "int_subclass_construction" && selected != "os_error_subclass_lookup" && selected != "integer_hash" && selected != "mixed_compare_small" && selected != "mixed_compare_large" && selected != "integer_compare" && selected != "int_subclass_compare" && selected != "int_subclass_float_compare" && selected != "user_compare" && selected != "sort_small" && selected != "sort_large" && selected != "sort_ordered") throw new ArgumentException($"Unknown workload: {selected}");
    workloads = new[] { selected };
}
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

