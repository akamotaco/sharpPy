using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// Python range 타입 구현 - range(start, stop, step)
    /// </summary>
    public class PyRange : PyObject
    {
        #region Core Properties

        public int Start { get; }
        public int Stop { get; }
        public int Step { get; }

        public PyRange(int stop) : this(0, stop, 1) { }
        
        public PyRange(int start, int stop) : this(start, stop, 1) { }
        
        public PyRange(int start, int stop, int step)
        {
            if (step == 0)
                throw PyValueError.Create("range() arg 3 must not be zero");
            
            Start = start;
            Stop = stop;
            Step = step;
        }

        public override PyType GetPyType() => PyType.RangeType;
        public override string GetTypeName() => "range";

        #endregion

        #region String Representation

        public override PyString ToStr() => ToRepr();

        public override PyString ToRepr()
        {
            if (Start == 0 && Step == 1)
                return new PyString($"range({Stop})");
            else if (Step == 1)
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
        /// 인덱스 접근 range[i]
        /// </summary>
        public PyInt GetItem(int index)
        {
            var length = Length();
            
            // 음수 인덱스 지원
            if (index < 0) index += length;
            
            if (index < 0 || index >= length)
                throw PyIndexError.Create("range object index out of range");
            
            return new PyInt(Start + index * Step);
        }

        /// <summary>
        /// 슬라이싱 range[start:stop:step]
        /// </summary>
        public PyRange GetSlice(int? start = null, int? stop = null, int? step = null)
        {
            var length = Length();
            var sliceStep = step ?? 1;
            
            if (sliceStep == 0)
                throw PyValueError.Create("slice step cannot be zero");
            
            var actualStart = start ?? (sliceStep > 0 ? 0 : length - 1);
            var actualStop = stop ?? (sliceStep > 0 ? length : -1);
            
            // 음수 인덱스 정규화
            if (actualStart < 0) actualStart += length;
            if (actualStop < 0) actualStop += length;
            
            // 슬라이스 범위를 실제 range 값으로 변환
            var newStart = Start + Math.Max(0, actualStart) * Step;
            var newStop = Start + Math.Min(length, actualStop) * Step;
            var newStep = Step * sliceStep;
            
            return new PyRange(newStart, newStop, newStep);
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
        public IEnumerable<PyInt> GetValues()
        {
            if (Step > 0)
            {
                for (int i = Start; i < Stop; i += Step)
                    yield return new PyInt(i);
            }
            else
            {
                for (int i = Start; i > Stop; i += Step)
                    yield return new PyInt(i);
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

        public override int Length()
        {
            if (Step > 0)
            {
                if (Start >= Stop) return 0;
                return (Stop - Start + Step - 1) / Step;
            }
            else
            {
                if (Start <= Stop) return 0;
                return (Start - Stop - Step - 1) / (-Step);
            }
        }

        public override bool PyBoolValue() => Length() > 0;

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
        public static PyRange Create(int stop) => new PyRange(stop);

        /// <summary>
        /// range(start, stop) 생성
        /// </summary>
        public static PyRange Create(int start, int stop) => new PyRange(start, stop);

        /// <summary>
        /// range(start, stop, step) 생성
        /// </summary>
        public static PyRange Create(int start, int stop, int step) => new PyRange(start, stop, step);

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
        public bool IsEmpty() => Length() == 0;

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
                return new PyInt(Start + (Length() - 1) * Step);
        }

        /// <summary>
        /// range의 최대값
        /// </summary>
        public PyInt Max()
        {
            if (IsEmpty())
                throw PyValueError.Create("max() arg is an empty sequence");
            
            if (Step > 0)
                return new PyInt(Start + (Length() - 1) * Step);
            else
                return new PyInt(Start);
        }

        #endregion
    }
}