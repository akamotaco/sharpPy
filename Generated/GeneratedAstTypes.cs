// Generated AST Types - CPython 3.12 compatible type hierarchy
// This file defines the type-safe AST node classes to avoid boxing/unboxing

using System.Collections.Generic;

namespace SharpPy.Generated
{
    // ============================================================
    // Base Types
    // ============================================================

    /// <summary>
    /// Base class for all AST nodes - replaces object-based approach
    /// CPython 3.12: All AST nodes inherit from this
    /// </summary>
    public abstract class GeneratedAstNode : GeneratedPtr
    {
        public int LineNo { get; set; }
        public int ColOffset { get; set; }
        public int EndLineNo { get; set; }
        public int EndColOffset { get; set; }
    }

    /// <summary>
    /// CPython 3.12: Represents void* return type for rules without explicit type
    /// Used for invalid_* error recovery rules that always return NULL
    /// This replaces C#'s object type to maintain type safety
    /// </summary>
    public class GeneratedVoidNode : GeneratedAstNode
    {
        // Singleton instance since invalid rules always return null/void
        public static readonly GeneratedVoidNode Instance = new GeneratedVoidNode();

        private GeneratedVoidNode() { }
    }

    // ============================================================
    // Statement Types (stmt_ty)
    // ============================================================

    /// <summary>
    /// Base class for all statement nodes
    /// CPython 3.12: stmt_ty (union in C, inheritance in C#)
    /// </summary>
    public abstract class GeneratedStmt : GeneratedAstNode { }

    public class GeneratedFunctionDefStmt : GeneratedStmt
    {
        public string Name { get; set; } = "";
        public GeneratedArguments Arguments { get; set; } = null!; // arguments_ty
        public GeneratedStmtSeq Body { get; set; } = null!;
        public GeneratedExprSeq DecoratorList { get; set; } = new();
        public GeneratedExpr? Returns { get; set; }
        public string? TypeComment { get; set; }
        public GeneratedTypeParamSeq? TypeParams { get; set; } // type_params
    }

    public class GeneratedAsyncFunctionDefStmt : GeneratedStmt
    {
        public string Name { get; set; } = "";
        public GeneratedArguments Arguments { get; set; } = null!;
        public GeneratedStmtSeq Body { get; set; } = null!;
        public GeneratedExprSeq DecoratorList { get; set; } = new();
        public GeneratedExpr? Returns { get; set; }
        public string? TypeComment { get; set; }
        public GeneratedTypeParamSeq? TypeParams { get; set; }
    }

    public class GeneratedClassDefStmt : GeneratedStmt
    {
        public string Name { get; set; } = "";
        public GeneratedExprSeq Bases { get; set; } = new();
        public GeneratedExprSeq Keywords { get; set; } = new();
        public GeneratedStmtSeq Body { get; set; } = null!;
        public GeneratedExprSeq DecoratorList { get; set; } = new();
        public GeneratedTypeParamSeq? TypeParams { get; set; }
    }

    public class GeneratedReturnStmt : GeneratedStmt
    {
        public GeneratedExpr? Value { get; set; }
    }

    public class GeneratedDeleteStmt : GeneratedStmt
    {
        public GeneratedExprSeq Targets { get; set; } = new();
    }

    public class GeneratedAssignStmt : GeneratedStmt
    {
        public GeneratedExprSeq Targets { get; set; } = new();
        public GeneratedExpr Value { get; set; } = null!;
        public string? TypeComment { get; set; }
    }

    public class GeneratedAugAssignStmt : GeneratedStmt
    {
        public GeneratedExpr Target { get; set; } = null!;
        public string Op { get; set; } = ""; // operator
        public GeneratedExpr Value { get; set; } = null!;
    }

    public class GeneratedAnnAssignStmt : GeneratedStmt
    {
        public GeneratedExpr Target { get; set; } = null!;
        public GeneratedExpr Annotation { get; set; } = null!;
        public GeneratedExpr? Value { get; set; }
        public bool Simple { get; set; }
    }

    public class GeneratedForStmt : GeneratedStmt
    {
        public GeneratedExpr Target { get; set; } = null!;
        public GeneratedExpr Iter { get; set; } = null!;
        public GeneratedStmtSeq Body { get; set; } = null!;
        public GeneratedStmtSeq OrElse { get; set; } = new();
        public string? TypeComment { get; set; }
    }

    public class GeneratedAsyncForStmt : GeneratedStmt
    {
        public GeneratedExpr Target { get; set; } = null!;
        public GeneratedExpr Iter { get; set; } = null!;
        public GeneratedStmtSeq Body { get; set; } = null!;
        public GeneratedStmtSeq OrElse { get; set; } = new();
        public string? TypeComment { get; set; }
    }

    public class GeneratedWhileStmt : GeneratedStmt
    {
        public GeneratedExpr Test { get; set; } = null!;
        public GeneratedStmtSeq Body { get; set; } = null!;
        public GeneratedStmtSeq OrElse { get; set; } = new();
    }

    public class GeneratedIfStmt : GeneratedStmt
    {
        public GeneratedExpr Test { get; set; } = null!;
        public GeneratedStmtSeq Body { get; set; } = null!;
        public GeneratedStmtSeq OrElse { get; set; } = new();
    }

    public class GeneratedWithStmt : GeneratedStmt
    {
        public GeneratedWithItemSeq Items { get; set; } = new(); // withitem*
        public GeneratedStmtSeq Body { get; set; } = null!;
        public string? TypeComment { get; set; }
    }

    public class GeneratedAsyncWithStmt : GeneratedStmt
    {
        public GeneratedWithItemSeq Items { get; set; } = new();
        public GeneratedStmtSeq Body { get; set; } = null!;
        public string? TypeComment { get; set; }
    }

    public class GeneratedMatchStmt : GeneratedStmt
    {
        public GeneratedExpr Subject { get; set; } = null!;
        public GeneratedMatchCaseSeq Cases { get; set; } = new(); // match_case*
    }

    public class GeneratedRaiseStmt : GeneratedStmt
    {
        public GeneratedExpr? Exc { get; set; }
        public GeneratedExpr? Cause { get; set; }
    }

    public class GeneratedTryStmt : GeneratedStmt
    {
        public GeneratedStmtSeq Body { get; set; } = null!;
        public GeneratedExceptHandlerSeq Handlers { get; set; } = new(); // excepthandler*
        public GeneratedStmtSeq OrElse { get; set; } = new();
        public GeneratedStmtSeq FinallyBody { get; set; } = new();
    }

    public class GeneratedTryStarStmt : GeneratedStmt
    {
        public GeneratedStmtSeq Body { get; set; } = null!;
        public GeneratedExceptHandlerSeq Handlers { get; set; } = new();
        public GeneratedStmtSeq OrElse { get; set; } = new();
        public GeneratedStmtSeq FinallyBody { get; set; } = new();
    }

    public class GeneratedAssertStmt : GeneratedStmt
    {
        public GeneratedExpr Test { get; set; } = null!;
        public GeneratedExpr? Msg { get; set; }
    }

    public class GeneratedImportStmt : GeneratedStmt
    {
        public GeneratedAliasSeq Names { get; set; } = new(); // alias*
    }

    public class GeneratedImportFromStmt : GeneratedStmt
    {
        public string? Module { get; set; }
        public GeneratedAliasSeq Names { get; set; } = new();
        public int Level { get; set; }
    }

    public class GeneratedGlobalStmt : GeneratedStmt
    {
        public List<string> Names { get; set; } = new();
    }

    public class GeneratedNonlocalStmt : GeneratedStmt
    {
        public List<string> Names { get; set; } = new();
    }

    public class GeneratedExprStmt : GeneratedStmt
    {
        public GeneratedExpr Value { get; set; } = null!;
    }

    public class GeneratedPassStmt : GeneratedStmt { }

    public class GeneratedBreakStmt : GeneratedStmt { }

    public class GeneratedContinueStmt : GeneratedStmt { }

    // ============================================================
    // Expression Types (expr_ty)
    // ============================================================

    /// <summary>
    /// Base class for all expression nodes
    /// CPython 3.12: expr_ty (union in C, inheritance in C#)
    /// </summary>
    public abstract class GeneratedExpr : GeneratedAstNode
    {
        public string Context { get; set; } = "Load"; // Load, Store, Del
    }

    public class GeneratedBoolOpExpr : GeneratedExpr
    {
        public string Op { get; set; } = ""; // And, Or
        public GeneratedExprSeq Values { get; set; } = new();
    }

    public class GeneratedNamedExprExpr : GeneratedExpr
    {
        public GeneratedExpr Target { get; set; } = null!;
        public GeneratedExpr Value { get; set; } = null!;
    }

    public class GeneratedBinOpExpr : GeneratedExpr
    {
        public GeneratedExpr Left { get; set; } = null!;
        public string Op { get; set; } = "";
        public GeneratedExpr Right { get; set; } = null!;
    }

    public class GeneratedUnaryOpExpr : GeneratedExpr
    {
        public string Op { get; set; } = "";
        public GeneratedExpr Operand { get; set; } = null!;
    }

    public class GeneratedLambdaExpr : GeneratedExpr
    {
        public GeneratedArguments Arguments { get; set; } = null!;
        public GeneratedExpr Body { get; set; } = null!;
    }

    public class GeneratedIfExpExpr : GeneratedExpr
    {
        public GeneratedExpr Test { get; set; } = null!;
        public GeneratedExpr Body { get; set; } = null!;
        public GeneratedExpr OrElse { get; set; } = null!;
    }

    public class GeneratedDictExpr : GeneratedExpr
    {
        public GeneratedExprSeq Keys { get; set; } = new();
        public GeneratedExprSeq Values { get; set; } = new();
    }

    public class GeneratedSetExpr : GeneratedExpr
    {
        public GeneratedExprSeq Elements { get; set; } = new();
    }

    public class GeneratedListCompExpr : GeneratedExpr
    {
        public GeneratedExpr Element { get; set; } = null!;
        public GeneratedComprehensionSeq Generators { get; set; } = new(); // comprehension*
    }

    public class GeneratedSetCompExpr : GeneratedExpr
    {
        public GeneratedExpr Element { get; set; } = null!;
        public GeneratedComprehensionSeq Generators { get; set; } = new();
    }

    public class GeneratedDictCompExpr : GeneratedExpr
    {
        public GeneratedExpr Key { get; set; } = null!;
        public GeneratedExpr Value { get; set; } = null!;
        public GeneratedComprehensionSeq Generators { get; set; } = new();
    }

    public class GeneratedGeneratorExpExpr : GeneratedExpr
    {
        public GeneratedExpr Element { get; set; } = null!;
        public GeneratedComprehensionSeq Generators { get; set; } = new();
    }

    public class GeneratedAwaitExpr : GeneratedExpr
    {
        public GeneratedExpr Value { get; set; } = null!;
    }

    public class GeneratedYieldExpr : GeneratedExpr
    {
        public GeneratedExpr? Value { get; set; }
    }

    public class GeneratedYieldFromExpr : GeneratedExpr
    {
        public GeneratedExpr Value { get; set; } = null!;
    }

    public class GeneratedCompareExpr : GeneratedExpr
    {
        public GeneratedExpr Left { get; set; } = null!;
        public List<string> Ops { get; set; } = new();
        public GeneratedExprSeq Comparators { get; set; } = new();
    }

    public class GeneratedCallExpr : GeneratedExpr
    {
        public GeneratedExpr Func { get; set; } = null!;
        public GeneratedExprSeq Args { get; set; } = new();
        public GeneratedKeywordSeq Keywords { get; set; } = new(); // keyword*
    }

    public class GeneratedFormattedValueExpr : GeneratedExpr
    {
        public GeneratedExpr Value { get; set; } = null!;
        public int Conversion { get; set; }
        public GeneratedExpr? FormatSpec { get; set; }
    }

    public class GeneratedJoinedStrExpr : GeneratedExpr
    {
        public GeneratedExprSeq Values { get; set; } = new();
    }

    /// <summary>
    /// CPython 3.12: Constant expression with PyObject value
    /// Avoids boxing/unboxing issues with object type
    /// </summary>
    public class GeneratedConstantExpr : GeneratedExpr
    {
        public PyObject Value { get; set; } = null!;
        public string? Kind { get; set; }
    }

    public class GeneratedAttributeExpr : GeneratedExpr
    {
        public GeneratedExpr Value { get; set; } = null!;
        public string Attr { get; set; } = "";
    }

    public class GeneratedSubscriptExpr : GeneratedExpr
    {
        public GeneratedExpr Value { get; set; } = null!;
        public GeneratedExpr Slice { get; set; } = null!;
    }

    public class GeneratedStarredExpr : GeneratedExpr
    {
        public GeneratedExpr Value { get; set; } = null!;
    }

    public class GeneratedNameExpr : GeneratedExpr
    {
        public string Id { get; set; } = "";
    }

    public class GeneratedListExpr : GeneratedExpr
    {
        public GeneratedExprSeq Elements { get; set; } = new();
    }

    public class GeneratedTupleExpr : GeneratedExpr
    {
        public GeneratedExprSeq Elements { get; set; } = new();
    }

    public class GeneratedSliceExpr : GeneratedExpr
    {
        public GeneratedExpr? Lower { get; set; }
        public GeneratedExpr? Upper { get; set; }
        public GeneratedExpr? Step { get; set; }
    }

    // ============================================================
    // Sequence Types
    // ============================================================

    /// <summary>
    /// Base generic sequence type for various list types in the parser
    /// CPython 3.12: asdl_seq* (generic sequence)
    /// </summary>
    public class GeneratedSeq : GeneratedPtr, IEnumerable<GeneratedAstNode>
    {
        private List<GeneratedAstNode> _items = new List<GeneratedAstNode>();
        public void Add(GeneratedAstNode item) => _items.Add(item);
        public void AddRange(IEnumerable<GeneratedAstNode> items) => _items.AddRange(items);
        public int Count => _items.Count;
        public GeneratedAstNode this[int index] { get => _items[index]; set => _items[index] = value; }
        public IEnumerator<GeneratedAstNode> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        public List<GeneratedAstNode> ToList() => _items;
        public static implicit operator List<GeneratedAstNode>(GeneratedSeq seq) => seq._items;
    }

    /// <summary>
    /// Sequence of base AST nodes - used for mixed or unknown AST node lists
    /// CPython 3.12: asdl_seq* is treated as a node in void* contexts
    /// Inherits from GeneratedAstNode so it can be stored alongside other AST nodes
    /// Wraps List functionality for sequence operations
    /// </summary>
    public class GeneratedAstNodeSeq : GeneratedAstNode
    {
        private List<GeneratedAstNode> _items = new List<GeneratedAstNode>();

        // List-like interface
        public void Add(GeneratedAstNode item) => _items.Add(item);
        public void AddRange(IEnumerable<GeneratedAstNode> items) => _items.AddRange(items);
        public int Count => _items.Count;
        public GeneratedAstNode this[int index]
        {
            get => _items[index];
            set => _items[index] = value;
        }
        public IEnumerator<GeneratedAstNode> GetEnumerator() => _items.GetEnumerator();
        public void Clear() => _items.Clear();
        public bool Contains(GeneratedAstNode item) => _items.Contains(item);
        public void RemoveAt(int index) => _items.RemoveAt(index);

        // Implicit conversion to/from List for compatibility
        public List<GeneratedAstNode> ToList() => _items;
        public static implicit operator List<GeneratedAstNode>(GeneratedAstNodeSeq seq) => seq._items;
    }

    public class GeneratedStmtSeq : GeneratedPtr, IEnumerable<GeneratedStmt>
    {
        private List<GeneratedStmt> _items = new List<GeneratedStmt>();
        public void Add(GeneratedStmt item) => _items.Add(item);
        public void AddRange(IEnumerable<GeneratedStmt> items) => _items.AddRange(items);
        public int Count => _items.Count;
        public GeneratedStmt this[int index] { get => _items[index]; set => _items[index] = value; }
        public IEnumerator<GeneratedStmt> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        public List<GeneratedStmt> ToList() => _items;
        public static implicit operator List<GeneratedStmt>(GeneratedStmtSeq seq) => seq._items;
    }

    public class GeneratedExprSeq : GeneratedPtr, IEnumerable<GeneratedExpr>
    {
        private List<GeneratedExpr> _items = new List<GeneratedExpr>();
        public void Add(GeneratedExpr item) => _items.Add(item);
        public void AddRange(IEnumerable<GeneratedExpr> items) => _items.AddRange(items);
        public int Count => _items.Count;
        public GeneratedExpr this[int index] { get => _items[index]; set => _items[index] = value; }
        public IEnumerator<GeneratedExpr> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        public List<GeneratedExpr> ToList() => _items;
        public static implicit operator List<GeneratedExpr>(GeneratedExprSeq seq) => seq._items;
    }

    public class GeneratedArgSeq : GeneratedPtr, IEnumerable<GeneratedArg>
    {
        private List<GeneratedArg> _items = new List<GeneratedArg>();
        public void Add(GeneratedArg item) => _items.Add(item);
        public void AddRange(IEnumerable<GeneratedArg> items) => _items.AddRange(items);
        public int Count => _items.Count;
        public GeneratedArg this[int index] { get => _items[index]; set => _items[index] = value; }
        public IEnumerator<GeneratedArg> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        public List<GeneratedArg> ToList() => _items;
        public static implicit operator List<GeneratedArg>(GeneratedArgSeq seq) => seq._items;
    }

    public class GeneratedPatternSeq : GeneratedPtr, IEnumerable<GeneratedPattern>
    {
        private List<GeneratedPattern> _items = new List<GeneratedPattern>();
        public void Add(GeneratedPattern item) => _items.Add(item);
        public void AddRange(IEnumerable<GeneratedPattern> items) => _items.AddRange(items);
        public int Count => _items.Count;
        public GeneratedPattern this[int index] { get => _items[index]; set => _items[index] = value; }
        public IEnumerator<GeneratedPattern> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        public List<GeneratedPattern> ToList() => _items;
        public static implicit operator List<GeneratedPattern>(GeneratedPatternSeq seq) => seq._items;
    }

    // ============================================================
    // Additional Typed Sequences (replacing List<object>)
    // ============================================================

    /// <summary>
    /// CPython 3.12: withitem = (expr context_expr, expr? optional_vars)
    /// </summary>
    public class GeneratedWithItem : GeneratedPtr
    {
        public GeneratedExpr ContextExpr { get; set; } = null!;
        public GeneratedExpr? OptionalVars { get; set; }
    }
    public class GeneratedWithItemSeq : GeneratedPtr, IEnumerable<GeneratedWithItem>
    {
        private List<GeneratedWithItem> _items = new List<GeneratedWithItem>();
        public void Add(GeneratedWithItem item) => _items.Add(item);
        public void AddRange(IEnumerable<GeneratedWithItem> items) => _items.AddRange(items);
        public int Count => _items.Count;
        public GeneratedWithItem this[int index] { get => _items[index]; set => _items[index] = value; }
        public IEnumerator<GeneratedWithItem> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        public List<GeneratedWithItem> ToList() => _items;
        public static implicit operator List<GeneratedWithItem>(GeneratedWithItemSeq seq) => seq._items;
    }

    /// <summary>
    /// CPython 3.12: KeywordOrStarred (union of keyword_ty and expr_ty for starred expressions)
    /// Used in function call arguments: f(a=1, *args, **kwargs)
    /// </summary>
    public class GeneratedKeywordOrStarred : GeneratedAstNode
    {
        public GeneratedKeyword? Keyword { get; set; } // For keyword arguments (a=1)
        public GeneratedExpr? StarredExpr { get; set; } // For starred expressions (*args)
        public bool IsKeyword { get; set; } // True if keyword, false if starred
    }
    public class GeneratedKeywordOrStarredSeq : GeneratedPtr, IEnumerable<GeneratedKeywordOrStarred>
    {
        private List<GeneratedKeywordOrStarred> _items = new List<GeneratedKeywordOrStarred>();
        public void Add(GeneratedKeywordOrStarred item) => _items.Add(item);
        public void AddRange(IEnumerable<GeneratedKeywordOrStarred> items) => _items.AddRange(items);
        public int Count => _items.Count;
        public GeneratedKeywordOrStarred this[int index] { get => _items[index]; set => _items[index] = value; }
        public IEnumerator<GeneratedKeywordOrStarred> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        public List<GeneratedKeywordOrStarred> ToList() => _items;
        public static implicit operator List<GeneratedKeywordOrStarred>(GeneratedKeywordOrStarredSeq seq) => seq._items;
    }

    /// <summary>
    /// CPython 3.12: match_case = (pattern pattern, expr? guard, stmt* body)
    /// </summary>
    public class GeneratedMatchCase : GeneratedPtr
    {
        public GeneratedPattern Pattern { get; set; } = null!;
        public GeneratedExpr? Guard { get; set; }
        public GeneratedStmtSeq Body { get; set; } = new();
    }
    public class GeneratedMatchCaseSeq : GeneratedPtr, IEnumerable<GeneratedMatchCase>
    {
        private List<GeneratedMatchCase> _items = new List<GeneratedMatchCase>();
        public void Add(GeneratedMatchCase item) => _items.Add(item);
        public void AddRange(IEnumerable<GeneratedMatchCase> items) => _items.AddRange(items);
        public int Count => _items.Count;
        public GeneratedMatchCase this[int index] { get => _items[index]; set => _items[index] = value; }
        public IEnumerator<GeneratedMatchCase> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        public List<GeneratedMatchCase> ToList() => _items;
        public static implicit operator List<GeneratedMatchCase>(GeneratedMatchCaseSeq seq) => seq._items;
    }

    /// <summary>
    /// CPython 3.12: pattern base type
    /// </summary>
    /// <summary>
    /// CPython 3.12: pattern_ty is a variant of AST nodes
    /// Patterns share the same base structure as expressions/statements
    /// </summary>
    public abstract class GeneratedPattern : GeneratedAstNode
    {
    }

    public class GeneratedMatchValue : GeneratedPattern
    {
        public GeneratedExpr Value { get; set; } = null!;
    }

    public class GeneratedMatchSingleton : GeneratedPattern
    {
        public object? Value { get; set; } // Py_None, Py_True, Py_False
    }

    public class GeneratedMatchSequence : GeneratedPattern
    {
        public List<GeneratedPattern> Patterns { get; set; } = new();
    }

    public class GeneratedMatchMapping : GeneratedPattern
    {
        public GeneratedExprSeq Keys { get; set; } = new();
        public List<GeneratedPattern> Patterns { get; set; } = new();
        public string? Rest { get; set; }
    }

    public class GeneratedMatchClass : GeneratedPattern
    {
        public GeneratedExpr Cls { get; set; } = null!;
        public List<GeneratedPattern> Patterns { get; set; } = new();
        public List<string> KwdAttrs { get; set; } = new();
        public List<GeneratedPattern> KwdPatterns { get; set; } = new();
    }

    public class GeneratedMatchStar : GeneratedPattern
    {
        public string? Name { get; set; }
    }

    public class GeneratedMatchAs : GeneratedPattern
    {
        public GeneratedPattern? Pattern { get; set; }
        public string? Name { get; set; }
    }

    public class GeneratedMatchOr : GeneratedPattern
    {
        public List<GeneratedPattern> Patterns { get; set; } = new();
    }

    /// <summary>
    /// CPython 3.12: excepthandler = ExceptHandler(expr? type, identifier? name, stmt* body)
    /// </summary>
    public abstract class GeneratedExceptHandler : GeneratedPtr
    {
        public int LineNo { get; set; }
        public int ColOffset { get; set; }
        public int EndLineNo { get; set; }
        public int EndColOffset { get; set; }
    }

    public class GeneratedExceptHandlerNode : GeneratedExceptHandler
    {
        public GeneratedExpr? Type { get; set; }
        public string? Name { get; set; }
        public GeneratedStmtSeq Body { get; set; } = new();
    }
    public class GeneratedExceptHandlerSeq : GeneratedPtr, IEnumerable<GeneratedExceptHandler>
    {
        private List<GeneratedExceptHandler> _items = new List<GeneratedExceptHandler>();
        public void Add(GeneratedExceptHandler item) => _items.Add(item);
        public void AddRange(IEnumerable<GeneratedExceptHandler> items) => _items.AddRange(items);
        public int Count => _items.Count;
        public GeneratedExceptHandler this[int index] { get => _items[index]; set => _items[index] = value; }
        public IEnumerator<GeneratedExceptHandler> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        public List<GeneratedExceptHandler> ToList() => _items;
        public static implicit operator List<GeneratedExceptHandler>(GeneratedExceptHandlerSeq seq) => seq._items;
    }

    /// <summary>
    /// CPython 3.12: type_param = TypeVar(identifier name, expr? bound)
    ///                          | ParamSpec(identifier name)
    ///                          | TypeVarTuple(identifier name)
    /// </summary>
    public abstract class GeneratedTypeParam : GeneratedPtr
    {
        public string Name { get; set; } = "";
        public int LineNo { get; set; }
        public int ColOffset { get; set; }
        public int EndLineNo { get; set; }
        public int EndColOffset { get; set; }
    }

    public class GeneratedTypeVar : GeneratedTypeParam
    {
        public GeneratedExpr? Bound { get; set; }
    }

    public class GeneratedParamSpec : GeneratedTypeParam { }

    public class GeneratedTypeVarTuple : GeneratedTypeParam { }

    public class GeneratedTypeParamSeq : GeneratedPtr, IEnumerable<GeneratedTypeParam>
    {
        private List<GeneratedTypeParam> _items = new List<GeneratedTypeParam>();
        public void Add(GeneratedTypeParam item) => _items.Add(item);
        public void AddRange(IEnumerable<GeneratedTypeParam> items) => _items.AddRange(items);
        public int Count => _items.Count;
        public GeneratedTypeParam this[int index] { get => _items[index]; set => _items[index] = value; }
        public IEnumerator<GeneratedTypeParam> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        public List<GeneratedTypeParam> ToList() => _items;
        public static implicit operator List<GeneratedTypeParam>(GeneratedTypeParamSeq seq) => seq._items;
    }

    /// <summary>
    /// CPython 3.12: alias = (identifier name, identifier? asname)
    /// Note: runtime/ast.cs has ImportAlias, but we create Generated version for consistency
    /// Inherits from GeneratedAstNode for type safety
    /// </summary>
    public class GeneratedAlias : GeneratedAstNode
    {
        public string Name { get; set; } = "";
        public string? AsName { get; set; }
        public int LineNo { get; set; }
        public int ColOffset { get; set; }
        public int EndLineNo { get; set; }
        public int EndColOffset { get; set; }
    }
    public class GeneratedAliasSeq : GeneratedPtr, IEnumerable<GeneratedAlias>
    {
        private List<GeneratedAlias> _items = new List<GeneratedAlias>();
        public void Add(GeneratedAlias item) => _items.Add(item);
        public void AddRange(IEnumerable<GeneratedAlias> items) => _items.AddRange(items);
        public int Count => _items.Count;
        public GeneratedAlias this[int index] { get => _items[index]; set => _items[index] = value; }
        public IEnumerator<GeneratedAlias> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        public List<GeneratedAlias> ToList() => _items;
        public static implicit operator List<GeneratedAlias>(GeneratedAliasSeq seq) => seq._items;
    }

    /// <summary>
    /// CPython 3.12: comprehension = (expr target, expr iter, expr* ifs, int is_async)
    /// Note: runtime/ast.cs has Comprehension, but we create Generated version for parser
    /// Inherits from GeneratedAstNode for type safety
    /// </summary>
    public class GeneratedComprehension : GeneratedAstNode
    {
        public GeneratedExpr Target { get; set; } = null!;
        public GeneratedExpr Iter { get; set; } = null!;
        public GeneratedExprSeq Ifs { get; set; } = new();
        public bool IsAsync { get; set; }
    }
    public class GeneratedComprehensionSeq : GeneratedPtr, IEnumerable<GeneratedComprehension>
    {
        private List<GeneratedComprehension> _items = new List<GeneratedComprehension>();
        public void Add(GeneratedComprehension item) => _items.Add(item);
        public void AddRange(IEnumerable<GeneratedComprehension> items) => _items.AddRange(items);
        public int Count => _items.Count;
        public GeneratedComprehension this[int index] { get => _items[index]; set => _items[index] = value; }
        public IEnumerator<GeneratedComprehension> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        public List<GeneratedComprehension> ToList() => _items;
        public static implicit operator List<GeneratedComprehension>(GeneratedComprehensionSeq seq) => seq._items;
    }

    /// <summary>
    /// CPython 3.12: keyword = (identifier? arg, expr value)
    /// Note: runtime/ast.cs has KeywordExpression, but we create Generated version for parser
    /// </summary>
    public class GeneratedKeyword : GeneratedPtr
    {
        public string? Arg { get; set; }  // None for **kwargs
        public GeneratedExpr Value { get; set; } = null!;
        public int LineNo { get; set; }
        public int ColOffset { get; set; }
        public int EndLineNo { get; set; }
        public int EndColOffset { get; set; }
    }
    public class GeneratedKeywordSeq : GeneratedPtr, IEnumerable<GeneratedKeyword>
    {
        private List<GeneratedKeyword> _items = new List<GeneratedKeyword>();
        public void Add(GeneratedKeyword item) => _items.Add(item);
        public void AddRange(IEnumerable<GeneratedKeyword> items) => _items.AddRange(items);
        public int Count => _items.Count;
        public GeneratedKeyword this[int index] { get => _items[index]; set => _items[index] = value; }
        public IEnumerator<GeneratedKeyword> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        public List<GeneratedKeyword> ToList() => _items;
        public static implicit operator List<GeneratedKeyword>(GeneratedKeywordSeq seq) => seq._items;
    }

    /// <summary>
    /// CPython 3.12: arg = (identifier arg, expr? annotation, string? type_comment)
    /// Note: runtime/FunctionArguments.cs has Arg, but we create Generated version for parser
    /// CPython 3.12: arg_ty is an AST node with position information (void* compatible)
    /// Inherits from GeneratedAstNode so it can be stored alongside other AST nodes
    /// </summary>
    public class GeneratedArg : GeneratedAstNode
    {
        public string Arg { get; set; } = "";
        public GeneratedExpr? Annotation { get; set; }
        public string? TypeComment { get; set; }
    }

    /// <summary>
    /// CPython 3.12: arguments = (arg* posonlyargs, arg* args, arg? vararg,
    ///                            arg* kwonlyargs, expr* kw_defaults, arg? kwarg, expr* defaults)
    /// Note: runtime/FunctionArguments.cs has similar, but we create Generated version for parser
    /// </summary>
    public class GeneratedArguments : GeneratedPtr
    {
        public List<GeneratedArg> PosOnlyArgs { get; set; } = new();
        public List<GeneratedArg> Args { get; set; } = new();
        public GeneratedArg? VarArg { get; set; }
        public List<GeneratedArg> KwOnlyArgs { get; set; } = new();
        public GeneratedExprSeq KwDefaults { get; set; } = new();
        public GeneratedArg? KwArg { get; set; }
        public GeneratedExprSeq Defaults { get; set; } = new();
    }

    // ============================================================
    // Module Type
    // ============================================================

    public class GeneratedModule : GeneratedAstNode
    {
        public GeneratedStmtSeq Body { get; set; } = new();
    }

    // ============================================================
    // Helper Types for Parameter Parsing
    // ============================================================

    /// <summary>
    /// CPython 3.12: SlashWithDefault - helper type for parsing function parameters with /
    /// Used in invalid_* rules for error detection
    /// Contains both params without defaults and params with defaults before /
    /// Inherits from GeneratedAstNode (not Seq) to be storable in sequences
    /// </summary>
    public class GeneratedSlashWithDefault : GeneratedAstNode
    {
        public GeneratedArgSeq? PlainArgs { get; set; }  // param_no_default*
        public List<(GeneratedArg, GeneratedExpr)>? ArgsWithDefaults { get; set; }  // param_with_default+
    }

    /// <summary>
    /// CPython 3.12: StarEtc - helper type for parsing *args and **kwargs
    /// Used in invalid_* rules for error detection
    /// Inherits from GeneratedAstNode (not Seq) to be storable in sequences
    /// </summary>
    public class GeneratedStarEtc : GeneratedAstNode
    {
        public GeneratedArg? VarArg { get; set; }  // *args
        public List<(GeneratedArg, GeneratedExpr?)>? KwOnlyArgs { get; set; }  // keyword-only args
        public GeneratedArg? KwArg { get; set; }  // **kwargs
    }
}
