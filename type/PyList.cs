using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    public class PyList : PyObject
    {
        static PyList()
        {
            InitializeListDescriptors();
        }

        private static void InitializeListDescriptors()
        {
            var listType = PyType.ListType;

            // append method descriptor
            listType.TypeDict["append"] = new PyMethodDescriptor(
                "append", listType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"append() takes exactly one argument ({args.Length} given)");
                    if (self is not PyList list)
                        throw PyTypeError.Create($"descriptor 'append' requires a 'list' object but received a '{self.GetTypeName()}'");
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
                    if (self is not PyList list)
                        throw PyTypeError.Create($"descriptor 'insert' requires a 'list' object but received a '{self.GetTypeName()}'");
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
                    if (self is not PyList list)
                        throw PyTypeError.Create($"descriptor 'remove' requires a 'list' object but received a '{self.GetTypeName()}'");
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
                    if (self is not PyList list)
                        throw PyTypeError.Create($"descriptor 'pop' requires a 'list' object but received a '{self.GetTypeName()}'");
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
                    if (self is not PyList list)
                        throw PyTypeError.Create($"descriptor 'clear' requires a 'list' object but received a '{self.GetTypeName()}'");
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
                    if (self is not PyList list)
                        throw PyTypeError.Create($"descriptor 'extend' requires a 'list' object but received a '{self.GetTypeName()}'");
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
                    if (self is not PyList list)
                        throw PyTypeError.Create($"descriptor 'index' requires a 'list' object but received a '{self.GetTypeName()}'");
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
                    if (self is not PyList list)
                        throw PyTypeError.Create($"descriptor 'count' requires a 'list' object but received a '{self.GetTypeName()}'");
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
                    if (self is not PyList list)
                        throw PyTypeError.Create($"descriptor 'reverse' requires a 'list' object but received a '{self.GetTypeName()}'");
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
                    if (self is not PyList list)
                        throw PyTypeError.Create($"descriptor 'sort' requires a 'list' object but received a '{self.GetTypeName()}'");

                    PyObject? key = null;
                    bool reverse = false;

                    if (kwargs != null)
                    {
                        var keyStr = new PyString("key");
                        var reverseStr = new PyString("reverse");

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
                    if (self is not PyList list)
                        throw PyTypeError.Create($"descriptor 'copy' requires a 'list' object but received a '{self.GetTypeName()}'");
                    return new PyList(list._items.ToArray());
                },
                minArgs: 0, maxArgs: 0
            );
        }

        private List<PyObject> _items;
        public PyObject[] Items => _items.ToArray();

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
        public override string ToString() => $"[{string.Join(", ", _items.Select(i => i.ToRepr().Value))}]";
        public override PyString ToRepr() => new PyString($"[{string.Join(", ", _items.Select(i => i.ToRepr().Value))}]");
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
        public override PyObject GetItem(PyObject index)
        {
            if (index is PyInt pyInt)
            {
                return GetItem((int)pyInt.Value);
            }
            else if (index is PySlice slice)
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
                throw PyTypeError.Create($"list indices must be integers or slices, not {index.GetTypeName()}");
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
                if (!(value is PyList valueList))
                {
                    throw PyTypeError.Create("can only assign a list to a slice");
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
        public override double ToFloat()
        {
            throw PyTypeError.Create($"float() argument must be a string or a number, not 'list'");
        }
        
        
        // === As* Methods: Type Conversion (PyList → PyObject types) ===
        
        /// <summary>
        /// CPython 호환: PyList를 PyList로 변환 (복사본 생성)
        /// </summary>
        public override PyList AsList()
        {
            // CPython list() 생성자 동작: 새로운 복사본 생성
            return new PyList(_items.ToArray());
        }
        
        /// <summary>
        /// CPython 호환: PyList를 PyTuple로 변환
        /// </summary>
        public override PyTuple AsTuple()
        {
            // CPython tuple() 생성자 동작: 리스트 요소들로 튜플 생성
            return new PyTuple(_items.ToArray());
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
    }
}