using SharpPy;
using SharpPy.Generated;

internal static class ExceptionTableInspection
{
    public static object Inspect(string path)
    {
        string source = File.ReadAllText(path);
        var tokens = PyParserRuntime.LexerSource(source);
        var statements = PyParserRuntime.ParseSource(tokens, source, path);
        var root = new PythonCompiler().Compile(statements, "<module>", new List<string>(), path);
        var rows = new List<object>();
        var tables = new List<List<ExceptionTableEntry>>();
        bool distinct = true;
        void Visit(PyCodeObject code, string parent)
        {
            string name = code.Name.StartsWith("<class_body_", StringComparison.Ordinal)
                ? code.Name.Substring(12, code.Name.Length - 13) : code.Name;
            string location = parent.Length == 0 ? name : parent + "/" + name;
            // Final assembly emits CACHE and EXTENDED_ARG as separate words.
            int length = code.Instructions.Count * PyCodeObject.INSTRUCTION_WORD_SIZE;
            bool Covered(ByteCodeOp op) => code.Instructions.Select((instruction, index) => (instruction, index))
                .Any(pair => pair.instruction.OpCode == op && code.ExceptionTable.Any(entry =>
                    entry.StartOffset <= pair.index * PyCodeObject.INSTRUCTION_WORD_SIZE &&
                    pair.index * PyCodeObject.INSTRUCTION_WORD_SIZE < entry.EndOffset));
            if (code.ExceptionTable.Count > 0)
            {
                if (tables.Any(table => ReferenceEquals(table, code.ExceptionTable))) distinct = false;
                tables.Add(code.ExceptionTable);
            }
            rows.Add(new
            {
                path = location,
                table_present = code.ExceptionTable.Count > 0,
                offset_conversion_valid = Enumerable.Range(0, code.Instructions.Count).All(index =>
                    code.InstructionIndexToByteOffset(index) == index * PyCodeObject.INSTRUCTION_WORD_SIZE &&
                    code.ByteOffsetToInstructionIndex(index * PyCodeObject.INSTRUCTION_WORD_SIZE) == index),
                bounds_valid = code.ExceptionTable.All(entry => entry.StartOffset >= 0 &&
                    entry.StartOffset < entry.EndOffset && entry.EndOffset <= length &&
                    entry.HandlerOffset >= 0 && entry.HandlerOffset < length && entry.Depth >= 0 &&
                    entry.StartOffset % 2 == 0 && entry.EndOffset % 2 == 0 && entry.HandlerOffset % 2 == 0),
                protected_binary_op = Covered(ByteCodeOp.BINARY_OP),
                protected_raise = Covered(ByteCodeOp.RAISE_VARARGS),
                code_bytes = length,
                entries = code.ExceptionTable.Select(entry => new { start = entry.StartOffset,
                    end = entry.EndOffset, target = entry.HandlerOffset, depth = entry.Depth, lasti = entry.Lasti }).ToArray()
            });
            foreach (var child in code.Constants.OfType<PyCodeObject>()) Visit(child, location);
        }
        Visit(root, "");
        return new { code_units = rows, nonempty_tables_distinct = distinct };
    }
}
