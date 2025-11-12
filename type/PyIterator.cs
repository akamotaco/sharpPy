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
        }

        public override PyType GetPyType() => PyType.IteratorType;
        public override string GetTypeName() => "iterator";

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
        /// iterator는 항상 호출 가능 (__next__)
        /// </summary>
        public override bool IsCallable() => true;

        /// <summary>
        /// __next__() 호출
        /// </summary>
        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 0)
                throw PyTypeError.Create("__next__() takes no arguments");
            return Next();
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

        public PyListIterator(PyList list)
        {
            _list = list ?? throw new ArgumentNullException(nameof(list));
            _index = 0;
        }

        public override PyObject Next()
        {
            if (_index >= _list.Length())
                throw PyStopIteration.Create();
            
            var item = _list.GetItem(_index);
            _index++;
            return item;
        }

        public override PyString ToRepr() => new PyString($"<list_iterator object>");
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

        public override PyString ToRepr() => new PyString($"<tuple_iterator object>");
    }

    /// <summary>
    /// 문자열 이터레이터 구현
    /// </summary>
    public class PyStringIterator : PyIterator
    {
        private readonly string _string;
        private int _index;

        public PyStringIterator(PyString pyString)
        {
            _string = pyString?.Value ?? throw new ArgumentNullException(nameof(pyString));
            _index = 0;
        }

        public override PyObject Next()
        {
            if (_index >= _string.Length)
                throw PyStopIteration.Create();
            
            return new PyString(_string[_index++].ToString());
        }

        public override PyString ToRepr() => new PyString($"<str_iterator object>");
    }

    /// <summary>
    /// Range 이터레이터 구현
    /// </summary>
    public class PyRangeIterator : PyIterator
    {
        private readonly PyRange _range;
        private int _current;
        private readonly int _step;
        private readonly int _stop;

        public PyRangeIterator(PyRange range)
        {
            _range = range ?? throw new ArgumentNullException(nameof(range));
            _current = range.Start;
            _step = range.Step;
            _stop = range.Stop;
        }

        public override PyObject Next()
        {
            if (_step > 0 ? _current >= _stop : _current <= _stop)
                throw PyStopIteration.Create();
            
            var result = new PyInt(_current);
            _current += _step;
            return result;
        }

        public override PyString ToRepr() => new PyString($"<range_iterator object>");
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

        public override PyString ToRepr() => new PyString($"<set_iterator object>");

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
    public class PyDictKeyIterator : PyIterator
    {
        private readonly IEnumerator<PyObject> _enumerator;

        public PyDictKeyIterator(PyDict dict)
        {
            if (dict == null) throw new ArgumentNullException(nameof(dict));
            _enumerator = ((IEnumerable<PyObject>)dict.Keys().Items).GetEnumerator();
        }

        public override PyObject Next()
        {
            if (!_enumerator.MoveNext())
                throw PyStopIteration.Create();
            
            return _enumerator.Current;
        }

        public override PyString ToRepr() => new PyString($"<dict_keyiterator object>");

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
    /// 딕셔너리 값 이터레이터 구현
    /// </summary>
    public class PyDictValueIterator : PyIterator
    {
        private readonly IEnumerator<PyObject> _enumerator;

        public PyDictValueIterator(PyDict dict)
        {
            if (dict == null) throw new ArgumentNullException(nameof(dict));
            _enumerator = ((IEnumerable<PyObject>)dict.Values().Items).GetEnumerator();
        }

        public override PyObject Next()
        {
            if (!_enumerator.MoveNext())
                throw PyStopIteration.Create();
            
            return _enumerator.Current;
        }

        public override PyString ToRepr() => new PyString($"<dict_valueiterator object>");

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
    /// 딕셔너리 아이템 이터레이터 구현
    /// </summary>
    public class PyDictItemIterator : PyIterator
    {
        private readonly IEnumerator<PyTuple> _enumerator;

        public PyDictItemIterator(PyDict dict)
        {
            if (dict == null) throw new ArgumentNullException(nameof(dict));
            _enumerator = dict.Items().Items.Cast<PyTuple>().GetEnumerator();
        }

        public override PyObject Next()
        {
            if (!_enumerator.MoveNext())
                throw PyStopIteration.Create();
            
            return _enumerator.Current;
        }

        public override PyString ToRepr() => new PyString($"<dict_itemiterator object>");

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
    /// 제네릭 이터레이터 구현 (임의의 시퀀스용)
    /// </summary>
    public class PyGenericIterator : PyIterator
    {
        private readonly PyObject _sequence;
        private int _index;

        public PyGenericIterator(PyObject sequence)
        {
            _sequence = sequence ?? throw new ArgumentNullException(nameof(sequence));
            _index = 0;
        }

        public override PyObject Next()
        {
            try
            {
                var result = _sequence.GetAttribute("__getitem__").Call(new PyObject[] { new PyInt(_index) }, null);
                _index++;
                return result;
            }
            catch (PythonException ex) when (ex.PyException is PyIndexError)
            {
                throw PyStopIteration.Create();
            }
            catch (Exception)
            {
                throw PyStopIteration.Create();
            }
        }

        public override PyString ToRepr() => new PyString($"<iterator object>");
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

        public override PyString ToRepr() => new PyString($"<empty_iterator object>");
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

        public override PyString ToRepr() => new PyString("<enumerate object>");

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