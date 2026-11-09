using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// An exporter whose memory can move or be resized while no view holds it (bytearray, the
    /// buffer of io.BytesIO): bf_getbuffer counts a view in, bf_releasebuffer counts it out, and
    /// the views read the memory through the exporter.
    /// </summary>
    public interface IBufferExporter
    {
        void AcquireBufferExport();
        void ReleaseBufferExport();
        Span<byte> BufferSpan { get; }
    }

    internal static class BufferExports
    {
        /// <summary>
        /// Whether views still hold the exporter. CPython releases a view the moment its last
        /// reference goes; here an unreachable view is released by its finalizer, so before an
        /// exporter refuses, the collector finishes the views nothing can reach any more.
        /// </summary>
        internal static bool Held(Func<bool> exported)
        {
            if (!exported())
                return false;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            return exported();
        }
    }

    /// <summary>
    /// MemoryView implementation for buffer protocol
    /// Represents a view into buffer data
    /// </summary>
    public class PyMemoryView : PyObject
    {
        /// <summary>PyBUF_FULL_RO, what memoryview() and bytes() ask a buffer for.</summary>
        internal const int FullReadOnly = 0x11C;

        /// <summary>
        /// slot_bf_getbuffer (Objects/typeobject.c): a class written in Python exports a buffer
        /// through __buffer__(flags), found on the type, which must answer a memoryview. Null when
        /// the type has no __buffer__.
        /// </summary>
        internal static PyMemoryView FromBufferSlot(PyObject value, int flags)
        {
            if (value.LookupSpecialMethodUncached("__buffer__") is not PyObject method)
                return null;
            return value.BindSpecialMethod(method).Call(new PyObject[] { PyInt.From(flags) }, null) as PyMemoryView
                ?? throw PyTypeError.Create("__buffer__ returned non-memoryview object");
        }

        // Either the exporting bytearray or an immutable-size byte[] buffer.
        // A null reference marks a released view and drops the backing storage.
        private object _data;
        private readonly int _itemsize;
        private readonly string _fmt;   // CPython Py_buffer.format ("" for byte views)
        // Physical storage is int-sized; only stride is exposed at native width.
        private readonly int _offset;
        private readonly int _length;
        private readonly nint _stride;
        // p42: cast(fmt, shape) adds CPython's ndim/shape/strides on top of the flat
        // storage model. Row-major element strides; null keeps the ordinary 1-D view.
        private readonly int[] _shape;
        private readonly long[] _estr;
        // memoryview.obj reports the Python object whose buffer is exported.
        private readonly PyObject _exportee;
        // Bit 0 is readonly; the upper 31 bits hold the borrowed-export count.
        // Sharing this slot avoids growing each view for the native-width stride.
        private uint _bufferState;
        private bool _readonly => (_bufferState & 1) != 0;

        static PyMemoryView()
        {
            var type = PyType.MemoryViewType;

            // CPython 3.12: memoryview.tobytes()/hex()/tolist(); bytes(view) is the buffer protocol, there is no __bytes__
            type.TypeDict["tobytes"] = new PyMethodDescriptor("tobytes", type, (self, args, kwargs) =>
            {
                var view = (PyMemoryView)self;
                // memoryview.tobytes(order='C'): the clinic's arguments, then CHECK_RELEASED
                var order = PyArgs.Unpack("tobytes", args, kwargs, new[] { "order" }, 0, 0, 1)[0];
                if (order != null && order is not PyNone)
                {
                    if (order is not PyString orderText)
                        throw PyArgs.BadArgument("tobytes", "argument 'order'", "str or None", order);
                    if (orderText.Value is not ("C" or "F" or "A"))
                        throw PyValueError.Create("order must be 'C', 'F' or 'A'");
                }
                view.CheckReleased();
                return PyBytes.From(view.Data);
            });
            // memoryview_toreadonly
            type.TypeDict["toreadonly"] = new PyMethodDescriptor("toreadonly", type, (self, args, kwargs) =>
            {
                var view = (PyMemoryView)self;
                view.CheckReleased();
                var copy = new PyMemoryView(view, new PySlice(PyNone.Instance, PyNone.Instance, PyNone.Instance));
                copy._bufferState |= 1;
                return copy;
            }, 0, 0);
            type.TypeDict["tolist"] = new PyMethodDescriptor("tolist", type, (self, args, kwargs) =>
            {
                var view = (PyMemoryView)self;
                if (args.Length != 0 || (kwargs != null && kwargs.Length() != 0))
                    throw PyTypeError.Create($"tolist() takes no arguments ({args.Length} given)");
                view.CheckReleased();
                return view.ToNestedList();
            });
            type.TypeDict["hex"] = new PyMethodDescriptor("hex", type, (self, args, kwargs) =>
            {
                var view = (PyMemoryView)self;
                // memoryview.hex(sep=<unrepresentable>, bytes_per_sep=1): the clinic, then CHECK_RELEASED
                PyArgs.Unpack("hex", args, kwargs, new[] { "sep", "bytes_per_sep" }, 0, 0, 2);
                view.CheckReleased();
                return BytesMethods.HexMethod(view.Data, args, kwargs);
            }, acceptsKwargs: true);

            foreach (var name in new[] { "release", "__enter__", "__exit__" })
                type.TypeDict[name] = new PyMethodDescriptor(name, type, (self, args, kwargs) => {
                    var view = (PyMemoryView)self;
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
                var view = (PyMemoryView)self;

                // CPython 3.12 Objects/memoryobject.c memory_cast is
                // METH_FASTCALL|METH_KEYWORDS with parameter names "format" and "shape".
                PyObject fmtArg = args.Length > 0 ? args[0] : null;
                PyObject shapeArg = args.Length > 1 ? args[1] : null;
                int extra = 0;
                if (kwargs != null && kwargs.InternalDict.Count > 0)
                {
                    foreach (var kv in kwargs.InternalDict)
                    {
                        string key = kv.Key.ToClrString();
                        if (key == "format")
                        {
                            if (args.Length > 0)
                                throw PyTypeError.Create("argument for cast() given by name ('format') and position (1)");
                            fmtArg = kv.Value;
                        }
                        else if (key == "shape")
                        {
                            if (args.Length > 1)
                                throw PyTypeError.Create("argument for cast() given by name ('shape') and position (2)");
                            shapeArg = kv.Value;
                        }
                        else extra++;
                    }
                }
                if (args.Length + extra > 2)
                    throw PyTypeError.Create($"cast() takes at most 2 arguments ({args.Length + extra} given)");
                if (fmtArg == null)
                    throw PyArgs.MissingArgument("cast", "format", 1);
                if (fmtArg is not PyString fmtStr)
                    // CPython names the None type "None" in this message and uses the
                    // ordinary tp_name for everything else.
                    throw PyArgs.BadArgument("cast", "argument 'format'", "str", fmtArg);

                // memoryview_cast_impl
                view.CheckReleased();
                if (!view.IsCContiguous)
                    throw PyTypeError.Create("memoryview: casts are restricted to C-contiguous views");
                int[] sourceShape = view._shape ?? new[] { view._length };
                if ((shapeArg != null || view.NDim != 1) && Array.IndexOf(sourceShape, 0) >= 0)
                    throw PyTypeError.Create("memoryview: cannot cast view with zeros in shape or strides");
                PyObject[] dims = null;
                int ndim = 1;
                if (shapeArg != null)
                {
                    // CHECK_LIST_OR_TUPLE(shape)
                    if (shapeArg is PyTuple t) dims = t.Items;
                    else if (shapeArg is PyList lst) dims = lst.Items;
                    else throw PyTypeError.Create("shape must be a list or a tuple");
                    ndim = dims.Length;
                    if (ndim > 64)
                        throw PyValueError.Create("memoryview: number of dimensions must not exceed 64");
                    if (view.NDim != 1 && ndim != 1)
                        throw PyTypeError.Create("memoryview: cast must be 1D -> ND or ND -> 1D");
                }

                // cast_to_1D: one native character after an optional '@' (get_native_fmtchar),
                // and one side of the cast a byte format.
                // PyUnicode_AsASCIIString(format)
                fmtStr.GetAttribute("encode").Call(new PyObject[] { PyString.From("ascii") }, null);
                string fs = fmtStr.Value;
                if (fs.Length == 2 && fs[0] == '@') fs = fs.Substring(1);
                int isz = fs.Length == 1 ? FormatSize(fs[0]) : -1;
                if (isz < 0)
                    throw PyValueError.Create(FormatError);
                char f = fs[0];
                static bool IsByteFormat(char c) => c == 'b' || c == 'B' || c == 'c';
                string source = string.IsNullOrEmpty(view._fmt) ? "B" : view._fmt;
                if (source.Length == 2 && source[0] == '@') source = source.Substring(1);
                bool sourceIsByte = source.Length == 1 && FormatSize(source[0]) >= 0 && IsByteFormat(source[0]);
                if (!sourceIsByte && !IsByteFormat(f))
                    throw PyTypeError.Create("memoryview: cannot cast between two non-byte formats");
                long totalBytes = (long)view._length * view._itemsize;
                if (totalBytes % isz != 0)
                    throw PyTypeError.Create("memoryview: length is not a multiple of itemsize");

                // cast_to_ND, through copy_shape
                int[] shape = null;
                if (dims != null)
                {
                    shape = new int[ndim];
                    long len = isz;
                    for (int k = 0; k < ndim; k++)
                    {
                        // PyLong_Check, which a bool passes, then PyLong_AsSsize_t
                        System.Numerics.BigInteger value = dims[k] switch
                        {
                            PyBool truth => truth.Value ? 1 : 0,
                            PyInt dim => dim.Value,
                            _ => throw PyTypeError.Create("memoryview.cast(): elements of shape must be integers"),
                        };
                        if (value > long.MaxValue || value < long.MinValue)
                            throw PyOverflowError.Create("Python int too large to convert to C ssize_t");
                        long x = (long)value;
                        if (x <= 0)
                            throw PyValueError.Create("memoryview.cast(): elements of shape must be integers > 0");
                        if (x > long.MaxValue / len)
                            throw PyValueError.Create("memoryview.cast(): product(shape) > SSIZE_MAX");
                        len *= x;
                        shape[k] = (int)Math.Min(x, int.MaxValue);
                    }
                    if (totalBytes != len)
                        throw PyTypeError.Create("memoryview: product(shape) * itemsize != buffer size");
                }
                // get_native_fmtstr: the format as written, '@' and all.
                return view.Reinterpret(fmtStr.Value, isz, shape);
            }, acceptsKwargs: true);

            // p42: CPython exposes these as slot wrappers on the type, so they are
            // visible through dir()/getattr and callable in unbound form.
            type.TypeDict["__getitem__"] = new PyWrapperDescriptor(
                "__getitem__", type,
                (self, args, kwargs) => {
                    var view = (PyMemoryView)self;
                    if (args.Length != 1) throw PyTypeError.Create($"expected 1 argument, got {args.Length}");
                    return view.MpSubscript(args[0]);
                }, minArgs: 1, maxArgs: 1);
            type.TypeDict["__setitem__"] = new PyWrapperDescriptor(
                "__setitem__", type,
                (self, args, kwargs) => {
                    var view = (PyMemoryView)self;
                    // CPython's METH_VARARGS "OO" slot wrapper emits a leading space.
                    if (args.Length != 2) throw PyTypeError.Create($" expected 2 arguments, got {args.Length}");
                    view.MpAssSubscript(args[0], args[1]);
                    return PyNone.Instance;
                }, minArgs: 2, maxArgs: 2);
            type.TypeDict["__len__"] = new PyWrapperDescriptor(
                "__len__", type,
                (self, args, kwargs) => {
                    var view = (PyMemoryView)self;
                    if (args.Length != 0) throw PyTypeError.Create($"expected 0 arguments, got {args.Length}");
                    return PyInt.From((int)view.Len());
                }, minArgs: 0, maxArgs: 0);

            // CPython memoryview is iterable and yields one element per position
            // (honouring the current cast format/itemsize).
            // tp_iter is memory_iter: the slot wrapper calls it.
            type.TypeDict["__iter__"] = new PyWrapperDescriptor("__iter__", type, (self, args, kwargs) =>
            {
                var view = (PyMemoryView)self;
                if (args.Length != 0 || (kwargs != null && kwargs.Length() != 0))
                    throw PyTypeError.Create($"expected 0 arguments, got {args.Length}");
                return view.Iter();
            });
        }

        public PyMemoryView(byte[] data, bool readOnly = false, int itemsize = 1, PyObject exportee = null)
            : this(data, readOnly, itemsize, 0, data?.Length ?? 0, 1, "B", null, exportee) { }

        internal static PyMemoryView FromByteArray(PyByteArray data) =>
            new ExportedView(data, false, 1, 0, data.Length(), 1);

        // PyMemoryView_FromObject over an exporter: a writable byte view of length bytes.
        internal static PyMemoryView FromExporter(IBufferExporter exporter, int length) =>
            new ExportedView(exporter, false, 1, 0, length, 1);

        // Only resizable exporters need CLR cleanup. Immutable byte views retain
        // their ordinary allocation/collection path. This fallback does not
        // promise CPython's immediate last-Python-reference release timing.
        private sealed class ExportedView : PyMemoryView
        {
            internal ExportedView(IBufferExporter data, bool readOnly, int itemsize,
                int offset, int length, nint stride, string fmt = "B", int[] shape = null)
                : base(data, readOnly, itemsize, offset, length, stride, fmt, shape, (PyObject)data) { }
            internal ExportedView(PyMemoryView source, PySlice slice) : base(source, slice) { }
            ~ExportedView() { ReleaseExport(false); }
        }

        private PyMemoryView(object data, bool readOnly, int itemsize,
            int offset, int length, nint stride, string fmt = "B", int[] shape = null,
            PyObject exportee = null)
        {
            _exportee = exportee ?? (data as IBufferExporter as PyObject);
            if (shape != null)
            {
                // A 1-element shape is CPython's ordinary 1-D view; drop it so the fast
                // paths stay in charge. Longer shapes get row-major element strides.
                if (shape.Length == 1)
                {
                    if (shape[0] != length)
                        throw PyTypeError.Create("memoryview: product(shape) * itemsize != buffer size");
                }
                else if (shape.Length == 0)
                {
                    // a scalar: one item, no axes
                    if (length != 1)
                        throw PyTypeError.Create("memoryview: product(shape) * itemsize != buffer size");
                    _estr = Array.Empty<long>();
                    _shape = shape;
                }
                else
                {
                    long product = 1;
                    foreach (var d in shape) product *= d;
                    if (product != length)
                        throw PyTypeError.Create("memoryview: product(shape) * itemsize != buffer size");
                    _estr = new long[shape.Length];
                    _estr[shape.Length - 1] = 1;
                    for (int k = shape.Length - 2; k >= 0; k--)
                        _estr[k] = _estr[k + 1] * shape[k + 1];
                    _shape = shape;
                }
            }
            _data = data ?? throw new ArgumentNullException(nameof(data));
            _bufferState = readOnly ? 1u : 0u;
            _itemsize = itemsize;  // CPython: Py_buffer.itemsize
            _fmt = fmt;
            _offset = offset;
            _length = length;
            _stride = stride;
            if (data is IBufferExporter exporter) exporter.AcquireBufferExport();
        }

        // view.obj, and whether release() has run (_Py_MEMORYVIEW_RELEASED).
        internal PyObject Exportee => _exportee;
        internal bool IsReleased => _data == null;

        private void CheckReleased()
        {
            if (_data == null)
                throw PyValueError.Create("operation forbidden on released memoryview object");
        }

        private PyMemoryView(PyMemoryView source, PySlice slice)
            : this(source._data, source._readonly, source._itemsize,
                   source._offset, source._length, source._stride, source._fmt, source._shape,
                   source._exportee)
        {
            // CPython registers the child before slice __index__ callbacks run.
            // Such callbacks may release the parent; the child still owns its export.
            try
            {
                // A shaped view slices its first axis; the remaining axes and their
                // element strides are inherited (CPython keeps Py_buffer.strides).
                int axis0 = _shape == null ? _length : _shape[0];
                var (start, stop, step) = slice.ViewIndices(axis0);
                int taken = PySlice.GetLength(start, stop, int.CreateSaturating(step));
                if (_shape != null)
                {
                    long axisStride = _estr[0];
                    var ns = (int[])_shape.Clone();
                    ns[0] = taken;
                    var ne = (long[])_estr.Clone();
                    ne[0] = axisStride * int.CreateSaturating(step);
                    _shape = ns;
                    _estr = ne;
                    long product = 1;
                    foreach (var d in ns) product *= d;
                    _length = (int)product;
                    _offset = unchecked((int)(_offset + start * _stride * axisStride));
                }
                else
                {
                    _length = taken;
                    _offset = unchecked((int)(_offset + start * _stride));
                }
                _stride = unchecked(_stride * (_shape == null ? step : 1));
            }
            catch
            {
                Release();
                throw;
            }
        }

        private byte ReadByte(int position) => _data is IBufferExporter exporter
            ? exporter.BufferSpan[position] : ((byte[])_data)[position];

        // CPython memoryview.cast element decoding. Native (little-endian on the
        // supported hosts) struct interpretation of the raw bytes.
        // CPython memory_cast rejects every other byte-order prefix with this text.
        internal const string FormatError =
            "memoryview: destination format must be a native single character format prefixed with an optional '@'";

        private static int FormatSize(char f) => f switch
        {
            'b' or 'B' or 'c' => 1,
            'h' or 'H' => 2,
            'i' or 'I' or 'l' or 'L' => 4,
            'q' or 'Q' => 8,
            'f' => 4,
            'd' => 8,
            '?' => 1,
            'e' => 2,
            'n' or 'N' or 'P' => 8,
            _ => -1,
        };

        private PyObject ReadElement(int storageByte) => UnpackSingle(storageByte, AdjustFmt());

        // adjust_fmt: one format character after an optional '@' (a buffer without a format is 'B').
        private string AdjustFmt()
        {
            string format = string.IsNullOrEmpty(_fmt) ? "B" : _fmt;
            string fmt = format[0] == '@' ? format.Substring(1) : format;
            if (fmt.Length == 1)
                return fmt;
            throw PyNotImplementedError.Create($"memoryview: unsupported format {format}");
        }

        private int NDim => _shape == null ? 1 : _shape.Length;

        // unpack_single: the item at storageByte in a native format.
        private PyObject UnpackSingle(int at, string fmt)
        {
            CheckReleased();
            ReadOnlySpan<byte> item = StorageSpan().Slice(at);
            switch (fmt[0])
            {
                case 'B': return PyInt.From(item[0]);
                case 'b': return PyInt.From((sbyte)item[0]);
                case 'h': return PyInt.From(BinaryPrimitives.ReadInt16LittleEndian(item));
                case 'i': case 'l': return PyInt.From(BinaryPrimitives.ReadInt32LittleEndian(item));
                case '?': return PyBool.FromBool(item[0] != 0);
                case 'H': return PyInt.From(BinaryPrimitives.ReadUInt16LittleEndian(item));
                case 'I': case 'L': return PyInt.From(BinaryPrimitives.ReadUInt32LittleEndian(item));
                case 'q': case 'n': return PyInt.From(BinaryPrimitives.ReadInt64LittleEndian(item));
                case 'Q': case 'N': case 'P': return PyInt.From((System.Numerics.BigInteger)BinaryPrimitives.ReadUInt64LittleEndian(item));
                case 'f': return new PyFloat(BinaryPrimitives.ReadSingleLittleEndian(item));
                case 'd': return new PyFloat(BinaryPrimitives.ReadDoubleLittleEndian(item));
                case 'e': return new PyFloat((double)BinaryPrimitives.ReadHalfLittleEndian(item));
                case 'c': return PyBytes.From(new[] { item[0] });
                default: throw PyNotImplementedError.Create($"memoryview: format {fmt} not supported");
            }
        }

        private static Exception InvalidType(string fmt) => PyTypeError.Create($"memoryview: invalid type for format '{fmt}'");
        private static Exception InvalidValue(string fmt) => PyValueError.Create($"memoryview: invalid value for format '{fmt}'");

        // _PyNumber_Index and PyLong_As*: fix_error_int makes a TypeError the format's type error
        // and an OverflowError or ValueError its value error; anything else stays as it was.
        private static System.Numerics.BigInteger IndexValue(PyObject item, string fmt, System.Numerics.BigInteger min, System.Numerics.BigInteger max)
        {
            PyInt index;
            try
            {
                index = PyObject.TryGetIndex(item);
            }
            catch (PythonException error) when (error.PyException is PyTypeError)
            {
                throw InvalidType(fmt);
            }
            catch (PythonException error) when (error.PyException is PyValueError or PyOverflowError)
            {
                throw InvalidValue(fmt);
            }
            if (index == null)
                throw InvalidType(fmt);
            if (index.Value < min || index.Value > max)
                throw InvalidValue(fmt);
            return index.Value;
        }

        // pack_single: item stored at storageByte in a native format.
        private void PackSingle(int at, PyObject item, string fmt)
        {
            switch (fmt[0])
            {
                case 'b': case 'h': case 'i': case 'l':
                {
                    // PyLong_AsLong: a C long, 32 bits here; then the range of the format.
                    long ld = (long)IndexValue(item, fmt, int.MinValue, int.MaxValue);
                    CheckReleased();
                    switch (fmt[0])
                    {
                        case 'b':
                            if (ld < sbyte.MinValue || ld > sbyte.MaxValue) throw InvalidValue(fmt);
                            StorageSpan()[at] = unchecked((byte)(sbyte)ld);
                            break;
                        case 'h':
                            if (ld < short.MinValue || ld > short.MaxValue) throw InvalidValue(fmt);
                            BinaryPrimitives.WriteInt16LittleEndian(StorageSpan().Slice(at), (short)ld);
                            break;
                        default:
                            BinaryPrimitives.WriteInt32LittleEndian(StorageSpan().Slice(at), (int)ld);
                            break;
                    }
                    break;
                }
                case 'B': case 'H': case 'I': case 'L':
                {
                    // PyLong_AsUnsignedLong: a C unsigned long, 32 bits here.
                    ulong lu = (ulong)IndexValue(item, fmt, 0, uint.MaxValue);
                    CheckReleased();
                    switch (fmt[0])
                    {
                        case 'B':
                            if (lu > byte.MaxValue) throw InvalidValue(fmt);
                            StorageSpan()[at] = (byte)lu;
                            break;
                        case 'H':
                            if (lu > ushort.MaxValue) throw InvalidValue(fmt);
                            BinaryPrimitives.WriteUInt16LittleEndian(StorageSpan().Slice(at), (ushort)lu);
                            break;
                        default:
                            BinaryPrimitives.WriteUInt32LittleEndian(StorageSpan().Slice(at), (uint)lu);
                            break;
                    }
                    break;
                }
                case 'q': case 'n':
                {
                    long value = (long)IndexValue(item, fmt, long.MinValue, long.MaxValue);
                    CheckReleased();
                    BinaryPrimitives.WriteInt64LittleEndian(StorageSpan().Slice(at), value);
                    break;
                }
                case 'Q': case 'N':
                {
                    ulong value = (ulong)IndexValue(item, fmt, 0, ulong.MaxValue);
                    CheckReleased();
                    BinaryPrimitives.WriteUInt64LittleEndian(StorageSpan().Slice(at), value);
                    break;
                }
                case 'P':
                {
                    // PyLong_AsVoidPtr: a negative value as a long long, any other as unsigned.
                    var value = IndexValue(item, fmt, long.MinValue, ulong.MaxValue);
                    CheckReleased();
                    BinaryPrimitives.WriteUInt64LittleEndian(StorageSpan().Slice(at),
                        value < 0 ? unchecked((ulong)(long)value) : (ulong)value);
                    break;
                }
                case 'f': case 'd': case 'e':
                {
                    double d;
                    try
                    {
                        d = SharpPy.Modules.PyMathFunction.RealNumber(item);
                    }
                    catch (PythonException error) when (error.PyException is PyTypeError)
                    {
                        throw InvalidType(fmt);
                    }
                    catch (PythonException error) when (error.PyException is PyValueError or PyOverflowError)
                    {
                        throw InvalidValue(fmt);
                    }
                    CheckReleased();
                    if (fmt[0] == 'f')
                        BinaryPrimitives.WriteSingleLittleEndian(StorageSpan().Slice(at), (float)d);
                    else if (fmt[0] == 'd')
                        BinaryPrimitives.WriteDoubleLittleEndian(StorageSpan().Slice(at), d);
                    else
                    {
                        // PyFloat_Pack2: rounded to the nearest half; one too large for it is an
                        // OverflowError, which fix_error_int makes the value error.
                        var half = (Half)d;
                        if (Half.IsInfinity(half) && !double.IsInfinity(d))
                            throw InvalidValue(fmt);
                        BinaryPrimitives.WriteHalfLittleEndian(StorageSpan().Slice(at), half);
                    }
                    break;
                }
                case '?':
                {
                    bool truth = item.IsTrue();
                    CheckReleased();
                    StorageSpan()[at] = truth ? (byte)1 : (byte)0;
                    break;
                }
                case 'c':
                    if (item is not PyBytes bytes)
                        throw InvalidType(fmt);
                    if (bytes.Value.Length != 1)
                        throw InvalidValue(fmt);
                    StorageSpan()[at] = bytes.Value[0];
                    break;
                default:
                    throw PyNotImplementedError.Create($"memoryview: format {fmt} not supported");
            }
        }

        // Build a new C-contiguous view over the same storage interpreted as
        // `itemsize`-wide elements (element i starts at _offset + i*itemsize).
        internal PyMemoryView Reinterpret(string fmt, int itemsize, int[] shape = null)
        {
            int total = _length * _itemsize;
            int elems = total / itemsize;
            return _data is IBufferExporter e
                ? new ExportedView(e, _readonly, itemsize, _offset, elems, itemsize, fmt, shape)
                : new PyMemoryView(_data, _readonly, itemsize, _offset, elems, itemsize, fmt, shape, _exportee);
        }

        // p42: CPython memory_tolist nests one list per remaining dimension.
        internal PyObject ToNestedList()
        {
            if (NDim == 0)
                return UnpackSingle(StorageIndex(0), AdjustFmt());
            if (_shape == null)
            {
                var flat = new PyObject[_length];
                for (int i = 0; i < _length; i++)
                    flat[i] = ReadElement(StorageIndex(i));
                return new PyList(flat);
            }
            return NestList(0, 0);
        }

        private PyObject NestList(int dim, long baseElem)
        {
            int axis = _shape[dim];
            var items = new PyObject[axis];
            if (dim == _shape.Length - 1)
            {
                for (int i = 0; i < axis; i++)
                    items[i] = ReadElement(StorageIndex((int)(baseElem + i * _estr[dim])));
            }
            else
            {
                for (int i = 0; i < axis; i++)
                    items[i] = NestList(dim + 1, baseElem + i * _estr[dim]);
            }
            return new PyList(items);
        }


        // PyNumber_AsSsize_t(key, PyExc_IndexError)
        private static long IndexOf(PyObject key)
        {
            return PyArgs.AsSsize(key, PyArgs.SsizeError.Index);
        }

        // lookup_dimension: the element offset an index makes along dim.
        private long LookupDimension(int dim, long index)
        {
            long items = _shape == null ? _length : _shape[dim];
            if (index < 0)
                index += items;
            if (index < 0 || index >= items)
                throw PyIndexError.Create($"index out of bounds on dimension {dim + 1}");
            return index * (_shape == null ? 1 : _estr[dim]);
        }

        // ptr_from_tuple: the element a tuple of indices names.
        private int ElementFromTuple(PyTuple key)
        {
            int count = key.Items.Length, ndim = NDim;
            if (count > ndim)
                throw PyTypeError.Create($"cannot index {ndim}-dimension view with {count}-element tuple");
            long element = 0;
            for (int dim = 0; dim < count; dim++)
                element += LookupDimension(dim, IndexOf(key.Items[dim]));
            return (int)element;
        }

        // is_multiindex and is_multislice
        private static bool IsMultiIndex(PyObject key)
        {
            if (key is not PyTuple tuple)
                return false;
            foreach (var item in tuple.Items)
                if (!PyObject.IndexCheck(item))
                    return false;
            return true;
        }

        private static bool IsMultiSlice(PyObject key)
        {
            if (key is not PyTuple tuple || tuple.Items.Length == 0)
                return false;
            foreach (var item in tuple.Items)
                if (item is not PySlice)
                    return false;
            return true;
        }

        private int GetIndex(PyInt index, PyObject original = null)
        {
            // CPython converts an index to Py_ssize_t before adjusting negatives.
            long position = PyArgs.FitSsize(original ?? index, index.Value, PyArgs.SsizeError.Index);
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
            if (data is IBufferExporter exporter)
            {
                exporter.ReleaseBufferExport();
                if (suppressFinalization) GC.SuppressFinalize(this);
            }
        }

        public override bool SupportsBuffer() => true;
        public override PyMemoryView GetBuffer(int flags)
        {
            CheckReleased();
            return _data is IBufferExporter exporter
                ? new ExportedView(exporter, _readonly, _itemsize, _offset, _length, _stride, _fmt, _shape)
                : new PyMemoryView(_data, _readonly, _itemsize, _offset, _length, _stride, _fmt);
        }

        public byte[] Data
        {
            get
            {
                CheckReleased();
                // Only the memory of a bytes object can be handed out without a copy; any other
                // array, read-only view or not, can change under whatever was made from it.
                if (_exportee is PyBytes && _itemsize == 1 && _data is byte[] array && _offset == 0 && _stride == 1 && _length == array.Length)
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
        // CPython 3.12: Objects/floatobject.c PyFloat_FromString exports the buffer and
        // parses it as text; the failure message names the view object.

        // CPython 3.12: Objects/memoryobject.c memory_richcompare.
        // Only EQ and NE exist (ordering stays NotImplemented); a non-memoryview operand has to
        // export a readable buffer and anything else falls back to identity; views whose shapes
        // differ are simply unequal; and items compare by *decoded value* - CPython refuses
        // memcmp even for identical format strings, because NaNs or uninitialized padding would
        // compare equal when they must not.
        protected override PyObject PyEquals(PyObject other)
        {
            if (_data == null)
                // BASE_INACCESSIBLE(v): answer with identity and raise nothing.
                return PyBool.FromBool(ReferenceEquals(this, other));

            PyMemoryView otherView;
            bool acquired = false;
            if (other is PyMemoryView view)
            {
                if (view._data == null)
                    return PyBool.FromBool(ReferenceEquals(this, other));
                otherView = view;
            }
            else
            {
                // PyObject_GetBuffer(w, &wbuf, PyBUF_FULL_RO); an object with no buffer is NotImplemented.
                try
                {
                    otherView = other.GetBuffer(0);
                }
                catch (PythonException error) when (error.PyException is PyTypeError or PyBufferError)
                {
                    return PyNotImplemented.Instance;
                }
                acquired = true;
            }
            try
            {
                if (!EquivalentShape(this, otherView))
                    return PyBool.False;
                var left = LogicalElements();
                var right = otherView.LogicalElements();
                for (int i = 0; i < left.Length; i++)
                {
                    if (!PyObject.CompareBool(left[i], right[i], CompareOp.EQ))
                        return PyBool.False;
                }
                return PyBool.True;
            }
            finally
            {
                // PyBuffer_Release(&wbuf)
                if (acquired)
                    otherView.Release();
            }
        }

        // equiv_shape(): equal ndim and equal element counts per axis. itemsize is not part of
        // the shape, so a 4-byte 'f' view and an 8-byte 'd' view are both shape (1,) and compare
        // as one float each.
        private static bool EquivalentShape(PyMemoryView a, PyMemoryView b)
        {
            int[] sa = a._shape ?? new[] { a._length };
            int[] sb = b._shape ?? new[] { b._length };
            if (sa.Length != sb.Length)
                return false;
            for (int i = 0; i < sa.Length; i++)
            {
                if (sa[i] != sb[i])
                    return false;
                if (sa[i] == 0)
                    break;
            }
            return true;
        }

        // Elements in logical (row-major) order, honouring the byte stride.
        private PyObject[] LogicalElements()
        {
            var items = new PyObject[_length];
            for (int i = 0; i < _length; i++)
                items[i] = ReadElement(StorageIndex(i));
            return items;
        }

        // memory_hash: self->hash, computed once. Released, writable and non-byte formats each
        // raise in that order, and the exporter must be hashable; a byte-format view hashes as the
        // equivalent bytes, so {b"abc": 1}[memoryview(b"abc")] finds the entry. A view released after
        // it was hashed keeps answering.
        private long _hash = -1;
        private int? _clrHash;

        protected internal override long Hash()
        {
            if (_hash == -1)
            {
                CheckReleased();
                if (!_readonly)
                    throw PyValueError.Create("cannot hash writable memoryview object");
                // get_native_fmtchar(): one native character, optionally '@'-prefixed.
                string f = _fmt ?? "";
                if (f.Length == 2 && f[0] == '@')
                    f = f.Substring(1);
                char fmt = f.Length == 1 ? f[0] : '_';
                if (fmt != 'B' && fmt != 'b' && fmt != 'c')
                    throw PyValueError.Create("memoryview: hashing is restricted to formats 'B', 'b' or 'c'");
                // PyObject_Hash(view->obj), keeping its own error
                _exportee?.Hash();
                var bytes = Data;
                _clrHash = PyBytes.From(bytes).ToHash();
                _hash = PyHash.Bytes(bytes);
            }
            return _hash;
        }

        // The CLR hash, the bytes' own, for host containers; the same rules through Hash().
        public override int ToHash()
        {
            Hash();
            return _clrHash.Value;
        }

        public int ItemSize { get { CheckReleased(); return _itemsize; } }

        public override PyType GetPyType() => PyType.MemoryViewType;

        /// <summary>
        /// CPython: PyGetSetDef memory_getsetlist - expose C# properties as Python attributes
        /// Objects/memoryobject.c:memory_itemsize_get
        /// </summary>
        internal PyObject Read(string name)
        {
            if (name == "strides")
            {
                CheckReleased();
                if (_shape == null)
                    return PyTuple.From(PyInt.From((long)_stride));
                var parts = new PyObject[_shape.Length];
                for (int k = 0; k < _shape.Length; k++)
                    parts[k] = PyInt.From(_estr[k] * _stride);
                return PyTuple.From(parts);
            }
            if (name == "c_contiguous" || name == "contiguous")
            {
                CheckReleased();
                // CPython checks PyBuffer_IsContiguous; with an explicit shape the
                // strides stay row-major, so only a non-unit element stride breaks it.
                return _stride == _itemsize ? PyBool.True : PyBool.False;
            }
            if (name == "f_contiguous")
            {
                CheckReleased();
                // Row-major strides read column-major only when at most one dimension is longer than 1.
                int wide = 0;
                foreach (int extent in _shape ?? Array.Empty<int>())
                    if (extent > 1)
                        wide++;
                return _stride == _itemsize && wide <= 1 ? PyBool.True : PyBool.False;
            }
            if (name == "readonly")
            {
                CheckReleased();
                return _readonly ? PyBool.True : PyBool.False;
            }
            if (name == "suboffsets")
            {
                CheckReleased();
                return PyTuple.Empty;
            }
            if (name == "obj")
            {
                CheckReleased();
                return _exportee ?? PyNone.Instance;
            }
            if (name == "itemsize")
            {
                return PyInt.From(ItemSize);
            }
            if (name == "format") { CheckReleased(); return PyString.From(_fmt); }
            if (name == "nbytes") { CheckReleased(); return PyInt.From((long)_length * _itemsize); }
            if (name == "ndim") { CheckReleased(); return PyInt.From(_shape == null ? 1 : _shape.Length); }
            if (name == "shape")
            {
                CheckReleased();
                if (_shape == null)
                    return PyTuple.From(new PyObject[] { PyInt.From(_length) });
                var sh = new PyObject[_shape.Length];
                for (int k = 0; k < _shape.Length; k++) sh[k] = PyInt.From(_shape[k]);
                return PyTuple.From(sh);
            }
            throw PyAttributeError.Create(name);
        }

        /// <summary>memory_subscript (Objects/memoryobject.c)</summary>
        protected internal override PyObject MpSubscript(PyObject key)
        {
            CheckReleased();
            int ndim = NDim;
            if (ndim == 0)
            {
                if (key is PyTuple { Items.Length: 0 })
                    return UnpackSingle(StorageIndex(0), AdjustFmt());
                if (key is PyEllipsis)
                    return this;
                throw PyTypeError.Create("invalid indexing of 0-dim memory");
            }
            if (PyObject.IndexCheck(key))
            {
                // memory_item
                long index = IndexOf(key);
                CheckReleased();
                string fmt = AdjustFmt();
                if (ndim != 1)
                    throw PyNotImplementedError.Create("multi-dimensional sub-views are not implemented");
                return UnpackSingle(StorageIndex((int)LookupDimension(0, index)), fmt);
            }
            if (key is PySlice slice)
                return _data is IBufferExporter ? new ExportedView(this, slice) : new PyMemoryView(this, slice);
            if (IsMultiIndex(key))
            {
                // memory_item_multi
                var tuple = (PyTuple)key;
                CheckReleased();
                string fmt = AdjustFmt();
                if (tuple.Items.Length < ndim)
                    throw PyNotImplementedError.Create("sub-views are not implemented");
                return UnpackSingle(StorageIndex(ElementFromTuple(tuple)), fmt);
            }
            if (IsMultiSlice(key))
                throw PyNotImplementedError.Create("multi-dimensional slicing is not implemented");
            throw PyTypeError.Create("memoryview: invalid slice key");
        }

        /// <summary>memory_ass_sub (Objects/memoryobject.c)</summary>
        protected internal override void MpAssSubscript(PyObject key, PyObject value)
        {
            CheckReleased();
            string fmt = AdjustFmt();
            if (_readonly)
                throw PyTypeError.Create("cannot modify read-only memory");
            if (value == null)
                throw PyTypeError.Create("cannot delete memory");
            int ndim = NDim;
            if (ndim == 0)
            {
                if (key is PyEllipsis || key is PyTuple { Items.Length: 0 })
                {
                    PackSingle(StorageIndex(0), value, fmt);
                    return;
                }
                throw PyTypeError.Create("invalid indexing of 0-dim memory");
            }
            if (PyObject.IndexCheck(key))
            {
                if (ndim > 1)
                    throw PyNotImplementedError.Create("sub-views are not implemented");
                long index = IndexOf(key);
                PackSingle(StorageIndex((int)LookupDimension(0, index)), value, fmt);
                return;
            }
            // one-dimensional: fast path
            if (key is PySlice slice && ndim == 1)
            {
                SetSlice(slice, value);
                return;
            }
            if (IsMultiIndex(key))
            {
                var tuple = (PyTuple)key;
                if (tuple.Items.Length < ndim)
                    throw PyNotImplementedError.Create("sub-views are not implemented");
                PackSingle(StorageIndex(ElementFromTuple(tuple)), value, fmt);
                return;
            }
            if (key is PySlice || IsMultiSlice(key))
                throw PyNotImplementedError.Create("memoryview slice assignments are currently restricted to ndim = 1");
            throw PyTypeError.Create("memoryview: invalid slice key");
        }

        // Valid logical indices of a nonempty view always map into int-sized storage.
        private int StorageIndex(int position) => unchecked((int)(_offset + position * _stride));

        private Span<byte> StorageSpan() => _data is IBufferExporter exporter
            ? exporter.BufferSpan : (byte[])_data;

        // Synchronous native IO only: no Python callbacks while this span is used.

        // PyBuffer_IsContiguous(view, 'C')
        internal bool IsCContiguous
        {
            get
            {
                CheckReleased();
                return _length <= 1 || _stride == _itemsize;
            }
        }

        // Py_buffer.len
        internal long ByteLength
        {
            get
            {
                CheckReleased();
                return (long)_length * _itemsize;
            }
        }

        // The bytes of a C-contiguous view in place, for a native reader that runs no Python code.
        internal ReadOnlySpan<byte> ContiguousBytes()
        {
            if (!IsCContiguous)
                throw PyBufferError.Create("memoryview: underlying buffer is not C-contiguous");
            int n = _length * _itemsize;
            return n == 0 ? ReadOnlySpan<byte>.Empty : StorageSpan().Slice(_offset, n);
        }

        // memcpy into a writable C-contiguous view at a byte offset.
        internal void WriteBytes(long offset, ReadOnlySpan<byte> data)
        {
            if (!IsCContiguous)
                throw PyBufferError.Create("memoryview: underlying buffer is not C-contiguous");
            if (_readonly)
                throw PyTypeError.Create("cannot modify read-only memory");
            data.CopyTo(StorageSpan().Slice(_offset + (int)offset, data.Length));
        }

        // Synchronous native IO only: fills the front of a writable, C-contiguous byte view.
        internal void WriteContiguous(ReadOnlySpan<byte> data)
        {
            CheckReleased();
            if (_readonly)
                throw PyTypeError.Create("cannot modify read-only memory");
            if (_itemsize != 1 || (_length > 1 && _stride != 1))
                throw PyBufferError.Create("memoryview: underlying buffer is not C-contiguous");
            if (data.Length > _length)
                throw PyValueError.Create("memoryview assignment: lvalue and rvalue have different structures");
            data.CopyTo(StorageSpan().Slice(_offset, data.Length));
        }

        // memory_ass_sub's one-dimensional slice: the rvalue is an exporter, the lvalue the sliced
        // view, and copy_single copies when equiv_structure holds.
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
                // copy_single: CHECK_RELEASED_INT_AGAIN, then equiv_structure.
                CheckReleased();
                if (!EquivFormat(this, source) || source.NDim != 1 || source._length != length)
                    throw PyValueError.Create("memoryview assignment: lvalue and rvalue have different structures");
                if (length == 0) return;

                nint offset = unchecked(_offset + start * _stride);
                nint stride = unchecked(_stride * step);
                int itemsize = _itemsize;
                // All Python callbacks have completed before taking storage spans. The source is
                // read whole before anything is written, as memmove and copy_base's buffer do.
                var input = source.StorageSpan();
                var snapshot = new byte[length * itemsize];
                for (int i = 0; i < length; i++)
                    input.Slice(source.StorageIndex(i), itemsize).CopyTo(snapshot.AsSpan(i * itemsize, itemsize));
                var destination = StorageSpan();
                for (int i = 0; i < length; i++)
                    snapshot.AsSpan(i * itemsize, itemsize).CopyTo(destination.Slice(unchecked((int)(offset + i * stride)), itemsize));
            }
            finally
            {
                source._bufferState -= 2;
                if (!borrowed) source.Release();
                GC.KeepAlive(this);
            }
        }

        // equiv_format: the same format after an optional '@', and the same itemsize.
        private static bool EquivFormat(PyMemoryView dest, PyMemoryView src)
        {
            static string Strip(string format)
            {
                format = string.IsNullOrEmpty(format) ? "B" : format;
                return format[0] == '@' ? format.Substring(1) : format;
            }
            return Strip(dest._fmt) == Strip(src._fmt) && dest._itemsize == src._itemsize;
        }

        // mp_ass_subscript with NULL
        protected internal override void MpDelSubscript(PyObject key) => MpAssSubscript(key, null);


        // len() of an N-D view is its first axis (CPython bf_length), while the flat
        // element count stays in _length for storage arithmetic.
        // memory_length
        protected internal override long Len()
        {
            CheckReleased();
            if (NDim == 0)
                throw PyTypeError.Create("0-dim memory has no length");
            return _shape == null ? _length : _shape[0];
        }

        // memory_iter
        protected internal override PyObject Iter()
        {
            CheckReleased();
            int ndim = NDim;
            if (ndim == 0)
                throw PyTypeError.Create("invalid indexing of 0-dim memory");
            if (ndim != 1)
                throw PyNotImplementedError.Create("multi-dimensional sub-views are not implemented");
            return new MemoryIterator(this, AdjustFmt(), _length);
        }

        /// <summary>
        /// memoryiterobject: one item at a time, the view checked for release at each, and the
        /// length taken when the iterator was made.
        /// </summary>
        private sealed class MemoryIterator : PyIterator
        {
            private PyMemoryView _seq;
            private readonly string _fmt;
            private readonly int _length;
            private int _index;

            internal MemoryIterator(PyMemoryView seq, string fmt, int length)
            {
                _seq = seq;
                _fmt = fmt;
                _length = length;
            }

            protected override string IteratorName => "memory_iterator";

            // memoryiter_next
            protected internal override PyObject IterNext()
            {
                var seq = _seq ?? throw PyStopIteration.Create();
                if (_index < _length)
                {
                    seq.CheckReleased();
                    return seq.UnpackSingle(seq.StorageIndex(_index++), _fmt);
                }
                _seq = null;
                throw PyStopIteration.Create();
            }
        }
        protected internal override bool Bool() => Len() != 0;

        // CPython memory_repr(): <memory at 0x..> or <released memory at 0x..>, and it never
        // raises. It must take the CLR identity hash directly instead of GetHashCode(): the
        // Python-level hash hook (ToHash) is restricted to readonly byte-format views and would
        // throw here, which would break print(f"{view}") for a writable or released view.
        public override string ToString() =>
            $"<{(_data == null ? "released memory" : "memory")} at {PyObjectId.Pointer(this)}>";
        protected internal override PyString Repr() => PyString.From(ToString());
    }
}
