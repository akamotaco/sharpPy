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
            if (text.EndsWith(".0", StringComparison.Ordinal))
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
            if (text.EndsWith(".0", StringComparison.Ordinal))
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

        // complex_hash: _PyHASH_IMAG is 1000003.
        internal override long GetPythonHash()
        {
            long combined = unchecked(new PyFloat(Real).PythonHash() + 1000003L * new PyFloat(Imag).PythonHash());
            return combined == -1 ? -2 : combined;
        }

        public override int ToHash() => unchecked((int)GetPythonHash());

        // complex_richcompare
        public override PyObject RichCompare(PyObject other, CompareOp op)
        {
            if (op != CompareOp.EQ && op != CompareOp.NE)
                return PyNotImplemented.Instance;
            bool equal;
            switch (other)
            {
                case PyInt or PyBool or PyIntSubclass:
                    if (Imag == 0.0)
                        return Compare(new PyFloat(Real), other, op);
                    equal = false;
                    break;
                case PyFloat or PyFloatSubclass:
                    ToComplex(other, out double r, out _);
                    equal = Real == r && Imag == 0.0;
                    break;
                case PyComplex complex:
                    equal = Real == complex.Real && Imag == complex.Imag;
                    break;
                default:
                    return PyNotImplemented.Instance;
            }
            return PyBool.FromBool(equal == (op == CompareOp.EQ));
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

        // to_complex: an int or a float, as the C structure; NotImplemented for anything else.
        private static bool ToComplex(PyObject value, out double real, out double imag)
        {
            real = imag = 0.0;
            switch (value)
            {
                case PyComplex complex:
                    real = complex.Real;
                    imag = complex.Imag;
                    return true;
                case PyFloat exact:
                    real = exact.Value;
                    return true;
                case PyFloatSubclass subclass:
                    real = subclass.Value;
                    return true;
                case PyInt or PyBool or PyIntSubclass:
                    real = value.AsFloat().Value;
                    return true;
                default:
                    return false;
            }
        }

        public override PyObject Add(PyObject other) =>
            ToComplex(other, out double r, out double i) ? new PyComplex(Real + r, Imag + i) : PyNotImplemented.Instance;

        public override PyObject Subtract(PyObject other) =>
            ToComplex(other, out double r, out double i) ? new PyComplex(Real - r, Imag - i) : PyNotImplemented.Instance;

        public override PyObject Multiply(PyObject other) =>
            ToComplex(other, out double r, out double i)
                ? new PyComplex(Real * r - Imag * i, Real * i + Imag * r) : PyNotImplemented.Instance;

        // _Py_c_quot, false for EDOM
        private static bool Quotient(double ar, double ai, double br, double bi, out double real, out double imag)
        {
            double absReal = Math.Abs(br), absImag = Math.Abs(bi);
            if (absReal >= absImag)
            {
                if (absReal == 0.0)
                {
                    real = imag = 0.0;
                    return false;
                }
                double ratio = bi / br;
                double denominator = br + bi * ratio;
                real = (ar + ai * ratio) / denominator;
                imag = (ai - ar * ratio) / denominator;
            }
            else if (absImag >= absReal)
            {
                double ratio = br / bi;
                double denominator = br * ratio + bi;
                real = (ar * ratio + ai) / denominator;
                imag = (ai * ratio - ar) / denominator;
            }
            else
            {
                real = imag = PyFloat.QuietNaN;
            }
            return true;
        }

        public override PyObject Divide(PyObject other)
        {
            if (!ToComplex(other, out double r, out double i))
                return PyNotImplemented.Instance;
            if (!Quotient(Real, Imag, r, i, out double real, out double imag))
                throw PyZeroDivisionError.Create("complex division by zero");
            return new PyComplex(real, imag);
        }

        private static (double, double) Product(double ar, double ai, double br, double bi) =>
            (ar * br - ai * bi, ar * bi + ai * br);

        // c_powu
        private (double, double) IntegralPower(long n)
        {
            double rr = 1.0, ri = 0.0, pr = Real, pi = Imag;
            long mask = 1;
            while (mask > 0 && n >= mask)
            {
                if ((n & mask) != 0)
                    (rr, ri) = Product(rr, ri, pr, pi);
                mask <<= 1;
                (pr, pi) = Product(pr, pi, pr, pi);
            }
            return (rr, ri);
        }

        // complex_pow with z None
        public override PyObject Power(PyObject other)
        {
            if (!ToComplex(other, out double br, out double bi))
                return PyNotImplemented.Instance;
            bool domain = false;
            double real, imag;
            if (bi == 0.0 && br == Math.Floor(br) && Math.Abs(br) <= 100.0)
            {
                // c_powi
                long n = (long)br;
                if (n > 0)
                {
                    (real, imag) = IntegralPower(n);
                }
                else
                {
                    var (dr, di) = IntegralPower(-n);
                    domain = !Quotient(1.0, 0.0, dr, di, out real, out imag);
                }
            }
            else if (br == 0.0 && bi == 0.0)
            {
                real = 1.0;
                imag = 0.0;
            }
            else if (Real == 0.0 && Imag == 0.0)
            {
                domain = bi != 0.0 || br < 0.0;
                real = imag = 0.0;
            }
            else
            {
                // _Py_c_pow
                double magnitude = Abs(Real, Imag);
                double length = Math.Pow(magnitude, br);
                double angle = Math.Atan2(Imag, Real);
                double phase = angle * br;
                if (bi != 0.0)
                {
                    length *= Math.Exp(-angle * bi);
                    phase += bi * Math.Log(magnitude);
                }
                real = length * Math.Cos(phase);
                imag = length * Math.Sin(phase);
            }
            if (domain)
                throw PyZeroDivisionError.Create("0.0 to a negative or complex power");
            if (double.IsInfinity(real) || double.IsInfinity(imag))
                throw PyOverflowError.Create("complex exponentiation");
            return new PyComplex(real, imag);
        }

        // hypot
        private static double Abs(double real, double imag) => System.Numerics.Complex.Abs(new System.Numerics.Complex(real, imag));

        public PyObject Negate()
        {
            return new PyComplex(-Real, -Imag);
        }

        public override PyObject Positive()
        {
            return new PyComplex(Real, Imag);
        }

        // complex_abs, _Py_c_abs
        public PyObject Absolute()
        {
            if (double.IsInfinity(Real) || double.IsInfinity(Imag))
                return new PyFloat(double.PositiveInfinity);
            if (double.IsNaN(Real) || double.IsNaN(Imag))
                return new PyFloat(PyFloat.QuietNaN);
            double result = Abs(Real, Imag);
            if (double.IsInfinity(result))
                throw PyOverflowError.Create("absolute value too large");
            return new PyFloat(result);
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
            SubclassInstanceSupport.GetAttribute(this, _class, name);

        internal override PyObject GenericGetAttr(string name) =>
            SubclassInstanceSupport.GenericGetAttribute(this, _class, InstanceDict, name, n => base.GetAttribute(n));

        public override void SetAttribute(string name, PyObject value) =>
            SubclassInstanceSupport.SetAttribute(this, _class, InstanceDict, name, value);

        public override PyObject RichCompare(PyObject other, CompareOp op) =>
            TryCompareSpecial(other, op, PyType.ComplexType) ?? base.RichCompare(other, op);

        public override PyString ToRepr() => SubclassInstanceSupport.Repr(this, () => base.ToRepr());
    }

}