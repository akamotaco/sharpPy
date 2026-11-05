using System;
using System.Collections.Generic;
using System.Numerics;

namespace SharpPy
{
    // Same Python type as PyRange. Only out-of-Int64 ranges pay for these fields.
    internal sealed class PyBigRange : PyRange
    {
        private readonly BigInteger _start, _stop, _step, _length;
        public override BigInteger Start => _start;
        public override BigInteger Stop => _stop;
        public override BigInteger Step => _step;
        internal override BigInteger ExactLength => _length;

        internal PyBigRange(BigInteger start, BigInteger stop, BigInteger step) : base(0)
        {
            if (step.IsZero) throw PyValueError.Create("range() arg 3 must not be zero");
            _start = start;
            _stop = stop;
            _step = step;
            _length = step.Sign > 0
                ? (start < stop ? 1 + (stop - start - 1) / step : BigInteger.Zero)
                : (start > stop ? 1 + (start - stop - 1) / -step : BigInteger.Zero);
        }

        public override PyInt GetItem(long index) => GetExactItem(index);
        protected internal override bool Bool() => !_length.IsZero;

        public override long LengthLong()
        {
            if (_length > long.MaxValue)
                throw PyOverflowError.Create("Python int too large to convert to C ssize_t");
            return (long)_length;
        }

        internal override long PythonLength()
        {
            if (_length > (long)nint.MaxValue)
                throw PyOverflowError.Create("Python int too large to convert to C ssize_t");
            return (long)_length;
        }

        public override IEnumerable<PyInt> GetValues()
        {
            if (_step.Sign > 0)
            {
                for (BigInteger value = _start; value < _stop; value += _step)
                    yield return new PyInt(value);
            }
            else
            {
                for (BigInteger value = _start; value > _stop; value += _step)
                    yield return new PyInt(value);
            }
        }

        protected internal override PyObject Iter() => new BigRangeIterator(this);

        private sealed class BigRangeIterator : PyIterator
        {
            private BigInteger _current, _remaining;
            private readonly BigInteger _step;

            internal BigRangeIterator(PyBigRange range)
            {
                _current = range._start;
                _step = range._step;
                _remaining = range._length;
            }

            protected internal override PyObject IterNext()
            {
                if (_remaining.IsZero) throw PyStopIteration.Create();
                var result = new PyInt(_current);
                _remaining--;
                if (!_remaining.IsZero) _current += _step;
                return result;
            }

            protected override string IteratorName => "longrange_iterator";

            internal override PyObject RemainingHint() => new PyInt(_remaining);

            // longrangeiter_reduce
            internal override PyObject Reduce()
            {
                var stop = _current + _remaining * _step;
                var range = PyType.RangeType.Call(new PyObject[] { new PyInt(_current), new PyInt(stop), new PyInt(_step) }, null);
                return new PyTuple(new PyObject[] { Builtin("iter"), new PyTuple(new[] { range }), PyNone.Instance });
            }

            // longrangeiter_setstate
            internal override void SetState(PyObject state)
            {
                var index = IndexValue(state);
                if (index < 0)
                    index = 0;
                else if (index > _remaining)
                    index = _remaining;
                _current += index * _step;
                _remaining -= index;
            }
        }
    }
}
