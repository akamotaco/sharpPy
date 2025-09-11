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
        // async generator 함수인지 먼저 확인
        if (CodeObject?.IsAsyncGenerator() == true)
        {
            // async generator 객체 생성
            return CreateAsyncGenerator(args);
        }
        // 일반 generator 함수인지 확인
        else if (CodeObject?.IsGenerator() == true)
        {
            // 제너레이터 객체 생성
            return CreateGenerator(args);
        }
        
        return Implementation(args);
    }
    
    /// <summary>
    /// async generator 객체 생성 - PEP 525 호환
    /// </summary>
    private SharpPy.Core.PyAsyncGenerator CreateAsyncGenerator(PyObject[] args)
    {
        // async generator 방식: Frame과 VM을 사용한 실제 async generator
        if (CodeObject == null)
        {
            throw new InvalidOperationException("Cannot create async generator without code object");
        }
        
        // async generator용 VM 인스턴스 사용
        var vm = PyVM.Instance;
        
        // async generator 실행용 Frame 생성
        var frame = new PyFrame(CodeObject, args, null, Closure);
        frame.IsGenerator = true;  // CPython 3.12: generator frame 표시
        
        // async generator enumerator 생성
        var enumerator = new FrameGeneratorEnumerator(frame, vm);
        
        return new SharpPy.Core.PyAsyncGenerator(enumerator, Name);
    }
    
    /// <summary>
    /// 제너레이터 객체 생성 - CPython 3.12 완전 호환
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
        
        // CPython 3.12 완전 호환 PyGenerator 사용
        return new PyGenerator(frame, vm, Name);
    }
    
    // Function은 항상 호출 가능
    public override bool IsCallable() => true;
    
    // Descriptor로서의 동작 (method binding)
    public PyObject Get(PyObject instance, PyType owner)
    {
        Console.WriteLine($"🔧 PyFunction.Get called:");
        Console.WriteLine($"   Function: {Name}");
        Console.WriteLine($"   Instance: {instance?.GetType().Name} = {instance}");
        Console.WriteLine($"   Owner: {owner?.Name}");
        Console.WriteLine($"   Has CodeObject: {CodeObject != null}");
        if (CodeObject != null)
        {
            Console.WriteLine($"   FreeVars: [{string.Join(", ", CodeObject.FreeVars ?? new List<string>())}]");
            Console.WriteLine($"   Has __class__ in FreeVars: {CodeObject.FreeVars?.Contains("__class__") == true}");
        }
        Console.WriteLine($"   Closure length: {Closure?.Length ?? 0}");
        
        if (instance == null)
        {
            Console.WriteLine($"   → returning unbound function (instance is null)");
            return this; // unbound function
        }
            
        // CPython 3.12: Handle __class__ cell dynamic binding for metaclass methods
        if (instance is PyClass metaclassInstance && CodeObject?.FreeVars?.Contains("__class__") == true)
        {
            Console.WriteLine($"🔧 CPython 3.12: Metaclass method binding detected");
            Console.WriteLine($"   Method: {Name}");
            Console.WriteLine($"   Binding to metaclass: {metaclassInstance}");
            
            // Create a copy of this function with adjusted __class__ cell for the target metaclass
            if (Closure != null && Closure.Length > 0)
            {
                var adjustedClosure = new PyCell[Closure.Length];
                Array.Copy(Closure, adjustedClosure, Closure.Length);
                
                var classIndex = CodeObject.FreeVars.IndexOf("__class__");
                if (classIndex >= 0 && classIndex < adjustedClosure.Length)
                {
                    Console.WriteLine($"   Original __class__ cell: {adjustedClosure[classIndex]?.Value}");
                    adjustedClosure[classIndex] = new PyCell(metaclassInstance);
                    Console.WriteLine($"   ✅ Updated __class__ cell[{classIndex}] to {metaclassInstance}");
                    
                    // Create a new function with the adjusted closure
                    var adjustedFunction = new PyFunction(Name, Implementation, DefiningModule, TypeParams, adjustedClosure, CodeObject);
                    var boundMethod = new PyMethod(instance, adjustedFunction);
                    Console.WriteLine($"   → returning bound method with adjusted __class__ cell");
                    return boundMethod; // bound method with correct __class__
                }
            }
            Console.WriteLine($"   ⚠️  Could not adjust __class__ cell (no closure or invalid index)");
        }
        
        var normalBoundMethod = new PyMethod(instance, this);
        Console.WriteLine($"   → returning normal bound method");
        return normalBoundMethod; // bound method
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
            "__dict__" => new PyDict(Attributes), // CPython 3.12: Function __dict__ attribute
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
            // CPython 3.12 context manager 호환성: __exit__ method 특별 처리
            if (Function.Name == "__exit__" && args.Length == 2)
            {
                // CALL 2로 2개 인수를 받았지만, __exit__는 4개 매개변수 필요
                // CPython에서는 스택에 3개 None이 있고 CALL 2는 특별 처리됨
                var contextArgs = new PyObject[4];
                contextArgs[0] = Instance; // self
                contextArgs[1] = args[0];  // exc_type (None)
                contextArgs[2] = args[1];  // exc_val (None)
                contextArgs[3] = PyNone.Instance; // exc_tb (None) - 암시적으로 추가
                return Function.Call(contextArgs);
            }
            
            // 일반적인 bound method 처리
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