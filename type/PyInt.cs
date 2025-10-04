using System;
using Py_int_t = System.Int64;

namespace SharpPy
{
    /// <summary>
    /// Python int 타입 구현 - C# long을 기반으로 한 정수
    /// Python int는 임의 정밀도이지만, 기본적으로 long (64-bit)을 사용
    /// Py_int_t = System.Int64 (CPython의 PyLong 호환)
    /// </summary>
    public class PyInt : PyObject
    {
        #region Core Properties

        public Py_int_t Value { get; }

        public PyInt(Py_int_t value) => Value = value;

        public override PyType GetPyType() => PyType.IntType;
        public override string GetTypeName() => "int";

        #endregion

        #region String Representation

        public override string ToStr() => Value.ToString();
        public override string ToRepr() => Value.ToString();

        #endregion

        #region Hash and Equality

        public override int ToHash() => Value.GetHashCode();

        protected override PyObject PyEquals(PyObject other)
        {
            return other switch
            {
                PyInt otherInt => PyBool.FromBool(Value == otherInt.Value),
                PyFloat otherFloat => PyBool.FromBool(Value == otherFloat.Value),
                PyBool otherBool => PyBool.FromBool(Value == (otherBool.Value ? 1 : 0)),
                _ => PyBool.False
            };
        }

        #endregion

        #region Comparison Operations

        protected override PyObject PyLess(PyObject other)
        {
            return other switch
            {
                PyInt otherInt => PyBool.FromBool(Value < otherInt.Value),
                PyFloat otherFloat => PyBool.FromBool(Value < otherFloat.Value),
                PyBool otherBool => PyBool.FromBool(Value < (otherBool.Value ? 1 : 0)),
                _ => throw PyTypeError.Create($"'<' not supported between instances of 'int' and '{other.GetTypeName()}'")
            };
        }

        protected override PyObject PyLessEqual(PyObject other)
        {
            return other switch
            {
                PyInt otherInt => PyBool.FromBool(Value <= otherInt.Value),
                PyFloat otherFloat => PyBool.FromBool(Value <= otherFloat.Value),
                PyBool otherBool => PyBool.FromBool(Value <= (otherBool.Value ? 1 : 0)),
                _ => throw PyTypeError.Create($"'<=' not supported between instances of 'int' and '{other.GetTypeName()}'")
            };
        }

        protected override PyObject PyGreater(PyObject other)
        {
            return other switch
            {
                PyInt otherInt => PyBool.FromBool(Value > otherInt.Value),
                PyFloat otherFloat => PyBool.FromBool(Value > otherFloat.Value),
                PyBool otherBool => PyBool.FromBool(Value > (otherBool.Value ? 1 : 0)),
                _ => throw PyTypeError.Create($"'>' not supported between instances of 'int' and '{other.GetTypeName()}'")
            };
        }

        protected override PyObject PyGreaterEqual(PyObject other)
        {
            return other switch
            {
                PyInt otherInt => PyBool.FromBool(Value >= otherInt.Value),
                PyFloat otherFloat => PyBool.FromBool(Value >= otherFloat.Value),
                PyBool otherBool => PyBool.FromBool(Value >= (otherBool.Value ? 1 : 0)),
                _ => throw PyTypeError.Create($"'>=' not supported between instances of 'int' and '{other.GetTypeName()}'")
            };
        }

        #endregion

        #region Arithmetic Operations

        public override PyObject Add(PyObject other)
        {
            return other switch
            {
                PyInt otherInt => new PyInt(Value + otherInt.Value),
                PyFloat otherFloat => new PyFloat(Value + otherFloat.Value),
                PyBool otherBool => new PyInt(Value + (otherBool.Value ? 1 : 0)),
                _ => PyNotImplemented.Instance
            };
        }

        public override PyObject Subtract(PyObject other)
        {
            return other switch
            {
                PyInt otherInt => new PyInt(Value - otherInt.Value),
                PyFloat otherFloat => new PyFloat(Value - otherFloat.Value),
                PyBool otherBool => new PyInt(Value - (otherBool.Value ? 1 : 0)),
                _ => PyNotImplemented.Instance
            };
        }

        public override PyObject Multiply(PyObject other)
        {
            return other switch
            {
                PyInt otherInt => new PyInt(Value * otherInt.Value),
                PyFloat otherFloat => new PyFloat(Value * otherFloat.Value),
                PyBool otherBool => new PyInt(Value * (otherBool.Value ? 1 : 0)),
                _ => PyNotImplemented.Instance
            };
        }

        public override PyObject Divide(PyObject other)
        {
            double otherValue;
            if (other is PyInt otherInt)
                otherValue = (double)otherInt.Value;
            else if (other is PyFloat otherFloat)
                otherValue = otherFloat.Value;
            else if (other is PyBool otherBool)
                otherValue = otherBool.Value ? 1.0 : 0.0;
            else
                throw PyTypeError.Create($"unsupported operand type(s) for /: 'int' and '{other.GetTypeName()}'");

            if (otherValue == 0.0)
                throw PyZeroDivisionError.Create("division by zero");

            return new PyFloat(Value / otherValue);
        }

        public override PyObject FloorDivide(PyObject other)
        {
            if (other is PyInt otherInt)
            {
                if (otherInt.Value == 0)
                    throw PyZeroDivisionError.Create("integer division or modulo by zero");
                return new PyInt(Value / otherInt.Value);
            }
            if (other is PyFloat otherFloat)
            {
                if (otherFloat.Value == 0.0)
                    throw PyZeroDivisionError.Create("integer division or modulo by zero");
                return new PyFloat(Math.Floor(Value / otherFloat.Value));
            }
            if (other is PyBool otherBool)
            {
                if (!otherBool.Value)
                    throw PyZeroDivisionError.Create("integer division or modulo by zero");
                return new PyInt(Value);
            }
            throw PyTypeError.Create($"unsupported operand type(s) for //: 'int' and '{other.GetTypeName()}'");
        }

        public override PyObject Modulo(PyObject other)
        {
            if (other is PyInt otherInt)
            {
                if (otherInt.Value == 0)
                    throw PyZeroDivisionError.Create("integer division or modulo by zero");
                return new PyInt(Value % otherInt.Value);
            }
            if (other is PyFloat otherFloat)
            {
                if (otherFloat.Value == 0.0)
                    throw PyZeroDivisionError.Create("float modulo");
                return new PyFloat(Value % otherFloat.Value);
            }
            if (other is PyBool otherBool)
            {
                if (!otherBool.Value)
                    throw PyZeroDivisionError.Create("integer division or modulo by zero");
                return new PyInt(0);
            }
            throw PyTypeError.Create($"unsupported operand type(s) for %: 'int' and '{other.GetTypeName()}'");
        }

        public override PyObject Power(PyObject other)
        {
            Py_int_t otherValue;
            if (other is PyInt otherInt)
                otherValue = otherInt.Value;
            else if (other is PyBool otherBool)
                otherValue = otherBool.Value ? 1 : 0;
            else
                throw PyTypeError.Create($"unsupported operand type(s) for ** or pow(): 'int' and '{other.GetTypeName()}'");

            if (otherValue < 0)
            {
                // 음수 거듭제곱은 float 결과
                return new PyFloat(Math.Pow(Value, otherValue));
            }

            var result = (Py_int_t)Math.Pow(Value, otherValue);
            return new PyInt(result);
        }

        #endregion

        #region Bitwise Operations

        public override PyObject BitwiseAnd(PyObject other)
        {
            if (other is PyInt otherInt)
                return new PyInt(Value & otherInt.Value);
            if (other is PyBool otherBool)
                return new PyInt(Value & (otherBool.Value ? 1 : 0));
            throw PyTypeError.Create($"unsupported operand type(s) for &: 'int' and '{other.GetTypeName()}'");
        }

        public override PyObject BitwiseOr(PyObject other)
        {
            if (other is PyInt otherInt)
                return new PyInt(Value | otherInt.Value);
            if (other is PyBool otherBool)
                return new PyInt(Value | (otherBool.Value ? 1 : 0));
            throw PyTypeError.Create($"unsupported operand type(s) for |: 'int' and '{other.GetTypeName()}'");
        }

        public override PyObject BitwiseXor(PyObject other)
        {
            if (other is PyInt otherInt)
                return new PyInt(Value ^ otherInt.Value);
            if (other is PyBool otherBool)
                return new PyInt(Value ^ (otherBool.Value ? 1 : 0));
            throw PyTypeError.Create($"unsupported operand type(s) for ^: 'int' and '{other.GetTypeName()}'");
        }

        public override PyObject LeftShift(PyObject other)
        {
            if (!(other is PyInt otherInt))
                throw PyTypeError.Create($"unsupported operand type(s) for <<: 'int' and '{other.GetTypeName()}'");

            if (otherInt.Value < 0)
                throw PyValueError.Create("negative shift count");

            return new PyInt(Value << (int)otherInt.Value);
        }

        public override PyObject RightShift(PyObject other)
        {
            if (!(other is PyInt otherInt))
                throw PyTypeError.Create($"unsupported operand type(s) for >>: 'int' and '{other.GetTypeName()}'");

            if (otherInt.Value < 0)
                throw PyValueError.Create("negative shift count");

            return new PyInt(Value >> (int)otherInt.Value);
        }

        #endregion

        #region Unary Operations

        public override PyObject Negative() => new PyInt(-Value);
        public override PyObject Positive() => this;
        public PyObject Absolute() => new PyInt(Math.Abs(Value));
        public override PyObject BitwiseNot() => new PyInt(~Value);

        #endregion

        #region Type Conversion (CPython Compatible)

        // === To* Methods: Value Extraction (PyObject → C# basic types) ===
        
        /// <summary>
        /// CPython PyLong_AsLong 호환: PyInt에서 C# int 값 추출
        /// </summary>
        public override int ToInt() => (int)Value;
        
        /// <summary>
        /// CPython PyLong_AsDouble 호환: PyInt에서 C# double 값 추출  
        /// </summary>
        public override double ToFloat() => Value;
        
        /// <summary>
        /// CPython PyObject_IsTrue 호환: PyInt에서 C# bool 값 추출
        /// </summary>
        public override bool PyBoolValue() => Value != 0;
        
        // === As* Methods: Type Conversion (PyInt → PyObject types) ===
        
        /// <summary>
        /// CPython 호환: PyInt를 PyInt로 변환 (자기 자신 반환)
        /// </summary>
        public override PyInt AsInt()
        {
            return this; // 이미 PyInt이므로 자기 자신 반환
        }
        
        /// <summary>
        /// CPython 호환: PyInt를 PyFloat로 변환
        /// </summary>
        public override PyFloat AsFloat()
        {
            return new PyFloat(Value);
        }
        
        /// <summary>
        /// CPython 호환: PyInt를 PyBool로 변환
        /// </summary>
        public override PyBool AsBool()
        {
            return PyBool.FromBool(Value != 0);
        }
        
        /// <summary>
        /// CPython 호환: PyInt를 PyString으로 변환
        /// </summary>
        public override PyString AsString()
        {
            return new PyString(Value.ToString());
        }

        #endregion

        #region Number Base Methods

        public PyString Bin() => new PyString("0b" + Convert.ToString(Value, 2));
        public PyString Oct() => new PyString("0o" + Convert.ToString(Value, 8));
        public PyString Hex() => new PyString("0x" + Convert.ToString(Value, 16));

        #endregion

        #region Bit Manipulation

        /// <summary>
        /// int.bit_length() - 이진 표현에서 부호 비트를 제외한 비트 수
        /// </summary>
        public PyInt BitLength()
        {
            if (Value == 0) return new PyInt(0);
            var abs = Math.Abs(Value);
            return new PyInt((int)Math.Floor(Math.Log(abs, 2)) + 1);
        }

        /// <summary>
        /// int.bit_count() - 1인 비트의 개수
        /// </summary>
        public PyInt BitCount()
        {
            Py_int_t count = 0;
            var n = Math.Abs(Value);
            while (n > 0)
            {
                count += n & 1;
                n >>= 1;
            }
            return new PyInt(count);
        }

        #endregion

        #region Static Factory Methods

        public static PyInt FromString(string s, int baseValue = 10)
        {
            try
            {
                s = s.Trim();

                // 진법 접두사 처리
                if (baseValue == 0)
                {
                    if (s.StartsWith("0x") || s.StartsWith("0X"))
                    {
                        baseValue = 16;
                        s = s.Substring(2);
                    }
                    else if (s.StartsWith("0b") || s.StartsWith("0B"))
                    {
                        baseValue = 2;
                        s = s.Substring(2);
                    }
                    else if (s.StartsWith("0o") || s.StartsWith("0O"))
                    {
                        baseValue = 8;
                        s = s.Substring(2);
                    }
                    else
                    {
                        baseValue = 10;
                    }
                }

                Py_int_t result = Convert.ToInt64(s, baseValue);
                return new PyInt(result);
            }
            catch (Exception)
            {
                throw PyValueError.Create($"invalid literal for int() with base {baseValue}: '{s}'");
            }
        }

        public static PyInt FromNumber(PyObject obj)
        {
            if (obj is PyInt pyInt)
                return pyInt;
            if (obj is PyFloat pyFloat)
                return new PyInt((Py_int_t)Math.Truncate(pyFloat.Value));
            if (obj is PyBool pyBool)
                return new PyInt(pyBool.Value ? 1 : 0);
            throw PyTypeError.Create($"int() argument must be a string, a bytes-like object or a number, not '{obj.GetTypeName()}'");
        }

        #endregion

        #region Constants

        public static readonly PyInt Zero = new PyInt(0);
        public static readonly PyInt One = new PyInt(1);
        public static readonly PyInt MinusOne = new PyInt(-1);

        #endregion

        #region Evaluate Method (NotImplementedException)

        public PyObject Evaluate(PyScope scope)
        {
            // Integer literals evaluate to themselves (CPython style)
            return this;
        }

        #endregion
    }
}