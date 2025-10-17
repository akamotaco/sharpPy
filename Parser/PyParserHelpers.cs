// PyParserHelpers - Helper functions for PEG parser
// CPython 3.12 compatible - Manual implementation (not generated)
// This file provides AST factory functions and PEG parser helper utilities

using System;
using System.Collections.Generic;
using System.Linq;
using static SharpPy.GeneratedParserBridge;

// CPython 3.12: Type aliases for grammar compatibility
using mod_ty = SharpPy.Generated.GeneratedMod;
using stmt_ty = SharpPy.Generated.GeneratedStmt;
using expr_ty = SharpPy.Generated.GeneratedExpr;
using alias_ty = SharpPy.Generated.GeneratedAlias;
using arguments_ty = SharpPy.Generated.GeneratedArguments;
using asdl_stmt_seq = SharpPy.Generated.GeneratedStmtSeq;
using asdl_expr_seq = SharpPy.Generated.GeneratedExprSeq;
using asdl_arg_seq = SharpPy.Generated.GeneratedArgSeq;
using asdl_identifier_seq = SharpPy.Generated.GeneratedIdentifierSeq;
using asdl_pattern_seq = SharpPy.Generated.GeneratedPatternSeq;
using asdl_int_seq = SharpPy.Generated.GeneratedCmpopSeq;
using asdl_keyword_seq = SharpPy.Generated.GeneratedKeywordSeq;
using asdl_seq = SharpPy.Generated.GeneratedSeq;
using keyword_ty = SharpPy.Generated.GeneratedKeyword;

namespace SharpPy.Generated
{
    // ============================================================
    // _PyAST_* Helper Functions
    // ============================================================

    /// <summary>
    /// AST node factory functions - CPython 3.12: Python-ast.c
    /// </summary>
    public static partial class AstFactory
    {
        public static GeneratedMod _PyAST_Module(GeneratedStmtSeq body, GeneratedTypeIgnoreSeq type_ignores)
        {
            var node = new GeneratedModule();
            node.Body = body;
            node.TypeIgnores = type_ignores;
            return node;
        }

        public static GeneratedMod _PyAST_Interactive(GeneratedStmtSeq body)
        {
            var node = new GeneratedInteractive();
            node.Body = body;
            return node;
        }

        public static GeneratedMod _PyAST_Expression(GeneratedExpr body)
        {
            var node = new GeneratedExpression();
            node.Body = body;
            return node;
        }

        public static GeneratedMod _PyAST_FunctionType(GeneratedExprSeq argtypes, GeneratedExpr returns)
        {
            var node = new GeneratedFunctionType();
            node.Argtypes = argtypes;
            node.Returns = returns;
            return node;
        }

        public static GeneratedStmt _PyAST_FunctionDef(string name, GeneratedArguments args, GeneratedStmtSeq body, GeneratedExprSeq decorator_list, GeneratedExpr? returns, string? type_comment, GeneratedTypeParamSeq type_params, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedFunctionDef();
            node.Name = name;
            node.Args = args;
            node.Body = body;
            node.DecoratorList = decorator_list;
            node.Returns = returns;
            node.TypeComment = type_comment;
            node.TypeParams = type_params;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_FunctionDef(string name, GeneratedArguments args, GeneratedStmtSeq body, GeneratedExprSeq decorator_list, GeneratedTypeParamSeq type_params, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_FunctionDef(name, args, body, decorator_list, null, null, type_params, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt _PyAST_AsyncFunctionDef(string name, GeneratedArguments args, GeneratedStmtSeq body, GeneratedExprSeq decorator_list, GeneratedExpr? returns, string? type_comment, GeneratedTypeParamSeq type_params, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedAsyncFunctionDef();
            node.Name = name;
            node.Args = args;
            node.Body = body;
            node.DecoratorList = decorator_list;
            node.Returns = returns;
            node.TypeComment = type_comment;
            node.TypeParams = type_params;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_AsyncFunctionDef(string name, GeneratedArguments args, GeneratedStmtSeq body, GeneratedExprSeq decorator_list, GeneratedTypeParamSeq type_params, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_AsyncFunctionDef(name, args, body, decorator_list, null, null, type_params, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt _PyAST_ClassDef(string name, GeneratedExprSeq bases, GeneratedKeywordSeq keywords, GeneratedStmtSeq body, GeneratedExprSeq decorator_list, GeneratedTypeParamSeq type_params, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedClassDef();
            node.Name = name;
            node.Bases = bases;
            node.Keywords = keywords;
            node.Body = body;
            node.DecoratorList = decorator_list;
            node.TypeParams = type_params;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_Return(GeneratedExpr? value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedReturn();
            node.Value = value;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_Return(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_Return(null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt _PyAST_Delete(GeneratedExprSeq targets, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedDelete();
            node.Targets = targets;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_Assign(GeneratedExprSeq targets, GeneratedExpr value, string? type_comment, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedAssign();
            node.Targets = targets;
            node.Value = value;
            node.TypeComment = type_comment;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_Assign(GeneratedExprSeq targets, GeneratedExpr value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_Assign(targets, value, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt _PyAST_TypeAlias(GeneratedExpr name, GeneratedTypeParamSeq type_params, GeneratedExpr value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedTypeAlias();
            node.Name = name;
            node.TypeParams = type_params;
            node.Value = value;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_AugAssign(GeneratedExpr target, GeneratedOperator op, GeneratedExpr value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedAugAssign();
            node.Target = target;
            node.Op = op;
            node.Value = value;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_AnnAssign(GeneratedExpr target, GeneratedExpr annotation, GeneratedExpr? value, int simple, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedAnnAssign();
            node.Target = target;
            node.Annotation = annotation;
            node.Value = value;
            node.Simple = simple;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_AnnAssign(GeneratedExpr target, GeneratedExpr annotation, int simple, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_AnnAssign(target, annotation, null, simple, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt _PyAST_For(GeneratedExpr target, GeneratedExpr iter, GeneratedStmtSeq body, GeneratedStmtSeq orelse, string? type_comment, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedFor();
            node.Target = target;
            node.Iter = iter;
            node.Body = body;
            node.Orelse = orelse;
            node.TypeComment = type_comment;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_For(GeneratedExpr target, GeneratedExpr iter, GeneratedStmtSeq body, GeneratedStmtSeq orelse, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_For(target, iter, body, orelse, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt _PyAST_AsyncFor(GeneratedExpr target, GeneratedExpr iter, GeneratedStmtSeq body, GeneratedStmtSeq orelse, string? type_comment, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedAsyncFor();
            node.Target = target;
            node.Iter = iter;
            node.Body = body;
            node.Orelse = orelse;
            node.TypeComment = type_comment;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_AsyncFor(GeneratedExpr target, GeneratedExpr iter, GeneratedStmtSeq body, GeneratedStmtSeq orelse, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_AsyncFor(target, iter, body, orelse, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt _PyAST_While(GeneratedExpr test, GeneratedStmtSeq body, GeneratedStmtSeq orelse, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedWhile();
            node.Test = test;
            node.Body = body;
            node.Orelse = orelse;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_If(GeneratedExpr test, GeneratedStmtSeq body, GeneratedStmtSeq orelse, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedIf();
            node.Test = test;
            node.Body = body;
            node.Orelse = orelse;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_With(GeneratedWithitemSeq items, GeneratedStmtSeq body, string? type_comment, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedWith();
            node.Items = items;
            node.Body = body;
            node.TypeComment = type_comment;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_With(GeneratedWithitemSeq items, GeneratedStmtSeq body, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_With(items, body, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt _PyAST_AsyncWith(GeneratedWithitemSeq items, GeneratedStmtSeq body, string? type_comment, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedAsyncWith();
            node.Items = items;
            node.Body = body;
            node.TypeComment = type_comment;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_AsyncWith(GeneratedWithitemSeq items, GeneratedStmtSeq body, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_AsyncWith(items, body, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt _PyAST_Match(GeneratedExpr subject, GeneratedMatchCaseSeq cases, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedMatch();
            node.Subject = subject;
            node.Cases = cases;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_Raise(GeneratedExpr? exc, GeneratedExpr? cause, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedRaise();
            node.Exc = exc;
            node.Cause = cause;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_Raise(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_Raise(null, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt _PyAST_Try(GeneratedStmtSeq body, GeneratedExcepthandlerSeq handlers, GeneratedStmtSeq orelse, GeneratedStmtSeq finalbody, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedTry();
            node.Body = body;
            node.Handlers = handlers;
            node.Orelse = orelse;
            node.Finalbody = finalbody;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_TryStar(GeneratedStmtSeq body, GeneratedExcepthandlerSeq handlers, GeneratedStmtSeq orelse, GeneratedStmtSeq finalbody, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedTryStar();
            node.Body = body;
            node.Handlers = handlers;
            node.Orelse = orelse;
            node.Finalbody = finalbody;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_Assert(GeneratedExpr test, GeneratedExpr? msg, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedAssert();
            node.Test = test;
            node.Msg = msg;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_Assert(GeneratedExpr test, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_Assert(test, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt _PyAST_Import(GeneratedAliasSeq names, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedImport();
            node.Names = names;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_ImportFrom(string? module, GeneratedAliasSeq names, int? level, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedImportFrom();
            node.Module = module;
            node.Names = names;
            node.Level = level;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_ImportFrom(GeneratedAliasSeq names, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_ImportFrom(null, names, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt _PyAST_Global(GeneratedIdentifierSeq names, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedGlobal();
            node.Names = names;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_Nonlocal(GeneratedIdentifierSeq names, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedNonlocal();
            node.Names = names;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_Expr(GeneratedExpr value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedExprStmt();
            node.Value = value;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_Pass(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedPass.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_Break(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedBreak.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_Continue(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedContinue.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_BoolOp(GeneratedBoolop op, GeneratedExprSeq values, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedBoolOp();
            node.Op = op;
            node.Values = values;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_NamedExpr(GeneratedExpr target, GeneratedExpr value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedNamedExpr();
            node.Target = target;
            node.Value = value;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_BinOp(GeneratedExpr left, GeneratedOperator op, GeneratedExpr right, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedBinOp();
            node.Left = left;
            node.Op = op;
            node.Right = right;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_UnaryOp(GeneratedUnaryop op, GeneratedExpr operand, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedUnaryOp();
            node.Op = op;
            node.Operand = operand;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_Lambda(GeneratedArguments args, GeneratedExpr body, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedLambda();
            node.Args = args;
            node.Body = body;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_IfExp(GeneratedExpr test, GeneratedExpr body, GeneratedExpr orelse, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedIfExp();
            node.Test = test;
            node.Body = body;
            node.Orelse = orelse;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_Dict(GeneratedExprSeq keys, GeneratedExprSeq values, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedDict();
            node.Keys = keys;
            node.Values = values;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_Set(GeneratedExprSeq elts, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedSet();
            node.Elts = elts;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_ListComp(GeneratedExpr elt, GeneratedComprehensionSeq generators, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedListComp();
            node.Elt = elt;
            node.Generators = generators;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_SetComp(GeneratedExpr elt, GeneratedComprehensionSeq generators, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedSetComp();
            node.Elt = elt;
            node.Generators = generators;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_DictComp(GeneratedExpr key, GeneratedExpr value, GeneratedComprehensionSeq generators, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedDictComp();
            node.Key = key;
            node.Value = value;
            node.Generators = generators;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_GeneratorExp(GeneratedExpr elt, GeneratedComprehensionSeq generators, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedGeneratorExp();
            node.Elt = elt;
            node.Generators = generators;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_Await(GeneratedExpr value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedAwait();
            node.Value = value;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_Yield(GeneratedExpr? value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedYield();
            node.Value = value;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_Yield(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_Yield(null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedExpr _PyAST_YieldFrom(GeneratedExpr value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedYieldFrom();
            node.Value = value;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_Compare(GeneratedExpr left, GeneratedCmpopSeq ops, GeneratedExprSeq comparators, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedCompare();
            node.Left = left;
            node.Ops = ops;
            node.Comparators = comparators;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_Call(GeneratedExpr func, GeneratedExprSeq args, GeneratedKeywordSeq keywords, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedCall();
            node.Func = func;
            node.Args = args;
            node.Keywords = keywords;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_FormattedValue(GeneratedExpr value, int conversion, GeneratedExpr? format_spec, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedFormattedValue();
            node.Value = value;
            node.Conversion = conversion;
            node.FormatSpec = format_spec;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_FormattedValue(GeneratedExpr value, int conversion, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_FormattedValue(value, conversion, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedExpr _PyAST_JoinedStr(GeneratedExprSeq values, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedJoinedStr();
            node.Values = values;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_Constant(GeneratedPyConstant value, string? kind, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedConstant();
            node.Value = value;
            node.Kind = kind;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_Constant(GeneratedPyConstant value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_Constant(value, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedExpr _PyAST_Attribute(GeneratedExpr value, string attr, GeneratedExprContext ctx, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedAttribute();
            node.Value = value;
            node.Attr = attr;
            node.Ctx = ctx;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_Subscript(GeneratedExpr value, GeneratedExpr slice, GeneratedExprContext ctx, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedSubscript();
            node.Value = value;
            node.Slice = slice;
            node.Ctx = ctx;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_Starred(GeneratedExpr value, GeneratedExprContext ctx, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedStarred();
            node.Value = value;
            node.Ctx = ctx;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_Name(string id, GeneratedExprContext ctx, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedName();
            node.Id = id;
            node.Ctx = ctx;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_List(GeneratedExprSeq elts, GeneratedExprContext ctx, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedList();
            node.Elts = elts;
            node.Ctx = ctx;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_Tuple(GeneratedExprSeq elts, GeneratedExprContext ctx, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedTuple();
            node.Elts = elts;
            node.Ctx = ctx;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_Slice(GeneratedExpr? lower, GeneratedExpr? upper, GeneratedExpr? step, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedSlice();
            node.Lower = lower;
            node.Upper = upper;
            node.Step = step;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_Slice(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_Slice(null, null, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedExprContext _PyAST_Load(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedLoad.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExprContext _PyAST_Store(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedStore.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExprContext _PyAST_Del(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedDel.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedBoolop _PyAST_And(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedAnd.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedBoolop _PyAST_Or(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedOr.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedOperator _PyAST_Add(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedAdd.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedOperator _PyAST_Sub(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedSub.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedOperator _PyAST_Mult(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedMult.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedOperator _PyAST_MatMult(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedMatMult.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedOperator _PyAST_Div(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedDiv.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedOperator _PyAST_Mod(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedMod_.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedOperator _PyAST_Pow(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedPow.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedOperator _PyAST_LShift(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedLShift.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedOperator _PyAST_RShift(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedRShift.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedOperator _PyAST_BitOr(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedBitOr.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedOperator _PyAST_BitXor(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedBitXor.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedOperator _PyAST_BitAnd(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedBitAnd.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedOperator _PyAST_FloorDiv(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedFloorDiv.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedUnaryop _PyAST_Invert(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedInvert.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedUnaryop _PyAST_Not(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedNot.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedUnaryop _PyAST_UAdd(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedUAdd.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedUnaryop _PyAST_USub(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedUSub.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedCmpop _PyAST_Eq(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedEq.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedCmpop _PyAST_NotEq(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedNotEq.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedCmpop _PyAST_Lt(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedLt.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedCmpop _PyAST_LtE(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedLtE.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedCmpop _PyAST_Gt(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedGt.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedCmpop _PyAST_GtE(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedGtE.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedCmpop _PyAST_Is(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedIs.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedCmpop _PyAST_IsNot(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedIsNot.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedCmpop _PyAST_In(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedIn.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedCmpop _PyAST_NotIn(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedNotIn.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedComprehension _PyAST_comprehension(GeneratedExpr target, GeneratedExpr iter, GeneratedExprSeq ifs, int is_async)
        {
            var node = new GeneratedComprehension();
            node.Target = target;
            node.Iter = iter;
            node.Ifs = ifs;
            node.IsAsync = is_async;
            return node;
        }

        public static GeneratedExcepthandler _PyAST_ExceptHandler(GeneratedExpr? type, string? name, GeneratedStmtSeq body, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedExceptHandler();
            node.Type = type;
            node.Name = name;
            node.Body = body;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExcepthandler _PyAST_ExceptHandler(GeneratedStmtSeq body, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_ExceptHandler(null, null, body, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedArguments _PyAST_arguments(GeneratedArgSeq posonlyargs, GeneratedArgSeq args, GeneratedArg? vararg, GeneratedArgSeq kwonlyargs, GeneratedExprSeq kw_defaults, GeneratedArg? kwarg, GeneratedExprSeq defaults)
        {
            var node = new GeneratedArguments();
            node.Posonlyargs = posonlyargs;
            node.Args = args;
            node.Vararg = vararg;
            node.Kwonlyargs = kwonlyargs;
            node.KwDefaults = kw_defaults;
            node.Kwarg = kwarg;
            node.Defaults = defaults;
            return node;
        }

        public static GeneratedArguments _PyAST_arguments(GeneratedArgSeq posonlyargs, GeneratedArgSeq args, GeneratedArgSeq kwonlyargs, GeneratedExprSeq kw_defaults, GeneratedExprSeq defaults)
        {
            return _PyAST_arguments(posonlyargs, args, null, kwonlyargs, kw_defaults, null, defaults);
        }

        public static GeneratedArg _PyAST_arg(string arg, GeneratedExpr? annotation, string? type_comment, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedArg();
            node.Arg = arg;
            node.Annotation = annotation;
            node.TypeComment = type_comment;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedArg _PyAST_arg(string arg, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_arg(arg, null, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedKeyword _PyAST_keyword(string? arg, GeneratedExpr value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedKeyword();
            node.Arg = arg;
            node.Value = value;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedKeyword _PyAST_keyword(GeneratedExpr value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_keyword(null, value, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedAlias _PyAST_alias(string name, string? asname, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedAlias();
            node.Name = name;
            node.Asname = asname;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedAlias _PyAST_alias(string name, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_alias(name, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedWithitem _PyAST_withitem(GeneratedExpr context_expr, GeneratedExpr? optional_vars)
        {
            var node = new GeneratedWithitem();
            node.ContextExpr = context_expr;
            node.OptionalVars = optional_vars;
            return node;
        }

        public static GeneratedWithitem _PyAST_withitem(GeneratedExpr context_expr)
        {
            return _PyAST_withitem(context_expr, null);
        }

        public static GeneratedMatchCase _PyAST_match_case(GeneratedPattern pattern, GeneratedExpr? guard, GeneratedStmtSeq body)
        {
            var node = new GeneratedMatchCase();
            node.Pattern = pattern;
            node.Guard = guard;
            node.Body = body;
            return node;
        }

        public static GeneratedMatchCase _PyAST_match_case(GeneratedPattern pattern, GeneratedStmtSeq body)
        {
            return _PyAST_match_case(pattern, null, body);
        }

        public static GeneratedPattern _PyAST_MatchValue(GeneratedExpr value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedMatchValue();
            node.Value = value;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedPattern _PyAST_MatchSingleton(GeneratedPyConstant value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedMatchSingleton();
            node.Value = value;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedPattern _PyAST_MatchSequence(GeneratedPatternSeq patterns, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedMatchSequence();
            node.Patterns = patterns;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedPattern _PyAST_MatchMapping(GeneratedExprSeq keys, GeneratedPatternSeq patterns, string? rest, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedMatchMapping();
            node.Keys = keys;
            node.Patterns = patterns;
            node.Rest = rest;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedPattern _PyAST_MatchMapping(GeneratedExprSeq keys, GeneratedPatternSeq patterns, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_MatchMapping(keys, patterns, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedPattern _PyAST_MatchClass(GeneratedExpr cls, GeneratedPatternSeq patterns, GeneratedIdentifierSeq kwd_attrs, GeneratedPatternSeq kwd_patterns, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedMatchClass();
            node.Cls = cls;
            node.Patterns = patterns;
            node.KwdAttrs = kwd_attrs;
            node.KwdPatterns = kwd_patterns;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedPattern _PyAST_MatchStar(string? name, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedMatchStar();
            node.Name = name;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedPattern _PyAST_MatchStar(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_MatchStar(null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedPattern _PyAST_MatchAs(GeneratedPattern? pattern, string? name, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedMatchAs();
            node.Pattern = pattern;
            node.Name = name;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedPattern _PyAST_MatchAs(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_MatchAs(null, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedPattern _PyAST_MatchOr(GeneratedPatternSeq patterns, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedMatchOr();
            node.Patterns = patterns;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedTypeIgnore _PyAST_TypeIgnore(int lineno, string tag)
        {
            var node = new GeneratedTypeIgnoreNode();
            node.Lineno = lineno;
            node.Tag = tag;
            return node;
        }

        public static GeneratedTypeParam _PyAST_TypeVar(string name, GeneratedExpr? bound, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedTypeVar();
            node.Name = name;
            node.Bound = bound;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedTypeParam _PyAST_TypeVar(string name, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_TypeVar(name, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedTypeParam _PyAST_ParamSpec(string name, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedParamSpec();
            node.Name = name;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedTypeParam _PyAST_TypeVarTuple(string name, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedTypeVarTuple();
            node.Name = name;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }


    }

    // ============================================================
    // _PyPegen_* Helper Functions
    // ============================================================

    /// <summary>
    /// PEG parser helper functions - CPython 3.12: Parser/pegen.c
    /// </summary>
    public static partial class PegenHelpers
    {
        // CPython: _PyPegen_seq_flatten
        // Flatten list of sequences into single sequence
        // CPython 3.12: Returns NULL if total size is 0 (assert fails in CPython)
        public static GeneratedStmtSeq _PyPegen_seq_flatten(System.Collections.Generic.List<GeneratedStmtSeq> sequences)
        {
            // CPython: Calculate flattened size
            int totalSize = 0;
            foreach (var seq in sequences)
            {
                totalSize += seq.Count;
            }

            // CPython: assert(flattened_seq_size > 0)
            if (totalSize == 0)
            {
                return null;  // CPython would assert fail
            }

            // CPython: Allocate and flatten
            var result = new GeneratedStmtSeq(totalSize);
            foreach (var seq in sequences)
            {
                result.AddRange(seq);
            }
            return result;
        }

        // CPython: _PyPegen_singleton_seq
        public static GeneratedStmtSeq _PyPegen_singleton_seq(GeneratedStmt item)
        {
            var seq = new GeneratedStmtSeq(1);
            seq.Add(item);
            return seq;
        }

        public static GeneratedExprSeq _PyPegen_singleton_seq(GeneratedExpr item)
        {
            var seq = new GeneratedExprSeq(1);
            seq.Add(item);
            return seq;
        }

        public static GeneratedAliasSeq _PyPegen_singleton_seq(GeneratedAlias item)
        {
            var seq = new GeneratedAliasSeq(1);
            seq.Add(item);
            return seq;
        }

        // For grammar helper types like SlashWithDefault
        public static GeneratedSeq _PyPegen_singleton_seq(GeneratedSlashWithDefault item)
        {
            var seq = new GeneratedSeq(1);
            seq.Add(item);
            return seq;
        }

        // CPython 3.12: _PyPegen_slash_with_default
        // Creates a SlashWithDefault structure for positional-only parameters
        // plain_names: arguments without defaults (e.g., "self" in "self, other=()")
        // names_with_defaults: arguments with defaults (e.g., "other=()" -> arg + default value)
        public static GeneratedSlashWithDefault? _PyPegen_slash_with_default(
            IEnumerable<GeneratedArg>? plain_names,
            GeneratedSeq? names_with_defaults)
        {
            var slash_with_default = new GeneratedSlashWithDefault();

            // CPython: plain_names are args without defaults
            if (plain_names != null)
            {
                foreach (var arg in plain_names)
                {
                    slash_with_default.Args.Add(arg);
                }
            }

            // CPython: names_with_defaults is a seq of NameDefaultPair
            // Extract both args and defaults from NameDefaultPair
            if (names_with_defaults != null)
            {
                foreach (var item in names_with_defaults)
                {
                    if (item is GeneratedNameDefaultPair pair)
                    {
                        slash_with_default.Args.Add(pair.Arg);
                        slash_with_default.Defaults.Add(pair.Default);
                    }
                }
            }

            return slash_with_default;
        }

        // CPython: _PyPegen_seq_insert_in_front
        // Typed sequence versions - same type input and output
        public static GeneratedExprSeq _PyPegen_seq_insert_in_front(GeneratedExpr item, GeneratedExprSeq seq)
        {
            if (seq == null)
            {
                var singletonSeq = new GeneratedExprSeq();
                singletonSeq.Add(item);
                return singletonSeq;
            }
            var newSeq = new GeneratedExprSeq(seq.Count + 1);
            newSeq.Add(item);
            newSeq.AddRange(seq);
            return newSeq;
        }

        public static GeneratedPatternSeq _PyPegen_seq_insert_in_front(GeneratedPattern item, GeneratedPatternSeq seq)
        {
            if (seq == null)
            {
                var singletonSeq = new GeneratedPatternSeq();
                singletonSeq.Add(item);
                return singletonSeq;
            }
            var newSeq = new GeneratedPatternSeq(seq.Count + 1);
            newSeq.Add(item);
            newSeq.AddRange(seq);
            return newSeq;
        }

        // CPython 3.12: Generic version for asdl_seq* - matches CPython's void* signature
        public static GeneratedSeq _PyPegen_seq_insert_in_front(GeneratedPtr item, GeneratedSeq seq)
        {
            if (seq == null)
            {
                var singletonSeq = new GeneratedSeq();
                singletonSeq.Add(item);
                return singletonSeq;
            }
            var newSeq = new GeneratedSeq(seq.Count + 1);
            newSeq.Add(item);
            newSeq.AddRange(seq);
            return newSeq;
        }

        // CPython: _PyPegen_seq_count_dots
        public static int _PyPegen_seq_count_dots(GeneratedIdentifierSeq seq)
        {
            return seq?.Count ?? 0;
        }

        // CPython: _PyPegen_seq_count_dots (token list overload)
        public static int _PyPegen_seq_count_dots(System.Collections.Generic.List<GeneratedTokenInfo>? tokens)
        {
            if (tokens == null) return 0;
            int count = 0;
            foreach (var token in tokens)
            {
                // '.' counts as 1, '...' (ELLIPSIS) counts as 3
                if (token.Value == "...")
                    count += 3;
                else
                    count += 1;
            }
            return count;
        }

        // CPython: _PyPegen_map_names_to_ids
        // Extract identifier strings from NAME tokens
        public static GeneratedIdentifierSeq _PyPegen_map_names_to_ids(System.Collections.Generic.List<GeneratedTokenInfo> tokens)
        {
            var ids = new GeneratedIdentifierSeq(tokens.Count);
            foreach (var token in tokens)
            {
                ids.Add(token.Value ?? string.Empty);
            }
            return ids;
        }

        // CPython: _PyPegen_map_names_to_ids
        // Extract identifier strings from Name expressions
        public static GeneratedIdentifierSeq _PyPegen_map_names_to_ids(GeneratedExprSeq names)
        {
            var ids = new GeneratedIdentifierSeq(names.Count);
            foreach (var name in names)
            {
                if (name is GeneratedName nameExpr)
                {
                    ids.Add(nameExpr.Id);
                }
            }
            return ids;
        }

        // CPython: _PyPegen_alias_for_star
        public static GeneratedAlias _PyPegen_alias_for_star(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var alias = new GeneratedAlias();
            alias.Name = "*";
            alias.Asname = null;
            alias.LineNo = lineno;
            alias.ColOffset = col_offset;
            alias.EndLineNo = end_lineno ?? 0;
            alias.EndColOffset = end_col_offset ?? 0;
            return alias;
        }

        // CPython: _PyPegen_empty_arguments
        public static GeneratedArguments _PyPegen_empty_arguments()
        {
            var args = new GeneratedArguments();
            args.Posonlyargs = GeneratedArgSeq.Empty;
            args.Args = GeneratedArgSeq.Empty;
            args.Kwonlyargs = GeneratedArgSeq.Empty;
            args.KwDefaults = GeneratedExprSeq.Empty;
            args.Defaults = GeneratedExprSeq.Empty;
            return args;
        }

        // CPython: _PyPegen_set_expr_context
        public static GeneratedExpr _PyPegen_set_expr_context(GeneratedExpr expr, GeneratedExprContext ctx)
        {
            // Update expr context based on type
            switch (expr)
            {
                case GeneratedName name:
                    name.Ctx = ctx;
                    break;
                case GeneratedAttribute attr:
                    attr.Ctx = ctx;
                    break;
                case GeneratedSubscript subscript:
                    subscript.Ctx = ctx;
                    break;
                case GeneratedList list:
                    list.Ctx = ctx;
                    foreach (var elt in list.Elts.ToEnumerable<GeneratedExpr>())
                    {
                        _PyPegen_set_expr_context(elt, ctx);
                    }
                    break;
                case GeneratedTuple tuple:
                    tuple.Ctx = ctx;
                    foreach (var elt in tuple.Elts.ToEnumerable<GeneratedExpr>())
                    {
                        _PyPegen_set_expr_context(elt, ctx);
                    }
                    break;
                case GeneratedStarred starred:
                    starred.Ctx = ctx;
                    _PyPegen_set_expr_context(starred.Value, ctx);
                    break;
            }
            return expr;
        }

        // CPython: _PyPegen_make_module
        public static GeneratedModule _PyPegen_make_module(GeneratedStmtSeq body)
        {
            var module = new GeneratedModule();
            module.Body = body ?? GeneratedStmtSeq.Empty;
            module.TypeIgnores = GeneratedTypeIgnoreSeq.Empty;
            return module;
        }

        // Helper: Decode string literal (remove quotes, handle escapes)
        public static string DecodeStringLiteral(string literal)
        {
            if (string.IsNullOrEmpty(literal))
                return string.Empty;

            // Handle string prefixes: r, b, u, f, etc.
            var workingLiteral = literal;
            var isRaw = false;
            while (workingLiteral.Length > 0 && char.IsLetter(workingLiteral[0]))
            {
                var prefix = char.ToLower(workingLiteral[0]);
                if (prefix == 'r')
                    isRaw = true;
                workingLiteral = workingLiteral.Substring(1);
            }

            // Remove quotes: \"hello\" -> hello, 'world' -> world
            if (workingLiteral.Length >= 2)
            {
                // Triple-quoted strings
                if (workingLiteral.StartsWith("\"\"\"") || workingLiteral.StartsWith("'''"))
                {
                    if (workingLiteral.Length >= 6)
                        workingLiteral = workingLiteral.Substring(3, workingLiteral.Length - 6);
                }
                // Single/double-quoted strings
                else if (workingLiteral.StartsWith("\"") || workingLiteral.StartsWith("'"))
                {
                    workingLiteral = workingLiteral.Substring(1, workingLiteral.Length - 2);
                }
            }

            // Handle escape sequences (unless raw string)
            if (!isRaw && workingLiteral.Contains("\\"))
            {
                workingLiteral = workingLiteral
                    .Replace("\\n", "\n")
                    .Replace("\\t", "\t")
                    .Replace("\\r", "\r")
                    .Replace("\\\\", "\\")
                    .Replace("\\\"", "\"")
                    .Replace("\\'", "'");
            }

            return workingLiteral;
        }

        // CPython: _PyPegen_seq_append_to_end
        public static GeneratedExprSeq _PyPegen_seq_append_to_end(GeneratedExprSeq seq, GeneratedExpr item)
        {
            var newSeq = new GeneratedExprSeq(seq.Count + 1);
            newSeq.AddRange(seq);
            newSeq.Add(item);
            return newSeq;
        }

        // CPython: Py_None, Py_True, Py_False, Py_Ellipsis
        // Note: These are constant VALUES for AST construction, not runtime PyObjects
        public static GeneratedPyConstant Py_None => GeneratedPyConstant.None;
        public static GeneratedPyConstant Py_True => GeneratedPyConstant.True;
        public static GeneratedPyConstant Py_False => GeneratedPyConstant.False;
        public static GeneratedPyConstant Py_Ellipsis => GeneratedPyConstant.Ellipsis;

        // CPython: _PyPegen_dummy_name
        public static GeneratedName _PyPegen_dummy_name()
        {
            return new GeneratedName { Id = "_", Ctx = GeneratedStore.Instance };
        }

        // CPython: _PyPegen_keyword_or_starred
        // Construct a KeywordOrStarred
        public static GeneratedKeywordOrStarred _PyPegen_keyword_or_starred(object element, int is_keyword)
        {
            return new GeneratedKeywordOrStarred
            {
                Keyword = is_keyword != 0 ? element as GeneratedKeyword : null,
                Starred = is_keyword == 0 ? element as GeneratedExpr : null
            };
        }

        // CPython: _PyPegen_collect_call_seqs
        // Collect arguments and keywords from call sequences
        public static GeneratedExpr _PyPegen_collect_call_seqs(
            GeneratedExprSeq a,
            GeneratedSeq? b,
            int lineno, int col_offset, int end_lineno, int end_col_offset)
        {
            int args_len = a.Count;
            int total_len = args_len;

            if (b == null)
            {
                return new GeneratedCall
                {
                    Func = _PyPegen_dummy_name(),
                    Args = a,
                    Keywords = null,
                    LineNo = lineno,
                    ColOffset = col_offset,
                    EndLineNo = end_lineno,
                    EndColOffset = end_col_offset
                };
            }

            var starreds = _PyPegen_seq_extract_starred_exprs(b);
            var keywords = _PyPegen_seq_delete_starred_exprs(b);

            if (starreds != null)
            {
                total_len += starreds.Count;
            }

            var args = new GeneratedExprSeq();

            // Copy args from 'a'
            for (int i = 0; i < args_len; i++)
            {
                args.Add(a[i]);
            }

            // Append starred expressions
            if (starreds != null)
            {
                for (int i = 0; i < starreds.Count; i++)
                {
                    args.Add(starreds[i]);
                }
            }

            return new GeneratedCall
            {
                Func = _PyPegen_dummy_name(),
                Args = args,
                Keywords = keywords,
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        // CPython: _PyPegen_get_cmpops
        // asdl_int_seq *_PyPegen_get_cmpops(Parser *p, asdl_seq *seq)
        // Extract comparison operators from (cmpop, expr) pairs
        public static GeneratedCmpopSeq _PyPegen_get_cmpops(GeneratedSeq pairs)
        {
            if (pairs == null || pairs.Count == 0)
            {
                return new GeneratedCmpopSeq();
            }

            var ops = new GeneratedCmpopSeq();
            foreach (var item in pairs)
            {
                var pair = (GeneratedCmpopExprPair)item;
                ops.Add(pair.Cmpop);
            }
            return ops;
        }

        // Overload for AstNodeSeq
        // CPython 3.12: Type cast with shared reference (no copy)
        public static GeneratedCmpopSeq _PyPegen_get_cmpops(GeneratedAstNodeSeq pairs)
        {
            // CPython: implicit cast shares same PyObject** array
            return _PyPegen_get_cmpops(pairs.Cast<GeneratedSeq>());
        }

        // CPython: _PyPegen_get_exprs
        // asdl_expr_seq *_PyPegen_get_exprs(Parser *p, asdl_seq *seq)
        // Extract expressions from (cmpop, expr) pairs
        public static GeneratedExprSeq _PyPegen_get_exprs(GeneratedSeq pairs)
        {
            if (pairs == null || pairs.Count == 0)
            {
                return new GeneratedExprSeq();
            }

            var exprs = new GeneratedExprSeq();
            foreach (var item in pairs)
            {
                var pair = (GeneratedCmpopExprPair)item;
                exprs.Add(pair.Expr);
            }
            return exprs;
        }

        // Overload for AstNodeSeq
        // CPython 3.12: Type cast with shared reference (no copy)
        public static GeneratedExprSeq _PyPegen_get_exprs(GeneratedAstNodeSeq pairs)
        {
            // CPython: implicit cast shares same PyObject** array
            return _PyPegen_get_exprs(pairs.Cast<GeneratedSeq>());
        }

        // CPython: _PyPegen_key_value_pair
        // Create a key-value pair for dictionary literals
        public static GeneratedKeyValuePair _PyPegen_key_value_pair(GeneratedExpr key, GeneratedExpr value)
        {
            return new GeneratedKeyValuePair { Key = key ?? value, Value = value };
        }

        // CPython: _PyPegen_get_keys
        // Extract keys from (key, value) pairs
        public static GeneratedExprSeq _PyPegen_get_keys(GeneratedSeq pairs)
        {
            var keys = new GeneratedExprSeq();
            if (pairs != null)
            {
                foreach (var pair in pairs)
                {
                    if (pair is GeneratedKeyValuePair kvp)
                    {
                        keys.Add(kvp.Key);
                    }
                }
            }
            return keys;
        }

        // CPython: _PyPegen_get_values
        // Extract values from (key, value) pairs
        public static GeneratedExprSeq _PyPegen_get_values(GeneratedSeq pairs)
        {
            var values = new GeneratedExprSeq();
            if (pairs != null)
            {
                foreach (var pair in pairs)
                {
                    if (pair is GeneratedKeyValuePair kvp)
                    {
                        values.Add(kvp.Value);
                    }
                }
            }
            return values;
        }

        // CPython: _PyPegen_seq_extract_starred_exprs
        // Extract starred expressions from KeywordOrStarred sequence
        public static GeneratedExprSeq _PyPegen_seq_extract_starred_exprs(GeneratedKeywordOrStarredSeq seq)
        {
            var exprs = new GeneratedExprSeq();
            foreach (var item in seq.ToEnumerable<GeneratedKeywordOrStarred>())
            {
                if (item.Starred != null)
                {
                    exprs.Add(item.Starred);
                }
            }
            return exprs;
        }

        // CPython: _PyPegen_seq_delete_starred_exprs
        // Extract keywords (non-starred items) from KeywordOrStarred sequence
        public static GeneratedKeywordSeq _PyPegen_seq_delete_starred_exprs(GeneratedKeywordOrStarredSeq seq)
        {
            var keywords = new GeneratedKeywordSeq();
            foreach (var item in seq.ToEnumerable<GeneratedKeywordOrStarred>())
            {
                if (item.Keyword != null)
                {
                    keywords.Add(item.Keyword);
                }
            }
            return keywords;
        }

        // Overload for MixedSeq
        // CPython 3.12: Type cast shares same underlying array (no copy)
        public static GeneratedExprSeq _PyPegen_seq_extract_starred_exprs(GeneratedSeq seq)
        {
            // CPython: implicit cast shares same PyObject** array
            // C#: Cast<T>() shares same List<GeneratedPtr> via _initialize
            return _PyPegen_seq_extract_starred_exprs(seq.Cast<GeneratedKeywordOrStarredSeq>());
        }

        // CPython 3.12: Type cast with shared reference (no copy)
        public static GeneratedKeywordSeq _PyPegen_seq_delete_starred_exprs(GeneratedSeq seq)
        {
            // CPython: implicit cast shares same PyObject** array
            // C#: Cast<T>() shares same List<GeneratedPtr> via _initialize
            return _PyPegen_seq_delete_starred_exprs(seq.Cast<GeneratedKeywordOrStarredSeq>());
        }

        // Conversion: GeneratedAstNodeSeq to GeneratedSeq
        // CPython 3.12: Type cast with shared reference (no copy)
        public static GeneratedSeq ToMixedSeq(GeneratedAstNodeSeq inputSeq)
        {
            // CPython: implicit cast shares same PyObject** array
            // C#: Cast<T>() shares same List<GeneratedPtr> via _initialize
            return inputSeq.Cast<GeneratedSeq>();
        }

        // Conversion: GeneratedKeywordOrStarredSeq to GeneratedSeq
        // CPython 3.12: Type cast with shared reference (no copy)
        public static GeneratedSeq ToMixedSeq(GeneratedKeywordOrStarredSeq inputSeq)
        {
            // CPython: implicit cast shares same PyObject** array
            // C#: Cast<T>() shares same List<GeneratedPtr> via _initialize
            return inputSeq.Cast<GeneratedSeq>();
        }

        // CPython: _PyPegen_check_legacy_stmt
        // Check if NAME is 'print' or 'exec' (legacy Python 2 statements)
        public static bool CheckLegacyStmt(GeneratedExpr expr)
        {
            if (expr is not GeneratedName name)
            {
                return false;
            }

            var id = name.Id;
            return id == "print" || id == "exec";
        }

        // Legacy alias for backward compatibility
        public static bool _PyPegen_check_legacy_stmt(GeneratedExpr expr) => CheckLegacyStmt(expr);

        // CPython: RAISE_SYNTAX_ERROR_KNOWN_RANGE macro
        // Raise syntax error with range information
        public static GeneratedPtr RaiseSyntaxErrorKnownRange(GeneratedAstNode start, GeneratedAstNode end, string message)
        {
            // Extract location info from start/end nodes
            var startLine = start?.LineNo ?? 1;
            var startCol = start?.ColOffset ?? 0;
            var endLine = end?.EndLineNo ?? startLine;
            var endCol = end?.EndColOffset ?? startCol;

            // Format message with location info
            var fullMessage = $"{message} (line {startLine}, col {startCol})";
            throw new PySyntaxErrorException(fullMessage);
        }

        // CPython: CHECK(type, expr) macro
        // Null-check and cast - throws if null, otherwise returns typed result
        public static T CHECK<T>(T? value) where T : class
        {
            if (value == null)
            {
                throw new PySyntaxErrorException("CHECK failed: unexpected null value");
            }
            return value;
        }

        /// <summary>
        /// CPython 3.12: CHECK_NULL_ALLOWED - allows NULL return without error
        /// Used for helper functions like _PyPegen_seq_extract_starred_exprs
        /// Returns nullable type - NULL is valid if no error occurred
        /// </summary>
        public static T? CHECK_NULL_ALLOWED<T>(T? value) where T : class
        {
            // CPython: if (result == NULL && PyErr_Occurred()) p->error_indicator = 1;
            // In C#: We don't set error_indicator here, just return null
            // The caller is responsible for checking null and handling it
            return value;
        }

        // ==================== F-string Helper Methods ====================
        // CPython: _PyPegen_constant_from_token
        public static GeneratedExpr _PyPegen_constant_from_token(GeneratedTokenInfo tok)
        {
            return GeneratedParserBridge._PyPegen_constant_from_token(tok);
        }

        // CPython: _PyPegen_formatted_value
        public static GeneratedExpr _PyPegen_formatted_value(
            GeneratedExpr expr,
            GeneratedTokenInfo? debug_expr,
            GeneratedTokenInfo? conversion,
            GeneratedExpr? format_spec,
            GeneratedTokenInfo rbrace,
            int lineno, int col_offset, int end_lineno, int end_col_offset)
        {
            // Conversion: 's' = str(), 'r' = repr(), 'a' = ascii(), -1 = no conversion
            int conv = -1;
            if (conversion != null)
            {
                var convStr = conversion.GetStringValue();
                if (convStr == "s") conv = (int)'s';
                else if (convStr == "r") conv = (int)'r';
                else if (convStr == "a") conv = (int)'a';
            }

            return PyAst.FormattedValue(expr, conv, format_spec, lineno, col_offset, end_lineno, end_col_offset);
        }

        // ==================== Sequence Item Access Helpers ====================
        // CPython: macro-like helpers for accessing sequence items

        // CPython: #define PyPegen_last_item(seq, type) ((type) asdl_seq_GET(seq, asdl_seq_LEN(seq)-1))
        public static T PyPegen_last_item<T>(GeneratedSeq seq) where T : GeneratedPtr
        {
            if (seq == null || seq.Count == 0)
                throw new InvalidOperationException("Cannot get last item from empty sequence");
            return (T)seq[seq.Count - 1];
        }

        // CPython: #define PyPegen_first_item(seq, type) ((type) asdl_seq_GET(seq, 0))
        public static T PyPegen_first_item<T>(GeneratedSeq seq) where T : GeneratedPtr
        {
            if (seq == null || seq.Count == 0)
                throw new InvalidOperationException("Cannot get first item from empty sequence");
            return (T)seq[0];
        }

        // ==================== Expression Name Helpers ====================
        // CPython: _PyPegen_get_expr_name - Gets a string representation of expression for error messages
        public static string _PyPegen_get_expr_name(GeneratedExpr expr)
        {
            // CPython logic: Returns name representation based on expression type
            // For now, return a generic name - TODO: implement full logic
            if (expr is GeneratedName nameExpr)
            {
                return nameExpr.Id;
            }
            else if (expr is GeneratedAttribute attrExpr)
            {
                return "attribute";
            }
            else if (expr is GeneratedSubscript)
            {
                return "subscript";
            }
            else if (expr is GeneratedStarred)
            {
                return "starred";
            }
            else if (expr is GeneratedList)
            {
                return "list";
            }
            else if (expr is GeneratedTuple)
            {
                return "tuple";
            }
            else if (expr is GeneratedCall)
            {
                return "function call";
            }
            return "expression";
        }

        // ==================== Comprehension Helpers ====================
        // CPython: _PyPegen_get_last_comprehension_item
        public static GeneratedComprehension _PyPegen_get_last_comprehension_item(GeneratedComprehension comp)
        {
            return comp; // Just returns the same item (used for error location tracking)
        }

        // ==================== Error Raising Helpers ====================
        // CPython: _PyPegen_nonparen_genexp_in_call - Raise error for non-parenthesized generator expression in call
        public static GeneratedArguments? _PyPegen_nonparen_genexp_in_call(GeneratedExprSeq? args, GeneratedSeq for_if_clauses)
        {
            // TODO: Raise proper syntax error
            // RaiseSyntaxError("Generator expression must be parenthesized");
            return null;
        }

        // CPython: _PyPegen_arguments_parsing_error - Raise error for invalid arguments
        public static GeneratedArguments? _PyPegen_arguments_parsing_error(GeneratedExprSeq args)
        {
            // TODO: Raise proper syntax error
            // RaiseSyntaxError("Invalid arguments");
            return null;
        }

    }


    // ============================================================
    // Parser Helper Types
    // CPython 3.12: Parser-specific types (not in ASDL)
    // Note: Some types (SlashWithDefault, StarEtc, KeywordOrStarred, KeyValuePair) are already defined in GeneratedAstTypes.cs
    // ============================================================

    public class GeneratedNameDefaultPair : GeneratedPtr
    {
        public GeneratedArg Arg { get; set; }
        public GeneratedExpr Default { get; set; }
        public string? TypeComment { get; set; }
    }

    public class GeneratedKeyPatternPair : GeneratedPtr
    {
        public GeneratedExpr Key { get; set; }
        public GeneratedPattern Pattern { get; set; }
    }

    public class GeneratedKeyPatternPairSeq : GeneratedSeq
    {
    }

    public class GeneratedNameDefaultPairSeq : GeneratedSeq
    {
        public GeneratedNameDefaultPairSeq() { }
        public GeneratedNameDefaultPairSeq(int capacity) : base(capacity) { }
    }

    public class GeneratedCmpopExprPair : GeneratedPtr
    {
        public GeneratedCmpop Cmpop { get; set; }  // CPython: cmpop_ty cmpop
        public GeneratedExpr Expr { get; set; }    // CPython: expr_ty expr
    }

    public class GeneratedResultTokenWithMetadata : GeneratedPtr
    {
        public GeneratedTokenInfo Token { get; set; }
        public object? Metadata { get; set; }
    }

    // ============================================================
    // ASTHelpers - Utility functions
    // ============================================================

    /// <summary>
    /// Helper functions for AST manipulation
    /// </summary>
    public static class ASTHelpers
    {
        public static string ExtractStringValue(GeneratedExpr expr)
        {
            if (expr is GeneratedName name)
            {
                return name.Id;
            }
            throw new InvalidOperationException("Expected Name expression");
        }

        public static GeneratedOperator ExtractOpKind(GeneratedAstNode op)
        {
            // Extract operator kind from AST node
            if (op is GeneratedOperator opNode) return opNode;
            // TODO: Implement more comprehensive operator extraction
            return GeneratedAdd.Instance;
        }

        public static GeneratedExprSeq ExtractCallArgs(GeneratedExpr call)
        {
            if (call is GeneratedCall callExpr)
            {
                return callExpr.Args;
            }
            return GeneratedExprSeq.Empty;
        }

        public static GeneratedKeywordSeq ExtractCallKeywords(GeneratedExpr call)
        {
            if (call is GeneratedCall callExpr)
            {
                return callExpr.Keywords;
            }
            return GeneratedKeywordSeq.Empty;
        }

        // CPython: _PyPegen_add_type_comment_to_arg
        // action_helpers.c: Add type comment to argument (Python 2 legacy)
        public static GeneratedArg _PyPegen_add_type_comment_to_arg(GeneratedArg arg, GeneratedTokenInfo tc)
        {
            // If no type comment, return arg as-is
            if (tc == null)
            {
                return arg;
            }
            // Type comments are Python 2 legacy feature
            // CPython 3.12 parses them but mostly ignores them
            // For now, we return the arg unchanged
            return arg;
        }

        // CPython: _PyPegen_check_barry_as_flufl
        // Easter egg: from __future__ import barry_as_BDFL
        // Returns 0 (false) if token is '!=', non-zero (true) otherwise
        public static bool _PyPegen_check_barry_as_flufl(GeneratedTokenInfo tok)
        {
            // SharpPy doesn't implement barry_as_BDFL flag
            // CPython: return strcmp(tok_str, "!=")
            // strcmp returns 0 if equal, non-zero if different
            // In C#: return false if tok is "!=", true otherwise
            return tok.Value != "!=";
        }

        // ============================================================
        // Sequence Helper Functions
        // ============================================================

        // CPython action_helpers.c:97
        // void *_PyPegen_seq_last_item(asdl_seq *seq)
        public static GeneratedPtr _PyPegen_seq_last_item<T>(T seq) where T : List<GeneratedPtr>
        {
            if (seq == null || seq.Count == 0) return null!;
            return seq[seq.Count - 1];
        }

        // CPython action_helpers.c:102
        // void *_PyPegen_seq_first_item(asdl_seq *seq)
        public static GeneratedPtr _PyPegen_seq_first_item<T>(T seq) where T : List<GeneratedPtr>
        {
            if (seq == null || seq.Count == 0) return null!;
            return seq[0];
        }

        // CPython pegen.h:253
        // #define PyPegen_last_item(seq, type) ((type)_PyPegen_seq_last_item((asdl_seq*)seq))
        public static TResult PyPegen_last_item<TSeq, TResult>(TSeq seq)
            where TSeq : List<GeneratedPtr>
            where TResult : GeneratedPtr
        {
            return (TResult)_PyPegen_seq_last_item(seq);
        }

        // CPython pegen.h:255
        // #define PyPegen_first_item(seq, type) ((type)_PyPegen_seq_first_item((asdl_seq*)seq))
        public static TResult PyPegen_first_item<TSeq, TResult>(TSeq seq)
            where TSeq : List<GeneratedPtr>
            where TResult : GeneratedPtr
        {
            return (TResult)_PyPegen_seq_first_item(seq);
        }

        // ============================================================
        // Expression Name Helper
        // ============================================================

        // CPython action_helpers.c:945
        // const char *_PyPegen_get_expr_name(expr_ty e)
        public static string _PyPegen_get_expr_name(GeneratedExpr e)
        {
            if (e == null) return "expression";

            return e switch
            {
                GeneratedAttribute => "attribute",
                GeneratedSubscript => "subscript",
                GeneratedStarred => "starred",
                GeneratedName => "name",
                GeneratedList => "list",
                GeneratedTuple => "tuple",
                GeneratedLambda => "lambda",
                GeneratedCall => "function call",
                GeneratedBoolOp => "expression",
                GeneratedBinOp => "expression",
                GeneratedUnaryOp => "expression",
                GeneratedGeneratorExp => "generator expression",
                GeneratedYield => "yield expression",
                GeneratedYieldFrom => "yield expression",
                GeneratedAwait => "await expression",
                GeneratedListComp => "list comprehension",
                GeneratedSetComp => "set comprehension",
                GeneratedDictComp => "dict comprehension",
                GeneratedDict => "dict literal",
                GeneratedSet => "set display",
                GeneratedJoinedStr => "f-string expression",
                GeneratedFormattedValue => "f-string expression",
                GeneratedConstant c => GetConstantName(c),
                _ => "expression"
            };
        }

        private static string GetConstantName(GeneratedConstant c)
        {
            if (c.Value == null) return "None";
            var pyConstant = c.Value as GeneratedPyConstant;
            if (pyConstant is GeneratedPyConstantBool b)
            {
                return b.Value ? "True" : "False";
            }
            if (pyConstant is GeneratedPyConstantEllipsis) return "ellipsis";
            return "literal";
        }

        // ============================================================
        // Comprehension Helpers
        // ============================================================

        // CPython action_helpers.c:1018
        // expr_ty _PyPegen_get_last_comprehension_item(comprehension_ty comprehension)
        public static GeneratedExpr _PyPegen_get_last_comprehension_item(GeneratedComprehension comprehension)
        {
            if (comprehension == null) return null!;
            if (comprehension.Ifs == null || comprehension.Ifs.Count == 0)
            {
                return comprehension.Iter;
            }
            return (GeneratedExpr)comprehension.Ifs[comprehension.Ifs.Count - 1];
        }

        // ============================================================
        // Error Handling Functions
        // ============================================================

        // CPython action_helpers.c:1118
        // void *_PyPegen_arguments_parsing_error(Parser *p, expr_ty e)
        // Note: Made internal so PyParser can access it
        internal static GeneratedPtr _PyPegen_arguments_parsing_error(dynamic p, GeneratedExpr e)
        {
            if (e == null || !(e is GeneratedCall call)) return null!;

            bool kwarg_unpacking = false;
            if (call.Keywords != null)
            {
                foreach (var keyword in call.Keywords)
                {
                    var kw = keyword as GeneratedKeyword;
                    if (kw != null && kw.Arg == null)
                    {
                        kwarg_unpacking = true;
                        break;
                    }
                }
            }

            string msg = kwarg_unpacking
                ? "positional argument follows keyword argument unpacking"
                : "positional argument follows keyword argument";

            return p.RaiseSyntaxError(msg);
        }

        // CPython action_helpers.c:1137
        // void *_PyPegen_nonparen_genexp_in_call(Parser *p, expr_ty args, asdl_comprehension_seq *comprehensions)
        // Note: Made internal so PyParser can access it
        internal static GeneratedPtr _PyPegen_nonparen_genexp_in_call(dynamic p, GeneratedExpr args,
                                                                      GeneratedComprehensionSeq comprehensions)
        {
            if (!(args is GeneratedCall call)) return null!;
            if (call.Args == null) return null!;

            int len = call.Args.Count;
            if (len <= 1) return null!;

            var last_comprehension = (GeneratedComprehension)comprehensions[comprehensions.Count - 1];
            var lastArg = (GeneratedExpr)call.Args[len - 1];
            var lastItem = _PyPegen_get_last_comprehension_item(last_comprehension);

            return p.RaiseSyntaxErrorKnownRange(lastArg, lastItem,
                "Generator expression must be parenthesized");
        }

    }

    // ============================================================
    // PyAst - C# Style AST Factory Functions
    // ============================================================

    /// <summary>
    /// C# style AST factory - wraps AstFactory._PyAST_* functions
    /// Usage in python_cs.gram: PyAst.Interactive(a) instead of _PyAST_Interactive(a)
    /// </summary>
    public static class PyAst
    {
        /// <summary>
        /// CPython: _PyAST_Interactive
        /// Creates Interactive mode AST (REPL)
        /// </summary>
        public static GeneratedMod Interactive(GeneratedStmtSeq statements)
        {
            return AstFactory._PyAST_Interactive(statements);
        }

        /// <summary>
        /// CPython: _PyAST_Expression
        /// Creates Expression mode AST (eval)
        /// </summary>
        public static GeneratedMod Expression(GeneratedExpr expr)
        {
            return AstFactory._PyAST_Expression(expr);
        }

        /// <summary>
        /// CPython: _PyAST_FunctionType
        /// Creates FunctionType mode AST (function type comment)
        /// </summary>
        public static GeneratedMod FunctionType(GeneratedExprSeq? argTypes, GeneratedExpr returnType)
        {
            var args = argTypes ?? new GeneratedExprSeq();
            return AstFactory._PyAST_FunctionType(args, returnType);
        }

        /// <summary>
        /// CPython: _PyAST_Pass
        /// Creates Pass statement
        /// </summary>
        public static GeneratedStmt Pass(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_Pass(lineno, col_offset, end_lineno, end_col_offset);
        }

        /// <summary>
        /// CPython: _PyAST_Break
        /// Creates Break statement
        /// </summary>
        public static GeneratedStmt Break(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_Break(lineno, col_offset, end_lineno, end_col_offset);
        }

        /// <summary>
        /// CPython: _PyAST_Continue
        /// Creates Continue statement
        /// </summary>
        public static GeneratedStmt Continue(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_Continue(lineno, col_offset, end_lineno, end_col_offset);
        }

        // ============================================================
        // Expression Node Wrappers
        // ============================================================

        public static GeneratedExpr BinOp(GeneratedExpr left, GeneratedOperator op, GeneratedExpr right,
                                          int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_BinOp(left, op, right, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedExpr UnaryOp(GeneratedUnaryop op, GeneratedExpr operand,
                                            int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_UnaryOp(op, operand, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedExpr BoolOp(GeneratedBoolop op, GeneratedExprSeq values,
                                           int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_BoolOp(op, values, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedExpr Compare(GeneratedExpr left, GeneratedCmpopSeq ops, GeneratedExprSeq comparators,
                                            int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_Compare(left, ops, comparators, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedExpr Call(GeneratedExpr func, GeneratedExprSeq args, GeneratedKeywordSeq keywords,
                                         int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_Call(func, args, keywords, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedExpr Constant(GeneratedPyConstant value, string? kind,
                                             int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_Constant(value, kind, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedExpr Attribute(GeneratedExpr value, GeneratedIdentifier attr, GeneratedExprContext ctx,
                                              int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_Attribute(value, attr, ctx, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedExpr Subscript(GeneratedExpr value, GeneratedExpr slice, GeneratedExprContext ctx,
                                              int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_Subscript(value, slice, ctx, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedExpr Starred(GeneratedExpr value, GeneratedExprContext ctx,
                                            int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_Starred(value, ctx, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedExpr List(GeneratedExprSeq elts, GeneratedExprContext ctx,
                                         int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_List(elts, ctx, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedExpr Tuple(GeneratedExprSeq elts, GeneratedExprContext ctx,
                                          int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_Tuple(elts, ctx, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedExpr Dict(GeneratedExprSeq keys, GeneratedExprSeq values,
                                         int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_Dict(keys, values, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedExpr Set(GeneratedExprSeq elts,
                                        int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_Set(elts, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedExpr NamedExpr(GeneratedExpr target, GeneratedExpr value,
                                              int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_NamedExpr(target, value, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedExpr Lambda(GeneratedArguments args, GeneratedExpr body,
                                           int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_Lambda(args, body, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedExpr IfExp(GeneratedExpr test, GeneratedExpr body, GeneratedExpr orelse,
                                          int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_IfExp(test, body, orelse, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedExpr ListComp(GeneratedExpr elt, GeneratedComprehensionSeq generators,
                                             int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_ListComp(elt, generators, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedExpr SetComp(GeneratedExpr elt, GeneratedComprehensionSeq generators,
                                            int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_SetComp(elt, generators, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedExpr DictComp(GeneratedExpr key, GeneratedExpr value, GeneratedComprehensionSeq generators,
                                             int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_DictComp(key, value, generators, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedExpr GeneratorExp(GeneratedExpr elt, GeneratedComprehensionSeq generators,
                                                 int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_GeneratorExp(elt, generators, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedExpr Await(GeneratedExpr value,
                                          int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_Await(value, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedExpr Yield(GeneratedExpr value,
                                          int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_Yield(value, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedExpr YieldFrom(GeneratedExpr value,
                                              int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_YieldFrom(value, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedExpr Slice(GeneratedExpr lower, GeneratedExpr upper, GeneratedExpr step,
                                          int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_Slice(lower, upper, step, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedExpr FormattedValue(GeneratedExpr value, int conversion, GeneratedExpr format_spec,
                                                   int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_FormattedValue(value, conversion, format_spec, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedExpr JoinedStr(GeneratedExprSeq values,
                                              int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_JoinedStr(values, lineno, col_offset, end_lineno, end_col_offset);
        }

        // ============================================================
        // Statement Node Wrappers
        // ============================================================

        public static GeneratedStmt Expr(GeneratedExpr value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_Expr(value, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt Assign(GeneratedExprSeq targets, GeneratedExpr value, string? type_comment,
                                           int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_Assign(targets, value, type_comment, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt AnnAssign(GeneratedExpr target, GeneratedExpr annotation, GeneratedExpr value, int simple,
                                              int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_AnnAssign(target, annotation, value, simple, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt AugAssign(GeneratedExpr target, GeneratedOperator op, GeneratedExpr value,
                                              int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_AugAssign(target, op, value, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt Return(GeneratedExpr value,
                                           int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_Return(value, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt Raise(GeneratedExpr exc, GeneratedExpr cause,
                                          int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_Raise(exc, cause, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt Delete(GeneratedExprSeq targets,
                                           int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_Delete(targets, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt If(GeneratedExpr test, GeneratedStmtSeq body, GeneratedStmtSeq orelse,
                                       int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_If(test, body, orelse, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt While(GeneratedExpr test, GeneratedStmtSeq body, GeneratedStmtSeq orelse,
                                          int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_While(test, body, orelse, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt For(GeneratedExpr target, GeneratedExpr iter, GeneratedStmtSeq body, GeneratedStmtSeq orelse, string? type_comment,
                                        int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_For(target, iter, body, orelse, type_comment, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt AsyncFor(GeneratedExpr target, GeneratedExpr iter, GeneratedStmtSeq body, GeneratedStmtSeq orelse, string? type_comment,
                                             int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_AsyncFor(target, iter, body, orelse, type_comment, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt With(GeneratedWithitemSeq items, GeneratedStmtSeq body, string? type_comment,
                                         int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_With(items, body, type_comment, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt AsyncWith(GeneratedWithitemSeq items, GeneratedStmtSeq body, string? type_comment,
                                              int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_AsyncWith(items, body, type_comment, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt FunctionDef(GeneratedIdentifier name, GeneratedArguments args, GeneratedStmtSeq body,
                                                GeneratedExprSeq decorator_list, GeneratedExpr returns, string? type_comment, GeneratedTypeParamSeq type_params,
                                                int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_FunctionDef(name, args, body, decorator_list, returns, type_comment, type_params, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt AsyncFunctionDef(GeneratedIdentifier name, GeneratedArguments args, GeneratedStmtSeq body,
                                                     GeneratedExprSeq decorator_list, GeneratedExpr returns, string? type_comment, GeneratedTypeParamSeq type_params,
                                                     int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_AsyncFunctionDef(name, args, body, decorator_list, returns, type_comment, type_params, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt ClassDef(GeneratedIdentifier name, GeneratedExprSeq bases, GeneratedKeywordSeq keywords, GeneratedStmtSeq body,
                                             GeneratedExprSeq decorator_list, GeneratedTypeParamSeq type_params,
                                             int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_ClassDef(name, bases, keywords, body, decorator_list, type_params, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt Try(GeneratedStmtSeq body, GeneratedExcepthandlerSeq handlers, GeneratedStmtSeq orelse, GeneratedStmtSeq finalbody,
                                        int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_Try(body, handlers, orelse, finalbody, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt TryStar(GeneratedStmtSeq body, GeneratedExcepthandlerSeq handlers, GeneratedStmtSeq orelse, GeneratedStmtSeq finalbody,
                                            int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_TryStar(body, handlers, orelse, finalbody, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt Assert(GeneratedExpr test, GeneratedExpr msg,
                                           int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_Assert(test, msg, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt Import(GeneratedAliasSeq names,
                                           int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_Import(names, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt ImportFrom(GeneratedIdentifier module, GeneratedAliasSeq names, int level,
                                               int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_ImportFrom(module, names, level, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt Global(GeneratedIdentifierSeq names,
                                           int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_Global(names, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt Nonlocal(GeneratedIdentifierSeq names,
                                             int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_Nonlocal(names, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt Match(GeneratedExpr subject, GeneratedMatchCaseSeq cases,
                                          int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_Match(subject, cases, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt TypeAlias(GeneratedExpr name, GeneratedTypeParamSeq type_params, GeneratedExpr value,
                                              int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_TypeAlias(name, type_params, value, lineno, col_offset, end_lineno, end_col_offset);
        }

        // ============================================================
        // Other Node Type Wrappers
        // ============================================================

        public static GeneratedAlias Alias(GeneratedIdentifier name, GeneratedIdentifier asname,
                                           int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_alias(name, asname, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedWithitem Withitem(GeneratedExpr context_expr, GeneratedExpr optional_vars)
        {
            return AstFactory._PyAST_withitem(context_expr, optional_vars);
        }

        public static GeneratedMatchCase MatchCase(GeneratedPattern pattern, GeneratedExpr guard, GeneratedStmtSeq body)
        {
            return AstFactory._PyAST_match_case(pattern, guard, body);
        }

        public static GeneratedPattern MatchValue(GeneratedExpr value,
                                                  int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_MatchValue(value, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedPattern MatchSingleton(GeneratedPyConstant value,
                                                      int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_MatchSingleton(value, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedPattern MatchSequence(GeneratedPatternSeq patterns,
                                                     int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_MatchSequence(patterns, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedPattern MatchMapping(GeneratedExprSeq keys, GeneratedPatternSeq patterns, GeneratedIdentifier rest,
                                                    int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_MatchMapping(keys, patterns, rest, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedPattern MatchClass(GeneratedExpr cls, GeneratedPatternSeq patterns, GeneratedIdentifierSeq kwd_attrs, GeneratedPatternSeq kwd_patterns,
                                                  int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_MatchClass(cls, patterns, kwd_attrs, kwd_patterns, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedPattern MatchStar(GeneratedIdentifier name,
                                                 int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_MatchStar(name, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedPattern MatchAs(GeneratedPattern pattern, GeneratedIdentifier name,
                                               int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_MatchAs(pattern, name, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedPattern MatchOr(GeneratedPatternSeq patterns,
                                               int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_MatchOr(patterns, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedArg Arg(GeneratedIdentifier arg, GeneratedExpr annotation, string? type_comment,
                                       int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_arg(arg, annotation, type_comment, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedKeyword Keyword(GeneratedIdentifier arg, GeneratedExpr value,
                                               int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_keyword(arg, value, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedComprehension Comprehension(GeneratedExpr target, GeneratedExpr iter, GeneratedExprSeq ifs, int is_async)
        {
            return AstFactory._PyAST_comprehension(target, iter, ifs, is_async);
        }

        public static GeneratedExcepthandler ExceptHandler(GeneratedExpr type, GeneratedIdentifier name, GeneratedStmtSeq body,
                                                           int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_ExceptHandler(type, name, body, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedArguments Arguments(GeneratedArgSeq posonlyargs, GeneratedArgSeq args, GeneratedArg vararg,
                                                   GeneratedArgSeq kwonlyargs, GeneratedExprSeq kw_defaults, GeneratedArg kwarg, GeneratedExprSeq defaults)
        {
            return AstFactory._PyAST_arguments(posonlyargs, args, vararg, kwonlyargs, kw_defaults, kwarg, defaults);
        }

        public static GeneratedTypeParam TypeVar(GeneratedIdentifier name, GeneratedExpr bound,
                                                 int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_TypeVar(name, bound, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedTypeParam ParamSpec(GeneratedIdentifier name,
                                                   int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_ParamSpec(name, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedTypeParam TypeVarTuple(GeneratedIdentifier name,
                                                      int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AstFactory._PyAST_TypeVarTuple(name, lineno, col_offset, end_lineno, end_col_offset);
        }

        // TODO: Add more AST factory methods as needed during grammar rewriting
    }

    // ============================================================
    // PyParserHelpers - C# Style Parser Helper Functions
    // ============================================================

    /// <summary>
    /// C# style parser helpers - wraps PegenHelpers._PyPegen_* functions
    /// Usage in python_cs.gram: PyParserHelpers.MakeModule(a) instead of _PyPegen_make_module(a)
    /// </summary>
    public static class PyParserHelpers
    {
        /// <summary>
        /// CPython: _PyPegen_make_module
        /// Creates a Module with optional statements
        /// </summary>
        public static GeneratedMod MakeModule(GeneratedStmtSeq? statements)
        {
            var body = statements ?? new GeneratedStmtSeq();
            var typeIgnores = new GeneratedTypeIgnoreSeq();
            return AstFactory._PyAST_Module(body, typeIgnores);
        }

        /// <summary>
        /// CPython: _PyPegen_seq_flatten
        /// Flattens nested statement sequences
        /// </summary>
        public static GeneratedStmtSeq FlattenStatementSequence(GeneratedSeq sequences)
        {
            return PegenHelpers._PyPegen_seq_flatten(
                sequences.ToCastList<GeneratedStmtSeq>()
            );
        }

        /// <summary>
        /// CPython: _PyPegen_singleton_seq
        /// Creates a sequence with a single item
        /// </summary>
        public static GeneratedStmtSeq SingletonSequence(GeneratedStmt item)
        {
            return PegenHelpers._PyPegen_singleton_seq(item);
        }

        /// <summary>
        /// CPython: _PyPegen_seq_insert_in_front
        /// Inserts item at the front of sequence
        /// </summary>
        public static GeneratedExprSeq InsertInFront(GeneratedExpr item, GeneratedExprSeq? seq)
        {
            return PegenHelpers._PyPegen_seq_insert_in_front(item, seq);
        }

        /// <summary>
        /// CPython: _PyPegen_set_expr_context
        /// Sets the context of an expression (Load, Store, Del)
        /// </summary>
        public static GeneratedExpr SetExprContext(GeneratedExpr expr, GeneratedExprContext ctx)
        {
            return PegenHelpers._PyPegen_set_expr_context(expr, ctx);
        }

        /// <summary>
        /// CPython: _PyPegen_interactive_exit
        /// Returns empty statement sequence for interactive mode exit (ENDMARKER)
        /// Used in statement_newline rule when encountering ENDMARKER
        /// </summary>
        public static GeneratedStmtSeq InteractiveExit()
        {
            // CPython: Returns empty sequence to signal end of interactive input
            return GeneratedParserBridge._PyPegen_interactive_exit();
        }

        /// <summary>
        /// CPython: Maps augmented assignment operator token to GeneratedOperator
        /// Used in augassign rule: augassign[GeneratedOperator]: '+=' { AugOperator(Add) }
        /// </summary>
        public static GeneratedOperator AugOperator(GeneratedOperator op)
        {
            return op;
        }

        /// <summary>
        /// CPython: _PyPegen_make_arguments
        /// Creates function arguments from parsed parameter components
        /// </summary>
        public static GeneratedArguments MakeArguments(
            GeneratedArgSeq? posonly,
            GeneratedSlashWithDefault? posonly_with_default,
            GeneratedArgSeq? args,
            GeneratedNameDefaultPairSeq? args_with_default,
            GeneratedStarEtc? star_etc)
        {
            return GeneratedParserBridge._PyPegen_make_arguments(
                posonly, posonly_with_default, args, args_with_default, star_etc);
        }

        /// <summary>
        /// CPython: _PyPegen_empty_arguments
        /// Creates empty arguments for function with no parameters
        /// </summary>
        public static GeneratedArguments EmptyArguments()
        {
            return GeneratedParserBridge._PyPegen_empty_arguments();
        }

        /// <summary>
        /// CPython: SlashWithDefault helper structure
        /// Represents positional-only parameters with defaults
        /// </summary>
        public static GeneratedSlashWithDefault SlashWithDefault(
            GeneratedArgSeq? plain_names,
            GeneratedNameDefaultPairSeq names_with_defaults)
        {
            return GeneratedParserBridge._PyPegen_slash_with_default(plain_names, names_with_defaults);
        }

        /// <summary>
        /// CPython: StarEtc helper structure
        /// Represents *args and **kwargs parameters
        /// </summary>
        public static GeneratedStarEtc StarEtc(
            GeneratedArg? vararg,
            GeneratedNameDefaultPairSeq? kwonly_args,
            GeneratedArg? kwarg)
        {
            return GeneratedParserBridge._PyPegen_star_etc(vararg, kwonly_args, kwarg);
        }

        /// <summary>
        /// CPython: _PyPegen_seq_count_dots
        /// Counts dots in import from statement (for relative imports)
        /// </summary>
        public static int SeqCountDots(List<GeneratedPtr> seq)
        {
            // Convert List<GeneratedPtr> to GeneratedSeq
            GeneratedSeq seq_wrapper = null;
            if (seq != null && seq.Count > 0)
            {
                seq_wrapper = new GeneratedSeq(seq.Count);
                foreach (var item in seq)
                {
                    seq_wrapper.Add(item);
                }
            }

            return GeneratedParserBridge._PyPegen_seq_count_dots(seq_wrapper);
        }

        /// <summary>
        /// CPython: _PyPegen_alias_for_star
        /// Creates alias for 'from module import *'
        /// </summary>
        public static GeneratedAlias AliasForStar(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return GeneratedParserBridge._PyPegen_alias_for_star(lineno, col_offset, end_lineno, end_col_offset);
        }

        /// <summary>
        /// CPython: _PyPegen_check_future_import
        /// Validates and creates __future__ import
        /// </summary>
        public static GeneratedStmt CheckedFutureImport(
            GeneratedIdentifier module_name,
            GeneratedAliasSeq names,
            int level,
            int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return GeneratedParserBridge._PyPegen_checked_future_import(
                module_name, names, level, lineno, col_offset, end_lineno ?? lineno, end_col_offset ?? col_offset);
        }

        /// <summary>
        /// CPython: _PyPegen_join_names_with_dot
        /// Joins dotted name components (for import statements)
        /// </summary>
        public static GeneratedExpr JoinNamesWithDot(GeneratedExpr a, GeneratedExpr b)
        {
            return GeneratedParserBridge._PyPegen_join_names_with_dot(a, b);
        }

        /// <summary>
        /// CPython: _PyPegen_seq_extract_starred_exprs
        /// Extracts NAME nodes from expression sequence and converts to identifier sequence
        /// </summary>
        public static GeneratedIdentifierSeq MapNamesToIds(GeneratedExprSeq exprs)
        {
            return GeneratedParserBridge._PyPegen_map_names_to_ids(exprs);
        }

        /// <summary>
        /// CPython: _PyPegen_class_def_decorators
        /// Applies decorators to class definition
        /// </summary>
        public static GeneratedStmt ClassDefDecorators(GeneratedExprSeq decorators, GeneratedStmt class_def)
        {
            return GeneratedParserBridge._PyPegen_class_def_decorators(decorators, class_def);
        }

        /// <summary>
        /// CPython: _PyPegen_function_def_decorators
        /// Applies decorators to function definition
        /// </summary>
        public static GeneratedStmt FunctionDefDecorators(GeneratedExprSeq decorators, GeneratedStmt function_def)
        {
            return GeneratedParserBridge._PyPegen_function_def_decorators(decorators, function_def);
        }

        /// <summary>
        /// CPython: NameDefaultPair helper structure
        /// Represents parameter with optional default value
        /// </summary>
        public static GeneratedNameDefaultPair NameDefaultPair(
            GeneratedArg arg,
            GeneratedExpr? default_value,
            string? type_comment)
        {
            GeneratedTokenInfo? type_comment_token = null;
            if (type_comment != null)
            {
                type_comment_token = new GeneratedTokenInfo(PyToken.Type.TYPE_COMMENT, type_comment, 0, 0, 0, 0);
            }
            return GeneratedParserBridge._PyPegen_name_default_pair(arg, default_value, type_comment_token);
        }

        /// <summary>
        /// CPython: arg_ty _PyPegen_add_type_comment_to_arg(Parser *p, arg_ty arg, Token *tc)
        /// Adds type comment to argument
        /// </summary>
        public static GeneratedArg AddTypeCommentToArg(GeneratedArg arg, GeneratedTokenInfo? type_comment)
        {
            return GeneratedParserBridge._PyPegen_add_type_comment_to_arg(arg, type_comment);
        }

        /// <summary>
        /// CPython: _PyPegen_cmpop_expr_pair
        /// Creates comparison operator-expression pair for Compare node
        /// </summary>
        public static GeneratedCmpopExprPair CmpopExprPair(GeneratedCmpop cmpop, GeneratedExpr expr)
        {
            return GeneratedParserBridge._PyPegen_cmpop_expr_pair(cmpop, expr);
        }

        /// <summary>
        /// CPython: _PyPegen_get_cmpops / _PyPegen_get_exprs
        /// Extracts operators and expressions from CmpopExprPair list
        /// </summary>
        public static GeneratedCmpopSeq GetCmpops(GeneratedSeq pairs)
        {
            return GeneratedParserBridge._PyPegen_get_cmpops(pairs);
        }

        public static GeneratedExprSeq GetExprs(GeneratedSeq pairs)
        {
            return GeneratedParserBridge._PyPegen_get_exprs(pairs);
        }

        /// <summary>
        /// CPython: KeyPatternPair for match statement patterns
        /// </summary>
        public static GeneratedKeyPatternPair KeyPatternPair(GeneratedExpr key, GeneratedPattern pattern)
        {
            return GeneratedParserBridge._PyPegen_key_pattern_pair(key, pattern);
        }

        public static GeneratedExprSeq GetPatternKeys(GeneratedSeq pairs)
        {
            return GeneratedParserBridge._PyPegen_get_pattern_keys(pairs);
        }

        public static GeneratedPatternSeq GetPatterns(GeneratedSeq pairs)
        {
            return GeneratedParserBridge._PyPegen_get_patterns(pairs);
        }

        /// <summary>
        /// CPython: Generic SeqInsertInFront for various sequence types
        /// </summary>
        public static GeneratedSeq SeqInsertInFront(GeneratedPtr item, GeneratedSeq? seq)
        {
            return GeneratedParserBridge._PyPegen_seq_insert_in_front(item, seq);
        }

        public static GeneratedSeq SeqInsertInFront(GeneratedExpr item, GeneratedSeq? seq)
        {
            return GeneratedParserBridge._PyPegen_seq_insert_in_front(item, seq);
        }

        public static GeneratedSeq SeqInsertInFront(GeneratedPattern item, GeneratedSeq? seq)
        {
            return GeneratedParserBridge._PyPegen_seq_insert_in_front(item, seq);
        }

        /// <summary>
        /// CPython: Generic SingletonSequence for various types
        /// </summary>
        public static GeneratedSeq SingletonSequence(GeneratedPtr item)
        {
            return GeneratedParserBridge._PyPegen_singleton_seq(item);
        }

        public static GeneratedSeq SingletonSequence(GeneratedExpr item)
        {
            return GeneratedParserBridge._PyPegen_singleton_seq(item);
        }

        /// <summary>
        /// CPython: _PyPegen_dummy_name for temporary/placeholder names
        /// </summary>
        public static GeneratedName DummyName()
        {
            return GeneratedParserBridge._PyPegen_dummy_name();
        }

        /// <summary>
        /// CPython: _PyPegen_ensure_real / _PyPegen_ensure_imaginary
        /// Validates number token types for complex number literals
        /// </summary>
        public static GeneratedExpr EnsureReal(GeneratedExpr number)
        {
            return GeneratedParserBridge._PyPegen_ensure_real(number);
        }

        public static GeneratedExpr EnsureImaginary(GeneratedExpr number)
        {
            return GeneratedParserBridge._PyPegen_ensure_imaginary(number);
        }

        /// <summary>
        /// CPython: _PyPegen_singleton_seq - Generic singleton sequence wrapper
        /// Grammar uses: PyParserHelpers.SingletonSeq(...)
        /// </summary>
        public static GeneratedSeq SingletonSeq(GeneratedPtr item)
        {
            return GeneratedParserBridge._PyPegen_singleton_seq(item);
        }

        /// <summary>
        /// CPython: _PyPegen_seq_insert_in_front - Append to end wrapper
        /// Grammar uses: PyParserHelpers.SeqAppendToEnd(seq, item)
        /// Implementation: Insert at end by iterating and adding
        /// </summary>
        public static GeneratedSeq SeqAppendToEnd(GeneratedSeq seq, GeneratedPtr item)
        {
            if (seq == null) return GeneratedParserBridge._PyPegen_singleton_seq(item);
            seq.Add(item);
            return seq;
        }

        /// <summary>
        /// CPython: _PyPegen_keyword_or_starred
        /// Grammar uses: PyParserHelpers.KeywordOrStarred(element, is_keyword)
        /// </summary>
        public static GeneratedKeywordOrStarred KeywordOrStarred(GeneratedPtr element, int is_keyword)
        {
            return GeneratedParserBridge._PyPegen_keyword_or_starred(element, is_keyword);
        }

        // ==================== Dictionary Helper Methods ====================
        // CPython: _PyPegen_get_keys - Extracts all keys from KeyValuePair sequence
        public static GeneratedExprSeq GetKeys(GeneratedSeq seq)
        {
            var keys = new List<GeneratedPtr>();
            foreach (var item in seq)
            {
                var pair = (GeneratedKeyValuePair)item;
                keys.Add(pair.Key);
            }
            return GeneratedSeq.FromList(keys).Cast<GeneratedExprSeq>();
        }

        // CPython: _PyPegen_get_values - Extracts all values from KeyValuePair sequence
        public static GeneratedExprSeq GetValues(GeneratedSeq seq)
        {
            var values = new List<GeneratedPtr>();
            foreach (var item in seq)
            {
                var pair = (GeneratedKeyValuePair)item;
                values.Add(pair.Value);
            }
            return GeneratedSeq.FromList(values).Cast<GeneratedExprSeq>();
        }

        // CPython: _PyPegen_key_value_pair - Creates a KeyValuePair
        public static GeneratedKeyValuePair KeyValuePair(GeneratedExpr key, GeneratedExpr value)
        {
            return new GeneratedKeyValuePair { Key = key, Value = value };
        }

        // ==================== Sequence Helper Methods ====================
        // CPython: _PyPegen_join_sequences - Joins two sequences
        public static GeneratedSeq JoinSequences(GeneratedSeq a, GeneratedSeq b)
        {
            var result = new List<GeneratedPtr>(a);
            result.AddRange(b);
            return new GeneratedSeq(result);
        }

        // CPython: _PyPegen_seq_extract_starred_exprs - Extract starred expressions
        public static GeneratedExprSeq? SeqExtractStarredExprs(GeneratedSeq seq)
        {
            var starred = new List<GeneratedPtr>();
            foreach (var item in seq)
            {
                var kws = (GeneratedKeywordOrStarred)item;
                if (kws.Starred != null) // Starred property is set for starred expressions
                {
                    starred.Add(kws.Starred);
                }
            }
            return starred.Count > 0 ? GeneratedSeq.FromList(starred).Cast<GeneratedExprSeq>() : null;
        }

        // CPython: _PyPegen_seq_delete_starred_exprs - Delete starred expressions, keep keywords
        public static GeneratedKeywordSeq? SeqDeleteStarredExprs(GeneratedSeq seq)
        {
            var keywords = new List<GeneratedPtr>();
            foreach (var item in seq)
            {
                var kws = (GeneratedKeywordOrStarred)item;
                if (kws.Keyword != null) // Keyword property is set for keyword arguments
                {
                    keywords.Add(kws.Keyword);
                }
            }
            return keywords.Count > 0 ? GeneratedSeq.FromList(keywords).Cast<GeneratedKeywordSeq>() : null;
        }

        // CPython: expr_ty _PyPegen_collect_call_seqs(Parser *p, asdl_expr_seq *a, asdl_seq *b, ...)
        // Returns a Call expression with combined args and keywords
        public static GeneratedExpr CollectCallSeqs(GeneratedExprSeq? a, GeneratedSeq? b,
                                                     int lineno, int col_offset, int end_lineno, int end_col_offset)
        {
            // CPython: If b is NULL, return Call with just 'a' as args
            if (b == null)
            {
                return PyAst.Call(DummyName(), a, null, lineno, col_offset, end_lineno, end_col_offset);
            }

            // CPython: Extract starred expressions and keywords from b
            var starreds = SeqExtractStarredExprs(b);
            var keywords = SeqDeleteStarredExprs(b);

            // CPython: Combine 'a' and 'starreds' into new args sequence
            var args_len = a?.Count ?? 0;
            var total_len = args_len + (starreds?.Count ?? 0);

            var combined_args = new GeneratedExprSeq(total_len);

            // Copy args from 'a'
            if (a != null)
            {
                foreach (var arg in a)
                {
                    combined_args.Add((GeneratedExpr)arg);
                }
            }

            // Append starred expressions
            if (starreds != null)
            {
                foreach (var starred in starreds)
                {
                    combined_args.Add((GeneratedExpr)starred);
                }
            }

            // CPython: Return Call with combined args and keywords
            return PyAst.Call(DummyName(), combined_args, keywords, lineno, col_offset, end_lineno, end_col_offset);
        }

        // ==================== F-string Helper Methods ====================
        // CPython: _PyPegen_constant_from_token - Creates a Constant from a string token
        public static GeneratedExpr ConstantFromString(GeneratedTokenInfo tok)
        {
            return GeneratedParserBridge._PyPegen_constant_from_token(tok);
        }

        // CPython: _PyPegen_decoded_constant_from_token - Decode constant from token
        public static GeneratedExpr DecodedConstantFromToken(GeneratedTokenInfo tok)
        {
            return GeneratedParserBridge._PyPegen_decoded_constant_from_token(tok);
        }

        // CPython: _PyPegen_concatenate_strings - Concatenate string expressions
        public static GeneratedExpr ConcatenateStrings(GeneratedExprSeq strings,
                                                       int lineno, int col_offset, int end_lineno, int end_col_offset)
        {
            // TODO: Implement string concatenation - for now return first string
            if (strings == null || strings.Count == 0)
                return PyAst.Constant(new GeneratedPyConstantString(""), null, lineno, col_offset, end_lineno, end_col_offset);
            return (GeneratedExpr)strings[0];
        }

        // CPython: _PyPegen_joined_str - Create a JoinedStr (f-string)
        public static GeneratedExpr JoinedStr(GeneratedExprSeq parts)
        {
            return PyAst.JoinedStr(parts, 0, 0, 0, 0);
        }

        // CPython: _PyPegen_check_fstring_conversion - Check f-string conversion specifier
        public static GeneratedTokenInfo? CheckFstringConversion(GeneratedTokenInfo? conv)
        {
            if (conv == null) return null;

            var text = conv.GetStringValue();
            if (text != "s" && text != "r" && text != "a")
            {
                // TODO: Raise proper syntax error
                return null;
            }
            return conv;
        }

        // CPython: _PyPegen_setup_full_format_spec - Setup format spec for f-string
        public static GeneratedExpr? SetupFullFormatSpec(GeneratedTokenInfo? colon, GeneratedExprSeq? spec)
        {
            if (colon == null) return null;
            if (spec == null || spec.Count == 0)
            {
                return PyAst.Constant(new GeneratedPyConstantString(""), null, 0, 0, 0, 0);
            }
            return JoinedStr(spec);
        }

        // TODO: Add more helper methods as needed during grammar rewriting
    }

}
