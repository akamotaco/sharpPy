namespace SharpPy
{
    #region User-Defined Classes

    /// <summary>
    /// 사용자 정의 클래스
    /// </summary>
    public class PyClass : PyType
    {
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
                case "mro":
                    #if DEBUG_LOG
                    Console.WriteLine($"   → returning mro method");
                    #endif
                    return new PyBuiltinFunction("mro", (args) => {
                        return new PyList(MRO.Cast<PyObject>().ToList());
                    });
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
                    
                    #if DEBUG_LOG
                    Console.WriteLine($"   ❌ '{name}' not found in MRO, calling PyObject.GetAttribute");
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
        private static PyObject? GetTypeAttribute(PyType pyType, string name)
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
                        "__str__" => new PyBuiltinFunction("__str__", args => new PyString(args[0].ToString())),
                        "__repr__" => new PyBuiltinFunction("__repr__", args => new PyString(args[0].ToString())),
                        "__hash__" => new PyBuiltinFunction("__hash__", args => new PyInt(args[0].GetHashCode())),
                        "__eq__" => new PyBuiltinFunction("__eq__", args => PyBool.FromBool(args[0].Equals(args[1]))),
                        "__ne__" => new PyBuiltinFunction("__ne__", args => PyBool.FromBool(!args[0].Equals(args[1]))),
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
        }

        public override string ToString()
        {
            // For exception classes, return the first argument (message)
            if (IsExceptionClass() && ConstructorArgs.Length > 0)
            {
                return ConstructorArgs[0].ToString();
            }

            // Default object representation
            return $"<{InstanceType.Name} object at 0x{GetHashCode():x}>";
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

            // 1. 인스턴스 딕셔너리에서 먼저 검색
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

            // 2. 클래스의 MRO에서 검색 (Python의 표준 attribute resolution order)
            #if DEBUG_LOG
            Console.WriteLine($"   → searching class MRO (count: {InstanceType.MRO.Count})");
            #endif
            foreach (var mroType in InstanceType.MRO)
            {
                #if DEBUG_LOG
                Console.WriteLine($"     - checking {mroType.Name}");
                #endif

                if (mroType is PyClass pyClass && pyClass.ClassDict.TryGetValue(name, out PyObject classValue))
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"   ✅ found '{name}' in {mroType.Name}: {classValue?.GetType().Name}");
                    #endif

                    // Descriptor 처리
                    if (classValue is IDescriptor desc)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"   🔧 calling descriptor.Get(this, {InstanceType.Name}) for '{name}'");
                        #endif
                        var result = desc.Get(this, InstanceType);
                        #if DEBUG_LOG
                        Console.WriteLine($"   🔧 descriptor returned: {result?.GetType().Name}");
                        #endif
                        return result;
                    }
                    // 함수를 bound method로 변환
                    else if (classValue is PyFunction func)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"   🔧 converting function to bound method for '{name}'");
                        #endif
                        return new PyMethod(this, func);
                    }

                    return classValue;
                }

                // PyType의 내장 속성들도 확인 (예: object 클래스의 메서드들)
                try
                {
                    var builtinAttr = mroType.GetAttribute(name);
                    if (builtinAttr != null)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"   ✅ found builtin attribute '{name}' in {mroType.Name}: {builtinAttr?.GetType().Name}");
                        #endif
                        if (builtinAttr is PyFunction builtinFunc)
                        {
                            return new PyMethod(this, builtinFunc);
                        }
                        return builtinAttr;
                    }
                }
                catch (Exception ex) when (ex.GetType().Name.Contains("PyAttributeError"))
                {
                    // 속성이 없으면 계속 진행
                }
            }

            #if DEBUG_LOG
            Console.WriteLine($"   ❌ attribute '{name}' not found in MRO");
            #endif

            // 3. __getattr__ 커스텀 핸들러 호출 (있다면)
            if (HasCustomGetAttr())
            {
                #if DEBUG_LOG
                Console.WriteLine($"   → trying custom __getattr__ for '{name}'");
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

            // 4. 기본 처리 (PyObject의 기본 구현)
            #if DEBUG_LOG
            Console.WriteLine($"   → falling back to base.GetAttribute for '{name}'");
            #endif
            return base.GetAttribute(name);
        }

        public override string ToRepr()
        {
            return $"<{GetTypeName()} object at 0x{GetHashCode():x}>";
        }
    }

    #endregion

    #region Super Implementation

    /// <summary>
    /// Python의 super() 구현
    /// </summary>
    public class PySuper : PyObject
    {
        public PyType Type { get; }
        public PyObject Instance { get; }
        public List<PyType> SuperMRO { get; }

        public PySuper(PyType type, PyObject instance)
        {
            Type = type;
            Instance = instance;

            // super()는 현재 클래스 다음부터의 MRO를 사용
            var instanceMRO = instance.GetPyType().MRO;
            var typeIndex = instanceMRO.IndexOf(type);
            if (typeIndex >= 0 && typeIndex < instanceMRO.Count - 1)
            {
                SuperMRO = instanceMRO.Skip(typeIndex + 1).ToList();
            }
            else
            {
                SuperMRO = new List<PyType>();
            }
        }

        public override string GetTypeName() => "super";
        public override string ToRepr() => $"<super: {Type.Name}, {Instance}>";

        public override PyObject GetAttribute(string name)
        {
            // super()의 MRO에서 메서드 찾기
            foreach (var mroType in SuperMRO)
            {
                if (mroType is PyClass customType && customType.ClassDict.ContainsKey(name))
                {
                    var attr = customType.ClassDict[name];
                    if (attr is PyFunction func)
                    {
                        return new PyMethod(Instance, func);
                    }
                    return attr;
                }
            }

            throw PyAttributeError.Create($"'super' object has no attribute '{name}'");
        }
    }

    #endregion
}