using System.Linq;

namespace SharpPy
{
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
        
        /// <summary>
        /// Analyze function/lambda for free variables
        /// </summary>
        public (List<string> freeVars, List<string> cellVars) AnalyzeScope(Expression body, List<string> parameters)
        {
            _definedVars.Clear();
            _usedVars.Clear();
            _parameters.Clear();
            
            // Parameters are always defined in local scope
            foreach (var param in parameters)
            {
                _parameters.Add(param);
                _definedVars.Add(param);
            }
            
            // Analyze the body expression
            AnalyzeExpression(body);
            
            // Free variables: used but not defined locally
            var freeVars = _usedVars.Except(_definedVars).ToList();
            
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
            var hasSuperCalls = HasSuperCalls(func.Body);
            if (hasSuperCalls && !_usedVars.Contains("__class__"))
            {
                Console.WriteLine($"  🔍 Found super() call in {func.Name}, adding __class__ as free variable");
                _usedVars.Add("__class__");
            }
            
            // Free variables: used but not defined locally AND exist in outer scope (CPython 3.12 방식)
            // Only variables that exist in the outer scope can be free variables
            var freeVars = _usedVars.Except(_definedVars)
                                   .Where(var => outerVarNames.Contains(var))
                                   .ToList();
            
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
            var freeVars = _usedVars.Where(var => !_definedVars.Contains(var))
                                   .Where(var => outerVarNames.Contains(var)) // 외부 스코프의 모든 변수
                                   .ToList();
            
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
                    _definedVars.Add(assignStmt.VariableName); // 새로 정의된 변수
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
                    
                // TODO: 다른 statement 타입들 추가 가능
            }
        }
        
        private void AnalyzeExpression(Expression expr)
        {
            switch (expr)
            {
                case NameExpression name:
                    _usedVars.Add(name.Name);
                    break;
                    
                case BinaryOpExpression binary:
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
                    
                case BinaryOpExpression binary:
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
        
        /// <summary>
        /// CPython 3.12: Check if function body contains super() calls
        /// </summary>
        private bool HasSuperCalls(List<Statement> statements)
        {
            foreach (var stmt in statements)
            {
                if (HasSuperCallsInStatement(stmt))
                    return true;
            }
            return false;
        }
        
        private bool HasSuperCallsInStatement(Statement stmt)
        {
            switch (stmt)
            {
                case ReturnStatement returnStmt:
                    if (returnStmt.Value != null)
                        return HasSuperCallsInExpression(returnStmt.Value);
                    return false;
                    
                case ExpressionStatement exprStmt:
                    return HasSuperCallsInExpression(exprStmt.Expression);
                    
                case AssignStatement assignStmt:
                    return HasSuperCallsInExpression(assignStmt.Value);
                    
                default:
                    return false;
            }
        }
        
        private bool HasSuperCallsInExpression(Expression expr)
        {
            switch (expr)
            {
                case CallExpression call:
                    // Check if this is a super() call
                    if (call.Function is NameExpression name && name.Name == "super" && call.Arguments.Count == 0)
                        return true;
                    
                    // Check for super().method() pattern
                    foreach (var arg in call.Arguments)
                    {
                        if (HasSuperCallsInExpression(arg))
                            return true;
                    }
                    return HasSuperCallsInExpression(call.Function);
                    
                case AttributeExpression attr:
                    return HasSuperCallsInExpression(attr.Value);
                    
                case BinaryOpExpression binary:
                    return HasSuperCallsInExpression(binary.Left) || 
                           HasSuperCallsInExpression(binary.Right);
                    
                default:
                    return false;
            }
        }
    }

    // AST를 바이트코드로 컴파일 (기존 시스템과 연동)
    public class PythonCompiler
    {
        
        private bool _enable_optimizer { 
            get {
                bool result = !SharpPyConfig.DisableOptimizer;
                Console.WriteLine($"🔧 _enable_optimizer: {result} (DisableOptimizer: {SharpPyConfig.DisableOptimizer})");
                return result;
            }
        }   // CPython 3.12 compatibility with 2-byte addressing
        private List<ByteCodeInstruction> _instructions;
        private List<PyObject> _constants;
        private List<string> _names;
        private List<string> _varNames;
        private bool _isInFunction = false; // Track if we're compiling inside a function
        private string _currentFunctionName = null; // Track current function name for module level detection
        private bool _isInComprehension = false; // Track if we're compiling inside a comprehension
        
        // Source location tracking for bytecode generation
        private int _currentLineNumber = -1;     // Current line number being compiled
        private int _currentColumnOffset = -1;   // Current column offset being compiled
        private string? _currentFileName = null;  // Current source file name
        private List<string>? _sourceLines = null; // Source code lines for error reporting
        private Dictionary<int, int> _lineNumberTable = new Dictionary<int, int>(); // instruction offset → line number mapping
        
        // Phase 2: 클로저 지원
        private List<string> _cellVars = new List<string>();
        private List<string> _freeVars = new List<string>();
        private List<ExceptionTableEntry> _exceptionTable = new List<ExceptionTableEntry>(); // CPython 3.12 Exception Table
        
        // CPython 3.12: 지연된 exception handler 생성 시스템
        private List<PendingExceptionHandler> _pendingExceptionHandlers = new List<PendingExceptionHandler>();
        
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
                Console.WriteLine($"🔍 Pre-scan found global variables: {string.Join(", ", _moduleGlobalVars)}");
            }
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
        
        public PyCodeObject Compile(List<Statement> statements, string name = "<module>")
        {
            return Compile(statements, name, new List<string>());
        }
        
        public PyCodeObject Compile(List<Statement> statements, string name, List<string> parameters, string? fileName = null)
        {
            _instructions = new List<ByteCodeInstruction>();
            _constants = new List<PyObject>();
            _names = new List<string>();
            _varNames = new List<string>();
            _exceptionTable = new List<ExceptionTableEntry>(); // Reset Exception Table
            _lineNumberTable = new Dictionary<int, int>(); // Reset line number table
            
            // Set current file name for source location tracking
            _currentFileName = fileName;
            
            // Phase 1: AST 수준 최적화 (CPython 3.12 스타일)
            var optimizedStatements = statements;
            if (!SharpPyConfig.DisableOptimizer)
            {
                var astOptimizer = new PyASTOptimizer(GetOptimizationLevel());
                optimizedStatements = astOptimizer.OptimizeAST(statements);
            }
            
            // Pre-scan for global variables in functions (CPython 3.12 compatibility)
            if (name == "<module>")
            {
                _moduleGlobalVars.Clear(); // 새로운 모듈 컴파일 시작
                PreScanForGlobalVariables(optimizedStatements);
            }
            
            // Load source lines for error reporting if fileName is provided
            _sourceLines = null;
            if (!string.IsNullOrEmpty(fileName) && File.Exists(fileName))
            {
                try
                {
                    _sourceLines = File.ReadAllLines(fileName).ToList();
                }
                catch (Exception ex)
                {
                    // If we can't read the file, just continue without source lines
                    Console.WriteLine($"Warning: Could not read source file {fileName}: {ex.Message}");
                }
            }
            
            // 함수 매개변수를 _varNames에 추가 (LOAD_FAST/STORE_FAST용)
            foreach (var param in parameters)
            {
                _varNames.Add(param);
            }
            
            if (!SharpPyConfig.DisassemblyOnlyMode)
            {
                Console.WriteLine($"\n🔧 컴파일: {name}");
            }
            
            // Python 3.12: 모든 코드는 RESUME으로 시작 (line 0)
            _currentLineNumber = 0;
            EmitInstruction(ByteCodeOp.RESUME, 0);
            
            foreach (var statement in optimizedStatements)
            {
                CompileStatement(statement);
            }
            
            // 모듈은 None 반환
            EmitLoadConst(PyNone.Instance);
            EmitInstruction(ByteCodeOp.RETURN_VALUE);
            
            var codeObject = new PyCodeObject(name, _instructions, _constants, _names, _varNames, parameters.Count, null, null, null, 0, _currentFileName, _sourceLines, false, _lineNumberTable);
            
            // Resolve Exception Table labels to offsets (CPython 3.12 compatible)
            ResolveExceptionTable();
            
            // Add Exception Table entries (CPython 3.12 compatible)
            if (_exceptionTable.Count > 0)
            {
                codeObject.ExceptionTable.AddRange(_exceptionTable);
                if (!SharpPyConfig.DisassemblyOnlyMode)
                {
                    Console.WriteLine($"📋 Exception Table: {_exceptionTable.Count}개 엔트리 추가됨 (라벨 해석 완료)");
                }
            }
            else
            {
                if (!SharpPyConfig.DisassemblyOnlyMode)
                {
                    Console.WriteLine($"📋 Exception Table: 비어있음 (CPython 3.12 compatible)");
                }
            }
            if (!SharpPyConfig.DisassemblyOnlyMode)
            {
                if (!SharpPyConfig.DisassemblyOnlyMode)
            {
                Console.WriteLine($"✅ 컴파일 완료: {_instructions.Count}개 명령어");
            }
            }
            
            // 바이트코드 최적화 적용
            var optimizer = new ByteCodeOptimizer(_enable_optimizer);
            var optimizedCode = optimizer.OptimizeCode(codeObject);
            
            return optimizedCode;
        }
        
        /// <summary>
        /// Phase 2: 클로저 정보를 포함한 컴파일
        /// </summary>
        public PyCodeObject CompileWithClosure(List<Statement> statements, string name, List<string> parameters,
                                             List<string> freeVars, List<string> cellVars)
        {
            _instructions = new List<ByteCodeInstruction>();
            _constants = new List<PyObject>();
            _names = new List<string>();
            _varNames = new List<string>();
            _exceptionTable = new List<ExceptionTableEntry>(); // Reset Exception Table
            
            // 함수 매개변수를 _varNames에 추가 (LOAD_FAST/STORE_FAST용)
            foreach (var param in parameters)
            {
                _varNames.Add(param);
            }
            
            if (!SharpPyConfig.DisassemblyOnlyMode)
            {
                Console.WriteLine($"\n🔧 컴파일 (클로저): {name}");
            }
            if (!SharpPyConfig.DisassemblyOnlyMode)
            {
                Console.WriteLine($"  FreeVars: [{string.Join(", ", freeVars)}]");
            }
            if (!SharpPyConfig.DisassemblyOnlyMode)
            {
                Console.WriteLine($"  CellVars: [{string.Join(", ", cellVars)}]");
            }
            
            // Phase 2: Cell 변수들을 위한 MAKE_CELL 명령어 발행
            foreach (var cellVar in cellVars)
            {
                var paramIndex = parameters.IndexOf(cellVar);
                if (paramIndex >= 0)
                {
                    if (!SharpPyConfig.DisassemblyOnlyMode)
                    {
                        Console.WriteLine($"  → Making cell for parameter: {cellVar}");
                    }
                    EmitInstruction(ByteCodeOp.MAKE_CELL, paramIndex);
                }
            }
            
            foreach (var statement in statements)
            {
                CompileStatement(statement);
            }
            
            // 모듈은 None 반환
            EmitLoadConst(PyNone.Instance);
            EmitInstruction(ByteCodeOp.RETURN_VALUE);
            
            var codeObject = new PyCodeObject(name, _instructions, _constants, _names, _varNames, 
                                            parameters.Count, freeVars, cellVars, null, 0, _currentFileName, _sourceLines);
            
            // Add Exception Table entries (CPython 3.12)
            codeObject.ExceptionTable.AddRange(_exceptionTable);
            if (!SharpPyConfig.DisassemblyOnlyMode)
            {
                Console.WriteLine($"\u2705 컴파일 완료: {_instructions.Count}개 명령어");
            }
            
            // 바이트코드 최적화 적용
            var optimizer = new ByteCodeOptimizer(_enable_optimizer);
            var optimizedCode = optimizer.OptimizeCode(codeObject);
            
            return optimizedCode;
        }
        
        /// <summary>
        /// CPython 호환: 매개변수 문자열에서 이름과 기본값 분리
        /// </summary>
        private (List<string> paramNames, List<PyObject> defaults, int flags) ParseFunctionParameters(List<string> parameters)
        {
            var paramNames = new List<string>();
            var defaults = new List<PyObject>();
            int flags = 0;
            
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
                    
                    // 타입 주석 제거: name:type -> name  
                    if (nameTypePart.Contains(":"))
                    {
                        cleanName = nameTypePart.Substring(0, nameTypePart.IndexOf(':')).Trim();
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
                    // 타입 주석만 있는 경우: name:type
                    cleanName = param.Substring(0, param.IndexOf(':')).Trim();
                }
                else
                {
                    // 단순 매개변수 이름
                    cleanName = param.Trim();
                }
                
                // CPython 방식: **kwargs 및 *args 플래그 설정
                if (cleanName.StartsWith("**"))
                {
                    flags |= PyCodeObject.CO_VARKEYWORDS;
                    cleanName = cleanName.Substring(2); // ** 제거
                }
                else if (cleanName.StartsWith("*"))
                {
                    flags |= PyCodeObject.CO_VARARGS;
                    cleanName = cleanName.Substring(1); // * 제거
                }
                
                paramNames.Add(cleanName);
                // CPython 방식: null이 아닌 기본값만 defaults 리스트에 추가
                if (defaultValue != null)
                {
                    defaults.Add(defaultValue);
                }
            }
            
            return (paramNames, defaults, flags);
        }
        
        /// <summary>
        /// Async function 매개변수 파싱 - 일반 함수와 동일한 로직
        /// </summary>
        private (List<string> paramNames, List<PyObject> defaults, int flags) ParseAsyncFunctionParameters(List<string> parameters)
        {
            return ParseFunctionParameters(parameters); // 동일한 로직 재사용
        }
        
        /// <summary>
        /// Async function body 컴파일 - CO_COROUTINE 플래그 추가
        /// </summary>
        private PyCodeObject CompileAsyncFunctionBody(AsyncFunctionDefStatement asyncFunc, List<string> freeVars, List<string> cellVars)
        {
            var (paramNames, defaults, flags) = ParseAsyncFunctionParameters(asyncFunc.Parameters);
            
            // CO_COROUTINE 플래그 추가
            flags |= PyCodeObject.CO_COROUTINE;
            
            var compiler = new PythonCompiler();
            compiler.SetupClosureCompilation(cellVars, freeVars);
            var codeObject = compiler.CompileWithClosureAndDefaults(asyncFunc.Body, asyncFunc.Name, paramNames, defaults, freeVars, cellVars, flags);
            
            // yield가 있는 async 함수는 async generator
            if (codeObject.IsGenerator())
            {
                // CO_ASYNC_GENERATOR 플래그 추가 및 CO_GENERATOR 제거
                var newFlags = codeObject.Flags | PyCodeObject.CO_ASYNC_GENERATOR;
                newFlags &= ~PyCodeObject.CO_GENERATOR; // CO_GENERATOR 플래그 제거
                
                // 새로운 플래그로 코드 객체 재생성
                codeObject = new PyCodeObject(
                    codeObject.Name,
                    codeObject.Instructions,
                    codeObject.Constants,
                    codeObject.Names,
                    codeObject.VarNames,
                    codeObject.ArgCount,
                    newFlags,
                    codeObject.FileName,
                    codeObject.FreeVars,
                    codeObject.CellVars,
                    codeObject.ExceptionTable
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
                Console.WriteLine($"⚠️ Warning: Complex default value '{defaultValueStr}' not yet supported");
            }
            return PyNone.Instance;
        }
        
        /// <summary>
        /// CPython 호환: 클로저와 기본값을 모두 지원하는 컴파일
        /// </summary>
        public PyCodeObject CompileWithClosureAndDefaults(List<Statement> statements, string name, List<string> paramNames, List<PyObject> defaults, List<string> freeVars, List<string> cellVars, int flags = 0)
        {
            _instructions = new List<ByteCodeInstruction>();
            _constants = new List<PyObject>();
            _names = new List<string>();
            _varNames = new List<string>();
            _exceptionTable = new List<ExceptionTableEntry>(); // Reset Exception Table
            _isInFunction = true; // We are now compiling inside a function
            _currentFunctionName = name; // Track function name for module level detection
            
            // 함수 매개변수를 _varNames에 추가 (LOAD_FAST/STORE_FAST용)
            foreach (var param in paramNames)
            {
                // **kwargs 매개변수는 변수명에서 ** 제거 (예: **kwargs -> kwargs)
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
            
            // **핵심 수정**: 함수 본문에서 지역 변수들도 수집해서 _varNames에 추가
            var localVarNames = new List<string>(paramNames);
            CollectLocalVariables(statements, localVarNames);
            
            // 매개변수가 아닌 지역 변수들을 _varNames에 추가
            foreach (var localVar in localVarNames)
            {
                if (!_varNames.Contains(localVar))
                {
                    _varNames.Add(localVar);
                }
            }
            
            Console.WriteLine($"\n🔧 컴파일 (클로저+기본값): {name}");
            Console.WriteLine($"  매개변수: [{string.Join(", ", paramNames)}]");
            Console.WriteLine($"  기본값: [{string.Join(", ", defaults.Select(d => d?.ToString() ?? "None"))}]");
            if (!SharpPyConfig.DisassemblyOnlyMode)
            {
                Console.WriteLine($"  FreeVars: [{string.Join(", ", freeVars)}]");
            }
            if (!SharpPyConfig.DisassemblyOnlyMode)
            {
                Console.WriteLine($"  CellVars: [{string.Join(", ", cellVars)}]");
            }
            
            // CPython 3.12: COPY_FREE_VARS for functions with free variables (MUST be first instruction)
            if (freeVars.Count > 0)
            {
                if (!SharpPyConfig.DisassemblyOnlyMode)
                {
                    Console.WriteLine($"  → Emitting COPY_FREE_VARS for {freeVars.Count} free variables");
                }
                EmitCopyFreeVars(freeVars.Count);
            }

            // Phase 2: Cell 변수들을 위한 MAKE_CELL 명령어 발행 (CPython 3.12 호환)
            foreach (var cellVar in cellVars)
            {
                var varIndex = _varNames.IndexOf(cellVar);
                if (varIndex >= 0)
                {
                    if (!SharpPyConfig.DisassemblyOnlyMode)
                    {
                        Console.WriteLine($"  → Making cell for variable: {cellVar} (var index {varIndex})");
                    }
                    EmitInstruction(ByteCodeOp.MAKE_CELL, varIndex);
                }
                else
                {
                    if (!SharpPyConfig.DisassemblyOnlyMode)
                    {
                        Console.WriteLine($"  ⚠️ Warning: Cell variable {cellVar} not found in _varNames");
                    }
                }
            }
            
            foreach (var statement in statements)
            {
                CompileStatement(statement);
            }
            
            // 함수는 None 반환
            EmitLoadConst(PyNone.Instance);
            EmitInstruction(ByteCodeOp.RETURN_VALUE);
            
            var codeObject = new PyCodeObject(name, _instructions, _constants, _names, _varNames, 
                                            paramNames.Count, freeVars, cellVars, defaults, flags, _currentFileName, _sourceLines);
            
            // Resolve Exception Table labels to offsets (CPython 3.12 compatible)
            ResolveExceptionTable();
            
            // Add Exception Table entries (CPython 3.12 compatible)
            if (_exceptionTable.Count > 0)
            {
                codeObject.ExceptionTable.AddRange(_exceptionTable);
                if (!SharpPyConfig.DisassemblyOnlyMode)
                {
                    Console.WriteLine($"📋 Exception Table: {_exceptionTable.Count}개 엔트리 추가됨 (라벨 해석 완료)");
                }
            }
            else
            {
                if (!SharpPyConfig.DisassemblyOnlyMode)
                {
                    Console.WriteLine($"📋 Exception Table: 비어있음 (CPython 3.12 compatible)");
                }
            }
            if (!SharpPyConfig.DisassemblyOnlyMode)
            {
                if (!SharpPyConfig.DisassemblyOnlyMode)
            {
                Console.WriteLine($"✅ 컴파일 완료: {_instructions.Count}개 명령어");
            }
            }
            
            // CPython 3.12: 지연된 exception handler들을 바이트코드 끝에 생성
            GeneratePendingExceptionHandlers();
            
            // 바이트코드 최적화 적용
            Console.WriteLine($"🔧 메인 컴파일러에서 최적화 호출: _enable_optimizer={_enable_optimizer}");
            var optimizer = new ByteCodeOptimizer(_enable_optimizer);
            var optimizedCode = optimizer.OptimizeCode(codeObject);
            
            _isInFunction = false; // Reset function context
            _currentFunctionName = null; // Reset function name
            return optimizedCode;
        }
        
        /// <summary>
        /// CPython 3.12: 지연된 exception handler들을 바이트코드 끝에 생성
        /// </summary>
        private void GeneratePendingExceptionHandlers()
        {
            foreach (var handler in _pendingExceptionHandlers)
            {
                // Exception handler를 바이트코드 끝에 생성
                var handlerStart = _instructions.Count * 2; // 바이트 오프셋
                
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
                
                // Exception table entry 생성
                var exceptionEntry = new ExceptionTableEntry(
                    start: handler.StartOffset,
                    end: handler.EndOffset,
                    handler: handlerStart,
                    depth: handler.Depth
                );
                _exceptionTable.Add(exceptionEntry);
            }
            
            // 사용 완료된 pending handler들 정리
            _pendingExceptionHandlers.Clear();
        }
        
        /// <summary>
        /// CPython 호환: 함수를 매개변수 기본값과 함께 컴파일
        /// </summary>
        public PyCodeObject CompileFunction(List<Statement> statements, string name, List<string> paramNames, List<PyObject> defaults, int flags = 0)
        {
            _instructions = new List<ByteCodeInstruction>();
            _constants = new List<PyObject>();
            _names = new List<string>();
            _varNames = new List<string>();
            _exceptionTable = new List<ExceptionTableEntry>(); // Reset Exception Table
            _isInFunction = true; // We are now compiling inside a function
            _currentFunctionName = name; // Track function name for module level detection
            
            // 함수 매개변수를 _varNames에 추가
            foreach (var param in paramNames)
            {
                _varNames.Add(param);
            }
            
            Console.WriteLine($"\n🔧 컴파일 함수: {name}");
            Console.WriteLine($"  매개변수: [{string.Join(", ", paramNames)}]");
            Console.WriteLine($"  기본값: [{string.Join(", ", defaults.Select(d => d?.ToString() ?? "None"))}]");
            
            // 함수 본문 컴파일
            foreach (var statement in statements)
            {
                CompileStatement(statement);
            }
            
            // 함수는 None 반환 (return문이 없을 경우)
            EmitLoadConst(PyNone.Instance);
            EmitInstruction(ByteCodeOp.RETURN_VALUE);
            
            // CPython 3.12: Generator 함수 감지 - 임시 객체로 체크
            var tempCodeObject = new PyCodeObject(name, _instructions, _constants, _names, _varNames, 
                                                paramNames.Count, null, null, defaults, flags, _currentFileName, _sourceLines);
            
            // Generator 함수 감지 및 수정
            if (tempCodeObject.IsGenerator())
            {
                Console.WriteLine($"🔍 Generator 함수 감지: {name}, RETURN_GENERATOR 추가");
                
                // RETURN_GENERATOR를 첫 번째 명령어로 삽입
                _instructions.Insert(0, new ByteCodeInstruction(ByteCodeOp.RETURN_GENERATOR, 0));
                _instructions.Insert(1, new ByteCodeInstruction(ByteCodeOp.POP_TOP, 0));
                
                // CO_GENERATOR 플래그 추가
                flags |= PyCodeObject.CO_GENERATOR;
                Console.WriteLine($"✅ Generator 함수 설정 완료: CO_GENERATOR 플래그 추가");
            }
            
            // 최종 PyCodeObject 생성 (수정된 flags 포함)
            var codeObject = new PyCodeObject(name, _instructions, _constants, _names, _varNames, 
                                            paramNames.Count, null, null, defaults, flags, _currentFileName, _sourceLines);
            
            // Add Exception Table entries (CPython 3.12)
            codeObject.ExceptionTable.AddRange(_exceptionTable);
            if (!SharpPyConfig.DisassemblyOnlyMode)
            {
                Console.WriteLine($"✅ 함수 컴파일 완료: {_instructions.Count}개 명령어");
            }
            
            _isInFunction = false; // Reset function context
            _currentFunctionName = null; // Reset function name
            return codeObject;
        }
        
        /// <summary>
        /// Phase 2: 컴파일러에 클로저 정보 설정
        /// </summary>
        public void SetupClosureCompilation(List<string> cellVars, List<string> freeVars = null)
        {
            _cellVars = cellVars ?? new List<string>();
            _freeVars = freeVars ?? new List<string>();
        }
        
        private void CompileStatement(Statement statement)
        {
            // Update current source location
            UpdateSourceLocation(statement);
            
            switch (statement)
            {
                case AssignStatement assign:
                    CompileExpression(assign.Value);
                    EmitStoreName(assign.VariableName);
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
                    EmitStoreName(walrus.Target);         // 저장
                    break;
                    
                case ExpressionStatement expr:
                    CompileExpression(expr.Expression);
                    EmitInstruction(ByteCodeOp.POP_TOP);
                    break;
                    
                case ReturnStatement ret:
                    if (ret.Value != null)
                        CompileExpression(ret.Value);
                    else
                        EmitLoadConst(PyNone.Instance);
                    EmitInstruction(ByteCodeOp.RETURN_VALUE);
                    break;
                    
                case YieldStatement yield:
                    if (yield.Value != null)
                        CompileExpression(yield.Value);
                    else
                        EmitLoadConst(PyNone.Instance);
                    EmitInstruction(ByteCodeOp.YIELD_VALUE, 1); // CPython 3.12: yield_value argument 1
                    EmitInstruction(ByteCodeOp.RESUME, 1); // CPython 3.12: Resume after yield
                    EmitInstruction(ByteCodeOp.POP_TOP); // CPython 3.12: POP_TOP after resume
                    break;
                    
                case YieldFromStatement yieldFrom:
                    CompileExpression(yieldFrom.Value);
                    EmitInstruction(ByteCodeOp.GET_ITER);
                    // CPython 3.12: YIELD_FROM removed, use yield loop pattern
                    EmitInstruction(ByteCodeOp.YIELD_VALUE);
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
                    CompileTry(tryStmt);
                    break;
                    
                case WithStatement withStmt:
                    CompileWith(withStmt);
                    break;
                    
                case MatchStatement matchStmt:
                    CompileMatch(matchStmt);
                    break;
                    
                case BreakStatement:
                    // CPython 3.12: BREAK_LOOP removed, use JUMP_FORWARD to loop end
                    if (_loopStack.Count > 0)
                    {
                        var currentLoop = _loopStack.Peek();
                        EmitJumpToLabel(ByteCodeOp.JUMP_FORWARD, currentLoop.BreakLabel);
                    }
                    else
                    {
                        EmitInstruction(ByteCodeOp.JUMP_FORWARD, 0); // Fallback - will be patched
                    }
                    break;
                    
                case ContinueStatement:
                    // CPython 3.12: CONTINUE_LOOP removed, use JUMP_BACKWARD to loop start  
                    if (_loopStack.Count > 0)
                    {
                        var currentLoop = _loopStack.Peek();
                        EmitJumpToLabel(ByteCodeOp.JUMP_BACKWARD, currentLoop.ContinueLabel);
                    }
                    else
                    {
                        EmitInstruction(ByteCodeOp.JUMP_BACKWARD, 0); // Fallback - will be patched
                    }
                    break;
                    
                case PassStatement:
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
                    // Store to variable
                    EmitStoreName(walrus.Target);
                    // Value remains on stack as return value
                    break;
                    
                case BinaryOpExpression binOp:
                    CompileExpression(binOp.Left);
                    CompileExpression(binOp.Right);
                    EmitBinaryOp(binOp.Operator);
                    break;
                    
                case UnaryOpExpression unaryOp:
                    CompileExpression(unaryOp.Operand);
                    EmitUnaryOp(unaryOp.Op);
                    break;
                    
                case CompareExpression compare:
                    CompileExpression(compare.Left);
                    CompileExpression(compare.Right);
                    EmitCompareOp(compare.Op);
                    break;
                    
                case BoolOpExpression boolOp:
                    CompileBoolOp(boolOp);
                    break;
                    
                case CallExpression call:
                    // CPython 3.12: PUSH_NULL을 먼저
                    EmitInstruction(ByteCodeOp.PUSH_NULL);
                    
                    CompileExpression(call.Function);
                    
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
                        var kwNames = call.Keywords.Select(kw => new PyString(kw.Arg ?? "")).ToArray();
                        var kwNamesTuple = new PyTuple(kwNames);
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
                    break;
                    
                case AttributeExpression attr:
                    // CPython 3.12: Check for super() calls and use LOAD_SUPER_ATTR
                    if (attr.Value is CallExpression superCall && 
                        superCall.Function is NameExpression superName && 
                        superName.Name == "super" && 
                        superCall.Arguments.Count == 0)
                    {
                        // super().method pattern: use LOAD_SUPER_ATTR
                        Console.WriteLine($"🔍 Detected super().{attr.Attr} - generating LOAD_DEREF + LOAD_SUPER_ATTR");
                        
                        // Load __class__ cell variable using LOAD_DEREF
                        var classIndex = _cellVars.IndexOf("__class__");
                        if (classIndex >= 0)
                        {
                            // Load super() (null + self)
                            var superNameIndex = GetOrAddName("super");
                            EmitInstruction(ByteCodeOp.LOAD_GLOBAL, superNameIndex);
                            EmitInstruction(ByteCodeOp.LOAD_DEREF, classIndex);
                            
                            // Load self - assume this is in first parameter (cls/self)
                            EmitInstruction(ByteCodeOp.LOAD_FAST, 0);
                            
                            // Call super with __class__ and self
                            var attrNameIndex = GetOrAddName(attr.Attr);
                            EmitInstruction(ByteCodeOp.LOAD_SUPER_ATTR, attrNameIndex);
                        }
                        else
                        {
                            // Fallback to regular attribute access if no __class__ cell
                            Console.WriteLine("⚠️  No __class__ cell variable found, falling back to LOAD_ATTR");
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
                    
                    // CPython 3.12: SliceExpression은 BINARY_SLICE 사용
                    if (subscript.Slice is SliceExpression sliceExpr)
                    {
                        // Start 값 로드 (None이면 0)
                        if (sliceExpr.Start != null)
                            CompileExpression(sliceExpr.Start);
                        else
                            EmitLoadConst(PyNone.Instance);
                            
                        // Stop 값 로드 (None이면 len)
                        if (sliceExpr.Stop != null)
                            CompileExpression(sliceExpr.Stop);
                        else
                            EmitLoadConst(PyNone.Instance);
                            
                        EmitInstruction(ByteCodeOp.BINARY_SLICE);
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
                    if (list.Elements.Count > 0 && list.Elements.All(e => e is ConstantExpression))
                    {
                        // All elements are constants, use LIST_EXTEND optimization
                        EmitInstruction(ByteCodeOp.BUILD_LIST, 0); // Empty list
                        
                        // Create tuple constant from all elements
                        var constantElements = list.Elements.Cast<ConstantExpression>()
                                                           .Select(c => c.Value)
                                                           .ToArray();
                        var tupleConstant = new PyTuple(constantElements);
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
                    foreach (var element in tuple.Elements)
                    {
                        CompileExpression(element);
                    }
                    EmitInstruction(ByteCodeOp.BUILD_TUPLE, tuple.Elements.Count);
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
                    
                case StarredExpression starred:
                    CompileExpression(starred.Value);
                    // 별표 처리는 문맥에 따라 다름
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
                    
                default:
                    throw PyNotImplementedError.Create($"Expression {expression.GetType().Name} not implemented");
            }
        }
        
        private void CompileFunction(FunctionDefStatement func)
        {
            // 모든 함수를 CompileNestedFunction으로 바이패스 (Phase 2 수정)
            CompileNestedFunction(func);
        }
        
        /// <summary>
        /// Phase 2: 중청 함수 컴파일 (자유 변수 지원)
        /// </summary>
        private void CompileNestedFunction(FunctionDefStatement func)
        {
            Console.WriteLine($"\n🔍 Compiling nested function: {func.Name}");
            
            // PEP 695: Check if function has type parameters
            if (func.TypeParams != null && func.TypeParams.Count > 0)
            {
                Console.WriteLine($"  → PEP 695 Generic function with type parameters: [{string.Join(", ", func.TypeParams)}]");
                CompileGenericFunction(func);
                return;
            }
            
            // 1. 자유 변수 분석
            var analyzer = new FreeVariableAnalyzer();
            Console.WriteLine($"  DEBUG: Current _varNames: [{string.Join(", ", _varNames)}]");
            var (freeVars, cellVars) = analyzer.AnalyzeNestedFunction(func, _varNames);
            
            Console.WriteLine($"  Free variables: [{string.Join(", ", freeVars)}]");
            Console.WriteLine($"  Cell variables: [{string.Join(", ", cellVars)}]");
            
            // CPython 3.12: 내부 함수의 nonlocal 선언을 고려한 추가 cell 분석
            var additionalCellVars = new List<string>(cellVars);
            var allFreeVars = new HashSet<string>();
            
            // 함수 본문에서 지역 변수들을 먼저 수집
            var localVarNames = new List<string>(func.Parameters);
            CollectLocalVariables(func.Body, localVarNames);
            
            Console.WriteLine($"  Local variables found: [{string.Join(", ", localVarNames)}]");
            
            // 내부 함수들의 nonlocal 선언을 기반으로 cell 변수 추가 분석
            AnalyzeFunctionClosures(func.Body, localVarNames, allFreeVars, additionalCellVars);
            
            // 업데이트된 cell 변수들 사용
            cellVars = additionalCellVars;
            Console.WriteLine($"  Updated Cell variables: [{string.Join(", ", cellVars)}]");
            
            // 2. 매개변수와 기본값 파싱 (FunctionDefStatement에서 수행하던 로직)
            var (paramNames, defaults, flags) = ParseFunctionParameters(func.Parameters);
            
            // 3. 코드 객체 컴파일 (자유 변수 정보와 기본값 포함)
            var compiler = new PythonCompiler();
            compiler.SetupClosureCompilation(cellVars, freeVars); // 셀 변수와 자유 변수 설정
            var funcCode = compiler.CompileWithClosureAndDefaults(func.Body, func.Name, paramNames, defaults, freeVars, cellVars, flags);
            
            // 3. 자유 변수가 있는 경우 클로저 생성
            if (freeVars.Count > 0)
            {
                Console.WriteLine($"  → Creating closure for {freeVars.Count} free variables");
                
                // 각 자유 변수에 대해 LOAD_CLOSURE 발행
                foreach (var freeVar in freeVars)
                {
                    // 자유 변수가 현재 스코프의 cell 변수인지 확인
                    var cellIndex = _cellVars.IndexOf(freeVar);
                    if (cellIndex >= 0)
                    {
                        EmitInstruction(ByteCodeOp.LOAD_CLOSURE, cellIndex);
                        Console.WriteLine($"    → LOAD_CLOSURE for {freeVar} (cell index {cellIndex})");
                    }
                    else
                    {
                        Console.WriteLine($"    ⚠️ Warning: Free variable {freeVar} not found in current scope cells");
                        // 빈 셀 생성
                        EmitInstruction(ByteCodeOp.LOAD_CLOSURE, 0);
                    }
                }
                
                // 클로저 튜플 생성
                EmitInstruction(ByteCodeOp.BUILD_TUPLE, freeVars.Count);
            }
            
            // 4. 기본값이 있는 경우 기본값 튜플을 스택에 푸시
            int makeFunctionFlags = 0;
            if (defaults.Count > 0)
            {
                foreach (var defaultValue in defaults)
                {
                    EmitLoadConst(defaultValue);
                }
                EmitInstruction(ByteCodeOp.BUILD_TUPLE, defaults.Count);
                makeFunctionFlags |= 1; // MAKE_FUNCTION_DEFAULTS flag
            }
            
            // 5. 함수 생성 (기본값 + 클로저 플래그 설정)
            if (freeVars.Count > 0)
            {
                makeFunctionFlags |= 8; // MAKE_FUNCTION_CLOSURE flag
            }
            
            // 6. 데코레이터 적용 (CPython 3.12 호환) - 역순으로 적용 먼저
            if (func.Decorators != null && func.Decorators.Count > 0)
            {
                // 데코레이터는 역순으로 적용됩니다 (안쪽부터 바깥쪽으로)
                for (int i = func.Decorators.Count - 1; i >= 0; i--)
                {
                    var decorator = func.Decorators[i];
                    
                    // CPython pattern: Load decorator first
                    CompileExpression(decorator.DecoratorFunction);
                    
                    // 데코레이터에 추가 인수가 있는 경우 처리 (@decorator(args))
                    // Note: These go on the stack after the decorator but before function
                    if (decorator.Arguments.Count > 0)
                    {
                        foreach (var arg in decorator.Arguments)
                        {
                            CompileExpression(arg);
                        }
                    }
                }
            }
            
            // 7. Load function code and create function
            EmitLoadConst(funcCode);
            EmitInstruction(ByteCodeOp.MAKE_FUNCTION, makeFunctionFlags);
            
            // 8. Call decorators (they're already on the stack)
            if (func.Decorators != null && func.Decorators.Count > 0)
            {
                for (int i = func.Decorators.Count - 1; i >= 0; i--)
                {
                    var decorator = func.Decorators[i];
                    int argCount = decorator.Arguments.Count; // function is implicit
                    
                    // CPython 3.12: CALL calls decorator with function as implicit argument
                    EmitInstruction(ByteCodeOp.CALL, argCount);
                    // Stack after: [decorated_function]
                }
            }
            
            EmitStoreName(func.Name);
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
                        
                        // 이 변수가 현재 함수의 매개변수이거나 지역변수이면 Cell로 만들어야 함
                        if ((parameters.Contains(freeVar) || _varNames.Contains(freeVar)) && !cellVars.Contains(freeVar))
                        {
                            cellVars.Add(freeVar);
                            Console.WriteLine($"🔍 Variable '{freeVar}' needs cell (referenced by nested function '{nestedFunc.Name}')");
                        }
                    }
                    
                    // CPython 3.12: 중첩 함수의 nonlocal 선언도 확인
                    var nonlocalVars = AnalyzeNonlocalDeclarations(nestedFunc.Body);
                    Console.WriteLine($"🔍 Found nonlocal vars in '{nestedFunc.Name}': [{string.Join(", ", nonlocalVars)}]");
                    Console.WriteLine($"🔍 Current parameters: [{string.Join(", ", parameters)}]");
                    Console.WriteLine($"🔍 Current _varNames: [{string.Join(", ", _varNames)}]");
                    
                    foreach (var nonlocalVar in nonlocalVars)
                    {
                        // nonlocal 변수가 현재 함수의 지역변수나 매개변수라면 cell로 만들어야 함
                        if ((parameters.Contains(nonlocalVar) || _varNames.Contains(nonlocalVar)) && !cellVars.Contains(nonlocalVar))
                        {
                            cellVars.Add(nonlocalVar);
                            Console.WriteLine($"🔗 Variable '{nonlocalVar}' needs cell (nonlocal in nested function '{nestedFunc.Name}')");
                        }
                        else
                        {
                            Console.WriteLine($"⚠️ Nonlocal variable '{nonlocalVar}' not found in current scope");
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
            foreach (var statement in statements)
            {
                switch (statement)
                {
                    case AssignStatement assign:
                        if (!localVars.Contains(assign.VariableName))
                        {
                            localVars.Add(assign.VariableName);
                        }
                        break;
                        
                    case FunctionDefStatement nestedFunc:
                        // 재귀적으로 중첩 함수도 확인 (하지만 별도 스코프이므로 현재 함수에는 추가하지 않음)
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
        
        private void EmitInstruction(ByteCodeOp opCode, int argument = 0)
        {
            var instructionOffset = _instructions.Count;
            _instructions.Add(new ByteCodeInstruction(opCode, argument, _currentLineNumber, _currentColumnOffset, _currentFileName));
            
            // Add to line number table if line number is valid
            if (_currentLineNumber >= 0)
            {
                _lineNumberTable[instructionOffset] = _currentLineNumber;
            }
        }
        
        /// <summary>
        /// CPython 3.12 호환 JUMP_BACKWARD oparg 계산 (통합 유틸리티 사용)
        /// </summary>
        private int CalculateJumpBackwardArg(int currentInstrPos, int targetInstrPos)
        {
            return PyJumpBackwardUtil.CalculateJumpBackwardOpArg(currentInstrPos, targetInstrPos, _instructions);
        }
        
        private void EmitLoadConst(PyObject value)
        {
            var index = AddConstant(value);
            EmitInstruction(ByteCodeOp.LOAD_CONST, index);
        }
        
        private void EmitLoadName(string name)
        {
            // Phase 2: 클로저 지원 - 자유 변수 처리 개선
            
            // 0. CPython 3.12: global 변수를 먼저 체크
            if (_globalVars.Contains(name))
            {
                var globalIndex = AddName(name);
                EmitInstruction(ByteCodeOp.LOAD_GLOBAL, globalIndex);
                Console.WriteLine($"    → LOAD_GLOBAL for global var: {name} (global index {globalIndex})");
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
                Console.WriteLine($"    → LOAD_DEREF for nonlocal var: {name} (free index {freeIndex})");
                return;
            }
            
            // 2. 지역 변수(매개변수 포함) 처리
            var varIndex = _varNames.IndexOf(name);
            if (varIndex >= 0)
            {
                // 이 변수가 cell로 변환되었는지 확인
                if (_cellVars.Contains(name))
                {
                    // Cell 변수는 LOAD_DEREF로 접근
                    var cellIndex = _cellVars.IndexOf(name);
                    EmitInstruction(ByteCodeOp.LOAD_DEREF, cellIndex);
                    Console.WriteLine($"    → LOAD_DEREF for cell var: {name} (index {cellIndex})");
                }
                else
                {
                    EmitInstruction(ByteCodeOp.LOAD_FAST, varIndex);
                }
                return;
            }
            
            // 2. 자유 변수 처리 (Phase 2)
            if (_freeVars.Contains(name))
            {
                var freeIndex = _freeVars.IndexOf(name);
                EmitInstruction(ByteCodeOp.LOAD_DEREF, freeIndex);
                Console.WriteLine($"    → LOAD_DEREF for free var: {name} (index {freeIndex})");
                return;
            }
            
            // 3. 모듈 레벨: CPython 3.12 호환성 확인
            if (!_isInFunction)
            {
                // 만약 이 변수가 함수 내에서 global로 선언되었다면 LOAD_GLOBAL 사용
                if (_globalVars.Contains(name) || _moduleGlobalVars.Contains(name))
                {
                    var globalIndex = AddName(name);
                    EmitInstruction(ByteCodeOp.LOAD_GLOBAL, globalIndex);
                    Console.WriteLine($"    → Module level LOAD_GLOBAL for global var: {name} (global index {globalIndex})");
                    return;
                }
                
                // 기본적으로 모듈 레벨에서는 LOAD_NAME 사용 
                var nameIndex = AddName(name);
                EmitInstruction(ByteCodeOp.LOAD_NAME, nameIndex);
                return;
            }
            
            // 4. 함수 내부에서의 전역 변수/내장 함수 참조 - CPython 3.12 호환성
            // 내장 함수 우선 처리
            if (IsBuiltinFunction(name))
            {
                var builtinIndex = AddName(name);
                EmitInstruction(ByteCodeOp.LOAD_GLOBAL, builtinIndex);
            }
            else
            {
                // 일반 전역 변수
                var globalIndex = AddName(name);
                EmitInstruction(ByteCodeOp.LOAD_GLOBAL, globalIndex);
            }
        }
        
        private void EmitStoreName(string name)
        {
            // 함수 내부에서는 지역변수로 등록하고 STORE_FAST 사용
            if (_isInFunction)
            {
                // CPython 3.12: global 변수는 STORE_GLOBAL 사용
                if (_globalVars.Contains(name))
                {
                    var globalIndex = AddName(name);
                    EmitInstruction(ByteCodeOp.STORE_GLOBAL, globalIndex);
                    Console.WriteLine($"    → STORE_GLOBAL for global var: {name} (global index {globalIndex})");
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
                    var freeIndex = _freeVars.IndexOf(name);
                    EmitInstruction(ByteCodeOp.STORE_DEREF, freeIndex);
                    Console.WriteLine($"    → STORE_DEREF for nonlocal var: {name} (free index {freeIndex})");
                    return;
                }
                
                // 셀 변수 처리 (Phase 2)
                if (_cellVars.Contains(name))
                {
                    var cellIndex = _cellVars.IndexOf(name);
                    EmitInstruction(ByteCodeOp.STORE_DEREF, cellIndex);
                    Console.WriteLine($"    → STORE_DEREF for cell var: {name} (index {cellIndex})");
                    return;
                }
                
                // 자유 변수 처리 (Phase 2)
                if (_freeVars.Contains(name))
                {
                    var freeIndex = _freeVars.IndexOf(name);
                    EmitInstruction(ByteCodeOp.STORE_DEREF, freeIndex);
                    Console.WriteLine($"    → STORE_DEREF for free var: {name} (index {freeIndex})");
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
                Console.WriteLine($"    → Module level STORE_GLOBAL for global var: {name} (global index {globalIndex})");
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
                    Console.WriteLine($"    → DELETE_GLOBAL for global var: {name} (global index {globalIndex})");
                    return;
                }
                
                // CPython 3.12: nonlocal 변수는 DELETE_DEREF 사용
                if (_nonlocalVars.Contains(name))
                {
                    var freeIndex = _freeVars.IndexOf(name);
                    if (freeIndex >= 0)
                    {
                        EmitInstruction(ByteCodeOp.DELETE_DEREF, freeIndex);
                        Console.WriteLine($"    → DELETE_DEREF for nonlocal var: {name} (free index {freeIndex})");
                        return;
                    }
                }
                
                // 지역 변수: DELETE_FAST 사용
                var varIndex = _varNames.IndexOf(name);
                if (varIndex >= 0)
                {
                    EmitInstruction(ByteCodeOp.DELETE_FAST, varIndex);
                    Console.WriteLine($"    → DELETE_FAST for local var: {name} (var index {varIndex})");
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
        
        private void EmitBinaryOp(string op)
        {
            // CPython 3.12 정확한 BINARY_OP 구조 - 새로운 순서로 업데이트됨
            var operation = op switch
            {
                "+" => BinaryOpType.ADD,                  // 0 ✅ 변경 없음
                "-" => BinaryOpType.SUBTRACT,             // 1 (was 9)
                "*" => BinaryOpType.MULTIPLY,             // 2 (was 5)
                "/" => BinaryOpType.TRUE_DIVIDE,          // 3 (was 11)
                "//" => BinaryOpType.FLOOR_DIVIDE,        // 4 (was 2)
                "%" => BinaryOpType.MODULO,               // 5 (was 4)
                "**" => BinaryOpType.POWER,               // 6 (was 8)
                "<<" => BinaryOpType.LSHIFT,              // 7 (was 3)
                ">>" => BinaryOpType.RSHIFT,              // 8 (was 7)
                "|" => BinaryOpType.OR,                   // 9 (was 6)
                "^" => BinaryOpType.XOR,                  // 10 (was 10)
                "&" => BinaryOpType.AND,                  // 11 (was 1)
                "@" => BinaryOpType.MATRIX_MULTIPLY,      // 12 (was 12)
                // Note: "and" and "or" are now handled as BoolOpExpression, not BinaryOpExpression
                _ => throw new NotImplementedException($"Binary operator '{op}' not implemented")
            };
            
            // BINARY_OP OpCode와 operation 타입을 argument로 전달
            EmitInstruction(ByteCodeOp.BINARY_OP, (int)operation);
        }
        
        private int AddConstant(PyObject value)
        {
            _constants.Add(value);
            return _constants.Count - 1;
        }
        
        private int AddName(string name)
        {
            if (!_names.Contains(name))
                _names.Add(name);
            return _names.IndexOf(name);
        }
        
        // 추가 컴파일 메서드들
        
        private void EmitUnaryOp(string op)
        {
            var opCode = op switch
            {
                "+" => ByteCodeOp.UNARY_POSITIVE,
                "-" => ByteCodeOp.UNARY_NEGATIVE,
                "not" => ByteCodeOp.UNARY_NOT,
                "~" => ByteCodeOp.UNARY_INVERT,
                _ => throw new NotImplementedException($"Unary operator '{op}' not implemented")
            };
            EmitInstruction(opCode);
        }
        
        private void EmitCompareOp(string op)
        {
            // Handle membership test operations with CONTAINS_OP
            if (op == "in" || op == "not in")
            {
                var containsOp = op switch
                {
                    "in" => 0,     // IN
                    "not in" => 1, // NOT_IN
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
                "<=" => (int)CompareOp.LE,  // 26
                "==" => (int)CompareOp.EQ,  // 40
                "!=" => (int)CompareOp.NE,  // 55
                ">" => (int)CompareOp.GT,   // 68
                ">=" => (int)CompareOp.GE,  // 92
                _ => throw new NotImplementedException($"Compare operator '{op}' not implemented")
            };
            EmitInstruction(ByteCodeOp.COMPARE_OP, compareOp);
        }
        
        private void EmitLoadAttr(string attrName)
        {
            var index = AddName(attrName);
            EmitInstruction(ByteCodeOp.LOAD_ATTR, index);
        }
        
        private void CompileAugAssign(AugAssignStatement augAssign)
        {
            // target += value -> LOAD target, LOAD value, INPLACE_ADD, STORE target
            EmitLoadName(augAssign.Target);
            CompileExpression(augAssign.Value);
            
            // CPython 3.12 정확한 순서로 업데이트됨 - 동일한 BinaryOpType 사용
            var binaryOpType = augAssign.Op switch
            {
                "+=" => BinaryOpType.ADD,           // 0 ✅ 변경 없음  
                "-=" => BinaryOpType.SUBTRACT,      // 1 (순서 변경됨)
                "*=" => BinaryOpType.MULTIPLY,      // 2 (순서 변경됨)
                "/=" => BinaryOpType.TRUE_DIVIDE,   // 3 (순서 변경됨)
                "//=" => BinaryOpType.FLOOR_DIVIDE, // 4 (순서 변경됨)
                "%=" => BinaryOpType.MODULO,        // 5 (순서 변경됨)
                "**=" => BinaryOpType.POWER,        // 6 (순서 변경됨)
                "<<=" => BinaryOpType.LSHIFT,       // 7 (순서 변경됨)
                ">>=" => BinaryOpType.RSHIFT,       // 8 (순서 변경됨)
                "|=" => BinaryOpType.OR,            // 9 (순서 변경됨)
                "^=" => BinaryOpType.XOR,           // 10 (순서 변경됨)
                "&=" => BinaryOpType.AND,           // 11 (순서 변경됨)
                "@=" => BinaryOpType.MATRIX_MULTIPLY, // 12 (순서 변경됨)
                _ => throw new NotImplementedException($"Augment assign operator '{augAssign.Op}' not implemented")
            };
            
            EmitInstruction(ByteCodeOp.BINARY_OP, (int)binaryOpType);
            EmitStoreName(augAssign.Target);
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
                "@" => BinaryOpType.MATRIX_MULTIPLY,
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
            
            // 1. First emit SETUP_ANNOTATIONS to ensure __annotations__ dict exists
            EmitInstruction(ByteCodeOp.SETUP_ANNOTATIONS);
            
            // 2. Handle the value assignment if present
            if (annAssign.Value != null)
            {
                CompileExpression(annAssign.Value);
                EmitStoreName(annAssign.VariableName);
            }
            
            // 3. Store type annotation in __annotations__ dict (CPython 3.12 pattern)
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
            Console.WriteLine($"\n🔍 Compiling async function: {asyncFunc.Name}");
            
            // 1. 자유 변수 분석 (동일한 방식으로 분석)
            var analyzer = new FreeVariableAnalyzer();
            Console.WriteLine($"  DEBUG: Current _varNames: [{string.Join(", ", _varNames)}]");
            var (freeVars, cellVars) = analyzer.AnalyzeAsyncFunction(asyncFunc, _varNames);
            
            Console.WriteLine($"  Free variables: [{string.Join(", ", freeVars)}]");
            Console.WriteLine($"  Cell variables: [{string.Join(", ", cellVars)}]");
            
            // 2. 매개변수와 기본값 파싱
            var (paramNames, defaults, flags) = ParseAsyncFunctionParameters(asyncFunc.Parameters);
            
            // 3. 코드 객체 컴파일 (async 함수 전용)
            var codeObject = CompileAsyncFunctionBody(asyncFunc, freeVars, cellVars);
            
            // 4. 기본값들을 스택에 로드
            foreach (var defaultValue in defaults)
            {
                EmitLoadConst(defaultValue); // 기본값은 이미 PyObject이므로 직접 로드
            }
            
            // 5. 클로저가 있으면 셀 변수들을 스택에 로드
            if (freeVars.Any())
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
            
            // 6. 코드 객체를 상수로 로드 (이름은 이미 코드 객체에 포함됨)
            EmitLoadConst(codeObject);
            
            // 8. MAKE_ASYNC_FUNCTION 명령어 생성 (아직 없으므로 MAKE_FUNCTION으로 대체)
            var makeFlags = 0;
            if (defaults.Any()) makeFlags |= 0x01;  // CO_HAS_DEFAULTS
            if (freeVars.Any()) makeFlags |= 0x08;  // CO_HAS_CLOSURE
            makeFlags |= 0x10; // CO_ASYNC_FUNCTION (async 함수 플래그)
            
            EmitInstruction(ByteCodeOp.MAKE_FUNCTION, makeFlags);
            
            // 9. 함수를 변수에 저장
            EmitStoreName(asyncFunc.Name);
            
            Console.WriteLine($"✅ Async function {asyncFunc.Name} compiled successfully");
        }
        private void CompileClass(ClassDefStatement cls)
        {
            // PEP 695: Generic class with type parameters requires special handling
            if (cls.TypeParams.Count > 0)
            {
                CompileGenericClass(cls);
            }
            else
            {
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
        private PyCodeObject CompileGenericParametersFunction(List<string> typeParams, string functionName, FunctionDefStatement func)
        {
            // Save current compilation state
            var savedInstructions = _instructions;
            var savedConstants = _constants;
            var savedNames = _names;
            var savedVarNames = _varNames;
            var savedCellVars = _cellVars;
            var savedFreeVars = _freeVars;
            
            // Initialize new compilation state for generic parameters function
            _instructions = new List<ByteCodeInstruction>();
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
                    EmitLoadConst(new PyString(typeParam));
                    EmitInstruction(ByteCodeOp.CALL_INTRINSIC_1, 7); // INTRINSIC_TYPEVAR
                    EmitInstruction(ByteCodeOp.COPY, 1);
                    
                    // Add to varNames for STORE_FAST/LOAD_FAST
                    if (!_varNames.Contains(typeParam))
                        _varNames.Add(typeParam);
                    
                    int varIndex = _varNames.IndexOf(typeParam);
                    EmitInstruction(ByteCodeOp.STORE_FAST, varIndex);
                }
                
                // Build tuple of type parameters
                EmitInstruction(ByteCodeOp.BUILD_TUPLE, typeParams.Count);
                
                // Create annotations tuple: complex parameter annotations
                var (paramNames, defaults, flags) = ParseFunctionParameters(func.Parameters);
                
                // Build complex annotations tuple for all parameters and return type
                var annotationCount = 0;
                
                // Add parameter annotations
                foreach (var paramName in paramNames)
                {
                    EmitLoadConst(new PyString(paramName)); // parameter name
                    annotationCount++;
                    
                    // Add type annotation (simplified: use first type param for now)
                    int typeVarIndex = _varNames.IndexOf(typeParams[0]);
                    EmitInstruction(ByteCodeOp.LOAD_FAST, typeVarIndex);
                    annotationCount++;
                }
                
                // Add return type annotation if exists
                if (func.Parameters.Count > 0) // Simple heuristic: if has params, likely has return type
                {
                    EmitLoadConst(new PyString("return"));
                    annotationCount++;
                    
                    int typeVarIndex = _varNames.IndexOf(typeParams[0]);
                    EmitInstruction(ByteCodeOp.LOAD_FAST, typeVarIndex);
                    annotationCount++;
                }
                
                EmitInstruction(ByteCodeOp.BUILD_TUPLE, annotationCount);
                
                // Compile actual function
                var compiler = new PythonCompiler();
                var funcCode = compiler.CompileFunction(func.Body, func.Name, paramNames, defaults, flags);
                EmitLoadConst(funcCode);
                EmitInstruction(ByteCodeOp.MAKE_FUNCTION, 4); // annotations flag
                
                // SWAP 2 (not 4!) and CALL_INTRINSIC_2 for SET_FUNCTION_TYPE_PARAMS
                EmitInstruction(ByteCodeOp.SWAP, 2);
                EmitInstruction(ByteCodeOp.CALL_INTRINSIC_2, 4); // INTRINSIC_SET_FUNCTION_TYPE_PARAMS
                
                EmitInstruction(ByteCodeOp.RETURN_VALUE);
                
                // Build the code object
                var functionName_full = $"<generic parameters of {functionName}>";
                return new PyCodeObject(
                    functionName_full, 
                    _instructions,
                    _constants,
                    _names,
                    _varNames,
                    0, // argCount
                    _freeVars,
                    _cellVars,
                    new List<PyObject>(), // defaultValues
                    0, // flags
                    "", // fileName
                    new List<string>() // sourceLines
                );
            }
            finally
            {
                // Restore compilation state
                _instructions = savedInstructions;
                _constants = savedConstants;
                _names = savedNames;
                _varNames = savedVarNames;
                _cellVars = savedCellVars;
                _freeVars = savedFreeVars;
            }
        }
        
        private void CompileRegularClass(ClassDefStatement cls)
        {
            // CPython 3.12: Regular class compilation (no type parameters)
            // PUSH_NULL 먼저, 그 다음 __build_class__ function 로드
            EmitInstruction(ByteCodeOp.PUSH_NULL);
            EmitLoadName("__build_class__");
            
            // Compile class body into a function
            var classBodyName = $"<class_body_{cls.Name}>";
            var classBodyCode = CompileClassBody(cls.Body, classBodyName);
            
            // Load the class body function code
            EmitLoadConst(classBodyCode);
            EmitInstruction(ByteCodeOp.MAKE_FUNCTION, 0);
            
            // Load class name
            EmitLoadConst(new PyString(cls.Name));
            
            // Load base classes
            Console.WriteLine($"🔍 CompileRegularClass: {cls.Name} has {cls.Bases.Count} base classes:");
            foreach (var baseExpr in cls.Bases)
            {
                Console.WriteLine($"   → Base class expression: {baseExpr.GetType().Name}");
                CompileExpression(baseExpr);
            }
            
            // Load metaclass if specified
            int totalArgs = 2 + cls.Bases.Count;
            if (cls.Metaclass != null)
            {
                // Push explicit metaclass marker to distinguish from base classes
                EmitLoadConst(new PyString("__metaclass__"));  // Special marker
                CompileExpression(cls.Metaclass);
                totalArgs += 2;  // marker + metaclass
            }
            
            // Call __build_class__(class_body_function, name, *bases [, "__metaclass__", metaclass])
            EmitInstruction(ByteCodeOp.CALL, totalArgs);
            
            // Store the created class
            EmitStoreName(cls.Name);
        }
        
        /// <summary>
        /// Compile simplified Generic Parameters function for PEP 695
        /// This creates a basic working version first
        /// </summary>
        private PyCodeObject CompileSimplifiedGenericParametersFunction(List<string> typeParams, string className, List<Statement> classBody)
        {
            // Save current compilation state
            var savedInstructions = _instructions;
            var savedConstants = _constants;
            var savedNames = _names;
            var savedVarNames = _varNames;
            var savedCellVars = _cellVars;
            var savedFreeVars = _freeVars;
            
            // Initialize new compilation state for generic parameters function
            _instructions = new List<ByteCodeInstruction>();
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
                    _cellVars.Add(typeParam);    // Index 2+ in MAKE_CELL
                }
                
                // Emit MAKE_CELL instructions
                // CPython 3.12 uses direct CellVars indexing: 0, 1, 2...
                for (int i = 0; i < _cellVars.Count; i++)
                {
                    EmitInstruction(ByteCodeOp.MAKE_CELL, i);
                }
                
                // 2. RESUME instruction
                EmitInstruction(ByteCodeOp.RESUME, 0);
                
                // 3. Create type parameters and store in cells
                foreach (var typeParam in typeParams)
                {
                    EmitLoadConst(new PyString(typeParam));
                    EmitInstruction(ByteCodeOp.CALL_INTRINSIC_1, (int)IntrinsicFunction.INTRINSIC_TYPEVAR);
                    EmitInstruction(ByteCodeOp.COPY, 1);
                    EmitStoreDeref(typeParam);
                }
                
                // 4. Build type parameters tuple and store
                EmitInstruction(ByteCodeOp.BUILD_TUPLE, typeParams.Count);
                EmitStoreDeref(".type_params");
                
                // 5. Create regular class with __build_class__
                EmitInstruction(ByteCodeOp.PUSH_NULL);
                EmitLoadName("__build_class__");
                
                // 6. Load closure for class body (type parameters) - CPython 3.12 uses LOAD_CLOSURE
                EmitLoadClosure(".type_params");
                EmitLoadClosure(typeParams[0]); // Load first type parameter (e.g., 'T')
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
                return new PyCodeObject(
                    functionName, 
                    _instructions, 
                    _constants, 
                    _names, 
                    _varNames, 
                    0,  // argCount
                    _freeVars,    // 7번째 매개변수: freeVars
                    _cellVars     // 8번째 매개변수: cellVars (수정됨!)
                );
            }
            finally
            {
                // Restore compilation state
                _instructions = savedInstructions;
                _constants = savedConstants;
                _names = savedNames;
                _varNames = savedVarNames;
                _cellVars = savedCellVars;
                _freeVars = savedFreeVars;
            }
        }
        
        private PyCodeObject CompileClassBody(List<Statement> body, string className)
        {
            // Save current compilation state
            var savedInstructions = _instructions;
            var savedConstants = _constants;
            var savedNames = _names;
            var savedVarNames = _varNames;
            var savedCellVars = _cellVars;
            var savedFreeVars = _freeVars;
            
            // Initialize new compilation state for class body
            _instructions = new List<ByteCodeInstruction>();
            _constants = new List<PyObject>();
            _names = new List<string>();
            _varNames = new List<string>();
            _cellVars = new List<string>();
            _freeVars = new List<string>();
            _exceptionTable = new List<ExceptionTableEntry>(); // Reset Exception Table
            
            // Check if class body contains super() calls and add __class__ cell variable if needed
            if (ContainsSuperCalls(body))
            {
                Console.WriteLine($"🔍 Detected super() calls in class {className}, adding __class__ cell variable");
                _cellVars.Add("__class__");
                
                // Generate MAKE_CELL instruction for __class__ cell variable
                // CPython 3.12: __class__ cell variable uses index 0 (first cellVar)
                var cellVarIndex = 0; // __class__ is always the first (index 0) cell variable
                Console.WriteLine($"🔧 Generating MAKE_CELL for __class__ at cell index {cellVarIndex}");
                EmitInstruction(ByteCodeOp.MAKE_CELL, cellVarIndex);
            }
            
            // For now, disable free variable analysis for class bodies  
            // Class bodies will use normal name lookup instead of closure mechanism
            // var freeVariableAnalyzer = new ClassBodyFreeVariableAnalyzer();
            // var classFreeVars = freeVariableAnalyzer.AnalyzeClassBody(body, savedNames);
            // _freeVars.AddRange(classFreeVars);
            
            try
            {
                // Compile class body statements
                foreach (var stmt in body)
                {
                    CompileStatement(stmt);
                }
                
                // CPython 3.12: If __class__ cell variable exists, store __classcell__ for __build_class__
                if (_cellVars.Contains("__class__"))
                {
                    var classIndex = _cellVars.IndexOf("__class__");
                    Console.WriteLine($"🔧 Generating __classcell__ store for __class__ cell at index {classIndex}");
                    
                    // LOAD_CLOSURE __class__ (CPython: LOAD_CLOSURE 0 (__class__))
                    EmitInstruction(ByteCodeOp.LOAD_CLOSURE, classIndex);
                    
                    // COPY 1 (CPython does this to duplicate the cell)
                    EmitInstruction(ByteCodeOp.COPY, 1);
                    
                    // STORE_NAME __classcell__ (CPython: STORE_NAME 4 (__classcell__))
                    var classcellIndex = AddName("__classcell__");
                    EmitInstruction(ByteCodeOp.STORE_NAME, classcellIndex);
                    Console.WriteLine($"✅ Stored __classcell__ at name index {classcellIndex}");
                }
                
                // Return None at the end
                EmitLoadConst(PyNone.Instance);
                EmitInstruction(ByteCodeOp.RETURN_VALUE);
                
                // Create code object for class body with free variables
                var codeObject = new PyCodeObject(
                    className,
                    _instructions.ToList(),
                    _constants.ToList(),
                    _names.ToList(),
                    _varNames.ToList(),
                    argCount: 0,  // Class body has no arguments
                    freeVars: _freeVars.ToList(),  // Include free variables
                    cellVars: _cellVars.ToList(),
                    defaultValues: null,
                    flags: 0,
                    fileName: _currentFileName,
                    sourceLines: _sourceLines
                );
                
                return codeObject;
            }
            finally
            {
                // Restore compilation state
                _instructions = savedInstructions;
                _constants = savedConstants;
                _names = savedNames;
                _varNames = savedVarNames;
                _cellVars = savedCellVars;
                _freeVars = savedFreeVars;
            }
        }
        
        /// <summary>
        /// Check if class body contains super() calls (without arguments)
        /// </summary>
        private bool ContainsSuperCalls(List<Statement> statements)
        {
            Console.WriteLine($"🔍 Checking {statements.Count} statements for super() calls");
            foreach (var stmt in statements)
            {
                Console.WriteLine($"  - Checking statement: {stmt.GetType().Name}");
                if (ContainsSuperCallsInStatement(stmt))
                {
                    Console.WriteLine($"    ✅ Found super() call in {stmt.GetType().Name}");
                    return true;
                }
            }
            Console.WriteLine($"  ❌ No super() calls found in {statements.Count} statements");
            return false;
        }
        
        /// <summary>
        /// Recursively check if a statement contains super() calls
        /// </summary>
        private bool ContainsSuperCallsInStatement(Statement stmt)
        {
            switch (stmt)
            {
                case FunctionDefStatement func:
                    // Check method bodies for super() calls
                    return ContainsSuperCalls(func.Body);
                    
                case IfStatement ifStmt:
                    bool result = ContainsSuperCallsInExpression(ifStmt.Test);
                    result |= ContainsSuperCalls(ifStmt.Body);
                    if (ifStmt.OrElse != null && ifStmt.OrElse.Count > 0)
                        result |= ContainsSuperCalls(ifStmt.OrElse);
                    return result;
                    
                case ExpressionStatement exprStmt:
                    return ContainsSuperCallsInExpression(exprStmt.Expression);
                    
                case AssignStatement assignStmt:
                    return ContainsSuperCallsInExpression(assignStmt.Value);
                    
                case ReturnStatement returnStmt:
                    if (returnStmt.Value != null)
                        return ContainsSuperCallsInExpression(returnStmt.Value);
                    return false;
                    
                default:
                    Console.WriteLine($"  ⚠️  Unhandled statement type: {stmt.GetType().Name}");
                    return false;
            }
        }
        
        /// <summary>
        /// Check if an expression contains super() calls
        /// </summary>
        private bool ContainsSuperCallsInExpression(Expression expr)
        {
            switch (expr)
            {
                case CallExpression call:
                    // Check if this is a super() call
                    if (call.Function is NameExpression name && name.Name == "super" && call.Arguments.Count == 0)
                    {
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
                    return ContainsSuperCallsInExpression(attr.Value);
                    
                case BinaryOpExpression binary:
                    return ContainsSuperCallsInExpression(binary.Left) || 
                           ContainsSuperCallsInExpression(binary.Right);
                    
                default:
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
                        // AssignStatement uses VariableName, not Target
                        definedNames.Add(assign.VariableName);
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
                EmitLoadConst(new PyString(typeParam)); // Type parameter name as placeholder
                EmitStoreName(typeParam); // Bind to current scope
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
                    alias = moduleName;
                }
                
                // Emit IMPORT_NAME bytecode
                var moduleIndex = AddConstant(new PyString(actualModule));
                EmitInstruction(ByteCodeOp.IMPORT_NAME, moduleIndex);
                
                // Store the imported module in the correct variable name
                // CPython 3.12: Use STORE_GLOBAL for module level imports
                var nameIndex = AddName(alias);
                if (IsModuleLevel())
                {
                    EmitInstruction(ByteCodeOp.STORE_GLOBAL, nameIndex);
                }
                else
                {
                    EmitInstruction(ByteCodeOp.STORE_NAME, nameIndex);
                }
            }
        }
        private void CompileImportFrom(ImportFromStatement importFrom)
        {
            // Load the module first
            var moduleIndex = AddConstant(new PyString(importFrom.Module));
            EmitInstruction(ByteCodeOp.IMPORT_NAME, moduleIndex);
            
            foreach (var itemName in importFrom.Names)
            {
                // Handle "item as alias" format
                string actualItem, alias;
                if (itemName.Contains(" as "))
                {
                    var parts = itemName.Split(new[] { " as " }, StringSplitOptions.RemoveEmptyEntries);
                    actualItem = parts[0].Trim();
                    alias = parts[1].Trim();
                }
                else
                {
                    actualItem = itemName;
                    alias = itemName;
                }
                
                // Emit IMPORT_FROM bytecode
                var itemIndex = AddConstant(new PyString(actualItem));
                EmitInstruction(ByteCodeOp.IMPORT_FROM, itemIndex);
                
                // Store the imported item in the correct variable name
                // CPython 3.12: Use STORE_GLOBAL for module level imports
                var nameIndex = AddName(alias);
                if (IsModuleLevel())
                {
                    EmitInstruction(ByteCodeOp.STORE_GLOBAL, nameIndex);
                }
                else
                {
                    EmitInstruction(ByteCodeOp.STORE_NAME, nameIndex);
                }
            }
            
            // Pop the module from stack (cleanup)
            EmitInstruction(ByteCodeOp.POP_TOP);
        }
        /// <summary>
        /// CPython-style if statement compilation - supports if-elif-else chains
        /// </summary>
        private void CompileIf(IfStatement ifStmt)
        {
            // CPython 3.12 스타일: if-elif-else 체인 컴파일 (완전 수정)
            var endJumps = new List<int>(); // 각 블록 끝에서 전체 if-elif-else 끝으로의 점프들
            var conditionJumps = new List<int>(); // 각 조건의 False 점프들 (나중에 패치)
            var conditionStarts = new List<int>(); // 각 조건 시작 위치 저장
            
            // 모든 if/elif 조건들을 미리 수집
            var conditions = new List<(Expression Test, List<Statement> Body)>();
            var currentIf = ifStmt;
            
            // 모든 if/elif 수집
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
            
            // 각 조건과 바디 컴파일
            for (int i = 0; i < conditions.Count; i++)
            {
                var (test, body) = conditions[i];
                
                // 조건 시작 위치 저장
                var conditionStartPos = _instructions.Count;
                conditionStarts.Add(conditionStartPos);
                
                // 조건 컴파일
                CompileExpression(test);
                
                // 조건이 False면 다음 elif/else로 점프 (나중에 패치)
                var jumpIfFalse = _instructions.Count;
                EmitInstruction(ByteCodeOp.POP_JUMP_IF_FALSE, 0);
                conditionJumps.Add(jumpIfFalse);
                
                // 바디 컴파일
                foreach (var stmt in body)
                {
                    CompileStatement(stmt);
                }
                
                // 바디 실행 후 전체 if-elif-else 끝으로 점프 (return이 없는 경우)
                var hasReturn = body.Any(stmt => stmt is ReturnStatement);
                if (!hasReturn)
                {
                    var jumpToEnd = _instructions.Count;
                    EmitInstruction(ByteCodeOp.JUMP_FORWARD, 0);
                    endJumps.Add(jumpToEnd);
                }
                
                // 이전 조건의 False 점프를 현재 조건의 시작으로 패치
                if (i > 0)
                {
                    var prevJumpIndex = conditionJumps[i - 1];
                    var currentConditionStart = conditionStarts[i]; // 실제 조건 시작 위치 사용
                    var relativeOffset = currentConditionStart - prevJumpIndex - 1;
                    _instructions[prevJumpIndex] = new ByteCodeInstruction(ByteCodeOp.POP_JUMP_IF_FALSE, relativeOffset);
                }
            }
            
            // else 블록 처리
            var finalElse = ifStmt;
            while (finalElse.OrElse != null && finalElse.OrElse.Count == 1 && 
                   finalElse.OrElse[0] is IfStatement)
            {
                finalElse = (IfStatement)finalElse.OrElse[0];
            }
            
            // 마지막 조건의 False 점프를 else 블록으로 패치
            if (conditionJumps.Count > 0)
            {
                var lastJumpIndex = conditionJumps.Last();
                var elsePos = _instructions.Count;
                var relativeOffset = elsePos - lastJumpIndex - 1;
                _instructions[lastJumpIndex] = new ByteCodeInstruction(ByteCodeOp.POP_JUMP_IF_FALSE, relativeOffset);
            }
            
            // else 블록 컴파일
            if (finalElse.OrElse != null && finalElse.OrElse.Count > 0)
            {
                foreach (var stmt in finalElse.OrElse)
                {
                    CompileStatement(stmt);
                }
            }
            
            // 모든 end jumps를 현재 위치로 패치
            var endPosition = _instructions.Count;
            foreach (var jumpIndex in endJumps)
            {
                var relativeOffset = endPosition - jumpIndex - 1;
                _instructions[jumpIndex] = new ByteCodeInstruction(ByteCodeOp.JUMP_FORWARD, relativeOffset);
            }
        }
        
        /// <summary>
        /// CPython 3.12 호환 while True: compilation
        /// 특징: 조건 체크 없이 바로 루프 바디 시작, NOP 삽입
        /// </summary>
        private void CompileWhileTrue(WhileStatement whileStmt)
        {
            Console.WriteLine("🔧 CPython 3.12 호환 while True 루프 컴파일");
            
            // Setup loop context for break/continue
            var breakLabel = CreateLabel("while_true_break");
            var continueLabel = CreateLabel("while_true_continue");
            PushLoopContext(breakLabel, continueLabel);
            
            // CPython pattern: emit NOP for while True:
            EmitInstruction(ByteCodeOp.NOP, 0);
            
            // 루프 바디 시작점 (JUMP_BACKWARD 타겟) - continue target
            var bodyStart = _instructions.Count;
            MarkLabel(continueLabel); // continue는 루프 바디 시작으로
            Console.WriteLine($"  바디 시작점 = {bodyStart} (JUMP_BACKWARD 타겟)");
            
            // Compile loop body
            foreach (var stmt in whileStmt.Body)
            {
                CompileStatement(stmt);
            }
            
            // JUMP_BACKWARD to body start (no condition check)
            int currentPos = _instructions.Count;
            int jumpBackwardArg = CalculateJumpBackwardArg(currentPos, bodyStart);
            Console.WriteLine($"  JUMP_BACKWARD {currentPos} → {bodyStart} (arg={jumpBackwardArg})");
            EmitInstruction(ByteCodeOp.JUMP_BACKWARD, jumpBackwardArg);
            
            // Pop loop context
            PopLoopContext();
            
            // Mark break label - break는 여기로 점프
            MarkLabel(breakLabel);
            Console.WriteLine("🔧 CPython 3.12 호환 while True 루프 컴파일 완료");
        }
        
        
        /// <summary>
        /// CPython 3.12 완전 호환 while loop compilation
        /// 특징: 조건을 두 번 체크 (초기 + 루프 끝)
        /// </summary>
        private void CompileWhile(WhileStatement whileStmt)
        {
            Console.WriteLine("🔧 CPython 3.12 호환 while 루프 컴파일 시작");
            
            // Check if this is while True: case
            bool isWhileTrue = IsConstantTrue(whileStmt.Test);
            Console.WriteLine($"  while True 패턴: {isWhileTrue}");
            
            if (isWhileTrue)
            {
                CompileWhileTrue(whileStmt);
                return;
            }
            
            // Phase 1: 초기 조건 체크 (CPython pattern)
            Console.WriteLine("  Phase 1: 초기 조건 체크");
            CompileExpression(whileStmt.Test);
            
            var initialJumpIfFalse = _instructions.Count;
            EmitInstruction(ByteCodeOp.POP_JUMP_IF_FALSE, 0); // 주소는 나중에 패치
            
            // Phase 2: 루프 바디 컴파일 (JUMP_BACKWARD 타겟은 첫 번째 바디 명령어)
            // CPython 패턴: JUMP_BACKWARD는 실제 루프 바디 시작으로 점프
            var bodyStart = _instructions.Count; // 바디 첫 번째 명령어 위치
            Console.WriteLine($"  Phase 2: 바디 시작점 = {bodyStart} (JUMP_BACKWARD 타겟)");
            
            // Compile loop body
            foreach (var stmt in whileStmt.Body)
            {
                CompileStatement(stmt);
            }
            
            // Phase 3: 루프 끝 조건 체크 (CPython pattern)
            Console.WriteLine("  Phase 3: 루프 끝 조건 체크");
            CompileExpression(whileStmt.Test);  // 조건을 두 번째로 체크
            
            var endJumpIfFalse = _instructions.Count;
            EmitInstruction(ByteCodeOp.POP_JUMP_IF_FALSE, 0); // 주소는 나중에 패치
            
            // Phase 4: JUMP_BACKWARD (바디 시작점으로 - CPython 패턴 확인됨)
            int currentPos = _instructions.Count;
            int jumpBackwardArg = CalculateJumpBackwardArg(currentPos, bodyStart);
            Console.WriteLine($"  Phase 4: JUMP_BACKWARD {currentPos} → {bodyStart} (arg={jumpBackwardArg})");
            EmitInstruction(ByteCodeOp.JUMP_BACKWARD, jumpBackwardArg);
            
            // Phase 5: 루프 종료 지점
            var loopEnd = _instructions.Count;
            Console.WriteLine($"  Phase 5: 루프 종료점 = {loopEnd}");
            
            // 점프 주소 패치 (상대 오프셋 사용)
            var relativeOffsetInitial = loopEnd - initialJumpIfFalse - 1;
            var relativeOffsetEnd = loopEnd - endJumpIfFalse - 1;
            Console.WriteLine($"  Patching jump instructions:");
            Console.WriteLine($"    initialJumpIfFalse[{initialJumpIfFalse}] → {loopEnd} (relative offset: {relativeOffsetInitial})");
            Console.WriteLine($"    endJumpIfFalse[{endJumpIfFalse}] → {loopEnd} (relative offset: {relativeOffsetEnd})");
            _instructions[initialJumpIfFalse] = new ByteCodeInstruction(ByteCodeOp.POP_JUMP_IF_FALSE, relativeOffsetInitial);
            _instructions[endJumpIfFalse] = new ByteCodeInstruction(ByteCodeOp.POP_JUMP_IF_FALSE, relativeOffsetEnd);
            
            // While completed normally - execute else clause if present
            if (whileStmt.ElseClause != null && whileStmt.ElseClause.Count > 0)
            {
                foreach (var stmt in whileStmt.ElseClause)
                {
                    CompileStatement(stmt);
                }
            }
            
            Console.WriteLine("🔧 CPython 3.12 호환 while 루프 컴파일 완료");
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
        /// </summary>
        private void CompileFor(ForStatement forStmt)
        {
            // CPython approach with loop-else support
            
            // 1. Get iterator from iterable
            CompileExpression(forStmt.Iter);  // Push iterable on stack
            
            // CPython 3.12: Generator function에서 .0 매개변수는 이미 iterator임
            // .0 매개변수인 경우 GET_ITER 건너뛰기
            bool skipGetIter = forStmt.Iter is NameExpression nameExpr && nameExpr.Name == ".0";
            if (!skipGetIter)
            {
                EmitInstruction(ByteCodeOp.GET_ITER); // Convert to iterator
            }
            
            // 2. Loop start - FOR_ITER will handle next() and StopIteration
            var forIterInstruction = _instructions.Count;
            EmitInstruction(ByteCodeOp.FOR_ITER, 0); // Jump target will be patched later
            
            // 3. FOR_ITER pushes the next value on stack, store it in loop variable
            // CPython 3.12에서 FOR_ITER는 4바이트 명령어이므로 자동으로 올바른 오프셋 생성
            EmitStoreName(forStmt.Target);
            
            // 4. Set up loop context for break/continue with FOR_ITER tracking
            var breakLabel = CreateLabel("for_break");
            var continueLabel = CreateLabel("for_continue");
            PushLoopContext(breakLabel, continueLabel, forIterInstruction);
            
            // 5. Execute loop body
            foreach (var stmt in forStmt.Body)
            {
                CompileStatement(stmt);
            }
            
            // 6. Mark continue label (jump back to FOR_ITER)
            MarkLabel(continueLabel);
            
            // 7. Jump back to FOR_ITER (not GET_ITER) - CPython 3.12 style relative offset
            // CPython 3.12: 통일된 JUMP_BACKWARD oparg 계산 사용
            int currentPos = _instructions.Count;
            int jumpBackwardArg = CalculateJumpBackwardArg(currentPos, forIterInstruction);
            EmitInstruction(ByteCodeOp.JUMP_BACKWARD, jumpBackwardArg);
            
            // 8. CPython 3.12 방식: END_FOR 추가 (통합 구조)
            EmitInstruction(ByteCodeOp.END_FOR, 0);
            
            // 9. Loop completed normally - execute else clause if present
            if (forStmt.ElseClause != null && forStmt.ElseClause.Count > 0)
            {
                foreach (var stmt in forStmt.ElseClause)
                {
                    CompileStatement(stmt);
                }
            }
            
            // 10. Mark break label (after ALL loop constructs including else)
            MarkLabel(breakLabel);
            
            // 11. Pop loop context after everything (FOR_ITER 패치가 자동으로 수행됨)
            PopLoopContext();
            
            // Note: Break statements will need to jump past the else clause to loopEnd
            // This requires break handling to be aware of loop-else structure
        }
        
        /// <summary>
        /// Compile for loop with tuple unpacking (e.g., for key, value in items:)
        /// </summary>
        private void CompileForTuple(ForTupleStatement forTupleStmt)
        {
            // CPython approach with tuple unpacking support
            
            // 1. Get iterator from iterable
            CompileExpression(forTupleStmt.Iter);  // Push iterable on stack
            EmitInstruction(ByteCodeOp.GET_ITER); // Convert to iterator
            
            // 2. Loop start - FOR_ITER will handle next() and StopIteration
            var forIterInstruction = _instructions.Count;
            EmitInstruction(ByteCodeOp.FOR_ITER, 0); // Jump target will be patched later
            
            // 3. FOR_ITER pushes the next value on stack, unpack it into target variables
            // The value is a tuple/list, we need to unpack it
            EmitInstruction(ByteCodeOp.UNPACK_SEQUENCE, forTupleStmt.Targets.Count);
            
            // 4. Store each unpacked value in the target variables (in correct order)
            // UNPACK_SEQUENCE pushes items in reverse order, so we store them in forward order
            for (int i = 0; i < forTupleStmt.Targets.Count; i++)
            {
                EmitStoreName(forTupleStmt.Targets[i]);
            }
            
            // 5. Set up loop context with FOR_ITER tracking
            var breakLabel = CreateLabel("for_break");
            var continueLabel = CreateLabel("for_continue");
            PushLoopContext(breakLabel, continueLabel, forIterInstruction);
            
            // 6. Execute loop body
            foreach (var stmt in forTupleStmt.Body)
            {
                CompileStatement(stmt);
            }
            
            // 7. Mark continue label
            MarkLabel(continueLabel);
            
            // 8. Jump back to FOR_ITER - CPython 3.12 style relative offset
            // CPython 3.12: 통일된 JUMP_BACKWARD oparg 계산 사용
            int currentPos = _instructions.Count;
            int jumpBackwardArg = CalculateJumpBackwardArg(currentPos, forIterInstruction);
            EmitInstruction(ByteCodeOp.JUMP_BACKWARD, jumpBackwardArg);
            
            // 9. CPython 3.12 방식: END_FOR 추가 (통합 구조)
            EmitInstruction(ByteCodeOp.END_FOR, 0);
            
            // 10. Loop completed normally - execute else clause if present
            if (forTupleStmt.ElseClause != null && forTupleStmt.ElseClause.Count > 0)
            {
                foreach (var stmt in forTupleStmt.ElseClause)
                {
                    CompileStatement(stmt);
                }
            }
            
            // 11. Mark break label
            MarkLabel(breakLabel);
            
            // 12. Pop loop context (FOR_ITER 패치가 자동으로 수행됨)
            PopLoopContext();
        }
        
        /// <summary>
        /// Compile for loop with complex tuple unpacking (e.g., for i, (name, value) in enumerate(tests):)
        /// </summary>
        private void CompileForComplex(ForComplexStatement forComplexStmt)
        {
            // CPython approach with complex tuple unpacking support
            
            // 1. Get iterator from iterable
            CompileExpression(forComplexStmt.Iter);  // Push iterable on stack
            EmitInstruction(ByteCodeOp.GET_ITER); // Convert to iterator
            
            // 2. Loop start - FOR_ITER will handle next() and StopIteration
            var forIterInstruction = _instructions.Count;
            EmitInstruction(ByteCodeOp.FOR_ITER, 0); // Jump target will be patched later
            
            // 3. FOR_ITER pushes the next value on stack
            // Now we need to compile the complex target assignment
            CompileComplexAssignTarget(forComplexStmt.Target);
            
            // 4. Set up loop context with FOR_ITER tracking
            var breakLabel = CreateLabel("for_break");
            var continueLabel = CreateLabel("for_continue");
            PushLoopContext(breakLabel, continueLabel, forIterInstruction);
            
            // 5. Execute loop body
            foreach (var stmt in forComplexStmt.Body)
            {
                CompileStatement(stmt);
            }
            
            // 6. Mark continue label
            MarkLabel(continueLabel);
            
            // 7. Jump back to FOR_ITER - CPython 3.12 style relative offset
            int currentPos = _instructions.Count;
            int jumpBackwardArg = CalculateJumpBackwardArg(currentPos, forIterInstruction);
            EmitInstruction(ByteCodeOp.JUMP_BACKWARD, jumpBackwardArg);
            
            // 8. CPython 3.12 방식: END_FOR 추가 (통합 구조)
            EmitInstruction(ByteCodeOp.END_FOR, 0);
            
            // 9. Loop completed normally - execute else clause if present
            if (forComplexStmt.ElseClause != null && forComplexStmt.ElseClause.Count > 0)
            {
                foreach (var stmt in forComplexStmt.ElseClause)
                {
                    CompileStatement(stmt);
                }
            }
            
            // 10. Mark break label
            MarkLabel(breakLabel);
            
            // 11. Pop loop context (FOR_ITER 패치가 자동으로 수행됨)
            PopLoopContext();
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
                Console.WriteLine($"🚀 Type-specialized: {specializedBinOp.LeftType} {specializedBinOp.Operator} {specializedBinOp.RightType} → {opCode}");
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
                Console.WriteLine($"🚀 Method inlined: {inlinedCall.ObjectType}.{inlinedCall.MethodName}() → {opCode}");
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
        /// CPython 3.12 Exception Table based try-except compilation
        /// </summary>
        private void CompileTry(TryStatement tryStmt)
        {
            // CPython 3.12: No SETUP_EXCEPT, use Exception Table instead
            Console.WriteLine($"🔧 Compiling try-except (CPython 3.12 style)");
            
            // Create label for continuation after entire try-except construct
            var continueLabel = CreateLabel("continue_after_try");
            
            // CPython 3.12: Add NOP instruction before try body (exact CPython pattern)
            EmitInstruction(ByteCodeOp.NOP);
            
            // Exception Table start offset is after NOP - 명령어 인덱스 사용 (CPython 호환)
            var tryStartOffset = _instructions.Count;
            
            // CPython 3.12: Direct compilation of try body (no SETUP_EXCEPT)
            foreach (var stmt in tryStmt.Body)
            {
                CompileStatement(stmt);
            }
            
            var tryEndOffset = _instructions.Count;
            
            // CPython 3.12: Jump to continuation if no exception (try body completed normally)
            EmitJumpToLabel(ByteCodeOp.JUMP_FORWARD, continueLabel);
            
            // Exception handler start (where PUSH_EXC_INFO will jump to)
            var handlersStartLabel = CreateLabel("handlers_start");
            MarkLabel(handlersStartLabel);
            
            // CPython 3.12: Exception path starts with PUSH_EXC_INFO
            EmitInstruction(ByteCodeOp.PUSH_EXC_INFO);
            
            // Compile exception handlers sequentially
            for (int i = 0; i < tryStmt.Handlers.Count; i++)
            {
                var handler = tryStmt.Handlers[i];
                var nextHandlerLabel = (i < tryStmt.Handlers.Count - 1) 
                    ? CreateLabel($"handler_{i+1}")
                    : CreateLabel("reraise");
                
                if (handler.Type != null)
                {
                    // Type-specific handler
                    EmitInstruction(ByteCodeOp.COPY, 1);
                    CompileExpression(handler.Type);
                    
                    if (handler.IsStar)
                    {
                        // PEP 654: Exception group matching
                        EmitInstruction(ByteCodeOp.CHECK_EG_MATCH);
                        
                        if (handler.Name != null)
                        {
                            // CPython 3.12: Exception variables - STORE_NAME for module level, STORE_FAST for function level
                            if (_isInFunction)
                            {
                                EmitInstruction(ByteCodeOp.STORE_FAST, GetOrAddVarName(handler.Name));
                            }
                            else
                            {
                                EmitInstruction(ByteCodeOp.STORE_NAME, GetOrAddName(handler.Name));
                            }
                        }
                        else
                        {
                            EmitInstruction(ByteCodeOp.POP_TOP);
                        }
                        
                        EmitInstruction(ByteCodeOp.COPY, 1);
                        EmitInstruction(ByteCodeOp.LOAD_CONST, AddConstant(PyNone.Instance));
                        EmitInstruction(ByteCodeOp.COMPARE_OP, 3); // IS_NOT
                        
                        EmitInstruction(ByteCodeOp.POP_JUMP_IF_FALSE, 0);
                        nextHandlerLabel.References.Add(_instructions.Count - 1);
                    }
                    else
                    {
                        // Regular exception matching - CPython 3.12 uses CHECK_EXC_MATCH
                        EmitInstruction(ByteCodeOp.CHECK_EXC_MATCH);
                        
                        EmitInstruction(ByteCodeOp.POP_JUMP_IF_FALSE, 0);
                        nextHandlerLabel.References.Add(_instructions.Count - 1);
                        
                        if (handler.Name != null)
                        {
                            // CPython 3.12: Exception variables - STORE_NAME for module level, STORE_FAST for function level
                            if (_isInFunction)
                            {
                                EmitInstruction(ByteCodeOp.STORE_FAST, GetOrAddVarName(handler.Name));
                            }
                            else
                            {
                                EmitInstruction(ByteCodeOp.STORE_NAME, GetOrAddName(handler.Name));
                            }
                        }
                        else
                        {
                            EmitInstruction(ByteCodeOp.POP_TOP);
                        }
                    }
                }
                else
                {
                    // Bare except - catches everything
                    if (handler.Name != null)
                    {
                        // CPython 3.12: Exception variables are stored as local variables (STORE_FAST)
                        EmitInstruction(ByteCodeOp.STORE_FAST, GetOrAddVarName(handler.Name));
                    }
                    else
                    {
                        EmitInstruction(ByteCodeOp.POP_TOP);
                    }
                }
                
                // Execute handler body
                foreach (var stmt in handler.Body)
                {
                    CompileStatement(stmt);
                }
                
                // CPython 3.12: POP_EXCEPT after handler execution
                EmitInstruction(ByteCodeOp.POP_EXCEPT);
                
                // CPython 3.12: Exception handler completion - always JUMP_FORWARD to continue after try-except
                // Delete variable binding (for 'as' variable) if needed
                if (handler.Name != null)
                {
                    EmitInstruction(ByteCodeOp.LOAD_CONST, AddConstant(PyNone.Instance));
                    if (_isInFunction)
                    {
                        EmitInstruction(ByteCodeOp.STORE_FAST, GetOrAddVarName(handler.Name));
                        // CPython 3.12: DELETE_FAST for exception variables in functions
                        EmitInstruction(ByteCodeOp.DELETE_FAST, GetOrAddVarName(handler.Name));
                    }
                    else
                    {
                        EmitInstruction(ByteCodeOp.STORE_NAME, GetOrAddName(handler.Name));
                        // CPython 3.12: DELETE_NAME for exception variables at module level
                        EmitInstruction(ByteCodeOp.DELETE_NAME, GetOrAddName(handler.Name));
                    }
                }
                
                // CPython 3.12: Always JUMP_FORWARD to continuation after exception handling
                EmitJumpToLabel(ByteCodeOp.JUMP_FORWARD, continueLabel);
                
                // Mark next handler if not last
                if (i < tryStmt.Handlers.Count - 1)
                {
                    MarkLabel(nextHandlerLabel);
                }
            }
            
            // Reraise if no handler matched (before continuation point)
            if (tryStmt.Handlers.Count > 0)
            {
                var reraiseLabel = CreateLabel("reraise");
                MarkLabel(reraiseLabel);
                EmitInstruction(ByteCodeOp.RERAISE, 1);
            }
            
            // CPython 3.12: Create Exception Table entries (both try block and handler block)
            
            // 1. Main try block entry
            var tryBlockEntry = new ExceptionTableEntry(
                start: tryStartOffset,
                end: tryEndOffset,
                handlerLabel: handlersStartLabel.Name,
                depth: 0,  // Stack depth when exception occurs
                lasti: false  // First entry is not lasti
            );
            _exceptionTable.Add(tryBlockEntry);
            
            // 2. Handler block entry (CPython 3.12 pattern: handlers need their own protection)
            if (tryStmt.Handlers.Count > 0)
            {
                // Find actual handler range by marking current position
                var currentPos = _instructions.Count;
                
                // The handler block starts from PUSH_EXC_INFO (after handlersStartLabel)
                // Since MarkLabel() sets the offset, we can use it directly
                var handlerStartOffset = handlersStartLabel.Offset;
                
                // Handler block ends where the current reraise block begins
                // We need to go back to find the end of the actual handler body
                var handlerEndOffset = currentPos - 1;  // Before current RERAISE
                
                // Create separate reraise handler for exceptions in the exception handler
                var handlerReraiseLabel = CreateLabel("handler_reraise");
                MarkLabel(handlerReraiseLabel);
                EmitInstruction(ByteCodeOp.COPY, 3);
                EmitInstruction(ByteCodeOp.POP_EXCEPT);
                EmitInstruction(ByteCodeOp.RERAISE, 1);
                
                var handlerBlockEntry = new ExceptionTableEntry(
                    start: handlerStartOffset,
                    end: handlerEndOffset, 
                    handlerLabel: handlerReraiseLabel.Name,
                    depth: 1,  // Higher depth for nested exception handling
                    lasti: true  // Second entry has lasti flag
                );
                _exceptionTable.Add(handlerBlockEntry);
                
                Console.WriteLine($"🔧 Handler Exception Table: {handlerStartOffset} to {handlerEndOffset} -> {handlerReraiseLabel.Name} [depth=1, lasti]");
            }
            
            // Mark continuation point AFTER all exception handling code - this is where normal execution continues after try-except
            MarkLabel(continueLabel);
            
            Console.WriteLine($"🔧 Exception Table Entries Created:");
            Console.WriteLine($"   Try Block: {tryStartOffset} to {tryEndOffset} -> {handlersStartLabel.Name}");
            if (tryStmt.Handlers.Count > 0)
            {
                Console.WriteLine($"   Handler Block: handler range -> handler_reraise [lasti]");
            }
        }
        private void CompileWith(WithStatement withStmt)
        {
            // CPython 3.12 compatible implementation
            // Support both single and multiple context managers
            if (withStmt.Items.Count == 1)
            {
                // Single context manager - original implementation
                CompileSingleWith(withStmt);
            }
            else if (withStmt.Items.Count > 1)
            {
                // Multiple context managers - transform to nested with statements
                CompileMultipleWith(withStmt);
            }
            else
            {
                throw PySyntaxError.Create("with statement requires at least one context manager");
            }
        }
        
        /// <summary>
        /// Compile single context manager (original implementation)
        /// </summary>
        private void CompileSingleWith(WithStatement withStmt)
        {
            var item = withStmt.Items[0];
            
            // CPython 3.12 approach with proper exception handling:
            // 1. Load context manager
            CompileExpression(item.ContextExpr);
            
            // 2. BEFORE_WITH: Load __exit__ to stack, call __enter__(), push result
            EmitInstruction(ByteCodeOp.BEFORE_WITH);
            
            // 3. CPython 3.12: Handle __enter__ result immediately after BEFORE_WITH
            if (item.OptionalVars != null)
            {
                // For simple variables, just emit STORE_NAME
                if (item.OptionalVars is NameExpression varExpr)
                {
                    EmitInstruction(ByteCodeOp.STORE_NAME, AddName(varExpr.Name));
                }
                else
                {
                    // Complex assignment target not supported yet
                    EmitInstruction(ByteCodeOp.POP_TOP);
                }
            }
            else
            {
                // Discard __enter__ result if no 'as' clause - CPython 3.12 does this immediately
                EmitInstruction(ByteCodeOp.POP_TOP);
            }
            
            // 4. Setup Exception Table entry (CPython 3.12 compatible)
            var withCleanupLabel = CreateLabel("with_cleanup");
            
            // CPython 3.12: Exception Table은 POP_TOP 이후부터 body 끝까지
            // 실제로 보호받는 코드는 with body만 해당
            var bodyStartOffset = _instructions.Count; // POP_TOP 이후 위치 = with body 시작
            
            // 5. Execute body
            foreach (var stmt in withStmt.Body)
            {
                CompileStatement(stmt);
            }
            
            var bodyEndOffset = _instructions.Count; // body 끝 위치
            
            // 6. Register Exception Table entry (CPython 3.12 style - Label based)  
            // CPython 3.12: 실제 보호받는 코드 범위는 with body만 해당
            var entry = new ExceptionTableEntry(
                start: bodyStartOffset,
                end: bodyEndOffset, 
                handlerLabel: withCleanupLabel.Name,
                depth: 1,
                lasti: true
            );
            _exceptionTable.Add(entry);
            
            Console.WriteLine($"🔧 Exception Table Entry Created (Label-based):");
            Console.WriteLine($"   Start: {bodyStartOffset}, End: {bodyEndOffset}");
            Console.WriteLine($"   Handler Label: {withCleanupLabel.Name}, Depth: 1");
            
            // 7. Normal exit: call __exit__(None, None, None) - no POP_EXCEPT needed
            // CPython 3.12: 동일한 None 상수를 재사용 (상수 풀 효율성)
            var noneConstIndex = AddConstant(PyNone.Instance);
            EmitInstruction(ByteCodeOp.LOAD_CONST, noneConstIndex);
            EmitInstruction(ByteCodeOp.LOAD_CONST, noneConstIndex); 
            EmitInstruction(ByteCodeOp.LOAD_CONST, noneConstIndex);
            // CPython 3.12: __exit__(exc_type, exc_val, exc_tb) - CALL 2 (CPython과 동일)
            EmitInstruction(ByteCodeOp.CALL, 2);
            EmitInstruction(ByteCodeOp.POP_TOP); // discard __exit__ return value
            
            var endLabel = CreateLabel("with_end");
            EmitInstruction(ByteCodeOp.JUMP_FORWARD, 0);
            endLabel.References.Add(_instructions.Count - 1);
            
            // 7. Exception handler (CPython 3.12: PUSH_EXC_INFO → WITH_EXCEPT_START)
            MarkLabel(withCleanupLabel);
            EmitInstruction(ByteCodeOp.PUSH_EXC_INFO);
            EmitInstruction(ByteCodeOp.WITH_EXCEPT_START);
            EmitInstruction(ByteCodeOp.POP_JUMP_IF_TRUE, 0);
            var suppressLabel = CreateLabel("suppress_exception");
            suppressLabel.References.Add(_instructions.Count - 1);
            
            // Re-raise exception if not suppressed
            EmitInstruction(ByteCodeOp.RAISE_VARARGS, 0);
            
            // Exception suppressed - continue normally (CPython 3.12 compatible)
            MarkLabel(suppressLabel);
            EmitInstruction(ByteCodeOp.POP_TOP);     // First cleanup - remove suppress boolean from WITH_EXCEPT_START
            EmitInstruction(ByteCodeOp.POP_EXCEPT);  // CPython 3.12: Remove exception info (4 items: exc_type, exc_value, exc_tb, lasti)
            EmitInstruction(ByteCodeOp.POP_TOP);     // Additional cleanup - remove exception object
            EmitInstruction(ByteCodeOp.POP_TOP);     // Additional cleanup - depends on with nesting depth
            
            MarkLabel(endLabel);
            
            // CPython 3.12 uses Exception Table instead of SETUP_EXCEPT
            // SharpPy's existing exception handling should work correctly
        }
        
        /// <summary>
        /// Compile multiple context managers: with a, b, c: body
        /// CPython 3.12 approach: Transform to nested with statements recursively
        /// with a, b: body -> with a: (with b: body)
        /// </summary>
        private void CompileMultipleWith(WithStatement withStmt)
        {
            // CPython 3.12: Convert "with a, b, c: body" to nested structure
            // Create nested WithStatement objects and compile recursively
            
            if (withStmt.Items.Count < 2)
            {
                throw new InvalidOperationException("CompileMultipleWith requires at least 2 context managers");
            }
            
            // Take the first context manager
            var outerItem = withStmt.Items[0];
            
            // Create inner with statement with remaining context managers
            var remainingItems = withStmt.Items.Skip(1).ToList();
            WithStatement innerWith;
            
            if (remainingItems.Count == 1)
            {
                // Base case: create simple with statement for the last context manager
                innerWith = new WithStatement(remainingItems, withStmt.Body);
            }
            else
            {
                // Recursive case: create nested with statement
                innerWith = new WithStatement(remainingItems, withStmt.Body);
            }
            
            // Create outer with statement that wraps the inner one
            var outerWith = new WithStatement(
                new List<WithItem> { outerItem },
                new List<Statement> { innerWith }
            );
            
            // Compile the outer with statement (which will recursively compile inner ones)
            CompileSingleWith(outerWith);
        }
        private void CompileMatch(MatchStatement matchStmt)
        {
            // CPython 3.12: Match statement compilation - exact pattern placement
            
            // FOR 루프 컨텍스트 내에서 패턴 매칭인지 확인
            bool inForLoop = IsInForLoopContext();
            if (inForLoop)
            {
                Console.WriteLine("🔍 FOR 루프 컨텍스트 내 패턴 매칭 감지 - 스택 관리 특별 처리");
            }
            
            // CPython 3.12: Don't keep subject on stack, load it per case
            // Note: Subject will be loaded individually for each case
            
            // Create labels for control flow with FOR 루프 컨텍스트 고려
            string labelPrefix = inForLoop ? "forloop_match" : "match";
            var endLabel = CreateLabel($"{labelPrefix}_end_{_labelCounter++}");
            var noMatchLabel = CreateLabel($"{labelPrefix}_no_match_{_labelCounter++}");
            
            // Console.WriteLine($"🔍 CompileMatch: Starting with {matchStmt.Cases.Count} cases");
            
            // CPython 3.12: Sequential pattern tests with proper label placement
            var bodyLabels = new List<Label>();
            var nextPatternLabels = new List<Label>();
            
            // Pre-create body labels and next pattern labels with 네임스페이스 분리
            for (int i = 0; i < matchStmt.Cases.Count; i++)
            {
                bodyLabels.Add(CreateLabel($"{labelPrefix}_body_{i}_{_labelCounter++}"));
                // Each pattern (except last) needs a "next pattern" label
                if (i + 1 < matchStmt.Cases.Count)
                {
                    nextPatternLabels.Add(CreateLabel($"{labelPrefix}_next_pattern_{i+1}_{_labelCounter++}"));
                }
            }
            
            // Compile pattern tests sequentially
            for (int i = 0; i < matchStmt.Cases.Count; i++)
            {
                var matchCase = matchStmt.Cases[i];
                // Console.WriteLine($"🔍 Compiling case {i}: Pattern={matchCase.Pattern?.GetType().Name} - {matchCase.Pattern}");
                
                // Place the "next pattern" label if this is not the first case
                if (i > 0)
                {
                    PlaceLabel(nextPatternLabels[i-1]);
                    // Console.WriteLine($"🔍 Placed label {nextPatternLabels[i-1].Name} at instruction {_instructions.Count}");
                }
                
                // Determine failure jump target
                Label failLabel = (i + 1 < matchStmt.Cases.Count) ? nextPatternLabels[i] : noMatchLabel;
                // Console.WriteLine($"🔍 Case {i}: Fail jump target = {failLabel.Name}");
                
                // Handle Guard patterns specially - CPython 3.12 compatible
                // Console.WriteLine($"🔍 Checking Guard for case {i}: Guard={matchCase.Guard?.GetType().Name} - {matchCase.Guard}");
                if (matchCase.Guard != null)
                {
                    // Guard pattern: CPython 3.12 compatible - load subject per case
                    // Load subject fresh for this case
                    CompileExpression(matchStmt.Subject);
                    
                    // Compile pattern matching - this will bind the variable and consume subject
                    if (!CompilePatternMatch(matchCase.Pattern, failLabel))
                    {
                        EmitJumpToLabel(ByteCodeOp.JUMP_FORWARD, failLabel);
                        continue;
                    }
                    
                    // Stack: [] (after pattern binding consumed subject)
                    // Now evaluate guard condition
                    CompileExpression(matchCase.Guard);
                    EmitJumpToLabel(ByteCodeOp.POP_JUMP_IF_FALSE, failLabel);
                    
                    // No cleanup needed - each case is independent
                }
                else
                {
                    // Regular pattern without guard - CPython 3.12 compatible
                    // Load subject fresh for this case
                    CompileExpression(matchStmt.Subject);
                    
                    if (!CompilePatternMatch(matchCase.Pattern, failLabel))
                    {
                        EmitJumpToLabel(ByteCodeOp.JUMP_FORWARD, failLabel);
                        continue;
                    }
                    
                    // No cleanup needed - each case is independent
                }
                EmitJumpToLabel(ByteCodeOp.JUMP_FORWARD, bodyLabels[i]);
            }
            
            // Compile all case bodies
            for (int i = 0; i < matchStmt.Cases.Count; i++)
            {
                var matchCase = matchStmt.Cases[i];
                PlaceLabel(bodyLabels[i]);
                
                foreach (var stmt in matchCase.Body)
                {
                    CompileStatement(stmt);
                }
                
                EmitJumpToLabel(ByteCodeOp.JUMP_FORWARD, endLabel);
            }
            
            // Place no match label - clean up subject when no case matched  
            PlaceLabel(noMatchLabel);
            EmitInstruction(ByteCodeOp.POP_TOP); // Clean up subject
            
            // Place end label - control flow joins here after match or no match
            PlaceLabel(endLabel);
        }
        
        
        /// <summary>
        /// CPython 3.12: Compile pattern matching using proper opcodes
        /// Returns true if pattern was compiled successfully, false otherwise
        /// </summary>
        private bool CompilePatternMatch(Expression pattern, Label failLabel)
        {
            Console.WriteLine($"🔍 CompilePatternMatch: {pattern?.GetType().Name} - {pattern}");
            switch (pattern)
            {
                case ConstantExpression constExpr:
                    // CPython 3.12: Direct constant comparison
                    // Stack: [subject] -> [subject, constant] -> [subject, result]
                    EmitInstruction(ByteCodeOp.COPY, 1); // Duplicate subject for comparison
                    CompileExpression(constExpr);
                    EmitComparison(CompareOp.EQ);
                    // Stack: [subject, comparison_result]
                    EmitJumpToLabel(ByteCodeOp.POP_JUMP_IF_FALSE, failLabel);
                    // Stack: [subject] (comparison result popped, subject preserved)
                    return true;
                
                case NameExpression nameExpr when nameExpr.Name == "_":
                    // Wildcard pattern - always matches, no binding
                    return true;
                    
                case NameExpression nameExpr:
                    // Variable binding pattern - always matches, binds subject to variable
                    // Stack: [subject] -> [] (subject consumed by store)
                    EmitStoreName(nameExpr.Name);
                    return true;
                    
                case AsPattern asPattern:
                    // CPython 3.12: As pattern (pattern as name)
                    Console.WriteLine($"🔍 AsPattern: {asPattern.Pattern} as {asPattern.Name}");
                    
                    // Stack: [subject] -> [subject] (preserve for variable binding)
                    EmitInstruction(ByteCodeOp.COPY, 1); // Copy subject for variable binding
                    
                    // Compile the inner pattern, which will consume one copy of subject
                    if (!CompilePatternMatch(asPattern.Pattern, failLabel))
                    {
                        return false;
                    }
                    
                    // Stack: [subject] - bind the subject to the 'as' variable
                    EmitStoreName(asPattern.Name);
                    
                    return true;
                
                case BinaryOpExpression binaryOp when binaryOp.Operator == "|":
                    // Handle BinaryOpExpression with OR operator as OrPattern
                    Console.WriteLine($"🔍 BinaryOpExpression OR converted to OrPattern: {binaryOp.Left} | {binaryOp.Right}");
                    var binaryPatterns = new List<Expression> { binaryOp.Left, binaryOp.Right };
                    return CompileOrPatternLogic(binaryPatterns, failLabel);
                    
                case OrPattern orPattern:
                    // CPython 3.12: Or patterns (PEP 634)
                    Console.WriteLine($"🔍 OrPattern detected with {orPattern.Patterns.Count} patterns");
                    return CompileOrPatternLogic(orPattern.Patterns, failLabel);
                    
                    // For simple constant or patterns like: case 1 | 2 | 3:
                    // Generate: subject == 1 or subject == 2 or subject == 3
                    var constantPatterns = new List<ConstantExpression>();
                    
                    // Check if all patterns are constants using more flexible approach
                    bool allConstants = true;
                    foreach (var subPattern in orPattern.Patterns)
                    {
                        // Try multiple ways to identify constant patterns
                        if (subPattern is ConstantExpression constPattern)
                        {
                            constantPatterns.Add(constPattern);
                        }
                        else if (subPattern.GetType().Name.Contains("Constant"))
                        {
                            // Type name contains "Constant" - try cast
                            try 
                            {
                                var castPattern = (ConstantExpression)subPattern;
                                constantPatterns.Add(castPattern);
                            }
                            catch
                            {
                                allConstants = false;
                                break;
                            }
                        }
                        else
                        {
                            allConstants = false;
                            break;
                        }
                    }
                    
                    if (allConstants && constantPatterns.Count > 0)
                    {
                        // CPython 3.12: Use multiple comparisons with OR short-circuiting
                        // Stack: subject
                        var successLabel = CreateLabel("or_match_success");
                        
                        for (int i = 0; i < constantPatterns.Count; i++)
                        {
                            var isLast = (i == constantPatterns.Count - 1);
                            
                            // Duplicate subject for comparison (except for last one)
                            if (!isLast)
                            {
                                EmitInstruction(ByteCodeOp.COPY, 1);
                            }
                            
                            // Compare with constant
                            CompileExpression(constantPatterns[i]);
                            EmitComparison(CompareOp.EQ);
                            
                            // If match, jump to success
                            if (!isLast)
                            {
                                EmitJumpToLabel(ByteCodeOp.POP_JUMP_IF_TRUE, successLabel);
                            }
                            else
                            {
                                // Last comparison - if false, jump to fail
                                EmitJumpToLabel(ByteCodeOp.POP_JUMP_IF_FALSE, failLabel);
                            }
                        }
                        
                        PlaceLabel(successLabel);
                        // Pop subject since pattern matched
                        EmitInstruction(ByteCodeOp.POP_TOP);
                        return true;
                    }
                    else
                    {
                        // Fallback: Handle any number of constant patterns using type inspection
                        var fallbackConstants = new List<ConstantExpression>();
                        bool allFallbackConstants = true;
                        
                        foreach (var fallbackPattern in orPattern.Patterns)
                        {
                            if (fallbackPattern.GetType().Name.Contains("Constant"))
                            {
                                try
                                {
                                    var constPattern = (ConstantExpression)fallbackPattern;
                                    fallbackConstants.Add(constPattern);
                                }
                                catch
                                {
                                    allFallbackConstants = false;
                                    break;
                                }
                            }
                            else
                            {
                                allFallbackConstants = false;
                                break;
                            }
                        }
                        
                        if (allFallbackConstants && fallbackConstants.Count > 0)
                        {
                            // CPython 3.12: Generate comparisons for all patterns
                            var successLabel = CreateLabel("or_match_success");
                            
                            for (int i = 0; i < fallbackConstants.Count; i++)
                            {
                                var isLast = (i == fallbackConstants.Count - 1);
                                
                                // Duplicate subject for comparison (except for last one)
                                if (!isLast)
                                {
                                    EmitInstruction(ByteCodeOp.COPY, 1);
                                }
                                
                                // Compare with constant
                                CompileExpression(fallbackConstants[i]);
                                EmitComparison(CompareOp.EQ);
                                
                                // If match, jump to success
                                if (!isLast)
                                {
                                    EmitJumpToLabel(ByteCodeOp.POP_JUMP_IF_TRUE, successLabel);
                                }
                                else
                                {
                                    // Last comparison - if false, jump to fail
                                    EmitJumpToLabel(ByteCodeOp.POP_JUMP_IF_FALSE, failLabel);
                                }
                            }
                            
                            PlaceLabel(successLabel);
                            // Pop subject since pattern matched
                            EmitInstruction(ByteCodeOp.POP_TOP);
                            return true;
                        }
                        
                        // Complex or patterns not yet supported
                        return false;
                    }
                    
                case SequencePattern sequencePattern:
                    // CPython 3.12: Sequence pattern matching [1, 2, *rest]
                    Console.WriteLine($"🔍 SequencePattern: {sequencePattern.Patterns.Count} patterns");
                    return CompileSequencePattern(sequencePattern, failLabel);
                    
                case MappingPattern mappingPattern:
                    // CPython 3.12: Dictionary pattern matching {"key": value}
                    Console.WriteLine($"🔍 MappingPattern: {mappingPattern.Patterns.Count} patterns");
                    return CompileMappingPattern(mappingPattern, failLabel);
                    
                case CallExpression callExpr:
                    // CPython 3.12: Class pattern matching Point(x, y) -> MATCH_CLASS
                    Console.WriteLine($"🔍 CallExpression (class pattern): {callExpr}");
                    return CompileClassPattern(callExpr, failLabel);
                    
                default:
                    // Unsupported pattern type for now - fallback to old system
                    return false;
            }
        }
        
        /// <summary>
        /// CPython 3.12: Compile pattern matching for Guard patterns - preserves subject on stack
        /// This is similar to CompilePatternMatch but designed for Guard context where subject must remain
        /// </summary>
        private bool CompilePatternMatchForGuard(Expression pattern, Label failLabel)
        {
            // Console.WriteLine($"🔍 CompilePatternMatchForGuard: {pattern?.GetType().Name} - {pattern}");
            switch (pattern)
            {
                case ConstantExpression constExpr:
                    // Guard constant pattern: subject is already copied, so consume the copy for comparison
                    CompileExpression(constExpr);
                    EmitComparison(CompareOp.EQ);
                    EmitJumpToLabel(ByteCodeOp.POP_JUMP_IF_FALSE, failLabel);
                    return true;
                
                case NameExpression nameExpr when nameExpr.Name == "_":
                    // Wildcard pattern - always matches, no binding needed
                    // But we need to consume the copied subject
                    EmitInstruction(ByteCodeOp.POP_TOP);  // consume the copy
                    return true;
                    
                case NameExpression nameExpr:
                    // Guard variable pattern: bind the copied subject to variable
                    // Stack: [original_subject, subject_copy] -> [original_subject]
                    EmitStoreName(nameExpr.Name);
                    return true;
                    
                default:
                    // For other patterns, fall back to regular pattern matching
                    // This might not work perfectly but provides basic functionality
                    return CompilePatternMatch(pattern, failLabel);
            }
        }
        
        /// <summary>
        /// Compile sequence pattern matching like [1, 2, *rest]
        /// </summary>
        private bool CompileSequencePattern(SequencePattern pattern, Label failLabel)
        {
            var patterns = pattern.Patterns;
            
            // Check if pattern has star expressions
            bool hasStarPattern = patterns.Any(p => p is StarPattern);
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
            EmitJumpToLabel(ByteCodeOp.POP_JUMP_IF_FALSE, failLabel);
            
            // 2. Check length constraints
            EmitInstruction(ByteCodeOp.GET_LEN);
            
            if (hasStarPattern)
            {
                // For star patterns: len >= (before + after)
                EmitLoadConst(new PyInt(countBefore + countAfter));
                EmitComparison(CompareOp.GE);
                EmitJumpToLabel(ByteCodeOp.POP_JUMP_IF_FALSE, failLabel);
            }
            else
            {
                // For exact patterns: len == pattern_count
                EmitLoadConst(new PyInt(patterns.Count));
                EmitComparison(CompareOp.EQ);
                EmitJumpToLabel(ByteCodeOp.POP_JUMP_IF_FALSE, failLabel);
            }
            
            // 3. Unpack and match elements
            if (hasStarPattern)
            {
                // Use UNPACK_EX for star patterns
                int arg = countBefore | (countAfter << 8);
                EmitInstruction(ByteCodeOp.UNPACK_EX, arg);
                
                // CPython order: First store star pattern, then match before elements, then after elements
                // Stack after UNPACK_EX: [before_elements..., star_list, after_elements...]
                // CPython immediately stores star pattern first
                
                // 1. Store star pattern first (as CPython does)
                if (starIndex >= 0 && patterns[starIndex] is StarPattern starPat)
                {
                    EmitStoreName(starPat.Name);
                }
                
                // 2. Match before elements (now on top of stack, in forward order)
                for (int i = 0; i < countBefore; i++)
                {
                    var p = patterns[i];
                    
                    if (p is ConstantExpression constExpr)
                    {
                        CompileExpression(constExpr);
                        EmitComparison(CompareOp.EQ);
                        EmitJumpToLabel(ByteCodeOp.POP_JUMP_IF_FALSE, failLabel);
                    }
                    else if (p is NameExpression nameExpr)
                    {
                        EmitStoreName(nameExpr.Name);
                    }
                    else
                    {
                        // Handle nested patterns (mapping, sequence, etc.) recursively
                        if (!CompilePatternMatch(p, failLabel))
                        {
                            return false; // Nested pattern compilation failed
                        }
                    }
                }
                
                // 3. Match after elements (remaining on stack, in forward order)
                for (int i = 0; i < countAfter; i++)
                {
                    var patternIdx = starIndex + 1 + i;  // patterns after star
                    var p = patterns[patternIdx];
                    
                    if (p is ConstantExpression constExpr)
                    {
                        CompileExpression(constExpr);
                        EmitComparison(CompareOp.EQ);
                        EmitJumpToLabel(ByteCodeOp.POP_JUMP_IF_FALSE, failLabel);
                    }
                    else if (p is NameExpression nameExpr)
                    {
                        EmitStoreName(nameExpr.Name);
                    }
                    else
                    {
                        // Handle nested patterns (mapping, sequence, etc.) recursively
                        if (!CompilePatternMatch(p, failLabel))
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
                        EmitJumpToLabel(ByteCodeOp.POP_JUMP_IF_FALSE, failLabel);
                    }
                    else if (p is NameExpression nameExpr)
                    {
                        EmitStoreName(nameExpr.Name);
                    }
                    else
                    {
                        // Handle nested patterns (mapping, sequence, etc.) recursively
                        if (!CompilePatternMatch(p, failLabel))
                        {
                            return false; // Nested pattern compilation failed
                        }
                    }
                }
            }
            
            return true;
        }
        
        /// <summary>
        /// Compile class pattern matching like Point(x, y) using MATCH_CLASS
        /// </summary>
        private bool CompileClassPattern(CallExpression callExpr, Label failLabel)
        {
            // CPython 3.12: Class pattern matching Point(x, y) -> MATCH_CLASS
            // We need to preserve the subject on stack for CompileMatch's POP_TOP
            
            // 1. Duplicate subject for MATCH_CLASS (which consumes it)
            // Stack: [subject] -> [subject, subject]
            EmitInstruction(ByteCodeOp.COPY, 1);
            
            // 2. Load the class to match against  
            // Stack: [subject, subject] -> [subject, subject, class]
            CompileExpression(callExpr.Function); // Load Point class
            
            // 3. Load keyword names tuple (empty for positional-only patterns)
            // Stack: [subject, subject, class] -> [subject, subject, class, kw_names]
            EmitLoadConst(new PyTuple()); // Empty tuple for now (no keyword matching)
            
            // 4. Use MATCH_CLASS with argument count (consumes subject, class, kw_names)
            // Stack: [subject, subject, class, kw_names] -> [subject, result_tuple_or_none]
            var argumentCount = callExpr.Arguments.Count;
            EmitInstruction(ByteCodeOp.MATCH_CLASS, argumentCount);
            
            // 5. Check if match succeeded (None = failure, tuple = success)
            // Stack: [subject, result_tuple_or_none] -> [subject, result_tuple_or_none, result_tuple_or_none]
            EmitInstruction(ByteCodeOp.COPY, 1); // Duplicate result for check
            EmitJumpToLabel(ByteCodeOp.POP_JUMP_IF_NONE, failLabel);
            
            // 6. If successful, unpack the attributes and bind to variables
            // Stack: [subject, result_tuple] -> [subject, attr1, attr2, ...]
            if (argumentCount > 0)
            {
                EmitInstruction(ByteCodeOp.UNPACK_SEQUENCE, argumentCount);
                
                // Bind each argument to its corresponding variable (forward order to match CPython)
                for (int i = 0; i < argumentCount; i++)
                {
                    var arg = callExpr.Arguments[i];
                    if (arg is NameExpression nameExpr)
                    {
                        EmitStoreName(nameExpr.Name);
                    }
                    else
                    {
                        // Non-name patterns not supported yet, just pop
                        EmitInstruction(ByteCodeOp.POP_TOP);
                    }
                }
            }
            else
            {
                // No arguments, just pop the result
                EmitInstruction(ByteCodeOp.POP_TOP);
            }
            
            // Stack now: [subject] - this will be consumed by CompileMatch's POP_TOP
            return true;
        }
        
        private bool CompileOrPatternLogic(List<Expression> patterns, Label failLabel)
        {
            Console.WriteLine($"🔍 CompileOrPatternLogic: {patterns.Count} patterns");
            for (int i = 0; i < patterns.Count; i++)
            {
                Console.WriteLine($"  Pattern {i}: {patterns[i]}");
            }
            
            if (patterns.Count == 1)
            {
                // Single pattern - just delegate
                return CompilePatternMatch(patterns[0], failLabel);
            }
            
            // Flatten nested OR patterns to handle ((1 | 2) | 3) properly
            var flattenedPatterns = new List<Expression>();
            FlattenOrPatterns(patterns, flattenedPatterns);
            
            Console.WriteLine($"🔍 Flattened to {flattenedPatterns.Count} patterns:");
            for (int i = 0; i < flattenedPatterns.Count; i++)
            {
                Console.WriteLine($"  Flattened Pattern {i}: {flattenedPatterns[i]}");
            }
            
            // CPython 3.12: OR pattern with proper jump logic
            // Stack: [subject] - preserve throughout
            
            // Create success label that all patterns jump to when they match
            var successLabel = CreateLabel("or_pattern_success");
            
            // Create labels for each pattern attempt
            var nextPatternLabels = new List<Label>();
            for (int i = 0; i < flattenedPatterns.Count - 1; i++)
            {
                nextPatternLabels.Add(CreateLabel($"or_next_{i}"));
            }
            
            for (int i = 0; i < flattenedPatterns.Count; i++)
            {
                if (i > 0)
                {
                    // Place the label for this pattern attempt
                    PlaceLabel(nextPatternLabels[i - 1]);
                }
                
                var pattern = flattenedPatterns[i];
                var isLastPattern = (i == flattenedPatterns.Count - 1);
                var nextLabel = isLastPattern ? failLabel : nextPatternLabels[i];
                
                if (pattern is ConstantExpression constExpr)
                {
                    // Duplicate subject for comparison
                    EmitInstruction(ByteCodeOp.COPY, 1); // [subject, subject]
                    CompileExpression(constExpr); // [subject, subject, constant]
                    EmitComparison(CompareOp.EQ);  // [subject, comparison_result]
                    
                    if (isLastPattern)
                    {
                        // Last pattern - if false, fail the entire OR
                        EmitJumpToLabel(ByteCodeOp.POP_JUMP_IF_FALSE, failLabel);
                        // If true, OR pattern succeeds - jump to success
                        EmitJumpToLabel(ByteCodeOp.JUMP_FORWARD, successLabel);
                    }
                    else
                    {
                        // Not last pattern - if false, try next pattern
                        EmitJumpToLabel(ByteCodeOp.POP_JUMP_IF_FALSE, nextLabel);
                        // If true, OR pattern succeeds - jump to success
                        EmitJumpToLabel(ByteCodeOp.JUMP_FORWARD, successLabel);
                    }
                }
                else
                {
                    // For non-constant patterns, delegate to individual pattern matching
                    if (!CompilePatternMatch(pattern, nextLabel))
                    {
                        // Pattern compilation failed
                        EmitJumpToLabel(ByteCodeOp.JUMP_FORWARD, failLabel);
                        return false;
                    }
                    else
                    {
                        // Pattern matched - jump to success
                        EmitJumpToLabel(ByteCodeOp.JUMP_FORWARD, successLabel);
                    }
                }
            }
            
            // Place the success label - all patterns that succeed jump here
            PlaceLabel(successLabel);
            return true;
        }
        
        private void FlattenOrPatterns(List<Expression> patterns, List<Expression> result)
        {
            foreach (var pattern in patterns)
            {
                if (pattern is BinaryOpExpression binaryExpr && binaryExpr.Operator == "|")
                {
                    // Recursively flatten nested OR patterns
                    var nestedPatterns = new List<Expression> { binaryExpr.Left, binaryExpr.Right };
                    FlattenOrPatterns(nestedPatterns, result);
                }
                else
                {
                    result.Add(pattern);
                }
            }
        }
        
        private bool ShouldCleanupSubjectAfterMatch(Expression pattern)
        {
            // Patterns that unpack values (sequence, mapping) should not cleanup subject
            // as the unpacked values remain on stack for variable binding
            switch (pattern)
            {
                case SequencePattern _:
                case MappingPattern _:
                    return false; // These patterns leave unpacked values on stack
                    
                case BinaryOpExpression binaryExpr when binaryExpr.Operator == "|":
                    return true; // OR patterns should cleanup subject
                    
                case ConstantExpression _:
                case NameExpression _:
                default:
                    return true; // Simple patterns should cleanup subject
            }
        }
        
        /// <summary>
        /// CPython 3.12: Compile dictionary pattern matching {"key": value}
        /// </summary>
        private bool CompileMappingPattern(MappingPattern pattern, Label failLabel)
        {
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
            EmitJumpToLabel(ByteCodeOp.POP_JUMP_IF_FALSE, failLabel);
            // Stack: [subject] (MATCH_MAPPING leaves subject on stack)
            
            // Step 2: Check minimum length (number of required keys)
            EmitInstruction(ByteCodeOp.GET_LEN);
            CompileExpression(new ConstantExpression(new PyInt(pattern.Patterns.Count)));
            EmitComparison(CompareOp.GE); // >= required count
            EmitJumpToLabel(ByteCodeOp.POP_JUMP_IF_FALSE, failLabel);
            // Stack: [subject]
            
            // Step 3: Create tuple of required keys and match them
            var keysList = pattern.Patterns.Keys.ToList();
            foreach (var key in keysList)
            {
                CompileExpression(new ConstantExpression(new PyString(key)));
            }
            EmitInstruction(ByteCodeOp.BUILD_TUPLE, keysList.Count);
            // Stack: [subject, keys_tuple]
            
            EmitInstruction(ByteCodeOp.MATCH_KEYS);
            // Stack: [subject, values_tuple_or_None]
            
            // Step 4: Check if keys matched (MATCH_KEYS returns None if no match)
            EmitInstruction(ByteCodeOp.COPY, 1); // Copy result for None check
            EmitJumpToLabel(ByteCodeOp.POP_JUMP_IF_NONE, failLabel);
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
                    var subFailLabel = CreateLabel("mapping_pattern_fail");
                    
                    // Compile the nested pattern recursively
                    if (!CompilePatternMatch(valuePattern, subFailLabel))
                    {
                        // If nested pattern compilation fails, cleanup and fail
                        MarkLabel(subFailLabel);
                        return false;
                    }
                    
                    // If nested pattern succeeds, continue with remaining patterns
                    // (Note: nested pattern should handle its own variable bindings)
                }
            }
            // Stack: [subject]
            
            // Step 7: Clean up - remove subject since pattern matched
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
            var endLabel = CreateLabel($"assert_end_{_labelCounter++}");
            
            // If test is true, skip the assertion error
            EmitJumpToLabel(ByteCodeOp.POP_JUMP_IF_TRUE, endLabel);
            
            // Load AssertionError class (CPython 3.12 calling convention)
            EmitInstruction(ByteCodeOp.PUSH_NULL);
            EmitInstruction(ByteCodeOp.LOAD_ASSERTION_ERROR);
            
            if (assert.Msg != null)
            {
                // assert test, msg: AssertionError(msg)
                CompileExpression(assert.Msg);
                EmitInstruction(ByteCodeOp.CALL, 1);
            }
            else
            {
                // assert test: AssertionError()
                EmitInstruction(ByteCodeOp.CALL, 0);
            }
            
            // Raise the AssertionError
            EmitInstruction(ByteCodeOp.RAISE_VARARGS, 1);
            
            // Mark end of assert
            MarkLabel(endLabel);
        }
        private void CompileRaise(RaiseStatement raise)
        {
            if (raise.Exc != null)
            {
                // raise Exception(...) - compile the exception expression
                CompileExpression(raise.Exc);
                EmitInstruction(ByteCodeOp.RAISE_VARARGS, 1);
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
                Console.WriteLine($"🌍 Global variable declared: {name}");
            }
        }
        private void CompileNonlocal(NonlocalStatement nonlocal)
        {
            // CPython 3.12: nonlocal 선언은 컴파일 타임에 스코프 분석에 영향을 줌
            // 런타임 바이트코드는 생성하지 않음
            foreach (var name in nonlocal.Names)
            {
                _nonlocalVars.Add(name);
                Console.WriteLine($"🔗 Nonlocal variable declared: {name}");
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
            
            var endLabel = CreateLabel($"bool_end_{_labelCounter++}");
            
            // Compile all operands except the last with short-circuit logic
            for (int i = 0; i < boolOp.Values.Count - 1; i++)
            {
                CompileExpression(boolOp.Values[i]);
                
                // CPython 3.12: DUP_TOP (similar to COPY 1)
                EmitInstruction(ByteCodeOp.COPY, 1);
                
                if (boolOp.Op == "and")
                {
                    // For 'and': if current value is falsy, jump to end (short-circuit)
                    EmitJumpToLabel(ByteCodeOp.POP_JUMP_IF_FALSE, endLabel);
                }
                else if (boolOp.Op == "or")
                {
                    // For 'or': if current value is truthy, jump to end (short-circuit)
                    EmitJumpToLabel(ByteCodeOp.POP_JUMP_IF_TRUE, endLabel);
                }
                
                // If we didn't short-circuit, pop the duplicate and continue
                EmitInstruction(ByteCodeOp.POP_TOP);
            }
            
            // Compile the last operand (no short-circuit needed)
            CompileExpression(boolOp.Values[boolOp.Values.Count - 1]);
            
            // Place end label
            PlaceLabel(endLabel);
        }
        private void CompileLambda(LambdaExpression lambda)
        {
            // CPython 3.12 compatible lambda compilation
            // Lambda creates an anonymous function object with proper parameter scope
            
            // Create a unique name for the lambda function
            string lambdaName = $"<lambda_{_lambdaCounter++}>";
            
            // CPython 3.12: Extract clean parameter names and default values FIRST
            var cleanParamNames = new List<string>();
            var defaultValues = new List<PyObject>();
            
            foreach (var arg in lambda.Args)
            {
                if (arg.Contains("="))
                {
                    // Parameter with default value: name=defaultValue
                    var parts = arg.Split('=', 2);
                    var paramName = parts[0].Trim();
                    var defaultValueStr = parts[1].Trim();
                    
                    cleanParamNames.Add(paramName);
                    
                    // Parse and evaluate default value at compile time (CPython way)
                    var defaultValue = ParseAndEvaluateDefaultValue(defaultValueStr);
                    defaultValues.Add(defaultValue);
                    
                    Console.WriteLine($"  → Parameter '{paramName}' with default value: {defaultValue}");
                }
                else
                {
                    // Parameter without default value
                    cleanParamNames.Add(arg.Trim());
                    Console.WriteLine($"  → Parameter '{arg}' (no default)");
                }
            }
            
            // Phase 1: Free variable analysis with clean parameter names
            var analyzer = new FreeVariableAnalyzer();
            var (freeVars, cellVars) = analyzer.AnalyzeScope(lambda.Body, cleanParamNames);
            
            Console.WriteLine($"\n🔍 Lambda analysis: {lambdaName}");
            Console.WriteLine($"  Parameters: [{string.Join(", ", lambda.Args)}]");
            Console.WriteLine($"  Clean parameters: [{string.Join(", ", cleanParamNames)}]");
            Console.WriteLine($"  Free variables: [{string.Join(", ", freeVars)}]");
            Console.WriteLine($"  Cell variables: [{string.Join(", ", cellVars)}]");
            
            // Compile lambda body in a separate compiler context
            var lambdaInstructions = new List<ByteCodeInstruction>();
            var lambdaConstants = new List<PyObject>();
            var lambdaNames = new List<string>();
            
            // Save current compiler state
            var tempInstructions = _instructions;
            var tempConstants = _constants;
            var tempNames = _names;
            var tempVarNames = _varNames; // Save current VarNames
            
            // Set up lambda compiler context
            _instructions = lambdaInstructions;
            _constants = lambdaConstants;
            _names = lambdaNames;
            _varNames = new List<string>(); // Fresh VarNames for lambda
            
            // Parameters must be first in VarNames for LOAD_FAST to work
            foreach (var paramName in cleanParamNames)
            {
                _varNames.Add(paramName);
                Console.WriteLine($"  → Added parameter '{paramName}' as FAST variable at index {_varNames.Count - 1}");
            }
            
            // Set up closure compilation if there are free variables
            if (freeVars.Count > 0)
            {
                SetupClosureCompilation(cellVars, freeVars);
            }
            
            // Phase 2: Cell 변수들을 위한 MAKE_CELL 명령어 발행 (람다 파라미터용)
            foreach (var cellVar in cellVars)
            {
                var paramIndex = cleanParamNames.IndexOf(cellVar);
                if (paramIndex >= 0)
                {
                    Console.WriteLine($"  → Making cell for lambda parameter: {cellVar}");
                    EmitInstruction(ByteCodeOp.MAKE_CELL, paramIndex);
                }
            }
            
            // Compile the lambda body expression - parameters will now be recognized as FAST variables
            CompileExpression(lambda.Body);
            EmitInstruction(ByteCodeOp.RETURN_VALUE);
            
            // Restore original compiler context
            _instructions = tempInstructions;
            _constants = tempConstants;
            _names = tempNames;
            _varNames = tempVarNames; // Restore original VarNames
            
            // CPython 3.12: Create function code object with correct VarNames order
            // VarNames = parameters first, then any local variables used in lambda body
            var lambdaVarNames = new List<string>(cleanParamNames);
            
            // Add any additional local variables that were used (beyond parameters)
            foreach (var name in lambdaNames)
            {
                if (!lambdaVarNames.Contains(name))
                {
                    lambdaVarNames.Add(name);
                }
            }
            
            var functionCode = new PyCodeObject(
                lambdaName,
                lambdaInstructions,
                lambdaConstants,
                lambdaNames,
                lambdaVarNames, // VarNames with parameters first
                cleanParamNames.Count, // Use clean parameter count
                freeVars, // Set FreeVars for closure support
                cellVars, // Set CellVars for closure support
                defaultValues: defaultValues, // CPython 3.12: Pass default values
                flags: 0,
                fileName: _currentFileName,
                sourceLines: _sourceLines
            );
            
            Console.WriteLine($"  → Lambda code object created: {lambdaVarNames.Count} variables, {cleanParamNames.Count} parameters, {defaultValues.Count} defaults");
            
            // CPython 3.12: Handle default values if present (스택 순서 1)
            if (defaultValues.Count > 0)
            {
                // Load default values onto stack
                foreach (var defaultValue in defaultValues)
                {
                    EmitLoadConst(defaultValue);
                }
                EmitInstruction(ByteCodeOp.BUILD_TUPLE, defaultValues.Count);
                Console.WriteLine($"  → Built defaults tuple: {defaultValues.Count} defaults");
            }
            
            // Handle closure creation if there are free variables (스택 순서 2)
            if (freeVars.Count > 0)
            {
                Console.WriteLine($"  → Creating closure with {freeVars.Count} free variables");
                
                // Load closure cells for free variables
                foreach (var freeVar in freeVars)
                {
                    // Emit LOAD_CLOSURE for each free variable
                    // For Phase 1, we'll emit a placeholder (Phase 2 will implement proper cell loading)
                    EmitInstruction(ByteCodeOp.LOAD_CLOSURE, 0); // TODO: proper cell index
                }
                
                // Build tuple of closure cells
                EmitInstruction(ByteCodeOp.BUILD_TUPLE, freeVars.Count);
            }
            
            // Load the function code object
            EmitInstruction(ByteCodeOp.LOAD_CONST, AddConstant(functionCode));
            
            // CPython 3.12: MAKE_FUNCTION 플래그 동적 계산
            int flags = 0;
            if (defaultValues.Count > 0)
            {
                flags |= MakeFunctionFlags.DEFAULTS;
            }
            if (freeVars.Count > 0)
            {
                flags |= MakeFunctionFlags.CLOSURE;
            }
            
            Console.WriteLine($"  → MAKE_FUNCTION flags: {flags} (defaults={defaultValues.Count > 0}, closure={freeVars.Count > 0})");
            EmitInstruction(ByteCodeOp.MAKE_FUNCTION, flags);
        }
        
        // Lambda counter for unique names
        private static int _lambdaCounter = 0;
        private void CompileConditional(ConditionalExpression conditional) 
        {
            // CPython 3.12 조건부 표현식: A if B else C
            // CPython은 조건부 표현식에서 코드 중복 방식을 사용함
            // 조건을 평가한 후 각 분기에서 전체 표현식 컨텍스트를 중복 실행
            
            var elseLabel = CreateLabel("conditional_else");
            
            // 1. 조건(B) 평가
            CompileExpression(conditional.Test);
            
            // 2. 조건이 False면 else 부분으로 점프
            EmitInstruction(ByteCodeOp.POP_JUMP_IF_FALSE, 0);
            elseLabel.References.Add(_instructions.Count - 1);
            
            // 3. True 분기: Body 값만 로드 (CPython 3.12 호환성)
            CompileExpression(conditional.Body);
            
            // 4. else 라벨 없이 직접 계속 (CPython처럼 중복 없음)
            // CPython은 여기서 JUMP하지 않고 다음 명령어로 계속감
            // 하지만 우리는 expression context에서 동작해야 하므로 
            // 최소한의 점프 사용
            if (IsInComplexExpression())
            {
                var endLabel = CreateLabel("conditional_end");
                EmitInstruction(ByteCodeOp.JUMP_FORWARD, 0);
                endLabel.References.Add(_instructions.Count - 1);
                
                // 5. False 분기
                MarkLabel(elseLabel);
                CompileExpression(conditional.OrElse);
                
                MarkLabel(endLabel);
            }
            else
            {
                // 단순 표현식의 경우 CPython의 코드 중복 패턴 모방
                // 실제로는 분기 없이 값만 스택에 남김
                MarkLabel(elseLabel);
                CompileExpression(conditional.OrElse);
            }
        }
        
        private bool IsInComplexExpression()
        {
            // 복잡한 표현식 컨텍스트인지 확인하는 간단한 휴리스틱
            // 실제 구현에서는 더 정교한 컨텍스트 추적이 필요
            return true; // 일단 모든 경우를 복잡한 표현식으로 처리
        }
        private void CompileFString(FStringExpression fstring)
        {
            // f-string은 여러 파트로 구성됨: 문자열과 표현식이 번갈아 나타남
            // 각 파트를 컴파일하고 FORMAT_VALUE로 포매팅한 후 BUILD_STRING으로 합침
            
            var values = fstring.Values;
            if (values == null || values.Count == 0)
            {
                // 빈 f-string은 빈 문자열
                EmitLoadConst(new PyString(""));
                return;
            }
            
            // 각 value를 컴파일
            foreach (var value in values)
            {
                CompileExpression(value);
                
                // 상수 문자열이 아닌 경우 FORMAT_VALUE 적용
                if (!(value is ConstantExpression constant && constant.Value is PyString))
                {
                    EmitInstruction(ByteCodeOp.FORMAT_VALUE, 0);
                }
            }
            
            // 모든 부분을 문자열로 연결
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
        
        // Evaluate 메서드 - 나중에 구현
        public PyObject Evaluate(PyScope scope)
        {
            throw new NotImplementedException("PythonCompiler.Evaluate() - 나중에 구현예정");
        }
        
        #region CPython-style Compiler Helper Methods
        
        /// <summary>
        /// Label management for jumps (CPython style)
        /// </summary>
        private class Label
        {
            public string Name { get; }
            public int Offset { get; set; } = -1;
            public bool IsMarked => Offset >= 0;
            public List<int> References { get; } = new();
            
            public Label(string name)
            {
                Name = name;
            }
        }
        
        private Dictionary<string, Label> _labels = new();
        private int _labelCounter = 0;
        
        private Label CreateLabel(string prefix)
        {
            var name = $"{prefix}_{_labelCounter++}";
            var label = new Label(name);
            _labels[name] = label;
            return label;
        }
        
        private void MarkLabel(Label label)
        {
            label.Offset = _instructions.Count;
            Console.WriteLine($"🔍 MarkLabel: {label.Name} → offset {label.Offset}, {label.References.Count} references");
            
            // Update all references to this label
            foreach (var refIndex in label.References)
            {
                var oldInstruction = _instructions[refIndex];
                Console.WriteLine($"🔍 Updating ref {refIndex}: {oldInstruction.OpCode} from arg {oldInstruction.Argument}");
                int argument;
                
                // CPython 3.12 compatible jump addressing
                if (oldInstruction.OpCode == ByteCodeOp.JUMP_FORWARD)
                {
                    // CPython 3.12: JUMP_FORWARD uses relative offset from next instruction
                    // Formula: target_instruction - (current_instruction + 1)
                    argument = label.Offset - (refIndex + 1);
                }
                else if (oldInstruction.OpCode == ByteCodeOp.POP_JUMP_IF_FALSE || 
                         oldInstruction.OpCode == ByteCodeOp.POP_JUMP_IF_TRUE)
                {
                    // CPython 3.12: POP_JUMP_IF_* use relative offset from next instruction
                    // Formula: target_instruction - (current_instruction + 1)
                    argument = label.Offset - (refIndex + 1);
                }
                else if (oldInstruction.OpCode == ByteCodeOp.JUMP_BACKWARD)
                {
                    // CPython 3.12: 통합된 JUMP_BACKWARD 유틸리티 사용
                    argument = PyJumpBackwardUtil.CalculateJumpBackwardOpArg(refIndex, label.Offset, _instructions);
                }
                else
                {
                    // Other jump instructions use absolute offsets
                    argument = label.Offset;
                }
                
                Console.WriteLine($"🔍 Updated to arg {argument} (offset {label.Offset} - {refIndex} - 1 = {label.Offset - (refIndex + 1)})");
                _instructions[refIndex] = new ByteCodeInstruction(oldInstruction.OpCode, argument);
            }
        }
        
        /// <summary>
        /// CPython 3.12 style: Emit jump instruction to a label
        /// </summary>
        private void EmitJumpToLabel(ByteCodeOp jumpOp, Label label)
        {
            EmitInstruction(jumpOp, 0);
            label.References.Add(_instructions.Count - 1);
        }
        
        /// <summary>
        /// CPython 3.12 style: Place/mark a label (alias for MarkLabel for consistency)
        /// </summary>
        private void PlaceLabel(Label label)
        {
            MarkLabel(label);
        }
        
        /// <summary>
        /// CPython 3.12 style: Resolve Exception Table labels to actual offsets
        /// </summary>
        private void ResolveExceptionTable()
        {
            if (!SharpPyConfig.DisassemblyOnlyMode)
            {
                Console.WriteLine($"🔧 Exception Table 해석: {_exceptionTable.Count}개 엔트리");
            }
            
            for (int i = 0; i < _exceptionTable.Count; i++)
            {
                var entry = _exceptionTable[i];
                if (!string.IsNullOrEmpty(entry.HandlerLabelName))
                {
                    // 라벨로부터 실제 오프셋 찾기
                    if (_labels.TryGetValue(entry.HandlerLabelName, out var label) && label.IsMarked)
                    {
                        entry.HandlerOffset = label.Offset;
                        Console.WriteLine($"   ✅ 라벨 '{entry.HandlerLabelName}' → 오프셋 {entry.HandlerOffset}");
                    }
                    else
                    {
                        throw new Exception($"Exception Table: 라벨 '{entry.HandlerLabelName}'을 찾을 수 없음 또는 미배치");
                    }
                }
                else if (entry.HandlerOffset < 0)
                {
                    throw new Exception($"Exception Table: 엔트리 {i}의 핸들러가 해석되지 않음");
                }
            }
        }
        
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
        private void EmitStoreVariable(string name) => EmitStoreName(name);
        private void EmitLoadAttribute(string attrName) => EmitLoadAttr(attrName);
        private void EmitStoreAttribute(string attrName) => EmitStoreAttr(attrName);
        
        
        /// <summary>
        /// Loop context management for break/continue (CPython style)
        /// </summary>
        private class LoopContext
        {
            public Label BreakLabel { get; }
            public Label ContinueLabel { get; }
            public int ForIterInstruction { get; set; } = -1; // FOR_ITER 명령어 위치
            
            public LoopContext(Label breakLabel, Label continueLabel)
            {
                BreakLabel = breakLabel;
                ContinueLabel = continueLabel;
            }
        }
        
        private Stack<LoopContext> _loopStack = new();
        
        private void PushLoopContext(Label breakLabel, Label continueLabel, int forIterInstruction = -1)
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
                var context = _loopStack.Pop();
                
                // FOR_ITER 패치: END_FOR 위치로 점프하도록 수정
                if (context.ForIterInstruction >= 0)
                {
                    int endForPosition = _instructions.Count - 1; // 현재 END_FOR 위치
                    int relativeJump = endForPosition - context.ForIterInstruction - 1;
                    _instructions[context.ForIterInstruction] = new ByteCodeInstruction(ByteCodeOp.FOR_ITER, relativeJump);
                    Console.WriteLine($"    → FOR_ITER 패치 (중첩 루프 지원): loop start {context.ForIterInstruction}, jump offset {relativeJump}, END_FOR at {endForPosition}");
                }
            }
        }
        
        private LoopContext? GetCurrentLoop()
        {
            return _loopStack.Count > 0 ? _loopStack.Peek() : null;
        }
        
        /// <summary>
        /// FOR 루프 컨텍스트 내부인지 확인
        /// </summary>
        private bool IsInForLoopContext()
        {
            return _loopStack.Count > 0;
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
        
        private int GetOrAddConstant(PyObject constant)
        {
            // Use object reference equality for constants
            for (int i = 0; i < _constants.Count; i++)
            {
                if (ReferenceEquals(_constants[i], constant) || 
                    (constant is PyInt intConst && _constants[i] is PyInt existingInt && intConst.Value == existingInt.Value) ||
                    (constant is PyString strConst && _constants[i] is PyString existingStr && strConst.Value == existingStr.Value))
                {
                    return i;
                }
            }
            
            _constants.Add(constant);
            return _constants.Count - 1;
        }
        
        #endregion
        
        #region PEP 709 Comprehension Optimization
        
        /// <summary>
        /// PEP 709 - List comprehension 바이트코드 인라인 최적화
        /// CPython 3.12 호환: LOAD_FAST_AND_CLEAR + SWAP + Exception Table 패턴
        /// </summary>
        private void CompileListComprehension(ListComprehension listComp)
        {
            Console.WriteLine("🚀 PEP 709: List comprehension 바이트코드 인라인 컴파일 (CPython 3.12 호환)");
            
            // CPython 3.12: 컴프리헨션 컨텍스트 시작
            var savedIsInComprehension = _isInComprehension;
            _isInComprehension = true;
            
            // CPython 3.12 패턴: 컴프리헨션 변수 사전 할당 및 정리 - 튜플 언패킹 지원
            var comprehensionVars = new List<string>();
            foreach (var gen in listComp.Generators)
            {
                if (gen.Target is NameExpression nameExpr)
                {
                    comprehensionVars.Add(nameExpr.Name);
                }
                else if (gen.Target is TupleExpression tupleExpr)
                {
                    foreach (var element in tupleExpr.Elements)
                    {
                        if (element is NameExpression elemName)
                        {
                            comprehensionVars.Add(elemName.Name);
                        }
                    }
                }
            }
            
            Console.WriteLine($"🔧 List comprehension vars: {string.Join(", ", comprehensionVars)} (count: {comprehensionVars.Count})");
            
            // 1. First compile the iterator source (CPython 3.12 pattern)
            var firstGenerator = listComp.Generators[0];
            CompileExpression(firstGenerator.Iter);
            EmitInstruction(ByteCodeOp.GET_ITER);
            
            // 2. LOAD_FAST_AND_CLEAR: 모든 컴프리헨션 변수 초기화 (CPython 3.12 패턴)  
            foreach (var varName in comprehensionVars)
            {
                EmitInstruction(ByteCodeOp.LOAD_FAST_AND_CLEAR, GetOrAddVarName(varName));
            }
            
            // 3. SWAP + BUILD_LIST + SWAP 패턴 (CPython 3.12 정확한 순서)
            // CPython 3.12: [iter, var_none] → [var_none, iter] → [var_none, iter, empty_list] → [var_none, empty_list, iter]
            if (comprehensionVars.Count > 0)
            {
                // CPython 3.12: SWAP 값 = 실제 컴프리헨션 변수 개수 + 1
                int swapArg = comprehensionVars.Count + 1;
                Console.WriteLine($"🔧 Initial SWAP: vars={comprehensionVars.Count}, swapArg={swapArg}");
                EmitInstruction(ByteCodeOp.SWAP, swapArg); // [iter, var_none] -> [var_none, iter]
            }
            
            EmitInstruction(ByteCodeOp.BUILD_LIST, 0); // [var_none, iter] -> [var_none, iter, empty_list]
            
            if (comprehensionVars.Count > 0)
            {
                EmitInstruction(ByteCodeOp.SWAP, 2); // [var_none, iter, empty_list] -> [var_none, empty_list, iter]
            }
            
            // 4. 중첩된 루프 컴파일 - CPython 3.12 방식 (첫 번째 generator는 이미 처리됨)
            var exceptionTableStart = _instructions.Count;
            // 첫 번째 generator는 이미 처리했으므로 FOR_ITER부터 시작
            var loopStart = _instructions.Count;
            EmitInstruction(ByteCodeOp.FOR_ITER, 0); // 패치 대상
            
            // 첫 번째 generator의 타겟 변수 저장 - 튜플 언패킹 지원
            if (firstGenerator.Target is NameExpression firstNameExpr)
            {
                EmitStoreComprehensionVar(firstNameExpr.Name, comprehensionVars);
            }
            else if (firstGenerator.Target is TupleExpression tupleExpr)
            {
                // CPython 3.12: 튜플 언패킹 패턴 (k, v) for k, v in items()
                // UNPACK_SEQUENCE + STORE_FAST 패턴 사용
                Console.WriteLine($"🔧 List comprehension: processing tuple unpacking with {tupleExpr.Elements.Count} elements");
                EmitInstruction(ByteCodeOp.UNPACK_SEQUENCE, tupleExpr.Elements.Count);
                
                foreach (var element in tupleExpr.Elements)
                {
                    if (element is NameExpression elemName)
                    {
                        Console.WriteLine($"    → unpacking element: {elemName.Name}");
                        EmitStoreComprehensionVar(elemName.Name, comprehensionVars);
                    }
                    else
                    {
                        throw new Exception("Only name expressions supported in tuple unpacking patterns");
                    }
                }
            }
            
            // 첫 번째 generator의 조건 검사 - CPython 3.12 정확한 패턴
            List<int> conditionJumps = new List<int>();
            
            foreach (var condition in firstGenerator.Ifs)
            {
                CompileExpression(condition);
                conditionJumps.Add(_instructions.Count);
                EmitInstruction(ByteCodeOp.POP_JUMP_IF_TRUE, 0); // 조건이 참이면 LIST_APPEND로 점프
                
                // CPython 패턴: 조건이 거짓이면 바로 JUMP_BACKWARD
                EmitInstruction(ByteCodeOp.JUMP_BACKWARD, 0); // 패치 대상 - FOR_ITER로 돌아감
            }
            
            // CPython 패턴: 조건이 참일 때의 타겟 - LIST_APPEND 준비 
            // POP_JUMP_IF_TRUE는 여기로 점프함
            var listAppendArg = comprehensionVars.Count + 1;
            int listAppendStart = _instructions.Count;
            
            if (listComp.Generators.Count > 1)
            {
                CompileNestedGenerators(listComp.Generators, 1, comprehensionVars, () =>
                {
                    CompileExpression(listComp.Element);
                    EmitInstruction(ByteCodeOp.LIST_APPEND, listAppendArg);
                });
            }
            else
            {
                // 단일 generator인 경우 직접 처리
                // CPython 패턴: element 값 로드 → LIST_APPEND
                CompileExpression(listComp.Element);  // 예: x 값 로드
                EmitInstruction(ByteCodeOp.LIST_APPEND, listAppendArg);
            }
            
            
            // JUMP_BACKWARD - CPython 3.12 통일된 oparg 계산
            // CPython 3.12: 통일된 JUMP_BACKWARD oparg 계산 사용
            int currentPos = _instructions.Count;
            int jumpBackwardArg = CalculateJumpBackwardArg(currentPos, loopStart);
            
            Console.WriteLine($"🔧 JUMP_BACKWARD 컴파일: currentPos={currentPos}, loopStart={loopStart}");
            Console.WriteLine($"   jumpBackwardArg={jumpBackwardArg}");
            
            // CPython 3.12: JUMP_BACKWARD는 바이트 단위 오프셋 사용 (명령어 단위가 아님)
            EmitInstruction(ByteCodeOp.JUMP_BACKWARD, jumpBackwardArg);
            
            // END_FOR 라벨 (FOR_ITER 패치용)
            var endFor = _instructions.Count;
            EmitInstruction(ByteCodeOp.END_FOR);
            
            // FOR_ITER 패치 - CPython 3.12 방식
            var relativeJump = endFor - loopStart - 1;
            _instructions[loopStart] = new ByteCodeInstruction(
                ByteCodeOp.FOR_ITER, 
                relativeJump
            );
            
            // 조건 점프들 패치 - CPython 3.12 패턴
            for (int i = 0; i < conditionJumps.Count; i++)
            {
                int popJumpIndex = conditionJumps[i];
                int jumpBackwardIndex = popJumpIndex + 1;
                
                // POP_JUMP_IF_TRUE: 조건이 참이면 LIST_APPEND로 점프
                int relativeOffset = listAppendStart - popJumpIndex - 1;
                _instructions[popJumpIndex] = new ByteCodeInstruction(
                    ByteCodeOp.POP_JUMP_IF_TRUE, 
                    relativeOffset
                );
                
                // JUMP_BACKWARD: 조건이 거짓이면 FOR_ITER로 돌아감
                int jumpBackArg = CalculateJumpBackwardArg(jumpBackwardIndex, loopStart);
                _instructions[jumpBackwardIndex] = new ByteCodeInstruction(
                    ByteCodeOp.JUMP_BACKWARD, 
                    jumpBackArg
                );
            }
            
            // fallback JUMP_BACKWARD 패치 제거 - 이제 조건문 처리에서 직접 생성함
            
            // 5. 정상 완료 시 스택 정리 - CPython 3.12 패턴
            // END_FOR 이후에 exception table end 설정 (CPython 3.12 호환)
            var exceptionTableEnd = _instructions.Count;
            
            // CPython 3.12: 정상 완료 시 즉시 comprehension 변수 저장 (END_FOR 직후)
            if (comprehensionVars.Count > 0)
            {
                // CPython 3.12: SWAP 값 = 실제 컴프리헨션 변수 개수 + 1 (result list)
                int swapArg = comprehensionVars.Count + 1;
                Console.WriteLine($"🔧 END_FOR SWAP: vars={comprehensionVars.Count}, swapArg={swapArg}");
                EmitInstruction(ByteCodeOp.SWAP, swapArg);
                for (int i = comprehensionVars.Count - 1; i >= 0; i--)
                {
                    // 모듈 레벨에서는 STORE_NAME 사용 (CPython 3.12 호환)
                    if (_isInFunction)
                    {
                        EmitInstruction(ByteCodeOp.STORE_FAST, GetOrAddVarName(comprehensionVars[i]));
                    }
                    else
                    {
                        EmitInstruction(ByteCodeOp.STORE_FAST, GetOrAddVarName(comprehensionVars[i]));
                    }
                }
            }
            
            // CPython 3.12: List comprehension 정상 완료 - 결과 리스트가 스택에 남음
            // Assignment target은 이 지점에서 AssignStatement에 의해 처리됨
            
            // CPython 3.12: Exception handler를 지연 생성으로 등록
            var pendingHandler = new PendingExceptionHandler
            {
                StartOffset = exceptionTableStart * 2,     // 바이트 오프셋으로 변환
                EndOffset = exceptionTableEnd * 2,         // 바이트 오프셋으로 변환
                ComprehensionVars = new List<string>(comprehensionVars),
                Depth = 2
            };
            _pendingExceptionHandlers.Add(pendingHandler);
            
            // CPython 3.12: 컴프리헨션 컨텍스트 종료
            _isInComprehension = savedIsInComprehension;
            
            Console.WriteLine($"✅ List comprehension 바이트코드 CPython 3.12 호환 완료 ({listComp.Generators.Count}개 중첩 generator)");
        }
        
        /// <summary>
        /// CPython 3.12 호환 중첩 Generator 컴파일
        /// 재귀적으로 각 generator에 대해 FOR_ITER 루프를 생성
        /// </summary>
        private void CompileNestedGenerators(List<Comprehension> generators, int currentIndex, 
                                           List<string> comprehensionVars, Action innerBlock)
        {
            if (currentIndex >= generators.Count)
            {
                // 모든 generator 처리 완료 - 내부 블록 실행
                innerBlock();
                return;
            }
            
            var generator = generators[currentIndex];
            Console.WriteLine($"  🔄 Generator [{currentIndex}]: {generator.Target} in {generator.Iter}");
            
            // 이터레이터 준비
            CompileExpression(generator.Iter);
            EmitInstruction(ByteCodeOp.GET_ITER);
            
            // 루프 시작 라벨
            var loopStart = _instructions.Count;
            EmitInstruction(ByteCodeOp.FOR_ITER, 0); // 패치 대상
            
            // 루프 변수 저장 - 컴프리헨션 변수로 관리
            if (generator.Target is NameExpression nameExpr)
            {
                // CPython 3.12: 컴프리헨션 변수는 임시 스코프에 저장
                EmitStoreComprehensionVar(nameExpr.Name, comprehensionVars);
            }
            else if (generator.Target is TupleExpression tupleExpr)
            {
                // CPython 3.12: 튜플 언패킹 패턴 (k, v) for k, v in items()
                // UNPACK_SEQUENCE + STORE_FAST 패턴 사용
                EmitInstruction(ByteCodeOp.UNPACK_SEQUENCE, tupleExpr.Elements.Count);
                
                foreach (var element in tupleExpr.Elements)
                {
                    if (element is NameExpression elemName)
                    {
                        EmitStoreComprehensionVar(elemName.Name, comprehensionVars);
                    }
                    else
                    {
                        throw PyRuntimeError.Create("Only name expressions supported in tuple unpacking patterns");
                    }
                }
            }
            else
            {
                throw PyRuntimeError.Create("Complex target patterns not yet supported");
            }
            
            // 조건 검사 (if문이 있는 경우) - CPython 3.12 패턴: POP_JUMP_IF_TRUE 사용
            List<int> conditionJumps = new List<int>();
            foreach (var condition in generator.Ifs)
            {
                CompileExpression(condition);
                conditionJumps.Add(_instructions.Count);
                EmitInstruction(ByteCodeOp.POP_JUMP_IF_TRUE, 0); // 조건이 참이면 내부 블록으로 점프
                
                // CPython 패턴: 조건이 거짓이면 바로 JUMP_BACKWARD
                EmitInstruction(ByteCodeOp.JUMP_BACKWARD, 0); // 패치 대상 - FOR_ITER로 돌아감
            }
            
            // 다음 generator 재귀 호출
            CompileNestedGenerators(generators, currentIndex + 1, comprehensionVars, innerBlock);
            
            // 다음 generator 재귀 호출 후 JUMP_BACKWARD - CPython 3.12 style relative offset
            // CPython 3.12: 통일된 JUMP_BACKWARD oparg 계산 사용
            int currentPos = _instructions.Count;
            int jumpBackwardArg = CalculateJumpBackwardArg(currentPos, loopStart);
            EmitInstruction(ByteCodeOp.JUMP_BACKWARD, jumpBackwardArg);
            
            // 조건 점프 대상 패치 - CPython 3.12 패턴
            for (int i = 0; i < conditionJumps.Count; i++)
            {
                int popJumpIndex = conditionJumps[i];
                int jumpBackwardIndex = popJumpIndex + 1;
                
                // POP_JUMP_IF_TRUE: 조건이 참이면 내부 블록(다음 generator 또는 LIST_APPEND)으로 점프
                int innerBlockStart = jumpBackwardIndex + 1; // JUMP_BACKWARD 다음부터 내부 블록
                int relativeOffset = innerBlockStart - popJumpIndex - 1;
                _instructions[popJumpIndex] = new ByteCodeInstruction(
                    ByteCodeOp.POP_JUMP_IF_TRUE, 
                    relativeOffset
                );
                
                // JUMP_BACKWARD: 조건이 거짓이면 FOR_ITER로 돌아감
                int jumpBackArg = CalculateJumpBackwardArg(jumpBackwardIndex, loopStart);
                _instructions[jumpBackwardIndex] = new ByteCodeInstruction(
                    ByteCodeOp.JUMP_BACKWARD, 
                    jumpBackArg
                );
            }
            
            // CPython 3.12 방식: FOR_ITER → END_FOR 점프 구조
            // END_FOR에서 루프 종료 시 정리 작업 수행
            
            Console.WriteLine($"🔧 FOR_ITER 패치 전 상태:");
            Console.WriteLine($"    Generator[{currentIndex}]: {generator.Target} in {generator.Iter}");
            Console.WriteLine($"    현재 바이트코드 길이: {_instructions.Count}");
            Console.WriteLine($"    FOR_ITER 위치: {loopStart}");
            
            // END_FOR 명령어 추가 (CPython 3.12 패턴)
            int endForPosition = _instructions.Count;
            EmitInstruction(ByteCodeOp.END_FOR, 0);
            Console.WriteLine($"    END_FOR 추가 위치: {endForPosition}");
            
            // CPython 3.12와 동일한 오프셋 계산
            // FOR_ITER 실행 시: InstructionPointer += argument, 그 후 메인 루프 +1
            // 따라서 END_FOR에 도달하려면: endForPosition - loopStart - 1
            var relativeJump = endForPosition - loopStart - 1;
            var originalInstruction = _instructions[loopStart];
            _instructions[loopStart] = new ByteCodeInstruction(
                ByteCodeOp.FOR_ITER, 
                relativeJump
            );
            Console.WriteLine($"🔧 FOR_ITER 패치 완료:");
            Console.WriteLine($"    위치 {loopStart}: 원래 인수 {originalInstruction.Argument} → 새 인수 {relativeJump}");
            Console.WriteLine($"    점프 계산: END_FOR({endForPosition}) - FOR_ITER({loopStart}) - 1 = {relativeJump}");
            Console.WriteLine($"    VM 실행 시 점프될 위치: {loopStart + 1 + relativeJump}");
            
            // Dict comprehension의 경우 STORE_GLOBAL이 건너뛰어지는 문제 디버깅
            if (_isInComprehension)
            {
                Console.WriteLine($"📋 Comprehension 컨텍스트에서 FOR_ITER 패치:");
                Console.WriteLine($"    다음 명령어들 위치 예상:");
                for (int i = endForPosition + 1; i < Math.Min(endForPosition + 5, _instructions.Count); i++)
                {
                    if (i < _instructions.Count)
                    {
                        Console.WriteLine($"    위치 {i}: {_instructions[i].OpCode} {_instructions[i].Argument}");
                    }
                }
            }
        }
        
        /// <summary>
        /// CPython 3.12 호환 컴프리헨션 변수 저장
        /// 컴프리헨션 내부 변수는 격리된 스코프에서 관리
        /// </summary>
        private void EmitStoreComprehensionVar(string name, List<string> comprehensionVars)
        {
            // 컴프리헨션 변수 목록에 추가 (중복 제거)
            if (!comprehensionVars.Contains(name))
            {
                comprehensionVars.Add(name);
            }
            
            // CPython 3.12 호환: 컴프리헨션 변수는 항상 STORE_FAST로 처리
            // 모듈 레벨에서도 컴프리헨션은 별도의 지역 스코프를 가짐
            var varIndex = GetOrAddVarName(name);
            EmitInstruction(ByteCodeOp.STORE_FAST, varIndex);
            Console.WriteLine($"    → 컴프리헨션 변수 저장: {name} (STORE_FAST index {varIndex})");
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
                    Console.WriteLine($"    → 컴프리헨션 변수 로드: {name} (LOAD_FAST index {varIndex})");
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
        /// </summary>
        private void CompileDictComprehension(DictComprehension dictComp)
        {
            Console.WriteLine("🚀 PEP 709: Dict comprehension 바이트코드 인라인 컴파일 (중첩 Generator 지원)");
            Console.WriteLine($"📊 Dict comprehension 시작 위치: {_instructions.Count}");
            
            // CPython 3.12: 컴프리헨션 컨텍스트 시작
            var savedIsInComprehension = _isInComprehension;
            _isInComprehension = true;
            
            // 1. 빈 딕셔너리 생성
            var buildMapPosition = _instructions.Count;
            EmitInstruction(ByteCodeOp.BUILD_MAP, 0);
            Console.WriteLine($"🔧 BUILD_MAP 위치: {buildMapPosition}");
            
            // 2. 임시 변수 저장을 위한 리스트 - 컴프리헨션 스코프 isolation
            var comprehensionVars = new List<string>();
            
            // 3. 중첩된 루프 컴파일 - CPython 3.12 방식
            Console.WriteLine($"🔄 CompileNestedGenerators 호출 전 위치: {_instructions.Count}");
            CompileNestedGenerators(dictComp.Generators, 0, comprehensionVars, () =>
            {
                // 모든 generator 루프가 완료된 후 실행되는 내부 블록
                var innerBlockStart = _instructions.Count;
                Console.WriteLine($"🎯 Dict comprehension 내부 블록 시작: {innerBlockStart}");
                
                CompileExpression(dictComp.Key);
                CompileExpression(dictComp.Value);
                
                var mapAddPosition = _instructions.Count;
                EmitInstruction(ByteCodeOp.MAP_ADD, 1); // 딕셔너리는 항상 스택의 맨 아래(1)에 위치
                Console.WriteLine($"🗝️ MAP_ADD 위치: {mapAddPosition}");
            });
            
            var afterNestedGenerators = _instructions.Count;
            Console.WriteLine($"🔄 CompileNestedGenerators 완료 후 위치: {afterNestedGenerators}");
            
            // CPython 3.12: 컴프리헨션 컨텍스트 종료
            _isInComprehension = savedIsInComprehension;
            
            Console.WriteLine($"✅ Dict comprehension 바이트코드 인라인 완료 ({dictComp.Generators.Count}개 중첩 generator)");
            Console.WriteLine($"📊 Dict comprehension 최종 위치: {_instructions.Count}");
        }
        
        /// <summary>
        /// PEP 709 - Set comprehension 바이트코드 인라인 최적화
        /// {expr for var in iterable if condition} → 직접 바이트코드 생성
        /// CPython 호환 방식: GET_ITER는 한 번만, 루프는 FOR_ITER부터 시작
        /// </summary>
        private void CompileSetComprehension(SetComprehension setComp)
        {
            Console.WriteLine("🚀 PEP 709: Set comprehension 바이트코드 인라인 컴파일 (중첩 Generator 지원)");
            
            // CPython 3.12: 컴프리헨션 컨텍스트 시작
            var savedIsInComprehension = _isInComprehension;
            _isInComprehension = true;
            
            // 1. 빈 셋 생성
            EmitInstruction(ByteCodeOp.BUILD_SET, 0);
            
            // 2. 임시 변수 저장을 위한 리스트 - 컴프리헨션 스코프 isolation
            var comprehensionVars = new List<string>();
            
            // 3. 중첩된 루프 컴파일 - CPython 3.12 방식
            CompileNestedGenerators(setComp.Generators, 0, comprehensionVars, () =>
            {
                // 모든 generator 루프가 완료된 후 실행되는 내부 블록
                CompileExpression(setComp.Element);
                EmitInstruction(ByteCodeOp.SET_ADD, 1); // 셋은 항상 스택의 맨 아래(1)에 위치
            });
            
            // CPython 3.12: 컴프리헨션 컨텍스트 종료
            _isInComprehension = savedIsInComprehension;
            
            Console.WriteLine($"✅ Set comprehension 바이트코드 인라인 완료 ({setComp.Generators.Count}개 중첩 generator)");
        }
        
        /// <summary>
        /// PEP 709 - Generator expression 바이트코드 인라인 최적화
        /// (expr for var in iterable if condition) → 제너레이터 함수 생성
        /// </summary>
        private void CompileGeneratorExpression(GeneratorExpression genExp)
        {
            Console.WriteLine("🚀 PEP 709: Generator expression 바이트코드 인라인 컴파일");
            
            // 제너레이터는 별도 함수로 컴파일 필요
            var genCompiler = new PythonCompiler();
            var generator = genExp.Generators[0];
            
            // 제너레이터 바디 컴파일 - CPython 3.12 올바른 패턴
            var targetName = generator.Target is NameExpression nameExpr ? nameExpr.Name : "x";
            
            // CPython 3.12 패턴: .0 iterator를 받아서 직접 FOR 루프 실행
            var iteratorExpr = new NameExpression(".0"); // .0 매개변수 (이미 iterator)
            var yieldStatement = new YieldStatement(genExp.Element);
            
            // 제너레이터 바디: for문 + yield
            var forBody = new List<Statement>();
            
            // 조건이 있으면 if문으로 감싸기
            if (generator.Ifs.Count > 0)
            {
                // 모든 조건을 AND로 연결
                Expression combinedCondition = generator.Ifs[0];
                for (int i = 1; i < generator.Ifs.Count; i++)
                {
                    combinedCondition = new BoolOpExpression(
                        "and", 
                        new List<Expression> { combinedCondition, generator.Ifs[i] }
                    );
                }
                
                forBody.Add(new IfStatement(
                    combinedCondition,
                    new List<Statement> { yieldStatement },
                    null
                ));
            }
            else
            {
                forBody.Add(yieldStatement);
            }
            
            // CPython 3.12: for x in .0 (iterator를 직접 사용)
            var genStatements = new List<Statement>
            {
                new ForStatement(targetName, iteratorExpr, forBody)
            };
            
            // CPython 3.12: 제너레이터 표현식은 iterator를 .0 매개변수로 받음
            var parameters = new List<string> { ".0" };  // 매개변수는 .0 하나
            var defaults = new List<PyObject>();  // 기본값 없음
            var flags = PyCodeObject.CO_GENERATOR;  // CO_GENERATOR 플래그 설정
            var genCode = genCompiler.CompileFunction(genStatements, "<genexpr>", parameters, defaults, flags);
            
            // 제너레이터 함수 객체 생성
            EmitLoadConst(genCode);
            EmitInstruction(ByteCodeOp.MAKE_FUNCTION, 0);
            
            // CPython 3.12: 올바른 스택 순서로 호출
            CompileExpression(generator.Iter);  // range(5) 컴파일  
            EmitInstruction(ByteCodeOp.GET_ITER);  // iterator 생성
            EmitInstruction(ByteCodeOp.CALL, 0);  // 제너레이터 함수 호출 (iterator는 특별 처리)
            
            Console.WriteLine("✅ Generator expression 바이트코드 인라인 완료");
        }
        
        // CPython 3.12: Assignment target compilation
        private void CompileAssignTarget(AssignTargetStatement assignTarget)
        {
            // Compile the value first
            CompileExpression(assignTarget.Value);
            
            // Handle different assignment targets
            switch (assignTarget.Target)
            {
                case NameExpression name:
                    EmitStoreName(name.Name);
                    break;
                    
                case AttributeExpression attr:
                    CompileExpression(attr.Value);
                    EmitStoreAttr(attr.Attr);
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
                    // Check if this is starred unpacking (contains StarExpression)
                    var starIndex = tuple.Elements.FindIndex(e => e is StarExpression);
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
                    
                case AttributeExpression attr:
                    CompileExpression(attr.Value);
                    EmitStoreAttr(attr.Attr);
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
                    // Nested tuple unpacking: (a, (b, c)) = (1, (2, 3))
                    EmitInstruction(ByteCodeOp.UNPACK_SEQUENCE, tuple.Elements.Count);
                    for (int i = 0; i < tuple.Elements.Count; i++)
                    {
                        var element = tuple.Elements[i];
                        CompileAssignmentTarget(element);
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
        /// Emit STORE_DEREF for cell variables (PEP 695)
        /// </summary>
        private void EmitStoreDeref(string varName)
        {
            var index = _cellVars.IndexOf(varName);
            if (index == -1)
                throw new Exception($"Variable '{varName}' not found in cell variables");
            EmitInstruction(ByteCodeOp.STORE_DEREF, index);
        }
        
        /// <summary>
        /// Emit LOAD_DEREF for cell variables (PEP 695)
        /// </summary>
        private void EmitLoadDeref(string varName)
        {
            var index = _cellVars.IndexOf(varName);
            if (index == -1)
                throw new Exception($"Variable '{varName}' not found in cell variables");
            EmitInstruction(ByteCodeOp.LOAD_DEREF, index);
        }
        
        /// <summary>
        /// Emit LOAD_CLOSURE for creating closure tuples (PEP 695)
        /// </summary>
        private void EmitLoadClosure(string varName)
        {
            var index = _cellVars.IndexOf(varName);
            if (index == -1)
                throw new Exception($"Variable '{varName}' not found in cell variables");
            EmitInstruction(ByteCodeOp.LOAD_CLOSURE, index);
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
        /// 현재 명령어 리스트의 정확한 바이트 오프셋 계산
        /// CPython 3.12 인라인 캐시를 포함한 실제 바이트 크기
        /// </summary>
        private int CalculateCurrentByteOffset()
        {
            int totalBytes = 0;
            
            foreach (var instruction in _instructions)
            {
                totalBytes += PyJumpBackwardUtil.GetCPythonInstructionSize(instruction.OpCode, instruction.Argument);
            }
            
            return totalBytes;
        }
        
        /// <summary>
        /// 현재 설정에 기반하여 최적화 레벨을 결정
        /// </summary>
        private OptimizationLevel GetOptimizationLevel()
        {
            // SharpPyConfig 설정에 기반하여 최적화 레벨 결정
            if (SharpPyConfig.DisableOptimizer)
                return OptimizationLevel.Disabled;
                
            // 환경변수나 설정에 따라 레벨 조정 가능
            // Phase 2 테스트를 위해 TypeAware 레벨 사용
            return OptimizationLevel.TypeAware;
        }
        
        #endregion
    }

    #endregion
}