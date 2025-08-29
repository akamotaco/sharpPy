namespace SharpPy
{
    #region Function and Method System

// Python의 function 타입
public class PyFunction : PyObject, IDescriptor
{
    public string Name { get; }
    public Func<PyObject[], PyObject> Implementation { get; }
    public Dictionary<string, PyObject> Attributes { get; }
    public PyModule DefiningModule { get; }
    public List<PyObject>? TypeParams { get; set; } // PEP 695 __type_params__

    public PyFunction(string name, Func<PyObject[], PyObject> implementation = null, PyModule definingModule = null, List<PyObject>? typeParams = null)
    {
        Name = name;
        Implementation = implementation ?? DefaultImplementation;
        Attributes = new Dictionary<string, PyObject>();
        DefiningModule = definingModule;
        TypeParams = typeParams;
        
        // __type_params__ 속성 설정
        if (TypeParams != null && TypeParams.Count > 0)
        {
            var typeParamsTuple = new PyTuple(TypeParams.ToArray());
            Attributes["__type_params__"] = typeParamsTuple;
        }
        else
        {
            Attributes["__type_params__"] = new PyTuple(new PyObject[0]);
        }
    }
    
    private PyObject DefaultImplementation(PyObject[] args)
    {
        Console.WriteLine($"Function {Name} called with {args.Length} arguments");
        return PyNone.Instance;
    }

    public override PyType GetPyType() => PyType.FunctionType;
    public override string GetTypeName() => "function";

    public override PyObject Call(params PyObject[] args)
    {
        return Implementation(args);
    }
    
    // Function은 항상 호출 가능
    public override bool IsCallable() => true;
    
    // Descriptor로서의 동작 (method binding)
    public PyObject Get(PyObject instance, PyType owner)
    {
        if (instance == null)
            return this; // unbound function
        return new PyMethod(instance, this); // bound method
    }
    
    public void Set(PyObject instance, PyObject value)
    {
        throw PyAttributeError.Create("can't set function");
    }
    
    public void Delete(PyObject instance)
    {
        throw PyAttributeError.Create("can't delete function");
    }
    
    public bool IsDataDescriptor() => false; // function은 non-data descriptor
    
    // Function attributes 접근
    public override PyObject GetAttribute(string name)
    {
        return name switch
        {
            "__name__" => new PyString(Name),
            "__module__" => DefiningModule != null ? new PyString(DefiningModule.Name) : new PyString("__main__"),
            "__doc__" => new PyString($"Function {Name}"),
            "__call__" => this, // 함수 자체가 __call__
            _ => Attributes.TryGetValue(name, out PyObject value) ? value : throw PyAttributeError.Create($"'function' object has no attribute '{name}'")
        };
    }
    
    public override void SetAttribute(string name, PyObject value)
    {
        Attributes[name] = value;
    }

    public override string ToString() => $"<function {Name}>";
}

    // Python의 method 타입 (바인드된 메서드)
    public class PyMethod : PyObject
    {
        public PyObject Instance { get; }
        public PyFunction Function { get; }

        public PyMethod(PyObject instance, PyFunction function)
        {
            Instance = instance;
            Function = function;
        }

        public override PyType GetPyType() => PyType.FunctionType; // 단순화
        public override string GetTypeName() => "method";

        public override PyObject Call(params PyObject[] args)
        {
            // self를 첫 번째 인자로 추가
            var newArgs = new PyObject[args.Length + 1];
            newArgs[0] = Instance;
            Array.Copy(args, 0, newArgs, 1, args.Length);
            return Function.Call(newArgs);
        }

        // Method는 항상 호출 가능
        public override bool IsCallable() => true;

        // Method attributes (__self__, __func__ 등)
        public override PyObject GetAttribute(string name)
        {
            return name switch
            {
                "__self__" => Instance,
                "__func__" => Function,
                "__name__" => new PyString(Function.Name),
                "__call__" => this, // 메서드 자체가 __call__
                _ => base.GetAttribute(name)
            };
        }

        public override string ToString() => $"<bound method {Function.Name} of {Instance}>";
    }

#endregion
}