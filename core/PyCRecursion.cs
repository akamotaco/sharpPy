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
        internal static readonly int Limit = OperatingSystem.IsWindows() ? 3000 : 10000;

        [ThreadStatic] private static int _depth;

        internal static void Enter(string where)
        {
            if (_depth >= Limit || !System.Runtime.CompilerServices.RuntimeHelpers.TryEnsureSufficientExecutionStack())
                throw new PythonException(new PyRecursionError("maximum recursion depth exceeded" + where));
            _depth++;
        }

        internal static void Leave() => _depth--;
    }
}
