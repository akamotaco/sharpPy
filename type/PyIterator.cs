using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// Python iterator protocol 구현 - __iter__ 및 __next__ 지원
    /// </summary>
    public abstract class PyIterator : PyObject, IDisposable
    {
        static PyIterator()
        {
            InitializeIteratorDescriptors();
        }

        private static void InitializeIteratorDescriptors()
        {
            var iterType = PyType.IteratorType;

            // __next__ method descriptor
            iterType.TypeDict["__next__"] = new PyMethodDescriptor(
                "__next__", iterType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create("__next__() takes no arguments");
                    if (self is not PyIterator iterator)
                        throw PyTypeError.Create($"descriptor '__next__' requires a 'iterator' object but received a '{self.GetTypeName()}'");
                    return iterator.Next();
                },
                minArgs: 0, maxArgs: 0
            );

            // __iter__ method descriptor
            iterType.TypeDict["__iter__"] = new PyMethodDescriptor(
                "__iter__", iterType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create("__iter__() takes no arguments");
                    if (self is not PyIterator iterator)
                        throw PyTypeError.Create($"descriptor '__iter__' requires a 'iterator' object but received a '{self.GetTypeName()}'");
                    return iterator;
                },
                minArgs: 0, maxArgs: 0
            );

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
                        return iterator.Next();
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
            }
        }

        // CPython names each iterator family its own type (list_iterator, ...);
        // type(it).__name__ and repr both derive from this name via
        // PyType.GetIteratorType. Subclasses override IteratorName.
        protected virtual string IteratorName => "iterator";
        public override string GetTypeName() => IteratorName;
        public override PyType GetPyType() => PyType.GetIteratorType(IteratorName);

        internal override bool IsIterator => true;

        /// <summary>
        /// 다음 값 반환 (Python __next__ 메서드)
        /// </summary>
        public abstract override PyObject Next();

        /// <summary>
        /// 자기 자신을 반환 (Python __iter__ 메서드)
        /// </summary>
        public virtual PyIterator Iter() => this;

        /// <summary>
        /// 자기 자신을 반환 (Python __iter__ 메서드) - PyObject 오버라이드
        /// </summary>
        public override PyObject GetIterator() => this;

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
        public PyListIterator(PyList list, string typeName = "list_iterator")
        {
            _list = list ?? throw new ArgumentNullException(nameof(list));
            _index = 0;
            _listLength = _list.Length();
            _typeName = typeName;
        }

        public override PyObject Next()
        {
            if (_index >= _list.Length())
            {
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
    public class PyTupleIterator : PyIterator
    {
        private readonly PyTuple _tuple;
        private int _index;

        public PyTupleIterator(PyTuple tuple)
        {
            _tuple = tuple ?? throw new ArgumentNullException(nameof(tuple));
            _index = 0;
        }

        public override PyObject Next()
        {
            #if DEBUG_LOG
            Console.WriteLine($"🔧 PyTupleIterator.Next(): _index={_index}, _tuple.Length()={_tuple.Length()}");
            #endif

            if (_index >= _tuple.Length())
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

        public PyStringIterator(PyString pyString)
        {
            _string = pyString?.Value ?? throw new ArgumentNullException(nameof(pyString));
            _index = 0;
            // CPython: iterating an ASCII string yields str_ascii_iterator; any
            // non-ASCII code point yields str_iterator.
            bool ascii = true;
            foreach (var ch in _string) { if (ch > 127) { ascii = false; break; } }
            _name = ascii ? "str_ascii_iterator" : "str_iterator";
        }

        public override PyObject Next()
        {
            if (_index >= _string.Length)
                throw PyStopIteration.Create();
            
            return new PyString(_string[_index++].ToString());
        }

        protected override string IteratorName => _name;
    }

    /// <summary>
    /// Range 이터레이터 구현
    /// </summary>
    public class PyRangeIterator : PyIterator
    {
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

        public override PyObject Next()
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

        public PySetIterator(PySet set)
        {
            if (set == null) throw new ArgumentNullException(nameof(set));
            _enumerator = set.Items.GetEnumerator();
        }

        public PySetIterator(PyFrozenSet frozenSet)
        {
            if (frozenSet == null) throw new ArgumentNullException(nameof(frozenSet));
            _enumerator = frozenSet.Items.GetEnumerator();
        }

        public override PyObject Next()
        {
            if (!_enumerator.MoveNext())
                throw PyStopIteration.Create();
            
            return _enumerator.Current;
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

        public override PyObject Next()
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

        public PyGenericIterator(PyObject sequence)
        {
            _sequence = sequence ?? throw new ArgumentNullException(nameof(sequence));
        }

        public override PyObject Next()
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

        public override PyObject Next()
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
        private readonly PyIterator _iterator;
        private long _index;

        public PyEnumerateIterator(PyObject iterable, long start = 0)
        {
            if (iterable == null) throw new ArgumentNullException(nameof(iterable));
            _iterator = iterable.GetIterator() as PyIterator
                ?? throw PyTypeError.Create($"'{iterable.GetTypeName()}' object is not iterable");
            _index = start;
        }

        public override PyObject Next()
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
                _iterator?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
