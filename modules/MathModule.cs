using System;
using System.Collections.Generic;
using System.Numerics;

namespace SharpPy.Modules
{
    /// <summary>
    /// Modules/mathmodule.c. The wrappers keep the rule of math_1, math_1a and math_2: what the C
    /// function answered decides between the value, ValueError and OverflowError.
    /// </summary>
    public static class MathModule
    {
        private enum Fault { None, Domain, Range }

        [ThreadStatic] private static Fault _errno;

        private static Exception Raise(Fault fault) => fault == Fault.Domain
            ? PyValueError.Create("math domain error") : PyOverflowError.Create("math range error");

        internal static double RealNumber(PyObject value) => PyMathFunction.RealNumber(value);

        // METH_O
        private static PyObject One(string name, PyObject[] args, Func<PyObject, PyObject> body)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"math.{name}() takes exactly one argument ({args.Length} given)");
            return body(args[0]);
        }

        private static void Check2(string name, PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"{name} expected 2 arguments, got {args.Length}");
        }

        // math_1
        private static PyObject Math1(PyObject arg, Func<double, double> function, bool canOverflow)
        {
            double x = RealNumber(arg);
            double r = function(x);
            if (double.IsNaN(r) && !double.IsNaN(x))
                throw Raise(Fault.Domain);
            if (double.IsInfinity(r) && double.IsFinite(x))
                throw Raise(canOverflow ? Fault.Range : Fault.Domain);
            return new PyFloat(r);
        }

        // math_1a: the function says itself, by errno.
        private static PyObject Math1a(PyObject arg, Func<double, double> function)
        {
            double x = RealNumber(arg);
            _errno = Fault.None;
            double r = function(x);
            if (_errno != Fault.None)
                throw Raise(_errno);
            return new PyFloat(r);
        }

        // math_2
        private static PyObject Math2(PyObject[] args, Func<double, double, double> function, string name)
        {
            Check2(name, args);
            double x = RealNumber(args[0]), y = RealNumber(args[1]);
            double r = function(x, y);
            if (double.IsNaN(r))
            {
                if (!double.IsNaN(x) && !double.IsNaN(y))
                    throw Raise(Fault.Domain);
            }
            else if (double.IsInfinity(r) && double.IsFinite(x) && double.IsFinite(y))
                throw Raise(Fault.Range);
            return new PyFloat(r);
        }

        private static double M_Atan2(double y, double x)
        {
            if (double.IsNaN(x) || double.IsNaN(y))
                return double.NaN;
            if (double.IsInfinity(y))
            {
                if (double.IsInfinity(x))
                    return Math.CopySign(Math.CopySign(1.0, x) == 1.0 ? 0.25 * Math.PI : 0.75 * Math.PI, y);
                return Math.CopySign(0.5 * Math.PI, y);
            }
            if (double.IsInfinity(x) || y == 0)
                return Math.CopySign(Math.CopySign(1.0, x) == 1.0 ? 0.0 : Math.PI, y);
            return Math.Atan2(y, x);
        }

        // m_remainder
        private static double M_Remainder(double x, double y)
        {
            if (double.IsFinite(x) && double.IsFinite(y))
            {
                if (y == 0.0)
                    return double.NaN;
                double absx = Math.Abs(x), absy = Math.Abs(y);
                double m = absx % absy;
                double c = absy - m, r;
                if (m < c) r = m;
                else if (m > c) r = -c;
                else r = m - 2.0 * ((0.5 * (absx - m)) % absy);
                return Math.CopySign(1.0, x) * r;
            }
            if (double.IsNaN(x)) return x;
            if (double.IsNaN(y)) return y;
            if (double.IsInfinity(x)) return double.NaN;
            return x;
        }

        private static double M_Log(double x)
        {
            if (double.IsFinite(x))
            {
                if (x > 0.0) return Math.Log(x);
                return x == 0.0 ? double.NegativeInfinity : double.NaN;
            }
            if (double.IsNaN(x)) return x;
            return x > 0.0 ? x : double.NaN;
        }

        private static double M_Log2(double x)
        {
            if (!double.IsFinite(x))
                return double.IsNaN(x) ? x : x > 0.0 ? x : double.NaN;
            if (x > 0.0) return Math.Log2(x);
            return x == 0.0 ? double.NegativeInfinity : double.NaN;
        }

        private static double M_Log10(double x)
        {
            if (double.IsFinite(x))
            {
                if (x > 0.0) return Math.Log10(x);
                return x == 0.0 ? double.NegativeInfinity : double.NaN;
            }
            if (double.IsNaN(x)) return x;
            return x > 0.0 ? x : double.NaN;
        }

        // _Py_log1p: 1+x loses the low bits of a small x, and log(u)/(u-1) puts them back.
        private static double M_Log1p(double x)
        {
            if (x == 0.0)
                return x;
            if (x < -1.0)
                return double.NaN;
            if (x == -1.0)
                return double.NegativeInfinity;
            if (double.IsPositiveInfinity(x))
                return x;
            double u = 1.0 + x;
            if (u == 1.0)
                return x;
            return Math.Log(u) * (x / (u - 1.0));
        }

        private static double M_Expm1(double x)
        {
            if (x == 0.0)
                return x;
            if (Math.Abs(x) < 0.7)
            {
                double u = Math.Exp(x);
                if (u == 1.0)
                    return x;
                double um1 = u - 1.0;
                return um1 == -1.0 ? -1.0 : um1 * x / Math.Log(u);
            }
            return Math.Exp(x) - 1.0;
        }

        // loghelper
        private static PyObject LogHelper(PyObject arg, Func<double, double> function)
        {
            if (PyInt.LongCheck(arg) && PyObject.TryGetIndex(arg) is { } integer)
            {
                var value = integer.Value;
                if (value.Sign <= 0)
                    throw Raise(Fault.Domain);
                double x = (double)value;
                if (double.IsInfinity(x))
                {
                    // _PyLong_Frexp: the top 64 bits are enough for a double.
                    long bits = (long)value.GetBitLength();
                    double fraction = Math.ScaleB((double)(value >> (int)(bits - 64)), -64);
                    return new PyFloat(function(fraction) + function(2.0) * bits);
                }
                return new PyFloat(function(x));
            }
            return Math1(arg, function, false);
        }

        // Modules/mathmodule.c m_tgamma, lanczos_sum and gamma_integral.
        private const double LanczosG = 6.024680040776729583740234375;
        private const double LanczosGMinusHalf = 5.524680040776729583740234375;
        private const double LogPi = 1.144729885849400174143427351353058711647;
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

        // m_sinpi
        private static double SinPi(double x)
        {
            double y = Math.Abs(x) % 2.0;
            int n = (int)Math.Round(2.0 * y, MidpointRounding.AwayFromZero);
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

        private static double M_Gamma(double x)
        {
            if (!double.IsFinite(x))
            {
                if (double.IsNaN(x) || x > 0.0)
                    return x;
                _errno = Fault.Domain;
                return double.NaN;
            }
            if (x == Math.Floor(x) && x <= 2.0)
            {
                if (x <= 0.0)
                {
                    _errno = Fault.Domain;
                    return double.NaN;
                }
            }
            if (x == Math.Floor(x) && x <= GammaIntegral.Length)
                return GammaIntegral[(int)x - 1];
            double absx = Math.Abs(x);
            if (absx < 1e-20)
            {
                double inverse = 1.0 / x;
                if (double.IsInfinity(inverse))
                    _errno = Fault.Range;
                return inverse;
            }
            if (absx > 200.0)
            {
                if (x < 0.0)
                    return 0.0 / SinPi(x);
                _errno = Fault.Range;
                return double.PositiveInfinity;
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
                if (absx < 140.0)
                    r /= Math.Pow(y, absx - 0.5);
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
                if (absx < 140.0)
                    r *= Math.Pow(y, absx - 0.5);
                else
                {
                    double sqrtpow = Math.Pow(y, absx / 2.0 - 0.25);
                    r *= sqrtpow;
                    r *= sqrtpow;
                }
            }
            if (double.IsInfinity(r))
                _errno = Fault.Range;
            return r;
        }

        private static double M_LGamma(double x)
        {
            if (!double.IsFinite(x))
                return double.IsNaN(x) ? x : double.PositiveInfinity;
            if (x == Math.Floor(x) && x <= 2.0)
            {
                if (x <= 0.0)
                {
                    _errno = Fault.Domain;
                    return double.PositiveInfinity;
                }
                return 0.0;
            }
            double absx = Math.Abs(x);
            if (absx < 1e-20)
                return -Math.Log(absx);
            double r = Math.Log(LanczosSum(absx)) - LanczosG;
            r += (absx - 0.5) * (Math.Log(absx + LanczosG - 0.5) - 1);
            if (x < 0.0)
                r = LogPi - Math.Log(Math.Abs(SinPi(absx))) - Math.Log(absx) - r;
            if (double.IsInfinity(r))
                _errno = Fault.Range;
            return r;
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
            if (x >= 30.0)
                return 0.0;
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

        private static double M_Erf(double x)
        {
            if (double.IsNaN(x))
                return x;
            double absx = Math.Abs(x);
            if (absx < 1.5)
                return ErfSeries(x);
            double cf = ErfcContinuedFraction(absx);
            return x > 0.0 ? 1.0 - cf : cf - 1.0;
        }

        private static double M_Erfc(double x)
        {
            if (double.IsNaN(x))
                return x;
            double absx = Math.Abs(x);
            if (absx < 1.5)
                return 1.0 - ErfSeries(x);
            double cf = ErfcContinuedFraction(absx);
            return x > 0.0 ? cf : 2.0 - cf;
        }

        // ceil, floor: a type's own slot, then the double.
        private static PyObject Rounded(string name, string slot, PyObject value, Func<double, double> function)
        {
            if (!value.IsExactly(PyType.FloatType) && value.LookupSpecialMethodUncached(slot) is { } method)
                return value.BindSpecialMethod(method).Call(Array.Empty<PyObject>(), null);
            return Whole(function(RealNumber(value)));
        }

        // PyLong_FromDouble
        private static PyObject Whole(double value)
        {
            if (double.IsInfinity(value))
                throw PyOverflowError.Create("cannot convert float infinity to integer");
            if (double.IsNaN(value))
                throw PyValueError.Create("cannot convert float NaN to integer");
            return PyInt.From(new BigInteger(value));
        }

        private static BigInteger Index(PyObject value) =>
            (PyObject.IndexOrThrow(value)).Value;

        private static PyObject Factorial(PyObject value)
        {
            var n = Index(value);
            if (n.Sign < 0)
                throw PyValueError.Create("factorial() not defined for negative values");
            if (n > int.MaxValue)
                throw PyOverflowError.Create($"factorial() argument should not exceed {long.MaxValue}");
            return PyInt.From(Product(1, (long)n));
        }

        // The product of first..last, halved so that the big multiplications are between like sizes.
        private static BigInteger Product(long first, long last)
        {
            if (first > last)
                return BigInteger.One;
            if (last - first < 16)
            {
                var result = new BigInteger(first);
                for (long i = first + 1; i <= last; i++)
                    result *= i;
                return result;
            }
            long middle = first + (last - first) / 2;
            return Product(first, middle) * Product(middle + 1, last);
        }

        private static PyObject Gcd(PyObject[] args)
        {
            var result = BigInteger.Zero;
            foreach (var arg in args)
                result = BigInteger.GreatestCommonDivisor(result, Index(arg));
            return PyInt.From(result);
        }

        private static PyObject Lcm(PyObject[] args)
        {
            var result = BigInteger.One;
            foreach (var arg in args)
            {
                var value = BigInteger.Abs(Index(arg));
                result = result.IsZero || value.IsZero ? BigInteger.Zero : result / BigInteger.GreatestCommonDivisor(result, value) * value;
            }
            return PyInt.From(result);
        }

        private static PyObject ISqrt(PyObject value)
        {
            var n = Index(value);
            if (n.Sign < 0)
                throw PyValueError.Create("isqrt() argument must be nonnegative");
            if (n.IsZero)
                return PyInt.From(0);
            var x = BigInteger.One << (int)((n.GetBitLength() + 1) / 2);
            while (true)
            {
                var y = (x + n / x) >> 1;
                if (y >= x)
                    return PyInt.From(x);
                x = y;
            }
        }

        private static PyObject Perm(PyObject[] args)
        {
            if (args.Length is < 1 or > 2)
                throw PyTypeError.Create(args.Length < 1 ? $"perm expected at least 1 argument, got {args.Length}" : $"perm expected at most 2 arguments, got {args.Length}");
            if (args.Length == 1 || args[1] is PyNone)
                return Factorial(args[0]);
            BigInteger n = Index(args[0]), k = Index(args[1]);
            if (n.Sign < 0) throw PyValueError.Create("n must be a non-negative integer");
            if (k.Sign < 0) throw PyValueError.Create("k must be a non-negative integer");
            if (k > n) return PyInt.From(0);
            if (k > long.MaxValue) throw PyOverflowError.Create($"k must not exceed {long.MaxValue}");
            var result = BigInteger.One;
            for (long i = 0; i < (long)k; i++)
                result *= n - i;
            return PyInt.From(result);
        }

        private static PyObject Comb(PyObject[] args)
        {
            Check2("comb", args);
            BigInteger n = Index(args[0]), k = Index(args[1]);
            if (n.Sign < 0) throw PyValueError.Create("n must be a non-negative integer");
            if (k.Sign < 0) throw PyValueError.Create("k must be a non-negative integer");
            if (k > n) return PyInt.From(0);
            k = BigInteger.Min(k, n - k);
            if (k > long.MaxValue) throw PyOverflowError.Create($"min(n - k, k) must not exceed {long.MaxValue}");
            var result = BigInteger.One;
            for (long i = 0; i < (long)k; i++)
                result = result * (n - i) / (i + 1);
            return PyInt.From(result);
        }

        // math_fsum: Shewchuk partials, with the half-even correction at the end.
        private static PyObject FSum(PyObject iterable)
        {
            var p = new List<double>();
            double specialSum = 0.0, infSum = 0.0;
            foreach (var item in PyObject.IterateItems(iterable))
            {
                double x = RealNumber(item);
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
                        p.Add(x);
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

        private readonly record struct DoubleLength(double Hi, double Lo);
        private readonly record struct TripleLength(double Hi, double Lo, double Tiny);

        private static DoubleLength FastSum(double a, double b)
        {
            double x = a + b;
            return new DoubleLength(x, (a - x) + b);
        }

        private static DoubleLength Sum(double a, double b)
        {
            double x = a + b, z = x - a;
            return new DoubleLength(x, (a - (x - z)) + (b - z));
        }

        private static DoubleLength Mul(double x, double y)
        {
            double z = x * y;
            return new DoubleLength(z, Math.FusedMultiplyAdd(x, y, -z));
        }

        private static TripleLength Fma(double x, double y, TripleLength total)
        {
            var pr = Mul(x, y);
            var sm = Sum(total.Hi, pr.Hi);
            var r1 = Sum(total.Lo, pr.Lo);
            var r2 = Sum(r1.Hi, sm.Lo);
            return new TripleLength(sm.Hi, r2.Hi, total.Tiny + r1.Lo + r2.Lo);
        }

        // vector_norm
        private static double VectorNorm(double[] vec, double max, bool foundNan)
        {
            if (double.IsInfinity(max)) return max;
            if (foundNan) return double.NaN;
            if (max == 0.0 || vec.Length <= 1) return max;
            int exponent = Math.ILogB(max) + 1;
            const double smallest = 2.2250738585072014e-308;
            if (exponent < -1023)
            {
                for (int i = 0; i < vec.Length; i++)
                    vec[i] /= smallest;
                return smallest * VectorNorm(vec, max / smallest, foundNan);
            }
            double scale = Math.ScaleB(1.0, -exponent), csum = 1.0, frac1 = 0.0, frac2 = 0.0;
            DoubleLength pr, sm;
            foreach (double value in vec)
            {
                double x = value * scale;
                pr = Mul(x, x);
                sm = FastSum(csum, pr.Hi);
                csum = sm.Hi;
                frac1 += pr.Lo;
                frac2 += sm.Lo;
            }
            double h = Math.Sqrt(csum - 1.0 + (frac1 + frac2));
            pr = Mul(-h, h);
            sm = FastSum(csum, pr.Hi);
            csum = sm.Hi;
            frac1 += pr.Lo;
            frac2 += sm.Lo;
            h += (csum - 1.0 + (frac1 + frac2)) / (2.0 * h);
            return h / scale;
        }

        private static PyObject Hypot(PyObject[] args)
        {
            var vec = new double[args.Length];
            double max = 0.0;
            bool foundNan = false;
            for (int i = 0; i < args.Length; i++)
            {
                double x = Math.Abs(RealNumber(args[i]));
                foundNan |= double.IsNaN(x);
                if (x > max) max = x;
                vec[i] = x;
            }
            return new PyFloat(VectorNorm(vec, max, foundNan));
        }

        private static PyObject Dist(PyObject[] args)
        {
            Check2("dist", args);
            var p = (args[0] as PyTuple ?? args[0].AsTuple()).Items;
            var q = (args[1] as PyTuple ?? args[1].AsTuple()).Items;
            if (p.Length != q.Length)
                throw PyValueError.Create("both points must have the same number of dimensions");
            var vec = new double[p.Length];
            double max = 0.0;
            bool foundNan = false;
            for (int i = 0; i < p.Length; i++)
            {
                double x = Math.Abs(RealNumber(p[i]) - RealNumber(q[i]));
                foundNan |= double.IsNaN(x);
                if (x > max) max = x;
                vec[i] = x;
            }
            return new PyFloat(VectorNorm(vec, max, foundNan));
        }

        private static PyObject Add(PyObject a, PyObject b) => PyVM.Instance.ExecuteBinaryOpType(a, b, BinaryOpType.ADD);
        private static PyObject Multiply(PyObject a, PyObject b) => PyVM.Instance.ExecuteBinaryOpType(a, b, BinaryOpType.MULTIPLY);

        private static bool ExactInt(PyObject value) => value.IsExactly(PyType.IntType);
        private static bool ExactFloat(PyObject value) => value.IsExactly(PyType.FloatType);

        private static bool Next(PyObject iterator, out PyObject item)
        {
            try
            {
                item = iterator.Next();
                return true;
            }
            catch (PythonException error) when (error.PyException is PyStopIteration)
            {
                item = null;
                return false;
            }
        }

        // math_sumprod_impl: ints together, floats in triple length, and anything else by the operators.
        private static PyObject SumProd(PyObject[] args)
        {
            Check2("sumprod", args);
            PyObject pIterator = args[0].GetIterator(), qIterator = args[1].GetIterator();
            PyObject total = PyInt.From(0);
            bool intPath = true, intInUse = false, floatPath = true, floatInUse = false;
            var intTotal = BigInteger.Zero;
            var floatTotal = new TripleLength(0, 0, 0);
            while (true)
            {
                bool hasP = Next(pIterator, out var p), hasQ = Next(qIterator, out var q);
                if (hasP != hasQ)
                    throw PyValueError.Create("Inputs are not the same length");
                bool finished = !hasP;
                if (intPath)
                {
                    if (!finished && ExactInt(p) && ExactInt(q))
                    {
                        var product = ((PyInt)p).Value * ((PyInt)q).Value;
                        intTotal += product;
                        intInUse = true;
                        continue;
                    }
                    intPath = false;
                    if (intInUse)
                    {
                        total = Add(total, PyInt.From(intTotal));
                        intInUse = false;
                    }
                }
                if (floatPath)
                {
                    if (!finished)
                    {
                        double x = 0, y = 0;
                        bool usable = true;
                        bool pFloat = ExactFloat(p), qFloat = ExactFloat(q);
                        try
                        {
                            if (pFloat && qFloat) { x = ((PyFloat)p).Value; y = ((PyFloat)q).Value; }
                            else if (pFloat && (ExactInt(q) || q is PyBool)) { x = ((PyFloat)p).Value; y = RealNumber(q); }
                            else if (qFloat && (ExactInt(p) || p is PyBool)) { y = ((PyFloat)q).Value; x = RealNumber(p); }
                            else usable = false;
                        }
                        catch (PythonException)
                        {
                            usable = false;
                        }
                        if (usable)
                        {
                            var next = Fma(x, y, floatTotal);
                            if (double.IsFinite(next.Hi))
                            {
                                floatTotal = next;
                                floatInUse = true;
                                continue;
                            }
                        }
                    }
                    floatPath = false;
                    if (floatInUse)
                    {
                        total = Add(total, new PyFloat(floatTotal.Hi + floatTotal.Lo + floatTotal.Tiny));
                        floatInUse = false;
                    }
                }
                if (finished)
                    return total;
                total = Add(total, Multiply(p, q));
            }
        }

        private static PyObject Prod(PyObject[] args, PyDict kwargs)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"prod() takes exactly 1 positional argument ({args.Length} given)");
            PyObject result = PyInt.From(1);
            if (kwargs != null)
                foreach (var pair in kwargs.InternalDict)
                {
                    if ((pair.Key as PyString)?.Value != "start")
                        throw PyArgs.InvalidKeyword("prod", pair.Key);
                    result = pair.Value;
                }
            var iterator = args[0].GetIterator();
            while (Next(iterator, out var item))
                result = Multiply(result, item);
            return result;
        }

        private static PyObject Pow(PyObject[] args)
        {
            Check2("pow", args);
            double x = RealNumber(args[0]), y = RealNumber(args[1]), r;
            var fault = Fault.None;
            if (!double.IsFinite(x) || !double.IsFinite(y))
            {
                if (double.IsNaN(x))
                    r = y == 0.0 ? 1.0 : x;
                else if (double.IsNaN(y))
                    r = x == 1.0 ? 1.0 : y;
                else if (double.IsInfinity(x))
                {
                    bool oddY = double.IsFinite(y) && Math.Abs(y) % 2.0 == 1.0;
                    if (y > 0.0) r = oddY ? x : Math.Abs(x);
                    else if (y == 0.0) r = 1.0;
                    else r = oddY ? Math.CopySign(0.0, x) : 0.0;
                }
                else if (Math.Abs(x) == 1.0)
                    r = 1.0;
                else if (y > 0.0 && Math.Abs(x) > 1.0)
                    r = y;
                else if (y < 0.0 && Math.Abs(x) < 1.0)
                {
                    r = -y;
                }
                else
                    r = 0.0;
            }
            else
            {
                r = x == 1.0 || y == 0.0 ? 1.0 : Math.Pow(x, y);
                if (double.IsNaN(r))
                    fault = Fault.Domain;
                else if (double.IsInfinity(r))
                    fault = x == 0.0 ? Fault.Domain : Fault.Range;
            }
            if (fault != Fault.None)
                throw Raise(fault);
            return new PyFloat(r);
        }

        private static PyObject FMod(PyObject[] args)
        {
            Check2("fmod", args);
            double x = RealNumber(args[0]), y = RealNumber(args[1]);
            if (double.IsInfinity(y) && double.IsFinite(x))
                return new PyFloat(x);
            double r = x % y;
            if (double.IsNaN(r) && !double.IsNaN(x) && !double.IsNaN(y))
                throw Raise(Fault.Domain);
            return new PyFloat(r == 0.0 ? Math.CopySign(0.0, x) : r);
        }

        private static PyObject FrExp(double x)
        {
            if (double.IsNaN(x) || double.IsInfinity(x) || x == 0.0)
                return PyTuple.From(new PyObject[] { new PyFloat(x), PyInt.From(0) });
            int exponent = Math.ILogB(x) + 1;
            return PyTuple.From(new PyObject[] { new PyFloat(Math.ScaleB(x, -exponent)), PyInt.From(exponent) });
        }

        private static PyObject LdExp(PyObject[] args)
        {
            Check2("ldexp", args);
            double x = RealNumber(args[0]);
            if (!(PyInt.LongCheck(args[1])))
                throw PyTypeError.Create("Expected an int as second argument to ldexp.");
            var exponent = PyObject.TryGetIndex(args[1]).Value;
            double r;
            if (x == 0.0 || !double.IsFinite(x))
                r = x;
            else if (exponent > int.MaxValue)
                throw Raise(Fault.Range);
            else if (exponent < int.MinValue)
                r = Math.CopySign(0.0, x);
            else
            {
                r = Math.ScaleB(x, (int)exponent);
                if (double.IsInfinity(r))
                    throw Raise(Fault.Range);
            }
            return new PyFloat(r);
        }

        private static PyObject ModF(double x)
        {
            if (double.IsInfinity(x))
                return PyTuple.From(new PyObject[] { new PyFloat(Math.CopySign(0.0, x)), new PyFloat(x) });
            if (double.IsNaN(x))
                return PyTuple.From(new PyObject[] { new PyFloat(x), new PyFloat(x) });
            double whole = Math.Truncate(x);
            return PyTuple.From(new PyObject[] { new PyFloat(Math.CopySign(x - whole, x)), new PyFloat(whole) });
        }

        private static double NextToward(double x, double y)
        {
            if (double.IsNaN(x)) return x;
            if (double.IsNaN(y)) return y;
            if (x == y) return y;
            return y > x ? Math.BitIncrement(x) : Math.BitDecrement(x);
        }

        // math_nextafter_impl
        private static PyObject NextAfter(PyObject[] args, PyDict kwargs)
        {
            Check2("nextafter", args);
            double x = RealNumber(args[0]), y = RealNumber(args[1]);
            PyObject steps = null;
            if (kwargs != null)
                foreach (var pair in kwargs.InternalDict)
                {
                    if ((pair.Key as PyString)?.Value != "steps")
                        throw PyArgs.InvalidKeyword("nextafter", pair.Key);
                    steps = pair.Value;
                }
            if (steps == null || steps is PyNone)
                return new PyFloat(NextToward(x, y));
            var count = Index(steps);
            if (count.Sign < 0)
                throw PyValueError.Create("steps must be a non-negative integer");
            if (count.IsZero) return new PyFloat(x);
            if (double.IsNaN(x)) return new PyFloat(x);
            if (double.IsNaN(y)) return new PyFloat(y);
            ulong usteps = count > ulong.MaxValue ? ulong.MaxValue : (ulong)count;
            ulong ux = BitConverter.DoubleToUInt64Bits(x), uy = BitConverter.DoubleToUInt64Bits(y);
            if (ux == uy) return new PyFloat(x);
            const ulong sign = 1UL << 63;
            ulong ax = ux & ~sign, ay = uy & ~sign;
            if (((ux ^ uy) & sign) != 0)
            {
                if (ax + ay <= usteps) return new PyFloat(y);
                if (ax < usteps) return new PyFloat(BitConverter.UInt64BitsToDouble((uy & sign) | (usteps - ax)));
                return new PyFloat(BitConverter.UInt64BitsToDouble(ux - usteps));
            }
            if (ax > ay)
                return new PyFloat(ax - ay >= usteps ? BitConverter.UInt64BitsToDouble(ux - usteps) : y);
            return new PyFloat(ay - ax >= usteps ? BitConverter.UInt64BitsToDouble(ux + usteps) : y);
        }

        private static PyObject Ulp(double x)
        {
            if (double.IsNaN(x)) return new PyFloat(x);
            x = Math.Abs(x);
            if (double.IsInfinity(x)) return new PyFloat(x);
            double next = NextToward(x, double.PositiveInfinity);
            return new PyFloat(double.IsInfinity(next) ? x - NextToward(x, double.NegativeInfinity) : next - x);
        }

        private static PyObject IsClose(PyObject[] args, PyDict kwargs)
        {
            PyObject a = args.Length > 0 ? args[0] : null, b = args.Length > 1 ? args[1] : null, relative = null, absolute = null;
            // _PyArg_UnpackKeywords: the count, the names that are taken, what is missing, then the rest.
            if (args.Length > 2)
                throw PyTypeError.Create($"isclose() takes exactly 2 positional arguments ({args.Length} given)");
            string unknown = null;
            if (kwargs != null)
                foreach (var pair in kwargs.GetInternalDict())
                {
                    string name = pair.Key.ToStr().Value;
                    switch (name)
                    {
                        case "a":
                            if (a != null) throw PyTypeError.Create("argument for isclose() given by name ('a') and position (1)");
                            a = pair.Value;
                            break;
                        case "b":
                            if (b != null) throw PyTypeError.Create("argument for isclose() given by name ('b') and position (2)");
                            b = pair.Value;
                            break;
                        case "rel_tol": relative = pair.Value; break;
                        case "abs_tol": absolute = pair.Value; break;
                        default: unknown ??= name; break;
                    }
                }
            if (a == null) throw PyArgs.MissingArgument("isclose", "a", 1);
            if (b == null) throw PyArgs.MissingArgument("isclose", "b", 2);
            if (unknown != null) throw PyTypeError.Create($"'{unknown}' is an invalid keyword argument for isclose()");
            double left = RealNumber(a), right = RealNumber(b);
            double rel = relative == null ? 1e-9 : RealNumber(relative);
            double abs = absolute == null ? 0 : RealNumber(absolute);
            if (rel < 0 || abs < 0)
                throw PyValueError.Create("tolerances must be non-negative");
            if (left == right) return PyBool.True;
            if (double.IsInfinity(left) || double.IsInfinity(right)) return PyBool.False;
            double difference = Math.Abs(right - left);
            return PyBool.FromBool(difference <= Math.Abs(rel * right) || difference <= Math.Abs(rel * left) || difference <= abs);
        }

        public static PyModule CreateMathModule()
        {
            var module = new PyModule("math");
            var names = module.ModuleDict;
            names["pi"] = new PyFloat(Math.PI);
            names["e"] = new PyFloat(Math.E);
            names["tau"] = new PyFloat(2 * Math.PI);
            names["inf"] = new PyFloat(double.PositiveInfinity);
            names["nan"] = new PyFloat(PyFloat.QuietNaN);

            void Func1(string name, Func<double, double> function, bool canOverflow = false) =>
                names[name] = new PyBuiltinFunction(name, args => One(name, args, arg => Math1(arg, function, canOverflow)));
            void Func1a(string name, Func<double, double> function) =>
                names[name] = new PyBuiltinFunction(name, args => One(name, args, arg => Math1a(arg, function)));
            void Func2(string name, Func<double, double, double> function) =>
                names[name] = new PyBuiltinFunction(name, args => Math2(args, function, name));
            void FuncO(string name, Func<PyObject, PyObject> body) =>
                names[name] = new PyBuiltinFunction(name, args => One(name, args, body));
            void Func(string name, Func<PyObject[], PyObject> body) => names[name] = new PyBuiltinFunction(name, body);
            void FuncK(string name, Func<PyObject[], PyDict, PyObject> body) => names[name] = new PyBuiltinFunction(name, body);

            Func1("acos", Math.Acos);
            Func1("acosh", Math.Acosh);
            Func1("asin", Math.Asin);
            Func1("asinh", Math.Asinh);
            Func1("atan", Math.Atan);
            Func1("atanh", Math.Atanh);
            Func1("cbrt", Math.Cbrt);
            Func1("cos", Math.Cos);
            Func1("cosh", Math.Cosh, true);
            Func1("exp", Math.Exp, true);
            Func1("exp2", x => Math.Pow(2.0, x), true);
            Func1("expm1", M_Expm1, true);
            Func1("fabs", Math.Abs);
            Func1("log1p", M_Log1p);
            Func1("sin", Math.Sin);
            Func1("sinh", Math.Sinh, true);
            Func1("sqrt", Math.Sqrt);
            Func1("tan", Math.Tan);
            Func1("tanh", Math.Tanh);
            Func1a("erf", M_Erf);
            Func1a("erfc", M_Erfc);
            Func1a("gamma", M_Gamma);
            Func1a("lgamma", M_LGamma);
            Func2("atan2", M_Atan2);
            Func2("copysign", Math.CopySign);
            Func2("remainder", M_Remainder);

            FuncO("ceil", value => Rounded("ceil", "__ceil__", value, Math.Ceiling));
            FuncO("floor", value => Rounded("floor", "__floor__", value, Math.Floor));
            FuncO("trunc", value =>
            {
                var method = value.LookupSpecialMethodUncached("__trunc__")
                    ?? throw PyTypeError.Create($"type {value.GetTypeName()} doesn't define __trunc__ method");
                return value.BindSpecialMethod(method).Call(Array.Empty<PyObject>(), null);
            });
            FuncO("factorial", Factorial);
            FuncO("fsum", FSum);
            FuncO("isqrt", ISqrt);
            FuncO("isfinite", value => PyBool.FromBool(double.IsFinite(RealNumber(value))));
            FuncO("isinf", value => PyBool.FromBool(double.IsInfinity(RealNumber(value))));
            FuncO("isnan", value => PyBool.FromBool(double.IsNaN(RealNumber(value))));
            FuncO("frexp", value => FrExp(RealNumber(value)));
            FuncO("modf", value => ModF(RealNumber(value)));
            FuncO("ulp", value => Ulp(RealNumber(value)));
            FuncO("degrees", value => new PyFloat(RealNumber(value) * (180.0 / Math.PI)));
            FuncO("radians", value => new PyFloat(RealNumber(value) * (Math.PI / 180.0)));
            FuncO("log2", value => LogHelper(value, M_Log2));
            FuncO("log10", value => LogHelper(value, M_Log10));
            Func("log", args =>
            {
                if (args.Length is < 1 or > 2)
                    throw PyTypeError.Create(args.Length < 1 ? "log expected at least 1 argument, got 0" : $"log expected at most 2 arguments, got {args.Length}");
                var number = LogHelper(args[0], M_Log);
                if (args.Length == 1)
                    return number;
                var divisor = LogHelper(args[1], M_Log);
                return PyVM.Instance.ExecuteBinaryOpType(number, divisor, BinaryOpType.TRUE_DIVIDE);
            });
            Func("gcd", Gcd);
            Func("lcm", Lcm);
            Func("perm", Perm);
            Func("comb", Comb);
            Func("hypot", Hypot);
            Func("dist", Dist);
            Func("pow", Pow);
            Func("fmod", FMod);
            Func("ldexp", LdExp);
            Func("sumprod", SumProd);
            FuncK("prod", Prod);
            FuncK("nextafter", NextAfter);
            FuncK("isclose", IsClose);
            return module;
        }
    }

    /// <summary>The conversion str % and bytes % share with math.</summary>
    public static class PyMathFunction
    {
        // PyFloat_AsDouble
        internal static double RealNumber(PyObject value)
        {
            if (PyFloat.TryFloatSelf(value, out var real)) return real.Value;
            if (value is PyBool boolean) return boolean.Value ? 1 : 0;
            if (value is PyInt { IsExact: true } integer) return IntegerDouble(integer);
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
    }
}
