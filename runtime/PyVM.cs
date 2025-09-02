namespace SharpPy
{
    #region Virtual Machine (기존 LEGB 시스템 활용)

    // VM 실행 프레임 (기존 PyScopeChain과 연동)
    public class PyFrame
    {
        public PyCodeObject Code { get; }
        public Stack<PyObject> ValueStack { get; }
        public PyScopeChain ScopeChain { get; }       // 기존 LEGB 시스템 활용!
        public Dictionary<string, PyObject> FastLocals { get; } // 빠른 지역변수 접근
        public int InstructionPointer { get; set; }
        
        // CPython-style closure support
        public PyCell[] Cells { get; set; } = new PyCell[0];     // 클로저 셀들 (freevars + cellvars)
        public PyCell[] Closure { get; set; } = new PyCell[0];   // 부모로부터 받은 클로저 셀들
        
        // CPython-style exception handling support
        public Stack<int> ExceptionHandlers { get; } = new Stack<int>();
        public PyBaseException? LastException { get; set; }
        
        // CPython 3.12 style generator frame state
        public enum FrameState
        {
            Created,     // FRAME_CREATED 
            Executing,   // FRAME_EXECUTING
            Suspended,   // FRAME_SUSPENDED
            Completed    // FRAME_COMPLETED
        }
        
        public FrameState State { get; set; } = FrameState.Created;
        public bool IsGenerator { get; set; } = false;
        public bool IsCoroutine { get; set; } = false;
        
        public PyFrame(PyCodeObject code, PyObject[] args, PyScopeChain parentScope = null, PyCell[] closure = null)
        {
            Console.WriteLine($"🆕 PyFrame 생성: {code.Name}, args={args.Length}개");
            
            Code = code;
            ValueStack = new Stack<PyObject>();
            // 부모 스코프 체인이 있으면 상속, 없으면 새로 생성
            ScopeChain = parentScope ?? new PyScopeChain();
            FastLocals = new Dictionary<string, PyObject>();
            InstructionPointer = 0;
            
            // 클로저 정보 설정
            Closure = closure ?? new PyCell[0];
            
            // Phase 2: CellVars 개수에 따라 Cells 배열 초기화
            if (code.CellVars != null && code.CellVars.Count > 0)
            {
                Cells = new PyCell[code.CellVars.Count];
                for (int i = 0; i < Cells.Length; i++)
                {
                    Cells[i] = new PyCell(); // 빈 셀로 초기화
                }
            }
            else
            {
                Cells = new PyCell[0];
            }
            
            // 함수 스코프 생성
            ScopeChain.PushScope(ScopeType.Local, code.Name);
            
            // CPython 호환: 매개변수 바인딩 (기본값 처리 포함)
            BindArgumentsToParameters(args, code);
        }
        
        /// <summary>
        /// CPython 호환: 인수를 매개변수에 바인딩 (기본값 처리 포함)
        /// </summary>
        private void BindArgumentsToParameters(PyObject[] args, PyCodeObject code)
        {
            Console.WriteLine($"🔗 매개변수 바인딩: {args.Length}개 인수, {code.ArgCount}개 매개변수");
            Console.WriteLine($"  DefaultValues.Count: {code.DefaultValues.Count}");
            for (int j = 0; j < code.DefaultValues.Count; j++)
            {
                Console.WriteLine($"    [{j}]: {code.DefaultValues[j]?.ToString() ?? "null"}");
            }
            
            // CPython처럼 위치 인수 먼저 처리
            for (int i = 0; i < code.ArgCount; i++)
            {
                var paramName = code.VarNames[i];
                Console.WriteLine($"  처리중: 매개변수[{i}] = '{paramName}'");
                
                if (i < args.Length)
                {
                    // 제공된 위치 인수 사용
                    Console.WriteLine($"  → {paramName} = {args[i]} (위치 인수)");
                    FastLocals[paramName] = args[i];
                    ScopeChain.AssignVariable(paramName, args[i]);
                }
                else if (i < code.DefaultValues.Count && code.DefaultValues[i] != null)
                {
                    // 기본값 사용
                    var defaultValue = code.DefaultValues[i];
                    Console.WriteLine($"  → {paramName} = {defaultValue} (기본값)");
                    FastLocals[paramName] = defaultValue;
                    ScopeChain.AssignVariable(paramName, defaultValue);
                }
                else
                {
                    // 필수 매개변수가 누락됨
                    Console.WriteLine($"  ❌ 조건 실패: i({i}) < DefaultValues.Count({code.DefaultValues.Count}) = {i < code.DefaultValues.Count}, DefaultValues[{i}] != null = {(i < code.DefaultValues.Count ? code.DefaultValues[i] != null : "N/A")}");
                    throw PyTypeError.Create($"[PyFrame] missing required argument: '{paramName}'");
                }
            }
            
            // 너무 많은 인수가 제공된 경우 (CPython 호환)
            if (args.Length > code.ArgCount)
            {
                throw PyTypeError.Create($"{code.Name}() takes {code.ArgCount} positional argument{(code.ArgCount != 1 ? "s" : "")} but {args.Length} {(args.Length != 1 ? "were" : "was")} given");
            }
        }
        
        /// <summary>
        /// CPython-style exception handler management
        /// </summary>
        public void PushExceptionHandler(int handlerOffset)
        {
            ExceptionHandlers.Push(handlerOffset);
        }
        
        public void PopExceptionHandler()
        {
            if (ExceptionHandlers.Count > 0)
                ExceptionHandlers.Pop();
        }
        
        public int? GetExceptionHandler()
        {
            return ExceptionHandlers.Count > 0 ? ExceptionHandlers.Peek() : null;
        }
        
        public override string ToString() => $"<frame for {Code.Name}>";
    }

    // Python 가상 머신 (기존 객체 시스템과 완전 통합)
    public class PyVM
    {
        public static PyVM Instance { get; } = new PyVM();
        
        private readonly Stack<PyFrame> _frameStack;
        private readonly PyScopeChain _globalScope;
        
        private PyVM()
        {
            _frameStack = new Stack<PyFrame>();
            _globalScope = new PyScopeChain(); // 기존 LEGB 시스템 사용!
        }
        
        // 메인 모듈 실행
        public PyObject ExecuteModule(PyCodeObject codeObject)
        {
            var frame = new PyFrame(codeObject, new PyObject[0], _globalScope);
            return ExecuteFrame(frame);
        }
        
        // 메인 모듈 실행 (특정 스코프 체인 사용)
        public PyObject ExecuteModule(PyCodeObject codeObject, PyScopeChain scopeChain)
        {
            var frame = new PyFrame(codeObject, new PyObject[0], scopeChain);
            return ExecuteFrame(frame);
        }
        
        // 프레임 실행 (바이트코드 해석)
        // CPython 3.12: Execute class body and return namespace
        public Dictionary<string, PyObject> ExecuteClassBody(PyCodeObject classBody)
        {
            // Get the current frame's scope chain to inherit variables like 'override'
            PyScopeChain parentScope = null;
            if (_frameStack.Count > 0)
            {
                parentScope = _frameStack.Peek().ScopeChain;
            }
            
            var frame = new PyFrame(classBody, new PyObject[0], parentScope);
            var result = ExecuteFrame(frame);
            
            // Extract all local variables from the frame
            var classNamespace = new Dictionary<string, PyObject>();
            
            // Method 1: Try FastLocals
            foreach (var kvp in frame.FastLocals)
            {
                classNamespace[kvp.Key] = kvp.Value;
            }
            
            // Method 2: Try ScopeChain current scope
            if (frame.ScopeChain?.CurrentScope != null)
            {
                foreach (var kvp in frame.ScopeChain.CurrentScope.Variables)
                {
                    classNamespace[kvp.Key] = kvp.Value;
                }
            }
            
            return classNamespace;
        }

        public PyObject ExecuteFrame(PyFrame frame)
        {
            _frameStack.Push(frame);
            
            Console.WriteLine($"\n🚀 VM 실행: {frame}");
            
            try
            {
                while (frame.InstructionPointer < frame.Code.Instructions.Count)
                {
                    var instruction = frame.Code.Instructions[frame.InstructionPointer];
                    
                    if (frame.ValueStack.Count <= 10) // 스택이 너무 크지 않을 때만 출력
                    {
                        var stackContents = string.Join(", ", frame.ValueStack.Reverse().Take(5));
                        Console.WriteLine($"  {frame.InstructionPointer,3}: {instruction,-25} 스택:[{stackContents}]");
                    }
                    
                    try
                    {
                        var result = ExecuteInstruction(frame, instruction);
                        
                        // RETURN_VALUE인 경우 함수 종료
                        if (result != null)
                        {
                            Console.WriteLine($"✅ VM 완료: {result}");
                            return result;
                        }
                        
                        frame.InstructionPointer++;
                    }
                    catch (PythonException pyEx)
                    {
                        // Handle Python exceptions with proper exception handler routing
                        var handlerOffset = frame.GetExceptionHandler();
                        if (handlerOffset.HasValue)
                        {
                            // Push the exception onto the stack and jump to handler
                            frame.ValueStack.Push(pyEx.PyException);
                            frame.LastException = pyEx.PyException;
                            frame.InstructionPointer = handlerOffset.Value;
                        }
                        else
                        {
                            // No handler - re-throw
                            throw;
                        }
                    }
                    catch (LoopBreakException)
                    {
                        // Break: jump to end of current loop
                        // For now, find the next loop end by looking for matching FOR_ITER
                        var loopEnd = FindLoopEnd(frame, frame.InstructionPointer);
                        frame.InstructionPointer = loopEnd;
                    }
                    catch (LoopContinueException)
                    {
                        // Continue: jump to beginning of current loop
                        // For now, find the loop start by looking for matching loop instruction
                        var loopStart = FindLoopStart(frame, frame.InstructionPointer);
                        frame.InstructionPointer = loopStart;
                    }
                }
                
                // 명시적 return이 없으면 None 반환
                Console.WriteLine($"✅ VM 완료: None (암시적)");
                return PyNone.Instance;
            }
            finally
            {
                _frameStack.Pop();
            }
        }
        
        // 개별 명령어 실행 (기존 시스템과 연동)
        private PyObject ExecuteInstruction(PyFrame frame, ByteCodeInstruction instruction)
        {
            switch (instruction.OpCode)
            {
                case ByteCodeOp.NOP:
                    // 아무것도 하지 않음
                    break;
                    
                case ByteCodeOp.POP_TOP:
                    frame.ValueStack.Pop();
                    break;
                    
                case ByteCodeOp.DUP_TOP:
                    var topValue = frame.ValueStack.Peek();
                    frame.ValueStack.Push(topValue);
                    break;
                    
                case ByteCodeOp.ROT_TWO:
                    var second = frame.ValueStack.Pop();
                    var first = frame.ValueStack.Pop();
                    frame.ValueStack.Push(second);
                    frame.ValueStack.Push(first);
                    break;
                    
                case ByteCodeOp.ROT_THREE:
                    var third = frame.ValueStack.Pop();
                    var sec = frame.ValueStack.Pop();
                    var fir = frame.ValueStack.Pop();
                    frame.ValueStack.Push(sec);
                    frame.ValueStack.Push(third);
                    frame.ValueStack.Push(fir);
                    break;
                    
                case ByteCodeOp.LOAD_CONST:
                    var constant = frame.Code.Constants[instruction.Argument];
                    frame.ValueStack.Push(constant);
                    break;
                    
                case ByteCodeOp.LOAD_NAME:
                    var name = frame.Code.Names[instruction.Argument];
                    // 기존 LEGB 시스템 사용!
                    var value = frame.ScopeChain.LookupVariable(name);
                    Console.WriteLine($"🔍 LOAD_NAME({name}): loaded {value?.GetType().Name ?? "null"} value = {value}");
                    frame.ValueStack.Push(value);
                    break;
                
                case ByteCodeOp.LOAD_FAST:
                    var varName = frame.Code.VarNames[instruction.Argument];
                    if (frame.FastLocals.TryGetValue(varName, out var fastValue))
                    {
                        frame.ValueStack.Push(fastValue);
                    }
                    else
                    {
                        throw PyNameError.Create($"local variable '{varName}' referenced before assignment");
                    }
                    break;
                    
                case ByteCodeOp.STORE_NAME:
                    var storeName = frame.Code.Names[instruction.Argument];
                    var storeValue = frame.ValueStack.Pop();
                    // 기존 LEGB 시스템 사용!
                    frame.ScopeChain.AssignVariable(storeName, storeValue);
                    break;
                    
                case ByteCodeOp.LOAD_GLOBAL:
                    var globalName = frame.Code.Names[instruction.Argument];
                    var globalValue = frame.ScopeChain.GlobalScope.GetVariable(globalName) ?? 
                                    frame.ScopeChain.BuiltinModule.GetBuiltin(globalName);
                    if (globalValue == null)
                        throw PyNameError.Create($"name '{globalName}' is not defined");
                    frame.ValueStack.Push(globalValue);
                    break;
                    
                case ByteCodeOp.BINARY_OP:
                    // CPython 3.12+ unified binary operation
                    var operation = (BinaryOpType)instruction.Argument;
                    var right = frame.ValueStack.Pop();
                    var left = frame.ValueStack.Pop();
                    var result = ExecuteBinaryOpType(left, right, operation);
                    frame.ValueStack.Push(result);
                    break;
                    
                // Deprecated individual binary opcodes (kept for compatibility)
                case ByteCodeOp.BINARY_ADD:
                    var rightAdd = frame.ValueStack.Pop();
                    var leftAdd = frame.ValueStack.Pop();
                    var addResult = ExecuteBinaryOpType(leftAdd, rightAdd, BinaryOpType.ADD);
                    frame.ValueStack.Push(addResult);
                    break;
                    
                case ByteCodeOp.BINARY_MULTIPLY:
                    var rightMul = frame.ValueStack.Pop();
                    var leftMul = frame.ValueStack.Pop();
                    var mulResult = ExecuteBinaryOpType(leftMul, rightMul, BinaryOpType.MULTIPLY);
                    frame.ValueStack.Push(mulResult);
                    break;
                    
                case ByteCodeOp.BINARY_SUBTRACT:
                    var rightSub = frame.ValueStack.Pop();
                    var leftSub = frame.ValueStack.Pop();
                    var subResult = ExecuteBinaryOpType(leftSub, rightSub, BinaryOpType.SUBTRACT);
                    frame.ValueStack.Push(subResult);
                    break;
                    
                case ByteCodeOp.BINARY_AND:
                    var rightAnd = frame.ValueStack.Pop();
                    var leftAnd = frame.ValueStack.Pop();
                    var andResult = ExecuteBinaryOpType(leftAnd, rightAnd, BinaryOpType.AND);
                    frame.ValueStack.Push(andResult);
                    break;
                    
                case ByteCodeOp.CALL_FUNCTION:
                    var argCount = instruction.Argument;
                    var args = new PyObject[argCount];
                    for (int i = argCount - 1; i >= 0; i--)
                    {
                        args[i] = frame.ValueStack.Pop();
                    }
                    var function = frame.ValueStack.Pop();
                    
                    // 기존 PyObject.Call() 시스템 사용!
                    var callResult = function.Call(args);
                    frame.ValueStack.Push(callResult);
                    break;
                    
                case ByteCodeOp.CALL_FUNCTION_KW:
                    var positionalArgCount = instruction.Argument;
                    
                    // 키워드 이름 튜플 (스택 맨 위)
                    var keywordNamesTuple = frame.ValueStack.Pop() as PyTuple;
                    if (keywordNamesTuple == null)
                        throw PyTypeError.Create("keyword names must be a tuple");
                    
                    var keywordCount = keywordNamesTuple.Items.Length;
                    
                    // 키워드 인수 값들 (키워드 개수만큼)
                    var keywordValues = new PyObject[keywordCount];
                    for (int i = keywordCount - 1; i >= 0; i--)
                    {
                        keywordValues[i] = frame.ValueStack.Pop();
                    }
                    
                    // 위치 인수들
                    var positionalArgs = new PyObject[positionalArgCount];
                    for (int i = positionalArgCount - 1; i >= 0; i--)
                    {
                        positionalArgs[i] = frame.ValueStack.Pop();
                    }
                    
                    var kwFunction = frame.ValueStack.Pop();
                    
                    // 키워드 인수 딕셔너리 생성
                    var kwargs = new Dictionary<string, PyObject>();
                    for (int i = 0; i < keywordCount; i++)
                    {
                        var keyName = keywordNamesTuple.Items[i].ToStr();
                        kwargs[keyName] = keywordValues[i];
                    }
                    
                    // 키워드 인수를 지원하는 함수 호출
                    var kwCallResult = CallFunctionWithKwargs(kwFunction, positionalArgs, kwargs);
                    frame.ValueStack.Push(kwCallResult);
                    break;
                    
                case ByteCodeOp.MAKE_FUNCTION:
                    // CPython-style function creation with closure support
                    var flags = instruction.Argument;
                    var codeObject = frame.ValueStack.Pop();
                    
                    PyCell[] closure = null;
                    PyTuple defaults = null;
                    
                    // Check for default parameters flag (1 = MAKE_FUNCTION_DEFAULTS) - CPython order
                    if ((flags & 1) != 0)
                    {
                        var defaultsTuple = frame.ValueStack.Pop();
                        if (defaultsTuple is PyTuple defTuple)
                        {
                            defaults = defTuple;
                            Console.WriteLine($"  → Creating function with {defTuple.Items.Length} default parameters");
                        }
                        else
                        {
                            Console.WriteLine($"  ⚠️ Warning: Expected tuple for defaults, got {defaultsTuple?.GetType()}");
                            defaults = new PyTuple(new PyObject[0]);
                        }
                    }
                    
                    // Check for closure flag (8 = MAKE_FUNCTION_CLOSURE)
                    if ((flags & 8) != 0)
                    {
                        var closureTuple = frame.ValueStack.Pop();
                        if (closureTuple is PyTuple closureTupleObj)
                        {
                            closure = closureTupleObj.Items.Cast<PyCell>().ToArray();
                            Console.WriteLine($"  → Creating function with closure: {closure.Length} cells");
                        }
                        else
                        {
                            Console.WriteLine($"  ⚠️ Warning: Expected tuple for closure, got {closureTuple?.GetType()}");
                            closure = new PyCell[0];
                        }
                    }
                    
                    if (codeObject is PyCodeObject pyCode)
                    {
                        // CPython 3.12: async def로 정의된 함수인지 확인
                        if (pyCode.IsCoroutine())
                        {
                            // Async function: 호출 시 PyCoroutine 객체 반환
                            var asyncImpl = new Func<PyObject[], PyObject>(args =>
                            {
                                var boundArgs = BindFunctionArguments(args, pyCode, defaults);
                                var asyncFrame = closure != null && closure.Length > 0 
                                    ? new PyFrame(pyCode, boundArgs, frame.ScopeChain, closure)
                                    : new PyFrame(pyCode, boundArgs, frame.ScopeChain);
                                
                                // Native coroutine 생성
                                return new SharpPy.Core.PyCoroutine(asyncFrame, this, pyCode.Name);
                            });
                            
                            var asyncFunction = new PyFunction(pyCode.Name, asyncImpl, null, null, closure, pyCode);
                            
                            if (defaults != null)
                            {
                                asyncFunction.SetAttribute("__defaults__", defaults);
                            }
                            
                            frame.ValueStack.Push(asyncFunction);
                            Console.WriteLine($"✅ Created async function: {pyCode.Name}");
                        }
                        else
                        {
                            // Regular function
                            PyFunction functionObject;
                            
                            // Create function implementation with proper parameter binding
                            Func<PyObject[], PyObject> implementation = args =>
                            {
                            // Apply CPython-style parameter binding with defaults
                            var boundArgs = BindFunctionArguments(args, pyCode, defaults);
                            // Create frame with closure support if needed
                            var functionFrame = closure != null && closure.Length > 0 
                                ? new PyFrame(pyCode, boundArgs, frame.ScopeChain, closure)
                                : new PyFrame(pyCode, boundArgs, frame.ScopeChain);
                            return ExecuteFrame(functionFrame);
                        };
                        
                        if (closure != null && closure.Length > 0)
                        {
                            // Create function with closure
                            functionObject = PyFunction.CreateClosureFunction(pyCode.Name, pyCode, closure, frame.ScopeChain);
                            // Override implementation to use our parameter binding
                            functionObject = new PyFunction(pyCode.Name, implementation, null, null, closure, pyCode);
                        }
                        else
                        {
                            // Create regular function without closure
                            functionObject = new PyFunction(pyCode.Name, implementation, null, null, closure, pyCode);
                        }
                        
                            // Set __defaults__ attribute following CPython
                            if (defaults != null)
                            {
                                functionObject.SetAttribute("__defaults__", defaults);
                            }
                            
                            frame.ValueStack.Push(functionObject);
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException($"MAKE_FUNCTION expected code object, got {codeObject?.GetType()}");
                    }
                    break;
                    
                case ByteCodeOp.LOAD_ATTR:
                    var attrName = frame.Code.Names[instruction.Argument];
                    var obj = frame.ValueStack.Pop();
                    // 기존 Attribute 시스템 사용!
                    var attr = obj.GetAttribute(attrName);
                    frame.ValueStack.Push(attr);
                    break;
                    
                case ByteCodeOp.STORE_ATTR:
                    var setAttrName = frame.Code.Names[instruction.Argument];
                    var setObj = frame.ValueStack.Pop();
                    var setAttrValue = frame.ValueStack.Pop();
                    // 기존 Attribute 시스템 사용!
                    setObj.SetAttribute(setAttrName, setAttrValue);
                    break;
                    
                case ByteCodeOp.RETURN_VALUE:
                    var returnValue = frame.ValueStack.Count > 0 ? frame.ValueStack.Pop() : PyNone.Instance;
                    return returnValue;
                    
                case ByteCodeOp.GET_AWAITABLE:
                    // CPython 3.12 GET_AWAITABLE 구현
                    var awaitableObj = frame.ValueStack.Pop();
                    var awaitable = GetAwaitable(awaitableObj);
                    frame.ValueStack.Push(awaitable);
                    break;

                // CPython-style Control Flow Opcodes (Phase 1 - High Priority)
                case ByteCodeOp.POP_JUMP_IF_TRUE:
                    var truthValue = frame.ValueStack.Pop();
                    if (truthValue.PyBoolValue())
                    {
                        frame.InstructionPointer = instruction.Argument - 1; // -1 because main loop will increment
                        return null; // Continue execution from new position
                    }
                    break;
                    
                case ByteCodeOp.POP_JUMP_IF_FALSE:
                    var falseValue = frame.ValueStack.Pop();
                    if (!falseValue.PyBoolValue())
                    {
                        frame.InstructionPointer = instruction.Argument - 1; // -1 because main loop will increment
                        return null; // Continue execution from new position
                    }
                    break;
                    
                case ByteCodeOp.JUMP_FORWARD:
                    frame.InstructionPointer = instruction.Argument - 1; // -1 because main loop will increment
                    return null; // Continue execution from new position
                    
                case ByteCodeOp.JUMP_BACKWARD:
                    // JUMP_BACKWARD uses relative offset - jump back by the specified amount
                    // CPython-compatible: jump to the exact instruction specified by the argument
                    // -1 because main loop will increment InstructionPointer
                    frame.InstructionPointer = frame.InstructionPointer - instruction.Argument - 1;
                    Console.WriteLine($"🔄 JUMP_BACKWARD: from {frame.InstructionPointer + instruction.Argument + 1} to {frame.InstructionPointer + 1} (next: {frame.InstructionPointer + 1})");
                    return null; // Continue execution from new position

                // CPython-style Container Building Opcodes (Phase 1)
                case ByteCodeOp.BUILD_LIST:
                    var listSize = instruction.Argument;
                    var listItems = new List<PyObject>();
                    for (int i = 0; i < listSize; i++)
                    {
                        listItems.Insert(0, frame.ValueStack.Pop()); // Reverse order
                    }
                    var pyList = new PyList();
                    foreach (var item in listItems)
                    {
                        // Use Append method instead of Add to avoid + operator
                        pyList.Append(item);
                    }
                    frame.ValueStack.Push(pyList);
                    break;
                    
                case ByteCodeOp.BUILD_TUPLE:
                    var tupleSize = instruction.Argument;
                    var tupleItems = new PyObject[tupleSize];
                    for (int i = tupleSize - 1; i >= 0; i--)
                    {
                        tupleItems[i] = frame.ValueStack.Pop();
                    }
                    var pyTuple = new PyTuple(tupleItems);
                    frame.ValueStack.Push(pyTuple);
                    break;
                    
                case ByteCodeOp.LOAD_SUBSCR:
                    // Stack: [object, key] -> [object[key]]
                    // CPython-style subscript access for list pattern matching
                    var subscriptKey = frame.ValueStack.Pop();
                    var subscriptObj = frame.ValueStack.Pop();
                    
                    try
                    {
                        PyObject subscriptResult;
                        if (subscriptObj is PyList subscriptList && subscriptKey is PyInt keyIntValue)
                        {
                            // List indexing: list[int]
                            subscriptResult = subscriptList.GetItem(keyIntValue.Value);
                        }
                        else if (subscriptObj is PyDict subscriptDict)
                        {
                            // Dictionary access: dict[key]
                            subscriptResult = subscriptDict.GetItem(subscriptKey);
                        }
                        else if (subscriptObj is PyType subscriptType)
                        {
                            // Type subscript access: Type[args] (for generics like Unpack[T], Required[T], etc.)
                            subscriptResult = subscriptType.GetItem(subscriptKey);
                        }
                        else
                        {
                            // Try generic GetItem method
                            try
                            {
                                subscriptResult = subscriptObj.GetItem(subscriptKey);
                            }
                            catch (NotImplementedException)
                            {
                                throw PyTypeError.Create($"'{subscriptObj.GetTypeName()}' object is not subscriptable");
                            }
                        }
                        
                        frame.ValueStack.Push(subscriptResult);
                    }
                    catch (Exception ex) when (ex is PyException)
                    {
                        // Re-throw Python exceptions
                        Console.WriteLine($"🔍 LOAD_SUBSCR: Re-throwing Python exception: {ex.GetType().Name} - {ex.Message}");
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // Convert C# exceptions to appropriate Python exceptions (CPython 호환)
                        if (ex.Message.Contains("key") || ex.Message.Contains("Key"))
                        {
                            // Key not found → KeyError (CPython 방식)
                            throw PyKeyError.Create(ex.Message.Replace("subscript error: ", ""));
                        }
                        else if (ex.Message.Contains("index") || ex.Message.Contains("range"))
                        {
                            // Index out of range → IndexError (CPython 방식)  
                            throw PyIndexError.Create(ex.Message.Replace("subscript error: ", ""));
                        }
                        else
                        {
                            // Other subscript errors → TypeError
                            throw PyTypeError.Create($"subscript error: {ex.Message}");
                        }
                    }
                    break;
                    
                case ByteCodeOp.STORE_SUBSCR:
                    // Stack: [value, object, key] -> []  
                    // Implements obj[key] = value
                    var subscrStoreKey = frame.ValueStack.Pop();      // key (top of stack)
                    var subscrStoreObj = frame.ValueStack.Pop();      // object 
                    var subscrStoreValue = frame.ValueStack.Pop();    // value (bottom)
                    
                    try
                    {
                        subscrStoreObj.SetItem(subscrStoreKey, subscrStoreValue);
                    }
                    catch (Exception ex) when (ex is PyException)
                    {
                        // Re-throw Python exceptions
                        Console.WriteLine($"🔍 STORE_SUBSCR: Re-throwing Python exception: {ex.GetType().Name} - {ex.Message}");
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // Convert C# exceptions to appropriate Python exceptions
                        throw PyTypeError.Create($"subscript assignment error: {ex.Message}");
                    }
                    break;
                    
                case ByteCodeOp.BUILD_SET:
                    var setSize = instruction.Argument;
                    var pySet = new PySet();
                    for (int i = 0; i < setSize; i++)
                    {
                        pySet.Add(frame.ValueStack.Pop());
                    }
                    frame.ValueStack.Push(pySet);
                    break;
                    
                case ByteCodeOp.BUILD_MAP:
                    var mapSize = instruction.Argument;
                    var pyDict = new PyDict();
                    for (int i = 0; i < mapSize; i++)
                    {
                        var mapValue = frame.ValueStack.Pop();
                        var mapKey = frame.ValueStack.Pop();
                        pyDict.SetItem(mapKey, mapValue);
                    }
                    frame.ValueStack.Push(pyDict);
                    break;

                // CPython-style Iterator Opcodes
                case ByteCodeOp.GET_ITER:
                    var iterable = frame.ValueStack.Pop();
                    var iterator = iterable.GetIterator();
                    frame.ValueStack.Push(iterator);
                    break;
                    
                case ByteCodeOp.FOR_ITER:
                    // CPython 3.12 compatible FOR_ITER implementation
                    var iter = frame.ValueStack.Peek(); // Keep iterator on stack for inspection
                    try
                    {
                        var nextItem = iter.Next();
                        frame.ValueStack.Push(nextItem); // Push next item on top of iterator
                        Console.WriteLine($"🔄 FOR_ITER: got next item {nextItem} from iterator");
                        // Continue normal execution (don't jump)
                    }
                    catch (PythonException ex) when (ex.PyException is PyStopIteration)
                    {
                        Console.WriteLine($"🔚 FOR_ITER: StopIteration - loop finished");
                        frame.ValueStack.Pop(); // Remove iterator from stack
                        
                        // CPython 3.12: Jump forward by delta (relative jump from next instruction)
                        // Current position + 1 (next instruction) + delta - 1 (main loop will increment)
                        frame.InstructionPointer += instruction.Argument;
                        Console.WriteLine($"🔚 FOR_ITER: Jumping to position {frame.InstructionPointer + 1}");
                        
                        // DON'T return null - continue execution
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"💥 FOR_ITER error: {ex.Message}");
                        throw;
                    }
                    break;

                // CPython-style Comparison Operations
                case ByteCodeOp.COMPARE_OP:
                    var compareRight = frame.ValueStack.Pop();
                    var compareLeft = frame.ValueStack.Pop();
                    var compareResult = CompareOperation(compareLeft, compareRight, instruction.Argument);
                    frame.ValueStack.Push(compareResult);
                    break;

                // CPython-style Unary Operations
                case ByteCodeOp.UNARY_POSITIVE:
                    var posValue = frame.ValueStack.Pop();
                    frame.ValueStack.Push(posValue.Positive());
                    break;
                    
                case ByteCodeOp.UNARY_NEGATIVE:
                    var negValue = frame.ValueStack.Pop();
                    frame.ValueStack.Push(negValue.Negative());
                    break;
                    
                case ByteCodeOp.UNARY_NOT:
                    var notValue = frame.ValueStack.Pop();
                    var boolResult = notValue.PyBoolValue() ? PyBool.False : PyBool.True;
                    frame.ValueStack.Push(boolResult);
                    break;

                // CPython-style Exception Handling (Basic)
                case ByteCodeOp.SETUP_EXCEPT:
                    frame.PushExceptionHandler(instruction.Argument);
                    break;
                    
                case ByteCodeOp.POP_EXCEPT:
                    frame.PopExceptionHandler();
                    break;
                    
                case ByteCodeOp.BEFORE_WITH:
                    // CPython 3.12: BEFORE_WITH performs several operations before a with block starts
                    var contextManager = frame.ValueStack.Pop();
                    
                    // 1. Load __exit__ method and push to stack (for later cleanup)
                    var exitMethod = contextManager.GetAttribute("__exit__");
                    if (!exitMethod.IsCallable())
                    {
                        throw PyAttributeError.Create("__exit__");
                    }
                    frame.ValueStack.Push(exitMethod);
                    
                    // 2. Call __enter__ method and push result to stack
                    var enterMethod = contextManager.GetAttribute("__enter__");
                    if (enterMethod.IsCallable())
                    {
                        var enterResult = enterMethod.Call(new PyObject[0]);
                        frame.ValueStack.Push(enterResult);
                    }
                    else
                    {
                        throw PyAttributeError.Create("__enter__");
                    }
                    break;
                    
                case ByteCodeOp.WITH_EXCEPT_START:
                    // CPython 3.12: Called when exception occurs in with block
                    // Stack layout: [..., __exit__ method, exception info]
                    
                    // In exception handler, we expect exception is already on top of stack
                    // and __exit__ method was preserved from BEFORE_WITH
                    if (frame.ValueStack.Count < 2)
                    {
                        // Push False to indicate we can't handle the exception
                        frame.ValueStack.Push(PyBool.False);
                        break;
                    }
                    
                    // Get the exception - in our simplified case it's the top of stack
                    var currentException = frame.ValueStack.Peek();
                    
                    // Find the __exit__ method - it should be preserved somewhere in stack
                    // For now, convert stack to array for indexing (this may need adjustment based on actual stack layout)
                    PyObject exitMethodForExcept = null;
                    if (frame.ValueStack.Count >= 2)
                    {
                        var stackArray = frame.ValueStack.ToArray();
                        exitMethodForExcept = stackArray[stackArray.Length - 2];
                    }
                    
                    if (exitMethodForExcept?.IsCallable() == true)
                    {
                        // Get exception info - CPython style
                        PyObject excType, excValue, excTb;
                        
                        if (currentException is PyException pyExc)
                        {
                            excType = pyExc.GetPyType();
                            excValue = pyExc;
                            excTb = PyNone.Instance; // traceback not implemented yet
                        }
                        else
                        {
                            excType = currentException.GetPyType();
                            excValue = currentException;
                            excTb = PyNone.Instance;
                        }
                        
                        // Call __exit__(exc_type, exc_value, traceback)
                        var exitArgs = new PyObject[] { excType, excValue, excTb };
                        var exitResult = exitMethodForExcept.Call(exitArgs);
                        
                        // Push result for caller to check - in CPython this determines exception suppression
                        frame.ValueStack.Push(exitResult);
                        
                        // Note: The caller (compiler) decides whether to suppress based on exitResult
                    }
                    else
                    {
                        // Push False to indicate we can't handle the exception
                        frame.ValueStack.Push(PyBool.False);
                    }
                    break;
                    
                case ByteCodeOp.EXCEPT_MATCH:
                    var exceptionType = frame.ValueStack.Pop();
                    var exception = frame.ValueStack.Peek(); // Don't pop, keep for handler
                    var matches = ExceptionMatches(exception, exceptionType);
                    frame.ValueStack.Push(PyBool.FromBool(matches));
                    break;
                    
                case ByteCodeOp.CHECK_EG_MATCH:
                    // PEP 654: ExceptionGroup matching
                    var egType = frame.ValueStack.Pop();
                    var egException = frame.ValueStack.Pop();
                    var (matched, remainder) = ExceptionGroupMatches(egException, egType);
                    frame.ValueStack.Push(matched ?? PyNone.Instance);
                    frame.ValueStack.Push(remainder ?? PyNone.Instance);
                    break;
                    
                case ByteCodeOp.RAISE_VARARGS:
                    // instruction.Argument indicates the number of arguments to the raise statement
                    if (instruction.Argument == 1)
                    {
                        // raise exception_instance
                        var raisedException = frame.ValueStack.Pop();
                        if (raisedException is PyException pyEx)
                        {
                            frame.LastException = pyEx;
                            throw new PythonException(pyEx);
                        }
                        else
                        {
                            throw new PythonException(new PyTypeError($"exceptions must derive from BaseException"));
                        }
                    }
                    else if (instruction.Argument == 0)
                    {
                        // bare raise - same as RERAISE
                        if (frame.LastException != null)
                            throw new PythonException(frame.LastException);
                        else
                            throw new PythonException(new PyRuntimeError("No active exception to re-raise"));
                    }
                    break;
                    
                case ByteCodeOp.RERAISE:
                    if (frame.LastException != null)
                        throw new PythonException(frame.LastException);
                    break;

                // F-String Support (PEP 701)
                case ByteCodeOp.FORMAT_VALUE:
                    var formatOption = instruction.Argument;
                    
                    PyString formattedString;
                    
                    if (formatOption == 4) // 포맷 지정자 있음
                    {
                        // 스택 순서: [값, 포맷스펙] -> 포맷스펙을 먼저 pop
                        var formatSpec = frame.ValueStack.Pop();
                        var formatValue = frame.ValueStack.Pop();
                        formattedString = ApplyFormatting(formatValue, formatSpec.ToStr());
                    }
                    else
                    {
                        // 기본 포맷팅
                        var formatValue = frame.ValueStack.Pop();
                        formattedString = new PyString(formatValue.ToStr());
                    }
                    
                    frame.ValueStack.Push(formattedString);
                    break;
                    
                case ByteCodeOp.BUILD_STRING:
                    var stringCount = instruction.Argument;
                    var stringParts = new List<string>();
                    for (int i = 0; i < stringCount; i++)
                    {
                        var part = frame.ValueStack.Pop();
                        stringParts.Insert(0, part.ToString()); // Reverse order
                    }
                    var concatenatedString = new PyString(string.Join("", stringParts));
                    frame.ValueStack.Push(concatenatedString);
                    break;
                    
                case ByteCodeOp.UNPACK_SEQUENCE:
                    // CPython 3.12 compatible tuple/sequence unpacking
                    var unpackCount = instruction.Argument;
                    var sequence = frame.ValueStack.Pop();
                    
                    // Unpack the sequence into individual elements
                    if (sequence is PyTuple tuple)
                    {
                        if (tuple.Items.Length != unpackCount)
                        {
                            throw PyValueError.Create($"not enough values to unpack (expected {unpackCount}, got {tuple.Items.Length})");
                        }
                        
                        // CPython pushes elements in reverse order (last element pushed first)
                        // So when popped, they come out in correct order for assignment
                        for (int i = tuple.Items.Length - 1; i >= 0; i--)
                        {
                            frame.ValueStack.Push(tuple.Items[i]);
                        }
                    }
                    else if (sequence is PyList list)
                    {
                        if (list.Items.Length != unpackCount)
                        {
                            throw PyValueError.Create($"not enough values to unpack (expected {unpackCount}, got {list.Items.Length})");
                        }
                        
                        // CPython pushes elements in reverse order
                        for (int i = list.Items.Length - 1; i >= 0; i--)
                        {
                            frame.ValueStack.Push(list.Items[i]);
                        }
                    }
                    else if (sequence is PyString str)
                    {
                        if (str.Value.Length != unpackCount)
                        {
                            throw PyValueError.Create($"not enough values to unpack (expected {unpackCount}, got {str.Value.Length})");
                        }
                        
                        // CPython pushes characters in reverse order
                        for (int i = str.Value.Length - 1; i >= 0; i--)
                        {
                            frame.ValueStack.Push(new PyString(str.Value[i].ToString()));
                        }
                    }
                    else
                    {
                        throw PyTypeError.Create($"cannot unpack non-sequence {sequence.GetTypeName()}");
                    }
                    break;
                    
                // Loop Control Statements
                case ByteCodeOp.BREAK_LOOP:
                    // Break from the current loop by jumping to loop end
                    // In CPython, this pops the loop block and jumps
                    // For simplicity, we'll use a special exception mechanism
                    throw new LoopBreakException();
                    
                case ByteCodeOp.CONTINUE_LOOP:
                    // Continue to the next iteration of the loop
                    // In CPython, this jumps to the loop beginning
                    throw new LoopContinueException();
                    
                case ByteCodeOp.IMPORT_NAME:
                    var moduleName = ((PyString)frame.Code.Constants[instruction.Argument]).Value;
                    var importedModule = PyImportSystem.Import(moduleName);
                    frame.ValueStack.Push(importedModule);
                    break;
                    
                case ByteCodeOp.IMPORT_FROM:
                    var itemName = ((PyString)frame.Code.Constants[instruction.Argument]).Value;
                    if (frame.ValueStack.Count == 0)
                    {
                        throw new Exception($"IMPORT_FROM: Stack empty when trying to import '{itemName}'. This may be caused by incorrect bytecode generation.");
                    }
                    var module = frame.ValueStack.Peek(); // Don't pop yet, needed for multiple imports
                    var importedItem = module.GetAttribute(itemName);
                    frame.ValueStack.Push(importedItem);
                    break;
                    
                // PEP 709 Comprehension Optimization - VM 구현
                case ByteCodeOp.LIST_APPEND:
                    // CPython 호환: LIST_APPEND i
                    // 스택에서 top 아이템을 pop하고, top에서 i번째 아래 리스트에 append
                    // 스택: [..., list, ..., item] → [..., list, ...]
                    var itemToAppend = frame.ValueStack.Pop();
                    var stackItems = frame.ValueStack.ToArray();
                    Array.Reverse(stackItems); // 스택 bottom부터 top 순서로 변경
                    
                    // CPython LIST_APPEND i: 스택에서 아이템을 pop한 후,
                    // 현재 스택 top에서 i-1번째 아래가 타겟 (0-based)
                    var targetIndex = instruction.Argument - 1;
                    
                    if (targetIndex < 0 || targetIndex >= stackItems.Length)
                    {
                        throw new Exception($"LIST_APPEND: invalid target index {targetIndex}, stack length {stackItems.Length}");
                    }
                    
                    var targetList = stackItems[targetIndex];
                    
                    if (targetList is PyList targetPyList)
                    {
                        targetPyList.Append(itemToAppend);
                    }
                    else
                    {
                        throw new Exception($"LIST_APPEND: target is not a list, got {targetList?.GetTypeName() ?? "null"}");
                    }
                    
                    // 스택은 그대로 유지 (아이템만 제거됨)
                    break;
                    
                case ByteCodeOp.SET_ADD:
                    // CPython 호환: SET_ADD i
                    // 스택: [..., set, ..., item] → [..., set, ...]
                    var setItem = frame.ValueStack.Pop();
                    var setStackArray = frame.ValueStack.ToArray();
                    Array.Reverse(setStackArray); // CPython 호환 스택 순서
                    
                    if (instruction.Argument > 0 && instruction.Argument <= setStackArray.Length)
                    {
                        var targetSet = setStackArray[instruction.Argument - 1];
                        if (targetSet is PySet targetPySet)
                        {
                            targetPySet.Add(setItem);
                        }
                        else
                        {
                            throw new Exception($"SET_ADD: target is not a set, got {targetSet.GetType().Name}");
                        }
                    }
                    else
                    {
                        throw new Exception($"SET_ADD: invalid stack position {instruction.Argument}");
                    }
                    break;
                    
                case ByteCodeOp.MAP_ADD:
                    // CPython 호환: MAP_ADD i
                    // 스택: [..., dict, ..., key, value] → [..., dict, ...]
                    var dictValue = frame.ValueStack.Pop();
                    var dictKey = frame.ValueStack.Pop();
                    var dictStackArray = frame.ValueStack.ToArray();
                    Array.Reverse(dictStackArray); // CPython 호환 스택 순서
                    
                    if (instruction.Argument > 0 && instruction.Argument <= dictStackArray.Length)
                    {
                        var targetDict = dictStackArray[instruction.Argument - 1];
                        if (targetDict is PyDict targetPyDict)
                        {
                            targetPyDict.InternalDict[dictKey] = dictValue;
                        }
                        else
                        {
                            throw new Exception($"MAP_ADD: target is not a dict, got {targetDict.GetType().Name}");
                        }
                    }
                    else
                    {
                        throw new Exception($"MAP_ADD: invalid stack position {instruction.Argument}");
                    }
                    break;

                // === Closure Support Bytecodes (CPython 호환) ===
                case ByteCodeOp.LOAD_DEREF:
                    // 클로저/자유 변수에서 값 로드
                    // argument는 (freevars + cellvars)에서의 인덱스
                    var cellIndex = instruction.Argument;
                    
                    Console.WriteLine($"🔍 LOAD_DEREF cell index {cellIndex}");
                    Console.WriteLine($"   Frame has {frame.Closure.Length} closure cells and {frame.Cells.Length} local cells");
                    
                    PyCell cell;
                    if (cellIndex < frame.Closure.Length)
                    {
                        // 부모로부터 받은 클로저 셀
                        cell = frame.Closure[cellIndex];
                        Console.WriteLine($"   → Using closure cell[{cellIndex}]: {(cell.HasValue ? cell.Value : "empty")}");
                    }
                    else
                    {
                        // 로컬 셀 (현재 미구현 - Phase 2에서 구현)
                        var localIndex = cellIndex - frame.Closure.Length;
                        if (localIndex < frame.Cells.Length)
                        {
                            cell = frame.Cells[localIndex];
                            Console.WriteLine($"   → Using local cell[{localIndex}]: {(cell.HasValue ? cell.Value : "empty")}");
                        }
                        else
                        {
                            throw new Exception($"LOAD_DEREF: invalid cell index {cellIndex}");
                        }
                    }
                    
                    if (cell.HasValue)
                    {
                        frame.ValueStack.Push(cell.Value!);
                        Console.WriteLine($"   ✅ Loaded value: {cell.Value}");
                    }
                    else
                    {
                        Console.WriteLine($"   ❌ Cell is empty!");
                        throw PyNameError.Create("local variable referenced before assignment");
                    }
                    break;
                    
                case ByteCodeOp.STORE_DEREF:
                    // 클로저/자유 변수에 값 저장
                    var storeCellIndex = instruction.Argument;
                    var storeDerefValue = frame.ValueStack.Pop();
                    
                    PyCell storeCell;
                    if (storeCellIndex < frame.Closure.Length)
                    {
                        // 부모로부터 받은 클로저 셀
                        storeCell = frame.Closure[storeCellIndex];
                    }
                    else
                    {
                        // 로컬 셀 (현재 미구현 - Phase 2에서 구현)
                        var localStoreIndex = storeCellIndex - frame.Closure.Length;
                        if (localStoreIndex < frame.Cells.Length)
                        {
                            storeCell = frame.Cells[localStoreIndex];
                        }
                        else
                        {
                            throw new Exception($"STORE_DEREF: invalid cell index {storeCellIndex}");
                        }
                    }
                    
                    storeCell.SetValue(storeDerefValue);
                    break;
                    
                case ByteCodeOp.DELETE_DEREF:
                    // 클로저/자유 변수 삭제
                    var deleteCellIndex = instruction.Argument;
                    
                    PyCell deleteCell;
                    if (deleteCellIndex < frame.Closure.Length)
                    {
                        deleteCell = frame.Closure[deleteCellIndex];
                    }
                    else
                    {
                        var localDeleteIndex = deleteCellIndex - frame.Closure.Length;
                        if (localDeleteIndex < frame.Cells.Length)
                        {
                            deleteCell = frame.Cells[localDeleteIndex];
                        }
                        else
                        {
                            throw new Exception($"DELETE_DEREF: invalid cell index {deleteCellIndex}");
                        }
                    }
                    
                    deleteCell.Clear();
                    break;
                    
                case ByteCodeOp.LOAD_CLOSURE:
                    // 클로저 셀 로드 (함수 생성용)
                    // 현재는 기본 구현만 제공 (Phase 2에서 완전 구현)
                    var closureCellIndex = instruction.Argument;
                    
                    Console.WriteLine($"🔐 LOAD_CLOSURE cell index {closureCellIndex}");
                    Console.WriteLine($"   Frame has {frame.Closure.Length} closure cells and {frame.Cells.Length} local cells");
                    
                    PyCell closureCell;
                    if (closureCellIndex < frame.Closure.Length)
                    {
                        closureCell = frame.Closure[closureCellIndex];
                        Console.WriteLine($"   → Using closure cell[{closureCellIndex}]: {(closureCell.HasValue ? closureCell.Value : "empty")}");
                    }
                    else
                    {
                        var localClosureIndex = closureCellIndex - frame.Closure.Length;
                        if (localClosureIndex < frame.Cells.Length)
                        {
                            closureCell = frame.Cells[localClosureIndex];
                            Console.WriteLine($"   → Using local cell[{localClosureIndex}]: {(closureCell.HasValue ? closureCell.Value : "empty")}");
                        }
                        else
                        {
                            // 새 셀 생성 (Phase 1 임시 구현)
                            closureCell = new PyCell();
                            Console.WriteLine($"   ⚠️  Creating new empty cell - local index {localClosureIndex} >= {frame.Cells.Length}");
                        }
                    }
                    
                    frame.ValueStack.Push(closureCell);
                    break;
                    
                case ByteCodeOp.MAKE_CELL:
                    // Phase 2: 지역 변수를 셀로 변환 (완전 구현)
                    var paramIndex = instruction.Argument;
                    var makeVarName = frame.Code.VarNames[paramIndex];
                    
                    Console.WriteLine($"🔧 MAKE_CELL for '{makeVarName}' at index {paramIndex}");
                    Console.WriteLine($"   FastLocals contains '{makeVarName}': {frame.FastLocals.ContainsKey(makeVarName)}");
                    
                    // 매개변수 값을 가져와서 Cell에 저장
                    PyObject? cellValue = null;
                    if (frame.FastLocals.TryGetValue(makeVarName, out var localValue))
                    {
                        cellValue = localValue;
                        Console.WriteLine($"   Found value for '{makeVarName}': {cellValue}");
                    }
                    else
                    {
                        Console.WriteLine($"   ⚠️  No value found for '{makeVarName}' in FastLocals");
                    }
                    
                    // CellVars 리스트에서 인덱스 찾기
                    var makeCellIndex = frame.Code.CellVars.IndexOf(makeVarName);
                    if (makeCellIndex >= 0 && makeCellIndex < frame.Cells.Length)
                    {
                        frame.Cells[makeCellIndex].SetValue(cellValue);
                        Console.WriteLine($"   ✅ Set cell[{makeCellIndex}] = {cellValue}");
                    }
                    else
                    {
                        Console.WriteLine($"   ❌ Invalid cell index for '{makeVarName}': {makeCellIndex}");
                    }
                    break;
                    
                // Generator Implementation
                case ByteCodeOp.YIELD_VALUE:
                    var yieldValue = frame.ValueStack.Pop();
                    
                    // yield는 제너레이터에서만 사용 가능
                    if (!frame.Code.IsGenerator())
                    {
                        throw PySyntaxError.Create("'yield' outside function");
                    }
                    
                    // CPython 3.12 스타일: instruction pointer를 다음으로 이동한 후 yield
                    frame.InstructionPointer++;
                    throw new PyYieldException(yieldValue);
                    
                // Generator Delegation - yield from implementation
                case ByteCodeOp.YIELD_FROM:
                    var delegatedIterable = frame.ValueStack.Pop();
                    
                    // yield from은 제너레이터에서만 사용 가능
                    if (!frame.Code.IsGenerator())
                    {
                        throw PySyntaxError.Create("'yield from' outside function");
                    }
                    
                    // PyYieldFrom 예외를 던져서 제너레이터 위임 요청
                    throw new PyYieldFromException(delegatedIterable);
                    
                default:
                    throw new NotImplementedException($"OpCode {instruction.OpCode} not implemented");
            }
            
            return null;
        }
        
        // 이항 연산 (기존 타입 시스템 활용)
        // CPython 3.12+ unified binary operation executor
        private PyObject ExecuteBinaryOpType(PyObject left, PyObject right, BinaryOpType binaryOp)
        {
            // Handle boolean operations (for match statements)
            if (binaryOp == BinaryOpType.AND || binaryOp == BinaryOpType.OR)
            {
                // Convert operands to boolean values
                var leftBool = left.ToBool();
                var rightBool = right.ToBool();
                
                var result = binaryOp switch
                {
                    BinaryOpType.AND => leftBool && rightBool ? PyBool.True : PyBool.False,
                    BinaryOpType.OR => leftBool || rightBool ? PyBool.True : PyBool.False,
                    _ => throw PyTypeError.Create($"unsupported operation: {binaryOp}")
                };
                
                Console.WriteLine($"    → {left} {binaryOp} {right} = {result}");
                return result;
            }
            
            // Use PyObject's built-in binary operation methods (CPython compatible)
            try
            {
                return binaryOp switch
                {
                    BinaryOpType.ADD => left.Add(right),
                    BinaryOpType.SUBTRACT => left.Subtract(right),
                    BinaryOpType.MULTIPLY => left.Multiply(right),
                    BinaryOpType.TRUE_DIVIDE => left.Divide(right),
                    BinaryOpType.FLOOR_DIVIDE => left.FloorDivide(right),
                    BinaryOpType.MODULO => left.Modulo(right),
                    BinaryOpType.POWER => left.Power(right),
                    BinaryOpType.LSHIFT => left.LeftShift(right),
                    BinaryOpType.RSHIFT => left.RightShift(right),
                    BinaryOpType.AND => left.BitwiseAnd(right),
                    BinaryOpType.OR => left.BitwiseOr(right),
                    BinaryOpType.XOR => left.BitwiseXor(right),
                    BinaryOpType.MATRIX_MULTIPLY => throw new NotImplementedException("Matrix multiplication not yet implemented"),
                    _ => throw PyTypeError.Create($"unsupported binary operation: {binaryOp}")
                };
            }
            catch (Exception ex) when (!(ex is PythonException))
            {
                // Convert C# exceptions to Python exceptions
                throw PyTypeError.Create($"unsupported operand type(s) for {binaryOp}: '{left.GetTypeName()}' and '{right.GetTypeName()}'");
            }
        }
        
        // Legacy method for backward compatibility
        private PyObject BinaryOperation(PyObject left, PyObject right, string op)
        {
            var operation = op switch
            {
                "+" => BinaryOpType.ADD,
                "-" => BinaryOpType.SUBTRACT,
                "*" => BinaryOpType.MULTIPLY,
                "/" => BinaryOpType.TRUE_DIVIDE,
                "//" => BinaryOpType.FLOOR_DIVIDE,
                "%" => BinaryOpType.MODULO,
                "**" => BinaryOpType.POWER,
                "<<" => BinaryOpType.LSHIFT,
                ">>" => BinaryOpType.RSHIFT,
                "&" => BinaryOpType.AND,
                "|" => BinaryOpType.OR,
                "^" => BinaryOpType.XOR,
                "and" => BinaryOpType.AND,
                "or" => BinaryOpType.OR,
                _ => throw PyTypeError.Create($"unsupported operator: {op}")
            };
            
            return ExecuteBinaryOpType(left, right, operation);
        }

        /// <summary>
        /// CPython-style comparison operations
        /// </summary>
        /// <summary>
        /// Python 스타일 포매팅을 적용 (VM에서 사용)
        /// </summary>
        private PyString ApplyFormatting(PyObject obj, string formatSpec)
        {
            try
            {
                // 숫자 포매팅 지원
                if (obj is PyFloat floatObj)
                {
                    if (formatSpec.EndsWith("f"))
                    {
                        // 소수점 자릿수 지정 (예: .2f)
                        if (formatSpec.StartsWith(".") && formatSpec.Length > 2)
                        {
                            var digits = formatSpec.Substring(1, formatSpec.Length - 2);
                            if (int.TryParse(digits, out int decimalPlaces))
                            {
                                var formatted = floatObj.Value.ToString($"F{decimalPlaces}");
                                return new PyString(formatted);
                            }
                        }
                        else if (formatSpec == "f")
                        {
                            return new PyString(floatObj.Value.ToString("F"));
                        }
                    }
                    else if (formatSpec.EndsWith("e"))
                    {
                        return new PyString(floatObj.Value.ToString("E"));
                    }
                    else if (formatSpec.EndsWith("%"))
                    {
                        return new PyString((floatObj.Value * 100).ToString("F") + "%");
                    }
                }
                else if (obj is PyInt intObj)
                {
                    if (formatSpec == "d")
                    {
                        return new PyString(intObj.Value.ToString());
                    }
                    else if (formatSpec == "x")
                    {
                        return new PyString(intObj.Value.ToString("x"));
                    }
                    else if (formatSpec == "X")
                    {
                        return new PyString(intObj.Value.ToString("X"));
                    }
                    else if (formatSpec == "o")
                    {
                        return new PyString(Convert.ToString(intObj.Value, 8));
                    }
                    else if (formatSpec == "b")
                    {
                        return new PyString(Convert.ToString(intObj.Value, 2));
                    }
                }
                
                // 문자열 정렬 지원 (예: >10, <10, ^10)
                if (formatSpec.Length > 0)
                {
                    var align = formatSpec[0];
                    var remaining = formatSpec.Substring(1);
                    
                    if ((align == '<' || align == '>' || align == '^') && int.TryParse(remaining, out int width))
                    {
                        var str = obj.ToStr();
                        switch (align)
                        {
                            case '<': return new PyString(str.PadRight(width));
                            case '>': return new PyString(str.PadLeft(width));
                            case '^': 
                                var totalPadding = width - str.Length;
                                var leftPadding = totalPadding / 2;
                                var rightPadding = totalPadding - leftPadding;
                                return new PyString(new string(' ', leftPadding) + str + new string(' ', rightPadding));
                        }
                    }
                }
            }
            catch
            {
                // 포매팅 실패 시 원본 값 반환
            }
            
            return new PyString(obj.ToStr());
        }
        
        private PyObject CompareOperation(PyObject left, PyObject right, int compareOp)
        {
            var operation = (CompareOp)compareOp;
            return operation switch
            {
                CompareOp.Eq => left.RichCompare(right, PyObject.CompareOp.EQ),
                CompareOp.NotEq => left.RichCompare(right, PyObject.CompareOp.NE),
                CompareOp.Lt => left.RichCompare(right, PyObject.CompareOp.LT),
                CompareOp.LtE => left.RichCompare(right, PyObject.CompareOp.LE),
                CompareOp.Gt => left.RichCompare(right, PyObject.CompareOp.GT),
                CompareOp.GtE => left.RichCompare(right, PyObject.CompareOp.GE),
                CompareOp.In => ((PyBool)right.Contains(left)),
                CompareOp.NotIn => ((PyBool)right.Contains(left)).Not(),
                CompareOp.Is => ReferenceEquals(left, right) ? PyBool.True : PyBool.False,
                CompareOp.IsNot => ReferenceEquals(left, right) ? PyBool.False : PyBool.True,
                _ => throw new NotImplementedException($"Compare operation {operation} not implemented")
            };
        }

        /// <summary>
        /// CPython-style sequence unpacking
        /// </summary>

        /// <summary>
        /// Compare operation enumeration matching CPython
        /// </summary>
        private enum CompareOp : int
        {
            Lt = 0,
            LtE = 1, 
            Eq = 2,
            NotEq = 3,
            Gt = 4,
            GtE = 5,
            In = 6,
            NotIn = 7,
            Is = 8,
            IsNot = 9
        }
        
        /// <summary>
        /// Find the end of the current loop for break statements
        /// </summary>
        private int FindLoopEnd(PyFrame frame, int currentPos)
        {
            var instructions = frame.Code.Instructions;
            
            // For FOR loops, look backwards for FOR_ITER and use its argument
            for (int i = currentPos - 1; i >= 0; i--)
            {
                var instr = instructions[i];
                if (instr.OpCode == ByteCodeOp.FOR_ITER)
                {
                    return instr.Argument;
                }
            }
            
            // For while loops, we need to find the outermost POP_JUMP_IF_FALSE that exits the loop
            // Look backwards to find all POP_JUMP_IF_FALSE instructions and take the one with highest jump target
            int bestJumpTarget = -1;
            for (int i = currentPos - 1; i >= 0; i--)
            {
                var instr = instructions[i];
                if (instr.OpCode == ByteCodeOp.POP_JUMP_IF_FALSE)
                {
                    // Take the POP_JUMP_IF_FALSE with the highest jump target (outermost loop)
                    if (instr.Argument > bestJumpTarget)
                    {
                        bestJumpTarget = instr.Argument;
                    }
                }
            }
            
            if (bestJumpTarget != -1)
            {
                return bestJumpTarget;
            }
            
            // If we can't find a proper loop end, just continue execution
            return currentPos + 1;
        }
        
        /// <summary>
        /// Find the start of the current loop for continue statements
        /// </summary>
        private int FindLoopStart(PyFrame frame, int currentPos)
        {
            var instructions = frame.Code.Instructions;
            
            // For FOR loops, look backwards for FOR_ITER first (priority)
            for (int i = currentPos - 1; i >= 0; i--)
            {
                var instr = instructions[i];
                if (instr.OpCode == ByteCodeOp.FOR_ITER)
                {
                    return i; // Jump back to FOR_ITER
                }
            }
            
            // For while loops, look for loop condition (COMPARE_OP followed by POP_JUMP_IF_FALSE)
            for (int i = currentPos - 1; i >= 1; i--)
            {
                var instr = instructions[i];
                if (instr.OpCode == ByteCodeOp.POP_JUMP_IF_FALSE && 
                    instructions[i-1].OpCode == ByteCodeOp.COMPARE_OP)
                {
                    return i - 1; // Jump back to COMPARE_OP
                }
            }
            
            // If we can't find a proper loop start, just continue execution
            return currentPos + 1;
        }
        
        /// <summary>
        /// Check if exception matches the given type (CPython-compatible)
        /// </summary>
        private bool ExceptionMatches(PyObject exception, PyObject exceptionType)
        {
            // CPython-style exception matching
            // exception: actual exception instance (e.g., ValueError("message"))
            // exceptionType: exception class (e.g., ValueError class)
            
            if (exception is PyException pyExc)
            {
                // Get the exception's actual type name
                string excTypeName = pyExc.GetType().Name;
                if (excTypeName.StartsWith("Py"))
                    excTypeName = excTypeName.Substring(2); // Remove "Py" prefix
                
                string targetTypeName = "";
                
                // Handle different types of exception type objects
                if (exceptionType is PyBuiltinType builtinType)
                {
                    targetTypeName = builtinType.Name;
                }
                else if (exceptionType is PyType pyType)
                {
                    targetTypeName = pyType.Name;
                }
                else
                {
                    // Try to get the type name directly from the object
                    targetTypeName = exceptionType.ToString();
                    if (targetTypeName.Contains("'") && targetTypeName.Contains("class"))
                    {
                        // Extract class name from "<class 'ValueError'>"
                        var start = targetTypeName.LastIndexOf("'") - targetTypeName.Length + targetTypeName.LastIndexOf("'") + 1;
                        start = targetTypeName.IndexOf("'") + 1;
                        var end = targetTypeName.LastIndexOf("'");
                        if (end > start)
                            targetTypeName = targetTypeName.Substring(start, end - start);
                    }
                }
                
                Console.WriteLine($"🔍 Exception match: {excTypeName} vs {targetTypeName}");
                
                // Direct type match
                bool matches = excTypeName.Equals(targetTypeName, StringComparison.OrdinalIgnoreCase);
                
                // Also check inheritance (Exception should match all exceptions)
                if (!matches && targetTypeName == "Exception")
                {
                    matches = true; // All exceptions inherit from Exception
                }
                
                return matches;
            }
            
            return false;
        }
        
        /// <summary>
        /// PEP 654: ExceptionGroup matching - returns (matched, remainder)
        /// </summary>
        private (PyObject?, PyObject?) ExceptionGroupMatches(PyObject exception, PyObject exceptionType)
        {
            if (exception is PyBaseExceptionGroup group)
            {
                var matchedExceptions = new List<PyException>();
                var remainderExceptions = new List<PyException>();
                
                foreach (var exc in group.Exceptions)
                {
                    if (ExceptionMatches(exc, exceptionType))
                    {
                        matchedExceptions.Add(exc);
                    }
                    else
                    {
                        remainderExceptions.Add(exc);
                    }
                }
                
                PyObject? matched = null;
                if (matchedExceptions.Count > 0)
                {
                    if (exception is PyExceptionGroup)
                        matched = new PyExceptionGroup(group.Message, matchedExceptions);
                    else
                        matched = new PyBaseExceptionGroup(group.Message, matchedExceptions);
                }
                
                PyObject? remainder = null;
                if (remainderExceptions.Count > 0)
                {
                    if (exception is PyExceptionGroup)
                        remainder = new PyExceptionGroup(group.Message, remainderExceptions);
                    else
                        remainder = new PyBaseExceptionGroup(group.Message, remainderExceptions);
                }
                
                return (matched, remainder);
            }
            
            // Not an exception group - check if single exception matches
            if (ExceptionMatches(exception, exceptionType))
            {
                return (exception, null);
            }
            
            return (null, exception);
        }
        
        /// <summary>
        /// CPython-style function argument binding with default parameters
        /// Used by MAKE_FUNCTION bytecode implementation
        /// </summary>
        private PyObject[] BindFunctionArguments(PyObject[] args, PyCodeObject code, PyTuple defaults)
        {
            Console.WriteLine($"🔗 함수 호출 매개변수 바인딩: {args.Length}개 인수, {code.ArgCount}개 매개변수");
            
            // Calculate required vs provided arguments
            int defaultCount = defaults?.Items?.Length ?? 0;
            int requiredArgCount = code.ArgCount - defaultCount;
            
            Console.WriteLine($"  필수 매개변수: {requiredArgCount}, 기본값 매개변수: {defaultCount}");
            
            // Check if we have enough arguments
            if (args.Length < requiredArgCount)
            {
                throw PyTypeError.Create($"{code.Name}() missing {requiredArgCount - args.Length} required positional argument(s)");
            }
            
            // Check if we have too many arguments
            if (args.Length > code.ArgCount)
            {
                throw PyTypeError.Create($"{code.Name}() takes {code.ArgCount} positional argument(s) but {args.Length} were given");
            }
            
            // Create bound arguments array
            var boundArgs = new PyObject[code.ArgCount];
            
            // Bind provided arguments first
            for (int i = 0; i < args.Length; i++)
            {
                boundArgs[i] = args[i];
                Console.WriteLine($"  → 매개변수[{i}] = {args[i]} (제공된 인수)");
            }
            
            // Bind default values for missing arguments
            if (defaults != null && defaults.Items.Length > 0)
            {
                for (int i = args.Length; i < code.ArgCount; i++)
                {
                    int defaultIndex = i - requiredArgCount;
                    if (defaultIndex >= 0 && defaultIndex < defaults.Items.Length)
                    {
                        boundArgs[i] = defaults.Items[defaultIndex];
                        Console.WriteLine($"  → 매개변수[{i}] = {defaults.Items[defaultIndex]} (기본값)");
                    }
                }
            }
            
            return boundArgs;
        }
        
        /// <summary>
        /// 키워드 인수를 지원하는 함수 호출
        /// </summary>
        private PyObject CallFunctionWithKwargs(PyObject function, PyObject[] args, Dictionary<string, PyObject> kwargs)
        {
            // 간단한 구현: 키워드 인수를 위치 인수로 변환하여 기존 Call 메서드 사용
            if (function is PyFunction pyFunc)
            {
                // 키워드 인수를 포함한 매개변수 바인딩 수행
                var totalArgs = BindArgumentsWithKwargs(args, kwargs, pyFunc);
                return pyFunc.Call(totalArgs);
            }
            else if (function is PyType pyType)
            {
                // PyType 객체의 경우 키워드 인수를 포함해서 전달
                var totalArgs = new List<PyObject>();
                totalArgs.AddRange(args);
                foreach (var kv in kwargs)
                {
                    totalArgs.Add(new PyString(kv.Key));
                    totalArgs.Add(kv.Value);
                }
                
                // 키워드 이름 튜플을 마지막에 추가
                var kwNames = kwargs.Keys.Select(k => (PyObject)new PyString(k)).ToArray();
                totalArgs.Add(new PyTuple(kwNames));
                
                return pyType.Call(totalArgs.ToArray());
            }
            else
            {
                // 다른 callable 객체의 경우 기본 Call 메서드 사용 (키워드 인수 무시)
                return function.Call(args);
            }
        }
        
        /// <summary>
        /// 키워드 인수를 포함한 매개변수 바인딩 (간소화 버전)
        /// </summary>
        private PyObject[] BindArgumentsWithKwargs(PyObject[] args, Dictionary<string, PyObject> kwargs, PyFunction function)
        {
            var code = function.CodeObject;
            var boundArgs = new PyObject[code.ArgCount];
            
            Console.WriteLine($"🔧 키워드 인수 포함 매개변수 바인딩: {args.Length}개 위치인수, {kwargs.Count}개 키워드인수, {code.ArgCount}개 매개변수");
            Console.WriteLine($"  Flags: 0x{code.Flags:X8}, VarNames count: {code.VarNames.Count}");
            
            // CPython 방식: 플래그 기반 **kwargs 탐지
            bool hasKwargs = (code.Flags & PyCodeObject.CO_VARKEYWORDS) != 0;
            bool hasVarargs = (code.Flags & PyCodeObject.CO_VARARGS) != 0;
            
            int kwargsParamIndex = hasKwargs ? code.ArgCount - 1 : -1;
            
            Console.WriteLine($"  hasKwargs: {hasKwargs}, hasVarargs: {hasVarargs}, kwargsIndex: {kwargsParamIndex}");
            
            // 실제 필수/선택적 매개변수 개수 계산 (**kwargs 제외)
            int regularParamCount = hasKwargs ? code.ArgCount : code.ArgCount;
            
            // 1. 위치 인수 바인딩
            for (int i = 0; i < Math.Min(args.Length, regularParamCount); i++)
            {
                boundArgs[i] = args[i];
                string paramName = i < code.VarNames.Count ? code.VarNames[i] : $"arg{i}";
                Console.WriteLine($"  → 매개변수[{i}] '{paramName}' = {args[i]} (위치인수)");
            }
            
            // 2. 키워드 인수 바인딩 및 **kwargs 수집
            var extraKwargs = new Dictionary<string, PyObject>();
            
            foreach (var kvp in kwargs)
            {
                string paramName = kvp.Key;
                PyObject paramValue = kvp.Value;
                
                // 일반 매개변수에서 매칭 찾기
                int paramIndex = -1;
                for (int i = 0; i < regularParamCount; i++)
                {
                    // CPython 방식: VarNames는 이미 clean한 매개변수 이름만 포함
                    string cleanParamName = i < code.VarNames.Count ? code.VarNames[i] : "";
                        
                    if (cleanParamName == paramName)
                    {
                        paramIndex = i;
                        break;
                    }
                }
                
                if (paramIndex != -1)
                {
                    // 일반 매개변수에 바인딩
                    if (boundArgs[paramIndex] != null)
                    {
                        throw PyTypeError.Create($"'{code.Name}() got multiple values for argument '{paramName}'");
                    }
                    
                    boundArgs[paramIndex] = paramValue;
                    Console.WriteLine($"  → 매개변수[{paramIndex}] '{paramName}' = {paramValue} (키워드인수)");
                }
                else if (kwargsParamIndex >= 0)
                {
                    // **kwargs에 수집
                    extraKwargs[paramName] = paramValue;
                    Console.WriteLine($"  → **kwargs['{paramName}'] = {paramValue}");
                }
                else
                {
                    throw PyTypeError.Create($"'{code.Name}() got an unexpected keyword argument '{paramName}'");
                }
            }
            
            // 3. **kwargs 딕셔너리 생성
            if (kwargsParamIndex >= 0)
            {
                var kwargsDict = new PyDict();
                foreach (var kvp in extraKwargs)
                {
                    kwargsDict.SetItem(new PyString(kvp.Key), kvp.Value);
                }
                boundArgs[kwargsParamIndex] = kwargsDict;
                Console.WriteLine($"  → 매개변수[{kwargsParamIndex}] '**kwargs' = {kwargsDict} (**kwargs 딕셔너리)");
            }
            
            // 4. 기본값 적용 (바인딩되지 않은 매개변수에)
            var defaults = GetFunctionDefaults(function);
            int defaultCount = defaults?.Length ?? 0;
            int requiredArgCount = regularParamCount - defaultCount;
            
            Console.WriteLine($"  기본값 매개변수: {defaultCount}개, 필수 매개변수: {requiredArgCount}개");
            
            if (defaults != null)
            {
                for (int i = requiredArgCount; i < regularParamCount; i++)
                {
                    if (boundArgs[i] == null)
                    {
                        int defaultIndex = i - requiredArgCount;
                        if (defaultIndex >= 0 && defaultIndex < defaults.Length)
                        {
                            boundArgs[i] = defaults[defaultIndex];
                            string paramName = i < code.VarNames.Count ? code.VarNames[i] : $"arg{i}";
                            Console.WriteLine($"  → 매개변수[{i}] '{paramName}' = {defaults[defaultIndex]} (기본값)");
                        }
                    }
                }
            }
            
            // 5. 바인딩되지 않은 필수 매개변수 확인
            for (int i = 0; i < requiredArgCount; i++)
            {
                if (boundArgs[i] == null)
                {
                    string paramName = i < code.VarNames.Count ? code.VarNames[i] : $"arg{i}";
                    throw PyTypeError.Create($"'{code.Name}() missing required argument: '{paramName}'");
                }
            }
            
            return boundArgs;
        }

        private PyObject[] GetFunctionDefaults(PyFunction function)
        {
            if (function.CodeObject?.DefaultValues == null)
                return new PyObject[0];
            
            return function.CodeObject.DefaultValues.ToArray();
        }
        
        /// <summary>
        /// CPython 3.12 GET_AWAITABLE 구현 - PEP 492 호환
        /// </summary>
        private PyObject GetAwaitable(PyObject obj)
        {
            // 1. Native coroutine 확인 (PyCoroutine)
            if (obj is SharpPy.Core.PyCoroutine coroutine)
            {
                Console.WriteLine($"✅ GET_AWAITABLE: Native coroutine {coroutine}");
                return coroutine.GetAwaiter();
            }
            
            // 2. Generator-based coroutine 확인 (__await__ 메서드 존재)
            try
            {
                var awaitMethod = obj.GetAttribute("__await__");
                if (awaitMethod != null)
                {
                    Console.WriteLine($"✅ GET_AWAITABLE: Generator-based coroutine with __await__");
                    return awaitMethod.Call(new PyObject[0]);
                }
            }
            catch
            {
                // __await__ 메서드가 없거나 호출 실패
            }
            
            // 3. Iterator protocol이 있는 객체 확인 (generator도 여기 포함)
            try
            {
                var iterator = obj.GetIterator();
                if (iterator != null)
                {
                    Console.WriteLine($"✅ GET_AWAITABLE: Iterator-based awaitable");
                    return iterator;
                }
            }
            catch
            {
                // Iterator protocol이 없음
            }
            
            // 4. 모든 조건을 만족하지 않으면 TypeError
            throw PyTypeError.Create($"object {obj.GetTypeName()} can't be used in 'await' expression");
        }
    }

    #endregion
}