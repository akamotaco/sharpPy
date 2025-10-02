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
    public abstract class GeneratedAstNode
    {
        public int LineNo { get; set; }
        public int ColOffset { get; set; }
        public int EndLineNo { get; set; }
        public int EndColOffset { get; set; }
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
        public object Arguments { get; set; } = null!; // arguments_ty
        public GeneratedStmtSeq Body { get; set; } = null!;
        public GeneratedExprSeq DecoratorList { get; set; } = new();
        public GeneratedExpr? Returns { get; set; }
        public string? TypeComment { get; set; }
        public object? TypeParams { get; set; } // type_params
    }

    public class GeneratedAsyncFunctionDefStmt : GeneratedStmt
    {
        public string Name { get; set; } = "";
        public object Arguments { get; set; } = null!;
        public GeneratedStmtSeq Body { get; set; } = null!;
        public GeneratedExprSeq DecoratorList { get; set; } = new();
        public GeneratedExpr? Returns { get; set; }
        public string? TypeComment { get; set; }
        public object? TypeParams { get; set; }
    }

    public class GeneratedClassDefStmt : GeneratedStmt
    {
        public string Name { get; set; } = "";
        public GeneratedExprSeq Bases { get; set; } = new();
        public GeneratedExprSeq Keywords { get; set; } = new();
        public GeneratedStmtSeq Body { get; set; } = null!;
        public GeneratedExprSeq DecoratorList { get; set; } = new();
        public object? TypeParams { get; set; }
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
        public List<object> Items { get; set; } = new(); // withitem*
        public GeneratedStmtSeq Body { get; set; } = null!;
        public string? TypeComment { get; set; }
    }

    public class GeneratedAsyncWithStmt : GeneratedStmt
    {
        public List<object> Items { get; set; } = new();
        public GeneratedStmtSeq Body { get; set; } = null!;
        public string? TypeComment { get; set; }
    }

    public class GeneratedMatchStmt : GeneratedStmt
    {
        public GeneratedExpr Subject { get; set; } = null!;
        public List<object> Cases { get; set; } = new(); // match_case*
    }

    public class GeneratedRaiseStmt : GeneratedStmt
    {
        public GeneratedExpr? Exc { get; set; }
        public GeneratedExpr? Cause { get; set; }
    }

    public class GeneratedTryStmt : GeneratedStmt
    {
        public GeneratedStmtSeq Body { get; set; } = null!;
        public List<object> Handlers { get; set; } = new(); // excepthandler*
        public GeneratedStmtSeq OrElse { get; set; } = new();
        public GeneratedStmtSeq FinallyBody { get; set; } = new();
    }

    public class GeneratedTryStarStmt : GeneratedStmt
    {
        public GeneratedStmtSeq Body { get; set; } = null!;
        public List<object> Handlers { get; set; } = new();
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
        public List<object> Names { get; set; } = new(); // alias*
    }

    public class GeneratedImportFromStmt : GeneratedStmt
    {
        public string? Module { get; set; }
        public List<object> Names { get; set; } = new();
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
        public object Arguments { get; set; } = null!;
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
        public List<object> Generators { get; set; } = new(); // comprehension*
    }

    public class GeneratedSetCompExpr : GeneratedExpr
    {
        public GeneratedExpr Element { get; set; } = null!;
        public List<object> Generators { get; set; } = new();
    }

    public class GeneratedDictCompExpr : GeneratedExpr
    {
        public GeneratedExpr Key { get; set; } = null!;
        public GeneratedExpr Value { get; set; } = null!;
        public List<object> Generators { get; set; } = new();
    }

    public class GeneratedGeneratorExpExpr : GeneratedExpr
    {
        public GeneratedExpr Element { get; set; } = null!;
        public List<object> Generators { get; set; } = new();
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
        public List<object> Keywords { get; set; } = new(); // keyword*
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
    public class GeneratedSeq : List<object> { }

    public class GeneratedStmtSeq : List<GeneratedStmt> { }
    public class GeneratedExprSeq : List<GeneratedExpr> { }

    // ============================================================
    // Module Type
    // ============================================================

    public class GeneratedModule : GeneratedAstNode
    {
        public GeneratedStmtSeq Body { get; set; } = new();
    }
}
