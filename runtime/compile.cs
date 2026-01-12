using SharpPy.Tools;

#if GODOT
using IOHelper = Godot_IO.Helper;
#else
using IOHelper = DotNet_IO.Helper;
#endif

namespace SharpPy
{
    /// <summary>
    /// CPython 3.12: SyntaxError exception for compilation errors
    /// Python/compile.c: compiler_error()
    /// </summary>
    public class SyntaxErrorException : Exception
    {
        public string? FileName { get; }
        public int LineNumber { get; }
        public int ColumnOffset { get; }

        public SyntaxErrorException(string message, string? fileName, int lineNumber, int columnOffset)
            : base($"SyntaxError: {message} (file {fileName}, line {lineNumber}, column {columnOffset})")
        {
            FileName = fileName;
            LineNumber = lineNumber;
            ColumnOffset = columnOffset;
        }

        public SyntaxErrorException(string message) : base($"SyntaxError: {message}")
        {
        }
    }

    // CPython 3.12 호환: MAKE_FUNCTION 플래그 상수
    public static class MakeFunctionFlags
    {
        public const int DEFAULTS = 0x01;        // 기본값 있음
        public const int KWDEFAULTS = 0x02;      // 키워드 기본값 있음
        public const int ANNOTATIONS = 0x04;     // 타입 어노테이션 있음
        public const int CLOSURE = 0x08;         // 클로저 있음
    }
    
    #region Compiler Extension (AST → Bytecode)


    /// <summary>
    /// CPython-style free variable analyzer for closure detection
    /// </summary>
    public class FreeVariableAnalyzer
    {
        private readonly HashSet<string> _definedVars = new HashSet<string>();
        private readonly HashSet<string> _usedVars = new HashSet<string>();
        private readonly HashSet<string> _parameters = new HashSet<string>();
        private readonly HashSet<string> _globalVars = new HashSet<string>();
        
        /// <summary>
        /// Analyze function/lambda for free variables
        /// </summary>
        public (List<string> freeVars, List<string> cellVars) AnalyzeScope(Expression body, List<string> parameters)
        {
            _definedVars.Clear();
            _usedVars.Clear();
            _parameters.Clear();
            _globalVars.Clear();
            
            // Parameters are always defined in local scope
            foreach (var param in parameters)
            {
                _parameters.Add(param);
                _definedVars.Add(param);
            }
            
            // Analyze the body expression
            AnalyzeExpression(body);
            
            // Free variables: used but not defined locally
            // Performance: Eliminated LINQ
            var freeVars = new List<string>();
            foreach (var varName in _usedVars)
            {
                if (!_definedVars.Contains(varName))
                {
                    freeVars.Add(varName);
                }
            }
            
            // Cell variables: defined locally but referenced by nested functions
            var cellVars = new List<string>();
            
            // Phase 2: Find variables that need to be cells (referenced by nested lambdas)
            var cellNeededVars = FindCellNeededVars(body);
            foreach (var varName in cellNeededVars)
            {
                // Only parameters can be cell variables in lambda
                if (parameters.Contains(varName))
                {
                    cellVars.Add(varName);
                }
            }
            
            return (freeVars, cellVars);
        }
        
        /// <summary>
        /// Phase 2: Analyze nested function for free variables
        /// </summary>
        public (List<string> freeVars, List<string> cellVars) AnalyzeNestedFunction(FunctionDefStatement func, List<string> outerVarNames)
        {
            _definedVars.Clear();
            _usedVars.Clear();
            _parameters.Clear();
            _globalVars.Clear();
            
            // Function parameters are defined locally
            foreach (var param in func.Parameters)
            {
                _parameters.Add(param);
                _definedVars.Add(param);
            }
            
            // Analyze function body
            foreach (var statement in func.Body)
            {
                AnalyzeStatement(statement);
            }
            
            // CPython 3.12: Check for super() calls and add __class__ as free variable
#if DEBUG_LOG
            Console.WriteLine($"  🔍 Checking function {func.Name} for super() calls...");
#endif
            var hasSuperCalls = PythonCompiler.ContainsSuperCalls(func.Body);
#if DEBUG_LOG
            Console.WriteLine($"  🔍 Super calls detection result for {func.Name}: {hasSuperCalls}");
#endif
            if (hasSuperCalls && !_usedVars.Contains("__class__"))
            {
#if DEBUG_LOG
                Console.WriteLine($"  ✅ Found super() call in {func.Name}, adding __class__ as free variable");
#endif
                _usedVars.Add("__class__");
            }
            else if (hasSuperCalls)
            {
#if DEBUG_LOG
                Console.WriteLine($"  ⚠️ Super() calls found but __class__ already in _usedVars for {func.Name}");
#endif
            }
            
            // Free variables: used but not defined locally AND exist in outer scope (CPython 3.12 방식)
            // Only variables that exist in the outer scope can be free variables
            // Exclude global variables - they should use LOAD_GLOBAL, not LOAD_DEREF
            // Performance: Eliminated LINQ
            var freeVars = new List<string>();
            foreach (var varName in _usedVars)
            {
                if (!_definedVars.Contains(varName) && !_globalVars.Contains(varName) && outerVarNames.Contains(varName))
                {
                    freeVars.Add(varName);
                }
            }
            
            // CPython 3.12: __class__ is always available in class body scope
            if (hasSuperCalls && !freeVars.Contains("__class__"))
            {
                freeVars.Add("__class__");
            }
            
            // Cell variables: analyze nested functions to see what they reference
            var cellVars = new List<string>();
            foreach (var statement in func.Body)
            {
                if (statement is FunctionDefStatement nestedFunc)
                {
                    var nestedAnalyzer = new FreeVariableAnalyzer();
                    var (nestedFreeVars, _) = nestedAnalyzer.AnalyzeNestedFunction(nestedFunc, func.Parameters);
                    
                    // Any of our parameters that nested functions use as free variables become cells
                    foreach (var nestedFreeVar in nestedFreeVars)
                    {
                        if (func.Parameters.Contains(nestedFreeVar) && !cellVars.Contains(nestedFreeVar))
                        {
                            cellVars.Add(nestedFreeVar);
                        }
                    }
                }
            }
            
            return (freeVars, cellVars);
        }
        
        /// <summary>
        /// Async function 분석 - 일반 함수와 동일한 방식으로 자유 변수 분석
        /// </summary>
        public (List<string> freeVars, List<string> cellVars) AnalyzeAsyncFunction(AsyncFunctionDefStatement asyncFunc, List<string> outerVarNames)
        {
            _definedVars.Clear();
            _usedVars.Clear();
            _parameters.Clear();
            
            // Async function parameters are defined locally
            foreach (var param in asyncFunc.Parameters)
            {
                _parameters.Add(param);
                _definedVars.Add(param);
            }
            
            // Analyze the function body for variable usage
            foreach (var statement in asyncFunc.Body)
            {
                AnalyzeStatement(statement);
            }
            
            // Free variables: variables used but not defined locally, and exist in outer scope
            // Performance: Eliminated LINQ
            var freeVars = new List<string>();
            foreach (var varName in _usedVars)
            {
                if (!_definedVars.Contains(varName) && outerVarNames.Contains(varName))
                {
                    freeVars.Add(varName);
                }
            }
            
            // Cell variables: analyze nested functions to see what they reference
            var cellVars = new List<string>();
            foreach (var statement in asyncFunc.Body)
            {
                if (statement is FunctionDefStatement nestedFunc)
                {
                    var nestedAnalyzer = new FreeVariableAnalyzer();
                    var (nestedFreeVars, _) = nestedAnalyzer.AnalyzeNestedFunction(nestedFunc, asyncFunc.Parameters);
                    
                    // Any of our parameters that nested functions use as free variables become cells
                    foreach (var nestedFreeVar in nestedFreeVars)
                    {
                        if (asyncFunc.Parameters.Contains(nestedFreeVar) && !cellVars.Contains(nestedFreeVar))
                        {
                            cellVars.Add(nestedFreeVar);
                        }
                    }
                }
                else if (statement is AsyncFunctionDefStatement nestedAsyncFunc)
                {
                    var nestedAnalyzer = new FreeVariableAnalyzer();
                    var (nestedFreeVars, _) = nestedAnalyzer.AnalyzeAsyncFunction(nestedAsyncFunc, asyncFunc.Parameters);
                    
                    // Any of our parameters that nested async functions use as free variables become cells
                    foreach (var nestedFreeVar in nestedFreeVars)
                    {
                        if (asyncFunc.Parameters.Contains(nestedFreeVar) && !cellVars.Contains(nestedFreeVar))
                        {
                            cellVars.Add(nestedFreeVar);
                        }
                    }
                }
            }
            
            return (freeVars, cellVars);
        }
        
        private void AnalyzeStatement(Statement stmt)
        {
            switch (stmt)
            {
                case ExpressionStatement exprStmt:
                    AnalyzeExpression(exprStmt.Expression);
                    break;
                    
                case ReturnStatement retStmt:
                    if (retStmt.Value != null)
                        AnalyzeExpression(retStmt.Value);
                    break;
                    
                case AssignStatement assignStmt:
                    AnalyzeExpression(assignStmt.Value);
                    // CPython 3.12: Extract names from targets
                    foreach (var target in assignStmt.Targets)
                    {
                        if (target is NameExpression nameExpr)
                            _definedVars.Add(nameExpr.Name); // 새로 정의된 변수
                    }
                    break;
                    
                case NonlocalStatement nonlocalStmt:
                    // CPython 3.12: nonlocal 변수는 외부 스코프에서 참조함
                    // 로컬에서 정의되지 않았지만 사용될 수 있음을 표시
                    foreach (var name in nonlocalStmt.Names)
                    {
                        _usedVars.Add(name); // nonlocal 변수는 외부에서 가져옴
                        // _definedVars에는 추가하지 않음 (외부 스코프에 정의되어 있음)
                    }
                    break;

                case GlobalStatement globalStmt:
                    // CPython 3.12: global 변수는 전역 스코프에서 참조함
                    // 자유 변수로 분류되지 않도록 global 변수로 추적
                    foreach (var name in globalStmt.Names)
                    {
                        _globalVars.Add(name);
                    }
                    break;

                // TODO: 다른 statement 타입들 추가 가능
            }
        }
        
        private void AnalyzeExpression(Expression expr)
        {
            switch (expr)
            {
                case NameExpression name:
                    // Only add if it's not a keyword
                    if (!SharpPy.Generated.PyParser.IsKeyword(name.Name))
                    {
                        _usedVars.Add(name.Name);
                    }
                    break;
                    
                case BinOpExpression binary:
                    AnalyzeExpression(binary.Left);
                    AnalyzeExpression(binary.Right);
                    break;
                    
                case UnaryOpExpression unary:
                    AnalyzeExpression(unary.Operand);
                    break;
                    
                case CallExpression call:
                    AnalyzeExpression(call.Function);
                    foreach (var arg in call.Arguments)
                    {
                        AnalyzeExpression(arg);
                    }
                    break;
                    
                case ConditionalExpression cond:
                    AnalyzeExpression(cond.Test);
                    AnalyzeExpression(cond.Body);
                    AnalyzeExpression(cond.OrElse);
                    break;
                    
                case LambdaExpression lambda:
                    // Nested lambda - analyze its scope
                    var nestedAnalyzer = new FreeVariableAnalyzer();
                    var (nestedFreeVars, _) = nestedAnalyzer.AnalyzeScope(lambda.Body, lambda.Args);
                    
                    // Nested lambda's free variables are our used variables
                    foreach (var freeVar in nestedFreeVars)
                    {
                        _usedVars.Add(freeVar);
                    }
                    break;
                    
                case AttributeExpression attr:
                    AnalyzeExpression(attr.Value);
                    break;
                    
                case SubscriptExpression subscript:
                    AnalyzeExpression(subscript.Value);
                    AnalyzeExpression(subscript.Slice);
                    break;
                    
                case ListExpression list:
                    foreach (var item in list.Elements)
                    {
                        AnalyzeExpression(item);
                    }
                    break;
                    
                case TupleExpression tuple:
                    foreach (var item in tuple.Elements)
                    {
                        AnalyzeExpression(item);
                    }
                    break;
                    
                case DictExpression dict:
                    foreach (var (key, value) in dict.Items)
                    {
                        AnalyzeExpression(key);
                        AnalyzeExpression(value);
                    }
                    break;
                    
                case FStringExpression fstring:
                    if (fstring.Values != null)
                    {
                        foreach (var value in fstring.Values)
                        {
                            AnalyzeExpression(value);
                        }
                    }
                    break;
                    
                case FormattedValue formatted:
                    AnalyzeExpression(formatted.Value);
                    break;

                case WalrusExpression walrus:
                    // Analyze the value expression first
                    AnalyzeExpression(walrus.Value);
                    // Mark the target variable as defined (like normal assignment)
                    _definedVars.Add(walrus.Target);
                    break;

                case NamedExpression named:
                    // Analyze the value expression first
                    AnalyzeExpression(named.Value);
                    // Mark the target variable as defined (should be NameExpression)
                    if (named.Target is NameExpression nameTarget)
                    {
                        _definedVars.Add(nameTarget.Name);
                    }
                    break;

                // For ConstantExpression and other leaf expressions, no variables are used
                case ConstantExpression _:
                    break;

                // TODO: Add more expression types as needed
            }
        }
        
        /// <summary>
        /// Find variables that need to be cells (referenced by nested lambdas)
        /// </summary>
        private List<string> FindCellNeededVars(Expression body)
        {
            var cellNeeded = new List<string>();
            FindCellNeededVarsRecursive(body, cellNeeded);
            return cellNeeded;
        }
        
        private void FindCellNeededVarsRecursive(Expression expr, List<string> cellNeeded)
        {
            switch (expr)
            {
                case LambdaExpression lambda:
                    // This is a nested lambda - find what variables it uses from outer scope
                    var nestedAnalyzer = new FreeVariableAnalyzer();
                    var (nestedFreeVars, _) = nestedAnalyzer.AnalyzeScope(lambda.Body, lambda.Args);
                    
                    // Variables used by nested lambda from outer scope need to be cells
                    foreach (var freeVar in nestedFreeVars)
                    {
                        if (!cellNeeded.Contains(freeVar))
                        {
                            cellNeeded.Add(freeVar);
                        }
                    }
                    break;
                    
                case BinOpExpression binary:
                    FindCellNeededVarsRecursive(binary.Left, cellNeeded);
                    FindCellNeededVarsRecursive(binary.Right, cellNeeded);
                    break;
                    
                case UnaryOpExpression unary:
                    FindCellNeededVarsRecursive(unary.Operand, cellNeeded);
                    break;
                    
                case CallExpression call:
                    FindCellNeededVarsRecursive(call.Function, cellNeeded);
                    foreach (var arg in call.Arguments)
                    {
                        FindCellNeededVarsRecursive(arg, cellNeeded);
                    }
                    break;
                    
                case ConditionalExpression cond:
                    FindCellNeededVarsRecursive(cond.Test, cellNeeded);
                    FindCellNeededVarsRecursive(cond.Body, cellNeeded);
                    FindCellNeededVarsRecursive(cond.OrElse, cellNeeded);
                    break;
                    
                case AttributeExpression attr:
                    FindCellNeededVarsRecursive(attr.Value, cellNeeded);
                    break;
                    
                case SubscriptExpression subscript:
                    FindCellNeededVarsRecursive(subscript.Value, cellNeeded);
                    FindCellNeededVarsRecursive(subscript.Slice, cellNeeded);
                    break;
                    
                case ListExpression list:
                    foreach (var element in list.Elements)
                    {
                        FindCellNeededVarsRecursive(element, cellNeeded);
                    }
                    break;
                    
                case FStringExpression fstring:
                    if (fstring.Values != null)
                    {
                        foreach (var value in fstring.Values)
                        {
                            FindCellNeededVarsRecursive(value, cellNeeded);
                        }
                    }
                    break;
                    
                case FormattedValue formatted:
                    FindCellNeededVarsRecursive(formatted.Value, cellNeeded);
                    break;
                    
                // Leaf nodes don't need recursion
                case NameExpression _:
                case ConstantExpression _:
                    break;
                    
                // TODO: Add more expression types as needed
            }
        }

        // Note: HasSuperCalls methods removed - now using ContainsSuperCalls instead for consistency
    }

    // AST를 바이트코드로 컴파일 (기존 시스템과 연동)
    public class PythonCompiler
    {
        // CPython 3.12: Label-based intermediate representation (CFG pipeline)
        private InstructionSequence _instructionSequence;

        private List<PyObject> _constants;
        private List<string> _names;
        private List<string> _varNames;

        /// <summary>
        /// PythonCompiler constructor - ensures proper initialization
        /// </summary>
        public PythonCompiler()
        {
            // Initialize all essential lists to prevent null reference issues
            _constants = new List<PyObject>();
            _names = new List<string>();
            _varNames = new List<string>();
            _exceptionTable = new List<ExceptionTableEntry>();
            _lineNumberTable = new Dictionary<int, int>();

            // CPython 3.12: Label-based intermediate representation
            _instructionSequence = new InstructionSequence();
        }
        private bool _isInFunction = false; // Track if we're compiling inside a function
        private string _currentFunctionName = null; // Track current function name for module level detection
        private bool _isInComprehension = false; // Track if we're compiling inside a comprehension
        private int _comprehensionNestingDepth = 0; // Track nesting depth for dict comprehensions
        private bool _isInteractive = false; // CPython 3.12: Track if we're in interactive mode ('single' mode)
        private CompileMode _compileMode = CompileMode.File; // CPython 3.12: Track compile mode for add_return_at_end

        // CPython 3.12: Python/compile.c:2281-2361 (compiler_class)
        // Track if we're compiling inside a class body (not a method, just the class body itself)
        private bool _isInClassBody = false;
        
        // Source location tracking for bytecode generation
        private int _currentLineNumber = -1;     // Current line number being compiled
        private int _currentColumnOffset = -1;   // Current column offset being compiled
        private string? _currentFileName = null;  // Current source file name
        private List<string>? _sourceLines = null; // Source code lines for error reporting

        // CFG usage tracking
        private int _cfgPathCount = 0;
        private Dictionary<int, int> _lineNumberTable = new Dictionary<int, int>(); // instruction offset → line number mapping
        
        // Phase 2: 클로저 지원
        private List<string> _cellVars = new List<string>();
        private List<string> _freeVars = new List<string>();
        private List<ExceptionTableEntry> _exceptionTable = new List<ExceptionTableEntry>(); // CPython 3.12 Exception Table
        
        // CPython 3.12: 지연된 exception handler 생성 시스템
        private List<PendingExceptionHandler> _pendingExceptionHandlers = new List<PendingExceptionHandler>();

        // CPython 3.12: Exception handler stack (compile.c: compiler->u->u_except_stack)
        // Tracks active exception handlers during compilation (SETUP_FINALLY/CLEANUP push, POP_BLOCK pop)
        private Stack<Label> _exceptionHandlerStack = new Stack<Label>();

        // CPython 3.12: Exception handler info removed from InstructionSequence
        // Will be set later in flowgraph.cs during CFG building

        // CPython 3.12 Exception Handler 정보
        private class PendingExceptionHandler
        {
            public int StartOffset { get; set; }      // 보호 구간 시작 (바이트 오프셋)
            public int EndOffset { get; set; }        // 보호 구간 끝 (바이트 오프셋)  
            public List<string> ComprehensionVars { get; set; } = new List<string>(); // 정리할 변수들
            public int Depth { get; set; } = 2;       // 스택 depth
        }
        
        // CPython 3.12 호환: Nonlocal/Global 변수 추적
        private HashSet<string> _nonlocalVars = new HashSet<string>();
        private HashSet<string> _globalVars = new HashSet<string>();

        // 모듈 전역으로 선언된 global 변수들 (static으로 모든 컴파일러 인스턴스가 공유)
        private static HashSet<string> _moduleGlobalVars = new HashSet<string>();

        // 캐시된 builtin 변수 이름들 (성능 최적화 및 자동 동기화)
        private static HashSet<string> _builtinNames;

        // 정적 생성자: builtin 변수 이름들을 캐시
        static PythonCompiler()
        {
            _builtinNames = new HashSet<string>(PyBuiltinsModule.Instance.BuiltinDict.Keys);
        }

        // CPython 3.12 호환: Symbol Table 지원
        private SymbolTable? _symbolTable = null;
        private SymbolTable? _currentSymbolTable = null;
        private SymbolTableBuilder? _symbolTableBuilder = null;  // CPython 3.12: st->st_blocks lookup

        // CPython 3.12: Frame block stack for unified control flow tracking
        // Replaces both old _fblockStack and _loopStack
        private const int CO_MAXBLOCKS = 20;
        private Stack<FBlockInfo> _fblock = new Stack<FBlockInfo>();

        /// <summary>
        /// CPython 3.12: Source location information (Python/compile.c: location struct)
        /// </summary>
        private struct SourceLocation
        {
            public int Line { get; set; }
            public int Column { get; set; }

            public SourceLocation(int line, int column)
            {
                Line = line;
                Column = column;
            }

            public static readonly SourceLocation NoLocation = new SourceLocation(-1, -1);
        }

        /// <summary>
        /// CPython 3.12: Frame block types (Python/compile.c lines 130-133)
        /// Complete enum matching CPython 3.12 implementation
        /// </summary>
        private enum FBlockType
        {
            WHILE_LOOP,                         // while loop
            FOR_LOOP,                           // for/async for loop
            TRY_EXCEPT,                         // try/except block
            FINALLY_TRY,                        // try with finally
            FINALLY_END,                        // finally exception handler
            WITH,                               // with statement
            ASYNC_WITH,                         // async with statement
            HANDLER_CLEANUP,                    // exception handler cleanup
            POP_VALUE,                          // temporary value preservation
            EXCEPTION_HANDLER,                  // except clause body
            EXCEPTION_GROUP_HANDLER,            // except* clause body
            ASYNC_COMPREHENSION_GENERATOR,      // async comprehension iterator
            STOP_ITERATION                      // generator StopIteration wrapper
        }

        /// <summary>
        /// CPython 3.12: Frame block structure (Python/compile.c lines 135-143)
        /// Unified structure for loops, exception handlers, and cleanup
        /// Matches CPython's struct fblockinfo exactly
        /// </summary>
        private class FBlockInfo
        {
            public FBlockType Type { get; }         // Block type
            public Label Block { get; }             // continue target (fb_block)
            public SourceLocation Loc { get; }      // Source location for diagnostics
            public Label Exit { get; }              // break target (fb_exit)
            public object? Datum { get; }           // Type-specific data (fb_datum)
                                                    // - FINALLY_TRY: finalbody statements
                                                    // - HANDLER_CLEANUP: exception variable name
                                                    // - WITH/ASYNC_WITH: statement (for nested)
                                                    // - Most types: null

            public FBlockInfo(FBlockType type, Label block, SourceLocation loc, Label exit, object? datum = null)
            {
                Type = type;
                Block = block;
                Loc = loc;
                Exit = exit;
                Datum = datum;
            }
        }

        // ========== CPython 3.12: FBlock Operations (Python/compile.c) ==========

        /// <summary>
        /// Push a frame block onto the fblock stack
        /// CPython: compiler_push_fblock (Python/compile.c)
        /// </summary>
        private void PushFBlock(SourceLocation loc, FBlockType type, Label block, Label exit, object? datum = null)
        {
            if (_fblock.Count >= CO_MAXBLOCKS)
            {
                throw new InvalidOperationException("too many statically nested blocks");
            }
            _fblock.Push(new FBlockInfo(type, block, loc, exit, datum));
        }

        /// <summary>
        /// Pop a frame block from the fblock stack with type/label verification
        /// CPython: compiler_pop_fblock (Python/compile.c)
        /// </summary>
        private void PopFBlock(FBlockType expectedType, Label expectedBlock)
        {
            if (_fblock.Count == 0)
            {
                throw new InvalidOperationException("fblock stack underflow");
            }
            var info = _fblock.Pop();
            System.Diagnostics.Debug.Assert(info.Type == expectedType,
                $"FBlock type mismatch: expected {expectedType}, got {info.Type}");
            System.Diagnostics.Debug.Assert(info.Block == expectedBlock,
                $"FBlock block label mismatch");
        }

        /// <summary>
        /// Emit cleanup code when unwinding a single fblock
        /// CPython: compiler_unwind_fblock (Python/compile.c lines 1543-1641)
        /// </summary>
        private void UnwindFBlock(ref SourceLocation loc, FBlockInfo info, bool preserveTos)
        {
            switch (info.Type)
            {
                case FBlockType.WHILE_LOOP:
                case FBlockType.EXCEPTION_HANDLER:
                case FBlockType.EXCEPTION_GROUP_HANDLER:
                case FBlockType.ASYNC_COMPREHENSION_GENERATOR:
                case FBlockType.STOP_ITERATION:
                    // No cleanup needed
                    break;

                case FBlockType.FOR_LOOP:
                    // Pop the iterator from stack
                    if (preserveTos)
                    {
                        EmitInstruction(ByteCodeOp.SWAP, 2);
                    }
                    EmitInstruction(ByteCodeOp.POP_TOP);
                    break;

                case FBlockType.TRY_EXCEPT:
                    EmitInstruction(ByteCodeOp.POP_BLOCK);
                    break;

                case FBlockType.FINALLY_TRY:
                    // Execute finally block during unwind
                    EmitInstruction(ByteCodeOp.POP_BLOCK);
                    if (preserveTos)
                    {
                        PushFBlock(loc, FBlockType.POP_VALUE, Label.NoLabel, Label.NoLabel, null);
                    }
                    // Visit finalbody statements (stored in info.Datum)
                    if (info.Datum is List<Statement> finalbody)
                    {
                        foreach (var stmt in finalbody)
                        {
                            CompileStatement(stmt);
                        }
                    }
                    if (preserveTos)
                    {
                        PopFBlock(FBlockType.POP_VALUE, Label.NoLabel);
                    }
                    loc = SourceLocation.NoLocation;
                    break;

                case FBlockType.FINALLY_END:
                    if (preserveTos)
                    {
                        EmitInstruction(ByteCodeOp.SWAP, 2);
                    }
                    EmitInstruction(ByteCodeOp.POP_TOP); // exc_value
                    if (preserveTos)
                    {
                        EmitInstruction(ByteCodeOp.SWAP, 2);
                    }
                    EmitInstruction(ByteCodeOp.POP_BLOCK);
                    EmitInstruction(ByteCodeOp.POP_EXCEPT);
                    break;

                case FBlockType.WITH:
                case FBlockType.ASYNC_WITH:
                    loc = info.Loc;
                    EmitInstruction(ByteCodeOp.POP_BLOCK);
                    if (preserveTos)
                    {
                        EmitInstruction(ByteCodeOp.SWAP, 2);
                    }
                    // Call __exit__(None, None, None)
                    CompilerCallExitWithNones(loc);
                    if (info.Type == FBlockType.ASYNC_WITH)
                    {
                        EmitInstruction(ByteCodeOp.GET_AWAITABLE, 2);
                        EmitLoadConst(PyNone.Instance);
                        EmitInstruction(ByteCodeOp.SEND);
                    }
                    EmitInstruction(ByteCodeOp.POP_TOP);
                    loc = SourceLocation.NoLocation;
                    break;

                case FBlockType.HANDLER_CLEANUP:
                    if (info.Datum != null)
                    {
                        EmitInstruction(ByteCodeOp.POP_BLOCK);
                    }
                    if (preserveTos)
                    {
                        EmitInstruction(ByteCodeOp.SWAP, 2);
                    }
                    EmitInstruction(ByteCodeOp.POP_BLOCK);
                    EmitInstruction(ByteCodeOp.POP_EXCEPT);
                    if (info.Datum is string exceptionVar)
                    {
                        // Clean up exception variable: var = None; del var
                        // CPython: compiler_nameop(c, NO_LOCATION, name, Store); compiler_nameop(c, NO_LOCATION, name, Del);
                        EmitLoadConst(PyNone.Instance);
                        EmitStoreName(exceptionVar);
                        EmitDeleteName(exceptionVar);
                    }
                    break;

                case FBlockType.POP_VALUE:
                    if (preserveTos)
                    {
                        EmitInstruction(ByteCodeOp.SWAP, 2);
                    }
                    EmitInstruction(ByteCodeOp.POP_TOP);
                    break;

                default:
                    throw new NotImplementedException($"Unwind not implemented for FBlockType.{info.Type}");
            }
        }

        /// <summary>
        /// Recursively unwind fblocks until reaching a loop (for break/continue) or all blocks (for return)
        /// CPython: compiler_unwind_fblock_stack (Python/compile.c lines 1645-1667)
        /// </summary>
        /// <param name="loc">Source location (ref - may be modified during unwinding)</param>
        /// <param name="preserveTos">Whether to preserve top-of-stack value</param>
        /// <param name="findLoop">If true, stop at first loop block; if false, unwind all blocks</param>
        /// <returns>The loop block if findLoop is true and a loop was found; otherwise null</returns>
        private FBlockInfo? UnwindFBlockStack(ref SourceLocation loc, bool preserveTos, bool findLoop)
        {
            if (_fblock.Count == 0)
            {
                return null;
            }

            var top = _fblock.Peek();

            // Error: break/continue/return in except*
            if (top.Type == FBlockType.EXCEPTION_GROUP_HANDLER)
            {
                throw new SyntaxErrorException(
                    "'break', 'continue' and 'return' cannot appear in an except* block",
                    _currentFileName, loc.Line, loc.Column);
            }

            // Stop unwinding if we found a loop (for break/continue)
            if (findLoop && (top.Type == FBlockType.WHILE_LOOP || top.Type == FBlockType.FOR_LOOP))
            {
                return top;
            }

            // Unwind this block (temporarily pop it)
            var copy = _fblock.Pop();
            UnwindFBlock(ref loc, copy, preserveTos);

            // Recursively unwind remaining blocks
            var loop = UnwindFBlockStack(ref loc, preserveTos, findLoop);

            // Restore block (important for preserving fblock state for later PopFBlock calls)
            _fblock.Push(copy);

            return loop;
        }

        /// <summary>
        /// Helper method to call __exit__(None, None, None) for WITH statement cleanup
        /// CPython: compiler_call_exit_with_nones (Python/compile.c)
        /// </summary>
        private void CompilerCallExitWithNones(SourceLocation loc)
        {
            // CPython compile.c:1487-1494 compiler_call_exit_with_nones
            // Stack: [... context_manager]  (after SWAP 2 if preserveTos=true)
            // Push 3 Nones for __exit__(exc_type, exc_val, exc_tb)
            EmitLoadConst(PyNone.Instance);  // exc_type
            EmitLoadConst(PyNone.Instance);  // exc_value
            EmitLoadConst(PyNone.Instance);  // exc_tb
            // Call: context_manager.__exit__(None, None, None)
            // CALL 2: means 2 arguments (not counting the callable itself)
            // Stack before CALL: [... context_manager, None, None, None]
            // Stack after CALL: [... return_value]
            EmitInstruction(ByteCodeOp.CALL, 2);
        }

        // ========== End of FBlock Operations ==========

        /// <summary>
        /// CPython 3.12: 심볼 테이블 컨텍스트를 설정 (중첩 함수 컴파일용)
        /// </summary>
        public void SetSymbolTableContext(SymbolTable symbolTable)
        {
            _currentSymbolTable = symbolTable;
#if DEBUG_LOG
            Console.WriteLine($"  📥 Symbol table context set: {symbolTable?.Name}");
#endif
        }

        /// <summary>
        /// CPython 3.12: 루트 심볼 테이블을 설정 (중첩 함수 컴파일용)
        /// </summary>
        public void SetRootSymbolTable(SymbolTable rootSymbolTable)
        {
            _symbolTable = rootSymbolTable;
#if DEBUG_LOG
            Console.WriteLine($"  📥 Root symbol table set: {rootSymbolTable?.Name}");
#endif
        }

        /// <summary>
        /// CPython 3.12: 중첩 함수들이 필요로 하는 자유 변수를 수집 (현재 함수에서 정의되지 않은 것만)
        /// </summary>
        private List<string> CollectNestedFreeVariables(SymbolTable functionTable)
        {
            var nestedVars = new List<string>();

            // 현재 함수의 로컬 심볼들 수집 (셀 변수와 로컬 변수)
            var currentLocalVars = new HashSet<string>();
            foreach (var symbol in functionTable.GetSymbols().Values)
            {
                if (symbol.IsAssigned() || symbol.IsCell())
                {
                    currentLocalVars.Add(symbol.Name);
                }
            }

            // 모든 하위 함수들의 자유 변수를 재귀적으로 수집
            CollectNestedFreeVariablesRecursive(functionTable, currentLocalVars, nestedVars);

#if DEBUG_LOG
            Console.WriteLine($"  🔄 Collected nested free vars for {functionTable.GetName()}: [{string.Join(", ", nestedVars)}]");
#endif
            return nestedVars;
        }

        private void CollectNestedFreeVariablesRecursive(SymbolTable table, HashSet<string> currentLocalVars, List<string> nestedVars)
        {
            foreach (var child in table.GetChildren())
            {
                // CPython 3.12 PEP 709: Inlined comprehensions do NOT need closure variables
                // Skip comprehension scopes - they're inlined and share the parent's locals
                // NOTE: Generator expressions are NOT inlined - they still need closure vars
                if (child.IsInlinedComprehension)
                {
#if DEBUG_LOG
                    Console.WriteLine($"    ⏭️ Skipping PEP 709 inlined comprehension: {child.GetName()}");
#endif
                    continue;
                }

                // 자식 함수의 자유 변수들 중 현재 함수에서 정의된 것들만 (현재 함수가 제공할 수 있는 변수들)
                var childFreeVars = child.FindFreeVariables();
#if DEBUG_LOG
                Console.WriteLine($"    🔍 Checking child {child.GetName()} free vars: [{string.Join(", ", childFreeVars)}]");
#endif
                foreach (var freeVar in childFreeVars)
                {
                    // 현재 함수에서 실제로 할당/정의된 변수인지 Symbol Table에서 확인
                    var symbol = table.Lookup(freeVar);
#if DEBUG_LOG
                    Console.WriteLine($"      → Checking {freeVar} in {table.GetName()}: found={symbol != null}");
                    if (symbol != null)
                    {
                        Console.WriteLine($"        Symbol scope: {symbol.Scope}, assigned: {symbol.IsAssigned()}, flags: {symbol.Flags}");
                    }
#endif
                    if (symbol != null && symbol.IsAssigned() &&
                        (symbol.Scope == SymbolScope.Local || symbol.Scope == SymbolScope.Cell) &&
                        !nestedVars.Contains(freeVar))
                    {
                        nestedVars.Add(freeVar);
#if DEBUG_LOG
                        Console.WriteLine($"    → ✅ Found nested free var: {freeVar} (from {child.GetName()}) - assigned in current scope");
#endif
                    }
                    else if (symbol != null)
                    {
#if DEBUG_LOG
                        Console.WriteLine($"    → ❌ Skipping {freeVar} - scope: {symbol.Scope}, assigned: {symbol.IsAssigned()} - not owner");
#endif
                    }
                    else
                    {
#if DEBUG_LOG
                        Console.WriteLine($"    → ❓ {freeVar} not found in {table.GetName()}");
#endif
                    }
                }

                // 재귀적으로 손자 함수들도 확인 (단, 직접 할당된 변수만)
                // NOTE: 손자의 자유변수를 현재 함수에서 수집하지 않음 - 중간 함수가 제공해야 함
                // CollectNestedFreeVariablesRecursive(child, currentLocalVars, nestedVars);
            }
        }
        
        /// <summary>
        /// Pre-scan all function definitions to collect global variable declarations
        /// This ensures module-level assignments use STORE_GLOBAL for variables declared as global
        /// </summary>
        private void PreScanForGlobalVariables(List<Statement> statements)
        {
            foreach (var stmt in statements)
            {
                PreScanStatement(stmt);
            }
            
            if (_moduleGlobalVars.Count > 0)
            {
#if DEBUG_LOG
                Console.WriteLine($"🔍 Pre-scan found global variables: {string.Join(", ", _moduleGlobalVars)}");
#endif
            }
        }

        /// <summary>
        /// CPython 3.12: Symbol table lookup helper
        /// </summary>
        private SymbolTable? FindSymbolTableByName(SymbolTable rootTable, string name)
        {
            #if DEBUG_LOG
            Console.WriteLine($"🔍 FindSymbolTableByName: Searching for '{name}' in table '{rootTable.GetName()}' (type: {rootTable.GetType()})");
            #endif

            // Check current table
            if (rootTable.GetName() == name)
            {
                #if DEBUG_LOG
                Console.WriteLine($"✅ Found matching table: {name}");
                #endif
                return rootTable;
            }

            // Search children recursively
            foreach (var child in rootTable.GetChildren())
            {
                #if DEBUG_LOG
                Console.WriteLine($"🔍 Checking child table: '{child.GetName()}' (type: {child.GetType()})");
                #endif
                var found = FindSymbolTableByName(child, name);
                if (found != null)
                {
                    return found;
                }
            }

            #if DEBUG_LOG
            Console.WriteLine($"❌ Table '{name}' not found in '{rootTable.GetName()}'");
            #endif
            return null;
        }

        private void PreScanStatement(Statement stmt)
        {
            switch (stmt)
            {
                case FunctionDefStatement funcStmt:
                    PreScanFunction(funcStmt.Body);
                    break;
                case AsyncFunctionDefStatement asyncFuncStmt:
                    PreScanFunction(asyncFuncStmt.Body);
                    break;
                case ClassDefStatement classStmt:
                    PreScanFunction(classStmt.Body);
                    break;
                case IfStatement ifStmt:
                    PreScanFunction(ifStmt.Body);
                    if (ifStmt.OrElse != null && ifStmt.OrElse.Count > 0)
                        PreScanFunction(ifStmt.OrElse);
                    break;
                case ForStatement forStmt:
                    PreScanFunction(forStmt.Body);
                    if (forStmt.ElseClause != null && forStmt.ElseClause.Count > 0)
                        PreScanFunction(forStmt.ElseClause);
                    break;
                case WhileStatement whileStmt:
                    PreScanFunction(whileStmt.Body);
                    if (whileStmt.ElseClause != null && whileStmt.ElseClause.Count > 0)
                        PreScanFunction(whileStmt.ElseClause);
                    break;
                case TryStatement tryStmt:
                    PreScanFunction(tryStmt.Body);
                    foreach (var handler in tryStmt.Handlers)
                        PreScanFunction(handler.Body);
                    if (tryStmt.OrElse != null && tryStmt.OrElse.Count > 0)
                        PreScanFunction(tryStmt.OrElse);
                    if (tryStmt.FinalBody != null && tryStmt.FinalBody.Count > 0)
                        PreScanFunction(tryStmt.FinalBody);
                    break;

                case TryStarStatement tryStarStmt:
                    PreScanFunction(tryStarStmt.Body);
                    foreach (var handler in tryStarStmt.Handlers)
                        PreScanFunction(handler.Body);
                    if (tryStarStmt.OrElse != null && tryStarStmt.OrElse.Count > 0)
                        PreScanFunction(tryStarStmt.OrElse);
                    if (tryStarStmt.FinalBody != null && tryStarStmt.FinalBody.Count > 0)
                        PreScanFunction(tryStarStmt.FinalBody);
                    break;
                case WithStatement withStmt:
                    PreScanFunction(withStmt.Body);
                    break;
            }
        }
        
        private void PreScanFunction(List<Statement> statements)
        {
            foreach (var stmt in statements)
            {
                if (stmt is GlobalStatement globalStmt)
                {
                    foreach (var name in globalStmt.Names)
                    {
                        _moduleGlobalVars.Add(name);
                    }
                }
                else
                {
                    PreScanStatement(stmt);
                }
            }
        }

        /// <summary>
        /// CPython: _PyAST_GetDocString in Python/ast.c:1094-1108
        /// Get the docstring from a sequence of statements if present.
        /// Returns the docstring value or null if not found.
        /// </summary>
        private string? GetDocString(List<Statement> statements)
        {
            // CPython: if (!asdl_seq_LEN(body)) return NULL;
            if (statements == null || statements.Count == 0)
            {
                return null;
            }

            // CPython: if (st->kind != Expr_kind) return NULL;
            var firstStmt = statements[0];
            if (!(firstStmt is ExpressionStatement exprStmt))
            {
                return null;
            }

            // CPython: if (e->kind == Constant_kind && PyUnicode_CheckExact(e->v.Constant.value))
            if (exprStmt.Expression is ConstantExpression constExpr &&
                constExpr.Value is PyString pyStr)
            {
                return pyStr.Value;
            }

            return null;
        }

        public PyCodeObject Compile(List<Statement> statements, string name, List<string> parameters, string? fileName = null, CompileMode mode = CompileMode.File)
        {
            // Clear all compilation state for new compilation
            // CPython 3.12: Each compilation starts with fresh state (compile.c:compiler_init)
            _constants.Clear();
            _names.Clear();
            _varNames.Clear();
            _exceptionTable.Clear(); // Reset Exception Table
            _lineNumberTable.Clear(); // Reset line number table
            _cellVars.Clear(); // Reset cell variables
            _freeVars.Clear(); // Reset free variables
            _pendingExceptionHandlers.Clear(); // Reset pending exception handlers
            _exceptionHandlerStack.Clear(); // Reset exception handler stack

            // CPython 3.12: Reset InstructionSequence for new compilation (compile.c:compiler_init)
            // Reference: Python/compile.c:856-860 (compiler_init clears instruction lists)
            _instructionSequence = new InstructionSequence();

            // Set current file name for source location tracking
            _currentFileName = fileName;

            // CPython 3.12: Set interactive mode flag
            // Python/pythonrun.c:266 - REPL uses Py_single_input
            // Include/compile.h:8 - Py_single_input = 256
            _isInteractive = (mode == CompileMode.Single);

            // CPython 3.12: Store compile mode for add_return_at_end logic
            // Python/compile.c:1749 - addNone = mod->kind != Expression_kind
            _compileMode = mode;

            // CPython 3.12: Build symbol table first
            var symbolTableBuilder = new SymbolTableBuilder();
            _symbolTable = symbolTableBuilder.BuildSymbolTable(statements, name);
            _currentSymbolTable = _symbolTable;
            _symbolTableBuilder = symbolTableBuilder;  // CPython 3.12: Store for PySymtable_Lookup

#if DEBUG_LOG
            Console.WriteLine($"🔍 Symbol table built for {name}: {_symbolTable.GetIdentifiers().Count()} symbols");
            foreach (var symbolName in _symbolTable.GetIdentifiers())
            {
                var symbol = _symbolTable.Lookup(symbolName);
#if DEBUG_LOG
                Console.WriteLine($"  {symbolName}: {symbol?.Scope} scope, flags: {symbol?.Flags}");
#endif
            }
#endif

            // Phase 1: AST 수준 최적화 (CPython 3.12 스타일, 항상 활성화)
            var astOptimizer = new PyASTOptimizer(GetOptimizationLevel());
            var optimizedStatements = astOptimizer.OptimizeAST(statements);
            
            // Pre-scan for global variables in functions (CPython 3.12 compatibility)
            if (name == "<module>")
            {
                _moduleGlobalVars.Clear(); // 새로운 모듈 컴파일 시작
                PreScanForGlobalVariables(optimizedStatements);
            }
            
            // Load source lines for error reporting if fileName is provided
            _sourceLines = null;
            if (!string.IsNullOrEmpty(fileName) && IOHelper.FileExists(fileName))
            {
                try
                {
                    // Performance: Eliminated LINQ
                    _sourceLines = new List<string>(IOHelper.ReadAllLines(fileName));
                }
                catch (Exception ex)
                {
                    // If we can't read the file, just continue without source lines
#if DEBUG_LOG
                    Console.WriteLine($"Warning: Could not read source file {fileName}: {ex.Message}");
#endif
                }
            }
            
            // 함수 매개변수를 _varNames에 추가 (LOAD_FAST/STORE_FAST용)
            foreach (var param in parameters)
            {
                _varNames.Add(param);
            }
            
            if (!SharpPyConfig.DisassemblyOnlyMode)
            {
#if DEBUG_LOG
                Console.WriteLine($"\n🔧 컴파일: {name}");
#endif
            }
            
            // Python 3.12: 모든 코드는 RESUME으로 시작 (line 0)
            _currentLineNumber = 0;
            EmitInstruction(ByteCodeOp.RESUME, 0);

            // CPython 3.12: SETUP_ANNOTATIONS once if module has any annotations
            // Performance: Eliminated LINQ
            bool hasAnnotations = false;
            foreach (var stmt in optimizedStatements)
            {
                if (stmt is AnnAssignStatement)
                {
                    hasAnnotations = true;
                    break;
                }
            }
            if (hasAnnotations)
            {
                EmitInstruction(ByteCodeOp.SETUP_ANNOTATIONS);
            }

            // CPython 3.12: Handle docstring (compile.c:1694-1704)
            // if not -OO mode, set docstring
            // BUT: In interactive mode, don't treat first string as docstring
            int startIndex = 0;
            if (name == "<module>" && !_isInteractive)
            {
                string? docstring = GetDocString(optimizedStatements);
                if (docstring != null)
                {
                    // CPython: VISIT(c, expr, st->v.Expr.value);
                    // Load docstring constant
                    var docConstIndex = GetOrAddConstant(new PyString(docstring));
                    EmitInstruction(ByteCodeOp.LOAD_CONST, docConstIndex);

                    // CPython: compiler_nameop(c, NO_LOCATION, &_Py_ID(__doc__), Store);
                    // Store in __doc__
                    EmitStoreName("__doc__");

                    // Skip first statement (it's the docstring)
                    startIndex = 1;
                }
            }

            // Compile remaining statements
            for (int i = startIndex; i < optimizedStatements.Count; i++)
            {
                CompileStatement(optimizedStatements[i]);
            }

            // CPython 3.12: add_return_at_end (Python/compile.c:7671-7681)
            // addNone = mod->kind != Expression_kind
            // - Eval mode (Expression_kind): RETURN_VALUE (return stack top, i.e., expression result)
            // - Exec/Single mode: LOAD_CONST None + RETURN_VALUE
            bool addNone = (_compileMode != CompileMode.Eval);
            if (addNone)
            {
                var noneConstIndex = GetOrAddConstant(PyNone.Instance);
                EmitInstruction(ByteCodeOp.LOAD_CONST, noneConstIndex);
            }
            EmitInstruction(ByteCodeOp.RETURN_VALUE);

            // CPython 3.12: 지연된 exception handler들을 바이트코드 끝에 생성
            GeneratePendingExceptionHandlers();

            // CPython 3.12: Get final instructions (CFG pipeline: InstructionSequence → CFG → Optimize → Assemble)
            // Exception table is built automatically in PyAssemble.Assemble()
            // Module-level code has no generator flags
            var finalInstructions = GetFinalInstructions(0);

            var codeObject = new PyCodeObject(name, finalInstructions, _constants, _names, _varNames, parameters.Count, 0, 0, null, null, null, null, 0, _currentFileName, _sourceLines, false, _lineNumberTable);

            // Add Exception Table entries (CFG pipeline sets _exceptionTable via GetFinalInstructions)
            if (_exceptionTable.Count > 0)
            {
                codeObject.ExceptionTable.AddRange(_exceptionTable);
                if (!SharpPyConfig.DisassemblyOnlyMode)
                {
#if DEBUG_LOG
                    Console.WriteLine($"📋 Exception Table: {_exceptionTable.Count}개 엔트리 추가됨 (라벨 해석 완료)");
#endif
                }
            }
            else
            {
                if (!SharpPyConfig.DisassemblyOnlyMode)
                {
#if DEBUG_LOG
                    Console.WriteLine($"📋 Exception Table: 비어있음 (CPython 3.12 compatible)");
#endif
                }
            }
            if (!SharpPyConfig.DisassemblyOnlyMode)
            {
                if (!SharpPyConfig.DisassemblyOnlyMode)
            {
    #if DEBUG_LOG
            // CPython 3.12: Instruction count now tracked in CFG pipeline
#endif
            }

#if DEBUG_COMPILER_LOG
                // Print compilation statistics
                Console.WriteLine($"");
                Console.WriteLine($"📊 [COMPILATION SUMMARY]");
                Console.WriteLine($"   CFG constructs: {_cfgPathCount}");
                Console.WriteLine($"");
#endif
            }

            return codeObject;
        }

        /// <summary>
        /// Phase 2: 클로저 정보를 포함한 컴파일
        /// </summary>
        public PyCodeObject CompileWithClosure(List<Statement> statements, string name, List<string> parameters,
                                             List<string> freeVars, List<string> cellVars)
        {
            // Clear all compilation state for new compilation
            // CPython 3.12: _instructions removed - using InstructionSequence only
            _constants.Clear();
            _names.Clear();
            _varNames.Clear();
            _exceptionTable.Clear(); // Reset Exception Table
            _lineNumberTable.Clear(); // Reset line number table
            
            // 함수 매개변수를 _varNames에 추가 (LOAD_FAST/STORE_FAST용)
            foreach (var param in parameters)
            {
                _varNames.Add(param);
            }
            
            if (!SharpPyConfig.DisassemblyOnlyMode)
            {
#if DEBUG_LOG
                Console.WriteLine($"\n🔧 컴파일 (클로저): {name}");
#endif
            }
            if (!SharpPyConfig.DisassemblyOnlyMode)
            {
    #if DEBUG_LOG
            Console.WriteLine($"  FreeVars: [{string.Join(", ", freeVars)}]");
#endif
            }
            if (!SharpPyConfig.DisassemblyOnlyMode)
            {
    #if DEBUG_LOG
            Console.WriteLine($"  CellVars: [{string.Join(", ", cellVars)}]");
#endif
            }
            
            // Phase 2: Cell 변수들을 위한 MAKE_CELL 명령어 발행
            // CPython 3.12: MAKE_CELL argument is the localsplus index (varnames index for parameters)
            // NOT the cellvars index!
            // Example: wraps(wrapped, assigned, updated)
            //   varnames: ['wrapped', 'assigned', 'updated', 'decorator']
            //   cellvars: ['assigned', 'updated', 'wrapped'] (alphabetically sorted)
            //   MAKE_CELL 0 (wrapped) → varnames[0]
            //   MAKE_CELL 1 (assigned) → varnames[1]
            //   MAKE_CELL 2 (updated) → varnames[2]
            for (int cellIndex = 0; cellIndex < cellVars.Count; cellIndex++)
            {
                var cellVar = cellVars[cellIndex];
                if (parameters.Contains(cellVar))
                {
                    // Get the varnames index (localsplus offset) for this parameter
                    var localsPlusOffset = parameters.IndexOf(cellVar);
                    if (!SharpPyConfig.DisassemblyOnlyMode)
                    {
#if DEBUG_LOG
                        Console.WriteLine($"  → Making cell for parameter: {cellVar} (varnames index {localsPlusOffset}, cellvars index {cellIndex})");
#endif
                    }
                    EmitInstruction(ByteCodeOp.MAKE_CELL, localsPlusOffset);
                }
            }
            
            foreach (var statement in statements)
            {
                CompileStatement(statement);
            }
            
            // CPython 3.12: 모듈은 RETURN_CONST로 None 반환
            var noneConstIndex = GetOrAddConstant(PyNone.Instance);
            EmitInstruction(ByteCodeOp.RETURN_CONST, noneConstIndex);

            // Comprehensions don't have generator flags
            var finalInstructions = GetFinalInstructions(0);
            var codeObject = new PyCodeObject(name, finalInstructions, _constants, _names, _varNames,
                                            parameters.Count, 0, 0, freeVars, cellVars, null, null, 0, _currentFileName, _sourceLines);
            
            // Add Exception Table entries (CPython 3.12)
            codeObject.ExceptionTable.AddRange(_exceptionTable);
            if (!SharpPyConfig.DisassemblyOnlyMode)
            {
#if DEBUG_LOG
                // CPython 3.12: Instruction count tracked in CFG pipeline
#endif
            }

            return codeObject;
        }

        /// <summary>
        /// CPython 3.12: Extract type name from annotation Expression
        /// Handles NameExpression, AttributeExpression, and SubscriptExpression
        /// </summary>
        private string ExtractAnnotationName(Expression annotation)
        {
            switch (annotation)
            {
                case NameExpression nameExpr:
                    // Simple type: int, str, float, etc.
                    return nameExpr.Name;

                case AttributeExpression attrExpr:
                    // Qualified name: typing.List, collections.abc.Mapping, etc.
                    // Recursively build the full name
                    string valueName = ExtractAnnotationName(attrExpr.Value);
                    return $"{valueName}.{attrExpr.Attr}";

                case SubscriptExpression subscriptExpr:
                    // Generic type: List[int], Dict[str, int], Optional[str], etc.
                    string baseName = ExtractAnnotationName(subscriptExpr.Value);
                    string sliceName = ExtractAnnotationName(subscriptExpr.Slice);
                    return $"{baseName}[{sliceName}]";

                case TupleExpression tupleExpr:
                    // Multiple types in subscript: Dict[str, int], Tuple[int, str, float]
                    // Performance: Eliminated LINQ
                    var elementNames = new List<string>(tupleExpr.Elements.Count);
                    foreach (var element in tupleExpr.Elements)
                    {
                        elementNames.Add(ExtractAnnotationName(element));
                    }
                    return string.Join(", ", elementNames);

                default:
                    // Fallback: use ToString() for other expression types
                    return annotation.ToString();
            }
        }

        /// <summary>
        /// CPython 3.12: FunctionArguments에서 매개변수와 기본값 추출
        /// Returns default expressions, NOT evaluated PyObjects
        /// Annotations are Expression objects, not strings (for complex types like List[str])
        /// </summary>
        private (List<string> paramNames, List<Expression> defaultExprs, List<Expression?> kwDefaultExprs, int flags, int argCount, int posonlyArgCount, int kwonlyArgCount, Dictionary<string, Expression> annotations) ParseFunctionArguments(FunctionArguments arguments)
        {
            var paramNames = new List<string>();
            var defaultExprs = new List<Expression>();
            var kwDefaultExprs = new List<Expression?>();
            var annotations = new Dictionary<string, Expression>();
            int flags = PyCodeObject.CO_OPTIMIZED | PyCodeObject.CO_NEWLOCALS;
            int posonlyArgCount = arguments.PosOnlyArgs.Count;
            int kwonlyArgCount = arguments.KwOnlyArgs.Count;

            #if DEBUG_LOG
            Console.WriteLine($"🔍 ParseFunctionArguments: Processing FunctionArguments");
            Console.WriteLine($"  PosOnlyArgs: {arguments.PosOnlyArgs.Count}, Args: {arguments.Args.Count}");
            Console.WriteLine($"  Defaults: {arguments.Defaults.Count}, VarArg: {arguments.VarArg != null}, KwArg: {arguments.KwArg != null}");
            #endif

            // Add positional-only parameters
            foreach (var arg in arguments.PosOnlyArgs)
            {
                paramNames.Add(arg.Name);
                #if DEBUG_LOG
                Console.WriteLine($"  PosOnlyArg: {arg.Name}, Annotation: {arg.Annotation?.ToString() ?? "null"}");
                #endif
                if (arg.Annotation != null)
                {
                    // CPython 3.12: Store annotation Expression (not string) for compilation
                    annotations[arg.Name] = arg.Annotation;
                    #if DEBUG_LOG
                    Console.WriteLine($"    → Stored annotation Expression: {arg.Annotation.GetType().Name}");
                    #endif
                }
            }

            // Add regular parameters
            foreach (var arg in arguments.Args)
            {
                paramNames.Add(arg.Name);
                #if DEBUG_LOG
                Console.WriteLine($"  RegularArg: {arg.Name}, Annotation: {arg.Annotation?.ToString() ?? "null"}");
                #endif
                if (arg.Annotation != null)
                {
                    // CPython 3.12: Store annotation Expression (not string) for compilation
                    annotations[arg.Name] = arg.Annotation;
                    #if DEBUG_LOG
                    Console.WriteLine($"    → Stored annotation Expression: {arg.Annotation.GetType().Name}");
                    #endif
                }
            }

            // CPython 3.12: symtable.c:symtable_visit_arguments
            // The order is: posonlyargs, args, kwonlyargs, vararg, kwarg
            // (kwonlyargs BEFORE vararg in varnames!)

            // Add keyword-only parameters (BEFORE *args, per CPython)
            foreach (var arg in arguments.KwOnlyArgs)
            {
                paramNames.Add(arg.Name);
                if (arg.Annotation != null)
                {
                    // CPython 3.12: Store annotation Expression (not string) for compilation
                    annotations[arg.Name] = arg.Annotation;
                    #if DEBUG_LOG
                    Console.WriteLine($"  KwOnlyArg: {arg.Name}, Annotation Expression: {arg.Annotation.GetType().Name}");
                    #endif
                }
            }

            // Add *args parameter (AFTER kwonlyargs, per CPython)
            if (arguments.VarArg != null)
            {
                paramNames.Add("*" + arguments.VarArg.Name);
                flags |= PyCodeObject.CO_VARARGS;
                if (arguments.VarArg.Annotation != null)
                {
                    // CPython 3.12: Store annotation Expression (not string) for compilation
                    annotations[arguments.VarArg.Name] = arguments.VarArg.Annotation;
                    #if DEBUG_LOG
                    Console.WriteLine($"    → Stored vararg annotation Expression: {arguments.VarArg.Annotation.GetType().Name}");
                    #endif
                }
            }

            // Add **kwargs parameter
            if (arguments.KwArg != null)
            {
                paramNames.Add("**" + arguments.KwArg.Name);
                flags |= PyCodeObject.CO_VARKEYWORDS;
                if (arguments.KwArg.Annotation != null)
                {
                    // CPython 3.12: Store annotation Expression (not string) for compilation
                    annotations[arguments.KwArg.Name] = arguments.KwArg.Annotation;
                    #if DEBUG_LOG
                    Console.WriteLine($"    → Stored kwarg annotation Expression: {arguments.KwArg.Annotation.GetType().Name}");
                    #endif
                }
            }

            // CPython 3.12: Defaults align with the LAST len(defaults) parameters in Args
            // Store expressions, they will be compiled at MAKE_FUNCTION time
            defaultExprs.AddRange(arguments.Defaults);

            // CPython 3.12: Keyword-only defaults
            kwDefaultExprs.AddRange(arguments.KwDefaults);

            // Calculate argCount: total non-variadic parameters
            int argCount = posonlyArgCount + arguments.Args.Count + arguments.KwOnlyArgs.Count;

            #if DEBUG_LOG
            Console.WriteLine($"🔍 ParseFunctionArguments: Final flags = {flags}, argCount = {argCount}, posonlyArgCount = {posonlyArgCount}, kwonlyArgCount = {kwonlyArgCount}");
            Console.WriteLine($"  paramNames = [{string.Join(", ", paramNames)}]");
            Console.WriteLine($"  default expressions = [{string.Join(", ", defaultExprs.Select(d => d.ToString()))}]");
            Console.WriteLine($"  kwdefault expressions = [{string.Join(", ", kwDefaultExprs.Select(d => d?.ToString() ?? "None"))}]");
            Console.WriteLine($"  annotations = [{string.Join(", ", annotations.Select(kv => $"{kv.Key}: {kv.Value}"))}]");
            #endif

            return (paramNames, defaultExprs, kwDefaultExprs, flags, argCount, posonlyArgCount, kwonlyArgCount, annotations);
        }

        /// <summary>
        /// CPython 호환: 매개변수 문자열에서 이름과 기본값 분리 (Legacy - for backwards compatibility)
        /// </summary>
        private (List<string> paramNames, List<PyObject> defaults, int flags, int argCount, int posonlyArgCount, Dictionary<string, string> annotations) ParseFunctionParameters(List<string> parameters)
        {
            var paramNames = new List<string>();
            var defaults = new List<PyObject>();
            var annotations = new Dictionary<string, string>(); // CPython 3.12: 타입 어노테이션 수집
            int flags = PyCodeObject.CO_OPTIMIZED | PyCodeObject.CO_NEWLOCALS; // CPython 3.12 standard flags
            int posonlyArgCount = 0; // CPython 3.12: positional-only 매개변수 개수

            #if DEBUG_LOG
            Console.WriteLine($"🔍 ParseFunctionParameters: Input parameters = [{string.Join(", ", parameters)}]");
            #endif
            
            foreach (var param in parameters)
            {
                string cleanName = param;
                PyObject defaultValue = null;
                
                // CPython 방식: 매개변수 문자열 파싱
                if (param.Contains("="))
                {
                    // name=value 또는 name:type=value 처리
                    var equalIndex = param.LastIndexOf('=');
                    var nameTypePart = param.Substring(0, equalIndex).Trim();
                    var defaultValueStr = param.Substring(equalIndex + 1).Trim();
                    
                    // CPython 3.12: 타입 주석 처리: name:type -> name + annotations
                    if (nameTypePart.Contains(":"))
                    {
                        var colonIndex = nameTypePart.IndexOf(':');
                        cleanName = nameTypePart.Substring(0, colonIndex).Trim();
                        var typeAnnotation = nameTypePart.Substring(colonIndex + 1).Trim();
                        annotations[cleanName] = typeAnnotation;
                    }
                    else
                    {
                        cleanName = nameTypePart;
                    }
                    
                    // CPython 호환: 기본값을 정의 시점에서 평가
                    defaultValue = ParseAndEvaluateDefaultValue(defaultValueStr);
                }
                else if (param.Contains(":"))
                {
                    // CPython 3.12: 타입 주석만 있는 경우: name:type
                    var colonIndex = param.IndexOf(':');
                    cleanName = param.Substring(0, colonIndex).Trim();
                    var typeAnnotation = param.Substring(colonIndex + 1).Trim();
                    annotations[cleanName] = typeAnnotation;
                }
                else
                {
                    // 단순 매개변수 이름
                    cleanName = param.Trim();
                }
                
                // CPython 3.12: 위치 전용 매개변수 구분자 처리
                if (cleanName == "/")
                {
                    // "/" is a separator, not a parameter - skip adding to paramNames
                    // 현재까지 추가된 매개변수들이 모두 positional-only
                    posonlyArgCount = paramNames.Count;
                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 Found positional-only separator: / (skipped from parameters, posonlyArgCount={posonlyArgCount})");
                    #endif
                    continue; // Skip adding "/" to parameter names
                }
                else if (cleanName == "*")
                {
                    // "*" is keyword-only separator, not a parameter - skip adding to paramNames
                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 Found keyword-only separator: * (skipped from parameters)");
                    #endif
                    continue; // Skip adding "*" to parameter names
                }

                // CPython 방식: **kwargs 및 *args 플래그 설정
                if (cleanName.StartsWith("**"))
                {
                    flags |= PyCodeObject.CO_VARKEYWORDS;
                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 Found **kwargs: {cleanName} -> flags = {flags}");
                    #endif
                    cleanName = cleanName.Substring(2); // ** 제거
                }
                else if (cleanName.StartsWith("*"))
                {
                    flags |= PyCodeObject.CO_VARARGS;
                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 Found *args: {cleanName} -> flags = {flags}");
                    #endif
                    cleanName = cleanName.Substring(1); // * 제거
                }

                paramNames.Add(cleanName);
                // CPython 방식: null이 아닌 기본값만 defaults 리스트에 추가
                if (defaultValue != null)
                {
                    defaults.Add(defaultValue);
                }
            }
            
            // CPython 호환: argCount는 일반 위치 매개변수만 포함 (*args/**kwargs 및 / separator 제외)
            int argCount = 0;
            for (int i = 0; i < parameters.Count; i++)
            {
                var originalParam = parameters[i].Trim();
                if (!originalParam.StartsWith("*") && originalParam != "/") // *args, **kwargs, / separator 제외
                {
                    argCount++;
                }
            }

            #if DEBUG_LOG
            Console.WriteLine($"🔍 ParseFunctionParameters: Final flags = {flags}, paramNames = [{string.Join(", ", paramNames)}], argCount = {argCount}, posonlyArgCount = {posonlyArgCount}, annotations = {annotations.Count}");
            #endif
            return (paramNames, defaults, flags, argCount, posonlyArgCount, annotations);
        }
        
        /// <summary>
        /// Async function 매개변수 파싱 - 일반 함수와 동일한 로직
        /// </summary>
        private (List<string> paramNames, List<PyObject> defaults, int flags, int argCount, int posonlyArgCount, Dictionary<string, string> annotations) ParseAsyncFunctionParameters(List<string> parameters)
        {
            var (paramNames, defaults, flags, argCount, posonlyArgCount, annotations) = ParseFunctionParameters(parameters);
            return (paramNames, defaults, flags, argCount, posonlyArgCount, annotations); // CPython 3.12: annotations 포함
        }

        /// <summary>
        /// CPython 3.12: Compile async function body with CO_COROUTINE flag
        /// </summary>
        private PyCodeObject CompileAsyncFunctionBody(AsyncFunctionDefStatement asyncFunc, List<string> freeVars, List<string> cellVars)
        {
            var (paramNames, defaultExprs, kwDefaultExprs, flags, argCount, posonlyArgCount, kwonlyArgCount, annotations) = ParseFunctionArguments(asyncFunc.Arguments);

            // Convert default expressions to PyObjects
            var defaults = new List<PyObject>();
            foreach (var defaultExpr in defaultExprs)
            {
                if (defaultExpr is ConstantExpression constExpr)
                    defaults.Add(constExpr.Value);
                else
                    defaults.Add(PyNone.Instance);
            }

            // Convert keyword-only default expressions
            var kwDefaults = new List<PyObject>();
            foreach (var kwDefaultExpr in kwDefaultExprs)
            {
                if (kwDefaultExpr == null)
                    kwDefaults.Add(PyNone.Instance);
                else if (kwDefaultExpr is ConstantExpression constExpr)
                    kwDefaults.Add(constExpr.Value);
                else
                    kwDefaults.Add(PyNone.Instance);
            }

            // CPython 3.12: CO_COROUTINE flag for async functions
            flags |= PyCodeObject.CO_COROUTINE;

            // Use new CompilerFunctionBody following CPython pattern
            var compiler = new PythonCompiler();
            compiler.SetupClosureCompilation(cellVars, freeVars);
            compiler.SetSourceLocation(_currentFileName, _sourceLines);

            // CPython 3.12: Pass symbol table context to nested compiler
            if (_symbolTable != null)
            {
                // Get the function's symbol table from root children
                // Symbol table names for functions include prefix like "<function:name>" or "<async function:name>"
                var funcSymbolTable = _symbolTable.GetChildren().FirstOrDefault(child => child.GetName().Contains(asyncFunc.Name));
                if (funcSymbolTable != null)
                {
                    compiler.SetSymbolTableContext(funcSymbolTable);
                }
                compiler.SetRootSymbolTable(_symbolTable);
            }

            var codeObject = compiler.CompilerFunctionBody(
                asyncFunc.Body, asyncFunc.Name, paramNames,
                defaults, kwDefaults, freeVars, cellVars,
                flags, argCount, posonlyArgCount, kwonlyArgCount);

            // CPython 3.12: Async generator detection (yield in async function)
            if (codeObject.IsGenerator())
            {
                // Set CO_ASYNC_GENERATOR and clear CO_GENERATOR
                var newFlags = codeObject.Flags | PyCodeObject.CO_ASYNC_GENERATOR;
                newFlags &= ~PyCodeObject.CO_GENERATOR;

                codeObject = new PyCodeObject(
                    codeObject.Name,
                    codeObject.Instructions,
                    codeObject.Constants,
                    codeObject.Names,
                    codeObject.VarNames,
                    codeObject.ArgCount,
                    codeObject.PosonlyArgCount,
                    0,
                    codeObject.FreeVars,
                    codeObject.CellVars,
                    codeObject.DefaultValues,
                    null,
                    newFlags,
                    codeObject.FileName,
                    codeObject.SourceLines
                );
            }

            return codeObject;
        }
        
        /// <summary>
        /// CPython 호환: 기본값을 정의 시점에서 파싱하고 평가
        /// </summary>
        private PyObject ParseAndEvaluateDefaultValue(string defaultValueStr)
        {
            // CPython 방식: 리터럴 우선 처리
            if (int.TryParse(defaultValueStr, out int intValue))
            {
                return new PyInt(intValue);
            }
            
            if (double.TryParse(defaultValueStr, out double floatValue))
            {
                return new PyFloat(floatValue);
            }
            
            // 불린 리터럴
            if (defaultValueStr == "True")
                return PyBool.True;
            if (defaultValueStr == "False")
                return PyBool.False;
            
            // 문자열 리터럴 처리
            if ((defaultValueStr.StartsWith("\"") && defaultValueStr.EndsWith("\"")) ||
                (defaultValueStr.StartsWith("'") && defaultValueStr.EndsWith("'")))
            {
                var content = defaultValueStr.Substring(1, defaultValueStr.Length - 2);
                // 기본적인 이스케이프 처리
                content = content.Replace("\\n", "\n")
                              .Replace("\\t", "\t")
                              .Replace("\\r", "\r")
                              .Replace("\\'", "'")
                              .Replace("\\\"", "\"")
                              .Replace("\\\\", "\\");
                return new PyString(content);
            }
            
            // 인용부호 없는 문자열 (파서에서 따옴표가 제거된 경우)
            // 간단한 식별자/리터럴일 가능성이 있는 문자열은 그대로 문자열로 처리
            if (!defaultValueStr.Contains(" ") && !defaultValueStr.Contains("(") && 
                !defaultValueStr.Contains("[") && !defaultValueStr.Contains("{") &&
                defaultValueStr.All(c => char.IsLetterOrDigit(c) || c == '_'))
            {
                return new PyString(defaultValueStr);
            }
            
            // None 처리
            if (defaultValueStr == "None")
                return PyNone.Instance;
            
            // 복합 표현식은 나중에 처리 (현재는 단순 리터럴만)
            if (!SharpPyConfig.DisassemblyOnlyMode)
            {
                #if DEBUG_LOG
                Console.WriteLine($"⚠️ Warning: Complex default value '{defaultValueStr}' not yet supported");
                #endif
            }
            return PyNone.Instance;
        }

        /// <summary>
        /// CPython 3.12: Evaluate constant expression at compile time
        /// Python/compile.c - handles lambda default values
        /// </summary>
        private PyObject EvaluateConstantExpression(Expression expr)
        {
            switch (expr)
            {
                case ConstantExpression constExpr:
                    return constExpr.Value;

                case NameExpression nameExpr:
                    // Special names
                    if (nameExpr.Name == "None") return PyNone.Instance;
                    if (nameExpr.Name == "True") return PyBool.True;
                    if (nameExpr.Name == "False") return PyBool.False;
                    // For other names, we cannot evaluate at compile time
                    // This would be a runtime evaluation case
                    #if DEBUG_LOG
                    Console.WriteLine($"⚠️ Warning: Non-constant default value '{nameExpr.Name}' - using None");
                    #endif
                    return PyNone.Instance;

                case UnaryOpExpression unaryExpr when unaryExpr.OpNode is USub:
                    // Handle negative numbers like -5
                    var innerValue = EvaluateConstantExpression(unaryExpr.Operand);
                    if (innerValue is PyInt pyInt)
                        return new PyInt(-pyInt.Value);
                    if (innerValue is PyFloat pyFloat)
                        return new PyFloat(-pyFloat.Value);
                    return PyNone.Instance;

                default:
                    #if DEBUG_LOG
                    Console.WriteLine($"⚠️ Warning: Cannot evaluate expression '{expr.GetType().Name}' at compile time");
                    #endif
                    return PyNone.Instance;
            }
        }

        /// <summary>
        /// CPython 3.12: compiler_function_body
        /// Compiles function body statements and returns PyCodeObject
        /// This is the CPython-compatible function body compilation method
        /// </summary>
        private PyCodeObject CompilerFunctionBody(
            List<Statement> statements,
            string name,
            List<string> paramNames,
            List<PyObject> defaults,
            List<PyObject> kwDefaults,
            List<string> freeVars,
            List<string> cellVars,
            int flags,
            int argCount,
            int posonlyArgCount,
            int kwonlyArgCount)
        {
            // Clear all compilation state for new compilation
            // CPython 3.12: _instructions removed - using InstructionSequence only
            _constants.Clear();
            _names.Clear();
            _varNames.Clear();
            _exceptionTable.Clear();
            _lineNumberTable.Clear();
            _isInFunction = true;
            _currentFunctionName = name;

            // CRITICAL: Set free and cell variables from parameters
            // These must be set BEFORE compiling the body so EmitLoadName can find them
            _freeVars = freeVars ?? new List<string>();
            // CPython 3.12: CellVars should be in alphabetical order (matching FindCellVariables)
            _cellVars = cellVars ?? new List<string>();

#if DEBUG_COMPILER_LOG
            Console.WriteLine($"  🔍 DEBUG CompilerFunctionBody: _currentSymbolTable = {_currentSymbolTable?.Name ?? "NULL"}");
            Console.WriteLine($"  🔍 DEBUG _freeVars set to: [{string.Join(", ", _freeVars)}]");
            Console.WriteLine($"  🔍 DEBUG _cellVars set to: [{string.Join(", ", _cellVars)}]");
#endif

            // Calculate correct argCount for CPython 3.12 compatibility
            int finalArgCount = (argCount >= 0) ? argCount : paramNames.Count;

            // Add function parameters to _varNames (for LOAD_FAST/STORE_FAST)
            foreach (var param in paramNames)
            {
                // Remove ** or * prefix from parameter names
                string localVarName = param;
                if (param.StartsWith("**"))
                {
                    localVarName = param.Substring(2);
                }
                else if (param.StartsWith("*"))
                {
                    localVarName = param.Substring(1);
                }

                _varNames.Add(localVarName);
            }

            // Collect local variables from function body
            // CPython 3.12: paramNames may contain "*args" and "**kwargs" but we need clean names
            var localVarNames = new List<string>();
            foreach (var param in paramNames)
            {
                string cleanName = param;
                if (param.StartsWith("**"))
                    cleanName = param.Substring(2);
                else if (param.StartsWith("*"))
                    cleanName = param.Substring(1);
                localVarNames.Add(cleanName);
            }
            CollectLocalVariables(statements, localVarNames);

            // Add non-parameter local variables to _varNames
            // CPython 3.12: Cell variables should NOT be in varNames!
            foreach (var localVar in localVarNames)
            {
                if (!_varNames.Contains(localVar) && !cellVars.Contains(localVar))
                {
                    _varNames.Add(localVar);
                }
            }

#if DEBUG_COMPILER_LOG
            Console.WriteLine($"\n🔧 CompilerFunctionBody: {name}");
            Console.WriteLine($"  _varNames after setup: [{string.Join(", ", _varNames)}]");
            Console.WriteLine($"  localVarNames collected: [{string.Join(", ", localVarNames)}]");
            Console.WriteLine($"  Parameters: [{string.Join(", ", paramNames)}]");
            Console.WriteLine($"  Defaults: [{string.Join(", ", defaults.Select(d => d?.ToString() ?? "None"))}]");
            Console.WriteLine($"  FreeVars: [{string.Join(", ", freeVars)}]");
            Console.WriteLine($"  CellVars: [{string.Join(", ", cellVars)}]");
            Console.WriteLine($"  Flags received: 0x{flags:X} (CO_GENERATOR={((flags & PyCodeObject.CO_GENERATOR) != 0)})");
#endif

            // CPython 3.12 compile.c line 1340: Add RESUME 0 at function entry
            // This is added BEFORE compiling the body
            _currentLineNumber = 0;
            EmitInstruction(ByteCodeOp.RESUME, 0);

            // Compile function body statements
            foreach (var statement in statements)
            {
                CompileStatement(statement);
            }

            // CPython 3.12: Add implicit None return if function doesn't end with return
            bool endsWithReturn = false;
            if (statements.Count > 0)
            {
                var lastStmt = statements[statements.Count - 1];
                endsWithReturn = EndsWithReturn(lastStmt);
            }

            if (!endsWithReturn)
            {
#if DEBUG_COMPILER_LOG
                Console.WriteLine($"  → Adding implicit None return for {name}");
#endif
                var noneConstIndex = GetOrAddConstant(PyNone.Instance);
                EmitInstruction(ByteCodeOp.RETURN_CONST, noneConstIndex);
            }

            // CPython 3.12: wrap_in_stopiteration_handler for generators/coroutines
            // compile.c:2244-2261 - add_stopiteration_handler = c->u->u_ste->ste_coroutine || c->u->u_ste->ste_generator
            // IMPORTANT: flags must have CO_GENERATOR/CO_COROUTINE set from SymbolTable (see CompileNestedFunction)
            bool addStopIterationHandler = (flags & (PyCodeObject.CO_GENERATOR | PyCodeObject.CO_COROUTINE | PyCodeObject.CO_ASYNC_GENERATOR)) != 0;

            if (addStopIterationHandler && _instructionSequence != null)
            {
#if DEBUG_COMPILER_LOG
                Console.WriteLine($"  → Wrapping generator/coroutine in StopIteration handler (flags=0x{flags:X})");
#endif
                WrapInStopIterationHandler();
            }

            // CPython 3.12: Get final instructions with prefix insertion in CFG phase
            // CPython compile.c:7710 prepare_localsplus calls insert_prefix_instructions
            // BEFORE compile.c:7724 _PyCfg_ResolveJumps calculates JUMP offsets
            var finalInstructions = GetFinalInstructions(flags);

            // Prefix instructions (RETURN_GENERATOR, POP_TOP, MAKE_CELL, COPY_FREE_VARS)
            // are now inserted in GetFinalInstructions during CFG phase
            // This ensures JUMP offsets are calculated AFTER prefix insertion

            // Create code object
            var codeObject = new PyCodeObject(
                name, finalInstructions, _constants, _names, _varNames,
                finalArgCount, posonlyArgCount, kwonlyArgCount,
                freeVars, cellVars, defaults, kwDefaults,
                flags, _currentFileName, _sourceLines
            );

            // Add Exception Table entries (CPython 3.12 compatible)
            // CPython 3.12: compile.c:7775 _PyAssemble_MakeCodeObject
            if (_exceptionTable.Count > 0)
            {
                codeObject.ExceptionTable.AddRange(_exceptionTable);
            }

            // CPython 3.12: Generate pending exception handlers at end of bytecode
            GeneratePendingExceptionHandlers();

            _isInFunction = false;
            _currentFunctionName = null;
            return codeObject;
        }

        /// <summary>
        /// CPython 3.12: compiler_decorators
        /// Compiles decorator expressions (called before function compilation)
        /// Loads decorators in SOURCE order (top to bottom)
        /// After MAKE_FUNCTION, CALL is applied in reverse (stack order)
        /// </summary>
        private void CompilerDecorators(List<DecoratorExpression>? decorators)
        {
            if (decorators == null || decorators.Count == 0)
                return;

            #if DEBUG_COMPILER_LOG
            Console.WriteLine($"🎨 CompilerDecorators: Loading {decorators.Count} decorators in source order");
            #endif

            // CPython 3.12: Load decorators in SOURCE order (top to bottom)
            // Python/compile.c:1852-1861 - compiler_decorators
            // The last decorator loaded (bottom-most in source) will be on top of stack
            // and will be called FIRST, wrapping the function before outer decorators
            for (int i = 0; i < decorators.Count; i++)
            {
                var decorator = decorators[i];

                if (decorator.Arguments.Count > 0)
                {
                    // Parametric decorator: @decorator(args) - needs PUSH_NULL
                    EmitInstruction(ByteCodeOp.PUSH_NULL);
                    CompileExpression(decorator.DecoratorFunction);

                    // Separate positional and keyword arguments
                    var positionalArgs = new List<Expression>();
                    var keywordArgs = new List<KeywordExpression>();

                    foreach (var arg in decorator.Arguments)
                    {
                        if (arg is KeywordExpression keyword)
                            keywordArgs.Add(keyword);
                        else
                            positionalArgs.Add(arg);
                    }

                    // Compile positional arguments first
                    foreach (var arg in positionalArgs)
                        CompileExpression(arg);

                    // Compile keyword argument values
                    foreach (var keyword in keywordArgs)
                        CompileExpression(keyword.Value);

                    // Handle keyword arguments with KW_NAMES (CPython 3.12 pattern)
                    if (keywordArgs.Count > 0)
                    {
                        // Performance: Eliminated LINQ + Cache
                        var kwNames = new PyObject[keywordArgs.Count];
                        for (int j = 0; j < keywordArgs.Count; j++)
                        {
                            kwNames[j] = StringCache.GetOrCreate(keywordArgs[j].Arg ?? "");
                        }
                        var kwNamesTuple = TupleCache.GetOrCreate(kwNames);
                        var kwNamesIndex = GetOrAddConstant(kwNamesTuple);

                        EmitInstruction(ByteCodeOp.KW_NAMES, kwNamesIndex);
                        EmitInstruction(ByteCodeOp.CALL, positionalArgs.Count + keywordArgs.Count);
                    }
                    else
                    {
                        EmitInstruction(ByteCodeOp.CALL, positionalArgs.Count);
                    }
                }
                else
                {
                    // CPython 3.12: Python/compile.c:1852-1861 (compiler_decorators)
                    // Simple decorator: @decorator - just load the decorator name
                    // Decorators are loaded onto stack and applied later in CompilerApplyDecorators
                    // PUSH_NULL is NOT needed - decorators are not called here, just loaded
                    CompileExpression(decorator.DecoratorFunction);
                }
            }
        }

        /// <summary>
        /// CPython 3.12: compiler_apply_decorators
        /// Applies decorators to the function on top of stack
        /// Decorators were loaded in reverse order, now call them forward
        /// </summary>
        private void CompilerApplyDecorators(List<DecoratorExpression>? decorators)
        {
            if (decorators == null || decorators.Count == 0)
                return;

            #if DEBUG_COMPILER_LOG
            Console.WriteLine($"🎨 CompilerApplyDecorators: Calling {decorators.Count} decorators");
            #endif

            // CPython 3.12: Apply decorators in forward order
            for (int i = 0; i < decorators.Count; i++)
            {
                // Call decorator with function (no arguments - they were already processed)
                EmitInstruction(ByteCodeOp.CALL, 0);
            }
        }

        /// <summary>
        /// CPython 3.12: compiler_default_arguments
        /// Compiles default argument values and returns function flags
        /// </summary>
        private int CompilerDefaultArguments(List<Expression> defaultExprs, List<Expression> kwDefaultExprs, List<string> kwOnlyArgNames)
        {
            int funcflags = 0;

            // Positional defaults
            if (defaultExprs.Count > 0)
            {
                foreach (var defaultExpr in defaultExprs)
                    CompileExpression(defaultExpr);

                EmitInstruction(ByteCodeOp.BUILD_TUPLE, defaultExprs.Count);
                funcflags |= MakeFunctionFlags.DEFAULTS;

                #if DEBUG_COMPILER_LOG
                Console.WriteLine($"  → Built defaults tuple: {defaultExprs.Count} defaults");
                #endif
            }

            // CPython 3.12: Keyword-only defaults
            // compiler_visit_kwonlydefaults in Python/compile.c:1878-1933
            if (kwDefaultExprs.Count > 0 && kwOnlyArgNames.Count > 0)
            {
                var keys = new List<string>();

                // Build dict of keyword-only defaults
                for (int i = 0; i < kwOnlyArgNames.Count; i++)
                {
                    if (i < kwDefaultExprs.Count && kwDefaultExprs[i] != null)
                    {
                        // Add parameter name to keys
                        keys.Add(kwOnlyArgNames[i]);

                        // Compile default value expression
                        CompileExpression(kwDefaultExprs[i]);
                    }
                }

                if (keys.Count > 0)
                {
                    // Load keys tuple as constant
                    // Performance: Eliminated LINQ + Cache
                    var keysArray = new PyObject[keys.Count];
                    for (int i = 0; i < keys.Count; i++)
                    {
                        keysArray[i] = StringCache.GetOrCreate(keys[i]);
                    }
                    var keysTuple = TupleCache.GetOrCreate(keysArray);
                    EmitLoadConst(keysTuple);

                    // BUILD_CONST_KEY_MAP with number of items
                    EmitInstruction(ByteCodeOp.BUILD_CONST_KEY_MAP, keys.Count);
                    funcflags |= MakeFunctionFlags.KWDEFAULTS;

                    #if DEBUG_COMPILER_LOG
                    Console.WriteLine($"  → Built kwdefaults dict: {keys.Count} kwdefaults, keys=[{string.Join(", ", keys)}]");
                    #endif
                }
            }

            return funcflags;
        }

        /// <summary>
        /// CPython 3.12: compiler_visit_annotations
        /// Compiles type annotations and returns annotation count (or -1 if none)
        /// </summary>
        private int CompilerVisitAnnotations(Dictionary<string, Expression> annotations)
        {
            if (annotations.Count == 0)
                return 0;

            // CPython 3.12 pattern: ('key', type_obj, 'key2', type_obj2, ...)
            foreach (var annotation in annotations)
            {
                EmitLoadConst(new PyString(annotation.Key));
                CompileExpression(annotation.Value);
            }

            EmitInstruction(ByteCodeOp.BUILD_TUPLE, annotations.Count * 2);

            #if DEBUG_COMPILER_LOG
            Console.WriteLine($"  → Built annotations tuple: {annotations.Count} annotations");
            #endif

            return annotations.Count;
        }

        /// <summary>
        /// CPython 3.12: compiler_make_closure
        /// Creates closure tuple and emits MAKE_FUNCTION with proper flags
        /// Returns updated funcflags with CLOSURE bit set if needed
        /// </summary>
        private int CompilerMakeClosure(PyCodeObject codeObject, List<string> freeVars, int funcflags)
        {
            // Build closure if function has free variables
            if (freeVars.Count > 0)
            {
                #if DEBUG_COMPILER_LOG
                Console.WriteLine($"  → Creating closure for {freeVars.Count} free variables");
                #endif

                // Load closure cells for each free variable
                // CPython 3.12: Iterate through nested function's freevars (already alphabetically sorted)
                // and emit LOAD_CLOSURE with parent's localsplus offset for each
                foreach (var freeVar in freeVars)
                {
                    // Use EmitLoadClosure helper which calculates correct localsplus offset
                    // - For parameters that are cells: uses varnames index
                    // - For non-parameter cells: uses nlocals + cellvars index
                    // - For freevars: uses nlocals + ncellvars + freevars index
                    EmitLoadClosure(freeVar);
                }

                EmitInstruction(ByteCodeOp.BUILD_TUPLE, freeVars.Count);
                funcflags |= MakeFunctionFlags.CLOSURE;
            }

            // Load code object and emit MAKE_FUNCTION
            EmitLoadConst(codeObject);

            #if DEBUG_COMPILER_LOG
            Console.WriteLine($"  → MAKE_FUNCTION flags: {funcflags}");
            #endif

            EmitInstruction(ByteCodeOp.MAKE_FUNCTION, funcflags);

            return funcflags;
        }

        /// <summary>
        /// CPython 3.12: 지연된 exception handler들을 바이트코드 끝에 생성
        /// </summary>
        private void GeneratePendingExceptionHandlers()
        {
            #if DEBUG_LOG
            Console.WriteLine($"🔧 GeneratePendingExceptionHandlers 호출: {_pendingExceptionHandlers.Count}개 handler 처리");
            #endif
            foreach (var handler in _pendingExceptionHandlers)
            {
                // Exception handler를 바이트코드 끝에 생성
                #if DEBUG_LOG
                // CPython 3.12: Exception handler position tracked by CFG labels
                #endif
                #if DEBUG_LOG
                // CPython 3.12: Last instruction tracked in InstructionSequence
                #endif

                // CPython 3.12: Handler position now tracked by CFG BuildExceptionTable()
                // TODO: Remove this legacy exception handler code after CFG migration complete
                var handlerStart = 0;  // Placeholder - CFG determines actual position

                // CPython 3.12 호환 exception handler 생성
                EmitInstruction(ByteCodeOp.SWAP, 2);
                EmitInstruction(ByteCodeOp.POP_TOP);
                EmitInstruction(ByteCodeOp.SWAP, 2);

                // Comprehension 변수 정리
                foreach (var varName in handler.ComprehensionVars)
                {
                    EmitInstruction(ByteCodeOp.STORE_FAST, GetOrAddVarName(varName));
                }

                EmitInstruction(ByteCodeOp.RERAISE, 0);

                #if DEBUG_LOG
                // CPython 3.12: Handler completion tracked by CFG
                #endif

                // Exception table entry 생성 - handlerStart는 실제 첫 번째 handler 명령어 위치
                var exceptionEntry = new ExceptionTableEntry(
                    start: handler.StartOffset,
                    end: handler.EndOffset,
                    handler: handlerStart,
                    depth: handler.Depth
                );
                #if DEBUG_LOG
                Console.WriteLine($"🔧 Exception table entry 생성: {handler.StartOffset} to {handler.EndOffset} -> {handlerStart} [depth={handler.Depth}]");
                #endif
                _exceptionTable.Add(exceptionEntry);
            }
            
            // 사용 완료된 pending handler들 정리
            _pendingExceptionHandlers.Clear();
        }
        
        /// <summary>
        /// REMOVED: This legacy method has been eliminated to match CPython 3.12 architecture
        /// Use CompilerFunctionBody instead, which requires proper symbol table setup via SetSymbolTableContext
        ///
        /// CPython 3.12 pattern:
        ///   1. Build symbol table first (_PySymtable_Build)
        ///   2. Enter scope (compiler_enter_scope sets u->u_ste)
        ///   3. Compile function body
        ///
        /// This ensures symbol table is ALWAYS valid, eliminating need for null checks
        /// </summary>
        /// <summary>
        /// LEGACY: Only for AST evaluation path (ast.cs)
        /// DO NOT use in compiler - use CompilerFunctionBody with SetSymbolTableContext
        /// This method builds symbol table on-demand, which is less efficient than CPython's approach
        /// </summary>
        [Obsolete("Use CompilerFunctionBody with proper symbol table setup for compiler code paths", false)]
        public PyCodeObject CompileFunction(List<Statement> statements, string name, List<string> paramNames, List<PyObject> defaults, int flags = 0, int posonlyArgCount = 0)
        {
            // AST evaluation path: Build symbol table on-demand
            var symbolTableBuilder = new SymbolTableBuilder();
            var functionSymbolTable = symbolTableBuilder.BuildSymbolTable(statements, name);
            _currentSymbolTable = functionSymbolTable;
            _symbolTableBuilder = symbolTableBuilder;  // CPython 3.12: Store for PySymtable_Lookup

            // Use CompilerFunctionBody now that symbol table is set
            var kwDefaults = new List<PyObject>();
            int argCount = paramNames.Count(p => !p.StartsWith("*"));
            int kwonlyArgCount = 0;

            return CompilerFunctionBody(
                statements, name, paramNames,
                defaults, kwDefaults, new List<string>(), new List<string>(),
                flags, argCount, posonlyArgCount, kwonlyArgCount);
        }
        
        /// <summary>
        /// CPython 3.12: Get final instruction list from CFG pipeline
        /// </summary>
        private List<ByteCodeInstruction> GetFinalInstructions(int codeFlags)
        {
            // CPython 3.12 CFG PIPELINE: InstructionSequence → CFG → InsertPrefix → Optimize → ByteCode
#if DEBUG_COMPILER_LOG
            Console.WriteLine($"🔷 [CFG PIPELINE] GetFinalInstructions: InstructionSequence → CFG → InsertPrefix → Optimize → Assemble");
            Console.WriteLine($"   InstructionSequence has {_instructionSequence.Count} instructions");
#endif

            // Phase 0.5: Insert prefix instructions in InstructionSequence (CPython compile.c:7517-7587)
            // CRITICAL: This must happen BEFORE CFG conversion so labels are automatically adjusted
            // CPython inserts prefix instructions before converting to CFG, ensuring all labels point correctly
#if DEBUG_COMPILER_LOG
            Console.WriteLine($"   🔧 Phase 0.5: Inserting prefix instructions in InstructionSequence (flags=0x{codeFlags:X})...");
#endif
            _instructionSequence.InsertPrefixInstructions(codeFlags, _cellVars ?? new List<string>(), _freeVars ?? new List<string>());
#if DEBUG_COMPILER_LOG
            Console.WriteLine($"   InstructionSequence after prefix: {_instructionSequence.Count} instructions");
#endif

            // Phase 1: InstructionSequence (labels) → CFG (basic blocks)
            var cfg = PyFlowGraph.Build(_instructionSequence);
#if DEBUG_COMPILER_LOG
            Console.WriteLine($"   CFG has {cfg.AllBlocks.Count} basic blocks");
#endif

            // Phase 2: Optimize CFG (CPython 3.12, 항상 활성화)
#if DEBUG_COMPILER_LOG
            Console.WriteLine($"   🔧 Running CFG optimization...");
#endif
            var cfgOptimizer = new CFGOptimizer(cfg, _constants);
            cfgOptimizer.Optimize();
#if DEBUG_COMPILER_LOG
            Console.WriteLine($"   ✅ CFG optimized: {cfg.AllBlocks.Count} blocks");
#endif

            // Phase 2.5: Fix cell/free variable offsets (CPython compile.c:7659)
            // CRITICAL: Must be called AFTER CFG is built but BEFORE final assembly
#if DEBUG_COMPILER_LOG
            Console.WriteLine($"   🔧 Phase 2.5: Fixing cell/free variable offsets...");
#endif
            cfg.FixCellOffsets(_varNames ?? new List<string>(), _cellVars ?? new List<string>(), _freeVars ?? new List<string>());

            // Phase 3: CFG → ByteCode (with correct offsets)
            // CPython compile.c:7724 _PyCfg_ResolveJumps - JUMP offsets calculated here
#if DEBUG_COMPILER_LOG
            Console.WriteLine($"   🔧 Phase 3: Calling PyAssemble.Assemble...");
#endif
            var assembled = PyAssemble.Assemble(cfg, _currentFileName ?? "");
#if DEBUG_COMPILER_LOG
            Console.WriteLine($"   Assembled {assembled.Instructions.Count} instructions");
            Console.WriteLine($"   Exception table has {assembled.ExceptionTable.Count} entries");
#endif

            // Store exception table for code object
            _exceptionTable = assembled.ExceptionTable;

            return assembled.Instructions;
        }

        /// <summary>
        /// Phase 2: 컴파일러에 클로저 정보 설정
        /// </summary>
        public void SetupClosureCompilation(List<string> cellVars, List<string> freeVars = null)
        {
            _cellVars = cellVars ?? new List<string>();
            _freeVars = freeVars ?? new List<string>();
        }

        /// <summary>
        /// CPython 3.12: Set source location information for nested function compilation
        /// </summary>
        public void SetSourceLocation(string? fileName, List<string>? sourceLines)
        {
            _currentFileName = fileName;
            _sourceLines = sourceLines;
        }
        
        private void CompileStatement(Statement statement)
        {
            // Update current source location
            UpdateSourceLocation(statement);

            switch (statement)
            {
                case AssignStatement assign:
                    // CPython 3.12: compile.c line 3951-3962 (Assign_kind)
                    // VISIT(c, expr, s->v.Assign.value);
                    // for each target: if not last, COPY 1; VISIT(c, expr, target);

                    // Standard assignment path (optimizer will handle tuple swap optimization)
                    // Step 1: Compile the value expression (right-hand side)
                    CompileExpression(assign.Value);

                    // Step 2: For each target, COPY if not last, then compile target
                    for (int i = 0; i < assign.Targets.Count; i++)
                    {
                        var target = assign.Targets[i];

                        // If not the last target, copy the value for next assignment
                        if (i < assign.Targets.Count - 1)
                        {
                            EmitInstruction(ByteCodeOp.COPY, 1);
                        }

                        // Compile target expression (this handles UNPACK_SEQUENCE for tuples)
                        CompileAssignmentTarget(target);
                    }
                    break;

                case AttributeStatement attrAssign:
                    // CPython 3.12: Attribute assignment (self.x = value)
                    CompileExpression(attrAssign.Value);    // 값을 먼저 스택에 로드
                    CompileExpression(attrAssign.Object);   // 객체를 스택에 로드
                    EmitInstruction(ByteCodeOp.STORE_ATTR, GetOrAddName(attrAssign.Attr));
                    break;

                case AssignTargetStatement assignTarget:
                    CompileAssignTarget(assignTarget);
                    break;
                    
                case ChainedAssignStatement chainedAssign:
                    CompileChainedAssign(chainedAssign);
                    break;
                    
                case AnnAssignStatement annAssign:
                    CompileAnnAssign(annAssign);
                    break;
                    
                case AugAssignStatement augAssign:
                    CompileAugAssign(augAssign);
                    break;
                    
                case AugmentedAssignStatement augmentedAssign:
                    CompileAugmentedAssign(augmentedAssign);
                    break;
                    
                case WalrusStatement walrus:
                    CompileExpression(walrus.Value);
                    EmitInstruction(ByteCodeOp.COPY, 1);  // 값 복사
                    EmitStoreVariable(walrus.Target);     // 올바른 스코프로 저장
                    break;
                    
                case ExpressionStatement expr:
                    // CPython 3.12: compiler_stmt_expr (Python/compile.c line 3915)
                    if (_isInteractive && !_isInFunction)
                    {
                        // Interactive mode: print expression result
                        // CPython: if (c->c_interactive && c->c_nestlevel <= 1)
                        #if DEBUG_LOG
                        Console.WriteLine($"🎯 Interactive mode: Compiling expression statement with INTRINSIC_PRINT");
                        Console.WriteLine($"   Expression type: {expr.Expression.GetType().Name}");
                        #endif
                        CompileExpression(expr.Expression);
                        EmitInstruction(ByteCodeOp.CALL_INTRINSIC_1, (int)IntrinsicFunction.INTRINSIC_PRINT);
                        EmitInstruction(ByteCodeOp.POP_TOP);
                    }
                    else if (_compileMode == CompileMode.Eval && !_isInFunction)
                    {
                        // CPython 3.12: Eval mode (Expression_kind) - keep result on stack for RETURN_VALUE
                        // Python/compile.c: compiler_body() case Expression_kind just visits the expression
                        // No POP_TOP - result stays on stack to be returned
                        #if DEBUG_LOG
                        Console.WriteLine($"📝 Eval mode: Compiling expression without POP_TOP (will return result)");
                        Console.WriteLine($"   Expression type: {expr.Expression.GetType().Name}");
                        #endif
                        CompileExpression(expr.Expression);
                        // No POP_TOP - result stays on stack for RETURN_VALUE
                    }
                    else
                    {
                        // Normal mode: just evaluate and discard
                        #if DEBUG_LOG
                        Console.WriteLine($"📝 Normal mode: Compiling expression statement (no print)");
                        Console.WriteLine($"   _isInteractive={_isInteractive}, _isInFunction={_isInFunction}");
                        #endif
                        CompileExpression(expr.Expression);
                        // CPython 3.12: After YIELD_VALUE + RESUME, sent value is on stack and needs POP_TOP
                        // POP_TOP is needed for ALL expressions, including YieldExpression
                        EmitInstruction(ByteCodeOp.POP_TOP);
                    }
                    break;
                    
                case ReturnStatement ret:
                    // CPython 3.12: compiler_return (Python/compile.c line 3165)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"🔍 CompileStatement ReturnStatement: LineNo={ret.LineNo}, ColOffset={ret.ColOffset}, _currentLineNumber={_currentLineNumber}");
                        if (ret.Value != null)
                        {
                            Console.WriteLine($"   Return value: {ret.Value.GetType().Name}, LineNo={ret.Value.LineNo}");
                        }
                        #endif

                        var loc = new SourceLocation(_currentLineNumber, _currentColumnOffset);
                        bool preserveTos = (ret.Value != null);

                        // Compile return value first (if exists)
                        if (ret.Value != null)
                        {
                            CompileExpression(ret.Value);
                        }

                        // Unwind ALL fblocks (findLoop = false means unwind everything)
                        UnwindFBlockStack(ref loc, preserveTos, findLoop: false);

                        // Emit return instruction
                        if (ret.Value != null)
                        {
                            // CPython 3.12: 상수 표현식이면 RETURN_CONST 직접 생성
                            if (ret.Value is ConstantExpression literal)
                            {
                                var constIndex = GetOrAddConstant(literal.Value);
                                EmitInstruction(ByteCodeOp.RETURN_CONST, constIndex);
                            }
                            else if (ret.Value is NameExpression name && name.Name == "None")
                            {
                                var constIndex = GetOrAddConstant(PyNone.Instance);
                                EmitInstruction(ByteCodeOp.RETURN_CONST, constIndex);
                            }
                            else
                            {
                                EmitInstruction(ByteCodeOp.RETURN_VALUE);
                            }
                        }
                        else
                        {
                            var constIndex = GetOrAddConstant(PyNone.Instance);
                            EmitInstruction(ByteCodeOp.RETURN_CONST, constIndex);
                        }
                    }
                    break;
                    
                case YieldStatement yield:
                    if (yield.Value != null)
                        CompileExpression(yield.Value);
                    else
                        EmitLoadConst(PyNone.Instance);

                    // CPython 3.12: compile.c:4106-4113
                    // Async generators need to wrap yielded values
                    // CPython guarantees u->u_ste is always valid via compiler_enter_scope (compile.c:1236-1257)
                    // SharpPy guarantees _currentSymbolTable is set via upfront SymbolTableBuilder analysis
                    if (_currentSymbolTable.IsGenerator && _currentSymbolTable.IsCoroutine)
                    {
                        EmitInstruction(ByteCodeOp.CALL_INTRINSIC_1, (int)IntrinsicFunction.INTRINSIC_ASYNC_GEN_WRAP);
                    }

                    EmitInstruction(ByteCodeOp.YIELD_VALUE, 1); // CPython 3.12: yield_value argument 1
                    EmitInstruction(ByteCodeOp.RESUME, 1); // CPython 3.12: Resume after yield
                    EmitInstruction(ByteCodeOp.POP_TOP); // CPython 3.12: POP_TOP after resume
                    break;
                    
                case YieldFromStatement yieldFrom:
                    // CPython 3.12: compile.c:6128-6131
                    // VISIT(c, expr, e->v.YieldFrom.value);
                    CompileExpression(yieldFrom.Value);
                    // ADDOP(c, loc, GET_YIELD_FROM_ITER);
                    EmitInstruction(ByteCodeOp.GET_YIELD_FROM_ITER);
                    #if DEBUG_COMPILER_LOG
                    Console.WriteLine($"[YieldFrom] Added GET_YIELD_FROM_ITER at instruction count {_instructionSequence!.Count}");
                    #endif
                    // ADDOP_LOAD_CONST(c, loc, Py_None);
                    // Note: LOAD_CONST None is added inside CompileYieldFrom() so sendLabel points to SEND
                    // ADD_YIELD_FROM(c, loc, 0);
                    CompileYieldFrom(isAwait: false);
                    break;
                    
                case FunctionDefStatement func:
                    CompileNestedFunction(func);
                    break;
                    
                case AsyncFunctionDefStatement asyncFunc:
                    CompileAsyncFunction(asyncFunc);
                    break;
                    
                case ClassDefStatement cls:
                    CompileClass(cls);
                    break;
                    
                case TypeAliasStatement typeAlias:
                    CompileTypeAlias(typeAlias);
                    break;
                    
                case ImportStatement import:
                    CompileImport(import);
                    break;
                    
                case ImportFromStatement importFrom:
                    CompileImportFrom(importFrom);
                    break;
                    
                case IfStatement ifStmt:
                    CompileIf(ifStmt);
                    break;
                    
                case WhileStatement whileStmt:
                    CompileWhile(whileStmt);
                    break;
                    
                case ForStatement forStmt:
                    CompileFor(forStmt);
                    break;

                case AsyncForStatement asyncForStmt:
                    CompileAsyncFor(asyncForStmt);
                    break;

                case ForTupleStatement forTupleStmt:
                    CompileForTuple(forTupleStmt);
                    break;
                    
                case ForComplexStatement forComplexStmt:
                    CompileForComplex(forComplexStmt);
                    break;
                    
                case BlockStatement blockStmt:
                    CompileBlockStatement(blockStmt);
                    break;
                    
                case TryStatement tryStmt:
                    // CPython 3.12: Always use InstructionSequence/CFG path
                    CompileTryStatementCFG(tryStmt);
                    break;

                case TryStarStatement tryStarStmt:
                    // CPython 3.12: Exception Groups (PEP 654) - except* syntax
                    CompileTryStarStatementCFG(tryStarStmt);
                    break;

                case WithStatement withStmt:
                    CompileWith(withStmt);
                    break;

                case AsyncWithStatement asyncWithStmt:
                    CompileAsyncWith(asyncWithStmt);
                    break;

                case MatchStatement matchStmt:
                    CompileMatch(matchStmt);
                    break;
                    
                case BreakStatement:
                    // CPython 3.12: compiler_break (Python/compile.c lines 3178-3191)
                    {
                        var loc = new SourceLocation(_currentLineNumber, _currentColumnOffset);
                        var originLoc = loc;

                        // NOP for line number marker
                        EmitInstruction(ByteCodeOp.NOP);

                        // Unwind fblocks until we find a loop
                        var loop = UnwindFBlockStack(ref loc, preserveTos: false, findLoop: true);

                        if (loop == null)
                        {
                            throw new SyntaxErrorException(
                                "'break' outside loop",
                                _currentFileName, originLoc.Line, originLoc.Column);
                        }

                        // Unwind the loop itself (pops iterator for FOR_LOOP)
                        UnwindFBlock(ref loc, loop, preserveTos: false);

                        // Jump to loop exit (fb_exit)
                        _instructionSequence!.AddOpWithLabel(
                            ByteCodeOp.JUMP,
                            loop.Exit,
                            _currentLineNumber,
                            _currentColumnOffset,
                            _currentFileName
                        );
                    }
                    break;

                case ContinueStatement:
                    // CPython 3.12: compiler_continue (Python/compile.c lines 3194-3206)
                    {
                        var loc = new SourceLocation(_currentLineNumber, _currentColumnOffset);
                        var originLoc = loc;

                        // NOP for line number marker
                        EmitInstruction(ByteCodeOp.NOP);

                        // Unwind fblocks until we find a loop
                        var loop = UnwindFBlockStack(ref loc, preserveTos: false, findLoop: true);

                        if (loop == null)
                        {
                            throw new SyntaxErrorException(
                                "'continue' not properly in loop",
                                _currentFileName, originLoc.Line, originLoc.Column);
                        }

                        // IMPORTANT: continue does NOT unwind the loop itself (unlike break)
                        // Just jump directly to loop start (fb_block)
                        _instructionSequence!.AddOpWithLabel(
                            ByteCodeOp.JUMP,
                            loop.Block,
                            _currentLineNumber,
                            _currentColumnOffset,
                            _currentFileName
                        );
                    }
                    break;
                    
                case PassStatement:
                    EmitInstruction(ByteCodeOp.NOP);
                    break;

                case NopStatement:
                    EmitInstruction(ByteCodeOp.NOP);
                    break;

                case AssertStatement assert:
                    CompileAssert(assert);
                    break;
                    
                case RaiseStatement raise:
                    CompileRaise(raise);
                    break;
                    
                case DeleteStatement delete:
                    CompileDelete(delete);
                    break;
                    
                case GlobalStatement global:
                    CompileGlobal(global);
                    break;
                    
                case NonlocalStatement nonlocal:
                    CompileNonlocal(nonlocal);
                    break;
                    
                default:
                    throw PyNotImplementedError.Create($"Statement {statement.GetType().Name} not implemented");
            }
        }
        
        private void CompileExpression(Expression expression)
        {
            // Update current source location
            UpdateSourceLocation(expression);


            switch (expression)
            {
                case ConstantExpression constant:
                    EmitLoadConst(constant.Value);
                    break;
                    
                case NameExpression name:
                    EmitLoadName(name.Name);
                    break;
                    
                case WalrusExpression walrus:
                    // Compile value first
                    CompileExpression(walrus.Value);
                    // Duplicate value on stack for assignment
                    EmitInstruction(ByteCodeOp.COPY, 1);
                    // Store to variable using correct scope like CPython 3.12
                    EmitStoreVariable(walrus.Target);
                    // Value remains on stack as return value
                    break;

                case NamedExpression named:
                    // Compile value first
                    CompileExpression(named.Value);
                    // Duplicate value on stack for assignment
                    EmitInstruction(ByteCodeOp.COPY, 1);
                    // Store to target variable (should be NameExpression)
                    if (named.Target is NameExpression nameTarget)
                    {
                        // CPython 3.12: Walrus variables use the current symbol table to determine scope
                        // In inline comprehensions (PEP 709): STORE_FAST/STORE_GLOBAL
                        // In generator expressions: STORE_DEREF if FREE variable
#if DEBUG_COMPILER_LOG
                        Console.WriteLine($"      🔍 NamedExpression: {nameTarget.Name}");
                        Console.WriteLine($"      Symbol table: {_currentSymbolTable?.GetName()}, Type: {_currentSymbolTable?.Type}, Has symbol: {_currentSymbolTable?.GetSymbols().ContainsKey(nameTarget.Name)}");
#endif
                        // Check symbol scope in the CURRENT symbol table (not root _symbolTable!)
                        if (_currentSymbolTable != null && _currentSymbolTable.GetSymbols().TryGetValue(nameTarget.Name, out var symbol))
                        {
#if DEBUG_COMPILER_LOG
                            Console.WriteLine($"      Symbol scope: {symbol.Scope}, Table type: {_currentSymbolTable.Type}");
#endif
                            // Emit store instruction based on symbol scope AND symbol table type
                            switch (symbol.Scope)
                            {
                                case SymbolScope.Local:
                                    // CPython: LOCAL scope uses STORE_FAST if in function-like scope, STORE_NAME if in module
                                    if (_currentSymbolTable.Type == SymbolTableType.Function)
                                    {
                                        var localIndex = GetOrAddVarName(nameTarget.Name);
                                        EmitInstruction(ByteCodeOp.STORE_FAST, localIndex);
#if DEBUG_COMPILER_LOG
                                        Console.WriteLine($"      → Emitted STORE_FAST for {nameTarget.Name} (Function scope + LOCAL)");
#endif
                                    }
                                    else
                                    {
                                        // Module scope: use STORE_NAME
                                        var nameIndex = AddName(nameTarget.Name);
                                        EmitInstruction(ByteCodeOp.STORE_NAME, nameIndex);
#if DEBUG_COMPILER_LOG
                                        Console.WriteLine($"      → Emitted STORE_NAME for {nameTarget.Name} (Module scope + LOCAL)");
#endif
                                    }
                                    break;
                                case SymbolScope.Global:
                                    var globalIndex = AddName(nameTarget.Name);
                                    EmitInstruction(ByteCodeOp.STORE_GLOBAL, globalIndex);
#if DEBUG_COMPILER_LOG
                                    Console.WriteLine($"      → Emitted STORE_GLOBAL for {nameTarget.Name}");
#endif
                                    break;
                                case SymbolScope.Free:
                                    // CPython 3.12 PEP 709: In inlined comprehensions, walrus vars are stored
                                    // as parent's local variables using STORE_FAST
                                    if (_isInComprehension)
                                    {
                                        var localIndex = _varNames.IndexOf(nameTarget.Name);
                                        if (localIndex >= 0)
                                        {
                                            EmitInstruction(ByteCodeOp.STORE_FAST, localIndex);
#if DEBUG_COMPILER_LOG
                                            Console.WriteLine($"      → PEP 709: Emitted STORE_FAST for walrus {nameTarget.Name} (Free in comp, local in parent)");
#endif
                                            break;
                                        }
                                    }
                                    // Generator expressions: Free variables use STORE_DEREF
                                    EmitStoreDeref(nameTarget.Name);
#if DEBUG_COMPILER_LOG
                                    Console.WriteLine($"      → Emitted STORE_DEREF for {nameTarget.Name} (FREE variable in genexpr)");
#endif
                                    break;
                                case SymbolScope.Cell:
                                    // CPython 3.12: Cell variables use STORE_DEREF
                                    EmitStoreDeref(nameTarget.Name);
#if DEBUG_COMPILER_LOG
                                    Console.WriteLine($"      → Emitted STORE_DEREF for {nameTarget.Name} (CELL variable)");
#endif
                                    break;
                                default:
                                    EmitStoreVariable(nameTarget.Name);
#if DEBUG_COMPILER_LOG
                                    Console.WriteLine($"      → Fallback to EmitStoreVariable for {nameTarget.Name}");
#endif
                                    break;
                            }
                        }
                        else
                        {
#if DEBUG_COMPILER_LOG
                            Console.WriteLine($"      ⚠️ Symbol {nameTarget.Name} not found in current symbol table, using fallback");
#endif
                            // Fallback
                            EmitStoreVariable(nameTarget.Name);
                        }
                    }
                    else
                    {
                        throw new NotSupportedException($"NamedExpression target must be a name, got: {named.Target.GetType()}");
                    }
                    // Value remains on stack as return value
                    break;
                    
                case BinOpExpression binOp:
                    // CPython 3.12: Compile-time constant folding (compile.c)
                    // Try to fold if both operands are constants
                    if (binOp.Left is ConstantExpression leftConst &&
                        binOp.Right is ConstantExpression rightConst)
                    {
                        var foldedValue = TryFoldBinaryOpAtCompileTime(
                            leftConst.Value,
                            rightConst.Value,
                            binOp.OpNode);

                        if (foldedValue != null)
                        {
                            // Successfully folded - emit single LOAD_CONST
                            EmitLoadConst(foldedValue);
                            break;
                        }
                        // If folding failed (e.g., division by zero), fall through to normal compilation
                    }

                    // Normal compilation: emit both operands and operator
                    CompileExpression(binOp.Left);
                    CompileExpression(binOp.Right);
                    EmitBinaryOp(binOp.OpNode);
                    break;

                case UnaryOpExpression unaryOp:
                    CompileExpression(unaryOp.Operand);
                    EmitUnaryOp(unaryOp.OpNode);
                    break;
                    
                case CompareExpression compare:
                    // CPython 3.12: Compare now uses lists (ops, comparators)
                    if (compare.Ops.Count == 1)
                    {
                        // Simple comparison: left op comparator
                        CompileExpression(compare.Left);
                        CompileExpression(compare.Comparators[0]);
                        EmitCompareOp(compare.Ops[0]);
                    }
                    else
                    {
                        // Chained comparison: use CPython pattern with SWAP/COPY
                        CompileChainedComparisonFromCompare(compare);
                    }
                    break;

                case ChainedCompareExpression chainedCompare:
                    CompileChainedComparison(chainedCompare);
                    break;

                case BoolOpExpression boolOp:
                    CompileBoolOp(boolOp);
                    break;
                    
                case CallExpression call:
                    // CPython 3.12: Check for *args/**kwargs unpacking
                    // Performance: Eliminated LINQ
                    bool hasStarArgs = false;
                    foreach (var arg in call.Arguments)
                    {
                        if (arg is StarredExpression)
                        {
                            hasStarArgs = true;
                            break;
                        }
                    }
                    bool hasKwargUnpacking = false;
                    foreach (var kw in call.Keywords)
                    {
                        if (kw.Arg == null) // **kwargs has null Arg
                        {
                            hasKwargUnpacking = true;
                            break;
                        }
                    }

                    if (hasStarArgs || hasKwargUnpacking)
                    {
                        // Use CALL_FUNCTION_EX for unpacking
                        EmitInstruction(ByteCodeOp.PUSH_NULL);
                        CompileExpression(call.Function);

                        // Handle args: combine regular args with *args
                        // Performance: Eliminated LINQ
                        var regularArgs = new List<Expression>();
                        StarredExpression? starredArg = null;
                        foreach (var arg in call.Arguments)
                        {
                            if (arg is StarredExpression starred)
                            {
                                if (starredArg == null)
                                {
                                    starredArg = starred;
                                }
                            }
                            else
                            {
                                regularArgs.Add(arg);
                            }
                        }

                        if (regularArgs.Count > 0 || starredArg != null)
                        {
                            // Build list starting with regular arguments
                            if (regularArgs.Count > 0)
                            {
                                // Compile regular args in order and build list
                                foreach (var arg in regularArgs)
                                {
                                    CompileExpression(arg);
                                }
                                EmitInstruction(ByteCodeOp.BUILD_LIST, regularArgs.Count);
                            }
                            else
                            {
                                // Start with empty list if no regular args
                                EmitInstruction(ByteCodeOp.BUILD_LIST, 0);
                            }

                            // If there's *args, extend the list
                            if (starredArg != null)
                            {
                                CompileExpression(starredArg.Value);
                                EmitInstruction(ByteCodeOp.LIST_EXTEND, 1);
                            }

                            // Convert list to tuple for CALL_FUNCTION_EX
                            EmitInstruction(ByteCodeOp.CALL_INTRINSIC_1, (int)IntrinsicFunction.INTRINSIC_LIST_TO_TUPLE);
                        }
                        else
                        {
                            // No args at all, create empty tuple
                            EmitLoadConst(new PyTuple(new PyObject[0]));
                        }

                        // Handle **kwargs
                        // CPython 3.12: Python/compile.c:5066-5103 (compiler_call_helper)
                        // CALL_FUNCTION_EX expects: [func, args_tuple, kwargs_dict]
                        if (hasKwargUnpacking)
                        {
                            // CPython 3.12: First, create dict with regular keyword arguments using BUILD_MAP
                            // Performance: Eliminated LINQ
                            var regularKwargs = new List<KeywordExpression>();
                            foreach (var kw in call.Keywords)
                            {
                                if (kw.Arg != null)
                                {
                                    regularKwargs.Add(kw);
                                }
                            }

                            // CPython 3.12: BUILD_MAP with count of regular keyword args
                            // Stack: [key1, val1, key2, val2, ...] -> BUILD_MAP n -> [dict]
                            foreach (var kw in regularKwargs)
                            {
                                EmitLoadConst(new PyString(kw.Arg));  // key
                                CompileExpression(kw.Value);         // value
                            }
                            EmitInstruction(ByteCodeOp.BUILD_MAP, regularKwargs.Count);

                            // CPython 3.12: Then merge each **kwargs dict using DICT_MERGE
                            // Performance: Eliminated LINQ
                            foreach (var kwarg in call.Keywords)
                            {
                                if (kwarg.Arg == null)
                                {
                                    CompileExpression(kwarg.Value); // This should be a dict
                                    EmitInstruction(ByteCodeOp.DICT_MERGE, 1);
                                }
                            }

                            EmitInstruction(ByteCodeOp.CALL_FUNCTION_EX, 1); // 1 = has kwargs
                        }
                        else
                        {
                            EmitInstruction(ByteCodeOp.CALL_FUNCTION_EX, 0); // 0 = no kwargs
                        }
                    }
                    else
                    {
                        // Regular call without unpacking
                        // CPython 3.12: Use LOAD_GLOBAL with NULL push for global function calls
                        #if DEBUG_LOG
                        Console.WriteLine($"🔍 CallExpression: function type = {call.Function.GetType().Name}");
                        if (call.Function is NameExpression ne)
                        {
                            Console.WriteLine($"   NameExpression: {ne.Name}");
                        }
                        #endif
                        if (call.Function is NameExpression funcName)
                        {
                            // CPython 3.12: Check scope and use appropriate load instruction
                            #if DEBUG_LOG
                            Console.WriteLine($"   → Checking scope for function name: '{funcName.Name}'");
                            #endif

                            // Check if it's a local variable (LOAD_FAST) or free variable (LOAD_DEREF) in function scope
                            // CPython 3.12 PEP 709: Comprehensions are inlined but have their own scope
                            // For inlined comprehensions, also check parent scope if not found in current scope
                            bool isHandled = false;
                            if (_currentSymbolTable != null && (_isInFunction || _isInComprehension))
                            {
                                var symbol = _currentSymbolTable.Lookup(funcName.Name);
                                // CPython 3.12 PEP 709: If not found in comprehension scope, check parent scope
                                if (symbol == null && _isInComprehension && _currentSymbolTable.GetParent() != null)
                                {
                                    symbol = _currentSymbolTable.GetParent().Lookup(funcName.Name);
                                }
                                if (symbol != null)
                                {
#if DEBUG_COMPILER_LOG
                                    Console.WriteLine($"   → CompileCallExpression: symbol '{funcName.Name}' scope={symbol.Scope}, _varNames=[{string.Join(", ", _varNames)}]");
#endif
                                    if (symbol.Scope == SymbolScope.Local)
                                    {
                                        var localIndex = _varNames.IndexOf(funcName.Name);
#if DEBUG_COMPILER_LOG
                                        Console.WriteLine($"   → Looking for '{funcName.Name}' in _varNames, localIndex={localIndex}");
#endif
                                        if (localIndex >= 0)
                                        {
                                            isHandled = true;
                                            #if DEBUG_LOG
                                            Console.WriteLine($"   → Found as local variable, using PUSH_NULL + LOAD_FAST");
                                            #endif
                                            EmitInstruction(ByteCodeOp.PUSH_NULL);
                                            EmitInstruction(ByteCodeOp.LOAD_FAST, localIndex);
                                        }
                                    }
                                    else if (symbol.Scope == SymbolScope.Free)
                                    {
                                        // CPython 3.12 PEP 709: Inlined comprehensions share _varNames with enclosing function
                                        // Check _varNames first for comprehension context
                                        if (_isInComprehension)
                                        {
                                            var localIndex = _varNames.IndexOf(funcName.Name);
                                            if (localIndex >= 0)
                                            {
                                                isHandled = true;
                                                #if DEBUG_COMPILER_LOG
                                                Console.WriteLine($"   → PEP 709: Free var '{funcName.Name}' found in _varNames, using PUSH_NULL + LOAD_FAST");
                                                #endif
                                                EmitInstruction(ByteCodeOp.PUSH_NULL);
                                                EmitInstruction(ByteCodeOp.LOAD_FAST, localIndex);
                                            }
                                            // PEP 709: Also check _cellVars - parent function's cell variables are accessible
                                            if (!isHandled && _cellVars.Contains(funcName.Name))
                                            {
                                                var cellIndex = _cellVars.IndexOf(funcName.Name);
                                                isHandled = true;
                                                #if DEBUG_COMPILER_LOG
                                                Console.WriteLine($"   → PEP 709: Free var '{funcName.Name}' found in _cellVars, using PUSH_NULL + LOAD_DEREF");
                                                #endif
                                                EmitInstruction(ByteCodeOp.PUSH_NULL);
                                                EmitInstruction(ByteCodeOp.LOAD_DEREF, cellIndex);
                                            }
                                        }
                                        // Free variable: use PUSH_NULL + LOAD_DEREF
                                        if (!isHandled && _freeVars.Contains(funcName.Name))
                                        {
                                            var freeIndex = _freeVars.IndexOf(funcName.Name);
                                            isHandled = true;
                                            #if DEBUG_COMPILER_LOG
                                            Console.WriteLine($"   → Found as free variable, using PUSH_NULL + LOAD_DEREF");
                                            #endif
                                            EmitInstruction(ByteCodeOp.PUSH_NULL);
                                            EmitInstruction(ByteCodeOp.LOAD_DEREF, freeIndex);
                                        }
                                    }
                                    else if (symbol.Scope == SymbolScope.Cell)
                                    {
                                        // Cell variable: use PUSH_NULL + LOAD_DEREF
                                        // CPython 3.12: Use cellvar index directly (FixCellOffsets will remap to localsplus offset)
                                        if (_cellVars.Contains(funcName.Name))
                                        {
                                            var cellIndex = _cellVars.IndexOf(funcName.Name);
                                            isHandled = true;
                                            #if DEBUG_COMPILER_LOG
                                            Console.WriteLine($"   → Found as cell variable, using PUSH_NULL + LOAD_DEREF (cell index {cellIndex})");
                                            #endif
                                            EmitInstruction(ByteCodeOp.PUSH_NULL);
                                            EmitInstruction(ByteCodeOp.LOAD_DEREF, cellIndex);
                                        }
                                    }
                                }
                            }

                            if (!isHandled)
                            {
                                if (_isInFunction)
                                {
                                    // Function scope: use optimized LOAD_GLOBAL(pushNull=true)
                                    #if DEBUG_LOG
                                    Console.WriteLine($"   → Using EmitLoadGlobal('{funcName.Name}', pushNull: true)");
                                    #endif
                                    EmitLoadGlobal(funcName.Name, pushNull: true);
                                }
                                else
                                {
                                    // Module scope (including inlined comprehensions): CPython 3.12 uses PUSH_NULL + LOAD_NAME
                                    // CPython 3.12 PEP 709: Comprehensions at module level use LOAD_NAME for outer variables
                                    #if DEBUG_LOG
                                    Console.WriteLine($"   → Module scope, using PUSH_NULL + LOAD_NAME");
                                    #endif
                                    EmitInstruction(ByteCodeOp.PUSH_NULL);
                                    var nameIndex = AddName(funcName.Name);
                                    EmitInstruction(ByteCodeOp.LOAD_NAME, nameIndex);
                                }
                            }
                        }
                        else if (call.Function is AttributeExpression attrExpr)
                        {
                            // CPython 3.12: Check for super().method() call pattern
                            if (attrExpr.Value is CallExpression attrSuperCall &&
                                attrSuperCall.Function is NameExpression attrSuperName &&
                                attrSuperName.Name == "super" &&
                                attrSuperCall.Arguments.Count == 0)
                            {
                                // super().method() call: use LOAD_SUPER_ATTR
                                #if DEBUG_LOG
                                Console.WriteLine($"🔍 Detected super().{attrExpr.Attr}() call - generating LOAD_SUPER_ATTR + CALL");
                                #endif

                                // Load __class__ free variable using LOAD_DEREF
                                var classIndex = _freeVars.IndexOf("__class__");
                                #if DEBUG_LOG
                                Console.WriteLine($"🔍 Looking for __class__ in free variables: index={classIndex}, freeVars=[{string.Join(", ", _freeVars)}]");
                                #endif

                                if (classIndex >= 0)
                                {
                                    // Check if we have 'self' or 'cls' in local scope
                                    bool hasSelfParameter = _varNames.Count > 0 &&
                                                           (_varNames[0] == "self" || _varNames[0] == "cls");

                                    if (hasSelfParameter)
                                    {
                                        // CPython 3.12: LOAD_GLOBAL without NULL for LOAD_SUPER_ATTR
                                        EmitLoadGlobal("super", pushNull: false);
                                        // CPython 3.12: Use EmitLoadDeref to calculate correct localsplus offset
                                        // Free variable offset = ncellvars + freevar_index
                                        EmitLoadDeref("__class__");

                                        // Load self - first parameter (cls/self)
                                        EmitInstruction(ByteCodeOp.LOAD_FAST, 0);

                                        // LOAD_SUPER_ATTR with NULL|self flag for method call
                                        // CPython 3.12: oparg = (name_index << 1) | method_flag
                                        var attrNameIndex = GetOrAddName(attrExpr.Attr);
                                        var flags = (attrNameIndex << 1) | 1;  // name index in high bits, method flag=1
                                        EmitInstruction(ByteCodeOp.LOAD_SUPER_ATTR, flags);
                                    }
                                    else
                                    {
                                        // No self parameter - fall back to regular call
                                        #if DEBUG_LOG
                                        Console.WriteLine("⚠️  No self/cls parameter in current scope, using regular call");
                                        #endif
                                        CompileExpression(attrExpr.Value); // super()
                                        EmitLoadAttr(attrExpr.Attr, pushNull: true);
                                    }
                                }
                                else
                                {
                                    // No __class__ free variable - fall back to regular call
                                    #if DEBUG_LOG
                                    Console.WriteLine("⚠️  No __class__ free variable found, using regular call");
                                    #endif
                                    CompileExpression(attrExpr.Value); // super()
                                    EmitLoadAttr(attrExpr.Attr, pushNull: true);
                                }
                            }
                            else
                            {
                                // Regular method call with LOAD_ATTR(pushNull=true)
                                // This will push [self/NULL, method] for efficient method calls
                                #if DEBUG_LOG
                                Console.WriteLine($"   → Using LOAD_ATTR(pushNull=true) for method call");
                                #endif
                                CompileExpression(attrExpr.Value); // Load the object
                                EmitLoadAttr(attrExpr.Attr, pushNull: true); // Load attribute with method optimization
                            }
                        }
                        else
                        {
                            // Other expressions: use PUSH_NULL + expression
                            #if DEBUG_LOG
                            Console.WriteLine($"   → Using PUSH_NULL + CompileExpression");
                            #endif
                            EmitInstruction(ByteCodeOp.PUSH_NULL);
                            CompileExpression(call.Function);
                        }

                        // 위치 인수 컴파일
                        foreach (var arg in call.Arguments)
                        {
                            CompileExpression(arg);
                        }

                        // 키워드 인수가 있는 경우
                        if (call.Keywords.Count > 0)
                        {
                            // 키워드 인수 값들을 스택에 푸시
                            foreach (var keyword in call.Keywords)
                            {
                                CompileExpression(keyword.Value);
                            }

                            // CPython 3.12: Create keyword names tuple and add to constants
                            // Performance: Eliminated LINQ + Cache
                            var kwNames = new PyObject[call.Keywords.Count];
                            for (int i = 0; i < call.Keywords.Count; i++)
                            {
                                kwNames[i] = StringCache.GetOrCreate(call.Keywords[i].Arg ?? "");
                            }
                            var kwNamesTuple = TupleCache.GetOrCreate(kwNames);
                            var kwNamesIndex = GetOrAddConstant(kwNamesTuple);

                            // CPython 3.12: KW_NAMES + CALL pattern
                            EmitInstruction(ByteCodeOp.KW_NAMES, kwNamesIndex);
                            EmitInstruction(ByteCodeOp.CALL, call.Arguments.Count + call.Keywords.Count);
                        }
                        else
                        {
                            // 키워드 인수가 없는 경우 Python 3.12 방식
                            EmitInstruction(ByteCodeOp.CALL, call.Arguments.Count);
                        }
                    }
                    break;
                    
                case AttributeExpression attr:
                    // CPython 3.12: Check for super() calls and use LOAD_SUPER_ATTR
                    if (attr.Value is CallExpression superCall && 
                        superCall.Function is NameExpression superName && 
                        superName.Name == "super" && 
                        superCall.Arguments.Count == 0)
                    {
                        // super().method pattern: use LOAD_SUPER_ATTR
                        #if DEBUG_LOG
                        Console.WriteLine($"🔍 Detected super().{attr.Attr} - generating LOAD_DEREF + LOAD_SUPER_ATTR");
                        #endif
                        
                        // Load __class__ free variable using LOAD_DEREF
                        // Note: In methods, __class__ is a free variable, not a cell variable
                        var classIndex = _freeVars.IndexOf("__class__");
                        #if DEBUG_LOG
                        Console.WriteLine($"🔍 Looking for __class__ in free variables: index={classIndex}, freeVars=[{string.Join(", ", _freeVars)}]");
                        #endif
                        if (classIndex >= 0)
                        {
                            // Load super() (null + self)
                            // CPython 3.12: Check if we have 'self' or 'cls' in local scope
                            // In nested functions, this check will fail and we fallback to regular super()
                            bool hasSelfParameter = _varNames.Count > 0 &&
                                                   (_varNames[0] == "self" || _varNames[0] == "cls");

                            if (hasSelfParameter)
                            {
                                // CPython 3.12: LOAD_GLOBAL without NULL for LOAD_SUPER_ATTR
                                EmitLoadGlobal("super", pushNull: false);
                                // CPython 3.12: Use EmitLoadDeref to calculate correct localsplus offset
                                EmitLoadDeref("__class__");

                                // Load self - first parameter (cls/self)
                                EmitInstruction(ByteCodeOp.LOAD_FAST, 0);

                                // LOAD_SUPER_ATTR for value access (not method call)
                                // CPython 3.12: oparg = (name_index << 1) | method_flag
                                var attrNameIndex = GetOrAddName(attr.Attr);
                                var flags = (attrNameIndex << 1) | 0;  // name index in high bits, method flag=0
                                EmitInstruction(ByteCodeOp.LOAD_SUPER_ATTR, flags);
                            }
                            else
                            {
                                // No self parameter (e.g., nested function)
                                // CPython 3.12: This will fail at runtime with proper error message
                                #if DEBUG_LOG
                                Console.WriteLine("⚠️  No self/cls parameter in current scope, zero-arg super() will fail");
                                #endif

                                // Generate code that will call super() without arguments
                                // This will properly trigger "super(): no arguments" error at runtime
                                CompileExpression(attr.Value);
                                EmitLoadAttr(attr.Attr);
                            }
                        }
                        else
                        {
                            // Fallback to regular attribute access if no __class__ cell
                            #if DEBUG_LOG
                            Console.WriteLine("⚠️  No __class__ cell variable found, falling back to LOAD_ATTR");
                            #endif
                            CompileExpression(attr.Value);
                            EmitLoadAttr(attr.Attr);
                        }
                    }
                    else
                    {
                        // Regular attribute access
                        CompileExpression(attr.Value);
                        EmitLoadAttr(attr.Attr);
                    }
                    break;
                    
                case SubscriptExpression subscript:
                    CompileExpression(subscript.Value);

                    // CPython 3.12: SliceExpression은 BUILD_SLICE + BINARY_SUBSCR 사용
                    if (subscript.Slice is SliceExpression sliceExpr)
                    {
                        // Start 값 로드 (None이면 None)
                        if (sliceExpr.Start != null)
                            CompileExpression(sliceExpr.Start);
                        else
                            EmitLoadConst(PyNone.Instance);

                        // Stop 값 로드 (None이면 None)
                        if (sliceExpr.Stop != null)
                            CompileExpression(sliceExpr.Stop);
                        else
                            EmitLoadConst(PyNone.Instance);

                        // Step 값 로드 및 BUILD_SLICE 생성
                        if (sliceExpr.Step != null)
                        {
                            CompileExpression(sliceExpr.Step);
                            EmitInstruction(ByteCodeOp.BUILD_SLICE, 3); // 3 arguments: start, stop, step
                        }
                        else
                        {
                            EmitInstruction(ByteCodeOp.BUILD_SLICE, 2); // 2 arguments: start, stop
                        }

                        // CPython 3.12: 슬라이스 객체로 subscript 수행
                        EmitInstruction(ByteCodeOp.BINARY_SUBSCR);
                    }
                    else
                    {
                        // 일반 인덱싱 - CPython 3.12 uses BINARY_SUBSCR
                        CompileExpression(subscript.Slice);
                        EmitInstruction(ByteCodeOp.BINARY_SUBSCR);
                    }
                    break;
                    
                case ListExpression list:
                    // CPython 3.12 LIST_EXTEND optimization: when all elements are constants,
                    // use BUILD_LIST 0 + LOAD_CONST (tuple) + LIST_EXTEND 1
                    // Performance: Eliminated LINQ
                    bool allListElementsConstant = list.Elements.Count > 0;
                    foreach (var e in list.Elements)
                    {
                        if (!(e is ConstantExpression))
                        {
                            allListElementsConstant = false;
                            break;
                        }
                    }
                    if (allListElementsConstant)
                    {
                        // All elements are constants, use LIST_EXTEND optimization
                        EmitInstruction(ByteCodeOp.BUILD_LIST, 0); // Empty list

                        // Create tuple constant from all elements
                        // Performance: Eliminated LINQ
                        var constantElements = new PyObject[list.Elements.Count];
                        for (int i = 0; i < list.Elements.Count; i++)
                        {
                            constantElements[i] = ((ConstantExpression)list.Elements[i]).Value;
                        }
#if DEBUG_LOG
                        Console.WriteLine($"🔍 LIST_EXTEND 최적화: {constantElements.Length}개 상수 요소");
                        for (int i = 0; i < constantElements.Length; i++)
                        {
                            Console.WriteLine($"  constantElements[{i}] = {constantElements[i]} (타입: {constantElements[i]?.GetType().Name})");
                        }
#endif
                        var tupleConstant = TupleCache.GetOrCreate(constantElements);
#if DEBUG_LOG
                        Console.WriteLine($"🔍 tupleConstant 생성: {tupleConstant.Items.Length}개 아이템");
                        for (int i = 0; i < tupleConstant.Items.Length; i++)
                        {
                            Console.WriteLine($"  tupleConstant.Items[{i}] = {tupleConstant.Items[i]} (타입: {tupleConstant.Items[i]?.GetType().Name})");
                        }
#endif
                        EmitLoadConst(tupleConstant);
                        
                        // Extend the list with the tuple
                        EmitInstruction(ByteCodeOp.LIST_EXTEND, 1);
                    }
                    else
                    {
                        // Fallback to original method for non-constant elements
                        foreach (var element in list.Elements)
                        {
                            CompileExpression(element);
                        }
                        EmitInstruction(ByteCodeOp.BUILD_LIST, list.Elements.Count);
                    }
                    break;
                    
                case TupleExpression tuple:
                    // CPython 3.12: Constant folding for tuples with all constant elements
                    // Reference: CPython ast_opt.c fold_tuple() and make_const_tuple()
                    // No size restriction - fold any tuple where all elements are constants
                    // Performance: Eliminated LINQ
                    bool allTupleElementsConstant = tuple.Elements.Count > 0;
                    foreach (var e in tuple.Elements)
                    {
                        if (!(e is ConstantExpression))
                        {
                            allTupleElementsConstant = false;
                            break;
                        }
                    }
                    if (allTupleElementsConstant)
                    {
                        // All elements are constants, create tuple constant at compile time
                        // Performance: Eliminated LINQ
                        var constantValues = new PyObject[tuple.Elements.Count];
                        for (int i = 0; i < tuple.Elements.Count; i++)
                        {
                            constantValues[i] = ((ConstantExpression)tuple.Elements[i]).Value;
                        }
                        var tupleConstant = new PyTuple(constantValues);
                        EmitInstruction(ByteCodeOp.LOAD_CONST, GetOrAddConstant(tupleConstant));
                    }
                    else
                    {
                        // Non-constant elements: compile each element and BUILD_TUPLE
                        foreach (var element in tuple.Elements)
                        {
                            CompileExpression(element);
                        }
                        EmitInstruction(ByteCodeOp.BUILD_TUPLE, tuple.Elements.Count);
                    }
                    break;
                    
                case SetExpression set:
                    foreach (var element in set.Elements)
                    {
                        CompileExpression(element);
                    }
                    EmitInstruction(ByteCodeOp.BUILD_SET, set.Elements.Count);
                    break;
                    
                case DictExpression dict:
                    foreach (var kvp in dict.Items)
                    {
                        CompileExpression(kvp.Key);
                        CompileExpression(kvp.Value);
                    }
                    EmitInstruction(ByteCodeOp.BUILD_MAP, dict.Items.Count);
                    break;
                    
                case LambdaExpression lambda:
                    CompileLambda(lambda);
                    break;
                    
                case ConditionalExpression conditional:
                    CompileConditional(conditional);
                    break;
                    
                case AwaitExpression await:
                    CompileExpression(await.Value);
                    EmitInstruction(ByteCodeOp.GET_AWAITABLE);
                    break;
                    
                case FStringExpression fstring:
                    CompileFString(fstring);
                    break;
                    
                case FormattedValue formatted:
                    CompileFormattedValue(formatted);
                    break;

                case FormatExpression formatExpr:
                    // FormatExpression을 FormattedValue로 처리
                    var formattedValue = new FormattedValue(formatExpr.Value, formatExpr.FormatSpec);
                    CompileFormattedValue(formattedValue);
                    break;

                case FormatExpressionWithSpec formatExprWithSpec:
                    // CPython 3.12 호환: 중첩된 표현식을 포함한 포맷 지시자
                    CompileFormatExpressionWithSpec(formatExprWithSpec);
                    break;

                case StarredExpression starred:
                    CompileExpression(starred.Value);
                    // 별표 처리는 문맥에 따라 다름
                    break;

                case StarExpression star:
                    CompileExpression(star.Value);
                    // Star unpacking은 리스트/튜플 컨텍스트에서 처리됨
                    break;
                    
                // Type-aware optimized expressions (Phase 2)
                case TypeSpecializedBinaryOpExpression specializedBinOp:
                    CompileTypeSpecializedBinaryOp(specializedBinOp);
                    break;
                    
                case InlinedMethodCallExpression inlinedCall:
                    CompileInlinedMethodCall(inlinedCall);
                    break;
                    
                // Python 3.12 Type Parameters
                case TypeVarExpression typeVar:
                    EmitLoadConst(new PyString(typeVar.Name));
                    EmitInstruction(ByteCodeOp.CALL_INTRINSIC_1, (int)IntrinsicFunction.INTRINSIC_TYPEVAR);
                    break;
                    
                case ParamSpecExpression paramSpec:
                    EmitLoadConst(new PyString(paramSpec.Name));
                    EmitInstruction(ByteCodeOp.CALL_INTRINSIC_1, (int)IntrinsicFunction.INTRINSIC_PARAMSPEC);
                    break;
                    
                case TypeVarTupleExpression typeVarTuple:
                    EmitLoadConst(new PyString(typeVarTuple.Name));
                    EmitInstruction(ByteCodeOp.CALL_INTRINSIC_1, (int)IntrinsicFunction.INTRINSIC_TYPEVARTUPLE);
                    break;
                    
                // PEP 709 Comprehension Optimization - Bytecode inlining
                case ListComprehension listComp:
                    CompileListComprehension(listComp);
                    break;
                    
                case DictComprehension dictComp:
                    CompileDictComprehension(dictComp);
                    break;
                    
                case SetComprehension setComp:
                    CompileSetComprehension(setComp);
                    break;
                    
                case GeneratorExpression genExp:
                    CompileGeneratorExpression(genExp);
                    break;
                    
                case SliceExpression sliceExp:
                    CompileSliceExpression(sliceExp);
                    break;
                    
                case KeywordExpression keyword:
                    CompileKeywordExpression(keyword);
                    break;

                case JoinedStrExpression joinedStr:
#if DEBUG_LOG
                    Console.WriteLine($"[DEBUG] Compiler: Compiling JoinedStrExpression with {joinedStr.Values.Count} values");
#endif
                    CompileJoinedStrExpression(joinedStr);
                    break;

                case FormattedValueExpression formattedValueExpr:
#if DEBUG_LOG
                    Console.WriteLine($"[DEBUG] Compiler: Compiling FormattedValueExpression");
#endif
                    CompileFormattedValueExpression(formattedValueExpr);
                    break;

                case FStringFormattedValue fstringValue:
#if DEBUG_LOG
                    Console.WriteLine($"[DEBUG] Compiler: Compiling FStringFormattedValue");
#endif
                    CompileFStringFormattedValue(fstringValue);
                    break;

                case YieldExpression yieldExpr:
                    if (yieldExpr.Value != null)
                        CompileExpression(yieldExpr.Value);
                    else
                        EmitLoadConst(PyNone.Instance);
                    EmitInstruction(ByteCodeOp.YIELD_VALUE, 1); // CPython 3.12: yield_value argument 1
                    EmitInstruction(ByteCodeOp.RESUME, 1); // CPython 3.12: Resume after yield
                    // CPython 3.12: After RESUME, sent value is on stack
                    // For 'yield expr' used as statement, POP_TOP is added by ExpressionStatement handler
                    // For 'x = yield expr' used in assignment, sent value stays on stack for STORE
                    // So YieldExpression itself doesn't add POP_TOP
                    break;

                case YieldFromExpression yieldFromExpr:
                    // CPython 3.12: compile.c:6128-6131 (same pattern as YieldFromStatement)
                    // VISIT(c, expr, e->v.YieldFrom.value);
                    CompileExpression(yieldFromExpr.Value);
                    // ADDOP(c, loc, GET_YIELD_FROM_ITER);
                    EmitInstruction(ByteCodeOp.GET_YIELD_FROM_ITER);
                    // CPython: ADDOP_LOAD_CONST + ADD_YIELD_FROM
                    // IMPORTANT: LOAD_CONST None must be added INSIDE CompileYieldFrom
                    // so that sendLabel points to SEND, not LOAD_CONST
                    CompileYieldFrom(isAwait: false);
                    break;

                default:
                    throw PyNotImplementedError.Create($"Expression {expression.GetType().Name} not implemented");
            }
        }

        /// <summary>
        /// CPython 3.12 compatible f-string formatted value compilation
        /// Generates FORMAT_VALUE bytecode instruction
        /// </summary>
        private void CompileFStringFormattedValue(FStringFormattedValue fstringValue)
        {
            // Compile the value expression
            CompileExpression(fstringValue.Value);

            // Emit FORMAT_VALUE instruction
            int formatFlags = 0;
            if (fstringValue.Conversion.HasValue)
            {
                // CPython 3.12: conversion mapping
                // 's' (115) → 1, 'r' (114) → 2, 'a' (97) → 3
                int conversionFlag = fstringValue.Conversion.Value switch
                {
                    115 => 1, // 's' → str()
                    114 => 2, // 'r' → repr()
                    97 => 3,  // 'a' → ascii()
                    _ => 0
                };
                formatFlags |= conversionFlag;
            }
            if (fstringValue.FormatSpec != null)
            {
                CompileExpression(fstringValue.FormatSpec);
                formatFlags |= 4; // Has format spec
            }

            EmitInstruction(ByteCodeOp.FORMAT_VALUE, formatFlags);
#if DEBUG_LOG
            Console.WriteLine($"[DEBUG] Compiler: Emitted FORMAT_VALUE with flags: {formatFlags}");
#endif
        }

        /// <summary>
        /// CPython 3.12 compatible f-string compilation
        /// Compiles f-strings by concatenating all parts using BUILD_STRING
        /// </summary>
        private void CompileJoinedStrExpression(JoinedStrExpression joinedStr)
        {
#if DEBUG_LOG
            Console.WriteLine($"[DEBUG] Compiler: CompileJoinedStrExpression with {joinedStr.Values.Count} parts");
#endif

            // Compile each part of the f-string
            foreach (var value in joinedStr.Values)
            {
#if DEBUG_LOG
                Console.WriteLine($"[DEBUG] Compiler: Compiling f-string part: {value.GetType().Name}");
#endif
                CompileExpression(value);
            }

            // CPython 3.12: Only use BUILD_STRING if there are multiple parts
            // Single part f-strings don't need BUILD_STRING (e.g., f"{f'{x:03d}'}" → FORMAT_VALUE 4, FORMAT_VALUE 0)
            if (joinedStr.Values.Count > 1)
            {
                EmitInstruction(ByteCodeOp.BUILD_STRING, joinedStr.Values.Count);
#if DEBUG_LOG
                Console.WriteLine($"[DEBUG] Compiler: Emitted BUILD_STRING with {joinedStr.Values.Count} parts");
#endif
            }
#if DEBUG_LOG
            else
            {
                Console.WriteLine($"[DEBUG] Compiler: Skipped BUILD_STRING for single-value JoinedStr");
            }
#endif
        }

        /// <summary>
        /// CPython 3.12 compatible formatted value compilation
        /// Compiles the {expression} parts inside f-strings using FORMAT_VALUE
        /// CPython 3.12 FORMAT_VALUE encoding (oparg):
        ///   Bits 0-1: conversion (FVC_NONE=0, FVC_STR=1, FVC_REPR=2, FVC_ASCII=3)
        ///   Bit 2: format spec present (0=no, 1=yes)
        /// </summary>
        private void CompileFormattedValueExpression(FormattedValueExpression formattedValue)
        {
#if DEBUG_LOG
            Console.WriteLine($"[DEBUG] Compiler: CompileFormattedValueExpression");
            Console.WriteLine($"[DEBUG] Compiler: formattedValue.Conversion = {formattedValue.Conversion}");
#endif

            // Compile the inner expression
            CompileExpression(formattedValue.Value);

            // Emit FORMAT_VALUE instruction
            // CPython 3.12: FORMAT_VALUE oparg encoding
            //   Bits 0-1: conversion type
            //     - 0 (FVC_NONE): no conversion
            //     - 1 (FVC_STR): !s conversion (str())
            //     - 2 (FVC_REPR): !r conversion (repr())
            //     - 3 (FVC_ASCII): !a conversion (ascii())
            //   Bit 2: format spec present (4 if present, 0 if not)
            int formatFlags = 0;

            // Map character conversion codes to CPython format flags
            // 's' (115) -> 1, 'r' (114) -> 2, 'a' (97) -> 3, -1 -> 0
            if (formattedValue.Conversion != -1) // -1 means no conversion
            {
                int conversionFlag;
                if (formattedValue.Conversion == 's' || formattedValue.Conversion == 115)
                {
                    conversionFlag = 1; // FVC_STR
                }
                else if (formattedValue.Conversion == 'r' || formattedValue.Conversion == 114)
                {
                    conversionFlag = 2; // FVC_REPR
                }
                else if (formattedValue.Conversion == 'a' || formattedValue.Conversion == 97)
                {
                    conversionFlag = 3; // FVC_ASCII
                }
                else
                {
                    conversionFlag = 0; // Unknown conversion, treat as none
                }
                formatFlags |= conversionFlag; // Conversion in bits 0-1 (NO shift)
#if DEBUG_LOG
                Console.WriteLine($"[DEBUG] Compiler: Mapped conversion '{(char)formattedValue.Conversion}' ({formattedValue.Conversion}) -> flag {conversionFlag}");
#endif
            }

            if (formattedValue.FormatSpec != null)
            {
                // CPython 3.12: Format spec handling
                // If format spec is a simple constant string (JoinedStr with single Constant),
                // load it directly as LOAD_CONST instead of compiling as JoinedStr (which emits BUILD_STRING)
                if (formattedValue.FormatSpec is JoinedStrExpression joinedStr &&
                    joinedStr.Values.Count == 1 &&
                    joinedStr.Values[0] is ConstantExpression constExpr)
                {
                    // Simple format spec like "05d" or ".2f" - load as constant
                    int constIndex = GetOrAddConstant(constExpr.Value);
                    EmitInstruction(ByteCodeOp.LOAD_CONST, constIndex);
                }
                else
                {
                    // Complex format spec (e.g., with embedded expressions) - compile normally
                    CompileExpression(formattedValue.FormatSpec);
                }
                formatFlags |= 4; // CPython 3.12: Bit 2 = format spec present
            }

            EmitInstruction(ByteCodeOp.FORMAT_VALUE, formatFlags);

#if DEBUG_LOG
            Console.WriteLine($"[DEBUG] Compiler: Emitted FORMAT_VALUE with flags: {formatFlags}");
#endif
        }

        private void CompileFunction(FunctionDefStatement func)
        {
            // 모든 함수를 CompileNestedFunction으로 바이패스 (Phase 2 수정)
            CompileNestedFunction(func);
        }
        
        /// <summary>
        /// Phase 2: 중청 함수 컴파일 (자유 변수 지원)
        /// CPython 3.12 호환: Symbol Table 기반 스코프 분석
        /// </summary>
        private void CompileNestedFunction(FunctionDefStatement func)
        {
            #if DEBUG_LOG
            Console.WriteLine($"\n🔍 Compiling nested function: {func.Name}");
            #endif
            #if DEBUG_LOG
            Console.WriteLine($"  📍 Current symbol table context: {_currentSymbolTable?.Name}");
            #endif

            // CPython 3.12: Check if this function should be compiled at module level
            if (_currentSymbolTable != null)
            {
                var functionSymbol = _currentSymbolTable.Lookup(func.Name);
                if (functionSymbol != null && functionSymbol.Scope == SymbolScope.Global)
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"  → Function {func.Name} is at module level (Global scope)");
                    #endif
                    // This should not happen if we're inside a class
                    if (_currentSymbolTable.Type == SymbolTableType.Class)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"  ⚠️ WARNING: Function {func.Name} marked as global but we're in class context");
                        #endif
                        #if DEBUG_LOG
                        Console.WriteLine($"  → Skipping compilation - should be handled at module level");
                        #endif
                        return;
                    }
                }
            }

            // PEP 695: Check if function has type parameters
            if (func.TypeParams != null && func.TypeParams.Count > 0)
            {
                #if DEBUG_LOG
                Console.WriteLine($"  → PEP 695 Generic function with type parameters: [{string.Join(", ", func.TypeParams)}]");
                #endif
                CompileGenericFunction(func);
                return;
            }

            // CPython 3.12: Use symbol table for scope analysis
            List<string> freeVars = new List<string>();
            List<string> cellVars = new List<string>();

            // Find function's symbol table
            SymbolTable? funcSymbolTable = null;
            SymbolTable? savedSymbolTable = null;
            if (_currentSymbolTable != null)
            {
                // First check if _currentSymbolTable itself is the function's symbol table
                // (e.g., for generator expressions where SetSymbolTableContext was already called)
                if (_currentSymbolTable.Name == func.Name ||
                    _currentSymbolTable.Name == $"<function:{func.Name}>")
                {
                    funcSymbolTable = _currentSymbolTable;
                }
                else
                {
                    // Otherwise, look for symbol table in children with function name format
                    funcSymbolTable = _currentSymbolTable.Children.FirstOrDefault(child =>
                        child.Name == $"<function:{func.Name}>" || child.Name == func.Name);
                }

                #if DEBUG_LOG
                Console.WriteLine($"  🔍 Looking for symbol table for function {func.Name}");
                #endif
                #if DEBUG_LOG
                Console.WriteLine($"    Current symbol table: {_currentSymbolTable.Name}");
                #endif
                #if DEBUG_LOG
                Console.WriteLine($"    Children count: {_currentSymbolTable.Children.Count()}");
                #endif
                foreach (var child in _currentSymbolTable.Children)
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"      Child: {child.Name}");
                    #endif
                }

                if (funcSymbolTable != null)
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"    ✅ Found symbol table: {funcSymbolTable.Name}");
                    #endif
                    var funcFreeVars = funcSymbolTable.FindFreeVariables();
                    var funcCellVars = funcSymbolTable.FindCellVariables();

                    freeVars.AddRange(funcFreeVars);
                    cellVars.AddRange(funcCellVars);

                    #if DEBUG_LOG
                    Console.WriteLine($"  Symbol table analysis - Free vars: [{string.Join(", ", freeVars)}]");
                    #endif
                    #if DEBUG_LOG
                    Console.WriteLine($"  Symbol table analysis - Cell vars: [{string.Join(", ", cellVars)}]");
                    #endif

                    // CPython 3.12: 중첩 함수를 위한 자유 변수 전파
                    var nestedFreeVars = CollectNestedFreeVariables(funcSymbolTable);
                    foreach (var nestedVar in nestedFreeVars)
                    {
                        if (!freeVars.Contains(nestedVar) && !cellVars.Contains(nestedVar))
                        {
                            freeVars.Add(nestedVar);
                            #if DEBUG_LOG
                            Console.WriteLine($"  → Added nested free var: {nestedVar}");
                            #endif
                        }
                    }

                    // CPython 3.12: If function or its nested functions contain super() calls, ensure __class__ is in freeVars
                    // This handles cases like: def method(self): def inner(): return super().__repr__()
                    if (ContainsSuperCalls(func.Body) && !freeVars.Contains("__class__"))
                    {
                        freeVars.Add("__class__");
                        #if DEBUG_LOG
                        Console.WriteLine($"  ✅ Added __class__ to freeVars due to super() calls in function or nested functions (symbol table path)");
                        #endif
                    }

                    // CPython 3.12: Save current symbol table context BEFORE switching to function scope
                    // Python/compile.c:2227 - compiler_enter_scope is called AFTER compiler_default_arguments
                    // Default arguments must be compiled in the ENCLOSING scope (module/class/function),
                    // NOT in the function's own scope!
                    savedSymbolTable = _currentSymbolTable;

                    #if DEBUG_LOG
                    Console.WriteLine($"  📍 About to switch symbol table context: {savedSymbolTable?.Name} → {funcSymbolTable.Name}");
                    Console.WriteLine($"  ⚠️ IMPORTANT: This switch will happen AFTER compiling default arguments");
                    #endif
                }
                else
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"  ⚠️ Warning: No symbol table found for function {func.Name}");
                    #endif
                    // Fallback to old method
                    var analyzer = new FreeVariableAnalyzer();
                    #if DEBUG_LOG
                    Console.WriteLine($"  DEBUG: Current _varNames: [{string.Join(", ", _varNames)}]");
                    #endif
                    var (oldFreeVars, oldCellVars) = analyzer.AnalyzeNestedFunction(func, _varNames);
                    freeVars.AddRange(oldFreeVars);
                    cellVars.AddRange(oldCellVars);

                    // CPython 3.12: If function contains super() calls, ensure __class__ is in freeVars
                    if (ContainsSuperCalls(func.Body) && !freeVars.Contains("__class__"))
                    {
                        freeVars.Add("__class__");
                        #if DEBUG_LOG
                        Console.WriteLine($"  ✅ Added __class__ to freeVars due to super() calls");
                        #endif
                    }
                }
            }

            #if DEBUG_LOG
            Console.WriteLine($"  Free variables: [{string.Join(", ", freeVars)}]");
            #endif
            #if DEBUG_LOG
            Console.WriteLine($"  Cell variables: [{string.Join(", ", cellVars)}]");
            #endif

            // CPython 3.12: 내부 함수의 nonlocal 선언을 고려한 추가 cell 분석
            var additionalCellVars = new List<string>(cellVars);
            var allFreeVars = new HashSet<string>();
            
            // 함수 본문에서 지역 변수들을 먼저 수집
            var localVarNames = new List<string>(func.Parameters);
            CollectLocalVariables(func.Body, localVarNames);
            
            #if DEBUG_LOG
            Console.WriteLine($"  Local variables found: [{string.Join(", ", localVarNames)}]");
            #endif
            
            // 내부 함수들의 nonlocal 선언을 기반으로 cell 변수 추가 분석
            AnalyzeFunctionClosures(func.Body, localVarNames, allFreeVars, additionalCellVars);
            
            // 업데이트된 cell 변수들 사용
            cellVars = additionalCellVars;
            #if DEBUG_LOG
            Console.WriteLine($"  Updated Cell variables: [{string.Join(", ", cellVars)}]");
            #endif
            
            // 2. 매개변수와 기본값 파싱 (CPython 3.12: use Arguments instead of Parameters)
            var (paramNames, defaultExprs, kwDefaultExprs, flags, argCount, posonlyArgCount, kwonlyArgCount, annotations) = ParseFunctionArguments(func.Arguments);

            // CPython 3.12: Set CO_GENERATOR/CO_COROUTINE flags from symbol table
            // compile.c:7428-7433 - Read ste->ste_generator and ste->ste_coroutine
#if DEBUG_COMPILER_LOG
            Console.WriteLine($"  🔍 funcSymbolTable={funcSymbolTable?.Name ?? "null"}, IsGenerator={funcSymbolTable?.IsGenerator}, IsCoroutine={funcSymbolTable?.IsCoroutine}");
#endif
            if (funcSymbolTable != null)
            {
                if (funcSymbolTable.IsGenerator && !funcSymbolTable.IsCoroutine)
                {
                    flags |= PyCodeObject.CO_GENERATOR;
#if DEBUG_COMPILER_LOG
                    Console.WriteLine($"  ✅ Set CO_GENERATOR flag (SymbolTable.IsGenerator=true), flags=0x{flags:X}");
#endif
                }
                else if (!funcSymbolTable.IsGenerator && funcSymbolTable.IsCoroutine)
                {
                    flags |= PyCodeObject.CO_COROUTINE;
#if DEBUG_COMPILER_LOG
                    Console.WriteLine($"  ✅ Set CO_COROUTINE flag (SymbolTable.IsCoroutine=true)");
#endif
                }
                else if (funcSymbolTable.IsGenerator && funcSymbolTable.IsCoroutine)
                {
                    flags |= PyCodeObject.CO_ASYNC_GENERATOR;
#if DEBUG_COMPILER_LOG
                    Console.WriteLine($"  ✅ Set CO_ASYNC_GENERATOR flag (both IsGenerator and IsCoroutine)");
#endif
                }
            }

            #if DEBUG_LOG
            Console.WriteLine($"🔍 Function {func.Name}: annotations = {annotations.Count}");
            foreach (var ann in annotations)
            {
                Console.WriteLine($"   {ann.Key} -> {ann.Value}");
            }
            #endif

            // Evaluate default expressions to PyObjects (defaults are evaluated at function definition time)
            var defaults = new List<PyObject>();
            foreach (var defaultExpr in defaultExprs)
            {
                if (defaultExpr is ConstantExpression constExpr)
                {
                    defaults.Add(constExpr.Value);
                }
                else
                {
                    // For complex expressions, we need to compile and evaluate them
                    // But for now, this will be handled by compiling them as bytecode below
                    defaults.Add(PyNone.Instance); // Placeholder, will be replaced below
                }
            }

            // Evaluate keyword-only default expressions
            var kwDefaults = new List<PyObject>();
            foreach (var kwDefaultExpr in kwDefaultExprs)
            {
                if (kwDefaultExpr == null)
                {
                    kwDefaults.Add(PyNone.Instance); // No default for this kwonly arg
                }
                else if (kwDefaultExpr is ConstantExpression constExpr)
                {
                    kwDefaults.Add(constExpr.Value);
                }
                else
                {
                    kwDefaults.Add(PyNone.Instance); // Placeholder
                }
            }

            // 3. Return type annotation 처리 (CPython 3.12)
            #if DEBUG_LOG
            Console.WriteLine($"🔍 func.ReturnTypeAnnotation: {func.ReturnTypeAnnotation?.ToString() ?? "null"}");
            #endif
            if (func.ReturnTypeAnnotation != null)
            {
                // CPython 3.12: Store annotation Expression (not string) for compilation
                annotations["return"] = func.ReturnTypeAnnotation;
                #if DEBUG_LOG
                Console.WriteLine($"  → Added return annotation Expression: {func.ReturnTypeAnnotation.GetType().Name}");
                #endif
            }
            #if DEBUG_LOG
            Console.WriteLine($"  → Total annotations: {annotations.Count}");
            foreach (var kv in annotations)
            {
                Console.WriteLine($"      {kv.Key}: {kv.Value.GetType().Name}");
            }
            #endif
            
            // 3. CPython 3.12 compatible: Multi-level closure chain analysis
            if (freeVars.Count == 0)
            {
                #if DEBUG_LOG
                Console.WriteLine($"🔧 Multi-level closure analysis for function: {func.Name}");
                #endif

                // Analyze function body to find all referenced variables
                var referencedVars = new HashSet<string>();
                foreach (var stmt in func.Body)
                {
                    if (stmt is ReturnStatement retStmt && retStmt.Value is TupleExpression tuple)
                    {
                        foreach (var element in tuple.Elements)
                        {
                            if (element is NameExpression nameExpr)
                            {
                                referencedVars.Add(nameExpr.Name);
                            }
                        }
                    }
                }
                #if DEBUG_LOG
                Console.WriteLine($"   Referenced variables: [{string.Join(", ", referencedVars)}]");
                #endif

                // Variables defined locally in this function
                var localVars = new HashSet<string>(func.Parameters);
                foreach (var stmt in func.Body)
                {
                    if (stmt is AssignStatement assign)
                        // CPython 3.12: Extract names from targets
                        foreach (var target in assign.Targets)
                        {
                            if (target is NameExpression nameExpr && !localVars.Contains(nameExpr.Name))
                                localVars.Add(nameExpr.Name);
                        }
                }
                #if DEBUG_LOG
                Console.WriteLine($"   Local variables: [{string.Join(", ", localVars)}]");
                #endif

                // Build complete chain of available outer variables
                var availableOuterVars = new HashSet<string>();
                availableOuterVars.UnionWith(_varNames);    // Current function's locals
                availableOuterVars.UnionWith(_cellVars);   // Current function's cells
                availableOuterVars.UnionWith(_freeVars);   // Current function's free vars
                #if DEBUG_LOG
                Console.WriteLine($"   Available from outer scopes: [{string.Join(", ", availableOuterVars)}]");
                #endif

                // CPython 3.12: Check for zero-argument super() calls and add __class__ as referenced variable
                // NOTE: We check ContainsSuperCalls() first, then add __class__ even if not in availableOuterVars
                // This handles the case where we're compiling a method inside a class body that has __class__ cell
                if (ContainsSuperCalls(func.Body))
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"   Function contains super() calls - adding __class__ as referenced variable");
                    #endif
                    referencedVars.Add("__class__");
                    // Also add __class__ to availableOuterVars so it can be resolved as a free variable
                    availableOuterVars.Add("__class__");
                }

                // Variables that are referenced but not defined locally become free variables
                foreach (var refVar in referencedVars)
                {
                    if (!localVars.Contains(refVar) && availableOuterVars.Contains(refVar))
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"   Adding '{refVar}' as free variable (available in outer scope)");
                        #endif
                        freeVars.Add(refVar);
                    }
                    else if (!localVars.Contains(refVar))
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"   Variable '{refVar}' referenced but not available - will try LOAD_GLOBAL");
                        #endif
                    }
                }
            }

            // 3. 코드 객체 컴파일 (자유 변수 정보와 기본값 포함)
            var compiler = new PythonCompiler();
            compiler.SetupClosureCompilation(cellVars, freeVars); // 셀 변수와 자유 변수 설정

            // CPython 3.12: Pass source location information to nested compiler
            compiler.SetSourceLocation(_currentFileName, _sourceLines);
            #if DEBUG_LOG
            Console.WriteLine($"  📤 Passed source location to nested compiler: {_currentFileName}");
            #endif

            // CPython 3.12: Python/compile.c:2361 - compiler_enter_scope
            // The nested compiler should use the FUNCTION's symbol table as its context,
            // NOT the parent's symbol table. This is critical for nested functions to find
            // free variables correctly.
            if (funcSymbolTable != null)
            {
                compiler.SetSymbolTableContext(funcSymbolTable);
                #if DEBUG_LOG
                Console.WriteLine($"  📤 Passed symbol table context to nested compiler: {funcSymbolTable.Name}");
                #endif
            }

            // CPython 3.12: 루트 심볼 테이블도 전달 (클래스 내 함수 컴파일을 위해 필요)
            if (_symbolTable != null)
            {
                compiler.SetRootSymbolTable(_symbolTable);
                #if DEBUG_LOG
                Console.WriteLine($"  📤 Passed root symbol table to nested compiler: {_symbolTable.Name}");
                #endif
            }

            // CPython 3.12: Pass symbol table builder for PySymtable_Lookup
            // Corresponds to c->c_st in compile.c - needed to lookup symbol tables by AST node
            if (_symbolTableBuilder != null)
            {
                compiler._symbolTableBuilder = _symbolTableBuilder;
                #if DEBUG_LOG
                Console.WriteLine($"  📤 Passed symbol table builder to nested compiler for PySymtable_Lookup");
                #endif
            }
            // CPython 3.12: Use CompilerFunctionBody instead of CompileWithClosureAndDefaults
            var funcCode = compiler.CompilerFunctionBody(func.Body, func.Name, paramNames, defaults, kwDefaults, freeVars, cellVars, flags, argCount, posonlyArgCount, kwonlyArgCount);

            // CPython 3.12: compiler_function pattern
            // Step 1: Load decorators (bottom-to-top order)
            CompilerDecorators(func.Decorators);

            // Step 2: Compile default arguments and get funcflags
            // Extract kwonly argument names for BUILD_CONST_KEY_MAP
            // Performance: Eliminated LINQ
            var kwOnlyArgNames = new List<string>();
            foreach (var arg in func.Arguments.KwOnlyArgs)
            {
                kwOnlyArgNames.Add(arg.Name);
            }
            int makeFunctionFlags = CompilerDefaultArguments(defaultExprs, kwDefaultExprs, kwOnlyArgNames);

            // Step 3: Compile annotations and update funcflags
            if (CompilerVisitAnnotations(annotations) > 0)
                makeFunctionFlags |= MakeFunctionFlags.ANNOTATIONS;

            // Step 4: Create closure and emit MAKE_FUNCTION
            CompilerMakeClosure(funcCode, freeVars, makeFunctionFlags);

            // Step 5: Apply decorators (calls them in forward order)
            CompilerApplyDecorators(func.Decorators);

            // Step 6: Store the final function (decorated or original)
            EmitStoreName(func.Name);

            // CPython 3.12: Restore previous symbol table context
            if (funcSymbolTable != null)
            {
                _currentSymbolTable = savedSymbolTable;
                #if DEBUG_LOG
                Console.WriteLine($"  🔄 Symbol table context restored: {funcSymbolTable.Name} → {savedSymbolTable?.Name}");
                #endif
            }
        }
        
        /// <summary>
        /// 함수 내의 모든 중청 함수를 분석하여 셀이 필요한 변수들을 찾는다
        /// CPython 3.12 호환: nonlocal 변수도 고려
        /// </summary>
        private void AnalyzeFunctionClosures(List<Statement> statements, List<string> parameters, 
                                           HashSet<string> allFreeVars, List<string> cellVars)
        {
            foreach (var statement in statements)
            {
                if (statement is FunctionDefStatement nestedFunc)
                {
                    // 중청 함수 발견 - 자유 변수 분석
                    var analyzer = new FreeVariableAnalyzer();
                    var (freeVars, _) = analyzer.AnalyzeNestedFunction(nestedFunc, parameters);
                    
                    foreach (var freeVar in freeVars)
                    {
                        allFreeVars.Add(freeVar);

                        // 현재 함수에서 실제로 할당/정의된 변수인지 Symbol Table에서 확인
                        var symbol = _currentSymbolTable?.Lookup(freeVar);
                        if (symbol != null && symbol.IsAssigned() &&
                            (symbol.Scope == SymbolScope.Local || symbol.Scope == SymbolScope.Cell) &&
                            !cellVars.Contains(freeVar))
                        {
                            cellVars.Add(freeVar);
                            #if DEBUG_LOG
                            Console.WriteLine($"🔍 Variable '{freeVar}' needs cell (referenced by nested function '{nestedFunc.Name}') - assigned in current scope");
                            #endif
                        }
                        else if (symbol != null)
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"🔍 Skipping '{freeVar}' - scope: {symbol.Scope}, assigned: {symbol.IsAssigned()} - not owner");
                            #endif
                        }
                    }
                    
                    // CPython 3.12: 중첩 함수의 nonlocal 선언도 확인
                    var nonlocalVars = AnalyzeNonlocalDeclarations(nestedFunc.Body);
                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 Found nonlocal vars in '{nestedFunc.Name}': [{string.Join(", ", nonlocalVars)}]");
                    #endif
                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 Current parameters: [{string.Join(", ", parameters)}]");
                    #endif
                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 Current _varNames: [{string.Join(", ", _varNames)}]");
                    #endif
                    
                    foreach (var nonlocalVar in nonlocalVars)
                    {
                        // nonlocal 변수가 현재 함수의 지역변수나 매개변수라면 cell로 만들어야 함
                        if ((parameters.Contains(nonlocalVar) || _varNames.Contains(nonlocalVar)) && !cellVars.Contains(nonlocalVar))
                        {
                            cellVars.Add(nonlocalVar);
                            #if DEBUG_LOG
                            Console.WriteLine($"🔗 Variable '{nonlocalVar}' needs cell (nonlocal in nested function '{nestedFunc.Name}')");
                            #endif
                        }
                        else
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"⚠️ Nonlocal variable '{nonlocalVar}' not found in current scope");
                            #endif
                        }
                    }
                    
                    // 재귀적으로 중청 함수들도 분석
                    AnalyzeFunctionClosures(nestedFunc.Body, nestedFunc.Parameters, allFreeVars, cellVars);
                }
            }
        }
        
        /// <summary>
        /// 함수 본문에서 nonlocal 선언된 변수들을 찾는다
        /// </summary>
        private HashSet<string> AnalyzeNonlocalDeclarations(List<Statement> statements)
        {
            var nonlocalVars = new HashSet<string>();
            
            foreach (var statement in statements)
            {
                if (statement is NonlocalStatement nonlocal)
                {
                    foreach (var name in nonlocal.Names)
                    {
                        nonlocalVars.Add(name);
                    }
                }
                // 중첩된 블록도 확인 (if, while, for 등)
                // 여기서는 간단히 FunctionDef만 확인
                else if (statement is FunctionDefStatement nestedFunc)
                {
                    var nestedNonlocals = AnalyzeNonlocalDeclarations(nestedFunc.Body);
                    foreach (var nonlocalVar in nestedNonlocals)
                    {
                        nonlocalVars.Add(nonlocalVar);
                    }
                }
            }
            
            return nonlocalVars;
        }
        
        /// <summary>
        /// 함수 본문에서 지역 변수 이름들을 수집한다 (할당문 기반)
        /// </summary>
        private void CollectLocalVariables(List<Statement> statements, List<string> localVars)
        {
            #if DEBUG_LOG
            Console.WriteLine($"🔍 CollectLocalVariables: Analyzing {statements.Count} statements");
            #endif
            foreach (var statement in statements)
            {
                #if DEBUG_LOG
                Console.WriteLine($"  → Statement type: {statement.GetType().Name}");
                #endif
                switch (statement)
                {
                    case AssignStatement assign:
                        #if DEBUG_LOG
                        Console.WriteLine($"    → Found AssignStatement with {assign.Targets.Count} targets");
                        #endif
                        // CPython 3.12: Extract all target names from the targets list
                        foreach (var target in assign.Targets)
                        {
                            if (target is NameExpression nameExpr)
                            {
                                if (!localVars.Contains(nameExpr.Name))
                                {
                                    localVars.Add(nameExpr.Name);
                                }
                            }
                        }
                        break;

                    case FunctionDefStatement nestedFunc:
                        #if DEBUG_LOG
                        Console.WriteLine($"    → Found nested function: {nestedFunc.Name}");
                        #endif
                        // CPython 3.12: The nested function name IS a local variable in the enclosing scope
                        // Python/compile.c: def inner(): ... makes 'inner' a local in the outer function
                        if (!localVars.Contains(nestedFunc.Name))
                        {
                            localVars.Add(nestedFunc.Name);
                        }
                        break;
                        
                    // TODO: 다른 statement 타입들에서 변수 할당 확인 가능
                }
            }
        }
        
        // 바이트코드 생성 도우미들
        private void UpdateSourceLocation(ASTNode node)
        {
            if (node.LineNo >= 0)
                _currentLineNumber = node.LineNo;
            if (node.ColOffset >= 0)
                _currentColumnOffset = node.ColOffset;
        }
        
        // CPython 3.12: Exception handler info removed from InstructionSequence
        // Will be set later in flowgraph.cs during CFG building

        private void EmitInstruction(ByteCodeOp opCode, int argument = 0)
        {
            // CPython 3.12: Use InstructionSequence API (CFG-based compilation)
            // Exception handler info is NOT stored here, set later in CFG phase
#if DEBUG_COMPILER_LOG
            if (opCode != ByteCodeOp.CACHE && opCode != ByteCodeOp.NOP)  // Reduce noise
            {
                Console.WriteLine($"   → [CFG] {opCode} (arg={argument})");
            }
#endif
            try
            {
                if (argument > 0)
                {
                    _instructionSequence.AddOpWithArg(
                        opCode,
                        argument,
                        _currentLineNumber,
                        _currentColumnOffset,
                        _currentFileName
                    );
                }
                else
                {
                    _instructionSequence.AddOp(
                        opCode,
                        _currentLineNumber,
                        _currentColumnOffset,
                        _currentFileName
                    );
                }

                // CPython 3.12: CACHE entries are handled by PyAssemble
                // No need to emit CACHE as separate instructions
            }
            catch (Exception ex)
            {
#if DEBUG_LOG
                Console.WriteLine($"💥 EmitInstruction 에러: {ex.Message}");
                Console.WriteLine($"   opCode: {opCode}, argument: {argument}");
                Console.WriteLine($"   Stack trace: {ex.StackTrace}");
#endif
                throw;
            }
        }
        
        private void EmitLoadConst(PyObject value)
        {
            try
            {
                var index = GetOrAddConstant(value);
#if DEBUG_LOG
                Console.WriteLine($"🔍 EmitLoadConst: 인덱스 {index}에 값 {value} (타입: {value?.GetType().Name}) 저장");
                Console.WriteLine($"🔍 현재 Constants 배열 크기: {_constants.Count}");
#endif
                EmitInstruction(ByteCodeOp.LOAD_CONST, index);
            }
            catch (Exception ex)
            {
#if DEBUG_LOG
                Console.WriteLine($"💥 EmitLoadConst 에러: {ex.Message}");
                Console.WriteLine($"   value: {value}");
                Console.WriteLine($"   _constants null? {_constants == null}");
                Console.WriteLine($"   _instructionSequence null? {_instructionSequence == null}");
                Console.WriteLine($"   Stack trace: {ex.StackTrace}");
#endif
                throw;
            }
        }
        
        private void EmitLoadName(string name)
        {
            // Phase 2: 클로저 지원 - 자유 변수 처리 개선

#if DEBUG_COMPILER_LOG
            Console.WriteLine($"  🔧 EmitLoadName('{name}') called. Function: {_currentFunctionName}");
            Console.WriteLine($"     _currentSymbolTable: {_currentSymbolTable?.Name ?? "NULL"}");
#endif

            // 0. CPython 3.12: Symbol Table을 먼저 확인 (우선순위)
            if (_currentSymbolTable != null)
            {
                var symbol = _currentSymbolTable.Lookup(name);
                // CPython 3.12 PEP 709: If not found in comprehension scope, check parent scope
                if (symbol == null && _isInComprehension && _currentSymbolTable.GetParent() != null)
                {
                    symbol = _currentSymbolTable.GetParent().Lookup(name);
                }
                if (symbol != null)
                {
                    #if DEBUG_COMPILER_LOG
                    Console.WriteLine($"    🔍 Symbol Table lookup: {name} → Scope: {symbol.Scope}");
                    #endif

                    // Symbol Table에서 분석된 스코프에 따라 적절한 명령어 생성
                    switch (symbol.Scope)
                    {
                        case SymbolScope.Free:
                        case SymbolScope.Cell:
                            // CPython 3.12: Python/compile.c:4076-4160 (compiler_nameop)
                            // Class body에서는 enclosing scope 변수를 LOAD_NAME으로 처리
                            // (FREE/CELL이 아님)
                            if (_isInClassBody)
                            {
                                #if DEBUG_COMPILER_LOG
                                Console.WriteLine($"      → Class body: Using LOAD_NAME instead of LOAD_DEREF for {name}");
                                #endif
                                var nameIndex = AddName(name);
                                EmitInstruction(ByteCodeOp.LOAD_NAME, nameIndex);
                                return;
                            }

                            // CPython 3.12 PEP 709: Inlined comprehensions share _varNames with enclosing scope
                            // Check if this "free" variable is actually in _varNames (from outer comprehension)
                            if (_isInComprehension)
                            {
                                var localIndex = _varNames.IndexOf(name);
                                if (localIndex >= 0)
                                {
                                    #if DEBUG_COMPILER_LOG
                                    Console.WriteLine($"      → PEP 709: Free var '{name}' found in _varNames, using LOAD_FAST");
                                    #endif
                                    EmitInstruction(ByteCodeOp.LOAD_FAST, localIndex);
                                    return;
                                }
                                // PEP 709: Also check _cellVars - parent function's cell variables are accessible
                                if (_cellVars.Contains(name))
                                {
                                    var cellIndex = _cellVars.IndexOf(name);
                                    #if DEBUG_COMPILER_LOG
                                    Console.WriteLine($"      → PEP 709: Free var '{name}' found in _cellVars, using LOAD_DEREF");
                                    #endif
                                    EmitInstruction(ByteCodeOp.LOAD_DEREF, cellIndex);
                                    return;
                                }
                            }

                            // Free/Cell variable: Use EmitLoadDeref which calculates correct localsplus offset
                            #if DEBUG_COMPILER_LOG
                            Console.WriteLine($"      → Symbol is {symbol.Scope}. Calling EmitLoadDeref");
                            #endif
                            EmitLoadDeref(name);
                            return;

                        case SymbolScope.Global:
                            // CPython 3.12: 모듈 레벨에서는 LOAD_NAME, 함수 내에서는 LOAD_GLOBAL
                            if (!_isInFunction)
                            {
                                var nameIndex = AddName(name);
                                EmitInstruction(ByteCodeOp.LOAD_NAME, nameIndex);
                                #if DEBUG_LOG
                                Console.WriteLine($"    → Module level LOAD_NAME for global var: {name} (name index {nameIndex})");
                                #endif
                            }
                            else
                            {
                                // CPython 3.12: Use new-style LOAD_GLOBAL with flag encoding
                                EmitLoadGlobal(name, pushNull: false);
                                #if DEBUG_LOG
                                Console.WriteLine($"    → Function level LOAD_GLOBAL for global var: {name}");
                                #endif
                            }
                            return;

                        case SymbolScope.Local:
                            // Local variable: LOAD_FAST 사용
                            // CPython 3.12: 모듈 레벨에서는 LOAD_NAME 사용
                            // 단, comprehension 내부의 iteration 변수는 LOAD_FAST 사용 (PEP 709)
                            if (_isInFunction || _isInComprehension)
                            {
                                var localIndex = _varNames.IndexOf(name);
                                if (localIndex >= 0)
                                {
                                    EmitInstruction(ByteCodeOp.LOAD_FAST, localIndex);
                                    #if DEBUG_LOG
                                    Console.WriteLine($"    → LOAD_FAST for local var: {name} (index {localIndex})");
                                    #endif
                                    return;
                                }
                            }
                            break;
                    }
                }
            }

            // 1. CPython 3.12: global 변수를 먼저 체크 (fallback)
            if (_globalVars.Contains(name))
            {
                // CPython 3.12: Use new-style LOAD_GLOBAL with flag encoding
                EmitLoadGlobal(name, pushNull: false);
                #if DEBUG_LOG
                Console.WriteLine($"    → LOAD_GLOBAL for global var: {name}");
                #endif
                return;
            }
            
            // 1. CPython 3.12: nonlocal 변수를 체크
            if (_nonlocalVars.Contains(name))
            {
                // nonlocal 변수를 _freeVars에 추가 (부모 스코프에서 가져옴)
                if (!_freeVars.Contains(name))
                {
                    _freeVars.Add(name);
                }
                var freeIndex = _freeVars.IndexOf(name);
                EmitInstruction(ByteCodeOp.LOAD_DEREF, freeIndex);
                #if DEBUG_LOG
                Console.WriteLine($"    → LOAD_DEREF for nonlocal var: {name} (free index {freeIndex})");
                #endif
                return;
            }
            
            // 2. 지역 변수(매개변수 포함) 처리
            // CPython 3.12 PEP 709: 모듈 레벨에서 comprehension 외부에서는 LOAD_NAME 사용
            var varIndex = _varNames.IndexOf(name);
            if (varIndex >= 0)
            {
                // 모듈 레벨이고 comprehension 내부가 아니면 LOAD_NAME 사용
                if (!_isInFunction && !_isInComprehension)
                {
                    // Comprehension 변수가 모듈 레벨 코드에서 재사용되는 경우
                    // LOAD_NAME 사용 (CPython 3.12 호환)
                    var nameIndex = AddName(name);
                    EmitInstruction(ByteCodeOp.LOAD_NAME, nameIndex);
                    #if DEBUG_LOG
                    Console.WriteLine($"    → Module level (outside comprehension) LOAD_NAME for: {name} (name index {nameIndex})");
                    #endif
                    return;
                }

                // 이 변수가 cell로 변환되었는지 확인
                // CPython 3.12: Python/compile.c, Objects/codeobject.c
                // Emit cell index, FixCellOffsets will remap to localsplus offset
                if (_cellVars.Contains(name))
                {
                    // Cell 변수는 LOAD_DEREF로 접근
                    // Emit the cell index (position in _cellVars), not varIndex
                    // FixCellOffsets will map this to the correct localsplus offset
                    var cellIdx = _cellVars.IndexOf(name);
                    EmitInstruction(ByteCodeOp.LOAD_DEREF, cellIdx);
                    #if DEBUG_LOG
                    Console.WriteLine($"    → LOAD_DEREF for cell var: {name} (cellIndex {cellIdx})");
                    #endif
                }
                else
                {
                    EmitInstruction(ByteCodeOp.LOAD_FAST, varIndex);
                }
                return;
            }
            
            // 2. 자유 변수 처리 (Phase 2)
            // CPython 3.12: Free vars are stored after varnames in localsplus
            // localsplus index for free var = len(varnames) + freeVars.IndexOf(name)
            #if DEBUG_COMPILER_LOG
            Console.WriteLine($"  🔍 EmitLoadName('{name}'): Checking _freeVars. List contains: [{string.Join(", ", _freeVars)}]");
            Console.WriteLine($"     _cellVars: [{string.Join(", ", _cellVars)}]");
            Console.WriteLine($"     _isInFunction: {_isInFunction}");
            #endif
            if (_freeVars.Contains(name))
            {
                var freeIndex = _freeVars.IndexOf(name);
                // CPython 3.12: Free vars come after varnames in localsplus
                var derefIndex = _varNames.Count + freeIndex;
                EmitInstruction(ByteCodeOp.LOAD_DEREF, derefIndex);
                #if DEBUG_COMPILER_LOG
                Console.WriteLine($"    → LOAD_DEREF for free var: {name} (freeIndex {freeIndex}, derefIndex {derefIndex})");
                #endif
                return;
            }
            
            // 3. 모듈 레벨: CPython 3.12 호환성 - 항상 LOAD_NAME 사용
            if (!_isInFunction)
            {
                // CPython 3.12: 모듈 레벨에서는 builtin 함수든 일반 변수든 모두 LOAD_NAME 사용
                var nameIndex = AddName(name);
                EmitInstruction(ByteCodeOp.LOAD_NAME, nameIndex);
                #if DEBUG_LOG
                Console.WriteLine($"    → Module level LOAD_NAME for: {name} (name index {nameIndex})");
                #endif
                return;
            }
            
            // 4. 함수 내부에서의 전역 변수/내장 함수 참조 - CPython 3.12 호환성
            // CPython 3.12: Use new-style LOAD_GLOBAL with flag encoding
            EmitLoadGlobal(name, pushNull: false);
            #if DEBUG_LOG
            Console.WriteLine($"    → LOAD_GLOBAL for {(IsBuiltinFunction(name) ? "builtin" : "global")}: {name}");
            #endif
        }
        
        /// <summary>
        /// CPython 3.12: LOAD_GLOBAL with optional NULL push
        /// oparg encoding: (nameIndex << 1) | pushNull
        /// </summary>
        private void EmitLoadGlobal(string name, bool pushNull = false)
        {
            var nameIndex = AddName(name);
            // CPython 3.12: low bit indicates whether to push NULL
            int oparg = pushNull ? (nameIndex << 1) | 1 : (nameIndex << 1);
            #if DEBUG_LOG
            Console.WriteLine($"   EmitLoadGlobal('{name}', pushNull={pushNull}): nameIndex={nameIndex}, oparg={oparg}");
            #endif
            EmitInstruction(ByteCodeOp.LOAD_GLOBAL, oparg);
        }

        private void EmitStoreName(string name)
        {
            // 함수 내부에서는 지역변수로 등록하고 STORE_FAST 사용
            if (_isInFunction)
            {
                // 0. CPython 3.12: Symbol Table을 먼저 확인 (우선순위)
                if (_currentSymbolTable != null)
                {
                    var symbol = _currentSymbolTable.Lookup(name);
                    if (symbol != null)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"    🔍 Symbol Table lookup for store: {name} → Scope: {symbol.Scope}");
                        #endif

                        // Symbol Table에서 분석된 스코프에 따라 적절한 명령어 생성
                        switch (symbol.Scope)
                        {
                            case SymbolScope.Cell:
                                // Cell variable: STORE_DEREF 사용
                                // CPython 3.12: Python/compile.c, Objects/codeobject.c
                                // Emit cell index, FixCellOffsets will remap to localsplus offset
                                if (_cellVars.Contains(name))
                                {
                                    // Emit the cell index (position in _cellVars)
                                    // FixCellOffsets will map this to the correct localsplus offset:
                                    // - param cells: varnames index
                                    // - non-param cells: nlocals + position among non-param cells
                                    var cellIdx = _cellVars.IndexOf(name);
                                    EmitInstruction(ByteCodeOp.STORE_DEREF, cellIdx);
                                    #if DEBUG_LOG
                                    Console.WriteLine($"    → STORE_DEREF for cell var: {name} (cellIndex {cellIdx})");
                                    #endif
                                    return;
                                }
                                break;

                            case SymbolScope.Global:
                                // Global variable: STORE_GLOBAL 사용
                                var globalIndex = AddName(name);
                                EmitInstruction(ByteCodeOp.STORE_GLOBAL, globalIndex);
                                #if DEBUG_LOG
                                Console.WriteLine($"    → STORE_GLOBAL for global var: {name} (global index {globalIndex})");
                                #endif
                                return;

                            case SymbolScope.Local:
                                // Local variable: STORE_FAST 사용
                                var localIndex = GetOrAddVarName(name);
                                EmitInstruction(ByteCodeOp.STORE_FAST, localIndex);
                                #if DEBUG_LOG
                                Console.WriteLine($"    → STORE_FAST for local var: {name} (index {localIndex})");
                                #endif
                                return;
                        }
                    }
                }

                // 1. CPython 3.12: global 변수는 STORE_GLOBAL 사용 (fallback)
                if (_globalVars.Contains(name))
                {
                    var globalIndex = AddName(name);
                    EmitInstruction(ByteCodeOp.STORE_GLOBAL, globalIndex);
                    #if DEBUG_LOG
                    Console.WriteLine($"    → STORE_GLOBAL for global var: {name} (global index {globalIndex})");
                    #endif
                    return;
                }
                
                // CPython 3.12: nonlocal 변수는 STORE_DEREF 사용
                if (_nonlocalVars.Contains(name))
                {
                    // nonlocal 변수를 _freeVars에 추가 (부모 스코프에서 가져옴)
                    if (!_freeVars.Contains(name))
                    {
                        _freeVars.Add(name);
                    }
                    // CPython 3.12: Free vars come after varnames in localsplus
                    var freeIndex = _freeVars.IndexOf(name);
                    var derefIndex = _varNames.Count + freeIndex;
                    EmitInstruction(ByteCodeOp.STORE_DEREF, derefIndex);
                    #if DEBUG_LOG
                    Console.WriteLine($"    → STORE_DEREF for nonlocal var: {name} (freeIndex {freeIndex}, derefIndex {derefIndex})");
                    #endif
                    return;
                }

                // 셀 변수 처리 (Phase 2)
                // CPython 3.12: Python/compile.c, Objects/codeobject.c
                // Emit cell index, FixCellOffsets will remap to localsplus offset
                if (_cellVars.Contains(name))
                {
                    // Emit the cell index (position in _cellVars)
                    // FixCellOffsets will map this to the correct localsplus offset
                    var cellIdx = _cellVars.IndexOf(name);
                    EmitInstruction(ByteCodeOp.STORE_DEREF, cellIdx);
                    #if DEBUG_LOG
                    Console.WriteLine($"    → STORE_DEREF for cell var: {name} (cellIndex {cellIdx})");
                    #endif
                    return;
                }

                // 자유 변수 처리 (Phase 2)
                // CPython 3.12: Free vars come after varnames in localsplus
                if (_freeVars.Contains(name))
                {
                    var freeIndex = _freeVars.IndexOf(name);
                    var derefIndex = _varNames.Count + freeIndex;
                    EmitInstruction(ByteCodeOp.STORE_DEREF, derefIndex);
                    #if DEBUG_LOG
                    Console.WriteLine($"    → STORE_DEREF for free var: {name} (freeIndex {freeIndex}, derefIndex {derefIndex})");
                    #endif
                    return;
                }
                
                // 지역 변수로 등록하고 STORE_FAST 사용
                var varIndex = GetOrAddVarName(name);
                EmitInstruction(ByteCodeOp.STORE_FAST, varIndex);
                return;
            }
            
            // 모듈 레벨: CPython 3.12 호환성 확인
            // 만약 이 변수가 함수 내에서 global로 선언되었다면 STORE_GLOBAL 사용
            if (_globalVars.Contains(name) || _moduleGlobalVars.Contains(name))
            {
                var globalIndex = AddName(name);
                EmitInstruction(ByteCodeOp.STORE_GLOBAL, globalIndex);
                #if DEBUG_LOG
                Console.WriteLine($"    → Module level STORE_GLOBAL for global var: {name} (global index {globalIndex})");
                #endif
                return;
            }
            
            // 기본적으로 모듈 레벨에서는 STORE_NAME 사용
            var index = AddName(name);
            EmitInstruction(ByteCodeOp.STORE_NAME, index);
        }
        
        private void EmitDeleteName(string name)
        {
            // 함수 내부에서의 변수 삭제
            if (_isInFunction)
            {
                // CPython 3.12: global 변수는 DELETE_GLOBAL 사용
                if (_globalVars.Contains(name))
                {
                    var globalIndex = AddName(name);
                    EmitInstruction(ByteCodeOp.DELETE_GLOBAL, globalIndex);
                    #if DEBUG_LOG
                    Console.WriteLine($"    → DELETE_GLOBAL for global var: {name} (global index {globalIndex})");
                    #endif
                    return;
                }
                
                // CPython 3.12: nonlocal 변수는 DELETE_DEREF 사용
                if (_nonlocalVars.Contains(name))
                {
                    var freeIndex = _freeVars.IndexOf(name);
                    if (freeIndex >= 0)
                    {
                        EmitInstruction(ByteCodeOp.DELETE_DEREF, freeIndex);
                        #if DEBUG_LOG
                        Console.WriteLine($"    → DELETE_DEREF for nonlocal var: {name} (free index {freeIndex})");
                        #endif
                        return;
                    }
                }
                
                // 지역 변수: DELETE_FAST 사용
                var varIndex = _varNames.IndexOf(name);
                if (varIndex >= 0)
                {
                    EmitInstruction(ByteCodeOp.DELETE_FAST, varIndex);
                    #if DEBUG_LOG
                    Console.WriteLine($"    → DELETE_FAST for local var: {name} (var index {varIndex})");
                    #endif
                    return;
                }
            }
            
            // 모듈 레벨: DELETE_NAME 사용
            var index = AddName(name);
            EmitInstruction(ByteCodeOp.DELETE_NAME, index);
        }
        
        /// <summary>
        /// CPython 3.12: 내장 함수 판별 - LOAD_GLOBAL 최적화용
        /// </summary>
        private bool IsBuiltinFunction(string name)
        {
            // CPython 3.12 주요 내장 함수들
            var builtins = new HashSet<string>
            {
                "print", "len", "str", "int", "float", "bool", "list", "tuple", "dict", "set",
                "range", "enumerate", "zip", "map", "filter", "sum", "min", "max", "abs",
                "round", "sorted", "reversed", "any", "all", "isinstance", "issubclass",
                "type", "id", "hash", "repr", "format", "input", "open", "iter", "next",
                "getattr", "setattr", "hasattr", "delattr", "vars", "dir", "globals", "locals",
                "eval", "exec", "compile", "callable", "chr", "ord", "hex", "oct", "bin"
            };
            
            return builtins.Contains(name);
        }
        
        private void EmitBinaryOp(BinaryOperator op)
        {
            // CPython 3.12: Use operator's GetOpType() method directly
            var operation = op.GetOpType();

            // BINARY_OP OpCode와 operation 타입을 argument로 전달
            EmitInstruction(ByteCodeOp.BINARY_OP, (int)operation);
        }

        /// <summary>
        /// CPython 3.12 compile-time constant folding (compile.c:fold_binop)
        /// Try to evaluate binary operation on constants at compile time
        /// Returns null if folding is not safe or not supported
        /// </summary>
        private PyObject? TryFoldBinaryOpAtCompileTime(PyObject left, PyObject right, BinaryOperator op)
        {
            try
            {
                var opType = op.GetOpType();

                // Only fold numeric operations (integers and floats)
                if (left is PyInt leftInt && right is PyInt rightInt)
                {
                    return FoldIntBinaryOp(leftInt, rightInt, opType);
                }
                else if ((left is PyInt || left is PyFloat) && (right is PyInt || right is PyFloat))
                {
                    // Convert to double for float operations
                    double leftVal = left is PyInt li ? (double)li.Value : ((PyFloat)left).Value;
                    double rightVal = right is PyInt ri ? (double)ri.Value : ((PyFloat)right).Value;
                    return FoldFloatBinaryOp(leftVal, rightVal, opType);
                }
            }
            catch
            {
                // If folding fails (e.g., division by zero), don't fold
                return null;
            }

            return null;
        }

        /// <summary>
        /// Fold integer binary operations at compile time
        /// </summary>
        private PyObject? FoldIntBinaryOp(PyInt left, PyInt right, BinaryOpType opType)
        {
            long leftVal = (long)left.Value;
            long rightVal = (long)right.Value;

            switch (opType)
            {
                case BinaryOpType.ADD:
                    return new PyInt(leftVal + rightVal);
                case BinaryOpType.AND:
                    return new PyInt(leftVal & rightVal);
                case BinaryOpType.FLOOR_DIVIDE:
                    if (rightVal == 0) return null;  // Don't fold division by zero
                    return new PyInt(leftVal / rightVal);
                case BinaryOpType.LSHIFT:
                    return null;  // Don't fold, can be unsafe
                case BinaryOpType.MATRIX_MULTIPLY:
                    return null;  // Not applicable to int
                case BinaryOpType.MULTIPLY:
                    return new PyInt(leftVal * rightVal);
                case BinaryOpType.MODULO:
                    if (rightVal == 0) return null;
                    return new PyInt(leftVal % rightVal);
                case BinaryOpType.OR:
                    return new PyInt(leftVal | rightVal);
                case BinaryOpType.POWER:
                    return null;  // Don't fold, can overflow
                case BinaryOpType.RSHIFT:
                    return null;  // Don't fold, can be unsafe
                case BinaryOpType.SUBTRACT:
                    return new PyInt(leftVal - rightVal);
                case BinaryOpType.TRUE_DIVIDE:
                    if (rightVal == 0) return null;
                    return new PyFloat((double)leftVal / (double)rightVal);
                case BinaryOpType.XOR:
                    return new PyInt(leftVal ^ rightVal);
                default:
                    return null;
            }
        }

        /// <summary>
        /// Fold float binary operations at compile time
        /// </summary>
        private PyObject? FoldFloatBinaryOp(double left, double right, BinaryOpType opType)
        {
            switch (opType)
            {
                case BinaryOpType.ADD:
                    return new PyFloat(left + right);
                case BinaryOpType.FLOOR_DIVIDE:
                    if (right == 0) return null;
                    return new PyFloat(Math.Floor(left / right));
                case BinaryOpType.MULTIPLY:
                    return new PyFloat(left * right);
                case BinaryOpType.MODULO:
                    if (right == 0) return null;
                    return new PyFloat(left % right);
                case BinaryOpType.POWER:
                    return null;  // Don't fold, can be unsafe
                case BinaryOpType.SUBTRACT:
                    return new PyFloat(left - right);
                case BinaryOpType.TRUE_DIVIDE:
                    if (right == 0) return null;
                    return new PyFloat(left / right);
                default:
                    return null;
            }
        }

        private int GetOrAddConstant(PyObject value)
        {
            // Use object reference equality for constants
            for (int i = 0; i < _constants.Count; i++)
            {
                if (ReferenceEquals(_constants[i], value) ||
                    (value is PyInt intConst && _constants[i] is PyInt existingInt && intConst.Value == existingInt.Value) ||
                    (value is PyString strConst && _constants[i] is PyString existingStr && strConst.Value == existingStr.Value) ||
                    (value is PyTuple tupleConst && _constants[i] is PyTuple existingTuple && TupleEquals(tupleConst, existingTuple)))
                {
                    return i;
                }
            }

            _constants.Add(value);
#if DEBUG_COMPILER_LOG
            var valueDesc = value switch
            {
                PyCodeObject code => $"<code:{code.Name}>",
                PyString str => $"\"{str.Value}\"",
                PyTuple tuple => $"tuple[{tuple.Items.Length}]",
                _ => value.ToString()
            };
            Console.WriteLine($"🔧 [CONST] Added constant [{_constants.Count - 1}] = {valueDesc} to compiler instance {this.GetHashCode():X}");
#endif
            return _constants.Count - 1;
        }

        private bool TupleEquals(PyTuple tuple1, PyTuple tuple2)
        {
            if (tuple1.Items.Length != tuple2.Items.Length)
                return false;

            for (int i = 0; i < tuple1.Items.Length; i++)
            {
                var elem1 = tuple1.Items[i];
                var elem2 = tuple2.Items[i];

                if (!ReferenceEquals(elem1, elem2))
                {
                    if (elem1 is PyInt int1 && elem2 is PyInt int2 && int1.Value == int2.Value)
                        continue;
                    if (elem1 is PyString str1 && elem2 is PyString str2 && str1.Value == str2.Value)
                        continue;
                    return false;
                }
            }
            return true;
        }
        
        private int AddName(string name)
        {
            if (!_names.Contains(name))
                _names.Add(name);
            return _names.IndexOf(name);
        }
        
        // 추가 컴파일 메서드들
        
        private void EmitUnaryOp(UnaryOperator op)
        {
            // CPython 3.12: Use type-based switch on operator
            var opCode = op switch
            {
                UAdd => ByteCodeOp.UNARY_POSITIVE,
                USub => ByteCodeOp.UNARY_NEGATIVE,
                Not => ByteCodeOp.UNARY_NOT,
                Invert => ByteCodeOp.UNARY_INVERT,
                _ => throw new NotImplementedException($"Unary operator '{op.OperatorType}' not implemented")
            };
            EmitInstruction(opCode);
        }
        
        // CPython 3.12: Emit compare op from GeneratedCmpop (ASDL)
        private void EmitCompareOp(Generated.GeneratedCmpop op)
        {
            switch (op)
            {
                // CPython: CONTAINS_OP
                case Generated.GeneratedIn _:
                    EmitInstruction(ByteCodeOp.CONTAINS_OP, 0); // 0 = in
                    return;
                case Generated.GeneratedNotIn _:
                    EmitInstruction(ByteCodeOp.CONTAINS_OP, 1); // 1 = not in
                    return;

                // CPython: IS_OP
                case Generated.GeneratedIs _:
                    EmitInstruction(ByteCodeOp.IS_OP, 0); // 0 = is
                    return;
                case Generated.GeneratedIsNot _:
                    EmitInstruction(ByteCodeOp.IS_OP, 1); // 1 = is not
                    return;

                // CPython: COMPARE_OP
                case Generated.GeneratedLt _:
                    EmitInstruction(ByteCodeOp.COMPARE_OP, (int)CompareOp.LT);
                    return;
                case Generated.GeneratedLtE _:
                    EmitInstruction(ByteCodeOp.COMPARE_OP, (int)CompareOp.LE);
                    return;
                case Generated.GeneratedEq _:
                    EmitInstruction(ByteCodeOp.COMPARE_OP, (int)CompareOp.EQ);
                    return;
                case Generated.GeneratedNotEq _:
                    EmitInstruction(ByteCodeOp.COMPARE_OP, (int)CompareOp.NE);
                    return;
                case Generated.GeneratedGt _:
                    EmitInstruction(ByteCodeOp.COMPARE_OP, (int)CompareOp.GT);
                    return;
                case Generated.GeneratedGtE _:
                    EmitInstruction(ByteCodeOp.COMPARE_OP, (int)CompareOp.GE);
                    return;

                default:
                    throw new NotImplementedException($"Compare operator {op.GetType().Name} not implemented");
            }
        }

        private void EmitCompareOp(string op)
        {
            // Handle membership test operations with CONTAINS_OP
            if (op == "in" || op == "not in" || op == "In" || op == "NotIn")
            {
                var containsOp = op switch
                {
                    "in" => 0,     // IN
                    "In" => 0,     // Parser generates In for 'in'
                    "not in" => 1, // NOT_IN
                    "NotIn" => 1,  // Parser generates NotIn for 'not in'
                    _ => throw new NotImplementedException($"Contains operator '{op}' not implemented")
                };
                EmitInstruction(ByteCodeOp.CONTAINS_OP, containsOp);
                return;
            }
            
            // Handle identity comparison operations with IS_OP (CPython 3.12)
            if (op == "is")
            {
                EmitInstruction(ByteCodeOp.IS_OP, 0); // 0 = is
                return;
            }
            else if (op == "is not")
            {
                EmitInstruction(ByteCodeOp.IS_OP, 1); // 1 = is not
                return;
            }

            // Handle regular comparison operations with COMPARE_OP
            // Use CPython 3.12 actual bytecode values
            var compareOp = op switch
            {
                "<" => (int)CompareOp.LT,   // 2
                "Lt" => (int)CompareOp.LT,  // Parser uses Lt for <
                "<=" => (int)CompareOp.LE,  // 26
                "LtE" => (int)CompareOp.LE, // Parser uses LtE for <=
                "==" => (int)CompareOp.EQ,  // 40
                "Eq" => (int)CompareOp.EQ,  // Parser uses Eq for ==
                "!=" => (int)CompareOp.NE,  // 55
                "NotEq" => (int)CompareOp.NE, // Parser uses NotEq for !=
                ">" => (int)CompareOp.GT,   // 68
                "Gt" => (int)CompareOp.GT,  // Parser uses Gt for >
                ">=" => (int)CompareOp.GE,  // 92
                "GtE" => (int)CompareOp.GE, // Parser uses GtE for >=
                // "In" and "NotIn" are now handled by CONTAINS_OP above
                _ => throw new NotImplementedException($"Compare operator '{op}' not implemented")
            };
            EmitInstruction(ByteCodeOp.COMPARE_OP, compareOp);
        }
        
        /// <summary>
        /// CPython 3.12: LOAD_ATTR with optional NULL push for method call optimization
        /// oparg encoding: (nameIndex << 1) | pushNull
        /// </summary>
        private void EmitLoadAttr(string attrName, bool pushNull = false)
        {
            var nameIndex = AddName(attrName);
            // CPython 3.12: low bit indicates whether to push NULL for method calls
            int oparg = pushNull ? (nameIndex << 1) | 1 : (nameIndex << 1);
            #if DEBUG_LOG
            Console.WriteLine($"   EmitLoadAttr('{attrName}', pushNull={pushNull}): nameIndex={nameIndex}, oparg={oparg}");
            #endif
            EmitInstruction(ByteCodeOp.LOAD_ATTR, oparg);
        }
        
        private void CompileAugAssign(AugAssignStatement augAssign)
        {
            // CPython 3.12: target += value (supports Name, Attribute, Subscript)
            // Pattern: LOAD_target, LOAD_value, BINARY_OP, STORE_target
            // CPython: Python/compile.c:compiler_augassign (lines 5527-5577)

            // Load current value from target
            if (augAssign.TargetExpr is NameExpression nameExpr)
            {
                // Simple name: x += 1
                EmitLoadName(nameExpr.Name);
                CompileExpression(augAssign.Value);

                // CPython 3.12: Convert regular BinaryOpType to INPLACE version
                // CPython: Python/compile.c:5543 - compiler_addop(c, inplace_binop(s->v.AugAssign.op))
                // CPython: Python/compile.c:1235-1258 - inplace_binop() function
                var binaryOpType = ConvertToInplaceBinaryOp(augAssign.Op.GetOpType());

                EmitInstruction(ByteCodeOp.BINARY_OP, (int)binaryOpType);
                EmitStoreName(nameExpr.Name);
            }
            else if (augAssign.TargetExpr is AttributeExpression attrExpr)
            {
                // Attribute: self.x += 1
                // CPython 3.12: LOAD obj, COPY 1, LOAD_ATTR, LOAD value, BINARY_OP, SWAP 2, STORE_ATTR
                CompileExpression(attrExpr.Value);  // Load object (self)
                EmitInstruction(ByteCodeOp.COPY, 1);  // Copy object reference
                EmitLoadAttr(attrExpr.Attr);          // Load attribute value
                CompileExpression(augAssign.Value);    // Load right-hand value

                // Perform binary operation (in-place version)
                var binaryOpType = ConvertToInplaceBinaryOp(augAssign.Op.GetOpType());
                EmitInstruction(ByteCodeOp.BINARY_OP, (int)binaryOpType);

                // Store result back to attribute
                EmitInstruction(ByteCodeOp.SWAP, 2);  // Swap result and object
                EmitInstruction(ByteCodeOp.STORE_ATTR, GetOrAddName(attrExpr.Attr));
            }
            else if (augAssign.TargetExpr is SubscriptExpression subscriptExpr)
            {
                // Subscript: list[0] += 1
                // CPython 3.12 pattern: LOAD container, LOAD index, COPY 2, COPY 2, BINARY_SUBSCR, LOAD value, BINARY_OP, SWAP 3, SWAP 2, STORE_SUBSCR
                CompileExpression(subscriptExpr.Value);   // Load container (list1)
                CompileExpression(subscriptExpr.Slice);   // Load index (0)
                EmitInstruction(ByteCodeOp.COPY, 2);      // Copy top 2: [list1, 0, list1]
                EmitInstruction(ByteCodeOp.COPY, 2);      // Copy top 2: [list1, 0, list1, 0]
                EmitInstruction(ByteCodeOp.BINARY_SUBSCR);  // Load current value: [list1, 0, list1[0]]
                CompileExpression(augAssign.Value);        // Load right-hand value: [list1, 0, list1[0], 10]

                // Perform binary operation (in-place version)
                var binaryOpType = ConvertToInplaceBinaryOp(augAssign.Op.GetOpType());
                EmitInstruction(ByteCodeOp.BINARY_OP, (int)binaryOpType);  // [list1, 0, result]

                // Store result back: SWAP to get [result, list1, 0], then STORE_SUBSCR
                EmitInstruction(ByteCodeOp.SWAP, 3);  // [result, 0, list1]
                EmitInstruction(ByteCodeOp.SWAP, 2);  // [result, list1, 0]
                EmitInstruction(ByteCodeOp.STORE_SUBSCR);  // list1[0] = result
            }
            else
            {
                throw new NotImplementedException($"AugAssign target type '{augAssign.TargetExpr.GetType().Name}' not implemented");
            }
        }

        /// <summary>
        /// Convert regular binary operation to in-place version for augmented assignment
        /// CPython 3.12: Python/compile.c:inplace_binop (lines 1235-1258)
        /// Maps NB_* to NB_INPLACE_* operations
        /// </summary>
        private BinaryOpType ConvertToInplaceBinaryOp(BinaryOpType op)
        {
            // CPython 3.12: Python/compile.c:1235-1258
            // static int inplace_binop(operator_ty op)
            return op switch
            {
                BinaryOpType.ADD => BinaryOpType.INPLACE_ADD,                      // Add -> __iadd__
                BinaryOpType.SUBTRACT => BinaryOpType.INPLACE_SUBTRACT,            // Sub -> __isub__
                BinaryOpType.MULTIPLY => BinaryOpType.INPLACE_MULTIPLY,            // Mult -> __imul__
                BinaryOpType.TRUE_DIVIDE => BinaryOpType.INPLACE_TRUE_DIVIDE,      // Div -> __itruediv__
                BinaryOpType.FLOOR_DIVIDE => BinaryOpType.INPLACE_FLOOR_DIVIDE,    // FloorDiv -> __ifloordiv__
                BinaryOpType.MODULO => BinaryOpType.INPLACE_MODULO,                // Mod -> __imod__
                BinaryOpType.POWER => BinaryOpType.INPLACE_POWER,                  // Pow -> __ipow__
                BinaryOpType.LSHIFT => BinaryOpType.INPLACE_LSHIFT,                // LShift -> __ilshift__
                BinaryOpType.RSHIFT => BinaryOpType.INPLACE_RSHIFT,                // RShift -> __irshift__
                BinaryOpType.OR => BinaryOpType.INPLACE_OR,                        // BitOr -> __ior__
                BinaryOpType.XOR => BinaryOpType.INPLACE_XOR,                      // BitXor -> __ixor__
                BinaryOpType.AND => BinaryOpType.INPLACE_AND,                      // BitAnd -> __iand__
                BinaryOpType.MATRIX_MULTIPLY => BinaryOpType.INPLACE_MATRIX_MULTIPLY,  // MatMult -> __imatmul__
                _ => throw new ArgumentException($"Unknown binary operation type for in-place conversion: {op}")
            };
        }
        
        private void CompileAugmentedAssign(AugmentedAssignStatement augAssign)
        {
            // CPython 3.12: Enhanced augmented assignment for complex targets (obj.attr += 1, list[0] += 1)
            // Pattern: LOAD_target, LOAD_value, BINARY_OP, STORE_target
            
            // Load current value from target
            switch (augAssign.Target)
            {
                case NameExpression name:
                    EmitLoadName(name.Name);
                    break;
                    
                case AttributeExpression attr:
                    CompileExpression(attr.Value);  // Load object
                    EmitInstruction(ByteCodeOp.LOAD_ATTR, GetOrAddName(attr.Attr));
                    break;
                    
                case SubscriptExpression subscript:
                    CompileExpression(subscript.Value);   // Load container
                    CompileExpression(subscript.Slice);   // Load index
                    EmitInstruction(ByteCodeOp.BINARY_SUBSCR);
                    break;
                    
                default:
                    throw new NotImplementedException($"Augmented assignment target '{augAssign.Target.GetType().Name}' not implemented");
            }
            
            // Load right-hand side value
            CompileExpression(augAssign.Value);
            
            // Perform binary operation
            var binaryOpType = augAssign.Op switch
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
                "|" => BinaryOpType.OR,
                "^" => BinaryOpType.XOR,
                "&" => BinaryOpType.AND,
                // "@" => BinaryOpType.MATRIX_MULTIPLY,  // Not implemented yet
                _ => throw new NotImplementedException($"Binary operator '{augAssign.Op}' not implemented")
            };
            
            EmitInstruction(ByteCodeOp.BINARY_OP, (int)binaryOpType);
            
            // Store result back to target
            switch (augAssign.Target)
            {
                case NameExpression name:
                    EmitStoreName(name.Name);
                    break;
                    
                case AttributeExpression attr:
                    CompileExpression(attr.Value);  // Load object again
                    EmitInstruction(ByteCodeOp.STORE_ATTR, GetOrAddName(attr.Attr));
                    break;
                    
                case SubscriptExpression subscript:
                    CompileExpression(subscript.Value);   // Load container again
                    CompileExpression(subscript.Slice);   // Load index again
                    EmitInstruction(ByteCodeOp.STORE_SUBSCR);
                    break;
            }
        }
        
        private void CompileAnnAssign(AnnAssignStatement annAssign)
        {
            // CPython 3.12 compatible annotated assignment: var: type = value

            // 1. Handle the value assignment if present
            if (annAssign.Value != null)
            {
                CompileExpression(annAssign.Value);
                EmitStoreName(annAssign.VariableName);
            }

            // 2. Store type annotation in __annotations__ dict (CPython 3.12 pattern)
            // Note: SETUP_ANNOTATIONS should be called once at module start, not here

            // Compile the annotation expression (e.g., list[int] becomes LOAD_NAME list, LOAD_NAME int, BINARY_SUBSCR)
            CompileExpression(annAssign.Annotation);

            // Load __annotations__ dict
            EmitLoadName("__annotations__");

            // Load variable name as string key
            EmitLoadConst(new PyString(annAssign.VariableName));

            // Store annotation: __annotations__[var_name] = annotation
            EmitInstruction(ByteCodeOp.STORE_SUBSCR);
        }
        
        /// <summary>
        /// Async function compilation - similar to CompileNestedFunction but creates async function
        /// </summary>
        private void CompileAsyncFunction(AsyncFunctionDefStatement asyncFunc)
        {
            #if DEBUG_LOG
            Console.WriteLine($"\n🔍 Compiling async function: {asyncFunc.Name}");
            #endif
            
            // 1. 자유 변수 분석 (동일한 방식으로 분석)
            var analyzer = new FreeVariableAnalyzer();
            #if DEBUG_LOG
            Console.WriteLine($"  DEBUG: Current _varNames: [{string.Join(", ", _varNames)}]");
            #endif
            var (freeVars, cellVars) = analyzer.AnalyzeAsyncFunction(asyncFunc, _varNames);
            
            #if DEBUG_LOG
            Console.WriteLine($"  Free variables: [{string.Join(", ", freeVars)}]");
            #endif
            #if DEBUG_LOG
            Console.WriteLine($"  Cell variables: [{string.Join(", ", cellVars)}]");
            #endif
            
            // 2. 매개변수와 기본값 파싱 (CPython 3.12: use Arguments)
            var (paramNames, defaultExprs, kwDefaultExprs, flags, argCount, posonlyArgCount, kwonlyArgCount, annotations) = ParseFunctionArguments(asyncFunc.Arguments);

            // Evaluate default expressions to PyObjects (defaults are evaluated at function definition time)
            var defaults = new List<PyObject>();
            foreach (var defaultExpr in defaultExprs)
            {
                if (defaultExpr is ConstantExpression constExpr)
                {
                    defaults.Add(constExpr.Value);
                }
                else
                {
                    defaults.Add(PyNone.Instance); // Placeholder
                }
            }

            // Evaluate keyword-only defaults
            var kwDefaults = new List<PyObject>();
            foreach (var kwDefaultExpr in kwDefaultExprs)
            {
                if (kwDefaultExpr == null)
                {
                    kwDefaults.Add(PyNone.Instance);
                }
                else if (kwDefaultExpr is ConstantExpression constExpr)
                {
                    kwDefaults.Add(constExpr.Value);
                }
                else
                {
                    kwDefaults.Add(PyNone.Instance);
                }
            }

            // 3. 코드 객체 컴파일 (async 함수 전용)
            var codeObject = CompileAsyncFunctionBody(asyncFunc, freeVars, cellVars);

            // 4. 클로저와 기본값은 나중에 MAKE_FUNCTION 직전에 로드

            // 5. MAKE_FUNCTION을 위한 스택 준비 (CPython 순서: defaults, annotations, code)

            // 6. 기본값들을 tuple로 만들어 스택에 로드 (CPython 3.12 호환)
            // Performance: Eliminated LINQ
            if (defaultExprs.Count > 0)
            {
                foreach (var defaultExpr in defaultExprs)
                {
                    // CPython 3.12: Default expressions are compiled and evaluated at function definition time
                    CompileExpression(defaultExpr);
                }
                EmitInstruction(ByteCodeOp.BUILD_TUPLE, defaultExprs.Count);
            }

            // 7. annotations 튜플을 스택에 로드 (CPython 3.12 호환성)
            // Performance: Eliminated LINQ
            if (annotations.Count > 0)
            {
                foreach (var annotation in annotations)
                {
                    EmitLoadConst(new PyString(annotation.Key));   // parameter name
                    CompileExpression(annotation.Value);            // CPython 3.12: Compile annotation expression
                }
                EmitInstruction(ByteCodeOp.BUILD_TUPLE, annotations.Count * 2);
            }

            // 8. 클로저가 있으면 셀 변수들을 스택에 로드
            // Performance: Eliminated LINQ
            if (freeVars.Count > 0)
            {
                foreach (var freeVar in freeVars)
                {
                    if (_varNames.Contains(freeVar))
                    {
                        // Local variable in enclosing scope
                        EmitInstruction(ByteCodeOp.LOAD_CLOSURE, _varNames.IndexOf(freeVar));
                    }
                    else
                    {
                        // Could be in enclosing function's free vars
                        EmitLoadName(freeVar);
                    }
                }
                EmitInstruction(ByteCodeOp.BUILD_TUPLE, freeVars.Count);
            }

            // 9. 코드 객체를 상수로 로드 (마지막에 - 스택 맨 위가 됨)
            EmitLoadConst(codeObject);

            // 10. MAKE_FUNCTION 명령어 생성 (CPython 3.12와 동일한 플래그)
            // Performance: Eliminated LINQ
            var makeFlags = 0;
            if (defaults.Count > 0) makeFlags |= 0x01;  // CO_HAS_DEFAULTS
            if (annotations.Count > 0) makeFlags |= 0x04; // HAS_ANNOTATIONS (CPython 3.12)
            if (freeVars.Count > 0) makeFlags |= 0x08;  // CO_HAS_CLOSURE
            // async 함수도 일반 MAKE_FUNCTION 사용, 코드 객체의 CO_COROUTINE 플래그로 구분
            
            EmitInstruction(ByteCodeOp.MAKE_FUNCTION, makeFlags);
            
            // 9. 함수를 변수에 저장
            EmitStoreName(asyncFunc.Name);
            
            #if DEBUG_LOG
            Console.WriteLine($"✅ Async function {asyncFunc.Name} compiled successfully");
            #endif
        }
        private void CompileClass(ClassDefStatement cls)
        {
            #if DEBUG_LOG
            Console.WriteLine($"🔍 CompileClass called for: {cls.Name}, TypeParams.Count: {cls.TypeParams.Count}");
            #endif

            // PEP 695: Generic class with type parameters requires special handling
            if (cls.TypeParams.Count > 0)
            {
                #if DEBUG_LOG
                Console.WriteLine($"🔍 Dispatching to CompileGenericClass for: {cls.Name}");
                #endif
                CompileGenericClass(cls);
            }
            else
            {
                #if DEBUG_LOG
                Console.WriteLine($"🔍 Dispatching to CompileRegularClass for: {cls.Name}");
                #endif
                CompileRegularClass(cls);
            }
        }
        
        private void CompileGenericClass(ClassDefStatement cls)
        {
            // CPython 3.12: PEP 695 generic class compilation  
            // For now, create a simplified generic parameters function
            EmitInstruction(ByteCodeOp.PUSH_NULL);
            
            // Generate simplified Generic Parameters function code
            var genericParamsCode = CompileSimplifiedGenericParametersFunction(cls.TypeParams, cls.Name, cls.Body);
            EmitLoadConst(genericParamsCode);
            EmitInstruction(ByteCodeOp.MAKE_FUNCTION, 0);
            EmitInstruction(ByteCodeOp.CALL, 0);
            
            // The result is the class with type parameters bound
            EmitStoreName(cls.Name);
        }
        
        /// <summary>
        /// PEP 695: Compile generic function with type parameters
        /// CPython pattern: def identity[T](x: T) -> PUSH_NULL, LOAD_CONST <generic parameters>, MAKE_FUNCTION, CALL
        /// </summary>
        private void CompileGenericFunction(FunctionDefStatement func)
        {
            // CPython pattern: PUSH_NULL, LOAD_CONST <generic parameters function>, MAKE_FUNCTION, CALL
            EmitInstruction(ByteCodeOp.PUSH_NULL);
            
            // Create generic parameters function
            var genericParamsCode = CompileGenericParametersFunction(func.TypeParams, func.Name, func);
            EmitLoadConst(genericParamsCode);
            EmitInstruction(ByteCodeOp.MAKE_FUNCTION, 0);
            EmitInstruction(ByteCodeOp.CALL, 0);
            
            // Store the resulting function
            EmitStoreName(func.Name);
        }
        
        /// <summary>
        /// Compile Generic Parameters function for PEP 695 function
        /// Creates: <code object <generic parameters of identity>>
        /// </summary>
        private PyCodeObject CompileGenericParametersFunction(List<TypeParam> typeParams, string functionName, FunctionDefStatement func)
        {
            // Save current compilation state
            var savedInstructionSequence = _instructionSequence;
            var savedConstants = _constants;
            var savedNames = _names;
            var savedVarNames = _varNames;
            var savedCellVars = _cellVars;
            var savedFreeVars = _freeVars;

            // Initialize new compilation state for generic parameters function
            _instructionSequence = new InstructionSequence();
            _constants = new List<PyObject>();
            _names = new List<string>();
            _varNames = new List<string>();
            _cellVars = new List<string>();
            _freeVars = new List<string>();
            
            try
            {
                // CPython 3.12 pattern for generic function parameters
                EmitInstruction(ByteCodeOp.RESUME, 0);
                
                // Create TYPEVAR for each type parameter
                foreach (var typeParam in typeParams)
                {
                    EmitLoadConst(new PyString(typeParam.Name));
                    EmitInstruction(ByteCodeOp.CALL_INTRINSIC_1, (int)IntrinsicFunction.INTRINSIC_TYPEVAR);
                    EmitInstruction(ByteCodeOp.COPY, 1);

                    // Add to varNames for STORE_FAST/LOAD_FAST
                    if (!_varNames.Contains(typeParam.Name))
                        _varNames.Add(typeParam.Name);

                    int varIndex = _varNames.IndexOf(typeParam.Name);
                    EmitInstruction(ByteCodeOp.STORE_FAST, varIndex);
                }
                
                // Build tuple of type parameters
                EmitInstruction(ByteCodeOp.BUILD_TUPLE, typeParams.Count);
                
                // Create annotations tuple: complex parameter annotations
                var (paramNames, defaultExprs, kwDefaultExprs, flags, argCount, posonlyArgCount, kwonlyArgCount, annotations) = ParseFunctionArguments(func.Arguments);

                // Convert defaults for internal use
                var defaults = new List<PyObject>();
                foreach (var defaultExpr in defaultExprs)
                {
                    if (defaultExpr is ConstantExpression constExpr)
                    {
                        defaults.Add(constExpr.Value);
                    }
                    else
                    {
                        defaults.Add(PyNone.Instance);
                    }
                }

                // Convert keyword-only defaults
                var kwDefaults = new List<PyObject>();
                foreach (var kwDefaultExpr in kwDefaultExprs)
                {
                    if (kwDefaultExpr == null)
                    {
                        kwDefaults.Add(PyNone.Instance);
                    }
                    else if (kwDefaultExpr is ConstantExpression constExpr)
                    {
                        kwDefaults.Add(constExpr.Value);
                    }
                    else
                    {
                        kwDefaults.Add(PyNone.Instance);
                    }
                }
                
                // Build complex annotations tuple for all parameters and return type
                var annotationCount = 0;
                
                // Add parameter annotations
                foreach (var paramName in paramNames)
                {
                    EmitLoadConst(new PyString(paramName)); // parameter name
                    annotationCount++;

                    // Add type annotation (simplified: use first type param for now)
                    int typeVarIndex = _varNames.IndexOf(typeParams[0].Name);
                    EmitInstruction(ByteCodeOp.LOAD_FAST, typeVarIndex);
                    annotationCount++;
                }

                // Add return type annotation if exists
                if (func.Parameters.Count > 0) // Simple heuristic: if has params, likely has return type
                {
                    EmitLoadConst(new PyString("return"));
                    annotationCount++;

                    int typeVarIndex = _varNames.IndexOf(typeParams[0].Name);
                    EmitInstruction(ByteCodeOp.LOAD_FAST, typeVarIndex);
                    annotationCount++;
                }
                
                EmitInstruction(ByteCodeOp.BUILD_TUPLE, annotationCount);

                // CPython 3.12: compile.c:2371 - compiler_function_body
                // Compile actual function using CompilerFunctionBody (not legacy CompileFunction)
                var compiler = new PythonCompiler();
                compiler.SetSourceLocation(_currentFileName, _sourceLines);

                // CPython 3.12: Set symbol table context for the function
                if (_symbolTable != null)
                {
                    var funcSymbolTable = _symbolTable.GetChildren().FirstOrDefault(child =>
                        child.GetName().Contains(func.Name));
                    if (funcSymbolTable != null)
                    {
                        compiler.SetSymbolTableContext(funcSymbolTable);
                    }
                    compiler.SetRootSymbolTable(_symbolTable);
                }

                var funcCode = compiler.CompilerFunctionBody(
                    func.Body, func.Name, paramNames,
                    defaults, kwDefaults, new List<string>(), new List<string>(),
                    flags, argCount, posonlyArgCount, kwonlyArgCount);
                EmitLoadConst(funcCode);
                EmitInstruction(ByteCodeOp.MAKE_FUNCTION, 4); // annotations flag
                
                // SWAP 2 (not 4!) and CALL_INTRINSIC_2 for SET_FUNCTION_TYPE_PARAMS
                EmitInstruction(ByteCodeOp.SWAP, 2);
                EmitInstruction(ByteCodeOp.CALL_INTRINSIC_2, (int)IntrinsicFunction.INTRINSIC_SET_FUNCTION_TYPE_PARAMS);
                
                EmitInstruction(ByteCodeOp.RETURN_VALUE);

                // Build the code object
                var functionName_full = $"<generic parameters of {functionName}>";
                // Generic parameters wrapper has no generator flags
                var finalInstructions = GetFinalInstructions(0);
                return new PyCodeObject(
                    functionName_full,
                    finalInstructions,
                    _constants,
                    _names,
                    _varNames,
                    0, // argCount
                    0, // posonlyArgCount
                    0, // kwonlyArgCount
                    _freeVars,
                    _cellVars,
                    new List<PyObject>(), // defaultValues
                    null, // kwDefaults
                    0, // flags
                    "", // fileName
                    new List<string>() // sourceLines
                );
            }
            finally
            {
                // Restore compilation state
                _instructionSequence = savedInstructionSequence;
                _constants = savedConstants;
                _names = savedNames;
                _varNames = savedVarNames;
                _cellVars = savedCellVars;
                _freeVars = savedFreeVars;
            }
        }
        
        private void CompileRegularClass(ClassDefStatement cls)
        {
            #if DEBUG_LOG
            Console.WriteLine($"🔍 CompileRegularClass called for: {cls.Name}");
            #endif

            // CPython 3.12: Load decorators FIRST (Python/compile.c:2547, 1852-1862)
            // compiler_decorators: for (i = 0; i < asdl_seq_LEN(decos); i++)
            // Decorators are loaded in forward order (0 to N-1)
            if (cls.Decorators != null && cls.Decorators.Count > 0)
            {
                #if DEBUG_LOG
                Console.WriteLine($"🎨 Loading {cls.Decorators.Count} decorators for {cls.Name}");
                #endif

                // CPython: VISIT(c, expr, (expr_ty)asdl_seq_GET(decos, i))
                // Load each decorator expression onto the stack
                for (int i = 0; i < cls.Decorators.Count; i++)
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"   → Loading decorator {i}: {cls.Decorators[i].GetType().Name}");
                    #endif

                    var decorator = cls.Decorators[i];
                    // Compile the decorator function
                    CompileExpression(decorator.DecoratorFunction);

                    // If the decorator has arguments (e.g., @decorator(arg1, arg2))
                    // we need to call it first to get the actual decorator
                    if (decorator.Arguments != null && decorator.Arguments.Count > 0)
                    {
                        foreach (var arg in decorator.Arguments)
                        {
                            CompileExpression(arg);
                        }
                        EmitInstruction(ByteCodeOp.CALL, decorator.Arguments.Count);
                    }
                }
            }

            // CPython 3.12: Regular class compilation (no type parameters)
            // CPython 3.12: Python/bytecodes.c:987 - LOAD_BUILD_CLASS opcode
            // SharpPy doesn't have LOAD_BUILD_CLASS, so we use LOAD_GLOBAL with pushNull=true
            // This pushes NULL then __build_class__, equivalent to PUSH_NULL + LOAD_GLOBAL
            EmitLoadGlobal("__build_class__", pushNull: true);

            // Compile class body into a function
            var classBodyName = $"<class_body_{cls.Name}>";

            // CPython 3.12: Get free variables for this class from symbol table
            // When a class is defined inside a function and its methods reference
            // variables from the enclosing function, those become free variables.
            // Example: def outer(): x=1; class Inner: def get(self): return x
            // The class body receives 'x' as a free variable via closure.
            var classFreeVars = GetClassFreeVariables(cls.Name);

            #if DEBUG_COMPILER_LOG
            Console.WriteLine($"🔍 CompileRegularClass: {cls.Name} free variables: [{string.Join(", ", classFreeVars)}]");
            #endif

            // CPython 3.12: If class has free variables, emit LOAD_CLOSURE for each
            // before compiling class body and creating closure
            if (classFreeVars.Count > 0)
            {
                foreach (var freeVar in classFreeVars)
                {
                    #if DEBUG_COMPILER_LOG
                    Console.WriteLine($"   → LOAD_CLOSURE for class free var: {freeVar}");
                    #endif
                    EmitLoadClosure(freeVar);
                }
                EmitInstruction(ByteCodeOp.BUILD_TUPLE, classFreeVars.Count);
            }

            var classBodyCode = CompileClassBody(cls.Body, classBodyName, classFreeVars);
            EmitLoadConst(classBodyCode);

            // CPython 3.12: MAKE_FUNCTION with closure flag if class has free variables
            int makeFunctionFlags = classFreeVars.Count > 0 ? MakeFunctionFlags.CLOSURE : 0;
            EmitInstruction(ByteCodeOp.MAKE_FUNCTION, makeFunctionFlags);

            // Load class name
            EmitLoadConst(new PyString(cls.Name));
            
            // Load base classes
            #if DEBUG_LOG
            Console.WriteLine($"🔍 CompileRegularClass: {cls.Name} has {cls.Bases.Count} base classes:");
            #endif
            foreach (var baseExpr in cls.Bases)
            {
                #if DEBUG_LOG
                Console.WriteLine($"   → Base class expression: {baseExpr.GetType().Name}");
                #endif
                CompileExpression(baseExpr);
            }

            // CPython 3.12: Load metaclass if specified, using KW_NAMES for keyword arguments
            int totalArgs = 2 + cls.Bases.Count;
            if (cls.Metaclass != null)
            {
                #if DEBUG_LOG
                Console.WriteLine($"   → Metaclass specified: {cls.Metaclass}");
                #endif
                // CPython 3.12: Push metaclass value as positional argument
                CompileExpression(cls.Metaclass);
                totalArgs += 1;  // metaclass value

                // CPython 3.12: Use KW_NAMES to specify 'metaclass' keyword argument
                var kwNamesTuple = new PyTuple(new PyObject[] { new PyString("metaclass") });
                var kwNamesIndex = GetOrAddConstant(kwNamesTuple);
                EmitInstruction(ByteCodeOp.KW_NAMES, kwNamesIndex);
            }

            // Call __build_class__(class_body_function, name, *bases, metaclass=Meta)
            // Note: CALL argument count includes only positional args (keyword args handled by KW_NAMES)
            EmitInstruction(ByteCodeOp.CALL, totalArgs);

            // CPython 3.12: Apply decorators (Python/compile.c:2634-2635, 1865-1876)
            // Decorators are applied in reverse order: last decorator is called first
            // Each decorator is a CALL with 0 arguments (the class is already on the stack)
            if (cls.Decorators != null && cls.Decorators.Count > 0)
            {
                #if DEBUG_LOG
                Console.WriteLine($"🎨 Applying {cls.Decorators.Count} decorators to {cls.Name}");
                #endif

                // CPython: for (i = asdl_seq_LEN(decos) - 1; i > -1; i--)
                // Apply decorators in reverse order
                for (int i = cls.Decorators.Count - 1; i >= 0; i--)
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"   → Decorator {i}: {cls.Decorators[i].GetType().Name}");
                    #endif

                    // CPython: ADDOP_I(c, loc, CALL, 0)
                    // The decorator is already on the stack (loaded before class creation)
                    // The class is on the stack (result of __build_class__)
                    // Call decorator(class) with 0 arguments
                    EmitInstruction(ByteCodeOp.CALL, 0);
                }
            }

            // Store the created class (or decorated class)
            EmitStoreName(cls.Name);
        }

        /// <summary>
        /// Get the root symbol table by traversing up the hierarchy
        /// </summary>
        private SymbolTable GetRootSymbolTable(SymbolTable table)
        {
            var current = table;
            while (current.GetParent() != null)
            {
                current = current.GetParent()!; // Non-null assertion since we checked it's not null
            }
            return current;
        }

        /// <summary>
        /// Get free variables for a class from its symbol table or by analyzing class body
        /// </summary>
        private List<string> GetClassFreeVariables(string className)
        {
            if (_symbolTable == null)
            {
                #if DEBUG_LOG
                Console.WriteLine($"❌ _symbolTable is null, cannot look up class {className}");
                #endif
                return new List<string>();
            }

            // Search from the root of the symbol table hierarchy, not the current scope
            var rootSymbolTable = GetRootSymbolTable(_symbolTable);

            #if DEBUG_LOG
            Console.WriteLine($"🔍 GetClassFreeVariables: Looking for class '{className}' from root table '{rootSymbolTable.GetName()}'");
            #endif

            var classSymbolTable = FindSymbolTableByName(rootSymbolTable, className);

            #if DEBUG_LOG
            Console.WriteLine($"🔍 FindSymbolTableByName({className}): {(classSymbolTable != null ? "Found" : "Not found")}");
            if (classSymbolTable != null)
            {
                Console.WriteLine($"🔍 Class {className} symbol table has {classSymbolTable.GetSymbols().Count} symbols:");
                foreach (var symbol in classSymbolTable.GetSymbols().Values)
                {
                    Console.WriteLine($"  Symbol {symbol.Name}: Scope={symbol.Scope}, IsFree={symbol.IsFree()}, Flags={symbol.Flags}");
                }
                var freeVars = classSymbolTable.FindFreeVariables();
                Console.WriteLine($"🔍 Class {className} symbol table free vars: [{string.Join(", ", freeVars)}]");
                return freeVars;
            }
            else
            {
                Console.WriteLine($"❌ Symbol table for class {className} not found even from root!");
            }
            #endif

            if (classSymbolTable != null)
            {
                return classSymbolTable.FindFreeVariables();
            }

            return new List<string>();
        }

        /// <summary>
        /// Alternative method: Extract free variables by analyzing class body functions
        /// This is used when symbol table context is not available during compilation
        /// </summary>
        private List<string> GetClassFreeVariablesFromBody(List<Statement> classBody)
        {
            var freeVariables = new HashSet<string>();

            #if DEBUG_LOG
            Console.WriteLine($"🔍 GetClassFreeVariablesFromBody: Analyzing {classBody.Count} statements");
            #endif

            foreach (var statement in classBody)
            {
                if (statement is FunctionDefStatement funcDef)
                {
                    // For each method in the class, collect variables that are:
                    // 1. Referenced but not defined locally
                    // 2. Not parameters
                    var referencedVars = CollectReferencedVariables(funcDef.Body);
                    var localVars = CollectLocallyDefinedVariables(funcDef.Body);
                    // Performance: Eliminated LINQ
                    var parameters = new HashSet<string>(funcDef.Parameters);

                    foreach (var varName in referencedVars)
                    {
                        if (!localVars.Contains(varName) && !parameters.Contains(varName) && !IsKeywordOrBuiltin(varName) && !IsGlobalVariable(varName))
                        {
                            freeVariables.Add(varName);
                            #if DEBUG_LOG
                            Console.WriteLine($"  Found potential free variable: {varName} in method {funcDef.Name}");
                            #endif
                        }
                        #if DEBUG_LOG
                        else if (IsGlobalVariable(varName))
                        {
                            Console.WriteLine($"  Skipping global variable: {varName} in method {funcDef.Name}");
                        }
                        #endif
                    }
                }
            }

            // Performance: Eliminated LINQ
            var result = new List<string>(freeVariables);
            #if DEBUG_LOG
            Console.WriteLine($"🔍 GetClassFreeVariablesFromBody result: [{string.Join(", ", result)}]");
            #endif

            return result;
        }

        private HashSet<string> CollectReferencedVariables(List<Statement> statements)
        {
            var variables = new HashSet<string>();
            foreach (var statement in statements)
            {
                CollectReferencedVariablesFromStatement(statement, variables);
            }
            return variables;
        }

        private void CollectReferencedVariablesFromStatement(Statement statement, HashSet<string> variables)
        {
            switch (statement)
            {
                case ExpressionStatement exprStmt:
                    CollectReferencedVariablesFromExpression(exprStmt.Expression, variables);
                    break;
                case IfStatement ifStmt:
                    CollectReferencedVariablesFromExpression(ifStmt.Test, variables);
                    foreach (var stmt in ifStmt.Body)
                        CollectReferencedVariablesFromStatement(stmt, variables);
                    if (ifStmt.OrElse != null)
                        foreach (var stmt in ifStmt.OrElse)
                            CollectReferencedVariablesFromStatement(stmt, variables);
                    break;
                case ReturnStatement retStmt:
                    if (retStmt.Value != null)
                        CollectReferencedVariablesFromExpression(retStmt.Value, variables);
                    break;
                // Add more statement types as needed
            }
        }

        private void CollectReferencedVariablesFromExpression(Expression expression, HashSet<string> variables)
        {
            switch (expression)
            {
                case NameExpression nameExpr:
                    // Only add if it's not a keyword
                    if (!SharpPy.Generated.PyParser.IsKeyword(nameExpr.Name))
                    {
                        variables.Add(nameExpr.Name);
                    }
                    break;
                case AttributeExpression attrExpr:
                    CollectReferencedVariablesFromExpression(attrExpr.Value, variables);
                    break;
                case CallExpression callExpr:
                    CollectReferencedVariablesFromExpression(callExpr.Function, variables);
                    foreach (var arg in callExpr.Arguments)
                        CollectReferencedVariablesFromExpression(arg, variables);
                    break;
                case FStringExpression fstringExpr:
                    foreach (var value in fstringExpr.Values)
                        if (value is Expression expr)
                            CollectReferencedVariablesFromExpression(expr, variables);
                    break;
                // Add more expression types as needed
            }
        }

        private HashSet<string> CollectLocallyDefinedVariables(List<Statement> statements)
        {
            var variables = new HashSet<string>();
            foreach (var statement in statements)
            {
                if (statement is AssignStatement assignStmt)
                {
                    // AssignStatement has VariableName property, not Targets
                    // CPython 3.12: Extract names from targets
                    foreach (var target in assignStmt.Targets)
                    {
                        if (target is NameExpression nameExpr)
                            variables.Add(nameExpr.Name);
                    }
                }
            }
            return variables;
        }

        private bool IsBuiltinVariable(string varName)
        {
            // 동적으로 PyBuiltinsModule에서 builtin 여부 확인 (자동 동기화)
            return _builtinNames.Contains(varName);
        }

        private bool IsGlobalVariable(string varName)
        {
            // Check if the variable is defined in the global (module) scope
            if (_symbolTable != null)
            {
                var symbol = _symbolTable.Lookup(varName);
                if (symbol != null && (symbol.Scope == SymbolScope.Global || symbol.Scope == SymbolScope.Local))
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"  IsGlobalVariable({varName}): Found in symbol table with scope {symbol.Scope}");
                    #endif
                    return true;
                }
                #if DEBUG_LOG
                else if (symbol == null)
                {
                    Console.WriteLine($"  IsGlobalVariable({varName}): Not found in symbol table");
                }
                #endif
            }

            return false;
        }

        private bool IsKeywordOrBuiltin(string varName)
        {
            // CPython 3.12: Check using generated parser's keyword table
            return SharpPy.Generated.PyParser.IsKeyword(varName) || IsBuiltinVariable(varName);
        }

        #if DEBUG_LOG
        private void PrintSymbolTableHierarchy(SymbolTable table, int depth)
        {
            var indent = new string(' ', depth * 2);
            Console.WriteLine($"{indent}SymbolTable: {table.GetName()}");
            var children = table.GetChildren();
            Console.WriteLine($"{indent}  → {children.Count()} children");
            foreach (var child in children)
            {
                PrintSymbolTableHierarchy(child, depth + 1);
            }
        }
        #endif

        /// <summary>
        /// Compile simplified Generic Parameters function for PEP 695
        /// This creates a basic working version first
        /// </summary>
        private PyCodeObject CompileSimplifiedGenericParametersFunction(List<TypeParam> typeParams, string className, List<Statement> classBody)
        {
            // Save current compilation state
            var savedInstructionSequence = _instructionSequence;
            var savedConstants = _constants;
            var savedNames = _names;
            var savedVarNames = _varNames;
            var savedCellVars = _cellVars;
            var savedFreeVars = _freeVars;
            
            // Initialize new compilation state for generic parameters function
            _instructionSequence = new InstructionSequence();
            _constants = new List<PyObject>();
            _names = new List<string>();
            _varNames = new List<string>();
            _cellVars = new List<string>();
            _freeVars = new List<string>();
            
            try
            {
                // CPython 3.12 PEP 695: Proper Generic Parameters function setup
                
                // 1. Set up VarNames for STORE_FAST/LOAD_FAST operations 
                _varNames.Add(".generic_base");  // CPython 3.12 standard: ['.generic_base']
                
                // 2. Set up CellVars for MAKE_CELL operations (CPython 3.12 order)
                _cellVars.Add(".type_params");  // Index 1 in MAKE_CELL
                foreach (var typeParam in typeParams)
                {
                    _cellVars.Add(typeParam.Name);    // Index 2+ in MAKE_CELL
                }
                
                // Emit MAKE_CELL instructions
                // CPython 3.12: MAKE_CELL argument is localsplus offset
                // For PEP 695 generic function: nlocals=1 (.generic_base), then cells
                for (int i = 0; i < _cellVars.Count; i++)
                {
                    // Localsplus offset: nlocals + cell_index = 1 + i
                    int localsPlusOffset = _varNames.Count + i;
                    EmitInstruction(ByteCodeOp.MAKE_CELL, localsPlusOffset);
                }
                
                // 2. RESUME instruction
                EmitInstruction(ByteCodeOp.RESUME, 0);
                
                // 3. Create type parameters and store in cells
                foreach (var typeParam in typeParams)
                {
                    EmitLoadConst(new PyString(typeParam.Name));
                    EmitInstruction(ByteCodeOp.CALL_INTRINSIC_1, (int)IntrinsicFunction.INTRINSIC_TYPEVAR);
                    EmitInstruction(ByteCodeOp.COPY, 1);
                    EmitStoreDeref(typeParam.Name);
                }
                
                // 4. Build type parameters tuple and store
                EmitInstruction(ByteCodeOp.BUILD_TUPLE, typeParams.Count);
                EmitStoreDeref(".type_params");
                
                // 5. Create regular class with __build_class__
                // CPython 3.12: Python/bytecodes.c:987 - LOAD_BUILD_CLASS opcode
                // SharpPy uses LOAD_GLOBAL with pushNull=true instead
                EmitLoadGlobal("__build_class__", pushNull: true);
                
                // 6. Load closure for class body (type parameters) - CPython 3.12 uses LOAD_CLOSURE
                EmitLoadClosure(".type_params");
                EmitLoadClosure(typeParams[0].Name); // Load first type parameter (e.g., 'T')
                EmitInstruction(ByteCodeOp.BUILD_TUPLE, 2);
                
                // 7. Compile class body with closure
                var classBodyCode = CompileClassBody(classBody, $"<class_body_{className}>");
                EmitLoadConst(classBodyCode);
                EmitInstruction(ByteCodeOp.MAKE_FUNCTION, 8); // 8 = closure flag
                
                // 8. Load class name  
                EmitLoadConst(new PyString(className));
                
                // 9. Create generic base using INTRINSIC_SUBSCRIPT_GENERIC
                EmitLoadDeref(".type_params");
                EmitInstruction(ByteCodeOp.CALL_INTRINSIC_1, (int)IntrinsicFunction.INTRINSIC_SUBSCRIPT_GENERIC);
                EmitStoreFast(".generic_base");  // STORE_FAST 0 (.generic_base)
                
                // 10. Load generic base and call __build_class__
                EmitLoadFast(".generic_base");   // LOAD_FAST 0 (.generic_base)
                EmitInstruction(ByteCodeOp.CALL, 3);  // __build_class__(function, name, generic_base)
                
                // 11. Return the created class
                EmitInstruction(ByteCodeOp.RETURN_VALUE);

                // Build the code object
                var functionName = $"<generic parameters of {className}>";
                // Generic parameters wrapper has no generator flags
                var finalInstructions = GetFinalInstructions(0);
                return new PyCodeObject(
                    functionName,
                    finalInstructions,
                    _constants,
                    _names,
                    _varNames,
                    0,  // argCount
                    0,  // posonlyArgCount
                    0,  // kwonlyArgCount
                    _freeVars,    // freeVars
                    _cellVars     // cellVars
                );
            }
            finally
            {
                // Restore compilation state
                _instructionSequence = savedInstructionSequence;
                _constants = savedConstants;
                _names = savedNames;
                _varNames = savedVarNames;
                _cellVars = savedCellVars;
                _freeVars = savedFreeVars;
            }
        }
        
        /// <summary>
        /// CPython 3.12: Compile class body with optional free variables
        /// When a class is defined inside a function and its methods reference
        /// variables from the enclosing function, those variables are passed
        /// as free variables to the class body.
        /// </summary>
        /// <param name="body">Class body statements</param>
        /// <param name="className">Class name (or &lt;class_body_ClassName&gt;)</param>
        /// <param name="classFreeVars">Free variables from enclosing scope (optional)</param>
        private PyCodeObject CompileClassBody(List<Statement> body, string className, List<string>? classFreeVars = null)
        {
            // Save current compilation state
            var savedInstructionSequence = _instructionSequence;
            var savedConstants = _constants;
            var savedNames = _names;
            var savedVarNames = _varNames;
            var savedCellVars = _cellVars;
            var savedFreeVars = _freeVars;
            // Performance: Eliminated LINQ
            var savedExceptionTable = new List<ExceptionTableEntry>(_exceptionTable); // Preserve Exception Table entries
            var savedCurrentSymbolTable = _currentSymbolTable;
            var savedIsInFunction = _isInFunction;  // CPython 3.12: Save function context flag

            // CPython 3.12: Find class symbol table for this class
            SymbolTable? classSymbolTable = null;
            if (_symbolTable != null)
            {
                #if DEBUG_LOG
                Console.WriteLine($"🔍 Searching for class symbol table: {className} in root table: {_symbolTable.Name}");
                Console.WriteLine($"🔍 Root table children: {_symbolTable.Children.Count()}");
                foreach (var child in _symbolTable.Children)
                {
                    Console.WriteLine($"  Child: {child.Name}");
                }
                #endif

                // Find the class symbol table by name
                classSymbolTable = FindSymbolTableByName(_symbolTable, className);
                if (classSymbolTable == null)
                {
                    // Try extracting class name from className (remove <class_body_ prefix)
                    var actualClassName = className.Replace("<class_body_", "").TrimEnd('>');
                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 Trying actual class name: {actualClassName}");
                    #endif
                    classSymbolTable = FindSymbolTableByName(_symbolTable, actualClassName);
                }
            }
            else
            {
                #if DEBUG_LOG
                Console.WriteLine($"❌ _symbolTable is null when compiling class body {className}");
                #endif
            }

            if (classSymbolTable != null)
            {
                #if DEBUG_LOG
                Console.WriteLine($"🔍 Found class symbol table for {className}: {classSymbolTable.GetIdentifiers().Count()} symbols");
                #endif
                _currentSymbolTable = classSymbolTable;
            }
            else
            {
                #if DEBUG_LOG
                Console.WriteLine($"⚠️ Warning: No symbol table found for class {className}");
                #endif
            }

            // Initialize new compilation state for class body
            _instructionSequence = new InstructionSequence();  // CPython 3.12: Always use CFG pipeline
            _constants = new List<PyObject>();
            _names = new List<string>();
            _varNames = new List<string>();
            _cellVars = new List<string>();
            // CPython 3.12: Class bodies CAN have free variables when defined inside a function
            // and their methods reference variables from the enclosing function scope.
            // Example: def outer(): x=1; class Inner: def get(self): return x
            // In this case, Inner's class body receives 'x' as a free variable.
            _freeVars = classFreeVars != null ? new List<string>(classFreeVars) : new List<string>();
            // Keep existing Exception Table entries instead of resetting
            // _exceptionTable = new List<ExceptionTableEntry>(); // Removed: This was causing Exception Table entry loss

            // CPython 3.12: Class bodies compile statements in class scope, NOT function scope
            // This is critical for proper STORE_NAME emission for decorated methods (@property, etc.)
            _isInFunction = false;

            // CPython 3.12: Python/compile.c:2281-2361 (compiler_class)
            // Mark that we're in a class body (for proper name resolution)
            _isInClassBody = true;

            // CPython 3.12: If class has free variables, emit COPY_FREE_VARS first
            // This must come BEFORE MAKE_CELL and RESUME
            // See CPython bytecode: Inner class starts with COPY_FREE_VARS 1
            if (_freeVars.Count > 0)
            {
                #if DEBUG_COMPILER_LOG
                Console.WriteLine($"🔧 Class {className} has {_freeVars.Count} free variables: [{string.Join(", ", _freeVars)}]");
                Console.WriteLine($"🔧 Emitting COPY_FREE_VARS {_freeVars.Count}");
                #endif
                EmitInstruction(ByteCodeOp.COPY_FREE_VARS, _freeVars.Count);
            }

            // Check if class body contains super() calls and add __class__ cell variable if needed
            if (ContainsSuperCalls(body))
            {
                #if DEBUG_COMPILER_LOG
                Console.WriteLine($"🔍 Detected super() calls in class {className}, adding __class__ cell variable");
                #endif
                _cellVars.Add("__class__");

                // Generate MAKE_CELL instruction for __class__ cell variable
                // CPython 3.12: __class__ cell variable uses index 0 (first cellVar)
                var cellVarIndex = 0; // __class__ is always the first (index 0) cell variable
                #if DEBUG_COMPILER_LOG
                Console.WriteLine($"🔧 Generating MAKE_CELL for __class__ at cell index {cellVarIndex}");
                #endif
                EmitInstruction(ByteCodeOp.MAKE_CELL, cellVarIndex);
            }

            // CPython 3.12: RESUME instruction after COPY_FREE_VARS and MAKE_CELL
            EmitInstruction(ByteCodeOp.RESUME, 0);

            try
            {
                // CPython 3.12: Setup __module__ attribute in class body
                // This is equivalent to: __module__ = __name__
                EmitLoadName("__name__");  // Load current module name
                EmitStoreName("__module__");  // Store as __module__ in class dict

                // CPython 3.12: Setup __qualname__ attribute in class body
                var actualClassName = className.Contains("<class_body_") ?
                    className.Replace("<class_body_", "").TrimEnd('>') : className;
                EmitLoadConst(new PyString(actualClassName));  // Load class name
                EmitStoreName("__qualname__");  // Store as __qualname__ in class dict

                // CPython 3.12: Check if class body has annotations
                // If any statement is an AnnAssignStatement, emit SETUP_ANNOTATIONS
                // Performance: Eliminated LINQ
                bool hasAnnotations = false;
                foreach (var stmt in body)
                {
                    if (stmt is AnnAssignStatement)
                    {
                        hasAnnotations = true;
                        break;
                    }
                }
                if (hasAnnotations)
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 Class {actualClassName} has annotations, emitting SETUP_ANNOTATIONS");
                    #endif
                    EmitInstruction(ByteCodeOp.SETUP_ANNOTATIONS);
                }

                // CPython 3.12: Handle class docstring (compile.c:2295-2310)
                // First statement can be a docstring (string expression)
                int startIndex = 0;
                string? docstring = GetDocString(body);
                if (docstring != null)
                {
                    // CPython: VISIT(c, expr, st->v.Expr.value);
                    // Load docstring constant
                    var docConstIndex = GetOrAddConstant(new PyString(docstring));
                    EmitInstruction(ByteCodeOp.LOAD_CONST, docConstIndex);

                    // CPython: compiler_nameop(c, NO_LOCATION, &_Py_ID(__doc__), Store);
                    // Store in __doc__
                    EmitStoreName("__doc__");

                    // Skip first statement (it's the docstring)
                    startIndex = 1;
                }

                // Compile class body statements
                for (int i = startIndex; i < body.Count; i++)
                {
                    CompileStatement(body[i]);
                }
                
                // CPython 3.12: If __class__ cell variable exists, store __classcell__ for __build_class__
                if (_cellVars.Contains("__class__"))
                {
                    var classIndex = _cellVars.IndexOf("__class__");
                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 Generating __classcell__ store for __class__ cell at index {classIndex}");
                    #endif
                    
                    // LOAD_CLOSURE __class__ (CPython: LOAD_CLOSURE 0 (__class__))
                    EmitInstruction(ByteCodeOp.LOAD_CLOSURE, classIndex);
                    
                    // COPY 1 (CPython does this to duplicate the cell)
                    EmitInstruction(ByteCodeOp.COPY, 1);
                    
                    // STORE_NAME __classcell__ (CPython: STORE_NAME 4 (__classcell__))
                    var classcellIndex = AddName("__classcell__");
                    EmitInstruction(ByteCodeOp.STORE_NAME, classcellIndex);
                    #if DEBUG_LOG
                    Console.WriteLine($"✅ Stored __classcell__ at name index {classcellIndex}");
                    #endif
                }
                
                // Return None at the end
                EmitLoadConst(PyNone.Instance);
                EmitInstruction(ByteCodeOp.RETURN_VALUE);

                // CPython 3.12: Get final instructions
                // Class body has no generator flags
                var finalInstructions = GetFinalInstructions(0);

                // Create code object for class body with free variables
                // Performance: Eliminated LINQ
                var codeObject = new PyCodeObject(
                    className,
                    finalInstructions,
                    new List<PyObject>(_constants),
                    new List<string>(_names),
                    new List<string>(_varNames),
                    argCount: 0,  // Class body has no arguments
                    posonlyArgCount: 0,
                    kwonlyArgCount: 0,
                    freeVars: new List<string>(_freeVars),  // Include free variables
                    cellVars: new List<string>(_cellVars),
                    defaultValues: null,
                    kwDefaults: null,
                    flags: 0,
                    fileName: _currentFileName,
                    sourceLines: _sourceLines
                );
                
                return codeObject;
            }
            finally
            {
                // Restore compilation state
                _instructionSequence = savedInstructionSequence;
                _instructionSequence = savedInstructionSequence;  // CFG PATH: Restore InstructionSequence
                _constants = savedConstants;
                _names = savedNames;
                _varNames = savedVarNames;
                _cellVars = savedCellVars;
                _freeVars = savedFreeVars;
                _exceptionTable = savedExceptionTable; // Restore Exception Table entries

                // CPython 3.12: Restore symbol table context
                _currentSymbolTable = savedCurrentSymbolTable;

                // CPython 3.12: Restore function context flag
                _isInFunction = savedIsInFunction;

                // CPython 3.12: Restore class body context flag
                _isInClassBody = false;
            }
        }
        
        /// <summary>
        /// Check if class body contains super() calls (without arguments)
        /// </summary>
        public static bool ContainsSuperCalls(List<Statement> statements)
        {
            #if DEBUG_LOG
            Console.WriteLine($"🔍 Checking {statements.Count} statements for super() calls");
            #endif
            foreach (var stmt in statements)
            {
                #if DEBUG_LOG
                Console.WriteLine($"  - Checking statement: {stmt.GetType().Name}");
                #endif
                if (ContainsSuperCallsInStatement(stmt))
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"    ✅ Found super() call in {stmt.GetType().Name}");
                    #endif
                    return true;
                }
            }
            #if DEBUG_LOG
            Console.WriteLine($"  ❌ No super() calls found in {statements.Count} statements");
            #endif
            return false;
        }
        
        /// <summary>
        /// Recursively check if a statement contains super() calls
        /// </summary>
        private static bool ContainsSuperCallsInStatement(Statement stmt)
        {
            switch (stmt)
            {
                case FunctionDefStatement func:
                    // For nested functions, recursively check their bodies
                    // This ensures class bodies correctly detect super() in methods
                    return ContainsSuperCalls(func.Body);

                case IfStatement ifStmt:
                    bool result = ContainsSuperCallsInExpression(ifStmt.Test);
                    result |= ContainsSuperCalls(ifStmt.Body);
                    if (ifStmt.OrElse != null && ifStmt.OrElse.Count > 0)
                        result |= ContainsSuperCalls(ifStmt.OrElse);
                    return result;

                case TryStatement tryStmt:
                    // Check try body for super() calls
                    bool tryResult = ContainsSuperCalls(tryStmt.Body);
                    // Check except handlers for super() calls
                    foreach (var handler in tryStmt.Handlers)
                    {
                        tryResult |= ContainsSuperCalls(handler.Body);
                    }
                    // Check else clause if it exists
                    if (tryStmt.OrElse != null && tryStmt.OrElse.Count > 0)
                        tryResult |= ContainsSuperCalls(tryStmt.OrElse);
                    // Check finally clause if it exists
                    if (tryStmt.FinalBody != null && tryStmt.FinalBody.Count > 0)
                        tryResult |= ContainsSuperCalls(tryStmt.FinalBody);
                    return tryResult;

                case ExpressionStatement exprStmt:
                    return ContainsSuperCallsInExpression(exprStmt.Expression);

                case AssignStatement assignStmt:
                    return ContainsSuperCallsInExpression(assignStmt.Value);

                case ReturnStatement returnStmt:
                    if (returnStmt.Value != null)
                        return ContainsSuperCallsInExpression(returnStmt.Value);
                    return false;

                default:
                    #if DEBUG_LOG
                    Console.WriteLine($"  ⚠️  Unhandled statement type: {stmt.GetType().Name}");
                    #endif
                    return false;
            }
        }
        
        /// <summary>
        /// Check if an expression contains super() calls
        /// </summary>
        private static bool ContainsSuperCallsInExpression(Expression expr)
        {
            switch (expr)
            {
                case CallExpression call:
                    // Check if this is a super() call
                    if (call.Function is NameExpression name && name.Name == "super" && call.Arguments.Count == 0)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"    ✅ Found direct super() call");
                        #endif
                        return true;
                    }
                    // Recursively check arguments
                    foreach (var arg in call.Arguments)
                    {
                        if (ContainsSuperCallsInExpression(arg))
                            return true;
                    }
                    return ContainsSuperCallsInExpression(call.Function);

                case NameExpression:
                    return false;

                case AttributeExpression attr:
#if DEBUG_LOG
                    Console.WriteLine($"    🔍 Checking AttributeExpression: {attr.Attr}");
#endif
                    bool result = ContainsSuperCallsInExpression(attr.Value);
#if DEBUG_LOG
                    if (result) Console.WriteLine($"    ✅ Found super() in AttributeExpression.Value");
#endif
                    return result;

                case BinOpExpression binary:
                    return ContainsSuperCallsInExpression(binary.Left) ||
                           ContainsSuperCallsInExpression(binary.Right);

                case FStringExpression fstring:
                    #if DEBUG_LOG
                    Console.WriteLine($"    🔍 Checking FStringExpression with {fstring.Values.Count} values");
                    #endif
                    foreach (var value in fstring.Values)
                    {
                        if (ContainsSuperCallsInExpression(value))
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"    ✅ Found super() in FStringExpression.Value");
                            #endif
                            return true;
                        }
                    }
                    return false;

                case JoinedStrExpression joinedStr:
                    #if DEBUG_LOG
                    Console.WriteLine($"    🔍 Checking JoinedStrExpression with {joinedStr.Values.Count} values");
                    #endif
                    foreach (var value in joinedStr.Values)
                    {
                        if (ContainsSuperCallsInExpression(value))
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"    ✅ Found super() in JoinedStrExpression.Value");
                            #endif
                            return true;
                        }
                    }
                    return false;

                case FormattedValueExpression formattedValue:
                    #if DEBUG_LOG
                    Console.WriteLine($"    🔍 Checking FormattedValueExpression");
                    #endif
                    bool hasSuper = ContainsSuperCallsInExpression(formattedValue.Value);
                    if (formattedValue.FormatSpec != null)
                        hasSuper |= ContainsSuperCallsInExpression(formattedValue.FormatSpec);
                    #if DEBUG_LOG
                    if (hasSuper) Console.WriteLine($"    ✅ Found super() in FormattedValueExpression");
                    #endif
                    return hasSuper;

                case ConstantExpression:
                    return false;

                default:
                    #if DEBUG_LOG
                    Console.WriteLine($"    ⚠️  Unhandled expression type: {expr.GetType().Name}");
                    #endif
                    return false;
            }
        }
        
        /// <summary>
        /// 클래스 body에서 사용되는 자유변수를 분석하는 클래스
        /// </summary>
        private class ClassBodyFreeVariableAnalyzer
        {
            public List<string> AnalyzeClassBody(List<Statement> body, List<string> parentScopeNames)
            {
                var usedNames = new HashSet<string>();
                var definedNames = new HashSet<string>();
                
                // 클래스 body에서 정의되는 이름들 수집
                foreach (var stmt in body)
                {
                    CollectDefinedNames(stmt, definedNames);
                }
                
                // 클래스 body에서 사용되는 이름들 수집
                foreach (var stmt in body)
                {
                    CollectUsedNames(stmt, usedNames);
                }
                
                // 사용되지만 클래스 내부에서 정의되지 않은 이름들 = 자유변수
                var freeVars = new List<string>();
                foreach (var name in usedNames)
                {
                    // 클래스 내부에서 정의되지 않고, 부모 스코프에 존재하는 변수들
                    if (!definedNames.Contains(name) && parentScopeNames.Contains(name))
                    {
                        freeVars.Add(name);
                    }
                }
                
                return freeVars;
            }
            
            private void CollectDefinedNames(Statement stmt, HashSet<string> definedNames)
            {
                switch (stmt)
                {
                    case FunctionDefStatement func:
                        definedNames.Add(func.Name);
                        break;
                    case AssignStatement assign:
                        // CPython 3.12: Extract names from targets list
                        foreach (var target in assign.Targets)
                        {
                            if (target is NameExpression nameExpr)
                                definedNames.Add(nameExpr.Name);
                        }
                        break;
                    case AssignTargetStatement assignTarget:
                        if (assignTarget.Target is NameExpression nameExpr2)
                            definedNames.Add(nameExpr2.Name);
                        break;
                    // 다른 정의 구문들도 필요시 추가
                }
            }
            
            private void CollectUsedNames(Statement stmt, HashSet<string> usedNames)
            {
                switch (stmt)
                {
                    case FunctionDefStatement func:
                        // 데코레이터에서 사용되는 이름들
                        foreach (var decorator in func.Decorators)
                        {
                            CollectUsedNamesFromExpression(decorator.DecoratorFunction, usedNames);
                        }
                        // 함수 body는 별도 스코프이므로 분석하지 않음
                        break;
                    case ExpressionStatement exprStmt:
                        CollectUsedNamesFromExpression(exprStmt.Expression, usedNames);
                        break;
                    case AssignStatement assign:
                        CollectUsedNamesFromExpression(assign.Value, usedNames);
                        break;
                    case AssignTargetStatement assignTarget:
                        CollectUsedNamesFromExpression(assignTarget.Value, usedNames);
                        break;
                    // 다른 구문들도 필요시 추가
                }
            }
            
            private void CollectUsedNamesFromExpression(Expression expr, HashSet<string> usedNames)
            {
                switch (expr)
                {
                    case NameExpression nameExpr:
                        usedNames.Add(nameExpr.Name);
                        break;
                    case CallExpression callExpr:
                        CollectUsedNamesFromExpression(callExpr.Function, usedNames);
                        foreach (var arg in callExpr.Arguments)
                            CollectUsedNamesFromExpression(arg, usedNames);
                        break;
                    case AttributeExpression attrExpr:
                        CollectUsedNamesFromExpression(attrExpr.Value, usedNames);
                        break;
                    // 다른 표현식들도 필요시 추가
                }
            }
        }

        private void CompileTypeAlias(TypeAliasStatement typeAlias)
        {
            // PEP 695: type X[T] = Y creates a TypeAliasType object
            // For type aliases with type parameters, we need to make the parameters available
            // This is a simplified implementation - type parameters are bound as variables

            // Bind type parameters to the current scope
            foreach (var typeParam in typeAlias.TypeParams)
            {
                // Create type parameter objects and bind them to variables
                EmitLoadConst(new PyString(typeParam.Name)); // Type parameter name as placeholder
                EmitStoreName(typeParam.Name); // Bind to current scope
            }
            
            // Compile the type expression (right-hand side) with type parameters available
            CompileExpression(typeAlias.Value);
            
            // Store the result with the alias name
            EmitStoreName(typeAlias.Name);
        }

        
        private void CompileImport(ImportStatement import)
        {
            foreach (var moduleName in import.Names)
            {
                // Handle "module as alias" format
                string actualModule, alias;
                if (moduleName.Contains(" as "))
                {
                    var parts = moduleName.Split(new[] { " as " }, StringSplitOptions.RemoveEmptyEntries);
                    actualModule = parts[0].Trim();
                    alias = parts[1].Trim();
                }
                else
                {
                    actualModule = moduleName;
                    // CPython 3.12: For dotted imports without 'as', store only the first part
                    // Example: import collections.abc → STORE_NAME collections (not collections.abc)
                    var dotIndex = actualModule.IndexOf('.');
                    alias = (dotIndex != -1) ? actualModule.Substring(0, dotIndex) : actualModule;
                }

                // CPython 3.12 pattern: LOAD_CONST(0), LOAD_CONST(None), IMPORT_NAME
                var levelIndex = GetOrAddConstant(new PyInt(0));  // fromlist level
                var fromlistIndex = GetOrAddConstant(PyNone.Instance);  // fromlist
                var moduleIndex = GetOrAddConstant(new PyString(actualModule));

                EmitInstruction(ByteCodeOp.LOAD_CONST, levelIndex);
                EmitInstruction(ByteCodeOp.LOAD_CONST, fromlistIndex);
                EmitInstruction(ByteCodeOp.IMPORT_NAME, moduleIndex);

                // Store the imported module in the correct variable name
                // CPython 3.12: Use STORE_FAST in functions, STORE_NAME at module level
                EmitStoreVariable(alias);
            }
        }
        private void CompileImportFrom(ImportFromStatement importFrom)
        {
            // CPython 3.12: IMPORT_NAME expects (level, fromlist) on stack
            // 1. LOAD_CONST - level (0 for absolute import, 1 for ., 2 for .., etc)
            var levelIndex = GetOrAddConstant(new PyInt(importFrom.Level));
            EmitInstruction(ByteCodeOp.LOAD_CONST, levelIndex);

            // 2. LOAD_CONST - fromlist (tuple of imported names)
            // Performance: Eliminated LINQ
            var fromlistItems = new PyObject[importFrom.Names.Count];
            for (int i = 0; i < importFrom.Names.Count; i++)
            {
                fromlistItems[i] = new PyString(importFrom.Names[i].Name);
            }
            var fromlist = new PyTuple(fromlistItems);
            var fromlistIndex = GetOrAddConstant(fromlist);
            EmitInstruction(ByteCodeOp.LOAD_CONST, fromlistIndex);

            // 3. IMPORT_NAME - module name
            var moduleIndex = GetOrAddConstant(new PyString(importFrom.Module ?? ""));
            EmitInstruction(ByteCodeOp.IMPORT_NAME, moduleIndex);

            // 4. For each imported name, emit IMPORT_FROM and STORE
            // CPython 3.12: Check for "import *" case (Python/compile.c:3864-3868)
            if (importFrom.Names.Count == 1 && importFrom.Names[0].Name == "*")
            {
                // from module import * - use INTRINSIC_IMPORT_STAR
                EmitInstruction(ByteCodeOp.CALL_INTRINSIC_1, (int)IntrinsicFunction.INTRINSIC_IMPORT_STAR);
                EmitInstruction(ByteCodeOp.POP_TOP);
                return;
            }

            foreach (var importAlias in importFrom.Names)
            {
                // Extract actual item name and alias from ImportAlias object
                string actualItem = importAlias.Name;
                string alias = importAlias.AsName ?? importAlias.Name;

                // Emit IMPORT_FROM bytecode
                var itemIndex = GetOrAddConstant(new PyString(actualItem));
                EmitInstruction(ByteCodeOp.IMPORT_FROM, itemIndex);

                // Store the imported item in the correct variable name
                // CPython 3.12: Use STORE_FAST in functions, STORE_NAME at module level
                EmitStoreName(alias);
            }

            // Pop the module from stack (cleanup)
            EmitInstruction(ByteCodeOp.POP_TOP);
        }
        /// <summary>
        /// CPython-style if statement compilation - supports if-elif-else chains
        /// CPython 3.12: Uses InstructionSequence with Labels
        /// </summary>
        private void CompileIf(IfStatement ifStmt)
        {
#if DEBUG_COMPILER_LOG
            Console.WriteLine($"🔷 [CFG] CompileIf: Using InstructionSequence with Labels (CPython 3.12)");
#endif
            _cfgPathCount++;
            CompileIfWithLabels(ifStmt);
        }

        /// <summary>
        /// CPython 3.12: Label-based if-elif-else compilation
        /// Uses InstructionSequence.NewLabel() and UseLabel()
        /// No manual offset calculation needed!
        /// </summary>
        private void CompileIfWithLabels(IfStatement ifStmt)
        {
            // Collect all if/elif conditions
            var conditions = new List<(Expression Test, List<Statement> Body)>();
            var currentIf = ifStmt;

            while (currentIf != null)
            {
                conditions.Add((currentIf.Test, currentIf.Body));

                if (currentIf.OrElse != null && currentIf.OrElse.Count == 1 &&
                    currentIf.OrElse[0] is IfStatement nextIf)
                {
                    currentIf = nextIf;
                }
                else
                {
                    break;
                }
            }

            // Create labels for each elif start and the end
            // Use SharpPy.Label (from InstructionSequence.cs), NOT PythonCompiler.Label (LEGACY)
            var nextConditionLabels = new List<SharpPy.Label>();
            for (int i = 0; i < conditions.Count; i++)
            {
                nextConditionLabels.Add(_instructionSequence!.NewLabel());
            }
            var endLabel = _instructionSequence!.NewLabel();
            var elseLabel = _instructionSequence!.NewLabel();

            // Find final else
            var finalElse = ifStmt;
            while (finalElse.OrElse != null && finalElse.OrElse.Count == 1 &&
                   finalElse.OrElse[0] is IfStatement)
            {
                finalElse = (IfStatement)finalElse.OrElse[0];
            }
            bool hasElse = finalElse.OrElse != null && finalElse.OrElse.Count > 0;

            // Compile each condition and body
            for (int i = 0; i < conditions.Count; i++)
            {
                var (test, body) = conditions[i];

                // Mark this condition's start (for previous elif to jump to)
                if (i > 0)
                {
                    _instructionSequence.UseLabel(nextConditionLabels[i - 1]);
                }

                // Compile condition
                CompileExpression(test);

                // If false, jump to next elif (or else, or end)
                SharpPy.Label falseTarget;
                if (i + 1 < conditions.Count)
                {
                    // Jump to next elif
                    falseTarget = nextConditionLabels[i];
                }
                else if (hasElse)
                {
                    // Jump to else block
                    falseTarget = elseLabel;
                }
                else
                {
                    // Jump to end
                    falseTarget = endLabel;
                }

                _instructionSequence.AddOpWithLabel(
                    ByteCodeOp.POP_JUMP_IF_FALSE,
                    falseTarget,
                    _currentLineNumber,
                    _currentColumnOffset,
                    _currentFileName
                );

                // Compile body
                foreach (var stmt in body)
                {
                    CompileStatement(stmt);
                }

                // CPython pattern: JUMP_FORWARD to end if there's more code after
                bool isLastCondition = (i == conditions.Count - 1);
                bool needsJump = !isLastCondition || hasElse;

                if (needsJump)
                {
                    _instructionSequence.AddOpWithLabel(
                        ByteCodeOp.JUMP,
                        endLabel,
                        _currentLineNumber,
                        _currentColumnOffset,
                        _currentFileName
                    );
                }
            }

            // Compile else block
            if (hasElse)
            {
                _instructionSequence.UseLabel(elseLabel);
                foreach (var stmt in finalElse.OrElse)
                {
                    CompileStatement(stmt);
                }
            }

            // Mark end of if-elif-else
            _instructionSequence.UseLabel(endLabel);
        }

        /// <summary>
        /// CPython 3.12 호환 while True: compilation
        /// 특징: 조건 체크 없이 바로 루프 바디 시작, NOP 삽입
        /// </summary>
        /// <summary>
        /// CPython 3.12: while True loop compilation
        /// </summary>
        private void CompileWhileTrue(WhileStatement whileStmt)
        {
#if DEBUG_COMPILER_LOG
            Console.WriteLine($"🔷 [CFG] CompileWhileTrue: Using InstructionSequence with Labels");
#endif
            _cfgPathCount++;
            CompileWhileTrueWithLabels(whileStmt);
        }

        /// <summary>
        /// NEW: Label-based while True compilation (CPython 3.12)
        /// Uses InstructionSequence with SharpPy.Label objects
        /// </summary>
        private void CompileWhileTrueWithLabels(WhileStatement whileStmt)
        {
            #if DEBUG_LOG
            Console.WriteLine("🔧 CPython 3.12 호환 while True 루프 컴파일 (Label-based)");
            #endif

            // Create labels for break/continue (use SharpPy.Label, NOT PythonCompiler.Label)
            var breakLabel = _instructionSequence!.NewLabel();
            var continueLabel = _instructionSequence!.NewLabel();

            // Push while loop fblock (CPython 3.12: PushFBlock)
            var loc = new SourceLocation(_currentLineNumber, _currentColumnOffset);
            PushFBlock(loc, FBlockType.WHILE_LOOP, continueLabel, breakLabel, null);

            // CPython pattern: emit NOP for while True:
            _instructionSequence.AddOp(
                ByteCodeOp.NOP,
                _currentLineNumber,
                _currentColumnOffset,
                _currentFileName
            );

            // Mark loop body start (continue target)
            _instructionSequence.UseLabel(continueLabel);

            // Compile loop body
            foreach (var stmt in whileStmt.Body)
            {
                CompileStatement(stmt);
            }

            // JUMP_BACKWARD to loop start
            _instructionSequence.AddOpWithLabel(
                ByteCodeOp.JUMP,
                continueLabel,
                _currentLineNumber,
                _currentColumnOffset,
                _currentFileName
            );

            // Pop loop fblock (CPython 3.12: PopFBlock)
            PopFBlock(FBlockType.WHILE_LOOP, continueLabel);

            // Mark break label
            _instructionSequence.UseLabel(breakLabel);

            #if DEBUG_LOG
            Console.WriteLine("🔧 CPython 3.12 호환 while True 루프 컴파일 완료 (Label-based)");
            #endif
        }

        /// <summary>
        /// CPython 3.12: compiler_add_yield_from implementation
        /// Corresponds to Python/compile.c:1497-1519
        /// Generates the SEND loop pattern for yield from delegation
        /// </summary>
        private void CompileYieldFrom(bool isAwait = false)
        {
            // CPython pattern from compiler_add_yield_from:
            // NEW_JUMP_TARGET_LABEL(c, send);
            // NEW_JUMP_TARGET_LABEL(c, fail);
            // NEW_JUMP_TARGET_LABEL(c, exit);
            var sendLabel = _instructionSequence!.NewLabel();
            var failLabel = _instructionSequence!.NewLabel();
            var exitLabel = _instructionSequence!.NewLabel();

            // CPython: ADDOP_LOAD_CONST(c, loc, Py_None);
            // IMPORTANT: Must be BEFORE USE_LABEL so sendLabel points to SEND, not LOAD_CONST
            EmitLoadConst(PyNone.Instance);

            // USE_LABEL(c, send);
            // sendLabel points to SEND instruction (JUMP_BACKWARD loops back to SEND)
            _instructionSequence.UseLabel(sendLabel);

            // ADDOP_JUMP(c, loc, SEND, exit);
            _instructionSequence.AddOpWithLabel(ByteCodeOp.SEND, exitLabel, _currentLineNumber);

            // Set up a virtual try/except to handle when StopIteration is raised during
            // a close or throw call. The only way YIELD_VALUE raises if they do!
            // ADDOP_JUMP(c, loc, SETUP_FINALLY, fail);
            _instructionSequence.AddOpWithLabel(ByteCodeOp.SETUP_FINALLY, failLabel, _currentLineNumber);

            // ADDOP_I(c, loc, YIELD_VALUE, 0);
            EmitInstruction(ByteCodeOp.YIELD_VALUE, 0);

            // ADDOP(c, NO_LOCATION, POP_BLOCK);
            EmitInstruction(ByteCodeOp.POP_BLOCK);

            // ADDOP_I(c, loc, RESUME, await ? 3 : 2);
            EmitInstruction(ByteCodeOp.RESUME, isAwait ? 3 : 2);

            // ADDOP_JUMP(c, loc, JUMP_NO_INTERRUPT, send);
            // CPython: Use JUMP_NO_INTERRUPT, assembler will convert to JUMP_BACKWARD_NO_INTERRUPT
            _instructionSequence.AddOpWithLabel(ByteCodeOp.JUMP_NO_INTERRUPT, sendLabel, _currentLineNumber);

            // USE_LABEL(c, fail);
            _instructionSequence.UseLabel(failLabel);

            // ADDOP(c, loc, CLEANUP_THROW);
            EmitInstruction(ByteCodeOp.CLEANUP_THROW);

            // USE_LABEL(c, exit);
            _instructionSequence.UseLabel(exitLabel);

            // ADDOP(c, loc, END_SEND);
            EmitInstruction(ByteCodeOp.END_SEND);

#if DEBUG_COMPILER_LOG
            Console.WriteLine($"✅ [CFG] CompileYieldFrom: Generated SEND loop pattern (await={isAwait})");
#endif
        }

        /// <summary>
        /// CPython 3.12: wrap_in_stopiteration_handler (compile.c:2118-2134)
        /// Wraps generator/coroutine body with StopIteration exception handler
        /// Inserts SETUP_CLEANUP at start and CALL_INTRINSIC_1 + RERAISE at end
        /// </summary>
        private void WrapInStopIterationHandler()
        {
            // CPython: NEW_JUMP_TARGET_LABEL(c, handler);
            var handlerLabel = _instructionSequence!.NewLabel();

            // CPython: Insert SETUP_CLEANUP at start (position 0)
            // RETURN_IF_ERROR(instr_sequence_insert_instruction(
            //     INSTR_SEQUENCE(c), 0, SETUP_CLEANUP, handler.id, NO_LOCATION));
            _instructionSequence.InsertAt(0, new Instruction(
                ByteCodeOp.SETUP_CLEANUP,
                handlerLabel,
                _currentLineNumber,
                _currentColumnOffset,
                _currentFileName
            ));

            // CPython: ADDOP_LOAD_CONST(c, NO_LOCATION, Py_None);
            EmitLoadConst(PyNone.Instance);

            // CPython: ADDOP(c, NO_LOCATION, RETURN_VALUE);
            EmitInstruction(ByteCodeOp.RETURN_VALUE);

            // CPython: USE_LABEL(c, handler);
            _instructionSequence.UseLabel(handlerLabel);

            // CPython: ADDOP_I(c, NO_LOCATION, CALL_INTRINSIC_1, INTRINSIC_STOPITERATION_ERROR);
            EmitInstruction(ByteCodeOp.CALL_INTRINSIC_1, (int)IntrinsicFunction.INTRINSIC_STOPITERATION_ERROR);

            // CPython: ADDOP_I(c, NO_LOCATION, RERAISE, 1);
            EmitInstruction(ByteCodeOp.RERAISE, 1);

#if DEBUG_COMPILER_LOG
            Console.WriteLine($"✅ [CFG] WrapInStopIterationHandler: Added SETUP_CLEANUP wrapper for generator");
#endif
        }

        /// <summary>
        /// CPython 3.12 완전 호환 while loop compilation
        /// 특징: 조건을 두 번 체크 (초기 + 루프 끝)
        /// </summary>
        private void CompileWhile(WhileStatement whileStmt)
        {
            #if DEBUG_LOG
            Console.WriteLine("🔧 CPython 3.12 호환 while 루프 컴파일 시작");
            #endif

            // Check if this is while True: case
            bool isWhileTrue = IsConstantTrue(whileStmt.Test);
            #if DEBUG_LOG
            Console.WriteLine($"  while True 패턴: {isWhileTrue}");
            #endif

            if (isWhileTrue)
            {
                CompileWhileTrue(whileStmt);
                return;
            }

            // CFG implementation (CPython 3.12)
#if DEBUG_COMPILER_LOG
            Console.WriteLine($"🔷 [CFG] CompileWhile: Using InstructionSequence with Labels (CPython 3.12)");
#endif
            _cfgPathCount++;
            CompileWhileWithLabels(whileStmt);
        }

        /// <summary>
        /// NEW: Label-based while compilation (CPython 3.12)
        /// Uses InstructionSequence with SharpPy.Label objects
        /// </summary>
        private void CompileWhileWithLabels(WhileStatement whileStmt)
        {
            #if DEBUG_LOG
            Console.WriteLine("🔧 CPython 3.12 호환 while 루프 컴파일 (Label-based with loop rotation)");
            #endif

            // Create labels for loop control
            // CPython 3.12: Python/compile.c:3270-3320 (compiler_while)
            var loopStartLabel = _instructionSequence!.NewLabel();   // Initial condition check (continue target)
            var loopBodyLabel = _instructionSequence!.NewLabel();    // Loop body start
            var elseLabel = _instructionSequence!.NewLabel();        // else clause (condition false target)
            var endLabel = _instructionSequence!.NewLabel();         // break target (after else clause)

            // Push loop context - break should jump past else clause
            var loc = new SourceLocation(_currentLineNumber, _currentColumnOffset);
            PushFBlock(loc, FBlockType.WHILE_LOOP, loopStartLabel, endLabel, null);

            // Phase 1: Initial condition check at loop start
            _instructionSequence.UseLabel(loopStartLabel);
            CompileExpression(whileStmt.Test);

            // If condition is false, jump to else clause (or end if no else)
            _instructionSequence.AddOpWithLabel(
                ByteCodeOp.POP_JUMP_IF_FALSE,
                elseLabel,
                _currentLineNumber,
                _currentColumnOffset,
                _currentFileName
            );

            // Phase 2: Mark loop body start and compile loop body
            _instructionSequence.UseLabel(loopBodyLabel);
            foreach (var stmt in whileStmt.Body)
            {
                CompileStatement(stmt);
            }

            // Phase 3: CPython 3.12 loop rotation - re-check condition at loop end
            CompileExpression(whileStmt.Test);

            // If condition is still true, jump back to loop body (skip initial check)
            _instructionSequence.AddOpWithLabel(
                ByteCodeOp.POP_JUMP_IF_FALSE,
                elseLabel,
                _currentLineNumber,
                _currentColumnOffset,
                _currentFileName
            );

            // Jump back to loop body
            _instructionSequence.AddOpWithLabel(
                ByteCodeOp.JUMP,
                loopBodyLabel,
                _currentLineNumber,
                _currentColumnOffset,
                _currentFileName
            );

            // Pop loop context (CPython 3.12: PopFBlock)
            PopFBlock(FBlockType.WHILE_LOOP, loopStartLabel);

            // Phase 4: Mark else clause position
            _instructionSequence.UseLabel(elseLabel);

            // Compile else clause if present (executed when loop exits normally, not via break)
            if (whileStmt.ElseClause != null && whileStmt.ElseClause.Count > 0)
            {
                foreach (var stmt in whileStmt.ElseClause)
                {
                    CompileStatement(stmt);
                }
            }

            // Phase 5: Mark end position (break target)
            _instructionSequence.UseLabel(endLabel);

            #if DEBUG_LOG
            Console.WriteLine("🔧 CPython 3.12 호환 while 루프 컴파일 완료 (with loop rotation)");
            #endif
        }

        /// <summary>
        /// Check if an expression is a constant True value
        /// </summary>
        private bool IsConstantTrue(Expression expr)
        {
            // Check for constant True
            if (expr is ConstantExpression constExpr)
            {
                return constExpr.Value == PyBool.True;
            }
            
            // Check for name reference to True
            if (expr is NameExpression nameExpr && nameExpr.Name == "True")
            {
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// CPython-style for loop compilation - uses FOR_ITER opcode with proper StopIteration handling
        /// Router: Dispatch to CFG or LEGACY implementation
        /// </summary>
        private void CompileFor(ForStatement forStmt)
        {
#if DEBUG_COMPILER_LOG
            Console.WriteLine($"🔷 [CFG] CompileFor: Using InstructionSequence with Labels (CPython 3.12)");
#endif
            _cfgPathCount++;
            CompileForWithLabels(forStmt);
        }

        /// <summary>
        /// NEW: Label-based for loop compilation (CPython 3.12)
        /// Uses InstructionSequence with SharpPy.Label objects
        /// CPython pattern: GET_ITER → FOR_ITER → body → JUMP → END_FOR
        /// </summary>
        private void CompileForWithLabels(ForStatement forStmt)
        {
#if DEBUG_LOG
            Console.WriteLine("🔧 CPython 3.12 호환 for 루프 컴파일 (Label-based)");
#endif

            // CPython 3.12 pattern: 4 labels (start, body, cleanup, end)
            var startLabel = _instructionSequence.NewLabel();    // FOR_ITER 위치
            var bodyLabel = _instructionSequence.NewLabel();     // 루프 본문 시작
            var cleanupLabel = _instructionSequence.NewLabel();  // END_FOR 위치
            var endLabel = _instructionSequence.NewLabel();      // 전체 종료

            // 1. Get iterator from iterable
            CompileExpression(forStmt.Iter);

            // CPython 3.12: Generator function의 .0 매개변수는 이미 iterator
            bool skipGetIter = forStmt.Iter is NameExpression nameExpr && nameExpr.Name == ".0";
            if (!skipGetIter)
            {
                EmitInstruction(ByteCodeOp.GET_ITER);
            }

            // 2. Mark start label and emit FOR_ITER
            _instructionSequence.UseLabel(startLabel);
            _instructionSequence.AddOpWithLabel(
                ByteCodeOp.FOR_ITER,
                cleanupLabel,  // Jump to cleanup (END_FOR) on StopIteration
                _currentLineNumber,
                _currentColumnOffset,
                _currentFileName
            );

            // 3. Mark body label
            _instructionSequence.UseLabel(bodyLabel);

            // 4. FOR_ITER pushes next value on stack, store it in loop variable
            CompileAssignmentTarget(forStmt.Target);

            // 5. Set up loop context for break/continue (CPython 3.12: PushFBlock)
            // Break → endLabel, Continue → startLabel
            var loc = new SourceLocation(_currentLineNumber, _currentColumnOffset);
            PushFBlock(loc, FBlockType.FOR_LOOP, startLabel, endLabel, null);

            // 6. Execute loop body
            foreach (var stmt in forStmt.Body)
            {
                CompileStatement(stmt);
            }

            // 7. Jump back to FOR_ITER
            // CPython 3.12: Uses JUMP_BACKWARD for backward jumps
            _instructionSequence.AddOpWithLabel(
                ByteCodeOp.JUMP,
                startLabel,
                _currentLineNumber,
                _currentColumnOffset,
                _currentFileName
            );

            // 8. Mark cleanup label and emit END_FOR
            _instructionSequence.UseLabel(cleanupLabel);
            EmitInstruction(ByteCodeOp.END_FOR, 0);

            // 9. Pop loop context (CPython 3.12: PopFBlock)
            PopFBlock(FBlockType.FOR_LOOP, startLabel);

            // 10. Execute else clause if present
            if (forStmt.ElseClause != null && forStmt.ElseClause.Count > 0)
            {
                foreach (var stmt in forStmt.ElseClause)
                {
                    CompileStatement(stmt);
                }
            }

            // 11. Mark end label
            _instructionSequence.UseLabel(endLabel);

#if DEBUG_LOG
            Console.WriteLine("🔧 CPython 3.12 for 루프 컴파일 완료 (Label-based)");
#endif
        }

        /// <summary>
        /// CPython 3.12: async for loop compilation (Python/compile.c:3059-3106)
        /// async for target in iter:
        ///     body
        /// else:
        ///     orelse
        /// </summary>
        private void CompileAsyncFor(AsyncForStatement asyncForStmt)
        {
#if DEBUG_COMPILER_LOG
            Console.WriteLine($"🔷 [CFG] CompileAsyncFor: Using InstructionSequence with Labels (CPython 3.12)");
#endif

            // CPython: Check if we're in an async function
            var loc = new SourceLocation(_currentLineNumber, _currentColumnOffset);

            // TODO: Add proper scope type checking when we have _scopeType field
            // if (_scopeType != CompilerScopeType.ASYNC_FUNCTION && !_isTopLevelAwait)
            // {
            //     throw new SyntaxErrorException("'async for' outside async function", _currentFileName, loc.Line, loc.Column);
            // }

            // CPython 3.12 pattern: 3 labels (start, except, end)
            var startLabel = _instructionSequence.NewLabel();    // GET_ANEXT location
            var exceptLabel = _instructionSequence.NewLabel();   // Exception handler (END_ASYNC_FOR)
            var endLabel = _instructionSequence.NewLabel();      // Loop exit

            // 1. Get async iterator from iterable
            // CPython: VISIT(c, expr, s->v.AsyncFor.iter);
            CompileExpression(asyncForStmt.Iter);

            // CPython: ADDOP(c, LOC(s->v.AsyncFor.iter), GET_AITER);
            EmitInstruction(ByteCodeOp.GET_AITER);

            // 2. Mark start label
            // CPython: USE_LABEL(c, start);
            _instructionSequence.UseLabel(startLabel);

            // 3. Push FOR_LOOP fblock for break/continue
            // CPython: compiler_push_fblock(c, loc, FOR_LOOP, start, end, NULL);
            PushFBlock(loc, FBlockType.FOR_LOOP, startLabel, endLabel, null);

            // 4. Setup exception handler for __anext__ call
            // CPython: ADDOP_JUMP(c, loc, SETUP_FINALLY, except);
            _instructionSequence.AddOpWithLabel(
                ByteCodeOp.SETUP_FINALLY,
                exceptLabel,
                _currentLineNumber,
                _currentColumnOffset,
                _currentFileName
            );

            // 5. Get next value from async iterator
            // CPython: ADDOP(c, loc, GET_ANEXT);
            EmitInstruction(ByteCodeOp.GET_ANEXT);

            // 6. Await the result (yield from pattern)
            // CPython: ADDOP_LOAD_CONST(c, loc, Py_None);
            // CPython: ADD_YIELD_FROM(c, loc, 1);  // await=1
            EmitLoadConst(PyNone.Instance);
            CompileYieldFrom(isAwait: true);  // This will emit SEND/YIELD_VALUE/RESUME sequence

            // 7. Pop exception handler block
            // CPython: ADDOP(c, loc, POP_BLOCK);
            EmitInstruction(ByteCodeOp.POP_BLOCK);

            // 8. Store value to target variable
            // CPython: VISIT(c, expr, s->v.AsyncFor.target);
            EmitStoreName(asyncForStmt.Target);

            // 9. Execute loop body
            // CPython: VISIT_SEQ(c, stmt, s->v.AsyncFor.body);
            foreach (var stmt in asyncForStmt.Body)
            {
                CompileStatement(stmt);
            }

            // 10. Jump back to start (loop again)
            // CPython: ADDOP_JUMP(c, NO_LOCATION, JUMP, start);
            _instructionSequence.AddOpWithLabel(
                ByteCodeOp.JUMP,
                startLabel,
                _currentLineNumber,
                _currentColumnOffset,
                _currentFileName
            );

            // 11. Pop FOR_LOOP fblock
            // CPython: compiler_pop_fblock(c, FOR_LOOP, start);
            PopFBlock(FBlockType.FOR_LOOP, startLabel);

            // 12. Mark exception handler label
            // CPython: USE_LABEL(c, except);
            _instructionSequence.UseLabel(exceptLabel);

            // 13. Handle StopAsyncIteration
            // CPython: ADDOP(c, loc, END_ASYNC_FOR);
            EmitInstruction(ByteCodeOp.END_ASYNC_FOR);

            // 14. Execute else clause if present
            // CPython: VISIT_SEQ(c, stmt, s->v.For.orelse);  // Note: typo in CPython, should be AsyncFor
            if (asyncForStmt.ElseClause != null && asyncForStmt.ElseClause.Count > 0)
            {
                foreach (var stmt in asyncForStmt.ElseClause)
                {
                    CompileStatement(stmt);
                }
            }

            // 15. Mark end label
            // CPython: USE_LABEL(c, end);
            _instructionSequence.UseLabel(endLabel);

#if DEBUG_COMPILER_LOG
            Console.WriteLine("🔷 CPython 3.12 async for 루프 컴파일 완료");
#endif
        }

        /// <summary>
        /// Compile for loop with tuple unpacking (e.g., for key, value in items:)
        /// </summary>
        private void CompileForTuple(ForTupleStatement forTupleStmt)
        {
            // CPython 3.12: Label-based control flow (no manual offset calculation)

            // 1. Get iterator from iterable
            CompileExpression(forTupleStmt.Iter);  // Push iterable on stack
            EmitInstruction(ByteCodeOp.GET_ITER); // Convert to iterator

            // 2. Create labels for loop control flow
            var startLabel = _instructionSequence.NewLabel();
            var endLabel = _instructionSequence.NewLabel();
            var breakLabel = _instructionSequence.NewLabel();
            var continueLabel = _instructionSequence.NewLabel();

            // 3. Mark loop start
            _instructionSequence.UseLabel(startLabel);

            // 4. FOR_ITER jumps to cleanup on StopIteration
            _instructionSequence.AddOpWithLabel(
                ByteCodeOp.FOR_ITER,
                endLabel,
                _currentLineNumber,
                _currentColumnOffset,
                _currentFileName
            );

            // 5. FOR_ITER pushes the next value on stack, unpack it into target variables
            EmitInstruction(ByteCodeOp.UNPACK_SEQUENCE, forTupleStmt.Targets.Count);

            // 6. Store each unpacked value in the target variables
            for (int i = 0; i < forTupleStmt.Targets.Count; i++)
            {
                EmitStoreName(forTupleStmt.Targets[i]);
            }

            // 7. Push loop fblock for break/continue (CPython 3.12: PushFBlock)
            var loc = new SourceLocation(_currentLineNumber, _currentColumnOffset);
            PushFBlock(loc, FBlockType.FOR_LOOP, startLabel, breakLabel, null);

            // 8. Execute loop body
            foreach (var stmt in forTupleStmt.Body)
            {
                CompileStatement(stmt);
            }

            // 9. Mark continue label
            _instructionSequence.UseLabel(continueLabel);

            // 10. Jump back to loop start (CPython 3.12: uses JUMP, assembler determines direction)
            _instructionSequence.AddOpWithLabel(
                ByteCodeOp.JUMP,
                startLabel,
                -1,  // NO_LOCATION
                -1,
                _currentFileName
            );

            // 11. Cleanup label - loop exits here
            _instructionSequence.UseLabel(endLabel);
            EmitInstruction(ByteCodeOp.END_FOR, 0);

            // 12. Pop loop fblock (CPython 3.12: PopFBlock)
            PopFBlock(FBlockType.FOR_LOOP, startLabel);

            // 13. Execute else clause if present
            if (forTupleStmt.ElseClause != null && forTupleStmt.ElseClause.Count > 0)
            {
                foreach (var stmt in forTupleStmt.ElseClause)
                {
                    CompileStatement(stmt);
                }
            }

            // 14. Mark break label
            _instructionSequence.UseLabel(breakLabel);
        }
        
        /// <summary>
        /// Compile for loop with complex tuple unpacking (e.g., for i, (name, value) in enumerate(tests):)
        /// </summary>
        private void CompileForComplex(ForComplexStatement forComplexStmt)
        {
            // CPython 3.12: Label-based control flow (no manual offset calculation)

            // 1. Get iterator from iterable
            CompileExpression(forComplexStmt.Iter);  // Push iterable on stack
            EmitInstruction(ByteCodeOp.GET_ITER); // Convert to iterator

            // 2. Create labels for loop control flow
            var startLabel = _instructionSequence.NewLabel();
            var endLabel = _instructionSequence.NewLabel();
            var breakLabel = _instructionSequence.NewLabel();
            var continueLabel = _instructionSequence.NewLabel();

            // 3. Mark loop start
            _instructionSequence.UseLabel(startLabel);

            // 4. FOR_ITER jumps to cleanup on StopIteration
            _instructionSequence.AddOpWithLabel(
                ByteCodeOp.FOR_ITER,
                endLabel,
                _currentLineNumber,
                _currentColumnOffset,
                _currentFileName
            );

            // 5. FOR_ITER pushes the next value on stack
            // Compile complex target assignment
            CompileComplexAssignTarget(forComplexStmt.Target);

            // 6. Push loop fblock for break/continue (CPython 3.12: PushFBlock)
            var loc = new SourceLocation(_currentLineNumber, _currentColumnOffset);
            PushFBlock(loc, FBlockType.FOR_LOOP, startLabel, breakLabel, null);

            // 7. Execute loop body
            foreach (var stmt in forComplexStmt.Body)
            {
                CompileStatement(stmt);
            }

            // 8. Mark continue label
            _instructionSequence.UseLabel(continueLabel);

            // 9. Jump back to loop start (CPython 3.12: uses JUMP, assembler determines direction)
            _instructionSequence.AddOpWithLabel(
                ByteCodeOp.JUMP,
                startLabel,
                -1,  // NO_LOCATION
                -1,
                _currentFileName
            );

            // 10. Cleanup label - loop exits here
            _instructionSequence.UseLabel(endLabel);
            EmitInstruction(ByteCodeOp.END_FOR, 0);

            // 11. Pop loop fblock (CPython 3.12: PopFBlock)
            PopFBlock(FBlockType.FOR_LOOP, startLabel);

            // 12. Execute else clause if present
            if (forComplexStmt.ElseClause != null && forComplexStmt.ElseClause.Count > 0)
            {
                foreach (var stmt in forComplexStmt.ElseClause)
                {
                    CompileStatement(stmt);
                }
            }

            // 13. Mark break label
            _instructionSequence.UseLabel(breakLabel);
        }
        
        /// <summary>
        /// Compile complex assignment target for for loop unpacking
        /// </summary>
        private void CompileComplexAssignTarget(Expression target)
        {
            if (target is NameExpression nameExpr)
            {
                // Simple assignment: x = stack_top
                EmitStoreName(nameExpr.Name);
            }
            else if (target is TupleExpression tupleExpr)
            {
                // Complex tuple unpacking: (x, y) = stack_top or (x, (y, z)) = stack_top
                EmitInstruction(ByteCodeOp.UNPACK_SEQUENCE, tupleExpr.Elements.Count);
                
                // Store each element (in forward order - UNPACK_SEQUENCE puts them in correct order)
                for (int i = 0; i < tupleExpr.Elements.Count; i++)
                {
                    CompileComplexAssignTarget(tupleExpr.Elements[i]);
                }
            }
            else
            {
                throw new Exception($"Cannot compile assignment target: {target.GetType().Name}");
            }
        }
        
        /// <summary>
        /// Compile a block statement (multiple statements grouped together)
        /// </summary>
        private void CompileBlockStatement(BlockStatement blockStmt)
        {
            foreach (var statement in blockStmt.Statements)
            {
                CompileStatement(statement);
            }
        }
        
        /// <summary>
        /// Compile type-specialized binary operation
        /// </summary>
        private void CompileTypeSpecializedBinaryOp(TypeSpecializedBinaryOpExpression specializedBinOp)
        {
            CompileExpression(specializedBinOp.Left);
            CompileExpression(specializedBinOp.Right);
            
            // 타입에 따른 특수화된 바이트코드 생성
            var opCode = GetSpecializedBinaryOpCode(specializedBinOp.LeftType, specializedBinOp.RightType, specializedBinOp.Operator);
            
            if (opCode != ByteCodeOp.NOP)
            {
                // 특수화된 명령어 사용
                EmitInstruction(opCode);
                #if DEBUG_LOG
                Console.WriteLine($"🚀 Type-specialized: {specializedBinOp.LeftType} {specializedBinOp.Operator} {specializedBinOp.RightType} → {opCode}");
                #endif
            }
            else
            {
                // 일반 이진 연산 사용
                var binaryOp = GetSpecializedBinaryOpCode(specializedBinOp.LeftType, specializedBinOp.RightType, specializedBinOp.Operator);
                EmitInstruction(ByteCodeOp.BINARY_OP, (int)binaryOp);
            }
        }
        
        /// <summary>
        /// 타입 기반 특수화된 바이트코드 결정
        /// </summary>
        private ByteCodeOp GetSpecializedBinaryOpCode(PyTypeInfo leftType, PyTypeInfo rightType, string op)
        {
            // 정수 연산 특수화
            if (leftType.IsInt && rightType.IsInt)
            {
                return op switch
                {
                    "+" => ByteCodeOp.BINARY_OP, // 향후 BINARY_ADD_INT로 확장
                    "-" => ByteCodeOp.BINARY_OP, // 향후 BINARY_SUB_INT로 확장
                    "*" => ByteCodeOp.BINARY_OP, // 향후 BINARY_MUL_INT로 확장
                    _ => ByteCodeOp.NOP
                };
            }
            
            // 문자열 연산 특수화
            if (leftType.IsString && rightType.IsString && op == "+")
            {
                return ByteCodeOp.BINARY_OP; // 향후 BINARY_ADD_STR로 확장
            }
            
            return ByteCodeOp.NOP;
        }
        
        /// <summary>
        /// Compile inlined method call
        /// </summary>
        private void CompileInlinedMethodCall(InlinedMethodCallExpression inlinedCall)
        {
            CompileExpression(inlinedCall.Target);
            
            // 인수들 컴파일
            foreach (var arg in inlinedCall.Arguments)
            {
                CompileExpression(arg);
            }
            
            // 인라인된 메서드에 특화된 바이트코드 생성
            var opCode = GetInlinedMethodOpCode(inlinedCall.ObjectType, inlinedCall.MethodName);
            
            if (opCode != ByteCodeOp.NOP)
            {
                EmitInstruction(opCode, inlinedCall.Arguments.Count);
                #if DEBUG_LOG
                Console.WriteLine($"🚀 Method inlined: {inlinedCall.ObjectType}.{inlinedCall.MethodName}() → {opCode}");
                #endif
            }
            else
            {
                // 일반 메서드 호출 사용
                EmitLoadConst(new PyString(inlinedCall.MethodName));
                EmitInstruction(ByteCodeOp.CALL, inlinedCall.Arguments.Count);
            }
        }
        
        /// <summary>
        /// 인라인된 메서드에 대한 특수화된 바이트코드 결정
        /// </summary>
        private ByteCodeOp GetInlinedMethodOpCode(string objectType, string methodName)
        {
            return (objectType, methodName) switch
            {
                ("str", "upper") => ByteCodeOp.CALL, // 향후 STR_UPPER로 확장
                ("str", "lower") => ByteCodeOp.CALL, // 향후 STR_LOWER로 확장
                ("list", "append") => ByteCodeOp.CALL, // 향후 LIST_APPEND로 확장
                ("dict", "get") => ByteCodeOp.CALL, // 향후 DICT_GET로 확장
                _ => ByteCodeOp.NOP
            };
        }
        

        /// <summary>
        /// CPython 3.12 CFG: Compile try-except-else-finally using SETUP_FINALLY/CLEANUP pseudo-instructions
        /// Follows CPython compile.c:compiler_try_except() pattern
        /// </summary>
        private void CompileTryStatementCFG(TryStatement tryStmt)
        {
            // Check if any handler is an except* handler (IsStar = true)
            // Performance: Eliminated LINQ
            bool hasExceptStar = false;
            if (tryStmt.Handlers != null)
            {
                foreach (var h in tryStmt.Handlers)
                {
                    if (h.IsStar)
                    {
                        hasExceptStar = true;
                        break;
                    }
                }
            }
            if (hasExceptStar)
            {
                // Convert TryStatement to TryStarStatement and use except* compilation
                // Performance: Eliminated LINQ
                var exceptStarHandlers = new List<ExceptStarHandler>();
                foreach (var h in tryStmt.Handlers!)
                {
                    exceptStarHandlers.Add(new ExceptStarHandler(h.Type, h.Name, h.Body));
                }
                var tryStarStmt = new TryStarStatement(
                    tryStmt.Body,
                    exceptStarHandlers,
                    tryStmt.OrElse,
                    tryStmt.FinalBody
                );
                CompileTryStarStatementCFG(tryStarStmt);
                return;
            }

#if DEBUG_COMPILER_LOG
            Console.WriteLine($"🔷 [CFG] CompileTryStatementCFG: Using InstructionSequence with SETUP_FINALLY (CPython 3.12 CFG path)");
#endif
            _cfgPathCount++;

            var hasExceptHandlers = tryStmt.Handlers != null && tryStmt.Handlers.Count > 0;
            var hasElse = tryStmt.OrElse != null && tryStmt.OrElse.Count > 0;
            var hasFinally = tryStmt.FinalBody != null && tryStmt.FinalBody.Count > 0;

            // CPython pattern: Create labels
            var exceptLabel = _instructionSequence.NewLabel();
            var endLabel = _instructionSequence.NewLabel();
            var cleanupLabel = _instructionSequence.NewLabel();
            var finallyLabel = hasFinally ? _instructionSequence.NewLabel() : default(SharpPy.Label);
            var finallyExceptLabel = hasFinally ? _instructionSequence.NewLabel() : default(SharpPy.Label);
            var finallyExceptCleanupLabel = hasFinally ? _instructionSequence.NewLabel() : default(SharpPy.Label);

            // 1. SETUP_FINALLY - marks try block start, pushes exception handler to stack
            // CPython compile.c:3249 - If finally exists, exception jumps to finally path (end label)
            // CPython compile.c:3269 - USE_LABEL(end) is where finally exception handler starts
            var exceptionTarget = hasFinally ? finallyExceptLabel : exceptLabel;
            _instructionSequence.AddOpWithLabel(ByteCodeOp.SETUP_FINALLY, exceptionTarget, _currentLineNumber);
            // Push exception handler to compiler stack (CPython: compiler->u->u_except_stack)
            _exceptionHandlerStack.Push(exceptionTarget);
            #if DEBUG
            Console.WriteLine($"[TEMP] CompileTryStatementCFG: Pushed outer handler {exceptLabel} to stack. Stack count = {_exceptionHandlerStack.Count}");
            #endif

            // 1.5. Push FINALLY_TRY fblock if finally clause exists
            // CPython compile.c:3239-3330 compiler_try_finally() pattern
            // This enables break/continue/return to execute finally inline via UnwindFBlock
            if (hasFinally)
            {
                var loc = new SourceLocation(_currentLineNumber, _currentColumnOffset);
                // fb_block: continue target (not used for FINALLY_TRY)
                // fb_exit: break target (not used for FINALLY_TRY)
                // fb_datum: finalbody statements to execute inline during unwinding
                PushFBlock(loc, FBlockType.FINALLY_TRY, finallyLabel, endLabel, tryStmt.FinalBody);
            }

            // 1.6. If both except handlers AND finally exist, add inner SETUP_FINALLY for except handlers
            // CPython compile.c:3256-3257 pattern: compiler_try_except is called from within compiler_try_finally
            // This creates nested SETUP_FINALLY instructions:
            //   Outer: SETUP_FINALLY finallyExceptLabel (for try-finally)
            //   Inner: SETUP_FINALLY exceptLabel (for try-except)  ← THIS ONE!
            if (hasExceptHandlers && hasFinally)
            {
                _instructionSequence.AddOpWithLabel(ByteCodeOp.SETUP_FINALLY, exceptLabel, _currentLineNumber);
                _exceptionHandlerStack.Push(exceptLabel);
                #if DEBUG
                Console.WriteLine($"[TEMP] CompileTryStatementCFG: Pushed inner except handler {exceptLabel} to stack. Stack count = {_exceptionHandlerStack.Count}");
                #endif
            }

            // 2. Try body (immediately follows SETUP_FINALLY, no label needed)
            foreach (var stmt in tryStmt.Body)
            {
                CompileStatement(stmt);
            }

            // Check if try block ends with unconditional terminator (RETURN/RAISE)
            // CPython 3.12: Python/flowgraph.c - avoid emitting unreachable code
            bool tryEndsWithTerminator = _instructionSequence.EndsWithTerminator();

            // NOTE: Don't pop FINALLY_TRY yet! Except handlers need to see it too.
            // CPython: pop happens AFTER compiler_try_except returns (compile.c:3263)

            // 3. POP_BLOCK - pop exception handler from stack (normal completion)
            // CRITICAL: POP_BLOCK is a pseudo-instruction, removed by flowgraph
            // But we still need to track it for exception handler stack management
            _instructionSequence.AddOp(ByteCodeOp.POP_BLOCK, _currentLineNumber);
            // Pop exception handler from compiler stack
            _exceptionHandlerStack.Pop();
            #if DEBUG
            Console.WriteLine($"[TEMP] CompileTryStatementCFG: Popped handler from stack. Stack count = {_exceptionHandlerStack.Count}");
            #endif

            // 3.5. If inner except handler exists, pop it too
            if (hasExceptHandlers && hasFinally)
            {
                _exceptionHandlerStack.Pop();
                #if DEBUG
                Console.WriteLine($"[TEMP] CompileTryStatementCFG: Popped inner except handler from stack. Stack count = {_exceptionHandlerStack.Count}");
                #endif
            }

            // 4. Else clause (only runs if no exception)
            if (hasElse)
            {
                foreach (var stmt in tryStmt.OrElse)
                {
                    CompileStatement(stmt);
                }
            }

            // 5. Try block completed successfully - JUMP to skip handlers
            // CPython 3.12: Both module-level and function-level use JUMP to skip handlers
            //               RETURN_CONST is only added at the END of the module
            // NOTE: This is crucial for nested try-except blocks!
            // CRITICAL: Don't add JUMP if try block ends with unconditional terminator (RETURN/RAISE/etc)
            // CPython 3.12: Python/flowgraph.c - unreachable code elimination
            if (!tryEndsWithTerminator)
            {
                if (hasFinally)
                {
                    _instructionSequence.AddOpWithLabel(ByteCodeOp.JUMP, finallyLabel, _currentLineNumber);
                }
                else
                {
                    _instructionSequence.AddOpWithLabel(ByteCodeOp.JUMP, endLabel, _currentLineNumber);
                }
            }

            // 6. Exception handler entry
            // CRITICAL: exceptLabel must point to the FIRST REAL INSTRUCTION after pseudo-instructions are removed
            // In CPython 3.12, the exception handler always starts with PUSH_EXC_INFO
            // SETUP_CLEANUP is a pseudo-instruction that gets removed by flowgraph, so we need to
            // place exceptLabel AFTER it, directly at PUSH_EXC_INFO
            // CPython: Python/compile.c:3357-3360

            if (hasExceptHandlers)
            {
                // CPython pattern (Python/compile.c:3394-3397):
                //   USE_LABEL(c, except);
                //   ADDOP_JUMP(c, NO_LOCATION, SETUP_CLEANUP, cleanup);
                //   ADDOP(c, NO_LOCATION, PUSH_EXC_INFO);
                // The except label is placed BEFORE SETUP_CLEANUP pseudo-instruction.
                // When flowgraph removes SETUP_CLEANUP, the label automatically points to PUSH_EXC_INFO.
                _instructionSequence.UseLabel(exceptLabel);

                // SETUP_CLEANUP protects the exception handlers themselves
                // (if an exception occurs in a handler, jump to cleanup or finally-except)
                var handlerCleanupTarget = hasFinally ? finallyExceptLabel : cleanupLabel;
                _instructionSequence.AddOpWithLabel(ByteCodeOp.SETUP_CLEANUP, handlerCleanupTarget, _currentLineNumber);

                // Push exception info to start exception handling
                _instructionSequence.AddOp(ByteCodeOp.PUSH_EXC_INFO, _currentLineNumber);

                // 7. Compile each except handler
                for (int i = 0; i < tryStmt.Handlers.Count; i++)
                {
                    var handler = tryStmt.Handlers[i];
                    var nextExceptLabel = _instructionSequence.NewLabel();

                    if (handler.Type != null)
                    {
                        // Load exception type and check match
                        CompileExpression(handler.Type);
                        _instructionSequence.AddOp(ByteCodeOp.CHECK_EXC_MATCH, _currentLineNumber);
                        _instructionSequence.AddOpWithLabel(ByteCodeOp.POP_JUMP_IF_FALSE, nextExceptLabel, _currentLineNumber);
                    }

                    // CPython pattern: Handle exception variable binding
                    // CPython compile.c:3417-3468
                    if (string.IsNullOrEmpty(handler.Name))
                    {
                        // No variable binding: just POP_TOP
                        _instructionSequence.AddOp(ByteCodeOp.POP_TOP, _currentLineNumber);

                        // CPython 3.12: Push HANDLER_CLEANUP fblock so break/continue/return emit POP_EXCEPT
                        // CPython compile.c:3475-3480
                        var handlerBodyLabel = _instructionSequence.NewLabel();
                        PushFBlock(
                            new SourceLocation(_currentLineNumber, _currentColumnOffset),
                            FBlockType.HANDLER_CLEANUP,
                            handlerBodyLabel,
                            Label.NoLabel,
                            null  // No exception variable
                        );

                        // Handler body
                        foreach (var stmt in handler.Body)
                        {
                            CompileStatement(stmt);
                        }

                        // CPython 3.12: Pop HANDLER_CLEANUP fblock
                        PopFBlock(FBlockType.HANDLER_CLEANUP, handlerBodyLabel);

                        // Clean exit: POP_BLOCK + POP_EXCEPT + JUMP
                        _instructionSequence.AddOp(ByteCodeOp.POP_BLOCK, _currentLineNumber);
                        _instructionSequence.AddOp(ByteCodeOp.POP_EXCEPT, _currentLineNumber);

                        if (hasFinally)
                        {
                            _instructionSequence.AddOpWithLabel(ByteCodeOp.JUMP, finallyLabel, _currentLineNumber);
                        }
                        else
                        {
                            _instructionSequence.AddOpWithLabel(ByteCodeOp.JUMP, endLabel, _currentLineNumber);
                        }
                    }
                    else
                    {
                        // Variable binding: CPython wraps handler body in nested try-finally
                        // CPython compile.c:3424-3433 pattern:
                        //   except type as name:
                        //       try:
                        //           # body
                        //       finally:
                        //           name = None
                        //           del name

                        var cleanupEndLabel = _instructionSequence.NewLabel();
                        var cleanupBodyLabel = _instructionSequence.NewLabel();

                        // Store exception to variable
                        EmitStoreName(handler.Name);

                        // Inner SETUP_CLEANUP for exception variable cleanup
                        _instructionSequence.AddOpWithLabel(ByteCodeOp.SETUP_CLEANUP, cleanupEndLabel, _currentLineNumber);

                        _instructionSequence.UseLabel(cleanupBodyLabel);

                        // CPython 3.12: Push HANDLER_CLEANUP fblock so break/continue/return emit POP_EXCEPT
                        // CPython compile.c:3439-3445
                        PushFBlock(
                            new SourceLocation(_currentLineNumber, _currentColumnOffset),
                            FBlockType.HANDLER_CLEANUP,
                            cleanupBodyLabel,
                            Label.NoLabel,
                            handler.Name  // Exception variable name for cleanup
                        );

                        // Handler body
                        foreach (var stmt in handler.Body)
                        {
                            CompileStatement(stmt);
                        }

                        // CPython 3.12: Pop HANDLER_CLEANUP fblock
                        PopFBlock(FBlockType.HANDLER_CLEANUP, cleanupBodyLabel);

                        // Normal path cleanup (CPython compile.c:3447-3455)
                        _instructionSequence.AddOp(ByteCodeOp.POP_BLOCK, _currentLineNumber);  // Inner SETUP_CLEANUP
                        _instructionSequence.AddOp(ByteCodeOp.POP_BLOCK, _currentLineNumber);  // Outer SETUP_CLEANUP
                        _instructionSequence.AddOp(ByteCodeOp.POP_EXCEPT, _currentLineNumber);

                        // Exception variable cleanup: name = None; del name
                        EmitLoadConst(PyNone.Instance);
                        EmitStoreName(handler.Name);
                        EmitDeleteName(handler.Name);

                        if (hasFinally)
                        {
                            _instructionSequence.AddOpWithLabel(ByteCodeOp.JUMP, finallyLabel, _currentLineNumber);
                        }
                        else
                        {
                            _instructionSequence.AddOpWithLabel(ByteCodeOp.JUMP, endLabel, _currentLineNumber);
                        }

                        // Exception path cleanup (CPython compile.c:3458-3467)
                        _instructionSequence.UseLabel(cleanupEndLabel);

                        // Exception variable cleanup: name = None; del name
                        EmitLoadConst(PyNone.Instance);
                        EmitStoreName(handler.Name);
                        EmitDeleteName(handler.Name);

                        _instructionSequence.AddOpWithArg(ByteCodeOp.RERAISE, 1, _currentLineNumber);
                    }

                    // Next exception handler label
                    _instructionSequence.UseLabel(nextExceptLabel);
                }

                // Reraise if no handler matched
                _instructionSequence.AddOpWithArg(ByteCodeOp.RERAISE, 0, _currentLineNumber);

                // CPython pattern: POP_BLOCK MUST come before cleanup label
                // This ends the SETUP_CLEANUP scope so cleanup code is NOT protected
                _instructionSequence.AddOp(ByteCodeOp.POP_BLOCK, _currentLineNumber);

                // 8. Cleanup handler (CPython pattern for exception propagation)
                // This block is reached when an exception occurs in the except handlers
                // IMPORTANT: This code is NOT protected by any exception handler (POP_BLOCK above)
                _instructionSequence.UseLabel(cleanupLabel);
                _instructionSequence.AddOpWithArg(ByteCodeOp.COPY, 3, _currentLineNumber);
                _instructionSequence.AddOp(ByteCodeOp.POP_EXCEPT, _currentLineNumber);
                _instructionSequence.AddOpWithArg(ByteCodeOp.RERAISE, 1, _currentLineNumber);

                // 8.5. Pop FINALLY_TRY fblock after all except handlers complete
                // CPython: pop happens after compiler_try_except returns (compile.c:3263)
                // This is where the try/except/finally structure ends and normal finally begins
                if (hasFinally)
                {
                    PopFBlock(FBlockType.FINALLY_TRY, finallyLabel);
                }
            }
            else
            {
                // No except handlers, but exceptLabel was still created
                // We need to place it here for the case where there's only finally (or cleanup)
                // CPython: In this case, exceptLabel is not used at all
                // SharpPy: We still need to UseLabel to avoid label reference errors
                _instructionSequence.UseLabel(exceptLabel);
            }

            // 9. Finally block (normal path) - offset 18 in CPython disassembly
            if (hasFinally)
            {
                _instructionSequence.UseLabel(finallyLabel);

                // Compile finally block (normal execution path)
                foreach (var stmt in tryStmt.FinalBody)
                {
                    CompileStatement(stmt);
                }

                // Jump to actual end
                _instructionSequence.AddOpWithLabel(ByteCodeOp.JUMP, endLabel, _currentLineNumber);

                // 10. Finally block (exception path) - offset 120 in CPython disassembly
                // This executes when an exception occurs in except handler
                // CRITICAL: Same as except handlers, place label AFTER SETUP_CLEANUP
                // SETUP_CLEANUP to protect finally block itself
                _instructionSequence.AddOpWithLabel(ByteCodeOp.SETUP_CLEANUP, finallyExceptCleanupLabel, _currentLineNumber);

                // NOW place the finallyExceptLabel at PUSH_EXC_INFO
                _instructionSequence.UseLabel(finallyExceptLabel);

                // PUSH_EXC_INFO to save exception state
                _instructionSequence.AddOp(ByteCodeOp.PUSH_EXC_INFO, _currentLineNumber);

                // Compile finally block again (exception execution path)
                foreach (var stmt in tryStmt.FinalBody)
                {
                    CompileStatement(stmt);
                }

                // RERAISE the exception after finally completes
                _instructionSequence.AddOpWithArg(ByteCodeOp.RERAISE, 0, _currentLineNumber);

                // Finally exception cleanup handler
                _instructionSequence.UseLabel(finallyExceptCleanupLabel);
                _instructionSequence.AddOpWithArg(ByteCodeOp.COPY, 3, _currentLineNumber);
                _instructionSequence.AddOp(ByteCodeOp.POP_EXCEPT, _currentLineNumber);
                _instructionSequence.AddOpWithArg(ByteCodeOp.RERAISE, 1, _currentLineNumber);
            }

            // 11. End label
            // CRITICAL: Add NOP after endLabel to prevent EndsWithTerminator() from considering
            // the try-except cleanup handler (ending with RERAISE) as the end of enclosing try body.
            // This ensures that if try-except is the last statement in a parent try block,
            // the parent try compiler will emit JUMP to skip its except handlers.
            // Same pattern as CompileWithStatement (Python/compile.c:6063)
            _instructionSequence.UseLabel(endLabel);
            _instructionSequence.AddOp(ByteCodeOp.NOP, _currentLineNumber);

#if DEBUG_COMPILER_LOG
            Console.WriteLine($"✅ [CFG] CompileTryStatementCFG: Complete (SETUP_FINALLY pattern)");
#endif
        }

        /// <summary>
        /// CPython 3.12 CFG: Compile try-except* (exception groups) using SETUP_FINALLY
        /// Follows CPython compile.c:compiler_try_star_except() pattern
        /// </summary>
        private void CompileTryStarStatementCFG(TryStarStatement tryStarStmt)
        {
#if DEBUG_COMPILER_LOG
            Console.WriteLine($"🔷 [CFG] CompileTryStarStatementCFG: Using InstructionSequence with SETUP_FINALLY (CPython 3.12 except* pattern)");
#endif
            _cfgPathCount++;

            var hasHandlers = tryStarStmt.Handlers != null && tryStarStmt.Handlers.Count > 0;
            var hasElse = tryStarStmt.OrElse != null && tryStarStmt.OrElse.Count > 0;
            var hasFinally = tryStarStmt.FinalBody != null && tryStarStmt.FinalBody.Count > 0;

            // CPython: If we have finally, use try_star_finally pattern
            if (hasFinally)
            {
                CompileTryStarFinallyCFG(tryStarStmt);
                return;
            }

            // Otherwise, use try_star_except pattern (lines 3563-3716 in CPython compile.c)
            CompileTryStarExceptCFG(tryStarStmt);
        }

        /// <summary>
        /// CPython 3.12: compiler_try_star_except() - Handle except* blocks for exception groups
        /// Reference: Python/compile.c lines 3563-3716
        /// </summary>
        private void CompileTryStarExceptCFG(TryStarStatement tryStarStmt)
        {
            // Create labels (CPython pattern)
            var bodyLabel = _instructionSequence.NewLabel();
            var exceptLabel = _instructionSequence.NewLabel();
            var orelseLabel = _instructionSequence.NewLabel();
            var cleanupLabel = _instructionSequence.NewLabel();
            var endLabel = _instructionSequence.NewLabel();
            var reraiseStarLabel = _instructionSequence.NewLabel();
            var reraiseLabel = _instructionSequence.NewLabel();

            // SETUP_FINALLY except (line 3563)
            _instructionSequence.AddOpWithLabel(ByteCodeOp.SETUP_FINALLY, exceptLabel, _currentLineNumber);
            // Push exception handler to stack (CPython: compiler->u->u_except_stack)
            _exceptionHandlerStack.Push(exceptLabel);
            #if DEBUG
            Console.WriteLine($"[TEMP] CompileTryStarExceptCFG: Pushed inner except* handler {exceptLabel} to stack. Stack count = {_exceptionHandlerStack.Count}");
            #endif

            // USE_LABEL body (line 3565)
            _instructionSequence.UseLabel(bodyLabel);

            // Try body (line 3568)
            foreach (var stmt in tryStarStmt.Body)
            {
                CompileStatement(stmt);
            }

            // CRITICAL: Capture outer handler BEFORE POP_BLOCK removes it from stack
            // The cleanup handler needs to know the OUTER try block's handler (not the inner except* handler)

            // POP_BLOCK and JUMP orelse (lines 3570-3571)
            _instructionSequence.AddOp(ByteCodeOp.POP_BLOCK, _currentLineNumber);
            // Pop exception handler from stack (normal completion path)
            if (_exceptionHandlerStack.Count > 0)
            {
                _exceptionHandlerStack.Pop();
                #if DEBUG
                Console.WriteLine($"[TEMP] CompileTryStarExceptCFG: Popped inner except* handler from stack. Stack count = {_exceptionHandlerStack.Count}");
                #endif
            }
            _instructionSequence.AddOpWithLabel(ByteCodeOp.JUMP, orelseLabel, _currentLineNumber);

            int n = tryStarStmt.Handlers.Count;

            // USE_LABEL except (line 3574)
            _instructionSequence.UseLabel(exceptLabel);

            // SETUP_CLEANUP cleanup and PUSH_EXC_INFO (lines 3576-3577)
            _instructionSequence.AddOpWithLabel(ByteCodeOp.SETUP_CLEANUP, cleanupLabel, _currentLineNumber);
            _instructionSequence.AddOp(ByteCodeOp.PUSH_EXC_INFO, _currentLineNumber);

            // Process each except* handler (lines 3584-3686)
            for (int i = 0; i < n; i++)
            {
                var handler = tryStarStmt.Handlers[i];
                var nextExceptLabel = _instructionSequence.NewLabel();
                var exceptWithErrorLabel = _instructionSequence.NewLabel();
                var noMatchLabel = _instructionSequence.NewLabel();

                // First handler: BUILD_LIST 0 and COPY 2 (lines 3592-3604)
                if (i == 0)
                {
                    // Build empty list for collected exceptions
                    _instructionSequence.AddOpWithArg(ByteCodeOp.BUILD_LIST, 0, _currentLineNumber);
                    // Copy the original exception group
                    _instructionSequence.AddOpWithArg(ByteCodeOp.COPY, 2, _currentLineNumber);
                }

                // If handler has type, check for match (lines 3605-3610)
                if (handler.Type != null)
                {
                    CompileExpression(handler.Type);
                    // CHECK_EG_MATCH: Check if exception group matches type
                    _instructionSequence.AddOp(ByteCodeOp.CHECK_EG_MATCH, _currentLineNumber);
                    _instructionSequence.AddOpWithArg(ByteCodeOp.COPY, 1, _currentLineNumber);
                    _instructionSequence.AddOpWithLabel(ByteCodeOp.POP_JUMP_IF_NONE, noMatchLabel, _currentLineNumber);
                }

                var cleanupEndLabel = _instructionSequence.NewLabel();
                var cleanupBodyLabel = _instructionSequence.NewLabel();

                // Bind exception name or POP_TOP (lines 3615-3621)
                if (!string.IsNullOrEmpty(handler.Name))
                {
                    EmitStoreName(handler.Name);
                }
                else
                {
                    _instructionSequence.AddOp(ByteCodeOp.POP_TOP, _currentLineNumber);
                }

                // SETUP_CLEANUP for handler body (line 3634)
                _instructionSequence.AddOpWithLabel(ByteCodeOp.SETUP_CLEANUP, cleanupEndLabel, _currentLineNumber);

                _instructionSequence.UseLabel(cleanupBodyLabel);

                // Handler body (line 3642)
                foreach (var stmt in handler.Body)
                {
                    CompileStatement(stmt);
                }

                // POP_BLOCK after handler body (line 3645)
                _instructionSequence.AddOp(ByteCodeOp.POP_BLOCK, _currentLineNumber);

                // Clean up exception name (lines 3646-3652)
                if (!string.IsNullOrEmpty(handler.Name))
                {
                    EmitLoadConst(PyNone.Instance);
                    EmitStoreName(handler.Name);
                    EmitDeleteName(handler.Name);
                }

                // JUMP to next except label (line 3653)
                _instructionSequence.AddOpWithLabel(ByteCodeOp.JUMP, nextExceptLabel, _currentLineNumber);

                // Cleanup handler (lines 3656-3670)
                _instructionSequence.UseLabel(cleanupEndLabel);

                if (!string.IsNullOrEmpty(handler.Name))
                {
                    EmitLoadConst(PyNone.Instance);
                    EmitStoreName(handler.Name);
                    EmitDeleteName(handler.Name);
                }

                // LIST_APPEND to collected exceptions list (line 3668)
                _instructionSequence.AddOpWithArg(ByteCodeOp.LIST_APPEND, 3, _currentLineNumber);
                _instructionSequence.AddOp(ByteCodeOp.POP_TOP, _currentLineNumber); // lasti
                _instructionSequence.AddOpWithLabel(ByteCodeOp.JUMP, exceptWithErrorLabel, _currentLineNumber);

                // Next except handler (lines 3672-3674)
                _instructionSequence.UseLabel(nextExceptLabel);
                _instructionSequence.AddOp(ByteCodeOp.NOP, _currentLineNumber);
                _instructionSequence.AddOpWithLabel(ByteCodeOp.JUMP, exceptWithErrorLabel, _currentLineNumber);

                // No match handler (lines 3676-3677)
                _instructionSequence.UseLabel(noMatchLabel);
                _instructionSequence.AddOp(ByteCodeOp.POP_TOP, _currentLineNumber); // match (None)

                _instructionSequence.UseLabel(exceptWithErrorLabel);

                // Last handler: LIST_APPEND and jump to reraise_star (lines 3681-3685)
                if (i == n - 1)
                {
                    _instructionSequence.AddOpWithArg(ByteCodeOp.LIST_APPEND, 1, _currentLineNumber);
                    _instructionSequence.AddOpWithLabel(ByteCodeOp.JUMP, reraiseStarLabel, _currentLineNumber);
                }

                exceptLabel = nextExceptLabel;
            }

            // Reraise star logic (lines 3691-3706)
            _instructionSequence.UseLabel(reraiseStarLabel);
            // PREP_RERAISE_STAR intrinsic: Prepares exception group for re-raising
            _instructionSequence.AddOpWithArg(ByteCodeOp.CALL_INTRINSIC_2, (int)IntrinsicFunction.INTRINSIC_PREP_RERAISE_STAR, _currentLineNumber);
            _instructionSequence.AddOpWithArg(ByteCodeOp.COPY, 1, _currentLineNumber);
            _instructionSequence.AddOpWithLabel(ByteCodeOp.POP_JUMP_IF_NOT_NONE, reraiseLabel, _currentLineNumber);

            // Nothing to reraise (lines 3696-3700)
            // CRITICAL: Use outerHandlerForCleanup so POP_EXCEPT is protected by outer try block
            _instructionSequence.AddOp(ByteCodeOp.POP_TOP, _currentLineNumber);
            _instructionSequence.AddOp(ByteCodeOp.POP_EXCEPT, _currentLineNumber);
            _instructionSequence.AddOpWithLabel(ByteCodeOp.JUMP, endLabel, _currentLineNumber);

            // Reraise exception group (lines 3702-3706)
            // CRITICAL: Use outerHandlerForCleanup so RERAISE 0 is protected by outer try block
            _instructionSequence.UseLabel(reraiseLabel);
            _instructionSequence.AddOpWithArg(ByteCodeOp.SWAP, 2, _currentLineNumber);
            _instructionSequence.AddOp(ByteCodeOp.POP_EXCEPT, _currentLineNumber);
            _instructionSequence.AddOpWithArg(ByteCodeOp.RERAISE, 0, _currentLineNumber);

            // Cleanup handler (lines 3708-3709)
            // CRITICAL: Pass outerHandlerForCleanup to all instructions
            // This ensures RERAISE is protected by outer try block's exception handler
            _instructionSequence.UseLabel(cleanupLabel);
            _instructionSequence.AddOpWithArg(ByteCodeOp.COPY, 3, _currentLineNumber);
            _instructionSequence.AddOp(ByteCodeOp.POP_EXCEPT, _currentLineNumber);
            _instructionSequence.AddOpWithArg(ByteCodeOp.RERAISE, 1, _currentLineNumber);

            // Else block (lines 3711-3712)
            _instructionSequence.UseLabel(orelseLabel);
            if (tryStarStmt.OrElse != null)
            {
                foreach (var stmt in tryStarStmt.OrElse)
                {
                    CompileStatement(stmt);
                }
            }

            // End label (line 3714)
            _instructionSequence.UseLabel(endLabel);

#if DEBUG_COMPILER_LOG
            Console.WriteLine($"✅ [CFG] CompileTryStarExceptCFG: Complete (except* pattern)");
#endif
        }

        /// <summary>
        /// CPython 3.12: compiler_try_star_finally() - Handle try-except*-finally
        /// Reference: Python/compile.c lines 3290-3338
        /// </summary>
        private void CompileTryStarFinallyCFG(TryStarStatement tryStarStmt)
        {
            // CPython pattern from compile.c:3290-3338
            var bodyLabel = _instructionSequence.NewLabel();
            var endLabel = _instructionSequence.NewLabel();
            var exitLabel = _instructionSequence.NewLabel();
            var cleanupLabel = _instructionSequence.NewLabel();

            // SETUP_FINALLY end (line 3299)
            _instructionSequence.AddOpWithLabel(ByteCodeOp.SETUP_FINALLY, endLabel, _currentLineNumber);

            // USE_LABEL body (line 3301)
            _instructionSequence.UseLabel(bodyLabel);

            // Try body: if has handlers, call compiler_try_star_except, else compile body (lines 3306-3311)
            var hasHandlers = tryStarStmt.Handlers != null && tryStarStmt.Handlers.Count > 0;
            if (hasHandlers)
            {
                // Call compiler_try_star_except for the except* handlers
                CompileTryStarExceptCFG(tryStarStmt);
            }
            else
            {
                // No handlers, just compile the body
                foreach (var stmt in tryStarStmt.Body)
                {
                    CompileStatement(stmt);
                }
            }

            // POP_BLOCK (line 3312)
            _instructionSequence.AddOp(ByteCodeOp.POP_BLOCK, _currentLineNumber);

            // Visit finalbody (line 3314)
            foreach (var stmt in tryStarStmt.FinalBody)
            {
                CompileStatement(stmt);
            }

            // JUMP exit (line 3316)
            _instructionSequence.AddOpWithLabel(ByteCodeOp.JUMP, exitLabel, _currentLineNumber);

            // Finally block: USE_LABEL end (line 3319)
            _instructionSequence.UseLabel(endLabel);

            // SETUP_CLEANUP cleanup, PUSH_EXC_INFO (lines 3322-3323)
            _instructionSequence.AddOpWithLabel(ByteCodeOp.SETUP_CLEANUP, cleanupLabel, _currentLineNumber);
            _instructionSequence.AddOp(ByteCodeOp.PUSH_EXC_INFO, _currentLineNumber);

            // Visit finalbody again (line 3327)
            foreach (var stmt in tryStarStmt.FinalBody)
            {
                CompileStatement(stmt);
            }

            // RERAISE 0 (line 3331)
            _instructionSequence.AddOpWithArg(ByteCodeOp.RERAISE, 0, _currentLineNumber);

            // USE_LABEL cleanup: POP_EXCEPT_AND_RERAISE (lines 3333-3334)
            _instructionSequence.UseLabel(cleanupLabel);
            // POP_EXCEPT_AND_RERAISE pattern
            _instructionSequence.AddOp(ByteCodeOp.POP_EXCEPT, _currentLineNumber);
            _instructionSequence.AddOpWithArg(ByteCodeOp.RERAISE, 1, _currentLineNumber);

            // USE_LABEL exit (line 3336)
            _instructionSequence.UseLabel(exitLabel);

#if DEBUG_COMPILER_LOG
            Console.WriteLine($"✅ [CFG] CompileTryStarFinallyCFG: Complete (try-except*-finally pattern)");
#endif
        }

        private void CompileWith(WithStatement withStmt)
        {
            // CPython 3.12: Always use InstructionSequence/CFG path
#if DEBUG_COMPILER_LOG
            Console.WriteLine($"🔷 [CFG] CompileWith: Using InstructionSequence (CPython 3.12 CFG path)");
#endif
            _cfgPathCount++;
            CompileWithCFG(withStmt);
        }

        /// <summary>
        /// CPython 3.12 CFG: Compile with statement using InstructionSequence
        /// Supports both single and multiple context managers
        /// </summary>
        private void CompileWithCFG(WithStatement withStmt)
        {
            if (withStmt.Items.Count == 0)
            {
                throw PySyntaxError.Create("with statement requires at least one context manager");
            }

            if (withStmt.Items.Count == 1)
            {
                // Single context manager
                CompileSingleWithCFG(withStmt);
            }
            else
            {
                // Multiple context managers - transform to nested with statements
                CompileMultipleWithCFG(withStmt);
            }
        }

        /// <summary>
        /// CPython 3.12 CFG: Compile single context manager
        /// Uses InstructionSequence labels and exception handler fblocks
        /// </summary>
        private void CompileSingleWithCFG(WithStatement withStmt)
        {
            var item = withStmt.Items[0];

            // CPython 3.12 pattern with proper exception handling (compile.c:6004-6063):
            // 1. Load context manager
            CompileExpression(item.ContextExpr);

            // 2. BEFORE_WITH: Load __exit__ to stack, call __enter__(), push result
            var loc = new SourceLocation(_currentLineNumber, _currentColumnOffset);
            EmitInstruction(ByteCodeOp.BEFORE_WITH);

            // 3. SETUP_WITH: Setup exception handler (CPython compile.c:6020)
            // CRITICAL: This is what enables exception handling in with statement!
            var withCleanupLabel = _instructionSequence.NewLabel();
            var endLabel = _instructionSequence.NewLabel();
            _instructionSequence.AddOpWithLabel(ByteCodeOp.SETUP_WITH, withCleanupLabel, _currentLineNumber);

            // 4. Push WITH fblock (CPython compile.c:6024)
            var blockLabel = _instructionSequence.NewLabel();
            _instructionSequence.UseLabel(blockLabel);
            PushFBlock(loc, FBlockType.WITH, blockLabel, withCleanupLabel, withStmt);

            // 5. CPython 3.12: Handle __enter__ result immediately after SETUP_WITH
            if (item.OptionalVars != null)
            {
                // Use proper assignment target compilation for correct scoping
                CompileAssignmentTarget(item.OptionalVars);
            }
            else
            {
                // Discard __enter__ result if no 'as' clause
                EmitInstruction(ByteCodeOp.POP_TOP);
            }

            // 6. Execute body (all instructions will be marked with exception handler)
            foreach (var stmt in withStmt.Body)
            {
                CompileStatement(stmt);
            }

            // 7. Pop exception handler (CPython compile.c:6043)
            EmitInstruction(ByteCodeOp.POP_BLOCK);

            // 8. Pop WITH fblock (body complete - CPython compile.c:6044)
            PopFBlock(FBlockType.WITH, blockLabel);

            // 9. Normal exit: call __exit__(None, None, None) (CPython compile.c:6051-6052)
            EmitLoadConst(PyNone.Instance);
            EmitLoadConst(PyNone.Instance);
            EmitLoadConst(PyNone.Instance);
            EmitInstruction(ByteCodeOp.CALL, 2);  // __exit__(exc_type, exc_val, exc_tb)
            EmitInstruction(ByteCodeOp.POP_TOP);  // discard __exit__ return value

            // Jump to end (CPython compile.c:6053)
            _instructionSequence.AddOpWithLabel(
                ByteCodeOp.JUMP,
                endLabel,
                _currentLineNumber,
                _currentColumnOffset,
                _currentFileName
            );

            // 10. Exception handler (CPython compile.c:6056-6061)
            _instructionSequence.UseLabel(withCleanupLabel);

            // Setup cleanup for nested exceptions (CPython compile.c:6058)
            var cleanupLabel = _instructionSequence.NewLabel();
            _instructionSequence.AddOpWithLabel(ByteCodeOp.SETUP_CLEANUP, cleanupLabel, _currentLineNumber);

            // Push exception info and call __exit__ with exception (CPython compile.c:6059-6060)
            EmitInstruction(ByteCodeOp.PUSH_EXC_INFO);
            EmitInstruction(ByteCodeOp.WITH_EXCEPT_START);

            // Check if exception was suppressed (CPython compile.c:6061 - compiler_with_except_finish)
            var suppressLabel = _instructionSequence.NewLabel();
            _instructionSequence.AddOpWithLabel(
                ByteCodeOp.POP_JUMP_IF_TRUE,
                suppressLabel,
                _currentLineNumber,
                _currentColumnOffset,
                _currentFileName
            );

            // Re-raise exception if not suppressed
            EmitInstruction(ByteCodeOp.RERAISE, 2);

            // Exception suppressed - continue normally (CPython compile.c:5860-5867)
            _instructionSequence.UseLabel(suppressLabel);
            EmitInstruction(ByteCodeOp.POP_TOP);     // Remove exc_value
            EmitInstruction(ByteCodeOp.POP_EXCEPT);  // Remove exception info
            EmitInstruction(ByteCodeOp.POP_TOP);     // Remove lasti
            EmitInstruction(ByteCodeOp.POP_TOP);     // Remove exit_func

            // Jump to end label to skip cleanup handler (CPython compile.c:5867)
            _instructionSequence.AddOpWithLabel(
                ByteCodeOp.JUMP,
                endLabel,
                _currentLineNumber,
                _currentColumnOffset,
                _currentFileName
            );

            // Cleanup handler for nested exceptions (CPython compile.c:5869-5870)
            _instructionSequence.UseLabel(cleanupLabel);
            EmitInstruction(ByteCodeOp.COPY, 3);
            EmitInstruction(ByteCodeOp.POP_EXCEPT);
            EmitInstruction(ByteCodeOp.RERAISE, 1);

            // Mark end label
            // CPython 3.12: Python/compile.c:6063 - USE_LABEL(c, exit)
            // CRITICAL: Add NOP after endLabel to prevent EndsWithTerminator() from considering
            // the with exception handler (ending with RERAISE) as the end of enclosing try body.
            // This ensures that if with is the last statement in a try block, the try compiler
            // will emit JUMP to skip except handlers (Python/compile.c:3249-3330).
            _instructionSequence.UseLabel(endLabel);
            EmitInstruction(ByteCodeOp.NOP);
        }

        /// <summary>
        /// CPython 3.12: async with statement compilation (Python/compile.c:5901-5979)
        /// async with expr as var:
        ///     body
        /// </summary>
        private void CompileAsyncWith(AsyncWithStatement asyncWithStmt)
        {
#if DEBUG_COMPILER_LOG
            Console.WriteLine($"🔷 [CFG] CompileAsyncWith: Using InstructionSequence (CPython 3.12)");
#endif

            // For now, implement single context manager (recursive for multiple later)
            if (asyncWithStmt.Items.Count == 0)
            {
                throw PySyntaxError.Create("async with statement requires at least one context manager");
            }

            if (asyncWithStmt.Items.Count == 1)
            {
                CompileSingleAsyncWith(asyncWithStmt);
            }
            else
            {
                // Multiple context managers - transform to nested async with
                CompileMultipleAsyncWith(asyncWithStmt);
            }
        }

        /// <summary>
        /// CPython 3.12: Compile single async context manager (Python/compile.c:5901-5979)
        /// </summary>
        private void CompileSingleAsyncWith(AsyncWithStatement asyncWithStmt)
        {
            var item = asyncWithStmt.Items[0];
            var loc = new SourceLocation(_currentLineNumber, _currentColumnOffset);

            // CPython: Create labels (compile.c:5913-5916)
            var blockLabel = _instructionSequence.NewLabel();
            var finalLabel = _instructionSequence.NewLabel();
            var exitLabel = _instructionSequence.NewLabel();
            var cleanupLabel = _instructionSequence.NewLabel();

            // 1. Evaluate context expression
            // CPython: VISIT(c, expr, item->context_expr);
            CompileExpression(item.ContextExpr);

            // 2. BEFORE_ASYNC_WITH: Call __aenter__()
            // CPython: ADDOP(c, loc, BEFORE_ASYNC_WITH);
            EmitInstruction(ByteCodeOp.BEFORE_ASYNC_WITH);

            // 3. Get awaitable from __aenter__() result
            // CPython: ADDOP_I(c, loc, GET_AWAITABLE, 1);
            EmitInstruction(ByteCodeOp.GET_AWAITABLE, 1);

            // 4. Await the __aenter__() result (yield from pattern)
            // CPython: ADDOP_LOAD_CONST(c, loc, Py_None);
            // CPython: ADD_YIELD_FROM(c, loc, 1);
            EmitLoadConst(PyNone.Instance);
            CompileYieldFrom(isAwait: true);

            // 5. Setup exception handler with SETUP_WITH
            // CPython: ADDOP_JUMP(c, loc, SETUP_WITH, final);
            _instructionSequence.AddOpWithLabel(ByteCodeOp.SETUP_WITH, finalLabel, _currentLineNumber);

            // 6. Mark block label and push ASYNC_WITH fblock
            // CPython: USE_LABEL(c, block);
            // CPython: compiler_push_fblock(c, loc, ASYNC_WITH, block, final, s);
            _instructionSequence.UseLabel(blockLabel);
            PushFBlock(loc, FBlockType.ASYNC_WITH, blockLabel, finalLabel, asyncWithStmt);

            // 7. Handle __aenter__() result
            if (item.OptionalVars != null)
            {
                // CPython: VISIT(c, expr, item->optional_vars);
                CompileAssignmentTarget(item.OptionalVars);
            }
            else
            {
                // CPython: ADDOP(c, loc, POP_TOP);
                EmitInstruction(ByteCodeOp.POP_TOP);
            }

            // 8. Execute body
            // CPython: pos++; if (pos == len) { VISIT_SEQ(c, stmt, body) }
            foreach (var stmt in asyncWithStmt.Body)
            {
                CompileStatement(stmt);
            }

            // 9. Pop fblock and exception handler
            // CPython: compiler_pop_fblock(c, ASYNC_WITH, block);
            PopFBlock(FBlockType.ASYNC_WITH, blockLabel);

            // CPython: ADDOP(c, loc, POP_BLOCK);
            EmitInstruction(ByteCodeOp.POP_BLOCK);

            // 10. Normal exit: call __aexit__(None, None, None) and await it
            // CPython: compiler_call_exit_with_nones(c, loc);
            EmitLoadConst(PyNone.Instance);
            EmitLoadConst(PyNone.Instance);
            EmitLoadConst(PyNone.Instance);
            EmitInstruction(ByteCodeOp.CALL, 2);  // __aexit__(exc_type, exc_val, exc_tb)

            // CPython: ADDOP_I(c, loc, GET_AWAITABLE, 2);
            EmitInstruction(ByteCodeOp.GET_AWAITABLE, 2);

            // CPython: ADDOP_LOAD_CONST(c, loc, Py_None);
            // CPython: ADD_YIELD_FROM(c, loc, 1);
            EmitLoadConst(PyNone.Instance);
            CompileYieldFrom(isAwait: true);

            // CPython: ADDOP(c, loc, POP_TOP);
            EmitInstruction(ByteCodeOp.POP_TOP);

            // CPython: ADDOP_JUMP(c, loc, JUMP, exit);
            _instructionSequence.AddOpWithLabel(ByteCodeOp.JUMP, exitLabel, _currentLineNumber);

            // 11. Exception handler
            // CPython: USE_LABEL(c, final);
            _instructionSequence.UseLabel(finalLabel);

            // CPython: ADDOP_JUMP(c, loc, SETUP_CLEANUP, cleanup);
            _instructionSequence.AddOpWithLabel(ByteCodeOp.SETUP_CLEANUP, cleanupLabel, _currentLineNumber);

            // CPython: ADDOP(c, loc, PUSH_EXC_INFO);
            EmitInstruction(ByteCodeOp.PUSH_EXC_INFO);

            // CPython: ADDOP(c, loc, WITH_EXCEPT_START);
            EmitInstruction(ByteCodeOp.WITH_EXCEPT_START);

            // CPython: ADDOP_I(c, loc, GET_AWAITABLE, 2);
            EmitInstruction(ByteCodeOp.GET_AWAITABLE, 2);

            // CPython: ADDOP_LOAD_CONST(c, loc, Py_None);
            // CPython: ADD_YIELD_FROM(c, loc, 1);
            EmitLoadConst(PyNone.Instance);
            CompileYieldFrom(isAwait: true);

            // CPython: compiler_with_except_finish(c, cleanup);
            // Check if exception was suppressed
            var suppressLabel = _instructionSequence.NewLabel();
            _instructionSequence.AddOpWithLabel(ByteCodeOp.POP_JUMP_IF_TRUE, suppressLabel, _currentLineNumber);

            // Re-raise exception if not suppressed
            EmitInstruction(ByteCodeOp.RERAISE, 2);

            // Exception suppressed
            _instructionSequence.UseLabel(suppressLabel);
            EmitInstruction(ByteCodeOp.POP_TOP);
            EmitInstruction(ByteCodeOp.POP_EXCEPT);
            EmitInstruction(ByteCodeOp.POP_TOP);
            EmitInstruction(ByteCodeOp.POP_TOP);

            _instructionSequence.AddOpWithLabel(ByteCodeOp.JUMP, exitLabel, _currentLineNumber);

            // Cleanup handler
            _instructionSequence.UseLabel(cleanupLabel);
            EmitInstruction(ByteCodeOp.COPY, 3);
            EmitInstruction(ByteCodeOp.POP_EXCEPT);
            EmitInstruction(ByteCodeOp.RERAISE, 1);

            // CPython: USE_LABEL(c, exit);
            _instructionSequence.UseLabel(exitLabel);
        }

        /// <summary>
        /// CPython 3.12: Compile multiple async context managers (recursive)
        /// </summary>
        private void CompileMultipleAsyncWith(AsyncWithStatement asyncWithStmt)
        {
            if (asyncWithStmt.Items.Count < 2)
            {
                throw new InvalidOperationException("CompileMultipleAsyncWith requires at least 2 context managers");
            }

            // Take the first context manager
            var outerItem = asyncWithStmt.Items[0];

            // Create inner async with statement with remaining context managers
            var remainingItems = new List<WithItem>();
            for (int i = 1; i < asyncWithStmt.Items.Count; i++)
            {
                remainingItems.Add(asyncWithStmt.Items[i]);
            }

            var innerAsyncWith = new AsyncWithStatement(
                items: remainingItems,
                body: asyncWithStmt.Body
            );

            // Create outer async with statement with first context manager and nested inner as body
            var outerAsyncWith = new AsyncWithStatement(
                items: new List<WithItem> { outerItem },
                body: new List<Statement> { innerAsyncWith }
            );

            // Compile the transformed outer async with statement
            CompileSingleAsyncWith(outerAsyncWith);
        }

        /// <summary>
        /// CPython 3.12 CFG: Compile multiple context managers
        /// Transform to nested with statements recursively
        /// with a, b, c: body -> with a: (with b: (with c: body))
        /// </summary>
        private void CompileMultipleWithCFG(WithStatement withStmt)
        {
            if (withStmt.Items.Count < 2)
            {
                throw new InvalidOperationException("CompileMultipleWithCFG requires at least 2 context managers");
            }

            // Take the first context manager
            var outerItem = withStmt.Items[0];

            // Create inner with statement with remaining context managers
            // Performance: Eliminated LINQ
            var remainingItems = new List<WithItem>();
            for (int i = 1; i < withStmt.Items.Count; i++)
            {
                remainingItems.Add(withStmt.Items[i]);
            }
            WithStatement innerWith;

            if (remainingItems.Count == 1)
            {
                // Base case: create simple with statement for the last context manager
                innerWith = new WithStatement(
                    items: remainingItems,
                    body: withStmt.Body
                );
            }
            else
            {
                // Recursive case: create nested with statement
                innerWith = new WithStatement(
                    items: remainingItems,
                    body: withStmt.Body
                );
            }

            // Create outer with statement with first context manager and nested inner with as body
            var outerWith = new WithStatement(
                items: new List<WithItem> { outerItem },
                body: new List<Statement> { innerWith }
            );

            // Compile the transformed outer with statement
            CompileSingleWithCFG(outerWith);
        }

        // CPython 3.12: CompileSingleWith and CompileMultipleWith removed
        // With statements now use CompileWithCFG (InstructionSequence/CFG path) only

        /// <summary>
        /// CPython 3.12: compiler_match_inner (Python/compile.c line 7306)
        /// Match statement compilation using InstructionSequence/CFG
        /// </summary>
        private void CompileMatch(MatchStatement matchStmt)
        {
#if DEBUG_COMPILER_LOG
            Console.WriteLine($"🔷 [CFG] CompileMatch: Using InstructionSequence (CPython 3.12 CFG path)");
#endif
            _cfgPathCount++;

            // CPython 3.12: compiler_match_inner implementation (Python/compile.c line 7306-7382)
            // VISIT(c, expr, s->v.Match.subject);
            CompileExpression(matchStmt.Subject);

            // NEW_JUMP_TARGET_LABEL(c, end);
            SharpPy.Label endLabel = _instructionSequence.NewLabel();

            int caseCount = matchStmt.Cases.Count;
            if (caseCount == 0)
            {
                // No cases - just pop subject and done
                _instructionSequence.AddOp(ByteCodeOp.POP_TOP, 0);
                _instructionSequence.UseLabel(endLabel);
                return;
            }

            // Check if last case is wildcard (case _:)
            var lastCase = matchStmt.Cases[caseCount - 1];
            bool hasDefault = IsWildcardPattern(lastCase.Pattern) && lastCase.Guard == null && caseCount > 1;

            // Compile all cases except the default (if exists)
            int loopEnd = hasDefault ? caseCount - 1 : caseCount;

            for (int i = 0; i < loopEnd; i++)
            {
                var matchCase = matchStmt.Cases[i];

                // CPython: Only copy the subject if we're *not* on the last case
                if (i != loopEnd - 1)
                {
                    _instructionSequence.AddOpWithArg(ByteCodeOp.COPY, 1, _currentLineNumber);
                }

                // CPython 3.12: Create pattern context for this case
                var pc = new PatternContext();
                SharpPy.Label failLabel = _instructionSequence.NewLabel();

                // CPython: Initialize fail_pop[0] with the fail label for this case
                pc.FailPop[0] = failLabel;

                // Compile pattern matching
                if (!CompilePatternMatchCFG(matchCase.Pattern, pc))
                {
                    // Pattern compilation failed - skip this case
                    _instructionSequence.UseLabel(failLabel);
                    continue;
                }

                // CPython 3.12: It's a match! Store all captured names
                foreach (var varName in pc.Stores)
                {
                    EmitStoreName(varName);
                }

                // Check guard if present
                if (matchCase.Guard != null)
                {
                    CompileExpression(matchCase.Guard);
                    _instructionSequence.AddOpWithLabel(ByteCodeOp.POP_JUMP_IF_FALSE, failLabel, _currentLineNumber);
                }

                // Pattern matched - pop subject if not last case
                if (i != loopEnd - 1)
                {
                    _instructionSequence.AddOp(ByteCodeOp.POP_TOP, 0);
                }

                // Compile case body
                foreach (var stmt in matchCase.Body)
                {
                    CompileStatement(stmt);
                }

                // Jump to end
                _instructionSequence.AddOpWithLabel(ByteCodeOp.JUMP, endLabel, _currentLineNumber);

                // CPython 3.12: emit_and_reset_fail_pop - generate POP_TOP chain for cleanup
                EmitAndResetFailPop(pc, failLabel);

                // Place fail label for next case
                _instructionSequence.UseLabel(failLabel);
            }

            // Handle default case if exists
            if (hasDefault)
            {
                if (caseCount == 1)
                {
                    // Only default case - pop subject
                    _instructionSequence.AddOp(ByteCodeOp.POP_TOP, 0);
                }
                else
                {
                    // CPython: Show line coverage for default case (it doesn't create bytecode)
                    _instructionSequence.AddOp(ByteCodeOp.NOP, 0);
                }

                if (lastCase.Guard != null)
                {
                    CompileExpression(lastCase.Guard);
                    _instructionSequence.AddOpWithLabel(ByteCodeOp.POP_JUMP_IF_FALSE, endLabel, _currentLineNumber);
                }

                foreach (var stmt in lastCase.Body)
                {
                    CompileStatement(stmt);
                }
            }

            // USE_LABEL(c, end);
            _instructionSequence.UseLabel(endLabel);
        }

        /// <summary>
        /// Check if pattern is wildcard (_)
        /// </summary>
        private bool IsWildcardPattern(Expression pattern)
        {
            return pattern is NameExpression nameExpr && nameExpr.Name == "_";
        }

        /// <summary>
        /// CPython 3.12: compiler_pattern for CFG path
        /// Simplified version - supports constant patterns for test_match_simple.py
        /// </summary>
        private bool CompilePatternMatchCFG(Expression pattern, PatternContext pc)
        {
            switch (pattern)
            {
                case ConstantExpression constExpr:
                    // CPython: compiler_pattern_value - Direct constant comparison
                    // Stack: [subject] -> [subject, constant] -> [comparison_result]
                    CompileExpression(constExpr);
                    _instructionSequence.AddOpWithArg(ByteCodeOp.COMPARE_OP, (int)CompareOp.EQ, _currentLineNumber);
                    JumpToFailPop(pc, ByteCodeOp.POP_JUMP_IF_FALSE);
                    return true;

                case NameExpression nameExpr:
                    // CPython 3.12: NameExpression in match context is treated as MatchAs(null, name)
                    // This is a capture pattern (binds subject to variable) or wildcard (_)
                    // Python/compile.c line 6789-6799: An irrefutable match
                    return PatternHelperStoreName(nameExpr.Name, pc);

                case OrPattern orPat:
                    // CPython 3.12: compiler_pattern_or
                    return CompileOrPattern(orPat, pc);

                case StarExpression starExpr:
                    // CPython 3.12: compiler_pattern_star (MatchStar_kind)
                    // Star pattern *name - only store the name, don't emit STORE instruction yet
                    // Stack: [subject] -> [subject] (star pattern consumes nothing from stack)
                    if (starExpr.Value is NameExpression starName)
                    {
                        if (starName.Name != "_")
                        {
                            // CPython: pattern_helper_store_name - add to stores list
                            if (pc.Stores.Contains(starName.Name))
                            {
                                throw new InvalidOperationException($"multiple assignments to name {starName.Name} in pattern");
                            }
                            pc.Stores.Add(starName.Name);
                        }
                        // Wildcard star (*_) - do nothing
                    }
                    return true;

                case StarPattern starPat:
                    // CPython 3.12: compiler_pattern_star (MatchStar_kind)
                    // Star pattern *name inside sequence patterns
                    // Stack: [subject] -> [subject] (star pattern consumes nothing from stack)
                    if (starPat.Name != "_")
                    {
                        // CPython: pattern_helper_store_name - add to stores list
                        if (pc.Stores.Contains(starPat.Name))
                        {
                            throw new InvalidOperationException($"multiple assignments to name {starPat.Name} in pattern");
                        }
                        pc.Stores.Add(starPat.Name);
                    }
                    // Wildcard star (*_) - do nothing
                    return true;

                case MatchSequence matchSeq:
                    // CPython 3.12: compiler_pattern_sequence (MatchSequence_kind)
                    // Pattern like [first, *middle, last] or [a, b, c]
                    // This is the CORRECT AST node for sequence patterns (NOT ListExpression)
                    return CompileSequencePattern(matchSeq.Patterns, pc);

                case MatchMapping matchMap:
                    // CPython 3.12: compiler_pattern_mapping (MatchMapping_kind)
                    // Pattern like {} or {"key": value} or {"x": x, **rest}
                    // This is the CORRECT AST node for mapping patterns (NOT DictExpression)
                    return CompileMappingPattern(matchMap, pc);

                case MatchClass matchCls:
                    // CPython 3.12: compiler_pattern_class (MatchClass_kind)
                    // Pattern like Point(x=0, y=0) or Point(x, y)
                    // This is the CORRECT AST node for class patterns (NOT CallExpression)
                    return CompileClassPattern(matchCls, pc);

                case AsPattern asPattern:
                    // CPython 3.12: compiler_pattern_as (MatchAs_kind)
                    // Pattern like [x, y] as point or _ as value
                    return CompileAsPattern(asPattern, pc);

                default:
                    // Unsupported pattern for now
                    throw new NotImplementedException($"Pattern type {pattern?.GetType().Name} not yet supported in CFG path");
            }
        }
        

        /// <summary>
        /// Compile sequence pattern matching like [1, 2, *rest]
        /// </summary>
        private bool CompileSequencePattern(SequencePattern pattern, PatternContext pc)
        {
            // Extract fail label from pattern context
            var failLabel = pc.GetFailLabel();

            var patterns = pattern.Patterns;

            // Check if pattern has star expressions
            // Performance: Eliminated LINQ
            bool hasStarPattern = false;
            foreach (var p in patterns)
            {
                if (p is StarPattern)
                {
                    hasStarPattern = true;
                    break;
                }
            }
            int starIndex = -1;
            int countBefore = 0, countAfter = 0;

            if (hasStarPattern)
            {
                starIndex = patterns.FindIndex(p => p is StarPattern);
                countBefore = starIndex;
                countAfter = patterns.Count - starIndex - 1;
            }

            // Stack: [subject] (the list/sequence to match)

            // 1. Check if subject is a sequence (CPython MATCH_SEQUENCE)
            EmitInstruction(ByteCodeOp.MATCH_SEQUENCE);
            _instructionSequence.AddOpWithLabel(ByteCodeOp.POP_JUMP_IF_FALSE, failLabel, _currentLineNumber, _currentColumnOffset, _currentFileName);
            
            // 2. Check length constraints
            EmitInstruction(ByteCodeOp.GET_LEN);
            
            if (hasStarPattern)
            {
                // For star patterns: len >= (before + after)
                EmitLoadConst(new PyInt(countBefore + countAfter));
                EmitComparison(CompareOp.GE);
                _instructionSequence.AddOpWithLabel(ByteCodeOp.POP_JUMP_IF_FALSE, failLabel, _currentLineNumber, _currentColumnOffset, _currentFileName);
            }
            else
            {
                // For exact patterns: len == pattern_count
                EmitLoadConst(new PyInt(patterns.Count));
                EmitComparison(CompareOp.EQ);
                _instructionSequence.AddOpWithLabel(ByteCodeOp.POP_JUMP_IF_FALSE, failLabel, _currentLineNumber, _currentColumnOffset, _currentFileName);
            }
            
            // 3. Unpack and match elements
            if (hasStarPattern)
            {
                // Use UNPACK_EX for star patterns
                int arg = countBefore | (countAfter << 8);
                EmitInstruction(ByteCodeOp.UNPACK_EX, arg);
                
                // CPython UNPACK_EX puts elements on stack in pattern order
                // For [first, *rest, last] with UNPACK_EX 257, stack becomes: [first, rest, last]
                // Then CPython stores in pattern order: STORE_FAST(first), STORE_FAST(rest), STORE_FAST(last)

                // 1. Store before elements (first on stack)
                for (int i = 0; i < countBefore; i++)
                {
                    var p = patterns[i];

                    if (p is ConstantExpression constExpr)
                    {
                        CompileExpression(constExpr);
                        EmitComparison(CompareOp.EQ);
                        _instructionSequence.AddOpWithLabel(ByteCodeOp.POP_JUMP_IF_FALSE, failLabel, _currentLineNumber, _currentColumnOffset, _currentFileName);
                    }
                    else if (p is NameExpression nameExpr)
                    {
                        EmitStoreName(nameExpr.Name);
                    }
                    else
                    {
                        // Handle nested patterns (mapping, sequence, etc.) recursively
                        if (!CompilePatternMatchCFG(p, pc))
                        {
                            return false; // Nested pattern compilation failed
                        }
                    }
                }

                // 2. Store star pattern (next on stack)
                if (starIndex >= 0 && patterns[starIndex] is StarPattern starPat)
                {
                    EmitStoreName(starPat.Name);
                }

                // 3. Store after elements (remaining on stack)
                for (int i = 0; i < countAfter; i++)
                {
                    var patternIdx = starIndex + 1 + i;  // patterns after star
                    var p = patterns[patternIdx];

                    if (p is ConstantExpression constExpr)
                    {
                        CompileExpression(constExpr);
                        EmitComparison(CompareOp.EQ);
                        _instructionSequence.AddOpWithLabel(ByteCodeOp.POP_JUMP_IF_FALSE, failLabel, _currentLineNumber, _currentColumnOffset, _currentFileName);
                    }
                    else if (p is NameExpression nameExpr)
                    {
                        EmitStoreName(nameExpr.Name);
                    }
                    else
                    {
                        // Handle nested patterns (mapping, sequence, etc.) recursively
                        if (!CompilePatternMatchCFG(p, pc))
                        {
                            return false; // Nested pattern compilation failed
                        }
                    }
                }
            }
            else
            {
                // Use regular UNPACK_SEQUENCE for non-star patterns
                EmitInstruction(ByteCodeOp.UNPACK_SEQUENCE, patterns.Count);

                // Match each pattern (CPython-compatible order)
                // UNPACK_SEQUENCE pushes elements, then we match in forward order
                for (int i = 0; i < patterns.Count; i++)
                {
                    var p = patterns[i];
                    if (p is ConstantExpression constExpr)
                    {
                        // CPython directly compares without DUP_TOP
                        CompileExpression(constExpr);
                        EmitComparison(CompareOp.EQ);
                        _instructionSequence.AddOpWithLabel(ByteCodeOp.POP_JUMP_IF_FALSE, failLabel, _currentLineNumber, _currentColumnOffset, _currentFileName);
                    }
                    else if (p is NameExpression nameExpr)
                    {
                        EmitStoreName(nameExpr.Name);
                    }
                    else
                    {
                        // Handle nested patterns (mapping, sequence, etc.) recursively
                        if (!CompilePatternMatchCFG(p, pc))
                        {
                            return false; // Nested pattern compilation failed
                        }
                    }
                }
            }
            
            return true;
        }

        /// <summary>
        /// CPython 3.12: Compile dictionary pattern matching {"key": value}
        /// </summary>
        private bool CompileMappingPattern(MappingPattern pattern, PatternContext pc)
        {
            // Extract fail label from pattern context
            var failLabel = pc.GetFailLabel();

            // CPython 3.12: Dictionary pattern matching with MATCH_MAPPING, MATCH_KEYS
            // Example: case {"key": value}:
            // Generates:
            //   MATCH_MAPPING      - check if subject is mapping
            //   GET_LEN            - get mapping length
            //   LOAD_CONST >= 1    - check minimum key count
            //   COMPARE_OP >=      - compare lengths
            //   POP_JUMP_IF_FALSE fail
            //   LOAD_CONST ('key',) - tuple of required keys
            //   MATCH_KEYS         - check if keys exist, return values
            //   UNPACK_SEQUENCE    - unpack matched values
            //   STORE_NAME value   - bind to variables

            // Stack: [subject]

            // Step 1: Check if subject is a mapping (dict-like)
            EmitInstruction(ByteCodeOp.MATCH_MAPPING);
            _instructionSequence.AddOpWithLabel(ByteCodeOp.POP_JUMP_IF_FALSE, failLabel, _currentLineNumber, _currentColumnOffset, _currentFileName);
            // Stack: [subject] (MATCH_MAPPING leaves subject on stack)

            // Step 2: Check minimum length (number of required keys)
            EmitInstruction(ByteCodeOp.GET_LEN);
            CompileExpression(new ConstantExpression(new PyInt(pattern.Patterns.Count)));
            EmitComparison(CompareOp.GE); // >= required count
            _instructionSequence.AddOpWithLabel(ByteCodeOp.POP_JUMP_IF_FALSE, failLabel, _currentLineNumber, _currentColumnOffset, _currentFileName);
            // Stack: [subject]
            
            // Step 3: Create tuple of required keys and match them
            // Performance: Eliminated LINQ
            var keysList = new List<string>(pattern.Patterns.Keys);

            // CPython 3.12 방식: 컴파일 시점에 튜플 상수 직접 생성
            // Performance: Eliminated LINQ
            var keysArray = new PyObject[keysList.Count];
            for (int i = 0; i < keysList.Count; i++)
            {
                keysArray[i] = new PyString(keysList[i]);
            }
            var keysTuple = new PyTuple(keysArray);
            EmitLoadConst(keysTuple);
            // Stack: [subject, keys_tuple]
            
            EmitInstruction(ByteCodeOp.MATCH_KEYS);
            // Stack: [subject, values_tuple_or_None]

            // Step 4: Check if keys matched (MATCH_KEYS returns None if no match)
            EmitInstruction(ByteCodeOp.COPY, 1); // Copy result for None check
            _instructionSequence.AddOpWithLabel(ByteCodeOp.POP_JUMP_IF_NONE, failLabel, _currentLineNumber, _currentColumnOffset, _currentFileName);
            // Stack: [subject, values_tuple]
            
            // Step 5: Unpack values and bind to pattern variables
            EmitInstruction(ByteCodeOp.UNPACK_SEQUENCE, keysList.Count);
            // Stack: [subject, value1, value2, ...]
            
            // Step 6: Store values to variables (in correct order - same as keys order)
            // CPython 3.12: UNPACK_SEQUENCE puts values on stack in same order as keys tuple
            // First key gets first value, second key gets second value, etc.
            for (int i = 0; i < keysList.Count; i++)
            {
                var key = keysList[i];
                var valuePattern = pattern.Patterns[key];
                
                if (valuePattern is NameExpression nameExpr)
                {
                    EmitStoreName(nameExpr.Name);
                }
                else
                {
                    // Handle complex patterns recursively (sequence, mapping, etc.)
                    // Stack has value for this pattern, need to match it
                    var subFailLabel = _instructionSequence.NewLabel();

                    // Create a new context with the sub-fail label
                    var subPatternContext = pc.Clone();
                    subPatternContext.FailPop.Clear();
                    subPatternContext.FailPop[0] = subFailLabel;

                    // Compile the nested pattern recursively
                    if (!CompilePatternMatchCFG(valuePattern, subPatternContext))
                    {
                        // If nested pattern compilation fails, cleanup and fail
                        _instructionSequence.UseLabel(subFailLabel);
                        return false;
                    }
                    
                    // If nested pattern succeeds, continue with remaining patterns
                    // (Note: nested pattern should handle its own variable bindings)
                }
            }
            // Stack: [subject]
            
            // Step 7: Handle **rest pattern if present
            if (!string.IsNullOrEmpty(pattern.RestVariable))
            {
                // Create rest dict by copying subject and removing matched keys
                // Stack: [subject]

                // Duplicate subject for creating rest dict
                EmitInstruction(ByteCodeOp.COPY, 1);
                // Stack: [subject, subject_copy]

                // Create the rest dict by removing matched keys from subject_copy
                foreach (var key in keysList)
                {
                    // Load the key to delete and delete it from subject_copy
                    EmitInstruction(ByteCodeOp.COPY, 1); // Copy subject_copy
                    CompileExpression(new ConstantExpression(new PyString(key)));
                    EmitInstruction(ByteCodeOp.DELETE_SUBSCR);
                    // Stack: [subject, subject_copy_without_key]
                }

                // Store the rest dict to the rest variable
                EmitStoreName(pattern.RestVariable);
                // Stack: [subject]
            }

            // Step 8: Clean up - remove subject since pattern matched
            EmitInstruction(ByteCodeOp.POP_TOP); // Remove subject

            return true;
        }
        
        private void CompileAssert(AssertStatement assert)
        {
            // CPython 3.12: assert test [, msg]
            // Pattern: test → POP_JUMP_IF_TRUE → LOAD_ASSERTION_ERROR [→ msg → CALL 0] → RAISE_VARARGS 1

            // Compile test expression
            CompileExpression(assert.Test);

            // Create label for end of assert (when test is true)
            var endLabel = _instructionSequence.NewLabel();

            // If test is true, skip the assertion error
            _instructionSequence.AddOpWithLabel(ByteCodeOp.POP_JUMP_IF_TRUE, endLabel, _currentLineNumber);

            // Load AssertionError class (CPython 3.12 calling convention)
            _instructionSequence.AddOp(ByteCodeOp.PUSH_NULL, _currentLineNumber);
            _instructionSequence.AddOp(ByteCodeOp.LOAD_ASSERTION_ERROR, _currentLineNumber);

            if (assert.Msg != null)
            {
                // assert test, msg: AssertionError(msg)
                CompileExpression(assert.Msg);
                _instructionSequence.AddOpWithArg(ByteCodeOp.CALL, 1, _currentLineNumber);
            }
            else
            {
                // assert test: AssertionError()
                _instructionSequence.AddOpWithArg(ByteCodeOp.CALL, 0, _currentLineNumber);
            }

            // Raise the AssertionError
            _instructionSequence.AddOpWithArg(ByteCodeOp.RAISE_VARARGS, 1, _currentLineNumber);

            // Mark end of assert
            // CPython 3.12: Python/compile.c:3164 - ADDOP(c, loc, NOP)
            // CRITICAL FIX (2025-11-29): Add NOP before endLabel
            // Without NOP, FindNextRealInstruction skips POP_BLOCK and endLabel points to PUSH_EXC_INFO
            _instructionSequence.AddOp(ByteCodeOp.NOP, _currentLineNumber);
            _instructionSequence.UseLabel(endLabel);
        }
        private void CompileRaise(RaiseStatement raise)
        {
            if (raise.Exc != null)
            {
                // raise Exception(...) [from Cause] - compile the exception expression
                CompileExpression(raise.Exc);

                if (raise.Cause != null)
                {
                    // raise Exception from Cause
                    CompileExpression(raise.Cause);
                    EmitInstruction(ByteCodeOp.RAISE_VARARGS, 2);
                }
                else
                {
                    // raise Exception
                    EmitInstruction(ByteCodeOp.RAISE_VARARGS, 1);
                }
            }
            else
            {
                // bare raise - re-raise current exception
                EmitInstruction(ByteCodeOp.RERAISE);
            }
        }
        private void CompileDelete(DeleteStatement delete)
        {
            // CPython 3.12: del target1, target2, ...
            foreach (var target in delete.Targets)
            {
                if (target is NameExpression nameExpr)
                {
                    // Delete variable: DELETE_NAME, DELETE_FAST, DELETE_GLOBAL, DELETE_DEREF
                    EmitDeleteName(nameExpr.Name);
                }
                else if (target is AttributeExpression attrExpr)
                {
                    // Delete attribute: obj.attr -> DELETE_ATTR
                    CompileExpression(attrExpr.Value);
                    var attrIndex = AddName(attrExpr.Attr);
                    EmitInstruction(ByteCodeOp.DELETE_ATTR, attrIndex);
                }
                else if (target is SubscriptExpression subscrExpr)
                {
                    // Delete item: obj[key] -> DELETE_SUBSCR
                    CompileExpression(subscrExpr.Value);
                    CompileExpression(subscrExpr.Slice);
                    EmitInstruction(ByteCodeOp.DELETE_SUBSCR);
                }
                else
                {
                    throw new PythonException(new PySyntaxError($"can't delete {target.GetType().Name}"));
                }
            }
        }
        private void CompileGlobal(GlobalStatement global)
        {
            // CPython 3.12: global 선언은 컴파일 타임에 스코프 분석에 영향을 줌
            // 런타임 바이트코드는 생성하지 않음
            foreach (var name in global.Names)
            {
                _globalVars.Add(name);
                _moduleGlobalVars.Add(name); // 모듈 전역에도 추가
                #if DEBUG_LOG
                Console.WriteLine($"🌍 Global variable declared: {name}");
                #endif
            }
        }
        private void CompileNonlocal(NonlocalStatement nonlocal)
        {
            // CPython 3.12: nonlocal 선언은 컴파일 타임에 스코프 분석에 영향을 줌
            // 런타임 바이트코드는 생성하지 않음
            foreach (var name in nonlocal.Names)
            {
                _nonlocalVars.Add(name);
                #if DEBUG_LOG
                Console.WriteLine($"🔗 Nonlocal variable declared: {name}");
                #endif
            }
        }
        private void CompileBoolOp(BoolOpExpression boolOp)
        {
            // CPython 3.12: Boolean operations with short-circuiting
            // For 'and': use POP_JUMP_IF_FALSE to skip rest if falsy
            // For 'or': use POP_JUMP_IF_TRUE to skip rest if truthy

            if (boolOp.Values.Count < 2)
            {
                // Single operand, just compile it
                if (boolOp.Values.Count == 1)
                {
                    CompileExpression(boolOp.Values[0]);
                }
                return;
            }

            // Use InstructionSequence Label (not Legacy Label)
            var endLabel = _instructionSequence.NewLabel();

            // Compile all operands except the last with short-circuit logic
            for (int i = 0; i < boolOp.Values.Count - 1; i++)
            {
                CompileExpression(boolOp.Values[i]);

                // CPython 3.12: DUP_TOP (similar to COPY 1)
                EmitInstruction(ByteCodeOp.COPY, 1);

                if (boolOp.OpNode is And)
                {
                    // For 'and': if current value is falsy, jump to end (short-circuit)
                    _instructionSequence.AddOpWithLabel(ByteCodeOp.POP_JUMP_IF_FALSE, endLabel, _currentLineNumber);
                }
                else if (boolOp.OpNode is Or)
                {
                    // For 'or': if current value is truthy, jump to end (short-circuit)
                    _instructionSequence.AddOpWithLabel(ByteCodeOp.POP_JUMP_IF_TRUE, endLabel, _currentLineNumber);
                }

                // If we didn't short-circuit, pop the duplicate and continue
                EmitInstruction(ByteCodeOp.POP_TOP);
            }

            // Compile the last operand (no short-circuit needed)
            CompileExpression(boolOp.Values[boolOp.Values.Count - 1]);

            // Place end label
            _instructionSequence.UseLabel(endLabel);
        }

        /// <summary>
        /// Compile chained comparison to match CPython 3.12 bytecode exactly
        /// Pattern: a < b < c generates SWAP, COPY, COMPARE_OP with proper cleanup
        /// </summary>
        private void CompileChainedComparison(ChainedCompareExpression chainedCompare)
        {
            if (chainedCompare.Operators.Count == 0)
            {
                // No comparisons, just compile the left operand
                CompileExpression(chainedCompare.Left);
                return;
            }

            if (chainedCompare.Operators.Count == 1)
            {
                // Single comparison, use regular CompareExpression logic
                CompileExpression(chainedCompare.Left);
                CompileExpression(chainedCompare.Comparators[0]);
                EmitCompareOp(chainedCompare.Operators[0]);
                return;
            }

            // Load first two operands for the first comparison
            CompileExpression(chainedCompare.Left);
            CompileExpression(chainedCompare.Comparators[0]);

            // Generate cleanup and end labels for short-circuiting (use InstructionSequence Label)
            var cleanupLabel = _instructionSequence.NewLabel();
            var endLabel = _instructionSequence.NewLabel();

            // CPython pattern for first comparison
            EmitInstruction(ByteCodeOp.SWAP, 2);      // Stack: [b, a]
            EmitInstruction(ByteCodeOp.COPY, 2);      // Stack: [b, a, b]
            EmitCompareOp(chainedCompare.Operators[0]); // Stack: [b, result1]
            EmitInstruction(ByteCodeOp.COPY, 1);      // Stack: [b, result1, result1]
            _instructionSequence.AddOpWithLabel(ByteCodeOp.POP_JUMP_IF_FALSE, cleanupLabel, _currentLineNumber); // Stack: [b, result1]
            EmitInstruction(ByteCodeOp.POP_TOP);      // Stack: [b]

            // Handle remaining comparisons
            for (int i = 1; i < chainedCompare.Operators.Count; i++)
            {
                var isLastComparison = (i == chainedCompare.Operators.Count - 1);

                // Load next operand
                CompileExpression(chainedCompare.Comparators[i]); // Stack: [prev, curr]

                if (!isLastComparison)
                {
                    // Intermediate comparison
                    EmitInstruction(ByteCodeOp.SWAP, 2);     // Stack: [curr, prev]
                    EmitInstruction(ByteCodeOp.COPY, 2);     // Stack: [curr, prev, curr]
                    EmitCompareOp(chainedCompare.Operators[i]); // Stack: [curr, result]
                    EmitInstruction(ByteCodeOp.COPY, 1);     // Stack: [curr, result, result]
                    _instructionSequence.AddOpWithLabel(ByteCodeOp.POP_JUMP_IF_FALSE, cleanupLabel, _currentLineNumber); // Stack: [curr, result]
                    EmitInstruction(ByteCodeOp.POP_TOP);     // Stack: [curr]
                }
                else
                {
                    // Last comparison - CPython doesn't SWAP here
                    EmitCompareOp(chainedCompare.Operators[i]); // Stack: [result]
                }
            }

            // Jump to end after successful completion
            _instructionSequence.AddOpWithLabel(ByteCodeOp.JUMP, endLabel, _currentLineNumber);

            // Cleanup: when any comparison fails
            _instructionSequence.UseLabel(cleanupLabel);
            EmitInstruction(ByteCodeOp.SWAP, 2);
            EmitInstruction(ByteCodeOp.POP_TOP);

            // End label
            _instructionSequence.UseLabel(endLabel);
        }

        /// <summary>
        /// Compile chained comparison from CompareExpression (CPython 3.12 compatible)
        /// Pattern: a < b < c generates SWAP, COPY, COMPARE_OP with proper cleanup
        /// </summary>
        private void CompileChainedComparisonFromCompare(CompareExpression compare)
        {
            // Load first two operands for the first comparison
            CompileExpression(compare.Left);
            CompileExpression(compare.Comparators[0]);

            // Generate cleanup and end labels for short-circuiting
            var cleanupLabel = _instructionSequence.NewLabel();
            var endLabel = _instructionSequence.NewLabel();

            // CPython pattern for first comparison
            EmitInstruction(ByteCodeOp.SWAP, 2);      // Stack: [b, a]
            EmitInstruction(ByteCodeOp.COPY, 2);      // Stack: [b, a, b]
            EmitCompareOp(compare.Ops[0]); // Stack: [b, result1]
            EmitInstruction(ByteCodeOp.COPY, 1);      // Stack: [b, result1, result1]
            _instructionSequence.AddOpWithLabel(ByteCodeOp.POP_JUMP_IF_FALSE, cleanupLabel, _currentLineNumber); // Stack: [b, result1]
            EmitInstruction(ByteCodeOp.POP_TOP);      // Stack: [b]

            // Handle remaining comparisons
            for (int i = 1; i < compare.Ops.Count; i++)
            {
                var isLastComparison = (i == compare.Ops.Count - 1);

                // Load next operand
                CompileExpression(compare.Comparators[i]); // Stack: [prev, curr]

                if (!isLastComparison)
                {
                    // Intermediate comparison
                    EmitInstruction(ByteCodeOp.SWAP, 2);     // Stack: [curr, prev]
                    EmitInstruction(ByteCodeOp.COPY, 2);     // Stack: [curr, prev, curr]
                    EmitCompareOp(compare.Ops[i]); // Stack: [curr, result]
                    EmitInstruction(ByteCodeOp.COPY, 1);     // Stack: [curr, result, result]
                    _instructionSequence.AddOpWithLabel(ByteCodeOp.POP_JUMP_IF_FALSE, cleanupLabel, _currentLineNumber); // Stack: [curr, result]
                    EmitInstruction(ByteCodeOp.POP_TOP);     // Stack: [curr]
                }
                else
                {
                    // Last comparison - CPython doesn't SWAP here
                    EmitCompareOp(compare.Ops[i]); // Stack: [result]
                }
            }

            // Jump to end after successful completion
            _instructionSequence.AddOpWithLabel(ByteCodeOp.JUMP, endLabel, _currentLineNumber);

            // Cleanup: when any comparison fails
            _instructionSequence.UseLabel(cleanupLabel);
            EmitInstruction(ByteCodeOp.SWAP, 2);
            EmitInstruction(ByteCodeOp.POP_TOP);

            // End label
            _instructionSequence.UseLabel(endLabel);
        }

        private void CompileLambda(LambdaExpression lambda)
        {
            // CPython 3.12 compatible lambda compilation
            // Lambda creates an anonymous function object with proper parameter scope

            // Create a unique name for the lambda function
            string lambdaName = $"<lambda_{_lambdaCounter++}>";

            // CPython 3.12: Extract clean parameter names and default values
            // Python/compile.c:2590-2650 (compiler_lambda)
            var cleanParamNames = new List<string>();
            var defaultValues = new List<PyObject>();

            // First, collect all clean parameter names (without '=' parsing)
            foreach (var arg in lambda.Args)
            {
                // Check if it's a raw name or contains '='
                if (arg.Contains("="))
                {
                    // Legacy: Parameter with default in string format
                    var parts = arg.Split('=', 2);
                    cleanParamNames.Add(parts[0].Trim());
                }
                else
                {
                    cleanParamNames.Add(arg.Trim());
                }
            }

            // CPython 3.12: Use lambda.Defaults if available (from AST)
            // This is the proper way - defaults are stored in the AST node as Expression
            // We need to evaluate them at compile time to get PyObject values
            if (lambda.Defaults != null && lambda.Defaults.Count > 0)
            {
                foreach (var defaultExpr in lambda.Defaults)
                {
                    // CPython 3.12: Evaluate constant expressions at compile time
                    // Python/compile.c - compiler_lambda()
                    var defaultValue = EvaluateConstantExpression(defaultExpr);
                    defaultValues.Add(defaultValue);
                }
                #if DEBUG_LOG
                Console.WriteLine($"  → Using {lambda.Defaults.Count} defaults from lambda.Defaults");
                #endif
            }
            else
            {
                // Fallback: Parse defaults from string (legacy behavior)
                foreach (var arg in lambda.Args)
                {
                    if (arg.Contains("="))
                    {
                        var parts = arg.Split('=', 2);
                        var defaultValueStr = parts[1].Trim();
                        var defaultValue = ParseAndEvaluateDefaultValue(defaultValueStr);
                        defaultValues.Add(defaultValue);
                        #if DEBUG_LOG
                        Console.WriteLine($"  → Parsed default from string: {defaultValue}");
                        #endif
                    }
                }
            }

            // CPython 3.12: Look up symbol table by AST node reference (not by name!)
            // This matches CPython's st_blocks lookup: PyDict_GetItem(st->st_blocks, (void *)e)
            var lambdaTable = FindLambdaSymbolTableByNode(lambda);
            var (freeVars, cellVars) = GetFreeAndCellVariables(lambdaTable);
            
            #if DEBUG_LOG
            Console.WriteLine($"\n🔍 Lambda analysis: {lambdaName}");
            #endif
            #if DEBUG_LOG
            Console.WriteLine($"  Parameters: [{string.Join(", ", lambda.Args)}]");
            #endif
            #if DEBUG_LOG
            Console.WriteLine($"  Clean parameters: [{string.Join(", ", cleanParamNames)}]");
            #endif
            #if DEBUG_LOG
            Console.WriteLine($"  Free variables: [{string.Join(", ", freeVars)}]");
            #endif
            #if DEBUG_LOG
            Console.WriteLine($"  Cell variables: [{string.Join(", ", cellVars)}]");
            #endif
            
            // Compile lambda body in a separate compiler context
            var lambdaInstructionSequence = new InstructionSequence();
            var lambdaConstants = new List<PyObject>();
            var lambdaNames = new List<string>();

            // Save current compiler state
            var tempInstructionSequence = _instructionSequence;
            var tempConstants = _constants;
            var tempNames = _names;
            var tempVarNames = _varNames; // Save current VarNames
            var tempCellVars = _cellVars; // Save current CellVars
            var tempFreeVars = _freeVars; // Save current FreeVars
            var tempIsInFunction = _isInFunction; // Save function context flag

            // Set up lambda compiler context
            _instructionSequence = lambdaInstructionSequence;
            _constants = lambdaConstants;
            _names = lambdaNames;
            _varNames = new List<string>(); // Fresh VarNames for lambda
            _isInFunction = true; // CPython 3.12: Lambdas are functions, use LOAD_FAST for parameters

            // Parameters must be first in VarNames for LOAD_FAST to work
            foreach (var paramName in cleanParamNames)
            {
                _varNames.Add(paramName);
                #if DEBUG_LOG
                Console.WriteLine($"  → Added parameter '{paramName}' as FAST variable at index {_varNames.Count - 1}");
                #endif
            }

            // CPython 3.12: CRITICAL - Always clear and set _cellVars and _freeVars for lambda
            // Even if empty, we must clear parent scope's cellVars (like __class__ from class body)
            // This ensures lambda doesn't inherit cell variables from enclosing class/function
            _cellVars = new List<string>(cellVars);
            _freeVars = new List<string>(freeVars);

            #if DEBUG_LOG
            Console.WriteLine($"  🔧 Set lambda cellVars: [{string.Join(", ", _cellVars)}]");
            Console.WriteLine($"  🔧 Set lambda freeVars: [{string.Join(", ", _freeVars)}]");
            #endif

            // Set lambda symbol table context for proper variable resolution
            var originalSymbolTable = _currentSymbolTable;
            if (lambdaTable != null)
            {
                _currentSymbolTable = lambdaTable;
                #if DEBUG_LOG
                Console.WriteLine($"  🔧 Set lambda symbol table context: {lambdaTable.GetName()}");
                #endif
            }
            
            // Phase 2: Cell 변수들을 위한 MAKE_CELL 명령어 발행 (람다 파라미터용)
            // CPython 3.12: MAKE_CELL argument is localsplus offset (varnames index for parameters)
            for (int cellIndex = 0; cellIndex < cellVars.Count; cellIndex++)
            {
                var cellVar = cellVars[cellIndex];
                if (cleanParamNames.Contains(cellVar))
                {
                    // Get the varnames index (localsplus offset) for this parameter
                    var localsPlusOffset = _varNames.IndexOf(cellVar);
                    #if DEBUG_LOG
                    Console.WriteLine($"  → Making cell for lambda parameter: {cellVar} (localsplus offset {localsPlusOffset}, cellvars index {cellIndex})");
                    #endif
                    EmitInstruction(ByteCodeOp.MAKE_CELL, localsPlusOffset);
                }
            }
            
            // Compile the lambda body expression - parameters will now be recognized as FAST variables
            CompileExpression(lambda.Body);
            EmitInstruction(ByteCodeOp.RETURN_VALUE);

            // Get final instructions for lambda
            // Lambda has no generator flags (lambdas cannot be generators in Python)
            var lambdaInstructions = GetFinalInstructions(0);

            // Restore original compiler context
            _instructionSequence = tempInstructionSequence;
            _constants = tempConstants;
            _names = tempNames;
            _varNames = tempVarNames; // Restore original VarNames
            _cellVars = tempCellVars; // Restore original CellVars
            _freeVars = tempFreeVars; // Restore original FreeVars
            _isInFunction = tempIsInFunction; // Restore function context flag
            _currentSymbolTable = originalSymbolTable; // Restore original symbol table

            // CPython 3.12: Create function code object with correct VarNames order
            // VarNames = parameters only (CPython co_varnames contains only local variables and parameters)
            // Note: lambdaNames is the 'names' array (for LOAD_ATTR, LOAD_GLOBAL), NOT varnames!
            // See CPython: lambda with no params has co_varnames=(), co_names=('append', 'instantiate')
            var lambdaVarNames = new List<string>(cleanParamNames);

            var functionCode = new PyCodeObject(
                lambdaName,
                lambdaInstructions,
                lambdaConstants,
                lambdaNames,
                lambdaVarNames, // VarNames with parameters first
                cleanParamNames.Count, // Use clean parameter count
                0, // posonlyArgCount
                0, // kwonlyArgCount
                freeVars, // Set FreeVars for closure support
                cellVars, // Set CellVars for closure support
                defaultValues: defaultValues, // CPython 3.12: Pass default values
                kwDefaults: null,
                flags: 0,
                fileName: _currentFileName,
                sourceLines: _sourceLines
            );
            
            #if DEBUG_LOG
            Console.WriteLine($"  → Lambda code object created: {lambdaVarNames.Count} variables, {cleanParamNames.Count} parameters, {defaultValues.Count} defaults");
            #endif
            
            // CPython 3.12: Handle default values if present (스택 순서 1)
            // Default values must be compiled as expressions, not evaluated at compile time
            // This ensures variables like 'i' in 'lambda x, i=i: x * i' are properly resolved at runtime
            if (lambda.Defaults != null && lambda.Defaults.Count > 0)
            {
                // Compile default value expressions - they will be evaluated at function definition time
                foreach (var defaultExpr in lambda.Defaults)
                {
                    CompileExpression(defaultExpr);
                }
                EmitInstruction(ByteCodeOp.BUILD_TUPLE, lambda.Defaults.Count);
                #if DEBUG_LOG
                Console.WriteLine($"  → Built defaults tuple: {lambda.Defaults.Count} defaults (compiled expressions)");
                #endif
            }
            else if (defaultValues.Count > 0)
            {
                // Fallback: Load pre-evaluated default values as constants (legacy behavior)
                foreach (var defaultValue in defaultValues)
                {
                    EmitLoadConst(defaultValue);
                }
                EmitInstruction(ByteCodeOp.BUILD_TUPLE, defaultValues.Count);
                #if DEBUG_LOG
                Console.WriteLine($"  → Built defaults tuple: {defaultValues.Count} defaults (constants)");
                #endif
            }
            
            // Handle closure creation if there are free variables (스택 순서 2)
            if (freeVars.Count > 0)
            {
                #if DEBUG_LOG
                Console.WriteLine($"  → Creating closure with {freeVars.Count} free variables");
                #endif
                
                // Load closure cells for free variables
                foreach (var freeVar in freeVars)
                {
                    // Emit LOAD_CLOSURE for each free variable
                    // In CPython 3.12, closure refers to cell variables in current scope
                    EmitLoadClosure(freeVar);
                }
                
                // Build tuple of closure cells
                EmitInstruction(ByteCodeOp.BUILD_TUPLE, freeVars.Count);
            }
            
            // Load the function code object
            EmitInstruction(ByteCodeOp.LOAD_CONST, GetOrAddConstant(functionCode));
            
            // CPython 3.12: MAKE_FUNCTION 플래그 동적 계산
            int flags = 0;
            bool hasDefaults = (lambda.Defaults != null && lambda.Defaults.Count > 0) || defaultValues.Count > 0;
            if (hasDefaults)
            {
                flags |= MakeFunctionFlags.DEFAULTS;
            }
            if (freeVars.Count > 0)
            {
                flags |= MakeFunctionFlags.CLOSURE;
            }

            #if DEBUG_LOG
            Console.WriteLine($"  → MAKE_FUNCTION flags: {flags} (defaults={hasDefaults}, closure={freeVars.Count > 0})");
            #endif
            EmitInstruction(ByteCodeOp.MAKE_FUNCTION, flags);
        }
        
        // Lambda counter for unique names
        // IMPORTANT: Must NOT be static - each compilation session should start from 0
        // to match symbol table lambda naming (which also starts from 0 per session)
        private int _lambdaCounter = 0;

        private SymbolTable? FindLambdaSymbolTable(string lambdaName)
        {
            // Find lambda symbol table from current symbol table context (not root)
            #if DEBUG_LOG
            Console.WriteLine($"  🔍 Looking for lambda symbol table: {lambdaName}");
            Console.WriteLine($"  🔍 Current symbol table context: {_currentSymbolTable?.GetName()}");
            #endif
            var result = _currentSymbolTable != null ? FindSymbolTableByName(_currentSymbolTable, lambdaName) : null;
            #if DEBUG_LOG
            Console.WriteLine($"  🔍 Found lambda symbol table: {result?.GetName()} (null: {result == null})");
            #endif
            return result;
        }

        /// <summary>
        /// CPython 3.12: Look up lambda symbol table by AST node reference
        /// Matches CPython's st_blocks lookup: PyDict_GetItem(st->st_blocks, (void *)e)
        /// See Python/compile.c:2955 - compiler_enter_scope with (void *)e
        /// See Python/symtable.c:2065 - symtable_enter_block with (void *)e
        /// </summary>
        private SymbolTable? FindLambdaSymbolTableByNode(LambdaExpression lambda)
        {
            if (_symbolTableBuilder == null)
            {
                #if DEBUG_LOG
                Console.WriteLine($"  🔍 WARNING: _symbolTableBuilder is NULL, cannot lookup lambda by node");
                #endif
                return null;
            }

            // CPython 3.12: Use AST node pointer lookup (st_blocks)
            var result = _symbolTableBuilder.LookupSymbolTable(lambda);

            #if DEBUG_LOG
            Console.WriteLine($"  🔍 Looking up lambda by AST node reference");
            Console.WriteLine($"  🔍 Found lambda symbol table: {result?.GetName()} (null: {result == null})");
            if (result != null)
            {
                Console.WriteLine($"  🔍 Lambda table symbols: {result.GetSymbols().Count}");
                foreach (var (name, symbol) in result.GetSymbols())
                {
                    Console.WriteLine($"      {name}: {symbol.Scope} ({symbol.Flags})");
                }
            }
            #endif

            return result;
        }

        private (List<string> freeVars, List<string> cellVars) GetFreeAndCellVariables(SymbolTable? table)
        {
            var freeVars = new List<string>();
            var cellVars = new List<string>();

            if (table == null) return (freeVars, cellVars);

            // Extract variables based on Symbol Table analysis (CPython 3.12 way)
            foreach (var (name, symbol) in table.GetSymbols())
            {
                if (symbol.Scope == SymbolScope.Free)
                {
                    freeVars.Add(name);
                }
                else if (symbol.Scope == SymbolScope.Cell)
                {
                    cellVars.Add(name);
                }
            }

            // CPython 3.12: Free/cell variables must be in ALPHABETICAL order!
            // This is critical for closure tuple creation and LOAD_CLOSURE indices.
            // See CPython: co_freevars and co_cellvars are sorted alphabetically
            freeVars.Sort(StringComparer.Ordinal);
            cellVars.Sort(StringComparer.Ordinal);

            #if DEBUG_LOG
            Console.WriteLine($"  Symbol Table Free Variables (sorted): [{string.Join(", ", freeVars)}]");
            Console.WriteLine($"  Symbol Table Cell Variables (sorted): [{string.Join(", ", cellVars)}]");
            Console.WriteLine($"  Symbol Table Debug - Table: {table?.GetName()}, Symbols: {table?.GetSymbols().Count}");
            if (table != null)
            {
                foreach (var (name, symbol) in table.GetSymbols())
                {
                    Console.WriteLine($"    → {name}: Scope={symbol.Scope}, Flags={symbol.Flags}");
                }
            }
            #endif

            return (freeVars, cellVars);
        }
        private void CompileConditional(ConditionalExpression conditional)
        {
            // CPython 3.12 조건부 표현식: A if B else C
            // CPython은 조건부 표현식에서 코드 중복 방식을 사용함
            // 조건을 평가한 후 각 분기에서 전체 표현식 컨텍스트를 중복 실행

            var elseLabel = _instructionSequence.NewLabel();

            // 1. 조건(B) 평가
            CompileExpression(conditional.Test);

            // 2. 조건이 False면 else 부분으로 점프
            _instructionSequence.AddOpWithLabel(ByteCodeOp.POP_JUMP_IF_FALSE, elseLabel, _currentLineNumber);

            // 3. True 분기: Body 값만 로드 (CPython 3.12 호환성)
            CompileExpression(conditional.Body);

            // 4. else 라벨 없이 직접 계속 (CPython처럼 중복 없음)
            // CPython은 여기서 JUMP하지 않고 다음 명령어로 계속감
            // 하지만 우리는 expression context에서 동작해야 하므로
            // 최소한의 점프 사용
            if (IsInComplexExpression())
            {
                var endLabel = _instructionSequence.NewLabel();
                _instructionSequence.AddOpWithLabel(ByteCodeOp.JUMP, endLabel, _currentLineNumber);

                // 5. False 분기
                _instructionSequence.UseLabel(elseLabel);
                CompileExpression(conditional.OrElse);

                _instructionSequence.UseLabel(endLabel);
            }
            else
            {
                // 단순 표현식의 경우 CPython의 코드 중복 패턴 모방
                // 실제로는 분기 없이 값만 스택에 남김
                _instructionSequence.UseLabel(elseLabel);
                CompileExpression(conditional.OrElse);
            }
        }
        
        private bool IsInComplexExpression()
        {
            // 복잡한 표현식 컨텍스트인지 확인하는 간단한 휴리스틱
            // 실제 구현에서는 더 정교한 컨텍스트 추적이 필요
            return true; // 일단 모든 경우를 복잡한 표현식으로 처리
        }
        /// <summary>
        /// CPython 3.12 compatible f-string compilation
        /// Generates BUILD_STRING bytecode instruction
        /// </summary>
        private void CompileFString(FStringExpression fstring)
        {
            var values = fstring.Values;
            if (values == null || values.Count == 0)
            {
                // Empty f-string becomes empty string
                EmitLoadConst(new PyString(""));
                return;
            }

            // Compile each value
            foreach (var value in values)
            {
                CompileExpression(value);

                // For non-constant expressions, FORMAT_VALUE is already emitted by FStringFormattedValue
                // For constant strings, no additional formatting needed
            }

            // Use BUILD_STRING to join all parts (CPython 3.12 style)
            if (values.Count > 1)
            {
                EmitInstruction(ByteCodeOp.BUILD_STRING, values.Count);
            }
        }
        
        /// <summary>
        /// FormattedValue 컴파일 - 포맷 지정자를 지원하는 f-string 값
        /// </summary>
        private void CompileFormattedValue(FormattedValue formatted)
        {
            // 1. 값 표현식을 컴파일
            CompileExpression(formatted.Value);
            
            // 2. 포맷 지정자가 있으면 포맷팅 적용
            if (!string.IsNullOrEmpty(formatted.FormatSpec))
            {
                // 포맷 지정자를 상수로 스택에 푸시
                EmitLoadConst(new PyString(formatted.FormatSpec));
                
                // FORMAT_VALUE_WITH_SPEC 명령어 (또는 기본 FORMAT_VALUE)
                // CPython에서는 FORMAT_VALUE 명령어가 포맷 옵션을 받음
                EmitInstruction(ByteCodeOp.FORMAT_VALUE, 4); // 4 = format spec 있음
            }
            else
            {
                // 포맷 지정자 없음 - 기본 FORMAT_VALUE
                EmitInstruction(ByteCodeOp.FORMAT_VALUE, 0);
            }
        }

        /// <summary>
        /// CPython 3.12 호환: 중첩된 표현식을 포함한 포맷 지시자 컴파일
        /// f"{value:{width}.{precision}f}" 처리
        /// </summary>
        private void CompileFormatExpressionWithSpec(FormatExpressionWithSpec formatExpr)
        {
            // 1. 값 표현식을 컴파일
            CompileExpression(formatExpr.Value);

            // 2. 포맷 지시자 표현식을 컴파일하여 문자열로 변환
            CompileExpression(formatExpr.FormatSpec);

            // 3. FORMAT_VALUE with format spec (4 = format spec 있음)
            EmitInstruction(ByteCodeOp.FORMAT_VALUE, 4);
        }

        // Evaluate 메서드 - 나중에 구현
        public PyObject Evaluate(PyScope scope)
        {
            throw new NotImplementedException("PythonCompiler.Evaluate() - 나중에 구현예정");
        }
        
        #region CPython-style Compiler Helper Methods

        /// <summary>
        /// CPython 3.12 style: Emit comparison operation
        /// </summary>
        private void EmitComparison(CompareOp compareOp)
        {
            EmitInstruction(ByteCodeOp.COMPARE_OP, (int)compareOp);
        }
        
        /// <summary>
        /// CPython 3.12 style aliases for consistency
        /// </summary>
        private void EmitLoadConstant(PyObject value) => EmitLoadConst(value);
        private void EmitLoadVariable(string name) => EmitLoadName(name);
        private void EmitStoreVariable(string name)
        {
            // Check symbol table to determine correct storage instruction
            if (_symbolTable.GetSymbols().TryGetValue(name, out var symbol) && symbol.Scope == SymbolScope.Global)
            {
                // For global scope variables, use STORE_GLOBAL like CPython 3.12
                var index = AddName(name);
                EmitInstruction(ByteCodeOp.STORE_GLOBAL, index);
            }
            else
            {
                EmitStoreName(name);
            }
        }
        private void EmitLoadAttribute(string attrName) => EmitLoadAttr(attrName);
        private void EmitStoreAttribute(string attrName) => EmitStoreAttr(attrName);
        

        // ========== LEGACY CODE (Replaced by unified FBlock system) ==========
        // The following LoopContext code has been replaced by the CPython 3.12 style
        // unified FBlock system. Keeping for reference during transition period.
        // TODO: Remove after verifying all tests pass with new system.

        /*
        /// <summary>
        /// Loop context management for break/continue
        /// Uses unified FBlock system (FBlockInfo with FOR_LOOP/WHILE_LOOP types)
        /// </summary>
        private class LoopContext
        {
            public SharpPy.Label? NewBreakLabel { get; }
            public SharpPy.Label? NewContinueLabel { get; }
            public int ForIterInstruction { get; set; } = -1;
            public int EndForPosition { get; set; } = -1;

            public LoopContext(SharpPy.Label breakLabel, SharpPy.Label continueLabel)
            {
                NewBreakLabel = breakLabel;
                NewContinueLabel = continueLabel;
            }
        }

        private Stack<LoopContext> _loopStack = new();

        private void PushLoopContext(SharpPy.Label breakLabel, SharpPy.Label continueLabel, int forIterInstruction = -1)
        {
            var context = new LoopContext(breakLabel, continueLabel);
            if (forIterInstruction >= 0)
            {
                context.ForIterInstruction = forIterInstruction;
            }
            _loopStack.Push(context);
        }

        private void PopLoopContext()
        {
            if (_loopStack.Count > 0)
            {
                _loopStack.Pop();
            }
        }

        private LoopContext? GetCurrentLoop()
        {
            return _loopStack.Count > 0 ? _loopStack.Peek() : null;
        }
        */

        // ========== END OF LEGACY CODE ==========

        /// <summary>
        /// Pattern matching context - corresponds to CPython's pattern_context
        /// Python/compile.c: pattern_context structure
        /// Used to manage pattern matching state and failure labels
        /// </summary>
        /// <summary>
        /// CPython 3.12: pattern_context structure
        /// Context for pattern matching compilation (CFG path only)
        /// </summary>
        private class PatternContext
        {
            /// <summary>List of variable names to store after successful pattern match</summary>
            public List<string> Stores { get; set; } = new List<string>();

            /// <summary>Number of items currently on top of stack (not including subject)</summary>
            public int OnTop { get; set; } = 0;

            /// <summary>
            /// Failure pop labels - corresponds to CPython's fail_pop
            /// fail_pop[i] is the label to jump to when we need to pop i items before failing
            /// </summary>
            public Dictionary<int, SharpPy.Label> FailPop { get; set; } = new Dictionary<int, SharpPy.Label>();

            public bool AllowIrrefutable { get; set; } = true;

            public PatternContext Clone()
            {
                return new PatternContext
                {
                    Stores = new List<string>(Stores),
                    OnTop = OnTop,
                    FailPop = new Dictionary<int, SharpPy.Label>(FailPop),
                    AllowIrrefutable = AllowIrrefutable
                };
            }

            public SharpPy.Label GetFailLabel()
            {
                if (FailPop.ContainsKey(0))
                    return FailPop[0];
                throw new InvalidOperationException("Pattern context has no failure label");
            }
        }

        /// <summary>
        /// FOR 루프 컨텍스트 내부인지 확인
        /// CPython 3.12: Check if we're inside any loop (FOR_LOOP or WHILE_LOOP)
        /// </summary>
        private bool IsInForLoopContext()
        {
            // Check if any fblock on the stack is a loop
            return _fblock.Any(fb => fb.Type == FBlockType.FOR_LOOP || fb.Type == FBlockType.WHILE_LOOP);
        }
        
        /// <summary>
        /// FOR 루프 컨텍스트에서 패턴 매칭을 위한 스택 상태 보정
        /// Guard 패턴 등에서 스택 언더플로우 방지
        /// </summary>
        private void AdjustStackForForLoopMatch()
        {
            // FOR 루프 내에서 패턴 매칭 시 필요한 스택 조정
            // FOR_ITER가 루프 변수를 스택에 남겨둔 상태에서
            // match subject와 충돌 방지
            if (IsInForLoopContext())
            {
                // Console.WriteLine("🔧 FOR 루프 컨텍스트에서 패턴 매칭 스택 조정");
            }
        }
        
        /// <summary>
        /// Enhanced bytecode emission with proper name/constant management
        /// </summary>
        
        private int GetOrAddVarName(string name)
        {
            var index = _varNames.IndexOf(name);
            if (index == -1)
            {
                _varNames.Add(name);
                index = _varNames.Count - 1;
            }
            return index;
        }
        
        private int GetOrAddName(string name)
        {
            var index = _names.IndexOf(name);
            if (index == -1)
            {
                _names.Add(name);
                index = _names.Count - 1;
            }
            return index;
        }
        
        
        #endregion
        
        #region PEP 709 Comprehension Optimization
        
        /// <summary>
        /// PEP 709 - List comprehension 바이트코드 인라인 최적화
        /// CPython 3.12 호환: LOAD_FAST_AND_CLEAR + SWAP + Exception Table 패턴
        /// </summary>
        private void CompileListComprehension(ListComprehension listComp)
        {
            #if DEBUG_LOG
            Console.WriteLine("🚀 PEP 709: List comprehension 바이트코드 인라인 컴파일 (CPython 3.12 호환)");
            #endif

            // CPython 3.12: 컴프리헨션 컨텍스트 시작
            var savedIsInComprehension = _isInComprehension;
            _isInComprehension = true;

            // CPython 3.12 PEP 709: Inlined comprehensions use their own symbol table for iteration variables,
            // but fall back to enclosing scope for outer variables (handled in EmitLoadName/CompileCallExpression)
            var savedSymbolTable = _currentSymbolTable;
            var compSymbolTable = _symbolTableBuilder.LookupSymbolTable(listComp);
            if (compSymbolTable != null)
            {
                _currentSymbolTable = compSymbolTable;
                #if DEBUG_LOG
                Console.WriteLine($"🔧 Switched to comprehension symbol table: {compSymbolTable.Name}");
                #endif
            }

            // 중첩 깊이 추적 시작 (리스트 컴프리헨션)
            _comprehensionNestingDepth++;
            #if DEBUG_LOG
            Console.WriteLine($"🔍 List comprehension 중첩 깊이 증가: {_comprehensionNestingDepth}");
            #endif
            
            // CPython 3.12 패턴: 컴프리헨션 변수 사전 할당 및 정리 - 튜플 언패킹 지원
            // CPython의 ste_symbols와 동일하게, loop 변수와 walrus 변수를 분리 수집
            var comprehensionVars = new List<string>(); // Loop variables (for x in ...)
            var walrusVars = new List<string>(); // Walrus variables (y := ...)

            // Loop 변수 수집 (generator targets)
            foreach (var gen in listComp.Generators)
            {
                CollectComprehensionVars(gen.Target, comprehensionVars);
            }

            // Walrus 변수 수집 (filter 조건 및 element 표현식에서)
            // NOTE: CollectAllComprehensionVars for ListComprehension itself skips (for nested comps)
            // So we need to manually traverse the comprehension's internal components
            CollectAllComprehensionVars(listComp.Element, walrusVars);
            foreach (var gen in listComp.Generators)
            {
                foreach (var ifExpr in gen.Ifs)
                {
                    CollectAllComprehensionVars(ifExpr, walrusVars);
                }
            }

            // Walrus 변수에서 loop 변수 제거 (loop 변수는 Local, walrus만 Global)
            for (int i = walrusVars.Count - 1; i >= 0; i--)
            {
                if (comprehensionVars.Contains(walrusVars[i]))
                {
                    walrusVars.RemoveAt(i);
                }
            }

            // 전체 변수 = loop 변수 + walrus 변수 (LOAD_FAST_AND_CLEAR/STORE_FAST에 사용)
            var allVars = new List<string>(comprehensionVars);
            allVars.AddRange(walrusVars);

            #if DEBUG_LOG
            Console.WriteLine($"🔧 List comprehension loop vars: {string.Join(", ", comprehensionVars)} (count: {comprehensionVars.Count})");
            Console.WriteLine($"🔧 List comprehension walrus vars: {string.Join(", ", walrusVars)} (count: {walrusVars.Count})");
            Console.WriteLine($"🔧 List comprehension all vars: {string.Join(", ", allVars)} (count: {allVars.Count})");
            #endif

            // CPython 3.12 PEP 709: push_inlined_comprehension_state pattern
            // 모듈 레벨에서만 Walrus 변수를 Global scope로 변경
            // 함수 내부에서는 이미 Local (FAST) scope이므로 override 하지 않음
            var savedSymbolScopes = new Dictionary<string, SymbolScope>();
            var activeSymbolTable = _currentSymbolTable ?? _symbolTable;
            if (activeSymbolTable != null && activeSymbolTable.GetType() == SymbolTableType.Module)
            {
                foreach (var varName in walrusVars) // walrus 변수만!
                {
                    if (activeSymbolTable.GetSymbols().TryGetValue(varName, out var symbol))
                    {
                        // 원래 scope 저장
                        savedSymbolScopes[varName] = symbol.Scope;
                        // 임시로 Global로 변경 (walrus 변수는 comprehension 내부에서 GLOBAL처럼 동작)
                        symbol.Scope = SymbolScope.Global;
                        #if DEBUG_LOG
                        Console.WriteLine($"  📌 PEP 709: Override scope for '{varName}': {savedSymbolScopes[varName]} → Global");
                        #endif
                    }
                    else
                    {
                        // Symbol table에 없는 변수는 새로 등록 (walrus 변수)
                        var newSymbol = new Symbol(varName);
                        newSymbol.Scope = SymbolScope.Global;
                        newSymbol.Flags = SymbolFlags.Assigned;
                        activeSymbolTable.GetSymbols()[varName] = newSymbol;
                        savedSymbolScopes[varName] = SymbolScope.Unknown; // 나중에 삭제하기 위해 표시
                        #if DEBUG_LOG
                        Console.WriteLine($"  📌 PEP 709: Add walrus variable '{varName}' as Global");
                        #endif
                    }
                }
            }

            // 1. First compile the iterator source (CPython 3.12 pattern)
            var firstGenerator = listComp.Generators[0];

            // 1. CPython 3.12 정확한 순서: LOAD_CONST → GET_ITER → LOAD_FAST_AND_CLEAR → SWAP → BUILD_LIST → SWAP
            // 첫 번째 generator의 처리 방식 결정 (단순화)
            bool firstGeneratorOptimized = false; // 복잡한 최적화 제거

            if (!firstGeneratorOptimized)
            {
                // 첫 번째 generator가 일반 루프인 경우: GET_ITER 생성
                // Performance: Eliminated LINQ
                bool allElementsConstant = false;
                ListExpression firstIterList = null;
                if (firstGenerator.Iter is ListExpression tmpList)
                {
                    firstIterList = tmpList;
                    allElementsConstant = true;
                    foreach (var e in tmpList.Elements)
                    {
                        if (!(e is ConstantExpression))
                        {
                            allElementsConstant = false;
                            break;
                        }
                    }
                }
                if (allElementsConstant)
                {
                    // 상수 리스트 → 상수 튜플로 변환 (CPython 3.12 패턴)
                    // Performance: Eliminated LINQ
                    var constantElements = new PyObject[firstIterList.Elements.Count];
                    for (int i = 0; i < firstIterList.Elements.Count; i++)
                    {
                        constantElements[i] = ((ConstantExpression)firstIterList.Elements[i]).Value;
                    }
                    var tupleConstant = new PyTuple(constantElements);
                    EmitLoadConst(tupleConstant);
                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 리스트 컴프리헨션: 상수 리스트를 튜플로 변환 {tupleConstant}");
                    #endif
                }
                else
                {
                    // 일반적인 경우
                    CompileExpression(firstGenerator.Iter);
                }

                EmitInstruction(ByteCodeOp.GET_ITER);
            }
            // 첫 번째 generator가 단일 요소인 경우: GET_ITER 생성 안함

            // 2. LOAD_FAST_AND_CLEAR: 모든 컴프리헨션 변수 초기화 (CPython 3.12 패턴)
            foreach (var varName in allVars)
            {
                EmitInstruction(ByteCodeOp.LOAD_FAST_AND_CLEAR, GetOrAddVarName(varName));
            }

            // 3. 첫 번째 SWAP: 스택 재배치 (CPython 3.12 정확한 순서)
            if (allVars.Count > 0)
            {
                // CPython 3.12: SWAP 값 계산
                // 일반적인 경우: 변수 개수 + 1 (iterator 포함)
                // 첫 번째 generator 최적화된 경우: 변수 개수만 (iterator 없음)
                int swapArg = firstGeneratorOptimized ? allVars.Count : allVars.Count + 1;
                #if DEBUG_LOG
                Console.WriteLine($"🔧 CPython 3.12 첫 번째 SWAP: vars={allVars.Count}, firstOptimized={firstGeneratorOptimized}, swapArg={swapArg}");
                #endif
                EmitInstruction(ByteCodeOp.SWAP, swapArg); // 스택 재배치
            }

            // 4. BUILD_LIST 생성
            EmitInstruction(ByteCodeOp.BUILD_LIST, 0); // [] 빈 리스트 생성
            #if DEBUG_LOG
            Console.WriteLine($"🔧 CPython 3.12 BUILD_LIST 0 생성");
            #endif

            // 5. 두 번째 SWAP: 리스트를 올바른 위치로 이동 (CPython 3.12 패턴)
            EmitInstruction(ByteCodeOp.SWAP, 2);
            #if DEBUG_LOG
            Console.WriteLine($"🔧 CPython 3.12 두 번째 SWAP 2");
            #endif

            // 4. 중첩된 루프 컴파일 - CPython 3.12 재귀 구조 사용
            // CPython 3.12: CFG가 exception table을 자동으로 관리
            #if DEBUG_LOG
            Console.WriteLine($"🔧 Using CPython 3.12 recursive generator compilation");
            #endif
            CompileSyncComprehensionGenerator(
                generators: listComp.Generators,
                genIndex: 0,
                depth: 0,
                elt: listComp.Element,
                val: null,
                type: ComprehensionType.ListComp,
                comprehensionVars: comprehensionVars,
                iterOnStack: true // 최외곽 iterator는 이미 스택에 있음
            );

            // CPython 3.12 PEP 709: 정상 종료 시 컴프리헨션 변수 복원
            // CPython 패턴: 한 번의 SWAP으로 모든 변수를 재배치한 후 순차적으로 저장
            if (allVars.Count > 0)
            {
                // SWAP으로 스택 재배치: allVars.Count + 1
                EmitInstruction(ByteCodeOp.SWAP, allVars.Count + 1);
                #if DEBUG_LOG
                Console.WriteLine($"🔄 CPython 3.12 스택 재배치: SWAP {allVars.Count + 1}");
                #endif

                // 역순으로 변수 저장 (CPython 3.12 패턴: 마지막 변수부터)
                for (int i = allVars.Count - 1; i >= 0; i--)
                {
                    var varName = allVars[i];
                    EmitInstruction(ByteCodeOp.STORE_FAST, GetOrAddVarName(varName));
                    #if DEBUG_LOG
                    Console.WriteLine($"🔄 변수 복원: {varName} (STORE_FAST {GetOrAddVarName(varName)})");
                    #endif
                }
            }

            // CPython 3.12 PEP 709: pop_inlined_comprehension_state pattern
            // Walrus 변수의 scope를 원래대로 복원
            if (activeSymbolTable != null && savedSymbolScopes.Count > 0)
            {
                foreach (var kvp in savedSymbolScopes)
                {
                    var varName = kvp.Key;
                    var originalScope = kvp.Value;

                    if (originalScope == SymbolScope.Unknown)
                    {
                        // 새로 추가한 walrus 변수는 symbol table에서 제거
                        activeSymbolTable.GetSymbols().Remove(varName);
                        #if DEBUG_LOG
                        Console.WriteLine($"  📌 PEP 709: Remove walrus variable '{varName}' from symbol table");
                        #endif
                    }
                    else if (activeSymbolTable.GetSymbols().TryGetValue(varName, out var symbol))
                    {
                        // 원래 scope로 복원
                        symbol.Scope = originalScope;
                        #if DEBUG_LOG
                        Console.WriteLine($"  📌 PEP 709: Restore scope for '{varName}': Global → {originalScope}");
                        #endif
                    }
                }
            }

            // CPython 3.12: CFG's BuildExceptionTable()이 exception handler를 자동으로 관리

            // CPython 3.12: 컴프리헨션 컨텍스트 종료
            _isInComprehension = savedIsInComprehension;
            _currentSymbolTable = savedSymbolTable;

            // 중첩 깊이 추적 종료 (리스트 컴프리헨션)
            _comprehensionNestingDepth--;
            #if DEBUG_LOG
            Console.WriteLine($"🔍 List comprehension 중첩 깊이 감소: {_comprehensionNestingDepth}");
            #endif

            #if DEBUG_LOG
            Console.WriteLine($"✅ List comprehension 바이트코드 CPython 3.12 호환 완료 ({listComp.Generators.Count}개 중첩 generator)");
            #endif
        }
        
        /// <summary>
        /// CPython 3.12 Comprehension 타입
        /// </summary>
        private enum ComprehensionType
        {
            ListComp,
            SetComp,
            DictComp,
            GenExp
        }

        /// <summary>
        /// Comprehension target에서 변수 이름 추출
        /// </summary>
        private string? GetComprehensionVarName(Expression target)
        {
            if (target is NameExpression nameExpr)
            {
                return nameExpr.Name;
            }
            // 튜플 언패킹의 경우 첫 번째 변수만 반환 (간단화)
            if (target is TupleExpression tupleExpr && tupleExpr.Elements.Count > 0)
            {
                if (tupleExpr.Elements[0] is NameExpression firstNameExpr)
                {
                    return firstNameExpr.Name;
                }
            }
            return null;
        }

        /// <summary>
        /// CPython 3.12 compiler_sync_comprehension_generator 구현
        /// 재귀적으로 각 generator를 처리하며, 각 레벨마다 완전한 FOR 루프 구조 생성
        /// </summary>
        /// <param name="generators">모든 comprehension generators</param>
        /// <param name="genIndex">현재 처리 중인 generator 인덱스 (0-based)</param>
        /// <param name="depth">중첩 깊이 (LIST_APPEND/SET_ADD/MAP_ADD argument 계산용)</param>
        /// <param name="elt">element 표현식 (list/set) 또는 key 표현식 (dict)</param>
        /// <param name="val">value 표현식 (dict만 사용, 그 외는 null)</param>
        /// <param name="type">comprehension 타입</param>
        /// <param name="comprehensionVars">comprehension에서 사용하는 모든 변수 목록</param>
        /// <param name="iterOnStack">iterator가 이미 스택에 있는지 여부 (최외곽은 true)</param>
        /// <returns>성공 시 0, 실패 시 -1</returns>
        /// <summary>
        /// CPython 3.12: compiler_sync_comprehension_generator (line 5232-5349)
        /// Label-based control flow - no manual offset calculation/patching
        /// </summary>
        private void CompileSyncComprehensionGenerator(
            List<Comprehension> generators,
            int genIndex,
            int depth,
            Expression elt,
            Expression? val,
            ComprehensionType type,
            List<string> comprehensionVars,
            bool iterOnStack)
        {
            #if DEBUG_LOG
            Console.WriteLine($"🔄 CompileSyncComprehensionGenerator: genIndex={genIndex}, depth={depth}, iterOnStack={iterOnStack}");
            #endif

            // CPython 3.12: NEW_JUMP_TARGET_LABEL (line 5241-5243)
            var startLabel = _instructionSequence.NewLabel();
            var ifCleanupLabel = _instructionSequence.NewLabel();
            var anchorLabel = _instructionSequence.NewLabel();

            var gen = generators[genIndex];
            bool needsLoop = false;

            // CPython line 5248-5282: iterator preparation
            if (!iterOnStack)
            {
                if (genIndex == 0)
                {
                    // Outermost iter is already on stack (from caller)
                    // CPython: Receives as implicit argument (LOAD_FAST 0)
                }
                else
                {
                    // Nested generator: compile iterator expression
                    #if DEBUG_LOG
                    Console.WriteLine($"  📦 Compiling iterator expression for generator[{genIndex}]");
                    #endif
                    CompileGeneratorIterable(gen.Iter);
                    EmitInstruction(ByteCodeOp.GET_ITER);
                }
            }

            // CPython line 5284-5288: FOR_ITER with label (no position tracking!)
            if (genIndex == 0 || !iterOnStack)
            {
                depth++;
                needsLoop = true;

                // CPython: USE_LABEL(c, start)
                _instructionSequence.UseLabel(startLabel);

                // CPython: ADDOP_JUMP(c, loc, FOR_ITER, anchor)
                _instructionSequence.AddOpWithLabel(
                    ByteCodeOp.FOR_ITER,
                    anchorLabel,
                    _currentLineNumber,
                    _currentColumnOffset,
                    _currentFileName
                );

                #if DEBUG_LOG
                Console.WriteLine($"  🔁 FOR_ITER with label, depth now {depth}");
                #endif
            }

            // CPython line 5289: VISIT(c, expr, gen->target)
            CompileComprehensionTarget(gen.Target, comprehensionVars);

            // CPython line 5292-5296: condition handling with labels
            foreach (var condition in gen.Ifs)
            {
                CompileExpression(condition);

                // CPython: ADDOP_JUMP(c, loc, POP_JUMP_IF_FALSE, if_cleanup)
                _instructionSequence.AddOpWithLabel(
                    ByteCodeOp.POP_JUMP_IF_FALSE,
                    ifCleanupLabel,
                    _currentLineNumber,
                    _currentColumnOffset,
                    _currentFileName
                );

                #if DEBUG_LOG
                Console.WriteLine($"  ❓ Condition jump to if_cleanup label");
                #endif
            }

            // CPython line 5298-5338: recursion or element handling
            if (genIndex + 1 < generators.Count)
            {
                // Recursive call for next generator
                #if DEBUG_LOG
                Console.WriteLine($"  ↪️  Recursing to generator[{genIndex + 1}]");
                #endif
                CompileSyncComprehensionGenerator(
                    generators,
                    genIndex + 1,
                    depth,
                    elt,
                    val,
                    type,
                    comprehensionVars,
                    iterOnStack: false
                );
            }
            else
            {
                // Last generator: emit append operation
                #if DEBUG_LOG
                Console.WriteLine($"  🎯 Last generator, emitting append operation");
                #endif
                switch (type)
                {
                    case ComprehensionType.ListComp:
                        CompileExpression(elt);
                        EmitInstruction(ByteCodeOp.LIST_APPEND, depth + 1);
                        break;
                    case ComprehensionType.SetComp:
                        CompileExpression(elt);
                        EmitInstruction(ByteCodeOp.SET_ADD, depth + 1);
                        break;
                    case ComprehensionType.DictComp:
                        CompileExpression(elt);
                        if (val != null)
                        {
                            CompileExpression(val);
                        }
                        EmitInstruction(ByteCodeOp.MAP_ADD, depth + 1);
                        break;
                    case ComprehensionType.GenExp:
                        CompileExpression(elt);
                        EmitInstruction(ByteCodeOp.YIELD_VALUE);
                        EmitInstruction(ByteCodeOp.POP_TOP);
                        break;
                }
            }

            // CPython line 5340-5346: if_cleanup label and loop back
            if (needsLoop)
            {
                // CPython: USE_LABEL(c, if_cleanup)
                _instructionSequence.UseLabel(ifCleanupLabel);

                // CPython: ADDOP_JUMP(c, elt_loc, JUMP, start)
                _instructionSequence.AddOpWithLabel(
                    ByteCodeOp.JUMP,
                    startLabel,
                    -1, -1,
                    _currentFileName
                );

                #if DEBUG_LOG
                Console.WriteLine($"  ↩️  JUMP back to start label");
                #endif

                // CPython: USE_LABEL(c, anchor); ADDOP(c, NO_LOCATION, END_FOR)
                _instructionSequence.UseLabel(anchorLabel);
                EmitInstruction(ByteCodeOp.END_FOR, 0);

                #if DEBUG_LOG
                Console.WriteLine($"  🔚 END_FOR at anchor label");
                #endif
            }
        }


        /// <summary>
        /// CPython 3.12 호환 컴프리헨션 변수 저장
        /// 컴프리헨션 내부 변수는 격리된 스코프에서 관리
        /// Cell 변수일 때는 STORE_DEREF 사용
        /// </summary>
        private void EmitStoreComprehensionVar(string name, List<string> comprehensionVars)
        {
            // 컴프리헨션 변수 목록에 추가 (중복 제거)
            if (!comprehensionVars.Contains(name))
            {
                comprehensionVars.Add(name);
            }

            // CPython 3.12: Cell 변수인지 확인하고 적절한 명령어 사용
            // CPython 3.12: Python/compile.c, Objects/codeobject.c
            // Cell vars use their original varnames index
            if (IsCellVariable(name))
            {
                // Cell 변수일 때는 STORE_DEREF 사용 (varNames에서의 인덱스)
                var varIndex = _varNames.IndexOf(name);
                if (varIndex == -1)
                {
                    varIndex = GetOrAddVarName(name);
                }
                EmitInstruction(ByteCodeOp.STORE_DEREF, varIndex);
                #if DEBUG_LOG
                Console.WriteLine($"    → 컴프리헨션 변수 저장: {name} (STORE_DEREF varIndex {varIndex})");
                #endif
            }
            else
            {
                // 일반 지역 변수일 때는 STORE_FAST 사용
                var varIndex = GetOrAddVarName(name);
                EmitInstruction(ByteCodeOp.STORE_FAST, varIndex);
                #if DEBUG_LOG
                Console.WriteLine($"    → 컴프리헨션 변수 저장: {name} (STORE_FAST index {varIndex})");
                #endif
            }
        }

        /// <summary>
        /// 변수가 cell 변수인지 확인
        /// </summary>
        private bool IsCellVariable(string name)
        {
            return _cellVars.Contains(name);
        }

        /// <summary>
        /// Cell 변수의 인덱스를 가져옴
        /// </summary>
        private int GetCellVariableIndex(string name)
        {
            var index = _cellVars.IndexOf(name);
            if (index < 0)
            {
                throw new ArgumentException($"Variable '{name}' is not a cell variable");
            }
            return index;
        }

        /// <summary>
        /// 컴프리헨션 변수 로드 (CPython 3.12 호환)
        /// </summary>
        private void EmitLoadComprehensionVar(string name, List<string> comprehensionVars)
        {
            if (comprehensionVars.Contains(name))
            {
                // 컴프리헨션 변수: LOAD_FAST 사용
                var varIndex = _varNames.IndexOf(name);
                if (varIndex >= 0)
                {
                    EmitInstruction(ByteCodeOp.LOAD_FAST, varIndex);
                    #if DEBUG_LOG
                    Console.WriteLine($"    → 컴프리헨션 변수 로드: {name} (LOAD_FAST index {varIndex})");
                    #endif
                    return;
                }
            }
            
            // 일반 변수: 기존 로직 사용
            EmitLoadName(name);
        }
        
        /// <summary>
        /// PEP 709 - Dict comprehension 바이트코드 인라인 최적화
        /// {key: value for var in iterable if condition} → 직접 바이트코드 생성
        /// CPython 호환 방식: GET_ITER는 한 번만, 루프는 FOR_ITER부터 시작
        /// Fixed: 중첩 dict comprehension 스택 위치 문제 해결
        /// </summary>
        private void CompileDictComprehension(DictComprehension dictComp)
        {
            #if DEBUG_LOG
            Console.WriteLine("🚀 PEP 709: Dict comprehension 바이트코드 인라인 컴파일 (CPython 3.12 호환)");
            #endif

            // CPython 3.12: 컴프리헨션 컨텍스트 시작
            var savedIsInComprehension = _isInComprehension;
            _isInComprehension = true;

            // CPython 3.12 PEP 709: Inlined comprehensions use their own symbol table for iteration variables,
            // but fall back to enclosing scope for outer variables (handled in EmitLoadName/CompileCallExpression)
            var savedSymbolTable = _currentSymbolTable;
            var compSymbolTable = _symbolTableBuilder.LookupSymbolTable(dictComp);
            if (compSymbolTable != null)
            {
                _currentSymbolTable = compSymbolTable;
                #if DEBUG_LOG
                Console.WriteLine($"🔧 Switched to dict comprehension symbol table: {compSymbolTable.Name}");
                #endif
            }

            // 중첩 깊이 추적 시작
            _comprehensionNestingDepth++;
            #if DEBUG_LOG
            Console.WriteLine($"🔍 Dict comprehension 중첩 깊이 증가: {_comprehensionNestingDepth}");
            #endif

            // CPython 3.12 패턴: 컴프리헨션 변수 사전 할당 및 정리 - List comprehension과 동일
            var comprehensionVars = new List<string>(); // Loop variables (for x in ...)
            var walrusVars = new List<string>(); // Walrus variables (y := ...)

            // Loop 변수 수집 (generator targets)
            foreach (var gen in dictComp.Generators)
            {
                CollectComprehensionVars(gen.Target, comprehensionVars);
            }

            // Walrus 변수 수집 (filter 조건 및 key/value 표현식에서)
            foreach (var gen in dictComp.Generators)
            {
                foreach (var ifExpr in gen.Ifs)
                {
                    CollectAllComprehensionVars(ifExpr, walrusVars);
                }
            }
            CollectAllComprehensionVars(dictComp.Key, walrusVars);
            CollectAllComprehensionVars(dictComp.Value, walrusVars);

            // Walrus 변수에서 loop 변수 제거 (loop 변수는 Local, walrus만 Global)
            for (int i = walrusVars.Count - 1; i >= 0; i--)
            {
                if (comprehensionVars.Contains(walrusVars[i]))
                {
                    walrusVars.RemoveAt(i);
                }
            }

            // 전체 변수 = loop 변수 + walrus 변수 (LOAD_FAST_AND_CLEAR/STORE_FAST에 사용)
            var allVars = new List<string>(comprehensionVars);
            allVars.AddRange(walrusVars);

            #if DEBUG_LOG
            Console.WriteLine($"🔧 Dict comprehension loop vars: {string.Join(", ", comprehensionVars)} (count: {comprehensionVars.Count})");
            Console.WriteLine($"🔧 Dict comprehension walrus vars: {string.Join(", ", walrusVars)} (count: {walrusVars.Count})");
            Console.WriteLine($"🔧 Dict comprehension all vars: {string.Join(", ", allVars)} (count: {allVars.Count})");
            #endif

            // Walrus 변수의 scope를 임시로 Global로 변경 (CPython 3.12 push pattern)
            // 모듈 레벨에서만 적용, 함수 내부에서는 이미 Local scope
            var savedSymbolScopes = new Dictionary<string, SymbolScope>();
            var activeSymbolTable = _currentSymbolTable ?? _symbolTable;
            if (activeSymbolTable != null && activeSymbolTable.GetType() == SymbolTableType.Module)
            {
                foreach (var varName in walrusVars) // walrus 변수만!
                {
                    if (activeSymbolTable.GetSymbols().TryGetValue(varName, out var symbol))
                    {
                        savedSymbolScopes[varName] = symbol.Scope;
                        symbol.Scope = SymbolScope.Global;
                    }
                    else
                    {
                        var newSymbol = new Symbol(varName);
                        newSymbol.Scope = SymbolScope.Global;
                        newSymbol.Flags = SymbolFlags.Assigned;
                        activeSymbolTable.GetSymbols()[varName] = newSymbol;
                        savedSymbolScopes[varName] = SymbolScope.Unknown;
                    }
                }
            }

            // 1. First compile the iterator source (CPython 3.12 pattern)
            var firstGenerator = dictComp.Generators[0];

            // 첫 번째 generator 처리
            bool firstGeneratorOptimized = false;

            if (!firstGeneratorOptimized)
            {
                // 첫 번째 generator가 일반 루프인 경우: GET_ITER 생성
                // Performance: Eliminated LINQ
                bool allElementsConstant = false;
                ListExpression dictIterList = null;
                if (firstGenerator.Iter is ListExpression tmpList)
                {
                    dictIterList = tmpList;
                    allElementsConstant = true;
                    foreach (var e in tmpList.Elements)
                    {
                        if (!(e is ConstantExpression))
                        {
                            allElementsConstant = false;
                            break;
                        }
                    }
                }
                if (allElementsConstant)
                {
                    // 상수 리스트 → 상수 튜플로 변환
                    // Performance: Eliminated LINQ
                    var constantElements = new PyObject[dictIterList.Elements.Count];
                    for (int i = 0; i < dictIterList.Elements.Count; i++)
                    {
                        constantElements[i] = ((ConstantExpression)dictIterList.Elements[i]).Value;
                    }
                    var tupleConstant = new PyTuple(constantElements);
                    EmitLoadConst(tupleConstant);
                }
                else
                {
                    CompileExpression(firstGenerator.Iter);
                }

                EmitInstruction(ByteCodeOp.GET_ITER);
            }

            // 2. LOAD_FAST_AND_CLEAR: 모든 컴프리헨션 변수 초기화 (CPython 3.12 패턴)
            foreach (var varName in allVars)  // allVars = loop vars + walrus vars
            {
                EmitInstruction(ByteCodeOp.LOAD_FAST_AND_CLEAR, GetOrAddVarName(varName));
            }

            // 3. 첫 번째 SWAP: 스택 재배치
            if (allVars.Count > 0)
            {
                int swapArg = firstGeneratorOptimized ? allVars.Count : allVars.Count + 1;
                EmitInstruction(ByteCodeOp.SWAP, swapArg);
            }

            // 4. BUILD_MAP 생성 (Dict comprehension의 핵심 차이점!)
            EmitInstruction(ByteCodeOp.BUILD_MAP, 0); // {} 빈 딕셔너리 생성
            #if DEBUG_LOG
            Console.WriteLine($"🔧 CPython 3.12 BUILD_MAP 0 생성");
            #endif

            // 5. 두 번째 SWAP: 딕셔너리를 올바른 위치로 이동
            EmitInstruction(ByteCodeOp.SWAP, 2);

            // 6. CPython 3.12 재귀 구조 사용 (CFG가 exception table 자동 관리)
            CompileSyncComprehensionGenerator(
                generators: dictComp.Generators,
                genIndex: 0,
                depth: 0,
                elt: dictComp.Key,        // Dict는 key 사용
                val: dictComp.Value,      // Dict는 value도 전달
                type: ComprehensionType.DictComp,
                comprehensionVars: comprehensionVars,
                iterOnStack: true
            );

            // 7. 변수 복원 (CPython 3.12 패턴)
            if (allVars.Count > 0)
            {
                EmitInstruction(ByteCodeOp.SWAP, allVars.Count + 1);

                for (int i = allVars.Count - 1; i >= 0; i--)
                {
                    var varName = allVars[i];
                    EmitInstruction(ByteCodeOp.STORE_FAST, GetOrAddVarName(varName));
                }
            }

            // 8. Walrus 변수의 scope를 원래대로 복원 (CPython 3.12 pop pattern)
            if (_symbolTable != null && savedSymbolScopes.Count > 0)
            {
                foreach (var kvp in savedSymbolScopes)
                {
                    var varName = kvp.Key;
                    var originalScope = kvp.Value;

                    if (originalScope == SymbolScope.Unknown)
                    {
                        _symbolTable.GetSymbols().Remove(varName);
                    }
                    else if (_symbolTable.GetSymbols().TryGetValue(varName, out var symbol))
                    {
                        symbol.Scope = originalScope;
                    }
                }
            }

            // 9. 컨텍스트 종료 (CFG가 exception table 자동 관리)
            _isInComprehension = savedIsInComprehension;
            _currentSymbolTable = savedSymbolTable;
            _comprehensionNestingDepth--;

            #if DEBUG_LOG
            Console.WriteLine($"✅ Dict comprehension 바이트코드 CPython 3.12 호환 완료");
            #endif
        }


        /// <summary>
        /// 중첩 dict comprehension의 깊이 계산
        /// </summary>
        private int GetNestingDepth(DictComprehension dictComp)
        {
            if (dictComp.Value is DictComprehension nestedComp)
            {
                return 1 + GetNestingDepth(nestedComp);
            }
            return 1;
        }

        /// <summary>
        /// 단일 요소 리스트 표현식인지 확인 ([expression] 형태)
        /// </summary>
        private bool IsSingleElementListExpression(Expression expr)
        {
            return expr is ListExpression list && list.Elements.Count == 1;
        }

        /// <summary>
        /// Generator 컨텍스트에서 이터레이터 표현식 컴파일 - BUILD_LIST 최적화 없이
        /// CPython 3.12와 호환성을 위해 중첩된 comprehension에서는 직접 튜플 상수 로드
        /// </summary>
        private void CompileGeneratorIterable(Expression iterExpr)
        {
            switch (iterExpr)
            {
                case ListExpression list:
                    // 리스트 표현식을 튜플 상수로 변환 (BUILD_LIST 생성하지 않음)
                    // Performance: Eliminated LINQ
                    bool allConstant = true;
                    foreach (var e in list.Elements)
                    {
                        if (!(e is ConstantExpression))
                        {
                            allConstant = false;
                            break;
                        }
                    }
                    if (allConstant)
                    {
                        // 모든 요소가 상수인 경우 - 튜플 상수로 직접 로드
                        // Performance: Eliminated LINQ
                        var constantElements = new PyObject[list.Elements.Count];
                        for (int i = 0; i < list.Elements.Count; i++)
                        {
                            constantElements[i] = ((ConstantExpression)list.Elements[i]).Value;
                        }
                        var tupleConstant = new PyTuple(constantElements);
                        EmitLoadConst(tupleConstant);
                    }
                    else
                    {
                        // 비상수 요소가 있는 경우 - 각 요소를 개별적으로 로드 후 BUILD_TUPLE
                        foreach (var element in list.Elements)
                        {
                            CompileExpression(element);
                        }
                        EmitInstruction(ByteCodeOp.BUILD_TUPLE, list.Elements.Count);
                    }
                    break;

                default:
                    // 다른 이터러블은 기본 컴파일 방식 사용
                    CompileExpression(iterExpr);
                    break;
            }
        }





        /// <summary>
        /// CPython 3.12 LIST_APPEND offset 계산 - 단순화된 접근법
        ///
        /// CPython 3.12의 LIST_APPEND는 스택에서 타겟 리스트까지의 거리를 나타냄
        /// 기본 공식: LIST_APPEND (nestingDepth + 1)
        ///
        /// 반환값:
        /// - listAppendArg: LIST_APPEND 명령어의 스택 offset
        /// - useNestedLoops: 항상 true (nested loops 사용)
        /// </summary>
        private (int listAppendArg, bool useNestedLoops) DetectComprehensionPattern(List<Comprehension> generators)
        {
            int generatorCount = generators.Count;
            int nestingDepth = _comprehensionNestingDepth;

            #if DEBUG_LOG
            Console.WriteLine($"🎯 LIST_APPEND offset 계산: generatorCount={generatorCount}, nestingDepth={nestingDepth}");
            #endif

            // CPython 3.12 LIST_APPEND offset: 기본적으로 generator 수 + 1
            // 중첩 리스트 컴프리헨션인 경우 적절히 조정
            int listAppendOffset;

            if (nestingDepth >= 1)
            {
                // 중첩된 리스트 컴프리헨션: LIST_APPEND 전용 계산 로직
                // 스택 구조: [..., [], iterator, dict] → LIST_APPEND는 depth 1의 []를 찾아야 함
                listAppendOffset = generatorCount + 1;
                #if DEBUG_LOG
                Console.WriteLine($"  📋 중첩 리스트 컴프리헨션: nesting={nestingDepth}, generators={generatorCount}, LIST_APPEND {listAppendOffset}");
                #endif
            }
            else
            {
                // 일반적인 경우: generator 수 + 1
                listAppendOffset = generatorCount + 1;
                #if DEBUG_LOG
                Console.WriteLine($"  📊 일반 리스트 컴프리헨션: {generatorCount}개 generator → LIST_APPEND {listAppendOffset}");
                #endif
            }

            // 항상 nested loops 사용 (복잡한 flattened logic 제거)
            return (listAppendOffset, true);
        }


        /// <summary>
        /// PEP 709 - Set comprehension 바이트코드 인라인 최적화
        /// {expr for var in iterable if condition} → 직접 바이트코드 생성
        /// CPython 호환 방식: GET_ITER는 한 번만, 루프는 FOR_ITER부터 시작
        /// </summary>
        private void CompileSetComprehension(SetComprehension setComp)
        {
            #if DEBUG_LOG
            Console.WriteLine("🚀 PEP 709: Set comprehension 바이트코드 인라인 컴파일 (중첩 Generator 지원)");
            #endif

            // CPython 3.12: 컴프리헨션 컨텍스트 시작
            var savedIsInComprehension = _isInComprehension;
            _isInComprehension = true;

            // CPython 3.12 PEP 709: Inlined comprehensions use their own symbol table for iteration variables,
            // but fall back to enclosing scope for outer variables (handled in EmitLoadName/CompileCallExpression)
            var savedSymbolTable = _currentSymbolTable;
            var compSymbolTable = _symbolTableBuilder.LookupSymbolTable(setComp);
            if (compSymbolTable != null)
            {
                _currentSymbolTable = compSymbolTable;
                #if DEBUG_LOG
                Console.WriteLine($"🔧 Switched to set comprehension symbol table: {compSymbolTable.Name}");
                #endif
            }

            // CPython 3.12 패턴: 컴프리헨션 변수 사전 할당 및 정리
            var comprehensionVars = new List<string>(); // Loop variables (for x in ...)
            var walrusVars = new List<string>(); // Walrus variables (y := ...)

            // Loop 변수 수집 (generator targets)
            foreach (var gen in setComp.Generators)
            {
                CollectComprehensionVars(gen.Target, comprehensionVars);
            }

            // Walrus 변수 수집 (filter 조건 및 element 표현식에서)
            foreach (var gen in setComp.Generators)
            {
                foreach (var ifExpr in gen.Ifs)
                {
                    CollectAllComprehensionVars(ifExpr, walrusVars);
                }
            }
            CollectAllComprehensionVars(setComp.Element, walrusVars);

            // Walrus 변수에서 loop 변수 제거 (loop 변수는 Local, walrus만 Global)
            for (int i = walrusVars.Count - 1; i >= 0; i--)
            {
                if (comprehensionVars.Contains(walrusVars[i]))
                {
                    walrusVars.RemoveAt(i);
                }
            }

            // 전체 변수 = loop 변수 + walrus 변수
            var allVars = new List<string>(comprehensionVars);
            allVars.AddRange(walrusVars);

            #if DEBUG_LOG
            Console.WriteLine($"🔧 Set comprehension loop vars: {string.Join(", ", comprehensionVars)} (count: {comprehensionVars.Count})");
            Console.WriteLine($"🔧 Set comprehension walrus vars: {string.Join(", ", walrusVars)} (count: {walrusVars.Count})");
            Console.WriteLine($"🔧 Set comprehension all vars: {string.Join(", ", allVars)} (count: {allVars.Count})");
            #endif

            // Walrus 변수의 scope를 임시로 Global로 변경 (CPython 3.12 push pattern)
            // 모듈 레벨에서만 적용, 함수 내부에서는 이미 Local scope
            var savedSymbolScopes = new Dictionary<string, SymbolScope>();
            var activeSymbolTable = _currentSymbolTable ?? _symbolTable;
            if (activeSymbolTable != null && activeSymbolTable.GetType() == SymbolTableType.Module)
            {
                foreach (var varName in walrusVars) // walrus 변수만!
                {
                    if (activeSymbolTable.GetSymbols().TryGetValue(varName, out var symbol))
                    {
                        savedSymbolScopes[varName] = symbol.Scope;
                        symbol.Scope = SymbolScope.Global;
                    }
                    else
                    {
                        var newSymbol = new Symbol(varName);
                        newSymbol.Scope = SymbolScope.Global;
                        newSymbol.Flags = SymbolFlags.Assigned;
                        activeSymbolTable.GetSymbols()[varName] = newSymbol;
                        savedSymbolScopes[varName] = SymbolScope.Unknown;
                    }
                }
            }

            // CPython 3.12: Set comprehension 스택 준비
            var firstGenerator = setComp.Generators[0];

            // 먼저 첫 번째 generator의 iterable 로드
            CompileExpression(firstGenerator.Iter);
            EmitInstruction(ByteCodeOp.GET_ITER);

            // CPython 3.12: LOAD_FAST_AND_CLEAR (allVars 사용)
            foreach (var varName in allVars)  // allVars = loop vars + walrus vars
            {
                EmitInstruction(ByteCodeOp.LOAD_FAST_AND_CLEAR, GetOrAddVarName(varName));
            }

            // CPython 3.12: 첫 번째 SWAP
            if (allVars.Count > 0)
            {
                int swapArg = allVars.Count + 1;
                #if DEBUG_LOG
                Console.WriteLine($"🔧 CPython 3.12 첫 번째 SWAP: vars={allVars.Count}, swapArg={swapArg}");
                #endif
                EmitInstruction(ByteCodeOp.SWAP, swapArg);
            }

            // 1. 빈 셋 생성
            EmitInstruction(ByteCodeOp.BUILD_SET, 0);
            #if DEBUG_LOG
            Console.WriteLine($"🔧 BUILD_SET created");
            #endif

            // CPython 3.12: 두 번째 SWAP
            if (allVars.Count > 0)
            {
                EmitInstruction(ByteCodeOp.SWAP, 2);
                #if DEBUG_LOG
                Console.WriteLine($"🔧 CPython 3.12 두 번째 SWAP 2");
                #endif
            }

            // CPython 3.12: 재귀 구조 사용 (CFG가 exception table 자동 관리)
            #if DEBUG_LOG
            Console.WriteLine($"🔧 Using CPython 3.12 recursive generator compilation");
            #endif
            CompileSyncComprehensionGenerator(
                generators: setComp.Generators,
                genIndex: 0,
                depth: 0,
                elt: setComp.Element,
                val: null,
                type: ComprehensionType.SetComp,
                comprehensionVars: comprehensionVars,
                iterOnStack: true // 최외곽 iterator는 이미 스택에 있음
            );

            // CPython 3.12: 스택 복원
            if (allVars.Count > 0)
            {
                EmitInstruction(ByteCodeOp.SWAP, allVars.Count + 1);
                for (int i = allVars.Count - 1; i >= 0; i--)
                {
                    EmitInstruction(ByteCodeOp.STORE_FAST, GetOrAddVarName(allVars[i]));
                }
            }

            // Walrus 변수의 scope를 원래대로 복원 (CPython 3.12 pop pattern)
            if (activeSymbolTable != null && savedSymbolScopes.Count > 0)
            {
                foreach (var kvp in savedSymbolScopes)
                {
                    var varName = kvp.Key;
                    var originalScope = kvp.Value;

                    if (originalScope == SymbolScope.Unknown)
                    {
                        activeSymbolTable.GetSymbols().Remove(varName);
                    }
                    else if (activeSymbolTable.GetSymbols().TryGetValue(varName, out var symbol))
                    {
                        symbol.Scope = originalScope;
                    }
                }
            }

            // CPython 3.12: CFG's BuildExceptionTable()이 exception handler를 자동으로 관리

            // CPython 3.12: 컴프리헨션 컨텍스트 종료
            _isInComprehension = savedIsInComprehension;
            _currentSymbolTable = savedSymbolTable;

            #if DEBUG_LOG
            Console.WriteLine($"✅ Set comprehension 바이트코드 인라인 완료 ({setComp.Generators.Count}개 중첩 generator)");
            #endif
        }

        /// <summary>
        /// PEP 709 - Generator expression 바이트코드 인라인 최적화
        /// (expr for var in iterable if condition) → 제너레이터 함수 생성
        /// </summary>
        private void CompileGeneratorExpression(GeneratorExpression genExp)
        {
            #if DEBUG_LOG
            Console.WriteLine("🚀 PEP 709: Generator expression 바이트코드 인라인 컴파일");
            #endif

            // 제너레이터는 별도 함수로 컴파일 필요
            var genCompiler = new PythonCompiler();
            // CPython 3.12: Pass source location information
            genCompiler.SetSourceLocation(_currentFileName, _sourceLines);
            // CPython 3.12: Pass symbol table builder for PySymtable_Lookup
            genCompiler._symbolTableBuilder = _symbolTableBuilder;

            // CPython 3.12: compile.c:5681-5700
            // PySTEntryObject *entry = PySymtable_Lookup(c->c_st, (void *)e);
            // compiler_enter_scope(c, name, COMPILER_SCOPE_COMPREHENSION, ...)
            // This ensures c->u->u_ste is always set before compiling the generator body

            // CPython uses c->c_st (global symbol table) for lookup, but in practice
            // the generator expression symbol table is a child of the current scope
            var searchTable = _currentSymbolTable ?? _symbolTable;

            if (searchTable == null)
            {
                throw new InvalidOperationException(
                    "Symbol table is null when compiling generator expression. " +
                    "CPython guarantees c->c_st is always valid via _PySymtable_Build. " +
                    "This indicates SymbolTableBuilder was not called before compilation.");
            }

            // CPython 3.12: PySymtable_Lookup(c->c_st, (void *)e) - symtable.c:381-400
            // Uses AST node pointer as key to lookup corresponding symbol table entry
            SymbolTable? genSymbolTable = null;
            if (_symbolTableBuilder != null)
            {
#if DEBUG_COMPILER_LOG
                Console.WriteLine($"[COMPILER] PySymtable_Lookup: AST node type={genExp.GetType().Name}, HashCode={genExp.GetHashCode()}, RuntimeHashCode={System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(genExp)}");
#endif
                genSymbolTable = _symbolTableBuilder.LookupSymbolTable(genExp);
#if DEBUG_COMPILER_LOG
                Console.WriteLine($"[COMPILER] PySymtable_Lookup: Result={(genSymbolTable != null ? $"Found ({genSymbolTable.GetName()})" : "NULL")}");
#endif
            }
            else
            {
#if DEBUG_COMPILER_LOG
                Console.WriteLine($"[COMPILER] WARNING: _symbolTableBuilder is NULL in scope '{searchTable.GetName()}'");
#endif
            }

            if (genSymbolTable == null)
            {
                throw new InvalidOperationException(
                    $"Symbol table not found for generator expression in scope '{searchTable.GetName()}'. " +
                    $"CPython guarantees PySymtable_Lookup(c->c_st, (void *)e) always succeeds because " +
                    $"_PySymtable_Build creates all symbol tables upfront and registers them in st->st_blocks. " +
                    $"This indicates SymbolTableBuilder did not register this AST node during AnalyzeComprehension.");
            }

#if DEBUG_COMPILER_LOG
            Console.WriteLine($"[COMPILER] Found symbol table for genexpr: IsGenerator={genSymbolTable.IsGenerator}, IsCoroutine={genSymbolTable.IsCoroutine}");
#endif
            genCompiler.SetSymbolTableContext(genSymbolTable);

            // Pass root symbol table for nested lookups
            genCompiler.SetRootSymbolTable(_symbolTable);

            // 다중 for 루프 지원: 모든 generators 처리
            var outerGenerator = genExp.Generators[0];
            var outerTargetName = outerGenerator.Target is NameExpression nameExpr ? nameExpr.Name : "x";

            // CPython 3.12 패턴: .0 iterator를 받아서 직접 FOR 루프 실행
            var iteratorExpr = new NameExpression(".0"); // .0 매개변수 (이미 iterator)

            // 중첩된 for 루프 생성 (안쪽부터)
            Statement innerMostStatement = new YieldStatement(genExp.Element);

            // 모든 조건문을 수집 (CPython 3.12: 조건문은 전체 expression에 적용)
            var allConditions = new List<Expression>();
            foreach (var generator in genExp.Generators)
            {
                allConditions.AddRange(generator.Ifs);
            }

            // 조건문이 있으면 yield를 if문으로 감싸기
            if (allConditions.Count > 0)
            {
                // 모든 조건을 AND로 연결
                Expression combinedCondition = allConditions[0];
                for (int j = 1; j < allConditions.Count; j++)
                {
                    combinedCondition = new BoolOpExpression(
                        And.Instance,
                        new List<Expression> { combinedCondition, allConditions[j] }
                    );
                }

                innerMostStatement = new IfStatement(
                    combinedCondition,
                    new List<Statement> { innerMostStatement },
                    null
                );
            }

            // 역순으로 for 루프를 중첩 구성 (가장 안쪽부터)
            for (int i = genExp.Generators.Count - 1; i >= 0; i--)
            {
                var generator = genExp.Generators[i];

                // for문의 바디는 현재까지 구성된 innerMostStatement
                var forBody = new List<Statement> { innerMostStatement };

                // 첫 번째 generator는 .0을 사용, 나머지는 각자의 iterable 사용
                Expression iterableExpr = (i == 0) ? iteratorExpr : generator.Iter;

                // CPython 3.12: Target expression을 그대로 사용 (tuple unpacking 지원)
                innerMostStatement = new ForStatement(generator.Target, iterableExpr, forBody);
            }

            // CPython 3.12: 최종 generator statement
            var genStatements = new List<Statement> { innerMostStatement };

            // CPython 3.12: 제너레이터 표현식은 iterator를 .0 매개변수로 받음
            var parameters = new List<string> { ".0" };  // 매개변수는 .0 하나
            var defaults = new List<PyObject>();  // 기본값 없음
            var kwDefaults = new List<PyObject>();  // keyword-only defaults 없음
            var flags = PyCodeObject.CO_GENERATOR;  // CO_GENERATOR 플래그 설정

            // Get free variables and cell variables from the genexpr symbol table
            var genFreeVars = genSymbolTable.FindFreeVariables();
            var genCellVars = genSymbolTable.FindCellVariables();

            // CPython 3.12: Use CompilerFunctionBody (not legacy CompileFunction)
            var genCode = genCompiler.CompilerFunctionBody(
                genStatements, "<genexpr>", parameters,
                defaults, kwDefaults, genFreeVars, genCellVars,
                flags, 1, 0, 0);

            // CPython 3.12: Create closure if there are free variables (compile.c:1797-1849)
            // compiler_make_closure: Only emit LOAD_CLOSURE for variables that are
            // in the current scope's cellvars/freevars (line 1814: get_ref_type)
            int makeFunctionFlags = 0;
            var actualClosureVars = new List<string>();

            if (genFreeVars.Count > 0)
            {
                // CPython 3.12: Check each free variable's reftype in current scope
                foreach (var freeVar in genFreeVars)
                {
                    // Check if this variable is in current scope's cellvars or freevars
                    // If not, it's a module-level global - genexpr will use LOAD_GLOBAL
                    if (_cellVars.Contains(freeVar) || _freeVars.Contains(freeVar))
                    {
                        // This variable is a cell/free var in current scope
                        // Need to pass it as closure to genexpr
                        actualClosureVars.Add(freeVar);
                        EmitLoadClosure(freeVar);
                    }
                    // else: module-level variable - genexpr will LOAD_GLOBAL directly
                }

                // Build tuple of closure cells only if we have actual closure vars
                if (actualClosureVars.Count > 0)
                {
                    EmitInstruction(ByteCodeOp.BUILD_TUPLE, actualClosureVars.Count);
                    makeFunctionFlags |= 0x08;  // Closure flag
                }
            }

            // 제너레이터 함수 객체 생성
            EmitLoadConst(genCode);
            EmitInstruction(ByteCodeOp.MAKE_FUNCTION, makeFunctionFlags);

            // CPython 3.12: 올바른 스택 순서로 호출
            CompileExpression(outerGenerator.Iter);  // range(5) 또는 중첩 genexpr 컴파일

            // CPython 3.12: 중첩된 generator expression의 경우 이미 CALL이 되어 generator 반환됨
            // 그 generator를 GET_ITER로 iterator화 해야 함
            EmitInstruction(ByteCodeOp.GET_ITER);  // iterator 생성 (generator도 iterable이므로 GET_ITER 필요)
            EmitInstruction(ByteCodeOp.CALL, 0);  // 제너레이터 함수 호출 (iterator는 특별 처리)
            
            #if DEBUG_LOG
            Console.WriteLine("✅ Generator expression 바이트코드 인라인 완료");
            #endif
        }
        
        // CPython 3.12: Assignment target compilation
        private void CompileAssignTarget(AssignTargetStatement assignTarget)
        {
#if DEBUG_LOG
            Console.WriteLine($"[DEBUG] CompileAssignTarget: Target type = {assignTarget.Target.GetType().Name}");
            if (assignTarget.Target is AttributeExpression attr)
            {
                Console.WriteLine($"[DEBUG] CompileAssignTarget: AttributeExpression.Value = {attr.Value.GetType().Name}, Attr = {attr.Attr}");
            }
#endif
            // CPython 3.12: Comprehension의 경우 특별 처리
            bool isListComprehension = assignTarget.Value is ListComprehension;
            bool isDictComprehension = assignTarget.Value is DictComprehension;
            bool isSetComprehension = assignTarget.Value is SetComprehension;
            bool isComprehension = isListComprehension || isDictComprehension || isSetComprehension;
            List<string> comprehensionVars = new List<string>();

            if (isComprehension)
            {
                // CPython 3.12: Comprehension 변수들을 재귀적으로 수집 (walrus operator 포함)
                // CollectAllComprehensionVars를 사용하여 filter 조건의 walrus도 수집
                CollectAllComprehensionVars(assignTarget.Value, comprehensionVars);
            }

            // Compile the value first
            CompileExpression(assignTarget.Value);

            // CPython 3.12 패턴: Comprehension cleanup을 assignment 전에 실행
            if (isComprehension && comprehensionVars.Count > 0)
            {
                var compType = isListComprehension ? "List" : isDictComprehension ? "Dict" : "Set";
                #if DEBUG_LOG
                Console.WriteLine($"🔧 CPython 3.12 {compType} comp cleanup: vars={comprehensionVars.Count}");
                #endif

                if (isDictComprehension)
                {
                    // Dict comprehension은 SWAP 3 (dict + 2 variables)
                    EmitInstruction(ByteCodeOp.SWAP, 3);
                }
                else if (isSetComprehension)
                {
                    // Set comprehension은 SWAP 2 (set + 1 variable)
                    EmitInstruction(ByteCodeOp.SWAP, 2);
                }
                else
                {
                    // List comprehension은 SWAP 2
                    EmitInstruction(ByteCodeOp.SWAP, 2);
                }

                foreach (var varName in comprehensionVars)
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"  🔧 STORE_FAST: storing {compType} comprehension var {varName} (CPython 3.12 order)");
                    #endif
                    EmitInstruction(ByteCodeOp.STORE_FAST, GetOrAddVarName(varName));
                }
            }

            // Handle different assignment targets
            switch (assignTarget.Target)
            {
                case NameExpression name:
                    EmitStoreName(name.Name);
                    break;

                case AttributeExpression attrExpr:
                    CompileExpression(attrExpr.Value);
                    EmitStoreAttr(attrExpr.Attr);
                    break;
                    
                case SubscriptExpression subscript:
                    // Check if this is a slice assignment (obj[start:stop] = value)
                    if (subscript.Slice is SliceExpression slice)
                    {
                        // CPython 3.12: Use STORE_SLICE for slice assignments
                        // Stack order: container, start, stop, value -> []
                        CompileExpression(subscript.Value);  // container
                        CompileExpression(slice.Start);      // start 
                        CompileExpression(slice.Stop);       // stop
                        // Value is already on stack from assignment statement
                        EmitInstruction(ByteCodeOp.STORE_SLICE);
                    }
                    else
                    {
                        // Regular subscript assignment (obj[key] = value)
                        CompileExpression(subscript.Value);
                        CompileExpression(subscript.Slice);
                        EmitInstruction(ByteCodeOp.STORE_SUBSCR);
                    }
                    break;
                    
                case TupleExpression tuple:
                    // CPython 3.12: Python/compile.c:4370-4400 (unpack_helper)
                    // Check if this is starred unpacking (contains StarExpression or StarredExpression)
                    var starIndex = tuple.Elements.FindIndex(e => e is StarExpression || e is StarredExpression);
                    if (starIndex >= 0)
                    {
                        // Starred unpacking: first, *middle, last = items
                        // Use UNPACK_EX instead of UNPACK_SEQUENCE
                        var beforeStarCount = starIndex;
                        var afterStarCount = tuple.Elements.Count - starIndex - 1;
                        var arg = beforeStarCount | (afterStarCount << 8);

                        // CPython 3.12: UNPACK_EX argument format
                        // Low byte: number of elements before *
                        // High byte: number of elements after *
                        EmitInstruction(ByteCodeOp.UNPACK_EX, arg);

                        // Store elements in order: before*, starred, after*
                        for (int i = 0; i < tuple.Elements.Count; i++)
                        {
                            var element = tuple.Elements[i];
                            if (element is StarExpression star)
                            {
                                CompileAssignmentTarget(star.Value);
                            }
                            else if (element is StarredExpression starred)
                            {
                                // CPython 3.12: Python/compile.c:4395 - visit starred value
                                CompileAssignmentTarget(starred.Value);
                            }
                            else
                            {
                                CompileAssignmentTarget(element);
                            }
                        }
                    }
                    else
                    {
                        // Regular tuple unpacking: x, y = (1, 2)
                        // Value is already on stack, unpack it
                        EmitInstruction(ByteCodeOp.UNPACK_SEQUENCE, tuple.Elements.Count);
                        
                        // CPython: UNPACK_SEQUENCE pushes elements in reverse order on stack
                        // When we pop for STORE operations, we get them in forward order
                        // So we store in forward order (x first, then y)
                        for (int i = 0; i < tuple.Elements.Count; i++)
                        {
                            var element = tuple.Elements[i];
                            CompileAssignmentTarget(element);
                        }
                    }
                    break;
                    
                default:
                    throw new Exception($"Invalid assignment target: {assignTarget.Target.GetType().Name}");
            }
        }
        
        /// <summary>
        /// Compiles an assignment target expression (used recursively for nested tuple unpacking)
        /// Assumes the value is already on the stack
        /// </summary>
        private void CompileAssignmentTarget(Expression target)
        {
            switch (target)
            {
                case NameExpression name:
                    EmitStoreName(name.Name);
                    break;
                    
                case AttributeExpression attrExpr2:
                    CompileExpression(attrExpr2.Value);
                    EmitStoreAttr(attrExpr2.Attr);
                    break;

                case SubscriptExpression subscript:
                    // Check if this is a slice assignment (obj[start:stop] = value)
                    if (subscript.Slice is SliceExpression slice)
                    {
                        // CPython 3.12: Use STORE_SLICE for slice assignments  
                        // Stack order: container, start, stop, value -> []
                        CompileExpression(subscript.Value);  // container
                        CompileExpression(slice.Start);      // start 
                        CompileExpression(slice.Stop);       // stop
                        // Value is already on stack from previous operations
                        EmitInstruction(ByteCodeOp.STORE_SLICE);
                    }
                    else
                    {
                        // Regular subscript assignment (obj[key] = value)
                        CompileExpression(subscript.Value);
                        CompileExpression(subscript.Slice);
                        EmitInstruction(ByteCodeOp.STORE_SUBSCR);
                    }
                    break;
                    
                case TupleExpression tuple:
                    // CPython 3.12: Python/compile.c:4370-4400 (unpack_helper)
                    // Nested tuple unpacking with possible star expression: (a, *b, c) = (1, 2, 3, 4)
                    var nestedStarIndex = tuple.Elements.FindIndex(e => e is StarExpression || e is StarredExpression);
                    if (nestedStarIndex >= 0)
                    {
                        // Starred unpacking in nested tuple
                        var beforeCount = nestedStarIndex;
                        var afterCount = tuple.Elements.Count - nestedStarIndex - 1;
                        var unpackArg = beforeCount | (afterCount << 8);
                        EmitInstruction(ByteCodeOp.UNPACK_EX, unpackArg);

                        for (int i = 0; i < tuple.Elements.Count; i++)
                        {
                            var element = tuple.Elements[i];
                            if (element is StarExpression nestedStar)
                            {
                                CompileAssignmentTarget(nestedStar.Value);
                            }
                            else if (element is StarredExpression nestedStarred)
                            {
                                CompileAssignmentTarget(nestedStarred.Value);
                            }
                            else
                            {
                                CompileAssignmentTarget(element);
                            }
                        }
                    }
                    else
                    {
                        // Regular nested tuple unpacking: (a, (b, c)) = (1, (2, 3))
                        EmitInstruction(ByteCodeOp.UNPACK_SEQUENCE, tuple.Elements.Count);
                        for (int i = 0; i < tuple.Elements.Count; i++)
                        {
                            var element = tuple.Elements[i];
                            CompileAssignmentTarget(element);
                        }
                    }
                    break;

                case ListExpression list:
                    // CPython 3.12: Python/compile.c:4370-4400 (unpack_helper)
                    // List unpacking with possible star expression: [a, *b] = [1, 2, 3]
                    var listStarIndex = list.Elements.FindIndex(e => e is StarExpression || e is StarredExpression);
                    if (listStarIndex >= 0)
                    {
                        // Starred unpacking
                        var beforeCount = listStarIndex;
                        var afterCount = list.Elements.Count - listStarIndex - 1;
                        var unpackArg = beforeCount | (afterCount << 8);
                        EmitInstruction(ByteCodeOp.UNPACK_EX, unpackArg);

                        for (int i = 0; i < list.Elements.Count; i++)
                        {
                            var element = list.Elements[i];
                            if (element is StarExpression listStar)
                            {
                                CompileAssignmentTarget(listStar.Value);
                            }
                            else if (element is StarredExpression listStarred)
                            {
                                CompileAssignmentTarget(listStarred.Value);
                            }
                            else
                            {
                                CompileAssignmentTarget(element);
                            }
                        }
                    }
                    else
                    {
                        // Regular list unpacking: [a, b] = [1, 2]
                        EmitInstruction(ByteCodeOp.UNPACK_SEQUENCE, list.Elements.Count);
                        for (int i = 0; i < list.Elements.Count; i++)
                        {
                            var element = list.Elements[i];
                            CompileAssignmentTarget(element);
                        }
                    }
                    break;

                default:
                    throw new Exception($"Invalid assignment target expression: {target.GetType().Name}");
            }
        }
        
        /// <summary>
        /// Compiles chained assignment: a = b = c + d
        /// CPython 3.12 compatible implementation using COPY instruction
        /// </summary>
        private void CompileChainedAssign(ChainedAssignStatement chainedAssign)
        {
            // Compile the value expression once
            CompileExpression(chainedAssign.Value);
            
            // For each target except the last, we need to COPY the value
            for (int i = 0; i < chainedAssign.Targets.Count; i++)
            {
                var target = chainedAssign.Targets[i];
                
                // If not the last target, copy the value for the next assignment
                if (i < chainedAssign.Targets.Count - 1)
                {
                    EmitInstruction(ByteCodeOp.COPY, 1);
                }
                
                // Assign to the current target
                CompileAssignmentTarget(target);
            }
        }
        
        private void EmitStoreAttr(string attrName)
        {
            var index = AddName(attrName);
            EmitInstruction(ByteCodeOp.STORE_ATTR, index);
        }
        
        /// <summary>
        /// Emit STORE_DEREF for cell/free variables
        /// CPython 3.12: Emit cell index for cell vars (will be remapped by FixCellOffsets)
        /// Free vars use ncellvars + freeIndex
        /// </summary>
        private void EmitStoreDeref(string varName)
        {
            // CPython 3.12: Cell variables - emit cell index, will be remapped
            var cellIndex = _cellVars.IndexOf(varName);
            if (cellIndex != -1)
            {
                EmitInstruction(ByteCodeOp.STORE_DEREF, cellIndex);
                return;
            }

            // Free variable: offset by ncellvars
            var freeIndex = _freeVars.IndexOf(varName);
            if (freeIndex == -1)
                throw new Exception($"Variable '{varName}' not found in cell or free variables");
            int derefIndex = _cellVars.Count + freeIndex;
            EmitInstruction(ByteCodeOp.STORE_DEREF, derefIndex);
        }
        
        /// <summary>
        /// Emit LOAD_DEREF for cell/free variables
        /// CPython 3.12: Emit cell index for cell vars (will be remapped by FixCellOffsets)
        /// Free vars use ncellvars + freeIndex
        /// </summary>
        private void EmitLoadDeref(string varName)
        {
            // CPython 3.12: Cell variables - emit cell index, will be remapped
            var cellIndex = _cellVars.IndexOf(varName);
            if (cellIndex != -1)
            {
                EmitInstruction(ByteCodeOp.LOAD_DEREF, cellIndex);
                return;
            }

            // Free variable: offset by ncellvars
            var freeIndex = _freeVars.IndexOf(varName);
            if (freeIndex != -1)
            {
                int derefIndex = _cellVars.Count + freeIndex;
                EmitInstruction(ByteCodeOp.LOAD_DEREF, derefIndex);
                return;
            }

            throw new Exception($"Variable '{varName}' not found in cell or free variables");
        }
        
        /// <summary>
        /// Emit LOAD_CLOSURE for creating closure tuples
        /// CPython 3.12: LOAD_CLOSURE uses localsplus offset
        /// For cell vars: emits cell index which FixCellOffsets will remap
        /// For free vars: emits varnames.count + freeIndex (already correct)
        /// </summary>
        private void EmitLoadClosure(string varName)
        {
            #if DEBUG_COMPILER_LOG
            Console.WriteLine($"    → EmitLoadClosure('{varName}') called");
            Console.WriteLine($"       _varNames: [{string.Join(", ", _varNames)}]");
            Console.WriteLine($"       _cellVars: [{string.Join(", ", _cellVars)}]");
            Console.WriteLine($"       _freeVars: [{string.Join(", ", _freeVars)}]");
            #endif

            // CPython 3.12: Cell variables - emit cell index, will be remapped by FixCellOffsets
            var cellIndex = _cellVars.IndexOf(varName);
            if (cellIndex != -1)
            {
                #if DEBUG_COMPILER_LOG
                Console.WriteLine($"    → LOAD_CLOSURE for cell var: {varName} (cellIndex {cellIndex})");
                #endif
                EmitInstruction(ByteCodeOp.LOAD_CLOSURE, cellIndex);
                return;
            }

            // CPython 3.12: Free variables - emit ncellvars + freeIndex
            var freeIndex = _freeVars.IndexOf(varName);
            if (freeIndex != -1)
            {
                int derefIndex = _cellVars.Count + freeIndex;
                #if DEBUG_COMPILER_LOG
                Console.WriteLine($"    → LOAD_CLOSURE for free var: {varName} (freeIndex {freeIndex}, derefIndex {derefIndex})");
                #endif
                EmitInstruction(ByteCodeOp.LOAD_CLOSURE, derefIndex);
                return;
            }

            throw new Exception($"Variable '{varName}' not found in cell or free variables");
        }
        
        /// <summary>
        /// Emit LOAD_CLOSURE for multiple cell variables
        /// </summary>
        private void EmitLoadClosure(List<int> cellIndices)
        {
            foreach (var index in cellIndices)
            {
                EmitInstruction(ByteCodeOp.LOAD_CLOSURE, index);
            }
        }
        
        /// <summary>
        /// Emit COPY_FREE_VARS for functions with free variables
        /// </summary>
        private void EmitCopyFreeVars(int count)
        {
            EmitInstruction(ByteCodeOp.COPY_FREE_VARS, count);
        }
        
        /// <summary>
        /// Emit STORE_FAST for local variables
        /// </summary>
        private void EmitStoreFast(string varName)
        {
            var index = _varNames.IndexOf(varName);
            if (index == -1)
            {
                _varNames.Add(varName);
                index = _varNames.Count - 1;
            }
            EmitInstruction(ByteCodeOp.STORE_FAST, index);
        }
        
        /// <summary>
        /// Emit LOAD_FAST for local variables
        /// </summary>
        private void EmitLoadFast(string varName)
        {
            var index = _varNames.IndexOf(varName);
            if (index == -1)
                throw new Exception($"Variable '{varName}' not found in local variables");
            EmitInstruction(ByteCodeOp.LOAD_FAST, index);
        }
        
        /// <summary>
        /// CPython 3.12: Check if we're compiling at module level
        /// </summary>
        private bool IsModuleLevel()
        {
            return _currentFunctionName == null || _currentFunctionName == "<module>";
        }
        
        
        /// <summary>
        /// Compile slice expression [start:stop] or [start:stop:step]
        /// CPython 3.12: Uses BINARY_SLICE for simple slicing
        /// </summary>
        private void CompileSliceExpression(SliceExpression sliceExp)
        {
            // SliceExpression은 실제로는 SubscriptExpression의 일부로 컴파일됨
            // 하지만 여기서는 직접 슬라이스 객체를 만들어야 할 수도 있음
            
            // Start 값 로드 (None이면 None)
            if (sliceExp.Start != null)
                CompileExpression(sliceExp.Start);
            else
                EmitLoadConst(PyNone.Instance);
                
            // Stop 값 로드 (None이면 None) 
            if (sliceExp.Stop != null)
                CompileExpression(sliceExp.Stop);
            else
                EmitLoadConst(PyNone.Instance);
                
            // Step 값 로드 (기본값은 None)
            if (sliceExp.Step != null)
            {
                CompileExpression(sliceExp.Step);
                // BUILD_SLICE 3 (start, stop, step)
                EmitInstruction(ByteCodeOp.BUILD_SLICE, 3);
            }
            else
            {
                // BUILD_SLICE 2 (start, stop)
                EmitInstruction(ByteCodeOp.BUILD_SLICE, 2);
            }
        }

        /// <summary>
        /// 키워드 표현식 컴파일 (arg=value)
        /// 데코레이터나 함수 호출에서 키워드 인자로 사용됨
        /// </summary>
        private void CompileKeywordExpression(KeywordExpression keyword)
        {
            // 키워드 표현식은 단순히 값 부분만 컴파일
            // 키워드명(Arg)은 호출자에서 별도로 처리함
            CompileExpression(keyword.Value);
        }
        
        #endregion
        
        #region CPython 3.12 Instruction Size Helper Methods
        
        /// <summary>
        /// CPython 3.12 완전 호환 명령어 크기 계산
        /// dis._inline_cache_entries 기반 정확한 인라인 캐시 반영
        /// 참조: https://github.com/python/cpython/blob/3.12/Python/bytecodes.c
        /// 참조: PEP 659 (Specializing Adaptive Interpreter)
        /// </summary>
        
        /// <summary>
        /// CPython 3.12 호환: 최적화 레벨 결정 (항상 활성화)
        /// </summary>
        private OptimizationLevel GetOptimizationLevel()
        {
            // CPython 3.12 호환: Basic 레벨 (ast_opt.c 호환)
            // Standard/TypeAware/Aggressive는 SharpPy 확장 기능
            // 환경변수나 설정에 따라 레벨 조정 가능
            return OptimizationLevel.Basic;
        }

        /// <summary>
        /// CPython 3.12 호환: 간단한 상수 매칭인지 확인
        /// </summary>
        private bool IsSimpleConstantMatch(MatchStatement matchStmt)
        {
            // 2개 케이스: 상수 패턴 + wildcard 패턴, 가드 없음
            return matchStmt.Cases.Count == 2 &&
                   matchStmt.Cases[0].Pattern is ConstantExpression &&
                   matchStmt.Cases[0].Guard == null &&
                   matchStmt.Cases[1].Pattern is NameExpression name && name.Name == "_" &&
                   matchStmt.Cases[1].Guard == null;
        }

        /// <summary>
        /// CPython 3.12 방식: 간단한 상수 매칭 컴파일
        /// </summary>
        private void CompileSimpleConstantMatch(MatchStatement matchStmt)
        {
            // CPython 방식: subject 한 번만 로드, 상수와 직접 비교, 실패시 wildcard 케이스로
            CompileExpression(matchStmt.Subject);

            var constExpr = (ConstantExpression)matchStmt.Cases[0].Pattern;
            CompileExpression(constExpr);
            EmitComparison(CompareOp.EQ);

            var wildcardLabel = _instructionSequence.NewLabel();
            _instructionSequence.AddOpWithLabel(ByteCodeOp.POP_JUMP_IF_FALSE, wildcardLabel, _currentLineNumber, _currentColumnOffset, _currentFileName);

            // 첫 번째 케이스 (상수 매칭) 컴파일
            foreach (var stmt in matchStmt.Cases[0].Body)
            {
                CompileStatement(stmt);
            }
            EmitInstruction(ByteCodeOp.RETURN_CONST, GetOrAddConstant(PyNone.Instance));

            // wildcard 케이스 - CPython과 동일한 구조
            _instructionSequence.UseLabel(wildcardLabel);
            EmitInstruction(ByteCodeOp.NOP); // CPython 호환

            foreach (var stmt in matchStmt.Cases[1].Body)
            {
                CompileStatement(stmt);
            }
            EmitInstruction(ByteCodeOp.RETURN_CONST, GetOrAddConstant(PyNone.Instance));
        }

        #endregion

        #region Nested Tuple Unpacking Helper Methods

        /// <summary>
        /// Recursively compile comprehension target with support for nested tuple unpacking
        /// </summary>
        private void CompileComprehensionTarget(Expression target, List<string> comprehensionVars)
        {
            #if DEBUG_LOG
            Console.WriteLine($"🔧 CompileComprehensionTarget called with: {target.GetType().Name}");
            #endif

            if (target is NameExpression nameExpr)
            {
                #if DEBUG_LOG
                Console.WriteLine($"    → Name: {nameExpr.Name}");
                #endif
                EmitStoreComprehensionVar(nameExpr.Name, comprehensionVars);
            }
            else if (target is TupleExpression tupleExpr)
            {
                // Check for wrapper tuple - single element tuple containing another tuple
                if (tupleExpr.Elements.Count == 1 && tupleExpr.Elements[0] is TupleExpression innerTuple)
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"    → Detected wrapper tuple, unwrapping inner tuple with {innerTuple.Elements.Count} elements");
                    #endif
                    // Skip the wrapper and process the inner tuple directly
                    CompileComprehensionTarget(innerTuple, comprehensionVars);
                    return;
                }

                #if DEBUG_LOG
                Console.WriteLine($"    → Tuple with {tupleExpr.Elements.Count} elements:");
                for (int i = 0; i < tupleExpr.Elements.Count; i++)
                {
                    var elem = tupleExpr.Elements[i];
                    Console.WriteLine($"      [{i}]: {elem.GetType().Name} - {elem}");
                    if (elem is TupleExpression innerTupleDebug)
                    {
                        Console.WriteLine($"          Inner tuple has {innerTupleDebug.Elements.Count} elements");
                    }
                    else if (elem is NameExpression innerName)
                    {
                        Console.WriteLine($"          Inner name: {innerName.Name}");
                    }
                }
                Console.WriteLine($"    → Emitting UNPACK_SEQUENCE {tupleExpr.Elements.Count}");
                #endif

                EmitInstruction(ByteCodeOp.UNPACK_SEQUENCE, tupleExpr.Elements.Count);

                // Process each element - may be names or nested tuples
                foreach (var element in tupleExpr.Elements)
                {
                    // Recursively call this method for each element
                    CompileComprehensionTarget(element, comprehensionVars);
                }
            }
            else
            {
                throw PyRuntimeError.Create($"Unsupported comprehension target type: {target.GetType().Name}");
            }
        }

        /// <summary>
        /// Recursively collect comprehension variable names from nested tuple targets
        /// </summary>
        private void CollectComprehensionVars(Expression target, List<string> comprehensionVars)
        {
            if (target is NameExpression nameExpr)
            {
                if (!comprehensionVars.Contains(nameExpr.Name))
                {
                    comprehensionVars.Add(nameExpr.Name);
                }
            }
            else if (target is TupleExpression tupleExpr)
            {
                foreach (var element in tupleExpr.Elements)
                {
                    CollectComprehensionVars(element, comprehensionVars);
                }
            }
        }

        /// <summary>
        /// CPython 3.12: 모든 nested comprehension의 변수를 수집 (ste_symbols 패턴)
        /// Symbol table 없이 AST를 재귀적으로 순회하여 모든 comprehension 변수 수집
        /// </summary>
        private void CollectAllComprehensionVars(Expression expr, List<string> comprehensionVars)
        {
            switch (expr)
            {
                case ListComprehension listComp:
                    // Nested comprehension: 별도의 scope이므로 재귀 중단
                    // 이 comprehension 내부의 변수는 parent scope와 무관
                    break;

                case SetComprehension setComp:
                    // Nested comprehension: 별도의 scope이므로 재귀 중단
                    break;

                case DictComprehension dictComp:
                    // Nested comprehension: 별도의 scope이므로 재귀 중단
                    break;

                case GeneratorExpression genExpr:
                    // Nested generator: 별도의 scope이므로 재귀 중단
                    break;

                case NamedExpression namedExpr:
                    // CPython 3.12: walrus operator (:=)로 할당된 변수 수집
                    if (namedExpr.Target is NameExpression targetName)
                    {
                        if (!comprehensionVars.Contains(targetName.Name))
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"  📌 CollectAllComprehensionVars: Found walrus variable '{targetName.Name}'");
                            #endif
                            comprehensionVars.Add(targetName.Name);
                        }
                    }
                    // value 표현식에 nested comprehension이 있을 수 있으므로 재귀
                    CollectAllComprehensionVars(namedExpr.Value, comprehensionVars);
                    break;

                // CPython 3.12: 다른 표현식 타입도 재귀적으로 탐색 (walrus가 중첩될 수 있음)
                case CompareExpression compExpr:
                    // CPython 3.12: Comparison expressions (>, <, ==, etc.) contain operands that may have walrus
                    CollectAllComprehensionVars(compExpr.Left, comprehensionVars);
                    foreach (var comp in compExpr.Comparators)
                    {
                        CollectAllComprehensionVars(comp, comprehensionVars);
                    }
                    break;

                case BinaryOpExpression binExpr:
                    CollectAllComprehensionVars(binExpr.Left, comprehensionVars);
                    CollectAllComprehensionVars(binExpr.Right, comprehensionVars);
                    break;

                case BoolOpExpression boolExpr:
                    // CPython 3.12: BoolOp expressions (and/or) can contain walrus operators
                    foreach (var val in boolExpr.Values)
                    {
                        CollectAllComprehensionVars(val, comprehensionVars);
                    }
                    break;

                case UnaryOpExpression unaryExpr:
                    CollectAllComprehensionVars(unaryExpr.Operand, comprehensionVars);
                    break;

                case CallExpression callExpr:
                    CollectAllComprehensionVars(callExpr.Function, comprehensionVars);
                    foreach (var arg in callExpr.Arguments)
                    {
                        CollectAllComprehensionVars(arg, comprehensionVars);
                    }
                    break;

                case ConditionalExpression condExpr:
                    CollectAllComprehensionVars(condExpr.Test, comprehensionVars);
                    CollectAllComprehensionVars(condExpr.Body, comprehensionVars);
                    CollectAllComprehensionVars(condExpr.OrElse, comprehensionVars);
                    break;

                case TupleExpression tupleExpr:
                    foreach (var elem in tupleExpr.Elements)
                    {
                        CollectAllComprehensionVars(elem, comprehensionVars);
                    }
                    break;

                case ListExpression listExpr:
                    foreach (var elem in listExpr.Elements)
                    {
                        CollectAllComprehensionVars(elem, comprehensionVars);
                    }
                    break;

                default:
                    // 다른 표현식 타입은 무시 (ConstantExpression, NameExpression 등)
                    break;
            }
        }

        /// <summary>
        /// CPython 3.12: statement가 return으로 끝나는지 확인 (control flow 분석)
        /// </summary>
        private bool EndsWithReturn(Statement stmt)
        {
            switch (stmt)
            {
                case ReturnStatement:
                    return true;

                case IfStatement ifStmt:
                    // if/elif/else가 모두 return으로 끝나야 함
                    if (ifStmt.Body.Count > 0 && EndsWithReturn(ifStmt.Body[ifStmt.Body.Count - 1]))
                    {
                        // else 절이 없으면 false (if만으로는 모든 경로를 커버하지 못함)
                        if (ifStmt.OrElse == null || ifStmt.OrElse.Count == 0)
                            return false;

                        // else 절도 return으로 끝나야 함
                        return EndsWithReturn(ifStmt.OrElse[ifStmt.OrElse.Count - 1]);
                    }
                    return false;

                case WhileStatement whileStmt:
                    // while은 break로 빠져나올 수 있으므로 항상 false
                    return false;

                case ForStatement forStmt:
                    // for도 break로 빠져나올 수 있으므로 항상 false
                    return false;

                case TryStatement tryStmt:
                    // try/except/finally 모두 분석해야 하지만 복잡하므로 보수적으로 false
                    // CPython도 복잡한 control flow는 보수적으로 처리
                    return false;

                default:
                    return false;
            }
        }

        /// <summary>
        /// CPython 3.12: compiler_pattern_sequence
        /// Compile sequence pattern like [first, *middle, last] or [a, b, c]
        /// </summary>
        private bool CompileSequencePattern(List<Expression> patterns, PatternContext pc)
        {
            int size = patterns.Count;
            int star = -1;
            bool onlyWildcard = true;
            bool starWildcard = false;

            // Find starred pattern (*rest)
            for (int i = 0; i < size; i++)
            {
                var pattern = patterns[i];
                if (IsStarPattern(pattern))
                {
                    if (star >= 0)
                    {
                        throw new InvalidOperationException("multiple starred names in sequence pattern");
                    }
                    starWildcard = IsWildcardStarPattern(pattern);
                    onlyWildcard &= starWildcard;
                    star = i;
                    continue;
                }
                onlyWildcard &= IsWildcardPattern(pattern);
            }

            // CPython: We need to keep the subject on top during the sequence and length checks
            pc.OnTop++;

            // CPython: MATCH_SEQUENCE - Check if subject is a sequence
            _instructionSequence.AddOp(ByteCodeOp.MATCH_SEQUENCE, 0, _currentLineNumber);
            JumpToFailPop(pc, ByteCodeOp.POP_JUMP_IF_FALSE);

            // CPython: GET_LEN + length check
            if (star < 0)
            {
                // No star: len(subject) == size
                _instructionSequence.AddOp(ByteCodeOp.GET_LEN, 0, _currentLineNumber);
                EmitLoadConst(new PyInt(size));
                _instructionSequence.AddOpWithArg(ByteCodeOp.COMPARE_OP, (int)CompareOp.EQ, _currentLineNumber);
                JumpToFailPop(pc, ByteCodeOp.POP_JUMP_IF_FALSE);
            }
            else if (size > 1)
            {
                // Star: len(subject) >= size - 1
                _instructionSequence.AddOp(ByteCodeOp.GET_LEN, 0, _currentLineNumber);
                EmitLoadConst(new PyInt(size - 1));
                _instructionSequence.AddOpWithArg(ByteCodeOp.COMPARE_OP, (int)CompareOp.GE, _currentLineNumber);
                JumpToFailPop(pc, ByteCodeOp.POP_JUMP_IF_FALSE);
            }

            // CPython: Whatever comes next should consume the subject
            pc.OnTop--;

            // Consume subject
            if (onlyWildcard)
            {
                // Patterns like: [] / [_] / [*_] - just pop subject
                _instructionSequence.AddOp(ByteCodeOp.POP_TOP, 0);
            }
            else if (starWildcard)
            {
                // Star is wildcard (*_) - use subscripting for efficiency
                PatternHelperSequenceSubscr(patterns, star, pc);
            }
            else
            {
                // General case - unpack sequence
                PatternHelperSequenceUnpack(patterns, star, pc);
            }

            return true;
        }

        /// <summary>
        /// CPython 3.12: pattern_helper_sequence_unpack (Python/compile.c lines 6715-6731)
        /// Unpack sequence and match each element
        /// </summary>
        private void PatternHelperSequenceUnpack(List<Expression> patterns, int star, PatternContext pc)
        {
            int size = patterns.Count;

            // CPython: pattern_unpack_helper - emit UNPACK_SEQUENCE or UNPACK_EX
            if (star >= 0)
            {
                // UNPACK_EX: arg = before_count + (after_count << 8)
                int beforeCount = star;
                int afterCount = size - star - 1;
                int arg = beforeCount + (afterCount << 8);
                _instructionSequence.AddOpWithArg(ByteCodeOp.UNPACK_EX, arg, _currentLineNumber);
            }
            else
            {
                // UNPACK_SEQUENCE: unpack all elements
                _instructionSequence.AddOpWithArg(ByteCodeOp.UNPACK_SEQUENCE, size, _currentLineNumber);
            }

            // CPython 3.12: We've now got a bunch of new subjects on the stack (line 6721-6722)
            // They need to remain there after each subpattern match:
            pc.OnTop += size;

            // CPython: Match each subpattern
            // Python/compile.c lines 6723-6729:
            // for (Py_ssize_t i = 0; i < size; i++) {
            //     pc->on_top--;
            //     pattern_ty pattern = asdl_seq_GET(patterns, i);
            //     RETURN_IF_ERROR(compiler_pattern_subpattern(c, pattern, pc));
            // }
            for (int i = 0; i < size; i++)
            {
                // One less item to keep track of each time we loop through:
                // Python/compile.c line 6724: pc->on_top--;
                pc.OnTop--;
                var pattern = patterns[i];

                // SharpPy optimization: For simple bindings, emit STORE directly (like mapping pattern)
                // to avoid unnecessary SWAP operations from PatternHelperRotate.
                // CPython calls compiler_pattern_subpattern (line 6726) which may call pattern_helper_store_name
                // and pattern_helper_rotate (Python/compile.c lines 6661-6682), but we optimize simple cases.
                if (pattern is NameExpression nameExpr && nameExpr.Name != "_")
                {
                    // Simple name binding - emit STORE directly
                    EmitStoreVariable(nameExpr.Name);
                }
                else if (pattern is AsPattern asPattern && asPattern.Pattern == null && asPattern.Name != "_")
                {
                    // AsPattern with no inner pattern (MatchAs(name='x', pattern=None))
                    // This is CPython's AST form for simple capture patterns
                    EmitStoreVariable(asPattern.Name);
                }
                else if (IsStarPattern(pattern))
                {
                    // Star pattern (*rest or *middle)
                    // CPython 3.12: Python/compile.c lines 6693-6700
                    // if (elt->kind == MatchStar_kind && !seen_star) {
                    //     ...
                    //     ADDOP_I(c, loc, UNPACK_EX, (i + ((n-i-1) << 8)));
                    //     seen_star = 1;
                    // }
                    // After UNPACK_EX, star value is on stack and needs to be stored.
                    // CPython calls compiler_pattern_subpattern which calls compiler_pattern_star (line 6810-6817)
                    // which calls pattern_helper_store_name for the star name.
                    // SharpPy optimization: For simple star names, emit STORE directly.
                    var starName = GetStarPatternName(pattern);
                    if (starName != null && starName != "_")
                    {
                        EmitStoreVariable(starName);
                    }
                    else
                    {
                        // Star wildcard (*_) - just pop
                        _instructionSequence.AddOp(ByteCodeOp.POP_TOP, 0, _currentLineNumber);
                    }
                }
                else if (pattern is NameExpression wildcardExpr && wildcardExpr.Name == "_")
                {
                    // Wildcard - just pop the value
                    _instructionSequence.AddOp(ByteCodeOp.POP_TOP, 0, _currentLineNumber);
                }
                else if (pattern is AsPattern wildcardAs && wildcardAs.Pattern == null && wildcardAs.Name == "_")
                {
                    // AsPattern wildcard (MatchAs(name='_', pattern=None))
                    _instructionSequence.AddOp(ByteCodeOp.POP_TOP, 0, _currentLineNumber);
                }
                else
                {
                    // Complex pattern - call compiler_pattern_subpattern
                    // Python/compile.c lines 6775-6783:
                    // static int compiler_pattern_subpattern(struct compiler *c,
                    //                             pattern_ty p, pattern_context *pc)
                    // {
                    //     int allow_irrefutable = pc->allow_irrefutable;
                    //     pc->allow_irrefutable = 1;
                    //     RETURN_IF_ERROR(compiler_pattern(c, p, pc));
                    //     pc->allow_irrefutable = allow_irrefutable;
                    //     return SUCCESS;
                    // }
                    int oldAllowIrrefutable = pc.AllowIrrefutable ? 1 : 0;
                    pc.AllowIrrefutable = true;
                    CompilePatternMatchCFG(pattern, pc);
                    pc.AllowIrrefutable = oldAllowIrrefutable != 0;
                }
            }
        }

        /// <summary>
        /// CPython 3.12: pattern_helper_rotate
        /// Rotate stack using SWAP instructions
        /// </summary>
        private void PatternHelperRotate(int count)
        {
            // CPython: while (1 < count) { ADDOP_I(c, loc, SWAP, count--); }
            while (1 < count)
            {
                _instructionSequence.AddOpWithArg(ByteCodeOp.SWAP, count--, _currentLineNumber);
            }
        }

        /// <summary>
        /// CPython 3.12: compiler_pattern_mapping
        /// Compile mapping/dict pattern (e.g., case {}:  or case {"key": value}:)
        /// </summary>
        private bool CompileMappingPattern(DictExpression dictExpr, PatternContext pc)
        {
            int size = dictExpr.Items.Count;

            // CPython: We need to keep the subject on top during the mapping and length checks
            pc.OnTop++;

            // CPython: MATCH_MAPPING - Check if subject is a mapping
            _instructionSequence.AddOp(ByteCodeOp.MATCH_MAPPING, 0, _currentLineNumber);
            JumpToFailPop(pc, ByteCodeOp.POP_JUMP_IF_FALSE);

            if (size == 0)
            {
                // CPython: If the pattern is just "{}", we're done! Pop the subject
                pc.OnTop--;
                _instructionSequence.AddOp(ByteCodeOp.POP_TOP, 0, _currentLineNumber);
                return true;
            }

            // CPython: If the pattern has any keys, perform a length check (len >= size)
            _instructionSequence.AddOp(ByteCodeOp.GET_LEN, 0, _currentLineNumber);
            EmitLoadConst(new PyInt(size));
            _instructionSequence.AddOpWithArg(ByteCodeOp.COMPARE_OP, (int)CompareOp.GE, _currentLineNumber);
            JumpToFailPop(pc, ByteCodeOp.POP_JUMP_IF_FALSE);

            // CPython: Collect all keys into a tuple for MATCH_KEYS
            foreach (var item in dictExpr.Items)
            {
                // Keys must be constants or attribute lookups
                CompileExpression(item.Key);
            }

            // CPython: BUILD_TUPLE with all keys
            _instructionSequence.AddOpWithArg(ByteCodeOp.BUILD_TUPLE, size, _currentLineNumber);

            // CPython: MATCH_KEYS - extracts values for the given keys
            _instructionSequence.AddOp(ByteCodeOp.MATCH_KEYS, 0, _currentLineNumber);

            // CPython: There's now a tuple of keys and a tuple of values on top of the subject
            pc.OnTop += 2;

            // CPython: COPY 1 to get the values tuple, then check if None (missing keys)
            _instructionSequence.AddOpWithArg(ByteCodeOp.COPY, 1, _currentLineNumber);
            JumpToFailPop(pc, ByteCodeOp.POP_JUMP_IF_NONE);

            // CPython: UNPACK_SEQUENCE to get individual values
            _instructionSequence.AddOpWithArg(ByteCodeOp.UNPACK_SEQUENCE, size, _currentLineNumber);
            pc.OnTop += size - 1;

            // CPython: Match each value against the pattern
            for (int i = 0; i < size; i++)
            {
                pc.OnTop--;
                var pattern = dictExpr.Items[i].Value;

                // CPython: For simple variable bindings, store directly from TOS without rotation
                // After UNPACK_SEQUENCE, values are already at the top of stack in correct order
                if (pattern is NameExpression nameExpr && nameExpr.Name != "_")
                {
                    // Simple variable capture - emit STORE instruction directly
                    if (pc.Stores.Contains(nameExpr.Name))
                    {
                        throw new InvalidOperationException($"multiple assignments to name {nameExpr.Name} in pattern");
                    }
                    // Emit STORE instruction directly (value is at TOS)
                    EmitStoreVariable(nameExpr.Name);
                    // Do NOT add to pc.Stores since we already stored it
                }
                else if (pattern is NameExpression wildcardExpr && wildcardExpr.Name == "_")
                {
                    // Wildcard - just pop the value
                    _instructionSequence.AddOp(ByteCodeOp.POP_TOP, 0, _currentLineNumber);
                }
                else
                {
                    // Complex pattern - use full pattern matching
                    if (!CompilePatternMatchCFG(pattern, pc))
                    {
                        return false;
                    }
                }
            }

            // CPython: If we get this far, it's a match! Pop the tuple of keys and subject
            // Note: We decrement pc.OnTop by 2 (keys_tuple + subject), but only POP keys_tuple here
            // The subject will be POPped by CompileMatch (line 7360)
            pc.OnTop -= 2;
            _instructionSequence.AddOp(ByteCodeOp.POP_TOP, 0, _currentLineNumber);  // Tuple of keys
            // Subject is left on stack - will be POPped by CompileMatch

            return true;
        }

        /// <summary>
        /// CPython 3.12: compiler_pattern_mapping (MatchMapping_kind)
        /// Compile mapping pattern from MatchMapping AST node
        /// This is the CORRECT implementation matching CPython's pattern compilation
        /// </summary>
        private bool CompileMappingPattern(MatchMapping matchMap, PatternContext pc)
        {
            int size = matchMap.Keys.Count;

            // CPython: We need to keep the subject on top during the mapping and length checks
            pc.OnTop++;

            // CPython: MATCH_MAPPING - Check if subject is a mapping
            _instructionSequence.AddOp(ByteCodeOp.MATCH_MAPPING, 0, _currentLineNumber);
            JumpToFailPop(pc, ByteCodeOp.POP_JUMP_IF_FALSE);

            if (size == 0)
            {
                // CPython: If the pattern is just "{}", we're done! Pop the subject
                pc.OnTop--;
                _instructionSequence.AddOp(ByteCodeOp.POP_TOP, 0, _currentLineNumber);
                return true;
            }

            // CPython: If the pattern has any keys, perform a length check (len >= size)
            _instructionSequence.AddOp(ByteCodeOp.GET_LEN, 0, _currentLineNumber);
            EmitLoadConst(new PyInt(size));
            _instructionSequence.AddOpWithArg(ByteCodeOp.COMPARE_OP, (int)CompareOp.GE, _currentLineNumber);
            JumpToFailPop(pc, ByteCodeOp.POP_JUMP_IF_FALSE);

            // CPython: Collect all keys into a tuple for MATCH_KEYS
            foreach (var key in matchMap.Keys)
            {
                // Keys must be constants or attribute lookups
                CompileExpression(key);
            }

            // CPython: BUILD_TUPLE with all keys
            _instructionSequence.AddOpWithArg(ByteCodeOp.BUILD_TUPLE, size, _currentLineNumber);

            // CPython: MATCH_KEYS - extracts values for the given keys
            _instructionSequence.AddOp(ByteCodeOp.MATCH_KEYS, 0, _currentLineNumber);

            // CPython: There's now a tuple of keys and a tuple of values on top of the subject
            pc.OnTop += 2;

            // CPython: COPY 1 to get the values tuple, then check if None (missing keys)
            _instructionSequence.AddOpWithArg(ByteCodeOp.COPY, 1, _currentLineNumber);
            JumpToFailPop(pc, ByteCodeOp.POP_JUMP_IF_NONE);

            // CPython: UNPACK_SEQUENCE to get individual values
            // Python/compile.c line 7004: ADDOP_I(c, LOC(p), UNPACK_SEQUENCE, size);
            _instructionSequence.AddOpWithArg(ByteCodeOp.UNPACK_SEQUENCE, size, _currentLineNumber);
            pc.OnTop += size - 1;

            // CPython: Match each value against the pattern
            // Python/compile.c lines 7006-7009
            for (int i = 0; i < size; i++)
            {
                pc.OnTop--;
                var pattern = matchMap.Patterns[i];

                // SharpPy optimization: For simple variable bindings, store directly from TOS
                // without rotation. After UNPACK_SEQUENCE, values are already at top in correct order.
                // CPython calls compiler_pattern_subpattern which may use pattern_helper_store_name
                // and PatternHelperRotate, but for simple names we can optimize by emitting STORE directly.
                if (pattern is NameExpression nameExpr && nameExpr.Name != "_")
                {
                    // Simple variable capture - emit STORE instruction directly
                    if (pc.Stores.Contains(nameExpr.Name))
                    {
                        throw new InvalidOperationException($"multiple assignments to name {nameExpr.Name} in pattern");
                    }
                    // Emit STORE instruction directly (value is at TOS)
                    EmitStoreVariable(nameExpr.Name);
                    // Do NOT add to pc.Stores since we already stored it
                }
                else if (pattern is AsPattern asPattern && asPattern.Pattern == null && asPattern.Name != "_")
                {
                    // AsPattern with no inner pattern (MatchAs(name='x', pattern=None))
                    // This is CPython's AST form for simple capture patterns in mapping values
                    if (pc.Stores.Contains(asPattern.Name))
                    {
                        throw new InvalidOperationException($"multiple assignments to name {asPattern.Name} in pattern");
                    }
                    // Emit STORE instruction directly (value is at TOS)
                    EmitStoreVariable(asPattern.Name);
                    // Do NOT add to pc.Stores since we already stored it
                }
                else if (pattern is NameExpression wildcardExpr && wildcardExpr.Name == "_")
                {
                    // Wildcard - just pop the value
                    _instructionSequence.AddOp(ByteCodeOp.POP_TOP, 0, _currentLineNumber);
                }
                else if (pattern is AsPattern wildcardAs && wildcardAs.Pattern == null && wildcardAs.Name == "_")
                {
                    // AsPattern wildcard (MatchAs(name='_', pattern=None))
                    _instructionSequence.AddOp(ByteCodeOp.POP_TOP, 0, _currentLineNumber);
                }
                else
                {
                    // Complex pattern - use full pattern matching
                    // Python/compile.c line 7008: compiler_pattern_subpattern(c, pattern, pc)
                    if (!CompilePatternMatchCFG(pattern, pc))
                    {
                        return false;
                    }
                }
            }

            // CPython: If we get this far, it's a match! Pop the tuple of keys and subject
            // Note: We decrement pc.OnTop by 2 (keys_tuple + subject), but only POP keys_tuple here
            // The subject will be POPped by CompileMatch (line 7360)
            pc.OnTop -= 2;
            _instructionSequence.AddOp(ByteCodeOp.POP_TOP, 0, _currentLineNumber);  // Tuple of keys
            // Subject is left on stack - will be POPped by CompileMatch

            return true;
        }

        /// <summary>
        /// CPython 3.12: compiler_pattern_class (MatchClass_kind)
        /// Compile class pattern from MatchClass AST node
        /// This is the CORRECT implementation matching CPython's pattern compilation
        /// </summary>
        private bool CompileClassPattern(MatchClass matchCls, PatternContext pc)
        {
            int nargs = matchCls.Patterns.Count;
            int nattrs = matchCls.KwdAttrs.Count;

            // CPython: Compile the class expression
            CompileExpression(matchCls.Cls);

            // CPython: Build tuple of keyword attribute names
            // Performance: Eliminated LINQ
            var attrNames = new PyObject[matchCls.KwdAttrs.Count];
            for (int i = 0; i < matchCls.KwdAttrs.Count; i++)
            {
                attrNames[i] = new PyString(matchCls.KwdAttrs[i]);
            }
            EmitLoadConst(new PyTuple(attrNames));

            // CPython: MATCH_CLASS with nargs (positional count)
            _instructionSequence.AddOpWithArg(ByteCodeOp.MATCH_CLASS, nargs, _currentLineNumber);

            // CPython: COPY 1 to check if result is None
            _instructionSequence.AddOpWithArg(ByteCodeOp.COPY, 1, _currentLineNumber);

            // CPython: Check if None (isinstance failed)
            EmitLoadConst(PyNone.Instance);
            _instructionSequence.AddOpWithArg(ByteCodeOp.IS_OP, 1, _currentLineNumber);

            // CPython: TOS is now a tuple of (nargs + nattrs) attributes (or None)
            pc.OnTop++;
            JumpToFailPop(pc, ByteCodeOp.POP_JUMP_IF_FALSE);

            // CPython: UNPACK_SEQUENCE to get individual attributes
            // Python/compile.c line 6886: ADDOP_I(c, LOC(p), UNPACK_SEQUENCE, nargs + nattrs);
            _instructionSequence.AddOpWithArg(ByteCodeOp.UNPACK_SEQUENCE, nargs + nattrs, _currentLineNumber);
            pc.OnTop += nargs + nattrs - 1;

            // CPython: Match each attribute value against its pattern
            // Python/compile.c lines 6888-6905
            for (int i = 0; i < nargs + nattrs; i++)
            {
                pc.OnTop--;
                Expression pattern;

                if (i < nargs)
                {
                    // Positional: from Patterns list
                    // Python/compile.c line 6893: pattern = asdl_seq_GET(patterns, i);
                    pattern = matchCls.Patterns[i];
                }
                else
                {
                    // Keyword: from KwdPatterns list
                    // Python/compile.c line 6897: pattern = asdl_seq_GET(kwd_patterns, i - nargs);
                    pattern = matchCls.KwdPatterns[i - nargs];
                }

                // CPython: Python/compile.c lines 6899-6902
                // if (WILDCARD_CHECK(pattern)) {
                //     ADDOP(c, LOC(p), POP_TOP);
                //     continue;
                // }
                // SharpPy optimization: For simple bindings, emit STORE directly instead of
                // calling compiler_pattern_subpattern. This avoids unnecessary SWAP operations.
                // CPython's WILDCARD_CHECK includes both wildcard (_) and simple names.
                if (pattern is NameExpression nameExpr && nameExpr.Name != "_")
                {
                    // Simple name binding - emit STORE directly
                    EmitStoreVariable(nameExpr.Name);
                }
                else if (pattern is AsPattern asPattern && asPattern.Pattern == null && asPattern.Name != "_")
                {
                    // AsPattern with no inner pattern (MatchAs(name='x', pattern=None))
                    // This is CPython's AST form for simple capture patterns
                    EmitStoreVariable(asPattern.Name);
                }
                else if (pattern is NameExpression wildcardExpr && wildcardExpr.Name == "_")
                {
                    // Wildcard - POP_TOP (CPython line 6900)
                    _instructionSequence.AddOp(ByteCodeOp.POP_TOP, 0, _currentLineNumber);
                }
                else if (pattern is AsPattern wildcardAs && wildcardAs.Pattern == null && wildcardAs.Name == "_")
                {
                    // AsPattern wildcard (MatchAs(name='_', pattern=None))
                    _instructionSequence.AddOp(ByteCodeOp.POP_TOP, 0, _currentLineNumber);
                }
                else
                {
                    // Complex pattern - use full pattern matching
                    // Python/compile.c line 6903: compiler_pattern_subpattern(c, pattern, pc)
                    if (!CompilePatternMatchCFG(pattern, pc))
                    {
                        return false;
                    }
                }
            }

            // Success! The tuple has been consumed
            return true;
        }

        /// <summary>
        /// CPython 3.12: compiler_pattern_class
        /// Compile class pattern (e.g., case Point(x=0, y=0):)
        /// </summary>
        private bool CompileClassPattern(CallExpression callExpr, PatternContext pc)
        {
            int nargs = callExpr.Arguments.Count;
            int nattrs = callExpr.Keywords.Count;

            // CPython: Compile the class expression
            CompileExpression(callExpr.Function);

            // CPython: Build tuple of keyword attribute names
            // Performance: Eliminated LINQ
            var attrNames = new PyObject[callExpr.Keywords.Count];
            for (int i = 0; i < callExpr.Keywords.Count; i++)
            {
                attrNames[i] = new PyString(callExpr.Keywords[i].Arg ?? "");
            }
            EmitLoadConst(new PyTuple(attrNames));

            // CPython: MATCH_CLASS with nargs (positional count)
            _instructionSequence.AddOpWithArg(ByteCodeOp.MATCH_CLASS, nargs, _currentLineNumber);

            // CPython: COPY 1 to check if result is None
            _instructionSequence.AddOpWithArg(ByteCodeOp.COPY, 1, _currentLineNumber);

            // CPython: Check if None (isinstance failed)
            EmitLoadConst(PyNone.Instance);
            _instructionSequence.AddOpWithArg(ByteCodeOp.IS_OP, 1, _currentLineNumber);

            // CPython: TOS is now a tuple of (nargs + nattrs) attributes (or None)
            pc.OnTop++;
            JumpToFailPop(pc, ByteCodeOp.POP_JUMP_IF_FALSE);

            // CPython: UNPACK_SEQUENCE to get individual attributes
            _instructionSequence.AddOpWithArg(ByteCodeOp.UNPACK_SEQUENCE, nargs + nattrs, _currentLineNumber);
            pc.OnTop += nargs + nattrs - 1;

            // CPython: Match each attribute value against its pattern
            for (int i = 0; i < nargs + nattrs; i++)
            {
                pc.OnTop--;
                Expression pattern;

                if (i < nargs)
                {
                    // Positional: from Arguments list
                    pattern = callExpr.Arguments[i];
                }
                else
                {
                    // Keyword: from Keywords list
                    pattern = callExpr.Keywords[i - nargs].Value;
                }

                // SharpPy optimization: For simple bindings, emit STORE directly
                // (same as MatchClass version above)
                if (pattern is NameExpression nameExpr && nameExpr.Name != "_")
                {
                    EmitStoreVariable(nameExpr.Name);
                }
                else if (pattern is AsPattern asPattern && asPattern.Pattern == null && asPattern.Name != "_")
                {
                    // AsPattern with no inner pattern (MatchAs(name='x', pattern=None))
                    EmitStoreVariable(asPattern.Name);
                }
                else if (pattern is NameExpression wildcardExpr && wildcardExpr.Name == "_")
                {
                    _instructionSequence.AddOp(ByteCodeOp.POP_TOP, 0, _currentLineNumber);
                }
                else if (pattern is AsPattern wildcardAs && wildcardAs.Pattern == null && wildcardAs.Name == "_")
                {
                    // AsPattern wildcard (MatchAs(name='_', pattern=None))
                    _instructionSequence.AddOp(ByteCodeOp.POP_TOP, 0, _currentLineNumber);
                }
                else
                {
                    // Complex pattern - use full pattern matching
                    if (!CompilePatternMatchCFG(pattern, pc))
                    {
                        return false;
                    }
                }
            }

            // Success! The tuple has been consumed
            return true;
        }

        /// <summary>
        /// CPython 3.12: compiler_pattern_as
        /// Compile AS pattern (e.g., case [x, y] as point:)
        /// Also handles capture patterns (just a name) and wildcard (_)
        /// </summary>
        // CPython 3.12: Python/compile.c lines 6786-6809
        private bool CompileAsPattern(AsPattern asPattern, PatternContext pc)
        {
            // CPython 3.12: MatchAs has two forms:
            // 1. MatchAs(pattern, name) - pattern as name (e.g., [x, y] as point)
            // 2. MatchAs(null, name) - capture pattern (e.g., x) or wildcard (_)

            if (asPattern.Pattern != null)
            {
                // CPython: Need to make a copy for storing later
                // pc->on_top++; (line 6802)
                // ADDOP_I(c, LOC(p), COPY, 1); (line 6803)
                pc.OnTop++;
                _instructionSequence.AddOpWithArg(ByteCodeOp.COPY, 1, _currentLineNumber);

                // CPython: RETURN_IF_ERROR(compiler_pattern(c, p->v.MatchAs.pattern, pc)); (line 6804)
                if (!CompilePatternMatchCFG(asPattern.Pattern, pc))
                {
                    return false;
                }

                // CPython: Success! Store it: (line 6805-6807)
                // pc->on_top--;
                // RETURN_IF_ERROR(pattern_helper_store_name(c, LOC(p), p->v.MatchAs.name, pc));
                pc.OnTop--;
                return PatternHelperStoreName(asPattern.Name, pc);
            }
            else
            {
                // CPython: An irrefutable match (line 6789-6799)
                // return pattern_helper_store_name(c, LOC(p), p->v.MatchAs.name, pc);
                return PatternHelperStoreName(asPattern.Name, pc);
            }
        }

        /// <summary>
        /// CPython 3.12: pattern_helper_store_name (Python/compile.c lines 6661-6682)
        /// Stores pattern variable name in pc->stores list for later emission
        /// Does NOT emit STORE instruction - that happens in compiler_match_inner
        /// </summary>
        private bool PatternHelperStoreName(string name, PatternContext pc)
        {
            // CPython: if (n == NULL) { ADDOP(c, loc, POP_TOP); return SUCCESS; } (line 6664-6666)
            if (name == null || name == "_")
            {
                _instructionSequence.AddOp(ByteCodeOp.POP_TOP, 0, _currentLineNumber);
                return true;
            }

            // CPython: Can't assign to the same name twice (line 6671-6676)
            if (pc.Stores.Contains(name))
            {
                throw new InvalidOperationException($"multiple assignments to name {name} in pattern");
            }

            // CPython: Rotate this object underneath any items we need to preserve (line 6677-6679)
            // Py_ssize_t rotations = pc->on_top + PyList_GET_SIZE(pc->stores) + 1;
            int rotations = pc.OnTop + pc.Stores.Count + 1;
            PatternHelperRotate(rotations);

            // CPython: RETURN_IF_ERROR(PyList_Append(pc->stores, n)); (line 6680)
            pc.Stores.Add(name);
            return true;
        }

        /// <summary>
        /// CPython 3.12: compiler_pattern_or
        /// Compile OR pattern (e.g., case 1 | 2 | 3:)
        /// </summary>
        private bool CompileOrPattern(OrPattern orPat, PatternContext pc)
        {
            // CPython: NEW_JUMP_TARGET_LABEL(c, end);
            var endLabel = _instructionSequence.NewLabel();
            int size = orPat.Patterns.Count;

            // CPython: Keep original pc info
            var oldPc = new PatternContext
            {
                Stores = new List<string>(pc.Stores),
                OnTop = pc.OnTop,
                FailPop = new Dictionary<int, SharpPy.Label>(pc.FailPop),
                AllowIrrefutable = pc.AllowIrrefutable
            };

            List<string> control = null;

            // CPython: for (i = 0; i < size; i++)
            for (int i = 0; i < size; i++)
            {
                var alt = orPat.Patterns[i];

                // CPython: Create new stores for this alternative
                pc.Stores = new List<string>();
                pc.AllowIrrefutable = (i == size - 1) && oldPc.AllowIrrefutable;
                pc.FailPop.Clear();
                pc.OnTop = 0;

                // CPython: COPY 1 to preserve subject for next alternative
                _instructionSequence.AddOpWithArg(ByteCodeOp.COPY, 1, _currentLineNumber);

                // CPython: compiler_pattern(c, alt, pc)
                if (!CompilePatternMatchCFG(alt, pc))
                {
                    return false;
                }

                // CPython: Success! Check stores
                int nstores = pc.Stores.Count;

                if (i == 0)
                {
                    // First alternative - save stores as control
                    control = new List<string>(pc.Stores);
                }
                else if (nstores != control.Count)
                {
                    // Different number of captures
                    throw new InvalidOperationException($"alternative patterns bind different names");
                }
                else if (nstores > 0)
                {
                    // Check if stores match control (same names, possibly different order)
                    for (int icontrol = nstores - 1; icontrol >= 0; icontrol--)
                    {
                        string name = control[icontrol];
                        int istores = pc.Stores.IndexOf(name);

                        if (istores < 0)
                        {
                            throw new InvalidOperationException($"alternative patterns bind different names");
                        }

                        if (icontrol != istores)
                        {
                            // Need to reorder - perform rotation
                            // CPython: rotations = istores + 1
                            int rotations = istores + 1;

                            // Reorder pc.Stores to match control
                            var rotated = pc.Stores.GetRange(0, rotations);
                            pc.Stores.RemoveRange(0, rotations);
                            pc.Stores.InsertRange(icontrol - istores, rotated);

                            // Rotate stack to match
                            for (int r = 0; r < rotations; r++)
                            {
                                PatternHelperRotate(icontrol + 1);
                            }
                        }
                    }
                }

                // CPython: JUMP to end on success
                _instructionSequence.AddOpWithLabel(ByteCodeOp.JUMP, endLabel, _currentLineNumber);

                // CPython: emit_and_reset_fail_pop
                EmitAndResetFailPop(pc, oldPc.FailPop[0]);
            }

            // CPython: Restore original pc and pop subject copy
            pc.Stores = oldPc.Stores;
            pc.OnTop = oldPc.OnTop;
            pc.FailPop = oldPc.FailPop;
            pc.AllowIrrefutable = oldPc.AllowIrrefutable;

            // CPython: No match - POP_TOP the remaining copy
            _instructionSequence.AddOp(ByteCodeOp.POP_TOP, 0, _currentLineNumber);

            // CPython: jump_to_fail_pop(c, LOC(p), pc, JUMP)
            JumpToFailPop(pc, ByteCodeOp.JUMP);

            // CPython: USE_LABEL(c, end)
            _instructionSequence.UseLabel(endLabel);

            // CPython 3.12: Python/compile.c line 7183
            // Pop the copy of the subject after successful OR match
            // ADDOP(c, LOC(p), POP_TOP);
            _instructionSequence.AddOp(ByteCodeOp.POP_TOP, 0, _currentLineNumber);

            // CPython: Stores from control need to be moved to the right position
            int controlStores = control?.Count ?? 0;
            for (int i = 0; i < controlStores; i++)
            {
                pc.Stores.Add(control[i]);
            }

            return true;
        }

        /// <summary>
        /// CPython 3.12: jump_to_fail_pop
        /// Jump to the appropriate fail_pop label based on how many items need to be popped
        /// </summary>
        private void JumpToFailPop(PatternContext pc, ByteCodeOp jumpOp)
        {
            // Pop any items on the top of the stack, plus any objects we were going to capture on success
            int pops = pc.OnTop + pc.Stores.Count;

            // Ensure we have a label for this number of pops
            if (!pc.FailPop.ContainsKey(pops))
            {
                pc.FailPop[pops] = _instructionSequence.NewLabel();
            }

            _instructionSequence.AddOpWithLabel(jumpOp, pc.FailPop[pops], _currentLineNumber);
        }

        /// <summary>
        /// CPython 3.12: emit_and_reset_fail_pop
        /// Build all of the fail_pop blocks and reset fail_pop
        /// Generates a chain of POP_TOP instructions for stack cleanup
        /// CPython: while (--pc->fail_pop_size) { USE_LABEL; ADDOP(POP_TOP); }
        ///          USE_LABEL(pc->fail_pop[0]);
        /// </summary>
        private void EmitAndResetFailPop(PatternContext pc, SharpPy.Label finalFailLabel)
        {
            if (pc.FailPop.Count == 0)
                return;

            // CPython: Emit from highest index down to 1, each with POP_TOP
            // Then emit fail_pop[0] without POP_TOP (it's the final fail target)
            // Performance: Eliminated LINQ
            var sortedPops = new List<int>(pc.FailPop.Keys);
            sortedPops.Sort((a, b) => b.CompareTo(a)); // Descending order

            foreach (int pops in sortedPops)
            {
                _instructionSequence.UseLabel(pc.FailPop[pops]);
                if (pops > 0)
                {
                    // fail_pop[N] (N>0): emit ONE POP_TOP, then fall through
                    // CPython does --pc->fail_pop_size in loop, so each iteration pops once
                    _instructionSequence.AddOp(ByteCodeOp.POP_TOP, 0, _currentLineNumber);
                }
            }

            // No jump needed - fail_pop[0] falls through naturally to finalFailLabel
            // which is placed by the caller (CompileMatch)
            pc.FailPop.Clear();
        }

        /// <summary>
        /// CPython 3.12: pattern_helper_sequence_subscr (Python/compile.c lines 6737-6771)
        /// Use BINARY_SUBSCR for patterns with starred wildcard like [first, *_, last]
        /// </summary>
        private void PatternHelperSequenceSubscr(List<Expression> patterns, int star, PatternContext pc)
        {
            // CPython 3.12: We need to keep the subject around for extracting elements (line 6741-6742)
            pc.OnTop++;

            int size = patterns.Count;

            for (int i = 0; i < size; i++)
            {
                var pattern = patterns[i];

                // Skip wildcards
                if (IsWildcardPattern(pattern))
                {
                    continue;
                }

                // Skip star wildcard
                if (i == star && IsWildcardStarPattern(pattern))
                {
                    continue;
                }

                // COPY 1 - keep subject on stack
                _instructionSequence.AddOpWithArg(ByteCodeOp.COPY, 1, _currentLineNumber);

                // Load index
                if (i < star)
                {
                    // Positive index
                    EmitLoadConst(new PyInt(i));
                }
                else
                {
                    // Negative index: GET_LEN, load (size - i), subtract
                    _instructionSequence.AddOp(ByteCodeOp.GET_LEN, 0, _currentLineNumber);
                    EmitLoadConst(new PyInt(size - i));
                    _instructionSequence.AddOpWithArg(ByteCodeOp.BINARY_OP, (int)BinaryOpType.SUBTRACT, _currentLineNumber);
                }

                // BINARY_SUBSCR
                _instructionSequence.AddOp(ByteCodeOp.BINARY_SUBSCR, 0, _currentLineNumber);

                // CPython 3.12: compiler_pattern_subpattern (lines 6765)
                // This will call PatternHelperStoreName which handles SWAP and stores
                int oldAllowIrrefutable = pc.AllowIrrefutable ? 1 : 0;
                pc.AllowIrrefutable = true;
                if (!CompilePatternMatchCFG(pattern, pc))
                {
                    throw new InvalidOperationException($"Failed to compile pattern at index {i}");
                }
                pc.AllowIrrefutable = oldAllowIrrefutable != 0;
            }

            // CPython 3.12: Pop the subject, we're done with it (lines 6767-6769)
            pc.OnTop--;
            _instructionSequence.AddOp(ByteCodeOp.POP_TOP, 0, _currentLineNumber);
        }

        /// <summary>
        /// Check if pattern is star pattern (*rest)
        /// CPython: pattern->kind == MatchStar_kind
        /// </summary>
        private bool IsStarPattern(Expression pattern)
        {
            // StarExpression and StarPattern represent *rest in pattern matching
            return pattern is StarExpression or StarPattern;
        }

        /// <summary>
        /// Check if pattern is wildcard star (*_)
        /// CPython: WILDCARD_STAR_CHECK(pattern)
        /// </summary>
        private bool IsWildcardStarPattern(Expression pattern)
        {
            if (pattern is StarExpression starExpr)
            {
                return starExpr.Value is NameExpression nameExpr && nameExpr.Name == "_";
            }
            if (pattern is StarPattern starPat)
            {
                return starPat.Name == "_";
            }
            return false;
        }

        /// <summary>
        /// Get the name from a star pattern (*name)
        /// CPython 3.12: Python/compile.c lines 6810-6817
        /// static int compiler_pattern_star(struct compiler *c, pattern_ty p, pattern_context *pc)
        /// {
        ///     assert(p->kind == MatchStar_kind);
        ///     RETURN_IF_ERROR(
        ///         pattern_helper_store_name(c, LOC(p), p->v.MatchStar.name, pc));
        ///     return SUCCESS;
        /// }
        /// MatchStar has a 'name' field (can be NULL for wildcard *_)
        /// </summary>
        private string GetStarPatternName(Expression pattern)
        {
            // Extract name from star pattern
            // CPython: p->v.MatchStar.name (Python/compile.c line 6815)
            if (pattern is StarExpression starExpr)
            {
                if (starExpr.Value is NameExpression nameExpr)
                {
                    return nameExpr.Name;
                }
            }
            else if (pattern is StarPattern starPat)
            {
                return starPat.Name;
            }
            return null;
        }

        #endregion
    }
    #endregion
}