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
        static PyInt()
        {
            InitializeIntDescriptors();
        }

        /// <summary>
        /// Initialize int type descriptors (CPython 3.12 compatible)
        /// CPython reference: Objects/longobject.c:5800-5900 - long_methods
        /// </summary>
        public static void InitializeIntDescriptors()
        {
            var intType = PyType.IntType;

            // CPython 3.12: Objects/longobject.c:5598-5605, 5652-5684 - long_new / long_subtype_new
            // __new__(cls, x=0, /) - Create a new int object
            // For int subclasses (IntEnum, IntFlag), this must preserve the custom type
            // Note: __new__ is special - 'self' parameter is actually 'cls' (the type)
            // CPython: tp_new slot, wrapped by WRAP_NEW macro (Objects/typeobject.c)
            intType.TypeDict["__new__"] = new PyMethodDescriptor(
                "__new__", intType,
                (self, args, kwargs) => {
                    // CPython: __new__ is special: 'self' is the class (cls), not an instance
                    // When called as: int.__new__(RegexFlag, 64)
                    // PyMethodDescriptor extracts first arg as 'self', so:
                    //   self = RegexFlag (the class)
                    //   args = [64] (remaining arguments)
                    PyType cls = self as PyType ?? (self as PyClass);
                    if (cls == null)
                        throw PyTypeError.Create($"__new__() argument 1 must be a type, not '{self.GetTypeName()}'");

                    // First arg in 'args' (if present) is the value
                    // Reference: Objects/longobject.c::long_new
                    long value = 0;
                    if (args.Length > 0)
                    {
                        if (args[0] is PyInt intArg)
                            value = intArg.ToLong();
                        else if (args[0] is PyBool boolArg)
                            value = boolArg.Value ? 1 : 0;
                        else if (args[0] is PyString strArg)
                            value = long.Parse(strArg.Value);
                        else
                            throw PyTypeError.Create($"int() argument must be a string or a number, not '{args[0].GetTypeName()}'");
                    }

                    // CPython 3.12: Objects/longobject.c:5603-5605
                    // if (type != &PyLong_Type)
                    //     return long_subtype_new(type, x, obase);
                    // For int subclasses, create PyInt with custom type
                    // CPython: long_subtype_new allocates via type->tp_alloc which sets ob_type
                    if (cls != PyType.IntType)
                    {
                        // Subclass of int - preserve the custom type
                        return new PyInt(value, cls);
                    }
                    else
                    {
                        // Plain int - no custom type needed
                        return new PyInt(value);
                    }
                },
                minArgs: 0, maxArgs: 1  // After 'self' extraction: 0-1 args (value)
            );

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
                        throw PyTypeError.Create($"bit_length() takes no arguments ({args.Length} given)");
                    if (self is not PyInt intObj)
                        throw PyTypeError.Create($"descriptor 'bit_length' requires a 'int' object but received a '{self.GetTypeName()}'");

                    // Reference: Objects/longobject.c::long_bit_length
                    long value = intObj.ToLong();
                    // CPython 3.12: Objects/longobject.c:5642-5658
                    // For negative numbers, bit_length() returns the same as abs(n).bit_length()
                    if (value < 0)
                        value = -value; // Use absolute value

                    int bitLength = 0;
                    while (value > 0)
                    {
                        bitLength++;
                        value >>= 1;
                    }
                    return new PyInt(bitLength);
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
                        throw PyTypeError.Create($"bit_count() takes no arguments ({args.Length} given)");
                    if (self is not PyInt intObj)
                        throw PyTypeError.Create($"descriptor 'bit_count' requires a 'int' object but received a '{self.GetTypeName()}'");

                    // Reference: Objects/longobject.c::long_bit_count
                    long value = intObj.ToLong();
                    if (value < 0)
                        value = -value;

                    int count = 0;
                    while (value > 0)
                    {
                        count += (int)(value & 1);
                        value >>= 1;
                    }
                    return new PyInt(count);
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/longobject.c:5678-5750 - int_to_bytes
            // Return an array of bytes representing an integer.
            intType.TypeDict["to_bytes"] = new PyMethodDescriptor(
                "to_bytes", intType,
                (self, args, kwargs) => {
                    if (self is not PyInt intObj)
                        throw PyTypeError.Create($"descriptor 'to_bytes' requires a 'int' object but received a '{self.GetTypeName()}'");

                    if (args.Length < 1 || args.Length > 2)
                        throw PyTypeError.Create($"to_bytes expected 1 to 2 arguments, got {args.Length}");

                    // Get length argument
                    if (args[0] is not PyInt lengthInt)
                        throw PyTypeError.Create($"'length' must be an int, not '{args[0].GetTypeName()}'");
                    int length = (int)lengthInt.Value;

                    // Get byteorder argument (default 'big')
                    string byteorder = "big";
                    if (args.Length >= 2)
                    {
                        if (args[1] is not PyString byteorderStr)
                            throw PyTypeError.Create($"'byteorder' must be a str, not '{args[1].GetTypeName()}'");
                        byteorder = byteorderStr.Value;
                    }

                    if (byteorder != "big" && byteorder != "little")
                        throw PyValueError.Create($"byteorder must be either 'little' or 'big'");

                    // CPython 3.12: Objects/longobject.c:5912-6010 - long_to_bytes
                    long value = (long)intObj.Value;
                    bool isNegative = value < 0;
                    if (isNegative)
                        value = -value;

                    byte[] bytes = new byte[length];
                    for (int i = 0; i < length; i++)
                    {
                        int index = byteorder == "big" ? length - 1 - i : i;
                        bytes[index] = (byte)(value & 0xFF);
                        value >>= 8;
                    }

                    // For negative numbers, convert to two's complement
                    if (isNegative)
                    {
                        for (int i = 0; i < length; i++)
                            bytes[i] = (byte)~bytes[i];

                        // Add 1 for two's complement
                        int carry = 1;
                        for (int i = (byteorder == "big" ? length - 1 : 0);
                             byteorder == "big" ? i >= 0 : i < length;
                             i += (byteorder == "big" ? -1 : 1))
                        {
                            int sum = bytes[i] + carry;
                            bytes[i] = (byte)(sum & 0xFF);
                            carry = sum >> 8;
                            if (carry == 0) break;
                        }
                    }

                    return new PyBytes(bytes);
                },
                minArgs: 1, maxArgs: 2
            );

            // CPython 3.12: Objects/longobject.c:5752-5820 - int_from_bytes (classmethod)
            // Return the integer represented by the given array of bytes.
            var fromBytesMethod = new PyMethodDescriptor(
                "from_bytes", intType,
                (self, args, kwargs) => {
                    // self is the class (PyType) when called as classmethod
                    if (args.Length < 1 || args.Length > 2)
                        throw PyTypeError.Create($"from_bytes() takes from 1 to 2 positional arguments but {args.Length} were given");

                    // Get bytes argument
                    if (args[0] is not PyBytes bytesObj)
                        throw PyTypeError.Create($"'bytes' must be a bytes object, not '{args[0].GetTypeName()}'");

                    // Get byteorder argument (default 'big')
                    string byteorder = "big";
                    if (args.Length >= 2)
                    {
                        if (args[1] is not PyString byteorderStr)
                            throw PyTypeError.Create($"'byteorder' must be a str, not '{args[1].GetTypeName()}'");
                        byteorder = byteorderStr.Value;
                    }

                    if (byteorder != "big" && byteorder != "little")
                        throw PyValueError.Create($"byteorder must be either 'little' or 'big'");

                    byte[] bytes = bytesObj.Value;
                    long result = 0;
                    bool isNegative = bytes.Length > 0 && (bytes[byteorder == "big" ? 0 : bytes.Length - 1] & 0x80) != 0;

                    if (isNegative)
                    {
                        // Two's complement for negative numbers
                        byte[] complemented = new byte[bytes.Length];
                        Array.Copy(bytes, complemented, bytes.Length);

                        // Subtract 1
                        int borrow = 1;
                        for (int i = (byteorder == "big" ? bytes.Length - 1 : 0);
                             byteorder == "big" ? i >= 0 : i < bytes.Length;
                             i += (byteorder == "big" ? -1 : 1))
                        {
                            int diff = complemented[i] - borrow;
                            complemented[i] = (byte)(diff & 0xFF);
                            borrow = diff < 0 ? 1 : 0;
                            if (borrow == 0) break;
                        }

                        // Invert bits
                        for (int i = 0; i < bytes.Length; i++)
                            complemented[i] = (byte)~complemented[i];

                        bytes = complemented;
                    }

                    for (int i = 0; i < bytes.Length; i++)
                    {
                        int index = byteorder == "big" ? i : bytes.Length - 1 - i;
                        result = (result << 8) | bytes[index];
                    }

                    return new PyInt(isNegative ? -result : result);
                },
                minArgs: 1, maxArgs: 2
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
                        throw PyTypeError.Create($"as_integer_ratio() takes no arguments ({args.Length} given)");
                    if (self is not PyInt intObj)
                        throw PyTypeError.Create($"descriptor 'as_integer_ratio' requires a 'int' object but received a '{self.GetTypeName()}'");

                    // For integers, the ratio is simply (self, 1)
                    return TupleCache.CreatePair(intObj, new PyInt(1));
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/longobject.c - int.conjugate method
            // Return self, the complex conjugate of any int.
            intType.TypeDict["conjugate"] = new PyMethodDescriptor(
                "conjugate", intType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"conjugate() takes no arguments ({args.Length} given)");
                    if (self is not PyInt intObj)
                        throw PyTypeError.Create($"descriptor 'conjugate' requires a 'int' object but received a '{self.GetTypeName()}'");

                    return intObj;  // For integers, conjugate is the number itself
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/longobject.c - int.real property
            // Return the real part of the int (which is the int itself).
            intType.TypeDict["real"] = new PyGetSetDescriptor(
                "real", intType,
                getter: self => {
                    if (self is not PyInt)
                        throw PyTypeError.Create($"descriptor 'real' for 'int' objects doesn't apply to a '{self.GetTypeName()}' object");
                    return self;  // For int, real is itself
                }
            );

            // CPython 3.12: Objects/longobject.c - int.imag property
            // Return the imaginary part of the int (which is 0).
            intType.TypeDict["imag"] = new PyGetSetDescriptor(
                "imag", intType,
                getter: self => {
                    if (self is not PyInt)
                        throw PyTypeError.Create($"descriptor 'imag' for 'int' objects doesn't apply to a '{self.GetTypeName()}' object");
                    return new PyInt(0);  // For int, imaginary part is 0
                }
            );

            // CPython 3.12: Objects/longobject.c - int.numerator property
            // The numerator of a rational number in lowest terms (returns self).
            intType.TypeDict["numerator"] = new PyGetSetDescriptor(
                "numerator", intType,
                getter: self => {
                    if (self is not PyInt)
                        throw PyTypeError.Create($"descriptor 'numerator' for 'int' objects doesn't apply to a '{self.GetTypeName()}' object");
                    return self;  // For int, numerator is itself
                }
            );

            // CPython 3.12: Objects/longobject.c - int.denominator property
            // The denominator of a rational number in lowest terms (always 1 for integers).
            intType.TypeDict["denominator"] = new PyGetSetDescriptor(
                "denominator", intType,
                getter: self => {
                    if (self is not PyInt)
                        throw PyTypeError.Create($"descriptor 'denominator' for 'int' objects doesn't apply to a '{self.GetTypeName()}' object");
                    return new PyInt(1);  // For int, denominator is always 1
                }
            );

            // CPython 3.12: Objects/longobject.c:5822-5840 - long_format
            // int.__format__(format_spec) - Format the integer according to format_spec
            intType.TypeDict["__format__"] = new PyMethodDescriptor(
                "__format__", intType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"__format__() takes 1 positional argument ({args.Length} given)");
                    if (args[0] is not PyString specStr)
                        throw PyTypeError.Create($"__format__() argument 1 must be str, not {args[0].GetTypeName()}");

                    // CPython 3.12: Objects/longobject.c:5555-5640 - long__format__
                    // Support both PyInt and PyIntSubclass (for IntEnum, IntFlag, etc.)
                    long value;
                    if (self is PyInt intObj)
                        value = (long)intObj.Value;
                    else if (self is PyIntSubclass intSubclass)
                        value = (long)intSubclass.GetIntValue().Value;
                    else
                        throw PyTypeError.Create($"descriptor '__format__' requires a 'int' object but received a '{self.GetTypeName()}'");

                    return new PyString(FormatInt(value, specStr.Value));
                },
                minArgs: 1, maxArgs: 1
            );

            // CPython 3.12: Objects/longobject.c:5597-5641 (long_new_impl)
            // int.__new__(cls, x=0, base=10)
            intType.TypeDict["__new__"] = new PyStaticBuiltinMethod(
                "__new__",
                (args, kwargs) =>
                {
                    // args[0] is cls
                    if (args.Length < 1)
                        throw PyTypeError.Create("int.__new__(): not enough arguments");

                    PyType cls = args[0] as PyType;
                    if (cls == null && args[0] is PyClass pyClass)
                        cls = pyClass.GetPyType();
                    if (cls == null)
                        throw PyTypeError.Create("int.__new__(X): X is not a type object");

                    // CPython 3.12: Objects/longobject.c:5605-5640 - from_bytes
                    // Get the value argument (args[1] if present, default 0)
                    long value = 0;
                    if (args.Length >= 2 && args[1] != PyNone.Instance)
                    {
                        // CPython: Line 5605-5615 - convert x to int
                        if (args[1] is PyInt pyInt)
                            value = (long)pyInt.Value;
                        else if (args[1] is PyFloat pyFloat)
                            value = (long)Math.Truncate(pyFloat.Value);
                        else if (args[1] is PyBool pyBool)
                            value = pyBool.Value ? 1 : 0;
                        else if (args[1] is PyString pyStr)
                        {
                            // Handle base parameter if present
                            int baseValue = 10;
                            if (args.Length >= 3 && args[2] is PyInt baseInt)
                                baseValue = (int)baseInt.Value;

                            var parsed = PyInt.FromString(pyStr.Value, baseValue);
                            value = (long)parsed.Value;
                        }
                        else
                        {
                            // Try to convert to int
                            try
                            {
                                value = args[1].ToInt();
                            }
                            catch
                            {
                                throw PyTypeError.Create($"int() argument must be a string, a bytes-like object or a number, not '{args[1].GetTypeName()}'");
                            }
                        }
                    }

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
                            return new PyIntSubclass(classObj, value);
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
                        return new PyInt(value);
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

        // CPython 3.12: Objects/object.c:350 - Py_TYPE(op) returns ob_type
        // Uses PyObject's GetPyType() which returns _customType ?? PyType.IntType
        public override PyType GetPyType() => _customType ?? PyType.IntType;

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


        /// <summary>
        /// Convert BigInteger to decimal
        /// Used for precise arithmetic operations
        /// </summary>
        public decimal ToDecimal()
        {
            // BigInteger to decimal can throw if too large
            try
            {
                return (decimal)Value;
            }
            catch (OverflowException)
            {
                throw PyOverflowError.Create("int too large to convert to decimal");
            }
        }

        #endregion

        #region String Representation

        public override PyString ToStr() => new PyString(Value.ToString());
        public override PyString ToRepr() => new PyString(Value.ToString());

        #endregion

        #region Hash and Equality

        public override int ToHash() => Value.GetHashCode();

        protected override PyObject PyEquals(PyObject other)
        {
            return other switch
            {
                // CPython 3.12: Objects/longobject.c:3200-3250 - long_richcompare
                PyInt otherInt => PyBool.FromBool(Value == otherInt.Value),
                PyFloat otherFloat => PyBool.FromBool((double)Value == otherFloat.Value),
                PyBool otherBool => PyBool.FromBool(Value == (otherBool.Value ? 1 : 0)),
                _ => PyBool.False
            };
        }

        #endregion

        #region Comparison Operations

        protected override PyObject PyLess(PyObject other)
        {
            // CPython 3.12: Objects/longobject.c:3200-3250 - long_richcompare
            if (other is PyInt otherInt)
                return PyBool.FromBool(Value < otherInt.Value);
            if (other is PyFloat otherFloat)
                return PyBool.FromBool((double)Value < otherFloat.Value);
            if (other is PyBool otherBool)
                return PyBool.FromBool(Value < (otherBool.Value ? 1 : 0));

            // Return NotImplemented to allow other object's __gt__ to be tried
            return PyNotImplemented.Instance;
        }

        protected override PyObject PyLessEqual(PyObject other)
        {
            // CPython 3.12: Objects/longobject.c:3200-3250 - long_richcompare
            if (other is PyInt otherInt)
                return PyBool.FromBool(Value <= otherInt.Value);
            if (other is PyFloat otherFloat)
                return PyBool.FromBool((double)Value <= otherFloat.Value);
            if (other is PyBool otherBool)
                return PyBool.FromBool(Value <= (otherBool.Value ? 1 : 0));

            // Return NotImplemented to allow other object's __ge__ to be tried
            return PyNotImplemented.Instance;
        }

        protected override PyObject PyGreater(PyObject other)
        {
            // CPython 3.12: Objects/longobject.c:3200-3250 - long_richcompare
            if (other is PyInt otherInt)
                return PyBool.FromBool(Value > otherInt.Value);
            if (other is PyFloat otherFloat)
                return PyBool.FromBool((double)Value > otherFloat.Value);
            if (other is PyBool otherBool)
                return PyBool.FromBool(Value > (otherBool.Value ? 1 : 0));

            // Return NotImplemented to allow other object's __lt__ to be tried
            return PyNotImplemented.Instance;
        }

        protected override PyObject PyGreaterEqual(PyObject other)
        {
            // CPython 3.12: Objects/longobject.c:3200-3250 - long_richcompare
            // Try direct comparison first
            if (other is PyInt otherInt)
                return PyBool.FromBool(Value >= otherInt.Value);
            if (other is PyFloat otherFloat)
                return PyBool.FromBool((double)Value >= otherFloat.Value);
            if (other is PyBool otherBool)
                return PyBool.FromBool(Value >= (otherBool.Value ? 1 : 0));

            // CPython: If other type doesn't handle comparison, return NotImplemented
            // This allows the other object's __le__ method to be tried
            // Objects/longobject.c:3246 - Py_RETURN_NOTIMPLEMENTED
            return PyNotImplemented.Instance;
        }

        #endregion

        #region Arithmetic Operations

        public override PyObject Add(PyObject other)
        {
            // CPython 3.12: Objects/longobject.c (long_add), Objects/complexobject.c (complex_add)
            return other switch
            {
                PyInt otherInt => new PyInt(Value + otherInt.Value),
                PyFloat otherFloat => new PyFloat((double)Value + otherFloat.Value),
                PyBool otherBool => new PyInt(Value + (otherBool.Value ? 1 : 0)),
                PyComplex otherComplex => new PyComplex((double)Value + otherComplex.Real, otherComplex.Imag),
                _ => PyNotImplemented.Instance
            };
        }

        public override PyObject Subtract(PyObject other)
        {
            // CPython 3.12: Objects/longobject.c (long_sub), Objects/complexobject.c (complex_sub)
            return other switch
            {
                PyInt otherInt => new PyInt(Value - otherInt.Value),
                PyFloat otherFloat => new PyFloat((double)Value - otherFloat.Value),
                PyBool otherBool => new PyInt(Value - (otherBool.Value ? 1 : 0)),
                PyComplex otherComplex => new PyComplex((double)Value - otherComplex.Real, -otherComplex.Imag),
                _ => PyNotImplemented.Instance
            };
        }

        public override PyObject Multiply(PyObject other)
        {
            // CPython 3.12: Objects/longobject.c (long_mul), Objects/complexobject.c (complex_mul)
            return other switch
            {
                PyInt otherInt => new PyInt(Value * otherInt.Value),
                PyFloat otherFloat => new PyFloat((double)Value * otherFloat.Value),
                PyBool otherBool => new PyInt(Value * (otherBool.Value ? 1 : 0)),
                PyComplex otherComplex => new PyComplex((double)Value * otherComplex.Real, (double)Value * otherComplex.Imag),
                _ => PyNotImplemented.Instance
            };
        }

        public override PyObject Divide(PyObject other)
        {
            // CPython 3.12: Objects/longobject.c:2533-2570 - long_true_divide
            double otherValue;
            if (other is PyInt otherInt)
                otherValue = (double)otherInt.Value;
            else if (other is PyFloat otherFloat)
                otherValue = otherFloat.Value;
            else if (other is PyBool otherBool)
                otherValue = otherBool.Value ? 1.0 : 0.0;
            else
                return PyNotImplemented.Instance;

            if (otherValue == 0.0)
                throw PyZeroDivisionError.Create("division by zero");

            return new PyFloat((double)Value / otherValue);
        }

        public override PyObject FloorDivide(PyObject other)
        {
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

                return new PyInt(quotient);
            }
            if (other is PyFloat otherFloat)
            {
                // CPython 3.12: Objects/longobject.c:2453-2494 - long_div (floor division)
                if (otherFloat.Value == 0.0)
                    throw PyZeroDivisionError.Create("integer division or modulo by zero");
                return new PyFloat(Math.Floor((double)Value / otherFloat.Value));
            }
            if (other is PyBool otherBool)
            {
                if (!otherBool.Value)
                    throw PyZeroDivisionError.Create("integer division or modulo by zero");
                return new PyInt(Value);
            }
            return PyNotImplemented.Instance;
        }

        public override PyObject Modulo(PyObject other)
        {
            if (other is PyInt otherInt)
            {
                if (otherInt.Value == 0)
                    throw PyZeroDivisionError.Create("integer division or modulo by zero");

                // CPython: Objects/longobject.c:long_rem - Python modulo
                // Python modulo: result has same sign as divisor
                var remainder = Value % otherInt.Value;

                // Adjust for Python modulo: if signs differ and remainder != 0, add divisor
                if (remainder != 0 && ((Value < 0) != (otherInt.Value < 0)))
                {
                    remainder += otherInt.Value;
                }

                return new PyInt(remainder);
            }
            if (other is PyFloat otherFloat)
            {
                // CPython 3.12: Objects/longobject.c:2496-2531 - long_mod
                if (otherFloat.Value == 0.0)
                    throw PyZeroDivisionError.Create("float modulo");

                var remainder = (double)Value % otherFloat.Value;
                if (remainder != 0 && (((double)Value < 0) != (otherFloat.Value < 0)))
                {
                    remainder += otherFloat.Value;
                }

                return new PyFloat(remainder);
            }
            if (other is PyBool otherBool)
            {
                if (!otherBool.Value)
                    throw PyZeroDivisionError.Create("integer division or modulo by zero");
                return new PyInt(0);
            }
            return PyNotImplemented.Instance;
        }

        /// <summary>
        /// CPython 3.12: int.__divmod__ - divmod() operation
        /// CPython: Objects/longobject.c:long_divmod (lines 4509-4526)
        /// Returns tuple of (quotient, remainder) equivalent to (a // b, a % b)
        /// </summary>
        public override PyObject DivMod(PyObject other)
        {
            if (other is not PyInt otherInt)
                return PyNotImplemented.Instance;

            if (otherInt.Value == 0)
                throw PyZeroDivisionError.Create("integer division or modulo by zero");

            // CPython: Objects/longobject.c:4515-4516 - Call l_divmod which returns quotient and remainder
            // Use existing FloorDivide and Modulo to ensure consistency
            var quotient = FloorDivide(other);
            var remainder = Modulo(other);

            return new PyTuple(quotient, remainder);
        }

        public override PyObject Power(PyObject other)
        {
            // CPython 3.12: Objects/longobject.c:long_pow
            // int ** int → int (if exponent >= 0) or float (if exponent < 0)
            // int ** float → float
            // Negative base with non-integer exponent → complex (not implemented yet)

            if (other is PyInt otherInt)
            {
                Py_int_t exponent = otherInt.Value;

                if (exponent < 0)
                {
                    // int ** negative_int → float
                    // CPython 3.12: Objects/longobject.c:4319-4538 - long_pow
                    return new PyFloat(Math.Pow((double)Value, (double)exponent));
                }

                // int ** positive_int → int
                // CPython: Use BigInteger.Pow for arbitrary precision
                var result = BigInteger.Pow(Value, (int)exponent);
                return new PyInt(result);
            }
            else if (other is PyBool otherBool)
            {
                Py_int_t exponent = otherBool.Value ? 1 : 0;

                // 0 ** 0 = 1 (CPython behavior)
                if (exponent == 0)
                    return new PyInt(1);

                return new PyInt(Value);
            }
            else if (other is PyFloat otherFloat)
            {
                // int ** float → float
                // CPython: Objects/floatobject.c:float_pow
                double baseValue = (double)Value;
                double exponent = otherFloat.Value;

                // Edge case: negative base with non-integer exponent
                // CPython: Returns complex number (Objects/floatobject.c:float_pow)
                if (baseValue < 0 && Math.Floor(exponent) != exponent)
                {
                    // Return complex number: (-2) ** 0.5 → (8.66e-17+1.414j)
                    var complexBase = new PyComplex(baseValue, 0);
                    var complexExponent = new PyComplex(exponent, 0);
                    return complexBase.Power(complexExponent);
                }

                double result = Math.Pow(baseValue, exponent);
                return new PyFloat(result);
            }
            else
            {
                return PyNotImplemented.Instance;
            }
        }

        /// <summary>
        /// CPython 3.12: pow(base, exp, mod) - 3-argument modular exponentiation
        /// CPython: Objects/longobject.c:long_pow (lines 4395-4644)
        /// Efficiently computes (base ** exp) % mod using binary exponentiation
        /// </summary>
        public PyObject PowerMod(PyObject expObj, PyObject modObj)
        {
            // CPython 3.12: Objects/longobject.c:4409-4420 - Type checking
            if (expObj is not PyInt expInt || modObj is not PyInt modInt)
                throw PyTypeError.Create("pow() 3rd argument not allowed unless all arguments are integers");

            long baseVal = (long)Value;
            long exp = (long)expInt.Value;
            long mod = (long)modInt.Value;

            // CPython: Objects/longobject.c:4445-4447 - Zero modulus check
            if (mod == 0)
                throw PyValueError.Create("pow() 3rd argument cannot be 0");

            // CPython: Objects/longobject.c:4509-4519 - Negative exponent requires modular inverse
            if (exp < 0)
            {
                // Compute modular inverse using extended Euclidean algorithm
                // CPython implements this in Objects/longobject.c:l_invmod
                long inverse = ModularInverse(baseVal, mod);
                if (inverse == -1)
                    throw PyValueError.Create($"base is not invertible for the given modulus");

                baseVal = inverse;
                exp = -exp;
            }

            // CPython: Objects/longobject.c:4550-4600 - Binary exponentiation algorithm
            // Use efficient modular exponentiation: (base^exp) % mod
            long result = ModularPow(baseVal, exp, mod);

            // CPython: Python uses positive modulo (result always has same sign as modulus)
            if (result < 0 && mod > 0)
                result += mod;
            else if (result > 0 && mod < 0)
                result += mod;

            return new PyInt((int)result);
        }

        /// <summary>
        /// CPython 3.12: Modular exponentiation using binary method
        /// CPython: Objects/longobject.c:l_mod (inlined in long_pow)
        /// Computes (base^exp) % mod efficiently
        /// </summary>
        private long ModularPow(long baseVal, long exp, long mod)
        {
            if (mod == 1)
                return 0;

            // Normalize base to [0, mod)
            baseVal = baseVal % mod;
            if (baseVal < 0)
                baseVal += mod;

            long result = 1;

            // Binary exponentiation: O(log exp) instead of O(exp)
            while (exp > 0)
            {
                if ((exp & 1) == 1)
                {
                    result = (result * baseVal) % mod;
                }
                baseVal = (baseVal * baseVal) % mod;
                exp >>= 1;
            }

            return result;
        }

        /// <summary>
        /// CPython 3.12: Extended Euclidean algorithm for modular inverse
        /// CPython: Objects/longobject.c:l_invmod (lines 4344-4393)
        /// Computes x such that (a * x) % m = 1, or returns -1 if not invertible
        /// </summary>
        private long ModularInverse(long a, long m)
        {
            // Normalize to positive values
            long origM = m;
            m = Math.Abs(m);
            a = a % m;
            if (a < 0)
                a += m;

            // Extended Euclidean algorithm
            long m0 = m;
            long x0 = 0, x1 = 1;

            if (m == 1)
                return -1; // Not invertible

            while (a > 1)
            {
                if (m == 0)
                    return -1; // Not invertible (gcd(a, m) != 1)

                long q = a / m;
                long t = m;

                m = a % m;
                a = t;
                t = x0;

                x0 = x1 - q * x0;
                x1 = t;
            }

            if (a != 1)
                return -1; // Not invertible (gcd != 1)

            if (x1 < 0)
                x1 += m0;

            return x1;
        }

        #endregion

        #region Bitwise Operations

        public override PyObject BitwiseAnd(PyObject other)
        {
            // CPython 3.12: Objects/longobject.c:5336 (long_and)
            // Line 1547-1551: CHECK_BINOP macro
            // #define CHECK_BINOP(v,w) if (!PyLong_Check(v) || !PyLong_Check(w)) Py_RETURN_NOTIMPLEMENTED;

            // CPython 3.12: Include/longobject.h:12-13
            // PyLong_Check: Include/longobject.h:12-13
            // #define PyLong_Check(op) PyType_FastSubclass(Py_TYPE(op), Py_TPFLAGS_LONG_SUBCLASS)
            // This includes int subclasses (IntEnum, IntFlag, etc.)

            long otherValue;
            if (other is PyInt otherInt)
            {
                otherValue = (long)otherInt.Value;
            }
            else if (other is PyIntSubclass intSubclass)
            {
                // int subclass is also PyLong_Check compatible
                otherValue = (long)intSubclass.GetIntValue().Value;
            }
            else if (other is PyBool otherBool)
            {
                otherValue = otherBool.Value ? 1 : 0;
            }
            else
            {
                // CPython: Py_RETURN_NOTIMPLEMENTED for non-int types
                return PyNotImplemented.Instance;
            }

            return new PyInt(Value & otherValue);
        }

        public override PyObject BitwiseOr(PyObject other)
        {
            // CPython 3.12: Objects/longobject.c:5347 (long_or)
            // Line 1547-1551: CHECK_BINOP macro checks PyLong_Check for both operands

            long otherValue;
            if (other is PyInt otherInt)
            {
                otherValue = (long)otherInt.Value;
            }
            else if (other is PyIntSubclass intSubclass)
            {
                // int subclass passes PyLong_Check (Include/longobject.h:12-13)
                otherValue = (long)intSubclass.GetIntValue().Value;
            }
            else if (other is PyBool otherBool)
            {
                otherValue = otherBool.Value ? 1 : 0;
            }
            else
            {
                // CPython: Py_RETURN_NOTIMPLEMENTED for non-int types
                return PyNotImplemented.Instance;
            }

            return new PyInt(Value | otherValue);
        }

        public override PyObject BitwiseXor(PyObject other)
        {
            // CPython 3.12: Objects/longobject.c:5358 (long_xor)
            // Line 1547-1551: CHECK_BINOP macro checks PyLong_Check

            long otherValue;
            if (other is PyInt otherInt)
            {
                otherValue = (long)otherInt.Value;
            }
            else if (other is PyIntSubclass intSubclass)
            {
                // int subclass passes PyLong_Check
                otherValue = (long)intSubclass.GetIntValue().Value;
            }
            else if (other is PyBool otherBool)
            {
                otherValue = otherBool.Value ? 1 : 0;
            }
            else
            {
                // CPython: Py_RETURN_NOTIMPLEMENTED for non-int types
                return PyNotImplemented.Instance;
            }

            return new PyInt(Value ^ otherValue);
        }

        public override PyObject LeftShift(PyObject other)
        {
            if (!(other is PyInt otherInt))
                return PyNotImplemented.Instance;

            if (otherInt.Value < 0)
                throw PyValueError.Create("negative shift count");

            return new PyInt(Value << (int)otherInt.Value);
        }

        public override PyObject RightShift(PyObject other)
        {
            if (!(other is PyInt otherInt))
                return PyNotImplemented.Instance;

            if (otherInt.Value < 0)
                throw PyValueError.Create("negative shift count");

            return new PyInt(Value >> (int)otherInt.Value);
        }

        #endregion

        #region Unary Operations

        public override PyObject Negative() => new PyInt(-Value);
        public override PyObject Positive() => this;
        public PyObject Absolute() => new PyInt(BigInteger.Abs(Value));
        public override PyObject BitwiseNot() => new PyInt(~Value);

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
        public override double ToFloat() => (double)Value;
        
        /// <summary>
        /// CPython PyObject_IsTrue 호환: PyInt에서 C# bool 값 추출
        /// </summary>
        public override bool PyBoolValue() => Value != 0;
        
        // === As* Methods: Type Conversion (PyInt → PyObject types) ===
        
        /// <summary>
        /// CPython 호환: PyInt를 PyInt로 변환 (자기 자신 반환)
        /// </summary>
        public override PyInt AsInt()
        {
            return this; // 이미 PyInt이므로 자기 자신 반환
        }
        
        /// <summary>
        /// CPython 호환: PyInt를 PyFloat로 변환
        /// </summary>
        public override PyFloat AsFloat()
        {
            return new PyFloat((double)Value);
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
        public override string AsString()
        {
            return Value.ToString();
        }

        #endregion

        #region Number Base Methods

        /// <summary>
        /// Convert BigInteger to binary string
        /// CPython 3.12: Objects/longobject.c:long_format_binary
        /// </summary>
        public PyString Bin()
        {
            if (Value == 0) return new PyString("0b0");

            var absValue = BigInteger.Abs(Value);
            var binaryStr = ConvertToBase(absValue, 2);

            return Value < 0
                ? new PyString("-0b" + binaryStr)
                : new PyString("0b" + binaryStr);
        }

        /// <summary>
        /// Convert BigInteger to octal string
        /// CPython 3.12: Objects/longobject.c:long_format
        /// </summary>
        public PyString Oct()
        {
            if (Value == 0) return new PyString("0o0");

            var absValue = BigInteger.Abs(Value);
            var octalStr = ConvertToBase(absValue, 8);

            return Value < 0
                ? new PyString("-0o" + octalStr)
                : new PyString("0o" + octalStr);
        }

        /// <summary>
        /// Convert BigInteger to hexadecimal string
        /// CPython 3.12: Objects/longobject.c:long_format
        /// </summary>
        public PyString Hex()
        {
            if (Value == 0) return new PyString("0x0");

            var absValue = BigInteger.Abs(Value);
            // BigInteger.ToString("x") provides lowercase hex without prefix
            // Note: C# "x" format may add leading zero for even-length output, we need to trim it
            var hexStr = absValue.ToString("x").TrimStart('0');
            if (string.IsNullOrEmpty(hexStr)) hexStr = "0";

            return Value < 0
                ? new PyString("-0x" + hexStr)
                : new PyString("0x" + hexStr);
        }

        /// <summary>
        /// Helper: Convert BigInteger to string in arbitrary base (2-36)
        /// CPython 3.12: Objects/longobject.c:long_format
        /// </summary>
        private static string ConvertToBase(BigInteger value, int baseValue)
        {
            if (value == 0) return "0";

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
            if (Value == 0) return new PyInt(0);
            var abs = BigInteger.Abs(Value);
            return new PyInt((int)Math.Floor(Math.Log((double)abs, 2)) + 1);
        }

        /// <summary>
        /// int.bit_count() - 1인 비트의 개수
        /// </summary>
        public PyInt BitCount()
        {
            Py_int_t count = 0;
            var n = BigInteger.Abs(Value);
            while (n > 0)
            {
                count += (int)(n & 1);
                n >>= 1;
            }
            return new PyInt(count);
        }

        #endregion

        #region Static Factory Methods

        /// <summary>
        /// Convert string to PyInt with optional base.
        /// CPython 3.12: Objects/longobject.c - PyLong_FromString() lines 2500-2700
        /// </summary>
        public static PyInt FromString(string s, int baseValue = 10)
        {
            try
            {
                s = s.Trim();

                // Handle sign prefix (CPython: longobject.c line 2540)
                int sign = 1;
                if (s.Length > 0 && (s[0] == '+' || s[0] == '-'))
                {
                    if (s[0] == '-') sign = -1;
                    s = s.Substring(1);
                }

                // Handle base prefix (CPython: longobject.c lines 2550-2600)
                // When base is 0, auto-detect from prefix
                // When base is explicit, strip matching prefix if present
                if (s.Length >= 2 && s[0] == '0')
                {
                    char prefix = char.ToLower(s[1]);
                    if (prefix == 'x')
                    {
                        if (baseValue == 0 || baseValue == 16)
                        {
                            if (baseValue == 0) baseValue = 16;
                            s = s.Substring(2);
                        }
                    }
                    else if (prefix == 'b')
                    {
                        if (baseValue == 0 || baseValue == 2)
                        {
                            if (baseValue == 0) baseValue = 2;
                            s = s.Substring(2);
                        }
                    }
                    else if (prefix == 'o')
                    {
                        if (baseValue == 0 || baseValue == 8)
                        {
                            if (baseValue == 0) baseValue = 8;
                            s = s.Substring(2);
                        }
                    }
                }

                // Default base for auto-detect (CPython: longobject.c line 2610)
                if (baseValue == 0) baseValue = 10;

                // Handle empty string after prefix strip
                if (string.IsNullOrEmpty(s))
                {
                    throw PyValueError.Create($"invalid literal for int() with base {baseValue}: '{s}'");
                }

                // Handle underscore separators (CPython 3.6+: PEP 515)
                s = s.Replace("_", "");

                // Convert string to number (CPython: longobject.c lines 2620-2700)
                // .NET's Convert.ToInt64 only supports base 2, 8, 10, 16
                // For other bases, use custom conversion
                Py_int_t result;
                if (baseValue == 2 || baseValue == 8 || baseValue == 10 || baseValue == 16)
                {
                    result = Convert.ToInt64(s, baseValue);
                }
                else
                {
                    result = ConvertFromBase(s, baseValue);
                }
                result *= sign;
                return new PyInt(result);
            }
            catch
            {
                throw PyValueError.Create($"invalid literal for int() with base {baseValue}: '{s}'");
            }
        }

        /// <summary>
        /// Convert string to long for bases not supported by Convert.ToInt64.
        /// CPython 3.12: Objects/longobject.c lines 2620-2700
        /// </summary>
        private static Py_int_t ConvertFromBase(string s, int baseValue)
        {
            if (baseValue < 2 || baseValue > 36)
                throw new ArgumentException($"int() base must be >= 2 and <= 36, or 0");

            Py_int_t result = 0;
            foreach (char c in s)
            {
                int digit;
                if (c >= '0' && c <= '9')
                    digit = c - '0';
                else if (c >= 'a' && c <= 'z')
                    digit = c - 'a' + 10;
                else if (c >= 'A' && c <= 'Z')
                    digit = c - 'A' + 10;
                else
                    throw new FormatException($"Invalid character '{c}'");

                if (digit >= baseValue)
                    throw new FormatException($"Invalid digit '{c}' for base {baseValue}");

                result = result * baseValue + digit;
            }
            return result;
        }

        public static PyInt FromNumber(PyObject obj)
        {
            if (obj is PyInt pyInt)
                return pyInt;
            if (obj is PyFloat pyFloat)
                return new PyInt((Py_int_t)Math.Truncate(pyFloat.Value));
            if (obj is PyBool pyBool)
                return new PyInt(pyBool.Value ? 1 : 0);
            throw PyTypeError.Create($"int() argument must be a string, a bytes-like object or a number, not '{obj.GetTypeName()}'");
        }

        #endregion

        #region Constants

        public static readonly PyInt Zero = new PyInt(0);
        public static readonly PyInt One = new PyInt(1);
        public static readonly PyInt MinusOne = new PyInt(-1);

        #endregion

        #region Evaluate Method (NotImplementedException)

        public PyObject Evaluate(PyScope scope)
        {
            // Integer literals evaluate to themselves (CPython style)
            return this;
        }

        #endregion

        #region Special Methods

        /// <summary>
        /// CPython 3.12: __index__() returns self for int objects
        /// CPython 3.12: __complex__() returns complex(self, 0) for int objects
        /// </summary>
        public override PyObject GetAttribute(string name)
        {
            if (name == "__index__")
            {
                // Return a bound method that returns self
                return new PyBuiltinFunction("__index__", (args) => this);
            }
            if (name == "__complex__")
            {
                // Return a bound method that returns complex(self, 0)
                // CPython 3.12: Objects/longobject.c:5765 - long___complex___impl
                return new PyBuiltinFunction("__complex__", (args) => new PyComplex((double)Value, 0));
            }
            return base.GetAttribute(name);
        }

        #endregion

        #region Format Support

        /// <summary>
        /// CPython 3.12: Objects/longobject.c - Format an integer value according to format_spec
        /// Format spec mini-language: [[fill]align][sign][#][0][width][,][.precision][type]
        /// Type can be: b (binary), c (character), d (decimal), o (octal), x/X (hex), n (number), '' (same as d)
        /// </summary>
        public static string FormatInt(long value, string formatSpec)
        {
            if (string.IsNullOrEmpty(formatSpec))
                return value.ToString();

            // Parse format spec
            char fill = ' ';
            char align = '\0';  // '\0' means default
            char sign = '-';    // default: only negative
            bool alternate = false;
            bool zeropad = false;
            int width = 0;
            bool thousands = false;
            char type = 'd';    // default type

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

            // Skip precision for integers (not used)
            if (i < len && formatSpec[i] == '.')
            {
                i++;
                while (i < len && char.IsDigit(formatSpec[i]))
                    i++;
            }

            // Type
            if (i < len)
            {
                type = formatSpec[i];
                i++;
            }

            // Convert value based on type
            string result;
            string prefix = "";
            bool isNegative = value < 0;
            long absValue = Math.Abs(value);

            switch (type)
            {
                case 'b':  // binary
                    result = Convert.ToString(absValue, 2);
                    if (alternate) prefix = "0b";
                    break;
                case 'c':  // character
                    if (value < 0 || value > 0x10FFFF)
                        throw PyOverflowError.Create("%c arg not in range(0x110000)");
                    return char.ConvertFromUtf32((int)value);
                case 'd':  // decimal
                case 'n':  // number (same as d for integers)
                case '\0': // default
                    result = absValue.ToString();
                    if (thousands)
                        result = absValue.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case 'o':  // octal
                    result = Convert.ToString(absValue, 8);
                    if (alternate) prefix = "0o";
                    break;
                case 'x':  // hex lowercase
                    result = Convert.ToString(absValue, 16).ToLower();
                    if (alternate) prefix = "0x";
                    break;
                case 'X':  // hex uppercase
                    result = Convert.ToString(absValue, 16).ToUpper();
                    if (alternate) prefix = "0X";
                    break;
                default:
                    throw PyValueError.Create($"Unknown format code '{type}' for object of type 'int'");
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
            string fullPrefix = signStr + prefix;
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
                    return signStr + prefix + new string(fill, padLen) + result;
                case '^':  // centered
                    int leftPad = padLen / 2;
                    int rightPad = padLen - leftPad;
                    return new string(fill, leftPad) + fullPrefix + result + new string(fill, rightPad);
                default:
                    return fullPrefix + result;
            }
        }

        #endregion
    }
}