using System;
using System.Runtime.CompilerServices;

namespace SharpPy
{
    /// <summary>
    /// Float 객체 캐시 시스템
    /// 자주 사용되는 float 값들을 미리 생성하여 캐싱합니다.
    /// CPython에는 float 캐시가 없지만, SharpPy에서는 성능 향상을 위해 구현합니다.
    /// </summary>
    public static class FloatCache
    {
        // 자주 사용되는 float 값들
        public static readonly PyFloat Zero = new PyFloat(0.0);
        public static readonly PyFloat One = new PyFloat(1.0);
        public static readonly PyFloat MinusOne = new PyFloat(-1.0);
        public static readonly PyFloat Half = new PyFloat(0.5);
        public static readonly PyFloat Two = new PyFloat(2.0);

        // 특수 값들
        public static readonly PyFloat NaN = new PyFloat(double.NaN);
        public static readonly PyFloat PositiveInfinity = new PyFloat(double.PositiveInfinity);
        public static readonly PyFloat NegativeInfinity = new PyFloat(double.NegativeInfinity);

        /// <summary>
        /// Negative zero 체크 (-0.0)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsNegativeZero(double value)
        {
            return value == 0.0 && double.IsNegativeInfinity(1.0 / value);
        }

        /// <summary>
        /// Float 값에 대한 PyFloat 객체를 가져오거나 생성합니다.
        /// 자주 사용되는 값은 캐시된 객체를 반환합니다.
        /// </summary>
        /// <param name="value">float 값</param>
        /// <returns>PyFloat 객체</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PyFloat GetOrCreate(double value)
        {
            // NaN 체크 (NaN == NaN은 false이므로 별도 처리)
            if (double.IsNaN(value))
                return NaN;

            // 무한대 체크
            if (double.IsPositiveInfinity(value))
                return PositiveInfinity;
            if (double.IsNegativeInfinity(value))
                return NegativeInfinity;

            // 자주 사용되는 값들 체크
            // 0.0은 negative zero가 아닐 때만 캐시 사용
            if (value == 0.0 && !IsNegativeZero(value))
                return Zero;
            if (value == 1.0)
                return One;
            if (value == -1.0)
                return MinusOne;
            if (value == 0.5)
                return Half;
            if (value == 2.0)
                return Two;

            // 캐시에 없는 값은 새로 생성
            return new PyFloat(value);
        }
    }
}
