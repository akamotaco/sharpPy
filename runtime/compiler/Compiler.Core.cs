// Python/compile.c of CPython v3.12.15: units, scopes, constants, frame blocks.
// Functions keep the names of the C file. A method here does what the C function of that name does.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using SharpPy.Generated;
using static SharpPy.PySymtable;

namespace SharpPy
{
    public readonly record struct Loc(int Line, int EndLine, int Col, int EndCol)
    {
        public static readonly Loc NoLocation = new(-1, -1, -1, -1);
        public static readonly Loc NextLocation = new(-2, -2, -1, -1);
        public bool IsNone => Line == -1;
    }

    public sealed partial class Compiler
    {
        private enum ScopeType
        {
            Module, Class, Function, AsyncFunction, Lambda, Comprehension, TypeParams,
        }

        private enum FBlockType
        {
            WHILE_LOOP, FOR_LOOP, TRY_EXCEPT, FINALLY_TRY, FINALLY_END, WITH, ASYNC_WITH, HANDLER_CLEANUP,
            POP_VALUE, EXCEPTION_HANDLER, EXCEPTION_GROUP_HANDLER, ASYNC_COMPREHENSION_GENERATOR, STOP_ITERATION,
        }

        private const int CO_MAXBLOCKS = 20;
        private const int CO_FUTURE_ANNOTATIONS = 0x1000000;
        private const int CO_FUTURE_BARRY_AS_BDFL = 0x400000;

        private sealed class FBlockInfo
        {
            public FBlockType Type;
            public Label Block;
            public Label Exit;
            public object? Datum;
            public Loc Loc;
        }

        private sealed class Unit
        {
            public PySTEntry Ste = null!;
            public ScopeType ScopeKind;
            public string? Private;
            public InstructionSequence Seq = new();
            public readonly List<FBlockInfo> FBlocks = new();
            public int InInlinedComp;

            public string Name = "";
            public string QualName = "";
            public readonly List<PyObject> Consts = new();
            public readonly Dictionary<string, int> ConstIndex = new();
            public readonly Dictionary<string, int> Names = new();
            public Dictionary<string, int> Varnames = new();
            public Dictionary<string, int> Cellvars = new();
            public Dictionary<string, int> Freevars = new();
            public readonly Dictionary<string, bool> Fasthidden = new();
            public int ArgCount, PosonlyArgCount, KwonlyArgCount, FirstLineNo;
        }

        private readonly string _filename;
        private readonly PySymtable _st;
        private readonly PyFuture _future;
        private readonly int _optimize;
        private readonly List<string>? _sourceLines;
        private readonly bool _isInteractiveInput;
        private bool _interactive;
        private int _nestLevel;
        private readonly Dictionary<string, PyObject> _constCache = new();
        private Unit _u = null!;
        private readonly List<Unit> _stack = new();
        private readonly ConditionalWeakTable<object, object> _ids = new();
        private long _nextId;

        private Compiler(string filename, PySymtable st, PyFuture future, int optimize, List<string>? sourceLines)
        {
            _filename = filename;
            _st = st;
            _future = future;
            _optimize = optimize;
            _sourceLines = sourceLines;
        }

        // _PyAST_Compile
        public static PyCodeObject Compile(GeneratedMod mod, string filename, int optimize, List<string>? sourceLines,
            int inheritedFlags = 0)
        {
            var future = PyFuture.FromAst(mod, filename, sourceLines);
            if ((inheritedFlags & CO_FUTURE_ANNOTATIONS) != 0)
                future.Annotations = true;
            if ((inheritedFlags & CO_FUTURE_BARRY_AS_BDFL) != 0)
                future.BarryAsBdfl = true;
            var folded = new AstOptimizer(optimize, future.Annotations).Optimize(mod);
            var st = PySymtable.Build(folded, filename, future.Annotations, sourceLines);
            return new Compiler(filename, st, future, optimize, sourceLines).CompilerMod(folded);
        }

        // LOC(x) and LOCATION()
        private static Loc LOC(GeneratedAstNode x) => new(x.LineNo, x.EndLineNo, x.ColOffset, x.EndColOffset);
        private static Loc LOCATION(int line, int endLine, int col, int endCol) => new(line, endLine, col, endCol);

        private static int Len(GeneratedSeq? seq) => seq?.Count ?? 0;

        // dictbytype
        private static Dictionary<string, int> DictByType(Dictionary<string, long> src, int scopeType, int flag, int offset)
        {
            int i = offset;
            var dest = new Dictionary<string, int>();
            foreach (var k in src.Keys.OrderBy(key => key, StringComparer.Ordinal))
            {
                long vi = src[k];
                long scope = (vi >> SCOPE_OFFSET) & SCOPE_MASK;
                if (scope == scopeType || (vi & flag) != 0)
                    dest[k] = i++;
            }
            return dest;
        }

        // compiler_set_qualname
        private void SetQualname()
        {
            var u = _u;
            string? @base = null;
            int stackSize = _stack.Count;
            if (stackSize > 1)
            {
                bool forceGlobal = false;
                var parent = _stack[stackSize - 1];
                if (parent.ScopeKind == ScopeType.TypeParams)
                {
                    if (stackSize == 2)
                    {
                        u.QualName = u.Name;
                        return;
                    }
                    parent = _stack[stackSize - 2];
                }
                if (u.ScopeKind is ScopeType.Function or ScopeType.AsyncFunction or ScopeType.Class)
                {
                    string mangled = Mangle(parent.Private, u.Name);
                    if (parent.Ste.GetScope(mangled) == GLOBAL_EXPLICIT)
                        forceGlobal = true;
                }
                if (!forceGlobal)
                {
                    @base = parent.ScopeKind is ScopeType.Function or ScopeType.AsyncFunction or ScopeType.Lambda
                        ? parent.QualName + ".<locals>"
                        : parent.QualName;
                }
            }
            u.QualName = @base != null ? @base + "." + u.Name : u.Name;
        }

        private static int DictAdd(Dictionary<string, int> dict, string o)
        {
            if (!dict.TryGetValue(o, out int arg))
            {
                arg = dict.Count;
                dict[o] = arg;
            }
            return arg;
        }

        private long IdOf(object o) =>
            (long)_ids.GetValue(o, _ => (object)(++_nextId));

        // _PyCode_ConstantKey: equal keys for constants that are the same object to the program.
        private string ConstantKey(PyObject o)
        {
            switch (o)
            {
                case PyNone:
                    return "N";
                case PyEllipsis:
                    return "E";
                case PyBool truth:
                    return truth.Value ? "b1" : "b0";
                case PyInt number:
                    return "i" + number.Value.ToString(CultureInfo.InvariantCulture);
                case PyFloat real:
                    return "f" + BitConverter.DoubleToInt64Bits(real.Value).ToString("x");
                case PyComplex complex:
                    return "c" + BitConverter.DoubleToInt64Bits(complex.Real).ToString("x") + "," +
                        BitConverter.DoubleToInt64Bits(complex.Imag).ToString("x");
                case PyString text:
                    return "s" + text.Value.Length + ":" + text.Value;
                case PyBytes bytes:
                    return "y" + Convert.ToHexString(bytes.Value);
                case PyTuple tuple:
                {
                    var key = new StringBuilder("t(");
                    foreach (var item in tuple.Items)
                        key.Append(ConstantKey(item)).Append(',');
                    return key.Append(')').ToString();
                }
                case PyFrozenSet set:
                {
                    var keys = new List<string>();
                    foreach (var item in PySet.IterableItems(set))
                        keys.Add(ConstantKey(item));
                    keys.Sort(StringComparer.Ordinal);
                    return "z(" + string.Join(",", keys) + ")";
                }
                default:
                    return "o" + IdOf(o);
            }
        }

        // merge_consts_recursive
        private PyObject MergeConsts(PyObject o, string key)
        {
            if (o is PyNone or PyEllipsis)
                return o;
            if (_constCache.TryGetValue(key, out var existing))
                return existing;
            _constCache[key] = o;
            if (o is PyTuple tuple)
            {
                var items = tuple.Items;
                for (int i = 0; i < items.Length; i++)
                    items[i] = MergeConsts(items[i], ConstantKey(items[i]));
            }
            return o;
        }

        // compiler_add_const
        private int AddConst(PyObject o)
        {
            string key = ConstantKey(o);
            var merged = MergeConsts(o, key);
            if (!_u.ConstIndex.TryGetValue(key, out int arg))
            {
                arg = _u.Consts.Count;
                _u.ConstIndex[key] = arg;
                _u.Consts.Add(merged);
            }
            return arg;
        }

        private void ADDOP(Loc loc, ByteCodeOp op) =>
            _u.Seq.AddOp(op, loc.Line, loc.Col, _filename);

        private void ADDOP_I(Loc loc, ByteCodeOp op, int arg) =>
            _u.Seq.AddOpWithArg(op, arg, loc.Line, loc.Col, _filename);

        private void ADDOP_JUMP(Loc loc, ByteCodeOp op, Label target) =>
            _u.Seq.AddOpWithLabel(op, target, loc.Line, loc.Col, _filename);

        private void ADDOP_LOAD_CONST(Loc loc, PyObject o) =>
            ADDOP_I(loc, ByteCodeOp.LOAD_CONST, AddConst(o));

        private enum NameOp
        {
            Plain, LoadAttr, LoadMethod, LoadSuperAttr, LoadSuperMethod, LoadZeroSuperAttr, LoadZeroSuperMethod,
        }

        // compiler_addop_name
        private void AddopName(Loc loc, ByteCodeOp op, Dictionary<string, int> dict, string o, NameOp kind = NameOp.Plain)
        {
            int arg = DictAdd(dict, MaybeMangle(_u.Private, _u.Ste, o));
            switch (kind)
            {
                case NameOp.LoadAttr:
                    arg <<= 1;
                    break;
                case NameOp.LoadMethod:
                    op = ByteCodeOp.LOAD_ATTR;
                    arg = (arg << 1) | 1;
                    break;
                case NameOp.LoadSuperAttr:
                    arg = (arg << 2) | 2;
                    break;
                case NameOp.LoadSuperMethod:
                    op = ByteCodeOp.LOAD_SUPER_ATTR;
                    arg = (arg << 2) | 3;
                    break;
                case NameOp.LoadZeroSuperAttr:
                    op = ByteCodeOp.LOAD_SUPER_ATTR;
                    arg <<= 2;
                    break;
                case NameOp.LoadZeroSuperMethod:
                    op = ByteCodeOp.LOAD_SUPER_ATTR;
                    arg = (arg << 2) | 1;
                    break;
            }
            ADDOP_I(loc, op, arg);
        }

        private Label NewLabel() => _u.Seq.NewLabel();
        private void UseLabel(Label label) => _u.Seq.UseLabel(label);

        // compiler_enter_scope
        private void EnterScope(string name, ScopeType scopeType, object key, int lineno)
        {
            var loc = LOCATION(lineno, lineno, 0, 0);
            var u = new Unit
            {
                ScopeKind = scopeType,
                Ste = _st.Lookup(key),
                Name = name,
                FirstLineNo = lineno,
            };
            int index = 0;
            foreach (var varname in u.Ste.Varnames)
                u.Varnames[varname] = index++;
            u.Cellvars = DictByType(u.Ste.Symbols, CELL, DEF_COMP_CELL, 0);
            if (u.Ste.NeedsClassClosure)
                DictAdd(u.Cellvars, "__class__");
            if (u.Ste.NeedsClassdict)
                DictAdd(u.Cellvars, "__classdict__");
            u.Freevars = DictByType(u.Ste.Symbols, FREE, DEF_FREE_CLASS, u.Cellvars.Count);

            if (_u != null)
            {
                _stack.Add(_u);
                u.Private = _u.Private;
            }
            _u = u;
            _nestLevel++;

            if (u.ScopeKind == ScopeType.Module)
                loc = loc with { Line = 0 };
            else
                SetQualname();
            ADDOP_I(loc, ByteCodeOp.RESUME, 0);
        }

        // compiler_exit_scope
        private void ExitScope()
        {
            _nestLevel--;
            int n = _stack.Count - 1;
            if (n >= 0)
            {
                _u = _stack[n];
                _stack.RemoveAt(n);
            }
            else
            {
                _u = null!;
            }
        }

        // find_ann
        private static bool FindAnn(GeneratedSeq? stmts)
        {
            for (int i = 0; i < Len(stmts); i++)
            {
                bool res;
                switch (stmts![i])
                {
                    case GeneratedAnnAssign:
                        return true;
                    case GeneratedFor s:
                        res = FindAnn(s.Body) || FindAnn(s.Orelse);
                        break;
                    case GeneratedAsyncFor s:
                        res = FindAnn(s.Body) || FindAnn(s.Orelse);
                        break;
                    case GeneratedWhile s:
                        res = FindAnn(s.Body) || FindAnn(s.Orelse);
                        break;
                    case GeneratedIf s:
                        res = FindAnn(s.Body) || FindAnn(s.Orelse);
                        break;
                    case GeneratedWith s:
                        res = FindAnn(s.Body);
                        break;
                    case GeneratedAsyncWith s:
                        res = FindAnn(s.Body);
                        break;
                    case GeneratedTry s:
                        for (int j = 0; j < Len(s.Handlers); j++)
                        {
                            if (FindAnn(((GeneratedExceptHandler)s.Handlers[j]).Body))
                                return true;
                        }
                        res = FindAnn(s.Body) || FindAnn(s.Finalbody) || FindAnn(s.Orelse);
                        break;
                    case GeneratedTryStar s:
                        for (int j = 0; j < Len(s.Handlers); j++)
                        {
                            if (FindAnn(((GeneratedExceptHandler)s.Handlers[j]).Body))
                                return true;
                        }
                        res = FindAnn(s.Body) || FindAnn(s.Finalbody) || FindAnn(s.Orelse);
                        break;
                    case GeneratedMatch s:
                        res = false;
                        for (int j = 0; j < Len(s.Cases); j++)
                        {
                            if (FindAnn(((GeneratedMatchCase)s.Cases[j]).Body))
                                return true;
                        }
                        break;
                    default:
                        res = false;
                        break;
                }
                if (res)
                    return true;
            }
            return false;
        }

        private void PushFBlock(Loc loc, FBlockType type, Label block, Label exit, object? datum)
        {
            if (_u.FBlocks.Count >= CO_MAXBLOCKS)
                throw Error(loc, "too many statically nested blocks");
            _u.FBlocks.Add(new FBlockInfo { Type = type, Block = block, Loc = loc, Exit = exit, Datum = datum });
        }

        private void PopFBlock(FBlockType type, Label block)
        {
            _u.FBlocks.RemoveAt(_u.FBlocks.Count - 1);
        }

        private void CallExitWithNones(Loc loc)
        {
            ADDOP_LOAD_CONST(loc, PyNone.Instance);
            ADDOP_LOAD_CONST(loc, PyNone.Instance);
            ADDOP_LOAD_CONST(loc, PyNone.Instance);
            ADDOP_I(loc, ByteCodeOp.CALL, 2);
        }

        private void ADD_YIELD_FROM(Loc loc, bool isAwait)
        {
            var send = NewLabel();
            var fail = NewLabel();
            var exit = NewLabel();

            UseLabel(send);
            ADDOP_JUMP(loc, ByteCodeOp.SEND, exit);
            ADDOP_JUMP(loc, ByteCodeOp.SETUP_FINALLY, fail);
            ADDOP_I(loc, ByteCodeOp.YIELD_VALUE, 0);
            ADDOP(Loc.NoLocation, ByteCodeOp.POP_BLOCK);
            ADDOP_I(loc, ByteCodeOp.RESUME, isAwait ? 3 : 2);
            ADDOP_JUMP(loc, ByteCodeOp.JUMP_NO_INTERRUPT, send);

            UseLabel(fail);
            ADDOP(loc, ByteCodeOp.CLEANUP_THROW);

            UseLabel(exit);
            ADDOP(loc, ByteCodeOp.END_SEND);
        }

        private void POP_EXCEPT_AND_RERAISE(Loc loc)
        {
            ADDOP_I(loc, ByteCodeOp.COPY, 3);
            ADDOP(loc, ByteCodeOp.POP_EXCEPT);
            ADDOP_I(loc, ByteCodeOp.RERAISE, 1);
        }

        // compiler_unwind_fblock
        private void UnwindFBlock(ref Loc ploc, FBlockInfo info, bool preserveTos)
        {
            switch (info.Type)
            {
                case FBlockType.WHILE_LOOP:
                case FBlockType.EXCEPTION_HANDLER:
                case FBlockType.EXCEPTION_GROUP_HANDLER:
                case FBlockType.ASYNC_COMPREHENSION_GENERATOR:
                case FBlockType.STOP_ITERATION:
                    return;

                case FBlockType.FOR_LOOP:
                    if (preserveTos)
                        ADDOP_I(ploc, ByteCodeOp.SWAP, 2);
                    ADDOP(ploc, ByteCodeOp.POP_TOP);
                    return;

                case FBlockType.TRY_EXCEPT:
                    ADDOP(ploc, ByteCodeOp.POP_BLOCK);
                    return;

                case FBlockType.FINALLY_TRY:
                    ADDOP(ploc, ByteCodeOp.POP_BLOCK);
                    if (preserveTos)
                        PushFBlock(ploc, FBlockType.POP_VALUE, Label.NoLabel, Label.NoLabel, null);
                    VisitStmts((GeneratedSeq?)info.Datum);
                    if (preserveTos)
                        PopFBlock(FBlockType.POP_VALUE, Label.NoLabel);
                    ploc = Loc.NoLocation;
                    return;

                case FBlockType.FINALLY_END:
                    if (preserveTos)
                        ADDOP_I(ploc, ByteCodeOp.SWAP, 2);
                    ADDOP(ploc, ByteCodeOp.POP_TOP);
                    if (preserveTos)
                        ADDOP_I(ploc, ByteCodeOp.SWAP, 2);
                    ADDOP(ploc, ByteCodeOp.POP_BLOCK);
                    ADDOP(ploc, ByteCodeOp.POP_EXCEPT);
                    return;

                case FBlockType.WITH:
                case FBlockType.ASYNC_WITH:
                    ploc = info.Loc;
                    ADDOP(ploc, ByteCodeOp.POP_BLOCK);
                    if (preserveTos)
                        ADDOP_I(ploc, ByteCodeOp.SWAP, 2);
                    CallExitWithNones(ploc);
                    if (info.Type == FBlockType.ASYNC_WITH)
                    {
                        ADDOP_I(ploc, ByteCodeOp.GET_AWAITABLE, 2);
                        ADDOP_LOAD_CONST(ploc, PyNone.Instance);
                        ADD_YIELD_FROM(ploc, true);
                    }
                    ADDOP(ploc, ByteCodeOp.POP_TOP);
                    ploc = Loc.NoLocation;
                    return;

                case FBlockType.HANDLER_CLEANUP:
                    if (info.Datum != null)
                        ADDOP(ploc, ByteCodeOp.POP_BLOCK);
                    if (preserveTos)
                        ADDOP_I(ploc, ByteCodeOp.SWAP, 2);
                    ADDOP(ploc, ByteCodeOp.POP_BLOCK);
                    ADDOP(ploc, ByteCodeOp.POP_EXCEPT);
                    if (info.Datum != null)
                    {
                        ADDOP_LOAD_CONST(ploc, PyNone.Instance);
                        Nameop(ploc, (string)info.Datum, Ctx.Store);
                        Nameop(ploc, (string)info.Datum, Ctx.Del);
                    }
                    return;

                case FBlockType.POP_VALUE:
                    if (preserveTos)
                        ADDOP_I(ploc, ByteCodeOp.SWAP, 2);
                    ADDOP(ploc, ByteCodeOp.POP_TOP);
                    return;
            }
        }

        // compiler_unwind_fblock_stack
        private void UnwindFBlockStack(ref Loc ploc, bool preserveTos, ref FBlockInfo? loop, bool wantLoop)
        {
            int n = _u.FBlocks.Count;
            if (n == 0)
                return;
            var top = _u.FBlocks[n - 1];
            if (top.Type == FBlockType.EXCEPTION_GROUP_HANDLER)
                throw Error(ploc, "'break', 'continue' and 'return' cannot appear in an except* block");
            if (wantLoop && top.Type is FBlockType.WHILE_LOOP or FBlockType.FOR_LOOP)
            {
                loop = top;
                return;
            }
            _u.FBlocks.RemoveAt(n - 1);
            UnwindFBlock(ref ploc, top, preserveTos);
            UnwindFBlockStack(ref ploc, preserveTos, ref loop, wantLoop);
            _u.FBlocks.Add(top);
        }

        private void VisitStmts(GeneratedSeq? seq)
        {
            for (int i = 0; i < Len(seq); i++)
                VisitStmt((GeneratedStmt)seq![i]);
        }

        private void VisitExprs(GeneratedSeq? seq)
        {
            for (int i = 0; i < Len(seq); i++)
                VisitExpr((GeneratedExpr)seq![i]);
        }

        // compiler_body
        private void CompilerBody(Loc loc, GeneratedSeq stmts)
        {
            int i = 0;
            if (_u.ScopeKind == ScopeType.Module && Len(stmts) != 0)
                loc = LOC((GeneratedStmt)stmts[0]);
            if (FindAnn(stmts))
                ADDOP(loc, ByteCodeOp.SETUP_ANNOTATIONS);
            if (Len(stmts) == 0)
                return;
            if (_optimize < 2 && PyFuture.GetDocString(stmts) != null)
            {
                i = 1;
                VisitExpr(((GeneratedExprStmt)stmts[0]).Value);
                Nameop(Loc.NoLocation, "__doc__", Ctx.Store);
            }
            for (; i < stmts.Count; i++)
                VisitStmt((GeneratedStmt)stmts[i]);
        }

        // compiler_codegen and compiler_mod
        private PyCodeObject CompilerMod(GeneratedMod mod)
        {
            EnterScope("<module>", ScopeType.Module, mod, 1);
            var loc = LOCATION(1, 1, 0, 0);
            switch (mod)
            {
                case GeneratedModule module:
                    CompilerBody(loc, module.Body);
                    break;
                case GeneratedInteractive interactive:
                    if (FindAnn(interactive.Body))
                        ADDOP(loc, ByteCodeOp.SETUP_ANNOTATIONS);
                    _interactive = true;
                    VisitStmts(interactive.Body);
                    break;
                case GeneratedExpression expression:
                    VisitExpr(expression.Body);
                    break;
                default:
                    throw new InvalidOperationException("module kind should not be possible");
            }
            var co = OptimizeAndAssemble(mod is not GeneratedExpression);
            ExitScope();
            return co;
        }

        // get_ref_type
        private int GetRefType(string name)
        {
            if (_u.ScopeKind == ScopeType.Class && name is "__class__" or "__classdict__")
                return CELL;
            int scope = _u.Ste.GetScope(name);
            if (scope == 0)
                throw new InvalidOperationException($"_PyST_GetScope(name='{name}') failed: unknown scope in unit {_u.Name}");
            return scope;
        }

        // compiler_make_closure
        private void MakeClosure(Loc loc, PyCodeObject co, int flags)
        {
            if (co.FreeVars.Count != 0)
            {
                foreach (var name in co.FreeVars)
                {
                    int reftype = GetRefType(name);
                    var dict = reftype == CELL ? _u.Cellvars : _u.Freevars;
                    if (!dict.TryGetValue(name, out int arg))
                        throw new InvalidOperationException(
                            $"compiler_lookup_arg(name='{name}') with reftype={reftype} failed in {_u.Name}; freevars of code {co.Name}");
                    ADDOP_I(loc, ByteCodeOp.LOAD_CLOSURE, arg);
                }
                flags |= 0x08;
                ADDOP_I(loc, ByteCodeOp.BUILD_TUPLE, co.FreeVars.Count);
            }
            ADDOP_LOAD_CONST(loc, co);
            ADDOP_I(loc, ByteCodeOp.MAKE_FUNCTION, flags);
        }

        private void Decorators(GeneratedSeq? decos) => VisitExprs(decos);

        private void ApplyDecorators(GeneratedSeq? decos)
        {
            for (int i = Len(decos) - 1; i > -1; i--)
                ADDOP_I(LOC((GeneratedExpr)decos![i]), ByteCodeOp.CALL, 0);
        }

        // compiler_visit_kwonlydefaults
        private int VisitKwonlydefaults(Loc loc, GeneratedSeq? kwonlyargs, GeneratedSeq? kwDefaults)
        {
            List<PyObject>? keys = null;
            for (int i = 0; i < Len(kwonlyargs); i++)
            {
                var arg = (GeneratedArg)kwonlyargs![i];
                if (kwDefaults![i] is GeneratedExpr value)
                {
                    keys ??= new List<PyObject>();
                    keys.Add(new PyString(MaybeMangle(_u.Private, _u.Ste, arg.Arg.Value)));
                    VisitExpr(value);
                }
            }
            if (keys == null)
                return 0;
            ADDOP_LOAD_CONST(loc, new PyTuple(keys.ToArray()));
            ADDOP_I(loc, ByteCodeOp.BUILD_CONST_KEY_MAP, keys.Count);
            return 1;
        }

        private void VisitAnnexpr(GeneratedExpr annotation) =>
            ADDOP_LOAD_CONST(LOC(annotation), new PyString(AstUnparse.ExprAsUnicode(annotation)));

        private void VisitArgannotation(string id, GeneratedExpr? annotation, ref int annotationsLen, Loc loc)
        {
            if (annotation == null)
                return;
            ADDOP_LOAD_CONST(loc, new PyString(MaybeMangle(_u.Private, _u.Ste, id)));
            if (_future.Annotations)
            {
                VisitAnnexpr(annotation);
            }
            else if (annotation is GeneratedStarred starred)
            {
                VisitExpr(starred.Value);
                ADDOP_I(loc, ByteCodeOp.UNPACK_SEQUENCE, 1);
            }
            else
            {
                VisitExpr(annotation);
            }
            annotationsLen += 2;
        }

        private void VisitArgannotations(GeneratedSeq? args, ref int annotationsLen, Loc loc)
        {
            for (int i = 0; i < Len(args); i++)
            {
                var arg = (GeneratedArg)args![i];
                VisitArgannotation(arg.Arg.Value, arg.Annotation, ref annotationsLen, loc);
            }
        }

        // compiler_visit_annotations
        private int VisitAnnotations(Loc loc, GeneratedArguments args, GeneratedExpr? returns)
        {
            int annotationsLen = 0;
            VisitArgannotations(args.Args, ref annotationsLen, loc);
            VisitArgannotations(args.Posonlyargs, ref annotationsLen, loc);
            if (args.Vararg?.Annotation != null)
                VisitArgannotation(args.Vararg.Arg.Value, args.Vararg.Annotation, ref annotationsLen, loc);
            VisitArgannotations(args.Kwonlyargs, ref annotationsLen, loc);
            if (args.Kwarg?.Annotation != null)
                VisitArgannotation(args.Kwarg.Arg.Value, args.Kwarg.Annotation, ref annotationsLen, loc);
            VisitArgannotation("return", returns, ref annotationsLen, loc);
            if (annotationsLen != 0)
            {
                ADDOP_I(loc, ByteCodeOp.BUILD_TUPLE, annotationsLen);
                return 1;
            }
            return 0;
        }

        // compiler_default_arguments
        private int DefaultArguments(Loc loc, GeneratedArguments args)
        {
            int funcflags = 0;
            if (Len(args.Defaults) > 0)
            {
                VisitExprs(args.Defaults);
                ADDOP_I(loc, ByteCodeOp.BUILD_TUPLE, Len(args.Defaults));
                funcflags |= 0x01;
            }
            if (args.Kwonlyargs != null && VisitKwonlydefaults(loc, args.Kwonlyargs, args.KwDefaults) > 0)
                funcflags |= 0x02;
            return funcflags;
        }

        private void ForbiddenName(Loc loc, string name, Ctx ctx)
        {
            if (ctx == Ctx.Store && name == "__debug__")
                throw Error(loc, "cannot assign to __debug__");
            if (ctx == Ctx.Del && name == "__debug__")
                throw Error(loc, "cannot delete __debug__");
        }

        private void CheckDebugOneArg(GeneratedArg? arg)
        {
            if (arg != null)
                ForbiddenName(LOC(arg), arg.Arg.Value, Ctx.Store);
        }

        private void CheckDebugArgsSeq(GeneratedSeq? args)
        {
            for (int i = 0; i < Len(args); i++)
                CheckDebugOneArg((GeneratedArg)args![i]);
        }

        private void CheckDebugArgs(GeneratedArguments args)
        {
            CheckDebugArgsSeq(args.Posonlyargs);
            CheckDebugArgsSeq(args.Args);
            CheckDebugOneArg(args.Vararg);
            CheckDebugArgsSeq(args.Kwonlyargs);
            CheckDebugOneArg(args.Kwarg);
        }

        // wrap_in_stopiteration_handler
        private void WrapInStopiterationHandler()
        {
            var handler = NewLabel();
            _u.Seq.InsertAt(0, new Instruction(ByteCodeOp.SETUP_CLEANUP, handler, -1, -1, _filename));
            ADDOP_LOAD_CONST(Loc.NoLocation, PyNone.Instance);
            ADDOP(Loc.NoLocation, ByteCodeOp.RETURN_VALUE);
            UseLabel(handler);
            ADDOP_I(Loc.NoLocation, ByteCodeOp.CALL_INTRINSIC_1, (int)IntrinsicFunction.INTRINSIC_STOPITERATION_ERROR);
            ADDOP_I(Loc.NoLocation, ByteCodeOp.RERAISE, 1);
        }

        // compiler_error
        private PythonException Error(Loc loc, string message) =>
            PySyntaxError.At(message, _filename, loc.Line, loc.Col + 1, loc.EndLine, loc.EndCol + 1, _sourceLines);
    }
}
