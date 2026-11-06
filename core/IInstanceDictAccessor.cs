using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// Attribute-store seam for objects with an instance __dict__.
    /// CPython 3.12: Objects/object.c PyObject_GenericGetAttr / _PyObject_GenericSetAttrWithDict
    /// </summary>
    public interface IInstanceDictAccessor
    {
        bool TryGetInstanceAttribute(string name, out PyObject value);
        IEnumerable<KeyValuePair<string, PyObject>> InstanceAttributes { get; }
    }

    /// <summary>
    /// tp_dictoffset (PyObject_GetDictPtr): the instance dict a builtin object keeps in its own
    /// struct, which PyObject_GenericGetAttr and _PyObject_GenericSetAttrWithDict read and write.
    /// null when the object has none yet and create is false.
    /// </summary>
    /// <summary>An instance whose own store is its __dict__, written through as well as read.</summary>
    internal interface IInstanceDictStore : IInstanceDictAccessor
    {
        void SetInstanceAttribute(string name, PyObject value);
        bool RemoveInstanceAttribute(string name);
    }

    public interface IObjectDict
    {
        PyDict ObjectDict(bool create);

        /// <summary>PyObject_GenericSetDict: the dict replaces the one the object had.</summary>
        void SetObjectDict(PyDict dict);
    }

    /// <summary>
    /// Dict-backed instances that store attributes in a plain Dictionary.
    /// Provides default implementations of IInstanceDictAccessor over InstanceDict.
    /// </summary>
    public interface IDictBackedInstance : IInstanceDictAccessor
    {
        Dictionary<string, PyObject> InstanceDict { get; }

        bool IInstanceDictAccessor.TryGetInstanceAttribute(string name, out PyObject value)
            => InstanceDict.TryGetValue(name, out value);

        IEnumerable<KeyValuePair<string, PyObject>> IInstanceDictAccessor.InstanceAttributes
            => InstanceDict;
    }
}
