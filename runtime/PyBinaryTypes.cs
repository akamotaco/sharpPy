using System;
using System.Collections;
using System.Collections.Generic;
// Performance: Eliminated LINQ
using System.Text;

namespace SharpPy
{
    /// <summary>
    /// Python bytes object - immutable sequence of bytes (CPython 3.12 compatible)
    /// </summary>
    public class PyBytesObject : PyObject, IEnumerable<byte>
    {
        private readonly byte[] _data;

        public PyBytesObject(byte[] data)
        {
            _data = data ?? new byte[0];
        }

        public PyBytesObject(string str, string encoding = "utf-8")
        {
            var encoder = Encoding.GetEncoding(encoding);
            _data = encoder.GetBytes(str);
        }

        public PyBytesObject(IEnumerable<int> values)
        {
            // Performance: Eliminated LINQ - replaced Select().ToArray() with manual conversion
            var tempList = new List<byte>();
            foreach (var v in values)
            {
                if (v < 0 || v > 255)
                    throw PyValueError.Create($"bytes must be in range(0, 256)");
                tempList.Add((byte)v);
            }
            _data = tempList.ToArray();
        }

        public override PyType GetPyType() => PyType.BytesType;
        public override string GetTypeName() => "bytes";

        public byte[] Data => (byte[])_data.Clone();
        public int Length => _data.Length;

        // Indexing
        public byte this[int index]
        {
            get
            {
                if (index < 0) index += _data.Length;
                if (index < 0 || index >= _data.Length)
                    throw PyIndexError.Create("bytes index out of range");
                return _data[index];
            }
        }

        // Slicing
        public PyBytesObject Slice(int start, int stop, int step = 1)
        {
            var indices = SliceHelper.GetSliceIndices(start, stop, step, _data.Length);
            var result = new List<byte>();

            if (step > 0)
            {
                for (int i = indices.start; i < indices.stop; i += step)
                    result.Add(_data[i]);
            }
            else
            {
                for (int i = indices.start; i > indices.stop; i += step)
                    result.Add(_data[i]);
            }

            // Performance: Eliminated LINQ - replaced Select() with manual conversion
            var intValues = new List<int>(result.Count);
            foreach (var b in result)
            {
                intValues.Add((int)b);
            }
            return new PyBytesObject(intValues);
        }

        // String operations
        public override string ToString()
        {
            var sb = new StringBuilder("b'");
            foreach (byte b in _data)
            {
                if (b >= 32 && b <= 126 && b != 39 && b != 92) // printable ASCII
                    sb.Append((char)b);
                else if (b == 39) // single quote
                    sb.Append("\\'");
                else if (b == 92) // backslash
                    sb.Append("\\\\");
                else if (b == 10) // newline
                    sb.Append("\\n");
                else if (b == 13) // carriage return
                    sb.Append("\\r");
                else if (b == 9) // tab
                    sb.Append("\\t");
                else
                    sb.Append($"\\x{b:x2}");
            }
            sb.Append("'");
            return sb.ToString();
        }

        public string Decode(string encoding = "utf-8")
        {
            var decoder = Encoding.GetEncoding(encoding);
            return decoder.GetString(_data);
        }

        // Concatenation
        public PyBytesObject Add(PyBytesObject other)
        {
            var result = new byte[_data.Length + other._data.Length];
            Array.Copy(_data, 0, result, 0, _data.Length);
            Array.Copy(other._data, 0, result, _data.Length, other._data.Length);
            return new PyBytesObject(result);
        }

        // Repetition
        public PyBytesObject Multiply(int count)
        {
            if (count <= 0) return new PyBytesObject(new byte[0]);

            var result = new byte[_data.Length * count];
            for (int i = 0; i < count; i++)
            {
                Array.Copy(_data, 0, result, i * _data.Length, _data.Length);
            }
            return new PyBytesObject(result);
        }

        // Membership test
        public bool Contains(byte value)
        {
            return _data.Contains(value);
        }

        public bool Contains(PyBytesObject other)
        {
            if (other._data.Length == 0) return true;
            if (other._data.Length > _data.Length) return false;

            for (int i = 0; i <= _data.Length - other._data.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < other._data.Length; j++)
                {
                    if (_data[i + j] != other._data[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return true;
            }
            return false;
        }

        // Comparison
        public override bool Equals(object obj)
        {
            if (obj is PyBytesObject other)
                return _data.SequenceEqual(other._data);
            return false;
        }

        public override int GetHashCode()
        {
            return _data.Aggregate(0, (hash, b) => hash * 31 + b);
        }

        // Iterator
        public IEnumerator<byte> GetEnumerator()
        {
            return ((IEnumerable<byte>)_data).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        // Methods
        public int Count(byte value)
        {
            return _data.Count(b => b == value);
        }

        public int Find(PyBytesObject sub, int start = 0, int end = -1)
        {
            if (end == -1) end = _data.Length;
            if (start < 0) start = 0;
            if (end > _data.Length) end = _data.Length;

            for (int i = start; i <= end - sub._data.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < sub._data.Length; j++)
                {
                    if (_data[i + j] != sub._data[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }

        public PyBytesObject Replace(PyBytesObject old, PyBytesObject @new, int count = -1)
        {
            if (old._data.Length == 0) return this;

            var result = new List<byte>(_data);
            int replacements = 0;
            int pos = 0;

            while (pos <= result.Count - old._data.Length && (count == -1 || replacements < count))
            {
                bool match = true;
                for (int i = 0; i < old._data.Length; i++)
                {
                    if (result[pos + i] != old._data[i])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    result.RemoveRange(pos, old._data.Length);
                    result.InsertRange(pos, @new._data);
                    pos += @new._data.Length;
                    replacements++;
                }
                else
                {
                    pos++;
                }
            }

            // Performance: Eliminated LINQ - replaced Select() with manual conversion
            var intValues2 = new List<int>(result.Count);
            foreach (var b in result)
            {
                intValues2.Add((int)b);
            }
            return new PyBytesObject(intValues2);
        }
    }

    /// <summary>
    /// Python bytearray object - mutable sequence of bytes (CPython 3.12 compatible)
    /// </summary>
    public class PyBytearrayObject : PyObject, IEnumerable<byte>
    {
        private List<byte> _data;

        public PyBytearrayObject(IEnumerable<byte> data = null)
        {
            // Performance: Eliminated LINQ - replaced ToList() with manual conversion
            if (data != null)
            {
                _data = new List<byte>(data);
            }
            else
            {
                _data = new List<byte>();
            }
        }

        public PyBytearrayObject(string str, string encoding = "utf-8")
        {
            var encoder = Encoding.GetEncoding(encoding);
            // Performance: Eliminated LINQ - replaced ToList() with new List constructor
            _data = new List<byte>(encoder.GetBytes(str));
        }

        public PyBytearrayObject(IEnumerable<int> values)
        {
            // Performance: Eliminated LINQ - replaced Select().ToList() with manual conversion
            _data = new List<byte>();
            foreach (var v in values)
            {
                if (v < 0 || v > 255)
                    throw PyValueError.Create($"byte must be in range(0, 256)");
                _data.Add((byte)v);
            }
        }

        public override PyType GetPyType() => PyType.BytearrayType;
        public override string GetTypeName() => "bytearray";

        public byte[] Data => _data.ToArray();
        public int Length => _data.Count;

        // Indexing (mutable)
        public byte this[int index]
        {
            get
            {
                if (index < 0) index += _data.Count;
                if (index < 0 || index >= _data.Count)
                    throw PyIndexError.Create("bytearray index out of range");
                return _data[index];
            }
            set
            {
                if (index < 0) index += _data.Count;
                if (index < 0 || index >= _data.Count)
                    throw PyIndexError.Create("bytearray assignment index out of range");
                _data[index] = value;
            }
        }

        // Mutable operations
        public void Append(byte value)
        {
            _data.Add(value);
        }

        public void Extend(IEnumerable<byte> values)
        {
            _data.AddRange(values);
        }

        public void Insert(int index, byte value)
        {
            if (index < 0) index += _data.Count;
            if (index < 0) index = 0;
            if (index > _data.Count) index = _data.Count;
            _data.Insert(index, value);
        }

        public byte Pop(int index = -1)
        {
            if (_data.Count == 0)
                throw PyIndexError.Create("pop from empty bytearray");

            if (index == -1) index = _data.Count - 1;
            if (index < 0) index += _data.Count;
            if (index < 0 || index >= _data.Count)
                throw PyIndexError.Create("pop index out of range");

            byte value = _data[index];
            _data.RemoveAt(index);
            return value;
        }

        public void Remove(byte value)
        {
            int index = _data.IndexOf(value);
            if (index == -1)
                throw PyValueError.Create("bytearray.remove(x): x not in bytearray");
            _data.RemoveAt(index);
        }

        public void Clear()
        {
            _data.Clear();
        }

        public void Reverse()
        {
            _data.Reverse();
        }

        // Convert to bytes
        public PyBytesObject ToBytes()
        {
            // Performance: Eliminated LINQ - replaced Select() with manual conversion
            var intValues3 = new List<int>(_data.Count);
            foreach (var b in _data)
            {
                intValues3.Add((int)b);
            }
            return new PyBytesObject(intValues3);
        }

        public override string ToString()
        {
            var sb = new StringBuilder("bytearray(b'");
            foreach (byte b in _data)
            {
                if (b >= 32 && b <= 126 && b != 39 && b != 92) // printable ASCII
                    sb.Append((char)b);
                else if (b == 39) // single quote
                    sb.Append("\\'");
                else if (b == 92) // backslash
                    sb.Append("\\\\");
                else if (b == 10) // newline
                    sb.Append("\\n");
                else if (b == 13) // carriage return
                    sb.Append("\\r");
                else if (b == 9) // tab
                    sb.Append("\\t");
                else
                    sb.Append($"\\x{b:x2}");
            }
            sb.Append("')");
            return sb.ToString();
        }

        public IEnumerator<byte> GetEnumerator()
        {
            return _data.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    /// <summary>
    /// Python memoryview object - buffer protocol interface (CPython 3.12 compatible)
    /// </summary>
    public class PyMemoryViewObject : PyObject, IEnumerable<byte>
    {
        private readonly PyObject _sourceObject;  // PyBytesObject or PyBytearrayObject
        private readonly byte[] _buffer;
        private readonly bool _readonly;

        public PyMemoryViewObject(PyBytesObject bytes_obj)
        {
            _sourceObject = bytes_obj;
            _buffer = bytes_obj.Data;
            _readonly = true; // bytes are immutable
        }

        public PyMemoryViewObject(PyBytearrayObject bytearray_obj)
        {
            _sourceObject = bytearray_obj;
            _buffer = bytearray_obj.Data;
            _readonly = false; // bytearray is mutable
        }

        public override PyType GetPyType() => PyType.MemoryViewType;
        public override string GetTypeName() => "memoryview";

        public int Length => _buffer.Length;
        public bool ReadOnly => _readonly;
        public PyObject SourceObject => _sourceObject;

        // Indexing
        public byte this[int index]
        {
            get
            {
                if (index < 0) index += _buffer.Length;
                if (index < 0 || index >= _buffer.Length)
                    throw PyIndexError.Create("memoryview index out of range");
                return _buffer[index];
            }
            set
            {
                if (_readonly)
                    throw PyTypeError.Create("cannot modify read-only memory");

                if (index < 0) index += _buffer.Length;
                if (index < 0 || index >= _buffer.Length)
                    throw PyIndexError.Create("memoryview assignment index out of range");

                // Update the underlying object if it's bytearray
                if (_sourceObject is PyBytearrayObject bytearray_obj)
                {
                    bytearray_obj[index] = value;
                }
            }
        }

        // Convert to bytes
        public PyBytesObject ToBytes()
        {
            return new PyBytesObject((byte[])_buffer.Clone());
        }

        // Convert to list
        public PyList ToList()
        {
            // Performance: Eliminated LINQ - replaced Select().Cast().ToArray() with manual conversion
            var items = new PyObject[_buffer.Length];
            for (int i = 0; i < _buffer.Length; i++)
            {
                items[i] = new PyInt(_buffer[i]);
            }
            return new PyList(items);
        }

        public override string ToString()
        {
            return $"<memory at 0x{GetHashCode():x}>";
        }

        public IEnumerator<byte> GetEnumerator()
        {
            return ((IEnumerable<byte>)_buffer).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        // Release the buffer (in real CPython this matters for C extensions)
        public void Release()
        {
            // In C# this is mostly a no-op, but we can mark as released
            // Real implementation would need reference counting
        }
    }

    /// <summary>
    /// Helper class for slice operations
    /// </summary>
    internal static class SliceHelper
    {
        public static (int start, int stop) GetSliceIndices(int start, int stop, int step, int length)
        {
            // Normalize negative indices
            if (start < 0) start += length;
            if (stop < 0) stop += length;

            // Clamp to valid range
            start = Math.Max(0, Math.Min(start, length));
            stop = Math.Max(0, Math.Min(stop, length));

            // Handle step direction
            if (step < 0)
            {
                if (start == length) start = length - 1;
                if (stop == length) stop = length - 1;
            }

            return (start, stop);
        }
    }
}