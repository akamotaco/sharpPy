namespace SharpPy
{
    public class PyString : PyObject
    {
        public string Value { get; }
        public PyString(string value) => Value = value;
        public override PyType GetPyType() => PyType.StrType;
        public override string GetTypeName() => "str";
        public override string ToString() => $"'{Value}'";
    }
}