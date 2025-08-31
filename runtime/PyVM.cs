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
        
        public PyFrame(PyCodeObject code, PyObject[] args, PyScopeChain parentScope = null, PyCell[] closure = null)
        {
            Code = code;
            ValueStack = new Stack<PyObject>();
            ScopeChain = new PyScopeChain(); // 새로운 스코프 체인 생성
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
            
            // 함수 인자를 지역 변수로 바인딩
            for (int i = 0; i < args.Length && i < code.ArgCount; i++)
            {
                var paramName = code.VarNames[i];
                FastLocals[paramName] = args[i];
                ScopeChain.AssignVariable(paramName, args[i]);
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
    public class PythonVM
    {
        public static PythonVM Instance { get; } = new PythonVM();
        
        private readonly Stack<PyFrame> _frameStack;
        private readonly PyScopeChain _globalScope;
        
        private PythonVM()
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
                    
                case ByteCodeOp.BINARY_ADD:
                    var rightAdd = frame.ValueStack.Pop();
                    var leftAdd = frame.ValueStack.Pop();
                    var addResult = BinaryOperation(leftAdd, rightAdd, "+");
                    frame.ValueStack.Push(addResult);
                    break;
                    
                case ByteCodeOp.BINARY_MULTIPLY:
                    var rightMul = frame.ValueStack.Pop();
                    var leftMul = frame.ValueStack.Pop();
                    var mulResult = BinaryOperation(leftMul, rightMul, "*");
                    frame.ValueStack.Push(mulResult);
                    break;
                    
                case ByteCodeOp.BINARY_SUBTRACT:
                    var rightSub = frame.ValueStack.Pop();
                    var leftSub = frame.ValueStack.Pop();
                    var subResult = BinaryOperation(leftSub, rightSub, "-");
                    frame.ValueStack.Push(subResult);
                    break;
                    
                case ByteCodeOp.BINARY_AND:
                    var rightAnd = frame.ValueStack.Pop();
                    var leftAnd = frame.ValueStack.Pop();
                    // For match statements: implement logical AND for boolean values
                    var andResult = BinaryOperation(leftAnd, rightAnd, "and");
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
                    
                case ByteCodeOp.MAKE_FUNCTION:
                    // CPython-style function creation with closure support
                    var flags = instruction.Argument;
                    var codeObject = frame.ValueStack.Pop();
                    
                    PyCell[] closure = null;
                    
                    // Check for closure flag (8 = MAKE_FUNCTION_CLOSURE)
                    if ((flags & 8) != 0)
                    {
                        var closureTuple = frame.ValueStack.Pop();
                        if (closureTuple is PyTuple tuple)
                        {
                            closure = tuple.Items.Cast<PyCell>().ToArray();
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
                        PyFunction functionObject;
                        
                        if (closure != null && closure.Length > 0)
                        {
                            // Create function with closure
                            functionObject = PyFunction.CreateClosureFunction(pyCode.Name, pyCode, closure, frame.ScopeChain);
                        }
                        else
                        {
                            // Create regular function without closure
                            functionObject = new PyFunction(pyCode.Name, args =>
                            {
                                // 새로운 프레임으로 함수 코드 실행
                                var functionFrame = new PyFrame(pyCode, args, frame.ScopeChain);
                                return ExecuteFrame(functionFrame);
                            }, null, null, closure, pyCode);
                        }
                        
                        frame.ValueStack.Push(functionObject);
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
                    frame.InstructionPointer = frame.InstructionPointer - instruction.Argument - 1; // -1 because main loop will increment
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
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // Convert C# exceptions to Python exceptions
                        throw PyTypeError.Create($"subscript error: {ex.Message}");
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
                    var iter = frame.ValueStack.Peek(); // Don't pop yet
                    try
                    {
                        var nextItem = iter.Next();
                        frame.ValueStack.Push(nextItem);
                    }
                    catch (PythonException ex) when (ex.PyException is PyStopIteration)
                    {
                        frame.ValueStack.Pop(); // Remove iterator
                        frame.InstructionPointer = instruction.Argument; // Jump to end of loop
                        return null;
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

                // CPython-style Sequence Operations  
                case ByteCodeOp.UNPACK_SEQUENCE:
                    var sequence = frame.ValueStack.Pop();
                    var count = instruction.Argument;
                    var items = UnpackSequence(sequence, count);
                    foreach (var item in items.Reverse())
                    {
                        frame.ValueStack.Push(item);
                    }
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
                    var module = frame.ValueStack.Peek(); // Don't pop yet, needed for multiple imports
                    var importedItem = module.GetAttribute(itemName);
                    frame.ValueStack.Push(importedItem);
                    break;
                    
                // PEP 709 Comprehension Optimization - VM 구현
                case ByteCodeOp.LIST_APPEND:
                    // 스택: [..., list, ..., item] → [..., list, ...]
                    // argument는 list의 위치 (스택 top에서 몇 번째 아래)
                    var listItem = frame.ValueStack.Pop();
                    var stackArray = frame.ValueStack.ToArray();
                    if (instruction.Argument <= stackArray.Length)
                    {
                        var targetList = stackArray[instruction.Argument - 1];
                        if (targetList is PyList targetPyList)
                        {
                            targetPyList.Append(listItem);
                        }
                        else
                        {
                            throw new Exception($"LIST_APPEND: target is not a list, got {targetList.GetType().Name}");
                        }
                    }
                    else
                    {
                        throw new Exception("LIST_APPEND: invalid stack position");
                    }
                    break;
                    
                case ByteCodeOp.SET_ADD:
                    // 스택: [..., set, ..., item] → [..., set, ...]
                    var setItem = frame.ValueStack.Pop();
                    var setStackArray = frame.ValueStack.ToArray();
                    if (instruction.Argument <= setStackArray.Length)
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
                        throw new Exception("SET_ADD: invalid stack position");
                    }
                    break;
                    
                case ByteCodeOp.MAP_ADD:
                    // 스택: [..., dict, ..., key, value] → [..., dict, ...]
                    var dictValue = frame.ValueStack.Pop();
                    var dictKey = frame.ValueStack.Pop();
                    var dictStackArray = frame.ValueStack.ToArray();
                    if (instruction.Argument <= dictStackArray.Length)
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
                        throw new Exception("MAP_ADD: invalid stack position");
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
                    
                default:
                    throw new NotImplementedException($"OpCode {instruction.OpCode} not implemented");
            }
            
            return null;
        }
        
        // 이항 연산 (기존 타입 시스템 활용)
        private PyObject BinaryOperation(PyObject left, PyObject right, string op)
        {
            // Handle boolean operations (for match statements)
            if (op == "and" || op == "or")
            {
                // Convert operands to boolean values
                var leftBool = left.ToBool();
                var rightBool = right.ToBool();
                
                var result = op switch
                {
                    "and" => leftBool && rightBool ? PyBool.True : PyBool.False,
                    "or" => leftBool || rightBool ? PyBool.True : PyBool.False,
                    _ => throw PyTypeError.Create($"unsupported operator: {op}")
                };
                
                Console.WriteLine($"    → {left} {op} {right} = {result}");
                return result;
            }
            
            if (left is PyInt leftInt && right is PyInt rightInt)
            {
                var result = op switch
                {
                    "+" => new PyInt(leftInt.Value + rightInt.Value),
                    "-" => new PyInt(leftInt.Value - rightInt.Value),
                    "*" => new PyInt(leftInt.Value * rightInt.Value),
                    "/" => new PyInt(leftInt.Value / rightInt.Value),
                    _ => throw PyTypeError.Create($"unsupported operator: {op}")
                };
                
                Console.WriteLine($"    → {left} {op} {right} = {result}");
                return result;
            }
            
            throw PyTypeError.Create($"unsupported operand type(s) for {op}: '{left.GetTypeName()}' and '{right.GetTypeName()}'");
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
        private PyObject[] UnpackSequence(PyObject sequence, int count)
        {
            if (sequence is PyList list)
            {
                if (list.Length() != count)
                    throw PyValueError.Create($"too many values to unpack (expected {count})");
                return list.Items.Take(count).ToArray();
            }
            else if (sequence is PyTuple tuple)
            {
                if (tuple.Items.Length != count)
                    throw PyValueError.Create($"too many values to unpack (expected {count})");
                return tuple.Items.Take(count).ToArray();
            }
            else if (sequence is PyString str)
            {
                if (str.Value.Length != count)
                    throw PyValueError.Create($"too many values to unpack (expected {count})");
                return str.Value.Select(c => new PyString(c.ToString())).Cast<PyObject>().ToArray();
            }
            else
            {
                // Try to get iterator and collect items
                var iterator = sequence.GetIterator();
                var items = new List<PyObject>();
                try
                {
                    while (items.Count < count)
                    {
                        items.Add(iterator.Next());
                    }
                    
                    // Check if there are more items (would indicate too many values)
                    try
                    {
                        iterator.Next();
                        throw PyValueError.Create($"too many values to unpack (expected {count})");
                    }
                    catch (PythonException ex) when (ex.PyException is PyStopIteration)
                    {
                        // This is expected - no more items
                    }
                }
                catch (PythonException ex) when (ex.PyException is PyStopIteration && items.Count < count)
                {
                    throw PyValueError.Create($"not enough values to unpack (expected {count}, got {items.Count})");
                }
                
                return items.ToArray();
            }
        }

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
            if (exception is PyException pyExc && exceptionType is PyBuiltinType builtinType)
            {
                // Get the exception's type name
                string excTypeName = pyExc.GetType().Name;
                
                // Match type names
                return excTypeName.Replace("Py", "") == builtinType.Name.Replace("Error", "Error");
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
    }

    #endregion
}