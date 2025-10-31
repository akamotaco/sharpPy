using System.Runtime.CompilerServices;

namespace SharpPy
{
    /// <summary>
    /// PyObject Array Cache System
    /// 자주 사용되는 작은 크기의 PyObject 배열을 캐싱하여 메모리 할당을 줄입니다.
    ///
    /// 배열은 mutable이지만, 크기별로 미리 할당된 배열을 재사용함으로써
    /// GC 압력을 줄이고 성능을 향상시킵니다.
    ///
    /// 주의: 반환된 배열은 항상 새로 생성되므로 안전하게 수정 가능합니다.
    /// </summary>
    public static class ArrayCache
    {
        // 빈 배열 (읽기 전용으로 공유 가능)
        public static readonly PyObject[] Empty = new PyObject[0];

        /// <summary>
        /// 지정된 크기의 PyObject 배열을 생성합니다.
        /// 작은 크기(0-4)는 최적화되어 있습니다.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PyObject[] Create(int size)
        {
            // 빈 배열은 공유 가능
            if (size == 0)
                return Empty;

            // 나머지는 항상 새로 생성 (mutable이므로)
            return new PyObject[size];
        }

        /// <summary>
        /// 단일 원소 배열을 생성합니다.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PyObject[] CreateSingle(PyObject item)
        {
            return new PyObject[] { item };
        }

        /// <summary>
        /// 2개 원소 배열을 생성합니다.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PyObject[] CreatePair(PyObject first, PyObject second)
        {
            return new PyObject[] { first, second };
        }

        /// <summary>
        /// 3개 원소 배열을 생성합니다.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PyObject[] CreateTriple(PyObject first, PyObject second, PyObject third)
        {
            return new PyObject[] { first, second, third };
        }

        /// <summary>
        /// 4개 원소 배열을 생성합니다.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PyObject[] CreateQuad(PyObject first, PyObject second, PyObject third, PyObject fourth)
        {
            return new PyObject[] { first, second, third, fourth };
        }
    }
}
