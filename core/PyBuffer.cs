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
        // Either the exporting bytearray or an immutable-size byte[] buffer.
        // A null reference marks a released view and drops the backing storage.
        private object _data;
        private readonly int _itemsize;
        private readonly string _fmt;   // CPython Py_buffer.format ("" for byte views)
        // Physical storage is int-sized; only stride is exposed at native width.
        private readonly int _offset;
        private readonly int _length;
        private readonly nint _stride;
        // Bit 0 is readonly; the upper 31 bits hold the borrowed-export count.
        // Sharing this slot avoids growing each view for the native-width stride.
        private uint _bufferState;
        private bool _readonly => (_bufferState & 1) != 0;

        static PyMemoryView()
        {
            var type = PyType.MemoryViewType;

            // CPython 3.12: memoryview.tobytes()/hex()/tolist()/__bytes__
            type.TypeDict["tobytes"] = new PyMethodDescriptor("tobytes", type, (self, args, kwargs) =>
            {
                if (self is not PyMemoryView view)
                    throw PyTypeError.Create($"descriptor 'tobytes' requires a 'memoryview' object but received a '{self.GetTypeName()}'");
                if (args.Length != 0 || (kwargs != null && kwargs.Length() != 0))
                    throw PyTypeError.Create($"tobytes() takes no arguments ({args.Length} given)");
                view.CheckReleased();
                return new PyBytes(view.Data);
            });
            type.TypeDict["tolist"] = new PyMethodDescriptor("tolist", type, (self, args, kwargs) =>
            {
                if (self is not PyMemoryView view)
                    throw PyTypeError.Create($"descriptor 'tolist' requires a 'memoryview' object but received a '{self.GetTypeName()}'");
                if (args.Length != 0 || (kwargs != null && kwargs.Length() != 0))
                    throw PyTypeError.Create($"tolist() takes no arguments ({args.Length} given)");
                view.CheckReleased();
                int n = view._length;
                var items = new PyObject[n];
                for (int i = 0; i < n; i++)
                    items[i] = view.ReadElement(view.StorageIndex(i));
                return new PyList(items);
            });
            type.TypeDict["hex"] = new PyMethodDescriptor("hex", type, (self, args, kwargs) =>
            {
                if (self is not PyMemoryView view)
                    throw PyTypeError.Create($"descriptor 'hex' requires a 'memoryview' object but received a '{self.GetTypeName()}'");
                view.CheckReleased();
                string sep = "";
                if (args.Length > 0 && args[0] is PyString sepStr)
                {
                    sep = sepStr.Value;
                }
                var data = view.Data;
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < data.Length; i++)
                {
                    if (i > 0 && sep.Length > 0)
                    {
                        sb.Append(sep);
                    }
                    sb.Append(data[i].ToString("x2"));
                }
                return new PyString(sb.ToString());
            });
            type.TypeDict["__bytes__"] = new PyMethodDescriptor("__bytes__", type, (self, args, kwargs) =>
            {
                if (self is not PyMemoryView view)
                    throw PyTypeError.Create($"descriptor '__bytes__' requires a 'memoryview' object but received a '{self.GetTypeName()}'");
                view.CheckReleased();
                return new PyBytes(view.Data);
            });

            foreach (var name in new[] { "release", "__enter__", "__exit__" })
                type.TypeDict[name] = new PyMethodDescriptor(name, type, (self, args, kwargs) => {
                    if (self is not PyMemoryView view)
                        throw PyTypeError.Create($"descriptor '{name}' requires a 'memoryview' object but received a '{self.GetTypeName()}'");
                    if (kwargs != null && kwargs.Length() != 0)
                        throw PyTypeError.Create($"{name}() takes no keyword arguments");
                    if (name != "__exit__" && args.Length != 0)
                        throw PyTypeError.Create($"{name}() takes no arguments ({args.Length} given)");
                    if (name == "__enter__")
                    {
                        view.CheckReleased();
                        return view;
                    }
                    view.Release();
                    return PyNone.Instance;
                });

            // CPython 3.12: Objects/memoryobject.c memory_cast - reinterpret the
            // buffer's bytes as a different native element type. Only the
            // C-contiguous, single-character native formats are supported.
            type.TypeDict["cast"] = new PyMethodDescriptor("cast", type, (self, args, kwargs) =>
            {
                if (self is not PyMemoryView view)
                    throw PyTypeError.Create($"descriptor 'cast' requires a 'memoryview' object but received a '{self.GetTypeName()}'");
                if (kwargs != null && kwargs.Length() != 0)
                    throw PyTypeError.Create("cast() takes no keyword arguments");
                view.CheckReleased();
                if (args.Length < 1 || args.Length > 2)
                    throw PyTypeError.Create($"cast() takes from 1 to 2 positional arguments but {args.Length} were given");
                if (args[0] is not PyString fmtStr)
                    throw PyValueError.Create("memoryview: destination format must be a native single character format prefixed with an optional '@'");
                // CPython allows an optional native byte-order prefix; the host is
                // little-endian so '@' (native), '=' and '<' decode identically.
                string fs = fmtStr.Value;
                if (fs.Length == 2 && (fs[0] == '@' || fs[0] == '=' || fs[0] == '<' || fs[0] == '>' || fs[0] == '!'))
                    fs = fs.Substring(1);
                if (fs.Length != 1)
                    throw PyValueError.Create("memoryview: destination format must be a native single character format prefixed with an optional '@'");
                char f = fs[0];
                int isz = FormatSize(f);
                if (isz < 0)
                    throw PyValueError.Create("memoryview: destination format must be a native single character format prefixed with an optional '@'");
                if (view._length > 1 && view._stride != view._itemsize)
                    throw PyTypeError.Create("memoryview: underlying buffer is not C-contiguous");
                long totalBytes = (long)view._length * view._itemsize;
                if (totalBytes % isz != 0)
                    throw PyTypeError.Create("memoryview: length is not a multiple of itemsize");
                if (args.Length == 2)
                {
                    var dims = args[1] is PyTuple t ? t.Items
                             : args[1] is PyList lst ? lst.Items
                             : throw PyTypeError.Create("memoryview: shape must be a tuple of integers");
                    long product = 1;
                    foreach (var d in dims)
                    {
                        if (d is not PyInt di)
                            throw PyTypeError.Create("memoryview: shape elements must be integers");
                        product *= (long)di.Value;
                    }
                    if (product * isz != totalBytes)
                        throw PyTypeError.Create("memoryview: product(shape) * itemsize != buffer size");
                    if (dims.Length != 1)
                        throw PyTypeError.Create("memoryview: only 1-dimensional cast is supported");
                }
                return view.Reinterpret(f.ToString(), isz);
            });

            // CPython memoryview is iterable and yields one element per position
            // (honouring the current cast format/itemsize).
            type.TypeDict["__iter__"] = new PyMethodDescriptor("__iter__", type, (self, args, kwargs) =>
            {
                if (self is not PyMemoryView view)
                    throw PyTypeError.Create($"descriptor '__iter__' requires a 'memoryview' object but received a '{self.GetTypeName()}'");
                if (args.Length != 0 || (kwargs != null && kwargs.Length() != 0))
                    throw PyTypeError.Create("__iter__() takes no arguments");
                view.CheckReleased();
                int n = view._length;
                var items = new PyObject[n];
                for (int i = 0; i < n; i++)
                    items[i] = view.ReadElement(view.StorageIndex(i));
                return new PyList(items).GetIterator();
            });
        }

        public PyMemoryView(byte[] data, bool readOnly = false, int itemsize = 1)
            : this(data, readOnly, itemsize, 0, data?.Length ?? 0, 1) { }

        internal static PyMemoryView FromByteArray(PyByteArray data) =>
            new ExportedView(data, false, 1, 0, data.Length(), 1);

        // Only resizable exporters need CLR cleanup. Immutable byte views retain
        // their ordinary allocation/collection path. This fallback does not
        // promise CPython's immediate last-Python-reference release timing.
        private sealed class ExportedView : PyMemoryView
        {
            internal ExportedView(PyByteArray data, bool readOnly, int itemsize,
                int offset, int length, nint stride, string fmt = "B") : base(data, readOnly, itemsize, offset, length, stride, fmt) { }
            internal ExportedView(PyMemoryView source, PySlice slice) : base(source, slice) { }
            ~ExportedView() { ReleaseExport(false); }
        }

        private PyMemoryView(object data, bool readOnly, int itemsize,
            int offset, int length, nint stride, string fmt = "B")
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            _bufferState = readOnly ? 1u : 0u;
            _itemsize = itemsize;  // CPython: Py_buffer.itemsize
            _fmt = fmt;
            _offset = offset;
            _length = length;
            _stride = stride;
            if (data is PyByteArray exporter) exporter.AcquireBufferExport();
        }

        private void CheckReleased()
        {
            if (_data == null)
                throw PyValueError.Create("operation forbidden on released memoryview object");
        }

        private PyMemoryView(PyMemoryView source, PySlice slice)
            : this(source._data, source._readonly, source._itemsize,
                   source._offset, source._length, source._stride, source._fmt)
        {
            // CPython registers the child before slice __index__ callbacks run.
            // Such callbacks may release the parent; the child still owns its export.
            try
            {
                var (start, stop, step) = slice.ViewIndices(_length);
                _length = PySlice.GetLength(start, stop, int.CreateSaturating(step));
                // An empty view can have an out-of-storage logical offset, but
                // cannot dereference it or yield a nonempty child.
                _offset = unchecked((int)(_offset + start * _stride));
                _stride = unchecked(_stride * step);
            }
            catch
            {
                Release();
                throw;
            }
        }

        private byte ReadByte(int position) => _data is PyByteArray exporter
            ? exporter.ReadBufferByte(position) : ((byte[])_data)[position];

        // CPython memoryview.cast element decoding. Native (little-endian on the
        // supported hosts) struct interpretation of the raw bytes.
        private static int FormatSize(char f) => f switch
        {
            'b' or 'B' or 'c' => 1,
            'h' or 'H' => 2,
            'i' or 'I' or 'l' or 'L' => 4,
            'q' or 'Q' => 8,
            'f' => 4,
            'd' => 8,
            _ => -1,
        };

        private PyObject ReadElement(int storageByte)
        {
            byte b0 = ReadByte(storageByte);
            switch (_fmt)
            {
                case "":
                case "B": return SmallIntCache.GetOrCreate(b0);
                case "b": return SmallIntCache.GetOrCreate((sbyte)b0);
                case "c": return new PyBytes(new[] { b0 });
                case "H": return SmallIntCache.GetOrCreate((uint)(b0 | (ReadByte(storageByte + 1) << 8)));
                case "h": return SmallIntCache.GetOrCreate((short)(b0 | (ReadByte(storageByte + 1) << 8)));
                case "I":
                case "L": return SmallIntCache.GetOrCreate((uint)(b0 | (ReadByte(storageByte + 1) << 8) | (ReadByte(storageByte + 2) << 16) | (ReadByte(storageByte + 3) << 24)));
                case "i":
                case "l": return SmallIntCache.GetOrCreate(b0 | (ReadByte(storageByte + 1) << 8) | (ReadByte(storageByte + 2) << 16) | (ReadByte(storageByte + 3) << 24));
                case "Q": { ulong v = 0; for (int k = 0; k < 8; k++) v |= (ulong)ReadByte(storageByte + k) << (8 * k); return new PyInt(unchecked((long)v)); }
                case "q": { long v = 0; for (int k = 0; k < 8; k++) v |= (long)ReadByte(storageByte + k) << (8 * k); return new PyInt(v); }
                case "f": { uint u = 0; for (int k = 0; k < 4; k++) u |= (uint)ReadByte(storageByte + k) << (8 * k); return new PyFloat(BitConverter.ToSingle(BitConverter.GetBytes(u), 0)); }
                case "d": { ulong u = 0; for (int k = 0; k < 8; k++) u |= (ulong)ReadByte(storageByte + k) << (8 * k); return new PyFloat(BitConverter.ToDouble(BitConverter.GetBytes(u), 0)); }
                default: return SmallIntCache.GetOrCreate(b0);
            }
        }

        // Build a new C-contiguous view over the same storage interpreted as
        // `itemsize`-wide elements (element i starts at _offset + i*itemsize).
        internal PyMemoryView Reinterpret(string fmt, int itemsize)
        {
            int total = _length * _itemsize;
            int elems = total / itemsize;
            return _data is PyByteArray e
                ? new ExportedView(e, _readonly, itemsize, _offset, elems, itemsize, fmt)
                : new PyMemoryView(_data, _readonly, itemsize, _offset, elems, itemsize, fmt);
        }

        private int GetIndex(PyInt index, PyObject original = null)
        {
            // CPython converts an index to Py_ssize_t before adjusting negatives.
            long position;
            try { position = (long)index.Value; }
            catch (OverflowException)
            {
                throw PyIndexError.Create($"cannot fit '{(original ?? index).GetTypeName()}' into an index-sized integer");
            }
            if (position < 0) position += _length;
            if ((ulong)position >= (ulong)_length)
                throw PyIndexError.Create("index out of bounds on dimension 1");
            return StorageIndex((int)position);
        }

        public void Release()
        {
            uint exports = _bufferState >> 1;
            if (exports != 0)
                throw PyBufferError.Create($"memoryview has {exports} exported buffer{(exports == 1 ? "" : "s")}");
            ReleaseExport(true);
        }

        private void ReleaseExport(bool suppressFinalization)
        {
            var data = _data;
            _data = null;
            if (data is PyByteArray exporter)
            {
                exporter.ReleaseBufferExport();
                if (suppressFinalization) GC.SuppressFinalize(this);
            }
        }

        public override bool SupportsBuffer() => true;
        public override PyMemoryView GetBuffer(int flags)
        {
            CheckReleased();
            return _data is PyByteArray exporter
                ? new ExportedView(exporter, _readonly, _itemsize, _offset, _length, _stride, _fmt)
                : new PyMemoryView(_data, _readonly, _itemsize, _offset, _length, _stride, _fmt);
        }

        public byte[] Data
        {
            get
            {
                CheckReleased();
                if (_itemsize == 1 && _data is byte[] array && _offset == 0 && _stride == 1 && _length == array.Length)
                    return array;
                int total = _length * _itemsize;
                var result = new byte[total];
                for (int i = 0; i < _length; i++)
                    for (int j = 0; j < _itemsize; j++)
                        result[i * _itemsize + j] = ReadByte(StorageIndex(i) + j);
                return result;
            }
        }
        public bool ReadOnly { get { CheckReleased(); return _readonly; } }
        public int ItemSize { get { CheckReleased(); return _itemsize; } }

        public override PyType GetPyType() => PyType.MemoryViewType;
        public override string GetTypeName() => "memoryview";

        /// <summary>
        /// CPython: PyGetSetDef memory_getsetlist - expose C# properties as Python attributes
        /// Objects/memoryobject.c:memory_itemsize_get
        /// </summary>
        public override PyObject GetAttribute(string name)
        {
            if (name == "strides")
            {
                CheckReleased();
                return new PyTuple(SmallIntCache.GetOrCreate((long)_stride));
            }
            if (name == "itemsize")
            {
                return SmallIntCache.GetOrCreate(ItemSize);
            }
            if (name == "format") { CheckReleased(); return new PyString(_fmt); }
            if (name == "nbytes") { CheckReleased(); return SmallIntCache.GetOrCreate((long)_length * _itemsize); }
            if (name == "ndim") { CheckReleased(); return SmallIntCache.GetOrCreate(1); }
            if (name == "shape") { CheckReleased(); return new PyTuple(new PyObject[] { SmallIntCache.GetOrCreate(_length) }); }
            return base.GetAttribute(name);
        }

        /// <summary>
        /// CPython 3.12: Objects/memoryobject.c:2547-2606 - memory_subscript
        /// Supports both integer indexing and slicing
        /// </summary>
        public override PyObject GetItem(PyObject index)
        {
            CheckReleased();
            // CPython 3.12: line 2571-2576 - integer indexing
            if (index is PyInt pyInt)
            {
                return ReadElement(GetIndex(pyInt));
            }
            // CPython 3.12: line 2578-2593 - slice support
            else if (index is PySlice slice)
            {
                return _data is PyByteArray ? new ExportedView(this, slice) : new PyMemoryView(this, slice);
            }
            var converted = TryGetIndex(index);
            if (converted == null) throw PyTypeError.Create("memoryview: invalid slice key");
            int position = GetIndex(converted, index);
            CheckReleased();
            return ReadElement(position);
        }

        public override void SetItem(PyObject index, PyObject value)
        {
            CheckReleased();
            if (_readonly)
                throw PyTypeError.Create("cannot modify read-only memory");

            var pyInt = index as PyInt;
            if (pyInt == null)
            {
                if (index is PySlice slice)
                {
                    SetSlice(slice, value);
                    return;
                }
                pyInt = TryGetIndex(index);
            }
            if (pyInt != null)
            {
                int position = GetIndex(pyInt, index);
                ulong byteValue = ConvertByteValue(value);
                CheckReleased();

                if (byteValue > 255)
                    throw PyValueError.Create("memoryview: invalid value for format 'B'");

                if (_data is PyByteArray exporter) exporter.WriteBufferByte(position, (byte)byteValue);
                else ((byte[])_data)[position] = (byte)byteValue;
                return;
            }
            throw PyTypeError.Create("memoryview: invalid slice key");
        }

        // Valid logical indices of a nonempty view always map into int-sized storage.
        private int StorageIndex(int position) => unchecked((int)(_offset + position * _stride));

        private Span<byte> StorageSpan() => _data is PyByteArray exporter
            ? exporter.BufferSpan : (byte[])_data;

        // Synchronous native IO only: no Python callbacks while this span is used.
        internal ReadOnlySpan<byte> ContiguousSpan()
        {
            CheckReleased();
            if (_length > 1 && _stride != 1)
                throw PyBufferError.Create("memoryview: underlying buffer is not C-contiguous");
            return _length == 0 ? ReadOnlySpan<byte>.Empty : StorageSpan().Slice(_offset, _length);
        }

        private void SetSlice(PySlice slice, PyObject value)
        {
            // memory_ass_sub acquires the source before invoking slice callbacks.
            // Borrowing a view prevents release(), unlike creating a child view.
            bool borrowed = value is PyMemoryView;
            var source = borrowed ? (PyMemoryView)value : value.GetBuffer(0);
            source.CheckReleased();
            source._bufferState += 2;
            try
            {
                var (start, stop, step) = slice.ViewIndices(_length);
                int length = PySlice.GetLength(start, stop, int.CreateSaturating(step));
                CheckReleased();
                if (length != source._length || _itemsize != source._itemsize)
                    throw PyValueError.Create("memoryview assignment: lvalue and rvalue have different structures");
                if (length == 0) return;

                nint offset = unchecked(_offset + start * _stride);
                nint stride = unchecked(_stride * step);
                // All Python callbacks have completed before taking storage spans.
                var destination = StorageSpan();
                var input = source.StorageSpan();
                if (stride == 1 && source._stride == 1)
                {
                    input.Slice(source._offset, length).CopyTo(destination.Slice(unchecked((int)offset), length));
                    return;
                }
                if (ReferenceEquals(_data, source._data))
                {
                    // A strided overlapping assignment must read every source byte
                    // before writing any destination byte (CPython copy_single).
                    var snapshot = new byte[length];
                    for (int i = 0; i < length; i++) snapshot[i] = input[source.StorageIndex(i)];
                    for (int i = 0; i < length; i++) destination[unchecked((int)(offset + i * stride))] = snapshot[i];
                }
                else
                    for (int i = 0; i < length; i++)
                        destination[unchecked((int)(offset + i * stride))] = input[source.StorageIndex(i)];
            }
            finally
            {
                source._bufferState -= 2;
                if (!borrowed) source.Release();
                GC.KeepAlive(this);
            }
        }

        private static ulong ConvertByteValue(PyObject value)
        {
            try
            {
                var integer = value as PyInt ?? TryGetIndex(value);
                if (integer == null) throw PyTypeError.Create("memoryview: invalid type for format 'B'");
                // pack_single converts to C unsigned long before checking release
                // and the byte range (32 bits on Windows, 64 on Unix x64).
                return OperatingSystem.IsWindows() ? (ulong)(uint)integer.Value : (ulong)integer.Value;
            }
            catch (PythonException ex) when (ex.PyException is PyTypeError)
            {
                throw PyTypeError.Create("memoryview: invalid type for format 'B'");
            }
            catch (PythonException ex) when (ex.PyException is PyValueError || ex.PyException is PyOverflowError)
            {
                throw PyValueError.Create("memoryview: invalid value for format 'B'");
            }
            catch (OverflowException)
            {
                throw PyValueError.Create("memoryview: invalid value for format 'B'");
            }
        }

        public override int Length() { CheckReleased(); return _length; }

        public override PyObject GetIterator()
        {
            CheckReleased();
            int n = _length;
            var items = new PyObject[n];
            for (int i = 0; i < n; i++)
                items[i] = ReadElement(StorageIndex(i));
            return new PyList(items).GetIterator();
        }
        public override bool IsTrue() => Length() != 0;
        public override bool PyBoolValue() => IsTrue();

        public override string ToString() => $"<{(_data == null ? "released memory" : "memory")} at 0x{GetHashCode():X}>";
        public override PyString ToRepr() => new PyString(ToString());
    }
}
