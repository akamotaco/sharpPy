using System;

namespace SharpPy.Modules
{
    /// <summary>
    /// Modules/cmathmodule.c. The functions and their special value tables are in CmathModule.g.cs,
    /// generated from the C; this file is what they stand on and how Python calls them.
    /// </summary>
    internal static partial class CmathModule
    {
        private enum Fault { None, Domain, Range }

        private struct Pair
        {
            public double Real, Imag;

            public Pair(double real, double imag)
            {
                Real = real;
                Imag = imag;
            }
        }

        [ThreadStatic] private static Fault _errno;

        private const double DoubleMin = 2.2250738585072014e-308;
        private const double M_LN2 = 0.6931471805599453094;
        private const double M_LN10 = 2.302585092994045684;
        private const double CM_LARGE_DOUBLE = double.MaxValue / 4.0;
        private static readonly double CM_SQRT_LARGE_DOUBLE = Math.Sqrt(CM_LARGE_DOUBLE);
        private static readonly double CM_LOG_LARGE_DOUBLE = Math.Log(CM_LARGE_DOUBLE);
        private static readonly double CM_SQRT_DBL_MIN = Math.Sqrt(DoubleMin);
        private const int CM_SCALE_UP = 2 * (53 / 2) + 1;
        private const int CM_SCALE_DOWN = -(CM_SCALE_UP + 1) / 2;
        private const double INF = double.PositiveInfinity;

        private static double copysign(double x, double y) => Math.CopySign(x, y);
        private static Pair _Py_c_neg(Pair z) => new Pair(-z.Real, -z.Imag);

        // special_type
        private static int SpecialType(double d)
        {
            if (double.IsFinite(d))
            {
                if (d != 0)
                    return Math.CopySign(1.0, d) == 1.0 ? 4 : 1;
                return Math.CopySign(1.0, d) == 1.0 ? 3 : 2;
            }
            if (double.IsNaN(d))
                return 6;
            return Math.CopySign(1.0, d) == 1.0 ? 5 : 0;
        }

        private static double Hypot(double x, double y)
        {
            x = Math.Abs(x);
            y = Math.Abs(y);
            if (double.IsInfinity(x) || double.IsInfinity(y))
                return double.PositiveInfinity;
            if (double.IsNaN(x) || double.IsNaN(y))
                return double.NaN;
            double big = Math.Max(x, y), small = Math.Min(x, y);
            if (small == 0.0)
                return big;
            if (big > 1e150 || small < 1e-150)
            {
                int scale = Math.ILogB(big);
                double a = Math.ScaleB(big, -scale), b = Math.ScaleB(small, -scale);
                return Math.ScaleB(Math.Sqrt(a * a + b * b), scale);
            }
            return Math.Sqrt(big * big + small * small);
        }

        private static double Hypot2(Pair z) => Hypot(z.Real, z.Imag);

        private static double Log1p(double x)
        {
            if (x == 0.0)
                return x;
            double u = 1.0 + x;
            return u == 1.0 ? x : Math.Log(u) * (x / (u - 1.0));
        }

        private static double Atan2(double y, double x)
        {
            if (double.IsNaN(x) || double.IsNaN(y))
                return double.NaN;
            return Math.Atan2(y, x);
        }

        // Py_complex from the argument: PyArg "D".
        private static Pair Argument(PyObject value)
        {
            // PyComplex_AsCComplex: a complex, else try_complex_special_method, else PyFloat_AsDouble.
            if (value is PyComplex complex)
                return new Pair(complex.Real, complex.Imag);
            if (PyComplex.TryComplexSpecialMethod(value) is { } converted)
                return new Pair(converted.Real, converted.Imag);
            return new Pair(PyMathFunction.RealNumber(value), 0.0);
        }

        // math_error
        private static PyObject Result(Pair z)
        {
            var fault = _errno;
            _errno = Fault.None;
            if (fault == Fault.Domain)
                throw PyValueError.Create("math domain error");
            if (fault == Fault.Range)
                throw PyOverflowError.Create("math range error");
            return new PyComplex(z.Real, z.Imag);
        }

        private static void Register(PyModule module, string name, Func<Pair, Pair> function) =>
            module.ModuleDict[name] = new PyBuiltinFunction(name, args =>
            {
                if (args.Length != 1)
                    throw PyTypeError.Create($"cmath.{name}() takes exactly one argument ({args.Length} given)");
                var z = Argument(args[0]);
                _errno = Fault.None;
                return Result(function(z));
            });

        // cmath_log_impl
        private static PyObject LogOf(PyObject[] args)
        {
            if (args.Length is < 1 or > 2)
                throw PyTypeError.Create(args.Length < 1 ? "log expected at least 1 argument, got 0" : $"log expected at most 2 arguments, got {args.Length}");
            var x = Argument(args[0]);
            _errno = Fault.None;
            var numerator = CLog(x);
            if (args.Length == 1)
                return Result(numerator);
            var y = Argument(args[1]);
            var denominator = CLog(y);
            var result = Result(numerator);
            var below = Result(denominator);
            return PyVM.Instance.ExecuteBinaryOpType(result, below, BinaryOpType.TRUE_DIVIDE);
        }

        private static PyObject Rect(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"rect expected 2 arguments, got {args.Length}");
            double r = PyMathFunction.RealNumber(args[0]), phi = PyMathFunction.RealNumber(args[1]);
            _errno = Fault.None;
            var z = default(Pair);
            if (!double.IsFinite(r) || !double.IsFinite(phi))
            {
                if (double.IsInfinity(phi) && !double.IsNaN(r) && r != 0.0)
                    _errno = Fault.Domain;
                if (double.IsInfinity(r) && double.IsFinite(phi) && phi != 0.0)
                {
                    z = r > 0
                        ? new Pair(copysign(INF, Math.Cos(phi)), copysign(INF, Math.Sin(phi)))
                        : new Pair(-copysign(INF, Math.Cos(phi)), -copysign(INF, Math.Sin(phi)));
                }
                else
                    z = rect_special_values[SpecialType(r) * 7 + SpecialType(phi)];
            }
            else if (phi == 0.0)
                z = new Pair(r, r * phi);
            else
                z = new Pair(r * Math.Cos(phi), r * Math.Sin(phi));
            return Result(z);
        }

        private static PyObject IsClose(PyObject[] args, PyDict kwargs)
        {
            PyObject a = args.Length > 0 ? args[0] : null, b = args.Length > 1 ? args[1] : null, relative = null, absolute = null;
            if (kwargs != null)
                foreach (var pair in kwargs.InternalDict)
                    switch ((pair.Key as PyString)?.Value)
                    {
                        case "a": a = pair.Value; break;
                        case "b": b = pair.Value; break;
                        case "rel_tol": relative = pair.Value; break;
                        case "abs_tol": absolute = pair.Value; break;
                        default: throw PyTypeError.Create($"'{pair.Key.ToStr().Value}' is an invalid keyword argument for isclose()");
                    }
            if (a == null || b == null)
                throw PyArgs.MissingArgument("isclose", (a == null ? "a" : "b"), (a == null ? 1 : 2));
            Pair left = Argument(a), right = Argument(b);
            double rel = relative == null ? 1e-9 : PyMathFunction.RealNumber(relative);
            double abs = absolute == null ? 0 : PyMathFunction.RealNumber(absolute);
            if (rel < 0 || abs < 0)
                throw PyValueError.Create("tolerances must be non-negative");
            if (left.Real == right.Real && left.Imag == right.Imag)
                return PyBool.True;
            if (double.IsInfinity(left.Real) || double.IsInfinity(left.Imag) || double.IsInfinity(right.Real) || double.IsInfinity(right.Imag))
                return PyBool.False;
            double difference = Hypot(left.Real - right.Real, left.Imag - right.Imag);
            return PyBool.FromBool(difference <= rel * Hypot2(right) || difference <= rel * Hypot2(left) || difference <= abs);
        }

        internal static PyModule CreateModule()
        {
            var module = new PyModule("cmath");
            var names = module.ModuleDict;
            names["pi"] = new PyFloat(Math.PI);
            names["e"] = new PyFloat(Math.E);
            names["tau"] = new PyFloat(2 * Math.PI);
            names["inf"] = new PyFloat(double.PositiveInfinity);
            names["nan"] = new PyFloat(PyFloat.QuietNaN);
            names["infj"] = new PyComplex(0.0, double.PositiveInfinity);
            names["nanj"] = new PyComplex(0.0, PyFloat.QuietNaN);
            Register(module, "acos", Acos);
            Register(module, "acosh", Acosh);
            Register(module, "asin", Asin);
            Register(module, "asinh", Asinh);
            Register(module, "atan", Atan);
            Register(module, "atanh", Atanh);
            Register(module, "cos", Cos);
            Register(module, "cosh", Cosh);
            Register(module, "exp", Exp);
            Register(module, "log10", Log10);
            Register(module, "sin", Sin);
            Register(module, "sinh", Sinh);
            Register(module, "sqrt", Sqrt);
            Register(module, "tan", Tan);
            Register(module, "tanh", Tanh);
            names["log"] = new PyBuiltinFunction("log", LogOf);
            names["rect"] = new PyBuiltinFunction("rect", Rect);
            names["isclose"] = new PyBuiltinFunction("isclose", IsClose);
            names["phase"] = new PyBuiltinFunction("phase", args =>
            {
                var z = Argument(args[0]);
                double phi = Math.Atan2(z.Imag, z.Real);
                return new PyFloat(phi);
            });
            names["polar"] = new PyBuiltinFunction("polar", args =>
            {
                var z = Argument(args[0]);
                double phi = Math.Atan2(z.Imag, z.Real), r = Hypot2(z);
                if (double.IsInfinity(r) && double.IsFinite(z.Real) && double.IsFinite(z.Imag))
                    throw PyOverflowError.Create("math range error");
                return PyTuple.From(new PyObject[] { new PyFloat(r), new PyFloat(phi) });
            });
            names["isfinite"] = new PyBuiltinFunction("isfinite", args =>
            {
                var z = Argument(args[0]);
                return PyBool.FromBool(double.IsFinite(z.Real) && double.IsFinite(z.Imag));
            });
            names["isnan"] = new PyBuiltinFunction("isnan", args =>
            {
                var z = Argument(args[0]);
                return PyBool.FromBool(double.IsNaN(z.Real) || double.IsNaN(z.Imag));
            });
            names["isinf"] = new PyBuiltinFunction("isinf", args =>
            {
                var z = Argument(args[0]);
                return PyBool.FromBool(double.IsInfinity(z.Real) || double.IsInfinity(z.Imag));
            });
            return module;
        }
    }
}
