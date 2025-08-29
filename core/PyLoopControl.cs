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
}