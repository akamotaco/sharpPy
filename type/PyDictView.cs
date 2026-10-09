namespace SharpPy
{
    public enum DictViewKind { Keys, Values, Items }

    // A view retains its dictionary. Materialization is deferred until requested.
    public sealed class PyDictView : PyObject
    {
        static PyDictView()
        {
            foreach (var type in new[] { PyType.DictKeysType, PyType.DictValuesType, PyType.DictItemsType })
            {
                type.TypeDict["__iter__"] = new PyMethodDescriptor("__iter__", type,
                    (self, args, kwargs) => ((PyDictView)self).GetIterator(), minArgs: 0, maxArgs: 0);
                type.TypeDict["__len__"] = new PyMethodDescriptor("__len__", type,
                    (self, args, kwargs) => new PyInt(((PyDictView)self).Length()), minArgs: 0, maxArgs: 0);
                type.TypeDict["__contains__"] = new PyMethodDescriptor("__contains__", type,
                    (self, args, kwargs) => ((PyDictView)self).Contains(args[0]), minArgs: 1, maxArgs: 1);
                type.TypeDict["__repr__"] = new PyMethodDescriptor("__repr__", type,
                    (self, args, kwargs) => ((PyDictView)self).ToRepr(), minArgs: 0, maxArgs: 0);
            }
        }

        private readonly PyDict _dictionary;
        private readonly DictViewKind _kind;

        public PyDictView(PyDict dictionary, DictViewKind kind)
        {
            _dictionary = dictionary;
            _kind = kind;
        }

        public override PyType GetPyType() => _kind switch
        {
            DictViewKind.Keys => PyType.DictKeysType,
            DictViewKind.Values => PyType.DictValuesType,
            _ => PyType.DictItemsType
        };
        public override string GetTypeName() => GetPyType().Name;
        public override int Length() => _dictionary.Length();
        public override bool IsTrue() => Length() != 0;
        public override bool PyBoolValue() => IsTrue();
        public override int ToHash() => _kind == DictViewKind.Values ? base.ToHash()
            : throw PyTypeError.Create($"unhashable type: '{GetTypeName()}'");

        // Internal snapshot helpers keep existing interop callers unchanged.
        public override PyList AsList() => _kind switch
        {
            DictViewKind.Keys => _dictionary.Keys(),
            DictViewKind.Values => _dictionary.Values(),
            _ => _dictionary.Items()
        };
        public override PyTuple AsTuple() => new PyTuple(AsList().Items);
        public override PyObject GetIterator() => _kind switch
        {
            DictViewKind.Keys => new PyDictKeyIterator(_dictionary),
            DictViewKind.Values => new PyDictValueIterator(_dictionary),
            _ => new PyDictItemIterator(_dictionary)
        };
        public override PyString ToRepr() => new PyString($"{GetTypeName()}({AsList().ToRepr().Value})");
        public override PyString ToStr() => ToRepr();

        public override PyBool Contains(PyObject item)
        {
            if (_kind == DictViewKind.Keys) return _dictionary.Contains(item);
            if (_kind == DictViewKind.Items)
            {
                if (item is not PyTuple pair || pair.Items.Length != 2) return PyBool.False;
                if (!_dictionary.InternalDict.TryGetValue(pair.Items[0], out var value)) return PyBool.False;
                return Equal(value, pair.Items[1]);
            }
            foreach (var entry in _dictionary.GetInternalDict())
                if (Equal(entry.Value, item).Value) return PyBool.True;
            return PyBool.False;
        }

        private static PyBool Equal(PyObject left, PyObject right) => ReferenceEquals(left, right)
            ? PyBool.True : (PyBool)left.RichCompare(right, CompareOp.EQ);
    }
}
