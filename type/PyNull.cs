using System;

namespace SharpPy
{
    /// <summary>
    /// CPython 내부 NULL 포인터에 해당하는 타입
    /// Python 코드에서 접근 불가능한 내부 구현용 마커
    /// LOAD_FAST_AND_CLEAR에서 초기화되지 않은 변수를 나타냄
    /// </summary>
    public sealed class PyNull : PyObject
    {
        #region Singleton Pattern

        public static readonly PyNull Instance = new PyNull();
        private PyNull() { }

        public override PyType GetPyType() => PyType.NullType;
        public override string GetTypeName() => "NullType";

        #endregion

        #region String Representation

        public override PyString ToStr() => new PyString("<NULL>");
        public override PyString ToRepr() => new PyString("<NULL>");
        public override string ToString() => "<NULL>";

        #endregion

        #region Hash and Equality

        public override int ToHash() => -1; // NULL은 고유한 해시값

        protected override PyObject PyEquals(PyObject other)
        {
            // NULL은 오직 NULL과만 같다
            return PyBool.FromBool(other is PyNull);
        }

        #endregion

        #region Comparison Operations (NULL은 비교 불가)

        protected override PyObject PyLess(PyObject other)
        {
            throw PyTypeError.Create("NULL values cannot be compared");
        }

        protected override PyObject PyLessEqual(PyObject other)
        {
            throw PyTypeError.Create("NULL values cannot be compared");
        }

        protected override PyObject PyGreater(PyObject other)
        {
            throw PyTypeError.Create("NULL values cannot be compared");
        }

        protected override PyObject PyGreaterEqual(PyObject other)
        {
            throw PyTypeError.Create("NULL values cannot be compared");
        }

        #endregion

        #region Truth Value

        public override bool PyBoolValue() => false; // NULL은 항상 falsy

        #endregion

        #region Type Conversion (NULL은 모든 변환 불가)

        public override int ToInt()
        {
            throw PyTypeError.Create("Cannot convert NULL to int");
        }

        public override float ToFloat()
        {
            throw PyTypeError.Create("Cannot convert NULL to float");
        }

        public override double ToDouble()
        {
            throw PyTypeError.Create("Cannot convert NULL to double");
        }

        #endregion

        #region Length (NULL은 길이 없음)

        public override int Length()
        {
            throw PyTypeError.Create("NULL values have no length");
        }

        #endregion

        #region Evaluate Method

        public PyObject Evaluate(PyScope scope)
        {
            // NULL은 평가 불가 (내부 구현용이므로)
            throw PyRuntimeError.Create("NULL values cannot be evaluated");
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// 이 객체가 CPython의 NULL에 해당하는지 확인
        /// </summary>
        public static bool IsNull(PyObject obj)
        {
            // Optimized: Also handle C# null (uninitialized array elements).
            // This allows skipping the LocalsPlus initialization loop in PyFrame constructor,
            // since new PyObject[] elements are already null.
            return obj == null || obj is PyNull;
        }

        /// <summary>
        /// NULL 값은 모든 연산을 지원하지 않음
        /// </summary>
        public PyObject UnsupportedOperation(string operation)
        {
            throw PyTypeError.Create($"unsupported operation on NULL: {operation}");
        }

        #endregion
    }
}