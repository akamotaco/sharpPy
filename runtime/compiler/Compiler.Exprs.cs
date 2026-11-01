// Python/compile.c of CPython v3.12.15: expressions, calls, comprehensions, with.
using System;
using System.Collections.Generic;
using SharpPy.Generated;
using static SharpPy.PySymtable;

namespace SharpPy
{
    public sealed partial class Compiler
    {
        private void Compare(GeneratedCompare e)
        {
            var loc = LOC(e);
            CheckCompare(e);
            VisitExpr(e.Left);
            int n = e.Ops.Count - 1;
            if (n == 0)
            {
                VisitExpr((GeneratedExpr)e.Comparators[0]);
                ADDOP_COMPARE(loc, e.Ops[0]);
                return;
            }
            var cleanup = NewLabel();
            for (int i = 0; i < n; i++)
            {
                VisitExpr((GeneratedExpr)e.Comparators[i]);
                ADDOP_I(loc, ByteCodeOp.SWAP, 2);
                ADDOP_I(loc, ByteCodeOp.COPY, 2);
                ADDOP_COMPARE(loc, e.Ops[i]);
                ADDOP_I(loc, ByteCodeOp.COPY, 1);
                ADDOP_JUMP(loc, ByteCodeOp.POP_JUMP_IF_FALSE, cleanup);
                ADDOP(loc, ByteCodeOp.POP_TOP);
            }
            VisitExpr((GeneratedExpr)e.Comparators[n]);
            ADDOP_COMPARE(loc, e.Ops[n]);
            var end = NewLabel();
            ADDOP_JUMP(Loc.NoLocation, ByteCodeOp.JUMP, end);

            UseLabel(cleanup);
            ADDOP_I(loc, ByteCodeOp.SWAP, 2);
            ADDOP(loc, ByteCodeOp.POP_TOP);

            UseLabel(end);
        }

        // infer_type: the tp_name, or null.
        private string? InferType(GeneratedExpr e) => e switch
        {
            GeneratedTuple => "tuple",
            GeneratedList or GeneratedListComp => "list",
            GeneratedDict or GeneratedDictComp => "dict",
            GeneratedSet or GeneratedSetComp => "set",
            GeneratedGeneratorExp => "generator",
            GeneratedLambda => "function",
            GeneratedJoinedStr or GeneratedFormattedValue => "str",
            GeneratedConstant c => ConstantValue(c.Value).GetTypeName(),
            _ => null,
        };

        private void CheckCaller(GeneratedExpr e)
        {
            if (e is GeneratedConstant or GeneratedTuple or GeneratedList or GeneratedListComp or GeneratedDict
                or GeneratedDictComp or GeneratedSet or GeneratedSetComp or GeneratedGeneratorExp
                or GeneratedJoinedStr or GeneratedFormattedValue)
            {
                Warn(LOC(e), $"'{InferType(e)}' object is not callable; perhaps you missed a comma?");
            }
        }

        private void CheckSubscripter(GeneratedExpr e)
        {
            if (e is GeneratedConstant c)
            {
                var v = ConstantValue(c.Value);
                if (v is not (PyNone or PyEllipsis or PyInt or PyBool or PyFloat or PyComplex or PySet or PyFrozenSet))
                    return;
            }
            else if (e is not (GeneratedSet or GeneratedSetComp or GeneratedGeneratorExp or GeneratedLambda))
            {
                return;
            }
            Warn(LOC(e), $"'{InferType(e)}' object is not subscriptable; perhaps you missed a comma?");
        }

        private void CheckIndex(GeneratedExpr e, GeneratedExpr s)
        {
            string? indexType = InferType(s);
            if (indexType == null || indexType is "int" or "bool" or "slice")
                return;
            if (e is GeneratedConstant c)
            {
                if (ConstantValue(c.Value) is not (PyString or PyBytes or PyTuple))
                    return;
            }
            else if (e is not (GeneratedTuple or GeneratedList or GeneratedListComp or GeneratedJoinedStr or GeneratedFormattedValue))
            {
                return;
            }
            Warn(LOC(e), $"{InferType(e)} indices must be integers or slices, not {indexType}; perhaps you missed a comma?");
        }

        private bool IsImportOriginated(GeneratedExpr e) =>
            e is GeneratedName name && (_st.Top.GetSymbol(name.Id.Value) & DEF_IMPORT) != 0;

        // can_optimize_super_call
        private bool CanOptimizeSuperCall(GeneratedAttribute attr)
        {
            if (attr.Value is not GeneratedCall { Func: GeneratedName { Id.Value: "super" } } e ||
                attr.Attr.Value == "__class__" || Len(e.Keywords) != 0)
                return false;
            int numArgs = Len(e.Args);
            if (_u.Ste.GetScope("super") != GLOBAL_IMPLICIT)
                return false;
            if (_st.Top.GetScope("super") != 0)
                return false;
            if (numArgs == 2)
            {
                for (int i = 0; i < numArgs; i++)
                {
                    if (e.Args[i] is GeneratedStarred)
                        return false;
                }
                return true;
            }
            if (numArgs != 0)
                return false;
            if (_u.ArgCount == 0 && _u.PosonlyArgCount == 0)
                return false;
            return GetRefType("__class__") == FREE;
        }

        private void LoadArgsForSuper(GeneratedCall e)
        {
            var loc = LOC(e);
            Nameop(LOC(e.Func), ((GeneratedName)e.Func).Id.Value, Ctx.Load);
            if (Len(e.Args) == 2)
            {
                VisitExpr((GeneratedExpr)e.Args[0]);
                VisitExpr((GeneratedExpr)e.Args[1]);
                return;
            }
            Nameop(loc, "__class__", Ctx.Load);
            foreach (var pair in _u.Varnames)
            {
                if (pair.Value == 0)
                {
                    Nameop(loc, pair.Key, Ctx.Load);
                    return;
                }
            }
            throw new InvalidOperationException("no first argument for super()");
        }

        // update_start_location_to_match_attr
        private static Loc UpdateStartLocationToMatchAttr(Loc loc, GeneratedAttribute attr)
        {
            if (loc.Line != attr.EndLineNo)
            {
                int line = attr.EndLineNo;
                int len = attr.Attr.Value.Length;
                int col = loc.Col;
                int endCol = loc.EndCol;
                if (len <= attr.EndColOffset)
                {
                    col = attr.EndColOffset - len;
                }
                else
                {
                    col = -1;
                    endCol = -1;
                }
                int endLine = Math.Max(line, loc.EndLine);
                if (line == endLine)
                    endCol = Math.Max(col, endCol);
                return new Loc(line, endLine, col, endCol);
            }
            return loc;
        }

        // maybe_optimize_method_call
        private bool MaybeOptimizeMethodCall(GeneratedCall e)
        {
            if (e.Func is not GeneratedAttribute meth || meth.Ctx is not GeneratedLoad)
                return false;
            if (IsImportOriginated(meth.Value))
                return false;

            var args = e.Args;
            var kwds = e.Keywords;
            int argsl = Len(args);
            int kwdsl = Len(kwds);
            if (argsl + kwdsl + (kwdsl != 0 ? 1 : 0) >= STACK_USE_GUIDELINE)
                return false;
            for (int i = 0; i < argsl; i++)
            {
                if (args[i] is GeneratedStarred)
                    return false;
            }
            for (int i = 0; i < kwdsl; i++)
            {
                if (((GeneratedKeyword)kwds[i]).Arg == null)
                    return false;
            }

            var loc = LOC(meth);

            if (CanOptimizeSuperCall(meth))
            {
                var call = (GeneratedCall)meth.Value;
                LoadArgsForSuper(call);
                AddopName(loc, ByteCodeOp.LOAD_SUPER_ATTR, _u.Names, meth.Attr.Value,
                    Len(call.Args) != 0 ? NameOp.LoadSuperMethod : NameOp.LoadZeroSuperMethod);
                loc = UpdateStartLocationToMatchAttr(loc, meth);
                ADDOP(loc, ByteCodeOp.NOP);
            }
            else
            {
                VisitExpr(meth.Value);
                loc = UpdateStartLocationToMatchAttr(loc, meth);
                AddopName(loc, ByteCodeOp.LOAD_ATTR, _u.Names, meth.Attr.Value, NameOp.LoadMethod);
            }

            VisitExprs(args);

            if (kwdsl != 0)
            {
                for (int i = 0; i < kwdsl; i++)
                    VisitExpr(((GeneratedKeyword)kwds[i]).Value);
                CallSimpleKwHelper(loc, kwds, kwdsl);
            }
            loc = UpdateStartLocationToMatchAttr(LOC(e), meth);
            ADDOP_I(loc, ByteCodeOp.CALL, argsl + kwdsl);
            return true;
        }

        private void ValidateKeywords(GeneratedSeq? keywords)
        {
            int n = Len(keywords);
            for (int i = 0; i < n; i++)
            {
                var key = (GeneratedKeyword)keywords![i];
                if (key.Arg == null)
                    continue;
                ForbiddenName(LOC(key), key.Arg.Value, Ctx.Store);
                for (int j = i + 1; j < n; j++)
                {
                    var other = (GeneratedKeyword)keywords[j];
                    if (other.Arg != null && other.Arg.Value == key.Arg.Value)
                        throw Error(LOC(other), $"keyword argument repeated: {key.Arg.Value}");
                }
            }
        }

        private void Call(GeneratedCall e)
        {
            ValidateKeywords(e.Keywords);
            if (MaybeOptimizeMethodCall(e))
                return;
            CheckCaller(e.Func);
            ADDOP(LOC(e.Func), ByteCodeOp.PUSH_NULL);
            VisitExpr(e.Func);
            CallHelper(LOC(e), 0, e.Args, e.Keywords);
        }

        private void JoinedStr(GeneratedJoinedStr e)
        {
            var loc = LOC(e);
            int count = Len(e.Values);
            if (count > STACK_USE_GUIDELINE)
            {
                ADDOP_LOAD_CONST(loc, new PyString(""));
                AddopName(loc, ByteCodeOp.LOAD_ATTR, _u.Names, "join", NameOp.LoadMethod);
                ADDOP_I(loc, ByteCodeOp.BUILD_LIST, 0);
                for (int i = 0; i < count; i++)
                {
                    VisitExpr((GeneratedExpr)e.Values[i]);
                    ADDOP_I(loc, ByteCodeOp.LIST_APPEND, 1);
                }
                ADDOP_I(loc, ByteCodeOp.CALL, 1);
            }
            else
            {
                VisitExprs(e.Values);
                if (count != 1)
                    ADDOP_I(loc, ByteCodeOp.BUILD_STRING, count);
            }
        }

        private void FormattedValue(GeneratedFormattedValue e)
        {
            VisitExpr(e.Value);
            int oparg = e.Conversion switch
            {
                's' => 1,
                'r' => 2,
                'a' => 3,
                -1 => 0,
                _ => throw new InvalidOperationException($"Unrecognized conversion character {e.Conversion}"),
            };
            if (e.FormatSpec != null)
            {
                VisitExpr(e.FormatSpec);
                oparg |= 4;
            }
            ADDOP_I(LOC(e), ByteCodeOp.FORMAT_VALUE, oparg);
        }

        private void Subkwargs(Loc loc, GeneratedSeq keywords, int begin, int end)
        {
            int n = end - begin;
            bool big = n * 2 > STACK_USE_GUIDELINE;
            if (n > 1 && !big)
            {
                for (int i = begin; i < end; i++)
                    VisitExpr(((GeneratedKeyword)keywords[i]).Value);
                var keys = new PyObject[n];
                for (int i = begin; i < end; i++)
                    keys[i - begin] = new PyString(((GeneratedKeyword)keywords[i]).Arg!.Value);
                ADDOP_LOAD_CONST(loc, new PyTuple(keys));
                ADDOP_I(loc, ByteCodeOp.BUILD_CONST_KEY_MAP, n);
                return;
            }
            if (big)
                ADDOP_I(Loc.NoLocation, ByteCodeOp.BUILD_MAP, 0);
            for (int i = begin; i < end; i++)
            {
                var kw = (GeneratedKeyword)keywords[i];
                ADDOP_LOAD_CONST(loc, new PyString(kw.Arg!.Value));
                VisitExpr(kw.Value);
                if (big)
                    ADDOP_I(Loc.NoLocation, ByteCodeOp.MAP_ADD, 1);
            }
            if (!big)
                ADDOP_I(loc, ByteCodeOp.BUILD_MAP, n);
        }

        private void CallSimpleKwHelper(Loc loc, GeneratedSeq keywords, int nkwelts)
        {
            var names = new PyObject[nkwelts];
            for (int i = 0; i < nkwelts; i++)
                names[i] = new PyString(((GeneratedKeyword)keywords[i]).Arg!.Value);
            ADDOP_I(loc, ByteCodeOp.KW_NAMES, AddConst(new PyTuple(names)));
        }

        // compiler_call_helper
        private void CallHelper(Loc loc, int n, GeneratedSeq? args, GeneratedSeq? keywords)
        {
            ValidateKeywords(keywords);
            int nelts = Len(args);
            int nkwelts = Len(keywords);
            bool exCall = nelts + nkwelts * 2 > STACK_USE_GUIDELINE;
            for (int i = 0; i < nelts && !exCall; i++)
                exCall = args![i] is GeneratedStarred;
            for (int i = 0; i < nkwelts && !exCall; i++)
                exCall = ((GeneratedKeyword)keywords![i]).Arg == null;

            if (!exCall)
            {
                for (int i = 0; i < nelts; i++)
                    VisitExpr((GeneratedExpr)args![i]);
                if (nkwelts != 0)
                {
                    for (int i = 0; i < nkwelts; i++)
                        VisitExpr(((GeneratedKeyword)keywords![i]).Value);
                    CallSimpleKwHelper(loc, keywords!, nkwelts);
                }
                ADDOP_I(loc, ByteCodeOp.CALL, n + nelts + nkwelts);
                return;
            }

            if (n == 0 && nelts == 1 && args![0] is GeneratedStarred only)
                VisitExpr(only.Value);
            else
                StarunpackHelper(loc, args ?? new GeneratedExprSeq(), n, ByteCodeOp.BUILD_LIST, ByteCodeOp.LIST_APPEND, ByteCodeOp.LIST_EXTEND, true);

            if (nkwelts != 0)
            {
                bool haveDict = false;
                int nseen = 0;
                for (int i = 0; i < nkwelts; i++)
                {
                    var kw = (GeneratedKeyword)keywords![i];
                    if (kw.Arg == null)
                    {
                        if (nseen != 0)
                        {
                            Subkwargs(loc, keywords, i - nseen, i);
                            if (haveDict)
                                ADDOP_I(loc, ByteCodeOp.DICT_MERGE, 1);
                            haveDict = true;
                            nseen = 0;
                        }
                        if (!haveDict)
                        {
                            ADDOP_I(loc, ByteCodeOp.BUILD_MAP, 0);
                            haveDict = true;
                        }
                        VisitExpr(kw.Value);
                        ADDOP_I(loc, ByteCodeOp.DICT_MERGE, 1);
                    }
                    else
                    {
                        nseen++;
                    }
                }
                if (nseen != 0)
                {
                    Subkwargs(loc, keywords!, nkwelts - nseen, nkwelts);
                    if (haveDict)
                        ADDOP_I(loc, ByteCodeOp.DICT_MERGE, 1);
                }
            }
            ADDOP_I(loc, ByteCodeOp.CALL_FUNCTION_EX, nkwelts > 0 ? 1 : 0);
        }

        private enum CompType { GenExp, ListComp, DictComp, SetComp }

        private void ComprehensionGenerator(Loc loc, GeneratedSeq generators, int genIndex, int depth,
            GeneratedExpr elt, GeneratedExpr? val, CompType type, bool iterOnStack)
        {
            if (((GeneratedComprehension)generators[genIndex]).IsAsync != 0)
                AsyncComprehensionGenerator(loc, generators, genIndex, depth, elt, val, type, iterOnStack);
            else
                SyncComprehensionGenerator(loc, generators, genIndex, depth, elt, val, type, iterOnStack);
        }

        private Loc ComprehensionElement(GeneratedExpr elt, GeneratedExpr? val, CompType type, int depth)
        {
            var eltLoc = LOC(elt);
            switch (type)
            {
                case CompType.GenExp:
                    VisitExpr(elt);
                    ADDOP_YIELD(eltLoc);
                    ADDOP(eltLoc, ByteCodeOp.POP_TOP);
                    break;
                case CompType.ListComp:
                    VisitExpr(elt);
                    ADDOP_I(eltLoc, ByteCodeOp.LIST_APPEND, depth + 1);
                    break;
                case CompType.SetComp:
                    VisitExpr(elt);
                    ADDOP_I(eltLoc, ByteCodeOp.SET_ADD, depth + 1);
                    break;
                case CompType.DictComp:
                    VisitExpr(elt);
                    VisitExpr(val!);
                    eltLoc = LOCATION(elt.LineNo, val!.EndLineNo, elt.ColOffset, val.EndColOffset);
                    ADDOP_I(eltLoc, ByteCodeOp.MAP_ADD, depth + 1);
                    break;
            }
            return eltLoc;
        }

        private void SyncComprehensionGenerator(Loc loc, GeneratedSeq generators, int genIndex, int depth,
            GeneratedExpr elt, GeneratedExpr? val, CompType type, bool iterOnStack)
        {
            Label? start = NewLabel();
            var ifCleanup = NewLabel();
            var anchor = NewLabel();

            var gen = (GeneratedComprehension)generators[genIndex];

            if (!iterOnStack)
            {
                if (genIndex == 0)
                {
                    _u.ArgCount = 1;
                    ADDOP_I(loc, ByteCodeOp.LOAD_FAST, 0);
                }
                else
                {
                    GeneratedSeq? elts = gen.Iter switch
                    {
                        GeneratedList list => list.Elts,
                        GeneratedTuple tuple => tuple.Elts,
                        _ => null,
                    };
                    if (Len(elts) == 1 && elts![0] is not GeneratedStarred)
                    {
                        VisitExpr((GeneratedExpr)elts[0]);
                        start = null;
                    }
                    if (start != null)
                    {
                        VisitExpr(gen.Iter);
                        ADDOP(LOC(gen.Iter), ByteCodeOp.GET_ITER);
                    }
                }
            }

            if (start != null)
            {
                depth++;
                UseLabel(start.Value);
                ADDOP_JUMP(LOC(gen.Iter), ByteCodeOp.FOR_ITER, anchor);
            }
            VisitExpr(gen.Target);

            for (int i = 0; i < Len(gen.Ifs); i++)
                JumpIf(loc, (GeneratedExpr)gen.Ifs[i], ifCleanup, false);

            if (++genIndex < generators.Count)
                ComprehensionGenerator(loc, generators, genIndex, depth, elt, val, type, false);

            var eltLoc = LOC(elt);
            if (genIndex >= generators.Count)
                eltLoc = ComprehensionElement(elt, val, type, depth);

            UseLabel(ifCleanup);
            if (start != null)
            {
                ADDOP_JUMP(eltLoc, ByteCodeOp.JUMP, start.Value);
                UseLabel(anchor);
                ADDOP(Loc.NoLocation, ByteCodeOp.END_FOR);
            }
        }

        private void AsyncComprehensionGenerator(Loc loc, GeneratedSeq generators, int genIndex, int depth,
            GeneratedExpr elt, GeneratedExpr? val, CompType type, bool iterOnStack)
        {
            var start = NewLabel();
            var except = NewLabel();
            var ifCleanup = NewLabel();

            var gen = (GeneratedComprehension)generators[genIndex];

            if (!iterOnStack)
            {
                if (genIndex == 0)
                {
                    _u.ArgCount = 1;
                    ADDOP_I(loc, ByteCodeOp.LOAD_FAST, 0);
                }
                else
                {
                    VisitExpr(gen.Iter);
                    ADDOP(LOC(gen.Iter), ByteCodeOp.GET_AITER);
                }
            }

            UseLabel(start);
            PushFBlock(loc, FBlockType.ASYNC_COMPREHENSION_GENERATOR, start, Label.NoLabel, null);

            ADDOP_JUMP(loc, ByteCodeOp.SETUP_FINALLY, except);
            ADDOP(loc, ByteCodeOp.GET_ANEXT);
            ADDOP_LOAD_CONST(loc, PyNone.Instance);
            ADD_YIELD_FROM(loc, true);
            ADDOP(loc, ByteCodeOp.POP_BLOCK);
            VisitExpr(gen.Target);

            for (int i = 0; i < Len(gen.Ifs); i++)
                JumpIf(loc, (GeneratedExpr)gen.Ifs[i], ifCleanup, false);

            depth++;
            if (++genIndex < generators.Count)
                ComprehensionGenerator(loc, generators, genIndex, depth, elt, val, type, false);

            var eltLoc = LOC(elt);
            if (genIndex >= generators.Count)
                eltLoc = ComprehensionElement(elt, val, type, depth);

            UseLabel(ifCleanup);
            ADDOP_JUMP(eltLoc, ByteCodeOp.JUMP, start);

            PopFBlock(FBlockType.ASYNC_COMPREHENSION_GENERATOR, start);

            UseLabel(except);
            ADDOP(loc, ByteCodeOp.END_ASYNC_FOR);
        }

        private sealed class InlinedComprehensionState
        {
            public List<string>? PushedLocals;
            public Dictionary<string, long>? TempSymbols;
            public HashSet<string>? FastHidden;
            public Label Cleanup = Label.NoLabel;
            public Label End = Label.NoLabel;
        }

        // push_inlined_comprehension_state
        private void PushInlinedComprehensionState(Loc loc, PySTEntry entry, InlinedComprehensionState state)
        {
            bool inClassBlock = _u.Ste.Type == BlockType.ClassBlock && _u.InInlinedComp == 0;
            _u.InInlinedComp++;
            foreach (var (k, symbol) in entry.Symbols)
            {
                int scope = (int)((symbol >> SCOPE_OFFSET) & SCOPE_MASK);
                bool hasOuter = _u.Ste.Symbols.TryGetValue(k, out long outv);
                int outsc = (int)((outv >> SCOPE_OFFSET) & SCOPE_MASK);
                if ((scope != outsc && scope != FREE && !(scope == CELL && outsc == FREE)) || inClassBlock)
                {
                    state.TempSymbols ??= new Dictionary<string, long>();
                    _u.Ste.Symbols[k] = symbol;
                    state.TempSymbols[k] = outv;
                }
                if (((symbol & DEF_LOCAL) != 0 && (symbol & DEF_NONLOCAL) == 0) || inClassBlock)
                {
                    if (!_u.Ste.IsFunctionLike)
                    {
                        if (!(_u.Fasthidden.TryGetValue(k, out bool orig) && orig))
                        {
                            _u.Fasthidden[k] = true;
                            state.FastHidden ??= new HashSet<string>();
                            state.FastHidden.Add(k);
                        }
                    }
                    state.PushedLocals ??= new List<string>();
                    AddopName(loc, ByteCodeOp.LOAD_FAST_AND_CLEAR, _u.Varnames, k);
                    if (scope == CELL)
                    {
                        if (outsc == FREE)
                            AddopName(loc, ByteCodeOp.MAKE_CELL, _u.Freevars, k);
                        else
                            AddopName(loc, ByteCodeOp.MAKE_CELL, _u.Cellvars, k);
                    }
                    state.PushedLocals.Add(k);
                }
            }
            if (state.PushedLocals != null)
            {
                ADDOP_I(loc, ByteCodeOp.SWAP, state.PushedLocals.Count + 1);
                state.Cleanup = NewLabel();
                state.End = NewLabel();
                ADDOP_JUMP(loc, ByteCodeOp.SETUP_FINALLY, state.Cleanup);
            }
        }

        private void RestoreInlinedComprehensionLocals(Loc loc, InlinedComprehensionState state)
        {
            int npops = state.PushedLocals!.Count;
            ADDOP_I(loc, ByteCodeOp.SWAP, npops + 1);
            for (int i = npops - 1; i >= 0; i--)
                AddopName(loc, ByteCodeOp.STORE_FAST_MAYBE_NULL, _u.Varnames, state.PushedLocals[i]);
        }

        private void PopInlinedComprehensionState(Loc loc, InlinedComprehensionState state)
        {
            _u.InInlinedComp--;
            if (state.TempSymbols != null)
            {
                foreach (var (k, v) in state.TempSymbols)
                    _u.Ste.Symbols[k] = v;
                state.TempSymbols = null;
            }
            if (state.PushedLocals != null)
            {
                ADDOP(Loc.NoLocation, ByteCodeOp.POP_BLOCK);
                ADDOP_JUMP(Loc.NoLocation, ByteCodeOp.JUMP, state.End);

                UseLabel(state.Cleanup);
                ADDOP_I(Loc.NoLocation, ByteCodeOp.SWAP, 2);
                ADDOP(Loc.NoLocation, ByteCodeOp.POP_TOP);
                RestoreInlinedComprehensionLocals(loc, state);
                ADDOP_I(Loc.NoLocation, ByteCodeOp.RERAISE, 0);

                UseLabel(state.End);
                RestoreInlinedComprehensionLocals(loc, state);
                state.PushedLocals = null;
            }
            if (state.FastHidden != null)
            {
                foreach (var k in state.FastHidden)
                    _u.Fasthidden[k] = false;
                state.FastHidden = null;
            }
        }

        private void ComprehensionIter(GeneratedComprehension comp)
        {
            VisitExpr(comp.Iter);
            ADDOP(LOC(comp.Iter), comp.IsAsync != 0 ? ByteCodeOp.GET_AITER : ByteCodeOp.GET_ITER);
        }

        // compiler_comprehension
        private void Comprehension(GeneratedExpr e, CompType type, string name, GeneratedSeq generators,
            GeneratedExpr elt, GeneratedExpr? val)
        {
            var inlineState = new InlinedComprehensionState();
            var scopeType = _u.ScopeKind;
            bool isTopLevelAwait = IsTopLevelAwait;
            var entry = _st.Lookup(e);
            bool isInlined = entry.CompInlined;
            bool isAsyncGenerator = entry.Coroutine;

            var loc = LOC(e);

            var outermost = (GeneratedComprehension)generators[0];
            if (isInlined)
            {
                ComprehensionIter(outermost);
                PushInlinedComprehensionState(loc, entry, inlineState);
            }
            else
            {
                EnterScope(name, ScopeType.Comprehension, e, e.LineNo);
            }

            if (isAsyncGenerator && type != CompType.GenExp && scopeType != ScopeType.AsyncFunction &&
                scopeType != ScopeType.Comprehension && !isTopLevelAwait)
                throw Error(loc, "asynchronous comprehension outside of an asynchronous function");

            if (type != CompType.GenExp)
            {
                ADDOP_I(loc, type switch
                {
                    CompType.ListComp => ByteCodeOp.BUILD_LIST,
                    CompType.SetComp => ByteCodeOp.BUILD_SET,
                    _ => ByteCodeOp.BUILD_MAP,
                }, 0);
                if (isInlined)
                    ADDOP_I(loc, ByteCodeOp.SWAP, 2);
            }

            ComprehensionGenerator(loc, generators, 0, 0, elt, val, type, isInlined);

            if (isInlined)
            {
                PopInlinedComprehensionState(loc, inlineState);
                return;
            }

            if (type != CompType.GenExp)
                ADDOP(LOC(e), ByteCodeOp.RETURN_VALUE);
            else
                WrapInStopiterationHandler();

            var co = OptimizeAndAssemble(true);
            ExitScope();
            if (isTopLevelAwait && isAsyncGenerator)
                _u.Ste.Coroutine = true;

            loc = LOC(e);
            MakeClosure(loc, co, 0);
            ComprehensionIter(outermost);
            ADDOP_I(loc, ByteCodeOp.CALL, 0);

            if (isAsyncGenerator && type != CompType.GenExp)
            {
                ADDOP_I(loc, ByteCodeOp.GET_AWAITABLE, 0);
                ADDOP_LOAD_CONST(loc, PyNone.Instance);
                ADD_YIELD_FROM(loc, true);
            }
        }

        private void WithExceptFinish(Label cleanup)
        {
            var suppress = NewLabel();
            ADDOP_JUMP(Loc.NoLocation, ByteCodeOp.POP_JUMP_IF_TRUE, suppress);
            ADDOP_I(Loc.NoLocation, ByteCodeOp.RERAISE, 2);

            UseLabel(suppress);
            ADDOP(Loc.NoLocation, ByteCodeOp.POP_TOP);
            ADDOP(Loc.NoLocation, ByteCodeOp.POP_BLOCK);
            ADDOP(Loc.NoLocation, ByteCodeOp.POP_EXCEPT);
            ADDOP(Loc.NoLocation, ByteCodeOp.POP_TOP);
            ADDOP(Loc.NoLocation, ByteCodeOp.POP_TOP);
            var exit = NewLabel();
            ADDOP_JUMP(Loc.NoLocation, ByteCodeOp.JUMP, exit);

            UseLabel(cleanup);
            POP_EXCEPT_AND_RERAISE(Loc.NoLocation);

            UseLabel(exit);
        }

        private void AsyncWith(GeneratedAsyncWith s, int pos)
        {
            var loc = LOC(s);
            var item = (GeneratedWithitem)s.Items[pos];
            if (IsTopLevelAwait)
                _u.Ste.Coroutine = true;
            else if (_u.ScopeKind != ScopeType.AsyncFunction)
                throw Error(loc, "'async with' outside async function");

            var block = NewLabel();
            var final = NewLabel();
            var exit = NewLabel();
            var cleanup = NewLabel();

            VisitExpr(item.ContextExpr);
            loc = LOC(item.ContextExpr);
            ADDOP(loc, ByteCodeOp.BEFORE_ASYNC_WITH);
            ADDOP_I(loc, ByteCodeOp.GET_AWAITABLE, 1);
            ADDOP_LOAD_CONST(loc, PyNone.Instance);
            ADD_YIELD_FROM(loc, true);

            ADDOP_JUMP(loc, ByteCodeOp.SETUP_WITH, final);

            UseLabel(block);
            PushFBlock(loc, FBlockType.ASYNC_WITH, block, final, s);

            if (item.OptionalVars != null)
                VisitExpr(item.OptionalVars);
            else
                ADDOP(loc, ByteCodeOp.POP_TOP);

            pos++;
            if (pos == Len(s.Items))
                VisitStmts(s.Body);
            else
                AsyncWith(s, pos);

            PopFBlock(FBlockType.ASYNC_WITH, block);

            ADDOP(loc, ByteCodeOp.POP_BLOCK);

            CallExitWithNones(loc);
            ADDOP_I(loc, ByteCodeOp.GET_AWAITABLE, 2);
            ADDOP_LOAD_CONST(loc, PyNone.Instance);
            ADD_YIELD_FROM(loc, true);

            ADDOP(loc, ByteCodeOp.POP_TOP);

            ADDOP_JUMP(loc, ByteCodeOp.JUMP, exit);

            UseLabel(final);

            ADDOP_JUMP(loc, ByteCodeOp.SETUP_CLEANUP, cleanup);
            ADDOP(loc, ByteCodeOp.PUSH_EXC_INFO);
            ADDOP(loc, ByteCodeOp.WITH_EXCEPT_START);
            ADDOP_I(loc, ByteCodeOp.GET_AWAITABLE, 2);
            ADDOP_LOAD_CONST(loc, PyNone.Instance);
            ADD_YIELD_FROM(loc, true);
            WithExceptFinish(cleanup);

            UseLabel(exit);
        }

        private void With(GeneratedWith s, int pos)
        {
            var item = (GeneratedWithitem)s.Items[pos];

            var block = NewLabel();
            var final = NewLabel();
            var exit = NewLabel();
            var cleanup = NewLabel();

            VisitExpr(item.ContextExpr);
            var loc = LOC(item.ContextExpr);
            ADDOP(loc, ByteCodeOp.BEFORE_WITH);
            ADDOP_JUMP(loc, ByteCodeOp.SETUP_WITH, final);

            UseLabel(block);
            PushFBlock(loc, FBlockType.WITH, block, final, s);

            if (item.OptionalVars != null)
                VisitExpr(item.OptionalVars);
            else
                ADDOP(loc, ByteCodeOp.POP_TOP);

            pos++;
            if (pos == Len(s.Items))
                VisitStmts(s.Body);
            else
                With(s, pos);

            ADDOP(Loc.NoLocation, ByteCodeOp.POP_BLOCK);
            PopFBlock(FBlockType.WITH, block);

            CallExitWithNones(loc);
            ADDOP(loc, ByteCodeOp.POP_TOP);
            ADDOP_JUMP(loc, ByteCodeOp.JUMP, exit);

            UseLabel(final);

            ADDOP_JUMP(loc, ByteCodeOp.SETUP_CLEANUP, cleanup);
            ADDOP(loc, ByteCodeOp.PUSH_EXC_INFO);
            ADDOP(loc, ByteCodeOp.WITH_EXCEPT_START);
            WithExceptFinish(cleanup);

            UseLabel(exit);
        }

        // compiler_visit_expr1
        private void VisitExpr(GeneratedExpr e)
        {
            System.Runtime.CompilerServices.RuntimeHelpers.EnsureSufficientExecutionStack();
            var loc = LOC(e);
            switch (e)
            {
                case GeneratedNamedExpr n:
                    VisitExpr(n.Value);
                    ADDOP_I(loc, ByteCodeOp.COPY, 1);
                    VisitExpr(n.Target);
                    break;
                case GeneratedBoolOp b:
                    Boolop(b);
                    break;
                case GeneratedBinOp b:
                    VisitExpr(b.Left);
                    VisitExpr(b.Right);
                    ADDOP_BINARY(loc, b.Op, false);
                    break;
                case GeneratedUnaryOp u:
                    VisitExpr(u.Operand);
                    switch (u.Op)
                    {
                        case GeneratedUAdd:
                            ADDOP_I(loc, ByteCodeOp.CALL_INTRINSIC_1, (int)IntrinsicFunction.INTRINSIC_UNARY_POSITIVE);
                            break;
                        case GeneratedInvert:
                            ADDOP(loc, ByteCodeOp.UNARY_INVERT);
                            break;
                        case GeneratedNot:
                            ADDOP(loc, ByteCodeOp.UNARY_NOT);
                            break;
                        default:
                            ADDOP(loc, ByteCodeOp.UNARY_NEGATIVE);
                            break;
                    }
                    break;
                case GeneratedLambda l:
                    Lambda(l);
                    break;
                case GeneratedIfExp i:
                    Ifexp(i);
                    break;
                case GeneratedDict d:
                    Dict(d);
                    break;
                case GeneratedSet s:
                    StarunpackHelper(loc, s.Elts, 0, ByteCodeOp.BUILD_SET, ByteCodeOp.SET_ADD, ByteCodeOp.SET_UPDATE, false);
                    break;
                case GeneratedGeneratorExp g:
                    Comprehension(e, CompType.GenExp, "<genexpr>", g.Generators, g.Elt, null);
                    break;
                case GeneratedListComp l:
                    Comprehension(e, CompType.ListComp, "<listcomp>", l.Generators, l.Elt, null);
                    break;
                case GeneratedSetComp s:
                    Comprehension(e, CompType.SetComp, "<setcomp>", s.Generators, s.Elt, null);
                    break;
                case GeneratedDictComp d:
                    Comprehension(e, CompType.DictComp, "<dictcomp>", d.Generators, d.Key, d.Value);
                    break;
                case GeneratedYield y:
                    if (!_u.Ste.IsFunctionLike)
                        throw Error(loc, "'yield' outside function");
                    if (y.Value != null)
                        VisitExpr(y.Value);
                    else
                        ADDOP_LOAD_CONST(loc, PyNone.Instance);
                    ADDOP_YIELD(loc);
                    break;
                case GeneratedYieldFrom y:
                    if (!_u.Ste.IsFunctionLike)
                        throw Error(loc, "'yield from' outside function");
                    if (_u.ScopeKind == ScopeType.AsyncFunction)
                        throw Error(loc, "'yield from' inside async function");
                    VisitExpr(y.Value);
                    ADDOP(loc, ByteCodeOp.GET_YIELD_FROM_ITER);
                    ADDOP_LOAD_CONST(loc, PyNone.Instance);
                    ADD_YIELD_FROM(loc, false);
                    break;
                case GeneratedAwait a:
                    if (!IsTopLevelAwait)
                    {
                        if (!_u.Ste.IsFunctionLike)
                            throw Error(loc, "'await' outside function");
                        if (_u.ScopeKind != ScopeType.AsyncFunction && _u.ScopeKind != ScopeType.Comprehension)
                            throw Error(loc, "'await' outside async function");
                    }
                    VisitExpr(a.Value);
                    ADDOP_I(loc, ByteCodeOp.GET_AWAITABLE, 0);
                    ADDOP_LOAD_CONST(loc, PyNone.Instance);
                    ADD_YIELD_FROM(loc, true);
                    break;
                case GeneratedCompare c:
                    Compare(c);
                    break;
                case GeneratedCall c:
                    Call(c);
                    break;
                case GeneratedConstant c:
                    ADDOP_LOAD_CONST(loc, ConstantValue(c.Value));
                    break;
                case GeneratedJoinedStr j:
                    JoinedStr(j);
                    break;
                case GeneratedFormattedValue f:
                    FormattedValue(f);
                    break;
                case GeneratedAttribute a:
                {
                    var ctx = CtxOf(a.Ctx);
                    if (ctx == Ctx.Load && CanOptimizeSuperCall(a))
                    {
                        var call = (GeneratedCall)a.Value;
                        LoadArgsForSuper(call);
                        AddopName(loc, ByteCodeOp.LOAD_SUPER_ATTR, _u.Names, a.Attr.Value,
                            Len(call.Args) != 0 ? NameOp.LoadSuperAttr : NameOp.LoadZeroSuperAttr);
                        loc = UpdateStartLocationToMatchAttr(loc, a);
                        ADDOP(loc, ByteCodeOp.NOP);
                        return;
                    }
                    VisitExpr(a.Value);
                    loc = UpdateStartLocationToMatchAttr(LOC(e), a);
                    switch (ctx)
                    {
                        case Ctx.Load:
                            AddopName(loc, ByteCodeOp.LOAD_ATTR, _u.Names, a.Attr.Value, NameOp.LoadAttr);
                            break;
                        case Ctx.Store:
                            ForbiddenName(loc, a.Attr.Value, ctx);
                            AddopName(loc, ByteCodeOp.STORE_ATTR, _u.Names, a.Attr.Value);
                            break;
                        case Ctx.Del:
                            AddopName(loc, ByteCodeOp.DELETE_ATTR, _u.Names, a.Attr.Value);
                            break;
                    }
                    break;
                }
                case GeneratedSubscript s:
                    Subscript(s);
                    break;
                case GeneratedStarred s:
                    throw Error(loc, s.Ctx is GeneratedStore
                        ? "starred assignment target must be in a list or tuple"
                        : "can't use starred expression here");
                case GeneratedSlice s:
                    ADDOP_I(loc, ByteCodeOp.BUILD_SLICE, Slice(s));
                    break;
                case GeneratedName n:
                    Nameop(loc, n.Id.Value, CtxOf(n.Ctx));
                    break;
                case GeneratedList l:
                    List(l);
                    break;
                case GeneratedTuple t:
                    Tuple(t);
                    break;
            }
        }

        private static bool IsTwoElementSlice(GeneratedExpr s) => s is GeneratedSlice { Step: null };

        private void Augassign(GeneratedAugAssign s)
        {
            var e = s.Target;
            var loc = LOC(e);

            switch (e)
            {
                case GeneratedAttribute a:
                    VisitExpr(a.Value);
                    ADDOP_I(loc, ByteCodeOp.COPY, 1);
                    loc = UpdateStartLocationToMatchAttr(loc, a);
                    AddopName(loc, ByteCodeOp.LOAD_ATTR, _u.Names, a.Attr.Value, NameOp.LoadAttr);
                    break;
                case GeneratedSubscript sub:
                    VisitExpr(sub.Value);
                    if (IsTwoElementSlice(sub.Slice))
                    {
                        Slice((GeneratedSlice)sub.Slice);
                        ADDOP_I(loc, ByteCodeOp.COPY, 3);
                        ADDOP_I(loc, ByteCodeOp.COPY, 3);
                        ADDOP_I(loc, ByteCodeOp.COPY, 3);
                        ADDOP(loc, ByteCodeOp.BINARY_SLICE);
                    }
                    else
                    {
                        VisitExpr(sub.Slice);
                        ADDOP_I(loc, ByteCodeOp.COPY, 2);
                        ADDOP_I(loc, ByteCodeOp.COPY, 2);
                        ADDOP(loc, ByteCodeOp.BINARY_SUBSCR);
                    }
                    break;
                case GeneratedName n:
                    Nameop(loc, n.Id.Value, Ctx.Load);
                    break;
                default:
                    throw new InvalidOperationException("invalid node type for augmented assignment");
            }

            loc = LOC(s);
            VisitExpr(s.Value);
            ADDOP_BINARY(loc, s.Op, true);

            loc = LOC(e);
            switch (e)
            {
                case GeneratedAttribute a:
                    loc = UpdateStartLocationToMatchAttr(loc, a);
                    ADDOP_I(loc, ByteCodeOp.SWAP, 2);
                    AddopName(loc, ByteCodeOp.STORE_ATTR, _u.Names, a.Attr.Value);
                    break;
                case GeneratedSubscript sub:
                    if (IsTwoElementSlice(sub.Slice))
                    {
                        ADDOP_I(loc, ByteCodeOp.SWAP, 4);
                        ADDOP_I(loc, ByteCodeOp.SWAP, 3);
                        ADDOP_I(loc, ByteCodeOp.SWAP, 2);
                        ADDOP(loc, ByteCodeOp.STORE_SLICE);
                    }
                    else
                    {
                        ADDOP_I(loc, ByteCodeOp.SWAP, 3);
                        ADDOP_I(loc, ByteCodeOp.SWAP, 2);
                        ADDOP(loc, ByteCodeOp.STORE_SUBSCR);
                    }
                    break;
                case GeneratedName n:
                    Nameop(loc, n.Id.Value, Ctx.Store);
                    break;
            }
        }

        private void CheckAnnExpr(GeneratedExpr e)
        {
            VisitExpr(e);
            ADDOP(LOC(e), ByteCodeOp.POP_TOP);
        }

        private void CheckAnnotation(GeneratedAnnAssign s)
        {
            if (_future.Annotations)
                return;
            if (_u.ScopeKind is ScopeType.Module or ScopeType.Class)
                CheckAnnExpr(s.Annotation);
        }

        private void CheckAnnSubscr(GeneratedExpr e)
        {
            switch (e)
            {
                case GeneratedSlice s:
                    if (s.Lower != null)
                        CheckAnnExpr(s.Lower);
                    if (s.Upper != null)
                        CheckAnnExpr(s.Upper);
                    if (s.Step != null)
                        CheckAnnExpr(s.Step);
                    break;
                case GeneratedTuple t:
                    for (int i = 0; i < Len(t.Elts); i++)
                        CheckAnnSubscr((GeneratedExpr)t.Elts[i]);
                    break;
                default:
                    CheckAnnExpr(e);
                    break;
            }
        }

        private void Annassign(GeneratedAnnAssign s)
        {
            var loc = LOC(s);
            var targ = s.Target;

            if (s.Value != null)
            {
                VisitExpr(s.Value);
                VisitExpr(targ);
            }
            switch (targ)
            {
                case GeneratedName n:
                    ForbiddenName(loc, n.Id.Value, Ctx.Store);
                    if (s.Simple != 0 && _u.ScopeKind is ScopeType.Module or ScopeType.Class)
                    {
                        if (_future.Annotations)
                            VisitAnnexpr(s.Annotation);
                        else
                            VisitExpr(s.Annotation);
                        AddopName(loc, ByteCodeOp.LOAD_NAME, _u.Names, "__annotations__");
                        ADDOP_LOAD_CONST(loc, new PyString(MaybeMangle(_u.Private, _u.Ste, n.Id.Value)));
                        ADDOP(loc, ByteCodeOp.STORE_SUBSCR);
                    }
                    break;
                case GeneratedAttribute a:
                    ForbiddenName(loc, a.Attr.Value, Ctx.Store);
                    if (s.Value == null)
                        CheckAnnExpr(a.Value);
                    break;
                case GeneratedSubscript sub:
                    if (s.Value == null)
                    {
                        CheckAnnExpr(sub.Value);
                        CheckAnnSubscr(sub.Slice);
                    }
                    break;
                default:
                    throw new InvalidOperationException("invalid node type for annotated assignment");
            }
            if (s.Simple == 0)
                CheckAnnotation(s);
        }

        private void Subscript(GeneratedSubscript e)
        {
            var loc = LOC(e);
            var ctx = CtxOf(e.Ctx);

            if (ctx == Ctx.Load)
            {
                CheckSubscripter(e.Value);
                CheckIndex(e.Value, e.Slice);
            }

            VisitExpr(e.Value);
            if (IsTwoElementSlice(e.Slice) && ctx != Ctx.Del)
            {
                Slice((GeneratedSlice)e.Slice);
                ADDOP(loc, ctx == Ctx.Load ? ByteCodeOp.BINARY_SLICE : ByteCodeOp.STORE_SLICE);
            }
            else
            {
                VisitExpr(e.Slice);
                ADDOP(loc, ctx switch
                {
                    Ctx.Load => ByteCodeOp.BINARY_SUBSCR,
                    Ctx.Store => ByteCodeOp.STORE_SUBSCR,
                    _ => ByteCodeOp.DELETE_SUBSCR,
                });
            }
        }

        private int Slice(GeneratedSlice s)
        {
            int n = 2;
            if (s.Lower != null)
                VisitExpr(s.Lower);
            else
                ADDOP_LOAD_CONST(LOC(s), PyNone.Instance);

            if (s.Upper != null)
                VisitExpr(s.Upper);
            else
                ADDOP_LOAD_CONST(LOC(s), PyNone.Instance);

            if (s.Step != null)
            {
                n++;
                VisitExpr(s.Step);
            }
            return n;
        }
    }
}
