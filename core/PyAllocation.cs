using System;

namespace SharpPy
{
    /// <summary>
    /// The host's allocation limits, where CPython's allocators fail with PyErr_NoMemory
    /// (PyUnicode_New, _PyBytes_FromSize, PyByteArray_Resize, list and tuple allocation): a length
    /// the CLR cannot give one object is the MemoryError of that allocation, without a message.
    /// CPython builds what fits in the address space; a CLR string stops at String.MaxLength
    /// (System.Private.CoreLib String.cs) and an array at Array.MaxLength, and neither limit is
    /// raised by gcAllowVeryLargeObjects for strings. What overflows Py_ssize_t first keeps the
    /// OverflowError its caller words, as CPython does.
    /// </summary>
    internal static class PyAllocation
    {
        // String.MaxLength: the longest CLR string, in UTF-16 units.
        internal const int MaxStringLength = 0x3FFFFFDF;

        /// <summary>A string of length units, or the MemoryError PyUnicode_New gives.</summary>
        internal static int StringLength(long length) =>
            length > MaxStringLength ? throw PyMemoryError.Create() : (int)length;

        /// <summary>An array (bytes, bytearray, list, tuple) of length items, or the MemoryError.</summary>
        internal static int ArrayLength(long length) =>
            length > Array.MaxLength ? throw PyMemoryError.Create() : (int)length;

        /// <summary>
        /// size * count as a Py_ssize_t, or the caller's OverflowError past PY_SSIZE_T_MAX
        /// ("repeated string is too long", "repeated bytes are too long").
        /// </summary>
        /// <summary>PyNumber_AsSsize_t with OverflowError: what sq_repeat's count is first.</summary>
        internal static long IndexSized(System.Numerics.BigInteger count) =>
            count > long.MaxValue || count < long.MinValue
                ? throw PyOverflowError.Create("cannot fit 'int' into an index-sized integer")
                : (long)count;

        /// <summary>
        /// list_repeat, tuple_repeat, bytearray_repeat, array_repeat: a size past PY_SSIZE_T_MAX is
        /// PyErr_NoMemory there, as is one no array holds.
        /// </summary>
        internal static int RepeatedItems(long size, long count)
        {
            if (size != 0 && count > long.MaxValue / size)
                throw PyMemoryError.Create();
            return ArrayLength(size * count);
        }

        internal static long Product(long size, long count, string overflowMessage)
        {
            if (size != 0 && count > long.MaxValue / size)
                throw PyOverflowError.Create(overflowMessage);
            return size * count;
        }
    }
}
