using System;
using System.Numerics;

namespace SharpPy
{
    /// <summary>
    /// Python float 타입 구현 - C# double을 기반으로 한 부동소수점 숫자
    /// CPython 호환을 위해 원본 문자열 표현 보존
    /// </summary>
    public class PyFloat : PyObject
    {
        static PyFloat()
        {
            InitializeFloatDescriptors();
        }

        /// <summary>
        /// Initialize float type descriptors (CPython 3.12 compatible)
        /// CPython reference: Objects/floatobject.c:1800-1900 - float_methods
        /// </summary>
        public static void InitializeFloatDescriptors()
        {
            var floatType = PyType.FloatType;

            // CPython 3.12: Objects/floatobject.c:1420-1430 - float_is_integer
            // Return True if the float is an integer.
            floatType.TypeDict["is_integer"] = new PyMethodDescriptor(
                "is_integer", floatType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"is_integer() takes no arguments ({args.Length} given)");
                    if (self is not PyFloat floatObj)
                        throw PyTypeError.Create($"descriptor 'is_integer' requires a 'float' object but received a '{self.GetTypeName()}'");

                    double value = floatObj.Value;
                    if (double.IsInfinity(value) || double.IsNaN(value))
                        return PyBool.False;

                    return PyBool.FromBool(value == Math.Floor(value));
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/floatobject.c:1432-1470 - float_as_integer_ratio
            // Return a pair of integers, whose ratio is exactly equal to the original float.
            floatType.TypeDict["as_integer_ratio"] = new PyMethodDescriptor(
                "as_integer_ratio", floatType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"as_integer_ratio() takes no arguments ({args.Length} given)");
                    if (self is not PyFloat floatObj)
                        throw PyTypeError.Create($"descriptor 'as_integer_ratio' requires a 'float' object but received a '{self.GetTypeName()}'");

                    double value = floatObj.Value;

                    if (double.IsInfinity(value))
                        throw PyOverflowError.Create("cannot convert Infinity to integer ratio");
                    if (double.IsNaN(value))
                        throw PyValueError.Create("cannot convert NaN to integer ratio");

                    // For exact integer values
                    if (value == Math.Floor(value))
                        return TupleCache.CreatePair(new PyInt((long)value), new PyInt(1));

                    // Convert to fraction (simplified algorithm)
                    long sign = value < 0 ? -1 : 1;
                    value = Math.Abs(value);

                    long numerator = (long)(value * 1e15);  // Use high precision
                    long denominator = 1000000000000000;  // 10^15

                    // Simple GCD to reduce fraction
                    long gcd = GCD(Math.Abs(numerator), denominator);
                    numerator = (numerator / gcd) * sign;
                    denominator = denominator / gcd;

                    return TupleCache.CreatePair(new PyInt(numerator), new PyInt(denominator));
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/floatobject.c:1545-1600 - float_hex
            // Return a hexadecimal representation of a floating-point number.
            floatType.TypeDict["hex"] = new PyMethodDescriptor(
                "hex", floatType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"hex() takes no arguments ({args.Length} given)");
                    if (self is not PyFloat floatObj)
                        throw PyTypeError.Create($"descriptor 'hex' requires a 'float' object but received a '{self.GetTypeName()}'");

                    // CPython 3.12: Objects/floatobject.c float_hex_impl
                    return floatObj.Hex();
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/floatobject.c:1311-1540 - float_fromhex (classmethod)
            // Create a floating-point number from a hexadecimal string.
            // Format: [sign] ['0x'] integer ['.' fraction] ['p' exponent]
            // Example: '0x1.921fb54442d18p+1' = pi
            var fromhexMethod = new PyMethodDescriptor(
                "fromhex", floatType,
                (self, args, kwargs) => {
                    // self is the class (PyType) when called as classmethod
                    if (args.Length != 1)
                        throw PyTypeError.Create($"fromhex() takes exactly one argument ({args.Length} given)");

                    if (args[0] is not PyString hexStr)
                        throw PyTypeError.Create($"fromhex() argument must be a string, not '{args[0].GetTypeName()}'");

                    string s = hexStr.Value.Trim();
                    int i = 0;

                    // Handle inf/nan
                    if (s.Length >= 3)
                    {
                        string lower = s.ToLowerInvariant();
                        if (lower.StartsWith("inf") || lower.StartsWith("+inf"))
                            return new PyFloat(double.PositiveInfinity);
                        if (lower.StartsWith("-inf"))
                            return new PyFloat(double.NegativeInfinity);
                        if (lower.StartsWith("nan") || lower.StartsWith("+nan") || lower.StartsWith("-nan"))
                            return new PyFloat(double.NaN);
                    }

                    // Optional sign
                    bool negate = false;
                    if (i < s.Length && s[i] == '-')
                    {
                        negate = true;
                        i++;
                    }
                    else if (i < s.Length && s[i] == '+')
                    {
                        i++;
                    }

                    // Optional 0x prefix
                    if (i + 1 < s.Length && s[i] == '0' && (s[i + 1] == 'x' || s[i + 1] == 'X'))
                    {
                        i += 2;
                    }

                    // Parse coefficient: <integer> [. <fraction>]
                    int coeffStart = i;
                    while (i < s.Length && IsHexDigit(s[i]))
                        i++;

                    int fractionStart = i;
                    int fractionDigits = 0;
                    if (i < s.Length && s[i] == '.')
                    {
                        i++;
                        fractionStart = i;
                        while (i < s.Length && IsHexDigit(s[i]))
                        {
                            fractionDigits++;
                            i++;
                        }
                    }

                    int coeffEnd = i;
                    if (coeffEnd == coeffStart)
                        throw PyValueError.Create($"invalid hexadecimal floating-point string: '{hexStr.Value}'");

                    // Parse exponent: [p <exponent>]
                    long exp = 0;
                    if (i < s.Length && (s[i] == 'p' || s[i] == 'P'))
                    {
                        i++;
                        bool expNeg = false;
                        if (i < s.Length && s[i] == '-')
                        {
                            expNeg = true;
                            i++;
                        }
                        else if (i < s.Length && s[i] == '+')
                        {
                            i++;
                        }

                        int expStart = i;
                        while (i < s.Length && char.IsDigit(s[i]))
                            i++;

                        if (i == expStart)
                            throw PyValueError.Create($"invalid hexadecimal floating-point string: '{hexStr.Value}'");

                        exp = long.Parse(s.Substring(expStart, i - expStart));
                        if (expNeg)
                            exp = -exp;
                    }

                    // Build the value: mantissa * 2^exponent
                    double x = 0.0;

                    // Parse hex digits from coefficient
                    for (int j = coeffStart; j < fractionStart - 1; j++)
                    {
                        if (s[j] != '.')
                            x = 16.0 * x + HexValue(s[j]);
                    }
                    for (int j = fractionStart; j < coeffEnd; j++)
                    {
                        x = 16.0 * x + HexValue(s[j]);
                    }

                    // Adjust exponent for fractional part (each hex digit = 4 bits)
                    exp -= 4 * fractionDigits;

                    // Apply exponent using ldexp (x * 2^exp)
                    if (exp != 0)
                    {
                        x = Math.ScaleB(x, (int)exp);  // ScaleB(x, n) = x * 2^n
                    }

                    if (negate)
                        x = -x;

                    return new PyFloat(x);
                },
                minArgs: 1, maxArgs: 1
            );
            // Wrap in classmethod descriptor
            floatType.TypeDict["fromhex"] = new PyClassMethodDescriptor("fromhex", floatType, fromhexMethod);

            // CPython 3.12: Objects/floatobject.c:1700-1710 - float_conjugate
            // Return self, the complex conjugate of any float.
            floatType.TypeDict["conjugate"] = new PyMethodDescriptor(
                "conjugate", floatType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"conjugate() takes no arguments ({args.Length} given)");
                    if (self is not PyFloat floatObj)
                        throw PyTypeError.Create($"descriptor 'conjugate' requires a 'float' object but received a '{self.GetTypeName()}'");

                    return floatObj;  // For real numbers, conjugate is the number itself
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/floatobject.c - float.real property
            // Return the real part of the float (which is the float itself).
            floatType.TypeDict["real"] = new PyGetSetDescriptor(
                "real", floatType,
                getter: self => {
                    if (self is not PyFloat)
                        throw PyTypeError.Create($"descriptor 'real' for 'float' objects doesn't apply to a '{self.GetTypeName()}' object");
                    return self;  // For float, real is itself
                }
            );

            // CPython 3.12: Objects/floatobject.c - float.imag property
            // Return the imaginary part of the float (which is 0.0).
            floatType.TypeDict["imag"] = new PyGetSetDescriptor(
                "imag", floatType,
                getter: self => {
                    if (self is not PyFloat)
                        throw PyTypeError.Create($"descriptor 'imag' for 'float' objects doesn't apply to a '{self.GetTypeName()}' object");
                    return new PyFloat(0.0);  // For float, imaginary part is 0
                }
            );

            // CPython 3.12: Objects/floatobject.c:1710-1750 - float_format
            // float.__format__(format_spec) - Format the float according to format_spec
            floatType.TypeDict["__format__"] = new PyMethodDescriptor(
                "__format__", floatType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"__format__() takes 1 positional argument ({args.Length} given)");
                    if (self is not PyFloat floatObj)
                        throw PyTypeError.Create($"descriptor '__format__' requires a 'float' object but received a '{self.GetTypeName()}'");
                    if (args[0] is not PyString specStr)
                        throw PyTypeError.Create($"__format__() argument 1 must be str, not {args[0].GetTypeName()}");

                    return new PyString(FormatFloat(floatObj.Value, specStr.Value));
                },
                minArgs: 1, maxArgs: 1
            );
        }

        // Helper method for GCD calculation
        private static long GCD(long a, long b)
        {
            while (b != 0)
            {
                long temp = b;
                b = a % b;
                a = temp;
            }
            return a;
        }

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

        // CPython 3.12: Objects/floatobject.c - float_bool
        // Optimized: Direct value check, no MRO traversal
        public override bool IsTrue() => Value != 0.0;

        #endregion

        #region String Representation

        public override PyString ToStr() => new PyString(FormatFloat(Value, ""));

        public override PyString ToRepr() => ToStr();

        #endregion

        #region Hash and Equality

        // Return a signed comparison value, or NaN for an unordered comparison.
        // Only integers exactly representable as double may take the coercion path.
        internal static double CompareToInteger(double value, BigInteger integer)
        {
            long bits = integer.GetBitLength();
            if (bits <= 53)
                return value - (double)integer;
            if (!double.IsFinite(value))
                return value;

            // A matching integer here is too large to have a fractional double.
            // CompareTo(long) avoids a temporary BigInteger backing array.
            if (value >= long.MinValue && value < 9223372036854775808.0)
                return -integer.CompareTo((long)value);
            if (bits > 1024)
                return -integer.Sign;

            // Trailing zero bits do not consume double's 53-bit significand.
            // Exact large integers can still use double without allocating.
            if (BigInteger.TrailingZeroCount(integer) >= bits - 53)
                return value - (double)integer;

            // Outside Int64 every finite double is integral. Preserve all its bits.
            return new BigInteger(value).CompareTo(integer);
        }

        internal long PythonHash()
        {
            if (double.IsNaN(Value)) return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
            if (double.IsInfinity(Value)) return Value > 0 ? 314159 : -314159;
            ulong bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(Value));
            int exponent = (int)((bits >> 52) & 2047);
            ulong mantissa = bits & ((1UL << 52) - 1);
            int shift;
            if (exponent == 0) shift = -1074;
            else { mantissa |= 1UL << 52; shift = exponent - 1023 - 52; }
            int width = IntPtr.Size == 8 ? 61 : 31;
            ulong modulus = (1UL << width) - 1;
            mantissa %= modulus;
            shift = (shift % width + width) % width;
            ulong hash = ((mantissa << shift) & modulus) | (mantissa >> (width - shift));
            long signed = (bits >> 63) == 0 ? (long)hash : -(long)hash;
            return signed == -1 ? -2 : signed;
        }

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

        protected override PyObject PyNotEquals(PyObject other)
        {
            var result = PyEquals(other);
            return result is PyBool equal ? PyBool.FromBool(!equal.Value) : result;
        }

        protected override PyObject PyEquals(PyObject other)
        {
            return other switch
            {
                // CPython 3.12: Objects/floatobject.c:526-550 - float_richcompare
                PyFloat otherFloat => PyBool.FromBool(Value == otherFloat.Value),
                PyInt otherInt => PyBool.FromBool(CompareToInteger(Value, otherInt.Value) == 0),
                PyBool otherBool => PyBool.FromBool(Value == (otherBool.Value ? 1.0 : 0.0)),
                PyIntSubclass otherSubclass => PyBool.FromBool(CompareToInteger(Value, otherSubclass.GetIntValue().Value) == 0),
                _ => PyNotImplemented.Instance
            };
        }

        #endregion

        #region Comparison Operations

        protected override PyObject PyLess(PyObject other)
        {
            return other switch
            {
                // CPython 3.12: Objects/floatobject.c:526-550 - float_richcompare
                PyFloat otherFloat => PyBool.FromBool(Value < otherFloat.Value),
                PyInt otherInt => PyBool.FromBool(CompareToInteger(Value, otherInt.Value) < 0),
                PyBool otherBool => PyBool.FromBool(Value < (otherBool.Value ? 1.0 : 0.0)),
                PyIntSubclass otherSubclass => PyBool.FromBool(CompareToInteger(Value, otherSubclass.GetIntValue().Value) < 0),
                _ => PyNotImplemented.Instance
            };
        }

        protected override PyObject PyLessEqual(PyObject other)
        {
            return other switch
            {
                // CPython 3.12: Objects/floatobject.c:526-550 - float_richcompare
                PyFloat otherFloat => PyBool.FromBool(Value <= otherFloat.Value),
                PyInt otherInt => PyBool.FromBool(CompareToInteger(Value, otherInt.Value) <= 0),
                PyBool otherBool => PyBool.FromBool(Value <= (otherBool.Value ? 1.0 : 0.0)),
                PyIntSubclass otherSubclass => PyBool.FromBool(CompareToInteger(Value, otherSubclass.GetIntValue().Value) <= 0),
                _ => PyNotImplemented.Instance
            };
        }

        protected override PyObject PyGreater(PyObject other)
        {
            return other switch
            {
                // CPython 3.12: Objects/floatobject.c:526-550 - float_richcompare
                PyFloat otherFloat => PyBool.FromBool(Value > otherFloat.Value),
                PyInt otherInt => PyBool.FromBool(CompareToInteger(Value, otherInt.Value) > 0),
                PyBool otherBool => PyBool.FromBool(Value > (otherBool.Value ? 1.0 : 0.0)),
                PyIntSubclass otherSubclass => PyBool.FromBool(CompareToInteger(Value, otherSubclass.GetIntValue().Value) > 0),
                _ => PyNotImplemented.Instance
            };
        }

        protected override PyObject PyGreaterEqual(PyObject other)
        {
            return other switch
            {
                // CPython 3.12: Objects/floatobject.c:526-550 - float_richcompare
                PyFloat otherFloat => PyBool.FromBool(Value >= otherFloat.Value),
                PyInt otherInt => PyBool.FromBool(CompareToInteger(Value, otherInt.Value) >= 0),
                PyBool otherBool => PyBool.FromBool(Value >= (otherBool.Value ? 1.0 : 0.0)),
                PyIntSubclass otherSubclass => PyBool.FromBool(CompareToInteger(Value, otherSubclass.GetIntValue().Value) >= 0),
                _ => PyNotImplemented.Instance
            };
        }

        #endregion

        #region Arithmetic Operations

        public override PyObject Add(PyObject other)
        {
            // CPython 3.12: Objects/floatobject.c (float_add), Objects/complexobject.c (complex_add)
            return other switch
            {
                PyFloat otherFloat => new PyFloat(Value + otherFloat.Value),
                PyInt otherInt => new PyFloat(Value + (double)otherInt.Value),
                PyBool otherBool => new PyFloat(Value + (otherBool.Value ? 1.0 : 0.0)),
                PyComplex otherComplex => new PyComplex(Value + otherComplex.Real, otherComplex.Imag),
                _ => PyNotImplemented.Instance
            };
        }

        public override PyObject Subtract(PyObject other)
        {
            // CPython 3.12: Objects/floatobject.c (float_sub), Objects/complexobject.c (complex_sub)
            return other switch
            {
                PyFloat otherFloat => new PyFloat(Value - otherFloat.Value),
                PyInt otherInt => new PyFloat(Value - (double)otherInt.Value),
                PyBool otherBool => new PyFloat(Value - (otherBool.Value ? 1.0 : 0.0)),
                PyComplex otherComplex => new PyComplex(Value - otherComplex.Real, -otherComplex.Imag),
                _ => PyNotImplemented.Instance
            };
        }

        public override PyObject Multiply(PyObject other)
        {
            // CPython 3.12: Objects/floatobject.c (float_mul), Objects/complexobject.c (complex_mul)
            return other switch
            {
                PyFloat otherFloat => new PyFloat(Value * otherFloat.Value),
                PyInt otherInt => new PyFloat(Value * (double)otherInt.Value),
                PyBool otherBool => new PyFloat(Value * (otherBool.Value ? 1.0 : 0.0)),
                PyComplex otherComplex => new PyComplex(Value * otherComplex.Real, Value * otherComplex.Imag),
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

        /// <summary>
        /// CPython 3.12: _float_div_mod - Helper for floor division and modulo
        /// CPython: Objects/floatobject.c:_float_div_mod (lines 673-709)
        /// Implements Python floor division semantics for floats
        /// </summary>
        private static void FloatDivMod(double vx, double wx, out double floordiv, out double mod)
        {
            // CPython: Objects/floatobject.c:676
            mod = vx % wx;  // fmod in C

            // CPython: Objects/floatobject.c:682-683
            double div = (vx - mod) / wx;

            if (mod != 0.0)
            {
                // CPython: Objects/floatobject.c:685-689
                // ensure the remainder has the same sign as the denominator
                if ((wx < 0) != (mod < 0))
                {
                    mod += wx;
                    div -= 1.0;
                }
            }
            else
            {
                // CPython: Objects/floatobject.c:691-695
                // the remainder is zero, ensure it has the same sign as the denominator
                mod = Math.CopySign(0.0, wx);
            }

            // CPython: Objects/floatobject.c:697-705
            // snap quotient to nearest integral value
            if (div != 0.0)
            {
                floordiv = Math.Floor(div);
                if (div - floordiv > 0.5)
                {
                    floordiv += 1.0;
                }
            }
            else
            {
                // CPython: Objects/floatobject.c:707
                // div is zero - get the same sign as the true quotient
                floordiv = Math.CopySign(0.0, vx / wx);
            }
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

            // CPython: Objects/floatobject.c:float_floor_div (lines 726-738)
            FloatDivMod(Value, otherValue, out double floordiv, out _);
            return new PyFloat(floordiv);
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

            // CPython: Objects/floatobject.c:float_rem (lines 740-752)
            FloatDivMod(Value, otherValue, out _, out double mod);
            return new PyFloat(mod);
        }

        /// <summary>
        /// CPython 3.12: float.__divmod__ - divmod() operation for floats
        /// CPython: Objects/floatobject.c:float_divmod (lines 711-723)
        /// Returns tuple of (quotient, remainder) equivalent to (a // b, a % b)
        /// </summary>
        public override PyObject DivMod(PyObject other)
        {
            double otherValue;
            switch (other)
            {
                case PyFloat otherFloat:
                    otherValue = otherFloat.Value;
                    break;
                case PyInt otherInt:
                    otherValue = (double)otherInt.Value;
                    break;
                case PyBool otherBool:
                    otherValue = otherBool.Value ? 1.0 : 0.0;
                    break;
                default:
                    return PyNotImplemented.Instance;
            }

            if (otherValue == 0.0)
                throw PyZeroDivisionError.Create("float divmod()");

            // CPython: Objects/floatobject.c:721-722
            FloatDivMod(Value, otherValue, out double floordiv, out double mod);
            return new PyTuple(new PyFloat(floordiv), new PyFloat(mod));
        }

        /// <summary>
        /// CPython 3.12: float.__round__ - round() 함수가 호출하는 메서드
        /// CPython: Objects/floatobject.c:float_round (lines 1311-1359)
        /// Returns int if ndigits is None, otherwise float
        /// </summary>
        public PyObject Round(PyObject ndigits = null)
        {
            // CPython: Objects/floatobject.c float_round - ndigits None → int (round-half-even)
            if (ndigits == null || ndigits is PyNone)
            {
                if (double.IsNaN(Value))
                    throw PyValueError.Create("cannot convert float NaN to integer");
                if (double.IsInfinity(Value))
                    throw PyOverflowError.Create("cannot convert float infinity to integer");
                return new PyInt((long)Math.Round(Value, MidpointRounding.ToEven));
            }

            // CPython: ndigits must be an int
            if (ndigits is not PyInt ndigitsInt)
                throw PyTypeError.Create($"'{ndigits.GetTypeName()}' object cannot be interpreted as an integer");

            long nd;
            try
            {
                nd = ndigitsInt.ToLong();
            }
            catch (OverflowException)
            {
                throw PyOverflowError.Create("Python int too large to convert to C long");
            }

            return new PyFloat(CPythonDoubleRound(Value, (int)Math.Clamp(nd, -1000, 1000)));
        }

        /// <summary>
        /// CPython 3.12 Objects/floatobject.c double_round(): round x to the nearest multiple of
        /// 10^-ndigits using round-half-even on the EXACT binary value, then convert the decimal
        /// result back to a double with correct rounding (CPython does dtoa → strtod).
        /// Example: round(2.675, 2) == 2.67 because 2.675 is really 2.674999...944.
        /// Implemented with exact BigInteger arithmetic instead of David Gay's dtoa.
        /// </summary>
        internal static double CPythonDoubleRound(double x, int ndigits)
        {
            if (double.IsNaN(x) || double.IsInfinity(x))
                return x;
            if (ndigits > 400)
                return x; // beyond the exact decimal expansion length of any double: identity

            double ax = Math.Abs(x);
            long bits = BitConverter.DoubleToInt64Bits(ax);
            int biased = (int)((bits >> 52) & 0x7FF);
            long mant = bits & ((1L << 52) - 1);
            BigInteger sig;
            long e2;
            if (biased == 0)
            {
                sig = mant;
                e2 = -1074;
            }
            else
            {
                sig = mant | (1L << 52);
                e2 = biased - 1075L;
            }
            if (sig.IsZero)
                return x; // +-0.0

            // |x| * 10^ndigits = sig * 2^e2 * 10^ndigits as an exact rational N/D
            BigInteger num = sig;
            BigInteger den = BigInteger.One;
            if (ndigits >= 0)
                num *= BigInteger.Pow(10, ndigits);
            else
                den = BigInteger.Pow(10, -ndigits);
            if (e2 >= 0)
                num <<= (int)e2;
            else
                den <<= (int)(-e2);

            BigInteger q = BigInteger.DivRem(num, den, out BigInteger rem);
            int cmp = (rem << 1).CompareTo(den);
            if (cmp > 0 || (cmp == 0 && !q.IsEven))
                q += BigInteger.One;

            bool negative = Math.Sign(x) < 0 || double.IsNegative(x);
            // Mirror CPython's "%s0%se%d" buffer + strtod: parse decimal text with correct rounding.
            string text = (negative ? "-" : "") + q.ToString() + "e" +
                          (-ndigits).ToString(System.Globalization.CultureInfo.InvariantCulture);
            double rounded = double.Parse(text, System.Globalization.NumberStyles.Float,
                                          System.Globalization.CultureInfo.InvariantCulture);
            if (double.IsInfinity(rounded))
                throw PyOverflowError.Create("rounded value too large to represent");
            return rounded;
        }

        /// <summary>
        /// CPython 3.12: float.__trunc__ - math.trunc()가 호출하는 메서드
        /// CPython: Objects/floatobject.c:float_trunc (lines 1269-1280)
        /// Truncates towards zero, returns int
        /// </summary>
        public PyObject Trunc()
        {
            // CPython: Objects/floatobject.c:1277 - Return int(self)
            return new PyInt((long)Math.Truncate(Value));
        }

        /// <summary>
        /// CPython 3.12: float.__floor__ - math.floor()가 호출하는 메서드
        /// CPython: Objects/floatobject.c:float_floor (lines 1282-1293)
        /// Returns the floor as int
        /// </summary>
        public PyObject Floor()
        {
            // CPython: Objects/floatobject.c:1290 - Returns floor as int
            return new PyInt((long)Math.Floor(Value));
        }

        /// <summary>
        /// CPython 3.12: float.__ceil__ - math.ceil()가 호출하는 메서드
        /// CPython: Objects/floatobject.c:float_ceil (lines 1295-1306)
        /// Returns the ceiling as int
        /// </summary>
        public PyObject Ceil()
        {
            // CPython: Objects/floatobject.c:1303 - Returns ceiling as int
            return new PyInt((long)Math.Ceiling(Value));
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

            // CPython: Objects/floatobject.c float_pow - 0.0 base with negative exponent
            if (Value == 0.0 && otherValue < 0.0)
                throw PyZeroDivisionError.Create("0.0 cannot be raised to a negative power");

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
            // CPython 3.12: float.__str__ is float.__repr__ (shortest round-trip form).
            return ToStr().Value;
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
            // CPython 3.12: Objects/floatobject.c float_hex_impl
            // Format: [sign]0x1.<13 hex>p<±exp>; subnormals use 0x0.<13 hex>p-1022
            // CPython: isnan/isinf short-circuit to repr; repr of NaN is always "nan"
            if (double.IsNaN(Value)) return new PyString("nan");
            if (double.IsPositiveInfinity(Value)) return new PyString("inf");
            if (double.IsNegativeInfinity(Value)) return new PyString("-inf");

            long bits = BitConverter.DoubleToInt64Bits(Value);
            bool negative = bits < 0;
            int biased = (int)((bits >> 52) & 0x7FF);
            long mantissa = bits & ((1L << 52) - 1);
            string sign = negative ? "-" : "";

            if (mantissa == 0 && (biased == 0 || biased == 1023))
                return new PyString(biased == 0 ? $"{sign}0x0.0p+0" : $"{sign}0x1.0000000000000p+0");

            if (biased == 0)
                return new PyString($"{sign}0x0.{mantissa:x13}p-1022");
            return new PyString($"{sign}0x1.{mantissa:x13}p{((biased - 1023) >= 0 ? "+" : "")}{biased - 1023}");
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
        /// CPython 3.12: Objects/floatobject.c:1533-1623 - PyFloat_FromDouble
        /// </summary>
        public static PyFloat FromNumber(PyObject obj)
        {
            return obj switch
            {
                PyFloat pyFloat => pyFloat,
                PyInt pyInt => new PyFloat((double)pyInt.Value),
                PyBool pyBool => new PyFloat(pyBool.Value ? 1.0 : 0.0),
                _ => throw PyTypeError.Create($"float() argument must be a string or a real number, not '{obj.GetTypeName()}'")
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

        #region Helper Methods for fromhex

        // CPython 3.12: Objects/floatobject.c - hex_from_char helper
        private static bool IsHexDigit(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }

        private static int HexValue(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return 0;
        }

        #endregion

        #region Format Support

        /// <summary>
        /// CPython 3.12: Objects/floatobject.c - Format a float value according to format_spec
        /// Format spec mini-language: [[fill]align][sign][#][0][width][,][.precision][type]
        /// Type can be: e/E (exponential), f/F (fixed), g/G (general), % (percentage), '' (same as g)
        /// </summary>
        public static string FormatFloat(double value, string formatSpec) => PyFloatFormatter.Format(value, formatSpec);

        #endregion

        #region Special Methods

        /// <summary>
        /// CPython 3.12: __complex__() returns complex(self, 0) for float objects
        /// </summary>
        public override PyObject GetAttribute(string name)
        {
            if (name == "__complex__")
            {
                // Return a bound method that returns complex(self, 0)
                // CPython 3.12: Objects/floatobject.c:1041 - float___complex___impl
                return new PyBuiltinFunction("__complex__", (args) => new PyComplex(Value, 0));
            }
            return base.GetAttribute(name);
        }

        #endregion
    }
}
