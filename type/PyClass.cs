using System;

namespace SharpPy
{
    #region User-Defined Classes

    /// <summary>
    /// 사용자 정의 클래스
    /// </summary>
    public class PyClass : PyType
    {
        static PyClass()
        {
            InitializeTypeTypeDescriptors();
        }

        private static void InitializeTypeTypeDescriptors()
        {
            var typeType = PyType.TypeType;

            // mro() method descriptor - CPython 3.12 호환
            if (!typeType.TypeDict.ContainsKey("mro"))
            {
                typeType.TypeDict["mro"] = new PyMethodDescriptor(
                    "mro", typeType,
                    (self, args, kwargs) => {
                        if (args.Length != 0)
                            throw PyTypeError.Create("mro() takes no arguments");
                        if (self is not PyType type)
                            throw PyTypeError.Create($"descriptor 'mro' requires a 'type' object but received a '{self.GetTypeName()}'");
                        return new PyList(type.MRO.Cast<PyObject>().ToList());
                    },
                    minArgs: 0, maxArgs: 0
                );
            }

            // CPython 3.12: Objects/typeobject.c:4482
            // {"__class_getitem__", Py_GenericAlias, METH_O|METH_CLASS, PyDoc_STR("See PEP 585")},
            if (!typeType.TypeDict.ContainsKey("__class_getitem__"))
            {
                typeType.TypeDict["__class_getitem__"] = new PyBuiltinClassMethod("__class_getitem__",
                    (cls, arg) => new PyGenericAlias(cls as PyType ?? throw PyTypeError.Create("Expected type"), arg)
                );
            }
        }

        public Dictionary<string, PyObject> ClassDict { get; }
        public List<PyObject>? TypeParams { get; set; } // PEP 695 __type_params__
        public PyClass? Metaclass { get; set; } // Metaclass information for type() calls

        // Cached tuple objects for __mro__ and __bases__ identity consistency
        // CPython 3.12 requirement: type.__mro__ is type.__mro__ must be True
        internal PyTuple _cachedMroTuple;
        internal PyTuple _cachedBasesTuple;
        // Note: __dict__ does NOT cache in CPython 3.12 - each access returns new mappingproxy

        // Note: Method lookups use global TypeMethodCache (CPython-style array cache)
        // See PyType.GlobalMethodCache and LookupInMRO() below

        public PyClass(string name, PyType[] baseTypes, Dictionary<string, PyObject> classDict = null, List<PyObject>? typeParams = null)
            : this(name, baseTypes, classDict, typeParams, null)
        {
        }

        public PyClass(string name, PyType[] baseTypes, Dictionary<string, PyObject> classDict, List<PyObject>? typeParams, string module)
            : base(name, baseTypes, module)
        {
            // CPython 3.12: Objects/typeobject.c:3751 (type_new_init)
            // Line 3751: PyObject *dict = PyDict_Copy(ctx->orig_dict);
            // We must copy the classDict to prevent shared references
            // This is critical for enum classes where update() modifies the dict
            if (classDict != null)
            {
                ClassDict = new Dictionary<string, PyObject>(classDict);
            }
            else
            {
                ClassDict = new Dictionary<string, PyObject>();
            }
            TypeParams = typeParams;
            
            // __type_params__ 속성 설정
            if (TypeParams != null && TypeParams.Count > 0)
            {
                var typeParamsTuple = new PyTuple(TypeParams.ToArray());
                ClassDict["__type_params__"] = typeParamsTuple;
            }
            else
            {
                ClassDict["__type_params__"] = new PyTuple(new PyObject[0]);
            }
            
            // PEP 698: @override 데코레이터 - CPython compatible (no runtime validation at class creation)
            // ValidateOverrideDecorators(); // Disabled for CPython compatibility

            // CPython 3.12: Objects/typeobject.c:6800-6996 (inherit_slots)
            // 부모 타입의 슬롯 메서드 상속
            InheritSlotMethods(baseTypes);
        }

        public new PyObject CreateInstance(params PyObject[] args)
        {
            return CreateInstance(args, null);
        }

        public new PyObject CreateInstance(PyObject[] args, PyDict kwargs)
        {
            // CPython 3.12: Objects/typeobject.c:1627-1689 (type_call)
            // The correct pattern is:
            // 1. Call type->tp_new(type, args, kwds) to create the object
            // 2. If returned object is not instance of type, return it (no __init__)
            // 3. Otherwise, call type->tp_init(obj, args, kwds)

            // Step 1: Call __new__ to create the object
            // CPython 3.12: Objects/typeobject.c:1667
            var newMethod = LookupInMRO("__new__");

            PyObject instance;
            if (newMethod != null)
            {
                // Call __new__ with (cls, *args, **kwargs)
                var newArgs = new PyObject[args.Length + 1];
                newArgs[0] = this;  // cls parameter
                Array.Copy(args, 0, newArgs, 1, args.Length);

                // CPython 3.12: __new__ can be a static method, class method, or builtin
                if (newMethod is PyFunction func)
                {
                    // User-defined __new__ (should be staticmethod, but bound correctly)
                    instance = func.Call(newArgs, kwargs);
                }
                else if (newMethod is PyStaticBuiltinMethod staticBuiltin)
                {
                    // Builtin static __new__ (e.g., int.__new__)
                    instance = staticBuiltin.Call(newArgs, kwargs);
                }
                else if (newMethod is PyBuiltinMethod builtinMethod)
                {
                    // Builtin __new__ from base types (int.__new__, object.__new__, etc.)
                    instance = builtinMethod.Call(newArgs, kwargs);
                }
                else if (newMethod is PyMethodDescriptor descriptor)
                {
                    // Builtin type's __new__ descriptor
                    instance = descriptor.Call(newArgs, kwargs);
                }
                else if (newMethod.IsCallable())
                {
                    instance = newMethod.Call(newArgs, kwargs);
                }
                else
                {
                    throw PyTypeError.Create($"__new__ is not callable");
                }
            }
            else
            {
                // No __new__ found - this should not happen for valid Python classes
                // All classes inherit object.__new__ at minimum
                throw PyTypeError.Create($"cannot create '{Name}' instances: no __new__ method");
            }

            // Step 2: Check if returned object is an instance of this type
            // CPython 3.12: Objects/typeobject.c:1672-1675
            // If __new__ returned a different type, return it immediately (no __init__)
            if (instance.GetPyType() != this)
            {
                return instance;
            }

            // Store constructor arguments for toString() behavior
            if (instance is PyClassInstance classInstance)
            {
                classInstance.ConstructorArgs = args;
            }
            else if (instance is PyTupleSubclass tupleSubclass)
            {
                tupleSubclass.ConstructorArgs = args;
            }

            // Step 3: Call __init__ on the instance
            // CPython 3.12: Objects/typeobject.c:1677-1687
            // __init__ lookup bypasses __getattribute__ (uses _PyType_Lookup)
            // Reference: Objects/typeobject.c:9028 (slot_tp_init -> lookup_method -> _PyType_Lookup)
            var init = LookupInMRO("__init__");
            if (init != null)
            {
                // Apply descriptor protocol if needed
                if (init is IDescriptor desc)
                {
                    init = desc.Get(instance, this);
                }
                else if (init is PyFunction function)
                {
                    // Convert function to bound method
                    init = new PyMethod(instance, function);
                }

                // Call the bound init method
                if (init is PyMethod method)
                {
                    // PyMethod는 이미 self가 바인딩되어 있으므로 args만 전달
                    method.Call(args, kwargs);
                }
                else if (init is PyBuiltinMethod builtinMethod)
                {
                    // Builtin method도 이미 바인딩되어 있음
                    builtinMethod.Call(args, kwargs);
                }
                else
                {
                    // Fallback: callable object
                    init.Call(args, kwargs);
                }
            }

            return instance;
        }

        /// <summary>
        /// CPython 3.12: Objects/typeobject.c:6800-6996 (inherit_slots)
        /// 부모 builtin 타입의 슬롯 메서드 상속
        /// </summary>
        private void InheritSlotMethods(PyType[] bases)
        {
            // CPython에서 상속되는 주요 슬롯 메서드들
            var slotMethods = new[] {
                // Iterator protocol (CPython: tp_iter, tp_iternext)
                "__iter__",      // CPython 3.12: Objects/typeobject.c:6956
                "__next__",      // CPython 3.12: Objects/typeobject.c:6957

                // Sequence protocol (CPython: sq_length, sq_item, etc.)
                "__len__",       // CPython 3.12: Objects/typeobject.c:6884
                "__getitem__",   // CPython 3.12: Objects/typeobject.c:6887
                "__setitem__",   // CPython 3.12: Objects/typeobject.c:6888
                "__delitem__",   // CPython 3.12: Objects/typeobject.c:6888
                "__contains__",  // CPython 3.12: Objects/typeobject.c:6889

                // Mapping protocol (for dict subclasses)
                "keys",
                "values",
                "items",
                "get",
                "pop",
                "popitem",
                "clear",
                "update",
                "setdefault",

                // Callable protocol (CPython: tp_call)
                "__call__",      // CPython 3.12: Objects/typeobject.c:6936

                // Comparison (CPython: tp_richcompare)
                "__eq__",        // CPython 3.12: Objects/typeobject.c:6950
                "__ne__",
                "__lt__",
                "__le__",
                "__gt__",
                "__ge__",

                // String representation (CPython: tp_str, tp_repr)
                "__str__",       // CPython 3.12: Objects/typeobject.c:6938
                "__repr__",      // CPython 3.12: Objects/typeobject.c:6922

                // Attribute access (CPython: tp_getattro, tp_setattro)
                "__getattribute__",  // CPython 3.12: Objects/typeobject.c:6916
                "__setattr__",       // CPython 3.12: Objects/typeobject.c:6920
                "__delattr__",

                // Hashing (CPython: tp_hash)
                "__hash__",      // CPython 3.12: Objects/typeobject.c:6951
            };

            foreach (var baseType in bases)
            {
                // builtin 타입만 슬롯 메서드 상속 (Python 클래스는 일반 MRO 사용)
                if (!IsBuiltinType(baseType))
                    continue;

                foreach (var slotMethod in slotMethods)
                {
                    // 현재 클래스에 없고 부모에 있으면 상속
                    if (!TypeDict.ContainsKey(slotMethod) &&
                        baseType.TypeDict.TryGetValue(slotMethod, out var method))
                    {
                        TypeDict[slotMethod] = method;
                    }
                }
            }
        }

        /// <summary>
        /// CPython builtin 타입 확인
        /// </summary>
        private bool IsBuiltinType(PyType type)
        {
            // CPython builtin 타입들
            return type == PyType.ListType ||
                   type == PyType.DictType ||
                   type == PyType.TupleType ||
                   type == PyType.SetType ||
                   type == PyType.FrozenSetType ||
                   type == PyType.StrType ||
                   type == PyType.BytesType ||
                   type == PyType.IntType ||
                   type == PyType.FloatType ||
                   type == PyType.BoolType ||
                   type == PyType.ObjectType;
        }

        public bool HasMethod(string name)
        {
            return MRO.OfType<PyClass>().Any(t => t.ClassDict.ContainsKey(name));
        }

        /// <summary>
        /// CPython's _PyType_Lookup equivalent: Look up a name in the type's MRO
        /// This bypasses __getattribute__ and goes directly to tp_dict/__dict__
        /// Used for special method lookup (PEP 252: "Special method lookup bypasses __getattribute__()")
        /// Reference: Objects/typeobject.c:4725 (_PyType_Lookup)
        /// </summary>
        public PyObject LookupInMRO(string name)
        {
            // Phase 2: Use global TypeMethodCache (CPython-style array cache)
            // This is faster than Dictionary (15-25 cycles vs 30-100+ cycles)
            return PyType.GlobalMethodCache.Lookup(this, name, out _);
        }

        // 클래스 호출 시 인스턴스 생성
        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            // Check for abstract methods before allowing instantiation
            CheckAbstractMethods();
            return CreateInstance(args, kwargs);
        }

        // 클래스는 항상 호출 가능 (인스턴스 생성)
        public override bool IsCallable() => true;

        // PEP 695: Generic class subscript support (Stack[int])
        public override PyObject GetItem(PyObject key)
        {
            // CPython 3.12: PEP 560 - Check for __class_getitem__ first
            // Delegate to PyType.GetItem() which handles the full protocol
            return base.GetItem(key);
        }

        // CPython 3.12: Override GetPyType to return metaclass if set
        public override PyType GetPyType()
        {
            #if DEBUG_LOG
            Console.WriteLine($"🔍 PyClass.GetPyType() called for {Name}");
            Console.WriteLine($"   Metaclass: {Metaclass}");
            Console.WriteLine($"   Metaclass != null: {Metaclass != null}");
            #endif

            // If this class was created with a metaclass, return the metaclass
            if (Metaclass != null)
            {
                #if DEBUG_LOG
                Console.WriteLine($"   → Returning Metaclass: {Metaclass}");
                #endif
                return Metaclass;
            }
            // Otherwise, return the default type (which is 'type')
            #if DEBUG_LOG
            Console.WriteLine($"   → Returning base.GetPyType()");
            #endif
            var baseType = base.GetPyType();
            #if DEBUG_LOG
            Console.WriteLine($"   → base.GetPyType() returned: {baseType}");
            #endif
            return baseType;
        }

        // CPython 3.12: Override GetIterator to check metaclass __iter__
        public override PyObject GetIterator()
        {
            // Check if metaclass has __iter__ method
            if (Metaclass != null)
            {
                // Check metaclass's ClassDict directly
                if (Metaclass is PyClass metaClass && metaClass.ClassDict.TryGetValue("__iter__", out PyObject iterMethod))
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"   ✅ found __iter__ in metaclass ClassDict");
                    #endif

                    if (iterMethod.IsCallable())
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"   → calling __iter__ with this class as argument");
                        #endif
                        // Call metaclass's __iter__ with this class as argument
                        var iterResult = iterMethod.Call(new PyObject[] { this }, null);
                        #if DEBUG_LOG
                        Console.WriteLine($"   → __iter__ returned: {iterResult?.GetTypeName()}");
                        #endif
                        return iterResult;
                    }
                }

                // Also check metaclass MRO (e.g., if EnumType inherits from type)
                for (int i = 1; i < Metaclass.MRO.Count; i++)
                {
                    var metaBase = Metaclass.MRO[i];
                    if (metaBase is PyClass metaPyClass && metaPyClass.ClassDict.TryGetValue("__iter__", out PyObject metaIterMethod))
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"   ✅ found __iter__ in metaclass MRO ({metaBase.Name})");
                        #endif

                        if (metaIterMethod.IsCallable())
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"   → calling metaclass MRO __iter__ with this class as argument");
                            #endif
                            var iterResult = metaIterMethod.Call(new PyObject[] { this }, null);
                            #if DEBUG_LOG
                            Console.WriteLine($"   → __iter__ returned: {iterResult?.GetTypeName()}");
                            #endif
                            return iterResult;
                        }
                    }
                }

                #if DEBUG_LOG
                Console.WriteLine($"   ❌ No __iter__ found in metaclass or its MRO");
                #endif
            }

            // Fall back to base behavior (which throws "not iterable" error)
            return base.GetIterator();
        }

        // 클래스 attribute 접근
        public override PyObject GetAttribute(string name)
        {
            #if DEBUG_LOG
            Console.WriteLine($"🔍 PyClass.GetAttribute: {Name}.{name}");
            #endif
            
            // CPython 3.12: All type attributes (__name__, __bases__, __mro__, __dict__, __module__, etc.)
            // are now handled by descriptors in the metaclass, no need for hardcoded switch cases
            switch (name)
            {
                case "__call__":
                    // CPython 3.12: __call__ is special - class itself is callable
                    #if DEBUG_LOG
                    Console.WriteLine($"   → returning self for __call__");
                    #endif
                    return this;
                default:
                    #if DEBUG_LOG
                    Console.WriteLine($"   → searching for '{name}' in ClassDict ({ClassDict.Count} items)");
                    #endif

                    if (ClassDict.TryGetValue(name, out PyObject value))
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"   ✅ found '{name}' in ClassDict: {value?.GetType().Name}");
                        #endif
                        // Descriptor 처리 (CPython 3.12: Objects/descrobject.c:271-286, func_descr_get)
                        // CPython: When accessing attribute from CLASS (not instance), pass NULL as instance
                        // Example: MyClass.func → descriptor.__get__(NULL, MyClass)
                        //          instance.func → descriptor.__get__(instance, type(instance))
                        if (PyClassInstance.IsDescriptor(value))
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"   🔧 calling descriptor.__get__(null, owner={Name}) for '{name}'");
                            #endif
                            // CPython 3.12: Objects/descrobject.c:271-286 (func_descr_get)
                            // Pass NULL as instance when accessing from CLASS object
                            // This makes function descriptors return unbound functions
                            var result = PyClassInstance.CallDescriptorGet(value, null, this);
                            #if DEBUG_LOG
                            Console.WriteLine($"   → descriptor returned: {result?.GetType().Name}");
                            #endif
                            return result;
                        }
                        #if DEBUG_LOG
                        Console.WriteLine($"   → returning direct value: {value}");
                        #endif
                        return value;
                    }
                    #if DEBUG_LOG
                    Console.WriteLine($"   ❌ '{name}' not found in ClassDict, checking MRO");
                    #endif

                    // Check MRO (Method Resolution Order) - skip self (index 0)
                    for (int i = 1; i < MRO.Count; i++)
                    {
                        var baseClass = MRO[i];

                        // Handle both PyClass and PyType in MRO
                        if (baseClass is PyClass pyClass)
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"   → Checking MRO class: {pyClass.Name}");
                            #endif
                            if (pyClass.ClassDict.TryGetValue(name, out PyObject baseValue))
                            {
                                #if DEBUG_LOG
                                Console.WriteLine($"   ✅ found '{name}' in MRO class {pyClass.Name}: {baseValue?.GetType().Name}");
                                #endif
                                // Descriptor 처리 (CPython 3.12: Objects/typeobject.c:4830)
                                if (PyClassInstance.IsDescriptor(baseValue))
                                {
                                    #if DEBUG_LOG
                                    Console.WriteLine($"   🔧 calling descriptor.__get__(null, {Name}) for '{name}' from MRO");
                                    #endif
                                    // CPython 3.12: Class attribute access passes NULL as instance
                                    // Reference: Objects/funcobject.c:892 (if obj == NULL, return Py_NewRef(func))
                                    var result = PyClassInstance.CallDescriptorGet(baseValue, null, this);
                                    #if DEBUG_LOG
                                    Console.WriteLine($"   → descriptor returned: {result?.GetType().Name}");
                                    #endif
                                    return result;
                                }
                                return baseValue;
                            }
                        }
                        else if (baseClass is PyType pyType)
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"   → Checking MRO type: {pyType.Name}");
                            #endif
                            // CPython 3.12: PyType의 속성을 직접 체크
                            // 재귀 방지를 위해 PyType의 internal attribute lookup 사용
                            var typeAttribute = GetTypeAttribute(pyType, name);
                            if (typeAttribute != null)
                            {
                                #if DEBUG_LOG
                                Console.WriteLine($"   ✅ found '{name}' in MRO type {pyType.Name}: {typeAttribute?.GetType().Name}");
                                #endif
                                // CPython 3.12: Objects/typeobject.c:4852-4859
                                // When descriptor is found in type's tp_dict (via MRO),
                                // call descriptor.__get__(NULL, owner) where owner is this class
                                if (PyClassInstance.IsDescriptor(typeAttribute))
                                {
                                    #if DEBUG_LOG
                                    Console.WriteLine($"   🔧 calling descriptor.__get__(null, {Name}) for '{name}' from PyType MRO");
                                    #endif
                                    var result = PyClassInstance.CallDescriptorGet(typeAttribute, null, this);
                                    #if DEBUG_LOG
                                    Console.WriteLine($"   → descriptor returned: {result?.GetType().Name}");
                                    #endif
                                    return result;
                                }
                                return typeAttribute;
                            }
                        }
                    }

                    // CPython 3.12: Check metaclass (type of this class) for attributes
                    #if DEBUG_LOG
                    Console.WriteLine($"   ❌ '{name}' not found in MRO, checking metaclass");
                    #endif
                    if (Metaclass != null)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"   → checking metaclass '{Metaclass.Name}' for '{name}'");
                        #endif

                        // Check metaclass's ClassDict and MRO
                        if (Metaclass.ClassDict.TryGetValue(name, out PyObject metaclassValue))
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"   ✅ found '{name}' in metaclass: {metaclassValue?.GetType().Name}");
                            #endif

                            // Descriptor 처리 - metaclass descriptor는 class 객체에 바인딩
                            if (metaclassValue is IDescriptor metaDesc)
                            {
                                #if DEBUG_LOG
                                Console.WriteLine($"   🔧 calling metaclass descriptor.Get({Name}, {Metaclass.Name})");
                                #endif
                                var result = metaDesc.Get(this, Metaclass);
                                #if DEBUG_LOG
                                Console.WriteLine($"   → metaclass descriptor returned: {result?.GetType().Name}");
                                #endif
                                return result;
                            }

                            return metaclassValue;
                        }

                        // Check metaclass MRO (e.g., type's __iter__)
                        for (int i = 1; i < Metaclass.MRO.Count; i++)
                        {
                            var metaBase = Metaclass.MRO[i];
                            if (metaBase is PyClass metaPyClass && metaPyClass.ClassDict.TryGetValue(name, out PyObject metaBaseValue))
                            {
                                #if DEBUG_LOG
                                Console.WriteLine($"   ✅ found '{name}' in metaclass MRO ({metaBase.Name}): {metaBaseValue?.GetType().Name}");
                                #endif

                                // Descriptor 처리
                                if (metaBaseValue is IDescriptor metaBaseDesc)
                                {
                                    #if DEBUG_LOG
                                    Console.WriteLine($"   🔧 calling metaclass MRO descriptor.Get({Name}, {Metaclass.Name})");
                                    #endif
                                    return metaBaseDesc.Get(this, Metaclass);
                                }

                                return metaBaseValue;
                            }
                        }
                    }

                    #if DEBUG_LOG
                    Console.WriteLine($"   ❌ '{name}' not found in metaclass, calling PyObject.GetAttribute");
                    #endif
                    var baseResult = base.GetAttribute(name);
                    #if DEBUG_LOG
                    Console.WriteLine($"   → base.GetAttribute returned: {baseResult?.GetType().Name}");
                    #endif
                    return baseResult;
            }
        }

        public override void SetAttribute(string name, PyObject value)
        {
#if DEBUG
            // Debug: Track _generate_next_value_ modifications
            if (name == "_generate_next_value_")
            {
                Console.WriteLine($"[PyClass.SetAttribute DEBUG] Setting '{name}' on class '{Name}'");
                var oldValue = ClassDict.ContainsKey(name) ? ClassDict[name].ToString() : "NOT SET";
                var oldType = ClassDict.ContainsKey(name) ? ClassDict[name].GetTypeName() : "N/A";
                Console.WriteLine($"[PyClass.SetAttribute DEBUG]   Old value: {oldValue}, type={oldType}");
                Console.WriteLine($"[PyClass.SetAttribute DEBUG]   New value: {value}, type={value.GetTypeName()}");
                // Print stack trace to see where this is being called from
                var stackTrace = new System.Diagnostics.StackTrace(1, true);
                Console.WriteLine($"[PyClass.SetAttribute DEBUG]   Stack trace:");
                for (int i = 0; i < Math.Min(15, stackTrace.FrameCount); i++)
                {
                    var frame = stackTrace.GetFrame(i);
                    var method = frame?.GetMethod();
                    Console.WriteLine($"[PyClass.SetAttribute DEBUG]     [{i}] {method?.DeclaringType?.Name}.{method?.Name}");
                }
            }
#endif
            ClassDict[name] = value;

            // CPython 3.12: Invalidate method cache when class dict changes
            // Reference: Objects/typeobject.c:420-522 (type_modified)
            InvalidateTypeCache();
        }

        // CPython 3.12: Override DelAttribute for type objects
        protected override void PyDelAttribute(string name)
        {
            if (ClassDict.ContainsKey(name))
            {
                ClassDict.Remove(name);

                // CPython 3.12: Invalidate method cache when class dict changes
                InvalidateTypeCache();
                return;
            }
            throw PyAttributeError.Create($"'{GetTypeName()}' object has no attribute '{name}'");
        }

        /// <summary>
        /// PEP 698: @override 데코레이터 런타임 검증
        /// 클래스가 생성될 때 @override가 적용된 메서드들이 실제로 부모 클래스의 메서드를 오버라이드하는지 확인
        /// </summary>
        private void ValidateOverrideDecorators()
        {
            foreach (var kvp in ClassDict)
            {
                string methodName = kvp.Key;
                PyObject methodValue = kvp.Value;

                // 함수이고 __override__ 속성이 True인 경우만 검증
                if (methodValue is PyFunction function && HasOverrideAttribute(function))
                {
                    // 부모 클래스들에서 이 메서드가 존재하는지 확인
                    bool foundInBase = false;
                    
                    foreach (var baseType in BaseTypes)
                    {
                        if (baseType != PyType.ObjectType && HasMethodInBase(baseType, methodName))
                        {
                            foundInBase = true;
                            break;
                        }
                    }
                    
                    if (!foundInBase)
                    {
                        throw PyTypeError.Create($"Method '{methodName}' is marked with @override, but does not override any method in base class");
                    }
                }
            }
        }

        /// <summary>
        /// 함수가 @override 데코레이터를 가지고 있는지 확인
        /// </summary>
        private bool HasOverrideAttribute(PyFunction function)
        {
            try
            {
                if (function.Attributes.TryGetValue("__override__", out PyObject overrideAttr))
                {
                    return overrideAttr.PyBoolValue();
                }
            }
            catch
            {
                // 속성 접근 실패시 false 반환
            }
            return false;
        }

        /// <summary>
        /// 기반 클래스에서 특정 메서드가 존재하는지 확인
        /// </summary>
        private bool HasMethodInBase(PyType baseType, string methodName)
        {
            try
            {
                // 직접 속성 확인
                if (baseType is PyClass baseClass)
                {
                    if (baseClass.ClassDict.ContainsKey(methodName))
                        return true;
                }
                
                // GetAttribute로 시도 (상속 체인을 통해)
                try
                {
                    var attr = baseType.GetAttribute(methodName);
                    return attr is PyFunction || attr is PyMethod;
                }
                catch (Exception)
                {
                    // 속성이 없거나 접근 실패시 false
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// CPython 3.12 compatible: Safe type attribute lookup without recursion
        /// </summary>
        public static PyObject? GetTypeAttribute(PyType pyType, string name)
        {
            // CPython 3.12: 내장 타입의 속성을 직접 조회
            // 재귀를 방지하기 위해 PyType의 내부 구조를 직접 사용

            try
            {
                // FIRST: Check TypeDict for descriptors (like __setattr__, __getattribute__, etc.)
                // This is critical for finding dynamically added descriptors
                if (pyType.TypeDict != null && pyType.TypeDict.TryGetValue(name, out PyObject descriptor))
                {
                    return descriptor;
                }

                // SECOND: Fall back to hardcoded switch for legacy/special attributes
                // object 타입의 기본 속성들
                if (pyType == PyType.ObjectType)
                {
                    return name switch
                    {
                        "__class__" => PyType.TypeType,
                        "__str__" => new PyBuiltinMethod("__str__", (self, args) => {
                            if (args.Length != 0)
                                throw PyTypeError.Create($"__str__() takes no arguments ({args.Length} given)");
                            return self.ToStr();
                        }, 1),
                        "__repr__" => new PyBuiltinMethod("__repr__", (self, args) => {
                            if (args.Length != 0)
                                throw PyTypeError.Create($"__repr__() takes no arguments ({args.Length} given)");
                            return self.ToRepr();
                        }, 1),
                        "__init__" => new PyBuiltinMethod("__init__", (self, args) => PyNone.Instance, 1),
                        "__hash__" => new PyBuiltinMethod("__hash__", (self, args) => {
                            if (args.Length != 0)
                                throw PyTypeError.Create($"__hash__() takes no arguments ({args.Length} given)");
                            return new PyInt(self.GetHashCode());
                        }, 1),
                        "__eq__" => new PyBuiltinMethod("__eq__", (self, args) => {
                            if (args.Length != 1)
                                throw PyTypeError.Create($"__eq__() takes exactly 1 argument ({args.Length} given)");
                            return self.RichCompare(args[0], PyObject.CompareOp.EQ);
                        }, 2),
                        "__ne__" => new PyBuiltinMethod("__ne__", (self, args) => {
                            if (args.Length != 1)
                                throw PyTypeError.Create($"__ne__() takes exactly 1 argument ({args.Length} given)");
                            return self.RichCompare(args[0], PyObject.CompareOp.NE);
                        }, 2),
                        _ => null
                    };
                }

                // type 타입의 기본 속성들
                if (pyType == PyType.TypeType)
                {
                    return name switch
                    {
                        "__mro__" => null, // 이미 PyClass.GetAttribute에서 처리됨
                        "__bases__" => null, // 이미 PyClass.GetAttribute에서 처리됨
                        "__name__" => null, // 이미 PyClass.GetAttribute에서 처리됨
                        _ => null
                    };
                }

                // dict 타입의 메서드들 - CPython 3.12 compatible
                if (pyType == PyType.DictType)
                {
                    // Helper to get PyDict from self (works for both PyDict and dict subclasses)
                    static PyDict GetDictStorage(PyObject self)
                    {
                        if (self is PyDict dict)
                            return dict;

                        if (self is PyClassInstance instance && instance.InstanceType.BaseTypes.Any(bt => bt == PyType.DictType))
                        {
                            // Access the _dictStorage field via reflection or provide public accessor
                            var field = typeof(PyClassInstance).GetField("_dictStorage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            return (PyDict)field?.GetValue(instance);
                        }

                        throw PyTypeError.Create($"descriptor requires a 'dict' object but received a '{self.GetTypeName()}'");
                    }

                    return name switch
                    {
                        "__init__" => new PyMethodDescriptor("__init__", PyType.DictType, (self, args, kwargs) =>
                        {
                            // CPython 3.12: dict.__init__ can accept optional args and kwargs
                            // dict() -> empty dict
                            // dict(mapping) -> dict initialized from a mapping
                            // dict(**kwargs) -> dict initialized with keyword arguments
                            // dict(iterable) -> dict initialized from iterable of pairs
                            // For dict subclasses (like _EnumDict), just return None
                            // The subclass's __init__ will handle its own initialization
                            return PyNone.Instance;
                        }, minArgs: 0, maxArgs: int.MaxValue, acceptsKwargs: true),

                        // CPython 3.12: __getitem__ wrapper descriptor (mp_subscript slot)
                        "__getitem__" => new PyWrapperDescriptor(
                            "__getitem__",
                            PyType.DictType,
                            (self, args, kwargs) =>
                            {
                                if (args.Length != 1)
                                    throw PyTypeError.Create($"__getitem__() takes exactly 1 argument ({args.Length} given)");

                                var key = args[0];
                                // CPython 3.12: Call self.GetItem() which allows subclass override
                                return self.GetItem(key);
                            }),

                        // CPython 3.12: __setitem__ wrapper descriptor (mp_ass_subscript slot)
                        "__setitem__" => new PyWrapperDescriptor(
                            "__setitem__",
                            PyType.DictType,
                            (self, args, kwargs) =>
                            {
                                if (args.Length != 2)
                                    throw PyTypeError.Create($"__setitem__() takes exactly 2 arguments ({args.Length} given)");

                                var key = args[0];
                                var value = args[1];

                                // CPython 3.12: Direct storage access (dict_ass_sub in C)
                                // This wrapper descriptor represents the C-level slot, not Python method
                                PyDict dict;
                                if (self is PyDict d)
                                {
                                    dict = d;
                                }
                                else if (self is PyClassInstance ci && ci.IsDictSubclass())
                                {
                                    dict = ci.GetDictStorage();
                                }
                                else
                                {
                                    throw PyTypeError.Create($"descriptor '__setitem__' for 'dict' objects doesn't apply to a '{self.GetTypeName()}'  object");
                                }

                                dict.SetItem(key, value);
                                return PyNone.Instance;
                            }),

                        "get" => new PyBuiltinFunction("get", (args, kwargs) =>
                        {
                            if (args.Length < 2 || args.Length > 3)
                                throw PyTypeError.Create($"get() takes from 2 to 3 positional arguments but {args.Length} were given");

                            var self = args[0];
                            var key = args[1];
                            var defaultValue = args.Length > 2 ? args[2] : PyNone.Instance;

                            // CPython 3.12: Use GetItem with exception handling (works for both PyDict and dict subclasses)
                            try
                            {
                                return self.GetItem(key);
                            }
                            catch (PythonException ex) when (ex.PyException is PyKeyError)
                            {
                                return defaultValue;
                            }
                        }),

                        "keys" => new PyBuiltinFunction("keys", (args, kwargs) =>
                        {
                            if (args.Length != 1)
                                throw PyTypeError.Create($"keys() takes exactly 1 argument ({args.Length} given)");

                            var dict = GetDictStorage(args[0]);
                            return dict.Keys();
                        }),

                        "values" => new PyBuiltinFunction("values", (args, kwargs) =>
                        {
                            if (args.Length != 1)
                                throw PyTypeError.Create($"values() takes exactly 1 argument ({args.Length} given)");

                            var dict = GetDictStorage(args[0]);
                            return dict.Values();
                        }),

                        "items" => new PyBuiltinFunction("items", (args, kwargs) =>
                        {
                            if (args.Length != 1)
                                throw PyTypeError.Create($"items() takes exactly 1 argument ({args.Length} given)");

                            var dict = GetDictStorage(args[0]);
                            return dict.Items();
                        }),

                        "pop" => new PyBuiltinFunction("pop", (args, kwargs) =>
                        {
                            if (args.Length < 2 || args.Length > 3)
                                throw PyTypeError.Create($"pop() takes from 2 to 3 positional arguments but {args.Length} were given");

                            var dict = GetDictStorage(args[0]);
                            var key = args[1];
                            var defaultValue = args.Length > 2 ? args[2] : null;
                            return dict.Pop(key, defaultValue);
                        }),

                        "clear" => new PyBuiltinFunction("clear", (args, kwargs) =>
                        {
                            if (args.Length != 1)
                                throw PyTypeError.Create($"clear() takes exactly 1 argument ({args.Length} given)");

                            var dict = GetDictStorage(args[0]);
                            dict.Clear();
                            return PyNone.Instance;
                        }),

                        "update" => new PyBuiltinFunction("update", (args, kwargs) =>
                        {
                            if (args.Length != 2)
                                throw PyTypeError.Create($"update() takes exactly 2 arguments ({args.Length} given)");

                            var dict = GetDictStorage(args[0]);

                            if (args[1] is PyDict otherDict)
                            {
                                dict.Update(otherDict);
                            }
                            else if (args[1] is PyClassInstance otherInstance && otherInstance.InstanceType.BaseTypes.Any(bt => bt == PyType.DictType))
                            {
                                // Dict subclass - get its storage
                                var otherStorage = GetDictStorage(args[1]);
                                dict.Update(otherStorage);
                            }
                            else
                            {
                                throw PyTypeError.Create($"update() argument must be dict, not '{args[1].GetTypeName()}'");
                            }
                            return PyNone.Instance;
                        }),

                        "setdefault" => new PyBuiltinFunction("setdefault", (args, kwargs) =>
                        {
                            if (args.Length < 2 || args.Length > 3)
                                throw PyTypeError.Create($"setdefault() takes from 2 to 3 positional arguments but {args.Length} were given");

                            var dict = GetDictStorage(args[0]);
                            var key = args[1];
                            var defaultValue = args.Length > 2 ? args[2] : PyNone.Instance;
                            return dict.SetDefault(key, defaultValue);
                        }),

                        "copy" => new PyBuiltinFunction("copy", (args, kwargs) =>
                        {
                            if (args.Length != 1)
                                throw PyTypeError.Create($"copy() takes exactly 1 argument ({args.Length} given)");

                            var dict = GetDictStorage(args[0]);
                            return dict.Copy();
                        }),

                        _ => null
                    };
                }

                // list 타입의 메서드들 - CPython 3.12 compatible
                if (pyType == PyType.ListType)
                {
                    // CPython 3.12: Objects/listobject.c
                    // Helper to get PyList from self (works for both PyList and list subclasses)
                    static PyList GetListStorage(PyObject self)
                    {
                        if (self is PyList list)
                            return list;

                        if (self is PyClassInstance instance && instance.IsListSubclass())
                        {
                            // List subclass: return internal list storage
                            return instance.GetListStorage();
                        }

                        throw PyTypeError.Create($"descriptor requires a 'list' object but received a '{self.GetTypeName()}'");
                    }

                    return name switch
                    {
                        // CPython 3.12: __getitem__ wrapper descriptor (sq_item slot)
                        // CPython 3.12: Objects/listobject.c:420-449 (list_subscript)
                        "__getitem__" => new PyWrapperDescriptor(
                            "__getitem__",
                            PyType.ListType,
                            (self, args, kwargs) =>
                            {
                                if (args.Length != 1)
                                    throw PyTypeError.Create($"__getitem__() takes exactly 1 argument ({args.Length} given)");

                                var key = args[0];
                                // CPython 3.12: Direct storage access to avoid infinite loop
                                // Use GetListStorage() to support both PyList and list subclasses
                                var list = GetListStorage(self);
                                return list.GetItem(key);
                            }),

                        // CPython 3.12: __setitem__ wrapper descriptor (sq_ass_item slot)
                        "__setitem__" => new PyWrapperDescriptor(
                            "__setitem__",
                            PyType.ListType,
                            (self, args, kwargs) =>
                            {
                                if (args.Length != 2)
                                    throw PyTypeError.Create($"__setitem__() takes exactly 2 arguments ({args.Length} given)");

                                var key = args[0];
                                var value = args[1];

                                // CPython 3.12: Direct storage access (list_ass_item in C)
                                // Use GetListStorage() to support both PyList and list subclasses
                                var list = GetListStorage(self);
                                list.SetItem(key, value);
                                return PyNone.Instance;
                            }),

                        // CPython 3.12: __delitem__ wrapper descriptor (sq_ass_item slot with NULL value)
                        "__delitem__" => new PyWrapperDescriptor(
                            "__delitem__",
                            PyType.ListType,
                            (self, args, kwargs) =>
                            {
                                if (args.Length != 1)
                                    throw PyTypeError.Create($"__delitem__() takes exactly 1 argument ({args.Length} given)");

                                var key = args[0];

                                // CPython 3.12: Direct storage access
                                PyList list;
                                if (self is PyList l)
                                {
                                    list = l;
                                }
                                else
                                {
                                    throw PyTypeError.Create($"descriptor '__delitem__' for 'list' objects doesn't apply to a '{self.GetTypeName()}' object");
                                }

                                // Delete item at index
                                if (key is PyInt index)
                                {
                                    int idx = (int)index.Value;
                                    int count = list.Length();
                                    if (idx < 0) idx += count;
                                    if (idx < 0 || idx >= count)
                                        throw PyIndexError.Create("list index out of range");
                                    // Use list.Pop() which handles deletion correctly
                                    list.Pop(idx);
                                }
                                else if (key is PySlice slice)
                                {
                                    // CPython 3.12: Objects/listobject.c:623-737 (list_ass_slice)
                                    // Handle slice deletion: del list[start:stop:step]
                                    var length = list.Length();
                                    var (start, stop, step) = slice.Indices(length);
                                    var sliceLength = slice.GetLength(length);

                                    if (step == 1)
                                    {
                                        // Simple case: contiguous deletion
                                        // del list[start:stop] removes items from start to stop-1
                                        // CPython: list_ass_slice(a, ilow, ihigh, NULL) with v=NULL means delete
                                        for (int i = 0; i < sliceLength; i++)
                                        {
                                            list.Pop(start);
                                        }
                                    }
                                    else if (step > 0)
                                    {
                                        // Extended slice deletion with positive step
                                        // Delete from end to beginning to maintain indices
                                        var indicesToDelete = new List<int>();
                                        for (int i = start; i < stop; i += step)
                                        {
                                            if (i >= 0 && i < length)
                                                indicesToDelete.Add(i);
                                        }
                                        // Remove in reverse order
                                        for (int i = indicesToDelete.Count - 1; i >= 0; i--)
                                        {
                                            list.Pop(indicesToDelete[i]);
                                        }
                                    }
                                    else // step < 0
                                    {
                                        // Extended slice deletion with negative step
                                        var indicesToDelete = new List<int>();
                                        for (int i = start; i > stop; i += step)
                                        {
                                            if (i >= 0 && i < length)
                                                indicesToDelete.Add(i);
                                        }
                                        // Remove in reverse order
                                        indicesToDelete.Sort();
                                        indicesToDelete.Reverse();
                                        foreach (var idx in indicesToDelete)
                                        {
                                            list.Pop(idx);
                                        }
                                    }
                                }
                                else
                                {
                                    throw PyTypeError.Create("list indices must be integers or slices, not " + key.GetTypeName());
                                }

                                return PyNone.Instance;
                            }),

                        _ => null
                    };
                }

                // 다른 내장 타입들은 기본적으로 object의 속성을 상속
                if (pyType.Name == "object")
                    return null;

                // 기본적으로는 null 반환 (속성 없음)
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Check if this class has any unimplemented abstract methods
        /// Throws TypeError if abstract methods are found (CPython 3.12 compatible)
        /// Reference: Objects/typeobject.c:5459-5505 (object_new)
        /// </summary>
        private void CheckAbstractMethods()
        {
            // CPython 3.12: Check __abstractmethods__ attribute directly
            // Reference: Objects/typeobject.c:5468 (type_abstractmethods)
            PyObject abstractMethodsAttr = null;
            try
            {
                // Get __abstractmethods__ from class (NOT instance)
                // This is set by AbcModule._abc_init or manually
                abstractMethodsAttr = this.GetAttribute("__abstractmethods__");
            }
            catch
            {
                // No __abstractmethods__ attribute - class is not abstract
                return;
            }

            // Check if __abstractmethods__ is None or empty
            if (abstractMethodsAttr == null || abstractMethodsAttr == PyNone.Instance)
            {
                return;
            }

            // __abstractmethods__ should be a frozenset or set
            // If it's non-empty, prevent instantiation
            if (abstractMethodsAttr is PyFrozenSet frozenSet)
            {
                if (frozenSet.Items.Count == 0)
                {
                    return; // Empty frozenset - all methods implemented
                }

                // CPython 3.12: Sort method names and format error message
                // Reference: Objects/typeobject.c:5471-5502
                var methodNames = frozenSet.Items
                    .Select(item => ((PyString)item).Value)
                    .OrderBy(name => name)
                    .ToList();

                // Format: "Can't instantiate abstract class X without an implementation for abstract method 'foo'"
                // or: "Can't instantiate abstract class X without an implementation for abstract methods 'bar', 'foo'"
                string joinedMethods = string.Join("', '", methodNames);
                string methodWord = methodNames.Count == 1 ? "method" : "methods";

                throw PyTypeError.Create(
                    $"Can't instantiate abstract class {Name} " +
                    $"without an implementation for abstract {methodWord} '{joinedMethods}'"
                );
            }
            else if (abstractMethodsAttr is PySet set)
            {
                if (set.Items.Count == 0)
                {
                    return; // Empty set - all methods implemented
                }

                // Same logic for PySet
                var methodNames = set.Items
                    .Select(item => ((PyString)item).Value)
                    .OrderBy(name => name)
                    .ToList();

                string joinedMethods = string.Join("', '", methodNames);
                string methodWord = methodNames.Count == 1 ? "method" : "methods";

                throw PyTypeError.Create(
                    $"Can't instantiate abstract class {Name} " +
                    $"without an implementation for abstract {methodWord} '{joinedMethods}'"
                );
            }
        }

    }

    #endregion

    #region Class Instances

    /// <summary>
    /// 사용자 정의 클래스의 인스턴스
    /// </summary>
    public class PyClassInstance : PyObject
    {
        public PyClass InstanceType { get; }
        public Dictionary<string, PyObject> InstanceDict { get; }
        public PyObject[] ConstructorArgs { get; set; } // Store constructor arguments
        private PyFunction _customGetAttr;

        // CPython 3.12: Dict subclasses have internal dict storage
        private PyDict _dictStorage;

        // CPython 3.12: List subclasses have internal list storage
        private PyList _listStorage;

        // CPython 3.12: Cache the __dict__ wrapper for identity consistency
        private PyDict _dictCache;

        public PyClassInstance(PyClass instanceType)
        {
            InstanceType = instanceType;
            InstanceDict = new Dictionary<string, PyObject>();
            ConstructorArgs = new PyObject[0]; // Default empty args

            // __getattr__ 메서드가 있는지 확인
            if (instanceType.ClassDict.ContainsKey("__getattr__"))
            {
                _customGetAttr = instanceType.ClassDict["__getattr__"] as PyFunction;
            }

            // CPython 3.12: If this is a dict subclass, create internal dict storage
            if (IsDictSubclass())
            {
                _dictStorage = new PyDict();
            }

            // CPython 3.12: If this is a list subclass, create internal list storage
            if (IsListSubclass())
            {
                _listStorage = new PyList();
            }
        }

        public bool IsDictSubclass()
        {
            // Check if any base type is dict
            return InstanceType.BaseTypes.Any(bt => bt == PyType.DictType);
        }

        public bool IsListSubclass()
        {
            // Check if any base type is list
            return InstanceType.BaseTypes.Any(bt => bt == PyType.ListType);
        }

        // CPython 3.12: Provide access to internal dict storage for dict subclasses
        public PyDict GetDictStorage()
        {
            return _dictStorage;
        }

        // CPython 3.12: Provide access to internal list storage for list subclasses
        public PyList GetListStorage()
        {
            return _listStorage;
        }

        public override string ToString()
        {
            // Debugging only: Simple C# representation
            return $"<{InstanceType.Name} instance at 0x{GetHashCode():x}>";
        }

        private bool IsExceptionClass()
        {
            // Check if this class inherits from Exception
            return InstanceType.Name.EndsWith("Error") ||
                   InstanceType.Name == "Exception" ||
                   InstanceType.BaseTypes.Any(bt => bt.Name == "Exception" || bt.Name == "BaseException");
        }

        public override PyType GetPyType() => InstanceType;
        public override string GetTypeName() => InstanceType.Name;

        // CPython 3.12: Objects/dictobject.c:2490-2523 (dict_subscript)
        // CPython 3.12: Objects/abstract.c:171-201 (PyObject_GetItem)
        //
        // Same pattern as SetItem:
        // 1. Check ONLY user-defined __getitem__ (PyClass.ClassDict)
        // 2. If found, call it
        // 3. If NOT found, use direct storage (_dictStorage for dict subclasses)
        // 4. NEVER call built-in descriptors from PyType.TypeDict here
        public override PyObject GetItem(PyObject key)
        {
            #if DEBUG
            var keyStr = key is PyString ps ? ps.Value : key?.ToString() ?? "null";
            if (InstanceType.Name == "_EnumDict" && (keyStr == "STRICT" || keyStr == "CONFORM" || keyStr == "EJECT" || keyStr == "KEEP"))
            {
                Console.WriteLine($"[DEBUG-GETITEM] _EnumDict.GetItem('{keyStr}') called, _dictStorage != null: {_dictStorage != null}");
                if (_dictStorage != null && _dictStorage.InternalDict.TryGetValue(key, out var stored))
                {
                    Console.WriteLine($"[DEBUG-GETITEM]   _dictStorage contains '{keyStr}': {stored}, type={stored?.GetType().Name}");
                }
            }
            #endif

            // CPython 3.12: Check for user-defined __getitem__ in MRO
            // Only check PyClass.ClassDict, NOT PyType.TypeDict
            foreach (var mroType in InstanceType.MRO)
            {
                if (mroType is PyClass pyClass && pyClass.ClassDict.TryGetValue("__getitem__", out PyObject getItemMethod))
                {
                    // Found user-defined __getitem__
                    if (getItemMethod is PyFunction func)
                    {
                        var boundMethod = new PyMethod(this, func);
                        return boundMethod.Call(new PyObject[] { key }, null);
                    }
                    else if (getItemMethod.IsCallable())
                    {
                        return getItemMethod.Call(new PyObject[] { this, key }, null);
                    }
                    break;
                }
            }

            // CPython 3.12: Fall back to direct storage access
            // For dict subclasses: read from _dictStorage (like PyDict_GetItem)
            if (_dictStorage != null)
            {
                var result = _dictStorage.GetItem(key);
                #if DEBUG
                if (InstanceType.Name == "_EnumDict" && (keyStr == "STRICT" || keyStr == "CONFORM" || keyStr == "EJECT" || keyStr == "KEEP"))
                {
                    Console.WriteLine($"[DEBUG-GETITEM]   Returning from _dictStorage: {result}, type={result?.GetType().Name}");
                }
                #endif
                return result;
            }

            // No __getitem__ and not a dict subclass
            return base.GetItem(key);
        }

        // CPython 3.12: Objects/dictobject.c:1879-1889 (PyDict_SetItem)
        // CPython 3.12: Objects/abstract.c:203-234 (PyObject_SetItem)
        // CPython 3.12: Lib/enum.py:509 (super().__setitem__)
        //
        // In CPython, PyObject_SetItem directly calls tp_as_mapping->mp_ass_subscript,
        // which for dict calls PyDict_SetItem (dictobject.c:2524-2530).
        // PyDict_SetItem checks PyDict_Check and writes to internal storage.
        //
        // For dict subclasses, CPython creates actual PyDictObject instances,
        // so mp_ass_subscript always gets the real dict's function pointer.
        // User-defined __setitem__ is only checked at VM level (STORE_SUBSCR),
        // NOT at the type slot level.
        //
        // SharpPy equivalent:
        // 1. Check ONLY user-defined __setitem__ (PyClass.ClassDict)
        // 2. If found, call it and return (user code must call super().__setitem__ to store)
        // 3. If NOT found, use direct storage (_dictStorage for dict subclasses)
        // 4. NEVER call built-in descriptors from PyType.TypeDict here
        //    (they are already handled at VM level in STORE_SUBSCR)
        public override void SetItem(PyObject key, PyObject value)
        {
            // CPython 3.12 Objects/dictobject.c:1879-1889 (PyDict_SetItem):
            //   For dict subclasses with user-defined __setitem__:
            //   - Call user-defined __setitem__ ONLY
            //   - Internal storage is updated ONLY if user calls super().__setitem__()
            //   - If user doesn't call super().__setitem__(), storage is NOT updated
            //
            // This is critical for Lib/enum.py:509 where _EnumDict.__setitem__
            // processes auto() values before calling super().__setitem__()

            // Check for user-defined __setitem__ in MRO
            // Only check PyClass.ClassDict, NOT PyType.TypeDict
            foreach (var mroType in InstanceType.MRO)
            {
                if (mroType is PyClass pyClass && pyClass.ClassDict.TryGetValue("__setitem__", out var setitemMethod))
                {
                    // Found user-defined __setitem__ - call it and return
                    // Storage will be updated only if user code calls super().__setitem__()
                    if (setitemMethod is PyFunction func)
                    {
                        var boundMethod = new PyMethod(this, func);
                        boundMethod.Call(new PyObject[] { key, value }, null);
                    }
                    else if (setitemMethod.IsCallable())
                    {
                        setitemMethod.Call(new PyObject[] { this, key, value }, null);
                    }
                    return;  // User __setitem__ found and called, we're done
                }
            }

            // No user-defined __setitem__ found
            // For dict subclasses: write directly to internal storage
            if (_dictStorage != null)
            {
                _dictStorage.SetItem(key, value);
                return;
            }

            // For other types: use base implementation
            base.SetItem(key, value);
        }

        // CPython 3.12: Dict subclasses support 'in' operator
        public override PyBool Contains(PyObject item)
        {
            if (_dictStorage != null)
            {
                return _dictStorage.Contains(item);
            }
            return base.Contains(item);
        }

        // CPython 3.12: Objects/abstract.c:2859-2864 (PyObject_GetIter)
        // Support iteration via __iter__ method or dict storage
        public override PyObject GetIterator()
        {
            // Try to call __iter__ method if it exists (takes precedence)
            try
            {
                // Check instance dict first
                if (InstanceDict.ContainsKey("__iter__"))
                {
                    var iterMethod = InstanceDict["__iter__"];
                    return iterMethod.Call(new PyObject[0], null);
                }

                // Then check class hierarchy (both PyClass and PyType)
                foreach (var mroType in InstanceType.MRO)
                {
                    // CPython 3.12: Check PyClass (user-defined classes)
                    if (mroType is PyClass customClass && customClass.ClassDict.ContainsKey("__iter__"))
                    {
                        var method = customClass.ClassDict["__iter__"];
                        if (method is PyFunction func)
                        {
                            // Bind to instance
                            var boundMethod = new PyMethod(this, func);
                            return boundMethod.Call(new PyObject[0], null);
                        }
                        break;
                    }
                    // CPython 3.12: Check PyType (builtin types like list, dict)
                    // This is critical for list subclasses that inherit __iter__ from list
                    else if (mroType.TypeDict.TryGetValue("__iter__", out var method))
                    {
                        // Handle PyMethodDescriptor from builtin types
                        if (method is PyMethodDescriptor descriptor)
                        {
                            // Call the descriptor with self as first argument
                            return descriptor.Call(new PyObject[] { this }, null);
                        }
                        else if (method is PyFunction func)
                        {
                            // Bind to instance
                            var boundMethod = new PyMethod(this, func);
                            return boundMethod.Call(new PyObject[0], null);
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                throw PyTypeError.Create($"iter() returned non-iterator of type '{GetTypeName()}': {ex.Message}");
            }

            // Fall back to dict storage if this is a dict subclass
            if (_dictStorage != null)
            {
                return _dictStorage.GetIterator();
            }

            // No __iter__ method found
            return base.GetIterator();
        }

        /// <summary>
        /// CPython 3.12: Get next item by calling __next__ method
        /// </summary>
        public override PyObject Next()
        {
            // Try to call __next__ method if it exists
            // Check instance dict first
            if (InstanceDict.ContainsKey("__next__"))
            {
                var nextMethod = InstanceDict["__next__"];
                return nextMethod.Call(new PyObject[0], null);
            }

            // Then check class hierarchy
            foreach (var mroType in InstanceType.MRO)
            {
                if (mroType is PyClass customClass && customClass.ClassDict.ContainsKey("__next__"))
                {
                    var method = customClass.ClassDict["__next__"];
                    if (method is PyFunction func)
                    {
                        // Bind to instance
                        var boundMethod = new PyMethod(this, func);
                        return boundMethod.Call(new PyObject[0], null);
                    }
                    break;
                }
            }

            // No __next__ method found
            return base.Next();
        }

        /// <summary>
        /// CPython 3.12: Helper to call a magic method if it exists
        /// Searches instance dict first, then class MRO
        /// </summary>
        private PyObject CallMagicMethod(string methodName, params PyObject[] args)
        {
            // Check instance dict first
            if (InstanceDict.ContainsKey(methodName))
            {
                var method = InstanceDict[methodName];
                return method.Call(args, null);
            }

            // Then check class hierarchy (MRO)
            foreach (var mroType in InstanceType.MRO)
            {
                if (mroType is PyClass pyClass && pyClass.ClassDict.TryGetValue(methodName, out PyObject method))
                {
                    // Bind method to this instance and call
                    if (method is PyFunction func)
                    {
                        var boundMethod = new PyMethod(this, func);
                        return boundMethod.Call(args, null);
                    }
                    else if (method.IsCallable())
                    {
                        // For non-function callables, pass self as first argument
                        var argsWithSelf = new PyObject[args.Length + 1];
                        argsWithSelf[0] = this;
                        Array.Copy(args, 0, argsWithSelf, 1, args.Length);
                        return method.Call(argsWithSelf, null);
                    }
                    break;
                }
            }

            return null; // Method not found
        }

        /// <summary>
        /// CPython 3.12: Binary operation with magic method support
        /// Implements the protocol: try __add__, then __radd__ on other
        /// </summary>
        private PyObject BinaryOpWithMagicMethod(PyObject other, string methodName, string reflectedMethodName)
        {
            // Try left operand's method first (e.g., self.__add__(other))
            var result = CallMagicMethod(methodName, other);
            if (result != null)
            {
                return result;
            }

            // Try right operand's reflected method (e.g., other.__radd__(self))
            if (other is PyClassInstance otherInstance)
            {
                result = otherInstance.CallMagicMethod(reflectedMethodName, this);
                if (result != null)
                {
                    return result;
                }
            }

            // No magic method found - fall back to base implementation (will throw TypeError)
            return null;
        }

        // CPython 3.12: Magic method overrides for binary operations
        public override PyObject Add(PyObject other)
        {
            var result = BinaryOpWithMagicMethod(other, "__add__", "__radd__");
            return result ?? base.Add(other);
        }

        public override PyObject Subtract(PyObject other)
        {
            var result = BinaryOpWithMagicMethod(other, "__sub__", "__rsub__");
            return result ?? base.Subtract(other);
        }

        public override PyObject Multiply(PyObject other)
        {
            var result = BinaryOpWithMagicMethod(other, "__mul__", "__rmul__");
            return result ?? base.Multiply(other);
        }

        public override PyObject Divide(PyObject other)
        {
            var result = BinaryOpWithMagicMethod(other, "__truediv__", "__rtruediv__");
            return result ?? base.Divide(other);
        }

        public override PyObject FloorDivide(PyObject other)
        {
            var result = BinaryOpWithMagicMethod(other, "__floordiv__", "__rfloordiv__");
            return result ?? base.FloorDivide(other);
        }

        public override PyObject Modulo(PyObject other)
        {
            var result = BinaryOpWithMagicMethod(other, "__mod__", "__rmod__");
            return result ?? base.Modulo(other);
        }

        public override PyObject Power(PyObject other)
        {
            var result = BinaryOpWithMagicMethod(other, "__pow__", "__rpow__");
            return result ?? base.Power(other);
        }

        public override PyObject LeftShift(PyObject other)
        {
            var result = BinaryOpWithMagicMethod(other, "__lshift__", "__rlshift__");
            return result ?? base.LeftShift(other);
        }

        public override PyObject RightShift(PyObject other)
        {
            var result = BinaryOpWithMagicMethod(other, "__rshift__", "__rrshift__");
            return result ?? base.RightShift(other);
        }

        public override PyObject BitwiseAnd(PyObject other)
        {
            var result = BinaryOpWithMagicMethod(other, "__and__", "__rand__");
            return result ?? base.BitwiseAnd(other);
        }

        public override PyObject BitwiseOr(PyObject other)
        {
            var result = BinaryOpWithMagicMethod(other, "__or__", "__ror__");
            return result ?? base.BitwiseOr(other);
        }

        public override PyObject BitwiseXor(PyObject other)
        {
            var result = BinaryOpWithMagicMethod(other, "__xor__", "__rxor__");
            return result ?? base.BitwiseXor(other);
        }

        protected override bool HasCustomGetAttr() => _customGetAttr != null;

        protected override PyObject CallGetAttr(string name)
        {
            if (_customGetAttr != null)
            {
                return _customGetAttr.Call(new PyObject[] { this, new PyString(name) }, null);
            }
            return null;
        }

        // Descriptor protocol helpers (CPython 3.12: Objects/descrobject.c:1021)

        /// <summary>
        /// Check if an object is a data descriptor
        /// CPython: PyDescr_IsData - checks if tp_descr_set != NULL
        /// Python level: has __set__ or __delete__ method
        /// </summary>
        internal static bool IsDataDescriptor(PyObject obj)
        {
            // C# descriptors implement IDescriptor
            if (obj is IDescriptor desc)
            {
                return desc.IsDataDescriptor();
            }

            // Python class instances: check for __set__ or __delete__ methods
            // CPython: Py_TYPE(ob)->tp_descr_set != NULL
            if (obj is PyClassInstance instance)
            {
                var objType = instance.InstanceType;
                // Use LookupInMRO to bypass __getattribute__ (like _PyType_Lookup)
                return objType.LookupInMRO("__set__") != null ||
                       objType.LookupInMRO("__delete__") != null;
            }

            return false;
        }

        /// <summary>
        /// Check if an object is a descriptor (has __get__)
        /// CPython: tp_descr_get != NULL
        /// </summary>
        internal static bool IsDescriptor(PyObject obj)
        {
            // C# descriptors implement IDescriptor
            if (obj is IDescriptor)
            {
                return true;
            }

            // Python class instances: check for __get__ method
            if (obj is PyClassInstance instance)
            {
                var objType = instance.InstanceType;
                return objType.LookupInMRO("__get__") != null;
            }

            return false;
        }

        /// <summary>
        /// Call descriptor's __get__ method
        /// CPython: tp_descr_get(descr, obj, (PyObject *)Py_TYPE(obj))
        /// </summary>
        internal static PyObject CallDescriptorGet(PyObject descriptor, PyObject instance, PyObject owner)
        {
            // C# descriptors implement IDescriptor
            if (descriptor is IDescriptor desc)
            {
                return desc.Get(instance, owner as PyType);
            }

            // Python class instances: call __get__ method
            if (descriptor is PyClassInstance descInstance)
            {
                var objType = descInstance.InstanceType;
                var getMethod = objType.LookupInMRO("__get__");

                if (getMethod != null)
                {
                    // CPython: When instance is NULL (class attribute access), pass None
                    var instanceArg = instance ?? PyNone.Instance;

                    // Apply descriptor protocol to __get__ itself (it might be a function)
                    if (getMethod is PyFunction func)
                    {
                        // Bind __get__ to the descriptor instance
                        var boundGet = new PyMethod(descriptor, func);
                        // Call: descriptor.__get__(instance, owner)
                        return boundGet.Call(new PyObject[] { instanceArg, owner }, null);
                    }
                    else if (getMethod.IsCallable())
                    {
                        // Call: __get__(descriptor, instance, owner)
                        return getMethod.Call(new PyObject[] { descriptor, instanceArg, owner }, null);
                    }
                }
            }

            // Not a descriptor, return as-is
            return descriptor;
        }

        /// <summary>
        /// Call descriptor's __set__ method
        /// CPython: tp_descr_set(descr, obj, value)
        /// Reference: Objects/object.c:1567-1570
        /// </summary>
        internal static void CallDescriptorSet(PyObject descriptor, PyObject instance, PyObject value)
        {
            // C# descriptors implement IDescriptor
            if (descriptor is IDescriptor desc)
            {
                desc.Set(instance, value);
                return;
            }

            // Python class instances: call __set__ method
            if (descriptor is PyClassInstance descInstance)
            {
                var objType = descInstance.InstanceType;
                var setMethod = objType.LookupInMRO("__set__");

                if (setMethod != null)
                {
                    // Apply descriptor protocol to __set__ itself (it might be a function)
                    if (setMethod is PyFunction func)
                    {
                        // Bind __set__ to the descriptor instance
                        var boundSet = new PyMethod(descriptor, func);
                        // Call: descriptor.__set__(instance, value)
                        boundSet.Call(new PyObject[] { instance, value }, null);
                        return;
                    }
                    else if (setMethod.IsCallable())
                    {
                        // Call: __set__(descriptor, instance, value)
                        setMethod.Call(new PyObject[] { descriptor, instance, value }, null);
                        return;
                    }
                }
            }

            // If we get here, it's a programming error (should have checked IsDataDescriptor first)
            throw PyAttributeError.Create("Descriptor does not have __set__ method");
        }

        // Special attributes

        /// <summary>
        /// Internal method: Generic attribute lookup WITHOUT checking for custom __getattribute__
        /// This is equivalent to CPython's PyObject_GenericGetAttr C function
        /// Used by object.__getattribute__ to avoid infinite recursion
        /// </summary>
        internal PyObject GetAttributeGeneric(string name)
        {
            #if DEBUG_LOG
            Console.WriteLine($"🔍 PyClassInstance.GetAttributeGeneric: {InstanceType.Name} instance.{name} (no custom __getattribute__)");
            #endif

            // 특별한 속성들 먼저 처리
            if (name == "__class__")
            {
                return InstanceType;
            }
            if (name == "__dict__")
            {
                // CPython 3.12: Return cached wrapper for identity consistency
                // obj.__dict__ is obj.__dict__ must be True
                if (_dictCache == null)
                {
                    _dictCache = new PyInstanceAttrDict(InstanceDict);
                }
                return _dictCache;
            }

            // CPython 3.12 descriptor protocol (without custom __getattribute__ check):
            // 1. 클래스 MRO에서 data descriptor 찾기 → Get() 호출
            // 2. 인스턴스 __dict__ 검색
            // 3. 클래스 MRO에서 non-data descriptor 또는 일반 attribute 찾기
            // 4. __getattr__ 시도
            // 5. AttributeError

            PyObject classAttribute = null;
            PyClass foundInClass = null;

            // 1. Data descriptor 찾기
            // CPython 3.12: Objects/object.c:1440-1453
            // _PyType_Lookup(tp, name) searches MRO once for both PyClass and PyType
            foreach (var mroType in InstanceType.MRO)
            {
                PyObject foundValue = null;

                // Check PyClass first (user-defined classes)
                if (mroType is PyClass pyClass && pyClass.ClassDict.TryGetValue(name, out PyObject classValue))
                {
                    foundValue = classValue;
                    #if DEBUG_LOG
                    Console.WriteLine($"   → Found '{name}' in PyClass {pyClass.Name}: {classValue?.GetType().Name}");
                    #endif
                }
                // Check PyType (built-in types like dict, list, etc.)
                else if (mroType is PyType pyType && !(mroType is PyClass))
                {
                    var typeAttr = SharpPy.PyClass.GetTypeAttribute(pyType, name);
                    if (typeAttr != null)
                    {
                        foundValue = typeAttr;
                        #if DEBUG_LOG
                        Console.WriteLine($"   → Found '{name}' in PyType {pyType.Name}: {typeAttr?.GetType().Name}");
                        #endif
                    }
                }

                if (foundValue != null)
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"   → IsDataDescriptor: {IsDataDescriptor(foundValue)}");
                    Console.WriteLine($"   → IsDescriptor: {IsDescriptor(foundValue)}");
                    #endif

                    // Check if it's a data descriptor (has __set__ or __delete__)
                    if (IsDataDescriptor(foundValue))
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"   → Calling __get__ for data descriptor");
                        #endif
                        return CallDescriptorGet(foundValue, this, InstanceType);
                    }

                    if (classAttribute == null)
                    {
                        classAttribute = foundValue;
                        foundInClass = mroType is PyClass pc ? pc : null;
                    }
                    break;
                }
            }

            // 2. 인스턴스 __dict__ 검색
            if (InstanceDict.TryGetValue(name, out PyObject instanceValue))
            {
                return instanceValue;
            }

            // 3. 클래스 attribute 처리 (non-data descriptor 포함)
            // CPython 3.12: Objects/object.c:1507-1520
            if (classAttribute != null)
            {
                // Check if it's a descriptor (has __get__)
                if (IsDescriptor(classAttribute))
                {
                    return CallDescriptorGet(classAttribute, this, InstanceType);
                }
                // PyBuiltinMethod: bind to instance (e.g., dict.items)
                else if (classAttribute is PyBuiltinMethod builtinMethod)
                {
                    return builtinMethod.Get(this, InstanceType);
                }
                // PyBuiltinFunction: bind to instance (e.g., dict methods from GetTypeAttribute)
                else if (classAttribute is PyBuiltinFunction builtinFunction)
                {
                    return new PyBuiltinBoundMethod(this, builtinFunction);
                }
                // PyFunction: bind to instance (user-defined methods)
                else if (classAttribute is PyFunction func)
                {
                    return new PyMethod(this, func);
                }
                // Regular class attribute (not a function/descriptor)
                return classAttribute;
            }

            // 5. __getattr__ 시도
            if (HasCustomGetAttr())
            {
                var customResult = CallGetAttr(name);
                if (customResult != null)
                {
                    return customResult;
                }
            }

            // 6. AttributeError
            throw PyAttributeError.Create($"'{InstanceType.Name}' object has no attribute '{name}'");
        }

        public override PyObject GetAttribute(string name)
        {
            #if DEBUG_LOG
            Console.WriteLine($"🔍 PyClassInstance.GetAttribute: {InstanceType.Name} instance.{name}");
            #endif

            // CPython 3.12: Check for custom __getattribute__ FIRST
            // (typeobject.c:8867 - _Py_slot_tp_getattr_hook)
            PyObject customGetAttr = null;
            foreach (var mroType in InstanceType.MRO)
            {
                if (mroType is PyClass pyClass && pyClass.ClassDict.TryGetValue("__getattribute__", out customGetAttr))
                {
                    break;
                }
                if (mroType.TypeDict != null && mroType.TypeDict.TryGetValue("__getattribute__", out customGetAttr))
                {
                    // Check if it's NOT the default object.__getattribute__
                    // (CPython: check if d_wrapped == PyObject_GenericGetAttr)
                    if (mroType == PyType.ObjectType)
                    {
                        // This is the default object.__getattribute__, continue with normal logic
                        customGetAttr = null;
                    }
                    break;
                }
            }

            // If custom __getattribute__ found, call it directly
            if (customGetAttr != null)
            {
                #if DEBUG_LOG
                Console.WriteLine($"   🔍 calling custom __getattribute__");
                #endif
                return customGetAttr.Call(new PyObject[] { this, new PyString(name) }, null);
            }

            // Use GetAttributeGeneric for standard attribute lookup
            // This handles the full CPython 3.12 descriptor protocol
            return GetAttributeGeneric(name);
        }

        public override void SetAttribute(string name, PyObject value)
        {
            #if DEBUG_LOG
            Console.WriteLine($"🔧 PyClassInstance.SetAttribute: {InstanceType.Name} instance.{name} = {value}");
            #endif

            // CPython 3.12 descriptor protocol:
            // Reference: Objects/object.c:1563-1572 (_PyObject_GenericSetAttrWithDict)
            // 1. 클래스 MRO에서 attribute 찾기
            // 2. data descriptor라면 descriptor.__set__() 호출
            // 3. 아니라면 instance.__dict__[name] = value

            // 1. 클래스 MRO에서 descriptor 찾기
            foreach (var mroType in InstanceType.MRO)
            {
                if (mroType is PyClass pyClass && pyClass.ClassDict.TryGetValue(name, out PyObject classValue))
                {
                    // 2. data descriptor 확인 및 __set__ 호출
                    if (IsDataDescriptor(classValue))
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"   → found data descriptor in {mroType.Name}, calling __set__()");
                        #endif
                        CallDescriptorSet(classValue, this, value);
                        #if DEBUG_LOG
                        Console.WriteLine($"   ✅ descriptor __set__() completed");
                        #endif
                        return;
                    }
                    // descriptor가 아니거나 non-data descriptor라면 계속 진행
                    break;
                }
            }

            // 3. 인스턴스 __dict__에 저장
            InstanceDict[name] = value;

            #if DEBUG_LOG
            Console.WriteLine($"   → stored in instance dict (now {InstanceDict.Count} items)");
            #endif
        }

        public override PyString ToRepr()
        {
            // CPython 3.12: Try to call __repr__ method if user defined it
            // Check instance dict and class hierarchy (not object's default)
            try
            {
                // First check instance dict
                if (InstanceDict.ContainsKey("__repr__"))
                {
                    var reprMethod = InstanceDict["__repr__"];
                    var result = reprMethod.Call(new PyObject[0], null);
                    if (result is PyString pyStr)
                    {
                        return pyStr;
                    }
                }

                // Then check class hierarchy (but not object's __repr__)
                foreach (var mroType in InstanceType.MRO)
                {
                    if (mroType is PyClass customClass && customClass.ClassDict.ContainsKey("__repr__"))
                    {
                        var method = customClass.ClassDict["__repr__"];
                        if (method is PyFunction func)
                        {
                            // Bind to instance
                            var boundMethod = new PyMethod(this, func);
                            var result = boundMethod.Call(new PyObject[0], null);
                            if (result is PyString pyStr)
                            {
                                return pyStr;
                            }
                        }
                        break;
                    }
                    // Stop before reaching object type to avoid default __repr__
                    if (mroType.Name == "object")
                    {
                        break;
                    }
                }
            }
            catch
            {
                // If __repr__ fails, fall back to default
            }

            // Default representation
            return new PyString($"<{GetTypeName()} object at 0x{GetHashCode():x}>");
        }

        public override PyString ToStr()
        {
            // CPython 3.12: Try to call __str__ method if user defined it
            try
            {
                // First check instance dict
                if (InstanceDict.ContainsKey("__str__"))
                {
                    var strMethod = InstanceDict["__str__"];
                    var result = strMethod.Call(new PyObject[0], null);
                    if (result is PyString pyStr)
                    {
                        return pyStr;
                    }
                }

                // Then check class hierarchy (but not object's __str__)
                foreach (var mroType in InstanceType.MRO)
                {
                    if (mroType is PyClass customClass && customClass.ClassDict.ContainsKey("__str__"))
                    {
                        var method = customClass.ClassDict["__str__"];
                        if (method is PyFunction func)
                        {
                            // Bind to instance
                            var boundMethod = new PyMethod(this, func);
                            var result = boundMethod.Call(new PyObject[0], null);
                            if (result is PyString pyStr)
                            {
                                return pyStr;
                            }
                        }
                        break;
                    }
                    // Stop before reaching object type
                    if (mroType.Name == "object")
                    {
                        break;
                    }
                }
            }
            catch
            {
                // If __str__ fails, fall back to __repr__
            }

            return ToRepr();
        }

        public override PyObject RichCompare(PyObject other, CompareOp op)
        {
            // CPython 3.12: Try to call __eq__, __ne__, __lt__, __le__, __gt__, __ge__ methods
            string methodName = op switch
            {
                CompareOp.EQ => "__eq__",
                CompareOp.NE => "__ne__",
                CompareOp.LT => "__lt__",
                CompareOp.LE => "__le__",
                CompareOp.GT => "__gt__",
                CompareOp.GE => "__ge__",
                _ => null
            };

            if (methodName != null)
            {
                try
                {
                    // Look for comparison method in class MRO
                    foreach (var mroType in InstanceType.MRO)
                    {
                        if (mroType is PyClass pyClass && pyClass.ClassDict.TryGetValue(methodName, out PyObject compareMethod))
                        {
                            // Found comparison method, call it with self and other
                            if (compareMethod is PyFunction func)
                            {
                                var boundMethod = new PyMethod(this, func);
                                var result = boundMethod.Call(new PyObject[] { other }, null);

                                // CPython 3.12: If result is NotImplemented, fall back to default
                                if (result == PyNotImplemented.Instance)
                                {
                                    break;
                                }

                                return result;
                            }
                            else if (compareMethod.IsCallable())
                            {
                                var result = compareMethod.Call(new PyObject[] { this, other }, null);

                                if (result == PyNotImplemented.Instance)
                                {
                                    break;
                                }

                                return result;
                            }
                            break;
                        }
                        // Stop before reaching object type to avoid default comparison
                        if (mroType.Name == "object")
                        {
                            break;
                        }
                    }
                }
                catch (PythonException)
                {
                    throw; // Re-throw Python exceptions
                }
            }

            // Fall back to base comparison (identity-based for EQ/NE)
            return base.RichCompare(other, op);
        }
    }

    #endregion

        /// <summary>
    /// Tuple subclass instance for user-defined classes that inherit from tuple
    /// CPython 3.12: Used for namedtuple and other tuple subclasses
    /// </summary>
    public class PyTupleSubclass : PyTuple
    {
        public PyClass InstanceType { get; }
        public Dictionary<string, PyObject> InstanceDict { get; }
        public PyObject[] ConstructorArgs { get; set; }

        public PyTupleSubclass(PyClass instanceType, PyObject[] items) : base(items)
        {
            InstanceType = instanceType;
            InstanceDict = new Dictionary<string, PyObject>();
        }

        public override PyType GetPyType() => InstanceType;

        public override string GetTypeName() => InstanceType.Name;

        // Override IsInstance to properly check MRO for tuple subclasses
        // CPython 3.12: PyObject_IsInstance checks tp_mro
        // NOTE: This override is no longer needed since isinstance() in BuiltinsModule.cs
        // now properly handles MRO checking for PyType (builtin types) at lines 286-293.
        // Keeping this as documentation of the fix.
        public override bool IsInstance(PyType type)
        {
            // Check if the type is in our MRO
            bool mroCheck = InstanceType.IsSubclassOf(type);
            if (mroCheck)
                return true;

            // Also check C# inheritance (PyTupleSubclass is a PyTuple in C#)
            return base.IsInstance(type);
        }

        // Override GetAttribute to support user-defined descriptors and __dict__
        public override PyObject GetAttribute(string name)
        {
            // First check if there's a descriptor in the class
            PyObject classAttribute = null;
            foreach (var mroType in InstanceType.MRO)
            {
                if (mroType is PyClass pyClass && pyClass.ClassDict.TryGetValue(name, out PyObject classValue))
                {
                    classAttribute = classValue;
                    break;
                }
            }

            // If it's a descriptor, use it
            if (classAttribute is IDescriptor desc)
            {
                return desc.Get(this, InstanceType);
            }
            else if (classAttribute is PyFunction function)
            {
                return new PyMethod(this, function);
            }

            // Check instance __dict__
            if (InstanceDict.TryGetValue(name, out PyObject instanceValue))
            {
                return instanceValue;
            }

            // Return class attribute if found
            if (classAttribute != null)
            {
                return classAttribute;
            }

            // Fall back to base PyTuple behavior
            return base.GetAttribute(name);
        }

        // Override SetAttribute to support instance __dict__
        public override void SetAttribute(string name, PyObject value)
        {
            // Check if there's a data descriptor in the class
            foreach (var mroType in InstanceType.MRO)
            {
                if (mroType is PyClass pyClass && pyClass.ClassDict.TryGetValue(name, out PyObject classValue))
                {
                    if (classValue is IDescriptor desc && desc.IsDataDescriptor())
                    {
                        desc.Set(this, value);
                        return;
                    }
                    break;
                }
            }

            // Set in instance __dict__
            InstanceDict[name] = value;
        }
    }

#region Super Implementation

    /// <summary>
    /// Python의 super() 구현
    /// </summary>
    // PySuper moved to PyBuiltin.cs to follow CPython 3.12 structure

    #endregion
}