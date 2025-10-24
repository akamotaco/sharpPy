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
            // PyType.TypeType의 descriptor가 이미 초기화되었는지 확인
            if (PyType.TypeType.TypeDict.ContainsKey("mro")) return;

            var typeType = PyType.TypeType;

            // mro() method descriptor - CPython 3.12 호환
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

        public Dictionary<string, PyObject> ClassDict { get; }
        public List<PyObject>? TypeParams { get; set; } // PEP 695 __type_params__
        public PyClass? Metaclass { get; set; } // Metaclass information for type() calls

        public PyClass(string name, PyType[] baseTypes, Dictionary<string, PyObject> classDict = null, List<PyObject>? typeParams = null)
            : this(name, baseTypes, classDict, typeParams, null)
        {
        }

        public PyClass(string name, PyType[] baseTypes, Dictionary<string, PyObject> classDict, List<PyObject>? typeParams, string module)
            : base(name, baseTypes, module)
        {
            ClassDict = classDict ?? new Dictionary<string, PyObject>();
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
        }

        public new PyClassInstance CreateInstance(params PyObject[] args)
        {
            return CreateInstance(args, null);
        }

        public new PyClassInstance CreateInstance(PyObject[] args, PyDict kwargs)
        {
            var instance = new PyClassInstance(this);

            // Store constructor arguments for toString() behavior
            instance.ConstructorArgs = args;

            // CPython: __init__ lookup bypasses __getattribute__ (uses _PyType_Lookup)
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
            // Search through MRO (Method Resolution Order)
            foreach (var mroType in MRO)
            {
                // For PyClass: check ClassDict
                if (mroType is PyClass pyClass)
                {
                    if (pyClass.ClassDict.TryGetValue(name, out PyObject value))
                    {
                        return value;
                    }
                }
                // For PyType: check TypeDict
                else if (mroType is PyType pyType)
                {
                    if (pyType.TypeDict != null && pyType.TypeDict.TryGetValue(name, out PyObject value))
                    {
                        return value;
                    }
                }
            }
            return null;
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
            // Create generic type with type arguments
            var typeArgs = new List<PyObject>();
            
            if (key is PyTuple tuple)
            {
                typeArgs.AddRange(tuple.Items);
            }
            else
            {
                typeArgs.Add(key);
            }

            return new PyGenericType($"{Name}[{key}]", this, typeArgs);
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
            #if DEBUG_LOG
            Console.WriteLine($"🔍 PyClass.GetIterator() called for {Name}");
            Console.WriteLine($"   Metaclass: {Metaclass?.Name}");
            Console.WriteLine($"   Metaclass type: {Metaclass?.GetType().Name}");
            Console.WriteLine($"   Metaclass is PyClass: {Metaclass is PyClass}");
            #endif

            // Check if metaclass has __iter__ method
            if (Metaclass != null)
            {
                #if DEBUG_LOG
                Console.WriteLine($"   → checking metaclass ClassDict for __iter__");
                if (Metaclass is PyClass metaPyClassDebug)
                {
                    Console.WriteLine($"   → metaclass ClassDict count: {metaPyClassDebug.ClassDict.Count}");
                    Console.WriteLine($"   → metaclass ClassDict keys: {string.Join(", ", metaPyClassDebug.ClassDict.Keys.Take(10))}");
                }
                #endif

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
                        // Descriptor 처리 (CPython 3.12: Objects/typeobject.c:4830)
                        // For class attribute access, only __get__ is checked (not data vs non-data)
                        if (PyClassInstance.IsDescriptor(value))
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"   🔧 calling descriptor.__get__(null, {Name}) for '{name}'");
                            #endif
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
            ClassDict[name] = value;
        }

        // CPython 3.12: Override DelAttribute for type objects
        protected override void PyDelAttribute(string name)
        {
            if (ClassDict.ContainsKey(name))
            {
                ClassDict.Remove(name);
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
                    // Helper to get PyList from self (works for both PyList and list subclasses)
                    static PyList GetListStorage(PyObject self)
                    {
                        if (self is PyList list)
                            return list;

                        if (self is PyClassInstance instance && instance.InstanceType.BaseTypes.Any(bt => bt == PyType.ListType))
                        {
                            // List subclass - get its storage
                            // For now, we don't have internal list storage for subclasses
                            // This would need to be implemented similar to dict subclasses
                            throw PyTypeError.Create($"list subclass storage not yet implemented");
                        }

                        throw PyTypeError.Create($"descriptor requires a 'list' object but received a '{self.GetTypeName()}'");
                    }

                    return name switch
                    {
                        // CPython 3.12: __getitem__ wrapper descriptor (sq_item slot)
                        "__getitem__" => new PyWrapperDescriptor(
                            "__getitem__",
                            PyType.ListType,
                            (self, args, kwargs) =>
                            {
                                if (args.Length != 1)
                                    throw PyTypeError.Create($"__getitem__() takes exactly 1 argument ({args.Length} given)");

                                var key = args[0];
                                // CPython 3.12: Call self.GetItem() which allows subclass override
                                return self.GetItem(key);
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
                                // This wrapper descriptor represents the C-level slot, not Python method
                                PyList list;
                                if (self is PyList l)
                                {
                                    list = l;
                                }
                                else
                                {
                                    throw PyTypeError.Create($"descriptor '__setitem__' for 'list' objects doesn't apply to a '{self.GetTypeName()}' object");
                                }

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
                                    // Handle slice deletion
                                    throw PyNotImplementedError.Create("list slice deletion not yet implemented");
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
        /// </summary>
        private void CheckAbstractMethods()
        {
            var abstractMethods = new List<string>();

            // Check all methods in MRO for abstract methods
            foreach (var mroType in MRO)
            {
                if (mroType is PyClass mroClass)
                {
                    foreach (var kvp in mroClass.ClassDict)
                    {
                        string methodName = kvp.Key;
                        PyObject methodValue = kvp.Value;

                        // Check if method is marked as abstract
                        if (IsAbstractMethod(methodValue))
                        {
                            // Check if this method is implemented in this class or any of its ancestors
                            if (!IsMethodImplemented(methodName))
                            {
                                abstractMethods.Add(methodName);
                            }
                        }
                    }
                }
            }

            // If any abstract methods are found, throw TypeError
            if (abstractMethods.Count > 0)
            {
                abstractMethods.Sort(); // CPython sorts the method names
                string methodList = abstractMethods.Count == 1
                    ? $"abstract method '{abstractMethods[0]}'"
                    : $"abstract methods {string.Join(", ", abstractMethods.Select(m => $"'{m}'"))}";
                throw PyTypeError.Create($"Can't instantiate abstract class {Name} without an implementation for {methodList}");
            }
        }

        /// <summary>
        /// Check if a method object is marked as abstract
        /// </summary>
        private bool IsAbstractMethod(PyObject method)
        {
            if (method is PyFunction func)
            {
                try
                {
                    if (func.Attributes.TryGetValue("__isabstractmethod__", out PyObject abstractAttr))
                    {
                        return abstractAttr.PyBoolValue();
                    }
                }
                catch
                {
                    // If we can't check, assume not abstract
                }
            }
            return false;
        }

        /// <summary>
        /// Check if a method is implemented (not abstract) in this class
        /// </summary>
        private bool IsMethodImplemented(string methodName)
        {
            // Look for the method in this class's dict
            if (ClassDict.TryGetValue(methodName, out PyObject method))
            {
                // If we have the method and it's not abstract, it's implemented
                return !IsAbstractMethod(method);
            }

            // Check if any base classes have a non-abstract implementation
            foreach (var baseType in BaseTypes)
            {
                if (baseType is PyClass baseClass)
                {
                    if (baseClass.ClassDict.TryGetValue(methodName, out PyObject baseMethod))
                    {
                        if (!IsAbstractMethod(baseMethod))
                        {
                            return true; // Found non-abstract implementation in base
                        }
                    }
                }
            }

            return false; // No non-abstract implementation found
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
        }

        public bool IsDictSubclass()
        {
            // Check if any base type is dict
            return InstanceType.BaseTypes.Any(bt => bt == PyType.DictType);
        }

        // CPython 3.12: Provide access to internal dict storage for dict subclasses
        public PyDict GetDictStorage()
        {
            return _dictStorage;
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

        // CPython 3.12: Dict subclasses use internal dict storage
        public override PyObject GetItem(PyObject key)
        {
            // CPython 3.12: First check if __getitem__ method is defined
            // This allows user-defined classes to implement subscript operator
            try
            {
                // Look for __getitem__ in class MRO
                foreach (var mroType in InstanceType.MRO)
                {
                    if (mroType is PyClass pyClass && pyClass.ClassDict.TryGetValue("__getitem__", out PyObject getItemMethod))
                    {
                        // Found __getitem__, call it with self and key
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
            }
            catch (PythonException)
            {
                throw; // Re-throw Python exceptions
            }

            // Fall back to dict storage for dict subclasses
            if (_dictStorage != null)
            {
                return _dictStorage.GetItem(key);
            }

            // No __getitem__ and not a dict subclass
            return base.GetItem(key);
        }

        public override void SetItem(PyObject key, PyObject value)
        {
            // CPython 3.12: Check for user-defined __setitem__ first
            // This allows dict subclasses to override __setitem__ behavior
            foreach (var mroType in InstanceType.MRO)
            {
                if (mroType is PyClass pyClass && pyClass.ClassDict.TryGetValue("__setitem__", out PyObject setitemMethod))
                {
                    // Found __setitem__, call it with self, key, and value
                    if (setitemMethod is PyFunction func)
                    {
                        var boundMethod = new PyMethod(this, func);
                        boundMethod.Call(new PyObject[] { key, value }, null);
                        return;
                    }
                    else if (setitemMethod.IsCallable())
                    {
                        setitemMethod.Call(new PyObject[] { this, key, value }, null);
                        return;
                    }
                    break;
                }
            }

            // Fall back to direct storage access for dict subclasses
            if (_dictStorage != null)
            {
                _dictStorage.SetItem(key, value);
                return;
            }
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

        // CPython 3.12: Dict subclasses support iteration
        public override PyObject GetIterator()
        {
            if (_dictStorage != null)
            {
                return _dictStorage.GetIterator();
            }
            return base.GetIterator();
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
                return new PyDict(InstanceDict);
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
            foreach (var mroType in InstanceType.MRO)
            {
                if (mroType is PyClass pyClass && pyClass.ClassDict.TryGetValue(name, out PyObject classValue))
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"   → Found '{name}' in {pyClass.Name}: {classValue?.GetType().Name}");
                    Console.WriteLine($"   → IsDataDescriptor: {IsDataDescriptor(classValue)}");
                    Console.WriteLine($"   → IsDescriptor: {IsDescriptor(classValue)}");
                    #endif

                    // Check if it's a data descriptor (has __set__ or __delete__)
                    if (IsDataDescriptor(classValue))
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"   → Calling __get__ for data descriptor");
                        #endif
                        return CallDescriptorGet(classValue, this, InstanceType);
                    }

                    if (classAttribute == null)
                    {
                        classAttribute = classValue;
                        foundInClass = pyClass;
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
                else if (classAttribute is PyFunction func)
                {
                    return new PyMethod(this, func);
                }
                return classAttribute;
            }

            // 4. 내장 속성 확인
            foreach (var mroType in InstanceType.MRO)
            {
                if (mroType is PyType pyType && !(mroType is PyClass))
                {
                    var typeAttr = SharpPy.PyClass.GetTypeAttribute(pyType, name);
                    if (typeAttr != null)
                    {
                        if (typeAttr is PyBuiltinMethod builtinMethod)
                        {
                            return builtinMethod.Get(this, InstanceType);
                        }
                        if (typeAttr is PyBuiltinFunction builtinFunction)
                        {
                            return new PyBuiltinBoundMethod(this, builtinFunction);
                        }
                        if (typeAttr is PyFunction func)
                        {
                            return new PyMethod(this, func);
                        }
                        return typeAttr;
                    }
                }
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

    #region Super Implementation

    /// <summary>
    /// Python의 super() 구현
    /// </summary>
    // PySuper moved to PyBuiltin.cs to follow CPython 3.12 structure

    #endregion
}