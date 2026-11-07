// Python/compile.c of CPython v3.12.15: definitions, conditions, loops, return.
using System;
using System.Collections.Generic;
using SharpPy.Generated;
using static SharpPy.PySymtable;

namespace SharpPy
{
    public sealed partial class Compiler
    {
        private enum Ctx { Load, Store, Del }

        private static Ctx CtxOf(GeneratedExprContext ctx) => ctx switch
        {
            GeneratedStore => Ctx.Store,
            GeneratedDel => Ctx.Del,
            _ => Ctx.Load,
        };

        private bool _allowTopLevelAwait = false;
        private bool IsTopLevelAwait => _allowTopLevelAwait && _u.Ste.Type == BlockType.ModuleBlock;

        // compiler_type_params
        private void TypeParams(GeneratedSeq? typeParams)
        {
            int n = Len(typeParams);
            if (typeParams == null)
                return;
            for (int i = 0; i < n; i++)
            {
                var typeparam = (GeneratedTypeParam)typeParams[i];
                var loc = LOC(typeparam);
                switch (typeparam)
                {
                    case GeneratedTypeVar v:
                        ADDOP_LOAD_CONST(loc, PyString.From(v.Name.Value));
                        if (v.Bound != null)
                        {
                            EnterScope(v.Name.Value, ScopeType.TypeParams, typeparam, v.Bound.LineNo);
                            VisitExpr(v.Bound);
                            ADDOP(loc, ByteCodeOp.RETURN_VALUE);
                            var co = OptimizeAndAssemble(true);
                            ExitScope();
                            MakeClosure(loc, co, 0);
                            ADDOP_I(loc, ByteCodeOp.CALL_INTRINSIC_2, (int)(v.Bound is GeneratedTuple
                                ? IntrinsicFunction.INTRINSIC_TYPEVAR_WITH_CONSTRAINTS
                                : IntrinsicFunction.INTRINSIC_TYPEVAR_WITH_BOUND));
                        }
                        else
                        {
                            ADDOP_I(loc, ByteCodeOp.CALL_INTRINSIC_1, (int)IntrinsicFunction.INTRINSIC_TYPEVAR);
                        }
                        ADDOP_I(loc, ByteCodeOp.COPY, 1);
                        Nameop(loc, v.Name.Value, Ctx.Store);
                        break;
                    case GeneratedTypeVarTuple v:
                        ADDOP_LOAD_CONST(loc, PyString.From(v.Name.Value));
                        ADDOP_I(loc, ByteCodeOp.CALL_INTRINSIC_1, (int)IntrinsicFunction.INTRINSIC_TYPEVARTUPLE);
                        ADDOP_I(loc, ByteCodeOp.COPY, 1);
                        Nameop(loc, v.Name.Value, Ctx.Store);
                        break;
                    case GeneratedParamSpec v:
                        ADDOP_LOAD_CONST(loc, PyString.From(v.Name.Value));
                        ADDOP_I(loc, ByteCodeOp.CALL_INTRINSIC_1, (int)IntrinsicFunction.INTRINSIC_PARAMSPEC);
                        ADDOP_I(loc, ByteCodeOp.COPY, 1);
                        Nameop(loc, v.Name.Value, Ctx.Store);
                        break;
                }
            }
            ADDOP_I(LOC((GeneratedTypeParam)typeParams[0]), ByteCodeOp.BUILD_TUPLE, n);
        }

        // compiler_function_body
        private void FunctionBody(GeneratedStmt s, bool isAsync, GeneratedArguments args, string name, GeneratedSeq body,
            int funcflags, int firstlineno)
        {
            EnterScope(name, isAsync ? ScopeType.AsyncFunction : ScopeType.Function, s, firstlineno);

            GeneratedExpr? docstring = _optimize < 2 ? PyFuture.GetDocString(body) : null;
            AddConst(docstring != null
                ? ConstantValue(((GeneratedConstant)docstring).Value)
                : PyNone.Instance);

            _u.ArgCount = Len(args.Args);
            _u.PosonlyArgCount = Len(args.Posonlyargs);
            _u.KwonlyArgCount = Len(args.Kwonlyargs);

            var start = NewLabel();
            UseLabel(start);
            bool addStopiterationHandler = _u.Ste.Coroutine || _u.Ste.Generator;
            if (addStopiterationHandler)
                PushFBlock(Loc.NoLocation, FBlockType.STOP_ITERATION, start, Label.NoLabel, null);

            for (int i = docstring != null ? 1 : 0; i < Len(body); i++)
                VisitStmt((GeneratedStmt)body[i]);
            if (addStopiterationHandler)
            {
                WrapInStopiterationHandler();
                PopFBlock(FBlockType.STOP_ITERATION, start);
            }
            var co = OptimizeAndAssemble(true);
            ExitScope();
            MakeClosure(LOC(s), co, funcflags);
        }

        // compiler_function
        private void Function(GeneratedStmt s, bool isAsync, GeneratedArguments args, GeneratedExpr? returns,
            GeneratedSeq decos, string name, GeneratedSeq typeParams, GeneratedSeq body)
        {
            CheckDebugArgs(args);
            Decorators(decos);

            int firstlineno = s.LineNo;
            if (Len(decos) != 0)
                firstlineno = ((GeneratedExpr)decos[0]).LineNo;

            var loc = LOC(s);
            bool isGeneric = Len(typeParams) > 0;
            if (isGeneric)
                ADDOP(loc, ByteCodeOp.PUSH_NULL);

            int funcflags = DefaultArguments(loc, args);
            int numTypeparamArgs = 0;

            if (isGeneric)
            {
                if ((funcflags & 0x01) != 0)
                    numTypeparamArgs += 1;
                if ((funcflags & 0x02) != 0)
                    numTypeparamArgs += 1;
                if (numTypeparamArgs == 2)
                    ADDOP_I(loc, ByteCodeOp.SWAP, 2);
                EnterScope($"<generic parameters of {name}>", ScopeType.TypeParams, typeParams, firstlineno);
                TypeParams(typeParams);
                if ((funcflags & 0x01) != 0 || (funcflags & 0x02) != 0)
                    ADDOP_I(loc, ByteCodeOp.LOAD_FAST, 0);
                if ((funcflags & 0x01) != 0 && (funcflags & 0x02) != 0)
                    ADDOP_I(loc, ByteCodeOp.LOAD_FAST, 1);
            }

            if (VisitAnnotations(loc, args, returns) > 0)
                funcflags |= 0x04;

            FunctionBody(s, isAsync, args, name, body, funcflags, firstlineno);

            if (isGeneric)
            {
                ADDOP_I(loc, ByteCodeOp.SWAP, 2);
                ADDOP_I(loc, ByteCodeOp.CALL_INTRINSIC_2, (int)IntrinsicFunction.INTRINSIC_SET_FUNCTION_TYPE_PARAMS);

                _u.ArgCount = numTypeparamArgs;
                var co = OptimizeAndAssemble(false);
                ExitScope();
                MakeClosure(loc, co, 0);
                if (numTypeparamArgs > 0)
                    ADDOP_I(loc, ByteCodeOp.SWAP, numTypeparamArgs + 1);
                ADDOP_I(loc, ByteCodeOp.CALL, numTypeparamArgs);
            }

            ApplyDecorators(decos);
            Nameop(loc, name, Ctx.Store);
        }

        // compiler_class_body
        private void ClassBody(GeneratedClassDef s, int firstlineno)
        {
            EnterScope(s.Name.Value, ScopeType.Class, s, firstlineno);

            var loc = LOCATION(firstlineno, firstlineno, 0, 0);
            _u.Private = s.Name.Value;
            Nameop(loc, "__name__", Ctx.Load);
            Nameop(loc, "__module__", Ctx.Store);
            ADDOP_LOAD_CONST(loc, PyString.From(_u.QualName));
            Nameop(loc, "__qualname__", Ctx.Store);
            if (Len(s.TypeParams) > 0)
            {
                Nameop(loc, ".type_params", Ctx.Load);
                Nameop(loc, "__type_params__", Ctx.Store);
            }
            if (_u.Ste.NeedsClassdict)
            {
                ADDOP(loc, ByteCodeOp.LOAD_LOCALS);
                ADDOP_I(loc, ByteCodeOp.STORE_DEREF, DictAdd(_u.Cellvars, "__classdict__"));
            }
            CompilerBody(loc, s.Body);
            if (_u.Ste.NeedsClassdict)
            {
                ADDOP_I(Loc.NoLocation, ByteCodeOp.LOAD_CLOSURE, _u.Cellvars["__classdict__"]);
                Nameop(Loc.NoLocation, "__classdictcell__", Ctx.Store);
            }
            if (_u.Ste.NeedsClassClosure)
            {
                ADDOP_I(Loc.NoLocation, ByteCodeOp.LOAD_CLOSURE, _u.Cellvars["__class__"]);
                ADDOP_I(Loc.NoLocation, ByteCodeOp.COPY, 1);
                Nameop(Loc.NoLocation, "__classcell__", Ctx.Store);
            }
            else
            {
                ADDOP_LOAD_CONST(Loc.NoLocation, PyNone.Instance);
            }
            ADDOP(Loc.NoLocation, ByteCodeOp.RETURN_VALUE);
            var co = OptimizeAndAssemble(true);
            ExitScope();

            loc = LOC(s);
            ADDOP(loc, ByteCodeOp.PUSH_NULL);
            ADDOP(loc, ByteCodeOp.LOAD_BUILD_CLASS);
            MakeClosure(loc, co, 0);
            ADDOP_LOAD_CONST(loc, PyString.From(s.Name.Value));
        }

        // compiler_class
        private void Class(GeneratedClassDef s)
        {
            var decos = s.DecoratorList;
            Decorators(decos);

            int firstlineno = s.LineNo;
            if (Len(decos) != 0)
                firstlineno = ((GeneratedExpr)decos[0]).LineNo;
            var loc = LOC(s);

            bool isGeneric = Len(s.TypeParams) > 0;
            if (isGeneric)
            {
                ADDOP(loc, ByteCodeOp.PUSH_NULL);
                EnterScope($"<generic parameters of {s.Name.Value}>", ScopeType.TypeParams, s.TypeParams, firstlineno);
                _u.Private = s.Name.Value;
                TypeParams(s.TypeParams);
                Nameop(loc, ".type_params", Ctx.Store);
            }

            ClassBody(s, firstlineno);

            if (isGeneric)
            {
                Nameop(loc, ".type_params", Ctx.Load);
                ADDOP_I(loc, ByteCodeOp.CALL_INTRINSIC_1, (int)IntrinsicFunction.INTRINSIC_SUBSCRIPT_GENERIC);
                Nameop(loc, ".generic_base", Ctx.Store);

                var bases = new GeneratedExprSeq();
                for (int i = 0; i < Len(s.Bases); i++)
                    bases.Add(s.Bases[i]);
                bases.Add(new GeneratedName
                {
                    Id = new GeneratedIdentifier(".generic_base"),
                    Ctx = GeneratedLoad.Instance,
                    LineNo = loc.Line, EndLineNo = loc.EndLine, ColOffset = loc.Col, EndColOffset = loc.EndCol,
                });
                CallHelper(loc, 2, bases, s.Keywords);

                var co = OptimizeAndAssemble(false);
                ExitScope();
                MakeClosure(loc, co, 0);
                ADDOP_I(loc, ByteCodeOp.CALL, 0);
            }
            else
            {
                CallHelper(loc, 2, s.Bases, s.Keywords);
            }

            ApplyDecorators(decos);
            Nameop(loc, s.Name.Value, Ctx.Store);
        }

        // compiler_typealias_body
        private void TypealiasBody(GeneratedTypeAlias s)
        {
            var loc = LOC(s);
            string name = ((GeneratedName)s.Name).Id.Value;
            EnterScope(name, ScopeType.Function, s, loc.Line);
            AddConst(PyNone.Instance);
            VisitExpr(s.Value);
            ADDOP(loc, ByteCodeOp.RETURN_VALUE);
            var co = OptimizeAndAssemble(false);
            ExitScope();
            MakeClosure(loc, co, 0);
            ADDOP_I(loc, ByteCodeOp.BUILD_TUPLE, 3);
            ADDOP_I(loc, ByteCodeOp.CALL_INTRINSIC_1, (int)IntrinsicFunction.INTRINSIC_TYPEALIAS);
        }

        // compiler_typealias
        private void Typealias(GeneratedTypeAlias s)
        {
            var loc = LOC(s);
            bool isGeneric = Len(s.TypeParams) > 0;
            string name = ((GeneratedName)s.Name).Id.Value;
            if (isGeneric)
            {
                ADDOP(loc, ByteCodeOp.PUSH_NULL);
                EnterScope($"<generic parameters of {name}>", ScopeType.TypeParams, s.TypeParams, loc.Line);
                ADDOP_LOAD_CONST(loc, PyString.From(name));
                TypeParams(s.TypeParams);
            }
            else
            {
                ADDOP_LOAD_CONST(loc, PyString.From(name));
                ADDOP_LOAD_CONST(loc, PyNone.Instance);
            }

            TypealiasBody(s);

            if (isGeneric)
            {
                var co = OptimizeAndAssemble(false);
                ExitScope();
                MakeClosure(loc, co, 0);
                ADDOP_I(loc, ByteCodeOp.CALL, 0);
            }
            Nameop(loc, name, Ctx.Store);
        }

        // check_is_arg
        private static bool CheckIsArg(GeneratedExpr e) =>
            e is not GeneratedConstant constant ||
            constant.Value is GeneratedPyConstantNone or GeneratedPyConstantBool or GeneratedPyConstantEllipsis;

        // check_compare
        private void CheckCompare(GeneratedCompare e)
        {
            bool left = CheckIsArg(e.Left);
            var leftExpr = e.Left;
            for (int i = 0; i < Len(e.Ops); i++)
            {
                var op = e.Ops[i];
                var rightExpr = (GeneratedExpr)e.Comparators[i];
                bool right = CheckIsArg(rightExpr);
                if (op is GeneratedIs or GeneratedIsNot && (!right || !left))
                {
                    var literal = !left ? leftExpr : rightExpr;
                    string typeName = InferType(literal);
                    Warn(LOC(e), op is GeneratedIs
                        ? $"\"is\" with '{typeName}' literal. Did you mean \"==\"?"
                        : $"\"is not\" with '{typeName}' literal. Did you mean \"!=\"?");
                    return;
                }
                left = right;
                leftExpr = rightExpr;
            }
        }

        // compiler_addcompare
        private void ADDOP_COMPARE(Loc loc, GeneratedPtr op)
        {
            const int LessThan = 2, GreaterThan = 4, Equals = 8, NotEquals = 7;
            int cmp, mask;
            switch (op)
            {
                case GeneratedEq: cmp = 2; mask = Equals; break;
                case GeneratedNotEq: cmp = 3; mask = NotEquals; break;
                case GeneratedLt: cmp = 0; mask = LessThan; break;
                case GeneratedLtE: cmp = 1; mask = LessThan | Equals; break;
                case GeneratedGt: cmp = 4; mask = GreaterThan; break;
                case GeneratedGtE: cmp = 5; mask = GreaterThan | Equals; break;
                case GeneratedIs:
                    ADDOP_I(loc, ByteCodeOp.IS_OP, 0);
                    return;
                case GeneratedIsNot:
                    ADDOP_I(loc, ByteCodeOp.IS_OP, 1);
                    return;
                case GeneratedIn:
                    ADDOP_I(loc, ByteCodeOp.CONTAINS_OP, 0);
                    return;
                case GeneratedNotIn:
                    ADDOP_I(loc, ByteCodeOp.CONTAINS_OP, 1);
                    return;
                default:
                    throw new InvalidOperationException("unreachable");
            }
            ADDOP_I(loc, ByteCodeOp.COMPARE_OP, (cmp << 4) | mask);
        }

        // compiler_jump_if
        private void JumpIf(Loc loc, GeneratedExpr e, Label next, bool cond)
        {
            switch (e)
            {
                case GeneratedUnaryOp { Op: GeneratedNot } unary:
                    JumpIf(loc, unary.Operand, next, !cond);
                    return;
                case GeneratedBoolOp boolOp:
                {
                    var s = boolOp.Values;
                    int n = s.Count - 1;
                    bool cond2 = boolOp.Op is GeneratedOr;
                    var next2 = next;
                    if (cond2 != cond)
                        next2 = NewLabel();
                    for (int i = 0; i < n; i++)
                        JumpIf(loc, (GeneratedExpr)s[i], next2, cond2);
                    JumpIf(loc, (GeneratedExpr)s[n], next, cond);
                    if (next2 != next)
                        UseLabel(next2);
                    return;
                }
                case GeneratedIfExp ifExp:
                {
                    var end = NewLabel();
                    var next2 = NewLabel();
                    JumpIf(loc, ifExp.Test, next2, false);
                    JumpIf(loc, ifExp.Body, next, cond);
                    ADDOP_JUMP(Loc.NoLocation, ByteCodeOp.JUMP, end);
                    UseLabel(next2);
                    JumpIf(loc, ifExp.Orelse, next, cond);
                    UseLabel(end);
                    return;
                }
                case GeneratedCompare compare when Len(compare.Ops) - 1 > 0:
                {
                    int n = compare.Ops.Count - 1;
                    var at = LOC(e);
                    CheckCompare(compare);
                    var cleanup = NewLabel();
                    VisitExpr(compare.Left);
                    for (int i = 0; i < n; i++)
                    {
                        VisitExpr((GeneratedExpr)compare.Comparators[i]);
                        ADDOP_I(at, ByteCodeOp.SWAP, 2);
                        ADDOP_I(at, ByteCodeOp.COPY, 2);
                        ADDOP_COMPARE(at, compare.Ops[i]);
                        ADDOP_JUMP(at, ByteCodeOp.POP_JUMP_IF_FALSE, cleanup);
                    }
                    VisitExpr((GeneratedExpr)compare.Comparators[n]);
                    ADDOP_COMPARE(at, compare.Ops[n]);
                    ADDOP_JUMP(at, cond ? ByteCodeOp.POP_JUMP_IF_TRUE : ByteCodeOp.POP_JUMP_IF_FALSE, next);
                    var end = NewLabel();
                    ADDOP_JUMP(Loc.NoLocation, ByteCodeOp.JUMP, end);
                    UseLabel(cleanup);
                    ADDOP(at, ByteCodeOp.POP_TOP);
                    if (!cond)
                        ADDOP_JUMP(Loc.NoLocation, ByteCodeOp.JUMP, next);
                    UseLabel(end);
                    return;
                }
            }

            VisitExpr(e);
            ADDOP_JUMP(LOC(e), cond ? ByteCodeOp.POP_JUMP_IF_TRUE : ByteCodeOp.POP_JUMP_IF_FALSE, next);
        }

        private void Ifexp(GeneratedIfExp e)
        {
            var end = NewLabel();
            var next = NewLabel();
            JumpIf(LOC(e), e.Test, next, false);
            VisitExpr(e.Body);
            ADDOP_JUMP(Loc.NoLocation, ByteCodeOp.JUMP, end);
            UseLabel(next);
            VisitExpr(e.Orelse);
            UseLabel(end);
        }

        private void Lambda(GeneratedLambda e)
        {
            var args = e.Args;
            CheckDebugArgs(args);
            var loc = LOC(e);
            int funcflags = DefaultArguments(loc, args);

            EnterScope("<lambda>", ScopeType.Lambda, e, e.LineNo);
            AddConst(PyNone.Instance);

            _u.ArgCount = Len(args.Args);
            _u.PosonlyArgCount = Len(args.Posonlyargs);
            _u.KwonlyArgCount = Len(args.Kwonlyargs);
            VisitExpr(e.Body);
            PyCodeObject co;
            if (_u.Ste.Generator)
            {
                co = OptimizeAndAssemble(false);
            }
            else
            {
                ADDOP(LOC(e.Body), ByteCodeOp.RETURN_VALUE);
                co = OptimizeAndAssemble(true);
            }
            ExitScope();
            MakeClosure(loc, co, funcflags);
        }

        private void If(GeneratedIf s)
        {
            var end = NewLabel();
            var next = Len(s.Orelse) != 0 ? NewLabel() : end;
            JumpIf(LOC(s), s.Test, next, false);
            VisitStmts(s.Body);
            if (Len(s.Orelse) != 0)
            {
                ADDOP_JUMP(Loc.NoLocation, ByteCodeOp.JUMP, end);
                UseLabel(next);
                VisitStmts(s.Orelse);
            }
            UseLabel(end);
        }

        private void For(GeneratedFor s)
        {
            var loc = LOC(s);
            var start = NewLabel();
            var body = NewLabel();
            var cleanup = NewLabel();
            var end = NewLabel();

            PushFBlock(loc, FBlockType.FOR_LOOP, start, end, null);
            VisitExpr(s.Iter);

            loc = LOC(s.Iter);
            ADDOP(loc, ByteCodeOp.GET_ITER);

            UseLabel(start);
            ADDOP_JUMP(loc, ByteCodeOp.FOR_ITER, cleanup);

            ADDOP(LOC(s.Target), ByteCodeOp.NOP);

            UseLabel(body);
            VisitExpr(s.Target);
            VisitStmts(s.Body);
            ADDOP_JUMP(Loc.NoLocation, ByteCodeOp.JUMP, start);

            UseLabel(cleanup);
            ADDOP(Loc.NoLocation, ByteCodeOp.END_FOR);

            PopFBlock(FBlockType.FOR_LOOP, start);
            VisitStmts(s.Orelse);
            UseLabel(end);
        }

        private void AsyncFor(GeneratedAsyncFor s)
        {
            var loc = LOC(s);
            if (IsTopLevelAwait)
                _u.Ste.Coroutine = true;
            else if (_u.ScopeKind != ScopeType.AsyncFunction)
                throw Error(loc, "'async for' outside async function");

            var start = NewLabel();
            var except = NewLabel();
            var end = NewLabel();

            VisitExpr(s.Iter);
            ADDOP(LOC(s.Iter), ByteCodeOp.GET_AITER);

            UseLabel(start);
            PushFBlock(loc, FBlockType.FOR_LOOP, start, end, null);

            ADDOP_JUMP(loc, ByteCodeOp.SETUP_FINALLY, except);
            ADDOP(loc, ByteCodeOp.GET_ANEXT);
            ADDOP_LOAD_CONST(loc, PyNone.Instance);
            ADD_YIELD_FROM(loc, true);
            ADDOP(loc, ByteCodeOp.POP_BLOCK);

            VisitExpr(s.Target);
            VisitStmts(s.Body);
            ADDOP_JUMP(Loc.NoLocation, ByteCodeOp.JUMP, start);

            PopFBlock(FBlockType.FOR_LOOP, start);

            UseLabel(except);
            ADDOP(LOC(s.Iter), ByteCodeOp.END_ASYNC_FOR);

            VisitStmts(s.Orelse);
            UseLabel(end);
        }

        private void While(GeneratedWhile s)
        {
            var loop = NewLabel();
            var body = NewLabel();
            var end = NewLabel();
            var anchor = NewLabel();

            UseLabel(loop);
            PushFBlock(LOC(s), FBlockType.WHILE_LOOP, loop, end, null);
            JumpIf(LOC(s), s.Test, anchor, false);

            UseLabel(body);
            VisitStmts(s.Body);
            JumpIf(LOC(s), s.Test, body, true);

            PopFBlock(FBlockType.WHILE_LOOP, loop);

            UseLabel(anchor);
            VisitStmts(s.Orelse);
            UseLabel(end);
        }

        private void Return(GeneratedReturn s)
        {
            var loc = LOC(s);
            bool preserveTos = s.Value != null && s.Value is not GeneratedConstant;
            if (!_u.Ste.IsFunctionLike)
                throw Error(loc, "'return' outside function");
            if (s.Value != null && _u.Ste.Coroutine && _u.Ste.Generator)
                throw Error(loc, "'return' with value in async generator");

            if (preserveTos)
            {
                VisitExpr(s.Value!);
            }
            else if (s.Value != null)
            {
                loc = LOC(s.Value);
                ADDOP(loc, ByteCodeOp.NOP);
            }
            if (s.Value == null || s.Value.LineNo != s.LineNo)
            {
                loc = LOC(s);
                ADDOP(loc, ByteCodeOp.NOP);
            }

            FBlockInfo? none = null;
            UnwindFBlockStack(ref loc, preserveTos, ref none, false);
            if (s.Value == null)
                ADDOP_LOAD_CONST(loc, PyNone.Instance);
            else if (!preserveTos)
                ADDOP_LOAD_CONST(loc, ConstantValue(((GeneratedConstant)s.Value).Value));
            ADDOP(loc, ByteCodeOp.RETURN_VALUE);
        }

        private void Break(Loc loc)
        {
            FBlockInfo? loop = null;
            var origin = loc;
            ADDOP(loc, ByteCodeOp.NOP);
            UnwindFBlockStack(ref loc, false, ref loop, true);
            if (loop == null)
                throw Error(origin, "'break' outside loop");
            UnwindFBlock(ref loc, loop, false);
            ADDOP_JUMP(loc, ByteCodeOp.JUMP, loop.Exit);
        }

        private void Continue(Loc loc)
        {
            FBlockInfo? loop = null;
            var origin = loc;
            ADDOP(loc, ByteCodeOp.NOP);
            UnwindFBlockStack(ref loc, false, ref loop, true);
            if (loop == null)
                throw Error(origin, "'continue' not properly in loop");
            ADDOP_JUMP(loc, ByteCodeOp.JUMP, loop.Block);
        }
    }
}
