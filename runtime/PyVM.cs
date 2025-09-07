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
        
        // CPython-style error location tracking
        public int CurrentLineNumber { get; set; } = -1;
        public int CurrentColumnOffset { get; set; } = -1;
        public string? CurrentFileName { get; set; }
        
        // CPython-style closure support
        public PyCell[] Cells { get; set; } = new PyCell[0];     // 클로저 셀들 (freevars + cellvars)
        public PyCell[] Closure { get; set; } = new PyCell[0];   // 부모로부터 받은 클로저 셀들
        
        // CPython-style exception handling support
        public Stack<int> ExceptionHandlers { get; } = new Stack<int>();
        public PyBaseException? LastException { get; set; }
        public PyBaseException? CurrentException { get; set; } // Current exception for PUSH_EXC_INFO
        public int ExceptionHandlerCallCount { get; set; } = 0; // Prevent infinite loops
        
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
            if (SharpPyConfig.ShouldShowDebugInfo)
            {
                Console.WriteLine($"🆕 PyFrame 생성: {code.Name}, args={args.Length}개");
            }
            
            Code = code;
            ValueStack = new Stack<PyObject>();
            // 부모 스코프 체인이 있으면 상속, 없으면 새로 생성
            ScopeChain = parentScope ?? new PyScopeChain();
            FastLocals = new Dictionary<string, PyObject>();
            InstructionPointer = 0;
            
            // Initialize filename from code object
            CurrentFileName = code.FileName;
            
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
            if (SharpPyConfig.ShouldShowDebugInfo)
            {
                Console.WriteLine($"🔗 매개변수 바인딩: {args.Length}개 인수, {code.ArgCount}개 매개변수");
                Console.WriteLine($"  DefaultValues.Count: {code.DefaultValues.Count}");
                for (int j = 0; j < code.DefaultValues.Count; j++)
                {
                    Console.WriteLine($"    [{j}]: {code.DefaultValues[j]?.ToString() ?? "null"}");
                }
            }
            
            // CPython처럼 위치 인수 먼저 처리
            for (int i = 0; i < code.ArgCount; i++)
            {
                var paramName = code.VarNames[i];
                if (SharpPyConfig.ShouldShowDebugInfo)
                {
                    Console.WriteLine($"  처리중: 매개변수[{i}] = '{paramName}'");
                }
                
                if (i < args.Length)
                {
                    // 제공된 위치 인수 사용
                    if (SharpPyConfig.ShouldShowDebugInfo)
                    {
                        Console.WriteLine($"  → {paramName} = {args[i]} (위치 인수)");
                    }
                    FastLocals[paramName] = args[i];
                    ScopeChain.AssignVariable(paramName, args[i]);
                }
                else
                {
                    // Check if this parameter has a default value
                    // Default values are stored for the last N parameters where N = DefaultValues.Count
                    int numRequiredParams = code.ArgCount - code.DefaultValues.Count;
                    if (i >= numRequiredParams && i - numRequiredParams < code.DefaultValues.Count)
                    {
                        var defaultValue = code.DefaultValues[i - numRequiredParams];
                        if (defaultValue != null)
                        {
                            Console.WriteLine($"  → {paramName} = {defaultValue} (기본값, index {i - numRequiredParams})");
                            FastLocals[paramName] = defaultValue;
                            ScopeChain.AssignVariable(paramName, defaultValue);
                        }
                        else
                        {
                            Console.WriteLine($"  ❌ Default value at index {i - numRequiredParams} is null");
                            throw PyTypeError.Create($"[PyFrame] missing required argument: '{paramName}'");
                        }
                    }
                    else
                    {
                        // 필수 매개변수가 누락됨
                        Console.WriteLine($"  ❌ No default available: param {i}, required={numRequiredParams}, defaults={code.DefaultValues.Count}");
                        throw PyTypeError.Create($"[PyFrame] missing required argument: '{paramName}'");
                    }
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
            // Prevent infinite loop in exception handling
            ExceptionHandlerCallCount++;
            if (ExceptionHandlerCallCount > 100)
            {
                Console.WriteLine($"❌ GetExceptionHandler: Infinite loop detected, stopping at call #{ExceptionHandlerCallCount}");
                return null; // Break the cycle
            }
            
            Console.WriteLine($"🔍 GetExceptionHandler called #{ExceptionHandlerCallCount} - IP: {InstructionPointer}");
            Console.WriteLine($"   Frame: {ToString()}");
            Console.WriteLine($"   Code Name: {Code.Name}");
            Console.WriteLine($"   Exception Table entries: {Code.ExceptionTable.Count}");
            if (Code.ExceptionTable.Count > 0)
            {
                Console.WriteLine($"   Exception Table details:");
                for (int i = 0; i < Code.ExceptionTable.Count; i++)
                {
                    var entry = Code.ExceptionTable[i];
                    Console.WriteLine($"     [{i}] Start: {entry.StartOffset}, End: {entry.EndOffset}, Handler: {entry.HandlerOffset}");
                }
            }
            Console.WriteLine($"   Legacy handlers: {ExceptionHandlers.Count}");
            
            // CPython 3.12: Use Exception Table instead of SETUP_EXCEPT stack
            if (Code.ExceptionTable.Count > 0)
            {
                var result = GetExceptionHandlerFromTable();
                Console.WriteLine($"   Exception Table result: {result}");
                return result;
            }
            
            // Fallback to legacy SETUP_EXCEPT stack for compatibility
            var legacyResult = ExceptionHandlers.Count > 0 ? (int?)ExceptionHandlers.Peek() : null;
            Console.WriteLine($"   Legacy handler result: {legacyResult}");
            return legacyResult;
        }
        
        // CPython 3.12 Exception Table lookup
        private int? GetExceptionHandlerFromTable()
        {
            var currentOffset = InstructionPointer;
            Console.WriteLine($"🔍 Searching Exception Table for offset {currentOffset}:");
            
            // Search Exception Table for a handler covering current instruction
            foreach (var entry in Code.ExceptionTable)
            {
                Console.WriteLine($"   Entry: Start={entry.StartOffset}, End={entry.EndOffset}, Handler={entry.HandlerOffset}");
                Console.WriteLine($"   Check: {currentOffset} >= {entry.StartOffset} && {currentOffset} < {entry.EndOffset}");
                
                if (currentOffset >= entry.StartOffset && currentOffset < entry.EndOffset)
                {
                    Console.WriteLine($"✅ Exception Table: MATCH! Handler at {entry.HandlerOffset} for instruction {currentOffset}");
                    return entry.HandlerOffset;
                }
                else
                {
                    Console.WriteLine($"❌ No match for this entry");
                }
            }
            
            Console.WriteLine($"❌ Exception Table: No handler found for instruction {currentOffset}");
            return null;
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
            Console.WriteLine($"🚀 ExecuteModule: Starting execution of {codeObject.Name}");
            Console.WriteLine($"   Exception Table entries: {codeObject.ExceptionTable.Count}");
            if (codeObject.ExceptionTable.Count > 0)
            {
                for (int i = 0; i < codeObject.ExceptionTable.Count; i++)
                {
                    var entry = codeObject.ExceptionTable[i];
                    Console.WriteLine($"     [{i}] Start: {entry.StartOffset}, End: {entry.EndOffset}, Handler: {entry.HandlerOffset}");
                }
            }
            
            var frame = new PyFrame(codeObject, new PyObject[0], _globalScope);
            return ExecuteFrame(frame);
        }
        
        // 메인 모듈 실행 (특정 스코프 체인 사용)
        public PyObject ExecuteModule(PyCodeObject codeObject, PyScopeChain scopeChain)
        {
            Console.WriteLine($"🚀 ExecuteModule (with scopeChain): Starting execution of {codeObject.Name}");
            Console.WriteLine($"   Exception Table entries: {codeObject.ExceptionTable.Count}");
            if (codeObject.ExceptionTable.Count > 0)
            {
                for (int i = 0; i < codeObject.ExceptionTable.Count; i++)
                {
                    var entry = codeObject.ExceptionTable[i];
                    Console.WriteLine($"     [{i}] Start: {entry.StartOffset}, End: {entry.EndOffset}, Handler: {entry.HandlerOffset}");
                }
            }
            
            Console.WriteLine($"🔍 PyFrame 생성 직전 codeObject.ExceptionTable.Count: {codeObject.ExceptionTable.Count}");
            var frame = new PyFrame(codeObject, new PyObject[0], scopeChain);
            Console.WriteLine($"🔍 PyFrame 생성 후 frame.Code.ExceptionTable.Count: {frame.Code.ExceptionTable.Count}");
            return ExecuteFrame(frame);
        }
        
        // 프레임 실행 (바이트코드 해석)
        // CPython 3.12: Execute class body and return namespace
        public Dictionary<string, PyObject> ExecuteClassBody(PyCodeObject classBody)
        {
            // Store the original global scope state to detect new variables
            Dictionary<string, PyObject> originalGlobals = null;
            PyScopeChain parentScope = null;
            
            if (_frameStack.Count > 0)
            {
                parentScope = _frameStack.Peek().ScopeChain;
                // Capture original global state
                originalGlobals = new Dictionary<string, PyObject>(parentScope.GlobalScope.Variables);
            }
            
            var frame = new PyFrame(classBody, new PyObject[0], parentScope);
            var result = ExecuteFrame(frame);
            
            // Extract class namespace - capture variables added during class body execution
            var classNamespace = new Dictionary<string, PyObject>();
            
            // Method 1: FastLocals (for STORE_FAST operations)
            foreach (var kvp in frame.FastLocals)
            {
                classNamespace[kvp.Key] = kvp.Value;
            }
            
            // Method 2: Local scope variables
            if (frame.ScopeChain?.CurrentScope != null)
            {
                foreach (var kvp in frame.ScopeChain.CurrentScope.Variables)
                {
                    classNamespace[kvp.Key] = kvp.Value;
                }
            }
            
            // Method 3: New global variables (added by class body STORE_GLOBAL operations)
            if (frame.ScopeChain?.GlobalScope != null && originalGlobals != null)
            {
                foreach (var kvp in frame.ScopeChain.GlobalScope.Variables)
                {
                    // Only include variables that were added during class body execution
                    if (!originalGlobals.ContainsKey(kvp.Key) || 
                        !ReferenceEquals(originalGlobals[kvp.Key], kvp.Value))
                    {
                        classNamespace[kvp.Key] = kvp.Value;
                    }
                }
            }
            
            Console.WriteLine($"📦 ExecuteClassBody captured {classNamespace.Count} variables:");
            foreach (var kvp in classNamespace)
            {
                Console.WriteLine($"  - {kvp.Key}: {kvp.Value?.GetType().Name}");
            }
            
            return classNamespace;
        }

        public PyObject ExecuteFrame(PyFrame frame)
        {
            _frameStack.Push(frame);
            
            Console.WriteLine($"\n🚀 VM 실행: {frame}");
            
            // 🛡️ 무한루프 방지 안전장치
            var startTime = DateTime.UtcNow;
            var maxInstructions = 1000000; // 최대 100만 명령어
            var maxTimeSeconds = 30; // 최대 30초
            var instructionCount = 0;
            
            try
            {
                while (frame.InstructionPointer < frame.Code.Instructions.Count)
                {
                    // 🛡️ 안전장치 검사
                    instructionCount++;
                    if (instructionCount % 10000 == 0) // 1만 명령어마다 검사
                    {
                        var elapsed = DateTime.UtcNow - startTime;
                        if (elapsed.TotalSeconds > maxTimeSeconds)
                        {
                            throw new PythonException(new PyRuntimeError($"Execution timeout after {elapsed.TotalSeconds:F1} seconds"));
                        }
                        if (instructionCount > maxInstructions)
                        {
                            throw new PythonException(new PyRuntimeError($"Instruction limit exceeded: {instructionCount} instructions"));
                        }
                    }
                    
                    var instruction = frame.Code.Instructions[frame.InstructionPointer];
                    
                    // CPython-style error location tracking: Update current execution location
                    if (instruction.LineNumber > 0)
                    {
                        frame.CurrentLineNumber = instruction.LineNumber;
                    }
                    if (instruction.ColumnOffset >= 0)
                    {
                        frame.CurrentColumnOffset = instruction.ColumnOffset;
                    }
                    if (!string.IsNullOrEmpty(instruction.FileName))
                    {
                        frame.CurrentFileName = instruction.FileName;
                    }
                    
                    if (SharpPyConfig.ShouldShowDebugInfo && frame.ValueStack.Count <= 10) // 스택이 너무 크지 않을 때만 출력
                    {
                        var stackContents = string.Join(", ", frame.ValueStack.Reverse().Take(5));
                        Console.WriteLine($"  {frame.InstructionPointer*2,3}: {instruction,-25} 스택:[{stackContents}]");
                    }
                    
                    try
                    {
                        var result = ExecuteInstruction(frame, instruction);
                        
                        // RETURN_VALUE인 경우 함수 종료
                        if (result != null)
                        {
                            if (SharpPyConfig.ShouldShowDebugInfo)
                            {
                                Console.WriteLine($"✅ VM 완료: {result}");
                            }
                            return result;
                        }
                        
                        frame.InstructionPointer++;
                    }
                    catch (PythonException pyEx)
                    {
                        // CPython-style error location tracking: Enrich exception with current location
                        if (string.IsNullOrEmpty(pyEx.FileName) && !string.IsNullOrEmpty(frame.CurrentFileName))
                        {
                            pyEx.FileName = frame.CurrentFileName;
                            pyEx.LineNumber = frame.CurrentLineNumber;
                            pyEx.ColumnOffset = frame.CurrentColumnOffset;
                            pyEx.SourceLines = frame.Code.SourceLines; // Add source lines for context display
                            
                            // Debug: Show enriched exception info
                            if (SharpPyConfig.ShouldShowDebugInfo)
                            {
                                Console.WriteLine($"🔍 Exception enriched: {pyEx.FileName}:{pyEx.LineNumber}:{pyEx.ColumnOffset}");
                                Console.WriteLine($"🔍 Source lines available: {pyEx.SourceLines?.Count ?? 0}");
                                Console.WriteLine($"🔍 Full exception: {pyEx}");
                            }
                        }
                        
                        // Handle Python exceptions with proper exception handler routing
                        var handlerOffset = frame.GetExceptionHandler();
                        if (handlerOffset.HasValue)
                        {
                            // Push the exception onto the stack and jump to handler
                            frame.ValueStack.Push(pyEx.PyException);
                            frame.LastException = pyEx.PyException;
                            frame.CurrentException = pyEx.PyException; // Set for PUSH_EXC_INFO
                            Console.WriteLine($"🔧 Exception handled: jumping to handler at offset {handlerOffset.Value}");
                            Console.WriteLine($"🔧 Stack after exception push: {frame.ValueStack.Count} items");
                            Console.WriteLine($"🔧 Total instructions: {frame.Code.Instructions.Count}");
                            Console.WriteLine($"🔧 Handler offset {handlerOffset.Value} → instruction index: {handlerOffset.Value}");
                            
                            // CPython 3.12 compatibility: SharpPy Exception Table stores instruction indices, not byte offsets
                            var instructionIndex = handlerOffset.Value;
                            if (instructionIndex >= 0 && instructionIndex < frame.Code.Instructions.Count)
                            {
                                frame.InstructionPointer = instructionIndex;
                                Console.WriteLine($"🔧 Jumping to instruction {instructionIndex}: {frame.Code.Instructions[instructionIndex].OpCode}");
                            }
                            else
                            {
                                Console.WriteLine($"❌ Invalid handler instruction index: {instructionIndex}");
                                frame.InstructionPointer = handlerOffset.Value; // Fallback to original
                            }
                        }
                        else
                        {
                            // No handler - re-throw with location information
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
                    
                case ByteCodeOp.CACHE:
                    // CPython 3.12 inline caching - NOP for compatibility
                    break;
                    
                case ByteCodeOp.POP_TOP:
                    frame.ValueStack.Pop();
                    break;
                    
                // CPython 3.12: DUP_TOP removed, use COPY 1
                    
                case ByteCodeOp.COPY:
                    // CPython 3.12: Copy the Nth element from stack top (1-indexed)
                    var copyIndex = instruction.Argument;
                    if (frame.ValueStack.Count == 0)
                    {
                        throw PyRuntimeError.Create($"COPY: Stack empty when trying to copy index {copyIndex}. This may be caused by incorrect match-case bytecode generation.");
                    }
                    if (copyIndex <= 0 || copyIndex > frame.ValueStack.Count)
                    {
                        throw PyRuntimeError.Create($"COPY index {copyIndex} out of range (stack size: {frame.ValueStack.Count}). Stack contents: [{string.Join(", ", frame.ValueStack.Take(5).Select(x => x.GetType().Name))}]");
                    }
                    var valueToCopy = frame.ValueStack.ElementAt(frame.ValueStack.Count - copyIndex);
                    frame.ValueStack.Push(valueToCopy);
                    break;
                    
                case ByteCodeOp.SWAP:
                    // CPython 3.12: SWAP i - Exchange top i elements with the next i elements
                    var swapCount = instruction.Argument;
                    if (frame.ValueStack.Count < swapCount * 2)
                    {
                        throw PyRuntimeError.Create($"SWAP: Not enough items on stack (need {swapCount * 2}, got {frame.ValueStack.Count})");
                    }
                    
                    // CPython 3.12 SWAP behavior: SWAP i exchanges TOS with TOS-i
                    var temp = new PyObject[swapCount];
                    
                    // Pop top i elements
                    for (int j = 0; j < swapCount; j++)
                    {
                        temp[j] = frame.ValueStack.Pop();
                    }
                    
                    // Get the element at position i (which is now at top)
                    var swapElement = frame.ValueStack.Pop();
                    
                    // Push back the temp elements in reverse order
                    for (int j = swapCount - 1; j >= 0; j--)
                    {
                        frame.ValueStack.Push(temp[j]);
                    }
                    
                    // Push the swapped element on top
                    frame.ValueStack.Push(swapElement);
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
                    // CPython 3.12 style: Direct array access optimization for first few locals
                    var argIndex = instruction.Argument;
                    if (argIndex < frame.Code.VarNames.Count)
                    {
                        var varName = frame.Code.VarNames[argIndex];
                        if (frame.FastLocals.TryGetValue(varName, out var fastValue))
                        {
                            frame.ValueStack.Push(fastValue);
                        }
                        else
                        {
                            throw PyNameError.Create($"local variable '{varName}' referenced before assignment");
                        }
                    }
                    else
                    {
                        throw PyRuntimeError.Create($"LOAD_FAST: index {argIndex} out of range");
                    }
                    break;

                case ByteCodeOp.LOAD_FAST_AND_CLEAR:
                    // CPython 3.12 PEP 709: Load variable and clear it from locals (for comprehensions)
                    var clearArgIndex = instruction.Argument;
                    if (clearArgIndex < frame.Code.VarNames.Count)
                    {
                        var clearVarName = frame.Code.VarNames[clearArgIndex];
                        if (frame.FastLocals.TryGetValue(clearVarName, out var clearValue))
                        {
                            frame.ValueStack.Push(clearValue);
                            // Clear the variable from locals (PEP 709 requirement)
                            frame.FastLocals.Remove(clearVarName);
                            Console.WriteLine($"🧹 LOAD_FAST_AND_CLEAR: loaded {clearVarName}={clearValue}, cleared from locals");
                        }
                        else
                        {
                            throw PyNameError.Create($"local variable '{clearVarName}' referenced before assignment");
                        }
                    }
                    else
                    {
                        throw PyRuntimeError.Create($"LOAD_FAST_AND_CLEAR: index {clearArgIndex} out of range");
                    }
                    break;
                    
                case ByteCodeOp.LOAD_FAST_CHECK:
                    // CPython 3.12: LOAD_FAST_CHECK - Load fast local with NULL check  
                    var checkIndex = instruction.Argument;
                    if (checkIndex < frame.Code.VarNames.Count)
                    {
                        var checkVarName = frame.Code.VarNames[checkIndex];
                        if (frame.FastLocals.TryGetValue(checkVarName, out var checkValue) && checkValue != null)
                        {
                            frame.ValueStack.Push(checkValue);
                        }
                        else
                        {
                            // More specific error than LOAD_FAST
                            throw PyNameError.Create($"local variable '{checkVarName}' referenced before assignment");
                        }
                    }
                    else
                    {
                        throw PyRuntimeError.Create($"LOAD_FAST_CHECK: index {checkIndex} out of range");
                    }
                    break;

                // CPython 3.12 Superinstructions - 연속된 바이트코드를 하나로 최적화
                // CPython 3.12: LOAD_FAST_LOAD_FAST super-instruction removed
                    
                // CPython 3.12: LOAD_CONST_LOAD_FAST super-instruction removed
                    
                // CPython 3.12: STORE_FAST_LOAD_FAST super-instruction removed
                    
                // CPython 3.12: STORE_FAST_STORE_FAST super-instruction removed
                    
                case ByteCodeOp.STORE_FAST:
                    // CPython 3.12 style: Direct array access for fast locals
                    var storeIndex = instruction.Argument;
                    if (storeIndex < frame.Code.VarNames.Count)
                    {
                        var varName = frame.Code.VarNames[storeIndex];
                        var storeVal = frame.ValueStack.Pop();
                        frame.FastLocals[varName] = storeVal;
                        frame.ScopeChain.AssignVariable(varName, storeVal);
                    }
                    else
                    {
                        throw PyRuntimeError.Create($"STORE_FAST: index {storeIndex} out of range");
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
                    
                case ByteCodeOp.IS_OP:
                    // CPython 3.12: IS_OP - Identity comparison (is/is not)
                    // Stack: [left, right] -> [result]
                    // arg=0: is, arg=1: is not
                    var isRight = frame.ValueStack.Pop();
                    var isLeft = frame.ValueStack.Pop();
                    
                    bool identityResult = ReferenceEquals(isLeft, isRight);
                    
                    // arg determines inversion: 0 = is, 1 = is not
                    if (instruction.Argument == 1)
                        identityResult = !identityResult;
                        
                    frame.ValueStack.Push(PyBool.FromBool(identityResult));
                    break;
                    
                case ByteCodeOp.STORE_GLOBAL:
                    var storeGlobalName = frame.Code.Names[instruction.Argument];
                    var storeGlobalValue = frame.ValueStack.Pop();
                    frame.ScopeChain.GlobalScope.SetVariable(storeGlobalName, storeGlobalValue);
                    break;
                    
                // Duplicate LOAD_GLOBAL case removed (was LOAD_GLOBAL_BUILTIN)
                    
                case ByteCodeOp.SETUP_ANNOTATIONS:
                    // CPython 3.12: Initialize __annotations__ dictionary if not exists
                    var annotationsName = "__annotations__";
                    var globalScope = frame.ScopeChain.GlobalScope;
                    
                    // Check if __annotations__ already exists
                    var existingAnnotations = globalScope.GetVariable(annotationsName);
                    if (existingAnnotations == null)
                    {
                        // __annotations__ doesn't exist, create it
                        globalScope.SetVariable(annotationsName, new PyDict());
                    }
                    else if (existingAnnotations is not PyDict)
                    {
                        // Replace with empty dict if it's not a dict
                        globalScope.SetVariable(annotationsName, new PyDict());
                    }
                    // If it exists and is already a dict, do nothing
                    break;
                    
                case ByteCodeOp.BINARY_OP:
                    // CPython 3.12+ unified binary operation
                    var operation = (BinaryOpType)instruction.Argument;
                    var right = frame.ValueStack.Pop();
                    var left = frame.ValueStack.Pop();
                    var result = ExecuteBinaryOpType(left, right, operation);
                    frame.ValueStack.Push(result);
                    break;
                    
                // ===============================================
                // CPython 3.12: 모든 Binary Operations는 BINARY_OP로 통합됨
                // Legacy individual binary opcodes는 더 이상 지원하지 않음
                // ===============================================
                    
                // Python 3.12 새로운 호출 시스템
                case ByteCodeOp.PUSH_NULL:
                    // NULL을 스택에 푸시 - Python 3.12에서 함수 호출 전에 사용
                    frame.ValueStack.Push(PyNone.Instance); // NULL 대신 None 사용
                    break;
                    
                case ByteCodeOp.CALL:
                    // CPython 3.12 정확한 CALL 동작
                    var callArgCount = instruction.Argument;
                    var callArgs = new PyObject[callArgCount];
                    
                    // 명시적 인수들을 스택에서 팝 (역순으로) - 스택 최상위부터
                    for (int i = callArgCount - 1; i >= 0; i--)
                    {
                        callArgs[i] = frame.ValueStack.Pop();
                    }
                    
                    // 함수 객체 팝 (callable) - 인수들 아래에 있음
                    var callableFunc = frame.ValueStack.Pop();
                    
                    // 다음 요소 확인 (NULL 또는 첫 번째 암시적 인수) - 최하위
                    var nextElement = frame.ValueStack.Pop();
                    
                    // CPython 3.12 호출 방식 결정
                    PyObject newCallResult;
                    PyObject[] finalArgs;
                    PyObject actualCallable;
                    
                    if (nextElement == null || nextElement.Equals(PyNone.Instance))
                    {
                        // PUSH_NULL 패턴: 일반 함수 호출
                        actualCallable = callableFunc;
                        finalArgs = callArgs;
                    }
                    else
                    {
                        // 데코레이터 패턴: nextElement is the decorator, callableFunc is the implicit first argument
                        actualCallable = nextElement;
                        finalArgs = new PyObject[callArgs.Length + 1];
                        finalArgs[0] = callableFunc;  // The function being decorated
                        Array.Copy(callArgs, 0, finalArgs, 1, callArgs.Length);
                    }
                    
                    // 함수 호출 실행
                    if (actualCallable is PyBuiltinFunction builtin)
                    {
                        newCallResult = builtin.Call(finalArgs);
                    }
                    else if (actualCallable is PyMethod method)
                    {
                        newCallResult = method.Call(finalArgs);
                    }
                    else if (actualCallable is PyFunction func)
                    {
                        newCallResult = ExecuteFunctionCall(func, finalArgs, frame.ScopeChain);
                    }
                    else
                    {
                        newCallResult = actualCallable.Call(finalArgs);
                    }
                    
                    frame.ValueStack.Push(newCallResult);
                    break;
                    
                case ByteCodeOp.RESUME:
                    // Python 3.12: 모든 코드 시작점에 있는 명령어
                    // 실제로는 아무것도 하지 않음 (단순 마커)
                    break;
                    
                // Duplicate CALL case removed (was CALL_FUNCTION_KW)
                    
                case ByteCodeOp.MAKE_FUNCTION:
                    // CPython 3.12 compatible function creation with full flags support
                    var flags = instruction.Argument;
                    var codeObject = frame.ValueStack.Pop();
                    
                    PyCell[] closure = null;
                    PyTuple defaults = null;
                    PyTuple kwDefaults = null;
                    PyTuple annotations = null;
                    
                    Console.WriteLine($"🔧 MAKE_FUNCTION with flags: {flags:X} (binary: {Convert.ToString(flags, 2)})");
                    
                    // CPython 3.12 MAKE_FUNCTION flags processing order (bit order matters!):
                    // 0x01 - HAS_DEFAULTS: function has positional default parameters  
                    // 0x02 - HAS_KW_DEFAULTS: function has keyword-only default parameters
                    // 0x04 - HAS_ANNOTATIONS: function has annotations
                    // 0x08 - HAS_CLOSURE: function uses closure variables
                    // 0x10 - HAS_QUALNAME: function has qualified name (not used in basic implementation)
                    
                    // Process in reverse stack order (last pushed = first popped)
                    
                    // Check for closure flag (8 = HAS_CLOSURE) - processed first due to stack order
                    if ((flags & 8) != 0)
                    {
                        var closureTuple = frame.ValueStack.Pop();
                        if (closureTuple is PyTuple closureTupleObj)
                        {
                            closure = closureTupleObj.Items.Cast<PyCell>().ToArray();
                            Console.WriteLine($"  → Function has closure: {closure.Length} cells");
                        }
                        else
                        {
                            Console.WriteLine($"  ⚠️ Warning: Expected tuple for closure, got {closureTuple?.GetType()}");
                            closure = new PyCell[0];
                        }
                    }
                    
                    // Check for annotations flag (4 = HAS_ANNOTATIONS)
                    if ((flags & 4) != 0)
                    {
                        var annotationsTuple = frame.ValueStack.Pop();
                        if (annotationsTuple is PyTuple annTuple)
                        {
                            annotations = annTuple;
                            Console.WriteLine($"  → Function has annotations: {annTuple.Items.Length} items");
                        }
                        else
                        {
                            Console.WriteLine($"  ⚠️ Warning: Expected tuple for annotations, got {annotationsTuple?.GetType()}");
                            annotations = new PyTuple(new PyObject[0]);
                        }
                    }
                    
                    // Check for keyword-only defaults flag (2 = HAS_KW_DEFAULTS)  
                    if ((flags & 2) != 0)
                    {
                        var kwDefaultsTuple = frame.ValueStack.Pop();
                        if (kwDefaultsTuple is PyTuple kwDefTuple)
                        {
                            kwDefaults = kwDefTuple;
                            Console.WriteLine($"  → Function has keyword-only defaults: {kwDefTuple.Items.Length} items");
                        }
                        else
                        {
                            Console.WriteLine($"  ⚠️ Warning: Expected tuple for kw-defaults, got {kwDefaultsTuple?.GetType()}");
                            kwDefaults = new PyTuple(new PyObject[0]);
                        }
                    }
                    
                    // Check for positional defaults flag (1 = HAS_DEFAULTS)
                    if ((flags & 1) != 0)
                    {
                        var defaultsTuple = frame.ValueStack.Pop();
                        if (defaultsTuple is PyTuple defTuple)
                        {
                            defaults = defTuple;
                            Console.WriteLine($"  → Function has positional defaults: {defTuple.Items.Length} parameters");
                        }
                        else
                        {
                            Console.WriteLine($"  ⚠️ Warning: Expected tuple for defaults, got {defaultsTuple?.GetType()}");
                            defaults = new PyTuple(new PyObject[0]);
                        }
                    }
                    
                    if (codeObject is PyCodeObject pyCode)
                    {
                        // CPython 3.12: async def로 정의된 함수인지 확인
                        if (pyCode.IsAsyncGenerator())
                        {
                            // Async generator: 호출 시 PyAsyncGenerator 객체 반환
                            var asyncGenImpl = new Func<PyObject[], PyObject>(args =>
                            {
                                var boundArgs = BindFunctionArguments(args, pyCode, defaults);
                                var asyncGenFrame = closure != null && closure.Length > 0 
                                    ? new PyFrame(pyCode, boundArgs, frame.ScopeChain, closure)
                                    : new PyFrame(pyCode, boundArgs, frame.ScopeChain);
                                
                                // Async generator 생성
                                var enumerator = new FrameGeneratorEnumerator(asyncGenFrame, this);
                                return new SharpPy.Core.PyAsyncGenerator(enumerator, pyCode.Name);
                            });
                            
                            var asyncGenFunction = new PyFunction(pyCode.Name, asyncGenImpl, null, null, closure, pyCode);
                            
                            // Set CPython 3.12 compatible function attributes
                            if (defaults != null)
                            {
                                asyncGenFunction.SetAttribute("__defaults__", defaults);
                            }
                            if (kwDefaults != null)
                            {
                                asyncGenFunction.SetAttribute("__kwdefaults__", kwDefaults);
                            }
                            
                            frame.ValueStack.Push(asyncGenFunction);
                            Console.WriteLine($"✅ Created async generator function: {pyCode.Name}");
                        }
                        else if (pyCode.IsCoroutine())
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
                            
                            // Set CPython 3.12 compatible function attributes
                            if (defaults != null)
                            {
                                asyncFunction.SetAttribute("__defaults__", defaults);
                            }
                            if (kwDefaults != null)
                            {
                                asyncFunction.SetAttribute("__kwdefaults__", kwDefaults);
                            }
                            if (annotations != null)
                            {
                                asyncFunction.SetAttribute("__annotations__", annotations);
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
                        
                            // Set CPython 3.12 compatible function attributes
                            if (defaults != null)
                            {
                                functionObject.SetAttribute("__defaults__", defaults);
                            }
                            if (kwDefaults != null)
                            {
                                functionObject.SetAttribute("__kwdefaults__", kwDefaults);
                            }
                            if (annotations != null)
                            {
                                functionObject.SetAttribute("__annotations__", annotations);
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
                    
                // CPython 3.12: Pattern matching opcodes
                case ByteCodeOp.MATCH_MAPPING:
                    // Check if subject is a mapping type (dict, etc.)
                    var mappingSubject = frame.ValueStack.Peek(); // Keep subject on stack
                    var isMapping = (mappingSubject is PyDict) ? PyBool.True : PyBool.False;
                    frame.ValueStack.Push(isMapping);
                    break;
                    
                case ByteCodeOp.MATCH_SEQUENCE:
                    // Check if subject is a sequence type (list, tuple, etc.)
                    var sequenceSubject = frame.ValueStack.Peek(); // Keep subject on stack
                    var isSequence = (sequenceSubject is PyList || sequenceSubject is PyTuple || sequenceSubject is PyString) 
                        ? PyBool.True : PyBool.False;
                    frame.ValueStack.Push(isSequence);
                    break;
                    
                case ByteCodeOp.GET_LEN:
                    // Get length of object on top of stack
                    var lenSubject = frame.ValueStack.Peek(); // Keep subject on stack
                    PyObject length;
                    if (lenSubject is PyList list)
                    {
                        length = new PyInt(list.Items.Length);
                    }
                    else if (lenSubject is PyTuple tupleDup)
                    {
                        length = new PyInt(tupleDup.Items.Length);
                    }
                    else if (lenSubject is PyString str)
                    {
                        length = new PyInt(str.Value.Length);
                    }
                    else if (lenSubject is PyDict dictDup)
                    {
                        length = new PyInt(dictDup.Keys().Items.Length);
                    }
                    else
                    {
                        // Try to call __len__ method
                        try
                        {
                            var lenMethod = lenSubject.GetAttribute("__len__");
                            if (lenMethod is PyFunction func)
                            {
                                length = func.Call(new PyObject[0]);
                            }
                            else
                            {
                                throw new Exception($"'{lenSubject.GetTypeName()}' object has no len()");
                            }
                        }
                        catch
                        {
                            throw PyTypeError.Create($"object of type '{lenSubject.GetTypeName()}' has no len()");
                        }
                    }
                    frame.ValueStack.Push(length);
                    break;
                    
                case ByteCodeOp.MATCH_KEYS:
                    // Match keys in mapping - TOS1 is subject, TOS is keys tuple
                    var keysToMatch = frame.ValueStack.Pop(); // keys tuple
                    var dictSubject = frame.ValueStack.Peek(); // Keep subject on stack
                    
                    if (dictSubject is PyDict dict && keysToMatch is PyTuple keysTuple)
                    {
                        var values = new List<PyObject>();
                        bool allKeysMatch = true;
                        
                        foreach (var key in keysTuple.Items)
                        {
                            if (dict.Contains(key).ToBool())
                            {
                                var foundValue = dict.GetItem(key);
                                values.Add(foundValue);
                            }
                            else
                            {
                                allKeysMatch = false;
                                break;
                            }
                        }
                        
                        if (allKeysMatch)
                        {
                            var resultTuple = new PyTuple(values.ToArray());
                            frame.ValueStack.Push(resultTuple);
                        }
                        else
                        {
                            frame.ValueStack.Push(PyNone.Instance); // CPython 3.12: None on failure
                        }
                    }
                    else
                    {
                        frame.ValueStack.Push(PyNone.Instance);
                    }
                    break;
                    
                case ByteCodeOp.POP_JUMP_IF_NONE:
                    // Pop top value and jump if it's None
                    var valueToCheck = frame.ValueStack.Pop();
                    if (valueToCheck == null || valueToCheck.Equals(PyNone.Instance))
                    {
                        frame.InstructionPointer = instruction.Argument;
                    }
                    break;
                    
                case ByteCodeOp.MATCH_CLASS:
                    // Match class pattern - CPython 3.12 compatible implementation
                    var classKwNames = frame.ValueStack.Pop(); // keyword names tuple (unused for now)
                    var classToMatch = frame.ValueStack.Pop(); // class to match against
                    var classSubject = frame.ValueStack.Pop(); // subject to match
                    
                    try 
                    {
                        // Check isinstance(subject, classToMatch)
                        bool isInstance = false;
                        if (classToMatch is PyClass targetClass && classSubject is PyClassInstance instance)
                        {
                            isInstance = (instance.InstanceType == targetClass);
                        }
                        
                        if (isInstance)
                        {
                            // Extract positional attributes based on __match_args__
                            var positionalCount = instruction.Argument;
                            var attrs = new List<PyObject>();
                            
                            if (classToMatch is PyClass cls && cls.GetAttribute("__match_args__") is PyTuple matchArgs)
                            {
                                for (int i = 0; i < Math.Min(positionalCount, matchArgs.Items.Length); i++)
                                {
                                    var matchArgName = matchArgs.Items[i].ToStr();
                                    
                                    if (classSubject is PyClassInstance subjectInstance)
                                    {
                                        var attrValue = subjectInstance.GetAttribute(matchArgName);
                                        attrs.Add(attrValue ?? PyNone.Instance);
                                    }
                                }
                                
                                var resultTuple = new PyTuple(attrs.ToArray());
                                frame.ValueStack.Push(resultTuple);
                            }
                            else
                            {
                                frame.ValueStack.Push(PyNone.Instance);
                            }
                        }
                        else
                        {
                            frame.ValueStack.Push(PyNone.Instance); // CPython 3.12: None on failure
                        }
                    }
                    catch (Exception ex)
                    {
                        frame.ValueStack.Push(PyNone.Instance);
                    }
                    break;
                    
                case ByteCodeOp.RETURN_VALUE:
                    var returnValue = frame.ValueStack.Count > 0 ? frame.ValueStack.Pop() : PyNone.Instance;
                    return returnValue;
                    
                case ByteCodeOp.RETURN_CONST:
                    // CPython 3.12: Return constant value directly
                    var constValue = frame.Code.Constants[instruction.Argument];
                    return constValue;
                    
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
                        // CPython 3.12: POP_JUMP_IF_TRUE uses absolute byte offset
                        // Convert byte offset back to instruction index
                        int targetInstructionIndex = instruction.Argument / 2;
                        Console.WriteLine($"🔄 POP_JUMP_IF_TRUE: condition True, jump to instr {targetInstructionIndex} (byte offset {instruction.Argument})");
                        // Subtract 1 because main loop will increment
                        frame.InstructionPointer = targetInstructionIndex - 1;
                        return null; // Continue execution from new position
                    }
                    break;
                    
                case ByteCodeOp.POP_JUMP_IF_FALSE:
                    var falseValue = frame.ValueStack.Pop();
                    if (!falseValue.PyBoolValue())
                    {
                        // CPython 3.12: POP_JUMP_IF_FALSE uses relative offset from next instruction
                        int currentPosJump = frame.InstructionPointer;
                        int relativeOffset = instruction.Argument;
                        int targetInstructionIndex = currentPosJump + 1 + relativeOffset;
                        Console.WriteLine($"🔄 POP_JUMP_IF_FALSE: condition False, jump from {currentPosJump} + 1 + {relativeOffset} to instr {targetInstructionIndex}");
                        // Subtract 1 because main loop will increment
                        frame.InstructionPointer = targetInstructionIndex - 1;
                        return null; // Continue execution from new position
                    }
                    break;
                    
                case ByteCodeOp.JUMP_FORWARD:
                    // CPython 3.12: JUMP_FORWARD uses relative offset from next instruction
                    // argument is the number of instructions to skip forward
                    int currentPos = frame.InstructionPointer;
                    int jumpOffset = instruction.Argument;
                    // Target = current instruction + 1 (next) + jump offset
                    int targetPos = currentPos + 1 + jumpOffset;
                    
                    Console.WriteLine($"🔄 JUMP_FORWARD: from instr {currentPos} forward {jumpOffset} to instr {targetPos}");
                    
                    // Subtract 1 because main loop will increment
                    frame.InstructionPointer = targetPos - 1;
                    return null; // Continue execution from new position
                    
                case ByteCodeOp.JUMP_BACKWARD:
                    // CPython 3.12 compatible: JUMP_BACKWARD uses relative offset
                    // instruction.Argument contains the number of instructions to jump backward
                    // CPython: JUMPBY(-oparg) means current position - oparg instructions
                    int currentInstrPos = frame.InstructionPointer;
                    int jumpBackCount = instruction.Argument;  // Number of instructions to jump back
                    int targetInstrPos = currentInstrPos - jumpBackCount;
                    Console.WriteLine($"🔄 JUMP_BACKWARD: from instr {currentInstrPos} back {jumpBackCount} instrs to instr {targetInstrPos} (CPython 3.12 relative)");
                    // Direct jump to target position (subtract 1 because main loop will increment)
                    frame.InstructionPointer = targetInstrPos - 1;
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
                    
                case ByteCodeOp.BUILD_SLICE:
                    // Build slice object - argument is 2 or 3
                    var sliceArgCount = instruction.Argument;
                    if (sliceArgCount == 2)
                    {
                        // Stack: [start, stop] -> [slice(start, stop)]
                        var buildSliceStop = frame.ValueStack.Pop();
                        var buildSliceStart = frame.ValueStack.Pop();
                        var buildSliceObj = new PySlice(buildSliceStart, buildSliceStop);
                        frame.ValueStack.Push(buildSliceObj);
                    }
                    else if (sliceArgCount == 3)
                    {
                        // Stack: [start, stop, step] -> [slice(start, stop, step)]
                        var buildSliceStep = frame.ValueStack.Pop();
                        var buildSliceStop = frame.ValueStack.Pop();
                        var buildSliceStart = frame.ValueStack.Pop();
                        var buildSliceObj = new PySlice(buildSliceStart, buildSliceStop, buildSliceStep);
                        frame.ValueStack.Push(buildSliceObj);
                    }
                    else
                    {
                        throw new InvalidOperationException($"BUILD_SLICE with {sliceArgCount} arguments not supported");
                    }
                    break;
                    
                case ByteCodeOp.BINARY_SUBSCR:
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
                        Console.WriteLine($"🔍 BINARY_SUBSCR: Re-throwing Python exception: {ex.GetType().Name} - {ex.Message}");
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
                    
                // Duplicate BINARY_SUBSCR case removed
                    
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
                    
                case ByteCodeOp.END_FOR:
                    // CPython 3.12: END_FOR는 단순한 루프 종료 마커
                    // FOR_ITER의 StopIteration에서 이미 모든 정리 작업 완료됨
                    Console.WriteLine($"🔚 END_FOR: Loop termination marker");
                    Console.WriteLine($"    스택 상태: count={frame.ValueStack.Count}");
                    
                    if (frame.ValueStack.Count > 0)
                    {
                        var resultValue = frame.ValueStack.Peek();
                        Console.WriteLine($"    → 스택 맨 위 결과: {resultValue?.GetType().Name}");
                    }
                    
                    // CPython 3.12: END_FOR는 스택을 건드리지 않음
                    // Iterator 제거는 이미 FOR_ITER StopIteration에서 처리됨
                    break;

                // CPython-style Comparison Operations
                case ByteCodeOp.COMPARE_OP:
                    var compareRight = frame.ValueStack.Pop();
                    var compareLeft = frame.ValueStack.Pop();
                    var compareResult = CompareOperation(compareLeft, compareRight, instruction.Argument);
                    frame.ValueStack.Push(compareResult);
                    break;

                case ByteCodeOp.CONTAINS_OP:
                    var containsRight = frame.ValueStack.Pop();
                    var containsLeft = frame.ValueStack.Pop();
                    var containsResult = ContainsOperation(containsLeft, containsRight, instruction.Argument);
                    frame.ValueStack.Push(containsResult);
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

                // CPython 3.12: SETUP_EXCEPT removed - using Exception Table instead
                // case ByteCodeOp.SETUP_EXCEPT: // Legacy - no longer used in CPython 3.12
                    
                case ByteCodeOp.POP_EXCEPT:
                    // CPython 3.12: POP_EXCEPT only executes in exception paths after PUSH_EXC_INFO
                    Console.WriteLine($"🔧 POP_EXCEPT: stack size = {frame.ValueStack.Count}");
                    
                    // In CPython 3.12, POP_EXCEPT always expects PyExceptionInfo on stack
                    if (frame.ValueStack.Count > 0 && frame.ValueStack.Peek() is PyExceptionInfo)
                    {
                        // Exception path: Remove PyExceptionInfo from stack
                        var poppedExceptionInfo = frame.ValueStack.Pop();
                        Console.WriteLine($"🔧 POP_EXCEPT: Removed PyExceptionInfo from stack");
                        
                        if (poppedExceptionInfo is PyExceptionInfo exceptionInfo)
                        {
                            Console.WriteLine($"   exc_type={exceptionInfo.ExcType}, exc_value={exceptionInfo.ExcValue}");
                            Console.WriteLine($"   exc_traceback={exceptionInfo.ExcTraceback}, lasti={exceptionInfo.Lasti}");
                        }
                        
                        // CPython 3.12: Clear exception handling state after successful exception processing
                        // This prevents infinite loop in exception handling
                        frame.CurrentException = null;
                        frame.ExceptionHandlerCallCount = 0;
                        Console.WriteLine($"🔧 POP_EXCEPT: Cleared exception handling state to prevent infinite loops");
                    }
                    else
                    {
                        // CPython 3.12: This should not happen in normal execution
                        Console.WriteLine($"⚠️  POP_EXCEPT: No PyExceptionInfo on stack - this indicates a bytecode generation issue");
                        Console.WriteLine($"   In CPython 3.12, POP_EXCEPT only appears after PUSH_EXC_INFO in exception handlers");
                    }
                    
                    Console.WriteLine($"🔍 POP_EXCEPT 완료 후 스택 크기: {frame.ValueStack.Count}");
                    break;
                    
                case ByteCodeOp.BEFORE_WITH:
                    // CPython 3.12: BEFORE_WITH performs several operations before a with block starts
                    var contextManager = frame.ValueStack.Pop();
                    
                    Console.WriteLine($"🔧 BEFORE_WITH: Processing context manager: {contextManager}");
                    
                    // 1. Load __exit__ method and push to stack (for later cleanup)
                    var exitMethod = contextManager.GetAttribute("__exit__");
                    if (!exitMethod.IsCallable())
                    {
                        throw PyAttributeError.Create("__exit__");
                    }
                    
                    Console.WriteLine($"🔧 BEFORE_WITH: Found __exit__ method: {exitMethod}");
                    
                    // 2. Call __enter__ method and get result
                    var enterMethod = contextManager.GetAttribute("__enter__");
                    PyObject enterResult;
                    if (enterMethod.IsCallable())
                    {
                        enterResult = enterMethod.Call(new PyObject[0]);
                        Console.WriteLine($"🔧 BEFORE_WITH: __enter__ returned: {enterResult}");
                    }
                    else
                    {
                        throw PyAttributeError.Create("__enter__");
                    }
                    
                    // CPython 3.12 stack layout: [..., __exit__, __enter_result__]
                    // This matches the expected layout for normal completion and exception handling
                    frame.ValueStack.Push(exitMethod);
                    frame.ValueStack.Push(enterResult);
                    
                    Console.WriteLine($"🔧 BEFORE_WITH: Stack after setup - size: {frame.ValueStack.Count}");
                    Console.WriteLine($"   TOS: {frame.ValueStack.Peek()} (enter result)");
                    break;
                    
                case ByteCodeOp.PUSH_EXC_INFO:
                    // CPython 3.12: Push exception info as single composite object (Stack effect: +1)
                    // Stack: [...] -> [..., PyExceptionInfo]
                    Console.WriteLine($"🔧 PUSH_EXC_INFO: Pushing current exception info to stack");
                    
                    // Get current exception from the frame's exception handler
                    var currentException = frame.CurrentException;
                    
                    PyExceptionInfo pushExceptionInfo;
                    if (currentException != null)
                    {
                        // Create composite exception info object
                        pushExceptionInfo = new PyExceptionInfo(
                            currentException.GetPyType(),           // exc_type
                            currentException,                       // exc_value  
                            PyNone.Instance,                       // exc_traceback (simplified)
                            new PyInt(frame.InstructionPointer)    // lasti
                        );
                        
                        Console.WriteLine($"🔧 PUSH_EXC_INFO: Created exception info for {currentException.GetType().Name}");
                    }
                    else
                    {
                        // No current exception - create with None values
                        pushExceptionInfo = new PyExceptionInfo(
                            PyNone.Instance,                       // exc_type
                            PyNone.Instance,                       // exc_value
                            PyNone.Instance,                       // exc_traceback
                            new PyInt(frame.InstructionPointer)    // lasti
                        );
                        
                        Console.WriteLine($"🔧 PUSH_EXC_INFO: Created exception info with None values");
                    }
                    
                    // Push single composite object (CPython 3.12 compatible stack effect +1)
                    frame.ValueStack.Push(pushExceptionInfo);
                    Console.WriteLine($"🔧 PUSH_EXC_INFO: Pushed composite exception info, stack size = {frame.ValueStack.Count}");
                    break;
                    
                    
                case ByteCodeOp.WITH_EXCEPT_START:
                    // CPython 3.12: WITH_EXCEPT_START implementation
                    // Stack: [..., __exit__, exception, exc_type, exc_value, exc_traceback, lasti]
                    // Goal: Call __exit__(exc_type, exc_value, exc_traceback) and push result
                    
                    Console.WriteLine($"🔧 WITH_EXCEPT_START: stack size = {frame.ValueStack.Count}");
                    
                    // Debug: Print current stack contents from top to bottom
                    var debugStack = new List<PyObject>(frame.ValueStack);
                    debugStack.Reverse(); // Now from top to bottom
                    for (int i = 0; i < debugStack.Count; i++)
                    {
                        Console.WriteLine($"🔍 Stack[{i}]: {debugStack[i]}");
                    }
                    
                    // CPython 3.12: Dynamic stack validation - check for required objects by type
                    if (frame.ValueStack.Count == 0)
                    {
                        Console.WriteLine($"❌ WITH_EXCEPT_START: Empty stack");
                        frame.ValueStack.Push(PyBool.False);
                        break;
                    }
                    
                    // CPython 3.12: Stack layout after PUSH_EXC_INFO: [..., __exit__, exception, PyExceptionInfo] 
                    // Get PyExceptionInfo (TOS) - should be at top of stack
                    var exceptionInfoObj = frame.ValueStack.Pop();
                    
                    // Dynamic validation: Check if TOS is PyExceptionInfo
                    if (!(exceptionInfoObj is PyExceptionInfo))
                    {
                        Console.WriteLine($"❌ WITH_EXCEPT_START: Expected PyExceptionInfo at TOS, got {exceptionInfoObj?.GetType().Name}");
                        frame.ValueStack.Push(exceptionInfoObj); // Restore stack
                        frame.ValueStack.Push(PyBool.False);
                        break;
                    }
                    
                    // Check if we have enough items for context exit method
                    if (frame.ValueStack.Count == 0)
                    {
                        Console.WriteLine($"❌ WITH_EXCEPT_START: No context exit method on stack");
                        frame.ValueStack.Push(exceptionInfoObj); // Restore stack
                        frame.ValueStack.Push(PyBool.False);
                        break;
                    }
                    
                    // Skip exception object and get __exit__ method
                    var exceptionObj = frame.ValueStack.Pop(); // Skip exception
                    
                    if (frame.ValueStack.Count == 0)
                    {
                        Console.WriteLine($"❌ WITH_EXCEPT_START: No context exit method on stack");
                        frame.ValueStack.Push(exceptionObj);     // Restore stack
                        frame.ValueStack.Push(exceptionInfoObj);
                        frame.ValueStack.Push(PyBool.False);
                        break;
                    }
                    
                    var contextExitMethod = frame.ValueStack.Pop(); // __exit__ method
                    
                    Console.WriteLine($"🔧 WITH_EXCEPT_START: Found __exit__ method: {contextExitMethod}");
                    
                    // Already validated above, safe to cast
                    var withExceptionInfo = (PyExceptionInfo)exceptionInfoObj;
                    
                    Console.WriteLine($"🔧 WITH_EXCEPT_START: Reading exception info from PyExceptionInfo");
                    Console.WriteLine($"   exc_type={withExceptionInfo.ExcType}, exc_value={withExceptionInfo.ExcValue}");
                    Console.WriteLine($"   exc_traceback={withExceptionInfo.ExcTraceback}, lasti={withExceptionInfo.Lasti}");
                    
                    // Push items back for POP_TOP and POP_EXCEPT cleanup
                    // CPython 3.12: Need 4 items for the 4 POP operations (28: POP_TOP, 29: POP_EXCEPT, 30: POP_TOP, 31: POP_TOP)
                    // Order: Items pushed in reverse order of POP operations
                    frame.ValueStack.Push(PyNone.Instance);            // For POP_TOP (31) - bottom
                    frame.ValueStack.Push(PyNone.Instance);            // For POP_TOP (30)
                    frame.ValueStack.Push(withExceptionInfo);          // For POP_EXCEPT (29)  
                    frame.ValueStack.Push(contextExitMethod);          // For POP_TOP (28) - top
                    
                    bool suppressException = false;
                    
                    if (contextExitMethod?.IsCallable() == true)
                    {
                        try
                        {
                            // CPython 3.12: Call __exit__(exc_type, exc_value, exc_traceback)
                            var exitResult = contextExitMethod.Call(new PyObject[] { 
                                withExceptionInfo.ExcType, 
                                withExceptionInfo.ExcValue, 
                                withExceptionInfo.ExcTraceback 
                            });
                            
                            // Convert result to boolean
                            suppressException = exitResult.AsBool() == PyBool.True;
                            
                            Console.WriteLine($"✅ WITH_EXCEPT_START: __exit__ returned {exitResult} (suppress={suppressException})");
                        }
                        catch (Exception exitException)
                        {
                            Console.WriteLine($"❌ WITH_EXCEPT_START: __exit__ threw exception: {exitException.Message}");
                            suppressException = false;
                            
                            // Re-throw the new exception
                            var newPyException = ConvertToPythonException(exitException);
                            throw new PythonException(newPyException);
                        }
                    }
                    else
                    {
                        Console.WriteLine($"❌ WITH_EXCEPT_START: Not callable: {contextExitMethod?.GetType().Name}");
                        suppressException = false;
                    }
                    
                    // CPython 3.12: Push boolean result for POP_JUMP_IF_TRUE
                    frame.ValueStack.Push(PyBool.FromBool(suppressException));
                    
                    // DEBUG: 스택 상태 확인
                    Console.WriteLine($"🔍 WITH_EXCEPT_START 완료 후 스택 크기: {frame.ValueStack.Count}");
                    for (int i = 0; i < Math.Min(frame.ValueStack.Count, 5); i++)
                    {
                        var debugItem = frame.ValueStack.ToArray()[frame.ValueStack.Count - 1 - i];
                        Console.WriteLine($"  Stack[{frame.ValueStack.Count - 1 - i}]: {debugItem}");
                    }
                    Console.WriteLine($"🔧 WITH_EXCEPT_START: Pushed result = {suppressException}");
                    break;
                    
                // CPython 3.12: EXCEPT_MATCH removed, exception matching now uses IS_OP
                    
                case ByteCodeOp.CHECK_EG_MATCH:
                    // PEP 654: ExceptionGroup matching
                    // Stack before: [exception_group, exception_type]
                    // Stack after: [matched_group, remainder_group]
                    
                    Console.WriteLine($"🔧 CHECK_EG_MATCH: stack size = {frame.ValueStack.Count}");
                    
                    // CPython 3.12: CHECK_EG_MATCH 동적 스택 검증
                    var checkEgMatchRequiredStack = StackEffectAnalyzer.GetMinStackRequirement(ByteCodeOp.CHECK_EG_MATCH, instruction.Argument);
                    if (frame.ValueStack.Count < checkEgMatchRequiredStack)
                    {
                        Console.WriteLine($"❌ CHECK_EG_MATCH: Not enough items on stack (need {checkEgMatchRequiredStack}, got {frame.ValueStack.Count})");
                        frame.ValueStack.Push(PyNone.Instance);
                        frame.ValueStack.Push(PyNone.Instance);
                        break;
                    }
                    
                    var egType = frame.ValueStack.Pop();
                    var egException = frame.ValueStack.Pop();
                    
                    Console.WriteLine($"🔧 CHECK_EG_MATCH: exception={egException?.GetType().Name}, type={egType?.GetType().Name}");
                    
                    var (matched, remainder) = ExceptionGroupMatches(egException, egType);
                    
                    Console.WriteLine($"🔧 CHECK_EG_MATCH: matched={matched?.GetType().Name}, remainder={remainder?.GetType().Name}");
                    
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
                    
                case ByteCodeOp.CHECK_EXC_MATCH:
                    // CPython 3.12: Check if the exception on stack matches the expected type
                    // Stack: [..., exception_instance, exception_type] -> [..., exception_instance, bool]
                    var expectedType = frame.ValueStack.Pop();
                    var exceptionInstance = frame.ValueStack.Peek(); // Don't pop, will be used later
                    
                    bool matches = false;
                    if (exceptionInstance is PyException pyException)
                    {
                        if (expectedType is PyBuiltinType builtinType)
                        {
                            // Check if exception is instance of expected type  
                            matches = pyException.GetTypeName() == builtinType.Name;
                        }
                    }
                    
                    frame.ValueStack.Push(matches ? PyBool.True : PyBool.False);
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
                    else if (sequence is PyList listDup)
                    {
                        if (listDup.Items.Length != unpackCount)
                        {
                            throw PyValueError.Create($"not enough values to unpack (expected {unpackCount}, got {listDup.Items.Length})");
                        }
                        
                        // CPython pushes elements in reverse order
                        for (int i = listDup.Items.Length - 1; i >= 0; i--)
                        {
                            frame.ValueStack.Push(listDup.Items[i]);
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
                    
                case ByteCodeOp.UNPACK_EX:
                    // CPython 3.12: Extended unpacking with star expressions (*args)
                    // Argument encodes: lower 8 bits = count before star, upper 8 bits = count after star
                    var countBefore = instruction.Argument & 0xFF;
                    var countAfter = (instruction.Argument >> 8) & 0xFF;
                    
                    var unpackExSequence = frame.ValueStack.Pop();
                    
                    if (unpackExSequence is PyList unpackExList)
                    {
                        var items = unpackExList.Items;
                        if (items.Length < countBefore + countAfter)
                        {
                            throw PyValueError.Create($"not enough values to unpack (expected at least {countBefore + countAfter}, got {items.Length})");
                        }
                        
                        // CPython UNPACK_EX pushes in order: [before_elements..., star_list, after_elements...]
                        // For [1, 2, 3, 4, 5] with pattern [a, b, *rest]: pushes [2, 1, [3, 4, 5]]
                        // Note: before elements are pushed in reverse order for stack-based comparison
                        
                        // Extract before elements (in reverse order for correct stack comparison)
                        for (int i = countBefore - 1; i >= 0; i--)
                        {
                            frame.ValueStack.Push(items[i]);
                        }
                        
                        // Extract star elements (middle part)
                        var starCount = items.Length - countBefore - countAfter;
                        var starItems = new PyObject[starCount];
                        for (int i = 0; i < starCount; i++)
                        {
                            starItems[i] = items[countBefore + i];
                        }
                        frame.ValueStack.Push(new PyList(starItems));
                        
                        // Extract after elements (in forward order)
                        for (int i = 0; i < countAfter; i++)
                        {
                            frame.ValueStack.Push(items[items.Length - countAfter + i]);
                        }
                    }
                    else if (unpackExSequence is PyTuple unpackExTuple)
                    {
                        var items = unpackExTuple.Items;
                        if (items.Length < countBefore + countAfter)
                        {
                            throw PyValueError.Create($"not enough values to unpack (expected at least {countBefore + countAfter}, got {items.Length})");
                        }
                        
                        // CPython UNPACK_EX pushes in order: [before_elements..., star_list, after_elements...]
                        
                        // Extract before elements (in forward order)
                        for (int i = 0; i < countBefore; i++)
                        {
                            frame.ValueStack.Push(items[i]);
                        }
                        
                        // Extract star elements (middle part) as list
                        var starCount = items.Length - countBefore - countAfter;
                        var starItems = new PyObject[starCount];
                        for (int i = 0; i < starCount; i++)
                        {
                            starItems[i] = items[countBefore + i];
                        }
                        frame.ValueStack.Push(new PyList(starItems));
                        
                        // Extract after elements (in forward order)
                        for (int i = 0; i < countAfter; i++)
                        {
                            frame.ValueStack.Push(items[items.Length - countAfter + i]);
                        }
                    }
                    else
                    {
                        throw PyTypeError.Create($"cannot unpack non-sequence {unpackExSequence.GetTypeName()}");
                    }
                    break;
                    
                // CPython 3.12: BREAK_LOOP and CONTINUE_LOOP removed
                // Loop control now uses structured JUMP_FORWARD/JUMP_BACKWARD
                    
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
                    // CPython 3.12: MAKE_CELL uses CellVars indices, not VarNames
                    var makeCellIndex = instruction.Argument;
                    
                    // Bounds checking for CellVars
                    if (makeCellIndex >= frame.Code.CellVars.Count)
                    {
                        throw new IndexOutOfRangeException($"Cell index {makeCellIndex} out of range. CellVars count: {frame.Code.CellVars.Count}, CellVars: [{string.Join(", ", frame.Code.CellVars)}]");
                    }
                    
                    var cellVarName = frame.Code.CellVars[makeCellIndex];
                    
                    Console.WriteLine($"🔧 MAKE_CELL for '{cellVarName}' at cell index {makeCellIndex}");
                    Console.WriteLine($"   CellVars: [{string.Join(", ", frame.Code.CellVars)}]");
                    Console.WriteLine($"   FastLocals contains '{cellVarName}': {frame.FastLocals.ContainsKey(cellVarName)}");
                    
                    // CPython 3.12: Create cell variable (initially None for type parameters)
                    PyObject? cellValue = null;
                    if (frame.FastLocals.TryGetValue(cellVarName, out var localValue))
                    {
                        cellValue = localValue;
                        Console.WriteLine($"   Found value for '{cellVarName}': {cellValue}");
                    }
                    else
                    {
                        // For Generic Parameters function, cells start as None
                        cellValue = PyNone.Instance;
                        Console.WriteLine($"   Initializing '{cellVarName}' cell with None (Generic Parameters standard)");
                    }
                    
                    // CPython 3.12: Use makeCellIndex directly (argument is the CellVars index)
                    if (makeCellIndex < frame.Cells.Length)
                    {
                        frame.Cells[makeCellIndex].SetValue(cellValue);
                        Console.WriteLine($"   ✅ Set cell[{makeCellIndex}] '{cellVarName}' = {cellValue}");
                    }
                    else
                    {
                        Console.WriteLine($"   ❌ Invalid cell index {makeCellIndex}, Cells.Length: {frame.Cells.Length}");
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
                    
                // CPython 3.12: YIELD_FROM removed

                // CPython 3.12 슬라이싱 연산 지원
                case ByteCodeOp.BINARY_SLICE:
                    // Stack: TOS = stop, TOS1 = start, TOS2 = container
                    // Result: container[start:stop]
                    var sliceStop = frame.ValueStack.Pop();
                    var sliceStart = frame.ValueStack.Pop();
                    var sliceContainer = frame.ValueStack.Pop();
                    
                    // PySlice 객체 생성하여 실제 슬라이싱 수행
                    var slice = new PySlice(sliceStart, sliceStop);
                    var sliceResult = sliceContainer.GetItem(slice);
                    frame.ValueStack.Push(sliceResult);
                    break;
                    
                case ByteCodeOp.STORE_SLICE:
                    // Stack: TOS = value, TOS1 = stop, TOS2 = start, TOS3 = container  
                    // Result: container[start:stop] = value
                    var sliceValue = frame.ValueStack.Pop();
                    var sliceStoreStop = frame.ValueStack.Pop();
                    var sliceStoreStart = frame.ValueStack.Pop();
                    var sliceStoreContainer = frame.ValueStack.Pop();
                    
                    // PySlice 객체 생성하여 실제 슬라이스 할당 수행
                    var storeSlice = new PySlice(sliceStoreStart, sliceStoreStop);
                    sliceStoreContainer.SetItem(storeSlice, sliceValue);
                    break;
                    
                case ByteCodeOp.CALL_INTRINSIC_1:
                    var intrinsicArg1 = frame.ValueStack.Pop();
                    var intrinsicResult1 = ExecuteIntrinsicFunction1(instruction.Argument, intrinsicArg1);
                    frame.ValueStack.Push(intrinsicResult1);
                    break;
                    
                case ByteCodeOp.CALL_INTRINSIC_2:
                    // CPython 3.12: CALL_INTRINSIC_2 for exception handling
                    var arg2_2 = frame.ValueStack.Pop();
                    var arg2_1 = frame.ValueStack.Pop();
                    var result2 = ExecuteIntrinsicFunction2(instruction.Argument, arg2_1, arg2_2);
                    frame.ValueStack.Push(result2);
                    break;
                    
                default:
                    throw new NotImplementedException($"OpCode {instruction.OpCode} not implemented");
            }
            
            return null;
        }
        
        // 이항 연산 (기존 타입 시스템 활용)
        // CPython 3.12+ unified binary operation executor
        private PyObject ExecuteBinaryOpType(PyObject left, PyObject right, BinaryOpType binaryOp)
        {
            try
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
            catch (Exception ex)
            {
                // Debug: Show what kind of exception occurred
                if (SharpPyConfig.ShouldShowDebugInfo)
                {
                    Console.WriteLine($"🔍 Exception in ExecuteBinaryOpType: {ex.GetType().Name}: {ex.Message}");
                }
                throw; // Re-throw for upper-level handling
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
            // CPython 3.12 바이트코드의 실제 compare operation 값 사용
            var operation = (CompareOp)compareOp;
            return operation switch
            {
                CompareOp.EQ => left.RichCompare(right, PyObject.CompareOp.EQ),    // 40
                CompareOp.NE => left.RichCompare(right, PyObject.CompareOp.NE),    // 55
                CompareOp.LT => left.RichCompare(right, PyObject.CompareOp.LT),    // 2
                CompareOp.LE => left.RichCompare(right, PyObject.CompareOp.LE),    // 26
                CompareOp.GT => left.RichCompare(right, PyObject.CompareOp.GT),    // 68
                CompareOp.GE => left.RichCompare(right, PyObject.CompareOp.GE),    // 92
                CompareOp.EXC_MATCH => left.RichCompare(right, PyObject.CompareOp.EQ), // 8 - exception match
                _ => throw new NotImplementedException($"Compare operation {compareOp} not implemented")
            };
        }

        private PyObject ContainsOperation(PyObject left, PyObject right, int containsOp)
        {
            var operation = (ContainsOp)containsOp;
            return operation switch
            {
                ContainsOp.In => ((PyBool)right.Contains(left)),
                ContainsOp.NotIn => ((PyBool)right.Contains(left)).Not(),
                _ => throw new NotImplementedException($"Contains operation {operation} not implemented")
            };
        }

        /// <summary>
        /// CPython-style sequence unpacking
        /// </summary>

        /// <summary>
        /// Compare operation enumeration matching CPython
        /// </summary>
        // CompareOp enum은 PyBytecode.CompareOp를 사용하도록 변경됨
        
        private enum ContainsOp : int
        {
            In = 0,
            NotIn = 1
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
            // exception: actual exception instance (e.g., ValueError("message")) or PyExceptionInfo
            // exceptionType: exception class (e.g., ValueError class)
            
            // Handle PyExceptionInfo wrapper (CPython 3.12 style)
            PyException actualException = null;
            if (exception is PyExceptionInfo excInfo)
            {
                actualException = excInfo.ExcValue as PyException;
                Console.WriteLine($"🔍 ExceptionMatches: Extracted {actualException?.GetType().Name} from PyExceptionInfo");
            }
            else if (exception is PyException pyExc)
            {
                actualException = pyExc;
                Console.WriteLine($"🔍 ExceptionMatches: Direct PyException {pyExc.GetType().Name}");
            }
            
            if (actualException != null)
            {
                // Get the exception's actual type name
                string excTypeName = actualException.GetType().Name;
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
        /// Execute intrinsic function with 1 argument (CPython 3.12 완전 호환)
        /// </summary>
        private PyObject ExecuteIntrinsicFunction1(int functionId, PyObject arg)
        {
            // CPython 3.12 정확한 intrinsic function IDs (pycore_intrinsics.h 호환)
            switch (functionId)
            {
                case 0: // INTRINSIC_1_INVALID
                    throw new InvalidOperationException("Invalid intrinsic function 0");
                case 1: // INTRINSIC_PRINT (was case 0)
                    Console.WriteLine(arg.ToString());
                    return PyNone.Instance;
                case 2: // INTRINSIC_IMPORT_STAR
                    throw new NotImplementedException("INTRINSIC_IMPORT_STAR not implemented");
                case 3: // INTRINSIC_STOPITERATION_ERROR
                    throw new NotImplementedException("INTRINSIC_STOPITERATION_ERROR not implemented");
                case 4: // INTRINSIC_ASYNC_GEN_WRAP
                    throw new NotImplementedException("INTRINSIC_ASYNC_GEN_WRAP not implemented");
                case 5: // INTRINSIC_UNARY_POSITIVE
                    return arg.Positive();
                case 6: // INTRINSIC_LIST_TO_TUPLE
                    if (arg is PyList list)
                    {
                        return new PyTuple(list.Items.ToArray());
                    }
                    throw PyTypeError.Create($"INTRINSIC_LIST_TO_TUPLE expected list, got {arg.GetTypeName()}");
                case 7: // INTRINSIC_TYPEVAR ✅ 이미 정확
                    return CreateTypeVar(arg);
                case 8: // INTRINSIC_PARAMSPEC ✅ 이미 정확
                    return CreateParamSpec(arg);
                case 9: // INTRINSIC_TYPEVARTUPLE ✅ 이미 정확
                    return CreateTypeVarTuple(arg);
                case 10: // INTRINSIC_SUBSCRIPT_GENERIC ✅ 이미 정확
                    return CreateGenericSubscript(arg);
                case 11: // INTRINSIC_TYPEALIAS
                    return CreateTypeAlias(arg);
                default:
                    throw new NotImplementedException($"Intrinsic function {functionId} not implemented");
            }
        }
        
        /// <summary>
        /// Create a TypeVar for PEP 695 type parameters
        /// </summary>
        private PyObject CreateTypeVar(PyObject nameObj)
        {
            var name = nameObj.ToStr();
            // For now, create a simple placeholder object
            // In full implementation, this would create a proper TypeVar
            return new PyString($"TypeVar('{name}')");
        }
        
        /// <summary>
        /// Create a ParamSpec for PEP 612 parameter specifications
        /// </summary>
        private PyObject CreateParamSpec(PyObject nameObj)
        {
            var name = nameObj.ToStr();
            return new PyString($"ParamSpec('{name}')");
        }
        
        /// <summary>
        /// Create a TypeVarTuple for PEP 646 variadic generics
        /// </summary>
        private PyObject CreateTypeVarTuple(PyObject nameObj)
        {
            var name = nameObj.ToStr();
            return new PyString($"TypeVarTuple('{name}')");
        }
        
        /// <summary>
        /// Create generic subscript for type[T] syntax (PEP 695)
        /// </summary>
        private PyObject CreateGenericSubscript(PyObject arg)
        {
            // This handles things like Generic[T] or MyClass[T]
            return arg; // For now, just return the argument
        }
        
        /// <summary>
        /// Create a TypeAlias for PEP 613 type aliases
        /// </summary>
        private PyObject CreateTypeAlias(PyObject nameObj)
        {
            var name = nameObj.ToStr();
            return new PyString($"TypeAlias('{name}')");
        }
        
        /// <summary>
        /// Execute intrinsic function with 2 arguments (CPython 3.12)
        /// </summary>
        private PyObject ExecuteIntrinsicFunction2(int functionId, PyObject arg1, PyObject arg2)
        {
            // CPython 3.12 intrinsic function IDs for 2-argument functions
            switch (functionId)
            {
                case 1: // INTRINSIC_PREP_RERAISE_STAR - Exception Groups cleanup
                    return PrepReraiseStarExceptions(arg1, arg2);
                default:
                    throw new NotImplementedException($"Intrinsic function 2-arg {functionId} not implemented");
            }
        }
        
        /// <summary>
        /// CPython 3.12: INTRINSIC_PREP_RERAISE_STAR - prepare exceptions for reraise in except* handlers
        /// </summary>
        private PyObject PrepReraiseStarExceptions(PyObject exceptionList, PyObject handledException)
        {
            // If there are no remaining exceptions, return None
            if (exceptionList is PyList list)
            {
                if (list.Length() == 0)
                {
                    return PyNone.Instance;
                }
                
                // If there's only one exception in the list, return it directly
                if (list.Length() == 1)
                {
                    return list.Items[0];
                }
                
                // If there are multiple exceptions, create an ExceptionGroup
                if (list.Length() > 1)
                {
                    var exceptions = list.Items.Cast<PyException>().ToList();
                    return new PyExceptionGroup("unhandled exceptions", exceptions);
                }
            }
            
            return PyNone.Instance;
        }

        /// <summary>
        /// PEP 654: ExceptionGroup matching - returns (matched, remainder)
        /// </summary>
        private (PyObject?, PyObject?) ExceptionGroupMatches(PyObject exception, PyObject exceptionType)
        {
            Console.WriteLine($"🔍 ExceptionGroupMatches: exception={exception?.GetType().Name}, type={exceptionType?.GetType().Name}");
            
            if (exception is PyBaseExceptionGroup group)
            {
                Console.WriteLine($"🔍 Processing ExceptionGroup with {group.Exceptions.Count} exceptions");
                
                var matchedExceptions = new List<PyException>();
                var remainderExceptions = new List<PyException>();
                
                foreach (var exc in group.Exceptions)
                {
                    Console.WriteLine($"🔍 Checking exception: {exc.GetType().Name} vs {exceptionType?.GetType().Name}");
                    if (ExceptionMatches(exc, exceptionType))
                    {
                        Console.WriteLine($"✅ Match found: {exc.GetType().Name}");
                        matchedExceptions.Add(exc);
                    }
                    else
                    {
                        Console.WriteLine($"❌ No match: {exc.GetType().Name}");
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
                    Console.WriteLine($"📦 Created matched group with {matchedExceptions.Count} exceptions");
                }
                
                PyObject? remainder = null;
                if (remainderExceptions.Count > 0)
                {
                    if (exception is PyExceptionGroup)
                        remainder = new PyExceptionGroup(group.Message, remainderExceptions);
                    else
                        remainder = new PyBaseExceptionGroup(group.Message, remainderExceptions);
                    Console.WriteLine($"📦 Created remainder group with {remainderExceptions.Count} exceptions");
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

        /// <summary>
        /// Convert C# exception to Python exception for context manager __exit__ handling
        /// </summary>
        private PyBaseException ConvertToPythonException(Exception exception)
        {
            // Convert common .NET exceptions to appropriate Python exceptions
            if (exception is PythonException pyEx)
                return pyEx.PyException;
            if (exception is ArgumentException)
                return new PyTypeError(exception.Message);
            if (exception is InvalidOperationException)
                return new PyRuntimeError(exception.Message);
            if (exception is NotImplementedException)
                return new PyNotImplementedError(exception.Message);
            
            return new PyRuntimeError($"Exception in context manager __exit__: {exception.Message}");
        }
        
        /// <summary>
        /// CPython 3.12 style: Optimized function call execution
        /// Fast path for Python function calls without full frame creation overhead
        /// </summary>
        // Bound method 호출을 위한 오버로드
        private PyObject ExecuteFunctionCall(PyFunction pyFunc, PyObject[] args, PyScopeChain parentScope, PyObject self)
        {
            // Bound method이므로 self를 첫 번째 인수로 추가
            var argsWithSelf = new PyObject[args.Length + 1];
            argsWithSelf[0] = self;
            Array.Copy(args, 0, argsWithSelf, 1, args.Length);
            
            // Only optimize if function has code object
            if (pyFunc.CodeObject == null)
            {
                return pyFunc.Call(argsWithSelf);
            }
            
            var code = pyFunc.CodeObject;
            
            // For simple functions with no complex features, use direct execution
            if (code.CellVars?.Count == 0 && code.FreeVars?.Count == 0 && 
                !code.IsGenerator() && !code.IsCoroutine())
            {
                try
                {
                    // Create minimal frame for simple function
                    var frame = new PyFrame(code, argsWithSelf, parentScope, pyFunc.Closure);
                    return ExecuteFrame(frame);
                }
                catch (PyReturnException retEx)
                {
                    return retEx.Value;
                }
            }
            
            // Fallback to full call for complex functions
            return pyFunc.Call(argsWithSelf);
        }
        
        private PyObject ExecuteFunctionCall(PyFunction pyFunc, PyObject[] args, PyScopeChain parentScope)
        {
            // Only optimize if function has code object
            if (pyFunc.CodeObject == null)
            {
                return pyFunc.Call(args);
            }
            
            var code = pyFunc.CodeObject;
            
            // For simple functions with no complex features, use direct execution
            if (code.CellVars?.Count == 0 && code.FreeVars?.Count == 0 && 
                !code.IsGenerator() && !code.IsCoroutine())
            {
                try
                {
                    // Create minimal frame for simple function
                    var frame = new PyFrame(code, args, parentScope, pyFunc.Closure);
                    return ExecuteFrame(frame);
                }
                catch (PyReturnException retEx)
                {
                    return retEx.Value;
                }
            }
            else
            {
                // Complex functions fall back to standard path
                return pyFunc.Call(args);
            }
        }
    }

    #endregion
}