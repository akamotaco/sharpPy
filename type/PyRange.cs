using System;
using System.Collections.Generic;
using System.Numerics;

namespace SharpPy
{
    /// <summary>
    /// Python range 타입 구현 - range(start, stop, step)
    /// </summary>
    public class PyRange : PyObject
    {
        #region Core Properties

        internal readonly long NativeStart;
        internal readonly long NativeStop;
        internal readonly long NativeStep;
        public virtual BigInteger Start => NativeStart;
        public virtual BigInteger Stop => NativeStop;
        public virtual BigInteger Step => NativeStep;
        internal virtual BigInteger ExactLength => LengthUnsigned();

        public PyRange(long stop) : this(0, stop, 1) { }

        public PyRange(long start, long stop) : this(start, stop, 1) { }

        public PyRange(long start, long stop, long step)
        {
            if (step == 0)
                throw PyValueError.Create("range() arg 3 must not be zero");

            NativeStart = start;
            NativeStop = stop;
            NativeStep = step;
        }

        public override PyType GetPyType() => PyType.RangeType;
        public override string GetTypeName() => "range";

        public override PyObject GetAttribute(string name) => name switch
        {
            "start" => new PyInt(Start),
            "stop" => new PyInt(Stop),
            "step" => new PyInt(Step),
            _ => base.GetAttribute(name)
        };

        #endregion

        #region String Representation

        public override PyString AsString() => ToRepr();

        public override PyString ToRepr()
        {
            if (Step == 1)
                return new PyString($"range({Start}, {Stop})");
            else
                return new PyString($"range({Start}, {Stop}, {Step})");
        }

        #endregion

        #region Hash and Equality

        public override int ToHash()
        {
            // range는 불변이므로 해시 가능
            return HashCode.Combine(Start, Stop, Step);
        }

        protected override PyObject PyEquals(PyObject other)
        {
            return other switch
            {
                PyRange otherRange => PyBool.FromBool(
                    Start == otherRange.Start && 
                    Stop == otherRange.Stop && 
                    Step == otherRange.Step),
                _ => PyBool.False
            };
        }

        #endregion

        #region Range Operations

        /// <summary>
        /// CPython 3.12: Objects/rangeobject.c:580-650 - range_subscript
        /// 인덱스/슬라이스 접근 range[i] / range[start:stop:step]
        /// </summary>
        public override PyObject GetItem(PyObject index)
        {
            if (index is PyInt integer)
                return GetIntegerItem(integer.Value);
            if (index is PySlice slice) return SliceRange(slice);
            integer = PyObject.TryGetIndex(index);
            if (integer == null)
                throw PyTypeError.Create($"range indices must be integers or slices, not {index.GetTypeName()}");
            return GetIntegerItem(integer.Value);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private PyRange SliceRange(PySlice slice)
        {
            // Keep ordinary native slices on the allocation-free int normalizer.
            // A callback step must use ExactIndices so it is evaluated only once.
            ulong nativeLength = this is PyBigRange ? ulong.MaxValue : LengthUnsigned();
            if (nativeLength <= int.MaxValue)
            {
                int step;
                if (slice.Step is PyInt s && s.Value.GetBitLength() < 32) step = (int)s.Value;
                else if (slice.Step == PyNone.Instance) step = 1;
                else if (slice.Step is PyBool boolean) step = boolean.Value ? 1 : 0;
                else return SliceExact(slice);
                var (a, b, stride) = slice.Indices((int)nativeLength, step);
                // Products of normalized int indices and an int step, plus
                // an int start, fit in Int64 without overflow checks.
                if (NativeStart == (int)NativeStart && NativeStep == (int)NativeStep)
                    return new PyRange(NativeStart + a * NativeStep,
                        NativeStart + b * NativeStep, NativeStep * stride);
                return MapWideSlice(a, b, stride);
            }
            return SliceExact(slice);
        }

        private PyRange MapWideSlice(int a, int b, int stride)
        {
            try
            {
                return new PyRange(checked(NativeStart + a * NativeStep),
                    checked(NativeStart + b * NativeStep), checked(NativeStep * stride));
            }
            catch (OverflowException)
            {
                // Reuse normalized values; do not repeat Python callbacks.
                return Create(Start + a * Step, Start + b * Step, Step * stride);
            }
        }

        private PyRange SliceExact(PySlice slice)
        {
            var (start, stop, step) = slice.ExactIndices(ExactLength);
            return Create(Start + start * Step, Start + stop * Step, Step * step);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private PyInt GetIntegerItem(BigInteger index)
        {
            if (this is not PyBigRange && FitsLong(index)) return GetNativeItem((long)index);
            return GetExactItem(index);
        }

        protected PyInt GetExactItem(BigInteger index)
        {
            var length = ExactLength;
            if (index.Sign < 0) index += length;
            if (index.Sign < 0 || index >= length)
                throw PyIndexError.Create("range object index out of range");
            return new PyInt(Start + index * Step);
        }

        /// <summary>
        /// 인덱스 접근 range[i] (internal helper)
        /// </summary>
        public virtual PyInt GetItem(long index) => GetNativeItem(index);

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private PyInt GetNativeItem(long index)
        {
            ulong length = LengthUnsigned();
            ulong position;
            if (index < 0)
            {
                ulong distance = unchecked(0UL - (ulong)index);
                if (distance > length)
                    throw PyIndexError.Create("range object index out of range");
                position = length - distance;
            }
            else position = (ulong)index;

            if (position >= length)
                throw PyIndexError.Create("range object index out of range");

            // The validated element lies between the signed-long endpoints.
            return new PyInt(unchecked((long)((ulong)NativeStart + position * (ulong)NativeStep)));
        }

        /// <summary>
        /// 슬라이싱 range[start:stop:step]
        /// </summary>
        public PyRange GetSlice(long? start = null, long? stop = null, long? step = null)
        {
            return (PyRange)GetItem(new PySlice(
                start.HasValue ? new PyInt(start.Value) : PyNone.Instance,
                stop.HasValue ? new PyInt(stop.Value) : PyNone.Instance,
                step.HasValue ? new PyInt(step.Value) : PyNone.Instance));
        }

        /// <summary>
        /// 값 포함 여부 확인 (in 연산자)
        /// </summary>
        public PyBool Contains(PyObject item)
        {
            if (!(item is PyInt intItem))
                return PyBool.False;
            
            var value = intItem.Value;
            
            // Step 방향 확인
            if (Step > 0)
            {
                if (value < Start || value >= Stop) return PyBool.False;
                return PyBool.FromBool((value - Start) % Step == 0);
            }
            else
            {
                if (value > Start || value <= Stop) return PyBool.False;
                return PyBool.FromBool((Start - value) % (-Step) == 0);
            }
        }

        /// <summary>
        /// 첫 번째 일치하는 값의 인덱스 반환
        /// </summary>
        public PyInt Index(PyObject value)
        {
            if (!(value is PyInt intValue))
                throw PyValueError.Create($"{value.ToRepr()} is not in range");
            
            var val = intValue.Value;
            
            if (!Contains(intValue).Value)
                throw PyValueError.Create($"{val} is not in range");
            
            var index = (val - Start) / Step;
            return new PyInt(index);
        }

        /// <summary>
        /// 특정 값의 개수 (항상 0 또는 1)
        /// </summary>
        public PyInt Count(PyObject value)
        {
            return new PyInt(Contains(value).Value ? 1 : 0);
        }

        #endregion

        #region Iteration Support

        /// <summary>
        /// range의 모든 값을 열거
        /// </summary>
        public virtual IEnumerable<PyInt> GetValues()
        {
            if (NativeStep > 0)
            {
                for (long current = NativeStart; current < NativeStop; current = unchecked(current + NativeStep))
                {
                    yield return new PyInt(current);
                    if (unchecked((ulong)NativeStop - (ulong)current) <= (ulong)NativeStep) yield break;
                }
            }
            else
            {
                for (long current = NativeStart; current > NativeStop; current = unchecked(current + NativeStep))
                {
                    yield return new PyInt(current);
                    if (unchecked((ulong)current - (ulong)NativeStop) <= unchecked(0UL - (ulong)NativeStep)) yield break;
                }
            }
        }

        /// <summary>
        /// PyList로 변환 (list(range(...)))
        /// </summary>
        public PyList ToList()
        {
            // Performance: Eliminated LINQ (.Cast + .ToArray) - manual conversion
            var values = new List<PyObject>();
            foreach (var pyInt in GetValues())
            {
                values.Add(pyInt);
            }
            return new PyList(values.ToArray());
        }

        /// <summary>
        /// PyTuple로 변환 (tuple(range(...)))
        /// </summary>
        public PyTuple ToTuple()
        {
            // Performance: Eliminated LINQ (.Cast + .ToArray) - manual conversion
            var values = new List<PyObject>();
            foreach (var pyInt in GetValues())
            {
                values.Add(pyInt);
            }
            return new PyTuple(values.ToArray());
        }

        /// <summary>
        /// 반복자 생성 (for 루프 지원)
        /// </summary>
        public override PyObject GetIterator()
        {
            return new PyRangeIterator(this);
        }

        #endregion

        #region Length and Type Checking

        /// <summary>
        /// Exact count for signed-long endpoints, including counts above long.MaxValue.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal ulong LengthUnsigned()
        {
            // CPython get_len_of_range: unsigned differences cover the full
            // signed endpoint span, including long.MinValue as a negative step.
            if (NativeStep > 0)
            {
                if (NativeStart >= NativeStop) return 0;
                return 1 + unchecked((ulong)NativeStop - 1 - (ulong)NativeStart) / (ulong)NativeStep;
            }
            else
            {
                if (NativeStart <= NativeStop) return 0;
                return 1 + unchecked((ulong)NativeStart - 1 - (ulong)NativeStop) / unchecked(0UL - (ulong)NativeStep);
            }
        }

        public virtual long LengthLong()
        {
            ulong length = LengthUnsigned();
            if (length > long.MaxValue)
                throw PyOverflowError.Create("Python int too large to convert to C ssize_t");
            return (long)length;
        }

        public override int Length()
        {
            var len = LengthLong();
            if (len > int.MaxValue)
                throw PyOverflowError.Create("Python int too large to convert to C# int");
            return (int)len;
        }

        internal virtual long PythonLength()
        {
            ulong length = LengthUnsigned();
            if (length > (ulong)nint.MaxValue)
                throw PyOverflowError.Create("Python int too large to convert to C ssize_t");
            return (long)length;
        }

        public override bool PyBoolValue() => NativeStep > 0 ? NativeStart < NativeStop : NativeStart > NativeStop;

        #endregion

        #region Comparison Operations

        protected override PyObject PyLess(PyObject other)
        {
            return other switch
            {
                PyRange otherRange => PyBool.FromBool(CompareRanges(otherRange) < 0),
                _ => throw PyTypeError.Create($"'<' not supported between instances of 'range' and '{other.GetTypeName()}'")
            };
        }

        protected override PyObject PyLessEqual(PyObject other)
        {
            return other switch
            {
                PyRange otherRange => PyBool.FromBool(CompareRanges(otherRange) <= 0),
                _ => throw PyTypeError.Create($"'<=' not supported between instances of 'range' and '{other.GetTypeName()}'")
            };
        }

        protected override PyObject PyGreater(PyObject other)
        {
            return other switch
            {
                PyRange otherRange => PyBool.FromBool(CompareRanges(otherRange) > 0),
                _ => throw PyTypeError.Create($"'>' not supported between instances of 'range' and '{other.GetTypeName()}'")
            };
        }

        protected override PyObject PyGreaterEqual(PyObject other)
        {
            return other switch
            {
                PyRange otherRange => PyBool.FromBool(CompareRanges(otherRange) >= 0),
                _ => throw PyTypeError.Create($"'>=' not supported between instances of 'range' and '{other.GetTypeName()}'")
            };
        }

        private int CompareRanges(PyRange other)
        {
            // 길이 우선 비교
            var lenCompare = Length().CompareTo(other.Length());
            if (lenCompare != 0) return lenCompare;
            
            // 같은 길이면 시작값 비교
            var startCompare = Start.CompareTo(other.Start);
            if (startCompare != 0) return startCompare;
            
            // 시작값도 같으면 step 비교
            return Step.CompareTo(other.Step);
        }

        #endregion

        #region Static Factory Methods

        /// <summary>
        /// range(stop) 생성
        /// </summary>
        public static PyRange Create(long stop) => new PyRange(stop);

        /// <summary>
        /// range(start, stop) 생성
        /// </summary>
        public static PyRange Create(long start, long stop) => new PyRange(start, stop);

        /// <summary>
        /// range(start, stop, step) 생성
        /// </summary>
        public static PyRange Create(long start, long stop, long step) => new PyRange(start, stop, step);

        // BigInteger excludes the sign bit, including for negative values.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal static bool FitsLong(BigInteger value) => value.GetBitLength() < 64;

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static PyRange Create(BigInteger start, BigInteger stop, BigInteger step)
        {
            if (FitsLong(start) && FitsLong(stop) && FitsLong(step))
                return new PyRange((long)start, (long)stop, (long)step);
            return new PyBigRange(start, stop, step);
        }

        #endregion

        #region Evaluate Method (NotImplementedException)

        public PyObject Evaluate(PyScope scope)
        {
            // Range objects evaluate to themselves (CPython style)
            return this;
        }

        #endregion

        #region Range-specific Methods

        /// <summary>
        /// range가 빈지 확인
        /// </summary>
        public bool IsEmpty() => !PyBoolValue();

        /// <summary>
        /// range의 최소값
        /// </summary>
        public PyInt Min()
        {
            if (IsEmpty())
                throw PyValueError.Create("min() arg is an empty sequence");

            if (Step > 0)
                return new PyInt(Start);
            else
                return new PyInt(Start + (ExactLength - 1) * Step);
        }

        /// <summary>
        /// range의 최대값
        /// </summary>
        public PyInt Max()
        {
            if (IsEmpty())
                throw PyValueError.Create("max() arg is an empty sequence");

            if (Step > 0)
                return new PyInt(Start + (ExactLength - 1) * Step);
            else
                return new PyInt(Start);
        }

        #endregion
    }
}
