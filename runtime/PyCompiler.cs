using System.Linq;
using SharpPy.Utils;

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
            var freeVars = _usedVars.Except(_definedVars)
                                   .Except(_globalVars)  // CPython 3.12: global 변수는 자유 변수가 아님
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
                    if (!PyToken.IsKeywordLexeme(name.Name))
                    {
                        _usedVars.Add(name.Name);
                    }
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

                case WalrusExpression walrus:
                    // Analyze the value expression first
                    AnalyzeExpression(walrus.Value);
                    // Mark the target variable as defined (like normal assignment)
                    _definedVars.Add(walrus.Target);
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

        // Note: HasSuperCalls methods removed - now using ContainsSuperCalls instead for consistency
    }

    // AST를 바이트코드로 컴파일 (기존 시스템과 연동)
    public class PythonCompiler
    {

        private bool _enable_optimizer {
            get {
                bool result = !SharpPyConfig.DisableOptimizer;
#if DEBUG_LOG
                Console.WriteLine($"🔧 _enable_optimizer: {result} (DisableOptimizer: {SharpPyConfig.DisableOptimizer})");
#endif
                return result;
            }
        }   // CPython 3.12 compatibility with 2-byte addressing
        private List<ByteCodeInstruction> _instructions;
        private List<PyObject> _constants;
        private List<string> _names;
        private List<string> _varNames;

        /// <summary>
        /// PythonCompiler constructor - ensures proper initialization
        /// </summary>
        public PythonCompiler()
        {
            // Initialize all essential lists to prevent null reference issues
            _instructions = new List<ByteCodeInstruction>();
            _constants = new List<PyObject>();
            _names = new List<string>();
            _varNames = new List<string>();
            _exceptionTable = new List<ExceptionTableEntry>();
            _lineNumberTable = new Dictionary<int, int>();
        }
        private bool _isInFunction = false; // Track if we're compiling inside a function
        private string _currentFunctionName = null; // Track current function name for module level detection
        private bool _isInComprehension = false; // Track if we're compiling inside a comprehension
        private int _comprehensionNestingDepth = 0; // Track nesting depth for dict comprehensions
        
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
        
        public PyCodeObject Compile(List<Statement> statements, string name, List<string> parameters, string? fileName = null)
        {
            // Clear all compilation state for new compilation
            _instructions.Clear();
            _constants.Clear();
            _names.Clear();
            _varNames.Clear();
            _exceptionTable.Clear(); // Reset Exception Table
            _lineNumberTable.Clear(); // Reset line number table

            // Set current file name for source location tracking
            _currentFileName = fileName;

            // CPython 3.12: Build symbol table first
            var symbolTableBuilder = new SymbolTableBuilder();
            _symbolTable = symbolTableBuilder.BuildSymbolTable(statements, name);
            _currentSymbolTable = _symbolTable;

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
            
            foreach (var statement in optimizedStatements)
            {
                CompileStatement(statement);
            }
            
            // CPython 3.12: 모듈은 RETURN_CONST로 None 반환 (Exception Handler 이전에)
            var noneConstIndex = GetOrAddConstant(PyNone.Instance);
            EmitInstruction(ByteCodeOp.RETURN_CONST, noneConstIndex);

            // CPython 3.12: 지연된 exception handler들을 바이트코드 끝에 생성
            GeneratePendingExceptionHandlers();

            var codeObject = new PyCodeObject(name, _instructions, _constants, _names, _varNames, parameters.Count, 0, null, null, null, 0, _currentFileName, _sourceLines, false, _lineNumberTable);
            
            // Resolve Exception Table labels to offsets (CPython 3.12 compatible)
            ResolveExceptionTable();
            
            // Add Exception Table entries (CPython 3.12 compatible)
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
            Console.WriteLine($"✅ 컴파일 완료: {_instructions.Count}개 명령어");
#endif
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
            // Clear all compilation state for new compilation
            _instructions.Clear();
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
            // CPython 3.12: MAKE_CELL uses CellVars index order (0, 1, 2...)
            for (int cellIndex = 0; cellIndex < cellVars.Count; cellIndex++)
            {
                var cellVar = cellVars[cellIndex];
                if (parameters.Contains(cellVar))
                {
                    if (!SharpPyConfig.DisassemblyOnlyMode)
                    {
#if DEBUG_LOG
                        Console.WriteLine($"  → Making cell for parameter: {cellVar} (cell index {cellIndex})");
#endif
                    }
                    EmitInstruction(ByteCodeOp.MAKE_CELL, cellIndex);
                }
            }
            
            foreach (var statement in statements)
            {
                CompileStatement(statement);
            }
            
            // CPython 3.12: 모듈은 RETURN_CONST로 None 반환
            var noneConstIndex = GetOrAddConstant(PyNone.Instance);
            EmitInstruction(ByteCodeOp.RETURN_CONST, noneConstIndex);
            
            var codeObject = new PyCodeObject(name, _instructions, _constants, _names, _varNames,
                                            parameters.Count, 0, freeVars, cellVars, null, 0, _currentFileName, _sourceLines);
            
            // Add Exception Table entries (CPython 3.12)
            codeObject.ExceptionTable.AddRange(_exceptionTable);
            if (!SharpPyConfig.DisassemblyOnlyMode)
            {
#if DEBUG_LOG
                Console.WriteLine($"\u2705 컴파일 완료: {_instructions.Count}개 명령어");
#endif
            }
            
            // 바이트코드 최적화 적용
            var optimizer = new ByteCodeOptimizer(_enable_optimizer);
            var optimizedCode = optimizer.OptimizeCode(codeObject);
            
            return optimizedCode;
        }
        
        /// <summary>
        /// CPython 호환: 매개변수 문자열에서 이름과 기본값 분리
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
        /// Async function body 컴파일 - CO_COROUTINE 플래그 추가
        /// </summary>
        private PyCodeObject CompileAsyncFunctionBody(AsyncFunctionDefStatement asyncFunc, List<string> freeVars, List<string> cellVars)
        {
            var (paramNames, defaults, flags, argCount, posonlyArgCount, annotations) = ParseAsyncFunctionParameters(asyncFunc.Parameters);
            
            // CO_COROUTINE 플래그 추가
            flags |= PyCodeObject.CO_COROUTINE;
            
            var compiler = new PythonCompiler();
            compiler.SetupClosureCompilation(cellVars, freeVars);
            var codeObject = compiler.CompileWithClosureAndDefaults(asyncFunc.Body, asyncFunc.Name, paramNames, defaults, freeVars, cellVars, flags, argCount, posonlyArgCount);
            
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
                    codeObject.PosonlyArgCount,
                    codeObject.FreeVars,
                    codeObject.CellVars,
                    codeObject.DefaultValues,
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
        /// CPython 호환: 클로저와 기본값을 모두 지원하는 컴파일
        /// </summary>
        public PyCodeObject CompileWithClosureAndDefaults(List<Statement> statements, string name, List<string> paramNames, List<PyObject> defaults, List<string> freeVars, List<string> cellVars, int flags = 0, int argCount = -1, int posonlyArgCount = 0)
        {
            // Clear all compilation state for new compilation
            _instructions.Clear();
            _constants.Clear();
            _names.Clear();
            _varNames.Clear();
            _exceptionTable.Clear(); // Reset Exception Table
            _lineNumberTable.Clear(); // Reset line number table
            _isInFunction = true; // We are now compiling inside a function
            _currentFunctionName = name; // Track function name for module level detection

            // Calculate correct argCount for CPython 3.12 compatibility early
            int finalArgCount = (argCount >= 0) ? argCount : paramNames.Count;
            
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
            
#if DEBUG_LOG
            Console.WriteLine($"\n🔧 컴파일 (클로저+기본값): {name}");
#endif
#if DEBUG_LOG
            Console.WriteLine($"  매개변수: [{string.Join(", ", paramNames)}]");
#endif
            #if DEBUG_LOG
            Console.WriteLine($"  기본값: [{string.Join(", ", defaults.Select(d => d?.ToString() ?? "None"))}]");
            #endif
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
            
            // CPython 3.12: COPY_FREE_VARS for functions with free variables (MUST be first instruction)
            if (freeVars.Count > 0)
            {
                if (!SharpPyConfig.DisassemblyOnlyMode)
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"  → Emitting COPY_FREE_VARS for {freeVars.Count} free variables");
                    #endif
                }
                EmitCopyFreeVars(freeVars.Count);
            }

            // Phase 2: Cell 변수들을 위한 MAKE_CELL 명령어 발행 (CPython 3.12 호환)
            // CPython 3.12: MAKE_CELL uses CellVars index order (0, 1, 2...)
            for (int cellIndex = 0; cellIndex < cellVars.Count; cellIndex++)
            {
                var cellVar = cellVars[cellIndex];
                if (!SharpPyConfig.DisassemblyOnlyMode)
                {
#if DEBUG_LOG
                    Console.WriteLine($"  → Making cell for variable: {cellVar} (cell index {cellIndex})");
#endif
                }
                EmitInstruction(ByteCodeOp.MAKE_CELL, cellIndex);
            }

            // CPython 3.12: RESUME instruction after MAKE_CELL and before function body
            EmitInstruction(ByteCodeOp.RESUME, 0);

            foreach (var statement in statements)
            {
                CompileStatement(statement);
            }
            
            // 함수는 None 반환
            EmitLoadConst(PyNone.Instance);
            EmitInstruction(ByteCodeOp.RETURN_VALUE);
            
            var codeObject = new PyCodeObject(name, _instructions, _constants, _names, _varNames,
                                            finalArgCount, posonlyArgCount, freeVars, cellVars, defaults, flags, _currentFileName, _sourceLines);
            
            // Resolve Exception Table labels to offsets (CPython 3.12 compatible)
            ResolveExceptionTable();
            
            // Add Exception Table entries (CPython 3.12 compatible)
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
            Console.WriteLine($"✅ 컴파일 완료: {_instructions.Count}개 명령어");
#endif
            }
            }
            
            // CPython 3.12: 지연된 exception handler들을 바이트코드 끝에 생성
            GeneratePendingExceptionHandlers();
            
            // 바이트코드 최적화 적용
#if DEBUG_LOG
            Console.WriteLine($"🔧 메인 컴파일러에서 최적화 호출: _enable_optimizer={_enable_optimizer}");
#endif
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
            #if DEBUG_LOG
            Console.WriteLine($"🔧 GeneratePendingExceptionHandlers 호출: {_pendingExceptionHandlers.Count}개 handler 처리");
            #endif
            foreach (var handler in _pendingExceptionHandlers)
            {
                // Exception handler를 바이트코드 끝에 생성
                #if DEBUG_LOG
                Console.WriteLine($"🔧 Handler 생성 시작: _instructions.Count={_instructions.Count}");
                #endif
                #if DEBUG_LOG
                Console.WriteLine($"🔧 현재 마지막 명령어: {(_instructions.Count > 0 ? _instructions.Last().ToString() : "없음")}");
                #endif

                // Handler 시작 위치를 기록 (SWAP 명령어 추가 직전) - 명령어 인덱스 사용
                var handlerStart = _instructions.Count;

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
                Console.WriteLine($"🔧 Handler 생성 완료: handlerStart={handlerStart}, 현재 _instructions.Count={_instructions.Count}");
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
        /// CPython 호환: 함수를 매개변수 기본값과 함께 컴파일
        /// </summary>
        public PyCodeObject CompileFunction(List<Statement> statements, string name, List<string> paramNames, List<PyObject> defaults, int flags = 0, int posonlyArgCount = 0)
        {
            // Clear all compilation state for new compilation
            _instructions.Clear();
            _constants.Clear();
            _names.Clear();
            _varNames.Clear();
            _exceptionTable.Clear(); // Reset Exception Table
            _lineNumberTable.Clear(); // Reset line number table
            _isInFunction = true; // We are now compiling inside a function
            _currentFunctionName = name; // Track function name for module level detection

            // Calculate correct argCount for CPython 3.12 compatibility
            int finalArgCount = 0;
            foreach (var param in paramNames)
            {
                if (!param.StartsWith("*")) // Count only regular parameters, exclude *args and **kwargs
                {
                    finalArgCount++;
                }
            }

            // 함수 매개변수를 _varNames에 추가
            foreach (var param in paramNames)
            {
                _varNames.Add(param);
            }
            
            #if DEBUG_LOG
            Console.WriteLine($"\n🔧 컴파일 함수: {name}");
            #endif
#if DEBUG_LOG
            Console.WriteLine($"  매개변수: [{string.Join(", ", paramNames)}]");
#endif
            #if DEBUG_LOG
            Console.WriteLine($"  기본값: [{string.Join(", ", defaults.Select(d => d?.ToString() ?? "None"))}]");
            #endif
            
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
                                                finalArgCount, posonlyArgCount, null, null, defaults, flags, _currentFileName, _sourceLines);
            
            // Generator 함수 감지 및 수정
            if (tempCodeObject.IsGenerator())
            {
                #if DEBUG_LOG
                Console.WriteLine($"🔍 Generator 함수 감지: {name}, RETURN_GENERATOR 추가");
                #endif
                
                // RETURN_GENERATOR를 첫 번째 명령어로 삽입
                _instructions.Insert(0, new ByteCodeInstruction(ByteCodeOp.RETURN_GENERATOR, 0));
                _instructions.Insert(1, new ByteCodeInstruction(ByteCodeOp.POP_TOP, 0));
                
                // CO_GENERATOR 플래그 추가
                flags |= PyCodeObject.CO_GENERATOR;
                #if DEBUG_LOG
                Console.WriteLine($"✅ Generator 함수 설정 완료: CO_GENERATOR 플래그 추가");
                #endif
            }
            
            // 최종 PyCodeObject 생성 (수정된 flags 포함)
            var codeObject = new PyCodeObject(name, _instructions, _constants, _names, _varNames,
                                            finalArgCount, posonlyArgCount, null, null, defaults, flags, _currentFileName, _sourceLines);
            
            // Add Exception Table entries (CPython 3.12)
            codeObject.ExceptionTable.AddRange(_exceptionTable);
            if (!SharpPyConfig.DisassemblyOnlyMode)
            {
                #if DEBUG_LOG
                Console.WriteLine($"✅ 함수 컴파일 완료: {_instructions.Count}개 명령어");
                #endif
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
                    EmitStoreVariable(walrus.Target);     // 올바른 스코프로 저장
                    break;
                    
                case ExpressionStatement expr:
                    CompileExpression(expr.Expression);
                    EmitInstruction(ByteCodeOp.POP_TOP);
                    break;
                    
                case ReturnStatement ret:
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
                            CompileExpression(ret.Value);
                            EmitInstruction(ByteCodeOp.RETURN_VALUE);
                        }
                    }
                    else
                    {
                        var constIndex = GetOrAddConstant(PyNone.Instance);
                        EmitInstruction(ByteCodeOp.RETURN_CONST, constIndex);
                    }
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
                    // CPython 3.12: Check for *args/**kwargs unpacking
                    bool hasStarArgs = call.Arguments.Any(arg => arg is StarredExpression);
                    bool hasKwargUnpacking = call.Keywords.Any(kw => kw.Arg == null); // **kwargs has null Arg

                    if (hasStarArgs || hasKwargUnpacking)
                    {
                        // Use CALL_FUNCTION_EX for unpacking
                        EmitInstruction(ByteCodeOp.PUSH_NULL);
                        CompileExpression(call.Function);

                        // Handle args: combine regular args with *args
                        var regularArgs = call.Arguments.Where(arg => !(arg is StarredExpression)).ToList();
                        var starredArg = call.Arguments.FirstOrDefault(arg => arg is StarredExpression) as StarredExpression;

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
                            EmitInstruction(ByteCodeOp.CALL_INTRINSIC_1, 6); // INTRINSIC_LIST_TO_TUPLE
                        }
                        else
                        {
                            // No args at all, create empty tuple
                            EmitLoadConst(new PyTuple(new PyObject[0]));
                        }

                        // Handle **kwargs
                        if (hasKwargUnpacking)
                        {
                            // Create empty dict first
                            EmitInstruction(ByteCodeOp.BUILD_MAP, 0);

                            // Add each kwargs dict
                            var kwargsKeywords = call.Keywords.Where(kw => kw.Arg == null);
                            foreach (var kwarg in kwargsKeywords)
                            {
                                CompileExpression(kwarg.Value); // This should be a dict
                                EmitInstruction(ByteCodeOp.DICT_MERGE, 1);
                            }

                            // Regular keyword arguments
                            var regularKeywords = call.Keywords.Where(kw => kw.Arg != null);
                            foreach (var kw in regularKeywords)
                            {
                                EmitLoadConst(new PyString(kw.Arg));
                                CompileExpression(kw.Value);
                                EmitInstruction(ByteCodeOp.DICT_MERGE, 1);
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
                    if (list.Elements.Count > 0 && list.Elements.All(e => e is ConstantExpression))
                    {
                        // All elements are constants, use LIST_EXTEND optimization
                        EmitInstruction(ByteCodeOp.BUILD_LIST, 0); // Empty list
                        
                        // Create tuple constant from all elements
                        var constantElements = list.Elements.Cast<ConstantExpression>()
                                                           .Select(c => c.Value)
                                                           .ToArray();
#if DEBUG_LOG
                        Console.WriteLine($"🔍 LIST_EXTEND 최적화: {constantElements.Length}개 상수 요소");
                        for (int i = 0; i < constantElements.Length; i++)
                        {
                            Console.WriteLine($"  constantElements[{i}] = {constantElements[i]} (타입: {constantElements[i]?.GetType().Name})");
                        }
#endif
                        var tupleConstant = new PyTuple(constantElements);
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

                case FStringFormattedValue fstringValue:
#if DEBUG_LOG
                    Console.WriteLine($"[DEBUG] Compiler: Compiling FStringFormattedValue");
#endif
                    CompileFStringFormattedValue(fstringValue);
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
                formatFlags |= (fstringValue.Conversion.Value << 2); // Conversion in bits 2-3
            }
            if (fstringValue.FormatSpec != null)
            {
                CompileExpression(fstringValue.FormatSpec);
                formatFlags |= 1; // Has format spec
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
                // Look for symbol table with function name format
                funcSymbolTable = _currentSymbolTable.Children.FirstOrDefault(child =>
                    child.Name == $"<function:{func.Name}>" || child.Name == func.Name);

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

                    // CPython 3.12: Update symbol table context for nested function compilation
                    savedSymbolTable = _currentSymbolTable;
                    _currentSymbolTable = funcSymbolTable;
                    #if DEBUG_LOG
                    Console.WriteLine($"  🔄 Symbol table context updated: {savedSymbolTable?.Name} → {funcSymbolTable.Name}");
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
            
            // 2. 매개변수와 기본값 파싱 (FunctionDefStatement에서 수행하던 로직)
            var (paramNames, defaults, flags, argCount, posonlyArgCount, annotations) = ParseFunctionParameters(func.Parameters);

            // 3. Return type annotation 처리 (CPython 3.12)
            if (func.ReturnTypeAnnotation != null)
            {
                annotations["return"] = func.ReturnTypeAnnotation.ToString();
            }
            
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
                        localVars.Add(assign.VariableName);
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
                if (ContainsSuperCalls(func.Body) && availableOuterVars.Contains("__class__"))
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"   Function contains super() calls - adding __class__ as referenced variable");
                    #endif
                    referencedVars.Add("__class__");
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

            // CPython 3.12: 심볼 테이블 컨텍스트를 새로운 컴파일러에 전달
            if (_currentSymbolTable != null)
            {
                compiler.SetSymbolTableContext(_currentSymbolTable);
                #if DEBUG_LOG
                Console.WriteLine($"  📤 Passed symbol table context to nested compiler: {_currentSymbolTable.Name}");
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
            var funcCode = compiler.CompileWithClosureAndDefaults(func.Body, func.Name, paramNames, defaults, freeVars, cellVars, flags, argCount, posonlyArgCount);
            
            // 3. CPython 3.12 exact pattern: Load decorators in REVERSE order (bottom to top in source)
            if (func.Decorators != null && func.Decorators.Count > 0)
            {
                // Load decorators in reverse order (bottom-most decorator first)
                for (int i = func.Decorators.Count - 1; i >= 0; i--)
                {
                    var decorator = func.Decorators[i];

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
                            {
                                keywordArgs.Add(keyword);
                            }
                            else
                            {
                                positionalArgs.Add(arg);
                            }
                        }

                        // Compile positional arguments first
                        foreach (var arg in positionalArgs)
                        {
                            CompileExpression(arg);
                        }

                        // Compile keyword argument values
                        foreach (var keyword in keywordArgs)
                        {
                            CompileExpression(keyword.Value);
                        }

                        // Handle keyword arguments with KW_NAMES (CPython 3.12 pattern)
                        if (keywordArgs.Count > 0)
                        {
                            // Create keyword names tuple and add to constants
                            var kwNames = keywordArgs.Select(kw => new PyString(kw.Arg ?? "")).ToArray();
                            var kwNamesTuple = new PyTuple(kwNames);
                            var kwNamesIndex = GetOrAddConstant(kwNamesTuple);

                            // CPython 3.12: KW_NAMES + CALL pattern
                            EmitInstruction(ByteCodeOp.KW_NAMES, kwNamesIndex);
                            EmitInstruction(ByteCodeOp.CALL, positionalArgs.Count + keywordArgs.Count);
                        }
                        else
                        {
                            // Only positional arguments
                            EmitInstruction(ByteCodeOp.CALL, positionalArgs.Count);
                        }
                    }
                    else
                    {
                        // Simple decorator: @decorator - just load the function
                        CompileExpression(decorator.DecoratorFunction);
                    }
                }
            }

            // 4. MAKE_FUNCTION 스택 순서 맞추기 (CPython 3.12 compatible)
            // 기본값이 있는 경우 기본값 튜플을 먼저 푸시 (스택 맨 아래)
            int makeFunctionFlags = 0;
            if (defaults.Count > 0)
            {
                foreach (var defaultValue in defaults)
                {
                    EmitLoadConst(defaultValue);
                }
                EmitInstruction(ByteCodeOp.BUILD_TUPLE, defaults.Count);
                makeFunctionFlags |= MakeFunctionFlags.DEFAULTS;
            }

            // CPython 3.12: 타입 어노테이션이 있는 경우 어노테이션 튜플 생성
            if (annotations.Count > 0)
            {
                // CPython 패턴: BUILD_TUPLE로 튜플 생성 후 BUILD_TUPLE로 최종 래핑
                foreach (var annotation in annotations)
                {
                    EmitLoadConst(new PyString(annotation.Key));    // key (예: 'return')
                    // 어노테이션 값은 실제 타입 객체가 아닌 이름으로 저장
                    if (annotation.Key == "return")
                    {
                        // return 타입 어노테이션: 타입 이름을 로드
                        EmitLoadName(annotation.Value); // 'int', 'str' 등
                    }
                    else
                    {
                        EmitLoadConst(new PyString(annotation.Value));  // 매개변수 타입
                    }
                }
                EmitInstruction(ByteCodeOp.BUILD_TUPLE, annotations.Count * 2);
                makeFunctionFlags |= MakeFunctionFlags.ANNOTATIONS;
                #if DEBUG_LOG
                Console.WriteLine($"  → Built annotations tuple: {annotations.Count} annotations");
                #endif
            }

            // 5. 자유 변수가 있는 경우 클로저 생성 (defaults/annotations 위에 푸시)
            if (freeVars.Count > 0)
            {
                #if DEBUG_LOG
                Console.WriteLine($"  → Creating closure for {freeVars.Count} free variables");
                #endif

                // CPython 호환: 클로저를 자유 변수 순서대로 생성
                #if DEBUG_LOG
                Console.WriteLine($"    🔍 Building closure for {freeVars.Count} variables: [{string.Join(", ", freeVars)}]");
                #endif
                #if DEBUG_LOG
                Console.WriteLine($"    📋 Available cells: [{string.Join(", ", _cellVars)}], frees: [{string.Join(", ", _freeVars)}]");
                #endif

                // 자유 변수들을 순서대로 LOAD_CLOSURE
                foreach (var freeVar in freeVars)
                {
                    int closureIndex = -1;
                    string source = "";

                    // 먼저 자유 변수에서 찾기 (closure에서 가져오는 변수들)
                    var freeVarIndex = _freeVars.IndexOf(freeVar);
                    if (freeVarIndex >= 0)
                    {
                        // free variable: closure index는 0부터 시작
                        closureIndex = freeVarIndex;
                        source = "free";
                    }
                    else
                    {
                        // 셀 변수에서 찾기 (현재 함수의 로컬 셀들)
                        var cellVarIndex = _cellVars.IndexOf(freeVar);
                        if (cellVarIndex >= 0)
                        {
                            // cell variable: closure index는 free vars 뒤에 위치
                            closureIndex = _freeVars.Count + cellVarIndex;
                            source = "cell";
                        }
                    }

                    if (closureIndex >= 0)
                    {
                        EmitInstruction(ByteCodeOp.LOAD_CLOSURE, closureIndex);
                        #if DEBUG_LOG
                        Console.WriteLine($"    → LOAD_CLOSURE for {freeVar} ({source} index {closureIndex})");
                        #endif
                    }
                    else
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"    ⚠️ Warning: Free variable {freeVar} not available (cells: [{string.Join(",", _cellVars)}], frees: [{string.Join(",", _freeVars)}])");
                        #endif
                        // Fallback: 빈 셀 생성
                        EmitInstruction(ByteCodeOp.LOAD_CLOSURE, 0);
                    }
                }

                // 클로저 튜플 생성
                EmitInstruction(ByteCodeOp.BUILD_TUPLE, freeVars.Count);
                makeFunctionFlags |= MakeFunctionFlags.CLOSURE;
            }

            // 6. Load function code and create function (AFTER decorators and annotations)
            EmitLoadConst(funcCode);
            #if DEBUG_LOG
            Console.WriteLine($"  → MAKE_FUNCTION flags: {makeFunctionFlags} (defaults={defaults.Count > 0}, annotations={annotations.Count > 0}, closure={freeVars.Count > 0})");
            #endif
            EmitInstruction(ByteCodeOp.MAKE_FUNCTION, makeFunctionFlags);

            // 8. Call decorators in forward order (CPython 3.12 compatible)
            if (func.Decorators != null && func.Decorators.Count > 0)
            {
                // Apply decorators in forward order: each decorator gets called with 0 arguments (the function)
                for (int i = 0; i < func.Decorators.Count; i++)
                {
                    // Call decorator with function (no arguments - they were already processed above)
                    EmitInstruction(ByteCodeOp.CALL, 0);
                }
            }

            // 8. Store the final function (decorated or original)
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
                        Console.WriteLine($"    → Found AssignStatement: {assign.VariableName}");
                        #endif
                        if (!localVars.Contains(assign.VariableName))
                        {
                            localVars.Add(assign.VariableName);
                        }
                        break;

                    case FunctionDefStatement nestedFunc:
                        #if DEBUG_LOG
                        Console.WriteLine($"    → Found nested function: {nestedFunc.Name}");
                        #endif
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
            try
            {
                _instructions.Add(new ByteCodeInstruction(opCode, argument, _currentLineNumber, _currentColumnOffset, _currentFileName));
            }
            catch (Exception ex)
            {
#if DEBUG_LOG
                Console.WriteLine($"💥 EmitInstruction 에러: {ex.Message}");
                Console.WriteLine($"   opCode: {opCode}, argument: {argument}");
                Console.WriteLine($"   _instructions null? {_instructions == null}");
                Console.WriteLine($"   _instructions count: {_instructions?.Count ?? -1}");
                Console.WriteLine($"   Stack trace: {ex.StackTrace}");
#endif
                throw;
            }

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
            var result = PyJumpBackwardUtil.CalculateJumpBackwardOpArg(currentInstrPos, targetInstrPos, _instructions);
            #if DEBUG_LOG
            Console.WriteLine($"🔧 CalculateJumpBackwardArg: currentPos={currentInstrPos}, targetPos={targetInstrPos}, result={result}");
            #endif
            return result;
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
                Console.WriteLine($"   _instructions null? {_instructions == null}");
                Console.WriteLine($"   Stack trace: {ex.StackTrace}");
#endif
                throw;
            }
        }
        
        private void EmitLoadName(string name)
        {
            // Phase 2: 클로저 지원 - 자유 변수 처리 개선

            // 0. CPython 3.12: Symbol Table을 먼저 확인 (우선순위)
            if (_currentSymbolTable != null)
            {
                var symbol = _currentSymbolTable.Lookup(name);
                if (symbol != null)
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"    🔍 Symbol Table lookup: {name} → Scope: {symbol.Scope}");
                    #endif

                    // Symbol Table에서 분석된 스코프에 따라 적절한 명령어 생성
                    switch (symbol.Scope)
                    {
                        case SymbolScope.Free:
                            // Free variable: LOAD_DEREF 사용
                            if (_freeVars.Contains(name))
                            {
                                var freeIndex = _freeVars.IndexOf(name);
                                EmitInstruction(ByteCodeOp.LOAD_DEREF, freeIndex);
                                #if DEBUG_LOG
                                Console.WriteLine($"    → LOAD_DEREF for free var: {name} (index {freeIndex})");
                                #endif
                                return;
                            }
                            break;

                        case SymbolScope.Cell:
                            // Cell variable: LOAD_DEREF 사용 (offset 계산)
                            if (_cellVars.Contains(name))
                            {
                                var cellIndex = _cellVars.IndexOf(name);
                                var instructionIndex = _freeVars.Count + cellIndex;
                                EmitInstruction(ByteCodeOp.LOAD_DEREF, instructionIndex);
                                #if DEBUG_LOG
                                Console.WriteLine($"    → LOAD_DEREF for cell var: {name} (cell index {cellIndex} → instruction index {instructionIndex})");
                                #endif
                                return;
                            }
                            break;

                        case SymbolScope.Global:
                            // CPython 3.12: 모듈 레벨에서는 LOAD_NAME, 함수 내에서는 LOAD_GLOBAL
                            var globalIndex = AddName(name);
                            if (!_isInFunction)
                            {
                                EmitInstruction(ByteCodeOp.LOAD_NAME, globalIndex);
                                #if DEBUG_LOG
                                Console.WriteLine($"    → Module level LOAD_NAME for global var: {name} (global index {globalIndex})");
                                #endif
                            }
                            else
                            {
                                EmitInstruction(ByteCodeOp.LOAD_GLOBAL, globalIndex);
                                #if DEBUG_LOG
                                Console.WriteLine($"    → Function level LOAD_GLOBAL for global var: {name} (global index {globalIndex})");
                                #endif
                            }
                            return;

                        case SymbolScope.Local:
                            // Local variable: LOAD_FAST 사용
                            var localIndex = _varNames.IndexOf(name);
                            if (localIndex >= 0)
                            {
                                EmitInstruction(ByteCodeOp.LOAD_FAST, localIndex);
                                #if DEBUG_LOG
                                Console.WriteLine($"    → LOAD_FAST for local var: {name} (index {localIndex})");
                                #endif
                                return;
                            }
                            break;
                    }
                }
            }

            // 1. CPython 3.12: global 변수를 먼저 체크 (fallback)
            if (_globalVars.Contains(name))
            {
                var globalIndex = AddName(name);
                EmitInstruction(ByteCodeOp.LOAD_GLOBAL, globalIndex);
                #if DEBUG_LOG
                Console.WriteLine($"    → LOAD_GLOBAL for global var: {name} (global index {globalIndex})");
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
            var varIndex = _varNames.IndexOf(name);
            if (varIndex >= 0)
            {
                // 이 변수가 cell로 변환되었는지 확인
                if (_cellVars.Contains(name))
                {
                    // Cell 변수는 LOAD_DEREF로 접근
                    var cellIndex = _cellVars.IndexOf(name);
                    // CPython 3.12: Cell variables come after free variables in instruction indices
                    var instructionIndex = _freeVars.Count + cellIndex;
                    EmitInstruction(ByteCodeOp.LOAD_DEREF, instructionIndex);
                    #if DEBUG_LOG
                    Console.WriteLine($"    → LOAD_DEREF for cell var: {name} (cell index {cellIndex} → instruction index {instructionIndex})");
                    #endif
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
                #if DEBUG_LOG
                Console.WriteLine($"    → LOAD_DEREF for free var: {name} (index {freeIndex})");
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
                                // Cell variable: STORE_DEREF 사용 (offset 계산)
                                if (_cellVars.Contains(name))
                                {
                                    var cellIndex = _cellVars.IndexOf(name);
                                    var instructionIndex = _freeVars.Count + cellIndex;
                                    EmitInstruction(ByteCodeOp.STORE_DEREF, instructionIndex);
                                    #if DEBUG_LOG
                                    Console.WriteLine($"    → STORE_DEREF for cell var: {name} (cell index {cellIndex} → instruction index {instructionIndex})");
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
                    var freeIndex = _freeVars.IndexOf(name);
                    EmitInstruction(ByteCodeOp.STORE_DEREF, freeIndex);
                    #if DEBUG_LOG
                    Console.WriteLine($"    → STORE_DEREF for nonlocal var: {name} (free index {freeIndex})");
                    #endif
                    return;
                }
                
                // 셀 변수 처리 (Phase 2)
                if (_cellVars.Contains(name))
                {
                    var cellIndex = _cellVars.IndexOf(name);
                    // CPython 3.12: Cell variables come after free variables in instruction indices
                    var instructionIndex = _freeVars.Count + cellIndex;
                    EmitInstruction(ByteCodeOp.STORE_DEREF, instructionIndex);
                    #if DEBUG_LOG
                    Console.WriteLine($"    → STORE_DEREF for cell var: {name} (cell index {cellIndex} → instruction index {instructionIndex})");
                    #endif
                    return;
                }
                
                // 자유 변수 처리 (Phase 2)
                if (_freeVars.Contains(name))
                {
                    var freeIndex = _freeVars.IndexOf(name);
                    EmitInstruction(ByteCodeOp.STORE_DEREF, freeIndex);
                    #if DEBUG_LOG
                    Console.WriteLine($"    → STORE_DEREF for free var: {name} (index {freeIndex})");
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
            
            // 2. 매개변수와 기본값 파싱
            var (paramNames, defaults, flags, argCount, posonlyArgCount, annotations) = ParseAsyncFunctionParameters(asyncFunc.Parameters);
            
            // 3. 코드 객체 컴파일 (async 함수 전용)
            var codeObject = CompileAsyncFunctionBody(asyncFunc, freeVars, cellVars);
            
            // 4. 클로저와 기본값은 나중에 MAKE_FUNCTION 직전에 로드
            
            // 5. MAKE_FUNCTION을 위한 스택 준비 (CPython 순서: defaults, annotations, code)

            // 6. 기본값들을 tuple로 만들어 스택에 로드 (CPython 3.12 호환)
            if (defaults.Any())
            {
                foreach (var defaultValue in defaults)
                {
                    EmitLoadConst(defaultValue); // 기본값은 이미 PyObject이므로 직접 로드
                }
                EmitInstruction(ByteCodeOp.BUILD_TUPLE, defaults.Count);
            }

            // 7. annotations 튜플을 스택에 로드 (CPython 3.12 호환성)
            if (annotations.Any())
            {
                foreach (var annotation in annotations)
                {
                    EmitLoadConst(new PyString(annotation.Key));   // parameter name
                    EmitLoadConst(new PyString(annotation.Value)); // annotation type
                }
                EmitInstruction(ByteCodeOp.BUILD_TUPLE, annotations.Count * 2);
            }

            // 8. 클로저가 있으면 셀 변수들을 스택에 로드
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

            // 9. 코드 객체를 상수로 로드 (마지막에 - 스택 맨 위가 됨)
            EmitLoadConst(codeObject);

            // 10. MAKE_FUNCTION 명령어 생성 (CPython 3.12와 동일한 플래그)
            var makeFlags = 0;
            if (defaults.Any()) makeFlags |= 0x01;  // CO_HAS_DEFAULTS
            if (annotations.Any()) makeFlags |= 0x04; // HAS_ANNOTATIONS (CPython 3.12)
            if (freeVars.Any()) makeFlags |= 0x08;  // CO_HAS_CLOSURE
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
                var (paramNames, defaults, flags, argCount, posonlyArgCount, annotations) = ParseFunctionParameters(func.Parameters);
                
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
                    0, // posonlyArgCount
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
            #if DEBUG_LOG
            Console.WriteLine($"🔍 CompileRegularClass called for: {cls.Name}");
            #endif

            // CPython 3.12: Regular class compilation (no type parameters)
            // PUSH_NULL 먼저, 그 다음 __build_class__ function 로드
            EmitInstruction(ByteCodeOp.PUSH_NULL);
            EmitLoadName("__build_class__");

            // Compile class body into a function
            var classBodyName = $"<class_body_{cls.Name}>";

            // Check if class has free variables (needs closure)
            var classFreeVars = GetClassFreeVariables(cls.Name);
            #if DEBUG_LOG
            Console.WriteLine($"🔍 GetClassFreeVariables for {cls.Name}: {classFreeVars.Count} free vars [{string.Join(", ", classFreeVars)}]");
            #endif

            // Try alternative class body name if first attempt failed
            if (classFreeVars.Count == 0)
            {
                var classBodyFreeVars = GetClassFreeVariables(classBodyName);
                #if DEBUG_LOG
                Console.WriteLine($"🔍 Alternative GetClassFreeVariables for {classBodyName}: {classBodyFreeVars.Count} free vars [{string.Join(", ", classBodyFreeVars)}]");
                #endif
                if (classBodyFreeVars.Count > 0)
                {
                    classFreeVars = classBodyFreeVars;
                }
                else
                {
                    // Fallback: Analyze class body directly to find free variables
                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 Symbol table lookup failed, using direct class body analysis");
                    #endif
                    classFreeVars = GetClassFreeVariablesFromBody(cls.Body);
                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 GetClassFreeVariablesFromBody for {cls.Name}: {classFreeVars.Count} free vars [{string.Join(", ", classFreeVars)}]");
                    #endif
                }
            }

            if (classFreeVars.Count > 0)
            {
                // Create closure: LOAD_CLOSURE + BUILD_TUPLE
                foreach (var freeVar in classFreeVars)
                {
                    EmitLoadClosure(freeVar);
                }
                EmitInstruction(ByteCodeOp.BUILD_TUPLE, classFreeVars.Count);

                // Compile class body with closure support
                var classBodyCode = CompileClassBody(cls.Body, classBodyName);
                EmitLoadConst(classBodyCode);
                EmitInstruction(ByteCodeOp.MAKE_FUNCTION, 8); // 8 = closure flag
            }
            else
            {
                // No closure needed - original logic
                var classBodyCode = CompileClassBody(cls.Body, classBodyName);
                EmitLoadConst(classBodyCode);
                EmitInstruction(ByteCodeOp.MAKE_FUNCTION, 0);
            }
            
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
                    var parameters = funcDef.Parameters.ToHashSet();

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

            var result = freeVariables.ToList();
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
                    if (!PyToken.IsKeywordLexeme(nameExpr.Name))
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
                    variables.Add(assignStmt.VariableName);
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
            // Use PyToken's keyword checking functionality
            return PyToken.IsKeywordLexeme(varName) || IsBuiltinVariable(varName);
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
                    0,  // posonlyArgCount
                    _freeVars,    // freeVars
                    _cellVars     // cellVars
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
            var savedExceptionTable = _exceptionTable.ToList(); // Preserve Exception Table entries
            var savedCurrentSymbolTable = _currentSymbolTable;

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
            _instructions = new List<ByteCodeInstruction>();
            _constants = new List<PyObject>();
            _names = new List<string>();
            _varNames = new List<string>();
            _cellVars = new List<string>();
            _freeVars = new List<string>();
            // Keep existing Exception Table entries instead of resetting
            // _exceptionTable = new List<ExceptionTableEntry>(); // Removed: This was causing Exception Table entry loss

            // Set up free variables if class symbol table is available
            if (classSymbolTable != null)
            {
                var classFreeVars = classSymbolTable.FindFreeVariables();
                _freeVars.AddRange(classFreeVars);
                #if DEBUG_LOG
                if (classFreeVars.Count > 0)
                {
                    Console.WriteLine($"🔍 Class {className} has {classFreeVars.Count} free variables: [{string.Join(", ", classFreeVars)}]");
                }
                #endif
            }
            
            // Check if class body contains super() calls and add __class__ cell variable if needed
            if (ContainsSuperCalls(body))
            {
                #if DEBUG_LOG
                Console.WriteLine($"🔍 Detected super() calls in class {className}, adding __class__ cell variable");
                #endif
                _cellVars.Add("__class__");
                
                // Generate MAKE_CELL instruction for __class__ cell variable
                // CPython 3.12: __class__ cell variable uses index 0 (first cellVar)
                var cellVarIndex = 0; // __class__ is always the first (index 0) cell variable
                #if DEBUG_LOG
                Console.WriteLine($"🔧 Generating MAKE_CELL for __class__ at cell index {cellVarIndex}");
                #endif
                EmitInstruction(ByteCodeOp.MAKE_CELL, cellVarIndex);
            }
            
            // For now, disable free variable analysis for class bodies  
            // Class bodies will use normal name lookup instead of closure mechanism
            // var freeVariableAnalyzer = new ClassBodyFreeVariableAnalyzer();
            // var classFreeVars = freeVariableAnalyzer.AnalyzeClassBody(body, savedNames);
            // _freeVars.AddRange(classFreeVars);
            
            try
            {
                // CPython 3.12: Emit COPY_FREE_VARS if there are free variables
                if (_freeVars.Count > 0)
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 Emitting COPY_FREE_VARS for {_freeVars.Count} free variables in class {className}");
                    #endif
                    EmitInstruction(ByteCodeOp.COPY_FREE_VARS, _freeVars.Count);
                }

                // CPython 3.12: Setup __module__ attribute in class body
                // This is equivalent to: __module__ = __name__
                EmitLoadName("__name__");  // Load current module name
                EmitStoreName("__module__");  // Store as __module__ in class dict

                // CPython 3.12: Setup __qualname__ attribute in class body
                var actualClassName = className.Contains("<class_body_") ?
                    className.Replace("<class_body_", "").TrimEnd('>') : className;
                EmitLoadConst(new PyString(actualClassName));  // Load class name
                EmitStoreName("__qualname__");  // Store as __qualname__ in class dict

                // Compile class body statements
                foreach (var stmt in body)
                {
                    CompileStatement(stmt);
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
                _exceptionTable = savedExceptionTable; // Restore Exception Table entries

                // CPython 3.12: Restore symbol table context
                _currentSymbolTable = savedCurrentSymbolTable;
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

                case BinaryOpExpression binary:
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
                
                // CPython 3.12 pattern: LOAD_CONST(0), LOAD_CONST(None), IMPORT_NAME
                var levelIndex = GetOrAddConstant(new PyInt(0));  // fromlist level
                var fromlistIndex = GetOrAddConstant(PyNone.Instance);  // fromlist
                var moduleIndex = GetOrAddConstant(new PyString(actualModule));

                EmitInstruction(ByteCodeOp.LOAD_CONST, levelIndex);
                EmitInstruction(ByteCodeOp.LOAD_CONST, fromlistIndex);
                EmitInstruction(ByteCodeOp.IMPORT_NAME, moduleIndex);
                
                // Store the imported module in the correct variable name
                // CPython 3.12: Always use STORE_NAME for module level imports
                var nameIndex = AddName(alias);
                EmitInstruction(ByteCodeOp.STORE_NAME, nameIndex);
            }
        }
        private void CompileImportFrom(ImportFromStatement importFrom)
        {
            // Load the module first
            var moduleIndex = GetOrAddConstant(new PyString(importFrom.Module));
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
                var itemIndex = GetOrAddConstant(new PyString(actualItem));
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
            #if DEBUG_LOG
            Console.WriteLine("🔧 CPython 3.12 호환 while True 루프 컴파일");
            #endif
            
            // Setup loop context for break/continue
            var breakLabel = CreateLabel("while_true_break");
            var continueLabel = CreateLabel("while_true_continue");
            PushLoopContext(breakLabel, continueLabel);
            
            // CPython pattern: emit NOP for while True:
            EmitInstruction(ByteCodeOp.NOP, 0);
            
            // 루프 바디 시작점 (JUMP_BACKWARD 타겟) - continue target
            var bodyStart = _instructions.Count;
            MarkLabel(continueLabel); // continue는 루프 바디 시작으로
            #if DEBUG_LOG
            Console.WriteLine($"  바디 시작점 = {bodyStart} (JUMP_BACKWARD 타겟)");
            #endif
            
            // Compile loop body
            foreach (var stmt in whileStmt.Body)
            {
                CompileStatement(stmt);
            }
            
            // JUMP_BACKWARD to body start (no condition check)
            int currentPos = _instructions.Count;
            int jumpBackwardArg = CalculateJumpBackwardArg(currentPos, bodyStart);
            #if DEBUG_LOG
            Console.WriteLine($"  JUMP_BACKWARD {currentPos} → {bodyStart} (arg={jumpBackwardArg})");
            #endif
            EmitInstruction(ByteCodeOp.JUMP_BACKWARD, jumpBackwardArg);
            
            // Pop loop context
            PopLoopContext();
            
            // Mark break label - break는 여기로 점프
            MarkLabel(breakLabel);
            #if DEBUG_LOG
            Console.WriteLine("🔧 CPython 3.12 호환 while True 루프 컴파일 완료");
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
            
            // Phase 1: 초기 조건 체크 (CPython pattern)
            #if DEBUG_LOG
            Console.WriteLine("  Phase 1: 초기 조건 체크");
            #endif
            CompileExpression(whileStmt.Test);
            
            var initialJumpIfFalse = _instructions.Count;
            EmitInstruction(ByteCodeOp.POP_JUMP_IF_FALSE, 0); // 주소는 나중에 패치
            
            // Phase 2: 루프 바디 컴파일 (JUMP_BACKWARD 타겟은 첫 번째 바디 명령어)
            // CPython 패턴: JUMP_BACKWARD는 실제 루프 바디 시작으로 점프
            var bodyStart = _instructions.Count; // 바디 첫 번째 명령어 위치
            #if DEBUG_LOG
            Console.WriteLine($"  Phase 2: 바디 시작점 = {bodyStart} (JUMP_BACKWARD 타겟)");
            #endif
            
            // Compile loop body
            foreach (var stmt in whileStmt.Body)
            {
                CompileStatement(stmt);
            }
            
            // Phase 3: 루프 끝 조건 체크 (CPython pattern)
            #if DEBUG_LOG
            Console.WriteLine("  Phase 3: 루프 끝 조건 체크");
            #endif
            CompileExpression(whileStmt.Test);  // 조건을 두 번째로 체크
            
            var endJumpIfFalse = _instructions.Count;
            EmitInstruction(ByteCodeOp.POP_JUMP_IF_FALSE, 0); // 주소는 나중에 패치
            
            // Phase 4: JUMP_BACKWARD (바디 시작점으로 - CPython 패턴 확인됨)
            int currentPos = _instructions.Count;
            int jumpBackwardArg = CalculateJumpBackwardArg(currentPos, bodyStart);
            #if DEBUG_LOG
            Console.WriteLine($"  Phase 4: JUMP_BACKWARD {currentPos} → {bodyStart} (arg={jumpBackwardArg})");
            #endif
            EmitInstruction(ByteCodeOp.JUMP_BACKWARD, jumpBackwardArg);
            
            // Phase 5: 루프 종료 지점
            var loopEnd = _instructions.Count;
            #if DEBUG_LOG
            Console.WriteLine($"  Phase 5: 루프 종료점 = {loopEnd}");
            #endif
            
            // 점프 주소 패치 (상대 오프셋 사용)
            var relativeOffsetInitial = loopEnd - initialJumpIfFalse - 1;
            var relativeOffsetEnd = loopEnd - endJumpIfFalse - 1;
            #if DEBUG_LOG
            Console.WriteLine($"  Patching jump instructions:");
            #endif
            #if DEBUG_LOG
            Console.WriteLine($"    initialJumpIfFalse[{initialJumpIfFalse}] → {loopEnd} (relative offset: {relativeOffsetInitial})");
            #endif
            #if DEBUG_LOG
            Console.WriteLine($"    endJumpIfFalse[{endJumpIfFalse}] → {loopEnd} (relative offset: {relativeOffsetEnd})");
            #endif
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
            
            #if DEBUG_LOG
            Console.WriteLine("🔧 CPython 3.12 호환 while 루프 컴파일 완료");
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
        /// CPython 3.12 Exception Table based try-except compilation
        /// </summary>
        private void CompileTry(TryStatement tryStmt)
        {
            // CPython 3.12: No SETUP_EXCEPT, use Exception Table instead

            // Create label for continuation after entire try-except construct
            var continueLabel = CreateLabel("continue_after_try");

            // Exception Table start offset BEFORE NOP - 명령어 인덱스 사용 (CPython 호환)
            // CPython 3.12: Try block should start from the actual first instruction (NOP)
            var tryStartOffset = _instructions.Count;

            // CPython 3.12: Add NOP instruction before try body (exact CPython pattern)
            EmitInstruction(ByteCodeOp.NOP);
            
            // CPython 3.12: Direct compilation of try body (no SETUP_EXCEPT)
            foreach (var stmt in tryStmt.Body)
            {
                CompileStatement(stmt);
            }

            // CPython 3.12: Try body completed normally - execute else clause first, then finally
            // Don't jump to continuation yet - execute else and finally first

            // Execute else clause if present (only when no exception occurred)
            if (tryStmt.OrElse != null && tryStmt.OrElse.Count > 0)
            {
                foreach (var stmt in tryStmt.OrElse)
                {
                    CompileStatement(stmt);
                }
            }

            // Execute finally clause if present (normal path - not exception handler)
            if (tryStmt.FinalBody != null && tryStmt.FinalBody.Count > 0)
            {
                foreach (var stmt in tryStmt.FinalBody)
                {
                    CompileStatement(stmt);
                }
            }

            // Now jump to continuation after normal try-else-finally execution
            EmitJumpToLabel(ByteCodeOp.JUMP_FORWARD, continueLabel);

            // Try block ends AFTER the JUMP_FORWARD instruction (CPython 3.12 compatible)
            var tryEndOffset = _instructions.Count;
            
            // Exception handler start (where PUSH_EXC_INFO will jump to)
            var handlersStartLabel = CreateLabel("handlers_start");
            MarkLabel(handlersStartLabel);
            
            // CPython 3.12: Exception path starts with PUSH_EXC_INFO
            EmitInstruction(ByteCodeOp.PUSH_EXC_INFO);

            // CPython 3.12: For except* handlers, start with BUILD_LIST 0 to collect matched handlers
            bool hasExceptStarHandlers = tryStmt.Handlers.Any(h => h.IsStar);
            if (hasExceptStarHandlers)
            {
                EmitInstruction(ByteCodeOp.BUILD_LIST, 0);
            }

            // Pre-create the reraise label once
            var reraiseLabel = CreateLabel("reraise");

            // Compile exception handlers sequentially
            for (int i = 0; i < tryStmt.Handlers.Count; i++)
            {
                var handler = tryStmt.Handlers[i];
                var nextHandlerLabel = (i < tryStmt.Handlers.Count - 1)
                    ? CreateLabel($"handler_{i+1}")
                    : reraiseLabel;
                
                if (handler.Type != null)
                {
                    if (handler.IsStar)
                    {
                        // CPython 3.12: except* pattern - COPY 2 to preserve handler list
                        EmitInstruction(ByteCodeOp.COPY, 2);
                        CompileExpression(handler.Type);

                        // PEP 654: Exception group matching
                        EmitInstruction(ByteCodeOp.CHECK_EG_MATCH);
                        EmitInstruction(ByteCodeOp.COPY, 1);
                        EmitInstruction(ByteCodeOp.POP_JUMP_IF_NONE, 0);
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
                    else
                    {
                        // Regular exception handler - keep original COPY 1 pattern
                        EmitInstruction(ByteCodeOp.COPY, 1);
                        CompileExpression(handler.Type);

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
                
                // CPython 3.12: Record handler body start for Exception Table
                var handlerBodyStart = _instructions.Count;

                // Execute handler body
                foreach (var stmt in handler.Body)
                {
                    CompileStatement(stmt);
                }

                // CPython 3.12: Record handler body end BEFORE cleanup operations
                var handlerBodyEnd = _instructions.Count;

                // CPython 3.12: Do not add LIST_APPEND here - it goes in cleanup section

                // CPython 3.12: POP_EXCEPT after handler execution
                EmitInstruction(ByteCodeOp.POP_EXCEPT);

                // CPython 3.12: Exception variable cleanup operations
                var cleanupStart = _instructions.Count;
                if (handler.Name != null)
                {
                    EmitInstruction(ByteCodeOp.LOAD_CONST, GetOrAddConstant(PyNone.Instance));
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
                var cleanupEnd = _instructions.Count;

                // CPython 3.12: For except* handlers, continue to next handler; for regular except, jump to finally or end
                if (handler.IsStar && i < tryStmt.Handlers.Count - 1)
                {
                    // For except* handlers, continue to next handler to process remainder
                    EmitJumpToLabel(ByteCodeOp.JUMP_FORWARD, nextHandlerLabel);
                }
                else
                {
                    // For regular except or last except* handler, check if finally block exists
                    if (tryStmt.FinalBody != null && tryStmt.FinalBody.Count > 0)
                    {
                        // Execute finally block after exception handling (exception path)
                        foreach (var stmt in tryStmt.FinalBody)
                        {
                            CompileStatement(stmt);
                        }
                    }
                    // Then jump to continuation
                    EmitJumpToLabel(ByteCodeOp.JUMP_FORWARD, continueLabel);
                }

                // CPython 3.12: Create Exception Table entry for handler body protection
                // For except* handlers, always protect the handler body (even without variable binding)
                if ((handler.IsStar || handler.Name != null) && handlerBodyEnd > handlerBodyStart)
                {
                    // Create simple cleanup handler for exception variables (CPython 3.12 pattern)
                    var cleanupHandlerLabel = CreateLabel($"cleanup_handler_{i}");
                    MarkLabel(cleanupHandlerLabel);

                    // Cleanup exception variable on exception in handler (only if variable exists)
                    if (handler.Name != null)
                    {
                        EmitInstruction(ByteCodeOp.LOAD_CONST, GetOrAddConstant(PyNone.Instance));
                        if (_isInFunction)
                        {
                            EmitInstruction(ByteCodeOp.STORE_FAST, GetOrAddVarName(handler.Name));
                            EmitInstruction(ByteCodeOp.DELETE_FAST, GetOrAddVarName(handler.Name));
                        }
                        else
                        {
                            EmitInstruction(ByteCodeOp.STORE_NAME, GetOrAddName(handler.Name));
                            EmitInstruction(ByteCodeOp.DELETE_NAME, GetOrAddName(handler.Name));
                        }
                    }

                    // CPython 3.12: For except* handlers, add to handler list in cleanup path
                    if (handler.IsStar)
                    {
                        EmitInstruction(ByteCodeOp.LIST_APPEND, 3);
                        EmitInstruction(ByteCodeOp.POP_TOP); // Clean up remaining copy
                    }

                    var cleanupReraiseOffset = _instructions.Count;
                    EmitInstruction(ByteCodeOp.RERAISE, 1);

                    // CPython 3.12: Handler body protection entry for except* handlers
                    var handlerExecutionEntry = new ExceptionTableEntry(
                        start: handlerBodyStart,
                        end: handlerBodyEnd,  // Only protect the handler body itself
                        handlerLabel: cleanupHandlerLabel.Name,  // Point to cleanup handler
                        depth: 4,  // CPython uses depth 4 for handler execution
                        lasti: true
                    );
                    _exceptionTable.Add(handlerExecutionEntry);

                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 Handler execution Exception Table: {handlerBodyStart} to {(cleanupEnd > cleanupStart ? cleanupEnd : handlerBodyEnd)} -> {reraiseLabel.Name} [depth=4, lasti] (CPython optimized)");
                    #endif
                }
                
                // Mark next handler if not last
                if (i < tryStmt.Handlers.Count - 1)
                {
                    MarkLabel(nextHandlerLabel);

                    // CPython 3.12: Add POP_TOP before next except* handler to clean up remainder
                    if (tryStmt.Handlers[i+1].IsStar)
                    {
                        EmitInstruction(ByteCodeOp.POP_TOP);
                    }
                }
            }

            // CPython 3.12: After all except* handlers, use INTRINSIC_PREP_RERAISE_STAR
            if (hasExceptStarHandlers)
            {
                // Record start of final except* processing for exception table
                var finalProcessingStart = _instructions.Count;

                // At this point we should have fallen through all except* handlers without match
                // The stack should contain the handler list from BUILD_LIST 0
                EmitInstruction(ByteCodeOp.LIST_APPEND, 1); // Add any remaining unhandled exceptions
                EmitInstruction(ByteCodeOp.CALL_INTRINSIC_2, 1); // INTRINSIC_PREP_RERAISE_STAR
                EmitInstruction(ByteCodeOp.COPY, 1);
                EmitInstruction(ByteCodeOp.POP_JUMP_IF_NOT_NONE, 0); // If there are exceptions to reraise
                var reraiseJumpRef = _instructions.Count - 1;

                // No exceptions to reraise - normal completion
                EmitInstruction(ByteCodeOp.POP_TOP); // Pop the None
                EmitInstruction(ByteCodeOp.POP_EXCEPT);
                EmitJumpToLabel(ByteCodeOp.JUMP_FORWARD, continueLabel);

                // Record end of final processing
                var finalProcessingEnd = _instructions.Count;

                // Add exception table entry for final processing block
                var finalProcessingEntry = new ExceptionTableEntry(
                    start: finalProcessingStart,
                    end: finalProcessingEnd,
                    handlerLabel: reraiseLabel.Name,
                    depth: 1,
                    lasti: true
                );
                _exceptionTable.Add(finalProcessingEntry);

                // Path for reraising exceptions
                var swapReraiseLabel = CreateLabel("swap_reraise");
                MarkLabel(swapReraiseLabel);
                _instructions[reraiseJumpRef] = new ByteCodeInstruction(
                    ByteCodeOp.POP_JUMP_IF_NOT_NONE,
                    swapReraiseLabel.Offset - reraiseJumpRef - 1,
                    _instructions[reraiseJumpRef].LineNumber,
                    _instructions[reraiseJumpRef].ColumnOffset,
                    _instructions[reraiseJumpRef].FileName
                );

                EmitInstruction(ByteCodeOp.SWAP, 2);
                EmitInstruction(ByteCodeOp.POP_EXCEPT);
                EmitInstruction(ByteCodeOp.RERAISE, 0);
            }
            
            // Reraise if no handler matched (before continuation point)
            if (tryStmt.Handlers.Count > 0)
            {
                MarkLabel(reraiseLabel);
                var reraiseOffset = _instructions.Count;
                EmitInstruction(ByteCodeOp.RERAISE, 0);

                // CPython 3.12: Add single instruction protection for reraise point
                var finalReraiseHandler = CreateLabel("final_reraise_handler");
                MarkLabel(finalReraiseHandler);
                EmitInstruction(ByteCodeOp.COPY, 3);
                EmitInstruction(ByteCodeOp.POP_EXCEPT);
                EmitInstruction(ByteCodeOp.RERAISE, 1);

                // CPython 3.12: Skip individual reraise protection - will be covered by handler block entry
                #if DEBUG_LOG
                Console.WriteLine($"🔧 Reraise protection skipped for CPython compatibility optimization");
                #endif
            }
            
            // CPython 3.12: Create Exception Table entries (both try block and handler block)
            
            // 1. Main try block entry - CPython 3.12 pattern for except* handlers
            var tryBlockEntry = new ExceptionTableEntry(
                start: tryStartOffset,
                end: tryEndOffset,
                handlerLabel: handlersStartLabel.Name,
                depth: 0,  // Stack depth when exception occurs
                lasti: false  // First entry is not lasti
            );
            _exceptionTable.Add(tryBlockEntry);

            // 2. Exception handler chain protection (CPython 3.12 except* pattern)
            if (hasExceptStarHandlers)
            {
                // Handler chain runs from PUSH_EXC_INFO to end of first except* check
                var handlerChainStart = handlersStartLabel.Offset;
                var handlerChainEnd = handlerChainStart + 14;  // Rough estimate, will be updated below

                // Find the end of the first CHECK_EG_MATCH sequence
                for (int j = handlersStartLabel.Offset; j < _instructions.Count; j++)
                {
                    if (_instructions[j].OpCode == ByteCodeOp.POP_JUMP_IF_NONE)
                    {
                        handlerChainEnd = j + 1;
                        break;
                    }
                }

                var handlerChainEntry = new ExceptionTableEntry(
                    start: handlerChainStart,
                    end: handlerChainEnd,
                    handlerLabel: reraiseLabel.Name,
                    depth: 1,
                    lasti: true
                );
                _exceptionTable.Add(handlerChainEntry);
            }
            
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
                
                #if DEBUG_LOG
                Console.WriteLine($"🔧 Handler Exception Table: {handlerStartOffset} to {handlerEndOffset} -> {handlerReraiseLabel.Name} [depth=1, lasti]");
                #endif
            }
            
            // CPython 3.12: Handle finally clause if present
            if (tryStmt.FinalBody != null && tryStmt.FinalBody.Count > 0)
            {
                // Create finally handler label
                var finallyHandlerLabel = CreateLabel("finally_handler");

                // Add Exception Table entry for finally block
                // Finally blocks in CPython 3.12 protect the entire try-except construct
                var finallyBlockEntry = new ExceptionTableEntry(
                    start: tryStartOffset,
                    end: _instructions.Count,  // Current position (end of exception handlers)
                    handlerLabel: finallyHandlerLabel.Name,
                    depth: 0,
                    lasti: false
                );
                _exceptionTable.Add(finallyBlockEntry);

                // CPython 3.12: Finally handler must start at the first actual instruction
                // For print() calls, this means PUSH_NULL should be the first instruction
                // So we need to peek at the first statement and handle it specially

                if (tryStmt.FinalBody.Count > 0 && tryStmt.FinalBody[0] is ExpressionStatement exprStmt &&
                    exprStmt.Expression is CallExpression callExpr)
                {
                    // Special handling for function calls in finally block
                    // Emit PUSH_NULL first, then mark the label
                    EmitInstruction(ByteCodeOp.PUSH_NULL);
                    MarkLabel(finallyHandlerLabel);

                    // Compile the function call (will emit LOAD_GLOBAL, LOAD_CONST, CALL)
                    CompileExpression(callExpr.Function);
                    if (callExpr.Arguments.Count > 0)
                    {
                        foreach (var arg in callExpr.Arguments)
                        {
                            CompileExpression(arg);
                        }
                    }
                    EmitInstruction(ByteCodeOp.CALL, callExpr.Arguments.Count);
                    EmitInstruction(ByteCodeOp.POP_TOP);

                    // Compile remaining statements
                    for (int i = 1; i < tryStmt.FinalBody.Count; i++)
                    {
                        CompileStatement(tryStmt.FinalBody[i]);
                    }
                }
                else
                {
                    // Fallback: original behavior for non-function-call finally blocks
                    MarkLabel(finallyHandlerLabel);
                    foreach (var stmt in tryStmt.FinalBody)
                    {
                        CompileStatement(stmt);
                    }
                }

                // Finally blocks always reraise the exception after execution
                EmitInstruction(ByteCodeOp.RERAISE, 0);

                #if DEBUG_LOG
                Console.WriteLine($"🔧 Finally Exception Table: {tryStartOffset} to {_instructions.Count - 1} -> {finallyHandlerLabel.Name} [depth=0]");
                #endif
            }

            // Mark continuation point AFTER all exception handling code - this is where normal execution continues after try-except
            MarkLabel(continueLabel);

            #if DEBUG_LOG
            Console.WriteLine($"🔧 Exception Table Entries Created:");
            #endif
            #if DEBUG_LOG
            Console.WriteLine($"   Try Block: {tryStartOffset} to {tryEndOffset} -> {handlersStartLabel.Name}");
            #endif
            if (tryStmt.Handlers.Count > 0)
            {
                #if DEBUG_LOG
                Console.WriteLine($"   Handler Block: handler range -> handler_reraise [lasti]");
                #endif
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
                // Use proper assignment target compilation for correct scoping
                CompileAssignmentTarget(item.OptionalVars);
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
            
            #if DEBUG_LOG
            Console.WriteLine($"🔧 Exception Table Entry Created (Label-based):");
            #endif
            #if DEBUG_LOG
            Console.WriteLine($"   Start: {bodyStartOffset}, End: {bodyEndOffset}");
            #endif
            #if DEBUG_LOG
            Console.WriteLine($"   Handler Label: {withCleanupLabel.Name}, Depth: 1");
            #endif
            
            // 7. Normal exit: call __exit__(None, None, None) - no POP_EXCEPT needed
            // CPython 3.12: 동일한 None 상수를 재사용 (상수 풀 효율성)
            var noneConstIndex = GetOrAddConstant(PyNone.Instance);
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

            // Special optimization for simple constant patterns (like CPython)
            if (IsSimpleConstantMatch(matchStmt))
            {
                CompileSimpleConstantMatch(matchStmt);
                return;
            }

            // FOR 루프 컨텍스트 내에서 패턴 매칭인지 확인
            bool inForLoop = IsInForLoopContext();
            if (inForLoop)
            {
#if DEBUG_LOG
                Console.WriteLine("🔍 FOR 루프 컨텍스트 내 패턴 매칭 감지 - 스택 관리 특별 처리");
#endif
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
                #if DEBUG_LOG
                Console.WriteLine($"🔍 Checking Guard for case {i}: Guard={matchCase.Guard?.GetType().Name} - {matchCase.Guard}");
                #endif
                if (matchCase.Guard != null)
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 Compiling Guard pattern case {i}: {matchCase.Pattern} if {matchCase.Guard}");
                    #endif
                    
                    // Guard pattern: CPython 3.12 compatible - load subject per case
                    // Load subject fresh for this case
                    CompileExpression(matchStmt.Subject);
                    
                    // Compile pattern matching - this will bind the variable and consume subject
                    if (!CompilePatternMatch(matchCase.Pattern, failLabel))
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"🔍 Pattern match failed for case {i}, jumping to {failLabel.Name}");
                        #endif
                        EmitJumpToLabel(ByteCodeOp.JUMP_FORWARD, failLabel);
                        continue;
                    }
                    
                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 Pattern matched for case {i}, now compiling guard: {matchCase.Guard}");
                    #endif
                    
                    // Stack: [] (after pattern binding consumed subject)
                    // Now evaluate guard condition
                    CompileExpression(matchCase.Guard);
                    EmitJumpToLabel(ByteCodeOp.POP_JUMP_IF_FALSE, failLabel);
                    
                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 Guard condition compiled for case {i}, will jump to {failLabel.Name} if false");
                    #endif
                    
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
            #if DEBUG_LOG
            Console.WriteLine($"🔍 CompilePatternMatch: {pattern?.GetType().Name} - {pattern}");
            #endif
            switch (pattern)
            {
                case ConstantExpression constExpr:
                    // CPython 3.12: Direct constant comparison without COPY
                    // Stack: [subject] -> [subject, constant] -> [comparison_result]
                    CompileExpression(constExpr);
                    EmitComparison(CompareOp.EQ);
                    // Stack: [comparison_result]
                    EmitJumpToLabel(ByteCodeOp.POP_JUMP_IF_FALSE, failLabel);
                    // Stack: [] (comparison result consumed by jump)
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
                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 AsPattern: {asPattern.Pattern} as {asPattern.Name}");
                    #endif
                    
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
                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 BinaryOpExpression OR converted to OrPattern: {binaryOp.Left} | {binaryOp.Right}");
                    #endif
                    var binaryPatterns = new List<Expression> { binaryOp.Left, binaryOp.Right };
                    return CompileOrPatternLogic(binaryPatterns, failLabel);
                    
                case OrPattern orPattern:
                    // CPython 3.12: Or patterns (PEP 634)
                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 OrPattern detected with {orPattern.Patterns.Count} patterns");
                    #endif
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
                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 SequencePattern: {sequencePattern.Patterns.Count} patterns");
                    #endif
                    return CompileSequencePattern(sequencePattern, failLabel);
                    
                case MappingPattern mappingPattern:
                    // CPython 3.12: Dictionary pattern matching {"key": value}
                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 MappingPattern: {mappingPattern.Patterns.Count} patterns");
                    #endif
                    return CompileMappingPattern(mappingPattern, failLabel);
                    
                case CallExpression callExpr:
                    // CPython 3.12: Class pattern matching Point(x, y) -> MATCH_CLASS
                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 CallExpression (class pattern): {callExpr}");
                    #endif
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
            
            // 3. Handle keyword arguments - extract keyword names for MATCH_CLASS
            var keywordArgs = new List<KeywordExpression>();
            var keywordNames = new List<PyObject>();

            // Process keyword arguments from the separate Keywords property
            foreach (var kwExpr in callExpr.Keywords)
            {
                keywordArgs.Add(kwExpr);
                keywordNames.Add(new PyString(kwExpr.Arg ?? ""));
            }

            // Also check Arguments list in case keywords are stored there (fallback)
            foreach (var arg in callExpr.Arguments)
            {
                if (arg is KeywordExpression kwExpr)
                {
                    keywordArgs.Add(kwExpr);
                    keywordNames.Add(new PyString(kwExpr.Arg ?? ""));
                }
            }

            // Load keyword names tuple - CPython 3.12 compatible
            var keywordTuple = new PyTuple(keywordNames.ToArray());
            EmitLoadConst(keywordTuple);
            
            // 4. Use MATCH_CLASS with argument count (consumes subject, class, kw_names)
            // Stack: [subject, subject, class, kw_names] -> [subject, result_tuple_or_none]
            var argumentCount = callExpr.Arguments.Count;
            EmitInstruction(ByteCodeOp.MATCH_CLASS, argumentCount);
            
            // 5. Check if match succeeded (None = failure, tuple = success)
            // Stack: [subject, result_tuple_or_none] -> [subject, result_tuple_or_none, result_tuple_or_none]
            EmitInstruction(ByteCodeOp.COPY, 1); // Duplicate result for check
            EmitJumpToLabel(ByteCodeOp.POP_JUMP_IF_NONE, failLabel);
            
            // 6. Handle keyword arguments - CPython 3.12 approach
            if (keywordArgs.Count > 0)
            {
                // Unpack the attribute values extracted by MATCH_CLASS
                // Stack: [subject, result_tuple] -> [subject, attr1, attr2, ...]
                EmitInstruction(ByteCodeOp.UNPACK_SEQUENCE, keywordArgs.Count);

                // Process each keyword argument in forward order (CPython 3.12 compatible)
                // UNPACK_SEQUENCE pushes items in reverse order, so we process forward to match CPython
                for (int i = 0; i < keywordArgs.Count; i++)
                {
                    var kwExpr = keywordArgs[i];

                    if (kwExpr.Value is NameExpression nameExpr && nameExpr.Name == kwExpr.Arg)
                    {
                        // Case: Point(x=x, y=y) - capture pattern, store the value to variable
                        // Stack: [subject, ..., attrValue] -> [subject, ...]
                        EmitStoreName(nameExpr.Name);
                    }
                    else
                    {
                        // Case: Point(x=0, y=0) - literal pattern, compare with expected value
                        // Stack: [subject, ..., attrValue] -> [subject, ..., attrValue, expectedValue]
                        CompileExpression(kwExpr.Value);

                        // Stack: [subject, ..., attrValue, expectedValue] -> [subject, ..., comparisonResult]
                        EmitInstruction(ByteCodeOp.COMPARE_OP, (int)CompareOp.EQ); // CPython 3.12: == is EQ(40)

                        // Stack: [subject, ..., comparisonResult] -> [subject, ...]
                        EmitJumpToLabel(ByteCodeOp.POP_JUMP_IF_FALSE, failLabel);
                    }
                }
            }
            else
            {
                // 7. Handle positional arguments - CPython 3.12 approach
                var positionalArgs = callExpr.Arguments
                    .Where(arg => !(arg is KeywordExpression))
                    .ToList();

                if (positionalArgs.Count > 0)
                {
                    // Unpack the attribute values extracted by MATCH_CLASS
                    // Stack: [subject, result_tuple] -> [subject, attr1, attr2, ...]
                    EmitInstruction(ByteCodeOp.UNPACK_SEQUENCE, positionalArgs.Count);

                    // Process each positional argument and bind to variables
                    // Arguments are pushed in reverse order by UNPACK_SEQUENCE
                    for (int i = positionalArgs.Count - 1; i >= 0; i--)
                    {
                        var arg = positionalArgs[i];
                        if (arg is NameExpression nameExpr)
                        {
                            // Case: Point(x, y) - capture pattern, store the value to variable
                            // Stack: [subject, ..., attrValue] -> [subject, ...]
                            EmitStoreName(nameExpr.Name);
                        }
                        else
                        {
                            // Case: Point(3, 4) - literal pattern, compare with expected value
                            // Stack: [subject, ..., attrValue] -> [subject, ..., attrValue, expectedValue]
                            CompileExpression(arg);

                            // Stack: [subject, ..., attrValue, expectedValue] -> [subject, ..., comparisonResult]
                            EmitInstruction(ByteCodeOp.COMPARE_OP, (int)CompareOp.EQ);

                            // Stack: [subject, ..., comparisonResult] -> [subject, ...]
                            EmitJumpToLabel(ByteCodeOp.POP_JUMP_IF_FALSE, failLabel);
                        }
                    }
                }
                else
                {
                    // No arguments at all, just pop the result tuple
                    EmitInstruction(ByteCodeOp.POP_TOP);
                }
            }
            
            // Stack now: [subject] - this will be consumed by CompileMatch's POP_TOP
            return true;
        }
        
        private bool CompileOrPatternLogic(List<Expression> patterns, Label failLabel)
        {
            #if DEBUG_LOG
            Console.WriteLine($"🔍 CompileOrPatternLogic: {patterns.Count} patterns");
            #endif
            for (int i = 0; i < patterns.Count; i++)
            {
                #if DEBUG_LOG
                Console.WriteLine($"  Pattern {i}: {patterns[i]}");
                #endif
            }
            
            if (patterns.Count == 1)
            {
                // Single pattern - just delegate
                return CompilePatternMatch(patterns[0], failLabel);
            }
            
            // Flatten nested OR patterns to handle ((1 | 2) | 3) properly
            var flattenedPatterns = new List<Expression>();
            FlattenOrPatterns(patterns, flattenedPatterns);
            
            #if DEBUG_LOG
            Console.WriteLine($"🔍 Flattened to {flattenedPatterns.Count} patterns:");
            #endif
            for (int i = 0; i < flattenedPatterns.Count; i++)
            {
                #if DEBUG_LOG
                Console.WriteLine($"  Flattened Pattern {i}: {flattenedPatterns[i]}");
                #endif
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

            // CPython 3.12 방식: 컴파일 시점에 튜플 상수 직접 생성
            var keysArray = keysList.Select(key => new PyString(key)).ToArray();
            var keysTuple = new PyTuple(keysArray);
            EmitLoadConst(keysTuple);
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
                    
                    #if DEBUG_LOG
                    Console.WriteLine($"  → Parameter '{paramName}' with default value: {defaultValue}");
                    #endif
                }
                else
                {
                    // Parameter without default value
                    cleanParamNames.Add(arg.Trim());
                    #if DEBUG_LOG
                    Console.WriteLine($"  → Parameter '{arg}' (no default)");
                    #endif
                }
            }
            
            // Phase 1: Use Symbol Table analysis instead of FreeVariableAnalyzer
            var lambdaTable = FindLambdaSymbolTable(lambdaName);
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
                #if DEBUG_LOG
                Console.WriteLine($"  → Added parameter '{paramName}' as FAST variable at index {_varNames.Count - 1}");
                #endif
            }
            
            // Set up closure compilation if there are free variables
            if (freeVars.Count > 0)
            {
                SetupClosureCompilation(cellVars, freeVars);
            }

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
            // CPython 3.12: MAKE_CELL uses CellVars index order (0, 1, 2...)
            for (int cellIndex = 0; cellIndex < cellVars.Count; cellIndex++)
            {
                var cellVar = cellVars[cellIndex];
                if (cleanParamNames.Contains(cellVar))
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"  → Making cell for lambda parameter: {cellVar} (cell index {cellIndex})");
                    #endif
                    EmitInstruction(ByteCodeOp.MAKE_CELL, cellIndex);
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
            _currentSymbolTable = originalSymbolTable; // Restore original symbol table
            
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
                0, // posonlyArgCount
                freeVars, // Set FreeVars for closure support
                cellVars, // Set CellVars for closure support
                defaultValues: defaultValues, // CPython 3.12: Pass default values
                flags: 0,
                fileName: _currentFileName,
                sourceLines: _sourceLines
            );
            
            #if DEBUG_LOG
            Console.WriteLine($"  → Lambda code object created: {lambdaVarNames.Count} variables, {cleanParamNames.Count} parameters, {defaultValues.Count} defaults");
            #endif
            
            // CPython 3.12: Handle default values if present (스택 순서 1)
            if (defaultValues.Count > 0)
            {
                // Load default values onto stack
                foreach (var defaultValue in defaultValues)
                {
                    EmitLoadConst(defaultValue);
                }
                EmitInstruction(ByteCodeOp.BUILD_TUPLE, defaultValues.Count);
                #if DEBUG_LOG
                Console.WriteLine($"  → Built defaults tuple: {defaultValues.Count} defaults");
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
            if (defaultValues.Count > 0)
            {
                flags |= MakeFunctionFlags.DEFAULTS;
            }
            if (freeVars.Count > 0)
            {
                flags |= MakeFunctionFlags.CLOSURE;
            }
            
            #if DEBUG_LOG
            Console.WriteLine($"  → MAKE_FUNCTION flags: {flags} (defaults={defaultValues.Count > 0}, closure={freeVars.Count > 0})");
            #endif
            EmitInstruction(ByteCodeOp.MAKE_FUNCTION, flags);
        }
        
        // Lambda counter for unique names
        private static int _lambdaCounter = 0;

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

            #if DEBUG_LOG
            Console.WriteLine($"  Symbol Table Free Variables: [{string.Join(", ", freeVars)}]");
            Console.WriteLine($"  Symbol Table Cell Variables: [{string.Join(", ", cellVars)}]");
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
            #if DEBUG_LOG
            Console.WriteLine($"🔍 MarkLabel: {label.Name} → offset {label.Offset}, {label.References.Count} references");
            #endif
            
            // Update all references to this label
            foreach (var refIndex in label.References)
            {
                var oldInstruction = _instructions[refIndex];
                #if DEBUG_LOG
                Console.WriteLine($"🔍 Updating ref {refIndex}: {oldInstruction.OpCode} from arg {oldInstruction.Argument}");
                #endif
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
                
                #if DEBUG_LOG
                Console.WriteLine($"🔍 Updated to arg {argument} (offset {label.Offset} - {refIndex} - 1 = {label.Offset - (refIndex + 1)})");
                #endif
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
                #if DEBUG_LOG
                Console.WriteLine($"🔧 Exception Table 해석: {_exceptionTable.Count}개 엔트리");
                #endif
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
                        #if DEBUG_LOG
                        Console.WriteLine($"   ✅ 라벨 '{entry.HandlerLabelName}' → 오프셋 {entry.HandlerOffset}");
                        #endif
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
                    #if DEBUG_LOG
                    Console.WriteLine($"    → FOR_ITER 패치 (중첩 루프 지원): loop start {context.ForIterInstruction}, jump offset {relativeJump}, END_FOR at {endForPosition}");
                    #endif
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

            // 중첩 깊이 추적 시작 (리스트 컴프리헨션)
            _comprehensionNestingDepth++;
            #if DEBUG_LOG
            Console.WriteLine($"🔍 List comprehension 중첩 깊이 증가: {_comprehensionNestingDepth}");
            #endif
            
            // CPython 3.12 패턴: 컴프리헨션 변수 사전 할당 및 정리 - 튜플 언패킹 지원
            var comprehensionVars = new List<string>();
            foreach (var gen in listComp.Generators)
            {
                CollectComprehensionVars(gen.Target, comprehensionVars);
            }
            
            #if DEBUG_LOG
            Console.WriteLine($"🔧 List comprehension vars: {string.Join(", ", comprehensionVars)} (count: {comprehensionVars.Count})");
            #endif
            
            // 1. First compile the iterator source (CPython 3.12 pattern)
            var firstGenerator = listComp.Generators[0];

            // 1. CPython 3.12 정확한 순서: LOAD_CONST → GET_ITER → LOAD_FAST_AND_CLEAR → SWAP → BUILD_LIST → SWAP
            // 첫 번째 generator의 처리 방식 결정 (단순화)
            bool firstGeneratorOptimized = false; // 복잡한 최적화 제거

            if (!firstGeneratorOptimized)
            {
                // 첫 번째 generator가 일반 루프인 경우: GET_ITER 생성
                if (firstGenerator.Iter is ListExpression iterList &&
                    iterList.Elements.All(e => e is ConstantExpression))
                {
                    // 상수 리스트 → 상수 튜플로 변환 (CPython 3.12 패턴)
                    var constantElements = iterList.Elements.Cast<ConstantExpression>()
                                                          .Select(c => c.Value)
                                                          .ToArray();
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
            foreach (var varName in comprehensionVars)
            {
                EmitInstruction(ByteCodeOp.LOAD_FAST_AND_CLEAR, GetOrAddVarName(varName));
            }

            // 3. 첫 번째 SWAP: 스택 재배치 (CPython 3.12 정확한 순서)
            if (comprehensionVars.Count > 0)
            {
                // CPython 3.12: SWAP 값 계산
                // 일반적인 경우: 변수 개수 + 1 (iterator 포함)
                // 첫 번째 generator 최적화된 경우: 변수 개수만 (iterator 없음)
                int swapArg = firstGeneratorOptimized ? comprehensionVars.Count : comprehensionVars.Count + 1;
                #if DEBUG_LOG
                Console.WriteLine($"🔧 CPython 3.12 첫 번째 SWAP: vars={comprehensionVars.Count}, firstOptimized={firstGeneratorOptimized}, swapArg={swapArg}");
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
            
            // 4. 중첩된 루프 컴파일 - CPython 3.12 방식
            var exceptionTableStart = _instructions.Count;
            int loopStart = -1;

            // 첫 번째 generator 처리 (단순화 - 항상 일반 루프로 처리)
            if (false) // 복잡한 최적화 제거
            {
                // 첫 번째 generator도 최적화 - FOR_ITER 없이 직접 할당
                if (firstGenerator.Iter is ListExpression listExpr)
                {
                    CompileExpression(listExpr.Elements[0]);
                }
                else if (firstGenerator.Iter is TupleExpression tupleExpr)
                {
                    CompileExpression(tupleExpr.Elements[0]);
                }
                CompileComprehensionTarget(firstGenerator.Target, comprehensionVars);
            }
            else
            {
                // 첫 번째 generator는 이미 처리했으므로 FOR_ITER부터 시작
                loopStart = _instructions.Count;
                EmitInstruction(ByteCodeOp.FOR_ITER, 0); // 패치 대상

                // 첫 번째 generator의 타겟 변수 저장 - 재귀 튜플 언패킹 지원
                CompileComprehensionTarget(firstGenerator.Target, comprehensionVars);
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
            var (listAppendArg, useNestedLoops) = DetectComprehensionPattern(listComp.Generators);
            int listAppendStart = _instructions.Count;

            if (listComp.Generators.Count > 1)
            {
                // CPython 3.12: 항상 nested loops 사용 (단순화)
                #if DEBUG_LOG
                Console.WriteLine($"🔧 Nested loops 모드: {listComp.Generators.Count}개 generator");
                #endif
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
            

            // JUMP_BACKWARD - CPython 3.12 통일된 oparg 계산 (실제 루프가 있는 경우에만)
            if (loopStart >= 0)
            {
                // CPython 3.12: 통일된 JUMP_BACKWARD oparg 계산 사용
                int currentPos = _instructions.Count;
                // JUMP_BACKWARD는 FOR_ITER 위치로 점프해야 함 (loopStart가 FOR_ITER의 실제 위치)
                int forIterPos = loopStart;
                int jumpBackwardArg = CalculateJumpBackwardArg(currentPos, forIterPos);

                #if DEBUG_LOG
                Console.WriteLine($"🔧 JUMP_BACKWARD 컴파일: currentPos={currentPos}, loopStart={loopStart}");
                Console.WriteLine($"   jumpBackwardArg={jumpBackwardArg}");
                #endif

                // CPython 3.12: JUMP_BACKWARD는 바이트 단위 오프셋 사용 (명령어 단위가 아님)
                EmitInstruction(ByteCodeOp.JUMP_BACKWARD, jumpBackwardArg);
            }
            
            // END_FOR 라벨 (FOR_ITER 패치용) - 실제 FOR_ITER가 있는 경우에만
            if (loopStart >= 0)
            {
                var endFor = _instructions.Count;
                EmitInstruction(ByteCodeOp.END_FOR);

                // FOR_ITER 패치 - CPython 3.12 바이트 오프셋 방식
                int forIterJump;
                if (SharpPyConfig._enable_optimizer)
                {
                    // 최적화 모드: 명령어 단위 계산 (SharpPy는 최적화된 상태)
                    forIterJump = endFor - loopStart - 1;
                }
                else
                {
                    // 비최적화 모드: 바이트 오프셋 계산
                    int currentByteOffset = PyJumpBackwardUtil.CalculateByteOffset(loopStart, _instructions);
                    int targetByteOffset = PyJumpBackwardUtil.CalculateByteOffset(endFor, _instructions);
                    forIterJump = (targetByteOffset - currentByteOffset - 2) / 2;
                }

                _instructions[loopStart] = new ByteCodeInstruction(
                    ByteCodeOp.FOR_ITER,
                    forIterJump
                );
            }
            
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

                // JUMP_BACKWARD: 조건이 거짓이면 FOR_ITER로 돌아감 (FOR_ITER가 있는 경우에만)
                if (loopStart >= 0)
                {
                    int jumpBackArg = CalculateJumpBackwardArg(jumpBackwardIndex, loopStart);
                    _instructions[jumpBackwardIndex] = new ByteCodeInstruction(
                        ByteCodeOp.JUMP_BACKWARD,
                        jumpBackArg
                    );
                }
            }
            
            // fallback JUMP_BACKWARD 패치 제거 - 이제 조건문 처리에서 직접 생성함
            
            // 5. 정상 완료 시 스택 정리 - CPython 3.12 패턴
            // CPython 3.12: List comprehension cleanup은 Assignment statement에서 처리
            // 여기서는 cleanup을 지연시키고 PendingCleanup으로 등록만 함

            // CPython 3.12: List comprehension 정상 완료 - 결과 리스트가 스택에 남음
            // Assignment target은 이 지점에서 AssignStatement에 의해 처리됨

            // CPython 3.12 PEP 709: 정상 종료 시 컴프리헨션 변수 복원
            // CPython 패턴: 한 번의 SWAP으로 모든 변수를 재배치한 후 순차적으로 저장
            if (comprehensionVars.Count > 0)
            {
                // SWAP으로 스택 재배치: comprehensionVars.Count + 1
                EmitInstruction(ByteCodeOp.SWAP, comprehensionVars.Count + 1);
                #if DEBUG_LOG
                Console.WriteLine($"🔄 CPython 3.12 스택 재배치: SWAP {comprehensionVars.Count + 1}");
                #endif

                // 역순으로 변수 저장 (CPython 3.12 패턴: 마지막 변수부터)
                for (int i = comprehensionVars.Count - 1; i >= 0; i--)
                {
                    var varName = comprehensionVars[i];
                    EmitInstruction(ByteCodeOp.STORE_FAST, GetOrAddVarName(varName));
                    #if DEBUG_LOG
                    Console.WriteLine($"🔄 변수 복원: {varName} (STORE_FAST {GetOrAddVarName(varName)})");
                    #endif
                }
            }

            // Exception table end는 변수 복원 완료 후에 설정 (CPython 3.12 호환)
            var exceptionTableEnd = _instructions.Count;

            // CPython 3.12: Exception handler를 지연 생성으로 등록
            var pendingHandler = new PendingExceptionHandler
            {
                StartOffset = exceptionTableStart,         // 명령어 인덱스 사용
                EndOffset = exceptionTableEnd,             // 명령어 인덱스 사용
                ComprehensionVars = new List<string>(comprehensionVars),
                Depth = 2
            };
            #if DEBUG_LOG
            Console.WriteLine($"🔧 PendingExceptionHandler 추가: start={exceptionTableStart}, end={exceptionTableEnd}, vars=[{string.Join(", ", comprehensionVars)}], depth=2");
            #endif
            _pendingExceptionHandlers.Add(pendingHandler);
            #if DEBUG_LOG
            Console.WriteLine($"🔧 현재 _pendingExceptionHandlers.Count: {_pendingExceptionHandlers.Count}");
            #endif
            
            // CPython 3.12: 컴프리헨션 컨텍스트 종료
            _isInComprehension = savedIsInComprehension;

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
            #if DEBUG_LOG
            Console.WriteLine($"  🔄 Generator [{currentIndex}]: {generator.Target} in {generator.Iter}");
            #endif
            
            // Generator 처리 (단순화 - 항상 일반 루프로 처리)
            if (false) // 복잡한 최적화 제거
            {
                // 단일 요소 generator 최적화 - CPython처럼 직접 할당
                if (generator.Iter is ListExpression listExpr)
                {
                    CompileExpression(listExpr.Elements[0]);
                }
                else if (generator.Iter is TupleExpression tupleExpr)
                {
                    CompileExpression(tupleExpr.Elements[0]);
                }
                CompileComprehensionTarget(generator.Target, comprehensionVars);

                // 조건 검사 (if문이 있는 경우)
                foreach (var condition in generator.Ifs)
                {
                    CompileExpression(condition);
                    EmitInstruction(ByteCodeOp.POP_JUMP_IF_FALSE, 0); // 패치 대상
                }

                // 다음 generator 재귀 호출 (단일 요소이므로 루프 없음)
                CompileNestedGenerators(generators, currentIndex + 1, comprehensionVars, innerBlock);
            }
            else
            {
                // 이터레이터 준비 - 중첩 컴프리헨션에서는 BUILD_LIST 최적화 없이 직접 이터레이터 생성
                CompileGeneratorIterable(generator.Iter);
                EmitInstruction(ByteCodeOp.GET_ITER);

                // 루프 시작 라벨
                var loopStart = _instructions.Count;
                EmitInstruction(ByteCodeOp.FOR_ITER, 0); // 패치 대상

                // 루프 변수 저장 - 재귀 튜플 언패킹 지원
                CompileComprehensionTarget(generator.Target, comprehensionVars);

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
            
            #if DEBUG_LOG
            Console.WriteLine($"🔧 FOR_ITER 패치 전 상태:");
            #endif
            #if DEBUG_LOG
            Console.WriteLine($"    Generator[{currentIndex}]: {generator.Target} in {generator.Iter}");
            #endif
            #if DEBUG_LOG
            Console.WriteLine($"    현재 바이트코드 길이: {_instructions.Count}");
            #endif
            #if DEBUG_LOG
            Console.WriteLine($"    FOR_ITER 위치: {loopStart}");
            #endif
            
            // END_FOR 명령어 추가 (CPython 3.12 패턴)
            int endForPosition = _instructions.Count;
            EmitInstruction(ByteCodeOp.END_FOR, 0);
            #if DEBUG_LOG
            Console.WriteLine($"    END_FOR 추가 위치: {endForPosition}");
            #endif
            
            // CPython 3.12와 동일한 오프셋 계산
            // FOR_ITER 실행 시: InstructionPointer += argument, 그 후 메인 루프 +1
            // 따라서 END_FOR에 도달하려면: endForPosition - loopStart - 1
            var relativeJump = endForPosition - loopStart - 1;
            var originalInstruction = _instructions[loopStart];
            _instructions[loopStart] = new ByteCodeInstruction(
                ByteCodeOp.FOR_ITER, 
                relativeJump
            );
            #if DEBUG_LOG
            Console.WriteLine($"🔧 FOR_ITER 패치 완료:");
            #endif
            #if DEBUG_LOG
            Console.WriteLine($"    위치 {loopStart}: 원래 인수 {originalInstruction.Argument} → 새 인수 {relativeJump}");
            #endif
            #if DEBUG_LOG
            Console.WriteLine($"    점프 계산: END_FOR({endForPosition}) - FOR_ITER({loopStart}) - 1 = {relativeJump}");
            #endif
            #if DEBUG_LOG
            Console.WriteLine($"    VM 실행 시 점프될 위치: {loopStart + 1 + relativeJump}");
            #endif
            
            // Dict comprehension의 경우 STORE_GLOBAL이 건너뛰어지는 문제 디버깅
            if (_isInComprehension)
            {
                #if DEBUG_LOG
                Console.WriteLine($"📋 Comprehension 컨텍스트에서 FOR_ITER 패치:");
                #endif
                #if DEBUG_LOG
                Console.WriteLine($"    다음 명령어들 위치 예상:");
                #endif
                for (int i = endForPosition + 1; i < Math.Min(endForPosition + 5, _instructions.Count); i++)
                {
                    if (i < _instructions.Count)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"    위치 {i}: {_instructions[i].OpCode} {_instructions[i].Argument}");
                        #endif
                    }
                }
            }
            } // Close else block for single-element list optimization
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
            if (IsCellVariable(name))
            {
                // Cell 변수일 때는 STORE_DEREF 사용
                var cellIndex = GetCellVariableIndex(name);
                EmitInstruction(ByteCodeOp.STORE_DEREF, cellIndex);
                #if DEBUG_LOG
                Console.WriteLine($"    → 컴프리헨션 변수 저장: {name} (STORE_DEREF index {cellIndex})");
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
            CompileDictComprehensionRecursive(dictComp, 0);
        }

        /// <summary>
        /// 재귀적 dict comprehension 컴파일 - CPython 3.12 패턴
        /// </summary>
        private void CompileDictComprehensionRecursive(DictComprehension dictComp, int nestingLevel)
        {
            #if DEBUG_LOG
            Console.WriteLine($"🚀 재귀적 Dict Comprehension 컴파일 (Level {nestingLevel})");
            #endif

            // CPython 3.12: 컴프리헨션 컨텍스트 시작
            var savedIsInComprehension = _isInComprehension;
            _isInComprehension = true;

            // 1. 현재 레벨부터 최하위까지 모든 변수 수집 (CPython 3.12 패턴)
            var allVarsFromThisLevel = CollectNestedVarsFromLevel(dictComp, nestingLevel);

            #if DEBUG_LOG
            Console.WriteLine($"🔧 Level {nestingLevel} vars: [{string.Join(", ", allVarsFromThisLevel)}] (count: {allVarsFromThisLevel.Count})");
            #endif

            // 2. 첫 번째 generator의 iterable 컴파일
            var firstGenerator = dictComp.Generators[0];
            CompileExpression(firstGenerator.Iter);
            EmitInstruction(ByteCodeOp.GET_ITER);

            // 3. CPython 3.12 패턴: 현재 레벨부터 최하위까지 모든 변수를 LOAD_FAST_AND_CLEAR
            foreach (var varName in allVarsFromThisLevel)
            {
                EmitInstruction(ByteCodeOp.LOAD_FAST_AND_CLEAR, GetOrAddVarName(varName));
            }

            // 4. CPython 3.12: SWAP + BUILD_MAP + SWAP 패턴
            if (allVarsFromThisLevel.Count > 0)
            {
                int swapArg = allVarsFromThisLevel.Count + 1;
                EmitInstruction(ByteCodeOp.SWAP, swapArg);
                #if DEBUG_LOG
                Console.WriteLine($"🔧 Level {nestingLevel} Initial SWAP: vars={allVarsFromThisLevel.Count}, swapArg={swapArg}");
                #endif
            }

            EmitInstruction(ByteCodeOp.BUILD_MAP, 0);

            if (allVarsFromThisLevel.Count > 0)
            {
                EmitInstruction(ByteCodeOp.SWAP, 2);
            }

            // 5. 중첩된 루프 컴파일
            var exceptionTableStart = _instructions.Count;
            var loopStart = _instructions.Count;
            EmitInstruction(ByteCodeOp.FOR_ITER, 0); // 패치 대상

            // 현재 레벨 변수만 저장 (타겟 변수)
            var currentLevelVars = new List<string>();
            CollectComprehensionVars(firstGenerator.Target, currentLevelVars);
            CompileComprehensionTarget(firstGenerator.Target, currentLevelVars);

            // 6. 나머지 generator들과 내부 블록 처리
            if (dictComp.Generators.Count > 1)
            {
                CompileNestedGenerators(dictComp.Generators, 1, currentLevelVars, () =>
                {
                    CompileInnerBlock();
                });
            }
            else
            {
                CompileInnerBlock();
            }

            void CompileInnerBlock()
            {
                // Key 컴파일
                CompileExpression(dictComp.Key);

                // Value 컴파일 - 중첩 comprehension이면 재귀 호출
                if (dictComp.Value is DictComprehension nestedComp)
                {
                    // 재귀 호출: 중첩된 comprehension 컴파일
                    CompileDictComprehensionRecursive(nestedComp, nestingLevel + 1);
                }
                else
                {
                    // 일반 표현식
                    CompileExpression(dictComp.Value);
                }

                // CPython 3.12 MAP_ADD offset 계산 - 개선된 nested comprehension 처리
                int forLoopCount = dictComp.Generators.Count;
                int mapAddArg;

                if (_comprehensionNestingDepth >= 1)
                {
                    // 중첩된 dict comprehension: LIST_APPEND와 같은 공식 시도
                    // LIST_APPEND가 성공한 공식: generatorCount + 1
                    mapAddArg = forLoopCount + 1;
                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 MAP_ADD depth 계산 (중첩): nesting={_comprehensionNestingDepth}, generators={forLoopCount}, depth={mapAddArg}");
                    #endif
                }
                else
                {
                    // 일반적인 경우: generator 수 + 1
                    mapAddArg = forLoopCount + 1;
                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 MAP_ADD depth 계산 (단독): {forLoopCount} generators → depth {mapAddArg}");
                    #endif
                }
                EmitInstruction(ByteCodeOp.MAP_ADD, mapAddArg);
                #if DEBUG_LOG
                Console.WriteLine($"🗝️ Level {nestingLevel} MAP_ADD {mapAddArg} (allVars: {allVarsFromThisLevel.Count})");
                #endif
            }

            // 7. FOR_ITER 루프 마무리
            var jumpBackwardArg = CalculateJumpBackwardArg(_instructions.Count, loopStart);
            EmitInstruction(ByteCodeOp.JUMP_BACKWARD, jumpBackwardArg);

            // FOR_ITER 패치
            var endForPosition = _instructions.Count;
            EmitInstruction(ByteCodeOp.END_FOR);
            var jumpDistance = endForPosition - loopStart - 1;
            _instructions[loopStart] = new ByteCodeInstruction(ByteCodeOp.FOR_ITER, jumpDistance);

            // 8. CPython 3.12: 변수들 복원 (SWAP + STORE_FAST 역순)
            if (allVarsFromThisLevel.Count > 0)
            {
                int swapValue = allVarsFromThisLevel.Count + 1;
                EmitInstruction(ByteCodeOp.SWAP, swapValue);

                // 변수들을 역순으로 저장 (CPython 3.12 패턴)
                for (int i = allVarsFromThisLevel.Count - 1; i >= 0; i--)
                {
                    EmitInstruction(ByteCodeOp.STORE_FAST, GetOrAddVarName(allVarsFromThisLevel[i]));
                }
            }

            // 9. Exception handler 등록
            var exceptionTableEnd = _instructions.Count;
            var pendingHandler = new PendingExceptionHandler
            {
                StartOffset = exceptionTableStart,
                EndOffset = exceptionTableEnd,
                ComprehensionVars = new List<string>(allVarsFromThisLevel),
                Depth = allVarsFromThisLevel.Count + 1
            };
            _pendingExceptionHandlers.Add(pendingHandler);

            // 컴프리헨션 컨텍스트 종료
            _isInComprehension = savedIsInComprehension;

            #if DEBUG_LOG
            Console.WriteLine($"✅ 재귀적 Dict comprehension Level {nestingLevel} 완료 ({dictComp.Generators.Count}개 generator)");
            #endif
        }



        /// <summary>
        /// 중첩 dict comprehension에서 현재 레벨부터 최하위까지의 모든 변수 수집
        /// CPython 3.12 패턴: Level N → [자신부터 최하위까지 모든 변수]
        /// </summary>
        private List<string> CollectNestedVarsFromLevel(DictComprehension dictComp, int currentLevel = 0)
        {
            var vars = new List<string>();

            // 현재 레벨 변수 수집
            foreach (var generator in dictComp.Generators)
            {
                CollectComprehensionVars(generator.Target, vars);
            }

            // 중첩된 comprehension이 있으면 재귀적으로 수집
            if (dictComp.Value is DictComprehension nestedComp)
            {
                var nestedVars = CollectNestedVarsFromLevel(nestedComp, currentLevel + 1);
                // 중복 제거하면서 추가
                foreach (var nestedVar in nestedVars)
                {
                    if (!vars.Contains(nestedVar))
                    {
                        vars.Add(nestedVar);
                    }
                }
            }

            #if DEBUG_LOG
            Console.WriteLine($"🔍 CollectNestedVarsFromLevel({currentLevel}): [{string.Join(", ", vars)}]");
            #endif

            return vars;
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
                    if (list.Elements.All(e => e is ConstantExpression))
                    {
                        // 모든 요소가 상수인 경우 - 튜플 상수로 직접 로드
                        var constantElements = list.Elements.Cast<ConstantExpression>()
                                                           .Select(c => c.Value)
                                                           .ToArray();
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

            // CPython 3.12: Exception table 시작 위치 기록
            int exceptionTableStart = 0;
            int buildSetPosition = _instructions.Count;

            // CPython 3.12: Set comprehension 스택 준비 (LIST comprehension과 동일한 패턴)
            var firstGenerator = setComp.Generators[0];
            var comprehensionVars = new List<string>();

            // 먼저 첫 번째 generator의 iterable 로드
            CompileExpression(firstGenerator.Iter);
            EmitInstruction(ByteCodeOp.GET_ITER);

            // 각 generator 변수를 컴프리헨션 변수로 추가
            foreach (var gen in setComp.Generators)
            {
                if (gen.Target is NameExpression nameExpr)
                {
                    comprehensionVars.Add(nameExpr.Name);
                }
            }

            // CPython 3.12: LOAD_FAST_AND_CLEAR
            foreach (var varName in comprehensionVars)
            {
                EmitInstruction(ByteCodeOp.LOAD_FAST_AND_CLEAR, GetOrAddVarName(varName));
            }

            // CPython 3.12: 첫 번째 SWAP
            if (comprehensionVars.Count > 0)
            {
                int swapArg = comprehensionVars.Count + 1;
                #if DEBUG_LOG
                Console.WriteLine($"🔧 CPython 3.12 첫 번째 SWAP: vars={comprehensionVars.Count}, swapArg={swapArg}");
                #endif
                EmitInstruction(ByteCodeOp.SWAP, swapArg);
            }

            // 1. 빈 셋 생성
            EmitInstruction(ByteCodeOp.BUILD_SET, 0);
            #if DEBUG_LOG
            Console.WriteLine($"📊 Set comprehension 시작 위치: {buildSetPosition}");
            #endif
            #if DEBUG_LOG
            Console.WriteLine($"🔧 BUILD_SET 위치: {buildSetPosition}");
            #endif

            // CPython 3.12: 두 번째 SWAP
            if (comprehensionVars.Count > 0)
            {
                EmitInstruction(ByteCodeOp.SWAP, 2);
                #if DEBUG_LOG
                Console.WriteLine($"🔧 CPython 3.12 두 번째 SWAP 2");
                #endif
            }

            int beforeGenerators = _instructions.Count;
            #if DEBUG_LOG
            Console.WriteLine($"🔄 CompileNestedGenerators 호출 전 위치: {beforeGenerators}");
            #endif

            // CPython 3.12 SET_ADD offset 계산 - 개선된 nested comprehension 처리
            int setAddDepth;
            if (_comprehensionNestingDepth >= 1)
            {
                // 중첩된 set comprehension: 더 정확한 계산
                // 스택 구조: [outer_container, iter1, vars..., inner_set, iter2, vars...]
                // inner_set까지의 거리를 계산 - 1 더 추가
                setAddDepth = setComp.Generators.Count + _comprehensionNestingDepth + 3;
                #if DEBUG_LOG
                Console.WriteLine($"🔧 SET_ADD depth 계산 (중첩): nesting={_comprehensionNestingDepth}, generators={setComp.Generators.Count}, depth={setAddDepth}");
                #endif
            }
            else
            {
                // 일반적인 경우: generator 수 + 1
                setAddDepth = setComp.Generators.Count + 1;  // 단일:2, 이중:3, 삼중:4
                #if DEBUG_LOG
                Console.WriteLine($"🔧 SET_ADD depth 계산 (단독): {setComp.Generators.Count} generators → depth {setAddDepth}");
                #endif
            }

            // 3. 첫 번째 generator FOR_ITER 직접 생성
            var forIterStartPosition = _instructions.Count;
            EmitInstruction(ByteCodeOp.FOR_ITER, 0); // 패치 예정

            // 첫 번째 generator target 컴파일
            CompileComprehensionTarget(firstGenerator.Target, comprehensionVars);

            // 나머지 generator들 처리 (있는 경우)
            if (setComp.Generators.Count > 1)
            {
                CompileNestedGenerators(setComp.Generators, 1, comprehensionVars, () =>
                {
                    CompileExpression(setComp.Element);
                    EmitInstruction(ByteCodeOp.SET_ADD, setAddDepth);
                });
            }
            else
            {
                // 단일 generator인 경우 직접 처리
                // 조건부 처리 (if 절이 있는 경우)
                foreach (var condition in firstGenerator.Ifs)
                {
                    CompileExpression(condition);
                    var conditionJump = _instructions.Count;
                    EmitInstruction(ByteCodeOp.POP_JUMP_IF_TRUE, 0); // 조건이 참이면 SET_ADD로 점프
                    EmitInstruction(ByteCodeOp.JUMP_BACKWARD, 0); // 패치 대상 - FOR_ITER로 돌아감
                }

                // element 값 계산 및 SET_ADD
                CompileExpression(setComp.Element);
                EmitInstruction(ByteCodeOp.SET_ADD, setAddDepth);
            }

            // JUMP_BACKWARD to FOR_ITER
            var jumpBackwardPos = _instructions.Count;
            var jumpBackwardArg = PyJumpBackwardUtil.CalculateJumpBackwardOpArg(jumpBackwardPos, forIterStartPosition, _instructions);
            EmitInstruction(ByteCodeOp.JUMP_BACKWARD, jumpBackwardArg);

            // END_FOR
            var endForPosition = _instructions.Count;
            EmitInstruction(ByteCodeOp.END_FOR);

            // FOR_ITER 패치
            var forIterOffset = endForPosition - forIterStartPosition - 1;
            _instructions[forIterStartPosition] = new ByteCodeInstruction(ByteCodeOp.FOR_ITER, forIterOffset);

            // CPython 3.12: 스택 복원 (LIST comprehension과 동일)
            if (comprehensionVars.Count > 0)
            {
                EmitInstruction(ByteCodeOp.SWAP, comprehensionVars.Count + 1);
                for (int i = comprehensionVars.Count - 1; i >= 0; i--)
                {
                    EmitInstruction(ByteCodeOp.STORE_FAST, GetOrAddVarName(comprehensionVars[i]));
                }
            }

            #if DEBUG_LOG
            Console.WriteLine($"🔧 FOR_ITER 패치: 위치 {forIterStartPosition}, offset {forIterOffset}");
            Console.WriteLine($"🗝️ SET_ADD depth: {setAddDepth}");
            #endif

            // Exception table 시작 위치는 첫 번째 FOR_ITER 명령어
            if (exceptionTableStart == 0)
            {
                // FOR_ITER 명령어를 찾기 위해 역방향 스캔
                for (int i = _instructions.Count - 1; i >= buildSetPosition; i--)
                {
                    if (_instructions[i].OpCode == ByteCodeOp.FOR_ITER)
                    {
                        exceptionTableStart = i;
                        break;
                    }
                }
            }

            #if DEBUG_LOG
            Console.WriteLine($"🔧 Set Exception table 시작 위치: {exceptionTableStart}");
            #endif

            int afterGenerators = _instructions.Count;
            #if DEBUG_LOG
            Console.WriteLine($"🔄 CompileNestedGenerators 완료 후 위치: {afterGenerators}");
            #endif

            // CPython 3.12: Exception table 종료 위치 계산
            int exceptionTableEnd = exceptionTableStart * 2 + 22; // SET_ADD까지의 바이트코드 범위

            // CPython 3.12: Exception handler를 지연 생성으로 등록 (Set comprehension용)
            if (comprehensionVars.Count > 0)
            {
                var pendingHandler = new PendingExceptionHandler
                {
                    StartOffset = exceptionTableStart,        // 명령어 인덱스 사용
                    EndOffset = exceptionTableEnd,
                    ComprehensionVars = new List<string>(comprehensionVars),
                    Depth = 2  // Set comprehension은 depth=2 (CPython 호환)
                };

                _pendingExceptionHandlers.Add(pendingHandler);
                #if DEBUG_LOG
                Console.WriteLine($"🔧 Set PendingExceptionHandler 추가: start={pendingHandler.StartOffset}, end={pendingHandler.EndOffset}, vars=[{string.Join(", ", comprehensionVars)}], depth={pendingHandler.Depth}");
                #endif
                #if DEBUG_LOG
                Console.WriteLine($"🔧 현재 _pendingExceptionHandlers.Count: {_pendingExceptionHandlers.Count}");
                #endif
            }

            // CPython 3.12: 컴프리헨션 컨텍스트 종료
            _isInComprehension = savedIsInComprehension;

            #if DEBUG_LOG
            Console.WriteLine($"✅ Set comprehension 바이트코드 인라인 완료 ({setComp.Generators.Count}개 중첩 generator)");
            #endif
            #if DEBUG_LOG
            Console.WriteLine($"📊 Set comprehension 최종 위치: {afterGenerators}");
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
                        "and",
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
                var targetName = generator.Target is NameExpression ne ? ne.Name : $"var{i}";

                // for문의 바디는 현재까지 구성된 innerMostStatement
                var forBody = new List<Statement> { innerMostStatement };

                // 첫 번째 generator는 .0을 사용, 나머지는 각자의 iterable 사용
                Expression iterableExpr = (i == 0) ? iteratorExpr : generator.Iter;

                innerMostStatement = new ForStatement(targetName, iterableExpr, forBody);
            }

            // CPython 3.12: 최종 generator statement
            var genStatements = new List<Statement> { innerMostStatement };
            
            // CPython 3.12: 제너레이터 표현식은 iterator를 .0 매개변수로 받음
            var parameters = new List<string> { ".0" };  // 매개변수는 .0 하나
            var defaults = new List<PyObject>();  // 기본값 없음
            var flags = PyCodeObject.CO_GENERATOR;  // CO_GENERATOR 플래그 설정
            var genCode = genCompiler.CompileFunction(genStatements, "<genexpr>", parameters, defaults, flags);
            
            // 제너레이터 함수 객체 생성
            EmitLoadConst(genCode);
            EmitInstruction(ByteCodeOp.MAKE_FUNCTION, 0);
            
            // CPython 3.12: 올바른 스택 순서로 호출
            CompileExpression(outerGenerator.Iter);  // range(5) 컴파일
            EmitInstruction(ByteCodeOp.GET_ITER);  // iterator 생성
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
                // Comprehension 변수들을 미리 수집
                if (isListComprehension)
                {
                    var listComp = (ListComprehension)assignTarget.Value;
                    foreach (var generator in listComp.Generators)
                    {
                        if (generator.Target is NameExpression name)
                        {
                            comprehensionVars.Add(name.Name);
                        }
                    }
                }
                else if (isDictComprehension)
                {
                    var dictComp = (DictComprehension)assignTarget.Value;
                    foreach (var generator in dictComp.Generators)
                    {
                        if (generator.Target is NameExpression name)
                        {
                            comprehensionVars.Add(name.Name);
                        }
                    }
                }
                else if (isSetComprehension)
                {
                    var setComp = (SetComprehension)assignTarget.Value;
                    foreach (var generator in setComp.Generators)
                    {
                        if (generator.Target is NameExpression name)
                        {
                            comprehensionVars.Add(name.Name);
                        }
                    }
                }
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
            // Check free variables first (for class body compilation)
            var freeIndex = _freeVars.IndexOf(varName);
            if (freeIndex != -1)
            {
                #if DEBUG_LOG
                Console.WriteLine($"    📋 LOAD_CLOSURE for free var: {varName} (free index {freeIndex})");
                #endif
                EmitInstruction(ByteCodeOp.LOAD_CLOSURE, freeIndex);
                return;
            }

            // Check cell variables (for regular function compilation)
            var cellIndex = _cellVars.IndexOf(varName);
            if (cellIndex != -1)
            {
                // CPython 3.12: Cell variables come after free variables in instruction indices
                var instructionIndex = _freeVars.Count + cellIndex;
                #if DEBUG_LOG
                Console.WriteLine($"    📋 LOAD_CLOSURE for cell var: {varName} (cell index {cellIndex} → instruction index {instructionIndex})");
                #endif
                EmitInstruction(ByteCodeOp.LOAD_CLOSURE, instructionIndex);
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

            var wildcardLabel = CreateLabel($"match_wildcard_{_labelCounter++}");
            EmitJumpToLabel(ByteCodeOp.POP_JUMP_IF_FALSE, wildcardLabel);

            // 첫 번째 케이스 (상수 매칭) 컴파일
            foreach (var stmt in matchStmt.Cases[0].Body)
            {
                CompileStatement(stmt);
            }
            EmitInstruction(ByteCodeOp.RETURN_CONST, GetOrAddConstant(PyNone.Instance));

            // wildcard 케이스 - CPython과 동일한 구조
            PlaceLabel(wildcardLabel);
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

        #endregion
    }

    #endregion
}