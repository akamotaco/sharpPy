using System;
using System.Linq;

namespace SharpPy
{
    public class PyList : PyObject
    {
        public PyObject[] Items { get; }

        public PyList(params PyObject[] items)
        {
            Items = items;
        }

        public override string GetTypeName() => "list";
        public override string ToString() => $"[{string.Join(", ", Items.Select(i => i.ToString()))}]";
        public override int Length() => Items.Length;
        public override bool PyBoolValue() => Items.Length > 0;
    }
}