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
            if (PyType.TypeType.Descriptors.Methods.ContainsKey("mro")) return;

            var typeType = PyType.TypeType;

            // mro() method descriptor - CPython 3.12 호환
            typeType.Descriptors.AddMethod("mro", new PyMethodDescriptor(
                "mro", typeType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create("mro() takes no arguments");
                    if (self is not PyType type)
                        throw PyTypeError.Create($"descriptor 'mro' requires a 'type' object but received a '{self.GetTypeName()}'");
                    return new PyList(type.MRO.Cast<PyObject>().ToList());
                },
                minArgs: 0, maxArgs: 0
            ));
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

            // __init__ 호출 (있다면)
            if (HasMethod("__init__"))
            {
                var init = instance.GetAttribute("__init__");
                if (init is PyMethod method)
                {
                    // PyMethod는 이미 self가 바인딩되어 있으므로 args만 전달
                    method.Call(args, kwargs);
                }
                else if (init is PyFunction function)
                {
                    // PyFunction은 self를 수동으로 추가해야 함
                    var allArgs = new PyObject[args.Length + 1];
                    allArgs[0] = instance;
                    Array.Copy(args, 0, allArgs, 1, args.Length);
                    function.Call(allArgs, kwargs);
                }
            }

            return instance;
        }

        public bool HasMethod(string name)
        {
            return MRO.OfType<PyClass>().Any(t => t.ClassDict.ContainsKey(name));
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
            
            switch (name)
            {
                case "__name__":
                    #if DEBUG_LOG
                    Console.WriteLine($"   → returning __name__ = {Name}");
                    #endif
                    return new PyString(Name);
                case "__bases__":
                    #if DEBUG_LOG
                    Console.WriteLine($"   → returning __bases__ (count: {BaseTypes.Length})");
                    #endif
                    return new PyTuple(BaseTypes);
                case "__mro__":
                    #if DEBUG_LOG
                    Console.WriteLine($"   → returning __mro__ (count: {MRO.Count})");
                    #endif
                    return new PyTuple(MRO.Cast<PyObject>().ToArray());
                case "__dict__":
                    #if DEBUG_LOG
                    Console.WriteLine($"   → returning __dict__ (count: {ClassDict.Count})");
                    #endif
                    return new PyDict(ClassDict);
                case "__call__":
                    #if DEBUG_LOG
                    Console.WriteLine($"   → returning self for __call__");
                    #endif
                    return this; // 클래스 자체가 __call__
                case "__module__":
                    #if DEBUG_LOG
                    Console.WriteLine($"   → returning __module__ = __main__");
                    #endif
                    return new PyString("__main__"); // CPython 호환성을 위해 __main__ 반환
                default:
                    #if DEBUG_LOG
                    Console.WriteLine($"   → searching for '{name}' in ClassDict ({ClassDict.Count} items)");
                    #endif

                    if (ClassDict.TryGetValue(name, out PyObject value))
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"   ✅ found '{name}' in ClassDict: {value?.GetType().Name}");
                        #endif
                        // Descriptor 처리
                        if (value is IDescriptor desc)
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"   🔧 calling descriptor.Get(null, {Name}) for '{name}'");
                            #endif
                            var result = desc.Get(null, this);
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
                                // Descriptor 처리
                                if (baseValue is IDescriptor baseDesc)
                                {
                                    #if DEBUG_LOG
                                    Console.WriteLine($"   🔧 calling descriptor.Get(null, {Name}) for '{name}' from MRO");
                                    #endif
                                    var result = baseDesc.Get(null, this);
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

        private bool IsDictSubclass()
        {
            // Check if any base type is dict
            return InstanceType.BaseTypes.Any(bt => bt == PyType.DictType);
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

        // Special attributes
        public override PyObject GetAttribute(string name)
        {
            #if DEBUG_LOG
            Console.WriteLine($"🔍 PyClassInstance.GetAttribute: {InstanceType.Name} instance.{name}");
            #endif

            // 특별한 속성들 먼저 처리
            if (name == "__class__")
            {
                #if DEBUG_LOG
                Console.WriteLine($"   → returning __class__ = {InstanceType.Name}");
                #endif
                return InstanceType;
            }
            if (name == "__dict__")
            {
                #if DEBUG_LOG
                Console.WriteLine($"   → returning instance __dict__ (count: {InstanceDict.Count})");
                #endif
                return new PyDict(InstanceDict);
            }

            // CPython 3.12 descriptor protocol:
            // 1. 클래스 MRO에서 data descriptor 찾기 → Get() 호출
            // 2. 인스턴스 __dict__ 검색
            // 3. 클래스 MRO에서 non-data descriptor 또는 일반 attribute 찾기
            // 4. __getattr__ 시도
            // 5. AttributeError

            // 1. 클래스 MRO에서 data descriptor 찾기
            #if DEBUG_LOG
            Console.WriteLine($"   → checking for data descriptors in class MRO");
            #endif

            PyObject classAttribute = null;
            PyClass foundInClass = null;

            foreach (var mroType in InstanceType.MRO)
            {
                if (mroType is PyClass pyClass && pyClass.ClassDict.TryGetValue(name, out PyObject classValue))
                {
                    // Data descriptor인 경우 즉시 Get() 호출
                    if (classValue is IDescriptor desc && desc.IsDataDescriptor())
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"   ✅ found data descriptor '{name}' in {mroType.Name}, calling Get()");
                        #endif
                        var result = desc.Get(this, InstanceType);
                        #if DEBUG_LOG
                        Console.WriteLine($"   → descriptor returned: {result?.GetType().Name}");
                        #endif
                        return result;
                    }

                    // Data descriptor가 아니면 일단 저장해두고 계속 진행
                    if (classAttribute == null)
                    {
                        classAttribute = classValue;
                        foundInClass = pyClass;
                        #if DEBUG_LOG
                        Console.WriteLine($"   → found non-data attribute '{name}' in {mroType.Name}, checking instance dict first");
                        #endif
                    }
                    break;
                }
            }

            // 2. 인스턴스 __dict__ 검색
            #if DEBUG_LOG
            Console.WriteLine($"   → checking instance dict (count: {InstanceDict.Count})");
            #endif
            if (InstanceDict.TryGetValue(name, out PyObject instanceValue))
            {
                #if DEBUG_LOG
                Console.WriteLine($"   ✅ found '{name}' in instance dict: {instanceValue?.GetType().Name}");
                #endif
                return instanceValue;
            }

            // 3. 클래스 attribute 처리 (non-data descriptor 포함)
            if (classAttribute != null)
            {
                #if DEBUG_LOG
                Console.WriteLine($"   → processing class attribute '{name}' from {foundInClass.Name}");
                #endif

                // Non-data descriptor 처리
                if (classAttribute is IDescriptor desc)
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"   🔧 calling non-data descriptor.Get()");
                    #endif
                    var result = desc.Get(this, InstanceType);
                    #if DEBUG_LOG
                    Console.WriteLine($"   → descriptor returned: {result?.GetType().Name}");
                    #endif
                    return result;
                }
                // 함수를 bound method로 변환
                else if (classAttribute is PyFunction func)
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"   🔧 converting function to bound method");
                    #endif
                    return new PyMethod(this, func);
                }

                #if DEBUG_LOG
                Console.WriteLine($"   ✅ returning class attribute: {classAttribute?.GetType().Name}");
                #endif
                return classAttribute;
            }

            // PyType의 내장 속성들도 확인 (예: object 클래스의 메서드들)
            #if DEBUG_LOG
            Console.WriteLine($"   → checking builtin attributes in MRO");
            #endif
            foreach (var mroType in InstanceType.MRO)
            {
                // CPython 3.12: PyType의 경우 PyClass.GetTypeAttribute() 사용 (재귀 방지)
                if (mroType is PyType pyType && !(mroType is PyClass))
                {
                    var typeAttr = SharpPy.PyClass.GetTypeAttribute(pyType, name);
                    if (typeAttr != null)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"   ✅ found builtin attribute '{name}' in PyType {mroType.Name}: {typeAttr?.GetType().Name}");
                        #endif

                        // CPython 3.12: PyBuiltinMethod는 descriptor이므로 Get() 호출
                        if (typeAttr is PyBuiltinMethod builtinMethod)
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"   🔧 calling PyBuiltinMethod.Get() for binding");
                            #endif
                            var bound = builtinMethod.Get(this, InstanceType);
                            #if DEBUG_LOG
                            Console.WriteLine($"   → Get() returned: {bound?.GetType().Name}");
                            #endif
                            return bound;
                        }
                        // CPython 3.12: PyBuiltinFunction도 bound method로 변환
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

            #if DEBUG_LOG
            Console.WriteLine($"   ❌ attribute '{name}' not found");
            #endif

            // 4. __getattr__ 커스텀 핸들러 호출 (있다면)
            if (HasCustomGetAttr())
            {
                #if DEBUG_LOG
                Console.WriteLine($"   → trying custom __getattr__");
                #endif
                var customResult = CallGetAttr(name);
                if (customResult != null)
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"   ✅ custom __getattr__ returned: {customResult?.GetType().Name}");
                    #endif
                    return customResult;
                }
            }

            // 5. 기본 처리 (AttributeError)
            #if DEBUG_LOG
            Console.WriteLine($"   → falling back to base.GetAttribute");
            #endif
            return base.GetAttribute(name);
        }

        public override void SetAttribute(string name, PyObject value)
        {
            #if DEBUG_LOG
            Console.WriteLine($"🔧 PyClassInstance.SetAttribute: {InstanceType.Name} instance.{name} = {value}");
            #endif

            // CPython 3.12 descriptor protocol:
            // 1. 클래스 MRO에서 attribute 찾기
            // 2. data descriptor라면 descriptor.Set() 호출
            // 3. 아니라면 instance.__dict__[name] = value

            // 1. 클래스 MRO에서 descriptor 찾기
            foreach (var mroType in InstanceType.MRO)
            {
                if (mroType is PyClass pyClass && pyClass.ClassDict.TryGetValue(name, out PyObject classValue))
                {
                    // 2. data descriptor 확인 및 Set 호출
                    if (classValue is IDescriptor desc && desc.IsDataDescriptor())
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"   → found data descriptor in {mroType.Name}, calling Set()");
                        #endif
                        desc.Set(this, value);
                        #if DEBUG_LOG
                        Console.WriteLine($"   ✅ descriptor Set() completed");
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