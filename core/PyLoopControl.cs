using System;

namespace SharpPy
{
    /// <summary>YIELD_VALUE leaves ExecuteFrame through this; the generator catches it.</summary>
    public class PyYieldException : Exception
    {
        public PyObject Value { get; }
        public PyYieldException(PyObject value) { Value = value; }
    }
}
