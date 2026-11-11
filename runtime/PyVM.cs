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
        private PyDict _localsDict;
        // _PyInterpreterFrame.prev_instr: the instruction being run, in code units
        public int LastInstruction;

        /// <summary>f_trace, and what the line event decides from.</summary>
        internal PyObject Trace;
        internal bool TraceLines = true;
        internal int TraceLine = -1;
        internal int TraceLastInstruction;

        public int InstructionPointer { get; set; }

        // CPython 3.12: Frame chain for proper call stack tracking
        public PyFrame? ParentFrame { get; set; }     // 부모 프레임 (call stack)

        // CPython 3.12: f_globals - Reference to module's __dict__ for import resolution
        public IDictionary<string, PyObject> Globals { get; private set; }

        private bool _builtinsResolved;
        private PyObject _ownBuiltins;

        // _PyEval_BuiltinsFromGlobals, once per frame: null is the builtins module itself.
        private void ResolveBuiltins()
        {
            _builtinsResolved = true;
            if (!Globals.TryGetValue("__builtins__", out var found) || ReferenceEquals(found, PyBuiltinsModule.Instance))
                return;
            if (found is PyGlobalsDict live && ReferenceEquals(live.Variables, PyBuiltinsModule.Instance.ModuleDict))
                return;
            _ownBuiltins = found;
        }

        internal bool HasOwnBuiltins
        {
            get
            {
                if (!_builtinsResolved)
                    ResolveBuiltins();
                return _ownBuiltins != null;
            }
        }

        // BUILTINS(): a dict, or any mapping through __getitem__.
        internal PyObject Builtin(string name)
        {
            if (!HasOwnBuiltins)
                return ScopeChain.BuiltinModule.GetBuiltin(name);
            if (_ownBuiltins is PyModule module)
                return module.ModuleDict.TryGetValue(name, out var found) ? found : null;
            try
            {
                return PyAbstract.GetItem(_ownBuiltins, PyString.From(name));
            }
            catch (PythonException error) when (error.PyException is PyKeyError)
            {
                return null;
            }
        }

        // Helper property to access local scope from ScopeChain
        public PyScope? LocalScope => ScopeChain?.CurrentScope;

        // CPython-style error location tracking
        // PyFrame_GetLineNumber: PyCode_Addr2Line(code, lasti), the line of the last instruction (-1
        // when it has none), and co_firstlineno before anything has run. Computed when asked.
        internal bool HasRun;
        // An error an inlined callee left: the caller raises it with its lasti on the CALL's caches.
        internal bool UnwindingFromCall;
        public int CurrentLineNumber => HasRun ? LineAt(Code, LastInstruction) : Code.GetFirstLineNo();

        internal static int LineAt(PyCodeObject code, int instruction) =>
            instruction >= 0 && instruction < code.Instructions.Count ? code.Instructions[instruction].LineNumber : -1;

        // CPython 3.12: Keyword names for next CALL instruction
        public PyTuple? KeywordNamesForNextCall { get; set; }
        public PyCell[] Closure { get; set; } = Array.Empty<PyCell>();   // 부모로부터 받은 클로저 셀들

        // **NEW**: Storage for class body variables before scope cleanup

        // CPython 3.12: Class body locals dictionary (for __prepare__ dict subclasses)
        // When executing class body, STORE_NAME writes to this dict instead of scope
        public PyObject? ClassLocalsDict { get; set; }

        public PyBaseException? LastException { get; set; }
        public PyBaseException? CurrentException { get; set; } // Current exception for PUSH_EXC_INFO


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
            PyScopeChain parentScope = null, PyCell[] closure = null, PyTuple defaults = null, PyDict kwdefaults = null, PyFrame parentFrame = null)
        {
            // CPython 3.12: Line 1628 - _PyStack_UnpackDict converts kwargs dict to (args + kwnames) format
            var names = new PyObject[kwargs.InternalDict.Count];
            var finalArgs = new PyObject[args.Length + names.Length];
            Array.Copy(args, finalArgs, args.Length);
            int index = 0;
            foreach (var kv in kwargs.InternalDict)
            {
                names[index] = kv.Key;
                finalArgs[args.Length + index++] = kv.Value;
            }
            // kwnames is call metadata, not a Python frame. In particular, creating
            // a holder frame must not push an artificial local scope into parentScope.
            return new PyFrame(code, finalArgs, parentScope, closure, parentFrame, defaults, kwdefaults, PyTuple.From(names));
        }

        public PyFrame(PyCodeObject code, PyObject[] args, PyScopeChain parentScope = null, PyCell[] closure = null, PyFrame parentFrame = null, PyTuple defaults = null, PyDict kwdefaults = null, PyTuple keywordNames = null)
        {
#if DEBUG_LOG
            Console.WriteLine($"🆕 PyFrame 생성: {code.Name}, args={args.Length}개");
#endif
            Code = code;
            ValueStack = new PyStack();
            // 부모 스코프 체인이 있으면 상속, 없으면 새로 생성
            ScopeChain = parentScope ?? new PyScopeChain();

            // _PyFrame_Initialize: co_nlocalsplus slots, all NULL. The cells and free variables
            // are in them too, put there by MAKE_CELL and COPY_FREE_VARS.
            int nlocalsplus = code.NLocalsPlus;
            LocalsPlus = nlocalsplus == 0 ? Array.Empty<PyObject>() : new PyObject[nlocalsplus];
            for (int i = 0; i < nlocalsplus; i++)
                LocalsPlus[i] = PyNull.Instance;

            InstructionPointer = 0;

            // CPython 3.12: Set parent frame for call stack tracking
            ParentFrame = parentFrame;

            // CPython 3.12: Initialize f_globals from ScopeChain.GlobalScope
            Globals = ScopeChain.GlobalScope?.Variables ?? new Dictionary<string, PyObject>();

            // Initialize filename from code object

            // func_closure, which COPY_FREE_VARS copies into the free slots.
            Closure = closure ?? Array.Empty<PyCell>();

            // A function's locals are its localsplus slots, a class body's and exec's are its
            // mapping; neither is a scope pushed on the chain, which holds the globals.

            // CPython 3.12 호환: 매개변수 바인딩 (키워드 인수 지원)
            BindArgumentsToParametersCPython312(args, code, parentFrame, defaults, kwdefaults, keywordNames);
        }


        /// <summary>The name argument errors use. CPython reports func_qualname, which an assignment to
        /// __qualname__ moves away from the code object; dataclasses does that for every method it builds.</summary>
        [ThreadStatic] internal static string CallingQualName;

        private static string CallName(PyCodeObject code) => CallingQualName ?? BindErrorName(code.DisplayQualName);

        internal static string BindErrorName(string codeName)
        {
            if (string.IsNullOrEmpty(codeName))
                return "<lambda>";
            return codeName;
        }

        // Display name used in call protocol TypeErrors ("takes N positional ...").
        internal static string GetCallDisplayName(PyObject f)
        {
            string name = f switch
            {
                PyFunction pf when !string.IsNullOrEmpty(pf.CodeObject?.DisplayQualName) => pf.CodeObject!.DisplayQualName,
                PyFunction pf when !string.IsNullOrEmpty(pf.Name) => pf.Name,
                // im_func is any callable (Objects/classobject.c:219), so the name only exists when
                // the bound function really is a function.
                PyMethod { Function: PyFunction pf } when !string.IsNullOrEmpty(pf.CodeObject?.DisplayQualName) => pf.CodeObject!.DisplayQualName,
                PyMethod { Function: PyFunction fn } when !string.IsNullOrEmpty(fn.Name) => fn.Name,
                PyBuiltinFunction bf when !string.IsNullOrEmpty(bf.Name) => bf.Name,
                _ => f.GetTypeName()
            };
            return name;
        }

        // Module-qualified prefix used by CPython's "got multiple values for keyword
        // argument" error (measured: "__main__.g() ...").
        // _PyObject_FunctionStr: str() of what has no __qualname__.
        internal static string FunctionStr(PyObject f) => f is PyNone ? "None" : GetFuncErrPrefix(f) + "()";

        internal static string GetFuncErrPrefix(PyObject f)
        {
            string name = GetCallDisplayName(f);
            IDictionary<string, PyObject> globals = f switch
            {
                PyFunction pf => pf.GlobalsDict,
                PyMethod pm => (pm.Function as PyFunction)?.GlobalsDict,
                _ => null
            };
            if (globals != null && globals.TryGetValue("__name__", out var mod) && mod != null)
            {
                var modName = mod.ToStr().Value;
                if (!string.IsNullOrEmpty(modName))
                    return modName + "." + name;
            }
            return name;
        }

        /// <summary>
        /// CPython 3.12 호환: 키워드 인수를 지원하는 매개변수 바인딩
        /// </summary>
        private void BindArgumentsToParametersCPython312(PyObject[] args, PyCodeObject code, PyFrame parentFrame, PyTuple runtimeDefaults = null, PyDict kwdefaults = null, PyTuple keywordNames = null)
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
            #endif

            // CPython 3.12: Check for keyword arguments from parent frame
            PyTuple kwNames = keywordNames ?? parentFrame?.KeywordNamesForNextCall;
            Dictionary<string, PyObject> keywordArgs = null;
            PyObject[] positionalArgs = args;

            if (kwNames != null && kwNames.Items.Length > 0)
            {
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
                if (keywordNames == null) parentFrame.KeywordNamesForNextCall = null;
            }
            else
            {
#if DEBUG_LOG
                Console.WriteLine($"🔗 매개변수 바인딩: {args.Length}개 인수, {code.ArgCount}개 매개변수");
#if DEBUG_LOG
                Console.WriteLine($"  Code flags: {code.Flags} (CO_VARARGS={((code.Flags & PyCodeObject.CO_VARARGS) != 0)}, CO_VARKEYWORDS={((code.Flags & PyCodeObject.CO_VARKEYWORDS) != 0)})");
#endif
#endif
            }

            bool hasVarArgs = (code.Flags & PyCodeObject.CO_VARARGS) != 0;
            bool hasVarKeywords = (code.Flags & PyCodeObject.CO_VARKEYWORDS) != 0;

            // Phase 1: Bind positional arguments to regular parameters (NOT including keyword-only)
            // CPython 3.12: co_argcount does NOT include keyword-only parameters
            int regularArgCount = code.ArgCount - code.KwonlyArgCount;
            int posArgIndex = 0;
            var missingPositional = new List<string>();
            // The defaults are the function's (func_defaults); a code object has none.
            int defaultCountForError = runtimeDefaults?.Items.Length ?? 0;
            for (int paramIndex = 0; paramIndex < regularArgCount; paramIndex++)
            {
                var paramName = code.VarNames[paramIndex];

                if (posArgIndex < positionalArgs.Length)
                {
                    // CPython: a parameter bound positionally may not also be a keyword
                    // (ceval.c duplicate positional check; co_name prefix, measured).
                    if (paramIndex >= code.PosonlyArgCount && keywordArgs != null && keywordArgs.ContainsKey(paramName))
                        throw PyTypeError.Create($"{CallName(code)}() got multiple values for argument '{paramName}'");
                    // Bind positional argument
                    LocalsPlus[paramIndex] = positionalArgs[posArgIndex];
                    RecordParameterInScope(paramName, positionalArgs[posArgIndex]);
                    posArgIndex++;

#if DEBUG_LOG
                    Console.WriteLine($"  → {paramName} = {positionalArgs[posArgIndex - 1]} (위치 인수)");
#endif
                }
                else if (keywordArgs != null && (paramIndex >= code.PosonlyArgCount || !hasVarKeywords) && keywordArgs.ContainsKey(paramName))
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
                    RecordParameterInScope(paramName, keywordValue);
                    keywordArgs.Remove(paramName); // Remove so it doesn't go into **kwargs

#if DEBUG_LOG
                    Console.WriteLine($"  → {paramName} = {keywordValue} (키워드 인수)");
#endif
                }
                else
                {
                    // func_defaults (__defaults__), as MAKE_FUNCTION set it.
                    PyTuple effectiveDefaults = runtimeDefaults;
                    int numRequiredParams = regularArgCount - (effectiveDefaults?.Items.Length ?? 0);

                    if (effectiveDefaults != null && paramIndex >= numRequiredParams && paramIndex - numRequiredParams < effectiveDefaults.Items.Length)
                    {
                        var defaultValue = effectiveDefaults.Items[paramIndex - numRequiredParams];
                        LocalsPlus[paramIndex] = defaultValue;
                        RecordParameterInScope(paramName, defaultValue);

#if DEBUG_LOG
                        Console.WriteLine($"  → {paramName} = {defaultValue} (기본값, index {paramIndex - numRequiredParams})");
#endif
                    }
                    else
                    {
                        // Missing required argument (message built after the loop with
                        // CPython's full grammar; collect every missing parameter).
                        missingPositional.Add(paramName);
                    }
                }
            }

            // CPython finalize_frame error order: too many positional args first,
            // then missing required positional (with the full name list).
            if (!hasVarArgs && posArgIndex < positionalArgs.Length)
            {
                int given = positionalArgs.Length;
                int slots = regularArgCount;
                int required = slots - Math.Min(defaultCountForError, slots);
                // too_many_positional
                int kwonlyGiven = 0;
                if (keywordArgs != null)
                    for (int i = slots; i < slots + code.KwonlyArgCount; i++)
                        if (keywordArgs.ContainsKey(code.VarNames[i]))
                            kwonlyGiven++;
                string signature = required < slots ? $"from {required} to {slots}" : slots.ToString();
                bool plural = required < slots || slots != 1;
                string kwonlyText = kwonlyGiven == 0 ? "" :
                    $" positional argument{(given != 1 ? "s" : "")} (and {kwonlyGiven} keyword-only argument{(kwonlyGiven != 1 ? "s" : "")})";
                throw PyTypeError.Create($"{CallName(code)}() takes {signature} positional argument{(plural ? "s" : "")} but {given}{kwonlyText} {(given == 1 && kwonlyGiven == 0 ? "was" : "were")} given");
            }
            if (missingPositional.Count > 0)
            {
                // CPython processes keyword names before reporting missing positional
                // arguments: an unknown keyword wins over the missing-argument error.
                if (keywordArgs != null && keywordArgs.Count > 0 && !hasVarKeywords)
                {
                    foreach (var kwName in keywordArgs.Keys)
                    {
                        bool matchesParam = false;
                        for (int pi = 0; pi < code.ArgCount; pi++)
                        {
                            if (code.VarNames[pi] == kwName)
                            {
                                matchesParam = true;
                                break;
                            }
                        }
                        // positional_only_passed_as_keyword, before the unknown name.
                        PyVM.RefusePositionalOnly(code, keywordArgs);
                        if (!matchesParam)
                            throw PyTypeError.Create($"{CallName(code)}() got an unexpected keyword argument '{kwName}'");
                    }
                }
                throw PyTypeError.Create($"{CallName(code)}() missing {missingPositional.Count} required positional {PyVM.MissingText(missingPositional)}");
            }

            var missingKeywordOnly = new List<string>();
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
                    RecordParameterInScope(paramName, keywordValue);
                    keywordArgs.Remove(paramName); // Remove so it doesn't go into **kwargs

#if DEBUG_LOG
                    Console.WriteLine($"  → {paramName} = {keywordValue} (keyword-only 인수)");
#endif
                }
                else
                {
                    // initialize_locals: PyDict_GetItemWithError(func->func_kwdefaults, varname).
                    PyObject defaultValue = null;
                    bool hasDefault = kwdefaults != null &&
                        kwdefaults.TryGetStoredValue(PyString.From(paramName), out defaultValue);

                    if (hasDefault)
                    {
                        LocalsPlus[paramIndex] = defaultValue;
                        RecordParameterInScope(paramName, defaultValue);
                    }
                    else
                    {
                        missingKeywordOnly.Add(paramName);
                    }
                }
            }

            if (missingKeywordOnly.Count > 0)
                throw PyTypeError.Create($"{CallName(code)}() missing {missingKeywordOnly.Count} required keyword-only {PyVM.MissingText(missingKeywordOnly)}");

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
                var argsTuple = PyTuple.From(extraArgs);

                LocalsPlus[varargsIndex] = argsTuple;
                RecordParameterInScope(varargsName, argsTuple);

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
                        kwargsDict.SetItem(PyString.From(kvp.Key), kvp.Value);
                    }
                }

                LocalsPlus[varkwargsIndex] = kwargsDict;
                RecordParameterInScope(varkwargsName, kwargsDict);

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
                PyVM.RefusePositionalOnly(code, keywordArgs);
                throw PyTypeError.Create($"{CallName(code)}() got an unexpected keyword argument '{unexpectedKey}'");
            }
        }


        // CPython 3.12 Exception Table lookup
        public (int? handlerOffset, ExceptionTableEntry? entry) GetExceptionHandlerFromTableWithEntry()
        {
            // CPython 3.12: Exception table uses byte offsets, but InstructionPointer is instruction index
            // CRITICAL: Must account for inline cache sizes when converting to byte offset!
            // CACHE and EXTENDED_ARG already occupy words in the final array.
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


        public override string ToString() => $"<frame for {Code.Name}>";

        // frame_repr
        protected internal override PyString Repr() => PyString.From(
            $"<frame at {PyObjectId.Pointer(this)}, file {PyString.From(Code.FileName ?? "<unknown>").ToRepr().Value}, line {Read("f_lineno").ToRepr().Value}, code {Read("f_code").GetAttribute("co_name").AsString().Value}>");

        // PyObject required overrides
        public override PyType GetPyType() => FramePyType;

        /// <summary>
        /// CPython 3.12: Frame attribute access for traceback.py compatibility
        /// Exposes frame introspection attributes
        /// </summary>
        // CPython: type(frame).__name__ == 'frame'
        internal static readonly PyType FramePyType = new PyType("frame", new[] { PyType.ObjectType });

        internal PyObject Read(string name)
        {
            return name switch
            {
                // f_code: code object being executed
                "f_code" => Code,

                // f_back: previous stack frame (toward the caller)
                "f_back" => ParentFrame != null ? ParentFrame : PyNone.Instance,

                // f_lineno: current line number in Python source code
                // frame_getlineno: PyFrame_GetLineNumber, None when the instruction has no line
                "f_lineno" => CurrentLineNumber is int line && line >= 0 ? PyInt.From(line) : PyNone.Instance,

                // f_locals: local namespace dictionary
                "f_locals" => BuildLocalsDict(),

                // f_globals: global namespace dictionary
                "f_globals" => PyGlobalsDict.DictOf(Globals),

                // f_builtins: built-in namespace dictionary (CPython returns the
                // builtins module dict, which is iterable; the module object is not)
                "f_builtins" => ScopeChain.BuiltinModule != null
                    ? ScopeChain.BuiltinModule.GetAttribute("__dict__")
                    : PyNone.Instance,

                // f_lasti: instruction index (for traceback)
                "f_lasti" => PyInt.From(LastInstruction * 2),

                // CPython 3.12: f_trace, f_trace_lines, f_trace_opcodes (not implemented yet)
                "f_trace" => Trace ?? PyNone.Instance,
                "f_trace_lines" => PyBool.FromBool(TraceLines),
                "f_trace_opcodes" => PyBool.False,  // Default to False

                _ => GenericGetAttribute(name)
            };
        }

        /// <summary>
        /// Build f_locals dict from LocalsPlus array and scope
        /// </summary>
        internal PyObject BuildLocalsDict()
        {
            if ((Code.Flags & PyCodeObject.CO_OPTIMIZED) != 0)
                return GetOptimizedLocals();

            // A frame without a mapping of its own has its globals as its locals (module code,
            // exec in the globals): f_locals is that very dict.
            var locals = ClassLocalsDict ?? PyGlobalsDict.DictOf(Globals);

            // _PyFrame_GetLocals(frame, include_hidden=1): the fast locals of a comprehension
            // inlined in module or class code (CO_FAST_HIDDEN) are seen while it runs, in a copy
            // of the namespace, never in the namespace itself.
            PyDict hidden = null;
            for (int i = 0; i < Code.LocalsPlusKinds.Length; i++)
            {
                if ((Code.LocalsPlusKinds[i] & PyCodeObject.FastHidden) == 0 || !FrameGetVar(i, out var value) || value == null)
                    continue;
                hidden ??= new PyDict();
                hidden.SetItem(PyString.From(Code.LocalsPlusNames[i]), value);
            }
            if (hidden == null)
                return locals;
            var inner = new PyDict();
            PyDict.UpdateCommon(inner, locals);
            PyDict.UpdateCommon(inner, hidden);
            return inner;
        }

        /// <summary>GETLOCAL(oparg) of a *_DEREF instruction: the cell MAKE_CELL or COPY_FREE_VARS put there.</summary>
        internal PyCell CellAt(int index) => (PyCell)LocalsPlus[index];

        /// <summary>format_exc_unbound (Python/ceval.c): a cell is a local, a free variable is not.</summary>
        internal Exception UnboundDeref(int index)
        {
            string name = Code.LocalsPlusNames[index];
            return index < Code.FirstFree
                ? PyUnboundLocalError.Create($"cannot access local variable '{name}' where it is not associated with a value")
                : PyNameError.Create($"cannot access free variable '{name}' where it is not associated with a value in enclosing scope", name);
        }

        /// <summary>
        /// _PyFrame_OpAlreadyRan (Objects/frameobject.c): whether an instruction before the current one
        /// is opcode with oparg.
        /// </summary>
        private bool OpAlreadyRan(ByteCodeOp opcode, int oparg)
        {
            var code = Code.Tables.Code;
            int checkOparg = 0;
            for (int unit = 0; unit < LastInstruction && 2 * unit + 1 < code.Length; unit++)
            {
                var checkOpcode = (ByteCodeOp)code[2 * unit];
                checkOparg |= code[2 * unit + 1];
                if (checkOpcode == opcode && checkOparg == oparg)
                    return true;
                checkOparg = checkOpcode == ByteCodeOp.EXTENDED_ARG ? checkOparg << 8 : 0;
                unit += PyAssemble.GetInlineCacheSize(checkOpcode);
            }
            return false;
        }

        /// <summary>
        /// frame_get_var (Objects/frameobject.c): the value of localsplus slot i as f_locals sees it,
        /// or false when an unoptimized namespace leaves a free variable out. A free slot holds its
        /// cell; a cell slot holds its cell once MAKE_CELL has run, and the argument before that.
        /// </summary>
        internal bool FrameGetVar(int index, out PyObject value)
        {
            byte kind = Code.LocalsPlusKinds[index];
            value = null;
            if ((kind & PyCodeObject.FastFree) != 0 && (Code.Flags & PyCodeObject.CO_OPTIMIZED) == 0)
                return false;
            var slot = LocalsPlus[index];
            if (PyNull.IsNull(slot))
                return true;
            if ((kind & PyCodeObject.FastFree) != 0)
                slot = ((PyCell)slot).Value;
            else if ((kind & PyCodeObject.FastCell) != 0 && slot is PyCell cell && OpAlreadyRan(ByteCodeOp.MAKE_CELL, index))
                slot = cell.Value;
            value = slot == null || PyNull.IsNull(slot) ? null : slot;
            return true;
        }

        internal void RecordParameterInScope(string name, PyObject value)
        {
            if ((Code.Flags & PyCodeObject.CO_OPTIMIZED) == 0)
                ScopeChain.AssignVariable(name, value);
        }


        // _PyFrame_GetLocals (Objects/frameobject.c): one retained dict, refreshed from the
        // localsplus slots. Writes to the dict alone do not reach the slots.
        internal PyDict GetOptimizedLocals()
        {
            var result = _localsDict ??= new PyDict();
            var keys = Code.LocalsPlusKeys;
            for (int i = 0; i < LocalsPlus.Length; i++)
            {
                if (!FrameGetVar(i, out var value))
                    continue;
                if (value != null)
                    result.SetItem(keys[i], value);
                else if (result.InternalDict.ContainsKey(keys[i]))
                    result.DelItem(keys[i]);
            }
            return result;
        }

        /// <summary>The generator, coroutine or async generator this frame belongs to (FRAME_OWNED_BY_GENERATOR).</summary>
        internal PyObject OwnerGenerator;

        // frame_clear (Objects/frameobject.c): a generator's frame closes its generator unless it is
        // running; a frame on a thread's stack cannot be cleared; any other frame lets go of its locals.
        internal PyObject FrameClear()
        {
            var running = PyRuntimeError.Create("cannot clear an executing frame");
            if (OwnerGenerator != null)
            {
                if (State == FrameState.Executing)
                    throw running;
                // _PyGen_Finalize: a coroutine that never ran warns that it was never awaited, a
                // suspended generator is closed; failures are unraisable.
                if (OwnerGenerator is SharpPy.Core.PyCoroutine coroutine)
                {
                    try { coroutine.Finalize(); }
                    catch (PythonException error) { PyUnraisable.Report(error, coroutine); }
                }
                else if (State == FrameState.Suspended)
                {
                    try { OwnerGenerator.GetAttribute("close").Call(Array.Empty<PyObject>(), null); }
                    catch (PythonException error) { PyUnraisable.Report(error, OwnerGenerator); }
                }
            }
            else if (PyVM.Instance.IsOnStack(this))
                throw running;
            else
                Clear();
            return PyNone.Instance;
        }

        /// <summary>
        /// frame_tp_clear: the frame drops f_trace, its localsplus slots and its value stack. A cell
        /// is let go, not emptied, since a closure may still share it; a locals dict (module, class
        /// body) is not the frame's to clear.
        /// </summary>
        public void Clear()
        {
            Trace = null;
            for (int i = 0; i < LocalsPlus.Length; i++)
                LocalsPlus[i] = PyNull.Instance;
            ValueStack.Clear();
        }
    }

    // Python 가상 머신 (기존 객체 시스템과 완전 통합)
    public class PyVM
    {
        public static PyVM Instance { get; } = new PyVM();
        internal int IntMaxStrDigits { get; set; } = 4300;

        private Stack<PyFrame> _frameStack;
        private readonly PyScopeChain _globalScope;

        // CPython 3.12: Adaptive Specialization System (PEP 659)

        // Current frame for zero-argument super() calls
        public static PyFrame? CurrentFrame => Instance._frameStack.Count > 0 ? Instance._frameStack.Peek() : null;

        // FRAME_OWNED_BY_THREAD: the frame is on this thread's stack, running or waiting on a call.
        internal bool IsOnStack(PyFrame frame)
        {
            foreach (var entry in _frameStack)
                if (ReferenceEquals(entry, frame))
                    return true;
            return false;
        }

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
                var exception = frame.CurrentException;
                if (exception != null)
                {
                    return exception;
                }
            }
            return null;
        }

        /// <summary>
        /// slot_tp_finalize saves the in-flight exception before running __del__ and puts it back
        /// afterwards (Objects/typeobject.c:9076 PyErr_GetRaisedException, :9090
        /// PyErr_SetRaisedException), so a finalizer that raises cannot disturb the handler that is
        /// running. SharpPy keeps the in-flight exception on the frames (PyFrame.CurrentException),
        /// so the frames that hold one are snapshotted and restored around the call.
        /// </summary>
        internal static List<(PyFrame Frame, PyBaseException Saved)> SnapshotExceptionStates()
        {
            List<(PyFrame, PyBaseException)> saved = null;
            var stack = Instance._frameStack;
            foreach (var frame in stack)
            {
                if (frame.CurrentException == null) continue;
                (saved ??= new List<(PyFrame, PyBaseException)>()).Add((frame, frame.CurrentException));
            }
            return saved;
        }

        internal static void RestoreExceptionStates(List<(PyFrame Frame, PyBaseException Saved)> saved)
        {
            if (saved == null) return;
            foreach (var entry in saved)
                entry.Frame.CurrentException = entry.Saved;
        }

        private PyVM()
        {
            BoundState = new PyThreadState { IsMain = true };
            _frameStack = BoundState.Frames;
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

        /// <summary>The scope of exec and eval: the given dict is the global scope itself.</summary>
        internal static PyScopeChain ScopeOver(PyDict globals)
        {
            IDictionary<string, PyObject> variables = globals is PyGlobalsDict live ? live.Variables : new PyDictVariables(globals);
            string name = variables.TryGetValue("__name__", out var moduleName) && moduleName is PyString text ? text.Value : "builtins";
            return new PyScopeChain(variables, name, null);
        }

        /// <summary>exec with a mapping for locals: names go to the mapping, as in a class body.</summary>
        internal PyObject ExecuteWithClosure(PyCodeObject code, PyScopeChain scopeChain, PyCell[] closure) =>
            ExecuteFrame(new PyFrame(code, new PyObject[0], scopeChain, closure));

        internal PyObject ExecuteWithNamespace(PyCodeObject code, PyScopeChain scopeChain, PyObject ns)
        {
            var frame = new PyFrame(code, new PyObject[0], scopeChain);
            frame.ClassLocalsDict = ns;
            return ExecuteFrame(frame);
        }

        // 프레임 실행 (바이트코드 해석)
        // CPython 3.12: Execute class body and return namespace
        /// <summary>_PyEval_Vector with the namespace as locals: returns what the body returned, the
        /// __class__ cell if there is one.</summary>
        public PyObject ExecuteClassBody(PyCodeObject classBody, PyCell[]? closure, PyObject ns)
        {
            var parentScope = _frameStack.Count > 0 ? _frameStack.Peek().ScopeChain : null;
            var frame = closure != null
                ? new PyFrame(classBody, new PyObject[0], parentScope, closure)
                : new PyFrame(classBody, new PyObject[0], parentScope);
            frame.ClassLocalsDict = ns;
            return ExecuteFrame(frame);
        }

        /// <summary>
        /// Create NameError: plain message plus .name; suggestions belong to display.
        /// CPython 3.12: Python/suggestions.c offer_suggestions_for_name_error
        /// </summary>
        private static Exception CreateNameErrorWithSuggestion(string name, PyFrame frame)
        {
            // CPython 3.12: raise a plain message plus the .name attribute.
            // "Did you mean" suggestions are produced by the traceback display layer
            // (Python/suggestions.c offer_suggestions_for_name_error / Lib/traceback.py),
            // never baked into the caught exception's args.
            _ = frame;
            return PyNameError.Create($"name '{name}' is not defined", name);
        }

        /// <summary>
        /// A Python call made from CALL, waiting for the dispatch loop to switch to it. Set by
        /// ExecuteCall, taken by ExecuteFrame right after the instruction returns.
        /// </summary>
        private PyFrame _inlineCall;

        /// <summary>
        /// How many ExecuteFrame calls may be on the host stack, the counterpart of C's recursion limit
        /// (Py_C_RECURSION_LIMIT). The host owns the stack: the Godot main thread is 1 MB on Windows and
        /// SharpPy cannot change it. TryEnsureSufficientExecutionStack alone is too late, because
        /// throwing the RecursionError needs stack as well: measured on that thread, a throw near the
        /// end overflowed it and ended the process. A host that runs on a larger stack may raise this.
        /// </summary>
        public static int HostRecursionLimit { get; set; } = 64;
        private int _hostFrames;

        internal int FrameDepth => _frameStack.Count;

        /// <summary>The thread state whose fields the VM holds now.</summary>
        internal PyThreadState BoundState { get; private set; }

        // PyThreadState_Swap, in two halves: only the GIL's holder uses these fields.
        internal void Bind(PyThreadState state)
        {
            BoundState = state;
            _frameStack = state.Frames;
            _inlineCall = state.InlineCall;
            _hostFrames = state.HostFrames;
            _tracing = state.Tracing;
            TraceFunction = state.TraceFunction;
            ProfileFunction = state.ProfileFunction;
            // COMPUTE_EVAL_BREAKER: a tripped signal waits for the main thread.
            if (PyGil.InterruptPending && state.IsMain)
                PyPending.Flag = true;
        }

        internal void Unbind(PyThreadState state)
        {
            state.InlineCall = _inlineCall;
            state.HostFrames = _hostFrames;
            state.Tracing = _tracing;
            state.TraceFunction = TraceFunction;
            state.ProfileFunction = ProfileFunction;
        }

        // _Py_HandlePending
        private void EvalBreaker()
        {
            PyFinalizer.Drain();
            PyGil.YieldIfRequested();
            if (PyGil.InterruptPending && BoundState.IsMain)
            {
                Modules._SignalModule.RunPending();
                PyHost.DrainMainQueue();
            }
        }

        private void CheckRecursionLimit()
        {
            if (_frameStack.Count >= Modules.PySysFunction.RecursionLimit)
                throw new PythonException(new PyRecursionError("maximum recursion depth exceeded"));
        }

        /// <summary>
        /// _Py_EnterRecursivePy for the Python limit (Python/ceval.c), plus the check C gets from
        /// _Py_EnterRecursiveCall: a call that still needs the host stack (__repr__, a generator, a
        /// builtin calling back) reports RecursionError while there is stack left to report it.
        /// </summary>
        private void EnterHostFrame()
        {
            CheckRecursionLimit();
            if (_hostFrames >= HostRecursionLimit
                || !System.Runtime.CompilerServices.RuntimeHelpers.TryEnsureSufficientExecutionStack())
                throw new PythonException(new PyRecursionError(
                    "maximum recursion depth exceeded while calling a Python object"));
            _hostFrames++;
        }

        /// <summary>
        /// DISPATCH_INLINED (Python/bytecodes.c CALL): a Python function called from Python takes a frame
        /// and no host stack, so recursion is bounded by the limit and not by the thread's stack.
        /// Generators and coroutines build objects instead of running, so they stay on the ordinary path.
        /// </summary>
        private bool TryInlineCall(PyFunction function, PyObject[] args)
        {
            if (!CanInline(function)) return false;
            var code = function.CodeObject;
            CheckRecursionLimit();
            PyFrame.CallingQualName = function.AssignedQualName;
            _inlineCall = new PyFrame(code, args, new PyScopeChain(function.GlobalsDict, "<function>"),
                function.Closure, CurrentFrame, function.GetDefaults(), function.GetKeywordDefaults());
            PyFrame.CallingQualName = null;
            SkipCallCaches(CurrentFrame);
            return true;
        }

        // SKIP_OVER(INLINE_CACHE_ENTRIES_CALL) then DISPATCH_INLINED: the caller's prev_instr, so its
        // f_lasti and tb_lasti while the callee runs, is the last cache entry of the CALL.
        private static void SkipCallCaches(PyFrame caller)
        {
            if (caller != null)
                caller.LastInstruction += PyAssemble.GetInlineCacheSize(ByteCodeOp.CALL);
        }

        private static bool CanInline(PyFunction function)
        {
            var code = function.CodeObject;
            return code != null && function.GlobalsDict != null && function.GetType() == typeof(PyFunction)
                && !code.IsGenerator() && !code.IsCoroutine() && !code.IsAsyncGenerator();
        }

        /// <summary>The same call with KW_NAMES: the trailing values of args belong to those names.</summary>
        private bool TryInlineKeywordCall(PyFunction function, PyObject[] args, PyTuple kwNames)
        {
            if (!CanInline(function)) return false;
            CheckRecursionLimit();
            int positional = args.Length - kwNames.Items.Length;
            var kwargs = new PyDict();
            for (int i = 0; i < kwNames.Items.Length; i++)
                kwargs.SetItem(kwNames.Items[i], args[positional + i]);
            PyFrame.CallingQualName = function.AssignedQualName;
            _inlineCall = PyFrame.CreateWithKwargs(function.CodeObject, args[..positional], kwargs,
                new PyScopeChain(function.GlobalsDict, "<function>"), function.Closure,
                function.GetDefaults(), function.GetKeywordDefaults(), CurrentFrame);
            PyFrame.CallingQualName = null;
            SkipCallCaches(CurrentFrame);
            return true;
        }

        public PyObject ExecuteFrame(PyFrame frame)
        {
            EnterHostFrame();
            PyFrame.CallingQualName = null;
            var entryFrame = frame;
            var outerDepth = _frameStack.Count;
            // f_back: a frame made by a host call has no caller yet; the running frame is it.
            if (frame.ParentFrame == null && outerDepth > 0)
                frame.ParentFrame = _frameStack.Peek();
            _frameStack.Push(frame);

#if DEBUG_LOG
            Console.WriteLine($"\n🚀 VM 실행: {frame}");
#endif

            // CPython 3.12: Extended argument accumulation for EXTENDED_ARG support
            int extendedArg = 0;

#if DEBUG
            // 🛡️ 무한루프 방지 안전장치 (DEBUG 모드 전용)
            // 2026-10-28: the instruction-count cap (50_000, since 2e55141) is removed.
            // Measured: slice_delete_compaction_regression.py needs 365_172 module-frame
            // instructions, byte-identical at a40d72c and HEAD - a legitimate workload,
            // not growth. int_loop alone exceeds 1M in one frame. Any fixed count false-
            // fires on real scripts; wall-clock is the actual runaway signal.
            var startTime = DateTime.UtcNow;
            var maxTimeSeconds = 30; // 최대 30초
            var instructionCount = 0;

            // DEBUG: Track last instructions for debugging infinite loops
            var lastInstructions = new System.Collections.Generic.Queue<string>();
            const int maxLastInstructions = 100; // Increase to 100 for better analysis
#endif

            try
            {
                while (true)
                {
                if (frame.InstructionPointer >= frame.Code.Instructions.Count)
                {
                    if (ReferenceEquals(frame, entryFrame)) break;
                    frame = ReturnToCaller(PyNone.Instance);
                    continue;
                }
#if DEBUG
                    // 🛡️ 안전장치 검사 (DEBUG 모드 전용)
                    instructionCount++;
                    if (instructionCount % 10000 == 0) // 1만 명령어마다 검사
                    {
                        var elapsed = DateTime.UtcNow - startTime;
                        if (elapsed.TotalSeconds > maxTimeSeconds)
                        {
                            Console.WriteLine($"\n[DEBUG] Execution timeout. Last {lastInstructions.Count} instructions:");
                            foreach (var log in lastInstructions)
                            {
                                Console.WriteLine($"  {log}");
                            }
                            Console.WriteLine($"[DEBUG] Current frame: {frame.Code.Name}");
                            Console.WriteLine($"[DEBUG] Current instruction pointer: {frame.InstructionPointer}");
                            throw new PythonException(new PyRuntimeError($"Execution timeout after {elapsed.TotalSeconds:F1} seconds ({instructionCount} instructions)"));
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

                    // CACHE words occupy offsets but do not execute Python code.
                    // Keep pending generator exceptions on the normal handling path.
                    if (instruction.OpCode == ByteCodeOp.CACHE && extendedArg == 0 && frame.PendingException == null)
                    {
                        frame.InstructionPointer++;
                        continue;
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
                            instruction.End,
                            instruction.TargetBlock,
                            instruction.ExceptBlock
                        );
                        extendedArg = 0; // Reset for next instruction
                    }

                    // exit_unwind into the caller leaves its prev_instr where the call left it.
                    if (!frame.UnwindingFromCall)
                        frame.LastInstruction = frame.InstructionPointer;
                    frame.UnwindingFromCall = false;

                    frame.HasRun = true;
                    if ((TraceFunction != null || ProfileFunction != null) && !_tracing)
                        TraceInstruction(frame, instruction);

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

                        var result = ExecuteInstruction(frame, instruction.OpCode, instruction.Argument);

                        // The caller's instruction pointer stays on the CALL, so a handler lookup and a
                        // traceback line see what they saw when the call ran on the host stack.
                        if (_inlineCall != null)
                        {
                            frame = _inlineCall;
                            _inlineCall = null;
                            _frameStack.Push(frame);
                            continue;
                        }

                        // RETURN_VALUE인 경우 함수 종료
                        if (result != null)
                        {
#if DEBUG_LOG
                            Console.WriteLine($"✅ VM 완료: {result}");
#endif
                            if (ReferenceEquals(frame, entryFrame)) return result;
                            frame = ReturnToCaller(result);
                            continue;
                        }

                        frame.InstructionPointer++;
                    }
                    catch (Exception thrown) when (thrown is PythonException || IsHostFailure(thrown))
                    {
                        // What C# threw inside an instruction is a Python exception from here on:
                        // any except clause sees it, and the frames unwind as for any other.
                        var pyEx = thrown as PythonException ?? new PythonException(ConvertToPythonException(thrown));

                        // The error label (Python/ceval.c): PyTraceBack_Here and the raise event; a
                        // re-raise (RERAISE, bare raise, CLEANUP_THROW) went to exception_unwind instead.
                        bool reraised = pyEx.FromReraise;
                        PyTraceBack_Here(frame, pyEx);

                        if (frame.Trace != null && !_tracing && !reraised)
                            TraceRaised(frame, pyEx);

                        // Handle Python exceptions with proper exception handler routing
                        if (!TryEnterExceptionHandler(frame, pyEx))
                        {
                            if (ProfileFunction != null && !_tracing)
                                ProfileEvent(frame, "return", PyNone.Instance);
                            // PY_UNWIND: the frame ends with a return event of None.
                            if (frame.Trace != null && !_tracing)
                                TraceEvent(frame, "return", PyNone.Instance);
                            // No handler - re-throw with location information
                            if (ReferenceEquals(frame, entryFrame))
                            {
                                if (thrown is PythonException)
                                    throw;
                                throw pyEx;
                            }
                            // exit_unwind: an inlined call has no host frame to throw through, so the
                            // error moves to the caller, whose next step raises it at its CALL.
                            _frameStack.Pop();
                            frame = _frameStack.Peek();
                            frame.PendingException = pyEx;
                            frame.UnwindingFromCall = true;
                        }

                        // This method now outlives the calls it runs, so a handled exception left in the
                        // catch variable would stay alive, with everything it holds, until it returns.
                        pyEx = null;
                        thrown = null;
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
                _inlineCall = null;
                _hostFrames--;
                while (_frameStack.Count > outerDepth) _frameStack.Pop();
            }
        }

        /// <summary>
        /// exception_unwind: enters this frame's handler, or says there is none. A method of its own
        /// because ExecuteFrame outlives the calls it runs, and a local there that names the exception
        /// would keep it, with everything it holds, alive until ExecuteFrame returns.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static bool TryEnterExceptionHandler(PyFrame frame, PythonException pyEx)
        {
            // Handle Python exceptions with proper exception handler routing
            var (handlerOffset, exceptionEntry) = frame.GetExceptionHandlerFromTableWithEntry();
            if (!handlerOffset.HasValue || exceptionEntry == null) return false;

            // CPython exception_unwind: retain the stack at SETUP_*,
            // discarding unfinished expression operands before the handler.
            while (frame.ValueStack.Count > exceptionEntry.Depth)
                frame.ValueStack.Pop();

            // CPython 3.12 _PyErr_SetObject: implicit __context__ is the
            // topmost *being-handled* exception captured at RAISE time, i.e.
            // BEFORE the unwind drops the handler scope we are escaping.
            // Done once per raise: an error a builtin raised is chained here, by the first handler.
            if (pyEx.PyException is PyBaseException { ImplicitContextDone: false } newExcBase &&
                frame.CurrentException is PyBaseException ctxBase &&
                !ReferenceEquals(newExcBase, ctxBase) &&
                newExcBase.__context__ == null &&
                !newExcBase.__suppress_context__)
            {
                newExcBase.__context__ = ctxBase;
                newExcBase.ImplicitContextDone = true;
            }

            // CPython 3.12: Push lasti if required (for WITH_EXCEPT_START)
            // CPython ceval.c:972-978
            if (exceptionEntry.Lasti)
            {
                // Push current instruction pointer as lasti (PyLong)
                var lastiValue = PyInt.From(frame.LastInstruction);
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
            // PUSH_EXC_INFO saves the previous handled exception before replacing it.
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
                    throw pyEx;
                }
            }

            return true;
        }

        /// <summary>The inlined frame finished: its caller resumes after the CALL with the result.</summary>
        /// <summary>sys.settrace: the global function, called with 'call' for each new frame.</summary>
        internal PyObject TraceFunction;

        /// <summary>sys.setprofile: called with 'call' and 'return' of Python frames.</summary>
        internal PyObject ProfileFunction;

        // tstate->tracing: a trace function is not traced.
        private bool _tracing;

        // call_trampoline: a None answer leaves the local function as it was.
        private void TraceEvent(PyFrame frame, string what, PyObject argument)
        {
            var callback = what == "call" ? TraceFunction : frame.Trace;
            if (callback == null) return;
            _tracing = true;
            try
            {
                var answer = callback.Call(new PyObject[] { frame, PyString.From(what), argument }, null);
                if (answer is not PyNone)
                    frame.Trace = answer;
            }
            catch
            {
                // A trace function that raises is removed.
                TraceFunction = null;
                frame.Trace = null;
                throw;
            }
            finally
            {
                _tracing = false;
            }
        }

        private void ProfileEvent(PyFrame frame, string what, PyObject argument)
        {
            _tracing = true;
            try
            {
                ProfileFunction.Call(new PyObject[] { frame, PyString.From(what), argument }, null);
            }
            catch
            {
                ProfileFunction = null;
                throw;
            }
            finally
            {
                _tracing = false;
            }
        }

        private void ProfileInstruction(PyFrame frame, ByteCodeInstruction instruction)
        {
            switch (instruction.OpCode)
            {
                case ByteCodeOp.RESUME:
                    ProfileEvent(frame, "call", PyNone.Instance);
                    break;
                case ByteCodeOp.RETURN_VALUE:
                case ByteCodeOp.YIELD_VALUE:
                    ProfileEvent(frame, "return", frame.ValueStack.Peek());
                    break;
                case ByteCodeOp.RETURN_CONST:
                    ProfileEvent(frame, "return", frame.Code.Constants[instruction.Argument]);
                    break;
            }
        }

        private void TraceInstruction(PyFrame frame, ByteCodeInstruction instruction)
        {
            if (ProfileFunction != null)
                ProfileInstruction(frame, instruction);
            if (TraceFunction == null)
                return;
            if (instruction.OpCode == ByteCodeOp.RESUME)
            {
                // After a yield the frame goes on from the line it was on.
                if (instruction.Argument == 0)
                    frame.TraceLine = -1;
                frame.TraceLastInstruction = frame.InstructionPointer;
                TraceEvent(frame, "call", PyNone.Instance);
                return;
            }
            if (frame.Trace == null) return;
            int line = instruction.LineNumber;
            bool backward = frame.InstructionPointer < frame.TraceLastInstruction;
            frame.TraceLastInstruction = frame.InstructionPointer;
            if (line > 0 && frame.TraceLines && (line != frame.TraceLine || backward))
            {
                frame.TraceLine = line;
                TraceEvent(frame, "line", PyNone.Instance);
            }
            switch (instruction.OpCode)
            {
                case ByteCodeOp.RETURN_VALUE:
                case ByteCodeOp.YIELD_VALUE:
                    TraceEvent(frame, "return", frame.ValueStack.Peek());
                    break;
                case ByteCodeOp.RETURN_CONST:
                    TraceEvent(frame, "return", frame.Code.Constants[instruction.Argument]);
                    break;
            }
        }

        // sys_trace_exception_func: 'exception' with (type, value, traceback).
        private void TraceRaised(PyFrame frame, PythonException raised)
        {
            var value = raised.PyException;
            TraceEvent(frame, "exception", PyTuple.From(new[] { value.GetPyType(), value, value.GetAttribute("__traceback__") }));
        }

        private PyFrame ReturnToCaller(PyObject result)
        {
            _frameStack.Pop();
            var caller = _frameStack.Peek();
            caller.ValueStack.Push(result);
            caller.InstructionPointer++;
            return caller;
        }

        // Keep mapping protocol exception handling outside the opcode dispatcher.
        /// <summary>
        /// import_from's error (Python/ceval.c): ImportError naming the module (its __name__, or
        /// "<unknown module name>") and its file (PyModule_GetFilenameObject, "unknown location"
        /// without one), "partially initialized" while its spec is still initializing, with name,
        /// path and name_from set as _PyErr_SetImportErrorWithNameFrom sets them.
        /// </summary>
        private static PythonException ImportFromError(PyObject module, string itemName)
        {
            var packageName = module.LookupAttribute("__name__") as PyString;
            string shown = packageName != null ? packageName.ToRepr().Value : "'<unknown module name>'";
            PyString path = module is PyModule fileModule && fileModule.ModuleDict.TryGetValue("__file__", out var file)
                ? file as PyString : null;
            string message;
            if (path == null)
                message = $"cannot import name {PyString.From(itemName).ToRepr().Value} from {shown} (unknown location)";
            else
            {
                var spec = module.LookupAttribute("__spec__");
                bool initializing = spec != null && spec is not PyNone && spec.LookupAttribute("_initializing") is { } flag && flag.IsTrue();
                message = initializing
                    ? $"cannot import name {PyString.From(itemName).ToRepr().Value} from partially initialized module {shown} (most likely due to a circular import) ({path.Value})"
                    : $"cannot import name {PyString.From(itemName).ToRepr().Value} from {shown} ({path.Value})";
            }
            var kwargs = new PyDict();
            kwargs.SetItem(PyString.From("name"), (PyObject)packageName ?? PyNone.Instance);
            kwargs.SetItem(PyString.From("path"), (PyObject)path ?? PyNone.Instance);
            var error = (PyBaseException)PyType.ImportErrorType.Call(new PyObject[] { PyString.From(message) }, kwargs);
            error.SetAttribute("name_from", PyString.From(itemName));
            return new PythonException(error);
        }

        // GLOBALS() then BUILTINS(), the tail of LOAD_NAME and all of LOAD_GLOBAL; NameError past both.
        private static PyObject GlobalOrBuiltin(PyFrame frame, string name) =>
            frame.ScopeChain.GlobalScope?.GetVariable(name) ?? frame.Builtin(name)
            ?? throw CreateNameErrorWithSuggestion(name, frame);

        private static PyObject LookupNamespaceMapping(PyObject mapping, PyString key)
        {
            if (mapping.IsExactly(PyType.DictType))
                return ((PyDict)mapping).InternalDict.TryGetValue(key, out var value) ? value : null;
            try { return PyAbstract.GetItem(mapping, key); }
            catch (PythonException ex) when (ex.PyException is PyKeyError) { return null; }
        }

        private static void DeleteNamespaceName(PyFrame frame, string name)
        {
            try
            {
                var key = PyString.From(name);
                // PyDict_CheckExact(ns) ? PyDict_DelItem : PyObject_DelItem
                if (frame.ClassLocalsDict is PyDict { IsExact: true } dict) dict.DelItem(key);
                else frame.ClassLocalsDict.DelItem(key);
            }
            catch (PythonException)
            {
                // CPython DELETE_NAME replaces all mapping deletion errors.
                throw CreateNameErrorWithSuggestion(name, frame);
            }
        }

        private void ExecuteCall(PyFrame frame, int callArgCount)
        {
            var callArgs = new PyObject[callArgCount];

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

            if (nextElement == null || PyNull.IsNull(nextElement))
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
                    if (actualCallable is PyFunction keywordTarget
                        && TryInlineKeywordCall(keywordTarget, finalArgs, kwNames)) return;
                    newCallResult = CallWithKeywords(actualCallable, finalArgs, kwNames);
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
                        if (method.Function is PyFunction boundFunction)
                        {
                            var withSelf = new PyObject[finalArgs.Length + 1];
                            withSelf[0] = method.Instance;
                            Array.Copy(finalArgs, 0, withSelf, 1, finalArgs.Length);
                            if (TryInlineCall(boundFunction, withSelf)) return;
                            newCallResult = boundFunction.Call(withSelf, null);
                        }
                        else
                        {
                            newCallResult = method.Call(finalArgs, null);
                        }
                    }
                    else if (actualCallable is PyFunction func)
                    {
                        if (TryInlineCall(func, finalArgs)) return;
                        newCallResult = func.Call(finalArgs, null);
                    }
                    else
                    {
                        newCallResult = actualCallable.Call(finalArgs, null);
                    }
                }
            }
            finally
            {
                // Consume KW_NAMES on success or failure.
                frame.KeywordNamesForNextCall = null;
            }

            frame.ValueStack.Push(newCallResult);
        }

        // Keep captured function-creation state out of the instruction dispatcher.
        // Otherwise the C# compiler allocates a display class for every opcode.
        private void ExecuteMakeFunction(PyFrame frame, int argument)
        {
            // CPython 3.12 compatible function creation with full flags support
            var flags = argument;

            PyCell[] closure = null;
            PyTuple defaults = null;
            PyTuple kwDefaults = null;

            #if DEBUG_LOG
            Console.WriteLine($"🔧 MAKE_FUNCTION with flags: {flags:X} (binary: {Convert.ToString(flags, 2)})");
            #endif
            #if DEBUG_LOG
            Console.WriteLine($"🔧 MAKE_FUNCTION stack size before processing: {frame.ValueStack.Count}");
            if (frame.ValueStack.Count > 0)
            {
                var debugStackItems = frame.ValueStack.ToArray();
                for (int i = 0; i < Math.Min(debugStackItems.Length, 5); i++)
                {
                    Console.WriteLine($"   Stack[{i}]: {debugStackItems[i]?.GetType().Name} = {debugStackItems[i]}");
                }
            }
            #endif

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
                    defaults = PyTuple.From(new PyObject[0]);
                }
            }

            if (codeObject is PyCodeObject pyCode)
            {
                // CPython 3.12: async def로 정의된 함수인지 확인
                if (pyCode.IsAsyncGenerator())
                {
                    // A function keeps its code, cells, defaults and globals and nothing of the frame
                    // that made it (func_new, Objects/funcobject.c). A lambda that names `frame` would
                    // keep every local of the enclosing call alive for the life of the function.
                    var asyncGenGlobals = frame.ScopeChain.GlobalScope?.Variables;

                    // Async generator: 호출 시 PyAsyncGenerator 객체 반환
                    var asyncGenImpl = new Func<PyObject[], PyObject>(args =>
                    {
                        var asyncGenChain = new PyScopeChain(asyncGenGlobals, "<async generator>");
                        var asyncGenFrame = closure != null && closure.Length > 0
                            ? new PyFrame(pyCode, args, asyncGenChain, closure, CurrentFrame)
                            : new PyFrame(pyCode, args, asyncGenChain, null, CurrentFrame);

                        // Async generator 생성
                        return new SharpPy.Core.PyAsyncGenerator(asyncGenFrame, this, pyCode.Name);
                    });

                    var asyncGenFunction = new PyFunction(pyCode.Name, asyncGenImpl, null, null, closure, pyCode);
                    asyncGenFunction.GlobalsDict = asyncGenGlobals;

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
                            ? new PyFrame(pyCode, args, functionScopeChain, closure, CurrentFrame)
                            : new PyFrame(pyCode, args, functionScopeChain, null, CurrentFrame);

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
                        ? new PyFrame(pyCode, args, functionScopeChain, closure, CurrentFrame, capturedDefaults)
                        : new PyFrame(pyCode, args, functionScopeChain, null, CurrentFrame, capturedDefaults);
                    return ExecuteFrame(functionFrame);
                };

                if (closure != null && closure.Length > 0)
                {
                    // Create function with closure
                    functionObject = new PyFunction(pyCode.Name, implementation, null, null, closure, pyCode);
                    functionObject.GlobalsDict = globalsDict;  // CPython 3.12: func.__globals__
                }
                else
                {
                    // Create regular function without closure
                    functionObject = new PyFunction(pyCode.Name, implementation, null, null, closure, pyCode);
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
        }

        private PyObject ExecuteInstruction(PyFrame frame, ByteCodeOp opcode, int argument)
        {
            switch (opcode)
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
                    var copyIndex = argument;
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
                    var oparg = argument;
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
                    var constant = frame.Code.Constants[argument];
                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 LOAD_CONST: 인덱스 {argument}, 값 {constant} (타입: {constant?.GetType().Name})");
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
                    var name = frame.Code.Names[argument];
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

                        value = LookupNamespaceMapping(frame.ClassLocalsDict, PyString.From(name));
                    }

                    // GLOBALS(), then BUILTINS() (lines 1736-1767). Without a mapping of its own the
                    // frame's locals are its globals, so this is the whole lookup.
                    value ??= GlobalOrBuiltin(frame, name);

                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 LOAD_NAME({name}): loaded {value?.GetType().Name ?? "null"} value = {value}");
                    #endif
                    frame.ValueStack.Push(value);
                    break;

                case ByteCodeOp.LOAD_FAST:
                    // CPython 3.12: Direct array access for local variables
                    var argIndex = argument;
                    if (argIndex < frame.LocalsPlus.Length)
                    {
                        var fastValue = frame.LocalsPlus[argIndex];
                        // CPython 3.12: PyNull indicates uninitialized variable
                        if (PyNull.IsNull(fastValue))
                        {
                            var varName = frame.Code.VarNames[argIndex];
                            throw PyUnboundLocalError.Create($"cannot access local variable '{varName}' where it is not associated with a value");
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
                    var clearArgIndex = argument;
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
                    var checkIndex = argument;
                    if (checkIndex < frame.LocalsPlus.Length)
                    {
                        var checkValue = frame.LocalsPlus[checkIndex];
                        if (PyNull.IsNull(checkValue))
                        {
                            var checkVarName = frame.Code.VarNames[checkIndex];
                            throw PyUnboundLocalError.Create($"cannot access local variable '{checkVarName}' where it is not associated with a value");
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
                    var storeIndex = argument;
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
                    var deleteFastIndex = argument;
                    if (deleteFastIndex < frame.LocalsPlus.Length)
                    {
                        if (PyNull.IsNull(frame.LocalsPlus[deleteFastIndex]))
                            throw PyUnboundLocalError.Create($"cannot access local variable '{frame.Code.VarNames[deleteFastIndex]}' where it is not associated with a value");
                        frame.LocalsPlus[deleteFastIndex] = PyNull.Instance;
                    }
                    else
                    {
                        throw PyRuntimeError.Create($"DELETE_FAST: index {deleteFastIndex} out of range");
                    }
                    break;

                case ByteCodeOp.STORE_NAME:
                    var storeName = frame.Code.Names[argument];
                    var storeValue = frame.ValueStack.Pop();

                    // CPython 3.12: If executing class body with __prepare__ dict subclass,
                    // call __setitem__ on the dict (like CPython's PyObject_SetItem)
                    if (frame.ClassLocalsDict != null)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"📝 STORE_NAME to ClassLocalsDict: {storeName} = {storeValue?.GetType().Name}");
                        #endif
                        frame.ClassLocalsDict.SetItem(PyString.From(storeName), storeValue);
                    }
                    else
                    {
                        // The frame's locals are its globals.
                        frame.ScopeChain.GlobalScope.SetVariable(storeName, storeValue);
                    }
                    break;

                case ByteCodeOp.DELETE_NAME:
                    var deleteName = frame.Code.Names[argument];
                    if (frame.ClassLocalsDict != null)
                    {
                        DeleteNamespaceName(frame, deleteName);
                        break;
                    }
                    // The frame's locals are its globals.
                    if (frame.ScopeChain.GlobalScope?.DeleteVariable(deleteName) == true)
                        break;
                    throw CreateNameErrorWithSuggestion(deleteName, frame);

                case ByteCodeOp.LOAD_GLOBAL:
                    // CPython 3.12: oparg encoding: (nameIndex << 1) | pushNull
                    int globalOparg = argument;
                    bool pushNull = (globalOparg & 1) == 1;
                    int globalNameIndex = globalOparg >> 1;

                    var globalName = frame.Code.Names[globalNameIndex];

                    // CPython 3.12: Push NULL first if flag is set
                    if (pushNull)
                        frame.ValueStack.Push(PyNull.Instance);
                    var globalValue = GlobalOrBuiltin(frame, globalName);

                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 LOAD_GLOBAL({globalName}): loaded {globalValue?.GetType().Name ?? "null"} value = {globalValue}");
                    #endif
                    frame.ValueStack.Push(globalValue);
                    break;

                case ByteCodeOp.LOAD_GLOBAL_BUILTIN:
                    var globalBuiltinName = frame.Code.Names[argument];
                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 LOAD_GLOBAL_BUILTIN({globalBuiltinName}): Direct builtin lookup");
                    #endif

                    // LOAD_GLOBAL_BUILTIN은 최적화된 버전으로 builtin을 직접 조회
                    var builtinValue = frame.Builtin(globalBuiltinName);
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
                    if (argument == 1)
                        identityResult = !identityResult;

                    frame.ValueStack.Push(PyBool.FromBool(identityResult));
                    break;

                case ByteCodeOp.STORE_GLOBAL:
                    var storeGlobalName = frame.Code.Names[argument];
                    var storeGlobalValue = frame.ValueStack.Pop();
                    // STORE_GLOBAL must always store to GlobalScope, not CurrentScope
                    frame.ScopeChain.GlobalScope.SetVariable(storeGlobalName, storeGlobalValue);
#if DEBUG_LOG
                    Console.WriteLine($"📝 STORE_GLOBAL: {storeGlobalName} = {storeGlobalValue}");
#endif
                    break;

                case ByteCodeOp.DELETE_GLOBAL:
                    // CPython 3.12: Delete global variable
                    var deleteGlobalName = frame.Code.Names[argument];
                    if (frame.ScopeChain.GlobalScope?.DeleteVariable(deleteGlobalName) == true)
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
                    // CPython 3.12 Python/ceval.c SETUP_ANNOTATIONS:
                    // Create __annotations__ in the namespace where STORE_NAME writes.
                    var annotationsKey = PyString.From("__annotations__");
                    if (frame.ClassLocalsDict != null)
                    {
                        if (LookupNamespaceMapping(frame.ClassLocalsDict, annotationsKey) == null)
                            frame.ClassLocalsDict.SetItem(annotationsKey, new PyDict());
                    }
                    else
                    {
                        var targetScope = frame.ScopeChain.CurrentScope;
                        if (targetScope.GetVariable(annotationsKey.Value) == null)
                            targetScope.SetVariable(annotationsKey.Value, new PyDict());
                    }
                    break;

                case ByteCodeOp.BINARY_OP:
                    // CPython 3.12+ unified binary operation
                    var operation = (BinaryOpType)argument;
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
                    frame.ValueStack.Push(PyInt.From((long)(leftInt + rightInt)));
                    break;

                case ByteCodeOp.BINARY_ADD_FLOAT:
                    var rightFloat = ((PyFloat)frame.ValueStack.Pop()).Value;
                    var leftFloat = ((PyFloat)frame.ValueStack.Pop()).Value;
                    frame.ValueStack.Push(new PyFloat(leftFloat + rightFloat));
                    break;

                case ByteCodeOp.BINARY_ADD_UNICODE:
                    var rightStr = ((PyString)frame.ValueStack.Pop()).Value;
                    var leftStr = ((PyString)frame.ValueStack.Pop()).Value;
                    frame.ValueStack.Push(PyString.From(leftStr + rightStr));
                    break;

                case ByteCodeOp.BINARY_MULTIPLY_INT:
                    var rightMulInt = ((PyInt)frame.ValueStack.Pop()).Value;
                    var leftMulInt = ((PyInt)frame.ValueStack.Pop()).Value;
                    frame.ValueStack.Push(PyInt.From((long)(leftMulInt * rightMulInt)));
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
                    frame.ValueStack.Push(PyNull.Instance);
                    break;

                case ByteCodeOp.CALL:
                    ExecuteCall(frame, argument);
                    // CHECK_EVAL_BREAKER: a callee in this loop has its own at RESUME.
                    if (PyPending.Flag && _inlineCall == null)
                        EvalBreaker();
                    break;

                // Specialized Method Calls - CPython 3.12 Adaptive Specialization
                case ByteCodeOp.CALL_LIST_APPEND:
                    var appendArg = frame.ValueStack.Pop();
                    var appendList = (PyList)frame.ValueStack.Pop();
                    frame.ValueStack.Pop(); // Pop the null (PUSH_NULL)
                    appendList.Append(appendArg);
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

                case ByteCodeOp.CALL_LEN_LIST:
                    var lenList = (PyList)frame.ValueStack.Pop();
                    frame.ValueStack.Pop(); // Pop the null (PUSH_NULL)
                    frame.ValueStack.Push(PyInt.From(lenList.Length()));
                    break;

                case ByteCodeOp.CALL_LEN_STR:
                    var lenStr = (PyString)frame.ValueStack.Pop();
                    frame.ValueStack.Pop(); // Pop the null (PUSH_NULL)
                    frame.ValueStack.Push(PyInt.From(lenStr.Value.Length));
                    break;

                case ByteCodeOp.CALL_FUNCTION_EX:
                    // CPython 3.12: Extended function call with *args and **kwargs
                    var hasKwargs = (argument & 1) != 0;

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
                    if (argsIterable is PyTuple { IsExact: true } argsTuple)
                    {
                        argsList.AddRange(argsTuple.Items);
                    }
                    else if (argsIterable is PyList { IsExact: true } argsListObj)
                    {
                        for (int i = 0; i < argsListObj.Length(); i++)
                        {
                            argsList.Add(argsListObj.GetItem(i));
                        }
                    }
                    else
                    {
                        // CPython 3.12 CALL_FUNCTION_EX materializes any iterable
                        // (call.c uses PyObject_GetIter for non tuple/list args).
                        try
                        {
                            argsList.AddRange(argsIterable.AsTuple().Items);
                        }
                        // check_args_iterable: only what has neither __iter__ nor __getitem__.
                        catch (PythonException ex) when (ex.PyException is PyTypeError &&
                            !argsIterable.HasTypeSlot("__iter__") &&
                            !argsIterable.HasTypeSlot("__getitem__"))
                        {
                            // CPython 3.12 measured wording includes the callee name.
                            throw PyTypeError.Create($"{PyFrame.FunctionStr(functionToCall)} argument after * must be an iterable, not {argsIterable.GetTypeName()}");
                        }
                    }

                    // PyObject_Call(func, callargs, kwargs): the dict DICT_MERGE built. A callable with
                    // vectorcall refuses a name that is not a str first (_PyStack_UnpackDict); any
                    // other meets it in its own parser.
                    PyDict callKeywords = hasKwargs && kwargsDict is PyDict kwargsPyDict && kwargsPyDict.Length() != 0 ? kwargsPyDict : null;
                    PyObject unpackedResult = PyArity.ObjectCall(functionToCall, argsList.ToArray(), callKeywords);

                    frame.ValueStack.Push(unpackedResult);
                    break;

                case ByteCodeOp.DICT_MERGE:
                    // CPython 3.12: Merge dictionaries for **kwargs unpacking
                    // Stack before: [target_dict, source_dict]
                    // Stack after: [merged_dict]
                    var mergeCount = argument;

                    // Get the source dictionary from stack (TOS)
                    var sourceDict = frame.ValueStack.Pop();

                    // Get the target dictionary from stack (TOS-1)
                    var targetDict = frame.ValueStack.Pop();

                    if (!(targetDict is PyDict targetPyDict))
                    {
                        throw PyTypeError.Create($"DICT_MERGE: target must be dict, got {targetDict.GetType().Name}");
                    }

                    // CPython 3.12: a **kwargs merge colliding with an existing keyword is
                    // "got multiple values for keyword argument" naming the callee
                    // (module-qualified); plain dict literals never reach this opcode.
                    // Stack here (after popping source and target): [.., NULL, func, argsTuple]
                    PyObject mergeCallee = frame.ValueStack.Count >= 2 ? frame.ValueStack.PeekAt(1) : null;

                    // Merge sourceDict into targetDict
                    if (sourceDict is not PyDict && !sourceDict.HasTypeSlot("keys"))
                    {
                        // CPython 3.12 measured wording for f(**non_mapping).
                        string mapPrefix = mergeCallee != null ? PyFrame.FunctionStr(mergeCallee) + " " : "";
                        throw PyTypeError.Create($"{mapPrefix}argument after ** must be a mapping, not {sourceDict.GetTypeName()}");
                    }
                    // dict_merge: a mapping is read through keys(), which may repeat one.
                    var mergedEntries = new List<KeyValuePair<PyObject, PyObject>>();
                    if (PyDict.IterIsDictIter(sourceDict) && sourceDict is PyDict plainSource)
                    {
                        foreach (var entry in plainSource.InternalDict)
                            mergedEntries.Add(new KeyValuePair<PyObject, PyObject>(entry.Key, entry.Value));
                    }
                    else
                    {
                        var keysOfSource = sourceDict.GetAttribute("keys").Call(new PyObject[0], null).GetIterator();
                        while (true)
                        {
                            PyObject mergeKey;
                            try { mergeKey = keysOfSource.Next(); }
                            catch (PythonException done) when (done.PyException is PyStopIteration) { break; }
                            mergedEntries.Add(new KeyValuePair<PyObject, PyObject>(mergeKey, PyAbstract.GetItem(sourceDict, mergeKey)));
                        }
                    }
                    foreach (var kvp in mergedEntries)
                    {
                        // format_kwargs_error: %S of the key, in quotes.
                        if (mergeCallee != null && targetPyDict.Contains(kvp.Key).ToBool() && kvp.Key is not PyString)
                            throw PyTypeError.Create($"{PyFrame.GetFuncErrPrefix(mergeCallee)}() got multiple values for keyword argument '{kvp.Key.AsString().Value}'");
                        if (mergeCallee != null && targetPyDict.Contains(kvp.Key).ToBool())
                            throw PyTypeError.Create($"{PyFrame.GetFuncErrPrefix(mergeCallee)}() got multiple values for keyword argument {kvp.Key.ToRepr().Value}");
                        targetPyDict.SetItem(kvp.Key, kvp.Value);
                    }

                    // Push the merged dictionary back onto the stack
                    frame.ValueStack.Push(targetPyDict);
                    break;

                case ByteCodeOp.RESUME:
                    // Python 3.12: every code object starts here. It is also CPython's safe point:
                    // Python/bytecodes.c:145-146 tests the eval breaker here, and only for
                    // oparg < 2 (a frame resumed for a generator send is not interrupted).
                    // SharpPy uses the same point for the lifetime queue (PyFinalizer.Drain).
                    if (argument < 2 && PyPending.Flag) EvalBreaker();
                    break;

                // Duplicate CALL case removed (was CALL_FUNCTION_KW)

                case ByteCodeOp.MAKE_FUNCTION:
                    ExecuteMakeFunction(frame, argument);
                    break;

                case ByteCodeOp.LOAD_ATTR:
                    // CPython 3.12: LOAD_ATTR with flag encoding
                    // oparg encoding: (nameIndex << 1) | pushNull
                    // If pushNull=1: Push two values [self/NULL, method/attr] for method call optimization
                    // If pushNull=0: Push one value [attr] for simple attribute access
                    {
                        int attrOparg = argument;
                        bool pushNullForMethod = (attrOparg & 1) == 1;
                        int attrNameIndex = attrOparg >> 1;

                        var attrName = frame.Code.NameObjects[attrNameIndex];
                        var obj = frame.ValueStack.Pop();

                        #if DEBUG_LOG
                        Console.WriteLine($"🔍 LOAD_ATTR: attribute '{attrName}' from object type: {obj.GetType().Name}, PyType: {obj.GetTypeName()}, pushNull={pushNullForMethod}");
                        if (obj is PyClassInstance objClassInst)
                        {
                            Console.WriteLine($"   → PyClassInstance of class: {objClassInst.PyClass.Name}");
                        }
                        #endif

                        // LOAD_ATTR: PyObject_GetAttr, or with the method bit _PyObject_GetMethod.
                        PyObject attr;
                        bool unboundMethod = false;
                        // Both name the AttributeError (set_attribute_error_context).
                        attr = pushNullForMethod ? obj.GetMethod(attrName, out unboundMethod) : obj.GetAttribute(attrName);

                        if (pushNullForMethod)
                        {
                            // meth_found: the unbound method descriptor and self, which CALL joins.
                            if (unboundMethod)
                            {
                                frame.ValueStack.Push(attr);
                                frame.ValueStack.Push(obj);
                            }
                            else
                            {
                                frame.ValueStack.Push(PyNull.Instance);
                                frame.ValueStack.Push(attr);
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
                    var setAttrName = frame.Code.NameObjects[argument];
                    var setObj = frame.ValueStack.Pop();
                    var setAttrValue = frame.ValueStack.Pop();
                    // 기존 Attribute 시스템 사용!
                    setObj.SetAttribute(setAttrName, setAttrValue);
                    break;

                case ByteCodeOp.DELETE_ATTR:
                    // CPython 3.12: Delete attribute from object
                    var delAttrName = frame.Code.NameObjects[argument];
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
                    // oparg: (name_index << 2) | (two_arguments << 1) | load_method
                    int superOparg = argument;
                    int superMethodFlag = superOparg & 1;
                    int superAttrIndex = superOparg >> 2;
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
                        // A name that shadows super is called as the source spelled it.
                        bool builtinSuper = superFunc is PyBuiltinFunction { Name: "super" } || ReferenceEquals(superFunc, PyType.SuperType);
                        var superArgs = builtinSuper || (superOparg & 2) != 0 ? new PyObject[] { classObj, selfObj } : Array.Empty<PyObject>();
                        PyObject superProxy = superFunc.Call(superArgs, null);

                        #if DEBUG_LOG
                        Console.WriteLine($"🔧 LOAD_SUPER_ATTR: calling GetAttribute({superAttrName}) on super proxy");
                        #endif

                        // LOAD_SUPER_ATTR (Python/bytecodes.c): res = PyObject_GetAttr(super, name), as it comes.
                        PyObject finalAttr = superProxy.GetAttribute(superAttrName);

                        // CPython 3.12: Push result based on method flag
                        if (superMethodFlag == 1)
                        {
                            // Method call: push [NULL, bound_method] for CALL optimization
                            frame.ValueStack.Push(PyNull.Instance);  // NULL marker
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
                    var isMapping = PyBool.FromBool((mappingSubject.GetPyType().CollectionFlags & PyType.TPFLAGS_MAPPING) != 0);
                    frame.ValueStack.Push(isMapping);
                    break;

                case ByteCodeOp.MATCH_SEQUENCE:
                    // Check if subject is a sequence type (list, tuple, etc.)
                    var sequenceSubject = frame.ValueStack.Peek(); // Keep subject on stack
                    var isSequence = PyBool.FromBool((sequenceSubject.GetPyType().CollectionFlags & PyType.TPFLAGS_SEQUENCE) != 0);
                    frame.ValueStack.Push(isSequence);
                    break;

                case ByteCodeOp.GET_LEN:
                    // PyObject_Length; the subject stays on the stack.
                    frame.ValueStack.Push(PyInt.From(frame.ValueStack.Peek().Length()));
                    break;

                case ByteCodeOp.MATCH_KEYS:
                    // MATCH_KEYS (Python/bytecodes.c), match_keys (Python/ceval.c): [subject, keys] stay, the
                    // values or None go on top. Any mapping answers through its own get.
                    frame.ValueStack.Push(MatchKeys(frame.ValueStack.PeekAt(1), (PyTuple)frame.ValueStack.PeekAt(0)));
                    break;

                case ByteCodeOp.POP_JUMP_IF_NONE:
                    // CPython 3.12: Pop top value and jump if it's None
                    var valueToCheck = frame.ValueStack.Pop();
                    if (valueToCheck == null || valueToCheck.Equals(PyNone.Instance))
                    {
                        // CPython 3.12: POP_JUMP_IF_NONE uses relative offset from next instruction
                        int currentPosNone = frame.InstructionPointer;
                        int relativeOffsetNone = argument;
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
                        int relativeOffsetNotNone = argument;
                        int targetInstructionIndexNotNone = currentPosNotNone + 1 + relativeOffsetNotNone;
                        // Subtract 1 because main loop will increment
                        frame.InstructionPointer = targetInstructionIndexNotNone - 1;
                    }
                    break;

                case ByteCodeOp.MATCH_CLASS:
                    {
                        // MATCH_CLASS (Python/bytecodes.c): [subject, type, names] become the attributes or None.
                        var classKwNames = (PyTuple)frame.ValueStack.Pop();
                        var classToMatch = frame.ValueStack.Pop();
                        var classSubject = frame.ValueStack.Pop();
                        frame.ValueStack.Push(MatchClass(classSubject, classToMatch, argument, classKwNames));
                    }
                    break;

                case ByteCodeOp.RETURN_VALUE:
                    var returnValue = frame.ValueStack.Count > 0 ? frame.ValueStack.Pop() : PyNone.Instance;
                    return returnValue;

                case ByteCodeOp.RETURN_CONST:
                    // CPython 3.12: Return constant value directly
                    var constValue = frame.Code.Constants[argument];
                    return constValue;

                case ByteCodeOp.GET_AWAITABLE:
                    // CPython 3.12 GET_AWAITABLE 구현
                    var awaitableObj = frame.ValueStack.Pop();
                    var awaitable = GetAwaitable(awaitableObj, argument);
                    if (awaitable is SharpPy.Core.PyCoroutine { YieldFrom: not null })
                        throw PyRuntimeError.Create("coroutine is being awaited already");
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
                    bool isTruthy = truthValue.IsTrue();
                    #if DEBUG_LOG
                    // Debug: Console.WriteLine($"🔄 POP_JUMP_IF_TRUE: popped value = {truthValue}, isTruthy = {isTruthy}");
                    #endif
                    if (isTruthy)  // CPython 3.12: Jump if the popped value is truthy
                    {
                        // CPython 3.12: POP_JUMP_IF_TRUE uses relative offset from next instruction (same as POP_JUMP_IF_FALSE)
                        int currentPosJump = frame.InstructionPointer;
                        int relativeOffset = argument;
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
                    bool isFalsy = !falseValue.IsTrue();
                    #if DEBUG_LOG
                    // Debug: Console.WriteLine($"🔄 POP_JUMP_IF_FALSE: popped value = {falseValue}, isFalsy = {isFalsy}");
                    #endif
                    if (isFalsy)  // CPython 3.12: Jump if the popped value is falsy
                    {
                        // CPython 3.12: POP_JUMP_IF_FALSE uses relative offset from next instruction
                        int currentPosJump = frame.InstructionPointer;
                        int relativeOffset = argument;
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
                    int jumpOffset = argument;
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
                    int jumpTarget = argument;

                    #if DEBUG_LOG
                    Console.WriteLine($"🔄 JUMP: from instr {frame.InstructionPointer} to instr {jumpTarget}");
                    #endif

                    // Subtract 1 because main loop will increment
                    frame.InstructionPointer = jumpTarget - 1;
                    return null;

                case ByteCodeOp.JUMP_NO_INTERRUPT:
                    // CPython 3.12: JUMP_NO_INTERRUPT is like JUMP but without interrupt check
                    // In SharpPy, we don't have interrupt checks, so it's identical to JUMP
                    int jumpNoIntTarget = argument;

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
                    int jumpBackNoIntDelta = argument;
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
                    // The back edge of a loop is the other safe point. JUMP_BACKWARD_NO_INTERRUPT
                    // deliberately has no such test, in CPython and here.
                    if (PyPending.Flag) EvalBreaker();
                    // CPython 3.12 호환: QuickenedCodeObject 방식으로 JUMP_BACKWARD 계산
                    int currentInstrPos = frame.InstructionPointer;
                    int targetInstrPos;

                    #if DEBUG_VM_LOG
                    Console.WriteLine($"🔧 JUMP_BACKWARD: currentIP={currentInstrPos}, arg={argument}");
                    #endif

                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 JUMP_BACKWARD Debug: currentInstrPos={currentInstrPos}, argument={argument}");
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

                    // bytecodes.c JUMP_BACKWARD: JUMPBY(-oparg), next_instr already past this one.
                    targetInstrPos = currentInstrPos + 1 - argument;

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

                    #if DEBUG_LOG
                    // Diagnostic inspection must not allocate on every loop back edge.
                    if (targetInstrPos >= 0 && targetInstrPos < frame.Code.Instructions.Count)
                    {
                        var targetInstruction = frame.Code.Instructions[targetInstrPos];
                        Console.WriteLine($"🔍 Target instruction at {targetInstrPos}: {targetInstruction.OpCode} (arg: {targetInstruction.Argument})");

                        // Verify this is a valid loop target (FOR_ITER for loops, various opcodes for WHILE loops)
                        if (targetInstruction.OpCode is ByteCodeOp.RETURN_VALUE or ByteCodeOp.RETURN_CONST or ByteCodeOp.RAISE_VARARGS)
                        {
                            Console.WriteLine($"⚠️ Warning: JUMP_BACKWARD targeting potentially invalid instruction {targetInstruction.OpCode}");
                        }
                    }
                    #endif

                    // CPython 3.12 호환: 점프 후 main loop가 ++하므로 -1 필요
                    // 하지만 FOR_ITER같은 경우는 target이 정확해야 함
                    frame.InstructionPointer = targetInstrPos - 1;
                    #if DEBUG_VM_LOG
                    Console.WriteLine($"    🎯 Setting IP to {targetInstrPos - 1}, after main loop++ will be {targetInstrPos}");
                    #endif
                    return null; // Continue execution from new position

                // CPython-style Container Building Opcodes (Phase 1)
                case ByteCodeOp.BUILD_LIST:
                    // Performance: Use array with reverse index instead of Insert(0, ...) which is O(n²)
                    var listSize = argument;
                    var listItems = new PyObject[listSize];
                    for (int i = listSize - 1; i >= 0; i--)
                    {
                        listItems[i] = frame.ValueStack.Pop();
                    }
                    var pyList = new PyList(listItems);
                    frame.ValueStack.Push(pyList);
                    break;

                case ByteCodeOp.BUILD_TUPLE:
                    var tupleSize = argument;
                    var tupleItems = new PyObject[tupleSize];
                    for (int i = tupleSize - 1; i >= 0; i--)
                    {
                        tupleItems[i] = frame.ValueStack.Pop();
                    }
                    // One empty tuple, as PyTuple_New hands back tuple_get_empty for size 0
                    // (Objects/tupleobject.c:68-73).
                    var pyTuple = tupleItems.Length == 0 ? PyTuple.Empty : PyTuple.From(tupleItems);
                    frame.ValueStack.Push(pyTuple);
                    break;

                case ByteCodeOp.LIST_EXTEND:
                    // CPython 3.12 LIST_EXTEND: extend the list at TOS1 with the iterable at TOS
                    // CPython: Python/bytecodes.c line 1507, _PyList_Extend calls list_extend
                    // list_extend uses PyObject_GetIter for generic iterables (Objects/listobject.c)
                    var extendIterable = frame.ValueStack.Pop();
                    try
                    {
                        ((PyList)frame.ValueStack.Peek()).Extend(extendIterable);
                    }
                    catch (PythonException ex) when (ex.PyException is PyTypeError &&
                        !extendIterable.HasTypeSlot("__iter__") &&
                        !extendIterable.HasTypeSlot("__getitem__"))
                    {
                        throw PyTypeError.Create($"Value after * must be an iterable, not {extendIterable.GetTypeName()}");
                    }
                    break;

                case ByteCodeOp.BUILD_SLICE:
                    // Build slice object - argument is 2 or 3
                    var sliceArgCount = argument;
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
                    frame.ValueStack.Push(PyAbstract.GetItem(subscriptObj, subscriptKey));
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
                    PyAbstract.SetItem(subscrStoreObj, subscrStoreKey, subscrStoreValue);
                    break;

                // CPython 3.12: Python/bytecodes.c:584-589 (DELETE_SUBSCR)
                // Implements: del container[sub]
                // Stack: [container, sub] -> []
                case ByteCodeOp.DELETE_SUBSCR:
                    var delSubSub = frame.ValueStack.Pop();        // sub (top of stack)
                    var delSubContainer = frame.ValueStack.Pop();  // container
                    PyAbstract.DelItem(delSubContainer, delSubSub);
                    break;

                // Duplicate BINARY_SUBSCR case removed

                case ByteCodeOp.BUILD_SET:
                    var setSize = argument;
                    var pySet = new PySet();
                    var setStack = frame.ValueStack.GetInternalList();
                    int setStart = setStack.Count - setSize;
                    try
                    {
                        for (int i = 0; i < setSize; i++) pySet.Add(setStack[setStart + i]);
                    }
                    finally { setStack.RemoveRange(setStart, setSize); }
                    frame.ValueStack.Push(pySet);
                    break;

                case ByteCodeOp.BUILD_MAP:
                    var mapSize = argument;
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
                    var constKeyCount = argument;
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

                case ByteCodeOp.DICT_UPDATE:
                    // CPython 3.12: Python/ceval.c DICT_UPDATE
                    // TOS에 있는 dict의 내용을 stack[-(arg)]에 있는 dict에 병합
                    // 주로 {**d} 딕셔너리 리터럴 언패킹에 사용됨
                    // Stack: [..., target_dict, source_dict] → [..., target_dict] (target_dict 수정됨)
                    var dictUpdateSource = frame.ValueStack.Pop();
                    if (dictUpdateSource is not PyDict && !dictUpdateSource.HasTypeSlot("keys"))
                        throw PyTypeError.Create($"'{dictUpdateSource.GetTypeName()}' object is not a mapping");
                    PyDict.UpdateCommon((PyDict)frame.ValueStack.PeekAt(argument - 1), dictUpdateSource);
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
                        var frameLocalDescriptions = new string[frame.Code.VarNames.Count];
                        for (int i = 0; i < frameLocalDescriptions.Length; i++)
                            frameLocalDescriptions[i] = $"{frame.Code.VarNames[i]}={frame.LocalsPlus[i]}";
                        Console.WriteLine($"   Frame locals: {string.Join(", ", frameLocalDescriptions)}");
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

                    if (yieldFromIterable is SharpPy.Core.PyCoroutine)
                    {
                        if ((frame.Code.Flags & (PyCodeObject.CO_COROUTINE | PyCodeObject.CO_ITERABLE_COROUTINE)) == 0)
                            throw PyTypeError.Create("cannot 'yield from' a coroutine object in a non-coroutine generator");
                        yieldFromIter = yieldFromIterable;
                    }
                    else if (yieldFromIterable is PyGenerator)
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
                        Console.WriteLine($"    Stack items: {string.Join(", ", stackItems)}");
                    }
                    #endif
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
                        // bytecodes.c FOR_ITER: JUMPBY(INLINE_CACHE_ENTRIES_FOR_ITER + oparg + 1); the main
                        // loop adds the one.
                        frame.InstructionPointer += argument + 1;

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

                            // the jump of FOR_ITER, which this specialization keeps
                            frame.InstructionPointer += argument + 1;
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

                            // the jump of FOR_ITER, which this specialization keeps
                            frame.InstructionPointer += argument + 1;
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
                    var compareResult = CompareOperation(compareLeft, compareRight, argument);
                    frame.ValueStack.Push(compareResult);
                    break;

                case ByteCodeOp.CONTAINS_OP:
                    var containsRight = frame.ValueStack.Pop();
                    var containsLeft = frame.ValueStack.Pop();
                    var containsResult = ContainsOperation(containsLeft, containsRight, argument);
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
                    var boolResult = notValue.IsTrue() ? PyBool.False : PyBool.True;
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
                        // Pop prev_exc from stack (pushed by PUSH_EXC_INFO) for stack shape.
                        var prevExcValue = frame.ValueStack.Pop();

                        // bytecodes.c POP_EXCEPT: exc_info takes the value PUSH_EXC_INFO saved.
                        var restored = prevExcValue is PyNone ? null : (PyBaseException)prevExcValue;
                        frame.CurrentException = restored;

                        #if DEBUG_LOG
                        Console.WriteLine($"🔧 POP_EXCEPT: Popped prev_exc={prevExcValue}; CurrentException={frame.CurrentException}");
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
                    // CPython: (sub_iter, last_sent_val, exc -- None, value).
                    var cleanupException = (PyBaseException)frame.ValueStack.Pop();
                    if (cleanupException is PyStopIteration stopped)
                    {
                        frame.ValueStack.Pop(); // last_sent_val
                        frame.ValueStack.Pop(); // sub_iter
                        frame.ValueStack.Push(PyNone.Instance);
                        frame.ValueStack.Push(stopped.Value);
                    }
                    else
                        throw new PythonException(cleanupException, fromReraise: true);
                    break;

                case ByteCodeOp.BEFORE_WITH:
                    // CPython 3.12: BEFORE_WITH performs several operations before a with block starts
                    var contextManager = frame.ValueStack.Pop();

                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 BEFORE_WITH: Processing context manager: {contextManager}");
                    #endif

                    // bytecodes.c BEFORE_WITH: both are looked up on the type, __enter__ first.
                    var enterMethod = WithMethod(contextManager, "__enter__") ?? throw PyTypeError.Create(
                        $"'{contextManager.GetTypeName()}' object does not support the context manager protocol");
                    var exitMethod = WithMethod(contextManager, "__exit__") ?? throw PyTypeError.Create(
                        $"'{contextManager.GetTypeName()}' object does not support the context manager protocol (missed __exit__ method)");

                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 BEFORE_WITH: Found __exit__ method: {exitMethod}");
                    #endif

                    var enterResult = enterMethod.Call(new PyObject[0], null);

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

                    // bytecodes.c BEFORE_ASYNC_WITH: both looked up on the type, __aenter__ first.
                    var aenterMethod = WithMethod(asyncContextManager, "__aenter__") ?? throw PyTypeError.Create(
                        $"'{asyncContextManager.GetTypeName()}' object does not support the asynchronous context manager protocol");
                    var aexitMethod = WithMethod(asyncContextManager, "__aexit__") ?? throw PyTypeError.Create(
                        $"'{asyncContextManager.GetTypeName()}' object does not support the asynchronous context manager protocol (missed __aexit__ method)");

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
                    if (newExc is PyBaseException pyExc)
                    {
                        frame.CurrentException = pyExc;
                    }

                    // __context__ capture now happens at exception dispatch (pre-unwind),
                    // matching CPython _PyErr_SetObject reading the topmost being-handled
                    // exception before the unwind discards the escaped handler scope.

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
                    if (!(val is PyBaseException pyExcVal))
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
                    // PyException_GetTraceback(val)
                    PyObject exc_tb = (PyObject)pyExcVal.__traceback__ ?? PyNone.Instance;

                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 WITH_EXCEPT_START: Calling exit_func(exc_type={exc_type}, val={val}, tb={exc_tb})");
                    #endif

                    // Keep the protected stack intact if __exit__ raises.
                    frame.ValueStack.Push(exit_func);
                    frame.ValueStack.Push(lasti);
                    frame.ValueStack.Push(unused);
                    frame.ValueStack.Push(val);

                    // CPython bytecodes.c:2545-2546: Call __exit__(exc_type, val, tb)
                    PyObject exitOutcome = PyBool.False;
                    if (exit_func?.IsCallable() == true)
                    {
                        try
                        {
                            // The result goes on the stack as it is. In async with it is the awaitable
                            // __aexit__ returned, and only the jump after it asks for truth.
                            exitOutcome = exit_func.Call(new PyObject[] {
                                exc_type,
                                val,
                                exc_tb
                            }, null);

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
                    frame.ValueStack.Push(exitOutcome);

                    // DEBUG: 스택 상태 확인
                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 WITH_EXCEPT_START 완료 후 스택 크기: {frame.ValueStack.Count}");
                    for (int i = 0; i < Math.Min(frame.ValueStack.Count, 5); i++)
                    {
                        var debugItem = frame.ValueStack.ToArray()[frame.ValueStack.Count - 1 - i];
                        Console.WriteLine($"  Stack[{frame.ValueStack.Count - 1 - i}]: {debugItem}");
                    }
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
                    var checkEgMatchRequiredStack = StackEffectAnalyzer.GetMinStackRequirement(ByteCodeOp.CHECK_EG_MATCH, argument);
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

                    ExceptionGroupMatch(egException, egType,
                        out PyObject egMatch, out PyObject egRest);

                    // CPython 3.12: CHECK_EG_MATCH pushes remainder first, then matched
                    // This way, STORE_NAME gets the matched exception group
                    frame.ValueStack.Push(egRest ?? PyNone.Instance);
                    frame.ValueStack.Push(egMatch ?? PyNone.Instance);
                    // PyErr_SetHandledException(match): inside the clause, the handled exception
                    // is the matched part, not the group that arrived.
                    if (egMatch != null && egMatch is not PyNone &&
                        egMatch is PyBaseException handledPart)
                        frame.CurrentException = handledPart;
                    break;

                case ByteCodeOp.RAISE_VARARGS:
                    // Resolve the active caller chain, including a resumed generator's
                    // own handler state. LastException is not handled exception state.
                    var handledException = GetTopmostException();
                    if (argument != 0)
                    {
                        var raiseCause = argument == 2 ? frame.ValueStack.Pop() : null;
                        DoRaise(frame, frame.ValueStack.Pop(), raiseCause, handledException);
                    }
                    else
                    {
                        // Bare raise uses the currently handled exception, not the last raised one.
                        if (handledException != null)
                            throw new PythonException(handledException, fromReraise: true);
                        else
                            throw new PythonException(new PyRuntimeError("No active exception to reraise"));
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

                    // CPython 3.12: Python/ceval.c CHECK_EXC_MATCH is PyObject_IsInstance(exc, given)
                    // and nothing else. This used to be a forest: CLR IsAssignableFrom reached through
                    // a name -> CLR type map for natives, OriginalClass for wrapped user exceptions,
                    // InstanceType for PyClassInstance, plus name-equality fallbacks. type(exc) is
                    // authoritative for all three kinds (measured: ValueError in
                    // type(SubValue("x")).__mro__ is True), so one walk over the raised object's own
                    // MRO replaces all of them.
                    bool matches = ExceptionMatchesHandler(exceptionInstance, expectedType);

                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 CHECK_EXC_MATCH: Result = {matches}");
                    #endif

                    frame.ValueStack.Push(matches ? PyBool.True : PyBool.False);
                    break;

                case ByteCodeOp.RERAISE:
                {
                    // bytecodes.c RERAISE (values[oparg], exc -- values[oparg]): with oparg, values[0]
                    // is the lasti of the instruction that raised; then exception_unwind.
                    var exceptionToReraise = (PyBaseException)frame.ValueStack.Pop();
                    if (argument > 0)
                    {
                        if (frame.ValueStack.PeekBelow(argument - 1) is PyInt restoredLasti)
                            frame.LastInstruction = (int)restoredLasti.Value;
                        else
                            throw new PythonException(new PySystemError("lasti is not an int"));
                    }
                    throw new PythonException(exceptionToReraise, fromReraise: true);
                }

                // F-String Support (PEP 701)
                case ByteCodeOp.FORMAT_VALUE:
                    var formatOption = argument;

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
                            3 => PyString.From(PyString.AsciiEscape(formatValue.ToRepr().Value)), // !a
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
                            3 => PyString.From(PyString.AsciiEscape(formatValue.ToRepr().Value)), // !a
                            _ => formatValue.Format(PyString.From(""))
                        };
                    }

                    frame.ValueStack.Push(formattedString);
                    break;

                case ByteCodeOp.BUILD_STRING:
                    // CPython 3.12: BUILD_STRING joins already-formatted PyString values
                    // Performance: Use array with reverse index instead of Insert(0, ...) which is O(n²)
                    var stringCount = argument;
                    var stringParts = new string[stringCount];
                    for (int i = stringCount - 1; i >= 0; i--)
                    {
                        var part = frame.ValueStack.Pop();
                        stringParts[i] = (part is PyString pyStr) ? pyStr.Value : part.ToStr().Value;
                    }
                    var concatenatedString = PyString.From(string.Concat(stringParts));
                    frame.ValueStack.Push(concatenatedString);
                    break;

                case ByteCodeOp.UNPACK_SEQUENCE:
                    // CPython 3.12 compatible tuple/sequence unpacking
                    var unpackCount = argument;
                    var sequence = frame.ValueStack.Pop();

                    // Unpack the sequence into individual elements
                    if (sequence is PyTuple { IsExact: true } tuple)
                    {
                        if (tuple.Items.Length != unpackCount)
                        {
                            throw UnpackCountError(unpackCount, tuple.Items.Length);
                        }

                        // CPython pushes elements in reverse order (last element pushed first)
                        // So when popped, they come out in correct order for assignment
                        for (int i = tuple.Items.Length - 1; i >= 0; i--)
                        {
                            frame.ValueStack.Push(tuple.Items[i]);
                        }
                    }
                    else if (sequence is PyList { IsExact: true } listDup)
                    {
                        if (listDup.Items.Length != unpackCount)
                        {
                            throw UnpackCountError(unpackCount, listDup.Items.Length);
                        }

                        // CPython pushes elements in reverse order
                        for (int i = listDup.Items.Length - 1; i >= 0; i--)
                        {
                            frame.ValueStack.Push(listDup.Items[i]);
                        }
                    }
                    // An exact str iterates its characters; a subclass is unpack_iterable's, over its __iter__.
                    else if (sequence is PyString { IsExact: true } str)
                    {
                        if (str.CodePointLength() != unpackCount)
                        {
                            throw UnpackCountError(unpackCount, str.CodePointLength());
                        }

                        for (int i = str.CodePointLength() - 1; i >= 0; i--)
                        {
                            frame.ValueStack.Push(str.GetItem(i));
                        }
                    }
                    else
                    {
                        // CPython 3.12: ceval.c unpack_iterable() - PyObject_GetIter first.
                        // Only a genuinely non-iterable object (tp_iter == NULL and not a
                        // sequence) reports "cannot unpack non-iterable X object"; a TypeError
                        // raised by a user __iter__ propagates unchanged.
                        PyObject unpackSeqIterator;
                        try
                        {
                            unpackSeqIterator = sequence.GetIterator();
                        }
                        catch (PythonException iterFailure) when (iterFailure.PyException is PyTypeError && IsNonIterable(sequence))
                        {
                            throw PyTypeError.Create($"cannot unpack non-iterable {sequence.GetTypeName()} object");
                        }

                        var items = new List<PyObject>();
                        while (true)
                        {
                            try
                            {
                                items.Add(unpackSeqIterator.Next());
                            }
                            catch (PythonException pex) when (pex.PyException is PyStopIteration)
                            {
                                break;
                            }
                        }

                        if (items.Count != unpackCount)
                        {
                            throw UnpackCountError(unpackCount, items.Count);
                        }

                        // Push items in reverse order (CPython convention)
                        for (int i = items.Count - 1; i >= 0; i--)
                        {
                            frame.ValueStack.Push(items[i]);
                        }
                    }
                    break;

                case ByteCodeOp.UNPACK_EX:
                    // CPython 3.12: Python/ceval.c lines 1105-1112 (bytecodes.c)
                    // CPython 3.12: Python/ceval.c lines 1950-2040 (unpack_iterable function)
                    // Argument encodes: lower 8 bits = count before star, upper 8 bits = count after star
                    var countBefore = argument & 0xFF;
                    var countAfter = (argument >> 8) & 0xFF;

                    var unpackExSequence = frame.ValueStack.Pop();

                    // CPython 3.12: Python/ceval.c:1960-1970 - Convert iterable to list first
                    PyObject[] itemsToUnpack;
                    if (unpackExSequence is PyList { IsExact: true } unpackExList)
                    {
                        itemsToUnpack = unpackExList.Items;
                    }
                    else if (unpackExSequence is PyTuple { IsExact: true } unpackExTuple)
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
                        // Unpacking a str yields characters (Objects/abstract.c unpack_iterable goes
                        // through PyObject_GetIter), so this uses the one coordinate-aware splitter.
                        itemsToUnpack = PyString.Characters(unpackExStr.Value);
                    }
                    else
                    {
                        // CPython 3.12: Python/ceval.c unpack_iterable() - PyObject_GetIter first.
                        // A non-iterable reports "cannot unpack non-iterable X object", and an
                        // exception raised by __next__ propagates unchanged. Only StopIteration
                        // ends the collection loop.
                        PyObject unpackIterator;
                        try
                        {
                            unpackIterator = unpackExSequence.GetIterator();
                        }
                        catch (PythonException iterFailure) when (iterFailure.PyException is PyTypeError && IsNonIterable(unpackExSequence))
                        {
                            throw PyTypeError.Create($"cannot unpack non-iterable {unpackExSequence.GetTypeName()} object");
                        }

                        // _PyEval_UnpackIterable: what comes before the star, one at a time, and
                        // then the rest as PySequence_List.
                        var unpackItems = new System.Collections.Generic.List<PyObject>();
                        while (unpackItems.Count < countBefore)
                        {
                            try
                            {
                                unpackItems.Add(unpackIterator.Next());
                            }
                            catch (PythonException stopSignal) when (stopSignal.PyException is PyStopIteration)
                            {
                                throw PyValueError.Create($"not enough values to unpack (expected at least {countBefore + countAfter}, got {unpackItems.Count})");
                            }
                        }
                        unpackItems.AddRange(PyList.FromIterable(unpackIterator).Items);
                        itemsToUnpack = unpackItems.ToArray();
                    }

                    if (itemsToUnpack.Length < countBefore + countAfter)
                    {
                        throw PyValueError.Create($"not enough values to unpack (expected at least {countBefore + countAfter}, got {itemsToUnpack.Length})");
                    }

                    // CPython 3.12: Python/ceval.c unpack_iterable() lines 1950-2040
                    //
                    // STORE order determines required stack layout:
                    //   STORE_NAME(before[0]), STORE_NAME(before[1]), ..., STORE_NAME(star), STORE_NAME(after[0]), ...
                    // Each STORE pops from TOS, so stack must be (bottom→top):
                    //   after[n-1], after[n-2], ..., after[0], star, before[n-1], ..., before[1], before[0]
                    //
                    // Example: a, *b, c = [1, 2, 3, 4, 5]
                    //   countBefore=1 (a), countAfter=1 (c)
                    //   before=[1], star=[2,3,4], after=[5]
                    //   Stack (bottom→top): 5, [2,3,4], 1
                    //   Pop order: 1(a), [2,3,4](b), 5(c) ✓

                    // Performance optimization: Build array once, then AddRange (O(n) instead of O(n²) Insert)
                    var totalElements = countAfter + 1 + countBefore;
                    var elementsToAdd = new PyObject[totalElements];
                    int elemIdx = 0;

                    // After elements go at bottom of stack segment (popped last)
                    // after[n-1] at bottom (first in array), after[0] closer to top
                    // Example: after=[30,40] → stack bottom has 40, then 30
                    for (int i = countAfter - 1; i >= 0; i--)
                    {
                        // after[i] = itemsToUnpack[length - countAfter + i]
                        elementsToAdd[elemIdx++] = itemsToUnpack[itemsToUnpack.Length - countAfter + i];
                    }

                    // Star list in middle
                    var starCount = itemsToUnpack.Length - countBefore - countAfter;
                    var starItems = new PyObject[starCount];
                    for (int i = 0; i < starCount; i++)
                    {
                        starItems[i] = itemsToUnpack[countBefore + i];
                    }
                    elementsToAdd[elemIdx++] = new PyList(starItems);

                    // Before elements go at top of stack segment (popped first)
                    // before[0] at top, before[n-1] at bottom of before-section
                    for (int i = countBefore - 1; i >= 0; i--)
                    {
                        elementsToAdd[elemIdx++] = itemsToUnpack[i];
                    }

                    // Add all elements at once - O(n) instead of O(n²)
                    var stackList = frame.ValueStack.GetInternalList();
                    stackList.AddRange(elementsToAdd);
                    break;

                // CPython 3.12: BREAK_LOOP and CONTINUE_LOOP removed
                // Loop control now uses structured JUMP_FORWARD/JUMP_BACKWARD

                case ByteCodeOp.IMPORT_NAME:
                    // CPython 3.12: IMPORT_NAME(namei)
                    // TOS = fromlist, TOS1 = level
                    // Implements: __import__(name, globals(), locals(), fromlist, level)
                    var fromlist = frame.ValueStack.Pop(); // TOS
                    var level = frame.ValueStack.Pop();    // TOS1
                    var moduleName = frame.Code.Names[argument];

                    // import_name: __import__ comes from the frame's builtins, and a replaced one is called.
                    var importer = frame.Builtin("__import__") ?? throw PyImportError.Create("__import__ not found");
                    if (importer is not PyBuiltinFunction { Name: "__import__" })
                    {
                        frame.ValueStack.Push(importer.Call(new PyObject[]
                        {
                            PyString.From(moduleName), PyGlobalsDict.DictOf(frame.Globals),
                            frame.ClassLocalsDict ?? ((frame.Code.Flags & PyCodeObject.CO_OPTIMIZED) != 0
                                ? frame.GetAttribute("f_locals") : PyGlobalsDict.DictOf(frame.Globals)),
                            fromlist, level,
                        }, null));
                        break;
                    }

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
                            fromlistArray[i] = item is PyString s ? s.Value : item.ToClrString();
                        }
                    }

                    // CPython 3.12: Pass frame.Globals to import system for relative import resolution
                    var importedModule = PyImportSystem.Import(moduleName, importLevel, fromlistArray, frame.Globals);
                    frame.ValueStack.Push(importedModule);
                    break;

                case ByteCodeOp.IMPORT_FROM:
                    var itemName = frame.Code.Names[argument];
                    if (frame.ValueStack.Count == 0)
                    {
                        throw new Exception($"IMPORT_FROM: Stack empty when trying to import '{itemName}'. This may be caused by incorrect bytecode generation.");
                    }
                    var module = frame.ValueStack.Peek(); // Don't pop yet, needed for multiple imports

                    // Normal case: import specific name
                    // CPython 3.12: Python/ceval.c:2530-2560 (import_from)
                    PyObject importedItem = null;

                    // Step 1: Try to get attribute from module object
                    // CPython 3.12: Python/ceval.c:2535-2537 (_PyObject_LookupAttr)
                    importedItem = module.LookupAttribute(itemName);

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
                        throw ImportFromError(module, itemName);
                    }

                    frame.ValueStack.Push(importedItem);
                
                    break;

                // PEP 709 Comprehension Optimization - VM 구현
                case ByteCodeOp.LIST_APPEND:
                    // CPython 3.12 호환: LIST_APPEND i
                    // 스택: [..., list, ..., item] → [..., list, ...]
                    #if DEBUG_VM_LOG
                    Console.WriteLine($"🔧 LIST_APPEND {argument}: Stack before pop = {frame.ValueStack.Count}");
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
                        Console.WriteLine($"    Stack: {string.Join(", ", stackBefore)}");
                    }
                    #endif

                    var itemToAppend = frame.ValueStack.Pop();
                    #if DEBUG_VM_LOG
                    Console.WriteLine($"    Popped item: {itemToAppend.GetType().Name}");
                    #endif

                    // CPython 3.12: LIST_APPEND i에서 타겟 리스트 찾기
                    var targetDepth = argument - 1; // 0-based 인덱스
                    #if DEBUG_VM_LOG
                    Console.WriteLine($"    Looking for list at depth {targetDepth} (arg={argument})");
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
                    var setTargetDepth = argument - 1; // 0-based 인덱스

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
                    var dictDepth = argument;

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
                case ByteCodeOp.LOAD_LOCALS:
                    frame.ValueStack.Push(frame.ClassLocalsDict ?? frame.GetAttribute("f_locals"));
                    break;

                case ByteCodeOp.LOAD_FROM_DICT_OR_GLOBALS:
                    var globalsMapping = frame.ValueStack.Pop();
                    var mappedName = frame.Code.Names[argument];
                    frame.ValueStack.Push(LookupNamespaceMapping(globalsMapping, PyString.From(mappedName))
                        ?? (frame.Globals.TryGetValue(mappedName, out var mappedGlobal) ? mappedGlobal : null)
                        ?? frame.ScopeChain.BuiltinModule.GetBuiltin(mappedName)
                        ?? throw CreateNameErrorWithSuggestion(mappedName, frame));
                    break;

                // The deref instructions (Python/bytecodes.c): oparg is a localsplus slot holding a cell.
                case ByteCodeOp.LOAD_FROM_DICT_OR_DEREF:
                {
                    var classDict = frame.ValueStack.Pop();
                    var mappedClosureValue = LookupNamespaceMapping(classDict, frame.Code.LocalsPlusKeys[argument]);
                    if (mappedClosureValue == null)
                    {
                        var cell = frame.CellAt(argument);
                        if (!cell.HasValue)
                            throw frame.UnboundDeref(argument);
                        mappedClosureValue = cell.Value;
                    }
                    frame.ValueStack.Push(mappedClosureValue);
                    break;
                }

                case ByteCodeOp.LOAD_DEREF:
                {
                    var cell = frame.CellAt(argument);
                    if (!cell.HasValue)
                        throw frame.UnboundDeref(argument);
                    frame.ValueStack.Push(cell.Value);
                    break;
                }

                case ByteCodeOp.STORE_DEREF:
                    frame.CellAt(argument).SetValue(frame.ValueStack.Pop());
                    break;

                case ByteCodeOp.DELETE_DEREF:
                {
                    var cell = frame.CellAt(argument);
                    if (!cell.HasValue)
                        throw frame.UnboundDeref(argument);
                    cell.Clear();
                    break;
                }

                // LOAD_CLOSURE: LOAD_FAST_CHECK under another name, kept for readable bytecode.
                case ByteCodeOp.LOAD_CLOSURE:
                {
                    var cell = frame.LocalsPlus[argument];
                    if (PyNull.IsNull(cell))
                        throw PyUnboundLocalError.Create($"cannot access local variable '{frame.Code.LocalsPlusNames[argument]}' where it is not associated with a value");
                    frame.ValueStack.Push(cell);
                    break;
                }

                // COPY_FREE_VARS: func_closure into the last oparg slots.
                case ByteCodeOp.COPY_FREE_VARS:
                {
                    int offset = frame.Code.NLocalsPlus - argument;
                    for (int i = 0; i < argument; i++)
                        frame.LocalsPlus[offset + i] = frame.Closure[i];
                    break;
                }

                // MAKE_CELL: the slot's value, NULL unless an argument, moves into a new cell.
                case ByteCodeOp.MAKE_CELL:
                    frame.LocalsPlus[argument] = new PyCell(frame.LocalsPlus[argument]);
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
                    if (!frame.Code.IsGenerator() && !frame.Code.IsCoroutine() && !frame.Code.IsAsyncGenerator())
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
                    Console.WriteLine($"🔍 SEND: IP={frame.InstructionPointer}, Stack.Count = {frame.ValueStack.Count}, Arg={argument}");
                    #endif
                    var sendValue = frame.ValueStack.Pop();
                    var receiver = frame.ValueStack.Peek(); // Keep receiver on stack

                    try
                    {
                        PyObject sendResult;

                        // None uses the iterator protocol, including Python-defined
                        // iterators. A non-None value requires send; its errors propagate.
                        if (receiver is SharpPy.Core.PyCoroutine awaited)
                            sendResult = awaited.Send(sendValue);
                        else if (sendValue is PyNone)
                            sendResult = receiver.Next();
                        else if (receiver is PyGenerator gen)
                            sendResult = gen.Send(sendValue);
                        else if (receiver is SharpPy.Core.PyCoroutineAwaiter wrapper &&
                                 receiver.GetType() == typeof(SharpPy.Core.PyCoroutineAwaiter))
                            sendResult = wrapper.Send(sendValue);
                        else
                            sendResult = receiver.GetAttribute("send").Call(new PyObject[] { sendValue }, null);

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
                        Console.WriteLine($"    🛑 SEND: Jumping from IP={frame.InstructionPointer} by oparg={argument}");
                        #endif

                        // Push StopIteration value to stack (receiver stays on stack for END_SEND)
                        frame.ValueStack.Push(stopIter.Value ?? PyNone.Instance);

                        // CPython 3.12: SEND has 1 CACHE entry (Include/internal/pycore_opcode.h:120)
                        const int INLINE_CACHE_ENTRIES_SEND = 1;

                        // Jump forward: IP += (INLINE_CACHE_ENTRIES_SEND + oparg)
                        // Example: IP=36 + (1 + 4) = 41, main loop IP++ → 42 (END_SEND)
                        frame.InstructionPointer += INLINE_CACHE_ENTRIES_SEND + argument;

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
                case ByteCodeOp.LOAD_BUILD_CLASS:
                    // bytecodes.c: looked up in the builtins alone, a global of that name does not count.
                    frame.ValueStack.Push(frame.Builtin("__build_class__")
                        ?? throw PyNameError.Create("__build_class__ not found"));
                    break;

                case ByteCodeOp.SET_UPDATE:
                {
                    // bytecodes.c: _PySet_Update(PEEK(oparg), iterable)
                    var setSource = frame.ValueStack.Pop();
                    var setTarget = (PySet)frame.ValueStack.PeekAt(argument - 1);
                    foreach (var updateItem in PySet.IterableItems(setSource))
                        setTarget.Add(updateItem);
                    break;
                }

                case ByteCodeOp.BINARY_SLICE:
                    // Stack: TOS = stop, TOS1 = start, TOS2 = container
                    // Result: container[start:stop]
                    var sliceStop = frame.ValueStack.Pop();
                    var sliceStart = frame.ValueStack.Pop();
                    var sliceContainer = frame.ValueStack.Pop();

                    // PySlice 객체 생성하여 실제 슬라이싱 수행
                    var slice = new PySlice(sliceStart, sliceStop);
                    var sliceResult = PyAbstract.GetItem(sliceContainer, slice);
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
                    PyAbstract.SetItem(sliceStoreContainer, storeSlice, sliceValue);
                    break;

                case ByteCodeOp.CALL_INTRINSIC_1:
                    var intrinsicArg1 = frame.ValueStack.Pop();
                    var intrinsicResult1 = ExecuteIntrinsicFunction1(argument, intrinsicArg1);
                    frame.ValueStack.Push(intrinsicResult1);
                    break;

                case ByteCodeOp.CALL_INTRINSIC_2:
                    // CPython 3.12: CALL_INTRINSIC_2 for exception handling
                    // Stack: [..., arg1, arg2] -> pop arg2 first, then arg1
                    var intrinsic2_arg2 = frame.ValueStack.Pop();  // TOS (second argument)
                    var intrinsic2_arg1 = frame.ValueStack.Pop();  // TOS-1 (first argument)
                    var result2 = ExecuteIntrinsicFunction2(argument, intrinsic2_arg1, intrinsic2_arg2);
                    frame.ValueStack.Push(result2);
                    break;

                case ByteCodeOp.KW_NAMES:
                    // CPython 3.12: KW_NAMES sets the names for keyword arguments
                    // The argument is an index into the constants table containing a tuple of keyword names
                    var kwNamesIndex = argument;
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
                    // unknown_opcode
                    throw PySystemError.Create($"{frame.Code.FileName}:{frame.CurrentLineNumber}: unknown opcode {(int)opcode}");
            }

            return null;
        }

        // 이항 연산 (기존 타입 시스템 활용)
        // CPython 3.12+ unified binary operation executor
        // PyUnicode_Check: a str subclass instance is a str.
        private static bool IsStrLikeInstance(PyObject o) => o is PyString;

        // unpack_iterable (Python/ceval.c)
        private static Exception UnpackCountError(int expected, int got) => PyValueError.Create(got < expected
            ? $"not enough values to unpack (expected {expected}, got {got})"
            : $"too many values to unpack (expected {expected})");

        private static string BinarySlotName(BinaryOpType operation) => operation switch
        {
            BinaryOpType.ADD => "__add__",
            BinaryOpType.MULTIPLY => "__mul__",
            BinaryOpType.SUBTRACT => "__sub__",
            BinaryOpType.TRUE_DIVIDE => "__truediv__",
            BinaryOpType.FLOOR_DIVIDE => "__floordiv__",
            BinaryOpType.MODULO => "__mod__",
            BinaryOpType.POWER => "__pow__",
            BinaryOpType.LSHIFT => "__lshift__",
            BinaryOpType.RSHIFT => "__rshift__",
            BinaryOpType.AND => "__and__",
            BinaryOpType.OR => "__or__",
            BinaryOpType.XOR => "__xor__",
            BinaryOpType.MATRIX_MULTIPLY => "__matmul__",
            BinaryOpType.DIVMOD => "__divmod__",
            BinaryOpType.INPLACE_ADD => "__iadd__",
            BinaryOpType.INPLACE_MULTIPLY => "__imul__",
            BinaryOpType.INPLACE_SUBTRACT => "__isub__",
            BinaryOpType.INPLACE_TRUE_DIVIDE => "__itruediv__",
            BinaryOpType.INPLACE_FLOOR_DIVIDE => "__ifloordiv__",
            BinaryOpType.INPLACE_MODULO => "__imod__",
            BinaryOpType.INPLACE_POWER => "__ipow__",
            BinaryOpType.INPLACE_LSHIFT => "__ilshift__",
            BinaryOpType.INPLACE_RSHIFT => "__irshift__",
            BinaryOpType.INPLACE_AND => "__iand__",
            BinaryOpType.INPLACE_OR => "__ior__",
            BinaryOpType.INPLACE_XOR => "__ixor__",
            BinaryOpType.INPLACE_MATRIX_MULTIPLY => "__imatmul__",
            _ => throw new InvalidOperationException($"no slot for {operation}"),
        };

        // PyNumber_Divmod: binary_op(v, w, NB_SLOT(nb_divmod), "divmod()").
        internal static PyObject NumberDivmod(PyObject v, PyObject w)
        {
            var result = Instance.BinaryOp1(v, w, BinaryOpType.DIVMOD);
            if (ReferenceEquals(result, PyNotImplemented.Instance))
                throw PyTypeError.Create($"unsupported operand type(s) for divmod(): '{v.GetPyType().FullName}' and '{w.GetPyType().FullName}'");
            return result;
        }

        // PyNumber_Add and friends, PyNumber_InPlaceAdd and friends. CPython v3.12.15 Objects/abstract.c.
        internal PyObject ExecuteBinaryOpType(PyObject left, PyObject right, BinaryOpType binaryOp)
        {
            var regular = binaryOp switch
            {
                BinaryOpType.INPLACE_ADD => BinaryOpType.ADD,
                BinaryOpType.INPLACE_SUBTRACT => BinaryOpType.SUBTRACT,
                BinaryOpType.INPLACE_MULTIPLY => BinaryOpType.MULTIPLY,
                BinaryOpType.INPLACE_TRUE_DIVIDE => BinaryOpType.TRUE_DIVIDE,
                BinaryOpType.INPLACE_FLOOR_DIVIDE => BinaryOpType.FLOOR_DIVIDE,
                BinaryOpType.INPLACE_MODULO => BinaryOpType.MODULO,
                BinaryOpType.INPLACE_POWER => BinaryOpType.POWER,
                BinaryOpType.INPLACE_LSHIFT => BinaryOpType.LSHIFT,
                BinaryOpType.INPLACE_RSHIFT => BinaryOpType.RSHIFT,
                BinaryOpType.INPLACE_AND => BinaryOpType.AND,
                BinaryOpType.INPLACE_OR => BinaryOpType.OR,
                BinaryOpType.INPLACE_XOR => BinaryOpType.XOR,
                BinaryOpType.INPLACE_MATRIX_MULTIPLY => BinaryOpType.MATRIX_MULTIPLY,
                _ => binaryOp,
            };
            if (regular != binaryOp)
                return InPlaceNumber(left, right, binaryOp, regular);
            var result = BinaryOp1(left, right, binaryOp);
            return ReferenceEquals(result, PyNotImplemented.Instance)
                ? SequenceOrTypeError(left, right, binaryOp, binaryOp)
                : result;
        }

        // A class has slot_nb_* when __op__ or __rop__ was written in Python somewhere in its MRO.
        // update_one_slot, Objects/typeobject.c.
        // format_missing: "argument: 'a'", "arguments: 'a' and 'b'", "arguments: 'a', 'b', and 'c'"
        internal static string MissingText(List<string> names)
        {
            var quoted = names.ConvertAll(name => "'" + name + "'");
            return quoted.Count switch
            {
                1 => "argument: " + quoted[0],
                2 => $"arguments: {quoted[0]} and {quoted[1]}",
                _ => "arguments: " + string.Join(", ", quoted.GetRange(0, quoted.Count - 1)) + ", and " + quoted[^1],
            };
        }

        // positional_only_passed_as_keyword
        internal static void RefusePositionalOnly(PyCodeObject code, IDictionary<string, PyObject> keywords)
        {
            var names = new List<string>();
            for (int index = 0; index < code.PosonlyArgCount; index++)
                if (keywords.ContainsKey(code.VarNames[index]))
                    names.Add(code.VarNames[index]);
            if (names.Count > 0)
                throw PyTypeError.Create($"{code.Name}() got some positional-only arguments passed as keyword arguments: '{string.Join(", ", names)}'");
        }

        private static bool HasNumberSlot(PyType type, string name, string reflected)
        {
            if (type is not PyClass)
                return false;
            foreach (var entry in type.MRO)
            {
                if (entry is PyClass written &&
                    (written.ClassDict.TryGetValue(name, out _) || written.ClassDict.TryGetValue(reflected, out _)))
                    return true;
            }
            return false;
        }

        // list, tuple, str, bytes, bytearray, deque and array have sq_concat and sq_repeat, and no
        // nb_add or nb_multiply.
        private static bool HasOnlySequenceSlot(PyObject value, BinaryOpType operation) =>
            operation is BinaryOpType.ADD or BinaryOpType.MULTIPLY &&
            value is PyList or PyTuple or PyString or PyBytes or PyByteArray
                or SharpPy.Modules.PyDeque or SharpPy.Modules.ArrayModule.PyArray;

        // SQSLOT("__add__", sq_concat, NULL, ...): __add__ written in Python leaves the slot empty.
        private static bool HasSequenceSlot(PyObject value, BinaryOpType operation) =>
            HasOnlySequenceSlot(value, operation) && value.LookupUserSpecial(BinarySlotName(operation)) == null;

        // binary_op1, Objects/abstract.c.
        private PyObject BinaryOp1(PyObject v, PyObject w, BinaryOpType operation)
        {
            // WRAP_BINARY: the slot of a proxy on the left works on what it refers to.
            if (v is PyWeakProxy)
                return ExecuteBinaryOpType(PyWeakProxy.Unwrap(v), PyWeakProxy.Unwrap(w), operation);
            var typeV = v.GetPyType();
            var typeW = w.GetPyType();
            bool sameType = ReferenceEquals(typeV, typeW);
            string name = null, reflected = null;
            bool writtenV = false, writtenW = false;
            if (typeV is PyClass || typeW is PyClass)
            {
                name = BinarySlotName(operation);
                reflected = "__r" + name.Substring(2);
                writtenV = HasNumberSlot(typeV, name, reflected);
                writtenW = !sameType && HasNumberSlot(typeW, name, reflected);
            }

            bool sequenceV = HasOnlySequenceSlot(v, operation);
            // slotw == slotv: slot_nb_* of the left operand asks both.
            bool slotW = !sameType && !(writtenV && writtenW);
            PyObject result;
            if (slotW && writtenW && (writtenV || !sequenceV) && typeW.IsSubclassOf(typeV))
            {
                result = SlotNumberBinary(v, w, name, reflected);
                if (!ReferenceEquals(result, PyNotImplemented.Instance))
                    return result;
                slotW = false;
            }
            if (writtenV)
            {
                result = SlotNumberBinary(v, w, name, reflected);
                if (!ReferenceEquals(result, PyNotImplemented.Instance))
                    return result;
            }
            else if (!sequenceV)
            {
                result = BuiltinNumberSlot(v, w, operation);
                if (!ReferenceEquals(result, PyNotImplemented.Instance))
                    return result;
            }
            if (!slotW)
                return PyNotImplemented.Instance;
            if (writtenW)
                return SlotNumberBinary(v, w, name, reflected);
            // A number slot of a builtin takes either side, and reads no sequence.
            if (sequenceV || HasOnlySequenceSlot(w, operation))
                return PyNotImplemented.Instance;
            var method = w.LookupSpecialMethodUncached(reflected ?? "__r" + BinarySlotName(operation).Substring(2));
            return method == null
                ? PyNotImplemented.Instance
                : w.BindSpecialMethod(method).Call(new[] { v }, null);
        }

        // SLOT1BINFULL, Objects/typeobject.c.
        private static PyObject SlotNumberBinary(PyObject self, PyObject other, string name, string reflected)
        {
            var typeSelf = self.GetPyType();
            var typeOther = other.GetPyType();
            bool sameType = ReferenceEquals(typeSelf, typeOther);
            bool doOther = !sameType && HasNumberSlot(typeOther, name, reflected);
            if (HasNumberSlot(typeSelf, name, reflected))
            {
                PyObject result;
                if (doOther && typeOther.IsSubclassOf(typeSelf) && MethodIsOverloaded(self, other, reflected))
                {
                    result = CallMaybe(other, reflected, self);
                    if (!ReferenceEquals(result, PyNotImplemented.Instance))
                        return result;
                    doOther = false;
                }
                result = CallMaybe(self, name, other);
                if (!ReferenceEquals(result, PyNotImplemented.Instance) || sameType)
                    return result;
            }
            return doOther ? CallMaybe(other, reflected, self) : PyNotImplemented.Instance;
        }

        private static bool MethodIsOverloaded(PyObject left, PyObject right, string name)
        {
            var overriding = right.LookupSpecialMethodUncached(name);
            if (overriding == null)
                return false;
            return !ReferenceEquals(left.LookupSpecialMethodUncached(name), overriding);
        }

        // vectorcall_maybe: a type without the method declines.
        private static PyObject CallMaybe(PyObject self, string name, PyObject argument)
        {
            var method = self.LookupSpecialMethodUncached(name);
            return method == null
                ? PyNotImplemented.Instance
                : self.BindSpecialMethod(method).Call(new[] { argument }, null);
        }

        // PyFloat_Check and PyLong_Check take subclasses: float_add reads the value of an int subclass.
        private static PyObject NumberOperand(PyObject left, PyObject right)
        {
            bool real = left is PyFloat or PyFloatSubclass or PyComplex;
            if (right is PyIntSubclass integer && (real || left is PyInt or PyIntSubclass or PyBool))
                return integer.GetIntValue();
            if (right is PyFloatSubclass floating && real)
                return floating.GetFloatValue();
            return right;
        }

        private static PyObject BuiltinNumberSlot(PyObject left, PyObject right, BinaryOpType operation)
        {
            if (left is PyClassInstance)
            {
                var inherited = left.LookupSpecialMethodUncached(BinarySlotName(operation));
                if (inherited != null)
                    return left.BindSpecialMethod(inherited).Call(new[] { right }, null);
            }
            // bool_and, bool_or and bool_xor: two bools give a bool; anything else is long_*'s.
            if (left is PyBool truth && right is PyBool && operation is BinaryOpType.AND or BinaryOpType.OR or BinaryOpType.XOR)
                return operation switch
                {
                    BinaryOpType.AND => truth.BitwiseAnd(right),
                    BinaryOpType.OR => truth.BitwiseOr(right),
                    _ => truth.BitwiseXor(right),
                };
            // The nb_* slot of a builtin number: its own operand rules, NotImplemented otherwise.
            if (PyNumberSlots.KindOf(left) is { } kind)
                return operation == BinaryOpType.POWER
                    ? PyNumberSlots.Power(kind, left, right, null)
                    : PyNumberSlots.Binary(kind, operation, left, right);
            right = NumberOperand(left, right);
            return operation switch
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
                BinaryOpType.MATRIX_MULTIPLY => left.MatrixMultiply(right),
                BinaryOpType.DIVMOD => left.DivMod(right),
                _ => throw PyTypeError.Create($"unsupported binary operation: {operation}"),
            };
        }

        private static PyObject BuiltinInPlaceSlot(PyObject left, PyObject right, BinaryOpType operation) => operation switch
        {
            BinaryOpType.INPLACE_ADD => left.InplaceAdd(right),
            BinaryOpType.INPLACE_SUBTRACT => left.InplaceSubtract(right),
            BinaryOpType.INPLACE_MULTIPLY => left.InplaceMultiply(right),
            BinaryOpType.INPLACE_TRUE_DIVIDE => left.InplaceDivide(right),
            BinaryOpType.INPLACE_FLOOR_DIVIDE => left.InplaceFloorDivide(right),
            BinaryOpType.INPLACE_MODULO => left.InplaceModulo(right),
            BinaryOpType.INPLACE_POWER => left.InplacePower(right),
            BinaryOpType.INPLACE_LSHIFT => left.InplaceLeftShift(right),
            BinaryOpType.INPLACE_RSHIFT => left.InplaceRightShift(right),
            BinaryOpType.INPLACE_AND => left.InplaceBitwiseAnd(right),
            BinaryOpType.INPLACE_OR => left.InplaceBitwiseOr(right),
            BinaryOpType.INPLACE_XOR => left.InplaceBitwiseXor(right),
            BinaryOpType.INPLACE_MATRIX_MULTIPLY => left.InplaceMatrixMultiply(right),
            _ => null,
        };

        // PyNumber_InPlaceAdd and friends: binary_iop1, then sq_inplace_concat when the numbers declined.
        private PyObject InPlaceNumber(PyObject left, PyObject right, BinaryOpType operation, BinaryOpType regular)
        {
            var name = BinarySlotName(operation);
            bool sequence = HasOnlySequenceSlot(left, regular) && left.LookupUserSpecial(name) == null;
            var result = BinaryIop1(left, right, operation, regular, sequence);
            if (!ReferenceEquals(result, PyNotImplemented.Instance))
                return result;
            if (sequence)
            {
                var method = left.LookupSpecialMethodUncached(name);
                result = method != null
                    ? left.BindSpecialMethod(method).Call(new[] { right }, null)
                    : BuiltinInPlaceSlot(left, right, operation);
                if (result != null && !ReferenceEquals(result, PyNotImplemented.Instance))
                    return result;
            }
            return SequenceOrTypeError(left, right, regular, operation);
        }

        // binary_iop1: nb_inplace_<op> of the left operand, then binary_op1; NotImplemented when both
        // decline. A sequence's in-place slot is its sq_inplace_concat, which comes after.
        private PyObject BinaryIop1(PyObject left, PyObject right, BinaryOpType operation, BinaryOpType regular, bool sequence)
        {
            if (!sequence)
            {
                var method = left.LookupSpecialMethodUncached(BinarySlotName(operation));
                PyObject result;
                if (method != null)
                {
                    result = left.BindSpecialMethod(method).Call(new[] { right }, null);
                    if (result != null && !ReferenceEquals(result, PyNotImplemented.Instance))
                        return result;
                }
                result = BuiltinInPlaceSlot(left, right, operation);
                if (result != null && !ReferenceEquals(result, PyNotImplemented.Instance))
                    return result;
            }
            return BinaryOp1(left, right, regular);
        }

        /// <summary>
        /// PySequence_InPlaceConcat (Objects/abstract.c): sq_inplace_concat, else sq_concat, of the
        /// left operand; for two other sequences binary_iop1 of +=; else "can't be concatenated".
        /// </summary>
        internal static PyObject SequenceInPlaceConcat(PyObject s, PyObject o)
        {
            if (HasOnlySequenceSlot(s, BinaryOpType.ADD))
            {
                var inPlace = BuiltinInPlaceSlot(s, o, BinaryOpType.INPLACE_ADD);
                return inPlace != null && !ReferenceEquals(inPlace, PyNotImplemented.Instance) ? inPlace : SequenceConcat(s, o);
            }
            if (IsSequence(s) && IsSequence(o))
            {
                var result = Instance.BinaryIop1(s, o, BinaryOpType.INPLACE_ADD, BinaryOpType.ADD, false);
                if (!ReferenceEquals(result, PyNotImplemented.Instance))
                    return result;
            }
            throw PyTypeError.Create($"'{s.GetTypeName()}' object can't be concatenated");
        }

        /// <summary>
        /// PySequence_Concat (Objects/abstract.c): sq_concat of the left operand, or binary_op1 for
        /// two objects that are both sequences, else "'X' object can't be concatenated".
        /// </summary>
        internal static PyObject SequenceConcat(PyObject s, PyObject o)
        {
            if (HasOnlySequenceSlot(s, BinaryOpType.ADD))
            {
                var joined = s.Add(o);
                if (!ReferenceEquals(joined, PyNotImplemented.Instance))
                    return joined;
                return Instance.ExecuteBinaryOpType(s, o, BinaryOpType.ADD);
            }
            if (IsSequence(s) && IsSequence(o))
            {
                var result = Instance.BinaryOp1(s, o, BinaryOpType.ADD);
                if (!ReferenceEquals(result, PyNotImplemented.Instance))
                    return result;
            }
            throw PyTypeError.Create($"'{s.GetTypeName()}' object can't be concatenated");
        }

        // unpack_iterable's test of a failed PyObject_GetIter: tp_iter NULL and no PySequence_Check.
        private static bool IsNonIterable(PyObject value) => !value.HasTypeSlot("__iter__") && !IsSequence(value);

        // PySequence_Check: __getitem__ and not a dict.
        internal static bool IsSequence(PyObject value) =>
            !(value is PyDict || value.GetPyType().IsSubclassOf(PyType.DictType)) && value.HasTypeSlot("__getitem__");

        // The tail of PyNumber_Add and PyNumber_Multiply, then binop_type_error.
        private PyObject SequenceOrTypeError(PyObject left, PyObject right, BinaryOpType operation, BinaryOpType shown)
        {
            if (operation == BinaryOpType.ADD && HasSequenceSlot(left, operation))
            {
                var joined = left.Add(right);
                if (!ReferenceEquals(joined, PyNotImplemented.Instance))
                    return joined;
                // PyUnicode_Concat, Objects/unicodeobject.c.
                if (IsStrLikeInstance(left))
                    throw PyTypeError.Create($"can only concatenate str (not \"{right.GetTypeName()}\") to str");
            }
            if (operation == BinaryOpType.MULTIPLY)
            {
                if (HasSequenceSlot(left, operation))
                    return SequenceRepeat(left, right);
                if (HasSequenceSlot(right, operation))
                    return SequenceRepeat(right, left);
            }
            // binary_op: print >> stream is pointed at the call it was in Python 2.
            if (shown == BinaryOpType.RSHIFT && left is PyBuiltinFunction { Name: "print" } && left.IsExactly(PyType.BuiltinFunctionOrMethodType))
                throw PyTypeError.Create(
                    $"unsupported operand type(s) for >>: '{left.GetTypeName()}' and '{right.GetTypeName()}'. Did you mean \"print(<message>, file=<output_stream>)\"?");
            throw PyTypeError.Create(
                $"unsupported operand type(s) for {GetBinaryOpSymbol(shown)}: '{left.GetTypeName()}' and '{right.GetTypeName()}'");
        }

        // sequence_repeat, Objects/abstract.c: _PyIndex_Check, then PyNumber_AsSsize_t with OverflowError.
        private static PyObject SequenceRepeat(PyObject sequence, PyObject count)
        {
            if (!PyObject.IndexCheck(count))
                throw PyTypeError.Create($"can't multiply sequence by non-int of type '{count.GetTypeName()}'");
            var native = sequence;
            // The type's own sq_repeat decides what the size means (OverflowError or MemoryError).
            return native.Multiply(PyInt.From(PyArgs.AsSsize(count, PyArgs.SsizeError.Overflow)));
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
                BinaryOpType.POWER => "** or pow()",
                BinaryOpType.INPLACE_ADD => "+=",
                BinaryOpType.INPLACE_SUBTRACT => "-=",
                BinaryOpType.INPLACE_MULTIPLY => "*=",
                BinaryOpType.INPLACE_TRUE_DIVIDE => "/=",
                BinaryOpType.INPLACE_FLOOR_DIVIDE => "//=",
                BinaryOpType.INPLACE_MODULO => "%=",
                BinaryOpType.INPLACE_POWER => "**=",
                BinaryOpType.INPLACE_LSHIFT => "<<=",
                BinaryOpType.INPLACE_RSHIFT => ">>=",
                BinaryOpType.INPLACE_AND => "&=",
                BinaryOpType.INPLACE_OR => "|=",
                BinaryOpType.INPLACE_XOR => "^=",
                BinaryOpType.INPLACE_MATRIX_MULTIPLY => "@=",
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

        /// <summary>Py_TPFLAGS_MATCH_SELF: case int(x) binds the subject itself.</summary>
        private static bool MatchesSelf(PyType type) =>
            type.IsSubclassOf(PyType.IntType) || type.IsSubclassOf(PyType.StrType) ||
            type.IsSubclassOf(PyType.ListType) || type.IsSubclassOf(PyType.TupleType) ||
            type.IsSubclassOf(PyType.DictType) || type.IsSubclassOf(PyType.SetType) ||
            type.IsSubclassOf(PyType.FrozenSetType) || type.IsSubclassOf(PyType.FloatType) ||
            type.IsSubclassOf(PyType.BytesType) || type.IsSubclassOf(PyType.BytearrayType);

        /// <summary>match_class_attr (Python/ceval.c): null when the attribute is missing.</summary>
        private static PyObject MatchClassAttribute(PyObject subject, PyType type, string name, HashSet<string> seen)
        {
            if (!seen.Add(name))
                throw PyTypeError.Create($"{type.Name}() got multiple sub-patterns for attribute '{name}'");
            try
            {
                return subject.GetAttribute(name);
            }
            catch (PythonException e) when (e.PyException is PyAttributeError)
            {
                return null;
            }
        }

        /// <summary>match_class (Python/ceval.c).</summary>
        private static PyObject MatchClass(PyObject subject, PyObject cls, int positional, PyTuple keywords)
        {
            if (cls is not PyType type)
                throw PyTypeError.Create("called match pattern must be a class");
            if (!SharpPy.Modules.BuiltinsModule.IsInstance(new[] { subject, cls }).IsTrue())
                return PyNone.Instance;

            var seen = new HashSet<string>();
            var attributes = new List<PyObject>(positional + keywords.Items.Length);
            if (positional > 0)
            {
                PyObject matchArgs = null;
                try
                {
                    matchArgs = cls.GetAttribute("__match_args__");
                }
                catch (PythonException e) when (e.PyException is PyAttributeError) { }

                if (matchArgs != null && matchArgs is not PyTuple)
                    throw PyTypeError.Create($"{type.Name}.__match_args__ must be a tuple (got {matchArgs.GetTypeName()})");
                bool matchSelf = matchArgs == null && MatchesSelf(type);
                var names = matchArgs as PyTuple;
                int allowed = matchSelf ? 1 : names?.Items.Length ?? 0;
                if (allowed < positional)
                    throw PyTypeError.Create(
                        $"{type.Name}() accepts {allowed} positional sub-pattern{(allowed == 1 ? "" : "s")} ({positional} given)");
                if (matchSelf)
                {
                    attributes.Add(subject);
                }
                else
                {
                    for (int i = 0; i < positional; i++)
                    {
                        if (names.Items[i] is not PyString name)
                            throw PyTypeError.Create(
                                $"__match_args__ elements must be strings (got {names.Items[i].GetTypeName()})");
                        var attribute = MatchClassAttribute(subject, type, name.Value, seen);
                        if (attribute == null) return PyNone.Instance;
                        attributes.Add(attribute);
                    }
                }
            }
            foreach (var keyword in keywords.Items)
            {
                var attribute = MatchClassAttribute(subject, type, ((PyString)keyword).Value, seen);
                if (attribute == null) return PyNone.Instance;
                attributes.Add(attribute);
            }
            return PyTuple.From(attributes.ToArray());
        }

        private static readonly PyObject MatchKeysMissing = PyTuple.From(Array.Empty<PyObject>());

        private static PyObject MatchKeys(PyObject subject, PyTuple keys)
        {
            var get = subject.GetAttribute("get");
            var seen = new PySet();
            var values = new PyObject[keys.Items.Length];
            for (int i = 0; i < values.Length; i++)
            {
                var key = keys.Items[i];
                if (seen.Contains(key).ToBool())
                    throw PyValueError.Create($"mapping pattern checks duplicate key ({key.ToRepr().Value})");
                seen.Add(key);
                values[i] = get.Call(new[] { key, MatchKeysMissing }, null);
                if (ReferenceEquals(values[i], MatchKeysMissing)) return PyNone.Instance;
            }
            return PyTuple.From(values);
        }

        private PyString ApplyFormatting(PyObject obj, string formatSpec) =>
            obj.Format(PyString.From(formatSpec));

        private PyObject CompareOperation(PyObject left, PyObject right, int compareOp)
        {
            // CPython 3.12는 바이트코드 값을 직접 사용:
            // 2=<, 26=<=, 40==, 55!=, 68=>, 92=>=
            // 더 이상 인덱스 기반 변환이 필요하지 않음

            var operation = (CompareOp)compareOp;

            // Special handling for IS_NOT (identity comparison)
            if (operation == CompareOp.IS_NOT)
                return IsNotOperation(left, right);

            var op = operation switch
            {
                CompareOp.EQ or CompareOp.EXC_MATCH => PyObject.CompareOp.EQ,
                CompareOp.NE => PyObject.CompareOp.NE,
                CompareOp.LT => PyObject.CompareOp.LT,
                CompareOp.LE => PyObject.CompareOp.LE,
                CompareOp.GT => PyObject.CompareOp.GT,
                CompareOp.GE => PyObject.CompareOp.GE,
                _ => throw PyNotImplementedError.Create($"Compare operation {compareOp} not implemented")
            };
            return PyObject.Compare(left, right, op);
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

                    {
                        PyObject displayhook = null;
                        if (PyImportSystem.TryGetModule("sys", out var sysModule))
                            displayhook = sysModule.LookupAttribute("displayhook");
                        if (displayhook == null)
                            throw PyRuntimeError.Create("lost sys.displayhook");
                        return displayhook.Call(new[] { arg }, null);
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
                        return PyTuple.From(list.Items);
                    }
                    throw PyTypeError.Create($"INTRINSIC_LIST_TO_TUPLE expected list, got {arg.GetTypeName()}");
                case (int)IntrinsicFunction.INTRINSIC_TYPEVAR:
                    return MakeTypeVar(arg, PyNone.Instance, PyNone.Instance);
                case (int)IntrinsicFunction.INTRINSIC_PARAMSPEC:
                    return PyImportSystem.Import("_typing").GetAttribute("ParamSpec").Call(new[] { arg }, InferVariance());
                case (int)IntrinsicFunction.INTRINSIC_TYPEVARTUPLE:
                    return MakeTypingObject("TypeVarTuple", arg);
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
            // import_all_from (Python/ceval.c): __all__, else the keys of __dict__ without the
            // underscored ones; each name is read with getattr and stored in the frame's locals.
            var frame = PyVM.CurrentFrame ?? throw new InvalidOperationException("No current frame during 'import *'");
            PyObject all = module.LookupAttribute("__all__");
            bool skipLeadingUnderscores = false;
            if (all == null)
            {
                var dict = module.LookupAttribute("__dict__")
                    ?? throw PyImportError.Create("from-import-* object has no __dict__ and no __all__");
                all = dict.GetAttribute("keys").Call(Array.Empty<PyObject>(), null);
                skipLeadingUnderscores = true;
            }
            foreach (var name in PySet.IterableItems(all))
            {
                if (name is not PyString text)
                {
                    var moduleName = module.GetAttribute("__name__");
                    if (moduleName is not PyString modName)
                        throw PyTypeError.Create($"module __name__ must be a string, not {moduleName.GetTypeName()}");
                    throw PyTypeError.Create(skipLeadingUnderscores
                        ? $"Key in {modName.Value}.__dict__ must be str, not {name.GetTypeName()}"
                        : $"Item in {modName.Value}.__all__ must be str, not {name.GetTypeName()}");
                }
                if (skipLeadingUnderscores && text.Value.StartsWith("_", StringComparison.Ordinal))
                    continue;
                var value = module.GetAttribute(text.Value);
                if (frame.ClassLocalsDict != null)
                    frame.ClassLocalsDict.SetItem(text, value);
                else
                    frame.ScopeChain.GlobalScope.SetVariable(text.Value, value);
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
            exc = exc;
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

        /// <summary>make_typevar (Python/intrinsics.c): a type parameter is the object typing.TypeVar makes.</summary>
        private static PyObject MakeTypeVar(PyObject name, PyObject evaluateBound, PyObject evaluateConstraints) =>
            PyImportSystem.Import("_typing").GetAttribute("TypeVar").GetAttribute("_from_type_param")
                .Call(new[] { name, evaluateBound, evaluateConstraints }, null);

        private static PyDict InferVariance()
        {
            var keywords = new PyDict();
            keywords.SetItem(PyString.From("infer_variance"), PyBool.True);
            return keywords;
        }

        private static PyObject MakeTypingObject(string type, PyObject name) =>
            PyImportSystem.Import("_typing").GetAttribute(type).Call(new[] { name }, null);

        /// <summary>_Py_subscript_generic (Objects/typevarobject.c): Generic[*params] made by typing.</summary>
        private PyObject CreateGenericSubscript(PyObject arg)
        {
            var typing = PyImportSystem.Import("typing");
            var parameters = new List<PyObject>();
            foreach (var parameter in ((PyTuple)arg).Items)
            {
                // unpack_typevartuples: Py_IS_TYPE(param, typevartuple_type) gives *param.
                if (ReferenceEquals(parameter.GetPyType(), PyImportSystem.Import("_typing").GetAttribute("TypeVarTuple")))
                    parameters.Add(parameter.GetIterator().Next());
                else parameters.Add(parameter);
            }
            return typing.GetAttribute("_generic_class_getitem").Call(
                new[] { typing.GetAttribute("Generic"), PyTuple.From(parameters.ToArray()) }, null);
        }

        // _Py_make_typealias
        private PyObject CreateTypeAlias(PyObject args) =>
            PyImportSystem.Import("_typing").GetAttribute("TypeAliasType").GetAttribute("_make").Call(((PyTuple)args).Items, null);

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
                    // CPython passes (orig, excs) to the intrinsic.
                    return PrepReraiseStar(arg1, arg2);
                case (int)IntrinsicFunction.INTRINSIC_TYPEVAR_WITH_BOUND:
                    return MakeTypeVar(arg1, arg2, PyNone.Instance);
                case (int)IntrinsicFunction.INTRINSIC_TYPEVAR_WITH_CONSTRAINTS:
                    return MakeTypeVar(arg1, PyNone.Instance, arg2);
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
                pyFunc.SetAttribute("__type_params__", paramTuple);
                return pyFunc;
            }

            return function; // Fallback: return function unchanged
        }

        /// <summary>
        /// CPython 3.12: INTRINSIC_PREP_RERAISE_STAR - prepare exceptions for reraise in except* handlers
        /// </summary>
        /// <summary>
        /// INTRINSIC_PREP_RERAISE_STAR - Objects/exceptions.c _PyExc_PrepReraiseStar(orig, excs).
        ///
        /// The except* chain left a list of what its clause bodies raised (None for a clause that
        /// finished without raising). Two kinds of entry appear: exceptions a handler re-raised,
        /// which keep the original's metadata and therefore its place in the nesting, and
        /// exceptions raised fresh, which become siblings next to the rebuilt original part.
        /// CPython names the wrapper group with an empty message; the old version of this method
        /// invented a group of all entries with the message "unhandled exceptions", which is
        /// observable in repr and in the traceback.
        /// </summary>
        private static PyObject PrepReraiseStar(PyObject orig, PyObject excs)
        {
            PyObject original = orig;

            PyObject[] entries = excs is PyList list
                ? list.Items
                : (excs is PyTuple excTuple ? excTuple.Items : new PyObject[] { excs });

            if (entries.Length == 0) return PyNone.Instance;

            var originalGroup = PyBaseExceptionGroup.Of(original);
            if (originalGroup == null)
            {
                // A naked exception was caught and wrapped, so at most one except* clause ran.
                foreach (PyObject entry in entries)
                    if (!(entry is PyNone)) return entry;
                return PyNone.Instance;
            }

            var raised = new List<PyObject>();
            var reraised = new List<PyObject>();
            foreach (PyObject entry in entries)
            {
                if (entry is PyNone) continue;
                if (PyBaseExceptionGroup.SameMetadata(entry, original)) reraised.Add(entry);
                else raised.Add(entry);
            }

            PyObject reraisedPart = originalGroup.Projection(reraised);
            if (raised.Count == 0) return reraisedPart;
            if (!(reraisedPart is PyNone)) raised.Add(reraisedPart);
            return raised.Count > 1
                ? PyBaseExceptionGroup.Create("", new PyList(raised))
                : raised[0];
        }

        /// <summary>
        /// CHECK_EG_MATCH - Python/ceval.c exception_group_match. Three cases, in this order:
        /// the exception itself matches (a group is taken as is, a naked exception is wrapped in
        /// ExceptionGroup("", [exc])); or it is a group that partially matches, and CPython calls
        /// the group's own split() - an ordinary attribute lookup, so a subclass can override it;
        /// or nothing matches and the exception lands in the rest part.
        /// The VM has no copy of the splitting logic any more.
        /// </summary>
        private static void ExceptionGroupMatch(PyObject excValue, PyObject matchType,
                                                out PyObject match, out PyObject rest)
        {

            // check_except_star_type_valid: match_type is a class or a tuple of classes that
            // inherit BaseException, otherwise "catching classes that do not inherit from
            // BaseException is not allowed" - before any matching is attempted.
            CheckExceptStarTypeValid(matchType);

            if (excValue is PyNone)
            {
                match = PyNone.Instance;
                rest = PyNone.Instance;
                return;
            }

            if (ExceptionMatchesHandler(excValue, matchType))
            {
                if (PyBaseExceptionGroup.Of(excValue) != null)
                {
                    match = excValue;
                }
                else
                {
                    // naked exception - wrap it, and give the wrapper the traceback of what is
                    // being handled, as CPython builds one from the current frame
                    PyObject wrapped = PyBaseExceptionGroup.Create(
                        "", PyTuple.From(new PyObject[] { excValue }));
                    if (excValue is PyBaseException naked &&
                        wrapped is PyBaseException wrapper)
                        wrapper.__traceback__ = naked.__traceback__;
                    match = wrapped;
                }
                rest = PyNone.Instance;
                return;
            }

            if (PyBaseExceptionGroup.Of(excValue) != null)
            {
                PyObject splitter = excValue.GetAttribute("split");
                PyObject pair = splitter.Call(new PyObject[] { matchType }, null);
                if (!(pair is PyTuple pairTuple))
                    throw PyTypeError.Create(
                        $"{excValue.GetTypeName()}.split must return a tuple, not {pair.GetTypeName()}");
                if (pairTuple.Items.Length < 2)
                    throw PyTypeError.Create(
                        $"{excValue.GetTypeName()}.split must return a 2-tuple, got tuple of size " +
                        $"{pairTuple.Items.Length}");
                match = pairTuple.Items[0];
                rest = pairTuple.Items[1];
                return;
            }

            match = PyNone.Instance;
            rest = excValue;
        }

        /// <summary>
        /// check_except_star_type_valid in Python/ceval.c.
        /// </summary>
        private static void CheckExceptStarTypeValid(PyObject matchType)
        {
            var classes = matchType is PyTuple tuple ? tuple.Items : new[] { matchType };
            foreach (var item in classes)
                if (item is not PyType type || !DerivesFromBaseException(type))
                    throw PyTypeError.Create("catching classes that do not inherit from BaseException is not allowed");
            foreach (var item in classes)
                if (((PyType)item).IsSubclassOf(PyType.BaseExceptionGroupType) || ReferenceEquals(item, PyType.BaseExceptionGroupType))
                    throw PyTypeError.Create("catching ExceptionGroup with except* is not allowed. Use except instead.");
        }

        /// <summary>
        /// CPython-style function argument binding with default parameters
        /// Used by MAKE_FUNCTION bytecode implementation
        /// </summary>

        /// <summary>
        /// CPython 3.12 GET_AWAITABLE 구현 - PEP 492 호환
        /// </summary>
        internal PyObject GetAwaitable(PyObject obj, int where = 0)
        {
            // _PyCoro_GetAwaitableIter: a coroutine is its own iterator.
            if (obj is SharpPy.Core.PyCoroutine || obj is PyGenerator { IsIterableCoroutine: true })
                return obj;
            var method = obj.LookupSpecialMethodUncached("__await__");
            if (method == null)
            {
                // format_awaitable_error
                if (where is 1 or 2)
                    throw PyTypeError.Create($"'async with' received an object from {(where == 1 ? "__aenter__" : "__aexit__")} that does not implement __await__: {obj.GetTypeName()}");
                throw PyTypeError.Create($"object {obj.GetTypeName()} can't be used in 'await' expression");
            }
            var result = obj.BindSpecialMethod(method).Call(Array.Empty<PyObject>(), null);
            if (result is SharpPy.Core.PyCoroutine || result is PyGenerator { IsIterableCoroutine: true })
                throw PyTypeError.Create("__await__() returned a coroutine");
            if (!result.IsIterator)
                throw PyTypeError.Create($"__await__() returned non-iterator of type '{result.GetTypeName()}'");
            return result;
        }

        // PyObject_GetAIter
        internal static PyObject Aiter(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"aiter() takes exactly one argument ({args.Length} given)");
            var method = args[0].LookupSpecialMethodUncached("__aiter__");
            if (method == null)
                throw PyTypeError.Create($"'{args[0].GetTypeName()}' object is not an async iterable");
            var iterator = args[0].BindSpecialMethod(method).Call(Array.Empty<PyObject>(), null);
            if (!iterator.HasTypeSlot("__anext__"))
                throw PyTypeError.Create($"aiter() returned not an async iterator of type '{iterator.GetTypeName()}'");
            return iterator;
        }

        // builtin_anext_impl
        internal static PyObject Anext(PyObject[] args)
        {
            if (args.Length is < 1 or > 2)
                throw PyTypeError.Create($"anext expected at {(args.Length < 1 ? "least 1 argument" : "most 2 arguments")}, got {args.Length}");
            var method = args[0].LookupSpecialMethodUncached("__anext__");
            if (method == null)
                throw PyTypeError.Create($"'{args[0].GetTypeName()}' object is not an async iterator");
            var awaitable = args[0].BindSpecialMethod(method).Call(Array.Empty<PyObject>(), null);
            return args.Length == 1 ? awaitable : new SharpPy.Core.PyAnextAwaitable(awaitable, args[1]);
        }

        private PyObject GetAsyncIterator(PyObject obj)
        {
            if (obj is SharpPy.Core.PyAsyncGenerator) return obj;
            var method = obj.LookupSpecialMethodUncached("__aiter__");
            if (method == null)
                throw PyTypeError.Create($"'async for' requires an object with __aiter__ method, got {obj.GetTypeName()}");
            var iterator = obj.BindSpecialMethod(method).Call(Array.Empty<PyObject>(), null);
            if (!iterator.HasTypeSlot("__anext__"))
                throw PyTypeError.Create($"'async for' received an object from __aiter__ that does not implement __anext__: {iterator.GetTypeName()}");
            return iterator;
        }

        private PyObject GetAsyncNext(PyObject iterator)
        {
            if (iterator is SharpPy.Core.PyAsyncGenerator generator) return generator.ANext();
            var method = iterator.LookupSpecialMethodUncached("__anext__");
            if (method == null)
                throw PyTypeError.Create($"'async for' requires an iterator with __anext__ method, got {iterator.GetTypeName()}");
            var next = iterator.BindSpecialMethod(method).Call(Array.Empty<PyObject>(), null);
            try { return GetAwaitable(next); }
            catch (PythonException error)
            {
                var exception = new PyTypeError($"'async for' received an invalid object from __anext__: {next.GetTypeName()}");
                exception.SetAttribute("__cause__", error.PyException);
                throw new PythonException(exception);
            }
        }

        /// <summary>
        /// Convert C# exception to Python exception for context manager __exit__ handling
        /// </summary>
        // Control flow of the host and syntax errors have their own routes.
        private static bool IsHostFailure(Exception exception) => exception is not (
            PyYieldException or PySyntaxErrorException or
            SyntaxErrorException or SharpPy.Generated.SyntaxErrorException);

        internal static PyBaseException ConvertToPythonException(Exception exception) => exception switch
        {
            PythonException python => python.PyException,
            InsufficientExecutionStackException => new PyRecursionError(),
            // PyErr_NoMemory: a MemoryError without a message.
            OutOfMemoryException => new PyMemoryError(),
            OverflowException => new PyOverflowError(exception.Message),
            DivideByZeroException => new PyZeroDivisionError(exception.Message),
            NotImplementedException => new PyNotImplementedError(exception.Message),
            // No Python code can cause these in CPython: an interpreter fault, as SystemError says.
            _ => new PySystemError($"{exception.GetType().Name}: {exception.Message}"),
        };

        /// <summary>
        /// CPython 3.12: PyTraceBack_Here - Add current frame to exception's traceback
        /// This is called each time an exception propagates through a frame without being handled.
        /// Corresponds to PyTraceBack_Here() in CPython traceback.c:266
        /// </summary>
        private void PyTraceBack_Here(PyFrame frame, PythonException pyEx)
        {
            // A re-raise reaches exception_unwind without the error label: no entry in the frame
            // that re-raised. Leaving it, the exception is an error of the caller like any other.
            if (pyEx.FromReraise)
            {
                pyEx.FromReraise = false;
                return;
            }

            var existingTraceback = pyEx.PyException.__traceback__;
            // CPython: next_instr-1 (ceval.c), one entry for each error in a frame.
            var lasti = frame.LastInstruction;

            // 3. _PyTraceBack_FromFrame: tb_lineno is PyFrame_GetLineNumber(frame), the line of lasti.
            int lineNo = PyFrame.LineAt(frame.Code, lasti);

            // 4. Create new traceback entry for current frame
            // CPython: newtb = _PyTraceBack_FromFrame(tb, frame)
            var newTraceback = new PyTraceback(
                frame: frame,
                // tb_lasti is a byte offset: _PyInterpreterFrame_LASTI(frame) * sizeof(_Py_CODEUNIT).
                lasti: lasti * 2,
                lineno: lineNo,
                next: existingTraceback  // Link to existing chain (prepend)
            );

            // 5. Attach new traceback to exception
            // CPython: PyException_SetTraceback(exc, newtb)
            pyEx.PyException.__traceback__ = newTraceback;

#if DEBUG_LOG
            Console.WriteLine($"🔍 PyTraceBack_Here: Added frame '{frame.Code.Name}' at line {newTraceback.LineNo} (lasti={lasti}, IP={frame.InstructionPointer})");
#endif
        }


        /// <summary>
        /// CALL with keyword names (Python/bytecodes.c CALL, PyObject_Vectorcall with kwnames): the
        /// last len(kwnames) arguments are the keywords, which a dict hands to the callable's tp_call.
        /// </summary>
        private static PyObject CallWithKeywords(PyObject callable, PyObject[] args, PyTuple kwNames)
        {
            int keywords = kwNames.Items.Length, positional = args.Length - keywords;
            var kwargs = new PyDict();
            for (int i = 0; i < keywords; i++)
                kwargs.SetItem(kwNames.Items[i], args[positional + i]);
            return callable.Call(args[..positional], kwargs);
        }


        /// <summary>
        /// Check if an exception is an instance of the expected exception type or its parent types
        /// </summary>
        // CPython 3.12: Python/errors.c:330-358 - PyErr_GivenExceptionMatches
        // Maps Python exception name to C# exception type for subtype checking

        // CPython 3.12: Python/ceval.c CHECK_EXC_MATCH -> PyObject_IsInstance.
        // The single rule for `except`, `except*` and ExceptionGroup.split():
        //   * a tuple handler matches when any element matches;
        //   * a handler that is not an exception class raises - even when the exception would
        //     otherwise escape the try statement;
        //   * otherwise the raised object's own type is walked through its MRO by reference.
        // Exception type identity is authoritative; comparing CLR class names or Python names is
        // what made two distinct classes with the same name catch each other.
        internal static bool ExceptionMatchesHandler(PyObject raised, PyObject handler)
        {
            if (handler is PyTuple tuple)
            {
                // check_except_type_valid: every item is a class, before any of them matches.
                foreach (var item in tuple.Items)
                    if (!(item is PyType candidate) || !DerivesFromBaseException(candidate))
                        throw PyTypeError.Create(
                            "catching classes that do not inherit from BaseException is not allowed");
                foreach (var item in tuple.Items)
                {
                    if (ExceptionMatchesHandler(raised, item))
                        return true;
                }
                return false;
            }

            if (!(handler is PyType expected) || !DerivesFromBaseException(expected))
                throw PyTypeError.Create(
                    "catching classes that do not inherit from BaseException is not allowed");

            var raisedType = raised?.GetPyType();
            if (raisedType == null)
                return false;

            var mro = raisedType.MRO;
            if (mro == null)
                return ReferenceEquals(raisedType, expected);
            for (int i = 0; i < mro.Count; i++)
            {
                if (ReferenceEquals(mro[i], expected))
                    return true;
            }
            return false;
        }

        // CPython 3.12: Objects/typeobject.c PyType_IsSubtype for `except` matching.
        // A raised exception class matches an `except` clause when the expected class
        // is the raised class itself OR appears anywhere in its MRO. Reference identity
        // is authoritative; name is a fallback only for identical names across modules.

        // PyExceptionClass_Check: a type whose MRO holds BaseException.
        private static bool DerivesFromBaseException(PyType? t) => t != null && t.IsSubclassOf(PyType.BaseExceptionType);

        // CPython 3.12: Python/errors.c:330-358 - PyErr_GivenExceptionMatches
        // Checks if exception instance is of expected type (using C# subtype checking)

        /// <summary>
        /// Check if an object can be treated as an exception-like object
        /// </summary>
        // _PyObject_LookupSpecial. A native class answers by name: its methods are in no type dict.
        private static PyObject WithMethod(PyObject manager, string name)
        {
            var method = manager.LookupSpecialMethodUncached(name);
            if (method != null)
                return manager.BindSpecialMethod(method);
            if (manager.GetPyType() is PyClass)
                return null;
            try
            {
                return manager.GetAttribute(name);
            }
            catch (PythonException error) when (error.PyException is PyAttributeError)
            {
                return null;
            }
        }

        private static bool IsExceptionClass(PyObject value) =>
            value is PyType type && DerivesFromBaseException(type);

        // do_raise (Python/ceval.c)
        private void DoRaise(PyFrame frame, PyObject exc, PyObject cause, PyBaseException handled)
        {
            PyObject value;
            if (IsExceptionClass(exc))
            {
                value = exc.Call(Array.Empty<PyObject>(), null);
                if (value is not PyBaseException)
                    throw PyTypeError.Create($"calling {exc.ToRepr().Value} should have returned an instance of BaseException, not {value.GetPyType().ToRepr().Value}");
            }
            else
            {
                value = exc;
            }
            if (value is not PyBaseException raised)
                throw PyTypeError.Create("exceptions must derive from BaseException");

            if (cause != null)
            {
                if (IsExceptionClass(cause))
                    cause = cause.Call(Array.Empty<PyObject>(), null);
                if (cause is PyBaseException causeException)
                    raised.__cause__ = causeException;
                else if (cause is PyNone)
                    raised.__cause__ = null;
                else
                    throw PyTypeError.Create("exception causes must derive from BaseException");
                raised.__suppress_context__ = true;
            }

            ChainContext(raised, handled);
            frame.LastException = raised;
            // The error label adds this frame to the traceback, as for any other error.
            throw new PythonException(raised);
        }

        /// <summary>
        /// _PyErr_SetObject for an exception a builtin raises into running code (gen_close's
        /// GeneratorExit): chained to the topmost handled exception, once.
        /// </summary>
        internal static PythonException SetObject(PyBaseException value)
        {
            ChainContext(value, GetTopmostException());
            return new PythonException(value);
        }

        // _PyErr_SetObject: implicit chaining, and no new cycle through the context chain.
        internal static void ChainContext(PyBaseException value, PyBaseException handled)
        {
            value.ImplicitContextDone = true;
            if (handled == null || ReferenceEquals(handled, value))
                return;
            var current = handled;
            var slow = current;
            bool advanceSlow = false;
            while (current.__context__ is { } context)
            {
                if (ReferenceEquals(context, value))
                {
                    current.__context__ = null;
                    break;
                }
                current = context;
                if (ReferenceEquals(current, slow))
                    break;
                if (advanceSlow)
                    slow = slow.__context__;
                advanceSlow = !advanceSlow;
            }
            value.__context__ = handled;
        }

    }

    #endregion
}
