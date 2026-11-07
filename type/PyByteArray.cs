using System;
using System.Collections.Generic;
using System.Text;

namespace SharpPy
{
    /// <summary>
    /// Python bytearray type - mutable sequence of bytes
    /// CPython 3.12 compatible implementation
    /// </summary>
    public class PyByteArray : PyObject, IBufferExporter
    {
        #region Fields

        private List<byte> _bytes;
        private int _exports = 0;  // Track buffer exports for resize safety

        internal void HoldExport() => _exports++;
        internal void ReleaseExport() => _exports--;

        public byte[] ToByteArrayCopy() => _bytes.ToArray();

        #endregion

        #region Static Constructor & Descriptors

        static PyByteArray()
        {
            InitializeByteArrayDescriptors();
        }

        private static void InitializeByteArrayDescriptors()
        {
            var baType = PyType.BytearrayType;
            RegisterBytesBackedMethods(baType);

            // CPython 3.12: bytearray.__setitem__ (index or slice, int 0..255 value)
            baType.TypeDict["__setitem__"] = new PyMethodDescriptor(
                "__setitem__", baType,
                (self, args, kwargs) => {
                    if (args.Length != 2)
                        throw PyTypeError.Create($"__setitem__() takes exactly 2 arguments ({args.Length} given)");
                    if (self is not PyByteArray baSelf)
                        throw PyTypeError.Create($"descriptor '__setitem__' requires a 'bytearray' object but received a '{self.GetTypeName()}'");
                    baSelf.MpAssSubscript(args[0], args[1]);
                    return PyNone.Instance;
                }, minArgs: 2, maxArgs: 2);

            baType.TypeDict["__delitem__"] = new PyMethodDescriptor(
                "__delitem__", baType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"__delitem__() takes exactly one argument ({args.Length} given)");
                    if (self is not PyByteArray ba)
                        throw PyTypeError.Create($"descriptor '__delitem__' requires a 'bytearray' object but received a '{self.GetTypeName()}'");
                    ba.MpDelSubscript(args[0]);
                    return PyNone.Instance;
                }, minArgs: 1, maxArgs: 1);

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
            // p40: count / index / rfind / rindex / startswith / endswith /
            // removeprefix / removesuffix were missing entirely (AttributeError), and
            // find ignored non-integer start/stop. All share the CPython core above.
            PyMethodDescriptor Ba(string name, System.Func<PyByteArray, PyObject[], PyObject> body, int min, int max)
                => new PyMethodDescriptor(name, baType, (self, args, kwargs) => {
                    if (self is not PyByteArray selfBa)
                        throw PyTypeError.Create($"descriptor '{name}' requires a 'bytearray' object but received a '{self.GetTypeName()}'");
                    if (args.Length < min)
                        throw PyTypeError.Create($"{name}() takes at least {min} argument ({args.Length} given)");
                    if (args.Length > max)
                    {
                        // METH_O members (removeprefix/removesuffix) use the CPython
                        // "bytearray.NAME() takes exactly one argument" form.
                        if (max == 1)
                            throw PyTypeError.Create($"bytearray.{name}() takes exactly one argument ({args.Length} given)");
                        throw PyTypeError.Create($"{name}() takes at most {max} arguments ({args.Length} given)");
                    }
                    return body(selfBa, args);
                }, minArgs: min, maxArgs: max);


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

        public override string GetTypeName() => GetPyType().Name;
        public override PyType GetPyType() => _customType ?? PyType.BytearrayType;

        // bytearray_init: the arguments reach the object tp_new allocated.
        // bytearray___init___impl: "Empty previous contents (yes, do this first of all!)", and the
        // bytes of an iterable arrive one at a time, in sight of that iterable.
        internal void Initialize(PyObject[] args, PyDict kwargs)
        {
            _bytes.Clear();
            var bytes = BytesMethods.ByteArrayInit(args, kwargs, _bytes);
            _bytes.Clear();
            _bytes.AddRange(bytes);
        }

        internal static void InstallNew()
        {
            var type = PyType.BytearrayType;
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) =>
                ReferenceEquals(cls, type) ? new PyByteArray() : new PyByteArraySubclass((PyType)cls), acceptsKwargs: true);
            type.TypeDict["__init__"] = new PyWrapperDescriptor("__init__", type, (self, args, kwargs) =>
            {
                ((PyByteArray)self).Initialize(args, kwargs);
                return PyNone.Instance;
            }, 0, int.MaxValue, acceptsKwargs: true);
        }

        public override string ToString() => ToRepr().Value;

        // ob_alloc: there is room for the trailing NUL of a C string.
        internal int Allocated => _bytes.Count == 0 ? 0 : Math.Max(_bytes.Capacity, _bytes.Count + 1);

        public override PyObject Modulo(PyObject other) => new PyByteArray(PyBytes.Format(ToByteArrayCopy(), other));

        // bytes_str and bytearray_str: this type's own repr function, not the type's slot.
        protected internal override PyString Str() => (PyString)Repr();

        protected internal override PyString Repr()
        {
            // bytearray_repr: the quote is chosen as for bytes, but ' is escaped either way.
            bool hasSingle = false, hasDouble = false;
            foreach (byte b in _bytes)
            {
                hasSingle |= b == '\'';
                hasDouble |= b == '"';
            }
            char quote = hasSingle && !hasDouble ? '"' : '\'';
            var sb = new StringBuilder(GetTypeName()).Append("(b").Append(quote);
            foreach (byte b in _bytes)
            {
                if (b == quote || b == '\\' || b == '\'')
                    sb.Append('\\').Append((char)b);
                else if (b == '\n')
                    sb.Append("\\n");
                else if (b == '\r')
                    sb.Append("\\r");
                else if (b == '\t')
                    sb.Append("\\t");
                else if (b < 32 || b >= 127)
                    sb.Append($"\\x{b:x2}");
                else
                    sb.Append((char)b);
            }
            return PyString.From(sb.Append(quote).Append(')').ToString());
        }

        protected internal override long Len() => _bytes.Count;
        protected internal override bool Bool() => _bytes.Count > 0;

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
        protected internal override PyObject MpSubscript(PyObject index)
        {
            if (BytesMethods.IsIndex(index))
            {
                long idx = BytesMethods.AsIndex(index);
                if (idx < 0) idx += _bytes.Count;

                if (idx < 0 || idx >= _bytes.Count)
                    throw PyIndexError.Create("bytearray index out of range");

                return PyInt.From(_bytes[(int)idx]);
            }
            else if (index is PySlice slice)
            {
                // Slice access - returns new bytearray
                var (start, stop, step) = slice.StorageIndices(_bytes.Count);

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
                        for (long position = start; position < stop; position += step)
                        {
                            int i = (int)position;
                            if ((uint)i < (uint)_bytes.Count)
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

            if (TryGetIndex(index) is { } itemIndex)
                return GetItem(itemIndex);
            throw PyTypeError.Create($"bytearray indices must be integers or slices, not {index.GetTypeName()}");
        }

        /// <summary>
        /// __setitem__ - Set item by index or slice
        /// </summary>
        protected internal override void MpAssSubscript(PyObject index, PyObject value)
        {
            if (BytesMethods.IsIndex(index))
            {
                long idx = BytesMethods.AsIndex(index);
                // CPython converts the value before checking the current size:
                // its __index__ callback may resize this bytearray.
                int byteValue = GetByteValue(value);
                if (idx < 0) idx += _bytes.Count;

                if (idx < 0 || idx >= _bytes.Count)
                    throw PyIndexError.Create("bytearray index out of range");

                _bytes[(int)idx] = (byte)byteValue;
            }
            else if (index is PySlice slice)
            {
                if (value is PyInt or PyBool or PyString)
                    throw PyTypeError.Create("can assign only bytes, buffers, or iterables of ints in range(0, 256)");
                var (start, stop, step) = slice.UnpackStorage();
                List<byte> newBytes;
                if (value is PyByteArray source && !ReferenceEquals(source, this))
                    newBytes = source._bytes;
                else
                {
                    // Immutable bytes need no snapshot; retain the existing buffer.
                    newBytes = value is PyBytes ? null : ConvertToBytes(value);
                    // CPython re-enters slice assignment after converting the RHS.
                    // Immutable built-in indices cannot observe that second unpack.
                    if (slice.HasIndexCallbacks)
                        (start, stop, step) = slice.UnpackStorage();
                }
                // Both index callbacks and RHS iteration may resize the target.
                (start, stop, step) = PySlice.AdjustStorageIndices(_bytes.Count, start, stop, step);
                // bytearray_ass_subscript: an empty slice is an insertion point.
                if ((step < 0 && start < stop) || (step > 0 && start > stop))
                    stop = start;
                ReadOnlySpan<byte> replacement = value is PyBytes bytes
                    ? bytes.Value : System.Runtime.InteropServices.CollectionsMarshal.AsSpan(newBytes);

                if (step == 1)
                {
                    // Contiguous slice - can change size
                    int deleteCount = Math.Max(0, stop - start);
                    if (HasBufferExports && deleteCount != replacement.Length)
                        throw PyBufferError.Create("Existing exports of data: object cannot be re-sized");
                    _bytes.RemoveRange(start, deleteCount);
                    if (value is PyBytes contiguousBytes) _bytes.InsertRange(start, contiguousBytes.Value);
                    else _bytes.InsertRange(start, newBytes);
                }
                else
                {
                    int count = PySlice.GetLength(start, stop, step);
                    if (replacement.Length == 0)
                    {
                        if (HasBufferExports)
                            throw PyBufferError.Create("Existing exports of data: object cannot be re-sized");
                        DeleteExtendedSlice(start, step, count);
                        return;
                    }
                    if (count != replacement.Length)
                        throw PyValueError.Create($"attempt to assign bytes of size {replacement.Length} to extended slice of size {count}");
                    for (int n = 0, position = start; n < count; n++, position = unchecked(position + step))
                        _bytes[position] = replacement[n];
                }
            }
            else
            {
                if (TryGetIndex(index) is { } storeIndex)
                {
                    SetItem(storeIndex, value);
                    return;
                }
                throw PyTypeError.Create($"bytearray indices must be integers or slices, not {index.GetTypeName()}");
            }
        }

        /// <summary>
        /// __delitem__ - Delete item by index or slice
        /// </summary>
        protected internal override void MpDelSubscript(PyObject index)
        {
            if (HasBufferExports)
                throw PyBufferError.Create("Existing exports of data: object cannot be re-sized");

            if (BytesMethods.IsIndex(index))
            {
                long idx = BytesMethods.AsIndex(index);
                if (idx < 0) idx += _bytes.Count;

                if (idx < 0 || idx >= _bytes.Count)
                    throw PyIndexError.Create("bytearray index out of range");

                _bytes.RemoveAt((int)idx);
            }
            else if (index is PySlice slice)
            {
                // Slice deletion
                var (start, stop, step) = slice.StorageIndices(_bytes.Count);

                if (step == 1)
                {
                    // Contiguous slice
                    int deleteCount = Math.Max(0, stop - start);
                    _bytes.RemoveRange(start, deleteCount);
                }
                else
                {
                    int count = PySlice.GetLength(start, stop, step);
                    DeleteExtendedSlice(start, step, count);
                }
            }
            else
            {
                if (TryGetIndex(index) is { } deleteIndex)
                {
                    DelItem(deleteIndex);
                    return;
                }
                throw PyTypeError.Create($"bytearray indices must be integers or slices, not {index.GetTypeName()}");
            }
        }

        /// <summary>
        /// __contains__ - Check if byte or bytes is in bytearray
        /// </summary>
        protected internal override PyBool ContainsItem(PyObject item) => PyBool.FromBool(BytesMethods.Contains(ToByteArrayCopy(), item));

        protected internal override PyObject Iter()
        {
            return new PyByteArrayIterator(this);
        }

        #endregion

        #region Operators

        // bytearray_iconcat and bytearray_irepeat keep the object.
        public override PyObject? InplaceAdd(PyObject other)
        {
            var joined = Add(other);
            if (joined is not PyByteArray result)
                return joined;
            _bytes = result._bytes;
            return this;
        }

        public override PyObject? InplaceMultiply(PyObject other)
        {
            var repeated = Multiply(other);
            if (repeated is not PyByteArray result)
                return repeated;
            _bytes = result._bytes;
            return this;
        }

        /// <summary>
        /// __add__ - Concatenation (returns new bytearray)
        /// </summary>
        public override PyObject Add(PyObject other)
        {
            return BytesMethods.Concat(this, other);
        }

        private void ReplaceContents(List<byte> bytes)
        {
            if (HasBufferExports && bytes.Count != _bytes.Count)
                throw PyBufferError.Create("Existing exports of data: object cannot be re-sized");
            _bytes = bytes;
        }

        // bytearray_iconcat
        internal PyObject InPlaceConcat(PyObject other)
        {
            var joined = (PyByteArray)Add(other);
            ReplaceContents(joined._bytes);
            return this;
        }

        // bytearray_irepeat
        internal PyObject InPlaceRepeat(PyObject count)
        {
            var repeated = (PyByteArray)Multiply(count);
            ReplaceContents(repeated._bytes);
            return this;
        }

        /// <summary>
        /// __mul__ - Repetition (returns new bytearray)
        /// </summary>
        public override PyObject Multiply(PyObject other)
        {
            if (other is PyInt count)
            {
                long times = PyAllocation.IndexSized(count.Value);
                if (times <= 0)
                    return new PyByteArray(new byte[0]);

                // bytearray_repeat: a size past Py_ssize_t is PyErr_NoMemory, as is one no array holds.
                var result = new List<byte>(PyAllocation.RepeatedItems(_bytes.Count, times));
                for (long i = 0; _bytes.Count != 0 && i < times; i++)
                {
                    result.AddRange(_bytes);
                }
                // Performance: Direct list usage instead of ToArray
                return new PyByteArray(result);
            }

            throw PyTypeError.Create($"can't multiply sequence by non-int of type '{other.GetTypeName()}'");
        }

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
            if (HasBufferExports)
                throw PyBufferError.Create("Existing exports of data: object cannot be re-sized");

            _bytes.Add(value);
        }

        public void Extend(PyObject iterable)
        {
            if (HasBufferExports)
                throw PyBufferError.Create("Existing exports of data: object cannot be re-sized");

            _bytes.AddRange(BytesMethods.FromObject(iterable, "bytearray", 32));
        }

        public void Insert(int index, byte value)
        {
            if (HasBufferExports)
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
            if (HasBufferExports)
                throw PyBufferError.Create("Existing exports of data: object cannot be re-sized");

            int index = _bytes.IndexOf(value);
            if (index == -1)
                throw PyValueError.Create("value not found in bytearray");

            _bytes.RemoveAt(index);
        }

        public PyInt Pop(int index = -1)
        {
            if (HasBufferExports)
                throw PyBufferError.Create("Existing exports of data: object cannot be re-sized");

            if (_bytes.Count == 0)
                throw PyIndexError.Create("pop from empty bytearray");

            // Normalize negative index
            if (index < 0) index += _bytes.Count;

            if (index < 0 || index >= _bytes.Count)
                throw PyIndexError.Create("pop index out of range");

            byte value = _bytes[index];
            _bytes.RemoveAt(index);
            return PyInt.From(value);
        }

        public void Clear()
        {
            if (HasBufferExports)
                throw PyBufferError.Create("Existing exports of data: object cannot be re-sized");

            _bytes.Clear();
        }

        public void Reverse()
        {
            _bytes.Reverse();
        }

        #endregion

        #region Buffer Protocol

        // CPython 3.12: Objects/floatobject.c PyFloat_FromString accepts any buffer.

        public override PyMemoryView GetBuffer(int flags)
        {
            return PyMemoryView.FromByteArray(this);
        }

        public override void ReleaseBuffer(PyMemoryView buffer)
        {
            buffer.Release();
        }

        public void AcquireBufferExport() => System.Threading.Interlocked.Increment(ref _exports);
        public void ReleaseBufferExport() => System.Threading.Interlocked.Decrement(ref _exports);
        private bool HasBufferExports => BufferExports.Held(() => System.Threading.Volatile.Read(ref _exports) > 0);
        public Span<byte> BufferSpan => System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_bytes);

        public override bool SupportsBuffer() => true;

        #endregion

        #region Helper Methods

        /// <summary>
        /// Validate and convert PyObject to byte value (0-255)
        /// </summary>
        private static void RegisterBytesBackedMethods(PyType baType)
        {
            BytesMethods.Install(baType);
            BytesMethods.InstallByteArray(baType);

            // Measured surface: bytearray has __add__/__mul__/__rmul__/__iter__, and does NOT have
            // __bytes__ or __getnewargs__ (bytes has both). An earlier draft published them.
            baType.TypeDict["__add__"] = new PyMethodDescriptor("__add__", baType,
                (self, args, kwargs) => self.Add(args[0]), minArgs: 1, maxArgs: 1);
            baType.TypeDict["__mul__"] = new PyMethodDescriptor("__mul__", baType,
                (self, args, kwargs) => self.Multiply(args[0]), minArgs: 1, maxArgs: 1);
            baType.TypeDict["__rmul__"] = new PyMethodDescriptor("__rmul__", baType,
                (self, args, kwargs) => self.Multiply(args[0]), minArgs: 1, maxArgs: 1);
            baType.TypeDict["__iter__"] = new PyMethodDescriptor("__iter__", baType,
                (self, args, kwargs) => self.Iter(), minArgs: 0, maxArgs: 0);
        }

        private static int GetByteValue(PyObject obj)
        {
            var pyInt = obj as PyInt ?? PyObject.TryGetIndex(obj);
            if (pyInt == null)
                throw PyTypeError.Create($"'{obj.GetTypeName()}' object cannot be interpreted as an integer");

            // Oversized integers are byte-range errors, not CLR cast overflow.
            int value = int.CreateSaturating(pyInt.Value);
            if ((uint)value > 255)
                throw PyValueError.Create("byte must be in range(0, 256)");

            return value;
        }

        /// <summary>
        /// Delete normalized extended-slice positions by compacting survivors.
        /// </summary>
        private void DeleteExtendedSlice(int start, int step, int count)
        {
            PySlice.DeleteItems(_bytes, start, step, count);
        }

        private static List<byte> ConvertToBytes(PyObject obj) => new List<byte>(BytesMethods.FromObject(obj, "bytearray"));

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

        #endregion
    }

    /// <summary>
    /// Iterator for bytearray objects
    /// </summary>
    public class PyByteArrayIterator : PyIterator
    {
        private PyByteArray _bytearray;
        private int _index;

        protected override string IteratorName => "bytearray_iterator";

        public PyByteArrayIterator(PyByteArray bytearray)
        {
            _bytearray = bytearray;
            _index = 0;
        }

        // bytearrayiter_next: an exhausted iterator lets go of the bytearray.
        protected internal override PyObject? IterNext()
        {
            if (_bytearray == null || _index >= _bytearray.Length())
            {
                _bytearray = null;
                throw PyStopIteration.Create();
            }

            var value = _bytearray.GetItem(PyInt.From(_index));
            _index++;
            return value;
        }

        internal override PyObject RemainingHint() => PyInt.From(_bytearray == null ? 0 : Math.Max(0, _bytearray.Length() - _index));

        internal override PyObject Reduce() => _bytearray != null
            ? ReduceIter(_bytearray, _index)
            : ReduceExhausted(PyTuple.From(new PyObject[0]));

        internal override void SetState(PyObject state)
        {
            if (_bytearray != null)
                _index = (int)ClippedIndex(state, _bytearray.Length());
        }
    }

    /// <summary>A Python class deriving from bytearray: the layout of bytearray, its own type and __dict__.</summary>
    public sealed class PyByteArraySubclass : PyByteArray
    {
        public PyByteArraySubclass(PyType type) => SetCustomType(type);


    }

}
