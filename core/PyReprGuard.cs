using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// CPython does not let a container's repr recurse into itself: <c>Py_ReprEnter</c> marks the
    /// object while its tp_repr runs and, if the same object is met again, tp_repr returns a
    /// placeholder instead of descending - <c>[1, [...]]</c> for a self-containing list
    /// (Objects/listobject.c:379 list_repr), <c>{1: {...}}</c> for a dict
    /// (Objects/dictobject.c:2400 dict_repr), <c>set(...)</c> for a set (Objects/setobject.c:507).
    ///
    /// Without that mark SharpPy descended until the CLR aborted the process with "Stack overflow",
    /// which is not even a Python exception and kills the whole run. Measured on a list subclass:
    /// <c>x = [1]; x.append(x); repr(x)</c> printed nothing and died.
    ///
    /// The set holds only the objects on the current repr path (removed in a finally block) and is
    /// per-thread, because two threads can be repr-ing different graphs at the same time.
    /// </summary>
    internal static class PyReprGuard
    {
        [ThreadStatic] private static HashSet<PyObject> Active;

        /// <summary>
        /// True when the caller may render the object; false when it is already being rendered,
        /// which is the signal to print the placeholder.
        /// </summary>
        internal static bool Enter(PyObject obj)
        {
            // Reference identity: these containers are unhashable in Python terms, a user class may
            // define __hash__ that matches other objects, and CPython marks the object, not a value.
            Active ??= new HashSet<PyObject>(ReferenceEqualityComparer.Instance);
            return Active.Add(obj);
        }

        internal static void Leave(PyObject obj)
        {
            Active?.Remove(obj);
        }
    }
}
