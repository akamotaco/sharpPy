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

        #endregion

        #region String Representation

        protected internal override PyString Str() => PyString.From("None");
        protected internal override PyString Repr() => PyString.From("None");
        public override string ToString() => "None";

        #endregion

        #region Hash and Equality

        public override int ToHash() => 0; // None은 항상 같은 해시값

        #endregion

        #region Truth Value


        // CPython 3.12: Objects/object.c - Py_None always returns false
        // Optimized: Direct return, no MRO traversal
        protected internal override bool Bool() => false;

        #endregion

        #region Type Conversion (None은 대부분 변환 불가)

        public override int ToInt()
        {
            throw PyTypeError.Create("int() argument must be a string, a bytes-like object or a real number, not 'NoneType'");
        }

        public override double ToFloat()
        {
            throw PyTypeError.Create("float() argument must be a string or a real number, not 'NoneType'");
        }

        #endregion

        #region Length (None은 길이 없음)

        protected internal override long Len()
        {
            throw PyTypeError.Create("object of type 'NoneType' has no len()");
        }

        #endregion

        #region Evaluate Method (NotImplementedException)

        public PyObject Evaluate(PyScope scope)
        {
            // None always evaluates to itself (CPython style)
            return this;
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