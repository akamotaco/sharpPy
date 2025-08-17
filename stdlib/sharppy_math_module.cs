// sharppy_math_module.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    /// <summary>
    /// Python math module implementation
    /// </summary>
    public static class MathModule
    {
        public static PythonModule CreateMathModule(List<string> searchPaths = null)
        {
            var module = new PythonModule("math", searchPaths);

            // === Mathematical Constants ===
            module.SetAttribute("pi", new PythonFloat(Math.PI));
            module.SetAttribute("e", new PythonFloat(Math.E));
            module.SetAttribute("tau", new PythonFloat(2 * Math.PI));
            module.SetAttribute("inf", new PythonFloat(double.PositiveInfinity));
            module.SetAttribute("nan", new PythonFloat(double.NaN));

            // === Basic Math Functions ===
            
            // math.sqrt(x)
            module.SetAttribute("sqrt", new BuiltinFunction("sqrt", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "sqrt() takes exactly one argument");
                
                double value = NumberHelper.ToDouble(args[0]);
                if (value < 0)
                    throw new PythonException("ValueError", "math domain error");
                
                return new PythonFloat(Math.Sqrt(value));
            }));

            // math.pow(x, y)
            module.SetAttribute("pow", new BuiltinFunction("pow", args =>
            {
                if (args.Count != 2)
                    throw new PythonException("TypeError", "pow() takes exactly 2 arguments");
                
                double x = NumberHelper.ToDouble(args[0]);
                double y = NumberHelper.ToDouble(args[1]);
                
                double result = Math.Pow(x, y);
                if (double.IsNaN(result))
                    throw new PythonException("ValueError", "math domain error");
                
                return new PythonFloat(result);
            }));

            // math.exp(x)
            module.SetAttribute("exp", new BuiltinFunction("exp", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "exp() takes exactly one argument");
                
                double value = NumberHelper.ToDouble(args[0]);
                return new PythonFloat(Math.Exp(value));
            }));

            // math.log(x[, base])
            module.SetAttribute("log", new BuiltinFunction("log", args =>
            {
                if (args.Count < 1 || args.Count > 2)
                    throw new PythonException("TypeError", "log() takes 1 or 2 arguments");
                
                double value = NumberHelper.ToDouble(args[0]);
                if (value <= 0)
                    throw new PythonException("ValueError", "math domain error");
                
                if (args.Count == 2)
                {
                    double baseValue = NumberHelper.ToDouble(args[1]);
                    if (baseValue <= 0 || baseValue == 1)
                        throw new PythonException("ValueError", "math domain error");
                    return new PythonFloat(Math.Log(value) / Math.Log(baseValue));
                }
                
                return new PythonFloat(Math.Log(value));
            }));

            // math.log10(x)
            module.SetAttribute("log10", new BuiltinFunction("log10", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "log10() takes exactly one argument");
                
                double value = NumberHelper.ToDouble(args[0]);
                if (value <= 0)
                    throw new PythonException("ValueError", "math domain error");
                
                return new PythonFloat(Math.Log10(value));
            }));

            // math.log2(x)
            module.SetAttribute("log2", new BuiltinFunction("log2", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "log2() takes exactly one argument");
                
                double value = NumberHelper.ToDouble(args[0]);
                if (value <= 0)
                    throw new PythonException("ValueError", "math domain error");
                
                return new PythonFloat(Math.Log(value) / Math.Log(2));
            }));

            // math.fabs(x)
            module.SetAttribute("fabs", new BuiltinFunction("fabs", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "fabs() takes exactly one argument");
                
                double value = NumberHelper.ToDouble(args[0]);
                return new PythonFloat(Math.Abs(value));
            }));

            // === Trigonometric Functions ===
            
            // math.sin(x)
            module.SetAttribute("sin", new BuiltinFunction("sin", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "sin() takes exactly one argument");
                
                double value = NumberHelper.ToDouble(args[0]);
                return new PythonFloat(Math.Sin(value));
            }));

            // math.cos(x)
            module.SetAttribute("cos", new BuiltinFunction("cos", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "cos() takes exactly one argument");
                
                double value = NumberHelper.ToDouble(args[0]);
                return new PythonFloat(Math.Cos(value));
            }));

            // math.tan(x)
            module.SetAttribute("tan", new BuiltinFunction("tan", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "tan() takes exactly one argument");
                
                double value = NumberHelper.ToDouble(args[0]);
                return new PythonFloat(Math.Tan(value));
            }));

            // math.asin(x)
            module.SetAttribute("asin", new BuiltinFunction("asin", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "asin() takes exactly one argument");
                
                double value = NumberHelper.ToDouble(args[0]);
                if (value < -1 || value > 1)
                    throw new PythonException("ValueError", "math domain error");
                
                return new PythonFloat(Math.Asin(value));
            }));

            // math.acos(x)
            module.SetAttribute("acos", new BuiltinFunction("acos", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "acos() takes exactly one argument");
                
                double value = NumberHelper.ToDouble(args[0]);
                if (value < -1 || value > 1)
                    throw new PythonException("ValueError", "math domain error");
                
                return new PythonFloat(Math.Acos(value));
            }));

            // math.atan(x)
            module.SetAttribute("atan", new BuiltinFunction("atan", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "atan() takes exactly one argument");
                
                double value = NumberHelper.ToDouble(args[0]);
                return new PythonFloat(Math.Atan(value));
            }));

            // math.atan2(y, x)
            module.SetAttribute("atan2", new BuiltinFunction("atan2", args =>
            {
                if (args.Count != 2)
                    throw new PythonException("TypeError", "atan2() takes exactly 2 arguments");
                
                double y = NumberHelper.ToDouble(args[0]);
                double x = NumberHelper.ToDouble(args[1]);
                return new PythonFloat(Math.Atan2(y, x));
            }));

            // math.degrees(x)
            module.SetAttribute("degrees", new BuiltinFunction("degrees", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "degrees() takes exactly one argument");
                
                double radians = NumberHelper.ToDouble(args[0]);
                return new PythonFloat(radians * (180.0 / Math.PI));
            }));

            // math.radians(x)
            module.SetAttribute("radians", new BuiltinFunction("radians", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "radians() takes exactly one argument");
                
                double degrees = NumberHelper.ToDouble(args[0]);
                return new PythonFloat(degrees * (Math.PI / 180.0));
            }));

            // === Hyperbolic Functions ===
            
            // math.sinh(x)
            module.SetAttribute("sinh", new BuiltinFunction("sinh", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "sinh() takes exactly one argument");
                
                double value = NumberHelper.ToDouble(args[0]);
                return new PythonFloat(Math.Sinh(value));
            }));

            // math.cosh(x)
            module.SetAttribute("cosh", new BuiltinFunction("cosh", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "cosh() takes exactly one argument");
                
                double value = NumberHelper.ToDouble(args[0]);
                return new PythonFloat(Math.Cosh(value));
            }));

            // math.tanh(x)
            module.SetAttribute("tanh", new BuiltinFunction("tanh", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "tanh() takes exactly one argument");
                
                double value = NumberHelper.ToDouble(args[0]);
                return new PythonFloat(Math.Tanh(value));
            }));

            // math.asinh(x)
            module.SetAttribute("asinh", new BuiltinFunction("asinh", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "asinh() takes exactly one argument");
                
                double value = NumberHelper.ToDouble(args[0]);
                return new PythonFloat(Math.Log(value + Math.Sqrt(value * value + 1)));
            }));

            // math.acosh(x)
            module.SetAttribute("acosh", new BuiltinFunction("acosh", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "acosh() takes exactly one argument");
                
                double value = NumberHelper.ToDouble(args[0]);
                if (value < 1)
                    throw new PythonException("ValueError", "math domain error");
                
                return new PythonFloat(Math.Log(value + Math.Sqrt(value * value - 1)));
            }));

            // math.atanh(x)
            module.SetAttribute("atanh", new BuiltinFunction("atanh", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "atanh() takes exactly one argument");
                
                double value = NumberHelper.ToDouble(args[0]);
                if (value <= -1 || value >= 1)
                    throw new PythonException("ValueError", "math domain error");
                
                return new PythonFloat(0.5 * Math.Log((1 + value) / (1 - value)));
            }));

            // === Rounding Functions ===
            
            // math.ceil(x)
            module.SetAttribute("ceil", new BuiltinFunction("ceil", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "ceil() takes exactly one argument");
                
                double value = NumberHelper.ToDouble(args[0]);
                return PythonInt.Create((int)Math.Ceiling(value));
            }));

            // math.floor(x)
            module.SetAttribute("floor", new BuiltinFunction("floor", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "floor() takes exactly one argument");
                
                double value = NumberHelper.ToDouble(args[0]);
                return PythonInt.Create((int)Math.Floor(value));
            }));

            // math.trunc(x)
            module.SetAttribute("trunc", new BuiltinFunction("trunc", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "trunc() takes exactly one argument");
                
                double value = NumberHelper.ToDouble(args[0]);
                return PythonInt.Create((int)Math.Truncate(value));
            }));

            // math.round(x[, n])
            module.SetAttribute("round", new BuiltinFunction("round", args =>
            {
                if (args.Count < 1 || args.Count > 2)
                    throw new PythonException("TypeError", "round() takes 1 or 2 arguments");
                
                double value = NumberHelper.ToDouble(args[0]);
                
                if (args.Count == 2)
                {
                    int digits = NumberHelper.ToInt(args[1]);
                    double result = Math.Round(value, digits);
                    return new PythonFloat(result);
                }
                
                return PythonInt.Create((int)Math.Round(value));
            }));

            // === Special Functions ===
            
            // math.factorial(x)
            module.SetAttribute("factorial", new BuiltinFunction("factorial", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "factorial() takes exactly one argument");
                
                if (!(args[0] is PythonInt))
                    throw new PythonException("TypeError", "factorial() only accepts integral values");
                
                int n = NumberHelper.ToInt(args[0]);
                if (n < 0)
                    throw new PythonException("ValueError", "factorial() not defined for negative values");
                
                long result = 1;
                for (int i = 2; i <= n; i++)
                {
                    result *= i;
                    if (result < 0) // Overflow check
                        throw new PythonException("OverflowError", "factorial() result too large");
                }
                
                return PythonInt.Create((int)result);
            }));

            // math.gcd(a, b)
            module.SetAttribute("gcd", new BuiltinFunction("gcd", args =>
            {
                if (args.Count != 2)
                    throw new PythonException("TypeError", "gcd() takes exactly 2 arguments");
                
                int a = Math.Abs(NumberHelper.ToInt(args[0]));
                int b = Math.Abs(NumberHelper.ToInt(args[1]));
                
                while (b != 0)
                {
                    int temp = b;
                    b = a % b;
                    a = temp;
                }
                
                return PythonInt.Create(a);
            }));

            // math.lcm(a, b)
            module.SetAttribute("lcm", new BuiltinFunction("lcm", args =>
            {
                if (args.Count != 2)
                    throw new PythonException("TypeError", "lcm() takes exactly 2 arguments");
                
                int a = Math.Abs(NumberHelper.ToInt(args[0]));
                int b = Math.Abs(NumberHelper.ToInt(args[1]));
                
                if (a == 0 || b == 0)
                    return PythonInt.Create(0);
                
                // Calculate GCD first
                int gcdValue = a;
                int temp = b;
                while (temp != 0)
                {
                    int t = temp;
                    temp = gcdValue % temp;
                    gcdValue = t;
                }
                
                return PythonInt.Create((a / gcdValue) * b);
            }));

            // math.isfinite(x)
            module.SetAttribute("isfinite", new BuiltinFunction("isfinite", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "isfinite() takes exactly one argument");
                
                double value = NumberHelper.ToDouble(args[0]);
                return PythonBool.Create(!double.IsInfinity(value) && !double.IsNaN(value));
            }));

            // math.isinf(x)
            module.SetAttribute("isinf", new BuiltinFunction("isinf", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "isinf() takes exactly one argument");
                
                double value = NumberHelper.ToDouble(args[0]);
                return PythonBool.Create(double.IsInfinity(value));
            }));

            // math.isnan(x)
            module.SetAttribute("isnan", new BuiltinFunction("isnan", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "isnan() takes exactly one argument");
                
                double value = NumberHelper.ToDouble(args[0]);
                return PythonBool.Create(double.IsNaN(value));
            }));

            // math.isclose(a, b, *, rel_tol=1e-09, abs_tol=0.0)
            module.SetAttribute("isclose", new BuiltinFunction("isclose", args =>
            {
                if (args.Count < 2 || args.Count > 4)
                    throw new PythonException("TypeError", "isclose() takes 2 to 4 arguments");
                
                double a = NumberHelper.ToDouble(args[0]);
                double b = NumberHelper.ToDouble(args[1]);
                
                // Default tolerances
                double relTol = 1e-9;
                double absTol = 0.0;
                
                // Parse optional rel_tol
                if (args.Count > 2 && !(args[2] is PythonNone))
                    relTol = NumberHelper.ToDouble(args[2]);
                
                // Parse optional abs_tol
                if (args.Count > 3 && !(args[3] is PythonNone))
                    absTol = NumberHelper.ToDouble(args[3]);
                
                if (double.IsNaN(a) || double.IsNaN(b))
                    return PythonBool.False;
                
                if (double.IsInfinity(a) || double.IsInfinity(b))
                    return PythonBool.Create(a == b);
                
                double diff = Math.Abs(a - b);
                return PythonBool.Create(
                    diff <= absTol || 
                    diff <= relTol * Math.Max(Math.Abs(a), Math.Abs(b))
                );
            }));

            // === Other Useful Functions ===
            
            // math.copysign(x, y)
            module.SetAttribute("copysign", new BuiltinFunction("copysign", args =>
            {
                if (args.Count != 2)
                    throw new PythonException("TypeError", "copysign() takes exactly 2 arguments");
                
                double magnitude = Math.Abs(NumberHelper.ToDouble(args[0]));
                double sign = NumberHelper.ToDouble(args[1]);
                
                if (sign < 0 || (sign == 0 && 1.0 / sign < 0)) // Handle negative zero
                    magnitude = -magnitude;
                
                return new PythonFloat(magnitude);
            }));

            // math.fmod(x, y)
            module.SetAttribute("fmod", new BuiltinFunction("fmod", args =>
            {
                if (args.Count != 2)
                    throw new PythonException("TypeError", "fmod() takes exactly 2 arguments");
                
                double x = NumberHelper.ToDouble(args[0]);
                double y = NumberHelper.ToDouble(args[1]);
                
                if (y == 0)
                    throw new PythonException("ValueError", "math domain error");
                
                return new PythonFloat(x % y);
            }));

            // math.modf(x)
            module.SetAttribute("modf", new BuiltinFunction("modf", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "modf() takes exactly one argument");
                
                double value = NumberHelper.ToDouble(args[0]);
                double intPart = Math.Truncate(value);
                double fracPart = value - intPart;
                
                var tuple = new PythonTuple();
                tuple.Items.Add(new PythonFloat(fracPart));
                tuple.Items.Add(new PythonFloat(intPart));
                return tuple;
            }));

            // math.hypot(*coordinates)
            module.SetAttribute("hypot", new BuiltinFunction("hypot", args =>
            {
                if (args.Count < 2)
                    throw new PythonException("TypeError", "hypot() requires at least 2 arguments");
                
                double sumOfSquares = 0;
                foreach (var arg in args)
                {
                    double value = NumberHelper.ToDouble(arg);
                    sumOfSquares += value * value;
                }
                
                return new PythonFloat(Math.Sqrt(sumOfSquares));
            }));

            // math.prod(iterable)
            module.SetAttribute("prod", new BuiltinFunction("prod", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "prod() takes exactly one argument");
                
                PythonTypeObject result = PythonInt.Create(1);
                
                if (args[0] is PythonList list)
                {
                    foreach (var item in list.Items)
                    {
                        result = NumberHelper.Multiply(result, item);
                    }
                }
                else if (args[0] is PythonTuple tuple)
                {
                    foreach (var item in tuple.Items)
                    {
                        result = NumberHelper.Multiply(result, item);
                    }
                }
                else
                {
                    throw new PythonException("TypeError", "prod() argument must be iterable");
                }
                
                return result;
            }));

            // math.dist(p, q)
            module.SetAttribute("dist", new BuiltinFunction("dist", args =>
            {
                if (args.Count != 2)
                    throw new PythonException("TypeError", "dist() takes exactly 2 arguments");
                
                List<PythonTypeObject> p, q;
                
                if (args[0] is PythonList list1)
                    p = list1.Items;
                else if (args[0] is PythonTuple tuple1)
                    p = tuple1.Items;
                else
                    throw new PythonException("TypeError", "dist() arguments must be iterables");
                
                if (args[1] is PythonList list2)
                    q = list2.Items;
                else if (args[1] is PythonTuple tuple2)
                    q = tuple2.Items;
                else
                    throw new PythonException("TypeError", "dist() arguments must be iterables");
                
                if (p.Count != q.Count)
                    throw new PythonException("ValueError", "both points must have the same number of dimensions");
                
                double sumOfSquares = 0;
                for (int i = 0; i < p.Count; i++)
                {
                    double diff = NumberHelper.ToDouble(p[i]) - NumberHelper.ToDouble(q[i]);
                    sumOfSquares += diff * diff;
                }
                
                return new PythonFloat(Math.Sqrt(sumOfSquares));
            }));

            return module;
        }
    }
}