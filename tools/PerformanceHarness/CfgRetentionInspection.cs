using System.Security.Cryptography;
using System.Text.Json;
using SharpPy;
using SharpPy.Generated;

internal static class CfgRetentionInspection
{
    public static object Inspect(string path, bool requireDetached = false)
    {
        string source = File.ReadAllText(path);
        var tokens = PyParserRuntime.LexerSource(source);
        var statements = PyParserRuntime.ParseSource(tokens, source, path);
        var root = new PythonCompiler().Compile(statements, "<module>", new List<string>(), path);
        var rows = new List<object>();
        void Visit(PyCodeObject code, string parent)
        {
            string location = parent + "/" + code.Name;
            var blocks = new HashSet<BasicBlock>();
            var pending = new Queue<BasicBlock>();
            void Add(BasicBlock? block)
            {
                if (block != null && blocks.Add(block)) pending.Enqueue(block);
            }
            foreach (var word in code.Instructions) { Add(word.TargetBlock); Add(word.ExceptBlock); }
            int direct = blocks.Count;
            while (pending.Count != 0)
            {
                var block = pending.Dequeue();
                Add(block.Next);
                Add(block.ExceptionHandler);
                foreach (var successor in block.Successors) Add(successor);
                foreach (var word in block.Instructions) { Add(word.TargetBlock); Add(word.ExceptBlock); }
            }
            if (requireDetached && blocks.Count != 0)
                throw new InvalidOperationException($"{location}: final code retains CFG blocks");
            // Excludes compile-only references; verifies every runtime word and table entry.
            var payload = new {
                words = code.Instructions.Select(w => new { w.OpCode, w.Argument, w.LineNumber, w.ColumnOffset, w.FileName }),
                exceptions = code.ExceptionTable.Select(e => new { e.StartOffset, e.EndOffset, e.HandlerOffset, e.Depth, e.Lasti })
            };
            rows.Add(new { path = location, words = code.Instructions.Count,
                direct_blocks = direct, reachable_blocks = blocks.Count,
                retained_cfg_instruction_capacity_bytes = blocks.Sum(b => (long)b.Instructions.Capacity * System.Runtime.CompilerServices.Unsafe.SizeOf<ByteCodeInstruction>()),
                execution_metadata_sha256 = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload))) });
            foreach (var child in code.Constants.OfType<PyCodeObject>()) Visit(child, location);
        }
        Visit(root, "");
        return new { scope = "Reachability from final instruction CFG fields; capacity bytes exclude block/list headers and other retained objects", code_units = rows };
    }
}
