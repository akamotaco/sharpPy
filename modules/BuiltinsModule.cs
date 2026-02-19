using System;
using System.Numerics;

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

            // CPython 3.12: exit and quit objects (Lib/_sitebuiltins.py:21-42)
            module.ModuleDict["exit"] = new PyQuitter("exit");
            module.ModuleDict["quit"] = new PyQuitter("quit");

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
            module.ModuleDict["SystemError"] = PyType.SystemErrorType;
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

            // CPython 3.12: Python/bltinmodule.c:246-278
            // __import__ is registered without explicit implementation here.
            // It uses the fallback lookup in PyBuiltinFunction._builtinImplementations["__import__"]
            // which properly handles all parameters: name, globals, locals, fromlist, level
            module.ModuleDict["__import__"] = new PyBuiltinFunction("__import__");

            // Add property, classmethod, staticmethod (required by enum module)
            // CPython 3.12: These should be TYPE objects, not functions
            module.ModuleDict["property"] = new PyBuiltinFunction("property");
            module.ModuleDict["classmethod"] = PyType.ClassMethodType; // Type object
            module.ModuleDict["staticmethod"] = PyType.StaticMethodType; // Type object

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
            module.ModuleDict["help"] = new PyBuiltinFunction("help");
            module.ModuleDict["breakpoint"] = new PyBuiltinFunction("breakpoint");

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

            // CPython 3.12: Python/bltinmodule.c:2340-2355 - builtin_abs
            var obj = args[0];
            return obj switch
            {
                PyInt pyInt => new PyInt(BigInteger.Abs(pyInt.Value)),
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

        /// <summary>
        /// CPython 3.12: Objects/abstract.c:2666-2671 (PyObject_IsInstance)
        /// Implements isinstance(obj, classInfo) builtin function.
        /// </summary>
        private static PyObject IsInstance(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"isinstance() takes exactly 2 arguments ({args.Length} given)");

            var obj = args[0];
            var classInfo = args[1];

            return PyBool.FromBool(ObjectRecursiveIsInstance(obj, classInfo));
        }

        /// <summary>
        /// CPython 3.12: Objects/abstract.c:2602-2663 (object_recursive_isinstance)
        /// Recursive isinstance check with tuple support.
        /// </summary>
        private static bool ObjectRecursiveIsInstance(PyObject inst, PyObject cls)
        {
            // CPython 3.12: Objects/abstract.c:2604-2607
            // Quick test for an exact match
            if (inst.GetPyType() == cls)
            {
                return true;
            }

            // CPython 3.12: Objects/abstract.c:2609-2612
            // We know what type's __instancecheck__ does.
            if (cls is PyType pyTypeExact)
            {
                return ObjectIsInstance(inst, cls);
            }

            // CPython 3.12: Objects/abstract.c:2618-2636
            // Check for tuple (recursively)
            if (cls is PyTuple tuple)
            {
                for (int i = 0; i < tuple.Items.Length; i++)
                {
                    bool r = ObjectRecursiveIsInstance(inst, tuple.Items[i]);
                    if (r)
                    {
                        return true;
                    }
                }
                return false;
            }

            // CPython 3.12: Objects/abstract.c:2638-2656
            // Check for __instancecheck__ (custom metaclasses)
            // For now, fall back to object_isinstance
            return ObjectIsInstance(inst, cls);
        }

        /// <summary>
        /// CPython 3.12: Objects/abstract.c:2566-2599 (object_isinstance)
        /// Core isinstance implementation.
        /// </summary>
        private static bool ObjectIsInstance(PyObject inst, PyObject cls)
        {
            // CPython 3.12: Objects/abstract.c:2570-2586
            // Fast path for PyType
            if (cls is PyType pyType)
            {
                // Check if inst's type matches or is subclass of cls
                var instType = inst.GetPyType();
                if (instType == pyType)
                    return true;

                // CPython 3.12: Objects/abstract.c:2573-2585
                // Check inst.__class__ if it differs from type(inst)
                PyObject icls = inst.LookupAttribute("__class__");
                if (icls != null)
                {
                    try
                    {
                        if (icls != instType && icls is PyType iclsType)
                        {
                            // Check if icls is subclass of cls
                            return IsSubtypeOf(iclsType, pyType);
                        }
                    }
                    finally
                    {
                        // In real CPython, we would Py_DECREF(icls) here
                    }
                }

                // Check MRO of inst's type
                return IsSubtypeOf(instType, pyType);
            }

            // CPython 3.12: Objects/abstract.c:2588-2596
            // cls is not a PyType, validate it has __bases__
            if (!CheckClass(cls, "isinstance() arg 2 must be a type, a tuple of types, or a union"))
            {
                throw PyTypeError.Create("isinstance() arg 2 must be a type, a tuple of types, or a union");
            }

            // Get inst.__class__
            PyObject instClass = inst.LookupAttribute("__class__");
            if (instClass != null)
            {
                try
                {
                    return AbstractIsSubclass(instClass, cls);
                }
                finally
                {
                    // In real CPython, we would Py_DECREF(instClass) here
                }
            }

            return false;
        }

        /// <summary>
        /// CPython 3.12: Objects/abstract.c:2498-2563 (abstract_issubclass)
        /// Check if derived is a subclass of cls.
        /// </summary>
        private static bool AbstractIsSubclass(PyObject derived, PyObject cls)
        {
            PyObject bases = null;

            while (true)
            {
                // CPython 3.12: Objects/abstract.c:2506-2509
                if (derived == cls)
                {
                    return true;
                }

                // CPython 3.12: Objects/abstract.c:2515 (abstract_get_bases)
                bases = AbstractGetBases(derived);
                if (bases == null)
                {
                    return false;
                }

                if (!(bases is PyTuple tuple))
                {
                    return false;
                }

                // CPython 3.12: Objects/abstract.c:2521-2525
                int n = tuple.Items.Length;
                if (n == 0)
                {
                    return false;
                }

                // CPython 3.12: Objects/abstract.c:2526-2530
                // Avoid recursivity in the single inheritance case
                if (n == 1)
                {
                    derived = tuple.Items[0];
                    continue;
                }

                // CPython 3.12: Objects/abstract.c:2531-2544
                // Multiple inheritance - check each base
                for (int i = 0; i < n; i++)
                {
                    bool r = AbstractIsSubclass(tuple.Items[i], cls);
                    if (r)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// CPython 3.12: Objects/abstract.c:2484-2495 (abstract_get_bases)
        /// Get __bases__ attribute, suppressing AttributeError.
        /// Returns null if __bases__ doesn't exist or is not a tuple.
        /// </summary>
        private static PyObject AbstractGetBases(PyObject cls)
        {
            // CPython 3.12: Objects/abstract.c:2489
            // (void)_PyObject_LookupAttr(cls, &_Py_ID(__bases__), &bases);
            PyObject bases = cls.LookupAttribute("__bases__");

            // CPython 3.12: Objects/abstract.c:2490-2493
            if (bases != null && !(bases is PyTuple))
            {
                // Not a tuple, return null
                return null;
            }

            return bases;
        }

        /// <summary>
        /// Check if cls is a valid class (has __bases__ attribute).
        /// CPython 3.12: Objects/abstract.c:2431-2456 (check_class)
        /// </summary>
        private static bool CheckClass(PyObject cls, string errorMessage)
        {
            // For PyType/PyClass, we know they're valid
            if (cls is PyType || cls is PyClass)
            {
                return true;
            }

            // CPython 3.12: Objects/abstract.c:2440-2446
            // Check if __bases__ exists
            PyObject bases = AbstractGetBases(cls);
            return bases != null;
        }

        /// <summary>
        /// Check if derived type is subtype of base type.
        /// Handles both PyType and PyClass.
        /// </summary>
        private static bool IsSubtypeOf(PyType derived, PyType baseType)
        {
            // Check MRO
            foreach (var mroType in derived.MRO)
            {
                if (mroType == baseType)
                    return true;
            }
            return false;
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
            // CPython 3.12: Objects/abstract.c:2498-2563 (abstract_issubclass)
            // Handle PyType (built-in types like int, bool, str, etc.)
            if (subclass is PyType subPyType && classInfo is PyType classPyType)
            {
                return PyBool.FromBool(subPyType.IsSubclassOf(classPyType));
            }

            // Handle PyClass (user-defined classes)
            if (subclass is PyClass subPyClass && classInfo is PyClass classPyClass)
            {
                foreach (var mroType in subPyClass.MRO)
                {
                    if (ReferenceEquals(mroType, classPyClass))
                        return PyBool.True;
                }
            }

            // Handle mixed case: PyClass subclass, PyType classInfo (e.g., MyClass subclass of object)
            if (subclass is PyClass subPyClassMixed && classInfo is PyType classPyTypeMixed)
            {
                // Check if any type in the MRO matches the classInfo PyType
                foreach (var mroType in subPyClassMixed.MRO)
                {
                    if (mroType is PyType mroTypeAsType && ReferenceEquals(mroTypeAsType, classPyTypeMixed))
                        return PyBool.True;
                }
                // Check if classInfo is 'object' type - all classes inherit from object
                if (classPyTypeMixed == PyType.ObjectType)
                    return PyBool.True;
            }

            return PyBool.False;
        }

        // CPython 3.12: Python/bltinmodule.c builtin_hasattr_impl
        // Uses _PyObject_LookupAttr() which only suppresses AttributeError.
        // Non-AttributeError exceptions are propagated.
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
            catch (PythonException ex) when (ex.PyException is PyAttributeError)
            {
                return PyBool.False;
            }
        }

        // CPython 3.12: Python/bltinmodule.c builtin_getattr
        // With default: uses _PyObject_LookupAttr() which only suppresses AttributeError.
        // Without default: uses PyObject_GetAttr() which propagates all exceptions.
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
            catch (PythonException ex) when (ex.PyException is PyAttributeError)
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

            // CPython 3.12: Python/bltinmodule.c - integer conversion via __index__
            // BigInteger support for arbitrary precision integers
            PyInt intValue;
            if (obj is PyInt pyInt)
            {
                intValue = pyInt;
            }
            else if (obj is PyBool pyBool)
            {
                intValue = new PyInt(pyBool.Value ? 1 : 0);
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
                        intValue = indexInt;
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

            // Delegate to PyInt.Bin() for proper BigInteger formatting
            return intValue.Bin();
        }

        // CPython 3.12: Python/bltinmodule.c:1133 - builtin_hex
        private static PyObject Hex(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"hex() takes exactly one argument ({args.Length} given)");

            var obj = args[0];

            // CPython 3.12: Python/bltinmodule.c - integer conversion via __index__
            // BigInteger support for arbitrary precision integers
            PyInt intValue;
            if (obj is PyInt pyInt)
            {
                intValue = pyInt;
            }
            else if (obj is PyBool pyBool)
            {
                intValue = new PyInt(pyBool.Value ? 1 : 0);
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
                        intValue = indexInt;
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

            // Delegate to PyInt.Hex() for proper BigInteger formatting
            return intValue.Hex();
        }

        // CPython 3.12: Python/bltinmodule.c:1664 - builtin_oct
        private static PyObject Oct(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"oct() takes exactly one argument ({args.Length} given)");

            var obj = args[0];

            // CPython 3.12: Python/bltinmodule.c - integer conversion via __index__
            // BigInteger support for arbitrary precision integers
            PyInt intValue;
            if (obj is PyInt pyInt)
            {
                intValue = pyInt;
            }
            else if (obj is PyBool pyBool)
            {
                intValue = new PyInt(pyBool.Value ? 1 : 0);
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
                        intValue = indexInt;
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

            // Delegate to PyInt.Oct() for proper BigInteger formatting
            return intValue.Oct();
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
