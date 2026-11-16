using System;
using System.Collections.Generic;

namespace SharpPy.Modules
{
    /// <summary>
    /// Objects/odictobject.c (CPython 3.12.15): collections.OrderedDict, a dict whose order is a
    /// doubly linked list of nodes. od_fast_nodes mirrors the dict's entries, indexed by the entry
    /// _Py_dict_lookup finds, and is rebuilt when the dict's table is replaced (od_resize_sentinel).
    /// od_state counts the changes to the list, which iterators and comparisons check.
    /// </summary>
    public sealed class PyODict : PyDict, IObjectDict
    {
        // struct _odictnode
        internal sealed class Node
        {
            internal PyObject Key;
            internal long Hash;
            internal Node Next, Prev;
        }

        internal static readonly PyType ODictType = CreateType();
        internal static readonly PyType KeysType = ViewType("odict_keys", PyType.DictKeysType, DictViewKind.Keys);
        internal static readonly PyType ItemsType = ViewType("odict_items", PyType.DictItemsType, DictViewKind.Items);
        internal static readonly PyType ValuesType = ViewType("odict_values", PyType.DictValuesType, DictViewKind.Values);

        private Node _first, _last;         // od_first, od_last
        private Node[] _fastNodes;          // od_fast_nodes
        private object _resizeSentinel;     // od_resize_sentinel
        internal ulong State;               // od_state
        private PyDict _instDict;           // od_inst_dict

        internal PyODict(PyType type)
        {
            if (!ReferenceEquals(type, ODictType))
                SetCustomType(type);
        }

        public override PyType GetPyType() => _customType ?? ODictType;

        /// <summary>Whether dict_new allocates this layout for the type.</summary>
        internal static bool IsLayoutOf(PyType type) =>
            ReferenceEquals(type, ODictType) || type.IsSubclassOf(ODictType);

        // PyODict_CheckExact
        private bool IsExactODict => IsExactly(ODictType);

        // od_inst_dict (tp_dictoffset): OrderedDict().__dict__
        public PyDict ObjectDict(bool create) => create ? _instDict ??= new PyDict() : _instDict;
        public void SetObjectDict(PyDict dict) => _instDict = dict;

        internal Node First => _first;
        internal Node Last => _last;
        private bool Empty => _first == null;

        // ---- odict keys (a simple doubly-linked list)

        // _odict_get_index_raw: the entry index of the key, or dk_nentries when it is absent.
        private int GetIndexRaw(PyObject key, long hash) => _dict.LookupIndex(key, hash);

        // _odict_resize: a new fast-nodes table the size of the dict's.
        private void Resize()
        {
            var fastNodes = new Node[_dict.TableSize];
            // Copy the current nodes into the table.
            for (var node = _first; node != null; node = node.Next)
            {
                int i = GetIndexRaw(node.Key, node.Hash);
                fastNodes[i] = node;
            }
            // Replace the old fast nodes table.
            _fastNodes = fastNodes;
            _resizeSentinel = _dict.TableIdentity;
        }

        // _odict_get_index: od_fast_nodes and dk_entries in sync first.
        private int GetIndex(PyObject key, long hash)
        {
            if (!ReferenceEquals(_resizeSentinel, _dict.TableIdentity) || _fastNodes == null || _fastNodes.Length != _dict.TableSize)
                Resize();
            return GetIndexRaw(key, hash);
        }

        // _odict_find_node_hash
        private Node FindNodeHash(PyObject key, long hash)
        {
            if (Empty)
                return null;
            int index = GetIndex(key, hash);
            return _fastNodes[index];
        }

        // _odict_find_node
        internal Node FindNode(PyObject key)
        {
            if (Empty)
                return null;
            long hash = key.GetPythonHash();
            int index = GetIndex(key, hash);
            return _fastNodes[index];
        }

        // _odict_add_head
        private void AddHead(Node node)
        {
            node.Prev = null;
            node.Next = _first;
            if (_first == null)
                _last = node;
            else
                _first.Prev = node;
            _first = node;
            State++;
        }

        // _odict_add_tail
        private void AddTail(Node node)
        {
            node.Prev = _last;
            node.Next = null;
            if (_last == null)
                _first = node;
            else
                _last.Next = node;
            _last = node;
            State++;
        }

        // _odict_add_new_node: adds the node to the end of the list.
        private void AddNewNode(PyObject key, long hash)
        {
            int i = GetIndex(key, hash);
            if (_fastNodes[i] != null)
                // We already have a node for the key so there's no need to add one.
                return;
            var node = new Node { Key = key, Hash = hash };
            AddTail(node);
            _fastNodes[i] = node;
        }

        // _odict_remove_node: repeated calls on the same node are no-ops.
        private void RemoveNode(Node node)
        {
            if (_first == node)
                _first = node.Next;
            else if (node.Prev != null)
                node.Prev.Next = node.Next;
            if (_last == node)
                _last = node.Prev;
            else if (node.Next != null)
                node.Next.Prev = node.Prev;
            node.Prev = null;
            node.Next = null;
            State++;
        }

        // _odict_clear_node: the node leaves od_fast_nodes before the dict loses the key, so a
        // colliding key's index cannot move under it.
        private void ClearNode(Node node, PyObject key, long hash)
        {
            if (Empty)
                // Let later code decide if this is a KeyError.
                return;
            int i = GetIndex(key, hash);
            node ??= _fastNodes[i];
            if (node == null)
                // Let later code decide if this is a KeyError.
                return;
            // Now clear the node.
            _fastNodes[i] = null;
            RemoveNode(node);
        }

        // _odict_clear_nodes
        private void ClearNodes()
        {
            _fastNodes = null;
            _resizeSentinel = null;
            _first = null;
            _last = null;
            State++;
        }

        // _odict_keys_equal
        private static bool KeysEqual(PyODict a, PyODict b)
        {
            // keep operands' state to detect undesired mutations
            ulong stateA = a.State, stateB = b.State;
            var nodeA = a._first;
            var nodeB = b._first;
            while (true)
            {
                if (nodeA == null && nodeB == null)
                    // success: hit the end of each at the same time
                    return true;
                if (nodeA == null || nodeB == null)
                    // unequal length
                    return false;
                bool res = CompareBool(nodeA.Key, nodeB.Key, CompareOp.EQ);
                if (a.State != stateA || b.State != stateB)
                    throw PyRuntimeError.Create("OrderedDict mutated during iteration");
                if (!res)
                    return false;
                // otherwise it must match, so move on to the next one
                nodeA = nodeA.Next;
                nodeB = nodeB.Next;
            }
        }

        // ---- the public OrderedDict API

        // _PyODict_SetItem_KnownHash
        private void SetItemKnownHash(PyObject key, PyObject value, long hash)
        {
            _dict.SetKnownHash(key, value, hash);
            try
            {
                AddNewNode(key, hash);
            }
            catch (PythonException)
            {
                // Revert setting the value on the dict
                try { _dict.Remove(key, out _, out _); }
                catch (PythonException) { }
                throw;
            }
        }

        // PyODict_SetItem
        internal void ODictSetItem(PyObject key, PyObject value) => SetItemKnownHash(key, value, key.GetPythonHash());

        // PyODict_DelItem
        internal void ODictDelItem(PyObject key)
        {
            long hash = key.GetPythonHash();
            ClearNode(null, key, hash);
            // _PyDict_DelItem_KnownHash
            if (!_dict.Remove(key, out _, out _))
                throw PyKeyError.Create(key);
        }

        // odict_mp_ass_sub: mp_ass_subscript, __setitem__ and __delitem__.
        protected internal override void MpAssSubscript(PyObject key, PyObject value) => ODictSetItem(key, value);
        protected internal override void MpDelSubscript(PyObject key) => ODictDelItem(key);

        // dict_ass_sub and PyDict_SetItem: what dict's own methods and dict_merge write is the
        // table alone, the linked list left as it was.
        internal override void DictAssSub(PyObject key, PyObject value) => base.MpAssSubscript(key, value);
        internal override void DictDelSub(PyObject key) => base.MpDelSubscript(key);
        public override void SetItemStorage(PyObject key, PyObject value) => base.MpAssSubscript(key, value);

        // tp_iter
        protected internal override PyObject Iter() => new ODictIterator(this, ODictIterator.Keys);

        // odict_repr
        protected internal override PyString Repr()
        {
            string name = GetPyType().Name;
            if (Len() == 0)
                return PyString.From($"{name}()");
            if (!PyReprGuard.Enter(this))
                return PyString.From("...");
            try
            {
                var dcopy = CopyAsDict(this);
                return PyString.From($"{name}({dcopy.ToRepr().Value})");
            }
            finally
            {
                PyReprGuard.Leave(this);
            }
        }

        protected internal override PyString Str() => ToRepr();

        // odict_richcompare
        protected internal override PyObject TpRichCompare(PyObject w, CompareOp op) => RichCompare(this, w, op);

        private static PyObject RichCompare(PyObject v, PyObject w, CompareOp op)
        {
            if (v is not PyODict left || w is not PyDict right)
                return PyNotImplemented.Instance;
            if (op is not (CompareOp.EQ or CompareOp.NE))
                return PyNotImplemented.Instance;
            bool equal = left.CheckDictEquality(right);
            var cmp = PyBool.FromBool(op == CompareOp.EQ ? equal : !equal);
            if (w is not PyODict odictRight)
                return cmp;
            if (op == CompareOp.EQ && !equal)
                return cmp;
            if (op == CompareOp.NE && !equal)
                return cmp;
            // Try comparing odict keys.
            bool eq = KeysEqual(left, odictRight);
            return PyBool.FromBool(eq == (op == CompareOp.EQ));
        }

        // odict_or: whichever operand is the OrderedDict picks the type; the other must be a dict.
        private static PyObject Or(PyObject left, PyObject right)
        {
            PyType type;
            PyObject other;
            if (left is PyODict)
            {
                type = left.GetPyType();
                other = right;
            }
            else
            {
                type = right.GetPyType();
                other = left;
            }
            if (other is not PyDict)
                return PyNotImplemented.Instance;
            var result = type.Call(new[] { left }, null);
            MutableMappingUpdateArg(result, right);
            return result;
        }

        public override PyObject BitwiseOr(PyObject other) => Or(this, other);

        // odict_inplace_or
        public override PyObject InplaceBitwiseOr(PyObject other)
        {
            MutableMappingUpdateArg(this, other);
            return this;
        }

        // ---- OrderedDict methods

        // odict_sizeof: the dict, the fast nodes table and the linked list.
        private PyObject SizeOf()
        {
            long res = DictSizeInBytes(Length());
            res += 8L * (_fastNodes?.Length ?? 0);
            if (!Empty)
                res += 32L * Len();
            return PyInt.From(res);
        }

        // odict_reduce
        private PyObject Reduce()
        {
            // capture any instance state
            var state = PyType.GetState(this);
            var items = GetAttribute("items").Call(Array.Empty<PyObject>(), null);
            var itemsIter = items.GetIterator();
            return PyTuple.From(new[] { GetPyType(), PyTuple.From(Array.Empty<PyObject>()), state, PyNone.Instance, itemsIter });
        }

        // OrderedDict_setdefault_impl: skips __missing__() calls.
        private PyObject SetDefaultImpl(PyObject key, PyObject defaultValue)
        {
            if (IsExactODict)
            {
                if (TryGetStoredValue(key, out var result))
                    return result;
                ODictSetItem(key, defaultValue);
                return defaultValue;
            }
            if (Contains(key).Value)
                return PyAbstract.GetItem(this, key);
            PyAbstract.SetItem(this, key, defaultValue);
            return defaultValue;
        }

        // _odict_popkey_hash
        private PyObject PopKeyHash(PyObject key, PyObject failobj, long hash)
        {
            var node = FindNodeHash(key, hash);
            if (node != null)
            {
                // Pop the node first to avoid a possible dict resize (due to eval loop
                // reentrancy) and complications due to hash collision resolution.
                ClearNode(node, key, hash);
                // Now delete the value from the dict (_PyDict_Pop_KnownHash).
                if (_dict.Remove(key, out var value, out _))
                    return value;
                if (failobj != null)
                    return failobj;
                throw PyKeyError.Create(key);
            }
            // Apply the fallback value, if necessary.
            if (failobj != null)
                return failobj;
            throw PyKeyError.Create(key);
        }

        // OrderedDict_pop_impl: skips __missing__() calls.
        private PyObject PopImpl(PyObject key, PyObject defaultValue) => PopKeyHash(key, defaultValue, key.GetPythonHash());

        // OrderedDict_popitem_impl
        private PyObject PopItemImpl(bool last)
        {
            // pull the item
            if (Empty)
                throw PyKeyError.Create("dictionary is empty");
            var node = last ? _last : _first;
            var key = node.Key;
            var value = PopKeyHash(key, null, node.Hash);
            return PyTuple.From(key, value);
        }

        // odict_clear
        private void ClearImpl()
        {
            base.Clear();
            ClearNodes();
        }

        // odict_copy
        private PyObject CopyImpl()
        {
            PyObject odCopy = IsExactODict ? new PyODict(ODictType) : GetPyType().Call(Array.Empty<PyObject>(), null);
            if (IsExactODict)
            {
                var copy = (PyODict)odCopy;
                for (var node = _first; node != null; node = node.Next)
                {
                    if (!TryGetStoredValue(node.Key, out var value))
                        throw PyKeyError.Create(node.Key);
                    copy.SetItemKnownHash(node.Key, value, node.Hash);
                }
            }
            else
            {
                for (var node = _first; node != null; node = node.Next)
                {
                    var value = PyAbstract.GetItem(this, node.Key);
                    PyAbstract.SetItem(odCopy, node.Key, value);
                }
            }
            return odCopy;
        }

        // OrderedDict_move_to_end_impl
        private void MoveToEnd(PyObject key, bool last)
        {
            if (Empty)
                throw PyKeyError.Create(key);
            var node = last ? _last : _first;
            if (ReferenceEquals(key, node.Key))
                return;
            node = FindNode(key) ?? throw PyKeyError.Create(key);
            if (last)
            {
                // Only move if not already the last one.
                if (node != _last)
                {
                    RemoveNode(node);
                    AddTail(node);
                }
            }
            else if (node != _first)
            {
                // Only move if not already the first one.
                RemoveNode(node);
                AddHead(node);
            }
        }

        // odict_init: __init__() triggering update() is just the way things are!
        private static void Init(PyObject self, PyObject[] args, PyDict kwds)
        {
            if (args.Length > 1)
                throw PyTypeError.Create($"expected at most 1 arguments, got {args.Length}");
            MutableMappingUpdate(self, args, kwds);
        }

        // PyDict_Copy of an OrderedDict: a dict made through dict_merge, which reads keys() since
        // the type's tp_iter is not dict_iter.
        private static PyDict CopyAsDict(PyODict od) => DictCopy(od);

        // ---- MutableMapping implementations

        // PyIter_Next: false once the iterator is exhausted.
        private static bool TryNext(PyObject iterator, out PyObject item)
        {
            try
            {
                item = iterator.Next();
                return true;
            }
            catch (PythonException e) when (e.PyException is PyStopIteration)
            {
                item = null;
                return false;
            }
        }

        // mutablemapping_add_pairs
        private static void MutableMappingAddPairs(PyObject self, PyObject pairs)
        {
            var iterator = pairs.GetIterator();
            while (TryNext(iterator, out var pair))
            {
                // could be more efficient (see UNPACK_SEQUENCE in ceval.c)
                var pairIterator = pair.GetIterator();
                if (!TryNext(pairIterator, out var key))
                    throw PyValueError.Create("need more than 0 values to unpack");
                if (!TryNext(pairIterator, out var value))
                    throw PyValueError.Create("need more than 1 value to unpack");
                if (TryNext(pairIterator, out _))
                    throw PyValueError.Create("too many values to unpack (expected 2)");
                PyAbstract.SetItem(self, key, value);
            }
        }

        // mutablemapping_update_arg
        private static void MutableMappingUpdateArg(PyObject self, PyObject arg)
        {
            if (arg is PyDict exact && exact.IsExact)
            {
                MutableMappingAddPairs(self, exact.Items());
                return;
            }
            var func = arg.LookupAttribute("keys");
            if (func != null)
            {
                var keys = func.Call(Array.Empty<PyObject>(), null);
                var iterator = keys.GetIterator();
                while (TryNext(iterator, out var key))
                {
                    var value = PyAbstract.GetItem(arg, key);
                    PyAbstract.SetItem(self, key, value);
                }
                return;
            }
            func = arg.LookupAttribute("items");
            if (func != null)
            {
                var items = func.Call(Array.Empty<PyObject>(), null);
                MutableMappingAddPairs(self, items);
                return;
            }
            MutableMappingAddPairs(self, arg);
        }

        // mutablemapping_update
        private static PyObject MutableMappingUpdate(PyObject self, PyObject[] args, PyDict kwargs)
        {
            // first handle args, if any
            if (args.Length > 1)
                throw PyTypeError.Create($"update() takes at most 1 positional argument ({args.Length} given)");
            if (args.Length != 0)
                MutableMappingUpdateArg(self, args[0]);
            // now handle kwargs
            if (kwargs != null && kwargs.Length() != 0)
                MutableMappingAddPairs(self, kwargs.Items());
            return PyNone.Instance;
        }

        // ---- the type

        private static PyODict Self(PyObject self) => (PyODict)self;

        private static PyType CreateType()
        {
            var type = new PyType("OrderedDict", new[] { PyType.DictType }, "collections");
            type.TypeDict["__module__"] = PyString.From("collections");
            type.TypeDict["__doc__"] = PyString.From("Dictionary that remembers insertion order");
            type.TypeDict["__init__"] = new PyWrapperDescriptor("__init__", type, (self, args, kwargs) =>
            {
                Init(self, args, kwargs);
                return PyNone.Instance;
            }, minArgs: 0, maxArgs: int.MaxValue, acceptsKwargs: true);
            // odict_as_mapping: mp_ass_subscript
            type.TypeDict["__setitem__"] = new PyWrapperDescriptor("__setitem__", type, (self, args, kwargs) =>
            {
                Self(self).ODictSetItem(args[0], args[1]);
                return PyNone.Instance;
            }, minArgs: 2, maxArgs: 2);
            type.TypeDict["__delitem__"] = new PyWrapperDescriptor("__delitem__", type, (self, args, kwargs) =>
            {
                Self(self).ODictDelItem(args[0]);
                return PyNone.Instance;
            }, minArgs: 1, maxArgs: 1);
            type.TypeDict["__iter__"] = new PyWrapperDescriptor("__iter__", type, (self, args, kwargs) =>
                new ODictIterator(Self(self), ODictIterator.Keys), minArgs: 0, maxArgs: 0);
            type.TypeDict["__repr__"] = new PyWrapperDescriptor("__repr__", type, (self, args, kwargs) => Self(self).Repr(), minArgs: 0, maxArgs: 0);
            foreach (var (name, op) in new[] { ("__lt__", CompareOp.LT), ("__le__", CompareOp.LE), ("__eq__", CompareOp.EQ),
                ("__ne__", CompareOp.NE), ("__gt__", CompareOp.GT), ("__ge__", CompareOp.GE) })
            {
                var compare = op;
                type.TypeDict[name] = new PyWrapperDescriptor(name, type, (self, args, kwargs) => RichCompare(self, args[0], compare), minArgs: 1, maxArgs: 1);
            }
            // odict_as_number
            type.TypeDict["__or__"] = new PyWrapperDescriptor("__or__", type, (self, args, kwargs) => Or(self, args[0]), minArgs: 1, maxArgs: 1);
            type.TypeDict["__ror__"] = new PyWrapperDescriptor("__ror__", type, (self, args, kwargs) => Or(args[0], self), minArgs: 1, maxArgs: 1);
            type.TypeDict["__ior__"] = new PyWrapperDescriptor("__ior__", type, (self, args, kwargs) => Self(self).InplaceBitwiseOr(args[0]), minArgs: 1, maxArgs: 1);

            // odict_methods
            type.TypeDict["fromkeys"] = new PyClassMethodDescriptor("fromkeys", type, new PyBuiltinFunction("fromkeys", (args, kwargs) =>
            {
                var p = PyArgs.Unpack("fromkeys", args[1..], kwargs, new[] { "iterable", "value" }, 0, 1, 2);
                return FromKeys(args[0], p[0], p[1] ?? PyNone.Instance);
            }));
            PyArgs.Define(type, "__sizeof__", PyCall.NoArgs, (self, args) => Self(self).SizeOf());
            PyArgs.Define(type, "__reduce__", PyCall.NoArgs, (self, args) => Self(self).Reduce());
            type.TypeDict["setdefault"] = new PyMethodDescriptor("setdefault", type, (self, args, kwargs) =>
            {
                var p = PyArgs.Unpack("setdefault", args, kwargs, new[] { "key", "default" }, 0, 1, 2);
                return Self(self).SetDefaultImpl(p[0], p[1] ?? PyNone.Instance);
            }, acceptsKwargs: true);
            type.TypeDict["pop"] = new PyMethodDescriptor("pop", type, (self, args, kwargs) =>
            {
                var p = PyArgs.Unpack("pop", args, kwargs, new[] { "key", "default" }, 0, 1, 2);
                return Self(self).PopImpl(p[0], p[1]);
            }, acceptsKwargs: true);
            type.TypeDict["popitem"] = new PyMethodDescriptor("popitem", type, (self, args, kwargs) =>
            {
                var p = PyArgs.Unpack("popitem", args, kwargs, new[] { "last" }, 0, 0, 1);
                return Self(self).PopItemImpl(p[0] == null || p[0].IsTrue());
            }, acceptsKwargs: true);
            PyArgs.Define(type, "keys", PyCall.NoArgs, (self, args) => new PyODictView(Self(self), DictViewKind.Keys));
            PyArgs.Define(type, "values", PyCall.NoArgs, (self, args) => new PyODictView(Self(self), DictViewKind.Values));
            PyArgs.Define(type, "items", PyCall.NoArgs, (self, args) => new PyODictView(Self(self), DictViewKind.Items));
            type.TypeDict["update"] = new PyMethodDescriptor("update", type, (self, args, kwargs) =>
                MutableMappingUpdate(self, args, kwargs), acceptsKwargs: true);
            PyArgs.Define(type, "clear", PyCall.NoArgs, (self, args) =>
            {
                Self(self).ClearImpl();
                return PyNone.Instance;
            });
            PyArgs.Define(type, "copy", PyCall.NoArgs, (self, args) => Self(self).CopyImpl());
            PyArgs.Define(type, "__reversed__", PyCall.NoArgs, (self, args) =>
                new ODictIterator(Self(self), ODictIterator.Keys | ODictIterator.Reversed));
            type.TypeDict["move_to_end"] = new PyMethodDescriptor("move_to_end", type, (self, args, kwargs) =>
            {
                var p = PyArgs.Unpack("move_to_end", args, kwargs, new[] { "key", "last" }, 0, 1, 2);
                Self(self).MoveToEnd(p[0], p[1] == null || p[1].IsTrue());
                return PyNone.Instance;
            }, acceptsKwargs: true);
            // odict_getset: {"__dict__", PyObject_GenericGetDict, PyObject_GenericSetDict}
            type.TypeDict["__dict__"] = SubclassInstanceSupport.GenericDictGetSet(type);
            return type;
        }

        private static PyType ViewType(string name, PyType baseType, DictViewKind kind)
        {
            var type = new PyType(name, new[] { baseType });
            // odictkeys_methods and the others: __reversed__
            PyArgs.Define(type, "__reversed__", PyCall.NoArgs, (self, args) =>
                ((PyODictView)self).Reversed());
            return type;
        }
    }

    /// <summary>odict_keys, odict_values and odict_items: dict views iterated in the list's order.</summary>
    public sealed class PyODictView : PyDictView
    {
        private readonly PyODict _odict;
        private readonly DictViewKind _kind;

        internal PyODictView(PyODict odict, DictViewKind kind) : base(odict, kind)
        {
            _odict = odict;
            _kind = kind;
        }

        public override PyType GetPyType() => _kind switch
        {
            DictViewKind.Keys => PyODict.KeysType,
            DictViewKind.Values => PyODict.ValuesType,
            _ => PyODict.ItemsType,
        };

        private int Kind => _kind switch
        {
            DictViewKind.Keys => ODictIterator.Keys,
            DictViewKind.Values => ODictIterator.Values,
            _ => ODictIterator.Keys | ODictIterator.Values,
        };

        // odictkeys_iter, odictvalues_iter, odictitems_iter
        protected internal override PyObject Iter() => new ODictIterator(_odict, Kind);

        // odictkeys_reversed and the others
        internal PyObject Reversed() => new ODictIterator(_odict, Kind | ODictIterator.Reversed);
    }

    /// <summary>
    /// odictiterobject: it tracks the current key rather than the current node, so that changes
    /// during iteration are found (od_state, the size) instead of followed.
    /// </summary>
    public sealed class ODictIterator : PyIterator
    {
        internal const int Reversed = 1, Keys = 2, Values = 4;

        private readonly int _kind;
        private PyODict _odict;      // di_odict
        private long _size;          // di_size
        private readonly ulong _state;  // di_state
        private PyObject _current;   // di_current

        // odictiter_new
        internal ODictIterator(PyODict od, int kind)
        {
            _kind = kind;
            var node = (kind & Reversed) != 0 ? od.Last : od.First;
            _current = node?.Key;
            _size = od.Length();
            _state = od.State;
            _odict = od;
        }

        protected override string IteratorName => "odict_iterator";

        // odictiter_nextkey
        private PyObject NextKey()
        {
            bool reversed = (_kind & Reversed) != 0;
            if (_odict == null)
                return null;
            if (_current == null)
            {
                // We're already done.
                _odict = null;
                return null;
            }
            // Check for unsupported changes.
            if (_odict.State != _state)
            {
                _odict = null;
                throw PyRuntimeError.Create("OrderedDict mutated during iteration");
            }
            if (_size != _odict.Length())
            {
                _size = -1;  // Make this state sticky
                throw PyRuntimeError.Create("OrderedDict changed size during iteration");
            }
            // Get the key.
            var node = _odict.FindNode(_current);
            if (node == null)
            {
                // Must have been deleted.
                var missing = _current;
                _current = null;
                throw PyKeyError.Create(missing);
            }
            var key = _current;
            // Advance to the next key.
            node = reversed ? node.Prev : node.Next;
            _current = node?.Key;
            return key;
        }

        // odictiter_iternext
        protected internal override PyObject IterNext()
        {
            var key = NextKey() ?? throw PyStopIteration.Create();
            // Handle the keys case.
            if ((_kind & Values) == 0)
                return key;
            if (!_odict.TryGetStoredValue(key, out var value))
            {
                _current = null;
                _odict = null;
                throw PyKeyError.Create(key);
            }
            // Handle the values case.
            if ((_kind & Keys) == 0)
                return value;
            // Handle the items case.
            return PyTuple.From(key, value);
        }

        // odictiter_reduce: a copy of the iterator's state, iterated into a list.
        internal override PyObject Reduce()
        {
            var copy = (ODictIterator)MemberwiseClone();
            var list = new List<PyObject>();
            while (true)
            {
                PyObject item;
                try { item = copy.IterNext(); }
                catch (PythonException e) when (e.PyException is PyStopIteration) { break; }
                list.Add(item);
            }
            return PyTuple.From(new[] { Builtin("iter"), PyTuple.From(new PyObject[] { new PyList(list.ToArray()) }) });
        }
    }
}
