using System;

namespace SharpPy
{
    /// <summary>
    /// Python complex 타입 구현 - 복소수 (real + imag*j)
    /// </summary>
    public class PyComplex : PyObject
    {
        #region Core Properties

        public double Real { get; }
        public double Imag { get; }

        public PyComplex(double real, double imag = 0.0)
        {
            Real = real;
            Imag = imag;
        }

        public override PyType GetPyType() => PyType.ComplexType;
        public override string GetTypeName() => "complex";

        #endregion

        #region String Representation

        public override PyString ToStr() => ToRepr();

        public override PyString ToRepr()
        {
            if (Imag == 0.0)
            {
                return new PyString($"({FormatNumber(Real)}+0j)");
            }
            else if (Real == 0.0)
            {
                return new PyString($"{FormatImaginary(Imag)}j");
            }
            else
            {
                var imagPart = Imag >= 0 ? $"+{FormatImaginary(Imag)}j" : $"{FormatImaginary(Imag)}j";
                return new PyString($"({FormatNumber(Real)}{imagPart})");
            }
        }

        private string FormatNumber(double value)
        {
            if (double.IsPositiveInfinity(value)) return "inf";
            if (double.IsNegativeInfinity(value)) return "-inf";
            if (double.IsNaN(value)) return "nan";
            
            // Python처럼 정수값은 소수점 없이 표시
            if (value == Math.Floor(value) && !double.IsInfinity(value))
                return value.ToString("0");
            
            return value.ToString("G");
        }

        /// <summary>
        /// CPython 3.12: Objects/complexobject.c:100-130 (complex_format)
        /// </summary>
        private string FormatImaginary(double value)
        {
            if (double.IsPositiveInfinity(value)) return "inf";
            if (double.IsNegativeInfinity(value)) return "-inf";
            if (double.IsNaN(value)) return "nan";

            // CPython: 1j outputs as "1j", not "j"
            if (value == Math.Floor(value) && !double.IsInfinity(value))
                return value.ToString("0");

            return value.ToString("G");
        }

        #endregion

        #region Attribute Access

        /// <summary>
        /// CPython 3.12: Objects/complexobject.c:398-410 (complex_getset)
        /// </summary>
        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "real":
                    return new PyFloat(Real);
                case "imag":
                    return new PyFloat(Imag);
                case "conjugate":
                    // CPython 3.12: Return a bound method - instance already bound
                    var conjugateMethod = new PyBuiltinMethod("conjugate", (s, args) => ((PyComplex)s).Conjugate(), 0);
                    return new PyBoundBuiltinMethod(this, conjugateMethod);
                case "__abs__":
                    var absMethod = new PyBuiltinMethod("__abs__", (s, args) => ((PyComplex)s).Absolute(), 0);
                    return new PyBoundBuiltinMethod(this, absMethod);
                default:
                    return base.GetAttribute(name);
            }
        }

        #endregion

        #region Hash and Equality

        public override int ToHash()
        {
            // Python complex hash algorithm
            if (Imag == 0.0)
            {
                // 실수부만 있는 경우 float hash와 동일
                return new PyFloat(Real).ToHash();
            }
            
            return HashCode.Combine(Real, Imag);
        }

        protected override PyObject PyEquals(PyObject other)
        {
            return other switch
            {
                PyComplex otherComplex => PyBool.FromBool(
                    Real == otherComplex.Real && Imag == otherComplex.Imag),
                // CPython 3.12: Objects/complexobject.c:395-430 - complex_richcompare
                PyFloat otherFloat => PyBool.FromBool(
                    Imag == 0.0 && Real == otherFloat.Value),
                PyInt otherInt => PyBool.FromBool(
                    Imag == 0.0 && Real == (double)otherInt.Value),
                PyBool otherBool => PyBool.FromBool(
                    Imag == 0.0 && Real == (otherBool.Value ? 1.0 : 0.0)),
                _ => PyBool.False
            };
        }

        #endregion

        #region Arithmetic Operations

        public override PyObject Add(PyObject other)
        {
            // CPython 3.12: Objects/complexobject.c:305-328 - complex_add
            return other switch
            {
                PyComplex otherComplex => new PyComplex(
                    Real + otherComplex.Real,
                    Imag + otherComplex.Imag),
                PyFloat otherFloat => new PyComplex(
                    Real + otherFloat.Value, Imag),
                PyInt otherInt => new PyComplex(
                    Real + (double)otherInt.Value, Imag),
                PyBool otherBool => new PyComplex(
                    Real + (otherBool.Value ? 1.0 : 0.0), Imag),
                _ => throw PyTypeError.Create($"unsupported operand type(s) for +: 'complex' and '{other.GetTypeName()}'")
            };
        }

        public override PyObject Subtract(PyObject other)
        {
            // CPython 3.12: Objects/complexobject.c:330-353 - complex_sub
            return other switch
            {
                PyComplex otherComplex => new PyComplex(
                    Real - otherComplex.Real,
                    Imag - otherComplex.Imag),
                PyFloat otherFloat => new PyComplex(
                    Real - otherFloat.Value, Imag),
                PyInt otherInt => new PyComplex(
                    Real - (double)otherInt.Value, Imag),
                PyBool otherBool => new PyComplex(
                    Real - (otherBool.Value ? 1.0 : 0.0), Imag),
                _ => throw PyTypeError.Create($"unsupported operand type(s) for -: 'complex' and '{other.GetTypeName()}'")
            };
        }

        public override PyObject Multiply(PyObject other)
        {
            // CPython 3.12: Objects/complexobject.c:279-303 - complex_mul
            return other switch
            {
                PyComplex otherComplex => new PyComplex(
                    Real * otherComplex.Real - Imag * otherComplex.Imag,
                    Real * otherComplex.Imag + Imag * otherComplex.Real),
                PyFloat otherFloat => new PyComplex(
                    Real * otherFloat.Value, Imag * otherFloat.Value),
                PyInt otherInt => new PyComplex(
                    Real * (double)otherInt.Value, Imag * (double)otherInt.Value),
                PyBool otherBool => otherBool.Value ? this : new PyComplex(0, 0),
                _ => throw PyTypeError.Create($"unsupported operand type(s) for *: 'complex' and '{other.GetTypeName()}'")
            };
        }

        public override PyObject Divide(PyObject other)
        {
            // CPython 3.12: Objects/complexobject.c:206-241 - complex_div
            return other switch
            {
                PyComplex otherComplex => DivideComplex(otherComplex),
                PyFloat otherFloat => CheckZeroDivision(otherFloat.Value) ?
                    new PyComplex(Real / otherFloat.Value, Imag / otherFloat.Value) :
                    throw PyZeroDivisionError.Create("complex division by zero"),
                PyInt otherInt => CheckZeroDivision((double)otherInt.Value) ?
                    new PyComplex(Real / (double)otherInt.Value, Imag / (double)otherInt.Value) :
                    throw PyZeroDivisionError.Create("complex division by zero"),
                PyBool otherBool => otherBool.Value ?
                    this : throw PyZeroDivisionError.Create("complex division by zero"),
                _ => throw PyTypeError.Create($"unsupported operand type(s) for /: 'complex' and '{other.GetTypeName()}'")
            };
        }

        private PyComplex DivideComplex(PyComplex other)
        {
            var denominator = other.Real * other.Real + other.Imag * other.Imag;

            if (denominator == 0.0)
                throw PyZeroDivisionError.Create("complex division by zero");

            var realPart = (Real * other.Real + Imag * other.Imag) / denominator;
            var imagPart = (Imag * other.Real - Real * other.Imag) / denominator;

            return new PyComplex(realPart, imagPart);
        }

        private bool CheckZeroDivision(double value) => value != 0.0;

        public override PyObject Power(PyObject other)
        {
            // CPython 3.12: Objects/complexobject.c:243-277 - complex_pow
            return other switch
            {
                PyComplex otherComplex => PowerComplex(otherComplex),
                PyFloat otherFloat => PowerComplex(new PyComplex(otherFloat.Value)),
                PyInt otherInt => PowerComplex(new PyComplex((double)otherInt.Value)),
                PyBool otherBool => PowerComplex(new PyComplex(otherBool.Value ? 1.0 : 0.0)),
                _ => throw PyTypeError.Create($"unsupported operand type(s) for **: 'complex' and '{other.GetTypeName()}'")
            };
        }

        private PyComplex PowerComplex(PyComplex exponent)
        {
            // z^w = exp(w * log(z))
            if (Real == 0.0 && Imag == 0.0)
            {
                if (exponent.Real == 0.0 && exponent.Imag == 0.0)
                    return new PyComplex(1.0, 0.0); // 0^0 = 1 in Python
                if (exponent.Real > 0.0)
                    return new PyComplex(0.0, 0.0);
                throw PyZeroDivisionError.Create("0.0 to a negative or complex power");
            }

            var magnitude = Math.Sqrt(Real * Real + Imag * Imag);
            var phase = Math.Atan2(Imag, Real);
            
            var logMagnitude = Math.Log(magnitude);
            var newMagnitude = Math.Exp(exponent.Real * logMagnitude - exponent.Imag * phase);
            var newPhase = exponent.Imag * logMagnitude + exponent.Real * phase;
            
            return new PyComplex(
                newMagnitude * Math.Cos(newPhase),
                newMagnitude * Math.Sin(newPhase));
        }

        public PyObject Negate()
        {
            return new PyComplex(-Real, -Imag);
        }

        public PyObject Positive()
        {
            return this;
        }

        public PyObject Absolute()
        {
            return new PyFloat(Math.Sqrt(Real * Real + Imag * Imag));
        }

        #endregion

        #region Complex-specific Methods

        /// <summary>
        /// 켤레복소수 반환 complex.conjugate()
        /// </summary>
        public PyComplex Conjugate()
        {
            return new PyComplex(Real, -Imag);
        }

        /// <summary>
        /// 복소수의 크기(절댓값) 반환
        /// </summary>
        public PyFloat Magnitude()
        {
            return new PyFloat(Math.Sqrt(Real * Real + Imag * Imag));
        }

        /// <summary>
        /// 복소수의 위상(각도) 반환 (라디안)
        /// </summary>
        public PyFloat Phase()
        {
            return new PyFloat(Math.Atan2(Imag, Real));
        }

        /// <summary>
        /// 극좌표 형태로 변환 (magnitude, phase)
        /// </summary>
        public PyTuple ToPolar()
        {
            return new PyTuple(new PyObject[] { Magnitude(), Phase() });
        }

        #endregion

        #region Type Conversion

        /// <summary>
        /// 복소수를 부동소수점으로 변환 (허수부가 0이어야 함)
        /// </summary>
        public PyFloat ToFloat()
        {
            if (Imag != 0.0)
                throw PyTypeError.Create("can't convert complex to float");
            return new PyFloat(Real);
        }

        /// <summary>
        /// 복소수를 정수로 변환 (허수부가 0이어야 함)
        /// </summary>
        public PyInt ToInt()
        {
            if (Imag != 0.0)
                throw PyTypeError.Create("can't convert complex to int");
            
            if (Real > int.MaxValue || Real < int.MinValue)
                throw PyOverflowError.Create("int too big to convert to int");
            
            return new PyInt((int)Real);
        }

        #endregion

        #region Type Checking

        public override bool PyBoolValue() => Real != 0.0 || Imag != 0.0;

        #endregion

        #region Static Factory Methods

        /// <summary>
        /// 실수부만으로 복소수 생성
        /// </summary>
        public static PyComplex FromReal(double real) => new PyComplex(real, 0.0);

        /// <summary>
        /// 허수부만으로 복소수 생성
        /// </summary>
        public static PyComplex FromImaginary(double imag) => new PyComplex(0.0, imag);

        /// <summary>
        /// 극좌표에서 복소수 생성
        /// </summary>
        public static PyComplex FromPolar(double magnitude, double phase)
        {
            return new PyComplex(
                magnitude * Math.Cos(phase),
                magnitude * Math.Sin(phase));
        }

        /// <summary>
        /// 문자열에서 복소수 파싱
        /// CPython 3.12: Objects/complexobject.c:complex_subtype_from_string
        /// </summary>
        public static PyComplex FromString(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw PyValueError.Create("complex() arg is an empty string");

            // Remove all spaces
            value = value.Trim().Replace(" ", "");

            // Special cases: pure imaginary
            if (value == "j" || value == "+j")
                return new PyComplex(0, 1);
            if (value == "-j")
                return new PyComplex(0, -1);

            // Check if it ends with 'j' (imaginary component)
            bool hasImaginary = value.EndsWith("j");
            if (hasImaginary)
                value = value.Substring(0, value.Length - 1);  // Remove 'j'

            // Try parsing as real number only
            if (!hasImaginary)
            {
                if (double.TryParse(value, out double real))
                    return new PyComplex(real, 0);

                throw PyValueError.Create($"complex() arg is a malformed string");
            }

            // Has imaginary component - check for real+imaginary format
            // Find the last + or - that's not at the beginning
            int opIndex = -1;
            for (int i = value.Length - 1; i > 0; i--)
            {
                if (value[i] == '+' || value[i] == '-')
                {
                    opIndex = i;
                    break;
                }
            }

            if (opIndex > 0)
            {
                // Format: "real+imagj" or "real-imagj"
                string realPart = value.Substring(0, opIndex);
                string imagPart = value.Substring(opIndex);

                if (!double.TryParse(realPart, out double real))
                    throw PyValueError.Create($"complex() arg is a malformed string");

                // Handle cases like "+2j" or "-2j" where imagPart is "+2" or "-2"
                if (imagPart == "+" || imagPart == "")
                    return new PyComplex(real, 1);
                if (imagPart == "-")
                    return new PyComplex(real, -1);

                if (!double.TryParse(imagPart, out double imag))
                    throw PyValueError.Create($"complex() arg is a malformed string");

                return new PyComplex(real, imag);
            }
            else
            {
                // Format: "imagj" (pure imaginary)
                if (string.IsNullOrEmpty(value) || value == "+")
                    return new PyComplex(0, 1);
                if (value == "-")
                    return new PyComplex(0, -1);

                if (!double.TryParse(value, out double imag))
                    throw PyValueError.Create($"complex() arg is a malformed string");

                return new PyComplex(0, imag);
            }
        }

        /// <summary>
        /// 상수들
        /// </summary>
        public static readonly PyComplex Zero = new PyComplex(0.0, 0.0);
        public static readonly PyComplex One = new PyComplex(1.0, 0.0);
        public static readonly PyComplex ImaginaryUnit = new PyComplex(0.0, 1.0);

        #endregion

        #region Comparison (Limited for Complex Numbers)

        // 복소수는 일반적으로 크기 비교가 불가능하므로 RichCompare로 처리
        // PyObject의 기본 RichCompare 메서드를 사용

        #endregion

        #region Evaluate Method (NotImplementedException)

        public PyObject Evaluate(PyScope scope)
        {
            // Complex literals evaluate to themselves (CPython style)
            return this;
        }

        #endregion
    }
}