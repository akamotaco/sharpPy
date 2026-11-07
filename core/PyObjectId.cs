using System.Runtime.CompilerServices;
using System.Threading;

namespace SharpPy
{
    /// <summary>
    /// id(). CPython answers with the address (Python/bltinmodule.c:1234 builtin_id, :1237
    /// PyLong_FromVoidPtr). The CLR moves objects and GetHashCode is neither unique nor unrelated
    /// to value, so a number is handed out on the first call and lives as long as the object.
    /// A number is never reused, which is stricter than the language asks for.
    ///
    /// The measured defect this removes: two separate objects holding the same value reported the
    /// same id (a join-built string equal to a literal, int("1000000"), 1.5 built from text), and
    /// 300000 live lists produced 299312 distinct ids. copy.deepcopy keys its memo on id(), so 667
    /// collisions there would have handed back the wrong object without any error.
    /// </summary>
    internal static class PyObjectId
    {
        private sealed class Number
        {
            internal readonly long Value = Interlocked.Add(ref _next, 16);
        }

        private static long _next = 0x10000;
        private static readonly ConditionalWeakTable<PyObject, Number> Numbers = new();

        internal static long Of(PyObject value) => Numbers.GetValue(value, static _ => new Number()).Value;

        /// <summary>
        /// The address a repr shows: PyUnicode_FromFormat's %p of the object, which is what id()
        /// answers - so repr(o) names hex(id(o)). %p is the C library's: MSVC writes sixteen
        /// upper-case digits, the others the lower-case number.
        /// </summary>
        internal static string Pointer(PyObject value) => System.OperatingSystem.IsWindows()
            ? $"0x{Of(value):X16}"
            : $"0x{Of(value):x}";
    }
}
