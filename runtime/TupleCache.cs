using System.Runtime.CompilerServices;

namespace SharpPy
{
    /// <summary>
    /// Tuple Cache System
    /// CPython 3.12의 tuple free list를 참고한 구조
    /// 자주 사용되는 작은 크기의 튜플들을 미리 생성하여 캐싱
    ///
    /// CPython 참조: Objects/tupleobject.c의 free_list와 numfree
    /// CPython은 크기별로 최대 2000개까지 재사용하지만,
    /// SharpPy는 불변 객체이므로 자주 사용되는 패턴만 미리 생성
    /// </summary>
    public static class TupleCache
    {
        // 빈 튜플 (가장 자주 사용됨)
        public static readonly PyTuple Empty = new PyTuple(new PyObject[0]);

        // 단일 원소 튜플 캐시 (자주 사용되는 값들)
        // None, True, False 같은 singleton 값들을 담는 튜플
        private static readonly PyTuple[] _singleElementCache = new PyTuple[16];
        private static int _singleCacheCount = 0;

        static TupleCache()
        {
            // 자주 사용되는 단일 원소 튜플들을 미리 생성
            AddToSingleCache(PyNone.Instance);
            AddToSingleCache(PyBool.True);
            AddToSingleCache(PyBool.False);
            AddToSingleCache(SmallIntCache.Zero);
            AddToSingleCache(SmallIntCache.One);
            AddToSingleCache(SmallIntCache.MinusOne);
            AddToSingleCache(StringCache.Empty);
        }

        private static void AddToSingleCache(PyObject item)
        {
            if (_singleCacheCount < _singleElementCache.Length)
            {
                _singleElementCache[_singleCacheCount++] = new PyTuple(new[] { item });
            }
        }

        /// <summary>
        /// 튜플 객체를 가져오거나 생성합니다.
        /// 캐시 가능한 패턴은 캐시된 객체를 반환합니다.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PyTuple GetOrCreate(PyObject[] items)
        {
            // 빈 튜플
            if (items == null || items.Length == 0)
                return Empty;

            // 단일 원소 튜플 - 캐시 검색
            if (items.Length == 1)
            {
                var item = items[0];
                for (int i = 0; i < _singleCacheCount; i++)
                {
                    // ReferenceEquals로 빠른 비교 (singleton 객체들)
                    if (ReferenceEquals(_singleElementCache[i].Items[0], item))
                        return _singleElementCache[i];
                }
            }

            // 캐시에 없는 경우 새로 생성
            return new PyTuple(items);
        }

        /// <summary>
        /// 단일 원소 튜플 생성 (자주 사용되는 패턴)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PyTuple CreateSingle(PyObject item)
        {
            // 캐시 검색
            for (int i = 0; i < _singleCacheCount; i++)
            {
                if (ReferenceEquals(_singleElementCache[i].Items[0], item))
                    return _singleElementCache[i];
            }

            // 캐시에 없으면 새로 생성
            return new PyTuple(new[] { item });
        }

        /// <summary>
        /// 2개 원소 튜플 생성 (자주 사용되는 패턴)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PyTuple CreatePair(PyObject first, PyObject second)
        {
            return new PyTuple(new[] { first, second });
        }

        /// <summary>
        /// 3개 원소 튜플 생성 (자주 사용되는 패턴)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PyTuple CreateTriple(PyObject first, PyObject second, PyObject third)
        {
            return new PyTuple(new[] { first, second, third });
        }

        /// <summary>
        /// 캐시 통계 정보 (디버깅용)
        /// </summary>
        public static (int SingleElementCacheSize, int TotalCachedTuples) GetCacheInfo()
        {
            return (_singleElementCache.Length, _singleCacheCount + 1); // +1 for Empty
        }
    }
}
