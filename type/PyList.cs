using System;
using System.Collections.Generic;

namespace SharpPy
{
    public class PyList : PyObject
    {
        static PyList()
        {
            InitializeListDescriptors();
        }

        // CPython 3.12: Objects/listobject.c
        // Helper to get PyList storage from self (works for both PyList and list subclasses)
        private static PyList GetListStorage(PyObject self)
        {
            if (self is PyList list)
                return list;

            // A list subclass keeps its value in storage; PyNativeStorage is the one place that
            // knows how to reach it (no reflection: the getter is a normal API).
            var subclassStorage = PyNativeStorage.ForList(self);
            if (subclassStorage != null)
                return subclassStorage;

            throw PyTypeError.Create($"descriptor requires a 'list' object but received a '{self.GetTypeName()}'");
        }

        // Self check for the item-protocol descriptors, per CPython descriptor kind:
        // - method_descriptor (list_methods table entries) reports
        //   "descriptor '<name>' for 'list' objects doesn't apply to a '<T>' object"
        //   (Objects/descrobject.c call_method_descriptor)
        // - slot wrapper (sq_contains) reports
        //   "descriptor '<name>' requires a 'list' object but received a '<T>'"
        //   (Objects/abstract.c generic slot wrapper self check)
        private static PyList GetListForItemDescriptor(PyObject self, string name, bool slotStyleMessage)
        {
            if (self is PyList list)
                return list;

            var subclassStorage = PyNativeStorage.ForList(self);
            if (subclassStorage != null)
                return subclassStorage;

            throw PyTypeError.Create(slotStyleMessage
                ? $"descriptor '{name}' requires a 'list' object but received a '{self.GetTypeName()}'"
                : $"descriptor '{name}' for 'list' objects doesn't apply to a '{self.GetTypeName()}' object");
        }

        private static void InitializeListDescriptors()
        {
            var listType = PyType.ListType;

            // list.__new__(cls, /) - Objects/listobject.c list_new builds an empty list and ignores
            // the iterable (list___init___impl at listobject.c:2785 is what reads it). Registering
            // it is what makes PyType.TypeNewIsObjectNew answer "no" for a list subclass: CPython's
            // tp_new is list_new, never object_new, so the constructor guard must not treat the
            // arguments as excess.
            listType.TypeDict["__new__"] = new PyMethodDescriptor(
                "__new__", listType,
                (self, args, kwargs) => {
                    var cls = self;
                    if (cls == PyType.ListType)
                        return new PyList();
                    if (!(cls is PyClass listClass)) {
                        string foreign = PyType.TpName(cls);
                        throw PyTypeError.Create(
                            "list.__new__(" + foreign + "): " + foreign + " is not a subtype of list");
                    }
                    // list_new allocates the list layout with ob_type set to the subclass; the
                    // iterable is consumed by list.__init__, not here.
                    return new PyListSubclass(listClass) { ConstructorArgs = args };
                },
                minArgs: 0, maxArgs: int.MaxValue
            );

            // CPython 3.12: list.__hash__ is None (mutable sequence is unhashable).
            // PyObject.GetPythonHash has an explicit __hash__-is-None branch that raises
            // "unhashable type: 'list'"; without this registration list lookups found no
            // __hash__ and silently fell back to identity hashing (d[[1]] => KeyError,
            // hash([1]) => int). Same mechanism as dict/set.
            listType.TypeDict["__hash__"] = PyNone.Instance;

            // CPython 3.12: Objects/listobject.c:838-845 (list_append)
            // append method descriptor
            listType.TypeDict["append"] = new PyMethodDescriptor(
                "append", listType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"append() takes exactly one argument ({args.Length} given)");
                    var list = GetListStorage(self);
                    list.Append(args[0]);
                    return PyNone.Instance;
                },
                minArgs: 1, maxArgs: 1
            );

            // insert method descriptor
            listType.TypeDict["insert"] = new PyMethodDescriptor(
                "insert", listType,
                (self, args, kwargs) => {
                    if (args.Length != 2)
                        throw PyTypeError.Create($"insert() takes exactly two arguments ({args.Length} given)");
                    var list = GetListStorage(self);
                    var index = args[0].ToInt();
                    list.Insert(index, args[1]);
                    return PyNone.Instance;
                },
                minArgs: 2, maxArgs: 2
            );

            // remove method descriptor
            listType.TypeDict["remove"] = new PyMethodDescriptor(
                "remove", listType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"remove() takes exactly one argument ({args.Length} given)");
                    var list = GetListStorage(self);
                    list.Remove(args[0]);
                    return PyNone.Instance;
                },
                minArgs: 1, maxArgs: 1
            );

            // pop method descriptor
            listType.TypeDict["pop"] = new PyMethodDescriptor(
                "pop", listType,
                (self, args, kwargs) => {
                    if (args.Length > 1)
                        throw PyTypeError.Create($"pop() takes at most one argument ({args.Length} given)");
                    var list = GetListStorage(self);
                    var index = args.Length == 0 ? -1 : args[0].ToInt();
                    return list.Pop(index);
                },
                minArgs: 0, maxArgs: 1
            );

            // clear method descriptor
            listType.TypeDict["clear"] = new PyMethodDescriptor(
                "clear", listType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"clear() takes no arguments ({args.Length} given)");
                    var list = GetListStorage(self);
                    list.Clear();
                    return PyNone.Instance;
                },
                minArgs: 0, maxArgs: 0
            );

            // extend method descriptor
            listType.TypeDict["extend"] = new PyMethodDescriptor(
                "extend", listType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"extend() takes exactly one argument ({args.Length} given)");
                    var list = GetListStorage(self);
                    list.Extend(args[0]);
                    return PyNone.Instance;
                },
                minArgs: 1, maxArgs: 1
            );

            // index method descriptor
            listType.TypeDict["index"] = new PyMethodDescriptor(
                "index", listType,
                (self, args, kwargs) => {
                    // CPython list.index(value, start=0, stop=len): positional-only.
                    if (kwargs != null && kwargs.Length() > 0)
                        throw PyTypeError.Create("list.index() takes no keyword arguments");
                    if (args.Length < 1)
                        throw PyTypeError.Create($"index expected at least 1 argument, got {args.Length}");
                    if (args.Length > 3)
                        throw PyTypeError.Create($"index expected at most 3 arguments, got {args.Length}");
                    var list = GetListStorage(self);
                    long n = list.RawCount();
                    long start = 0, stop = n;
                    if (args.Length >= 2) {
                        var si = PyObject.TryGetIndex(args[1]);
                        if (si == null)
                            throw PyTypeError.Create("slice indices must be integers or have an __index__ method");
                        start = (long)si.Value;
                        if (start < 0) { start += n; if (start < 0) start = 0; }
                        else if (start > n) start = n;
                    }
                    if (args.Length >= 3 && args[2] != PyNone.Instance) {
                        var ti = PyObject.TryGetIndex(args[2]);
                        if (ti == null)
                            throw PyTypeError.Create("slice indices must be integers or have an __index__ method");
                        stop = (long)ti.Value;
                        if (stop < 0) { stop += n; if (stop < 0) stop = 0; }
                        else if (stop > n) stop = n;
                    }
                    return new PyInt(list.Index(args[0], start, stop));
                },
                minArgs: 1, maxArgs: 3
            );

            // count method descriptor
            listType.TypeDict["count"] = new PyMethodDescriptor(
                "count", listType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"count() takes exactly one argument ({args.Length} given)");
                    var list = GetListStorage(self);
                    var count = list.Count(args[0]);
                    return new PyInt(count);
                },
                minArgs: 1, maxArgs: 1
            );

            // reverse method descriptor
            listType.TypeDict["reverse"] = new PyMethodDescriptor(
                "reverse", listType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"reverse() takes no arguments ({args.Length} given)");
                    var list = GetListStorage(self);
                    list.Reverse();
                    return PyNone.Instance;
                },
                minArgs: 0, maxArgs: 0
            );

            // sort method descriptor (special handling for keyword-only args)
            listType.TypeDict["sort"] = new PyMethodDescriptor(
                "sort", listType,
                (self, args, kwargs) => {
                    if (args.Length > 0)
                        throw PyTypeError.Create($"sort() takes no positional arguments ({args.Length} given)");
                    var list = GetListStorage(self);

                    ParseSortKeywords(kwargs, out var key, out bool reverse);
                    list.Sort(key, reverse);
                    return PyNone.Instance;
                },
                minArgs: 0, maxArgs: 0, acceptsKwargs: true
            );

            // copy method descriptor
            listType.TypeDict["copy"] = new PyMethodDescriptor(
                "copy", listType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"copy() takes no arguments ({args.Length} given)");
                    var list = GetListStorage(self);
                    // Performance: Eliminated LINQ (.ToArray) - direct array copy + Cache
                    var copy = new PyObject[list._items.Count];
                    list._items.CopyTo(copy, 0);
                    return ListCache.Create(copy);
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/listobject.c:2785-2805 (list___init___impl)
            // __init__ method descriptor
            listType.TypeDict["__init__"] = new PyWrapperDescriptor(
                "__init__", listType,
                (self, args, kwargs) => {
                    if (args.Length > 1)
                        throw PyTypeError.Create($"list.__init__() takes at most 1 argument ({args.Length} given)");

                    // CPython 3.12: Get actual list storage (works for subclasses)
                    var list = GetListStorage(self);

                    // CPython 3.12: Empty previous contents
                    list.Clear();

                    // CPython 3.12: If iterable provided, extend with it
                    if (args.Length == 1)
                    {
                        list.Extend(args[0]);
                    }

                    return PyNone.Instance;
                },
                minArgs: 0, maxArgs: 1
            );

            // CPython 3.12: Objects/listobject.c:3155 (tp_iter slot)
            // __iter__ method descriptor - enables iteration for list subclasses
            listType.TypeDict["__iter__"] = new PyMethodDescriptor(
                "__iter__",
                listType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create("__iter__() takes no arguments");

                    var list = GetListStorage(self);
                    return list.RawIterator();
                },
                minArgs: 0,
                maxArgs: 0
            );

            // CPython list.__reversed__ (mp_length+mp_subscript sequence reversed).
            // Materialized so `reversed(list subclass)` and `list.__reversed__(l)`
            // work through attribute lookup and dir(list) reports it.
            listType.TypeDict["__reversed__"] = new PyMethodDescriptor(
                "__reversed__",
                listType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create("__reversed__() takes no arguments");
                    var list = GetListStorage(self);
                    return new PyReversedIterator(list, list.RawCount(), "list_reverseiterator");
                },
                minArgs: 0,
                maxArgs: 0
            );

            // CPython 3.12: Objects/listobject.c:2783 (list_length)
            // __len__ method descriptor - enables len() for list subclasses
            listType.TypeDict["__len__"] = new PyMethodDescriptor(
                "__len__",
                listType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create("__len__() takes no arguments");

                    var list = GetListStorage(self);
                    return new PyInt(list.RawCount());
                },
                minArgs: 0,
                maxArgs: 0
            );

            // CPython 3.12: list item protocol as *visible attributes*.
            // CPython fills tp_dict for these (typeobject.c add_operators/update_one_slot
            // plus the list_methods entries), so `l.__setitem__(...)`, `list.__getitem__(l, i)`
            // and dir(list) all work. Materializing them here makes the attribute-lookup
            // path agree with the VM-internal LookupSpecial path; each wrapper calls the
            // same CLR implementation used by BINARY_SUBSCR/STORE_SUBSCR/DELETE_SUBSCR/
            // CONTAINS_OP, so syntax and explicit dunder calls stay semantically identical
            // (slices, negative indices, error texts, subclass storage).
            //
            // Error texts below are measured against CPython 3.12.15: list __getitem__/...
            // come from listobject.c (METH_O / METH_VARARGS "OO"), __contains__ from the
            // sq_contains slot, so their argcount messages differ.

            // CPython 3.12: Objects/listobject.c list_getitem (METH_O)
            // CPython: list.__getitem__ is a METH_O method_descriptor (not a slot wrapper).
            listType.TypeDict["__getitem__"] = new PyMethodDescriptor(
                "__getitem__",
                listType,
                (self, args, kwargs) =>
                {
                    if (kwargs != null && kwargs.InternalDict.Count > 0)
                        throw PyTypeError.Create("list.__getitem__() takes no keyword arguments");
                    if (args.Length != 1)
                        throw PyTypeError.Create($"list.__getitem__() takes exactly one argument ({args.Length} given)");

                    return GetListForItemDescriptor(self, "__getitem__", false).GetItem(args[0]);
                });

            // CPython 3.12: Objects/listobject.c list_setitem (METH_VARARGS, "OO")
            listType.TypeDict["__setitem__"] = new PyWrapperDescriptor(
                "__setitem__",
                listType,
                (self, args, kwargs) =>
                {
                    if (kwargs != null && kwargs.InternalDict.Count > 0)
                        throw PyTypeError.Create("list.__setitem__() takes no keyword arguments");
                    if (args.Length != 2)
                        throw PyTypeError.Create($" expected 2 arguments, got {args.Length}");

                    GetListForItemDescriptor(self, "__setitem__", false).SetItem(args[0], args[1]);
                    return PyNone.Instance;
                });

            // CPython 3.12: Objects/listobject.c list_delitem (METH_O) routed through
            // list_ass_subscript, hence "list assignment index out of range" for OOB.
            listType.TypeDict["__delitem__"] = new PyWrapperDescriptor(
                "__delitem__",
                listType,
                (self, args, kwargs) =>
                {
                    if (kwargs != null && kwargs.InternalDict.Count > 0)
                        throw PyTypeError.Create("list.__delitem__() takes no keyword arguments");
                    if (args.Length != 1)
                        throw PyTypeError.Create($"expected 1 argument, got {args.Length}");

                    var list = GetListForItemDescriptor(self, "__delitem__", false);
                    var key = args[0];

                    if (key is PySlice slice)
                    {
                        // CPython 3.12: list_ass_subscript with slice (value == NULL)
                        var length = list.RawCount();
                        var (start, stop, step) = slice.StorageIndices(length);
                        int count = PySlice.GetLength(start, stop, step);
                        list.DeleteSlice(start, step, count);
                    }
                    else
                    {
                        int idx = PyObject.GetIndex(key, "list");
                        int len = list.RawCount();
                        if (idx < 0) idx += len;
                        if (idx < 0 || idx >= len)
                            throw PyIndexError.Create("list assignment index out of range");
                        list.Pop(idx);
                    }

                    return PyNone.Instance;
                });

            // CPython 3.12: Objects/typeobject.c:9629 SQSLOT(__contains__, sq_contains)
            listType.TypeDict["__contains__"] = new PyWrapperDescriptor(
                "__contains__",
                listType,
                (self, args, kwargs) =>
                {
                    if (kwargs != null && kwargs.InternalDict.Count > 0)
                        throw PyTypeError.Create("list.__contains__() takes no keyword arguments");
                    if (args.Length != 1)
                        throw PyTypeError.Create($"expected 1 argument, got {args.Length}");

                    return GetListForItemDescriptor(self, "__contains__", true).Contains(args[0]);
                });

            // CPython 3.12: list sq_concat/sq_multiply/nb_inplace slot wrappers
            // (Objects/typeobject.c slotdefs + listobject.c METH_VARARGS list_inplace_*/
            // list_repeat). Error strings measured against CPython 3.12.15; the varargs
            // list_repeat family has the distinctive leading-space message.
            // returnsSelf: nb_inplace_add / nb_inplace_multiply return the object itself, which for
            // a subclass instance is the user's object and not the storage. Returning the storage
            // there made x += [...] rebind a plain list and broke aliasing (y = x saw nothing).
            void regListBin(string name, bool varargsStyle, Func<PyList, PyObject, PyObject> impl,
                bool returnsSelf = false)
            {
                listType.TypeDict[name] = new PyWrapperDescriptor(name, listType, (self, args, kwargs) =>
                {
                    var list = GetListForItemDescriptor(self, name, true);
                    if (kwargs != null && kwargs.InternalDict.Count > 0)
                        throw PyTypeError.Create($"list.{name}() takes no keyword arguments");
                    if (args.Length != 1)
                        throw PyTypeError.Create(varargsStyle
                            ? $" expected 1 argument, got {args.Length}"
                            : $"expected 1 argument, got {args.Length}");
                    var produced = impl(list, args[0]);
                    return returnsSelf ? self : produced;
                });
            }
            regListBin("__add__", false, (list, other) =>
            {
                var result = list.Add(other);
                if (result is PyNotImplemented)
                    throw PyTypeError.Create($"can only concatenate list (not \"{other.GetTypeName()}\") to list");
                return result;
            });
            // list_repeat coerces with __index__; the C slot raises instead of
            // returning NotImplemented (measured: 'float' object cannot be
            // interpreted as an integer).
            PyObject ListRepeat(PyList list, PyObject other)
            {
                var result = list.Multiply(other);
                if (result is PyNotImplemented)
                    throw PyTypeError.Create($"'{other.GetTypeName()}' object cannot be interpreted as an integer");
                return result;
            }
            regListBin("__mul__", true, ListRepeat);
            regListBin("__rmul__", true, ListRepeat);
            regListBin("__imul__", true, (list, other) =>
            {
                var repeated = (PyList)ListRepeat(list, other);
                list._items.Clear();
                list._items.AddRange(repeated._items);
                return list; // CPython returns the same list object
            }, returnsSelf: true);
            regListBin("__iadd__", false, (list, other) =>
            {
                list.Extend(other); // accepts any iterable; raises like list.extend
                return list;
            }, returnsSelf: true);

            // CPython 3.12: list tp_richcompare wrappers; non-list other -> NotImplemented.
            string[] listComparisonNames = { "__lt__", "__le__", "__eq__", "__ne__", "__gt__", "__ge__" };
            for (int i = 0; i < listComparisonNames.Length; i++)
            {
                var op = (CompareOp)i;
                string name = listComparisonNames[i];
                listType.TypeDict[name] = new PyWrapperDescriptor(name, listType, (self, args, kwargs) =>
                {
                    var list = GetListForItemDescriptor(self, name, true);
                    if (kwargs != null && kwargs.InternalDict.Count > 0)
                        throw PyTypeError.Create($"list.{name}() takes no keyword arguments");
                    if (args.Length != 1)
                        throw PyTypeError.Create($"expected 1 argument, got {args.Length}");
                    return list.RichCompare(args[0], op);
                });
            }

            // CPython 3.12: Objects/listobject.c:2867 (list methods table)
            // {"__class_getitem__", Py_GenericAlias, METH_O|METH_CLASS, PyDoc_STR("See PEP 585")},
            // METH_CLASS means it's a classmethod - first arg is the class itself
            listType.TypeDict["__class_getitem__"] = new PyBuiltinClassMethod("__class_getitem__",
                (cls, arg) => new PyGenericAlias(cls as PyType ?? throw PyTypeError.Create("Expected type"), arg)
            );
        }

        private List<PyObject> _items;
        // Performance: Eliminated LINQ (.ToArray) - direct array copy
        public PyObject[] Items
        {
            get
            {
                var items = new PyObject[_items.Count];
                _items.CopyTo(items, 0);
                return items;
            }
        }

        public PyList(params PyObject[] items)
        {
            _items = new List<PyObject>(items);
        }

        public PyList(IEnumerable<PyObject> items)
        {
            _items = new List<PyObject>(items);
        }

        public override string GetTypeName() => "list";
        public override PyType GetPyType() => PyType.ListType;

        // CPython 3.12: Objects/listobject.c - list_bool
        // Optimized: Direct count check, no MRO traversal
        public override bool IsTrue() => _items.Count > 0;

        public override string ToString()
        {
            // Performance: CPython-style - pre-calculate size, allocate once, direct copy
            if (_items.Count == 0) return "[]";

            // Step 1: Calculate total length
            int totalLength = 2; // "[]"
            for (int i = 0; i < _items.Count; i++)
            {
                if (i > 0) totalLength += 2; // ", "
                totalLength += _items[i].ToRepr().Value.Length;
            }

            // Step 2: Allocate exact size
            var chars = new char[totalLength];
            int pos = 0;
            chars[pos++] = '[';

            // Step 3: Direct copy
            for (int i = 0; i < _items.Count; i++)
            {
                if (i > 0)
                {
                    chars[pos++] = ',';
                    chars[pos++] = ' ';
                }

                string itemRepr = _items[i].ToRepr().Value;
                itemRepr.CopyTo(0, chars, pos, itemRepr.Length);
                pos += itemRepr.Length;
            }

            chars[pos] = ']';
            return new string(chars);
        }

        public override PyString ToRepr()
        {
            if (_items.Count == 0) return StringCache.GetOrCreate("[]");

            // Objects/listobject.c:379 list_repr calls Py_ReprEnter: a list that contains itself
            // prints [1, [...]] instead of recursing forever.
            if (!PyReprGuard.Enter(this))
                return StringCache.GetOrCreate("[...]");
            try
            {
                return ToReprContents();
            }
            finally
            {
                PyReprGuard.Leave(this);
            }
        }

        private PyString ToReprContents()
        {
            // Performance: string.Create() - CPython-style single allocation

            // Step 1: Calculate total length and cache reprs
            var reprs = new string[_items.Count];
            int totalLength = 2; // "[]"
            for (int i = 0; i < _items.Count; i++)
            {
                reprs[i] = _items[i].ToRepr().Value;
                if (i > 0) totalLength += 2; // ", "
                totalLength += reprs[i].Length;
            }

            // Step 2: string.Create with single allocation
            var result = string.Create(totalLength, reprs, (span, items) =>
            {
                int pos = 0;
                span[pos++] = '[';

                for (int i = 0; i < items.Length; i++)
                {
                    if (i > 0)
                    {
                        span[pos++] = ',';
                        span[pos++] = ' ';
                    }

                    items[i].AsSpan().CopyTo(span.Slice(pos));
                    pos += items[i].Length;
                }

                span[pos] = ']';
            });

            return StringCache.GetOrCreate(result);
        }
        /// <summary>
        /// list_length (Objects/listobject.c) - the count straight off the layout. Reachable without
        /// virtual dispatch on purpose: <c>list.__len__(x)</c> is the base's own method_descriptor and
        /// keeps reporting the real length even when x's class declares <c>__len__</c>, which is how
        /// _warnings can scan <c>sys.filters</c> without a filter list being able to interfere.
        /// Python-level <c>len(x)</c> goes through the slot and may call the declared method.
        /// </summary>
        internal int RawCount() => _items.Count;

        /// <summary>The layout iterator, non-virtual for the same reason as RawCount.</summary>
        internal PyObject RawIterator() => new PyListIterator(this);

        /// <summary>list_contains, non-virtual for the same reason.</summary>
        internal bool RawContains(PyObject item)
        {
            foreach (var listItem in _items)
            {
                if (AreEqual(listItem, item))
                    return true;
            }
            return false;
        }

        public override int Length() => RawCount();
        public override bool PyBoolValue() => _items.Count > 0;

        // 인덱싱 지원
        public PyObject GetItem(int index)
        {
            if (index < 0) index += _items.Count;
            if (index < 0 || index >= _items.Count)
                throw PyIndexError.Create("list index out of range");
            return _items[index];
        }
        
        // PyObject.GetItem 오버라이드 - 슬라이싱 및 인덱싱 지원
        // CPython 3.12: Objects/listobject.c:268-291 (list_subscript)
        public override PyObject GetItem(PyObject index)
        {
            if (index is PySlice slice)
            {
                // 슬라이싱 처리
                var (start, stop, step) = slice.StorageIndices(_items.Count);

                int count = PySlice.GetLength(start, stop, step);
                var result = new List<PyObject>(count);
                for (int n = 0, position = start; n < count; n++, position = unchecked(position + step))
                    result.Add(_items[position]);

                return new PyList(result);
            }
            else
            {
                // Try __index__ protocol for integer-like objects
                int idx = PyObject.GetIndex(index, "list");
                return GetItem(idx);
            }
        }

        internal void DeleteSlice(int start, int step, int count)
            => PySlice.DeleteItems(_items, start, step, count);

        public void SetItem(int index, PyObject value)
        {
            if (index < 0) index += _items.Count;
            if (index < 0 || index >= _items.Count)
                throw PyIndexError.Create("list assignment index out of range");
            _items[index] = value;
        }

        public override void SetItem(PyObject index, PyObject value)
        {
            if (index is PyInt intIndex)
            {
                SetItem((int)intIndex.Value, value);
            }
            else if (index is PySlice slice)
            {
                // Handle slice assignment: list[start:stop] = values
                // CPython 3.12: Objects/listobject.c:716-856 - list_ass_subscript
                // Must accept any iterable, not just lists
                PyList valueList;
                if (value is PyList pl)
                {
                    valueList = pl;
                }
                else
                {
                    // Convert iterable to list
                    var tempList = new List<PyObject>();
                    PyObject iterator;
                    try
                    {
                        iterator = value.GetIterator();
                    }
                    catch (PythonException ex) when (ex.PyException is PyTypeError te
                                 && te.Message.EndsWith("object is not iterable"))
                    {
                        // CPython list_ass_subscript: PySequence_Fast wording
                        // "must assign iterable to extended slice".
                        throw PyTypeError.Create("must assign iterable to extended slice");
                    }
                    try
                    {
                        while (true)
                        {
                            var item = iterator.Next();
                            tempList.Add(item);
                        }
                    }
                    catch (PythonException ex) when (ex.PyException is PyStopIteration)
                    {
                        // Normal iteration end
                    }
                    valueList = new PyList(tempList);
                }

                var (start, stop, step) = slice.StorageIndices(_items.Count);

                if (step == 1)
                {
                    // Simple slice assignment: replace items[start:stop] with valueList
                    _items.RemoveRange(start, stop - start);
                    _items.InsertRange(start, valueList.Items);
                }
                else
                {
                    // Extended slice assignment: must have same number of items
                    int sliceLength = PySlice.GetLength(start, stop, step);
                    if (sliceLength != valueList._items.Count)
                    {
                        throw PyValueError.Create($"attempt to assign sequence of size {valueList._items.Count} to extended slice of size {sliceLength}");
                    }
                    // CPython snapshots self-assignment before writing. Other
                    // lists can be read directly: this loop invokes no Python.
                    var replacement = ReferenceEquals(this, valueList)
                        ? new List<PyObject>(_items) : valueList._items;
                    for (int i = 0, position = start; i < sliceLength; i++, position = unchecked(position + step))
                    {
                        _items[position] = replacement[i];
                    }
                }
            }
            else
            {
                throw PyTypeError.Create($"list indices must be integers or slices, not {index.GetTypeName()}");
            }
        }

        // 리스트 조작 메서드들
        public void Append(PyObject item)
        {
            _items.Add(item);
        }

        public void Insert(int index, PyObject item)
        {
            if (index < 0) index += _items.Count;
            index = Math.Max(0, Math.Min(index, _items.Count));
            _items.Insert(index, item);
        }

        public PyObject Pop(int index = -1)
        {
            if (_items.Count == 0)
                throw PyIndexError.Create("pop from empty list");

            if (index == -1) index = _items.Count - 1;
            if (index < 0) index += _items.Count;
            if (index < 0 || index >= _items.Count)
                throw PyIndexError.Create("pop index out of range");

            var item = _items[index];
            _items.RemoveAt(index);
            return item;
        }

        public void Remove(PyObject item)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (AreEqual(_items[i], item))
                {
                    _items.RemoveAt(i);
                    return;
                }
            }
            throw PyValueError.Create("list.remove(x): x not in list");
        }

        public void Clear()
        {
            _items.Clear();
        }

        public void Extend(PyObject iterable)
        {
            // CPython list_extend semantics: InplaceAdd already implements the
            // list/tuple fast paths and the general iterator path
            // (Objects/listobject.c:860-970). The previous placeholder only
            // accepted PyList and reported "'PyObject' object is not iterable".
            InplaceAdd(iterable);
        }

        public int Index(PyObject item)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (AreEqual(_items[i], item))
                    return i;
            }
            throw PyValueError.Create($"{item.ToRepr().Value} is not in list");
        }

        // CPython list.index(value, start=0, stop=len): search [start, stop) after
        // caller-provided negative adjustment/clamping.
        public int Index(PyObject item, long start, long stop)
        {
            for (long i = start; i < stop; i++)
            {
                if (AreEqual(_items[(int)i], item))
                    return (int)i;
            }
            throw PyValueError.Create($"{item.ToRepr().Value} is not in list");
        }

        public int Count(PyObject item)
        {
            int count = 0;
            foreach (var listItem in _items)
            {
                if (AreEqual(listItem, item))
                    count++;
            }
            return count;
        }

        public void Reverse()
        {
            _items.Reverse();
        }

        internal static void ParseSortKeywords(PyDict kwargs, out PyObject key, out bool reverse)
        {
            key = null;
            reverse = false;
            PyObject reverseArg = null;
            if (kwargs != null)
            {
                foreach (var pair in kwargs.InternalDict)
                {
                    if (pair.Key is not PyString name)
                        throw PyTypeError.Create("keywords must be strings");
                    if (name.Value == "key") key = pair.Value;
                    else if (name.Value == "reverse") reverseArg = pair.Value;
                    else throw PyTypeError.Create($"'{name.Value}' is an invalid keyword argument for sort()");
                }
            }
            if (reverseArg != null)
                reverse = reverseArg.IsTrue();
        }

        public void Sort(PyObject? key = null, bool reverse = false)
        {
            var saved = _items;
            if ((key == null || key == PyNone.Instance) && saved.Count < 64)
            {
                if (saved.Count < 2) return;
                _items = new List<PyObject>();
                if (reverse) saved.Reverse();
                try
                {
                    PyListSort.SortSmall(saved);
                    if (_items.Capacity != 0)
                        throw PyValueError.Create("list modified during sort");
                }
                finally
                {
                    if (reverse) saved.Reverse();
                    _items = saved;
                }
                return;
            }
            var values = saved.ToArray();
            _items = new List<PyObject>();
            bool reversed = false;
            try
            {
                var keys = values;
                if (key != null && key != PyNone.Instance)
                {
                    keys = new PyObject[values.Length];
                    for (int i = 0; i < values.Length; i++)
                        keys[i] = key.Call(new[] { values[i] }, null);
                }
                if (reverse)
                {
                    Array.Reverse(values);
                    if (!ReferenceEquals(keys, values)) Array.Reverse(keys);
                    reversed = true;
                }
                new PyListSort(keys, values).Sort();
                if (_items.Capacity != 0)
                    throw PyValueError.Create("list modified during sort");
            }
            finally
            {
                if (reversed) Array.Reverse(values);
                saved.Clear();
                saved.AddRange(values);
                _items = saved;
            }
        }

        private static bool AreEqual(PyObject a, PyObject b)
        {
            if (a == b) return true;
            if (a == null || b == null) return false;

            return CompareBool(a, b, CompareOp.EQ);
        }

        // CPython 3.12: Use descriptor protocol for attribute access
        public override PyObject GetAttribute(string name)
        {
            return GenericGetAttribute(name);
        }

        // 이터레이터 지원
        public override PyObject GetIterator() => RawIterator();
        
        #region Type Conversion (CPython Compatible)

        // === To* Methods: Value Extraction (PyList → C# basic types) ===
        
        /// <summary>
        /// CPython PyLong_AsLong 호환: PyList는 일반적으로 int로 변환될 수 없음
        /// </summary>
        public override int ToInt()
        {
            throw PyTypeError.Create($"int() argument must be a string, a bytes-like object or a real number, not 'list'");
        }
        
        /// <summary>
        /// CPython PyFloat_AsDouble 호환: PyList는 일반적으로 float로 변환될 수 없음
        /// </summary>
        public override double ToFloat()
        {
            throw PyTypeError.Create($"float() argument must be a string or a real number, not 'list'");
        }
        
        
        // === As* Methods: Type Conversion (PyList → PyObject types) ===
        
        /// <summary>
        /// CPython 호환: PyList를 PyList로 변환 (복사본 생성)
        /// </summary>
        public override PyList AsList()
        {
            // CPython list() 생성자 동작: 새로운 복사본 생성
            // Performance: Eliminated LINQ (.ToArray) - direct array copy + Cache
            var copy = new PyObject[_items.Count];
            _items.CopyTo(copy, 0);
            return ListCache.Create(copy);
        }
        
        /// <summary>
        /// CPython 호환: PyList를 PyTuple로 변환
        /// </summary>
        public override PyTuple AsTuple()
        {
            // CPython tuple() 생성자 동작: 리스트 요소들로 튜플 생성
            // Performance: Eliminated LINQ (.ToArray) - direct array copy + Cache
            var items = new PyObject[_items.Count];
            _items.CopyTo(items, 0);
            return TupleCache.GetOrCreate(items);
        }
        
        /// <summary>
        /// CPython 호환: PyList를 PyBool로 변환
        /// </summary>
        public override PyBool AsBool()
        {
            return PyBool.FromBool(_items.Count > 0);
        }
        
        /// <summary>
        /// CPython 호환: PyList를 PyString으로 변환 (str() 호출과 동일)
        /// </summary>
        public override string ToClrString()
        {
            return ToRepr().Value; // CPython에서 str(list)는 repr(list)와 동일
        }

        #endregion
        
        /// <summary>
        /// CPython __contains__ 메소드 구현 - in 연산자 지원
        /// </summary>
        public override PyBool Contains(PyObject item) => PyBool.FromBool(RawContains(item));

        /// <summary>
        /// PyList 동등성 비교 - CPython 호환
        /// </summary>
        protected override PyObject PyEquals(PyObject other)
        {
            // list_richcompare tests the other operand with PyList_Check, which accepts subclasses,
            // so [1, 2] == C([1, 2]) has to see through the user object to its storage.
            other = PyNativeStorage.Unwrap(other);
            if (other is not PyList otherList)
                return PyBool.False;

            // 길이가 다르면 False
            if (_items.Count != otherList._items.Count)
                return PyBool.False;

            // 각 요소를 비교
            for (int i = 0; i < _items.Count; i++)
            {
                if (!AreEqual(_items[i], otherList._items[i]))
                    return PyBool.False;
            }

            return PyBool.True;
        }

        /// <summary>
        /// CPython __add__ 구현 - 리스트 연결
        /// </summary>
        public override PyObject Add(PyObject other)
        {
            // list_concat tests the right operand with PyList_Check, which accepts subclasses, so
            // [2] + C([1]) is [2, 1] (a plain list, which is what CPython returns).
            other = PyNativeStorage.Unwrap(other);
            if (other is not PyList otherList)
                throw PyTypeError.Create($"can only concatenate list (not \"{other.GetTypeName()}\") to list");

            // 새로운 리스트 생성 (원본 리스트는 변경하지 않음)
            var newItems = new PyObject[_items.Count + otherList._items.Count];
            _items.CopyTo(newItems, 0);
            otherList._items.CopyTo(newItems, _items.Count);

            return ListCache.Create(newItems);
        }

        // CPython 3.12: list.__mul__ and list.__rmul__ - Repeat list n times
        // CPython: Objects/listobject.c:567-589 (list_repeat)
        // Note: __rmul__ is handled by VM's reverse operation dispatch (TryReverseBinaryOp)
        public override PyObject Multiply(PyObject other)
        {
            if (other is not PyInt pyInt)
                return PyNotImplemented.Instance;

            int n = (int)pyInt.Value;
            if (n <= 0)
            {
                // Empty list for 0 or negative multiplier
                return ListCache.Create(new PyObject[0]);
            }

            // Create new list with repeated elements
            int itemCount = _items.Count;
            var newItems = new PyObject[itemCount * n];

            for (int i = 0; i < n; i++)
            {
                _items.CopyTo(newItems, i * itemCount);
            }

            return ListCache.Create(newItems);
        }

        /// <summary>
        /// CPython 3.12: list.__iadd__ - In-place concatenation (+=)
        /// CPython: Objects/listobject.c:list_inplace_concat (lines 856-970)
        /// Modifies the list in-place by extending it with items from other
        /// </summary>
        public override PyObject? InplaceAdd(PyObject other)
        {
            // CPython: Objects/listobject.c:870-872 - Special cases: lists and tuples use fast path
            // Special cases:
            //   1) lists and tuples which can use PySequence_Fast ops
            //   2) extending self to self requires making a copy first
            if (other is PyList otherList)
            {
                // CPython: Objects/listobject.c:875-909 - Fast path for lists
                _items.AddRange(otherList._items);
            }
            else if (other is PyTuple otherTuple)
            {
                // CPython: Objects/listobject.c:870-872 - Fast path for tuples
                // PyTuple_CheckExact(iterable) -> use PySequence_Fast
                for (int i = 0; i < otherTuple.Length(); i++)
                {
                    _items.Add(otherTuple.GetItem(i));
                }
            }
            else
            {
                // CPython: Objects/listobject.c:911-970 - General iterable path
                // it = PyObject_GetIter(iterable)
                var iterator = other.GetIterator();
                try
                {
                    // CPython: Objects/listobject.c:939-960 - Run iterator to exhaustion
                    while (true)
                    {
                        var item = iterator.Next();
                        _items.Add(item);
                    }
                }
                catch (PythonException ex) when (ex.PyException is PyStopIteration)
                {
                    // CPython: Objects/listobject.c:944-948 - Handle StopIteration
                    // End of iteration
                }
            }

            // CPython: Objects/listobject.c:967 - Return None (but for iadd, CPython returns self via BINARY_OP)
            // Return self (same object)
            return this;
        }

        /// <summary>
        /// CPython 3.12: list.__imul__ - In-place repetition (*=)
        /// CPython: Objects/listobject.c:list_inplace_repeat (lines 601-619)
        /// Modifies the list in-place by repeating its elements
        /// </summary>
        public override PyObject? InplaceMultiply(PyObject other)
        {
            if (other is not PyInt pyInt)
                return PyNotImplemented.Instance;

            int n = (int)pyInt.Value;

            // CPython: Objects/listobject.c:608-612 - Handle n <= 0
            if (n <= 0)
            {
                _items.Clear();
                return this;
            }

            // CPython: Objects/listobject.c:614-617 - Repeat elements n times
            if (n == 1)
            {
                // No change needed
                return this;
            }

            // Create a copy of original items
            var originalItems = _items.ToArray();
            int itemCount = originalItems.Length;

            // Extend list to hold n copies
            for (int i = 1; i < n; i++)
            {
                _items.AddRange(originalItems);
            }

            // CPython: Return self (same object)
            return this;
        }

        /// <summary>
        /// CPython 3.12: list rich comparison - Lexicographic comparison
        /// CPython: Objects/listobject.c:list_richcompare (lines 2710-2772)
        /// </summary>
        public override PyObject RichCompare(PyObject other, CompareOp op)
        {
            // CPython: Objects/listobject.c:2715-2716 - Type check
            if (other is not PyList otherList)
                return PyNotImplemented.Instance;

            // CPython: Objects/listobject.c:2721-2728 - Shortcut: if lengths differ and op is EQ/NE
            if (op == CompareOp.EQ || op == CompareOp.NE)
            {
                if (_items.Count != otherList._items.Count)
                    return PyBool.FromBool(op == CompareOp.NE);

                // CPython: Objects/listobject.c:2730-2747 - Search for first index where items differ
                for (int i = 0; i < _items.Count; i++)
                {
                    if (!CompareBool(_items[i], otherList._items[i], CompareOp.EQ))
                        return PyBool.FromBool(op == CompareOp.NE);
                }

                return PyBool.FromBool(op == CompareOp.EQ);
            }

            // CPython: Objects/listobject.c:2730-2770 - Search for first index where items differ
            int minLen = Math.Min(_items.Count, otherList._items.Count);
            for (int i = 0; i < minLen; i++)
            {
                // First check if items are equal
                if (!CompareBool(_items[i], otherList._items[i], CompareOp.EQ))
                {
                    // Items differ - compare using the actual operator
                    return Compare(_items[i], otherList._items[i], op);
                }
                // Items are equal, continue to next pair
            }

            // CPython: Objects/listobject.c:2749-2752 - No more items to compare, compare sizes
            return op switch
            {
                CompareOp.LT => PyBool.FromBool(_items.Count < otherList._items.Count),
                CompareOp.LE => PyBool.FromBool(_items.Count <= otherList._items.Count),
                CompareOp.GT => PyBool.FromBool(_items.Count > otherList._items.Count),
                CompareOp.GE => PyBool.FromBool(_items.Count >= otherList._items.Count),
                _ => PyNotImplemented.Instance
            };
        }
    }
}
