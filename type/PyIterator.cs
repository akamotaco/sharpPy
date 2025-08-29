using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// Python iterator protocol 구현 - __iter__ 및 __next__ 지원
    /// </summary>
    public abstract class PyIterator : PyObject, IDisposable
    {
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
        /// iterator는 항상 호출 가능 (__next__)
        /// </summary>
        public override bool IsCallable() => true;

        /// <summary>
        /// __next__() 호출
        /// </summary>
        public override PyObject Call(params PyObject[] args)
        {
            if (args.Length != 0)
                throw PyTypeError.Create("__next__() takes no arguments");
            return Next();
        }

        public override bool PyBoolValue() => true;

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

        public override string ToRepr() => $"<list_iterator object>";
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
            if (_index >= _tuple.Length())
                throw PyStopIteration.Create();
            
            return _tuple.Items[_index++];
        }

        public override string ToRepr() => $"<tuple_iterator object>";
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

        public override string ToRepr() => $"<str_iterator object>";
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

        public override string ToRepr() => $"<range_iterator object>";
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

        public override string ToRepr() => $"<set_iterator object>";

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

        public override string ToRepr() => $"<dict_keyiterator object>";

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

        public override string ToRepr() => $"<dict_valueiterator object>";

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

        public override string ToRepr() => $"<dict_itemiterator object>";

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
                var result = _sequence.GetAttribute("__getitem__").Call(new PyInt(_index));
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

        public override string ToRepr() => $"<iterator object>";
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

        public override string ToRepr() => $"<empty_iterator object>";
    }
}