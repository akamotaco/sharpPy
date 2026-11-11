// PyParserHelpers - Helper functions for PEG parser
// CPython 3.12 compatible - Manual implementation (not generated)
// This file provides AST factory functions and PEG parser helper utilities

using System;
using System.Collections.Generic;

namespace SharpPy.Generated
{
    // ============================================================
    // PyAST Helper Functions (AST Node Factories)
    // ============================================================

    /// <summary>
    /// AST node factory functions - CPython 3.12: Python-ast.c
    /// Renamed from * to PyAst.* for C# naming conventions
    /// </summary>
    public static partial class PyAst
    {
        public static GeneratedMod Module(GeneratedStmtSeq body, GeneratedTypeIgnoreSeq type_ignores)
        {
            var node = new GeneratedModule();
            node.Body = body;
            node.TypeIgnores = type_ignores;
            return node;
        }

        public static GeneratedMod Interactive(GeneratedStmtSeq body)
        {
            var node = new GeneratedInteractive();
            node.Body = body;
            return node;
        }

        public static GeneratedMod Expression(GeneratedExpr body)
        {
            var node = new GeneratedExpression();
            node.Body = body;
            return node;
        }

        public static GeneratedMod FunctionType(GeneratedExprSeq argtypes, GeneratedExpr returns)
        {
            var node = new GeneratedFunctionType();
            node.Argtypes = argtypes;
            node.Returns = returns;
            return node;
        }

        public static GeneratedStmt FunctionDef(string name, GeneratedArguments args, GeneratedStmtSeq body, GeneratedExprSeq decorator_list, GeneratedExpr? returns, string? type_comment, GeneratedTypeParamSeq type_params, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedStmt FunctionDef(string name, GeneratedArguments args, GeneratedStmtSeq body, GeneratedExprSeq decorator_list, GeneratedTypeParamSeq type_params, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return FunctionDef(name, args, body, decorator_list, null, null, type_params, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt AsyncFunctionDef(string name, GeneratedArguments args, GeneratedStmtSeq body, GeneratedExprSeq decorator_list, GeneratedExpr? returns, string? type_comment, GeneratedTypeParamSeq type_params, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedStmt AsyncFunctionDef(string name, GeneratedArguments args, GeneratedStmtSeq body, GeneratedExprSeq decorator_list, GeneratedTypeParamSeq type_params, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AsyncFunctionDef(name, args, body, decorator_list, null, null, type_params, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt ClassDef(string name, GeneratedExprSeq bases, GeneratedKeywordSeq keywords, GeneratedStmtSeq body, GeneratedExprSeq decorator_list, GeneratedTypeParamSeq type_params, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedStmt Return(GeneratedExpr? value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedReturn();
            node.Value = value;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt Return(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return Return(null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt Delete(GeneratedExprSeq targets, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedDelete();
            node.Targets = targets;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt Assign(GeneratedExprSeq targets, GeneratedExpr value, string? type_comment, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedStmt Assign(GeneratedExprSeq targets, GeneratedExpr value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return Assign(targets, value, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt TypeAlias(GeneratedExpr name, GeneratedTypeParamSeq type_params, GeneratedExpr value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedStmt AugAssign(GeneratedExpr target, GeneratedOperator op, GeneratedExpr value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedStmt AnnAssign(GeneratedExpr target, GeneratedExpr annotation, GeneratedExpr? value, int simple, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedStmt AnnAssign(GeneratedExpr target, GeneratedExpr annotation, int simple, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AnnAssign(target, annotation, null, simple, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt For(GeneratedExpr target, GeneratedExpr iter, GeneratedStmtSeq body, GeneratedStmtSeq orelse, string? type_comment, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedStmt For(GeneratedExpr target, GeneratedExpr iter, GeneratedStmtSeq body, GeneratedStmtSeq orelse, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return For(target, iter, body, orelse, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt AsyncFor(GeneratedExpr target, GeneratedExpr iter, GeneratedStmtSeq body, GeneratedStmtSeq orelse, string? type_comment, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedStmt AsyncFor(GeneratedExpr target, GeneratedExpr iter, GeneratedStmtSeq body, GeneratedStmtSeq orelse, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AsyncFor(target, iter, body, orelse, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt While(GeneratedExpr test, GeneratedStmtSeq body, GeneratedStmtSeq orelse, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedStmt If(GeneratedExpr test, GeneratedStmtSeq body, GeneratedStmtSeq orelse, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedStmt With(GeneratedWithitemSeq items, GeneratedStmtSeq body, string? type_comment, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedStmt With(GeneratedWithitemSeq items, GeneratedStmtSeq body, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return With(items, body, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt AsyncWith(GeneratedWithitemSeq items, GeneratedStmtSeq body, string? type_comment, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedStmt AsyncWith(GeneratedWithitemSeq items, GeneratedStmtSeq body, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return AsyncWith(items, body, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt Match(GeneratedExpr subject, GeneratedMatchCaseSeq cases, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedStmt Raise(GeneratedExpr? exc, GeneratedExpr? cause, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedStmt Raise(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return Raise(null, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt Try(GeneratedStmtSeq body, GeneratedExcepthandlerSeq handlers, GeneratedStmtSeq orelse, GeneratedStmtSeq finalbody, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedStmt TryStar(GeneratedStmtSeq body, GeneratedExcepthandlerSeq handlers, GeneratedStmtSeq orelse, GeneratedStmtSeq finalbody, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedStmt Assert(GeneratedExpr test, GeneratedExpr? msg, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedStmt Assert(GeneratedExpr test, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return Assert(test, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt Import(GeneratedAliasSeq names, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedImport();
            node.Names = names;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt ImportFrom(string? module, GeneratedAliasSeq names, int? level, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedStmt ImportFrom(GeneratedAliasSeq names, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return ImportFrom(null, names, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt Global(GeneratedIdentifierSeq names, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedGlobal();
            node.Names = names;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt Nonlocal(GeneratedIdentifierSeq names, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedNonlocal();
            node.Names = names;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt Expr(GeneratedExpr value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedExprStmt();
            node.Value = value;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt Pass(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedPass();
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt Break(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedBreak();
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt Continue(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedContinue();
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr BoolOp(GeneratedBoolop op, GeneratedExprSeq values, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedExpr NamedExpr(GeneratedExpr target, GeneratedExpr value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedExpr BinOp(GeneratedExpr left, GeneratedOperator op, GeneratedExpr right, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedExpr UnaryOp(GeneratedUnaryop op, GeneratedExpr operand, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedExpr Lambda(GeneratedArguments args, GeneratedExpr body, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedExpr IfExp(GeneratedExpr test, GeneratedExpr body, GeneratedExpr orelse, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedExpr Dict(GeneratedExprSeq keys, GeneratedExprSeq values, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedExpr Set(GeneratedExprSeq elts, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedSet();
            node.Elts = elts;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr ListComp(GeneratedExpr elt, GeneratedComprehensionSeq generators, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedExpr SetComp(GeneratedExpr elt, GeneratedComprehensionSeq generators, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedExpr DictComp(GeneratedExpr key, GeneratedExpr value, GeneratedComprehensionSeq generators, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedExpr GeneratorExp(GeneratedExpr elt, GeneratedComprehensionSeq generators, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedExpr Await(GeneratedExpr value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedAwait();
            node.Value = value;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr Yield(GeneratedExpr? value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedYield();
            node.Value = value;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr Yield(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return Yield(null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedExpr YieldFrom(GeneratedExpr value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedYieldFrom();
            node.Value = value;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr Compare(GeneratedExpr left, GeneratedCmpopSeq ops, GeneratedExprSeq comparators, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedExpr Call(GeneratedExpr func, GeneratedExprSeq args, GeneratedKeywordSeq keywords, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedExpr FormattedValue(GeneratedExpr value, int conversion, GeneratedExpr? format_spec, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedExpr JoinedStr(GeneratedExprSeq values, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedJoinedStr();
            node.Values = values;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr Constant(GeneratedPyConstant value, string? kind, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedExpr Constant(GeneratedPyConstant value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return Constant(value, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedExpr Attribute(GeneratedExpr value, string attr, GeneratedExprContext ctx, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedExpr Subscript(GeneratedExpr value, GeneratedExpr slice, GeneratedExprContext ctx, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedExpr Starred(GeneratedExpr value, GeneratedExprContext ctx, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedExpr Name(string id, GeneratedExprContext ctx, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedExpr List(GeneratedExprSeq elts, GeneratedExprContext ctx, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedExpr Tuple(GeneratedExprSeq elts, GeneratedExprContext ctx, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedExpr Slice(GeneratedExpr? lower, GeneratedExpr? upper, GeneratedExpr? step, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedExpr Slice(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return Slice(null, null, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedExprContext Load(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedLoad.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExprContext Store(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedStore.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExprContext Del(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedDel.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedBoolop And(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedAnd.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedBoolop Or(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedOr.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedOperator Add(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedAdd.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedOperator Sub(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedSub.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedOperator Mult(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedMult.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedOperator MatMult(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedMatMult.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedOperator Div(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedDiv.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedOperator Mod(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedMod_.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedOperator Pow(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedPow.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedOperator LShift(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedLShift.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedOperator RShift(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedRShift.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedOperator BitOr(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedBitOr.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedOperator BitXor(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedBitXor.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedOperator BitAnd(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedBitAnd.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedOperator FloorDiv(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedFloorDiv.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedUnaryop Invert(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedInvert.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedUnaryop Not(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedNot.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedUnaryop UAdd(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedUAdd.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedUnaryop USub(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedUSub.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedCmpop Eq(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedEq.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedCmpop NotEq(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedNotEq.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedCmpop Lt(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedLt.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedCmpop LtE(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedLtE.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedCmpop Gt(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedGt.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedCmpop GtE(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedGtE.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedCmpop Is(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedIs.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedCmpop IsNot(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedIsNot.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedCmpop In(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedIn.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedCmpop NotIn(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedNotIn.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedComprehension comprehension(GeneratedExpr target, GeneratedExpr iter, GeneratedExprSeq ifs, int is_async)
        {
            var node = new GeneratedComprehension();
            node.Target = target;
            node.Iter = iter;
            node.Ifs = ifs;
            node.IsAsync = is_async;
            return node;
        }

        public static GeneratedExcepthandler ExceptHandler(GeneratedExpr? type, string? name, GeneratedStmtSeq body, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedExcepthandler ExceptHandler(GeneratedStmtSeq body, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return ExceptHandler(null, null, body, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedArguments arguments(GeneratedArgSeq posonlyargs, GeneratedArgSeq args, GeneratedArg? vararg, GeneratedArgSeq kwonlyargs, GeneratedExprSeq kw_defaults, GeneratedArg? kwarg, GeneratedExprSeq defaults)
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

        public static GeneratedArguments arguments(GeneratedArgSeq posonlyargs, GeneratedArgSeq args, GeneratedArgSeq kwonlyargs, GeneratedExprSeq kw_defaults, GeneratedExprSeq defaults)
        {
            return arguments(posonlyargs, args, null, kwonlyargs, kw_defaults, null, defaults);
        }

        public static GeneratedArg arg(string arg, GeneratedExpr? annotation, string? type_comment, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedArg arg(string arg, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return PyAst.arg(arg, null, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedKeyword keyword(string? arg, GeneratedExpr value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedKeyword keyword(GeneratedExpr value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return keyword(null, value, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedAlias alias(string name, string? asname, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedAlias alias(string name, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return alias(name, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedWithitem withitem(GeneratedExpr context_expr, GeneratedExpr? optional_vars)
        {
            var node = new GeneratedWithitem();
            node.ContextExpr = context_expr;
            node.OptionalVars = optional_vars;
            return node;
        }

        public static GeneratedWithitem withitem(GeneratedExpr context_expr)
        {
            return withitem(context_expr, null);
        }

        public static GeneratedMatchCase match_case(GeneratedPattern pattern, GeneratedExpr? guard, GeneratedStmtSeq body)
        {
            var node = new GeneratedMatchCase();
            node.Pattern = pattern;
            node.Guard = guard;
            node.Body = body;
            return node;
        }

        public static GeneratedMatchCase match_case(GeneratedPattern pattern, GeneratedStmtSeq body)
        {
            return match_case(pattern, null, body);
        }

        public static GeneratedPattern MatchValue(GeneratedExpr value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedMatchValue();
            node.Value = value;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedPattern MatchSingleton(GeneratedPyConstant value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedMatchSingleton();
            node.Value = value;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedPattern MatchSequence(GeneratedPatternSeq patterns, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedMatchSequence();
            node.Patterns = patterns;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedPattern MatchMapping(GeneratedExprSeq keys, GeneratedPatternSeq patterns, string? rest, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedPattern MatchMapping(GeneratedExprSeq keys, GeneratedPatternSeq patterns, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return MatchMapping(keys, patterns, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedPattern MatchClass(GeneratedExpr cls, GeneratedPatternSeq patterns, GeneratedIdentifierSeq kwd_attrs, GeneratedPatternSeq kwd_patterns, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedPattern MatchStar(string? name, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedMatchStar();
            node.Name = name;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedPattern MatchStar(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return MatchStar(null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedPattern MatchAs(GeneratedPattern? pattern, string? name, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedPattern MatchAs(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return MatchAs(null, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedPattern MatchOr(GeneratedPatternSeq patterns, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedMatchOr();
            node.Patterns = patterns;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedTypeIgnore TypeIgnore(int lineno, string tag)
        {
            var node = new GeneratedTypeIgnoreNode();
            node.Lineno = lineno;
            node.Tag = tag;
            return node;
        }

        public static GeneratedTypeParam TypeVar(string name, GeneratedExpr? bound, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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

        public static GeneratedTypeParam TypeVar(string name, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return TypeVar(name, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedTypeParam ParamSpec(string name, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedParamSpec();
            node.Name = name;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedTypeParam TypeVarTuple(string name, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
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
    /// Renamed from _PyPegen_* to PyParserRuntime.* for C# naming conventions
    /// </summary>
    public static partial class PyParserRuntime
    {
        // CPython: SeqFlatten
        // Flatten list of sequences into single sequence
        // CPython 3.12: Returns NULL if total size is 0 (assert fails in CPython)
        public static GeneratedStmtSeq SeqFlatten(GeneratedSeq seqs)
        {
            var sequences = seqs.ToCastList<GeneratedStmtSeq>();
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

        // CPython: _PyPegen_concatenate_strings (Parser/action_helpers.c)
        // CPython: _PyPegen_concatenate_strings - Concatenate string expressions
        // CPython 3.12: Parser/action_helpers.c:1413-1603
        public static GeneratedExpr ConcatenateStrings(GeneratedExprSeq strings,
                                                       int lineno, int col_offset, int end_lineno, int end_col_offset)
        {
            if (strings == null || strings.Count == 0)
                return PyAst.Constant(new GeneratedPyConstantString(""), null, lineno, col_offset, end_lineno, end_col_offset);

            int len = strings.Count;

            // CPython: Check for f-strings, unicode strings, and bytes
            bool fStringFound = false;
            bool unicodeStringFound = false;
            bool bytesFound = false;
            int nFlattenedElements = 0;

            for (int i = 0; i < len; i++)
            {
                var elem = strings[i] as GeneratedExpr;
                if (elem == null) continue;

                if (elem is GeneratedConstant constant)
                {
                    if (constant.Value is GeneratedPyConstantBytes)
                    {
                        bytesFound = true;
                    }
                    else
                    {
                        unicodeStringFound = true;
                    }
                    nFlattenedElements++;
                }
                else if (elem is GeneratedJoinedStr joinedStr)
                {
                    nFlattenedElements += joinedStr.Values?.Count ?? 0;
                    fStringFound = true;
                }
                else
                {
                    nFlattenedElements++;
                    fStringFound = true;
                }
            }

            // CPython: Handle bytes concatenation
            if (bytesFound)
            {
                var bytesList = new System.Collections.Generic.List<byte>();
                string? kind = null;

                for (int i = 0; i < len; i++)
                {
                    var elem = strings[i] as GeneratedConstant;
                    if (elem?.Value is GeneratedPyConstantBytes bytesVal)
                    {
                        if (i == 0) kind = elem.Kind;
                        bytesList.AddRange(bytesVal.Value);
                    }
                }

                return PyAst.Constant(new GeneratedPyConstantBytes(bytesList.ToArray()), kind, lineno, col_offset, end_lineno, end_col_offset);
            }

            // CPython: If no f-string and only one element, return it directly
            if (!fStringFound && len == 1)
            {
                return strings[0] as GeneratedExpr ?? PyAst.Constant(new GeneratedPyConstantString(""), null, lineno, col_offset, end_lineno, end_col_offset);
            }

            // CPython: Build flattened list (flatten JoinedStr nodes)
            var flattened = new System.Collections.Generic.List<GeneratedExpr>(nFlattenedElements);
            for (int i = 0; i < len; i++)
            {
                var elem = strings[i] as GeneratedExpr;
                if (elem == null) continue;

                if (elem is GeneratedJoinedStr joinedStr && joinedStr.Values != null)
                {
                    foreach (var subvalue in joinedStr.Values)
                    {
                        if (subvalue is GeneratedExpr subExpr)
                            flattened.Add(subExpr);
                    }
                }
                else
                {
                    flattened.Add(elem);
                }
            }

            // CPython: Calculate folded element count (consecutive Constants merge into one)
            int nElements = 0;
            bool prevIsConstant = false;
            for (int i = 0; i < flattened.Count; i++)
            {
                var elem = flattened[i];

                // Skip empty constants in f-string context
                if (fStringFound && elem is GeneratedConstant emptyConst &&
                    emptyConst.Value is GeneratedPyConstantString emptyStr &&
                    string.IsNullOrEmpty(emptyStr.Value))
                {
                    continue;
                }

                if (!prevIsConstant || !(elem is GeneratedConstant))
                {
                    nElements++;
                }
                prevIsConstant = elem is GeneratedConstant;
            }

            // CPython: Build folded list (concatenate consecutive Constants)
            var values = new GeneratedExprSeq(nElements);
            var sb = new System.Text.StringBuilder();

            for (int i = 0; i < flattened.Count; i++)
            {
                var elem = flattened[i];

                // CPython: if the current elem and the following are constants, fold them
                if (elem is GeneratedConstant constant && constant.Value is GeneratedPyConstantString strVal)
                {
                    // Check if next element is also a constant string
                    if (i + 1 < flattened.Count && flattened[i + 1] is GeneratedConstant nextConst &&
                        nextConst.Value is GeneratedPyConstantString)
                    {
                        var firstElem = constant;
                        string? kind = constant.Kind;
                        sb.Clear();

                        GeneratedConstant lastElem = constant;
                        int j = i;
                        for (; j < flattened.Count; j++)
                        {
                            if (flattened[j] is GeneratedConstant currentConst &&
                                currentConst.Value is GeneratedPyConstantString currentStr)
                            {
                                sb.Append(currentStr.Value);
                                lastElem = currentConst;
                            }
                            else
                            {
                                break;
                            }
                        }
                        i = j - 1;

                        var concatenatedStr = sb.ToString();
                        elem = PyAst.Constant(new GeneratedPyConstantString(concatenatedStr), kind,
                                              firstElem.LineNo, firstElem.ColOffset,
                                              lastElem.EndLineNo, lastElem.EndColOffset) as GeneratedExpr;
                    }

                    // Skip empty constant strings in f-string context
                    if (fStringFound && elem is GeneratedConstant emptyCheck &&
                        emptyCheck.Value is GeneratedPyConstantString emptyVal &&
                        string.IsNullOrEmpty(emptyVal.Value))
                    {
                        continue;
                    }
                }

                values.Add(elem!);
            }

            // CPython: If no f-string, return the single concatenated constant
            if (!fStringFound)
            {
                if (values.Count == 1)
                {
                    return values[0] as GeneratedExpr ?? PyAst.Constant(new GeneratedPyConstantString(""), null, lineno, col_offset, end_lineno, end_col_offset);
                }
                // Fallback: concatenate all string values
                sb.Clear();
                string? finalKind = null;
                foreach (var v in values)
                {
                    if (v is GeneratedConstant c && c.Value is GeneratedPyConstantString s)
                    {
                        sb.Append(s.Value);
                        if (finalKind == null) finalKind = c.Kind;
                    }
                }
                return PyAst.Constant(new GeneratedPyConstantString(sb.ToString()), finalKind, lineno, col_offset, end_lineno, end_col_offset);
            }

            // CPython: Return JoinedStr for f-strings
            return PyAst.JoinedStr(values, lineno, col_offset, end_lineno, end_col_offset);
        }

        // CPython: SingletonSeq
        public static GeneratedStmtSeq SingletonSeq(GeneratedStmt item)
        {
            var seq = new GeneratedStmtSeq(1);
            seq.Add(item);
            return seq;
        }

        public static GeneratedExprSeq SingletonSeq(GeneratedExpr item)
        {
            var seq = new GeneratedExprSeq(1);
            seq.Add(item);
            return seq;
        }

        public static GeneratedAliasSeq SingletonSeq(GeneratedAlias item)
        {
            var seq = new GeneratedAliasSeq(1);
            seq.Add(item);
            return seq;
        }

        // For grammar helper types like SlashWithDefault
        public static GeneratedSeq SingletonSeq(GeneratedSlashWithDefault item)
        {
            var seq = new GeneratedSeq(1);
            seq.Add(item);
            return seq;
        }




        // CPython: MakeModule
        public static GeneratedModule MakeModule(GeneratedStmtSeq body)
        {
            var module = new GeneratedModule();
            module.Body = body ?? GeneratedStmtSeq.Empty;
            module.TypeIgnores = GeneratedTypeIgnoreSeq.Empty;
            return module;
        }

        // CPython: _PyPegen_seq_append_to_end (Parser/action_helpers.c): a new sequence of the items
        // and a, a singleton when seq is NULL; seq itself is left as it was.
        public static GeneratedSeq SeqAppendToEnd(GeneratedSeq? seq, GeneratedPtr a)
        {
            if (seq == null)
                return SingletonSeq(a);
            var newSeq = new GeneratedSeq(seq.Count + 1);
            foreach (var item in seq)
                newSeq.Add(item);
            newSeq.Add(a);
            return newSeq;
        }

        // CPython: Py_None, Py_True, Py_False, Py_Ellipsis
        // Note: These are constant VALUES for AST construction, not runtime PyObjects
        public static GeneratedPyConstant Py_None => GeneratedPyConstant.None;
        public static GeneratedPyConstant Py_True => GeneratedPyConstant.True;
        public static GeneratedPyConstant Py_False => GeneratedPyConstant.False;
        public static GeneratedPyConstant Py_Ellipsis => GeneratedPyConstant.Ellipsis;


        // CPython: CollectCallSeqs
        // Collect arguments and keywords from call sequences
        public static GeneratedExpr CollectCallSeqs(
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
                    Func = DummyName(),
                    Args = a,
                    Keywords = null,
                    LineNo = lineno,
                    ColOffset = col_offset,
                    EndLineNo = end_lineno,
                    EndColOffset = end_col_offset
                };
            }

            var starreds = SeqExtractStarredExprs(b);
            var keywords = SeqDeleteStarredExprs(b);

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
                Func = DummyName(),
                Args = args,
                Keywords = keywords,
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        // CPython: KeyValuePair
        // Create a key-value pair for dictionary literals
        public static GeneratedKeyValuePair KeyValuePair(GeneratedExpr key, GeneratedExpr value)
        {
            // _PyPegen_key_value_pair: a '**' entry has no key (NULL), as dict displays expect.
            return new GeneratedKeyValuePair { Key = key, Value = value };
        }

        // CPython: GetKeys
        // Extract keys from (key, value) pairs
        public static GeneratedExprSeq GetKeys(GeneratedSeq pairs)
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

        // CPython: GetValues
        // Extract values from (key, value) pairs
        public static GeneratedExprSeq GetValues(GeneratedSeq pairs)
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

        // CPython: SeqExtractStarredExprs
        // Extract starred expressions from KeywordOrStarred sequence
        public static GeneratedExprSeq SeqExtractStarredExprs(GeneratedKeywordOrStarredSeq seq)
        {
            var exprs = new GeneratedExprSeq();
            foreach (var item in seq.ToEnumerable<GeneratedKeywordOrStarred>())
            {
                if (item.Starred != null)
                {
                    exprs.Add(item.Starred);
                }
            }
            // _PyPegen_seq_extract_starred_exprs: NULL when there is none.
            return exprs.Count == 0 ? null : exprs;
        }

        // CPython: SeqDeleteStarredExprs
        // Extract keywords (non-starred items) from KeywordOrStarred sequence
        public static GeneratedKeywordSeq SeqDeleteStarredExprs(GeneratedKeywordOrStarredSeq seq)
        {
            var keywords = new GeneratedKeywordSeq();
            foreach (var item in seq.ToEnumerable<GeneratedKeywordOrStarred>())
            {
                if (item.Keyword != null)
                {
                    keywords.Add(item.Keyword);
                }
            }
            // _PyPegen_seq_delete_starred_exprs: NULL when nothing is left.
            return keywords.Count == 0 ? null : keywords;
        }

        // Overload for MixedSeq
        // CPython 3.12: Type cast shares same underlying array (no copy)
        public static GeneratedExprSeq SeqExtractStarredExprs(GeneratedSeq seq)
        {
            // CPython: implicit cast shares same PyObject** array
            // C#: Cast<T>() shares same List<GeneratedPtr> via _initialize
            return SeqExtractStarredExprs(seq.Cast<GeneratedKeywordOrStarredSeq>());
        }

        // CPython 3.12: Type cast with shared reference (no copy)
        public static GeneratedKeywordSeq SeqDeleteStarredExprs(GeneratedSeq seq)
        {
            // CPython: implicit cast shares same PyObject** array
            // C#: Cast<T>() shares same List<GeneratedPtr> via _initialize
            return SeqDeleteStarredExprs(seq.Cast<GeneratedKeywordOrStarredSeq>());
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

        // CPython: CheckLegacyStmt
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
        /// Used for helper functions like SeqExtractStarredExprs
        /// Returns nullable type - NULL is valid if no error occurred
        /// </summary>
        public static T? CHECK_NULL_ALLOWED<T>(T? value) where T : class
        {
            // CPython: if (result == NULL && PyErr_Occurred()) p->error_indicator = 1;
            // In C#: We don't set error_indicator here, just return null
            // The caller is responsible for checking null and handling it
            return value;
        }

        // ==================== Sequence Item Access Helpers ====================
        // CPython: macro-like helpers for accessing sequence items

        // CPython: #define PyPegen_last_item(seq, type) ((type) asdl_seq_GET(seq, asdl_seq_LEN(seq)-1))
        public static T LastItem<T>(GeneratedSeq seq) where T : GeneratedPtr
        {
            if (seq == null || seq.Count == 0)
                throw new InvalidOperationException("Cannot get last item from empty sequence");
            return (T)seq[seq.Count - 1];
        }

        // CPython: #define PyPegen_first_item(seq, type) ((type) asdl_seq_GET(seq, 0))
        public static T FirstItem<T>(GeneratedSeq seq) where T : GeneratedPtr
        {
            if (seq == null || seq.Count == 0)
                throw new InvalidOperationException("Cannot get first item from empty sequence");
            return (T)seq[0];
        }

        // ==================== Expression Name Helpers ====================
        public static string GetExprName(GeneratedExpr expr) => ASTHelpers.GetExprName(expr);

        // ==================== Comprehension Helpers ====================
        public static GeneratedExpr GetLastComprehensionItem(GeneratedComprehension comp) =>
            ASTHelpers.GetLastComprehensionItem(comp);

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
        public GeneratedExpr Result { get; set; }
        public string Metadata { get; set; }
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

        // CPython: AddTypeCommentToArg
        // action_helpers.c: Add type comment to argument (Python 2 legacy)
        public static GeneratedArg AddTypeCommentToArg(GeneratedArg arg, GeneratedTokenInfo tc)
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

        // ============================================================
        // Sequence Helper Functions
        // ============================================================

        // CPython action_helpers.c:97
        // void *SeqLastItem(asdl_seq *seq)
        public static GeneratedPtr SeqLastItem<T>(T seq) where T : List<GeneratedPtr>
        {
            if (seq == null || seq.Count == 0) return null!;
            return seq[seq.Count - 1];
        }

        // CPython action_helpers.c:102
        // void *SeqFirstItem(asdl_seq *seq)
        public static GeneratedPtr SeqFirstItem<T>(T seq) where T : List<GeneratedPtr>
        {
            if (seq == null || seq.Count == 0) return null!;
            return seq[0];
        }

        // CPython pegen.h:253
        // #define PyPegen_last_item(seq, type) ((type)SeqLastItem((asdl_seq*)seq))
        public static TResult LastItem<TSeq, TResult>(TSeq seq)
            where TSeq : List<GeneratedPtr>
            where TResult : GeneratedPtr
        {
            return (TResult)SeqLastItem(seq);
        }

        // CPython pegen.h:255
        // #define PyPegen_first_item(seq, type) ((type)SeqFirstItem((asdl_seq*)seq))
        public static TResult FirstItem<TSeq, TResult>(TSeq seq)
            where TSeq : List<GeneratedPtr>
            where TResult : GeneratedPtr
        {
            return (TResult)SeqFirstItem(seq);
        }

        // ============================================================
        // Expression Name Helper
        // ============================================================

        // CPython action_helpers.c:945
        // const char *GetExprName(expr_ty e)
        public static string GetExprName(GeneratedExpr e)
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
                GeneratedCompare => "comparison",
                GeneratedIfExp => "conditional expression",
                GeneratedNamedExpr => "named expression",
                GeneratedConstant c => GetConstantName(c),
                _ => "expression"
            };
        }

        private static string GetConstantName(GeneratedConstant c)
        {
            if (c.Value is null or GeneratedPyConstantNone) return "None";
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
        // expr_ty GetLastComprehensionItem(comprehension_ty comprehension)
        public static GeneratedExpr GetLastComprehensionItem(GeneratedComprehension comprehension)
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
        // void *ArgumentsParsingError(Parser *p, expr_ty e)
        // Note: Made internal so PyParser can access it
        public static GeneratedPtr ArgumentsParsingError(dynamic p, GeneratedExpr e)
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
        // void *NonparenGenexpInCall(Parser *p, expr_ty args, asdl_comprehension_seq *comprehensions)
        // Note: Made internal so PyParser can access it
        public static GeneratedPtr NonparenGenexpInCall(dynamic p, GeneratedExpr args,
                                                                      GeneratedComprehensionSeq comprehensions)
        {
            if (!(args is GeneratedCall call)) return null!;
            if (call.Args == null) return null!;

            int len = call.Args.Count;
            if (len <= 1) return null!;

            var last_comprehension = (GeneratedComprehension)comprehensions[comprehensions.Count - 1];
            var lastArg = (GeneratedExpr)call.Args[len - 1];
            var lastItem = GetLastComprehensionItem(last_comprehension);

            return p.RaiseSyntaxErrorKnownRange(lastArg, lastItem,
                "Generator expression must be parenthesized");
        }

    }

    // ============================================================
    // PyAst - C# Style AST Factory Functions
    // ============================================================
    // Duplicate PyAst wrapper class removed - now using partial class PyAst directly
    // ============================================================
    /// Usage in python_cs.gram: PyParserHelpers.MakeModule(a) instead of MakeModule(a)
    /// </summary>

    // ============================================================
    // Extension Methods for GeneratedTokenInfo
    // ============================================================

    /// <summary>
    /// Extension methods for GeneratedTokenInfo (CPython pattern)
    /// Used in grammar action code for null-safe access
    /// </summary>
    public static class GeneratedTokenInfoExtensions
    {
        /// <summary>
        /// Get token value (name) - null-safe
        /// CPython: token->value or NULL
        /// </summary>
        public static string? GetNameValue(this GeneratedTokenInfo? token)
        {
            return token?.Value;
        }

        /// <summary>
        /// Get type comment value - null-safe
        /// CPython: token != NULL ? token->value : NULL
        /// Used for TYPE_COMMENT optional tokens
        /// </summary>
        public static string? GetCommentValue(this GeneratedTokenInfo? token)
        {
            return token?.Value;
        }
    }

}
