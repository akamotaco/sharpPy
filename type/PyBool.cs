using System;
using System.Numerics;

namespace SharpPy
{
    /// <summary>
    /// Python bool 타입 구현 - True/False 불린 값
    /// </summary>
    public class PyBool : PyObject
    {
        #region Core Properties

        public bool Value { get; }
        
        public static readonly PyBool True = new PyBool(true);
        public static readonly PyBool False = new PyBool(false);

        private PyBool(bool value) => Value = value;

        public static PyBool FromBool(bool value) => value ? True : False;

        public override PyType GetPyType() => PyType.BoolType;

        // CPython 3.12: Objects/boolobject.c - bool is subclass of int
        // Optimized: Direct value check, no MRO traversal
        protected internal override bool Bool() => Value;

        #endregion

        #region String Representation

        protected internal override PyString Str() => PyString.From(Value ? "True" : "False");
        protected internal override PyString Repr() => PyString.From(Value ? "True" : "False");
        public override string ToString() => Value ? "True" : "False";

        #endregion

        #region Hash and Equality

        // bool inherits long_hash.
        protected internal override long Hash() => Value ? 1 : 0;

        public override int ToHash() => Value ? 1 : 0;

        protected override PyObject PyNotEquals(PyObject other)
        {
            var result = PyEquals(other);
            return result is PyBool equal ? FromBool(!equal.Value) : result;
        }

        protected override PyObject PyEquals(PyObject other)
        {
            return other switch
            {
                PyBool otherBool => FromBool(Value == otherBool.Value),
                PyInt otherInt => FromBool((Value ? 1 : 0) == otherInt.Value),
                PyFloat otherFloat => FromBool((Value ? 1.0 : 0.0) == otherFloat.Value),
                _ => PyNotImplemented.Instance
            };
        }

        #endregion

        #region Comparison Operations

        protected override PyObject PyLess(PyObject other)
        {
            return other switch
            {
                PyBool otherBool => FromBool(!Value && otherBool.Value), // False < True
                PyInt otherInt => FromBool((Value ? 1 : 0) < otherInt.Value),
                PyFloat otherFloat => FromBool((Value ? 1.0 : 0.0) < otherFloat.Value),
                _ => PyNotImplemented.Instance
            };
        }

        protected override PyObject PyLessEqual(PyObject other)
        {
            return other switch
            {
                PyBool otherBool => FromBool(!Value || otherBool.Value), // False <= anything, True <= True
                PyInt otherInt => FromBool((Value ? 1 : 0) <= otherInt.Value),
                PyFloat otherFloat => FromBool((Value ? 1.0 : 0.0) <= otherFloat.Value),
                _ => PyNotImplemented.Instance
            };
        }

        protected override PyObject PyGreater(PyObject other)
        {
            return other switch
            {
                PyBool otherBool => FromBool(Value && !otherBool.Value), // True > False
                PyInt otherInt => FromBool((Value ? 1 : 0) > otherInt.Value),
                PyFloat otherFloat => FromBool((Value ? 1.0 : 0.0) > otherFloat.Value),
                _ => PyNotImplemented.Instance
            };
        }

        protected override PyObject PyGreaterEqual(PyObject other)
        {
            return other switch
            {
                PyBool otherBool => FromBool(Value || !otherBool.Value), // True >= anything, False >= False
                PyInt otherInt => FromBool((Value ? 1 : 0) >= otherInt.Value),
                PyFloat otherFloat => FromBool((Value ? 1.0 : 0.0) >= otherFloat.Value),
                _ => PyNotImplemented.Instance
            };
        }

        #endregion

        #region Number Operations

        // PyBool_Type has no number slots of its own but and, or and xor (Objects/boolobject.c): the
        // rest are long's, inherited, and work on the value 0 or 1.
        private PyInt Long => Value ? PyInt.One : PyInt.Zero;

        public override PyObject Add(PyObject other) => Long.Add(other);
        public override PyObject Subtract(PyObject other) => Long.Subtract(other);
        public override PyObject Multiply(PyObject other) => Long.Multiply(other);
        public override PyObject Divide(PyObject other) => Long.Divide(other);
        public override PyObject FloorDivide(PyObject other) => Long.FloorDivide(other);
        public override PyObject Modulo(PyObject other) => Long.Modulo(other);
        public override PyObject Power(PyObject other) => Long.Power(other);
        public override PyObject LeftShift(PyObject other) => Long.LeftShift(other);
        public override PyObject RightShift(PyObject other) => Long.RightShift(other);
        public override PyObject DivMod(PyObject other) => Long.DivMod(other);

        // bool_and, bool_or, bool_xor: two bools give a bool, anything else is long's.
        public override PyObject BitwiseAnd(PyObject other) =>
            other is PyBool otherBool ? FromBool(Value & otherBool.Value) : Long.BitwiseAnd(other);
        public override PyObject BitwiseOr(PyObject other) =>
            other is PyBool otherBool ? FromBool(Value | otherBool.Value) : Long.BitwiseOr(other);
        public override PyObject BitwiseXor(PyObject other) =>
            other is PyBool otherBool ? FromBool(Value ^ otherBool.Value) : Long.BitwiseXor(other);

        #endregion

        #region Unary Operations

        protected internal override PyObject NbNegative() => Value ? PyInt.MinusOne : PyInt.Zero;
        protected internal override PyObject NbPositive() => Value ? PyInt.One : PyInt.Zero;
        public PyObject Absolute() => Value ? PyInt.One : PyInt.Zero;
        // bool_invert
        protected internal override PyObject NbInvert()
        {
            PyWarnings.Deprecated("Bitwise inversion '~' on bool is deprecated and will be removed in Python 3.16. This returns the bitwise inversion of the underlying int object and is usually not what you expect from negating a bool. Use the 'not' operator for boolean negation or ~int(x) if you really want the bitwise inversion of the underlying int.");
            return PyInt.From(Value ? -2 : -1); // ~True = -2, ~False = -1;
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
        public override double ToFloat() => Value ? 1.0 : 0.0;
        
        /// <summary>
        /// CPython PyObject_IsTrue 호환: PyBool에서 C# bool 값 추출
        /// </summary>
        
        // === As* Methods: Type Conversion (PyBool → PyObject types) ===
        
        /// <summary>
        /// CPython 호환: PyBool을 PyBool로 변환 (자기 자신 반환)
        /// </summary>
        public override PyBool AsBool()
        {
            return this; // 이미 PyBool이므로 자기 자신 반환 (싱글톤 패턴)
        }
        
        /// <summary>
        /// CPython 호환: PyBool을 PyInt로 변환
        /// </summary>
        // bool inherits long_abs.
        protected internal override PyObject NbAbsolute() => Value ? PyInt.One : PyInt.Zero;

        // bool inherits long_long, which copies a strict subclass into an exact int.
        protected internal override PyObject NbInt() => Value ? PyInt.One : PyInt.Zero;

        /// <summary>
        /// CPython 호환: PyBool을 PyFloat로 변환
        /// </summary>
        protected internal override PyObject NbFloat() => Value ? new PyFloat(1.0) : new PyFloat(0.0);
        
        /// <summary>
        /// CPython 호환: PyBool을 PyString으로 변환
        /// </summary>
        public override string ToClrString()
        {
            return Value ? "True" : "False";
        }

        #endregion

        #region Evaluate Method (NotImplementedException)

        public PyObject Evaluate(PyScope scope)
        {
            // Boolean literals evaluate to themselves (CPython style)
            return this;
        }

        #endregion
    }
}
