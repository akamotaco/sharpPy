using System;

namespace SharpPy.Modules
{
    /// <summary>
    /// CPython builtins module - provides access to Python built-in objects
    /// This is the "builtins" module that can be imported with "import builtins"
    /// </summary>
    public static class BuiltinsModule
    {
        public static PyModule CreateBuiltinsModule()
        {
            var module = new PyModule("builtins", "<builtins module>");

            // Built-in types (as type objects, not constructors)
            module.ModuleDict["object"] = PyType.ObjectType;
            module.ModuleDict["type"] = PyType.TypeType;
            module.ModuleDict["int"] = PyType.IntType;
            module.ModuleDict["float"] = PyType.FloatType;
            module.ModuleDict["str"] = PyType.StrType;
            module.ModuleDict["bool"] = PyType.BoolType;
            module.ModuleDict["list"] = PyType.ListType;
            module.ModuleDict["tuple"] = PyType.TupleType;
            module.ModuleDict["dict"] = PyType.DictType;
            module.ModuleDict["set"] = PyType.SetType;
            module.ModuleDict["frozenset"] = PyType.FrozenSetType;
            module.ModuleDict["NoneType"] = PyType.NoneType;
            module.ModuleDict["function"] = PyType.FunctionType;

            // Built-in constants
            module.ModuleDict["None"] = PyNone.Instance;
            module.ModuleDict["True"] = PyBool.True;
            module.ModuleDict["False"] = PyBool.False;
            module.ModuleDict["NotImplemented"] = PyNotImplemented.Instance;

            // Exception types (most important for enum module)
            module.ModuleDict["BaseException"] = PyType.BaseExceptionType;
            module.ModuleDict["Exception"] = PyType.ExceptionType;
            module.ModuleDict["StopIteration"] = PyType.StopIterationType;
            module.ModuleDict["TypeError"] = PyType.TypeErrorType;
            module.ModuleDict["ValueError"] = PyType.ValueErrorType;
            module.ModuleDict["KeyError"] = PyType.KeyErrorType;
            module.ModuleDict["IndexError"] = PyType.IndexErrorType;
            module.ModuleDict["AttributeError"] = PyType.AttributeErrorType;
            module.ModuleDict["NameError"] = PyType.NameErrorType;
            module.ModuleDict["RuntimeError"] = PyType.RuntimeErrorType;
            module.ModuleDict["ZeroDivisionError"] = PyType.ZeroDivisionErrorType;
            module.ModuleDict["ImportError"] = PyType.ImportErrorType;
            module.ModuleDict["ModuleNotFoundError"] = PyType.ModuleNotFoundErrorType;
            module.ModuleDict["NotImplementedError"] = PyType.NotImplementedErrorType;
            module.ModuleDict["AssertionError"] = PyType.AssertionErrorType;
            module.ModuleDict["SyntaxError"] = PyType.SyntaxErrorType;
            module.ModuleDict["IndentationError"] = PyType.IndentationErrorType;
            module.ModuleDict["OverflowError"] = PyType.OverflowErrorType;
            module.ModuleDict["RecursionError"] = PyType.RecursionErrorType;
            module.ModuleDict["UnboundLocalError"] = PyType.UnboundLocalErrorType;

            // Built-in functions (commonly used in standard library)
            module.ModuleDict["abs"] = new PyBuiltinFunction("abs", Abs);
            module.ModuleDict["len"] = new PyBuiltinFunction("len", Len);
            module.ModuleDict["isinstance"] = new PyBuiltinFunction("isinstance", IsInstance);
            module.ModuleDict["issubclass"] = new PyBuiltinFunction("issubclass", IsSubclass);
            module.ModuleDict["hasattr"] = new PyBuiltinFunction("hasattr", HasAttr);
            module.ModuleDict["getattr"] = new PyBuiltinFunction("getattr", GetAttr);
            module.ModuleDict["setattr"] = new PyBuiltinFunction("setattr", SetAttr);

            // Python 3.12 special attributes
            module.ModuleDict["__name__"] = new PyString("builtins");
            module.ModuleDict["__doc__"] = new PyString("Built-in functions, exceptions, and other objects.");

            return module;
        }

        // Built-in function implementations
        private static PyObject Abs(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"abs() takes exactly one argument ({args.Length} given)");

            var obj = args[0];
            return obj switch
            {
                PyInt pyInt => new PyInt(Math.Abs(pyInt.Value)),
                PyFloat pyFloat => new PyFloat(Math.Abs(pyFloat.Value)),
                _ => throw PyTypeError.Create($"bad operand type for abs(): '{obj.GetTypeName()}'")
            };
        }

        private static PyObject Len(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"len() takes exactly one argument ({args.Length} given)");

            return new PyInt(args[0].Length());
        }

        private static PyObject IsInstance(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"isinstance() takes exactly 2 arguments ({args.Length} given)");

            var obj = args[0];
            var classInfo = args[1];

            if (classInfo is PyType pyType)
            {
                return PyBool.FromBool(obj.GetPyType() == pyType);
            }
            else if (classInfo is PyTuple tuple)
            {
                foreach (var item in tuple.Items)
                {
                    if (item is PyType type && obj.GetPyType() == type)
                        return PyBool.True;
                }
                return PyBool.False;
            }

            throw PyTypeError.Create("isinstance() arg 2 must be a type or tuple of types");
        }

        private static PyObject IsSubclass(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"issubclass() takes exactly 2 arguments ({args.Length} given)");

            // Simple implementation - can be extended
            return PyBool.False;
        }

        private static PyObject HasAttr(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"hasattr() takes exactly 2 arguments ({args.Length} given)");

            var obj = args[0];
            var name = args[1];

            if (!(name is PyString nameStr))
                throw PyTypeError.Create("hasattr(): attribute name must be string");

            try
            {
                obj.GetAttribute(nameStr.Value);
                return PyBool.True;
            }
            catch
            {
                return PyBool.False;
            }
        }

        private static PyObject GetAttr(PyObject[] args)
        {
            if (args.Length < 2 || args.Length > 3)
                throw PyTypeError.Create($"getattr expected 2 or 3 arguments, got {args.Length}");

            var obj = args[0];
            var name = args[1];
            var defaultValue = args.Length > 2 ? args[2] : null;

            if (!(name is PyString nameStr))
                throw PyTypeError.Create("getattr(): attribute name must be string");

            try
            {
                return obj.GetAttribute(nameStr.Value);
            }
            catch
            {
                if (defaultValue != null)
                    return defaultValue;
                throw;
            }
        }

        private static PyObject SetAttr(PyObject[] args)
        {
            if (args.Length != 3)
                throw PyTypeError.Create($"setattr expected exactly 3 arguments, got {args.Length}");

            var obj = args[0];
            var name = args[1];
            var value = args[2];

            if (!(name is PyString nameStr))
                throw PyTypeError.Create("setattr(): attribute name must be string");

            obj.SetAttribute(nameStr.Value, value);
            return PyNone.Instance;
        }
    }
}
