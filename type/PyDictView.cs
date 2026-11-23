namespace SharpPy
{
    public enum DictViewKind { Keys, Values, Items }

    // A view retains its dictionary. Materialization is deferred until requested.
    public class PyDictView : PyObject
    {
        static PyDictView()
        {
            foreach (var type in new[] { PyType.DictKeysType, PyType.DictValuesType, PyType.DictItemsType })
            {
                type.TypeDict["__iter__"] = new PyWrapperDescriptor("__iter__", type,
                    (self, args, kwargs) => ((PyDictView)self).Iter(), minArgs: 0, maxArgs: 0);
                type.TypeDict["__len__"] = new PyWrapperDescriptor("__len__", type,
                    (self, args, kwargs) => PyInt.From(((PyDictView)self).Len()), minArgs: 0, maxArgs: 0);
                // dictvalues_as_sequence has no sq_contains: `in` iterates the values.
                if (!ReferenceEquals(type, PyType.DictValuesType))
                    type.TypeDict["__contains__"] = new PyWrapperDescriptor("__contains__", type,
                        (self, args, kwargs) => ((PyDictView)self).ContainsItem(args[0]), minArgs: 1, maxArgs: 1);
                // keys and items compare as sets, which leaves their tp_hash PyObject_HashNotImplemented.
                if (!ReferenceEquals(type, PyType.DictValuesType))
                    type.TypeDict["__hash__"] = PyNone.Instance;
                type.TypeDict["mapping"] = new PyGetSetDescriptor("mapping", type,
                    self => new PyMappingProxy((PyObject)((PyDictView)self)._dictionary));
                type.TypeDict["__repr__"] = new PyWrapperDescriptor("__repr__", type,
                    (self, args, kwargs) => ((PyDictView)self).Repr(), minArgs: 0, maxArgs: 0);
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
        protected internal override long Len() => _dictionary.Length();
        protected internal override bool Bool() => Len() != 0;

        protected internal override PyObject Iter() => _kind switch
        {
            DictViewKind.Keys => new PyDictKeyIterator(_dictionary),
            DictViewKind.Values => new PyDictValueIterator(_dictionary),
            _ => new PyDictItemIterator(_dictionary)
        };
        // dictview_repr: Py_ReprEnter, then the list of what it shows.
        protected internal override PyString Repr()
        {
            if (!PyReprGuard.Enter(this))
                return PyString.From("...");
            try
            {
                return PyString.From($"{GetTypeName()}({AsList().ToRepr().Value})");
            }
            finally
            {
                PyReprGuard.Leave(this);
            }
        }
        protected internal override PyString Str() => ToRepr();

        protected internal override PyBool ContainsItem(PyObject item)
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

        // PyDictViewSet_Check: a keys or items view.
        internal static bool IsViewSet(PyObject value) => value is PyDictView view && view.IsSetView;

        // all_contained_in: every item self's iterator gives is in other (PySequence_Contains).
        private static bool AllContainedIn(PyObject self, PyObject other)
        {
            foreach (var item in IterateItems(self))
                if (!other.Contains(item).Value) return false;
            return true;
        }

        // dictview_richcompare
        protected internal override PyObject TpRichCompare(PyObject other, CompareOp op)
        {
            if (!IsSetView || !(other is PySet or PyFrozenSet || IsViewSet(other))) return PyNotImplemented.Instance;
            long lenSelf = Length(), lenOther = other.Length();
            bool ok = op switch
            {
                CompareOp.EQ => lenSelf == lenOther && AllContainedIn(this, other),
                CompareOp.NE => !(lenSelf == lenOther && AllContainedIn(this, other)),
                CompareOp.LT => lenSelf < lenOther && AllContainedIn(this, other),
                CompareOp.LE => lenSelf <= lenOther && AllContainedIn(this, other),
                CompareOp.GT => lenSelf > lenOther && AllContainedIn(other, this),
                CompareOp.GE => lenSelf >= lenOther && AllContainedIn(other, this),
                _ => false
            };
            return PyBool.FromBool(ok);
        }

        // nb_and and the rest are the functions below for either operand order, as the slots are.
        public override PyObject BitwiseAnd(PyObject other) => IsSetView ? Intersect(this, other) : base.BitwiseAnd(other);
        public override PyObject BitwiseOr(PyObject other) => IsSetView ? Or(this, other) : base.BitwiseOr(other);
        public override PyObject Subtract(PyObject other) => IsSetView ? Sub(this, other) : base.Subtract(other);
        public override PyObject BitwiseXor(PyObject other) => IsSetView ? Xor(this, other) : base.BitwiseXor(other);

        /// <summary>
        /// _PyDictView_Intersect: an exact set at least as large as the view does the work
        /// (set.intersection); otherwise the other operand, or the smaller of two views, is
        /// iterated and its items kept when the view contains them.
        /// </summary>
        internal static PyObject Intersect(PyObject self, PyObject other)
        {
            if (!IsViewSet(self))
                (self, other) = (other, self);
            long lenSelf = ((PyDictView)self).Length();
            if (other is PySet { IsExact: true } && lenSelf <= other.Length())
                return other.GetAttribute("intersection").Call(new[] { self }, null);
            if (IsViewSet(other) && ((PyDictView)other).Length() > lenSelf)
                (self, other) = (other, self);
            var view = (PyDictView)self;
            var result = new PySet();
            foreach (var key in IterateItems(other))
                if (view.ContainsItem(key).Value)
                    result.Add(key);
            return result;
        }

        /// <summary>dictviews_isdisjoint: the smaller of two set-likes is iterated.</summary>
        internal static PyObject IsDisjoint(PyObject self, PyObject other)
        {
            if (ReferenceEquals(self, other))
                return PyBool.FromBool(((PyDictView)self).Length() == 0);
            if (other is PySet or PyFrozenSet || IsViewSet(other))
            {
                long lenSelf = ((PyDictView)self).Length(), lenOther = other.Length();
                if (lenOther > lenSelf)
                    (self, other) = (other, self);
            }
            foreach (var item in IterateItems(other))
                if (self.Contains(item).Value)
                    return PyBool.False;
            return PyBool.True;
        }

        // dictviews_to_set: PySet_New of the operand, the dict itself for a keys view of an exact dict.
        private static PySet ToSet(PyObject self)
        {
            PyObject left = self;
            if (self is PyDictView { _kind: DictViewKind.Keys } keys && PySet.IsExactDict(keys._dictionary))
                left = keys._dictionary;
            return new PySet(PySet.SourceItems(left));
        }

        // dictviews_sub
        internal static PyObject Sub(PyObject self, PyObject other)
        {
            var result = ToSet(self);
            result.GetAttribute("difference_update").Call(new[] { other }, null);
            return result;
        }

        // dictviews_or: _PySet_Update.
        internal static PyObject Or(PyObject self, PyObject other)
        {
            var result = ToSet(self);
            result.Update(other);
            return result;
        }

        // dictviews_xor
        internal static PyObject Xor(PyObject self, PyObject other)
        {
            if (self is PyDictView { _kind: DictViewKind.Items } left && other is PyDictView { _kind: DictViewKind.Items } right)
                return ItemsXor(left._dictionary, right._dictionary);
            var result = ToSet(self);
            result.GetAttribute("symmetric_difference_update").Call(new[] { other }, null);
            return result;
        }

        // dictitems_xor: the pairs of d2 whose key d1 lacks or holds with an unequal value, then
        // what is left of a copy of d1.
        private static PyObject ItemsXor(PyDict d1, PyDict d2)
        {
            var temp = PyDict.DictCopy(d1);
            var result = new PySet();
            int position = 0;
            while (d2.InternalDict.NextEntry(ref position, out var entry))
            {
                var key = entry.Key;
                var val2 = entry.Value;
                bool delete = temp.TryGetStoredValue(key, out var val1) && CompareBool(val1, val2, CompareOp.EQ);
                if (delete)
                    temp.DelItem(key);
                else
                    result.Add(PyTuple.From(new[] { key, val2 }));
            }
            result.Update(temp.GetAttribute("items").Call(Array.Empty<PyObject>(), null));
            return result;
        }
    }
}
