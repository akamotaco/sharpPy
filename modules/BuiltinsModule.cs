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

            // CPython 3.12: Use actual type metaclass with methods
            var typeMetaclass = PyTypeMetaclass.Instance;
            Console.WriteLine($"[BUILTINS] PyTypeMetaclass.Instance: {typeMetaclass.GetType().Name}");
            Console.WriteLine($"[BUILTINS] PyTypeMetaclass.ClassDict count: {typeMetaclass.ClassDict.Count}");
            Console.WriteLine($"[BUILTINS] PyTypeMetaclass.ClassDict keys: {string.Join(", ", typeMetaclass.ClassDict.Keys)}");
            Console.WriteLine($"[BUILTINS] Has __repr__: {typeMetaclass.ClassDict.ContainsKey("__repr__")}");
            module.ModuleDict["type"] = typeMetaclass;
            module.ModuleDict["int"] = PyType.IntType;
            module.ModuleDict["float"] = PyType.FloatType;
            module.ModuleDict["str"] = PyType.StrType;

            // Initialize str type descriptors (join, split, etc.)
            PyString.InitializeStringDescriptors();

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
            module.ModuleDict["Ellipsis"] = PyEllipsis.Instance;

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

            // Add property, classmethod, staticmethod (required by enum module)
            module.ModuleDict["property"] = new PyBuiltinFunction("property");
            module.ModuleDict["classmethod"] = new PyBuiltinFunction("classmethod");
            module.ModuleDict["staticmethod"] = new PyBuiltinFunction("staticmethod");

            // Add other commonly used builtins
            module.ModuleDict["print"] = new PyBuiltinFunction("print");
            module.ModuleDict["input"] = new PyBuiltinFunction("input");
            module.ModuleDict["repr"] = new PyBuiltinFunction("repr");
            module.ModuleDict["id"] = new PyBuiltinFunction("id");
            module.ModuleDict["hash"] = new PyBuiltinFunction("hash");
            module.ModuleDict["callable"] = new PyBuiltinFunction("callable");
            module.ModuleDict["dir"] = new PyBuiltinFunction("dir");
            module.ModuleDict["iter"] = new PyBuiltinFunction("iter");
            module.ModuleDict["next"] = new PyBuiltinFunction("next");
            module.ModuleDict["range"] = new PyBuiltinFunction("range");
            module.ModuleDict["enumerate"] = new PyBuiltinFunction("enumerate");
            module.ModuleDict["zip"] = new PyBuiltinFunction("zip");
            module.ModuleDict["map"] = new PyBuiltinFunction("map");
            module.ModuleDict["filter"] = new PyBuiltinFunction("filter");
            module.ModuleDict["sorted"] = new PyBuiltinFunction("sorted");
            module.ModuleDict["reversed"] = new PyBuiltinFunction("reversed");
            module.ModuleDict["sum"] = new PyBuiltinFunction("sum");
            module.ModuleDict["min"] = new PyBuiltinFunction("min");
            module.ModuleDict["max"] = new PyBuiltinFunction("max");
            module.ModuleDict["any"] = new PyBuiltinFunction("any");
            module.ModuleDict["all"] = new PyBuiltinFunction("all");
            module.ModuleDict["delattr"] = new PyBuiltinFunction("delattr");
            module.ModuleDict["super"] = new PyBuiltinFunction("super");
            module.ModuleDict["round"] = new PyBuiltinFunction("round");
            module.ModuleDict["pow"] = new PyBuiltinFunction("pow");
            module.ModuleDict["divmod"] = new PyBuiltinFunction("divmod");
            module.ModuleDict["ord"] = new PyBuiltinFunction("ord");
            module.ModuleDict["chr"] = new PyBuiltinFunction("chr");
            module.ModuleDict["open"] = new PyBuiltinFunction("open");
            module.ModuleDict["globals"] = new PyBuiltinFunction("globals");
            module.ModuleDict["locals"] = new PyBuiltinFunction("locals");

            // CPython 3.12: __build_class__ is a builtin function for class creation
            module.ModuleDict["__build_class__"] = new PyBuiltinFunction("__build_class__");

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

            // CPython 3.12: Check for tuple first
            if (classInfo is PyTuple tuple)
            {
                foreach (var item in tuple.Items)
                {
                    // Recursively check each type in the tuple
                    var result = IsInstance(new PyObject[] { obj, item });
                    if (result == PyBool.True)
                        return PyBool.True;
                }
                return PyBool.False;
            }

            // CPython 3.12: Check if classInfo is a valid type by looking for __bases__
            // This works for both PyType and PyClass (including type metaclass)
            PyObject bases = null;
            try
            {
                bases = classInfo.GetAttribute("__bases__");
                Console.WriteLine($"[isinstance check_class] bases type: {bases?.GetType().Name}");
                Console.WriteLine($"[isinstance check_class] bases value: {bases}");

                // If bases is a descriptor, we need to call it with classInfo
                // to get the actual __bases__ value
                if (bases is PyGetSetDescriptor getSetDescriptor)
                {
                    Console.WriteLine($"[isinstance check_class] Calling PyGetSetDescriptor.Get()");
                    // Call the descriptor's getter with classInfo as the instance
                    bases = getSetDescriptor.Get(classInfo, classInfo.GetPyType());
                    Console.WriteLine($"[isinstance check_class] After descriptor.Get(), bases type: {bases?.GetType().Name}");
                    Console.WriteLine($"[isinstance check_class] After descriptor.Get(), bases value: {bases}");
                }
                else if (bases is PyBasesDescriptor basesDescriptor)
                {
                    Console.WriteLine($"[isinstance check_class] Calling PyBasesDescriptor.Get()");
                    // Call the descriptor's getter with classInfo as the instance
                    bases = basesDescriptor.Get(classInfo, classInfo.GetPyType());
                    Console.WriteLine($"[isinstance check_class] After descriptor.Get(), bases type: {bases?.GetType().Name}");
                    Console.WriteLine($"[isinstance check_class] After descriptor.Get(), bases value: {bases}");
                }

                if (bases == null || !(bases is PyTuple))
                {
                    Console.WriteLine($"[isinstance check_class] ERROR: bases is not a PyTuple");
                    throw PyTypeError.Create("isinstance() arg 2 must be a type or tuple of types");
                }
                Console.WriteLine($"[isinstance check_class] SUCCESS: bases is a PyTuple");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[isinstance check_class] EXCEPTION: {ex.Message}");
                throw PyTypeError.Create("isinstance() arg 2 must be a type or tuple of types");
            }

            // Now perform the actual isinstance check
            // CPython 3.12: First check if obj.__class__ is a subclass of classInfo
            var objType = obj.GetPyType();

            // Quick check: exact type match
            if (objType == classInfo)
                return PyBool.True;

            // Check MRO if available
            if (classInfo is PyType pyTypeClass)
            {
                // For builtin types: check if obj's type matches
                return PyBool.FromBool(objType == pyTypeClass);
            }
            else if (classInfo is PyClass pyClass)
            {
                // For user-defined classes: check instance
                if (obj is PyClassInstance instance)
                {
                    foreach (var mroType in instance.InstanceType.MRO)
                    {
                        if (mroType == pyClass)
                            return PyBool.True;
                    }
                }
                // Check if obj itself is a type/class that is subclass of pyClass
                else if (obj is PyType || obj is PyClass)
                {
                    // isinstance(int, type) should return True
                    // Check if obj (which is a type) has classInfo in its MRO
                    try
                    {
                        var objBases = obj.GetAttribute("__bases__");
                        if (objBases is PyTuple objBasesTuple)
                        {
                            // obj is a type, check if classInfo is in its metaclass chain
                            // For isinstance(int, type), we need to check if int is an instance of type
                            // This means: type(int) should be type (or subclass of type)
                            var objMetaclass = obj.GetPyType();
                            if (objMetaclass == classInfo)
                                return PyBool.True;

                            // Check MRO of the metaclass
                            if (objMetaclass is PyClass metaClass)
                            {
                                foreach (var mroType in metaClass.MRO)
                                {
                                    if (mroType == pyClass)
                                        return PyBool.True;
                                }
                            }
                        }
                    }
                    catch { }
                }
                return PyBool.False;
            }

            return PyBool.False;
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
