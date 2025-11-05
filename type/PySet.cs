using System;
using System.Collections.Generic;

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

        #region Python Method Access

        /// <summary>
        /// CPython 3.12: Use descriptor protocol for attribute access
        /// </summary>
        public override PyObject GetAttribute(string name)
        {
            // CPython 3.12: Use GenericGetAttribute to follow descriptor protocol
            // This will find methods from PyType.SetType.Descriptors
            return GenericGetAttribute(name);
        }

        #endregion

        #region OLD_HARDCODED_METHODS_REMOVED
        /* 아래 하드코딩된 메서드들은 모두 PyType.InitializeSetTypeDescriptors()로 이동됨
        switch (name)
        {
            case "add":
                    {
                        var self = this;
                        return new PyBuiltinFunction("remove", (args, kwargs) =>
                        {
                            if (args.Length != 2)
                                throw PyTypeError.Create($"remove() takes exactly one argument ({args.Length - 1} given)");
                            return self.Remove(args[1]);
                        });
                    }
                case "discard":
                    {
                        var self = this;
                        return new PyBuiltinFunction("discard", (args, kwargs) =>
                        {
                            if (args.Length != 2)
                                throw PyTypeError.Create($"discard() takes exactly one argument ({args.Length - 1} given)");
                            return self.Discard(args[1]);
                        });
                    }
                case "pop":
                    {
                        var self = this;
                        return new PyBuiltinFunction("pop", (args, kwargs) =>
                        {
                            // args[0] = self (from VM), no other arguments
                            if (args.Length != 1)
                                throw PyTypeError.Create($"pop() takes no arguments ({args.Length - 1} given)");
                            return self.Pop();
                        });
                    }
                case "clear":
                    {
                        var self = this;
                        return new PyBuiltinFunction("clear", (args, kwargs) =>
                        {
                            if (args.Length != 1)
                                throw PyTypeError.Create($"clear() takes no arguments ({args.Length - 1} given)");
                            return self.Clear();
                        });
                    }
                case "update":
                    {
                        var self = this;
                        return new PyBuiltinFunction("update", (args, kwargs) =>
                        {
                            if (args.Length != 2)
                                throw PyTypeError.Create($"update() takes exactly one argument ({args.Length - 1} given)");
                            return self.Update(args[1]);
                        });
                    }
                case "union":
                    {
                        var self = this;
                        return new PyBuiltinFunction("union", (args, kwargs) =>
                        {
                            if (args.Length != 2)
                                throw PyTypeError.Create($"union() takes exactly one argument ({args.Length - 1} given)");
                            return self.Union(args[1]);
                        });
                    }
                case "intersection":
                    {
                        var self = this;
                        return new PyBuiltinFunction("intersection", (args, kwargs) =>
                        {
                            if (args.Length != 2)
                                throw PyTypeError.Create($"intersection() takes exactly one argument ({args.Length - 1} given)");
                            return self.Intersection(args[1]);
                        });
                    }
                case "difference":
                    {
                        var self = this;
                        return new PyBuiltinFunction("difference", (args, kwargs) =>
                        {
                            if (args.Length != 2)
                                throw PyTypeError.Create($"difference() takes exactly one argument ({args.Length - 1} given)");
                            return self.Difference(args[1]);
                        });
                    }
                case "symmetric_difference":
                    {
                        var self = this;
                        return new PyBuiltinFunction("symmetric_difference", (args, kwargs) =>
                        {
                            if (args.Length != 2)
                                throw PyTypeError.Create($"symmetric_difference() takes exactly one argument ({args.Length - 1} given)");
                            return self.SymmetricDifference(args[1]);
                        });
                    }
                case "issubset":
                    {
                        var self = this;
                        return new PyBuiltinFunction("issubset", (args, kwargs) =>
                        {
                            if (args.Length != 2)
                                throw PyTypeError.Create($"issubset() takes exactly one argument ({args.Length - 1} given)");
                            return self.IsSubset(args[1]);
                        });
                    }
                case "issuperset":
                    {
                        var self = this;
                        return new PyBuiltinFunction("issuperset", (args, kwargs) =>
                        {
                            if (args.Length != 2)
                                throw PyTypeError.Create($"issuperset() takes exactly one argument ({args.Length - 1} given)");
                            return self.IsSuperset(args[1]);
                        });
                    }
                case "isdisjoint":
                    {
                        var self = this;
                        return new PyBuiltinFunction("isdisjoint", (args, kwargs) =>
                        {
                            if (args.Length != 2)
                                throw PyTypeError.Create($"isdisjoint() takes exactly one argument ({args.Length - 1} given)");
                            return self.IsDisjoint(args[1]);
                        });
                    }
                case "copy":
                    {
                        var self = this;
                        return new PyBuiltinFunction("copy", (args, kwargs) =>
                        {
                            if (args.Length != 1)
                                throw PyTypeError.Create($"copy() takes no arguments ({args.Length - 1} given)");
                            return self.Copy();
                        });
                    }
                default:
                    return base.GetAttribute(name);
            }
        }
        */

        #endregion

        #region String Representation

        public override PyString ToStr() => ToRepr();

        public override PyString ToRepr()
        {
            if (_items.Count == 0) return StringCache.GetOrCreate("set()");

            // Performance: Eliminated LINQ (Select) - use StringBuilder
            var sb = new System.Text.StringBuilder("{");
            bool first = true;
            foreach (var item in _items)
            {
                if (!first) sb.Append(", ");
                sb.Append(item.ToRepr().Value);
                first = false;
            }
            sb.Append("}");
            return StringCache.GetOrCreate(sb.ToString());
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

            // Performance: Eliminated LINQ (First) - use enumerator
            var item = GetFirstItem();
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
            // Performance: Eliminated LINQ (Where) - manual filtering
            var result = new PySet();
            foreach (var item in _items)
            {
                if (otherItems.Contains(item))
                {
                    result._items.Add(item);
                }
            }
            return result;
        }

        /// <summary>
        /// 차집합 set - other 또는 set.difference(other)
        /// </summary>
        public PySet Difference(PyObject other)
        {
            var otherItems = GetSetItems(other);
            // Performance: Eliminated LINQ (Where) - manual filtering
            var result = new PySet();
            foreach (var item in _items)
            {
                if (!otherItems.Contains(item))
                {
                    result._items.Add(item);
                }
            }
            return result;
        }

        /// <summary>
        /// 대칭차집합 set ^ other 또는 set.symmetric_difference(other)
        /// </summary>
        public PySet SymmetricDifference(PyObject other)
        {
            var otherItems = GetSetItems(other);
            var result = new PySet();

            // Performance: Eliminated LINQ (Where) - manual filtering
            // this - other
            foreach (var item in _items)
            {
                if (!otherItems.Contains(item))
                {
                    result._items.Add(item);
                }
            }

            // other - this
            foreach (var item in otherItems)
            {
                if (!_items.Contains(item))
                {
                    result._items.Add(item);
                }
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
            // Performance: Eliminated LINQ (Select) - manual conversion for string
            return obj switch
            {
                PyList list => list.Items,
                PyTuple tuple => tuple.Items,
                PyString str => ConvertStringToCharArray(str.Value),
                PySet set => set._items,
                PyFrozenSet frozenSet => frozenSet.Items,
                _ => throw PyTypeError.Create($"'{obj.GetTypeName()}' object is not iterable")
            };
        }

        private static IEnumerable<PyObject> ConvertStringToCharArray(string str)
        {
            var result = new List<PyObject>(str.Length);
            for (int i = 0; i < str.Length; i++)
            {
                result.Add(new PyString(str[i].ToString()));
            }
            return result;
        }

        private PyObject GetFirstItem()
        {
            foreach (var item in _items)
            {
                return item;
            }
            throw new InvalidOperationException("Set is empty");
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
            if (_items.Count == 0) return StringCache.GetOrCreate("frozenset()");

            // Performance: Eliminated LINQ (Select) - use StringBuilder
            var sb = new System.Text.StringBuilder("frozenset({");
            bool first = true;
            foreach (var item in _items)
            {
                if (!first) sb.Append(", ");
                sb.Append(item.ToRepr().Value);
                first = false;
            }
            sb.Append("})");
            return StringCache.GetOrCreate(sb.ToString());
        }

        #endregion

        #region Hash and Equality

        public override int ToHash()
        {
            // CPython 3.12 compatible frozenset hash algorithm
            // Based on CPython's frozenset_hash implementation
            long hash = 1927868237L; // Initial hash value (CPython uses specific prime)

            // Performance: Eliminated LINQ (Select + OrderBy + ToList) - manual sort
            // Sort items by hash to ensure deterministic ordering
            var hashes = new long[_items.Count];
            int index = 0;
            foreach (var item in _items)
            {
                hashes[index++] = (long)item.ToHash();
            }
            Array.Sort(hashes);

            foreach (var itemHash in hashes)
            {
                hash ^= (itemHash ^ 89869747L) * 3644798167L;  // CPython-style mixing
            }

            // Additional mixing for better distribution
            hash = hash * 69069L + 907133923L;

            if (hash == -1)
                hash = 590923713L;

            return (int)(hash & 0xFFFFFFFF);
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

        public override PyBool Contains(PyObject item) => PyBool.FromBool(_items.Contains(item));
        
        public PyFrozenSet Union(PyObject other) => new PyFrozenSet(new PySet(_items).Union(other).Items);
        public PyFrozenSet Intersection(PyObject other) => new PyFrozenSet(new PySet(_items).Intersection(other).Items);
        public PyFrozenSet Difference(PyObject other) => new PyFrozenSet(new PySet(_items).Difference(other).Items);
        public PyFrozenSet SymmetricDifference(PyObject other) => new PyFrozenSet(new PySet(_items).SymmetricDifference(other).Items);
        
        public PyBool IsSubset(PyObject other) => new PySet(_items).IsSubset(other);
        public PyBool IsSuperset(PyObject other) => new PySet(_items).IsSuperset(other);
        public PyBool IsDisjoint(PyObject other) => new PySet(_items).IsDisjoint(other);

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