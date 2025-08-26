namespace SharpPy
{
    public class PyTuple : PyObject
    {
        public PyObject[] Items { get; }

        public PyTuple(params PyObject[] items)
        {
            Items = items;
        }

        public override string GetTypeName() => "tuple";
        public override string ToString() => $"({string.Join(", ", Items.Select(i => i.ToString()))})";
    }
}