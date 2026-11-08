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

        public sealed class PyArray : PyObject
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

            // array_buffer_getbuf: the view is over the items themselves, with no spare capacity.
            public override PyMemoryView GetBuffer(int flags)
            {
                if (_buffer.Length != _count * ItemSize)
                    Array.Resize(ref _buffer, _count * ItemSize);
                return new PyMemoryView(_buffer, false, 1, this).Reinterpret(_typecode.ToString(), ItemSize);
            }

            public override bool SupportsBuffer() => true;

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
                    var (start, stop, step) = slice.Indices(_count);
                    int count = slice.GetIndices(_count).Length;
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
                    var (start, stop, step) = slice.Indices(_count);
                    int count = slice.GetIndices(_count).Length;
                    if (step != 1)
                    {
                        if (!(value is PyArray va) || va._count != count)
                            throw PyValueError.Create("attempt to assign sequence of size " + (value is PyArray va2 ? va2._count.ToString() : "?") + " to extended slice of size " + count);
                        int idx = start;
                        for (int i = 0; i < count; i++)
                        {
                            WriteItem(_buffer, idx, ReadItem(va._buffer, i, va._typecode), _typecode);
                            idx += step;
                        }
                        return;
                    }
                    if (!(value is PyArray arr))
                        throw PyTypeError.Create($"can only assign array (not \"{value.GetTypeName()}\") to array slice");
                    if (arr._typecode != _typecode)
                        throw PyTypeError.Create($"can only assign array (not \"{value.GetTypeName()}\") to array slice");

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
                    var (start, stop, step) = slice.Indices(_count);
                    int count = slice.GetIndices(_count).Length;
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
                    if (_typecode == 'u')
                    {
                        sb.Append('\'');
                        for (int i = 0; i < _count; i++)
                        {
                            var item = ReadItem(_buffer, i, _typecode);
                            sb.Append(((PyString)item).Value);
                        }
                        sb.Append('\'');
                    }
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

            protected internal override PyObject TpRichCompare(PyObject other, PyObject.CompareOp op)
            {
                if (!(other is PyArray arr))
                    return PyNotImplemented.Instance;
                if (_typecode != arr._typecode)
                {
                    switch (op)
                    {
                        case PyObject.CompareOp.EQ: return PyBool.FromBool(false);
                        case PyObject.CompareOp.NE: return PyBool.FromBool(true);
                        default: return PyNotImplemented.Instance;
                    }
                }
                int minLen = Math.Min(_count, arr._count);
                for (int i = 0; i < minLen; i++)
                {
                    var a = ReadItem(_buffer, i, _typecode);
                    var b = ReadItem(arr._buffer, i, arr._typecode);
                    var cmp = PyObject.Compare(a, b, PyObject.CompareOp.EQ);
                    if (!cmp.IsTrue())
                    {
                        var lt = PyObject.Compare(a, b, PyObject.CompareOp.LT);
                        bool aLess = lt.IsTrue();
                        switch (op)
                        {
                            case PyObject.CompareOp.LT: return PyBool.FromBool(aLess);
                            case PyObject.CompareOp.LE: return PyBool.FromBool(aLess);
                            case PyObject.CompareOp.EQ: return PyBool.FromBool(false);
                            case PyObject.CompareOp.NE: return PyBool.FromBool(true);
                            case PyObject.CompareOp.GT: return PyBool.FromBool(!aLess);
                            case PyObject.CompareOp.GE: return PyBool.FromBool(!aLess);
                        }
                    }
                }
                switch (op)
                {
                    case PyObject.CompareOp.LT: return PyBool.FromBool(_count < arr._count);
                    case PyObject.CompareOp.LE: return PyBool.FromBool(_count <= arr._count);
                    case PyObject.CompareOp.EQ: return PyBool.FromBool(_count == arr._count);
                    case PyObject.CompareOp.NE: return PyBool.FromBool(_count != arr._count);
                    case PyObject.CompareOp.GT: return PyBool.FromBool(_count > arr._count);
                    case PyObject.CompareOp.GE: return PyBool.FromBool(_count >= arr._count);
                }
                return PyNotImplemented.Instance;
            }

            internal void Append(PyObject value)
            {
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
                    throw PyTypeError.Create("tounicode() requires an array of unicode characters");
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
                    throw PyTypeError.Create("fromunicode() requires an array of unicode characters");
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
            private int MachineFormat() => _typecode switch
            {
                'B' => 0,
                'b' => 1,
                'u' => ItemSize == 2 ? 18 : 20,
                'f' => 14,
                'd' => 16,
                _ => ItemSize switch { 2 => 2, 4 => 6, _ => 10 } + (char.IsLower(_typecode) ? 2 : 0),
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

            internal static readonly PyBuiltinFunction Reconstructor = new("_array_reconstructor", Reconstruct);

            // array__array_reconstructor_impl: the items as written on another machine, read here.
            internal static PyType Type;

            private static PyObject Reconstruct(PyObject[] args)
            {
                if (args.Length != 4)
                    throw PyTypeError.Create($"_array_reconstructor expected 4 arguments, got {args.Length}");
                if (args[0] is not PyType arrayType)
                    throw PyTypeError.Create($"first argument must be a type object, not {args[0].GetTypeName()}");
                if (!arrayType.IsSubclassOf(Type))
                    throw PyTypeError.Create($"{arrayType.Name} is not a subtype of array.array");
                if (args[1] is not PyString { Value.Length: 1 } typecode || !"bBuhHiIlLqQfd".Contains(typecode.Value))
                    throw PyValueError.Create("second argument must be a valid type code");
                int code = args[2].ToInt();
                if (code is < 0 or > 21)
                    throw PyValueError.Create("third argument must be a valid machine format code");
                if (args[3] is not PyBytes bytes)
                    throw PyTypeError.Create($"fourth argument should be bytes, not {args[3].GetTypeName()}");
                int size = code switch { 0 or 1 => 1, >= 2 and <= 5 => 2, >= 6 and <= 9 or 14 or 15 => 4, 18 or 19 => 2, 20 or 21 => 4, _ => 8 };
                var data = bytes.Value;
                if (data.Length % size != 0)
                    throw PyValueError.Create("bytes length not a multiple of item size");
                bool bigEndian = code >= 2 && code % 2 == 1;
                var cell = new byte[size];
                PyObject Item(int offset)
                {
                    for (int i = 0; i < size; i++)
                        cell[i] = data[offset + (bigEndian ? size - 1 - i : i)];
                    return code switch
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
                        16 or 17 => new PyFloat(BitConverter.ToDouble(cell)),
                        18 or 19 => PyString.From(((char)BitConverter.ToUInt16(cell)).ToString()),
                        _ => PyString.From(char.ConvertFromUtf32(BitConverter.ToInt32(cell))),
                    };
                }
                if (code >= 18 && typecode.Value != "u" || code < 18 && typecode.Value == "u")
                    throw PyValueError.Create("bad typecode (must be b, B, u, h, H, i, I, l, L, q, Q, f or d)");
                var items = new PyObject[data.Length / size];
                for (int i = 0; i < items.Length; i++)
                    items[i] = Item(i * size);
                return arrayType.Call(new PyObject[] { typecode, new PyList(items) }, null);
            }

            internal void InitializeFrom(PyObject initializer)
            {
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
                    if (_typecode == 'u')
                        FromUnicode(ps.Value);
                    else
                        throw PyTypeError.Create($"cannot use '{initializer.GetTypeName()}' as array initializer");
                }
                else if (initializer is PyArray arr)
                {
                    if (arr._typecode != _typecode)
                        throw PyTypeError.Create("array typecode not as specified");
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
            private readonly PyArray _array;
            private int _index;

            public PyArrayIterator(PyArray array)
            {
                _array = array;
                _index = 0;
            }

            protected override string IteratorName => "arrayiterator";

            protected internal override PyObject IterNext()
            {
                if (_index >= _array.Count)
                    throw PyStopIteration.Create();
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
                    throw PyTypeError.Create($"array() argument 1 must be a unicode character, not {typecodeArg.GetTypeName()}");

                char tc = ps.Value[0];
                if (!TypeCodeTable.ContainsKey(tc))
                    throw PyValueError.Create("bad typecode (must be b, B, u, h, H, i, I, l, L, q, Q, f or d)");

                var arr = new PyArray((PyType)cls, tc);

                if (args.Length >= 2 && args[1] is not PyNone)
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
            type.TypeDict["__iadd__"] = new PyMethodDescriptor("__iadd__", type, (self, args, kwargs) =>
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

            // array_methods (Modules/clinic/arraymodule.c.h): the calling convention of each method,
            // checked before its body.
            foreach (var (name, kind, min, max) in new (string, PyCall, int, int)[]
            {
                ("__copy__", PyCall.NoArgs, 0, 0), ("__deepcopy__", PyCall.One, 1, 1), ("count", PyCall.One, 1, 1),
                ("index", PyCall.Fast, 1, 3), ("remove", PyCall.One, 1, 1), ("pop", PyCall.Fast, 0, 1),
                ("extend", PyCall.Defining, 1, 1), ("insert", PyCall.Fast, 2, 2), ("buffer_info", PyCall.NoArgs, 0, 0),
                ("append", PyCall.One, 1, 1), ("byteswap", PyCall.NoArgs, 0, 0), ("reverse", PyCall.NoArgs, 0, 0),
                ("fromlist", PyCall.One, 1, 1), ("tolist", PyCall.NoArgs, 0, 0), ("frombytes", PyCall.One, 1, 1),
                ("tobytes", PyCall.NoArgs, 0, 0), ("fromunicode", PyCall.One, 1, 1), ("tounicode", PyCall.NoArgs, 0, 0),
                ("__reduce_ex__", PyCall.Defining, 1, 1),
            })
            {
                if (type.TypeDict.TryGetValue(name, out var defined) && defined is PyMethodDescriptor method)
                    type.TypeDict[name] = PyArgs.Method(type, name, kind, (self, args) => method._implementation(self, args, null), min, max);
            }

            type.CollectionFlags = PyType.TPFLAGS_SEQUENCE;
            PySlotWrappers.IndexArgRepeat(type);
            module.ModuleDict["array"] = type;
            module.ModuleDict["ArrayType"] = type;
            PyArray.Type = type;
            module.ModuleDict["_array_reconstructor"] = PyArray.Reconstructor;
            module.ModuleDict["typecodes"] = PyString.From("bBuhHiIlLqQfd");

            return module;
        }
    }
}