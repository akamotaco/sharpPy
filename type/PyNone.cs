using System;

namespace SharpPy
{
    /// <summary>
    /// Python None 타입 구현 - 단일 None 값
    /// </summary>
    public sealed class PyNone : PyObject
    {
        #region Singleton Pattern

        public static readonly PyNone Instance = new PyNone();
        private PyNone() { }

        public override PyType GetPyType() => PyType.NoneType;
        public override string GetTypeName() => "NoneType";

        #endregion

        #region String Representation

        public override string ToStr() => "None";
        public override string ToRepr() => "None";
        public override string ToString() => "None";

        #endregion

        #region Hash and Equality

        public override int ToHash() => 0; // None은 항상 같은 해시값

        protected override PyObject PyEquals(PyObject other)
        {
            // None은 오직 None과만 같다
            return PyBool.FromBool(other is PyNone);
        }

        #endregion

        #region Comparison Operations (None은 자기 자신과만 비교 가능)

        protected override PyObject PyLess(PyObject other)
        {
            return other switch
            {
                PyNone => PyBool.False, // None < None은 False
                _ => throw PyTypeError.Create($"'<' not supported between instances of 'NoneType' and '{other.GetTypeName()}'")
            };
        }

        protected override PyObject PyLessEqual(PyObject other)
        {
            return other switch
            {
                PyNone => PyBool.True, // None <= None은 True
                _ => throw PyTypeError.Create($"'<=' not supported between instances of 'NoneType' and '{other.GetTypeName()}'")
            };
        }

        protected override PyObject PyGreater(PyObject other)
        {
            return other switch
            {
                PyNone => PyBool.False, // None > None은 False
                _ => throw PyTypeError.Create($"'>' not supported between instances of 'NoneType' and '{other.GetTypeName()}'")
            };
        }

        protected override PyObject PyGreaterEqual(PyObject other)
        {
            return other switch
            {
                PyNone => PyBool.True, // None >= None은 True
                _ => throw PyTypeError.Create($"'>=' not supported between instances of 'NoneType' and '{other.GetTypeName()}'")
            };
        }

        #endregion

        #region Truth Value

        public override bool PyBoolValue() => false; // None은 항상 falsy

        #endregion

        #region Type Conversion (None은 대부분 변환 불가)

        public override int ToInt()
        {
            throw PyTypeError.Create("int() argument must be a string, a bytes-like object or a number, not 'NoneType'");
        }

        public override double ToFloat()
        {
            throw PyTypeError.Create("float() argument must be a string or a number, not 'NoneType'");
        }

        #endregion

        #region Length (None은 길이 없음)

        public override int Length()
        {
            throw PyTypeError.Create("object of type 'NoneType' has no len()");
        }

        #endregion

        #region Evaluate Method (NotImplementedException)

        public PyObject Evaluate(PyScope scope)
        {
            throw new NotImplementedException("PyNone.Evaluate() - 나중에 구현예정");
        }

        #endregion

        #region Custom Methods

        /// <summary>
        /// None 타입은 대부분의 산술/비트 연산을 지원하지 않음을 명시적으로 표현
        /// </summary>
        public PyObject UnsupportedOperation(string operation)
        {
            throw PyTypeError.Create($"unsupported operand type(s) for {operation}: 'NoneType'");
        }

        #endregion
    }
}