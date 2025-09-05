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
        public override PyType GetPyType() => PyType.BytesType;
        
        public override string ToString() => ToRepr();
        
        public override string ToRepr()
        {
            var sb = new System.Text.StringBuilder("b'");
            foreach (byte b in Value)
            {
                if (b >= 32 && b < 127 && b != '\\' && b != '\'')
                {
                    sb.Append((char)b);
                }
                else
                {
                    switch (b)
                    {
                        case (byte)'\\': sb.Append("\\\\"); break;
                        case (byte)'\'': sb.Append("\\'"); break;
                        case (byte)'\n': sb.Append("\\n"); break;
                        case (byte)'\r': sb.Append("\\r"); break;
                        case (byte)'\t': sb.Append("\\t"); break;
                        default: sb.Append($"\\x{b:x2}"); break;
                    }
                }
            }
            sb.Append('\'');
            return sb.ToString();
        }
        
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