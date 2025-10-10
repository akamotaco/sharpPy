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

        protected readonly Dictionary<PyObject, PyObject> _dict;

        // 내부 딕셔너리 접근용 (타입 생성 등에서 사용)
        internal Dictionary<PyObject, PyObject> InternalDict => _dict;
        
        public PyDict() => _dict = new Dictionary<PyObject, PyObject>(new PyObjectEqualityComparer());

        public PyDict(Dictionary<string, PyObject> items)
        {
            _dict = new Dictionary<PyObject, PyObject>(new PyObjectEqualityComparer());
            foreach (var kv in items)
            {
                _dict[new PyString(kv.Key)] = kv.Value;
            }
        }

        public PyDict(Dictionary<PyObject, PyObject> items) =>
            _dict = new Dictionary<PyObject, PyObject>(items, new PyObjectEqualityComparer());

        public override PyType GetPyType() => PyType.DictType;
        public override string GetTypeName() => "dict";

        #endregion

        #region String Representation

        public override PyString ToStr() => ToRepr();

        public override PyString ToRepr()
        {
            if (_dict.Count == 0) return new PyString("{}");

            var pairs = _dict.Select(kv => $"{kv.Key.ToRepr().Value}: {kv.Value.ToRepr().Value}");
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
        /// </summary>
        public override void SetItem(PyObject key, PyObject value)
        {
            _dict[key] = value;
        }

        /// <summary>
        /// 키 삭제 del dict[key]
        /// </summary>
        public virtual PyObject DelItem(PyObject key)
        {
            if (!_dict.Remove(key))
                throw PyKeyError.Create(key.ToRepr());
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
        /// </summary>
        public PyObject Pop(PyObject key, PyObject defaultValue = null)
        {
            if (_dict.TryGetValue(key, out PyObject value))
            {
                _dict.Remove(key);
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
            if (_dict.Count == 0)
                throw PyKeyError.Create("popitem(): dictionary is empty");

            var first = _dict.First();
            _dict.Remove(first.Key);
            return new PyTuple(first.Key, first.Value);
        }

        /// <summary>
        /// 모든 키-값 쌍 제거
        /// </summary>
        public PyNone Clear()
        {
            _dict.Clear();
            return PyNone.Instance;
        }

        /// <summary>
        /// 다른 딕셔너리의 키-값으로 업데이트
        /// </summary>
        public PyNone Update(PyDict other)
        {
            foreach (var kv in other._dict)
            {
                _dict[kv.Key] = kv.Value;
            }
            return PyNone.Instance;
        }

        /// <summary>
        /// 키가 없으면 기본값 설정하고 반환
        /// </summary>
        public PyObject SetDefault(PyObject key, PyObject defaultValue = null)
        {
            if (_dict.TryGetValue(key, out PyObject value))
                return value;

            var newValue = defaultValue ?? PyNone.Instance;
            _dict[key] = newValue;
            return newValue;
        }

        #endregion

        #region Views (Keys, Values, Items)

        /// <summary>
        /// 모든 키의 뷰 반환
        /// </summary>
        public PyList Keys()
        {
            return new PyList(_dict.Keys.ToArray());
        }

        /// <summary>
        /// 모든 값의 뷰 반환
        /// </summary>
        public PyList Values()
        {
            return new PyList(_dict.Values.ToArray());
        }

        /// <summary>
        /// 모든 키-값 쌍의 뷰 반환
        /// </summary>
        public PyList Items()
        {
            var items = _dict.Select(kv => new PyTuple(kv.Key, kv.Value)).Cast<PyObject>().ToArray();
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
        /// </summary>
        public PyDict Copy()
        {
            return new PyDict(new Dictionary<PyObject, PyObject>(_dict));
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
        /// </summary>
        protected override PyObject PyGetAttribute(string name)
        {
            switch (name)
            {
                case "keys":
                    return new PyFunction("keys", args =>
                    {
                        if (args.Length != 0)
                            throw PyTypeError.Create($"keys() takes no arguments ({args.Length} given)");
                        return Keys();
                    });

                case "values":
                    return new PyFunction("values", args =>
                    {
                        if (args.Length != 0)
                            throw PyTypeError.Create($"values() takes no arguments ({args.Length} given)");
                        return Values();
                    });

                case "items":
                    return new PyFunction("items", args =>
                    {
                        if (args.Length != 0)
                            throw PyTypeError.Create($"items() takes no arguments ({args.Length} given)");
                        return Items();
                    });

                case "get":
                    return new PyFunction("get", args =>
                    {
                        if (args.Length < 1 || args.Length > 2)
                            throw PyTypeError.Create($"get() takes from 1 to 2 positional arguments but {args.Length} were given");
                        var key = args[0];
                        var defaultValue = args.Length > 1 ? args[1] : PyNone.Instance;
                        return Get(key, defaultValue);
                    });

                case "pop":
                    return new PyFunction("pop", args =>
                    {
                        if (args.Length < 1 || args.Length > 2)
                            throw PyTypeError.Create($"pop() takes from 1 to 2 positional arguments but {args.Length} were given");
                        var key = args[0];
                        var defaultValue = args.Length > 1 ? args[1] : null;
                        return Pop(key, defaultValue);
                    });

                case "clear":
                    return new PyFunction("clear", args =>
                    {
                        if (args.Length != 0)
                            throw PyTypeError.Create($"clear() takes no arguments ({args.Length} given)");
                        Clear();
                        return PyNone.Instance;
                    });

                case "copy":
                    return new PyFunction("copy", args =>
                    {
                        if (args.Length != 0)
                            throw PyTypeError.Create($"copy() takes no arguments ({args.Length} given)");
                        return Copy();
                    });

                case "update":
                    return new PyFunction("update", args =>
                    {
                        if (args.Length != 1)
                            throw PyTypeError.Create($"update() takes exactly one argument ({args.Length} given)");
                        if (args[0] is PyDict otherDict)
                        {
                            Update(otherDict);
                        }
                        else
                        {
                            throw PyTypeError.Create($"'update() argument must be dict, not '{args[0].GetTypeName()}'");
                        }
                        return PyNone.Instance;
                    });

                default:
                    // 기본 속성 접근은 부모 클래스에 위임
                    return base.PyGetAttribute(name);
            }
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
        /// </summary>
        public override PyTuple AsTuple()
        {
            // CPython tuple(dict) 동작: 딕셔너리의 키들을 튜플로 변환
            return new PyTuple(_dict.Keys.ToArray());
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