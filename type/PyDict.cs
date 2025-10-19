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

        // Python 3.7+: dict는 삽입 순서를 보장해야 함
        // C# Dictionary는 순서 보장 안 함 → List + Dictionary 혼합 사용
        protected readonly Dictionary<PyObject, PyObject> _dict;  // O(1) 조회용
        protected readonly List<PyObject> _keys;  // 삽입 순서 보관

        // 내부 딕셔너리 접근용 (타입 생성 등에서 사용)
        internal Dictionary<PyObject, PyObject> InternalDict => _dict;

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

        #endregion

        #region String Representation

        public override PyString ToStr() => ToRepr();

        public override PyString ToRepr()
        {
            if (_dict.Count == 0) return new PyString("{}");

            // Python 3.7+: 삽입 순서대로 출력
            var pairs = _keys.Select(k => $"{k.ToRepr().Value}: {_dict[k].ToRepr().Value}");
            return new PyString($"{{{string.Join(", ", pairs)}}}");
        }

        #endregion

        #region Hash and Equality

        protected override PyObject PyEquals(PyObject other)
        {
            return other switch
            {
                PyDict otherDict => PyBool.FromBool(_dict.Count == otherDict._dict.Count &&
                    _dict.All(kv => otherDict._dict.ContainsKey(kv.Key) &&
                        ((PyBool)otherDict._dict[kv.Key].RichCompare(kv.Value, CompareOp.EQ)).Value)),
                _ => PyBool.False
            };
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
        /// 키 삭제 del dict[key]
        /// Python 3.7+: 삽입 순서 유지 (_keys에서도 제거)
        /// </summary>
        public virtual PyObject DelItem(PyObject key)
        {
            if (!_dict.Remove(key))
                throw PyKeyError.Create(key.ToRepr());

            // _keys에서도 제거 (삽입 순서 유지)
            _keys.Remove(key);
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
        public PyObject Pop(PyObject key, PyObject defaultValue = null)
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
        public PyTuple PopItem()
        {
            if (_dict.Count == 0)
                throw PyKeyError.Create("popitem(): dictionary is empty");

            // Python 3.7+: LIFO (Last-In-First-Out) - 마지막 삽입된 키
            var lastKey = _keys[_keys.Count - 1];
            var value = _dict[lastKey];
            _dict.Remove(lastKey);
            _keys.RemoveAt(_keys.Count - 1);
            return new PyTuple(lastKey, value);
        }

        /// <summary>
        /// 모든 키-값 쌍 제거
        /// Python 3.7+: 삽입 순서 리스트도 함께 제거
        /// </summary>
        public PyNone Clear()
        {
            _dict.Clear();
            _keys.Clear();
            return PyNone.Instance;
        }

        /// <summary>
        /// 다른 딕셔너리나 매핑의 키-값으로 업데이트 (CPython dict.update 호환)
        /// Python 3.7+: 삽입 순서 유지
        /// </summary>
        public PyNone Update(PyObject other)
        {
            // CPython 3.12: dict.update() can accept dict, mappingproxy, or any mapping-like object
            if (other is PyDict otherDict)
            {
                // 삽입 순서 유지: otherDict의 키 순서대로 업데이트
                foreach (var key in otherDict._keys)
                {
                    var value = otherDict._dict[key];
                    // SetItem 사용하여 삽입 순서 보장
                    if (!_dict.ContainsKey(key))
                    {
                        _keys.Add(key);
                    }
                    _dict[key] = value;
                }
            }
            else if (other is PyMappingProxy mappingProxy)
            {
                // PyMappingProxy uses string keys, convert to PyString
                foreach (var key in mappingProxy.Keys)
                {
                    var pyKey = new PyString(key);
                    var value = mappingProxy.GetItem(pyKey);
                    // SetItem 사용하여 삽입 순서 보장
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
            return new PyList(_keys.ToArray());
        }

        /// <summary>
        /// 모든 값의 뷰 반환
        /// Python 3.7+: 삽입 순서 보장
        /// </summary>
        public PyList Values()
        {
            // _keys 순서대로 값을 가져옴
            var values = _keys.Select(k => _dict[k]).ToArray();
            return new PyList(values);
        }

        /// <summary>
        /// 모든 키-값 쌍의 뷰 반환
        /// Python 3.7+: 삽입 순서 보장
        /// </summary>
        public PyList Items()
        {
            // _keys 순서대로 키-값 쌍을 생성
            var items = _keys.Select(k => new PyTuple(k, _dict[k])).Cast<PyObject>().ToArray();
            return new PyList(items);
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
        public override double ToFloat()
        {
            throw PyTypeError.Create($"float() argument must be a string or a number, not 'dict'");
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
            return new PyTuple(_keys.ToArray());
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
        /// Iterator support for dictionary iteration (for key in dict) - CPython compatible
        /// Returns keys only, same as CPython behavior
        /// </summary>
        public override PyObject GetIterator()
        {
            return new PyDictKeyIterator(this);
        }

        #endregion
    }
}