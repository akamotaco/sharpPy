using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace SharpPy.Modules
{
    internal static class _StructModule
    {
        private static PyType ErrorType;
        private static PyType StructType;

        private static PythonException Error(string message) => PyExceptionSlots.Raise(ErrorType, message);

        private static BigInteger IntOf(PyObject value) => value is PyBool b ? (b.Value ? BigInteger.One : BigInteger.Zero) : value is PyInt ? BigInteger.Parse(value.ToRepr().Value) : throw Error("required argument is not an integer");

        private struct FormatEntry
        {
            public char Code;
            public int Count;
        }

        private static (char byteOrder, List<FormatEntry> entries) ParseFormat(string fmt)
        {
            char byteOrder = '@';
            int i = 0;
            if (fmt.Length > 0 && (fmt[0] == '@' || fmt[0] == '=' || fmt[0] == '<' || fmt[0] == '>' || fmt[0] == '!'))
            {
                byteOrder = fmt[0];
                i = 1;
            }
            var entries = new List<FormatEntry>();
            while (i < fmt.Length)
            {
                if (char.IsWhiteSpace(fmt[i])) { i++; continue; }
                int count = 0;
                bool hasCount = false;
                while (i < fmt.Length && char.IsDigit(fmt[i]))
                {
                    count = count * 10 + (fmt[i] - '0');
                    hasCount = true;
                    i++;
                }
                if (i >= fmt.Length)
                {
                    if (hasCount) throw Error("repeat count given without format specifier");
                    break;
                }
                char code = fmt[i];
                if ("xcbB?hHiIlLqQnNfdespP".IndexOf(code) < 0)
                    throw Error("bad char in struct format");
                if (!hasCount) count = 1;
                entries.Add(new FormatEntry { Code = code, Count = count });
                i++;
            }
            return (byteOrder, entries);
        }

        private static int GetItemSize(char code, char byteOrder)
        {
            switch (code)
            {
                case 'x': case 'c': case 'b': case 'B': case '?': return 1;
                case 'h': case 'H': case 'e': return 2;
                case 'i': case 'I': case 'f': return 4;
                case 'l': case 'L': return (byteOrder == '@' && !OperatingSystem.IsWindows()) ? 8 : 4;
                case 'q': case 'Q': case 'n': case 'N': case 'P': case 'd': return 8;
                case 's': case 'p': return 1;
                default: throw Error("bad char in struct format");
            }
        }

        private static int GetAlignment(char code, char byteOrder)
        {
            if (byteOrder != '@') return 1;
            switch (code)
            {
                case 'x': case 'c': case 'b': case 'B': case '?': case 's': case 'p': return 1;
                case 'h': case 'H': case 'e': return 2;
                case 'i': case 'I': case 'f': return 4;
                case 'l': case 'L': return OperatingSystem.IsWindows() ? 4 : 8;
                case 'q': case 'Q': case 'n': case 'N': case 'P': case 'd': return 8;
                default: return 1;
            }
        }

        private static int CalcSize(char byteOrder, List<FormatEntry> entries)
        {
            int offset = 0;
            foreach (var entry in entries)
            {
                int align = GetAlignment(entry.Code, byteOrder);
                if (byteOrder == '@' && align > 1)
                    offset += (align - (offset % align)) % align;
                if (entry.Code == 's' || entry.Code == 'p')
                    offset += entry.Count;
                else
                    offset += GetItemSize(entry.Code, byteOrder) * entry.Count;
            }
            return offset;
        }

        private static bool IsLE(char byteOrder)
        {
            if (byteOrder == '<') return true;
            if (byteOrder == '>' || byteOrder == '!') return false;
            return BitConverter.IsLittleEndian;
        }

        private static void WriteInt(byte[] buf, int offset, BigInteger value, int size, bool le)
        {
            if (le)
                for (int i = 0; i < size; i++) { buf[offset + i] = (byte)(value & 0xFF); value >>= 8; }
            else
                for (int i = size - 1; i >= 0; i--) { buf[offset + i] = (byte)(value & 0xFF); value >>= 8; }
        }

        private static BigInteger ReadInt(byte[] buf, int offset, int size, bool le, bool signed)
        {
            BigInteger result = 0;
            if (le)
                for (int i = size - 1; i >= 0; i--) result = (result << 8) | buf[offset + i];
            else
                for (int i = 0; i < size; i++) result = (result << 8) | buf[offset + i];
            if (signed && size > 0 && (buf[offset + (le ? size - 1 : 0)] & 0x80) != 0)
                result -= BigInteger.One << (size * 8);
            return result;
        }

        private static void WriteFloat(byte[] buf, int offset, double value, int size, bool le)
        {
            if (size == 4)
                WriteInt(buf, offset, BitConverter.SingleToInt32Bits((float)value), 4, le);
            else if (size == 8)
                WriteInt(buf, offset, BitConverter.DoubleToInt64Bits(value), 8, le);
            else
                WriteInt(buf, offset, BitConverter.HalfToUInt16Bits((Half)value), 2, le);
        }

        private static double ReadFloat(byte[] buf, int offset, int size, bool le)
        {
            if (size == 4)
                return BitConverter.Int32BitsToSingle((int)ReadInt(buf, offset, 4, le, true));
            if (size == 8)
                return BitConverter.Int64BitsToDouble((long)ReadInt(buf, offset, 8, le, true));
            return (double)BitConverter.UInt16BitsToHalf((ushort)ReadInt(buf, offset, 2, le, false));
        }

        private static void CheckRange(char code, BigInteger value)
        {
            switch (code)
            {
                case 'b': if (value < -128 || value > 127) throw Error("'b' format requires -128 <= number <= 127"); break;
                case 'B': if (value < 0 || value > 255) throw Error("'B' format requires 0 <= number <= 255"); break;
                case 'h': if (value < -32768 || value > 32767) throw Error("'h' format requires -32768 <= number <= 32767"); break;
                case 'H': if (value < 0 || value > 65535) throw Error("'H' format requires 0 <= number <= 65535"); break;
                case 'i': if (value < -2147483648 || value > 2147483647) throw Error("'i' format requires -2147483648 <= number <= 2147483647"); break;
                case 'I': if (value < 0 || value > 4294967295) throw Error("'I' format requires 0 <= number <= 4294967295"); break;
                case 'l': if (value < -2147483648 || value > 2147483647) throw Error("'l' format requires -2147483648 <= number <= 2147483647"); break;
                case 'L': if (value < 0 || value > 4294967295) throw Error("'L' format requires 0 <= number <= 4294967295"); break;
                case 'q': if (value < BigInteger.Parse("-9223372036854775808") || value > BigInteger.Parse("9223372036854775807")) throw Error("'q' format requires -9223372036854775808 <= number <= 9223372036854775807"); break;
                case 'Q': if (value < 0 || value > BigInteger.Parse("18446744073709551615")) throw Error("'Q' format requires 0 <= number <= 18446744073709551615"); break;
                case 'n': if (value < BigInteger.Parse("-9223372036854775808") || value > BigInteger.Parse("9223372036854775807")) throw Error("'n' format requires -9223372036854775808 <= number <= 9223372036854775807"); break;
                case 'N': if (value < 0 || value > BigInteger.Parse("18446744073709551615")) throw Error("'N' format requires 0 <= number <= 18446744073709551615"); break;
                case 'P': if (value < 0 || value > BigInteger.Parse("18446744073709551615")) throw Error("'P' format requires 0 <= number <= 18446744073709551615"); break;
            }
        }

        private static int GetItemCount(List<FormatEntry> entries)
        {
            int count = 0;
            foreach (var e in entries)
            {
                if (e.Code == 'x') continue;
                if (e.Code == 's' || e.Code == 'p') count += 1;
                else count += e.Count;
            }
            return count;
        }

        private static byte[] PackValues(char byteOrder, List<FormatEntry> entries, PyObject[] values, int vi)
        {
            int size = CalcSize(byteOrder, entries);
            byte[] buf = new byte[size];
            bool le = IsLE(byteOrder);
            int offset = 0;

            foreach (var entry in entries)
            {
                int itemSize = GetItemSize(entry.Code, byteOrder);
                int align = GetAlignment(entry.Code, byteOrder);
                if (byteOrder == '@' && align > 1)
                    offset += (align - (offset % align)) % align;

                if (entry.Code == 'x')
                {
                    offset += entry.Count;
                }
                else if (entry.Code == 's')
                {
                    byte[] data = GetBytes(values[vi++]);
                    int copyLen = Math.Min(data.Length, entry.Count);
                    if (copyLen > 0) Array.Copy(data, 0, buf, offset, copyLen);
                    offset += entry.Count;
                }
                else if (entry.Code == 'p')
                {
                    byte[] data = GetBytes(values[vi++]);
                    int len = Math.Min(data.Length, entry.Count - 1);
                    if (len < 0) len = 0;
                    buf[offset] = (byte)len;
                    if (len > 0) Array.Copy(data, 0, buf, offset + 1, len);
                    offset += entry.Count;
                }
                else if (entry.Code == 'c')
                {
                    byte[] data = GetBytes(values[vi++]);
                    if (data.Length != 1) throw Error("c format requires a bytes object of length 1");
                    buf[offset] = data[0];
                    offset += 1;
                }
                else if (entry.Code == '?')
                {
                    buf[offset] = (byte)(values[vi++].IsTrue() ? 1 : 0);
                    offset += 1;
                }
                else if (entry.Code == 'f' || entry.Code == 'd' || entry.Code == 'e')
                {
                    for (int i = 0; i < entry.Count; i++)
                    {
                        PyObject val = values[vi++];
                        double d;
                        if (val is PyFloat pf) d = pf.Value;
                        else if (val is PyInt) d = (double)IntOf(val);
                        else throw Error("required argument is not a float");
                        WriteFloat(buf, offset, d, itemSize, le);
                        offset += itemSize;
                    }
                }
                else
                {
                    for (int i = 0; i < entry.Count; i++)
                    {
                        BigInteger iv = IntOf(values[vi++]);
                        CheckRange(entry.Code, iv);
                        WriteInt(buf, offset, iv, itemSize, le);
                        offset += itemSize;
                    }
                }
            }
            return buf;
        }

        private static PyObject[] UnpackValues(char byteOrder, List<FormatEntry> entries, byte[] buf, int offset)
        {
            bool le = IsLE(byteOrder);
            var result = new List<PyObject>();

            foreach (var entry in entries)
            {
                int itemSize = GetItemSize(entry.Code, byteOrder);
                int align = GetAlignment(entry.Code, byteOrder);
                if (byteOrder == '@' && align > 1)
                    offset += (align - (offset % align)) % align;

                if (entry.Code == 'x')
                {
                    offset += entry.Count;
                }
                else if (entry.Code == 's')
                {
                    byte[] data = new byte[entry.Count];
                    Array.Copy(buf, offset, data, 0, entry.Count);
                    result.Add(new PyBytes(data));
                    offset += entry.Count;
                }
                else if (entry.Code == 'p')
                {
                    int len = buf[offset];
                    if (len > entry.Count - 1) len = entry.Count - 1;
                    byte[] data = new byte[len];
                    if (len > 0) Array.Copy(buf, offset + 1, data, 0, len);
                    result.Add(new PyBytes(data));
                    offset += entry.Count;
                }
                else if (entry.Code == 'c')
                {
                    result.Add(new PyBytes(new byte[] { buf[offset] }));
                    offset += 1;
                }
                else if (entry.Code == '?')
                {
                    result.Add(PyBool.FromBool(buf[offset] != 0));
                    offset += 1;
                }
                else if (entry.Code == 'f' || entry.Code == 'd' || entry.Code == 'e')
                {
                    for (int i = 0; i < entry.Count; i++)
                    {
                        result.Add(new PyFloat(ReadFloat(buf, offset, itemSize, le)));
                        offset += itemSize;
                    }
                }
                else
                {
                    bool signed = "bhilqn".IndexOf(entry.Code) >= 0;
                    for (int i = 0; i < entry.Count; i++)
                    {
                        result.Add(new PyInt(ReadInt(buf, offset, itemSize, le, signed)));
                        offset += itemSize;
                    }
                }
            }
            return result.ToArray();
        }

        private static byte[] GetBytes(PyObject obj)
        {
            if (obj is PyBytes pb) return pb.Value;
            if (obj is PyByteArray pba) return pba.ToByteArrayCopy();
            if (obj is PyString ps) return Encoding.UTF8.GetBytes(ps.Value);
            throw Error("argument for 's' must be a bytes object");
        }

        private static byte[] GetBuffer(PyObject obj)
        {
            if (obj is PyBytes pb) return pb.Value;
            if (obj is PyByteArray pba) return pba.ToByteArrayCopy();
            throw PyTypeError.Create("a bytes-like object is required");
        }

        private static int OffsetArgument(PyObject[] args, int position, PyDict kwargs)
        {
            if (args.Length > position) return (int)IntOf(args[position]);
            return kwargs != null && kwargs.TryGetStoredValue(new PyString("offset"), out var offset)
                ? (int)IntOf(offset) : 0;
        }

        private static string GetFormat(PyObject obj)
        {
            if (obj is PyString ps) return ps.Value;
            if (obj is PyBytes pb) return Encoding.ASCII.GetString(pb.Value);
            throw PyTypeError.Create("format must be str or bytes");
        }

        internal static PyModule CreateStructModule()
        {
            var module = new PyModule("_struct");
            module.ModuleDict["__doc__"] = new PyString("Functions to convert between Python values and C structs.");

            ErrorType = PyExceptionSlots.NewException("struct", "error", PyType.ExceptionType);
            module.ModuleDict["error"] = ErrorType;

            module.ModuleDict["calcsize"] = new PyBuiltinFunction("calcsize", args =>
            {
                if (args.Length != 1) throw PyTypeError.Create("calcsize() takes exactly one argument");
                var (bo, entries) = ParseFormat(GetFormat(args[0]));
                return new PyInt(CalcSize(bo, entries));
            });

            module.ModuleDict["pack"] = new PyBuiltinFunction("pack", args =>
            {
                if (args.Length < 1) throw PyTypeError.Create("pack() requires at least one argument");
                var (bo, entries) = ParseFormat(GetFormat(args[0]));
                int expected = GetItemCount(entries);
                int got = args.Length - 1;
                if (expected != got) throw Error($"pack expected {expected} items for packing (got {got})");
                return new PyBytes(PackValues(bo, entries, args, 1));
            });

            module.ModuleDict["unpack"] = new PyBuiltinFunction("unpack", args =>
            {
                if (args.Length != 2) throw PyTypeError.Create("unpack() takes exactly two arguments");
                var (bo, entries) = ParseFormat(GetFormat(args[0]));
                byte[] buf = GetBuffer(args[1]);
                int size = CalcSize(bo, entries);
                if (buf.Length != size) throw Error($"unpack requires a buffer of {size} bytes");
                return new PyTuple(UnpackValues(bo, entries, buf, 0));
            });

            module.ModuleDict["pack_into"] = new PyBuiltinFunction("pack_into", args =>
            {
                if (args.Length < 3) throw PyTypeError.Create("pack_into() requires at least three arguments");
                var (bo, entries) = ParseFormat(GetFormat(args[0]));
                if (!(args[1] is PyByteArray pba)) throw PyTypeError.Create("pack_into() argument must be a bytearray");
                int offset = (int)IntOf(args[2]);
                int expected = GetItemCount(entries);
                int got = args.Length - 3;
                if (expected != got) throw Error($"pack_into expected {expected} items for packing (got {got})");
                int size = CalcSize(bo, entries);
                if (offset + size > pba.Length())
                    throw Error("pack_into requires a buffer of at least " + (offset + size) + " bytes");
                byte[] packed = PackValues(bo, entries, args, 3);
                pba.WriteAt(offset, packed, size);
                return PyNone.Instance;
            });

            module.ModuleDict["unpack_from"] = new PyBuiltinFunction("unpack_from", (args, kwargs) =>
            {
                if (args.Length < 2) throw PyTypeError.Create("unpack_from() requires at least two arguments");
                var (bo, entries) = ParseFormat(GetFormat(args[0]));
                byte[] buf = GetBuffer(args[1]);
                int offset = OffsetArgument(args, 2, kwargs);
                int size = CalcSize(bo, entries);
                if (offset + size > buf.Length)
                    throw Error($"unpack_from requires a buffer of at least {offset + size} bytes for unpacking {size} bytes at offset {offset} (actual buffer size is {buf.Length})");
                return new PyTuple(UnpackValues(bo, entries, buf, offset));
            });

            module.ModuleDict["iter_unpack"] = new PyBuiltinFunction("iter_unpack", args =>
            {
                if (args.Length != 2) throw PyTypeError.Create("iter_unpack() takes exactly two arguments");
                var (bo, entries) = ParseFormat(GetFormat(args[0]));
                byte[] buf = GetBuffer(args[1]);
                int size = CalcSize(bo, entries);
                if (size == 0) throw Error("iter_unpack() requires a non-empty format");
                if (buf.Length % size != 0)
                    throw Error($"iterative unpacking requires a buffer of a multiple of {size} bytes");
                var list = new PyList();
                for (int i = 0; i < buf.Length; i += size)
                    list.Append(new PyTuple(UnpackValues(bo, entries, buf, i)));
                return list;
            });

            module.ModuleDict["_clearcache"] = new PyBuiltinFunction("_clearcache", args => PyNone.Instance);

            StructType = new PyType("Struct", new[] { PyType.ObjectType }, "_struct");
            StructType.ConstructedByNewSlot = true;

            StructType.TypeDict["__new__"] = new PyMethodDescriptor("__new__", StructType, (cls, args, kwargs) =>
            {
                if (args.Length != 1) throw PyTypeError.Create("Struct() takes exactly one argument");
                string fmt = GetFormat(args[0]);
                var (bo, entries) = ParseFormat(fmt);
                return new PyStructObject(fmt, bo, entries);
            }, 1, 1);

            StructType.TypeDict["pack"] = new PyMethodDescriptor("pack", StructType, (self, args, kwargs) =>
            {
                var so = (PyStructObject)self;
                int expected = GetItemCount(so.Entries);
                if (expected != args.Length) throw Error($"pack expected {expected} items for packing (got {args.Length})");
                return new PyBytes(PackValues(so.ByteOrder, so.Entries, args, 0));
            }, 0, int.MaxValue);

            StructType.TypeDict["unpack"] = new PyMethodDescriptor("unpack", StructType, (self, args, kwargs) =>
            {
                var so = (PyStructObject)self;
                if (args.Length != 1) throw PyTypeError.Create("unpack() takes exactly one argument");
                byte[] buf = GetBuffer(args[0]);
                int size = CalcSize(so.ByteOrder, so.Entries);
                if (buf.Length != size) throw Error($"unpack requires a buffer of {size} bytes");
                return new PyTuple(UnpackValues(so.ByteOrder, so.Entries, buf, 0));
            }, 1, 1);

            StructType.TypeDict["pack_into"] = new PyMethodDescriptor("pack_into", StructType, (self, args, kwargs) =>
            {
                var so = (PyStructObject)self;
                if (args.Length < 2) throw PyTypeError.Create("pack_into() requires at least two arguments");
                if (!(args[0] is PyByteArray pba)) throw PyTypeError.Create("pack_into() argument must be a bytearray");
                int offset = (int)IntOf(args[1]);
                int expected = GetItemCount(so.Entries);
                int got = args.Length - 2;
                if (expected != got) throw Error($"pack_into expected {expected} items for packing (got {got})");
                int size = CalcSize(so.ByteOrder, so.Entries);
                if (offset + size > pba.Length())
                    throw Error("pack_into requires a buffer of at least " + (offset + size) + " bytes");
                byte[] packed = PackValues(so.ByteOrder, so.Entries, args, 2);
                pba.WriteAt(offset, packed, size);
                return PyNone.Instance;
            }, 2, int.MaxValue);

            StructType.TypeDict["unpack_from"] = new PyMethodDescriptor("unpack_from", StructType, (self, args, kwargs) =>
            {
                var so = (PyStructObject)self;
                if (args.Length < 1) throw PyTypeError.Create("unpack_from() requires at least one argument");
                byte[] buf = GetBuffer(args[0]);
                int offset = OffsetArgument(args, 1, kwargs);
                int size = CalcSize(so.ByteOrder, so.Entries);
                if (offset + size > buf.Length)
                    throw Error($"unpack_from requires a buffer of at least {offset + size} bytes for unpacking {size} bytes at offset {offset} (actual buffer size is {buf.Length})");
                return new PyTuple(UnpackValues(so.ByteOrder, so.Entries, buf, offset));
            }, 1, int.MaxValue);

            StructType.TypeDict["iter_unpack"] = new PyMethodDescriptor("iter_unpack", StructType, (self, args, kwargs) =>
            {
                var so = (PyStructObject)self;
                if (args.Length != 1) throw PyTypeError.Create("iter_unpack() takes exactly one argument");
                byte[] buf = GetBuffer(args[0]);
                int size = CalcSize(so.ByteOrder, so.Entries);
                if (size == 0) throw Error("iter_unpack() requires a non-empty format");
                if (buf.Length % size != 0)
                    throw Error($"iterative unpacking requires a buffer of a multiple of {size} bytes");
                var list = new PyList();
                for (int i = 0; i < buf.Length; i += size)
                    list.Append(new PyTuple(UnpackValues(so.ByteOrder, so.Entries, buf, i)));
                return list;
            }, 1, 1);

            module.ModuleDict["Struct"] = StructType;
            return module;
        }

        private sealed class PyStructObject : PyObject
        {
            public string Format;
            public char ByteOrder;
            public List<FormatEntry> Entries;

            public PyStructObject(string format, char byteOrder, List<FormatEntry> entries)
            {
                Format = format;
                ByteOrder = byteOrder;
                Entries = entries;
            }

            public override PyType GetPyType() => StructType;
            public override string GetTypeName() => "Struct";

            public override PyObject GetAttribute(string name) => name switch
            {
                "format" => new PyString(Format),
                "size" => new PyInt(CalcSize(ByteOrder, Entries)),
                _ => base.GetAttribute(name)
            };
        }
    }
}