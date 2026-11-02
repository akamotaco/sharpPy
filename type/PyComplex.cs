using System;
using System.Collections.Generic;
using System.Text;

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

        // complex_subtype_from_c_complex: complex_new builds the value, the subclass gets it.
        internal static void InstallNew()
        {
            var type = PyType.ComplexType;
            type.TypeDict["__new__"] = new PyStaticBuiltinMethod("__new__", (args, kwargs) =>
            {
                var rest = args[1..];
                var value = (PyComplex)PyBuiltinFunction.CallComplex(rest, kwargs);
                return args[0] is PyClass cls && !ReferenceEquals(cls, type)
                    ? new PyComplexSubclass(cls, value.Real, value.Imag)
                    : value;
            }, type);
        }

        static PyComplex()
        {
            // CPython 3.12: Objects/complexobject.c complex_format via float mini-language.
            PyType.ComplexType.TypeDict["__format__"] = new PyMethodDescriptor(
                "__format__", PyType.ComplexType,
                (self, args, kwargs) =>
                {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"complex.__format__() takes exactly one argument ({args.Length} given)");
                    if (args[0] is not PyString specStr)
                        throw PyTypeError.Create($"__format__() argument 1 must be str, not {args[0].GetTypeName()}");
                    if (self is not PyComplex cx)
                        throw WrongComplexSelf("__format__", self);
                    return new PyString(FormatComplex(cx.Real, cx.Imag, specStr.Value));
                },
                minArgs: 1, maxArgs: 1);

            // Objects/complexobject.c:398-410 publishes real/imag as getsets and conjugate as a method
            // in the type dict. They used to live only in a GetAttribute override below, which made
            // hasattr(complex, "real") False while (1+2j).real worked - two answers about one attribute.
            PyType.ComplexType.TypeDict["real"] = new PyMemberDescriptor(
                "real", PyType.ComplexType,
                getter: self => self is PyComplex cx ? new PyFloat(cx.Real)
                    : throw WrongComplexSelf("real", self));
            PyType.ComplexType.TypeDict["imag"] = new PyMemberDescriptor(
                "imag", PyType.ComplexType,
                getter: self => self is PyComplex cx ? new PyFloat(cx.Imag)
                    : throw WrongComplexSelf("imag", self));
            PyType.ComplexType.TypeDict["conjugate"] = new PyMethodDescriptor(
                "conjugate", PyType.ComplexType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create(
                            $"complex.conjugate() takes no arguments ({args.Length} given)");
                    if (self is not PyComplex cx)
                        throw WrongComplexSelf("conjugate", self);
                    // complex_conjugate builds a new object from the C value.
                    return new PyComplex(cx.Real, -cx.Imag);
                },
                minArgs: 0, maxArgs: 0);

            // Objects/complexobject.c:653-664 complex.__getnewargs__ -> (real, imag)
            PyType.ComplexType.TypeDict["__getnewargs__"] = new PyMethodDescriptor(
                "__getnewargs__", PyType.ComplexType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create(
                            $"complex.__getnewargs__() takes no arguments ({args.Length} given)");
                    if (self is not PyComplex cx)
                        throw WrongComplexSelf("__getnewargs__", self);
                    return new PyTuple(new PyFloat(cx.Real), new PyFloat(cx.Imag));
                },
                minArgs: 0, maxArgs: 0);
        }

        // Objects/descrobject.c descr_check wording.
        internal static System.Exception WrongComplexSelf(string name, PyObject self) =>
            PyTypeError.Create(
                $"descriptor '{name}' for 'complex' objects doesn't apply to a '{self.GetTypeName()}' object");

        /// <summary>
        /// CPython complex_format: format real and |imag| as floats, join with the
        /// imaginary sign, append 'j', then apply fill/align/width to the combined text.
        /// </summary>
        internal static string FormatComplex(double real, double imag, string spec)
        {
            if (string.IsNullOrEmpty(spec))
                return new PyComplex(real, imag).ToRepr().Value;
            var format = FormatSpec.Parse("complex", spec, '\0', '>');
            if (!(format.Type is '\0' or 'e' or 'E' or 'f' or 'F' or 'g' or 'G' or 'n'))
                throw FormatSpec.Unknown(format.Type, "complex");
            return FormatComplexInternal(real, imag, format);
        }

        // format_complex_internal
        private static string FormatComplexInternal(double real, double imag, FormatSpec format)
        {
            if (format.Precision > int.MaxValue) throw PyValueError.Create("precision too big");
            if (format.Width > int.MaxValue) throw PyMemoryError.Create();
            if (format.Fill == "0")
                throw PyValueError.Create("Zero padding is not allowed in complex format specifier");
            if (format.Align == '=')
                throw PyValueError.Create("'=' alignment flag is not allowed in complex format specifier");

            var part = format.Copy();
            part.Fill = " ";
            part.Align = '<';
            part.Width = -1;
            bool skipReal = false, parens = false;
            if (format.Type == '\0')
            {
                // Like str(self), or %g with a precision.
                part.Type = format.Precision < 0 ? 'r' : 'g';
                if (real == 0.0 && !double.IsNegative(real)) skipReal = true;
                else parens = true;
            }
            else if (format.Type == 'n')
            {
                part.Type = 'g';
            }

            string realText = skipReal ? "" : PyFloatFormatter.Render(real, part);
            if (!skipReal) part.Sign = '+';
            string body = realText + PyFloatFormatter.Render(imag, part) + "j";
            if (parens) body = "(" + body + ")";

            int length = new PyString(body).Length();
            long padding = System.Math.Max(0, format.Width - length);
            int left = format.Align == '>' ? (int)padding : format.Align == '^' ? (int)(padding / 2) : 0;
            var output = new StringBuilder();
            for (int n = 0; n < left; n++) output.Append(format.Fill);
            output.Append(body);
            for (int n = 0; n < padding - left; n++) output.Append(format.Fill);
            return output.ToString();
        }

        #endregion

        #region String Representation

        public override PyString AsString() => ToRepr();

        public override PyString ToRepr()
        {
            // complex_repr: with a real part of +0 there is neither real part nor parens.
            if (Real == 0.0 && !double.IsNegative(Real))
                return new PyString($"{FormatNumber(Imag)}j");
            string imag = FormatNumber(Imag);
            if (imag[0] != '-')
                imag = "+" + imag;
            return new PyString($"({FormatNumber(Real)}{imag}j)");
        }

        // complex_neg
        public override PyObject Negative() => new PyComplex(-Real, -Imag);

        private string FormatNumber(double value)
        {
            if (double.IsPositiveInfinity(value)) return "inf";
            if (double.IsNegativeInfinity(value)) return "-inf";
            if (double.IsNaN(value)) return "nan";
            
            // CPython complex repr prints each part with the shortest float repr,
            // omitting the trailing ".0" for integral parts (1e-05, 1e+20 styles).
            string text = PyFloat.FormatFloat(value, "");
            if (text.EndsWith(".0"))
                text = text.Substring(0, text.Length - 2);
            return text;
        }

        /// <summary>
        /// CPython 3.12: Objects/complexobject.c:100-130 (complex_format)
        /// </summary>
        private string FormatImaginary(double value)
        {
            if (double.IsPositiveInfinity(value)) return "inf";
            if (double.IsNegativeInfinity(value)) return "-inf";
            if (double.IsNaN(value)) return "nan";

            // CPython: 1j outputs as "1j", not "j"; same shortest-repr rule as real parts.
            string text = PyFloat.FormatFloat(value, "");
            if (text.EndsWith(".0"))
                text = text.Substring(0, text.Length - 2);
            return text;
        }

        #endregion

        #region Attribute Access

        /// <summary>
        /// real, imag and conjugate are published in the type dict now (Objects/complexobject.c:398-410),
        /// so they reach here through the normal descriptor path. Only __abs__ is still answered here;
        /// CPython exposes it as a slot wrapper, and moving it belongs to the type-surface item P0-28.
        /// </summary>
        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
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
                PyIntSubclass otherInt => PyEquals(otherInt.GetIntValue()),
                PyFloatSubclass otherFloat => PyEquals(new PyFloat(otherFloat.Value)),
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

        private PyComplex PowerIntegral(int n)
        {
            if (n == 0)
                return new PyComplex(1.0, 0.0);
            bool negative = n < 0;
            long e = negative ? -(long)n : n;
            double xr = 1.0, xi = 0.0;
            double br = Real, bi = Imag;
            while (e > 0)
            {
                if ((e & 1L) != 0)
                {
                    (xr, xi) = ComplexMul(xr, xi, br, bi);
                }
                e >>= 1;
                if (e > 0)
                {
                    (br, bi) = ComplexMul(br, bi, br, bi);
                }
            }
            if (negative)
            {
                double d = xr * xr + xi * xi;
                if (d == 0.0)
                    throw PyZeroDivisionError.Create("0.0 to a negative or complex power");
                xr /= d;
                xi = -xi / d;
            }
            return new PyComplex(xr, xi);
        }

        private static (double, double) ComplexMul(double ar, double ai, double br, double bi)
            => (ar * br - ai * bi, ar * bi + ai * br);

        private PyComplex PowerComplex(PyComplex exponent)
        {
            // CPython 3.12: Objects/complexobject.c complex_pow uses c_powi (exact
            // repeated multiplication) for small integral exponents.
            if (exponent.Imag == 0.0 && exponent.Real == Math.Floor(exponent.Real)
                && Math.Abs(exponent.Real) <= 100.0)
            {
                return PowerIntegral((int)exponent.Real);
            }

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

        public override PyObject Positive()
        {
            return new PyComplex(Real, Imag);
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
        public static PyComplex FromString(string text) => FromObject(new PyString(text));

        /// <summary>complex_from_string_inner, after the same text passes as float().</summary>
        internal static PyComplex FromObject(PyObject value)
        {
            var failure = PyValueError.Create("complex() arg is a malformed string");
            string text = PyFloat.WithoutUnderscores(PyFloat.TransformDecimalAndSpace(((PyString)value).Value));
            if (text == null) throw failure;
            int s = 0;
            bool bracket = false;
            double x = 0.0, y = 0.0;
            void Space(ref int at) { while (at < text.Length && PyFloat.IsCSpace(text[at])) at++; }
            char At(int at) => at < text.Length ? text[at] : '\0';
            Space(ref s);
            if (At(s) == '(')
            {
                bracket = true;
                s++;
                Space(ref s);
            }
            if (PyFloat.ScanDouble(text, ref s, out double z))
            {
                if (At(s) == '+' || At(s) == '-')
                {
                    x = z;
                    if (!PyFloat.ScanDouble(text, ref s, out y))
                        y = text[s++] == '+' ? 1.0 : -1.0;
                    if (At(s) != 'j' && At(s) != 'J') throw failure;
                    s++;
                }
                else if (At(s) == 'j' || At(s) == 'J')
                {
                    s++;
                    y = z;
                }
                else
                {
                    x = z;
                }
            }
            else
            {
                y = 1.0;
                if (At(s) == '+' || At(s) == '-')
                    y = text[s++] == '+' ? 1.0 : -1.0;
                if (At(s) != 'j' && At(s) != 'J') throw failure;
                s++;
            }
            Space(ref s);
            if (bracket)
            {
                if (At(s) != ')') throw failure;
                s++;
                Space(ref s);
            }
            if (s != text.Length) throw failure;
            return new PyComplex(x, y);
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

    /// <summary>A Python class deriving from complex: the value of complex, its own type and __dict__.</summary>
    public sealed class PyComplexSubclass : PyComplex, IDictBackedInstance, IHeapInstance
    {
        private PyClass _class;

        public void ChangeClass(PyClass target) => _class = target;

        public PyComplexSubclass(PyClass cls, double real, double imag) : base(real, imag) => _class = cls;

        public Dictionary<string, PyObject> InstanceDict { get; } = new();

        public override PyType GetPyType() => _class;

        public override string GetTypeName() => _class.Name;

        public override PyObject GetAttribute(string name) =>
            SubclassInstanceSupport.GetAttribute(this, _class, InstanceDict, name, n => base.GetAttribute(n));

        public override void SetAttribute(string name, PyObject value) =>
            SubclassInstanceSupport.SetAttribute(this, _class, InstanceDict, name, value);

        public override PyString ToRepr() => SubclassInstanceSupport.Repr(this, () => base.ToRepr());
    }

}