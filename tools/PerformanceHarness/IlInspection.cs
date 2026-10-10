using System.Reflection;
using System.Reflection.Emit;
using SharpPy;

// Static IL evidence, not a count of runtime executions or framework-internal boxing.
internal static class IlInspection
{
    public static object Inspect()
    {
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(opcode => unchecked((ushort)opcode.Value));
        var results = new List<object>();
        var targets = new (Type Type, string Name)[]
        {
            (typeof(PyInt), "get_Value"), (typeof(PyInt), "Add"), (typeof(PyInt), "FloorDivide"),
            (typeof(PyStack), "Push"), (typeof(PyStack), "Pop"),
            (typeof(PyVM), "ExecuteBinaryOpType"), (typeof(PyVM), "TryInplaceOp"),
            (typeof(PyVM), "ExecuteMakeFunction"),
            (typeof(PyVM), "ExecuteInstruction"), (typeof(PyFunction), "Call"),
            (typeof(PyObject), "GenericGetAttribute"), (typeof(PyFrame), ".ctor"),
            (typeof(PyObject), "LookupSpecialMethodUncached"), (typeof(PyObject), "BindSpecialMethod"),
        };
        foreach (var target in targets)
        {
            IEnumerable<MethodBase> methods = target.Name == ".ctor"
                ? target.Type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                : target.Type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(method => method.Name == target.Name);
            foreach (var method in methods)
            {
                byte[]? bytes = method.GetMethodBody()?.GetILAsByteArray();
                if (bytes is null) continue;
                var boxing = new List<object>();
                var constructors = new Dictionary<string, int>();
                var constructorCalls = new List<object>();
                int position = 0;
                while (position < bytes.Length)
                {
                    int offset = position;
                    ushort value = bytes[position++];
                    if (value == 0xfe) value = (ushort)(0xfe00 | bytes[position++]);
                    var opcode = opcodes[value];
                    int size = opcode.OperandType switch
                    {
                        OperandType.InlineNone => 0,
                        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                        OperandType.InlineVar => 2,
                        OperandType.InlineI8 or OperandType.InlineR => 8,
                        OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, position),
                        _ => 4,
                    };
                    if (opcode == OpCodes.Box || opcode == OpCodes.Unbox || opcode == OpCodes.Unbox_Any)
                    {
                        string type = method.Module.ResolveType(BitConverter.ToInt32(bytes, position)).ToString();
                        boxing.Add(new { offset, opcode = opcode.Name, type });
                    }
                    if (opcode == OpCodes.Newobj)
                    {
                        var constructor = method.Module.ResolveMethod(BitConverter.ToInt32(bytes, position));
                        string name = constructor?.DeclaringType?.ToString() ?? "unknown";
                        constructors[name] = constructors.GetValueOrDefault(name) + 1;
                        constructorCalls.Add(new { offset, type = name, value_type = constructor?.DeclaringType?.IsValueType });
                    }
                    position += size;
                }
                results.Add(new { type = target.Type.FullName, method = method.ToString(), boxing, constructors, constructor_calls = constructorCalls });
            }
        }
        var closures = typeof(PyVM).GetNestedTypes(BindingFlags.NonPublic)
            .Where(type => type.Name.StartsWith("<>c__DisplayClass", StringComparison.Ordinal))
            .Select(type => new { type = type.FullName,
                fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Select(field => new { field.Name, type = field.FieldType.ToString() }).ToArray() }).ToArray();
        return new { scope = "Selected SharpPy method IL; allocation/call frequencies and runtime transitive boxing are not measured",
            instruction_size_bytes = System.Runtime.CompilerServices.Unsafe.SizeOf<ByteCodeInstruction>(),
            instruction_contains_references = System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<ByteCodeInstruction>(),
            methods = results, compiler_closures = closures,
            allocation_probes = AllocationProbes() };
    }

    private static object AllocationProbes()
    {
        const int iterations = 10000;
        var seed = new PyInt(1);
        _ = SmallIntCache.GetOrCreate(1); // Exclude first-time descriptor/cache initialization.
        var references = new PyObject[iterations];
        var boxes = new object[iterations];
        long start = GC.GetAllocatedBytesForCurrentThread();
        int checksum = 0;
        for (int i = 0; i < iterations; i++)
        {
            PyObject reference = seed;
            checksum += ((PyInt)reference).Value.Sign;
        }
        long referenceBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        if (checksum != iterations) throw new InvalidOperationException("Reference cast probe failed");
        start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++) references[i] = new PyInt(i);
        long wrapperBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++) references[i] = SmallIntCache.GetOrCreate(i % 257);
        long cachedBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++) boxes[i] = i;
        long boxBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        start = GC.GetAllocatedBytesForCurrentThread();
        checksum = 0;
        for (int i = 0; i < iterations; i++) checksum += (int)boxes[i];
        long unboxBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        if (checksum != iterations * (iterations - 1) / 2) throw new InvalidOperationException("Unbox probe failed");
        GC.KeepAlive(references);
        GC.KeepAlive(boxes);
        return new { iterations, reference_cast_bytes = referenceBytes, pyint_wrapper_bytes = wrapperBytes,
            cached_pyint_bytes = cachedBytes, clr_int_box_bytes = boxBytes, clr_int_unbox_bytes = unboxBytes,
            scope = "Small integers; output arrays allocated before measurement; reference casts and unboxing copy/read existing values" };
    }
}
