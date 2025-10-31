using System.Runtime.CompilerServices;

namespace SharpPy
{
    /// <summary>
    /// List Cache System
    /// 빈 리스트는 mutable이므로 캐시할 수 없지만,
    /// 빈 리스트를 생성하는 패턴을 최적화합니다.
    ///
    /// 주의: 리스트는 mutable이므로 singleton 패턴을 사용할 수 없습니다!
    /// 대신 빈 배열을 재사용하는 방식으로 메모리 할당을 줄입니다.
    /// </summary>
    public static class ListCache
    {
        // 빈 배열 (모든 빈 리스트가 공유 가능 - 리스트 생성 시점에는 읽기 전용)
        // PyList는 내부적으로 List<PyObject>를 사용하므로 빈 배열로 초기화 가능
        private static readonly PyObject[] _emptyArray = new PyObject[0];

        /// <summary>
        /// 빈 리스트를 생성합니다.
        /// 내부적으로 빈 배열을 재사용하여 메모리 할당을 줄입니다.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PyList CreateEmpty()
        {
            // PyList는 mutable이므로 항상 새 인스턴스 생성
            // 하지만 내부 배열은 빈 배열을 공유 (PyList 생성자에서 복사됨)
            return new PyList(_emptyArray);
        }

        /// <summary>
        /// 단일 원소 리스트를 생성합니다.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PyList CreateSingle(PyObject item)
        {
            return new PyList(new[] { item });
        }

        /// <summary>
        /// 2개 원소 리스트를 생성합니다.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PyList CreatePair(PyObject first, PyObject second)
        {
            return new PyList(new[] { first, second });
        }

        /// <summary>
        /// 배열로부터 리스트를 생성합니다.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PyList Create(PyObject[] items)
        {
            if (items == null || items.Length == 0)
                return CreateEmpty();

            return new PyList(items);
        }
    }
}
