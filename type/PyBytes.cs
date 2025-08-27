using System;
using System.Linq;

namespace SharpPy
{
    public class PyBytes : PyObject
    {
        public byte[] Value { get; }

        public PyBytes(byte[] value)
        {
            Value = value ?? new byte[0];
        }

        public override string GetTypeName() => "bytes";
        public override string ToString() => $"b'{string.Join("", Value.Select(b => (char)b))}'";
        public override int Length() => Value.Length;
        public override bool PyBoolValue() => Value.Length > 0;
    }
}