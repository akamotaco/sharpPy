namespace SharpPy
{
    public class PyDict : PyObject
    {
        public Dictionary<string, PyObject> Items { get; }

        public PyDict(Dictionary<string, PyObject> items)
        {
            Items = new Dictionary<string, PyObject>(items);
        }

        public override string GetTypeName() => "dict";
        public override string ToString() => $"{{{string.Join(", ", Items.Select(kv => $"'{kv.Key}': {kv.Value}"))}}}";
    }
}