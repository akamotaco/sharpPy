using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// Interface for objects that have an instance dictionary (__dict__)
    /// CPython 3.12: Objects/typeobject.c:4200-4250 (PyObject_GenericGetDict)
    ///
    /// Implemented by:
    /// - PyClassInstance: user-defined class instances
    /// - PyIntSubclass: int subclass instances (e.g., IntEnum)
    /// - PyStrSubclass: str subclass instances (e.g., StrEnum)
    /// - PyTupleSubclass: tuple subclass instances (e.g., NamedTuple)
    /// </summary>
    public interface IInstanceDictAccessor
    {
        /// <summary>
        /// The instance's attribute dictionary
        /// CPython 3.12: Include/cpython/object.h:88 (PyObject_GenericGetDict returns this)
        /// </summary>
        Dictionary<string, PyObject> InstanceDict { get; }
    }
}
