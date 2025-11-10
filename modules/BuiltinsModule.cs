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
#if DEBUG_VM_LOG
            Console.WriteLine($"[BUILTINS] PyTypeMetaclass.Instance: {typeMetaclass.GetType().Name}");
            Console.WriteLine($"[BUILTINS] PyTypeMetaclass.ClassDict count: {typeMetaclass.ClassDict.Count}");
            Console.WriteLine($"[BUILTINS] PyTypeMetaclass.ClassDict keys: {string.Join(", ", typeMetaclass.ClassDict.Keys)}");
            Console.WriteLine($"[BUILTINS] Has __repr__: {typeMetaclass.ClassDict.ContainsKey("__repr__")}");
#endif
            module.ModuleDict["type"] = typeMetaclass;
            module.ModuleDict["int"] = PyType.IntType;
            module.ModuleDict["float"] = PyType.FloatType;
            module.ModuleDict["complex"] = PyType.ComplexType;
            module.ModuleDict["str"] = PyType.StrType;

            // Initialize str type descriptors (join, split, etc.)
            PyString.InitializeStringDescriptors();

            // Initialize dict type descriptors (get, keys, values, items, etc.)
            PyDict.InitializeDictDescriptors();

            // Initialize tuple type descriptors (count, index)
            PyTuple.InitializeTupleDescriptors();

            // Initialize int type descriptors (bit_length, bit_count, to_bytes, from_bytes, as_integer_ratio)
            PyInt.InitializeIntDescriptors();

            // Initialize float type descriptors (is_integer, as_integer_ratio, hex, fromhex, conjugate, real, imag)
            PyFloat.InitializeFloatDescriptors();

            module.ModuleDict["bool"] = PyType.BoolType;
            module.ModuleDict["list"] = PyType.ListType;
            module.ModuleDict["tuple"] = PyType.TupleType;
            module.ModuleDict["dict"] = PyType.DictType;
            module.ModuleDict["set"] = PyType.SetType;
            module.ModuleDict["frozenset"] = PyType.FrozenSetType;
            module.ModuleDict["bytes"] = PyType.BytesType;
            module.ModuleDict["bytearray"] = PyType.BytearrayType;
            module.ModuleDict["NoneType"] = PyType.NoneType;
            module.ModuleDict["function"] = PyType.FunctionType;
            module.ModuleDict["slice"] = PyType.SliceType;

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
            module.ModuleDict["BufferError"] = PyType.BufferErrorType;
            module.ModuleDict["OSError"] = PyType.OSErrorType;
            module.ModuleDict["FileNotFoundError"] = PyType.FileNotFoundErrorType;
            module.ModuleDict["EOFError"] = PyType.EOFErrorType;
            module.ModuleDict["MemoryError"] = PyType.MemoryErrorType;
            module.ModuleDict["UnicodeError"] = PyType.UnicodeErrorType;
            module.ModuleDict["UnicodeDecodeError"] = PyType.UnicodeDecodeErrorType;
            module.ModuleDict["UnicodeEncodeError"] = PyType.UnicodeEncodeErrorType;

            // Exception Groups (PEP 654 - Python 3.11+)
            module.ModuleDict["BaseExceptionGroup"] = PyType.BaseExceptionGroupType;
            module.ModuleDict["ExceptionGroup"] = PyType.ExceptionGroupType;

            // Warning types (Python 3.12)
            module.ModuleDict["Warning"] = PyType.WarningType;
            module.ModuleDict["UserWarning"] = PyType.UserWarningType;
            module.ModuleDict["DeprecationWarning"] = PyType.DeprecationWarningType;
            module.ModuleDict["PendingDeprecationWarning"] = PyType.PendingDeprecationWarningType;
            module.ModuleDict["SyntaxWarning"] = PyType.SyntaxWarningType;
            module.ModuleDict["RuntimeWarning"] = PyType.RuntimeWarningType;
            module.ModuleDict["FutureWarning"] = PyType.FutureWarningType;
            module.ModuleDict["ImportWarning"] = PyType.ImportWarningType;
            module.ModuleDict["UnicodeWarning"] = PyType.UnicodeWarningType;
            module.ModuleDict["BytesWarning"] = PyType.BytesWarningType;
            module.ModuleDict["ResourceWarning"] = PyType.ResourceWarningType;

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
            module.ModuleDict["eval"] = new PyBuiltinFunction("eval");
            module.ModuleDict["compile"] = new PyBuiltinFunction("compile");
            module.ModuleDict["exec"] = new PyBuiltinFunction("exec");
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

            // Python 3.12 number conversion builtins
            module.ModuleDict["bin"] = new PyBuiltinFunction("bin", Bin);
            module.ModuleDict["hex"] = new PyBuiltinFunction("hex", Hex);
            module.ModuleDict["oct"] = new PyBuiltinFunction("oct", Oct);
            module.ModuleDict["ascii"] = new PyBuiltinFunction("ascii", Ascii);
            module.ModuleDict["vars"] = new PyBuiltinFunction("vars", Vars);
            module.ModuleDict["format"] = new PyBuiltinFunction("format", Format);
            module.ModuleDict["memoryview"] = new PyBuiltinFunction("memoryview", MemoryView);

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
                PyComplex pyComplex => pyComplex.Absolute(),
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
                #if DEBUG
                Console.WriteLine($"[isinstance check_class] bases type: {bases?.GetType().Name}");
                #endif
                #if DEBUG
                Console.WriteLine($"[isinstance check_class] bases value: {bases}");
                #endif

                // If bases is a descriptor, we need to call it with classInfo
                // to get the actual __bases__ value
                if (bases is PyGetSetDescriptor getSetDescriptor)
                {
                    #if DEBUG
                    Console.WriteLine($"[isinstance check_class] Calling PyGetSetDescriptor.Get()");
                    #endif
                    // Call the descriptor's getter with classInfo as the instance
                    bases = getSetDescriptor.Get(classInfo, classInfo.GetPyType());
                    #if DEBUG
                    Console.WriteLine($"[isinstance check_class] After descriptor.Get(), bases type: {bases?.GetType().Name}");
                    #endif
                    #if DEBUG
                    Console.WriteLine($"[isinstance check_class] After descriptor.Get(), bases value: {bases}");
                    #endif
                }
                else if (bases is PyBasesDescriptor basesDescriptor)
                {
                    #if DEBUG
                    Console.WriteLine($"[isinstance check_class] Calling PyBasesDescriptor.Get()");
                    #endif
                    // Call the descriptor's getter with classInfo as the instance
                    bases = basesDescriptor.Get(classInfo, classInfo.GetPyType());
                    #if DEBUG
                    Console.WriteLine($"[isinstance check_class] After descriptor.Get(), bases type: {bases?.GetType().Name}");
                    #endif
                    #if DEBUG
                    Console.WriteLine($"[isinstance check_class] After descriptor.Get(), bases value: {bases}");
                    #endif
                }

                if (bases == null || !(bases is PyTuple))
                {
                    #if DEBUG
                    Console.WriteLine($"[isinstance check_class] ERROR: bases is not a PyTuple");
                    #endif
                    throw PyTypeError.Create("isinstance() arg 2 must be a type or tuple of types");
                }
                #if DEBUG
                Console.WriteLine($"[isinstance check_class] SUCCESS: bases is a PyTuple");
                #endif
            }
            catch (Exception ex)
            {
                #if DEBUG
                Console.WriteLine($"[isinstance check_class] EXCEPTION: {ex.Message}");
                #endif
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
                // For builtin types: check if obj's type matches OR is in MRO
                // Fast path: exact type match
                if (objType == pyTypeClass)
                    return PyBool.True;

                // Check MRO: objType might be a PyClass that subclasses pyTypeClass
                // Example: MyTuple (PyClass) subclasses tuple (PyType)
                if (objType is PyClass objClass)
                {
                    foreach (var mroType in objClass.MRO)
                    {
                        if (mroType == pyTypeClass)
                            return PyBool.True;
                    }
                }

                return PyBool.False;
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

            var subclass = args[0];
            var classInfo = args[1];

            // CPython 3.12: Check for tuple first (Objects/abstract.c:2701-2721)
            if (classInfo is PyTuple tuple)
            {
                foreach (var item in tuple.Items)
                {
                    // Recursively check each type in the tuple
                    var result = IsSubclass(new PyObject[] { subclass, item });
                    if (result == PyBool.True)
                        return PyBool.True;
                }
                return PyBool.False;
            }

            // CPython 3.12: Check if classInfo is a valid type by looking for __bases__
            try
            {
                var bases = classInfo.GetAttribute("__bases__");
                if (bases == null || !(bases is PyTuple))
                {
                    throw PyTypeError.Create("issubclass() arg 2 must be a class");
                }
            }
            catch
            {
                throw PyTypeError.Create("issubclass() arg 2 must be a class");
            }

            // Check if subclass is a valid type
            try
            {
                var subBases = subclass.GetAttribute("__bases__");
                if (subBases == null || !(subBases is PyTuple))
                {
                    throw PyTypeError.Create("issubclass() arg 1 must be a class");
                }
            }
            catch
            {
                throw PyTypeError.Create("issubclass() arg 1 must be a class");
            }

            // CPython 3.12: Check for __subclasscheck__ on the metaclass of classInfo
            // Objects/abstract.c:2727-2739 (PyObject_IsSubclass)
            var metaclass = classInfo.GetPyType();

            try
            {
                var subclasscheck = metaclass.GetAttribute("__subclasscheck__");

                if (subclasscheck != null && subclasscheck != PyNone.Instance)
                {
                    // Call __subclasscheck__(cls, subclass)
                    var result = subclasscheck.Call(new PyObject[] { classInfo, subclass }, null);
                    return result;
                }
            }
            catch (Exception ex)
            {
                // If __subclasscheck__ lookup fails, fall through to default behavior
            }

            // Fallback: check MRO directly
            if (subclass is PyClass subPyClass && classInfo is PyClass classPyClass)
            {
                foreach (var mroType in subPyClass.MRO)
                {
                    if (ReferenceEquals(mroType, classPyClass))
                        return PyBool.True;
                }
            }

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

        // CPython 3.12: Python/bltinmodule.c:560 - builtin_bin
        private static PyObject Bin(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"bin() takes exactly one argument ({args.Length} given)");

            var obj = args[0];

            // Try to get __index__ method for integer conversion
            long value;
            if (obj is PyInt pyInt)
            {
                value = pyInt.Value;
            }
            else if (obj is PyBool pyBool)
            {
                value = pyBool.Value ? 1 : 0;
            }
            else
            {
                // Try to call __index__ method
                var indexMethod = obj.GetAttribute("__index__");
                if (indexMethod != null && indexMethod != PyNone.Instance)
                {
                    var result = indexMethod.Call(Array.Empty<PyObject>(), null);
                    if (result is PyInt indexInt)
                    {
                        value = indexInt.Value;
                    }
                    else
                    {
                        throw PyTypeError.Create($"__index__ returned non-int (type {result.GetTypeName()})");
                    }
                }
                else
                {
                    throw PyTypeError.Create($"'{obj.GetTypeName()}' object cannot be interpreted as an integer");
                }
            }

            // Convert to binary string with "0b" prefix
            if (value >= 0)
            {
                return new PyString("0b" + Convert.ToString(value, 2));
            }
            else
            {
                // Negative numbers: "-0b..." format
                return new PyString("-0b" + Convert.ToString(-value, 2));
            }
        }

        // CPython 3.12: Python/bltinmodule.c:1133 - builtin_hex
        private static PyObject Hex(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"hex() takes exactly one argument ({args.Length} given)");

            var obj = args[0];

            // Try to get __index__ method for integer conversion
            long value;
            if (obj is PyInt pyInt)
            {
                value = pyInt.Value;
            }
            else if (obj is PyBool pyBool)
            {
                value = pyBool.Value ? 1 : 0;
            }
            else
            {
                // Try to call __index__ method
                var indexMethod = obj.GetAttribute("__index__");
                if (indexMethod != null && indexMethod != PyNone.Instance)
                {
                    var result = indexMethod.Call(Array.Empty<PyObject>(), null);
                    if (result is PyInt indexInt)
                    {
                        value = indexInt.Value;
                    }
                    else
                    {
                        throw PyTypeError.Create($"__index__ returned non-int (type {result.GetTypeName()})");
                    }
                }
                else
                {
                    throw PyTypeError.Create($"'{obj.GetTypeName()}' object cannot be interpreted as an integer");
                }
            }

            // Convert to hexadecimal string with "0x" prefix
            if (value >= 0)
            {
                return new PyString("0x" + value.ToString("x"));
            }
            else
            {
                // Negative numbers: "-0x..." format
                return new PyString("-0x" + (-value).ToString("x"));
            }
        }

        // CPython 3.12: Python/bltinmodule.c:1664 - builtin_oct
        private static PyObject Oct(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"oct() takes exactly one argument ({args.Length} given)");

            var obj = args[0];

            // Try to get __index__ method for integer conversion
            long value;
            if (obj is PyInt pyInt)
            {
                value = pyInt.Value;
            }
            else if (obj is PyBool pyBool)
            {
                value = pyBool.Value ? 1 : 0;
            }
            else
            {
                // Try to call __index__ method
                var indexMethod = obj.GetAttribute("__index__");
                if (indexMethod != null && indexMethod != PyNone.Instance)
                {
                    var result = indexMethod.Call(Array.Empty<PyObject>(), null);
                    if (result is PyInt indexInt)
                    {
                        value = indexInt.Value;
                    }
                    else
                    {
                        throw PyTypeError.Create($"__index__ returned non-int (type {result.GetTypeName()})");
                    }
                }
                else
                {
                    throw PyTypeError.Create($"'{obj.GetTypeName()}' object cannot be interpreted as an integer");
                }
            }

            // Convert to octal string with "0o" prefix
            if (value >= 0)
            {
                return new PyString("0o" + Convert.ToString(value, 8));
            }
            else
            {
                // Negative numbers: "-0o..." format
                return new PyString("-0o" + Convert.ToString(-value, 8));
            }
        }

        // CPython 3.12: Python/bltinmodule.c:537 - builtin_ascii
        private static PyObject Ascii(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"ascii() takes exactly one argument ({args.Length} given)");

            var obj = args[0];

            // Get repr() of the object
            var reprStr = obj.ToRepr();
            if (reprStr is not PyString pyStr)
            {
                throw PyTypeError.Create($"__repr__ returned non-string (type {reprStr.GetTypeName()})");
            }

            // Escape non-ASCII characters
            var result = new System.Text.StringBuilder();
            foreach (char c in pyStr.Value)
            {
                if (c < 128)
                {
                    result.Append(c);
                }
                else if (c <= 0xFF)
                {
                    result.Append($"\\x{(int)c:x2}");
                }
                else if (c <= 0xFFFF)
                {
                    result.Append($"\\u{(int)c:x4}");
                }
                else
                {
                    result.Append($"\\U{(int)c:x8}");
                }
            }

            return new PyString(result.ToString());
        }

        // CPython 3.12: Python/bltinmodule.c:3079 - builtin_vars
        private static PyObject Vars(PyObject[] args)
        {
            if (args.Length == 0)
            {
                // No argument: return locals()
                // This should return the current local symbol table
                // For now, throw NotImplementedError as it requires VM integration
                throw PyNotImplementedError.Create("vars() without arguments requires locals() implementation");
            }
            else if (args.Length == 1)
            {
                var obj = args[0];

                // Try to get __dict__ attribute
                var dictAttr = obj.GetAttribute("__dict__");
                if (dictAttr != null && dictAttr != PyNone.Instance)
                {
                    return dictAttr;
                }
                else
                {
                    throw PyTypeError.Create($"vars() argument must have __dict__ attribute");
                }
            }
            else
            {
                throw PyTypeError.Create($"vars() takes at most 1 argument ({args.Length} given)");
            }
        }

        // CPython 3.12: Python/bltinmodule.c:689 - builtin_format
        // format(value[, format_spec]) -> string
        // Returns value.__format__(format_spec)
        private static PyObject Format(PyObject[] args)
        {
            if (args.Length < 1 || args.Length > 2)
                throw PyTypeError.Create($"format() takes 1 or 2 arguments ({args.Length} given)");

            var value = args[0];
            var formatSpec = args.Length == 2 ? args[1] : new PyString("");

            // format_spec must be a string
            if (formatSpec is not PyString formatStr)
            {
                throw PyTypeError.Create($"format() argument 2 must be str, not {formatSpec.GetTypeName()}");
            }

            // Try to call __format__ method
            try
            {
                var formatMethod = value.GetAttribute("__format__");
                if (formatMethod != null && formatMethod != PyNone.Instance)
                {
                    // Call __format__(format_spec)
                    var result = formatMethod.Call(new PyObject[] { formatStr }, null);

                    // Result must be a string
                    if (result is not PyString)
                    {
                        throw PyTypeError.Create($"__format__ must return a str, not {result.GetTypeName()}");
                    }

                    return result;
                }
            }
            catch (Exception ex) when (ex.Message.Contains("AttributeError") || ex.Message.Contains("has no attribute"))
            {
                // __format__ attribute doesn't exist, fall through to default handling
            }

            // If no __format__ method or AttributeError, use str() for empty format_spec
            if (formatStr.Value == "")
            {
                return value.ToStr();
            }
            else
            {
                throw PyTypeError.Create($"unsupported format string passed to {value.GetTypeName()}.__format__");
            }
        }

        // CPython 3.12: Objects/memoryobject.c:850 - PyMemoryView_FromObject
        // memoryview(object) -> memoryview
        // Create a new memoryview object which references the given object
        private static PyObject MemoryView(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"memoryview() takes exactly one argument ({args.Length} given)");

            var obj = args[0];

            // Try to get buffer from object using buffer protocol
            if (obj.SupportsBuffer())
            {
                // Object implements buffer protocol, get buffer
                return obj.GetBuffer(0);  // 0 = PyBUF_FULL_RO
            }

            // Check if object has __buffer__ method
            try
            {
                var bufferMethod = obj.GetAttribute("__buffer__");
                if (bufferMethod != null && bufferMethod != PyNone.Instance)
                {
                    var result = bufferMethod.Call(Array.Empty<PyObject>(), null);
                    if (result is PyMemoryView memView)
                    {
                        return memView;
                    }
                    throw PyTypeError.Create($"__buffer__ returned non-memoryview (type {result.GetTypeName()})");
                }
            }
            catch (Exception ex) when (ex.Message.Contains("AttributeError") || ex.Message.Contains("has no attribute"))
            {
                // __buffer__ doesn't exist, continue with error
            }

            throw PyTypeError.Create($"memoryview: a bytes-like object is required, not '{obj.GetTypeName()}'");
        }
    }
}
