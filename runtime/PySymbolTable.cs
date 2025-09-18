using System;
using System.Collections.Generic;
using System.Linq;

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

        public SymbolTable(string name, SymbolTableType type, SymbolTable? parent = null)
        {
            _name = name;
            _type = type;
            _parent = parent;
            _symbols = new Dictionary<string, Symbol>();
            _children = new List<SymbolTable>();
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

            // If it's assigned locally, mark as local scope (unless already classified as Cell or Global)
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
#if DEBUG_LOG
            Console.WriteLine($"    🔍 FindFreeVariables in {_name}: checking {_symbols.Count} symbols");
#endif
            foreach (var symbol in _symbols.Values)
            {
#if DEBUG_LOG
                Console.WriteLine($"      Symbol {symbol.Name}: Scope={symbol.Scope}, IsFree={symbol.IsFree()}");
#endif
                if (symbol.IsFree())
                {
                    freeVars.Add(symbol.Name);
#if DEBUG_LOG
                    Console.WriteLine($"        → Added to freeVars: {symbol.Name}");
#endif
                }
            }
#if DEBUG_LOG
            Console.WriteLine($"    → FindFreeVariables result: [{string.Join(", ", freeVars)}]");
#endif
            return freeVars;
        }

        /// <summary>
        /// CPython 3.12: Find all cell variables in this scope
        /// </summary>
        public List<string> FindCellVariables()
        {
            var cellVars = new List<string>();
#if DEBUG_LOG
            Console.WriteLine($"    🔍 FindCellVariables in {_name}: checking {_symbols.Count} symbols");
#endif
            foreach (var symbol in _symbols.Values)
            {
#if DEBUG_LOG
                Console.WriteLine($"      Symbol {symbol.Name}: Scope={symbol.Scope}, IsCell={symbol.IsCell()}");
#endif
                if (symbol.IsCell())
                {
                    cellVars.Add(symbol.Name);
#if DEBUG_LOG
                    Console.WriteLine($"        → Added to cellVars: {symbol.Name}");
#endif
                }
            }
#if DEBUG_LOG
            Console.WriteLine($"    → FindCellVariables result: [{string.Join(", ", cellVars)}]");
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

        public SymbolTable BuildSymbolTable(List<Statement> statements, string name = "<module>")
        {
#if DEBUG_LOG
            Console.WriteLine($"🔧 Building symbol table for: {name}");
#endif

            // Initialize/reset state to prevent issues from previous builds
            _processedTables.Clear();
            _propagationInProgress.Clear();
            _cellProcessingInProgress.Clear();
            _recursionDepth = 0;

            _rootTable = new SymbolTable(name, SymbolTableType.Module);
            _currentTable = _rootTable;

            // Basic analysis - just track function and class definitions for now
            foreach (var statement in statements)
            {
                AnalyzeStatement(statement);
            }

            // Resolve free variables across all scopes after initial analysis
            ResolveFreeVariablesRecursive(_rootTable);

#if DEBUG_LOG
            Console.WriteLine($"✅ Symbol table built: {_rootTable.GetIdentifiers().Count()} symbols");
#endif
            foreach (var symbol in _rootTable.GetIdentifiers())
            {
                var sym = _rootTable.Lookup(symbol);
#if DEBUG_LOG
                Console.WriteLine($"  {symbol}: {sym?.Scope} scope, flags: {sym?.Flags}");
#endif
            }

            return _rootTable;
        }

        private void AnalyzeStatement(Statement statement)
        {
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

                case AssignTargetStatement assignTarget:
                    AnalyzeAssignTarget(assignTarget);
                    break;

                case AugmentedAssignStatement augAssign:
                    AnalyzeAugmentedAssignment(augAssign);
                    break;

                case AugAssignStatement augAssign:
                    AnalyzeAugAssignment(augAssign);
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
                    if (returnStmt.Value != null)
                    {
                        AnalyzeExpression(returnStmt.Value);
                    }
                    break;

                case ExpressionStatement exprStmt:
                    AnalyzeExpression(exprStmt.Expression);
                    break;

                case TryStatement tryStmt:
#if DEBUG_LOG
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
#if DEBUG_LOG
                    Console.WriteLine($"  AnalyzeStatement: ForStatement in scope '{_currentTable?.GetName()}'");
#endif
                    // 1. Analyze iterable expression
                    AnalyzeExpression(forStmt.Iter);

                    // 2. Define loop variable as assigned
                    _currentTable?.DefineSymbol(forStmt.Target, SymbolFlags.Assigned);

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
#if DEBUG_LOG
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

                // For now, skip complex statement types
                default:
#if DEBUG_LOG
                    Console.WriteLine($"  AnalyzeStatement: Unhandled statement type {statement.GetType().Name} in scope '{_currentTable?.GetName()}'");
#endif
                    break;
            }
        }

        private void AnalyzeFunction(FunctionDefStatement func)
        {
            // Define function name in current scope
            _currentTable?.DefineSymbol(func.Name, SymbolFlags.Assigned);

            // Create new scope for function body
            var functionTable = new SymbolTable($"<function:{func.Name}>", SymbolTableType.Function);
            _currentTable?.AddChild(functionTable);

            var savedTable = _currentTable;
            _currentTable = functionTable;

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

            // After analyzing body, resolve free variables
            ResolveFreeVariables(_currentTable);

            _currentTable = savedTable;
        }

        /// <summary>
        /// CPython 3.12: Resolve free variables by checking parent scopes
        /// </summary>
        private void ResolveFreeVariables(SymbolTable table)
        {
#if DEBUG_LOG
            Console.WriteLine($"  🔍 ResolveFreeVariables: Processing {table.GetSymbols().Count} symbols in {table.GetName()}");
#endif

            foreach (var symbol in table.GetSymbols().Values)
            {
#if DEBUG_LOG
                Console.WriteLine($"    Symbol: {symbol.Name}, Scope: {symbol.Scope}, Flags: {symbol.Flags}");
#endif

                // Special handling for nonlocal variables
                if (symbol.IsNonlocal())
                {
#if DEBUG_LOG
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

#if DEBUG_LOG
                        Console.WriteLine($"      ↳ NONLOCAL marked as FREE (found in parent: {parentSymbol.Name})");
#endif
                    }
                    else
                    {
                        // nonlocal variable not found in parent scope - this is an error
#if DEBUG_LOG
                        Console.WriteLine($"      ⚠️ NONLOCAL variable '{symbol.Name}' not found in enclosing scope");
#endif
                        symbol.Scope = SymbolScope.Global; // fallback
                    }
                    continue;
                }

                // Skip if already resolved or is parameter/assigned locally
                if (symbol.Scope != SymbolScope.Unknown)
                {
#if DEBUG_LOG
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
#if DEBUG_LOG
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

#if DEBUG_LOG
                        Console.WriteLine($"      ↳ Marked as FREE (found in parent: {foundInParent.Name})");
#endif
                    }
                    else
                    {
                        // Not found in any parent scope, assume global
                        symbol.Scope = SymbolScope.Global;
#if DEBUG_LOG
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
#if DEBUG_LOG
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
#if DEBUG_LOG
                    Console.WriteLine($"      ↳ Found {name} in enclosing scope {parent.GetName()}");
#endif
                    // CPython 3.12: If the symbol is declared as global in the parent scope,
                    // it should not be treated as a free variable in the current scope
                    if (symbol.IsGlobal())
                    {
#if DEBUG_LOG
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
#if DEBUG_LOG
                Console.WriteLine($"⚠️  Skipping already processed table: {table.GetName()}");
#endif
                _recursionDepth--;
                return;
            }

            _processedTables.Add(table);

#if DEBUG_LOG
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
#if DEBUG_LOG
                Console.WriteLine($"⚠️  Skipping already processed cell variables for: {table.GetName()}");
#endif
                return;
            }

            _cellProcessingInProgress.Add(tableKey);

            try
            {
#if DEBUG_LOG
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

#if DEBUG_LOG
                    Console.WriteLine($"    Checking child scope: {child.GetName()}");
#endif
                    foreach (var childSymbol in child.GetSymbols().Values)
                    {
#if DEBUG_LOG
                        Console.WriteLine($"      Symbol {childSymbol.Name}: Scope={childSymbol.Scope}, IsFree={childSymbol.IsFree()}");
#endif
                        // If child has a free or cell variable, parent should have it as cell variable
                        // BUT ONLY if the parent actually defines/owns this variable
                        // CPython 3.12: Cell variables in child scopes also need parent cells
                        if (childSymbol.IsFree() || childSymbol.Scope == SymbolScope.Cell)
                        {
                            var parentSymbol = table.Lookup(childSymbol.Name);
#if DEBUG_LOG
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
#if DEBUG_LOG
                                Console.WriteLine($"    → ✅ Marking {childSymbol.Name} as CELL in {table.GetName()} (used as FREE in {child.GetName()})");
#endif
                            parentSymbol.Scope = SymbolScope.Cell;
                        }
                        else if (parentSymbol != null &&
                                 (parentSymbol.Scope == SymbolScope.Free ||
                                  parentSymbol.Scope == SymbolScope.Unknown))
                        {
                            // Parent also treats this as Free/Unknown variable - don't change to Cell
#if DEBUG_LOG
                            Console.WriteLine($"    → ❌ Skipping {childSymbol.Name} - parent scope is {parentSymbol.Scope}, not owner");
#endif
                        }
                        else if (parentSymbol != null)
                        {
#if DEBUG_LOG
                            Console.WriteLine($"    → ⚠️ Checking {childSymbol.Name} - parent scope: {parentSymbol.Scope}, assigned: {parentSymbol.IsAssigned()}");
#endif
                        }
                        else
                        {
#if DEBUG_LOG
                            Console.WriteLine($"    → ❓ {childSymbol.Name} not found in parent {table.GetName()}");
#endif
                        }
                    }
                }
            }

#if DEBUG_LOG
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
#if DEBUG_LOG
                Console.WriteLine($"⚠️  Skipping circular propagation: {propagationKey}");
#endif
                return;
            }

            _propagationInProgress.Add(propagationKey);

            try
            {
                var childFreeVars = child.FindFreeVariables();
#if DEBUG_LOG
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
#if DEBUG_LOG
                        Console.WriteLine($"    → Added {freeVar} as free variable to {parent.GetName()}");
#endif
                    }
                    else
                    {
#if DEBUG_LOG
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
        /// CPython 3.12: Analyze expressions for variable references
        /// </summary>
        private void AnalyzeExpression(Expression expr)
        {
            switch (expr)
            {
                case NameExpression name:
                    // Reference to a variable - define it if not already defined
#if DEBUG_LOG
                    Console.WriteLine($"      AnalyzeExpression: NameExpression '{name.Name}' in scope '{_currentTable?.GetName()}'");
#endif
                    _currentTable?.DefineSymbol(name.Name, SymbolFlags.Used);
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
#if DEBUG_LOG
                    Console.WriteLine($"      AnalyzeExpression: AttributeExpression '{attr.Attr}' on object in scope '{_currentTable?.GetName()}'");
#endif
                    AnalyzeExpression(attr.Value);
                    break;

                case BinaryOpExpression binOp:
                    AnalyzeExpression(binOp.Left);
                    AnalyzeExpression(binOp.Right);
                    break;

                case CompareExpression compareOp:
                    AnalyzeExpression(compareOp.Left);
                    AnalyzeExpression(compareOp.Right);
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

                case LambdaExpression lambda:
                    // Lambda functions need their own scope to track free variables
#if DEBUG_LOG
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

                case ConditionalExpression conditional:
                    // Analyze test, body, and orelse expressions
                    AnalyzeExpression(conditional.Test);
                    AnalyzeExpression(conditional.Body);
                    AnalyzeExpression(conditional.OrElse);
                    break;

                case ListComprehension listComp:
                    // Analyze list comprehensions (walrus operators can be in conditions)
                    // CPython 3.12: Analyze generators first to define iteration variables
                    // before analyzing element expression (which may contain lambdas that reference them)
                    foreach (var generator in listComp.Generators)
                    {
                        AnalyzeExpression(generator.Iter);
                        // CPython 3.12: Comprehension target variables are local to the comprehension
                        AnalyzeComprehensionTarget(generator.Target);
                        foreach (var condition in generator.Ifs)
                        {
                            AnalyzeExpression(condition);
                        }
                    }
                    // Now analyze the element expression after iteration variables are defined
                    AnalyzeExpression(listComp.Element);
                    break;

                case DictComprehension dictComp:
                    // Analyze dict comprehensions
                    // CPython 3.12: Analyze generators first to define iteration variables
                    // before analyzing key/value expressions (which may contain lambdas that reference them)
                    foreach (var generator in dictComp.Generators)
                    {
                        AnalyzeExpression(generator.Iter);
                        // CPython 3.12: Comprehension target variables are local to the comprehension
                        AnalyzeComprehensionTarget(generator.Target);
                        foreach (var condition in generator.Ifs)
                        {
                            AnalyzeExpression(condition);
                        }
                    }
                    // Now analyze key and value expressions after iteration variables are defined
                    AnalyzeExpression(dictComp.Key);
                    AnalyzeExpression(dictComp.Value);
                    break;

                case SetComprehension setComp:
                    // Analyze set comprehensions
                    // CPython 3.12: Analyze generators first to define iteration variables
                    // before analyzing element expression (which may contain lambdas that reference them)
                    foreach (var generator in setComp.Generators)
                    {
                        AnalyzeExpression(generator.Iter);
                        // CPython 3.12: Comprehension target variables are local to the comprehension
                        AnalyzeComprehensionTarget(generator.Target);
                        foreach (var condition in generator.Ifs)
                        {
                            AnalyzeExpression(condition);
                        }
                    }
                    // Now analyze the element expression after iteration variables are defined
                    AnalyzeExpression(setComp.Element);
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

            var savedTable = _currentTable;
            _currentTable = lambdaTable;

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
#if DEBUG_LOG
            Console.WriteLine($"        Analyzing lambda body in scope '{_currentTable?.GetName()}'");
#endif
            AnalyzeExpression(lambda.Body);

            // After analyzing body, resolve free variables
            ResolveFreeVariables(_currentTable);

            _currentTable = savedTable;
        }

        private void AnalyzeAsyncFunction(AsyncFunctionDefStatement func)
        {
            // Similar to regular function
            _currentTable?.DefineSymbol(func.Name, SymbolFlags.Assigned);

            var functionTable = new SymbolTable($"<async function:{func.Name}>", SymbolTableType.Function);
            _currentTable?.AddChild(functionTable);

            var savedTable = _currentTable;
            _currentTable = functionTable;

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

            _currentTable = savedTable;
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

            // Analyze class body
            foreach (var stmt in cls.Body)
            {
                AnalyzeStatement(stmt);
            }

            _currentTable = savedTable;
        }

        private void AnalyzeAssignment(AssignStatement assign)
        {
            // For simple assignments like "x = value"
            _currentTable?.DefineSymbol(assign.VariableName, SymbolFlags.Assigned);

            // Also analyze the right-hand side expression
            if (assign.Value != null)
            {
                AnalyzeExpression(assign.Value);
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
    }
}