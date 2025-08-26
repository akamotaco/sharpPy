namespace SharpPy
{
    public class PyInt : PyObject
    {
        public int Value { get; }
        public PyInt(int value) => Value = value;
        public override PyType GetPyType() => PyType.IntType;
        public override string GetTypeName() => "int";
        public override string ToString() => Value.ToString();
    }
}