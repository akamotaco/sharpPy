using System;
using System.Numerics;

namespace SharpPy.Modules
{
    /// <summary>
    /// CPython _operator module - C extension backend for operator.py
    /// Provides optimized operator functions
    /// </summary>
    public static class _OperatorModule
    {
        public static PyModule CreateOperatorModule()
        {
            var module = new PyModule("_operator", "<_operator C module>");

            // Modules/_operator.c has no __all__: "from _operator import *" in Lib/operator.py takes
            // every public name this module defines over the pure-Python ones.

            // Comparison operators
            module.ModuleDict["lt"] = new PyBuiltinFunction("lt", Lt);
            module.ModuleDict["le"] = new PyBuiltinFunction("le", Le);
            module.ModuleDict["eq"] = new PyBuiltinFunction("eq", Eq);
            module.ModuleDict["ne"] = new PyBuiltinFunction("ne", Ne);
            module.ModuleDict["ge"] = new PyBuiltinFunction("ge", Ge);
            module.ModuleDict["gt"] = new PyBuiltinFunction("gt", Gt);

            // Logical operators
            module.ModuleDict["not_"] = new PyBuiltinFunction("not_", Not);
            module.ModuleDict["truth"] = new PyBuiltinFunction("truth", Truth);
            module.ModuleDict["is_"] = new PyBuiltinFunction("is_", Is);
            module.ModuleDict["is_not"] = new PyBuiltinFunction("is_not", IsNot);

            // Arithmetic operators
            module.ModuleDict["abs"] = new PyBuiltinFunction("abs", Abs);
            module.ModuleDict["add"] = new PyBuiltinFunction("add", Add);
            module.ModuleDict["and_"] = new PyBuiltinFunction("and_", And);
            module.ModuleDict["floordiv"] = new PyBuiltinFunction("floordiv", FloorDiv);
            module.ModuleDict["index"] = new PyBuiltinFunction("index", Index);
            module.ModuleDict["inv"] = new PyBuiltinFunction("inv", Inv);
            module.ModuleDict["invert"] = new PyBuiltinFunction("invert", Invert);
            module.ModuleDict["lshift"] = new PyBuiltinFunction("lshift", LShift);
            module.ModuleDict["mod"] = new PyBuiltinFunction("mod", Mod);
            module.ModuleDict["mul"] = new PyBuiltinFunction("mul", Mul);
            module.ModuleDict["matmul"] = new PyBuiltinFunction("matmul", MatMul);
            module.ModuleDict["neg"] = new PyBuiltinFunction("neg", Neg);
            module.ModuleDict["or_"] = new PyBuiltinFunction("or_", Or);
            module.ModuleDict["pos"] = new PyBuiltinFunction("pos", Pos);
            module.ModuleDict["pow"] = new PyBuiltinFunction("pow", Pow);
            module.ModuleDict["rshift"] = new PyBuiltinFunction("rshift", RShift);
            module.ModuleDict["sub"] = new PyBuiltinFunction("sub", Sub);
            module.ModuleDict["truediv"] = new PyBuiltinFunction("truediv", TrueDiv);
            module.ModuleDict["xor"] = new PyBuiltinFunction("xor", Xor);

            // Sequence operators
            module.ModuleDict["concat"] = new PyBuiltinFunction("concat", Concat);
            module.ModuleDict["contains"] = new PyBuiltinFunction("contains", Contains);
            module.ModuleDict["countOf"] = new PyBuiltinFunction("countOf", CountOf);
            module.ModuleDict["delitem"] = new PyBuiltinFunction("delitem", DelItem);
            module.ModuleDict["getitem"] = new PyBuiltinFunction("getitem", GetItem);
            module.ModuleDict["indexOf"] = new PyBuiltinFunction("indexOf", IndexOf);
            module.ModuleDict["setitem"] = new PyBuiltinFunction("setitem", SetItem);
            module.ModuleDict["length_hint"] = new PyBuiltinFunction("length_hint", LengthHint);

            return module;
        }

        #region Comparison Operators

        private static PyObject Lt(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"lt() takes exactly 2 arguments ({args.Length} given)");
            return PyObject.Compare(args[0], args[1], PyObject.CompareOp.LT);
        }

        private static PyObject Le(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"le() takes exactly 2 arguments ({args.Length} given)");
            return PyObject.Compare(args[0], args[1], PyObject.CompareOp.LE);
        }

        private static PyObject Eq(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"eq() takes exactly 2 arguments ({args.Length} given)");
            return PyObject.Compare(args[0], args[1], PyObject.CompareOp.EQ);
        }

        private static PyObject Ne(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"ne() takes exactly 2 arguments ({args.Length} given)");
            return PyObject.Compare(args[0], args[1], PyObject.CompareOp.NE);
        }

        private static PyObject Ge(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"ge() takes exactly 2 arguments ({args.Length} given)");
            return PyObject.Compare(args[0], args[1], PyObject.CompareOp.GE);
        }

        private static PyObject Gt(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"gt() takes exactly 2 arguments ({args.Length} given)");
            return PyObject.Compare(args[0], args[1], PyObject.CompareOp.GT);
        }

        #endregion

        #region Logical Operators

        private static PyObject Not(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"not_() takes exactly 1 argument ({args.Length} given)");
            return PyBool.FromBool(!args[0].IsTrue());
        }

        private static PyObject Truth(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"truth() takes exactly 1 argument ({args.Length} given)");
            return PyBool.FromBool(args[0].IsTrue());
        }

        private static PyObject Is(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"is_() takes exactly 2 arguments ({args.Length} given)");
            return PyBool.FromBool(ReferenceEquals(args[0], args[1]));
        }

        private static PyObject IsNot(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"is_not() takes exactly 2 arguments ({args.Length} given)");
            return PyBool.FromBool(!ReferenceEquals(args[0], args[1]));
        }

        #endregion

        #region Arithmetic Operators

        private static PyObject Abs(PyObject[] args)
        {
            // Measured arity text: "_operator.abs() takes exactly one argument (0 given)" - the
            // module-qualified name and the word "one", not "abs() takes exactly 1 argument".
            if (args.Length != 1)
                throw PyTypeError.Create(
                    $"_operator.abs() takes exactly one argument ({args.Length} given)");

            // CPython 3.12: Modules/_operator.c - _operator_abs goes through nb_absolute, the same
            // meaning builtins.abs has. It used to have its own exact-type switch here, which made
            // operator.abs(FSub(-2.5)) and operator.abs(a user class) fail while abs() worked.
            return PyBuiltinFunction.Absolute(args[0]);
        }

        private static PyObject Number(PyObject[] args, BinaryOpType operation) =>
            PyVM.Instance.ExecuteBinaryOpType(args[0], args[1], operation);

        private static PyObject Add(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"add() takes exactly 2 arguments ({args.Length} given)");
            return Number(args, BinaryOpType.ADD);
        }

        private static PyObject And(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"and_() takes exactly 2 arguments ({args.Length} given)");
            return Number(args, BinaryOpType.AND);
        }

        private static PyObject FloorDiv(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"floordiv() takes exactly 2 arguments ({args.Length} given)");
            return Number(args, BinaryOpType.FLOOR_DIVIDE);
        }

        private static PyObject Index(PyObject[] args)
        {
            // Measured text: "_operator.index() takes exactly one argument (0 given)" - the
            // module-qualified name and the word "one".
            if (args.Length != 1)
                throw PyTypeError.Create(
                    $"_operator.index() takes exactly one argument ({args.Length} given)");

            // One shared PyNumber_Index rule (see PyObject.IndexOrThrow). This copy resolved
            // __index__ through an instance-level GetAttribute and then caught failures by
            // sniffing the AttributeError message text, so an int subclass followed a user
            // __index__ (3 where CPython answers with the value 5) and unrelated attribute errors
            // were swallowed by the string match.
            return PyObject.IndexOrThrow(args[0]);
        }

        private static PyObject Inv(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"inv() takes exactly 1 argument ({args.Length} given)");
            return args[0].BitwiseNot();
        }

        private static PyObject Invert(PyObject[] args)
        {
            return Inv(args);
        }

        private static PyObject LShift(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"lshift() takes exactly 2 arguments ({args.Length} given)");
            return Number(args, BinaryOpType.LSHIFT);
        }

        private static PyObject Mod(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"mod() takes exactly 2 arguments ({args.Length} given)");
            return Number(args, BinaryOpType.MODULO);
        }

        private static PyObject Mul(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"mul() takes exactly 2 arguments ({args.Length} given)");
            return Number(args, BinaryOpType.MULTIPLY);
        }

        private static PyObject MatMul(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"matmul() takes exactly 2 arguments ({args.Length} given)");
            return Number(args, BinaryOpType.MATRIX_MULTIPLY);
        }

        private static PyObject Neg(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"neg() takes exactly 1 argument ({args.Length} given)");
            return args[0].Negative();
        }

        private static PyObject Or(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"or_() takes exactly 2 arguments ({args.Length} given)");
            return Number(args, BinaryOpType.OR);
        }

        private static PyObject Pos(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"pos() takes exactly 1 argument ({args.Length} given)");
            return args[0].Positive();
        }

        private static PyObject Pow(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"pow() takes exactly 2 arguments ({args.Length} given)");
            return Number(args, BinaryOpType.POWER);
        }

        private static PyObject RShift(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"rshift() takes exactly 2 arguments ({args.Length} given)");
            return Number(args, BinaryOpType.RSHIFT);
        }

        private static PyObject Sub(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"sub() takes exactly 2 arguments ({args.Length} given)");
            return Number(args, BinaryOpType.SUBTRACT);
        }

        private static PyObject TrueDiv(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"truediv() takes exactly 2 arguments ({args.Length} given)");
            return Number(args, BinaryOpType.TRUE_DIVIDE);
        }

        private static PyObject Xor(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"xor() takes exactly 2 arguments ({args.Length} given)");
            return Number(args, BinaryOpType.XOR);
        }

        #endregion

        #region Sequence Operators

        private static PyObject Concat(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"concat() takes exactly 2 arguments ({args.Length} given)");
            return PyVM.SequenceConcat(args[0], args[1]);
        }

        private static PyObject Contains(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"contains() takes exactly 2 arguments ({args.Length} given)");
            return args[0].Contains(args[1]);
        }

        private static PyObject CountOf(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"countOf() takes exactly 2 arguments ({args.Length} given)");

            // _PySequence_IterSearch, PY_ITERSEARCH_COUNT
            int count = 0;
            var iterator = args[0].GetIterator();
            while (true)
            {
                PyObject item;
                try
                {
                    item = iterator.Next();
                }
                catch (PythonException error) when (error.PyException is PyStopIteration)
                {
                    break;
                }
                if (PyObject.CompareBool(item, args[1], PyObject.CompareOp.EQ))
                    count++;
            }

            return PyInt.From(count);
        }

        private static PyObject DelItem(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"delitem() takes exactly 2 arguments ({args.Length} given)");

            // PyObject_DelItem
            PyAbstract.DelItem(args[0], args[1]);
            return PyNone.Instance;
        }

        private static PyObject GetItem(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"getitem() takes exactly 2 arguments ({args.Length} given)");
            // PyObject_GetItem
            return PyAbstract.GetItem(args[0], args[1]);
        }

        private static PyObject IndexOf(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"indexOf() takes exactly 2 arguments ({args.Length} given)");

            // _PySequence_IterSearch, PY_ITERSEARCH_INDEX
            int index = 0;
            var iterator = args[0].GetIterator();
            while (true)
            {
                PyObject item;
                try
                {
                    item = iterator.Next();
                }
                catch (PythonException error) when (error.PyException is PyStopIteration)
                {
                    throw PyValueError.Create("sequence.index(x): x not in sequence");
                }
                if (PyObject.CompareBool(item, args[1], PyObject.CompareOp.EQ))
                    return PyInt.From(index);
                index++;
            }
        }

        private static PyObject SetItem(PyObject[] args)
        {
            if (args.Length != 3)
                throw PyTypeError.Create($"setitem() takes exactly 3 arguments ({args.Length} given)");

            // PyObject_SetItem
            PyAbstract.SetItem(args[0], args[1], args[2]);
            return PyNone.Instance;
        }

        private static PyObject LengthHint(PyObject[] args, PyDict kwargs)
        {
            if (kwargs != null && kwargs.Length() != 0)
                throw PyTypeError.Create("_operator.length_hint() takes no keyword arguments");
            if (args.Length < 1)
                throw PyTypeError.Create("length_hint expected at least 1 argument, got 0");
            if (args.Length > 2)
                throw PyTypeError.Create($"length_hint expected at most 2 arguments, got {args.Length}");
            long defaultValue = 0;
            if (args.Length == 2)
            {
                var integer = PyObject.TryGetIndex(args[1]);
                if (integer == null)
                    throw PyTypeError.Create($"'{args[1].GetTypeName()}' object cannot be interpreted as an integer");
                try { defaultValue = (long)nint.CreateChecked(integer.Value); }
                catch (OverflowException)
                {
                    throw PyOverflowError.Create("Python int too large to convert to C ssize_t");
                }
            }
            return PyInt.From(args[0].LengthHint(defaultValue));
        }

        #endregion

        #region Attribute/Item/Method Getters

        #endregion
    }
}
