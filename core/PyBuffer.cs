using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// PEP 688: Buffer Protocol implementation for SharpPy
    /// Provides Python-level access to the buffer protocol
    /// </summary>
    public abstract class PyBuffer : PyObject
    {
        public override PyType GetPyType() => PyType.BufferType;
        public override string GetTypeName() => "buffer";

        /// <summary>
        /// PEP 688: __buffer__ method
        /// Returns a memoryview object representing this buffer
        /// </summary>
        public abstract override PyMemoryView GetBuffer(int flags);

        /// <summary>
        /// PEP 688: __release_buffer__ method (optional)
        /// Releases resources associated with the buffer
        /// </summary>
        public override void ReleaseBuffer(PyMemoryView buffer)
        {
            // Default implementation - no cleanup needed
        }

        /// <summary>
        /// Check if this object implements the buffer protocol
        /// </summary>
        public override bool SupportsBuffer() => true;
    }

    /// <summary>
    /// collections.abc.Buffer ABC implementation
    /// Used for isinstance/issubclass checks and type annotations
    /// </summary>
    public class PyBufferABC : PyType
    {
        public PyBufferABC() : base("Buffer", new[] { PyType.ObjectType })
        {
        }

        public override PyObject CreateInstance(PyObject[] args, PyDict kwargs = null)
        {
            throw PyTypeError.Create("Buffer is an abstract base class and cannot be instantiated directly");
        }

        /// <summary>
        /// Check if a type implements the buffer protocol
        /// </summary>
        public static bool IsBuffer(PyObject obj)
        {
            return obj is PyBuffer || 
                   obj.GetAttribute("__buffer__") != null;
        }
    }

    /// <summary>
    /// Basic implementation of a byte buffer for SharpPy
    /// Wraps C# byte[] with buffer protocol support
    /// </summary>
    public class PyByteBuffer : PyBuffer
    {
        private readonly byte[] _data;
        private readonly bool _readonly;

        public PyByteBuffer(byte[] data, bool readOnly = false)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            _readonly = readOnly;
        }

        public override PyMemoryView GetBuffer(int flags)
        {
            // Create memoryview from byte data
            // Note: This is a simplified implementation
            // In full implementation, would need to handle all buffer flags
            return new PyMemoryView(_data, _readonly);
        }

        public override void ReleaseBuffer(PyMemoryView buffer)
        {
            // For byte arrays, no special cleanup needed
            // In more complex implementations might need resource cleanup
        }

        public override string ToString() => $"<buffer at 0x{GetHashCode():X}>";
        public override PyString ToRepr() => new PyString(ToString());
    }

    /// <summary>
    /// MemoryView implementation for buffer protocol
    /// Represents a view into buffer data
    /// </summary>
    public class PyMemoryView : PyObject
    {
        private readonly IList<byte> _data;
        private readonly bool _readonly;
        private readonly int _itemsize;
        private readonly int _offset;
        private readonly int _length;
        private readonly int _stride;

        public PyMemoryView(byte[] data, bool readOnly = false, int itemsize = 1)
            : this(data, readOnly, itemsize, 0, data?.Length ?? 0, 1) { }

        internal PyMemoryView(List<byte> data)
            : this(data, false, 1, 0, data.Count, 1) { }

        private PyMemoryView(IList<byte> data, bool readOnly, int itemsize,
            int offset, int length, int stride)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            _readonly = readOnly;
            _itemsize = itemsize;  // CPython: Py_buffer.itemsize
            _offset = offset;
            _length = length;
            _stride = stride;
        }

        public byte[] Data
        {
            get
            {
                if (_data is byte[] array && _offset == 0 && _stride == 1 && _length == array.Length)
                    return array;
                var result = new byte[_length];
                for (int i = 0; i < result.Length; i++) result[i] = _data[_offset + i * _stride];
                return result;
            }
        }
        public bool ReadOnly => _readonly;
        public int ItemSize => _itemsize;  // CPython: self->view.itemsize

        public override PyType GetPyType() => PyType.MemoryViewType;
        public override string GetTypeName() => "memoryview";

        /// <summary>
        /// CPython: PyGetSetDef memory_getsetlist - expose C# properties as Python attributes
        /// Objects/memoryobject.c:memory_itemsize_get
        /// </summary>
        public override PyObject GetAttribute(string name)
        {
            if (name == "itemsize")
            {
                return new PyInt(_itemsize);  // CPython: PyLong_FromSsize_t(self->view.itemsize)
            }
            return base.GetAttribute(name);
        }

        /// <summary>
        /// CPython 3.12: Objects/memoryobject.c:2547-2606 - memory_subscript
        /// Supports both integer indexing and slicing
        /// </summary>
        public override PyObject GetItem(PyObject index)
        {
            // CPython 3.12: line 2571-2576 - integer indexing
            if (index is PyInt pyInt)
            {
                // CPython 3.12: Objects/memoryobject.c:2540-2560 - memory_subscript
                var idx = pyInt.Value;
                if (idx < 0) idx += _length;
                if (idx < 0 || idx >= _length)
                    throw PyIndexError.Create("memoryview index out of range");

                return SmallIntCache.GetOrCreate(_data[_offset + (int)idx * _stride]);
            }
            // CPython 3.12: line 2578-2593 - slice support
            else if (index is PySlice slice)
            {
                // Calculate slice indices
                var (start, stop, step) = slice.Indices(_length);
                var sliceLength = slice.GetLength(_length);
                return new PyMemoryView(_data, _readonly, _itemsize,
                    _offset + start * _stride, sliceLength, _stride * step);
            }
            throw PyTypeError.Create($"memoryview indices must be integers or slices, not {index.GetTypeName()}");
        }

        public override void SetItem(PyObject index, PyObject value)
        {
            if (_readonly)
                throw PyTypeError.Create("cannot modify read-only memory");

            if (index is PyInt pyInt && value is PyInt pyValue)
            {
                var idx = pyInt.Value;
                if (idx < 0) idx += _length;
                // CPython 3.12: Objects/memoryobject.c:2700-2730 - memory_ass_sub
                if (idx < 0 || idx >= _length)
                    throw PyIndexError.Create("memoryview index out of range");

                if (pyValue.Value < 0 || pyValue.Value > 255)
                    throw PyValueError.Create("byte must be in range(0, 256)");

                _data[_offset + (int)idx * _stride] = (byte)(int)pyValue.Value;
                return;
            }
            throw PyTypeError.Create($"memoryview indices must be integers, not {index.GetTypeName()}");
        }

        public override int Length() => _length;

        public override string ToString() => $"<memory at 0x{GetHashCode():X}>";
        public override PyString ToRepr() => new PyString(ToString());
    }
}
