using SharpPy;
using SharpPy.Generated;

internal static class BytecodeInspection
{
    public static object Inspect(string path)
    {
        string source = File.ReadAllText(path);
        var tokens = PyParserRuntime.LexerSource(source);
        var statements = PyParserRuntime.ParseSource(tokens, source, path);
        var root = new PythonCompiler().Compile(statements, "<module>", new List<string>(), path);
        var rows = new List<object>();
        void Visit(PyCodeObject code, string parent)
        {
            string location = parent.Length == 0 ? code.Name : parent + "/" + code.Name;
            rows.Add(new { name = location, exceptionTable = code.ExceptionTable.Select(e => new {
                start = e.StartOffset, end = e.EndOffset, handler = e.HandlerOffset,
                depth = e.Depth, lasti = e.Lasti
            }).ToArray(), constants = code.Constants.Select(c =>
                c is PyCodeObject childCode ? "<code:" + childCode.Name + ">" : c.ToRepr().Value).ToArray(), instructions = code.Instructions.Select(i => new {
                opcode = (int)i.OpCode, name = i.OpCode.ToString(), arg = i.Argument
            }).ToArray() });
            foreach (var child in code.Constants.OfType<PyCodeObject>()) Visit(child, location);
        }
        Visit(root, "");
        return rows;
    }
}
