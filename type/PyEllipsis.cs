using System;

namespace SharpPy
{
    /// <summary>
    /// Python Ellipsis 타입 구현 - 단일 Ellipsis 값 (...)
    /// CPython: Objects/sliceobject.c - _PyEllipsis_Type
    /// </summary>
    public sealed class PyEllipsis : PyObject
    {
        #region Singleton Pattern

        public static readonly PyEllipsis Instance = new PyEllipsis();
        private PyEllipsis() { }

        public override PyType GetPyType() => PyType.EllipsisType;

        #endregion

        #region String Representation

        protected internal override PyString Str() => PyString.From("Ellipsis");
        protected internal override PyString Repr() => PyString.From("Ellipsis");
        public override string ToString() => "Ellipsis";

        #endregion

        #region Hash and Equality

        public override int ToHash() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this); // CPython: uses address, we use object hash

        protected override PyObject PyEquals(PyObject other)
        {
            // Ellipsis는 오직 Ellipsis와만 같다
            return PyBool.FromBool(other is PyEllipsis);
        }

        #endregion

        #region Truth Value

        protected internal override bool Bool() => true; // Ellipsis는 truthy

        #endregion

        #region Type Conversion (Ellipsis는 대부분 변환 불가)

        public override int ToInt()
        {
            throw PyTypeError.Create("int() argument must be a string, a bytes-like object or a real number, not 'ellipsis'");
        }

        public override double ToFloat()
        {
            throw PyTypeError.Create("float() argument must be a string or a real number, not 'ellipsis'");
        }

        #endregion

        #region Length (Ellipsis는 길이 없음)

        protected internal override long Len()
        {
            throw PyTypeError.Create("object of type 'ellipsis' has no len()");
        }

        #endregion
    }
}
