using System;

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

            // Attribute operators
            module.ModuleDict["attrgetter"] = new PyBuiltinFunction("attrgetter", AttrGetter);
            module.ModuleDict["itemgetter"] = new PyBuiltinFunction("itemgetter", ItemGetter);
            module.ModuleDict["methodcaller"] = new PyBuiltinFunction("methodcaller", MethodCaller);

            return module;
        }

        #region Comparison Operators

        private static PyObject Lt(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"lt() takes exactly 2 arguments ({args.Length} given)");
            return args[0].RichCompare(args[1], PyObject.CompareOp.LT);
        }

        private static PyObject Le(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"le() takes exactly 2 arguments ({args.Length} given)");
            return args[0].RichCompare(args[1], PyObject.CompareOp.LE);
        }

        private static PyObject Eq(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"eq() takes exactly 2 arguments ({args.Length} given)");
            return args[0].RichCompare(args[1], PyObject.CompareOp.EQ);
        }

        private static PyObject Ne(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"ne() takes exactly 2 arguments ({args.Length} given)");
            return args[0].RichCompare(args[1], PyObject.CompareOp.NE);
        }

        private static PyObject Ge(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"ge() takes exactly 2 arguments ({args.Length} given)");
            return args[0].RichCompare(args[1], PyObject.CompareOp.GE);
        }

        private static PyObject Gt(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"gt() takes exactly 2 arguments ({args.Length} given)");
            return args[0].RichCompare(args[1], PyObject.CompareOp.GT);
        }

        #endregion

        #region Logical Operators

        private static PyObject Not(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"not_() takes exactly 1 argument ({args.Length} given)");
            return PyBool.FromBool(!args[0].PyBoolValue());
        }

        private static PyObject Truth(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"truth() takes exactly 1 argument ({args.Length} given)");
            return PyBool.FromBool(args[0].PyBoolValue());
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
            if (args.Length != 1)
                throw PyTypeError.Create($"abs() takes exactly 1 argument ({args.Length} given)");

            var obj = args[0];
            return obj switch
            {
                PyInt pyInt => new PyInt(Math.Abs(pyInt.Value)),
                PyFloat pyFloat => new PyFloat(Math.Abs(pyFloat.Value)),
                _ => throw PyTypeError.Create($"bad operand type for abs(): '{obj.GetTypeName()}'")
            };
        }

        private static PyObject Add(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"add() takes exactly 2 arguments ({args.Length} given)");
            return args[0].Add(args[1]);
        }

        private static PyObject And(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"and_() takes exactly 2 arguments ({args.Length} given)");
            return args[0].BitwiseAnd(args[1]);
        }

        private static PyObject FloorDiv(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"floordiv() takes exactly 2 arguments ({args.Length} given)");
            return args[0].FloorDivide(args[1]);
        }

        private static PyObject Index(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"index() takes exactly 1 argument ({args.Length} given)");

            if (args[0] is PyInt pyInt)
                return pyInt;

            throw PyTypeError.Create($"'{args[0].GetTypeName()}' object cannot be interpreted as an integer");
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
            return args[0].LeftShift(args[1]);
        }

        private static PyObject Mod(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"mod() takes exactly 2 arguments ({args.Length} given)");
            return args[0].Modulo(args[1]);
        }

        private static PyObject Mul(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"mul() takes exactly 2 arguments ({args.Length} given)");
            return args[0].Multiply(args[1]);
        }

        private static PyObject MatMul(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"matmul() takes exactly 2 arguments ({args.Length} given)");
            return args[0].MatrixMultiply(args[1]);
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
            return args[0].BitwiseOr(args[1]);
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
            return args[0].Power(args[1]);
        }

        private static PyObject RShift(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"rshift() takes exactly 2 arguments ({args.Length} given)");
            return args[0].RightShift(args[1]);
        }

        private static PyObject Sub(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"sub() takes exactly 2 arguments ({args.Length} given)");
            return args[0].Subtract(args[1]);
        }

        private static PyObject TrueDiv(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"truediv() takes exactly 2 arguments ({args.Length} given)");
            return args[0].Divide(args[1]);
        }

        private static PyObject Xor(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"xor() takes exactly 2 arguments ({args.Length} given)");
            return args[0].BitwiseXor(args[1]);
        }

        #endregion

        #region Sequence Operators

        private static PyObject Concat(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"concat() takes exactly 2 arguments ({args.Length} given)");
            return args[0].Add(args[1]);
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

            // Simple implementation for sequences
            int count = 0;
            if (args[0] is PyList list)
            {
                foreach (var item in list.Items)
                {
                    var result = item.RichCompare(args[1], PyObject.CompareOp.EQ);
                    if (result is PyBool pyBool && pyBool.Value)
                        count++;
                }
            }
            else if (args[0] is PyTuple tuple)
            {
                foreach (var item in tuple.Items)
                {
                    var result = item.RichCompare(args[1], PyObject.CompareOp.EQ);
                    if (result is PyBool pyBool && pyBool.Value)
                        count++;
                }
            }

            return new PyInt(count);
        }

        private static PyObject DelItem(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"delitem() takes exactly 2 arguments ({args.Length} given)");

            args[0].DelItem(args[1]);
            return PyNone.Instance;
        }

        private static PyObject GetItem(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"getitem() takes exactly 2 arguments ({args.Length} given)");
            return args[0].GetItem(args[1]);
        }

        private static PyObject IndexOf(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"indexOf() takes exactly 2 arguments ({args.Length} given)");

            // Simple implementation for sequences
            if (args[0] is PyList list)
            {
                for (int i = 0; i < list.Items.Length; i++)
                {
                    var result = list.Items[i].RichCompare(args[1], PyObject.CompareOp.EQ);
                    if (result is PyBool pyBool && pyBool.Value)
                        return new PyInt(i);
                }
            }
            else if (args[0] is PyTuple tuple)
            {
                for (int i = 0; i < tuple.Items.Length; i++)
                {
                    var result = tuple.Items[i].RichCompare(args[1], PyObject.CompareOp.EQ);
                    if (result is PyBool pyBool && pyBool.Value)
                        return new PyInt(i);
                }
            }

            throw PyValueError.Create($"{args[1].ToRepr().Value} is not in sequence");
        }

        private static PyObject SetItem(PyObject[] args)
        {
            if (args.Length != 3)
                throw PyTypeError.Create($"setitem() takes exactly 3 arguments ({args.Length} given)");

            args[0].SetItem(args[1], args[2]);
            return PyNone.Instance;
        }

        private static PyObject LengthHint(PyObject[] args)
        {
            if (args.Length < 1 || args.Length > 2)
                throw PyTypeError.Create($"length_hint() takes 1 or 2 arguments ({args.Length} given)");

            try
            {
                return new PyInt(args[0].Length());
            }
            catch
            {
                return args.Length > 1 ? args[1] : new PyInt(0);
            }
        }

        #endregion

        #region Attribute/Item/Method Getters

        private static PyObject AttrGetter(PyObject[] args)
        {
            // Simplified implementation - returns a callable that gets attributes
            throw PyNotImplementedError.Create("attrgetter() not yet implemented");
        }

        private static PyObject ItemGetter(PyObject[] args)
        {
            // Simplified implementation - returns a callable that gets items
            throw PyNotImplementedError.Create("itemgetter() not yet implemented");
        }

        private static PyObject MethodCaller(PyObject[] args)
        {
            // Simplified implementation - returns a callable that calls methods
            throw PyNotImplementedError.Create("methodcaller() not yet implemented");
        }

        #endregion
    }
}
