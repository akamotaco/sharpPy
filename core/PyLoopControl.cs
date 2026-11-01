using System;

namespace SharpPy
{
    /// <summary>
    /// Exception used to implement break statement in loops
    /// </summary>
    public class LoopBreakException : Exception
    {
        public LoopBreakException() : base("Loop break") { }
    }

    /// <summary>
    /// Exception used to implement continue statement in loops
    /// </summary>
    public class LoopContinueException : Exception
    {
        public LoopContinueException() : base("Loop continue") { }
    }

    /// <summary>Raised by RETURN paths of the host and caught where a frame was entered.</summary>
    public class PyReturnException : Exception
    {
        public PyObject Value { get; }
        public PyReturnException(PyObject value) { Value = value; }
    }

    /// <summary>YIELD_VALUE leaves ExecuteFrame through this; the generator catches it.</summary>
    public class PyYieldException : Exception
    {
        public PyObject Value { get; }
        public PyYieldException(PyObject value) { Value = value; }
    }
}
