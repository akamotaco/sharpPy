using System;

namespace SharpPy
{
    /// <summary>
    /// Python tuple 타입 구현 - 불변 순서 컬렉션
    /// </summary>
    public class PyTuple : PyObject
    {
        // PyTuple_CheckExact
        internal bool IsExact => IsExactly(PyType.TupleType);

        // What the slots of tuple read: the items, not the methods of a subclass.
        internal PyTuple Stored => IsExact ? this : PyTuple.From(Items);

        /// <summary>
        /// A subclass of tuple/bytes resolves the slot through its own type: a declared dunder wins
        /// and the inherited base slot only runs when the class declares none (measured: a tuple
        /// subclass with __len__/__iter__/__add__ answered 2, (1, 2) and (1, 2, 3) where CPython
        /// answered 99, ['t'] and 'user-tadd'). Every entry below is guarded by HasCustomType, so an
        /// exact tuple or bytes pays one field read; these are the hottest paths in the interpreter.
        /// </summary>



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
            tupleType.TypeDict["__len__"] = new PyWrapperDescriptor("__len__", tupleType, (self, args, kwargs) => {
                var tuple = (PyTuple)self;
                return PyInt.From(tuple.Items.Length);
            });

            // CPython materialises tuple's sequence/iteration slots in tp_dict so
            // dir(tuple) lists them and tuple.__iter__/__contains__/__getnewargs__
            // are callable through attribute lookup. __iter__/__contains__ are slot
            // wrappers; __getnewargs__ is an explicit method.
            tupleType.TypeDict["__iter__"] = new PyWrapperDescriptor(
                "__iter__", tupleType,
                (self, args, kwargs) => {
                    // Strict typed call (not the PyObject virtual) to avoid re-entering
                    // this descriptor for subclasses; CPython reads the tuple slot
                    // directly for real tuples. Tuple subclasses are unmodelled here.
                    var it = (PyTuple)self;
                    return it.Stored.Iter();
                }, minArgs: 0, maxArgs: 0);

            tupleType.TypeDict["__contains__"] = new PyWrapperDescriptor(
                "__contains__", tupleType,
                (self, args, kwargs) => {
                    var ct = (PyTuple)self;
                    return ct.Stored.ContainsItem(args[0]);
                }, minArgs: 1, maxArgs: 1);

            tupleType.TypeDict["__getnewargs__"] = new PyMethodDescriptor(
                "__getnewargs__", tupleType,
                // tuple_getnewargs: (tupleslice(v, 0, n),), the tuple itself only when exact.
                (self, args, kwargs) => PyTuple.From(((PyTuple)self).Stored),
                minArgs: 0, maxArgs: 0);

            // CPython 3.12: Objects/tupleobject.c:488-502 - tuple_index
            // T.index(value, [start, [stop]]) -> integer -- return first index of value.
            // Raises ValueError if the value is not present.
            tupleType.TypeDict["index"] = new PyMethodDescriptor(
                "index", tupleType,
                (self, args, kwargs) => {
                    if (kwargs != null && kwargs.Length() != 0)
                        throw PyTypeError.Create("tuple.index() takes no keyword arguments");

                    var tuple = (PyTuple)self;

                    if (args.Length < 1 || args.Length > 3)
                        throw PyTypeError.Create($"index expected at most 3 arguments, got {args.Length}");

                    var value = args[0];
                    // CPython tuple.index uses PyNumber_Index for start/stop: bool and
                    // __index__ are accepted, non-integers raise with this wording.
                    long start = args.Length >= 2 ? PyArgs.SliceIndexNotNone(args[1]) : 0;
                    long? stop = args.Length >= 3 ? PyArgs.SliceIndexNotNone(args[2]) : null;

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
                        throw PyTypeError.Create($"tuple.count() takes exactly one argument ({args.Length} given)");
                    var tuple = (PyTuple)self;

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
                    // CPython 3.12: tuple is immutable, so we can call GetItem directly
                    // No need for special storage handling like dict/list
                    var tuple = (PyTuple)self;

                    return tuple.Stored.MpSubscript(args[0]);
                },
                minArgs: 1, maxArgs: 1
            );

            // CPython 3.12: Objects/tupleobject.c:440-486 - tupleconcat
            // CPython 3.12: Objects/tupleobject.c:753 - sq_concat mapped to __add__
            // tuple + tuple -> concatenated tuple
            tupleType.TypeDict["__add__"] = new PyWrapperDescriptor(
                "__add__", tupleType,
                (self, args, kwargs) => {
                    var tuple = (PyTuple)self;

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
                    var tuple = (PyTuple)self;

                    return tuple.Multiply(args[0]);
                },
                minArgs: 1, maxArgs: 1
            );

            // CPython 3.12: tuple.__rmul__ is same as __mul__ (sequence repeat is commutative)
            tupleType.TypeDict["__rmul__"] = new PyWrapperDescriptor(
                "__rmul__", tupleType,
                (self, args, kwargs) => {
                    var tuple = (PyTuple)self;

                    return tuple.Multiply(args[0]);
                },
                minArgs: 1, maxArgs: 1
            );
            // sq_repeat's wrappers, wrap_indexargfunc
            PySlotWrappers.IndexArgRepeat(tupleType);

            // CPython 3.12: Objects/tupleobject.c:576
            // {"__class_getitem__", Py_GenericAlias, METH_O|METH_CLASS, PyDoc_STR("See PEP 585")},
            tupleType.TypeDict["__class_getitem__"] = PyClassMethodDescriptor.GenericAliasGetItem(tupleType);
        }

        #region Core Properties

        public PyObject[] Items { get; }

        public PyTuple(params PyObject[] items) => Items = items ?? new PyObject[0];

        public override PyType GetPyType() => _customType ?? PyType.TupleType;

        // CPython 3.12: Objects/tupleobject.c - tuple_bool
        // Optimized: Direct length check, no MRO traversal
        protected internal override bool Bool() => Items.Length > 0;

        #endregion

        #region String Representation

        protected internal override PyString Str() => ToRepr();

        protected internal override PyString Repr()
        {
            if (Items.Length == 0) return PyString.From("()");

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
                return PyString.From(singleResult);
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

            return PyString.From(result);
        }

        public override string ToString() => ToRepr().Value;

        #endregion

        #region Hash and Equality

        // tuplehash: the xxHash mixing of the items' hashes, at the native width.
        protected internal override long Hash()
        {
            if (IntPtr.Size == 8)
            {
                ulong acc = 2870177450012600261UL;
                foreach (var item in Items)
                {
                    acc = unchecked(acc + (ulong)item.GetPythonHash() * 14029467366897019727UL);
                    acc = (acc << 31) | (acc >> 33);
                    acc = unchecked(acc * 11400714785074694791UL);
                }
                acc = unchecked(acc + ((ulong)Items.Length ^ (2870177450012600261UL ^ 3527539UL)));
                return acc == ulong.MaxValue ? 1546275796 : unchecked((long)acc);
            }
            uint small = 374761393U;
            foreach (var item in Items)
            {
                small = unchecked(small + (uint)item.GetPythonHash() * 2246822519U);
                small = (small << 13) | (small >> 19);
                small = unchecked(small * 2654435761U);
            }
            small = unchecked(small + ((uint)Items.Length ^ (374761393U ^ 3527539U)));
            return small == uint.MaxValue ? 1546275796 : unchecked((int)small);
        }

        public override int ToHash()
        {
            int hash = 0x345678;
            foreach (var item in Items)
            {
                hash = ((hash << 5) + hash) ^ item.ToHash();
            }
            return hash;
        }

        protected internal override PyObject TpRichCompare(PyObject other, CompareOp op)
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
                return PyTuple.From(result);
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
                // tuple_repeat: an exact tuple that is empty or taken once is itself; a count below 1
                // is the empty tuple; a size past Py_ssize_t is PyErr_NoMemory.
                long n = PyAllocation.IndexSized(count.Value);
                bool exact = IsExactly(PyType.TupleType);
                if ((Items.Length == 0 || n == 1) && exact)
                    return this;
                if (Items.Length == 0 || n <= 0)
                    return PyTuple.Empty;
                var result = new PyObject[PyAllocation.RepeatedItems(Items.Length, n)];
                for (int i = 0; i < count.Value; i++)
                {
                    Array.Copy(Items, 0, result, i * Items.Length, Items.Length);
                }
                return PyTuple.From(result);
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
        protected internal override PyObject MpSubscript(PyObject key)
        {
            if (key is PySlice slice)
            {
                var (start, stop, step) = slice.StorageIndices(Items.Length);
                return GetSlice(start, stop, step);
            }

            if (TrySequenceIndex(key, out int position))
                return GetItem(position);
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
                return PyTuple.From(contiguous);
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
            return PyTuple.From(resultArray);
        }

        /// <summary>
        /// 요소 포함 여부 확인 (in 연산자)
        /// CPython: __contains__ magic method
        /// </summary>
        protected internal override PyBool ContainsItem(PyObject item)
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
                    return PyInt.From(i);
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
            return PyInt.From(count);
        }

        #endregion

        #region Iterator Protocol

        /// <summary>
        /// Iterator protocol 구현 - Python __iter__ 메서드
        /// </summary>
        protected internal override PyObject Iter() => new PyTupleIterator(this);

        #endregion

        #region Length and Type Checking

        protected internal override long Len() => Items.Length;

        #endregion

        #region Static Factory Methods

        /// <summary>
        /// There is exactly one empty tuple: PyTuple_New returns tuple_get_empty for size 0
        /// (Objects/tupleobject.c:68-73, :62), which is why <c>() is ()</c> is true in Python. This
        /// was a property that built a fresh object on every access, and runtime/TupleCache.cs held a
        /// second competing empty tuple, so identity checks below the corrected id() came apart.
        /// </summary>
        public static readonly PyTuple Empty = new PyTuple();

        /// <summary>
        /// The one way the interpreter makes an exact tuple, as PyTuple_New is CPython's: the empty
        /// tuple is tuple_get_empty's one object, any other is a new tuple (no other is shared).
        /// The public constructor stays for host code.
        /// </summary>
        public static PyTuple From(params PyObject[] items) =>
            items == null || items.Length == 0 ? Empty : new PyTuple(items);

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
            if (!IsExact) return base.AsTuple();
            // CPython tuple() 생성자 동작: 새로운 복사본 생성
            // Performance: Eliminated LINQ (.ToArray) - direct array copy + Cache
            var copy = new PyObject[Items.Length];
            Array.Copy(Items, copy, Items.Length);
            return PyTuple.From(copy);
        }
        
        /// <summary>
        /// CPython 호환: PyTuple을 PyList로 변환
        /// </summary>
        public override PyList AsList()
        {
            if (!IsExact) return base.AsList();
            // CPython list(tuple) 동작: 튜플 요소들을 리스트로 변환
            return new PyList(Items);
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
        public override string ToClrString()
        {
            return ToRepr().Value; // CPython에서 str(tuple)는 repr(tuple)와 동일
        }

        #endregion
    }
}
