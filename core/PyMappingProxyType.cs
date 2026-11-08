using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// mappingproxyobject (Objects/descrobject.c of CPython v3.12.15): a read-only view that holds one
    /// mapping and hands every operation to it. A type's __dict__ is the view of the type's own table,
    /// whose mapping is that table as a dict; mappingproxy(m) holds m itself.
    /// </summary>
    public class PyMappingProxy : PyObject
    {
        // A type's own dict, read in place; null for mappingproxy(m).
        private readonly Dictionary<string, PyObject> _table;
        // pp->mapping of mappingproxy(m).
        private readonly PyObject _mapping;

        public PyMappingProxy(Dictionary<string, PyObject> table) => _table = table;

        internal PyMappingProxy(PyObject mapping) => _mapping = mapping;

        /// <summary>pp->mapping. For a type's table, a dict of it as it is now, key objects and all.</summary>
        internal PyObject Mapping
        {
            get
            {
                if (_mapping != null)
                    return _mapping;
                var dict = new PyDict();
                foreach (var entry in TableEntries)
                    dict.SetItem(entry.Key, entry.Value);
                return dict;
            }
        }

        internal int InternalCount => _table is PyTypeDictionary typed ? typed.EntryCount : _table?.Count ?? _mapping.Length();

        // The entries of a type's table, its key objects in its order: dict_merge and the update
        // paths read it in place. Null for mappingproxy(m).
        internal IEnumerable<KeyValuePair<PyObject, PyObject>> TableEntries =>
            _table is PyTypeDictionary typed ? typed.Entries()
            : _table == null ? null : PlainEntries(_table);

        private static IEnumerable<KeyValuePair<PyObject, PyObject>> PlainEntries(Dictionary<string, PyObject> table)
        {
            foreach (var entry in table)
                yield return new KeyValuePair<PyObject, PyObject>(PyString.From(entry.Key), entry.Value);
        }

        private bool TryGetFromTable(PyObject key, out PyObject value)
        {
            if (_table is PyTypeDictionary typed)
                return typed.TryGetItem(key, out value);
            value = null;
            return key is PyString text && _table.TryGetValue(text.Value, out value);
        }

        public override PyType GetPyType() => PyType.MappingProxyType;
        public override string GetTypeName() => "mappingproxy";

        // mappingproxy_len: PyObject_Size(pp->mapping)
        protected internal override long Len() => InternalCount;

        // mappingproxy_getitem: PyObject_GetItem(pp->mapping, key)
        protected internal override PyObject MpSubscript(PyObject key)
        {
            if (_table == null)
                return PyAbstract.GetItem(_mapping, key);
            if (TryGetFromTable(key, out var value))
                return value;
            throw PyKeyError.Create(key);
        }

        protected internal override void MpAssSubscript(PyObject key, PyObject value)
        {
            throw PyTypeError.Create("'mappingproxy' object does not support item assignment");
        }

        // mappingproxy_contains: PyDict_Contains for an exact dict, PySequence_Contains otherwise.
        protected internal override PyBool ContainsItem(PyObject item)
        {
            if (_table != null)
                return PyBool.FromBool(TryGetFromTable(item, out _));
            return _mapping.Contains(item);
        }

        public bool ContainsKey(string key) => _table != null ? _table.ContainsKey(key) : Contains(PyString.From(key)).Value;

        // mappingproxy_getiter: PyObject_GetIter(pp->mapping)
        protected internal override PyObject Iter() => Mapping.GetIterator();

        // mappingproxy_hash: PyObject_Hash(pp->mapping)
        public override int ToHash() => Mapping.ToHash();
        protected internal override long Hash() => Mapping.GetPythonHash();

        // mappingproxy_str and mappingproxy_repr
        protected internal override PyString Str() => Mapping.ToStr();
        protected internal override PyString Repr() => PyString.From($"mappingproxy({Mapping.ToRepr().Value})");

        // mappingproxy_richcompare: PyObject_RichCompare(v->mapping, w, op)
        protected internal override PyObject TpRichCompare(PyObject other, CompareOp op) => Compare(Mapping, other, op);

        // nb_or, which binary_op1 calls with this object on the left.
        public override PyObject BitwiseOr(PyObject other) => Or(this, other);

        // mappingproxy_or: both operands seen through, then PyNumber_Or.
        internal static PyObject Or(PyObject left, PyObject right) =>
            PyVM.Instance.ExecuteBinaryOpType(left is PyMappingProxy l ? l.Mapping : left,
                right is PyMappingProxy r ? r.Mapping : right, BinaryOpType.OR);

        // mappingproxy_keys, _values, _items, _copy, _reversed: PyObject_CallMethodNoArgs(pp->mapping, name).
        internal PyObject Forward(string name) => Mapping.GetAttribute(name).Call(new PyObject[0], null);

        /// <summary>mappingproxy_methods and mappingproxy_as_number.</summary>
        internal static void Install()
        {
            var type = PyType.MappingProxyType;
            foreach (var name in new[] { "keys", "values", "items", "copy", "__reversed__" })
            {
                var method = name;
                type.TypeDict[method] = PyArgs.Method(type, method, PyCall.NoArgs, (self, args) => ((PyMappingProxy)self).Forward(method));
            }
            // mappingproxy_get: _PyArg_UnpackStack("get", 1, 2), then pp->mapping.get(key, default).
            type.TypeDict["get"] = PyArgs.Method(type, "get", PyCall.Fast, (self, args) =>
                ((PyMappingProxy)self).Mapping.GetAttribute("get").Call(new[] { args[0], args.Length > 1 ? args[1] : PyNone.Instance }, null), 1, 2);
            type.TypeDict["__or__"] = new PyWrapperDescriptor("__or__", type, (self, args, kwargs) => Or(self, args[0]));
            type.TypeDict["__ror__"] = new PyWrapperDescriptor("__ror__", type, (self, args, kwargs) => Or(args[0], self));
            // mappingproxy_ior
            type.TypeDict["__ior__"] = new PyWrapperDescriptor("__ior__", type, (self, args, kwargs) =>
                throw PyTypeError.Create($"'|=' is not supported by {self.GetPyType().FullName}; use '|' instead"));
        }

        /// <summary>mappingproxy_check_mapping and mappingproxy_new_impl.</summary>
        internal static PyMappingProxy New(PyObject mapping)
        {
            if (mapping is PyList or PyTuple || !(mapping is PyDict || mapping is PyMappingProxy || mapping.HasTypeSlot("__getitem__")))
                throw PyArgs.BadArgument("mappingproxy", "argument", "a mapping", mapping);
            return new PyMappingProxy(mapping);
        }
    }
}
