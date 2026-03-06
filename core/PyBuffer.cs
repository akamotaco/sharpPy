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
        public override PyStr ToRepr() => new PyStr(ToString());
    }

    /// <summary>
    /// MemoryView implementation for buffer protocol
    /// Represents a view into buffer data
    /// </summary>
    public class PyMemoryView : PyObject
    {
        private readonly byte[] _data;
        private readonly bool _readonly;
        private readonly int _itemsize;

        public PyMemoryView(byte[] data, bool readOnly = false, int itemsize = 1)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            _readonly = readOnly;
            _itemsize = itemsize;  // CPython: Py_buffer.itemsize
        }

        public byte[] Data => _data;
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
                if (idx < 0) idx += _data.Length;
                if (idx < 0 || idx >= _data.Length)
                    throw PyIndexError.Create("memoryview index out of range");

                return new PyInt(_data[(int)idx]);
            }
            // CPython 3.12: line 2578-2593 - slice support
            else if (index is PySlice slice)
            {
                // Calculate slice indices
                var (start, stop, step) = slice.Indices(_data.Length);

                // Create new byte array for sliced data
                var sliceLength = slice.GetLength(_data.Length);
                var slicedData = new byte[sliceLength];

                int idx = 0;
                for (int i = start; step > 0 ? i < stop : i > stop; i += step)
                {
                    slicedData[idx++] = _data[i];
                }

                // Return new memoryview with sliced data
                // CPython: creates a new memoryview object sharing the same buffer
                // SharpPy: creates a new memoryview with copied data (simpler implementation)
                return new PyMemoryView(slicedData, _readonly, _itemsize);
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
                if (idx < 0) idx += _data.Length;
                // CPython 3.12: Objects/memoryobject.c:2700-2730 - memory_ass_sub
                if (idx < 0 || idx >= _data.Length)
                    throw PyIndexError.Create("memoryview index out of range");

                if (pyValue.Value < 0 || pyValue.Value > 255)
                    throw PyValueError.Create("byte must be in range(0, 256)");

                _data[(int)idx] = (byte)(int)pyValue.Value;
                return;
            }
            throw PyTypeError.Create($"memoryview indices must be integers, not {index.GetTypeName()}");
        }

        public override int Length() => _data.Length;

        public override string ToString() => $"<memory at 0x{GetHashCode():X}>";
        public override PyStr ToRepr() => new PyStr(ToString());
    }
}