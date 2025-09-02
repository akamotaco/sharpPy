using System.Collections.Generic;
using System.Linq;

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
    public PyFunctionSignature? Signature { get; set; } // PEP 692 **kwargs 타입 정보
    
    // Closure support (CPython 호환)
    public PyCell[] Closure { get; set; } = new PyCell[0];  // 클로저 셀 배열
    public PyCodeObject? CodeObject { get; set; }           // 함수의 코드 객체
    public PyScopeChain? ParentScope { get; set; }         // 부모 스코프 (클로저용)

    public PyFunction(string name, Func<PyObject[], PyObject> implementation = null, PyModule definingModule = null, List<PyObject>? typeParams = null, PyCell[] closure = null, PyCodeObject codeObject = null)
    {
        Name = name;
        Implementation = implementation ?? DefaultImplementation;
        Attributes = new Dictionary<string, PyObject>();
        DefiningModule = definingModule;
        TypeParams = typeParams;
        Closure = closure ?? new PyCell[0];
        CodeObject = codeObject;
        
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
        
        // Closure 정보를 속성으로 노출
        if (Closure.Length > 0)
        {
            Attributes["__closure__"] = new PyTuple(Closure.Cast<PyObject>().ToArray());
        }
        else
        {
            Attributes["__closure__"] = PyNone.Instance;
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
        // 제너레이터 함수인지 확인
        if (CodeObject?.IsGenerator() == true)
        {
            // 제너레이터 객체 생성
            return CreateGenerator(args);
        }
        
        return Implementation(args);
    }
    
    /// <summary>
    /// 제너레이터 객체 생성 - CPython 3.12 스타일
    /// </summary>
    private PyGenerator CreateGenerator(PyObject[] args)
    {
        // CPython 3.12 방식: Frame과 VM을 사용한 실제 제너레이터
        if (CodeObject == null)
        {
            throw new InvalidOperationException("Cannot create generator without code object");
        }
        
        // 제너레이터용 VM 인스턴스 사용
        var vm = PyVM.Instance;
        
        // 제너레이터 실행용 Frame 생성 (한 번만 생성하여 재사용)
        var frame = new PyFrame(CodeObject, args, null, Closure);
        frame.IsGenerator = true;  // CPython 3.12: generator frame 표시
        
        // CPython 3.12 스타일 FrameGeneratorEnumerator 사용
        var enumerator = new FrameGeneratorEnumerator(frame, vm);
        
        return new PyGenerator(enumerator, Name);
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
            "__closure__" => Attributes["__closure__"], // 클로저 정보
            "__code__" => (PyObject)(CodeObject ?? (object)PyNone.Instance), // 코드 객체
            _ => Attributes.TryGetValue(name, out PyObject value) ? value : throw PyAttributeError.Create($"'function' object has no attribute '{name}'")
        };
    }
    
    public override void SetAttribute(string name, PyObject value)
    {
        Attributes[name] = value;
    }

    public override string ToString() => $"<function {Name}>";
    
    /// <summary>
    /// CPython-style closure function creation helper
    /// </summary>
    public static PyFunction CreateClosureFunction(string name, PyCodeObject codeObject, PyCell[] closure, PyScopeChain parentScope = null)
    {
        // Create implementation that executes code object with closure support
        Func<PyObject[], PyObject> implementation = args =>
        {
            var frame = new PyFrame(codeObject, args, parentScope, closure);
            return PyVM.Instance.ExecuteFrame(frame);
        };
        
        var function = new PyFunction(name, implementation, null, null, closure, codeObject);
        function.ParentScope = parentScope;
        return function;
    }
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

#region PEP 692: Function Signature Support

/// <summary>
/// PEP 692: 함수 시그니처 - **kwargs 타입 검증 지원
/// </summary>
public class PyFunctionSignature
{
    public List<string> Parameters { get; }
    public Dictionary<string, PyObject> ParameterTypes { get; }
    public PyUnpackWrapper? KwargsType { get; set; } // **kwargs: Unpack[TypedDict]
    
    public PyFunctionSignature()
    {
        Parameters = new List<string>();
        ParameterTypes = new Dictionary<string, PyObject>();
    }
    
    /// <summary>
    /// **kwargs 타입을 설정 (PEP 692)
    /// </summary>
    public void SetKwargsType(PyUnpackWrapper unpackType)
    {
        KwargsType = unpackType;
    }
    
    /// <summary>
    /// 함수 호출 시 kwargs 검증
    /// </summary>
    public void ValidateKwargs(PyDict kwargs)
    {
        if (KwargsType == null) return; // 타입 검증 없음
        
        if (!KwargsType.ValidateKwargs(kwargs))
        {
            var typedDict = KwargsType.TypedDict;
            var missing = typedDict.RequiredKeys.Where(k => !kwargs.InternalDict.ContainsKey(new PyString(k))).ToList();
            var extra = kwargs.InternalDict.Keys
                .Select(k => ((PyString)k).Value)
                .Where(k => !typedDict.RequiredKeys.Contains(k) && !typedDict.OptionalKeys.Contains(k))
                .ToList();
            
            var errors = new List<string>();
            if (missing.Any())
                errors.Add($"missing required keys: {string.Join(", ", missing)}");
            if (extra.Any())
                errors.Add($"unexpected keys: {string.Join(", ", extra)}");
                
            throw PyTypeError.Create($"Invalid kwargs for {typedDict.Name}: {string.Join("; ", errors)}");
        }
    }
}

/// <summary>
/// PEP 692: **kwargs 검증이 있는 함수 호출 헬퍼
/// </summary>
public static class PEP692CallHelper
{
    /// <summary>
    /// **kwargs 타입 검증과 함께 함수 호출
    /// </summary>
    public static PyObject CallWithKwargsValidation(PyFunction function, PyObject[] args, PyDict? kwargs = null)
    {
        // **kwargs 타입 검증
        if (function.Signature?.KwargsType != null && kwargs != null)
        {
            function.Signature.ValidateKwargs(kwargs);
        }
        
        return function.Call(args);
    }
}

#endregion
}