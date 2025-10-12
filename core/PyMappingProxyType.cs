using System.Collections.Generic;
using System.Linq;

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
            _mapping = new Dictionary<string, PyObject>(mapping); // Copy to ensure immutability
        }

        public override PyType GetPyType() => PyType.MappingProxyType;
        public override string GetTypeName() => "mappingproxy";

        #region Dictionary-like operations (read-only)

        public override PyObject GetItem(PyObject key)
        {
            if (key is PyString keyStr)
            {
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
            var items = _mapping.Select(kv => $"'{kv.Key}': {kv.Value.ToRepr().Value}");
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
                    return new PyList(mappingProxy._mapping.Keys.Select(k => (PyObject)new PyString(k)).ToList());
                })),
                "values" => new PyBoundBuiltinMethod(this, new PyBuiltinMethod("values", (self, args) =>
                {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"values() takes no arguments ({args.Length} given)");
                    var mappingProxy = self as PyMappingProxy;
                    return new PyList(mappingProxy._mapping.Values.ToList());
                })),
                "items" => new PyBoundBuiltinMethod(this, new PyBuiltinMethod("items", (self, args) =>
                {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"items() takes no arguments ({args.Length} given)");
                    var mappingProxy = self as PyMappingProxy;
                    var items = mappingProxy._mapping.Select(kv => (PyObject)new PyTuple(new PyObject[] { new PyString(kv.Key), kv.Value })).ToList();
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
