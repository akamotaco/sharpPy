using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace SharpPy.Modules
{
    /// <summary>
    /// Modules/_struct.c (CPython v3.12.15): the format tables, prepare_s, the Struct type, the
    /// unpack iterator and the module functions over the cache of compiled formats.
    /// </summary>
    internal static class _StructModule
    {
        internal static readonly PyType StructType;
        internal static readonly PyType UnpackIterType;
        internal static readonly PyType ErrorType;

        // _structmodulestate.cache, cleared once it holds MAXCACHE formats.
        private const int MAXCACHE = 100;
        private static PyDict _cache;

        internal static PythonException Error(string message) => PyExceptionSlots.Raise(ErrorType, message);

        internal delegate PyObject Unpacker(ReadOnlySpan<byte> p, FormatDef f);
        internal delegate void Packer(Span<byte> p, PyObject v, FormatDef f);

        /// <summary>formatdef: the code, its size and alignment, and how it reads and writes.</summary>
        internal sealed class FormatDef
        {
            internal readonly char Format;
            internal readonly long Size;
            internal readonly long Alignment;
            internal Unpacker Unpack;
            internal Packer Pack;

            internal FormatDef(char format, long size, long alignment, Unpacker unpack = null, Packer pack = null)
            {
                Format = format;
                Size = size;
                Alignment = alignment;
                Unpack = unpack;
                Pack = pack;
            }
        }

        /// <summary>formatcode</summary>
        internal struct FormatCode
        {
            internal FormatDef Def;
            internal long Offset;
            internal long Size;
            internal long Repeat;
        }

        // A C long is 32 bits on Windows (LLP64) and 64 elsewhere.
        private static readonly int SizeofLong = OperatingSystem.IsWindows() ? 4 : 8;

        private static readonly FormatDef[] NativeTable;
        private static readonly FormatDef[] BigEndianTable;
        private static readonly FormatDef[] LilEndianTable;

        // ---- integer helpers ----

        // get_pylong: an int, or what __index__ makes of the value.
        private static BigInteger GetPyLong(PyObject v)
        {
            if (ItertoolsModule.LongValue(v, out var number))
                return number;
            if (!PyObject.IndexCheck(v))
                throw Error("required argument is not an integer");
            return PyObject.TryGetIndex(v).Value;
        }

        private static readonly BigInteger CLongMin = OperatingSystem.IsWindows() ? int.MinValue : long.MinValue;
        private static readonly BigInteger CLongMax = OperatingSystem.IsWindows() ? int.MaxValue : long.MaxValue;
        private static readonly BigInteger CULongMax = OperatingSystem.IsWindows() ? uint.MaxValue : ulong.MaxValue;

        // The OverflowError of each PyLong_As* conversion, which the pack functions turn into a range error.
        private sealed class COverflow : Exception
        {
        }

        // get_long: PyLong_AsLong
        private static long GetLong(PyObject v)
        {
            var x = GetPyLong(v);
            if (x < CLongMin || x > CLongMax)
                throw new COverflow();
            return (long)x;
        }

        // get_ulong: PyLong_AsUnsignedLong
        private static ulong GetULong(PyObject v)
        {
            var x = GetPyLong(v);
            if (x.Sign < 0 || x > CULongMax)
                throw new COverflow();
            return (ulong)x;
        }

        // get_longlong and get_ssize_t: PyLong_AsLongLong
        private static long GetLongLong(PyObject v)
        {
            var x = GetPyLong(v);
            if (x < long.MinValue || x > long.MaxValue)
                throw new COverflow();
            return (long)x;
        }

        // get_ulonglong and get_size_t: PyLong_AsUnsignedLongLong
        private static ulong GetULongLong(PyObject v)
        {
            var x = GetPyLong(v);
            if (x.Sign < 0 || x > ulong.MaxValue)
                throw new COverflow();
            return (ulong)x;
        }

        // _range_error
        private static PythonException RangeError(FormatDef f, bool isUnsigned)
        {
            ulong ulargest = ulong.MaxValue >> (int)((8 - f.Size) * 8);
            if (isUnsigned)
                return Error($"'{f.Format}' format requires 0 <= number <= {ulargest}");
            long largest = (long)(ulargest >> 1);
            return Error($"'{f.Format}' format requires {~largest} <= number <= {largest}");
        }

        // A pack function's own range test over get_long and friends: an overflow is the range error.
        private static T Ranged<T>(FormatDef f, bool isUnsigned, Func<T> get)
        {
            try
            {
                return get();
            }
            catch (COverflow)
            {
                throw RangeError(f, isUnsigned);
            }
        }

        // PyFloat_AsDouble, whose failure the pack functions name themselves.
        private static double GetDouble(PyObject v)
        {
            try
            {
                return PyMathFunction.RealNumber(v);
            }
            catch (PythonException)
            {
                throw Error("required argument is not a float");
            }
        }

        private static PyObject Long(long value) => PyInt.From(value);
        private static PyObject ULong(ulong value) => PyInt.From(value);

        private static long ReadNative(ReadOnlySpan<byte> p, int size)
        {
            long x = 0;
            for (int i = size - 1; i >= 0; i--)
                x = (x << 8) | p[i];
            return x;
        }

        private static void WriteNative(Span<byte> p, ulong x, int size)
        {
            for (int i = 0; i < size; i++)
            {
                p[i] = (byte)(x & 0xff);
                x >>= 8;
            }
        }

        // ---- native routines (little-endian memory, as on every .NET target) ----

        private static PyObject NuChar(ReadOnlySpan<byte> p, FormatDef f) => PyBytes.From(new[] { p[0] });
        private static PyObject NuByte(ReadOnlySpan<byte> p, FormatDef f) => Long((sbyte)p[0]);
        private static PyObject NuUByte(ReadOnlySpan<byte> p, FormatDef f) => Long(p[0]);
        private static PyObject NuShort(ReadOnlySpan<byte> p, FormatDef f) => Long((short)ReadNative(p, 2));
        private static PyObject NuUShort(ReadOnlySpan<byte> p, FormatDef f) => Long((ushort)ReadNative(p, 2));
        private static PyObject NuInt(ReadOnlySpan<byte> p, FormatDef f) => Long((int)ReadNative(p, 4));
        private static PyObject NuUInt(ReadOnlySpan<byte> p, FormatDef f) => ULong((uint)ReadNative(p, 4));
        private static PyObject NuLong(ReadOnlySpan<byte> p, FormatDef f) =>
            SizeofLong == 4 ? Long((int)ReadNative(p, 4)) : Long(ReadNative(p, 8));
        private static PyObject NuULong(ReadOnlySpan<byte> p, FormatDef f) =>
            SizeofLong == 4 ? ULong((uint)ReadNative(p, 4)) : ULong((ulong)ReadNative(p, 8));
        private static PyObject NuSsizeT(ReadOnlySpan<byte> p, FormatDef f) => Long(ReadNative(p, 8));
        private static PyObject NuSizeT(ReadOnlySpan<byte> p, FormatDef f) => ULong((ulong)ReadNative(p, 8));
        private static PyObject NuLongLong(ReadOnlySpan<byte> p, FormatDef f) => Long(ReadNative(p, 8));
        private static PyObject NuULongLong(ReadOnlySpan<byte> p, FormatDef f) => ULong((ulong)ReadNative(p, 8));
        // nu_bool: memcmp against a false _Bool
        private static PyObject NuBool(ReadOnlySpan<byte> p, FormatDef f) => PyBool.FromBool(p[0] != 0);
        private static PyObject NuHalfFloat(ReadOnlySpan<byte> p, FormatDef f) => new PyFloat(PyFloatPack.Unpack2(p, true));
        private static PyObject NuFloat(ReadOnlySpan<byte> p, FormatDef f) => new PyFloat(BitConverter.Int32BitsToSingle((int)ReadNative(p, 4)));
        private static PyObject NuDouble(ReadOnlySpan<byte> p, FormatDef f) => new PyFloat(BitConverter.Int64BitsToDouble(ReadNative(p, 8)));
        // nu_void_p: PyLong_FromVoidPtr, an unsigned value
        private static PyObject NuVoidP(ReadOnlySpan<byte> p, FormatDef f) => ULong((ulong)ReadNative(p, 8));

        private static void NpByte(Span<byte> p, PyObject v, FormatDef f)
        {
            long x = Ranged(f, false, () => GetLong(v));
            if (x < -128 || x > 127)
                throw RangeError(f, false);
            p[0] = (byte)x;
        }

        private static void NpUByte(Span<byte> p, PyObject v, FormatDef f)
        {
            long x = Ranged(f, true, () => GetLong(v));
            if (x < 0 || x > 255)
                throw RangeError(f, true);
            p[0] = (byte)x;
        }

        private static void NpChar(Span<byte> p, PyObject v, FormatDef f)
        {
            if (v is not PyBytes bytes || bytes.Value.Length != 1)
                throw Error("char format requires a bytes object of length 1");
            p[0] = bytes.Value[0];
        }

        private static void NpShort(Span<byte> p, PyObject v, FormatDef f)
        {
            long x = Ranged(f, false, () => GetLong(v));
            if (x < short.MinValue || x > short.MaxValue)
                throw RangeError(f, false);
            WriteNative(p, (ulong)x, 2);
        }

        private static void NpUShort(Span<byte> p, PyObject v, FormatDef f)
        {
            long x = Ranged(f, true, () => GetLong(v));
            if (x < 0 || x > ushort.MaxValue)
                throw RangeError(f, true);
            WriteNative(p, (ulong)x, 2);
        }

        private static void NpInt(Span<byte> p, PyObject v, FormatDef f)
        {
            long x = Ranged(f, false, () => GetLong(v));
            if (SizeofLong > 4 && (x < int.MinValue || x > int.MaxValue))
                throw RangeError(f, false);
            WriteNative(p, (ulong)x, 4);
        }

        private static void NpUInt(Span<byte> p, PyObject v, FormatDef f)
        {
            ulong x = Ranged(f, true, () => GetULong(v));
            if (SizeofLong > 4 && x > uint.MaxValue)
                throw RangeError(f, true);
            WriteNative(p, x, 4);
        }

        private static void NpLong(Span<byte> p, PyObject v, FormatDef f) =>
            WriteNative(p, (ulong)Ranged(f, false, () => GetLong(v)), SizeofLong);

        private static void NpULong(Span<byte> p, PyObject v, FormatDef f) =>
            WriteNative(p, Ranged(f, true, () => GetULong(v)), SizeofLong);

        private static void NpSsizeT(Span<byte> p, PyObject v, FormatDef f) =>
            WriteNative(p, (ulong)Ranged(f, false, () => GetLongLong(v)), 8);

        private static void NpSizeT(Span<byte> p, PyObject v, FormatDef f) =>
            WriteNative(p, Ranged(f, true, () => GetULongLong(v)), 8);

        private static void NpLongLong(Span<byte> p, PyObject v, FormatDef f)
        {
            long x;
            try
            {
                x = GetLongLong(v);
            }
            catch (COverflow)
            {
                throw Error($"'{f.Format}' format requires {long.MinValue} <= number <= {long.MaxValue}");
            }
            WriteNative(p, (ulong)x, 8);
        }

        private static void NpULongLong(Span<byte> p, PyObject v, FormatDef f)
        {
            ulong x;
            try
            {
                x = GetULongLong(v);
            }
            catch (COverflow)
            {
                throw Error($"'{f.Format}' format requires 0 <= number <= {ulong.MaxValue}");
            }
            WriteNative(p, x, 8);
        }

        private static void NpBool(Span<byte> p, PyObject v, FormatDef f) => p[0] = v.IsTrue() ? (byte)1 : (byte)0;

        private static void NpHalfFloat(Span<byte> p, PyObject v, FormatDef f) => PyFloatPack.Pack2(GetDouble(v), p, true);

        // np_float: the plain cast, with no overflow test.
        private static void NpFloat(Span<byte> p, PyObject v, FormatDef f) =>
            WriteNative(p, (uint)BitConverter.SingleToInt32Bits((float)GetDouble(v)), 4);

        private static void NpDouble(Span<byte> p, PyObject v, FormatDef f) =>
            WriteNative(p, (ulong)BitConverter.DoubleToInt64Bits(GetDouble(v)), 8);

        // np_void_p: PyLong_AsVoidPtr, a negative value through PyLong_AsLongLong.
        private static void NpVoidP(Span<byte> p, PyObject v, FormatDef f)
        {
            var x = GetPyLong(v);
            ulong bits;
            if (x.Sign < 0)
            {
                if (x < long.MinValue)
                    throw PyOverflowError.Create("Python int too large to convert to C long long");
                bits = (ulong)(long)x;
            }
            else
            {
                if (x > ulong.MaxValue)
                    throw PyOverflowError.Create("Python int too large to convert to C unsigned long long");
                bits = (ulong)x;
            }
            WriteNative(p, bits, 8);
        }

        // ---- standard size routines ----

        private static ulong ReadBig(ReadOnlySpan<byte> p, long size)
        {
            ulong x = 0;
            for (int i = 0; i < size; i++)
                x = (x << 8) | p[i];
            return x;
        }

        private static ulong ReadLittle(ReadOnlySpan<byte> p, long size)
        {
            ulong x = 0;
            for (int i = (int)size - 1; i >= 0; i--)
                x = (x << 8) | p[i];
            return x;
        }

        private static PyObject BuShort(ReadOnlySpan<byte> p, FormatDef f) => Long((short)ReadBig(p, 2));
        private static PyObject BuInt(ReadOnlySpan<byte> p, FormatDef f) => Long((int)ReadBig(p, 4));
        private static PyObject BuUInt(ReadOnlySpan<byte> p, FormatDef f) => ULong(ReadBig(p, f.Size));
        private static PyObject BuLongLong(ReadOnlySpan<byte> p, FormatDef f) => Long((long)ReadBig(p, 8));
        private static PyObject BuULongLong(ReadOnlySpan<byte> p, FormatDef f) => ULong(ReadBig(p, f.Size));
        private static PyObject BuHalfFloat(ReadOnlySpan<byte> p, FormatDef f) => new PyFloat(PyFloatPack.Unpack2(p, false));
        private static PyObject BuFloat(ReadOnlySpan<byte> p, FormatDef f) => new PyFloat(PyFloatPack.Unpack4(p, false));
        private static PyObject BuDouble(ReadOnlySpan<byte> p, FormatDef f) => new PyFloat(PyFloatPack.Unpack8(p, false));
        private static PyObject BuBool(ReadOnlySpan<byte> p, FormatDef f) => PyBool.FromBool(p[0] != 0);

        private static PyObject LuShort(ReadOnlySpan<byte> p, FormatDef f) => Long((short)ReadLittle(p, 2));
        private static PyObject LuInt(ReadOnlySpan<byte> p, FormatDef f) => Long((int)ReadLittle(p, 4));
        private static PyObject LuUInt(ReadOnlySpan<byte> p, FormatDef f) => ULong(ReadLittle(p, f.Size));
        private static PyObject LuLongLong(ReadOnlySpan<byte> p, FormatDef f) => Long((long)ReadLittle(p, 8));
        private static PyObject LuULongLong(ReadOnlySpan<byte> p, FormatDef f) => ULong(ReadLittle(p, f.Size));
        private static PyObject LuHalfFloat(ReadOnlySpan<byte> p, FormatDef f) => new PyFloat(PyFloatPack.Unpack2(p, true));
        private static PyObject LuFloat(ReadOnlySpan<byte> p, FormatDef f) => new PyFloat(PyFloatPack.Unpack4(p, true));
        private static PyObject LuDouble(ReadOnlySpan<byte> p, FormatDef f) => new PyFloat(PyFloatPack.Unpack8(p, true));

        // bp_int and lp_int: get_long, then the range of a 2 byte code when a long is wider.
        private static long StdInt(PyObject v, FormatDef f)
        {
            long x = Ranged(f, false, () => GetLong(v));
            long i = f.Size;
            if (i != SizeofLong)
            {
                if (i == 2 && (x < -32768 || x > 32767))
                    throw RangeError(f, false);
                if (SizeofLong != 4 && i == 4 && (x < -2147483648L || x > 2147483647L))
                    throw RangeError(f, false);
            }
            return x;
        }

        // bp_uint and lp_uint
        private static ulong StdUInt(PyObject v, FormatDef f)
        {
            ulong x = Ranged(f, true, () => GetULong(v));
            long i = f.Size;
            if (i != SizeofLong)
            {
                ulong maxint = 1UL << (int)(i * 8);
                if (x >= maxint)
                    throw RangeError(f, true);
            }
            return x;
        }

        private static void WriteBig(Span<byte> p, ulong x, long size)
        {
            for (int i = (int)size - 1; i >= 0; i--)
            {
                p[i] = (byte)(x & 0xff);
                x >>= 8;
            }
        }

        private static void BpInt(Span<byte> p, PyObject v, FormatDef f) => WriteBig(p, (ulong)StdInt(v, f), f.Size);
        private static void BpUInt(Span<byte> p, PyObject v, FormatDef f) => WriteBig(p, StdUInt(v, f), f.Size);
        private static void LpInt(Span<byte> p, PyObject v, FormatDef f) => WriteNative(p, (ulong)StdInt(v, f), (int)f.Size);
        private static void LpUInt(Span<byte> p, PyObject v, FormatDef f) => WriteNative(p, StdUInt(v, f), (int)f.Size);

        // bp_longlong and lp_longlong: _PyLong_AsByteArray, whose every failure is the range message.
        private static long StdLongLong(PyObject v, FormatDef f)
        {
            var x = GetPyLong(v);
            if (x < long.MinValue || x > long.MaxValue)
                throw Error($"'{f.Format}' format requires {long.MinValue} <= number <= {long.MaxValue}");
            return (long)x;
        }

        private static ulong StdULongLong(PyObject v, FormatDef f)
        {
            var x = GetPyLong(v);
            if (x.Sign < 0 || x > ulong.MaxValue)
                throw Error($"'{f.Format}' format requires 0 <= number <= {ulong.MaxValue}");
            return (ulong)x;
        }

        private static void BpLongLong(Span<byte> p, PyObject v, FormatDef f) => WriteBig(p, (ulong)StdLongLong(v, f), 8);
        private static void BpULongLong(Span<byte> p, PyObject v, FormatDef f) => WriteBig(p, StdULongLong(v, f), 8);
        private static void LpLongLong(Span<byte> p, PyObject v, FormatDef f) => WriteNative(p, (ulong)StdLongLong(v, f), 8);
        private static void LpULongLong(Span<byte> p, PyObject v, FormatDef f) => WriteNative(p, StdULongLong(v, f), 8);

        private static void BpHalfFloat(Span<byte> p, PyObject v, FormatDef f) => PyFloatPack.Pack2(GetDouble(v), p, false);
        private static void BpFloat(Span<byte> p, PyObject v, FormatDef f) => PyFloatPack.Pack4(GetDouble(v), p, false);
        private static void BpDouble(Span<byte> p, PyObject v, FormatDef f) => PyFloatPack.Pack8(GetDouble(v), p, false);
        private static void BpBool(Span<byte> p, PyObject v, FormatDef f) => p[0] = v.IsTrue() ? (byte)1 : (byte)0;
        private static void LpHalfFloat(Span<byte> p, PyObject v, FormatDef f) => PyFloatPack.Pack2(GetDouble(v), p, true);
        private static void LpFloat(Span<byte> p, PyObject v, FormatDef f) => PyFloatPack.Pack4(GetDouble(v), p, true);
        private static void LpDouble(Span<byte> p, PyObject v, FormatDef f) => PyFloatPack.Pack8(GetDouble(v), p, true);

        static _StructModule()
        {
            int longAlign = SizeofLong;
            NativeTable = new[]
            {
                new FormatDef('x', 1, 0),
                new FormatDef('b', 1, 0, NuByte, NpByte),
                new FormatDef('B', 1, 0, NuUByte, NpUByte),
                new FormatDef('c', 1, 0, NuChar, NpChar),
                new FormatDef('s', 1, 0),
                new FormatDef('p', 1, 0),
                new FormatDef('h', 2, 2, NuShort, NpShort),
                new FormatDef('H', 2, 2, NuUShort, NpUShort),
                new FormatDef('i', 4, 4, NuInt, NpInt),
                new FormatDef('I', 4, 4, NuUInt, NpUInt),
                new FormatDef('l', SizeofLong, longAlign, NuLong, NpLong),
                new FormatDef('L', SizeofLong, longAlign, NuULong, NpULong),
                new FormatDef('n', 8, 8, NuSsizeT, NpSsizeT),
                new FormatDef('N', 8, 8, NuSizeT, NpSizeT),
                new FormatDef('q', 8, 8, NuLongLong, NpLongLong),
                new FormatDef('Q', 8, 8, NuULongLong, NpULongLong),
                new FormatDef('?', 1, 1, NuBool, NpBool),
                new FormatDef('e', 2, 2, NuHalfFloat, NpHalfFloat),
                new FormatDef('f', 4, 4, NuFloat, NpFloat),
                new FormatDef('d', 8, 8, NuDouble, NpDouble),
                new FormatDef('P', 8, 8, NuVoidP, NpVoidP),
            };
            BigEndianTable = new[]
            {
                new FormatDef('x', 1, 0),
                new FormatDef('b', 1, 0, NuByte, NpByte),
                new FormatDef('B', 1, 0, NuUByte, NpUByte),
                new FormatDef('c', 1, 0, NuChar, NpChar),
                new FormatDef('s', 1, 0),
                new FormatDef('p', 1, 0),
                new FormatDef('h', 2, 0, BuShort, BpInt),
                new FormatDef('H', 2, 0, BuUInt, BpUInt),
                new FormatDef('i', 4, 0, BuInt, BpInt),
                new FormatDef('I', 4, 0, BuUInt, BpUInt),
                new FormatDef('l', 4, 0, BuInt, BpInt),
                new FormatDef('L', 4, 0, BuUInt, BpUInt),
                new FormatDef('q', 8, 0, BuLongLong, BpLongLong),
                new FormatDef('Q', 8, 0, BuULongLong, BpULongLong),
                new FormatDef('?', 1, 0, BuBool, BpBool),
                new FormatDef('e', 2, 0, BuHalfFloat, BpHalfFloat),
                new FormatDef('f', 4, 0, BuFloat, BpFloat),
                new FormatDef('d', 8, 0, BuDouble, BpDouble),
            };
            LilEndianTable = new[]
            {
                new FormatDef('x', 1, 0),
                new FormatDef('b', 1, 0, NuByte, NpByte),
                new FormatDef('B', 1, 0, NuUByte, NpUByte),
                new FormatDef('c', 1, 0, NuChar, NpChar),
                new FormatDef('s', 1, 0),
                new FormatDef('p', 1, 0),
                new FormatDef('h', 2, 0, LuShort, LpInt),
                new FormatDef('H', 2, 0, LuUInt, LpUInt),
                new FormatDef('i', 4, 0, LuInt, LpInt),
                new FormatDef('I', 4, 0, LuUInt, LpUInt),
                new FormatDef('l', 4, 0, LuInt, LpInt),
                new FormatDef('L', 4, 0, LuUInt, LpUInt),
                new FormatDef('q', 8, 0, LuLongLong, LpLongLong),
                new FormatDef('Q', 8, 0, LuULongLong, LpULongLong),
                // Std rep not endian dep, but potentially different from native rep -- reuse bx_bool funcs.
                new FormatDef('?', 1, 0, BuBool, BpBool),
                new FormatDef('e', 2, 0, LuHalfFloat, LpHalfFloat),
                new FormatDef('f', 4, 0, LuFloat, LpFloat),
                new FormatDef('d', 8, 0, LuDouble, LpDouble),
            };

            // _structmodule_exec: the little-endian table takes the native functions wherever the
            // size matches, except for float, double and _Bool.
            int other = 0;
            for (int n = 0; n < NativeTable.Length && other < LilEndianTable.Length; n++)
            {
                var native = NativeTable[n];
                for (int ptr = other; ptr < LilEndianTable.Length; ptr++)
                {
                    var entry = LilEndianTable[ptr];
                    if (entry.Format != native.Format)
                        continue;
                    if (ptr == other)
                        other++;
                    if (entry.Size != native.Size || entry.Format is 'd' or 'f' or '?')
                        break;
                    entry.Pack = native.Pack;
                    entry.Unpack = native.Unpack;
                    break;
                }
            }

            ErrorType = PyExceptionSlots.NewException("struct", "error", PyType.ExceptionType);
            StructType = BuildStructType();
            UnpackIterType = BuildUnpackIterType();
        }

        // whichtable
        private static FormatDef[] WhichTable(byte[] fmt, ref int pos)
        {
            if (pos < fmt.Length)
            {
                switch ((char)fmt[pos])
                {
                    case '<':
                        pos++;
                        return LilEndianTable;
                    case '>':
                    case '!': // Network byte order is big-endian
                        pos++;
                        return BigEndianTable;
                    case '=': // Host byte order -- different from native in alignment!
                        pos++;
                        return BitConverter.IsLittleEndian ? LilEndianTable : BigEndianTable;
                    case '@':
                        pos++;
                        return NativeTable;
                }
            }
            return NativeTable;
        }

        // getentry
        private static FormatDef GetEntry(char c, FormatDef[] table)
        {
            foreach (var f in table)
                if (f.Format == c)
                    return f;
            throw Error("bad char in struct format");
        }

        // align: -1 on overflow
        private static long Align(long size, char c, FormatDef e)
        {
            if (e.Format == c && e.Alignment != 0 && size > 0)
            {
                long extra = (e.Alignment - 1) - (size - 1) % e.Alignment;
                if (extra > long.MaxValue - size)
                    return -1;
                size += extra;
            }
            return size;
        }

        private static bool IsSpace(byte c) => c is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' or 0x0b or 0x0c;

        // prepare_s
        internal static void Prepare(StructObject self)
        {
            var fmtBytes = self.Format.Value;
            if (Array.IndexOf(fmtBytes, (byte)0) >= 0)
                throw Error("embedded null character");
            int start = 0;
            var f = WhichTable(fmtBytes, ref start);

            long size = 0, len = 0, ncodes = 0;
            int s = start;
            while (s < fmtBytes.Length)
            {
                char c = (char)fmtBytes[s++];
                if (IsSpace((byte)c))
                    continue;
                long num;
                if (c >= '0' && c <= '9')
                {
                    num = c - '0';
                    while (true)
                    {
                        c = s < fmtBytes.Length ? (char)fmtBytes[s++] : '\0';
                        if (c < '0' || c > '9')
                            break;
                        // overflow-safe version of num*10 + (c - '0') > PY_SSIZE_T_MAX
                        if (num >= long.MaxValue / 10 && (num > long.MaxValue / 10 || (c - '0') > long.MaxValue % 10))
                            throw Error("total struct size too long");
                        num = num * 10 + (c - '0');
                    }
                    if (c == '\0')
                        throw Error("repeat count given without format specifier");
                }
                else
                    num = 1;

                var e = GetEntry(c, f);
                switch (c)
                {
                    case 's':
                    case 'p': len++; ncodes++; break;
                    case 'x': break;
                    default: len += num; if (num != 0) ncodes++; break;
                }
                long itemsize = e.Size;
                size = Align(size, c, e);
                if (size == -1)
                    throw Error("total struct size too long");
                // if (size + num * itemsize > PY_SSIZE_T_MAX) { ... }
                if (num > (long.MaxValue - size) / itemsize)
                    throw Error("total struct size too long");
                size += num * itemsize;
            }

            // check for overflow, then PyMem_Malloc of the codes
            if (ncodes + 1 > Array.MaxLength)
                throw PyMemoryError.Create();

            var codes = new FormatCode[ncodes + 1];
            int k = 0;
            s = start;
            size = 0;
            while (s < fmtBytes.Length)
            {
                char c = (char)fmtBytes[s++];
                if (IsSpace((byte)c))
                    continue;
                long num;
                if (c >= '0' && c <= '9')
                {
                    num = c - '0';
                    while (s < fmtBytes.Length && fmtBytes[s] >= '0' && fmtBytes[s] <= '9')
                        num = num * 10 + (fmtBytes[s++] - '0');
                    c = (char)fmtBytes[s++];
                }
                else
                    num = 1;

                var e = GetEntry(c, f);
                size = Align(size, c, e);
                if (c == 's' || c == 'p')
                {
                    codes[k++] = new FormatCode { Offset = size, Size = num, Def = e, Repeat = 1 };
                    size += num;
                }
                else if (c == 'x')
                    size += num;
                else if (num != 0)
                {
                    codes[k++] = new FormatCode { Offset = size, Size = e.Size, Def = e, Repeat = num };
                    size += e.Size * num;
                }
            }
            codes[k] = new FormatCode { Def = null, Offset = size, Size = 0, Repeat = 0 };

            self.Size = size;
            self.Len = len;
            self.Codes = codes;
        }

        // s_unpack_internal
        internal static PyTuple UnpackInternal(StructObject so, ReadOnlySpan<byte> startfrom)
        {
            var result = new PyObject[so.Len];
            int i = 0;
            foreach (var code in so.Codes)
            {
                var e = code.Def;
                if (e == null)
                    break;
                long res = code.Offset;
                for (long j = code.Repeat; j > 0; j--)
                {
                    PyObject v;
                    if (e.Format == 's')
                        v = PyBytes.From(startfrom.Slice((int)res, (int)code.Size).ToArray());
                    else if (e.Format == 'p')
                    {
                        long n;
                        if (code.Size == 0)
                            n = 0;
                        else
                        {
                            n = startfrom[(int)res];
                            if (n >= code.Size)
                                n = code.Size - 1;
                        }
                        v = PyBytes.From(n == 0 ? Array.Empty<byte>() : startfrom.Slice((int)res + 1, (int)n).ToArray());
                    }
                    else
                        v = e.Unpack(startfrom.Slice((int)res, (int)e.Size), e);
                    result[i++] = v;
                    res += code.Size;
                }
            }
            return PyTuple.From(result);
        }

        // s_pack_internal
        internal static void PackInternal(StructObject so, PyObject[] args, int offset, Span<byte> buf)
        {
            buf.Slice(0, (int)so.Size).Clear();
            int i = offset;
            foreach (var code in so.Codes)
            {
                var e = code.Def;
                if (e == null)
                    break;
                long res = code.Offset;
                for (long j = code.Repeat; j > 0; j--)
                {
                    var v = args[i++];
                    if (e.Format == 's' || e.Format == 'p')
                    {
                        byte[] p = v switch
                        {
                            PyBytes bytes => bytes.Value,
                            PyByteArray array => array.ToByteArrayCopy(),
                            _ => throw Error($"argument for '{e.Format}' must be a bytes object"),
                        };
                        long n = p.Length;
                        if (e.Format == 's')
                        {
                            if (n > code.Size)
                                n = code.Size;
                            if (n > 0)
                                p.AsSpan(0, (int)n).CopyTo(buf.Slice((int)res));
                        }
                        else
                        {
                            if (code.Size == 0)
                                n = 0;
                            else if (n > code.Size - 1)
                                n = code.Size - 1;
                            if (n > 0)
                                p.AsSpan(0, (int)n).CopyTo(buf.Slice((int)res + 1));
                            if (n > 255)
                                n = 255;
                            // A zero-width code has no count byte of its own to write.
                            if (code.Size > 0)
                                buf[(int)res] = (byte)n;
                        }
                    }
                    else
                    {
                        try
                        {
                            e.Pack(buf.Slice((int)res, (int)e.Size), v, e);
                        }
                        catch (PythonException ex) when (ex.PyException is PyOverflowError && ItertoolsModule.LongValue(v, out _))
                        {
                            throw Error("int too large to convert");
                        }
                    }
                    res += code.Size;
                }
            }
        }

        // PyObject_GetBuffer(PyBUF_SIMPLE), then the C-contiguous check the clinic makes for a Py_buffer.
        internal static PyMemoryView SimpleBuffer(PyObject obj, string fname, string displayname)
        {
            var view = obj.GetBuffer(0);
            if (!view.IsCContiguous)
            {
                // memory_getbuf refuses a simple buffer of a strided view before the clinic's own test.
                view.Release();
                throw PyBufferError.Create("memoryview: underlying buffer is not C-contiguous");
            }
            return view;
        }

        // s_pack
        internal static PyObject Pack(StructObject so, PyObject[] args, int offset)
        {
            long nargs = args.Length - offset;
            if (nargs != so.Len)
                throw Error($"pack expected {so.Len} items for packing (got {nargs})");
            if (so.Size > Array.MaxLength)
                throw PyMemoryError.Create();
            var buf = new byte[so.Size];
            PackInternal(so, args, offset, buf);
            return PyBytes.From(buf);
        }

        // s_pack_into
        internal static PyObject PackInto(StructObject so, PyObject[] args, int first)
        {
            long nargs = args.Length - first;
            if (nargs != so.Len + 2)
            {
                if (nargs == 0)
                    throw Error("pack_into expected buffer argument");
                if (nargs == 1)
                    throw Error("pack_into expected offset argument");
                throw Error($"pack_into expected {so.Len} items for packing (got {nargs - 2})");
            }
            // Extract a writable memory buffer from the first argument: "w*".
            var target = args[first];
            var buffer = target.SupportsBuffer() ? target.GetBuffer(1) : null;
            if (buffer == null || buffer.ReadOnly)
            {
                buffer?.Release();
                throw PyTypeError.Create($"argument must be read-write bytes-like object, not {target.GetPyType().Name}");
            }
            try
            {
                if (!buffer.IsCContiguous)
                    throw PyTypeError.Create($"argument must be read-write bytes-like object, not {target.GetPyType().Name}");
                long len = buffer.ByteLength;
                // Extract the offset from the first argument
                long offset = IndexSsize(args[first + 1], index: true);
                // Support negative offsets.
                if (offset < 0)
                {
                    // Check that negative offset is low enough to fit data
                    if (offset + so.Size > 0)
                        throw Error($"no space to pack {so.Size} bytes at offset {offset}");
                    // Check that negative offset is not crossing buffer boundary
                    if (offset + len < 0)
                        throw Error($"offset {offset} out of range for {len}-byte buffer");
                    offset += len;
                }
                // Check boundaries
                if (len - offset < so.Size)
                    throw Error($"pack_into requires a buffer of at least {(ulong)so.Size + (ulong)offset} bytes for " +
                                $"packing {so.Size} bytes at offset {offset} (actual buffer size is {len})");
                var packed = new byte[so.Size];
                PackInternal(so, args, first + 2, packed);
                buffer.WriteBytes(offset, packed);
            }
            finally
            {
                buffer.Release();
            }
            return PyNone.Instance;
        }

        // PyNumber_AsSsize_t(value, IndexError or OverflowError)
        private static long IndexSsize(PyObject value, bool index)
        {
            var number = PyObject.TryGetIndex(value)
                ?? throw PyTypeError.Create($"'{value.GetTypeName()}' object cannot be interpreted as an integer");
            if (number.Value < long.MinValue || number.Value > long.MaxValue)
            {
                string message = $"cannot fit '{value.GetTypeName()}' into an index-sized integer";
                throw index ? PyIndexError.Create(message) : PyOverflowError.Create(message);
            }
            return (long)number.Value;
        }

        // Struct_unpack_impl
        internal static PyObject Unpack(StructObject so, PyObject arg, string fname, string displayname)
        {
            var buffer = SimpleBuffer(arg, fname, displayname);
            try
            {
                var span = buffer.ContiguousBytes();
                if (span.Length != so.Size)
                    throw Error($"unpack requires a buffer of {so.Size} bytes");
                return UnpackInternal(so, span);
            }
            finally
            {
                buffer.Release();
            }
        }

        // Struct_unpack_from_impl
        internal static PyObject UnpackFrom(StructObject so, PyObject arg, long offset)
        {
            var buffer = SimpleBuffer(arg, "unpack_from", "argument 'buffer'");
            try
            {
                var span = buffer.ContiguousBytes();
                long len = span.Length;
                if (offset < 0)
                {
                    if (offset + so.Size > 0)
                        throw Error($"not enough data to unpack {so.Size} bytes at offset {offset}");
                    if (offset + len < 0)
                        throw Error($"offset {offset} out of range for {len}-byte buffer");
                    offset += len;
                }
                if (len - offset < so.Size)
                    throw Error($"unpack_from requires a buffer of at least {(ulong)so.Size + (ulong)offset} bytes for " +
                                $"unpacking {so.Size} bytes at offset {offset} (actual buffer size is {len})");
                return UnpackInternal(so, span.Slice((int)offset));
            }
            finally
            {
                buffer.Release();
            }
        }

        // Struct_iter_unpack
        internal static PyObject IterUnpack(StructObject so, PyObject arg)
        {
            if (so.Size == 0)
                throw Error("cannot iteratively unpack with a struct of length 0");
            var buffer = arg.GetBuffer(0);
            long len = buffer.ByteLength;
            if (len % so.Size != 0)
            {
                buffer.Release();
                throw Error($"iterative unpacking requires a buffer of a multiple of {so.Size} bytes");
            }
            return new UnpackIterator(so, buffer);
        }

        private static PyType BuildStructType()
        {
            var type = new PyType("Struct", new[] { PyType.ObjectType }, "_struct");
            type.SetSpecLayout(new TypeLayout(56, 0, 0, 48, true));
            type.TypeDict["__module__"] = PyString.From("_struct");
            // s_new: an object with no format yet, which __init__ prepares.
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type,
                (cls, args, kwargs) => new StructObject((PyType)cls), 0, int.MaxValue, acceptsKwargs: true);
            // Struct___init__: "format" by position or name.
            type.TypeDict["__init__"] = new PyWrapperDescriptor("__init__", type, (self, args, kwargs) =>
            {
                var fast = PyArgs.Unpack("Struct", args, kwargs, new[] { "format" }, 0, 1, 1);
                ((StructObject)self).Init(fast[0]);
                return PyNone.Instance;
            });
            TypeTables.GenericAttributes(type);
            TypeTables.GenericSetAttributes(type);
            PyArgs.Define(type, "iter_unpack", PyCall.One, (self, args) => IterUnpack((StructObject)self, args[0]));
            Fast(type, "pack", (self, args) => Pack((StructObject)self, args, 0));
            Fast(type, "pack_into", (self, args) => PackInto((StructObject)self, args, 0));
            PyArgs.Define(type, "unpack", PyCall.One, (self, args) => Unpack((StructObject)self, args[0], "unpack", "argument"));
            type.TypeDict["unpack_from"] = new PyMethodDescriptor("unpack_from", type, (self, args, kwargs) =>
            {
                var fast = PyArgs.Unpack("unpack_from", args, kwargs, new[] { "buffer", "offset" }, 0, 1, 2);
                long offset = fast[1] == null ? 0 : PyArgs.Ssize(fast[1]);
                return UnpackFrom((StructObject)self, fast[0], offset);
            }, minArgs: 0, maxArgs: int.MaxValue, acceptsKwargs: true);
            PyArgs.Define(type, "__sizeof__", PyCall.NoArgs, (self, args) => ((StructObject)self).SizeOf());
            type.TypeDict["format"] = new PyGetSetDescriptor("format", type, self => ((StructObject)self).FormatString());
            type.TypeDict["size"] = new PyGetSetDescriptor("size", type, self => PyInt.From(((StructObject)self).Size));
            if (PyDocs.Lookup("_struct.Struct") is { } doc)
                type.TypeDict["__doc__"] = PyDocs.DocFromInternal("Struct", doc);
            return type;
        }

        // METH_FASTCALL: _PyObject_FunctionStr names the method in the keyword refusal.
        private static void Fast(PyType type, string name, Func<PyObject, PyObject[], PyObject> body) =>
            type.TypeDict[name] = new PyMethodDescriptor(name, type, (self, args, kwargs) =>
            {
                if (kwargs != null && kwargs.Length() != 0)
                    throw PyTypeError.Create($"{type.Name}.{name}() takes no keyword arguments");
                return body(self, args);
            }, minArgs: 0, maxArgs: int.MaxValue, acceptsKwargs: true);

        private static PyType BuildUnpackIterType()
        {
            var type = new PyType("unpack_iterator", new[] { PyType.ObjectType }, "_struct");
            // unpackiter_type_spec: Py_TPFLAGS_DISALLOW_INSTANTIATION
            type.SetSpecLayout(new TypeLayout(112, 0, 0, 0, true, NoInstances: true));
            type.TypeDict["__module__"] = PyString.From("_struct");
            TypeTables.GenericAttributes(type);
            type.TypeDict["__iter__"] = new PyWrapperDescriptor("__iter__", type, (self, args, kwargs) => self);
            type.TypeDict["__next__"] = new PyWrapperDescriptor("__next__", type, (self, args, kwargs) => self.IterNext());
            PyArgs.Define(type, "__length_hint__", PyCall.NoArgs, (self, args) => ((UnpackIterator)self).RemainingHint());
            type.TypeDict["__doc__"] = PyNone.Instance;
            return type;
        }

        // cache_struct_converter
        private static StructObject CachedStruct(PyObject fmt)
        {
            _cache ??= new PyDict();
            if (_cache.TryGetStoredValue(fmt, out var cached))
                return (StructObject)cached;
            var s = (StructObject)StructType.Call(new[] { fmt }, null);
            if (_cache.Length() >= MAXCACHE)
                _cache.Clear();
            try
            {
                _cache.SetItem(fmt, s);
            }
            catch (PythonException)
            {
            }
            return s;
        }

        internal static PyModule CreateStructModule()
        {
            var module = new PyModule("_struct");
            var d = module.ModuleDict;
            if (PyDocs.Lookup("_struct.__doc__") is { } doc)
                d["__doc__"] = PyString.From(doc);
            d["_clearcache"] = new PyBuiltinFunction("_clearcache", (args, kwargs) =>
            {
                PyArgs.NoArgs("_clearcache", args, kwargs);
                _cache = null;
                return PyNone.Instance;
            });
            d["calcsize"] = new PyBuiltinFunction("calcsize", (args, kwargs) =>
            {
                PyArgs.One("calcsize", args, kwargs);
                return PyInt.From(CachedStruct(args[0]).Size);
            });
            d["iter_unpack"] = new PyBuiltinFunction("iter_unpack", (args, kwargs) =>
            {
                PyArgs.Positional("iter_unpack", args, kwargs, 2, 2);
                return IterUnpack(CachedStruct(args[0]), args[1]);
            });
            d["pack"] = new PyBuiltinFunction("pack", (args, kwargs) =>
            {
                NoKeywords("pack", kwargs);
                if (args.Length == 0)
                    throw PyTypeError.Create("missing format argument");
                return Pack(CachedStruct(args[0]), args, 1);
            });
            d["pack_into"] = new PyBuiltinFunction("pack_into", (args, kwargs) =>
            {
                NoKeywords("pack_into", kwargs);
                if (args.Length == 0)
                    throw PyTypeError.Create("missing format argument");
                return PackInto(CachedStruct(args[0]), args, 1);
            });
            d["unpack"] = new PyBuiltinFunction("unpack", (args, kwargs) =>
            {
                PyArgs.Positional("unpack", args, kwargs, 2, 2);
                var so = CachedStruct(args[0]);
                return Unpack(so, args[1], "unpack", "argument 2");
            });
            d["unpack_from"] = new PyBuiltinFunction("unpack_from", (args, kwargs) =>
            {
                var fast = PyArgs.Unpack("unpack_from", args, kwargs, new[] { "", "buffer", "offset" }, 1, 2, 3);
                var so = CachedStruct(fast[0]);
                long offset = fast[2] == null ? 0 : PyArgs.Ssize(fast[2]);
                return UnpackFrom(so, fast[1], offset);
            });
            d["Struct"] = StructType;
            d["error"] = ErrorType;
            return module;
        }

        // cfunction_vectorcall_FASTCALL: "_struct.pack() takes no keyword arguments"
        private static void NoKeywords(string name, PyDict kwargs)
        {
            if (kwargs != null && kwargs.Length() != 0)
                throw PyTypeError.Create($"_struct.{name}() takes no keyword arguments");
        }
    }

    /// <summary>PyStructObject</summary>
    internal sealed class StructObject : PyObject
    {
        private const long BasicSize = 56;
        private const long FormatCodeSize = 32;

        internal long Size = -1;
        internal long Len = -1;
        internal _StructModule.FormatCode[] Codes;
        // s_format: None until __init__, then the format as bytes.
        private PyObject _format = PyNone.Instance;

        internal StructObject(PyType type)
        {
            if (type != _StructModule.StructType)
                SetCustomType(type);
        }

        public override PyType GetPyType() => _customType ?? _StructModule.StructType;
        public override string GetTypeName() => _customType is PyClass heap ? heap.Name : "_struct.Struct";

        internal PyBytes Format => (PyBytes)_format;

        // Struct___init___impl
        internal void Init(PyObject format)
        {
            if (format is PyString text)
                format = text.Encode("ascii");
            if (format is not PyBytes)
                throw PyTypeError.Create($"Struct() argument 1 must be a str or bytes object, not {format.GetPyType().Name}");
            _format = format;
            _StructModule.Prepare(this);
        }

        // s_get_format
        internal PyObject FormatString() => PyString.From(Encoding.UTF8.GetString(Format.Value));

        // s_sizeof
        internal PyObject SizeOf()
        {
            long size = BasicSize + FormatCodeSize;
            foreach (var code in Codes)
            {
                if (code.Def == null)
                    break;
                size += FormatCodeSize;
            }
            return PyInt.From(size);
        }
    }

    /// <summary>unpackiterobject: the struct, the buffer it holds, and the index into it.</summary>
    internal sealed class UnpackIterator : PyIterator
    {
        private StructObject _so;
        private PyMemoryView _buf;
        private long _index;

        internal UnpackIterator(StructObject so, PyMemoryView buf)
        {
            _so = so;
            _buf = buf;
        }

        public override PyType GetPyType() => _StructModule.UnpackIterType;
        public override string GetTypeName() => "_struct.unpack_iterator";
        protected override string IteratorName => "_struct.unpack_iterator";

        // unpackiter_len
        internal override PyObject RemainingHint() =>
            PyInt.From(_so == null ? 0 : (_buf.ByteLength - _index) / _so.Size);

        // unpackiter_iternext
        protected internal override PyObject IterNext()
        {
            if (_so == null)
                throw PyStopIteration.Create();
            var span = _buf.ContiguousBytes();
            if (_index >= span.Length)
            {
                // Iterator exhausted
                _so = null;
                _buf.Release();
                _buf = null;
                throw PyStopIteration.Create();
            }
            var result = _StructModule.UnpackInternal(_so, span.Slice((int)_index));
            _index += _so.Size;
            return result;
        }
    }
}
