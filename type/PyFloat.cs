using System;

namespace SharpPy
{
    /// <summary>
    /// Python float 타입 구현 - C# double을 기반으로 한 부동소수점 숫자
    /// </summary>
    public class PyFloat : PyObject
    {
        #region Core Properties

        public double Value { get; }

        public PyFloat(double value) => Value = value;

        public override PyType GetPyType() => PyType.FloatType;
        public override string GetTypeName() => "float";

        #endregion

        #region String Representation

        public override string ToStr()
        {
            // Python처럼 필요시에만 소수점 표시
            if (Value == Math.Floor(Value) && !double.IsInfinity(Value) && !double.IsNaN(Value))
            {
                return Value.ToString("0.0");
            }
            return Value.ToString("G17"); // 17자리 정밀도로 표현
        }

        public override string ToRepr()
        {
            if (double.IsPositiveInfinity(Value)) return "inf";
            if (double.IsNegativeInfinity(Value)) return "-inf";
            if (double.IsNaN(Value)) return "nan";
            return ToStr();
        }

        #endregion

        #region Hash and Equality

        public override int ToHash()
        {
            // Python과 같은 방식으로 해시 계산
            if (double.IsInfinity(Value) || double.IsNaN(Value))
            {
                return Value.GetHashCode();
            }
            
            // 정수와 같은 값이면 같은 해시값 (Python 규칙)
            if (Value == Math.Floor(Value) && Value >= int.MinValue && Value <= int.MaxValue)
            {
                return ((int)Value).GetHashCode();
            }
            
            return Value.GetHashCode();
        }

        protected override PyObject PyEquals(PyObject other)
        {
            return other switch
            {
                PyFloat otherFloat => PyBool.FromBool(Value == otherFloat.Value),
                PyInt otherInt => PyBool.FromBool(Value == otherInt.Value),
                PyBool otherBool => PyBool.FromBool(Value == (otherBool.Value ? 1.0 : 0.0)),
                _ => PyBool.False
            };
        }

        #endregion

        #region Comparison Operations

        protected override PyObject PyLess(PyObject other)
        {
            return other switch
            {
                PyFloat otherFloat => PyBool.FromBool(Value < otherFloat.Value),
                PyInt otherInt => PyBool.FromBool(Value < otherInt.Value),
                PyBool otherBool => PyBool.FromBool(Value < (otherBool.Value ? 1.0 : 0.0)),
                _ => throw PyTypeError.Create($"'<' not supported between instances of 'float' and '{other.GetTypeName()}'")
            };
        }

        protected override PyObject PyLessEqual(PyObject other)
        {
            return other switch
            {
                PyFloat otherFloat => PyBool.FromBool(Value <= otherFloat.Value),
                PyInt otherInt => PyBool.FromBool(Value <= otherInt.Value),
                PyBool otherBool => PyBool.FromBool(Value <= (otherBool.Value ? 1.0 : 0.0)),
                _ => throw PyTypeError.Create($"'<=' not supported between instances of 'float' and '{other.GetTypeName()}'")
            };
        }

        protected override PyObject PyGreater(PyObject other)
        {
            return other switch
            {
                PyFloat otherFloat => PyBool.FromBool(Value > otherFloat.Value),
                PyInt otherInt => PyBool.FromBool(Value > otherInt.Value),
                PyBool otherBool => PyBool.FromBool(Value > (otherBool.Value ? 1.0 : 0.0)),
                _ => throw PyTypeError.Create($"'>' not supported between instances of 'float' and '{other.GetTypeName()}'")
            };
        }

        protected override PyObject PyGreaterEqual(PyObject other)
        {
            return other switch
            {
                PyFloat otherFloat => PyBool.FromBool(Value >= otherFloat.Value),
                PyInt otherInt => PyBool.FromBool(Value >= otherInt.Value),
                PyBool otherBool => PyBool.FromBool(Value >= (otherBool.Value ? 1.0 : 0.0)),
                _ => throw PyTypeError.Create($"'>=' not supported between instances of 'float' and '{other.GetTypeName()}'")
            };
        }

        #endregion

        #region Arithmetic Operations

        public PyObject Add(PyObject other)
        {
            return other switch
            {
                PyFloat otherFloat => new PyFloat(Value + otherFloat.Value),
                PyInt otherInt => new PyFloat(Value + otherInt.Value),
                PyBool otherBool => new PyFloat(Value + (otherBool.Value ? 1.0 : 0.0)),
                _ => PyNotImplemented.Instance
            };
        }

        public PyObject Subtract(PyObject other)
        {
            return other switch
            {
                PyFloat otherFloat => new PyFloat(Value - otherFloat.Value),
                PyInt otherInt => new PyFloat(Value - otherInt.Value),
                PyBool otherBool => new PyFloat(Value - (otherBool.Value ? 1.0 : 0.0)),
                _ => PyNotImplemented.Instance
            };
        }

        public PyObject Multiply(PyObject other)
        {
            return other switch
            {
                PyFloat otherFloat => new PyFloat(Value * otherFloat.Value),
                PyInt otherInt => new PyFloat(Value * otherInt.Value),
                PyBool otherBool => new PyFloat(Value * (otherBool.Value ? 1.0 : 0.0)),
                _ => PyNotImplemented.Instance
            };
        }

        public PyObject TrueDivide(PyObject other)
        {
            var otherValue = other switch
            {
                PyFloat otherFloat => otherFloat.Value,
                PyInt otherInt => (double)otherInt.Value,
                PyBool otherBool => otherBool.Value ? 1.0 : 0.0,
                _ => throw PyTypeError.Create($"unsupported operand type(s) for /: 'float' and '{other.GetTypeName()}'")
            };

            if (otherValue == 0.0)
                throw PyZeroDivisionError.Create("float division by zero");

            return new PyFloat(Value / otherValue);
        }

        public PyObject FloorDivide(PyObject other)
        {
            var otherValue = other switch
            {
                PyFloat otherFloat => otherFloat.Value,
                PyInt otherInt => (double)otherInt.Value,
                PyBool otherBool => otherBool.Value ? 1.0 : 0.0,
                _ => throw PyTypeError.Create($"unsupported operand type(s) for //: 'float' and '{other.GetTypeName()}'")
            };

            if (otherValue == 0.0)
                throw PyZeroDivisionError.Create("float floor division by zero");

            return new PyFloat(Math.Floor(Value / otherValue));
        }

        public PyObject Modulo(PyObject other)
        {
            var otherValue = other switch
            {
                PyFloat otherFloat => otherFloat.Value,
                PyInt otherInt => (double)otherInt.Value,
                PyBool otherBool => otherBool.Value ? 1.0 : 0.0,
                _ => throw PyTypeError.Create($"unsupported operand type(s) for %: 'float' and '{other.GetTypeName()}'")
            };

            if (otherValue == 0.0)
                throw PyZeroDivisionError.Create("float modulo");

            return new PyFloat(Value % otherValue);
        }

        public PyObject Power(PyObject other, PyObject modulus = null)
        {
            if (modulus != null)
                throw PyTypeError.Create("pow() 3rd argument not allowed unless all arguments are integers");

            var otherValue = other switch
            {
                PyFloat otherFloat => otherFloat.Value,
                PyInt otherInt => (double)otherInt.Value,
                PyBool otherBool => otherBool.Value ? 1.0 : 0.0,
                _ => throw PyTypeError.Create($"unsupported operand type(s) for ** or pow(): 'float' and '{other.GetTypeName()}'")
            };

            var result = Math.Pow(Value, otherValue);
            
            // 특수 케이스 처리
            if (double.IsNaN(result))
            {
                if (Value < 0 && !IsIntegral(otherValue))
                    throw PyValueError.Create("negative number cannot be raised to a fractional power");
            }

            return new PyFloat(result);
        }

        #endregion

        #region Unary Operations

        public PyObject Negative()
        {
            return new PyFloat(-Value);
        }

        public PyObject Positive()
        {
            return this; // +x는 x와 같음
        }

        public PyObject Absolute()
        {
            return new PyFloat(Math.Abs(Value));
        }

        #endregion

        #region Type Conversion

        public override int ToInt()
        {
            if (double.IsInfinity(Value) || double.IsNaN(Value))
                throw PyOverflowError.Create("cannot convert float infinity to integer");

            if (Value > int.MaxValue || Value < int.MinValue)
                throw PyOverflowError.Create("int too big to convert to float");

            return (int)Math.Truncate(Value);
        }

        public override double ToFloat()
        {
            return Value;
        }

        public override bool PyBoolValue()
        {
            return Value != 0.0 && !double.IsNaN(Value);
        }

        #endregion

        #region Math Methods (Python Built-in Functions)

        /// <summary>
        /// float.is_finite() - 무한대나 NaN이 아닌지 검사
        /// </summary>
        public PyBool IsFinite()
        {
            return PyBool.FromBool(double.IsFinite(Value));
        }

        /// <summary>
        /// float.is_infinite() - 무한대인지 검사
        /// </summary>
        public PyBool IsInfinite()
        {
            return PyBool.FromBool(double.IsInfinity(Value));
        }

        /// <summary>
        /// float.is_nan() - NaN인지 검사
        /// </summary>
        public PyBool IsNaN()
        {
            return PyBool.FromBool(double.IsNaN(Value));
        }

        /// <summary>
        /// float.is_integer() - 정수값인지 검사
        /// </summary>
        public PyBool IsInteger()
        {
            return PyBool.FromBool(IsIntegral(Value));
        }

        /// <summary>
        /// float.hex() - 16진수 문자열로 변환
        /// </summary>
        public PyString Hex()
        {
            if (double.IsNaN(Value)) return new PyString("nan");
            if (double.IsPositiveInfinity(Value)) return new PyString("inf");
            if (double.IsNegativeInfinity(Value)) return new PyString("-inf");
            
            // C#의 BitConverter를 사용하여 IEEE 754 표현으로 변환
            var bytes = BitConverter.GetBytes(Value);
            var hex = BitConverter.ToString(bytes).Replace("-", "").ToLower();
            return new PyString($"0x{hex}p+0"); // 간단한 구현
        }

        #endregion

        #region Helper Methods

        private static bool IsIntegral(double value)
        {
            return !double.IsInfinity(value) && !double.IsNaN(value) && value == Math.Floor(value);
        }

        #endregion

        #region Static Factory Methods

        /// <summary>
        /// 문자열에서 PyFloat 생성
        /// </summary>
        public static PyFloat FromString(string s)
        {
            s = s.Trim().ToLower();
            
            // 특수 값들
            if (s == "inf" || s == "infinity" || s == "+inf" || s == "+infinity")
                return new PyFloat(double.PositiveInfinity);
            if (s == "-inf" || s == "-infinity")
                return new PyFloat(double.NegativeInfinity);
            if (s == "nan")
                return new PyFloat(double.NaN);

            if (double.TryParse(s, out double result))
                return new PyFloat(result);
            
            throw PyValueError.Create($"could not convert string to float: '{s}'");
        }

        /// <summary>
        /// 다른 숫자 타입에서 PyFloat 생성
        /// </summary>
        public static PyFloat FromNumber(PyObject obj)
        {
            return obj switch
            {
                PyFloat pyFloat => pyFloat,
                PyInt pyInt => new PyFloat(pyInt.Value),
                PyBool pyBool => new PyFloat(pyBool.Value ? 1.0 : 0.0),
                _ => throw PyTypeError.Create($"float() argument must be a string or a number, not '{obj.GetTypeName()}'")
            };
        }

        #endregion

        #region Constants

        public static readonly PyFloat Zero = new PyFloat(0.0);
        public static readonly PyFloat One = new PyFloat(1.0);
        public static readonly PyFloat PositiveInfinity = new PyFloat(double.PositiveInfinity);
        public static readonly PyFloat NegativeInfinity = new PyFloat(double.NegativeInfinity);
        public static readonly PyFloat NaN = new PyFloat(double.NaN);

        #endregion

        #region Evaluate Method (NotImplementedException)

        /// <summary>
        /// Evaluate 메서드 - 나중에 구현
        /// </summary>
        public PyObject Evaluate(PyScope scope)
        {
            throw new NotImplementedException("PyFloat.Evaluate() - 나중에 구현예정");
        }

        #endregion
    }
}