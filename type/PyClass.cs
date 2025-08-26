namespace SharpPy
{
    // 사용자 정의 클래스
    public class PyClass : PyType
    {
        public Dictionary<string, PyObject> ClassDict { get; }

        public PyClass(string name, PyType[] baseTypes) : base(name, baseTypes)
        {
            ClassDict = new Dictionary<string, PyObject>();
        }

        public PyClassInstance CreateInstance(params PyObject[] args)
        {
            var instance = new PyClassInstance(this);

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
            // Special attributes
            if (name == "__name__") return new PyString(Name);
            if (name == "__bases__") return new PyTuple(BaseTypes);
            if (name == "__mro__") return new PyTuple(MRO.Cast<PyObject>().ToArray());
            if (name == "__dict__") return new PyDict(ClassDict);
            if (name == "__call__") return this; // 클래스 자체가 __call__ (인스턴스 생성)

            if (ClassDict.TryGetValue(name, out PyObject value))
            {
                if (value is IDescriptor desc)
                    return desc.Get(null, this);
                return value;
            }

            return base.GetAttribute(name);
        }

        public override void SetAttribute(string name, PyObject value)
        {
            ClassDict[name] = value;
        }
    }

    // 사용자 정의 클래스의 인스턴스
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
    }

    #region Super Implementation

    // Python의 super() 구현
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

            throw new AttributeError($"'super' object has no attribute '{name}'");
        }

        public override string GetTypeName() => "super";
        public override string ToString() => $"<super: {Type.Name}, {Instance}>";
    }

#endregion
}
