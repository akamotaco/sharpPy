using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// CPython 3.12 mappingproxy type - Read-only proxy for dictionaries
    /// C equivalent: Objects/descrobject.c - mappingproxy
    /// </summary>
    public class PyMappingProxy : PyObject
    {
        private readonly Dictionary<string, PyObject> _mapping;

        // Internal property to access the count without triggering recursion in __len__
        internal virtual int InternalCount => _mapping.Count;

        public PyMappingProxy(Dictionary<string, PyObject> mapping)
        {
            // CPython 3.12: Keep reference to original dictionary, don't copy
            // This allows equality comparison between mappingproxies wrapping the same dict
            _mapping = mapping;
        }

        internal PyDict Snapshot() => new PyDict(_mapping);

        public override PyType GetPyType() => PyType.MappingProxyType;
        public override string GetTypeName() => "mappingproxy";

        // CPython 3.12: mappingproxy delegates comparison to the underlying dictionary
        // See Objects/descrobject.c - mappingproxy_richcompare
        // return PyObject_RichCompare(v->mapping, w, op);
        public override PyObject RichCompare(PyObject other, CompareOp op)
        {
            // If other is also a mappingproxy, extract its underlying dictionary
            PyObject otherToCompare = other;
            if (other is PyMappingProxy otherProxy)
            {
                otherToCompare = new PyDict(otherProxy._mapping);
            }

            // Convert internal dict to PyDict and delegate comparison
            var pyDict = new PyDict(_mapping);
            return Compare(pyDict, otherToCompare, op);
        }

        #region Dictionary-like operations (read-only)

        // mappingproxy_getiter (Objects/descrobject.c) iterates the mapping.
        public override PyObject GetIterator() => new PyDict(_mapping).GetIterator();

        public override PyObject GetItem(PyObject key)
        {
            if (key is PyString keyStr)
            {
#if DEBUG
                // Debug: Track _generate_next_value_ access from __prepare__
                if (keyStr.Value == "_generate_next_value_")
                {
                    if (_mapping.TryGetValue(keyStr.Value, out var debugValue))
                    {
                        Console.WriteLine($"[PyMappingProxy.GetItem DEBUG] Accessing '{keyStr.Value}'");
                        Console.WriteLine($"[PyMappingProxy.GetItem DEBUG]   Returning: {debugValue}, type={debugValue.GetTypeName()}");
                    }
                }
#endif
                if (_mapping.TryGetValue(keyStr.Value, out var value))
                    return value;
                throw PyKeyError.Create(key);
            }
            throw PyKeyError.Create(key);
        }

        public override void SetItem(PyObject key, PyObject value)
        {
            throw PyTypeError.Create("'mappingproxy' object does not support item assignment");
        }

        // CPython 3.12: __contains__ method for 'in' operator
        public override PyBool Contains(PyObject item)
        {
            if (item is PyString keyStr)
            {
                return PyBool.FromBool(_mapping.ContainsKey(keyStr.Value));
            }
            return PyBool.False;
        }

        public bool ContainsKey(string key) => _mapping.ContainsKey(key);

        public IEnumerable<string> Keys => _mapping.Keys;
        public IEnumerable<PyObject> Values => _mapping.Values;

        #endregion

        #region String representation

        public override PyString AsString()
        {
            // Performance: Eliminated LINQ - manual loop instead of Select
            var items = new string[_mapping.Count];
            int index = 0;
            foreach (var kv in _mapping)
            {
                items[index++] = $"'{kv.Key}': {kv.Value.ToRepr().Value}";
            }
            return new PyString("{" + string.Join(", ", items) + "}");
        }

        // mappingproxy_repr
        public override PyString ToRepr() => new PyString($"mappingproxy({AsString().Value})");

        #endregion

        #region Built-in methods (CPython mappingproxy methods)

        // mappingproxy_keys and the rest: PyObject_CallMethod on the mapping.
        internal virtual PyObject Forward(string name) => Snapshot().GetAttribute(name);

        #endregion
    }

    /// <summary>mappingproxy(mapping) of Objects/descrobject.c: a read-only view of any mapping.</summary>
    public sealed class PyMappingWrapper : PyMappingProxy
    {
        private readonly PyObject _wrapped;

        public PyMappingWrapper(PyObject mapping) : base(new Dictionary<string, PyObject>()) => _wrapped = mapping;

        internal override int InternalCount => _wrapped.Length();

        public override int Length() => _wrapped.Length();

        public override PyObject GetIterator() => _wrapped.GetIterator();

        public override PyObject GetItem(PyObject key) => _wrapped.GetItem(key);

        public override PyBool Contains(PyObject item) => _wrapped.Contains(item);

        public override PyObject RichCompare(PyObject other, CompareOp op) =>
            _wrapped.RichCompare(other is PyMappingWrapper wrapper ? wrapper._wrapped : other, op);

        public override PyString AsString() => _wrapped.AsString();

        public override PyString ToRepr() => new PyString($"mappingproxy({_wrapped.ToRepr().Value})");

        internal override PyObject Forward(string name) => _wrapped.GetAttribute(name);
    }

}
