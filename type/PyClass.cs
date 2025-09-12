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
            : base(name, baseTypes)
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
            var instance = new PyClassInstance(this);

            // __init__ 호출 (있다면)
            if (HasMethod("__init__"))
            {
                var init = instance.GetAttribute("__init__");
                if (init is PyMethod method)
                {
                    // PyMethod는 이미 self가 바인딩되어 있으므로 args만 전달
                    method.Call(args);
                }
                else if (init is PyFunction function)
                {
                    // PyFunction은 self를 수동으로 추가해야 함
                    var allArgs = new PyObject[args.Length + 1];
                    allArgs[0] = instance;
                    Array.Copy(args, 0, allArgs, 1, args.Length);
                    function.Call(allArgs);
                }
            }

            return instance;
        }

        public bool HasMethod(string name)
        {
            return MRO.OfType<PyClass>().Any(t => t.ClassDict.ContainsKey(name));
        }

        // 클래스 호출 시 인스턴스 생성
        public override PyObject Call(params PyObject[] args)
        {
            return CreateInstance(args);
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
            Console.WriteLine($"🔍 PyClass.GetPyType() called for {Name}");
            Console.WriteLine($"   Metaclass: {Metaclass}");
            Console.WriteLine($"   Metaclass != null: {Metaclass != null}");
            
            // If this class was created with a metaclass, return the metaclass
            if (Metaclass != null)
            {
                Console.WriteLine($"   → Returning Metaclass: {Metaclass}");
                return Metaclass;
            }
            // Otherwise, return the default type (which is 'type')
            Console.WriteLine($"   → Returning base.GetPyType()");
            var baseType = base.GetPyType();
            Console.WriteLine($"   → base.GetPyType() returned: {baseType}");
            return baseType;
        }

        // 클래스 attribute 접근
        public override PyObject GetAttribute(string name)
        {
            Console.WriteLine($"🔍 PyClass.GetAttribute: {Name}.{name}");
            
            switch (name)
            {
                case "__name__":
                    Console.WriteLine($"   → returning __name__ = {Name}");
                    return new PyString(Name);
                case "__bases__":
                    Console.WriteLine($"   → returning __bases__ (count: {BaseTypes.Length})");
                    return new PyTuple(BaseTypes);
                case "__mro__":
                    Console.WriteLine($"   → returning __mro__ (count: {MRO.Count})");
                    return new PyTuple(MRO.Cast<PyObject>().ToArray());
                case "__dict__":
                    Console.WriteLine($"   → returning __dict__ (count: {ClassDict.Count})");
                    return new PyDict(ClassDict);
                case "__call__":
                    Console.WriteLine($"   → returning self for __call__");
                    return this; // 클래스 자체가 __call__
                default:
                    Console.WriteLine($"   → searching for '{name}' in ClassDict ({ClassDict.Count} items)");
                    if (ClassDict.TryGetValue(name, out PyObject value))
                    {
                        Console.WriteLine($"   ✅ found '{name}' in ClassDict: {value?.GetType().Name}");
                        // Descriptor 처리
                        if (value is IDescriptor desc)
                        {
                            Console.WriteLine($"   🔧 calling descriptor.Get(null, {Name}) for '{name}'");
                            var result = desc.Get(null, this);
                            Console.WriteLine($"   → descriptor returned: {result?.GetType().Name}");
                            return result;
                        }
                        Console.WriteLine($"   → returning direct value: {value}");
                        return value;
                    }
                    Console.WriteLine($"   ❌ '{name}' not found in ClassDict, checking base classes");
                    
                    // Check base classes (MRO)
                    foreach (var baseClass in BaseTypes)
                    {
                        if (baseClass is PyClass pyBaseClass)
                        {
                            Console.WriteLine($"   → Checking base class: {pyBaseClass.Name}");
                            if (pyBaseClass.ClassDict.TryGetValue(name, out PyObject baseValue))
                            {
                                Console.WriteLine($"   ✅ found '{name}' in base class {pyBaseClass.Name}: {baseValue?.GetType().Name}");
                                // Descriptor 처리
                                if (baseValue is IDescriptor baseDesc)
                                {
                                    Console.WriteLine($"   🔧 calling descriptor.Get(null, {Name}) for '{name}' from base");
                                    var result = baseDesc.Get(null, this);
                                    Console.WriteLine($"   → descriptor returned: {result?.GetType().Name}");
                                    return result;
                                }
                                return baseValue;
                            }
                        }
                    }
                    
                    Console.WriteLine($"   ❌ '{name}' not found in any base class, calling PyObject.GetAttribute");
                    var baseResult = base.GetAttribute(name);
                    Console.WriteLine($"   → base.GetAttribute returned: {baseResult?.GetType().Name}");
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
        private PyFunction _customGetAttr;

        public PyClassInstance(PyClass instanceType)
        {
            InstanceType = instanceType;
            InstanceDict = new Dictionary<string, PyObject>();

            // __getattr__ 메서드가 있는지 확인
            if (instanceType.ClassDict.ContainsKey("__getattr__"))
            {
                _customGetAttr = instanceType.ClassDict["__getattr__"] as PyFunction;
            }
        }

        public override PyType GetPyType() => InstanceType;
        public override string GetTypeName() => InstanceType.Name;

        protected override bool HasCustomGetAttr() => _customGetAttr != null;

        protected override PyObject CallGetAttr(string name)
        {
            if (_customGetAttr != null)
            {
                return _customGetAttr.Call(this, new PyString(name));
            }
            return null;
        }

        // Special attributes
        public override PyObject GetAttribute(string name)
        {
            if (name == "__class__") return InstanceType;
            if (name == "__dict__") return new PyDict(InstanceDict);

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