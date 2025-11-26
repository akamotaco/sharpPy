using System;
using System.Collections.Generic;
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

                    // Performance: Direct list copy instead of ToArray
                    return new PyByteArray(ba._bytes);
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/bytearrayobject.c:1500-1550 - bytearray_find
            // B.find(sub[, start[, end]]) -> int
            // Return the lowest index in B where subsection sub is found, such that sub is contained within B[start,end].
            // Optional arguments start and end are interpreted as in slice notation.
            // Return -1 on failure.
            baType.TypeDict["find"] = new PyMethodDescriptor(
                "find", baType,
                (self, args, kwargs) => {
                    if (self is not PyByteArray ba)
                        throw PyTypeError.Create($"descriptor 'find' requires a 'bytearray' object but received a '{self.GetTypeName()}'");

                    if (args.Length < 1 || args.Length > 3)
                        throw PyTypeError.Create($"find() takes at least 1 argument ({args.Length} given)");

                    // Accept bytes, bytearray, or int (single byte)
                    byte[] subBytes;
                    if (args[0] is PyBytes subBytesObj)
                    {
                        subBytes = subBytesObj.Value;
                    }
                    else if (args[0] is PyByteArray subByteArray)
                    {
                        subBytes = subByteArray._bytes.ToArray();
                    }
                    // CPython 3.12: Objects/bytearrayobject.c:1180-1220 - bytearray_find
                    else if (args[0] is PyInt intValue)
                    {
                        // CPython allows int (0-255) as single byte to search
                        long val = (long)intValue.Value;
                        if (val < 0 || val > 255)
                            throw PyValueError.Create("byte must be in range(0, 256)");
                        subBytes = new byte[] { (byte)val };
                    }
                    else
                    {
                        throw PyTypeError.Create($"a bytes-like object is required, not '{args[0].GetTypeName()}'");
                    }

                    int start = 0;
                    int end = ba._bytes.Count;

                    if (args.Length >= 2 && args[1] is PyInt startInt)
                        start = (int)startInt.Value;
                    if (args.Length >= 3 && args[2] is PyInt endInt)
                        end = (int)endInt.Value;

                    // Normalize indices (CPython behavior)
                    if (start < 0) start += ba._bytes.Count;
                    if (end < 0) end += ba._bytes.Count;
                    start = Math.Max(0, Math.Min(start, ba._bytes.Count));
                    end = Math.Max(0, Math.Min(end, ba._bytes.Count));

                    // Search for substring
                    for (int i = start; i <= end - subBytes.Length; i++)
                    {
                        bool match = true;
                        for (int j = 0; j < subBytes.Length; j++)
                        {
                            if (ba._bytes[i + j] != subBytes[j])
                            {
                                match = false;
                                break;
                            }
                        }
                        if (match) return new PyInt(i);
                    }
                    return new PyInt(-1);
                },
                minArgs: 1, maxArgs: 3
            );

            // CPython 3.12: Objects/bytearrayobject.c:2100-2200 - bytearray_translate
            baType.TypeDict["translate"] = new PyMethodDescriptor(
                "translate", baType,
                (self, args, kwargs) => {
                    if (self is not PyByteArray ba)
                        throw PyTypeError.Create($"descriptor 'translate' requires a 'bytearray' object but received a '{self.GetTypeName()}'");

                    if (args.Length < 1 || args.Length > 2)
                        throw PyTypeError.Create($"translate() takes at least 1 argument ({args.Length} given)");

                    // Get translation table (can be None)
                    PyObject tableArg = args[0];
                    byte[]? table = null;

                    if (tableArg != PyNone.Instance)
                    {
                        if (tableArg is PyBytes tableBytes)
                        {
                            table = tableBytes.Value;
                            if (table.Length != 256)
                                throw PyValueError.Create("translation table must be 256 characters long");
                        }
                        else if (tableArg is PyByteArray tableByteArray)
                        {
                            table = tableByteArray._bytes.ToArray();
                            if (table.Length != 256)
                                throw PyValueError.Create("translation table must be 256 characters long");
                        }
                        else
                        {
                            throw PyTypeError.Create($"a bytes-like object is required, not '{tableArg.GetTypeName()}'");
                        }
                    }

                    // Get delete characters (optional)
                    byte[] deleteChars = Array.Empty<byte>();
                    if (args.Length >= 2 && args[1] != PyNone.Instance)
                    {
                        if (args[1] is PyBytes deleteBytes)
                        {
                            deleteChars = deleteBytes.Value;
                        }
                        else if (args[1] is PyByteArray deleteByteArray)
                        {
                            deleteChars = deleteByteArray._bytes.ToArray();
                        }
                        else
                        {
                            throw PyTypeError.Create($"a bytes-like object is required, not '{args[1].GetTypeName()}'");
                        }
                    }

                    // Create delete set for fast lookup
                    HashSet<byte> deleteSet = new HashSet<byte>(deleteChars);

                    // Translate bytearray
                    List<byte> result = new List<byte>();
                    foreach (byte b in ba._bytes)
                    {
                        // Skip if in delete set
                        if (deleteSet.Contains(b))
                            continue;

                        // Apply translation if table exists
                        if (table != null)
                        {
                            result.Add(table[b]);
                        }
                        else
                        {
                            result.Add(b);
                        }
                    }

                    return new PyByteArray(result.ToArray());
                },
                minArgs: 1, maxArgs: 2
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
            // Performance: Eliminated LINQ - Direct list construction
            _bytes = bytes != null ? new List<byte>(bytes) : new List<byte>();
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
                // Performance: Direct list copy instead of ToArray
                return new PyByteArray(pyByteArray._bytes);
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

            // Performance: Direct list usage instead of ToArray
            return new PyByteArray(bytes);
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
                    // Performance: Direct list usage instead of ToArray
                    return new PyByteArray(result);
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
                // Performance: Direct list access instead of ToArray
                return ContainsSubsequenceList(pyByteArray._bytes) ? PyBool.True : PyBool.False;
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
                // Performance: Direct list usage instead of ToArray
                return new PyByteArray(result);
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
                // Performance: Eliminated LINQ - Manual sequence comparison
                return BytesEqualList(_bytes, otherBa._bytes) ? PyBool.True : PyBool.False;
            }
            else if (other is PyBytes otherBytes)
            {
                // Performance: Eliminated LINQ - Manual sequence comparison
                return BytesEqualArray(_bytes, otherBytes.Value) ? PyBool.True : PyBool.False;
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
                // Performance: Eliminated LINQ - Direct list comparison
                return CompareBytesList(_bytes, otherBa._bytes) < 0 ? PyBool.True : PyBool.False;
            }
            else if (other is PyBytes otherBytes)
            {
                // Performance: Eliminated LINQ - Direct list vs array comparison
                return CompareBytesListArray(_bytes, otherBytes.Value) < 0 ? PyBool.True : PyBool.False;
            }

            return PyNotImplemented.Instance;
        }

        protected override PyObject PyLessEqual(PyObject other)
        {
            if (other is PyByteArray otherBa)
            {
                // Performance: Eliminated LINQ - Direct list comparison
                return CompareBytesList(_bytes, otherBa._bytes) <= 0 ? PyBool.True : PyBool.False;
            }
            else if (other is PyBytes otherBytes)
            {
                // Performance: Eliminated LINQ - Direct list vs array comparison
                return CompareBytesListArray(_bytes, otherBytes.Value) <= 0 ? PyBool.True : PyBool.False;
            }

            return PyNotImplemented.Instance;
        }

        protected override PyObject PyGreater(PyObject other)
        {
            if (other is PyByteArray otherBa)
            {
                // Performance: Eliminated LINQ - Direct list comparison
                return CompareBytesList(_bytes, otherBa._bytes) > 0 ? PyBool.True : PyBool.False;
            }
            else if (other is PyBytes otherBytes)
            {
                // Performance: Eliminated LINQ - Direct list vs array comparison
                return CompareBytesListArray(_bytes, otherBytes.Value) > 0 ? PyBool.True : PyBool.False;
            }

            return PyNotImplemented.Instance;
        }

        protected override PyObject PyGreaterEqual(PyObject other)
        {
            if (other is PyByteArray otherBa)
            {
                // Performance: Eliminated LINQ - Direct list comparison
                return CompareBytesList(_bytes, otherBa._bytes) >= 0 ? PyBool.True : PyBool.False;
            }
            else if (other is PyBytes otherBytes)
            {
                // Performance: Eliminated LINQ - Direct list vs array comparison
                return CompareBytesListArray(_bytes, otherBytes.Value) >= 0 ? PyBool.True : PyBool.False;
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
            // Performance: ToArray needed here for buffer export (creates snapshot)
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

        // Performance: Eliminated LINQ - Helper methods for List<byte> operations

        /// <summary>
        /// Check if two byte lists are equal
        /// </summary>
        private static bool BytesEqualList(List<byte> a, List<byte> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        /// <summary>
        /// Check if byte list equals byte array
        /// </summary>
        private static bool BytesEqualArray(List<byte> list, byte[] array)
        {
            if (list.Count != array.Length) return false;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != array[i]) return false;
            }
            return true;
        }

        /// <summary>
        /// Compare two byte lists lexicographically
        /// </summary>
        private static int CompareBytesList(List<byte> a, List<byte> b)
        {
            int minLen = Math.Min(a.Count, b.Count);
            for (int i = 0; i < minLen; i++)
            {
                if (a[i] != b[i])
                    return a[i].CompareTo(b[i]);
            }
            return a.Count.CompareTo(b.Count);
        }

        /// <summary>
        /// Compare byte list with byte array lexicographically
        /// </summary>
        private static int CompareBytesListArray(List<byte> list, byte[] array)
        {
            int minLen = Math.Min(list.Count, array.Length);
            for (int i = 0; i < minLen; i++)
            {
                if (list[i] != array[i])
                    return list[i].CompareTo(array[i]);
            }
            return list.Count.CompareTo(array.Length);
        }

        /// <summary>
        /// Check if subsequence exists in bytearray using List
        /// </summary>
        private bool ContainsSubsequenceList(List<byte> sub)
        {
            if (sub.Count == 0)
                return true;
            if (sub.Count > _bytes.Count)
                return false;

            for (int i = 0; i <= _bytes.Count - sub.Count; i++)
            {
                bool found = true;
                for (int j = 0; j < sub.Count; j++)
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
