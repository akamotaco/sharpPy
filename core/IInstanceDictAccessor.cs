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
