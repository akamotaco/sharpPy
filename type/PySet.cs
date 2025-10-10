using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    /// <summary>
    /// Python set 타입 구현 - 중복 없는 변경 가능한 컬렉션
    /// </summary>
    public class PySet : PyObject
    {
        #region Core Properties

        // PyObject를 요소로 사용하기 위한 HashSet with custom comparer
        private readonly HashSet<PyObject> _items;

        public PySet() => _items = new HashSet<PyObject>(new PyObjectEqualityComparer());
        
        public PySet(IEnumerable<PyObject> items) => 
            _items = new HashSet<PyObject>(items, new PyObjectEqualityComparer());

        public override PyType GetPyType() => PyType.SetType;
        public override string GetTypeName() => "set";

        // 내부 HashSet 접근 (읽기 전용)
        public IReadOnlyCollection<PyObject> Items => _items;

        #endregion

        #region String Representation

        public override PyString ToStr() => ToRepr();

        public override PyString ToRepr()
        {
            if (_items.Count == 0) return new PyString("set()");
            return new PyString($"{{{string.Join(", ", _items.Select(item => item.ToRepr().Value))}}}");
        }

        #endregion

        #region Hash and Equality

        protected override PyObject PyEquals(PyObject other)
        {
            return other switch
            {
                PySet otherSet => PyBool.FromBool(_items.SetEquals(otherSet._items)),
                PyFrozenSet frozenSet => PyBool.FromBool(_items.SetEquals(frozenSet.Items)),
                _ => PyBool.False
            };
        }

        #endregion

        #region Set Operations

        /// <summary>
        /// 요소 추가 set.add(elem)
        /// </summary>
        public PyNone Add(PyObject item)
        {
            _items.Add(item);
            return PyNone.Instance;
        }

        /// <summary>
        /// 요소 제거 set.remove(elem) - 없으면 KeyError
        /// </summary>
        public PyNone Remove(PyObject item)
        {
            if (!_items.Remove(item))
                throw PyKeyError.Create(item.ToRepr());
            return PyNone.Instance;
        }

        /// <summary>
        /// 요소 제거 set.discard(elem) - 없어도 에러 없음
        /// </summary>
        public PyNone Discard(PyObject item)
        {
            _items.Remove(item);
            return PyNone.Instance;
        }

        /// <summary>
        /// 임의 요소 제거하고 반환 set.pop()
        /// </summary>
        public PyObject Pop()
        {
            if (_items.Count == 0)
                throw PyKeyError.Create("pop from empty set");
            
            var item = _items.First();
            _items.Remove(item);
            return item;
        }

        /// <summary>
        /// 모든 요소 제거 set.clear()
        /// </summary>
        public PyNone Clear()
        {
            _items.Clear();
            return PyNone.Instance;
        }

        /// <summary>
        /// 요소 포함 여부 확인 (in 연산자)
        /// </summary>
        public override PyBool Contains(PyObject item)
        {
            return PyBool.FromBool(_items.Contains(item));
        }

        /// <summary>
        /// 다른 집합으로 업데이트 set.update(other)
        /// </summary>
        public PyNone Update(PyObject other)
        {
            var items = GetIterableItems(other);
            foreach (var item in items)
            {
                _items.Add(item);
            }
            return PyNone.Instance;
        }

        #endregion

        #region Set Mathematical Operations

        /// <summary>
        /// 합집합 set | other 또는 set.union(other)
        /// </summary>
        public PySet Union(PyObject other)
        {
            var result = new PySet(_items);
            var otherItems = GetSetItems(other);
            foreach (var item in otherItems)
            {
                result._items.Add(item);
            }
            return result;
        }

        /// <summary>
        /// 교집합 set & other 또는 set.intersection(other)
        /// </summary>
        public PySet Intersection(PyObject other)
        {
            var otherItems = GetSetItems(other);
            var result = new PySet(_items.Where(item => otherItems.Contains(item)));
            return result;
        }

        /// <summary>
        /// 차집합 set - other 또는 set.difference(other)
        /// </summary>
        public PySet Difference(PyObject other)
        {
            var otherItems = GetSetItems(other);
            var result = new PySet(_items.Where(item => !otherItems.Contains(item)));
            return result;
        }

        /// <summary>
        /// 대칭차집합 set ^ other 또는 set.symmetric_difference(other)
        /// </summary>
        public PySet SymmetricDifference(PyObject other)
        {
            var otherItems = GetSetItems(other);
            var result = new PySet();
            
            // this - other
            foreach (var item in _items.Where(item => !otherItems.Contains(item)))
            {
                result._items.Add(item);
            }
            
            // other - this
            foreach (var item in otherItems.Where(item => !_items.Contains(item)))
            {
                result._items.Add(item);
            }
            
            return result;
        }

        #endregion

        #region Set Arithmetic Operations (Python operators)

        /// <summary>
        /// 합집합 연산 (| 연산자)
        /// </summary>
        public override PyObject BitwiseOr(PyObject other)
        {
            return Union(other);
        }

        /// <summary>
        /// 교집합 연산 (& 연산자)
        /// </summary>
        public override PyObject BitwiseAnd(PyObject other)
        {
            return Intersection(other);
        }

        /// <summary>
        /// 차집합 연산 (- 연산자)
        /// </summary>
        public override PyObject Subtract(PyObject other)
        {
            return Difference(other);
        }

        /// <summary>
        /// 대칭 차집합 연산 (^ 연산자)
        /// </summary>
        public override PyObject BitwiseXor(PyObject other)
        {
            return SymmetricDifference(other);
        }

        #endregion

        #region Set Comparison Operations

        /// <summary>
        /// 부분집합 확인 set.issubset(other) 또는 set <= other
        /// </summary>
        public PyBool IsSubset(PyObject other)
        {
            var otherItems = GetSetItems(other);
            return PyBool.FromBool(_items.IsSubsetOf(otherItems));
        }

        /// <summary>
        /// 진부분집합 확인 set < other
        /// </summary>
        public PyBool IsProperSubset(PyObject other)
        {
            var otherItems = GetSetItems(other);
            return PyBool.FromBool(_items.IsProperSubsetOf(otherItems));
        }

        /// <summary>
        /// 상위집합 확인 set.issuperset(other) 또는 set >= other
        /// </summary>
        public PyBool IsSuperset(PyObject other)
        {
            var otherItems = GetSetItems(other);
            return PyBool.FromBool(_items.IsSupersetOf(otherItems));
        }

        /// <summary>
        /// 진상위집합 확인 set > other
        /// </summary>
        public PyBool IsProperSuperset(PyObject other)
        {
            var otherItems = GetSetItems(other);
            return PyBool.FromBool(_items.IsProperSupersetOf(otherItems));
        }

        /// <summary>
        /// 서로소 확인 set.isdisjoint(other)
        /// </summary>
        public PyBool IsDisjoint(PyObject other)
        {
            var otherItems = GetSetItems(other);
            return PyBool.FromBool(!_items.Overlaps(otherItems));
        }

        #endregion

        #region Set Mutating Operations

        /// <summary>
        /// 교집합으로 업데이트 set &= other
        /// </summary>
        public PyNone IntersectionUpdate(PyObject other)
        {
            var otherItems = GetSetItems(other);
            _items.IntersectWith(otherItems);
            return PyNone.Instance;
        }

        /// <summary>
        /// 차집합으로 업데이트 set -= other
        /// </summary>
        public PyNone DifferenceUpdate(PyObject other)
        {
            var otherItems = GetSetItems(other);
            _items.ExceptWith(otherItems);
            return PyNone.Instance;
        }

        /// <summary>
        /// 대칭차집합으로 업데이트 set ^= other
        /// </summary>
        public PyNone SymmetricDifferenceUpdate(PyObject other)
        {
            var otherItems = GetSetItems(other);
            _items.SymmetricExceptWith(otherItems);
            return PyNone.Instance;
        }

        #endregion

        #region Length and Type Checking

        public override int Length() => _items.Count;
        public override bool PyBoolValue() => _items.Count > 0;

        #endregion

        #region Helper Methods

        private HashSet<PyObject> GetSetItems(PyObject obj)
        {
            return obj switch
            {
                PySet set => new HashSet<PyObject>(set._items, new PyObjectEqualityComparer()),
                PyFrozenSet frozenSet => new HashSet<PyObject>(frozenSet.Items, new PyObjectEqualityComparer()),
                _ => new HashSet<PyObject>(GetIterableItems(obj), new PyObjectEqualityComparer())
            };
        }

        private IEnumerable<PyObject> GetIterableItems(PyObject obj)
        {
            return obj switch
            {
                PyList list => list.Items,
                PyTuple tuple => tuple.Items,
                PyString str => str.Value.Select(c => new PyString(c.ToString())),
                PySet set => set._items,
                PyFrozenSet frozenSet => frozenSet.Items,
                _ => throw PyTypeError.Create($"'{obj.GetTypeName()}' object is not iterable")
            };
        }

        #endregion

        #region Copy

        /// <summary>
        /// 집합의 얕은 복사본 생성
        /// </summary>
        public PySet Copy()
        {
            return new PySet(_items);
        }

        #endregion

        #region Iterator Protocol

        /// <summary>
        /// Iterator protocol 구현 - Python __iter__ 메서드
        /// </summary>
        public override PyIterator GetIterator()
        {
            return new PySetIterator(this);
        }

        #endregion

        #region Static Factory Methods

        public static PySet Empty => new PySet();

        #endregion

        #region Evaluate Method (NotImplementedException)

        public PyObject Evaluate(PyScope scope)
        {
            // Set literals evaluate to themselves (CPython style)
            return this;
        }

        #endregion
    }

    /// <summary>
    /// Python frozenset 타입 구현 - 불변 집합
    /// </summary>
    public class PyFrozenSet : PyObject
    {
        #region Core Properties

        private readonly HashSet<PyObject> _items;

        public PyFrozenSet() => _items = new HashSet<PyObject>(new PyObjectEqualityComparer());
        
        public PyFrozenSet(IEnumerable<PyObject> items) => 
            _items = new HashSet<PyObject>(items, new PyObjectEqualityComparer());

        public override PyType GetPyType() => PyType.FrozenSetType;
        public override string GetTypeName() => "frozenset";

        public IReadOnlyCollection<PyObject> Items => _items;

        #endregion

        #region String Representation

        public override PyString ToStr() => ToRepr();

        public override PyString ToRepr()
        {
            if (_items.Count == 0) return new PyString("frozenset()");
            return new PyString($"frozenset({{{string.Join(", ", _items.Select(item => item.ToRepr().Value))}}})");
        }

        #endregion

        #region Hash and Equality

        public override int ToHash()
        {
            // frozenset은 해시 가능 (불변이므로)
            int hash = 0;
            foreach (var item in _items.OrderBy(x => x.ToHash()))
            {
                hash ^= item.ToHash();
            }
            return hash;
        }

        protected override PyObject PyEquals(PyObject other)
        {
            return other switch
            {
                PyFrozenSet otherFrozen => PyBool.FromBool(_items.SetEquals(otherFrozen._items)),
                PySet set => PyBool.FromBool(_items.SetEquals(set.Items)),
                _ => PyBool.False
            };
        }

        #endregion

        #region Set Operations (Read-only versions)

        public PyBool Contains(PyObject item) => PyBool.FromBool(_items.Contains(item));
        
        public PyFrozenSet Union(PyObject other) => new PyFrozenSet(new PySet(_items).Union(other).Items);
        public PyFrozenSet Intersection(PyObject other) => new PyFrozenSet(new PySet(_items).Intersection(other).Items);
        public PyFrozenSet Difference(PyObject other) => new PyFrozenSet(new PySet(_items).Difference(other).Items);
        public PyFrozenSet SymmetricDifference(PyObject other) => new PyFrozenSet(new PySet(_items).SymmetricDifference(other).Items);
        
        public PyBool IsSubset(PyObject other) => new PySet(_items).IsSubset(other);
        public PyBool IsSuperset(PyObject other) => new PySet(_items).IsSuperset(other);
        public PyBool IsDisjoint(PyObject other) => new PySet(_items).IsDisjoint(other);

        #endregion

        #region Length and Type Checking

        public override int Length() => _items.Count;
        public override bool PyBoolValue() => _items.Count > 0;

        #endregion

        #region Copy

        public PyFrozenSet Copy() => new PyFrozenSet(_items);

        #endregion

        #region Evaluate Method (NotImplementedException)

        public PyObject Evaluate(PyScope scope)
        {
            throw new NotImplementedException("PyFrozenSet.Evaluate() - 나중에 구현예정");
        }

        #endregion
    }

    #region PyObjectEqualityComparer (if not already defined)
    
    /// <summary>
    /// PyObject를 키로 사용하기 위한 사용자 정의 비교기
    /// </summary>
    internal class PyObjectEqualityComparer : IEqualityComparer<PyObject>
    {
        public bool Equals(PyObject x, PyObject y)
        {
            if (x == null && y == null) return true;
            if (x == null || y == null) return false;
            return ((PyBool)x.RichCompare(y, PyObject.CompareOp.EQ)).Value;
        }

        public int GetHashCode(PyObject obj)
        {
            return obj?.ToHash() ?? 0;
        }
    }
    
    #endregion
}