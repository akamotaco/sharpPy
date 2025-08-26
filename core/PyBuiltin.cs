namespace SharpPy
{
    public class PyBuiltinFunction : PyObject
    {
        public string Name { get; }

        public PyBuiltinFunction(string name)
        {
            Name = name;
        }

        public override string GetTypeName() => "builtin_function_or_method";

        // 내장 함수 호출
        public override PyObject Call(params PyObject[] args)
        {
            return Name switch
            {
                "print" => CallPrint(args),
                "len" => CallLen(args),
                "abs" => CallAbs(args),
                "callable" => CallCallable(args),
                _ => throw PyNotImplementedError.Create($"Built-in function '{Name}' not implemented")
            };
        }

        // 내장 함수들의 구현
        private PyObject CallPrint(PyObject[] args)
        {
            var output = string.Join(" ", args.Select(arg => arg.ToString()));
            Console.WriteLine(output);
            return PyNone.Instance;
        }

        private PyObject CallLen(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"len() takes exactly one argument ({args.Length} given)");

            // 간단한 구현
            if (args[0] is PyString str)
                return new PyInt(str.Value.Length);
            else if (args[0] is PyTuple tuple)
                return new PyInt(tuple.Items.Length);
            else
                throw PyTypeError.Create($"object of type '{args[0].GetTypeName()}' has no len()");
        }

        private PyObject CallAbs(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"abs() takes exactly one argument ({args.Length} given)");

            if (args[0] is PyInt intVal)
                return new PyInt(Math.Abs(intVal.Value));
            else
                throw PyTypeError.Create($"bad operand type for abs(): '{args[0].GetTypeName()}'");
        }

        private PyObject CallCallable(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"callable() takes exactly one argument ({args.Length} given)");

            return PyBool.FromBool(args[0].IsCallable());
        }

        // Built-in function은 항상 호출 가능
        public override bool IsCallable() => true;

        public override PyObject GetAttribute(string name)
        {
            return name switch
            {
                "__name__" => new PyString(Name),
                "__call__" => this,
                _ => throw PyAttributeError.Create($"'builtin_function_or_method' object has no attribute '{name}'")
            };
        }

        public override string ToString() => $"<built-in function {Name}>";
    }
}