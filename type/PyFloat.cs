using System;

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

                    // C# doesn't have a direct equivalent to Python's float.hex()
                    // Simplified implementation using BitConverter
                    long bits = BitConverter.DoubleToInt64Bits(floatObj.Value);
                    return new PyString($"0x{bits:x}");
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
            // CPython: Objects/floatobject.c:1317-1320 - ndigits가 None이면 정수 반환
            if (ndigits == null || ndigits is PyNone)
            {
                // Banker's rounding (round half to even) - CPython 기본 동작
                return new PyInt((long)Math.Round(Value, MidpointRounding.ToEven));
            }

            // CPython: Objects/floatobject.c:1322-1359 - ndigits가 주어지면 float 반환
            if (ndigits is not PyInt ndigitsInt)
                throw PyTypeError.Create("'int' object cannot be interpreted as an integer");

            int digits = (int)ndigitsInt.Value;
            double rounded = Math.Round(Value, digits, MidpointRounding.ToEven);
            return new PyFloat(rounded);
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
        public static string FormatFloat(double value, string formatSpec)
        {
            if (string.IsNullOrEmpty(formatSpec))
                return value.ToString(System.Globalization.CultureInfo.InvariantCulture);

            // Parse format spec
            char fill = ' ';
            char align = '\0';  // '\0' means default
            char sign = '-';    // default: only negative
            bool alternate = false;
            bool zeropad = false;
            int width = 0;
            bool thousands = false;
            int precision = -1;  // -1 means default
            char type = '\0';   // default type

            int i = 0;
            int len = formatSpec.Length;

            // Check for fill + align (fill is any char, align is one of <>^=)
            if (len >= 2 && "<>=^".Contains(formatSpec[1]))
            {
                fill = formatSpec[0];
                align = formatSpec[1];
                i = 2;
            }
            else if (len >= 1 && "<>=^".Contains(formatSpec[0]))
            {
                align = formatSpec[0];
                i = 1;
            }

            // Sign
            if (i < len && "+-".Contains(formatSpec[i]))
            {
                sign = formatSpec[i];
                i++;
            }
            else if (i < len && formatSpec[i] == ' ')
            {
                sign = ' ';
                i++;
            }

            // Alternate form (#)
            if (i < len && formatSpec[i] == '#')
            {
                alternate = true;
                i++;
            }

            // Zero padding (0)
            if (i < len && formatSpec[i] == '0')
            {
                zeropad = true;
                i++;
            }

            // Width
            while (i < len && char.IsDigit(formatSpec[i]))
            {
                width = width * 10 + (formatSpec[i] - '0');
                i++;
            }

            // Grouping option (,)
            if (i < len && formatSpec[i] == ',')
            {
                thousands = true;
                i++;
            }

            // Precision
            if (i < len && formatSpec[i] == '.')
            {
                i++;
                precision = 0;
                while (i < len && char.IsDigit(formatSpec[i]))
                {
                    precision = precision * 10 + (formatSpec[i] - '0');
                    i++;
                }
            }

            // Type
            if (i < len)
            {
                type = formatSpec[i];
                i++;
            }

            // Handle special float values
            if (double.IsNaN(value))
                return FormatSpecialFloat("nan", fill, align, width, sign, false);
            if (double.IsPositiveInfinity(value))
                return FormatSpecialFloat("inf", fill, align, width, sign, false);
            if (double.IsNegativeInfinity(value))
                return FormatSpecialFloat("inf", fill, align, width, '-', true);

            // Convert value based on type
            string result;
            bool isNegative = value < 0;
            double absValue = Math.Abs(value);

            // Default precision
            if (precision < 0)
                precision = (type == 'f' || type == 'F') ? 6 : 6;

            switch (type)
            {
                case 'e':  // exponential lowercase
                    result = absValue.ToString($"e{precision}", System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case 'E':  // exponential uppercase
                    result = absValue.ToString($"E{precision}", System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case 'f':  // fixed-point lowercase
                case 'F':  // fixed-point uppercase (same as f for Python)
                    result = absValue.ToString($"F{precision}", System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case 'g':  // general format lowercase
                    result = FormatGeneral(absValue, precision, false);
                    break;
                case 'G':  // general format uppercase
                    result = FormatGeneral(absValue, precision, true);
                    break;
                case '%':  // percentage
                    result = (absValue * 100).ToString($"F{precision}", System.Globalization.CultureInfo.InvariantCulture) + "%";
                    break;
                case '\0': // default (same as g but without trailing zeros)
                    result = FormatGeneral(absValue, precision >= 0 ? precision : 6, false);
                    break;
                default:
                    throw PyValueError.Create($"Unknown format code '{type}' for object of type 'float'");
            }

            // Apply thousands separator if requested
            if (thousands && (type == 'f' || type == 'F' || type == '\0' || type == 'g' || type == 'G'))
            {
                result = ApplyThousandsSeparator(result);
            }

            // Alternate form: always include decimal point
            if (alternate && !result.Contains('.') && !result.Contains('e') && !result.Contains('E'))
            {
                result += ".";
            }

            // Build sign string
            string signStr = "";
            if (isNegative)
                signStr = "-";
            else if (sign == '+')
                signStr = "+";
            else if (sign == ' ')
                signStr = " ";

            // Apply width and alignment
            string fullPrefix = signStr;
            int totalLen = fullPrefix.Length + result.Length;

            if (width <= totalLen)
                return fullPrefix + result;

            int padLen = width - totalLen;

            // Default alignment for numbers is right-aligned
            if (align == '\0')
                align = zeropad ? '=' : '>';

            // Apply padding
            if (zeropad && align == '=')
                fill = '0';

            switch (align)
            {
                case '<':  // left-aligned
                    return fullPrefix + result + new string(fill, padLen);
                case '>':  // right-aligned
                    return new string(fill, padLen) + fullPrefix + result;
                case '=':  // pad after sign
                    return signStr + new string(fill, padLen) + result;
                case '^':  // centered
                    int leftPad = padLen / 2;
                    int rightPad = padLen - leftPad;
                    return new string(fill, leftPad) + fullPrefix + result + new string(fill, rightPad);
                default:
                    return fullPrefix + result;
            }
        }

        private static string FormatSpecialFloat(string value, char fill, char align, int width, char sign, bool isNegative)
        {
            string signStr = isNegative ? "-" : (sign == '+' ? "+" : (sign == ' ' ? " " : ""));
            string result = signStr + value;

            if (width <= result.Length)
                return result;

            int padLen = width - result.Length;
            if (align == '\0') align = '>';

            switch (align)
            {
                case '<': return result + new string(fill, padLen);
                case '>': return new string(fill, padLen) + result;
                case '^':
                    int leftPad = padLen / 2;
                    return new string(fill, leftPad) + result + new string(fill, padLen - leftPad);
                default: return result;
            }
        }

        private static string FormatGeneral(double value, int precision, bool uppercase)
        {
            // CPython's general format: uses exponential if exponent < -4 or >= precision
            if (value == 0)
                return "0";

            int exponent = (int)Math.Floor(Math.Log10(Math.Abs(value)));

            if (exponent < -4 || exponent >= precision)
            {
                // Use exponential format
                string format = uppercase ? $"E{precision - 1}" : $"e{precision - 1}";
                return value.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                // Use fixed format, then strip trailing zeros
                int decimalPlaces = precision - exponent - 1;
                if (decimalPlaces < 0) decimalPlaces = 0;
                string result = value.ToString($"F{decimalPlaces}", System.Globalization.CultureInfo.InvariantCulture);
                // Strip trailing zeros after decimal point
                if (result.Contains('.'))
                {
                    result = result.TrimEnd('0').TrimEnd('.');
                }
                return result;
            }
        }

        private static string ApplyThousandsSeparator(string value)
        {
            int dotIndex = value.IndexOf('.');
            string intPart = dotIndex >= 0 ? value.Substring(0, dotIndex) : value;
            string decPart = dotIndex >= 0 ? value.Substring(dotIndex) : "";

            // Add commas to integer part
            var chars = new System.Collections.Generic.List<char>();
            int count = 0;
            for (int j = intPart.Length - 1; j >= 0; j--)
            {
                if (count > 0 && count % 3 == 0 && char.IsDigit(intPart[j]))
                    chars.Insert(0, ',');
                chars.Insert(0, intPart[j]);
                if (char.IsDigit(intPart[j]))
                    count++;
            }

            return new string(chars.ToArray()) + decPart;
        }

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