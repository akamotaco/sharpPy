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
        /// <summary>
        /// CPython PyFloat_Check semantics for builtin float descriptors: subclass instances pass
        /// the self test and ob_fval is read directly (Objects/floatobject.c never demands an exact
        /// PyFloat_Type). CLR float subclasses are a separate runtime class, so every exact
        /// `self is PyFloat` gate has to route here.
        /// </summary>
        internal static bool TryFloatSelf(PyObject self, out PyFloat value)
        {
            value = FloatValue(self);
            return value != null;
        }

        /// <summary>PyFloat_Check, and the exact float of the value (a subclass instance's ob_fval).</summary>
        internal static PyFloat FloatValue(PyObject value) => value switch
        {
            PyFloatSubclass subclass => subclass.GetFloatValue(),
            PyFloat exact => exact,
            _ => null,
        };

        // Objects/descrobject.c descr_check wording, shared by float's methods and getsets.
        internal static System.Exception WrongFloatSelf(string name, PyObject self) =>
            PyTypeError.Create(
                $"descriptor '{name}' for 'float' objects doesn't apply to a '{self.GetTypeName()}' object");

        /// <summary>
        /// float_new / float_subtype_new, one implementation for both entry points: the
        /// <c>float.__new__</c> slot below and the <c>float(...)</c> call path in PyType.Call.
        /// CPython likewise has a single parser for both - float_new_impl, reached from
        /// float_vectorcall too (Objects/floatobject.c:1698).
        /// </summary>
        internal static PyObject FloatNew(PyObject cls, PyObject[] valueArgs, PyDict kwargs)
        {
            // float_new: only a subclass with an __init__ of its own may take keywords.
            bool hasOwnInit = cls is PyType type && !ReferenceEquals(type, PyType.FloatType) && !PyType.TypeInitIsObjectInit(type);
            if (kwargs != null && kwargs.Length() != 0 && !hasOwnInit)
                throw PyTypeError.Create("float() takes no keyword arguments");
            if (valueArgs.Length > 1)
                throw PyTypeError.Create($"float expected at most 1 argument, got {valueArgs.Length}");

            var value = valueArgs.Length == 0 ? null : valueArgs[0];
            PyFloat result = value == null ? new PyFloat(0.0)
                : value.IsExactly(PyType.StrType) ? FromObject(value) : NumberFloat(value);
            if (ReferenceEquals(cls, PyType.FloatType))
                return result;
            if (!(cls is PyClass subtype))
                throw PyTypeError.Create($"float.__new__(X): X is not a type object ({cls.GetTypeName()})");
            return new PyFloatSubclass(subtype, result);
        }

        // The check PyNumber_Float makes on what nb_float gave.
        internal static PyFloat CheckFloatResult(PyObject source, PyObject result)
        {
            if (result is PyFloat { IsExact: true } exact)
                return exact;
            if (result is not PyFloatSubclass subclass)
                throw PyTypeError.Create($"{source.GetPyType().Name}.__float__ returned non-float (type {result.GetPyType().Name})");
            PyWarnings.Deprecated($"{source.GetPyType().Name}.__float__ returned non-float (type {result.GetPyType().Name}).  " +
                "The ability to return an instance of a strict subclass of float is deprecated, " +
                "and may be removed in a future version of Python.");
            return new PyFloat(subclass.Value);
        }

        /// <summary>
        /// PyNumber_Float: an exact float is itself; then nb_float, whose answer must be a float;
        /// nb_index through PyLong_AsDouble; the value of a float subclass; PyFloat_FromString.
        /// </summary>
        internal static PyFloat NumberFloat(PyObject value)
        {
            if (value.IsExactly(PyType.FloatType))
                return (PyFloat)value;
            var result = value.TpNbFloat();
            if (result != null)
                return CheckFloatResult(value, result);
            if (value.HasTypeSlot("__index__"))
                return (PyFloat)PyObject.IndexOrThrow(value).NbFloat();
            if (value is PyFloat real)
                return new PyFloat(real.Value);
            return FromObject(value);
        }

        public static void InitializeFloatDescriptors()
        {
            var floatType = PyType.FloatType;

            // CPython 3.12: Objects/floatobject.c float_round (float.__round__) and float_hash
            // (the __hash__ slot wrapper). Without them `hasattr(float, '__round__')` and
            // `hasattr(float, '__hash__')` are False, builtin round() refuses float subclasses, and
            // hash(FSub(2.5)) falls back to identity so dict/set identity with plain floats breaks.
            floatType.TypeDict["__round__"] = new PyMethodDescriptor(
                "__round__", floatType,
                (self, args, kwargs) => {
                    if (!PyFloat.TryFloatSelf(self, out var roundTarget))
                        throw PyTypeError.Create($"descriptor '__round__' requires a 'float' object but received a '{self.GetTypeName()}'");
                    if (kwargs != null && kwargs.Length() != 0)
                        throw PyTypeError.Create("float.__round__() takes no keyword arguments");
                    if (args.Length > 1)
                        throw PyTypeError.Create(
                            $"__round__ expected at most 1 argument, got {args.Length}");

                    // CPython float_round resolves ndigits with PyNumber_Index, so an __index__
                    // object is valid (measured: round(2.675, Q with __index__ -> 2.67). A positive
                    // ndigits beyond the representable digit count leaves the value unchanged
                    // (measured: round(1.5, 10**30) == 1.5).
                    PyObject places = args.Length > 0 ? args[0] : null;
                    if (places != null && places is not PyNone)
                    {
                        var index = PyObject.TryGetIndex(places);
                        if (index == null)
                            throw PyTypeError.Create(
                                $"'{places.GetTypeName()}' object cannot be interpreted as an integer");
                        if (index.Value > (System.Numerics.BigInteger)int.MaxValue)
                            return new PyFloat(roundTarget.Value);
                        places = index;
                    }
                    return roundTarget.Round(places);
                },
                minArgs: 0, maxArgs: 1);

            // CPython publishes the numeric slots of float as slot wrappers (Objects/float.c
            // float_as_number, referenced through Objects/slotdefs.c nb_reflected), so
            //   1 + FSub(2.5)  resolves float.__radd__(FSub(2.5), 1) -> 3.5
            //   abs(FSub(2.5)) resolves float.__abs__                -> 2.5
            // None of these were registered: the VM only had hard-coded CLR paths for an exact
            // PyFloat, so a float subclass answered TypeError even though it carries a float value.
            // `self` is read through TryFloatSelf, which is the CLR counterpart of nb_reflected
            // reading ob_fval: the subclass contributes its own value, the result stays a plain float.
            void regFloatRev(string name, Func<PyObject, PyObject, PyObject> fwd)
            {
                floatType.TypeDict[name] = new PyWrapperDescriptor(name, floatType, (self, args, kwargs) =>
                {
                    if (!PyFloat.TryFloatSelf(self, out var selfFloat))
                        throw PyTypeError.Create($"descriptor '{name}' requires a 'float' object but received a '{self.GetTypeName()}'");
                    if (kwargs != null && kwargs.Length() != 0)
                        throw PyTypeError.Create($"wrapper {name}() takes no keyword arguments");
                    if (args.Length != 1)
                        throw PyTypeError.Create($"expected 1 argument, got {args.Length}");
                    return fwd(args[0], selfFloat);
                });
            }

            void regFloatUn(string name, Func<PyFloat, PyObject> impl)
            {
                floatType.TypeDict[name] = new PyWrapperDescriptor(name, floatType, (self, args, kwargs) =>
                {
                    if (!PyFloat.TryFloatSelf(self, out var selfFloat))
                        throw PyTypeError.Create($"descriptor '{name}' requires a 'float' object but received a '{self.GetTypeName()}'");
                    if (kwargs != null && kwargs.Length() != 0)
                        throw PyTypeError.Create($"wrapper {name}() takes no keyword arguments");
                    if (args.Length != 0)
                        throw PyTypeError.Create($"expected 0 arguments, got {args.Length}");
                    return impl(selfFloat);
                });
            }

            regFloatRev("__radd__", (o, s) => o.Add(s));
            regFloatRev("__rsub__", (o, s) => o.Subtract(s));
            regFloatRev("__rmul__", (o, s) => o.Multiply(s));
            regFloatRev("__rtruediv__", (o, s) => o.Divide(s));
            regFloatRev("__rfloordiv__", (o, s) => o.FloorDivide(s));
            regFloatRev("__rmod__", (o, s) => o.Modulo(s));
            regFloatRev("__rpow__", (o, s) => o.Power(s));
            regFloatRev("__rdivmod__", (o, s) => DivModValues(o, s));
            regFloatUn("__abs__", s => new PyFloat(Math.Abs(s.Value)));
            regFloatUn("__neg__", s => new PyFloat(-s.Value));
            regFloatUn("__pos__", s => s);
            regFloatUn("__floor__", s => FromDouble(Math.Floor(s.Value)));
            regFloatUn("__trunc__", s => FromDouble(Math.Truncate(s.Value)));
            regFloatUn("__ceil__", s => FromDouble(Math.Ceiling(s.Value)));
            // Forward binary slots. CPython lists these on float as slot wrappers and SharpPy had
            // none, so a float subclass reached through a dunder path found nothing at all; only
            // the hard-coded exact-PyFloat CLR path worked.
            void regFloatBin(string name, Func<PyObject, PyObject, PyObject> impl)
            {
                floatType.TypeDict[name] = new PyWrapperDescriptor(name, floatType, (self, args, kwargs) =>
                {
                    if (!PyFloat.TryFloatSelf(self, out var selfFloat))
                        throw PyTypeError.Create($"descriptor '{name}' requires a 'float' object but received a '{self.GetTypeName()}'");
                    if (kwargs != null && kwargs.Length() != 0)
                        throw PyTypeError.Create($"wrapper {name}() takes no keyword arguments");
                    if (args.Length != 1)
                        throw PyTypeError.Create($"expected 1 argument, got {args.Length}");
                    return impl(selfFloat, args[0]);
                });
            }

            // CPython publishes float.__int__ and float.__float__ too. __float__ returns the object
            // itself, not a fresh float, so it needs the raw self rather than the payload.
            regFloatUn("__int__", s => s.NbInt());
            floatType.TypeDict["__float__"] = new PyWrapperDescriptor("__float__", floatType,
                (self, args, kwargs) =>
                {
                    if (!PyFloat.TryFloatSelf(self, out _))
                        throw PyTypeError.Create(
                            $"descriptor '__float__' requires a 'float' object but received a '{self.GetTypeName()}'");
                    if (kwargs != null && kwargs.Length() != 0)
                        throw PyTypeError.Create("wrapper __float__() takes no keyword arguments");
                    if (args.Length != 0)
                        throw PyTypeError.Create($"expected 0 arguments, got {args.Length}");
                    // float_float: an exact float is itself, a subclass instance its exact value.
                    return FloatValue(self);
                });

            regFloatBin("__add__", (s, o) => s.Add(o));
            regFloatBin("__sub__", (s, o) => s.Subtract(o));
            regFloatBin("__mul__", (s, o) => s.Multiply(o));
            regFloatBin("__truediv__", (s, o) => s.Divide(o));
            regFloatBin("__floordiv__", (s, o) => s.FloorDivide(o));
            regFloatBin("__mod__", (s, o) => s.Modulo(o));
            regFloatBin("__pow__", (s, o) => s.Power(o));
            regFloatRev("__divmod__", (o, s) => DivModValues(s, o));

            floatType.TypeDict["__hash__"] = new PyWrapperDescriptor(
                "__hash__", floatType,
                (self, args, kwargs) => {
                    if (!PyFloat.TryFloatSelf(self, out var hashTarget))
                        throw PyTypeError.Create($"descriptor '__hash__' requires a 'float' object but received a '{self.GetTypeName()}'");
                    if (kwargs != null && kwargs.Length() != 0)
                        throw PyTypeError.Create("wrapper __hash__() takes no keyword arguments");
                    if (args.Length != 0)
                        throw PyTypeError.Create($"expected 0 arguments, got {args.Length}");
                    // CPython float_hash keeps the invariant hash(2.0) == hash(2).
                    return PyInt.From(double.IsNaN(hashTarget.Value) ? self.ToHash() : hashTarget.PythonHash());
                });

            // CPython 3.12: Objects/floatobject.c:1420-1430 - float_is_integer
            // Return True if the float is an integer.
            floatType.TypeDict["is_integer"] = new PyMethodDescriptor(
                "is_integer", floatType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"float.is_integer() takes no arguments ({args.Length} given)");
                    if (!PyFloat.TryFloatSelf(self, out var floatObj))
                        throw PyTypeError.Create($"descriptor 'is_integer' requires a 'float' object but received a '{self.GetTypeName()}'");

                    double value = floatObj.Value;
                    if (double.IsInfinity(value) || double.IsNaN(value))
                        return PyBool.False;

                    return PyBool.FromBool(value == Math.Floor(value));
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython: Objects/floatobject.c:1644 float.__new__ (clinic: x: object = 0, /
            // - positional only), arity checked by _PyArg_CheckPositional("float", nargs, 0, 1) at
            // Objects/floatobject.c:1705, and a subtype routed to float_subtype_new
            // (Objects/floatobject.c:1677) which converts first and then allocates the subtype.
            //
            // This slot was missing, and the absence was observable in two ways: PyClass had to
            // special-case float subclasses in CreateInstanceCore, and super().__new__(cls, v) in a
            // user float subclass resolved to object.__new__ and raised
            // "object.__new__() takes exactly one argument (the type to instantiate)" where CPython
            // returns the converted value.
            floatType.TypeDict["__new__"] = PyStaticBuiltinMethod.ForNew(floatType,
                (args, kwargs) => FloatNew(
                    args.Length > 0 ? args[0] : floatType,
                    args.Length > 1 ? args[1..] : System.Array.Empty<PyObject>(),
                    kwargs));

            // CPython 3.12: Objects/floatobject.c:1432-1470 - float_as_integer_ratio
            // Return a pair of integers, whose ratio is exactly equal to the original float.
            floatType.TypeDict["as_integer_ratio"] = new PyMethodDescriptor(
                "as_integer_ratio", floatType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"float.as_integer_ratio() takes no arguments ({args.Length} given)");
                    if (!PyFloat.TryFloatSelf(self, out var floatObj))
                        throw PyTypeError.Create($"descriptor 'as_integer_ratio' requires a 'float' object but received a '{self.GetTypeName()}'");

                    double value = floatObj.Value;

                    if (double.IsInfinity(value))
                        throw PyOverflowError.Create("cannot convert Infinity to integer ratio");
                    if (double.IsNaN(value))
                        throw PyValueError.Create("cannot convert NaN to integer ratio");

                    // float_as_integer_ratio: the exact value of the double, from its bits.
                    long bits = BitConverter.DoubleToInt64Bits(value);
                    int exponent = (int)((bits >> 52) & 0x7FF);
                    long fraction = bits & 0xFFFFFFFFFFFFFL;
                    if (exponent == 0) exponent++;
                    else fraction |= 1L << 52;
                    exponent -= 1075;
                    if (fraction == 0) return PyTuple.From(PyInt.From(0), PyInt.From(1));
                    int zeros = System.Numerics.BitOperations.TrailingZeroCount(fraction);
                    fraction >>= zeros;
                    exponent += zeros;
                    var numerator = new System.Numerics.BigInteger(value < 0 ? -fraction : fraction);
                    var denominator = System.Numerics.BigInteger.One;
                    if (exponent > 0) numerator <<= exponent;
                    else denominator <<= -exponent;
                    return PyTuple.From(PyInt.From(numerator), PyInt.From(denominator));
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/floatobject.c:1545-1600 - float_hex
            // Return a hexadecimal representation of a floating-point number.
            floatType.TypeDict["hex"] = new PyMethodDescriptor(
                "hex", floatType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"float.hex() takes no arguments ({args.Length} given)");
                    if (!PyFloat.TryFloatSelf(self, out var floatObj))
                        throw PyTypeError.Create($"descriptor 'hex' requires a 'float' object but received a '{self.GetTypeName()}'");

                    // CPython 3.12: Objects/floatobject.c float_hex_impl
                    return floatObj.Hex();
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/floatobject.c:1311-1549 float_fromhex.
            // The conversion is *correctly rounded*: CPython extracts the mantissa bits and applies
            // round-half-even, so 0x1.00000000000008p+0 is 1.0 while 0x1.00000000000018p+0 is
            // 1.0000000000000004. The previous accumulate-then-ScaleB version got those wrong,
            // returned 0.0 for every coefficient without a point (the two accumulation loops
            // cancelled each other), accepted trailing junk ("0x1p3x"), and treated NBSP as
            // whitespace.
            var fromhexMethod = new PyMethodDescriptor(
                "fromhex", floatType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create(
                            $"float.fromhex() takes exactly one argument ({args.Length} given)");
                    if (args[0] is not PyString hexText)
                        throw PyTypeError.Create(
                            $"fromhex() argument must be a string, not '{args[0].GetTypeName()}'");

                    double value = ParseHexFloat(hexText.Value);
                    // float_fromhex rebuilds through `type` when the classmethod reached a subclass.
                    if (self is PyType cls && !ReferenceEquals(cls, floatType))
                        return cls.Call(new PyObject[] { new PyFloat(value) });
                    return new PyFloat(value);
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
                        throw PyTypeError.Create(
                            $"float.conjugate() takes no arguments ({args.Length} given)");
                    if (!PyFloat.TryFloatSelf(self, out var floatObj))
                        throw PyFloat.WrongFloatSelf("conjugate", self);

                    // float_conjugate IS float_real in floatobject.c, and float_real builds a new
                    // PyFloat from the double - it never hands back the receiver, so a float subclass
                    // loses its type and an exact float comes back as a different object.
                    return new PyFloat(floatObj.Value);
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/floatobject.c - float.real property
            // Return the real part of the float (which is the float itself).
            floatType.TypeDict["real"] = new PyGetSetDescriptor(
                "real", floatType,
                getter: self => {
                    if (!PyFloat.TryFloatSelf(self, out var floatValue))
                        throw PyFloat.WrongFloatSelf("real", self);
                    // float_getreal: PyFloat_FromDouble(ob_fval), so type(F(2.5).real) is float.
                    return new PyFloat(floatValue.Value);
                }
            );

            // CPython 3.12: Objects/floatobject.c - float.imag property
            // Return the imaginary part of the float (which is 0.0).
            floatType.TypeDict["imag"] = new PyGetSetDescriptor(
                "imag", floatType,
                getter: self => {
                    if (!PyFloat.TryFloatSelf(self, out _))
                        throw PyFloat.WrongFloatSelf("imag", self);
                    return new PyFloat(0.0);  // float_getimag
                }
            );

            // CPython Objects/floatobject.c:1714-1723 float.__getnewargs__ -> (double,)
            floatType.TypeDict["__getnewargs__"] = new PyMethodDescriptor(
                "__getnewargs__", floatType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create(
                            $"float.__getnewargs__() takes no arguments ({args.Length} given)");
                    if (!PyFloat.TryFloatSelf(self, out var floatValue))
                        throw PyFloat.WrongFloatSelf("__getnewargs__", self);
                    return PyTuple.From(new PyFloat(floatValue.Value));
                },
                minArgs: 0, maxArgs: 0);

            // CPython 3.12: Objects/floatobject.c:1710-1750 - float_format
            // float.__format__(format_spec) - Format the float according to format_spec
            // float.__repr__(/) - slot wrapper usable unbound: float.__repr__(1.5)
            floatType.TypeDict["__repr__"] = new PyMethodDescriptor(
                "__repr__", floatType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"expected 0 arguments, got {args.Length}");
                    if (!PyFloat.TryFloatSelf(self, out var floatObj))
                        throw PyTypeError.Create($"descriptor '__repr__' requires a 'float' object but received a '{self.GetTypeName()}'");
                    return PyString.From(FormatFloat(floatObj.Value, ""));
                },
                minArgs: 1, maxArgs: 1
            );

            floatType.TypeDict["__format__"] = new PyMethodDescriptor(
                "__format__", floatType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"__format__() takes 1 positional argument ({args.Length} given)");
                    if (!PyFloat.TryFloatSelf(self, out var floatObj))
                        throw PyTypeError.Create($"descriptor '__format__' requires a 'float' object but received a '{self.GetTypeName()}'");
                    if (args[0] is not PyString specStr)
                        throw PyTypeError.Create($"__format__() argument 1 must be str, not {args[0].GetTypeName()}");

                    // _PyFloat_FormatAdvancedWriter: an empty spec is str(self).
                    return specStr.Value.Length == 0 ? self.ToStr() : PyString.From(FormatFloat(floatObj.Value, specStr.Value));
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

        public override PyType GetPyType() => _customType ?? PyType.FloatType;

        // PyX_CheckExact
        internal bool IsExact => IsExactly(PyType.FloatType);
        public override string GetTypeName() => _customType?.Name ?? "float";

        // CPython 3.12: Objects/floatobject.c - float_bool
        // Optimized: Direct value check, no MRO traversal
        protected internal override bool Bool() => Value != 0.0;

        #endregion

        #region String Representation

        protected internal override PyString Str() => PyString.From(FormatFloat(Value, ""));

        protected internal override PyString Repr() => AsString();

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

        // float_hash: _Py_HashDouble, by identity for a NaN.
        protected internal override long Hash() => PythonHash();

        public override int ToHash()
        {
            // Python과 같은 방식으로 해시 계산
            // _Py_HashDouble: a NaN hashes by identity.
            if (double.IsNaN(Value))
                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
            if (double.IsInfinity(Value))
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
                PyInt otherInt => new PyFloat(Value + Modules.PyMathFunction.RealNumber(otherInt)),
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
                PyInt otherInt => new PyFloat(Value - Modules.PyMathFunction.RealNumber(otherInt)),
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
                PyInt otherInt => new PyFloat(Value * Modules.PyMathFunction.RealNumber(otherInt)),
                PyBool otherBool => new PyFloat(Value * (otherBool.Value ? 1.0 : 0.0)),
                PyComplex otherComplex => new PyComplex(Value * otherComplex.Real, Value * otherComplex.Imag),
                _ => PyNotImplemented.Instance
            };
        }

        public override PyObject Divide(PyObject other)
        {
            if (!TryConvertOperand(other, out double otherValue))
                return PyNotImplemented.Instance;

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
            if (!TryConvertOperand(other, out double otherValue))
                return PyNotImplemented.Instance;

            if (otherValue == 0.0)
                throw PyZeroDivisionError.Create("float floor division by zero");

            // CPython: Objects/floatobject.c:float_floor_div (lines 726-738)
            FloatDivMod(Value, otherValue, out double floordiv, out _);
            return new PyFloat(floordiv);
        }

        public override PyObject Modulo(PyObject other)
        {
            if (!TryConvertOperand(other, out double otherValue))
                return PyNotImplemented.Instance;

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
        public override PyObject DivMod(PyObject other) => DivModValues(this, other);

        /// <summary>
        /// CPython float_divmod with both operands read as numbers. Shared by __divmod__ and
        /// __rdivmod__ because the reflected form is the same arithmetic with the operands the other
        /// way round: measured divmod(2, FSub(2.5)) is (0.0, 2.0). Going through
        /// other.DivMod(self) instead could not work, since int's divmod answers NotImplemented for
        /// float operands and there was nothing left to try.
        /// Operands are read through TryFloatSelf/TryLongSelf, so a numeric subclass contributes its
        /// own value while the result stays a plain float pair.
        /// </summary>
        internal static PyObject DivModValues(PyObject left, PyObject right)
        {
            double leftValue;
            if (PyFloat.TryFloatSelf(left, out var leftFloat))
                leftValue = leftFloat.Value;
            else if (PyInt.TryLongSelf(left, out var leftInt))
                leftValue = Modules.PyMathFunction.RealNumber(leftInt);
            else if (left is PyBool leftBool)
                leftValue = leftBool.Value ? 1.0 : 0.0;
            else
                return PyNotImplemented.Instance;

            double rightValue;
            if (PyFloat.TryFloatSelf(right, out var rightFloat))
                rightValue = rightFloat.Value;
            else if (PyInt.TryLongSelf(right, out var rightInt))
                rightValue = Modules.PyMathFunction.RealNumber(rightInt);
            else if (right is PyBool rightBool)
                rightValue = rightBool.Value ? 1.0 : 0.0;
            else
                return PyNotImplemented.Instance;

            if (rightValue == 0.0)
                throw PyZeroDivisionError.Create("float divmod()");

            // CPython: Objects/floatobject.c:721-722
            FloatDivMod(leftValue, rightValue, out double floordiv, out double mod);
            return PyTuple.From(new PyFloat(floordiv), new PyFloat(mod));
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
                return FromDouble(Math.Round(Value, MidpointRounding.ToEven));
            }

            // PyNumber_AsSsize_t(o_ndigits, NULL): a huge value clamps, it does not raise.
            var places = TryGetIndex(ndigits) ?? throw PyTypeError.Create(
                $"'{ndigits.GetTypeName()}' object cannot be interpreted as an integer");
            long nd = (long)System.Numerics.BigInteger.Clamp(places.Value, -1000, 1000);

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
        /// CPython 3.12: float.__floor__ - math.floor()가 호출하는 메서드
        /// CPython: Objects/floatobject.c:float_floor (lines 1282-1293)
        /// Returns the floor as int
        /// </summary>
        // PyLong_FromDouble
        private static PyInt FromDouble(double value)
        {
            if (double.IsNaN(value))
                throw PyValueError.Create("cannot convert float NaN to integer");
            if (double.IsInfinity(value))
                throw PyOverflowError.Create("cannot convert float infinity to integer");
            return PyInt.From(new System.Numerics.BigInteger(value));
        }

        public PyObject Floor()
        {
            // CPython: Objects/floatobject.c:1290 - Returns floor as int
            return FromDouble(Math.Floor(Value));
        }

        private static bool IsOddInteger(double value) => Math.Abs(value) % 2.0 == 1.0;

        // float_pow
        public override PyObject Power(PyObject other)
        {
            if (!TryConvertOperand(other, out double exponent))
                return PyNotImplemented.Instance;
            double value = Value;
            if (exponent == 0) return new PyFloat(1.0);
            if (double.IsNaN(value)) return new PyFloat(value);
            if (double.IsNaN(exponent)) return new PyFloat(value == 1.0 ? 1.0 : exponent);
            if (double.IsInfinity(exponent))
            {
                value = Math.Abs(value);
                if (value == 1.0) return new PyFloat(1.0);
                return new PyFloat((exponent > 0.0) == (value > 1.0) ? Math.Abs(exponent) : 0.0);
            }
            if (double.IsInfinity(value))
            {
                bool odd = IsOddInteger(exponent);
                if (exponent > 0.0) return new PyFloat(odd ? value : Math.Abs(value));
                return new PyFloat(odd ? Math.CopySign(0.0, value) : 0.0);
            }
            if (value == 0.0)
            {
                if (exponent < 0.0)
                    throw PyZeroDivisionError.Create("0.0 cannot be raised to a negative power");
                return new PyFloat(IsOddInteger(exponent) ? value : 0.0);
            }
            bool negate = false;
            if (value < 0.0)
            {
                if (exponent != Math.Floor(exponent))
                    return new PyComplex(value, 0).Power(new PyComplex(exponent, 0));
                value = -value;
                negate = IsOddInteger(exponent);
            }
            if (value == 1.0) return new PyFloat(negate ? -1.0 : 1.0);
            double result = Math.Pow(value, exponent);
            if (double.IsInfinity(result))
                throw new PythonException(new PyOverflowError(new PyObject[] { PyInt.From(34), PyString.From("Result too large") }, null));
            return new PyFloat(negate ? -result : result);
        }

        // CONVERT_TO_DOUBLE
        private static bool TryConvertOperand(PyObject operand, out double value)
        {
            value = 0.0;
            switch (operand)
            {
                case PyFloat exact: value = exact.Value; return true;
                case PyInt or PyBool or PyIntSubclass: value = ((PyFloat)PyObject.TryGetIndex(operand).NbFloat()).Value; return true;
                default: return false;
            }
        }

        #endregion

        #region Unary Operations

        protected internal override PyObject NbNegative()
        {
            return new PyFloat(-Value);
        }

        protected internal override PyObject NbPositive()
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
        
        // === As* Methods: Type Conversion (PyFloat → PyObject types) ===
        
        /// <summary>
        /// CPython 호환: PyFloat를 PyFloat로 변환 (자기 자신 반환)
        /// </summary>
        // float_abs
        protected internal override PyObject NbAbsolute() => new PyFloat(Math.Abs(Value));

        // float_float: the float itself, a subclass's value as a float.
        protected internal override PyObject NbFloat() => IsExactly(PyType.FloatType) ? this : new PyFloat(Value);
        
        /// <summary>
        /// CPython 호환: PyFloat를 PyInt로 변환
        /// </summary>
        // float___trunc___impl, nb_int: arbitrary precision, toward zero.
        protected internal override PyObject NbInt() => PyInt.From(PyInt.FloatToIntegral(Value));
        
        /// <summary>
        /// CPython 호환: PyFloat를 PyBool로 변환
        /// </summary>
        public override PyBool AsBool()
        {
            return PyBool.FromBool(IsTrue());
        }
        
        /// <summary>
        /// CPython 호환: PyFloat를 PyString으로 변환
        /// </summary>
        public override string ToClrString()
        {
            // CPython 3.12: float.__str__ is float.__repr__ (shortest round-trip form).
            return AsString().Value;
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
        /// float.is_nan() - NaN인지 검사
        /// </summary>
        public PyBool IsNaN()
        {
            return PyBool.FromBool(double.IsNaN(Value));
        }

        /// <summary>
        /// float.hex() - 16진수 문자열로 변환
        /// </summary>
        public PyString Hex()
        {
            // CPython 3.12: Objects/floatobject.c float_hex_impl
            // Format: [sign]0x1.<13 hex>p<±exp>; subnormals use 0x0.<13 hex>p-1022
            // CPython: isnan/isinf short-circuit to repr; repr of NaN is always "nan"
            if (double.IsNaN(Value)) return PyString.From("nan");
            if (double.IsPositiveInfinity(Value)) return PyString.From("inf");
            if (double.IsNegativeInfinity(Value)) return PyString.From("-inf");

            long bits = BitConverter.DoubleToInt64Bits(Value);
            bool negative = bits < 0;
            int biased = (int)((bits >> 52) & 0x7FF);
            long mantissa = bits & ((1L << 52) - 1);
            string sign = negative ? "-" : "";

            if (mantissa == 0 && (biased == 0 || biased == 1023))
                return PyString.From(biased == 0 ? $"{sign}0x0.0p+0" : $"{sign}0x1.0000000000000p+0");

            if (biased == 0)
                return PyString.From($"{sign}0x0.{mantissa:x13}p-1022");
            return PyString.From($"{sign}0x1.{mantissa:x13}p{((biased - 1023) >= 0 ? "+" : "")}{biased - 1023}");
        }

        #endregion

        #region Helper Methods

        #endregion

        #region Static Factory Methods

        /// <summary>A quiet NaN with the sign clear: the CLR's double.NaN has it set.</summary>
        internal static readonly double QuietNaN = BitConverter.Int64BitsToDouble(0x7FF8000000000000);

        // _PyUnicode_TransformDecimalAndSpaceToASCII
        internal static string TransformDecimalAndSpace(string text)
        {
            bool ascii = true;
            foreach (char c in text)
                if (c >= 127) { ascii = false; break; }
            if (ascii) return text;
            var output = new System.Text.StringBuilder(text.Length);
            foreach (var rune in text.EnumerateRunes())
            {
                if (rune.Value < 127) output.Append((char)rune.Value);
                else if (System.Text.Rune.IsWhiteSpace(rune)) output.Append(' ');
                else
                {
                    int digit = (int)System.Text.Rune.GetNumericValue(rune);
                    if (System.Text.Rune.GetUnicodeCategory(rune) != System.Globalization.UnicodeCategory.DecimalDigitNumber)
                    {
                        output.Append('?');
                        break;
                    }
                    output.Append((char)('0' + digit));
                }
            }
            return output.ToString();
        }

        internal static bool IsCSpace(char c) => c == ' ' || (c >= 9 && c <= 13);

        // _Py_string_to_number_with_underscores
        internal static string WithoutUnderscores(string text)
        {
            if (text.IndexOf('_') < 0) return text;
            var output = new System.Text.StringBuilder(text.Length);
            char previous = '\0';
            foreach (char c in text)
            {
                if (c == '_')
                {
                    if (!char.IsAsciiDigit(previous)) return null;
                }
                else
                {
                    output.Append(c);
                    if (previous == '_' && !char.IsAsciiDigit(c)) return null;
                }
                previous = c;
            }
            return previous == '_' ? null : output.ToString();
        }

        private static bool MatchesAt(string text, int position, string word) =>
            string.Compare(text, position, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0;

        /// <summary>PyOS_string_to_double with an end pointer: the longest double at position.</summary>
        internal static bool ScanDouble(string text, ref int position, out double value)
        {
            value = 0.0;
            int cursor = position;
            bool negative = false;
            if (cursor < text.Length && (text[cursor] == '+' || text[cursor] == '-'))
                negative = text[cursor++] == '-';
            if (MatchesAt(text, cursor, "inf"))
            {
                position = cursor + (MatchesAt(text, cursor, "infinity") ? 8 : 3);
                value = negative ? double.NegativeInfinity : double.PositiveInfinity;
                return true;
            }
            if (MatchesAt(text, cursor, "nan"))
            {
                position = cursor + 3;
                value = negative ? -QuietNaN : QuietNaN;
                return true;
            }
            int start = position, digits = 0;
            while (cursor < text.Length && char.IsAsciiDigit(text[cursor])) { cursor++; digits++; }
            if (cursor < text.Length && text[cursor] == '.')
            {
                cursor++;
                while (cursor < text.Length && char.IsAsciiDigit(text[cursor])) { cursor++; digits++; }
            }
            if (digits == 0) return false;
            if (cursor < text.Length && (text[cursor] == 'e' || text[cursor] == 'E'))
            {
                int exponent = cursor + 1;
                if (exponent < text.Length && (text[exponent] == '+' || text[exponent] == '-')) exponent++;
                int first = exponent;
                while (exponent < text.Length && char.IsAsciiDigit(text[exponent])) exponent++;
                if (exponent != first) cursor = exponent;
            }
            position = cursor;
            return double.TryParse(text.AsSpan(start, cursor - start), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        private static bool TryParseDouble(string text, out double value)
        {
            int position = 0;
            return ScanDouble(text, ref position, out value) && position == text.Length;
        }

        // float_from_string_inner with the underscore pass in front.
        internal static bool TryParsePythonText(string text, out double value)
        {
            value = 0.0;
            var clean = text == null ? null : WithoutUnderscores(text);
            if (clean == null) return false;
            int first = 0, last = clean.Length;
            while (first < last && IsCSpace(clean[first])) first++;
            while (last > first + 1 && IsCSpace(clean[last - 1])) last--;
            return first < last && TryParseDouble(clean.Substring(first, last - first), out value);
        }

        /// <summary>PyFloat_FromString: a str, or the bytes of anything exporting a buffer.</summary>
        internal static PyFloat FromObject(PyObject value)
        {
            string text;
            if (value is PyString str)
                text = TransformDecimalAndSpace(str.Value);
            else
            {
                var data = BytesMethods.Buffer(value) ?? throw PyTypeError.Create(
                    $"float() argument must be a string or a real number, not '{value.GetPyType().Name}'");
                text = System.Text.Encoding.Latin1.GetString(data);
            }
            if (TryParsePythonText(text, out double parsed))
                return new PyFloat(parsed);
            throw PyValueError.Create($"could not convert string to float: {value.ToRepr().Value}");
        }

        public static PyFloat FromString(string s) => FromObject(PyString.From(s));

        /// <summary>
        /// 다른 숫자 타입에서 PyFloat 생성
        /// CPython 3.12: Objects/floatobject.c:1533-1623 - PyFloat_FromDouble
        /// </summary>
        public static PyFloat FromNumber(PyObject obj)
        {
            return obj switch
            {
                PyFloat pyFloat => pyFloat,
                PyInt pyInt => new PyFloat(Modules.PyMathFunction.RealNumber(pyInt)),
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
        public static readonly PyFloat NaN = new PyFloat(QuietNaN);

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


        /// <summary>
        /// CPython 3.12: Objects/floatobject.c:1311-1549 float_fromhex, transliterated. Whitespace is
        /// ASCII-only (Py_ISSPACE), the exponent is optional, the result is correctly rounded with
        /// round-half-even, overflow raises OverflowError and an unparseable string raises ValueError
        /// with CPython's own text.
        /// </summary>
        internal static double ParseHexFloat(string text)
        {
            const int DblMantDig = 53;
            const int DblMinExp = -1021;
            const int DblMaxExp = 1024;
            long longHalfMax = long.MaxValue / 2;
            long longHalfMin = long.MinValue / 2;

            int end = text.Length;
            int s = 0;
            while (s < end && IsAsciiSpace(text[s]))
                s++;

            // Infinities and nans consume their own sign and jump to the trailing-whitespace check.
            {
                int probe = s;
                bool probeNegate = false;
                if (probe < end && (text[probe] == '-' || text[probe] == '+'))
                {
                    probeNegate = text[probe] == '-';
                    probe++;
                }

                string tail = text.Substring(probe).ToLowerInvariant();
                if (tail.StartsWith("inf", StringComparison.Ordinal))
                {
                    probe += 3;
                    if (text.Length - probe >= 5 && text.Substring(probe, 5).ToLowerInvariant() == "inity")
                        probe += 5;
                    s = probe;
                    // The sign belongs to the parsed value, not to the negate flag.
                    return FinishHex(false,
                        probeNegate ? double.NegativeInfinity : double.PositiveInfinity, text, s, end);
                }

                if (tail.StartsWith("nan", StringComparison.Ordinal))
                {
                    probe += 3;
                    s = probe;
                    return FinishHex(false, QuietNaN, text, s, end);
                }
            }

            bool negate = false;
            if (s < end && text[s] == '-')
            {
                negate = true;
                s++;
            }
            else if (s < end && text[s] == '+')
                s++;

            // [0x]
            int prefixStore = s;
            if (AsciiCharAt(text, s) == '0')
            {
                s++;
                if (AsciiCharAt(text, s) == 'x' || AsciiCharAt(text, s) == 'X')
                    s++;
                else
                    s = prefixStore;
            }

            // coefficient: <integer> [. <fraction>]
            int coeffStart = s;
            while (HexDigitOrMinus1(AsciiCharAt(text, s)) >= 0)
                s++;
            int fractionStore = s;
            int coeffEnd;
            if (AsciiCharAt(text, s) == '.')
            {
                s++;
                while (HexDigitOrMinus1(AsciiCharAt(text, s)) >= 0)
                    s++;
                coeffEnd = s - 1;
            }
            else
            {
                coeffEnd = s;
            }

            int ndigits = coeffEnd - coeffStart;
            int fdigits = coeffEnd - fractionStore;
            if (ndigits == 0)
                throw HexParseError();
            long digitLimit = Math.Min(DblMinExp - DblMantDig - longHalfMin,
                longHalfMax + 1 - DblMaxExp) / 4;
            if (ndigits > digitLimit)
                throw PyValueError.Create("hexadecimal string too long to convert");

            long exp = 0;
            if (AsciiCharAt(text, s) == 'p' || AsciiCharAt(text, s) == 'P')
            {
                s++;
                int expStart = s;
                if (AsciiCharAt(text, s) == '-' || AsciiCharAt(text, s) == '+')
                    s++;
                if (!(AsciiCharAt(text, s) >= '0' && AsciiCharAt(text, s) <= '9'))
                    throw HexParseError();
                s++;
                while (AsciiCharAt(text, s) >= '0' && AsciiCharAt(text, s) <= '9')
                    s++;
                exp = ParseClampedInt64(text.Substring(expStart, s - expStart));
            }

            // HEX_DIGIT(j) gives the jth most significant hex digit.
            int HexDigitAt(int j) => HexDigitOrMinus1(
                AsciiCharAt(text, j < fdigits ? coeffEnd - j : coeffEnd - 1 - j));

            while (ndigits > 0 && HexDigitAt(ndigits - 1) == 0)
                ndigits--;
            double x;
            if (ndigits == 0 || exp < longHalfMin)
                return FinishHex(negate, 0.0, text, s, end);
            if (exp > longHalfMax)
                throw HexOverflowError();

            exp = exp - 4L * fdigits;
            long topExp = exp + 4L * (ndigits - 1);
            for (int digit = HexDigitAt(ndigits - 1); digit != 0; digit /= 2)
                topExp++;

            if (topExp < DblMinExp - DblMantDig)
                return FinishHex(negate, 0.0, text, s, end);
            if (topExp > DblMaxExp)
                throw HexOverflowError();

            long lsb = Math.Max(topExp, DblMinExp) - DblMantDig;
            x = 0.0;
            if (exp >= lsb)
            {
                for (int i = ndigits - 1; i >= 0; i--)
                    x = 16.0 * x + HexDigitAt(i);
                x = Math.ScaleB(x, (int)exp);
                return FinishHex(negate, x, text, s, end);
            }

            int halfEps = 1 << (int)((lsb - exp - 1) % 4);
            int keyDigit = (int)((lsb - exp - 1) / 4);
            for (int i = ndigits - 1; i > keyDigit; i--)
                x = 16.0 * x + HexDigitAt(i);
            int keyHex = HexDigitAt(keyDigit);
            x = 16.0 * x + (double)(keyHex & (16 - 2 * halfEps));

            // round-half-even
            if ((keyHex & halfEps) != 0)
            {
                bool roundUp;
                if ((keyHex & (3 * halfEps - 1)) != 0 ||
                    (halfEps == 8 && keyDigit + 1 < ndigits && (HexDigitAt(keyDigit + 1) & 1) != 0))
                {
                    roundUp = true;
                }
                else
                {
                    roundUp = false;
                    for (int i = keyDigit - 1; i >= 0; i--)
                    {
                        if (HexDigitAt(i) != 0)
                        {
                            roundUp = true;
                            break;
                        }
                    }
                }

                if (roundUp)
                {
                    x += 2 * halfEps;
                    if (topExp == DblMaxExp && x == Math.ScaleB((double)(2 * halfEps), DblMantDig))
                        throw HexOverflowError();
                }
            }

            x = Math.ScaleB(x, (int)(exp + 4L * keyDigit));
            return FinishHex(negate, x, text, s, end);
        }

        private static double FinishHex(bool negate, double value, string text, int s, int end)
        {
            // optional trailing whitespace leading to the end of the string
            while (s < end && IsAsciiSpace(text[s]))
                s++;
            if (s != end)
                throw HexParseError();
            return negate ? -value : value;
        }

        private static Exception HexParseError() =>
            PyValueError.Create("invalid hexadecimal floating-point string");

        private static Exception HexOverflowError() =>
            PyOverflowError.Create("hexadecimal value too large to represent as a float");

        private static bool IsAsciiSpace(char c) =>
            c == ' ' || c == '\t' || c == '\n' || c == '\v' || c == '\f' || c == '\r';

        private static char AsciiCharAt(string text, int index) =>
            index >= 0 && index < text.Length ? text[index] : '\0';

        private static int HexDigitOrMinus1(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        private static long ParseClampedInt64(string digits)
        {
            // strtol saturates; the caller only compares the result against LONG_MIN/2 and LONG_MAX/2.
            bool negative = digits.Length > 0 && digits[0] == '-';
            int i = digits.Length > 0 && (digits[0] == '-' || digits[0] == '+') ? 1 : 0;
            long value = 0;
            for (; i < digits.Length; i++)
            {
                if (value > (long.MaxValue - 9) / 10)
                    return negative ? long.MinValue : long.MaxValue;
                value = value * 10 + (digits[i] - '0');
            }
            return negative ? -value : value;
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
    }
}
