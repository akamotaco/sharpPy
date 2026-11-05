using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// Python iterator protocol 구현 - __iter__ 및 __next__ 지원
    /// </summary>
    public abstract class PyIterator : PyObject, IDisposable
    {
        /// <summary>__length_hint__: what is left, or NotImplemented.</summary>
        internal virtual PyObject RemainingHint() => PyNotImplemented.Instance;

        // __reduce__ and __setstate__ of the concrete kind: Objects/listobject.c listiter_reduce_general,
        // tupleobject.c, rangeobject.c, unicodeobject.c, bytesobject.c, dictobject.c, setobject.c,
        // iterobject.c, enumobject.c, bltinmodule.c.
        internal virtual PyObject Reduce() =>
            throw PyTypeError.Create($"cannot pickle '{GetTypeName()}' object");

        internal virtual void SetState(PyObject state) =>
            throw PyAttributeError.Create($"'{GetTypeName()}' object has no attribute '__setstate__'");

        // _PyEval_GetBuiltin: the running frame's builtins, and AttributeError if it has none.
        protected static PyObject Builtin(string name) =>
            PyVM.CurrentFrame?.Builtin(name) ?? throw PyAttributeError.Create(name);

        // gh-101765: looking the name up can run Python, a __eq__ of a key in builtins, which may
        // exhaust this iterator. The state is read after that.
        private static PyObject ReduceAfterLookup(PyObject self)
        {
            var iterator = (PyIterator)self;
            Builtin(iterator.ReduceBuiltin);
            return iterator.Reduce();
        }

        /// <summary>The builtin that __reduce__ answers with.</summary>
        protected virtual string ReduceBuiltin => "iter";

        // (iter, (sequence,), index)
        protected static PyObject ReduceIter(PyObject sequence, long index) => new PyTuple(new PyObject[]
            { Builtin("iter"), new PyTuple(new[] { sequence }), new PyInt(index) });

        // (iter, (empty,)): what an exhausted iterator pickles as
        protected static PyObject ReduceExhausted(PyObject empty) => new PyTuple(new PyObject[]
            { Builtin("iter"), new PyTuple(new[] { empty }) });

        protected static System.Numerics.BigInteger IndexValue(PyObject state) =>
            (PyObject.TryGetIndex(state) ?? throw PyTypeError.Create("an integer is required")).Value;

        // PyLong_AsSsize_t and the silent clipping of every __setstate__
        protected static long ClippedIndex(PyObject state, long length)
        {
            var value = IndexValue(state);
            if (value < 0)
                return 0;
            return value > length ? length : (long)value;
        }

        // type_ready of the iterator types that are made on demand
        internal static void InstallMethods(PyType type, bool lengthHint, bool reduce, bool setState)
        {
            type.TypeDict["__iter__"] = new PyWrapperDescriptor("__iter__", type, (self, args, kwargs) => self);
            type.TypeDict["__next__"] = new PyWrapperDescriptor("__next__", type, (self, args, kwargs) => self.IterNext());
            if (lengthHint)
                type.TypeDict["__length_hint__"] = new PyMethodDescriptor("__length_hint__", type,
                    (self, args, kwargs) => ((PyIterator)self).RemainingHint());
            if (reduce)
                type.TypeDict["__reduce__"] = new PyMethodDescriptor("__reduce__", type,
                    (self, args, kwargs) => ReduceAfterLookup(self));
            if (setState)
                type.TypeDict["__setstate__"] = new PyMethodDescriptor("__setstate__", type, (self, args, kwargs) =>
                {
                    ((PyIterator)self).SetState(args[0]);
                    return PyNone.Instance;
                });
        }

        internal static void InstallByName(PyType type)
        {
            switch (type.Name)
            {
                case "list_iterator":
                case "list_reverseiterator":
                case "tuple_iterator":
                case "str_ascii_iterator":
                case "str_iterator":
                case "bytes_iterator":
                case "bytearray_iterator":
                case "range_iterator":
                case "longrange_iterator":
                case "iterator":
                    InstallMethods(type, true, true, true);
                    break;
                case "dict_keyiterator":
                case "dict_valueiterator":
                case "dict_itemiterator":
                case "dict_reversekeyiterator":
                case "dict_reversevalueiterator":
                case "dict_reverseitemiterator":
                case "set_iterator":
                    InstallMethods(type, true, true, false);
                    break;
                case "enumerate":
                    InstallMethods(type, false, true, false);
                    break;
                // repeat_methods: __length_hint__ beside __reduce__.
                case "repeat":
                    InstallMethods(type, true, false, false);
                    break;
                default:
                    InstallMethods(type, false, false, false);
                    break;
            }
        }

        static PyIterator()
        {
            InitializeIteratorDescriptors();
        }

        private static void InitializeIteratorDescriptors()
        {
            var iterType = PyType.IteratorType;

            // tp_iternext: a slot wrapper, whose arity CheckSlotArguments checks.
            iterType.TypeDict["__next__"] = new PyWrapperDescriptor(
                "__next__", iterType,
                (self, args, kwargs) => {
                    if (self is not PyIterator iterator)
                        throw PyTypeError.Create($"descriptor '__next__' requires a 'iterator' object but received a '{self.GetTypeName()}'");
                    return iterator.IterNext();
                });

            // __iter__ method descriptor
            // tp_iter: a slot wrapper, PyObject_SelfIter.
            iterType.TypeDict["__iter__"] = new PyWrapperDescriptor(
                "__iter__", iterType,
                (self, args, kwargs) => {
                    if (self is not PyIterator iterator)
                        throw PyTypeError.Create($"descriptor '__iter__' requires a 'iterator' object but received a '{self.GetTypeName()}'");
                    return iterator;
                });

            // CPython exposes tp_iternext/tp_iter of map/zip/filter/callable_iterator/
            // reversed as slot wrappers bound to the concrete type (measured wording:
            // "descriptor '__next__' requires a 'map' object but received a 'int'",
            // arity error "expected 0 arguments, got 1").
            foreach (var iterTy in new[]
            {
                PyType.MapType, PyType.ZipType, PyType.FilterType,
                PyType.CallableIteratorType, PyType.ReversedType
            })
            {
                var owner = iterTy;
                owner.TypeDict["__next__"] = new PyWrapperDescriptor(
                    "__next__", owner,
                    (self, args, kwargs) => {
                        if (args.Length != 0)
                            throw PyTypeError.Create($"expected 0 arguments, got {args.Length}");
                        if (self is not PyIterator iterator)
                            throw PyTypeError.Create($"descriptor '__next__' requires a '{owner.Name}' object but received a '{self.GetTypeName()}'");
                        return iterator.IterNext();
                    },
                    minArgs: 0, maxArgs: 0);
                owner.TypeDict["__iter__"] = new PyWrapperDescriptor(
                    "__iter__", owner,
                    (self, args, kwargs) => {
                        if (args.Length != 0)
                            throw PyTypeError.Create($"expected 0 arguments, got {args.Length}");
                        if (self is not PyIterator iterator)
                            throw PyTypeError.Create($"descriptor '__iter__' requires a '{owner.Name}' object but received a '{self.GetTypeName()}'");
                        return iterator;
                    },
                    minArgs: 0, maxArgs: 0);
                owner.TypeDict["__reduce__"] = new PyMethodDescriptor("__reduce__", owner,
                    (self, args, kwargs) => ReduceAfterLookup(self));
            }
            PyType.ReversedType.TypeDict["__length_hint__"] = new PyMethodDescriptor("__length_hint__", PyType.ReversedType,
                (self, args, kwargs) => ((PyIterator)self).RemainingHint());
            PyType.ZipType.TypeDict["__setstate__"] = new PyMethodDescriptor("__setstate__", PyType.ZipType, (self, args, kwargs) =>
            {
                ((PyIterator)self).SetState(args[0]);
                return PyNone.Instance;
            });
            PyType.ReversedType.TypeDict["__setstate__"] = new PyMethodDescriptor("__setstate__", PyType.ReversedType, (self, args, kwargs) =>
            {
                ((PyIterator)self).SetState(args[0]);
                return PyNone.Instance;
            });
        }

        // CPython names each iterator family its own type (list_iterator, ...);
        // type(it).__name__ and repr both derive from this name via
        // PyType.GetIteratorType. Subclasses override IteratorName.
        protected virtual string IteratorName => "iterator";
        public override string GetTypeName() => IteratorName;
        // Py_TYPE: the iterator family's type, looked up by name once per object.
        private PyType _familyType;
        public override PyType GetPyType() => _customType ?? (_familyType ??= PyType.GetIteratorType(IteratorName));

        /// <summary>
        /// 다음 값 반환 (Python __next__ 메서드)
        /// </summary>
        protected internal abstract override PyObject IterNext();


        /// <summary>
        /// 자기 자신을 반환 (Python __iter__ 메서드) - PyObject 오버라이드
        /// </summary>
        protected internal override PyObject Iter() => this;

        /// <summary>
        /// CPython 3.12: iterator objects are NOT callable (list_iterator() raises
        /// TypeError: 'list_iterator' object is not callable). __next__ is reached
        /// only via next(it) / the iteration protocol, never by calling the object.
        /// (Historical note: this class used to forward Call() to Next(), which made
        /// `genexp()` return items and leak StopIteration.)
        /// </summary>
        public override bool IsCallable() => false;

        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            throw PyTypeError.Create($"'{GetTypeName()}' object is not callable");
        }

        public override bool PyBoolValue() => true;

        /// <summary>
        /// CPython 3.12: Use descriptor protocol for attribute access
        /// </summary>
        public override PyObject GetAttribute(string name)
        {
            // A Python subclass keeps its attributes beside the object, which PyObject's route reads.
            if (_customType is PyClass)
                return base.GetAttribute(name);
            return GenericGetAttribute(name);
        }

        #region IDisposable Implementation

        private bool _disposed = false;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // 파생 클래스에서 오버라이드하여 리소스 정리
                }
                _disposed = true;
            }
        }

        #endregion
        
        #region Evaluate Method (NotImplementedException)

        public PyObject Evaluate(PyScope scope)
        {
            throw new NotImplementedException("PyIterator.Evaluate() - 나중에 구현예정");
        }

        #endregion
    }

    /// <summary>
    /// 리스트 이터레이터 구현
    /// </summary>
    public class PyListIterator : PyIterator
    {
        private readonly PyList _list;
        private int _index;
        private static int _totalCallCount = 0;
        private readonly int _listLength;
        private readonly string _typeName;

        // Reused for bytes_iterator / reversed-key iteration, so the reported type
        // name is parameterised (default list_iterator).
        internal override PyObject RemainingHint() => new PyInt(_exhausted ? 0 : Math.Max(0, _list.RawCount() - _index));

        private bool _exhausted;
        private readonly PyObject _origin;

        // listiter_reduce_general: the sequence it walks, or an empty one when done
        internal override PyObject Reduce()
        {
            // dictreviter_reduce: a list of what is left
            if (_typeName.StartsWith("dict_", StringComparison.Ordinal))
            {
                var left = new List<PyObject>();
                for (int i = _index; !_exhausted && i < _list.RawCount(); i++)
                    left.Add(_list.GetItem(i));
                return ReduceExhausted(new PyList(left.ToArray()));
            }
            if (_exhausted)
                return ReduceExhausted(_origin != null ? new PyTuple(new PyObject[0]) : new PyList());
            return ReduceIter(_origin ?? _list, _index);
        }

        internal override void SetState(PyObject state)
        {
            if (!_exhausted)
                _index = (int)ClippedIndex(state, _list.RawCount());
        }

        public PyListIterator(PyList list, string typeName = "list_iterator", PyObject origin = null)
        {
            _origin = origin;
            _list = list ?? throw new ArgumentNullException(nameof(list));
            _index = 0;
            _listLength = _list.Length();
            _typeName = typeName;
        }

        protected internal override PyObject IterNext()
        {
            if (_exhausted || _index >= _list.RawCount())
            {
                _exhausted = true;
                throw PyStopIteration.Create();
            }

            var item = _list.GetItem(_index);
            _index++;
            return item;
        }

        protected override string IteratorName => _typeName;
    }

    /// <summary>
    /// 튜플 이터레이터 구현
    /// </summary>
    /// <summary>
    /// Drives an arbitrary Python iterator object as a CLR PyIterator. Needed because a few builtin
    /// types override GetIterator with the covariant PyIterator return type, while a user __iter__ on
    /// a subclass of tuple or bytes may return any object with __next__ - a PyClassInstance, which is
    /// an iterator in CPython's terms (tp_iternext dispatches to __next__) but is not a PyIterator.
    /// </summary>
    public class PyObjectIteratorShim : PyIterator
    {
        private readonly PyObject _inner;

        public PyObjectIteratorShim(PyObject inner)
        {
            _inner = inner;
        }

        protected internal override PyObject IterNext() => _inner.Next();

        /// <summary>What iter() returned: a user object with __next__ is an iterator as much as a CLR one.</summary>
        internal static PyIterator Of(PyObject iterator) => iterator as PyIterator ?? new PyObjectIteratorShim(iterator);
    }

    public class PyTupleIterator : PyIterator
    {
        internal override PyObject RemainingHint() => new PyInt(Math.Max(0, _tuple.Items.Length - _index));

        internal override PyObject Reduce() =>
            _index < _tuple.Items.Length ? ReduceIter(_tuple, _index) : ReduceExhausted(new PyTuple(new PyObject[0]));

        internal override void SetState(PyObject state)
        {
            if (_index < _tuple.Items.Length)
                _index = (int)ClippedIndex(state, _tuple.Items.Length);
        }

        private readonly PyTuple _tuple;
        private int _index;

        public PyTupleIterator(PyTuple tuple)
        {
            _tuple = tuple ?? throw new ArgumentNullException(nameof(tuple));
            _index = 0;
        }

        protected internal override PyObject IterNext()
        {
            #if DEBUG_LOG
            Console.WriteLine($"🔧 PyTupleIterator.Next(): _index={_index}, _tuple.Length()={_tuple.Length()}");
            #endif

            if (_index >= _tuple.Items.Length)
            {
                #if DEBUG_LOG
                Console.WriteLine($"🔚 PyTupleIterator.Next(): StopIteration (_index={_index} >= length={_tuple.Length()})");
                #endif
                throw PyStopIteration.Create();
            }

            var result = _tuple.Items[_index++];
            #if DEBUG_LOG
            Console.WriteLine($"✅ PyTupleIterator.Next(): 반환값={result}, 새로운 _index={_index}");
            #endif

            return result;
        }

        protected override string IteratorName => "tuple_iterator";
    }

    /// <summary>
    /// 문자열 이터레이터 구현
    /// </summary>
    public class PyStringIterator : PyIterator
    {
        private readonly string _string;
        private int _index;
        private readonly string _name;

        // Positions are characters to Python and code units here.
        private long Characters(int to)
        {
            long count = 0;
            for (int i = 0; i < to; i += PyString.PairWidth(_string, i))
                count++;
            return count;
        }

        internal override PyObject RemainingHint() => new PyInt(Characters(_string.Length) - Characters(_index));

        internal override PyObject Reduce() =>
            _index < _string.Length ? ReduceIter(new PyString(_string), Characters(_index)) : ReduceExhausted(new PyString(""));

        internal override void SetState(PyObject state)
        {
            if (_index >= _string.Length)
                return;
            long target = ClippedIndex(state, Characters(_string.Length));
            _index = 0;
            for (long i = 0; i < target; i++)
                _index += PyString.PairWidth(_string, _index);
        }

        public PyStringIterator(PyString pyString)
        {
            _string = pyString?.Value ?? throw new ArgumentNullException(nameof(pyString));
            _index = 0;
            // unicode_iter: PyUnicode_IS_COMPACT_ASCII picks str_ascii_iterator. A subclass
            // instance is never compact (unicode_subtype_new), so it has str_iterator.
            bool ascii = pyString.GetType() == typeof(PyString);
            foreach (var ch in _string) { if (ch > 127) { ascii = false; break; } }
            _name = ascii ? "str_ascii_iterator" : "str_iterator";
        }

        // CPython iter(str) yields characters (Objects/unicodeobject.c unicode_iterator next with
        // PyUnicode_READ), so an astral character comes out whole instead of as two halves.
        protected internal override PyObject IterNext()
        {
            if (_index >= _string.Length)
                throw PyStopIteration.Create();

            int width = PyString.PairWidth(_string, _index);
            var ch = new PyString(_string.Substring(_index, width));
            _index += width;
            return ch;
        }

        protected override string IteratorName => _name;
    }

    /// <summary>
    /// Range 이터레이터 구현
    /// </summary>
    public class PyRangeIterator : PyIterator
    {
        internal override PyObject RemainingHint() => new PyInt((System.Numerics.BigInteger)_remaining);

        // rangeiter_reduce
        internal override PyObject Reduce()
        {
            var stop = (System.Numerics.BigInteger)_current + (System.Numerics.BigInteger)_remaining * _step;
            var range = PyType.RangeType.Call(new PyObject[] { new PyInt(_current), new PyInt(stop), new PyInt(_step) }, null);
            return new PyTuple(new PyObject[] { Builtin("iter"), new PyTuple(new[] { range }), PyNone.Instance });
        }

        // rangeiter_setstate
        internal override void SetState(PyObject state)
        {
            ulong index = (ulong)ClippedIndex(state, _remaining > long.MaxValue ? long.MaxValue : (long)_remaining);
            _current = unchecked(_current + (long)index * _step);
            _remaining -= index;
        }

        private long _current;
        private readonly long _step;
        private ulong _remaining;

        public PyRangeIterator(PyRange range)
        {
            if (range == null) throw new ArgumentNullException(nameof(range));
            // Public C# callers must obtain arbitrary-precision iterators via GetIterator.
            if (range is PyBigRange)
                throw new ArgumentException("Use range.GetIterator() for an arbitrary-precision range", nameof(range));
            _current = range.NativeStart;
            _step = range.NativeStep;
            _remaining = range.LengthUnsigned();
        }

        protected internal override PyObject IterNext()
        {
            if (_remaining == 0)
                throw PyStopIteration.Create();
            
            var result = new PyInt(_current);
            _current = unchecked(_current + _step);
            _remaining--;
            return result;
        }

        protected override string IteratorName => "range_iterator";
    }

    /// <summary>
    /// 집합 이터레이터 구현
    /// </summary>
    public class PySetIterator : PyIterator
    {
        private readonly IEnumerator<PyObject> _enumerator;
        private readonly Queue<PyObject> _read = new Queue<PyObject>();
        private readonly PyObject _set;
        private readonly int _size;
        private int _left;

        public PySetIterator(PySet set)
        {
            if (set == null) throw new ArgumentNullException(nameof(set));
            _enumerator = set.Items.GetEnumerator();
            _set = set;
            _size = _left = set.Length();
        }

        public PySetIterator(PyFrozenSet frozenSet)
        {
            if (frozenSet == null) throw new ArgumentNullException(nameof(frozenSet));
            _enumerator = frozenSet.Items.GetEnumerator();
            _set = frozenSet;
            _size = _left = frozenSet.Length();
        }

        // setiter_iternext
        protected internal override PyObject IterNext()
        {
            if (_left == 0 && _read.Count == 0)
                throw PyStopIteration.Create();
            if (_set.Length() != _size)
            {
                _left = 0;
                _read.Clear();
                throw PyRuntimeError.Create("Set changed size during iteration");
            }
            _left--;
            if (_read.Count != 0)
                return _read.Dequeue();
            if (!_enumerator.MoveNext())
                throw PyStopIteration.Create();
            return _enumerator.Current;
        }

        internal override PyObject RemainingHint() => new PyInt(_left);

        // setiter_reduce: a list of what is left
        internal override PyObject Reduce()
        {
            while (_read.Count < _left && _enumerator.MoveNext())
                _read.Enqueue(_enumerator.Current);
            return ReduceExhausted(new PyList(_read.ToArray()));
        }

        protected override string IteratorName => "set_iterator";

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _enumerator?.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// 딕셔너리 키 이터레이터 구현
    /// </summary>
    public abstract class PyDictIterator : PyIterator
    {
        private PyDict _dictionary;
        private readonly int _initialSize;
        private int _position;
        private int _remaining;
        private bool _finished;
        private bool _sizeFailed;

        protected PyDictIterator(PyDict dict)
        {
            if (dict == null) throw new ArgumentNullException(nameof(dict));
            _dictionary = dict;
            _initialSize = dict.Length();
            _remaining = _initialSize;
        }

        protected internal override PyObject IterNext()
        {
            if (_finished) throw PyStopIteration.Create();
            if (_sizeFailed || _dictionary.Length() != _initialSize)
            {
                _sizeFailed = true;
                throw PyRuntimeError.Create("dictionary changed size during iteration");
            }
            if (!_dictionary.InternalDict.NextEntry(ref _position, out var entry))
            {
                Finish();
                throw PyStopIteration.Create();
            }
            if (_remaining == 0)
            {
                Finish();
                throw PyRuntimeError.Create("dictionary keys changed during iteration");
            }
            _remaining--;
            return ProjectEntry(entry.Key, entry.Value);
        }

        // dictiter_len: nothing is left once the dict is gone or no longer the size it was.
        internal override PyObject RemainingHint() =>
            new PyInt(_finished || _sizeFailed || _dictionary.Length() != _initialSize ? 0 : _remaining);

        // dictiter_reduce: a list of what is left
        internal override PyObject Reduce()
        {
            var left = new List<PyObject>();
            if (!_finished && !_sizeFailed)
            {
                int position = _position;
                while (_dictionary.InternalDict.NextEntry(ref position, out var entry))
                    left.Add(ProjectEntry(entry.Key, entry.Value));
            }
            return ReduceExhausted(new PyList(left.ToArray()));
        }

        protected abstract PyObject Project(PyObject key, PyDict dictionary);
        protected virtual PyObject ProjectEntry(PyObject key, PyObject value) => Project(key, _dictionary);

        private void Finish()
        {
            _finished = true;
            _dictionary = null;
        }

    }

    public class PyDictKeyIterator : PyDictIterator
    {
        public PyDictKeyIterator(PyDict dict) : base(dict) { }
        protected override PyObject Project(PyObject key, PyDict dictionary) => key;
        protected override string IteratorName => "dict_keyiterator";
    }

    /// <summary>
    /// 딕셔너리 값 이터레이터 구현
    /// </summary>
    public class PyDictValueIterator : PyDictIterator
    {
        public PyDictValueIterator(PyDict dict) : base(dict) { }
        protected override PyObject Project(PyObject key, PyDict dictionary) => dictionary.InternalDict.StoredValue(key);
        protected override PyObject ProjectEntry(PyObject key, PyObject value) => value;
        protected override string IteratorName => "dict_valueiterator";
    }

    /// <summary>
    /// 딕셔너리 아이템 이터레이터 구현
    /// </summary>
    public class PyDictItemIterator : PyDictIterator
    {
        public PyDictItemIterator(PyDict dict) : base(dict) { }
        protected override PyObject Project(PyObject key, PyDict dictionary) =>
            TupleCache.CreatePair(key, dictionary.InternalDict.StoredValue(key));
        protected override PyObject ProjectEntry(PyObject key, PyObject value) => TupleCache.CreatePair(key, value);
        protected override string IteratorName => "dict_itemiterator";
    }

    /// <summary>
    /// 제네릭 이터레이터 구현 (임의의 시퀀스용)
    /// </summary>
    public class PyGenericIterator : PyIterator
    {
        private PyObject _sequence;
        private long _index;

        // iter_len
        internal override PyObject RemainingHint()
        {
            if (_sequence == null)
                return new PyInt(0);
            long size;
            try
            {
                size = _sequence.Length();
            }
            catch (PythonException error) when (error.PyException is PyTypeError)
            {
                return PyNotImplemented.Instance;
            }
            return new PyInt(Math.Max(0, size - _index));
        }

        // iter_reduce
        internal override PyObject Reduce() =>
            _sequence != null ? ReduceIter(_sequence, _index) : ReduceExhausted(new PyTuple(new PyObject[0]));

        // iter_setstate
        internal override void SetState(PyObject state)
        {
            if (_sequence != null)
                _index = ClippedIndex(state, long.MaxValue);
        }

        public PyGenericIterator(PyObject sequence)
        {
            _sequence = sequence ?? throw new ArgumentNullException(nameof(sequence));
        }

        protected internal override PyObject IterNext()
        {
            if (_sequence == null) throw PyStopIteration.Create();
            if (_index == long.MaxValue) throw PyOverflowError.Create("iter index too large");
            try
            {
                var method = _sequence.LookupSpecialMethodUncached("__getitem__");
                var result = method != null
                    ? _sequence.BindSpecialMethod(method).Call(new PyObject[] { new PyInt(_index) }, null)
                    : _sequence.GetItem(new PyInt(_index));
                _index++;
                return result;
            }
            catch (PythonException ex) when (ex.PyException is PyIndexError || ex.PyException is PyStopIteration)
            {
                // Sequence exhaustion is permanent and releases the sequence.
                _sequence = null;
                throw PyStopIteration.Create();
            }
        }

        // Generic sequence iterator: CPython reports the plain "iterator" name.
    }

    /// <summary>
    /// 빈 이터레이터 (완료된 이터레이터)
    /// </summary>
    public class PyEmptyIterator : PyIterator
    {
        public static readonly PyEmptyIterator Instance = new PyEmptyIterator();

        private PyEmptyIterator() { }

        protected internal override PyObject IterNext()
        {
            throw PyStopIteration.Create();
        }

        protected override string IteratorName => "empty_iterator";
    }

    /// <summary>
    /// enumerate 이터레이터 구현 - CPython 3.12 호환
    /// </summary>
    public class PyEnumerateIterator : PyIterator
    {
        private readonly PyObject _iterator;
        private System.Numerics.BigInteger _index;

        // enum_reduce
        internal override PyObject Reduce() => new PyTuple(new PyObject[]
            { GetPyType(), new PyTuple(new PyObject[] { _iterator, new PyInt(_index) }) });

        public PyEnumerateIterator(PyObject iterable, System.Numerics.BigInteger start = default)
        {
            if (iterable == null) throw new ArgumentNullException(nameof(iterable));
            _iterator = iterable.GetIterator();
            _index = start;
        }

        protected internal override PyObject IterNext()
        {
            var item = _iterator.Next();
            var result = new PyTuple(new PyInt(_index), item);
            _index++;
            return result;
        }

        protected override string IteratorName => "enumerate";

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                (_iterator as PyIterator)?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
