using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

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
        public (int start, int stop, int step) Indices(int length) => NormalizeIndices(length, false);

        // Array/List-backed sequences only observe selected items. With int-sized
        // storage, a larger stride selects at most one item. Do not use this
        // clipping for range steps or memoryview stride metadata.
        internal (int start, int stop, int step) StorageIndices(int length) => NormalizeIndices(length, true);

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private (int start, int stop, int step) NormalizeIndices(int length, bool clipStep)
        {
            var (start, stop, step) = UnpackIndices(clipStep);
            return AdjustStorageIndices(length, start, stop, step);
        }

        // Bytearray must read its current size after all __index__ callbacks.
        internal (int start, int stop, int step) UnpackStorage() => UnpackIndices(true);

        internal bool HasIndexCallbacks
        {
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
            get => NeedsIndexCall(Start) || NeedsIndexCall(Stop) || NeedsIndexCall(Step);
        }
        private static bool NeedsIndexCall(PyObject value) =>
            value != PyNone.Instance && value is not PyInt && value is not PyBool;

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private (int start, int stop, int step) UnpackIndices(bool clipStep)
        {
            int step = GetStepValue(clipStep);
            if (step == 0) throw PyValueError.Create("slice step cannot be zero");
            int start = GetBoundValue(Start, step < 0 ? int.MaxValue : 0);
            int stop = GetBoundValue(Stop, step < 0 ? int.MinValue : int.MaxValue);
            return (start, stop, step);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private int GetStepValue(bool clipStep)
        {
            if (Step == PyNone.Instance)
                return 1;

            // Try __index__ protocol for integer-like objects
            // CPython 3.12: Objects/sliceobject.c:245 (slice_indices)
            var index = PyObject.TryGetIndex(Step);
            if (index != null)
                return clipStep ? int.CreateSaturating(index.Value) : (int)index.Value;

            throw PyTypeError.Create("slice indices must be integers or None or have an __index__ method");
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static int GetBoundValue(PyObject value, int defaultValue)
        {
            if (value == PyNone.Instance) return defaultValue;
            var index = PyObject.TryGetIndex(value);
            if (index == null)
                throw PyTypeError.Create("slice indices must be integers or None or have an __index__ method");
            return int.CreateSaturating(index.Value);
        }

        // CPython PySlice_AdjustIndices: this phase cannot execute Python code.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal static (int start, int stop, int step) AdjustStorageIndices(int length, int start, int stop, int step)
        {
            if (start < 0) start += length;
            if (start < 0) start = step < 0 ? -1 : 0;
            else if (start >= length) start = step < 0 ? length - 1 : length;
            if (stop < 0) stop += length;
            if (stop < 0) stop = step < 0 ? -1 : 0;
            else if (stop >= length) stop = step < 0 ? length - 1 : length;
            return (start, stop, step);
        }

        /// <summary>
        /// 슬라이스가 생성할 요소들의 개수 계산
        /// </summary>
        public int GetLength(int sequenceLength)
        {
            var (start, stop, step) = Indices(sequenceLength);

            return GetLength(start, stop, step);
        }

        // Normalized bounds let callers reuse one __index__ evaluation.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal static int GetLength(int start, int stop, int step) => step > 0
            ? (start < stop ? 1 + (stop - 1 - start) / step : 0)
            : (stop < start ? (int)(1 + ((long)start - 1 - stop) / -(long)step) : 0);

        /// <summary>
        /// 특정 인덱스에 해당하는 실제 인덱스 계산
        /// </summary>
        public int GetIndexAt(int i, int sequenceLength)
        {
            var (start, stop, step) = Indices(sequenceLength);
            var length = GetLength(start, stop, step);
            
            if (i < 0) i += length;
            if (i < 0 || i >= length)
                throw PyIndexError.Create("slice index out of range");
            
            return start + i * step;
        }

        #endregion

        #region Slice Utilities

        // Arguments have already been normalized; no Python callbacks occur
        // while the backing span is in use. Each surviving run moves once.
        internal static void DeleteItems<T>(List<T> items, int start, int step, int count)
        {
            if (count == 0) return;
            long stride = step;
            if (stride < 0)
            {
                start = (int)(start + (count - 1) * stride);
                stride = -stride;
            }
            if (count == 1 || stride == 1)
            {
                items.RemoveRange(start, count);
                return;
            }
            var storage = CollectionsMarshal.AsSpan(items);
            int write = start;
            long removedPosition = start;
            for (int n = 0; n < count; n++)
            {
                int read = (int)removedPosition + 1;
                long next = n + 1 < count ? removedPosition + stride : storage.Length;
                int length = (int)(next - read);
                storage.Slice(read, length).CopyTo(storage.Slice(write, length));
                write += length;
                removedPosition = next;
            }
            // List<T> clears reference slots in the discarded tail when needed.
            items.RemoveRange(write, count);
        }

        /// <summary>
        /// 시퀀스에 슬라이스를 적용하여 인덱스 배열 반환
        /// </summary>
        public int[] GetIndices(int sequenceLength)
        {
            var (start, stop, step) = Indices(sequenceLength);
            var result = new int[GetLength(start, stop, step)];
            // Count-bounded traversal: the final increment may wrap, but its
            // value is never used to decide whether another element exists.
            for (int n = 0, position = start; n < result.Length; n++, position = unchecked(position + step))
                result[n] = position;

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
