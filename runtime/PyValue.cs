using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SharpPy
{
    /// <summary>
    /// Tagged value struct for VM-internal stack and locals storage.
    /// Allows int64/float64/bool/none/null to live inline without heap allocation.
    ///
    /// Tag values:
    ///   0 = Object  (ObjRef holds PyObject reference)
    ///   1 = Int64   (RawBits holds long value)
    ///   2 = Float64 (RawBits holds BitConverter.DoubleToInt64Bits)
    ///   3 = Bool    (RawBits: 0=False, 1=True)
    ///   4 = None
    ///   5 = Null    (CPython internal NULL / uninitialized)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PyValue
    {
        public const byte TAG_OBJECT = 0;
        public const byte TAG_INT64 = 1;
        public const byte TAG_FLOAT64 = 2;
        public const byte TAG_BOOL = 3;
        public const byte TAG_NONE = 4;
        public const byte TAG_NULL = 5;

        public byte Tag;
        public long RawBits;
        public PyObject ObjRef;

        // Pre-built singletons for common values
        public static readonly PyValue None = new PyValue { Tag = TAG_NONE };
        public static readonly PyValue Null = new PyValue { Tag = TAG_NULL };
        public static readonly PyValue True = new PyValue { Tag = TAG_BOOL, RawBits = 1 };
        public static readonly PyValue False = new PyValue { Tag = TAG_BOOL, RawBits = 0 };
        public static readonly PyValue IntZero = new PyValue { Tag = TAG_INT64, RawBits = 0 };
        public static readonly PyValue IntOne = new PyValue { Tag = TAG_INT64, RawBits = 1 };
        public static readonly PyValue FloatZero = new PyValue { Tag = TAG_FLOAT64, RawBits = 0 }; // +0.0

        #region Tag checks

        public bool IsObject
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Tag == TAG_OBJECT;
        }

        public bool IsInt64
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Tag == TAG_INT64;
        }

        public bool IsFloat64
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Tag == TAG_FLOAT64;
        }

        public bool IsBool
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Tag == TAG_BOOL;
        }

        public bool IsNone
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Tag == TAG_NONE;
        }

        public bool IsNull
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Tag == TAG_NULL;
        }

        /// <summary>
        /// True if this value is an integer type (Int64 or Bool).
        /// Python: bool is subclass of int, so True + 1 == 2.
        /// </summary>
        public bool IsIntLike
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Tag == TAG_INT64 || Tag == TAG_BOOL;
        }

        /// <summary>
        /// True if this value is a numeric type (Int64, Float64, or Bool).
        /// </summary>
        public bool IsNumeric
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Tag == TAG_INT64 || Tag == TAG_FLOAT64 || Tag == TAG_BOOL;
        }

        #endregion

        #region Value accessors

        /// <summary>
        /// Get the int64 value. Only valid when Tag == TAG_INT64 or TAG_BOOL.
        /// </summary>
        public long AsInt64
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => RawBits;
        }

        /// <summary>
        /// Get the float64 value. Only valid when Tag == TAG_FLOAT64.
        /// </summary>
        public double AsFloat64
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => BitConverter.Int64BitsToDouble(RawBits);
        }

        /// <summary>
        /// Get the bool value. Only valid when Tag == TAG_BOOL.
        /// </summary>
        public bool AsBool
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => RawBits != 0;
        }

        #endregion

        #region Factory methods

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PyValue FromInt64(long value)
        {
            return new PyValue { Tag = TAG_INT64, RawBits = value };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PyValue FromFloat64(double value)
        {
            return new PyValue { Tag = TAG_FLOAT64, RawBits = BitConverter.DoubleToInt64Bits(value) };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PyValue FromBool(bool value)
        {
            return new PyValue { Tag = TAG_BOOL, RawBits = value ? 1L : 0L };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PyValue FromObject(PyObject obj)
        {
            if (obj == null || obj is PyNull)
                return Null;

            // PyBool before PyInt — PyBool inherits PyInt, so check subclass first
            if (obj is PyBool pb)
                return new PyValue { Tag = TAG_BOOL, RawBits = pb == PyBool.True ? 1L : 0L };

            if (obj is PyInt pi)
            {
                // Fast path: use cached long value to avoid BigInteger comparison/cast
                // CPython 3.12: ob_digit compact representation for small ints
                if (pi.FitsInLong)
                    return new PyValue { Tag = TAG_INT64, RawBits = pi.CachedLong };
                // BigInteger that doesn't fit in long → keep as object
                return new PyValue { Tag = TAG_OBJECT, ObjRef = obj };
            }

            if (obj is PyFloat pf)
                return new PyValue { Tag = TAG_FLOAT64, RawBits = BitConverter.DoubleToInt64Bits(pf.Value) };

            if (obj == PyNone.Instance)
                return None;

            return new PyValue { Tag = TAG_OBJECT, ObjRef = obj };
        }

        #endregion

        #region Conversion to PyObject

        /// <summary>
        /// Convert this tagged value back to a PyObject.
        /// Uses SmallIntCache/FloatCache for cached values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PyObject ToObject()
        {
            switch (Tag)
            {
                case TAG_OBJECT:
                    return ObjRef;
                case TAG_INT64:
                    return SmallIntCache.GetOrCreate(RawBits);
                case TAG_FLOAT64:
                    return FloatCache.GetOrCreate(BitConverter.Int64BitsToDouble(RawBits));
                case TAG_BOOL:
                    return RawBits != 0 ? PyBool.True : PyBool.False;
                case TAG_NONE:
                    return PyNone.Instance;
                case TAG_NULL:
                    return PyNull.Instance;
                default:
                    return ObjRef;
            }
        }

        #endregion

        #region Truthiness

        /// <summary>
        /// Python truth value test. Equivalent to bool(value).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsTrue()
        {
            switch (Tag)
            {
                case TAG_INT64:
                    return RawBits != 0;
                case TAG_FLOAT64:
                    return BitConverter.Int64BitsToDouble(RawBits) != 0.0;
                case TAG_BOOL:
                    return RawBits != 0;
                case TAG_NONE:
                    return false;
                case TAG_NULL:
                    return false;
                case TAG_OBJECT:
                    return ObjRef != null && ObjRef.PyBoolValue();
                default:
                    return false;
            }
        }

        #endregion

        #region Debug

        public override string ToString()
        {
            switch (Tag)
            {
                case TAG_OBJECT: return ObjRef?.ToString() ?? "<null-ref>";
                case TAG_INT64: return $"int64:{RawBits}";
                case TAG_FLOAT64: return $"float64:{BitConverter.Int64BitsToDouble(RawBits)}";
                case TAG_BOOL: return RawBits != 0 ? "True" : "False";
                case TAG_NONE: return "None";
                case TAG_NULL: return "<NULL>";
                default: return $"<unknown tag {Tag}>";
            }
        }

        #endregion
    }
}
