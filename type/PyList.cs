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

            // Check if it's a list subclass (PyClassInstance with list base)
            if (self is PyClassInstance instance)
            {
                // Use reflection to call GetListStorage
                var method = typeof(PyClassInstance).GetMethod("GetListStorage");
                if (method != null)
                {
                    var storage = method.Invoke(instance, null) as PyList;
                    if (storage != null)
                        return storage;
                }
            }

            throw PyTypeError.Create($"descriptor requires a 'list' object but received a '{self.GetTypeName()}'");
        }

        private static void InitializeListDescriptors()
        {
            var listType = PyType.ListType;

            // CPython 3.12: Objects/listobject.c:838-845 (list_append)
            // append method descriptor
            var appendDesc = new PyMethodDescriptor(
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
            appendDesc._fastCall1 = (self, arg) => { GetListStorage(self).Append(arg); return PyNone.Instance; };
            listType.TypeDict["append"] = appendDesc;

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
            var popDesc = new PyMethodDescriptor(
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
            popDesc._fastCall0 = self => GetListStorage(self).Pop(-1);
            listType.TypeDict["pop"] = popDesc;

            // clear method descriptor
            var clearDesc = new PyMethodDescriptor(
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
            clearDesc._fastCall0 = self => { GetListStorage(self).Clear(); return PyNone.Instance; };
            listType.TypeDict["clear"] = clearDesc;

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
                    if (args.Length != 1)
                        throw PyTypeError.Create($"index() takes exactly one argument ({args.Length} given)");
                    var list = GetListStorage(self);
                    var index = list.Index(args[0]);
                    return new PyInt(index);
                },
                minArgs: 1, maxArgs: 1
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

                    PyObject? key = null;
                    bool reverse = false;

                    if (kwargs != null)
                    {
                        var keyStr = new PyStr("key");
                        var reverseStr = new PyStr("reverse");

                        if (kwargs.Contains(keyStr).Value)
                            key = kwargs.GetItem(keyStr);

                        if (kwargs.Contains(reverseStr).Value)
                        {
                            var reverseObj = kwargs.GetItem(reverseStr);
                            reverse = reverseObj.PyBoolValue();
                        }
                    }

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
            listType.TypeDict["__init__"] = new PyMethodDescriptor(
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
                    return list.GetIterator();  // Use existing PyListIterator implementation
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
                    return new PyInt(list.Length());
                },
                minArgs: 0,
                maxArgs: 0
            );

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

        public override PyStr ToRepr()
        {
            if (_items.Count == 0) return StringCache.GetOrCreate("[]");

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
        public override int Length() => _items.Count;
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
                var (start, stop, step) = slice.Indices(_items.Count);

                var result = new List<PyObject>();
                if (step > 0)
                {
                    // After Indices() normalization, start/stop are guaranteed to be within bounds
                    for (int i = start; i < stop; i += step)
                    {
                        result.Add(_items[i]);
                    }
                }
                else if (step < 0)
                {
                    // After Indices() normalization, start/stop are guaranteed to be within bounds
                    for (int i = start; i > stop; i += step)
                    {
                        result.Add(_items[i]);
                    }
                }

                return new PyList(result);
            }
            else
            {
                // Try __index__ protocol for integer-like objects
                int idx = PyObject.GetIndex(index, "list");
                return GetItem(idx);
            }
        }

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
                    var iterator = value.GetIterator();
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

                var (start, stop, step) = slice.Indices(_items.Count);

                if (step == 1)
                {
                    // Simple slice assignment: replace items[start:stop] with valueList
                    _items.RemoveRange(start, stop - start);
                    _items.InsertRange(start, valueList.Items);
                }
                else
                {
                    // Extended slice assignment: must have same number of items
                    var sliceIndices = slice.GetIndices(_items.Count);
                    if (sliceIndices.Length != valueList.Items.Length)
                    {
                        throw PyValueError.Create($"attempt to assign sequence of size {valueList.Items.Length} to extended slice of size {sliceIndices.Length}");
                    }

                    for (int i = 0; i < sliceIndices.Length; i++)
                    {
                        _items[sliceIndices[i]] = valueList.Items[i];
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
            if (iterable is PyList other)
            {
                _items.AddRange(other._items);
            }
            else
            {
                // 이터러블 지원 (나중에 구현)
                throw PyTypeError.Create("'PyObject' object is not iterable");
            }
        }

        public int Index(PyObject item)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (AreEqual(_items[i], item))
                    return i;
            }
            throw PyValueError.Create($"{item.ToRepr()} is not in list");
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

        public void Sort(PyObject? key = null, bool reverse = false)
        {
            // CPython 3.12: list.sort(*, key=None, reverse=False)
            if (key == null || key == PyNone.Instance)
            {
                // No key function: direct comparison
                _items.Sort((a, b) =>
                {
                    try
                    {
                        var result = a.RichCompare(b, CompareOp.LT);
                        if (result is PyBool boolResult)
                        {
                            return boolResult.Value ? -1 :
                                   (a.RichCompare(b, CompareOp.GT) is PyBool gt && gt.Value ? 1 : 0);
                        }
                        return 0;
                    }
                    catch
                    {
                        return 0;
                    }
                });
            }
            else
            {
                // CPython 3.12: Apply key function to all elements
                var keysAndValues = new List<(PyObject key, PyObject value)>();

                for (int i = 0; i < _items.Count; i++)
                {
                    var keyResult = key.Call(new[] { _items[i] }, null);
                    keysAndValues.Add((keyResult, _items[i]));
                }

                // Sort by keys
                keysAndValues.Sort((a, b) =>
                {
                    try
                    {
                        var result = a.key.RichCompare(b.key, CompareOp.LT);
                        if (result is PyBool boolResult)
                        {
                            return boolResult.Value ? -1 :
                                   (a.key.RichCompare(b.key, CompareOp.GT) is PyBool gt && gt.Value ? 1 : 0);
                        }
                        return 0;
                    }
                    catch
                    {
                        return 0;
                    }
                });

                // Extract sorted values
                _items.Clear();
                foreach (var (_, value) in keysAndValues)
                {
                    _items.Add(value);
                }
            }

            // CPython 3.12: Apply reverse if requested
            if (reverse)
            {
                _items.Reverse();
            }
        }

        // 동등성 비교
        private static bool AreEqual(PyObject a, PyObject b)
        {
            if (a == b) return true;
            if (a == null || b == null) return false;

            try
            {
                var result = a.RichCompare(b, PyObject.CompareOp.EQ);
                return result is PyBool boolResult && boolResult.Value;
            }
            catch
            {
                return false;
            }
        }

        // CPython 3.12: Use descriptor protocol for attribute access
        public override PyObject GetAttribute(string name)
        {
            return GenericGetAttribute(name);
        }

        // 이터레이터 지원
        public override PyObject GetIterator()
        {
            return new PyListIterator(this);
        }
        
        #region Type Conversion (CPython Compatible)

        // === To* Methods: Value Extraction (PyList → C# basic types) ===
        
        /// <summary>
        /// CPython PyLong_AsLong 호환: PyList는 일반적으로 int로 변환될 수 없음
        /// </summary>
        public override int ToInt()
        {
            throw PyTypeError.Create($"int() argument must be a string, a bytes-like object or a number, not 'list'");
        }
        
        /// <summary>
        /// CPython PyFloat_AsDouble 호환: PyList는 일반적으로 float로 변환될 수 없음
        /// </summary>
        public override float ToFloat()
        {
            throw PyTypeError.Create($"float() argument must be a string or a number, not 'list'");
        }

        public override double ToDouble()
        {
            throw PyTypeError.Create($"double() argument must be a string or a number, not 'list'");
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
        /// CPython 호환: PyList를 PyStr으로 변환 (str() 호출과 동일)
        /// </summary>
        public override string AsString()
        {
            return ToRepr().Value; // CPython에서 str(list)는 repr(list)와 동일
        }

        #endregion
        
        /// <summary>
        /// CPython __contains__ 메소드 구현 - in 연산자 지원
        /// </summary>
        public override PyBool Contains(PyObject item)
        {
            foreach (var listItem in _items)
            {
                if (AreEqual(listItem, item))
                    return PyBool.True;
            }
            return PyBool.False;
        }

        /// <summary>
        /// PyList 동등성 비교 - CPython 호환
        /// </summary>
        protected override PyObject PyEquals(PyObject other)
        {
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
                    var eq = _items[i].RichCompare(otherList._items[i], CompareOp.EQ);
                    if (eq is PyBool e && !e.Value)
                        return PyBool.FromBool(op == CompareOp.NE);
                }

                return PyBool.FromBool(op == CompareOp.EQ);
            }

            // CPython: Objects/listobject.c:2730-2770 - Search for first index where items differ
            int minLen = Math.Min(_items.Count, otherList._items.Count);
            for (int i = 0; i < minLen; i++)
            {
                // First check if items are equal
                var eq = _items[i].RichCompare(otherList._items[i], CompareOp.EQ);
                if (eq is PyBool e && !e.Value)
                {
                    // Items differ - compare using the actual operator
                    return _items[i].RichCompare(otherList._items[i], op);
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