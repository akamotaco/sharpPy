using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpPy
{
    /// <summary>
    /// Python bytearray type - mutable sequence of bytes
    /// CPython 3.12 compatible implementation
    /// </summary>
    public class PyByteArray : PyObject
    {
        #region Fields

        private List<byte> _bytes;
        private int _exports = 0;  // Track buffer exports for resize safety

        #endregion

        #region Static Constructor & Descriptors

        static PyByteArray()
        {
            InitializeByteArrayDescriptors();
        }

        private static void InitializeByteArrayDescriptors()
        {
            var baType = PyType.BytearrayType;

            // append method descriptor
            baType.TypeDict["append"] = new PyMethodDescriptor(
                "append", baType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"append() takes exactly one argument ({args.Length} given)");
                    if (self is not PyByteArray ba)
                        throw PyTypeError.Create($"descriptor 'append' requires a 'bytearray' object but received a '{self.GetTypeName()}'");

                    int value = GetByteValue(args[0]);
                    ba.Append((byte)value);
                    return PyNone.Instance;
                },
                minArgs: 1, maxArgs: 1
            );

            // extend method descriptor
            baType.TypeDict["extend"] = new PyMethodDescriptor(
                "extend", baType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"extend() takes exactly one argument ({args.Length} given)");
                    if (self is not PyByteArray ba)
                        throw PyTypeError.Create($"descriptor 'extend' requires a 'bytearray' object but received a '{self.GetTypeName()}'");

                    ba.Extend(args[0]);
                    return PyNone.Instance;
                },
                minArgs: 1, maxArgs: 1
            );

            // insert method descriptor
            baType.TypeDict["insert"] = new PyMethodDescriptor(
                "insert", baType,
                (self, args, kwargs) => {
                    if (args.Length != 2)
                        throw PyTypeError.Create($"insert() takes exactly 2 arguments ({args.Length} given)");
                    if (self is not PyByteArray ba)
                        throw PyTypeError.Create($"descriptor 'insert' requires a 'bytearray' object but received a '{self.GetTypeName()}'");

                    int index = (int)args[0].ToInt();
                    int value = GetByteValue(args[1]);
                    ba.Insert(index, (byte)value);
                    return PyNone.Instance;
                },
                minArgs: 2, maxArgs: 2
            );

            // remove method descriptor
            baType.TypeDict["remove"] = new PyMethodDescriptor(
                "remove", baType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"remove() takes exactly one argument ({args.Length} given)");
                    if (self is not PyByteArray ba)
                        throw PyTypeError.Create($"descriptor 'remove' requires a 'bytearray' object but received a '{self.GetTypeName()}'");

                    int value = GetByteValue(args[0]);
                    ba.Remove((byte)value);
                    return PyNone.Instance;
                },
                minArgs: 1, maxArgs: 1
            );

            // pop method descriptor
            baType.TypeDict["pop"] = new PyMethodDescriptor(
                "pop", baType,
                (self, args, kwargs) => {
                    if (args.Length > 1)
                        throw PyTypeError.Create($"pop() takes at most 1 argument ({args.Length} given)");
                    if (self is not PyByteArray ba)
                        throw PyTypeError.Create($"descriptor 'pop' requires a 'bytearray' object but received a '{self.GetTypeName()}'");

                    int index = args.Length == 0 ? -1 : (int)args[0].ToInt();
                    return ba.Pop(index);
                },
                minArgs: 0, maxArgs: 1
            );

            // clear method descriptor
            baType.TypeDict["clear"] = new PyMethodDescriptor(
                "clear", baType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"clear() takes no arguments ({args.Length} given)");
                    if (self is not PyByteArray ba)
                        throw PyTypeError.Create($"descriptor 'clear' requires a 'bytearray' object but received a '{self.GetTypeName()}'");

                    ba.Clear();
                    return PyNone.Instance;
                },
                minArgs: 0, maxArgs: 0
            );

            // reverse method descriptor
            baType.TypeDict["reverse"] = new PyMethodDescriptor(
                "reverse", baType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"reverse() takes no arguments ({args.Length} given)");
                    if (self is not PyByteArray ba)
                        throw PyTypeError.Create($"descriptor 'reverse' requires a 'bytearray' object but received a '{self.GetTypeName()}'");

                    ba.Reverse();
                    return PyNone.Instance;
                },
                minArgs: 0, maxArgs: 0
            );

            // copy method descriptor
            baType.TypeDict["copy"] = new PyMethodDescriptor(
                "copy", baType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"copy() takes no arguments ({args.Length} given)");
                    if (self is not PyByteArray ba)
                        throw PyTypeError.Create($"descriptor 'copy' requires a 'bytearray' object but received a '{self.GetTypeName()}'");

                    return new PyByteArray(ba._bytes.ToArray());
                },
                minArgs: 0, maxArgs: 0
            );
        }

        #endregion

        #region Constructors

        public PyByteArray() : this(new byte[0])
        {
        }

        public PyByteArray(byte[] bytes)
        {
            _bytes = new List<byte>(bytes ?? new byte[0]);
        }

        public PyByteArray(int size)
        {
            if (size < 0)
                throw PyValueError.Create("negative count");
            _bytes = new List<byte>(new byte[size]);
        }

        public PyByteArray(IEnumerable<byte> bytes)
        {
            _bytes = new List<byte>(bytes ?? Enumerable.Empty<byte>());
        }

        /// <summary>
        /// Constructor from PyObject iterable
        /// </summary>
        public static PyByteArray FromIterable(PyObject iterable)
        {
            var bytes = new List<byte>();

            if (iterable is PyBytes pyBytes)
            {
                return new PyByteArray(pyBytes.Value);
            }
            else if (iterable is PyByteArray pyByteArray)
            {
                return new PyByteArray(pyByteArray._bytes.ToArray());
            }
            else if (iterable is PyList list)
            {
                foreach (var item in list.Items)
                {
                    bytes.Add((byte)GetByteValue(item));
                }
            }
            else
            {
                // Generic iterable
                var iterator = iterable.GetIterator();
                PyObject? item;
                while ((item = iterator.Next()) != null)
                {
                    bytes.Add((byte)GetByteValue(item));
                }
            }

            return new PyByteArray(bytes.ToArray());
        }

        #endregion

        #region Core Overrides

        public override string GetTypeName() => "bytearray";
        public override PyType GetPyType() => PyType.BytearrayType;

        public override string ToString() => ToRepr().Value;

        public override PyString ToRepr()
        {
            var sb = new StringBuilder("bytearray(b'");
            foreach (byte b in _bytes)
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
            sb.Append("')");
            return new PyString(sb.ToString());
        }

        public override int Length() => _bytes.Count;
        public override bool PyBoolValue() => _bytes.Count > 0;

        // bytearray is unhashable (mutable type)
        public override int GetHashCode()
        {
            throw PyTypeError.Create("unhashable type: 'bytearray'");
        }

        #endregion

        #region Sequence Protocol

        /// <summary>
        /// __getitem__ - Get item by index or slice
        /// </summary>
        public override PyObject GetItem(PyObject index)
        {
            if (index is PyInt pyInt)
            {
                // Single index access - returns PyInt (0-255)
                int idx = (int)pyInt.Value;
                if (idx < 0) idx += _bytes.Count;

                if (idx < 0 || idx >= _bytes.Count)
                    throw PyIndexError.Create("bytearray index out of range");

                return new PyInt(_bytes[idx]);
            }
            else if (index is PySlice slice)
            {
                // Slice access - returns new bytearray
                var indices = slice.GetIndices(_bytes.Count);
                int start = indices[0], stop = indices[1], step = indices[2];

                if (step == 1)
                {
                    // Contiguous slice
                    int length = Math.Max(0, stop - start);
                    var result = new byte[length];
                    for (int i = 0; i < length; i++)
                    {
                        result[i] = _bytes[start + i];
                    }
                    return new PyByteArray(result);
                }
                else
                {
                    // Extended slice
                    var result = new List<byte>();
                    if (step > 0)
                    {
                        for (int i = start; i < stop; i += step)
                        {
                            if (i >= 0 && i < _bytes.Count)
                                result.Add(_bytes[i]);
                        }
                    }
                    else
                    {
                        for (int i = start; i > stop; i += step)
                        {
                            if (i >= 0 && i < _bytes.Count)
                                result.Add(_bytes[i]);
                        }
                    }
                    return new PyByteArray(result.ToArray());
                }
            }

            throw PyTypeError.Create($"bytearray indices must be integers or slices, not {index.GetTypeName()}");
        }

        /// <summary>
        /// __setitem__ - Set item by index or slice
        /// </summary>
        public override void SetItem(PyObject index, PyObject value)
        {
            if (_exports > 0)
                throw PyBufferError.Create("Existing exports of data: object cannot be re-sized");

            if (index is PyInt pyInt)
            {
                // Single index assignment
                int idx = (int)pyInt.Value;
                if (idx < 0) idx += _bytes.Count;

                if (idx < 0 || idx >= _bytes.Count)
                    throw PyIndexError.Create("bytearray index out of range");

                int byteValue = GetByteValue(value);
                _bytes[idx] = (byte)byteValue;
            }
            else if (index is PySlice slice)
            {
                // Slice assignment
                var indices = slice.GetIndices(_bytes.Count);
                int start = indices[0], stop = indices[1], step = indices[2];

                // Convert value to bytes
                List<byte> newBytes = ConvertToBytes(value);

                if (step == 1)
                {
                    // Contiguous slice - can change size
                    int deleteCount = Math.Max(0, stop - start);
                    _bytes.RemoveRange(start, deleteCount);
                    _bytes.InsertRange(start, newBytes);
                }
                else
                {
                    // Extended slice - must have same length
                    var sliceIndices = new List<int>();
                    if (step > 0)
                    {
                        for (int i = start; i < stop; i += step)
                        {
                            if (i >= 0 && i < _bytes.Count)
                                sliceIndices.Add(i);
                        }
                    }
                    else
                    {
                        for (int i = start; i > stop; i += step)
                        {
                            if (i >= 0 && i < _bytes.Count)
                                sliceIndices.Add(i);
                        }
                    }

                    if (sliceIndices.Count != newBytes.Count)
                        throw PyValueError.Create($"attempt to assign bytes of size {newBytes.Count} to extended slice of size {sliceIndices.Count}");

                    for (int i = 0; i < sliceIndices.Count; i++)
                    {
                        _bytes[sliceIndices[i]] = newBytes[i];
                    }
                }
            }
            else
            {
                throw PyTypeError.Create($"bytearray indices must be integers or slices, not {index.GetTypeName()}");
            }
        }

        /// <summary>
        /// __delitem__ - Delete item by index or slice
        /// </summary>
        public override void DelItem(PyObject index)
        {
            if (_exports > 0)
                throw PyBufferError.Create("Existing exports of data: object cannot be re-sized");

            if (index is PyInt pyInt)
            {
                // Single index deletion
                int idx = (int)pyInt.Value;
                if (idx < 0) idx += _bytes.Count;

                if (idx < 0 || idx >= _bytes.Count)
                    throw PyIndexError.Create("bytearray index out of range");

                _bytes.RemoveAt(idx);
            }
            else if (index is PySlice slice)
            {
                // Slice deletion
                var indices = slice.GetIndices(_bytes.Count);
                int start = indices[0], stop = indices[1], step = indices[2];

                if (step == 1)
                {
                    // Contiguous slice
                    int deleteCount = Math.Max(0, stop - start);
                    _bytes.RemoveRange(start, deleteCount);
                }
                else
                {
                    // Extended slice - delete in reverse order
                    var indicesToDelete = new List<int>();
                    if (step > 0)
                    {
                        for (int i = start; i < stop; i += step)
                        {
                            if (i >= 0 && i < _bytes.Count)
                                indicesToDelete.Add(i);
                        }
                    }
                    else
                    {
                        for (int i = start; i > stop; i += step)
                        {
                            if (i >= 0 && i < _bytes.Count)
                                indicesToDelete.Add(i);
                        }
                    }

                    // Delete in reverse order to maintain indices
                    indicesToDelete.Sort();
                    indicesToDelete.Reverse();
                    foreach (int idx in indicesToDelete)
                    {
                        _bytes.RemoveAt(idx);
                    }
                }
            }
            else
            {
                throw PyTypeError.Create($"bytearray indices must be integers or slices, not {index.GetTypeName()}");
            }
        }

        /// <summary>
        /// __contains__ - Check if byte or bytes is in bytearray
        /// </summary>
        public override PyBool Contains(PyObject item)
        {
            if (item is PyInt pyInt)
            {
                // Check for single byte
                int value = (int)pyInt.Value;
                if (value < 0 || value > 255)
                    return PyBool.False;

                return _bytes.Contains((byte)value) ? PyBool.True : PyBool.False;
            }
            else if (item is PyBytes pyBytes)
            {
                // Check for subsequence
                return ContainsSubsequence(pyBytes.Value) ? PyBool.True : PyBool.False;
            }
            else if (item is PyByteArray pyByteArray)
            {
                return ContainsSubsequence(pyByteArray._bytes.ToArray()) ? PyBool.True : PyBool.False;
            }

            return PyBool.False;
        }

        public override PyObject GetIterator()
        {
            return new PyByteArrayIterator(this);
        }

        #endregion

        #region Operators

        /// <summary>
        /// __add__ - Concatenation (returns new bytearray)
        /// </summary>
        public override PyObject Add(PyObject other)
        {
            if (other is PyByteArray otherBa)
            {
                var result = new byte[_bytes.Count + otherBa._bytes.Count];
                _bytes.CopyTo(result, 0);
                otherBa._bytes.CopyTo(result, _bytes.Count);
                return new PyByteArray(result);
            }
            else if (other is PyBytes otherBytes)
            {
                var result = new byte[_bytes.Count + otherBytes.Value.Length];
                _bytes.CopyTo(result, 0);
                Array.Copy(otherBytes.Value, 0, result, _bytes.Count, otherBytes.Value.Length);
                return new PyByteArray(result);
            }

            throw PyTypeError.Create($"can't concat bytearray to {other.GetTypeName()}");
        }

        // Note: For mutable types, += is handled by the VM which calls extend()
        // No need to override InPlaceAdd - it doesn't exist in PyObject

        /// <summary>
        /// __mul__ - Repetition (returns new bytearray)
        /// </summary>
        public override PyObject Multiply(PyObject other)
        {
            if (other is PyInt count)
            {
                if (count.Value <= 0)
                    return new PyByteArray(new byte[0]);

                var result = new List<byte>(_bytes.Count * (int)count.Value);
                for (int i = 0; i < count.Value; i++)
                {
                    result.AddRange(_bytes);
                }
                return new PyByteArray(result.ToArray());
            }

            throw PyTypeError.Create($"can't multiply sequence by non-int of type '{other.GetTypeName()}'");
        }

        // Note: For mutable types, *= is handled by the VM similarly to +=
        // No need to override InPlaceMultiply - it doesn't exist in PyObject

        #endregion

        #region Comparison

        protected override PyObject PyEquals(PyObject other)
        {
            if (other is PyByteArray otherBa)
            {
                return _bytes.SequenceEqual(otherBa._bytes) ? PyBool.True : PyBool.False;
            }
            else if (other is PyBytes otherBytes)
            {
                return _bytes.SequenceEqual(otherBytes.Value) ? PyBool.True : PyBool.False;
            }

            return PyNotImplemented.Instance;
        }

        protected override PyObject PyNotEquals(PyObject other)
        {
            var result = PyEquals(other);
            if (result == PyNotImplemented.Instance)
                return result;
            return result == PyBool.True ? PyBool.False : PyBool.True;
        }

        protected override PyObject PyLess(PyObject other)
        {
            if (other is PyByteArray otherBa)
            {
                return CompareBytes(_bytes.ToArray(), otherBa._bytes.ToArray()) < 0 ? PyBool.True : PyBool.False;
            }
            else if (other is PyBytes otherBytes)
            {
                return CompareBytes(_bytes.ToArray(), otherBytes.Value) < 0 ? PyBool.True : PyBool.False;
            }

            return PyNotImplemented.Instance;
        }

        protected override PyObject PyLessEqual(PyObject other)
        {
            if (other is PyByteArray otherBa)
            {
                return CompareBytes(_bytes.ToArray(), otherBa._bytes.ToArray()) <= 0 ? PyBool.True : PyBool.False;
            }
            else if (other is PyBytes otherBytes)
            {
                return CompareBytes(_bytes.ToArray(), otherBytes.Value) <= 0 ? PyBool.True : PyBool.False;
            }

            return PyNotImplemented.Instance;
        }

        protected override PyObject PyGreater(PyObject other)
        {
            if (other is PyByteArray otherBa)
            {
                return CompareBytes(_bytes.ToArray(), otherBa._bytes.ToArray()) > 0 ? PyBool.True : PyBool.False;
            }
            else if (other is PyBytes otherBytes)
            {
                return CompareBytes(_bytes.ToArray(), otherBytes.Value) > 0 ? PyBool.True : PyBool.False;
            }

            return PyNotImplemented.Instance;
        }

        protected override PyObject PyGreaterEqual(PyObject other)
        {
            if (other is PyByteArray otherBa)
            {
                return CompareBytes(_bytes.ToArray(), otherBa._bytes.ToArray()) >= 0 ? PyBool.True : PyBool.False;
            }
            else if (other is PyBytes otherBytes)
            {
                return CompareBytes(_bytes.ToArray(), otherBytes.Value) >= 0 ? PyBool.True : PyBool.False;
            }

            return PyNotImplemented.Instance;
        }

        #endregion

        #region Mutable Methods

        public void Append(byte value)
        {
            if (_exports > 0)
                throw PyBufferError.Create("Existing exports of data: object cannot be re-sized");

            _bytes.Add(value);
        }

        public void Extend(PyObject iterable)
        {
            if (_exports > 0)
                throw PyBufferError.Create("Existing exports of data: object cannot be re-sized");

            var bytes = ConvertToBytes(iterable);
            _bytes.AddRange(bytes);
        }

        public void Insert(int index, byte value)
        {
            if (_exports > 0)
                throw PyBufferError.Create("Existing exports of data: object cannot be re-sized");

            // Normalize negative index
            if (index < 0) index += _bytes.Count;

            // Clamp to valid range
            if (index < 0) index = 0;
            if (index > _bytes.Count) index = _bytes.Count;

            _bytes.Insert(index, value);
        }

        public void Remove(byte value)
        {
            if (_exports > 0)
                throw PyBufferError.Create("Existing exports of data: object cannot be re-sized");

            int index = _bytes.IndexOf(value);
            if (index == -1)
                throw PyValueError.Create("value not found in bytearray");

            _bytes.RemoveAt(index);
        }

        public PyInt Pop(int index = -1)
        {
            if (_exports > 0)
                throw PyBufferError.Create("Existing exports of data: object cannot be re-sized");

            if (_bytes.Count == 0)
                throw PyIndexError.Create("pop from empty bytearray");

            // Normalize negative index
            if (index < 0) index += _bytes.Count;

            if (index < 0 || index >= _bytes.Count)
                throw PyIndexError.Create("pop index out of range");

            byte value = _bytes[index];
            _bytes.RemoveAt(index);
            return new PyInt(value);
        }

        public void Clear()
        {
            if (_exports > 0)
                throw PyBufferError.Create("Existing exports of data: object cannot be re-sized");

            _bytes.Clear();
        }

        public void Reverse()
        {
            _bytes.Reverse();
        }

        #endregion

        #region Buffer Protocol

        public override PyMemoryView GetBuffer(int flags)
        {
            _exports++;
            // bytearray provides writable buffer
            return new PyMemoryView(_bytes.ToArray(), false);  // writable=true
        }

        public override void ReleaseBuffer(PyMemoryView buffer)
        {
            _exports--;
            if (_exports < 0) _exports = 0;
        }

        public override bool SupportsBuffer() => true;

        #endregion

        #region Helper Methods

        /// <summary>
        /// Validate and convert PyObject to byte value (0-255)
        /// </summary>
        private static int GetByteValue(PyObject obj)
        {
            if (obj is not PyInt pyInt)
                throw PyTypeError.Create("an integer is required");

            int value = (int)pyInt.Value;
            if (value < 0 || value > 255)
                throw PyValueError.Create("byte must be in range(0, 256)");

            return value;
        }

        /// <summary>
        /// Convert PyObject to List of bytes
        /// </summary>
        private static List<byte> ConvertToBytes(PyObject obj)
        {
            var result = new List<byte>();

            if (obj is PyBytes pyBytes)
            {
                result.AddRange(pyBytes.Value);
            }
            else if (obj is PyByteArray pyByteArray)
            {
                result.AddRange(pyByteArray._bytes);
            }
            else
            {
                // Try to iterate
                var iterator = obj.GetIterator();
                PyObject? item;
                while ((item = iterator.Next()) != null)
                {
                    result.Add((byte)GetByteValue(item));
                }
            }

            return result;
        }

        /// <summary>
        /// Check if subsequence exists in bytearray
        /// </summary>
        private bool ContainsSubsequence(byte[] sub)
        {
            if (sub.Length == 0)
                return true;
            if (sub.Length > _bytes.Count)
                return false;

            for (int i = 0; i <= _bytes.Count - sub.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < sub.Length; j++)
                {
                    if (_bytes[i + j] != sub[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Compare two byte arrays lexicographically
        /// </summary>
        private static int CompareBytes(byte[] a, byte[] b)
        {
            int minLen = Math.Min(a.Length, b.Length);
            for (int i = 0; i < minLen; i++)
            {
                if (a[i] != b[i])
                    return a[i].CompareTo(b[i]);
            }
            return a.Length.CompareTo(b.Length);
        }

        #endregion
    }

    /// <summary>
    /// Iterator for bytearray objects
    /// </summary>
    public class PyByteArrayIterator : PyIterator
    {
        private readonly PyByteArray _bytearray;
        private int _index;

        public PyByteArrayIterator(PyByteArray bytearray)
        {
            _bytearray = bytearray;
            _index = 0;
        }

        public override PyObject? Next()
        {
            if (_index >= _bytearray.Length())
                throw PyStopIteration.Create();

            var value = _bytearray.GetItem(new PyInt(_index));
            _index++;
            return value;
        }
    }
}
