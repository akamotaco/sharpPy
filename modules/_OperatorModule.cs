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
            module.ModuleDict["lt"] = PyArgs.FastCall("_operator", "lt", 2, 2, Lt);
            module.ModuleDict["le"] = PyArgs.FastCall("_operator", "le", 2, 2, Le);
            module.ModuleDict["eq"] = PyArgs.FastCall("_operator", "eq", 2, 2, Eq);
            module.ModuleDict["ne"] = PyArgs.FastCall("_operator", "ne", 2, 2, Ne);
            module.ModuleDict["ge"] = PyArgs.FastCall("_operator", "ge", 2, 2, Ge);
            module.ModuleDict["gt"] = PyArgs.FastCall("_operator", "gt", 2, 2, Gt);

            // Logical operators
            module.ModuleDict["not_"] = PyArgs.MethO("_operator", "not_", Not);
            module.ModuleDict["truth"] = PyArgs.MethO("_operator", "truth", Truth);
            module.ModuleDict["is_"] = PyArgs.FastCall("_operator", "is_", 2, 2, Is);
            module.ModuleDict["is_not"] = PyArgs.FastCall("_operator", "is_not", 2, 2, IsNot);

            // Arithmetic operators
            module.ModuleDict["abs"] = PyArgs.MethO("_operator", "abs", Abs);
            module.ModuleDict["add"] = PyArgs.FastCall("_operator", "add", 2, 2, Add);
            module.ModuleDict["and_"] = PyArgs.FastCall("_operator", "and_", 2, 2, And);
            module.ModuleDict["floordiv"] = PyArgs.FastCall("_operator", "floordiv", 2, 2, FloorDiv);
            module.ModuleDict["index"] = PyArgs.MethO("_operator", "index", Index);
            module.ModuleDict["inv"] = PyArgs.MethO("_operator", "inv", Inv);
            module.ModuleDict["invert"] = PyArgs.MethO("_operator", "invert", Invert);
            module.ModuleDict["lshift"] = PyArgs.FastCall("_operator", "lshift", 2, 2, LShift);
            module.ModuleDict["mod"] = PyArgs.FastCall("_operator", "mod", 2, 2, Mod);
            module.ModuleDict["mul"] = PyArgs.FastCall("_operator", "mul", 2, 2, Mul);
            module.ModuleDict["matmul"] = PyArgs.FastCall("_operator", "matmul", 2, 2, MatMul);
            module.ModuleDict["neg"] = PyArgs.MethO("_operator", "neg", Neg);
            module.ModuleDict["or_"] = PyArgs.FastCall("_operator", "or_", 2, 2, Or);
            module.ModuleDict["pos"] = PyArgs.MethO("_operator", "pos", Pos);
            module.ModuleDict["pow"] = PyArgs.FastCall("_operator", "pow", 2, 2, Pow);
            module.ModuleDict["rshift"] = PyArgs.FastCall("_operator", "rshift", 2, 2, RShift);
            module.ModuleDict["sub"] = PyArgs.FastCall("_operator", "sub", 2, 2, Sub);
            module.ModuleDict["truediv"] = PyArgs.FastCall("_operator", "truediv", 2, 2, TrueDiv);
            module.ModuleDict["xor"] = PyArgs.FastCall("_operator", "xor", 2, 2, Xor);

            // Sequence operators
            module.ModuleDict["concat"] = PyArgs.FastCall("_operator", "concat", 2, 2, Concat);
            module.ModuleDict["contains"] = PyArgs.FastCall("_operator", "contains", 2, 2, Contains);
            module.ModuleDict["countOf"] = PyArgs.FastCall("_operator", "countOf", 2, 2, CountOf);
            module.ModuleDict["delitem"] = PyArgs.FastCall("_operator", "delitem", 2, 2, DelItem);
            module.ModuleDict["getitem"] = PyArgs.FastCall("_operator", "getitem", 2, 2, GetItem);
            module.ModuleDict["indexOf"] = PyArgs.FastCall("_operator", "indexOf", 2, 2, IndexOf);
            module.ModuleDict["setitem"] = PyArgs.FastCall("_operator", "setitem", 3, 3, SetItem);
            module.ModuleDict["length_hint"] = PyArgs.FastCall("_operator", "length_hint", 1, 2, LengthHint);

            // In-place operators: PyNumber_InPlace*, and PySequence_InPlaceConcat for iconcat.
            module.ModuleDict["iadd"] = PyArgs.FastCall("_operator", "iadd", 2, 2, args => Number(args, BinaryOpType.INPLACE_ADD));
            module.ModuleDict["isub"] = PyArgs.FastCall("_operator", "isub", 2, 2, args => Number(args, BinaryOpType.INPLACE_SUBTRACT));
            module.ModuleDict["imul"] = PyArgs.FastCall("_operator", "imul", 2, 2, args => Number(args, BinaryOpType.INPLACE_MULTIPLY));
            module.ModuleDict["imatmul"] = PyArgs.FastCall("_operator", "imatmul", 2, 2, args => Number(args, BinaryOpType.INPLACE_MATRIX_MULTIPLY));
            module.ModuleDict["ifloordiv"] = PyArgs.FastCall("_operator", "ifloordiv", 2, 2, args => Number(args, BinaryOpType.INPLACE_FLOOR_DIVIDE));
            module.ModuleDict["itruediv"] = PyArgs.FastCall("_operator", "itruediv", 2, 2, args => Number(args, BinaryOpType.INPLACE_TRUE_DIVIDE));
            module.ModuleDict["imod"] = PyArgs.FastCall("_operator", "imod", 2, 2, args => Number(args, BinaryOpType.INPLACE_MODULO));
            module.ModuleDict["ilshift"] = PyArgs.FastCall("_operator", "ilshift", 2, 2, args => Number(args, BinaryOpType.INPLACE_LSHIFT));
            module.ModuleDict["irshift"] = PyArgs.FastCall("_operator", "irshift", 2, 2, args => Number(args, BinaryOpType.INPLACE_RSHIFT));
            module.ModuleDict["iand"] = PyArgs.FastCall("_operator", "iand", 2, 2, args => Number(args, BinaryOpType.INPLACE_AND));
            module.ModuleDict["ixor"] = PyArgs.FastCall("_operator", "ixor", 2, 2, args => Number(args, BinaryOpType.INPLACE_XOR));
            module.ModuleDict["ior"] = PyArgs.FastCall("_operator", "ior", 2, 2, args => Number(args, BinaryOpType.INPLACE_OR));
            module.ModuleDict["ipow"] = PyArgs.FastCall("_operator", "ipow", 2, 2, args => Number(args, BinaryOpType.INPLACE_POWER));
            module.ModuleDict["iconcat"] = PyArgs.FastCall("_operator", "iconcat", 2, 2,
                args => PyVM.SequenceInPlaceConcat(args[0], args[1]));

            // _operator_call: METH_FASTCALL | METH_KEYWORDS, obj then what it is called with.
            module.ModuleDict["call"] = new PyBuiltinFunction("call", (args, kwargs) =>
            {
                PyArgs.Positional("call", args, null, 1, int.MaxValue);
                return args[0].Call(args[1..], kwargs);
            });

            return module;
        }

        #region Comparison Operators

        private static PyObject Lt(PyObject[] args)
        {
            return PyObject.Compare(args[0], args[1], PyObject.CompareOp.LT);
        }

        private static PyObject Le(PyObject[] args)
        {
            return PyObject.Compare(args[0], args[1], PyObject.CompareOp.LE);
        }

        private static PyObject Eq(PyObject[] args)
        {
            return PyObject.Compare(args[0], args[1], PyObject.CompareOp.EQ);
        }

        private static PyObject Ne(PyObject[] args)
        {
            return PyObject.Compare(args[0], args[1], PyObject.CompareOp.NE);
        }

        private static PyObject Ge(PyObject[] args)
        {
            return PyObject.Compare(args[0], args[1], PyObject.CompareOp.GE);
        }

        private static PyObject Gt(PyObject[] args)
        {
            return PyObject.Compare(args[0], args[1], PyObject.CompareOp.GT);
        }

        #endregion

        #region Logical Operators

        private static PyObject Not(PyObject[] args)
        {
            return PyBool.FromBool(!args[0].IsTrue());
        }

        private static PyObject Truth(PyObject[] args)
        {
            return PyBool.FromBool(args[0].IsTrue());
        }

        private static PyObject Is(PyObject[] args)
        {
            return PyBool.FromBool(ReferenceEquals(args[0], args[1]));
        }

        private static PyObject IsNot(PyObject[] args)
        {
            return PyBool.FromBool(!ReferenceEquals(args[0], args[1]));
        }

        #endregion

        #region Arithmetic Operators

        private static PyObject Abs(PyObject[] args)
        {
            // CPython 3.12: Modules/_operator.c - _operator_abs goes through nb_absolute, the same
            // meaning builtins.abs has. It used to have its own exact-type switch here, which made
            // operator.abs(FSub(-2.5)) and operator.abs(a user class) fail while abs() worked.
            return PyBuiltinFunction.Absolute(args[0]);
        }

        private static PyObject Number(PyObject[] args, BinaryOpType operation) =>
            PyVM.Instance.ExecuteBinaryOpType(args[0], args[1], operation);

        private static PyObject Add(PyObject[] args)
        {
            return Number(args, BinaryOpType.ADD);
        }

        private static PyObject And(PyObject[] args)
        {
            return Number(args, BinaryOpType.AND);
        }

        private static PyObject FloorDiv(PyObject[] args)
        {
            return Number(args, BinaryOpType.FLOOR_DIVIDE);
        }

        private static PyObject Index(PyObject[] args)
        {
            // One shared PyNumber_Index rule (see PyObject.IndexOrThrow). This copy resolved
            // __index__ through an instance-level GetAttribute and then caught failures by
            // sniffing the AttributeError message text, so an int subclass followed a user
            // __index__ (3 where CPython answers with the value 5) and unrelated attribute errors
            // were swallowed by the string match.
            return PyObject.IndexOrThrow(args[0]);
        }

        private static PyObject Inv(PyObject[] args)
        {
            return args[0].BitwiseNot();
        }

        private static PyObject Invert(PyObject[] args)
        {
            return Inv(args);
        }

        private static PyObject LShift(PyObject[] args)
        {
            return Number(args, BinaryOpType.LSHIFT);
        }

        private static PyObject Mod(PyObject[] args)
        {
            return Number(args, BinaryOpType.MODULO);
        }

        private static PyObject Mul(PyObject[] args)
        {
            return Number(args, BinaryOpType.MULTIPLY);
        }

        private static PyObject MatMul(PyObject[] args)
        {
            return Number(args, BinaryOpType.MATRIX_MULTIPLY);
        }

        private static PyObject Neg(PyObject[] args)
        {
            return args[0].Negative();
        }

        private static PyObject Or(PyObject[] args)
        {
            return Number(args, BinaryOpType.OR);
        }

        private static PyObject Pos(PyObject[] args)
        {
            return args[0].Positive();
        }

        private static PyObject Pow(PyObject[] args)
        {
            return Number(args, BinaryOpType.POWER);
        }

        private static PyObject RShift(PyObject[] args)
        {
            return Number(args, BinaryOpType.RSHIFT);
        }

        private static PyObject Sub(PyObject[] args)
        {
            return Number(args, BinaryOpType.SUBTRACT);
        }

        private static PyObject TrueDiv(PyObject[] args)
        {
            return Number(args, BinaryOpType.TRUE_DIVIDE);
        }

        private static PyObject Xor(PyObject[] args)
        {
            return Number(args, BinaryOpType.XOR);
        }

        #endregion

        #region Sequence Operators

        private static PyObject Concat(PyObject[] args)
        {
            return PyVM.SequenceConcat(args[0], args[1]);
        }

        private static PyObject Contains(PyObject[] args)
        {
            return args[0].Contains(args[1]);
        }

        private static PyObject CountOf(PyObject[] args)
        {
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
            // PyObject_DelItem
            PyAbstract.DelItem(args[0], args[1]);
            return PyNone.Instance;
        }

        private static PyObject GetItem(PyObject[] args)
        {
            // PyObject_GetItem
            return PyAbstract.GetItem(args[0], args[1]);
        }

        private static PyObject IndexOf(PyObject[] args)
        {
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
            // PyObject_SetItem
            PyAbstract.SetItem(args[0], args[1], args[2]);
            return PyNone.Instance;
        }

        private static PyObject LengthHint(PyObject[] args)
        {
            long defaultValue = 0;
            if (args.Length == 2)
            {
                var integer = PyObject.IndexOrThrow(args[1]);
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
