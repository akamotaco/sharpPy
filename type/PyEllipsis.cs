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
        public override string GetTypeName() => "ellipsis";

        #endregion

        #region String Representation

        public override PyStr ToStr() => new PyStr("Ellipsis");
        public override PyStr ToRepr() => new PyStr("Ellipsis");
        public override string ToString() => "Ellipsis";

        #endregion

        #region Hash and Equality

        public override int ToHash() => GetHashCode(); // CPython: uses address, we use object hash

        protected override PyObject PyEquals(PyObject other)
        {
            // Ellipsis는 오직 Ellipsis와만 같다
            return PyBool.FromBool(other is PyEllipsis);
        }

        #endregion

        #region Truth Value

        public override bool PyBoolValue() => true; // Ellipsis는 truthy

        #endregion

        #region Type Conversion (Ellipsis는 대부분 변환 불가)

        public override int ToInt()
        {
            throw PyTypeError.Create("int() argument must be a string, a bytes-like object or a number, not 'ellipsis'");
        }

        public override float ToFloat()
        {
            throw PyTypeError.Create("float() argument must be a string or a number, not 'ellipsis'");
        }

        public override double ToDouble()
        {
            throw PyTypeError.Create("double() argument must be a string or a number, not 'ellipsis'");
        }

        #endregion

        #region Length (Ellipsis는 길이 없음)

        public override int Length()
        {
            throw PyTypeError.Create("object of type 'ellipsis' has no len()");
        }

        #endregion
    }
}
