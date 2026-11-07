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
        public override string GetTypeName() => "bool";

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

        #region Arithmetic Operations (bool은 int의 서브클래스처럼 동작)

        public override PyObject Add(PyObject other)
        {
            // CPython 3.12: Objects/boolobject.c - bool is subclass of int, uses int operations
            int boolAsInt = Value ? 1 : 0;
            return other switch
            {
                PyBool otherBool => PyInt.From(boolAsInt + (otherBool.Value ? 1 : 0)),
                PyInt otherInt => PyInt.From(boolAsInt + otherInt.Value),
                PyFloat otherFloat => new PyFloat(boolAsInt + otherFloat.Value),
                _ => PyNotImplemented.Instance
            };
        }

        public override PyObject Subtract(PyObject other)
        {
            // CPython 3.12: Objects/boolobject.c - bool arithmetic via int
            int boolAsInt = Value ? 1 : 0;
            return other switch
            {
                PyBool otherBool => PyInt.From(boolAsInt - (otherBool.Value ? 1 : 0)),
                PyInt otherInt => PyInt.From(boolAsInt - otherInt.Value),
                PyFloat otherFloat => new PyFloat(boolAsInt - otherFloat.Value),
                _ => PyNotImplemented.Instance
            };
        }

        public override PyObject Multiply(PyObject other)
        {
            // CPython 3.12: Objects/boolobject.c - bool multiplication via int
            int boolAsInt = Value ? 1 : 0;
            return other switch
            {
                PyBool otherBool => PyInt.From(boolAsInt * (otherBool.Value ? 1 : 0)),
                PyInt otherInt => PyInt.From(boolAsInt * otherInt.Value),
                PyFloat otherFloat => new PyFloat(boolAsInt * otherFloat.Value),
                _ => PyNotImplemented.Instance
            };
        }

        public override PyObject Divide(PyObject other)
        {
            double boolAsDouble = Value ? 1.0 : 0.0;
            
            double otherValue = other switch
            {
                PyBool otherBool => otherBool.Value ? 1.0 : 0.0,
                PyInt otherInt => (double)otherInt.Value,
                PyFloat otherFloat => otherFloat.Value,
                _ => throw PyTypeError.Create($"unsupported operand type(s) for /: 'bool' and '{other.GetTypeName()}'")
            };

            if (otherValue == 0.0)
                throw PyZeroDivisionError.Create("division by zero");

            return new PyFloat(boolAsDouble / otherValue);
        }

        public override PyObject FloorDivide(PyObject other)
        {
            int boolAsInt = Value ? 1 : 0;
            
            if (other is PyBool otherBool)
            {
                if (!otherBool.Value)
                    throw PyZeroDivisionError.Create("integer division or modulo by zero");
                return PyInt.From(boolAsInt);
            }

            if (other is PyInt otherInt)
            {
                // CPython 3.12: Objects/longobject.c:2453-2494 - long_div
                if (otherInt.Value == 0)
                    throw PyZeroDivisionError.Create("integer division or modulo by zero");
                return PyInt.From(boolAsInt / otherInt.Value);
            }

            if (other is PyFloat otherFloat)
            {
                if (otherFloat.Value == 0.0)
                    throw PyZeroDivisionError.Create("integer division or modulo by zero");
                return new PyFloat(Math.Floor(boolAsInt / otherFloat.Value));
            }
            
            throw PyTypeError.Create($"unsupported operand type(s) for //: 'bool' and '{other.GetTypeName()}'");
        }

        public override PyObject Modulo(PyObject other)
        {
            int boolAsInt = Value ? 1 : 0;

            if (other is PyBool otherBool)
            {
                if (!otherBool.Value)
                    throw PyZeroDivisionError.Create("integer division or modulo by zero");
                return PyInt.Zero; // 1 % 1 = 0, 0 % 1 = 0
            }

            if (other is PyInt otherInt)
            {
                // CPython 3.12: Objects/longobject.c:2496-2531 - long_mod
                if (otherInt.Value == 0)
                    throw PyZeroDivisionError.Create("integer division or modulo by zero");
                return PyInt.From(boolAsInt % otherInt.Value);
            }

            if (other is PyFloat otherFloat)
            {
                if (otherFloat.Value == 0.0)
                    throw PyZeroDivisionError.Create("float modulo");
                return new PyFloat(boolAsInt % otherFloat.Value);
            }

            throw PyTypeError.Create($"unsupported operand type(s) for %: 'bool' and '{other.GetTypeName()}'");
        }

        public override PyObject Power(PyObject other)
        {
            int boolAsInt = Value ? 1 : 0;
            
            if (other is PyBool otherBool)
            {
                int otherAsInt = otherBool.Value ? 1 : 0;
                return PyInt.From((int)Math.Pow(boolAsInt, otherAsInt));
            }

            if (other is PyInt otherInt)
            {
                // CPython 3.12: Objects/longobject.c:4319-4538 - long_pow
                if (otherInt.Value < 0)
                    return new PyFloat(Math.Pow(boolAsInt, (double)otherInt.Value));
                return PyInt.From(BigInteger.Pow(boolAsInt, (int)otherInt.Value));
            }
            
            throw PyTypeError.Create($"unsupported operand type(s) for ** or pow(): 'bool' and '{other.GetTypeName()}'");
        }

        #endregion

        #region Bitwise Operations

        public override PyObject BitwiseAnd(PyObject other)
        {
            // CPython 3.12: Objects/longobject.c:3562-3590 - long_and
            int boolAsInt = Value ? 1 : 0;
            return other switch
            {
                PyBool otherBool => PyBool.FromBool(Value && otherBool.Value),
                PyInt otherInt => PyInt.From(boolAsInt & otherInt.Value),
                _ => PyNotImplemented.Instance,
            };
        }

        public override PyObject BitwiseOr(PyObject other)
        {
            // CPython 3.12: Objects/longobject.c:3592-3620 - long_or
            int boolAsInt = Value ? 1 : 0;
            return other switch
            {
                PyBool otherBool => PyBool.FromBool(Value || otherBool.Value),
                PyInt otherInt => PyInt.From(boolAsInt | otherInt.Value),
                _ => PyNotImplemented.Instance,
            };
        }

        public override PyObject BitwiseXor(PyObject other)
        {
            // CPython 3.12: Objects/longobject.c:3622-3650 - long_xor
            int boolAsInt = Value ? 1 : 0;
            return other switch
            {
                PyBool otherBool => PyBool.FromBool(Value ^ otherBool.Value),
                PyInt otherInt => PyInt.From(boolAsInt ^ otherInt.Value),
                _ => PyNotImplemented.Instance,
            };
        }

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
