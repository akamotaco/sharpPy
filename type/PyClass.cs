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
        }

        public new PyClassInstance CreateInstance(params PyObject[] args)
        {
            var instance = new PyClassInstance(this);

            // __init__ 호출 (있다면)
            // __init__ 호출
            if (HasMethod("__init__"))
            {
                var init = instance.GetAttribute("__init__");
                var allArgs = new PyObject[args.Length + 1];
                allArgs[0] = instance;
                Array.Copy(args, 0, allArgs, 1, args.Length);
                if (init is PyMethod method)
                {
                    method.Call(allArgs);
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

        // 클래스 attribute 접근
        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "__name__":
                    return new PyString(Name);
                case "__bases__":
                    return new PyTuple(BaseTypes);
                case "__mro__":
                    return new PyTuple(MRO.Cast<PyObject>().ToArray());
                case "__dict__":
                    return new PyDict(ClassDict);
                case "__call__":
                    return this; // 클래스 자체가 __call__
                default:
                    if (ClassDict.TryGetValue(name, out PyObject value))
                    {
                        // Descriptor 처리
                        if (value is IDescriptor desc)
                            return desc.Get(null, this);
                        return value;
                    }
                    return base.GetAttribute(name);
            }
        }

        public override void SetAttribute(string name, PyObject value)
        {
            ClassDict[name] = value;
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