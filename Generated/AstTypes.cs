// Generated AST Types from Python.asdl
// CPython 3.12 compatible - Auto-generated, DO NOT EDIT

using System;
using System.Collections.Generic;

namespace SharpPy.Generated
{
    // ============================================================
    // Base Types
    // ============================================================

    // Note: GeneratedPtr and GeneratedSeq are defined in Parser/Tokenizer.cs
    // They are fundamental types used by tokenizer, parser, and AST

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
        public GeneratedIdentifier Name { get; set; } = null!;
        public GeneratedArguments Args { get; set; } = null!;
        public GeneratedStmtSeq Body { get; set; }
        public GeneratedExprSeq DecoratorList { get; set; }
        public GeneratedExpr? Returns { get; set; }
        public string? TypeComment { get; set; }
        public GeneratedTypeParamSeq TypeParams { get; set; }
    }

    public class GeneratedAsyncFunctionDef : GeneratedStmt
    {
        public GeneratedIdentifier Name { get; set; } = null!;
        public GeneratedArguments Args { get; set; } = null!;
        public GeneratedStmtSeq Body { get; set; }
        public GeneratedExprSeq DecoratorList { get; set; }
        public GeneratedExpr? Returns { get; set; }
        public string? TypeComment { get; set; }
        public GeneratedTypeParamSeq TypeParams { get; set; }
    }

    public class GeneratedClassDef : GeneratedStmt
    {
        public GeneratedIdentifier Name { get; set; } = null!;
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
        public GeneratedIdentifier? Module { get; set; }
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
        public GeneratedPyConstant Value { get; set; } = null!;
        public string? Kind { get; set; }
    }

    public class GeneratedAttribute : GeneratedExpr
    {
        public GeneratedExpr Value { get; set; } = null!;
        public GeneratedIdentifier Attr { get; set; } = null!;
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
        public GeneratedIdentifier Id { get; set; } = null!;
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
        public GeneratedIdentifier? Name { get; set; }
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
        public GeneratedIdentifier Arg { get; set; } = null!;
        public GeneratedExpr? Annotation { get; set; }
        public string? TypeComment { get; set; }
    }

    // ============================================================
    // keyword types
    // ============================================================

    public class GeneratedKeyword : GeneratedAstNode
    {
        public GeneratedIdentifier? Arg { get; set; }
        public GeneratedExpr Value { get; set; } = null!;
    }

    // ============================================================
    // alias types
    // ============================================================

    public class GeneratedAlias : GeneratedAstNode
    {
        public GeneratedIdentifier Name { get; set; } = null!;
        public GeneratedIdentifier? Asname { get; set; }
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
        public GeneratedPyConstant Value { get; set; } = null!;
    }

    public class GeneratedMatchSequence : GeneratedPattern
    {
        public GeneratedPatternSeq Patterns { get; set; }
    }

    public class GeneratedMatchMapping : GeneratedPattern
    {
        public GeneratedExprSeq Keys { get; set; }
        public GeneratedPatternSeq Patterns { get; set; }
        public GeneratedIdentifier? Rest { get; set; }
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
        public GeneratedIdentifier? Name { get; set; }
    }

    public class GeneratedMatchAs : GeneratedPattern
    {
        public GeneratedPattern? Pattern { get; set; }
        public GeneratedIdentifier? Name { get; set; }
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
        public GeneratedIdentifier Name { get; set; } = null!;
        public GeneratedExpr? Bound { get; set; }
    }

    public class GeneratedParamSpec : GeneratedTypeParam
    {
        public GeneratedIdentifier Name { get; set; } = null!;
    }

    public class GeneratedTypeVarTuple : GeneratedTypeParam
    {
        public GeneratedIdentifier Name { get; set; } = null!;
    }

    // ============================================================
    // Sequence Types (GC optimized)
    // ============================================================

    /// <summary>
    /// Sequence of alias - CPython: asdl_alias_seq
    /// </summary>
    public class GeneratedAliasSeq : GeneratedSeq
    {
        public static readonly GeneratedAliasSeq Empty = new();

        public GeneratedAliasSeq() { }
        public GeneratedAliasSeq(int capacity) : base(capacity) { }
        public GeneratedAliasSeq(IEnumerable<GeneratedAlias> collection)
        {
            foreach (var item in collection) Add(item);
        }

        public new void Add(GeneratedAlias item) => base.Add(item);
        public new GeneratedAlias this[int index]
        {
            get => (GeneratedAlias)base[index];
            set => base[index] = value;
        }
    }

    /// <summary>
    /// Sequence of arg - CPython: asdl_arg_seq
    /// </summary>
    public class GeneratedArgSeq : GeneratedSeq
    {
        public static readonly GeneratedArgSeq Empty = new();

        public GeneratedArgSeq() { }
        public GeneratedArgSeq(int capacity) : base(capacity) { }
        public GeneratedArgSeq(IEnumerable<GeneratedArg> collection)
        {
            foreach (var item in collection) Add(item);
        }

        public new void Add(GeneratedArg item) => base.Add(item);
        public new GeneratedArg this[int index]
        {
            get => (GeneratedArg)base[index];
            set => base[index] = value;
        }
    }

    /// <summary>
    /// Sequence of arguments - CPython: asdl_arguments_seq
    /// </summary>
    public class GeneratedArgumentsSeq : GeneratedSeq
    {
        public static readonly GeneratedArgumentsSeq Empty = new();

        public GeneratedArgumentsSeq() { }
        public GeneratedArgumentsSeq(int capacity) : base(capacity) { }
        public GeneratedArgumentsSeq(IEnumerable<GeneratedArguments> collection)
        {
            foreach (var item in collection) Add(item);
        }

        public new void Add(GeneratedArguments item) => base.Add(item);
        public new GeneratedArguments this[int index]
        {
            get => (GeneratedArguments)base[index];
            set => base[index] = value;
        }
    }

    /// <summary>
    /// Sequence of boolop - CPython: asdl_boolop_seq
    /// </summary>
    public class GeneratedBoolopSeq : GeneratedSeq
    {
        public static readonly GeneratedBoolopSeq Empty = new();

        public GeneratedBoolopSeq() { }
        public GeneratedBoolopSeq(int capacity) : base(capacity) { }
        public GeneratedBoolopSeq(IEnumerable<GeneratedBoolop> collection)
        {
            foreach (var item in collection) Add(item);
        }

        public new void Add(GeneratedBoolop item) => base.Add(item);
        public new GeneratedBoolop this[int index]
        {
            get => (GeneratedBoolop)base[index];
            set => base[index] = value;
        }
    }

    /// <summary>
    /// Sequence of cmpop - CPython: asdl_cmpop_seq
    /// </summary>
    public class GeneratedCmpopSeq : GeneratedSeq
    {
        public static readonly GeneratedCmpopSeq Empty = new();

        public GeneratedCmpopSeq() { }
        public GeneratedCmpopSeq(int capacity) : base(capacity) { }
        public GeneratedCmpopSeq(IEnumerable<GeneratedCmpop> collection)
        {
            foreach (var item in collection) Add(item);
        }

        public new void Add(GeneratedCmpop item) => base.Add(item);
        public new GeneratedCmpop this[int index]
        {
            get => (GeneratedCmpop)base[index];
            set => base[index] = value;
        }
    }

    /// <summary>
    /// Sequence of comprehension - CPython: asdl_comprehension_seq
    /// </summary>
    public class GeneratedComprehensionSeq : GeneratedSeq
    {
        public static readonly GeneratedComprehensionSeq Empty = new();

        public GeneratedComprehensionSeq() { }
        public GeneratedComprehensionSeq(int capacity) : base(capacity) { }
        public GeneratedComprehensionSeq(IEnumerable<GeneratedComprehension> collection)
        {
            foreach (var item in collection) Add(item);
        }

        public new void Add(GeneratedComprehension item) => base.Add(item);
        public new GeneratedComprehension this[int index]
        {
            get => (GeneratedComprehension)base[index];
            set => base[index] = value;
        }
    }

    /// <summary>
    /// Sequence of excepthandler - CPython: asdl_excepthandler_seq
    /// </summary>
    public class GeneratedExcepthandlerSeq : GeneratedSeq
    {
        public static readonly GeneratedExcepthandlerSeq Empty = new();

        public GeneratedExcepthandlerSeq() { }
        public GeneratedExcepthandlerSeq(int capacity) : base(capacity) { }
        public GeneratedExcepthandlerSeq(IEnumerable<GeneratedExcepthandler> collection)
        {
            foreach (var item in collection) Add(item);
        }

        public new void Add(GeneratedExcepthandler item) => base.Add(item);
        public new GeneratedExcepthandler this[int index]
        {
            get => (GeneratedExcepthandler)base[index];
            set => base[index] = value;
        }
    }

    /// <summary>
    /// Sequence of expr - CPython: asdl_expr_seq
    /// </summary>
    public class GeneratedExprSeq : GeneratedSeq
    {
        public static readonly GeneratedExprSeq Empty = new();

        public GeneratedExprSeq() { }
        public GeneratedExprSeq(int capacity) : base(capacity) { }
        public GeneratedExprSeq(IEnumerable<GeneratedExpr> collection)
        {
            foreach (var item in collection) Add(item);
        }

        public new void Add(GeneratedExpr item) => base.Add(item);
        public new GeneratedExpr this[int index]
        {
            get => (GeneratedExpr)base[index];
            set => base[index] = value;
        }
    }

    /// <summary>
    /// Sequence of expr_context - CPython: asdl_expr_context_seq
    /// </summary>
    public class GeneratedExprContextSeq : GeneratedSeq
    {
        public static readonly GeneratedExprContextSeq Empty = new();

        public GeneratedExprContextSeq() { }
        public GeneratedExprContextSeq(int capacity) : base(capacity) { }
        public GeneratedExprContextSeq(IEnumerable<GeneratedExprContext> collection)
        {
            foreach (var item in collection) Add(item);
        }

        public new void Add(GeneratedExprContext item) => base.Add(item);
        public new GeneratedExprContext this[int index]
        {
            get => (GeneratedExprContext)base[index];
            set => base[index] = value;
        }
    }

    /// <summary>
    /// Sequence of identifier - CPython: asdl_identifier_seq
    /// </summary>
    public class GeneratedIdentifierSeq : GeneratedSeq
    {
        public static readonly GeneratedIdentifierSeq Empty = new();

        public GeneratedIdentifierSeq() { }
        public GeneratedIdentifierSeq(int capacity) : base(capacity) { }
        public GeneratedIdentifierSeq(IEnumerable<GeneratedIdentifier> collection)
        {
            foreach (var item in collection) Add(item);
        }

        public new void Add(GeneratedIdentifier item) => base.Add(item);
        public new GeneratedIdentifier this[int index]
        {
            get => (GeneratedIdentifier)base[index];
            set => base[index] = value;
        }
    }

    /// <summary>
    /// Sequence of keyword - CPython: asdl_keyword_seq
    /// </summary>
    public class GeneratedKeywordSeq : GeneratedSeq
    {
        public static readonly GeneratedKeywordSeq Empty = new();

        public GeneratedKeywordSeq() { }
        public GeneratedKeywordSeq(int capacity) : base(capacity) { }
        public GeneratedKeywordSeq(IEnumerable<GeneratedKeyword> collection)
        {
            foreach (var item in collection) Add(item);
        }

        public new void Add(GeneratedKeyword item) => base.Add(item);
        public new GeneratedKeyword this[int index]
        {
            get => (GeneratedKeyword)base[index];
            set => base[index] = value;
        }
    }

    /// <summary>
    /// Sequence of match_case - CPython: asdl_match_case_seq
    /// </summary>
    public class GeneratedMatchCaseSeq : GeneratedSeq
    {
        public static readonly GeneratedMatchCaseSeq Empty = new();

        public GeneratedMatchCaseSeq() { }
        public GeneratedMatchCaseSeq(int capacity) : base(capacity) { }
        public GeneratedMatchCaseSeq(IEnumerable<GeneratedMatchCase> collection)
        {
            foreach (var item in collection) Add(item);
        }

        public new void Add(GeneratedMatchCase item) => base.Add(item);
        public new GeneratedMatchCase this[int index]
        {
            get => (GeneratedMatchCase)base[index];
            set => base[index] = value;
        }
    }

    /// <summary>
    /// Sequence of mod - CPython: asdl_mod_seq
    /// </summary>
    public class GeneratedModSeq : GeneratedSeq
    {
        public static readonly GeneratedModSeq Empty = new();

        public GeneratedModSeq() { }
        public GeneratedModSeq(int capacity) : base(capacity) { }
        public GeneratedModSeq(IEnumerable<GeneratedMod> collection)
        {
            foreach (var item in collection) Add(item);
        }

        public new void Add(GeneratedMod item) => base.Add(item);
        public new GeneratedMod this[int index]
        {
            get => (GeneratedMod)base[index];
            set => base[index] = value;
        }
    }

    /// <summary>
    /// Sequence of operator - CPython: asdl_operator_seq
    /// </summary>
    public class GeneratedOperatorSeq : GeneratedSeq
    {
        public static readonly GeneratedOperatorSeq Empty = new();

        public GeneratedOperatorSeq() { }
        public GeneratedOperatorSeq(int capacity) : base(capacity) { }
        public GeneratedOperatorSeq(IEnumerable<GeneratedOperator> collection)
        {
            foreach (var item in collection) Add(item);
        }

        public new void Add(GeneratedOperator item) => base.Add(item);
        public new GeneratedOperator this[int index]
        {
            get => (GeneratedOperator)base[index];
            set => base[index] = value;
        }
    }

    /// <summary>
    /// Sequence of pattern - CPython: asdl_pattern_seq
    /// </summary>
    public class GeneratedPatternSeq : GeneratedSeq
    {
        public static readonly GeneratedPatternSeq Empty = new();

        public GeneratedPatternSeq() { }
        public GeneratedPatternSeq(int capacity) : base(capacity) { }
        public GeneratedPatternSeq(IEnumerable<GeneratedPattern> collection)
        {
            foreach (var item in collection) Add(item);
        }

        public new void Add(GeneratedPattern item) => base.Add(item);
        public new GeneratedPattern this[int index]
        {
            get => (GeneratedPattern)base[index];
            set => base[index] = value;
        }
    }

    /// <summary>
    /// Sequence of stmt - CPython: asdl_stmt_seq
    /// </summary>
    public class GeneratedStmtSeq : GeneratedSeq
    {
        public static readonly GeneratedStmtSeq Empty = new();

        public GeneratedStmtSeq() { }
        public GeneratedStmtSeq(int capacity) : base(capacity) { }
        public GeneratedStmtSeq(IEnumerable<GeneratedStmt> collection)
        {
            foreach (var item in collection) Add(item);
        }

        public new void Add(GeneratedStmt item) => base.Add(item);
        public new GeneratedStmt this[int index]
        {
            get => (GeneratedStmt)base[index];
            set => base[index] = value;
        }
    }

    /// <summary>
    /// Sequence of type_ignore - CPython: asdl_type_ignore_seq
    /// </summary>
    public class GeneratedTypeIgnoreSeq : GeneratedSeq
    {
        public static readonly GeneratedTypeIgnoreSeq Empty = new();

        public GeneratedTypeIgnoreSeq() { }
        public GeneratedTypeIgnoreSeq(int capacity) : base(capacity) { }
        public GeneratedTypeIgnoreSeq(IEnumerable<GeneratedTypeIgnore> collection)
        {
            foreach (var item in collection) Add(item);
        }

        public new void Add(GeneratedTypeIgnore item) => base.Add(item);
        public new GeneratedTypeIgnore this[int index]
        {
            get => (GeneratedTypeIgnore)base[index];
            set => base[index] = value;
        }
    }

    /// <summary>
    /// Sequence of type_param - CPython: asdl_type_param_seq
    /// </summary>
    public class GeneratedTypeParamSeq : GeneratedSeq
    {
        public static readonly GeneratedTypeParamSeq Empty = new();

        public GeneratedTypeParamSeq() { }
        public GeneratedTypeParamSeq(int capacity) : base(capacity) { }
        public GeneratedTypeParamSeq(IEnumerable<GeneratedTypeParam> collection)
        {
            foreach (var item in collection) Add(item);
        }

        public new void Add(GeneratedTypeParam item) => base.Add(item);
        public new GeneratedTypeParam this[int index]
        {
            get => (GeneratedTypeParam)base[index];
            set => base[index] = value;
        }
    }

    /// <summary>
    /// Sequence of unaryop - CPython: asdl_unaryop_seq
    /// </summary>
    public class GeneratedUnaryopSeq : GeneratedSeq
    {
        public static readonly GeneratedUnaryopSeq Empty = new();

        public GeneratedUnaryopSeq() { }
        public GeneratedUnaryopSeq(int capacity) : base(capacity) { }
        public GeneratedUnaryopSeq(IEnumerable<GeneratedUnaryop> collection)
        {
            foreach (var item in collection) Add(item);
        }

        public new void Add(GeneratedUnaryop item) => base.Add(item);
        public new GeneratedUnaryop this[int index]
        {
            get => (GeneratedUnaryop)base[index];
            set => base[index] = value;
        }
    }

    /// <summary>
    /// Sequence of withitem - CPython: asdl_withitem_seq
    /// </summary>
    public class GeneratedWithitemSeq : GeneratedSeq
    {
        public static readonly GeneratedWithitemSeq Empty = new();

        public GeneratedWithitemSeq() { }
        public GeneratedWithitemSeq(int capacity) : base(capacity) { }
        public GeneratedWithitemSeq(IEnumerable<GeneratedWithitem> collection)
        {
            foreach (var item in collection) Add(item);
        }

        public new void Add(GeneratedWithitem item) => base.Add(item);
        public new GeneratedWithitem this[int index]
        {
            get => (GeneratedWithitem)base[index];
            set => base[index] = value;
        }
    }

    // ============================================================
    // Additional Parser Helper Sequence Types
    // ============================================================

    /// <summary>
    /// Sequence of mixed AST nodes - CPython: asdl_seq*
    /// Used for intermediate parser results (CmpopExprPair, etc.)
    /// </summary>
    public class GeneratedAstNodeSeq : GeneratedSeq
    {
        public static readonly GeneratedAstNodeSeq Empty = new();

        public GeneratedAstNodeSeq() { }
        public GeneratedAstNodeSeq(int capacity) : base(capacity) { }
        public GeneratedAstNodeSeq(IEnumerable<GeneratedPtr> collection)
        {
            foreach (var item in collection) Add(item);
        }
    }

    /// <summary>
    /// Sequence of KeywordOrStarred - CPython: asdl_seq*
    /// Used for parsing function call arguments
    /// </summary>
    public class GeneratedKeywordOrStarredSeq : GeneratedSeq
    {
        public static readonly GeneratedKeywordOrStarredSeq Empty = new();

        public GeneratedKeywordOrStarredSeq() { }
        public GeneratedKeywordOrStarredSeq(int capacity) : base(capacity) { }
        public GeneratedKeywordOrStarredSeq(IEnumerable<GeneratedKeywordOrStarred> collection)
        {
            foreach (var item in collection) Add(item);
        }

        public new void Add(GeneratedKeywordOrStarred item) => base.Add(item);
        public new GeneratedKeywordOrStarred this[int index]
        {
            get => (GeneratedKeywordOrStarred)base[index];
            set => base[index] = value;
        }
    }

    // ============================================================
    // Helper Types
    // ============================================================

    /// <summary>
    /// Wrapper for Python identifier (ASDL builtin)
    /// </summary>
    public class GeneratedIdentifier : GeneratedPtr
    {
        public string Value { get; set; }

        public GeneratedIdentifier(string value) { Value = value; }

        public static implicit operator string(GeneratedIdentifier id) => id?.Value;
        public static implicit operator GeneratedIdentifier(string value) => new GeneratedIdentifier(value);

        public override string ToString() => Value;
    }

    // ============================================================
    // Parser Intermediate Types
    // ============================================================

    public class GeneratedSlashWithDefault : GeneratedPtr
    {
        public GeneratedArgSeq Args { get; set; } = new();
        public GeneratedExprSeq Defaults { get; set; } = new();
    }

    public class GeneratedKeyValuePair : GeneratedAstNode
    {
        public GeneratedExpr Key { get; set; } = null!;
        public GeneratedExpr Value { get; set; } = null!;
    }

    public abstract class GeneratedPyConstant
    {
        public static readonly GeneratedPyConstantNone None = new();
        public static readonly GeneratedPyConstantBool True = new(true);
        public static readonly GeneratedPyConstantBool False = new(false);
        public static readonly GeneratedPyConstantEllipsis Ellipsis = new();
    }

    public class GeneratedPyConstantNone : GeneratedPyConstant
    {
        internal GeneratedPyConstantNone() { }
    }

    public class GeneratedPyConstantBool : GeneratedPyConstant
    {
        public bool Value { get; }
        internal GeneratedPyConstantBool(bool value) => Value = value;
    }

    public class GeneratedPyConstantInt : GeneratedPyConstant
    {
        public long Value { get; }
        public GeneratedPyConstantInt(long value) => Value = value;
    }

    public class GeneratedPyConstantFloat : GeneratedPyConstant
    {
        public double Value { get; }
        public GeneratedPyConstantFloat(double value) => Value = value;
    }

    public class GeneratedPyConstantString : GeneratedPyConstant
    {
        public string Value { get; }
        public GeneratedPyConstantString(string value) => Value = value;
    }

    public class GeneratedPyConstantBytes : GeneratedPyConstant
    {
        public byte[] Value { get; }
        public GeneratedPyConstantBytes(byte[] value) => Value = value;
    }

    public class GeneratedPyConstantEllipsis : GeneratedPyConstant
    {
        internal GeneratedPyConstantEllipsis() { }
    }

    public class GeneratedStarEtc : GeneratedPtr
    {
        public GeneratedArg? Vararg { get; set; }
        public GeneratedArgSeq Kwonlyargs { get; set; } = new();
        public GeneratedExprSeq KwDefaults { get; set; } = new();
        public GeneratedArg? Kwarg { get; set; }
    }

    public class GeneratedKeywordOrStarred : GeneratedPtr
    {
        public GeneratedKeyword? Keyword { get; set; }
        public GeneratedExpr? Starred { get; set; }
    }

    // ============================================================
    // Placeholder Type for incomplete AST generation
    // ============================================================

    /// <summary>
    /// Placeholder for rules that don't yet generate proper AST nodes
    /// TODO: Replace with actual AST node generation
    /// </summary>
    public class GeneratedPlaceholder : GeneratedPtr
    {
        public static readonly GeneratedPlaceholder Instance = new();
        private GeneratedPlaceholder() { }
    }

}
