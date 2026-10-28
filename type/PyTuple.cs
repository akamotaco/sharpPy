using System;

namespace SharpPy
{
    /// <summary>
    /// Python tuple 타입 구현 - 불변 순서 컬렉션
    /// </summary>
    public class PyTuple : PyObject
    {
        static PyTuple()
        {
            InitializeTupleDescriptors();
        }

        /// <summary>
        /// Initialize tuple type descriptors (CPython 3.12 compatible)
        /// CPython reference: Objects/tupleobject.c:550-600 - tuple_methods
        /// </summary>
        public static void InitializeTupleDescriptors()
        {
            var tupleType = PyType.TupleType;
            tupleType.TypeDict["__len__"] = new PyMethodDescriptor("__len__", tupleType, (self, args, kwargs) => {
                if (self is not PyTuple tuple)
                    throw PyTypeError.Create($"descriptor '__len__' requires a 'tuple' object but received a '{self.GetTypeName()}'");
                if (args.Length != 0) throw PyTypeError.Create($"expected 0 arguments, got {args.Length}");
                if (kwargs != null && kwargs.Length() != 0) throw PyTypeError.Create("wrapper __len__() takes no keyword arguments");
                return new PyInt(tuple.Items.Length);
            });

            // CPython 3.12: Objects/tupleobject.c:488-502 - tuple_index
            // T.index(value, [start, [stop]]) -> integer -- return first index of value.
            // Raises ValueError if the value is not present.
            tupleType.TypeDict["index"] = new PyMethodDescriptor(
                "index", tupleType,
                (self, args, kwargs) => {
                    if (kwargs != null && kwargs.Length() != 0)
                        throw PyTypeError.Create("tuple.index() takes no keyword arguments");

                    if (self is not PyTuple tuple)
                        throw PyTypeError.Create($"descriptor 'index' requires a 'tuple' object but received a '{self.GetTypeName()}'");

                    if (args.Length < 1 || args.Length > 3)
                        throw PyTypeError.Create($"index expected at most 3 arguments, got {args.Length}");

                    var value = args[0];
                    // CPython tuple.index uses PyNumber_Index for start/stop: bool and
                    // __index__ are accepted, non-integers raise with this wording.
                    long start = 0;
                    if (args.Length >= 2 && args[1] != PyNone.Instance)
                    {
                        var si = PyObject.TryGetIndex(args[1]);
                        if (si == null)
                            throw PyTypeError.Create("slice indices must be integers or have an __index__ method");
                        start = (long)si.Value;
                    }

                    long? stop = null;
                    if (args.Length >= 3 && args[2] != PyNone.Instance)
                    {
                        var si = PyObject.TryGetIndex(args[2]);
                        if (si == null)
                            throw PyTypeError.Create("slice indices must be integers or have an __index__ method");
                        stop = (long)si.Value;
                    }

                    return tuple.Index(value, start, stop);
                },
                minArgs: 1, maxArgs: 3
            );

            // CPython 3.12: Objects/tupleobject.c:550-560 - tuple_count
            // T.count(value) -> integer -- return number of occurrences of value
            tupleType.TypeDict["count"] = new PyMethodDescriptor(
                "count", tupleType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"count expected exactly 1 arguments, got {args.Length}");
                    if (self is not PyTuple tuple)
                        throw PyTypeError.Create($"descriptor 'count' requires a 'tuple' object but received a '{self.GetTypeName()}'");

                    var value = args[0];
                    return tuple.Count(value);
                },
                minArgs: 1, maxArgs: 1
            );

            // CPython 3.12: Objects/tupleobject.c:230-280 (tuplesubscript)
            // __getitem__ descriptor
            tupleType.TypeDict["__getitem__"] = new PyWrapperDescriptor(
                "__getitem__", tupleType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"__getitem__() takes exactly 1 argument ({args.Length} given)");

                    // CPython 3.12: tuple is immutable, so we can call GetItem directly
                    // No need for special storage handling like dict/list
                    if (self is not PyTuple tuple)
                        throw PyTypeError.Create($"descriptor '__getitem__' requires a 'tuple' object but received a '{self.GetTypeName()}'");

                    return tuple.GetItem(args[0]);
                },
                minArgs: 1, maxArgs: 1
            );

            // CPython 3.12: Objects/tupleobject.c:440-486 - tupleconcat
            // CPython 3.12: Objects/tupleobject.c:753 - sq_concat mapped to __add__
            // tuple + tuple -> concatenated tuple
            tupleType.TypeDict["__add__"] = new PyWrapperDescriptor(
                "__add__", tupleType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"__add__() takes exactly 1 argument ({args.Length} given)");

                    if (self is not PyTuple tuple)
                        throw PyTypeError.Create($"descriptor '__add__' requires a 'tuple' object but received a '{self.GetTypeName()}'");

                    return tuple.Add(args[0]);
                },
                minArgs: 1, maxArgs: 1
            );

            // CPython 3.12: Objects/tupleobject.c:487-540 - tuplerepeat
            // CPython 3.12: Objects/tupleobject.c:754 - sq_repeat mapped to __mul__ and __rmul__
            // tuple * int -> repeated tuple
            tupleType.TypeDict["__mul__"] = new PyWrapperDescriptor(
                "__mul__", tupleType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"__mul__() takes exactly 1 argument ({args.Length} given)");

                    if (self is not PyTuple tuple)
                        throw PyTypeError.Create($"descriptor '__mul__' requires a 'tuple' object but received a '{self.GetTypeName()}'");

                    return tuple.Multiply(args[0]);
                },
                minArgs: 1, maxArgs: 1
            );

            // CPython 3.12: tuple.__rmul__ is same as __mul__ (sequence repeat is commutative)
            tupleType.TypeDict["__rmul__"] = new PyMethodDescriptor(
                "__rmul__", tupleType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"__rmul__() takes exactly 1 argument ({args.Length} given)");

                    if (self is not PyTuple tuple)
                        throw PyTypeError.Create($"descriptor '__rmul__' requires a 'tuple' object but received a '{self.GetTypeName()}'");

                    return tuple.Multiply(args[0]);
                },
                minArgs: 1, maxArgs: 1
            );

            // CPython 3.12: Objects/tupleobject.c:576
            // {"__class_getitem__", Py_GenericAlias, METH_O|METH_CLASS, PyDoc_STR("See PEP 585")},
            tupleType.TypeDict["__class_getitem__"] = new PyBuiltinClassMethod("__class_getitem__",
                (cls, arg) => new PyGenericAlias(cls as PyType ?? throw PyTypeError.Create("Expected type"), arg)
            );
        }

        #region Core Properties

        public PyObject[] Items { get; }

        public PyTuple(params PyObject[] items) => Items = items ?? new PyObject[0];

        public override PyType GetPyType() => PyType.TupleType;
        public override string GetTypeName() => "tuple";

        // CPython 3.12: Objects/tupleobject.c - tuple_bool
        // Optimized: Direct length check, no MRO traversal
        public override bool IsTrue() => Items.Length > 0;

        #endregion

        #region String Representation

        public override PyString ToStr() => ToRepr();

        public override PyString ToRepr()
        {
            if (Items.Length == 0) return StringCache.GetOrCreate("()");

            // Single element tuple needs trailing comma
            if (Items.Length == 1)
            {
                string itemRepr = Items[0].ToRepr().Value;
                int singleLength = 3 + itemRepr.Length; // "(,)"

                // Performance: string.Create() - single allocation
                var singleResult = string.Create(singleLength, itemRepr, (span, repr) =>
                {
                    span[0] = '(';
                    repr.AsSpan().CopyTo(span.Slice(1));
                    span[singleLength - 2] = ',';
                    span[singleLength - 1] = ')';
                });
                return StringCache.GetOrCreate(singleResult);
            }

            // Performance: string.Create() - CPython-style single allocation

            // Step 1: Calculate total length and cache reprs
            var reprs = new string[Items.Length];
            int totalLength = 2; // "()"
            for (int i = 0; i < Items.Length; i++)
            {
                reprs[i] = Items[i].ToRepr().Value;
                if (i > 0) totalLength += 2; // ", "
                totalLength += reprs[i].Length;
            }

            // Step 2: string.Create with single allocation
            var result = string.Create(totalLength, reprs, (span, items) =>
            {
                int pos = 0;
                span[pos++] = '(';

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

                span[pos] = ')';
            });

            return StringCache.GetOrCreate(result);
        }

        public override string ToString() => ToRepr().Value;

        #endregion

        #region Hash and Equality

        public override int ToHash()
        {
            int hash = 0x345678;
            foreach (var item in Items)
            {
                hash = ((hash << 5) + hash) ^ item.ToHash();
            }
            return hash;
        }

        public override PyObject RichCompare(PyObject other, CompareOp op)
        {
            if (other is not PyTuple tuple) return PyNotImplemented.Instance;
            int count = Math.Min(Items.Length, tuple.Items.Length);
            for (int i = 0; i < count; i++)
            {
                if (CompareBool(Items[i], tuple.Items[i], CompareOp.EQ)) continue;
                if (op == CompareOp.EQ) return PyBool.False;
                if (op == CompareOp.NE) return PyBool.True;
                return Compare(Items[i], tuple.Items[i], op);
            }
            return op switch
            {
                CompareOp.EQ => PyBool.FromBool(Items.Length == tuple.Items.Length),
                CompareOp.NE => PyBool.FromBool(Items.Length != tuple.Items.Length),
                CompareOp.LT => PyBool.FromBool(Items.Length < tuple.Items.Length),
                CompareOp.LE => PyBool.FromBool(Items.Length <= tuple.Items.Length),
                CompareOp.GT => PyBool.FromBool(Items.Length > tuple.Items.Length),
                CompareOp.GE => PyBool.FromBool(Items.Length >= tuple.Items.Length),
                _ => throw PyValueError.Create($"Invalid comparison operator: {op}")
            };
        }

        #endregion

        #region Tuple Operations

        /// <summary>
        /// 튜플 연결 (+ 연산자)
        /// CPython 3.12: Objects/tupleobject.c:440-486 - tupleconcat
        /// </summary>
        public override PyObject Add(PyObject other)
        {
            if (other is PyTuple otherTuple)
            {
                // Performance: Eliminated LINQ (.Concat + .ToArray) - manual array concatenation + Cache
                var result = new PyObject[Items.Length + otherTuple.Items.Length];
                Array.Copy(Items, 0, result, 0, Items.Length);
                Array.Copy(otherTuple.Items, 0, result, Items.Length, otherTuple.Items.Length);
                return TupleCache.GetOrCreate(result);
            }

            throw PyTypeError.Create($"can only concatenate tuple (not \"{other.GetTypeName()}\") to tuple");
        }

        /// <summary>
        /// 튜플 반복 (* 연산자)
        /// CPython 3.12: Objects/tupleobject.c:487-540 - tuplerepeat
        /// </summary>
        public override PyObject Multiply(PyObject other)
        {
            if (other is PyInt count)
            {
                if (count.Value <= 0)
                    return TupleCache.Empty;

                // Performance: Eliminated LINQ (Enumerable.Range + .SelectMany + .ToArray) - manual repetition + Cache
                var result = new PyObject[Items.Length * (int)count.Value];
                for (int i = 0; i < count.Value; i++)
                {
                    Array.Copy(Items, 0, result, i * Items.Length, Items.Length);
                }
                return TupleCache.GetOrCreate(result);
            }

            throw PyTypeError.Create($"can't multiply sequence by non-int of type '{other.GetTypeName()}'");
        }

        /// <summary>
        /// 인덱스 접근 tuple[i]
        /// </summary>
        public PyObject GetItem(int index)
        {
            // Python식 음수 인덱스 지원
            if (index < 0) index += Items.Length;
            
            if (index < 0 || index >= Items.Length)
                throw PyIndexError.Create("tuple index out of range");
            
            return Items[index];
        }

        /// <summary>
        /// CPython 3.12: tuple.__getitem__ - Indexing and slicing
        /// CPython: Objects/tupleobject.c:tuplesubscript (lines ~230-280)
        /// </summary>
        public override PyObject GetItem(PyObject key)
        {
            if (key is PyInt index)
                return GetItem((int)index.Value);

            if (key is PySlice slice)
            {
                var (start, stop, step) = slice.StorageIndices(Items.Length);
                return GetSlice(start, stop, step);
            }

            throw PyTypeError.Create($"tuple indices must be integers or slices, not {key.GetTypeName()}");
        }

        /// <summary>
        /// CPython 3.12: tuple slicing with normalized indices from PySlice.Indices()
        /// CPython: Objects/tupleobject.c:tuplesubscript (lines ~230-280)
        /// Note: start, stop, step are already normalized by PySlice.Indices()
        /// </summary>
        public PyTuple GetSlice(int start, int stop, int step)
        {
            if (step == 0)
                throw PyValueError.Create("slice step cannot be zero");

            if (step == 1)
            {
                var contiguous = new PyObject[Math.Max(0, stop - start)];
                Array.Copy(Items, start, contiguous, 0, contiguous.Length);
                return TupleCache.GetOrCreate(contiguous);
            }

            var result = new System.Collections.Generic.List<PyObject>();

            if (step > 0)
            {
                for (long position = start; position < stop; position += step)
                {
                    int i = (int)position;
                    if (i >= 0 && i < Items.Length)
                        result.Add(Items[i]);
                }
            }
            else
            {
                for (int i = start; i > stop; i += step)
                {
                    if (i >= 0 && i < Items.Length)
                        result.Add(Items[i]);
                }
            }

            // Performance: Eliminated LINQ (.ToArray) - direct array copy + Cache
            var resultArray = new PyObject[result.Count];
            result.CopyTo(resultArray, 0);
            return TupleCache.GetOrCreate(resultArray);
        }

        /// <summary>
        /// 요소 포함 여부 확인 (in 연산자)
        /// CPython: __contains__ magic method
        /// </summary>
        public override PyBool Contains(PyObject item)
        {
            // Performance: Eliminated LINQ (.Any) - manual search
            for (int i = 0; i < Items.Length; i++)
            {
                if (CompareBool(Items[i], item, CompareOp.EQ))
                    return PyBool.True;
            }
            return PyBool.False;
        }

        /// <summary>
        /// 첫 번째 일치하는 요소의 인덱스 반환
        /// </summary>
        public PyInt Index(PyObject value, long start = 0, long? stop = null)
        {
            // CPython tupleindex_impl clamps negative start/stop into range.
            long len = Items.Length;
            if (start < 0) start = Math.Max(start + len, 0);
            else start = Math.Min(start, len);
            long actualStop = stop ?? len;
            if (actualStop < 0) actualStop = Math.Max(actualStop + len, 0);
            else actualStop = Math.Min(actualStop, len);

            for (long i = start; i < actualStop; i++)
            {
                if (CompareBool(Items[(int)i], value, CompareOp.EQ))
                    return new PyInt(i);
            }
            
            // CPython tupleindex_impl: static message (Objects/tupleobject.c).
            throw PyValueError.Create("tuple.index(x): x not in tuple");
        }

        /// <summary>
        /// 특정 값의 개수
        /// </summary>
        public PyInt Count(PyObject value)
        {
            // Performance: Eliminated LINQ (.Count) - manual counting
            int count = 0;
            for (int i = 0; i < Items.Length; i++)
            {
                if (CompareBool(Items[i], value, CompareOp.EQ))
                    count++;
            }
            return new PyInt(count);
        }

        #endregion

        #region Iterator Protocol

        /// <summary>
        /// Iterator protocol 구현 - Python __iter__ 메서드
        /// </summary>
        public override PyIterator GetIterator()
        {
            return new PyTupleIterator(this);
        }

        #endregion

        #region Length and Type Checking

        public override int Length() => Items.Length;
        public override bool PyBoolValue() => Items.Length > 0;

        #endregion

        #region Static Factory Methods

        public static PyTuple Empty => new PyTuple();

        #endregion

        #region Evaluate Method (NotImplementedException)

        public PyObject Evaluate(PyScope scope)
        {
            // Tuple literals evaluate to themselves (CPython style)
            return this;
        }

        #endregion

        #region Type Conversion (CPython Compatible)

        // === To* Methods: Value Extraction (PyTuple → C# basic types) ===
        
        /// <summary>
        /// CPython PyLong_AsLong 호환: PyTuple은 일반적으로 int로 변환될 수 없음
        /// </summary>
        public override int ToInt()
        {
            throw PyTypeError.Create($"int() argument must be a string, a bytes-like object or a real number, not 'tuple'");
        }
        
        /// <summary>
        /// CPython PyFloat_AsDouble 호환: PyTuple은 일반적으로 float로 변환될 수 없음
        /// </summary>
        public override double ToFloat()
        {
            throw PyTypeError.Create($"float() argument must be a string or a real number, not 'tuple'");
        }
        
        
        // === As* Methods: Type Conversion (PyTuple → PyObject types) ===
        
        /// <summary>
        /// CPython 호환: PyTuple을 PyTuple로 변환 (복사본 생성)
        /// </summary>
        public override PyTuple AsTuple()
        {
            // CPython tuple() 생성자 동작: 새로운 복사본 생성
            // Performance: Eliminated LINQ (.ToArray) - direct array copy + Cache
            var copy = new PyObject[Items.Length];
            Array.Copy(Items, copy, Items.Length);
            return TupleCache.GetOrCreate(copy);
        }
        
        /// <summary>
        /// CPython 호환: PyTuple을 PyList로 변환
        /// </summary>
        public override PyList AsList()
        {
            // CPython list(tuple) 동작: 튜플 요소들을 리스트로 변환
            return ListCache.Create(Items);
        }
        
        /// <summary>
        /// CPython 호환: PyTuple을 PyBool로 변환
        /// </summary>
        public override PyBool AsBool()
        {
            return PyBool.FromBool(Items.Length > 0);
        }
        
        /// <summary>
        /// CPython 호환: PyTuple을 PyString으로 변환 (str() 호출과 동일)
        /// </summary>
        public override string AsString()
        {
            return ToRepr().Value; // CPython에서 str(tuple)는 repr(tuple)와 동일
        }

        #endregion
    }
}
