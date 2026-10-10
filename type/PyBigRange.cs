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
        public override bool PyBoolValue() => !_length.IsZero;

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

        public override PyObject GetIterator() => new BigRangeIterator(this);

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

            public override PyObject Next()
            {
                if (_remaining.IsZero) throw PyStopIteration.Create();
                var result = new PyInt(_current);
                _remaining--;
                if (!_remaining.IsZero) _current += _step;
                return result;
            }

            public override PyString ToRepr() => new PyString("<longrange_iterator object>");
        }
    }
}
