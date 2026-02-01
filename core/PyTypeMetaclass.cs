using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// CPython 3.12 compatible implementation of the 'type' metaclass
    /// This is the base metaclass for all Python classes
    /// 
    /// Key characteristics:
    /// - type.__class__ == type (type is its own metaclass)
    /// - type.__bases__ == (object,)
    /// - type.__init__ exists and is callable from subclasses
    /// - type.__new__ creates new classes
    /// - type.__call__ instantiates classes
    /// </summary>
    public class PyTypeMetaclass : PyClass
    {
        private static PyTypeMetaclass _instance;
        private static PyBuiltinMethod _typeNewMethod;  // CPython 3.12: Store reference to type.__new__

        // CPython 3.12: Track if we're already inside CreateNewClass to prevent infinite recursion
        [ThreadStatic]
        private static int _createNewClassDepth = 0;

        /// <summary>
        /// The global 'type' object - equivalent to CPython's type
        /// </summary>
        public static PyTypeMetaclass Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = CreateTypeMetaclass();
                }
                return _instance;
            }
        }

        /// <summary>
        /// CPython 3.12: Get the builtin type.__new__ method for comparison
        /// </summary>
        private static PyBuiltinMethod TypeNewMethod
        {
            get
            {
                if (_typeNewMethod == null && _instance != null)
                {
                    // Get type.__new__ from the type metaclass
                    var newAttr = _instance.ClassDict.GetValueOrDefault("__new__");
                    _typeNewMethod = newAttr as PyBuiltinMethod;
                }
                return _typeNewMethod;
            }
        }

        private PyTypeMetaclass(string name, PyType[] baseTypes, Dictionary<string, PyObject> classDict)
            : base(name, baseTypes, classDict)
        {
            // type is its own metaclass
            this.Metaclass = this;
        }

        // CPython 3.12: Objects/typeobject.c:7163-7182 (type_ready_fill_dict)
        // Override InitializeDescriptors to call InitializeTypeTypeDescriptors
        // This is called from PyType constructor, equivalent to type_add_getset
        protected override void InitializeDescriptors()
        {
            // CPython 3.12: Objects/typeobject.c:6701-6722 (type_add_getset)
            // For PyType_Type, tp_getset = type_getsets (Objects/typeobject.c:1577-1590)
            // SharpPy equivalent: Add type descriptors (__dict__, __bases__, etc.) to TypeDict
            InitializeTypeTypeDescriptors();

            // Also call base implementation for PyClass-specific descriptors
            base.InitializeDescriptors();
        }

        // CPython 3.12: Objects/typeobject.c:5339 - PyType_Type.tp_getattro = _Py_type_getattro
        // Override GetAttribute to use type_getattro logic (not PyClass custom logic)
        // This ensures type object attributes invoke descriptors correctly
        public override PyObject GetAttribute(string name)
        {
            // CPython 3.12: Objects/typeobject.c:4800-4880 (type_getattro_impl)
            // For PyType_Type, metatype = &PyType_Type (type is its own metaclass)
            // Search in metatype's MRO for descriptors and invoke them with type as instance

            var metatype = GetPyType(); // Returns this (type is its own metaclass)

            // Search in MRO for descriptor
            foreach (var mroType in MRO)
            {
                if (mroType is PyType pyType && pyType.TypeDict != null)
                {
                    if (pyType.TypeDict.TryGetValue(name, out var attr))
                    {
                        // Found attribute - check if it's a descriptor
                        if (attr is IDescriptor descriptor)
                        {
                            // CPython 3.12: Objects/typeobject.c:4835-4838
                            // meta_get(meta_attribute, (PyObject *)type, (PyObject *)metatype)
                            // First arg is the type object itself (not NULL!)
                            return descriptor.Get(this, metatype);
                        }
                        return attr;
                    }
                }
            }

            // Not found in MRO, fall back to base implementation
            return base.GetAttribute(name);
        }

        /// <summary>
        /// Creates the global type metaclass instance
        /// </summary>
        private static PyTypeMetaclass CreateTypeMetaclass()
        {
            #if DEBUG_LOG
            Console.WriteLine("🏗️ Creating global 'type' metaclass");
            #endif
            
            var classDict = new Dictionary<string, PyObject>();

            // type.__init__(cls, name, bases, namespace) 
            classDict["__init__"] = new PyBuiltinMethod("__init__", (self, args) => 
            {
                #if DEBUG_LOG
                Console.WriteLine($"🔧 type.__init__ called with {args.Length} args");
                #endif
                
                // type.__init__ doesn't need to do much - just exist for super() calls
                // The real work is done in type.__new__
                return PyNone.Instance;
            }, 4); // self + 3 args

            // type.__prepare__(metacls, name, bases, **kwargs)
            // CPython 3.12: Returns an empty dict by default, can be overridden in subclasses
            // CPython reference: Python/bltinmodule.c:186 - passes mkw to __prepare__
            classDict["__prepare__"] = new PyStaticBuiltinMethod("__prepare__", (args, kwargs) =>
            {
                #if DEBUG_LOG
                Console.WriteLine($"🔧 type.__prepare__ called with {args.Length} args");
                if (kwargs != null && kwargs.InternalDict.Count > 0)
                {
                    Console.WriteLine($"   kwargs: {string.Join(", ", kwargs.InternalDict.Keys.Select(k => (k as PyString)?.Value))}");
                }
                #endif

                if (args.Length >= 2)
                {
                    // args[0] = metaclass (cls)
                    // args[1] = name
                    // args[2] = bases (optional)
                    // kwargs may contain additional parameters for custom __prepare__ methods
                    #if DEBUG_LOG
                    Console.WriteLine($"   → Returning empty dict for class namespace");
                    #endif
                    return new PyDict();
                }
                else
                {
                    throw PyTypeError.Create($"type.__prepare__() takes at least 2 arguments, got {args.Length}");
                }
            });

            // type.__new__(cls, name, bases, namespace, **kwargs)
            // CPython 3.12: tp_new behaves like staticmethod - cls is explicit first argument
            // CPython reference: Objects/typeobject.c:3152-3525 (type_new)
            classDict["__new__"] = new PyStaticBuiltinMethod("__new__", (args, kwargs) =>
            {
                #if DEBUG_LOG
                Console.WriteLine($"🔧 type.__new__ (staticmethod) called with {args.Length} args");
                if (kwargs != null && kwargs.InternalDict.Count > 0)
                {
                    Console.WriteLine($"   kwargs: {string.Join(", ", kwargs.InternalDict.Keys.Select(k => (k as PyString)?.Value))}");
                }
                #endif

                if (args.Length == 4)
                {
                    // CPython 3.12: type.__new__(cls, name, bases, namespace, **kwargs)
                    // args[0] = metaclass (cls)
                    // args[1] = name
                    // args[2] = bases
                    // args[3] = namespace
                    #if DEBUG_LOG
                    Console.WriteLine($"   → CreateNewClass with metaclass={args[0]}");
                    #endif
                    // CRITICAL: Pass skipMetaclassCheck=true to prevent infinite recursion
                    // When type.__new__ is called (e.g. from ABCMeta.__new__ via super().__new__()),
                    // we should NOT check for custom metaclass __new__ methods again.
                    // CPython avoids this because type_new is a C function that doesn't go through metaclass lookup.
                    return CreateNewClass(args, skipMetaclassCheck: true, kwargs: kwargs);
                }
                else
                {
                    throw PyTypeError.Create($"type.__new__() takes exactly 4 arguments (metaclass, name, bases, dict), got {args.Length}");
                }
            });

            // type.__call__(cls, *args, **kwargs) - class instantiation
            classDict["__call__"] = new PyBuiltinMethod("__call__", (self, args) => 
            {
                #if DEBUG_LOG
                Console.WriteLine($"🔧 type.__call__ called: instantiating {self}");
                #endif
                
                if (self is PyClass pyClass)
                {
                    // This is class instantiation: MyClass() -> instance
                    return pyClass.CreateInstance(args);
                }
                
                throw PyTypeError.Create($"'{self}' object is not callable");
            }, -1); // variable args

            // type.__str__()
            classDict["__str__"] = new PyBuiltinMethod("__str__", (self, args) => 
            {
                if (self is PyClass pyClass)
                {
                    return new PyString($"<class '{pyClass.Name}'>");
                }
                return new PyString($"<type '{self}'>"); 
            }, 1);

            // type.__repr__() - same as __str__ for type
            classDict["__repr__"] = classDict["__str__"];

            // Add other essential type methods...
            classDict["mro"] = new PyBuiltinMethod("mro", (self, args) =>
            {
                if (self is PyClass pyClass)
                {
                    // Performance: Eliminated LINQ - manual conversion instead of Cast + ToList
                    var mroList = new List<PyObject>(pyClass.MRO.Count);
                    for (int i = 0; i < pyClass.MRO.Count; i++)
                    {
                        mroList.Add(pyClass.MRO[i]);
                    }
                    return new PyList(mroList);
                }
                return new PyList();
            }, 1);

            // CPython 3.12: Add all getset_descriptors (matches CPython's type_getsets[])
            // These descriptors provide attribute access for type objects
            // Equivalent to CPython's getsetdef array in Objects/typeobject.c:1578-1591
            classDict["__dict__"] = new PyDictDescriptor();
            classDict["__name__"] = new PyNameDescriptor();
            classDict["__module__"] = new PyModuleDescriptor();
            classDict["__bases__"] = new PyBasesDescriptor();
            classDict["__mro__"] = new PyMroDescriptor();
            classDict["__doc__"] = new PyDocDescriptor();
            classDict["__qualname__"] = new PyQualnameDescriptor();

            // CPython 3.12: Objects/typeobject.c:4482
            // {"__class_getitem__", Py_GenericAlias, METH_O|METH_CLASS, PyDoc_STR("See PEP 585")},
            classDict["__class_getitem__"] = new PyBuiltinClassMethod("__class_getitem__",
                (cls, arg) => new PyGenericAlias(cls as PyType ?? throw PyTypeError.Create("Expected type"), arg)
            );

            // type inherits from object
            var baseTypes = new PyType[] { PyType.ObjectType };

            // CPython 3.12: Objects/typeobject.c:5322-5367 (PyType_Type static struct)
            // Line 5323: PyVarObject_HEAD_INIT(&PyType_Type, 0) - type is its own metaclass
            // Line 5355: .tp_getset = type_getsets (contains __dict__, __bases__, etc.)
            //
            // SharpPy: PyTypeMetaclass constructor calls InitializeDescriptors() which:
            // 1. Calls InitializeTypeTypeDescriptors() to add type_getsets to TypeDict
            // 2. Calls base.InitializeDescriptors() for PyClass-specific descriptors
            var typeClass = new PyTypeMetaclass("type", baseTypes, classDict);

            // Add type.__format__ - CPython 3.12 (defaults to __str__)
            // This descriptor is for type objects themselves
            // Must be added AFTER typeClass is created so we can set OwnerType
            typeClass.ClassDict["__format__"] = new PyMethodDescriptor(
                "__format__",
                typeClass, // OwnerType is the type metaclass itself
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"__format__() takes exactly 1 argument ({args.Length} given)");
                    if (self is not PyType type)
                        throw PyTypeError.Create("descriptor '__format__' for 'type' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    // format_spec is args[0], but for type objects we just return str()
                    return new PyString($"<class '{type.Name}'>");
                },
                minArgs: 1,
                maxArgs: 1
            );

            // Add type.__reduce_ex__ - CPython 3.12 (pickle support)
            typeClass.ClassDict["__reduce_ex__"] = new PyMethodDescriptor(
                "__reduce_ex__",
                typeClass,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"__reduce_ex__() takes exactly 1 argument ({args.Length} given)");
                    if (self is not PyType type)
                        throw PyTypeError.Create("descriptor '__reduce_ex__' for 'type' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    // Return (type, (type.__name__,))
                    return new PyTuple(new PyObject[] {
                        typeClass,
                        new PyTuple(new PyObject[] { new PyString(type.Name) })
                    });
                },
                minArgs: 1,
                maxArgs: 1
            );

            #if DEBUG_LOG
            Console.WriteLine("✅ Global 'type' metaclass created successfully");
            #endif
            return typeClass;
        }

        /// <summary>
        /// CPython 3.12: Public wrapper for CalculateMetaclass
        /// Called from __build_class__ to determine winner metaclass
        /// </summary>
        public static PyClass CallCalculateMetaclass(PyObject metatype, PyType[] bases)
        {
            return CalculateMetaclass(metatype as PyClass ?? Instance, bases);
        }

        /// <summary>
        /// CPython 3.12: Calculate the appropriate metaclass
        /// Equivalent to CPython's _PyType_CalculateMetaclass
        /// </summary>
        private static PyClass CalculateMetaclass(PyClass metatype, PyType[] bases)
        {
            // Start with the provided metatype
            PyClass winner = metatype;

            // Check all bases to find the most derived metaclass
            foreach (var baseType in bases)
            {
                if (baseType is PyClass baseClass)
                {
                    PyClass baseMeta = baseClass.Metaclass as PyClass ?? Instance;

                    // If baseMeta is more derived than winner, use it
                    if (IsSubclass(baseMeta, winner))
                    {
                        winner = baseMeta;
                    }
                    else if (!IsSubclass(winner, baseMeta))
                    {
                        // Metaclass conflict
                        throw PyTypeError.Create($"metaclass conflict: the metaclass of a derived class must be a (non-strict) subclass of the metaclasses of all its bases");
                    }
                }
            }

            return winner;
        }

        /// <summary>
        /// Check if derived is a subclass of base
        /// </summary>
        private static bool IsSubclass(PyClass derived, PyClass baseClass)
        {
            if (derived == baseClass)
                return true;

            foreach (var mroType in derived.MRO)
            {
                if (mroType == baseClass)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// CPython 3.12: Check if a __new__ method is type.__new__ (inherited, not overridden)
        /// Equivalent to CPython's "winner->tp_new != type_new" check
        /// </summary>
        private static bool IsTypeNew(PyObject newMethod)
        {
            // CPython 3.12: Check for PyStaticBuiltinMethod (tp_new slot)
            if (newMethod is PyStaticBuiltinMethod staticBuiltin)
            {
                // Check if it's the same reference as type.__new__
                var typeNew = _instance?.ClassDict.GetValueOrDefault("__new__");
                if (typeNew != null && ReferenceEquals(staticBuiltin, typeNew))
                {
                    return true;
                }

                // Also check by name
                if (staticBuiltin.Name == "__new__")
                {
                    return true;
                }
            }

            // Legacy: If it's a PyBuiltinMethod, also check (for compatibility)
            if (newMethod is PyBuiltinMethod builtinMethod)
            {
                if (TypeNewMethod != null && ReferenceEquals(builtinMethod, TypeNewMethod))
                {
                    return true;
                }

                if (builtinMethod.Name == "__new__")
                {
                    return TypeNewMethod != null && builtinMethod.Method == TypeNewMethod.Method;
                }
            }

            // If it's a PyMethod (bound method), check the underlying function
            if (newMethod is PyMethod pyMethod)
            {
                if (pyMethod.Function is PyBuiltinMethod || pyMethod.Function is PyStaticBuiltinMethod)
                {
                    return IsTypeNew(pyMethod.Function);
                }
            }

            // Otherwise, it's a custom __new__ (overridden)
            return false;
        }

        /// <summary>
        /// Implementation of type.__new__ for creating new classes
        /// </summary>
        /// <param name="args">Array of [cls, name, bases, namespace]</param>
        /// <param name="skipMetaclassCheck">CPython 3.12: Skip custom metaclass check (when called from super().__new__)</param>
        /// <param name="kwargs">CPython 3.12: Keyword arguments to pass to metaclass.__new__</param>
        private static PyObject CreateNewClass(PyObject[] args, bool skipMetaclassCheck = false, PyDict kwargs = null)
        {
            var cls = args[0];        // metaclass (should be type or subclass)
            var name = args[1];       // class name
            var bases = args[2];      // base classes tuple
            var namespaceDict = args[3];  // class namespace dict

            #if DEBUG_LOG
            if (kwargs != null && kwargs.InternalDict.Count > 0)
            {
                Console.WriteLine($"🔍 CreateNewClass called with kwargs: {string.Join(", ", kwargs.InternalDict.Keys.Select(k => (k as PyString)?.Value))}");
            }
            #endif

            #if DEBUG_LOG
            Console.WriteLine($"🏗️ type.__new__ creating class: {(name is PyString pyStr ? pyStr.Value : name.ToString())}");
            Console.WriteLine($"   cls (metaclass): {cls.GetType().Name} / {cls}");
            Console.WriteLine($"   bases: {bases}");
            Console.WriteLine($"   namespaceDict: {namespaceDict.GetType().Name} / {namespaceDict.GetTypeName()}");
            Console.WriteLine($"   skipMetaclassCheck: {skipMetaclassCheck}");
            #endif

            // Convert arguments
            if (!(name is PyString nameStr))
                throw PyTypeError.Create("type.__new__() name must be string");
                
            if (!(bases is PyTuple basesTuple))
                throw PyTypeError.Create("type.__new__() bases must be tuple");

            // CPython 3.12: namespace can be dict or dict subclass (e.g., _EnumDict)
            // Accept both PyDict and PyClassInstance (for dict subclasses like _EnumDict)
            PyList dictItems;
            if (namespaceDict is PyDict pyDict)
            {
                dictItems = pyDict.Items();
            }
            else if (namespaceDict is PyClassInstance classInstance)
            {
                // This is a dict subclass like _EnumDict - call items() method
                try
                {
                    var itemsMethod = classInstance.GetAttribute("items");
                    if (itemsMethod != null && itemsMethod.IsCallable())
                    {
                        var itemsResult = itemsMethod.Call(new PyObject[0], null);
                        if (itemsResult is PyList)
                        {
                            dictItems = (PyList)itemsResult;
                        }
                        else
                        {
                            throw PyTypeError.Create("type.__new__() namespace.items() must return list");
                        }
                    }
                    else
                    {
                        throw PyTypeError.Create("type.__new__() namespace must be dict or dict-like with items() method");
                    }
                }
                catch (PythonException)
                {
                    throw PyTypeError.Create("type.__new__() namespace must be dict or dict-like");
                }
            }
            else
            {
                throw PyTypeError.Create("type.__new__() namespace must be dict");
            }

            // Convert bases tuple to PyType array
            // Performance: Eliminated LINQ - manual cast instead of Cast + ToArray
            var baseTypes = new PyType[basesTuple.Items.Length];
            for (int i = 0; i < basesTuple.Items.Length; i++)
            {
                baseTypes[i] = (PyType)basesTuple.Items[i];
            }

            // CPython 3.12: If bases is empty, add object as default base
            // This is done in type.__new__, not in __build_class__
            if (baseTypes.Length == 0)
            {
                baseTypes = new PyType[] { PyType.ObjectType };
                #if DEBUG_LOG
                Console.WriteLine($"   No bases provided, added object as default base");
                #endif
            }

            // CPython 3.12: Calculate the winner metaclass
            // This is equivalent to CPython's _PyType_CalculateMetaclass
            // Objects/typeobject.c:3857-3859
            PyClass winner = CalculateMetaclass(cls as PyClass ?? Instance, baseTypes);

            // CPython 3.12: Check if we have a custom metaclass before converting namespace
            // If we have a custom metaclass, we should pass the namespace as-is to its __new__
            // IMPORTANT: Only detect custom metaclass on the FIRST call, not on recursive calls (skipMetaclassCheck=true)
            // When metaclass.__new__ calls super().__new__, we're in a recursive call and should NOT treat it as custom
            PyClass customMetaclass = null;
            if (!skipMetaclassCheck && winner != null && winner != Instance)
            {
                customMetaclass = winner;
                #if DEBUG_LOG
                Console.WriteLine($"🔧 Custom metaclass detected early: {customMetaclass.Name}");
                Console.WriteLine($"   Will pass namespace dict as-is to metaclass.__new__");
                #endif
            }
            else if (skipMetaclassCheck)
            {
                #if DEBUG_LOG
                Console.WriteLine($"🔧 Recursive call (skipMetaclassCheck=true): will create classDict from namespace");
                #endif
            }

            // Convert namespace dict to class dict (only if no custom metaclass)
            Dictionary<string, PyObject> classDict = null;
            if (customMetaclass == null)
            {
                classDict = new Dictionary<string, PyObject>();

                #if DEBUG_LOG
                Console.WriteLine($"🔍 type.__new__: namespace dict has {dictItems.Items.Length} items");
                // Performance: Eliminated LINQ - manual loop with limit instead of Take
                int logLimit = System.Math.Min(20, dictItems.Items.Length);
                for (int i = 0; i < logLimit; i++)
                {
                    var item = dictItems.Items[i];
                    if (item is PyTuple tuple && tuple.Items.Length == 2 && tuple.Items[0] is PyString keyStr)
                    {
                        Console.WriteLine($"   - {keyStr.Value}: {tuple.Items[1].GetTypeName()}");
                    }
                }
                #endif

                foreach (var item in dictItems.Items)
                {
                    if (item is PyTuple tuple && tuple.Items.Length == 2)
                    {
                        if (tuple.Items[0] is PyString keyStr)
                        {
                            // CPython 3.12: Objects/typeobject.c:3751 - values preserved as-is
                            classDict[keyStr.Value] = tuple.Items[1];
                        }
                    }
                }
            }

            // CPython 3.12: Check if we need to call custom metaclass.__new__
            // This follows CPython's type_new_get_bases logic (typeobject.c:3864-3875)
            // Key: if (winner->tp_new != type_new) - check if metaclass has custom __new__
            // This applies whether winner == metatype OR winner != metatype
            PyClass newClass;
            PyClass metatype = cls as PyClass ?? Instance;

            #if DEBUG_LOG
            Console.WriteLine($"🔧 Metaclass check: winner={winner?.Name}, metatype={metatype.Name}");
            Console.WriteLine($"   winner != metatype: {winner != metatype}");
            Console.WriteLine($"   skipMetaclassCheck: {skipMetaclassCheck}");
            #endif

            // CPython 3.12: Special handling for metaclass creation (subclass of type)
            // If we're creating a metaclass (base class is type or subclass of type),
            // we must NOT call the __new__ from the namespace being created,
            // because that would cause infinite recursion (ABCMeta.__new__ calling super().__new__ → CreateNewClass → ABCMeta.__new__ → ...)
            bool creatingMetaclass = false;
            #if DEBUG_MODULE_LOG
            Console.WriteLine($"🔍 CreateNewClass: {nameStr.Value}, winner={winner?.GetPyType()?.Name ?? "null"}, bases={string.Join(", ", baseTypes.Select(b => b.GetPyType()?.Name ?? b.ToString()))}");
            #endif

            foreach (var baseType in baseTypes)
            {
                #if DEBUG_MODULE_LOG
                Console.WriteLine($"  🔍 Checking base: {baseType.GetPyType()?.Name ?? baseType.ToString()}, Instance={baseType == Instance}, isMetaclass={baseType is PyTypeMetaclass}");
                #endif

                if (baseType == Instance || baseType is PyTypeMetaclass)
                {
                    creatingMetaclass = true;
                    #if DEBUG_MODULE_LOG
                    Console.WriteLine($"  ✅ Detected metaclass creation (base is type or PyTypeMetaclass)");
                    #endif
                    break;
                }
                // Check if baseType is a subclass of type by checking its MRO
                if (baseType is PyClass baseClass)
                {
                    #if DEBUG_MODULE_LOG
                    Console.WriteLine($"  🔍 Checking MRO for {baseClass.Name}...");
                    #endif
                    foreach (var mroType in baseClass.MRO)
                    {
                        #if DEBUG_MODULE_LOG
                        Console.WriteLine($"    - MRO entry: {mroType.GetPyType()?.Name ?? mroType.ToString()}");
                        #endif
                        if (mroType == Instance || mroType is PyTypeMetaclass)
                        {
                            creatingMetaclass = true;
                            #if DEBUG_MODULE_LOG
                            Console.WriteLine($"    ✅ Found type in MRO - this is a metaclass");
                            #endif
                            break;
                        }
                    }
                    if (creatingMetaclass) break;
                }
            }

            if (creatingMetaclass)
            {
                #if DEBUG_MODULE_LOG
                Console.WriteLine($"🔧 Creating metaclass '{nameStr.Value}' (subclass of type) - skipping custom __new__ check to prevent recursion");
                #endif
            }
            else
            {
                #if DEBUG_MODULE_LOG
                Console.WriteLine($"📦 Creating regular class '{nameStr.Value}'");
                #endif
            }

            // CPython: Check if the winner metaclass (or metatype if winner==metatype) has custom __new__
            // We need to call it even when winner == metatype if it's overridden!
            // BUT: Skip this check if we're creating a metaclass (would cause infinite recursion)
            bool shouldCallCustomNew = false;
            PyObject customNewMethod = null;

            #if DEBUG_MODULE_LOG
            Console.WriteLine($"🔍 Should check for custom __new__? skipCheck={skipMetaclassCheck}, creatingMetaclass={creatingMetaclass}, winner={winner?.GetPyType()?.Name ?? "null"}");
            #endif

            if (!skipMetaclassCheck && !creatingMetaclass && winner != null && winner != Instance)
            {
                // Try to get __new__ from the winner metaclass
                #if DEBUG_MODULE_LOG
                Console.WriteLine($"  ✅ Checking for custom __new__ on {winner.GetPyType()?.Name ?? winner.ToString()}");
                #endif
                try
                {
                    var newMethod = winner.GetAttribute("__new__");
                    if (newMethod != null && newMethod.IsCallable())
                    {
                        bool isTypeNew = IsTypeNew(newMethod);
                        #if DEBUG_MODULE_LOG
                        Console.WriteLine($"    Found __new__ method, isTypeNew={isTypeNew}, method={newMethod.GetType().Name}");
                        #endif

                        if (!isTypeNew)
                        {
                            shouldCallCustomNew = true;
                            customNewMethod = newMethod;  // Store the method to reuse it
                            #if DEBUG_MODULE_LOG
                            Console.WriteLine($"    ⚠️  Will call custom __new__");
                            #endif
                        }
                        else
                        {
                            #if DEBUG_MODULE_LOG
                            Console.WriteLine($"    ✅ Using default type.__new__");
                            #endif
                        }
                    }
                }
                catch (PythonException ex)
                {
                    if (ex.PyException.GetTypeName() != "AttributeError")
                    {
                        throw;
                    }
                }
            }
            else
            {
                #if DEBUG_MODULE_LOG
                Console.WriteLine($"  ⛔ Skipping custom __new__ check");
                #endif
            }

            if (shouldCallCustomNew && customNewMethod != null)
            {
                // Custom metaclass with overridden __new__ - call it
                #if DEBUG_LOG
                Console.WriteLine($"🔧 Calling custom metaclass {winner.Name}.__new__");
                Console.WriteLine($"   Method type: {customNewMethod.GetType().Name}");
                Console.WriteLine($"   namespaceDict type: {namespaceDict.GetType().Name} / {namespaceDict.GetTypeName()}");
                if (kwargs != null && kwargs.InternalDict.Count > 0)
                {
                    Console.WriteLine($"   Forwarding kwargs: {string.Join(", ", kwargs.InternalDict.Keys.Select(k => (k as PyString)?.Value))}");
                }
                #endif

                // Call metaclass.__new__(cls, name, bases, namespace, **kwargs)
                // Note: The namespace should be passed as-is (could be _EnumDict)
                // CPython reference: Objects/typeobject.c:1667 - passes kwargs to tp_new
                var result = customNewMethod.Call(new PyObject[] { winner, name, bases, namespaceDict }, kwargs);

                if (result is PyClass resultClass)
                {
                    newClass = resultClass;
                    #if DEBUG_LOG
                    Console.WriteLine($"   ✅ {winner.Name}.__new__ returned: {newClass.Name}");
                    #endif
                }
                else
                {
                    throw PyTypeError.Create($"{winner.Name}.__new__ must return a class, got {result.GetTypeName()}");
                }
            }
            else
            {
                // Default type.__new__ behavior - create class directly
                #if DEBUG_LOG
                Console.WriteLine($"🔧 Creating class directly (no custom metaclass __new__)");
                #endif

                // Convert namespace if needed
                if (classDict == null)
                {
                    classDict = new Dictionary<string, PyObject>();
                    foreach (var item in dictItems.Items)
                    {
                        if (item is PyTuple tuple && tuple.Items.Length == 2)
                        {
                            if (tuple.Items[0] is PyString keyStr)
                            {
                                classDict[keyStr.Value] = tuple.Items[1];
                            }
                        }
                    }
                }

                newClass = new PyClass(nameStr.Value, baseTypes, classDict);
                // CPython 3.12: Use the winner metaclass (could be custom metaclass)
                newClass.Metaclass = winner ?? Instance;


            }

            #if DEBUG_LOG
            Console.WriteLine($"✅ Created class {nameStr.Value} with metaclass {newClass.Metaclass?.Name}");
            #endif

            // PEP 487: Call __set_name__ on all descriptors in the class namespace
            // CPython 3.12: This MUST be called regardless of custom metaclass
            // The custom metaclass creates the class, but type.__new__ still needs to call __set_name__
            #if DEBUG_LOG
            Console.WriteLine($"🔧 PEP 487: Calling __set_name__ on descriptors in {nameStr.Value}");
            Console.WriteLine($"   newClass.ClassDict has {newClass.ClassDict.Count} attributes");
            #endif

            // CPython 3.12: Objects/typeobject.c:9969-10010 (type_new_set_names)
            // Line 9972: Create a copy of the dict to iterate over
            // This prevents "Collection was modified" errors when __set_name__ modifies the dict
            var classDict_copy = new Dictionary<string, PyObject>(newClass.ClassDict);

            foreach (var kvp in classDict_copy)
            {
                string attrName = kvp.Key;
                PyObject attrValue = kvp.Value;

                // Check if the attribute has __set_name__ method
                try
                {
                    var setNameMethod = attrValue.GetAttribute("__set_name__");
                    if (setNameMethod != null && setNameMethod.IsCallable())
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"  Calling __set_name__ on {attrName}: {attrValue.GetType().Name}");
                        #endif
                        // Call __set_name__(owner, name)
                        setNameMethod.Call(new PyObject[] { newClass, new PyString(attrName) }, null);
                        #if DEBUG_LOG
                        Console.WriteLine($"  ✅ __set_name__ completed for {attrName}");
                        #endif
                    }
                }
                catch (PythonException ex)
                {
                    // If it's an AttributeError, the attribute doesn't have __set_name__ - that's fine
                    if (ex.PyException.GetTypeName() == "AttributeError")
                    {
                        continue;
                    }

                    #if DEBUG_LOG
                    Console.WriteLine($"  Error calling __set_name__ on {attrName}: {ex.Message}");
                    #endif
                    // Re-throw other exceptions - __set_name__ errors should propagate
                    throw;
                }
            }

            // PEP 487: Call __init_subclass__ on the parent class(es)
            // CPython 3.12: Objects/typeobject.c:10015 (type_new_init_subclass)
            // This is called via super().__init_subclass__(**kwargs)
            // TODO: Pass keyword arguments from class definition (e.g., class Foo(Base, plugin=True))
            #if DEBUG_LOG
            Console.WriteLine($"🔧 PEP 487: Calling __init_subclass__ for {nameStr.Value}");
            #endif

            try
            {
                // Create super(newClass, newClass) to get parent's __init_subclass__
                var superObj = new PySuper(newClass, newClass);
                var initSubclassMethod = superObj.GetAttribute("__init_subclass__");

                if (initSubclassMethod != null && initSubclassMethod.IsCallable())
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"  Calling __init_subclass__ with cls={nameStr.Value}");
                    #endif

                    // Call __init_subclass__(cls, **kwargs)
                    // CPython: __init_subclass__ is a classmethod, so first arg is the subclass
                    // TODO: Extract kwargs from class definition and pass them here
                    initSubclassMethod.Call(new PyObject[] { newClass }, null);

                    #if DEBUG_LOG
                    Console.WriteLine($"  ✅ __init_subclass__ completed");
                    #endif
                }
            }
            catch (PythonException ex)
            {
                #if DEBUG_LOG
                Console.WriteLine($"  Error calling __init_subclass__: {ex.Message}");
                #endif
                // Re-throw - __init_subclass__ errors should propagate
                throw;
            }

            return newClass;
        }

        /// <summary>
        /// Convert a plain function to classmethod if it exists in the class dict
        /// CPython 3.12: Objects/typeobject.c (type_new_classmethod)
        /// </summary>
        internal static void ConvertToClassmethod(PyClass cls, string methodName)
        {
            if (cls.ClassDict.TryGetValue(methodName, out PyObject value))
            {
                // Only convert if it's a plain function (not already a classmethod)
                if (value is PyFunction func)
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"  Converting {methodName} to classmethod");
                    #endif
                    cls.ClassDict[methodName] = new PyClassmethod(func);
                }
            }
        }

        /// <summary>
        /// Override GetPyType to return self (type is its own type)
        /// </summary>
        public override PyType GetPyType()
        {
            return this; // type.__class__ == type
        }

        /// <summary>
        /// Override Call to handle type() calls properly
        /// </summary>
        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            // CPython 3.12: Objects/typeobject.c:1627-1689 (type_call)
            for (int i = 0; i < args.Length; i++)
            {
                #if DEBUG_LOG
                Console.WriteLine($"   arg[{i}]: {args[i]?.GetType().Name} = {args[i]}");
                #endif
            }

            if (args.Length == 1)
            {
                // type(obj) - return type of object
                return args[0].GetPyType();
            }
            else if (args.Length == 3)
            {
                // type(name, bases, namespace, **kwargs) - create new class
                // The metaclass itself is the first argument in CreateNewClass
                // CPython reference: Objects/typeobject.c:1667 - forwards kwargs to tp_new
                var newArgs = new PyObject[] { this, args[0], args[1], args[2] };
                return CreateNewClass(newArgs, skipMetaclassCheck: false, kwargs: kwargs);
            }
            else if (args.Length == 4)
            {
                // This might be the case where it's called by __build_class__
                // args[0] might be the metaclass, args[1] name, args[2] bases, args[3] namespace
                return CreateNewClass(args, skipMetaclassCheck: false, kwargs: kwargs);
            }
            else
            {
                throw PyTypeError.Create($"type() takes 1, 3 or 4 arguments ({args.Length} given)");
            }
        }

        // CPython 3.12: No need to override GetAttribute anymore
        // All type attributes (__dict__, __name__, __module__, etc.) are now handled
        // by descriptors in ClassDict, just like CPython does via getsetdef array
    }

    /// <summary>
    /// Helper class for built-in methods
    /// </summary>
    /// <summary>
    /// CPython 3.12 호환: Descriptor protocol을 구현한 builtin method
    /// 클래스에서 접근 시 unbound, 인스턴스에서 접근 시 자동으로 bound method 반환
    /// </summary>
    public class PyBuiltinMethod : PyObject, IDescriptor
    {
        public string Name { get; }
        public Func<PyObject, PyObject[], PyObject> Method { get; }
        public int ArgCount { get; } // -1 for variable args

        public PyBuiltinMethod(string name, Func<PyObject, PyObject[], PyObject> method, int argCount = -1)
        {
            Name = name;
            Method = method;
            ArgCount = argCount;
        }

        public override string ToString()
        {
            return $"<built-in method '{Name}'>";
        }

        public override PyType GetPyType()
        {
            return PyType.MethodType; // builtin methods are instances of method type
        }

        // Descriptor protocol: instance.method → bound method
        public PyObject Get(PyObject instance, PyType owner)
        {
            if (instance == null || instance == PyNone.Instance)
            {
                // 클래스에서 접근: Class.method → unbound
                return this;
            }

            // 인스턴스에서 접근: instance.method → bound method
            return new PyBoundBuiltinMethod(instance, this);
        }

        public void Set(PyObject instance, PyObject value)
        {
            throw PyAttributeError.Create($"can't set attribute '{Name}'");
        }

        public void Delete(PyObject instance)
        {
            throw PyAttributeError.Create($"can't delete attribute '{Name}'");
        }

        public bool IsDataDescriptor() => false; // non-data descriptor

        // Unbound call: 첫 인자를 self로 사용
        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            // The first argument is 'self' for unbound methods
            if (args.Length < 1)
            {
                throw PyTypeError.Create($"{Name}() missing required 'self' argument");
            }

            var self = args[0];
            var methodArgs = new PyObject[args.Length - 1];
            Array.Copy(args, 1, methodArgs, 0, methodArgs.Length);

            return Method(self, methodArgs);
        }

        public override bool IsCallable()
        {
            return true;
        }
    }

    /// <summary>
    /// Bound builtin method: self가 이미 바인딩된 메서드
    /// </summary>
    public class PyBoundBuiltinMethod : PyObject
    {
        private PyObject _instance;
        private PyBuiltinMethod _method;

        public PyBoundBuiltinMethod(PyObject instance, PyBuiltinMethod method)
        {
            _instance = instance;
            _method = method;
        }

        public override string ToString()
        {
            return $"<bound method '{_method.Name}' of {_instance}>";
        }

        public override PyType GetPyType()
        {
            return PyType.MethodType;
        }

        // Bound call: self 자동 추가
        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            return _method.Method(_instance, args);
        }

        public override bool IsCallable()
        {
            return true;
        }

        // Bound method attributes
        public override PyObject GetAttribute(string name)
        {
            return name switch
            {
                "__self__" => _instance,
                "__func__" => _method,
                "__name__" => new PyString(_method.Name),
                _ => base.GetAttribute(name)
            };
        }
    }

    /// <summary>
    /// CPython 3.12: Static builtin method (like tp_new)
    /// Behaves like @staticmethod - does NOT bind to instance
    /// </summary>
    public class PyStaticBuiltinMethod : PyObject, IDescriptor
    {
        public string Name { get; }
        public Func<PyObject[], PyDict, PyObject> Method { get; }

        public PyStaticBuiltinMethod(string name, Func<PyObject[], PyDict, PyObject> method)
        {
            Name = name;
            Method = method;
        }

        public override string ToString()
        {
            return $"<built-in method '{Name}'>";
        }

        public override PyType GetPyType()
        {
            return PyType.MethodType;
        }

        // Descriptor protocol: Always return self (staticmethod behavior)
        public PyObject Get(PyObject instance, PyType owner)
        {
            // Staticmethod: return unbound function regardless of instance
            return this;
        }

        public void Set(PyObject instance, PyObject value)
        {
            throw PyAttributeError.Create($"can't set attribute '{Name}'");
        }

        public void Delete(PyObject instance)
        {
            throw PyAttributeError.Create($"can't delete attribute '{Name}'");
        }

        public bool IsDataDescriptor() => false;

        // Direct call: all arguments and kwargs passed as-is
        // CPython 3.12: Objects/typeobject.c - tp_new receives kwargs
        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            return Method(args, kwargs);
        }

        public override bool IsCallable()
        {
            return true;
        }
    }

    /// <summary>
    /// CPython 3.12: getset_descriptor for __dict__ attribute
    /// Equivalent to CPython's type_dict getter in Objects/typeobject.c
    ///
    /// CPython implementation:
    /// static PyObject *
    /// type_dict(PyTypeObject *type, void *context)
    /// {
    ///     PyObject *dict = lookup_tp_dict(type);
    ///     if (dict == NULL) {
    ///         Py_RETURN_NONE;
    ///     }
    ///     return PyDictProxy_New(dict);
    /// }
    ///
    /// getsetdef: {"__dict__",  (getter)type_dict,  NULL, NULL}
    /// - getter: type_dict
    /// - setter: NULL (read-only)
    /// </summary>
    public class PyDictDescriptor : PyObject, IDescriptor
    {
        public PyDictDescriptor()
        {
        }

        public override string ToString()
        {
            return "<attribute '__dict__' of 'type' objects>";
        }

        public override PyType GetPyType()
        {
            // CPython 3.12: getset_descriptor has its own type
            return PyType.ObjectType; // TODO: Create PyType.GetSetDescriptorType
        }

        public override string GetTypeName()
        {
            return "getset_descriptor";
        }

        /// <summary>
        /// CPython 3.12: __get__(self, obj, type=None)
        /// Equivalent to type_dict(PyTypeObject *type, void *context)
        /// </summary>
        public PyObject Get(PyObject instance, PyType owner)
        {
            #if DEBUG_LOG
            Console.WriteLine($"[PyDictDescriptor.__get__] instance={instance?.GetType().Name}, owner={owner?.Name}");
            #endif

            // CPython 3.12: If accessed from class (not instance), return self
            // Example: type.__dict__ (not SomeClass.__dict__)
            if (instance == null || instance == PyNone.Instance)
            {
                #if DEBUG_LOG
                Console.WriteLine($"  → Accessed from class, returning descriptor itself");
                #endif
                return this;
            }

            // CPython 3.12: instance should be a type/class object
            // Check PyClass first (PyClass inherits from PyType)
            if (instance is PyClass pyClass)
            {
                #if DEBUG_LOG
                Console.WriteLine($"  → Returning mappingproxy of {pyClass.Name}.__dict__ (count: {pyClass.ClassDict.Count})");
                #endif
                // CPython 3.12: Each access creates a NEW mappingproxy instance
                // (class.__dict__ is class.__dict__) returns False in CPython
                return new PyMappingProxy(pyClass.ClassDict);
            }
            // CPython 3.12: Handle builtin PyType instances (like dict, str, int, etc.)
            else if (instance is PyType pyType)
            {
                #if DEBUG_LOG
                Console.WriteLine($"  → Returning mappingproxy of {pyType.Name}.__dict__ (builtin type)");
                #endif
                // CPython 3.12: Return TypeDict as mappingproxy
                // All descriptors are now in TypeDict, no need to merge
                return new PyMappingProxy(pyType.TypeDict);
            }

            // CPython 3.12: If dict is NULL, return None
            #if DEBUG_LOG
            Console.WriteLine($"  → instance is not a type/class, returning None");
            #endif
            return PyNone.Instance;
        }

        /// <summary>
        /// CPython 3.12: setter=NULL in getsetdef
        /// Attempting to set __dict__ should raise AttributeError
        /// </summary>
        public void Set(PyObject instance, PyObject value)
        {
            throw PyAttributeError.Create("attribute '__dict__' of 'type' objects is not writable");
        }

        /// <summary>
        /// CPython 3.12: deleter not supported
        /// </summary>
        public void Delete(PyObject instance)
        {
            throw PyAttributeError.Create("can't delete attribute '__dict__'");
        }

        /// <summary>
        /// CPython 3.12: getset_descriptor is a data descriptor (has both __get__ and __set__)
        /// Even though setter raises error, it's still considered a data descriptor
        /// </summary>
        public bool IsDataDescriptor()
        {
            return true; // CPython getset_descriptor is always data descriptor
        }
    }

    /// <summary>
    /// CPython 3.12: getset_descriptor for __name__ attribute
    /// Equivalent to CPython's type_name getter/type_set_name setter
    /// getsetdef: {"__name__", (getter)type_name, (setter)type_set_name, NULL}
    /// </summary>
    public class PyNameDescriptor : PyObject, IDescriptor
    {
        public override string ToString() => "<attribute '__name__' of 'type' objects>";
        public override PyType GetPyType() => PyType.ObjectType;
        public override string GetTypeName() => "getset_descriptor";

        public PyObject Get(PyObject instance, PyType owner)
        {
            // CPython 3.12: When accessing type.__name__ (instance=NULL, owner=type),
            // return type's name, not the descriptor itself
            if (instance == null || instance == PyNone.Instance)
            {
                // If owner is provided, return its name
                if (owner != null)
                {
                    if (owner is PyClass ownerClass)
                    {
                        // CPython 3.12: Return the owner's tp_name directly, don't look in ClassDict
                        // (otherwise we'd return the descriptor itself!)
                        return new PyString(ownerClass.Name);
                    }

                    // Owner is a PyType (including PyTypeMetaclass)
                    return new PyString(owner.Name);
                }

                // Fallback: return descriptor itself for unbound access
                return this;
            }

            // CPython 3.12: Handle both PyClass and PyType
            if (instance is PyClass pyClass)
            {
                // CPython: Check ClassDict first, then fallback to tp_name
                if (pyClass.ClassDict.TryGetValue("__name__", out var name))
                    return name;

                // Fallback to Name property
                return new PyString(pyClass.Name);
            }

            if (instance is PyType pyType)
            {
                // PyType stores name in Name property
                return new PyString(pyType.Name);
            }

            return PyNone.Instance;
        }

        public void Set(PyObject instance, PyObject value)
        {
            if (instance is PyClass pyClass)
            {
                if (!(value is PyString nameStr))
                    throw PyTypeError.Create($"can only assign string to {pyClass.Name}.__name__, not '{value.GetTypeName()}'");

                // CPython: Update tp_name (in SharpPy, store in ClassDict)
                // This is similar to CPython's heap type behavior
                pyClass.ClassDict["__name__"] = value;
            }
            else
            {
                throw PyAttributeError.Create("attribute '__name__' of 'type' objects is not writable");
            }
        }

        public void Delete(PyObject instance)
        {
            throw PyAttributeError.Create("can't delete attribute '__name__'");
        }

        public bool IsDataDescriptor() => true;
    }

    /// <summary>
    /// CPython 3.12: getset_descriptor for __module__ attribute
    /// Equivalent to CPython's type_module getter/type_set_module setter
    /// getsetdef: {"__module__", (getter)type_module, (setter)type_set_module, NULL}
    /// </summary>
    public class PyModuleDescriptor : PyObject, IDescriptor
    {
        public override string ToString() => "<attribute '__module__' of 'type' objects>";
        public override PyType GetPyType() => PyType.ObjectType;
        public override string GetTypeName() => "getset_descriptor";

        public PyObject Get(PyObject instance, PyType owner)
        {
            if (instance == null || instance == PyNone.Instance)
                return this;

            // CPython 3.12: Objects/typeobject.c:1063-1091 (type_module)
            // Check PyClass first (heap type - user-defined classes)
            if (instance is PyClass pyClass)
            {
                // CPython: For heap types, look up __module__ in ClassDict (tp_dict)
                if (pyClass.ClassDict.TryGetValue("__module__", out var module))
                    return module;

                // Default to "builtins" if not found
                return new PyString("builtins");
            }

            // CPython 3.12: Objects/typeobject.c:1078-1088
            // For built-in types (non-heap types), check tp_name for '.'
            // If found, return module name (part before '.'); otherwise return "builtins"
            if (instance is PyType pyType)
            {
                // CPython: const char *s = strrchr(type->tp_name, '.');
                var dotIndex = pyType.Name.LastIndexOf('.');
                if (dotIndex >= 0)
                {
                    // Return module name (part before '.')
                    return new PyString(pyType.Name.Substring(0, dotIndex));
                }

                // Default: "builtins" for built-in types
                // CPython: mod = Py_NewRef(&_Py_ID(builtins));
                return new PyString("builtins");
            }

            return PyNone.Instance;
        }

        public void Set(PyObject instance, PyObject value)
        {
            if (instance is PyClass pyClass)
            {
                // CPython: PyDict_SetItem(dict, &_Py_ID(__module__), value)
                pyClass.ClassDict["__module__"] = value;
            }
            else
            {
                throw PyAttributeError.Create("attribute '__module__' of 'type' objects is not writable");
            }
        }

        public void Delete(PyObject instance)
        {
            throw PyAttributeError.Create("can't delete attribute '__module__'");
        }

        public bool IsDataDescriptor() => true;
    }

    /// <summary>
    /// CPython 3.12: getset_descriptor for __bases__ attribute
    /// Equivalent to CPython's type_get_bases getter/type_set_bases setter
    /// getsetdef: {"__bases__", (getter)type_get_bases, (setter)type_set_bases, NULL}
    /// </summary>
    public class PyBasesDescriptor : PyObject, IDescriptor
    {
        public override string ToString() => "<attribute '__bases__' of 'type' objects>";
        public override PyType GetPyType() => PyType.ObjectType;
        public override string GetTypeName() => "getset_descriptor";

        public PyObject Get(PyObject instance, PyType owner)
        {
            if (instance == null || instance == PyNone.Instance)
                return this;

            // CPython: Return lookup_tp_bases(type)
            // Works for both PyClass and PyType
            if (instance is PyClass pyClass)
            {
                // CPython 3.12 requirement: type.__bases__ is type.__bases__ must be True
                // Return cached tuple to ensure identity consistency
                if (pyClass._cachedBasesTuple == null)
                {
                    var basesArray = new PyObject[pyClass.BaseTypes.Length];
                    for (int i = 0; i < pyClass.BaseTypes.Length; i++)
                    {
                        basesArray[i] = pyClass.BaseTypes[i];
                    }
                    pyClass._cachedBasesTuple = new PyTuple(basesArray);
                }
                return pyClass._cachedBasesTuple;
            }

            if (instance is PyType pyType)
            {
                // CPython 3.12 requirement: type.__bases__ is type.__bases__ must be True
                // Return cached tuple to ensure identity consistency
                if (pyType._cachedBasesTuple == null)
                {
                    var basesArray = new PyObject[pyType.BaseTypes.Length];
                    for (int i = 0; i < pyType.BaseTypes.Length; i++)
                    {
                        basesArray[i] = pyType.BaseTypes[i];
                    }
                    pyType._cachedBasesTuple = new PyTuple(basesArray);
                }
                return pyType._cachedBasesTuple;
            }

            throw PyTypeError.Create($"descriptor '__bases__' for 'type' objects doesn't apply to a '{instance.GetTypeName()}' object");
        }

        public void Set(PyObject instance, PyObject value)
        {
            // CPython: type_set_bases is complex - modifies MRO, checks compatibility, etc.
            // For now, make it read-only to match most common use case
            throw PyAttributeError.Create("attribute '__bases__' of 'type' objects is not writable");
        }

        public void Delete(PyObject instance)
        {
            throw PyAttributeError.Create("can't delete attribute '__bases__'");
        }

        public bool IsDataDescriptor() => true;
    }

    /// <summary>
    /// CPython 3.12: getset_descriptor for __mro__ attribute
    /// Equivalent to CPython's type_get_mro getter (setter=NULL, read-only)
    /// getsetdef: {"__mro__", (getter)type_get_mro, NULL, NULL}
    /// </summary>
    public class PyMroDescriptor : PyObject, IDescriptor
    {
        public override string ToString() => "<attribute '__mro__' of 'type' objects>";
        public override PyType GetPyType() => PyType.ObjectType;
        public override string GetTypeName() => "getset_descriptor";

        public PyObject Get(PyObject instance, PyType owner)
        {
#if DEBUG_LOG
            Console.WriteLine($"🔍 PyMroDescriptor.Get() called:");
            Console.WriteLine($"  instance = {instance} (type: {instance?.GetType().Name})");
            Console.WriteLine($"  owner = {owner} (type: {owner?.GetType().Name})");
            Console.WriteLine($"  instance is PyClass? {instance is PyClass}");
            Console.WriteLine($"  instance is PyType? {instance is PyType}");
            Console.WriteLine($"  instance is PyTypeMetaclass? {instance is PyTypeMetaclass}");
#endif
            // CPython 3.12: When accessing type.__mro__ (instance=NULL, owner=type),
            // return type's MRO, not the descriptor itself
            if (instance == null || instance == PyNone.Instance)
            {
                // If owner is a type, return its MRO
                if (owner is PyClass ownerClass)
                {
                    if (ownerClass._cachedMroTuple == null)
                    {
                        var mroArray = new PyObject[ownerClass.MRO.Count];
                        for (int i = 0; i < ownerClass.MRO.Count; i++)
                        {
                            mroArray[i] = ownerClass.MRO[i];
                        }
                        ownerClass._cachedMroTuple = new PyTuple(mroArray);
                    }
                    return ownerClass._cachedMroTuple;
                }

                if (owner is PyType ownerType)
                {
                    if (ownerType._cachedMroTuple == null)
                    {
                        var mroArray = new PyObject[ownerType.MRO.Count];
                        for (int i = 0; i < ownerType.MRO.Count; i++)
                        {
                            mroArray[i] = ownerType.MRO[i];
                        }
                        ownerType._cachedMroTuple = new PyTuple(mroArray);
                    }
                    return ownerType._cachedMroTuple;
                }

                // Fallback: return descriptor itself for unbound access
                return this;
            }

            // CPython: Return lookup_tp_mro(type)
            // Works for both PyClass and PyType
            if (instance is PyClass pyClass)
            {
                // CPython 3.12 requirement: type.__mro__ is type.__mro__ must be True
                // Return cached tuple to ensure identity consistency
                if (pyClass._cachedMroTuple == null)
                {
                    var mroArray = new PyObject[pyClass.MRO.Count];
                    for (int i = 0; i < pyClass.MRO.Count; i++)
                    {
                        mroArray[i] = pyClass.MRO[i];
                    }
                    pyClass._cachedMroTuple = new PyTuple(mroArray);
                }
                return pyClass._cachedMroTuple;
            }

            if (instance is PyType pyType)
            {
                // CPython 3.12 requirement: type.__mro__ is type.__mro__ must be True
                // Return cached tuple to ensure identity consistency
                if (pyType._cachedMroTuple == null)
                {
                    var mroArray = new PyObject[pyType.MRO.Count];
                    for (int i = 0; i < pyType.MRO.Count; i++)
                    {
                        mroArray[i] = pyType.MRO[i];
                    }
                    pyType._cachedMroTuple = new PyTuple(mroArray);
                }
                return pyType._cachedMroTuple;
            }

            throw PyTypeError.Create($"descriptor '__mro__' for 'type' objects doesn't apply to a '{instance.GetTypeName()}' object");
        }

        public void Set(PyObject instance, PyObject value)
        {
            // CPython: setter=NULL (read-only)
            throw PyAttributeError.Create("attribute '__mro__' of 'type' objects is not writable");
        }

        public void Delete(PyObject instance)
        {
            throw PyAttributeError.Create("can't delete attribute '__mro__'");
        }

        public bool IsDataDescriptor() => true;
    }

    /// <summary>
    /// CPython 3.12: getset_descriptor for __doc__ attribute
    /// Equivalent to CPython's type_get_doc getter/type_set_doc setter
    /// getsetdef: {"__doc__", (getter)type_get_doc, (setter)type_set_doc, NULL}
    /// </summary>
    public class PyDocDescriptor : PyObject, IDescriptor
    {
        public override string ToString() => "<attribute '__doc__' of 'type' objects>";
        public override PyType GetPyType() => PyType.ObjectType;
        public override string GetTypeName() => "getset_descriptor";

        public PyObject Get(PyObject instance, PyType owner)
        {
            if (instance == null || instance == PyNone.Instance)
                return this;

            if (instance is PyClass pyClass)
            {
                // CPython: PyDict_GetItemWithError(dict, &_Py_ID(__doc__))
                if (pyClass.ClassDict.TryGetValue("__doc__", out var doc))
                    return doc;

                // Return None if not found
                return PyNone.Instance;
            }

            return PyNone.Instance;
        }

        public void Set(PyObject instance, PyObject value)
        {
            if (instance is PyClass pyClass)
            {
                // CPython: PyDict_SetItem(dict, &_Py_ID(__doc__), value)
                pyClass.ClassDict["__doc__"] = value;
            }
            else
            {
                throw PyAttributeError.Create("attribute '__doc__' of 'type' objects is not writable");
            }
        }

        public void Delete(PyObject instance)
        {
            throw PyAttributeError.Create("can't delete attribute '__doc__'");
        }

        public bool IsDataDescriptor() => true;
    }

    /// <summary>
    /// CPython 3.12: getset_descriptor for __qualname__ attribute
    /// Equivalent to CPython's type_qualname getter/type_set_qualname setter
    /// getsetdef: {"__qualname__", (getter)type_qualname, (setter)type_set_qualname, NULL}
    /// </summary>
    public class PyQualnameDescriptor : PyObject, IDescriptor
    {
        public override string ToString() => "<attribute '__qualname__' of 'type' objects>";
        public override PyType GetPyType() => PyType.ObjectType;
        public override string GetTypeName() => "getset_descriptor";

        public PyObject Get(PyObject instance, PyType owner)
        {
            if (instance == null || instance == PyNone.Instance)
                return this;

            if (instance is PyClass pyClass)
            {
                // CPython: Return ht_qualname if available, else tp_name
                if (pyClass.ClassDict.TryGetValue("__qualname__", out var qualname))
                    return qualname;

                // Default to __name__
                return new PyString(pyClass.Name);
            }

            // CPython 3.12: For builtin types (PyType), return tp_name
            // Objects/typeobject.c:250-275 (type_qualname getter)
            if (instance is PyType pyType)
            {
                // For builtin types, qualname == name
                return new PyString(pyType.Name);
            }

            return PyNone.Instance;
        }

        public void Set(PyObject instance, PyObject value)
        {
            if (instance is PyClass pyClass)
            {
                if (!(value is PyString))
                    throw PyTypeError.Create($"can only assign string to {pyClass.Name}.__qualname__, not '{value.GetTypeName()}'");

                // CPython: Py_SETREF(et->ht_qualname, Py_NewRef(value))
                pyClass.ClassDict["__qualname__"] = value;
            }
            else
            {
                throw PyAttributeError.Create("attribute '__qualname__' of 'type' objects is not writable");
            }
        }

        public void Delete(PyObject instance)
        {
            throw PyAttributeError.Create("can't delete attribute '__qualname__'");
        }

        public bool IsDataDescriptor() => true;
    }
}