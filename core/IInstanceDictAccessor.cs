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

    /// <summary>
    /// A builtin whose tp_dictoffset dict is a PyDict of its own (function, BaseException): the
    /// instance store the generic attribute paths read and write is that dict.
    /// </summary>
    internal interface IObjectDict : IInstanceDictStore
    {
        PyDict ObjectDict(bool create);

        /// <summary>PyObject_GenericSetDict: the dict replaces the one the object had.</summary>
        void SetObjectDict(PyDict dict);

        bool IInstanceDictAccessor.TryGetInstanceAttribute(string name, out PyObject value)
        {
            value = null;
            return ObjectDict(false) is { } dict && dict.TryGetStoredValue(new PyString(name), out value);
        }

        IEnumerable<KeyValuePair<string, PyObject>> IInstanceDictAccessor.InstanceAttributes
        {
            get
            {
                if (ObjectDict(false) is not { } dict) yield break;
                foreach (var entry in dict.InternalDict)
                    if (entry.Key is PyString key) yield return new KeyValuePair<string, PyObject>(key.Value, entry.Value);
            }
        }

        void IInstanceDictStore.SetInstanceAttribute(string name, PyObject value) => ObjectDict(true).SetItem(new PyString(name), value);

        bool IInstanceDictStore.RemoveInstanceAttribute(string name)
        {
            var key = new PyString(name);
            if (ObjectDict(false) is not { } dict || !dict.ContainsKey(key)) return false;
            dict.DelItem(key);
            return true;
        }
    }
}
