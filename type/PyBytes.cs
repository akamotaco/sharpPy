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

        public override string ToString() => ToRepr().Value;
        
        public override PyString ToRepr()
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
            return new PyString(sb.ToString());
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

        #region Operators

        /// <summary>
        /// bytes + bytes concatenation
        /// </summary>
        public override PyObject Add(PyObject other)
        {
            if (other is PyBytes otherBytes)
            {
                var result = new byte[Value.Length + otherBytes.Value.Length];
                Array.Copy(Value, 0, result, 0, Value.Length);
                Array.Copy(otherBytes.Value, 0, result, Value.Length, otherBytes.Value.Length);
                return new PyBytes(result);
            }
            
            throw PyTypeError.Create($"can't concat bytes to {other.GetTypeName()}");
        }

        /// <summary>
        /// bytes * int repetition
        /// </summary>
        public override PyObject Multiply(PyObject other)
        {
            if (other is PyInt count)
            {
                if (count.Value < 0)
                    return new PyBytes(new byte[0]);
                
                var result = new byte[Value.Length * count.Value];
                for (int i = 0; i < count.Value; i++)
                {
                    Array.Copy(Value, 0, result, i * Value.Length, Value.Length);
                }
                return new PyBytes(result);
            }
            
            throw PyTypeError.Create($"can't multiply sequence by non-int of type '{other.GetTypeName()}'");
        }

        /// <summary>
        /// bytes equality comparison (used by equals operation)
        /// </summary>
        public override bool Equals(object? obj)
        {
            if (obj is PyBytes other)
            {
                if (Value.Length != other.Value.Length)
                    return false;
                
                for (int i = 0; i < Value.Length; i++)
                {
                    if (Value[i] != other.Value[i])
                        return false;
                }
                return true;
            }
            return false;
        }

        public override int GetHashCode()
        {
            // Simple hash code for bytes
            int hash = 17;
            for (int i = 0; i < Value.Length; i++)
            {
                hash = hash * 31 + Value[i];
            }
            return hash;
        }

        /// <summary>
        /// bytes indexing - returns int (byte value)
        /// </summary>
        public override PyObject GetItem(PyObject index)
        {
            if (index is PyInt pyInt)
            {
                var idx = pyInt.Value;
                if (idx < 0) idx += Value.Length;
                if (idx < 0 || idx >= Value.Length)
                    throw PyIndexError.Create("index out of range");
                
                return new PyInt(Value[idx]);
            }
            else if (index is PySlice slice)
            {
                // Handle slice indexing
                var (start, stop, step) = slice.Indices(Value.Length);
                
                if (step == 1)
                {
                    // Simple slice
                    var length = Math.Max(0, stop - start);
                    var result = new byte[length];
                    Array.Copy(Value, start, result, 0, length);
                    return new PyBytes(result);
                }
                else
                {
                    // Step slice
                    var resultList = new List<byte>();
                    if (step > 0)
                    {
                        for (int i = start; i < stop; i += step)
                        {
                            resultList.Add(Value[i]);
                        }
                    }
                    else
                    {
                        for (int i = start; i > stop; i += step)
                        {
                            resultList.Add(Value[i]);
                        }
                    }
                    return new PyBytes(resultList.ToArray());
                }
            }
            
            throw PyTypeError.Create($"byte indices must be integers or slices, not {index.GetTypeName()}");
        }

        /// <summary>
        /// bytes containment check
        /// </summary>
        public override PyBool Contains(PyObject item)
        {
            if (item is PyBytes other)
            {
                // Check if 'other' bytes sequence is contained in this bytes
                return PyBool.FromBool(IndexOf(other.Value) >= 0);
            }
            else if (item is PyInt pyInt)
            {
                // Check if byte value is in bytes
                if (pyInt.Value < 0 || pyInt.Value > 255)
                    return PyBool.False;
                
                byte b = (byte)pyInt.Value;
                return PyBool.FromBool(Value.Contains(b));
            }
            
            return PyBool.False;
        }

        /// <summary>
        /// Helper method to find index of byte sequence
        /// </summary>
        private int IndexOf(byte[] pattern)
        {
            for (int i = 0; i <= Value.Length - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (Value[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }

        #endregion
    }
}