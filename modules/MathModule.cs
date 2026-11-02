using System;
using System.Numerics;

namespace SharpPy.Modules
{
    /// <summary>
    /// Python math 모듈 구현 - 수학 함수들과 상수들
    /// </summary>
    public static class MathModule
    {
        public static PyModule CreateMathModule()
        {
            var module = new PyModule("math", "C:\\Users\\m11\\Desktop\\work\\sharpPy\\modules\\math.py");

            // 수학 상수들
            module.ModuleDict["pi"] = new PyFloat(Math.PI);
            module.ModuleDict["e"] = new PyFloat(Math.E);
            module.ModuleDict["tau"] = new PyFloat(2 * Math.PI);
            module.ModuleDict["inf"] = new PyFloat(double.PositiveInfinity);
            module.ModuleDict["nan"] = new PyFloat(PyFloat.QuietNaN);

            // 기본 수학 함수들
            module.ModuleDict["sqrt"] = new PyMathFunction("sqrt");
            module.ModuleDict["pow"] = new PyMathFunction("pow");
            module.ModuleDict["exp"] = new PyMathFunction("exp");
            module.ModuleDict["log"] = new PyMathFunction("log");
            module.ModuleDict["log10"] = new PyMathFunction("log10");
            module.ModuleDict["log2"] = new PyMathFunction("log2");

            // 삼각 함수들
            module.ModuleDict["sin"] = new PyMathFunction("sin");
            module.ModuleDict["cos"] = new PyMathFunction("cos");
            module.ModuleDict["tan"] = new PyMathFunction("tan");
            module.ModuleDict["asin"] = new PyMathFunction("asin");
            module.ModuleDict["acos"] = new PyMathFunction("acos");
            module.ModuleDict["atan"] = new PyMathFunction("atan");
            module.ModuleDict["atan2"] = new PyMathFunction("atan2");

            // 쌍곡선 함수들
            module.ModuleDict["sinh"] = new PyMathFunction("sinh");
            module.ModuleDict["cosh"] = new PyMathFunction("cosh");
            module.ModuleDict["tanh"] = new PyMathFunction("tanh");
            module.ModuleDict["asinh"] = new PyMathFunction("asinh");
            module.ModuleDict["acosh"] = new PyMathFunction("acosh");
            module.ModuleDict["atanh"] = new PyMathFunction("atanh");

            // 각도 변환
            module.ModuleDict["degrees"] = new PyMathFunction("degrees");
            module.ModuleDict["radians"] = new PyMathFunction("radians");

            // 기타 함수들
            // measured: hasattr(math, "abs") is False in CPython - the C module has fabs, not abs.
            // SharpPy exported an fabs-like abs here, which made hasattr(math, "abs") True.
            module.ModuleDict["ceil"] = new PyMathFunction("ceil");
            module.ModuleDict["floor"] = new PyMathFunction("floor");
            module.ModuleDict["trunc"] = new PyMathFunction("trunc");
            module.ModuleDict["round"] = new PyMathFunction("round");
            module.ModuleDict["copysign"] = new PyMathFunction("copysign");
            module.ModuleDict["fabs"] = new PyMathFunction("fabs");
            module.ModuleDict["factorial"] = new PyMathFunction("factorial");
            module.ModuleDict["lgamma"] = new PyMathFunction("lgamma");
            module.ModuleDict["gcd"] = new PyMathFunction("gcd");
            module.ModuleDict["lcm"] = new PyMathFunction("lcm");

            // 부동소수점 관련
            module.ModuleDict["isfinite"] = new PyMathFunction("isfinite");
            module.ModuleDict["isinf"] = new PyMathFunction("isinf");
            module.ModuleDict["isnan"] = new PyMathFunction("isnan");
            module.ModuleDict["isclose"] = new PyMathFunction("isclose");
            module.ModuleDict["modf"] = new PyMathFunction("modf");
            module.ModuleDict["frexp"] = new PyMathFunction("frexp");
            module.ModuleDict["ldexp"] = new PyMathFunction("ldexp");

            // Python 3.8+ 추가 함수들
            module.ModuleDict["isqrt"] = new PyMathFunction("isqrt");

            // Python 3.9+ / 3.11+ / 3.12+ 추가 함수들
            module.ModuleDict["cbrt"] = new PyMathFunction("cbrt");
            module.ModuleDict["comb"] = new PyMathFunction("comb");
            module.ModuleDict["erf"] = new PyMathFunction("erf");
            module.ModuleDict["erfc"] = new PyMathFunction("erfc");
            module.ModuleDict["exp2"] = new PyMathFunction("exp2");
            module.ModuleDict["expm1"] = new PyMathFunction("expm1");
            module.ModuleDict["fmod"] = new PyMathFunction("fmod");
            module.ModuleDict["fsum"] = new PyMathFunction("fsum");
            module.ModuleDict["gamma"] = new PyMathFunction("gamma");
            module.ModuleDict["log1p"] = new PyMathFunction("log1p");
            module.ModuleDict["nextafter"] = new PyMathFunction("nextafter");
            module.ModuleDict["perm"] = new PyMathFunction("perm");
            module.ModuleDict["prod"] = new PyMathFunction("prod");
            module.ModuleDict["remainder"] = new PyMathFunction("remainder");
            module.ModuleDict["sumprod"] = new PyMathFunction("sumprod");
            module.ModuleDict["ulp"] = new PyMathFunction("ulp");
            module.ModuleDict["dist"] = new PyMathFunction("dist");
            module.ModuleDict["hypot"] = new PyMathFunction("hypot");

            return module;
        }
    }

    /// <summary>
    /// math 모듈의 함수 구현
    /// </summary>
    public class PyMathFunction : PyObject
    {
        public string Name { get; }

        public PyMathFunction(string name)
        {
            Name = name;
        }

        public override string GetTypeName() => "builtin_function_or_method";
        public override bool IsCallable() => true;

        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            if (Name == "isclose") return CallIsClose(args, kwargs);
            // Keyword-only parameters reach the implementation as trailing arguments.
            if (Name == "prod" && KwValue(kwargs, "start") is PyObject start)
                return CallProd(new[] { args[0], start });
            if (Name == "nextafter" && KwValue(kwargs, "steps") is PyObject steps)
            {
                var step = args[0];
                for (var left = steps.ToInt(); left > 0; left--) step = CallNextafter(new[] { step, args[1] });
                return step;
            }
            return Name switch
            {
                // 기본 수학 함수들
                "sqrt" => CallSqrt(args),
                "pow" => CallPow(args),
                "exp" => CallExp(args),
                "log" => CallLog(args),
                "log10" => CallLog10(args),
                "log2" => CallLog2(args),

                // 삼각 함수들
                "sin" => CallSin(args),
                "cos" => CallCos(args),
                "tan" => CallTan(args),
                "asin" => CallAsin(args),
                "acos" => CallAcos(args),
                "atan" => CallAtan(args),
                "atan2" => CallAtan2(args),

                // 쌍곡선 함수들
                "sinh" => CallSinh(args),
                "cosh" => CallCosh(args),
                "tanh" => CallTanh(args),
                "asinh" => CallAsinh(args),
                "acosh" => CallAcosh(args),
                "atanh" => CallAtanh(args),

                // 각도 변환
                "degrees" => CallDegrees(args),
                "radians" => CallRadians(args),

                // 기타 함수들
                "ceil" => CallCeil(args),
                "floor" => CallFloor(args),
                "trunc" => CallTrunc(args),
                "round" => CallRound(args),
                "copysign" => CallCopysign(args),
                "fabs" => CallFabs(args),
                "factorial" => CallFactorial(args),
                "lgamma" => CallLGamma(args),
                "gcd" => CallGcd(args),
                "lcm" => CallLcm(args),

                // 부동소수점 관련
                "isfinite" => CallIsFinite(args),
                "isinf" => CallIsInf(args),
                "isnan" => CallIsNaN(args),
                "modf" => CallModf(args),
                "frexp" => CallFrexp(args),
                "ldexp" => CallLdexp(args),

                // Python 3.9+ / 3.11+ / 3.12+
                "cbrt" => CallCbrt(args),
                "comb" => CallComb(args),
                "erf" => CallErf(args),
                "erfc" => CallErfc(args),
                "exp2" => CallExp2(args),
                "expm1" => CallExpm1(args),
                "fmod" => CallFmod(args),
                "fsum" => CallFsum(args),
                "gamma" => CallGamma(args),
                "log1p" => CallLog1p(args),
                "nextafter" => CallNextafter(args),
                "perm" => CallPerm(args),
                "prod" => CallProd(args),
                "remainder" => CallRemainder(args),
                "sumprod" => CallSumprod(args),
                "ulp" => CallUlp(args),

                // 추가 함수들
                "isqrt" => CallIsqrt(args),
                "dist" => CallDist(args),
                "hypot" => CallHypot(args),

                _ => throw PyNotImplementedError.Create($"math.{Name} not implemented")
            };
        }

        // === 기본 수학 함수 구현 ===

        private PyObject CallSqrt(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"sqrt() takes exactly one argument ({args.Length} given)");

            var x = GetFloatValue(args[0]);
            if (x < 0)
                throw PyValueError.Create("math domain error");

            return new PyFloat(Math.Sqrt(x));
        }

        private PyObject CallPow(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"pow() takes exactly two arguments ({args.Length} given)");

            var x = GetFloatValue(args[0]);
            var y = GetFloatValue(args[1]);

            return new PyFloat(Math.Pow(x, y));
        }

        private PyObject CallExp(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"exp() takes exactly one argument ({args.Length} given)");

            var x = GetFloatValue(args[0]);
            return new PyFloat(Math.Exp(x));
        }

        private PyObject CallLog(PyObject[] args)
        {
            if (args.Length < 1 || args.Length > 2)
                throw PyTypeError.Create($"log() takes from 1 to 2 positional arguments ({args.Length} given)");

            var x = GetFloatValue(args[0]);
            if (x <= 0)
                throw PyValueError.Create("math domain error");

            if (args.Length == 1)
                return new PyFloat(Math.Log(x));
            else
            {
                var base_ = GetFloatValue(args[1]);
                if (base_ <= 0 || base_ == 1)
                    throw PyValueError.Create("math domain error");
                return new PyFloat(Math.Log(x, base_));
            }
        }

        private PyObject CallLog10(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"log10() takes exactly one argument ({args.Length} given)");

            var x = GetFloatValue(args[0]);
            if (x <= 0)
                throw PyValueError.Create("math domain error");

            return new PyFloat(Math.Log10(x));
        }

        private PyObject CallLog2(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"log2() takes exactly one argument ({args.Length} given)");

            var x = GetFloatValue(args[0]);
            if (x <= 0)
                throw PyValueError.Create("math domain error");

            return new PyFloat(Math.Log2(x));
        }

        // === 삼각 함수 구현 ===

        private PyObject CallSin(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"sin() takes exactly one argument ({args.Length} given)");

            var x = GetFloatValue(args[0]);
            return new PyFloat(Math.Sin(x));
        }

        private PyObject CallCos(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"cos() takes exactly one argument ({args.Length} given)");

            var x = GetFloatValue(args[0]);
            return new PyFloat(Math.Cos(x));
        }

        private PyObject CallTan(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"tan() takes exactly one argument ({args.Length} given)");

            var x = GetFloatValue(args[0]);
            return new PyFloat(Math.Tan(x));
        }

        private PyObject CallAsin(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"asin() takes exactly one argument ({args.Length} given)");

            var x = GetFloatValue(args[0]);
            if (x < -1 || x > 1)
                throw PyValueError.Create("math domain error");

            return new PyFloat(Math.Asin(x));
        }

        private PyObject CallAcos(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"acos() takes exactly one argument ({args.Length} given)");

            var x = GetFloatValue(args[0]);
            if (x < -1 || x > 1)
                throw PyValueError.Create("math domain error");

            return new PyFloat(Math.Acos(x));
        }

        private PyObject CallAtan(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"atan() takes exactly one argument ({args.Length} given)");

            var x = GetFloatValue(args[0]);
            return new PyFloat(Math.Atan(x));
        }

        private PyObject CallAtan2(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"atan2() takes exactly two arguments ({args.Length} given)");

            var y = GetFloatValue(args[0]);
            var x = GetFloatValue(args[1]);
            return new PyFloat(Math.Atan2(y, x));
        }

        // === 쌍곡선 함수 구현 ===

        private PyObject CallSinh(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"sinh() takes exactly one argument ({args.Length} given)");

            var x = GetFloatValue(args[0]);
            return new PyFloat(Math.Sinh(x));
        }

        private PyObject CallCosh(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"cosh() takes exactly one argument ({args.Length} given)");

            var x = GetFloatValue(args[0]);
            return new PyFloat(Math.Cosh(x));
        }

        private PyObject CallTanh(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"tanh() takes exactly one argument ({args.Length} given)");

            var x = GetFloatValue(args[0]);
            return new PyFloat(Math.Tanh(x));
        }

        private PyObject CallAsinh(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"asinh() takes exactly one argument ({args.Length} given)");

            var x = GetFloatValue(args[0]);
            return new PyFloat(Math.Asinh(x));
        }

        private PyObject CallAcosh(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"acosh() takes exactly one argument ({args.Length} given)");

            var x = GetFloatValue(args[0]);
            if (x < 1)
                throw PyValueError.Create("math domain error");

            return new PyFloat(Math.Acosh(x));
        }

        private PyObject CallAtanh(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"atanh() takes exactly one argument ({args.Length} given)");

            var x = GetFloatValue(args[0]);
            if (x <= -1 || x >= 1)
                throw PyValueError.Create("math domain error");

            return new PyFloat(Math.Atanh(x));
        }

        // === 각도 변환 ===

        private PyObject CallDegrees(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"degrees() takes exactly one argument ({args.Length} given)");

            var x = GetFloatValue(args[0]);
            return new PyFloat(x * 180.0 / Math.PI);
        }

        private PyObject CallRadians(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"radians() takes exactly one argument ({args.Length} given)");

            var x = GetFloatValue(args[0]);
            return new PyFloat(x * Math.PI / 180.0);
        }

        // === 기타 함수들 ===

        // CPython 3.12: Modules/mathmodule.c:math_ceil (lines 2299-2310)
        // Delegates to __ceil__ special method
        /// <summary>
        /// CPython m_floor / m_ceil / m_trunc are thin wrappers over PyNumber_Floor and friends,
        /// i.e. the operand type's __floor__ / __ceil__ / __trunc__ slot. int and float publish
        /// those slots and the delegates read the payload, so a plain subclass answers with its
        /// value, while a user __floor__ in the subclass wins because the MRO reaches it first
        /// (measured: math.floor(FO) is 'user-floor', math.floor(PF) is 0, math.floor(PI) is 0).
        /// The previous code tested exact CLR PyFloat/PyInt here, which is why every subclass got
        /// "must be real number".
        /// </summary>
        private static PyObject SlotNumeric(PyObject value, string slotName)
        {
            var method = value.LookupSpecialMethodUncached(slotName);
            if (method != null)
                return value.BindSpecialMethod(method).Call(System.Array.Empty<PyObject>(), null);
            throw PyTypeError.Create($"must be real number, not {value.GetTypeName()}");
        }

        private PyObject CallCeil(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create("ceil() takes exactly one argument ({args.Length} given)");

            return SlotNumeric(args[0], "__ceil__");
        }

        // CPython 3.12: Modules/mathmodule.c:math_floor (lines 2282-2293)
        // Delegates to __floor__ special method
        private PyObject CallFloor(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create("floor() takes exactly one argument ({args.Length} given)");

            return SlotNumeric(args[0], "__floor__");
        }

        // CPython 3.12: Modules/mathmodule.c:math_trunc (lines 2265-2276)
        // Delegates to __trunc__ special method
        private PyObject CallTrunc(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create("trunc() takes exactly one argument ({args.Length} given)");

            return SlotNumeric(args[0], "__trunc__");
        }

        private PyObject CallRound(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"round() takes exactly one argument ({args.Length} given)");

            var x = GetFloatValue(args[0]);
            return new PyFloat(Math.Round(x));
        }

        private PyObject CallCopysign(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"copysign() takes exactly two arguments ({args.Length} given)");

            var mag = GetFloatValue(args[0]);
            var sign = GetFloatValue(args[1]);
            return new PyFloat(Math.CopySign(mag, sign));
        }

        private PyObject CallFabs(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"fabs() takes exactly one argument ({args.Length} given)");

            var x = GetFloatValue(args[0]);
            return new PyFloat(Math.Abs(x));
        }

        private PyObject CallFactorial(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"factorial() takes exactly one argument ({args.Length} given)");

            if (args[0] is PyInt n)
            {
                if (n.Value < 0)
                    throw PyValueError.Create("factorial() not defined for negative values");

                long result = 1;
                for (int i = 2; i <= n.Value; i++)
                    result *= i;

                return new PyInt((int)result);
            }
            else
            {
                throw PyTypeError.Create("'int' object cannot be interpreted as an integer");
            }
        }

        // Modules/mathmodule.c m_tgamma, lanczos_sum and gamma_integral, line for line.
        private const double LanczosG = 6.024680040776729583740234375;
        private const double LanczosGMinusHalf = 5.524680040776729583740234375;
        private static readonly double[] LanczosNum = {
            23531376880.410759688572007674451636754734846804940,
            42919803642.649098768957899047001988850926355848959,
            35711959237.355668049440185451547166705960488635843,
            17921034426.037209699919755754458931112671403265390,
            6039542586.3520280050642916443072979210699388420708,
            1439720407.3117216736632230727949123939715485786772,
            248874557.86205415651146038641322942321632125127801,
            31426415.585400194380614231628318205362874684987640,
            2876370.6289353724412254090516208496135991145378768,
            186056.26539522349504029498971604569928220784236328,
            8071.6720023658162106380029022722506138218516325024,
            210.82427775157934587250973392071336271166969580291,
            2.5066282746310002701649081771338373386264310793408 };
        private static readonly double[] LanczosDen = {
            0.0, 39916800.0, 120543840.0, 150917976.0, 105258076.0, 45995730.0,
            13339535.0, 2637558.0, 357423.0, 32670.0, 1925.0, 66.0, 1.0 };
        private static readonly double[] GammaIntegral = {
            1.0, 1.0, 2.0, 6.0, 24.0, 120.0, 720.0, 5040.0, 40320.0, 362880.0,
            3628800.0, 39916800.0, 479001600.0, 6227020800.0, 87178291200.0,
            1307674368000.0, 20922789888000.0, 355687428096000.0,
            6402373705728000.0, 121645100408832000.0, 2432902008176640000.0,
            51090942171709440000.0, 1124000727777607680000.0 };

        private static double LanczosSum(double x)
        {
            double num = 0.0, den = 0.0;
            if (x < 5.0)
            {
                for (int i = LanczosNum.Length - 1; i >= 0; i--)
                {
                    num = num * x + LanczosNum[i];
                    den = den * x + LanczosDen[i];
                }
            }
            else
            {
                for (int i = 0; i < LanczosNum.Length; i++)
                {
                    num = num / x + LanczosNum[i];
                    den = den / x + LanczosDen[i];
                }
            }
            return num / den;
        }

        // m_sinpi: sin(pi*x), giving accurate zeros.
        private static double SinPi(double x)
        {
            double y = Math.IEEERemainder(Math.Abs(x), 2.0);
            if (y < 0) y += 2.0;
            int n = (int)Math.Round(2.0 * y, MidpointRounding.ToEven);
            double r = n switch
            {
                0 => Math.Sin(Math.PI * y),
                1 => Math.Cos(Math.PI * (y - 0.5)),
                2 => Math.Sin(Math.PI * (1.0 - y)),
                3 => -Math.Cos(Math.PI * (y - 1.5)),
                _ => Math.Sin(Math.PI * (y - 2.0)),
            };
            return Math.CopySign(1.0, x) * r;
        }

        private PyObject CallGamma(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"math.gamma() takes exactly one argument ({args.Length} given)");
            double x = args[0].ToFloat();
            if (double.IsNaN(x) || double.IsPositiveInfinity(x)) return new PyFloat(x);
            if (double.IsNegativeInfinity(x) || x == 0.0)
                throw PyValueError.Create("math domain error");
            if (x == Math.Floor(x))
            {
                if (x < 0.0) throw PyValueError.Create("math domain error");
                if (x <= GammaIntegral.Length) return new PyFloat(GammaIntegral[(int)x - 1]);
            }
            double absx = Math.Abs(x);
            if (absx < 1e-20) return new PyFloat(1.0 / x);
            if (absx > 200.0)
            {
                if (x < 0.0) return new PyFloat(0.0 / SinPi(x));
                throw PyOverflowError.Create("math range error");
            }
            double y = absx + LanczosGMinusHalf;
            double z;
            if (absx > LanczosGMinusHalf)
            {
                double q = y - absx;
                z = q - LanczosGMinusHalf;
            }
            else
            {
                double q = y - LanczosGMinusHalf;
                z = q - absx;
            }
            z = z * LanczosG / y;
            double r;
            if (x < 0.0)
            {
                r = -Math.PI / SinPi(absx) / absx * Math.Exp(y) / LanczosSum(absx);
                r -= z * r;
                if (absx < 140.0) r /= Math.Pow(y, absx - 0.5);
                else
                {
                    double sqrtpow = Math.Pow(y, absx / 2.0 - 0.25);
                    r /= sqrtpow;
                    r /= sqrtpow;
                }
            }
            else
            {
                r = LanczosSum(absx) / Math.Exp(y);
                r += z * r;
                if (absx < 140.0) r *= Math.Pow(y, absx - 0.5);
                else
                {
                    double sqrtpow = Math.Pow(y, absx / 2.0 - 0.25);
                    r *= sqrtpow;
                    r *= sqrtpow;
                }
            }
            if (double.IsInfinity(r)) throw PyOverflowError.Create("math range error");
            return new PyFloat(r);
        }

        // math_fsum: Shewchuk partials, with the half-even correction at the end.
        private PyObject CallFsum(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"math.fsum() takes exactly one argument ({args.Length} given)");
            var p = new List<double>();
            double specialSum = 0.0, infSum = 0.0;
            var iterator = args[0].GetIterator();
            while (true)
            {
                PyObject item;
                try { item = iterator.Next(); }
                catch (PythonException ex) when (ex.PyException is PyStopIteration) { break; }
                double x = item.ToFloat();
                double xsave = x;
                int i = 0;
                for (int j = 0; j < p.Count; j++)
                {
                    double y = p[j];
                    if (Math.Abs(x) < Math.Abs(y)) (x, y) = (y, x);
                    double hi = x + y;
                    double yr = hi - x;
                    double lo = y - yr;
                    if (lo != 0.0) p[i++] = lo;
                    x = hi;
                }
                p.RemoveRange(i, p.Count - i);
                if (x != 0.0)
                {
                    if (!double.IsFinite(x))
                    {
                        if (double.IsFinite(xsave))
                            throw PyOverflowError.Create("intermediate overflow in fsum");
                        if (double.IsInfinity(xsave)) infSum += xsave;
                        specialSum += xsave;
                        p.Clear();
                    }
                    else
                    {
                        p.Add(x);
                    }
                }
            }
            if (specialSum != 0.0)
            {
                if (double.IsNaN(infSum))
                    throw PyValueError.Create("-inf + inf in fsum");
                return new PyFloat(specialSum);
            }
            double result = 0.0;
            int n = p.Count;
            if (n > 0)
            {
                result = p[--n];
                double lo = 0.0;
                while (n > 0)
                {
                    double x = result;
                    double y = p[--n];
                    result = x + y;
                    double yr = result - x;
                    lo = y - yr;
                    if (lo != 0.0) break;
                }
                if (n > 0 && ((lo < 0.0 && p[n - 1] < 0.0) || (lo > 0.0 && p[n - 1] > 0.0)))
                {
                    double y = lo * 2.0;
                    double x = result + y;
                    double yr = x - result;
                    if (y == yr) result = x;
                }
            }
            return new PyFloat(result);
        }

        // m_erf_series and m_erfc_contfrac, the portable forms mathmodule.c carried before libm.
        private const double SqrtPi = 1.772453850905516027298167483341145182798;

        private static double ErfSeries(double x)
        {
            double x2 = x * x, acc = 0.0, fk = 25 + 0.5;
            for (int i = 0; i < 25; i++)
            {
                acc = 2.0 + x2 * acc / fk;
                fk -= 1.0;
            }
            return acc * x * Math.Exp(-x2) / SqrtPi;
        }

        private static double ErfcContinuedFraction(double x)
        {
            if (x >= 30.0) return 0.0;
            double x2 = x * x, a = 0.0, da = 0.5, p = 1.0, pLast = 0.0;
            double q = da + x2, qLast = 1.0;
            for (int i = 0; i < 50; i++)
            {
                a += da;
                da += 2.0;
                double b = da + x2;
                (p, pLast) = (b * p - a * pLast, p);
                (q, qLast) = (b * q - a * qLast, q);
            }
            return p / q * x * Math.Exp(-x2) / SqrtPi;
        }

        private PyObject CallErf(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"math.erf() takes exactly one argument ({args.Length} given)");
            double x = args[0].ToFloat();
            if (double.IsNaN(x)) return new PyFloat(x);
            double absx = Math.Abs(x);
            if (absx < 1.5) return new PyFloat(ErfSeries(x));
            double cf = ErfcContinuedFraction(absx);
            return new PyFloat(x > 0.0 ? 1.0 - cf : cf - 1.0);
        }

        private PyObject CallErfc(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"math.erfc() takes exactly one argument ({args.Length} given)");
            double x = args[0].ToFloat();
            if (double.IsNaN(x)) return new PyFloat(x);
            double absx = Math.Abs(x);
            if (absx < 1.5) return new PyFloat(1.0 - ErfSeries(x));
            double cf = ErfcContinuedFraction(absx);
            return new PyFloat(x > 0.0 ? cf : 2.0 - cf);
        }

        // --- Python 3.9+ / 3.11+ / 3.12+ math functions ---

        private PyObject CallCbrt(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"cbrt() takes exactly one argument ({args.Length} given)");
            double x = args[0].ToFloat();
            return new PyFloat(Math.Cbrt(x));
        }

        private PyObject CallComb(PyObject[] args)
        {
            // Modules/mathmodule.c m_comb
            if (args.Length < 2 || args.Length > 3)
                throw PyTypeError.Create($"comb() takes at least 2 arguments ({args.Length} given)");

            if (args[0] is not PyInt nObj)
                throw PyTypeError.Create($"'{(args[0] is PyInt ? args[1] : args[0]).GetTypeName()}' object cannot be interpreted as an integer");
            if (args[1] is not PyInt kObj)
                throw PyTypeError.Create($"'{(args[0] is PyInt ? args[1] : args[0]).GetTypeName()}' object cannot be interpreted as an integer");

            BigInteger n = nObj.Value;
            BigInteger k = kObj.Value;

            if (n < 0)
                throw PyValueError.Create("n must be a non-negative integer");
            if (k < 0)
                throw PyValueError.Create("k must be a non-negative integer");

            if (k > n)
                return new PyInt(0);

            // C(n, k) = n! / (k! * (n-k)!)
            // Use the smaller of k and n-k for efficiency
            if (k > n - k)
                k = n - k;

            BigInteger result = BigInteger.One;
            for (BigInteger i = 0; i < k; i++)
            {
                result = result * (n - i) / (i + 1);
            }
            return new PyInt(result);
        }

        private PyObject CallPerm(PyObject[] args)
        {
            // Modules/mathmodule.c m_perm
            if (args.Length < 1 || args.Length > 3)
                throw PyTypeError.Create($"perm() takes at least 1 argument ({args.Length} given)");

            if (args[0] is not PyInt nObj)
                throw PyTypeError.Create($"'{(args[0] is PyInt ? args[1] : args[0]).GetTypeName()}' object cannot be interpreted as an integer");

            BigInteger n = nObj.Value;
            if (n < 0)
                throw PyValueError.Create("n must be a non-negative integer");

            BigInteger k;
            if (args.Length >= 2 && args[1] is not PyNone)
            {
                if (args[1] is not PyInt kObj)
                    throw PyTypeError.Create($"'{(args[0] is PyInt ? args[1] : args[0]).GetTypeName()}' object cannot be interpreted as an integer");
                k = kObj.Value;
                if (k < 0)
                    throw PyValueError.Create("k must be a non-negative integer");
            }
            else
            {
                k = n; // perm(n) = n!
            }

            if (k > n)
                return new PyInt(0);

            BigInteger result = BigInteger.One;
            for (BigInteger i = 0; i < k; i++)
            {
                result *= (n - i);
            }
            return new PyInt(result);
        }

        private PyObject CallExp2(PyObject[] args)
        {
            // Modules/mathmodule.c m_exp2
            if (args.Length != 1)
                throw PyTypeError.Create($"exp2() takes exactly one argument ({args.Length} given)");
            double x = args[0].ToFloat();
            return new PyFloat(Math.Pow(2.0, x));
        }

        private PyObject CallExpm1(PyObject[] args)
        {
            // Modules/mathmodule.c m_expm1 - accurate for small x
            if (args.Length != 1)
                throw PyTypeError.Create($"expm1() takes exactly one argument ({args.Length} given)");
            double x = args[0].ToFloat();
            if (double.IsNaN(x)) return new PyFloat(double.NaN);
            if (x == 0) return new PyFloat(0);
            if (double.IsPositiveInfinity(x)) return new PyFloat(double.PositiveInfinity);
            if (double.IsNegativeInfinity(x)) return new PyFloat(-1);

            // Kahan's identity: exact division by log(u) cancels the rounding of u.
            double u = Math.Exp(x);
            if (u == 1.0) return new PyFloat(x);
            double uMinusOne = u - 1.0;
            if (uMinusOne == -1.0) return new PyFloat(-1.0);
            if (double.IsInfinity(u)) throw PyOverflowError.Create("math range error");
            return new PyFloat(uMinusOne * x / Math.Log(u));
        }

        private PyObject CallLog1p(PyObject[] args)
        {
            // Modules/mathmodule.c m_log1p - accurate for small x
            if (args.Length != 1)
                throw PyTypeError.Create($"log1p() takes exactly one argument ({args.Length} given)");
            double x = args[0].ToFloat();
            if (double.IsNaN(x)) return new PyFloat(double.NaN);
            if (x == 0) return new PyFloat(0);
            if (x == -1) return new PyFloat(double.NegativeInfinity);
            if (x < -1)
                throw PyValueError.Create("math domain error");
            if (double.IsPositiveInfinity(x)) return new PyFloat(double.PositiveInfinity);

            // For small |x|, use series: log(1+x) = x - x^2/2 + x^3/3 - ...
            if (Math.Abs(x) < 1e-4)
            {
                double result = x;
                double term = x;
                for (int n = 2; n <= 30; n++)
                {
                    term *= -x;
                    double contrib = term / n;
                    result += contrib;
                    if (Math.Abs(contrib) < Math.Abs(result) * 1e-17) break;
                }
                return new PyFloat(result);
            }
            return new PyFloat(Math.Log(1.0 + x));
        }

        private PyObject CallFmod(PyObject[] args)
        {
            // Modules/mathmodule.c m_fmod - C fmod semantics
            if (args.Length != 2)
                throw PyTypeError.Create($"fmod() takes exactly two arguments ({args.Length} given)");
            double x = args[0].ToFloat();
            double y = args[1].ToFloat();

            if (double.IsNaN(x) || double.IsNaN(y))
                return new PyFloat(double.NaN);
            if (double.IsInfinity(x))
                throw PyValueError.Create("math domain error");
            if (y == 0)
                throw PyValueError.Create("math domain error");

            // C fmod: result has same sign as x, |result| < |y|
            double result = x - y * Math.Truncate(x / y);
            // Handle -0.0
            if (result == 0 && x < 0) result = -0.0;
            return new PyFloat(result);
        }

        private PyObject CallRemainder(PyObject[] args)
        {
            // Modules/mathmodule.c m_remainder - IEEE 754 remainder
            if (args.Length != 2)
                throw PyTypeError.Create($"remainder() takes exactly two arguments ({args.Length} given)");
            double x = args[0].ToFloat();
            double y = args[1].ToFloat();

            if (double.IsNaN(x) || double.IsNaN(y))
                return new PyFloat(double.NaN);
            if (double.IsInfinity(x))
                throw PyValueError.Create("math domain error");
            if (y == 0)
                throw PyValueError.Create("math domain error");

            return new PyFloat(Math.IEEERemainder(x, y));
        }

        private PyObject CallNextafter(PyObject[] args)
        {
            // Modules/mathmodule.c m_nextafter
            if (args.Length < 2 || args.Length > 3)
                throw PyTypeError.Create($"nextafter() takes at least 2 arguments ({args.Length} given)");

            double x = args[0].ToFloat();
            double y = args[1].ToFloat();

            int steps = 1;
            if (args.Length >= 3 && args[2] is not PyNone)
            {
                if (args[2] is PyInt stepObj)
                    steps = (int)stepObj.Value;
                else
                    throw PyTypeError.Create("steps must be an integer");
            }

            if (double.IsNaN(x) || double.IsNaN(y))
                return new PyFloat(double.NaN);

            for (int i = 0; i < steps; i++)
            {
                if (x == y) break;
                if (x < y)
                    x = Math.BitIncrement(x);
                else
                    x = Math.BitDecrement(x);
            }
            return new PyFloat(x);
        }

        private PyObject CallUlp(PyObject[] args)
        {
            // Modules/mathmodule.c m_ulp
            if (args.Length != 1)
                throw PyTypeError.Create($"ulp() takes exactly one argument ({args.Length} given)");
            double x = args[0].ToFloat();

            if (double.IsNaN(x)) return new PyFloat(double.NaN);
            if (double.IsInfinity(x)) return new PyFloat(double.PositiveInfinity);

            double ax = Math.Abs(x);
            if (ax == 0) return new PyFloat(double.Epsilon);

            return new PyFloat(Math.BitIncrement(ax) - ax);
        }

        private PyObject CallProd(PyObject[] args)
        {
            // Modules/mathmodule.c m_prod
            if (args.Length < 1 || args.Length > 2)
                throw PyTypeError.Create($"prod() takes at least 1 argument ({args.Length} given)");

            PyObject start = args.Length >= 2 ? args[1] : new PyInt(1);
            var iterator = args[0].GetIterator();

            PyObject result = start;
            while (true)
            {
                PyObject item;
                try { item = iterator.Next(); }
                catch (PythonException ex) when (ex.PyException is PyStopIteration) { break; }
                result = PyVM.Instance.ExecuteBinaryOpType(result, item, BinaryOpType.MULTIPLY);
            }
            return result;
        }

        private PyObject CallSumprod(PyObject[] args)
        {
            // Modules/mathmodule.c m_sumprod
            if (args.Length != 2)
                throw PyTypeError.Create($"sumprod() takes exactly two arguments ({args.Length} given)");

            var iter1 = args[0].GetIterator();
            var iter2 = args[1].GetIterator();

            bool allInts = true;
            BigInteger intAccum = BigInteger.Zero;
            double dblAccum = 0;

            while (true)
            {
                PyObject a, b;
                try { a = iter1.Next(); }
                catch (PythonException ex) when (ex.PyException is PyStopIteration)
                {
                    // Check if iter2 also ended
                    try { iter2.Next(); throw PyValueError.Create("Inputs are not the same length"); }
                    catch (PythonException ex2) when (ex2.PyException is PyStopIteration) { break; }
                }

                try { b = iter2.Next(); }
                catch (PythonException ex) when (ex.PyException is PyStopIteration)
                {
                    throw PyValueError.Create("Inputs are not the same length");
                }

                if (allInts && a is PyInt ai && b is PyInt bi)
                {
                    intAccum += ai.Value * bi.Value;
                }
                else
                {
                    if (allInts)
                    {
                        dblAccum = (double)intAccum;
                        allInts = false;
                    }
                    dblAccum += a.ToFloat() * b.ToFloat();
                }
            }

            if (allInts)
                return new PyInt(intAccum);
            return new PyFloat(dblAccum);
        }

        private PyObject CallLGamma(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"lgamma() takes exactly one argument ({args.Length} given)");

            double x = args[0].ToFloat();

            // lgamma(x) = log(abs(gamma(x)))
            // Modules/mathmodule.c m_loggamma uses m_tgamma internally

            if (double.IsNaN(x))
                return new PyFloat(double.NaN);

            if (double.IsPositiveInfinity(x))
                return new PyFloat(double.PositiveInfinity);

            if (double.IsNegativeInfinity(x) || x == 0)
                return new PyFloat(double.PositiveInfinity);

            if (x < 0 && x == Math.Floor(x))
                throw PyValueError.Create("lgamma() not defined for negative integers");

            // Use built-in .NET approximation
            // This is a simplified implementation - for production use, consider a more accurate algorithm
            double result = LogGammaApproximation(x);
            return new PyFloat(result);
        }

        // Log-gamma approximation using Stirling's formula
        private double LogGammaApproximation(double x)
        {
            // For x > 0, use Stirling's approximation
            // ln(Gamma(x)) ≈ (x - 0.5) * ln(x) - x + 0.5 * ln(2π) + correction terms

            if (x <= 0)
            {
                // Use reflection formula for negative values
                // Gamma(x) * Gamma(1-x) = pi / sin(pi*x)
                double sinPiX = Math.Sin(Math.PI * x);
                if (Math.Abs(sinPiX) < 1e-10)
                    return double.PositiveInfinity;
                return Math.Log(Math.PI / Math.Abs(sinPiX)) - LogGammaApproximation(1 - x);
            }

            // Lanczos approximation coefficients
            double[] coef = {
                76.18009172947146,
                -86.50532032941677,
                24.01409824083091,
                -1.231739572450155,
                0.001208650973866179,
                -0.000005395239384953
            };

            double temp = x + 5.5;
            temp = (x + 0.5) * Math.Log(temp) - temp;
            double ser = 1.000000000190015;

            for (int i = 0; i < 6; i++)
            {
                ser += coef[i] / (x + i + 1);
            }

            return temp + Math.Log(2.5066282746310005 * ser / x);
        }

        private static BigInteger IntArgument(PyObject value) => value switch
        {
            PyBool truth => truth.Value ? BigInteger.One : BigInteger.Zero,
            PyInt number => number.Value,
            PyIntSubclass subclass => subclass.GetIntValue().Value,
            _ => throw PyTypeError.Create($"'{value.GetTypeName()}' object cannot be interpreted as an integer"),
        };

        private PyObject CallGcd(PyObject[] args)
        {
            var result = BigInteger.Zero;
            foreach (var arg in args)
                result = BigInteger.GreatestCommonDivisor(result, IntArgument(arg));
            return new PyInt(result);
        }

        private PyObject CallLcm(PyObject[] args)
        {
            var result = BigInteger.One;
            foreach (var arg in args)
            {
                var value = BigInteger.Abs(IntArgument(arg));
                if (value.IsZero) return new PyInt(0);
                result = result / BigInteger.GreatestCommonDivisor(result, value) * value;
            }
            return new PyInt(result);
        }

        // === 부동소수점 관련 ===

        private PyObject CallIsFinite(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"isfinite() takes exactly one argument ({args.Length} given)");

            var x = GetFloatValue(args[0]);
            return PyBool.FromBool(double.IsFinite(x));
        }

        private PyObject CallIsInf(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"isinf() takes exactly one argument ({args.Length} given)");

            var x = GetFloatValue(args[0]);
            return PyBool.FromBool(double.IsInfinity(x));
        }

        private PyObject CallIsNaN(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"isnan() takes exactly one argument ({args.Length} given)");

            var x = GetFloatValue(args[0]);
            return PyBool.FromBool(double.IsNaN(x));
        }

        private PyObject CallModf(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"modf() takes exactly one argument ({args.Length} given)");

            var x = GetFloatValue(args[0]);
            var fractional = x - Math.Truncate(x);
            var integral = Math.Truncate(x);
            return new PyTuple(new PyFloat(fractional), new PyFloat(integral));
        }

        private PyObject CallFrexp(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"frexp() takes exactly one argument ({args.Length} given)");

            var x = GetFloatValue(args[0]);
            
            if (x == 0)
                return new PyTuple(new PyFloat(0.0), new PyInt(0));

            var exponent = (int)Math.Floor(Math.Log2(Math.Abs(x))) + 1;
            var mantissa = x / Math.Pow(2, exponent);

            return new PyTuple(new PyFloat(mantissa), new PyInt(exponent));
        }

        private PyObject CallLdexp(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"ldexp() takes exactly two arguments ({args.Length} given)");

            // CPython 3.12: Modules/mathmodule.c:1466-1485 - math_ldexp
            var x = GetFloatValue(args[0]);
            if (args[1] is PyInt exp)
            {
                return new PyFloat(x * Math.Pow(2, (double)exp.Value));
            }
            else
            {
                throw PyTypeError.Create("'int' object cannot be interpreted as an integer");
            }
        }

        // === 추가 함수들 ===

        private PyObject CallIsqrt(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"math.isqrt() takes exactly one argument ({args.Length} given)");
            if (args[0] is not PyInt && args[0] is not PyIntSubclass && args[0] is not PyBool)
                throw PyTypeError.Create($"'{args[0].GetTypeName()}' object cannot be interpreted as an integer");
            var n = args[0] is PyBool truth ? (truth.Value ? 1 : 0) : (args[0] as PyInt ?? ((PyIntSubclass)args[0]).GetIntValue()).Value;
            if (n.Sign < 0)
                throw PyValueError.Create("isqrt() argument must be nonnegative");
            if (n.IsZero) return new PyInt(0);
            // Newton's method from above; the C version's adaptive precision is an optimization of the same result.
            var x = System.Numerics.BigInteger.One << (int)((n.GetBitLength() + 1) / 2);
            while (true)
            {
                var y = (x + n / x) >> 1;
                if (y >= x) return new PyInt(x);
                x = y;
            }
        }

        private PyObject CallDist(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"dist() takes exactly two arguments ({args.Length} given)");

            // 간단한 2D 거리 구현
            if (args[0] is PyTuple p1 && args[1] is PyTuple p2)
            {
                if (p1.Items.Length != 2 || p2.Items.Length != 2)
                    throw PyValueError.Create("dist() requires 2D points");

                var x1 = GetFloatValue(p1.Items[0]);
                var y1 = GetFloatValue(p1.Items[1]);
                var x2 = GetFloatValue(p2.Items[0]);
                var y2 = GetFloatValue(p2.Items[1]);

                return new PyFloat(Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2)));
            }
            else
            {
                throw PyTypeError.Create("dist() requires tuple arguments");
            }
        }

        private PyObject CallHypot(PyObject[] args)
        {
            if (args.Length < 2)
                throw PyTypeError.Create($"hypot() takes at least two arguments ({args.Length} given)");

            double sumOfSquares = 0;
            foreach (var arg in args)
            {
                var val = GetFloatValue(arg);
                sumOfSquares += val * val;
            }

            return new PyFloat(Math.Sqrt(sumOfSquares));
        }

        // === 헬퍼 메서드들 ===

        private static PyBool CallIsClose(PyObject[] args, PyDict kwargs)
        {
            int total = args.Length + (kwargs?.Length() ?? 0);
            if (total > 4) throw PyTypeError.Create($"isclose() takes at most 4 arguments ({total} given)");
            if (args.Length > 2) throw PyTypeError.Create($"isclose() takes exactly 2 positional arguments ({args.Length} given)");
            PyObject a = args.Length > 0 ? args[0] : null, b = args.Length > 1 ? args[1] : null;
            PyObject relative = null, absolute = null;
            if (kwargs != null)
            {
                if (kwargs.TryGetStoredValue(StringCache.GetOrCreate("a"), out var namedA))
                {
                    if (a != null) throw PyTypeError.Create("argument for isclose() given by name ('a') and position (1)");
                    a = namedA;
                }
                if (kwargs.TryGetStoredValue(StringCache.GetOrCreate("b"), out var namedB))
                {
                    if (b != null) throw PyTypeError.Create("argument for isclose() given by name ('b') and position (2)");
                    b = namedB;
                }
            }
            if (a == null) throw PyTypeError.Create("isclose() missing required argument 'a' (pos 1)");
            if (b == null) throw PyTypeError.Create("isclose() missing required argument 'b' (pos 2)");
            if (kwargs != null)
            {
                foreach (var pair in kwargs.GetInternalDict())
                {
                    string name = pair.Key.ToStr().Value;
                    switch (name)
                    {
                        case "a": case "b": break;
                        case "rel_tol": relative = pair.Value; break;
                        case "abs_tol": absolute = pair.Value; break;
                        default: throw PyTypeError.Create($"'{name}' is an invalid keyword argument for isclose()");
                    }
                }
            }
            double left = RealNumber(a), right = RealNumber(b);
            double rel = relative == null ? 1e-9 : RealNumber(relative);
            double abs = absolute == null ? 0 : RealNumber(absolute);
            // CPython 3.12 Modules/mathmodule.c: math_isclose_impl.
            if (rel < 0 || abs < 0) throw PyValueError.Create("tolerances must be non-negative");
            if (left == right) return PyBool.True;
            if (double.IsInfinity(left) || double.IsInfinity(right)) return PyBool.False;
            double difference = Math.Abs(right - left);
            return PyBool.FromBool(difference <= Math.Abs(rel * right) ||
                difference <= Math.Abs(rel * left) || difference <= abs);
        }

        private static double RealNumber(PyObject value)
        {
            if (value is PyFloat real) return real.Value;
            if (value is PyBool boolean) return boolean.Value ? 1 : 0;
            if (value is PyInt integer && !value.HasCustomType) return IntegerDouble(integer);
            var method = value.LookupSpecialMethodUncached("__float__");
            if (method != null)
            {
                var result = value.BindSpecialMethod(method).Call(Array.Empty<PyObject>(), null);
                if (result is PyFloat converted) return converted.Value;
                throw PyTypeError.Create($"{value.GetTypeName()}.__float__ returned non-float (type {result.GetTypeName()})");
            }
            var index = PyObject.TryGetIndex(value);
            if (index != null) return IntegerDouble(index);
            throw PyTypeError.Create($"must be real number, not {value.GetTypeName()}");
        }

        private static double IntegerDouble(PyInt value)
        {
            double result = (double)value.Value;
            if (double.IsInfinity(result)) throw PyOverflowError.Create("int too large to convert to float");
            return result;
        }

        private double GetFloatValue(PyObject obj)
        {
            return obj switch
            {
                PyFloat f => f.Value,
                PyInt i => (double)i.Value,
                PyBool b => b.Value ? 1.0 : 0.0,
                _ => throw PyTypeError.Create("a float is required")
            };
        }

        private int GcdHelper(int a, int b)
        {
            while (b != 0)
            {
                int temp = b;
                b = a % b;
                a = temp;
            }
            return a;
        }

        public override string ToString() => $"<built-in function {Name}>";

        public override PyObject GetAttribute(string name)
        {
            return name switch
            {
                "__name__" => new PyString(Name),
                "__module__" => new PyString("math"),
                _ => throw PyAttributeError.Create($"'builtin_function_or_method' object has no attribute '{name}'")
            };
        }
    }
}
