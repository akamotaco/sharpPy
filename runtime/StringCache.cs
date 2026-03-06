using System.Runtime.CompilerServices;

namespace SharpPy
{
    /// <summary>
    /// String 객체 캐시 시스템
    /// 자주 사용되는 문자열들을 미리 생성하여 캐싱합니다.
    /// CPython은 string interning을 사용하지만, SharpPy에서는 간단한 캐시 방식을 사용합니다.
    /// </summary>
    public static class StringCache
    {
        // 자주 사용되는 문자열 상수들
        public static readonly PyStr Empty = new PyStr("");
        public static readonly PyStr Space = new PyStr(" ");
        public static readonly PyStr Newline = new PyStr("\n");
        public static readonly PyStr Tab = new PyStr("\t");
        public static readonly PyStr CarriageReturn = new PyStr("\r");

        // ASCII 단일 문자 캐시 (0-127)
        // 단일 문자 문자열은 매우 자주 사용되므로 모두 캐싱
        private static readonly PyStr[] _asciiCache = new PyStr[128];

        /// <summary>
        /// Static constructor - ASCII 캐시 초기화
        /// </summary>
        static StringCache()
        {
            // ASCII 0-127 범위의 모든 단일 문자 문자열을 미리 생성
            for (int i = 0; i < 128; i++)
            {
                _asciiCache[i] = new PyStr(((char)i).ToString());
            }
        }

        /// <summary>
        /// 문자열 값에 대한 PyStr 객체를 가져오거나 생성합니다.
        /// 자주 사용되는 문자열은 캐시된 객체를 반환합니다.
        /// </summary>
        /// <param name="value">문자열 값</param>
        /// <returns>PyStr 객체</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PyStr GetOrCreate(string value)
        {
            // null이거나 빈 문자열
            if (value == null || value.Length == 0)
                return Empty;

            // 단일 문자 문자열인 경우
            if (value.Length == 1)
            {
                char c = value[0];
                // ASCII 범위 내의 문자는 캐시에서 반환
                if (c < 128)
                    return _asciiCache[c];
            }

            // 자주 사용되는 특수 문자열들
            // (이미 단일 문자 체크를 했으므로 길이가 1인 경우는 제외됨)

            // 캐시에 없는 문자열은 새로 생성
            return new PyStr(value);
        }

        /// <summary>
        /// 캐시 통계 정보 (디버깅용)
        /// </summary>
        public static int GetCachedStringCount()
        {
            return _asciiCache.Length + 4; // ASCII + (Empty, Space, Newline, Tab)
        }
    }
}
