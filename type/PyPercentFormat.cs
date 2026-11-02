using System.Numerics;

namespace SharpPy
{
    /// <summary>
    /// How str % and bytes % turn an argument into a number. unicodeobject.c (mainformatlong,
    /// formatchar) and bytesobject.c (formatlong, byte_converter) ask the same questions. The
    /// callers differ only in what they say on failure, so that part stays with them.
    /// </summary>
    internal static class PyPercentFormat
    {
        /// <summary>
        /// %d %i %u go through int(), %o %x %X through __index__. Whatever fails with TypeError
        /// is reported as the wrong type of argument, and any other error passes through.
        /// </summary>
        internal static BigInteger Integer(PyObject value, char type)
        {
            bool indexOnly = type is 'o' or 'x' or 'X';
            if (value is PyBool truth)
                return truth.Value ? BigInteger.One : BigInteger.Zero;
            if (PyInt.TryLongSelf(value, out var self))
                return self.Value;
            if (PyObject.NumberCheck(value))
            {
                try
                {
                    return indexOnly
                        ? PyObject.IndexOrThrow(value).Value
                        : PyObject.TryGetIndex(PyType.IntType.Call(new[] { value }, null)).Value;
                }
                catch (PythonException error) when (error.PyException is PyTypeError)
                {
                }
            }
            throw PyTypeError.Create(
                $"%{type} format: {(indexOnly ? "an integer" : "a real number")} is required, not {value.GetTypeName()}");
        }

        /// <summary>PyLong_AsLongAndOverflow for %c: the code point, or null for the wrong type.</summary>
        internal static BigInteger? Character(PyObject value)
        {
            try
            {
                return PyObject.TryGetIndex(value)?.Value;
            }
            catch (PythonException error) when (error.PyException is PyTypeError)
            {
                return null;
            }
        }
    }
}
