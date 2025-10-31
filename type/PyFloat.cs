using System;

namespace SharpPy
{
    /// <summary>
    /// Python float 타입 구현 - C# double을 기반으로 한 부동소수점 숫자
    /// CPython 호환을 위해 원본 문자열 표현 보존
    /// </summary>
    public class PyFloat : PyObject
    {
        #region Core Properties

        public double Value { get; }
        public string OriginalString { get; }

        public PyFloat(double value) : this(value, null) { }

        public PyFloat(double value, string originalString)
        {
            Value = value;
            OriginalString = originalString;
        }

        public override PyType GetPyType() => PyType.FloatType;
        public override string GetTypeName() => "float";

        #endregion

        #region String Representation

        public override PyString ToStr()
        {
            // CPython 호환: 원본 문자열이 있으면 우선 사용
            if (!string.IsNullOrEmpty(OriginalString))
            {
                // 원본 문자열이 유효한 표현인지 확인
                if (double.TryParse(OriginalString, out double parsed) && Math.Abs(parsed - Value) < 1e-15)
                {
                    return new PyString(OriginalString);
                }
            }

            // Python처럼 필요시에만 소수점 표시
            if (Value == Math.Floor(Value) && !double.IsInfinity(Value) && !double.IsNaN(Value))
            {
                return new PyString(Value.ToString("0.0"));
            }
            return new PyString(Value.ToString("G")); // CPython 호환: shortest round-trip representation
        }

        public override PyString ToRepr()
        {
            if (double.IsPositiveInfinity(Value)) return new PyString("inf");
            if (double.IsNegativeInfinity(Value)) return new PyString("-inf");
            if (double.IsNaN(Value)) return new PyString("nan");
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

        public override PyObject Add(PyObject other)
        {
            return other switch
            {
                PyFloat otherFloat => new PyFloat(Value + otherFloat.Value),
                PyInt otherInt => new PyFloat(Value + otherInt.Value),
                PyBool otherBool => new PyFloat(Value + (otherBool.Value ? 1.0 : 0.0)),
                _ => PyNotImplemented.Instance
            };
        }

        public override PyObject Subtract(PyObject other)
        {
            return other switch
            {
                PyFloat otherFloat => new PyFloat(Value - otherFloat.Value),
                PyInt otherInt => new PyFloat(Value - otherInt.Value),
                PyBool otherBool => new PyFloat(Value - (otherBool.Value ? 1.0 : 0.0)),
                _ => PyNotImplemented.Instance
            };
        }

        public override PyObject Multiply(PyObject other)
        {
            return other switch
            {
                PyFloat otherFloat => new PyFloat(Value * otherFloat.Value),
                PyInt otherInt => new PyFloat(Value * otherInt.Value),
                PyBool otherBool => new PyFloat(Value * (otherBool.Value ? 1.0 : 0.0)),
                _ => PyNotImplemented.Instance
            };
        }

        public override PyObject Divide(PyObject other)
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

        public override PyObject FloorDivide(PyObject other)
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

        public override PyObject Modulo(PyObject other)
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

        public override PyObject Power(PyObject other)
        {

            var otherValue = other switch
            {
                PyFloat otherFloat => otherFloat.Value,
                PyInt otherInt => (double)otherInt.Value,
                PyBool otherBool => otherBool.Value ? 1.0 : 0.0,
                _ => throw PyTypeError.Create($"unsupported operand type(s) for ** or pow(): 'float' and '{other.GetTypeName()}'")
            };

            // 특수 케이스 처리: negative base with non-integer exponent
            // CPython: Returns complex number (Objects/floatobject.c:float_pow)
            if (Value < 0 && !IsIntegral(otherValue))
            {
                // Return complex number: (-2.0) ** 0.5 → (8.66e-17+1.414j)
                var complexBase = new PyComplex(Value, 0);
                var complexExponent = new PyComplex(otherValue, 0);
                return complexBase.Power(complexExponent);
            }

            var result = Math.Pow(Value, otherValue);
            return new PyFloat(result);
        }

        #endregion

        #region Unary Operations

        public override PyObject Negative()
        {
            return new PyFloat(-Value);
        }

        public override PyObject Positive()
        {
            return this; // +x는 x와 같음
        }

        public PyObject Absolute()
        {
            return new PyFloat(Math.Abs(Value));
        }

        #endregion

        #region Type Conversion (CPython Compatible)

        // === To* Methods: Value Extraction (PyFloat → C# basic types) ===
        
        /// <summary>
        /// CPython PyLong_AsLong 호환: PyFloat에서 C# int 값 추출
        /// </summary>
        public override int ToInt()
        {
            if (double.IsInfinity(Value) || double.IsNaN(Value))
                throw PyOverflowError.Create("cannot convert float infinity to integer");

            if (Value > int.MaxValue || Value < int.MinValue)
                throw PyOverflowError.Create("int too big to convert to float");

            return (int)Math.Truncate(Value);
        }

        /// <summary>
        /// CPython PyFloat_AsDouble 호환: PyFloat에서 C# double 값 추출
        /// </summary>
        public override double ToFloat()
        {
            return Value;
        }

        /// <summary>
        /// CPython PyObject_IsTrue 호환: PyFloat에서 C# bool 값 추출
        /// </summary>
        public override bool PyBoolValue()
        {
            return Value != 0.0 && !double.IsNaN(Value);
        }
        
        // === As* Methods: Type Conversion (PyFloat → PyObject types) ===
        
        /// <summary>
        /// CPython 호환: PyFloat를 PyFloat로 변환 (자기 자신 반환)
        /// </summary>
        public override PyFloat AsFloat()
        {
            return this; // 이미 PyFloat이므로 자기 자신 반환
        }
        
        /// <summary>
        /// CPython 호환: PyFloat를 PyInt로 변환
        /// </summary>
        public override PyInt AsInt()
        {
            return new PyInt(ToInt());
        }
        
        /// <summary>
        /// CPython 호환: PyFloat를 PyBool로 변환
        /// </summary>
        public override PyBool AsBool()
        {
            return PyBool.FromBool(PyBoolValue());
        }
        
        /// <summary>
        /// CPython 호환: PyFloat를 PyString으로 변환
        /// </summary>
        public override string AsString()
        {
            // CPython의 float.__str__() 동작 모방
            if (double.IsNaN(Value))
                return "nan";
            if (double.IsPositiveInfinity(Value))
                return "inf";
            if (double.IsNegativeInfinity(Value))
                return "-inf";

            return Value.ToString();
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
            // Float literals evaluate to themselves (CPython style)
            return this;
        }

        #endregion
    }
}