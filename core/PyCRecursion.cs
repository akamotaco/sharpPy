using System;

namespace SharpPy
{
    /// <summary>
    /// _Py_EnterRecursiveCallTstate / _Py_LeaveRecursiveCallTstate (Include/internal/pycore_ceval.h):
    /// one per-thread count of the C recursion that no Python frame records - comparisons, repr,
    /// str - against C_RECURSION_LIMIT (Include/cpython/pystate.h: 3000 on Windows, 10000
    /// elsewhere). The host stack is checked too, so the error comes while there is stack to raise it.
    /// </summary>
    internal static class PyCRecursion
    {
        internal static int Limit => CRecursionLimit.Value;

        // tstate->c_recursion_remaining: the thread's state is read once per Enter and per Leave.
        internal sealed class State { internal int Depth; }
        [ThreadStatic] private static State _state;

        internal static void Enter(string where) => EnterState(where);

        /// <summary>Enter, answering the thread's state so the caller leaves without reading it again.</summary>
        internal static State EnterState(string where)
        {
            var state = _state ??= new State();
            if (state.Depth >= CRecursionLimit.Value || !System.Runtime.CompilerServices.RuntimeHelpers.TryEnsureSufficientExecutionStack())
                throw new PythonException(new PyRecursionError("maximum recursion depth exceeded" + where));
            state.Depth++;
            return state;
        }

        internal static void Leave() => _state.Depth--;
    }

    // Apart from PyCRecursion, whose thread-static is then read without a class-initialization check.
    internal static class CRecursionLimit
    {
        internal static readonly int Value = OperatingSystem.IsWindows() ? 3000 : 10000;
    }
}
