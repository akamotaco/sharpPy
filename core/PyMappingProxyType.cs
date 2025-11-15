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

        public PyMappingProxy(Dictionary<string, PyObject> mapping)
        {
            // CPython 3.12: Keep reference to original dictionary, don't copy
            // This allows equality comparison between mappingproxies wrapping the same dict
            _mapping = mapping;
        }

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
            return pyDict.RichCompare(otherToCompare, op);
        }

        #region Dictionary-like operations (read-only)

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
                throw PyKeyError.Create($"'{keyStr.Value}'");
            }
            throw PyTypeError.Create($"mappingproxy key must be str, not '{key.GetTypeName()}'");
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

        public override PyString ToStr()
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

        public override PyString ToRepr() => ToStr();

        #endregion

        #region Built-in methods (CPython mappingproxy methods)

        public override PyObject GetAttribute(string name)
        {
            return name switch
            {
                "keys" => new PyBoundBuiltinMethod(this, new PyBuiltinMethod("keys", (self, args) =>
                {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"keys() takes no arguments ({args.Length} given)");
                    var mappingProxy = self as PyMappingProxy;
                    // Performance: Eliminated LINQ - manual conversion instead of Select + ToList
                    var keysList = new List<PyObject>(mappingProxy._mapping.Keys.Count);
                    foreach (var key in mappingProxy._mapping.Keys)
                    {
                        keysList.Add(new PyString(key));
                    }
                    return new PyList(keysList);
                })),
                "values" => new PyBoundBuiltinMethod(this, new PyBuiltinMethod("values", (self, args) =>
                {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"values() takes no arguments ({args.Length} given)");
                    var mappingProxy = self as PyMappingProxy;
                    // Performance: Eliminated LINQ - direct List construction instead of ToList
                    return new PyList(new List<PyObject>(mappingProxy._mapping.Values));
                })),
                "items" => new PyBoundBuiltinMethod(this, new PyBuiltinMethod("items", (self, args) =>
                {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"items() takes no arguments ({args.Length} given)");
                    var mappingProxy = self as PyMappingProxy;
                    // Performance: Eliminated LINQ - manual loop instead of Select + ToList
                    var items = new List<PyObject>(mappingProxy._mapping.Count);
                    foreach (var kv in mappingProxy._mapping)
                    {
                        items.Add(new PyTuple(new PyObject[] { new PyString(kv.Key), kv.Value }));
                    }
                    return new PyList(items);
                })),
                "get" => new PyBoundBuiltinMethod(this, new PyBuiltinMethod("get", (self, args) =>
                {
                    if (args.Length < 1 || args.Length > 2)
                        throw PyTypeError.Create($"get() takes 1 or 2 arguments ({args.Length} given)");

                    var mappingProxy = self as PyMappingProxy;
                    if (args[0] is PyString keyStr)
                    {
                        if (mappingProxy._mapping.TryGetValue(keyStr.Value, out var value))
                            return value;
                        return args.Length == 2 ? args[1] : PyNone.Instance;
                    }
                    throw PyTypeError.Create($"mappingproxy key must be str, not '{args[0].GetTypeName()}'");
                })),
                _ => base.GetAttribute(name)
            };
        }

        #endregion
    }
}
