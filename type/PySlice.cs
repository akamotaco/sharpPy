using System;

namespace SharpPy
{
    /// <summary>
    /// Python slice 타입 구현 - slice(start, stop, step)
    /// </summary>
    public class PySlice : PyObject
    {
        #region Core Properties

        public PyObject Start { get; }
        public PyObject Stop { get; }
        public PyObject Step { get; }

        public PySlice(PyObject start, PyObject stop, PyObject step = null)
        {
            Start = start ?? PyNone.Instance;
            Stop = stop ?? PyNone.Instance;
            Step = step ?? PyNone.Instance;
        }

        // 편의 생성자들
        public PySlice(int? start, int? stop, int? step = null)
            : this(start?.ToPyInt(), stop?.ToPyInt(), step?.ToPyInt()) { }

        public override PyType GetPyType() => PyType.SliceType;
        public override string GetTypeName() => "slice";

        /// <summary>
        /// CPython 3.12: slice.start, slice.stop, slice.step attributes
        /// </summary>
        public override PyObject GetAttribute(string name)
        {
            return name switch
            {
                "start" => Start,
                "stop" => Stop,
                "step" => Step,
                _ => base.GetAttribute(name)
            };
        }

        #endregion

        #region String Representation

        public override PyString ToStr() => ToRepr();

        public override PyString ToRepr()
        {
            var startStr = Start == PyNone.Instance ? "None" : Start.ToRepr().Value;
            var stopStr = Stop == PyNone.Instance ? "None" : Stop.ToRepr().Value;
            var stepStr = Step == PyNone.Instance ? "None" : Step.ToRepr().Value;

            return new PyString($"slice({startStr}, {stopStr}, {stepStr})");
        }

        #endregion

        #region Hash and Equality

        public override int ToHash()
        {
            return HashCode.Combine(Start.ToHash(), Stop.ToHash(), Step.ToHash());
        }

        protected override PyObject PyEquals(PyObject other)
        {
            return other switch
            {
                PySlice otherSlice => PyBool.FromBool(
                    Start.RichCompare(otherSlice.Start, CompareOp.EQ).PyBoolValue() &&
                    Stop.RichCompare(otherSlice.Stop, CompareOp.EQ).PyBoolValue() &&
                    Step.RichCompare(otherSlice.Step, CompareOp.EQ).PyBoolValue()),
                _ => PyBool.False
            };
        }

        #endregion

        #region Slice Operations

        /// <summary>
        /// 슬라이스를 실제 인덱스로 변환 (Python indices 메서드)
        /// </summary>
        public (int start, int stop, int step) Indices(int length)
        {
            var step = GetStepValue();
            
            if (step == 0)
                throw PyValueError.Create("slice step cannot be zero");
            
            var (start, stop) = GetStartStopValues(length, step);
            
            return (start, stop, step);
        }

        private int GetStepValue()
        {
            if (Step == PyNone.Instance)
                return 1;

            if (Step is PyInt stepInt)
                return (int)stepInt.Value;

            throw PyTypeError.Create("slice indices must be integers or None");
        }

        private (int start, int stop) GetStartStopValues(int length, int step)
        {
            int start, stop;

            // Start 값 처리
            if (Start == PyNone.Instance)
            {
                start = step < 0 ? length - 1 : 0;
            }
            else if (Start is PyInt startInt)
            {
                start = (int)startInt.Value;
                if (start < 0) start += length;
                // CPython 3.12: PySlice_AdjustIndices (sliceobject.c:289-290)
                // if (*start >= length) { *start = (step < 0) ? length - 1 : length; }
                if (start < 0)
                {
                    start = (step < 0) ? -1 : 0;
                }
                else if (start >= length)
                {
                    start = (step < 0) ? length - 1 : length;
                }
            }
            else
            {
                throw PyTypeError.Create("slice indices must be integers or None");
            }

            // Stop 값 처리
            if (Stop == PyNone.Instance)
            {
                stop = step < 0 ? -1 : length;
            }
            else if (Stop is PyInt stopInt)
            {
                stop = (int)stopInt.Value;
                if (stop < 0) stop += length;
                // CPython 3.12: PySlice_AdjustIndices (sliceobject.c:299-300)
                // if (*stop >= length) { *stop = (step < 0) ? length - 1 : length; }
                if (stop < 0)
                {
                    stop = (step < 0) ? -1 : 0;
                }
                else if (stop >= length)
                {
                    stop = (step < 0) ? length - 1 : length;
                }
            }
            else
            {
                throw PyTypeError.Create("slice indices must be integers or None");
            }

            return (start, stop);
        }

        /// <summary>
        /// 슬라이스가 생성할 요소들의 개수 계산
        /// </summary>
        public int GetLength(int sequenceLength)
        {
            var (start, stop, step) = Indices(sequenceLength);
            
            if (step > 0)
            {
                if (start >= stop) return 0;
                return (stop - start + step - 1) / step;
            }
            else
            {
                if (start <= stop) return 0;
                return (start - stop - step - 1) / (-step);
            }
        }

        /// <summary>
        /// 특정 인덱스에 해당하는 실제 인덱스 계산
        /// </summary>
        public int GetIndexAt(int i, int sequenceLength)
        {
            var (start, stop, step) = Indices(sequenceLength);
            var length = GetLength(sequenceLength);
            
            if (i < 0) i += length;
            if (i < 0 || i >= length)
                throw PyIndexError.Create("slice index out of range");
            
            return start + i * step;
        }

        #endregion

        #region Slice Utilities

        /// <summary>
        /// 시퀀스에 슬라이스를 적용하여 인덱스 배열 반환
        /// </summary>
        public int[] GetIndices(int sequenceLength)
        {
            var (start, stop, step) = Indices(sequenceLength);
            var result = new int[GetLength(sequenceLength)];
            
            int index = 0;
            if (step > 0)
            {
                for (int i = start; i < stop; i += step)
                {
                    result[index++] = i;
                }
            }
            else
            {
                for (int i = start; i > stop; i += step)
                {
                    result[index++] = i;
                }
            }
            
            return result;
        }

        /// <summary>
        /// 다른 슬라이스와 비교하여 동일한 결과를 생성하는지 확인
        /// </summary>
        public bool IsEquivalent(PySlice other, int sequenceLength)
        {
            try
            {
                var indices1 = GetIndices(sequenceLength);
                var indices2 = other.GetIndices(sequenceLength);
                // Performance: Eliminated LINQ (.SequenceEqual) - manual comparison
                if (indices1.Length != indices2.Length)
                    return false;
                for (int i = 0; i < indices1.Length; i++)
                {
                    if (indices1[i] != indices2[i])
                        return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Type Checking

        public override bool PyBoolValue() => true; // 슬라이스는 항상 truthy

        #endregion

        #region Static Factory Methods

        /// <summary>
        /// slice(stop) 생성
        /// </summary>
        public static PySlice Create(PyObject stop) => new PySlice(null, stop);

        /// <summary>
        /// slice(start, stop) 생성
        /// </summary>
        public static PySlice Create(PyObject start, PyObject stop) => new PySlice(start, stop);

        /// <summary>
        /// slice(start, stop, step) 생성
        /// </summary>
        public static PySlice Create(PyObject start, PyObject stop, PyObject step) => new PySlice(start, stop, step);

        /// <summary>
        /// 정수로 slice(start, stop, step) 생성
        /// </summary>
        public static PySlice Create(int? start, int? stop, int? step = null) => new PySlice(start, stop, step);

        #endregion

        #region Evaluate Method (NotImplementedException)

        public PyObject Evaluate(PyScope scope)
        {
            throw new NotImplementedException("PySlice.Evaluate() - 나중에 구현예정");
        }

        #endregion
    }

    /// <summary>
    /// 편의를 위한 확장 메서드
    /// </summary>
    public static class SliceExtensions
    {
        public static PyInt ToPyInt(this int value) => new PyInt(value);
        public static PyInt ToPyInt(this int? value) => value.HasValue ? new PyInt(value.Value) : null;
    }
}