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
            BuiltinSlots.Install();
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
            module.ModuleDict["slice"] = PyType.SliceType;

            // Built-in constants
            module.ModuleDict["None"] = PyNone.Instance;
            module.ModuleDict["True"] = PyBool.True;
            module.ModuleDict["__debug__"] = PyBool.True;
            module.ModuleDict["False"] = PyBool.False;
            module.ModuleDict["NotImplemented"] = PyNotImplemented.Instance;
            module.ModuleDict["Ellipsis"] = PyEllipsis.Instance;

            // CPython 3.12: exit and quit objects (Lib/_sitebuiltins.py:21-42)
            module.ModuleDict["exit"] = new PyQuitter("exit");
            module.ModuleDict["quit"] = new PyQuitter("quit");

            // Exception types (most important for enum module)
            module.ModuleDict["BaseException"] = PyType.BaseExceptionType;
            module.ModuleDict["GeneratorExit"] = PyType.GeneratorExitType;
            // Python/pylifecycle.c and Objects/exceptions.c make these two BaseException members
            // visible in builtins; they were the reason `except SystemExit` and
            // `BaseExceptionGroup` rules could not be written in Python (known_gaps item 4).
            // MRO measured: SystemExit -> BaseException -> object, same for KeyboardInterrupt.
            module.ModuleDict["SystemExit"] = PyType.SystemExitType;
            module.ModuleDict["KeyboardInterrupt"] = PyType.KeyboardInterruptType;
            module.ModuleDict["Exception"] = PyType.ExceptionType;
            module.ModuleDict["StopIteration"] = PyType.StopIterationType;
            module.ModuleDict["StopAsyncIteration"] = PyType.StopAsyncIterationType;
            module.ModuleDict["TypeError"] = PyType.TypeErrorType;
            module.ModuleDict["ValueError"] = PyType.ValueErrorType;
            module.ModuleDict["LookupError"] = PyType.LookupErrorType;
            module.ModuleDict["ArithmeticError"] = PyType.ArithmeticErrorType;
            module.ModuleDict["FloatingPointError"] = PyType.FloatingPointErrorType;
            module.ModuleDict["TabError"] = PyType.TabErrorType;
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
            // Objects/exceptions.c: ReferenceError is raised by a dead weak reference and by nothing
            // else, but it is a builtins name today, so it is registered with the rest of them.
            module.ModuleDict["ReferenceError"] = PyType.ReferenceErrorType;
            module.ModuleDict["OSError"] = PyType.OSErrorType;
            module.ModuleDict["IOError"] = PyType.OSErrorType;
            module.ModuleDict["EnvironmentError"] = PyType.OSErrorType;
            module.ModuleDict["ConnectionError"] = PyType.ConnectionErrorType;
            module.ModuleDict["BrokenPipeError"] = PyType.BrokenPipeErrorType;
            module.ModuleDict["ConnectionAbortedError"] = PyType.ConnectionAbortedErrorType;
            module.ModuleDict["ConnectionRefusedError"] = PyType.ConnectionRefusedErrorType;
            module.ModuleDict["ConnectionResetError"] = PyType.ConnectionResetErrorType;
            module.ModuleDict["ChildProcessError"] = PyType.ChildProcessErrorType;
            module.ModuleDict["InterruptedError"] = PyType.InterruptedErrorType;
            module.ModuleDict["ProcessLookupError"] = PyType.ProcessLookupErrorType;
            module.ModuleDict["TimeoutError"] = PyType.TimeoutErrorType;
            module.ModuleDict["BlockingIOError"] = PyType.BlockingIOErrorType;
            module.ModuleDict["FileNotFoundError"] = PyType.FileNotFoundErrorType;
            module.ModuleDict["PermissionError"] = PyType.PermissionErrorType;
            module.ModuleDict["FileExistsError"] = PyType.FileExistsErrorType;
            module.ModuleDict["NotADirectoryError"] = PyType.NotADirectoryErrorType;
            module.ModuleDict["IsADirectoryError"] = PyType.IsADirectoryErrorType;
            module.ModuleDict["EOFError"] = PyType.EOFErrorType;
            module.ModuleDict["MemoryError"] = PyType.MemoryErrorType;
            module.ModuleDict["UnicodeError"] = PyType.UnicodeErrorType;
            module.ModuleDict["UnicodeDecodeError"] = PyType.UnicodeDecodeErrorType;
            module.ModuleDict["UnicodeEncodeError"] = PyType.UnicodeEncodeErrorType;
            module.ModuleDict["UnicodeTranslateError"] = PyType.UnicodeTranslateErrorType;

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
            module.ModuleDict["EncodingWarning"] = PyType.EncodingWarningType;

            // Built-in functions (commonly used in standard library)
            module.ModuleDict["abs"] = new PyBuiltinFunction("abs", Abs);
            module.ModuleDict["len"] = new PyBuiltinFunction("len", Len);
            module.ModuleDict["isinstance"] = new PyBuiltinFunction("isinstance", IsInstance);
            module.ModuleDict["aiter"] = new PyBuiltinFunction("aiter", args => PyVM.Aiter(args));
            module.ModuleDict["anext"] = new PyBuiltinFunction("anext", args => PyVM.Anext(args));
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                module.ModuleDict["WindowsError"] = module.ModuleDict["OSError"];
            module.ModuleDict["issubclass"] = new PyBuiltinFunction("issubclass", IsSubclass);
            // hasattr/getattr/setattr used to have a second implementation here as well, and this one
            // won because it was passed as the delegate. It caught every exception, so
            // getattr(obj, "x", "D") returned "D" for a ValueError raised inside a user __getattr__.
            // There is one implementation now: PyBuiltinFunction._builtinImplementations.
            module.ModuleDict["hasattr"] = new PyBuiltinFunction("hasattr");
            module.ModuleDict["getattr"] = new PyBuiltinFunction("getattr");
            module.ModuleDict["setattr"] = new PyBuiltinFunction("setattr");

            // CPython 3.12: Python/bltinmodule.c:246-278
            // __import__ is registered without explicit implementation here.
            // It uses the fallback lookup in PyBuiltinFunction._builtinImplementations["__import__"]
            // which properly handles all parameters: name, globals, locals, fromlist, level
            module.ModuleDict["__import__"] = new PyBuiltinFunction("__import__");

            // Add property, classmethod, staticmethod (required by enum module)
            // CPython 3.12: These should be TYPE objects, not functions
            PyProperty.InitializePropertyDescriptors();
            module.ModuleDict["property"] = PyType.PropertyType;
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
            module.ModuleDict["range"] = BuiltinCallableType(PyType.RangeType, "range");
            module.ModuleDict["enumerate"] = BuiltinCallableType(PyType.GetIteratorType("enumerate"), "enumerate");
            module.ModuleDict["zip"] = BuiltinCallableType(PyType.ZipType, "zip");
            // map_new and filter_new: "(type == base || type->tp_init == base->tp_init) && !_PyArg_NoKeywords".
            module.ModuleDict["map"] = BuiltinCallableType(PyType.MapType, new PyBuiltinFunction("map"), keywordsForInit: true);
            module.ModuleDict["filter"] = BuiltinCallableType(PyType.FilterType, new PyBuiltinFunction("filter"), keywordsForInit: true);
            module.ModuleDict["sorted"] = new PyBuiltinFunction("sorted");
            module.ModuleDict["reversed"] = BuiltinCallableType(PyType.ReversedType, "reversed");
            module.ModuleDict["sum"] = new PyBuiltinFunction("sum");
            module.ModuleDict["min"] = new PyBuiltinFunction("min");
            module.ModuleDict["max"] = new PyBuiltinFunction("max");
            module.ModuleDict["any"] = new PyBuiltinFunction("any");
            module.ModuleDict["all"] = new PyBuiltinFunction("all");
            module.ModuleDict["delattr"] = new PyBuiltinFunction("delattr");
            module.ModuleDict["super"] = BuiltinCallableType(PyType.SuperType, "super");
            PySuper.Install();
            module.ModuleDict["round"] = new PyBuiltinFunction("round");
            module.ModuleDict["pow"] = new PyBuiltinFunction("pow");
            module.ModuleDict["divmod"] = new PyBuiltinFunction("divmod");
            module.ModuleDict["ord"] = new PyBuiltinFunction("ord");
            module.ModuleDict["chr"] = new PyBuiltinFunction("chr");
            module.ModuleDict["open"] = IoModule.OpenFunction;
            module.ModuleDict["globals"] = new PyBuiltinFunction("globals");
            module.ModuleDict["locals"] = new PyBuiltinFunction("locals");

            // Python 3.12 number conversion builtins
            module.ModuleDict["bin"] = new PyBuiltinFunction("bin", Bin);
            module.ModuleDict["hex"] = new PyBuiltinFunction("hex", Hex);
            module.ModuleDict["oct"] = new PyBuiltinFunction("oct", Oct);
            module.ModuleDict["ascii"] = new PyBuiltinFunction("ascii", Ascii);
            module.ModuleDict["vars"] = new PyBuiltinFunction("vars", Vars);
            module.ModuleDict["format"] = new PyBuiltinFunction("format", Format);
            module.ModuleDict["memoryview"] = BuiltinCallableType(PyType.MemoryViewType, new PyBuiltinFunction("memoryview", MemoryView));
            module.ModuleDict["help"] = new PyBuiltinFunction("help");
            module.ModuleDict["breakpoint"] = new PyBuiltinFunction("breakpoint");

            // CPython 3.12: __build_class__ is a builtin function for class creation
            module.ModuleDict["__build_class__"] = new PyBuiltinFunction("__build_class__");

            // Python 3.12 special attributes
            module.ModuleDict["__name__"] = new PyString("builtins");
            module.ModuleDict["__doc__"] = new PyString("Built-in functions, exceptions, and other objects.");

            PyDocs.SetTypeDocs(module.ModuleDict.Values);
            return module;
        }

        // Built-in function implementations
        private static PyObject Abs(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"abs() takes exactly one argument ({args.Length} given)");

            // This was the implementation actually wired to builtins.abs, and it duplicated
            // core/PyBuiltin's: exact CLR type tests plus a hand-coded PyClassInstance case, so a
            // float or int subclass - which is neither - raised even though float.__abs__ works.
            // One implementation now lives in PyBuiltin.Absolute.
            return PyBuiltinFunction.Absolute(args[0]);
        }

        private static PyObject Len(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"len() takes exactly one argument ({args.Length} given)");

            return new PyInt(args[0].SequenceLength());
        }

        /// <summary>
        /// CPython 3.12: Objects/abstract.c:2666-2671 (PyObject_IsInstance)
        /// Implements isinstance(obj, classInfo) builtin function.
        /// </summary>
        /// <summary>range, zip, map and the like are classes: isinstance and issubclass take them. Calling one still
        /// goes to the builtin implementation.</summary>
        private static PyType BuiltinCallableType(PyType type, string name) =>
            BuiltinCallableType(type, new PyBuiltinFunction(name));

        /// <summary>keywordsForInit: the keywords are left to a subclass's own __init__, the new
        /// function refuses them only for the type itself or a subclass with object's __init__.</summary>
        private static PyType BuiltinCallableType(PyType type, PyBuiltinFunction implementation, bool keywordsForInit = false)
        {
            type.ConstructedByNewSlot = true;
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) =>
            {
                if (keywordsForInit && !ReferenceEquals(cls, type) && !PyType.TypeInitIsObjectInit((PyType)cls))
                    kwargs = null;
                var created = implementation.Call(args, kwargs);
                // A Python subclass gets the same object under its class.
                if (!ReferenceEquals(cls, type))
                    created.SetCustomType((PyType)cls);
                return created;
            }, 0, int.MaxValue, acceptsKwargs: true);
            return type;
        }

        internal static PyObject IsInstance(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"isinstance() takes exactly 2 arguments ({args.Length} given)");
            return PyBool.FromBool(ObjectRecursiveIsInstance(args[0], args[1]));
        }

        internal static PyObject IsSubclass(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"issubclass() takes exactly 2 arguments ({args.Length} given)");
            return PyBool.FromBool(ObjectIsSubclass(args[0], args[1]));
        }

        // _PyObject_RealIsInstance and _PyObject_RealIsSubclass: type.__instancecheck__ and
        // type.__subclasscheck__, which consult no hook.
        internal static PyObject RealIsInstance(PyObject instance, PyObject cls) => PyBool.FromBool(ObjectIsInstance(instance, cls));

        internal static PyObject RealIsSubclass(PyObject derived, PyObject cls) => PyBool.FromBool(RecursiveIsSubclass(derived, cls));

        // PyType_CheckExact
        private static bool IsExactType(PyObject cls) => cls is PyType && ReferenceEquals(cls.GetPyType(), PyType.TypeType);

        // _PyUnion_Check, then _Py_union_args
        private static PyObject UnionArgs(PyObject cls) => cls is SharpPy.PyUnionType union ? new PyTuple(union.Args) : cls;

        /// <summary>object_recursive_isinstance (Objects/abstract.c of CPython v3.12.15).</summary>
        private static bool ObjectRecursiveIsInstance(PyObject inst, PyObject cls)
        {
            // Quick test for an exact match
            if (ReferenceEquals(inst.GetPyType(), cls))
                return true;
            // We know what type's __instancecheck__ does.
            if (IsExactType(cls))
                return ObjectIsInstance(inst, cls);
            cls = UnionArgs(cls);
            if (cls is PyTuple tuple)
            {
                // Not a general sequence -- that opens up the road to recursion and stack overflow.
                PyCRecursion.Enter(" in __instancecheck__");
                try
                {
                    foreach (var item in tuple.Items)
                        if (ObjectRecursiveIsInstance(inst, item))
                            return true;
                    return false;
                }
                finally
                {
                    PyCRecursion.Leave();
                }
            }
            // _PyObject_LookupSpecial: on the type of cls, bound to cls.
            var checker = cls.GetPyType().LookupSpecial("__instancecheck__");
            if (checker != null)
            {
                PyCRecursion.Enter(" in __instancecheck__");
                try
                {
                    return cls.CallSpecial(checker, inst).IsTrue();
                }
                finally
                {
                    PyCRecursion.Leave();
                }
            }
            // cls has no __instancecheck__() method
            return ObjectIsInstance(inst, cls);
        }

        /// <summary>object_isinstance: the type of inst, then inst.__class__; or the __bases__ protocol.</summary>
        private static bool ObjectIsInstance(PyObject inst, PyObject cls)
        {
            if (cls is PyType type)
            {
                var instType = inst.GetPyType();
                if (ReferenceEquals(instType, type) || instType.IsSubclassOf(type))
                    return true;
                var icls = inst.LookupAttribute("__class__");
                return icls != null && !ReferenceEquals(icls, instType) && icls is PyType iclsType && iclsType.IsSubclassOf(type);
            }
            CheckClass(cls, "isinstance() arg 2 must be a type, a tuple of types, or a union");
            var instClass = inst.LookupAttribute("__class__");
            return instClass != null && AbstractIsSubclass(instClass, cls);
        }

        /// <summary>abstract_issubclass: derived is cls or reaches it through __bases__.</summary>
        private static bool AbstractIsSubclass(PyObject derived, PyObject cls)
        {
            PyTuple bases;
            while (true)
            {
                if (ReferenceEquals(derived, cls))
                    return true;
                bases = AbstractGetBases(derived);
                if (bases == null || bases.Items.Length == 0)
                    return false;
                // Avoid recursivity in the single inheritance case
                if (bases.Items.Length == 1)
                {
                    derived = bases.Items[0];
                    continue;
                }
                break;
            }
            PyCRecursion.Enter(" in __issubclass__");
            try
            {
                foreach (var item in bases.Items)
                    if (AbstractIsSubclass(item, cls))
                        return true;
                return false;
            }
            finally
            {
                PyCRecursion.Leave();
            }
        }

        /// <summary>abstract_get_bases: __bases__ when it is a tuple; only AttributeError is suppressed.</summary>
        private static PyTuple AbstractGetBases(PyObject cls) => cls.LookupAttribute("__bases__") as PyTuple;

        /// <summary>check_class: a class is what has a tuple of __bases__.</summary>
        private static void CheckClass(PyObject cls, string error)
        {
            if (AbstractGetBases(cls) == null)
                throw PyTypeError.Create(error);
        }

        /// <summary>recursive_issubclass</summary>
        private static bool RecursiveIsSubclass(PyObject derived, PyObject cls)
        {
            // Fast path (non-recursive)
            if (cls is PyType target && derived is PyType type)
                return ReferenceEquals(type, target) || type.IsSubclassOf(target);
            CheckClass(derived, "issubclass() arg 1 must be a class");
            if (cls is not SharpPy.PyUnionType)
                CheckClass(cls, "issubclass() arg 2 must be a class, a tuple of classes, or a union");
            return AbstractIsSubclass(derived, cls);
        }

        /// <summary>object_issubclass</summary>
        private static bool ObjectIsSubclass(PyObject derived, PyObject cls)
        {
            // We know what type's __subclasscheck__ does.
            if (IsExactType(cls))
                return ReferenceEquals(derived, cls) || RecursiveIsSubclass(derived, cls);
            cls = UnionArgs(cls);
            if (cls is PyTuple tuple)
            {
                PyCRecursion.Enter(" in __subclasscheck__");
                try
                {
                    foreach (var item in tuple.Items)
                        if (ObjectIsSubclass(derived, item))
                            return true;
                    return false;
                }
                finally
                {
                    PyCRecursion.Leave();
                }
            }
            var checker = cls.GetPyType().LookupSpecial("__subclasscheck__");
            if (checker != null)
            {
                PyCRecursion.Enter(" in __subclasscheck__");
                try
                {
                    return cls.CallSpecial(checker, derived).IsTrue();
                }
                finally
                {
                    PyCRecursion.Leave();
                }
            }
            // Probably never reached anymore.
            return RecursiveIsSubclass(derived, cls);
        }

        // The attribute builtins live in PyBuiltinFunction only. See P0-29 in docs/known_gaps.md for
        // the measured argument texts and for which exception the default in getattr suppresses.

        // CPython 3.12: Python/bltinmodule.c:560 - builtin_bin
        private static PyObject Bin(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"bin() takes exactly one argument ({args.Length} given)");

            var obj = args[0];

            // CPython 3.12: Python/bltinmodule.c - integer conversion via __index__
            // BigInteger support for arbitrary precision integers
            // PyNumber_Index: one shared rule (see PyObject.IndexOrThrow). These three
            // builtins each had their own copy, which followed a user __index__ on an int
            // subclass and reported AttributeError-shaped failures for float subclasses.
            var intValue = PyObject.IndexOrThrow(obj);

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
            // PyNumber_Index: one shared rule (see PyObject.IndexOrThrow). These three
            // builtins each had their own copy, which followed a user __index__ on an int
            // subclass and reported AttributeError-shaped failures for float subclasses.
            var intValue = PyObject.IndexOrThrow(obj);

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
            // PyNumber_Index: one shared rule (see PyObject.IndexOrThrow). These three
            // builtins each had their own copy, which followed a user __index__ on an int
            // subclass and reported AttributeError-shaped failures for float subclasses.
            var intValue = PyObject.IndexOrThrow(obj);

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

            // Escape non-ASCII characters. CPython escapes one *code point* at a time
            // (Python/run.c repr with the ASCII flag), so an astral character becomes a single
            // \UXXXXXXXX and not two \uXXXX halves.
            var result = new System.Text.StringBuilder();
            string text = pyStr.Value;
            for (int i = 0; i < text.Length; i += PyString.PairWidth(text, i))
            {
                int c = PyString.CodePointAtUnit(text, i);
                if (c < 128)
                {
                    result.Append((char)c);
                }
                else if (c <= 0xFF)
                {
                    result.Append($"\\x{c:x2}");
                }
                else if (c <= 0xFFFF)
                {
                    result.Append($"\\u{c:x4}");
                }
                else
                {
                    result.Append($"\\U{c:x8}");
                }
            }

            return new PyString(result.ToString());
        }

        // CPython 3.12: Python/bltinmodule.c:3079 builtin_vars.
        // vars() with no argument is exactly locals(). vars(obj) looks __dict__ up as an attribute,
        // so an object without one reports the TypeError below instead of letting an AttributeError
        // escape (int has no __dict__, and GetAttribute raised). The argument-count texts are
        // CPython's handwritten family: "vars expected at most 1 argument, got 2".
        private static PyObject Vars(PyObject[] args)
        {
            if (args.Length > 1)
                throw PyTypeError.Create($"vars expected at most 1 argument, got {args.Length}");

            if (args.Length == 0)
                return PyBuiltinFunction.CallLocals(new PyObject[0]);

            // builtin_vars: an attribute like any other, a property included.
            try
            {
                return args[0].GetAttribute("__dict__");
            }
            catch (PythonException error) when (error.PyException is PyAttributeError)
            {
                throw PyTypeError.Create("vars() argument must have __dict__ attribute");
            }
        }

        // CPython 3.12: Python/bltinmodule.c:689 - builtin_format
        // format(value[, format_spec]) -> string
        // Returns value.__format__(format_spec)
        private static PyObject Format(PyObject[] args)
        {
            // CPython builtin_format: "format expected at least 1 argument, got 0" /
            // "format expected at most 2 arguments, got 3".
            if (args.Length < 1)
                throw PyTypeError.Create($"format expected at least 1 argument, got {args.Length}");
            if (args.Length > 2)
                throw PyTypeError.Create($"format expected at most 2 arguments, got {args.Length}");

            var value = args[0];
            var formatSpec = args.Length == 2 ? args[1] : new PyString("");

            // format_spec must be a string
            if (formatSpec is not PyString formatStr)
            {
                throw PyTypeError.Create($"format() argument 2 must be str, not {formatSpec.GetTypeName()}");
            }

            return value.Format(formatStr);
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
