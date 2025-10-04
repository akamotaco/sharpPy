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
    public class GeneratedModOp : GeneratedOperator
    {
        public static readonly GeneratedModOp Instance = new();
        private GeneratedModOp() { }
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
    /// Sequence of alias - CPython: asdl_alias_seq
    /// C# GC optimized: List<T> with Empty singleton
    /// </summary>
    public class GeneratedAliasSeq : List<GeneratedAlias>
    {
        public static readonly GeneratedAliasSeq Empty = new();

        public GeneratedAliasSeq() { }
        public GeneratedAliasSeq(int capacity) : base(capacity) { }
        public GeneratedAliasSeq(IEnumerable<GeneratedAlias> collection) : base(collection) { }
    }

    /// <summary>
    /// Sequence of arg - CPython: asdl_arg_seq
    /// C# GC optimized: List<T> with Empty singleton
    /// </summary>
    public class GeneratedArgSeq : List<GeneratedArg>
    {
        public static readonly GeneratedArgSeq Empty = new();

        public GeneratedArgSeq() { }
        public GeneratedArgSeq(int capacity) : base(capacity) { }
        public GeneratedArgSeq(IEnumerable<GeneratedArg> collection) : base(collection) { }
    }

    /// <summary>
    /// Sequence of arguments - CPython: asdl_arguments_seq
    /// C# GC optimized: List<T> with Empty singleton
    /// </summary>
    public class GeneratedArgumentsSeq : List<GeneratedArguments>
    {
        public static readonly GeneratedArgumentsSeq Empty = new();

        public GeneratedArgumentsSeq() { }
        public GeneratedArgumentsSeq(int capacity) : base(capacity) { }
        public GeneratedArgumentsSeq(IEnumerable<GeneratedArguments> collection) : base(collection) { }
    }

    /// <summary>
    /// Sequence of boolop - CPython: asdl_boolop_seq
    /// C# GC optimized: List<T> with Empty singleton
    /// </summary>
    public class GeneratedBoolopSeq : List<GeneratedBoolop>
    {
        public static readonly GeneratedBoolopSeq Empty = new();

        public GeneratedBoolopSeq() { }
        public GeneratedBoolopSeq(int capacity) : base(capacity) { }
        public GeneratedBoolopSeq(IEnumerable<GeneratedBoolop> collection) : base(collection) { }
    }

    /// <summary>
    /// Sequence of cmpop - CPython: asdl_cmpop_seq
    /// C# GC optimized: List<T> with Empty singleton
    /// </summary>
    public class GeneratedCmpopSeq : List<GeneratedCmpop>
    {
        public static readonly GeneratedCmpopSeq Empty = new();

        public GeneratedCmpopSeq() { }
        public GeneratedCmpopSeq(int capacity) : base(capacity) { }
        public GeneratedCmpopSeq(IEnumerable<GeneratedCmpop> collection) : base(collection) { }
    }

    /// <summary>
    /// Sequence of comprehension - CPython: asdl_comprehension_seq
    /// C# GC optimized: List<T> with Empty singleton
    /// </summary>
    public class GeneratedComprehensionSeq : List<GeneratedComprehension>
    {
        public static readonly GeneratedComprehensionSeq Empty = new();

        public GeneratedComprehensionSeq() { }
        public GeneratedComprehensionSeq(int capacity) : base(capacity) { }
        public GeneratedComprehensionSeq(IEnumerable<GeneratedComprehension> collection) : base(collection) { }
    }

    /// <summary>
    /// Sequence of excepthandler - CPython: asdl_excepthandler_seq
    /// C# GC optimized: List<T> with Empty singleton
    /// </summary>
    public class GeneratedExcepthandlerSeq : List<GeneratedExcepthandler>
    {
        public static readonly GeneratedExcepthandlerSeq Empty = new();

        public GeneratedExcepthandlerSeq() { }
        public GeneratedExcepthandlerSeq(int capacity) : base(capacity) { }
        public GeneratedExcepthandlerSeq(IEnumerable<GeneratedExcepthandler> collection) : base(collection) { }
    }

    /// <summary>
    /// Sequence of expr - CPython: asdl_expr_seq
    /// C# GC optimized: List<T> with Empty singleton
    /// </summary>
    public class GeneratedExprSeq : List<GeneratedExpr>
    {
        public static readonly GeneratedExprSeq Empty = new();

        public GeneratedExprSeq() { }
        public GeneratedExprSeq(int capacity) : base(capacity) { }
        public GeneratedExprSeq(IEnumerable<GeneratedExpr> collection) : base(collection) { }
    }

    /// <summary>
    /// Sequence of expr_context - CPython: asdl_expr_context_seq
    /// C# GC optimized: List<T> with Empty singleton
    /// </summary>
    public class GeneratedExprContextSeq : List<GeneratedExprContext>
    {
        public static readonly GeneratedExprContextSeq Empty = new();

        public GeneratedExprContextSeq() { }
        public GeneratedExprContextSeq(int capacity) : base(capacity) { }
        public GeneratedExprContextSeq(IEnumerable<GeneratedExprContext> collection) : base(collection) { }
    }

    /// <summary>
    /// Sequence of identifier - CPython: asdl_identifier_seq
    /// C# GC optimized: List<T> with Empty singleton
    /// </summary>
    public class GeneratedIdentifierSeq : List<string>
    {
        public static readonly GeneratedIdentifierSeq Empty = new();

        public GeneratedIdentifierSeq() { }
        public GeneratedIdentifierSeq(int capacity) : base(capacity) { }
        public GeneratedIdentifierSeq(IEnumerable<string> collection) : base(collection) { }
    }

    /// <summary>
    /// Sequence of keyword - CPython: asdl_keyword_seq
    /// C# GC optimized: List<T> with Empty singleton
    /// </summary>
    public class GeneratedKeywordSeq : List<GeneratedKeyword>
    {
        public static readonly GeneratedKeywordSeq Empty = new();

        public GeneratedKeywordSeq() { }
        public GeneratedKeywordSeq(int capacity) : base(capacity) { }
        public GeneratedKeywordSeq(IEnumerable<GeneratedKeyword> collection) : base(collection) { }
    }

    /// <summary>
    /// Sequence of match_case - CPython: asdl_match_case_seq
    /// C# GC optimized: List<T> with Empty singleton
    /// </summary>
    public class GeneratedMatchCaseSeq : List<GeneratedMatchCase>
    {
        public static readonly GeneratedMatchCaseSeq Empty = new();

        public GeneratedMatchCaseSeq() { }
        public GeneratedMatchCaseSeq(int capacity) : base(capacity) { }
        public GeneratedMatchCaseSeq(IEnumerable<GeneratedMatchCase> collection) : base(collection) { }
    }

    /// <summary>
    /// Sequence of mod - CPython: asdl_mod_seq
    /// C# GC optimized: List<T> with Empty singleton
    /// </summary>
    public class GeneratedModSeq : List<GeneratedMod>
    {
        public static readonly GeneratedModSeq Empty = new();

        public GeneratedModSeq() { }
        public GeneratedModSeq(int capacity) : base(capacity) { }
        public GeneratedModSeq(IEnumerable<GeneratedMod> collection) : base(collection) { }
    }

    /// <summary>
    /// Sequence of operator - CPython: asdl_operator_seq
    /// C# GC optimized: List<T> with Empty singleton
    /// </summary>
    public class GeneratedOperatorSeq : List<GeneratedOperator>
    {
        public static readonly GeneratedOperatorSeq Empty = new();

        public GeneratedOperatorSeq() { }
        public GeneratedOperatorSeq(int capacity) : base(capacity) { }
        public GeneratedOperatorSeq(IEnumerable<GeneratedOperator> collection) : base(collection) { }
    }

    /// <summary>
    /// Sequence of pattern - CPython: asdl_pattern_seq
    /// C# GC optimized: List<T> with Empty singleton
    /// </summary>
    public class GeneratedPatternSeq : List<GeneratedPattern>
    {
        public static readonly GeneratedPatternSeq Empty = new();

        public GeneratedPatternSeq() { }
        public GeneratedPatternSeq(int capacity) : base(capacity) { }
        public GeneratedPatternSeq(IEnumerable<GeneratedPattern> collection) : base(collection) { }
    }

    /// <summary>
    /// Sequence of stmt - CPython: asdl_stmt_seq
    /// C# GC optimized: List<T> with Empty singleton
    /// </summary>
    public class GeneratedStmtSeq : List<GeneratedStmt>
    {
        public static readonly GeneratedStmtSeq Empty = new();

        public GeneratedStmtSeq() { }
        public GeneratedStmtSeq(int capacity) : base(capacity) { }
        public GeneratedStmtSeq(IEnumerable<GeneratedStmt> collection) : base(collection) { }
    }

    /// <summary>
    /// Sequence of type_ignore - CPython: asdl_type_ignore_seq
    /// C# GC optimized: List<T> with Empty singleton
    /// </summary>
    public class GeneratedTypeIgnoreSeq : List<GeneratedTypeIgnore>
    {
        public static readonly GeneratedTypeIgnoreSeq Empty = new();

        public GeneratedTypeIgnoreSeq() { }
        public GeneratedTypeIgnoreSeq(int capacity) : base(capacity) { }
        public GeneratedTypeIgnoreSeq(IEnumerable<GeneratedTypeIgnore> collection) : base(collection) { }
    }

    /// <summary>
    /// Sequence of type_param - CPython: asdl_type_param_seq
    /// C# GC optimized: List<T> with Empty singleton
    /// </summary>
    public class GeneratedTypeParamSeq : List<GeneratedTypeParam>
    {
        public static readonly GeneratedTypeParamSeq Empty = new();

        public GeneratedTypeParamSeq() { }
        public GeneratedTypeParamSeq(int capacity) : base(capacity) { }
        public GeneratedTypeParamSeq(IEnumerable<GeneratedTypeParam> collection) : base(collection) { }
    }

    /// <summary>
    /// Sequence of unaryop - CPython: asdl_unaryop_seq
    /// C# GC optimized: List<T> with Empty singleton
    /// </summary>
    public class GeneratedUnaryopSeq : List<GeneratedUnaryop>
    {
        public static readonly GeneratedUnaryopSeq Empty = new();

        public GeneratedUnaryopSeq() { }
        public GeneratedUnaryopSeq(int capacity) : base(capacity) { }
        public GeneratedUnaryopSeq(IEnumerable<GeneratedUnaryop> collection) : base(collection) { }
    }

    /// <summary>
    /// Sequence of withitem - CPython: asdl_withitem_seq
    /// C# GC optimized: List<T> with Empty singleton
    /// </summary>
    public class GeneratedWithitemSeq : List<GeneratedWithitem>
    {
        public static readonly GeneratedWithitemSeq Empty = new();

        public GeneratedWithitemSeq() { }
        public GeneratedWithitemSeq(int capacity) : base(capacity) { }
        public GeneratedWithitemSeq(IEnumerable<GeneratedWithitem> collection) : base(collection) { }
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
    /// </summary>
    public class GeneratedSlashWithDefault
    {
        public GeneratedArgSeq Args { get; set; } = new();
        public GeneratedExprSeq Defaults { get; set; } = new();
    }

    /// <summary>
    /// Intermediate type for star_etc rule in python.gram
    /// </summary>
    public class GeneratedStarEtc
    {
        public GeneratedArg? Vararg { get; set; }
        public GeneratedArgSeq Kwonlyargs { get; set; } = new();
        public GeneratedExprSeq KwDefaults { get; set; } = new();
        public GeneratedArg? Kwarg { get; set; }
    }

    /// <summary>
    /// Intermediate type for keyword_or_starred rule in python.gram
    /// </summary>
    public class GeneratedKeywordOrStarred
    {
        public GeneratedKeyword? Keyword { get; set; }
        public GeneratedExpr? Starred { get; set; }
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
    /// Non-generic sequence for mixed types
    /// </summary>
    public class GeneratedSeq : List<object>
    {
        public GeneratedSeq() { }
        public GeneratedSeq(int capacity) : base(capacity) { }
        public GeneratedSeq(IEnumerable<object> collection) : base(collection) { }
    }

    /// <summary>
    /// Sequence of GeneratedAstNode for mixed AST types
    /// </summary>
    public class GeneratedAstNodeSeq : List<GeneratedAstNode>
    {
        public static readonly GeneratedAstNodeSeq Empty = new();

        public GeneratedAstNodeSeq() { }
        public GeneratedAstNodeSeq(int capacity) : base(capacity) { }
        public GeneratedAstNodeSeq(IEnumerable<GeneratedAstNode> collection) : base(collection) { }
    }

}
