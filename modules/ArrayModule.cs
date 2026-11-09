using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace SharpPy.Modules
{
    internal static class ArrayModule
    {
        private struct TypeCodeInfo
        {
            public int Size;
            public bool IsSigned;
            public bool IsFloat;
            public string Name;
        }

        private static readonly Dictionary<char, TypeCodeInfo> TypeCodeTable;

        static ArrayModule()
        {
            int longSize = OperatingSystem.IsWindows() ? 4 : 8;
            TypeCodeTable = new Dictionary<char, TypeCodeInfo>
            {
                ['b'] = new TypeCodeInfo { Size = 1, IsSigned = true, IsFloat = false, Name = "signed char" },
                ['B'] = new TypeCodeInfo { Size = 1, IsSigned = false, IsFloat = false, Name = "unsigned byte integer" },
                ['u'] = new TypeCodeInfo { Size = 4, IsSigned = false, IsFloat = false, Name = "unsigned int" },
                ['h'] = new TypeCodeInfo { Size = 2, IsSigned = true, IsFloat = false, Name = "signed short integer" },
                ['H'] = new TypeCodeInfo { Size = 2, IsSigned = false, IsFloat = false, Name = "unsigned short integer" },
                ['i'] = new TypeCodeInfo { Size = 4, IsSigned = true, IsFloat = false, Name = "signed integer" },
                ['I'] = new TypeCodeInfo { Size = 4, IsSigned = false, IsFloat = false, Name = "unsigned integer" },
                ['l'] = new TypeCodeInfo { Size = longSize, IsSigned = true, IsFloat = false, Name = "signed long integer" },
                ['L'] = new TypeCodeInfo { Size = longSize, IsSigned = false, IsFloat = false, Name = "unsigned long integer" },
                ['q'] = new TypeCodeInfo { Size = 8, IsSigned = true, IsFloat = false, Name = "signed long long integer" },
                ['Q'] = new TypeCodeInfo { Size = 8, IsSigned = false, IsFloat = false, Name = "unsigned long long integer" },
                ['f'] = new TypeCodeInfo { Size = 4, IsSigned = true, IsFloat = true, Name = "float" },
                ['d'] = new TypeCodeInfo { Size = 8, IsSigned = true, IsFloat = true, Name = "double" },
            };
        }

        private static BigInteger GetIntValue(PyObject value, char typecode)
        {
            // the h, i, l, ... formats of the setitem functions: PyLong_AsLong and its kin, __index__ first
            return PyObject.IndexOrThrow(value).Value;
        }

        private static double GetFloatValue(PyObject value, char typecode)
        {
            if (value is PyFloat pf) return pf.Value;
            if (value is PyInt pi) return (double)pi.Value;
            if (value is PyBool pb) return pb.Value ? 1.0 : 0.0;
            throw PyTypeError.Create($"must be real number, not {value.GetTypeName()}");
        }

        private static PyObject ReadItem(byte[] buffer, int index, char typecode)
        {
            var info = TypeCodeTable[typecode];
            int offset = index * info.Size;
            Span<byte> span = buffer.AsSpan(offset, info.Size);

            if (info.IsFloat)
            {
                if (info.Size == 4)
                    return new PyFloat(BitConverter.ToSingle(span));
                return new PyFloat(BitConverter.ToDouble(span));
            }

            if (typecode == 'u')
            {
                int codePoint = BinaryPrimitives.ReadInt32LittleEndian(span);
                return PyString.From(char.ConvertFromUtf32(codePoint));
            }

            switch (info.Size)
            {
                case 1:
                    if (info.IsSigned) return PyInt.From((long)(sbyte)span[0]);
                    return PyInt.From((long)span[0]);
                case 2:
                    if (info.IsSigned) return PyInt.From((long)BinaryPrimitives.ReadInt16LittleEndian(span));
                    return PyInt.From((long)BinaryPrimitives.ReadUInt16LittleEndian(span));
                case 4:
                    if (info.IsSigned) return PyInt.From((long)BinaryPrimitives.ReadInt32LittleEndian(span));
                    return PyInt.From((long)BinaryPrimitives.ReadUInt32LittleEndian(span));
                case 8:
                    if (info.IsSigned) return PyInt.From(BinaryPrimitives.ReadInt64LittleEndian(span));
                    return PyInt.From(new BigInteger(BinaryPrimitives.ReadUInt64LittleEndian(span)));
                default:
                    throw new InvalidOperationException();
            }
        }

        private static void WriteItem(byte[] buffer, int index, PyObject value, char typecode)
        {
            var info = TypeCodeTable[typecode];
            int offset = index * info.Size;
            Span<byte> span = buffer.AsSpan(offset, info.Size);

            if (info.IsFloat)
            {
                double d = GetFloatValue(value, typecode);
                if (info.Size == 4)
                    BitConverter.TryWriteBytes(span, (float)d);
                else
                    BitConverter.TryWriteBytes(span, d);
                return;
            }

            if (typecode == 'u')
            {
                if (value is PyString ps && ps.Value.Length == 1)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(span, char.ConvertToUtf32(ps.Value, 0));
                    return;
                }
                // u_setitem: PyArg_Parse(v, "U;array item must be unicode character"), then one character
                throw PyTypeError.Create("array item must be unicode character");
            }

            BigInteger bi = GetIntValue(value, typecode);

            if (!info.IsSigned && bi < 0)
                throw PyOverflowError.Create(typecode == 'B' ? "unsigned byte integer is less than minimum" : "can't convert negative int to unsigned");

            BigInteger min, max;
            switch (info.Size)
            {
                case 1:
                    min = info.IsSigned ? -128 : 0;
                    max = info.IsSigned ? 127 : 255;
                    break;
                case 2:
                    min = info.IsSigned ? short.MinValue : 0;
                    max = info.IsSigned ? short.MaxValue : ushort.MaxValue;
                    break;
                case 4:
                    min = info.IsSigned ? int.MinValue : 0;
                    max = info.IsSigned ? int.MaxValue : uint.MaxValue;
                    break;
                case 8:
                    min = info.IsSigned ? long.MinValue : 0;
                    max = info.IsSigned ? long.MaxValue : ulong.MaxValue;
                    break;
                default:
                    throw new InvalidOperationException();
            }

            if (bi < min)
                throw PyOverflowError.Create($"{info.Name} is less than minimum");
            if (bi > max)
                throw PyOverflowError.Create($"{info.Name} is greater than maximum");

            switch (info.Size)
            {
                case 1:
                    if (info.IsSigned) span[0] = (byte)(sbyte)(int)bi;
                    else span[0] = (byte)(int)bi;
                    break;
                case 2:
                    if (info.IsSigned) BinaryPrimitives.WriteInt16LittleEndian(span, (short)(int)bi);
                    else BinaryPrimitives.WriteUInt16LittleEndian(span, (ushort)(int)bi);
                    break;
                case 4:
                    if (info.IsSigned) BinaryPrimitives.WriteInt32LittleEndian(span, (int)bi);
                    else BinaryPrimitives.WriteUInt32LittleEndian(span, (uint)bi);
                    break;
                case 8:
                    if (info.IsSigned) BinaryPrimitives.WriteInt64LittleEndian(span, (long)bi);
                    else BinaryPrimitives.WriteUInt64LittleEndian(span, (ulong)bi);
                    break;
            }
        }

        public sealed class PyArray : PyObject, IBufferExporter
        {
            /// <summary>array.array; what slicing, concatenation, repetition and copying build
            /// (newarrayobject(state->ArrayType, ...)), whatever the operand's class.</summary>
            internal static PyType ArrayType;
            internal readonly char _typecode;
            internal byte[] _buffer;
            internal int _count;


            internal int ItemSize => TypeCodeTable[_typecode].Size;
            internal int Count => _count;
            internal byte[] Buffer => _buffer;

            public PyArray(PyType type, char typecode)
            {
                if (!ReferenceEquals(type, ArrayType)) SetCustomType(type);
                _typecode = typecode;
                _buffer = Array.Empty<byte>();
                _count = 0;
            }

            public override PyType GetPyType() => _customType ?? ArrayType;
            public override string GetTypeName() => GetPyType().FullName;

            // array_buffer_getbuf: the view is over the items themselves and counts as an export
            // (ob_exports) until it is released.
            public override PyMemoryView GetBuffer(int flags) =>
                PyMemoryView.FromExporter(this, _count * ItemSize).Reinterpret(_typecode.ToString(), ItemSize);

            public override void ReleaseBuffer(PyMemoryView buffer) => buffer.Release();

            private int _exports;
            public void AcquireBufferExport() => System.Threading.Interlocked.Increment(ref _exports);
            public void ReleaseBufferExport() => System.Threading.Interlocked.Decrement(ref _exports);
            public Span<byte> BufferSpan => _buffer.AsSpan(0, _count * ItemSize);

            // array_resize: a size change while a buffer is exported is refused, before anything moves.
            internal void CheckResize(long newCount)
            {
                if (newCount != _count && BufferExports.Held(() => System.Threading.Volatile.Read(ref _exports) > 0))
                    throw PyBufferError.Create("cannot resize an array that is exporting buffers");
            }

            public override bool SupportsBuffer() => true;

            // allocated * itemsize
            internal long AllocatedBytes => _buffer.Length;

            internal void EnsureCapacity(int newCount)
            {
                int itemSize = ItemSize;
                int needed = newCount * itemSize;
                if (needed <= _buffer.Length) return;
                int newCap = Math.Max(needed, Math.Max(_buffer.Length * 2, itemSize * 8));
                var newBuf = new byte[newCap];
                Array.Copy(_buffer, newBuf, _count * itemSize);
                _buffer = newBuf;
            }

            private int NormalizeIndex(PyObject index)
            {
                // array_subscr and array_ass_subscr
                long i = TrySequenceIndex(index, out int position) ? position : throw PyTypeError.Create("array indices must be integers");

                if (i < 0) i += _count;
                if (i < 0 || i >= _count)
                    throw PyIndexError.Create("array index out of range");
                return (int)i;
            }

            protected internal override long Len() => _count;

            protected internal override bool Bool() => _count > 0;

            protected internal override PyObject Iter() => new PyArrayIterator(this);

            protected internal override PyBool ContainsItem(PyObject item)
            {
                for (int i = 0; i < _count; i++)
                {
                    if (PyObject.CompareBool(ReadItem(_buffer, i, _typecode), item, PyObject.CompareOp.EQ))
                        return PyBool.FromBool(true);
                }
                return PyBool.FromBool(false);
            }

            protected internal override PyObject MpSubscript(PyObject index)
            {
                if (index is PySlice slice)
                {
                    // PySlice_Unpack and PySlice_AdjustIndices, once
                    var (start, stop, step) = slice.StorageIndices(_count);
                    int count = PySlice.GetLength(start, stop, step);
                    var result = new PyArray(ArrayType, _typecode);
                    result.EnsureCapacity(count);
                    int idx = start;
                    for (int i = 0; i < count; i++)
                    {
                        var val = ReadItem(_buffer, idx, _typecode);
                        WriteItem(result._buffer, i, val, _typecode);
                        idx += step;
                    }
                    result._count = count;
                    return result;
                }
                int i2 = NormalizeIndex(index);
                return ReadItem(_buffer, i2, _typecode);
            }

            protected internal override void MpAssSubscript(PyObject index, PyObject value)
            {
                if (index is PySlice slice)
                {
                    // PySlice_Unpack and PySlice_AdjustIndices, once
                    var (start, stop, step) = slice.StorageIndices(_count);
                    int count = PySlice.GetLength(start, stop, step);
                    // array_ass_subscr: an array, or "can only assign array"; an extended slice takes
                    // exactly as many items as it selects.
                    if (value is not PyArray other)
                        throw PyTypeError.Create($"can only assign array (not \"{value.GetTypeName()}\") to array slice");
                    if (other._typecode != _typecode)
                        throw PyTypeError.Create("bad argument type for built-in operation");  // PyErr_BadArgument
                    if (ReferenceEquals(other, this))
                        other = (PyArray)MpSubscript(new PySlice(PyNone.Instance, PyNone.Instance, PyNone.Instance));
                    if (step != 1)
                    {
                        var va = other;
                        if (va._count != count)
                            throw PyValueError.Create($"attempt to assign array of size {va._count} to extended slice of size {count}");
                        int idx = start;
                        for (int i = 0; i < count; i++)
                        {
                            WriteItem(_buffer, idx, ReadItem(va._buffer, i, va._typecode), _typecode);
                            idx += step;
                        }
                        return;
                    }
                    var arr = other;
                    CheckResize(_count - count + arr._count);

                    int newCount = _count - count + arr._count;
                    int itemSize = ItemSize;
                    int tailStart = start + count;
                    int tailLen = _count - tailStart;
                    int newTailStart = start + arr._count;

                    EnsureCapacity(newCount);
                    if (tailLen > 0)
                        Array.Copy(_buffer, tailStart * itemSize, _buffer, newTailStart * itemSize, tailLen * itemSize);
                    if (arr._count > 0)
                        Array.Copy(arr._buffer, 0, _buffer, start * itemSize, arr._count * itemSize);
                    _count = newCount;
                    return;
                }
                int i2 = NormalizeIndex(index);
                WriteItem(_buffer, i2, value, _typecode);
            }

            protected internal override void MpDelSubscript(PyObject index)
            {
                if (index is PySlice slice)
                {
                    // PySlice_Unpack and PySlice_AdjustIndices, once
                    var (start, stop, step) = slice.StorageIndices(_count);
                    int count = PySlice.GetLength(start, stop, step);
                    CheckResize(_count - count);
                    if (step == 1)
                    {
                        int width = ItemSize;
                        int tailStart = start + count;
                        int tailItems = _count - tailStart;
                        if (tailItems > 0)
                            Array.Copy(_buffer, tailStart * width, _buffer, start * width, tailItems * width);
                        _count -= count;
                    }
                    else
                    {
                        var keep = new List<int>();
                        for (int i = 0; i < _count; i++)
                        {
                            bool inSlice = false;
                            int idx = start;
                            for (int j = 0; j < count; j++)
                            {
                                if (idx == i) { inSlice = true; break; }
                                idx += step;
                            }
                            if (!inSlice) keep.Add(i);
                        }
                        int itemSize2 = ItemSize;
                        var newBuf = new byte[keep.Count * itemSize2];
                        for (int i = 0; i < keep.Count; i++)
                            Array.Copy(_buffer, keep[i] * itemSize2, newBuf, i * itemSize2, itemSize2);
                        _buffer = newBuf;
                        _count = keep.Count;
                    }
                    return;
                }
                int i2 = NormalizeIndex(index);
                CheckResize(_count - 1);
                int itemSize = ItemSize;
                int tailLen = _count - i2 - 1;
                if (tailLen > 0)
                    Array.Copy(_buffer, (i2 + 1) * itemSize, _buffer, i2 * itemSize, tailLen * itemSize);
                _count--;
            }

            protected internal override PyString Repr()
            {
                var sb = new StringBuilder();
                sb.Append("array('");
                sb.Append(_typecode);
                sb.Append('\'');
                if (_count > 0)
                {
                    sb.Append(", ");
                    // array_repr: a 'u' array shows tounicode() by its repr (%R).
                    if (_typecode == 'u')
                        sb.Append(ToUnicode().ToRepr().Value);
                    else
                    {
                        sb.Append('[');
                        for (int i = 0; i < _count; i++)
                        {
                            if (i > 0) sb.Append(", ");
                            sb.Append(ReadItem(_buffer, i, _typecode).ToRepr().Value);
                        }
                        sb.Append(']');
                    }
                }
                sb.Append(')');
                return PyString.From(sb.ToString());
            }

            protected internal override PyString Str() => ToRepr();

            public override PyObject Add(PyObject other)
            {
                if (!(other is PyArray arr))
                    throw PyTypeError.Create($"can only append array (not \"{other.GetTypeName()}\") to array");
                if (arr._typecode != _typecode)
                    throw PyTypeError.Create("can only append two arrays with the same typecode");
                var result = new PyArray(ArrayType, _typecode);
                int total = _count + arr._count;
                result.EnsureCapacity(total);
                int itemSize = ItemSize;
                Array.Copy(_buffer, 0, result._buffer, 0, _count * itemSize);
                Array.Copy(arr._buffer, 0, result._buffer, _count * itemSize, arr._count * itemSize);
                result._count = total;
                return result;
            }

            public override PyObject Multiply(PyObject other)
            {
                // array_repeat (sq_repeat): sequence_repeat has made the count a Py_ssize_t already.
                long n;
                if (other is PyInt pi) n = PyAllocation.IndexSized(pi.Value);
                else if (other is PyBool pb) n = pb.Value ? 1 : 0;
                else return PyNotImplemented.Instance;
                if (n < 0) n = 0;
                var result = new PyArray(ArrayType, _typecode);
                // array_repeat: a size past Py_ssize_t is PyErr_NoMemory.
                int total = n == 0 ? 0 : PyAllocation.RepeatedItems((long)_count * ItemSize, n) / ItemSize;
                result.EnsureCapacity(total);
                int itemSize = ItemSize;
                for (long r = 0; r < n; r++)
                    Array.Copy(_buffer, 0, result._buffer, (int)r * _count * itemSize, _count * itemSize);
                result._count = total;
                return result;
            }

            // array_richcompare: lengths first for == and !=, then the first index where the items
            // differ (PyObject_RichCompareBool), whose items decide; equal up to the shorter, the sizes.
            protected internal override PyObject TpRichCompare(PyObject other, PyObject.CompareOp op)
            {
                if (other is not PyArray arr)
                    return PyNotImplemented.Instance;
                if (_count != arr._count && op is PyObject.CompareOp.EQ or PyObject.CompareOp.NE)
                    return PyBool.FromBool(op == PyObject.CompareOp.NE);
                int common = Math.Min(_count, arr._count);
                for (int i = 0; i < common; i++)
                {
                    var vi = ReadItem(_buffer, i, _typecode);
                    var wi = ReadItem(arr._buffer, i, arr._typecode);
                    if (PyObject.CompareBool(vi, wi, PyObject.CompareOp.EQ))
                        continue;
                    return op switch
                    {
                        PyObject.CompareOp.EQ => PyBool.False,
                        PyObject.CompareOp.NE => PyBool.True,
                        _ => PyObject.Compare(vi, wi, op),
                    };
                }
                return PyBool.FromBool(op switch
                {
                    PyObject.CompareOp.LT => _count < arr._count,
                    PyObject.CompareOp.LE => _count <= arr._count,
                    PyObject.CompareOp.GT => _count > arr._count,
                    PyObject.CompareOp.GE => _count >= arr._count,
                    PyObject.CompareOp.EQ => true,
                    _ => false,
                });
            }

            internal void Append(PyObject value)
            {
                CheckResize(_count + 1);
                EnsureCapacity(_count + 1);
                WriteItem(_buffer, _count, value, _typecode);
                _count++;
            }

            internal void Extend(PyObject iterable)
            {
                if (iterable is PyArray arr)
                {
                    if (arr._typecode != _typecode)
                        throw PyTypeError.Create("can only extend with array of same kind");
                    CheckResize(_count + arr._count);
                    int itemSize = ItemSize;
                    EnsureCapacity(_count + arr._count);
                    Array.Copy(arr._buffer, 0, _buffer, _count * itemSize, arr._count * itemSize);
                    _count += arr._count;
                    return;
                }
                foreach (var item in PyObject.IterateItems(iterable))
                {
                    Append(item);
                }
            }

            internal void Insert(PyObject indexObj, PyObject value)
            {
                long idx = PyArgs.Ssize(indexObj);

                if (idx < 0) idx += _count;
                if (idx < 0) idx = 0;
                if (idx > _count) idx = _count;

                CheckResize(_count + 1);
                int itemSize = ItemSize;
                EnsureCapacity(_count + 1);
                int tailLen = _count - (int)idx;
                if (tailLen > 0)
                    Array.Copy(_buffer, (int)idx * itemSize, _buffer, ((int)idx + 1) * itemSize, tailLen * itemSize);
                WriteItem(_buffer, (int)idx, value, _typecode);
                _count++;
            }

            internal PyObject Pop(PyObject[] args)
            {
                if (_count == 0)
                    throw PyIndexError.Create("pop from empty array");
                long idx = -1;
                if (args.Length >= 1)
                {
                    idx = PyArgs.Ssize(args[0]);
                }
                if (idx < 0) idx += _count;
                if (idx < 0 || idx >= _count)
                    throw PyIndexError.Create("pop index out of range");
                int i = (int)idx;
                var result = ReadItem(_buffer, i, _typecode);
                CheckResize(_count - 1);
                int itemSize = ItemSize;
                int tailLen = _count - i - 1;
                if (tailLen > 0)
                    Array.Copy(_buffer, (i + 1) * itemSize, _buffer, i * itemSize, tailLen * itemSize);
                _count--;
                return result;
            }

            internal void Remove(PyObject value)
            {
                for (int i = 0; i < _count; i++)
                {
                    if (PyObject.CompareBool(ReadItem(_buffer, i, _typecode), value, PyObject.CompareOp.EQ))
                    {
                        CheckResize(_count - 1);
                        int itemSize = ItemSize;
                        int tailLen = _count - i - 1;
                        if (tailLen > 0)
                            Array.Copy(_buffer, (i + 1) * itemSize, _buffer, i * itemSize, tailLen * itemSize);
                        _count--;
                        return;
                    }
                }
                throw PyValueError.Create("array.remove(x): x not in array");
            }

            internal PyObject Index(PyObject[] args)
            {
                PyObject x = args[0];
                long start = 0, stop = _count;
                if (args.Length >= 2)
                {
                    start = PyArgs.SliceIndexNotNone(args[1]);
                }
                if (args.Length >= 3)
                {
                    stop = PyArgs.SliceIndexNotNone(args[2]);
                }
                if (start < 0) start += _count;
                if (stop < 0) stop += _count;
                if (start < 0) start = 0;
                if (stop > _count) stop = _count;
                for (long i = start; i < stop; i++)
                {
                    if (PyObject.CompareBool(ReadItem(_buffer, (int)i, _typecode), x, PyObject.CompareOp.EQ))
                        return PyInt.From(i);
                }
                throw PyValueError.Create("array.index(x): x not in array");
            }

            internal PyObject CountOf(PyObject value)
            {
                long c = 0;
                for (int i = 0; i < _count; i++)
                {
                    if (PyObject.CompareBool(ReadItem(_buffer, i, _typecode), value, PyObject.CompareOp.EQ))
                        c++;
                }
                return PyInt.From(c);
            }

            internal void Reverse()
            {
                int itemSize = ItemSize;
                for (int i = 0; i < _count / 2; i++)
                {
                    int j = _count - 1 - i;
                    for (int b = 0; b < itemSize; b++)
                    {
                        byte tmp = _buffer[i * itemSize + b];
                        _buffer[i * itemSize + b] = _buffer[j * itemSize + b];
                        _buffer[j * itemSize + b] = tmp;
                    }
                }
            }

            internal void Byteswap()
            {
                int itemSize = ItemSize;
                if (itemSize == 1) return;
                for (int i = 0; i < _count; i++)
                {
                    int offset = i * itemSize;
                    for (int b = 0; b < itemSize / 2; b++)
                    {
                        byte tmp = _buffer[offset + b];
                        _buffer[offset + b] = _buffer[offset + itemSize - 1 - b];
                        _buffer[offset + itemSize - 1 - b] = tmp;
                    }
                }
            }

            internal PyBytes ToBytes()
            {
                int byteLen = _count * ItemSize;
                var result = new byte[byteLen];
                Array.Copy(_buffer, result, byteLen);
                return PyBytes.From(result);
            }

            internal void FromBytes(byte[] data)
            {
                int itemSize = ItemSize;
                if (data.Length % itemSize != 0)
                    throw PyValueError.Create("bytes length not a multiple of item size");
                int newItems = data.Length / itemSize;
                CheckResize(_count + newItems);
                EnsureCapacity(_count + newItems);
                Array.Copy(data, 0, _buffer, _count * itemSize, data.Length);
                _count += newItems;
            }

            internal PyList ToList()
            {
                var list = new PyList();
                for (int i = 0; i < _count; i++)
                    list.Append(ReadItem(_buffer, i, _typecode));
                return list;
            }

            internal void FromList(PyObject iterable)
            {
                foreach (var item in PyObject.IterateItems(iterable))
                {
                    Append(item);
                }
            }

            internal PyString ToUnicode()
            {
                if (_typecode != 'u')
                    throw PyValueError.Create("tounicode() may only be called on unicode type arrays");
                var sb = new StringBuilder();
                for (int i = 0; i < _count; i++)
                {
                    var item = ReadItem(_buffer, i, _typecode);
                    sb.Append(((PyString)item).Value);
                }
                return PyString.From(sb.ToString());
            }

            internal void FromUnicode(string s)
            {
                if (_typecode != 'u')
                    throw PyValueError.Create("fromunicode() may only be called on unicode type arrays");
                for (int i = 0; i < s.Length; )
                {
                    int cp = char.ConvertToUtf32(s, i);
                    i += char.IsHighSurrogate(s[i]) ? 2 : 1;
                    Append(PyString.From(char.ConvertFromUtf32(cp)));
                }
            }

            internal PyTuple BufferInfo()
            {
                return PyTuple.From(new PyObject[] { PyInt.From(0), PyInt.From(_count) });
            }

            internal PyArray Copy()
            {
                var result = new PyArray(ArrayType, _typecode);
                int itemSize = ItemSize;
                result.EnsureCapacity(_count);
                Array.Copy(_buffer, result._buffer, _count * itemSize);
                result._count = _count;
                return result;
            }

            // typecode_to_mformat_code
            private int MachineFormat() => MachineFormatOf(_typecode);

            // typecode_to_mformat_code, for this little-endian machine
            private static int MachineFormatOf(char typecode) => typecode switch
            {
                'B' => 0,
                'b' => 1,
                'u' => TypeCodeTable['u'].Size == 2 ? 18 : 20,
                'f' => 14,
                'd' => 16,
                _ => TypeCodeTable[typecode].Size switch { 2 => 2, 4 => 6, _ => 10 } + (char.IsLower(typecode) ? 2 : 0),
            };

            // array___reduce_ex___impl
            internal PyTuple ReduceEx(int protocol)
            {
                PyObject state = HasCustomType ? GetAttribute("__dict__") : PyNone.Instance;
                if (protocol < 3)
                    return PyTuple.From(new PyObject[]
                    {
                        GetPyType(),
                        PyTuple.From(new PyObject[] { PyString.From(_typecode.ToString()), ToList() }),
                        state,
                    });
                return PyTuple.From(new PyObject[]
                {
                    Reconstructor,
                    PyTuple.From(new PyObject[] { GetPyType(), PyString.From(_typecode.ToString()), PyInt.From(MachineFormat()), ToBytes() }),
                    state,
                });
            }

            // array._array_reconstructor(arraytype, typecode: int(accept={str}), mformat_code: int, items, /)
            internal static readonly PyBuiltinFunction Reconstructor = PyArgs.FastCall("array", "_array_reconstructor", 4, 4, Reconstruct);

            // array__array_reconstructor_impl: the items as written on another machine, read here.
            internal static PyType Type;

            private static PyObject Reconstruct(PyObject[] args)
            {
                if (args[0] is not PyType arrayType)
                    throw PyTypeError.Create($"first argument must be a type object, not {args[0].GetTypeName()}");
                if (!arrayType.IsSubclassOf(Type))
                    throw PyTypeError.Create($"{arrayType.Name} is not a subtype of array.array");
                if (args[1] is not PyString { Value.Length: 1 } typecode)
                    throw PyArgs.BadArgument("_array_reconstructor", "argument 2", "a unicode character", args[1]);
                if (!"bBuhHiIlLqQfd".Contains(typecode.Value))
                    throw PyValueError.Create("second argument must be a valid type code");
                int code = PyArgs.Int(args[2]);
                if (code is < 0 or > 21)
                    throw PyValueError.Create("third argument must be a valid machine format code.");
                if (args[3] is not PyBytes bytes)
                    throw PyTypeError.Create($"fourth argument should be bytes, not {args[3].GetPyType().FullName}");
                char tc = typecode.Value[0];
                // Fast path: the items are in this machine's format for the typecode (make_array).
                if (code == MachineFormatOf(tc))
                    return arrayType.Call(new PyObject[] { typecode, bytes }, null);
                // Slow path: decoded by the format code, as pickled on another machine.
                int size = code switch { 0 or 1 => 1, >= 2 and <= 5 => 2, >= 6 and <= 9 or 14 or 15 => 4, 18 or 19 => 2, 20 or 21 => 4, _ => 8 };
                var data = bytes.Value;
                if (data.Length % size != 0)
                    throw PyValueError.Create("string length not a multiple of item size");
                bool bigEndian = code >= 2 && code % 2 == 1;
                PyObject converted;
                if (code >= 18)
                {
                    // PyUnicode_DecodeUTF16 / PyUnicode_DecodeUTF32, strict
                    string codec = (code <= 19 ? "utf-16-" : "utf-32-") + (bigEndian ? "be" : "le");
                    converted = PyString.From(PyStringNewArguments.DecodeBytes(data, codec, "strict"));
                }
                else
                {
                    bool integer = code < 14;
                    bool signedInt = integer && (code == 1 || (code >= 4 && code % 4 is 0 or 1));
                    // An integer format picks the first integer typecode of its size and signedness.
                    if (integer)
                        foreach (char candidate in "bBhHiIlLqQ")
                            if (TypeCodeTable[candidate].Size == size && TypeCodeTable[candidate].IsSigned == signedInt)
                            {
                                tc = candidate;
                                break;
                            }
                    var cell = new byte[size];
                    var items = new PyObject[data.Length / size];
                    for (int n = 0; n < items.Length; n++)
                    {
                        for (int i = 0; i < size; i++)
                            cell[i] = data[n * size + (bigEndian ? size - 1 - i : i)];
                        items[n] = code switch
                        {
                            0 => PyInt.From(cell[0]),
                            1 => PyInt.From((sbyte)cell[0]),
                            2 or 3 => PyInt.From(BitConverter.ToUInt16(cell)),
                            4 or 5 => PyInt.From(BitConverter.ToInt16(cell)),
                            6 or 7 => PyInt.From(BitConverter.ToUInt32(cell)),
                            8 or 9 => PyInt.From(BitConverter.ToInt32(cell)),
                            10 or 11 => PyInt.From((System.Numerics.BigInteger)BitConverter.ToUInt64(cell)),
                            12 or 13 => PyInt.From(BitConverter.ToInt64(cell)),
                            14 or 15 => new PyFloat(BitConverter.ToSingle(cell)),
                            _ => new PyFloat(BitConverter.ToDouble(cell)),
                        };
                    }
                    converted = new PyList(items);
                }
                return arrayType.Call(new PyObject[] { PyString.From(tc.ToString()), converted }, null);
            }

            // array_new: a str is refused but for 'u', and a 'u' array but by one; then a list, tuple,
            // bytes, bytearray, str or array of this typecode is read at once, and anything else
            // (another array, None, a generator) is iterated (array_iter_extend).
            internal void InitializeFrom(PyObject initializer)
            {
                if (_typecode != 'u')
                {
                    if (initializer is PyString)
                        throw PyTypeError.Create($"cannot use a str to initialize an array with typecode '{_typecode}'");
                    if (initializer is PyArray { _typecode: 'u' })
                        throw PyTypeError.Create($"cannot use a unicode array to initialize an array with typecode '{_typecode}'");
                }
                if (initializer is PyBytes pb)
                {
                    FromBytes(pb.Value);
                }
                else if (initializer is PyByteArray pba)
                {
                    FromBytes(pba.ToByteArrayCopy());
                }
                else if (initializer is PyString ps)
                {
                    FromUnicode(ps.Value);
                }
                else if (initializer is PyArray arr && arr._typecode == _typecode)
                {
                    int itemSize = ItemSize;
                    EnsureCapacity(_count + arr._count);
                    Array.Copy(arr._buffer, 0, _buffer, _count * itemSize, arr._count * itemSize);
                    _count += arr._count;
                }
                else if (initializer is PyList || initializer is PyTuple)
                {
                    FromList(initializer);
                }
                else
                {
                    foreach (var item in PyObject.IterateItems(initializer))
                    {
                        Append(item);
                    }
                }
            }
        }

        private sealed class PyArrayIterator : PyIterator
        {
            private PyArray _array;
            private int _index;

            public PyArrayIterator(PyArray array)
            {
                _array = array;
                _index = 0;
            }

            // tp_name of Arraytype's iterator spec
            protected override string IteratorName => "array.arrayiterator";

            // array_arrayiterator___reduce___impl: (iter, (array,), index), or iter(()) once exhausted.
            internal override PyObject Reduce() =>
                _array == null ? ReduceExhausted(PyTuple.From(System.Array.Empty<PyObject>())) : ReduceIter(_array, _index);

            // array_arrayiterator___setstate__: PyLong_AsSsize_t, clipped to the array, if any.
            internal override void SetState(PyObject state)
            {
                long index = ClippedIndex(state, _array?.Count ?? 0);
                if (_array != null)
                    _index = (int)index;
            }

            protected internal override PyObject IterNext()
            {
                if (_array == null || _index >= _array.Count)
                {
                    _array = null;
                    throw PyStopIteration.Create();
                }
                var item = ReadItem(_array.Buffer, _index, _array._typecode);
                _index++;
                return item;
            }
        }

        internal static PyModule CreateArrayModule()
        {
            var module = new PyModule("array");

            var type = new PyType("array", new[] { PyType.ObjectType }, "array");
            PyArray.ArrayType = type;

            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) =>
            {
                if (args.Length < 1)
                    throw PyTypeError.Create("array() takes at least 1 argument (0 given)");

                PyObject typecodeArg = args[0];
                if (!(typecodeArg is PyString ps) || ps.Value.Length != 1)
                    throw PyArgs.BadArgument("array", "argument 1", "a unicode character", typecodeArg);

                char tc = ps.Value[0];
                if (!TypeCodeTable.ContainsKey(tc))
                    throw PyValueError.Create("bad typecode (must be b, B, u, h, H, i, I, l, L, q, Q, f or d)");

                var arr = new PyArray((PyType)cls, tc);

                if (args.Length >= 2)
                    arr.InitializeFrom(args[1]);

                return arr;
            }, 1, 2, acceptsKwargs: true);

            type.TypeDict["append"] = new PyMethodDescriptor("append", type, (self, args, kwargs) =>
            {
                ((PyArray)self).Append(args[0]);
                return PyNone.Instance;
            }, 1, 1);

            type.TypeDict["extend"] = new PyMethodDescriptor("extend", type, (self, args, kwargs) =>
            {
                ((PyArray)self).Extend(args[0]);
                return PyNone.Instance;
            }, 1, 1);

            type.TypeDict["insert"] = new PyMethodDescriptor("insert", type, (self, args, kwargs) =>
            {
                ((PyArray)self).Insert(args[0], args[1]);
                return PyNone.Instance;
            }, 2, 2);

            type.TypeDict["pop"] = new PyMethodDescriptor("pop", type, (self, args, kwargs) =>
            {
                return ((PyArray)self).Pop(args);
            }, 0, 1);

            type.TypeDict["remove"] = new PyMethodDescriptor("remove", type, (self, args, kwargs) =>
            {
                ((PyArray)self).Remove(args[0]);
                return PyNone.Instance;
            }, 1, 1);

            type.TypeDict["index"] = new PyMethodDescriptor("index", type, (self, args, kwargs) =>
            {
                return ((PyArray)self).Index(args);
            }, 1, 3);

            type.TypeDict["count"] = new PyMethodDescriptor("count", type, (self, args, kwargs) =>
            {
                return ((PyArray)self).CountOf(args[0]);
            }, 1, 1);

            type.TypeDict["reverse"] = new PyMethodDescriptor("reverse", type, (self, args, kwargs) =>
            {
                ((PyArray)self).Reverse();
                return PyNone.Instance;
            }, 0, 0);

            type.TypeDict["byteswap"] = new PyMethodDescriptor("byteswap", type, (self, args, kwargs) =>
            {
                ((PyArray)self).Byteswap();
                return PyNone.Instance;
            }, 0, 0);

            type.TypeDict["tobytes"] = new PyMethodDescriptor("tobytes", type, (self, args, kwargs) =>
            {
                return ((PyArray)self).ToBytes();
            }, 0, 0);

            type.TypeDict["frombytes"] = new PyMethodDescriptor("frombytes", type, (self, args, kwargs) =>
            {
                var arr = (PyArray)self;
                // array.frombytes(buffer: Py_buffer)
                arr.FromBytes(BytesMethods.RequireBuffer(args[0]));
                return PyNone.Instance;
            }, 1, 1);

            type.TypeDict["tolist"] = new PyMethodDescriptor("tolist", type, (self, args, kwargs) =>
            {
                return ((PyArray)self).ToList();
            }, 0, 0);

            type.TypeDict["fromlist"] = new PyMethodDescriptor("fromlist", type, (self, args, kwargs) =>
            {
                ((PyArray)self).FromList(args[0]);
                return PyNone.Instance;
            }, 1, 1);

            type.TypeDict["tounicode"] = new PyMethodDescriptor("tounicode", type, (self, args, kwargs) =>
            {
                return ((PyArray)self).ToUnicode();
            }, 0, 0);

            type.TypeDict["fromunicode"] = new PyMethodDescriptor("fromunicode", type, (self, args, kwargs) =>
            {
                var arr = (PyArray)self;
                if (args[0] is PyString ps)
                    arr.FromUnicode(ps.Value);
                else
                    throw PyTypeError.Create($"a unicode string is required, not \"{args[0].GetTypeName()}\"");
                return PyNone.Instance;
            }, 1, 1);

            type.TypeDict["buffer_info"] = new PyMethodDescriptor("buffer_info", type, (self, args, kwargs) =>
            {
                return ((PyArray)self).BufferInfo();
            }, 0, 0);

            type.TypeDict["__copy__"] = new PyMethodDescriptor("__copy__", type, (self, args, kwargs) =>
            {
                return ((PyArray)self).Copy();
            }, 0, 0);

            type.TypeDict["__deepcopy__"] = new PyMethodDescriptor("__deepcopy__", type, (self, args, kwargs) =>
            {
                return ((PyArray)self).Copy();
            }, 1, 1);

            type.TypeDict["__reduce_ex__"] = new PyMethodDescriptor("__reduce_ex__", type, (self, args, kwargs) =>
            {
                if (!PyInt.LongCheck(args[0]))
                    throw PyTypeError.Create("__reduce_ex__ argument should be an integer");
                return ((PyArray)self).ReduceEx((int)PyArgs.CLong(args[0]));
            }, 1, 1);

            type.TypeDict["__add__"] = new PyWrapperDescriptor("__add__", type, (self, args, kwargs) => self.Add(args[0]));
            type.TypeDict["__mul__"] = new PyWrapperDescriptor("__mul__", type, (self, args, kwargs) => self.Multiply(args[0]));
            type.TypeDict["__rmul__"] = new PyWrapperDescriptor("__rmul__", type, (self, args, kwargs) => self.Multiply(args[0]));
            type.TypeDict["__class_getitem__"] = PyClassMethodDescriptor.GenericAliasGetItem(type);
            TypeTables.Repr(type);
            TypeTables.Compared(type);
            // sq_inplace_concat: a slot, whose __iadd__ is its wrapper
            type.TypeDict["__iadd__"] = new PyWrapperDescriptor("__iadd__", type, (self, args, kwargs) =>
            {
                var arr = (PyArray)self;
                if (args[0] is PyArray other && other._typecode == arr._typecode)
                {
                    arr.Extend(other);
                    return arr;
                }
                throw PyTypeError.Create($"can only append array (not \"{args[0].GetTypeName()}\") to array");
            }, 1, 1);

            type.TypeDict["__imul__"] = new PyMethodDescriptor("__imul__", type, (self, args, kwargs) =>
            {
                var arr = (PyArray)self;
                long n;
                if (args[0] is PyInt pi) n = PyAllocation.IndexSized(pi.Value);
                else if (args[0] is PyBool pb) n = pb.Value ? 1 : 0;
                else throw PyTypeError.Create($"can't multiply sequence by non-int of type '{args[0].GetTypeName()}'");
                if (n < 0) n = 0;
                int itemSize = arr.ItemSize;
                int origCount = arr.Count;
                // array_inplace_repeat: a size past Py_ssize_t is PyErr_NoMemory.
                int total = n == 0 ? 0 : PyAllocation.RepeatedItems((long)origCount * itemSize, n) / itemSize;
                arr.CheckResize(total);
                var newBuf = new byte[total * itemSize];
                for (long r = 0; r < n; r++)
                    Array.Copy(arr.Buffer, 0, newBuf, (int)r * origCount * itemSize, origCount * itemSize);
                arr._buffer = newBuf.Length == 0 ? new byte[itemSize] : newBuf;
                arr._count = total;
                return arr;
            }, 1, 1);

            type.TypeDict["__len__"] = new PyWrapperDescriptor("__len__", type, (self, args, kwargs) =>
            {
                return PyInt.From(((PyArray)self).Len());
            });

            type.TypeDict["__iter__"] = new PyWrapperDescriptor("__iter__", type, (self, args, kwargs) =>
            {
                return ((PyArray)self).Iter();
            });

            type.TypeDict["__getitem__"] = new PyWrapperDescriptor("__getitem__", type, (self, args, kwargs) =>
            {
                return ((PyArray)self).MpSubscript(args[0]);
            });

            type.TypeDict["__setitem__"] = new PyWrapperDescriptor("__setitem__", type, (self, args, kwargs) =>
            {
                ((PyArray)self).MpAssSubscript(args[0], args[1]);
                return PyNone.Instance;
            });

            type.TypeDict["__delitem__"] = new PyWrapperDescriptor("__delitem__", type, (self, args, kwargs) =>
            {
                ((PyArray)self).MpDelSubscript(args[0]);
                return PyNone.Instance;
            });

            type.TypeDict["__contains__"] = new PyWrapperDescriptor("__contains__", type, (self, args, kwargs) =>
            {
                return ((PyArray)self).ContainsItem(args[0]);
            });

            type.TypeDict["typecode"] = new PyGetSetDescriptor("typecode", type, self =>
            {
                return PyString.From(((PyArray)self)._typecode.ToString());
            });

            type.TypeDict["itemsize"] = new PyGetSetDescriptor("itemsize", type, self =>
            {
                return PyInt.From(((PyArray)self).ItemSize);
            });

            type.TypeDict["__hash__"] = PyNone.Instance;

            // array_array___sizeof___impl: the object, and the items it has room for.
            type.TypeDict["__sizeof__"] = new PyMethodDescriptor("__sizeof__", type, (self, args, kwargs) =>
                PyInt.From(type.Layout.BasicSize + ((PyArray)self).AllocatedBytes));

            type.CollectionFlags = PyType.TPFLAGS_SEQUENCE;
            PySlotWrappers.IndexArgRepeat(type);
            module.ModuleDict["array"] = type;
            module.ModuleDict["ArrayType"] = type;

            // array_array_fromfile_impl: f.read(n * itemsize), which must be bytes, frombytes of it, and
            // EOFError when it came back short (what it held is kept).
            type.TypeDict["fromfile"] = PyArgs.Method(type, "fromfile", PyCall.Defining, (self, args) =>
            {
                var arr = (PyArray)self;
                long n = PyArgs.Ssize(args[1]);
                if (n < 0)
                    throw PyValueError.Create("negative count");
                if (n > long.MaxValue / arr.ItemSize)
                    throw PyMemoryError.Create();
                long nbytes = n * arr.ItemSize;
                var read = args[0].GetAttribute("read").Call(new PyObject[] { PyInt.From(nbytes) }, null);
                if (read is not PyBytes bytes)
                    throw PyTypeError.Create("read() didn't return bytes");
                arr.FromBytes(bytes.Value);
                if (bytes.Value.Length != nbytes)
                    throw PyEOFError.Create("read() didn't return enough bytes");
                return PyNone.Instance;
            }, 2, 2);

            // array_array_tofile_impl: f.write of 64K blocks.
            type.TypeDict["tofile"] = PyArgs.Method(type, "tofile", PyCall.Defining, (self, args) =>
            {
                var arr = (PyArray)self;
                const int BlockSize = 64 * 1024;
                int nbytes = arr.Count * arr.ItemSize;
                var write = args[0].GetAttribute("write");
                for (int offset = 0; offset < nbytes; offset += BlockSize)
                {
                    var block = new byte[Math.Min(BlockSize, nbytes - offset)];
                    Array.Copy(arr.Buffer, offset, block, 0, block.Length);
                    write.Call(new PyObject[] { PyBytes.From(block) }, null);
                }
                return PyNone.Instance;
            }, 1, 1);

            // array_modexec: collections.abc.MutableSequence.register(array)
            var mutableSequence = PyImportSystem.Import("collections.abc").GetAttribute("MutableSequence");
            mutableSequence.GetAttribute("register").Call(new PyObject[] { type }, null);
            PyArray.Type = type;
            module.ModuleDict["_array_reconstructor"] = PyArray.Reconstructor;
            module.ModuleDict["typecodes"] = PyString.From("bBuhHiIlLqQfd");

            return module;
        }
    }
}