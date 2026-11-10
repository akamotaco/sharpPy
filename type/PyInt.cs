using System;
using System.Numerics;
using Py_int_t = System.Numerics.BigInteger;

namespace SharpPy
{
    /// <summary>
    /// Python int 타입 구현 - BigInteger를 사용한 임의 정밀도 정수
    /// CPython 3.12: Objects/longobject.c - PyLongObject (arbitrary precision)
    /// Py_int_t = System.Numerics.BigInteger
    /// </summary>
    public class PyInt : PyObject
    {
        /// <summary>PyLong_Check: an int, a bool, or an instance of a class written over int.</summary>
        internal static bool LongCheck(PyObject value) => value is PyInt or PyBool or PyIntSubclass;

        /// <summary>The int value of what PyLong_Check accepts (a subclass instance's long_value), else null.</summary>
        internal static PyInt LongValue(PyObject value) => value switch
        {
            PyIntSubclass subclass => subclass.GetIntValue(),
            PyInt exact => exact,
            PyBool flag => flag.Value ? PyInt.One : PyInt.Zero,
            _ => null,
        };

        // Python's numeric hash has native signed width, unlike CLR GetHashCode.
        private static readonly BigInteger PythonHashModulus =
            (BigInteger.One << (IntPtr.Size == 8 ? 61 : 31)) - 1;

        internal static long PythonHash(BigInteger value)
        {
            long hash = (long)(value % PythonHashModulus);
            return hash == -1 ? -2 : hash;
        }

        static PyInt()
        {
            InitializeIntDescriptors();
        }

        /// <summary>
        /// Initialize int type descriptors (CPython 3.12 compatible)
        /// CPython reference: Objects/longobject.c:5800-5900 - long_methods
        /// </summary>
        /// <summary>
        /// CPython PyLong_Check semantics for builtin int descriptors and getsets: a subclass
        /// instance passes the self test and the stored digits are used directly
        /// (Objects/longobject.c never demands an exact PyLong_Type). In CLR a PyIntSubclass is not
        /// a PyInt, so every `self is PyInt` gate is a subclass bug; they all route here instead.
        /// </summary>
        /// <summary>
        /// CPython 3.12: Objects/longobject.c long_round, shared by the builtin round() and the
        /// int.__round__ slot so there is one implementation.
        ///   - no ndigits / None            -> the value itself, as an exact int
        ///   - ndigits &gt;= 0                     -> the value itself (nothing to truncate)
        ///   - ndigits &lt; 0                      -> round half to even at 10**-ndigits
        ///   - ndigits too big for C long   -> the value itself (measured: round(5, 10**30) == 5)
        /// An int subclass rounds to a plain int, because PyLong_FromDouble/long_round does not
        /// preserve the subtype.
        /// </summary>
        internal static PyObject RoundTo(PyObject self, PyObject ndigits)
        {
            // int___round___impl: no ndigits is the int itself; anything given is _PyNumber_Index's,
            // None included (unlike float_round, which takes None).
            var value = AsIntForRound(self);
            if (ndigits == null)
                return PyInt.From(value.Value);

            var index = PyObject.IndexOrThrow(ndigits);

            // Measured: round(5, 10**30) == 5. A positive ndigits beyond the digit count of the
            // value means "nothing to truncate", so it is answered without converting through a
            // bounded CLR integer.
            if (index.Value > (System.Numerics.BigInteger)int.MaxValue)
                return PyInt.From(value.Value);

            long nd = (long)index.Value;
            if (nd >= 0)
                return PyInt.From(value.Value);
            return PyInt.From(RoundIntToPowerOfTen(value.Value, -nd));
        }

        // The self side of long_round reads the digits, so a subclass contributes its value.
        private static PyInt AsIntForRound(PyObject self)
        {
            if (self is PyInt exact)
                return exact;
            if (self is PyIntSubclass subclass)
                return subclass.GetIntValue();
            if (self is PyBool flag)
                return PyInt.From(flag.Value ? System.Numerics.BigInteger.One : System.Numerics.BigInteger.Zero);
            throw PyTypeError.Create($"type {self.GetTypeName()} doesn't define __round__ method");
        }

        internal static bool TryLongSelf(PyObject self, out PyInt value)
        {
            // PyLong_Check gates these slots (bool and subclasses pass), and long_long gives the
            // exact int: a subclass instance becomes a plain int.
            value = LongValue(self);
            return value != null;
        }


        public static void InitializeIntDescriptors()
        {
            var intType = PyType.IntType;
            string[] comparisonNames = { "__lt__", "__le__", "__eq__", "__ne__", "__gt__", "__ge__" };
            for (int i = 0; i < comparisonNames.Length; i++)
            {
                string name = comparisonNames[i];
                var op = (CompareOp)i;
                intType.TypeDict[name] = new PyWrapperDescriptor(name, intType,
                    (self, args, kwargs) => {
                        var left = self as PyInt ?? (self as PyIntSubclass)?.GetIntValue();
                        var right = args[0] as PyInt ?? (args[0] as PyIntSubclass)?.GetIntValue();
                        if (right == null && args[0] is not PyBool)
                            return PyNotImplemented.Instance;
                        var a = left != null ? left.Value : (((PyBool)self).Value ? BigInteger.One : BigInteger.Zero);
                        var b = right != null ? right.Value : (((PyBool)args[0]).Value ? BigInteger.One : BigInteger.Zero);
                        int order = a.CompareTo(b);
                        return PyBool.FromBool(op switch {
                            CompareOp.LT => order < 0, CompareOp.LE => order <= 0,
                            CompareOp.EQ => order == 0, CompareOp.NE => order != 0,
                            CompareOp.GT => order > 0, _ => order >= 0
                        });
                    });
            }
            // CPython 3.12: int exposes the nb_* slots as slot wrappers
            // (Objects/typeobject.c NBINOP/NBUNARYOP slotdefs). Error strings measured
            // against CPython 3.12.15; NotImplemented propagates for unsupported right
            // operands exactly like the C slots.
            static bool IntLike(PyObject o) => LongCheck(o);
            static PyInt AsIntObj(PyObject o) => LongValue(o);
            void regIntUn(string name, Func<PyObject, PyObject> impl)
            {
                intType.TypeDict[name] = new PyWrapperDescriptor(name, intType, (self, args, kwargs) =>
                {
                    return impl(AsIntObj(self));
                });
            }
            // Measured against dir(int) in CPython, which also publishes __index__, __trunc__,
            // __floor__, __ceil__, __int__ and __float__. SharpPy had none of them, so
            // operator.index(ISub(5)) raised, and bin(ISub(5)) followed a user __index__ (0b11) while
            // CPython uses the inherited value slot (0b101). The delegates read the payload through
            // AsIntObj/IntLike, so a subclass contributes its own value and the result stays plain int.
            regIntUn("__index__", s => s);
            intType.TypeDict["__trunc__"] = new PyMethodDescriptor("__trunc__", intType, (self, args, kwargs) =>
            {
                if (args.Length != 0)
                    throw PyTypeError.Create($"int.__trunc__() takes no arguments ({args.Length} given)");
                return AsIntObj(self);
            });
            intType.TypeDict["__floor__"] = new PyMethodDescriptor("__floor__", intType, (self, args, kwargs) =>
            {
                if (args.Length != 0)
                    throw PyTypeError.Create($"int.__floor__() takes no arguments ({args.Length} given)");
                return AsIntObj(self);
            });
            intType.TypeDict["__ceil__"] = new PyMethodDescriptor("__ceil__", intType, (self, args, kwargs) =>
            {
                if (args.Length != 0)
                    throw PyTypeError.Create($"int.__ceil__() takes no arguments ({args.Length} given)");
                return AsIntObj(self);
            });
            regIntUn("__int__", s => s);
            regIntUn("__float__", s => s.NbFloat());

            regIntUn("__neg__", s => s.NbNegative());
            regIntUn("__pos__", s => s.NbPositive());
            regIntUn("__invert__", s => s.NbInvert());
            regIntUn("__abs__", s => PyInt.From(BigInteger.Abs(AsIntObj(s).Value)));

            // CPython 3.12: Objects/longobject.c long_round exposed as the int.__round__ slot, so a
            // subclass inherits rounding and the result is a plain int.
            intType.TypeDict["__round__"] = new PyMethodDescriptor(
                "__round__", intType,
                (self, args, kwargs) => {
                    // Measured CPython slot surface: keywords are refused with the qualified name,
                    // extra positionals with the bare method name.
                    if (kwargs != null && kwargs.Length() != 0)
                        throw PyTypeError.Create("int.__round__() takes no keyword arguments");
                    if (args.Length > 1)
                        throw PyTypeError.Create(
                            $"__round__ expected at most 1 argument, got {args.Length}");
                    return PyInt.RoundTo(self, args.Length > 0 ? args[0] : null);
                },
                minArgs: 0, maxArgs: 1);

            intType.TypeDict["__hash__"] = new PyWrapperDescriptor(
                "__hash__", intType,
                (self, args, kwargs) => {
                    var number = self as PyInt ?? (self as PyIntSubclass)?.GetIntValue();
                    if (number != null) return PyInt.From(PythonHash(number.Value));
                    return PyInt.From(((PyBool)self).Value ? 1 : 0);
                });


            // CPython 3.12: Objects/longobject.c:5642-5658 - int_bit_length
            // Number of bits necessary to represent self in binary.
            // >>> bin(37)
            // '0b100101'
            // >>> (37).bit_length()
            // 6
            intType.TypeDict["bit_length"] = new PyMethodDescriptor(
                "bit_length", intType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"int.bit_length() takes no arguments ({args.Length} given)");
                    PyInt.TryLongSelf(self, out var intObj);

                    // CPython 3.12: Objects/longobject.c long_bit_length via _PyLong_NumBits:
                    // bit count of abs(n), 0 -> 0. Must work beyond the CLR long range:
                    // (1 << 63).bit_length() == 64.
                    return PyInt.From(System.Numerics.BigInteger.Abs(intObj.Value).GetBitLength());
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/longobject.c:5660-5676 - int_bit_count
            // Number of ones in the binary representation of the absolute value of self.
            // Also known as the population count.
            // >>> bin(13)
            // '0b1101'
            // >>> (13).bit_count()
            // 3
            intType.TypeDict["bit_count"] = new PyMethodDescriptor(
                "bit_count", intType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"int.bit_count() takes no arguments ({args.Length} given)");
                    PyInt.TryLongSelf(self, out var intObj);

                    return intObj.BitCount();
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/longobject.c:5678-5750 - int_to_bytes
            // Return an array of bytes representing an integer.
            intType.TypeDict["to_bytes"] = new PyMethodDescriptor(
                "to_bytes", intType,
                (self, args, kwargs) => {
                    PyInt.TryLongSelf(self, out var intObj);

                    // CPython 3.12, Objects/longobject.c: long_to_bytes_impl is
                    // METH_FASTCALL|METH_KEYWORDS with (length=1, byteorder='big', /, signed=False).
                    // Measured on CPython 3.12.15:
                    //   (5).to_bytes()                 -> b'\x05'   (length defaults to 1, not "minimal")
                    //   (70000).to_bytes()             -> OverflowError: int too big to convert
                    //   (-1).to_bytes()                -> OverflowError: can't convert negative int to unsigned
                    //   (256).to_bytes(1)              -> OverflowError: int too big to convert
                    //   (-1).to_bytes(1, signed=True)  -> b'\xff'
                    //   (255).to_bytes(1,'big',signed=True) -> OverflowError: int too big to convert
                    // The delegate reads the positional array, so keywords are projected into their
                    // parameter slots first. `signed` is keyword-only in CPython.
                    PyObject lengthArg = args.Length > 0 ? args[0] : KwValue(kwargs, "length");
                    PyObject byteorderArg = args.Length > 1 ? args[1] : KwValue(kwargs, "byteorder");
                    PyObject signedArg = KwValue(kwargs, "signed");

                    int length = 1;
                    if (lengthArg != null)
                    {
                        // int_to_bytes's clinic: length is a Py_ssize_t (_PyNumber_Index).
                        length = PyAllocation.ArrayLength(PyArgs.Ssize(lengthArg));
                    }

                    string byteorder = "big";
                    if (byteorderArg != null)
                    {
                        if (byteorderArg is not PyString byteorderStr)
                            throw PyArgs.BadArgument("to_bytes", "argument 'byteorder'", "str", byteorderArg);
                        byteorder = byteorderStr.Value;
                    }

                    bool signed = signedArg != null && signedArg.ToBool();

                    if (byteorder != "big" && byteorder != "little")
                        throw PyValueError.Create($"byteorder must be either 'little' or 'big'");

                    // CPython 3.12: Objects/longobject.c long_to_bytes_impl / long_to_bytes.
                    // An int is arbitrary precision, so the capacity test compares bit counts and the
                    // bytes come out of the BigInteger. The previous code cast to long: for 2**70 that
                    // leaked System.OverflowException, which is not a Python exception, so the program
                    // died with a CLR stack trace instead of reporting OverflowError.
                    System.Numerics.BigInteger big = intObj.Value;
                    bool isNegative = big.Sign < 0;

                    // Order of the checks is CPython's.
                    if (isNegative && !signed)
                        throw PyOverflowError.Create("can't convert negative int to unsigned");
                    if (length < 0)
                        throw PyValueError.Create("length argument must be non-negative");

                    // CPython tests _PyLong_NumBits against length * 8, and for signed additionally
                    // against the sign bit: -128 fits in one signed byte, 128 does not; -1 fits, 255
                    // does not.
                    if ((System.Numerics.BigInteger)BigBitLength(big)
                        > (System.Numerics.BigInteger)length * 8)
                        throw PyOverflowError.Create("int too big to convert");
                    if (signed && length > 0)
                    {
                        var signBit = System.Numerics.BigInteger.One << (length * 8 - 1);
                        if (isNegative ? big < -signBit : big >= signBit)
                            throw PyOverflowError.Create("int too big to convert");
                    }

                    // Two's complement over exactly `length` bytes, so negative values need no
                    // separate invert-and-increment pass.
                    if (isNegative)
                        big += System.Numerics.BigInteger.One << (length * 8);

                    byte[] bytes = new byte[length];
                    for (int i2 = 0; i2 < length; i2++)
                    {
                        int index = byteorder == "big" ? length - 1 - i2 : i2;
                        bytes[index] = (byte)(int)(big & 255);
                        big >>= 8;
                    }

                    return PyBytes.From(bytes);
                },
                minArgs: 0, maxArgs: 2, acceptsKwargs: true);

            // CPython 3.12: Objects/longobject.c:5752-5820 - int_from_bytes (classmethod)
            // Return the integer represented by the given array of bytes.
            var fromBytesMethod = new PyMethodDescriptor(
                "from_bytes", intType,
                (self, args, kwargs) => {
                    // self is the class (PyType) when called as classmethod

                    // The argument is bytes-like or an iterable of ints; the conversion and its
                    // error text live in PyObject_Bytes (BytesMethods.ObjectBytes), so no exact-PyBytes gate goes here.

                    // Get byteorder argument (default 'big')
                    // The delegate reads the positional array, so byteorder= has to be resolved
                    // from the keywords as well; reading only args[1] silently kept the 'big'
                    // default and int.from_bytes(b' ', byteorder='little') answered 256.
                    string byteorder = "big";
                    PyObject byteorderArg = args.Length >= 2 ? args[1] : KwValue(kwargs, "byteorder");
                    if (byteorderArg != null)
                    {
                        if (byteorderArg is not PyString byteorderStr)
                            throw PyArgs.BadArgument("from_bytes", "argument 'byteorder'", "str", byteorderArg);
                        byteorder = byteorderStr.Value;
                    }

                    if (byteorder != "big" && byteorder != "little")
                        throw PyValueError.Create($"byteorder must be either 'little' or 'big'");

                    // CPython signature is from_bytes(bytes, byteorder='big', *, signed=False).
                    // `signed` was not parsed at all here.
                    bool signedValue = false;
                    var signedArg = KwValue(kwargs, "signed");
                    if (signedArg != null)
                        signedValue = signedArg.ToBool();

                    // CPython long_from_bytes: read the bytes as an unsigned magnitude, then apply
                    // the sign only when signed=True. Two defects were here: the accumulator was a
                    // long, so 10 bytes of 2**70 shifted out and the call returned 0 silently; and
                    // the top bit was read as a sign unconditionally, so int.from_bytes(b'\xff', 'big')
                    // answered -1 where CPython answers 255.
                    var data = BytesMethods.ObjectBytes(args[0]).Value;
                    System.Numerics.BigInteger magnitude = 0;
                    for (int i = 0; i < data.Length; i++)
                    {
                        int index = byteorder == "big" ? data.Length - 1 - i : i;
                        magnitude += (System.Numerics.BigInteger)data[index] << (8 * i);
                    }

                    if (signedValue && data.Length > 0 &&
                        (data[byteorder == "big" ? 0 : data.Length - 1] & 0x80) != 0)
                        magnitude -= System.Numerics.BigInteger.One << (8 * data.Length);

                    // int_from_bytes: a subclass is built from the value.
                    return ReferenceEquals(self, intType) ? PyInt.From(magnitude) : self.Call(new PyObject[] { PyInt.From(magnitude) }, null);
                },
                minArgs: 1, maxArgs: 2, acceptsKwargs: true
            );
            // Wrap in classmethod descriptor
            intType.TypeDict["from_bytes"] = new PyClassMethodDescriptor("from_bytes", intType, fromBytesMethod);

            // CPython 3.12: Objects/longobject.c:5526-5540 - int_as_integer_ratio
            // Return integer ratio.
            // Return a pair of integers, whose ratio is exactly equal to the original int
            // and with a positive denominator.
            intType.TypeDict["as_integer_ratio"] = new PyMethodDescriptor(
                "as_integer_ratio", intType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"int.as_integer_ratio() takes no arguments ({args.Length} given)");
                    PyInt.TryLongSelf(self, out var intObj);

                    // For integers, the ratio is simply (self, 1)
                    return PyTuple.From(intObj, PyInt.From(1));
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/longobject.c - int.conjugate method
            // Return self, the complex conjugate of any int.
            intType.TypeDict["conjugate"] = new PyMethodDescriptor(
                "conjugate", intType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create(
                            $"int.conjugate() takes no arguments ({args.Length} given)");
                    PyInt.TryLongSelf(self, out var intObj);

                    // long_long (Objects/longobject.c:5358-5367): an exact int is returned as itself, a
                    // bool or int subclass becomes a plain int, so type(I(3).conjugate()) is int.
                    return intObj;
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/longobject.c - int.real property
            // Return the real part of the int (which is the int itself).
            intType.TypeDict["real"] = new PyGetSetDescriptor(
                "real", intType,
                getter: self => {
                    PyInt.TryLongSelf(self, out var longValue);
                    // long_long: exact ints come back as themselves, bool/int subclasses as plain ints.
                    return longValue;
                }
            );

            // CPython 3.12: Objects/longobject.c - int.imag property
            // Return the imaginary part of the int (which is 0).
            intType.TypeDict["imag"] = new PyGetSetDescriptor(
                "imag", intType,
                getter: self => {
                    return PyInt.From(0);  // long_get0: a fresh int, not self
                }
            );

            // CPython 3.12: Objects/longobject.c - int.numerator property
            // The numerator of a rational number in lowest terms (returns self).
            intType.TypeDict["numerator"] = new PyGetSetDescriptor(
                "numerator", intType,
                getter: self => {
                    PyInt.TryLongSelf(self, out var longValue);
                    return longValue;  // long_long, same rule as real
                }
            );

            // CPython 3.12: Objects/longobject.c - int.denominator property
            // The denominator of a rational number in lowest terms (always 1 for integers).
            intType.TypeDict["denominator"] = new PyGetSetDescriptor(
                "denominator", intType,
                getter: self => {
                    return PyInt.From(1);  // long_get1
                }
            );

            // CPython 3.12: Objects/longobject.c:5822-5840 - long_format
            // int.__format__(format_spec) - Format the integer according to format_spec
            // CPython Objects/longobject.c:5679-5690 int.__getnewargs__ -> (_PyLong_Copy(self),):
            // a plain int copy, which is what pickle reconstructs from.
            intType.TypeDict["__getnewargs__"] = new PyMethodDescriptor(
                "__getnewargs__", intType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create(
                            $"int.__getnewargs__() takes no arguments ({args.Length} given)");
                    PyInt.TryLongSelf(self, out var longValue);
                    return PyTuple.From(longValue);
                },
                minArgs: 0, maxArgs: 0);

            intType.TypeDict["__format__"] = new PyMethodDescriptor(
                "__format__", intType,
                (self, args, kwargs) => {
                    if (args[0] is not PyString specStr)
                        throw PyArgs.BadArgument("__format__", "argument", "str", args[0]);

                    if (specStr.Value.Length == 0)
                        return self.ToStr();
                    if (specStr.Value == "d")
                    {
                        if (self is PyInt plain) return plain.ToStr();
                        if (self is PyIntSubclass derived) return derived.GetIntValue().ToStr();
                    }

                    // CPython 3.12: Objects/longobject.c:5555-5640 - long__format__
                    // Support both PyInt and PyIntSubclass (for IntEnum, IntFlag, etc.)
                    BigInteger value;
                    if (self is PyInt intObj)
                        value = intObj.Value;
                    else if (self is PyIntSubclass intSubclass)
                        value = intSubclass.GetIntValue().Value;
                    else
                        value = ((PyBool)self).Value ? BigInteger.One : BigInteger.Zero;

                    return PyString.From(FormatInt(value, specStr.Value, self.GetPyType().FullName));
                },
                minArgs: 1, maxArgs: 1
            );

            // CPython 3.12: Objects/longobject.c:5597-5641 (long_new_impl)
            // int.__new__(cls, x=0, base=10)
            intType.TypeDict["__new__"] = PyStaticBuiltinMethod.ForNew(
                intType,
                (args, kwargs) =>
                {
                    // args[0] is the type, which tp_new_wrapper has checked.
                    var cls = (PyType)args[0];

                    var converted = FromArguments(args, kwargs, 1);
                    Py_int_t value = converted.Value;

                    // CPython: Line 5603-5604
                    // if (type != &PyLong_Type)
                    //     return long_subtype_new(type, x, obase);
                    if (cls != PyType.IntType)
                    {
                        // This is an int subclass (like IntEnum)
                        // CPython: long_subtype_new (Objects/longobject.c:5648-5677)
                        // Creates PyLongObject with subtype's ob_type
                        if (args[0] is PyClass classObj)
                        {
                            // CPython: line 5665 - newobj = (PyLongObject *)type->tp_alloc(type, n);
                            // line 5671-5674 - copy int value from tmp to newobj
                            return new PyIntSubclass(classObj, converted);
                        }
                        else
                        {
                            // cls is a PyType but not PyClass, shouldn't happen normally
                            throw PyTypeError.Create($"int.__new__: expected class, got {cls.GetTypeName()}");
                        }
                    }
                    else
                    {
                        // Regular int type, return PyInt
                        return converted;
                    }
                }
            );
        }

        #region Core Properties

        public Py_int_t Value { get; }

        // CPython 3.12: Objects/longobject.c - PyLongObject inherits ob_type from PyObject
        // Constructor can optionally set custom type for subclasses (IntEnum, IntFlag)
        public PyInt(Py_int_t value, PyType? customType = null)
        {
            Value = value;
            if (customType != null)
            {
                _customType = customType;  // Inherited from PyObject
            }
        }

        /// <summary>
        /// The one way the interpreter makes an exact int, as PyLong_FromLong, maybe_small_long and
        /// _PyLong_Copy are CPython's: -5 .. 256 is get_small_int's one object each. The public
        /// constructor stays for host code; the gate keeps it out of the interpreter.
        /// </summary>
        public static PyInt From(Py_int_t value) =>
            value >= SmallMin && value <= SmallMax ? Small[(int)value - SmallMin] : new PyInt(value);

        public static PyInt From(long value) =>
            value >= SmallMin && value <= SmallMax ? Small[(int)value - SmallMin] : new PyInt(value);

        // _PY_NSMALLNEGINTS and _PY_NSMALLPOSINTS
        private const int SmallMin = -5, SmallMax = 256;
        private static readonly PyInt[] Small = MakeSmall();
        private static PyInt[] MakeSmall()
        {
            var table = new PyInt[SmallMax - SmallMin + 1];
            for (int i = 0; i < table.Length; i++) table[i] = new PyInt(SmallMin + i);
            return table;
        }

        // CPython 3.12: Objects/object.c:350 - Py_TYPE(op) returns ob_type
        // Uses PyObject's GetPyType() which returns _customType ?? PyType.IntType
        public override PyType GetPyType() => _customType ?? PyType.IntType;

        // PyX_CheckExact
        internal bool IsExact => IsExactly(PyType.IntType);

        // CPython 3.12: Objects/longobject.c - long_bool
        // Optimized: Direct value check, no MRO traversal
        protected internal override bool Bool() => Value != 0;

        #endregion

        #region Type Conversion Methods (CPython 3.12 compatible)

        /// <summary>
        /// Convert BigInteger to long (int64)
        /// CPython 3.12: Objects/longobject.c:339-352 - PyLong_AsLong
        /// Raises OverflowError if value is out of range
        /// </summary>
        public long ToLong()
        {
            // CPython: Check if value fits in long range
            if (Value > long.MaxValue || Value < long.MinValue)
            {
                throw PyOverflowError.Create("int too large to convert to long");
            }
            return (long)Value;
        }

        /// <summary>
        /// Convert BigInteger to double
        /// CPython 3.12: Objects/longobject.c:3329-3360 - PyLong_AsDouble
        /// </summary>
        public double ToDouble()
        {
            return (double)Value;
        }


        #endregion

        #region String Representation

        private string DecimalString() => DecimalString(Value);

        private static string DecimalString(BigInteger value)
        {
            // Every permitted nonzero limit is at least 640 digits. Native-sized
            // values cannot reach it, and Int64 formatting avoids BigInteger's
            // general decimal conversion machinery.
            long bits = value.GetBitLength();
            if (bits < 64) return ((long)value).ToString(System.Globalization.CultureInfo.InvariantCulture);
            int limit = PyVM.Instance.IntMaxStrDigits;
            // A conservative bit bound rejects clearly oversized output before
            // expensive decimal conversion; near the boundary count the digits.
            if (limit != 0 && bits > (long)limit * 3322 / 1000 + 2)
                throw PyValueError.Create($"Exceeds the limit ({limit} digits) for integer string conversion; use sys.set_int_max_str_digits() to increase the limit");
            string text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            int digits = text.Length - (value.Sign < 0 ? 1 : 0);
            if (limit != 0 && digits > limit)
                throw PyValueError.Create($"Exceeds the limit ({limit} digits) for integer string conversion; use sys.set_int_max_str_digits() to increase the limit");
            return text;
        }
        protected internal override PyString Str() => PyString.Fresh(DecimalString());
        protected internal override PyString Repr() => PyString.Fresh(DecimalString());

        #endregion

        #region Hash and Equality

        // long_hash
        protected internal override long Hash() => PythonHash(Value);

        public override int ToHash() => Value.GetHashCode();

        protected override PyObject PyNotEquals(PyObject other)
        {
            var result = PyEquals(other);
            return result is PyBool equal ? PyBool.FromBool(!equal.Value) : result;
        }

        protected override PyObject PyEquals(PyObject other)
        {
            return other switch
            {
                // CPython 3.12: Objects/longobject.c:3200-3250 - long_richcompare
                PyInt otherInt => PyBool.FromBool(Value == otherInt.Value),
                PyFloat { IsExact: true } otherFloat => PyBool.FromBool(PyFloat.CompareToInteger(otherFloat.Value, Value) == 0),
                PyBool otherBool => PyBool.FromBool(Value == (otherBool.Value ? 1 : 0)),
                _ => PyNotImplemented.Instance
            };
        }

        #endregion

        #region Comparison Operations

        protected override PyObject PyLess(PyObject other)
        {
            // CPython 3.12: Objects/longobject.c:3200-3250 - long_richcompare
            if (other is PyInt otherInt)
                return PyBool.FromBool(Value < otherInt.Value);
            if (other is PyFloat { IsExact: true } otherFloat)
                return PyBool.FromBool(PyFloat.CompareToInteger(otherFloat.Value, Value) > 0);
            if (other is PyBool otherBool)
                return PyBool.FromBool(Value < (otherBool.Value ? 1 : 0));
            if (other is PyIntSubclass otherSubclass)
                return PyBool.FromBool(Value < otherSubclass.GetIntValue().Value);

            // Return NotImplemented to allow other object's __gt__ to be tried
            return PyNotImplemented.Instance;
        }

        protected override PyObject PyLessEqual(PyObject other)
        {
            // CPython 3.12: Objects/longobject.c:3200-3250 - long_richcompare
            if (other is PyInt otherInt)
                return PyBool.FromBool(Value <= otherInt.Value);
            if (other is PyFloat { IsExact: true } otherFloat)
                return PyBool.FromBool(PyFloat.CompareToInteger(otherFloat.Value, Value) >= 0);
            if (other is PyBool otherBool)
                return PyBool.FromBool(Value <= (otherBool.Value ? 1 : 0));
            if (other is PyIntSubclass otherSubclass)
                return PyBool.FromBool(Value <= otherSubclass.GetIntValue().Value);

            // Return NotImplemented to allow other object's __ge__ to be tried
            return PyNotImplemented.Instance;
        }

        protected override PyObject PyGreater(PyObject other)
        {
            // CPython 3.12: Objects/longobject.c:3200-3250 - long_richcompare
            if (other is PyInt otherInt)
                return PyBool.FromBool(Value > otherInt.Value);
            if (other is PyFloat { IsExact: true } otherFloat)
                return PyBool.FromBool(PyFloat.CompareToInteger(otherFloat.Value, Value) < 0);
            if (other is PyBool otherBool)
                return PyBool.FromBool(Value > (otherBool.Value ? 1 : 0));
            if (other is PyIntSubclass otherSubclass)
                return PyBool.FromBool(Value > otherSubclass.GetIntValue().Value);

            // Return NotImplemented to allow other object's __lt__ to be tried
            return PyNotImplemented.Instance;
        }

        protected override PyObject PyGreaterEqual(PyObject other)
        {
            // CPython 3.12: Objects/longobject.c:3200-3250 - long_richcompare
            // Try direct comparison first
            if (other is PyInt otherInt)
                return PyBool.FromBool(Value >= otherInt.Value);
            if (other is PyFloat { IsExact: true } otherFloat)
                return PyBool.FromBool(PyFloat.CompareToInteger(otherFloat.Value, Value) <= 0);
            if (other is PyBool otherBool)
                return PyBool.FromBool(Value >= (otherBool.Value ? 1 : 0));
            if (other is PyIntSubclass otherSubclass)
                return PyBool.FromBool(Value >= otherSubclass.GetIntValue().Value);

            // CPython: If other type doesn't handle comparison, return NotImplemented
            // This allows the other object's __le__ method to be tried
            // Objects/longobject.c:3246 - Py_RETURN_NOTIMPLEMENTED
            return PyNotImplemented.Instance;
        }

        #endregion

        #region Arithmetic Operations

        // PyLong_Check: a bool and an int subclass are ints, with their value.
        private static PyObject LongOperand(PyObject other) => other switch
        {
            PyBool flag => flag.Value ? One : Zero,
            PyIntSubclass subclass => subclass.GetIntValue(),
            _ => other,
        };

        // CONVERT_TO_DOUBLE of float_add and its kin, to_complex of complex_add: this int as the
        // left operand of the other type's own operation.
        private PyFloat AsFloat() => new PyFloat(ToFloat());
        private PyComplex AsComplex() => new PyComplex(ToFloat(), 0.0);

        public override PyObject Add(PyObject other) => LongOperand(other) switch
        {
            PyInt otherInt => PyInt.From(Value + otherInt.Value),
            PyFloat { IsExact: true } otherFloat => AsFloat().Add(otherFloat),
            PyComplex otherComplex => AsComplex().Add(otherComplex),
            _ => PyNotImplemented.Instance,
        };

        public override PyObject Subtract(PyObject other) => LongOperand(other) switch
        {
            PyInt otherInt => PyInt.From(Value - otherInt.Value),
            PyFloat { IsExact: true } otherFloat => AsFloat().Subtract(otherFloat),
            PyComplex otherComplex => AsComplex().Subtract(otherComplex),
            _ => PyNotImplemented.Instance,
        };

        public override PyObject Multiply(PyObject other) => LongOperand(other) switch
        {
            PyInt otherInt => PyInt.From(Value * otherInt.Value),
            PyFloat { IsExact: true } otherFloat => AsFloat().Multiply(otherFloat),
            PyComplex otherComplex => AsComplex().Multiply(otherComplex),
            _ => PyNotImplemented.Instance,
        };

        public override PyObject Divide(PyObject other)
        {
            // CPython 3.12: Objects/longobject.c:2533-2570 - long_true_divide
            other = LongOperand(other);
            if (other is PyInt otherInt)
            {
                if (otherInt.Value.IsZero)
                    throw PyZeroDivisionError.Create("division by zero");
                // Both exact as doubles: IEEE division is already the correctly rounded quotient.
                const long Exact = 1L << 53;
                if (Value < Exact && Value > -Exact && otherInt.Value < Exact && otherInt.Value > -Exact)
                    return new PyFloat((double)Value / (double)otherInt.Value);
                return BigIntTrueDivide(Value, otherInt.Value);
            }

            return other is PyFloat { IsExact: true } otherFloat ? AsFloat().Divide(otherFloat) : PyNotImplemented.Instance;
        }

        public override PyObject FloorDivide(PyObject other)
        {
            other = LongOperand(other);
            if (other is PyInt otherInt)
            {
                if (otherInt.Value == 0)
                    throw PyZeroDivisionError.Create("integer division or modulo by zero");

                // CPython: Objects/longobject.c:long_div - Python floor division
                // Python floor division: result is floor(a/b), not truncate(a/b)
                var quotient = Value / otherInt.Value;
                var remainder = Value % otherInt.Value;

                // Adjust for floor division: if signs differ and there's a remainder, subtract 1
                if (remainder != 0 && ((Value < 0) != (otherInt.Value < 0)))
                {
                    quotient -= 1;
                }

                return PyInt.From(quotient);
            }
            return other is PyFloat { IsExact: true } otherFloat ? AsFloat().FloorDivide(otherFloat) : PyNotImplemented.Instance;
        }

        public override PyObject Modulo(PyObject other)
        {
            other = LongOperand(other);
            if (other is PyInt otherInt)
            {
                if (otherInt.Value == 0)
                    throw PyZeroDivisionError.Create("integer modulo by zero");

                // CPython: Objects/longobject.c:long_rem - Python modulo
                // Python modulo: result has same sign as divisor
                var remainder = Value % otherInt.Value;

                // Adjust for Python modulo: if signs differ and remainder != 0, add divisor
                if (remainder != 0 && ((Value < 0) != (otherInt.Value < 0)))
                {
                    remainder += otherInt.Value;
                }

                return PyInt.From(remainder);
            }
            return other is PyFloat { IsExact: true } otherFloat ? AsFloat().Modulo(otherFloat) : PyNotImplemented.Instance;
        }

        /// <summary>
        /// CPython 3.12: int.__divmod__ - divmod() operation
        /// CPython: Objects/longobject.c:long_divmod (lines 4509-4526)
        /// Returns tuple of (quotient, remainder) equivalent to (a // b, a % b)
        /// </summary>
        /// <summary>
        /// CPython _PyLong_NumBits: the number of bits in the magnitude, 0 for zero.
        /// </summary>
        private static int BigBitLength(System.Numerics.BigInteger value)
        {
            if (value.Sign == 0)
                return 0;
            // BigInteger.BitLength is an explicit IBinaryInteger member here, not reachable
            // directly on net8.0, so the top byte is scanned.
            var bytes = System.Numerics.BigInteger.Abs(value)
                .ToByteArray(isUnsigned: true, isBigEndian: false);
            int top = bytes.Length - 1;
            while (top > 0 && bytes[top] == 0)
                top--;
            int bits = top * 8;
            byte high = bytes[top];
            while (high > 0)
            {
                bits++;
                high >>= 1;
            }
            return bits;
        }

        public override PyObject DivMod(PyObject other)
        {
            // CPython's long_divmod hands a float operand to float_divmod, so an int on the left
            // and a float (or float subclass) on the right answers with the float pair: measured
            // divmod(2, FSub(2.5)) is (0.0, 2.0). Answering NotImplemented here left nothing to
            // try, because the reflected side is reached through the VM's own dispatch.
            if (PyFloat.TryFloatSelf(other, out var otherFloat))
                return PyFloat.DivModValues(this, otherFloat);

            // PyLong_Check accepts subclasses, so divmod(2, IPlain(5)) is (0, 2); an exact CLR test
            // answered NotImplemented and nothing downstream recovered.
            if (!PyInt.TryLongSelf(other, out var otherInt))
                return PyNotImplemented.Instance;

            if (otherInt.Value == 0)
                throw PyZeroDivisionError.Create("integer division or modulo by zero");

            // CPython: Objects/longobject.c:4515-4516 - Call l_divmod which returns quotient and remainder
            // Use existing FloorDivide and Modulo to ensure consistency
            // The operands here are the resolved payloads, not the originals: FloorDivide/Modulo
            // answer NotImplemented for an int subclass, which used to build (NotImplemented,
            // NotImplemented) into the tuple. Results stay plain ints, as in CPython.
            var quotient = FloorDivide(otherInt);
            var remainder = Modulo(otherInt);

            return PyTuple.From(quotient, remainder);
        }

        // long_pow with no modulus; a float exponent is float_pow of the converted value.
        public override PyObject Power(PyObject other)
        {
            other = LongOperand(other);
            if (other is PyFloat { IsExact: true } otherFloat)
                return AsFloat().Power(otherFloat);
            if (other is not PyInt otherInt)
                return PyNotImplemented.Instance;
            Py_int_t exponent = otherInt.Value;
            if (exponent < 0)
            {
                // A negative exponent is float_pow of the two (0 ** -1 refuses there).
                if (Value.IsZero)
                    throw PyZeroDivisionError.Create("0.0 cannot be raised to a negative power");
                return new PyFloat(Math.Pow((double)Value, (double)exponent));
            }
            // 0, 1 and -1 stay where they are for any exponent.
            if (exponent.IsZero)
                return PyInt.From(1);
            if (Value.IsZero || Value.IsOne)
                return this;
            if (Value == BigInteger.MinusOne)
                return exponent.IsEven ? PyInt.From(1) : this;
            // Any other base past an int exponent has more digits than the host can hold.
            if (exponent > int.MaxValue)
                throw PyMemoryError.Create();
            return PyInt.From(BigInteger.Pow(Value, (int)exponent));
        }

        #endregion

        #region Bitwise Operations

        /// <summary>PyLong_Check: bool and int subclasses are ints, at any size.</summary>
        private static bool TryIntValue(PyObject other, out BigInteger value)
        {
            switch (other)
            {
                case PyBool truth:
                    value = truth.Value ? BigInteger.One : BigInteger.Zero;
                    return true;
                case PyInt number:
                    value = number.Value;
                    return true;
                default:
                    value = default;
                    return false;
            }
        }

        public override PyObject BitwiseAnd(PyObject other) =>
            TryIntValue(other, out var right) ? PyInt.From(Value & right) : PyNotImplemented.Instance;

        public override PyObject BitwiseOr(PyObject other) =>
            TryIntValue(other, out var right) ? PyInt.From(Value | right) : PyNotImplemented.Instance;

        public override PyObject BitwiseXor(PyObject other) =>
            TryIntValue(other, out var right) ? PyInt.From(Value ^ right) : PyNotImplemented.Instance;

        // MAX_LONG_DIGITS of 30-bit digits: what _PyLong_New refuses as "too many digits in integer".
        private static readonly BigInteger MaxLongBits = ((BigInteger)long.MaxValue - 24) / 4 * 30;

        // long_lshift: zero stays zero; divmod_shift clips the count, and _PyLong_New refuses a
        // result past MAX_LONG_DIGITS. One the host cannot hold is the MemoryError of the allocation.
        public override PyObject LeftShift(PyObject other)
        {
            if (!TryIntValue(other, out var shift))
                return PyNotImplemented.Instance;
            if (shift < 0)
                throw PyValueError.Create("negative shift count");
            if (Value.IsZero)
                return PyInt.From(0);
            if (shift + (BigInteger)Value.GetBitLength() > MaxLongBits)
                throw PyOverflowError.Create("too many digits in integer");
            if (shift > int.MaxValue)
                throw PyMemoryError.Create();
            return PyInt.From(Value << (int)shift);
        }

        // long_rshift: a shift past every bit leaves 0, or -1 for a negative value.
        public override PyObject RightShift(PyObject other)
        {
            if (!TryIntValue(other, out var shift))
                return PyNotImplemented.Instance;
            if (shift < 0)
                throw PyValueError.Create("negative shift count");
            if (Value.IsZero)
                return PyInt.From(0);
            if (shift > int.MaxValue)
                return PyInt.From(Value.Sign < 0 ? BigInteger.MinusOne : BigInteger.Zero);
            return PyInt.From(Value >> (int)shift);
        }

        #endregion

        #region Unary Operations

        protected internal override PyObject NbNegative() => PyInt.From(-Value);
        protected internal override PyObject NbPositive() => this;
        public PyObject Absolute() => PyInt.From(BigInteger.Abs(Value));
        protected internal override PyObject NbInvert() => PyInt.From(~Value);

        #endregion

        #region Type Conversion (CPython Compatible)

        // === To* Methods: Value Extraction (PyObject → C# basic types) ===
        
        /// <summary>
        /// CPython PyLong_AsLong 호환: PyInt에서 C# int 값 추출
        /// Used for array indexing and size operations
        /// Reference: Objects/longobject.c::PyLong_AsLong
        /// </summary>
        public override int ToInt()
        {
            if (Value > int.MaxValue || Value < int.MinValue)
            {
                throw PyOverflowError.Create("int too large to convert to int32");
            }
            return (int)Value;
        }
        
        /// <summary>
        /// CPython PyLong_AsDouble 호환: PyInt에서 C# double 값 추출
        /// Reference: Objects/longobject.c::PyLong_AsDouble
        /// </summary>
        // PyLong_AsDouble (Objects/longobject.c): correctly rounded, and an error rather than infinity.
        public override double ToFloat()
        {
            const long Exact = 1L << 53;
            if (Value < Exact && Value > -Exact) return (double)Value;
            try
            {
                return ((PyFloat)BigIntTrueDivide(Value, BigInteger.One)).Value;
            }
            catch (PythonException)
            {
                throw PyOverflowError.Create("int too large to convert to float");
            }
        }
        
        /// <summary>
        /// CPython PyObject_IsTrue 호환: PyInt에서 C# bool 값 추출
        /// </summary>
        
        // === As* Methods: Type Conversion (PyInt → PyObject types) ===
        
        /// <summary>
        /// CPython 호환: PyInt를 PyInt로 변환 (자기 자신 반환)
        /// </summary>
        // long_abs
        protected internal override PyObject NbAbsolute() => PyInt.From(BigInteger.Abs(Value));

        // long_long: the int itself, a subclass's value as an int.
        protected internal override PyObject NbInt() => this;
        
        /// <summary>
        /// CPython 호환: PyInt를 PyFloat로 변환
        /// </summary>
        /// <summary>
        /// CPython int_round_impl (Objects/longobject.c): round a Python int to 10^ndigits
        /// (ndigits &lt; 0) with round-half-even: 1234 → 1200, 1250 → 1200, 1350 → 1400.
        /// </summary>
        public static BigInteger RoundIntToPowerOfTen(BigInteger value, long digitsAbs)
        {
            int k = digitsAbs > 100000 ? 100000 : (int)digitsAbs;
            BigInteger p10 = BigInteger.Pow(10, k);
            BigInteger q = BigInteger.DivRem(value, p10, out BigInteger r);
            if (!r.IsZero)
            {
                int cmp = (BigInteger.Abs(r) << 1).CompareTo(p10);
                if (cmp > 0 || (cmp == 0 && !q.IsEven))
                    q += value.Sign;
            }
            return q * p10;
        }

        // long_float: PyLong_AsDouble.
        protected internal override PyObject NbFloat() => new PyFloat(ToFloat());

        /// <summary>PyLong_FromDouble: the integral part; infinity and NaN have none.</summary>
        internal static PyInt FromDouble(double value)
        {
            if (double.IsInfinity(value))
                throw PyOverflowError.Create("cannot convert float infinity to integer");
            if (double.IsNaN(value))
                throw PyValueError.Create("cannot convert float NaN to integer");
            return From(new BigInteger(value));
        }

        /// <summary>
        /// CPython long_true_divide: int/int produces a correctly rounded double; when the
        /// quotient overflows it raises OverflowError instead of returning infinity.
        /// Measured: 10 ** 400 / 1 → OverflowError: integer division result too large for a float
        /// while 10 ** 400 / 10 ** 400 → 1.0.
        /// </summary>
        internal static PyObject BigIntTrueDivide(BigInteger a, BigInteger b)
        {
            // long_true_divide (Objects/longobject.c): a quotient of 55 or 56 bits plus a sticky bit for the
            // remainder, rounded half-even to the 53 bits a double has (fewer in the subnormal range).
            bool negative = (a.Sign < 0) ^ (b.Sign < 0);
            var numerator = BigInteger.Abs(a);
            var denominator = BigInteger.Abs(b);
            if (numerator.IsZero) return new PyFloat(negative ? -0.0 : 0.0);

            int shift = 55 - (int)(numerator.GetBitLength() - denominator.GetBitLength());
            var quotient = shift >= 0
                ? BigInteger.DivRem(numerator << shift, denominator, out var remainder)
                : BigInteger.DivRem(numerator, denominator << -shift, out remainder);
            ulong bits = (ulong)quotient;
            int width = 64 - System.Numerics.BitOperations.LeadingZeroCount(bits);
            int exponent = width - shift;
            int excess = Math.Max(width - 53, width - (exponent + 1074));
            if (excess >= 64) return new PyFloat(negative ? -0.0 : 0.0);

            ulong half = 1UL << (excess - 1);
            ulong dropped = bits & ((half << 1) - 1);
            ulong kept = bits >> excess;
            if (dropped > half || (dropped == half && (!remainder.IsZero || (kept & 1) == 1))) kept++;
            double value = Math.ScaleB((double)kept, excess - shift);
            if (double.IsInfinity(value))
                throw PyOverflowError.Create("integer division result too large for a float");
            return new PyFloat(negative ? -value : value);
        }
        
        /// <summary>
        /// CPython 호환: PyInt를 PyBool로 변환
        /// </summary>
        public override PyBool AsBool()
        {
            return PyBool.FromBool(Value != 0);
        }
        
        /// <summary>
        /// CPython 호환: PyInt를 PyString으로 변환
        /// </summary>
        public override string ToClrString()
        {
            return DecimalString();
        }

        #endregion

        #region Number Base Methods

        /// <summary>
        /// Convert BigInteger to binary string
        /// CPython 3.12: Objects/longobject.c:long_format_binary
        /// </summary>
        public PyString Bin()
        {
            if (Value == 0) return PyString.From("0b0");

            var absValue = BigInteger.Abs(Value);
            var binaryStr = ConvertToBase(absValue, 2);

            return Value < 0
                ? PyString.From("-0b" + binaryStr)
                : PyString.From("0b" + binaryStr);
        }

        /// <summary>
        /// Convert BigInteger to octal string
        /// CPython 3.12: Objects/longobject.c:long_format
        /// </summary>
        public PyString Oct()
        {
            if (Value == 0) return PyString.From("0o0");

            var absValue = BigInteger.Abs(Value);
            var octalStr = ConvertToBase(absValue, 8);

            return Value < 0
                ? PyString.From("-0o" + octalStr)
                : PyString.From("0o" + octalStr);
        }

        /// <summary>
        /// Convert BigInteger to hexadecimal string
        /// CPython 3.12: Objects/longobject.c:long_format
        /// </summary>
        public PyString Hex()
        {
            if (Value == 0) return PyString.From("0x0");

            var absValue = BigInteger.Abs(Value);
            // BigInteger.ToString("x") provides lowercase hex without prefix
            // Note: C# "x" format may add leading zero for even-length output, we need to trim it
            var hexStr = absValue.ToString("x").TrimStart('0');
            if (string.IsNullOrEmpty(hexStr)) hexStr = "0";

            return Value < 0
                ? PyString.From("-0x" + hexStr)
                : PyString.From("0x" + hexStr);
        }

        /// <summary>
        /// Helper: Convert BigInteger to string in arbitrary base (2-36)
        /// CPython 3.12: Objects/longobject.c:long_format
        /// </summary>
        private static string ConvertToBase(BigInteger value, int baseValue)
        {
            if (value == 0) return "0";

            if (baseValue is 2 or 8 or 16)
            {
                int shift = baseValue == 2 ? 1 : baseValue == 8 ? 3 : 4;
                int count = checked((int)((value.GetBitLength() + shift - 1) / shift));
                var bytes = value.ToByteArray(isUnsigned: true);
                var output = new char[count];
                for (int i = count - 1, bit = 0; i >= 0; i--, bit += shift)
                {
                    int index = bit >> 3, offset = bit & 7;
                    int digit = bytes[index] >> offset;
                    if (offset + shift > 8 && index + 1 < bytes.Length)
                        digit |= bytes[index + 1] << (8 - offset);
                    output[i] = "0123456789abcdef"[digit & (baseValue - 1)];
                }
                return new string(output);
            }

            var digits = "0123456789abcdefghijklmnopqrstuvwxyz";
            var result = new System.Text.StringBuilder();

            while (value > 0)
            {
                var remainder = (int)(value % baseValue);
                result.Insert(0, digits[remainder]);
                value /= baseValue;
            }

            return result.ToString();
        }

        #endregion

        #region Bit Manipulation

        /// <summary>
        /// int.bit_length() - 이진 표현에서 부호 비트를 제외한 비트 수
        /// </summary>
        public PyInt BitLength()
        {
            if (Value == 0) return PyInt.From(0);
            var abs = BigInteger.Abs(Value);
            return PyInt.From((int)Math.Floor(Math.Log((double)abs, 2)) + 1);
        }

        /// <summary>long_bit_count: of the absolute value, at any size.</summary>
        public PyInt BitCount()
        {
            int count = 0;
            foreach (var part in System.Numerics.BigInteger.Abs(Value).ToByteArray())
                count += System.Numerics.BitOperations.PopCount(part);
            return PyInt.From(count);
        }

        #endregion

        #region Static Factory Methods

        internal static PyInt FromArguments(PyObject[] args, PyDict kwargs, int offset = 0)
        {
            int count = args.Length - offset;
            int keywords = kwargs?.Length() ?? 0;
            if (count > 2 || count + keywords > 2)
                throw PyTypeError.Create($"int() takes at most 2 arguments ({count + keywords} given)");
            PyObject radix = count > 1 ? args[offset + 1] : null;
            if (keywords != 0)
            {
                foreach (var pair in kwargs.GetInternalDict())
                {
                    if (pair.Key is not PyString key || key.Value != "base")
                        throw PyTypeError.Create($"'{pair.Key.ToStr().Value}' is an invalid keyword argument for int()");
                    radix = pair.Value;
                }
            }
            if (count == 0)
            {
                if (radix != null) throw PyTypeError.Create("int() missing string argument");
                return PyInt.From(0);
            }
            var value = args[offset];
            int baseValue = 10;
            if (radix != null)
            {
                var index = IndexOrThrow(radix);
                if (index.Value != 0 && (index.Value < 2 || index.Value > 36))
                    throw PyValueError.Create("int() base must be >= 2 and <= 36, or 0");
                baseValue = (int)index.Value;
            }
            // long_new_impl: without a base, PyNumber_Long; with one, a str, bytes or bytearray.
            if (radix == null)
                return NumberLong(value);
            return ParseText(value, baseValue) ?? throw PyTypeError.Create("int() can't convert non-string with explicit base");
        }

        // The text forms of long_new_impl and PyNumber_Long: a str, bytes or bytearray.
        private static PyInt ParseText(PyObject value, int baseValue) => value switch
        {
            PyString text => FromString(text.Value, baseValue),
            PyBytes bytes => FromString(System.Text.Encoding.Latin1.GetString(bytes.Value), baseValue, false, value),
            PyByteArray buffer => FromString(System.Text.Encoding.Latin1.GetString(buffer.BufferSpan), baseValue, false, value),
            _ => null,
        };

        /// <summary>
        /// PyNumber_Long (Objects/abstract.c): an exact int is itself; then nb_int, whose answer
        /// must be an int (_PyLong_FromNbInt); nb_index; the deprecated __trunc__; the text of a
        /// str, bytes or bytearray; the bytes of a buffer.
        /// </summary>
        internal static PyInt NumberLong(PyObject value)
        {
            if (value.IsExactly(PyType.IntType))
                return (PyInt)value;
            var result = value.TpNbInt();
            if (result != null)
                return FromNbInt(result, "__int__");
            if (value.HasTypeSlot("__index__"))
                return PyObject.IndexOrThrow(value);
            var truncate = value.LookupSpecialMethodUncached("__trunc__");
            if (truncate != null)
            {
                PyWarnings.Deprecated("The delegation of int() to __trunc__ is deprecated.");
                var whole = value.BindSpecialMethod(truncate).Call(Array.Empty<PyObject>(), null);
                if (PyInt.LongCheck(whole))
                    return PyObject.TryGetIndex(whole);
                if (!whole.HasTypeSlot("__index__"))
                    throw PyTypeError.Create($"__trunc__ returned non-Integral (type {whole.GetPyType().FullName})");
                return PyObject.IndexOrThrow(whole);
            }
            var parsed = ParseText(value, 10);
            if (parsed != null)
                return parsed;
            if (value.SupportsBuffer() && BytesMethods.Buffer(value) is { } exported)
                return FromString(System.Text.Encoding.Latin1.GetString(exported), 10, false, value);
            throw PyTypeError.Create(
                $"int() argument must be a string, a bytes-like object or a real number, not '{value.GetPyType().FullName}'");
        }

        // _PyLong_FromNbInt: an exact int, or a strict subclass with a DeprecationWarning.
        private static PyInt FromNbInt(PyObject result, string name)
        {
            if (result.IsExactly(PyType.IntType))
                return (PyInt)result;
            if (PyInt.LongCheck(result))
            {
                PyWarnings.Deprecated($"{name} returned non-int (type {result.GetPyType().FullName}).  The ability to return an instance of a strict subclass of int is deprecated, and may be removed in a future version of Python.");
                return PyObject.TryGetIndex(result);
            }
            throw PyTypeError.Create($"{name} returned non-int (type {result.GetPyType().FullName})");
        }

        internal static BigInteger FloatToIntegral(double d)
        {
            if (double.IsNaN(d))
                throw PyValueError.Create("cannot convert float NaN to integer");
            if (double.IsInfinity(d))
                throw PyOverflowError.Create("cannot convert float infinity to integer");
            return new BigInteger(d);
        }

        /// <summary>
        /// Convert string to PyInt with optional base.
        /// CPython 3.12: Objects/longobject.c - PyLong_FromString() lines 2500-2700
        /// </summary>
        public static PyInt FromString(string original, int baseValue = 10, bool unicode = true, PyObject literal = null)
        {
            if (baseValue != 0 && (baseValue < 2 || baseValue > 36))
                throw PyValueError.Create("int() base must be >= 2 and <= 36, or 0");
            int requestedBase = baseValue;
            string text = original;
            // PyUnicode_TransformDecimalAndSpaceToASCII. Allocate only for
            // non-ASCII input; errors retain the original Unicode spelling.
            for (int i = 0; unicode && i < text.Length; i++)
            {
                if (text[i] < 128) continue;
                var normalized = new System.Text.StringBuilder(text.Length);
                for (int j = 0; j < text.Length; j++)
                {
                    char c = text[j];
                    if (c < 128) normalized.Append(c);
                    else if (char.IsWhiteSpace(c)) normalized.Append(' ');
                    else
                    {
                        int digit = System.Globalization.CharUnicodeInfo.GetDecimalDigitValue(text, j);
                        if (digit < 0) normalized.Append(c);
                        else
                        {
                            normalized.Append((char)('0' + digit));
                            if (char.IsHighSurrogate(c) && j + 1 < text.Length && char.IsLowSurrogate(text[j + 1])) j++;
                        }
                    }
                }
                text = normalized.ToString();
                break;
            }
            int start = 0, end = text.Length;
            while (start < end && IsIntegerSpace(text[start])) start++;
            while (end > start && IsIntegerSpace(text[end - 1])) end--;
            bool negative = false;
            if (start < end && (text[start] == '+' || text[start] == '-'))
                negative = text[start++] == '-';
            bool leadingZero = baseValue == 0 && start < end && text[start] == '0';
            if (start + 1 < end && text[start] == '0')
            {
                int prefixBase = text[start + 1] switch { 'x' or 'X' => 16, 'b' or 'B' => 2, 'o' or 'O' => 8, _ => 0 };
                if (prefixBase != 0 && (baseValue == 0 || baseValue == prefixBase))
                {
                    baseValue = prefixBase; start += 2; leadingZero = false;
                    if (start < end && text[start] == '_') start++;
                }
            }
            if (baseValue == 0) baseValue = 10;
            int digits = 0;
            bool previousDigit = false, underscores = false, nonzero = false;
            for (int i = start; i < end; i++)
            {
                if (text[i] == '_')
                {
                    if (!previousDigit) throw InvalidIntegerLiteral(original, requestedBase, literal);
                    underscores = true; previousDigit = false; continue;
                }
                int digit = IntegerDigit(text[i]);
                if (digit < 0 || digit >= baseValue) throw InvalidIntegerLiteral(original, requestedBase, literal);
                previousDigit = true; digits++; nonzero |= digit != 0;
            }
            if (!previousDigit || (leadingZero && nonzero)) throw InvalidIntegerLiteral(original, requestedBase, literal);
            int limit = PyVM.Instance.IntMaxStrDigits;
            if ((baseValue & (baseValue - 1)) != 0 && limit != 0 && digits > limit)
                throw PyValueError.Create($"Exceeds the limit ({limit} digits) for integer string conversion: value has {digits} digits; use sys.set_int_max_str_digits() to increase the limit");
            BigInteger result;
            if (baseValue == 10)
            {
                ReadOnlySpan<char> span = text.AsSpan(start, end - start);
                if (underscores)
                {
                    var buffer = new char[digits];
                    int next = 0;
                    foreach (char c in span) if (c != '_') buffer[next++] = c;
                    span = buffer;
                }
                result = ulong.TryParse(span, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out ulong small)
                    ? new BigInteger(small)
                    : BigInteger.Parse(span, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                // Accumulate native chunks rather than allocating a BigInteger
                // product for every digit.
                uint factor = (uint)baseValue;
                int width = 1;
                while (factor <= uint.MaxValue / (uint)baseValue) { factor *= (uint)baseValue; width++; }
                uint chunk = 0, chunkFactor = 1;
                int count = 0;
                result = BigInteger.Zero;
                for (int i = start; i < end; i++)
                {
                    if (text[i] == '_') continue;
                    chunk = chunk * (uint)baseValue + (uint)IntegerDigit(text[i]);
                    chunkFactor *= (uint)baseValue;
                    if (++count == width)
                    {
                        result = result * factor + chunk;
                        chunk = 0; chunkFactor = 1; count = 0;
                    }
                }
                if (count != 0) result = result * chunkFactor + chunk;
            }
            return PyInt.From(negative ? -result : result);
        }

        private static bool IsIntegerSpace(char c) => c == ' ' || (c >= '\t' && c <= '\r');
        private static int IntegerDigit(char c) => c >= '0' && c <= '9' ? c - '0' :
            c >= 'a' && c <= 'z' ? c - 'a' + 10 : c >= 'A' && c <= 'Z' ? c - 'A' + 10 : -1;
        private static Exception InvalidIntegerLiteral(string text, int radix, PyObject literal = null)
        {
            if (literal is PyByteArray buffer) literal = PyBytes.From(buffer.BufferSpan.ToArray());
            return PyValueError.Create($"invalid literal for int() with base {radix}: {(literal ?? PyString.From(text)).ToRepr().Value}");
        }

        public static PyInt FromNumber(PyObject obj)
        {
            if (obj is PyInt pyInt)
                return pyInt;
            if (obj is PyFloat pyFloat)
                return PyInt.From((Py_int_t)Math.Truncate(pyFloat.Value));
            if (obj is PyBool pyBool)
                return PyInt.From(pyBool.Value ? 1 : 0);
            throw PyTypeError.Create($"int() argument must be a string, a bytes-like object or a real number, not '{obj.GetTypeName()}'");
        }

        #endregion

        #region Constants

        public static readonly PyInt Zero = PyInt.From(0);
        public static readonly PyInt One = PyInt.From(1);
        public static readonly PyInt MinusOne = PyInt.From(-1);

        #endregion

        #region Evaluate Method (NotImplementedException)

        public PyObject Evaluate(PyScope scope)
        {
            // Integer literals evaluate to themselves (CPython style)
            return this;
        }

        #endregion

        #region Special Methods


        #endregion

        #region Format Support

        /// <summary>
        /// CPython 3.12: Objects/longobject.c - Format an integer value according to format_spec
        /// Format spec mini-language: [[fill]align][sign][#][0][width][,][.precision][type]
        /// Type can be: b (binary), c (character), d (decimal), o (octal), x/X (hex), n (number), '' (same as d)
        /// </summary>
        public static string FormatInt(long value, string formatSpec) => FormatInt(new BigInteger(value), formatSpec);

        /// <param name="typeName">tp_name of the object formatted, which the refusals name.</param>
        public static string FormatInt(BigInteger value, string spec, string typeName = "int")
        {
            if (string.IsNullOrEmpty(spec)) return DecimalString(value);
            var format = FormatSpec.Parse(typeName, spec, 'd', '>');
            PyAllocation.StringLength(format.Width);
            string fill = format.Fill;
            char align = format.Align, sign = format.Sign, grouping = format.Thousands, type = format.TypeChar;
            bool alternate = format.Alternate, negativeZero = format.NoNegativeZero;
            int width = (int)System.Math.Max(0, format.Width);
            long precision = format.Precision;
            bool floating = "eEfFgG%".Contains(type);
            bool binary = "boxX".Contains(type);
            if (floating)
            {
                double number = (double)value;
                if (double.IsInfinity(number)) throw PyOverflowError.Create("int too large to convert to float");
                return PyFloat.FormatFloat(number, spec);
            }
            if (!"bcdonxX".Contains(type))
                throw FormatSpec.Unknown(format.Type, typeName);
            if (precision >= 0) throw PyValueError.Create("Precision not allowed in integer format specifier");
            if (negativeZero) throw PyValueError.Create("Negative zero coercion (z) not allowed in integer format specifier");

            string digits, prefix = "";
            int digitWidth;
            if (type == 'c')
            {
                if (sign != '\0') throw PyValueError.Create("Sign not allowed with integer format specifier 'c'");
                if (alternate) throw PyValueError.Create("Alternate form (#) not allowed with integer format specifier 'c'");
                // format_long_internal: PyLong_AsLong first, then the code point range.
                if (OperatingSystem.IsWindows() ? value < int.MinValue || value > int.MaxValue : value < long.MinValue || value > long.MaxValue)
                    throw PyOverflowError.Create("Python int too large to convert to C long");
                if (value < 0 || value > 0x10ffff) throw PyOverflowError.Create("%c arg not in range(0x110000)");
                int codepoint = (int)value;
                digits = codepoint <= 0xffff ? new string((char)codepoint, 1) : char.ConvertFromUtf32(codepoint);
                digitWidth = 1;
            }
            else
            {
                var magnitude = BigInteger.Abs(value);
                digits = binary ? ConvertToBase(magnitude, type == 'b' ? 2 : type == 'o' ? 8 : 16) : DecimalString(magnitude);
                if (type == 'X') digits = digits.ToUpperInvariant();
                prefix = value.Sign < 0 ? "-" : sign == '+' ? "+" : sign == ' ' ? " " : "";
                if (alternate && binary) prefix += type == 'b' ? "0b" : type == 'o' ? "0o" : type == 'X' ? "0X" : "0x";
                int groupSize = binary ? 4 : 3;
                if (grouping != '\0')
                {
                    int minDigits = digits.Length;
                    if (fill == "0" && align == '=')
                    {
                        int wanted = Math.Max(0, width - prefix.Length);
                        minDigits = Math.Max(minDigits, wanted - Math.Max(0, wanted - 1) / (groupSize + 1));
                    }
                    int groupedLength = checked(minDigits + (minDigits - 1) / groupSize);
                    var grouped = new char[groupedLength];
                    for (int dst = groupedLength - 1, src = digits.Length - 1, count = 0; dst >= 0; count++)
                    {
                        if (count != 0 && count % groupSize == 0) grouped[dst--] = grouping;
                        grouped[dst--] = src >= 0 ? digits[src--] : '0';
                    }
                    digits = new string(grouped);
                }
                digitWidth = digits.Length;
            }
            int padding = Math.Max(0, width - prefix.Length - digitWidth);
            if (padding == 0) return prefix + digits;
            return string.Create(checked(prefix.Length + digits.Length + padding * fill.Length),
                (prefix, digits, fill, align, padding), static (output, state) => {
                    int cursor = 0;
                    int before = state.align == '>' ? state.padding : state.align == '^' ? state.padding / 2 : 0;
                    int middle = state.align == '=' ? state.padding : 0;
                    Fill(output.Slice(cursor, before * state.fill.Length), state.fill);
                    cursor += before * state.fill.Length;
                    state.prefix.AsSpan().CopyTo(output.Slice(cursor)); cursor += state.prefix.Length;
                    Fill(output.Slice(cursor, middle * state.fill.Length), state.fill);
                    cursor += middle * state.fill.Length;
                    state.digits.AsSpan().CopyTo(output.Slice(cursor)); cursor += state.digits.Length;
                    Fill(output.Slice(cursor), state.fill);
                });
            static void Fill(Span<char> output, string text)
            {
                if (text.Length == 1) output.Fill(text[0]);
                else for (int n = 0; n < output.Length; n += text.Length) text.AsSpan().CopyTo(output.Slice(n));
            }
        }

        #endregion
    }
}
