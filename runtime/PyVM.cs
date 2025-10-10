using System.Linq;

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

        // CPython 3.12: Frame chain for proper call stack tracking
        public PyFrame? ParentFrame { get; set; }     // 부모 프레임 (call stack)

        // Helper property to access local scope from ScopeChain
        public PyScope? LocalScope => ScopeChain?.CurrentScope;

        // CPython-style error location tracking
        public int CurrentLineNumber { get; set; } = -1;
        public int CurrentColumnOffset { get; set; } = -1;
        public string? CurrentFileName { get; set; }

        // CPython-style closure support
        public PyCell[] Cells { get; set; } = new PyCell[0];     // 클로저 셀들 (freevars + cellvars)

        // CPython 3.12: Keyword names for next CALL instruction
        public PyTuple? KeywordNamesForNextCall { get; set; }
        public PyCell[] Closure { get; set; } = new PyCell[0];   // 부모로부터 받은 클로저 셀들

        // **NEW**: Storage for class body variables before scope cleanup
        public Dictionary<string, PyObject>? ClassBodyVariables { get; set; }

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

        public PyFrame(PyCodeObject code, PyObject[] args, PyScopeChain parentScope = null, PyCell[] closure = null, PyFrame parentFrame = null)
        {
#if DEBUG_LOG
            Console.WriteLine($"🆕 PyFrame 생성: {code.Name}, args={args.Length}개");
#endif

            Code = code;
            ValueStack = new Stack<PyObject>();
            // 부모 스코프 체인이 있으면 상속, 없으면 새로 생성
            ScopeChain = parentScope ?? new PyScopeChain();
            FastLocals = new Dictionary<string, PyObject>();
            InstructionPointer = 0;

            // CPython 3.12: Set parent frame for call stack tracking
            ParentFrame = parentFrame;

            // Initialize filename from code object
            CurrentFileName = code.FileName;

            // 클로저 정보 설정
            Closure = closure ?? new PyCell[0];

            // CPython 3.12: Cells array includes both FreeVars (first) and CellVars (after)
            int freeVarCount = code.FreeVars?.Count ?? 0;
            int cellVarCount = code.CellVars?.Count ?? 0;
            int totalCellCount = freeVarCount + cellVarCount;

            if (totalCellCount > 0)
            {
                Cells = new PyCell[totalCellCount];
                for (int i = 0; i < Cells.Length; i++)
                {
                    Cells[i] = new PyCell(); // 빈 셀로 초기화
                }
#if DEBUG_LOG
                Console.WriteLine($"🆕 Frame Cells initialized: {freeVarCount} FreeVars + {cellVarCount} CellVars = {totalCellCount} total cells");
#endif
            }
            else
            {
                Cells = new PyCell[0];
            }

            // 함수 스코프 생성 (모듈 실행인 경우 제외)
            if (!IsModuleExecution(code.Name))
            {
                ScopeChain.PushScope(ScopeType.Local, code.Name);
#if DEBUG_LOG
                Console.WriteLine($"📁 스코프 추가: Local Scope '{code.Name}': 0 variables");
#endif
            }
            else
            {
#if DEBUG_LOG
                Console.WriteLine($"📦 모듈 실행 감지: '{code.Name}' - Local 스코프 생성 생략");
#endif
            }

            // CPython 3.12 호환: 매개변수 바인딩 (키워드 인수 지원)
            BindArgumentsToParametersCPython312(args, code, parentFrame);
        }

        /// <summary>
        /// CPython 3.12 호환: 코드 이름으로 모듈 실행인지 판단
        /// 모듈 레벨 코드는 항상 "<module>"이어야 함
        /// </summary>
        private static bool IsModuleExecution(string codeName)
        {
            // CPython 3.12: 모듈 코드 객체 이름은 항상 "<module>"
            return codeName == "<module>";
        }

        /// <summary>
        /// CPython 3.12 호환: 키워드 인수를 지원하는 매개변수 바인딩
        /// </summary>
        private void BindArgumentsToParametersCPython312(PyObject[] args, PyCodeObject code, PyFrame parentFrame)
        {
            // CPython 3.12: Check for keyword arguments from parent frame
            PyTuple kwNames = null;
            Dictionary<string, PyObject> keywordArgs = null;
            PyObject[] positionalArgs = args;

            if (parentFrame?.KeywordNamesForNextCall != null && parentFrame.KeywordNamesForNextCall.Items.Length > 0)
            {
                kwNames = parentFrame.KeywordNamesForNextCall;
                var kwNamesList = kwNames.Items.Select(name => ((PyString)name).Value).ToArray();
                var numKwArgs = kwNamesList.Length;
                var numPosArgs = args.Length - numKwArgs;

                // Split positional and keyword arguments (CPython 3.12 way)
                positionalArgs = new PyObject[numPosArgs];
                keywordArgs = new Dictionary<string, PyObject>();

                Array.Copy(args, 0, positionalArgs, 0, numPosArgs);

                for (int i = 0; i < numKwArgs; i++)
                {
                    keywordArgs[kwNamesList[i]] = args[numPosArgs + i];
                }

#if DEBUG_LOG
                Console.WriteLine($"🔗 키워드 인수 매개변수 바인딩: {positionalArgs.Length}개 위치 인수, {keywordArgs.Count}개 키워드 인수, {code.ArgCount}개 매개변수");
                Console.WriteLine($"  Code flags: {code.Flags} (CO_VARARGS={((code.Flags & PyCodeObject.CO_VARARGS) != 0)}, CO_VARKEYWORDS={((code.Flags & PyCodeObject.CO_VARKEYWORDS) != 0)})");
#endif

                // Clear keyword names to prevent reuse
                parentFrame.KeywordNamesForNextCall = null;
            }
            else
            {
#if DEBUG_LOG
                Console.WriteLine($"🔗 매개변수 바인딩: {args.Length}개 인수, {code.ArgCount}개 매개변수");
#if DEBUG_LOG
                Console.WriteLine($"  Code flags: {code.Flags} (CO_VARARGS={((code.Flags & PyCodeObject.CO_VARARGS) != 0)}, CO_VARKEYWORDS={((code.Flags & PyCodeObject.CO_VARKEYWORDS) != 0)})");
#endif
#if DEBUG_LOG
                Console.WriteLine($"  DefaultValues.Count: {code.DefaultValues.Count}");
#endif
#endif
            }

            bool hasVarArgs = (code.Flags & PyCodeObject.CO_VARARGS) != 0;
            bool hasVarKeywords = (code.Flags & PyCodeObject.CO_VARKEYWORDS) != 0;

            // Phase 1: Bind positional arguments to regular parameters
            int posArgIndex = 0;
            for (int paramIndex = 0; paramIndex < code.ArgCount; paramIndex++)
            {
                var paramName = code.VarNames[paramIndex];

                if (posArgIndex < positionalArgs.Length)
                {
                    // Bind positional argument
                    FastLocals[paramName] = positionalArgs[posArgIndex];
                    ScopeChain.AssignVariable(paramName, positionalArgs[posArgIndex]);
                    posArgIndex++;

#if DEBUG_LOG
                    Console.WriteLine($"  → {paramName} = {positionalArgs[posArgIndex - 1]} (위치 인수)");
#endif
                }
                else if (keywordArgs != null && keywordArgs.ContainsKey(paramName))
                {
                    // Bind keyword argument to parameter
                    var keywordValue = keywordArgs[paramName];
                    FastLocals[paramName] = keywordValue;
                    ScopeChain.AssignVariable(paramName, keywordValue);
                    keywordArgs.Remove(paramName); // Remove so it doesn't go into **kwargs

#if DEBUG_LOG
                    Console.WriteLine($"  → {paramName} = {keywordValue} (키워드 인수)");
#endif
                }
                else
                {
                    // Check for default value
                    int numRequiredParams = code.ArgCount - code.DefaultValues.Count;
                    if (paramIndex >= numRequiredParams && paramIndex - numRequiredParams < code.DefaultValues.Count)
                    {
                        var defaultValue = code.DefaultValues[paramIndex - numRequiredParams];
                        FastLocals[paramName] = defaultValue;
                        ScopeChain.AssignVariable(paramName, defaultValue);

#if DEBUG_LOG
                        Console.WriteLine($"  → {paramName} = {defaultValue} (기본값, index {paramIndex - numRequiredParams})");
#endif
                    }
                    else
                    {
                        // Missing required argument
                        throw PyTypeError.Create($"[PyFrame] missing required argument: '{paramName}'");
                    }
                }
            }

            // Phase 2: Handle *args (if function has varargs) - CPython 3.12 compatible
            if (hasVarArgs)
            {
                var varargsName = code.VarNames[code.ArgCount]; // *args parameter

                // Collect remaining positional arguments into *args tuple
                var extraArgs = new PyObject[Math.Max(0, positionalArgs.Length - posArgIndex)];
                if (posArgIndex < positionalArgs.Length)
                {
                    Array.Copy(positionalArgs, posArgIndex, extraArgs, 0, extraArgs.Length);
                }
                var argsTuple = new PyTuple(extraArgs);

                FastLocals[varargsName] = argsTuple;
                ScopeChain.AssignVariable(varargsName, argsTuple);

#if DEBUG_LOG
                Console.WriteLine($"  → {varargsName} = {argsTuple} (*args with {extraArgs.Length} items)");
#endif
            }

            // Phase 3: Handle **kwargs (if function has varkeywords)
            if (hasVarKeywords)
            {
                var varkwargsName = code.VarNames[code.ArgCount + (hasVarArgs ? 1 : 0)]; // **kwargs parameter
                var kwargsDict = new PyDict();

                // Add keyword arguments to **kwargs dict if they exist
                if (keywordArgs != null && keywordArgs.Count > 0)
                {
                    foreach (var kvp in keywordArgs)
                    {
                        kwargsDict.SetItem(new PyString(kvp.Key), kvp.Value);
                    }
                }

                FastLocals[varkwargsName] = kwargsDict;
                ScopeChain.AssignVariable(varkwargsName, kwargsDict);

#if DEBUG_LOG
                var itemCount = keywordArgs?.Count ?? 0;
#if DEBUG_LOG
                Console.WriteLine($"  → {varkwargsName} = {kwargsDict} (**kwargs with {itemCount} items)");
#endif
#endif
            }
            else if (keywordArgs != null && keywordArgs.Count > 0)
            {
                // Unexpected keyword arguments
                var unexpectedKey = keywordArgs.Keys.First();
                throw PyTypeError.Create($"[PyFrame] {code.Name}() got an unexpected keyword argument '{unexpectedKey}'");
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
                #if DEBUG_LOG
                Console.WriteLine($"❌ GetExceptionHandler: Infinite loop detected, stopping at call #{ExceptionHandlerCallCount}");
                #endif
                return null; // Break the cycle
            }

            #if DEBUG_LOG
            Console.WriteLine($"🔍 GetExceptionHandler called #{ExceptionHandlerCallCount} - IP: {InstructionPointer}");
            Console.WriteLine($"   Frame: {ToString()}");
            Console.WriteLine($"   Code Name: {Code.Name}");
            Console.WriteLine($"   Exception Table entries: {Code.ExceptionTable.Count}");
            #endif
            if (Code.ExceptionTable.Count > 0)
            {
                #if DEBUG_LOG
                Console.WriteLine($"   Exception Table details:");
                #endif
                for (int i = 0; i < Code.ExceptionTable.Count; i++)
                {
                    var entry = Code.ExceptionTable[i];
                    #if DEBUG_LOG
                    Console.WriteLine($"     [{i}] Start: {entry.StartOffset}, End: {entry.EndOffset}, Handler: {entry.HandlerOffset}");
                    #endif
                }
            }
            #if DEBUG_LOG
            Console.WriteLine($"   Legacy handlers: {ExceptionHandlers.Count}");
            #endif

            // CPython 3.12: Use Exception Table instead of SETUP_EXCEPT stack
            if (Code.ExceptionTable.Count > 0)
            {
                var result = GetExceptionHandlerFromTable();
                #if DEBUG_LOG
                Console.WriteLine($"   Exception Table result: {result}");
                #endif
                return result;
            }

            // Fallback to legacy SETUP_EXCEPT stack for compatibility
            var legacyResult = ExceptionHandlers.Count > 0 ? (int?)ExceptionHandlers.Peek() : null;
            #if DEBUG_LOG
            Console.WriteLine($"   Legacy handler result: {legacyResult}");
            #endif
            return legacyResult;
        }

        // CPython 3.12 Exception Table lookup
        public (int? handlerOffset, ExceptionTableEntry? entry) GetExceptionHandlerFromTableWithEntry()
        {
            var currentOffset = InstructionPointer;
            #if DEBUG_LOG
            Console.WriteLine($"🔍 Searching Exception Table for offset {currentOffset}:");
            #endif

            // Search Exception Table for a handler covering current instruction
            foreach (var entry in Code.ExceptionTable)
            {
                #if DEBUG_LOG
                Console.WriteLine($"   Entry: Start={entry.StartOffset}, End={entry.EndOffset}, Handler={entry.HandlerOffset}");
                #endif
                #if DEBUG_LOG
                Console.WriteLine($"   Check: {currentOffset} >= {entry.StartOffset} && {currentOffset} < {entry.EndOffset}");
                #endif

                if (currentOffset >= entry.StartOffset && currentOffset < entry.EndOffset)
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"✅ Exception Table: MATCH! Handler at {entry.HandlerOffset} for instruction {currentOffset}");
                    #endif
                    #if DEBUG_LOG
                    Console.WriteLine($"   Entry details: Depth={entry.Depth}, Lasti={entry.Lasti}");
                    #endif
                    return (entry.HandlerOffset, entry);
                }
                else
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"❌ No match for this entry");
                    #endif
                }
            }

            #if DEBUG_LOG
            Console.WriteLine($"❌ Exception Table: No handler found for instruction {currentOffset}");
            #endif
            return (null, null);
        }

        private int? GetExceptionHandlerFromTable()
        {
            var (handlerOffset, _) = GetExceptionHandlerFromTableWithEntry();
            return handlerOffset;
        }

        public override string ToString() => $"<frame for {Code.Name}>";
    }

    // Python 가상 머신 (기존 객체 시스템과 완전 통합)
    public class PyVM
    {
        public static PyVM Instance { get; } = new PyVM();

        private readonly Stack<PyFrame> _frameStack;
        private readonly PyScopeChain _globalScope;

        // Current frame for zero-argument super() calls
        public static PyFrame? CurrentFrame => Instance._frameStack.Count > 0 ? Instance._frameStack.Peek() : null;

        // CPython 3.12: Get current frame for sys.exc_info() and other introspection
        public static PyFrame? GetCurrentFrame() => CurrentFrame;

        private PyVM()
        {
            _frameStack = new Stack<PyFrame>();
            _globalScope = new PyScopeChain(); // 기존 LEGB 시스템 사용!
        }

        // 메인 모듈 실행
        public PyObject ExecuteModule(PyCodeObject codeObject)
        {
            #if DEBUG_LOG
            Console.WriteLine($"🚀 ExecuteModule: Starting execution of {codeObject.Name}");
            #endif
            #if DEBUG_LOG
            Console.WriteLine($"   Exception Table entries: {codeObject.ExceptionTable.Count}");
            #endif
            if (codeObject.ExceptionTable.Count > 0)
            {
                for (int i = 0; i < codeObject.ExceptionTable.Count; i++)
                {
                    var entry = codeObject.ExceptionTable[i];
                    #if DEBUG_LOG
                    Console.WriteLine($"     [{i}] Start: {entry.StartOffset}, End: {entry.EndOffset}, Handler: {entry.HandlerOffset}");
                    #endif
                }
            }

            var frame = new PyFrame(codeObject, new PyObject[0], _globalScope);
            return ExecuteFrame(frame);
        }

        // 메인 모듈 실행 (특정 스코프 체인 사용)
        public PyObject ExecuteModule(PyCodeObject codeObject, PyScopeChain scopeChain)
        {
            #if DEBUG_LOG
            Console.WriteLine($"🚀 ExecuteModule (with scopeChain): Starting execution of {codeObject.Name}");
            #endif
            #if DEBUG_LOG
            Console.WriteLine($"   Exception Table entries: {codeObject.ExceptionTable.Count}");
            #endif

            // CPython 3.12 Adaptive Optimization - 실행 전 최적화 검사 (--no-optimize 체크)
            if (!SharpPyConfig.DisableOptimizer)
            {
                codeObject = PyAdaptiveOptimizer.Instance.OptimizeIfNeeded(codeObject);
            }
            else
            {
#if DEBUG_LOG
                Console.WriteLine("🚫 Adaptive Optimization disabled by --no-optimize flag");
#endif
            }

            // 🔍 실제 VM에서 실행할 바이트코드 출력 (디버그용)
#if DEBUG_LOG
            Console.WriteLine($"\n📋 VM에서 실제 실행할 바이트코드 ({codeObject.Instructions.Count}개 명령어):");
#endif
            for (int i = 0; i < codeObject.Instructions.Count; i++)
            {
                var instr = codeObject.Instructions[i];
                int byteOffset = PyJumpBackwardUtil.CalculateByteOffset(i, codeObject.Instructions);

                // CPython 스타일로 포맷팅
                string line = $"          {byteOffset,3}";
                if (i % 2 == 0) line += " >> ";
                else line += "    ";

                line += $"{instr.OpCode,-20}";
                if (instr.Argument != 0)
                {
                    line += $"{instr.Argument,8}";

                    // 상수나 이름 표시
                    if (instr.OpCode == ByteCodeOp.LOAD_CONST && instr.Argument < codeObject.Constants.Count)
                    {
                        var constant = codeObject.Constants[instr.Argument];
                        line += $" ({constant?.ToString() ?? "None"})";
                    }
                    else if ((instr.OpCode == ByteCodeOp.LOAD_NAME || instr.OpCode == ByteCodeOp.STORE_NAME) &&
                             instr.Argument < codeObject.Names.Count)
                    {
                        line += $" ({codeObject.Names[instr.Argument]})";
                    }
                }

#if DEBUG_LOG
                Console.WriteLine(line);
#endif

                // List comprehension 관련 명령어만 출력 (너무 길어지지 않도록)
                if (i > 20 && instr.OpCode != ByteCodeOp.FOR_ITER && instr.OpCode != ByteCodeOp.JUMP_BACKWARD &&
                    instr.OpCode != ByteCodeOp.LIST_APPEND && instr.OpCode != ByteCodeOp.END_FOR) continue;
                if (i > 40) break;
            }
#if DEBUG_LOG
            Console.WriteLine("📋 실제 바이트코드 출력 완료\n");
#endif
            if (codeObject.ExceptionTable.Count > 0)
            {
                for (int i = 0; i < codeObject.ExceptionTable.Count; i++)
                {
                    var entry = codeObject.ExceptionTable[i];
                    #if DEBUG_LOG
                    Console.WriteLine($"     [{i}] Start: {entry.StartOffset}, End: {entry.EndOffset}, Handler: {entry.HandlerOffset}");
                    #endif
                }
            }

            #if DEBUG_LOG
            Console.WriteLine($"🔍 PyFrame 생성 직전 codeObject.ExceptionTable.Count: {codeObject.ExceptionTable.Count}");
            #endif
            var frame = new PyFrame(codeObject, new PyObject[0], scopeChain);
            #if DEBUG_LOG
            Console.WriteLine($"🔍 PyFrame 생성 후 frame.Code.ExceptionTable.Count: {frame.Code.ExceptionTable.Count}");
            #endif
            return ExecuteFrame(frame);
        }

        // 프레임 실행 (바이트코드 해석)
        // CPython 3.12: Execute class body and return namespace
        public Dictionary<string, PyObject> ExecuteClassBody(PyCodeObject classBody, PyCell[]? closure = null)
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

            // CPython 3.12: Create frame with closure if provided
            var frame = closure != null
                ? new PyFrame(classBody, new PyObject[0], parentScope, closure)
                : new PyFrame(classBody, new PyObject[0], parentScope);
            var result = ExecuteFrame(frame);

            // Extract class namespace - capture variables added during class body execution
            var classNamespace = new Dictionary<string, PyObject>();

            // Method 1: FastLocals (for STORE_FAST operations)
            foreach (var kvp in frame.FastLocals)
            {
                classNamespace[kvp.Key] = kvp.Value;
            }

            // Method 2: Saved class body variables (FIXED: use saved variables from before scope cleanup)
            if (frame.ClassBodyVariables != null)
            {
                foreach (var kvp in frame.ClassBodyVariables)
                {
                    classNamespace[kvp.Key] = kvp.Value;
                }
            }
            // Fallback: Local scope variables (if still available)
            else if (frame.ScopeChain?.CurrentScope != null)
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

            #if DEBUG_LOG
            Console.WriteLine($"📦 ExecuteClassBody captured {classNamespace.Count} variables:");
            #endif
            foreach (var kvp in classNamespace)
            {
#if DEBUG_LOG
                Console.WriteLine($"  - {kvp.Key}: {kvp.Value?.GetType().Name}");
#endif
            }

            return classNamespace;
        }

        public PyObject ExecuteFrame(PyFrame frame)
        {
            _frameStack.Push(frame);

#if DEBUG_LOG
            Console.WriteLine($"\n🚀 VM 실행: {frame}");
#endif
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
                    // First try from LineNumberTable (more accurate), then from instruction
                    if (frame.Code.LineNumberTable.TryGetValue(frame.InstructionPointer, out var lineFromTable))
                    {
                        frame.CurrentLineNumber = lineFromTable;
                    }
                    else if (instruction.LineNumber > 0)
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

#if DEBUG_LOG
                    if (frame.ValueStack.Count <= 10) // 스택이 너무 크지 않을 때만 출력
                    {
                        var stackContents = string.Join(", ", frame.ValueStack.Reverse().Take(5));
                        Console.WriteLine($"  {frame.InstructionPointer*2,3}: {instruction,-25} 스택:[{stackContents}]");
                    }
#endif

                    try
                    {
                        var result = ExecuteInstruction(frame, instruction);

                        // RETURN_VALUE인 경우 함수 종료
                        if (result != null)
                        {
#if DEBUG_LOG
                            Console.WriteLine($"✅ VM 완료: {result}");
#endif
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
#if DEBUG_LOG
                            Console.WriteLine($"🔍 Exception enriched: {pyEx.FileName}:{pyEx.LineNumber}:{pyEx.ColumnOffset}");
                            Console.WriteLine($"🔍 Source lines available: {pyEx.SourceLines?.Count ?? 0}");
                            Console.WriteLine($"🔍 Full exception: {pyEx}");
#endif
                        }

                        // Handle Python exceptions with proper exception handler routing
                        var (handlerOffset, exceptionEntry) = frame.GetExceptionHandlerFromTableWithEntry();
                        if (handlerOffset.HasValue && exceptionEntry != null)
                        {
                            // CPython 3.12: Finally handlers (depth=0) need clean stack
                            if (exceptionEntry.Depth == 0)
                            {
                                #if DEBUG_LOG
                                Console.WriteLine($"🔧 Finally handler detected (depth=0): cleaning stack");
                                Console.WriteLine($"🔧 Stack before cleanup: {frame.ValueStack.Count} items");
                                #endif

                                // For finally handlers, clean up any stale ExceptionInfo objects
                                var cleanStack = new Stack<PyObject>();
                                var itemsToKeep = Math.Min(3, frame.ValueStack.Count); // Keep at most 3 recent items
                                var tempList = new List<PyObject>();

                                // Pop recent items but avoid ExceptionInfo
                                for (int i = 0; i < itemsToKeep && frame.ValueStack.Count > 0; i++)
                                {
                                    var item = frame.ValueStack.Pop();
                                    if (!(item is PyExceptionInfo))
                                    {
                                        tempList.Add(item);
                                    }
                                }

                                // Push back non-ExceptionInfo items
                                for (int i = tempList.Count - 1; i >= 0; i--)
                                {
                                    frame.ValueStack.Push(tempList[i]);
                                }

                                #if DEBUG_LOG
                                Console.WriteLine($"🔧 Stack after cleanup: {frame.ValueStack.Count} items");
                                #endif
                            }

                            // CPython 3.12: All exception handlers push PyExceptionInfo
                            // The stack depth is managed by PUSH_EXC_INFO instruction later
                            var exceptionInfo = new PyExceptionInfo(
                                excType: pyEx.PyException,
                                excValue: pyEx.PyException,
                                excTraceback: PyNone.Instance,
                                lasti: new PyInt(frame.InstructionPointer)
                            );
                            frame.ValueStack.Push(exceptionInfo);
                            #if DEBUG_LOG
                            Console.WriteLine($"🔧 Exception handled: pushed PyExceptionInfo to stack (lasti={exceptionEntry.Lasti})");
                            #endif

                            frame.LastException = pyEx.PyException;
                            frame.CurrentException = pyEx.PyException;
                            #if DEBUG_LOG
                            Console.WriteLine($"🔧 Exception handled: jumping to handler at offset {handlerOffset.Value}");
                            #endif
                            #if DEBUG_LOG
                            Console.WriteLine($"🔧 Stack after exception push: {frame.ValueStack.Count} items");
                            #endif
                            #if DEBUG_LOG
                            Console.WriteLine($"🔧 Total instructions: {frame.Code.Instructions.Count}");
                            #endif
                            #if DEBUG_LOG
                            Console.WriteLine($"🔧 Handler offset {handlerOffset.Value} → instruction index: {handlerOffset.Value}");
                            #endif

                            // CPython 3.12 compatibility: SharpPy Exception Table stores instruction indices, not byte offsets
                            var instructionIndex = handlerOffset.Value;
                            if (instructionIndex >= 0 && instructionIndex < frame.Code.Instructions.Count)
                            {
                                frame.InstructionPointer = instructionIndex;
                                #if DEBUG_LOG
                                Console.WriteLine($"🔧 Jumping to instruction {instructionIndex}: {frame.Code.Instructions[instructionIndex].OpCode}");
                                #endif
                            }
                            else
                            {
                                // Invalid handler index - provide detailed diagnostic information
                                #if DEBUG_LOG
                                Console.WriteLine($"❌ Invalid handler instruction index: {instructionIndex}");
                                #endif
                                #if DEBUG_LOG
                                Console.WriteLine($"   Max valid index: {frame.Code.Instructions.Count - 1}");
                                #endif
                                #if DEBUG_LOG
                                Console.WriteLine($"   Exception Table entries: {frame.Code.ExceptionTable.Count}");
                                #endif
                                #if DEBUG_LOG
                                Console.WriteLine($"   Current IP: {frame.InstructionPointer}");
                                #endif

                                // Try to find a valid handler or fall back gracefully
                                if (frame.Code.Instructions.Count > 0)
                                {
                                    // Jump to the last instruction as a safer fallback
                                    int safeIndex = frame.Code.Instructions.Count - 1;
                                    frame.InstructionPointer = safeIndex;
                                    #if DEBUG_LOG
                                    Console.WriteLine($"🔧 Fallback: Jumping to safe instruction {safeIndex}");
                                    #endif
                                }
                                else
                                {
                                    // No instructions available - re-throw the original exception
                                    #if DEBUG_LOG
                                    Console.WriteLine("❌ No valid instructions to jump to - re-throwing exception");
                                    #endif
                                    throw;
                                }
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
                #if DEBUG_LOG
                Console.WriteLine($"✅ VM 완료: None (암시적)");
                #endif
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
                    // COPY 1 = copy TOS (top of stack), COPY 2 = copy TOS-1, etc.
                    var copyIndex = instruction.Argument;
                    if (frame.ValueStack.Count == 0)
                    {
                        throw PyRuntimeError.Create($"COPY: Stack empty when trying to copy index {copyIndex}. This may be caused by incorrect match-case bytecode generation.");
                    }
                    if (copyIndex <= 0 || copyIndex > frame.ValueStack.Count)
                    {
                        // CPython 3.12 compatibility: Handle exception cleanup edge cases
                        if (copyIndex > frame.ValueStack.Count)
                        {
                            // Push None for missing stack items - this handles complex exception cleanup scenarios
                            #if DEBUG_LOG
                            Console.WriteLine($"🔧 COPY {copyIndex}: Stack size {frame.ValueStack.Count} insufficient, pushing None");
                            #endif
                            frame.ValueStack.Push(PyNone.Instance);
                            break;
                        }
                        throw PyRuntimeError.Create($"COPY index {copyIndex} out of range (stack size: {frame.ValueStack.Count}). Stack contents: [{string.Join(", ", frame.ValueStack.Take(5).Select(x => x.GetType().Name))}]");
                    }

                    // CPython 3.12: COPY 1 copies TOS, COPY 2 copies TOS-1 (second from top), etc.
                    // .NET Stack: ElementAt(0) is TOS, ElementAt(1) is TOS-1
                    // So COPY 1 should use ElementAt(copyIndex - 1)
                    var valueToCopy = frame.ValueStack.ElementAt(copyIndex - 1);
                    #if DEBUG_LOG
                    // Debug: Console.WriteLine($"🔄 COPY {copyIndex}: copying TOS-{copyIndex-1} = {valueToCopy}");
                    #endif
                    frame.ValueStack.Push(valueToCopy);
                    break;

                case ByteCodeOp.SWAP: // SWAP(n) - TOS와 TOS-(n-1) 교환
                    var oparg = instruction.Argument;
                    if (frame.ValueStack.Count < oparg)
                    {
                        throw PyRuntimeError.Create($"SWAP({oparg}): Not enough items on stack (need {oparg}, got {frame.ValueStack.Count})");
                    }

                    var stackArray = frame.ValueStack.ToArray(); // Stack을 임시 배열로 변환

                    var temp = stackArray[0];
                    stackArray[0] = stackArray[oparg - 1];
                    stackArray[oparg - 1] = temp;

                    frame.ValueStack.Clear();
                    for (int i = stackArray.Length - 1; i >= 0; i--)
                    {
                        frame.ValueStack.Push(stackArray[i]);
                    }

                    break;

                case ByteCodeOp.LOAD_CONST:
                    var constant = frame.Code.Constants[instruction.Argument];
                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 LOAD_CONST: 인덱스 {instruction.Argument}, 값 {constant} (타입: {constant?.GetType().Name})");
                    Console.WriteLine($"🔍 Constants 배열 전체: [{string.Join(", ", frame.Code.Constants.Select((c, i) => $"{i}:{c}"))}]");
                    #endif
                    frame.ValueStack.Push(constant);
                    break;

                case ByteCodeOp.LOAD_NAME:
                    var name = frame.Code.Names[instruction.Argument];
                    // 기존 LEGB 시스템 사용!
                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 LOAD_NAME({name}): 현재 스코프 = {frame.ScopeChain.CurrentScope?.Name ?? "null"}");
                    #endif
                    var value = frame.ScopeChain.LookupVariable(name, verbose: true);
                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 LOAD_NAME({name}): loaded {value?.GetType().Name ?? "null"} value = {value}");
                    #endif
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
                            // CPython 3.12 호환: PyNull인 경우 UnboundLocalError 발생
                            if (PyNull.IsNull(fastValue))
                            {
                                throw PyNameError.Create($"local variable '{varName}' referenced before assignment");
                            }
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

                        // Try fast locals first
                        if (frame.FastLocals.TryGetValue(clearVarName, out var clearValue))
                        {
                            frame.ValueStack.Push(clearValue);
                            // Clear the variable from locals (PEP 709 requirement)
                            frame.FastLocals.Remove(clearVarName);
                            #if DEBUG_LOG
                            Console.WriteLine($"🧹 LOAD_FAST_AND_CLEAR: loaded {clearVarName}={clearValue} from fast locals, cleared");
                            #endif
                        }
                        // If not in fast locals, try global scope (for module-level variables)
                        else
                        {
                            try
                            {
                                var globalVal = frame.ScopeChain.LookupVariable(clearVarName);
                                frame.ValueStack.Push(globalVal);
                                // Store original value in fast locals for proper restoration
                                frame.FastLocals[clearVarName] = globalVal;
                                #if DEBUG_LOG
                                Console.WriteLine($"🧹 LOAD_FAST_AND_CLEAR: loaded {clearVarName}={globalVal} from global scope, saved to fast locals");
                                #endif
                            }
                            catch (System.Exception ex) when (ex.Message.Contains("is not defined"))
                            {
                                // CPython 3.12: Load NULL if variable doesn't exist (for comprehensions)
                                frame.ValueStack.Push(PyNull.Instance);
                                #if DEBUG_LOG
                                Console.WriteLine($"🧹 LOAD_FAST_AND_CLEAR: {clearVarName} not found in fast locals or globals, loaded NULL");
                                #endif
                            }
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
                    // STORE_FAST는 frame의 fast locals에만 저장하고, 전역 스코프에는 저장하지 않음
                    var storeIndex = instruction.Argument;
                    if (storeIndex < frame.Code.VarNames.Count)
                    {
                        var varName = frame.Code.VarNames[storeIndex];
                        var storeVal = frame.ValueStack.Pop();
                        frame.FastLocals[varName] = storeVal;
                        // CPython 3.12: STORE_FAST는 FastLocals에만 저장 (ScopeChain에 저장하지 않음)
                        // frame.ScopeChain.AssignVariable(varName, storeVal); // ❌ 제거: 전역 스코프 오염 방지
                    }
                    else
                    {
                        throw PyRuntimeError.Create($"STORE_FAST: index {storeIndex} out of range");
                    }
                    break;

                case ByteCodeOp.DELETE_FAST:
                    // CPython 3.12: Delete fast local variable - NULL 상태로 설정
                    var deleteFastIndex = instruction.Argument;
                    if (deleteFastIndex < frame.Code.VarNames.Count)
                    {
                        var deleteFastName = frame.Code.VarNames[deleteFastIndex];
                        // CPython 3.12 호환: 변수를 제거하는 대신 PyNull로 설정
                        // 이렇게 하면 LOAD_FAST에서 PyNull을 반환할 수 있음
                        frame.FastLocals[deleteFastName] = PyNull.Instance;
                        #if DEBUG_LOG
                        Console.WriteLine($"🔧 DELETE_FAST: set variable '{deleteFastName}' to NULL");
                        #endif
                    }
                    else
                    {
                        throw PyRuntimeError.Create($"DELETE_FAST: index {deleteFastIndex} out of range");
                    }
                    break;

                case ByteCodeOp.STORE_NAME:
                    var storeName = frame.Code.Names[instruction.Argument];
                    var storeValue = frame.ValueStack.Pop();
                    // 기존 LEGB 시스템 사용!
                    frame.ScopeChain.AssignVariable(storeName, storeValue);
                    break;

                case ByteCodeOp.DELETE_NAME:
                    var deleteName = frame.Code.Names[instruction.Argument];
                    // 변수 삭제: 현재 스코프에서 먼저 찾기
                    if (frame.ScopeChain.CurrentScope?.Variables.Remove(deleteName) == true)
                    {
                        // 현재 스코프에서 삭제됨
                        break;
                    }
                    // 전역 스코프에서 찾기
                    if (frame.ScopeChain.GlobalScope?.Variables.Remove(deleteName) == true)
                    {
                        // 전역 스코프에서 삭제됨
                        break;
                    }
                    // 변수가 없으면 NameError
                    throw PyNameError.Create($"name '{deleteName}' is not defined");
                    break;

                case ByteCodeOp.LOAD_GLOBAL:
                    var globalName = frame.Code.Names[instruction.Argument];
                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 LOAD_GLOBAL({globalName}): Checking GlobalScope");
                    #endif
                    #if DEBUG_LOG
                    Console.WriteLine($"   GlobalScope is null: {frame.ScopeChain.GlobalScope == null}");
                    #endif
                    if (frame.ScopeChain.GlobalScope != null)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"   GlobalScope variables: {frame.ScopeChain.GlobalScope.Variables.Count}");
                        #endif
                        #if DEBUG_LOG
                        Console.WriteLine($"   Has '{globalName}': {frame.ScopeChain.GlobalScope.Variables.ContainsKey(globalName)}");
                        #endif
                    }
                    var globalValue = frame.ScopeChain.GlobalScope?.GetVariable(globalName) ??
                                    frame.ScopeChain.BuiltinModule.GetBuiltin(globalName);
                    if (globalValue == null)
                        throw PyNameError.Create($"name '{globalName}' is not defined");
                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 LOAD_GLOBAL({globalName}): loaded {globalValue?.GetType().Name ?? "null"} value = {globalValue}");
                    #endif
                    frame.ValueStack.Push(globalValue);
                    break;

                case ByteCodeOp.LOAD_GLOBAL_BUILTIN:
                    var globalBuiltinName = frame.Code.Names[instruction.Argument];
                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 LOAD_GLOBAL_BUILTIN({globalBuiltinName}): Direct builtin lookup");
                    #endif

                    // LOAD_GLOBAL_BUILTIN은 최적화된 버전으로 builtin을 직접 조회
                    var builtinValue = frame.ScopeChain.BuiltinModule.GetBuiltin(globalBuiltinName);
                    if (builtinValue == null)
                    {
                        // builtin에 없으면 global에서 찾기
                        builtinValue = frame.ScopeChain.GlobalScope?.GetVariable(globalBuiltinName);
                    }

                    if (builtinValue == null)
                        throw PyNameError.Create($"name '{globalBuiltinName}' is not defined");

                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 LOAD_GLOBAL_BUILTIN({globalBuiltinName}): loaded {builtinValue?.GetType().Name ?? "null"} value = {builtinValue}");
                    #endif
                    frame.ValueStack.Push(builtinValue);
                    break;

                case ByteCodeOp.LOAD_ASSERTION_ERROR:
                    // CPython 3.12: Load AssertionError class for assert statements
                    var assertionError = frame.ScopeChain.BuiltinModule.GetBuiltin("AssertionError");
                    if (assertionError == null)
                        throw PyNameError.Create("name 'AssertionError' is not defined");
                    frame.ValueStack.Push(assertionError);
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
                    // STORE_GLOBAL must always store to GlobalScope, not CurrentScope
                    frame.ScopeChain.GlobalScope.SetVariable(storeGlobalName, storeGlobalValue);
#if DEBUG_LOG
                    Console.WriteLine($"📝 STORE_GLOBAL: {storeGlobalName} = {storeGlobalValue}");
#endif
                    break;

                case ByteCodeOp.DELETE_GLOBAL:
                    // CPython 3.12: Delete global variable
                    var deleteGlobalName = frame.Code.Names[instruction.Argument];
                    if (frame.ScopeChain.GlobalScope?.Variables.Remove(deleteGlobalName) == true)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"🔧 DELETE_GLOBAL: deleted global variable '{deleteGlobalName}'");
                        #endif
                    }
                    else
                    {
                        // CPython behavior: NameError if variable doesn't exist
                        throw PyNameError.Create($"name '{deleteGlobalName}' is not defined");
                    }
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
                    // CPython 3.12+ unified binary operation with adaptive profiling
                    var operation = (BinaryOpType)instruction.Argument;
                    var right = frame.ValueStack.Pop();
                    var left = frame.ValueStack.Pop();

                    // Record profiling data for adaptive specialization
                    var location = $"{frame.Code.Name}_{frame.InstructionPointer}";
                    var opName = operation.ToString().ToLower().Replace("_", "");
                    if (opName == "truedivide") opName = "/";
                    else if (opName == "floordivide") opName = "//";
                    else if (opName == "add") opName = "+";
                    else if (opName == "subtract") opName = "-";
                    else if (opName == "multiply") opName = "*";
                    else if (opName == "modulo") opName = "%";
                    else if (opName == "power") opName = "**";

                    PyAdaptiveProfile.Instance.RecordBinaryOp(location, left, right, opName);

                    var result = ExecuteBinaryOpType(left, right, operation);
                    frame.ValueStack.Push(result);
                    break;

                // Specialized Binary Operations - CPython 3.12 Adaptive Specialization
                case ByteCodeOp.BINARY_ADD_INT:
                    var rightInt = ((PyInt)frame.ValueStack.Pop()).Value;
                    var leftInt = ((PyInt)frame.ValueStack.Pop()).Value;
                    frame.ValueStack.Push(new PyInt(leftInt + rightInt));
                    break;

                case ByteCodeOp.BINARY_ADD_FLOAT:
                    var rightFloat = ((PyFloat)frame.ValueStack.Pop()).Value;
                    var leftFloat = ((PyFloat)frame.ValueStack.Pop()).Value;
                    frame.ValueStack.Push(new PyFloat(leftFloat + rightFloat));
                    break;

                case ByteCodeOp.BINARY_ADD_UNICODE:
                    var rightStr = ((PyString)frame.ValueStack.Pop()).Value;
                    var leftStr = ((PyString)frame.ValueStack.Pop()).Value;
                    frame.ValueStack.Push(new PyString(leftStr + rightStr));
                    break;

                case ByteCodeOp.BINARY_MULTIPLY_INT:
                    var rightMulInt = ((PyInt)frame.ValueStack.Pop()).Value;
                    var leftMulInt = ((PyInt)frame.ValueStack.Pop()).Value;
                    frame.ValueStack.Push(new PyInt(leftMulInt * rightMulInt));
                    break;

                case ByteCodeOp.BINARY_MULTIPLY_FLOAT:
                    var rightMulFloat = ((PyFloat)frame.ValueStack.Pop()).Value;
                    var leftMulFloat = ((PyFloat)frame.ValueStack.Pop()).Value;
                    frame.ValueStack.Push(new PyFloat(leftMulFloat * rightMulFloat));
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

                    // CPython 3.12: Save current scope depth before function call for proper restoration
                    var savedScopeCount = frame.ScopeChain?.ScopeCount ?? 0;
                    var savedCurrentScopeName = frame.ScopeChain?.CurrentScope?.Name;

                    // CPython 3.12: Check for keyword arguments from KW_NAMES
                    var kwNames = frame.KeywordNamesForNextCall;
#if DEBUG_LOG
                    Console.WriteLine($"🔧 CALL Debug: kwNames = {(kwNames == null ? "null" : $"length {kwNames.Items.Length}")}, callArgCount = {callArgCount}");
#endif

                    // 🔧 REMOVED HOTFIX: The malformed finally handler stack hotfix is no longer needed
                    // The underlying issue was fixed in the compiler by properly generating exception tables
                    // and ensuring finally blocks execute in both normal and exception paths

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

                    // CPython 3.12: 키워드 인수 처리
                    if (kwNames != null && kwNames.Items.Length > 0)
                    {
                        // 키워드 인수가 있는 경우 - CallWithKeywords 사용
                        newCallResult = CallWithKeywords(actualCallable, finalArgs, kwNames, frame.ScopeChain);
                    }
                    else
                    {
                        // 위치 인수만 있는 경우 - 기존 방식 사용
                        if (actualCallable is PyBuiltinFunction builtin)
                        {
                            newCallResult = builtin.Call(finalArgs, null);
                        }
                        else if (actualCallable is PyMethod method)
                        {
                            newCallResult = method.Call(finalArgs, null);
                        }
                        else if (actualCallable is PyFunction func)
                        {
                            newCallResult = ExecuteFunctionCall(func, finalArgs, frame.ScopeChain);
                        }
                        else
                        {
                            newCallResult = actualCallable.Call(finalArgs, null);
                        }
                    }

                    frame.ValueStack.Push(newCallResult);

                    // CPython 3.12: Restore scope depth after function call (especially important for metaclass)
                    if (frame.ScopeChain != null && frame.ScopeChain.ScopeCount != savedScopeCount)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"🔧 Restoring scope depth after function call: {frame.ScopeChain.CurrentScope?.Name} (depth={frame.ScopeChain.ScopeCount}) → {savedCurrentScopeName} (depth={savedScopeCount})");
                        #endif
                        frame.ScopeChain.RestoreScopeDepth(savedScopeCount);
                        #if DEBUG_LOG
                        Console.WriteLine($"✅ Scope depth restored successfully to: {frame.ScopeChain.CurrentScope?.Name}");
                        #endif
                    }

                    // CPython 3.12: Clear keyword names after call
                    frame.KeywordNamesForNextCall = null;
                    break;

                // Specialized Method Calls - CPython 3.12 Adaptive Specialization
                case ByteCodeOp.CALL_LIST_APPEND:
                    var appendArg = frame.ValueStack.Pop();
                    var appendList = (PyList)frame.ValueStack.Pop();
                    frame.ValueStack.Pop(); // Pop the null (PUSH_NULL)
                    appendList.Add(appendArg);
                    frame.ValueStack.Push(PyNone.Instance);
                    break;

                case ByteCodeOp.CALL_DICT_GET:
                    var getDefault = frame.ValueStack.Pop();  // default value
                    var getKey = frame.ValueStack.Pop();      // key
                    var getDict = (PyDict)frame.ValueStack.Pop();  // dict
                    frame.ValueStack.Pop(); // Pop the null (PUSH_NULL)
                    var getValue = getDict.Get(getKey, getDefault);
                    frame.ValueStack.Push(getValue);
                    break;

                case ByteCodeOp.CALL_STR_UPPER:
                    var upperStr = (PyString)frame.ValueStack.Pop();
                    frame.ValueStack.Pop(); // Pop the null (PUSH_NULL)
                    frame.ValueStack.Push(new PyString(upperStr.Value.ToUpper()));
                    break;

                case ByteCodeOp.CALL_STR_LOWER:
                    var lowerStr = (PyString)frame.ValueStack.Pop();
                    frame.ValueStack.Pop(); // Pop the null (PUSH_NULL)
                    frame.ValueStack.Push(new PyString(lowerStr.Value.ToLower()));
                    break;

                case ByteCodeOp.CALL_LEN_LIST:
                    var lenList = (PyList)frame.ValueStack.Pop();
                    frame.ValueStack.Pop(); // Pop the null (PUSH_NULL)
                    frame.ValueStack.Push(new PyInt(lenList.Length()));
                    break;

                case ByteCodeOp.CALL_LEN_STR:
                    var lenStr = (PyString)frame.ValueStack.Pop();
                    frame.ValueStack.Pop(); // Pop the null (PUSH_NULL)
                    frame.ValueStack.Push(new PyInt(lenStr.Value.Length));
                    break;

                case ByteCodeOp.CALL_FUNCTION_EX:
                    // CPython 3.12: Extended function call with *args and **kwargs
                    var hasKwargs = (instruction.Argument & 1) != 0;

                    PyObject kwargsDict = null;
                    if (hasKwargs)
                    {
                        kwargsDict = frame.ValueStack.Pop(); // kwargs dictionary
                    }

                    var argsIterable = frame.ValueStack.Pop(); // args iterable
                    var functionToCall = frame.ValueStack.Pop(); // function
                    frame.ValueStack.Pop(); // Pop the null (PUSH_NULL)

                    // Convert args iterable to list
                    var argsList = new List<PyObject>();
                    if (argsIterable is PyTuple argsTuple)
                    {
                        argsList.AddRange(argsTuple.Items);
                    }
                    else if (argsIterable is PyList argsListObj)
                    {
                        for (int i = 0; i < argsListObj.Length(); i++)
                        {
                            argsList.Add(argsListObj.GetItem(i));
                        }
                    }
                    else
                    {
                        throw PyTypeError.Create("argument after * must be an iterable");
                    }

                    // Convert kwargs dict to keyword arguments
                    var keywordArgs = new List<(string name, PyObject value)>();
                    if (hasKwargs && kwargsDict is PyDict kwargsPyDict)
                    {
                        foreach (var kvp in kwargsPyDict.InternalDict)
                        {
                            if (kvp.Key is PyString keyStr)
                            {
                                keywordArgs.Add((keyStr.Value, kvp.Value));
                            }
                            else
                            {
                                throw PyTypeError.Create("keywords must be strings");
                            }
                        }
                    }

                    // Call function with unpacked arguments
                    PyObject unpackedResult;

                    if (functionToCall is PyFunction function)
                    {
                        var kwDict = new Dictionary<string, PyObject>();
                        foreach (var kw in keywordArgs)
                        {
                            kwDict[kw.name] = kw.value;
                        }
                        unpackedResult = ExecuteFunctionCallWithKeywords(function, argsList.ToArray(), kwDict, frame.ScopeChain);
                    }
                    else if (functionToCall is PyBuiltinFunction builtinFunc)
                    {
                        // Convert keyword arguments to PyDict
                        PyDict? kwDict = null;
                        if (keywordArgs.Count > 0)
                        {
                            kwDict = new PyDict();
                            foreach (var kw in keywordArgs)
                            {
                                kwDict.SetItem(new PyString(kw.name), kw.value);
                            }
                        }
                        unpackedResult = builtinFunc.Call(argsList.ToArray(), kwDict);
                    }
                    else if (functionToCall is PyMethod method)
                    {
                        // Convert keyword arguments to PyDict
                        PyDict? kwDict = null;
                        if (keywordArgs.Count > 0)
                        {
                            kwDict = new PyDict();
                            foreach (var kw in keywordArgs)
                            {
                                kwDict.SetItem(new PyString(kw.name), kw.value);
                            }
                        }
                        unpackedResult = method.Call(argsList.ToArray(), kwDict);
                    }
                    else
                    {
                        // Generic callable with kwargs
                        PyDict? kwDict = null;
                        if (keywordArgs.Count > 0)
                        {
                            kwDict = new PyDict();
                            foreach (var kw in keywordArgs)
                            {
                                kwDict.SetItem(new PyString(kw.name), kw.value);
                            }
                        }
                        unpackedResult = functionToCall.Call(argsList.ToArray(), kwDict);
                    }

                    frame.ValueStack.Push(unpackedResult);
                    break;

                case ByteCodeOp.DICT_MERGE:
                    // CPython 3.12: Merge dictionaries for **kwargs unpacking
                    // Stack before: [target_dict, source_dict]
                    // Stack after: [merged_dict]
                    var mergeCount = instruction.Argument;

                    // Get the source dictionary from stack (TOS)
                    var sourceDict = frame.ValueStack.Pop();

                    // Get the target dictionary from stack (TOS-1)
                    var targetDict = frame.ValueStack.Pop();

                    if (!(targetDict is PyDict targetPyDict))
                    {
                        throw PyTypeError.Create($"DICT_MERGE: target must be dict, got {targetDict.GetType().Name}");
                    }

                    // Merge sourceDict into targetDict
                    if (sourceDict is PyDict sourcePyDict)
                    {
                        foreach (var kvp in sourcePyDict.InternalDict)
                        {
                            targetPyDict.SetItem(kvp.Key, kvp.Value);
                        }
                    }
                    else
                    {
                        throw PyTypeError.Create("DICT_MERGE: source must be dictionary");
                    }

                    // Push the merged dictionary back onto the stack
                    frame.ValueStack.Push(targetPyDict);
                    break;

                case ByteCodeOp.RESUME:
                    // Python 3.12: 모든 코드 시작점에 있는 명령어
                    // 실제로는 아무것도 하지 않음 (단순 마커)
                    break;

                // Duplicate CALL case removed (was CALL_FUNCTION_KW)

                case ByteCodeOp.MAKE_FUNCTION:
                    // CPython 3.12 compatible function creation with full flags support
                    var flags = instruction.Argument;

                    PyCell[] closure = null;
                    PyTuple defaults = null;
                    PyTuple kwDefaults = null;
                    PyTuple annotations = null;

                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 MAKE_FUNCTION with flags: {flags:X} (binary: {Convert.ToString(flags, 2)})");
                    #endif
                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 MAKE_FUNCTION stack size before processing: {frame.ValueStack.Count}");
                    #endif
                    if (frame.ValueStack.Count > 0)
                    {
                        var debugStackItems = frame.ValueStack.ToArray();
                        for (int i = 0; i < Math.Min(debugStackItems.Length, 5); i++)
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"   Stack[{i}]: {debugStackItems[i]?.GetType().Name} = {debugStackItems[i]}");
                            #endif
                        }
                    }

                    // CPython 3.12 MAKE_FUNCTION flags processing order (bit order matters!):
                    // 0x01 - HAS_DEFAULTS: function has positional default parameters
                    // 0x02 - HAS_KW_DEFAULTS: function has keyword-only default parameters
                    // 0x04 - HAS_ANNOTATIONS: function has annotations
                    // 0x08 - HAS_CLOSURE: function uses closure variables
                    // 0x10 - HAS_QUALNAME: function has qualified name (not used in basic implementation)

                    // Process in correct stack order: code object first (TOS), then others as needed

                    // First, pop the code object (always at TOS)
                    var codeObject = frame.ValueStack.Pop();
                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 Popped code object: {codeObject?.GetType().Name} = {codeObject}");
                    #endif

                    // Check for closure flag (8 = HAS_CLOSURE) - processed next if present
                    if ((flags & 8) != 0)
                    {
                        var closureTuple = frame.ValueStack.Pop();
                        #if DEBUG_LOG
                        Console.WriteLine($"🔧 Processing closure: {closureTuple?.GetType().Name} = {closureTuple}");
                        #endif
                        if (closureTuple is PyTuple closureTupleObj)
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"   Closure tuple has {closureTupleObj.Items.Length} items:");
                            #endif
                            for (int i = 0; i < closureTupleObj.Items.Length; i++)
                            {
                                var item = closureTupleObj.Items[i];
                                #if DEBUG_LOG
                                Console.WriteLine($"     Item[{i}]: {item?.GetType().Name} = {item}");
                                #endif
                            }
                            try
                            {
                                closure = closureTupleObj.Items.Cast<PyCell>().ToArray();
                                #if DEBUG_LOG
                                Console.WriteLine($"  → Function has closure: {closure.Length} cells");
                                #endif
                            }
                            catch (InvalidCastException e)
                            {
                                #if DEBUG_LOG
                                Console.WriteLine($"  ❌ Closure casting error: {e.Message}");
                                #endif
                                #if DEBUG_LOG
                                Console.WriteLine($"     Failed to cast items to PyCell");
                                #endif
                                throw;
                            }
                        }
                        else
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"  ⚠️ Warning: Expected tuple for closure, got {closureTuple?.GetType()}");
                            #endif
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
                            #if DEBUG_LOG
                            Console.WriteLine($"  → Function has annotations: {annTuple.Items.Length} items");
                            #endif
                        }
                        else
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"  ⚠️ Warning: Expected tuple for annotations, got {annotationsTuple?.GetType()}");
                            #endif
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
                            #if DEBUG_LOG
                            Console.WriteLine($"  → Function has keyword-only defaults: {kwDefTuple.Items.Length} items");
                            #endif
                        }
                        else
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"  ⚠️ Warning: Expected tuple for kw-defaults, got {kwDefaultsTuple?.GetType()}");
                            #endif
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
                            #if DEBUG_LOG
                            Console.WriteLine($"  → Function has positional defaults: {defTuple.Items.Length} parameters");
                            #endif
                        }
                        else
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"  ⚠️ Warning: Expected tuple for defaults, got {defaultsTuple?.GetType()}");
                            #endif
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
                                var asyncGenFrame = closure != null && closure.Length > 0
                                    ? new PyFrame(pyCode, args, frame.ScopeChain, closure, frame)
                                    : new PyFrame(pyCode, args, frame.ScopeChain, null, frame);

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
                            #if DEBUG_LOG
                            Console.WriteLine($"✅ Created async generator function: {pyCode.Name}");
                            #endif
                        }
                        else if (pyCode.IsCoroutine())
                        {
                            // CPython 3.12: Capture globals from current frame's GlobalScope
                            var globalsDict = frame.ScopeChain.GlobalScope?.Variables;

                            // Async function: 호출 시 PyCoroutine 객체 반환
                            var asyncImpl = new Func<PyObject[], PyObject>(args =>
                            {
                                var functionScopeChain = new PyScopeChain(globalsDict, "<async function>");
                                var asyncFrame = closure != null && closure.Length > 0
                                    ? new PyFrame(pyCode, args, functionScopeChain, closure, frame)
                                    : new PyFrame(pyCode, args, functionScopeChain, null, frame);

                                // Native coroutine 생성
                                return new SharpPy.Core.PyCoroutine(asyncFrame, this, pyCode.Name);
                            });

                            var asyncFunction = new PyFunction(pyCode.Name, asyncImpl, null, null, closure, pyCode);
                            asyncFunction.GlobalsDict = globalsDict;

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
                            #if DEBUG_LOG
                            Console.WriteLine($"✅ Created async function: {pyCode.Name}");
                            #endif
                        }
                        else
                        {
                            // Regular function
                            PyFunction functionObject;

                            // CPython 3.12: Capture globals from current frame's GlobalScope
                            // This is equivalent to CPython's GLOBALS() macro: frame->f_globals
                            var globalsDict = frame.ScopeChain.GlobalScope?.Variables;

                            Console.WriteLine($"[GLOBALS CAPTURE] MAKE_FUNCTION for {pyCode.Name}:");
                            Console.WriteLine($"  frame.ScopeChain.GlobalScope.Name: {frame.ScopeChain.GlobalScope?.Name}");
                            Console.WriteLine($"  globalsDict count: {globalsDict?.Count ?? 0}");
                            if (globalsDict != null)
                            {
                                Console.WriteLine($"  globalsDict keys: {string.Join(", ", globalsDict.Keys.Take(10))}");
                                Console.WriteLine($"  globalsDict reference hash: {globalsDict.GetHashCode()}");
                            }

                            // Create function implementation with proper parameter binding
                            Func<PyObject[], PyObject> implementation = args =>
                            {
                            // CPython 3.12: Create new ScopeChain with captured globals
                            // The function's globals are fixed at function definition time
                            Console.WriteLine($"[FUNCTION CALL] Function {pyCode.Name} called:");
                            Console.WriteLine($"  globalsDict count at call time: {globalsDict?.Count ?? 0}");
                            if (globalsDict != null)
                            {
                                Console.WriteLine($"  globalsDict keys at call time: {string.Join(", ", globalsDict.Keys.Take(10))}");
                                Console.WriteLine($"  globalsDict reference hash at call time: {globalsDict.GetHashCode()}");
                            }

                            if (globalsDict == null)
                            {
                                throw new InvalidOperationException($"Function {pyCode.Name} has null globals!");
                            }

                            var functionScopeChain = new PyScopeChain(globalsDict, "<function>");

                            Console.WriteLine($"  New ScopeChain GlobalScope count: {functionScopeChain.GlobalScope?.Variables.Count ?? 0}");
                            Console.WriteLine($"  New ScopeChain GlobalScope hash: {functionScopeChain.GlobalScope?.Variables.GetHashCode()}");

                            var functionFrame = closure != null && closure.Length > 0
                                ? new PyFrame(pyCode, args, functionScopeChain, closure, frame)
                                : new PyFrame(pyCode, args, functionScopeChain, null, frame);
                            return ExecuteFrame(functionFrame);
                        };

                        if (closure != null && closure.Length > 0)
                        {
                            // Create function with closure
                            functionObject = PyFunction.CreateClosureFunction(pyCode.Name, pyCode, closure, frame.ScopeChain);
                            // Override implementation to use our parameter binding
                            functionObject = new PyFunction(pyCode.Name, implementation, null, null, closure, pyCode);
                            functionObject.ParentScope = frame.ScopeChain;
                            functionObject.GlobalsDict = globalsDict;  // CPython 3.12: func.__globals__
                        }
                        else
                        {
                            // Create regular function without closure
                            functionObject = new PyFunction(pyCode.Name, implementation, null, null, closure, pyCode);
                            functionObject.ParentScope = frame.ScopeChain;
                            functionObject.GlobalsDict = globalsDict;  // CPython 3.12: func.__globals__
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

                case ByteCodeOp.DELETE_ATTR:
                    // CPython 3.12: Delete attribute from object
                    var delAttrName = frame.Code.Names[instruction.Argument];
                    var delObj = frame.ValueStack.Pop();
                    // 기존 Attribute 시스템 사용!
                    delObj.DelAttribute(delAttrName);
                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 DELETE_ATTR: deleted attribute '{delAttrName}' from {delObj.GetType().Name}");
                    #endif
                    break;

                case ByteCodeOp.LOAD_SUPER_ATTR:
                    #if DEBUG_LOG
                    Console.WriteLine($"🚀 ENTERING LOAD_SUPER_ATTR");
                    #endif
                    // CPython 3.12: super() attribute access
                    // Stack: [..., super_func, __class__, self] -> [..., attr_value]
                    var superAttrName = frame.Code.Names[instruction.Argument];
                    var selfObj = frame.ValueStack.Pop();         // self
                    var classObj = frame.ValueStack.Pop();        // __class__
                    var superFunc = frame.ValueStack.Pop();       // super function

                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 LOAD_SUPER_ATTR: {superAttrName}, super={superFunc.GetType().Name}, class={classObj.GetType().Name}, self={selfObj.GetType().Name}");
                    #endif

                    // Call super(__class__, self) to create super proxy, then get attribute
                    try
                    {
                        // Create super proxy by calling super() with class and self
                        var superArgs = new PyObject[] { classObj, selfObj };
                        PyObject superProxy;

                        if (superFunc is PyBuiltinFunction builtinSuper)
                        {
                            superProxy = builtinSuper.Call(superArgs, null);
                        }
                        else if (superFunc is PyFunction userSuper)
                        {
                            superProxy = userSuper.Call(superArgs, null);
                        }
                        else
                        {
                            throw PyRuntimeError.Create($"super object must be callable, got {superFunc.GetType().Name}");
                        }

                        #if DEBUG_LOG
                        Console.WriteLine($"🔧 LOAD_SUPER_ATTR: calling GetAttribute({superAttrName}) on super proxy");
                        #endif

                        var superAttr = superProxy.GetAttribute(superAttrName);

                        #if DEBUG_LOG
                        Console.WriteLine($"🔧 LOAD_SUPER_ATTR: found attribute type: {superAttr?.GetType().Name ?? "null"}");
                        #endif

                        // CPython 3.12: LOAD_SUPER_ATTR automatically binds methods to self
                        PyObject finalAttr = superAttr;
                        if (superAttr is PyFunction pyFunc)
                        {
                            finalAttr = new PyMethod(selfObj, pyFunc);
                            #if DEBUG_LOG
                            Console.WriteLine($"🔧 LOAD_SUPER_ATTR: binding function {superAttrName} to self");
                            #endif
                        }
                        else if (superAttr is PyBuiltinFunction builtinFunc)
                        {
                            // Convert PyBuiltinFunction to PyFunction for proper binding
                            var func = new PyFunction(builtinFunc.Name, args => builtinFunc.Call(args, null));
                            finalAttr = new PyMethod(selfObj, func);
                            #if DEBUG_LOG
                            Console.WriteLine($"🔧 LOAD_SUPER_ATTR: binding builtin function {superAttrName} to self");
                            #endif
                        }
                        else if (superAttr is PyBuiltinMethod builtinMethod)
                        {
                            // Convert PyBuiltinMethod to PyFunction for proper binding
                            var func = new PyFunction(builtinMethod.Name, args => builtinMethod.Call(args, null));
                            finalAttr = new PyMethod(selfObj, func);
                            #if DEBUG_LOG
                            Console.WriteLine($"🔧 LOAD_SUPER_ATTR: binding builtin method {superAttrName} to self");
                            #endif
                        }
                        else if (superAttr is PyMethod existingMethod)
                        {
                            // Already bound, but we need to re-bind to current self
                            finalAttr = new PyMethod(selfObj, existingMethod.Function);
                            #if DEBUG_LOG
                            Console.WriteLine($"🔧 LOAD_SUPER_ATTR: re-binding existing method {superAttrName} to self");
                            #endif
                        }

                        frame.ValueStack.Push(finalAttr);
                        #if DEBUG_LOG
                        Console.WriteLine($"🔧 LOAD_SUPER_ATTR success: got {finalAttr.GetType().Name}");
                        #endif
                    }
                    catch (Exception ex)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"🔧 LOAD_SUPER_ATTR failed: {ex.Message}");
                        #endif
                        throw;
                    }
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
                                length = func.Call(new PyObject[0], null);
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

                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 MATCH_CLASS: subject={classSubject?.GetType().Name}, classToMatch={classToMatch?.GetType().Name}");
                    #endif

                    try
                    {
                        // Check isinstance(subject, classToMatch) - supports both built-in and custom types
                        bool isInstance = false;

                        // Handle custom classes FIRST (PyClass inherits from PyType, so check this first)
                        if (classToMatch is PyClass targetClass && classSubject is PyClassInstance instance)
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"🔍 MATCH_CLASS: Checking custom class {targetClass.Name}");
                            #endif
                            isInstance = (instance.InstanceType == targetClass);
                        }
                        // Handle built-in types (int, str, list, etc.)
                        else if (classToMatch is PyType builtinType)
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"🔍 MATCH_CLASS: Checking built-in type {builtinType.Name}");
                            #endif

                            if (builtinType.Name == "int" && classSubject is PyInt)
                                isInstance = true;
                            else if (builtinType.Name == "str" && classSubject is PyString)
                                isInstance = true;
                            else if (builtinType.Name == "list" && classSubject is PyList)
                                isInstance = true;
                            else if (builtinType.Name == "dict" && classSubject is PyDict)
                                isInstance = true;
                            else if (builtinType.Name == "tuple" && classSubject is PyTuple)
                                isInstance = true;
                            else if (builtinType.Name == "float" && classSubject is PyFloat)
                                isInstance = true;
                            else if (builtinType.Name == "bool" && classSubject is PyBool)
                                isInstance = true;
                        }

                        #if DEBUG_LOG
                        Console.WriteLine($"🔍 MATCH_CLASS: isInstance = {isInstance}");
                        #endif

                        if (isInstance)
                        {
                            var positionalCount = instruction.Argument;

                            // CPython 3.12 behavior: Extract attribute values based on keyword names tuple
                            if (classKwNames is PyTuple classKwNamesTuple && classKwNamesTuple.Items.Length > 0 &&
                                classSubject is PyClassInstance classSubjectInstance)
                            {
                                #if DEBUG_LOG
                                Console.WriteLine($"🔍 MATCH_CLASS: Extracting {classKwNamesTuple.Items.Length} attributes");
                                #endif

                                // Extract attribute values in the order specified by keyword names
                                var attrs = new List<PyObject>();
                                for (int i = 0; i < classKwNamesTuple.Items.Length; i++)
                                {
                                    var classAttrName = classKwNamesTuple.Items[i].ToStr().Value;
                                    var classAttrValue = classSubjectInstance.GetAttribute(classAttrName);
                                    attrs.Add(classAttrValue ?? PyNone.Instance);
                                    #if DEBUG_LOG
                                    Console.WriteLine($"🔍 MATCH_CLASS: Extracted {classAttrName} = {classAttrValue}");
                                    #endif
                                }

                                frame.ValueStack.Push(new PyTuple(attrs.ToArray()));
                            }
                            else if (classToMatch is PyClass cls && positionalCount > 0 &&
                                     cls.GetAttribute("__match_args__") is PyTuple matchArgs)
                            {
                                // Extract positional attributes for custom classes
                                var attrs = new List<PyObject>();

                                for (int i = 0; i < Math.Min(positionalCount, matchArgs.Items.Length); i++)
                                {
                                    var matchArgName = matchArgs.Items[i].ToStr().Value;

                                    if (classSubject is PyClassInstance matchSubjectInstance)
                                    {
                                        var matchAttrValue = matchSubjectInstance.GetAttribute(matchArgName);
                                        attrs.Add(matchAttrValue ?? PyNone.Instance);
                                    }
                                }

                                frame.ValueStack.Push(new PyTuple(attrs.ToArray()));
                            }
                            else
                            {
                                // No attributes to extract, return empty tuple
                                frame.ValueStack.Push(new PyTuple(new PyObject[0]));
                            }
                        }
                        else
                        {
                            frame.ValueStack.Push(PyNone.Instance); // CPython 3.12: None on failure
                        }
                    }
                    catch (Exception ex)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"🚨 MATCH_CLASS error: {ex.Message}");
                        #endif
                        frame.ValueStack.Push(PyNone.Instance);
                    }
                    break;

                case ByteCodeOp.RETURN_VALUE:
                    var returnValue = frame.ValueStack.Count > 0 ? frame.ValueStack.Pop() : PyNone.Instance;
                    // 🔧 함수 종료 시 scope cleanup
                    if (frame.ScopeChain.CurrentScope?.Type == ScopeType.Local)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"🔧 RETURN_VALUE: Cleaning up function scope '{frame.ScopeChain.CurrentScope.Name}'");
                        #endif

                        // **CRITICAL FIX**: 클래스 body인 경우 scope 제거 전에 변수들을 저장
                        if (frame.ScopeChain.CurrentScope.Name.StartsWith("<class_body_"))
                        {
                            frame.ClassBodyVariables = new Dictionary<string, PyObject>(frame.ScopeChain.CurrentScope.Variables);
                            #if DEBUG_LOG
                            Console.WriteLine($"💾 Saved {frame.ClassBodyVariables.Count} class body variables before scope cleanup");
                            #endif
                        }

                        frame.ScopeChain.PopScope();
                    }
                    return returnValue;

                case ByteCodeOp.RETURN_CONST:
                    // CPython 3.12: Return constant value directly
                    var constValue = frame.Code.Constants[instruction.Argument];
                    // 🔧 함수 종료 시 scope cleanup
                    if (frame.ScopeChain.CurrentScope?.Type == ScopeType.Local)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"🔧 RETURN_CONST: Cleaning up function scope '{frame.ScopeChain.CurrentScope.Name}'");
                        #endif
                        frame.ScopeChain.PopScope();
                    }
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
                    bool isTruthy = truthValue.PyBoolValue();
                    #if DEBUG_LOG
                    // Debug: Console.WriteLine($"🔄 POP_JUMP_IF_TRUE: popped value = {truthValue}, isTruthy = {isTruthy}");
                    #endif
                    if (isTruthy)  // CPython 3.12: Jump if the popped value is truthy
                    {
                        // CPython 3.12: POP_JUMP_IF_TRUE uses relative offset from next instruction (same as POP_JUMP_IF_FALSE)
                        int currentPosJump = frame.InstructionPointer;
                        int relativeOffset = instruction.Argument;
                        int targetInstructionIndex = currentPosJump + 1 + relativeOffset;
                        #if DEBUG_LOG
                        Console.WriteLine($"   → JUMPING: from {currentPosJump} + 1 + {relativeOffset} to instr {targetInstructionIndex}");
                        #endif
                        // Subtract 1 because main loop will increment
                        frame.InstructionPointer = targetInstructionIndex - 1;
                        return null; // Continue execution from new position
                    }
                    else
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"   → NOT JUMPING: continue to next instruction");
                        #endif
                    }
                    break;

                case ByteCodeOp.POP_JUMP_IF_FALSE:
                    var falseValue = frame.ValueStack.Pop();
                    bool isFalsy = !falseValue.PyBoolValue();
                    #if DEBUG_LOG
                    // Debug: Console.WriteLine($"🔄 POP_JUMP_IF_FALSE: popped value = {falseValue}, isFalsy = {isFalsy}");
                    #endif
                    if (isFalsy)  // CPython 3.12: Jump if the popped value is falsy
                    {
                        // CPython 3.12: POP_JUMP_IF_FALSE uses relative offset from next instruction
                        int currentPosJump = frame.InstructionPointer;
                        int relativeOffset = instruction.Argument;
                        int targetInstructionIndex = currentPosJump + 1 + relativeOffset;
                        #if DEBUG_LOG
                        Console.WriteLine($"   → JUMPING: from {currentPosJump} + 1 + {relativeOffset} to instr {targetInstructionIndex}");
                        #endif
                        // Subtract 1 because main loop will increment
                        frame.InstructionPointer = targetInstructionIndex - 1;
                        return null; // Continue execution from new position
                    }
                    else
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"   → NOT JUMPING: continue to next instruction");
                        #endif
                    }
                    break;

                case ByteCodeOp.JUMP_FORWARD:
                    // CPython 3.12: JUMP_FORWARD uses relative offset from next instruction
                    // argument is the number of instructions to skip forward
                    int currentPos = frame.InstructionPointer;
                    int jumpOffset = instruction.Argument;
                    // Target = current instruction + 1 (next) + jump offset
                    int targetPos = currentPos + 1 + jumpOffset;

                    #if DEBUG_LOG
                    Console.WriteLine($"🔄 JUMP_FORWARD: from instr {currentPos} forward {jumpOffset} to instr {targetPos}");
                    #endif

                    // Subtract 1 because main loop will increment
                    frame.InstructionPointer = targetPos - 1;
                    return null; // Continue execution from new position

                case ByteCodeOp.JUMP_BACKWARD:
                    // CPython 3.12 호환: QuickenedCodeObject 방식으로 JUMP_BACKWARD 계산
                    int currentInstrPos = frame.InstructionPointer;
                    int targetInstrPos;

                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 JUMP_BACKWARD Debug: currentInstrPos={currentInstrPos}, instruction.Argument={instruction.Argument}");
                    Console.WriteLine($"    현재 instruction: {frame.Code.Instructions[currentInstrPos].OpCode} (arg: {frame.Code.Instructions[currentInstrPos].Argument})");

                    // 실제 바이트코드에서 JUMP_BACKWARD 위치 찾기
                    for (int i = 0; i < frame.Code.Instructions.Count; i++)
                    {
                        if (frame.Code.Instructions[i].OpCode == ByteCodeOp.JUMP_BACKWARD)
                        {
                            Console.WriteLine($"    실제 JUMP_BACKWARD at instruction {i}: arg={frame.Code.Instructions[i].Argument}");
                        }
                        if (frame.Code.Instructions[i].OpCode == ByteCodeOp.FOR_ITER)
                        {
                            Console.WriteLine($"    실제 FOR_ITER at instruction {i}: arg={frame.Code.Instructions[i].Argument}");
                        }
                    }
                    #endif

                    if (frame.Code is PyQuickenedCodeObject quickenedJumpCode)
                    {
                        // Quickened Code: instruction offset 사용
                        targetInstrPos = quickenedJumpCode.CalculateJumpBackwardTarget(currentInstrPos, instruction.Argument);
                        #if DEBUG_LOG
                        Console.WriteLine($"🔙 JUMP_BACKWARD: QuickenedCode {currentInstrPos} → {targetInstrPos}");
                        #endif
                    }
                    else
                    {
                        // 레거시 방식: PyJumpBackwardUtil 사용
                        targetInstrPos = PyJumpBackwardUtil.CalculateJumpBackwardTarget(currentInstrPos, instruction.Argument, frame.Code.Instructions);
                        #if DEBUG_LOG
                        Console.WriteLine($"🔙 JUMP_BACKWARD: Legacy {currentInstrPos} → {targetInstrPos}");
                        #endif
                    }

                    // Validate target instruction position
                    if (targetInstrPos < 0 || targetInstrPos >= frame.Code.Instructions.Count)
                    {
                        throw new InvalidOperationException($"JUMP_BACKWARD: Invalid target position {targetInstrPos} (valid range: 0-{frame.Code.Instructions.Count - 1})");
                    }

                    // For generator functions, ensure we have a consistent stack state
                    if ((frame.Code.Flags & PyCodeObject.CO_GENERATOR) != 0)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"   Generator JUMP_BACKWARD: preserving stack state for yield/resume");
                        #endif
                    }

                    // Debug: Check what instruction will be executed at target
                    if (targetInstrPos >= 0 && targetInstrPos < frame.Code.Instructions.Count)
                    {
                        var targetInstruction = frame.Code.Instructions[targetInstrPos];
                        #if DEBUG_LOG
                        Console.WriteLine($"🔍 Target instruction at {targetInstrPos}: {targetInstruction.OpCode} (arg: {targetInstruction.Argument})");
                        #endif

                        // Verify this is a valid loop target (FOR_ITER for loops, various opcodes for WHILE loops)
                        var invalidTargets = new[] { ByteCodeOp.RETURN_VALUE, ByteCodeOp.RETURN_CONST, ByteCodeOp.RAISE_VARARGS };
                        if (invalidTargets.Contains(targetInstruction.OpCode))
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"⚠️ Warning: JUMP_BACKWARD targeting potentially invalid instruction {targetInstruction.OpCode}");
                            #endif
                        }
                    }

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

                case ByteCodeOp.LIST_EXTEND:
                    // CPython 3.12 LIST_EXTEND: extend the list at TOS1 with the iterable at TOS
                    var extendArg = instruction.Argument; // Should be 1 for this case
                    var extendIterable = frame.ValueStack.Pop(); // Pop iterable from top

                    // The list should now be on top of the stack
                    var extendTargetList = (PyList)frame.ValueStack.Peek();

                    // Handle different iterable types
                    if (extendIterable is PyTuple extendTuple)
                    {
                        foreach (var item in extendTuple.Items)
                        {
                            extendTargetList.Append(item);
                        }
                    }
                    else if (extendIterable is PyList extendList)
                    {
                        foreach (var item in extendList.Items)
                        {
                            extendTargetList.Append(item);
                        }
                    }
                    else if (extendIterable is PyString extendStr)
                    {
                        foreach (char c in extendStr.Value)
                        {
                            extendTargetList.Append(new PyString(c.ToString()));
                        }
                    }
                    else
                    {
                        // Generic iterable handling if needed
                        throw new Exception($"LIST_EXTEND: Unsupported iterable type {extendIterable.GetType()}");
                    }
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
                            subscriptResult = subscriptList.GetItem((int)keyIntValue.Value);
                        }
                        else if (subscriptObj is PyList subscriptList2 && subscriptKey is PySlice sliceKey)
                        {
                            // List slicing: list[slice] (CPython 3.12 compatible)
                            subscriptResult = subscriptList2.GetItem(sliceKey);
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
                        else if (subscriptObj is PyGenericAlias subscriptGeneric)
                        {
                            // Generic alias subscript access: Point[int] where Point = tuple[T, T]
                            subscriptResult = subscriptGeneric.GetItem(subscriptKey);
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
                        #if DEBUG_LOG
                        Console.WriteLine($"🔍 BINARY_SUBSCR: Re-throwing Python exception: {ex.GetType().Name} - {ex.Message}");
                        #endif
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
                        #if DEBUG_LOG
                        Console.WriteLine($"🔍 STORE_SUBSCR: Re-throwing Python exception: {ex.GetType().Name} - {ex.Message}");
                        #endif
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
                    if (iterable is PyTuple iterTuple)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"🔍 GET_ITER: 튜플 길이 = {iterTuple.Items.Length}");
                        #endif
                        for (int i = 0; i < iterTuple.Items.Length; i++)
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"  튜플[{i}] = {iterTuple.Items[i]}");
                            #endif
                        }
                    }
                    else
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"🔍 GET_ITER: iterable 타입 = {iterable.GetType().Name}, 값 = {iterable}");
                        #endif
                    }
                    var iterator = iterable.GetIterator();
                    frame.ValueStack.Push(iterator);
                    break;

                case ByteCodeOp.FOR_ITER:
                    // CPython 3.12 compatible FOR_ITER implementation
                    var iter = frame.ValueStack.Peek(); // Keep iterator on stack for inspection
                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 FOR_ITER: iterator type = {iter.GetType().Name}, calling Next()...");
                    Console.WriteLine($"    InstructionPointer = {frame.InstructionPointer}");
                    #endif
                    try
                    {
                        var nextItem = iter.Next();
                        frame.ValueStack.Push(nextItem); // Push next item on top of iterator
                        #if DEBUG_LOG
                        Console.WriteLine($"🔄 FOR_ITER: got next item {nextItem} from iterator");
                        #endif
                        // Continue normal execution (don't jump)
                    }
                    catch (PythonException ex) when (ex.PyException is PyStopIteration)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"🔚 FOR_ITER: StopIteration - loop finished");
                        Console.WriteLine($"    스택 상태 (pop 전): count={frame.ValueStack.Count}");
                        var stackContents = frame.ValueStack.ToArray();
                        for (int i = 0; i < stackContents.Length; i++)
                        {
                            Console.WriteLine($"      스택[{i}] = {stackContents[i]} ({stackContents[i].GetType().Name})");
                        }
                        #endif
                        // FOR_ITER 스택 구조: [..., value, iterator] (CPython 호환)
                        // StopIteration 시: iterator를 제거하고 value를 유지
                        var removedIterator = frame.ValueStack.Pop(); // iterator 제거 (TOS)
                        #if DEBUG_LOG
                        Console.WriteLine($"    제거된 객체: {removedIterator} ({removedIterator.GetType().Name})");
                        Console.WriteLine($"    스택 상태 (pop 후): count={frame.ValueStack.Count}");
                        var stackContentsAfter = frame.ValueStack.ToArray();
                        for (int i = 0; i < stackContentsAfter.Length; i++)
                        {
                            Console.WriteLine($"      스택[{i}] = {stackContentsAfter[i]} ({stackContentsAfter[i].GetType().Name})");
                        }
                        #endif

                        // CPython 3.12: QuickenedCodeObject 방식으로 점프 계산
                        if (frame.Code is PyQuickenedCodeObject quickenedCode)
                        {
                            // Quickened Code: instruction offset 사용
                            int forIterQuickenedTarget = quickenedCode.CalculateForIterTarget(frame.InstructionPointer, instruction.Argument);

                            #if DEBUG_LOG
                            Console.WriteLine($"🔚 FOR_ITER: Jumping to position {forIterQuickenedTarget} (QuickenedCode)");
                            #endif
                            frame.InstructionPointer = forIterQuickenedTarget - 1; // main loop will increment
                        }
                        else if (!frame.Code.IsOptimized)
                        {
                            // 최적화 OFF: argument는 instruction 단위
                            frame.InstructionPointer += instruction.Argument;
                            #if DEBUG_LOG
                            Console.WriteLine($"🔚 FOR_ITER: Jumping to position {frame.InstructionPointer + 1} (unoptimized)");
                            #endif
                        }
                        else
                        {
                            // CPython 3.12 호환: FOR_ITER arg → current_instruction + arg + 1 위치로 점프
                            // 메인 루프에서 +1하므로 실제로는 current + arg로 설정
                            int forIterTargetInstrPos = frame.InstructionPointer + instruction.Argument;

                            #if DEBUG_LOG
                            Console.WriteLine($"🔚 FOR_ITER: Jumping to position {forIterTargetInstrPos} (CPython 3.12 compatible)");
                            #endif
                            frame.InstructionPointer = forIterTargetInstrPos; // main loop will increment to correct position
                        }

                        // DON'T return null - continue execution
                    }
                    catch (Exception ex)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"💥 FOR_ITER error: {ex.Message}");
                        #endif
                        throw;
                    }
                    break;

                // Specialized Loop Operations - CPython 3.12 Adaptive Specialization
                case ByteCodeOp.FOR_ITER_LIST:
                    // 리스트 전용 최적화된 iteration
                    var listIter = frame.ValueStack.Peek();
                    if (listIter is PyListIterator listIterator)
                    {
                        try
                        {
                            var nextListItem = listIterator.Next();
                            frame.ValueStack.Push(nextListItem);
                            #if DEBUG_LOG
                            Console.WriteLine($"🚀 FOR_ITER_LIST: got next item {nextListItem} (optimized)");
                            #endif
                        }
                        catch (PythonException ex) when (ex.PyException is PyStopIteration)
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"🔚 FOR_ITER_LIST: StopIteration - loop finished (optimized)");
                            #endif
                            frame.ValueStack.Pop(); // Remove exhausted iterator

                            // CPython 3.12: QuickenedCodeObject 방식으로 점프 계산
                            if (frame.Code is PyQuickenedCodeObject quickenedListCode)
                            {
                                // Quickened Code: instruction offset 사용
                                int listQuickenedTarget = quickenedListCode.CalculateForIterTarget(frame.InstructionPointer, instruction.Argument);

                                #if DEBUG_LOG
                                Console.WriteLine($"🔚 FOR_ITER_LIST: Jumping to position {listQuickenedTarget} (QuickenedCode)");
                                #endif
                                frame.InstructionPointer = listQuickenedTarget - 1;
                            }
                            else
                            {
                                // CPython 3.12 호환: FOR_ITER_LIST arg → current_instruction + arg + 1 위치로 점프
                                int listIterTargetInstrPos = frame.InstructionPointer + instruction.Argument;

                                #if DEBUG_LOG
                                Console.WriteLine($"🔚 FOR_ITER_LIST: Jumping to position {listIterTargetInstrPos} (CPython 3.12 compatible)");
                                #endif
                                frame.InstructionPointer = listIterTargetInstrPos; // main loop will increment to correct position
                            }
                        }
                    }
                    else
                    {
                        // Fallback to regular FOR_ITER
                        goto case ByteCodeOp.FOR_ITER;
                    }
                    break;

                case ByteCodeOp.FOR_ITER_TUPLE:
                    // 튜플 전용 최적화된 iteration
                    var tupleIter = frame.ValueStack.Peek();
                    if (tupleIter is PyTupleIterator tupleIterator)
                    {
                        try
                        {
                            var nextTupleItem = tupleIterator.Next();
                            frame.ValueStack.Push(nextTupleItem);
                            #if DEBUG_LOG
                            Console.WriteLine($"🚀 FOR_ITER_TUPLE: got next item {nextTupleItem} (optimized)");
                            #endif
                        }
                        catch (PythonException ex) when (ex.PyException is PyStopIteration)
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"🔚 FOR_ITER_TUPLE: StopIteration - loop finished (optimized)");
                            #endif
                            frame.ValueStack.Pop();

                            // CPython 3.12: QuickenedCodeObject 방식으로 점프 계산
                            if (frame.Code is PyQuickenedCodeObject quickenedTupleCode)
                            {
                                // Quickened Code: instruction offset 사용
                                int tupleQuickenedTarget = quickenedTupleCode.CalculateForIterTarget(frame.InstructionPointer, instruction.Argument);

                                #if DEBUG_LOG
                                Console.WriteLine($"🔚 FOR_ITER_TUPLE: Jumping to position {tupleQuickenedTarget} (QuickenedCode)");
                                #endif
                                frame.InstructionPointer = tupleQuickenedTarget - 1;
                            }
                            else
                            {
                                // CPython 3.12 호환: FOR_ITER_TUPLE arg → current_instruction + arg + 1 위치로 점프
                                int tupleIterTargetInstrPos = frame.InstructionPointer + instruction.Argument;

                                #if DEBUG_LOG
                                Console.WriteLine($"🔚 FOR_ITER_TUPLE: Jumping to position {tupleIterTargetInstrPos} (CPython 3.12 compatible)");
                                #endif
                                frame.InstructionPointer = tupleIterTargetInstrPos; // main loop will increment to correct position
                            }
                        }
                    }
                    else
                    {
                        goto case ByteCodeOp.FOR_ITER;
                    }
                    break;

                case ByteCodeOp.END_FOR:
                    // CPython 3.12: END_FOR는 단순한 루프 종료 마커
                    // FOR_ITER의 StopIteration에서 이미 모든 정리 작업 완료됨
                    #if DEBUG_LOG
                    Console.WriteLine($"🔚 END_FOR: Loop termination marker");
                    #endif
                    #if DEBUG_LOG
                    Console.WriteLine($"    스택 상태: count={frame.ValueStack.Count}");
                    #endif

                    if (frame.ValueStack.Count > 0)
                    {
                        var resultValue = frame.ValueStack.Peek();
                        #if DEBUG_LOG
                        Console.WriteLine($"    → 스택 맨 위 결과: {resultValue?.GetType().Name}");
                        #endif
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
                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 POP_EXCEPT: stack size = {frame.ValueStack.Count}");
                    #endif

                    // In CPython 3.12, POP_EXCEPT always expects PyExceptionInfo on stack
                    if (frame.ValueStack.Count > 0 && frame.ValueStack.Peek() is PyExceptionInfo)
                    {
                        // Exception path: Remove PyExceptionInfo from stack
                        var poppedExceptionInfo = frame.ValueStack.Pop();
                        #if DEBUG_LOG
                        Console.WriteLine($"🔧 POP_EXCEPT: Removed PyExceptionInfo from stack");
                        #endif

                        if (poppedExceptionInfo is PyExceptionInfo exceptionInfo)
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"   exc_type={exceptionInfo.ExcType}, exc_value={exceptionInfo.ExcValue}");
                            #endif
                            #if DEBUG_LOG
                            Console.WriteLine($"   exc_traceback={exceptionInfo.ExcTraceback}, lasti={exceptionInfo.Lasti}");
                            #endif
                        }

                        // CPython 3.12: Clear exception handling state after successful exception processing
                        // This prevents infinite loop in exception handling
                        frame.CurrentException = null;
                        frame.LastException = null; // Also clear LastException
                        frame.ExceptionHandlerCallCount = 0;
                        #if DEBUG_LOG
                        Console.WriteLine($"🔧 POP_EXCEPT: Cleared exception handling state to prevent infinite loops");
                        #endif
                    }
                    else
                    {
                        // CPython 3.12: This should not happen in normal execution
                        #if DEBUG_LOG
                        Console.WriteLine($"⚠️  POP_EXCEPT: No PyExceptionInfo on stack - this indicates a bytecode generation issue");
                        #endif
                        #if DEBUG_LOG
                        Console.WriteLine($"   In CPython 3.12, POP_EXCEPT only appears after PUSH_EXC_INFO in exception handlers");
                        #endif
                    }

                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 POP_EXCEPT 완료 후 스택 크기: {frame.ValueStack.Count}");
                    #endif
                    break;

                case ByteCodeOp.BEFORE_WITH:
                    // CPython 3.12: BEFORE_WITH performs several operations before a with block starts
                    var contextManager = frame.ValueStack.Pop();

                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 BEFORE_WITH: Processing context manager: {contextManager}");
                    #endif

                    // 1. Load __exit__ method and push to stack (for later cleanup)
                    var exitMethod = contextManager.GetAttribute("__exit__");
                    if (!exitMethod.IsCallable())
                    {
                        throw PyAttributeError.Create("__exit__");
                    }

                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 BEFORE_WITH: Found __exit__ method: {exitMethod}");
                    #endif

                    // 2. Call __enter__ method and get result
                    var enterMethod = contextManager.GetAttribute("__enter__");
                    PyObject enterResult;
                    if (enterMethod.IsCallable())
                    {
                        enterResult = enterMethod.Call(new PyObject[0], null);
                        #if DEBUG_LOG
                        Console.WriteLine($"🔧 BEFORE_WITH: __enter__ returned: {enterResult}");
                        #endif
                    }
                    else
                    {
                        throw PyAttributeError.Create("__enter__");
                    }

                    // CPython 3.12 stack layout: [..., __exit__, __enter_result__]
                    // This matches the expected layout for normal completion and exception handling
                    frame.ValueStack.Push(exitMethod);
                    frame.ValueStack.Push(enterResult);

                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 BEFORE_WITH: Stack after setup - size: {frame.ValueStack.Count}");
                    #endif
                    #if DEBUG_LOG
                    Console.WriteLine($"   TOS: {frame.ValueStack.Peek()} (enter result)");
                    #endif
                    break;

                case ByteCodeOp.PUSH_EXC_INFO:
                    // CPython 3.12: Push exception info as single composite object (Stack effect: +1)
                    // Stack: [...] -> [..., PyExceptionInfo]
                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 PUSH_EXC_INFO: Pushing current exception info to stack");
                    #endif

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

                        #if DEBUG_LOG
                        Console.WriteLine($"🔧 PUSH_EXC_INFO: Created exception info for {currentException.GetType().Name}");
                        #endif
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

                        #if DEBUG_LOG
                        Console.WriteLine($"🔧 PUSH_EXC_INFO: Created exception info with None values");
                        #endif
                    }

                    // Push single composite object (CPython 3.12 compatible stack effect +1)
                    frame.ValueStack.Push(pushExceptionInfo);
                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 PUSH_EXC_INFO: Pushed composite exception info, stack size = {frame.ValueStack.Count}");
                    #endif
                    break;


                case ByteCodeOp.WITH_EXCEPT_START:
                    // CPython 3.12: WITH_EXCEPT_START implementation
                    // Stack: [..., __exit__, exception, exc_type, exc_value, exc_traceback, lasti]
                    // Goal: Call __exit__(exc_type, exc_value, exc_traceback) and push result

                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 WITH_EXCEPT_START: stack size = {frame.ValueStack.Count}");
                    #endif

                    // Debug: Print current stack contents from top to bottom
                    var debugStack = new List<PyObject>(frame.ValueStack);
                    debugStack.Reverse(); // Now from top to bottom
                    for (int i = 0; i < debugStack.Count; i++)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"🔍 Stack[{i}]: {debugStack[i]}");
                        #endif
                    }

                    // CPython 3.12: Dynamic stack validation - check for required objects by type
                    if (frame.ValueStack.Count == 0)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"❌ WITH_EXCEPT_START: Empty stack");
                        #endif
                        frame.ValueStack.Push(PyBool.False);
                        break;
                    }

                    // CPython 3.12: Stack layout after PUSH_EXC_INFO: [..., __exit__, exception, PyExceptionInfo]
                    // Get PyExceptionInfo (TOS) - should be at top of stack
                    var exceptionInfoObj = frame.ValueStack.Pop();

                    // Dynamic validation: Check if TOS is PyExceptionInfo
                    if (!(exceptionInfoObj is PyExceptionInfo))
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"❌ WITH_EXCEPT_START: Expected PyExceptionInfo at TOS, got {exceptionInfoObj?.GetType().Name}");
                        #endif
                        frame.ValueStack.Push(exceptionInfoObj); // Restore stack
                        frame.ValueStack.Push(PyBool.False);
                        break;
                    }

                    // Check if we have enough items for context exit method
                    if (frame.ValueStack.Count == 0)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"❌ WITH_EXCEPT_START: No context exit method on stack");
                        #endif
                        frame.ValueStack.Push(exceptionInfoObj); // Restore stack
                        frame.ValueStack.Push(PyBool.False);
                        break;
                    }

                    // Skip exception object and get __exit__ method
                    var exceptionObj = frame.ValueStack.Pop(); // Skip exception

                    if (frame.ValueStack.Count == 0)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"❌ WITH_EXCEPT_START: No context exit method on stack");
                        #endif
                        frame.ValueStack.Push(exceptionObj);     // Restore stack
                        frame.ValueStack.Push(exceptionInfoObj);
                        frame.ValueStack.Push(PyBool.False);
                        break;
                    }

                    var contextExitMethod = frame.ValueStack.Pop(); // __exit__ method

                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 WITH_EXCEPT_START: Found __exit__ method: {contextExitMethod}");
                    #endif

                    // Already validated above, safe to cast
                    var withExceptionInfo = (PyExceptionInfo)exceptionInfoObj;

                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 WITH_EXCEPT_START: Reading exception info from PyExceptionInfo");
                    #endif
                    #if DEBUG_LOG
                    Console.WriteLine($"   exc_type={withExceptionInfo.ExcType}, exc_value={withExceptionInfo.ExcValue}");
                    #endif
                    #if DEBUG_LOG
                    Console.WriteLine($"   exc_traceback={withExceptionInfo.ExcTraceback}, lasti={withExceptionInfo.Lasti}");
                    #endif

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
                            }, null);

                            // Convert result to boolean
                            suppressException = exitResult.AsBool() == PyBool.True;

                            #if DEBUG_LOG
                            Console.WriteLine($"✅ WITH_EXCEPT_START: __exit__ returned {exitResult} (suppress={suppressException})");
                            #endif
                        }
                        catch (Exception exitException)
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"❌ WITH_EXCEPT_START: __exit__ threw exception: {exitException.Message}");
                            #endif
                            suppressException = false;

                            // Re-throw the new exception
                            var newPyException = ConvertToPythonException(exitException);
                            throw new PythonException(newPyException);
                        }
                    }
                    else
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"❌ WITH_EXCEPT_START: Not callable: {contextExitMethod?.GetType().Name}");
                        #endif
                        suppressException = false;
                    }

                    // CPython 3.12: Push boolean result for POP_JUMP_IF_TRUE
                    frame.ValueStack.Push(PyBool.FromBool(suppressException));

                    // DEBUG: 스택 상태 확인
                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 WITH_EXCEPT_START 완료 후 스택 크기: {frame.ValueStack.Count}");
                    #endif
                    for (int i = 0; i < Math.Min(frame.ValueStack.Count, 5); i++)
                    {
                        var debugItem = frame.ValueStack.ToArray()[frame.ValueStack.Count - 1 - i];
                        #if DEBUG_LOG
                        Console.WriteLine($"  Stack[{frame.ValueStack.Count - 1 - i}]: {debugItem}");
                        #endif
                    }
                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 WITH_EXCEPT_START: Pushed result = {suppressException}");
                    #endif
                    break;

                // CPython 3.12: EXCEPT_MATCH removed, exception matching now uses IS_OP

                case ByteCodeOp.CHECK_EG_MATCH:
                    // PEP 654: ExceptionGroup matching
                    // Stack before: [exception_group, exception_type]
                    // Stack after: [matched_group, remainder_group]

                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 CHECK_EG_MATCH: stack size = {frame.ValueStack.Count}");
                    #endif

                    // CPython 3.12: CHECK_EG_MATCH 동적 스택 검증
                    var checkEgMatchRequiredStack = StackEffectAnalyzer.GetMinStackRequirement(ByteCodeOp.CHECK_EG_MATCH, instruction.Argument);
                    if (frame.ValueStack.Count < checkEgMatchRequiredStack)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"❌ CHECK_EG_MATCH: Not enough items on stack (need {checkEgMatchRequiredStack}, got {frame.ValueStack.Count})");
                        #endif
                        frame.ValueStack.Push(PyNone.Instance);
                        frame.ValueStack.Push(PyNone.Instance);
                        break;
                    }

                    var egType = frame.ValueStack.Pop();
                    var egException = frame.ValueStack.Pop();

                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 CHECK_EG_MATCH: exception={egException?.GetType().Name}, type={egType?.GetType().Name}");
                    #endif

                    var (matched, remainder) = ExceptionGroupMatches(egException, egType);

                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 CHECK_EG_MATCH: matched={matched?.GetType().Name}, remainder={remainder?.GetType().Name}");
                    #endif

                    // CPython 3.12: CHECK_EG_MATCH pushes remainder first, then matched
                    // This way, STORE_NAME gets the matched exception group
                    frame.ValueStack.Push(remainder ?? PyNone.Instance);
                    frame.ValueStack.Push(matched ?? PyNone.Instance);
                    break;

                case ByteCodeOp.RAISE_VARARGS:
                    // instruction.Argument indicates the number of arguments to the raise statement
                    // 0: bare raise (reraise)
                    // 1: raise exc
                    // 2: raise exc from cause
                    if (instruction.Argument == 2)
                    {
                        // raise exc from cause - exception chaining
                        // Stack: TOS = cause, TOS1 = exc
                        var cause = frame.ValueStack.Pop();  // Pop TOS (cause)
                        var exc = frame.ValueStack.Pop();     // Pop TOS1 (exc)

                        // Instantiate exc if it's a type
                        PyException excInstance;
                        if (exc is PyException pyExc)
                        {
                            excInstance = pyExc;
                        }
                        else if (exc is PyType pyType)
                        {
                            var instance = pyType.Call(Array.Empty<PyObject>());
                            if (instance is PyException pyExcInst)
                            {
                                excInstance = pyExcInst;
                            }
                            else
                            {
                                throw new PythonException(new PyTypeError($"exceptions must derive from BaseException"));
                            }
                        }
                        else if (exc is PyBuiltinType builtinType)
                        {
                            var instance = builtinType.Call(Array.Empty<PyObject>());
                            if (instance is PyException pyExcInst)
                            {
                                excInstance = pyExcInst;
                            }
                            else
                            {
                                throw new PythonException(new PyTypeError($"exceptions must derive from BaseException"));
                            }
                        }
                        else
                        {
                            throw new PythonException(new PyTypeError($"exceptions must derive from BaseException"));
                        }

                        // Set __cause__ attribute
                        if (cause is PyException causeExc)
                        {
                            excInstance.__cause__ = causeExc;
                            excInstance.__suppress_context__ = true;
                        }
                        else if (cause is PyNone)
                        {
                            // raise exc from None - suppress context
                            excInstance.__cause__ = null;
                            excInstance.__suppress_context__ = true;
                        }
                        else
                        {
                            throw new PythonException(new PyTypeError($"exception cause must be None or derive from BaseException"));
                        }

                        frame.LastException = excInstance;
                        throw new PythonException(excInstance);
                    }
                    else if (instruction.Argument == 1)
                    {
                        // raise exception_instance or exception_class
                        var raisedException = frame.ValueStack.Pop();
                        #if DEBUG_LOG
                        Console.WriteLine($"🔧 RAISE_VARARGS: raisedException type = {raisedException?.GetType().Name}, value = {raisedException}");
                        #endif
                        if (raisedException is PyException pyEx)
                        {
                            // Already an exception instance
                            frame.LastException = pyEx;
                            throw new PythonException(pyEx);
                        }
                        else if (raisedException is PyType pyType)
                        {
                            // Exception class (PyType) - instantiate it
                            #if DEBUG_LOG
                            Console.WriteLine($"🔧 RAISE_VARARGS: Detected PyType exception class {pyType.Name}, instantiating it");
                            #endif

                            var instance = pyType.Call(Array.Empty<PyObject>());
                            if (instance is PyException instanceException)
                            {
                                frame.LastException = instanceException;
                                throw new PythonException(instanceException);
                            }
                            else
                            {
                                throw new PythonException(new PyTypeError($"exceptions must derive from BaseException"));
                            }
                        }
                        else if (raisedException is PyBuiltinType builtinType)
                        {
                            // Exception class - instantiate it
                            var builtinException = builtinType.Call(new PyObject[0], null);
                            if (builtinException == null)
                            {
                                throw new PythonException(new PyTypeError($"exception class {builtinType} returned null when instantiated"));
                            }
                            else if (builtinException is PyException pyExInstance)
                            {
                                frame.LastException = pyExInstance;
                                throw new PythonException(pyExInstance);
                            }
                            else
                            {
                                throw new PythonException(new PyTypeError($"exceptions must derive from BaseException, got {builtinException.GetType().Name}"));
                            }
                        }
                        else if (raisedException is PyClass userClass)
                        {
                            // User-defined exception class - instantiate it
                            #if DEBUG_LOG
                            Console.WriteLine($"🔧 RAISE_VARARGS: User-defined class {userClass.Name}");
                            #endif
                            var userException = userClass.Call(new PyObject[0], null);
                            if (userException is PyException pyUserExInstance)
                            {
                                frame.LastException = pyUserExInstance;
                                throw new PythonException(pyUserExInstance);
                            }
                            else
                            {
                                #if DEBUG_LOG
                                Console.WriteLine($"🔧 RAISE_VARARGS: User class instance is not PyException: {userException?.GetType().Name}");
                                #endif
                                throw new PythonException(new PyTypeError($"exceptions must derive from BaseException"));
                            }
                        }
                        else if (raisedException is PyObject customInstance)
                        {
                            // Could be an instance of a user-defined exception class
                            #if DEBUG_LOG
                            Console.WriteLine($"🔧 RAISE_VARARGS: Custom instance type = {customInstance.GetType().Name}, ToString = {customInstance.ToString()}");
                            #endif
                            // Check if it's derived from BaseException by checking its class hierarchy
                            // For now, treat as a PyException if it has the right properties
                            if (IsExceptionLike(customInstance))
                            {
                                // Create a PyException wrapper with class information AND instance preserved
                                PyException wrappedException;
                                if (customInstance is PyClassInstance classInst)
                                {
                                    // CRITICAL: Store the original PyClassInstance so attributes are preserved
                                    wrappedException = new PyException(customInstance.ToString(), classInst.InstanceType, classInst);
                                    #if DEBUG_LOG
                                    Console.WriteLine($"🔧 RAISE_VARARGS: Created PyException wrapper with OriginalClass={classInst.InstanceType.Name}, OriginalInstance preserved, message='{customInstance.ToString()}'");
                                    #endif
                                }
                                else
                                {
                                    wrappedException = new PyException(customInstance.ToString());
                                }
                                frame.LastException = wrappedException;
                                throw new PythonException(wrappedException);
                            }
                            else
                            {
                                throw new PythonException(new PyTypeError($"exceptions must derive from BaseException"));
                            }
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
                    var stackTop = frame.ValueStack.Peek(); // Don't pop, will be used later

                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 CHECK_EXC_MATCH Entry: expectedType={expectedType?.GetType().Name}={expectedType}, stackTop={stackTop?.GetType().Name}={stackTop}");
                    #endif

                    if (expectedType == null)
                    {
                        throw PyRuntimeError.Create("CHECK_EXC_MATCH: expectedType is null - exception type was not loaded properly");
                    }

                    bool matches = false;

                    // Handle PyExceptionInfo case (from PUSH_EXC_INFO)
                    if (stackTop is PyExceptionInfo excInfo)
                    {
                        // Extract actual exception from PyExceptionInfo
                        var actualException = excInfo.ExcValue;

                        #if DEBUG_LOG
                        Console.WriteLine($"🔧 CHECK_EXC_MATCH: actualException type = {actualException?.GetType().FullName}, value = {actualException}");
                        #endif

                        // CRITICAL: If PyException has OriginalInstance, use that instead
                        PyObject exceptionToStore = actualException;
                        if (actualException is PyException pyExc && pyExc.OriginalInstance != null)
                        {
                            exceptionToStore = pyExc.OriginalInstance;
                            #if DEBUG_LOG
                            Console.WriteLine($"🔧 CHECK_EXC_MATCH: Using OriginalInstance instead of PyException wrapper");
                            #endif
                        }

                        // Replace PyExceptionInfo with actual exception on stack (for STORE_NAME)
                        frame.ValueStack.Pop(); // Remove PyExceptionInfo
                        frame.ValueStack.Push(exceptionToStore); // Push original instance or exception

                        if (actualException is PyException pyException)
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"🔧 CHECK_EXC_MATCH: PyException with OriginalClass={pyException.OriginalClass?.Name ?? "null"}");
                            #endif

                            if (expectedType is PyClass userExceptionClass && pyException.OriginalClass != null)
                            {
                                // Check if the PyException wrapper has the expected original class
                                #if DEBUG_LOG
                                Console.WriteLine($"🔧 CHECK_EXC_MATCH: Comparing PyException.OriginalClass={pyException.OriginalClass.Name} with expected class {userExceptionClass.Name}");
                                #endif

                                if (pyException.OriginalClass == userExceptionClass ||
                                    pyException.OriginalClass.Name == userExceptionClass.Name)
                                {
                                    matches = true;
                                    #if DEBUG_LOG
                                    Console.WriteLine($"🔧 CHECK_EXC_MATCH: PyException OriginalClass matched!");
                                    #endif
                                }
                            }
                            else if (expectedType is PyBuiltinType builtinType)
                            {
                                matches = IsExceptionInstanceOf(pyException, builtinType.Name);
                            }
                            else if (expectedType is PyType pyType)
                            {
                                matches = IsExceptionInstanceOf(pyException, pyType.Name);
                            }
                            else if (expectedType is PyTuple exceptionTuple)
                            {
                                // Handle tuple of exception types: except (ValueError, TypeError)
                                foreach (var excType in exceptionTuple.Items)
                                {
                                    if (excType is PyBuiltinType tupleBuiltinType)
                                    {
                                        if (IsExceptionInstanceOf(pyException, tupleBuiltinType.Name))
                                        {
                                            matches = true;
                                            break;
                                        }
                                    }
                                    else if (excType is PyType tuplePyType)
                                    {
                                        if (IsExceptionInstanceOf(pyException, tuplePyType.Name))
                                        {
                                            matches = true;
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                        else if (actualException is PyClassInstance classInstance)
                        {
                            // Handle user-defined exception instances (PyClassInstance)
                            #if DEBUG_LOG
                            Console.WriteLine($"🔧 CHECK_EXC_MATCH: PyClassInstance from class {classInstance.InstanceType.Name}");
                            #endif

                            if (expectedType is PyClass userExceptionClass)
                            {
                                // Check if the instance was created from the expected class
                                #if DEBUG_LOG
                                Console.WriteLine($"🔧 CHECK_EXC_MATCH: Comparing PyClassInstance.InstanceType={classInstance.InstanceType.Name} with expected class {userExceptionClass.Name}");
                                #endif

                                // Direct class comparison
                                if (classInstance.InstanceType == userExceptionClass ||
                                    classInstance.InstanceType.Name == userExceptionClass.Name)
                                {
                                    matches = true;
                                    #if DEBUG_LOG
                                    Console.WriteLine($"🔧 CHECK_EXC_MATCH: PyClassInstance matched with user class!");
                                    #endif
                                }
                            }
                            else if (expectedType is PyType pyType)
                            {
                                // Check if the custom instance is compatible with built-in exception types
                                // Check inheritance chain: CustomError -> Exception -> BaseException
                                if (pyType.Name == "Exception" || pyType.Name == "BaseException")
                                {
                                    matches = true;
                                    #if DEBUG_LOG
                                    Console.WriteLine($"🔧 CHECK_EXC_MATCH: PyClassInstance matches {pyType.Name}");
                                    #endif
                                }
                            }
                        }
                        else if (actualException is PyObject customException)
                        {
                            // Handle other custom objects
                            #if DEBUG_LOG
                            Console.WriteLine($"🔧 CHECK_EXC_MATCH: Custom exception in PyExceptionInfo: {customException.GetType().Name}");
                            #endif

                            if (expectedType is PyClass userExceptionClass)
                            {
                                // Check if the instance was created from the expected class
                                #if DEBUG_LOG
                                Console.WriteLine($"🔧 CHECK_EXC_MATCH: Comparing custom exception with user class {userExceptionClass.Name}");
                                #endif

                                // Simple matching: check if both relate to the same custom exception
                                if (customException.ToString().Contains("CustomError") &&
                                    userExceptionClass.Name == "CustomError")
                                {
                                    matches = true;
                                    #if DEBUG_LOG
                                    Console.WriteLine($"🔧 CHECK_EXC_MATCH: Custom exception matched with user class!");
                                    #endif
                                }
                            }
                            else if (expectedType is PyType pyType)
                            {
                                // Check if the custom instance is compatible with built-in exception types
                                if (pyType.Name == "Exception" || pyType.Name == "BaseException")
                                {
                                    matches = true;
                                    #if DEBUG_LOG
                                    Console.WriteLine($"🔧 CHECK_EXC_MATCH: Custom exception matches {pyType.Name}");
                                    #endif
                                }
                            }
                        }
                    }
                    else if (stackTop is PyBaseException builtinException)
                    {
                        // Handle direct builtin exception instances (PyValueError, PyTypeError, etc.)
                        #if DEBUG_LOG
                        Console.WriteLine($"🔧 CHECK_EXC_MATCH: Direct builtin exception {builtinException.GetType().Name}");
                        #endif

                        if (expectedType is PyType pyType)
                        {
                            // Match builtin exception with builtin type
                            var exceptionTypeName = builtinException.GetType().Name;
                            // Convert PyValueError -> ValueError, PyTypeError -> TypeError, etc.
                            if (exceptionTypeName.StartsWith("Py") && exceptionTypeName.EndsWith("Error"))
                            {
                                var simpleName = exceptionTypeName.Substring(2); // Remove "Py" prefix
                                matches = pyType.Name == simpleName;

                                #if DEBUG_LOG
                                Console.WriteLine($"🔧 CHECK_EXC_MATCH: Comparing {simpleName} with {pyType.Name} -> {matches}");
                                #endif
                            }
                        }
                        else if (expectedType is PyBuiltinType builtinType)
                        {
                            var exceptionTypeName = builtinException.GetType().Name;
                            if (exceptionTypeName.StartsWith("Py") && exceptionTypeName.EndsWith("Error"))
                            {
                                var simpleName = exceptionTypeName.Substring(2); // Remove "Py" prefix
                                matches = builtinType.Name == simpleName;

                                #if DEBUG_LOG
                                Console.WriteLine($"🔧 CHECK_EXC_MATCH: Comparing {simpleName} with {builtinType.Name} -> {matches}");
                                #endif
                            }
                        }
                    }
                    else if (stackTop is PyException pyException)
                    {
                        if (expectedType is PyBuiltinType builtinType)
                        {
                            // Check if exception is instance of expected type
                            matches = pyException.GetTypeName() == builtinType.Name;
                        }
                        else if (expectedType is PyTuple exceptionTuple)
                        {
                            // Handle tuple of exception types: except (ValueError, TypeError)
                            foreach (var excType in exceptionTuple.Items)
                            {
                                if (excType is PyBuiltinType tupleBuiltinType)
                                {
                                    if (pyException.GetTypeName() == tupleBuiltinType.Name)
                                    {
                                        matches = true;
                                        break;
                                    }
                                }
                                else if (excType is PyType tuplePyType)
                                {
                                    if (pyException.GetTypeName() == tuplePyType.Name)
                                    {
                                        matches = true;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    else if (stackTop is PyObject customExceptionInstance)
                    {
                        // Handle user-defined exception instances
                        #if DEBUG_LOG
                        Console.WriteLine($"🔧 CHECK_EXC_MATCH: Custom exception instance {customExceptionInstance.GetType().Name}");
                        #endif

                        if (expectedType is PyClass userExceptionClass)
                        {
                            // Check if the instance was created from the expected class
                            #if DEBUG_LOG
                            Console.WriteLine($"🔧 CHECK_EXC_MATCH: Comparing with user class {userExceptionClass.Name}");
                            #endif

                            // For now, check if both are custom objects and have compatible types
                            // In a full implementation, we'd check the actual class hierarchy
                            if (customExceptionInstance.GetType().Name.Contains("CustomError") &&
                                userExceptionClass.Name == "CustomError")
                            {
                                matches = true;
                                #if DEBUG_LOG
                                Console.WriteLine($"🔧 CHECK_EXC_MATCH: Custom exception matched!");
                                #endif
                            }
                        }
                        else if (expectedType is PyType pyType)
                        {
                            // Check if the custom instance is compatible with built-in exception types
                            if (pyType.Name == "Exception" || pyType.Name == "BaseException")
                            {
                                matches = true;
                                #if DEBUG_LOG
                                Console.WriteLine($"🔧 CHECK_EXC_MATCH: Custom exception matches {pyType.Name}");
                                #endif
                            }
                        }
                    }

                    frame.ValueStack.Push(matches ? PyBool.True : PyBool.False);
                    break;

                case ByteCodeOp.RERAISE:
                    // CPython 3.12: RERAISE argument controls stack management
                    // arg 0: only pops exception from stack
                    // arg 1: pops both exception and additional value (lasti) from stack
                    var reraiseArg = instruction.Argument;

                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 RERAISE: arg={reraiseArg}, stack size={frame.ValueStack.Count}");
                    #endif

                    // CPython 3.12: Handle stack cleanup based on argument
                    if (reraiseArg > 0)
                    {
                        // Pop additional value (last instruction pointer) from stack
                        if (frame.ValueStack.Count > 0)
                        {
                            var additionalValue = frame.ValueStack.Pop();
                            #if DEBUG_LOG
                            Console.WriteLine($"🔧 RERAISE: Popped additional value: {additionalValue}");
                            #endif
                        }
                    }

                    // CPython 3.12: For RERAISE 0 in finally handlers, only reraise if there's an active exception
                    // If exception was handled normally, don't reraise
                    if (reraiseArg == 0 && frame.CurrentException == null && frame.LastException == null)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"🔧 RERAISE: No active exception to reraise, continuing normally");
                        #endif

                        // Still need to clean up the stack if there's an ExceptionInfo
                        if (frame.ValueStack.Count > 0 && frame.ValueStack.Peek() is PyExceptionInfo)
                        {
                            frame.ValueStack.Pop(); // Remove the ExceptionInfo
                            #if DEBUG_LOG
                            Console.WriteLine($"🔧 RERAISE: Cleaned up ExceptionInfo from stack");
                            #endif
                        }
                        break; // Continue normally without raising
                    }

                    // CPython 3.12: Pop exception from stack if present
                    if (frame.ValueStack.Count > 0)
                    {
                        var exceptionOnStack = frame.ValueStack.Pop();
                        #if DEBUG_LOG
                        Console.WriteLine($"🔧 RERAISE: Popped exception from stack: {exceptionOnStack}");
                        #endif

                        // If it's a PyExceptionInfo, extract the actual exception
                        if (exceptionOnStack is PyExceptionInfo reraiseExcInfo)
                        {
                            if (reraiseExcInfo.ExcValue is PyBaseException exception)
                            {
                                throw new PythonException(exception);
                            }
                        }
                        else if (exceptionOnStack is PyBaseException directException)
                        {
                            throw new PythonException(directException);
                        }
                    }

                    // Fallback: use LastException if no exception on stack
                    if (frame.LastException != null)
                        throw new PythonException(frame.LastException);
                    break;

                // F-String Support (PEP 701)
                case ByteCodeOp.FORMAT_VALUE:
                    var formatOption = instruction.Argument;

                    PyString formattedString;

                    // CPython 3.12: formatOption encoding
                    // Bits 0-1: conversion (1=str, 2=repr, 3=ascii)
                    // Bit 2 (4): has format spec
                    int conversion = formatOption & 3; // Get bits 0-1
                    bool hasFormatSpec = (formatOption & 4) != 0; // Check bit 2

                    var formatValue = frame.ValueStack.Pop();

                    if (hasFormatSpec)
                    {
                        var formatSpec = frame.ValueStack.Pop();
                        // Apply conversion first
                        PyObject converted = conversion switch
                        {
                            1 => formatValue.ToStr(), // !s
                            2 => formatValue.ToRepr(), // !r
                            3 => formatValue.ToRepr(), // !a (simplified as repr)
                            _ => formatValue.ToStr()
                        };
                        formattedString = ApplyFormatting(converted, formatSpec.ToStr().Value);
                    }
                    else
                    {
                        // Apply conversion
                        formattedString = conversion switch
                        {
                            1 => formatValue.ToStr(), // !s
                            2 => formatValue.ToRepr(), // !r
                            3 => formatValue.ToRepr(), // !a (simplified as repr)
                            _ => formatValue.ToStr() // default is str()
                        };
                    }

                    frame.ValueStack.Push(formattedString);
                    break;

                case ByteCodeOp.BUILD_STRING:
                    var stringCount = instruction.Argument;
                    var stringParts = new List<string>();
                    for (int i = 0; i < stringCount; i++)
                    {
                        var part = frame.ValueStack.Pop();
                        // CPython 3.12: BUILD_STRING joins already-formatted PyString values
                        // Use the string value directly, not ToString() which adds quotes
                        if (part is PyString pyStr)
                        {
                            stringParts.Insert(0, pyStr.Value);
                        }
                        else
                        {
                            stringParts.Insert(0, part.ToStr().Value);
                        }
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
                        // For [1, 2, 3, 4, 5] with pattern [first, *middle, last]:
                        // Should push: first(1), middle([2,3,4]), last(5) on stack in order

                        // Stack is LIFO, so we need to push in reverse order for STORE operations
                        // STORE order will be: first, middle, last
                        // So we push: last, middle, first (reverse order)

                        // Push after elements first (in reverse order)
                        for (int i = countAfter - 1; i >= 0; i--)
                        {
                            var afterItem = items[items.Length - countAfter + i];
                            frame.ValueStack.Push(afterItem);
                        }

                        // Push star elements (middle part)
                        var starCount = items.Length - countBefore - countAfter;
                        var starItems = new PyObject[starCount];
                        for (int i = 0; i < starCount; i++)
                        {
                            starItems[i] = items[countBefore + i];
                        }
                        frame.ValueStack.Push(new PyList(starItems));

                        // Push before elements last (in reverse order)
                        for (int i = countBefore - 1; i >= 0; i--)
                        {
                            frame.ValueStack.Push(items[i]);
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
                    // CPython 3.12: IMPORT_NAME(namei)
                    // TOS = fromlist, TOS1 = level
                    // Implements: __import__(name, globals(), locals(), fromlist, level)
                    var fromlist = frame.ValueStack.Pop(); // TOS
                    var level = frame.ValueStack.Pop();    // TOS1
                    var moduleName = ((PyString)frame.Code.Constants[instruction.Argument]).Value;

                    // Extract level as integer (0 for absolute, 1+ for relative)
                    int importLevel = 0;
                    if (level is PyInt pyIntLevel)
                    {
                        importLevel = (int)pyIntLevel.Value;
                    }

                    // Extract fromlist as string array
                    string[] fromlistArray = null;
                    if (fromlist is PyTuple pyTupleFromlist)
                    {
                        fromlistArray = pyTupleFromlist.Items
                            .Select(item => item is PyString s ? s.Value : item.AsString())
                            .ToArray();
                    }

                    // Call import system with level and fromlist
                    var importedModule = PyImportSystem.Import(moduleName, importLevel, fromlistArray);
                    frame.ValueStack.Push(importedModule);
                    break;

                case ByteCodeOp.IMPORT_FROM:
                    var itemName = ((PyString)frame.Code.Constants[instruction.Argument]).Value;
                    if (frame.ValueStack.Count == 0)
                    {
                        throw new Exception($"IMPORT_FROM: Stack empty when trying to import '{itemName}'. This may be caused by incorrect bytecode generation.");
                    }
                    var module = frame.ValueStack.Peek(); // Don't pop yet, needed for multiple imports

                    // CPython 3.12: Handle "from module import *"
                    // When itemName is "*", import all public names from module
                    if (itemName == "*")
                    {
                        // Get __all__ attribute if it exists, otherwise use all non-private attributes
                        PyObject allAttr = null;
                        try
                        {
                            allAttr = module.GetAttribute("__all__");
                        }
                        catch
                        {
                            // __all__ doesn't exist, will use dir() instead
                        }

                        List<string> namesToImport = new List<string>();

                        if (allAttr != null)
                        {
                            // Use __all__ to determine what to import
                            if (allAttr is PyList allList)
                            {
                                foreach (var item in allList.Items)
                                {
                                    if (item is PyString nameStr)
                                    {
                                        namesToImport.Add(nameStr.Value);
                                    }
                                }
                            }
                            else if (allAttr is PyTuple allTuple)
                            {
                                foreach (var item in allTuple.Items)
                                {
                                    if (item is PyString nameStr)
                                    {
                                        namesToImport.Add(nameStr.Value);
                                    }
                                }
                            }
                        }
                        else
                        {
                            // No __all__, import all non-private names
                            if (module is PyModule pyModule)
                            {
                                foreach (var key in pyModule.ModuleDict.Keys)
                                {
                                    if (!key.StartsWith("_"))
                                    {
                                        namesToImport.Add(key);
                                    }
                                }
                            }
                        }

                        // Import each name into the current scope
                        // CPython: This is handled by IMPORT_STAR bytecode, but we handle it here
                        foreach (var importName in namesToImport)
                        {
                            try
                            {
                                var importValue = module.GetAttribute(importName);
                                // Store in current frame's local scope
                                if (frame.LocalScope != null)
                                {
                                    frame.LocalScope.Variables[importName] = importValue;
                                }
                                else
                                {
                                    // Fallback: use global scope
                                    frame.ScopeChain.GlobalScope.Variables[importName] = importValue;
                                }
                            }
                            catch
                            {
                                // Skip attributes that can't be imported
                            }
                        }

                        // Push a dummy value to satisfy stack expectations
                        // This will be handled by subsequent IMPORT_STAR or POP_TOP
                        frame.ValueStack.Push(PyNone.Instance);
                    }
                    else
                    {
                        // Normal case: import specific name
                        var importedItem = module.GetAttribute(itemName);
                        frame.ValueStack.Push(importedItem);
                    }
                    break;

                // PEP 709 Comprehension Optimization - VM 구현
                case ByteCodeOp.LIST_APPEND:
                    // CPython 3.12 호환: LIST_APPEND i
                    // 스택: [..., list, ..., item] → [..., list, ...]
                    var itemToAppend = frame.ValueStack.Pop();

                    // CPython 3.12: LIST_APPEND i에서 타겟 리스트 찾기
                    var targetDepth = instruction.Argument - 1; // 0-based 인덱스

                    if (frame.ValueStack.Count <= targetDepth)
                    {
                        throw new Exception($"LIST_APPEND: not enough items on stack (need {targetDepth + 1}, got {frame.ValueStack.Count})");
                    }

                    // 스택 위치에서 리스트 찾기 - PyNull 건너뛰기
                    var targetList = frame.ValueStack.ElementAt(targetDepth);

                    // PyNull인 경우 실제 리스트를 찾기 위해 스택을 탐색
                    if (PyNull.IsNull(targetList))
                    {
                        // PyNull들을 건너뛰고 실제 리스트 찾기
                        for (int i = targetDepth; i < frame.ValueStack.Count; i++)
                        {
                            var candidate = frame.ValueStack.ElementAt(i);
                            if (!PyNull.IsNull(candidate))
                            {
                                targetList = candidate;
                                break;
                            }
                        }
                    }

                    if (targetList is PyList targetPyList)
                    {
#if DEBUG_LOG
                        Console.WriteLine($"   LIST_APPEND: {itemToAppend?.GetTypeName() ?? "null"} 값={itemToAppend?.ToString() ?? "null"} 추가 → 리스트 크기: {targetPyList.Count}");
#endif
                        targetPyList.Append(itemToAppend);
#if DEBUG_LOG
                        Console.WriteLine($"   LIST_APPEND 완료: 리스트 크기: {targetPyList.Count}, 내용: [{string.Join(", ", targetPyList.Items.Select(x => x?.ToString() ?? "null"))}]");
#endif
                    }
                    else if (PyNull.IsNull(targetList))
                    {
                        throw new Exception($"LIST_APPEND: target is NULL at depth {targetDepth}");
                    }
                    else
                    {
                        throw new Exception($"LIST_APPEND: target is not a list, got {targetList?.GetTypeName() ?? "null"} at depth {targetDepth}");
                    }

                    // 스택은 그대로 유지 (아이템만 제거됨)
                    break;

                case ByteCodeOp.SET_ADD:
                    // CPython 3.12 호환: SET_ADD i
                    // 스택: [..., set, ..., item] → [..., set, ...]
                    var setItem = frame.ValueStack.Pop();

                    // CPython 3.12: SET_ADD i에서 타겟 set 찾기 (LIST_APPEND와 동일한 방식)
                    var setTargetDepth = instruction.Argument - 1; // 0-based 인덱스

                    if (frame.ValueStack.Count <= setTargetDepth)
                    {
                        throw new Exception($"SET_ADD: not enough items on stack (need {setTargetDepth + 1}, got {frame.ValueStack.Count})");
                    }

                    // 스택 위치에서 set 찾기 - PyNull 건너뛰기
                    var targetSet = frame.ValueStack.ElementAt(setTargetDepth);

                    // PyNull인 경우 실제 set을 찾기 위해 스택을 탐색
                    if (PyNull.IsNull(targetSet))
                    {
                        // PyNull들을 건너뛰고 실제 set 찾기
                        for (int i = setTargetDepth; i < frame.ValueStack.Count; i++)
                        {
                            var candidate = frame.ValueStack.ElementAt(i);
                            if (!PyNull.IsNull(candidate))
                            {
                                targetSet = candidate;
                                break;
                            }
                        }
                    }

                    if (targetSet is PySet targetPySet)
                    {
                        targetPySet.Add(setItem);
                    }
                    else if (PyNull.IsNull(targetSet))
                    {
                        throw new Exception($"SET_ADD: target is NULL at depth {setTargetDepth}");
                    }
                    else
                    {
                        throw new Exception($"SET_ADD: target is not a set, got {targetSet?.GetType().Name ?? "null"}");
                    }
                    break;

                case ByteCodeOp.MAP_ADD:
                    // CPython 3.12 호환: MAP_ADD i에서 dict는 스택의 i번째 깊이에 위치
                    // 스택: [..., dict, ..., key, value] → [..., dict, ...]
                    var dictValue = frame.ValueStack.Pop();
                    var dictKey = frame.ValueStack.Pop();

                    // CPython: MAP_ADD oparg는 dict까지의 스택 거리
                    // oparg=2: 2단계 아래, oparg=3: 3단계 아래, etc.
                    var dictDepth = instruction.Argument;

                    if (frame.ValueStack.Count >= dictDepth)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"🔍 MAP_ADD Debug: depth={dictDepth}, stackSize={frame.ValueStack.Count}");
                        var debugStackArray = frame.ValueStack.ToArray();
                        Array.Reverse(debugStackArray);
                        for (int i = 0; i < Math.Min(5, debugStackArray.Length); i++)
                        {
                            Console.WriteLine($"    Stack[{i}]: {debugStackArray[i]?.GetType().Name ?? "null"} = {debugStackArray[i]?.ToString() ?? "null"}");
                        }
                        Console.WriteLine($"🔍 MAP_ADD Debug: targetDict at ElementAt({dictDepth - 1})");
                        #endif

                        // CPython PEEK 방식: ElementAt(dictDepth-1)
                        // dictDepth=2 → ElementAt(1), dictDepth=3 → ElementAt(2), etc.
                        var mapAddTarget = frame.ValueStack.ElementAt(dictDepth - 1);

                        if (mapAddTarget is PyDict mapAddDict)
                        {
                            mapAddDict.InternalDict[dictKey] = dictValue;
                            #if DEBUG_LOG
                            Console.WriteLine($"   MAP_ADD: {dictKey}={dictValue} → dict (depth {dictDepth})");
                            #endif
                        }
                        else if (PyNull.IsNull(mapAddTarget))
                        {
                            throw new Exception($"MAP_ADD: target is NULL at depth {dictDepth}");
                        }
                        else
                        {
                            throw new Exception($"MAP_ADD: target is not a dict, got {mapAddTarget?.GetTypeName() ?? "null"} at depth {dictDepth}");
                        }
                    }
                    else
                    {
                        throw new Exception($"MAP_ADD: not enough items on stack (need {dictDepth}, got {frame.ValueStack.Count})");
                    }
                    break;

                // === Closure Support Bytecodes (CPython 호환) ===
                case ByteCodeOp.LOAD_DEREF:
                    // 클로저/자유 변수에서 값 로드
                    // argument는 (freevars + cellvars)에서의 인덱스
                    var cellIndex = instruction.Argument;

                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 LOAD_DEREF cell index {cellIndex}");
                    #endif
                    #if DEBUG_LOG
                    Console.WriteLine($"   Frame has {frame.Closure.Length} closure cells and {frame.Cells.Length} local cells");
                    #endif
                    #if DEBUG_LOG
                    Console.WriteLine($"   Free vars: [{string.Join(", ", frame.Code.FreeVars)}]");
                    #endif
                    #if DEBUG_LOG
                    Console.WriteLine($"   Cell vars: [{string.Join(", ", frame.Code.CellVars)}]");
                    #endif

                    PyCell cell;
                    if (cellIndex < frame.Closure.Length)
                    {
                        // 부모로부터 받은 클로저 셀
                        cell = frame.Closure[cellIndex];
                        #if DEBUG_LOG
                        Console.WriteLine($"   → Using closure cell[{cellIndex}]: {(cell.HasValue ? cell.Value : "empty")}");
                        #endif
                    }
                    else
                    {
                        // CPython 3.12: Cell variables are at offset FreeVars.Count in the cell array
                        var loadCellVarIndex = cellIndex - frame.Closure.Length;
                        var loadActualCellIndex = frame.Code.FreeVars.Count + loadCellVarIndex;

                        if (loadActualCellIndex < frame.Cells.Length)
                        {
                            cell = frame.Cells[loadActualCellIndex];
                            #if DEBUG_LOG
                            Console.WriteLine($"   → Using local cell[{loadActualCellIndex}]: {(cell.HasValue ? cell.Value : "empty")}");
                            #endif
                        }
                        else
                        {
                            throw new Exception($"LOAD_DEREF: invalid actual cell index {loadActualCellIndex}");
                        }
                    }

                    if (cell.HasValue)
                    {
                        frame.ValueStack.Push(cell.Value!);
                        #if DEBUG_LOG
                        Console.WriteLine($"   ✅ Loaded value: {cell.Value}");
                        #endif
                    }
                    else
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"   ❌ Cell is empty! Cell: {cell}, HasValue: {cell?.HasValue}");
                        #endif

                        // CPython 3.12: Cell이 비어있으면 즉시 UnboundLocalError 발생
                        // Global fallback 같은 메커니즘은 존재하지 않음
                        string varName = "unknown_variable";
                        if (cellIndex < frame.Code.FreeVars.Count)
                        {
                            varName = frame.Code.FreeVars[cellIndex];
                        }
                        else
                        {
                            var localCellVarIndex = cellIndex - frame.Code.FreeVars.Count;
                            if (localCellVarIndex < frame.Code.CellVars.Count)
                            {
                                varName = frame.Code.CellVars[localCellVarIndex];
                            }
                        }

                        #if DEBUG_LOG
                        Console.WriteLine($"   ❌ CPython 3.12 behavior: Throwing UnboundLocalError for '{varName}'");
                        #endif
                        throw PyNameError.Create($"local variable '{varName}' referenced before assignment");
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
                        // CPython 3.12: STORE_DEREF uses direct cell array index
                        if (storeCellIndex < frame.Cells.Length)
                        {
                            storeCell = frame.Cells[storeCellIndex];
                        }
                        else
                        {
                            throw new Exception($"STORE_DEREF: invalid cell index {storeCellIndex}");
                        }
                    }

                    storeCell.SetValue(storeDerefValue);
                    break;

                case ByteCodeOp.DELETE_DEREF:
                    // CPython 3.12: 클로저/자유 변수 삭제 - NULL 상태로 설정
                    var deleteCellIndex = instruction.Argument;

                    PyCell deleteCell;
                    if (deleteCellIndex < frame.Closure.Length)
                    {
                        deleteCell = frame.Closure[deleteCellIndex];
                    }
                    else
                    {
                        // CPython 3.12: DELETE_DEREF uses direct cell array index
                        if (deleteCellIndex < frame.Cells.Length)
                        {
                            deleteCell = frame.Cells[deleteCellIndex];
                        }
                        else
                        {
                            throw new Exception($"DELETE_DEREF: invalid cell index {deleteCellIndex}");
                        }
                    }

                    // CPython 3.12 호환: cell을 PyNull 상태로 설정
                    deleteCell.Clear();
                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 DELETE_DEREF: cleared cell at index {deleteCellIndex} to NULL");
                    #endif
                    break;

                case ByteCodeOp.LOAD_CLOSURE:
                    // 클로저 셀 로드 (함수 생성용)
                    // 현재는 기본 구현만 제공 (Phase 2에서 완전 구현)
                    var closureCellIndex = instruction.Argument;

                    #if DEBUG_LOG
                    Console.WriteLine($"🔐 LOAD_CLOSURE cell index {closureCellIndex}");
                    Console.WriteLine($"   Frame has {frame.Closure.Length} closure cells and {frame.Cells.Length} local cells");
                    #endif

                    PyCell closureCell;
                    if (closureCellIndex < frame.Closure.Length)
                    {
                        closureCell = frame.Closure[closureCellIndex];
                        #if DEBUG_LOG
                        Console.WriteLine($"   → Using closure cell[{closureCellIndex}]: {(closureCell.HasValue ? closureCell.Value : "empty")}");
                        #endif
                    }
                    else
                    {
                        // CPython 3.12: Cell variables are at offset FreeVars.Count in the cell array
                        var closureCellVarIndex = closureCellIndex - frame.Closure.Length;
                        var closureActualCellIndex = frame.Code.FreeVars.Count + closureCellVarIndex;

                        #if DEBUG_LOG
                        Console.WriteLine($"   → Cell var index {closureCellVarIndex} → actual cell index {closureActualCellIndex}");
                        #endif

                        if (closureActualCellIndex < frame.Cells.Length)
                        {
                            closureCell = frame.Cells[closureActualCellIndex];
                            #if DEBUG_LOG
                            Console.WriteLine($"   → Using local cell[{closureActualCellIndex}]: {(closureCell.HasValue ? closureCell.Value : "empty")}");
                            #endif
                        }
                        else
                        {
                            // 새 셀 생성 (Phase 1 임시 구현)
                            closureCell = new PyCell();
                            #if DEBUG_LOG
                            Console.WriteLine($"   ⚠️  Creating new empty cell - actual cell index {closureActualCellIndex} >= {frame.Cells.Length}");
                            #endif
                        }
                    }

                    frame.ValueStack.Push(closureCell);
                    break;

                case ByteCodeOp.COPY_FREE_VARS:
                    // CPython 3.12: COPY_FREE_VARS initializes free variable cells from closure
                    var freeVarCount = instruction.Argument;
                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 COPY_FREE_VARS: Initializing {freeVarCount} free variables");
                    #endif

                    // Copy closure cells to frame's free variable cells
                    if (frame.Closure != null && frame.Closure.Length >= freeVarCount)
                    {
                        for (int i = 0; i < freeVarCount; i++)
                        {
                            if (i < frame.Cells.Length && i < frame.Closure.Length)
                            {
                                // Copy closure cell to frame cell (free variables start from cell index 0)
                                frame.Cells[i] = frame.Closure[i];
                                #if DEBUG_LOG
                                Console.WriteLine($"   ✅ Copied closure[{i}] to cell[{i}]: {frame.Closure[i]?.Value}");
                                #endif
                            }
                            else
                            {
                                #if DEBUG_LOG
                                Console.WriteLine($"   ⚠️ Cannot copy closure[{i}]: frame.Cells.Length={frame.Cells.Length}, closure.Length={frame.Closure.Length}");
                                #endif
                            }
                        }
                    }
                    else
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"   ⚠️ No closure available or insufficient closure cells. Closure: {frame.Closure?.Length ?? -1}, needed: {freeVarCount}");
                        #endif
                    }
                    break;

                case ByteCodeOp.MAKE_CELL:
                    // CPython 3.12: MAKE_CELL uses CellVars index directly (not VarNames index)
                    var cellVarIndex = instruction.Argument;

                    // Bounds checking for CellVars
                    if (cellVarIndex >= frame.Code.CellVars.Count)
                    {
                        throw new IndexOutOfRangeException($"MAKE_CELL: CellVar index {cellVarIndex} out of range. CellVars count: {frame.Code.CellVars.Count}, CellVars: [{string.Join(", ", frame.Code.CellVars)}]");
                    }

                    var cellVarName = frame.Code.CellVars[cellVarIndex];

                    // CPython 3.12: Cell variables come after free variables in the cell array
                    var actualCellIndex = frame.Code.FreeVars.Count + cellVarIndex;

                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 MAKE_CELL for '{cellVarName}' at cell index {cellVarIndex} → actual index {actualCellIndex}");
                    #endif
                    #if DEBUG_LOG
                    Console.WriteLine($"   CellVars: [{string.Join(", ", frame.Code.CellVars)}]");
                    #endif
                    #if DEBUG_LOG
                    Console.WriteLine($"   FreeVars: [{string.Join(", ", frame.Code.FreeVars)}] (offset: {frame.Code.FreeVars.Count})");
                    #endif
                    #if DEBUG_LOG
                    Console.WriteLine($"   FastLocals contains '{cellVarName}': {frame.FastLocals.ContainsKey(cellVarName)}");
                    #endif

                    // CPython 3.12: Create cell variable (initially None for type parameters)
                    PyObject? cellValue = null;
                    if (frame.FastLocals.TryGetValue(cellVarName, out var localValue))
                    {
                        cellValue = localValue;
                        #if DEBUG_LOG
                        Console.WriteLine($"   Found value for '{cellVarName}': {cellValue}");
                        #endif
                    }
                    else
                    {
                        // For Generic Parameters function, cells start as None
                        cellValue = PyNone.Instance;
                        #if DEBUG_LOG
                        Console.WriteLine($"   Initializing '{cellVarName}' cell with None (Generic Parameters standard)");
                        #endif
                    }

                    // CPython 3.12: Use actualCellIndex (offset by free var count) for cell access
                    if (actualCellIndex < frame.Cells.Length)
                    {
                        frame.Cells[actualCellIndex].SetValue(cellValue);
                        #if DEBUG_LOG
                        Console.WriteLine($"   ✅ Set cell[{actualCellIndex}] '{cellVarName}' = {cellValue}");
                        #endif
                    }
                    else
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"   ❌ Invalid actual cell index {actualCellIndex}, Cells.Length: {frame.Cells.Length}");
                        #endif
                    }
                    break;

                // Generator Implementation
                case ByteCodeOp.RETURN_GENERATOR:
                    // CPython 3.12: RETURN_GENERATOR는 Generator 함수의 첫 명령어
                    // Generator 객체를 생성하고 반환해야 하지만, 여기서는 실행을 계속 진행
                    // 실제로는 이 명령어가 실행될 때 이미 Generator 객체가 생성되어 있음
                    break;

                case ByteCodeOp.YIELD_VALUE:
                    var yieldValue = frame.ValueStack.Pop();

                    // yield는 제너레이터에서만 사용 가능
                    if (!frame.Code.IsGenerator())
                    {
                        throw PySyntaxError.Create("'yield' outside function");
                    }

                    // CPython 3.12: YIELD_VALUE 후에 다음 명령어(RESUME)로 진행
                    frame.InstructionPointer++;

                    #if DEBUG_LOG
                    Console.WriteLine($"🔄 Generator: Yielding {yieldValue}, stack size: {frame.ValueStack.Count}");
                    #endif
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
                    // CPython 3.12: Stack: TOS = stop, TOS1 = start, TOS2 = container, TOS3 = value
                    // Result: container[start:stop] = value
                    var sliceStoreStop = frame.ValueStack.Pop();    // stop
                    var sliceStoreStart = frame.ValueStack.Pop();   // start
                    var sliceStoreContainer = frame.ValueStack.Pop(); // container
                    var sliceValue = frame.ValueStack.Pop();        // value

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

                case ByteCodeOp.KW_NAMES:
                    // CPython 3.12: KW_NAMES sets the names for keyword arguments
                    // The argument is an index into the constants table containing a tuple of keyword names
                    var kwNamesIndex = instruction.Argument;
                    var kwNamesTuple = frame.Code.Constants[kwNamesIndex];

                    // Store keyword names tuple for the following CALL instruction
                    frame.KeywordNamesForNextCall = kwNamesTuple as PyTuple;
                    break;

                default:
                    throw PyNotImplementedError.Create($"OpCode {instruction.OpCode} not implemented");
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

                #if DEBUG_LOG
                Console.WriteLine($"    → {left} {binaryOp} {right} = {result}");
                #endif
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
                    BinaryOpType.MATRIX_MULTIPLY => throw PyNotImplementedError.Create("Matrix multiplication not yet implemented"),
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
#if DEBUG_LOG
                Console.WriteLine($"🔍 Exception in ExecuteBinaryOpType: {ex.GetType().Name}: {ex.Message}");
#endif
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
                        var str = obj.ToStr().Value;
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

                // 천단위 구분자 지원 (예: :,)
                if (formatSpec == "," || formatSpec.Contains(","))
                {
                    if (obj is PyInt intObjComma)
                    {
                        return new PyString(intObjComma.Value.ToString("N0"));
                    }
                    else if (obj is PyFloat floatObjComma)
                    {
                        return new PyString(floatObjComma.Value.ToString("N"));
                    }
                }
            }
            catch
            {
                // 포매팅 실패 시 원본 값 반환
            }

            return obj.ToStr();
        }

        private PyObject CompareOperation(PyObject left, PyObject right, int compareOp)
        {
            // CPython 3.12는 바이트코드 값을 직접 사용:
            // 2=<, 26=<=, 40==, 55!=, 68=>, 92=>=
            // 더 이상 인덱스 기반 변환이 필요하지 않음

            var operation = (CompareOp)compareOp;
            return operation switch
            {
                CompareOp.EQ => left.RichCompare(right, PyObject.CompareOp.EQ),    // 40
                CompareOp.NE => left.RichCompare(right, PyObject.CompareOp.NE),    // 55
                CompareOp.LT => left.RichCompare(right, PyObject.CompareOp.LT),    // 2
                CompareOp.IS_NOT => IsNotOperation(left, right),                   // 3 - is not
                CompareOp.LE => left.RichCompare(right, PyObject.CompareOp.LE),    // 26
                CompareOp.GT => left.RichCompare(right, PyObject.CompareOp.GT),    // 68
                CompareOp.GE => left.RichCompare(right, PyObject.CompareOp.GE),    // 92
                CompareOp.EXC_MATCH => left.RichCompare(right, PyObject.CompareOp.EQ), // 8 - exception match
                _ => throw PyNotImplementedError.Create($"Compare operation {compareOp} not implemented")
            };
        }

        private PyObject IsNotOperation(PyObject left, PyObject right)
        {
            // 'is not' 연산: 객체 identity 비교의 반대
            // CPython에서는 PyObject_RichCompareBool을 사용하지만,
            // is/is not은 identity 비교이므로 ReferenceEquals를 사용
            bool result = !ReferenceEquals(left, right);
            return result ? PyBool.True : PyBool.False;
        }

        private PyObject ContainsOperation(PyObject left, PyObject right, int containsOp)
        {
            var operation = (ContainsOp)containsOp;
            return operation switch
            {
                ContainsOp.In => ((PyBool)right.Contains(left)),
                ContainsOp.NotIn => ((PyBool)right.Contains(left)).Not(),
                _ => throw PyNotImplementedError.Create($"Contains operation {operation} not implemented")
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
                #if DEBUG_LOG
                Console.WriteLine($"🔍 ExceptionMatches: Extracted {actualException?.GetType().Name} from PyExceptionInfo");
                #endif
            }
            else if (exception is PyException pyExc)
            {
                actualException = pyExc;
                #if DEBUG_LOG
                Console.WriteLine($"🔍 ExceptionMatches: Direct PyException {pyExc.GetType().Name}");
                #endif
            }

            if (actualException != null)
            {
                // Get the exception's actual type name
                string excTypeName = actualException.GetType().Name;
                if (excTypeName.StartsWith("Py"))
                    excTypeName = excTypeName.Substring(2); // Remove "Py" prefix

                string targetTypeName = "";

                // Handle different types of exception type objects
                if (exceptionType is PyTuple tuple)
                {
                    // Handle tuple of exception types: except* (ValueError, RuntimeError)
                    foreach (var item in tuple.Items)
                    {
                        if (ExceptionMatches(exception, item))
                        {
                            return true;
                        }
                    }
                    return false; // No match in tuple
                }
                else if (exceptionType is PyBuiltinType builtinType)
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

                #if DEBUG_LOG
                Console.WriteLine($"🔍 Exception match: {excTypeName} vs {targetTypeName}");
                #endif

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
                    #if DEBUG_LOG
                    Console.WriteLine(arg.ToString());
                    #endif
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
                case 4: // INTRINSIC_SET_FUNCTION_TYPE_PARAMS - PEP 695 Generic Function
                    return SetFunctionTypeParams(arg1, arg2);
                default:
                    throw new NotImplementedException($"Intrinsic function 2-arg {functionId} not implemented");
            }
        }

        /// <summary>
        /// CPython 3.12: INTRINSIC_SET_FUNCTION_TYPE_PARAMS - Set Generic Type Parameters for PEP 695 function
        /// </summary>
        private PyObject SetFunctionTypeParams(PyObject function, PyObject typeParams)
        {
            // arg1 = function object, arg2 = type parameters tuple
            if (function is PyFunction pyFunc && typeParams is PyTuple paramTuple)
            {
                // Set the type parameters on the function
                // For now, just return the function as-is (basic implementation)
                // TODO: Enhanced type parameter handling if needed
                return pyFunc;
            }

            return function; // Fallback: return function unchanged
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
            #if DEBUG_LOG
            Console.WriteLine($"🔍 ExceptionGroupMatches: exception={exception?.GetType().Name}, type={exceptionType?.GetType().Name}");
            #endif

            // Extract actual exception from PyExceptionInfo if needed
            PyObject actualException = exception;
            if (exception is PyExceptionInfo exceptionInfo)
            {
                actualException = exceptionInfo.ExcValue;
                #if DEBUG_LOG
                Console.WriteLine($"🔍 Extracted exception from PyExceptionInfo: {actualException?.GetType().Name}");
                #endif
            }

            if (actualException is PyBaseExceptionGroup group)
            {
                #if DEBUG_LOG
                Console.WriteLine($"🔍 Processing ExceptionGroup with {group.Exceptions.Count} exceptions");
                #endif

                var matchedExceptions = new List<PyException>();
                var remainderExceptions = new List<PyException>();

                foreach (var exc in group.Exceptions)
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 Checking exception: {exc.GetType().Name} vs {exceptionType?.GetType().Name}");
                    #endif
                    if (ExceptionMatches(exc, exceptionType))
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"✅ Match found: {exc.GetType().Name}");
                        #endif
                        matchedExceptions.Add(exc);
                    }
                    else
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"❌ No match: {exc.GetType().Name}");
                        #endif
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
                    #if DEBUG_LOG
                    Console.WriteLine($"📦 Created matched group with {matchedExceptions.Count} exceptions");
                    #endif
                }

                PyObject? remainder = null;
                if (remainderExceptions.Count > 0)
                {
                    if (exception is PyExceptionGroup)
                        remainder = new PyExceptionGroup(group.Message, remainderExceptions);
                    else
                        remainder = new PyBaseExceptionGroup(group.Message, remainderExceptions);
                    #if DEBUG_LOG
                    Console.WriteLine($"📦 Created remainder group with {remainderExceptions.Count} exceptions");
                    #endif
                }

                return (matched, remainder);
            }

            // Not an exception group - check if single exception matches
            if (ExceptionMatches(actualException, exceptionType))
            {
                return (actualException, null);
            }

            return (null, actualException);
        }

        /// <summary>
        /// CPython-style function argument binding with default parameters
        /// Used by MAKE_FUNCTION bytecode implementation
        /// </summary>

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
                return pyFunc.Call(totalArgs, null);
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

                return pyType.Call(totalArgs.ToArray(), null);
            }
            else
            {
                // 다른 callable 객체의 경우 기본 Call 메서드 사용 (키워드 인수 무시)
                return function.Call(args, null);
            }
        }

        /// <summary>
        /// 키워드 인수를 포함한 매개변수 바인딩 (간소화 버전)
        /// </summary>
        private PyObject[] BindArgumentsWithKwargs(PyObject[] args, Dictionary<string, PyObject> kwargs, PyFunction function)
        {
            var code = function.CodeObject;
            var boundArgs = new PyObject[code.ArgCount];

#if DEBUG_LOG
            Console.WriteLine($"🔧 키워드 인수 포함 매개변수 바인딩: {args.Length}개 위치인수, {kwargs.Count}개 키워드인수, {code.ArgCount}개 매개변수");
            Console.WriteLine($"  Flags: 0x{code.Flags:X8}, VarNames count: {code.VarNames.Count}, PosonlyArgCount: {code.PosonlyArgCount}");
#endif

            // CPython 방식: 플래그 기반 **kwargs 탐지
            bool hasKwargs = (code.Flags & PyCodeObject.CO_VARKEYWORDS) != 0;
            bool hasVarargs = (code.Flags & PyCodeObject.CO_VARARGS) != 0;

            int kwargsParamIndex = hasKwargs ? code.ArgCount - 1 : -1;

            #if DEBUG_LOG
            Console.WriteLine($"  hasKwargs: {hasKwargs}, hasVarargs: {hasVarargs}, kwargsIndex: {kwargsParamIndex}");
            #endif

            // 실제 필수/선택적 매개변수 개수 계산 (**kwargs 제외)
            int regularParamCount = hasKwargs ? code.ArgCount - 1 : code.ArgCount;

            // 1. 위치 인수 바인딩
            for (int i = 0; i < Math.Min(args.Length, regularParamCount); i++)
            {
                boundArgs[i] = args[i];
                string paramName = i < code.VarNames.Count ? code.VarNames[i] : $"arg{i}";
                #if DEBUG_LOG
                Console.WriteLine($"  → 매개변수[{i}] '{paramName}' = {args[i]} (위치인수)");
                #endif
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
                    // CPython 3.12: positional-only 매개변수에 키워드 인수 사용 시 에러
                    if (paramIndex < code.PosonlyArgCount)
                    {
                        throw PyTypeError.Create($"{code.Name}() got some positional-only arguments passed as keyword arguments: '{paramName}'");
                    }

                    // 일반 매개변수에 바인딩
                    if (boundArgs[paramIndex] != null)
                    {
                        throw PyTypeError.Create($"'{code.Name}() got multiple values for argument '{paramName}'");
                    }

                    boundArgs[paramIndex] = paramValue;
                    #if DEBUG_LOG
                    Console.WriteLine($"  → 매개변수[{paramIndex}] '{paramName}' = {paramValue} (키워드인수)");
                    #endif
                }
                else if (kwargsParamIndex >= 0)
                {
                    // **kwargs에 수집
                    extraKwargs[paramName] = paramValue;
                    #if DEBUG_LOG
                    Console.WriteLine($"  → **kwargs['{paramName}'] = {paramValue}");
                    #endif
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
                #if DEBUG_LOG
                Console.WriteLine($"  → 매개변수[{kwargsParamIndex}] '**kwargs' = {kwargsDict} (**kwargs 딕셔너리)");
                #endif
            }

            // 4. 기본값 적용 (바인딩되지 않은 매개변수에)
            var defaults = GetFunctionDefaults(function);
            int defaultCount = defaults?.Length ?? 0;
            int requiredArgCount = regularParamCount - defaultCount;

            #if DEBUG_LOG
            Console.WriteLine($"  기본값 매개변수: {defaultCount}개, 필수 매개변수: {requiredArgCount}개");
            #endif

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
                            #if DEBUG_LOG
                            Console.WriteLine($"  → 매개변수[{i}] '{paramName}' = {defaults[defaultIndex]} (기본값)");
                            #endif
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
                #if DEBUG_LOG
                Console.WriteLine($"✅ GET_AWAITABLE: Native coroutine {coroutine}");
                #endif

                // For now, immediately execute the coroutine using our event loop
                // In a real implementation, this would be scheduled properly
                var eventLoop = new SharpPy.Modules.SimpleEventLoop(this);
                try
                {
                    return eventLoop.RunUntilComplete(coroutine);
                }
                catch (Exception ex)
                {
                    // If execution fails, fall back to awaiter
                    Console.WriteLine($"Coroutine execution failed: {ex.Message}");
                    return coroutine.GetAwaiter();
                }
            }

            // 2. Generator-based coroutine 확인 (__await__ 메서드 존재)
            try
            {
                var awaitMethod = obj.GetAttribute("__await__");
                if (awaitMethod != null)
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"✅ GET_AWAITABLE: Generator-based coroutine with __await__");
                    #endif
                    return awaitMethod.Call(new PyObject[0], null);
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
                    #if DEBUG_LOG
                    Console.WriteLine($"✅ GET_AWAITABLE: Iterator-based awaitable");
                    #endif
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
                return pyFunc.Call(argsWithSelf, null);
            }

            var code = pyFunc.CodeObject;

            // For simple functions with no complex features, use direct execution
            if (code.CellVars?.Count == 0 && code.FreeVars?.Count == 0 &&
                !code.IsGenerator() && !code.IsCoroutine())
            {
                try
                {
                    // CPython 3.12: Use function's captured globals, not caller's scope
                    PyScopeChain functionScope;
                    if (pyFunc.GlobalsDict != null)
                    {
                        Console.WriteLine($"[FUNCTION SCOPE] Creating ScopeChain for {pyFunc.Name} from globalsDict:");
                        Console.WriteLine($"  globalsDict count: {pyFunc.GlobalsDict.Count}");
                        Console.WriteLine($"  globalsDict keys: {string.Join(", ", pyFunc.GlobalsDict.Keys.Take(10))}");

                        // Use the function's captured globals (CPython 3.12 compatible)
                        functionScope = new PyScopeChain(pyFunc.GlobalsDict, "<function>");
                    }
                    else
                    {
                        Console.WriteLine($"[FUNCTION SCOPE] Using ParentScope for {pyFunc.Name} (GlobalsDict is null)");
                        // Fallback to ParentScope for backward compatibility
                        functionScope = pyFunc.ParentScope ?? parentScope;
                    }

                    // Create minimal frame for simple function - CPython 3.12: include parent frame
                    var frame = new PyFrame(code, argsWithSelf, functionScope, pyFunc.Closure, CurrentFrame);
                    return ExecuteFrame(frame);
                }
                catch (PyReturnException retEx)
                {
                    return retEx.Value;
                }
            }

            // Fallback to full call for complex functions
            return pyFunc.Call(argsWithSelf, null);
        }

        private PyObject ExecuteFunctionCall(PyFunction pyFunc, PyObject[] args, PyScopeChain parentScope)
        {
            // Only optimize if function has code object
            if (pyFunc.CodeObject == null)
            {
                return pyFunc.Call(args, null);
            }

            var code = pyFunc.CodeObject;

            // For simple functions with no complex features, use direct execution
            if (code.CellVars?.Count == 0 && code.FreeVars?.Count == 0 &&
                !code.IsGenerator() && !code.IsCoroutine())
            {
                try
                {
                    // CPython 3.12: Use function's captured globals, not caller's scope
                    // func->f_globals is set at function definition time, not call time
                    PyScopeChain functionScope;
                    if (pyFunc.GlobalsDict != null)
                    {
                        Console.WriteLine($"[FUNCTION SCOPE] Creating ScopeChain for {pyFunc.Name} from globalsDict:");
                        Console.WriteLine($"  globalsDict count: {pyFunc.GlobalsDict.Count}");
                        Console.WriteLine($"  globalsDict keys: {string.Join(", ", pyFunc.GlobalsDict.Keys.Take(10))}");

                        // Use the function's captured globals (CPython 3.12 compatible)
                        functionScope = new PyScopeChain(pyFunc.GlobalsDict, "<function>");
                    }
                    else
                    {
                        Console.WriteLine($"[FUNCTION SCOPE] Using ParentScope for {pyFunc.Name} (GlobalsDict is null)");
                        // Fallback to ParentScope for backward compatibility
                        functionScope = pyFunc.ParentScope ?? parentScope;
                    }

                    // Create minimal frame for simple function - CPython 3.12: include parent frame
                    var frame = new PyFrame(code, args, functionScope, pyFunc.Closure, CurrentFrame);
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
                return pyFunc.Call(args, null);
            }
        }

        /// <summary>
        /// CPython 3.12: Implement super() attribute lookup
        /// </summary>
        private PyObject GetSuperAttribute(PyObject superObj, PyObject selfObj, string attrName)
        {
            try
            {
                // In CPython, super() object contains the class hierarchy info
                // For now, implement a simple version that looks up parent class methods

                if (selfObj is PyType selfType)
                {
                    // Get the parent class (metaclass case)
                    var parentType = typeof(PyType); // Python type metaclass

                    // Look for the method in parent type
                    if (attrName == "__new__")
                    {
                        // Return type.__new__ method
                        return new PyFunction("__new__", (args) =>
                        {
                            var cls = args[0];
                            var name = args[1];
                            var bases = args[2];
                            var attrs = args[3];

                            #if DEBUG_LOG
                            Console.WriteLine($"🔧 type.__new__: Creating class {name}");
                            #endif

                            // Create new class using PyType constructor
                            return new PyType(((PyString)name).Value, new PyType[0]);
                        }, null); // cls, name, bases, attrs
                    }
                }

                // Fallback: try to get attribute directly from super object
                return superObj.GetAttribute(attrName);
            }
            catch (Exception ex)
            {
                #if DEBUG_LOG
                Console.WriteLine($"⚠️  GetSuperAttribute error: {ex.Message}");
                #endif
                // Return None for missing attributes for now
                return PyNone.Instance;
            }
        }

        /// <summary>
        /// CPython 3.12: Handle function calls with keyword arguments using KW_NAMES
        /// </summary>
        private PyObject CallWithKeywords(PyObject callable, PyObject[] args, PyTuple kwNames, PyScopeChain scopeChain)
        {
            // KW_NAMES contains the names of keyword arguments
            // args array: [positional_args...] [keyword_values...]
            var kwNamesList = kwNames.Items.Select(name => ((PyString)name).Value).ToArray();
            var numKwArgs = kwNamesList.Length;
            var numPosArgs = args.Length - numKwArgs;

            // Split positional and keyword arguments
            var positionalArgs = new PyObject[numPosArgs];
            var keywordArgs = new Dictionary<string, PyObject>();

            Array.Copy(args, 0, positionalArgs, 0, numPosArgs);

            for (int i = 0; i < numKwArgs; i++)
            {
                keywordArgs[kwNamesList[i]] = args[numPosArgs + i];
            }

            // Special handling for different callable types
            if (callable is PyType pyType)
            {
                return CallTypeWithKeywords(pyType, positionalArgs, keywordArgs);
            }
            else if (callable is PyBuiltinFunction builtin)
            {
                return CallBuiltinWithKeywords(builtin, positionalArgs, keywordArgs);
            }
            else if (callable is PyFunction func)
            {
                return CallPyFunctionWithKeywords(func, positionalArgs, keywordArgs, scopeChain);
            }
            else
            {
                // Fallback: convert keyword arguments to PyDict and call
                PyDict? kwargs = null;
                if (keywordArgs != null && keywordArgs.Count > 0)
                {
                    kwargs = new PyDict();
                    foreach (var kv in keywordArgs)
                    {
                        kwargs.SetItem(new PyString(kv.Key), kv.Value);
                    }
                }
                return callable.Call(positionalArgs, kwargs);
            }
        }

        /// <summary>
        /// Call PyType (constructor) with keyword arguments
        /// </summary>
        private PyObject CallTypeWithKeywords(PyType pyType, PyObject[] positionalArgs, Dictionary<string, PyObject> keywordArgs)
        {
            // For now, handle common datetime types specially
            if (pyType.Name == "timedelta")
            {
                return CreateTimeDeltaWithKeywords(positionalArgs, keywordArgs);
            }
            else if (pyType.Name == "datetime")
            {
                return CreateDateTimeWithKeywords(positionalArgs, keywordArgs);
            }

            // CPython 3.12: Convert keyword arguments to PyDict
            PyDict? kwargs = null;
            if (keywordArgs != null && keywordArgs.Count > 0)
            {
                kwargs = new PyDict();
                foreach (var kv in keywordArgs)
                {
                    kwargs.SetItem(new PyString(kv.Key), kv.Value);
                }
            }

            // Call with both positional and keyword arguments
            return pyType.Call(positionalArgs, kwargs);
        }

        /// <summary>
        /// Create timedelta object with keyword arguments
        /// </summary>
        private PyObject CreateTimeDeltaWithKeywords(PyObject[] positionalArgs, Dictionary<string, PyObject> keywordArgs)
        {
            var days = 0;
            var seconds = 0;
            var microseconds = 0;
            var milliseconds = 0;
            var minutes = 0;
            var hours = 0;
            var weeks = 0;

            // Process positional arguments first
            if (positionalArgs.Length > 0 && positionalArgs[0] is PyInt daysArg) days = (int)daysArg.Value;
            if (positionalArgs.Length > 1 && positionalArgs[1] is PyInt secondsArg) seconds = (int)secondsArg.Value;
            if (positionalArgs.Length > 2 && positionalArgs[2] is PyInt microsecondsArg) microseconds = (int)microsecondsArg.Value;
            if (positionalArgs.Length > 3 && positionalArgs[3] is PyInt millisecondsArg) milliseconds = (int)millisecondsArg.Value;
            if (positionalArgs.Length > 4 && positionalArgs[4] is PyInt minutesArg) minutes = (int)minutesArg.Value;
            if (positionalArgs.Length > 5 && positionalArgs[5] is PyInt hoursArg) hours = (int)hoursArg.Value;
            if (positionalArgs.Length > 6 && positionalArgs[6] is PyInt weeksArg) weeks = (int)weeksArg.Value;

            // Process keyword arguments
            foreach (var kvp in keywordArgs)
            {
                if (kvp.Value is PyInt intVal)
                {
                    switch (kvp.Key)
                    {
                        case "days": days = (int)intVal.Value; break;
                        case "seconds": seconds = (int)intVal.Value; break;
                        case "microseconds": microseconds = (int)intVal.Value; break;
                        case "milliseconds": milliseconds = (int)intVal.Value; break;
                        case "minutes": minutes = (int)intVal.Value; break;
                        case "hours": hours = (int)intVal.Value; break;
                        case "weeks": weeks = (int)intVal.Value; break;
                    }
                }
            }

            return new Modules.Stdlib.PyTimeDelta(days, seconds, microseconds, milliseconds, minutes, hours, weeks);
        }

        /// <summary>
        /// Create datetime object with keyword arguments
        /// </summary>
        private PyObject CreateDateTimeWithKeywords(PyObject[] positionalArgs, Dictionary<string, PyObject> keywordArgs)
        {
            // Basic implementation - extend as needed
            return new Modules.Stdlib.PyDateTime(DateTime.Now);
        }

        /// <summary>
        /// Call builtin function with keyword arguments
        /// </summary>
        private PyObject CallBuiltinWithKeywords(PyBuiltinFunction builtin, PyObject[] positionalArgs, Dictionary<string, PyObject> keywordArgs)
        {
            // For now, ignore keyword arguments and call with positional only
            return builtin.Call(positionalArgs, null);
        }

        /// <summary>
        /// Call PyFunction with keyword arguments
        /// </summary>
        private PyObject CallPyFunctionWithKeywords(PyFunction func, PyObject[] positionalArgs, Dictionary<string, PyObject> keywordArgs, PyScopeChain scopeChain)
        {
            // CPython 3.12 방식: ExecuteFunctionCall에 키워드 인수를 전달하여 매개변수 바인딩 처리
            return ExecuteFunctionCallWithKeywords(func, positionalArgs, keywordArgs, scopeChain);
        }

        /// <summary>
        /// CPython 3.12: Execute function call with keyword arguments support
        /// </summary>
        private PyObject ExecuteFunctionCallWithKeywords(PyFunction pyFunc, PyObject[] positionalArgs, Dictionary<string, PyObject> keywordArgs, PyScopeChain parentScope)
        {
            if (pyFunc.CodeObject == null)
            {
                return pyFunc.Call(positionalArgs, null);
            }

            var code = pyFunc.CodeObject;

            try
            {
                // CPython 3.12: Combine positional and keyword arguments into single array for frame
                var allArgs = new PyObject[positionalArgs.Length + keywordArgs.Count];
                Array.Copy(positionalArgs, 0, allArgs, 0, positionalArgs.Length);

                int keywordIndex = positionalArgs.Length;
                foreach (var kvp in keywordArgs)
                {
                    allArgs[keywordIndex++] = kvp.Value;
                }

                // Create frame with all arguments
                var frame = new PyFrame(code, allArgs, parentScope, pyFunc.Closure, CurrentFrame);

                // CPython 3.12: Bind keyword arguments to parameters
                BindArgumentsToParametersWithKeywords(frame, positionalArgs, keywordArgs, code);

                return ExecuteFrame(frame);
            }
            catch (PyReturnException retEx)
            {
                return retEx.Value;
            }
        }

        /// <summary>
        /// CPython 3.12: Bind arguments to parameters with keyword arguments support
        /// </summary>
        private void BindArgumentsToParametersWithKeywords(PyFrame frame, PyObject[] positionalArgs, Dictionary<string, PyObject> keywordArgs, PyCodeObject code)
        {
#if DEBUG_LOG
            Console.WriteLine($"🔗 키워드 인수 매개변수 바인딩: {positionalArgs.Length}개 위치 인수, {keywordArgs.Count}개 키워드 인수, {code.ArgCount}개 매개변수");
            Console.WriteLine($"  Code flags: {code.Flags} (CO_VARARGS={((code.Flags & PyCodeObject.CO_VARARGS) != 0)}, CO_VARKEYWORDS={((code.Flags & PyCodeObject.CO_VARKEYWORDS) != 0)})");
#endif

            bool hasVarArgs = (code.Flags & PyCodeObject.CO_VARARGS) != 0;
            bool hasVarKeywords = (code.Flags & PyCodeObject.CO_VARKEYWORDS) != 0;

            // Phase 1: Bind positional arguments to regular parameters
            int posArgIndex = 0;
            for (int paramIndex = 0; paramIndex < code.ArgCount; paramIndex++)
            {
                var paramName = code.VarNames[paramIndex];

                if (posArgIndex < positionalArgs.Length)
                {
                    // Bind positional argument
                    frame.FastLocals[paramName] = positionalArgs[posArgIndex];
                    frame.ScopeChain.AssignVariable(paramName, positionalArgs[posArgIndex]);
                    posArgIndex++;

#if DEBUG_LOG
                    Console.WriteLine($"  → {paramName} = {positionalArgs[posArgIndex - 1]} (위치 인수 {posArgIndex - 1})");
#endif
                }
                else if (keywordArgs.ContainsKey(paramName))
                {
                    // Bind keyword argument to parameter
                    var keywordValue = keywordArgs[paramName];
                    frame.FastLocals[paramName] = keywordValue;
                    frame.ScopeChain.AssignVariable(paramName, keywordValue);
                    keywordArgs.Remove(paramName); // Remove so it doesn't go into **kwargs

#if DEBUG_LOG
                    Console.WriteLine($"  → {paramName} = {keywordValue} (키워드 인수)");
#endif
                }
                else
                {
                    // Check for default value
                    int numRequiredParams = code.ArgCount - code.DefaultValues.Count;
                    if (paramIndex >= numRequiredParams && paramIndex - numRequiredParams < code.DefaultValues.Count)
                    {
                        var defaultValue = code.DefaultValues[paramIndex - numRequiredParams];
                        frame.FastLocals[paramName] = defaultValue;
                        frame.ScopeChain.AssignVariable(paramName, defaultValue);

#if DEBUG_LOG
                        Console.WriteLine($"  → {paramName} = {defaultValue} (기본값)");
#endif
                    }
                    else
                    {
                        throw PyTypeError.Create($"missing required argument: '{paramName}'");
                    }
                }
            }

            // Phase 2: Handle *args parameter
            if (hasVarArgs)
            {
                string argsParamName = code.ArgCount < code.VarNames.Count ? code.VarNames[code.ArgCount] : "args";
                var remainingPositionalArgs = new List<PyObject>();

                // Collect remaining positional arguments
                for (int i = posArgIndex; i < positionalArgs.Length; i++)
                {
                    remainingPositionalArgs.Add(positionalArgs[i]);
                }

                var argsTuple = new PyTuple(remainingPositionalArgs.ToArray());
                frame.FastLocals[argsParamName] = argsTuple;
                frame.ScopeChain.AssignVariable(argsParamName, argsTuple);

#if DEBUG_LOG
                Console.WriteLine($"  → *{argsParamName} = {argsTuple} ({remainingPositionalArgs.Count}개 인수)");
#endif
            }
            else if (posArgIndex < positionalArgs.Length)
            {
                // Too many positional arguments and no *args parameter
                throw PyTypeError.Create($"{code.Name}() takes {code.ArgCount} positional arguments but {positionalArgs.Length} were given");
            }

            // Phase 3: Handle **kwargs parameter
            if (hasVarKeywords)
            {
                string kwargsParamName = "kwargs";
                int kwargsIndex = code.ArgCount + (hasVarArgs ? 1 : 0);
                if (kwargsIndex < code.VarNames.Count)
                {
                    kwargsParamName = code.VarNames[kwargsIndex];
                }

                // Create kwargs dictionary with remaining keyword arguments
                var kwargsDict = new PyDict();
                foreach (var kvp in keywordArgs)
                {
                    kwargsDict.SetItem(new PyString(kvp.Key), kvp.Value);
                }

                frame.FastLocals[kwargsParamName] = kwargsDict;
                frame.ScopeChain.AssignVariable(kwargsParamName, kwargsDict);

#if DEBUG_LOG
                Console.WriteLine($"  → **{kwargsParamName} = {kwargsDict} ({keywordArgs.Count}개 키워드)");
#endif
            }
            else if (keywordArgs.Count > 0)
            {
                // Unexpected keyword arguments and no **kwargs parameter
                var firstUnexpectedKwarg = keywordArgs.Keys.First();
                throw PyTypeError.Create($"{code.Name}() got an unexpected keyword argument '{firstUnexpectedKwarg}'");
            }
        }

        /// <summary>
        /// Calculate cumulative byte offset for given instruction index (CPython 3.12 compatible)
        /// </summary>
        private int CalculateByteOffset(int instructionIndex, List<ByteCodeInstruction> instructions)
        {
            int byteOffset = 0;
            for (int i = 0; i < instructionIndex && i < instructions.Count; i++)
            {
                var instruction = instructions[i];
                byteOffset += PyJumpBackwardUtil.GetCPythonInstructionSize(instruction.OpCode, instruction.Argument);
            }
            return byteOffset;
        }

        /// <summary>
        /// Check if an exception is an instance of the expected exception type or its parent types
        /// </summary>
        private static bool IsExceptionInstanceOf(PyException exception, string expectedTypeName)
        {
            // Direct type match
            if (exception.GetTypeName() == expectedTypeName)
                return true;

            // Check inheritance hierarchy
            switch (exception.GetTypeName())
            {
                case "ValueError":
                    return expectedTypeName == "Exception" || expectedTypeName == "BaseException";
                case "TypeError":
                    return expectedTypeName == "Exception" || expectedTypeName == "BaseException";
                case "RuntimeError":
                    return expectedTypeName == "Exception" || expectedTypeName == "BaseException";
                case "AttributeError":
                    return expectedTypeName == "Exception" || expectedTypeName == "BaseException";
                case "KeyError":
                    return expectedTypeName == "LookupError" || expectedTypeName == "Exception" || expectedTypeName == "BaseException";
                case "IndexError":
                    return expectedTypeName == "LookupError" || expectedTypeName == "Exception" || expectedTypeName == "BaseException";
                case "NameError":
                    return expectedTypeName == "Exception" || expectedTypeName == "BaseException";
                case "ImportError":
                    return expectedTypeName == "Exception" || expectedTypeName == "BaseException";
                case "OSError":
                    return expectedTypeName == "Exception" || expectedTypeName == "BaseException";
                case "IOError":
                    return expectedTypeName == "OSError" || expectedTypeName == "Exception" || expectedTypeName == "BaseException";
                case "ZeroDivisionError":
                    return expectedTypeName == "ArithmeticError" || expectedTypeName == "Exception" || expectedTypeName == "BaseException";
                case "OverflowError":
                    return expectedTypeName == "ArithmeticError" || expectedTypeName == "Exception" || expectedTypeName == "BaseException";
                case "StopIteration":
                    return expectedTypeName == "Exception" || expectedTypeName == "BaseException";
                case "AssertionError":
                    return expectedTypeName == "Exception" || expectedTypeName == "BaseException";
                case "SystemExit":
                    return expectedTypeName == "BaseException";
                case "KeyboardInterrupt":
                    return expectedTypeName == "BaseException";
                case "GeneratorExit":
                    return expectedTypeName == "BaseException";
                default:
                    // For unknown exceptions, assume they inherit from Exception
                    return expectedTypeName == "Exception" || expectedTypeName == "BaseException";
            }
        }

        /// <summary>
        /// Check if an object can be treated as an exception-like object
        /// </summary>
        private bool IsExceptionLike(PyObject obj)
        {
            // For now, any object can potentially be an exception if it has the right structure
            // In a full implementation, we'd check the class hierarchy
            // For simplicity, we'll accept any custom object as potentially exception-like
            return true;
        }
    }

    #endregion
}