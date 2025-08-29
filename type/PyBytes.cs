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

        #region Buffer Protocol (PEP 688)

        /// <summary>
        /// PEP 688: bytes objects implement the buffer protocol
        /// </summary>
        public override PyMemoryView GetBuffer(int flags)
        {
            // bytes objects are read-only buffers
            return new PyMemoryView(Value, true);
        }

        /// <summary>
        /// Check if this object supports the buffer protocol (always true for bytes)
        /// </summary>
        public override bool SupportsBuffer() => true;

        #endregion
    }
}