// Python/compile.c of CPython v3.12.15: try, import, statements, names, collections.
using System;
using System.Collections.Generic;
using SharpPy.Generated;
using static SharpPy.PySymtable;

namespace SharpPy
{
    public sealed partial class Compiler
    {
        private const int STACK_USE_GUIDELINE = 30;

        private void TryFinally(Loc loc, GeneratedSeq finalbody, Action tryBody)
        {
            var body = NewLabel();
            var end = NewLabel();
            var exit = NewLabel();
            var cleanup = NewLabel();

            ADDOP_JUMP(loc, ByteCodeOp.SETUP_FINALLY, end);

            UseLabel(body);
            PushFBlock(loc, FBlockType.FINALLY_TRY, body, end, finalbody);
            tryBody();
            ADDOP(Loc.NoLocation, ByteCodeOp.POP_BLOCK);
            PopFBlock(FBlockType.FINALLY_TRY, body);
            VisitStmts(finalbody);

            ADDOP_JUMP(Loc.NoLocation, ByteCodeOp.JUMP, exit);

            UseLabel(end);
            loc = Loc.NoLocation;
            ADDOP_JUMP(loc, ByteCodeOp.SETUP_CLEANUP, cleanup);
            ADDOP(loc, ByteCodeOp.PUSH_EXC_INFO);
            PushFBlock(loc, FBlockType.FINALLY_END, end, Label.NoLabel, null);
            VisitStmts(finalbody);
            PopFBlock(FBlockType.FINALLY_END, end);

            loc = Loc.NoLocation;
            ADDOP_I(loc, ByteCodeOp.RERAISE, 0);

            UseLabel(cleanup);
            POP_EXCEPT_AND_RERAISE(loc);

            UseLabel(exit);
        }

        // compiler_try_except
        private void TryExcept(GeneratedStmt s, GeneratedSeq tryBody, GeneratedSeq? orelseBody, GeneratedSeq handlers)
        {
            var loc = LOC(s);
            var body = NewLabel();
            var except = NewLabel();
            var end = NewLabel();
            var cleanup = NewLabel();

            ADDOP_JUMP(loc, ByteCodeOp.SETUP_FINALLY, except);

            UseLabel(body);
            PushFBlock(loc, FBlockType.TRY_EXCEPT, body, Label.NoLabel, null);
            VisitStmts(tryBody);
            PopFBlock(FBlockType.TRY_EXCEPT, body);
            ADDOP(Loc.NoLocation, ByteCodeOp.POP_BLOCK);
            VisitStmts(orelseBody);
            ADDOP_JUMP(Loc.NoLocation, ByteCodeOp.JUMP, end);
            int n = Len(handlers);

            UseLabel(except);

            ADDOP_JUMP(Loc.NoLocation, ByteCodeOp.SETUP_CLEANUP, cleanup);
            ADDOP(Loc.NoLocation, ByteCodeOp.PUSH_EXC_INFO);

            PushFBlock(loc, FBlockType.EXCEPTION_HANDLER, Label.NoLabel, Label.NoLabel, null);

            for (int i = 0; i < n; i++)
            {
                var handler = (GeneratedExceptHandler)handlers[i];
                var at = LOC(handler);
                if (handler.Type == null && i < n - 1)
                    throw Error(at, "default 'except:' must be last");
                except = NewLabel();
                if (handler.Type != null)
                {
                    VisitExpr(handler.Type);
                    ADDOP(at, ByteCodeOp.CHECK_EXC_MATCH);
                    ADDOP_JUMP(at, ByteCodeOp.POP_JUMP_IF_FALSE, except);
                }
                if (handler.Name != null)
                {
                    string name = handler.Name.Value;
                    var cleanupEnd = NewLabel();
                    var cleanupBody = NewLabel();

                    Nameop(at, name, Ctx.Store);
                    ADDOP_JUMP(at, ByteCodeOp.SETUP_CLEANUP, cleanupEnd);

                    UseLabel(cleanupBody);
                    PushFBlock(at, FBlockType.HANDLER_CLEANUP, cleanupBody, Label.NoLabel, name);

                    VisitStmts(handler.Body);
                    PopFBlock(FBlockType.HANDLER_CLEANUP, cleanupBody);
                    ADDOP(Loc.NoLocation, ByteCodeOp.POP_BLOCK);
                    ADDOP(Loc.NoLocation, ByteCodeOp.POP_BLOCK);
                    ADDOP(Loc.NoLocation, ByteCodeOp.POP_EXCEPT);
                    ADDOP_LOAD_CONST(Loc.NoLocation, PyNone.Instance);
                    Nameop(Loc.NoLocation, name, Ctx.Store);
                    Nameop(Loc.NoLocation, name, Ctx.Del);
                    ADDOP_JUMP(Loc.NoLocation, ByteCodeOp.JUMP, end);

                    UseLabel(cleanupEnd);
                    ADDOP_LOAD_CONST(Loc.NoLocation, PyNone.Instance);
                    Nameop(Loc.NoLocation, name, Ctx.Store);
                    Nameop(Loc.NoLocation, name, Ctx.Del);
                    ADDOP_I(Loc.NoLocation, ByteCodeOp.RERAISE, 1);
                }
                else
                {
                    var cleanupBody = NewLabel();

                    ADDOP(at, ByteCodeOp.POP_TOP);

                    UseLabel(cleanupBody);
                    PushFBlock(at, FBlockType.HANDLER_CLEANUP, cleanupBody, Label.NoLabel, null);

                    VisitStmts(handler.Body);
                    PopFBlock(FBlockType.HANDLER_CLEANUP, cleanupBody);
                    ADDOP(Loc.NoLocation, ByteCodeOp.POP_BLOCK);
                    ADDOP(Loc.NoLocation, ByteCodeOp.POP_EXCEPT);
                    ADDOP_JUMP(Loc.NoLocation, ByteCodeOp.JUMP, end);
                }

                UseLabel(except);
            }
            PopFBlock(FBlockType.EXCEPTION_HANDLER, Label.NoLabel);
            ADDOP_I(Loc.NoLocation, ByteCodeOp.RERAISE, 0);

            UseLabel(cleanup);
            POP_EXCEPT_AND_RERAISE(Loc.NoLocation);

            UseLabel(end);
        }

        // compiler_try_star_except
        private void TryStarExcept(GeneratedTryStar s)
        {
            var loc = LOC(s);
            var body = NewLabel();
            var except = NewLabel();
            var orelse = NewLabel();
            var end = NewLabel();
            var cleanup = NewLabel();
            var reraiseStar = NewLabel();

            ADDOP_JUMP(loc, ByteCodeOp.SETUP_FINALLY, except);

            UseLabel(body);
            PushFBlock(loc, FBlockType.TRY_EXCEPT, body, Label.NoLabel, null);
            VisitStmts(s.Body);
            PopFBlock(FBlockType.TRY_EXCEPT, body);
            ADDOP(Loc.NoLocation, ByteCodeOp.POP_BLOCK);
            ADDOP_JUMP(Loc.NoLocation, ByteCodeOp.JUMP, orelse);
            int n = Len(s.Handlers);

            UseLabel(except);

            ADDOP_JUMP(Loc.NoLocation, ByteCodeOp.SETUP_CLEANUP, cleanup);
            ADDOP(Loc.NoLocation, ByteCodeOp.PUSH_EXC_INFO);

            PushFBlock(loc, FBlockType.EXCEPTION_GROUP_HANDLER, Label.NoLabel, Label.NoLabel, "except handler");

            for (int i = 0; i < n; i++)
            {
                var handler = (GeneratedExceptHandler)s.Handlers[i];
                var at = LOC(handler);
                except = NewLabel();
                var exceptWithError = NewLabel();
                var noMatch = NewLabel();
                if (i == 0)
                {
                    ADDOP_I(at, ByteCodeOp.BUILD_LIST, 0);
                    ADDOP_I(at, ByteCodeOp.COPY, 2);
                }
                if (handler.Type != null)
                {
                    VisitExpr(handler.Type);
                    ADDOP(at, ByteCodeOp.CHECK_EG_MATCH);
                    ADDOP_I(at, ByteCodeOp.COPY, 1);
                    ADDOP_JUMP(at, ByteCodeOp.POP_JUMP_IF_NONE, noMatch);
                }

                var cleanupEnd = NewLabel();
                var cleanupBody = NewLabel();
                string? name = handler.Name?.Value;

                if (name != null)
                    Nameop(at, name, Ctx.Store);
                else
                    ADDOP(at, ByteCodeOp.POP_TOP);

                ADDOP_JUMP(at, ByteCodeOp.SETUP_CLEANUP, cleanupEnd);

                UseLabel(cleanupBody);
                PushFBlock(at, FBlockType.HANDLER_CLEANUP, cleanupBody, Label.NoLabel, name);

                VisitStmts(handler.Body);
                PopFBlock(FBlockType.HANDLER_CLEANUP, cleanupBody);
                ADDOP(Loc.NoLocation, ByteCodeOp.POP_BLOCK);
                if (name != null)
                {
                    ADDOP_LOAD_CONST(Loc.NoLocation, PyNone.Instance);
                    Nameop(Loc.NoLocation, name, Ctx.Store);
                    Nameop(Loc.NoLocation, name, Ctx.Del);
                }
                ADDOP_JUMP(Loc.NoLocation, ByteCodeOp.JUMP, except);

                UseLabel(cleanupEnd);
                if (name != null)
                {
                    ADDOP_LOAD_CONST(Loc.NoLocation, PyNone.Instance);
                    Nameop(Loc.NoLocation, name, Ctx.Store);
                    Nameop(Loc.NoLocation, name, Ctx.Del);
                }

                ADDOP_I(Loc.NoLocation, ByteCodeOp.LIST_APPEND, 3);
                ADDOP(Loc.NoLocation, ByteCodeOp.POP_TOP);
                ADDOP_JUMP(Loc.NoLocation, ByteCodeOp.JUMP, exceptWithError);

                UseLabel(except);
                ADDOP(Loc.NoLocation, ByteCodeOp.NOP);
                ADDOP_JUMP(Loc.NoLocation, ByteCodeOp.JUMP, exceptWithError);

                UseLabel(noMatch);
                ADDOP(at, ByteCodeOp.POP_TOP);

                UseLabel(exceptWithError);

                if (i == n - 1)
                {
                    ADDOP_I(Loc.NoLocation, ByteCodeOp.LIST_APPEND, 1);
                    ADDOP_JUMP(Loc.NoLocation, ByteCodeOp.JUMP, reraiseStar);
                }
            }
            PopFBlock(FBlockType.EXCEPTION_GROUP_HANDLER, Label.NoLabel);
            var reraise = NewLabel();

            UseLabel(reraiseStar);
            ADDOP_I(Loc.NoLocation, ByteCodeOp.CALL_INTRINSIC_2, (int)IntrinsicFunction.INTRINSIC_PREP_RERAISE_STAR);
            ADDOP_I(Loc.NoLocation, ByteCodeOp.COPY, 1);
            ADDOP_JUMP(Loc.NoLocation, ByteCodeOp.POP_JUMP_IF_NOT_NONE, reraise);

            ADDOP(Loc.NoLocation, ByteCodeOp.POP_TOP);
            ADDOP(Loc.NoLocation, ByteCodeOp.POP_BLOCK);
            ADDOP(Loc.NoLocation, ByteCodeOp.POP_EXCEPT);
            ADDOP_JUMP(Loc.NoLocation, ByteCodeOp.JUMP, end);

            UseLabel(reraise);
            ADDOP(Loc.NoLocation, ByteCodeOp.POP_BLOCK);
            ADDOP_I(Loc.NoLocation, ByteCodeOp.SWAP, 2);
            ADDOP(Loc.NoLocation, ByteCodeOp.POP_EXCEPT);
            ADDOP_I(Loc.NoLocation, ByteCodeOp.RERAISE, 0);

            UseLabel(cleanup);
            POP_EXCEPT_AND_RERAISE(Loc.NoLocation);

            UseLabel(orelse);
            VisitStmts(s.Orelse);

            UseLabel(end);
        }

        private void Try(GeneratedTry s)
        {
            if (Len(s.Finalbody) == 0)
            {
                TryExcept(s, s.Body, s.Orelse, s.Handlers);
                return;
            }
            TryFinally(LOC(s), s.Finalbody, () =>
            {
                if (Len(s.Handlers) != 0)
                    TryExcept(s, s.Body, s.Orelse, s.Handlers);
                else
                    VisitStmts(s.Body);
            });
        }

        private void TryStar(GeneratedTryStar s)
        {
            if (Len(s.Finalbody) == 0)
            {
                TryStarExcept(s);
                return;
            }
            TryFinally(LOC(s), s.Finalbody, () =>
            {
                if (Len(s.Handlers) != 0)
                    TryStarExcept(s);
                else
                    VisitStmts(s.Body);
            });
        }

        private void ImportAs(Loc loc, string name, string asname)
        {
            int dot = name.IndexOf('.');
            if (dot != -1)
            {
                while (true)
                {
                    int pos = dot + 1;
                    dot = name.IndexOf('.', pos);
                    string attr = dot != -1 ? name.Substring(pos, dot - pos) : name.Substring(pos);
                    ADDOP_I(loc, ByteCodeOp.IMPORT_FROM, DictAdd(_u.Names, attr));
                    if (dot == -1)
                        break;
                    ADDOP_I(loc, ByteCodeOp.SWAP, 2);
                    ADDOP(loc, ByteCodeOp.POP_TOP);
                }
                Nameop(loc, asname, Ctx.Store);
                ADDOP(loc, ByteCodeOp.POP_TOP);
                return;
            }
            Nameop(loc, asname, Ctx.Store);
        }

        private void Import(GeneratedImport s)
        {
            var loc = LOC(s);
            for (int i = 0; i < Len(s.Names); i++)
            {
                var alias = (GeneratedAlias)s.Names[i];
                ADDOP_LOAD_CONST(loc, new PyInt(0));
                ADDOP_LOAD_CONST(loc, PyNone.Instance);
                AddopName(loc, ByteCodeOp.IMPORT_NAME, _u.Names, alias.Name.Value);

                if (alias.Asname != null)
                {
                    ImportAs(loc, alias.Name.Value, alias.Asname.Value);
                }
                else
                {
                    string tmp = alias.Name.Value;
                    int dot = tmp.IndexOf('.');
                    if (dot != -1)
                        tmp = tmp.Substring(0, dot);
                    Nameop(loc, tmp, Ctx.Store);
                }
            }
        }

        private void FromImport(GeneratedImportFrom s)
        {
            int n = Len(s.Names);
            var loc = LOC(s);
            ADDOP_LOAD_CONST(loc, new PyInt(s.Level ?? 0));

            var names = new PyObject[n];
            for (int i = 0; i < n; i++)
                names[i] = PyString.From(((GeneratedAlias)s.Names[i]).Name.Value);

            bool afterFuture = loc.Line > _future.LastLine ||
                (loc.Line == _future.LastLine && loc.Col > _future.LastColumn);
            if (afterFuture && s.Module?.Value == "__future__")
                throw Error(loc, "from __future__ imports must occur at the beginning of the file");
            ADDOP_LOAD_CONST(loc, new PyTuple(names));

            AddopName(loc, ByteCodeOp.IMPORT_NAME, _u.Names, s.Module?.Value ?? "");
            for (int i = 0; i < n; i++)
            {
                var alias = (GeneratedAlias)s.Names[i];
                if (i == 0 && alias.Name.Value[0] == '*')
                {
                    ADDOP_I(loc, ByteCodeOp.CALL_INTRINSIC_1, (int)IntrinsicFunction.INTRINSIC_IMPORT_STAR);
                    ADDOP(Loc.NoLocation, ByteCodeOp.POP_TOP);
                    return;
                }
                AddopName(loc, ByteCodeOp.IMPORT_FROM, _u.Names, alias.Name.Value);
                Nameop(loc, alias.Asname?.Value ?? alias.Name.Value, Ctx.Store);
            }
            ADDOP(loc, ByteCodeOp.POP_TOP);
        }

        private void Assert(GeneratedAssert s)
        {
            bool nonEmptyTuple = s.Test is GeneratedTuple tuple && Len(tuple.Elts) > 0;
            if (s.Test is GeneratedConstant constant && ConstantValue(constant.Value) is PyTuple { Items.Length: > 0 })
                nonEmptyTuple = true;
            if (nonEmptyTuple)
                Warn(LOC(s), "assertion is always true, perhaps remove parentheses?");
            if (_optimize != 0)
                return;
            var end = NewLabel();
            JumpIf(LOC(s), s.Test, end, true);
            ADDOP(LOC(s), ByteCodeOp.LOAD_ASSERTION_ERROR);
            if (s.Msg != null)
            {
                VisitExpr(s.Msg);
                ADDOP_I(LOC(s), ByteCodeOp.CALL, 0);
            }
            ADDOP_I(LOC(s.Test), ByteCodeOp.RAISE_VARARGS, 1);
            UseLabel(end);
        }

        private void StmtExpr(Loc loc, GeneratedExpr value)
        {
            if (_interactive && _nestLevel <= 1)
            {
                VisitExpr(value);
                ADDOP_I(loc, ByteCodeOp.CALL_INTRINSIC_1, (int)IntrinsicFunction.INTRINSIC_PRINT);
                ADDOP(Loc.NoLocation, ByteCodeOp.POP_TOP);
                return;
            }
            if (value is GeneratedConstant)
            {
                ADDOP(loc, ByteCodeOp.NOP);
                return;
            }
            VisitExpr(value);
            ADDOP(Loc.NoLocation, ByteCodeOp.POP_TOP);
        }

        // compiler_visit_stmt
        private void VisitStmt(GeneratedStmt s)
        {
            switch (s)
            {
                case GeneratedFunctionDef f:
                    Function(s, false, f.Args, f.Returns, f.DecoratorList, f.Name.Value, f.TypeParams, f.Body);
                    break;
                case GeneratedClassDef c:
                    Class(c);
                    break;
                case GeneratedTypeAlias t:
                    Typealias(t);
                    break;
                case GeneratedReturn r:
                    Return(r);
                    break;
                case GeneratedDelete d:
                    VisitExprs(d.Targets);
                    break;
                case GeneratedAssign a:
                {
                    int n = Len(a.Targets);
                    VisitExpr(a.Value);
                    for (int i = 0; i < n; i++)
                    {
                        if (i < n - 1)
                            ADDOP_I(LOC(s), ByteCodeOp.COPY, 1);
                        VisitExpr((GeneratedExpr)a.Targets[i]);
                    }
                    break;
                }
                case GeneratedAugAssign a:
                    Augassign(a);
                    break;
                case GeneratedAnnAssign a:
                    Annassign(a);
                    break;
                case GeneratedFor f:
                    For(f);
                    break;
                case GeneratedWhile w:
                    While(w);
                    break;
                case GeneratedIf i:
                    If(i);
                    break;
                case GeneratedMatch m:
                    Match(m);
                    break;
                case GeneratedRaise r:
                {
                    int n = 0;
                    if (r.Exc != null)
                    {
                        VisitExpr(r.Exc);
                        n++;
                        if (r.Cause != null)
                        {
                            VisitExpr(r.Cause);
                            n++;
                        }
                    }
                    ADDOP_I(LOC(s), ByteCodeOp.RAISE_VARARGS, n);
                    break;
                }
                case GeneratedTry t:
                    Try(t);
                    break;
                case GeneratedTryStar t:
                    TryStar(t);
                    break;
                case GeneratedAssert a:
                    Assert(a);
                    break;
                case GeneratedImport i:
                    Import(i);
                    break;
                case GeneratedImportFrom i:
                    FromImport(i);
                    break;
                case GeneratedGlobal:
                case GeneratedNonlocal:
                    break;
                case GeneratedExprStmt e:
                    StmtExpr(LOC(s), e.Value);
                    break;
                case GeneratedPass:
                    ADDOP(LOC(s), ByteCodeOp.NOP);
                    break;
                case GeneratedBreak:
                    Break(LOC(s));
                    break;
                case GeneratedContinue:
                    Continue(LOC(s));
                    break;
                case GeneratedWith w:
                    With(w, 0);
                    break;
                case GeneratedAsyncFunctionDef f:
                    Function(s, true, f.Args, f.Returns, f.DecoratorList, f.Name.Value, f.TypeParams, f.Body);
                    break;
                case GeneratedAsyncWith w:
                    AsyncWith(w, 0);
                    break;
                case GeneratedAsyncFor f:
                    AsyncFor(f);
                    break;
            }
        }

        // addop_binary
        private void ADDOP_BINARY(Loc loc, GeneratedOperator binop, bool inplace)
        {
            BinaryOpType oparg = binop switch
            {
                GeneratedAdd => inplace ? BinaryOpType.INPLACE_ADD : BinaryOpType.ADD,
                GeneratedSub => inplace ? BinaryOpType.INPLACE_SUBTRACT : BinaryOpType.SUBTRACT,
                GeneratedMult => inplace ? BinaryOpType.INPLACE_MULTIPLY : BinaryOpType.MULTIPLY,
                GeneratedMatMult => inplace ? BinaryOpType.INPLACE_MATRIX_MULTIPLY : BinaryOpType.MATRIX_MULTIPLY,
                GeneratedDiv => inplace ? BinaryOpType.INPLACE_TRUE_DIVIDE : BinaryOpType.TRUE_DIVIDE,
                GeneratedMod_ => inplace ? BinaryOpType.INPLACE_MODULO : BinaryOpType.MODULO,
                GeneratedPow => inplace ? BinaryOpType.INPLACE_POWER : BinaryOpType.POWER,
                GeneratedLShift => inplace ? BinaryOpType.INPLACE_LSHIFT : BinaryOpType.LSHIFT,
                GeneratedRShift => inplace ? BinaryOpType.INPLACE_RSHIFT : BinaryOpType.RSHIFT,
                GeneratedBitOr => inplace ? BinaryOpType.INPLACE_OR : BinaryOpType.OR,
                GeneratedBitXor => inplace ? BinaryOpType.INPLACE_XOR : BinaryOpType.XOR,
                GeneratedBitAnd => inplace ? BinaryOpType.INPLACE_AND : BinaryOpType.AND,
                GeneratedFloorDiv => inplace ? BinaryOpType.INPLACE_FLOOR_DIVIDE : BinaryOpType.FLOOR_DIVIDE,
                _ => throw new InvalidOperationException("binary op should not be possible"),
            };
            ADDOP_I(loc, ByteCodeOp.BINARY_OP, (int)oparg);
        }

        private void ADDOP_YIELD(Loc loc)
        {
            if (_u.Ste.Generator && _u.Ste.Coroutine)
                ADDOP_I(loc, ByteCodeOp.CALL_INTRINSIC_1, (int)IntrinsicFunction.INTRINSIC_ASYNC_GEN_WRAP);
            ADDOP_I(loc, ByteCodeOp.YIELD_VALUE, 0);
            ADDOP_I(loc, ByteCodeOp.RESUME, 1);
        }

        // compiler_nameop
        private void Nameop(Loc loc, string name, Ctx ctx)
        {
            ForbiddenName(loc, name, ctx);
            string mangled = MaybeMangle(_u.Private, _u.Ste, name);

            var dict = _u.Names;
            int optype = 3; // OP_FAST 0, OP_GLOBAL 1, OP_DEREF 2, OP_NAME 3
            ByteCodeOp op = ByteCodeOp.NOP;
            int scope = _u.Ste.GetScope(mangled);
            switch (scope)
            {
                case FREE:
                    dict = _u.Freevars;
                    optype = 2;
                    break;
                case CELL:
                    dict = _u.Cellvars;
                    optype = 2;
                    break;
                case LOCAL:
                    if (_u.Ste.IsFunctionLike || (_u.Fasthidden.TryGetValue(mangled, out bool hidden) && hidden))
                        optype = 0;
                    break;
                case GLOBAL_IMPLICIT:
                    if (_u.Ste.IsFunctionLike)
                        optype = 1;
                    break;
                case GLOBAL_EXPLICIT:
                    optype = 1;
                    break;
            }

            switch (optype)
            {
                case 2:
                    switch (ctx)
                    {
                        case Ctx.Load:
                            if (_u.Ste.Type == BlockType.ClassBlock && _u.InInlinedComp == 0)
                            {
                                op = ByteCodeOp.LOAD_FROM_DICT_OR_DEREF;
                                ADDOP(loc, ByteCodeOp.LOAD_LOCALS);
                            }
                            else if (_u.Ste.CanSeeClassScope)
                            {
                                op = ByteCodeOp.LOAD_FROM_DICT_OR_DEREF;
                                ADDOP_I(loc, ByteCodeOp.LOAD_DEREF, DictAdd(_u.Freevars, "__classdict__"));
                            }
                            else
                            {
                                op = ByteCodeOp.LOAD_DEREF;
                            }
                            break;
                        case Ctx.Store:
                            op = ByteCodeOp.STORE_DEREF;
                            break;
                        case Ctx.Del:
                            op = ByteCodeOp.DELETE_DEREF;
                            break;
                    }
                    break;
                case 0:
                    op = ctx switch
                    {
                        Ctx.Load => ByteCodeOp.LOAD_FAST,
                        Ctx.Store => ByteCodeOp.STORE_FAST,
                        _ => ByteCodeOp.DELETE_FAST,
                    };
                    ADDOP_I(loc, op, DictAdd(_u.Varnames, mangled));
                    return;
                case 1:
                    switch (ctx)
                    {
                        case Ctx.Load:
                            if (_u.Ste.CanSeeClassScope && scope == GLOBAL_IMPLICIT)
                            {
                                op = ByteCodeOp.LOAD_FROM_DICT_OR_GLOBALS;
                                ADDOP_I(loc, ByteCodeOp.LOAD_DEREF, DictAdd(_u.Freevars, "__classdict__"));
                            }
                            else
                            {
                                op = ByteCodeOp.LOAD_GLOBAL;
                            }
                            break;
                        case Ctx.Store:
                            op = ByteCodeOp.STORE_GLOBAL;
                            break;
                        case Ctx.Del:
                            op = ByteCodeOp.DELETE_GLOBAL;
                            break;
                    }
                    break;
                case 3:
                    op = ctx switch
                    {
                        Ctx.Load => _u.Ste.Type == BlockType.ClassBlock && _u.InInlinedComp != 0
                            ? ByteCodeOp.LOAD_GLOBAL
                            : ByteCodeOp.LOAD_NAME,
                        Ctx.Store => ByteCodeOp.STORE_NAME,
                        _ => ByteCodeOp.DELETE_NAME,
                    };
                    break;
            }

            int arg = DictAdd(dict, mangled);
            if (op == ByteCodeOp.LOAD_GLOBAL)
                arg <<= 1;
            ADDOP_I(loc, op, arg);
        }

        private void Boolop(GeneratedBoolOp e)
        {
            var loc = LOC(e);
            var jumpi = e.Op is GeneratedAnd ? ByteCodeOp.POP_JUMP_IF_FALSE : ByteCodeOp.POP_JUMP_IF_TRUE;
            var end = NewLabel();
            var s = e.Values;
            int n = s.Count - 1;
            for (int i = 0; i < n; i++)
            {
                VisitExpr((GeneratedExpr)s[i]);
                ADDOP_I(loc, ByteCodeOp.COPY, 1);
                ADDOP_JUMP(loc, jumpi, end);
                ADDOP(loc, ByteCodeOp.POP_TOP);
            }
            VisitExpr((GeneratedExpr)s[n]);
            UseLabel(end);
        }

        private static bool AreAllItemsConst(GeneratedSeq seq, int begin, int end)
        {
            for (int i = begin; i < end; i++)
            {
                if (seq[i] is not GeneratedConstant)
                    return false;
            }
            return true;
        }

        // starunpack_helper
        private void StarunpackHelper(Loc loc, GeneratedSeq elts, int pushed, ByteCodeOp build, ByteCodeOp add,
            ByteCodeOp extend, bool tuple)
        {
            int n = Len(elts);
            if (n > 2 && AreAllItemsConst(elts, 0, n))
            {
                var items = new PyObject[n];
                for (int i = 0; i < n; i++)
                    items[i] = ConstantValue(((GeneratedConstant)elts[i]).Value);
                PyObject folded = new PyTuple(items);
                if (tuple && pushed == 0)
                {
                    ADDOP_LOAD_CONST(loc, folded);
                }
                else
                {
                    if (add == ByteCodeOp.SET_ADD)
                        folded = new PyFrozenSet(items);
                    ADDOP_I(loc, build, pushed);
                    ADDOP_LOAD_CONST(loc, folded);
                    ADDOP_I(loc, extend, 1);
                    if (tuple)
                        ADDOP_I(loc, ByteCodeOp.CALL_INTRINSIC_1, (int)IntrinsicFunction.INTRINSIC_LIST_TO_TUPLE);
                }
                return;
            }

            bool big = n + pushed > STACK_USE_GUIDELINE;
            bool seenStar = false;
            for (int i = 0; i < n; i++)
            {
                if (elts[i] is GeneratedStarred)
                {
                    seenStar = true;
                    break;
                }
            }
            if (!seenStar && !big)
            {
                for (int i = 0; i < n; i++)
                    VisitExpr((GeneratedExpr)elts[i]);
                ADDOP_I(loc, tuple ? ByteCodeOp.BUILD_TUPLE : build, n + pushed);
                return;
            }
            bool sequenceBuilt = false;
            if (big)
            {
                ADDOP_I(loc, build, pushed);
                sequenceBuilt = true;
            }
            for (int i = 0; i < n; i++)
            {
                if (elts[i] is GeneratedStarred starred)
                {
                    if (!sequenceBuilt)
                    {
                        ADDOP_I(loc, build, i + pushed);
                        sequenceBuilt = true;
                    }
                    VisitExpr(starred.Value);
                    ADDOP_I(loc, extend, 1);
                }
                else
                {
                    VisitExpr((GeneratedExpr)elts[i]);
                    if (sequenceBuilt)
                        ADDOP_I(loc, add, 1);
                }
            }
            if (tuple)
                ADDOP_I(loc, ByteCodeOp.CALL_INTRINSIC_1, (int)IntrinsicFunction.INTRINSIC_LIST_TO_TUPLE);
        }

        // unpack_helper
        private void UnpackHelper(Loc loc, GeneratedSeq elts)
        {
            int n = Len(elts);
            bool seenStar = false;
            for (int i = 0; i < n; i++)
            {
                if (elts[i] is GeneratedStarred)
                {
                    if (seenStar)
                        throw Error(loc, "multiple starred expressions in assignment");
                    if (i >= (1 << 8) || n - i - 1 >= (int.MaxValue >> 8))
                        throw Error(loc, "too many expressions in star-unpacking assignment");
                    ADDOP_I(loc, ByteCodeOp.UNPACK_EX, i + ((n - i - 1) << 8));
                    seenStar = true;
                }
            }
            if (!seenStar)
                ADDOP_I(loc, ByteCodeOp.UNPACK_SEQUENCE, n);
        }

        private void AssignmentHelper(Loc loc, GeneratedSeq elts)
        {
            UnpackHelper(loc, elts);
            for (int i = 0; i < Len(elts); i++)
                VisitExpr(elts[i] is GeneratedStarred starred ? starred.Value : (GeneratedExpr)elts[i]);
        }

        private void List(GeneratedList e)
        {
            var loc = LOC(e);
            switch (CtxOf(e.Ctx))
            {
                case Ctx.Store:
                    AssignmentHelper(loc, e.Elts);
                    break;
                case Ctx.Load:
                    StarunpackHelper(loc, e.Elts, 0, ByteCodeOp.BUILD_LIST, ByteCodeOp.LIST_APPEND, ByteCodeOp.LIST_EXTEND, false);
                    break;
                default:
                    VisitExprs(e.Elts);
                    break;
            }
        }

        private void Tuple(GeneratedTuple e)
        {
            var loc = LOC(e);
            switch (CtxOf(e.Ctx))
            {
                case Ctx.Store:
                    AssignmentHelper(loc, e.Elts);
                    break;
                case Ctx.Load:
                    StarunpackHelper(loc, e.Elts, 0, ByteCodeOp.BUILD_LIST, ByteCodeOp.LIST_APPEND, ByteCodeOp.LIST_EXTEND, true);
                    break;
                default:
                    VisitExprs(e.Elts);
                    break;
            }
        }

        private void Subdict(GeneratedDict e, int begin, int end)
        {
            int n = end - begin;
            bool big = n * 2 > STACK_USE_GUIDELINE;
            var loc = LOC(e);
            if (n > 1 && !big && AreAllItemsConst(e.Keys, begin, end))
            {
                for (int i = begin; i < end; i++)
                    VisitExpr((GeneratedExpr)e.Values[i]);
                var keys = new PyObject[n];
                for (int i = begin; i < end; i++)
                    keys[i - begin] = ConstantValue(((GeneratedConstant)e.Keys[i]).Value);
                ADDOP_LOAD_CONST(loc, new PyTuple(keys));
                ADDOP_I(loc, ByteCodeOp.BUILD_CONST_KEY_MAP, n);
                return;
            }
            if (big)
                ADDOP_I(loc, ByteCodeOp.BUILD_MAP, 0);
            for (int i = begin; i < end; i++)
            {
                VisitExpr((GeneratedExpr)e.Keys[i]);
                VisitExpr((GeneratedExpr)e.Values[i]);
                if (big)
                    ADDOP_I(loc, ByteCodeOp.MAP_ADD, 1);
            }
            if (!big)
                ADDOP_I(loc, ByteCodeOp.BUILD_MAP, n);
        }

        private void Dict(GeneratedDict e)
        {
            var loc = LOC(e);
            int n = Len(e.Values);
            bool haveDict = false;
            int elements = 0;
            for (int i = 0; i < n; i++)
            {
                if (e.Keys[i] == null)
                {
                    if (elements != 0)
                    {
                        Subdict(e, i - elements, i);
                        if (haveDict)
                            ADDOP_I(loc, ByteCodeOp.DICT_UPDATE, 1);
                        haveDict = true;
                        elements = 0;
                    }
                    if (!haveDict)
                    {
                        ADDOP_I(loc, ByteCodeOp.BUILD_MAP, 0);
                        haveDict = true;
                    }
                    VisitExpr((GeneratedExpr)e.Values[i]);
                    ADDOP_I(loc, ByteCodeOp.DICT_UPDATE, 1);
                }
                else if (elements * 2 > STACK_USE_GUIDELINE)
                {
                    Subdict(e, i - elements, i + 1);
                    if (haveDict)
                        ADDOP_I(loc, ByteCodeOp.DICT_UPDATE, 1);
                    haveDict = true;
                    elements = 0;
                }
                else
                {
                    elements++;
                }
            }
            if (elements != 0)
            {
                Subdict(e, n - elements, n);
                if (haveDict)
                    ADDOP_I(loc, ByteCodeOp.DICT_UPDATE, 1);
                haveDict = true;
            }
            if (!haveDict)
                ADDOP_I(loc, ByteCodeOp.BUILD_MAP, 0);
        }
    }
}
