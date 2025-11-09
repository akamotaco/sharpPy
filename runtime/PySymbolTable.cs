using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
// Performance: Eliminated LINQ - no LINQ usage found

namespace SharpPy
{
    /// <summary>
    /// CPython 3.12 compatible symbol table enums and basic classes
    /// Simplified version focusing on scope resolution
    /// </summary>
    public enum SymbolScope
    {
        Unknown,
        Local,
        Global,
        Free,
        Cell
    }

    public enum SymbolFlags
    {
        None = 0,
        Assigned = 1,
        Used = 2,
        Parameter = 4,
        Global = 8,
        Nonlocal = 16,
        Imported = 32
    }

    public enum SymbolTableType
    {
        Module,
        Function,
        Class
    }

    /// <summary>
    /// CPython 3.12 compatible Symbol class
    /// </summary>
    public class Symbol
    {
        public string Name { get; }
        public SymbolScope Scope { get; set; }
        public SymbolFlags Flags { get; set; }

        public Symbol(string name)
        {
            Name = name;
            Scope = SymbolScope.Unknown;
            Flags = SymbolFlags.None;
        }

        public bool IsAssigned() => (Flags & SymbolFlags.Assigned) != 0;
        public bool IsGlobal() => (Flags & SymbolFlags.Global) != 0;
        public bool IsLocal() => Scope == SymbolScope.Local;
        public bool IsFree() => Scope == SymbolScope.Free;
        public bool IsCell() => Scope == SymbolScope.Cell;
        public bool IsParameter() => (Flags & SymbolFlags.Parameter) != 0;
        public bool IsNonlocal() => (Flags & SymbolFlags.Nonlocal) != 0;
    }

    /// <summary>
    /// CPython 3.12 compatible SymbolTable class
    /// </summary>
    public class SymbolTable
    {
        private readonly string _name;
        private readonly SymbolTableType _type;
        private readonly Dictionary<string, Symbol> _symbols;
        private readonly List<SymbolTable> _children;
        private SymbolTable? _parent;

        // CPython 3.12: symtable.h - ste_generator, ste_coroutine, ste_comprehension flags
        // Set during symbol table analysis when yield/yield from/await are encountered
        public bool IsGenerator { get; set; }
        public bool IsCoroutine { get; set; }
        public bool IsComprehension { get; set; }  // CPython 3.12: ste_comprehension flag

        public SymbolTable(string name, SymbolTableType type, SymbolTable? parent = null)
        {
            _name = name;
            _type = type;
            _parent = parent;
            _symbols = new Dictionary<string, Symbol>();
            _children = new List<SymbolTable>();
            IsGenerator = false;
            IsCoroutine = false;
            IsComprehension = false;
        }

        public string GetName() => _name;
        public string Name => _name; // Add property for compiler compatibility
        public new SymbolTableType GetType() => _type;  // Hide Object.GetType()
        public SymbolTableType Type => _type; // Add property for compiler compatibility
        public IEnumerable<string> GetIdentifiers() => _symbols.Keys;
        public IEnumerable<SymbolTable> GetChildren() => _children;
        public IEnumerable<SymbolTable> Children => _children; // Add property for compiler compatibility

        public void AddChild(SymbolTable child)
        {
            _children.Add(child);
            child._parent = this;
        }

        public Symbol? Lookup(string name)
        {
            return _symbols.TryGetValue(name, out var symbol) ? symbol : null;
        }

        public bool HasSymbol(string name)
        {
            return _symbols.ContainsKey(name);
        }

        public void DefineSymbol(string name, SymbolFlags flags = SymbolFlags.None)
        {
            if (!_symbols.TryGetValue(name, out var symbol))
            {
                symbol = new Symbol(name);
                _symbols[name] = symbol;
            }
            symbol.Flags |= flags;

            // CPython 3.12: symtable.c:569 - analyze_name() sets scope to LOCAL for DEF_BOUND
            // Note: In CPython, LOCAL means "locally bound" not "function local"
            // The distinction between module-level and function-level is made during code generation
            if ((flags & SymbolFlags.Assigned) != 0 && symbol.Scope != SymbolScope.Cell && !symbol.IsGlobal())
            {
                symbol.Scope = SymbolScope.Local;
            }
        }

        public SymbolTable? GetParent() => _parent;
        public Dictionary<string, Symbol> GetSymbols() => _symbols;

        /// <summary>
        /// CPython 3.12: Find all free variables in this scope
        /// </summary>
        public List<string> FindFreeVariables()
        {
            var freeVars = new List<string>();
#if DEBUG_COMPILER_LOG
            Console.WriteLine($"    🔍 FindFreeVariables in {_name}: checking {_symbols.Count} symbols");
#endif
            foreach (var symbol in _symbols.Values)
            {
#if DEBUG_COMPILER_LOG
                Console.WriteLine($"      Symbol {symbol.Name}: Scope={symbol.Scope}, IsFree={symbol.IsFree()}");
#endif
                if (symbol.IsFree())
                {
                    freeVars.Add(symbol.Name);
#if DEBUG_COMPILER_LOG
                    Console.WriteLine($"        → Added to freeVars: {symbol.Name}");
#endif
                }
            }

            // CPython 3.12: Free variables must be in ALPHABETICAL order!
            // This is critical for LOAD_CLOSURE indices and closure tuple creation
            freeVars.Sort(StringComparer.Ordinal);

#if DEBUG_COMPILER_LOG
            Console.WriteLine($"    → FindFreeVariables result (sorted): [{string.Join(", ", freeVars)}]");
#endif
            return freeVars;
        }

        /// <summary>
        /// CPython 3.12: Find all cell variables in this scope
        /// </summary>
        public List<string> FindCellVariables()
        {
            var cellVars = new List<string>();
#if DEBUG_COMPILER_LOG
            Console.WriteLine($"    🔍 FindCellVariables in {_name}: checking {_symbols.Count} symbols");
#endif
            foreach (var symbol in _symbols.Values)
            {
#if DEBUG_COMPILER_LOG
                Console.WriteLine($"      Symbol {symbol.Name}: Scope={symbol.Scope}, IsCell={symbol.IsCell()}");
#endif
                if (symbol.IsCell())
                {
                    cellVars.Add(symbol.Name);
#if DEBUG_COMPILER_LOG
                    Console.WriteLine($"        → Added to cellVars: {symbol.Name}");
#endif
                }
            }

            // CPython 3.12: Cell variables must be in ALPHABETICAL order!
            // This is critical for LOAD_CLOSURE indices and closure tuple creation
            cellVars.Sort(StringComparer.Ordinal);

#if DEBUG_COMPILER_LOG
            Console.WriteLine($"    → FindCellVariables result (sorted): [{string.Join(", ", cellVars)}]");
#endif
            return cellVars;
        }

    }

    /// <summary>
    /// Simple symbol table builder focused on basic scope analysis
    /// </summary>
    public class SymbolTableBuilder
    {
        // 캐시된 builtin 변수 이름들 (성능 최적화 및 자동 동기화)
        private static HashSet<string> _builtinNames;

        // 정적 생성자: builtin 변수 이름들을 캐시
        static SymbolTableBuilder()
        {
            _builtinNames = new HashSet<string>(PyBuiltinsModule.Instance.BuiltinDict.Keys);
        }

        private SymbolTable? _rootTable;
        private SymbolTable? _currentTable;
        private int _lambdaCounter = 0;

        // CPython 3.12: st->st_blocks - AST node → SymbolTable mapping
        // Corresponds to PyDict_SetItem(st->st_blocks, ste->ste_id, ste) in symtable.c:142
        // Uses ConditionalWeakTable for O(1) lookup without boxing, based on reference equality
        // (equivalent to CPython's pointer-based identity comparison)
        private ConditionalWeakTable<Expression, SymbolTable> _astNodeToSymbolTable
            = new ConditionalWeakTable<Expression, SymbolTable>();

        // CPython 3.12: st->st_stack - scope stack for nested scope tracking
        // Used for symtable_extend_namedexpr_scope (symtable.c:1906)
        private Stack<SymbolTable> _scopeStack = new Stack<SymbolTable>();

        public SymbolTable BuildSymbolTable(List<Statement> statements, string name = "<module>")
        {
#if DEBUG_COMPILER_LOG
            Console.WriteLine($"🔧 Building symbol table for: {name}");
#endif

            // Initialize/reset state to prevent issues from previous builds
            _processedTables.Clear();
            _propagationInProgress.Clear();
            _cellProcessingInProgress.Clear();
            _recursionDepth = 0;
            _astNodeToSymbolTable.Clear();  // CPython 3.12: Clear st_blocks
            _scopeStack.Clear();  // CPython 3.12: Clear scope stack

            _rootTable = new SymbolTable(name, SymbolTableType.Module);
            _currentTable = _rootTable;

            // CPython 3.12: Push module onto scope stack
            _scopeStack.Push(_rootTable);

            // Basic analysis - just track function and class definitions for now
            foreach (var statement in statements)
            {
                AnalyzeStatement(statement);
            }

            // Resolve free variables across all scopes after initial analysis
            ResolveFreeVariablesRecursive(_rootTable);

#if DEBUG_COMPILER_LOG
            Console.WriteLine($"✅ Symbol table built: {_rootTable.GetIdentifiers().Count()} symbols");
#endif
            foreach (var symbol in _rootTable.GetIdentifiers())
            {
                var sym = _rootTable.Lookup(symbol);
#if DEBUG_COMPILER_LOG
                Console.WriteLine($"  {symbol}: {sym?.Scope} scope, flags: {sym?.Flags}");
#endif
            }

            return _rootTable;
        }

        private void AnalyzeStatement(Statement statement)
        {
#if DEBUG_COMPILER_LOG
            Console.WriteLine($"  AnalyzeStatement: Type={statement.GetType().Name} in scope '{_currentTable?.GetName()}'");
#endif
            switch (statement)
            {
                case FunctionDefStatement func:
                    AnalyzeFunction(func);
                    break;

                case AsyncFunctionDefStatement asyncFunc:
                    AnalyzeAsyncFunction(asyncFunc);
                    break;

                case ClassDefStatement cls:
                    AnalyzeClass(cls);
                    break;

                case AssignStatement assign:
                    AnalyzeAssignment(assign);
                    break;

                case ChainedAssignStatement chainedAssign:
                    AnalyzeChainedAssignment(chainedAssign);
                    break;

                case AssignTargetStatement assignTarget:
                    AnalyzeAssignTarget(assignTarget);
                    break;

                case AugmentedAssignStatement augAssign:
                    AnalyzeAugmentedAssignment(augAssign);
                    break;

                case AugAssignStatement augAssign:
                    AnalyzeAugAssignment(augAssign);
                    break;

                case AnnAssignStatement annAssign:
                    AnalyzeAnnAssignment(annAssign);
                    break;

                case GlobalStatement global:
                    foreach (var name in global.Names)
                    {
                        _currentTable?.DefineSymbol(name, SymbolFlags.Global);
                    }
                    break;

                case NonlocalStatement nonlocal:
                    foreach (var name in nonlocal.Names)
                    {
                        _currentTable?.DefineSymbol(name, SymbolFlags.Nonlocal);
                    }
                    break;

                case ReturnStatement returnStmt:
#if DEBUG_COMPILER_LOG
                    Console.WriteLine($"    ReturnStatement: Value is {(returnStmt.Value != null ? returnStmt.Value.GetType().Name : "null")}");
#endif
                    if (returnStmt.Value != null)
                    {
                        AnalyzeExpression(returnStmt.Value);
                    }
                    break;

                case YieldStatement yieldStmt:
                    // CPython 3.12: Mark scope as generator
                    if (_currentTable != null)
                    {
                        _currentTable.IsGenerator = true;
                    }
                    if (yieldStmt.Value != null)
                    {
                        AnalyzeExpression(yieldStmt.Value);
                    }
                    break;

                case YieldFromStatement yieldFromStmt:
                    // CPython 3.12: Mark scope as generator
                    if (_currentTable != null)
                    {
                        _currentTable.IsGenerator = true;
                    }
                    AnalyzeExpression(yieldFromStmt.Value);
                    break;

                case ExpressionStatement exprStmt:
                    AnalyzeExpression(exprStmt.Expression);
                    break;

                case TryStatement tryStmt:
#if DEBUG_COMPILER_LOG
                    Console.WriteLine($"  AnalyzeStatement: TryStatement in scope '{_currentTable?.GetName()}'");
#endif
                    // Analyze try block
                    foreach (var stmt in tryStmt.Body)
                    {
                        AnalyzeStatement(stmt);
                    }
                    // Analyze except handlers
                    foreach (var handler in tryStmt.Handlers)
                    {
                        if (handler.Name != null)
                        {
                            _currentTable?.DefineSymbol(handler.Name, SymbolFlags.Assigned);
                        }
                        foreach (var stmt in handler.Body)
                        {
                            AnalyzeStatement(stmt);
                        }
                    }
                    // Analyze else clause
                    if (tryStmt.OrElse != null)
                    {
                        foreach (var stmt in tryStmt.OrElse)
                        {
                            AnalyzeStatement(stmt);
                        }
                    }
                    // Analyze finally clause
                    if (tryStmt.FinalBody != null)
                    {
                        foreach (var stmt in tryStmt.FinalBody)
                        {
                            AnalyzeStatement(stmt);
                        }
                    }
                    break;

                case ForStatement forStmt:
#if DEBUG_COMPILER_LOG
                    Console.WriteLine($"  AnalyzeStatement: ForStatement in scope '{_currentTable?.GetName()}'");
#endif
                    // 1. Analyze iterable expression
                    AnalyzeExpression(forStmt.Iter);

                    // 2. Define loop variable(s) as assigned (CPython 3.12: target can be tuple)
                    DefineTargetSymbols(forStmt.Target, SymbolFlags.Assigned);

                    // 3. Analyze loop body
                    foreach (var stmt in forStmt.Body)
                    {
                        AnalyzeStatement(stmt);
                    }

                    // 4. Analyze else clause if present
                    if (forStmt.ElseClause != null)
                    {
                        foreach (var stmt in forStmt.ElseClause)
                        {
                            AnalyzeStatement(stmt);
                        }
                    }
                    break;

                case IfStatement ifStmt:
#if DEBUG_COMPILER_LOG
                    Console.WriteLine($"  AnalyzeStatement: IfStatement in scope '{_currentTable?.GetName()}'");
#endif
                    // Analyze condition
                    AnalyzeExpression(ifStmt.Test);
                    // Analyze body
                    foreach (var stmt in ifStmt.Body)
                    {
                        AnalyzeStatement(stmt);
                    }
                    // Analyze else clause
                    if (ifStmt.OrElse != null)
                    {
                        foreach (var stmt in ifStmt.OrElse)
                        {
                            AnalyzeStatement(stmt);
                        }
                    }
                    break;

                case WhileStatement whileStmt:
                    // CPython 3.12: symtable.c:1718-1723 - VISIT while test, body, and orelse
#if DEBUG_COMPILER_LOG
                    Console.WriteLine($"  AnalyzeStatement: WhileStatement in scope '{_currentTable?.GetName()}'");
#endif
                    // 1. Analyze test condition (CPython: VISIT(st, expr, s->v.While.test))
                    AnalyzeExpression(whileStmt.Test);

                    // 2. Analyze loop body (CPython: VISIT_SEQ(st, stmt, s->v.While.body))
                    foreach (var stmt in whileStmt.Body)
                    {
                        AnalyzeStatement(stmt);
                    }

                    // 3. Analyze else clause if present (CPython: VISIT_SEQ(st, stmt, s->v.While.orelse))
                    if (whileStmt.ElseClause != null)
                    {
                        foreach (var stmt in whileStmt.ElseClause)
                        {
                            AnalyzeStatement(stmt);
                        }
                    }
                    break;

                case ImportStatement importStmt:
                    // Register imported modules as global variables
                    foreach (var moduleName in importStmt.Names)
                    {
                        _currentTable?.DefineSymbol(moduleName, SymbolFlags.Assigned);
                        #if DEBUG_COMPILER_LOG
                        Console.WriteLine($"  ImportStatement: Registered '{moduleName}' as global symbol");
                        #endif
                    }
                    break;

                case ImportFromStatement importFromStmt:
                    // Register imported names as global variables
                    foreach (var importAlias in importFromStmt.Names)
                    {
                        // Use alias name if available, otherwise use the actual name
                        string symbolName = importAlias.AsName ?? importAlias.Name;
                        _currentTable?.DefineSymbol(symbolName, SymbolFlags.Assigned);
                        #if DEBUG_COMPILER_LOG
                        Console.WriteLine($"  ImportFromStatement: Registered '{symbolName}' as global symbol");
                        #endif
                    }
                    break;

                case MatchStatement matchStmt:
#if DEBUG_COMPILER_LOG
                    Console.WriteLine($"  AnalyzeStatement: MatchStatement in scope '{_currentTable?.GetName()}'");
#endif
                    // 1. Analyze the subject expression
                    AnalyzeExpression(matchStmt.Subject);

                    // 2. Analyze each match case
                    foreach (var matchCase in matchStmt.Cases)
                    {
                        // CPython 3.12: Analyze pattern to define variables as LOCAL
                        // Use AnalyzePattern instead of AnalyzeExpression
                        AnalyzePattern(matchCase.Pattern);

                        // Analyze guard if present
                        if (matchCase.Guard != null)
                        {
                            AnalyzeExpression(matchCase.Guard);
                        }

                        // Analyze case body
                        foreach (var stmt in matchCase.Body)
                        {
                            AnalyzeStatement(stmt);
                        }
                    }
                    break;

                case WithStatement withStmt:
#if DEBUG_COMPILER_LOG
                    Console.WriteLine($"  AnalyzeStatement: WithStatement in scope '{_currentTable?.GetName()}'");
#endif
                    // 1. Analyze context expressions
                    foreach (var item in withStmt.Items)
                    {
                        AnalyzeExpression(item.ContextExpr);

                        // 2. Define optional variable if present (as target)
                        if (item.OptionalVars != null)
                        {
                            if (item.OptionalVars is NameExpression nameExpr)
                            {
                                _currentTable?.DefineSymbol(nameExpr.Name, SymbolFlags.Assigned);
                            }
                            else
                            {
                                // Handle complex assignment targets (tuples, lists, etc.)
                                AnalyzeExpression(item.OptionalVars);
                            }
                        }
                    }

                    // 3. Analyze body
                    foreach (var stmt in withStmt.Body)
                    {
                        AnalyzeStatement(stmt);
                    }
                    break;

                // For now, skip complex statement types
                default:
#if DEBUG_COMPILER_LOG
                    Console.WriteLine($"  AnalyzeStatement: Unhandled statement type {statement.GetType().Name} in scope '{_currentTable?.GetName()}'");
#endif
                    break;
            }
        }

        private void AnalyzeFunction(FunctionDefStatement func)
        {
            // CRITICAL: Analyze decorators FIRST in current scope before defining function
            // This ensures decorator variables are marked as Used in current scope
            // and can become cell variables if referenced from nested scopes
            foreach (var decorator in func.Decorators)
            {
                AnalyzeExpression(decorator.DecoratorFunction);
                foreach (var arg in decorator.Arguments)
                {
                    AnalyzeExpression(arg);
                }
            }

            // Define function name in current scope
            _currentTable?.DefineSymbol(func.Name, SymbolFlags.Assigned);

            // Create new scope for function body
            var functionTable = new SymbolTable($"<function:{func.Name}>", SymbolTableType.Function);
            _currentTable?.AddChild(functionTable);

            var savedTable = _currentTable;
            _currentTable = functionTable;

            // CPython 3.12: Push onto scope stack (symtable.c:331)
            _scopeStack.Push(_currentTable);

            // Add parameters to function scope - extract parameter names only
            foreach (var param in func.Parameters)
            {
                // Extract parameter name from "param=default" format
                var paramName = param.Split('=')[0].Trim();
                // Handle *args and **kwargs
                if (paramName.StartsWith("**"))
                    paramName = paramName.Substring(2);
                else if (paramName.StartsWith("*"))
                    paramName = paramName.Substring(1);

                _currentTable.DefineSymbol(paramName, SymbolFlags.Parameter | SymbolFlags.Assigned);
            }

            // Analyze function body
            foreach (var stmt in func.Body)
            {
                AnalyzeStatement(stmt);
            }

            // CPython 3.12: If function or nested functions contain super() calls, add __class__ as free variable
            // This is critical for proper closure propagation in cases like:
            //   def outer(self): def inner(): return super().__repr__()
            if (PythonCompiler.ContainsSuperCalls(func.Body))
            {
                if (!_currentTable.HasSymbol("__class__"))
                {
                    // Add __class__ as a used (free) variable
                    _currentTable.DefineSymbol("__class__", SymbolFlags.Used);
#if DEBUG_COMPILER_LOG
                    Console.WriteLine($"  ✅ Added __class__ to {_currentTable.GetName()} due to super() in function or nested functions");
#endif
                }
            }

            // After analyzing body, resolve free variables
            ResolveFreeVariables(_currentTable);

            // CPython 3.12: Pop from scope stack (symtable.c:353)
            _scopeStack.Pop();

            _currentTable = savedTable;
        }

        /// <summary>
        /// CPython 3.12: Resolve free variables by checking parent scopes
        /// </summary>
        private void ResolveFreeVariables(SymbolTable table)
        {
#if DEBUG_COMPILER_LOG
            Console.WriteLine($"  🔍 ResolveFreeVariables: Processing {table.GetSymbols().Count} symbols in {table.GetName()}");
#endif

            foreach (var symbol in table.GetSymbols().Values)
            {
#if DEBUG_COMPILER_LOG
                Console.WriteLine($"    Symbol: {symbol.Name}, Scope: {symbol.Scope}, Flags: {symbol.Flags}");
#endif

                // Special handling for nonlocal variables
                if (symbol.IsNonlocal())
                {
#if DEBUG_COMPILER_LOG
                    Console.WriteLine($"      ↳ Processing NONLOCAL variable: {symbol.Name}");
#endif
                    // nonlocal variables must be found in enclosing scope
                    var parentSymbol = FindInEnclosingScope(table, symbol.Name);
                    if (parentSymbol != null)
                    {
                        // Mark as free variable (needs closure)
                        symbol.Scope = SymbolScope.Free;

                        // Mark the parent symbol as cell variable (needs to be captured)
                        parentSymbol.Scope = SymbolScope.Cell;

#if DEBUG_COMPILER_LOG
                        Console.WriteLine($"      ↳ NONLOCAL marked as FREE (found in parent: {parentSymbol.Name})");
#endif
                    }
                    else
                    {
                        // nonlocal variable not found in parent scope - this is an error
#if DEBUG_COMPILER_LOG
                        Console.WriteLine($"      ⚠️ NONLOCAL variable '{symbol.Name}' not found in enclosing scope");
#endif
                        symbol.Scope = SymbolScope.Global; // fallback
                    }
                    continue;
                }

                // Skip if already resolved or is parameter/assigned locally
                if (symbol.Scope != SymbolScope.Unknown)
                {
#if DEBUG_COMPILER_LOG
                    Console.WriteLine($"      ↳ Skipped (already resolved or local)");
#endif
                    continue;
                }

                // This is a name that was referenced but not defined locally
                // Check if it's declared as global first
                if (symbol.IsGlobal())
                {
                    // Variable explicitly declared as global - don't make it a free variable
                    symbol.Scope = SymbolScope.Global;
#if DEBUG_COMPILER_LOG
                    Console.WriteLine($"      ↳ Marked as GLOBAL (explicitly declared with 'global' statement)");
#endif
                }
                else
                {
                    // Look for it in enclosing scopes
                    var foundInParent = FindInEnclosingScope(table, symbol.Name);
                    if (foundInParent != null)
                    {
                        // Mark as free variable (needs closure)
                        symbol.Scope = SymbolScope.Free;

                        // Mark the parent symbol as cell variable if it's assigned OR a parameter
                        // CPython 3.12: Parameters that are used in nested scopes need to become Cell variables
                        if (foundInParent.IsAssigned() || foundInParent.IsParameter())
                        {
                            foundInParent.Scope = SymbolScope.Cell;
                        }

#if DEBUG_COMPILER_LOG
                        Console.WriteLine($"      ↳ Marked as FREE (found in parent: {foundInParent.Name})");
#endif
                    }
                    else
                    {
                        // Not found in any parent scope, assume global
                        symbol.Scope = SymbolScope.Global;
#if DEBUG_COMPILER_LOG
                        Console.WriteLine($"      ↳ Marked as GLOBAL (not found in parents)");
#endif
                    }
                }
            }
        }

        /// <summary>
        /// CPython 3.12: Find symbol in enclosing scopes (excluding module scope)
        /// Module-level variables should always be accessed as GLOBAL, not FREE
        /// Built-in variables like 'print' should be accessed as GLOBAL, not FREE
        /// </summary>
        private Symbol? FindInEnclosingScope(SymbolTable currentTable, string name)
        {
            // Check for built-in variables first - they should never be cell variables
            if (IsBuiltinName(name))
            {
#if DEBUG_COMPILER_LOG
                Console.WriteLine($"      ↳ {name} is a builtin - should be GLOBAL, not FREE/CELL");
#endif
                return null; // Built-ins should be accessed as GLOBAL
            }

            var parent = currentTable.GetParent();
            while (parent != null)
            {
                // CPython 3.12: Module-level variables are GLOBAL when accessed from function scope
                if (parent.Type == SymbolTableType.Module && currentTable.Type == SymbolTableType.Function)
                {
                    // Function scope accessing module variables → should be GLOBAL
                    break;  // Stop searching, will be marked as GLOBAL
                }

                if (parent.GetSymbols().TryGetValue(name, out var symbol))
                {
#if DEBUG_COMPILER_LOG
                    Console.WriteLine($"      ↳ Found {name} in enclosing scope {parent.GetName()}");
#endif
                    // CPython 3.12: If the symbol is declared as global in the parent scope,
                    // it should not be treated as a free variable in the current scope
                    if (symbol.IsGlobal())
                    {
#if DEBUG_COMPILER_LOG
                        Console.WriteLine($"      ↳ Symbol {name} is marked as GLOBAL in parent scope - skipping");
#endif
                        parent = parent.GetParent();
                        continue;
                    }
                    return symbol;
                }
                parent = parent.GetParent();
            }
            return null;
        }

        /// <summary>
        /// CPython 3.12: symtable.c:1906-1997 - symtable_extend_namedexpr_scope
        /// Find the correct enclosing scope for a walrus variable in a comprehension.
        /// Iterates through the scope stack in reverse order, skips comprehension scopes,
        /// and adds the variable to the first Function or Module scope found.
        /// </summary>
        private void ExtendNamedExprScope(string varName)
        {
#if DEBUG_COMPILER_LOG
            Console.WriteLine($"      ExtendNamedExprScope: Finding scope for walrus variable '{varName}'");
            Console.WriteLine($"      Current scope: {_currentTable?.GetName()}, IsComprehension: {_currentTable?.IsComprehension}");
            Console.WriteLine($"      Scope stack size: {_scopeStack.Count}");
#endif

            // CPython 3.12: Iterate over the stack from top (current) to bottom (module)
            // ToArray() returns: [Comprehension (top/current), Function, Module (bottom)]
            // We iterate from index 0 (current) upwards to find the enclosing scope
            var scopeArray = _scopeStack.ToArray();

#if DEBUG_COMPILER_LOG
            Console.WriteLine($"      Scope stack contents (top to bottom):");
            for (int j = 0; j < scopeArray.Length; j++)
            {
                Console.WriteLine($"        [{j}]: {scopeArray[j].GetName()}, Type: {scopeArray[j].Type}, IsComprehension: {scopeArray[j].IsComprehension}");
            }
#endif

            // Iterate from current scope (index 0) upwards through parent scopes
            for (int i = 0; i < scopeArray.Length; i++)
            {
                var ste = scopeArray[i];

#if DEBUG_COMPILER_LOG
                Console.WriteLine($"      Checking scope[{i}]: {ste.GetName()}, Type: {ste.Type}, IsComprehension: {ste.IsComprehension}");
#endif

                // CPython 3.12: If we find a comprehension scope, skip it
                if (ste.IsComprehension)
                {
#if DEBUG_COMPILER_LOG
                    Console.WriteLine($"      ↳ Skipping comprehension scope");
#endif
                    continue;
                }

                // CPython 3.12: If we find a FunctionBlock entry, add as LOCAL (ASSIGNED)
                if (ste.Type == SymbolTableType.Function)
                {
#if DEBUG_COMPILER_LOG
                    Console.WriteLine($"      ↳ Adding '{varName}' to function scope '{ste.GetName()}' as ASSIGNED (will use STORE_FAST)");
#endif
                    ste.DefineSymbol(varName, SymbolFlags.Assigned);
                    return;
                }

                // CPython 3.12: If we find a ModuleBlock entry, add as GLOBAL (ASSIGNED)
                if (ste.Type == SymbolTableType.Module)
                {
#if DEBUG_COMPILER_LOG
                    Console.WriteLine($"      ↳ Adding '{varName}' to module scope '{ste.GetName()}' as ASSIGNED (will use STORE_GLOBAL)");
#endif
                    ste.DefineSymbol(varName, SymbolFlags.Assigned);
                    return;
                }

                // CPython 3.12: Class scopes are also skipped for walrus variables
                if (ste.Type == SymbolTableType.Class)
                {
#if DEBUG_COMPILER_LOG
                    Console.WriteLine($"      ↳ Skipping class scope");
#endif
                    continue;
                }
            }

#if DEBUG_COMPILER_LOG
            Console.WriteLine($"      ⚠️ WARNING: No suitable scope found for walrus variable '{varName}'");
#endif
        }

        /// <summary>
        /// Check if a name is a Python built-in that should be accessed as GLOBAL
        /// </summary>
        private bool IsBuiltinName(string name)
        {
            // 동적으로 PyBuiltinsModule에서 builtin 여부 확인 (자동 동기화)
            return _builtinNames.Contains(name);
        }

        // Track processed tables to prevent infinite recursion
        private readonly HashSet<SymbolTable> _processedTables = new HashSet<SymbolTable>();
        private int _recursionDepth = 0;
        private const int MAX_RECURSION_DEPTH = 20;

        /// <summary>
        /// CPython 3.12: Recursively resolve free variables in all scopes
        /// First pass: analyze which variables are referenced in nested scopes
        /// Second pass: resolve variable scopes based on usage patterns
        /// </summary>
        private void ResolveFreeVariablesRecursive(SymbolTable table)
        {
            // Prevent infinite recursion
            _recursionDepth++;
            if (_recursionDepth > MAX_RECURSION_DEPTH)
            {
                Console.WriteLine($"❌ ERROR: Maximum recursion depth exceeded in ResolveFreeVariablesRecursive for {table.GetName()}");
                _recursionDepth--;
                return;
            }

            // Prevent processing the same table multiple times
            if (_processedTables.Contains(table))
            {
#if DEBUG_COMPILER_LOG
                Console.WriteLine($"⚠️  Skipping already processed table: {table.GetName()}");
#endif
                _recursionDepth--;
                return;
            }

            _processedTables.Add(table);

#if DEBUG_COMPILER_LOG
            Console.WriteLine($"🔍 Resolving free variables in scope: {table.GetName()} (depth: {_recursionDepth})");
#endif

            // First recursively resolve child scopes (depth-first)
            foreach (var child in table.GetChildren())
            {
                ResolveFreeVariablesRecursive(child);

                // After child is resolved, propagate its free variables to current scope
                PropagateChildFreeVariables(table, child);
            }

            // Then resolve this scope
            ResolveFreeVariables(table);

            // CPython 3.12: Post-process to ensure variables used in nested functions become cells
            MarkCellVariablesBasedOnChildUsage(table);

            _recursionDepth--;
        }

        // Track cell variable processing to prevent multiple markings
        private readonly HashSet<string> _cellProcessingInProgress = new HashSet<string>();

        /// <summary>
        /// CPython 3.12: Mark variables as cell variables if they're used in child scopes
        /// This is crucial for proper closure handling
        /// </summary>
        private void MarkCellVariablesBasedOnChildUsage(SymbolTable table)
        {
            // Prevent reprocessing the same table
            string tableKey = $"cell_process_{table.GetName()}";
            if (_cellProcessingInProgress.Contains(tableKey))
            {
#if DEBUG_COMPILER_LOG
                Console.WriteLine($"⚠️  Skipping already processed cell variables for: {table.GetName()}");
#endif
                return;
            }

            _cellProcessingInProgress.Add(tableKey);

            try
            {
#if DEBUG_COMPILER_LOG
                Console.WriteLine($"🔄 Post-processing cell variables for scope: {table.GetName()}");
                Console.WriteLine($"    Current symbols in {table.GetName()}:");
                foreach (var kvp in table.GetSymbols())
                {
                    Console.WriteLine($"      {kvp.Key}: Scope={kvp.Value.Scope}, Flags={kvp.Value.Flags}");
                }
#endif

                foreach (var child in table.GetChildren())
                {
                    // Skip class scopes for now - they have different rules
                    if (child.Type == SymbolTableType.Class) continue;

#if DEBUG_COMPILER_LOG
                    Console.WriteLine($"    Checking child scope: {child.GetName()}");
#endif
                    foreach (var childSymbol in child.GetSymbols().Values)
                    {
#if DEBUG_COMPILER_LOG
                        Console.WriteLine($"      Symbol {childSymbol.Name}: Scope={childSymbol.Scope}, IsFree={childSymbol.IsFree()}");
#endif
                        // If child has a free or cell variable, parent should have it as cell variable
                        // BUT ONLY if the parent actually defines/owns this variable
                        // CPython 3.12: Cell variables in child scopes also need parent cells
                        if (childSymbol.IsFree() || childSymbol.Scope == SymbolScope.Cell)
                        {
                            var parentSymbol = table.Lookup(childSymbol.Name);
#if DEBUG_COMPILER_LOG
                            Console.WriteLine($"        → Looking for {childSymbol.Name} in parent {table.GetName()}: found={parentSymbol != null}");
                            if (parentSymbol != null)
                            {
                                Console.WriteLine($"          Parent scope: {parentSymbol.Scope}, Flags: {parentSymbol.Flags}");
                            }
#endif
                            if (parentSymbol != null &&
                                (parentSymbol.Scope == SymbolScope.Local || parentSymbol.Scope == SymbolScope.Cell) &&
                                (parentSymbol.IsAssigned() || parentSymbol.IsParameter()))
                            {
#if DEBUG_COMPILER_LOG
                                Console.WriteLine($"    → ✅ Marking {childSymbol.Name} as CELL in {table.GetName()} (used as FREE in {child.GetName()})");
#endif
                            parentSymbol.Scope = SymbolScope.Cell;
                        }
                        else if (parentSymbol != null &&
                                 (parentSymbol.Scope == SymbolScope.Free ||
                                  parentSymbol.Scope == SymbolScope.Unknown))
                        {
                            // Parent also treats this as Free/Unknown variable - don't change to Cell
#if DEBUG_COMPILER_LOG
                            Console.WriteLine($"    → ❌ Skipping {childSymbol.Name} - parent scope is {parentSymbol.Scope}, not owner");
#endif
                        }
                        else if (parentSymbol != null)
                        {
#if DEBUG_COMPILER_LOG
                            Console.WriteLine($"    → ⚠️ Checking {childSymbol.Name} - parent scope: {parentSymbol.Scope}, assigned: {parentSymbol.IsAssigned()}");
#endif
                        }
                        else
                        {
#if DEBUG_COMPILER_LOG
                            Console.WriteLine($"    → ❓ {childSymbol.Name} not found in parent {table.GetName()}");
#endif
                        }
                    }
                }
            }

#if DEBUG_COMPILER_LOG
                Console.WriteLine($"    Final symbols in {table.GetName()} after processing:");
                foreach (var kvp in table.GetSymbols())
                {
                    Console.WriteLine($"      {kvp.Key}: Scope={kvp.Value.Scope}, Flags={kvp.Value.Flags}");
                }
#endif
            }
            finally
            {
                _cellProcessingInProgress.Remove(tableKey);
            }
        }

        // Track propagation operations to prevent circular propagation
        private readonly HashSet<string> _propagationInProgress = new HashSet<string>();

        /// <summary>
        /// CPython 3.12: Propagate free variables from child scope to parent scope
        /// If child needs a variable that parent doesn't have, parent should also need it as free
        /// </summary>
        private void PropagateChildFreeVariables(SymbolTable parent, SymbolTable child)
        {
            // Prevent circular propagation
            string propagationKey = $"{child.GetName()}→{parent.GetName()}";
            if (_propagationInProgress.Contains(propagationKey))
            {
#if DEBUG_COMPILER_LOG
                Console.WriteLine($"⚠️  Skipping circular propagation: {propagationKey}");
#endif
                return;
            }

            _propagationInProgress.Add(propagationKey);

            try
            {
                var childFreeVars = child.FindFreeVariables();
#if DEBUG_COMPILER_LOG
                Console.WriteLine($"  🔄 Propagating free vars from {child.GetName()} to {parent.GetName()}: [{string.Join(", ", childFreeVars)}]");
#endif

                foreach (var freeVar in childFreeVars)
                {
                    // Check if parent has this variable
                    var parentSymbol = parent.Lookup(freeVar);
                    if (parentSymbol == null)
                    {
                        // Parent doesn't have this variable, so parent also needs it as free variable
                        parent.DefineSymbol(freeVar, SymbolFlags.None);
                        // Explicitly set scope to Free for proper identification
                        var addedSymbol = parent.Lookup(freeVar);
                        if (addedSymbol != null)
                        {
                            addedSymbol.Scope = SymbolScope.Free;
                        }
#if DEBUG_COMPILER_LOG
                        Console.WriteLine($"    → Added {freeVar} as free variable to {parent.GetName()}");
#endif
                    }
                    else
                    {
#if DEBUG_COMPILER_LOG
                        Console.WriteLine($"    → {freeVar} already available in {parent.GetName()} (scope: {parentSymbol.Scope})");
#endif
                    }
                }
            }
            finally
            {
                _propagationInProgress.Remove(propagationKey);
            }
        }

        /// <summary>
        /// CPython 3.12: Analyze pattern matching patterns to define LOCAL variables
        /// Based on CPython's symtable_visit_pattern() in Python/symtable.c
        /// </summary>
        private void AnalyzePattern(Expression pattern)
        {
#if DEBUG_COMPILER_LOG
            Console.WriteLine($"      AnalyzePattern: {pattern.GetType().Name} in scope '{_currentTable?.GetName()}'");
#endif
            switch (pattern)
            {
                // MatchValue: constant - nothing to do
                case ConstantExpression:
                    break;

                // MatchValue: attribute reference - analyze the value expression
                case AttributeExpression attr:
                    AnalyzeExpression(attr);
                    break;

                // MatchSingleton: True/False/None - nothing to do
                // (represented as ConstantExpression, already handled above)

                // MatchSequence: [patterns...] - recursively analyze nested patterns
                case ListExpression list:
                    foreach (var elem in list.Elements)
                    {
                        AnalyzePattern(elem);
                    }
                    break;

                case TupleExpression tuple:
                    foreach (var elem in tuple.Elements)
                    {
                        AnalyzePattern(elem);
                    }
                    break;

                // MatchStar: *name - define the star variable as LOCAL
                // After PyParserRuntime_Bridge fix, this is StarExpression wrapping a NameExpression
                case StarExpression star:
                    if (star.Value is NameExpression starName && starName.Name != "_")
                    {
                        // CPython: symtable_add_def(st, name, DEF_LOCAL)
                        _currentTable?.DefineSymbol(starName.Name, SymbolFlags.Assigned);
#if DEBUG_COMPILER_LOG
                        Console.WriteLine($"        AnalyzePattern: Defined star variable '{starName.Name}' as LOCAL");
#endif
                    }
                    break;

                // StarPattern: *name in match patterns - define the star variable as LOCAL
                case StarPattern starPat:
                    if (starPat.Name != "_")
                    {
                        // CPython: symtable_add_def(st, name, DEF_LOCAL)
                        _currentTable?.DefineSymbol(starPat.Name, SymbolFlags.Assigned);
#if DEBUG_COMPILER_LOG
                        Console.WriteLine($"        AnalyzePattern: Defined star pattern variable '{starPat.Name}' as LOCAL");
#endif
                    }
                    break;

                // MatchMapping: {key: pattern, ...} - TODO: implement if needed
                // MatchClass: ClassName(patterns...) - TODO: implement if needed
                // For now, treat these as expressions

                // MatchAs: pattern as name - analyze nested pattern and define name
                // In SharpPy AST, this might be represented differently
                // For now, handle bare NameExpression as capture variable
                case NameExpression name:
                    // Wildcard pattern _ doesn't define a variable
                    if (name.Name != "_" && !IsKeyword(name.Name))
                    {
                        // CPython: symtable_add_def(st, name, DEF_LOCAL)
                        _currentTable?.DefineSymbol(name.Name, SymbolFlags.Assigned);
#if DEBUG_COMPILER_LOG
                        Console.WriteLine($"        AnalyzePattern: Defined capture variable '{name.Name}' as LOCAL");
#endif
                    }
                    break;

                // MatchOr: pattern1 | pattern2 - recursively analyze all alternatives
                case BinOpExpression binOp when binOp.OpNode.OperatorType == "BitOr":
                    AnalyzePattern(binOp.Left);
                    AnalyzePattern(binOp.Right);
                    break;

                // Default: treat as expression (for complex patterns)
                default:
#if DEBUG_COMPILER_LOG
                    Console.WriteLine($"        AnalyzePattern: Treating {pattern.GetType().Name} as expression");
#endif
                    AnalyzeExpression(pattern);
                    break;
            }
        }

        /// <summary>
        /// CPython 3.12: Analyze expressions for variable references
        /// </summary>
        private void AnalyzeExpression(Expression expr)
        {
            switch (expr)
            {
                case NameExpression name:
                    // Reference to a variable - define it if not already defined
#if DEBUG_COMPILER_LOG
                    Console.WriteLine($"      AnalyzeExpression: NameExpression '{name.Name}' in scope '{_currentTable?.GetName()}'");
#endif
                    // Only add if it's not a keyword
                    if (!IsKeyword(name.Name))
                    {
                        _currentTable?.DefineSymbol(name.Name, SymbolFlags.Used);
                    }
                    break;

                case TupleExpression tuple:
                    foreach (var element in tuple.Elements)
                    {
                        AnalyzeExpression(element);
                    }
                    break;

                case ListExpression list:
                    foreach (var element in list.Elements)
                    {
                        AnalyzeExpression(element);
                    }
                    break;

                case CallExpression call:
                    AnalyzeExpression(call.Function);
                    foreach (var arg in call.Arguments)
                    {
                        AnalyzeExpression(arg);
                    }
                    break;

                case AttributeExpression attr:
#if DEBUG_COMPILER_LOG
                    Console.WriteLine($"      AnalyzeExpression: AttributeExpression '{attr.Attr}' on object in scope '{_currentTable?.GetName()}'");
#endif
                    AnalyzeExpression(attr.Value);
                    break;

                case BinOpExpression binOp:
                    AnalyzeExpression(binOp.Left);
                    AnalyzeExpression(binOp.Right);
                    break;

                case CompareExpression compareOp:
                    AnalyzeExpression(compareOp.Left);
                    foreach (var comparator in compareOp.Comparators)
                    {
                        AnalyzeExpression(comparator);
                    }
                    break;

                case UnaryOpExpression unaryOp:
                    AnalyzeExpression(unaryOp.Operand);
                    break;

                case FStringExpression fstring:
                    // Analyze all expressions within the f-string
                    foreach (var value in fstring.Values)
                    {
                        AnalyzeExpression(value);
                    }
                    break;

                case JoinedStrExpression joinedStr:
                    // CPython 3.12: JoinedStr is the AST node for f-strings
                    // Same as FStringExpression - analyze all expressions within
                    foreach (var value in joinedStr.Values)
                    {
                        AnalyzeExpression(value);
                    }
                    break;

                case FormattedValueExpression formattedValue:
                    // CPython 3.12: Recursively analyze the value expression inside the formatted value
                    // This is critical for: f"{x for x in items}" where genexpr is inside f-string
                    AnalyzeExpression(formattedValue.Value);
                    // Also analyze format spec if present
                    if (formattedValue.FormatSpec != null)
                    {
                        AnalyzeExpression(formattedValue.FormatSpec);
                    }
                    break;

                case LambdaExpression lambda:
                    // Lambda functions need their own scope to track free variables
#if DEBUG_COMPILER_LOG
                    Console.WriteLine($"      AnalyzeExpression: LambdaExpression in scope '{_currentTable?.GetName()}'");
#endif
                    AnalyzeLambda(lambda);
                    break;

                case WalrusExpression walrus:
                    // Analyze the value expression first
                    AnalyzeExpression(walrus.Value);
                    // In CPython 3.12, walrus variables scoping depends on context:
                    // - Module scope: Used (→ LOAD_GLOBAL/STORE_GLOBAL)
                    // - Function scope: Assigned (→ LOAD_FAST/STORE_FAST)
                    var flags = _currentTable?.Type == SymbolTableType.Module
                        ? SymbolFlags.Used
                        : SymbolFlags.Assigned;
                    _currentTable?.DefineSymbol(walrus.Target, flags);
                    break;

                case NamedExpression named:
                    // CPython 3.12: symtable.c:2001-2020 - symtable_handle_namedexpr
                    // Analyze the value expression first
                    AnalyzeExpression(named.Value);

                    // Add the target variable to the correct scope
                    if (named.Target is NameExpression nameTarget)
                    {
                        // If we're inside a comprehension, use extend scope logic
                        // Otherwise, add to current scope directly
                        if (_currentTable?.IsComprehension == true)
                        {
                            // CPython 3.12: symtable.c:2015 - symtable_extend_namedexpr_scope
                            ExtendNamedExprScope(nameTarget.Name);
                        }
                        else
                        {
                            // Direct assignment in function/module scope
                            _currentTable?.DefineSymbol(nameTarget.Name, SymbolFlags.Assigned);
                        }

                        // CPython 3.12: symtable.c:2019 - VISIT(st, expr, e->v.NamedExpr.target)
                        // Also visit the target as a USE in current scope (important for generator expressions!)
                        // This marks the variable as USED in the comprehension scope, making it a FREE variable
                        // which then causes the parent function to mark it as a CELL variable
                        AnalyzeExpression(named.Target);
                    }
                    break;

                case ConditionalExpression conditional:
                    // Analyze test, body, and orelse expressions
                    AnalyzeExpression(conditional.Test);
                    AnalyzeExpression(conditional.Body);
                    AnalyzeExpression(conditional.OrElse);
                    break;

                case GeneratorExpression genExpr:
                    // CPython 3.12: symtable.c:2605-2610 - symtable_visit_genexp
                    AnalyzeGeneratorExpression(genExpr);
                    break;

                case ListComprehension listComp:
                    // CPython 3.12: symtable.c:2612-2618 - symtable_visit_listcomp
                    AnalyzeListComprehension(listComp);
                    break;

                case DictComprehension dictComp:
                    // CPython 3.12: symtable.c:2628-2634 - symtable_visit_dictcomp
                    AnalyzeDictComprehension(dictComp);
                    break;

                case SetComprehension setComp:
                    // CPython 3.12: symtable.c:2620-2626 - symtable_visit_setcomp
                    AnalyzeSetComprehension(setComp);
                    break;

                case YieldExpression yieldExpr:
                    // CPython 3.12: symtable.c:2108-2114 - Set ste_generator flag
                    if (_currentTable != null)
                    {
                        _currentTable.IsGenerator = true;
#if DEBUG_COMPILER_LOG
                        Console.WriteLine($"      AnalyzeExpression: YieldExpression - marked scope '{_currentTable.GetName()}' as generator");
#endif
                    }
                    if (yieldExpr.Value != null)
                    {
                        AnalyzeExpression(yieldExpr.Value);
                    }
                    break;

                case YieldFromExpression yieldFromExpr:
                    // CPython 3.12: symtable.c:2115-2122 - Set ste_generator flag
                    if (_currentTable != null)
                    {
                        _currentTable.IsGenerator = true;
#if DEBUG_COMPILER_LOG
                        Console.WriteLine($"      AnalyzeExpression: YieldFromExpression - marked scope '{_currentTable.GetName()}' as generator");
#endif
                    }
                    AnalyzeExpression(yieldFromExpr.Value);
                    break;

                case StarredExpression starred:
                    // CPython 3.12: Starred expressions (e.g., *expr in function calls)
                    // Need to recursively analyze the inner expression
                    // This is critical for: list(*(x for x in items))
                    // Without this, the generator expression would not get its symbol table created
                    AnalyzeExpression(starred.Value);
                    break;

                // Skip constants and other literal expressions
                default:
                    break;
            }
        }

        private void AnalyzeLambda(LambdaExpression lambda)
        {
            // Create new scope for lambda body - similar to function but unnamed
            var lambdaTable = new SymbolTable($"<lambda_{_lambdaCounter++}>", SymbolTableType.Function);
            _currentTable?.AddChild(lambdaTable);

            // CPython 3.12: Register lambda AST node → SymbolTable mapping (st_blocks)
            // This allows compiler to find symbol table by AST node reference
            _astNodeToSymbolTable.Add(lambda, lambdaTable);

            var savedTable = _currentTable;
            _currentTable = lambdaTable;

            // CPython 3.12: Push onto scope stack (symtable.c:331)
            _scopeStack.Push(_currentTable);

            // Add lambda parameters to scope
            foreach (var param in lambda.Args)
            {
                // Extract parameter name (handle defaults, *args, **kwargs if needed)
                var paramName = param.Split('=')[0].Trim();
                if (paramName.StartsWith("**"))
                    paramName = paramName.Substring(2);
                else if (paramName.StartsWith("*"))
                    paramName = paramName.Substring(1);

                _currentTable.DefineSymbol(paramName, SymbolFlags.Parameter | SymbolFlags.Assigned);
            }

            // Analyze lambda body (single expression)
#if DEBUG_COMPILER_LOG
            Console.WriteLine($"        Analyzing lambda body in scope '{_currentTable?.GetName()}'");
#endif
            AnalyzeExpression(lambda.Body);

            // After analyzing body, resolve free variables
            ResolveFreeVariables(_currentTable);

            // CPython 3.12: Pop from scope stack (symtable.c:353)
            _scopeStack.Pop();

            _currentTable = savedTable;
        }

        private void AnalyzeAsyncFunction(AsyncFunctionDefStatement func)
        {
            // Similar to regular function
            _currentTable?.DefineSymbol(func.Name, SymbolFlags.Assigned);

            var functionTable = new SymbolTable($"<async function:{func.Name}>", SymbolTableType.Function);
            // CPython 3.12: Mark async functions as coroutines
            functionTable.IsCoroutine = true;
            _currentTable?.AddChild(functionTable);

            var savedTable = _currentTable;
            _currentTable = functionTable;

            // CPython 3.12: Push onto scope stack (symtable.c:331)
            _scopeStack.Push(_currentTable);

            foreach (var param in func.Parameters)
            {
                // Extract parameter name from "param=default" format
                var paramName = param.Split('=')[0].Trim();
                // Handle *args and **kwargs
                if (paramName.StartsWith("**"))
                    paramName = paramName.Substring(2);
                else if (paramName.StartsWith("*"))
                    paramName = paramName.Substring(1);

                _currentTable.DefineSymbol(paramName, SymbolFlags.Parameter | SymbolFlags.Assigned);
            }

            foreach (var stmt in func.Body)
            {
                AnalyzeStatement(stmt);
            }

            // After analyzing body, resolve free variables
            ResolveFreeVariables(_currentTable);

            // CPython 3.12: Pop from scope stack (symtable.c:353)
            _scopeStack.Pop();

            _currentTable = savedTable;
        }

        // CPython 3.12: symtable.c:2530-2602 - symtable_handle_comprehension
        private void AnalyzeComprehension(Expression expr, string scopeName, List<Comprehension> generators,
            Expression element, Expression? keyOrValue = null, bool isGenerator = false)
        {
            if (generators.Count == 0)
                return;

            // CPython 3.12: Outermost iterator is evaluated in current scope (line 2549-2552)
            var outermost = generators[0];
            AnalyzeExpression(outermost.Iter);

            // CPython 3.12: Create comprehension scope for the rest (line 2554-2559)
            var compTable = new SymbolTable(scopeName, SymbolTableType.Function);
            _currentTable?.AddChild(compTable);

            // CPython 3.12: Register AST node → symbol table mapping (symtable.c:142)
            // Corresponds to PyDict_SetItem(st->st_blocks, ste->ste_id, ste)
            _astNodeToSymbolTable.Add(expr, compTable);
#if DEBUG_COMPILER_LOG
            Console.WriteLine($"[SYMTABLE] Registered AST node: type={expr.GetType().Name}, HashCode={expr.GetHashCode()}, SymbolTable={scopeName}");
#endif

            var savedTable = _currentTable;
            _currentTable = compTable;

            // CPython 3.12: Mark as comprehension (symtable.c:2555)
            _currentTable.IsComprehension = true;

            // CPython 3.12: Push onto scope stack (symtable.c:331)
            _scopeStack.Push(_currentTable);

            // CPython 3.12: Mark as generator if needed (line 2593)
            if (isGenerator)
            {
                _currentTable.IsGenerator = true;
            }

            // CPython 3.12: Outermost iter is received as an implicit argument (line 2579-2582)
            _currentTable.DefineSymbol(".0", SymbolFlags.Parameter | SymbolFlags.Assigned);

            // CPython 3.12: Visit iteration variable target (line 2584-2586)
            AnalyzeComprehensionTarget(outermost.Target);

            // CPython 3.12: Visit the rest of the comprehension body (line 2588-2592)
            foreach (var condition in outermost.Ifs)
            {
                AnalyzeExpression(condition);
            }

            // Visit remaining generators
            for (int i = 1; i < generators.Count; i++)
            {
                var gen = generators[i];
                AnalyzeExpression(gen.Iter);
                AnalyzeComprehensionTarget(gen.Target);
                foreach (var condition in gen.Ifs)
                {
                    AnalyzeExpression(condition);
                }
            }

            // Visit the element expression (and key/value for dict comprehensions)
            if (keyOrValue != null)
                AnalyzeExpression(keyOrValue);
            AnalyzeExpression(element);

            // CPython 3.12: Resolve free variables before exiting scope
            ResolveFreeVariables(_currentTable);

            // CPython 3.12: Pop from scope stack (symtable.c:353)
            _scopeStack.Pop();

            _currentTable = savedTable;
        }

        // CPython 3.12: symtable.c:2605-2610
        private void AnalyzeGeneratorExpression(GeneratorExpression genExpr)
        {
#if DEBUG_COMPILER_LOG
            Console.WriteLine($"      AnalyzeExpression: GeneratorExpression in scope '{_currentTable?.GetName()}'");
#endif
            AnalyzeComprehension(genExpr, "<genexpr>", genExpr.Generators, genExpr.Element, isGenerator: true);
        }

        // CPython 3.12: symtable.c:2612-2618
        private void AnalyzeListComprehension(ListComprehension listComp)
        {
#if DEBUG_COMPILER_LOG
            Console.WriteLine($"      AnalyzeExpression: ListComprehension in scope '{_currentTable?.GetName()}'");
#endif
            AnalyzeComprehension(listComp, "<listcomp>", listComp.Generators, listComp.Element, isGenerator: false);
        }

        // CPython 3.12: symtable.c:2620-2626
        private void AnalyzeSetComprehension(SetComprehension setComp)
        {
#if DEBUG_COMPILER_LOG
            Console.WriteLine($"      AnalyzeExpression: SetComprehension in scope '{_currentTable?.GetName()}'");
#endif
            AnalyzeComprehension(setComp, "<setcomp>", setComp.Generators, setComp.Element, isGenerator: false);
        }

        // CPython 3.12: symtable.c:2628-2634
        private void AnalyzeDictComprehension(DictComprehension dictComp)
        {
#if DEBUG_COMPILER_LOG
            Console.WriteLine($"      AnalyzeExpression: DictComprehension in scope '{_currentTable?.GetName()}'");
#endif
            AnalyzeComprehension(dictComp, "<dictcomp>", dictComp.Generators, dictComp.Value, dictComp.Key, isGenerator: false);
        }

        private void AnalyzeClass(ClassDefStatement cls)
        {
            // Define class name in current scope
            _currentTable?.DefineSymbol(cls.Name, SymbolFlags.Assigned);

            // Create new scope for class body
            var classTable = new SymbolTable(cls.Name, SymbolTableType.Class);
            _currentTable?.AddChild(classTable);

            var savedTable = _currentTable;
            _currentTable = classTable;

            // CPython 3.12: Push onto scope stack (symtable.c:331)
            _scopeStack.Push(_currentTable);

            // Analyze class body
            foreach (var stmt in cls.Body)
            {
                AnalyzeStatement(stmt);
            }

            // CPython 3.12: Resolve free variables after analyzing class body
            // This ensures nested lambdas and comprehensions have correct scope
            ResolveFreeVariables(_currentTable);

            _currentTable = savedTable;
        }

        private void AnalyzeAssignment(AssignStatement assign)
        {
            // For assignments like "x = value" or "a, b = value1, value2"
            // CPython 3.12: Extract names from all targets (including tuple unpacking)
            foreach (var target in assign.Targets)
            {
                AnalyzeAssignmentTarget(target);  // Use helper to handle all target types
            }

            // Also analyze the right-hand side expression
            if (assign.Value != null)
            {
                AnalyzeExpression(assign.Value);
            }
        }

        private void AnalyzeChainedAssignment(ChainedAssignStatement chainedAssign)
        {
#if DEBUG_COMPILER_LOG
            Console.WriteLine($"  AnalyzeChainedAssignment: Processing {chainedAssign.Targets.Count} targets in scope '{_currentTable?.GetName()}'");
#endif
            // For chained assignments like "a = b = c = value" or "gen = (x for x in [1, 2, 3])"
            // First analyze the right-hand side expression
            if (chainedAssign.Value != null)
            {
                AnalyzeExpression(chainedAssign.Value);
            }

            // Then mark all targets as assigned (left-to-right)
            foreach (var target in chainedAssign.Targets)
            {
#if DEBUG_COMPILER_LOG
                Console.WriteLine($"    → Analyzing target: {target.GetType().Name}");
#endif
                AnalyzeAssignmentTarget(target);
            }
        }

        private void AnalyzeAssignTarget(AssignTargetStatement assignTarget)
        {
            // For tuple assignments like "a, b = value" or "a, b = b, a + b"
            // CRITICAL: Analyze TARGET first to define assignment variables,
            // THEN analyze VALUE that may reference them

            AnalyzeAssignmentTarget(assignTarget.Target);

            // Then analyze the right-hand side expression
            if (assignTarget.Value != null)
            {
                AnalyzeExpression(assignTarget.Value);
            }
        }

        private void AnalyzeAugmentedAssignment(AugmentedAssignStatement augAssign)
        {
            // For compound assignments like "current += step * value"
            // The target is already defined (as nonlocal in this case)
            // We need to analyze the right-hand side expression to find variable references
            AnalyzeExpression(augAssign.Target);
            AnalyzeExpression(augAssign.Value);
        }

        private void AnalyzeAugAssignment(AugAssignStatement augAssign)
        {
            // For compound assignments like "current += step * value"
            // The target is already defined (as nonlocal in this case)
            // We need to analyze the right-hand side expression to find variable references
            if (augAssign.Target != null)
            {
                _currentTable?.DefineSymbol(augAssign.Target, SymbolFlags.Assigned);
            }
            AnalyzeExpression(augAssign.Value);
        }

        private void AnalyzeAnnAssignment(AnnAssignStatement annAssign)
        {
            // For annotated assignments like "x: int = 5" or "y: list[str]"
            // CPython 3.12: Variable is defined even if no value is assigned
            _currentTable?.DefineSymbol(annAssign.VariableName, SymbolFlags.Assigned);

            // Analyze the annotation expression (e.g., int, list[str])
            AnalyzeExpression(annAssign.Annotation);

            // Analyze the value expression if present
            if (annAssign.Value != null)
            {
                AnalyzeExpression(annAssign.Value);
            }
        }

        private void AnalyzeAssignmentTarget(Expression target)
        {
            switch (target)
            {
                case NameExpression name:
                    // Simple assignment target: mark as assigned
                    _currentTable?.DefineSymbol(name.Name, SymbolFlags.Assigned);
                    break;

                case TupleExpression tuple:
                    // Tuple unpacking: a, b = value
                    foreach (var element in tuple.Elements)
                    {
                        AnalyzeAssignmentTarget(element);
                    }
                    break;

                case ListExpression list:
                    // List unpacking: [a, b] = value
                    foreach (var element in list.Elements)
                    {
                        AnalyzeAssignmentTarget(element);
                    }
                    break;

                default:
                    // For other assignment targets, just analyze as expression
                    AnalyzeExpression(target);
                    break;
            }
        }

        /// <summary>
        /// CPython 3.12: Analyze comprehension target variables (like 'f' in 'for f in functions')
        /// These are treated as local variables within the comprehension scope
        /// </summary>
        private void AnalyzeComprehensionTarget(Expression target)
        {
            switch (target)
            {
                case NameExpression name:
                    // CPython 3.12: Comprehension variables are local to the function scope
                    // They don't become global variables
                    _currentTable?.DefineSymbol(name.Name, SymbolFlags.Assigned);
                    break;

                case TupleExpression tuple:
                    // Tuple unpacking in comprehension: for (a, b) in items
                    foreach (var element in tuple.Elements)
                    {
                        AnalyzeComprehensionTarget(element);
                    }
                    break;

                case ListExpression list:
                    // List unpacking in comprehension: for [a, b] in items
                    foreach (var element in list.Elements)
                    {
                        AnalyzeComprehensionTarget(element);
                    }
                    break;

                default:
                    // For other target types, just analyze as expression
                    AnalyzeExpression(target);
                    break;
            }
        }
        private static bool IsKeyword(string name)
        {
            var pythonKeywords = new HashSet<string>
            {
                "False", "None", "True", "__peg_parser__", "and", "as", "assert", "async", "await",
                "break", "class", "continue", "def", "del", "elif", "else", "except", "finally",
                "for", "from", "global", "if", "import", "in", "is", "lambda", "nonlocal", "not",
                "or", "pass", "raise", "return", "try", "while", "with", "yield", "match", "case"
            };
            return pythonKeywords.Contains(name);
        }

        /// <summary>
        /// Define symbols from target expression (supports tuple unpacking)
        /// CPython 3.12: for k, v in items -> both k and v are assigned
        /// </summary>
        private void DefineTargetSymbols(Expression target, SymbolFlags flags)
        {
            if (target is NameExpression nameExpr)
            {
                _currentTable?.DefineSymbol(nameExpr.Name, flags);
            }
            else if (target is TupleExpression tupleExpr)
            {
                foreach (var elem in tupleExpr.Elements)
                {
                    DefineTargetSymbols(elem, flags);  // Recursive for nested unpacking
                }
            }
            else if (target is ListExpression listExpr)
            {
                foreach (var elem in listExpr.Elements)
                {
                    DefineTargetSymbols(elem, flags);
                }
            }
            // AttributeExpression, SubscriptExpression don't define new symbols
        }

        /// <summary>
        /// CPython 3.12: PySymtable_Lookup(st, key)
        /// Lookup symbol table for a given AST node (comprehension/generator expression)
        /// Corresponds to PySymtable_Lookup in Python/symtable.c:381-400
        /// </summary>
        public SymbolTable? LookupSymbolTable(Expression astNode)
        {
            if (_astNodeToSymbolTable.TryGetValue(astNode, out var table))
            {
                return table;
            }
            return null;
        }
    }
}