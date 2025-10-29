using System;

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
            module.ModuleDict["nan"] = new PyFloat(double.NaN);

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
            module.ModuleDict["abs"] = new PyMathFunction("abs");
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
            module.ModuleDict["modf"] = new PyMathFunction("modf");
            module.ModuleDict["frexp"] = new PyMathFunction("frexp");
            module.ModuleDict["ldexp"] = new PyMathFunction("ldexp");

            // Python 3.8+ 추가 함수들
            module.ModuleDict["isqrt"] = new PyMathFunction("isqrt");
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
                "abs" => CallAbs(args),
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

        private PyObject CallAbs(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"abs() takes exactly one argument ({args.Length} given)");

            var x = GetFloatValue(args[0]);
            return new PyFloat(Math.Abs(x));
        }

        private PyObject CallCeil(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"ceil() takes exactly one argument ({args.Length} given)");

            var x = GetFloatValue(args[0]);
            return new PyInt((int)Math.Ceiling(x));
        }

        private PyObject CallFloor(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"floor() takes exactly one argument ({args.Length} given)");

            var x = GetFloatValue(args[0]);
            return new PyInt((int)Math.Floor(x));
        }

        private PyObject CallTrunc(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"trunc() takes exactly one argument ({args.Length} given)");

            var x = GetFloatValue(args[0]);
            return new PyInt((int)Math.Truncate(x));
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

        private PyObject CallLGamma(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"lgamma() takes exactly one argument ({args.Length} given)");

            double x = args[0].ToFloat();

            // lgamma(x) = log(abs(gamma(x)))
            // Using Stirling's approximation for large values
            // For small values, use the actual gamma function

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

        private PyObject CallGcd(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"gcd() takes exactly two arguments ({args.Length} given)");

            if (args[0] is PyInt a && args[1] is PyInt b)
            {
                return new PyInt(GcdHelper((int)Math.Abs(a.Value), (int)Math.Abs(b.Value)));
            }
            else
            {
                throw PyTypeError.Create("'int' object cannot be interpreted as an integer");
            }
        }

        private PyObject CallLcm(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"lcm() takes exactly two arguments ({args.Length} given)");

            if (args[0] is PyInt a && args[1] is PyInt b)
            {
                var gcd = GcdHelper((int)Math.Abs(a.Value), (int)Math.Abs(b.Value));
                return new PyInt(Math.Abs(a.Value * b.Value) / gcd);
            }
            else
            {
                throw PyTypeError.Create("'int' object cannot be interpreted as an integer");
            }
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

            var x = GetFloatValue(args[0]);
            if (args[1] is PyInt exp)
            {
                return new PyFloat(x * Math.Pow(2, exp.Value));
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
                throw PyTypeError.Create($"isqrt() takes exactly one argument ({args.Length} given)");

            if (args[0] is PyInt n)
            {
                if (n.Value < 0)
                    throw PyValueError.Create("isqrt() domain error");

                return new PyInt((int)Math.Floor(Math.Sqrt(n.Value)));
            }
            else
            {
                throw PyTypeError.Create("'int' object cannot be interpreted as an integer");
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