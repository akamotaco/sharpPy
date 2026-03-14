using System;
using System.Numerics;

namespace SharpPy
{
    /// <summary>
    /// Python bool 타입 구현 - True/False 불린 값
    /// CPython 3.12: Objects/boolobject.c - bool is subclass of int
    /// CPython에서 bool은 int의 서브클래스: isinstance(True, int) == True
    /// </summary>
    public class PyBool : PyInt
    {
        #region Core Properties

        /// <summary>
        /// C# bool 값 (PyInt.Value는 BigInteger 0 또는 1)
        /// PyBool 참조로 접근 시 bool 반환, PyInt 참조로 접근 시 BigInteger 반환
        /// </summary>
        public new bool Value { get; }

        public static readonly PyBool True = new PyBool(true);
        public static readonly PyBool False = new PyBool(false);

        private PyBool(bool value) : base(value ? 1 : 0)
        {
            Value = value;
        }

        public static PyBool FromBool(bool value) => value ? True : False;

        // CPython 3.12: Py_TYPE(op) returns &PyBool_Type
        public override PyType GetPyType() => PyType.BoolType;
        public override string GetTypeName() => "bool";

        // CPython 3.12: Objects/boolobject.c - bool_bool (trivial)
        public override bool IsTrue() => Value;

        #endregion

        #region String Representation

        // CPython 3.12: Objects/boolobject.c - bool_repr
        // bool.__repr__ returns "True" or "False", not "1" or "0"
        public override PyStr ToStr() => new PyStr(Value ? "True" : "False");
        public override PyStr ToRepr() => new PyStr(Value ? "True" : "False");
        public override string ToString() => Value ? "True" : "False";

        #endregion

        #region Hash

        // CPython 3.12: hash(True) == 1, hash(False) == 0
        public override int ToHash() => Value ? 1 : 0;

        #endregion

        #region Bitwise Operations (bool×bool → bool, CPython compatible)

        // CPython 3.12: Objects/boolobject.c - bool_and
        // If both operands are bool, return bool. Otherwise delegate to int.
        public override PyObject BitwiseAnd(PyObject other)
        {
            if (other is PyBool otherBool)
                return FromBool(Value && otherBool.Value);
            return base.BitwiseAnd(other);
        }

        // CPython 3.12: Objects/boolobject.c - bool_or
        public override PyObject BitwiseOr(PyObject other)
        {
            if (other is PyBool otherBool)
                return FromBool(Value || otherBool.Value);
            return base.BitwiseOr(other);
        }

        // CPython 3.12: Objects/boolobject.c - bool_xor
        public override PyObject BitwiseXor(PyObject other)
        {
            if (other is PyBool otherBool)
                return FromBool(Value ^ otherBool.Value);
            return base.BitwiseXor(other);
        }

        #endregion

        #region Type Conversion (CPython Compatible)

        // === To* Methods: Value Extraction (PyBool → C# basic types) ===

        /// <summary>
        /// CPython PyLong_AsLong 호환: PyBool에서 C# int 값 추출
        /// </summary>
        public override int ToInt() => Value ? 1 : 0;

        /// <summary>
        /// CPython PyFloat_AsDouble 호환: PyBool에서 C# double 값 추출
        /// </summary>
        public override float ToFloat() => Value ? 1.0f : 0.0f;
        public override double ToDouble() => Value ? 1.0 : 0.0;

        /// <summary>
        /// CPython PyObject_IsTrue 호환: PyBool에서 C# bool 값 추출
        /// </summary>
        public override bool PyBoolValue() => Value;

        // === As* Methods: Type Conversion (PyBool → PyObject types) ===

        /// <summary>
        /// CPython 호환: bool(True) → True (자기 자신 반환, 싱글톤)
        /// </summary>
        public override PyBool AsBool() => this;

        /// <summary>
        /// CPython 호환: int(True) → 1 (plain int, not bool)
        /// CPython: int(True) returns a new int object, type(int(True)) == int
        /// </summary>
        public override PyInt AsInt() => Value ? SmallIntCache.One : SmallIntCache.Zero;

        /// <summary>
        /// CPython 호환: float(True) → 1.0
        /// </summary>
        public override PyFloat AsFloat() => Value ? FloatCache.One : FloatCache.Zero;

        /// <summary>
        /// CPython 호환: str(True) → "True"
        /// </summary>
        public override string AsString() => Value ? "True" : "False";

        #endregion

        #region Evaluate Method

        public PyObject Evaluate(PyScope scope)
        {
            // Boolean literals evaluate to themselves (CPython style)
            return this;
        }

        #endregion

        #region Special Methods

        /// <summary>
        /// CPython 3.12: __index__() returns 0 or 1 for bool objects
        /// </summary>
        public override PyObject GetAttribute(string name)
        {
            if (name == "__index__")
            {
                return new PyBuiltinFunction("__index__", (args) => Value ? SmallIntCache.One : SmallIntCache.Zero);
            }
            return base.GetAttribute(name);
        }

        #endregion
    }
}
