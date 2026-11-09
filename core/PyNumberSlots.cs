using System.Numerics;

namespace SharpPy
{
    /// <summary>
    /// The nb_* slots of int, float and complex (Objects/longobject.c, floatobject.c,
    /// complexobject.c): each reads both operands its own way (CHECK_BINOP, CONVERT_TO_DOUBLE,
    /// to_complex) and answers NotImplemented for what it cannot read. binary_op1 asks the left
    /// operand's slot, then the right one's; the slot wrappers __op__ and __rop__ (wrap_binaryfunc_l
    /// and _r) call the same slot with the operands in either order.
    /// </summary>
    internal static class PyNumberSlots
    {
        internal enum Kind { Long, Float, Complex }

        /// <summary>The number family whose slots the object's type has, if any.</summary>
        internal static Kind? KindOf(PyObject value) => value switch
        {
            PyBool or PyInt => Kind.Long,
            PyFloat => Kind.Float,
            PyComplex => Kind.Complex,
            _ => null,
        };

        // CHECK_BINOP: PyLong_Check, the value of a bool or a subclass.
        internal static PyInt AsLong(PyObject value) => value switch
        {
            PyBool truth => truth.Value ? PyInt.One : PyInt.Zero,
            PyIntSubclass number => number.GetIntValue(),
            PyInt number => number,
            _ => null,
        };

        // CONVERT_TO_DOUBLE: a float, or an int by PyLong_AsDouble (OverflowError past a double).
        private static PyFloat AsDouble(PyObject value) => value switch
        {
            PyFloatSubclass real => real.GetFloatValue(),
            PyFloat real => real,
            _ => AsLong(value) is { } number ? new PyFloat(number.ToFloat()) : null,
        };

        // to_complex: a complex, else what CONVERT_TO_DOUBLE reads.
        private static PyComplex AsComplex(PyObject value) => value switch
        {
            PyComplex number => number.IsExactly(PyType.ComplexType) ? number : new PyComplex(number.Real, number.Imag),
            _ => AsDouble(value) is { } real ? new PyComplex(real.Value, 0) : null,
        };

        /// <summary>The binary slot of the family for v op w.</summary>
        internal static PyObject Binary(Kind kind, BinaryOpType operation, PyObject v, PyObject w)
        {
            switch (kind)
            {
                case Kind.Long:
                {
                    var a = AsLong(v);
                    var b = AsLong(w);
                    if (a == null || b == null)
                        return PyNotImplemented.Instance;
                    return operation switch
                    {
                        BinaryOpType.ADD => a.Add(b),
                        BinaryOpType.SUBTRACT => a.Subtract(b),
                        BinaryOpType.MULTIPLY => a.Multiply(b),
                        BinaryOpType.TRUE_DIVIDE => a.Divide(b),
                        BinaryOpType.FLOOR_DIVIDE => a.FloorDivide(b),
                        BinaryOpType.MODULO => a.Modulo(b),
                        BinaryOpType.DIVMOD => a.DivMod(b),
                        BinaryOpType.POWER => a.Power(b),
                        BinaryOpType.LSHIFT => a.LeftShift(b),
                        BinaryOpType.RSHIFT => a.RightShift(b),
                        BinaryOpType.AND => a.BitwiseAnd(b),
                        BinaryOpType.OR => a.BitwiseOr(b),
                        BinaryOpType.XOR => a.BitwiseXor(b),
                        _ => PyNotImplemented.Instance,
                    };
                }
                case Kind.Float:
                {
                    if (!IsFloatOperation(operation))
                        return PyNotImplemented.Instance;
                    var a = AsDouble(v);
                    if (a == null)
                        return PyNotImplemented.Instance;
                    var b = AsDouble(w);
                    if (b == null)
                        return PyNotImplemented.Instance;
                    return operation switch
                    {
                        BinaryOpType.ADD => a.Add(b),
                        BinaryOpType.SUBTRACT => a.Subtract(b),
                        BinaryOpType.MULTIPLY => a.Multiply(b),
                        BinaryOpType.TRUE_DIVIDE => a.Divide(b),
                        BinaryOpType.FLOOR_DIVIDE => a.FloorDivide(b),
                        BinaryOpType.MODULO => a.Modulo(b),
                        BinaryOpType.DIVMOD => PyFloat.DivModValues(a, b),
                        _ => a.Power(b),
                    };
                }
                default:
                {
                    if (operation is not (BinaryOpType.ADD or BinaryOpType.SUBTRACT or BinaryOpType.MULTIPLY
                        or BinaryOpType.TRUE_DIVIDE or BinaryOpType.POWER))
                        return PyNotImplemented.Instance;
                    var a = AsComplex(v);
                    if (a == null)
                        return PyNotImplemented.Instance;
                    var b = AsComplex(w);
                    if (b == null)
                        return PyNotImplemented.Instance;
                    return operation switch
                    {
                        BinaryOpType.ADD => a.Add(b),
                        BinaryOpType.SUBTRACT => a.Subtract(b),
                        BinaryOpType.MULTIPLY => a.Multiply(b),
                        BinaryOpType.TRUE_DIVIDE => a.Divide(b),
                        _ => a.Power(b),
                    };
                }
            }
        }

        // float_as_number: the arithmetic slots; no shift, bitwise or matrix one.
        private static bool IsFloatOperation(BinaryOpType operation) => operation is BinaryOpType.ADD
            or BinaryOpType.SUBTRACT or BinaryOpType.MULTIPLY or BinaryOpType.TRUE_DIVIDE or BinaryOpType.FLOOR_DIVIDE
            or BinaryOpType.MODULO or BinaryOpType.DIVMOD or BinaryOpType.POWER;

        /// <summary>
        /// nb_power with its third operand (None when absent): long_pow, float_pow, complex_pow.
        /// float_pow refuses a modulus before reading the operands, complex_pow after.
        /// </summary>
        internal static PyObject Power(Kind kind, PyObject v, PyObject w, PyObject modulus)
        {
            bool none = modulus == null || modulus is PyNone;
            switch (kind)
            {
                case Kind.Long:
                {
                    var a = AsLong(v);
                    var b = AsLong(w);
                    if (a == null || b == null)
                        return PyNotImplemented.Instance;
                    if (none)
                        return a.Power(b);
                    var c = AsLong(modulus);
                    return c == null ? PyNotImplemented.Instance : PyInt.From(LongPowMod(a.Value, b.Value, c.Value));
                }
                case Kind.Float:
                    if (!none)
                        throw PyTypeError.Create("pow() 3rd argument not allowed unless all arguments are integers");
                    return Binary(kind, BinaryOpType.POWER, v, w);
                default:
                    if (AsComplex(v) == null || AsComplex(w) == null)
                        return PyNotImplemented.Instance;
                    if (!none)
                        throw PyValueError.Create("complex modulo");
                    return Binary(kind, BinaryOpType.POWER, v, w);
            }
        }

        // long_pow with a modulus: the result takes the sign of the modulus, and a negative exponent
        // is the power of the inverse.
        private static BigInteger LongPowMod(BigInteger value, BigInteger exponent, BigInteger modulus)
        {
            if (modulus.IsZero)
                throw PyValueError.Create("pow() 3rd argument cannot be 0");
            var size = BigInteger.Abs(modulus);
            if (size.IsOne)
                return BigInteger.Zero;
            var reduced = ((value % size) + size) % size;
            if (exponent.Sign < 0)
            {
                reduced = Inverse(reduced, size);
                exponent = -exponent;
            }
            var result = BigInteger.ModPow(reduced, exponent, size);
            return modulus.Sign < 0 && !result.IsZero ? result - size : result;
        }

        // long_invmod
        private static BigInteger Inverse(BigInteger value, BigInteger modulus)
        {
            BigInteger oldR = value, r = modulus, oldS = BigInteger.One, s = BigInteger.Zero;
            while (!r.IsZero)
            {
                var quotient = oldR / r;
                (oldR, r) = (r, oldR - quotient * r);
                (oldS, s) = (s, oldS - quotient * s);
            }
            if (!oldR.IsOne)
                throw PyValueError.Create("base is not invertible for the given modulus");
            return ((oldS % modulus) + modulus) % modulus;
        }

        private static readonly (string Name, BinaryOpType Operation)[] Rows =
        {
            ("add", BinaryOpType.ADD), ("sub", BinaryOpType.SUBTRACT), ("mul", BinaryOpType.MULTIPLY),
            ("truediv", BinaryOpType.TRUE_DIVIDE), ("floordiv", BinaryOpType.FLOOR_DIVIDE), ("mod", BinaryOpType.MODULO),
            ("divmod", BinaryOpType.DIVMOD), ("lshift", BinaryOpType.LSHIFT), ("rshift", BinaryOpType.RSHIFT),
            ("and", BinaryOpType.AND), ("or", BinaryOpType.OR), ("xor", BinaryOpType.XOR),
        };

        /// <summary>
        /// add_operators for the binary and ternary number slots of int, float and complex: a pair
        /// of wrappers for each slot the family has (wrap_binaryfunc_l and _r, wrap_ternaryfunc and _r).
        /// </summary>
        internal static void Install()
        {
            foreach (var (type, kind) in new[] { (PyType.IntType, Kind.Long), (PyType.FloatType, Kind.Float), (PyType.ComplexType, Kind.Complex) })
            {
                foreach (var (name, operation) in Rows)
                {
                    if (!HasSlot(kind, operation))
                        continue;
                    var op = operation;
                    var k = kind;
                    type.TypeDict[$"__{name}__"] = new PyWrapperDescriptor($"__{name}__", type,
                        (self, args, kwargs) => Binary(k, op, self, args[0]));
                    type.TypeDict[$"__r{name}__"] = new PyWrapperDescriptor($"__r{name}__", type,
                        (self, args, kwargs) => Binary(k, op, args[0], self));
                }
                var family = kind;
                type.TypeDict["__pow__"] = new PyWrapperDescriptor("__pow__", type,
                    (self, args, kwargs) => Power(family, self, args[0], args.Length > 1 ? args[1] : null));
                type.TypeDict["__rpow__"] = new PyWrapperDescriptor("__rpow__", type,
                    (self, args, kwargs) => Power(family, args[0], self, args.Length > 1 ? args[1] : null));
            }
        }

        private static bool HasSlot(Kind kind, BinaryOpType operation) => kind switch
        {
            Kind.Long => true,
            Kind.Float => IsFloatOperation(operation),
            _ => operation is BinaryOpType.ADD or BinaryOpType.SUBTRACT or BinaryOpType.MULTIPLY or BinaryOpType.TRUE_DIVIDE,
        };
    }
}
