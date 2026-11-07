using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// Python dict 타입 구현 - 해시 가능한 키와 임의 값의 매핑
    /// </summary>
    public class PyDict : PyObject
    {
        static PyDict()
        {
            InitializeDictDescriptors();
        }

        /// <summary>
        /// Initialize dict type descriptors (CPython 3.12 compatible)
        /// CPython reference: Objects/dictobject.c:3600-3700 - mapp_methods
        /// </summary>
        public static void InitializeDictDescriptors()
        {
            var dictType = PyType.DictType;

            // CPython 3.12: dict.__hash__ is None (mutable mapping is unhashable).
            // Registering None routes PyObject.GetPythonHash through its existing
            // __hash__-is-None branch, so mutable builtins used as dict keys raise
            // "unhashable type: 'list'" etc. instead of silently hashing by identity.
            dictType.TypeDict["__hash__"] = PyNone.Instance;
            // Helper to get PyDict from self (handles both PyDict and PyClassInstance dict subclasses)
            static PyDict GetDict(PyObject self, PyType dictType)
            {
                if (self is PyDict dict)
                    return dict;
                throw PyTypeError.Create($"descriptor requires a 'dict' object but received a '{self.GetTypeName()}'");
            }

            // CPython 3.12: Objects/dictobject.c:3627-3630 - dict_get
            // D.get(k[,d]) -> D[k] if k in D, else d.  d defaults to None.
            dictType.TypeDict["get"] = new PyMethodDescriptor(
                "get", dictType,
                (self, args, kwargs) => {
                    // CPython 3.12: Include/dictobject.h:17-18 - PyDict_Check accepts subclasses
                    if (!self.GetPyType().IsSubclassOf(dictType))
                        throw PyTypeError.Create($"descriptor 'get' requires a 'dict' object but received a '{self.GetTypeName()}'");

                    if (args.Length < 1 || args.Length > 2)
                        throw PyTypeError.Create(args.Length < 1 ? "get expected at least 1 argument, got 0" : $"get expected at most 2 arguments, got {args.Length}");

                    var key = args[0];
                    var defaultValue = args.Length == 2 ? args[1] : PyNone.Instance;

                    // dict_get reads the table: a subclass's __getitem__ is not involved.
                    return (self as PyDict).Get(key, defaultValue);
                },
                minArgs: 1, maxArgs: 2
            );

            // CPython 3.12: Objects/dictobject.c:3672-3675 - dict_keys
            // D.keys() -> a set-like object providing a view on D's keys
            dictType.TypeDict["keys"] = new PyMethodDescriptor(
                "keys", dictType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"keys() takes no arguments ({args.Length} given)");

                    // CPython 3.12: Include/dictobject.h:17-18 - PyDict_Check accepts subclasses
                    if (!self.GetPyType().IsSubclassOf(dictType))
                        throw PyTypeError.Create($"descriptor 'keys' requires a 'dict' object but received a '{self.GetTypeName()}'");

                    return new PyDictView(GetDict(self, dictType), DictViewKind.Keys);
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/dictobject.c:3677-3680 - dict_values
            // D.values() -> an object providing a view on D's values
            dictType.TypeDict["values"] = new PyMethodDescriptor(
                "values", dictType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"values() takes no arguments ({args.Length} given)");

                    if (!self.GetPyType().IsSubclassOf(dictType))
                        throw PyTypeError.Create($"descriptor 'values' requires a 'dict' object but received a '{self.GetTypeName()}'");

                    return new PyDictView(GetDict(self, dictType), DictViewKind.Values);
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/dictobject.c:3682-3685 - dict_items
            // D.items() -> a set-like object providing a view on D's items
            dictType.TypeDict["items"] = new PyMethodDescriptor(
                "items", dictType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"items() takes no arguments ({args.Length} given)");

                    if (!self.GetPyType().IsSubclassOf(dictType))
                        throw PyTypeError.Create($"descriptor 'items' requires a 'dict' object but received a '{self.GetTypeName()}'");

                    return new PyDictView(GetDict(self, dictType), DictViewKind.Items);
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/dictobject.c:3632-3635 - dict_setdefault
            // D.setdefault(k[,d]) -> D.get(k,d), also set D[k]=d if k not in D
            dictType.TypeDict["setdefault"] = new PyMethodDescriptor(
                "setdefault", dictType,
                (self, args, kwargs) => {
                    // CPython 3.12: Include/dictobject.h:17-18 - PyDict_Check accepts subclasses
                    // Check if self is dict or dict subclass
                    if (!self.GetPyType().IsSubclassOf(dictType))
                        throw PyTypeError.Create($"descriptor 'setdefault' requires a 'dict' object but received a '{self.GetTypeName()}'");

                    if (args.Length < 1 || args.Length > 2)
                        throw PyTypeError.Create(args.Length < 1 ? "setdefault expected at least 1 argument, got 0" : $"setdefault expected at most 2 arguments, got {args.Length}");

                    var key = args[0];
                    var defaultValue = args.Length == 2 ? args[1] : PyNone.Instance;

                    // For C# PyDict, use direct method
                    if (self is PyDict dict)
                        return dict.SetDefault(key, defaultValue);

                    // For Python dict subclasses, use __contains__, __getitem__, __setitem__
                    // CPython: if key in D: return D[key] else: D[key] = default; return default
                    var contains = self.Contains(key);
                    if (contains == PyBool.True)
                        return self.GetItem(key);

                    self.SetItem(key, defaultValue);
                    return defaultValue;
                },
                minArgs: 1, maxArgs: 2
            );

            // CPython 3.12: Objects/dictobject.c:3637-3640 - dict_pop
            // D.pop(k[,d]) -> v, remove specified key and return the corresponding value.
            // If key is not found, d is returned if given, otherwise KeyError is raised
            dictType.TypeDict["pop"] = new PyMethodDescriptor(
                "pop", dictType,
                (self, args, kwargs) => {
                    // CPython 3.12: Include/dictobject.h:17-18 - PyDict_Check accepts subclasses
                    if (!self.GetPyType().IsSubclassOf(dictType))
                        throw PyTypeError.Create($"descriptor 'pop' requires a 'dict' object but received a '{self.GetTypeName()}'");

                    if (args.Length < 1 || args.Length > 2)
                        throw PyTypeError.Create(args.Length < 1 ? "pop expected at least 1 argument, got 0" : $"pop expected at most 2 arguments, got {args.Length}");

                    var key = args[0];
                    var defaultValue = args.Length == 2 ? args[1] : null;

                    // dict_pop_default works on the storage: it never calls a subclass's __delitem__,
                    // which is how OrderedDict.popitem can use it from inside its own __delitem__.
                    return (self as PyDict).Pop(key, defaultValue);
                },
                minArgs: 1, maxArgs: 2
            );

            // CPython 3.12: Objects/dictobject.c:3642-3645 - dict_popitem
            // D.popitem() -> (k, v), remove and return some (key, value) pair as a
            // 2-tuple; but raise KeyError if D is empty.
            dictType.TypeDict["popitem"] = new PyMethodDescriptor(
                "popitem", dictType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"popitem() takes no arguments ({args.Length} given)");
                    if (self is not PyDict dict)
                        throw PyTypeError.Create($"descriptor 'popitem' requires a 'dict' object but received a '{self.GetTypeName()}'");

                    return dict.PopItem();
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/dictobject.c:3687-3690 - dict_clear
            // D.clear() -> None.  Remove all items from D.
            dictType.TypeDict["clear"] = new PyMethodDescriptor(
                "clear", dictType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"clear() takes no arguments ({args.Length} given)");
                    if (self is not PyDict dict)
                        throw PyTypeError.Create($"descriptor 'clear' requires a 'dict' object but received a '{self.GetTypeName()}'");

                    return dict.Clear();
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/dictobject.c:3647-3650 - dict_update
            // D.update([E, ]**F) -> None.  Update D from dict/iterable E and F.
            // If E is present and has a .keys() method, then does:  for k in E: D[k] = E[k]
            // If E is present and lacks a .keys() method, then does:  for k, v in E: D[k] = v
            // In either case, this is followed by: for k in F:  D[k] = F[k]
            dictType.TypeDict["update"] = new PyMethodDescriptor(
                "update", dictType,
                (self, args, kwargs) => {
                    var target = RequireDictOperand(self, "update");

                    if (args.Length > 1)
                        throw PyTypeError.Create($"update expected at most 1 argument, got {args.Length}");

                    // CPython dict_update: merge E first, then apply **F (F wins on collision)
                    if (args.Length == 1)
                        UpdateCommon(target, args[0]);

                    if (kwargs != null)
                    {
                        foreach (var entry in kwargs.GetInternalDict())
                            target.SetItemStorage(entry.Key, entry.Value);
                    }
                    return PyNone.Instance;
                },
                minArgs: 0, maxArgs: 1
, acceptsKwargs: true);

            // CPython 3.12: dict binary-operator slot wrappers (Objects/dictobject.c
            // dict_or/dict_ior and dict_richcompare via slotdefs).
            // dict supports | and |= (merge) but NOT &, -, ^; ordering comparisons are
            // NotImplemented. Unbound calls return raw NotImplemented for non-mapping rhs.
            dictType.TypeDict["__or__"] = new PyWrapperDescriptor(
                "__or__", dictType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"expected 1 argument, got {args.Length}");
                    var left = RequireDictOperand(self, "__or__");
                    if (!IsDictLike(args[0])) return PyNotImplemented.Instance;
                    return left.BitwiseOr(args[0]);
                }
            );
            dictType.TypeDict["__ror__"] = new PyWrapperDescriptor(
                "__ror__", dictType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"expected 1 argument, got {args.Length}");
                    RequireDictOperand(self, "__ror__");
                    return PyNotImplemented.Instance;
                }
            );
            dictType.TypeDict["__ior__"] = new PyWrapperDescriptor(
                "__ior__", dictType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"expected 1 argument, got {args.Length}");
                    var target = RequireDictOperand(self, "__ior__");
                    // CPython dict_ior == PyDict_Update(self, other): any update argument,
                    // returns self identity.
                    UpdateCommon(target, args[0]);
                    return self;
                }
            );
            dictType.TypeDict["__eq__"] = new PyWrapperDescriptor(
                "__eq__", dictType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"expected 1 argument, got {args.Length}");
                    var left = RequireDictOperand(self, "__eq__");
                    if (!IsDictLike(args[0])) return PyNotImplemented.Instance;
                    // dict_richcompare: dict_equal on the two tables.
                    return PyBool.FromBool(left.CheckDictEquality((PyDict)args[0]));
                }
            );
            dictType.TypeDict["__ne__"] = new PyWrapperDescriptor(
                "__ne__", dictType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"expected 1 argument, got {args.Length}");
                    var left = RequireDictOperand(self, "__ne__");
                    if (!IsDictLike(args[0])) return PyNotImplemented.Instance;
                    return PyBool.FromBool(!left.CheckDictEquality((PyDict)args[0]));
                }
            );
            foreach (var ordering in new[] { "__lt__", "__le__", "__gt__", "__ge__" })
            {
                string slotName = ordering;
                dictType.TypeDict[slotName] = new PyWrapperDescriptor(
                    slotName, dictType,
                    (self, args, kwargs) => {
                        if (args.Length != 1)
                            throw PyTypeError.Create($"expected 1 argument, got {args.Length}");
                        RequireDictOperand(self, slotName);
                        return PyNotImplemented.Instance;
                    }
                );
            }

            // CPython 3.12: Objects/dictobject.c:3652-3655 - dict_copy
            // D.copy() -> a shallow copy of D
            dictType.TypeDict["copy"] = new PyMethodDescriptor(
                "copy", dictType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"copy() takes no arguments ({args.Length} given)");
                    // PyDict_Copy: an exact dict copies its table; a subclass goes through dict_merge.
                    if (self is PyDict { IsExact: true } dict) return dict.Copy();
                    return CopySubclass(self, GetDict(self, dictType));
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/dictobject.c:3657-3660 - dict_fromkeys
            // dict.fromkeys(S[,v]) -> New dict with keys from S and values equal to v.
            // v defaults to None.
            // IMPORTANT: fromkeys is a classmethod, not an instance method!
            // CPython 3.12: Objects/dictobject.c:4108 - METH_CLASS | METH_FASTCALL | METH_KEYWORDS
            dictType.TypeDict["fromkeys"] = new PyClassMethodDescriptor(
                "fromkeys", dictType,
                new PyBuiltinFunction("fromkeys", (args, kwargs) => {
                    // First arg is the class (from classmethod binding)
                    // Remaining args are: keys, [value]
                    if (args.Length < 2 || args.Length > 3)
                        throw PyTypeError.Create(args.Length < 2 ? "fromkeys expected at least 1 argument, got 0" : $"fromkeys expected at most 2 arguments, got {args.Length - 1}");

                    return PyDict.FromKeys(args[0], args[1], args.Length == 3 ? args[2] : PyNone.Instance);
                })
            );

            // CPython 3.12: __getitem__ slot (mp_subscript)
            // CPython 3.12: Objects/dictobject.c:2490-2523 (dict_subscript)
            // D[key] -> value
            //
            // IMPORTANT: Must access actual dict storage, NOT call polymorphic GetItem!
            // For dict subclasses (PyClassInstance), we must access _dictStorage directly
            // to avoid calling user-defined __getitem__ again (infinite loop).
            dictType.TypeDict["__getitem__"] = new PyMethodDescriptor(
                "__getitem__", dictType,
                (self, args, kwargs) => {
                    // CPython 3.12.15 measured: dict.__getitem__ is METH_O in the dict method table
                    if (kwargs != null && kwargs.InternalDict.Count > 0)
                        throw PyTypeError.Create("dict.__getitem__() takes no keyword arguments");
                    if (args.Length != 1)
                        throw PyTypeError.Create($"dict.__getitem__() takes exactly one argument ({args.Length} given)");

                    // dict_subscript: the table, then __missing__ when the type is not exactly dict.
                    if (self is PyDict pyDict)
                    {
                        if (pyDict.TryGetStoredValue(args[0], out var value))
                            return value;
                        // dict_subscript: only !PyDict_CheckExact looks __missing__ up.
                        if (!pyDict.IsExact && pyDict.LookupSpecialMethodUncached("__missing__") is { } missing)
                            return pyDict.CallSpecial(missing, args[0]);
                        throw PyKeyError.Create(args[0]);
                    }

                    // CPython 3.12.15 measured: dict methods are method_descriptors
                    // (descrobject.c call_method), not slot wrappers.
                    throw PyTypeError.Create($"descriptor '__getitem__' for 'dict' objects doesn't apply to a '{self.GetTypeName()}' object");
                },
                minArgs: 1, maxArgs: 1
            );

            // CPython 3.12: __setitem__ slot (mp_ass_subscript)
            // CPython 3.12: Objects/dictobject.c:2524-2530 (dict_ass_sub)
            // D[key] = value
            //
            // Same pattern: access actual dict storage, NOT polymorphic SetItem
            dictType.TypeDict["__setitem__"] = new PyMethodDescriptor(
                "__setitem__", dictType,
                (self, args, kwargs) => {
                    // CPython 3.12.15 measured: dict.__setitem__ is METH_VARARGS with format "OO";
                    // PyArg_ParseTuple reports without the function name (note leading space).
                    if (kwargs != null && kwargs.InternalDict.Count > 0)
                        throw PyTypeError.Create("dict.__setitem__() takes no keyword arguments");
                    if (args.Length != 2)
                        throw PyTypeError.Create($" expected 2 arguments, got {args.Length}");

                    // dict_ass_sub: the table itself.
                    if (self is PyDict pyDict)
                    {
                        pyDict.MpAssSubscript(args[0], args[1]);
                        return PyNone.Instance;
                    }

                    throw PyTypeError.Create($"descriptor '__setitem__' for 'dict' objects doesn't apply to a '{self.GetTypeName()}' object");
                },
                minArgs: 2, maxArgs: 2
            );

            // CPython 3.12: __delitem__ slot (mp_ass_subscript with NULL value)
            // CPython 3.12: Objects/dictobject.c:2524-2530 (dict_ass_sub with w == NULL)
            // del D[key]
            //
            // Same pattern: access actual dict storage, NOT polymorphic DelItem
            dictType.TypeDict["__delitem__"] = new PyMethodDescriptor(
                "__delitem__", dictType,
                (self, args, kwargs) => {
                    // CPython 3.12.15 measured: dict.__delitem__ reports "expected 1 argument, got N".
                    if (kwargs != null && kwargs.InternalDict.Count > 0)
                        throw PyTypeError.Create("dict.__delitem__() takes no keyword arguments");
                    if (args.Length != 1)
                        throw PyTypeError.Create($"expected 1 argument, got {args.Length}");

                    // dict_ass_sub without a value: the table itself.
                    if (self is PyDict pyDict)
                    {
                        pyDict.MpDelSubscript(args[0]);
                        return PyNone.Instance;
                    }

                    throw PyTypeError.Create($"descriptor '__delitem__' for 'dict' objects doesn't apply to a '{self.GetTypeName()}' object");
                },
                minArgs: 1, maxArgs: 1
            );

            // CPython 3.12: Objects/dictobject.c:2547
            // {"__class_getitem__", Py_GenericAlias, METH_O|METH_CLASS, PyDoc_STR("See PEP 585")},
            dictType.TypeDict["__class_getitem__"] = PyClassMethodDescriptor.GenericAliasGetItem(dictType);

            // dict_sizeof (Objects/dictobject.c). The pinned interpreter reports 48 empty and 208
            // with two items, so those are the values here. The table term past the first entry is
            // an estimate: SharpPy's storage is a CLR dictionary, not a C allocatable table, and
            // docs/known_gaps.md P0-45 records that the exact figure is not CPython's.
            dictType.TypeDict["__sizeof__"] = new PyMethodDescriptor(
                "__sizeof__", dictType,
                (self, args, kwargs) => {
                    PyType.RefuseExtra(dictType, "__sizeof__", args);
                    return PyInt.From(DictSizeInBytes(
                        ((PyDict)PyType.NeedStorage(self, "__sizeof__", "dict",
                            (self as PyDict))).Length()));
                },
                minArgs: 0, maxArgs: 0);
        }

        internal static long DictSizeInBytes(int count)
        {
            if (count == 0) return 48;
            long table = 8;
            while (count * 3 >= table * 2) table *= 2;
            return 48 + table * 20;
        }

        #region Core Properties

        // PyObject를 키로 사용하기 위한 사용자 정의 비교기
        protected readonly PyDictionaryStorage _dict;  // O(1) 조회용
        protected readonly List<PyObject> _keys;  // 삽입 순서 보관

        // 내부 딕셔너리 접근용 (타입 생성 등에서 사용)
        internal PyDictionaryStorage InternalDict => _dict;
        // Bypass Python __getitem__ overrides while allowing internal storage adapters.
        internal virtual bool TryGetStoredValue(PyObject key, out PyObject value) => _dict.TryGetValue(key, out value);
        internal List<PyObject>.Enumerator GetStorageKeyEnumerator() => _keys.GetEnumerator();
        internal OrderedIterationState GetIterationState() => ((OrderedKeyList)_keys).GetIterationState();
        private void AddOrderedKey(PyObject key) => ((OrderedKeyList)_keys).AddKey(key);
        private void RemoveOrderedKeyAt(int index, bool trimTail = false) => ((OrderedKeyList)_keys).RemoveKeyAt(index, trimTail);
        private void ClearOrderedKeys() => ((OrderedKeyList)_keys).ClearKeys();

        /// <summary>
        /// CPython 3.12: Include/cpython/dictobject.h - 내부 딕셔너리 순회용
        /// DICT_UPDATE/DICT_MERGE 명령어에서 사용
        /// </summary>
        public IEnumerable<KeyValuePair<PyObject, PyObject>> GetInternalDict()
        {
            // Python 3.7+: 삽입 순서 보장하여 반환
            foreach (var key in _keys)
            {
                yield return new KeyValuePair<PyObject, PyObject>(key, _dict.StoredValue(key));
            }
        }

        public PyDict()
        {
            _dict = new PyDictionaryStorage();
            _keys = new OrderedKeyList();
        }

        public PyDict(Dictionary<string, PyObject> items)
        {
            _dict = new PyDictionaryStorage();
            _keys = new OrderedKeyList();
            foreach (var kv in items)
            {
                var key = PyString.From(kv.Key);
                _dict[key] = kv.Value;
                AddOrderedKey(key);
            }
        }

        public PyDict(Dictionary<PyObject, PyObject> items)
        {
            _dict = new PyDictionaryStorage(items);
            _keys = new OrderedKeyList(items.Keys);
        }

        public override PyType GetPyType() => _customType ?? PyType.DictType;

        // PyX_CheckExact
        internal bool IsExact => IsExactly(PyType.DictType);
        public override string GetTypeName() => _customType?.Name ?? "dict";

        // CPython 3.12: Objects/dictobject.c - dict_bool
        // Optimized: Direct count check, no MRO traversal
        protected internal override bool Bool() => _dict.Count > 0;

        #endregion

        #region String Representation

        protected internal override PyString Str() => ToRepr();

        protected internal override PyString Repr()
        {
            if (_dict.Count == 0) return PyString.From("{}");

            // Objects/dictobject.c:2400 dict_repr calls Py_ReprEnter, so a self-containing dict
            // prints {1: {...}} instead of recursing until the process dies.
            if (!PyReprGuard.Enter(this))
                return PyString.From("{...}");
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
            // Python 3.7+: 삽입 순서대로 출력
            // Performance: string.Create() - CPython-style single allocation

            // Step 1: Calculate total length and cache reprs
            var keyReprs = new string[_keys.Count];
            var valueReprs = new string[_keys.Count];
            int totalLength = 2; // "{}"
            for (int i = 0; i < _keys.Count; i++)
            {
                keyReprs[i] = _keys[i].ToRepr().Value;
                valueReprs[i] = _dict.StoredValue(_keys[i]).ToRepr().Value;

                if (i > 0) totalLength += 2; // ", "
                totalLength += keyReprs[i].Length;
                totalLength += 2; // ": "
                totalLength += valueReprs[i].Length;
            }

            // Step 2: string.Create with single allocation
            var result = string.Create(totalLength, (keyReprs, valueReprs), (span, state) =>
            {
                int pos = 0;
                span[pos++] = '{';

                for (int i = 0; i < state.keyReprs.Length; i++)
                {
                    if (i > 0)
                    {
                        span[pos++] = ',';
                        span[pos++] = ' ';
                    }

                    state.keyReprs[i].AsSpan().CopyTo(span.Slice(pos));
                    pos += state.keyReprs[i].Length;

                    span[pos++] = ':';
                    span[pos++] = ' ';

                    state.valueReprs[i].AsSpan().CopyTo(span.Slice(pos));
                    pos += state.valueReprs[i].Length;
                }

                span[pos] = '}';
            });

            return PyString.From(result);
        }

        #endregion

        #region Hash and Equality

        // dict_equal tests the other operand with PyDict_Check, which accepts subclasses, so
        // {'a': 1} == D({'a': 1}) compares contents.
        protected override PyObject PyEquals(PyObject other)
        {
            return other switch
            {
                PyDict otherDict => PyBool.FromBool(CheckDictEquality(otherDict)),
                // dict_richcompare: both must be dicts, or the other side is asked.
                _ => PyNotImplemented.Instance
            };
        }

        // Performance: Eliminated LINQ (.All) - manual iteration
        internal bool CheckDictEquality(PyDict otherDict)
        {
            if (_dict.Count != otherDict._dict.Count)
                return false;

            int position = 0;
            while (_dict.NextEntry(ref position, out var entry))
            {
                if (!otherDict._dict.TryGetKnownHash(entry.Key, entry.Hash, out var value))
                    return false;
                if (!CompareBool(entry.Value, value, CompareOp.EQ))
                    return false;
            }
            return true;
        }

        #endregion

        #region Dictionary Operations

        /// <summary>
        /// 키로 값 접근 dict[key]
        /// </summary>
        protected internal override PyObject MpSubscript(PyObject key)
        {
            if (_dict.TryGetValue(key, out PyObject value))
                return value;

            throw PyKeyError.Create(key);
        }

        /// <summary>
        /// 키에 값 설정 dict[key] = value
        /// Python 3.7+: 삽입 순서 보장
        /// </summary>
        protected internal override void MpAssSubscript(PyObject key, PyObject value)
        {
            if (_dict.Set(key, value)) AddOrderedKey(key);
        }

        /// <summary>
        /// 키 삭제 del dict[key]
        /// Python 3.7+: 삽입 순서 유지 (_keys에서도 제거)
        /// </summary>
        // dict_ass_sub without a value: PyDict_DelItem.
        protected internal override void MpDelSubscript(PyObject key) => DelItem(key);

        public virtual PyObject DelItem(PyObject key)
        {
            if (!_dict.Remove(key, out _, out var storedKey))
                throw PyKeyError.Create(key);

            RemoveOrderedKey(storedKey);
            return PyNone.Instance;
        }

        protected void RemoveOrderedKey(PyObject key)
        {
            // Use the same Python equality as lookup storage; List.Remove uses CLR equality.
            for (int i = 0; i < _keys.Count; i++)
            {
                if (ReferenceEquals(_keys[i], key))
                {
                    RemoveOrderedKeyAt(i);
                    break;
                }
            }
        }

        /// <summary>
        /// 키 포함 여부 확인 (in 연산자)
        /// </summary>
        protected internal override PyBool ContainsItem(PyObject key)
        {
            return PyBool.FromBool(_dict.ContainsKey(key));
        }

        /// <summary>
        /// CPython 3.12: PyDict_Contains - 키 포함 여부 확인 (bool 반환)
        /// DICT_MERGE 명령어에서 중복 키 검사에 사용
        /// </summary>
        public virtual bool ContainsKey(PyObject key)
        {
            return _dict.ContainsKey(key);
        }

        /// <summary>
        /// 기본값과 함께 값 가져오기
        /// </summary>
        public virtual PyObject Get(PyObject key, PyObject defaultValue = null)
        {
            return _dict.TryGetValue(key, out PyObject value)
                ? value
                : defaultValue ?? PyNone.Instance;
        }

        /// <summary>
        /// 키가 있으면 값 반환하고 삭제, 없으면 기본값 반환
        /// Python 3.7+: 삽입 순서 유지 (_keys에서도 제거)
        /// </summary>
        public virtual PyObject Pop(PyObject key, PyObject defaultValue = null)
        {
            if (_dict.Count == 0)
            {
                if (defaultValue != null) return defaultValue;
                throw PyKeyError.Create(key);
            }
            if (_dict.Remove(key, out PyObject value, out var storedKey))
            {
                RemoveOrderedKey(storedKey);
                return value;
            }

            if (defaultValue != null)
                return defaultValue;

            throw PyKeyError.Create(key);
        }

        /// <summary>
        /// 임의의 키-값 쌍을 제거하고 반환
        /// Python 3.7+: LIFO 순서 (마지막 삽입된 항목 반환)
        /// </summary>
        public virtual PyTuple PopItem()
        {
            if (_dict.Count == 0)
                throw PyKeyError.Create("popitem(): dictionary is empty");

            // Python 3.7+: LIFO (Last-In-First-Out) - 마지막 삽입된 키
            var lastKey = _keys[_keys.Count - 1];
            var value = _dict.StoredValue(lastKey);
            _dict.Remove(lastKey, out _, out _, knownHash: true);
            RemoveOrderedKeyAt(_keys.Count - 1, trimTail: true);
            return PyTuple.From(lastKey, value);
        }

        /// <summary>
        /// 모든 키-값 쌍 제거
        /// Python 3.7+: 삽입 순서 리스트도 함께 제거
        /// </summary>
        public virtual PyNone Clear()
        {
            _dict.Clear();
            ClearOrderedKeys();
            return PyNone.Instance;
        }

        /// <summary>
        /// 다른 딕셔너리나 매핑의 키-값으로 업데이트 (CPython dict.update 호환)
        /// Python 3.7+: 삽입 순서 유지
        /// </summary>
        /// <summary>
        /// Direct storage update used by CPython merge semantics (PyDict_SetItem):
        /// bypasses user-defined __setitem__ on dict subclasses.
        /// </summary>
        // Merge-style write. Must stay virtual so PyGlobalsDict keeps its
        // module-namespace synchronization (enum.global_enum injects re.I via
        // sys.modules[...].__dict__.update(...)).
        public virtual void SetItemStorage(PyObject key, PyObject value) => MpAssSubscript(key, value);

        internal static bool IsDictLike(PyObject value) => value is PyDict;

        /// <summary>
        /// dict_merge (Objects/dictobject.c): PyDict_Check(b) and Py_TYPE(b)->tp_iter == dict_iter,
        /// when the storage may be read directly instead of through keys().
        /// </summary>
        internal static bool IterIsDictIter(PyObject value) =>
            IsDictLike(value) && (value.GetPyType() is not SharpPy.PyClass heap || heap.IterSlotIs(PyType.DictType));

        internal static PyDict RequireDictOperand(PyObject self, string name)
        {
            if (self is PyDict dict)
                return dict;
            throw PyTypeError.Create($"descriptor '{name}' requires a 'dict' object but received a '{self.GetTypeName()}'");
        }

        /// <summary>
        /// CPython 3.12: Objects/dictobject.c dict_update_common dispatch:
        ///   - dict (incl. subclass) instances merge from internal storage
        ///   - other objects with a keys() attribute merge via keys() + __getitem__
        ///   - remaining iterables are consumed as key/value pairs
        /// </summary>
        internal static void UpdateCommon(PyDict target, PyObject source)
        {
            if (ReferenceEquals(target, source))
                return;

            // dict_merge reads the table unless the subclass has its own __iter__.
            if (IterIsDictIter(source))
            {
                target.Update(source);
                return;
            }

            PyObject keysCallable = null;
            try
            {
                keysCallable = source.GetAttribute("keys");
            }
            catch (PythonException ex) when (ex is PyAttributeError || ex.PyException is PyAttributeError)
            {
                keysCallable = null;
            }

            if (keysCallable != null)
            {
                // Generic mapping: for k in E.keys(): self[k] = E[k] (PyObject_GetItem values)
                var keysObj = keysCallable.Call(new PyObject[0], null);
                foreach (var key in PySet.IterableItems(keysObj))
                    target.SetItemStorage(key, source.GetItem(key));
                return;
            }

            int elementIndex = 0;
            foreach (var element in PySet.IterableItems(source))
            {
                PyObject[] pair;
                if (element is PyTuple tuple)
                    pair = tuple.Items;
                else
                {
                    try
                    {
                        pair = element.AsList().Items;
                    }
                    catch (PythonException)
                    {
                        throw PyTypeError.Create(
                            $"cannot convert dictionary update sequence element #{elementIndex} to a sequence");
                    }
                }
                if (pair.Length != 2)
                    throw PyValueError.Create(
                        $"dictionary update sequence element #{elementIndex} has length {pair.Length}; 2 is required");
                target.SetItemStorage(pair[0], pair[1]);
                elementIndex++;
            }
        }

        public virtual PyNone Update(PyObject other)
        {
            // CPython 3.12: dict.update() can accept dict, mappingproxy, or any mapping-like object
            if (other is PyDict otherDict)
            {
                if (ReferenceEquals(this, otherDict)) return PyNone.Instance;
                if (_dict.Count == 0 && otherDict._dict.CanCloneForMerge)
                {
                    _dict.CopyFrom(otherDict._dict);
                    ClearOrderedKeys();
                    foreach (var key in otherDict._keys) AddOrderedKey(key);
                    return PyNone.Instance;
                }
                // CPython 3.12: Objects/dictobject.c:2811-2990 (dict_merge)
                // Line 2967: PyDict_SetItem (does NOT call __setitem__, updates storage directly)
                // For dict subclasses, PyDict_SetItem updates internal storage directly,
                // bypassing user-defined __setitem__
                _dict.PrepareMerge(otherDict._dict.Count);
                int position = 0, originalSize = otherDict._dict.EntriesUsed;
                while (otherDict._dict.NextEntry(ref position, out var entry))
                {
                    if (_dict.SetKnownHash(entry.Key, entry.Value, entry.Hash)) AddOrderedKey(entry.Key);
                    if (originalSize != otherDict._dict.EntriesUsed)
                        throw PyRuntimeError.Create("dict mutated during update");
                }
            }
            else if (other is PyMappingProxy { TableEntries: not null } mappingProxy)
            {
                // CPython 3.12: Objects/dictobject.c:2811-2990 (dict_merge)
                // Line 2961: value = PyObject_GetItem(b, key);
                // Line 2967: PyDict_SetItem (does NOT call __setitem__, updates storage directly)
                foreach (var tableEntry in mappingProxy.TableEntries)
                {
                    var pyKey = tableEntry.Key;
                    var key = pyKey is PyString keyText ? keyText.Value : null;
                    var value = tableEntry.Value;

                    #if DEBUG
                    // Debug: Track _generate_next_value_ updates
                    if (key == "_generate_next_value_")
                    {
                        Console.WriteLine($"[PyDict.Update DEBUG] Copying '{key}' from mappingproxy");
                        Console.WriteLine($"[PyDict.Update DEBUG]   value = {value}, type = {value.GetTypeName()}");
                    }
                    #endif

                    // Direct storage update (bypass __setitem__)
                    if (_dict.Set(pyKey, value)) AddOrderedKey(pyKey);
                }
            }
            else
            {
                throw PyTypeError.Create($"update() argument must be dict or mapping, not '{other.GetTypeName()}'");
            }
            return PyNone.Instance;
        }

        /// <summary>
        /// 키가 없으면 기본값 설정하고 반환
        /// Python 3.7+: 삽입 순서 유지
        /// </summary>
        public virtual PyObject SetDefault(PyObject key, PyObject defaultValue = null)
        {
            var value = _dict.SetDefault(key, defaultValue ?? PyNone.Instance, out bool added);
            if (added) AddOrderedKey(key);
            return value;
        }

        #endregion

        #region Internal Snapshots (Python methods return PyDictView)

        /// <summary>
        /// 모든 키의 뷰 반환
        /// Python 3.7+: 삽입 순서 보장
        /// </summary>
        public PyList Keys()
        {
            // _keys를 사용하여 삽입 순서 보장
            // Performance: Eliminated LINQ (.ToArray) - direct array copy + Cache
            var keysArray = new PyObject[_keys.Count];
            for (int i = 0; i < _keys.Count; i++)
            {
                keysArray[i] = _keys[i];
            }
            return new PyList(keysArray);
        }

        /// <summary>
        /// 모든 값의 뷰 반환
        /// Python 3.7+: 삽입 순서 보장
        /// </summary>
        public PyList Values()
        {
            // _keys 순서대로 값을 가져옴
            // Performance: Eliminated LINQ (Select + ToArray) - direct array copy + Cache
            var values = new PyObject[_keys.Count];
            for (int i = 0; i < _keys.Count; i++)
            {
                values[i] = _dict.StoredValue(_keys[i]);
            }
            return new PyList(values);
        }

        /// <summary>
        /// 모든 키-값 쌍의 뷰 반환
        /// Python 3.7+: 삽입 순서 보장
        /// </summary>
        public PyList Items()
        {
            // _keys 순서대로 키-값 쌍을 생성
            // Performance: Eliminated LINQ (Select + Cast + ToArray) - direct tuple creation + Cache
            var items = new PyObject[_keys.Count];
            for (int i = 0; i < _keys.Count; i++)
            {
                items[i] = PyTuple.From(_keys[i], _dict.StoredValue(_keys[i]));
            }
            return new PyList(items);
        }

        #endregion

        #region Length and Type Checking

        protected internal override long Len() => _dict.Count;

        #endregion

        #region Dictionary Creation Methods

        /// <summary>
        /// 키 시퀀스와 값으로부터 딕셔너리 생성
        /// </summary>
        // _PyDict_FromKeys: cls() is what gets filled, an exact dict or set gives its hashes.
        internal static PyObject FromKeys(PyObject cls, PyObject iterable, PyObject value)
        {
            var target = cls.Call(Array.Empty<PyObject>(), null);
            var exact = PySet.IsExactDict(target) ? (PyDict)target : null;
            if (exact != null && exact.Length() == 0)
            {
                if (PySet.IsExactDict(iterable))
                {
                    int position = 0;
                    while (((PyDict)iterable).InternalDict.NextEntry(ref position, out var entry))
                        exact.SetKnownHash(entry.Key, value, entry.Hash);
                    return exact;
                }
                IEnumerable<PySetStorage.Entry> entries = null;
                if (iterable is PySet set && ReferenceEquals(set.GetPyType(), PyType.SetType)) entries = set.Entries();
                if (iterable is PyFrozenSet frozen && ReferenceEquals(frozen.GetPyType(), PyType.FrozenSetType))
                    entries = ((PySetStorage)frozen.Items).Entries();
                if (entries != null)
                {
                    foreach (var entry in entries)
                        exact.SetKnownHash(entry.Key, value, entry.Hash);
                    return exact;
                }
            }
            var iterator = iterable.GetIterator();
            while (true)
            {
                PyObject key;
                try { key = iterator.Next(); }
                catch (PythonException error) when (error.PyException is PyStopIteration) { return target; }
                if (exact != null) exact.SetItem(key, value);
                else PyAbstract.SetItem(target, key, value);
            }
        }

        internal void SetKnownHash(PyObject key, PyObject value, long hash)
        {
            if (_dict.SetKnownHash(key, value, hash)) AddOrderedKey(key);
        }

        public static PyDict FromKeys(PyObject keys, PyObject value = null)
        {
            var dict = new PyDict();
            var defaultValue = value ?? PyNone.Instance;
            
            if (keys is PyList { IsExact: true } list)
            {
                foreach (var key in list.Items)
                {
                    dict.SetItem(key, defaultValue);
                }
            }
            else if (keys is PyTuple { IsExact: true } tuple)
            {
                foreach (var key in tuple.Items)
                {
                    dict.SetItem(key, defaultValue);
                }
            }
            else if (keys is PyString str)
            {
                foreach (char c in str.Value)
                {
                    dict.SetItem(PyString.From(c.ToString()), defaultValue);
                }
            }
            else
            {
                foreach (var key in PySet.IterableItems(keys)) dict.SetItem(key, defaultValue);
            }
            
            return dict;
        }

        #endregion

        #region Copy

        private static PyDict CopySubclass(PyObject source, PyDict storage)
        {
            // PyDict_Copy skips callbacks for empty storage. Otherwise an
            // overridden iteration slot selects dict_merge's mapping path.
            // ma_used, not a subclass's __len__.
            if (storage._dict.Count == 0 || IterIsDictIter(source))
                return storage.Copy();

            var output = source.GetAttribute("keys").Call(Array.Empty<PyObject>(), null);
            PyList keys;
            if (output is PyList list && output.GetPyType() == PyType.ListType)
                keys = list;
            else
            {
                PyObject iterator;
                try { iterator = output.GetIterator(); }
                catch (PythonException ex) when (ex.PyException is PyTypeError)
                {
                    throw PyTypeError.Create($"{source.GetTypeName()}.keys() returned a non-iterable (type {output.GetTypeName()})");
                }
                // PyMapping_Keys materializes before any item lookup. Avoid
                // AsList, which currently replaces iterator errors with TypeError.
                keys = new PyList();
                keys.InplaceAdd(iterator);
            }

            var copy = new PyDict();
            for (int i = 0; i < keys.Length(); i++)
            {
                var key = keys.Items[i];
                copy.SetItem(key, source.GetItem(key));
            }
            return copy;
        }

        /// <summary>
        /// 딕셔너리의 얕은 복사본 생성
        /// Python 3.7+: 삽입 순서 보장
        /// </summary>
        public virtual PyDict Copy()
        {
            if (!_dict.CanCloneForCopy)
            {
                var compact = new PyDict();
                compact.Update(this);
                return compact;
            }
            return new PyDict(_dict, _keys);
        }

        private PyDict(PyDictionaryStorage items, List<PyObject> keys)
        {
            // The identical comparer lets Dictionary reuse stored hashes. Keep the
            // Python insertion order separately: CLR deletion reuses entry slots.
            _dict = items.Clone();
            _keys = new OrderedKeyList(keys);
        }

        #endregion

        #region Static Factory Methods

        public static PyDict Empty => new PyDict();

        #endregion

        #region Evaluate Method (NotImplementedException)

        public PyObject Evaluate(PyScope scope)
        {
            // Dictionary literals evaluate to themselves (CPython style)
            return this;
        }

        #endregion

        #region CPython Compatible Methods (Attribute Access)


        #endregion

        #region Type Conversion (CPython Compatible)

        // === To* Methods: Value Extraction (PyDict → C# basic types) ===
        
        /// <summary>
        /// CPython PyLong_AsLong 호환: PyDict는 일반적으로 int로 변환될 수 없음
        /// </summary>
        public override int ToInt()
        {
            throw PyTypeError.Create($"int() argument must be a string, a bytes-like object or a real number, not 'dict'");
        }
        
        /// <summary>
        /// CPython PyFloat_AsDouble 호환: PyDict는 일반적으로 float로 변환될 수 없음
        /// </summary>
        public override double ToFloat()
        {
            throw PyTypeError.Create($"float() argument must be a string or a real number, not 'dict'");
        }
        
        
        // === As* Methods: Type Conversion (PyDict → PyObject types) ===
        
        /// <summary>
        /// CPython 호환: PyDict를 PyDict로 변환 (복사본 생성)
        /// </summary>
        public override PyDict AsDict()
        {
            // CPython dict() 생성자 동작: 새로운 복사본 생성
            var created = new PyDict();
            created.Update(this);
            return created;
        }
        
        /// <summary>
        /// CPython 호환: PyDict를 PyBool로 변환
        /// </summary>
        public override PyBool AsBool()
        {
            return PyBool.FromBool(_dict.Count > 0);
        }
        
        /// <summary>
        /// CPython 호환: PyDict를 PyString으로 변환 (str() 호출과 동일)
        /// </summary>
        public override string ToClrString()
        {
            return ToRepr().Value; // CPython에서 str(dict)는 repr(dict)와 동일
        }

        /// <summary>
        /// Dict merge operator (|) - Python 3.9+
        /// CPython 3.12: Objects/dictobject.c:dict_or (lines 3144-3169)
        /// Creates new dict merging self and other (rightmost values win for duplicate keys)
        /// </summary>
        public override PyObject BitwiseOr(PyObject other)
        {
            // dict_or (Objects/dictobject.c): both PyDict_Check; new = PyDict_Copy(self), then
            // dict_update_arg(new, other). Both copies are dict_merge, which reads a subclass
            // with its own __iter__ through keys().
            if (other is not PyDict)
                return PyNotImplemented.Instance;
            var result = HasCustomType ? CopySubclass(this, this) : Copy();
            UpdateCommon(result, other);
            return result;
        }

        /// <summary>
        /// Dict in-place merge operator (|=) - Python 3.9+
        /// CPython 3.12: Objects/dictobject.c:dict_ior (lines 3172-3194)
        /// Merges other into self in-place and returns self
        /// </summary>
        public override PyObject? InplaceBitwiseOr(PyObject other)
        {
            // CPython dict_ior: PyDict_Update(self, other) - accepts any update argument
            // (mapping or key/value pairs) and propagates update errors, e.g.
            //   {} |= 5 → TypeError: 'int' object is not iterable
            UpdateCommon(this, other);
            return this;
        }

        /// <summary>
        /// Iterator support for dictionary iteration (for key in dict) - CPython compatible
        /// Returns keys only, same as CPython behavior
        /// </summary>
        protected internal override PyObject Iter()
        {
            return new PyDictKeyIterator(this);
        }

        #endregion
    }

    /// <summary>The globals of exec and eval: a frame reads and writes the dict it was given.</summary>
    internal sealed class PyDictVariables : IDictionary<string, PyObject>
    {
        private readonly PyDict _dict;
        internal PyDictVariables(PyDict dict) => _dict = dict;

        internal PyDict Dict => _dict;

        private IEnumerable<KeyValuePair<string, PyObject>> Entries()
        {
            foreach (var entry in new List<KeyValuePair<PyObject, PyObject>>(_dict.GetInternalDict()))
                if (entry.Key is PyString name)
                    yield return new KeyValuePair<string, PyObject>(name.Value, entry.Value);
        }

        public PyObject this[string key]
        {
            get => TryGetValue(key, out var value) ? value : throw new KeyNotFoundException(key);
            set => _dict.SetItem(PyString.From(key), value);
        }
        public bool TryGetValue(string key, out PyObject value) => _dict.TryGetStoredValue(PyString.From(key), out value);
        public bool ContainsKey(string key) => TryGetValue(key, out _);
        public void Add(string key, PyObject value) => this[key] = value;
        public bool Remove(string key)
        {
            if (!ContainsKey(key)) return false;
            _dict.DelItem(PyString.From(key));
            return true;
        }
        public void Clear() => _dict.Clear();
        public int Count => _dict.Length();
        public bool IsReadOnly => false;
        public ICollection<string> Keys => System.Linq.Enumerable.ToList(System.Linq.Enumerable.Select(Entries(), entry => entry.Key));
        public ICollection<PyObject> Values => System.Linq.Enumerable.ToList(System.Linq.Enumerable.Select(Entries(), entry => entry.Value));
        public void Add(KeyValuePair<string, PyObject> item) => this[item.Key] = item.Value;
        public bool Contains(KeyValuePair<string, PyObject> item) => TryGetValue(item.Key, out var value) && ReferenceEquals(value, item.Value);
        public bool Remove(KeyValuePair<string, PyObject> item) => Contains(item) && Remove(item.Key);
        public void CopyTo(KeyValuePair<string, PyObject>[] array, int arrayIndex)
        {
            foreach (var entry in Entries()) array[arrayIndex++] = entry;
        }
        public IEnumerator<KeyValuePair<string, PyObject>> GetEnumerator() => Entries().GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// Special PyDict subclass that synchronizes with global scope variables
    /// CPython 3.12: globals() returns the actual module __dict__, not a copy
    /// When globals().update() is called, it modifies the module namespace
    /// </summary>
    public class PyGlobalsDict : PyDict
    {
        internal IDictionary<string, PyObject> Variables => _globalScopeVariables;

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IDictionary<string, PyObject>, PyGlobalsDict> Views = new();
        /// <summary>The dict Python sees for a scope: the one exec was given, if it was.</summary>
        internal static PyDict DictOf(IDictionary<string, PyObject> variables) =>
            variables is PyDictVariables given ? given.Dict : For(variables);

        internal static PyGlobalsDict For(IDictionary<string, PyObject> variables) => variables is PyNamespace owned ? owned.Mapping : Views.GetValue(variables, source => new PyGlobalsDict(source, false));
        /// <summary>The live __dict__ of an object that keeps its attributes in a plain dictionary.</summary>
        internal static PyGlobalsDict OfInstance(Dictionary<string, PyObject> attributes)
        {
            var view = For(attributes);
            view.SynchronizeFromHost();
            return view;
        }

        internal static void Store(IDictionary<string, PyObject> variables, string name, PyObject value)
        {
            if (variables is PyNamespace) { variables[name] = value; return; }
            if (Views.TryGetValue(variables, out var view)) view.SetItem(PyString.From(name), value);
            else variables[name] = value;
        }
        /// <summary>Empties a store and the __dict__ view made over it, as Store and Delete keep both.</summary>
        internal static void Clear(IDictionary<string, PyObject> variables)
        {
            if (variables is not PyNamespace && Views.TryGetValue(variables, out var view)) view.Clear();
            else variables.Clear();
        }

        internal static bool Delete(IDictionary<string, PyObject> variables, string name)
        {
            if (variables is PyNamespace) return variables.Remove(name);
            if (!variables.ContainsKey(name)) return false;
            if (Views.TryGetValue(variables, out var view)) view.DelItem(PyString.From(name));
            else variables.Remove(name);
            return true;
        }
        private readonly IDictionary<string, PyObject> _globalScopeVariables;
        private readonly bool _hostMutable;
        private int _nonStringCount;

        internal PyGlobalsDict(PyNamespace variables)
        {
            _globalScopeVariables = variables;
        }

        public PyGlobalsDict(Dictionary<string, PyObject> globalScopeVariables)
            : this(globalScopeVariables, true) { }

        private PyGlobalsDict(IDictionary<string, PyObject> globalScopeVariables, bool hostMutable)
            : base()
        {
            _globalScopeVariables = globalScopeVariables;
            _hostMutable = hostMutable;

            // Initialize with current global variables
            foreach (var kv in globalScopeVariables)
            {
                base.MpAssSubscript(PyString.From(kv.Key), kv.Value);
            }
        }

        protected internal override PyObject MpSubscript(PyObject key)
        {
            if (!CanUseHostLookup(key)) return base.MpSubscript(key);
            var name = (PyString)key;
            if (TryGetHostValue(name.Value, out var value)) return value;
            throw PyKeyError.Create(key);
        }

        public override PyObject Get(PyObject key, PyObject defaultValue = null)
        {
            if (!CanUseHostLookup(key)) return base.Get(key, defaultValue);
            var name = (PyString)key;
            return TryGetHostValue(name.Value, out var value)
                ? value : defaultValue ?? PyNone.Instance;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private bool CanUseHostLookup(PyObject key) => key is PyString &&
            (_globalScopeVariables is not PyNamespace owned ||
             (!owned.GenericKeys && key.IsExactly(PyType.StrType)));

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private bool TryGetHostValue(string name, out PyObject value) => _globalScopeVariables is PyNamespace owned
            ? owned.TryGetExactString(name, out value) : _globalScopeVariables.TryGetValue(name, out value);

        internal override bool TryGetStoredValue(PyObject key, out PyObject value) => CanUseHostLookup(key)
            ? TryGetHostValue(((PyString)key).Value, out value) : base.TryGetStoredValue(key, out value);

        public override bool ContainsKey(PyObject key) => CanUseHostLookup(key)
            ? TryGetHostValue(((PyString)key).Value, out _) : base.ContainsKey(key);

        protected internal override PyBool ContainsItem(PyObject key) => PyBool.FromBool(ContainsKey(key));

        protected internal override void MpAssSubscript(PyObject key, PyObject value)
        {
            if (_globalScopeVariables is PyNamespace canonical)
            {
                canonical.PrepareKey(key);
                base.MpAssSubscript(key, value);
                canonical.PrepareKey(key);
                if (!canonical.GenericKeys) canonical.StoreFromMapping(((PyString)key).Value, value);
                return;
            }
            // CPython 3.12: Setting globals()[key] = value modifies the module namespace
            int before = (int)base.Len();
            base.MpAssSubscript(key, value);
            if (key is not PyString) _nonStringCount += (int)base.Len() - before;

            // Sync back to global scope
            if (key is PyString strKey)
            {
                if (_globalScopeVariables is PyNamespace owned) owned.StoreFromMapping(strKey.Value, value);
                else _globalScopeVariables[strKey.Value] = value;
            }
        }

        public override PyObject DelItem(PyObject key)
        {
            if (_globalScopeVariables is PyNamespace canonical)
            {
                canonical.PrepareKey(key);
                var result = base.DelItem(key);
                canonical.PrepareKey(key);
                if (!canonical.GenericKeys) canonical.RemoveFromMapping(((PyString)key).Value);
                return result;
            }
            if (key is not PyString name)
            {
                var result = base.DelItem(key);
                _nonStringCount--;
                return result;
            }
            if (!RemoveHost(name.Value)) throw PyKeyError.Create(key);
            if (base.ContainsKey(key)) base.DelItem(key);
            return PyNone.Instance;
        }

        public override PyObject Pop(PyObject key, PyObject defaultValue = null)
        {
            if (_globalScopeVariables is PyNamespace canonical)
            {
                canonical.PrepareKey(key);
                var result = base.Pop(key, defaultValue);
                canonical.PrepareKey(key);
                if (!canonical.GenericKeys) canonical.RemoveFromMapping(((PyString)key).Value);
                return result;
            }
            if (key is not PyString name)
            {
                int before = (int)base.Len();
                var result = base.Pop(key, defaultValue);
                _nonStringCount += (int)base.Len() - before;
                return result;
            }
            bool found = _globalScopeVariables.TryGetValue(name.Value, out var value);
            if (found) RemoveHost(name.Value);
            if (base.ContainsKey(key)) base.DelItem(key);
            if (found) return value;
            if (defaultValue != null) return defaultValue;
            throw PyKeyError.Create(key);
        }

        public override PyNone Clear()
        {
            if (_globalScopeVariables is PyNamespace canonical)
            {
                var result = base.Clear();
                canonical.RebuildLookup();
                return result;
            }
            if (_globalScopeVariables is PyNamespace owned) owned.ClearFromMapping();
            else _globalScopeVariables.Clear();
            _nonStringCount = 0;
            return base.Clear();
        }

        public override PyObject SetDefault(PyObject key, PyObject defaultValue = null)
        {
            if (_globalScopeVariables is PyNamespace canonical)
            {
                canonical.PrepareKey(key);
                var result = base.SetDefault(key, defaultValue);
                canonical.PrepareKey(key);
                if (!canonical.GenericKeys) canonical.StoreFromMapping(((PyString)key).Value, result);
                return result;
            }
            if (key is not PyString name)
            {
                int before = (int)base.Len();
                var result = base.SetDefault(key, defaultValue);
                _nonStringCount += (int)base.Len() - before;
                return result;
            }
            if (_globalScopeVariables.TryGetValue(name.Value, out var value))
            {
                base.MpAssSubscript(key, value);
                return value;
            }
            value = defaultValue ?? PyNone.Instance;
            SetItem(key, value);
            return value;
        }

        public override PyNone Update(PyObject other)
        {
            if (_globalScopeVariables is PyNamespace canonical)
            {
                if (ReferenceEquals(this, other)) return PyNone.Instance;
                // Two exact-string tables cannot invoke Python equality/hash
                // callbacks. Merge stored hashes, then update only incoming cache
                // entries rather than rescanning the entire global namespace.
                if (!canonical.GenericKeys && other is PyDict sourceDict)
                {
                    bool exactStrings = true;
                    foreach (var pair in sourceDict.InternalDict)
                        if (!pair.Key.IsExactly(PyType.StrType)) { exactStrings = false; break; }
                    if (exactStrings)
                    {
                        var result = base.Update(other);
                        foreach (var pair in sourceDict.InternalDict)
                            canonical.StoreFromMapping(((PyString)pair.Key).Value, pair.Value);
                        return result;
                    }
                }
                canonical.BeginBulkMutation();
                try { return base.Update(other); }
                finally { canonical.EndBulkMutation(); }
            }
            // Self-update must not replay a stale snapshot over the actual namespace.
            if (ReferenceEquals(other, this) || (other is PyGlobalsDict globals &&
                ReferenceEquals(globals._globalScopeVariables, _globalScopeVariables))) return PyNone.Instance;
            if (other is PyDict source)
            {
                var keys = source.GetStorageKeyEnumerator();
                while (keys.MoveNext())
                {
                    var key = keys.Current;
                    SetItem(key, source.InternalDict[key]);
                }
                return PyNone.Instance;
            }
            if (other is PyMappingProxy { TableEntries: not null } proxy)
            {
                foreach (var entry in proxy.TableEntries)
                    SetItem(entry.Key, entry.Value);
                return PyNone.Instance;
            }
            return base.Update(other); // Preserve existing unsupported-input errors.
        }

        public override PyObject InplaceBitwiseOr(PyObject other)
        {
            if (other is not PyDict) return PyNotImplemented.Instance;
            Update(other);
            return this;
        }

        protected internal override long Len() => _globalScopeVariables is PyNamespace ? base.Len() : _globalScopeVariables.Count + _nonStringCount;
        protected internal override bool Bool() => Len() != 0;
        public override PyBool AsBool() => PyBool.FromBool(IsTrue());

        public override PyDict Copy()
        {
            if (_hostMutable) SynchronizeFromHost();
            return base.Copy();
        }

        /// <summary>
        /// Reconcile an externally mutated Dictionary before bulk observations.
        /// Runtime-owned mappings use Store/Delete and need no scan. Existing
        /// keys keep Python order; unobserved remove/reinsert history cannot be
        /// reconstructed from a Dictionary snapshot. Prefer SetItem/DelItem.
        /// </summary>
        public void SynchronizeFromHost()
        {
            if (_globalScopeVariables is PyNamespace) return;
            for (int i = _keys.Count - 1; i >= 0; i--)
            {
                if (_keys[i] is not PyString key) continue;
                if (!_globalScopeVariables.TryGetValue(key.Value, out var value)) base.DelItem(key);
                else if (!ReferenceEquals(_dict.StoredValue(key), value)) base.MpAssSubscript(key, value);
            }
            if ((int)base.Len() == _globalScopeVariables.Count + _nonStringCount) return;
            foreach (var pair in _globalScopeVariables)
            {
                var key = PyString.From(pair.Key);
                if (!base.ContainsKey(key)) base.MpAssSubscript(key, pair.Value);
            }
        }

        private bool RemoveHost(string name) => _globalScopeVariables is PyNamespace owned
            ? owned.RemoveFromMapping(name) : _globalScopeVariables.Remove(name);

        internal PyObject ReadStoredKey(PyObject key) => _dict.StoredValue(key);
        internal void ClearStringKeys()
        {
            for (int i = _keys.Count - 1; i >= 0; i--)
            {
                var key = _keys[i];
                if (key is not PyString) continue;
                _dict.Remove(key, out _, out _, knownHash: true);
                RemoveOrderedKey(key);
            }
            ((PyNamespace)_globalScopeVariables).RebuildLookup();
        }
        internal int StringKeyCount()
        {
            int count = 0;
            foreach (var key in _keys) if (key is PyString) count++;
            return count;
        }
        internal bool RemoveName(string name)
        {
            if (!_dict.Remove(PyString.From(name), out _, out var stored)) return false;
            RemoveOrderedKey(stored);
            if (_globalScopeVariables is PyNamespace owned && !owned.GenericKeys) owned.RemoveFromMapping(name);
            return true;
        }

        public override PyTuple PopItem()
        {
            var result = base.PopItem();
            if (_globalScopeVariables is PyNamespace canonical)
            {
                if (!canonical.GenericKeys) canonical.RemoveFromMapping(((PyString)result.Items[0]).Value);
                return result;
            }
            if (result.Items[0] is PyString name) RemoveHost(name.Value);
            else _nonStringCount--;
            return result;
        }
    }

    /// <summary>
    /// An instance of a Python class deriving from dict: dict_new allocates the dict layout with
    /// ob_type set to the subclass (Objects/dictobject.c dict_new), so it is a dict whose
    /// _customType is the class, with the __dict__ a heap type adds.
    /// </summary>
    public sealed class PyDictSubclass : PyDict
    {
        public PyDictSubclass(PyClass cls) => SetCustomType(cls);


    }
}
