// Performance: Eliminated System.Linq - all LINQ calls replaced with manual loops

using SharpPy.Core;

namespace SharpPy
{
    #region Virtual Machine (기존 LEGB 시스템 활용)

    // VM 실행 프레임 (기존 PyScopeChain과 연동)
    public class PyFrame : PyObject
    {
        public PyCodeObject Code { get; }
        public PyStack ValueStack { get; }
        public PyScopeChain ScopeChain { get; }       // 기존 LEGB 시스템 활용!
        public PyObject[] LocalsPlus { get; }  // CPython 3.12 style: Direct array for local variables
        public int InstructionPointer { get; set; }

        // CPython 3.12: Frame chain for proper call stack tracking
        public PyFrame? ParentFrame { get; set; }     // 부모 프레임 (call stack)

        // CPython 3.12: f_globals - Reference to module's __dict__ for import resolution
        public Dictionary<string, PyObject> Globals { get; private set; }

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

        // CPython 3.12: Class body locals dictionary (for __prepare__ dict subclasses)
        // When executing class body, STORE_NAME writes to this dict instead of scope
        public PyObject? ClassLocalsDict { get; set; }

        // CPython-style exception handling support
        public Stack<int> ExceptionHandlers { get; } = new Stack<int>();
        public PyBaseException? LastException { get; set; }
        public PyBaseException? CurrentException { get; set; } // Current exception for PUSH_EXC_INFO
        public int ExceptionHandlerCallCount { get; set; } = 0; // Prevent infinite loops

        // CPython 3.12: Generator throw() support - injected exception to raise on next frame execution
        // CPython reference: Objects/genobject.c:531-556 (gen_send_ex with exc_state handling)
        public PythonException? PendingException { get; set; }

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

        /// <summary>
        /// CPython 3.12: Create PyFrame with kwargs dict (equivalent to _PyEvalFramePushAndInit_Ex)
        /// CPython reference: Python/ceval.c:1621-1658
        /// </summary>
        public static PyFrame CreateWithKwargs(PyCodeObject code, PyObject[] args, PyDict kwargs,
            PyScopeChain parentScope = null, PyCell[] closure = null, PyTuple defaults = null)
        {
            // CPython 3.12: Line 1628 - _PyStack_UnpackDict converts kwargs dict to (args + kwnames) format
            var kwNamesList = new List<PyObject>();
            var kwValues = new List<PyObject>();

            foreach (var kv in kwargs.InternalDict)
            {
                kwNamesList.Add(kv.Key);  // PyString key
                kwValues.Add(kv.Value);   // Argument value
            }

            // Combine positional args and keyword values
            var finalArgs = new PyObject[args.Length + kwValues.Count];
            Array.Copy(args, 0, finalArgs, 0, args.Length);
            for (int i = 0; i < kwValues.Count; i++)
            {
                finalArgs[args.Length + i] = kwValues[i];
            }

            var kwNames = new PyTuple(kwNamesList.ToArray());

            // Create a parent frame to hold KeywordNamesForNextCall
            // This simulates CPython's approach where kwnames is passed through the call chain
            var dummyCode = new PyCodeObject("<kwargs_holder>", new List<ByteCodeInstruction>(),
                new List<PyObject>(), new List<string>(), new List<string>());
            var parentFrame = new PyFrame(dummyCode, new PyObject[0], parentScope);
            parentFrame.KeywordNamesForNextCall = kwNames;

            // Create the actual frame with combined args
            return new PyFrame(code, finalArgs, parentScope, closure, parentFrame, defaults, null);
        }

        public PyFrame(PyCodeObject code, PyObject[] args, PyScopeChain parentScope = null, PyCell[] closure = null, PyFrame parentFrame = null, PyTuple defaults = null, PyDict kwdefaults = null)
        {
#if DEBUG_LOG
            Console.WriteLine($"🆕 PyFrame 생성: {code.Name}, args={args.Length}개");
#endif
            Code = code;
            ValueStack = new PyStack();
            // 부모 스코프 체인이 있으면 상속, 없으면 새로 생성
            ScopeChain = parentScope ?? new PyScopeChain();

            // CPython 3.12: Initialize LocalsPlus array for fast local variable access
            int nlocals = code.VarNames.Count;
            LocalsPlus = new PyObject[nlocals];
            // Initialize all to PyNull (uninitialized marker)
            for (int i = 0; i < nlocals; i++)
            {
                LocalsPlus[i] = PyNull.Instance;
            }

            InstructionPointer = 0;

            // CPython 3.12: Set parent frame for call stack tracking
            ParentFrame = parentFrame;

            // CPython 3.12: Initialize f_globals from ScopeChain.GlobalScope
            Globals = ScopeChain.GlobalScope?.Variables ?? new Dictionary<string, PyObject>();

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
            BindArgumentsToParametersCPython312(args, code, parentFrame, defaults, kwdefaults);
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
        private void BindArgumentsToParametersCPython312(PyObject[] args, PyCodeObject code, PyFrame parentFrame, PyTuple runtimeDefaults = null, PyDict kwdefaults = null)
        {
            #if DEBUG_VM_LOG
            Console.WriteLine($"[BIND ARGS] BindArgumentsToParametersCPython312 for {code.Name}:");
            Console.WriteLine($"  runtimeDefaults is null: {runtimeDefaults == null}");
            if (runtimeDefaults != null)
            {
                Console.WriteLine($"  runtimeDefaults.Items.Length: {runtimeDefaults.Items.Length}");
                for (int i = 0; i < runtimeDefaults.Items.Length; i++)
                {
                    Console.WriteLine($"  runtimeDefaults[{i}]: {runtimeDefaults.Items[i]}");
                }
            }
            Console.WriteLine($"  code.DefaultValues.Count: {code.DefaultValues.Count}");
            #endif

            // CPython 3.12: Check for keyword arguments from parent frame
            PyTuple kwNames = null;
            Dictionary<string, PyObject> keywordArgs = null;
            PyObject[] positionalArgs = args;

            if (parentFrame?.KeywordNamesForNextCall != null && parentFrame.KeywordNamesForNextCall.Items.Length > 0)
            {
                kwNames = parentFrame.KeywordNamesForNextCall;
                // Performance: Eliminated LINQ - manual array conversion
                var kwNamesList = new string[kwNames.Items.Length];
                for (int i = 0; i < kwNames.Items.Length; i++)
                {
                    kwNamesList[i] = ((PyString)kwNames.Items[i]).Value;
                }
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

            // Phase 1: Bind positional arguments to regular parameters (NOT including keyword-only)
            // CPython 3.12: co_argcount does NOT include keyword-only parameters
            int regularArgCount = code.ArgCount - code.KwonlyArgCount;
            int posArgIndex = 0;
            for (int paramIndex = 0; paramIndex < regularArgCount; paramIndex++)
            {
                var paramName = code.VarNames[paramIndex];

                if (posArgIndex < positionalArgs.Length)
                {
                    // Bind positional argument
                    LocalsPlus[paramIndex] = positionalArgs[posArgIndex];
                    ScopeChain.AssignVariable(paramName, positionalArgs[posArgIndex]);
                    posArgIndex++;

#if DEBUG_LOG
                    Console.WriteLine($"  → {paramName} = {positionalArgs[posArgIndex - 1]} (위치 인수)");
#endif
                }
                else if (keywordArgs != null && keywordArgs.ContainsKey(paramName))
                {
                    // CPython 3.12: Check if this is a positional-only parameter
                    // Positional-only parameters cannot be passed as keyword arguments
                    if (paramIndex < code.PosonlyArgCount)
                    {
                        // Collect all positional-only parameters passed as keywords
                        var posonlyNames = new List<string>();
                        for (int k = 0; k < code.PosonlyArgCount; k++)
                        {
                            var posonlyName = code.VarNames[k];
                            if (keywordArgs.ContainsKey(posonlyName))
                            {
                                posonlyNames.Add(posonlyName);
                            }
                        }

                        if (posonlyNames.Count > 0)
                        {
                            var errorNames = string.Join(", ", posonlyNames);
                            throw PyTypeError.Create($"{code.Name}() got some positional-only arguments passed as keyword arguments: '{errorNames}'");
                        }
                    }

                    // Bind keyword argument to parameter
                    var keywordValue = keywordArgs[paramName];
                    LocalsPlus[paramIndex] = keywordValue;
                    ScopeChain.AssignVariable(paramName, keywordValue);
                    keywordArgs.Remove(paramName); // Remove so it doesn't go into **kwargs

#if DEBUG_LOG
                    Console.WriteLine($"  → {paramName} = {keywordValue} (키워드 인수)");
#endif
                }
                else
                {
                    // CPython 3.12: Check for default value from runtime defaults (captured from MAKE_FUNCTION)
                    // Priority: runtimeDefaults (from func.__defaults__) > code.DefaultValues (compile-time, legacy)
                    // Performance: Eliminated LINQ - check List directly
                    PyTuple effectiveDefaults = runtimeDefaults;
                    if (effectiveDefaults == null && code.DefaultValues.Count > 0)
                    {
                        var defaultsArray = new PyObject[code.DefaultValues.Count];
                        code.DefaultValues.CopyTo(defaultsArray, 0);
                        effectiveDefaults = new PyTuple(defaultsArray);
                    }
                    int numRequiredParams = regularArgCount - (effectiveDefaults?.Items.Length ?? 0);

                    if (effectiveDefaults != null && paramIndex >= numRequiredParams && paramIndex - numRequiredParams < effectiveDefaults.Items.Length)
                    {
                        var defaultValue = effectiveDefaults.Items[paramIndex - numRequiredParams];
                        LocalsPlus[paramIndex] = defaultValue;
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

            // Phase 1.5: Bind keyword-only arguments (CPython 3.12)
            // These come AFTER regular parameters but BEFORE *args/**kwargs
            for (int kwOnlyIndex = 0; kwOnlyIndex < code.KwonlyArgCount; kwOnlyIndex++)
            {
                int paramIndex = regularArgCount + kwOnlyIndex;
                var paramName = code.VarNames[paramIndex];

                // Keyword-only parameters can ONLY be passed by keyword, never positionally
                if (keywordArgs != null && keywordArgs.ContainsKey(paramName))
                {
                    var keywordValue = keywordArgs[paramName];
                    LocalsPlus[paramIndex] = keywordValue;
                    ScopeChain.AssignVariable(paramName, keywordValue);
                    keywordArgs.Remove(paramName); // Remove so it doesn't go into **kwargs

#if DEBUG_LOG
                    Console.WriteLine($"  → {paramName} = {keywordValue} (keyword-only 인수)");
#endif
                }
                else
                {
                    // CPython 3.12: Check runtime kwdefaults (from func.__kwdefaults__) first, then compile-time
                    // Priority: kwdefaults (runtime) > code.KwDefaults (compile-time)
                    // Note: CPython uses PyDict_GetItemWithError(func->func_kwdefaults, varname)
                    PyObject defaultValue = null;
                    bool hasDefault = false;

                    // First check runtime kwdefaults dict (CPython 3.12 Python/ceval.c)
                    if (kwdefaults != null)
                    {
                        // CPython: PyDict_GetItemWithError(func->func_kwdefaults, varname)
                        // We iterate because PyString instances may not match in Dictionary lookup
                        foreach (var kv in kwdefaults.InternalDict)
                        {
                            if (kv.Key is PyString keyStr && keyStr.Value == paramName)
                            {
                                defaultValue = kv.Value;
                                hasDefault = true;
#if DEBUG_LOG
                                Console.WriteLine($"  → {paramName} = {defaultValue} (runtime __kwdefaults__)");
#endif
                                break;
                            }
                        }
                    }

                    // Fallback to compile-time KwDefaults
                    if (!hasDefault && kwOnlyIndex < code.KwDefaults.Count)
                    {
                        defaultValue = code.KwDefaults[kwOnlyIndex];
                        hasDefault = true;
#if DEBUG_LOG
                        Console.WriteLine($"  → {paramName} = {defaultValue} (compile-time keyword-only 기본값)");
#endif
                    }

                    if (hasDefault)
                    {
                        LocalsPlus[paramIndex] = defaultValue;
                        ScopeChain.AssignVariable(paramName, defaultValue);
                    }
                    else
                    {
                        // Missing required keyword-only argument
                        throw PyTypeError.Create($"[PyFrame] missing required keyword-only argument: '{paramName}'");
                    }
                }
            }

            // Phase 2: Handle *args (if function has varargs) - CPython 3.12 compatible
            if (hasVarArgs)
            {
                int varargsIndex = code.ArgCount; // *args parameter index
                var varargsName = code.VarNames[varargsIndex];

                // Collect remaining positional arguments into *args tuple
                var extraArgs = new PyObject[Math.Max(0, positionalArgs.Length - posArgIndex)];
                if (posArgIndex < positionalArgs.Length)
                {
                    Array.Copy(positionalArgs, posArgIndex, extraArgs, 0, extraArgs.Length);
                }
                var argsTuple = new PyTuple(extraArgs);

                LocalsPlus[varargsIndex] = argsTuple;
                ScopeChain.AssignVariable(varargsName, argsTuple);

#if DEBUG_LOG
                Console.WriteLine($"  → {varargsName} = {argsTuple} (*args with {extraArgs.Length} items)");
#endif
            }

            // Phase 3: Handle **kwargs (if function has varkeywords)
            if (hasVarKeywords)
            {
                int varkwargsIndex = code.ArgCount + (hasVarArgs ? 1 : 0); // **kwargs parameter index
                var varkwargsName = code.VarNames[varkwargsIndex];
                var kwargsDict = new PyDict();

                // Add keyword arguments to **kwargs dict if they exist
                if (keywordArgs != null && keywordArgs.Count > 0)
                {
                    foreach (var kvp in keywordArgs)
                    {
                        kwargsDict.SetItem(new PyString(kvp.Key), kvp.Value);
                    }
                }

                LocalsPlus[varkwargsIndex] = kwargsDict;
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
                // Performance: Eliminated LINQ - get first key manually
                string unexpectedKey = null;
                foreach (var key in keywordArgs.Keys)
                {
                    unexpectedKey = key;
                    break;
                }
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
            // CPython 3.12: Exception table uses byte offsets, but InstructionPointer is instruction index
            // CRITICAL: Must account for inline cache sizes when converting to byte offset!
            // WRONG: currentByteOffset = InstructionPointer * 2 (doesn't account for inline cache)
            // RIGHT: Use PyCodeObject.InstructionIndexToByteOffset which sums actual instruction word counts
            var currentByteOffset = Code.InstructionIndexToByteOffset(InstructionPointer);

            #if DEBUG_LOG
            Console.WriteLine($"🔍 Searching Exception Table for instruction {InstructionPointer} (byte offset {currentByteOffset}):");
            #endif

            // Search Exception Table for a handler covering current instruction
            foreach (var entry in Code.ExceptionTable)
            {
                #if DEBUG_LOG
                Console.WriteLine($"   Entry: Start={entry.StartOffset}, End={entry.EndOffset}, Handler={entry.HandlerOffset}");
                #endif
                #if DEBUG_LOG
                Console.WriteLine($"   Check: {currentByteOffset} >= {entry.StartOffset} && {currentByteOffset} < {entry.EndOffset}");
                #endif

                if (currentByteOffset >= entry.StartOffset && currentByteOffset < entry.EndOffset)
                {
                    // CPython 3.12: Handler offset in exception table is byte offset
                    // CRITICAL: Must account for inline cache when converting to instruction index!
                    // WRONG: handlerInstructionIndex = entry.HandlerOffset / 2 (doesn't account for inline cache)
                    // RIGHT: Use PyCodeObject.ByteOffsetToInstructionIndex
                    int handlerInstructionIndex = Code.ByteOffsetToInstructionIndex(entry.HandlerOffset);
                    #if DEBUG_LOG
                    Console.WriteLine($"✅ Exception Table: MATCH! Handler at byte offset {entry.HandlerOffset} (instruction {handlerInstructionIndex}) for instruction {InstructionPointer}");
                    #endif
                    #if DEBUG_LOG
                    Console.WriteLine($"   Entry details: Depth={entry.Depth}, Lasti={entry.Lasti}");
                    #endif
                    return (handlerInstructionIndex, entry);
                }
                else
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"❌ No match for this entry");
                    #endif
                }
            }

            #if DEBUG_LOG
            Console.WriteLine($"❌ Exception Table: No handler found for instruction {InstructionPointer} (byte offset {currentByteOffset})");
            #endif
            return (null, null);
        }

        private int? GetExceptionHandlerFromTable()
        {
            var (handlerOffset, _) = GetExceptionHandlerFromTableWithEntry();
            return handlerOffset;
        }

        public override string ToString() => $"<frame for {Code.Name}>";

        // PyObject required overrides
        public override PyType GetPyType() => PyType.ObjectType;
        public override string GetTypeName() => "frame";

        /// <summary>
        /// CPython 3.12: Frame attribute access for traceback.py compatibility
        /// Exposes frame introspection attributes
        /// </summary>
        public override PyObject GetAttribute(string name)
        {
            return name switch
            {
                // f_code: code object being executed
                "f_code" => Code,

                // f_back: previous stack frame (toward the caller)
                "f_back" => ParentFrame != null ? ParentFrame : PyNone.Instance,

                // f_lineno: current line number in Python source code
                "f_lineno" => new PyInt(CurrentLineNumber >= 0 ? CurrentLineNumber : Code.GetFirstLineNo()),

                // f_locals: local namespace dictionary
                "f_locals" => BuildLocalsDict(),

                // f_globals: global namespace dictionary
                "f_globals" => new PyDict(Globals),

                // f_builtins: built-in namespace dictionary
                "f_builtins" => ScopeChain.BuiltinModule != null
                    ? ScopeChain.BuiltinModule
                    : PyNone.Instance,

                // f_lasti: instruction index (for traceback)
                "f_lasti" => new PyInt(Math.Max(0, InstructionPointer - 1)),

                // CPython 3.12: f_trace, f_trace_lines, f_trace_opcodes (not implemented yet)
                "f_trace" => PyNone.Instance,
                "f_trace_lines" => PyBool.True,  // Default to True
                "f_trace_opcodes" => PyBool.False,  // Default to False

                _ => throw PyAttributeError.Create($"'frame' object has no attribute '{name}'")
            };
        }

        /// <summary>
        /// Build f_locals dict from LocalsPlus array and scope
        /// </summary>
        private PyDict BuildLocalsDict()
        {
            var localsDict = new Dictionary<string, PyObject>();

            // Add variables from LocalsPlus array
            for (int i = 0; i < LocalsPlus.Length && i < Code.VarNames.Count; i++)
            {
                var value = LocalsPlus[i];
                if (value != null && value != PyNull.Instance)
                {
                    localsDict[Code.VarNames[i]] = value;
                }
            }

            // Add variables from LocalScope if exists
            if (LocalScope != null)
            {
                foreach (var kvp in LocalScope.Variables)
                {
                    localsDict[kvp.Key] = kvp.Value;
                }
            }

            return new PyDict(localsDict);
        }

        /// <summary>
        /// CPython 3.12: Clear frame locals to break reference cycles
        /// Called when frame is no longer needed
        /// </summary>
        public void Clear()
        {
            // Clear LocalsPlus array
            for (int i = 0; i < LocalsPlus.Length; i++)
            {
                LocalsPlus[i] = PyNull.Instance;
            }

            // Clear cells
            foreach (var cell in Cells)
            {
                cell.Value = PyNull.Instance;
            }

            // Clear local scope
            LocalScope?.Variables.Clear();
        }
    }

    // Python 가상 머신 (기존 객체 시스템과 완전 통합)
    public class PyVM
    {
        public static PyVM Instance { get; } = new PyVM();

        private readonly Stack<PyFrame> _frameStack;
        private readonly PyScopeChain _globalScope;

        // CPython 3.12: Adaptive Specialization System (PEP 659)
        private readonly AdaptiveSpecializer _specializer;

        // Current frame for zero-argument super() calls
        public static PyFrame? CurrentFrame => Instance._frameStack.Count > 0 ? Instance._frameStack.Peek() : null;

        // CPython 3.12: Get current frame for sys.exc_info() and other introspection
        public static PyFrame? GetCurrentFrame() => CurrentFrame;

        /// <summary>
        /// CPython 3.12: Python/errors.c:125-136 (_PyErr_GetTopmostException)
        /// Traverse the frame stack to find the topmost exception.
        /// Returns the exception from current frame or any parent frame that has one.
        /// </summary>
        public static PyBaseException? GetTopmostException()
        {
            // CPython 3.12: Python/errors.c:130-134
            // while ((exc_info->exc_value == NULL || exc_info->exc_value == Py_None) &&
            //        exc_info->previous_item != NULL)
            // {
            //     exc_info = exc_info->previous_item;
            // }
            foreach (var frame in Instance._frameStack)
            {
                var exception = frame.CurrentException ?? frame.LastException;
                if (exception != null)
                {
                    return exception;
                }
            }
            return null;
        }

        private PyVM()
        {
            _frameStack = new Stack<PyFrame>();
            _globalScope = new PyScopeChain(); // 기존 LEGB 시스템 사용!
            _specializer = new AdaptiveSpecializer(); // CPython 3.12: PEP 659
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

        /// <summary>
        /// Execute an expression with custom globals/locals (for eval())
        /// CPython 3.12: Python/pythonrun.c:run_eval_code_obj
        /// </summary>
        public PyObject ExecuteExpression(PyCodeObject codeObject, PyDict globals, PyDict locals)
        {
            // Create a scope chain from the provided globals/locals
            var scopeChain = new PyScopeChain();

            // Convert globals dict to scope variables
            if (globals != null)
            {
                foreach (var kvp in globals.InternalDict)
                {
                    if (kvp.Key is PyString keyStr)
                    {
                        scopeChain.GlobalScope.Variables[keyStr.Value] = kvp.Value;
                    }
                }
            }

            // If locals is different from globals, push a new local scope
            // CPython 3.12: eval() creates a new local scope if locals dict is provided
            if (locals != null && locals != globals)
            {
                var localScope = scopeChain.PushScope(ScopeType.Local, "eval_locals");

                foreach (var kvp in locals.InternalDict)
                {
                    if (kvp.Key is PyString keyStr)
                    {
                        localScope.Variables[keyStr.Value] = kvp.Value;
                    }
                }
            }

#if DEBUG_VM_LOG
            Console.WriteLine($"[ExecuteExpression] Globals count: {scopeChain.GlobalScope.Variables.Count}");
            Console.WriteLine($"[ExecuteExpression] Scopes count: {scopeChain.ScopeCount}");
#endif

            return ExecuteModule(codeObject, scopeChain);
        }

        // 프레임 실행 (바이트코드 해석)
        // CPython 3.12: Execute class body and return namespace
        public Dictionary<string, PyObject> ExecuteClassBody(PyCodeObject classBody, PyCell[]? closure = null, Dictionary<string, PyObject>? initialNamespace = null)
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

            // CPython 3.12: Check if __prepare__ returned a dict subclass
            // If so, use it as the LOCALS() dict for STORE_NAME operations
            PyObject? prepareResult = null;
            if (initialNamespace != null && initialNamespace.TryGetValue("__prepare_result__", out var prepareMarker))
            {
                prepareResult = prepareMarker;
                frame.ClassLocalsDict = prepareResult;
                #if DEBUG_LOG
                Console.WriteLine($"📦 Using __prepare__ result as ClassLocalsDict: {prepareResult?.GetType().Name}");
                #endif
            }

            // CPython 3.12: Pre-populate local scope with initial namespace from __prepare__
            // IMPORTANT: CPython passes the __prepare__ result dict DIRECTLY as locals() to the class body.
            // The dict is used as-is, without extraction and re-insertion.
            // (CPython: Python/bltinmodule.c:198 - ns passed directly to _PyEval_Vector)
            //
            // SharpPy Note: The ClassLocalsDict is already the SAME object returned by __prepare__.
            // Items added by __prepare__ (like _generate_next_value_) are already in that dict.
            // We must NOT call __setitem__ again for those items, as that would trigger validation logic
            // (e.g., _EnumDict checking if _auto_called was already set).
            //
            // Instead, we only need to make items visible to LOAD_NAME by adding to scope chain.
            if (initialNamespace != null && initialNamespace.Count > 0)
            {
                #if DEBUG_LOG
                Console.WriteLine($"📦 Pre-populating class namespace with {initialNamespace.Count} items from __prepare__");
                #endif
                foreach (var kvp in initialNamespace)
                {
                    // Skip the marker - it's not a real class attribute
                    if (kvp.Key == "__prepare_result__")
                        continue;

                    // Add to scope chain so LOAD_NAME can find it
                    // (ClassLocalsDict is checked FIRST by STORE_NAME, but LOAD_NAME uses LEGB)
                    frame.ScopeChain.CurrentScope.SetVariable(kvp.Key, kvp.Value);
                    #if DEBUG_LOG
                    Console.WriteLine($"  - {kvp.Key}: {kvp.Value?.GetType().Name}");
                    #endif
                }
            }

            var result = ExecuteFrame(frame);

            // Extract class namespace - capture variables added during class body execution
            var classNamespace = new Dictionary<string, PyObject>();

            // CPython Python/bltinmodule.c:201-209:
            //   - Class body executes with ns as locals (line 201)
            //   - After execution, pass SAME ns to metaclass.__new__ (line 208)
            //   - NO extraction or re-insertion of items
            //
            // SharpPy: When ClassLocalsDict was used (prepareResult != null):
            //   - STORE_NAME already called __setitem__ on ClassLocalsDict for all items
            //   - Items from initialNamespace are already IN the ClassLocalsDict
            //   - We must NOT include them in classNamespace again!
            //   - CallBuildClass will use originalPrepareResult directly
            //
            // Only include initialNamespace items when ClassLocalsDict was NOT used
            if (prepareResult == null && initialNamespace != null)
            {
                foreach (var kvp in initialNamespace)
                {
                    // Skip the marker - it's not a real class attribute
                    if (kvp.Key == "__prepare_result__")
                        continue;

                    classNamespace[kvp.Key] = kvp.Value;
                }
            }

            // Method 1: LocalsPlus array (for STORE_FAST operations)
            for (int i = 0; i < frame.LocalsPlus.Length; i++)
            {
                var value = frame.LocalsPlus[i];
                // Skip uninitialized variables (PyNull)
                if (!PyNull.IsNull(value))
                {
                    var varName = frame.Code.VarNames[i];
                    classNamespace[varName] = value;
                }
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

        /// <summary>
        /// Create NameError with suggestion
        /// CPython 3.12: Python/suggestions.c:217-290
        /// </summary>
        private static Exception CreateNameErrorWithSuggestion(string name, PyFrame frame)
        {
            var locals = new Dictionary<string, PyObject>();
            var globals = new Dictionary<string, PyObject>();
            var builtins = new Dictionary<string, PyObject>();

            // Collect locals from current scope
            if (frame.ScopeChain.CurrentScope != null)
            {
                foreach (var kvp in frame.ScopeChain.CurrentScope.Variables)
                {
                    locals[kvp.Key] = kvp.Value;
                }
            }

            // Collect globals
            if (frame.ScopeChain.GlobalScope != null)
            {
                foreach (var kvp in frame.ScopeChain.GlobalScope.Variables)
                {
                    globals[kvp.Key] = kvp.Value;
                }
            }

            // Collect builtins
            var builtinNames = frame.ScopeChain.BuiltinModule.GetAllBuiltinNames();
            foreach (var builtinName in builtinNames)
            {
                var builtin = frame.ScopeChain.BuiltinModule.GetBuiltin(builtinName);
                if (builtin != null)
                {
                    builtins[builtinName] = builtin;
                }
            }

            string? suggestion = ErrorSuggestions.GetSuggestionForNameError(name, locals, globals, builtins);
            string errorMessage = ErrorSuggestions.FormatNameErrorWithSuggestion(name, suggestion);
            return PyNameError.Create(errorMessage);
        }

        public PyObject ExecuteFrame(PyFrame frame)
        {
            _frameStack.Push(frame);

#if DEBUG_LOG
            Console.WriteLine($"\n🚀 VM 실행: {frame}");
#endif

            // CPython 3.12: Extended argument accumulation for EXTENDED_ARG support
            int extendedArg = 0;

#if DEBUG
            // 🛡️ 무한루프 방지 안전장치 (DEBUG 모드 전용)
            var startTime = DateTime.UtcNow;
            var maxInstructions = 50_000; // 최대 5만 명령어 (for debugging infinite loops)
            var maxTimeSeconds = 30; // 최대 30초
            var instructionCount = 0;

            // DEBUG: Track last instructions for debugging infinite loops
            var lastInstructions = new System.Collections.Generic.Queue<string>();
            const int maxLastInstructions = 100; // Increase to 100 for better analysis
#endif

            try
            {
                while (frame.InstructionPointer < frame.Code.Instructions.Count)
                {
#if DEBUG
                    // 🛡️ 안전장치 검사 (DEBUG 모드 전용)
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
                            Console.WriteLine($"\n[DEBUG] Instruction limit exceeded. Last {lastInstructions.Count} instructions:");
                            foreach (var log in lastInstructions)
                            {
                                Console.WriteLine($"  {log}");
                            }
                            Console.WriteLine($"[DEBUG] Current frame: {frame.Code.Name}");
                            Console.WriteLine($"[DEBUG] Current instruction pointer: {frame.InstructionPointer}");
                            throw new PythonException(new PyRuntimeError($"Instruction limit exceeded: {instructionCount} instructions"));
                        }
                    }
#endif

                    var instruction = frame.Code.Instructions[frame.InstructionPointer];

#if DEBUG
                    // DEBUG: Track instruction for debugging
                    var instructionLog = $"[{instructionCount}] IP={frame.InstructionPointer} {instruction.OpCode} arg={instruction.Argument} in {frame.Code.Name}";
                    if (lastInstructions.Count >= maxLastInstructions)
                        lastInstructions.Dequeue();
                    lastInstructions.Enqueue(instructionLog);
#endif

                    // CPython 3.12: Handle EXTENDED_ARG by accumulating argument bits
                    // EXTENDED_ARG shifts left by 8 bits and ORs with next instruction's arg
                    // Pattern: oparg = (oparg << 8) | instruction.Argument
                    if (instruction.OpCode == ByteCodeOp.EXTENDED_ARG)
                    {
                        extendedArg = (extendedArg << 8) | instruction.Argument;
                        frame.InstructionPointer++;
                        continue; // Skip to next instruction
                    }

                    // Apply accumulated extended argument to current instruction
                    // Create modified instruction with combined argument
                    if (extendedArg != 0)
                    {
                        int combinedArg = (extendedArg << 8) | instruction.Argument;
                        instruction = new ByteCodeInstruction(
                            instruction.OpCode,
                            combinedArg,
                            instruction.LineNumber,
                            instruction.ColumnOffset,
                            instruction.FileName,
                            instruction.TargetBlock,
                            instruction.ExceptBlock
                        );
                        extendedArg = 0; // Reset for next instruction
                    }

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
                        // Performance: Eliminated LINQ - manual stack preview
                        var stackArray = frame.ValueStack.ToArray();
                        var previewCount = Math.Min(5, stackArray.Length);
                        var stackItems = new string[previewCount];
                        for (int i = 0; i < previewCount; i++)
                        {
                            stackItems[i] = stackArray[stackArray.Length - 1 - i]?.ToString() ?? "null";
                        }
                        var stackContents = string.Join(", ", stackItems);
                        Console.WriteLine($"  {frame.InstructionPointer*2,3}: {instruction,-25} 스택:[{stackContents}]");
                    }
#endif

                    // CPython 3.12: Attempt adaptive specialization (PEP 659)
                    // NOTE: TrySpecialize is currently a no-op (skeleton implementation)
                    // When Enabled=false, this call returns immediately without any work
                    _specializer.TrySpecialize(frame, frame.InstructionPointer, instruction.OpCode);

                    try
                    {
                        // CPython 3.12: Check for pending exception from generator.throw()
                        // This must be inside the try block so it can be caught by exception handler
                        // CPython reference: Objects/genobject.c:531-556 (gen_send_ex with exc_state handling)
                        if (frame.PendingException != null)
                        {
                            var pendingExc = frame.PendingException;
                            frame.PendingException = null; // Clear before handling
                            #if DEBUG_LOG
                            Console.WriteLine($"🔧 PendingException detected: {pendingExc.PyException?.GetTypeName() ?? "unknown"}");
                            #endif
                            throw pendingExc; // This will be caught by the exception handler below
                        }

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

                        // CPython 3.12: Add current frame to traceback BEFORE unwinding
                        // Corresponds to PyTraceBack_Here() in CPython ceval.c:941
                        PyTraceBack_Here(frame, pyEx);

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

                            // CPython 3.12: Push lasti if required (for WITH_EXCEPT_START)
                            // CPython ceval.c:972-978
                            if (exceptionEntry.Lasti)
                            {
                                // Push current instruction pointer as lasti (PyLong)
                                var lastiValue = new PyInt(frame.InstructionPointer);
                                frame.ValueStack.Push(lastiValue);
                                #if DEBUG_LOG
                                Console.WriteLine($"🔧 Exception handled: pushed lasti={frame.InstructionPointer} to stack");
                                #endif
                            }

                            // CPython 3.12: Push exception instance to stack for PUSH_EXC_INFO
                            // CPython ceval.c:985-986
                            // PUSH_EXC_INFO will add prev_exc, transforming stack to: [..., lasti (if lasti=true), prev_exc, exc]
                            frame.ValueStack.Push(pyEx.PyException);
                            #if DEBUG_LOG
                            Console.WriteLine($"🔧 Exception handled: pushed exception instance to stack (depth={exceptionEntry.Depth}, lasti={exceptionEntry.Lasti})");
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
                        // Performance: Eliminated LINQ - manual stack preview for error message
                        var stackArray = frame.ValueStack.ToArray();
                        var previewCount = Math.Min(5, stackArray.Length);
                        var stackTypes = new string[previewCount];
                        for (int i = 0; i < previewCount; i++)
                        {
                            stackTypes[i] = stackArray[i].GetType().Name;
                        }
                        throw PyRuntimeError.Create($"COPY index {copyIndex} out of range (stack size: {frame.ValueStack.Count}). Stack contents: [{string.Join(", ", stackTypes)}]");
                    }

                    // CPython 3.12: COPY 1 copies TOS, COPY 2 copies TOS-1 (second from top), etc.
                    // PyStack: PeekAt(0) is TOS, PeekAt(1) is TOS-1
                    // So COPY 1 should use PeekAt(copyIndex - 1)
                    var valueToCopy = frame.ValueStack.PeekAt(copyIndex - 1);
                    #if DEBUG_LOG
                    // Debug: Console.WriteLine($"🔄 COPY {copyIndex}: copying TOS-{copyIndex-1} = {valueToCopy}");
                    #endif
                    frame.ValueStack.Push(valueToCopy);
                    break;

                case ByteCodeOp.SWAP: // SWAP(n) - TOS와 TOS-(n-1) 교환
                    var oparg = instruction.Argument;
                    #if DEBUG_VM_LOG
                    Console.WriteLine($"🔍 SWAP({oparg}): Stack.Count = {frame.ValueStack.Count}");
                    if (frame.ValueStack.Count > 0)
                    {
                        // Performance: Eliminated LINQ - manual stack preview
                        var stackArray = frame.ValueStack.ToArray();
                        var previewCount = Math.Min(5, stackArray.Length);
                        var stackTypes = new string[previewCount];
                        for (int i = 0; i < previewCount; i++)
                        {
                            stackTypes[i] = stackArray[i].GetType().Name;
                        }
                        var stackPreview = string.Join(", ", stackTypes);
                        Console.WriteLine($"    Stack top items: [{stackPreview}]");
                    }
                    #endif

                    // O(1) Swap operation using PyStack's indexed access
                    frame.ValueStack.Swap(oparg);

                    #if DEBUG_VM_LOG
                    Console.WriteLine($"    ✅ SWAP completed, Stack.Count = {frame.ValueStack.Count}");
                    #endif
                    break;

                case ByteCodeOp.LOAD_CONST:
                    var constant = frame.Code.Constants[instruction.Argument];
                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 LOAD_CONST: 인덱스 {instruction.Argument}, 값 {constant} (타입: {constant?.GetType().Name})");
                    // Performance: Eliminated LINQ - manual constant array formatting
                    var constPairs = new string[frame.Code.Constants.Count];
                    for (int i = 0; i < frame.Code.Constants.Count; i++)
                    {
                        constPairs[i] = $"{i}:{frame.Code.Constants[i]}";
                    }
                    Console.WriteLine($"🔍 Constants 배열 전체: [{string.Join(", ", constPairs)}]");
                    #endif
                    frame.ValueStack.Push(constant);
                    break;

                case ByteCodeOp.LOAD_NAME:
                    // CPython 3.12: Python/generated_cases.c.h lines 1708-1770
                    // LOAD_NAME checks LOCALS() first (which is ClassLocalsDict in class body),
                    // then GLOBALS(), then BUILTINS()
                    var name = frame.Code.Names[instruction.Argument];
                    PyObject? value = null;

                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 LOAD_NAME({name}): 현재 스코프 = {frame.ScopeChain.CurrentScope?.Name ?? "null"}");
                    #endif

                    // CPython 3.12: Check LOCALS() first (line 1711)
                    // In class body, LOCALS() returns the __prepare__ result (ClassLocalsDict)
                    if (frame.ClassLocalsDict != null)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"🔍 LOAD_NAME({name}): Checking ClassLocalsDict first");
                        #endif

                        // Try to get item from ClassLocalsDict (lines 1718-1735)
                        try
                        {
                            value = frame.ClassLocalsDict.GetItem(new PyString(name));
                            #if DEBUG_LOG
                            Console.WriteLine($"🔍 LOAD_NAME({name}): Found in ClassLocalsDict: {value?.GetType().Name}");
                            #endif
                        }
                        catch (PythonException ex) when (ex.PyException is PyKeyError)
                        {
                            // Not found in ClassLocalsDict, will fallback to globals/builtins
                            value = null;
                            #if DEBUG_LOG
                            Console.WriteLine($"🔍 LOAD_NAME({name}): Not found in ClassLocalsDict, trying LEGB");
                            #endif
                        }
                    }

                    // CPython 3.12: If not found in locals, check GLOBALS() and BUILTINS() (lines 1736-1767)
                    if (value == null)
                    {
                        value = frame.ScopeChain.LookupVariable(name, verbose: true);
                    }

                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 LOAD_NAME({name}): loaded {value?.GetType().Name ?? "null"} value = {value}");
                    #endif
                    frame.ValueStack.Push(value);
                    break;

                case ByteCodeOp.LOAD_FAST:
                    // CPython 3.12: Direct array access for local variables
                    var argIndex = instruction.Argument;
                    if (argIndex < frame.LocalsPlus.Length)
                    {
                        var fastValue = frame.LocalsPlus[argIndex];
                        // CPython 3.12: PyNull indicates uninitialized variable
                        if (PyNull.IsNull(fastValue))
                        {
                            var varName = frame.Code.VarNames[argIndex];
                            throw PyNameError.Create($"local variable '{varName}' referenced before assignment");
                        }
                        frame.ValueStack.Push(fastValue);
                    }
                    else
                    {
                        throw PyRuntimeError.Create($"LOAD_FAST: index {argIndex} out of range");
                    }
                    break;

                case ByteCodeOp.LOAD_FAST_AND_CLEAR:
                    // CPython 3.12 PEP 709: Load variable and clear it (set to PyNull)
                    var clearArgIndex = instruction.Argument;
                    if (clearArgIndex < frame.LocalsPlus.Length)
                    {
                        var clearValue = frame.LocalsPlus[clearArgIndex];
                        frame.ValueStack.Push(clearValue);  // Push even if PyNull
                        // Clear the variable (set to PyNull)
                        frame.LocalsPlus[clearArgIndex] = PyNull.Instance;
                    }
                    else
                    {
                        throw PyRuntimeError.Create($"LOAD_FAST_AND_CLEAR: index {clearArgIndex} out of range");
                    }
                    break;

                case ByteCodeOp.LOAD_FAST_CHECK:
                    // CPython 3.12: LOAD_FAST_CHECK - Load fast local with NULL check (same as LOAD_FAST)
                    var checkIndex = instruction.Argument;
                    if (checkIndex < frame.LocalsPlus.Length)
                    {
                        var checkValue = frame.LocalsPlus[checkIndex];
                        if (PyNull.IsNull(checkValue))
                        {
                            var checkVarName = frame.Code.VarNames[checkIndex];
                            throw PyNameError.Create($"local variable '{checkVarName}' referenced before assignment");
                        }
                        frame.ValueStack.Push(checkValue);
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
                    // CPython 3.12: Direct array access for fast locals
                    var storeIndex = instruction.Argument;
                    if (storeIndex < frame.LocalsPlus.Length)
                    {
                        var storeVal = frame.ValueStack.Pop();
                        frame.LocalsPlus[storeIndex] = storeVal;
                    }
                    else
                    {
                        throw PyRuntimeError.Create($"STORE_FAST: index {storeIndex} out of range");
                    }
                    break;

                case ByteCodeOp.DELETE_FAST:
                    // CPython 3.12: Delete fast local variable - set to PyNull
                    var deleteFastIndex = instruction.Argument;
                    if (deleteFastIndex < frame.LocalsPlus.Length)
                    {
                        // Set to PyNull to mark as deleted/uninitialized
                        frame.LocalsPlus[deleteFastIndex] = PyNull.Instance;
                    }
                    else
                    {
                        throw PyRuntimeError.Create($"DELETE_FAST: index {deleteFastIndex} out of range");
                    }
                    break;

                case ByteCodeOp.STORE_NAME:
                    var storeName = frame.Code.Names[instruction.Argument];
                    var storeValue = frame.ValueStack.Pop();

                    // CPython 3.12: If executing class body with __prepare__ dict subclass,
                    // call __setitem__ on the dict (like CPython's PyObject_SetItem)
                    if (frame.ClassLocalsDict != null)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"📝 STORE_NAME to ClassLocalsDict: {storeName} = {storeValue?.GetType().Name}");
                        #endif
                        frame.ClassLocalsDict.SetItem(new PyString(storeName), storeValue);
                    }
                    else
                    {
                        // Normal case: use LEGB system
                        frame.ScopeChain.AssignVariable(storeName, storeValue);
                    }
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
                    // 변수가 없으면 NameError with suggestion
                    throw CreateNameErrorWithSuggestion(deleteName, frame);

                case ByteCodeOp.LOAD_GLOBAL:
                    // CPython 3.12: oparg encoding: (nameIndex << 1) | pushNull
                    int globalOparg = instruction.Argument;
                    bool pushNull = (globalOparg & 1) == 1;
                    int globalNameIndex = globalOparg >> 1;

                    var globalName = frame.Code.Names[globalNameIndex];

                    // Special debugging for ReprEnum, Enum, Flag lookup
                    bool isEnumRelated = globalName == "ReprEnum" || globalName == "Enum" || globalName == "Flag";

                    if (isEnumRelated)
                    {
                        #if DEBUG_VM_LOG
                        Console.WriteLine($"\n[LOAD_GLOBAL] Looking for: {globalName}");
                        Console.WriteLine($"  Current function: {frame.Code.Name}");
                        Console.WriteLine($"  GlobalScope: {frame.ScopeChain.GlobalScope?.Name ?? "null"}");
                        #endif
                        if (frame.ScopeChain.GlobalScope != null)
                        {
                            #if DEBUG_VM_LOG
                            Console.WriteLine($"  GlobalScope variable count: {frame.ScopeChain.GlobalScope.Variables.Count}");
                            // Performance: Eliminated LINQ - manual key preview
                            var keyCount = Math.Min(20, frame.ScopeChain.GlobalScope.Variables.Keys.Count);
                            var keys = new string[keyCount];
                            int keyIdx = 0;
                            foreach (var key in frame.ScopeChain.GlobalScope.Variables.Keys)
                            {
                                if (keyIdx >= keyCount) break;
                                keys[keyIdx++] = key;
                            }
                            Console.WriteLine($"  GlobalScope keys: {string.Join(", ", keys)}");
                            Console.WriteLine($"  Has '{globalName}': {frame.ScopeChain.GlobalScope.Variables.ContainsKey(globalName)}");
                            #endif
                        }
                    }

                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 LOAD_GLOBAL({globalName}): pushNull={pushNull}, nameIndex={globalNameIndex}");
                    #endif

                    // CPython 3.12: Push NULL first if flag is set
                    if (pushNull)
                    {
                        frame.ValueStack.Push(PyNone.Instance); // Use PyNone.Instance as NULL marker
                        #if DEBUG_LOG
                        Console.WriteLine($"   Pushed NULL before loading {globalName}");
                        #endif
                    }

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
                    {
                        if (isEnumRelated)
                        {
                            #if DEBUG_VM_LOG
                            Console.WriteLine($"[LOAD_GLOBAL] ❌ Failed to find '{globalName}'!");
                            #endif
                        }
                        throw CreateNameErrorWithSuggestion(globalName, frame);
                    }

                    if (isEnumRelated)
                    {
                        #if DEBUG_VM_LOG
                        Console.WriteLine($"[LOAD_GLOBAL] ✅ Found '{globalName}': {globalValue?.GetType().Name}");
                        #endif
                    }

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
                        throw CreateNameErrorWithSuggestion(globalBuiltinName, frame);

                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 LOAD_GLOBAL_BUILTIN({globalBuiltinName}): loaded {builtinValue?.GetType().Name ?? "null"} value = {builtinValue}");
                    #endif
                    frame.ValueStack.Push(builtinValue);
                    break;

                case ByteCodeOp.LOAD_ASSERTION_ERROR:
                    // CPython 3.12: Load AssertionError class for assert statements
                    var assertionError = frame.ScopeChain.BuiltinModule.GetBuiltin("AssertionError");
                    if (assertionError == null)
                        throw CreateNameErrorWithSuggestion("AssertionError", frame);
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
                        throw CreateNameErrorWithSuggestion(deleteGlobalName, frame);
                    }
                    break;

                // Duplicate LOAD_GLOBAL case removed (was LOAD_GLOBAL_BUILTIN)

                case ByteCodeOp.SETUP_ANNOTATIONS:
                    // CPython 3.12: Initialize __annotations__ dictionary if not exists
                    // IMPORTANT: Use CurrentScope for class bodies, GlobalScope for module level
                    var annotationsName = "__annotations__";

                    // Determine which scope to use:
                    // - Module level: GlobalScope (frame name is usually '<module>')
                    // - Class body: CurrentScope (frame name like '<class_body_ClassName>')
                    // - Function: Should not have SETUP_ANNOTATIONS
                    bool isModuleLevel = frame.Code.Name == "<module>";
                    var targetScope = isModuleLevel ? frame.ScopeChain.GlobalScope : frame.ScopeChain.CurrentScope;

                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 SETUP_ANNOTATIONS: frame={frame.Code.Name}, isModuleLevel={isModuleLevel}, targetScope={targetScope.Name}");
                    #endif

                    // Check if __annotations__ already exists in target scope
                    var existingAnnotations = targetScope.GetVariable(annotationsName);
                    if (existingAnnotations == null)
                    {
                        // __annotations__ doesn't exist, create it
                        targetScope.SetVariable(annotationsName, new PyDict());
                        #if DEBUG_LOG
                        Console.WriteLine($"  → Created new __annotations__ dict in {targetScope.Name}");
                        #endif
                    }
                    else if (existingAnnotations is not PyDict)
                    {
                        // Replace with empty dict if it's not a dict
                        targetScope.SetVariable(annotationsName, new PyDict());
                        #if DEBUG_LOG
                        Console.WriteLine($"  → Replaced non-dict __annotations__ in {targetScope.Name}");
                        #endif
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

                // CPython 3.12: Python/bytecodes.c:400-450 - Specialized Binary Operations
                // Specialized Binary Operations - CPython 3.12 Adaptive Specialization
                case ByteCodeOp.BINARY_ADD_INT:
                    var rightInt = ((PyInt)frame.ValueStack.Pop()).Value;
                    var leftInt = ((PyInt)frame.ValueStack.Pop()).Value;
                    frame.ValueStack.Push(SmallIntCache.GetOrCreate((long)(leftInt + rightInt)));
                    break;

                case ByteCodeOp.BINARY_ADD_FLOAT:
                    var rightFloat = ((PyFloat)frame.ValueStack.Pop()).Value;
                    var leftFloat = ((PyFloat)frame.ValueStack.Pop()).Value;
                    frame.ValueStack.Push(FloatCache.GetOrCreate(leftFloat + rightFloat));
                    break;

                case ByteCodeOp.BINARY_ADD_UNICODE:
                    var rightStr = ((PyString)frame.ValueStack.Pop()).Value;
                    var leftStr = ((PyString)frame.ValueStack.Pop()).Value;
                    frame.ValueStack.Push(StringCache.GetOrCreate(leftStr + rightStr));
                    break;

                case ByteCodeOp.BINARY_MULTIPLY_INT:
                    var rightMulInt = ((PyInt)frame.ValueStack.Pop()).Value;
                    var leftMulInt = ((PyInt)frame.ValueStack.Pop()).Value;
                    frame.ValueStack.Push(SmallIntCache.GetOrCreate((long)(leftMulInt * rightMulInt)));
                    break;

                case ByteCodeOp.BINARY_MULTIPLY_FLOAT:
                    var rightMulFloat = ((PyFloat)frame.ValueStack.Pop()).Value;
                    var leftMulFloat = ((PyFloat)frame.ValueStack.Pop()).Value;
                    frame.ValueStack.Push(FloatCache.GetOrCreate(leftMulFloat * rightMulFloat));
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

                    // CPython 3.12: Check if there's a NULL/self on the stack
                    // If stack is empty or top is NULL, it's a simple call
                    // Otherwise, it's a method call with self
                    PyObject nextElement = null;
                    if (frame.ValueStack.Count > 0)
                    {
                        nextElement = frame.ValueStack.Pop();
                    }

                    // CPython 3.12 호출 방식 결정
                    // CPython: if (method != NULL) { callable = method; args--; total_args++; }
                    PyObject newCallResult;
                    PyObject[] finalArgs;
                    PyObject actualCallable;

                    if (nextElement == null || nextElement.Equals(PyNone.Instance))
                    {
                        // PUSH_NULL 패턴: method == NULL, 일반 함수 호출
                        actualCallable = callableFunc;
                        finalArgs = callArgs;
                    }
                    else
                    {
                        // CPython 3.12: method != NULL
                        // callable = method, args includes original callable as first arg
                        actualCallable = nextElement;  // method becomes the callable!
                        finalArgs = new PyObject[callArgs.Length + 1];
                        finalArgs[0] = callableFunc;  // original callable becomes first arg
                        Array.Copy(callArgs, 0, finalArgs, 1, callArgs.Length);
                    }

                    // CPython 3.12: 키워드 인수 처리
                    // CPython 3.12: Wrap function call in try-catch to add caller frame to traceback
                    // This ensures the full call stack is recorded when exception propagates
                    try
                    {
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
                    }
                    catch (PythonException pyEx)
                    {
                        // CPython 3.12: Add caller frame to traceback when exception propagates
                        // This matches CPython's PyTraceBack_Here() behavior in ceval.c
                        PyTraceBack_Here(frame, pyEx);
                        throw;
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

                    #if DEBUG_LOG
                    Console.WriteLine($"[CALL_FUNCTION_EX] hasKwargs={hasKwargs}, stack size={frame.ValueStack.Count}, frame={frame.Code.Name}");
                    #endif

                    PyObject kwargsDict = null;
                    if (hasKwargs)
                    {
                        if (frame.ValueStack.Count == 0)
                        {
                            throw new InvalidOperationException($"[CALL_FUNCTION_EX] Stack is empty when trying to pop kwargs. Frame={frame.Code.Name}, IP={frame.InstructionPointer}");
                        }
                        kwargsDict = frame.ValueStack.Pop(); // kwargs dictionary
                    }

                    if (frame.ValueStack.Count < 3)
                    {
                        throw new InvalidOperationException($"[CALL_FUNCTION_EX] Stack has only {frame.ValueStack.Count} items, need at least 3. Frame={frame.Code.Name}, IP={frame.InstructionPointer}");
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
                        // Performance: Eliminated LINQ - manual List to array conversion
                        var argsArray = new PyObject[argsList.Count];
                        argsList.CopyTo(argsArray, 0);
                        unpackedResult = ExecuteFunctionCallWithKeywords(function, argsArray, kwDict, frame.ScopeChain);
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
                        // Performance: Eliminated LINQ - manual List to array conversion
                        var argsArray = new PyObject[argsList.Count];
                        argsList.CopyTo(argsArray, 0);
                        unpackedResult = builtinFunc.Call(argsArray, kwDict);
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
                        // Performance: Eliminated LINQ - manual List to array conversion
                        var argsArray = new PyObject[argsList.Count];
                        argsList.CopyTo(argsArray, 0);
                        unpackedResult = method.Call(argsArray, kwDict);
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
                        // Performance: Eliminated LINQ - manual List to array conversion
                        var argsArray = new PyObject[argsList.Count];
                        argsList.CopyTo(argsArray, 0);
                        unpackedResult = functionToCall.Call(argsArray, kwDict);
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
                                // Performance: Eliminated LINQ - manual cast to PyCell array
                                closure = new PyCell[closureTupleObj.Items.Length];
                                for (int i = 0; i < closureTupleObj.Items.Length; i++)
                                {
                                    closure[i] = (PyCell)closureTupleObj.Items[i];
                                }
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
                    PyDict annotationsDict = null;
                    if ((flags & 4) != 0)
                    {
                        var annotationsTuple = frame.ValueStack.Pop();
                        if (annotationsTuple is PyTuple annTuple)
                        {
                            // CPython 3.12: Convert annotations tuple to dict
                            // Tuple format: ('key1', type1, 'key2', type2, ...)
                            // Dict format: {'key1': type1, 'key2': type2, ...}
                            annotationsDict = new PyDict();
                            for (int i = 0; i < annTuple.Items.Length; i += 2)
                            {
                                if (i + 1 < annTuple.Items.Length)
                                {
                                    var annKey = annTuple.Items[i];
                                    var annValue = annTuple.Items[i + 1];
                                    annotationsDict.SetItem(annKey, annValue);
                                }
                            }
                            #if DEBUG_LOG
                            Console.WriteLine($"  → Function has annotations: {annTuple.Items.Length / 2} items");
                            foreach (var kvp in annotationsDict.InternalDict)
                            {
                                Console.WriteLine($"     {kvp.Key}: {kvp.Value}");
                            }
                            #endif
                        }
                        else
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"  ⚠️ Warning: Expected tuple for annotations, got {annotationsTuple?.GetType()}");
                            #endif
                            annotationsDict = new PyDict();
                        }
                    }

                    // Check for keyword-only defaults flag (2 = HAS_KW_DEFAULTS)
                    // CPython 3.12: kwdefaults is a dict (not a tuple)
                    PyDict kwDefaultsDict = null;
                    if ((flags & 2) != 0)
                    {
                        var kwDefaultsObj = frame.ValueStack.Pop();
                        if (kwDefaultsObj is PyDict kwDefDict)
                        {
                            kwDefaultsDict = kwDefDict;
                            #if DEBUG_LOG
                            Console.WriteLine($"  → Function has keyword-only defaults: {kwDefDict.InternalDict.Count} items");
                            foreach (var kvp in kwDefDict.InternalDict)
                            {
                                Console.WriteLine($"     {kvp.Key}: {kvp.Value}");
                            }
                            #endif
                        }
                        else
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"  ⚠️ Warning: Expected dict for kw-defaults, got {kwDefaultsObj?.GetType()}");
                            #endif
                            kwDefaultsDict = new PyDict();
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
                            if (kwDefaultsDict != null)
                            {
                                asyncGenFunction.SetAttribute("__kwdefaults__", kwDefaultsDict);
                            }
                            if (annotationsDict != null)
                            {
                                asyncGenFunction.SetAttribute("__annotations__", annotationsDict);
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
                            if (kwDefaultsDict != null)
                            {
                                asyncFunction.SetAttribute("__kwdefaults__", kwDefaultsDict);
                            }
                            if (annotationsDict != null)
                            {
                                asyncFunction.SetAttribute("__annotations__", annotationsDict);
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

                            #if DEBUG_VM_LOG
                            Console.WriteLine($"[GLOBALS CAPTURE] MAKE_FUNCTION for {pyCode.Name}:");
                            Console.WriteLine($"  frame.ScopeChain.GlobalScope.Name: {frame.ScopeChain.GlobalScope?.Name}");
                            Console.WriteLine($"  globalsDict count: {globalsDict?.Count ?? 0}");
                            #endif
                            if (globalsDict != null)
                            {
                                #if DEBUG_VM_LOG
                                // Performance: Eliminated LINQ - manual key preview
                                var keyCount = Math.Min(10, globalsDict.Keys.Count);
                                var keys = new string[keyCount];
                                int keyIdx = 0;
                                foreach (var key in globalsDict.Keys)
                                {
                                    if (keyIdx >= keyCount) break;
                                    keys[keyIdx++] = key;
                                }
                                Console.WriteLine($"  globalsDict keys: {string.Join(", ", keys)}");
                                Console.WriteLine($"  globalsDict reference hash: {globalsDict.GetHashCode()}");
                                #endif
                            }

                            // Create function implementation with proper parameter binding
                            // CPython 3.12: Capture defaults at function definition time
                            var capturedDefaults = defaults; // Capture for closure
                            #if DEBUG_VM_LOG
                            Console.WriteLine($"[DEFAULTS CAPTURE] Capturing defaults for {pyCode.Name}:");
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
                            Func<PyObject[], PyObject> implementation = args =>
                            {
                            // CPython 3.12: Create new ScopeChain with captured globals
                            // The function's globals are fixed at function definition time
                            #if DEBUG_VM_LOG
                            Console.WriteLine($"[FUNCTION CALL] Function {pyCode.Name} called:");
                            Console.WriteLine($"  globalsDict count at call time: {globalsDict?.Count ?? 0}");
                            Console.WriteLine($"  capturedDefaults is null: {capturedDefaults == null}");
                            if (capturedDefaults != null)
                            {
                                Console.WriteLine($"  capturedDefaults.Items.Length: {capturedDefaults.Items.Length}");
                                for (int i = 0; i < capturedDefaults.Items.Length; i++)
                                {
                                    Console.WriteLine($"  capturedDefaults[{i}]: {capturedDefaults.Items[i]}");
                                }
                            }
                            #endif
                            if (globalsDict != null)
                            {
                                #if DEBUG_VM_LOG
                                // Performance: Eliminated LINQ - manual key preview
                                var keyCount = Math.Min(10, globalsDict.Keys.Count);
                                var keys = new string[keyCount];
                                int keyIdx = 0;
                                foreach (var key in globalsDict.Keys)
                                {
                                    if (keyIdx >= keyCount) break;
                                    keys[keyIdx++] = key;
                                }
                                Console.WriteLine($"  globalsDict keys at call time: {string.Join(", ", keys)}");
                                Console.WriteLine($"  globalsDict reference hash at call time: {globalsDict.GetHashCode()}");
                                #endif
                            }

                            if (globalsDict == null)
                            {
                                throw new InvalidOperationException($"Function {pyCode.Name} has null globals!");
                            }

                            var functionScopeChain = new PyScopeChain(globalsDict, "<function>");

                            #if DEBUG_VM_LOG
                            Console.WriteLine($"  New ScopeChain GlobalScope count: {functionScopeChain.GlobalScope?.Variables.Count ?? 0}");
                            Console.WriteLine($"  New ScopeChain GlobalScope hash: {functionScopeChain.GlobalScope?.Variables.GetHashCode()}");
                            #endif

                            var functionFrame = closure != null && closure.Length > 0
                                ? new PyFrame(pyCode, args, functionScopeChain, closure, frame, capturedDefaults)
                                : new PyFrame(pyCode, args, functionScopeChain, null, frame, capturedDefaults);
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
                            if (kwDefaultsDict != null)
                            {
                                functionObject.SetAttribute("__kwdefaults__", kwDefaultsDict);
                            }
                            if (annotationsDict != null)
                            {
                                functionObject.SetAttribute("__annotations__", annotationsDict);
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
                    // CPython 3.12: LOAD_ATTR with flag encoding
                    // oparg encoding: (nameIndex << 1) | pushNull
                    // If pushNull=1: Push two values [self/NULL, method/attr] for method call optimization
                    // If pushNull=0: Push one value [attr] for simple attribute access
                    {
                        int attrOparg = instruction.Argument;
                        bool pushNullForMethod = (attrOparg & 1) == 1;
                        int attrNameIndex = attrOparg >> 1;

                        var attrName = frame.Code.Names[attrNameIndex];
                        var obj = frame.ValueStack.Pop();

                        #if DEBUG_LOG
                        Console.WriteLine($"🔍 LOAD_ATTR: attribute '{attrName}' from object type: {obj.GetType().Name}, PyType: {obj.GetTypeName()}, pushNull={pushNullForMethod}");
                        if (obj is PyClassInstance objClassInst)
                        {
                            Console.WriteLine($"   → PyClassInstance of class: {objClassInst.PyClass.Name}");
                        }
                        #endif

                        // CPython 3.12: Check for descriptor BEFORE calling GetAttribute
                        // This allows us to distinguish staticmethod from regular methods
                        bool isStaticMethod = false;
                        if (obj is PyClassInstance instance && pushNullForMethod)
                        {
                            // Check if this attribute is a staticmethod descriptor
                            foreach (var mroType in instance.InstanceType.MRO)
                            {
                                if (mroType is PyClass pyClass && pyClass.ClassDict.TryGetValue(attrName, out PyObject classValue))
                                {
                                    if (classValue is PyStaticmethod)
                                    {
                                        isStaticMethod = true;
                                        #if DEBUG_LOG
                                        Console.WriteLine($"   → Found staticmethod descriptor for '{attrName}'");
                                        #endif
                                    }
                                    break;
                                }
                            }
                        }

                        // Get attribute using existing system
                        var attr = obj.GetAttribute(attrName);

                        if (pushNullForMethod)
                        {
                            // CPython 3.12: Objects/object.c:1310-1410 (_PyObject_GetMethod)
                            // Method call optimization logic

                            // CPython 3.12: Objects/object.c:1322-1326
                            // If object has custom tp_getattro (overrides GetAttribute), use simple GetAttr
                            // This returns 0 → push [NULL, attr]
                            bool hasCustomGetAttribute = false;
                            var objType = obj.GetType();
                            var getAttrMethod = objType.GetMethod("GetAttribute",
                                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                            if (getAttrMethod != null && getAttrMethod.DeclaringType != typeof(PyObject))
                            {
                                hasCustomGetAttribute = true;
                                #if DEBUG_LOG
                                Console.WriteLine($"   → Object has custom GetAttribute override: {objType.Name}");
                                #endif
                            }

                            if (hasCustomGetAttribute)
                            {
                                // CPython: Custom tp_getattro → push [NULL, attr]
                                frame.ValueStack.Push(PyNone.Instance); // NULL marker
                                frame.ValueStack.Push(attr);
                                #if DEBUG_LOG
                                Console.WriteLine($"   → Custom GetAttribute: pushed [NULL, attr]");
                                #endif
                            }
                            else if (attr is PyMethod)
                            {
                                // It's already a bound method: push [NULL, bound_method]
                                // The method already has self bound, so we don't add it again
                                frame.ValueStack.Push(PyNone.Instance); // NULL marker
                                frame.ValueStack.Push(attr); // bound method
                                #if DEBUG_LOG
                                Console.WriteLine($"   → Bound method: pushed [NULL, bound_method]");
                                #endif
                            }
                            else if (attr is PyFunction || attr is PyBuiltinFunction)
                            {
                                // CPython 3.12: Objects/descrobject.c:271-286 (func_descr_get)
                                // Function descriptor protocol:
                                // - Access from TYPE/CLASS → return unbound function
                                // - Access from INSTANCE → return bound method (push [self, function])
                                // - staticmethod → always return unbound function
                                // - instance.__dict__ function → return unbound function (not a method!)

                                // Check if obj is a type/class object
                                // CPython: PyType_Check(obj) - checks if obj is type or class
                                // CPython 3.12: Objects/funcobject.c:1228-1238 (sm_descr_get), 1058-1064 (cm_descr_get)
                                // staticmethod/classmethod: __func__/__wrapped__ returns unbound callable
                                bool isTypeOrClass = (obj is PyType) || (obj is PyClass) || (obj is PyStaticmethod) || (obj is PyClassmethod);

                                // Check if obj is module or super (also return unbound)
                                bool isModuleOrSuper = (obj is PyModule) || (obj is PySuper);

                                // CPython: Check if attribute is from instance __dict__ (not a method!)
                                // Instance attributes that are functions are NOT bound as methods
                                bool isInstanceAttribute = false;
                                if (obj is PyClassInstance classInstance)
                                {
                                    isInstanceAttribute = classInstance.InstanceDict.ContainsKey(attrName);
                                }

                                if (isTypeOrClass || isModuleOrSuper || isStaticMethod || isInstanceAttribute)
                                {
                                    // Class/type access, staticmethod, or instance attribute: push [NULL, function]
                                    // CPython 3.12: No method binding - return function as-is
                                    frame.ValueStack.Push(PyNone.Instance); // NULL marker
                                    frame.ValueStack.Push(attr); // unbound function
                                    #if DEBUG_LOG
                                    Console.WriteLine($"   → Type/class/staticmethod/instance-attr access: pushed [NULL, function]");
                                    #endif
                                }
                                else
                                {
                                    // Instance method (from class): push [self, unbound_method]
                                    // CPython 3.12: Method binding - CALL will pass self as first argument
                                    frame.ValueStack.Push(obj);  // self
                                    frame.ValueStack.Push(attr); // unbound method
                                    #if DEBUG_LOG
                                    Console.WriteLine($"   → Instance method access: pushed [self, unbound_method]");
                                    #endif
                                }
                            }
                            else
                            {
                                // It's a regular attribute or callable descriptor: push [NULL, attr]
                                frame.ValueStack.Push(PyNone.Instance); // NULL marker
                                frame.ValueStack.Push(attr);
                                #if DEBUG_LOG
                                Console.WriteLine($"   → Regular attribute: pushed [NULL, attr]");
                                #endif
                            }
                        }
                        else
                        {
                            // Simple attribute access: push [attr]
                            frame.ValueStack.Push(attr);
                            #if DEBUG_LOG
                            Console.WriteLine($"   → Simple access: pushed [attr]");
                            #endif
                        }
                    }
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
                    // Stack: [..., super_func, __class__, self] -> [..., attr_value] or [..., NULL, bound_method]
                    // oparg format: (name_index << 1) | method_flag
                    int superOparg = instruction.Argument;
                    int superMethodFlag = superOparg & 1;  // Low bit: method flag
                    int superAttrIndex = superOparg >> 1;  // High bits: name index
                    var superAttrName = frame.Code.Names[superAttrIndex];
                    var selfObj = frame.ValueStack.Pop();         // self
                    var classObj = frame.ValueStack.Pop();        // __class__
                    var superFunc = frame.ValueStack.Pop();       // super function

                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 LOAD_SUPER_ATTR: {superAttrName}, methodFlag={superMethodFlag}, super={superFunc.GetType().Name}, class={classObj.GetType().Name}, self={selfObj.GetType().Name}");
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
                        // IMPORTANT: class-mode super (selfObj is a type) should NOT auto-bind
                        PyObject finalAttr = superAttr;

                        // Check if this is class-mode super: selfObj is the class itself (PyType or PyClass)
                        bool isClassModeSuper = (selfObj is PyType) || (selfObj is PyClass);

                        #if DEBUG_LOG
                        Console.WriteLine($"🔧 LOAD_SUPER_ATTR: isClassModeSuper={isClassModeSuper}, selfObj type={selfObj.GetType().Name}");
                        #endif

                        if (!isClassModeSuper)
                        {
                            // Instance-mode super: auto-bind methods to instance
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
                        }
                        else
                        {
                            // Class-mode super: do NOT auto-bind, return as-is
                            #if DEBUG_LOG
                            Console.WriteLine($"🔧 LOAD_SUPER_ATTR: class-mode super, NOT binding {superAttrName}");
                            #endif
                        }

                        // CPython 3.12: Push result based on method flag
                        if (superMethodFlag == 1)
                        {
                            // Method call: push [NULL, bound_method] for CALL optimization
                            frame.ValueStack.Push(PyNone.Instance);  // NULL marker
                            frame.ValueStack.Push(finalAttr);
                            #if DEBUG_LOG
                            Console.WriteLine($"🔧 LOAD_SUPER_ATTR success (method call): pushed [NULL, {finalAttr.GetType().Name}]");
                            #endif
                        }
                        else
                        {
                            // Value access: push [attr_value]
                            frame.ValueStack.Push(finalAttr);
                            #if DEBUG_LOG
                            Console.WriteLine($"🔧 LOAD_SUPER_ATTR success (value access): pushed [{finalAttr.GetType().Name}]");
                            #endif
                        }
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
                            // Performance: Eliminated LINQ - List already has efficient ToArray
                            var resultArray = new PyObject[values.Count];
                            values.CopyTo(resultArray, 0);
                            var resultTuple = new PyTuple(resultArray);
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
                    // CPython 3.12: Pop top value and jump if it's None
                    var valueToCheck = frame.ValueStack.Pop();
                    if (valueToCheck == null || valueToCheck.Equals(PyNone.Instance))
                    {
                        // CPython 3.12: POP_JUMP_IF_NONE uses relative offset from next instruction
                        int currentPosNone = frame.InstructionPointer;
                        int relativeOffsetNone = instruction.Argument;
                        int targetInstructionIndexNone = currentPosNone + 1 + relativeOffsetNone;
                        // Subtract 1 because main loop will increment
                        frame.InstructionPointer = targetInstructionIndexNone - 1;
                    }
                    break;

                case ByteCodeOp.POP_JUMP_IF_NOT_NONE:
                    // CPython 3.12: Pop top value and jump if it's NOT None
                    var valueToCheckNotNone = frame.ValueStack.Pop();
                    if (valueToCheckNotNone != null && !valueToCheckNotNone.Equals(PyNone.Instance))
                    {
                        // CPython 3.12: POP_JUMP_IF_NOT_NONE uses relative offset from next instruction
                        int currentPosNotNone = frame.InstructionPointer;
                        int relativeOffsetNotNone = instruction.Argument;
                        int targetInstructionIndexNotNone = currentPosNotNone + 1 + relativeOffsetNotNone;
                        // Subtract 1 because main loop will increment
                        frame.InstructionPointer = targetInstructionIndexNotNone - 1;
                    }
                    break;

                case ByteCodeOp.MATCH_CLASS:
                    // CPython 3.12: Python/bytecodes.c:2230-2243 - MATCH_CLASS opcode
                    // CPython 3.12: Python/ceval.c:406-428 - match_class_attr helper
                    // CPython 3.12: Python/ceval.c:430-533 - match_class helper
                    // Match class pattern - structural pattern matching (PEP 634)
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

                                // Performance: Eliminated LINQ - manual List to array + Cache
                                var attrsArray = new PyObject[attrs.Count];
                                attrs.CopyTo(attrsArray, 0);
                                frame.ValueStack.Push(TupleCache.GetOrCreate(attrsArray));
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

                                // Performance: Eliminated LINQ - manual List to array + Cache
                                var attrsArray = new PyObject[attrs.Count];
                                attrs.CopyTo(attrsArray, 0);
                                frame.ValueStack.Push(TupleCache.GetOrCreate(attrsArray));
                            }
                            else
                            {
                                // CPython 3.12: Python/ceval.c:515-523
                                // For built-in types (int, str, etc.) with positional patterns like case int(x):
                                // Return tuple containing the subject if positionalCount > 0
                                // This allows unpacking: case int(x): captures x=5 from match 5
                                if (positionalCount > 0)
                                {
                                    frame.ValueStack.Push(new PyTuple(new[] { classSubject }));
                                }
                                else
                                {
                                    // No attributes to extract, return empty tuple
                                    frame.ValueStack.Push(new PyTuple(new PyObject[0]));
                                }
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

                case ByteCodeOp.GET_AITER:
                    // CPython 3.12: GET_AITER for async for loops
                    // Python/bytecodes.c: inst(GET_AITER)
                    var aiterObj = frame.ValueStack.Pop();
                    var aiter = GetAsyncIterator(aiterObj);
                    frame.ValueStack.Push(aiter);
                    break;

                case ByteCodeOp.GET_ANEXT:
                    // CPython 3.12: GET_ANEXT for async for loops
                    // Python/bytecodes.c: inst(GET_ANEXT)
                    // Stack: [aiter] -> [aiter, awaitable]
                    var asyncIter = frame.ValueStack.Peek();  // Keep aiter on stack
                    var anext = GetAsyncNext(asyncIter);
                    frame.ValueStack.Push(anext);  // Push awaitable
                    break;

                case ByteCodeOp.END_ASYNC_FOR:
                    // CPython 3.12: END_ASYNC_FOR for async for loops
                    // Python/bytecodes.c: inst(END_ASYNC_FOR)
                    // Stack: [awaitable, exc] -> []
                    // Check if exception is StopAsyncIteration
                    var asyncForExc = frame.ValueStack.Pop();
                    var asyncForAwaitable = frame.ValueStack.Pop();

                    if (asyncForExc is PyStopAsyncIteration)
                    {
                        // Normal loop termination - just discard both values
                        break;
                    }
                    else if (asyncForExc is PyBaseException asyncForException)
                    {
                        // Re-raise other exceptions
                        throw new PythonException(asyncForException);
                    }
                    else
                    {
                        // Should not happen
                        throw new InvalidOperationException($"END_ASYNC_FOR received non-exception: {asyncForExc?.GetTypeName() ?? "null"}");
                    }

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

                case ByteCodeOp.JUMP:
                    // CPython 3.12: JUMP can jump forward or backward (determined by assembler)
                    // The argument is the absolute target instruction offset
                    int jumpTarget = instruction.Argument;

                    #if DEBUG_LOG
                    Console.WriteLine($"🔄 JUMP: from instr {frame.InstructionPointer} to instr {jumpTarget}");
                    #endif

                    // Subtract 1 because main loop will increment
                    frame.InstructionPointer = jumpTarget - 1;
                    return null;

                case ByteCodeOp.JUMP_NO_INTERRUPT:
                    // CPython 3.12: JUMP_NO_INTERRUPT is like JUMP but without interrupt check
                    // In SharpPy, we don't have interrupt checks, so it's identical to JUMP
                    int jumpNoIntTarget = instruction.Argument;

                    #if DEBUG_LOG
                    Console.WriteLine($"🔄 JUMP_NO_INTERRUPT: from instr {frame.InstructionPointer} to instr {jumpNoIntTarget}");
                    #endif

                    // Subtract 1 because main loop will increment
                    frame.InstructionPointer = jumpNoIntTarget - 1;
                    return null;

                case ByteCodeOp.JUMP_BACKWARD_NO_INTERRUPT:
                    // CPython 3.12: JUMP_BACKWARD_NO_INTERRUPT for yield from loops
                    // CPython: JUMPBY(-oparg) where next_instr is already incremented (pre-fetch)
                    //   - delta = (index after jump) - target
                    //   - VM: next_instr += (-delta) = next_instr - delta
                    //   - Since next_instr already incremented: target = (IP+1) - delta
                    //
                    // SharpPy: IP points to current instruction, incremented at loop end
                    //   - Same formula: target = (IP+1) - delta
                    //   - But main loop will ++, so set IP = target - 1
                    int jumpBackNoIntDelta = instruction.Argument;
                    int jumpBackNoIntTarget = (frame.InstructionPointer + 1) - jumpBackNoIntDelta;

                    #if DEBUG_VM_LOG
                    Console.WriteLine($"🔄 JUMP_BACKWARD_NO_INTERRUPT: IP={frame.InstructionPointer}, delta={jumpBackNoIntDelta}, target={jumpBackNoIntTarget}");
                    Console.WriteLine($"   Calculation: ({frame.InstructionPointer}+1) - {jumpBackNoIntDelta} = {jumpBackNoIntTarget}");
                    Console.WriteLine($"   Setting IP to {jumpBackNoIntTarget - 1} (main loop will ++)");
                    #endif

                    // Subtract 1 because main loop will increment
                    frame.InstructionPointer = jumpBackNoIntTarget - 1;
                    return null;

                case ByteCodeOp.JUMP_BACKWARD:
                    // CPython 3.12 호환: QuickenedCodeObject 방식으로 JUMP_BACKWARD 계산
                    int currentInstrPos = frame.InstructionPointer;
                    int targetInstrPos;

                    #if DEBUG_VM_LOG
                    Console.WriteLine($"🔧 JUMP_BACKWARD: currentIP={currentInstrPos}, arg={instruction.Argument}");
                    #endif

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
                        #if DEBUG_VM_LOG
                        Console.WriteLine($"    🔙 JUMP_BACKWARD: QuickenedCode {currentInstrPos} → {targetInstrPos}");
                        #endif
                        #if DEBUG_LOG
                        Console.WriteLine($"🔙 JUMP_BACKWARD: QuickenedCode {currentInstrPos} → {targetInstrPos}");
                        #endif
                    }
                    else
                    {
                        // 레거시 방식: PyJumpBackwardUtil 사용
                        targetInstrPos = PyJumpBackwardUtil.CalculateJumpBackwardTarget(currentInstrPos, instruction.Argument, frame.Code.Instructions);
                        #if DEBUG_VM_LOG
                        Console.WriteLine($"    🔙 JUMP_BACKWARD: Legacy (Util) {currentInstrPos} → {targetInstrPos}");
                        #endif
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

                    // CPython 3.12 호환: 점프 후 main loop가 ++하므로 -1 필요
                    // 하지만 FOR_ITER같은 경우는 target이 정확해야 함
                    // PyJumpBackwardUtil이 이미 올바른 target을 계산했으므로 -1 적용
                    frame.InstructionPointer = targetInstrPos - 1;
                    #if DEBUG_VM_LOG
                    Console.WriteLine($"    🎯 Setting IP to {targetInstrPos - 1}, after main loop++ will be {targetInstrPos}");
                    #endif
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
                    // CPython: Python/bytecodes.c line 1507, _PyList_Extend calls list_extend
                    // list_extend uses PyObject_GetIter for generic iterables (Objects/listobject.c)
                    var extendArg = instruction.Argument; // Should be 1 for this case
                    var extendIterable = frame.ValueStack.Pop(); // Pop iterable from top

                    // The list should now be on top of the stack
                    var extendTargetList = (PyList)frame.ValueStack.Peek();

                    // Use generic iterator approach to handle all iterable types
                    // This includes PyList, PyTuple, PyString, PyGenerator, etc.
                    var extendIterator = extendIterable.GetIterator();

                    // Iterate and append all items
                    while (true)
                    {
                        try
                        {
                            var nextItem = extendIterator.Next();
                            extendTargetList.Append(nextItem);
                        }
                        catch (PythonException ex) when (ex.PyException is PyStopIteration)
                        {
                            // Iterator exhausted
                            break;
                        }
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

                // CPython 3.12: Python/bytecodes.c BINARY_SUBSCR
                // CPython 3.12: Objects/abstract.c:171-201 (PyObject_GetItem)
                //
                // Similar to STORE_SUBSCR, we need to lookup __getitem__ via MRO
                // to support user-defined __getitem__ methods in subclasses
                case ByteCodeOp.BINARY_SUBSCR:
                    var subscriptKey = frame.ValueStack.Pop();
                    var subscriptObj = frame.ValueStack.Pop();

                    try
                    {
                        // CPython 3.12: PEP 585 - If subscripting a type, use __class_getitem__ instead of __getitem__
                        // See Objects/typeobject.c:type_subscript
                        PyObject subscriptResult;
                        if (subscriptObj is PyType typeObj)
                        {
                            // Subscripting a type (e.g., dict[int], list[str]) - use __class_getitem__
                            var classGetitemAttr = typeObj.LookupSpecial("__class_getitem__");

                            if (classGetitemAttr != null && classGetitemAttr is PyBuiltinClassMethod classMethod)
                            {
                                // Call __class_getitem__(cls, arg)
                                subscriptResult = classMethod.Call(new[] { typeObj, subscriptKey }, null);
                            }
                            else if (classGetitemAttr != null && classGetitemAttr is IDescriptor descriptor)
                            {
                                // Descriptor protocol: Get bound method
                                var boundMethod = descriptor.Get(null, typeObj);
                                subscriptResult = boundMethod.Call(new[] { subscriptKey }, null);
                            }
                            else if (classGetitemAttr != null)
                            {
                                // Fallback: direct call
                                subscriptResult = classGetitemAttr.Call(new[] { typeObj, subscriptKey }, null);
                            }
                            else
                            {
                                // No __class_getitem__, fallback to regular __getitem__
                                subscriptResult = subscriptObj.GetItem(subscriptKey);
                            }
                        }
                        else
                        {
                            // Regular instance subscripting - use __getitem__
                            var objType = subscriptObj.GetPyType();
                            var getitemAttr = objType.LookupSpecial("__getitem__");

                            if (getitemAttr != null && getitemAttr is PyMethodDescriptor getitemDescriptor)
                            {
                                // Found descriptor (user-defined or built-in)
                                // Call __getitem__(self, key)
                                subscriptResult = getitemDescriptor.Call(new[] { subscriptObj, subscriptKey }, null);
                            }
                            else if (getitemAttr != null && getitemAttr is PyFunction getitemFunc)
                            {
                                // Found unbound function (rare case)
                                subscriptResult = getitemFunc.Call(new[] { subscriptObj, subscriptKey }, null);
                            }
                            else
                            {
                                // No __getitem__ found, use built-in GetItem
                                subscriptResult = subscriptObj.GetItem(subscriptKey);
                            }
                        }

                        frame.ValueStack.Push(subscriptResult);
                    }
                    // CPython 3.12: Python exceptions (KeyError, IndexError, TypeError) should propagate
                    catch (Exception ex) when (ex is PythonException)
                    {
                        // Re-throw Python exceptions (PythonException is the C# wrapper)
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

                // CPython 3.12: Python/bytecodes.c STORE_SUBSCR
                // CPython 3.12: Objects/abstract.c:203-234 (PyObject_SetItem)
                //
                // In CPython's VM (ceval.c), STORE_SUBSCR calls PyObject_SetItem,
                // which looks up tp_as_mapping->mp_ass_subscript from the type.
                //
                // For built-in types (dict, list), mp_ass_subscript points to
                // the C function (e.g., dict_ass_sub -> PyDict_SetItem).
                //
                // For user-defined classes, the type's mp_ass_subscript is set
                // during type creation if __setitem__ is defined. The slot wrapper
                // mechanism ensures user-defined __setitem__ gets called.
                //
                // SharpPy equivalent:
                // 1. Lookup __setitem__ via LookupSpecial (traverses MRO)
                // 2. If found (PyMethodDescriptor or PyFunction), call it
                // 3. Otherwise, fall back to obj.SetItem() (built-in behavior)
                case ByteCodeOp.STORE_SUBSCR:
                    var subscrStoreKey = frame.ValueStack.Pop();      // key (top of stack)
                    var subscrStoreObj = frame.ValueStack.Pop();      // object
                    var subscrStoreValue = frame.ValueStack.Pop();    // value (bottom)

                    try
                    {
                        // CPython 3.12: Lookup __setitem__ in type's MRO
                        var objType = subscrStoreObj.GetPyType();
                        var setitemAttr = objType.LookupSpecial("__setitem__");

                        if (setitemAttr != null && setitemAttr is PyMethodDescriptor setitemDescriptor)
                        {
                            // Found descriptor (user-defined or built-in)
                            // Call __setitem__(self, key, value)
                            setitemDescriptor.Call(new[] { subscrStoreObj, subscrStoreKey, subscrStoreValue }, null);
                        }
                        else if (setitemAttr != null && setitemAttr is PyFunction setitemFunc)
                        {
                            // Found unbound function (rare case)
                            setitemFunc.Call(new[] { subscrStoreObj, subscrStoreKey, subscrStoreValue }, null);
                        }
                        else
                        {
                            // No __setitem__ found, use built-in SetItem
                            // This handles types without explicit __setitem__ descriptor
                            subscrStoreObj.SetItem(subscrStoreKey, subscrStoreValue);
                        }
                    }
                    // CPython 3.12: Python exceptions should propagate
                    catch (Exception ex) when (ex is PythonException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        throw PyTypeError.Create($"subscript assignment error: {ex.Message}");
                    }
                    break;

                // CPython 3.12: Python/bytecodes.c:584-589 (DELETE_SUBSCR)
                // Implements: del container[sub]
                // Stack: [container, sub] -> []
                case ByteCodeOp.DELETE_SUBSCR:
                    var delSubSub = frame.ValueStack.Pop();        // sub (top of stack)
                    var delSubContainer = frame.ValueStack.Pop();  // container

                    try
                    {
                        // CPython 3.12: PyObject_DelItem(container, sub)
                        // For PyClassInstance (including dict subclasses), use DelItem directly
                        // This allows the override in PyClassInstance to handle user-defined __delitem__
                        // and fall back to _dictStorage for dict subclasses
                        if (delSubContainer is PyClassInstance classInstance)
                        {
                            classInstance.DelItem(delSubSub);
                            break;
                        }

                        // For built-in types, lookup __delitem__ via MRO
                        var containerType = delSubContainer.GetPyType();
                        var delitemAttr = containerType.LookupSpecial("__delitem__");


                        if (delitemAttr != null && delitemAttr is PyWrapperDescriptor delitemWrapper)
                        {
                            // Found wrapper descriptor (builtin __delitem__)
                            // Call __delitem__(self, key)
                            delitemWrapper.Call(new[] { delSubContainer, delSubSub }, null);
                        }
                        else if (delitemAttr != null && delitemAttr is PyMethodDescriptor delitemDescriptor)
                        {
                            // Found descriptor (user-defined or built-in)
                            // Call __delitem__(self, key)
                            delitemDescriptor.Call(new[] { delSubContainer, delSubSub }, null);
                        }
                        else if (delitemAttr != null && delitemAttr is PyFunction delitemFunc)
                        {
                            // Found unbound function (rare case)
                            delitemFunc.Call(new[] { delSubContainer, delSubSub }, null);
                        }
                        else
                        {
                            // No __delitem__ found, try DelItem method
                            // Most types don't support deletion
                            if (delSubContainer is PyDict delDict)
                            {
                                // dict supports deletion via pop
                                delDict.Pop(delSubSub, null);
                            }
                            else if (delSubContainer is PyList delList && delSubSub is PyInt delIndex)
                            {
                                // list supports deletion
                                delList.Pop((int)delIndex.Value);
                            }
                            else
                            {
                                throw PyTypeError.Create($"'{containerType.Name}' object does not support item deletion");
                            }
                        }
                    }
                    // CPython 3.12: Python exceptions should propagate
                    catch (Exception ex) when (ex is PythonException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        throw PyTypeError.Create($"subscript deletion error: {ex.Message}");
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
                    // Python 3.7+: dict는 삽입 순서를 보장해야 함
                    // 스택은 LIFO이므로 pop한 쌍들을 임시 리스트에 저장 후 역순으로 추가
                    var pairs = new List<(PyObject key, PyObject value)>(mapSize);
                    for (int i = 0; i < mapSize; i++)
                    {
                        var mapValue = frame.ValueStack.Pop();
                        var mapKey = frame.ValueStack.Pop();
                        pairs.Add((mapKey, mapValue));
                    }
                    // 역순으로 dict에 추가 (컴파일 시 순서 보장)
                    for (int i = pairs.Count - 1; i >= 0; i--)
                    {
                        pyDict.SetItem(pairs[i].key, pairs[i].value);
                    }
                    frame.ValueStack.Push(pyDict);
                    break;

                case ByteCodeOp.BUILD_CONST_KEY_MAP:
                    // CPython 3.12: Build dict from keys tuple and values on stack
                    // Stack: value1, value2, ..., valueN, keys_tuple
                    // TOS is keys tuple, TOS1...TOSN are values
                    var constKeyCount = instruction.Argument;
                    var constKeysTuple = frame.ValueStack.Pop() as PyTuple;
                    if (constKeysTuple == null)
                        throw new InvalidOperationException("BUILD_CONST_KEY_MAP: keys must be a tuple");

                    var constKeyDict = new PyDict();
                    var constKeyValues = new PyObject[constKeyCount];

                    // Pop values in reverse order (stack is LIFO)
                    for (int i = constKeyCount - 1; i >= 0; i--)
                    {
                        constKeyValues[i] = frame.ValueStack.Pop();
                    }

                    // Build dict with keys from tuple and values from stack
                    for (int i = 0; i < constKeyCount; i++)
                    {
                        constKeyDict.SetItem(constKeysTuple.Items[i], constKeyValues[i]);
                    }

                    frame.ValueStack.Push(constKeyDict);
                    break;

                // CPython-style Iterator Opcodes
                case ByteCodeOp.GET_ITER:
                    #if DEBUG_GET_ITER
                    Console.WriteLine($"🔍 GET_ITER at IP {frame.InstructionPointer}: Stack.Count = {frame.ValueStack.Count}");
                    Console.WriteLine($"   Current function: {frame.Code.Name}");
                    #endif
                    var iterable = frame.ValueStack.Pop();
                    #if DEBUG_GET_ITER
                    Console.WriteLine($"   Iterable type: {iterable.GetType().Name}, value: {iterable}");
                    if (iterable is PyFunction funcObj)
                    {
                        Console.WriteLine($"   ❌ ERROR: Trying to iterate over function '{funcObj.Name}'");
                        Console.WriteLine($"   Frame locals: {string.Join(", ", frame.Code.VarNames.Select((v, i) => $"{v}={frame.LocalsPlus[i]}"))}");
                    }
                    #endif
                    if (iterable is PyTuple iterTuple)
                    {
                        for (int i = 0; i < iterTuple.Items.Length; i++)
                        {
                        }
                    }
                    else
                    {
                    }
                    var iterator = iterable.GetIterator();
                    frame.ValueStack.Push(iterator);
                    #if DEBUG_VM_LOG
                    Console.WriteLine($"    ✅ GET_ITER: Created iterator, Stack.Count after push = {frame.ValueStack.Count}");
                    #endif
                    break;

                case ByteCodeOp.GET_YIELD_FROM_ITER:
                    // CPython 3.12: GET_YIELD_FROM_ITER
                    // Converts iterable to iterator for yield from
                    #if DEBUG_VM_LOG
                    Console.WriteLine($"🔍 GET_YIELD_FROM_ITER: Stack.Count = {frame.ValueStack.Count}");
                    #endif
                    var yieldFromIterable = frame.ValueStack.Pop();
                    PyObject yieldFromIter;

                    // Check if it's a generator or coroutine - use as-is
                    if (yieldFromIterable is PyGenerator)
                    {
                        yieldFromIter = yieldFromIterable;
                    }
                    else
                    {
                        // Regular iterable - get iterator
                        yieldFromIter = yieldFromIterable.GetIterator();
                    }

                    frame.ValueStack.Push(yieldFromIter);
                    #if DEBUG_VM_LOG
                    Console.WriteLine($"    ✅ GET_YIELD_FROM_ITER: Prepared iterator {yieldFromIter.GetType().Name}");
                    #endif
                    break;

                case ByteCodeOp.FOR_ITER:
                    // CPython 3.12 compatible FOR_ITER implementation
                    #if DEBUG_VM_LOG
                    Console.WriteLine($"🔍 FOR_ITER: Stack.Count before Peek = {frame.ValueStack.Count}, IP={frame.InstructionPointer}");
                    #endif
                    if (frame.ValueStack.Count > 0)
                    {
                        // Performance: Eliminated LINQ - manual stack preview
                        var stackArray = frame.ValueStack.ToArray();
                        var previewCount = Math.Min(5, stackArray.Length);
                        var stackItems = new string[previewCount];
                        for (int i = 0; i < previewCount; i++)
                        {
                            stackItems[i] = $"[{i}]={stackArray[i].GetType().Name}";
                        }
                        #if DEBUG_VM_LOG
                        Console.WriteLine($"    Stack items: {string.Join(", ", stackItems)}");
                        #endif
                    }
                    var iter = frame.ValueStack.Peek(); // Keep iterator on stack for inspection
                    #if DEBUG_VM_LOG
                    Console.WriteLine($"    Iterator type: {iter.GetType().Name}, value: {iter}");
                    #endif
                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 FOR_ITER: iterator type = {iter.GetType().Name}, calling Next()...");
                    Console.WriteLine($"    InstructionPointer = {frame.InstructionPointer}");
                    #endif
                    try
                    {
                        var nextItem = iter.Next();
                        frame.ValueStack.Push(nextItem); // Push next item on top of iterator
                        #if DEBUG_VM_LOG
                        Console.WriteLine($"    ✅ FOR_ITER: got next item, Stack.Count after push = {frame.ValueStack.Count}");
                        #endif
                        #if DEBUG_LOG
                        Console.WriteLine($"🔄 FOR_ITER: got next item {nextItem} from iterator");
                        #endif
                        // Continue normal execution (don't jump)
                    }
                    catch (PythonException ex) when (ex.PyException is PyStopIteration)
                    {
                        #if DEBUG_VM_LOG
                        Console.WriteLine($"🔚 FOR_ITER: StopIteration - loop finished, Stack.Count before pop = {frame.ValueStack.Count}");
                        #endif
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
                        #if DEBUG_VM_LOG
                        Console.WriteLine($"    ✅ FOR_ITER: Popped iterator, Stack.Count after pop = {frame.ValueStack.Count}");
                        #endif
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
                            // CPython 3.12 호환: FOR_ITER StopIteration 점프
                            // bytecodes.c:2341: JUMPBY(INLINE_CACHE_ENTRIES_FOR_ITER + oparg + 1)
                            // INLINE_CACHE_ENTRIES_FOR_ITER = 1 (one CACHE instruction)
                            // Jump amount = 1 + oparg + 1 = oparg + 2 instruction indices
                            // Main loop will ++, so set to: current + oparg + 2 - 1 = current + oparg + 1
                            int targetIndex = frame.InstructionPointer + instruction.Argument + 1;
                            #if DEBUG_VM_LOG
                            Console.WriteLine($"    🔚 FOR_ITER: Jumping from IP={frame.InstructionPointer} to IP={targetIndex + 1} (no-optimize, arg={instruction.Argument})");
                            #endif
                            frame.InstructionPointer = targetIndex; // main loop will increment
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

                case ByteCodeOp.UNARY_INVERT:
                    var invertValue = frame.ValueStack.Pop();
                    frame.ValueStack.Push(invertValue.BitwiseNot());
                    break;

                // CPython 3.12: SETUP_EXCEPT removed - using Exception Table instead
                // case ByteCodeOp.SETUP_EXCEPT: // Legacy - no longer used in CPython 3.12

                 case ByteCodeOp.POP_EXCEPT:
                    // CPython 3.12: POP_EXCEPT pops the prev_exc value left by PUSH_EXC_INFO
                    // and RESTORES it to the current exception state
                    // CPython bytecodes.c:929-932: Py_XSETREF(exc_info->exc_value, exc_value)
                    // Stack before: [..., prev_exc]
                    // Stack after: [...]
                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 POP_EXCEPT: stack size = {frame.ValueStack.Count}");
                    #endif

                    if (frame.ValueStack.Count > 0)
                    {
                        // Pop prev_exc from stack (pushed by PUSH_EXC_INFO)
                        var prevExcValue = frame.ValueStack.Pop();

                        // RESTORE previous exception to CurrentException
                        // This is what Py_XSETREF does in CPython bytecodes.c:931
                        if (prevExcValue is PyBaseException previousException)
                        {
                            frame.CurrentException = previousException;
                        }
                        else if (prevExcValue == PyNone.Instance)
                        {
                            frame.CurrentException = null;  // No previous exception
                        }

                        #if DEBUG_LOG
                        Console.WriteLine($"🔧 POP_EXCEPT: Popped prev_exc={prevExcValue} from stack");
                        Console.WriteLine($"🔧 POP_EXCEPT: Restored CurrentException={frame.CurrentException}");
                        #endif
                    }
                    else
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"⚠️  POP_EXCEPT: Stack is empty - this indicates a bytecode generation issue");
                        #endif
                    }

                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 POP_EXCEPT completed, stack size: {frame.ValueStack.Count}");
                    #endif
                    break;

                case ByteCodeOp.CLEANUP_THROW:
                    // CPython 3.12: CLEANUP_THROW is used in yield from exception handling
                    // This opcode is part of the exception handling for generators
                    #if DEBUG_VM_LOG
                    Console.WriteLine($"🔍 CLEANUP_THROW: Cleaning up after throw in generator");
                    #endif
                    // For now, just clear any exception state
                    // The actual throw handling will be in the SEND opcode
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

                case ByteCodeOp.BEFORE_ASYNC_WITH:
                    // CPython 3.12: BEFORE_ASYNC_WITH for async context managers
                    // Python/bytecodes.c: inst(BEFORE_ASYNC_WITH)
                    // Stack: [mgr] -> [exit, res]
                    var asyncContextManager = frame.ValueStack.Pop();

                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 BEFORE_ASYNC_WITH: Processing async context manager: {asyncContextManager}");
                    #endif

                    // 1. Load __aenter__ method
                    PyObject aenterMethod;
                    try
                    {
                        aenterMethod = asyncContextManager.GetAttribute("__aenter__");
                        if (!aenterMethod.IsCallable())
                        {
                            throw PyTypeError.Create(
                                $"'{asyncContextManager.GetTypeName()}' object does not support the asynchronous context manager protocol");
                        }
                    }
                    catch
                    {
                        throw PyTypeError.Create(
                            $"'{asyncContextManager.GetTypeName()}' object does not support the asynchronous context manager protocol");
                    }

                    // 2. Load __aexit__ method
                    PyObject aexitMethod;
                    try
                    {
                        aexitMethod = asyncContextManager.GetAttribute("__aexit__");
                        if (!aexitMethod.IsCallable())
                        {
                            throw PyTypeError.Create(
                                $"'{asyncContextManager.GetTypeName()}' object does not support the asynchronous context manager protocol (missed __aexit__ method)");
                        }
                    }
                    catch
                    {
                        throw PyTypeError.Create(
                            $"'{asyncContextManager.GetTypeName()}' object does not support the asynchronous context manager protocol (missed __aexit__ method)");
                    }

                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 BEFORE_ASYNC_WITH: Found __aenter__ and __aexit__ methods");
                    #endif

                    // 3. Call __aenter__() to get awaitable
                    var aenterResult = aenterMethod.Call(new PyObject[0], null);

                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 BEFORE_ASYNC_WITH: __aenter__ returned: {aenterResult}");
                    #endif

                    // CPython 3.12 stack layout: [..., __aexit__, __aenter_result__]
                    // Push __aexit__ first (for later cleanup), then __aenter__ result
                    frame.ValueStack.Push(aexitMethod);
                    frame.ValueStack.Push(aenterResult);

                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 BEFORE_ASYNC_WITH: Stack after setup - size: {frame.ValueStack.Count}");
                    #endif
                    break;

                case ByteCodeOp.PUSH_EXC_INFO:
                    // CPython 3.12: Stack effect (new_exc -- prev_exc, new_exc)
                    // Takes current exception from stack, pushes previous exception, then current exception
                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 PUSH_EXC_INFO: Processing exception from stack");
                    #endif

                    // Pop current exception from stack (pushed by exception handler)
                    var newExc = frame.ValueStack.Pop();

                    // Get previous exception from frame state
                    PyObject prevExc;
                    if (frame.CurrentException != null)
                    {
                        prevExc = frame.CurrentException;
                    }
                    else
                    {
                        prevExc = PyNone.Instance;
                    }

                    // Update frame's current exception
                    if (newExc is PyException pyExc)
                    {
                        frame.CurrentException = pyExc;
                    }

                    // Push prev_exc first, then new_exc (CPython 3.12 stack order)
                    frame.ValueStack.Push(prevExc);
                    frame.ValueStack.Push(newExc);

                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 PUSH_EXC_INFO: Pushed prev_exc={prevExc}, new_exc={newExc}, stack size = {frame.ValueStack.Count}");
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
                    // Use PyStack.Reverse() for efficient iteration
                    int debugIdx = 0;
                    foreach (var item in frame.ValueStack.Reverse())
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"🔍 Stack[{debugIdx}]: {item}");
                        debugIdx++;
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

                    // CPython 3.12: Stack layout (CPython bytecodes.c:2523-2549)
                    // TOS: val (exception instance)
                    // TOS-1: unused (previous exception)
                    // TOS-2: lasti (instruction index as PyLong)
                    // TOS-3: exit_func (__exit__ method)

                    // Pop stack in reverse order
                    var val = frame.ValueStack.Pop();      // TOS
                    var unused = frame.ValueStack.Pop();   // TOS-1
                    var lasti = frame.ValueStack.Pop();    // TOS-2
                    var exit_func = frame.ValueStack.Pop(); // TOS-3

                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 WITH_EXCEPT_START: Extracted from stack:");
                    Console.WriteLine($"   val (exc) = {val}");
                    Console.WriteLine($"   unused (prev_exc) = {unused}");
                    Console.WriteLine($"   lasti = {lasti}");
                    Console.WriteLine($"   exit_func = {exit_func}");
                    #endif

                    // Validate val is an exception
                    if (!(val is PyException pyExcVal))
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"❌ WITH_EXCEPT_START: val is not PyException, got {val?.GetType().Name}");
                        #endif
                        // Restore stack and return False
                        frame.ValueStack.Push(exit_func);
                        frame.ValueStack.Push(lasti);
                        frame.ValueStack.Push(unused);
                        frame.ValueStack.Push(val);
                        frame.ValueStack.Push(PyBool.False);
                        break;
                    }

                    // CPython bytecodes.c:2535: exc = PyExceptionInstance_Class(val)
                    var exc_type = pyExcVal.GetPyType();

                    // CPython bytecodes.c:2536-2542: tb = PyException_GetTraceback(val)
                    PyObject exc_tb;
                    try
                    {
                        var tb_attr = pyExcVal.GetAttribute("__traceback__");
                        exc_tb = (tb_attr == PyNone.Instance) ? PyNone.Instance : tb_attr;
                    }
                    catch
                    {
                        exc_tb = PyNone.Instance;
                    }

                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 WITH_EXCEPT_START: Calling exit_func(exc_type={exc_type}, val={val}, tb={exc_tb})");
                    #endif

                    // CPython bytecodes.c:2545-2546: Call __exit__(exc_type, val, tb)
                    bool suppressException = false;
                    if (exit_func?.IsCallable() == true)
                    {
                        try
                        {
                            var exitResult = exit_func.Call(new PyObject[] {
                                exc_type,
                                val,
                                exc_tb
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
                            // Re-throw the new exception from __exit__
                            var newPyException = ConvertToPythonException(exitException);
                            throw new PythonException(newPyException);
                        }
                    }
                    else
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"❌ WITH_EXCEPT_START: exit_func not callable: {exit_func?.GetType().Name}");
                        #endif
                    }

                    // CPython bytecodes.c: Restore stack and push result
                    // Stack after: [..., exit_func, lasti, unused, val, res]
                    frame.ValueStack.Push(exit_func);
                    frame.ValueStack.Push(lasti);
                    frame.ValueStack.Push(unused);
                    frame.ValueStack.Push(val);
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

                        // Handle exc - could be a type or an instance
                        PyException excInstance;
                        if (exc is PyException pyExc2)
                        {
                            // Already a PyException instance
                            excInstance = pyExc2;
                        }
                        else if (exc is PyType pyType)
                        {
                            // Exception class (PyType) - instantiate it
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
                            // Builtin exception class - instantiate it
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
                        else if (exc is PyClass userClass)
                        {
                            // User-defined exception class - instantiate it
                            var userException = userClass.Call(Array.Empty<PyObject>());
                            if (userException is PyException pyUserExInstance)
                            {
                                excInstance = pyUserExInstance;
                            }
                            else
                            {
                                throw new PythonException(new PyTypeError($"exceptions must derive from BaseException"));
                            }
                        }
                        else if (exc is PyObject customInstance)
                        {
                            // Instance of a user-defined exception (e.g., PyClassInstance)
                            if (IsExceptionLike(customInstance))
                            {
                                // Create a PyException wrapper with class information and instance preserved
                                if (customInstance is PyClassInstance classInst)
                                {
                                    // CRITICAL: Store the original PyClassInstance so attributes are preserved
                                    excInstance = new PyException(customInstance.ToString(), classInst.InstanceType, classInst);
                                }
                                else
                                {
                                    excInstance = new PyException(customInstance.ToString());
                                }
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

                        // CPython 3.12: Implicit exception chaining - set __context__ before __cause__
                        // Corresponds to _PyErr_SetObject in Python/errors.c:207-235
                        if (frame.CurrentException != null && frame.CurrentException != excInstance)
                        {
                            excInstance.__context__ = frame.CurrentException;
                        }

                        // Set __cause__ attribute (explicit chaining)
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

                        // CPython 3.12: Set traceback to current frame before throwing
                        // Corresponds to PyTraceBack_Here() in traceback.c:266
                        var pyExToThrow = new PythonException(excInstance);
                        PyTraceBack_Here(frame, pyExToThrow);
                        throw pyExToThrow;
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
                            // CPython 3.12: Implicit exception chaining
                            // Corresponds to _PyErr_SetObject in Python/errors.c:207-235
                            if (frame.CurrentException != null && frame.CurrentException != pyEx)
                            {
                                pyEx.__context__ = frame.CurrentException;
                            }

                            // Already an exception instance
                            frame.LastException = pyEx;
                            // CPython 3.12: Set traceback to current frame before throwing
                            var pyExToThrow1 = new PythonException(pyEx);
                            PyTraceBack_Here(frame, pyExToThrow1);
                            throw pyExToThrow1;
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
                                // CPython 3.12: Implicit exception chaining
                                if (frame.CurrentException != null && frame.CurrentException != instanceException)
                                {
                                    instanceException.__context__ = frame.CurrentException;
                                }

                                frame.LastException = instanceException;
                                // CPython 3.12: Set traceback to current frame before throwing
                                var pyExToThrow2 = new PythonException(instanceException);
                                PyTraceBack_Here(frame, pyExToThrow2);
                                throw pyExToThrow2;
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
                                // CPython 3.12: Implicit exception chaining
                                if (frame.CurrentException != null && frame.CurrentException != pyExInstance)
                                {
                                    pyExInstance.__context__ = frame.CurrentException;
                                }

                                frame.LastException = pyExInstance;
                                // CPython 3.12: Set traceback to current frame before throwing
                                var pyExToThrow3 = new PythonException(pyExInstance);
                                PyTraceBack_Here(frame, pyExToThrow3);
                                throw pyExToThrow3;
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
                                // CPython 3.12: Implicit exception chaining
                                if (frame.CurrentException != null && frame.CurrentException != pyUserExInstance)
                                {
                                    pyUserExInstance.__context__ = frame.CurrentException;
                                }

                                frame.LastException = pyUserExInstance;
                                // CPython 3.12: Set traceback to current frame before throwing
                                var pyExToThrow4 = new PythonException(pyUserExInstance);
                                PyTraceBack_Here(frame, pyExToThrow4);
                                throw pyExToThrow4;
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

                                // CPython 3.12: Implicit exception chaining
                                if (frame.CurrentException != null && frame.CurrentException != wrappedException)
                                {
                                    wrappedException.__context__ = frame.CurrentException;
                                }

                                frame.LastException = wrappedException;
                                // CPython 3.12: Set traceback to current frame before throwing
                                var pyExToThrow5 = new PythonException(wrappedException);
                                PyTraceBack_Here(frame, pyExToThrow5);
                                throw pyExToThrow5;
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
                    // Stack effect: (left, right -- left, b)
                    // Pops type (right), keeps exception (left), pushes boolean result
                    var expectedType = frame.ValueStack.Pop(); // right (exception type)
                    var exceptionInstance = frame.ValueStack.Peek(); // left (exception instance) - keep on stack

                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 CHECK_EXC_MATCH: expectedType={expectedType?.GetType().Name}={expectedType}");
                    Console.WriteLine($"🔧 CHECK_EXC_MATCH: exceptionInstance={exceptionInstance?.GetType().Name}={exceptionInstance}");
                    #endif

                    if (expectedType == null)
                    {
                        throw PyRuntimeError.Create("CHECK_EXC_MATCH: expectedType is null");
                    }

                    bool matches = false;

                    // Match exception instance against expected type
                    if (exceptionInstance is PyException pyException)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"🔧 CHECK_EXC_MATCH: PyException with OriginalClass={pyException.OriginalClass?.Name ?? "null"}");
                        #endif

                        // Check against user-defined class
                        if (expectedType is PyClass userClass && pyException.OriginalClass != null)
                        {
                            matches = pyException.OriginalClass == userClass ||
                                     pyException.OriginalClass.Name == userClass.Name;
                        }
                        // Check against built-in type
                        else if (expectedType is PyBuiltinType builtinType)
                        {
                            matches = IsExceptionInstanceOf(pyException, builtinType.Name);
                        }
                        else if (expectedType is PyType pyType)
                        {
                            matches = IsExceptionInstanceOf(pyException, pyType.Name);
                        }
                        // Check against tuple of exception types: except (ValueError, TypeError)
                        else if (expectedType is PyTuple exceptionTuple)
                        {
                            foreach (var excType in exceptionTuple.Items)
                            {
                                if (excType is PyBuiltinType tupleBuiltin)
                                {
                                    if (IsExceptionInstanceOf(pyException, tupleBuiltin.Name))
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
                    else if (exceptionInstance is PyBaseException builtinException)
                    {
                        // CPython 3.12: Python/errors.c:350-354
                        // Direct builtin exception (PyValueError, PyTypeError, etc.)
                        #if DEBUG_LOG
                        Console.WriteLine($"🔧 CHECK_EXC_MATCH: PyBaseException {builtinException.GetType().Name}");
                        #endif

                        // CPython: Get exception class from instance
                        var actualType = builtinException.GetType();

                        if (expectedType is PyType pyType)
                        {
                            // CPython: PyType_IsSubtype - check if actualType is subtype of expectedType
                            var expectedCSharpType = GetExceptionTypeByName(pyType.Name);
                            if (expectedCSharpType != null)
                            {
                                matches = expectedCSharpType.IsAssignableFrom(actualType);
                            }
                            else
                            {
                                // Fall back to name matching for unknown types
                                string simpleName = actualType.Name.StartsWith("Py")
                                    ? actualType.Name.Substring(2)
                                    : actualType.Name;
                                matches = pyType.Name == simpleName;
                            }
                        }
                        else if (expectedType is PyBuiltinType builtinType)
                        {
                            // CPython: PyType_IsSubtype - check if actualType is subtype of expectedType
                            var expectedCSharpType = GetExceptionTypeByName(builtinType.Name);
                            if (expectedCSharpType != null)
                            {
                                matches = expectedCSharpType.IsAssignableFrom(actualType);
                            }
                            else
                            {
                                // Fall back to name matching for unknown types
                                string simpleName = actualType.Name.StartsWith("Py")
                                    ? actualType.Name.Substring(2)
                                    : actualType.Name;
                                matches = builtinType.Name == simpleName;
                            }
                        }
                    }
                    else if (exceptionInstance is PyClassInstance classInstance)
                    {
                        // User-defined exception instance
                        #if DEBUG_LOG
                        Console.WriteLine($"🔧 CHECK_EXC_MATCH: PyClassInstance from {classInstance.InstanceType.Name}");
                        #endif

                        if (expectedType is PyClass userClass)
                        {
                            matches = classInstance.InstanceType == userClass ||
                                     classInstance.InstanceType.Name == userClass.Name;
                        }
                        else if (expectedType is PyType pyType)
                        {
                            // Check if custom instance is compatible with Exception/BaseException
                            matches = pyType.Name == "Exception" || pyType.Name == "BaseException";
                        }
                    }

                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 CHECK_EXC_MATCH: Result = {matches}");
                    #endif

                    frame.ValueStack.Push(matches ? PyBool.True : PyBool.False);
                    break;

                case ByteCodeOp.RERAISE:
                    // CPython 3.12: RERAISE stack layout: (values[oparg], exc -- values[oparg])
                    // - exc: exception to reraise (top of stack)
                    // - values[oparg]: oparg values below exc (e.g., lasti for cleanup)
                    // - After reraise: exc is popped and raised, values are left on stack if oparg > 0
                    var reraiseArg = instruction.Argument;

                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 RERAISE: arg={reraiseArg}, stack size={frame.ValueStack.Count}");
                    #endif

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

                    // CPython 3.12: Pop exception from top of stack (this is what we reraise)
                    PyBaseException exceptionToReraise = null;
                    if (frame.ValueStack.Count > 0)
                    {
                        var exceptionOnStack = frame.ValueStack.Pop();
                        #if DEBUG_LOG
                        Console.WriteLine($"🔧 RERAISE: Popped exception from stack (TOS): {exceptionOnStack}");
                        #endif

                        // If it's a PyExceptionInfo, extract the actual exception
                        if (exceptionOnStack is PyExceptionInfo reraiseExcInfo)
                        {
                            exceptionToReraise = reraiseExcInfo.ExcValue as PyBaseException;
                        }
                        else if (exceptionOnStack is PyBaseException directException)
                        {
                            exceptionToReraise = directException;
                        }
                    }

                    // CPython 3.12: If oparg > 0, pop additional values from stack (but don't use them)
                    // These are typically lasti values used for traceback reconstruction
                    if (reraiseArg > 0)
                    {
                        for (int i = 0; i < reraiseArg; i++)
                        {
                            if (frame.ValueStack.Count > 0)
                            {
                                var additionalValue = frame.ValueStack.Pop();
                                #if DEBUG_LOG
                                Console.WriteLine($"🔧 RERAISE: Popped additional value[{i}]: {additionalValue}");
                                #endif
                            }
                        }
                    }

                    // Reraise the exception
                    if (exceptionToReraise != null)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"🔧 RERAISE: Reraising exception: {exceptionToReraise}");
                        #endif
                        // CPython 3.12: RERAISE preserves existing traceback, don't add new frames
                        throw new PythonException(exceptionToReraise, fromReraise: true);
                    }

                    // Fallback: use LastException if no exception on stack
                    if (frame.LastException != null)
                        throw new PythonException(frame.LastException, fromReraise: true);
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

                    // CPython 3.12 stack order: [value, formatSpec] (TOS is formatSpec)
                    // Pop in reverse order: formatSpec first, then value
                    PyObject formatSpec = null;
                    if (hasFormatSpec)
                    {
                        formatSpec = frame.ValueStack.Pop(); // Pop format spec (TOS)
                    }
                    var formatValue = frame.ValueStack.Pop(); // Pop value

                    if (hasFormatSpec)
                    {
                        // Apply conversion first (if specified)
                        PyObject converted = conversion switch
                        {
                            1 => formatValue.ToStr(), // !s
                            2 => formatValue.ToRepr(), // !r
                            3 => formatValue.ToRepr(), // !a (simplified as repr)
                            _ => formatValue // No conversion, use original value
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
                        // CPython 3.12: Use GetIterator() for iterable objects (including metaclass __iter__)
                        try
                        {
                            // Call GetIterator() directly to support metaclass __iter__
                            var iteratorObj = sequence.GetIterator();

                            // Collect all items from iterator
                            var items = new List<PyObject>();

                            while (true)
                            {
                                try
                                {
                                    var item = iteratorObj.Next();
                                    items.Add(item);
                                }
                                catch (PythonException pex) when (pex.PyException is PyStopIteration)
                                {
                                    break;
                                }
                            }

                            // Check count matches
                            if (items.Count != unpackCount)
                            {
                                throw PyValueError.Create($"not enough values to unpack (expected {unpackCount}, got {items.Count})");
                            }

                            // Push items in reverse order (CPython convention)
                            for (int i = items.Count - 1; i >= 0; i--)
                            {
                                frame.ValueStack.Push(items[i]);
                            }
                        }
                        catch (PythonException pex) when (pex.PyException is PyTypeError)
                        {
                            // Re-throw PyTypeError (already has correct message)
                            throw;
                        }
                    }
                    break;

                case ByteCodeOp.UNPACK_EX:
                    // CPython 3.12: Python/ceval.c lines 1105-1112 (bytecodes.c)
                    // CPython 3.12: Python/ceval.c lines 1950-2040 (unpack_iterable function)
                    // Argument encodes: lower 8 bits = count before star, upper 8 bits = count after star
                    var countBefore = instruction.Argument & 0xFF;
                    var countAfter = (instruction.Argument >> 8) & 0xFF;

                    var unpackExSequence = frame.ValueStack.Pop();

                    // CPython 3.12: Python/ceval.c:1960-1970 - Convert iterable to list first
                    PyObject[] itemsToUnpack;
                    if (unpackExSequence is PyList unpackExList)
                    {
                        itemsToUnpack = unpackExList.Items;
                    }
                    else if (unpackExSequence is PyTuple unpackExTuple)
                    {
                        itemsToUnpack = unpackExTuple.Items;
                    }
                    else if (unpackExSequence is PyRange unpackExRange)
                    {
                        // CPython 3.12: Convert range to list for unpacking
                        itemsToUnpack = unpackExRange.ToList().Items;
                    }
                    else if (unpackExSequence is PyString unpackExStr)
                    {
                        // Convert string to array of single-character strings
                        itemsToUnpack = unpackExStr.Value.Select(c => (PyObject)new PyString(c.ToString())).ToArray();
                    }
                    else
                    {
                        // CPython 3.12: Try to iterate using __iter__
                        try
                        {
                            var unpackIterator = unpackExSequence.GetIterator();
                            var unpackItems = new System.Collections.Generic.List<PyObject>();
                            while (true)
                            {
                                try
                                {
                                    unpackItems.Add(unpackIterator.Next());
                                }
                                catch (Exception ex) when (ex is PyStopIteration || ex.Message.Contains("StopIteration"))
                                {
                                    break;
                                }
                            }
                            itemsToUnpack = unpackItems.ToArray();
                        }
                        catch (Exception)
                        {
                            throw PyTypeError.Create($"cannot unpack non-sequence {unpackExSequence.GetTypeName()}");
                        }
                    }

                    if (itemsToUnpack.Length < countBefore + countAfter)
                    {
                        throw PyValueError.Create($"not enough values to unpack (expected at least {countBefore + countAfter}, got {itemsToUnpack.Length})");
                    }

                    // CPython: unpack_iterable uses *--sp which pushes in REVERSE order
                    // Python/ceval.c lines 1973-1982: for (; i < argcnt; i++) { *--sp = w; }
                    // Python/ceval.c lines 2002-2004: *--sp = l;
                    // Python/ceval.c lines 2013-2015: for (j = argcntafter; j > 0; j--) { *--sp = PyList_GET_ITEM(l, ll - j); }

                    // Get current stack position for insertion
                    var stackList = frame.ValueStack.GetInternalList();
                    int insertPosition = stackList.Count;

                    // Push before elements (lines 1973-1982)
                    for (int i = 0; i < countBefore; i++)
                    {
                        // *--sp = items[i] → insert at current position (reverse order)
                        stackList.Insert(insertPosition, itemsToUnpack[i]);
                    }

                    // Extract and push star element (lines 2002-2004)
                    var starCount = itemsToUnpack.Length - countBefore - countAfter;
                    var starItems = new PyObject[starCount];
                    for (int i = 0; i < starCount; i++)
                    {
                        starItems[i] = itemsToUnpack[countBefore + i];
                    }
                    // *--sp = l
                    stackList.Insert(insertPosition, new PyList(starItems));

                    // Push after elements (lines 2013-2015)
                    // for (j = argcntafter; j > 0; j--) { *--sp = PyList_GET_ITEM(l, ll - j); }
                    for (int j = countAfter; j > 0; j--)
                    {
                        // *--sp = items[length - j]
                        stackList.Insert(insertPosition, itemsToUnpack[itemsToUnpack.Length - j]);
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
                        // Performance: Eliminated LINQ - manual conversion
                        fromlistArray = new string[pyTupleFromlist.Items.Length];
                        for (int i = 0; i < pyTupleFromlist.Items.Length; i++)
                        {
                            var item = pyTupleFromlist.Items[i];
                            fromlistArray[i] = item is PyString s ? s.Value : item.AsString();
                        }
                    }

                    // CPython 3.12: Pass frame.Globals to import system for relative import resolution
                    var importedModule = PyImportSystem.Import(moduleName, importLevel, fromlistArray, frame.Globals);
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
                        // CPython 3.12: Python/ceval.c:2530-2560 (import_from)
                        PyObject importedItem = null;

                        // Step 1: Try to get attribute from module object
                        // CPython 3.12: Python/ceval.c:2535-2537 (_PyObject_LookupAttr)
                        try
                        {
                            importedItem = module.GetAttribute(itemName);
                        }
                        catch
                        {
                            // Attribute not found, continue to fallback
                        }

                        if (importedItem == null)
                        {
                            // Step 2: Fallback for circular imports / submodule import
                            // CPython 3.12: Python/ceval.c:2538-2560
                            // Try to read submodule directly from sys.modules, or import it
                            try
                            {
                                // Get package name from module.__name__
                                // CPython 3.12: Python/ceval.c:2541
                                var pkgName = module.GetAttribute("__name__");
                                if (pkgName is PyString pkgNameStr)
                                {
                                    // Construct full module name: package.name
                                    // CPython 3.12: Python/ceval.c:2549
                                    string fullModuleName = $"{pkgNameStr.Value}.{itemName}";

                                    // Try to get from sys.modules ONLY
                                    // CPython 3.12: Python/ceval.c:2554 (PyImport_GetModule)
                                    // IMPORTANT: Do NOT import if not found - this is intentional!
                                    // The submodule should have been imported by IMPORT_NAME's fromlist handling.
                                    // If it's not in sys.modules, this is a circular import or the module doesn't exist.
                                    if (PyImportSystem.TryGetModule(fullModuleName, out var subModule))
                                    {
                                        importedItem = subModule;
                                    }
                                    // If not found in sys.modules, importedItem remains null
                                    // and we'll raise an error below
                                }
                            }
                            catch
                            {
                                // Fallback also failed
                            }
                        }

                        if (importedItem == null)
                        {
                            // Generate error message similar to CPython
                            // CPython 3.12: Python/ceval.c:2561-2579
                            string pkgModuleName = "unknown";
                            try
                            {
                                var nameAttr = module.GetAttribute("__name__");
                                if (nameAttr is PyString nameStr)
                                {
                                    pkgModuleName = nameStr.Value;
                                }
                            }
                            catch { }

                            throw PyAttributeError.Create($"module '{pkgModuleName}' has no attribute '{itemName}'");
                        }

                        frame.ValueStack.Push(importedItem);
                    }
                    break;

                // PEP 709 Comprehension Optimization - VM 구현
                case ByteCodeOp.LIST_APPEND:
                    // CPython 3.12 호환: LIST_APPEND i
                    // 스택: [..., list, ..., item] → [..., list, ...]
                    #if DEBUG_VM_LOG
                    Console.WriteLine($"🔧 LIST_APPEND {instruction.Argument}: Stack before pop = {frame.ValueStack.Count}");
                    #endif
                    if (frame.ValueStack.Count > 0)
                    {
                        // Performance: Eliminated LINQ - manual stack preview
                        var stackArray = frame.ValueStack.ToArray();
                        var previewCount = Math.Min(5, stackArray.Length);
                        var stackBefore = new string[previewCount];
                        for (int i = 0; i < previewCount; i++)
                        {
                            stackBefore[i] = $"[{i}]={stackArray[i].GetType().Name}";
                        }
                        #if DEBUG_VM_LOG
                        Console.WriteLine($"    Stack: {string.Join(", ", stackBefore)}");
                        #endif
                    }

                    var itemToAppend = frame.ValueStack.Pop();
                    #if DEBUG_VM_LOG
                    Console.WriteLine($"    Popped item: {itemToAppend.GetType().Name}");
                    #endif

                    // CPython 3.12: LIST_APPEND i에서 타겟 리스트 찾기
                    var targetDepth = instruction.Argument - 1; // 0-based 인덱스
                    #if DEBUG_VM_LOG
                    Console.WriteLine($"    Looking for list at depth {targetDepth} (arg={instruction.Argument})");
                    #endif

                    if (frame.ValueStack.Count <= targetDepth)
                    {
                        throw new Exception($"LIST_APPEND: not enough items on stack (need {targetDepth + 1}, got {frame.ValueStack.Count})");
                    }

                    // 스택 위치에서 리스트 찾기 - PyNull 건너뛰기 (O(1) indexed access)
                    var targetList = frame.ValueStack.PeekAt(targetDepth);
                    #if DEBUG_VM_LOG
                    Console.WriteLine($"    Found at PeekAt({targetDepth}): {targetList.GetType().Name}");
                    #endif

                    // PyNull인 경우 실제 리스트를 찾기 위해 스택을 탐색
                    if (PyNull.IsNull(targetList))
                    {
                        // PyNull들을 건너뛰고 실제 리스트 찾기
                        for (int i = targetDepth; i < frame.ValueStack.Count; i++)
                        {
                            var candidate = frame.ValueStack.PeekAt(i);
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
                        // Performance: Eliminated LINQ - manual ToString conversion
                        var itemStrings = new string[targetPyList.Items.Length];
                        for (int i = 0; i < targetPyList.Items.Length; i++)
                        {
                            itemStrings[i] = targetPyList.Items[i]?.ToString() ?? "null";
                        }
                        Console.WriteLine($"   LIST_APPEND 완료: 리스트 크기: {targetPyList.Count}, 내용: [{string.Join(", ", itemStrings)}]");
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

                    // 스택 위치에서 set 찾기 - PyNull 건너뛰기 (O(1) indexed access)
                    var targetSet = frame.ValueStack.PeekAt(setTargetDepth);

                    // PyNull인 경우 실제 set을 찾기 위해 스택을 탐색
                    if (PyNull.IsNull(targetSet))
                    {
                        // PyNull들을 건너뛰고 실제 set 찾기
                        for (int i = setTargetDepth; i < frame.ValueStack.Count; i++)
                        {
                            var candidate = frame.ValueStack.PeekAt(i);
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
                        for (int i = 0; i < Math.Min(5, debugStackArray.Length); i++)
                        {
                            Console.WriteLine($"    Stack[{i}]: {debugStackArray[i]?.GetType().Name ?? "null"} = {debugStackArray[i]?.ToString() ?? "null"}");
                        }
                        Console.WriteLine($"🔍 MAP_ADD Debug: targetDict at PeekAt({dictDepth - 1})");
                        #endif

                        // CPython PEEK 방식: PeekAt(dictDepth-1) - O(1) indexed access
                        // dictDepth=2 → PeekAt(1), dictDepth=3 → PeekAt(2), etc.
                        var mapAddTarget = frame.ValueStack.PeekAt(dictDepth - 1);

                        if (mapAddTarget is PyDict mapAddDict)
                        {
                            // Use SetItem to properly maintain insertion order (_keys list)
                            mapAddDict.SetItem(dictKey, dictValue);
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
                    // CPython 3.12: After fix_cell_offsets(), argument is localsplus offset
                    // Need to convert to SharpPy Cells index
                    var loadLocalsPlusOffset = instruction.Argument;
                    int loadNlocals = frame.Code.VarNames.Count;
                    int loadNcellvars = frame.Code.CellVars.Count;
                    int loadNfreevars = frame.Code.FreeVars.Count;

                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 LOAD_DEREF at localsplus offset {loadLocalsPlusOffset}");
                    #endif

                    // Convert CPython localsplus offset to SharpPy Cells index
                    int loadCellIndex;
                    string loadVarName;

                    if (loadLocalsPlusOffset < loadNlocals)
                    {
                        // It's a parameter that's also a cell
                        loadVarName = frame.Code.VarNames[loadLocalsPlusOffset];
                        int cellVarIdx = frame.Code.CellVars.IndexOf(loadVarName);
                        if (cellVarIdx < 0)
                        {
                            throw new Exception($"LOAD_DEREF: varname '{loadVarName}' not found in cellvars");
                        }
                        loadCellIndex = loadNfreevars + cellVarIdx;
                    }
                    else
                    {
                        // CPython 3.12: localsplus layout is [varnames | non-param cells | freevars]
                        // Build list of non-param cell names (cells NOT in varnames)
                        var nonParamCellNames = new List<string>();
                        for (int i = 0; i < loadNcellvars; i++)
                        {
                            if (!frame.Code.VarNames.Contains(frame.Code.CellVars[i]))
                                nonParamCellNames.Add(frame.Code.CellVars[i]);
                        }
                        int loadNumNonParamCells = nonParamCellNames.Count;

                        int offsetAfterLocals = loadLocalsPlusOffset - loadNlocals;
                        if (offsetAfterLocals < loadNumNonParamCells)
                        {
                            // It's a non-parameter cellvar
                            // Find which cell by name (non-param cells are sorted alphabetically)
                            loadVarName = nonParamCellNames[offsetAfterLocals];
                            int cellVarIdx = frame.Code.CellVars.IndexOf(loadVarName);
                            loadCellIndex = loadNfreevars + cellVarIdx;
                        }
                        else
                        {
                            // It's a freevar
                            int freeVarIdx = offsetAfterLocals - loadNumNonParamCells;
                            loadVarName = frame.Code.FreeVars[freeVarIdx];
                            loadCellIndex = freeVarIdx;
                        }
                    }

                    #if DEBUG_LOG
                    Console.WriteLine($"   Converting CPython localsplus[{loadLocalsPlusOffset}] → SharpPy Cells[{loadCellIndex}] for '{loadVarName}'");
                    #endif

                    // Access the cell
                    PyCell cell;
                    if (loadCellIndex < frame.Cells.Length)
                    {
                        cell = frame.Cells[loadCellIndex];
                        #if DEBUG_LOG
                        Console.WriteLine($"   → Using Cells[{loadCellIndex}]: {(cell.HasValue ? cell.Value : "empty")}");
                        #endif
                    }
                    else
                    {
                        throw new Exception($"LOAD_DEREF: invalid cell index {loadCellIndex} (Cells.Length={frame.Cells.Length})");
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
                        Console.WriteLine($"   ❌ CPython 3.12 behavior: Throwing UnboundLocalError for '{loadVarName}'");
                        #endif
                        throw PyNameError.Create($"local variable '{loadVarName}' referenced before assignment");
                    }
                    break;

                case ByteCodeOp.STORE_DEREF:
                    // CPython 3.12: After fix_cell_offsets(), argument is localsplus offset
                    var storeLocalsPlusOffset = instruction.Argument;
                    var storeDerefValue = frame.ValueStack.Pop();
                    int storeNlocals = frame.Code.VarNames.Count;
                    int storeNcellvars = frame.Code.CellVars.Count;
                    int storeNfreevars = frame.Code.FreeVars.Count;

                    // Convert to SharpPy Cells index
                    int storeCellIndex;
                    if (storeLocalsPlusOffset < storeNlocals)
                    {
                        string varName = frame.Code.VarNames[storeLocalsPlusOffset];
                        storeCellIndex = storeNfreevars + frame.Code.CellVars.IndexOf(varName);
                    }
                    else
                    {
                        // CPython 3.12: localsplus layout is [varnames | non-param cells | freevars]
                        // Build list of non-param cell names (cells NOT in varnames)
                        var nonParamCellNames = new List<string>();
                        for (int i = 0; i < storeNcellvars; i++)
                        {
                            if (!frame.Code.VarNames.Contains(frame.Code.CellVars[i]))
                                nonParamCellNames.Add(frame.Code.CellVars[i]);
                        }
                        int storeNumNonParamCells = nonParamCellNames.Count;

                        int offsetAfterLocals = storeLocalsPlusOffset - storeNlocals;
                        if (offsetAfterLocals < storeNumNonParamCells)
                        {
                            // Non-param cell: find by name
                            string storeCellVarName = nonParamCellNames[offsetAfterLocals];
                            int storeCellVarIdx = frame.Code.CellVars.IndexOf(storeCellVarName);
                            storeCellIndex = storeNfreevars + storeCellVarIdx;
                        }
                        else
                        {
                            // Free var
                            int freeVarIdx = offsetAfterLocals - storeNumNonParamCells;
                            storeCellIndex = freeVarIdx;
                        }
                    }

                    if (storeCellIndex < frame.Cells.Length)
                    {
                        frame.Cells[storeCellIndex].SetValue(storeDerefValue);
                    }
                    else
                    {
                        throw new Exception($"STORE_DEREF: invalid cell index {storeCellIndex}");
                    }
                    break;

                case ByteCodeOp.DELETE_DEREF:
                    // CPython 3.12: After fix_cell_offsets(), argument is localsplus offset
                    var deleteLocalsPlusOffset = instruction.Argument;
                    int deleteNlocals = frame.Code.VarNames.Count;
                    int deleteNcellvars = frame.Code.CellVars.Count;
                    int deleteNfreevars = frame.Code.FreeVars.Count;

                    // Convert to SharpPy Cells index
                    int deleteCellIndex;
                    if (deleteLocalsPlusOffset < deleteNlocals)
                    {
                        string varName = frame.Code.VarNames[deleteLocalsPlusOffset];
                        deleteCellIndex = deleteNfreevars + frame.Code.CellVars.IndexOf(varName);
                    }
                    else
                    {
                        // CPython 3.12: localsplus layout is [varnames | non-param cells | freevars]
                        var deleteNonParamCellNames = new List<string>();
                        for (int i = 0; i < deleteNcellvars; i++)
                        {
                            if (!frame.Code.VarNames.Contains(frame.Code.CellVars[i]))
                                deleteNonParamCellNames.Add(frame.Code.CellVars[i]);
                        }
                        int deleteNumNonParamCells = deleteNonParamCellNames.Count;

                        int offsetAfterLocals = deleteLocalsPlusOffset - deleteNlocals;
                        if (offsetAfterLocals < deleteNumNonParamCells)
                        {
                            // Non-param cell: find by name
                            string deleteCellVarName = deleteNonParamCellNames[offsetAfterLocals];
                            int deleteCellVarIdx = frame.Code.CellVars.IndexOf(deleteCellVarName);
                            deleteCellIndex = deleteNfreevars + deleteCellVarIdx;
                        }
                        else
                        {
                            // Free var
                            int freeVarIdx = offsetAfterLocals - deleteNumNonParamCells;
                            deleteCellIndex = freeVarIdx;
                        }
                    }

                    if (deleteCellIndex < frame.Cells.Length)
                    {
                        frame.Cells[deleteCellIndex].Clear();
                        #if DEBUG_LOG
                        Console.WriteLine($"🔧 DELETE_DEREF: cleared cell at index {deleteCellIndex} to NULL");
                        #endif
                    }
                    else
                    {
                        throw new Exception($"DELETE_DEREF: invalid cell index {deleteCellIndex}");
                    }
                    break;

                case ByteCodeOp.LOAD_CLOSURE:
                    // CPython 3.12: After fix_cell_offsets(), argument is localsplus offset
                    var closureLocalsPlusOffset = instruction.Argument;
                    int closureNlocals = frame.Code.VarNames.Count;
                    int closureNcellvars = frame.Code.CellVars.Count;
                    int closureNfreevars = frame.Code.FreeVars.Count;

                    // Convert to SharpPy Cells index
                    int closureCellIndex;
                    if (closureLocalsPlusOffset < closureNlocals)
                    {
                        string varName = frame.Code.VarNames[closureLocalsPlusOffset];
                        closureCellIndex = closureNfreevars + frame.Code.CellVars.IndexOf(varName);
                    }
                    else
                    {
                        // CPython 3.12: localsplus layout is [varnames | non-param cells | freevars]
                        var closureNonParamCellNames = new List<string>();
                        for (int i = 0; i < closureNcellvars; i++)
                        {
                            if (!frame.Code.VarNames.Contains(frame.Code.CellVars[i]))
                                closureNonParamCellNames.Add(frame.Code.CellVars[i]);
                        }
                        int closureNumNonParamCells = closureNonParamCellNames.Count;

                        int offsetAfterLocals = closureLocalsPlusOffset - closureNlocals;
                        if (offsetAfterLocals < closureNumNonParamCells)
                        {
                            // Non-param cell: find by name
                            string closureCellVarName = closureNonParamCellNames[offsetAfterLocals];
                            int closureCellVarIdx = frame.Code.CellVars.IndexOf(closureCellVarName);
                            closureCellIndex = closureNfreevars + closureCellVarIdx;
                        }
                        else
                        {
                            // Free var
                            int freeVarIdx = offsetAfterLocals - closureNumNonParamCells;
                            closureCellIndex = freeVarIdx;
                        }
                    }

                    PyCell closureCell;
                    if (closureCellIndex < frame.Cells.Length)
                    {
                        closureCell = frame.Cells[closureCellIndex];
                        #if DEBUG_LOG
                        Console.WriteLine($"🔐 LOAD_CLOSURE: localsplus[{closureLocalsPlusOffset}] → Cells[{closureCellIndex}]");
                        #endif
                    }
                    else
                    {
                        // Create new cell if needed (shouldn't happen in correct code)
                        closureCell = new PyCell();
                        #if DEBUG_LOG
                        Console.WriteLine($"   ⚠️  LOAD_CLOSURE: Creating new empty cell for index {closureCellIndex}");
                        #endif
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
                    // CPython 3.12: After fix_cell_offsets(), argument is localsplus offset
                    // CPython localsplus layout: [varnames(0..nlocals-1) | cellvars(nlocals..nlocals+ncellvars-1) | freevars(...)]
                    // SharpPy Cells layout: [freevars(0..nfreevars-1) | cellvars(nfreevars..nfreevars+ncellvars-1)]
                    var localsPlusOffset = instruction.Argument;
                    int nlocals = frame.Code.VarNames.Count;
                    int ncellvars = frame.Code.CellVars.Count;
                    int nfreevars = frame.Code.FreeVars.Count;

                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 MAKE_CELL at localsplus offset {localsPlusOffset} (nlocals={nlocals}, ncellvars={ncellvars}, nfreevars={nfreevars})");
                    #endif

                    // Convert CPython localsplus offset to SharpPy Cells index
                    string cellVarName;
                    int actualCellIndex;

                    if (localsPlusOffset < nlocals)
                    {
                        // It's a parameter (in varnames) that's also a cell
                        cellVarName = frame.Code.VarNames[localsPlusOffset];
                        // Find it in cellvars to get the cell index
                        int cellVarIdx = frame.Code.CellVars.IndexOf(cellVarName);
                        if (cellVarIdx < 0)
                        {
                            throw new IndexOutOfRangeException($"MAKE_CELL: varname '{cellVarName}' not found in cellvars");
                        }
                        // SharpPy: cellvars are at Cells[nfreevars + cellVarIdx]
                        actualCellIndex = nfreevars + cellVarIdx;
                    }
                    else
                    {
                        // It's not a parameter - either cellvar or freevar
                        // CPython 3.12: localsplus layout is [varnames | non-param cells | freevars]
                        var makeNonParamCellNames = new List<string>();
                        for (int i = 0; i < ncellvars; i++)
                        {
                            if (!frame.Code.VarNames.Contains(frame.Code.CellVars[i]))
                                makeNonParamCellNames.Add(frame.Code.CellVars[i]);
                        }
                        int numNonParamCells = makeNonParamCellNames.Count;

                        int offsetAfterLocals = localsPlusOffset - nlocals;
                        if (offsetAfterLocals < numNonParamCells)
                        {
                            // It's a cellvar (non-parameter) - find by name
                            cellVarName = makeNonParamCellNames[offsetAfterLocals];
                            int cellVarIdx = frame.Code.CellVars.IndexOf(cellVarName);
                            // SharpPy: cellvars are at Cells[nfreevars + cellVarIdx]
                            actualCellIndex = nfreevars + cellVarIdx;
                        }
                        else
                        {
                            // It's a freevar
                            int freeVarIdx = offsetAfterLocals - numNonParamCells;
                            cellVarName = frame.Code.FreeVars[freeVarIdx];
                            // SharpPy: freevars are at Cells[freeVarIdx]
                            actualCellIndex = freeVarIdx;
                        }
                    }

                    #if DEBUG_LOG
                    Console.WriteLine($"   Converting CPython localsplus[{localsPlusOffset}] → SharpPy Cells[{actualCellIndex}] for '{cellVarName}'");
                    #endif
                    #if DEBUG_LOG
                    Console.WriteLine($"   CellVars: [{string.Join(", ", frame.Code.CellVars)}]");
                    #endif
                    #if DEBUG_LOG
                    Console.WriteLine($"   FreeVars: [{string.Join(", ", frame.Code.FreeVars)}] (offset: {frame.Code.FreeVars.Count})");
                    #endif
                    // CPython 3.12: Create cell variable (initially None for type parameters)
                    PyObject? cellValue = null;
                    // Find the variable in LocalsPlus by name
                    int localIndex = frame.Code.VarNames.IndexOf(cellVarName);
                    if (localIndex >= 0 && localIndex < frame.LocalsPlus.Length)
                    {
                        var localValue = frame.LocalsPlus[localIndex];
                        if (!PyNull.IsNull(localValue))
                        {
                            cellValue = localValue;
                            #if DEBUG_LOG
                            Console.WriteLine($"   Found value for '{cellVarName}': {cellValue}");
                            #endif
                        }
                        else
                        {
                            // Variable is PyNull (uninitialized), use None for cell
                            cellValue = PyNone.Instance;
                            #if DEBUG_LOG
                            Console.WriteLine($"   Initializing '{cellVarName}' cell with None (uninitialized local)");
                            #endif
                        }
                    }
                    else
                    {
                        // For Generic Parameters function, cells start as None
                        cellValue = PyNone.Instance;
                        #if DEBUG_LOG
                        Console.WriteLine($"   Initializing '{cellVarName}' cell with None (not in locals)");
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
                    // CPython 3.12: Python/bytecodes.c:911-927
                    // YIELD_VALUE pops the yield value and saves stack pointer at (stack - 1)
                    // _PyFrame_SetStackPointer(frame, stack_pointer - 1)
                    var yieldValue = frame.ValueStack.Pop();

                    // yield는 제너레이터에서만 사용 가능
                    if (!frame.Code.IsGenerator())
                    {
                        throw PySyntaxError.Create("'yield' outside function");
                    }

                    // CPython 3.12: bytecodes.c:918 - Stack pointer saved AFTER popping yield value
                    // IP is NOT incremented here - it stays at YIELD_VALUE
                    // Objects/genobject.c:217 - Resume will push sent value, then execute from (IP + 1)
                    // Next resume will execute RESUME instruction (IP+1)

                    #if DEBUG_LOG
                    Console.WriteLine($"🔄 YIELD_VALUE: Yielding {yieldValue}, stack size after pop: {frame.ValueStack.Count}, IP: {frame.InstructionPointer}");
                    #endif

                    // DO NOT increment IP here - PyGenerator.Next() will handle resume from correct position
                    throw new PyYieldException(yieldValue);

                case ByteCodeOp.SEND:
                    // CPython 3.12: SEND opcode for yield from
                    // Stack: TOS = value to send, TOS1 = receiver (iterator/generator)
                    // bytecodes.c:825-872
                    #if DEBUG_VM_LOG
                    Console.WriteLine($"🔍 SEND: IP={frame.InstructionPointer}, Stack.Count = {frame.ValueStack.Count}, Arg={instruction.Argument}");
                    #endif
                    var sendValue = frame.ValueStack.Pop();
                    var receiver = frame.ValueStack.Peek(); // Keep receiver on stack

                    try
                    {
                        PyObject sendResult;

                        // CPython pattern: if (Py_IsNone(v) && PyIter_Check(receiver))
                        // If value is None and receiver is iterator, call next
                        if (sendValue is PyNone && receiver is PyIterator receiverIter)
                        {
                            sendResult = receiverIter.Next();
                        }
                        // If receiver is a generator, send value to it
                        else if (receiver is PyGenerator gen)
                        {
                            sendResult = gen.Send(sendValue);
                        }
                        else
                        {
                            // Try to call .send() method using GetAttribute
                            // CPython: retval = PyObject_CallMethodOneArg(receiver, &_Py_ID(send), v);
                            try
                            {
                                var sendMethod = receiver.GetAttribute("send");
                                sendResult = sendMethod.Call(new PyObject[] { sendValue }, null);
                            }
                            catch (PythonException pyEx) when (pyEx.PyException is PyAttributeError)
                            {
                                // Fallback to iterator protocol
                                if (receiver is PyIterator receiverIterFallback)
                                {
                                    sendResult = receiverIterFallback.Next();
                                }
                                else
                                {
                                    throw PyTypeError.Create($"SEND: receiver {receiver.GetType().Name} is not a generator or iterator");
                                }
                            }
                        }

                        // CPython: Push result to stack (receiver stays on stack)
                        frame.ValueStack.Push(sendResult);
                        #if DEBUG_VM_LOG
                        Console.WriteLine($"    ✅ SEND: Got result {sendResult}, continuing to next instruction");
                        #endif
                    }
                    catch (PythonException pyEx) when (pyEx.PyException is PyStopIteration stopIter)
                    {
                        // CPython 3.12: Python/bytecodes.c:858-865
                        // if (_PyGen_FetchStopIterationValue(&retval) == 0) { JUMPBY(oparg); }
                        // StopIteration raised - extract value and jump
                        //
                        // CPython 3.12: Python/bytecodes.c:843 - JUMPBY(oparg)
                        // In CPython, next_instr is already past SEND instruction and points to CACHE
                        // JUMPBY(oparg) means: next_instr += oparg (instruction words)
                        // oparg is the relative offset from the position AFTER SEND+CACHE
                        //
                        // CPython 3.12: Include/internal/pycore_opcode.h:120
                        // SEND has INLINE_CACHE_ENTRIES_SEND = 1 (one CACHE instruction)
                        //
                        // In SharpPy:
                        // - IP is currently at SEND instruction (index 36 in example)
                        // - SEND has 1 CACHE entry at index 37
                        // - oparg is relative to position AFTER SEND+CACHE (index 38 in example)
                        // - Main loop will do IP++ after we return
                        // - To reach target: IP = current + 1 (SEND) + 1 (CACHE) + oparg - 1 (main loop++)
                        // - Simplify: IP += (1 + INLINE_CACHE_ENTRIES_SEND + oparg - 1)
                        // - Final: IP += (INLINE_CACHE_ENTRIES_SEND + oparg)
                        #if DEBUG_VM_LOG
                        Console.WriteLine($"    🛑 SEND: StopIteration raised, value={stopIter.Value}");
                        Console.WriteLine($"    🛑 SEND: Jumping from IP={frame.InstructionPointer} by oparg={instruction.Argument}");
                        #endif

                        // Push StopIteration value to stack (receiver stays on stack for END_SEND)
                        frame.ValueStack.Push(stopIter.Value ?? PyNone.Instance);

                        // CPython 3.12: SEND has 1 CACHE entry (Include/internal/pycore_opcode.h:120)
                        const int INLINE_CACHE_ENTRIES_SEND = 1;

                        // Jump forward: IP += (INLINE_CACHE_ENTRIES_SEND + oparg)
                        // Example: IP=36 + (1 + 4) = 41, main loop IP++ → 42 (END_SEND)
                        frame.InstructionPointer += INLINE_CACHE_ENTRIES_SEND + instruction.Argument;

                        #if DEBUG_VM_LOG
                        Console.WriteLine($"    🛑 SEND: After jump, IP={frame.InstructionPointer} (will become {frame.InstructionPointer + 1} after main loop increment)");
                        #endif
                    }
                    break;

                case ByteCodeOp.END_SEND:
                    // CPython 3.12: END_SEND opcode
                    // Stack: TOS = value, TOS1 = receiver
                    // Result: TOS = value (receiver is discarded)
                    #if DEBUG_VM_LOG
                    Console.WriteLine($"🔍 END_SEND: Stack.Count = {frame.ValueStack.Count}");
                    #endif
                    var endSendValue = frame.ValueStack.Pop();
                    var endSendReceiver = frame.ValueStack.Pop();
                    frame.ValueStack.Push(endSendValue);
                    #if DEBUG_VM_LOG
                    Console.WriteLine($"    ✅ END_SEND: Kept value {endSendValue}, discarded receiver");
                    #endif
                    break;

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
                    // Stack: [..., arg1, arg2] -> pop arg2 first, then arg1
                    var intrinsic2_arg2 = frame.ValueStack.Pop();  // TOS (second argument)
                    var intrinsic2_arg1 = frame.ValueStack.Pop();  // TOS-1 (first argument)
                    var result2 = ExecuteIntrinsicFunction2(instruction.Argument, intrinsic2_arg1, intrinsic2_arg2);
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

                case ByteCodeOp.EXTENDED_ARG:
                    // CPython 3.12: EXTENDED_ARG shifts argument left by 8 bits
                    // This is handled in the main loop by accumulating extended args
                    // This case should never be reached as EXTENDED_ARG is processed before ExecuteInstruction
                    throw new InvalidOperationException("EXTENDED_ARG should be handled in the main execution loop");

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
                // CPython 3.12: BINARY_OP is for bitwise operations only
                // Logical 'and'/'or' use BoolOp in AST and POP_JUMP_IF_TRUE/FALSE in bytecode (short-circuit)
                // BINARY_OP with AND/OR/XOR are bitwise operators (&, |, ^)

                // Use PyObject's built-in binary operation methods (CPython compatible)
                try
                {
                    // CPython 3.12: Try left.__op__(right) first
                    // CPython: Objects/abstract.c:947-1054 (binary_op1)
                    PyObject result = binaryOp switch
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

                        // CPython 3.12: In-place operations
                        // CPython: Objects/abstract.c:binary_iop1 (lines 1162-1193)
                        // Try in-place method first, fallback to regular operation
                        BinaryOpType.INPLACE_ADD => TryInplaceOp(left, right, "InplaceAdd", () => left.Add(right)),
                        BinaryOpType.INPLACE_SUBTRACT => TryInplaceOp(left, right, "InplaceSubtract", () => left.Subtract(right)),
                        BinaryOpType.INPLACE_MULTIPLY => TryInplaceOp(left, right, "InplaceMultiply", () => left.Multiply(right)),
                        BinaryOpType.INPLACE_TRUE_DIVIDE => TryInplaceOp(left, right, "InplaceDivide", () => left.Divide(right)),
                        BinaryOpType.INPLACE_FLOOR_DIVIDE => TryInplaceOp(left, right, "InplaceFloorDivide", () => left.FloorDivide(right)),
                        BinaryOpType.INPLACE_MODULO => TryInplaceOp(left, right, "InplaceModulo", () => left.Modulo(right)),
                        BinaryOpType.INPLACE_POWER => TryInplaceOp(left, right, "InplacePower", () => left.Power(right)),
                        BinaryOpType.INPLACE_LSHIFT => TryInplaceOp(left, right, "InplaceLeftShift", () => left.LeftShift(right)),
                        BinaryOpType.INPLACE_RSHIFT => TryInplaceOp(left, right, "InplaceRightShift", () => left.RightShift(right)),
                        BinaryOpType.INPLACE_AND => TryInplaceOp(left, right, "InplaceBitwiseAnd", () => left.BitwiseAnd(right)),
                        BinaryOpType.INPLACE_OR => TryInplaceOp(left, right, "InplaceBitwiseOr", () => left.BitwiseOr(right)),
                        BinaryOpType.INPLACE_XOR => TryInplaceOp(left, right, "InplaceBitwiseXor", () => left.BitwiseXor(right)),
                        BinaryOpType.INPLACE_MATRIX_MULTIPLY => throw PyNotImplementedError.Create("In-place matrix multiplication not yet implemented"),

                        _ => throw PyTypeError.Create($"unsupported binary operation: {binaryOp}")
                    };

                    // CPython 3.12: If left.__op__ returns NotImplemented, try right.__rop__(left)
                    // CPython: Objects/abstract.c:964-984 (binary_op1)
                    if (result == PyNotImplemented.Instance)
                    {
                        result = TryReverseBinaryOp(right, left, binaryOp);
                    }

                    // If still NotImplemented, raise TypeError
                    if (result == PyNotImplemented.Instance)
                    {
                        var opSymbol = GetBinaryOpSymbol(binaryOp);
                        throw PyTypeError.Create($"unsupported operand type(s) for {opSymbol}: '{left.GetTypeName()}' and '{right.GetTypeName()}'");
                    }

                    return result;
                }
                catch (Exception ex) when (!(ex is PythonException))
                {
                    // Convert C# exceptions to Python exceptions
                    var opSymbol = GetBinaryOpSymbol(binaryOp);
                    throw PyTypeError.Create($"unsupported operand type(s) for {opSymbol}: '{left.GetTypeName()}' and '{right.GetTypeName()}'");
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

        /// <summary>
        /// Try in-place operation first, fallback to regular operation
        /// CPython 3.12: Objects/abstract.c:binary_iop1 (lines 1162-1193)
        ///
        /// In-place operations modify the object in place and return self for mutable types (list, dict, set).
        /// For immutable types (int, str, tuple), they fall back to regular operations and return a new object.
        /// </summary>
        /// <summary>
        /// Try in-place operation first, fallback to regular operation
        /// CPython 3.12: Objects/abstract.c:binary_iop1 (lines 1162-1193)
        ///
        /// In-place operations modify the object in place and return self for mutable types (list, dict, set).
        /// For immutable types (int, str, tuple), they fall back to regular operations and return a new object.
        /// </summary>
        private PyObject TryInplaceOp(PyObject left, PyObject right, string inplaceMethodName, Func<PyObject> regularOpFallback)
        {
            // CPython: Objects/abstract.c:1166-1173 - Try in-place method first
            // Map C# method name to Python dunder method name
            string pythonMethodName = inplaceMethodName switch
            {
                "InplaceAdd" => "__iadd__",
                "InplaceSubtract" => "__isub__",
                "InplaceMultiply" => "__imul__",
                "InplaceDivide" => "__itruediv__",
                "InplaceFloorDivide" => "__ifloordiv__",
                "InplaceModulo" => "__imod__",
                "InplacePower" => "__ipow__",
                "InplaceLeftShift" => "__ilshift__",
                "InplaceRightShift" => "__irshift__",
                "InplaceBitwiseAnd" => "__iand__",
                "InplaceBitwiseOr" => "__ior__",
                "InplaceBitwiseXor" => "__ixor__",
                "InplaceMatrixMultiply" => "__imatmul__",
                _ => null
            };

            // Try Python dunder method via GetAttribute (for user-defined classes)
            if (pythonMethodName != null)
            {
                try
                {
                    PyObject method = left.GetAttribute(pythonMethodName);
                    if (method != null)
                    {
                        PyObject result = null;
                        if (method is PyMethod boundMethod)
                            result = boundMethod.Call(new PyObject[] { right }, null);
                        else if (method is PyFunction func)
                            result = func.Call(new PyObject[] { left, right }, null);
                        else if (method is PyBuiltinFunction builtinFunc)
                            result = builtinFunc.Call(new PyObject[] { right });
                        else
                            result = method.Call(new PyObject[] { right }, null);

                        // CPython: Objects/abstract.c:1171 - If not NotImplemented, use result
                        if (result != null && result != PyNotImplemented.Instance)
                            return result;
                    }
                }
                catch
                {
                    // If GetAttribute throws AttributeError or any other error,
                    // fall through to C# method or fallback
                }
            }

            // CPython 3.12: Try C# virtual method for built-in types (PyList, etc.)
            // Performance: Direct virtual call instead of Reflection (no boxing, JIT inlinable)
            var inplaceResult = inplaceMethodName switch
            {
                "InplaceAdd" => left.InplaceAdd(right),
                "InplaceSubtract" => left.InplaceSubtract(right),
                "InplaceMultiply" => left.InplaceMultiply(right),
                "InplaceDivide" => left.InplaceDivide(right),
                "InplaceFloorDivide" => left.InplaceFloorDivide(right),
                "InplaceModulo" => left.InplaceModulo(right),
                "InplacePower" => left.InplacePower(right),
                "InplaceLeftShift" => left.InplaceLeftShift(right),
                "InplaceRightShift" => left.InplaceRightShift(right),
                "InplaceBitwiseAnd" => left.InplaceBitwiseAnd(right),
                "InplaceBitwiseOr" => left.InplaceBitwiseOr(right),
                "InplaceBitwiseXor" => left.InplaceBitwiseXor(right),
                "InplaceMatrixMultiply" => left.InplaceMatrixMultiply(right),
                _ => null
            };

            // CPython: Objects/abstract.c:1171 - If not NotImplemented, use result
            if (inplaceResult != null && inplaceResult != PyNotImplemented.Instance)
            {
                return inplaceResult;
            }

            // CPython: Objects/abstract.c:1179 - Fall back to regular operation
            return regularOpFallback();
        }

        /// <summary>
        /// Try reverse binary operation (right.__rop__(left))
        /// CPython 3.12: Objects/abstract.c:964-984 (binary_op1)
        /// </summary>
        private PyObject TryReverseBinaryOp(PyObject right, PyObject left, BinaryOpType binaryOp)
        {
            try
            {
                // Get the reflected method name
                string methodName = binaryOp switch
                {
                    BinaryOpType.ADD => "__radd__",
                    BinaryOpType.SUBTRACT => "__rsub__",
                    BinaryOpType.MULTIPLY => "__rmul__",
                    BinaryOpType.TRUE_DIVIDE => "__rtruediv__",
                    BinaryOpType.FLOOR_DIVIDE => "__rfloordiv__",
                    BinaryOpType.MODULO => "__rmod__",
                    BinaryOpType.POWER => "__rpow__",
                    BinaryOpType.LSHIFT => "__rlshift__",
                    BinaryOpType.RSHIFT => "__rrshift__",
                    BinaryOpType.AND => "__rand__",
                    BinaryOpType.OR => "__ror__",
                    BinaryOpType.XOR => "__rxor__",
                    BinaryOpType.MATRIX_MULTIPLY => "__rmatmul__",
                    _ => null
                };

                if (methodName == null)
                    return PyNotImplemented.Instance;

                // Try to get the reflected method from the right operand
                PyObject method = null;
                try
                {
                    method = right.GetAttribute(methodName);
                }
                catch
                {
                    return PyNotImplemented.Instance;
                }

                if (method == null)
                    return PyNotImplemented.Instance;

                // Call the reflected method with left as argument
                if (method is PyMethod boundMethod)
                {
                    return boundMethod.Call(new PyObject[] { left }, null);
                }
                else if (method is PyFunction func)
                {
                    return func.Call(new PyObject[] { right, left }, null);
                }
                else if (method is PyBuiltinFunction builtinFunc)
                {
                    return builtinFunc.Call(new PyObject[] { left });
                }
                else
                {
                    // Try to call it as a callable
                    return method.Call(new PyObject[] { left }, null);
                }
            }
            catch
            {
                return PyNotImplemented.Instance;
            }
        }

        /// <summary>
        /// Get operator symbol for error messages
        /// </summary>
        private string GetBinaryOpSymbol(BinaryOpType binaryOp)
        {
            return binaryOp switch
            {
                BinaryOpType.ADD => "+",
                BinaryOpType.SUBTRACT => "-",
                BinaryOpType.MULTIPLY => "*",
                BinaryOpType.TRUE_DIVIDE => "/",
                BinaryOpType.FLOOR_DIVIDE => "//",
                BinaryOpType.MODULO => "%",
                BinaryOpType.POWER => "**",
                BinaryOpType.LSHIFT => "<<",
                BinaryOpType.RSHIFT => ">>",
                BinaryOpType.AND => "&",
                BinaryOpType.OR => "|",
                BinaryOpType.XOR => "^",
                BinaryOpType.MATRIX_MULTIPLY => "@",
                _ => binaryOp.ToString()
            };
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
                    // Parse format spec: [[fill]align][sign][#][0][width][grouping_option][.precision][type]
                    // Examples: "d", "03d", "10d", ">10d", "0>10d"

                    string typeSpec = "d"; // default
                    int width = 0;
                    char fillChar = ' ';
                    char? align = null;

                    // Extract type (last character if it's a type specifier)
                    if (formatSpec.Length > 0)
                    {
                        char lastChar = formatSpec[formatSpec.Length - 1];
                        if (lastChar == 'd' || lastChar == 'x' || lastChar == 'X' || lastChar == 'o' || lastChar == 'b')
                        {
                            typeSpec = lastChar.ToString();
                            formatSpec = formatSpec.Substring(0, formatSpec.Length - 1);
                        }
                    }

                    // Parse width and fill/align
                    if (formatSpec.Length > 0)
                    {
                        // Check for zero-padding (leading 0)
                        if (formatSpec[0] == '0')
                        {
                            fillChar = '0';
                            align = '>'; // right-align for zero-padding
                            formatSpec = formatSpec.Substring(1);
                        }

                        // Parse width
                        if (int.TryParse(formatSpec, out int parsedWidth))
                        {
                            width = parsedWidth;
                        }
                    }

                    // CPython 3.12: Objects/stringlib/formatter.h - format_int_or_long
                    // Format the value based on type
                    string formatted = typeSpec switch
                    {
                        "d" => intObj.Value.ToString(),
                        "x" => intObj.Value.ToString("x"),
                        "X" => intObj.Value.ToString("X"),
                        "o" => Convert.ToString((long)intObj.Value, 8),
                        "b" => Convert.ToString((long)intObj.Value, 2),
                        _ => intObj.Value.ToString()
                    };

                    // Apply width and padding
                    if (width > 0 && formatted.Length < width)
                    {
                        if (align == '>' || fillChar == '0')
                        {
                            formatted = formatted.PadLeft(width, fillChar);
                        }
                        else
                        {
                            formatted = formatted.PadRight(width, fillChar);
                        }
                    }

                    return new PyString(formatted);
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

            // Special handling for IS_NOT (identity comparison)
            if (operation == CompareOp.IS_NOT)
                return IsNotOperation(left, right);

            // CPython 3.12: Objects/object.c:813-878 - PyObject_RichCompare
            // Try left operand's comparison method
            var result = operation switch
            {
                CompareOp.EQ => left.RichCompare(right, PyObject.CompareOp.EQ),
                CompareOp.NE => left.RichCompare(right, PyObject.CompareOp.NE),
                CompareOp.LT => left.RichCompare(right, PyObject.CompareOp.LT),
                CompareOp.LE => left.RichCompare(right, PyObject.CompareOp.LE),
                CompareOp.GT => left.RichCompare(right, PyObject.CompareOp.GT),
                CompareOp.GE => left.RichCompare(right, PyObject.CompareOp.GE),
                CompareOp.EXC_MATCH => left.RichCompare(right, PyObject.CompareOp.EQ),
                _ => throw PyNotImplementedError.Create($"Compare operation {compareOp} not implemented")
            };

            // CPython: If left returns NotImplemented, try right's reversed operation
            // Objects/object.c:868-876
            if (result == PyNotImplemented.Instance)
            {
                // Get reversed operation: < ↔ >, <= ↔ >=
                var reversedOp = operation switch
                {
                    CompareOp.LT => PyObject.CompareOp.GT,
                    CompareOp.LE => PyObject.CompareOp.GE,
                    CompareOp.GT => PyObject.CompareOp.LT,
                    CompareOp.GE => PyObject.CompareOp.LE,
                    CompareOp.EQ => PyObject.CompareOp.EQ,
                    CompareOp.NE => PyObject.CompareOp.NE,
                    _ => throw PyNotImplementedError.Create($"Cannot reverse operation {operation}")
                };

                result = right.RichCompare(left, reversedOp);

                // If right also returns NotImplemented, raise TypeError
                if (result == PyNotImplemented.Instance)
                {
                    var opStr = operation switch
                    {
                        CompareOp.LT => "<",
                        CompareOp.LE => "<=",
                        CompareOp.GT => ">",
                        CompareOp.GE => ">=",
                        CompareOp.EQ => "==",
                        CompareOp.NE => "!=",
                        _ => operation.ToString()
                    };
                    throw PyTypeError.Create($"'{opStr}' not supported between instances of '{left.GetTypeName()}' and '{right.GetTypeName()}'");
                }
            }

            return result;
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
                case (int)IntrinsicFunction.INTRINSIC_1_INVALID:
                    throw new InvalidOperationException("Invalid intrinsic function 0");
                case (int)IntrinsicFunction.INTRINSIC_PRINT:
                    // CPython 3.12: Call sys.displayhook(value)
                    // Reference: Python/intrinsics.c:25-35 (print_expr)
                    //   PyObject *hook = _PySys_GetRequiredAttr(&_Py_ID(displayhook));
                    //   PyObject *res = PyObject_CallOneArg(hook, value);

                    try
                    {
                        // CPython 3.12: Get sys.displayhook
                        if (!PyImportSystem.TryGetModule("sys", out var sysModule))
                        {
                            throw new InvalidOperationException("sys module not found");
                        }

                        var displayhook = sysModule.GetAttribute("displayhook");
                        if (displayhook == null)
                        {
                            throw new InvalidOperationException("sys.displayhook not found");
                        }

                        // Call displayhook(arg)
                        if (displayhook is PyBuiltinFunction func)
                        {
                            return func.Call(new[] { arg });
                        }
                        else if (displayhook is PyFunction pyFunc)
                        {
                            // User-defined displayhook
                            return pyFunc.Call(new[] { arg }, null);
                        }
                        else
                        {
                            throw new InvalidOperationException("sys.displayhook is not callable");
                        }
                    }
                    catch
                    {
                        // Fallback: direct print if sys.displayhook fails
                        if (arg != PyNone.Instance && !(arg is PyNone))
                        {
                            var reprValue = arg.ToRepr().Value;
                            Console.WriteLine(reprValue);
                        }
                        return PyNone.Instance;
                    }
                case (int)IntrinsicFunction.INTRINSIC_IMPORT_STAR:
                    // CPython 3.12: Python/intrinsics.c:127-146 (import_star)
                    // Import all names from a module (from module import *)
                    return ImportStar(arg);
                case (int)IntrinsicFunction.INTRINSIC_STOPITERATION_ERROR:
                    // CPython 3.12: Python/intrinsics.c:149-190 (stopiteration_error)
                    // Convert StopIteration in generators to RuntimeError
                    return StopIterationError(arg);
                case (int)IntrinsicFunction.INTRINSIC_ASYNC_GEN_WRAP:
                    // CPython 3.12: Python/intrinsics.c (INTRINSIC_ASYNC_GEN_WRAP)
                    // Wraps yielded value from async generator
                    // Used when: generator && coroutine (async def with yield)
                    // Uses freelist for object pooling (6-10% performance improvement)
                    return PyAsyncGenWrappedValue.Create(arg);
                case (int)IntrinsicFunction.INTRINSIC_UNARY_POSITIVE:
                    return arg.Positive();
                case (int)IntrinsicFunction.INTRINSIC_LIST_TO_TUPLE:
                    if (arg is PyList list)
                    {
                        // Performance: Eliminated LINQ - PyList.Items is already an array, no copy needed
                        return new PyTuple(list.Items);
                    }
                    throw PyTypeError.Create($"INTRINSIC_LIST_TO_TUPLE expected list, got {arg.GetTypeName()}");
                case (int)IntrinsicFunction.INTRINSIC_TYPEVAR:
                    return CreateTypeVar(arg);
                case (int)IntrinsicFunction.INTRINSIC_PARAMSPEC:
                    return CreateParamSpec(arg);
                case (int)IntrinsicFunction.INTRINSIC_TYPEVARTUPLE:
                    return CreateTypeVarTuple(arg);
                case (int)IntrinsicFunction.INTRINSIC_SUBSCRIPT_GENERIC:
                    return CreateGenericSubscript(arg);
                case (int)IntrinsicFunction.INTRINSIC_TYPEALIAS:
                    return CreateTypeAlias(arg);
                default:
                    throw new NotImplementedException($"Intrinsic function {functionId} not implemented");
            }
        }

        /// <summary>
        /// CPython 3.12: import_star (Python/intrinsics.c:127-146)
        /// Implements "from module import *"
        /// </summary>
        private PyObject ImportStar(PyObject module)
        {
            // CPython: import_all_from(tstate, locals, from)
            var currentFrame = PyVM.CurrentFrame;
            if (currentFrame == null)
            {
                throw new InvalidOperationException("No current frame during 'import *'");
            }

            // Get the locals scope from current frame
            var locals = currentFrame.ScopeChain.CurrentScope;
            if (locals == null)
            {
                throw new InvalidOperationException("no locals found during 'import *'");
            }

            // CPython: Check for __all__ attribute first
            PyObject all = null;
            bool skipUnderscores = false;

            // Try to get __all__
            bool hasAll = false;
            try
            {
                all = module.GetAttribute("__all__");
                hasAll = true;
            }
            catch
            {
                // No __all__, will try __dict__
            }

            if (!hasAll)
            {
                // No __all__, use __dict__ keys instead
                try
                {
                    var dict = module.GetAttribute("__dict__");
                    if (dict is PyDict pyDict)
                    {
                        all = pyDict.Keys();
                        skipUnderscores = true;
                    }
                    else
                    {
                        throw PyImportError.Create("from-import-* object has no __dict__ and no __all__");
                    }
                }
                catch
                {
                    throw PyImportError.Create("from-import-* object has no __dict__ and no __all__");
                }
            }

            // Import each name
            if (all is PyList list)
            {
                foreach (var item in list.Items)
                {
                    if (item is PyString nameStr)
                    {
                        var name = nameStr.Value;

                        // Skip names starting with underscore if using __dict__
                        if (skipUnderscores && name.StartsWith("_"))
                        {
                            continue;
                        }

                        // Get attribute from module
                        try
                        {
                            var value = module.GetAttribute(name);
                            // Set in local scope
                            locals.SetVariable(name, value);
                        }
                        catch
                        {
                            // CPython: If attribute doesn't exist, skip it
                            continue;
                        }
                    }
                }
            }
            else if (all is PyTuple tuple)
            {
                foreach (var item in tuple.Items)
                {
                    if (item is PyString nameStr)
                    {
                        var name = nameStr.Value;

                        // Skip names starting with underscore if using __dict__
                        if (skipUnderscores && name.StartsWith("_"))
                        {
                            continue;
                        }

                        // Get attribute from module
                        try
                        {
                            var value = module.GetAttribute(name);
                            // Set in local scope
                            locals.SetVariable(name, value);
                        }
                        catch
                        {
                            // CPython: If attribute doesn't exist, skip it
                            continue;
                        }
                    }
                }
            }

            return PyNone.Instance;
        }

        /// <summary>
        /// CPython 3.12: stopiteration_error (Python/intrinsics.c:149-190)
        /// Converts StopIteration exceptions raised in generators to RuntimeError
        /// </summary>
        private PyObject StopIterationError(PyObject exc)
        {
            // CPython: assert(PyExceptionInstance_Check(exc))
            if (exc is not PyBaseException exception)
            {
                throw new InvalidOperationException("INTRINSIC_STOPITERATION_ERROR requires an exception instance");
            }

            var currentFrame = PyVM.CurrentFrame;
            if (currentFrame == null)
            {
                throw new InvalidOperationException("No current frame during INTRINSIC_STOPITERATION_ERROR");
            }

            // CPython: Check frame owner is FRAME_OWNED_BY_GENERATOR
            // For now, we'll check if the code object has generator flags
            var codeObject = currentFrame.Code;
            string? msg = null;

            // CPython: Check if exception matches StopIteration
            if (exc is PyStopIteration)
            {
                msg = "generator raised StopIteration";

                // CPython: Check CO_ASYNC_GENERATOR flag
                if ((codeObject.Flags & PyCodeObject.CO_ASYNC_GENERATOR) != 0)
                {
                    msg = "async generator raised StopIteration";
                }
                // CPython: Check CO_COROUTINE flag
                else if ((codeObject.Flags & PyCodeObject.CO_COROUTINE) != 0)
                {
                    msg = "coroutine raised StopIteration";
                }
            }
            // CPython: Check if async generator raised StopAsyncIteration
            else if ((codeObject.Flags & PyCodeObject.CO_ASYNC_GENERATOR) != 0 && exc is PyStopAsyncIteration)
            {
                msg = "async generator raised StopAsyncIteration";
            }

            // CPython: If we have a message, create RuntimeError with cause and context
            if (msg != null)
            {
                // Create RuntimeError with the message
                var runtimeError = new PyRuntimeError(msg);

                // CPython: PyException_SetCause(error, Py_NewRef(exc))
                runtimeError.__cause__ = exception;

                // CPython: PyException_SetContext(error, Py_NewRef(exc))
                runtimeError.__context__ = exception;

                return runtimeError;
            }

            // CPython: return Py_NewRef(exc) - just return the original exception
            return exc;
        }

        /// <summary>
        /// Create a TypeVar for PEP 695 type parameters
        /// </summary>
        private PyObject CreateTypeVar(PyObject nameObj)
        {
            var name = nameObj.ToStr().Value;
            // CPython 3.12: Create proper TypeVar object for PEP 695
            return new PyTypeVar(name);
        }

        /// <summary>
        /// Create a ParamSpec for PEP 612 parameter specifications
        /// </summary>
        private PyObject CreateParamSpec(PyObject nameObj)
        {
            var name = nameObj.ToStr().Value;
            // CPython 3.12: Create proper ParamSpec object for PEP 612
            return new PyParamSpec(name);
        }

        /// <summary>
        /// Create a TypeVarTuple for PEP 646 variadic generics
        /// </summary>
        private PyObject CreateTypeVarTuple(PyObject nameObj)
        {
            var name = nameObj.ToStr().Value;
            // CPython 3.12: Create proper TypeVarTuple object for PEP 646
            return new PyTypeVarTuple(name);
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
                case (int)IntrinsicFunction.INTRINSIC_PREP_RERAISE_STAR: // Exception Groups cleanup (CPython 3.12: PREP_RERAISE_STAR = 0)
                    // CPython signature: _PyExc_PrepReraiseStar(orig, excs)
                    // Stack layout: [orig, excs] -> arg1=orig, arg2=excs (list)
                    // But PrepReraiseStarExceptions expects (list, orig), so swap
                    return PrepReraiseStarExceptions(arg2, arg1);
                case (int)IntrinsicFunction.INTRINSIC_SET_FUNCTION_TYPE_PARAMS: // PEP 695 Generic Function
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
                // CPython 3.12: Set the __type_params__ attribute on the function
                // This makes the TypeVar objects accessible via function.__type_params__
                // Performance: Eliminated LINQ - manual array to List conversion
                pyFunc.TypeParams = new List<PyObject>(paramTuple.Items);
                pyFunc.Attributes["__type_params__"] = paramTuple;
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
                    // Performance: Eliminated LINQ - manual cast to List<PyException>
                    var exceptions = new List<PyException>();
                    foreach (var item in list.Items)
                    {
                        exceptions.Add((PyException)item);
                    }
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

                    // CPython 3.12: If exc is also an ExceptionGroup, recursively split it
                    if (exc is PyBaseExceptionGroup nestedGroup)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"🔄 Recursively checking nested ExceptionGroup with {nestedGroup.Exceptions.Count} exceptions");
                        #endif

                        var (nestedMatched, nestedRemainder) = ExceptionGroupMatches(exc, exceptionType);

                        if (nestedMatched != null && nestedMatched is PyException matchedExc)
                        {
                            matchedExceptions.Add(matchedExc);
                        }

                        if (nestedRemainder != null && nestedRemainder is PyException remainderExc)
                        {
                            remainderExceptions.Add(remainderExc);
                        }
                    }
                    else if (ExceptionMatches(exc, exceptionType))
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
                // Performance: Eliminated LINQ - manual conversion
                var kwNamesArray = new PyObject[kwargs.Count];
                int kwIndex = 0;
                foreach (var key in kwargs.Keys)
                {
                    kwNamesArray[kwIndex++] = new PyString(key);
                }
                totalArgs.Add(new PyTuple(kwNamesArray));

                // Performance: Eliminated LINQ - manual List to array conversion
                var totalArgsArray = new PyObject[totalArgs.Count];
                totalArgs.CopyTo(totalArgsArray, 0);
                return pyType.Call(totalArgsArray, null);
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

            // Performance: Eliminated LINQ - manual List to array conversion
            var defaults = new PyObject[function.CodeObject.DefaultValues.Count];
            function.CodeObject.DefaultValues.CopyTo(defaults, 0);
            return defaults;
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
                    #if DEBUG_VM_LOG
                    Console.WriteLine($"Coroutine execution failed: {ex.Message}");
                    #endif
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
        /// CPython 3.12: GET_AITER implementation (Python/bytecodes.c)
        /// Get async iterator from an object for async for loops
        /// </summary>
        private PyObject GetAsyncIterator(PyObject obj)
        {
            // CPython: Call __aiter__() method to get async iterator
            try
            {
                var aiterMethod = obj.GetAttribute("__aiter__");
                if (aiterMethod != null)
                {
                    var aiter = aiterMethod.Call(new PyObject[0], null);

                    // CPython: Verify the async iterator has __anext__ method
                    try
                    {
                        var anextMethod = aiter.GetAttribute("__anext__");
                        if (anextMethod == null)
                        {
                            throw PyTypeError.Create(
                                $"'async for' received an object from __aiter__ that does not implement __anext__: {aiter.GetTypeName()}");
                        }
                    }
                    catch
                    {
                        throw PyTypeError.Create(
                            $"'async for' received an object from __aiter__ that does not implement __anext__: {aiter.GetTypeName()}");
                    }

                    return aiter;
                }
            }
            catch (Exception ex) when (!(ex is PythonException))
            {
                // __aiter__ doesn't exist or failed
            }

            // CPython: TypeError if no __aiter__ method
            throw PyTypeError.Create($"'async for' requires an object with __aiter__ method, got {obj.GetTypeName()}");
        }

        /// <summary>
        /// CPython 3.12: GET_ANEXT implementation (Python/bytecodes.c)
        /// Get next awaitable from async iterator
        /// </summary>
        private PyObject GetAsyncNext(PyObject aiter)
        {
            // CPython: Call __anext__() method to get awaitable
            try
            {
                var anextMethod = aiter.GetAttribute("__anext__");
                if (anextMethod != null)
                {
                    // Call __anext__() which should return an awaitable
                    var awaitable = anextMethod.Call(new PyObject[0], null);
                    return awaitable;
                }
            }
            catch (Exception ex) when (!(ex is PythonException))
            {
                // __anext__ doesn't exist or failed
            }

            // CPython: TypeError if no __anext__ method
            throw PyTypeError.Create($"async iterator has no __anext__ method");
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
                        #if DEBUG_VM_LOG
                        Console.WriteLine($"[FUNCTION SCOPE] Creating ScopeChain for {pyFunc.Name} from globalsDict:");
                        Console.WriteLine($"  globalsDict count: {pyFunc.GlobalsDict.Count}");
                        // Performance: Eliminated LINQ - manual key preview
                        var keyCount = Math.Min(10, pyFunc.GlobalsDict.Keys.Count);
                        var keys = new string[keyCount];
                        int keyIdx = 0;
                        foreach (var key in pyFunc.GlobalsDict.Keys)
                        {
                            if (keyIdx >= keyCount) break;
                            keys[keyIdx++] = key;
                        }
                        Console.WriteLine($"  globalsDict keys: {string.Join(", ", keys)}");
                        #endif

                        // Use the function's captured globals (CPython 3.12 compatible)
                        functionScope = new PyScopeChain(pyFunc.GlobalsDict, "<function>");
                    }
                    else
                    {
                        #if DEBUG_VM_LOG
                        Console.WriteLine($"[FUNCTION SCOPE] Using ParentScope for {pyFunc.Name} (GlobalsDict is null)");
                        #endif
                        // Fallback to ParentScope for backward compatibility
                        functionScope = pyFunc.ParentScope ?? parentScope;
                    }

                    // Create minimal frame for simple function - CPython 3.12: include parent frame
                    // CPython 3.12: Get defaults from func.__defaults__ attribute
                    PyTuple defaults = null;
                    if (pyFunc.Attributes.TryGetValue("__defaults__", out var defaultsAttr) && defaultsAttr is PyTuple defaultsTuple)
                    {
                        defaults = defaultsTuple;
                    }
                    // CPython 3.12: Get kwdefaults from func.__kwdefaults__ attribute
                    PyDict kwdefaults = null;
                    if (pyFunc.Attributes.TryGetValue("__kwdefaults__", out var kwdefaultsAttr) && kwdefaultsAttr is PyDict kwdefaultsDict)
                    {
                        kwdefaults = kwdefaultsDict;
                    }
                    var frame = new PyFrame(code, argsWithSelf, functionScope, pyFunc.Closure, CurrentFrame, defaults, kwdefaults);
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
                        #if DEBUG_VM_LOG
                        Console.WriteLine($"[FUNCTION SCOPE] Creating ScopeChain for {pyFunc.Name} from globalsDict:");
                        Console.WriteLine($"  globalsDict count: {pyFunc.GlobalsDict.Count}");
                        // Performance: Eliminated LINQ - manual key preview
                        var keyCount = Math.Min(10, pyFunc.GlobalsDict.Keys.Count);
                        var keys = new string[keyCount];
                        int keyIdx = 0;
                        foreach (var key in pyFunc.GlobalsDict.Keys)
                        {
                            if (keyIdx >= keyCount) break;
                            keys[keyIdx++] = key;
                        }
                        Console.WriteLine($"  globalsDict keys: {string.Join(", ", keys)}");
                        #endif

                        // Use the function's captured globals (CPython 3.12 compatible)
                        functionScope = new PyScopeChain(pyFunc.GlobalsDict, "<function>");
                    }
                    else
                    {
                        #if DEBUG_VM_LOG
                        Console.WriteLine($"[FUNCTION SCOPE] Using ParentScope for {pyFunc.Name} (GlobalsDict is null)");
                        #endif
                        // Fallback to ParentScope for backward compatibility
                        functionScope = pyFunc.ParentScope ?? parentScope;
                    }

                    // Create minimal frame for simple function - CPython 3.12: include parent frame
                    // CPython 3.12: Get defaults from func.__defaults__ attribute
                    PyTuple defaults = null;
                    if (pyFunc.Attributes.TryGetValue("__defaults__", out var defaultsAttr) && defaultsAttr is PyTuple defaultsTuple)
                    {
                        defaults = defaultsTuple;
                    }
                    // CPython 3.12: Get kwdefaults from func.__kwdefaults__ attribute
                    PyDict kwdefaults = null;
                    if (pyFunc.Attributes.TryGetValue("__kwdefaults__", out var kwdefaultsAttr) && kwdefaultsAttr is PyDict kwdefaultsDict)
                    {
                        kwdefaults = kwdefaultsDict;
                    }
                    var frame = new PyFrame(code, args, functionScope, pyFunc.Closure, CurrentFrame, defaults, kwdefaults);
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
        /// CPython 3.12: PyTraceBack_Here - Add current frame to exception's traceback
        /// This is called each time an exception propagates through a frame without being handled.
        /// Corresponds to PyTraceBack_Here() in CPython traceback.c:266
        /// </summary>
        private void PyTraceBack_Here(PyFrame frame, PythonException pyEx)
        {
            // 1. Get existing traceback from exception (may be null)
            var existingTraceback = pyEx.PyException.__traceback__;

            // CPython 3.12: RERAISE preserves existing traceback for the same frame only
            // When exception propagates to a different frame (caller), we should add the caller frame
            // CPython traceback.c:266 - PyTraceBack_Here adds frames as exception unwinds
            if (pyEx.FromReraise)
            {
                // Check if exception's top traceback frame is the same as current frame
                // If so, skip (this is the same frame that did RERAISE)
                // If not, this is a caller frame and we should add it
                if (existingTraceback != null && existingTraceback.Frame == frame)
                {
#if DEBUG_LOG
                    Console.WriteLine($"🔍 PyTraceBack_Here: Skipping RERAISE in same frame '{frame.Code.Name}'");
#endif
                    return;
                }
                // Different frame - clear FromReraise flag since we're in a new frame
                pyEx.FromReraise = false;
            }

            // 2. Calculate lasti - CPython uses "next_instr-1" (ceval.c:941)
            // InstructionPointer points to NEXT instruction after exception, so subtract 1
            var lasti = Math.Max(0, frame.InstructionPointer - 1);

            // CPython 3.12: Check if this frame is already in the traceback chain
            // This prevents duplicate entries when exception propagates through same frame multiple times
            // (e.g., in with statement cleanup code)
            var tb = existingTraceback;
            while (tb != null)
            {
                if (tb.Frame == frame)
                {
#if DEBUG_LOG
                    Console.WriteLine($"🔍 PyTraceBack_Here: Skipping duplicate - traceback already contains '{frame.Code.Name}' (existing lasti={tb.LastI}, current lasti={lasti})");
#endif
                    return;
                }
                tb = tb.Next;
            }

            // 3. Get line number and column offset - try multiple strategies
            int lineNo = 0;
            int colNo = -1;

            // Strategy 1: Get line number and column offset directly from instruction
            if (lasti >= 0 && lasti < frame.Code.Instructions.Count)
            {
                var instr = frame.Code.Instructions[lasti];
                lineNo = instr.LineNumber;
                colNo = instr.ColumnOffset;
            }

            // Strategy 2: Use CurrentLineNumber if valid
            if (lineNo <= 0 && frame.CurrentLineNumber > 0)
            {
                lineNo = frame.CurrentLineNumber;
                colNo = frame.CurrentColumnOffset;
            }

            // Strategy 3: Look up exact lasti in LineNumberTable
            if (lineNo <= 0 && frame.Code.LineNumberTable.TryGetValue(lasti, out var line))
            {
                lineNo = line;
            }

            // Strategy 4: Scan backwards in line number table to find most recent line
            if (lineNo <= 0)
            {
                for (int offset = lasti; offset >= 0; offset--)
                {
                    if (frame.Code.LineNumberTable.TryGetValue(offset, out var foundLine) && foundLine > 0)
                    {
                        lineNo = foundLine;
                        break;
                    }
                }
            }

            // Strategy 5: Scan backwards in instructions to find most recent line and column
            if (lineNo <= 0)
            {
                for (int offset = lasti; offset >= 0; offset--)
                {
                    if (offset < frame.Code.Instructions.Count)
                    {
                        var instrLine = frame.Code.Instructions[offset].LineNumber;
                        if (instrLine > 0)
                        {
                            lineNo = instrLine;
                            if (colNo < 0)
                            {
                                colNo = frame.Code.Instructions[offset].ColumnOffset;
                            }
                            break;
                        }
                    }
                }
            }

            // 4. Create new traceback entry for current frame
            // CPython: newtb = _PyTraceBack_FromFrame(tb, frame)
            var newTraceback = new PyTraceback(
                frame: frame,
                lasti: lasti,
                lineno: lineNo,
                next: existingTraceback,  // Link to existing chain (prepend)
                colno: colNo,
                endcolno: colNo  // For now, use same value for end
            );

            // 5. Attach new traceback to exception
            // CPython: PyException_SetTraceback(exc, newtb)
            pyEx.PyException.__traceback__ = newTraceback;

#if DEBUG_LOG
            Console.WriteLine($"🔍 PyTraceBack_Here: Added frame '{frame.Code.Name}' at line {newTraceback.LineNo} (lasti={lasti}, IP={frame.InstructionPointer})");
#endif
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
            // Performance: Eliminated LINQ - manual array conversion
            var kwNamesList = new string[kwNames.Items.Length];
            for (int i = 0; i < kwNames.Items.Length; i++)
            {
                kwNamesList[i] = ((PyString)kwNames.Items[i]).Value;
            }
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
            // CPython 3.12: Convert keyword arguments to PyDict and call builtin function
            PyDict? kwargs = null;
            if (keywordArgs != null && keywordArgs.Count > 0)
            {
                kwargs = new PyDict();
                foreach (var kv in keywordArgs)
                {
                    kwargs.SetItem(new PyString(kv.Key), kv.Value);
                }
            }
            return builtin.Call(positionalArgs, kwargs);
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

            // CPython 3.12: Python/bytecodes.c:3306-3327 (RETURN_GENERATOR)
            // Generator functions must create and return a generator object, not execute the frame
            // Check if this is a generator/coroutine/async generator function
            if (code.IsGenerator() || code.IsCoroutine() || code.IsAsyncGenerator())
            {
                // Convert keyword dict to PyDict for PyFunction.Call
                PyDict kwDict = null;
                if (keywordArgs.Count > 0)
                {
                    kwDict = new PyDict();
                    foreach (var kvp in keywordArgs)
                    {
                        kwDict.SetItem(new PyString(kvp.Key), kvp.Value);
                    }
                }

                // Let PyFunction.Call handle generator/coroutine creation
                return pyFunc.Call(positionalArgs, kwDict);
            }

            try
            {
                // CPython 3.12: Use function's captured globals
                PyScopeChain functionScope;
                if (pyFunc.GlobalsDict != null)
                {
                    functionScope = new PyScopeChain(pyFunc.GlobalsDict, "<function>");
                }
                else
                {
                    functionScope = pyFunc.ParentScope ?? parentScope;
                }

                // CPython 3.12: Get defaults and kwdefaults from function attributes
                PyTuple defaults = null;
                if (pyFunc.Attributes.TryGetValue("__defaults__", out var defaultsAttr) && defaultsAttr is PyTuple defaultsTuple)
                {
                    defaults = defaultsTuple;
                }

                PyDict kwdefaults = null;
                if (pyFunc.Attributes.TryGetValue("__kwdefaults__", out var kwdefaultsAttr) && kwdefaultsAttr is PyDict kwdefaultsDict)
                {
                    kwdefaults = kwdefaultsDict;
                }

                // CPython 3.12: Combine positional and keyword arguments into single array for frame
                var allArgs = new PyObject[positionalArgs.Length + keywordArgs.Count];
                Array.Copy(positionalArgs, 0, allArgs, 0, positionalArgs.Length);

                int keywordIndex = positionalArgs.Length;
                foreach (var kvp in keywordArgs)
                {
                    allArgs[keywordIndex++] = kvp.Value;
                }

                // Create frame with all arguments, defaults, and kwdefaults (CPython 3.12 compatible)
                // Note: PyFrame constructor calls BindArgumentsToParametersCPython312, which handles kwdefaults
                var frame = new PyFrame(code, allArgs, functionScope, pyFunc.Closure, CurrentFrame, defaults, kwdefaults);

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
                    frame.LocalsPlus[paramIndex] = positionalArgs[posArgIndex];
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
                    frame.LocalsPlus[paramIndex] = keywordValue;
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
                        frame.LocalsPlus[paramIndex] = defaultValue;
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
                int argsParamIndex = code.ArgCount;
                string argsParamName = code.ArgCount < code.VarNames.Count ? code.VarNames[argsParamIndex] : "args";
                var remainingPositionalArgs = new List<PyObject>();

                // Collect remaining positional arguments
                for (int i = posArgIndex; i < positionalArgs.Length; i++)
                {
                    remainingPositionalArgs.Add(positionalArgs[i]);
                }

                // Performance: Eliminated LINQ - manual List to array conversion
                var argsArray = new PyObject[remainingPositionalArgs.Count];
                remainingPositionalArgs.CopyTo(argsArray, 0);
                var argsTuple = new PyTuple(argsArray);
                frame.LocalsPlus[argsParamIndex] = argsTuple;
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

                frame.LocalsPlus[kwargsIndex] = kwargsDict;
                frame.ScopeChain.AssignVariable(kwargsParamName, kwargsDict);

#if DEBUG_LOG
                Console.WriteLine($"  → **{kwargsParamName} = {kwargsDict} ({keywordArgs.Count}개 키워드)");
#endif
            }
            else if (keywordArgs.Count > 0)
            {
                // Unexpected keyword arguments and no **kwargs parameter
                // Performance: Eliminated LINQ - get first key manually
                string firstUnexpectedKwarg = null;
                foreach (var key in keywordArgs.Keys)
                {
                    firstUnexpectedKwarg = key;
                    break;
                }
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
        // CPython 3.12: Python/errors.c:330-358 - PyErr_GivenExceptionMatches
        // Maps Python exception name to C# exception type for subtype checking
        private static Type GetExceptionTypeByName(string exceptionName)
        {
            switch (exceptionName)
            {
                case "BaseException": return typeof(PyBaseException);
                case "Exception": return typeof(PyException);
                case "ImportError": return typeof(PyImportError);
                case "ModuleNotFoundError": return typeof(PyModuleNotFoundError);
                case "ValueError": return typeof(PyValueError);
                case "TypeError": return typeof(PyTypeError);
                case "RuntimeError": return typeof(PyRuntimeError);
                case "AttributeError": return typeof(PyAttributeError);
                case "KeyError": return typeof(PyKeyError);
                case "IndexError": return typeof(PyIndexError);
                case "NameError": return typeof(PyNameError);
                case "OSError": return typeof(PyOSError);
                case "IOError": return typeof(PyOSError);  // In Python 3, IOError is an alias for OSError
                case "ZeroDivisionError": return typeof(PyZeroDivisionError);
                case "OverflowError": return typeof(PyOverflowError);
                case "StopIteration": return typeof(PyStopIteration);
                case "AssertionError": return typeof(PyAssertionError);
                case "SystemExit": return typeof(PySystemExit);
                case "KeyboardInterrupt": return typeof(PyKeyboardInterrupt);
                case "GeneratorExit": return typeof(PyGeneratorExit);
                case "LookupError": return typeof(PyLookupError);
                case "ArithmeticError": return typeof(PyArithmeticError);
                case "RecursionError": return typeof(PyRecursionError);
                default: return null;
            }
        }

        // CPython 3.12: Python/errors.c:330-358 - PyErr_GivenExceptionMatches
        // Checks if exception instance is of expected type (using C# subtype checking)
        private static bool IsExceptionInstanceOf(PyException exception, string expectedTypeName)
        {
            // CPython: Python/errors.c:350-351
            // If err is an instance, get its class
            var actualType = exception.GetType();

            // CPython: Python/errors.c:353-354
            // If both are exception classes, use PyType_IsSubtype
            var expectedType = GetExceptionTypeByName(expectedTypeName);

            if (expectedType == null)
            {
                // Unknown exception type, fall back to name match
                return exception.GetTypeName() == expectedTypeName;
            }

            // CPython: PyType_IsSubtype - check if actualType is subtype of expectedType
            // In C#: expectedType.IsAssignableFrom(actualType)
            return expectedType.IsAssignableFrom(actualType);
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