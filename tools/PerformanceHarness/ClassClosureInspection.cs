using SharpPy;

internal static class ClassClosureInspection
{
    public static object Inspect(string path)
    {
        var interpreter = new IntegratedPythonInterpreter();
        interpreter.Execute(File.ReadAllText(path), path, false, false, false);
        return new[] { "observed", "local_name", "prepared" }.Select(name =>
        {
            var parent = ((PyFunction)interpreter.GetGlobalVariable(name)).CodeObject;
            var body = parent.Constants.OfType<PyCodeObject>().Single(code => code.Name == "<class_body_Body>");
            var pairs = new List<int[]>();
            for (int i = 0; i + 1 < body.Instructions.Count; i++)
                if (body.Instructions[i].OpCode == ByteCodeOp.LOAD_LOCALS &&
                    body.Instructions[i + 1].OpCode == ByteCodeOp.LOAD_FROM_DICT_OR_DEREF)
                    pairs.Add(new[] { (int)body.Instructions[i].OpCode, body.Instructions[i].Argument,
                        (int)body.Instructions[i + 1].OpCode, body.Instructions[i + 1].Argument });
            return new { name, parent_cells = parent.CellVars, class_locals = body.VarNames,
                class_cells = body.CellVars, class_freevars = body.FreeVars, pairs };
        }).ToArray();
    }
}
