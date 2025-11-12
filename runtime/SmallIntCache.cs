using System.Runtime.CompilerServices;

namespace SharpPy
{
    /// <summary>
    /// CPython 3.12 호환 Small Integer Cache
    /// CPython은 -5 ~ 256 범위의 정수를 미리 생성하여 캐싱합니다.
    /// 이는 작은 정수가 매우 자주 사용되기 때문에 메모리 할당을 줄이고 성능을 향상시킵니다.
    ///
    /// CPython 참조: Objects/longobject.c의 small_ints 배열
    /// </summary>
    public static class SmallIntCache
    {
        // CPython 호환 범위
        private const int MIN_CACHED_INT = -5;
        private const int MAX_CACHED_INT = 256;
        private const int CACHE_SIZE = MAX_CACHED_INT - MIN_CACHED_INT + 1;

        // 미리 생성된 PyInt 객체들
        private static readonly PyInt[] _cache = new PyInt[CACHE_SIZE];

        // Static 상수들 (자주 사용되는 값들)
        public static readonly PyInt Zero;
        public static readonly PyInt One;
        public static readonly PyInt MinusOne;

        /// <summary>
        /// Static constructor - 캐시 초기화
        /// </summary>
        static SmallIntCache()
        {
            // -5부터 256까지 모든 정수를 미리 생성
            for (int i = 0; i < CACHE_SIZE; i++)
            {
                _cache[i] = new PyInt(MIN_CACHED_INT + i);
            }

            // 자주 사용되는 상수들에 대한 참조 설정
            Zero = _cache[-MIN_CACHED_INT];      // _cache[5] = PyInt(0)
            One = _cache[1 - MIN_CACHED_INT];    // _cache[6] = PyInt(1)
            MinusOne = _cache[-1 - MIN_CACHED_INT]; // _cache[4] = PyInt(-1)
        }

        /// <summary>
        /// 정수 값에 대한 PyInt 객체를 가져오거나 생성합니다.
        /// 캐시 범위 내의 값은 캐시된 객체를 반환하고, 범위 밖의 값은 새로 생성합니다.
        /// </summary>
        /// <param name="value">정수 값</param>
        /// <returns>PyInt 객체</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PyInt GetOrCreate(long value)
        {
            // 캐시 범위 체크
            if (value >= MIN_CACHED_INT && value <= MAX_CACHED_INT)
            {
                return _cache[value - MIN_CACHED_INT];
            }

            // 캐시 범위 밖의 값은 새로 생성
            return new PyInt(value);
        }

        /// <summary>
        /// 캐시 통계 정보 (디버깅용)
        /// </summary>
        public static (int MinValue, int MaxValue, int CacheSize) GetCacheInfo()
        {
            return (MIN_CACHED_INT, MAX_CACHED_INT, CACHE_SIZE);
        }
    }
}
