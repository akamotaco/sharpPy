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
        public override PyString AsString() => ToRepr();

        public override PyBool Contains(PyObject item)
        {
            if (_kind == DictViewKind.Keys) return _dictionary.Contains(item);
            if (_kind == DictViewKind.Items)
            {
                if (item is not PyTuple pair || pair.Items.Length != 2) return PyBool.False;
                if (!_dictionary.TryGetStoredValue(pair.Items[0], out var value)) return PyBool.False;
                return Equal(value, pair.Items[1]);
            }
            foreach (var entry in _dictionary.GetInternalDict())
                if (Equal(entry.Value, item).Value) return PyBool.True;
            return PyBool.False;
        }

        private static PyBool Equal(PyObject left, PyObject right) =>
            PyBool.FromBool(CompareBool(left, right, CompareOp.EQ));

        private bool IsSetView => _kind != DictViewKind.Values;
        private static bool SetLike(PyObject value) => value is PySet || value is PyFrozenSet ||
            value is PyDictView view && view.IsSetView;
        private static bool AllContained(PyObject source, PyObject target)
        {
            foreach (var item in PySet.IterableItems(source))
                if (!target.Contains(item).Value) return false;
            return true;
        }
        public override PyObject RichCompare(PyObject other, CompareOp op)
        {
            if (!IsSetView || !SetLike(other)) return PyNotImplemented.Instance;
            int left = Length(), right = other.Length();
            bool equal = op switch
            {
                CompareOp.EQ => left == right && AllContained(this, other),
                CompareOp.NE => left != right || !AllContained(this, other),
                CompareOp.LE => left <= right && AllContained(this, other),
                CompareOp.LT => left < right && AllContained(this, other),
                CompareOp.GE => left >= right && AllContained(other, this),
                CompareOp.GT => left > right && AllContained(other, this),
                _ => false
            };
            return PyBool.FromBool(equal);
        }
        public override PyObject BitwiseAnd(PyObject other)
        {
            if (!IsSetView) return base.BitwiseAnd(other);
            var result = new PySet();
            foreach (var item in PySet.IterableItems(other))
                if (Contains(item).Value) result.Add(item);
            return result;
        }
        public override PyObject BitwiseOr(PyObject other)
        {
            if (!IsSetView) return base.BitwiseOr(other);
            var result = new PySet(PySet.IterableItems(this));
            foreach (var item in PySet.IterableItems(other)) result.Add(item);
            return result;
        }
        public override PyObject Subtract(PyObject other)
        {
            if (!IsSetView) return base.Subtract(other);
            var result = new PySet(PySet.IterableItems(this));
            foreach (var item in PySet.IterableItems(other)) result.Discard(item);
            return result;
        }
        public override PyObject BitwiseXor(PyObject other)
        {
            if (!IsSetView) return base.BitwiseXor(other);
            return new PySet(PySet.IterableItems(this)).SymmetricDifference(new PySet(PySet.IterableItems(other)));
        }
    }
}
