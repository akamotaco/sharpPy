using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    public class PyList : PyObject
    {
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
        public override string ToString() => $"[{string.Join(", ", _items.Select(i => i.ToString()))}]";
        public override string ToRepr() => $"[{string.Join(", ", _items.Select(i => i.ToRepr()))}]";
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

        public void SetItem(int index, PyObject value)
        {
            if (index < 0) index += _items.Count;
            if (index < 0 || index >= _items.Count)
                throw PyIndexError.Create("list assignment index out of range");
            _items[index] = value;
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

        public void Sort()
        {
            // 간단한 정렬 구현 (나중에 개선)
            _items.Sort((a, b) => string.Compare(a.ToString(), b.ToString()));
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

        // 이터레이터 지원
        public override PyObject GetIterator()
        {
            return new PyListIterator(this);
        }
    }
}