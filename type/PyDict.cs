using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    /// <summary>
    /// Python dict 타입 구현 - 해시 가능한 키와 임의 값의 매핑
    /// </summary>
    public class PyDict : PyObject
    {
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

        private readonly Dictionary<PyObject, PyObject> _items;
        
        // 내부 딕셔너리 접근용 (타입 생성 등에서 사용)
        internal Dictionary<PyObject, PyObject> InternalDict => _items;
        
        public PyDict() => _items = new Dictionary<PyObject, PyObject>(new PyObjectEqualityComparer());
        
        public PyDict(Dictionary<string, PyObject> items) 
        {
            _items = new Dictionary<PyObject, PyObject>(new PyObjectEqualityComparer());
            foreach (var kv in items)
            {
                _items[new PyString(kv.Key)] = kv.Value;
            }
        }

        public PyDict(Dictionary<PyObject, PyObject> items) => 
            _items = new Dictionary<PyObject, PyObject>(items, new PyObjectEqualityComparer());

        public override PyType GetPyType() => PyType.DictType;
        public override string GetTypeName() => "dict";

        #endregion

        #region String Representation

        public override string ToStr() => ToRepr();
        
        public override string ToRepr()
        {
            if (_items.Count == 0) return "{}";
            
            var pairs = _items.Select(kv => $"{kv.Key.ToRepr()}: {kv.Value.ToRepr()}");
            return $"{{{string.Join(", ", pairs)}}}";
        }

        #endregion

        #region Hash and Equality

        protected override PyObject PyEquals(PyObject other)
        {
            return other switch
            {
                PyDict otherDict => PyBool.FromBool(_items.Count == otherDict._items.Count &&
                    _items.All(kv => otherDict._items.ContainsKey(kv.Key) && 
                        ((PyBool)otherDict._items[kv.Key].RichCompare(kv.Value, CompareOp.EQ)).Value)),
                _ => PyBool.False
            };
        }

        #endregion

        #region Dictionary Operations

        /// <summary>
        /// 키로 값 접근 dict[key]
        /// </summary>
        public PyObject GetItem(PyObject key)
        {
            if (_items.TryGetValue(key, out PyObject value))
                return value;
            
            throw PyKeyError.Create(key.ToRepr());
        }

        /// <summary>
        /// 키에 값 설정 dict[key] = value
        /// </summary>
        public void SetItem(PyObject key, PyObject value)
        {
            _items[key] = value;
        }

        /// <summary>
        /// 키 삭제 del dict[key]
        /// </summary>
        public void DelItem(PyObject key)
        {
            if (!_items.Remove(key))
                throw PyKeyError.Create(key.ToRepr());
        }

        /// <summary>
        /// 키 포함 여부 확인 (in 연산자)
        /// </summary>
        public PyBool Contains(PyObject key)
        {
            return PyBool.FromBool(_items.ContainsKey(key));
        }

        /// <summary>
        /// 기본값과 함께 값 가져오기
        /// </summary>
        public PyObject Get(PyObject key, PyObject defaultValue = null)
        {
            return _items.TryGetValue(key, out PyObject value) 
                ? value 
                : defaultValue ?? PyNone.Instance;
        }

        /// <summary>
        /// 키가 있으면 값 반환하고 삭제, 없으면 기본값 반환
        /// </summary>
        public PyObject Pop(PyObject key, PyObject defaultValue = null)
        {
            if (_items.TryGetValue(key, out PyObject value))
            {
                _items.Remove(key);
                return value;
            }
            
            if (defaultValue != null)
                return defaultValue;
                
            throw PyKeyError.Create(key.ToRepr());
        }

        /// <summary>
        /// 임의의 키-값 쌍을 제거하고 반환
        /// </summary>
        public PyTuple PopItem()
        {
            if (_items.Count == 0)
                throw PyKeyError.Create("popitem(): dictionary is empty");
            
            var first = _items.First();
            _items.Remove(first.Key);
            return new PyTuple(first.Key, first.Value);
        }

        /// <summary>
        /// 모든 키-값 쌍 제거
        /// </summary>
        public PyNone Clear()
        {
            _items.Clear();
            return PyNone.Instance;
        }

        /// <summary>
        /// 다른 딕셔너리의 키-값으로 업데이트
        /// </summary>
        public PyNone Update(PyDict other)
        {
            foreach (var kv in other._items)
            {
                _items[kv.Key] = kv.Value;
            }
            return PyNone.Instance;
        }

        /// <summary>
        /// 키가 없으면 기본값 설정하고 반환
        /// </summary>
        public PyObject SetDefault(PyObject key, PyObject defaultValue = null)
        {
            if (_items.TryGetValue(key, out PyObject value))
                return value;
            
            var newValue = defaultValue ?? PyNone.Instance;
            _items[key] = newValue;
            return newValue;
        }

        #endregion

        #region Views (Keys, Values, Items)

        /// <summary>
        /// 모든 키의 뷰 반환
        /// </summary>
        public PyList Keys()
        {
            return new PyList(_items.Keys.ToArray());
        }

        /// <summary>
        /// 모든 값의 뷰 반환
        /// </summary>
        public PyList Values()
        {
            return new PyList(_items.Values.ToArray());
        }

        /// <summary>
        /// 모든 키-값 쌍의 뷰 반환
        /// </summary>
        public PyList Items()
        {
            var items = _items.Select(kv => new PyTuple(kv.Key, kv.Value)).Cast<PyObject>().ToArray();
            return new PyList(items);
        }

        #endregion

        #region Length and Type Checking

        public override int Length() => _items.Count;
        public override bool PyBoolValue() => _items.Count > 0;

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
        /// </summary>
        public PyDict Copy()
        {
            return new PyDict(new Dictionary<PyObject, PyObject>(_items));
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
    }
}