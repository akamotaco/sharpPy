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

            // Helper to get PyDict from self (handles both PyDict and PyClassInstance dict subclasses)
            static PyDict GetDict(PyObject self, PyType dictType)
            {
                if (self is PyDict dict)
                    return dict;

                if (self is PyClassInstance ci)
                {
                    var storage = ci.GetDictStorage();
                    if (storage != null)
                        return storage;
                }

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
                        throw PyTypeError.Create($"get expected at most 2 arguments, got {args.Length}");

                    var key = args[0];
                    var defaultValue = args.Length == 2 ? args[1] : PyNone.Instance;

                    // For C# PyDict, use direct method
                    if (self is PyDict dict)
                    {
                        return dict.Get(key, defaultValue);
                    }

                    // For Python dict subclasses
                    var contains = self.Contains(key);
                    if (contains == PyBool.True)
                        return self.GetItem(key);

                    return defaultValue;
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

                    return GetDict(self, dictType).Keys();
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

                    return GetDict(self, dictType).Values();
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

                    return GetDict(self, dictType).Items();
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
                        throw PyTypeError.Create($"setdefault expected at most 2 arguments, got {args.Length}");

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
                        throw PyTypeError.Create($"pop expected at most 2 arguments, got {args.Length}");

                    var key = args[0];
                    var defaultValue = args.Length == 2 ? args[1] : null;

                    // For C# PyDict, use direct method
                    if (self is PyDict dict)
                        return dict.Pop(key, defaultValue);

                    // For Python dict subclasses, use __delitem__ + __getitem__
                    // CPython: if key in D: v = D[key]; del D[key]; return v else: return default
                    var contains = self.Contains(key);
                    if (contains == PyBool.True)
                    {
                        var value = self.GetItem(key);

                        // Call __delitem__ special method
                        var delitem = self.GetAttribute("__delitem__");
                        if (delitem != null && delitem.IsCallable())
                        {
                            delitem.Call(new PyObject[] { key }, null);
                        }
                        else
                        {
                            throw PyTypeError.Create($"'{self.GetTypeName()}' object does not support item deletion");
                        }

                        return value;
                    }

                    if (defaultValue != null)
                        return defaultValue;

                    throw PyKeyError.Create(key);
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
                    if (self is not PyDict dict)
                        throw PyTypeError.Create($"descriptor 'update' requires a 'dict' object but received a '{self.GetTypeName()}'");

                    if (args.Length > 1)
                        throw PyTypeError.Create($"update expected at most 1 arguments, got {args.Length}");

                    if (args.Length == 1)
                    {
                        return dict.Update(args[0]);
                    }

                    // TODO: Handle **kwargs update when kwargs support is added
                    return PyNone.Instance;
                },
                minArgs: 0, maxArgs: 1
            );

            // CPython 3.12: Objects/dictobject.c:3652-3655 - dict_copy
            // D.copy() -> a shallow copy of D
            dictType.TypeDict["copy"] = new PyMethodDescriptor(
                "copy", dictType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"copy() takes no arguments ({args.Length} given)");
                    if (self is not PyDict dict)
                        throw PyTypeError.Create($"descriptor 'copy' requires a 'dict' object but received a '{self.GetTypeName()}'");

                    return dict.Copy();
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
                        throw PyTypeError.Create($"fromkeys expected at most 2 arguments, got {args.Length - 1}");

                    var keys = args[1];
                    var value = args.Length == 3 ? args[2] : PyNone.Instance;
                    return PyDict.FromKeys(keys, value);
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
                    if (args.Length != 1)
                        throw PyTypeError.Create($"__getitem__ expected 1 argument, got {args.Length}");

                    // CPython 3.12: PyDict_GetItem checks PyDict_Check then accesses internal storage
                    // For PyDict: access _dict directly
                    // For dict subclasses (PyClassInstance): access _dictStorage field via reflection
                    if (self is PyDict pyDict)
                    {
                        // Real PyDict: use internal _dict
                        return pyDict._dict.TryGetValue(args[0], out var value)
                            ? value
                            : throw PyKeyError.Create(args[0]);
                    }
                    else if (self is PyClassInstance classInstance)
                    {
                        // Dict subclass: use GetDictStorage() which is now properly initialized
                        var dictStorage = classInstance.GetDictStorage();
                        if (dictStorage != null)
                        {
                            #if DEBUG
                            var keyStr = args[0] is PyString ps ? ps.Value : args[0]?.ToString() ?? "null";
                            if (classInstance.GetTypeName() == "_EnumDict" && (keyStr == "STRICT" || keyStr == "CONFORM" || keyStr == "EJECT" || keyStr == "KEEP"))
                            {
                                Console.WriteLine($"[DEBUG-DICT-GETITEM] dict.__getitem__ for _EnumDict['{keyStr}']:");
                                Console.WriteLine($"  dictStorage != null: true");
                                Console.WriteLine($"  dictStorage._dict != null: {dictStorage._dict != null}");
                                try
                                {
                                    var hasKey = dictStorage._dict.TryGetValue(args[0], out var debugValue);
                                    Console.WriteLine($"  dictStorage._dict.ContainsKey: {hasKey}");
                                    if (hasKey)
                                    {
                                        Console.WriteLine($"  Value: {debugValue}, type={debugValue?.GetType().Name}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"  Exception during TryGetValue: {ex.Message}");
                                }
                            }
                            #endif
                            if (dictStorage._dict.TryGetValue(args[0], out var value))
                                return value;

                            // CPython 3.12: Objects/dictobject.c:2143-2157 - call __missing__ if key not found
                            // Try __missing__ method for dict subclasses
                            try
                            {
                                var missingMethod = classInstance.GetAttribute("__missing__");
                                if (missingMethod != null)
                                {
                                    PyObject result;
                                    if (missingMethod is PyMethod boundMethod)
                                        result = boundMethod.Call(new PyObject[] { args[0] }, null);
                                    else if (missingMethod is PyFunction func)
                                        result = func.Call(new PyObject[] { classInstance, args[0] }, null);
                                    else if (missingMethod is PyBuiltinFunction builtinFunc)
                                        result = builtinFunc.Call(new PyObject[] { args[0] });
                                    else
                                        result = missingMethod.Call(new PyObject[] { args[0] }, null);
                                    return result;
                                }
                            }
                            catch
                            {
                                // No __missing__ method or it raised an error
                            }

                            throw PyKeyError.Create(args[0]);
                        }
                    }

                    throw PyTypeError.Create($"descriptor '__getitem__' requires a 'dict' object but received a '{self.GetTypeName()}'");
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
                    if (args.Length != 2)
                        throw PyTypeError.Create($"__setitem__ expected 2 arguments, got {args.Length}");

                    // CPython 3.12: PyDict_SetItem checks PyDict_Check then writes to internal storage
                    if (self is PyDict pyDict)
                    {
                        // Real PyDict: write to _dict directly
                        pyDict.SetItem(args[0], args[1]);  // Use SetItem to maintain _keys order
                        return PyNone.Instance;
                    }
                    else if (self is PyClassInstance classInstance)
                    {
                        // Dict subclass: use GetDictStorage() which is now properly initialized
                        var dictStorage = classInstance.GetDictStorage();
                        if (dictStorage != null)
                        {
                            #if DEBUG
                            var keyStr = args[0] is PyString ps ? ps.Value : args[0]?.ToString() ?? "null";
                            if (classInstance.GetTypeName() == "_EnumDict" && (keyStr == "STRICT" || keyStr == "CONFORM" || keyStr == "EJECT" || keyStr == "KEEP"))
                            {
                                Console.WriteLine($"[DEBUG-DICT-SETITEM] dict.__setitem__ for _EnumDict['{keyStr}'] = {args[1]}, type={args[1]?.GetType().Name}");
                            }
                            #endif
                            dictStorage.SetItem(args[0], args[1]);  // Use SetItem to maintain _keys order
                            return PyNone.Instance;
                        }
                    }

                    throw PyTypeError.Create($"descriptor '__setitem__' requires a 'dict' object but received a '{self.GetTypeName()}'");
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
                    if (args.Length != 1)
                        throw PyTypeError.Create($"__delitem__ expected 1 argument, got {args.Length}");

                    // CPython 3.12: PyDict_DelItem checks PyDict_Check then deletes from internal storage
                    if (self is PyDict pyDict)
                    {
                        // Real PyDict: delete from _dict directly
                        pyDict.DelItem(args[0]);
                        return PyNone.Instance;
                    }
                    else if (self is PyClassInstance classInstance)
                    {
                        // Dict subclass: use GetDictStorage() which is now properly initialized
                        var dictStorage = classInstance.GetDictStorage();
                        if (dictStorage != null)
                        {
                            dictStorage.DelItem(args[0]);
                            return PyNone.Instance;
                        }
                    }

                    throw PyTypeError.Create($"descriptor '__delitem__' requires a 'dict' object but received a '{self.GetTypeName()}'");
                },
                minArgs: 1, maxArgs: 1
            );

            // CPython 3.12: Objects/dictobject.c:2547
            // {"__class_getitem__", Py_GenericAlias, METH_O|METH_CLASS, PyDoc_STR("See PEP 585")},
            dictType.TypeDict["__class_getitem__"] = new PyBuiltinClassMethod("__class_getitem__",
                (cls, arg) => new PyGenericAlias(cls as PyType ?? throw PyTypeError.Create("Expected type"), arg)
            );
        }

        #region Core Properties

        // PyObject를 키로 사용하기 위한 사용자 정의 비교기
        private class PyObjectEqualityComparer : IEqualityComparer<PyObject>
        {
            public bool Equals(PyObject x, PyObject y)
            {
                if (x == null && y == null) return true;
                if (x == null || y == null) return false;
                return ((PyBool)x.RichCompare(y, CompareOp.EQ)).Value;
            }

            public int GetHashCode(PyObject obj)
            {
                return obj?.ToHash() ?? 0;
            }
        }

        // Python 3.7+: dict는 삽입 순서를 보장해야 함
        // C# Dictionary는 순서 보장 안 함 → List + Dictionary 혼합 사용
        protected readonly Dictionary<PyObject, PyObject> _dict;  // O(1) 조회용
        protected readonly List<PyObject> _keys;  // 삽입 순서 보관

        // 내부 딕셔너리 접근용 (타입 생성 등에서 사용)
        internal Dictionary<PyObject, PyObject> InternalDict => _dict;

        /// <summary>
        /// CPython 3.12: Include/cpython/dictobject.h - 내부 딕셔너리 순회용
        /// DICT_UPDATE/DICT_MERGE 명령어에서 사용
        /// </summary>
        public IEnumerable<KeyValuePair<PyObject, PyObject>> GetInternalDict()
        {
            // Python 3.7+: 삽입 순서 보장하여 반환
            foreach (var key in _keys)
            {
                yield return new KeyValuePair<PyObject, PyObject>(key, _dict[key]);
            }
        }

        public PyDict()
        {
            _dict = new Dictionary<PyObject, PyObject>(new PyObjectEqualityComparer());
            _keys = new List<PyObject>();
        }

        public PyDict(Dictionary<string, PyObject> items)
        {
            _dict = new Dictionary<PyObject, PyObject>(new PyObjectEqualityComparer());
            _keys = new List<PyObject>();
            foreach (var kv in items)
            {
                var key = new PyString(kv.Key);
                _dict[key] = kv.Value;
                _keys.Add(key);
            }
        }

        public PyDict(Dictionary<PyObject, PyObject> items)
        {
            _dict = new Dictionary<PyObject, PyObject>(items, new PyObjectEqualityComparer());
            _keys = new List<PyObject>(items.Keys);
        }

        public override PyType GetPyType() => PyType.DictType;
        public override string GetTypeName() => "dict";

        // CPython 3.12: Objects/dictobject.c - dict_bool
        // Optimized: Direct count check, no MRO traversal
        public override bool IsTrue() => _dict.Count > 0;

        #endregion

        #region String Representation

        public override PyString ToStr() => ToRepr();

        public override PyString ToRepr()
        {
            if (_dict.Count == 0) return StringCache.GetOrCreate("{}");

            // Python 3.7+: 삽입 순서대로 출력
            // Performance: string.Create() - CPython-style single allocation

            // Step 1: Calculate total length and cache reprs
            var keyReprs = new string[_keys.Count];
            var valueReprs = new string[_keys.Count];
            int totalLength = 2; // "{}"
            for (int i = 0; i < _keys.Count; i++)
            {
                keyReprs[i] = _keys[i].ToRepr().Value;
                valueReprs[i] = _dict[_keys[i]].ToRepr().Value;

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

            return StringCache.GetOrCreate(result);
        }

        #endregion

        #region Hash and Equality

        protected override PyObject PyEquals(PyObject other)
        {
            return other switch
            {
                PyDict otherDict => PyBool.FromBool(CheckDictEquality(otherDict)),
                _ => PyBool.False
            };
        }

        // Performance: Eliminated LINQ (.All) - manual iteration
        private bool CheckDictEquality(PyDict otherDict)
        {
            if (_dict.Count != otherDict._dict.Count)
                return false;

            foreach (var kv in _dict)
            {
                if (!otherDict._dict.ContainsKey(kv.Key))
                    return false;
                if (!((PyBool)otherDict._dict[kv.Key].RichCompare(kv.Value, CompareOp.EQ)).Value)
                    return false;
            }
            return true;
        }

        #endregion

        #region Dictionary Operations

        /// <summary>
        /// 키로 값 접근 dict[key]
        /// </summary>
        public override PyObject GetItem(PyObject key)
        {
            if (_dict.TryGetValue(key, out PyObject value))
                return value;

            throw PyKeyError.Create(key.ToRepr());
        }

        /// <summary>
        /// 키에 값 설정 dict[key] = value
        /// Python 3.7+: 삽입 순서 보장
        /// </summary>
        public override void SetItem(PyObject key, PyObject value)
        {
            // 새 키인 경우에만 _keys에 추가 (삽입 순서 보장)
            if (!_dict.ContainsKey(key))
            {
                _keys.Add(key);
            }
            _dict[key] = value;
        }

        /// <summary>
        /// C# indexer for PyObject keys.
        /// CPython 3.12: Objects/dictobject.c:2488-2530 (dict_subscript / dict_ass_sub)
        /// </summary>
        public PyObject this[PyObject key]
        {
            get => GetItem(key);
            set => SetItem(key, value);
        }

        /// <summary>
        /// C# indexer for string keys (convenience).
        /// CPython 3.12: Objects/dictobject.c:3891-3927 (PyDict_GetItemString / PyDict_SetItemString)
        /// </summary>
        public PyObject this[string key]
        {
            get => GetItem(new PyString(key));
            set => SetItem(new PyString(key), value);
        }

        /// <summary>
        /// 키 삭제 del dict[key]
        /// Python 3.7+: 삽입 순서 유지 (_keys에서도 제거)
        /// </summary>
        public virtual PyObject DelItem(PyObject key)
        {
            if (!_dict.Remove(key))
                throw PyKeyError.Create(key.ToRepr());

            // _keys에서도 제거 (삽입 순서 유지)
            // Use PyObjectEqualityComparer for proper Python equality semantics
            // (List.Remove uses Object.Equals which doesn't work for PyObject)
            var comparer = new PyObjectEqualityComparer();
            for (int i = 0; i < _keys.Count; i++)
            {
                if (comparer.Equals(_keys[i], key))
                {
                    _keys.RemoveAt(i);
                    break;
                }
            }
            return PyNone.Instance;
        }

        /// <summary>
        /// 키 포함 여부 확인 (in 연산자)
        /// </summary>
        public override PyBool Contains(PyObject key)
        {
            return PyBool.FromBool(_dict.ContainsKey(key));
        }

        /// <summary>
        /// CPython 3.12: PyDict_Contains - 키 포함 여부 확인 (bool 반환)
        /// DICT_MERGE 명령어에서 중복 키 검사에 사용
        /// </summary>
        public bool ContainsKey(PyObject key)
        {
            return _dict.ContainsKey(key);
        }

        /// <summary>
        /// 기본값과 함께 값 가져오기
        /// </summary>
        public PyObject Get(PyObject key, PyObject defaultValue = null)
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
            if (_dict.TryGetValue(key, out PyObject value))
            {
                _dict.Remove(key);
                _keys.Remove(key);  // 삽입 순서 유지
                return value;
            }

            if (defaultValue != null)
                return defaultValue;

            throw PyKeyError.Create(key.ToRepr());
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
            var value = _dict[lastKey];
            _dict.Remove(lastKey);
            _keys.RemoveAt(_keys.Count - 1);
            return TupleCache.CreatePair(lastKey, value);
        }

        /// <summary>
        /// 모든 키-값 쌍 제거
        /// Python 3.7+: 삽입 순서 리스트도 함께 제거
        /// </summary>
        public virtual PyNone Clear()
        {
            _dict.Clear();
            _keys.Clear();
            return PyNone.Instance;
        }

        /// <summary>
        /// 다른 딕셔너리나 매핑의 키-값으로 업데이트 (CPython dict.update 호환)
        /// Python 3.7+: 삽입 순서 유지
        /// </summary>
        public virtual PyNone Update(PyObject other)
        {
            // CPython 3.12: dict.update() can accept dict, mappingproxy, or any mapping-like object
            if (other is PyDict otherDict)
            {
                // CPython 3.12: Objects/dictobject.c:2811-2990 (dict_merge)
                // Line 2967: PyDict_SetItem (does NOT call __setitem__, updates storage directly)
                // For dict subclasses, PyDict_SetItem updates internal storage directly,
                // bypassing user-defined __setitem__
                foreach (var key in otherDict._keys)
                {
                    var value = otherDict._dict[key];
                    // Direct storage update (bypass __setitem__)
                    if (!_dict.ContainsKey(key))
                    {
                        _keys.Add(key);
                    }
                    _dict[key] = value;
                }
            }
            else if (other is PyMappingProxy mappingProxy)
            {
                // CPython 3.12: Objects/dictobject.c:2811-2990 (dict_merge)
                // Line 2961: value = PyObject_GetItem(b, key);
                // Line 2967: PyDict_SetItem (does NOT call __setitem__, updates storage directly)
                foreach (var key in mappingProxy.Keys)
                {
                    var pyKey = new PyString(key);
                    var value = mappingProxy.GetItem(pyKey);

                    #if DEBUG
                    // Debug: Track _generate_next_value_ updates
                    if (key == "_generate_next_value_")
                    {
                        Console.WriteLine($"[PyDict.Update DEBUG] Copying '{key}' from mappingproxy");
                        Console.WriteLine($"[PyDict.Update DEBUG]   value = {value}, type = {value.GetTypeName()}");
                    }
                    #endif

                    // Direct storage update (bypass __setitem__)
                    if (!_dict.ContainsKey(pyKey))
                    {
                        _keys.Add(pyKey);
                    }
                    _dict[pyKey] = value;
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
        public PyObject SetDefault(PyObject key, PyObject defaultValue = null)
        {
            if (_dict.TryGetValue(key, out PyObject value))
                return value;

            var newValue = defaultValue ?? PyNone.Instance;
            // 새 키이므로 _keys에 추가 (삽입 순서 보장)
            _keys.Add(key);
            _dict[key] = newValue;
            return newValue;
        }

        #endregion

        #region Views (Keys, Values, Items)

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
            return ListCache.Create(keysArray);
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
                values[i] = _dict[_keys[i]];
            }
            return ListCache.Create(values);
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
                items[i] = TupleCache.CreatePair(_keys[i], _dict[_keys[i]]);
            }
            return ListCache.Create(items);
        }

        #endregion

        #region Length and Type Checking

        public override int Length() => _dict.Count;
        public override bool PyBoolValue() => _dict.Count > 0;

        #endregion

        #region Dictionary Creation Methods

        /// <summary>
        /// 키 시퀀스와 값으로부터 딕셔너리 생성
        /// </summary>
        public static PyDict FromKeys(PyObject keys, PyObject value = null)
        {
            var dict = new PyDict();
            var defaultValue = value ?? PyNone.Instance;
            
            if (keys is PyList list)
            {
                foreach (var key in list.Items)
                {
                    dict.SetItem(key, defaultValue);
                }
            }
            else if (keys is PyTuple tuple)
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
                    dict.SetItem(new PyString(c.ToString()), defaultValue);
                }
            }
            else
            {
                throw PyTypeError.Create($"'{keys.GetTypeName()}' object is not iterable");
            }
            
            return dict;
        }

        #endregion

        #region Copy

        /// <summary>
        /// 딕셔너리의 얕은 복사본 생성
        /// Python 3.7+: 삽입 순서 보장
        /// </summary>
        public PyDict Copy()
        {
            var newDict = new PyDict();
            // 삽입 순서대로 복사
            foreach (var key in _keys)
            {
                newDict._keys.Add(key);
                newDict._dict[key] = _dict[key];
            }
            return newDict;
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

        /// <summary>
        /// CPython 호환: 딕셔너리 메서드들을 속성으로 접근
        /// CPython 3.12: GenericGetAttribute를 사용하여 descriptor protocol 따름
        /// </summary>
        protected override PyObject PyGetAttribute(string name)
        {
            // CPython 3.12: Use GenericGetAttribute to follow descriptor protocol
            return GenericGetAttribute(name);
        }

        #endregion

        #region Type Conversion (CPython Compatible)

        // === To* Methods: Value Extraction (PyDict → C# basic types) ===
        
        /// <summary>
        /// CPython PyLong_AsLong 호환: PyDict는 일반적으로 int로 변환될 수 없음
        /// </summary>
        public override int ToInt()
        {
            throw PyTypeError.Create($"int() argument must be a string, a bytes-like object or a number, not 'dict'");
        }
        
        /// <summary>
        /// CPython PyFloat_AsDouble 호환: PyDict는 일반적으로 float로 변환될 수 없음
        /// </summary>
        public override float ToFloat()
        {
            throw PyTypeError.Create($"float() argument must be a string or a number, not 'dict'");
        }

        public override double ToDouble()
        {
            throw PyTypeError.Create($"double() argument must be a string or a number, not 'dict'");
        }


        // === As* Methods: Type Conversion (PyDict → PyObject types) ===
        
        /// <summary>
        /// CPython 호환: PyDict를 PyDict로 변환 (복사본 생성)
        /// </summary>
        public override PyDict AsDict()
        {
            // CPython dict() 생성자 동작: 새로운 복사본 생성
            return Copy();
        }
        
        /// <summary>
        /// CPython 호환: PyDict를 PyList로 변환 (키 목록)
        /// </summary>
        public override PyList AsList()
        {
            // CPython list(dict) 동작: 딕셔너리의 키들을 리스트로 변환
            return Keys();
        }
        
        /// <summary>
        /// CPython 호환: PyDict를 PyTuple로 변환 (키 목록)
        /// Python 3.7+: 삽입 순서 보장
        /// </summary>
        public override PyTuple AsTuple()
        {
            // CPython tuple(dict) 동작: 딕셔너리의 키들을 튜플로 변환 (삽입 순서 보장)
            // Performance: Eliminated LINQ (.ToArray) - direct array copy + Cache
            var keysArray = new PyObject[_keys.Count];
            for (int i = 0; i < _keys.Count; i++)
            {
                keysArray[i] = _keys[i];
            }
            return TupleCache.GetOrCreate(keysArray);
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
        public override string AsString()
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
            // CPython: Objects/dictobject.c:3149-3152 - Type check
            if (other is not PyDict otherDict)
            {
                return PyNotImplemented.Instance;
            }

            // CPython: Objects/dictobject.c:3154-3159 - Create new dict and merge
            // result = PyDict_Copy(self)
            var result = new PyDict();

            // Copy self's items (maintain insertion order)
            foreach (var key in _keys)
            {
                result._dict[key] = _dict[key];
                result._keys.Add(key);
            }

            // CPython: Objects/dictobject.c:3164-3168 - Merge other's items (rightmost wins)
            // PyDict_Merge(result, other, 1)
            foreach (var key in otherDict._keys)
            {
                if (!result._dict.ContainsKey(key))
                {
                    result._keys.Add(key);
                }
                result._dict[key] = otherDict._dict[key];
            }

            return result;
        }

        /// <summary>
        /// Dict in-place merge operator (|=) - Python 3.9+
        /// CPython 3.12: Objects/dictobject.c:dict_ior (lines 3172-3194)
        /// Merges other into self in-place and returns self
        /// </summary>
        public override PyObject? InplaceBitwiseOr(PyObject other)
        {
            // CPython: Objects/dictobject.c:3177-3180 - Type check
            if (other is not PyDict otherDict)
            {
                return PyNotImplemented.Instance;
            }

            // CPython: Objects/dictobject.c:3182-3189 - Merge in place
            // PyDict_Merge(self, other, 1) - 1 means override existing keys
            foreach (var key in otherDict._keys)
            {
                if (!_dict.ContainsKey(key))
                {
                    _keys.Add(key);
                }
                _dict[key] = otherDict._dict[key];
            }

            // CPython: Objects/dictobject.c:3191 - Return self
            return this;
        }

        /// <summary>
        /// Iterator support for dictionary iteration (for key in dict) - CPython compatible
        /// Returns keys only, same as CPython behavior
        /// </summary>
        public override PyObject GetIterator()
        {
            return new PyDictKeyIterator(this);
        }

        #endregion
    }

    /// <summary>
    /// Special PyDict subclass that synchronizes with global scope variables
    /// CPython 3.12: globals() returns the actual module __dict__, not a copy
    /// When globals().update() is called, it modifies the module namespace
    /// </summary>
    public class PyGlobalsDict : PyDict
    {
        private readonly Dictionary<string, PyObject> _globalScopeVariables;

        public PyGlobalsDict(Dictionary<string, PyObject> globalScopeVariables)
            : base()
        {
            _globalScopeVariables = globalScopeVariables;

            // Initialize with current global variables
            foreach (var kv in globalScopeVariables)
            {
                base.SetItem(new PyString(kv.Key), kv.Value);
            }
        }

        public override void SetItem(PyObject key, PyObject value)
        {
            // CPython 3.12: Setting globals()[key] = value modifies the module namespace
            base.SetItem(key, value);

            // Sync back to global scope
            if (key is PyString strKey)
            {
                _globalScopeVariables[strKey.Value] = value;
            }
        }

        public override PyNone Update(PyObject other)
        {
            // Call base Update to modify this dict
            var result = base.Update(other);

            // Sync all changes back to global scope
            // Note: base.Update modifies _dict directly, so we need to sync everything
            foreach (var key in _keys)
            {
                if (key is PyString strKey)
                {
                    _globalScopeVariables[strKey.Value] = _dict[key];
                }
            }

            return result;
        }
    }
}