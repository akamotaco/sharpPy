using System;
using System.Collections.Generic;
using System.Linq;
using SharpPy.Core;

namespace SharpPy
{
    #region Function and Method System

// Python의 function 타입
public partial class PyFunction : PyObject, IDescriptor
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

    // CPython 3.12: __globals__ attribute - function's global namespace
    public Dictionary<string, PyObject>? GlobalsDict { get; set; }  // func.__globals__

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

        // CPython 호환: function type descriptor 초기화
        InitializeFunctionDescriptors();
    }

    /// <summary>
    /// CPython 호환: function 타입의 descriptor 테이블 초기화
    /// </summary>
    private static void InitializeFunctionDescriptors()
    {
        var funcType = PyType.FunctionType;

        // __name__ getset descriptor
        funcType.TypeDict["__name__"] = new PyGetSetDescriptor(
            "__name__",
            funcType,
            getter: self => {
                if (self is PyFunction func)
                    return new PyString(func.Name);
                throw PyTypeError.Create("descriptor '__name__' for 'function' objects doesn't apply to a '" + self.GetTypeName() + "' object");
            }
        );

        // __module__ getset descriptor
        funcType.TypeDict["__module__"] = new PyGetSetDescriptor(
            "__module__",
            funcType,
            getter: self => {
                if (self is PyFunction func)
                    return func.DefiningModule != null ? new PyString(func.DefiningModule.Name) : new PyString("__main__");
                throw PyTypeError.Create("descriptor '__module__' for 'function' objects doesn't apply to a '" + self.GetTypeName() + "' object");
            }
        );

        // __doc__ getset descriptor
        funcType.TypeDict["__doc__"] = new PyGetSetDescriptor(
            "__doc__",
            funcType,
            getter: self => {
                if (self is PyFunction func)
                    return new PyString($"Function {func.Name}");
                throw PyTypeError.Create("descriptor '__doc__' for 'function' objects doesn't apply to a '" + self.GetTypeName() + "' object");
            }
        );

        // __code__ getset descriptor
        funcType.TypeDict["__code__"] = new PyGetSetDescriptor(
            "__code__",
            funcType,
            getter: self => {
                if (self is PyFunction func)
                    return (PyObject)(func.CodeObject ?? (object)PyNone.Instance);
                throw PyTypeError.Create("descriptor '__code__' for 'function' objects doesn't apply to a '" + self.GetTypeName() + "' object");
            }
        );

        // __closure__ getset descriptor
        funcType.TypeDict["__closure__"] = new PyGetSetDescriptor(
            "__closure__",
            funcType,
            getter: self => {
                if (self is PyFunction func)
                    return func.Attributes["__closure__"];
                throw PyTypeError.Create("descriptor '__closure__' for 'function' objects doesn't apply to a '" + self.GetTypeName() + "' object");
            }
        );

        // __globals__ getset descriptor - CPython 3.12
        funcType.TypeDict["__globals__"] = new PyGetSetDescriptor(
            "__globals__",
            funcType,
            getter: self => {
                if (self is PyFunction func)
                    return func.GlobalsDict != null ? new PyDict(func.GlobalsDict) : PyNone.Instance;
                throw PyTypeError.Create("descriptor '__globals__' for 'function' objects doesn't apply to a '" + self.GetTypeName() + "' object");
            }
        );

        // __dict__ getset descriptor
        funcType.TypeDict["__dict__"] = new PyGetSetDescriptor(
            "__dict__",
            funcType,
            getter: self => {
                if (self is PyFunction func)
                    return new PyDict(func.Attributes);
                throw PyTypeError.Create("descriptor '__dict__' for 'function' objects doesn't apply to a '" + self.GetTypeName() + "' object");
            }
        );

        // __call__ getset descriptor (함수 자체를 반환)
        funcType.TypeDict["__call__"] = new PyGetSetDescriptor(
            "__call__",
            funcType,
            getter: self => self // 함수 자체가 __call__
        );
    }
    
    private PyObject DefaultImplementation(PyObject[] args)
    {
        #if DEBUG_LOG
        Console.WriteLine($"Function {Name} called with {args.Length} arguments");
        #endif
        return PyNone.Instance;
    }

    public override PyType GetPyType() => PyType.FunctionType;
    public override string GetTypeName() => "function";

    // CPython 3.12 호환: kwargs 지원 버전
    public override PyObject Call(PyObject[] args, PyDict kwargs)
    {
        // async generator 함수인지 먼저 확인
        if (CodeObject?.IsAsyncGenerator() == true)
        {
            // async generator 객체 생성 (kwargs는 생성 시 사용하지 않음)
            return CreateAsyncGenerator(args);
        }
        // 코루틴 함수인지 확인 (async def, but not async generator)
        else if (CodeObject?.IsCoroutine() == true)
        {
            // 코루틴 객체 생성 (kwargs는 생성 시 사용하지 않음)
            return CreateCoroutine(args);
        }
        // 일반 generator 함수인지 확인
        else if (CodeObject?.IsGenerator() == true)
        {
            // 제너레이터 객체 생성 (kwargs는 생성 시 사용하지 않음)
            return CreateGenerator(args);
        }

        // CPython 3.12: CodeObject가 있으면 VM을 통해 실행
        if (CodeObject != null)
        {
            // CPython 3.12: Use captured globals (func.__globals__)
            // Functions must use the globals from the module where they were defined,
            // not the caller's globals. This is essential for closures and nested functions.
            PyScopeChain functionScopeChain;
            if (GlobalsDict != null)
            {
                // Use the globals captured at function definition time (CPython equivalent: frame->f_globals)
                functionScopeChain = new PyScopeChain(GlobalsDict, CodeObject.Name);

                #if DEBUG_LOG
                // Log for enum-related functions
                if (Name == "__new__" || Name.Contains("Enum"))
                {
                    Console.WriteLine($"\n[PyFunction.Call] Using GlobalsDict for function '{Name}'");
                    Console.WriteLine($"  GlobalsDict: {GlobalsDict.Count} items");
                    Console.WriteLine($"  Has ReprEnum: {GlobalsDict.ContainsKey("ReprEnum")}");
                    Console.WriteLine($"  Created scope: {functionScopeChain.GlobalScope?.Name}");
                }
                #endif
            }
            else
            {
                // Fallback to ParentScope for backwards compatibility (e.g., built-in functions)
                functionScopeChain = ParentScope ?? new PyScopeChain();

                #if DEBUG_LOG
                // Log for enum-related functions
                if (Name == "__new__" || Name.Contains("Enum"))
                {
                    Console.WriteLine($"\n[PyFunction.Call] GlobalsDict is NULL for function '{Name}' - using ParentScope");
                    Console.WriteLine($"  ParentScope: {(ParentScope == null ? "null" : ParentScope.GlobalScope?.Name ?? "no global")}");
                }
                #endif
            }

            var frame = new PyFrame(CodeObject, args, functionScopeChain, Closure);
            var vm = PyVM.Instance;
            return vm.ExecuteFrame(frame);
        }

        // TODO: kwargs 처리 로직 추가 필요 (현재는 무시)
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
    /// 코루틴 객체 생성 - PEP 492 호환
    /// </summary>
    private SharpPy.Core.PyCoroutine CreateCoroutine(PyObject[] args)
    {
        // CPython 3.12 방식: Frame과 VM을 사용한 실제 코루틴
        if (CodeObject == null)
        {
            throw new InvalidOperationException("Cannot create coroutine without code object");
        }

        // 코루틴용 VM 인스턴스 사용
        var vm = PyVM.Instance;

        // 코루틴 실행용 Frame 생성
        var frame = new PyFrame(CodeObject, args, null, Closure);
        frame.IsCoroutine = true;  // CPython 3.12: coroutine frame 표시

        return new SharpPy.Core.PyCoroutine(frame, vm, Name);
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
        // Generator는 정의된 모듈의 GlobalScope를 유지해야 함
        // CPython 3.12: Use captured globals (func.__globals__) - same as regular function calls

        Console.WriteLine($"[CreateGenerator] Function: {Name}");
        Console.WriteLine($"  GlobalsDict: {(GlobalsDict == null ? "NULL" : $"{GlobalsDict.Count} items")}");
        if (GlobalsDict != null && GlobalsDict.Count > 0)
        {
            Console.WriteLine($"  GlobalsDict keys: {string.Join(", ", GlobalsDict.Keys.Take(10))}");
            Console.WriteLine($"  Has 'print': {GlobalsDict.ContainsKey("print")}");
        }
        Console.WriteLine($"  ParentScope: {(ParentScope == null ? "NULL" : ParentScope.GlobalScope?.Name ?? "no global")}");
        if (ParentScope?.GlobalScope?.Variables != null)
        {
            Console.WriteLine($"  ParentScope.GlobalScope.Variables: {ParentScope.GlobalScope.Variables.Count} items");
            Console.WriteLine($"  ParentScope has 'print': {ParentScope.GlobalScope.Variables.ContainsKey("print")}");
        }

        PyScopeChain generatorScopeChain;
        if (GlobalsDict != null)
        {
            // Use the globals captured at function definition time
            generatorScopeChain = new PyScopeChain(GlobalsDict, CodeObject.Name);
            Console.WriteLine($"  → Using GlobalsDict");
        }
        else
        {
            // Fallback to ParentScope for backwards compatibility
            generatorScopeChain = ParentScope ?? new PyScopeChain();
            Console.WriteLine($"  → Using ParentScope (fallback)");
        }

        var frame = new PyFrame(CodeObject, args, generatorScopeChain, Closure);
        frame.IsGenerator = true;  // CPython 3.12: generator frame 표시

        // CPython 3.12 완전 호환 PyGenerator 사용
        return new PyGenerator(frame, vm, Name);
    }
    
    // Function은 항상 호출 가능
    public override bool IsCallable() => true;
    
    // Descriptor로서의 동작 (method binding)
    public PyObject Get(PyObject instance, PyType owner)
    {
        #if DEBUG_LOG
        Console.WriteLine($"🔧 PyFunction.Get called:");
        Console.WriteLine($"   Function: {Name}");
        Console.WriteLine($"   Instance: {instance?.GetType().Name} = {instance}");
        Console.WriteLine($"   Owner: {owner?.Name}");
        Console.WriteLine($"   Has CodeObject: {CodeObject != null}");
        Console.WriteLine($"   GlobalsDict: {(GlobalsDict == null ? "null" : $"{GlobalsDict.Count} items")}");
        Console.WriteLine($"   ParentScope: {(ParentScope == null ? "null" : ParentScope.GlobalScope?.Name ?? "no global")}");
        #endif
        if (CodeObject != null)
        {
            #if DEBUG_LOG
            Console.WriteLine($"   FreeVars: [{string.Join(", ", CodeObject.FreeVars ?? new List<string>())}]");
            Console.WriteLine($"   Has __class__ in FreeVars: {CodeObject.FreeVars?.Contains("__class__") == true}");
            #endif
        }
        #if DEBUG_LOG
        Console.WriteLine($"   Closure length: {Closure?.Length ?? 0}");
        #endif
        
        if (instance == null)
        {
            #if DEBUG_LOG
            Console.WriteLine($"   → returning unbound function (instance is null)");
            #endif
            return this; // unbound function
        }
            
        // CPython 3.12: Handle __class__ cell dynamic binding for metaclass methods
        if (instance is PyClass metaclassInstance && CodeObject?.FreeVars?.Contains("__class__") == true)
        {
            #if DEBUG_LOG
            Console.WriteLine($"🔧 CPython 3.12: Metaclass method binding detected");
            #endif
            #if DEBUG_LOG
            Console.WriteLine($"   Method: {Name}");
            #endif
            #if DEBUG_LOG
            Console.WriteLine($"   Binding to metaclass: {metaclassInstance}");
            #endif
            
            // Create a copy of this function with adjusted __class__ cell for the target metaclass
            if (Closure != null && Closure.Length > 0)
            {
                var adjustedClosure = new PyCell[Closure.Length];
                Array.Copy(Closure, adjustedClosure, Closure.Length);
                
                var classIndex = CodeObject.FreeVars.IndexOf("__class__");
                if (classIndex >= 0 && classIndex < adjustedClosure.Length)
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"   Original __class__ cell: {adjustedClosure[classIndex]?.Value}");
                    #endif
                    adjustedClosure[classIndex] = new PyCell(metaclassInstance);
                    #if DEBUG_LOG
                    Console.WriteLine($"   ✅ Updated __class__ cell[{classIndex}] to {metaclassInstance}");
                    #endif
                    
                    // Create a new function with the adjusted closure
                    var adjustedFunction = new PyFunction(Name, Implementation, DefiningModule, TypeParams, adjustedClosure, CodeObject);
                    // CPython 3.12: Preserve GlobalsDict and ParentScope
                    adjustedFunction.GlobalsDict = this.GlobalsDict;
                    adjustedFunction.ParentScope = this.ParentScope;

                    #if DEBUG_LOG
                    Console.WriteLine($"   🔧 Preserved GlobalsDict and ParentScope:");
                    Console.WriteLine($"      GlobalsDict: {(this.GlobalsDict == null ? "null" : $"{this.GlobalsDict.Count} items")}");
                    Console.WriteLine($"      ParentScope: {(this.ParentScope == null ? "null" : this.ParentScope.GlobalScope?.Name ?? "no global")}");
                    #endif

                    var boundMethod = new PyMethod(instance, adjustedFunction);
                    #if DEBUG_LOG
                    Console.WriteLine($"   → returning bound method with adjusted __class__ cell");
                    #endif
                    return boundMethod; // bound method with correct __class__
                }
            }
            #if DEBUG_LOG
            Console.WriteLine($"   ⚠️  Could not adjust __class__ cell (no closure or invalid index)");
            #endif
        }
        
        var normalBoundMethod = new PyMethod(instance, this);
        #if DEBUG_LOG
        Console.WriteLine($"   → returning normal bound method");
        #endif
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
    
    // Function attributes 접근 - CPython 호환: descriptor 테이블 사용
    public override PyObject GetAttribute(string name)
    {
        // CPython 3.12: 먼저 function의 instance dictionary (Attributes) 확인
        if (Attributes.TryGetValue(name, out PyObject value))
            return value;

        // 그 다음 descriptor 테이블 조회
        try
        {
            return GenericGetAttribute(name);
        }
        catch (PythonException)
        {
            throw;
        }
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

        public override PyType GetPyType() => PyType.MethodType; // CPython 3.12: bound method type
        public override string GetTypeName() => "method";

        // CPython 3.12 호환: kwargs 지원 버전
        public override PyObject Call(PyObject[] args, PyDict kwargs)
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
                return Function.Call(contextArgs, kwargs);
            }

            // 일반적인 bound method 처리
            var newArgs = new PyObject[args.Length + 1];
            newArgs[0] = Instance;
            Array.Copy(args, 0, newArgs, 1, args.Length);
            return Function.Call(newArgs, kwargs);
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
                "__code__" => Function.GetAttribute("__code__"), // CPython 3.12: Delegate to underlying function
                _ => base.GetAttribute(name)
            };
        }

        public override string ToString() => $"<bound method {Function.Name} of {Instance}>";
    }

    /// <summary>
    /// CPython 3.12: Bound builtin method wrapper
    /// PyBuiltinFunction을 인스턴스에 바인딩하여 self를 자동으로 전달
    /// </summary>
    public class PyBuiltinBoundMethod : PyObject
    {
        public PyObject Instance { get; }
        public PyBuiltinFunction BuiltinFunction { get; }

        public PyBuiltinBoundMethod(PyObject instance, PyBuiltinFunction builtinFunction)
        {
            Instance = instance;
            BuiltinFunction = builtinFunction;
        }

        public override PyType GetPyType() => PyType.FunctionType;
        public override string GetTypeName() => "builtin_function_or_method";

        // CPython 3.12 호환: kwargs 지원 버전
        public override PyObject Call(PyObject[] args, PyDict kwargs)
        {
            // self를 첫 번째 인수로 자동 추가
            var newArgs = new PyObject[args.Length + 1];
            newArgs[0] = Instance;
            Array.Copy(args, 0, newArgs, 1, args.Length);
            return BuiltinFunction.Call(newArgs, kwargs);
        }

        // Method는 항상 호출 가능
        public override bool IsCallable() => true;

        // Method attributes (__self__, __func__ 등)
        public override PyObject GetAttribute(string name)
        {
            return name switch
            {
                "__self__" => Instance,
                "__func__" => BuiltinFunction,
                "__name__" => new PyString(BuiltinFunction.Name),
                "__call__" => this,
                _ => base.GetAttribute(name)
            };
        }

        public override string ToString() => $"<built-in method {BuiltinFunction.Name} of {Instance.GetTypeName()} object>";
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
/// IronPython 스타일의 편리한 함수 생성을 위한 팩토리 메서드들
/// 자동 타입 변환과 타입 안전성을 제공
/// </summary>
public static class PyFunctionFactory
{
    #region Func 델리게이트용 Create 메서드들

    /// <summary>
    /// 매개변수 없는 함수 생성
    /// </summary>
    public static PyFunction Create<TResult>(string name, Func<TResult> func)
    {
        return new PyFunction(name, args =>
        {
            if (args.Length != 0)
                throw PyTypeError.Create($"{name}() takes no arguments but {args.Length} were given");

            try
            {
                var result = func();
                return PyTypeConverter.ToPyObject(result);
            }
            catch (Exception ex)
            {
                throw PyRuntimeError.Create($"Error in {name}(): {ex.Message}");
            }
        });
    }

    /// <summary>
    /// 매개변수 1개 함수 생성
    /// </summary>
    public static PyFunction Create<T1, TResult>(string name, Func<T1, TResult> func)
    {
        return new PyFunction(name, args =>
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"{name}() takes exactly 1 argument but {args.Length} were given");

            try
            {
                var arg1 = PyTypeConverter.FromPyObject<T1>(args[0]);
                var result = func(arg1);
                return PyTypeConverter.ToPyObject(result);
            }
            catch (Exception ex) when (!(ex is PyException))
            {
                throw PyRuntimeError.Create($"Error in {name}(): {ex.Message}");
            }
        });
    }

    /// <summary>
    /// 매개변수 2개 함수 생성
    /// </summary>
    public static PyFunction Create<T1, T2, TResult>(string name, Func<T1, T2, TResult> func)
    {
        return new PyFunction(name, args =>
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"{name}() takes exactly 2 arguments but {args.Length} were given");

            try
            {
                var arg1 = PyTypeConverter.FromPyObject<T1>(args[0]);
                var arg2 = PyTypeConverter.FromPyObject<T2>(args[1]);
                var result = func(arg1, arg2);
                return PyTypeConverter.ToPyObject(result);
            }
            catch (Exception ex) when (!(ex is PyException))
            {
                throw PyRuntimeError.Create($"Error in {name}(): {ex.Message}");
            }
        });
    }

    /// <summary>
    /// 매개변수 3개 함수 생성
    /// </summary>
    public static PyFunction Create<T1, T2, T3, TResult>(string name, Func<T1, T2, T3, TResult> func)
    {
        return new PyFunction(name, args =>
        {
            if (args.Length != 3)
                throw PyTypeError.Create($"{name}() takes exactly 3 arguments but {args.Length} were given");

            try
            {
                var arg1 = PyTypeConverter.FromPyObject<T1>(args[0]);
                var arg2 = PyTypeConverter.FromPyObject<T2>(args[1]);
                var arg3 = PyTypeConverter.FromPyObject<T3>(args[2]);
                var result = func(arg1, arg2, arg3);
                return PyTypeConverter.ToPyObject(result);
            }
            catch (Exception ex) when (!(ex is PyException))
            {
                throw PyRuntimeError.Create($"Error in {name}(): {ex.Message}");
            }
        });
    }

    /// <summary>
    /// 매개변수 4개 함수 생성
    /// </summary>
    public static PyFunction Create<T1, T2, T3, T4, TResult>(string name, Func<T1, T2, T3, T4, TResult> func)
    {
        return new PyFunction(name, args =>
        {
            if (args.Length != 4)
                throw PyTypeError.Create($"{name}() takes exactly 4 arguments but {args.Length} were given");

            try
            {
                var arg1 = PyTypeConverter.FromPyObject<T1>(args[0]);
                var arg2 = PyTypeConverter.FromPyObject<T2>(args[1]);
                var arg3 = PyTypeConverter.FromPyObject<T3>(args[2]);
                var arg4 = PyTypeConverter.FromPyObject<T4>(args[3]);
                var result = func(arg1, arg2, arg3, arg4);
                return PyTypeConverter.ToPyObject(result);
            }
            catch (Exception ex) when (!(ex is PyException))
            {
                throw PyRuntimeError.Create($"Error in {name}(): {ex.Message}");
            }
        });
    }

    #endregion

    #region Action 델리게이트용 Create 메서드들 (void 반환)

    /// <summary>
    /// 매개변수 없는 Action 생성
    /// </summary>
    public static PyFunction Create(string name, Action action)
    {
        return new PyFunction(name, args =>
        {
            if (args.Length != 0)
                throw PyTypeError.Create($"{name}() takes no arguments but {args.Length} were given");

            try
            {
                action();
                return PyNone.Instance;
            }
            catch (Exception ex)
            {
                throw PyRuntimeError.Create($"Error in {name}(): {ex.Message}");
            }
        });
    }

    /// <summary>
    /// 매개변수 1개 Action 생성
    /// </summary>
    public static PyFunction Create<T1>(string name, Action<T1> action)
    {
        return new PyFunction(name, args =>
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"{name}() takes exactly 1 argument but {args.Length} were given");

            try
            {
                var arg1 = PyTypeConverter.FromPyObject<T1>(args[0]);
                action(arg1);
                return PyNone.Instance;
            }
            catch (Exception ex) when (!(ex is PyException))
            {
                throw PyRuntimeError.Create($"Error in {name}(): {ex.Message}");
            }
        });
    }

    /// <summary>
    /// 매개변수 2개 Action 생성
    /// </summary>
    public static PyFunction Create<T1, T2>(string name, Action<T1, T2> action)
    {
        return new PyFunction(name, args =>
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"{name}() takes exactly 2 arguments but {args.Length} were given");

            try
            {
                var arg1 = PyTypeConverter.FromPyObject<T1>(args[0]);
                var arg2 = PyTypeConverter.FromPyObject<T2>(args[1]);
                action(arg1, arg2);
                return PyNone.Instance;
            }
            catch (Exception ex) when (!(ex is PyException))
            {
                throw PyRuntimeError.Create($"Error in {name}(): {ex.Message}");
            }
        });
    }

    /// <summary>
    /// 매개변수 3개 Action 생성
    /// </summary>
    public static PyFunction Create<T1, T2, T3>(string name, Action<T1, T2, T3> action)
    {
        return new PyFunction(name, args =>
        {
            if (args.Length != 3)
                throw PyTypeError.Create($"{name}() takes exactly 3 arguments but {args.Length} were given");

            try
            {
                var arg1 = PyTypeConverter.FromPyObject<T1>(args[0]);
                var arg2 = PyTypeConverter.FromPyObject<T2>(args[1]);
                var arg3 = PyTypeConverter.FromPyObject<T3>(args[2]);
                action(arg1, arg2, arg3);
                return PyNone.Instance;
            }
            catch (Exception ex) when (!(ex is PyException))
            {
                throw PyRuntimeError.Create($"Error in {name}(): {ex.Message}");
            }
        });
    }

    #endregion
}

/// <summary>
/// PyFunction 클래스에 IronPython 스타일 팩토리 메서드 추가
/// </summary>
public partial class PyFunction
{
    #region IronPython 스타일 Create 메서드들

    /// <summary>
    /// 매개변수 없는 함수 생성
    /// </summary>
    public static PyFunction Create<TResult>(string name, Func<TResult> func)
        => PyFunctionFactory.Create(name, func);

    /// <summary>
    /// 매개변수 1개 함수 생성
    /// </summary>
    public static PyFunction Create<T1, TResult>(string name, Func<T1, TResult> func)
        => PyFunctionFactory.Create(name, func);

    /// <summary>
    /// 매개변수 2개 함수 생성
    /// </summary>
    public static PyFunction Create<T1, T2, TResult>(string name, Func<T1, T2, TResult> func)
        => PyFunctionFactory.Create(name, func);

    /// <summary>
    /// 매개변수 3개 함수 생성
    /// </summary>
    public static PyFunction Create<T1, T2, T3, TResult>(string name, Func<T1, T2, T3, TResult> func)
        => PyFunctionFactory.Create(name, func);

    /// <summary>
    /// 매개변수 4개 함수 생성
    /// </summary>
    public static PyFunction Create<T1, T2, T3, T4, TResult>(string name, Func<T1, T2, T3, T4, TResult> func)
        => PyFunctionFactory.Create(name, func);

    /// <summary>
    /// 매개변수 없는 Action 생성
    /// </summary>
    public static PyFunction Create(string name, Action action)
        => PyFunctionFactory.Create(name, action);

    /// <summary>
    /// 매개변수 1개 Action 생성
    /// </summary>
    public static PyFunction Create<T1>(string name, Action<T1> action)
        => PyFunctionFactory.Create(name, action);

    /// <summary>
    /// 매개변수 2개 Action 생성
    /// </summary>
    public static PyFunction Create<T1, T2>(string name, Action<T1, T2> action)
        => PyFunctionFactory.Create(name, action);

    /// <summary>
    /// 매개변수 3개 Action 생성
    /// </summary>
    public static PyFunction Create<T1, T2, T3>(string name, Action<T1, T2, T3> action)
        => PyFunctionFactory.Create(name, action);

    #endregion
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
        
        return function.Call(args, kwargs);
    }
}

/// <summary>
/// typing.TypedDict 구현 - PEP 589
/// </summary>
public class PyTypedDict : PyObject
{
    public string Name { get; }
    public HashSet<string> RequiredKeys { get; } = new HashSet<string>();
    public HashSet<string> OptionalKeys { get; } = new HashSet<string>();
    public Dictionary<string, PyObject> KeyTypes { get; } = new Dictionary<string, PyObject>();

    public PyTypedDict(string name)
    {
        Name = name;
    }

    public void AddRequired(string key, PyObject type)
    {
        RequiredKeys.Add(key);
        KeyTypes[key] = type;
    }

    public void AddOptional(string key, PyObject type)
    {
        OptionalKeys.Add(key);
        KeyTypes[key] = type;
    }

    public override string GetTypeName() => "TypedDict";
    public override string ToString() => $"TypedDict('{Name}')";
}

/// <summary>
/// PEP 692: Unpack 래퍼 - **kwargs: Unpack[TypedDict] 표현
/// </summary>
public class PyUnpackWrapper : PyObject
{
    public PyObject WrappedType { get; }
    public PyTypedDict? TypedDict => WrappedType as PyTypedDict;

    public PyUnpackWrapper(PyObject wrappedType)
    {
        WrappedType = wrappedType;
    }

    public override string GetTypeName() => $"Unpack[{WrappedType.GetTypeName()}]";
    public override string ToString() => GetTypeName();

    /// <summary>
    /// kwargs 딕셔너리가 이 TypedDict와 호환되는지 검증
    /// </summary>
    public bool IsCompatibleWith(PyDict kwargs)
    {
        if (TypedDict == null) return true; // 검증할 TypedDict가 없으면 통과

        var requiredKeys = TypedDict.RequiredKeys;
        var allKeys = TypedDict.RequiredKeys.Concat(TypedDict.OptionalKeys).ToHashSet();

        // 필수 키가 모두 있는지 확인
        foreach (var requiredKey in requiredKeys)
        {
            if (!kwargs.InternalDict.ContainsKey(new PyString(requiredKey)))
                return false;
        }

        // 추가 키가 허용되지 않는 키인지 확인
        foreach (var kvp in kwargs.InternalDict)
        {
            if (kvp.Key is PyString keyStr)
            {
                if (!allKeys.Contains(keyStr.Value))
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// kwargs 딕셔너리 검증 (예외 던지지 않고 boolean 반환)
    /// </summary>
    public bool ValidateKwargs(PyDict kwargs)
    {
        return IsCompatibleWith(kwargs);
    }
}

#endregion
}