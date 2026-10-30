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
        private readonly PySetStorage _items;

        public PySet() => _items = new PySetStorage();
        
        public PySet(IEnumerable<PyObject> items) => 
            _items = new PySetStorage(items);

        internal static IEnumerable<PyObject> IterableItems(PyObject value)
        {
            if (value is PySet set) return set.Items;
            if (value is PyFrozenSet frozen) return frozen.Items;
            return Iterate(value);
        }
        private static IEnumerable<PyObject> Iterate(PyObject value)
        {
            var iterator = value.GetIterator();
            while (true)
            {
                PyObject item;
                try { item = iterator.Next(); }
                catch (PythonException error) when (error.PyException is PyStopIteration) { yield break; }
                yield return item;
            }
        }

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

        public override PyString AsString() => ToRepr();

        public override PyString ToRepr() => Repr("set", exactType: true);

        /// <summary>
        /// CPython Objects/setobject.c:507 set_repr formats with Py_TYPE(so)-&gt;tp_name, which is why
        /// the three shapes differ only by who is asking:
        ///   empty            "%s()"        -&gt; set(), frozenset(), C()
        ///   exact set        "{%U}"        -&gt; {1, 2}
        ///   any other type   "%s({%U})"    -&gt; frozenset({1, 2}), C({1, 2})
        /// frozenset shares this function: PySet_CheckExact() is false for it, so an exact
        /// frozenset also takes the prefixed branch and prints frozenset({1, 2}).
        /// A subclass of set therefore needs the same code with its own name, not a second repr.
        /// </summary>
        internal PyString Repr(string typeName, bool exactType)
        {
            if (_items.Count == 0) return StringCache.GetOrCreate(typeName + "()");

            // Performance: Eliminated LINQ (Select) - use StringBuilder
            bool prefix = !exactType;
            var sb = new System.Text.StringBuilder(prefix ? typeName + "({" : "{");
            bool first = true;
            foreach (var item in _items)
            {
                if (!first) sb.Append(", ");
                sb.Append(item.ToRepr().Value);
                first = false;
            }
            sb.Append(prefix ? "})" : "}");
            return StringCache.GetOrCreate(sb.ToString());
        }

        #endregion

        #region Hash and Equality

        // set_richcompare tests the other operand with PySet_Check/PyFrozenSet_Check, which accept
        // subclasses, so {1,2} == C({1,2}) has to see through the user object to its storage.
        protected override PyObject PyEquals(PyObject other)
        {
            return PyNativeStorage.Unwrap(other) switch
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
                throw PyKeyError.Create(item);
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
                throw PyKeyError.Create("pop from an empty set");

            // Performance: Eliminated LINQ (First) - use enumerator
            return _items.Pop();
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
            if (other is PySet set) { _items.Merge(set._items); return PyNone.Instance; }
            if (other is PyFrozenSet frozen) { _items.Merge((PySetStorage)frozen.Items); return PyNone.Instance; }
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
            result.Update(other);
            return result;
        }

        /// <summary>
        /// 교집합 set & other 또는 set.intersection(other)
        /// </summary>
        public PySet Intersection(PyObject other)
        {
            if (ReferenceEquals(this, other)) return Copy();
            var result = new PySet();
            if (other is not PySet && other is not PyFrozenSet)
            {
                foreach (var item in IterableItems(other))
                {
                    long hash = item.GetPythonHash();
                    if (_items.ContainsKnown(item, hash))
                    {
                        result._items.AddKnown(item, hash);
                        if (result.Length() >= Length()) break;
                    }
                }
                return result;
            }
            var otherItems = GetSetItems(other);
            var scanned = otherItems.Count > _items.Count ? _items : otherItems;
            var searched = otherItems.Count > _items.Count ? otherItems : _items;
            foreach (var entry in scanned.Entries())
                if (searched.ContainsKnown(entry.Key, entry.Hash)) result._items.AddKnown(entry.Key, entry.Hash);
            return result;
        }

        /// <summary>
        /// 차집합 set - other 또는 set.difference(other)
        /// </summary>
        public PySet Difference(PyObject other)
        {
            if ((other is not PySet && other is not PyFrozenSet) || (Length() >> 2) > other.Length())
            {
                var copied = Copy();
                copied.DifferenceUpdate(other);
                return copied;
            }
            var otherItems = GetSetItems(other);
            var result = new PySet();
            foreach (var entry in _items.Entries())
                if (!otherItems.ContainsKnown(entry.Key, entry.Hash)) result._items.AddKnown(entry.Key, entry.Hash);
            return result;
        }

        /// <summary>
        /// 대칭차집합 set ^ other 또는 set.symmetric_difference(other)
        /// </summary>
        public PySet SymmetricDifference(PyObject other)
        {
            var otherItems = GetSetItems(other);
            var result = new PySet(otherItems);
            result._items.SymmetricExceptWith(_items);
            return result;
        }

        #endregion

        #region Set Arithmetic Operations (Python operators)

        /// <summary>
        /// 합집합 연산 (| 연산자)
        /// </summary>
        public override PyObject BitwiseOr(PyObject other)
        {
            // CPython set_or: both operands must be set/frozenset (setobject.c).
            RequireAnySet(this, other, "|");
            return Union(other);
        }

        /// <summary>
        /// 교집합 연산 (& 연산자)
        /// </summary>
        public override PyObject BitwiseAnd(PyObject other)
        {
            RequireAnySet(this, other, "&");
            return Intersection(other);
        }

        /// <summary>
        /// 차집합 연산 (- 연산자)
        /// </summary>
        public override PyObject Subtract(PyObject other)
        {
            RequireAnySet(this, other, "-");
            return Difference(other);
        }

        /// <summary>
        /// 대칭 차집합 연산 (^ 연산자)
        /// </summary>
        public override PyObject BitwiseXor(PyObject other)
        {
            RequireAnySet(this, other, "^");
            return SymmetricDifference(other);
        }

        // CPython nb_inplace_* slots: mutate in place and preserve identity.
        public override PyObject? InplaceBitwiseOr(PyObject other)
        {
            RequireAnySet(this, other, "|=");
            Update(other);
            return this;
        }

        public override PyObject? InplaceBitwiseAnd(PyObject other)
        {
            RequireAnySet(this, other, "&=");
            IntersectionUpdate(other);
            return this;
        }

        public override PyObject? InplaceSubtract(PyObject other)
        {
            RequireAnySet(this, other, "-=");
            DifferenceUpdate(other);
            return this;
        }

        public override PyObject? InplaceBitwiseXor(PyObject other)
        {
            RequireAnySet(this, other, "^=");
            SymmetricDifferenceUpdate(other);
            return this;
        }

        internal static void RequireAnySet(PyObject self, PyObject other, string op)
        {
            // set_or / set_and / set_sub / set_xor test both operands with PyAnySet_Check, which
            // accepts subclasses, so {1} | C({2}) is a union and not a TypeError. The message still
            // names the operand the caller passed, which is what CPython prints.
            var mate = PyNativeStorage.Unwrap(other);
            if (!(mate is PySet) && !(mate is PyFrozenSet))
                throw PyTypeError.Create($"unsupported operand type(s) for {op}: '{self.GetTypeName()}' and '{other.GetTypeName()}'");
        }

        #endregion

        #region Set Comparison Operations

        /// <summary>
        /// 부분집합 확인 set.issubset(other) 또는 set <= other
        /// </summary>
        public PyBool IsSubset(PyObject other)
        {
            if (other is not PySet && other is not PyFrozenSet)
                return PyBool.FromBool(Intersection(other).Length() == Length());
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
            if (other is not PySet && other is not PyFrozenSet)
            {
                foreach (var item in IterableItems(other)) if (!_items.Contains(item)) return PyBool.False;
                return PyBool.True;
            }
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
            if (ReferenceEquals(this, other)) return PyBool.FromBool(Length() == 0);
            if (other is not PySet && other is not PyFrozenSet)
            {
                foreach (var item in IterableItems(other)) if (_items.Contains(item)) return PyBool.False;
                return PyBool.True;
            }
            var otherItems = GetSetItems(other);
            return PyBool.FromBool(!_items.Overlaps(otherItems));
        }

        /// <summary>
        /// CPython 3.12: set rich comparison (set_richcompare)
        /// CPython: Objects/setobject.c:set_richcompare (lines 1918-1964)
        /// Implements comparison operators based on subset/superset relationships
        /// </summary>
        public override PyObject RichCompare(PyObject other, CompareOp op)
        {
            // set_richcompare tests the operands with PySet_Check / PyAnySet_Check, which accept
            // subclasses, so F([1]) < S([2]) has to see through both user objects to their values.
            other = PyNativeStorage.Unwrap(other);

            // CPython: Objects/setobject.c:1923-1925 - Type check
            if (other is not PySet && other is not PyFrozenSet)
                return PyNotImplemented.Instance;

            // CPython: Objects/setobject.c:1927-1964 - Dispatch to appropriate operation
            return op switch
            {
                CompareOp.EQ => PyEquals(other),           // set == other
                CompareOp.NE => PyNotEquals(other),        // set != other
                CompareOp.LT => IsProperSubset(other),     // set < other (proper subset)
                CompareOp.LE => IsSubset(other),           // set <= other (subset)
                CompareOp.GT => IsProperSuperset(other),   // set > other (proper superset)
                CompareOp.GE => IsSuperset(other),         // set >= other (superset)
                _ => PyNotImplemented.Instance
            };
        }

        #endregion

        #region Set Mutating Operations

        /// <summary>
        /// 교집합으로 업데이트 set &= other
        /// </summary>
        public PyNone IntersectionUpdate(PyObject other)
        {
            if (ReferenceEquals(this, other)) return PyNone.Instance;
            _items.ReplaceWith(Intersection(other)._items);
            return PyNone.Instance;
        }

        /// <summary>
        /// 차집합으로 업데이트 set -= other
        /// </summary>
        public PyNone DifferenceUpdate(PyObject other)
        {
            if (other is not PySet && other is not PyFrozenSet)
            {
                foreach (var item in IterableItems(other)) _items.Remove(item);
                return PyNone.Instance;
            }
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

        private PySetStorage GetSetItems(PyObject obj)
        {
            return obj switch
            {
                PySet set => set._items,
                PyFrozenSet frozenSet => (PySetStorage)frozenSet.Items,
                _ => new PySetStorage(GetIterableItems(obj))
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
                _ => IterableItems(obj)
            };
        }

        // set('a\U00010000b') has three members: iterating a str yields characters, so this defers
        // to the one coordinate-aware splitter instead of looping over UTF-16 units.
        private static IEnumerable<PyObject> ConvertStringToCharArray(string str) =>
            PyString.Characters(str);

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
        // The frozenset method table (including copy) is registered once in
        // PyType.InitializeFrozenSetTypeDescriptors. It used to be registered here too, and that
        // copy hard-cast self to PyFrozenSet, which made C([1]).copy() die with an CLR
        // InvalidCastException instead of answering through the subclass's storage.

        #region Core Properties

        private readonly PySetStorage _items;

        public PyFrozenSet() => _items = new PySetStorage(Array.Empty<PyObject>());
        
        public PyFrozenSet(IEnumerable<PyObject> items) => 
            _items = new PySetStorage(items);

        public override PyType GetPyType() => PyType.FrozenSetType;
        public override string GetTypeName() => "frozenset";

        public IReadOnlyCollection<PyObject> Items => _items;

        #endregion

        #region String Representation

        public override PyString AsString() => ToRepr();

        // set_repr is shared with set (see PySet.Repr): the prefix is always present because
        // PySet_CheckExact() is false for a frozenset, giving frozenset({1, 2}) and frozenset().
        public override PyString ToRepr() => Repr("frozenset");

        internal PyString Repr(string typeName)
        {
            if (_items.Count == 0) return StringCache.GetOrCreate(typeName + "()");

            // Performance: Eliminated LINQ (Select) - use StringBuilder
            var sb = new System.Text.StringBuilder(typeName).Append("({");
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

        private long? cachedHash;
        internal override long GetPythonHash() => cachedHash ??= _items.FrozenHash();
        public override int ToHash() => unchecked((int)GetPythonHash());

        protected override PyObject PyEquals(PyObject other)
        {
            // PyFrozenSet_Check accepts subclasses: frozenset({1}) == C({1}) compares contents.
            return PyNativeStorage.Unwrap(other) switch
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

        public PyBool IsProperSubset(PyObject other) => new PySet(_items).IsProperSubset(other);
        public PyBool IsProperSuperset(PyObject other) => new PySet(_items).IsProperSuperset(other);

        /// <summary>
        /// CPython 3.12: frozenset rich comparison (frozenset_richcompare)
        /// CPython: Objects/setobject.c:set_richcompare (lines 1918-1964)
        /// Implements comparison operators based on subset/superset relationships
        /// </summary>
        public override PyObject RichCompare(PyObject other, CompareOp op)
        {
            // set_richcompare tests the operands with PySet_Check / PyAnySet_Check, which accept
            // subclasses, so F([1]) < S([2]) has to see through both user objects to their values.
            other = PyNativeStorage.Unwrap(other);

            // CPython: Objects/setobject.c:1923-1925 - Type check
            if (other is not PySet && other is not PyFrozenSet)
                return PyNotImplemented.Instance;

            // CPython: Objects/setobject.c:1927-1964 - Dispatch to appropriate operation
            return op switch
            {
                CompareOp.EQ => PyEquals(other),           // frozenset == other
                CompareOp.NE => PyNotEquals(other),        // frozenset != other
                CompareOp.LT => IsProperSubset(other),     // frozenset < other (proper subset)
                CompareOp.LE => IsSubset(other),           // frozenset <= other (subset)
                CompareOp.GT => IsProperSuperset(other),   // frozenset > other (proper superset)
                CompareOp.GE => IsSuperset(other),         // frozenset >= other (superset)
                _ => PyNotImplemented.Instance
            };
        }

        #endregion

        #region Set Arithmetic Operations (Python operators)

        /// <summary>
        /// 합집합 연산 (| 연산자)
        /// </summary>
        public override PyObject BitwiseOr(PyObject other)
        {
            PySet.RequireAnySet(this, other, "|");
            return Union(other);
        }

        /// <summary>
        /// 교집합 연산 (& 연산자)
        /// </summary>
        public override PyObject BitwiseAnd(PyObject other)
        {
            PySet.RequireAnySet(this, other, "&");
            return Intersection(other);
        }

        /// <summary>
        /// 차집합 연산 (- 연산자)
        /// </summary>
        public override PyObject Subtract(PyObject other)
        {
            PySet.RequireAnySet(this, other, "-");
            return Difference(other);
        }

        /// <summary>
        /// 대칭 차집합 연산 (^ 연산자)
        /// </summary>
        public override PyObject BitwiseXor(PyObject other)
        {
            PySet.RequireAnySet(this, other, "^");
            return SymmetricDifference(other);
        }

        #endregion

        #region Length and Type Checking

        public override int Length() => _items.Count;
        public override bool PyBoolValue() => _items.Count > 0;

        #endregion

        #region Copy

        public PyFrozenSet Copy() => this;

        #endregion

        #region Iterator

        /// <summary>
        /// CPython 3.12: frozenset iterator support
        /// Reference: Objects/setobject.c tp_iter slot (line 2252-2253)
        /// </summary>
        public override PyIterator GetIterator()
        {
            return new PySetIterator(this);
        }

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
        internal static readonly PyObjectEqualityComparer Instance = new PyObjectEqualityComparer();
        public bool Equals(PyObject x, PyObject y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x == null || y == null) return false;
            return PyObject.CompareBool(x, y, PyObject.CompareOp.EQ);
        }

        public int GetHashCode(PyObject obj)
        {
            return obj?.GetPythonHash().GetHashCode() ?? 0;
        }
    }
    
    #endregion
}
