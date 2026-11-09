using System;
using System.Collections.Generic;
using SharpPy.Core;

namespace SharpPy
{
    #region Function and Method System

// Python의 function 타입
public partial class PyFunction : PyObject, IDescriptor, IObjectDict
{
    public string Name { get; private set; }
    // func_qualname (Objects/funcobject.c:705). It starts as co_qualname and moves independently of
    // __name__ once something assigns it, which is what functools.wraps relies on.
    private string _qualName;
    public string QualName => _qualName ?? PyFrame.BindErrorName(CodeObject?.DisplayQualName ?? Name);
    internal void SetQualName(string value) => _qualName = value;
    private PyString _qualNameObject;
    private PyString _nameObject;
    internal string AssignedQualName => _qualName;
    // func_annotations (Objects/funcobject.c): reading creates the dict, so it is never an AttributeError.
    private PyObject _annotations;
    public Func<PyObject[], PyObject> Implementation { get; }
    public PyModule DefiningModule { get; }
    public List<PyObject>? TypeParams { get; set; } // PEP 695 __type_params__
    public PyFunctionSignature? Signature { get; set; } // PEP 692 **kwargs 타입 정보
    
    // Closure support (CPython 호환)
    public PyCell[] Closure { get; set; } = new PyCell[0];  // 클로저 셀 배열
    public PyCodeObject? CodeObject { get; set; }           // 함수의 코드 객체
    public PyScopeChain? ParentScope { get; set; }         // 부모 스코프 (클로저용)

    // CPython 3.12: __globals__ attribute - function's global namespace
    public IDictionary<string, PyObject>? GlobalsDict { get; set; }  // func.__globals__

    // func_dict: the function's one __dict__, made when it is first needed (PyObject_GenericGetDict).
    private PyDict? _dict;
    public PyDict ObjectDict(bool create) => create ? _dict ??= new PyDict() : _dict;
    public void SetObjectDict(PyDict dict) => _dict = dict;
    private PyDict? _globalsCache = null;
    private PyObject? _inheritedBuiltins;
    private PyObject? _module;
    private PyTuple? _closureTuple;
    private PyCell[]? _closureSource;
    private PyObject? _defaults;
    private PyObject? _kwdefaults;
    private PyObject? _typeParams;
    private PyObject? _doc;
    private bool _docWasSet;

    public PyFunction(string name, Func<PyObject[], PyObject> implementation = null, PyModule definingModule = null, List<PyObject>? typeParams = null, PyCell[] closure = null, PyCodeObject codeObject = null)
    {
        Name = name;
        Implementation = implementation ?? DefaultImplementation;
        DefiningModule = definingModule;
        TypeParams = typeParams;
        Closure = closure ?? new PyCell[0];
        CodeObject = codeObject;

        if (TypeParams != null && TypeParams.Count > 0)
            _typeParams = PyTuple.From(TypeParams.ToArray());

        // CPython 호환: function type descriptor 초기화
        InitializeFunctionDescriptors();
    }

    /// <summary>
    /// CPython 호환: function 타입의 descriptor 테이블 초기화
    /// </summary>
    // func_new_impl(type, code, globals, name=None, argdefs=None, closure=None)
    private static PyObject FunctionNew(PyObject cls, PyObject[] args, PyDict kwargs)
    {
        string[] names = { "code", "globals", "name", "argdefs", "closure" };
        if (args.Length > names.Length)
            throw PyTypeError.Create($"function() takes at most 5 arguments ({args.Length} given)");
        var given = new PyObject[names.Length];
        Array.Copy(args, given, args.Length);
        if (kwargs != null)
            foreach (var entry in kwargs.InternalDict)
            {
                int position = Array.IndexOf(names, (entry.Key as PyString)?.Value);
                if (position < 0)
                    throw PyArgs.InvalidKeyword("function", entry.Key);
                if (given[position] != null)
                    throw PyTypeError.Create($"argument for function() given by name ('{names[position]}') and position ({position + 1})");
                given[position] = entry.Value;
            }
        for (int i = 0; i < 2; i++)
            if (given[i] == null)
                throw PyArgs.MissingArgument("function", names[i], i + 1);
        if (given[0] is not PyCodeObject code)
            throw PyArgs.BadArgument("function", "argument 'code'", "code", given[0]);
        if (given[1] is not PyDict globals)
            throw PyArgs.BadArgument("function", "argument 'globals'", "dict", given[1]);
        var name = given[2] ?? PyNone.Instance;
        var defaults = given[3] ?? PyNone.Instance;
        var closure = given[4] ?? PyNone.Instance;
        if (name is not PyNone && name is not PyString)
            throw PyTypeError.Create("arg 3 (name) must be None or string");
        if (defaults is not PyNone && defaults is not PyTuple)
            throw PyTypeError.Create("arg 4 (defaults) must be None or tuple");
        if (closure is not PyTuple)
        {
            if (code.FreeVars.Count != 0 && closure is PyNone)
                throw PyTypeError.Create("arg 5 (closure) must be tuple");
            if (closure is not PyNone)
                throw PyTypeError.Create("arg 5 (closure) must be None or tuple");
        }
        var cells = (closure as PyTuple)?.Items ?? Array.Empty<PyObject>();
        if (code.FreeVars.Count != cells.Length)
            throw PyValueError.Create($"{code.Name} requires closure of length {code.FreeVars.Count}, not {cells.Length}");
        var closureCells = new PyCell[cells.Length];
        for (int i = 0; i < cells.Length; i++)
            closureCells[i] = cells[i] as PyCell ??
                throw PyTypeError.Create($"arg 5 (closure) expected cell, found {cells[i].GetTypeName()}");

        var function = new PyFunction(name is PyString text ? text.Value : code.Name, null, null, null, closureCells, code);
        function.GlobalsDict = PyVM.ScopeOver(globals).GlobalScope.Variables;
        function._globalsCache = globals;
        // _PyFunction_FromConstructor: no __builtins__ in globals, so the caller's.
        PyObject current = null;
        PyVM.CurrentFrame?.Globals.TryGetValue("__builtins__", out current);
        function._inheritedBuiltins = current;
        if (defaults is not PyNone)
            function.SetAttribute("__defaults__", defaults);
        return function;
    }

    private static void InitializeFunctionDescriptors()
    {
        var funcType = PyType.FunctionType;

        // func_get_name and func_set_name
        funcType.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", funcType, FunctionNew, 0, int.MaxValue, acceptsKwargs: true);

        funcType.TypeDict["__name__"] = new PyGetSetDescriptor(
            "__name__",
            funcType,
            getter: self => {
                if (self is PyFunction func)
                    return func._nameObject ??= PyString.From(func.Name);
                throw PyTypeError.Create("descriptor '__name__' for 'function' objects doesn't apply to a '" + self.GetTypeName() + "' object");
            },
            setter: (self, value) => {
                if (value is not PyString text)
                    throw PyTypeError.Create("__name__ must be set to a string object");
                ((PyFunction)self).Name = text.Value;
                ((PyFunction)self)._nameObject = text;
            },
            deleter: self => throw PyTypeError.Create("__name__ must be set to a string object")
        );

        // __qualname__ getset (Objects/funcobject.c:705, getter :529, setter :535). "Not legal to
        // del f.__qualname__ or to set it to anything other than a string object" (:536-:541) - one
        // message for both, which is what a probe sees three times.
        funcType.TypeDict["__qualname__"] = new PyGetSetDescriptor(
            "__qualname__",
            funcType,
            getter: self => {
                if (self is PyFunction func)
                    return func._qualNameObject ??= PyString.From(func.QualName);
                throw PyTypeError.Create("descriptor '__qualname__' for 'function' objects doesn't apply to a '" + self.GetTypeName() + "' object");
            },
            setter: (self, value) => {
                if (!(self is PyFunction func))
                    throw PyTypeError.Create("descriptor '__qualname__' for 'function' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                if (!(value is PyString text))
                    throw PyTypeError.Create("__qualname__ must be set to a string object");
                func.SetQualName(text.Value);
                func._qualNameObject = text;
            },
            deleter: self => {
                if (!(self is PyFunction))
                    throw PyTypeError.Create("descriptor '__qualname__' for 'function' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                throw PyTypeError.Create("__qualname__ must be set to a string object");
            }
        );

        // __annotations__ getset (func_get_annotations, func_set_annotations): None and deletion clear it.
        funcType.TypeDict["__annotations__"] = new PyGetSetDescriptor(
            "__annotations__",
            funcType,
            getter: self => ((PyFunction)self)._annotations ??= new PyDict(),
            setter: (self, value) => {
                if (value is PyNone) { ((PyFunction)self)._annotations = null; return; }
                if (!(value is PyDict))
                    throw PyTypeError.Create("__annotations__ must be set to a dict object");
                ((PyFunction)self)._annotations = value;
            },
            deleter: self => ((PyFunction)self)._annotations = null
        );

        // __module__ getset descriptor
        funcType.TypeDict["__module__"] = new PyMemberDescriptor(
            "__module__",
            funcType,
            getter: self => {
                var func = (PyFunction)self;
                // PyFunction_NewWithQualName: func_module is globals['__name__'], and it can be set.
                if (func._module != null)
                    return func._module;
                if (func.GlobalsDict != null && func.GlobalsDict.TryGetValue("__name__", out var name))
                    return name;
                return func.DefiningModule != null ? PyString.From(func.DefiningModule.Name) : PyNone.Instance;
            },
            setter: (self, value) => ((PyFunction)self)._module = value
        );

        // func_doc: a member, set from the code's first constant
        funcType.TypeDict["__doc__"] = new PyMemberDescriptor(
            "__doc__",
            funcType,
            getter: self => {
                var func = (PyFunction)self;
                if (func._docWasSet)
                    return func._doc ?? PyNone.Instance;
                if (func.CodeObject != null && func.CodeObject.Constants.Count > 0 && func.CodeObject.Constants[0] is PyString docString)
                    return docString;
                return PyNone.Instance;
            },
            setter: (self, value) => {
                var func = (PyFunction)self;
                func._docWasSet = true;
                func._doc = value;
            },
            deleter: self => {
                var func = (PyFunction)self;
                func._docWasSet = true;
                func._doc = null;
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
            },
            setter: (self, value) => {
                // func_set_code: a code of the same closure size, and nothing else.
                if (self is not PyFunction func || value is not PyCodeObject code)
                    throw PyTypeError.Create("__code__ must be set to a code object");
                if (func.Closure.Length != code.FreeVars.Count)
                    throw PyValueError.Create($"{func.Name}() requires a code object with {func.Closure.Length} free vars, not {code.FreeVars.Count}");
                func.CodeObject = code;
            },
            deleter: self => throw PyTypeError.Create("__code__ must be set to a code object")
        );

        // __closure__ getset descriptor
        funcType.TypeDict["__closure__"] = new PyMemberDescriptor(
            "__closure__",
            funcType,
            getter: self => {
                var func = (PyFunction)self;
                if (func.Closure.Length == 0)
                    return PyNone.Instance;
                // func_closure is one tuple: the attribute hands out the same object.
                if (func._closureTuple == null || !ReferenceEquals(func._closureSource, func.Closure))
                {
                    var cells = new PyObject[func.Closure.Length];
                    Array.Copy(func.Closure, cells, cells.Length);
                    func._closureTuple = PyTuple.From(cells);
                    func._closureSource = func.Closure;
                }
                return func._closureTuple;
            }
        );

        // __globals__ getset descriptor - CPython 3.12
        // CPython 3.12: func.__globals__ is func.__globals__ must be True
        funcType.TypeDict["__globals__"] = new PyMemberDescriptor(
            "__globals__",
            funcType,
            getter: self => {
                if (self is PyFunction func)
                {
                    if (func.GlobalsDict == null)
                        return PyNone.Instance;

                    // Lazy-initialize and cache the globals dict wrapper
                    if (func._globalsCache == null)
                    {
                        func._globalsCache = PyGlobalsDict.For(func.GlobalsDict);
                    }
                    return func._globalsCache;
                }
                throw PyTypeError.Create("descriptor '__globals__' for 'function' objects doesn't apply to a '" + self.GetTypeName() + "' object");
            }
        );

        // func_builtins
        funcType.TypeDict["__builtins__"] = new PyMemberDescriptor(
            "__builtins__",
            funcType,
            getter: self => {
                var func = (PyFunction)self;
                PyObject builtins = null;
                func.GlobalsDict?.TryGetValue("__builtins__", out builtins);
                builtins ??= func._inheritedBuiltins ?? PyImportSystem.Import("builtins");
                return builtins is PyModule ? builtins.GetAttribute("__dict__") : builtins;
            }
        );

        // func_get_defaults and func_set_defaults
        funcType.TypeDict["__defaults__"] = new PyGetSetDescriptor(
            "__defaults__",
            funcType,
            getter: self => {
                if (self is PyFunction func)
                    return func._defaults ?? PyNone.Instance;
                throw PyTypeError.Create("descriptor '__defaults__' for 'function' objects doesn't apply to a '" + self.GetTypeName() + "' object");
            },
            setter: (self, value) => {
                if (value is PyNone)
                    ((PyFunction)self)._defaults = null;
                else if (value is PyTuple)
                    ((PyFunction)self)._defaults = value;
                else
                    throw PyTypeError.Create("__defaults__ must be set to a tuple object");
            },
            deleter: self => ((PyFunction)self)._defaults = null
        );

        // func_get_kwdefaults and func_set_kwdefaults
        funcType.TypeDict["__kwdefaults__"] = new PyGetSetDescriptor(
            "__kwdefaults__",
            funcType,
            getter: self => ((PyFunction)self)._kwdefaults ?? PyNone.Instance,
            setter: (self, value) => {
                if (value is PyNone)
                    ((PyFunction)self)._kwdefaults = null;
                else if (value is PyDict)
                    ((PyFunction)self)._kwdefaults = value;
                else
                    throw PyTypeError.Create("__kwdefaults__ must be set to a dict object");
            },
            deleter: self => ((PyFunction)self)._kwdefaults = null
        );

        // func_get_type_params and func_set_type_params
        funcType.TypeDict["__type_params__"] = new PyGetSetDescriptor(
            "__type_params__",
            funcType,
            getter: self => ((PyFunction)self)._typeParams ?? PyTuple.Empty,
            setter: (self, value) => {
                if (value is not PyTuple)
                    throw PyTypeError.Create("__type_params__ must be set to a tuple");
                ((PyFunction)self)._typeParams = value;
            },
            deleter: self => throw PyTypeError.Create("__type_params__ must be set to a tuple")
        );

        // func_getsetlist: {"__dict__", PyObject_GenericGetDict, PyObject_GenericSetDict}.
        funcType.TypeDict["__dict__"] = SubclassInstanceSupport.GenericDictGetSet(funcType);

        // add_operators: tp_call and tp_descr_get
        funcType.TypeDict["__call__"] = new PyWrapperDescriptor("__call__", funcType, (self, args, kwargs) => self.Call(args, kwargs));
        funcType.TypeDict["__get__"] = new PyWrapperDescriptor("__get__", funcType, (self, args, kwargs) =>
        {
            // func_descr_get: anything but None binds, a class too.
            var (instance, _) = TypeTables.DescrGetArguments(args);
            return instance != null ? new PyMethod(instance, self) : self;
        });
    }
    
    private PyObject DefaultImplementation(PyObject[] args)
    {
        #if DEBUG_LOG
        Console.WriteLine($"Function {Name} called with {args.Length} arguments");
        #endif
        return PyNone.Instance;
    }

    public override PyType GetPyType() => PyType.FunctionType;

    // CPython 3.12 호환: kwargs 지원 버전
    public override PyObject Call(PyObject[] args, PyDict kwargs)
    {
        // PyObject_Call -> _PyStack_UnpackDict: the keywords of a dict become names, which must be str.
        if (kwargs != null && kwargs.Length() != 0)
            foreach (var pair in kwargs.InternalDict)
                if (pair.Key is not PyString)
                    throw PyTypeError.Create("keywords must be strings");
        // async generator 함수인지 먼저 확인
        if (CodeObject?.IsAsyncGenerator() == true)
        {
            // async generator 객체 생성 (kwargs는 생성 시 사용하지 않음)
            return CreateAsyncGenerator(args, kwargs);
        }
        // 코루틴 함수인지 확인 (async def, but not async generator)
        else if (CodeObject?.IsCoroutine() == true)
        {
            // 코루틴 객체 생성 (kwargs는 생성 시 사용하지 않음)
            return CreateCoroutine(args, kwargs);
        }
        // 일반 generator 함수인지 확인
        else if (CodeObject?.IsGenerator() == true)
        {
            // 제너레이터 객체 생성 (kwargs 지원 - CPython 3.12 호환)
            return CreateGenerator(args, kwargs);
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

            // CPython 3.12: Pass defaults from func.__defaults__ to PyFrame
            var defaults = GetDefaults();
            #if DEBUG_VM_LOG
            Console.WriteLine($"[PyFunction.Call] Getting defaults for {Name}:");
            Console.WriteLine($"  defaults is null: {defaults == null}");
            if (defaults != null)
            {
                Console.WriteLine($"  defaults.Items.Length: {defaults.Items.Length}");
                for (int i = 0; i < defaults.Items.Length; i++)
                {
                    Console.WriteLine($"  defaults[{i}]: {defaults.Items[i]}");
                }
            }
            #endif

            // CPython 3.12: Create frame with kwargs if present
            // CPython reference: Python/ceval.c:1621-1658 (_PyEvalFramePushAndInit_Ex)
            PyFrame frame;
            PyFrame.CallingQualName = _qualName;

            if (kwargs != null && kwargs.InternalDict.Count > 0)
            {
                // Use PyFrame constructor that accepts kwargs dict
                // This will internally convert it to CPython's (args + kwnames) format
                frame = PyFrame.CreateWithKwargs(CodeObject, args, kwargs, functionScopeChain, Closure, defaults, GetKeywordDefaults());
            }
            else
            {
                frame = new PyFrame(CodeObject, args, functionScopeChain, Closure, null, defaults, GetKeywordDefaults());
            }

            var vm = PyVM.Instance;
            return vm.ExecuteFrame(frame);
        }

        // TODO: kwargs 처리 로직 추가 필요 (현재는 무시)
        return Implementation(args);
    }
    
    /// <summary>
    /// CPython 3.12: Get defaults tuple from func.__defaults__ attribute
    /// </summary>
    internal PyTuple GetDefaults()
    {
        return _defaults as PyTuple;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal PyDict GetKeywordDefaults() => CodeObject?.KwonlyArgCount > 0 ? _kwdefaults as PyDict : null;

    /// <summary>
    /// async generator 객체 생성 - PEP 525 호환
    /// </summary>
    private SharpPy.Core.PyAsyncGenerator CreateAsyncGenerator(PyObject[] args, PyDict kwargs = null)
    {
        // async generator 방식: Frame과 VM을 사용한 실제 async generator
        if (CodeObject == null)
        {
            throw new InvalidOperationException("Cannot create async generator without code object");
        }

        // async generator용 VM 인스턴스 사용
        var vm = PyVM.Instance;

        // CPython 3.12: Pass defaults from func.__defaults__
        var defaults = GetDefaults();

        // async generator 실행용 Frame 생성
        var scope = GlobalsDict != null ? new PyScopeChain(GlobalsDict, CodeObject.Name) : ParentScope;
        var frame = kwargs != null && kwargs.Length() != 0
            ? PyFrame.CreateWithKwargs(CodeObject, args, kwargs, scope, Closure, defaults, GetKeywordDefaults())
            : new PyFrame(CodeObject, args, scope, Closure, null, defaults, GetKeywordDefaults());
        frame.IsGenerator = true;  // CPython 3.12: generator frame 표시

        // async generator enumerator 생성
        return new SharpPy.Core.PyAsyncGenerator(frame, vm, Name, QualName);
    }

    /// <summary>
    /// 코루틴 객체 생성 - PEP 492 호환
    /// </summary>
    private SharpPy.Core.PyCoroutine CreateCoroutine(PyObject[] args, PyDict kwargs = null)
    {
        // CPython 3.12 방식: Frame과 VM을 사용한 실제 코루틴
        if (CodeObject == null)
        {
            throw new InvalidOperationException("Cannot create coroutine without code object");
        }

        // 코루틴용 VM 인스턴스 사용
        var vm = PyVM.Instance;

        // CPython 3.12: Pass defaults from func.__defaults__
        var defaults = GetDefaults();

        // 코루틴 실행용 Frame 생성
        var scope = GlobalsDict != null
            ? new PyScopeChain(GlobalsDict, CodeObject.Name)
            : ParentScope ?? new PyScopeChain();
        var frame = kwargs != null && kwargs.Length() != 0
            ? PyFrame.CreateWithKwargs(CodeObject, args, kwargs, scope, Closure, defaults, GetKeywordDefaults())
            : new PyFrame(CodeObject, args, scope, Closure, null, defaults, GetKeywordDefaults());
        frame.IsCoroutine = true;  // CPython 3.12: coroutine frame 표시

        return new SharpPy.Core.PyCoroutine(frame, vm, Name, QualName);
    }

    /// <summary>
    /// 제너레이터 객체 생성 - CPython 3.12 완전 호환
    /// </summary>
    private PyGenerator CreateGenerator(PyObject[] args, PyDict kwargs = null)
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
#if DEBUG_DEBUG_LOG
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
#endif
        PyScopeChain generatorScopeChain;
        if (GlobalsDict != null)
        {
                // Use the globals captured at function definition time
                generatorScopeChain = new PyScopeChain(GlobalsDict, CodeObject.Name);
            #if DEBUG_DEBUG_LOG
            Console.WriteLine($"  → Using GlobalsDict");
            #endif
        }
        else
        {
                // Fallback to ParentScope for backwards compatibility
                generatorScopeChain = ParentScope ?? new PyScopeChain();
            #if DEBUG_DEBUG_LOG
            Console.WriteLine($"  → Using ParentScope (fallback)");
            #endif
        }

        // CPython 3.12: Pass defaults from func.__defaults__
        var defaults = GetDefaults();

        // CPython 3.12: Create frame with kwargs if present (same as regular function calls)
        PyFrame frame;
        if (kwargs != null && kwargs.InternalDict.Count > 0)
        {
            frame = PyFrame.CreateWithKwargs(CodeObject, args, kwargs, generatorScopeChain, Closure, defaults, GetKeywordDefaults());
        }
        else
        {
            frame = new PyFrame(CodeObject, args, generatorScopeChain, Closure, null, defaults, GetKeywordDefaults());
        }
        frame.IsGenerator = true;  // CPython 3.12: generator frame 표시

        // CPython 3.12 완전 호환 PyGenerator 사용
        return new PyGenerator(frame, vm, Name) { Qualname = PyString.From(QualName) };
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
            
        // A method retains its function and lexical closure when bound.
        // Objects/funcobject.c: func_descr_get returns PyMethod_New(func, obj).
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
    
    // CPython repr(func)/method uses __qualname__, not the bare code name: repr is
    // "<function C.m at 0x..>" (funcobject.c func_repr), bound method is
    // "<bound method C.m of ...>". QualName starts from co_qualname and is what an assignment
    // changes, so repr follows the name the object actually carries.
    public string ReprName => QualName;
    // CPython: repr(func) == "<function QUALNAME at 0xADDR>" (funcobject.c).
    protected internal override PyString Repr()
        => PyString.From($"<function {ReprName} at {PyObjectId.Pointer(this)}>");
    public override string ToString() => $"<function {ReprName}>";
    
}

    // Python의 method 타입 (바인드된 메서드)
    public class PyMethod : PyObject
    {
        public PyObject Instance { get; }

        /// <summary>
        /// im_func. CPython stores any callable here - method_new only checks PyCallable_Check
        /// (Objects/classobject.c:219) - so this is PyObject, not PyFunction. Lib/weakref.py:73
        /// rebuilds a method through type(m)(func, obj), and the probe compares methods whose
        /// "function" is an ordinary object with __call__.
        /// </summary>
        public PyObject Function { get; }

        public PyMethod(PyObject instance, PyObject function)
        {
            Instance = instance;
            Function = function;
        }

        public override PyType GetPyType() => PyType.MethodType; // CPython 3.12: bound method type

        /// <summary>
        /// method_richcompare (Objects/classobject.c:245). Only two bound methods compare, and only
        /// for == and != (:251-:256); the function is compared with == (:259) and the receiver by
        /// identity (:261). An instance's own __eq__ therefore cannot make two bindings equal, and
        /// o.f == Weird().f stays False.
        /// </summary>
        protected internal override PyObject TpRichCompare(PyObject other, CompareOp op)
        {
            if ((op != CompareOp.EQ && op != CompareOp.NE) || !(other is PyMethod bound))
                return PyNotImplemented.Instance;
            // PyObject_RichCompareBool: the very same object is equal without calling anything, and
            // an exception from __eq__ propagates instead of becoming False.
            bool equal = ReferenceEquals(Function, bound.Function) ||
                PyObject.Compare(Function, bound.Function, CompareOp.EQ).IsTrue();
            if (equal) equal = ReferenceEquals(Instance, bound.Instance);
            return op == CompareOp.EQ ? PyBool.FromBool(equal) : PyBool.FromBool(!equal);
        }

        /// <summary>
        /// method_hash (Objects/classobject.c:299): the receiver hashed as an address (:303) mixed
        /// with the function's own hash (:304-:307). PyObjectId is this interpreter's stable identity
        /// number, so two bindings of the same function to the same receiver hash equal - which is
        /// what makes {o.f: 1}[o.f] work - while different receivers hash apart.
        /// </summary>
        public override int ToHash()
        {
            var self = PyObjectId.Of(Instance);
            // PyObject_Hash(im_func) is the Python-level hash: a callable object with __hash__ has to
            // contribute what __hash__ returns, not its CLR identity (probes/l23_method_type.py).
            return (int)(self ^ (self >> 32)) ^ (int)Function.GetPythonHash();
        }

        // CPython 3.12 호환: kwargs 지원 버전
        public override PyObject Call(PyObject[] args, PyDict kwargs)
        {
            // 일반적인 bound method 처리
            var newArgs = new PyObject[args.Length + 1];
            newArgs[0] = Instance;
            Array.Copy(args, 0, newArgs, 1, args.Length);
            return Function.Call(newArgs, kwargs);
        }

        // Method는 항상 호출 가능
        public override bool IsCallable() => true;

        // method_getattro (Objects/classobject.c): what the method type has (_PyType_Lookup and its
        // __get__: __func__, __self__, __doc__, the slot wrappers) answers, anything else is im_func's.
        protected internal override PyObject TpGetattro(string name) =>
            GetPyType().FindNameInMro(name) != null ? base.TpGetattro(name) : Function.GetAttribute(name);

        /// <summary>
        /// method_repr (Objects/classobject.c:273): the name comes from __qualname__, then __name__,
        /// and stays "?" when the callable offers neither (:278-:289).
        /// </summary>
        private string ReprName
        {
            get
            {
                // For a function those names are its own fields, which is how PyFunction already
                // exposes them (core/PyFunction.cs:601).
                if (Function is PyFunction function) return function.ReprName;
                foreach (var slot in new[] { "__qualname__", "__name__" })
                {
                    PyObject value;
                    try { value = Function.GetAttribute(slot); } catch (PythonException) { continue; }
                    if (value is PyString text) return text.Value;
                }
                return "?";
            }
        }

        protected internal override PyString Repr()
            => PyString.From($"<bound method {ReprName} of {Instance.ToRepr().Value}>");
        public override string ToString() => ToRepr().Value;
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
    /// 함수 호출 시 kwargs 검증
    /// </summary>
    public void ValidateKwargs(PyDict kwargs)
    {
        if (KwargsType == null) return; // 타입 검증 없음

        if (!KwargsType.ValidateKwargs(kwargs))
        {
            var typedDict = KwargsType.TypedDict;
            // Performance: Eliminated LINQ - manual loops instead of Where + Select + ToList
            var missing = new List<string>();
            foreach (var key in typedDict.RequiredKeys)
            {
                if (!kwargs.InternalDict.ContainsKey(PyString.From(key)))
                {
                    missing.Add(key);
                }
            }

            var extra = new List<string>();
            foreach (var kvp in kwargs.InternalDict)
            {
                if (kvp.Key is PyString keyStr)
                {
                    string keyValue = keyStr.Value;
                    if (!typedDict.RequiredKeys.Contains(keyValue) && !typedDict.OptionalKeys.Contains(keyValue))
                    {
                        extra.Add(keyValue);
                    }
                }
            }

            var errors = new List<string>();
            if (missing.Count > 0)
                errors.Add($"missing required keys: {string.Join(", ", missing)}");
            if (extra.Count > 0)
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
            // A host delegate's own failure is a RuntimeError; a Python exception it let through
            // stays what it is.
            catch (Exception ex) when (ex is not PythonException)
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
            catch (Exception ex) when (ex is not PythonException)
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
            catch (Exception ex) when (ex is not PythonException)
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
            catch (Exception ex) when (ex is not PythonException)
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
            catch (Exception ex) when (ex is not PythonException)
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
            // A host delegate's own failure is a RuntimeError; a Python exception it let through
            // stays what it is.
            catch (Exception ex) when (ex is not PythonException)
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
            catch (Exception ex) when (ex is not PythonException)
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
            catch (Exception ex) when (ex is not PythonException)
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
            catch (Exception ex) when (ex is not PythonException)
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
/// typing.TypedDict 구현 - PEP 589
/// </summary>
public class PyTypedDict : PyObject
{
    public string Name { get; private set; }
    public HashSet<string> RequiredKeys { get; } = new HashSet<string>();
    public HashSet<string> OptionalKeys { get; } = new HashSet<string>();
    public Dictionary<string, PyObject> KeyTypes { get; } = new Dictionary<string, PyObject>();

    public PyTypedDict(string name)
    {
        Name = name;
    }

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
        // Performance: Eliminated LINQ - manual HashSet construction instead of Concat + ToHashSet
        var allKeys = new HashSet<string>();
        foreach (var key in TypedDict.RequiredKeys)
        {
            allKeys.Add(key);
        }
        foreach (var key in TypedDict.OptionalKeys)
        {
            allKeys.Add(key);
        }

        // 필수 키가 모두 있는지 확인
        foreach (var requiredKey in requiredKeys)
        {
            if (!kwargs.InternalDict.ContainsKey(PyString.From(requiredKey)))
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
