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

            // If it's assigned locally, mark as local scope
            if ((flags & SymbolFlags.Assigned) != 0)
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
            Console.WriteLine($"    🔍 FindFreeVariables in {_name}: checking {_symbols.Count} symbols");
            foreach (var symbol in _symbols.Values)
            {
                Console.WriteLine($"      Symbol {symbol.Name}: Scope={symbol.Scope}, IsFree={symbol.IsFree()}");
                if (symbol.IsFree())
                {
                    freeVars.Add(symbol.Name);
                    Console.WriteLine($"        → Added to freeVars: {symbol.Name}");
                }
            }
            Console.WriteLine($"    → FindFreeVariables result: [{string.Join(", ", freeVars)}]");
            return freeVars;
        }

        /// <summary>
        /// CPython 3.12: Find all cell variables in this scope
        /// </summary>
        public List<string> FindCellVariables()
        {
            var cellVars = new List<string>();
            Console.WriteLine($"    🔍 FindCellVariables in {_name}: checking {_symbols.Count} symbols");
            foreach (var symbol in _symbols.Values)
            {
                Console.WriteLine($"      Symbol {symbol.Name}: Scope={symbol.Scope}, IsCell={symbol.IsCell()}");
                if (symbol.IsCell())
                {
                    cellVars.Add(symbol.Name);
                    Console.WriteLine($"        → Added to cellVars: {symbol.Name}");
                }
            }
            Console.WriteLine($"    → FindCellVariables result: [{string.Join(", ", cellVars)}]");
            return cellVars;
        }

    }

    /// <summary>
    /// Simple symbol table builder focused on basic scope analysis
    /// </summary>
    public class SymbolTableBuilder
    {
        private SymbolTable? _rootTable;
        private SymbolTable? _currentTable;

        public SymbolTable BuildSymbolTable(List<Statement> statements, string name = "<module>")
        {
            Console.WriteLine($"🔧 Building symbol table for: {name}");

            _rootTable = new SymbolTable(name, SymbolTableType.Module);
            _currentTable = _rootTable;

            // Basic analysis - just track function and class definitions for now
            foreach (var statement in statements)
            {
                AnalyzeStatement(statement);
            }

            // Resolve free variables across all scopes after initial analysis
            ResolveFreeVariablesRecursive(_rootTable);

            Console.WriteLine($"✅ Symbol table built: {_rootTable.GetIdentifiers().Count()} symbols");
            foreach (var symbol in _rootTable.GetIdentifiers())
            {
                var sym = _rootTable.Lookup(symbol);
                Console.WriteLine($"  {symbol}: {sym?.Scope} scope, flags: {sym?.Flags}");
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

                // For now, skip complex statement types
                default:
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

            // Add parameters to function scope
            foreach (var param in func.Parameters)
            {
                _currentTable.DefineSymbol(param, SymbolFlags.Parameter | SymbolFlags.Assigned);
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
            Console.WriteLine($"  🔍 ResolveFreeVariables: Processing {table.GetSymbols().Count} symbols in {table.GetName()}");

            foreach (var symbol in table.GetSymbols().Values)
            {
                Console.WriteLine($"    Symbol: {symbol.Name}, Scope: {symbol.Scope}, Flags: {symbol.Flags}");

                // Skip if already resolved or is parameter/assigned locally
                if (symbol.Scope != SymbolScope.Unknown ||
                    symbol.IsParameter() ||
                    symbol.IsAssigned())
                {
                    Console.WriteLine($"      ↳ Skipped (already resolved or local)");
                    continue;
                }

                // This is a name that was referenced but not defined locally
                // Look for it in enclosing scopes
                var foundInParent = FindInEnclosingScope(table, symbol.Name);
                if (foundInParent != null)
                {
                    // Mark as free variable (needs closure)
                    symbol.Scope = SymbolScope.Free;

                    // Mark the parent symbol as cell variable (needs to be captured)
                    foundInParent.Scope = SymbolScope.Cell;

                    Console.WriteLine($"      ↳ Marked as FREE (found in parent: {foundInParent.Name})");
                }
                else
                {
                    // Not found in any parent scope, assume global
                    symbol.Scope = SymbolScope.Global;
                    Console.WriteLine($"      ↳ Marked as GLOBAL (not found in parents)");
                }
            }
        }

        /// <summary>
        /// CPython 3.12: Find symbol in enclosing scopes (excluding module scope)
        /// Module-level variables should always be accessed as GLOBAL, not FREE
        /// </summary>
        private Symbol? FindInEnclosingScope(SymbolTable currentTable, string name)
        {
            var parent = currentTable.GetParent();
            while (parent != null)
            {
                // CPython 3.12: Module-level variables are always GLOBAL, never FREE
                if (parent.Type == SymbolTableType.Module)
                {
                    // Skip module scope - variables there should be GLOBAL
                    parent = parent.GetParent();
                    continue;
                }

                if (parent.GetSymbols().TryGetValue(name, out var symbol))
                {
                    return symbol;
                }
                parent = parent.GetParent();
            }
            return null;
        }

        /// <summary>
        /// CPython 3.12: Recursively resolve free variables in all scopes
        /// </summary>
        private void ResolveFreeVariablesRecursive(SymbolTable table)
        {
            Console.WriteLine($"🔍 Resolving free variables in scope: {table.GetName()}");

            // First recursively resolve child scopes (depth-first)
            foreach (var child in table.GetChildren())
            {
                ResolveFreeVariablesRecursive(child);

                // After child is resolved, propagate its free variables to current scope
                PropagateChildFreeVariables(table, child);
            }

            // Then resolve this scope
            ResolveFreeVariables(table);
        }

        /// <summary>
        /// CPython 3.12: Propagate free variables from child scope to parent scope
        /// If child needs a variable that parent doesn't have, parent should also need it as free
        /// </summary>
        private void PropagateChildFreeVariables(SymbolTable parent, SymbolTable child)
        {
            var childFreeVars = child.FindFreeVariables();
            Console.WriteLine($"  🔄 Propagating free vars from {child.GetName()} to {parent.GetName()}: [{string.Join(", ", childFreeVars)}]");

            foreach (var freeVar in childFreeVars)
            {
                // Check if parent has this variable
                var parentSymbol = parent.Lookup(freeVar);
                if (parentSymbol == null)
                {
                    // Parent doesn't have this variable, so parent also needs it as free variable
                    parent.DefineSymbol(freeVar, SymbolFlags.None);
                    Console.WriteLine($"    → Added {freeVar} as free variable to {parent.GetName()}");
                }
                else
                {
                    Console.WriteLine($"    → {freeVar} already available in {parent.GetName()} (scope: {parentSymbol.Scope})");
                }
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
                    _currentTable?.DefineSymbol(name.Name, SymbolFlags.None);
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
                    AnalyzeExpression(attr.Value);
                    break;

                case BinaryOpExpression binOp:
                    AnalyzeExpression(binOp.Left);
                    AnalyzeExpression(binOp.Right);
                    break;

                case UnaryOpExpression unaryOp:
                    AnalyzeExpression(unaryOp.Operand);
                    break;

                // Skip constants and other literal expressions
                default:
                    break;
            }
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
                _currentTable.DefineSymbol(param, SymbolFlags.Parameter | SymbolFlags.Assigned);
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
    }
}