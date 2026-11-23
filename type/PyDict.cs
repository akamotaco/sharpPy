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

            // CPython 3.12: Objects/dictobject.c:3627-3630 - dict_get
            // D.get(k[,d]) -> D[k] if k in D, else d.  d defaults to None.
            dictType.TypeDict["get"] = new PyMethodDescriptor(
                "get", dictType,
                (self, args, kwargs) => {


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


                    return new PyDictView(((PyDict)self), DictViewKind.Keys);
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/dictobject.c:3677-3680 - dict_values
            // D.values() -> an object providing a view on D's values
            dictType.TypeDict["values"] = new PyMethodDescriptor(
                "values", dictType,
                (self, args, kwargs) => {


                    return new PyDictView(((PyDict)self), DictViewKind.Values);
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/dictobject.c:3682-3685 - dict_items
            // D.items() -> a set-like object providing a view on D's items
            dictType.TypeDict["items"] = new PyMethodDescriptor(
                "items", dictType,
                (self, args, kwargs) => {


                    return new PyDictView(((PyDict)self), DictViewKind.Items);
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/dictobject.c:3632-3635 - dict_setdefault
            // D.setdefault(k[,d]) -> D.get(k,d), also set D[k]=d if k not in D
            dictType.TypeDict["setdefault"] = new PyMethodDescriptor(
                "setdefault", dictType,
                (self, args, kwargs) => {


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
                    var dict = (PyDict)self;

                    return dict.PopItem();
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/dictobject.c:3687-3690 - dict_clear
            // D.clear() -> None.  Remove all items from D.
            dictType.TypeDict["clear"] = new PyMethodDescriptor(
                "clear", dictType,
                (self, args, kwargs) => {
                    var dict = (PyDict)self;

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
                    var target = (PyDict)self;

                    UpdateCommon(target, args, kwargs, "update");
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
                    var left = (PyDict)self;
                    if (!IsDictLike(args[0])) return PyNotImplemented.Instance;
                    return left.BitwiseOr(args[0]);
                }
            );
            dictType.TypeDict["__ror__"] = new PyWrapperDescriptor(
                "__ror__", dictType,
                (self, args, kwargs) => {
                    return PyNotImplemented.Instance;
                }
            );
            dictType.TypeDict["__ior__"] = new PyWrapperDescriptor(
                "__ior__", dictType,
                (self, args, kwargs) => {
                    var target = (PyDict)self;
                    // dict_ior: dict_update_arg, then self.
                    UpdateArg(target, args[0]);
                    return self;
                }
            );
            dictType.TypeDict["__eq__"] = new PyWrapperDescriptor(
                "__eq__", dictType,
                (self, args, kwargs) => {
                    var left = (PyDict)self;
                    if (!IsDictLike(args[0])) return PyNotImplemented.Instance;
                    // dict_richcompare: dict_equal on the two tables.
                    return PyBool.FromBool(left.CheckDictEquality((PyDict)args[0]));
                }
            );
            dictType.TypeDict["__ne__"] = new PyWrapperDescriptor(
                "__ne__", dictType,
                (self, args, kwargs) => {
                    var left = (PyDict)self;
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
                        return PyNotImplemented.Instance;
                    }
                );
            }

            // CPython 3.12: Objects/dictobject.c:3652-3655 - dict_copy
            // D.copy() -> a shallow copy of D
            dictType.TypeDict["copy"] = new PyMethodDescriptor(
                "copy", dictType,
                (self, args, kwargs) => {
                    return DictCopy((PyDict)self);
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
            // dict_methods: {"__contains__", dict___contains__, METH_O | METH_COEXIST}
            dictType.TypeDict["__contains__"] = new PyMethodDescriptor("__contains__", dictType, (self, args, kwargs) =>
            {
                var dict = (PyDict)self;
                return dict.ContainsItem(args[0]);
            }, acceptsKwargs: true);
            dictType.TypeDict["__getitem__"] = new PyMethodDescriptor(
                "__getitem__", dictType,
                (self, args, kwargs) => {
                    // CPython 3.12.15 measured: dict.__getitem__ is METH_O in the dict method table

                    return ((PyDict)self).DictSubscript(args[0]);
                },
                minArgs: 1, maxArgs: 1
            );

            // CPython 3.12: __setitem__ slot (mp_ass_subscript)
            // CPython 3.12: Objects/dictobject.c:2524-2530 (dict_ass_sub)
            // D[key] = value
            //
            // Same pattern: access actual dict storage, NOT polymorphic SetItem
            dictType.TypeDict["__setitem__"] = new PyWrapperDescriptor(
                "__setitem__", dictType,
                (self, args, kwargs) => {
                    // CPython 3.12.15 measured: dict.__setitem__ is METH_VARARGS with format "OO";
                    // PyArg_ParseTuple reports without the function name (note leading space).

                    // dict_ass_sub: the table itself.
                    var pyDict = (PyDict)self;
                    pyDict.DictAssSub(args[0], args[1]);
                    return PyNone.Instance;
                },
                minArgs: 2, maxArgs: 2
            );

            // CPython 3.12: __delitem__ slot (mp_ass_subscript with NULL value)
            // CPython 3.12: Objects/dictobject.c:2524-2530 (dict_ass_sub with w == NULL)
            // del D[key]
            //
            // Same pattern: access actual dict storage, NOT polymorphic DelItem
            dictType.TypeDict["__delitem__"] = new PyWrapperDescriptor(
                "__delitem__", dictType,
                (self, args, kwargs) => {
                    // CPython 3.12.15 measured: dict.__delitem__ reports "expected 1 argument, got N".
                    // dict_ass_sub without a value: the table itself.
                    var pyDict = (PyDict)self;
                    pyDict.DictDelSub(args[0]);
                    return PyNone.Instance;
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
                        ((PyDict)self).Length()));
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
        // ma_keys: the hash table and dk_entries, which also hold the insertion order.
        protected readonly PyDictionaryStorage _dict;

        // 내부 딕셔너리 접근용 (타입 생성 등에서 사용)
        internal PyDictionaryStorage InternalDict => _dict;
        // Bypass Python __getitem__ overrides while allowing internal storage adapters.
        internal virtual bool TryGetStoredValue(PyObject key, out PyObject value) => _dict.TryGetValue(key, out value);
        internal PyDictionaryStorage.Enumerator GetStorageKeyEnumerator() => _dict.GetEnumerator();

        /// <summary>
        /// CPython 3.12: Include/cpython/dictobject.h - 내부 딕셔너리 순회용
        /// DICT_UPDATE/DICT_MERGE 명령어에서 사용
        /// </summary>
        public IEnumerable<KeyValuePair<PyObject, PyObject>> GetInternalDict()
        {
            foreach (var entry in _dict)
                yield return entry;
        }

        public PyDict()
        {
            _dict = new PyDictionaryStorage();
        }

        public PyDict(Dictionary<string, PyObject> items)
        {
            _dict = new PyDictionaryStorage();
            foreach (var kv in items)
                _dict[PyString.From(kv.Key)] = kv.Value;
        }

        public PyDict(Dictionary<PyObject, PyObject> items)
        {
            _dict = new PyDictionaryStorage(items);
        }

        public override PyType GetPyType() => _customType ?? PyType.DictType;

        // PyX_CheckExact
        internal bool IsExact => IsExactly(PyType.DictType);

        // CPython 3.12: Objects/dictobject.c - dict_bool
        // Optimized: Direct count check, no MRO traversal
        protected internal override bool Bool() => _dict.Count > 0;

        #endregion

        #region String Representation

        protected internal override PyString Str() => ToRepr();

        protected internal override PyString Repr() => DictRepr();

        /// <summary>dict_repr itself, which dict.__repr__ and a subtype's own repr (defdict_repr) call.</summary>
        internal PyString DictRepr()
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

        // dict_repr: PyDict_Next over the entries, each key and value repr'd as it is reached, so a
        // repr that changes the dict is seen by what follows.
        private PyString ToReprContents()
        {
            var writer = new System.Text.StringBuilder();
            writer.Append('{');
            bool first = true;
            int position = 0;
            while (_dict.NextEntry(ref position, out var entry))
            {
                if (!first)
                    writer.Append(", ");
                first = false;
                // Prevent repr from deleting key or value during key format.
                var key = entry.Key;
                var value = entry.Value;
                writer.Append(key.ToRepr().Value);
                writer.Append(": ");
                writer.Append(value.ToRepr().Value);
            }
            writer.Append('}');
            return PyString.From(writer.ToString());
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
        protected internal override PyObject MpSubscript(PyObject key) => DictSubscript(key);

        /// <summary>
        /// dict_subscript, the mp_subscript of dict and of every type that inherits it: the table,
        /// then, when the type is not exactly dict (!PyDict_CheckExact), its __missing__.
        /// </summary>
        internal PyObject DictSubscript(PyObject key)
        {
            if (TryGetStoredValue(key, out PyObject value))
                return value;
            if (!IsExact && LookupSpecialMethodUncached("__missing__") is { } missing)
                return CallSpecial(missing, key);
            throw PyKeyError.Create(key);
        }

        /// <summary>
        /// 키에 값 설정 dict[key] = value
        /// Python 3.7+: 삽입 순서 보장
        /// </summary>
        protected internal override void MpAssSubscript(PyObject key, PyObject value)
        {
            _dict.Set(key, value);
        }

        /// <summary>
        /// 키 삭제 del dict[key]
        /// Python 3.7+: 삽입 순서 유지 (_keys에서도 제거)
        /// </summary>
        // dict_ass_sub without a value: PyDict_DelItem.
        protected internal override void MpDelSubscript(PyObject key) => DelItem(key);

        /// <summary>
        /// dict_ass_sub itself, what dict.__setitem__ and dict.__delitem__ call: a type with an
        /// mp_ass_subscript of its own (OrderedDict) is written in the table alone here.
        /// </summary>
        internal virtual void DictAssSub(PyObject key, PyObject value) => MpAssSubscript(key, value);
        internal virtual void DictDelSub(PyObject key) => MpDelSubscript(key);

        public virtual PyObject DelItem(PyObject key)
        {
            if (!_dict.Remove(key, out _, out _))
                throw PyKeyError.Create(key);
            return PyNone.Instance;
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
            if (_dict.Remove(key, out PyObject value, out _))
                return value;

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

            // dict_popitem: the last entry of dk_entries, which then ends before it.
            var last = _dict.LastEntry();
            _dict.Remove(last.Key, out _, out _, knownHash: true);
            return PyTuple.From(last.Key, last.Value);
        }

        /// <summary>
        /// 모든 키-값 쌍 제거
        /// Python 3.7+: 삽입 순서 리스트도 함께 제거
        /// </summary>
        public virtual PyNone Clear()
        {
            _dict.Clear();
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
        public virtual void SetItemStorage(PyObject key, PyObject value) => MpAssSubscript(key, value);

        internal static bool IsDictLike(PyObject value) => value is PyDict;

        /// <summary>
        /// dict_merge (Objects/dictobject.c): PyDict_Check(b) and Py_TYPE(b)->tp_iter == dict_iter,
        /// when the storage may be read directly instead of through keys().
        /// </summary>
        internal static bool IterIsDictIter(PyObject value) =>
            IsDictLike(value) && (value.GetPyType() is not SharpPy.PyClass heap
                ? value is not SharpPy.Modules.PyODict
                : heap.IterSlotIs(PyType.DictType));


        /// <summary>
        /// dict_update_common (Objects/dictobject.c), dict.update's and dict.__init__'s body:
        /// PyArg_UnpackTuple(args, methname, 0, 1), dict_update_arg, then the keywords merged
        /// once PyArg_ValidateKeywordArguments has found every name a str.
        /// </summary>
        internal static void UpdateCommon(PyDict target, PyObject[] args, PyDict kwargs, string methname)
        {
            if (args.Length > 1)
                throw PyTypeError.Create($"{methname} expected at most 1 argument, got {args.Length}");
            if (args.Length == 1)
                UpdateArg(target, args[0]);
            if (kwargs == null || kwargs.Length() == 0)
                return;
            foreach (var entry in kwargs.GetInternalDict())
                if (entry.Key is not PyString)
                    throw PyTypeError.Create("keywords must be strings");
            Merge(target, kwargs, 1);
        }

        /// <summary>
        /// dict_update_arg: an exact dict, or anything with a keys attribute, is merged as a
        /// mapping; anything else is a sequence of pairs (PyDict_MergeFromSeq2).
        /// </summary>
        internal static void UpdateArg(PyDict self, PyObject arg)
        {
            if (arg is PyDict { IsExact: true } || arg.LookupAttribute("keys") != null)
                Merge(self, arg, 1);
            else
                MergeFromSeq2(self, arg, true);
        }

        /// <summary>PyDict_MergeFromSeq2: each item a sequence of two, set or set-default.</summary>
        internal static void MergeFromSeq2(PyDict d, PyObject seq2, bool @override)
        {
            int i = 0;
            foreach (var item in PyObject.IterateItems(seq2))
            {
                PyObject[] fast;
                try { fast = PyList.SequenceFast(item, ""); }
                catch (PythonException error) when (error.PyException is PyTypeError)
                {
                    throw PyTypeError.Create($"cannot convert dictionary update sequence element #{i} to a sequence");
                }
                if (fast.Length != 2)
                    throw PyValueError.Create($"dictionary update sequence element #{i} has length {fast.Length}; 2 is required");
                if (@override)
                    d.SetItemStorage(fast[0], fast[1]);
                else
                    d._dict.SetDefault(fast[0], fast[1], out _);
                i++;
            }
        }

        /// <summary>
        /// dict_merge (Objects/dictobject.c), the one merge of a mapping into a dict: PyDict_Update
        /// and PyDict_Merge (override 1 replaces, 0 keeps what is there) and _PyDict_MergeEx's
        /// override 2, which raises KeyError(key) for a key already there (DICT_MERGE). A dict
        /// iterated as a dict is read from its table; anything else through PyMapping_Keys and
        /// PyObject_GetItem.
        /// </summary>
        internal static void Merge(PyDict a, PyObject b, int @override)
        {
            if (IterIsDictIter(b))
            {
                var other = ((PyDict)b)._dict;
                if (ReferenceEquals(a, b) || other.Count == 0)
                    return;
                if (a._dict.Count == 0)
                {
                    @override = 1;
                    if (other.CanCloneForMerge)
                    {
                        a._dict.CopyFrom(other);
                        return;
                    }
                }
                a._dict.PrepareMerge(other.Count);
                int position = 0, originalSize = other.EntriesUsed;
                while (other.NextEntry(ref position, out var entry))
                {
                    if (@override == 1 || !a._dict.TryGetKnownHash(entry.Key, entry.Hash, out _))
                        a._dict.SetKnownHash(entry.Key, entry.Value, entry.Hash);
                    else if (@override != 0)
                        throw PyKeyError.Create(entry.Key);
                    if (originalSize != other.EntriesUsed)
                        throw PyRuntimeError.Create("dict mutated during update");
                }
                return;
            }
            // PyObject_GetIter of the list: the list as it is at each step.
            var keys = MappingKeys(b);
            for (int i = 0; i < keys.Length(); i++)
            {
                var key = keys.Items[i];
                if (@override != 1 && a._dict.ContainsKey(key))
                {
                    if (@override == 0)
                        continue;
                    throw PyKeyError.Create(key);
                }
                a.SetItemStorage(key, PyAbstract.GetItem(b, key));
            }
        }

        /// <summary>
        /// PyMapping_Keys (Objects/abstract.c): an exact dict's keys, else what keys() returns as
        /// a list (method_output_as_list).
        /// </summary>
        internal static PyList MappingKeys(PyObject o)
        {
            if (o is PyDict { IsExact: true } dict)
                return dict.Keys();
            var output = o.GetAttribute("keys").Call(Array.Empty<PyObject>(), null);
            if (output is PyList { IsExact: true } list)
                return list;
            PyObject iterator;
            try { iterator = output.GetIterator(); }
            catch (PythonException error) when (error.PyException is PyTypeError)
            {
                throw PyTypeError.Create($"{o.GetTypeName()}.keys() returned a non-iterable (type {output.GetTypeName()})");
            }
            var keys = new PyList();
            keys.InplaceAdd(iterator);
            return keys;
        }

        /// <summary>
        /// PyDict_Copy: an empty dict for an empty one, the table cloned when the type iterates as
        /// a dict, else a new dict merged from it.
        /// </summary>
        internal static PyDict DictCopy(PyDict o)
        {
            if (o._dict.Count == 0)
                return new PyDict();
            if (IterIsDictIter(o))
                return o.Copy();
            var copy = new PyDict();
            Merge(copy, o, 1);
            return copy;
        }

        /// <summary>PyDict_Update: dict_merge with override 1.</summary>
        public virtual PyNone Update(PyObject other)
        {
            Merge(this, other, 1);
            return PyNone.Instance;
        }

        /// <summary>
        /// 키가 없으면 기본값 설정하고 반환
        /// Python 3.7+: 삽입 순서 유지
        /// </summary>
        public virtual PyObject SetDefault(PyObject key, PyObject defaultValue = null)
        {
            return _dict.SetDefault(key, defaultValue ?? PyNone.Instance, out _);
        }

        #endregion

        #region Internal Snapshots (Python methods return PyDictView)

        /// <summary>
        /// 모든 키의 뷰 반환
        /// Python 3.7+: 삽입 순서 보장
        /// </summary>
        public PyList Keys()
        {
            var keys = new PyObject[_dict.Count];
            int i = 0;
            foreach (var entry in _dict)
                keys[i++] = entry.Key;
            return new PyList(keys);
        }

        public PyList Values()
        {
            var values = new PyObject[_dict.Count];
            int i = 0;
            foreach (var entry in _dict)
                values[i++] = entry.Value;
            return new PyList(values);
        }

        public PyList Items()
        {
            var items = new PyObject[_dict.Count];
            int i = 0;
            foreach (var entry in _dict)
                items[i++] = PyTuple.From(entry.Key, entry.Value);
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
            _dict.SetKnownHash(key, value, hash);
        }

        /// <summary>dict.fromkeys(keys, value) for the host: _PyDict_FromKeys on dict.</summary>
        public static PyDict FromKeys(PyObject keys, PyObject value = null) =>
            (PyDict)FromKeys(PyType.DictType, keys, value ?? PyNone.Instance);

        #endregion

        #region Copy

        /// <summary>
        /// 딕셔너리의 얕은 복사본 생성
        /// Python 3.7+: 삽입 순서 보장
        /// </summary>
        public virtual PyDict Copy()
        {
            if (!_dict.CanCloneForCopy)
            {
                // The entries alone, whatever the type's iteration.
                var compact = new PyDict();
                compact._dict.PrepareMerge(_dict.Count);
                int position = 0;
                while (_dict.NextEntry(ref position, out var entry))
                    compact._dict.SetKnownHash(entry.Key, entry.Value, entry.Hash);
                return compact;
            }
            return new PyDict(_dict);
        }

        // clone_combined_dict_keys: the table and its entries, order included.
        private PyDict(PyDictionaryStorage items)
        {
            _dict = items.Clone();
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
            var result = DictCopy(this);
            UpdateArg(result, other);
            return result;
        }

        /// <summary>
        /// Dict in-place merge operator (|=) - Python 3.9+
        /// CPython 3.12: Objects/dictobject.c:dict_ior (lines 3172-3194)
        /// Merges other into self in-place and returns self
        /// </summary>
        public override PyObject? InplaceBitwiseOr(PyObject other)
        {
            // dict_ior: dict_update_arg, then self.
            UpdateArg(this, other);
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
