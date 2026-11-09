// Python/ast.c of CPython v3.12.15: _PyAST_Validate, what compile() runs over a tree given as AST
// objects before compiling it. A failure here is the exception the C code sets; each validate_*
// that returned 0 throws it instead.
using SharpPy.Generated;

namespace SharpPy
{
    internal static class AstValidator
    {
        private enum Context { Load, Store, Del }

        // _PyAST_Validate
        internal static void Validate(GeneratedMod mod)
        {
            switch (mod)
            {
                case GeneratedModule module:
                    Stmts(module.Body);
                    break;
                case GeneratedInteractive interactive:
                    Stmts(interactive.Body);
                    break;
                case GeneratedExpression expression:
                    Expr(expression.Body, Context.Load);
                    break;
                case GeneratedFunctionType function:
                    Exprs(function.Argtypes, Context.Load, nullOk: false);
                    Expr(function.Returns, Context.Load);
                    break;
                default:
                    throw PySystemError.Create("impossible module node");
            }
        }

        // The validator's recursion counter shares the C recursion limit (C_RECURSION_LIMIT).
        private static void Enter() => PyCRecursion.Enter(" during compilation");

        private static void Leave() => PyCRecursion.Leave();

        // VALIDATE_POSITIONS
        private static void Positions(GeneratedAstNode node)
        {
            if (node.LineNo > node.EndLineNo)
                throw PyValueError.Create($"AST node line range ({node.LineNo}, {node.EndLineNo}) is not valid");
            if ((node.LineNo < 0 && node.EndLineNo != node.LineNo) || (node.ColOffset < 0 && node.ColOffset != node.EndColOffset))
                throw PyValueError.Create($"AST node column range ({node.ColOffset}, {node.EndColOffset}) for line range ({node.LineNo}, {node.EndLineNo}) is not valid");
            if (node.LineNo == node.EndLineNo && node.ColOffset > node.EndColOffset)
                throw PyValueError.Create($"line {node.LineNo}, column {node.ColOffset}-{node.EndColOffset} is not a valid range");
        }

        // validate_name
        private static void Name(GeneratedIdentifier name)
        {
            if (name.Value is "None" or "True" or "False")
                throw PyValueError.Create($"identifier field can't represent '{name.Value}' constant");
        }

        // validate_comprehension
        private static void Comprehension(GeneratedComprehensionSeq generators)
        {
            if (generators.Count == 0)
                throw PyValueError.Create("comprehension with no generators");
            foreach (GeneratedComprehension comprehension in generators)
            {
                Expr(comprehension.Target, Context.Store);
                Expr(comprehension.Iter, Context.Load);
                Exprs(comprehension.Ifs, Context.Load, nullOk: false);
            }
        }

        // validate_keywords
        private static void Keywords(GeneratedKeywordSeq keywords)
        {
            foreach (GeneratedKeyword keyword in keywords)
                Expr(keyword.Value, Context.Load);
        }

        // validate_args
        private static void Args(GeneratedArgSeq args)
        {
            foreach (GeneratedArg arg in args)
            {
                Positions(arg);
                if (arg.Annotation != null)
                    Expr(arg.Annotation, Context.Load);
            }
        }

        // validate_arguments
        private static void Arguments(GeneratedArguments args)
        {
            Args(args.Posonlyargs);
            Args(args.Args);
            if (args.Vararg?.Annotation != null)
                Expr(args.Vararg.Annotation, Context.Load);
            Args(args.Kwonlyargs);
            if (args.Kwarg?.Annotation != null)
                Expr(args.Kwarg.Annotation, Context.Load);
            if (args.Defaults.Count > args.Posonlyargs.Count + args.Args.Count)
                throw PyValueError.Create("more positional defaults than args on arguments");
            if (args.KwDefaults.Count != args.Kwonlyargs.Count)
                throw PyValueError.Create("length of kwonlyargs is not the same as kw_defaults on arguments");
            Exprs(args.Defaults, Context.Load, nullOk: false);
            Exprs(args.KwDefaults, Context.Load, nullOk: true);
        }

        // validate_constant
        private static void Constant(GeneratedPyConstant constant)
        {
            if (constant is not GeneratedPyConstantObject wrapped)
                return; // None, Ellipsis, a bool, number, str or bytes the parser made
            ConstantObject(wrapped.Value);
        }

        private static void ConstantObject(PyObject value)
        {
            if (value is PyNone or PyEllipsis || value is PyBool
                || value.IsExactly(PyType.IntType) || value.IsExactly(PyType.FloatType) || value.IsExactly(PyType.ComplexType)
                || value.IsExactly(PyType.StrType) || value.IsExactly(PyType.BytesType))
                return;
            if (value.IsExactly(PyType.TupleType) || value.IsExactly(PyType.FrozenSetType))
            {
                Enter();
                try
                {
                    foreach (var item in PySet.IterableItems(value))
                        ConstantObject(item);
                }
                finally
                {
                    Leave();
                }
                return;
            }
            throw PyTypeError.Create($"got an invalid type in Constant: {value.GetPyType().Name}");
        }

        private static Context ContextOf(GeneratedExprContext context) => context switch
        {
            GeneratedStore => Context.Store,
            GeneratedDel => Context.Del,
            _ => Context.Load,
        };

        // validate_expr
        private static void Expr(GeneratedExpr exp, Context context)
        {
            Positions(exp);
            Enter();
            try
            {
                GeneratedExprContext actual = exp switch
                {
                    GeneratedAttribute attribute => attribute.Ctx,
                    GeneratedSubscript subscript => subscript.Ctx,
                    GeneratedStarred starred => starred.Ctx,
                    GeneratedName name => NameContext(name),
                    GeneratedList list => list.Ctx,
                    GeneratedTuple tuple => tuple.Ctx,
                    _ => null,
                };
                if (actual == null)
                {
                    if (context != Context.Load)
                        throw PyValueError.Create($"expression which can't be assigned to in {context} context");
                }
                else if (ContextOf(actual) != context)
                {
                    throw PyValueError.Create($"expression must have {context} context but has {ContextOf(actual)} instead");
                }

                switch (exp)
                {
                    case GeneratedBoolOp boolOp:
                        if (boolOp.Values.Count < 2)
                            throw PyValueError.Create("BoolOp with less than 2 values");
                        Exprs(boolOp.Values, Context.Load, nullOk: false);
                        break;
                    case GeneratedBinOp binOp:
                        Expr(binOp.Left, Context.Load);
                        Expr(binOp.Right, Context.Load);
                        break;
                    case GeneratedUnaryOp unaryOp:
                        Expr(unaryOp.Operand, Context.Load);
                        break;
                    case GeneratedLambda lambda:
                        Arguments(lambda.Args);
                        Expr(lambda.Body, Context.Load);
                        break;
                    case GeneratedIfExp ifExp:
                        Expr(ifExp.Test, Context.Load);
                        Expr(ifExp.Body, Context.Load);
                        Expr(ifExp.Orelse, Context.Load);
                        break;
                    case GeneratedDict dict:
                        if (dict.Keys.Count != dict.Values.Count)
                            throw PyValueError.Create("Dict doesn't have the same number of keys as values");
                        // A null key is {**mapping}.
                        Exprs(dict.Keys, Context.Load, nullOk: true);
                        Exprs(dict.Values, Context.Load, nullOk: false);
                        break;
                    case GeneratedSet set:
                        Exprs(set.Elts, Context.Load, nullOk: false);
                        break;
                    case GeneratedListComp listComp:
                        Comprehension(listComp.Generators);
                        Expr(listComp.Elt, Context.Load);
                        break;
                    case GeneratedSetComp setComp:
                        Comprehension(setComp.Generators);
                        Expr(setComp.Elt, Context.Load);
                        break;
                    case GeneratedGeneratorExp generatorExp:
                        Comprehension(generatorExp.Generators);
                        Expr(generatorExp.Elt, Context.Load);
                        break;
                    case GeneratedDictComp dictComp:
                        Comprehension(dictComp.Generators);
                        Expr(dictComp.Key, Context.Load);
                        Expr(dictComp.Value, Context.Load);
                        break;
                    case GeneratedYield yield:
                        if (yield.Value != null)
                            Expr(yield.Value, Context.Load);
                        break;
                    case GeneratedYieldFrom yieldFrom:
                        Expr(yieldFrom.Value, Context.Load);
                        break;
                    case GeneratedAwait await:
                        Expr(await.Value, Context.Load);
                        break;
                    case GeneratedCompare compare:
                        if (compare.Comparators.Count == 0)
                            throw PyValueError.Create("Compare with no comparators");
                        if (compare.Comparators.Count != compare.Ops.Count)
                            throw PyValueError.Create("Compare has a different number of comparators and operands");
                        Exprs(compare.Comparators, Context.Load, nullOk: false);
                        Expr(compare.Left, Context.Load);
                        break;
                    case GeneratedCall call:
                        Expr(call.Func, Context.Load);
                        Exprs(call.Args, Context.Load, nullOk: false);
                        Keywords(call.Keywords);
                        break;
                    case GeneratedConstant constant:
                        Constant(constant.Value);
                        break;
                    case GeneratedJoinedStr joined:
                        Exprs(joined.Values, Context.Load, nullOk: false);
                        break;
                    case GeneratedFormattedValue formatted:
                        Expr(formatted.Value, Context.Load);
                        if (formatted.FormatSpec != null)
                            Expr(formatted.FormatSpec, Context.Load);
                        break;
                    case GeneratedAttribute attribute:
                        Expr(attribute.Value, Context.Load);
                        break;
                    case GeneratedSubscript subscript:
                        Expr(subscript.Slice, Context.Load);
                        Expr(subscript.Value, Context.Load);
                        break;
                    case GeneratedStarred starred:
                        Expr(starred.Value, context);
                        break;
                    case GeneratedSlice slice:
                        if (slice.Lower != null) Expr(slice.Lower, Context.Load);
                        if (slice.Upper != null) Expr(slice.Upper, Context.Load);
                        if (slice.Step != null) Expr(slice.Step, Context.Load);
                        break;
                    case GeneratedList list:
                        Exprs(list.Elts, context, nullOk: false);
                        break;
                    case GeneratedTuple tuple:
                        Exprs(tuple.Elts, context, nullOk: false);
                        break;
                    case GeneratedNamedExpr namedExpr:
                        if (namedExpr.Target is not GeneratedName)
                            throw PyTypeError.Create("NamedExpr target must be a Name");
                        Expr(namedExpr.Value, Context.Load);
                        break;
                    case GeneratedName:
                        break;
                    default:
                        throw PySystemError.Create("unexpected expression");
                }
            }
            finally
            {
                Leave();
            }
        }

        private static GeneratedExprContext NameContext(GeneratedName name)
        {
            Name(name.Id);
            return name.Ctx;
        }

        // ensure_literal_number
        private static bool LiteralNumber(GeneratedConstant constant, bool real, bool imaginary)
        {
            var value = constant.Value;
            if (value is GeneratedPyConstantObject wrapped)
            {
                var o = wrapped.Value;
                return (real && (o.IsExactly(PyType.FloatType) || o.IsExactly(PyType.IntType)))
                    || (imaginary && o.IsExactly(PyType.ComplexType));
            }
            return (real && value is GeneratedPyConstantFloat or GeneratedPyConstantInt)
                || (imaginary && value is GeneratedPyConstantComplex);
        }

        // ensure_literal_negative
        private static bool LiteralNegative(GeneratedUnaryOp exp, bool real, bool imaginary) =>
            exp.Op is GeneratedUSub && exp.Operand is GeneratedConstant constant && LiteralNumber(constant, real, imaginary);

        // ensure_literal_complex
        private static bool LiteralComplex(GeneratedBinOp exp)
        {
            if (exp.Op is not (GeneratedAdd or GeneratedSub))
                return false;
            bool left = exp.Left switch
            {
                GeneratedConstant constant => LiteralNumber(constant, real: true, imaginary: false),
                GeneratedUnaryOp unary => LiteralNegative(unary, real: true, imaginary: false),
                _ => false,
            };
            return left && exp.Right is GeneratedConstant right && LiteralNumber(right, real: false, imaginary: true);
        }

        // validate_pattern_match_value
        private static void PatternMatchValue(GeneratedExpr exp)
        {
            Expr(exp, Context.Load);
            switch (exp)
            {
                case GeneratedConstant constant:
                    Expr(exp, Context.Load);
                    var literal = constant.Value;
                    if (literal is GeneratedPyConstantInt or GeneratedPyConstantFloat or GeneratedPyConstantBytes
                        or GeneratedPyConstantComplex or GeneratedPyConstantString)
                        return;
                    if (literal is GeneratedPyConstantObject wrapped && (wrapped.Value.IsExactly(PyType.IntType)
                        || wrapped.Value.IsExactly(PyType.FloatType) || wrapped.Value.IsExactly(PyType.BytesType)
                        || wrapped.Value.IsExactly(PyType.ComplexType) || wrapped.Value.IsExactly(PyType.StrType)))
                        return;
                    throw PyValueError.Create("unexpected constant inside of a literal pattern");
                case GeneratedAttribute:
                    return;
                case GeneratedUnaryOp unary when LiteralNegative(unary, real: true, imaginary: true):
                    return;
                case GeneratedBinOp binary when LiteralComplex(binary):
                    return;
                case GeneratedJoinedStr:
                    return;
            }
            throw PyValueError.Create("patterns may only match literals and attribute lookups");
        }

        // validate_capture
        private static void Capture(GeneratedIdentifier name)
        {
            if (name.Value == "_")
                throw PyValueError.Create("can't capture name '_' in patterns");
            Name(name);
        }

        private static bool IsNoneOrBool(GeneratedPyConstant value) =>
            value is GeneratedPyConstantNone or GeneratedPyConstantBool
            || value is GeneratedPyConstantObject { Value: PyNone or PyBool };

        // validate_pattern
        private static void Pattern(GeneratedPattern p, bool starOk)
        {
            Positions(p);
            Enter();
            try
            {
                switch (p)
                {
                    case GeneratedMatchValue matchValue:
                        PatternMatchValue(matchValue.Value);
                        break;
                    case GeneratedMatchSingleton singleton:
                        if (!IsNoneOrBool(singleton.Value))
                            throw PyValueError.Create("MatchSingleton can only contain True, False and None");
                        break;
                    case GeneratedMatchSequence sequence:
                        Patterns(sequence.Patterns, starOk: true);
                        break;
                    case GeneratedMatchMapping mapping:
                        if (mapping.Keys.Count != mapping.Patterns.Count)
                            throw PyValueError.Create("MatchMapping doesn't have the same number of keys as patterns");
                        if (mapping.Rest != null)
                            Capture(mapping.Rest);
                        foreach (GeneratedExpr key in mapping.Keys)
                        {
                            // True, False and None are syntactically valid keys: passed on.
                            if (key is GeneratedConstant { Value: var literal } && IsNoneOrBool(literal))
                                continue;
                            PatternMatchValue(key);
                        }
                        Patterns(mapping.Patterns, starOk: false);
                        break;
                    case GeneratedMatchClass matchClass:
                        if (matchClass.KwdAttrs.Count != matchClass.KwdPatterns.Count)
                            throw PyValueError.Create("MatchClass doesn't have the same number of keyword attributes as patterns");
                        Expr(matchClass.Cls, Context.Load);
                        var cls = matchClass.Cls;
                        while (cls is not GeneratedName)
                        {
                            if (cls is GeneratedAttribute attribute)
                                cls = attribute.Value;
                            else
                                throw PyValueError.Create("MatchClass cls field can only contain Name or Attribute nodes.");
                        }
                        foreach (GeneratedIdentifier identifier in matchClass.KwdAttrs)
                            Name(identifier);
                        Patterns(matchClass.Patterns, starOk: false);
                        Patterns(matchClass.KwdPatterns, starOk: false);
                        break;
                    case GeneratedMatchStar star:
                        if (!starOk)
                            throw PyValueError.Create("can't use MatchStar here");
                        if (star.Name != null)
                            Capture(star.Name);
                        break;
                    case GeneratedMatchAs matchAs:
                        if (matchAs.Name != null)
                            Capture(matchAs.Name);
                        if (matchAs.Pattern == null)
                            break;
                        if (matchAs.Name == null)
                            throw PyValueError.Create("MatchAs must specify a target name if a pattern is given");
                        Pattern(matchAs.Pattern, starOk: false);
                        break;
                    case GeneratedMatchOr matchOr:
                        if (matchOr.Patterns.Count < 2)
                            throw PyValueError.Create("MatchOr requires at least 2 patterns");
                        Patterns(matchOr.Patterns, starOk: false);
                        break;
                    default:
                        throw PySystemError.Create("unexpected pattern");
                }
            }
            finally
            {
                Leave();
            }
        }

        // _validate_nonempty_seq
        private static void NonEmpty(GeneratedSeq seq, string what, string owner)
        {
            if (seq == null || seq.Count == 0)
                throw PyValueError.Create($"empty {what} on {owner}");
        }

        // validate_assignlist
        private static void AssignList(GeneratedExprSeq targets, Context context)
        {
            NonEmpty(targets, "targets", context == Context.Del ? "Delete" : "Assign");
            Exprs(targets, context, nullOk: false);
        }

        // validate_body
        private static void Body(GeneratedStmtSeq body, string owner)
        {
            NonEmpty(body, "body", owner);
            Stmts(body);
        }

        private static void WithItems(GeneratedWithitemSeq items, string owner)
        {
            NonEmpty(items, "items", owner);
            foreach (GeneratedWithitem item in items)
            {
                Expr(item.ContextExpr, Context.Load);
                if (item.OptionalVars != null)
                    Expr(item.OptionalVars, Context.Store);
            }
        }

        private static void TryStatement(GeneratedStmtSeq body, GeneratedExcepthandlerSeq handlers, GeneratedStmtSeq orelse,
            GeneratedStmtSeq finalbody, string owner, bool positions)
        {
            Body(body, owner);
            if (handlers.Count == 0 && finalbody.Count == 0)
                throw PyValueError.Create($"{owner} has neither except handlers nor finalbody");
            if (handlers.Count == 0 && orelse.Count != 0)
                throw PyValueError.Create($"{owner} has orelse but no except handlers");
            foreach (GeneratedExceptHandler handler in handlers)
            {
                if (positions)
                    Positions(handler);
                if (handler.Type != null)
                    Expr(handler.Type, Context.Load);
                Body(handler.Body, "ExceptHandler");
            }
            if (finalbody.Count != 0)
                Stmts(finalbody);
            if (orelse.Count != 0)
                Stmts(orelse);
        }

        // validate_stmt
        private static void Stmt(GeneratedStmt stmt)
        {
            Positions(stmt);
            Enter();
            try
            {
                switch (stmt)
                {
                    case GeneratedFunctionDef function:
                        Body(function.Body, "FunctionDef");
                        TypeParams(function.TypeParams);
                        Arguments(function.Args);
                        Exprs(function.DecoratorList, Context.Load, nullOk: false);
                        if (function.Returns != null) Expr(function.Returns, Context.Load);
                        break;
                    case GeneratedAsyncFunctionDef function:
                        Body(function.Body, "AsyncFunctionDef");
                        TypeParams(function.TypeParams);
                        Arguments(function.Args);
                        Exprs(function.DecoratorList, Context.Load, nullOk: false);
                        if (function.Returns != null) Expr(function.Returns, Context.Load);
                        break;
                    case GeneratedClassDef classDef:
                        Body(classDef.Body, "ClassDef");
                        TypeParams(classDef.TypeParams);
                        Exprs(classDef.Bases, Context.Load, nullOk: false);
                        Keywords(classDef.Keywords);
                        Exprs(classDef.DecoratorList, Context.Load, nullOk: false);
                        break;
                    case GeneratedReturn ret:
                        if (ret.Value != null) Expr(ret.Value, Context.Load);
                        break;
                    case GeneratedDelete delete:
                        AssignList(delete.Targets, Context.Del);
                        break;
                    case GeneratedAssign assign:
                        AssignList(assign.Targets, Context.Store);
                        Expr(assign.Value, Context.Load);
                        break;
                    case GeneratedAugAssign augAssign:
                        Expr(augAssign.Target, Context.Store);
                        Expr(augAssign.Value, Context.Load);
                        break;
                    case GeneratedAnnAssign annAssign:
                        if (annAssign.Target is not GeneratedName && annAssign.Simple != 0)
                            throw PyTypeError.Create("AnnAssign with simple non-Name target");
                        Expr(annAssign.Target, Context.Store);
                        if (annAssign.Value != null) Expr(annAssign.Value, Context.Load);
                        Expr(annAssign.Annotation, Context.Load);
                        break;
                    case GeneratedTypeAlias typeAlias:
                        if (typeAlias.Name is not GeneratedName)
                            throw PyTypeError.Create("TypeAlias with non-Name name");
                        Expr(typeAlias.Name, Context.Store);
                        TypeParams(typeAlias.TypeParams);
                        Expr(typeAlias.Value, Context.Load);
                        break;
                    case GeneratedFor forStmt:
                        Expr(forStmt.Target, Context.Store);
                        Expr(forStmt.Iter, Context.Load);
                        Body(forStmt.Body, "For");
                        Stmts(forStmt.Orelse);
                        break;
                    case GeneratedAsyncFor asyncFor:
                        Expr(asyncFor.Target, Context.Store);
                        Expr(asyncFor.Iter, Context.Load);
                        Body(asyncFor.Body, "AsyncFor");
                        Stmts(asyncFor.Orelse);
                        break;
                    case GeneratedWhile whileStmt:
                        Expr(whileStmt.Test, Context.Load);
                        Body(whileStmt.Body, "While");
                        Stmts(whileStmt.Orelse);
                        break;
                    case GeneratedIf ifStmt:
                        Expr(ifStmt.Test, Context.Load);
                        Body(ifStmt.Body, "If");
                        Stmts(ifStmt.Orelse);
                        break;
                    case GeneratedWith with:
                        WithItems(with.Items, "With");
                        Body(with.Body, "With");
                        break;
                    case GeneratedAsyncWith asyncWith:
                        WithItems(asyncWith.Items, "AsyncWith");
                        Body(asyncWith.Body, "AsyncWith");
                        break;
                    case GeneratedMatch match:
                        Expr(match.Subject, Context.Load);
                        NonEmpty(match.Cases, "cases", "Match");
                        foreach (GeneratedMatchCase matchCase in match.Cases)
                        {
                            Pattern(matchCase.Pattern, starOk: false);
                            if (matchCase.Guard != null) Expr(matchCase.Guard, Context.Load);
                            Body(matchCase.Body, "match_case");
                        }
                        break;
                    case GeneratedRaise raise:
                        if (raise.Exc != null)
                        {
                            Expr(raise.Exc, Context.Load);
                            if (raise.Cause != null) Expr(raise.Cause, Context.Load);
                        }
                        else if (raise.Cause != null)
                        {
                            throw PyValueError.Create("Raise with cause but no exception");
                        }
                        break;
                    case GeneratedTry tryStmt:
                        TryStatement(tryStmt.Body, tryStmt.Handlers, tryStmt.Orelse, tryStmt.Finalbody, "Try", positions: true);
                        break;
                    case GeneratedTryStar tryStar:
                        TryStatement(tryStar.Body, tryStar.Handlers, tryStar.Orelse, tryStar.Finalbody, "TryStar", positions: false);
                        break;
                    case GeneratedAssert assert:
                        Expr(assert.Test, Context.Load);
                        if (assert.Msg != null) Expr(assert.Msg, Context.Load);
                        break;
                    case GeneratedImport import:
                        NonEmpty(import.Names, "names", "Import");
                        break;
                    case GeneratedImportFrom importFrom:
                        if (importFrom.Level < 0)
                            throw PyValueError.Create("Negative ImportFrom level");
                        NonEmpty(importFrom.Names, "names", "ImportFrom");
                        break;
                    case GeneratedGlobal global:
                        NonEmpty(global.Names, "names", "Global");
                        break;
                    case GeneratedNonlocal nonlocal:
                        NonEmpty(nonlocal.Names, "names", "Nonlocal");
                        break;
                    case GeneratedExprStmt expr:
                        Expr(expr.Value, Context.Load);
                        break;
                    case GeneratedPass or GeneratedBreak or GeneratedContinue:
                        break;
                    default:
                        throw PySystemError.Create("unexpected statement");
                }
            }
            finally
            {
                Leave();
            }
        }

        // validate_stmts
        private static void Stmts(GeneratedStmtSeq seq)
        {
            if (seq == null)
                return;
            foreach (var item in seq)
            {
                if (item is GeneratedStmt stmt)
                    Stmt(stmt);
                else
                    throw PyValueError.Create("None disallowed in statement list");
            }
        }

        // validate_exprs
        private static void Exprs(GeneratedExprSeq exprs, Context context, bool nullOk)
        {
            if (exprs == null)
                return;
            foreach (var item in exprs)
            {
                if (item is GeneratedExpr expr)
                    Expr(expr, context);
                else if (!nullOk)
                    throw PyValueError.Create("None disallowed in expression list");
            }
        }

        // validate_patterns
        private static void Patterns(GeneratedPatternSeq patterns, bool starOk)
        {
            foreach (GeneratedPattern pattern in patterns)
                Pattern(pattern, starOk);
        }

        // validate_typeparam and validate_type_params
        private static void TypeParams(GeneratedTypeParamSeq typeParams)
        {
            if (typeParams == null)
                return;
            foreach (var item in typeParams)
            {
                if (item is not GeneratedTypeParam typeParam)
                    continue;
                Positions(typeParam);
                switch (typeParam)
                {
                    case GeneratedTypeVar typeVar:
                        Name(typeVar.Name);
                        if (typeVar.Bound != null) Expr(typeVar.Bound, Context.Load);
                        break;
                    case GeneratedParamSpec paramSpec:
                        Name(paramSpec.Name);
                        break;
                    case GeneratedTypeVarTuple typeVarTuple:
                        Name(typeVarTuple.Name);
                        break;
                }
            }
        }
    }
}
