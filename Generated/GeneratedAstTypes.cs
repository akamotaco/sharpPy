// Generated AST Types from Python.asdl
// CPython 3.12 compatible - Auto-generated, DO NOT EDIT

using System;
using System.Collections.Generic;

namespace SharpPy.Generated
{
    // ============================================================
    // Base Types
    // ============================================================

    // GeneratedPtr is defined in GeneratedPtr.cs

    /// <summary>
    /// Base class for all AST nodes
    /// CPython 3.12: All AST nodes have lineno, col_offset attributes
    /// </summary>
    public abstract class GeneratedAstNode : GeneratedPtr
    {
        public int LineNo { get; set; }
        public int ColOffset { get; set; }
        public int EndLineNo { get; set; }
        public int EndColOffset { get; set; }
    }

    // ============================================================
    // mod types
    // ============================================================

    /// <summary>
    /// Base class for all mod nodes
    /// CPython 3.12: mod_ty
    /// </summary>
    public abstract class GeneratedMod : GeneratedAstNode { }

    public class GeneratedModule : GeneratedMod
    {
        public GeneratedStmtSeq Body { get; set; }
        public GeneratedTypeIgnoreSeq TypeIgnores { get; set; }
    }

    public class GeneratedInteractive : GeneratedMod
    {
        public GeneratedStmtSeq Body { get; set; }
    }

    public class GeneratedExpression : GeneratedMod
    {
        public GeneratedExpr Body { get; set; } = null!;
    }

    public class GeneratedFunctionType : GeneratedMod
    {
        public GeneratedExprSeq Argtypes { get; set; }
        public GeneratedExpr Returns { get; set; } = null!;
    }

    // ============================================================
    // stmt types
    // ============================================================

    /// <summary>
    /// Base class for all stmt nodes
    /// CPython 3.12: stmt_ty
    /// </summary>
    public abstract class GeneratedStmt : GeneratedAstNode { }

    public class GeneratedFunctionDef : GeneratedStmt
    {
        public string Name { get; set; } = "";
        public GeneratedArguments Args { get; set; } = null!;
        public GeneratedStmtSeq Body { get; set; }
        public GeneratedExprSeq DecoratorList { get; set; }
        public GeneratedExpr? Returns { get; set; }
        public string? TypeComment { get; set; }
        public GeneratedTypeParamSeq TypeParams { get; set; }
    }

    public class GeneratedAsyncFunctionDef : GeneratedStmt
    {
        public string Name { get; set; } = "";
        public GeneratedArguments Args { get; set; } = null!;
        public GeneratedStmtSeq Body { get; set; }
        public GeneratedExprSeq DecoratorList { get; set; }
        public GeneratedExpr? Returns { get; set; }
        public string? TypeComment { get; set; }
        public GeneratedTypeParamSeq TypeParams { get; set; }
    }

    public class GeneratedClassDef : GeneratedStmt
    {
        public string Name { get; set; } = "";
        public GeneratedExprSeq Bases { get; set; }
        public GeneratedKeywordSeq Keywords { get; set; }
        public GeneratedStmtSeq Body { get; set; }
        public GeneratedExprSeq DecoratorList { get; set; }
        public GeneratedTypeParamSeq TypeParams { get; set; }
    }

    public class GeneratedReturn : GeneratedStmt
    {
        public GeneratedExpr? Value { get; set; }
    }

    public class GeneratedDelete : GeneratedStmt
    {
        public GeneratedExprSeq Targets { get; set; }
    }

    public class GeneratedAssign : GeneratedStmt
    {
        public GeneratedExprSeq Targets { get; set; }
        public GeneratedExpr Value { get; set; } = null!;
        public string? TypeComment { get; set; }
    }

    public class GeneratedTypeAlias : GeneratedStmt
    {
        public GeneratedExpr Name { get; set; } = null!;
        public GeneratedTypeParamSeq TypeParams { get; set; }
        public GeneratedExpr Value { get; set; } = null!;
    }

    public class GeneratedAugAssign : GeneratedStmt
    {
        public GeneratedExpr Target { get; set; } = null!;
        public GeneratedOperator Op { get; set; } = null!;
        public GeneratedExpr Value { get; set; } = null!;
    }

    public class GeneratedAnnAssign : GeneratedStmt
    {
        public GeneratedExpr Target { get; set; } = null!;
        public GeneratedExpr Annotation { get; set; } = null!;
        public GeneratedExpr? Value { get; set; }
        public int Simple { get; set; } = 0;
    }

    public class GeneratedFor : GeneratedStmt
    {
        public GeneratedExpr Target { get; set; } = null!;
        public GeneratedExpr Iter { get; set; } = null!;
        public GeneratedStmtSeq Body { get; set; }
        public GeneratedStmtSeq Orelse { get; set; }
        public string? TypeComment { get; set; }
    }

    public class GeneratedAsyncFor : GeneratedStmt
    {
        public GeneratedExpr Target { get; set; } = null!;
        public GeneratedExpr Iter { get; set; } = null!;
        public GeneratedStmtSeq Body { get; set; }
        public GeneratedStmtSeq Orelse { get; set; }
        public string? TypeComment { get; set; }
    }

    public class GeneratedWhile : GeneratedStmt
    {
        public GeneratedExpr Test { get; set; } = null!;
        public GeneratedStmtSeq Body { get; set; }
        public GeneratedStmtSeq Orelse { get; set; }
    }

    public class GeneratedIf : GeneratedStmt
    {
        public GeneratedExpr Test { get; set; } = null!;
        public GeneratedStmtSeq Body { get; set; }
        public GeneratedStmtSeq Orelse { get; set; }
    }

    public class GeneratedWith : GeneratedStmt
    {
        public GeneratedWithitemSeq Items { get; set; }
        public GeneratedStmtSeq Body { get; set; }
        public string? TypeComment { get; set; }
    }

    public class GeneratedAsyncWith : GeneratedStmt
    {
        public GeneratedWithitemSeq Items { get; set; }
        public GeneratedStmtSeq Body { get; set; }
        public string? TypeComment { get; set; }
    }

    public class GeneratedMatch : GeneratedStmt
    {
        public GeneratedExpr Subject { get; set; } = null!;
        public GeneratedMatchCaseSeq Cases { get; set; }
    }

    public class GeneratedRaise : GeneratedStmt
    {
        public GeneratedExpr? Exc { get; set; }
        public GeneratedExpr? Cause { get; set; }
    }

    public class GeneratedTry : GeneratedStmt
    {
        public GeneratedStmtSeq Body { get; set; }
        public GeneratedExcepthandlerSeq Handlers { get; set; }
        public GeneratedStmtSeq Orelse { get; set; }
        public GeneratedStmtSeq Finalbody { get; set; }
    }

    public class GeneratedTryStar : GeneratedStmt
    {
        public GeneratedStmtSeq Body { get; set; }
        public GeneratedExcepthandlerSeq Handlers { get; set; }
        public GeneratedStmtSeq Orelse { get; set; }
        public GeneratedStmtSeq Finalbody { get; set; }
    }

    public class GeneratedAssert : GeneratedStmt
    {
        public GeneratedExpr Test { get; set; } = null!;
        public GeneratedExpr? Msg { get; set; }
    }

    public class GeneratedImport : GeneratedStmt
    {
        public GeneratedAliasSeq Names { get; set; }
    }

    public class GeneratedImportFrom : GeneratedStmt
    {
        public string? Module { get; set; }
        public GeneratedAliasSeq Names { get; set; }
        public int? Level { get; set; }
    }

    public class GeneratedGlobal : GeneratedStmt
    {
        public GeneratedIdentifierSeq Names { get; set; }
    }

    public class GeneratedNonlocal : GeneratedStmt
    {
        public GeneratedIdentifierSeq Names { get; set; }
    }

    public class GeneratedExprStmt : GeneratedStmt
    {
        public GeneratedExpr Value { get; set; } = null!;
    }

    /// <summary>
    /// Pass - Singleton pattern (no fields)
    /// </summary>
    public class GeneratedPass : GeneratedStmt
    {
        public static readonly GeneratedPass Instance = new();
        private GeneratedPass() { }
    }

    /// <summary>
    /// Break - Singleton pattern (no fields)
    /// </summary>
    public class GeneratedBreak : GeneratedStmt
    {
        public static readonly GeneratedBreak Instance = new();
        private GeneratedBreak() { }
    }

    /// <summary>
    /// Continue - Singleton pattern (no fields)
    /// </summary>
    public class GeneratedContinue : GeneratedStmt
    {
        public static readonly GeneratedContinue Instance = new();
        private GeneratedContinue() { }
    }

    // ============================================================
    // expr types
    // ============================================================

    /// <summary>
    /// Base class for all expr nodes
    /// CPython 3.12: expr_ty
    /// </summary>
    public abstract class GeneratedExpr : GeneratedAstNode { }

    public class GeneratedBoolOp : GeneratedExpr
    {
        public GeneratedBoolop Op { get; set; } = null!;
        public GeneratedExprSeq Values { get; set; }
    }

    public class GeneratedNamedExpr : GeneratedExpr
    {
        public GeneratedExpr Target { get; set; } = null!;
        public GeneratedExpr Value { get; set; } = null!;
    }

    public class GeneratedBinOp : GeneratedExpr
    {
        public GeneratedExpr Left { get; set; } = null!;
        public GeneratedOperator Op { get; set; } = null!;
        public GeneratedExpr Right { get; set; } = null!;
    }

    public class GeneratedUnaryOp : GeneratedExpr
    {
        public GeneratedUnaryop Op { get; set; } = null!;
        public GeneratedExpr Operand { get; set; } = null!;
    }

    public class GeneratedLambda : GeneratedExpr
    {
        public GeneratedArguments Args { get; set; } = null!;
        public GeneratedExpr Body { get; set; } = null!;
    }

    public class GeneratedIfExp : GeneratedExpr
    {
        public GeneratedExpr Test { get; set; } = null!;
        public GeneratedExpr Body { get; set; } = null!;
        public GeneratedExpr Orelse { get; set; } = null!;
    }

    public class GeneratedDict : GeneratedExpr
    {
        public GeneratedExprSeq Keys { get; set; }
        public GeneratedExprSeq Values { get; set; }
    }

    public class GeneratedSet : GeneratedExpr
    {
        public GeneratedExprSeq Elts { get; set; }
    }

    public class GeneratedListComp : GeneratedExpr
    {
        public GeneratedExpr Elt { get; set; } = null!;
        public GeneratedComprehensionSeq Generators { get; set; }
    }

    public class GeneratedSetComp : GeneratedExpr
    {
        public GeneratedExpr Elt { get; set; } = null!;
        public GeneratedComprehensionSeq Generators { get; set; }
    }

    public class GeneratedDictComp : GeneratedExpr
    {
        public GeneratedExpr Key { get; set; } = null!;
        public GeneratedExpr Value { get; set; } = null!;
        public GeneratedComprehensionSeq Generators { get; set; }
    }

    public class GeneratedGeneratorExp : GeneratedExpr
    {
        public GeneratedExpr Elt { get; set; } = null!;
        public GeneratedComprehensionSeq Generators { get; set; }
    }

    public class GeneratedAwait : GeneratedExpr
    {
        public GeneratedExpr Value { get; set; } = null!;
    }

    public class GeneratedYield : GeneratedExpr
    {
        public GeneratedExpr? Value { get; set; }
    }

    public class GeneratedYieldFrom : GeneratedExpr
    {
        public GeneratedExpr Value { get; set; } = null!;
    }

    public class GeneratedCompare : GeneratedExpr
    {
        public GeneratedExpr Left { get; set; } = null!;
        public GeneratedCmpopSeq Ops { get; set; }
        public GeneratedExprSeq Comparators { get; set; }
    }

    public class GeneratedCall : GeneratedExpr
    {
        public GeneratedExpr Func { get; set; } = null!;
        public GeneratedExprSeq Args { get; set; }
        public GeneratedKeywordSeq Keywords { get; set; }
    }

    public class GeneratedFormattedValue : GeneratedExpr
    {
        public GeneratedExpr Value { get; set; } = null!;
        public int Conversion { get; set; } = 0;
        public GeneratedExpr? FormatSpec { get; set; }
    }

    public class GeneratedJoinedStr : GeneratedExpr
    {
        public GeneratedExprSeq Values { get; set; }
    }

    public class GeneratedConstant : GeneratedExpr
    {
        public object Value { get; set; } = null!;
        public string? Kind { get; set; }
    }

    public class GeneratedAttribute : GeneratedExpr
    {
        public GeneratedExpr Value { get; set; } = null!;
        public string Attr { get; set; } = "";
        public GeneratedExprContext Ctx { get; set; } = null!;
    }

    public class GeneratedSubscript : GeneratedExpr
    {
        public GeneratedExpr Value { get; set; } = null!;
        public GeneratedExpr Slice { get; set; } = null!;
        public GeneratedExprContext Ctx { get; set; } = null!;
    }

    public class GeneratedStarred : GeneratedExpr
    {
        public GeneratedExpr Value { get; set; } = null!;
        public GeneratedExprContext Ctx { get; set; } = null!;
    }

    public class GeneratedName : GeneratedExpr
    {
        public string Id { get; set; } = "";
        public GeneratedExprContext Ctx { get; set; } = null!;
    }

    public class GeneratedList : GeneratedExpr
    {
        public GeneratedExprSeq Elts { get; set; }
        public GeneratedExprContext Ctx { get; set; } = null!;
    }

    public class GeneratedTuple : GeneratedExpr
    {
        public GeneratedExprSeq Elts { get; set; }
        public GeneratedExprContext Ctx { get; set; } = null!;
    }

    public class GeneratedSlice : GeneratedExpr
    {
        public GeneratedExpr? Lower { get; set; }
        public GeneratedExpr? Upper { get; set; }
        public GeneratedExpr? Step { get; set; }
    }

    // ============================================================
    // expr_context types
    // ============================================================

    /// <summary>
    /// Base class for all expr_context nodes
    /// CPython 3.12: expr_context_ty
    /// </summary>
    public abstract class GeneratedExprContext : GeneratedAstNode { }

    /// <summary>
    /// Load - Singleton pattern (no fields)
    /// </summary>
    public class GeneratedLoad : GeneratedExprContext
    {
        public static readonly GeneratedLoad Instance = new();
        private GeneratedLoad() { }
    }

    /// <summary>
    /// Store - Singleton pattern (no fields)
    /// </summary>
    public class GeneratedStore : GeneratedExprContext
    {
        public static readonly GeneratedStore Instance = new();
        private GeneratedStore() { }
    }

    /// <summary>
    /// Del - Singleton pattern (no fields)
    /// </summary>
    public class GeneratedDel : GeneratedExprContext
    {
        public static readonly GeneratedDel Instance = new();
        private GeneratedDel() { }
    }

    // ============================================================
    // boolop types
    // ============================================================

    /// <summary>
    /// Base class for all boolop nodes
    /// CPython 3.12: boolop_ty
    /// </summary>
    public abstract class GeneratedBoolop : GeneratedAstNode { }

    /// <summary>
    /// And - Singleton pattern (no fields)
    /// </summary>
    public class GeneratedAnd : GeneratedBoolop
    {
        public static readonly GeneratedAnd Instance = new();
        private GeneratedAnd() { }
    }

    /// <summary>
    /// Or - Singleton pattern (no fields)
    /// </summary>
    public class GeneratedOr : GeneratedBoolop
    {
        public static readonly GeneratedOr Instance = new();
        private GeneratedOr() { }
    }

    // ============================================================
    // operator types
    // ============================================================

    /// <summary>
    /// Base class for all operator nodes
    /// CPython 3.12: operator_ty
    /// </summary>
    public abstract class GeneratedOperator : GeneratedAstNode { }

    /// <summary>
    /// Add - Singleton pattern (no fields)
    /// </summary>
    public class GeneratedAdd : GeneratedOperator
    {
        public static readonly GeneratedAdd Instance = new();
        private GeneratedAdd() { }
    }

    /// <summary>
    /// Sub - Singleton pattern (no fields)
    /// </summary>
    public class GeneratedSub : GeneratedOperator
    {
        public static readonly GeneratedSub Instance = new();
        private GeneratedSub() { }
    }

    /// <summary>
    /// Mult - Singleton pattern (no fields)
    /// </summary>
    public class GeneratedMult : GeneratedOperator
    {
        public static readonly GeneratedMult Instance = new();
        private GeneratedMult() { }
    }

    /// <summary>
    /// MatMult - Singleton pattern (no fields)
    /// </summary>
    public class GeneratedMatMult : GeneratedOperator
    {
        public static readonly GeneratedMatMult Instance = new();
        private GeneratedMatMult() { }
    }

    /// <summary>
    /// Div - Singleton pattern (no fields)
    /// </summary>
    public class GeneratedDiv : GeneratedOperator
    {
        public static readonly GeneratedDiv Instance = new();
        private GeneratedDiv() { }
    }

    /// <summary>
    /// Mod - Singleton pattern (no fields)
    /// </summary>
    public class GeneratedMod_ : GeneratedOperator
    {
        public static readonly GeneratedMod_ Instance = new();
        private GeneratedMod_() { }
    }

    /// <summary>
    /// Pow - Singleton pattern (no fields)
    /// </summary>
    public class GeneratedPow : GeneratedOperator
    {
        public static readonly GeneratedPow Instance = new();
        private GeneratedPow() { }
    }

    /// <summary>
    /// LShift - Singleton pattern (no fields)
    /// </summary>
    public class GeneratedLShift : GeneratedOperator
    {
        public static readonly GeneratedLShift Instance = new();
        private GeneratedLShift() { }
    }

    /// <summary>
    /// RShift - Singleton pattern (no fields)
    /// </summary>
    public class GeneratedRShift : GeneratedOperator
    {
        public static readonly GeneratedRShift Instance = new();
        private GeneratedRShift() { }
    }

    /// <summary>
    /// BitOr - Singleton pattern (no fields)
    /// </summary>
    public class GeneratedBitOr : GeneratedOperator
    {
        public static readonly GeneratedBitOr Instance = new();
        private GeneratedBitOr() { }
    }

    /// <summary>
    /// BitXor - Singleton pattern (no fields)
    /// </summary>
    public class GeneratedBitXor : GeneratedOperator
    {
        public static readonly GeneratedBitXor Instance = new();
        private GeneratedBitXor() { }
    }

    /// <summary>
    /// BitAnd - Singleton pattern (no fields)
    /// </summary>
    public class GeneratedBitAnd : GeneratedOperator
    {
        public static readonly GeneratedBitAnd Instance = new();
        private GeneratedBitAnd() { }
    }

    /// <summary>
    /// FloorDiv - Singleton pattern (no fields)
    /// </summary>
    public class GeneratedFloorDiv : GeneratedOperator
    {
        public static readonly GeneratedFloorDiv Instance = new();
        private GeneratedFloorDiv() { }
    }

    // ============================================================
    // unaryop types
    // ============================================================

    /// <summary>
    /// Base class for all unaryop nodes
    /// CPython 3.12: unaryop_ty
    /// </summary>
    public abstract class GeneratedUnaryop : GeneratedAstNode { }

    /// <summary>
    /// Invert - Singleton pattern (no fields)
    /// </summary>
    public class GeneratedInvert : GeneratedUnaryop
    {
        public static readonly GeneratedInvert Instance = new();
        private GeneratedInvert() { }
    }

    /// <summary>
    /// Not - Singleton pattern (no fields)
    /// </summary>
    public class GeneratedNot : GeneratedUnaryop
    {
        public static readonly GeneratedNot Instance = new();
        private GeneratedNot() { }
    }

    /// <summary>
    /// UAdd - Singleton pattern (no fields)
    /// </summary>
    public class GeneratedUAdd : GeneratedUnaryop
    {
        public static readonly GeneratedUAdd Instance = new();
        private GeneratedUAdd() { }
    }

    /// <summary>
    /// USub - Singleton pattern (no fields)
    /// </summary>
    public class GeneratedUSub : GeneratedUnaryop
    {
        public static readonly GeneratedUSub Instance = new();
        private GeneratedUSub() { }
    }

    // ============================================================
    // cmpop types
    // ============================================================

    /// <summary>
    /// Base class for all cmpop nodes
    /// CPython 3.12: cmpop_ty
    /// </summary>
    public abstract class GeneratedCmpop : GeneratedAstNode { }

    /// <summary>
    /// Eq - Singleton pattern (no fields)
    /// </summary>
    public class GeneratedEq : GeneratedCmpop
    {
        public static readonly GeneratedEq Instance = new();
        private GeneratedEq() { }
    }

    /// <summary>
    /// NotEq - Singleton pattern (no fields)
    /// </summary>
    public class GeneratedNotEq : GeneratedCmpop
    {
        public static readonly GeneratedNotEq Instance = new();
        private GeneratedNotEq() { }
    }

    /// <summary>
    /// Lt - Singleton pattern (no fields)
    /// </summary>
    public class GeneratedLt : GeneratedCmpop
    {
        public static readonly GeneratedLt Instance = new();
        private GeneratedLt() { }
    }

    /// <summary>
    /// LtE - Singleton pattern (no fields)
    /// </summary>
    public class GeneratedLtE : GeneratedCmpop
    {
        public static readonly GeneratedLtE Instance = new();
        private GeneratedLtE() { }
    }

    /// <summary>
    /// Gt - Singleton pattern (no fields)
    /// </summary>
    public class GeneratedGt : GeneratedCmpop
    {
        public static readonly GeneratedGt Instance = new();
        private GeneratedGt() { }
    }

    /// <summary>
    /// GtE - Singleton pattern (no fields)
    /// </summary>
    public class GeneratedGtE : GeneratedCmpop
    {
        public static readonly GeneratedGtE Instance = new();
        private GeneratedGtE() { }
    }

    /// <summary>
    /// Is - Singleton pattern (no fields)
    /// </summary>
    public class GeneratedIs : GeneratedCmpop
    {
        public static readonly GeneratedIs Instance = new();
        private GeneratedIs() { }
    }

    /// <summary>
    /// IsNot - Singleton pattern (no fields)
    /// </summary>
    public class GeneratedIsNot : GeneratedCmpop
    {
        public static readonly GeneratedIsNot Instance = new();
        private GeneratedIsNot() { }
    }

    /// <summary>
    /// In - Singleton pattern (no fields)
    /// </summary>
    public class GeneratedIn : GeneratedCmpop
    {
        public static readonly GeneratedIn Instance = new();
        private GeneratedIn() { }
    }

    /// <summary>
    /// NotIn - Singleton pattern (no fields)
    /// </summary>
    public class GeneratedNotIn : GeneratedCmpop
    {
        public static readonly GeneratedNotIn Instance = new();
        private GeneratedNotIn() { }
    }

    // ============================================================
    // comprehension types
    // ============================================================

    public class GeneratedComprehension : GeneratedAstNode
    {
        public GeneratedExpr Target { get; set; } = null!;
        public GeneratedExpr Iter { get; set; } = null!;
        public GeneratedExprSeq Ifs { get; set; }
        public int IsAsync { get; set; } = 0;
    }

    // ============================================================
    // excepthandler types
    // ============================================================

    /// <summary>
    /// Base class for all excepthandler nodes
    /// CPython 3.12: excepthandler_ty
    /// </summary>
    public abstract class GeneratedExcepthandler : GeneratedAstNode { }

    public class GeneratedExceptHandler : GeneratedExcepthandler
    {
        public GeneratedExpr? Type { get; set; }
        public string? Name { get; set; }
        public GeneratedStmtSeq Body { get; set; }
    }

    // ============================================================
    // arguments types
    // ============================================================

    public class GeneratedArguments : GeneratedAstNode
    {
        public GeneratedArgSeq Posonlyargs { get; set; }
        public GeneratedArgSeq Args { get; set; }
        public GeneratedArg? Vararg { get; set; }
        public GeneratedArgSeq Kwonlyargs { get; set; }
        public GeneratedExprSeq KwDefaults { get; set; }
        public GeneratedArg? Kwarg { get; set; }
        public GeneratedExprSeq Defaults { get; set; }
    }

    // ============================================================
    // arg types
    // ============================================================

    public class GeneratedArg : GeneratedAstNode
    {
        public string Arg { get; set; } = "";
        public GeneratedExpr? Annotation { get; set; }
        public string? TypeComment { get; set; }
    }

    // ============================================================
    // keyword types
    // ============================================================

    public class GeneratedKeyword : GeneratedAstNode
    {
        public string? Arg { get; set; }
        public GeneratedExpr Value { get; set; } = null!;
    }

    // ============================================================
    // alias types
    // ============================================================

    public class GeneratedAlias : GeneratedAstNode
    {
        public string Name { get; set; } = "";
        public string? Asname { get; set; }
    }

    // ============================================================
    // withitem types
    // ============================================================

    public class GeneratedWithitem : GeneratedAstNode
    {
        public GeneratedExpr ContextExpr { get; set; } = null!;
        public GeneratedExpr? OptionalVars { get; set; }
    }

    // ============================================================
    // match_case types
    // ============================================================

    public class GeneratedMatchCase : GeneratedAstNode
    {
        public GeneratedPattern Pattern { get; set; } = null!;
        public GeneratedExpr? Guard { get; set; }
        public GeneratedStmtSeq Body { get; set; }
    }

    // ============================================================
    // pattern types
    // ============================================================

    /// <summary>
    /// Base class for all pattern nodes
    /// CPython 3.12: pattern_ty
    /// </summary>
    public abstract class GeneratedPattern : GeneratedAstNode { }

    public class GeneratedMatchValue : GeneratedPattern
    {
        public GeneratedExpr Value { get; set; } = null!;
    }

    public class GeneratedMatchSingleton : GeneratedPattern
    {
        public object Value { get; set; } = null!;
    }

    public class GeneratedMatchSequence : GeneratedPattern
    {
        public GeneratedPatternSeq Patterns { get; set; }
    }

    public class GeneratedMatchMapping : GeneratedPattern
    {
        public GeneratedExprSeq Keys { get; set; }
        public GeneratedPatternSeq Patterns { get; set; }
        public string? Rest { get; set; }
    }

    public class GeneratedMatchClass : GeneratedPattern
    {
        public GeneratedExpr Cls { get; set; } = null!;
        public GeneratedPatternSeq Patterns { get; set; }
        public GeneratedIdentifierSeq KwdAttrs { get; set; }
        public GeneratedPatternSeq KwdPatterns { get; set; }
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
        public GeneratedPatternSeq Patterns { get; set; }
    }

    // ============================================================
    // type_ignore types
    // ============================================================

    /// <summary>
    /// Base class for all type_ignore nodes
    /// CPython 3.12: type_ignore_ty
    /// </summary>
    public abstract class GeneratedTypeIgnore : GeneratedAstNode { }

    public class GeneratedTypeIgnoreNode : GeneratedTypeIgnore
    {
        public int Lineno { get; set; } = 0;
        public string Tag { get; set; } = "";
    }

    // ============================================================
    // type_param types
    // ============================================================

    /// <summary>
    /// Base class for all type_param nodes
    /// CPython 3.12: type_param_ty
    /// </summary>
    public abstract class GeneratedTypeParam : GeneratedAstNode { }

    public class GeneratedTypeVar : GeneratedTypeParam
    {
        public string Name { get; set; } = "";
        public GeneratedExpr? Bound { get; set; }
    }

    public class GeneratedParamSpec : GeneratedTypeParam
    {
        public string Name { get; set; } = "";
    }

    public class GeneratedTypeVarTuple : GeneratedTypeParam
    {
        public string Name { get; set; } = "";
    }

    // ============================================================
    // Sequence Types (GC optimized)
    // ============================================================

    /// <summary>
    /// Base class for all sequence types
    /// CPython 3.12: All asdl_seq types inherit from this for void* compatibility
    /// </summary>
    public abstract class GeneratedSeq : GeneratedPtr { }

    /// <summary>
    /// Sequence of alias - CPython: asdl_alias_seq
    /// C# GC optimized: List<T> with GeneratedSeq inheritance
    /// </summary>
    public class GeneratedAliasSeq : GeneratedSeq, System.Collections.Generic.IList<GeneratedAlias>
    {
        private readonly List<GeneratedAlias> _items = new();
        public static readonly GeneratedAliasSeq Empty = new();

        public GeneratedAliasSeq() { }
        public GeneratedAliasSeq(int capacity) { _items = new List<GeneratedAlias>(capacity); }
        public GeneratedAliasSeq(IEnumerable<GeneratedAlias> collection) { _items = new List<GeneratedAlias>(collection); }

        // IList<T> implementation
        public GeneratedAlias this[int index] { get => _items[index]; set => _items[index] = value; }
        public int Count => _items.Count;
        public bool IsReadOnly => false;
        public void Add(GeneratedAlias item) => _items.Add(item);
        public void Clear() => _items.Clear();
        public bool Contains(GeneratedAlias item) => _items.Contains(item);
        public void CopyTo(GeneratedAlias[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public System.Collections.Generic.IEnumerator<GeneratedAlias> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        public int IndexOf(GeneratedAlias item) => _items.IndexOf(item);
        public void Insert(int index, GeneratedAlias item) => _items.Insert(index, item);
        public bool Remove(GeneratedAlias item) => _items.Remove(item);
        public void RemoveAt(int index) => _items.RemoveAt(index);

        // Additional List<T> methods for compatibility
        public void AddRange(System.Collections.Generic.IEnumerable<GeneratedAlias> collection) => _items.AddRange(collection);
    }

    /// <summary>
    /// Sequence of arg - CPython: asdl_arg_seq
    /// C# GC optimized: List<T> with GeneratedSeq inheritance
    /// </summary>
    public class GeneratedArgSeq : GeneratedSeq, System.Collections.Generic.IList<GeneratedArg>
    {
        private readonly List<GeneratedArg> _items = new();
        public static readonly GeneratedArgSeq Empty = new();

        public GeneratedArgSeq() { }
        public GeneratedArgSeq(int capacity) { _items = new List<GeneratedArg>(capacity); }
        public GeneratedArgSeq(IEnumerable<GeneratedArg> collection) { _items = new List<GeneratedArg>(collection); }

        // IList<T> implementation
        public GeneratedArg this[int index] { get => _items[index]; set => _items[index] = value; }
        public int Count => _items.Count;
        public bool IsReadOnly => false;
        public void Add(GeneratedArg item) => _items.Add(item);
        public void Clear() => _items.Clear();
        public bool Contains(GeneratedArg item) => _items.Contains(item);
        public void CopyTo(GeneratedArg[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public System.Collections.Generic.IEnumerator<GeneratedArg> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        public int IndexOf(GeneratedArg item) => _items.IndexOf(item);
        public void Insert(int index, GeneratedArg item) => _items.Insert(index, item);
        public bool Remove(GeneratedArg item) => _items.Remove(item);
        public void RemoveAt(int index) => _items.RemoveAt(index);

        // Additional List<T> methods for compatibility
        public void AddRange(System.Collections.Generic.IEnumerable<GeneratedArg> collection) => _items.AddRange(collection);
    }

    /// <summary>
    /// Sequence of arguments - CPython: asdl_arguments_seq
    /// C# GC optimized: List<T> with GeneratedSeq inheritance
    /// </summary>
    public class GeneratedArgumentsSeq : GeneratedSeq, System.Collections.Generic.IList<GeneratedArguments>
    {
        private readonly List<GeneratedArguments> _items = new();
        public static readonly GeneratedArgumentsSeq Empty = new();

        public GeneratedArgumentsSeq() { }
        public GeneratedArgumentsSeq(int capacity) { _items = new List<GeneratedArguments>(capacity); }
        public GeneratedArgumentsSeq(IEnumerable<GeneratedArguments> collection) { _items = new List<GeneratedArguments>(collection); }

        // IList<T> implementation
        public GeneratedArguments this[int index] { get => _items[index]; set => _items[index] = value; }
        public int Count => _items.Count;
        public bool IsReadOnly => false;
        public void Add(GeneratedArguments item) => _items.Add(item);
        public void Clear() => _items.Clear();
        public bool Contains(GeneratedArguments item) => _items.Contains(item);
        public void CopyTo(GeneratedArguments[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public System.Collections.Generic.IEnumerator<GeneratedArguments> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        public int IndexOf(GeneratedArguments item) => _items.IndexOf(item);
        public void Insert(int index, GeneratedArguments item) => _items.Insert(index, item);
        public bool Remove(GeneratedArguments item) => _items.Remove(item);
        public void RemoveAt(int index) => _items.RemoveAt(index);

        // Additional List<T> methods for compatibility
        public void AddRange(System.Collections.Generic.IEnumerable<GeneratedArguments> collection) => _items.AddRange(collection);
    }

    /// <summary>
    /// Sequence of boolop - CPython: asdl_boolop_seq
    /// C# GC optimized: List<T> with GeneratedSeq inheritance
    /// </summary>
    public class GeneratedBoolopSeq : GeneratedSeq, System.Collections.Generic.IList<GeneratedBoolop>
    {
        private readonly List<GeneratedBoolop> _items = new();
        public static readonly GeneratedBoolopSeq Empty = new();

        public GeneratedBoolopSeq() { }
        public GeneratedBoolopSeq(int capacity) { _items = new List<GeneratedBoolop>(capacity); }
        public GeneratedBoolopSeq(IEnumerable<GeneratedBoolop> collection) { _items = new List<GeneratedBoolop>(collection); }

        // IList<T> implementation
        public GeneratedBoolop this[int index] { get => _items[index]; set => _items[index] = value; }
        public int Count => _items.Count;
        public bool IsReadOnly => false;
        public void Add(GeneratedBoolop item) => _items.Add(item);
        public void Clear() => _items.Clear();
        public bool Contains(GeneratedBoolop item) => _items.Contains(item);
        public void CopyTo(GeneratedBoolop[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public System.Collections.Generic.IEnumerator<GeneratedBoolop> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        public int IndexOf(GeneratedBoolop item) => _items.IndexOf(item);
        public void Insert(int index, GeneratedBoolop item) => _items.Insert(index, item);
        public bool Remove(GeneratedBoolop item) => _items.Remove(item);
        public void RemoveAt(int index) => _items.RemoveAt(index);

        // Additional List<T> methods for compatibility
        public void AddRange(System.Collections.Generic.IEnumerable<GeneratedBoolop> collection) => _items.AddRange(collection);
    }

    /// <summary>
    /// Sequence of cmpop - CPython: asdl_cmpop_seq
    /// C# GC optimized: List<T> with GeneratedSeq inheritance
    /// </summary>
    public class GeneratedCmpopSeq : GeneratedSeq, System.Collections.Generic.IList<GeneratedCmpop>
    {
        private readonly List<GeneratedCmpop> _items = new();
        public static readonly GeneratedCmpopSeq Empty = new();

        public GeneratedCmpopSeq() { }
        public GeneratedCmpopSeq(int capacity) { _items = new List<GeneratedCmpop>(capacity); }
        public GeneratedCmpopSeq(IEnumerable<GeneratedCmpop> collection) { _items = new List<GeneratedCmpop>(collection); }

        // IList<T> implementation
        public GeneratedCmpop this[int index] { get => _items[index]; set => _items[index] = value; }
        public int Count => _items.Count;
        public bool IsReadOnly => false;
        public void Add(GeneratedCmpop item) => _items.Add(item);
        public void Clear() => _items.Clear();
        public bool Contains(GeneratedCmpop item) => _items.Contains(item);
        public void CopyTo(GeneratedCmpop[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public System.Collections.Generic.IEnumerator<GeneratedCmpop> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        public int IndexOf(GeneratedCmpop item) => _items.IndexOf(item);
        public void Insert(int index, GeneratedCmpop item) => _items.Insert(index, item);
        public bool Remove(GeneratedCmpop item) => _items.Remove(item);
        public void RemoveAt(int index) => _items.RemoveAt(index);

        // Additional List<T> methods for compatibility
        public void AddRange(System.Collections.Generic.IEnumerable<GeneratedCmpop> collection) => _items.AddRange(collection);
    }

    /// <summary>
    /// Sequence of comprehension - CPython: asdl_comprehension_seq
    /// C# GC optimized: List<T> with GeneratedSeq inheritance
    /// </summary>
    public class GeneratedComprehensionSeq : GeneratedSeq, System.Collections.Generic.IList<GeneratedComprehension>
    {
        private readonly List<GeneratedComprehension> _items = new();
        public static readonly GeneratedComprehensionSeq Empty = new();

        public GeneratedComprehensionSeq() { }
        public GeneratedComprehensionSeq(int capacity) { _items = new List<GeneratedComprehension>(capacity); }
        public GeneratedComprehensionSeq(IEnumerable<GeneratedComprehension> collection) { _items = new List<GeneratedComprehension>(collection); }

        // IList<T> implementation
        public GeneratedComprehension this[int index] { get => _items[index]; set => _items[index] = value; }
        public int Count => _items.Count;
        public bool IsReadOnly => false;
        public void Add(GeneratedComprehension item) => _items.Add(item);
        public void Clear() => _items.Clear();
        public bool Contains(GeneratedComprehension item) => _items.Contains(item);
        public void CopyTo(GeneratedComprehension[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public System.Collections.Generic.IEnumerator<GeneratedComprehension> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        public int IndexOf(GeneratedComprehension item) => _items.IndexOf(item);
        public void Insert(int index, GeneratedComprehension item) => _items.Insert(index, item);
        public bool Remove(GeneratedComprehension item) => _items.Remove(item);
        public void RemoveAt(int index) => _items.RemoveAt(index);

        // Additional List<T> methods for compatibility
        public void AddRange(System.Collections.Generic.IEnumerable<GeneratedComprehension> collection) => _items.AddRange(collection);
    }

    /// <summary>
    /// Sequence of excepthandler - CPython: asdl_excepthandler_seq
    /// C# GC optimized: List<T> with GeneratedSeq inheritance
    /// </summary>
    public class GeneratedExcepthandlerSeq : GeneratedSeq, System.Collections.Generic.IList<GeneratedExcepthandler>
    {
        private readonly List<GeneratedExcepthandler> _items = new();
        public static readonly GeneratedExcepthandlerSeq Empty = new();

        public GeneratedExcepthandlerSeq() { }
        public GeneratedExcepthandlerSeq(int capacity) { _items = new List<GeneratedExcepthandler>(capacity); }
        public GeneratedExcepthandlerSeq(IEnumerable<GeneratedExcepthandler> collection) { _items = new List<GeneratedExcepthandler>(collection); }

        // IList<T> implementation
        public GeneratedExcepthandler this[int index] { get => _items[index]; set => _items[index] = value; }
        public int Count => _items.Count;
        public bool IsReadOnly => false;
        public void Add(GeneratedExcepthandler item) => _items.Add(item);
        public void Clear() => _items.Clear();
        public bool Contains(GeneratedExcepthandler item) => _items.Contains(item);
        public void CopyTo(GeneratedExcepthandler[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public System.Collections.Generic.IEnumerator<GeneratedExcepthandler> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        public int IndexOf(GeneratedExcepthandler item) => _items.IndexOf(item);
        public void Insert(int index, GeneratedExcepthandler item) => _items.Insert(index, item);
        public bool Remove(GeneratedExcepthandler item) => _items.Remove(item);
        public void RemoveAt(int index) => _items.RemoveAt(index);

        // Additional List<T> methods for compatibility
        public void AddRange(System.Collections.Generic.IEnumerable<GeneratedExcepthandler> collection) => _items.AddRange(collection);
    }

    /// <summary>
    /// Sequence of expr - CPython: asdl_expr_seq
    /// C# GC optimized: List<T> with GeneratedSeq inheritance
    /// </summary>
    public class GeneratedExprSeq : GeneratedSeq, System.Collections.Generic.IList<GeneratedExpr>
    {
        private readonly List<GeneratedExpr> _items = new();
        public static readonly GeneratedExprSeq Empty = new();

        public GeneratedExprSeq() { }
        public GeneratedExprSeq(int capacity) { _items = new List<GeneratedExpr>(capacity); }
        public GeneratedExprSeq(IEnumerable<GeneratedExpr> collection) { _items = new List<GeneratedExpr>(collection); }

        // IList<T> implementation
        public GeneratedExpr this[int index] { get => _items[index]; set => _items[index] = value; }
        public int Count => _items.Count;
        public bool IsReadOnly => false;
        public void Add(GeneratedExpr item) => _items.Add(item);
        public void Clear() => _items.Clear();
        public bool Contains(GeneratedExpr item) => _items.Contains(item);
        public void CopyTo(GeneratedExpr[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public System.Collections.Generic.IEnumerator<GeneratedExpr> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        public int IndexOf(GeneratedExpr item) => _items.IndexOf(item);
        public void Insert(int index, GeneratedExpr item) => _items.Insert(index, item);
        public bool Remove(GeneratedExpr item) => _items.Remove(item);
        public void RemoveAt(int index) => _items.RemoveAt(index);

        // Additional List<T> methods for compatibility
        public void AddRange(System.Collections.Generic.IEnumerable<GeneratedExpr> collection) => _items.AddRange(collection);
    }

    /// <summary>
    /// Sequence of expr_context - CPython: asdl_expr_context_seq
    /// C# GC optimized: List<T> with GeneratedSeq inheritance
    /// </summary>
    public class GeneratedExprContextSeq : GeneratedSeq, System.Collections.Generic.IList<GeneratedExprContext>
    {
        private readonly List<GeneratedExprContext> _items = new();
        public static readonly GeneratedExprContextSeq Empty = new();

        public GeneratedExprContextSeq() { }
        public GeneratedExprContextSeq(int capacity) { _items = new List<GeneratedExprContext>(capacity); }
        public GeneratedExprContextSeq(IEnumerable<GeneratedExprContext> collection) { _items = new List<GeneratedExprContext>(collection); }

        // IList<T> implementation
        public GeneratedExprContext this[int index] { get => _items[index]; set => _items[index] = value; }
        public int Count => _items.Count;
        public bool IsReadOnly => false;
        public void Add(GeneratedExprContext item) => _items.Add(item);
        public void Clear() => _items.Clear();
        public bool Contains(GeneratedExprContext item) => _items.Contains(item);
        public void CopyTo(GeneratedExprContext[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public System.Collections.Generic.IEnumerator<GeneratedExprContext> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        public int IndexOf(GeneratedExprContext item) => _items.IndexOf(item);
        public void Insert(int index, GeneratedExprContext item) => _items.Insert(index, item);
        public bool Remove(GeneratedExprContext item) => _items.Remove(item);
        public void RemoveAt(int index) => _items.RemoveAt(index);

        // Additional List<T> methods for compatibility
        public void AddRange(System.Collections.Generic.IEnumerable<GeneratedExprContext> collection) => _items.AddRange(collection);
    }

    /// <summary>
    /// Sequence of identifier - CPython: asdl_identifier_seq
    /// C# GC optimized: List<T> with GeneratedSeq inheritance
    /// </summary>
    public class GeneratedIdentifierSeq : GeneratedSeq, System.Collections.Generic.IList<string>
    {
        private readonly List<string> _items = new();
        public static readonly GeneratedIdentifierSeq Empty = new();

        public GeneratedIdentifierSeq() { }
        public GeneratedIdentifierSeq(int capacity) { _items = new List<string>(capacity); }
        public GeneratedIdentifierSeq(IEnumerable<string> collection) { _items = new List<string>(collection); }

        // IList<T> implementation
        public string this[int index] { get => _items[index]; set => _items[index] = value; }
        public int Count => _items.Count;
        public bool IsReadOnly => false;
        public void Add(string item) => _items.Add(item);
        public void Clear() => _items.Clear();
        public bool Contains(string item) => _items.Contains(item);
        public void CopyTo(string[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public System.Collections.Generic.IEnumerator<string> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        public int IndexOf(string item) => _items.IndexOf(item);
        public void Insert(int index, string item) => _items.Insert(index, item);
        public bool Remove(string item) => _items.Remove(item);
        public void RemoveAt(int index) => _items.RemoveAt(index);

        // Additional List<T> methods for compatibility
        public void AddRange(System.Collections.Generic.IEnumerable<string> collection) => _items.AddRange(collection);
    }

    /// <summary>
    /// Sequence of keyword - CPython: asdl_keyword_seq
    /// C# GC optimized: List<T> with GeneratedSeq inheritance
    /// </summary>
    public class GeneratedKeywordSeq : GeneratedSeq, System.Collections.Generic.IList<GeneratedKeyword>
    {
        private readonly List<GeneratedKeyword> _items = new();
        public static readonly GeneratedKeywordSeq Empty = new();

        public GeneratedKeywordSeq() { }
        public GeneratedKeywordSeq(int capacity) { _items = new List<GeneratedKeyword>(capacity); }
        public GeneratedKeywordSeq(IEnumerable<GeneratedKeyword> collection) { _items = new List<GeneratedKeyword>(collection); }

        // IList<T> implementation
        public GeneratedKeyword this[int index] { get => _items[index]; set => _items[index] = value; }
        public int Count => _items.Count;
        public bool IsReadOnly => false;
        public void Add(GeneratedKeyword item) => _items.Add(item);
        public void Clear() => _items.Clear();
        public bool Contains(GeneratedKeyword item) => _items.Contains(item);
        public void CopyTo(GeneratedKeyword[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public System.Collections.Generic.IEnumerator<GeneratedKeyword> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        public int IndexOf(GeneratedKeyword item) => _items.IndexOf(item);
        public void Insert(int index, GeneratedKeyword item) => _items.Insert(index, item);
        public bool Remove(GeneratedKeyword item) => _items.Remove(item);
        public void RemoveAt(int index) => _items.RemoveAt(index);

        // Additional List<T> methods for compatibility
        public void AddRange(System.Collections.Generic.IEnumerable<GeneratedKeyword> collection) => _items.AddRange(collection);
    }

    /// <summary>
    /// Sequence of match_case - CPython: asdl_match_case_seq
    /// C# GC optimized: List<T> with GeneratedSeq inheritance
    /// </summary>
    public class GeneratedMatchCaseSeq : GeneratedSeq, System.Collections.Generic.IList<GeneratedMatchCase>
    {
        private readonly List<GeneratedMatchCase> _items = new();
        public static readonly GeneratedMatchCaseSeq Empty = new();

        public GeneratedMatchCaseSeq() { }
        public GeneratedMatchCaseSeq(int capacity) { _items = new List<GeneratedMatchCase>(capacity); }
        public GeneratedMatchCaseSeq(IEnumerable<GeneratedMatchCase> collection) { _items = new List<GeneratedMatchCase>(collection); }

        // IList<T> implementation
        public GeneratedMatchCase this[int index] { get => _items[index]; set => _items[index] = value; }
        public int Count => _items.Count;
        public bool IsReadOnly => false;
        public void Add(GeneratedMatchCase item) => _items.Add(item);
        public void Clear() => _items.Clear();
        public bool Contains(GeneratedMatchCase item) => _items.Contains(item);
        public void CopyTo(GeneratedMatchCase[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public System.Collections.Generic.IEnumerator<GeneratedMatchCase> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        public int IndexOf(GeneratedMatchCase item) => _items.IndexOf(item);
        public void Insert(int index, GeneratedMatchCase item) => _items.Insert(index, item);
        public bool Remove(GeneratedMatchCase item) => _items.Remove(item);
        public void RemoveAt(int index) => _items.RemoveAt(index);

        // Additional List<T> methods for compatibility
        public void AddRange(System.Collections.Generic.IEnumerable<GeneratedMatchCase> collection) => _items.AddRange(collection);
    }

    /// <summary>
    /// Sequence of mod - CPython: asdl_mod_seq
    /// C# GC optimized: List<T> with GeneratedSeq inheritance
    /// </summary>
    public class GeneratedModSeq : GeneratedSeq, System.Collections.Generic.IList<GeneratedMod>
    {
        private readonly List<GeneratedMod> _items = new();
        public static readonly GeneratedModSeq Empty = new();

        public GeneratedModSeq() { }
        public GeneratedModSeq(int capacity) { _items = new List<GeneratedMod>(capacity); }
        public GeneratedModSeq(IEnumerable<GeneratedMod> collection) { _items = new List<GeneratedMod>(collection); }

        // IList<T> implementation
        public GeneratedMod this[int index] { get => _items[index]; set => _items[index] = value; }
        public int Count => _items.Count;
        public bool IsReadOnly => false;
        public void Add(GeneratedMod item) => _items.Add(item);
        public void Clear() => _items.Clear();
        public bool Contains(GeneratedMod item) => _items.Contains(item);
        public void CopyTo(GeneratedMod[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public System.Collections.Generic.IEnumerator<GeneratedMod> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        public int IndexOf(GeneratedMod item) => _items.IndexOf(item);
        public void Insert(int index, GeneratedMod item) => _items.Insert(index, item);
        public bool Remove(GeneratedMod item) => _items.Remove(item);
        public void RemoveAt(int index) => _items.RemoveAt(index);

        // Additional List<T> methods for compatibility
        public void AddRange(System.Collections.Generic.IEnumerable<GeneratedMod> collection) => _items.AddRange(collection);
    }

    /// <summary>
    /// Sequence of operator - CPython: asdl_operator_seq
    /// C# GC optimized: List<T> with GeneratedSeq inheritance
    /// </summary>
    public class GeneratedOperatorSeq : GeneratedSeq, System.Collections.Generic.IList<GeneratedOperator>
    {
        private readonly List<GeneratedOperator> _items = new();
        public static readonly GeneratedOperatorSeq Empty = new();

        public GeneratedOperatorSeq() { }
        public GeneratedOperatorSeq(int capacity) { _items = new List<GeneratedOperator>(capacity); }
        public GeneratedOperatorSeq(IEnumerable<GeneratedOperator> collection) { _items = new List<GeneratedOperator>(collection); }

        // IList<T> implementation
        public GeneratedOperator this[int index] { get => _items[index]; set => _items[index] = value; }
        public int Count => _items.Count;
        public bool IsReadOnly => false;
        public void Add(GeneratedOperator item) => _items.Add(item);
        public void Clear() => _items.Clear();
        public bool Contains(GeneratedOperator item) => _items.Contains(item);
        public void CopyTo(GeneratedOperator[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public System.Collections.Generic.IEnumerator<GeneratedOperator> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        public int IndexOf(GeneratedOperator item) => _items.IndexOf(item);
        public void Insert(int index, GeneratedOperator item) => _items.Insert(index, item);
        public bool Remove(GeneratedOperator item) => _items.Remove(item);
        public void RemoveAt(int index) => _items.RemoveAt(index);

        // Additional List<T> methods for compatibility
        public void AddRange(System.Collections.Generic.IEnumerable<GeneratedOperator> collection) => _items.AddRange(collection);
    }

    /// <summary>
    /// Sequence of pattern - CPython: asdl_pattern_seq
    /// C# GC optimized: List<T> with GeneratedSeq inheritance
    /// </summary>
    public class GeneratedPatternSeq : GeneratedSeq, System.Collections.Generic.IList<GeneratedPattern>
    {
        private readonly List<GeneratedPattern> _items = new();
        public static readonly GeneratedPatternSeq Empty = new();

        public GeneratedPatternSeq() { }
        public GeneratedPatternSeq(int capacity) { _items = new List<GeneratedPattern>(capacity); }
        public GeneratedPatternSeq(IEnumerable<GeneratedPattern> collection) { _items = new List<GeneratedPattern>(collection); }

        // IList<T> implementation
        public GeneratedPattern this[int index] { get => _items[index]; set => _items[index] = value; }
        public int Count => _items.Count;
        public bool IsReadOnly => false;
        public void Add(GeneratedPattern item) => _items.Add(item);
        public void Clear() => _items.Clear();
        public bool Contains(GeneratedPattern item) => _items.Contains(item);
        public void CopyTo(GeneratedPattern[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public System.Collections.Generic.IEnumerator<GeneratedPattern> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        public int IndexOf(GeneratedPattern item) => _items.IndexOf(item);
        public void Insert(int index, GeneratedPattern item) => _items.Insert(index, item);
        public bool Remove(GeneratedPattern item) => _items.Remove(item);
        public void RemoveAt(int index) => _items.RemoveAt(index);

        // Additional List<T> methods for compatibility
        public void AddRange(System.Collections.Generic.IEnumerable<GeneratedPattern> collection) => _items.AddRange(collection);
    }

    /// <summary>
    /// Sequence of stmt - CPython: asdl_stmt_seq
    /// C# GC optimized: List<T> with GeneratedSeq inheritance
    /// </summary>
    public class GeneratedStmtSeq : GeneratedSeq, System.Collections.Generic.IList<GeneratedStmt>
    {
        private readonly List<GeneratedStmt> _items = new();
        public static readonly GeneratedStmtSeq Empty = new();

        public GeneratedStmtSeq() { }
        public GeneratedStmtSeq(int capacity) { _items = new List<GeneratedStmt>(capacity); }
        public GeneratedStmtSeq(IEnumerable<GeneratedStmt> collection) { _items = new List<GeneratedStmt>(collection); }

        // IList<T> implementation
        public GeneratedStmt this[int index] { get => _items[index]; set => _items[index] = value; }
        public int Count => _items.Count;
        public bool IsReadOnly => false;
        public void Add(GeneratedStmt item) => _items.Add(item);
        public void Clear() => _items.Clear();
        public bool Contains(GeneratedStmt item) => _items.Contains(item);
        public void CopyTo(GeneratedStmt[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public System.Collections.Generic.IEnumerator<GeneratedStmt> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        public int IndexOf(GeneratedStmt item) => _items.IndexOf(item);
        public void Insert(int index, GeneratedStmt item) => _items.Insert(index, item);
        public bool Remove(GeneratedStmt item) => _items.Remove(item);
        public void RemoveAt(int index) => _items.RemoveAt(index);

        // Additional List<T> methods for compatibility
        public void AddRange(System.Collections.Generic.IEnumerable<GeneratedStmt> collection) => _items.AddRange(collection);
    }

    /// <summary>
    /// Sequence of type_ignore - CPython: asdl_type_ignore_seq
    /// C# GC optimized: List<T> with GeneratedSeq inheritance
    /// </summary>
    public class GeneratedTypeIgnoreSeq : GeneratedSeq, System.Collections.Generic.IList<GeneratedTypeIgnore>
    {
        private readonly List<GeneratedTypeIgnore> _items = new();
        public static readonly GeneratedTypeIgnoreSeq Empty = new();

        public GeneratedTypeIgnoreSeq() { }
        public GeneratedTypeIgnoreSeq(int capacity) { _items = new List<GeneratedTypeIgnore>(capacity); }
        public GeneratedTypeIgnoreSeq(IEnumerable<GeneratedTypeIgnore> collection) { _items = new List<GeneratedTypeIgnore>(collection); }

        // IList<T> implementation
        public GeneratedTypeIgnore this[int index] { get => _items[index]; set => _items[index] = value; }
        public int Count => _items.Count;
        public bool IsReadOnly => false;
        public void Add(GeneratedTypeIgnore item) => _items.Add(item);
        public void Clear() => _items.Clear();
        public bool Contains(GeneratedTypeIgnore item) => _items.Contains(item);
        public void CopyTo(GeneratedTypeIgnore[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public System.Collections.Generic.IEnumerator<GeneratedTypeIgnore> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        public int IndexOf(GeneratedTypeIgnore item) => _items.IndexOf(item);
        public void Insert(int index, GeneratedTypeIgnore item) => _items.Insert(index, item);
        public bool Remove(GeneratedTypeIgnore item) => _items.Remove(item);
        public void RemoveAt(int index) => _items.RemoveAt(index);

        // Additional List<T> methods for compatibility
        public void AddRange(System.Collections.Generic.IEnumerable<GeneratedTypeIgnore> collection) => _items.AddRange(collection);
    }

    /// <summary>
    /// Sequence of type_param - CPython: asdl_type_param_seq
    /// C# GC optimized: List<T> with GeneratedSeq inheritance
    /// </summary>
    public class GeneratedTypeParamSeq : GeneratedSeq, System.Collections.Generic.IList<GeneratedTypeParam>
    {
        private readonly List<GeneratedTypeParam> _items = new();
        public static readonly GeneratedTypeParamSeq Empty = new();

        public GeneratedTypeParamSeq() { }
        public GeneratedTypeParamSeq(int capacity) { _items = new List<GeneratedTypeParam>(capacity); }
        public GeneratedTypeParamSeq(IEnumerable<GeneratedTypeParam> collection) { _items = new List<GeneratedTypeParam>(collection); }

        // IList<T> implementation
        public GeneratedTypeParam this[int index] { get => _items[index]; set => _items[index] = value; }
        public int Count => _items.Count;
        public bool IsReadOnly => false;
        public void Add(GeneratedTypeParam item) => _items.Add(item);
        public void Clear() => _items.Clear();
        public bool Contains(GeneratedTypeParam item) => _items.Contains(item);
        public void CopyTo(GeneratedTypeParam[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public System.Collections.Generic.IEnumerator<GeneratedTypeParam> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        public int IndexOf(GeneratedTypeParam item) => _items.IndexOf(item);
        public void Insert(int index, GeneratedTypeParam item) => _items.Insert(index, item);
        public bool Remove(GeneratedTypeParam item) => _items.Remove(item);
        public void RemoveAt(int index) => _items.RemoveAt(index);

        // Additional List<T> methods for compatibility
        public void AddRange(System.Collections.Generic.IEnumerable<GeneratedTypeParam> collection) => _items.AddRange(collection);
    }

    /// <summary>
    /// Sequence of unaryop - CPython: asdl_unaryop_seq
    /// C# GC optimized: List<T> with GeneratedSeq inheritance
    /// </summary>
    public class GeneratedUnaryopSeq : GeneratedSeq, System.Collections.Generic.IList<GeneratedUnaryop>
    {
        private readonly List<GeneratedUnaryop> _items = new();
        public static readonly GeneratedUnaryopSeq Empty = new();

        public GeneratedUnaryopSeq() { }
        public GeneratedUnaryopSeq(int capacity) { _items = new List<GeneratedUnaryop>(capacity); }
        public GeneratedUnaryopSeq(IEnumerable<GeneratedUnaryop> collection) { _items = new List<GeneratedUnaryop>(collection); }

        // IList<T> implementation
        public GeneratedUnaryop this[int index] { get => _items[index]; set => _items[index] = value; }
        public int Count => _items.Count;
        public bool IsReadOnly => false;
        public void Add(GeneratedUnaryop item) => _items.Add(item);
        public void Clear() => _items.Clear();
        public bool Contains(GeneratedUnaryop item) => _items.Contains(item);
        public void CopyTo(GeneratedUnaryop[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public System.Collections.Generic.IEnumerator<GeneratedUnaryop> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        public int IndexOf(GeneratedUnaryop item) => _items.IndexOf(item);
        public void Insert(int index, GeneratedUnaryop item) => _items.Insert(index, item);
        public bool Remove(GeneratedUnaryop item) => _items.Remove(item);
        public void RemoveAt(int index) => _items.RemoveAt(index);

        // Additional List<T> methods for compatibility
        public void AddRange(System.Collections.Generic.IEnumerable<GeneratedUnaryop> collection) => _items.AddRange(collection);
    }

    /// <summary>
    /// Sequence of withitem - CPython: asdl_withitem_seq
    /// C# GC optimized: List<T> with GeneratedSeq inheritance
    /// </summary>
    public class GeneratedWithitemSeq : GeneratedSeq, System.Collections.Generic.IList<GeneratedWithitem>
    {
        private readonly List<GeneratedWithitem> _items = new();
        public static readonly GeneratedWithitemSeq Empty = new();

        public GeneratedWithitemSeq() { }
        public GeneratedWithitemSeq(int capacity) { _items = new List<GeneratedWithitem>(capacity); }
        public GeneratedWithitemSeq(IEnumerable<GeneratedWithitem> collection) { _items = new List<GeneratedWithitem>(collection); }

        // IList<T> implementation
        public GeneratedWithitem this[int index] { get => _items[index]; set => _items[index] = value; }
        public int Count => _items.Count;
        public bool IsReadOnly => false;
        public void Add(GeneratedWithitem item) => _items.Add(item);
        public void Clear() => _items.Clear();
        public bool Contains(GeneratedWithitem item) => _items.Contains(item);
        public void CopyTo(GeneratedWithitem[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public System.Collections.Generic.IEnumerator<GeneratedWithitem> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        public int IndexOf(GeneratedWithitem item) => _items.IndexOf(item);
        public void Insert(int index, GeneratedWithitem item) => _items.Insert(index, item);
        public bool Remove(GeneratedWithitem item) => _items.Remove(item);
        public void RemoveAt(int index) => _items.RemoveAt(index);

        // Additional List<T> methods for compatibility
        public void AddRange(System.Collections.Generic.IEnumerable<GeneratedWithitem> collection) => _items.AddRange(collection);
    }

    // ============================================================
    // Helper Types
    // ============================================================

    // GeneratedTokenInfo is defined in PyTokenizer.cs

    // ============================================================
    // Parser Intermediate Types
    // CPython 3.12: pegen.c에서 사용하는 임시 타입들
    // ============================================================

    /// <summary>
    /// Intermediate type for slash_with_default rule in python.gram
    /// CPython 3.12: void* compatible via GeneratedPtr inheritance
    /// </summary>
    public class GeneratedSlashWithDefault : GeneratedPtr
    {
        public GeneratedArgSeq Args { get; set; } = new();
        public GeneratedExprSeq Defaults { get; set; } = new();
    }

    /// <summary>
    /// Intermediate type for kvpair rule in python.gram
    /// CPython 3.12: Inherits from GeneratedAstNode for compatibility with mixed sequences
    /// </summary>
    public class GeneratedKeyValuePair : GeneratedAstNode
    {
        public GeneratedExpr Key { get; set; } = null!;
        public GeneratedExpr Value { get; set; } = null!;
    }

    /// <summary>
    /// Intermediate type for star_etc rule in python.gram
    /// CPython 3.12: void* compatible via GeneratedPtr inheritance
    /// </summary>
    public class GeneratedStarEtc : GeneratedPtr
    {
        public GeneratedArg? Vararg { get; set; }
        public GeneratedArgSeq Kwonlyargs { get; set; } = new();
        public GeneratedExprSeq KwDefaults { get; set; } = new();
        public GeneratedArg? Kwarg { get; set; }
    }

    /// <summary>
    /// Intermediate type for keyword_or_starred rule in python.gram
    /// CPython 3.12: void* compatible via GeneratedPtr inheritance
    /// </summary>
    public class GeneratedKeywordOrStarred : GeneratedPtr
    {
        public GeneratedKeyword? Keyword { get; set; }
        public GeneratedExpr? Starred { get; set; }
    }

    /// <summary>
    /// Sequence type for GeneratedSlashWithDefault
    /// CPython 3.12: Used in arguments parsing (slash_with_default*)
    /// </summary>
    public class GeneratedSlashWithDefaultSeq : GeneratedSeq, System.Collections.Generic.IList<GeneratedSlashWithDefault>
    {
        private readonly List<GeneratedSlashWithDefault> _items = new();
        public int Count => _items.Count;
        public bool IsReadOnly => false;
        public GeneratedSlashWithDefault this[int index] { get => _items[index]; set => _items[index] = value; }
        public void Add(GeneratedSlashWithDefault item) => _items.Add(item);
        public void AddRange(System.Collections.Generic.IEnumerable<GeneratedSlashWithDefault> collection) => _items.AddRange(collection);
        public void Clear() => _items.Clear();
        public bool Contains(GeneratedSlashWithDefault item) => _items.Contains(item);
        public void CopyTo(GeneratedSlashWithDefault[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public System.Collections.Generic.IEnumerator<GeneratedSlashWithDefault> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        public int IndexOf(GeneratedSlashWithDefault item) => _items.IndexOf(item);
        public void Insert(int index, GeneratedSlashWithDefault item) => _items.Insert(index, item);
        public bool Remove(GeneratedSlashWithDefault item) => _items.Remove(item);
        public void RemoveAt(int index) => _items.RemoveAt(index);
    }

    /// <summary>
    /// Sequence type for GeneratedStarEtc
    /// CPython 3.12: Used in arguments parsing (star_etc*)
    /// </summary>
    public class GeneratedStarEtcSeq : GeneratedSeq, System.Collections.Generic.IList<GeneratedStarEtc>
    {
        private readonly List<GeneratedStarEtc> _items = new();
        public int Count => _items.Count;
        public bool IsReadOnly => false;
        public GeneratedStarEtc this[int index] { get => _items[index]; set => _items[index] = value; }
        public void Add(GeneratedStarEtc item) => _items.Add(item);
        public void AddRange(System.Collections.Generic.IEnumerable<GeneratedStarEtc> collection) => _items.AddRange(collection);
        public void Clear() => _items.Clear();
        public bool Contains(GeneratedStarEtc item) => _items.Contains(item);
        public void CopyTo(GeneratedStarEtc[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public System.Collections.Generic.IEnumerator<GeneratedStarEtc> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        public int IndexOf(GeneratedStarEtc item) => _items.IndexOf(item);
        public void Insert(int index, GeneratedStarEtc item) => _items.Insert(index, item);
        public bool Remove(GeneratedStarEtc item) => _items.Remove(item);
        public void RemoveAt(int index) => _items.RemoveAt(index);
    }

    /// <summary>
    /// Sequence type for GeneratedKeywordOrStarred
    /// CPython 3.12: Used in call arguments parsing (','.kwarg_or_starred+)
    /// </summary>
    public class GeneratedKeywordOrStarredSeq : GeneratedSeq, System.Collections.Generic.IList<GeneratedKeywordOrStarred>
    {
        private readonly List<GeneratedKeywordOrStarred> _items = new();
        public int Count => _items.Count;
        public bool IsReadOnly => false;
        public GeneratedKeywordOrStarred this[int index] { get => _items[index]; set => _items[index] = value; }
        public void Add(GeneratedKeywordOrStarred item) => _items.Add(item);
        public void AddRange(System.Collections.Generic.IEnumerable<GeneratedKeywordOrStarred> collection) => _items.AddRange(collection);
        public void Clear() => _items.Clear();
        public bool Contains(GeneratedKeywordOrStarred item) => _items.Contains(item);
        public void CopyTo(GeneratedKeywordOrStarred[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public System.Collections.Generic.IEnumerator<GeneratedKeywordOrStarred> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        public int IndexOf(GeneratedKeywordOrStarred item) => _items.IndexOf(item);
        public void Insert(int index, GeneratedKeywordOrStarred item) => _items.Insert(index, item);
        public bool Remove(GeneratedKeywordOrStarred item) => _items.Remove(item);
        public void RemoveAt(int index) => _items.RemoveAt(index);
    }

    /// <summary>
    /// Generic sequence wrapper for parser intermediate results
    /// </summary>
    public class GeneratedSeq<T> : List<T>
    {
        public GeneratedSeq() { }
        public GeneratedSeq(int capacity) : base(capacity) { }
        public GeneratedSeq(IEnumerable<T> collection) : base(collection) { }
    }

    /// <summary>
    /// Non-generic sequence for mixed types - parser intermediate
    /// CPython 3.12: asdl_seq* equivalent - no boxing/unboxing
    /// </summary>
    public class GeneratedMixedSeq : GeneratedSeq, System.Collections.Generic.IList<GeneratedPtr>
    {
        private readonly List<GeneratedPtr> _items = new();

        public GeneratedMixedSeq() { }
        public GeneratedMixedSeq(int capacity) { _items = new List<GeneratedPtr>(capacity); }
        public GeneratedMixedSeq(IEnumerable<GeneratedPtr> collection) { _items = new List<GeneratedPtr>(collection); }

        // IList<GeneratedPtr> implementation
        public GeneratedPtr this[int index] { get => _items[index]; set => _items[index] = value; }
        public int Count => _items.Count;
        public bool IsReadOnly => false;
        public void Add(GeneratedPtr item) => _items.Add(item);
        public void Clear() => _items.Clear();
        public bool Contains(GeneratedPtr item) => _items.Contains(item);
        public void CopyTo(GeneratedPtr[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public System.Collections.Generic.IEnumerator<GeneratedPtr> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        public int IndexOf(GeneratedPtr item) => _items.IndexOf(item);
        public void Insert(int index, GeneratedPtr item) => _items.Insert(index, item);
        public bool Remove(GeneratedPtr item) => _items.Remove(item);
        public void RemoveAt(int index) => _items.RemoveAt(index);

        // Additional List<T> methods for compatibility
        public void AddRange(System.Collections.Generic.IEnumerable<GeneratedPtr> collection) => _items.AddRange(collection);
    }

    /// <summary>
    /// Sequence of GeneratedAstNode for mixed AST types
    /// CPython 3.12: Used for sequences that can contain any AST node type
    /// </summary>
    public class GeneratedAstNodeSeq : GeneratedSeq, System.Collections.Generic.IList<GeneratedAstNode>
    {
        private readonly List<GeneratedAstNode> _items = new();
        public static readonly GeneratedAstNodeSeq Empty = new();

        public GeneratedAstNodeSeq() { }
        public GeneratedAstNodeSeq(int capacity) { _items = new List<GeneratedAstNode>(capacity); }
        public GeneratedAstNodeSeq(IEnumerable<GeneratedAstNode> collection) { _items = new List<GeneratedAstNode>(collection); }

        // IList<GeneratedAstNode> implementation
        public GeneratedAstNode this[int index] { get => _items[index]; set => _items[index] = value; }
        public int Count => _items.Count;
        public bool IsReadOnly => false;
        public void Add(GeneratedAstNode item) => _items.Add(item);
        public void Clear() => _items.Clear();
        public bool Contains(GeneratedAstNode item) => _items.Contains(item);
        public void CopyTo(GeneratedAstNode[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public System.Collections.Generic.IEnumerator<GeneratedAstNode> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        public int IndexOf(GeneratedAstNode item) => _items.IndexOf(item);
        public void Insert(int index, GeneratedAstNode item) => _items.Insert(index, item);
        public bool Remove(GeneratedAstNode item) => _items.Remove(item);
        public void RemoveAt(int index) => _items.RemoveAt(index);

        // Additional List<T> methods for compatibility
        public void AddRange(System.Collections.Generic.IEnumerable<GeneratedAstNode> collection) => _items.AddRange(collection);
    }

}
