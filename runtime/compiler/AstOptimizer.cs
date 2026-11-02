// Python/ast_opt.c of CPython v3.12.15. C overwrites a node with COPY_NODE; here a fold returns its replacement.
using System;
using System.Collections.Generic;
using System.Numerics;
using SharpPy.Generated;

namespace SharpPy
{
    /// <summary>A constant the optimizer computed: a tuple, a frozenset, anything the parser can't write.</summary>
    public sealed class GeneratedPyConstantObject : GeneratedPyConstant
    {
        public PyObject Value { get; }
        public GeneratedPyConstantObject(PyObject value) => Value = value;
    }

    public sealed class AstOptimizer
    {
        private const int NB_BITS = 128;
        private const int MaxIntSize = 128;
        private const int MaxCollectionSize = 256;
        private const int MaxStrSize = 4096;
        private const int MaxTotalItems = 1024;

        private readonly int _optimize;
        private readonly bool _futureAnnotations;

        public AstOptimizer(int optimize, bool futureAnnotations)
        {
            _optimize = optimize;
            _futureAnnotations = futureAnnotations;
        }

        public static PyObject Value(GeneratedPyConstant value) => value switch
        {
            GeneratedPyConstantObject o => o.Value,
            GeneratedPyConstantNone => PyNone.Instance,
            GeneratedPyConstantBool b => b.Value ? PyBool.True : PyBool.False,
            GeneratedPyConstantInt i => new PyInt(i.Value),
            GeneratedPyConstantFloat f => new PyFloat(f.Value),
            GeneratedPyConstantString s => new PyString(s.Value),
            GeneratedPyConstantBytes bytes => new PyBytes(bytes.Value),
            GeneratedPyConstantComplex c => new PyComplex(c.Real, c.Imag),
            GeneratedPyConstantEllipsis => PyEllipsis.Instance,
            _ => throw new NotImplementedException($"Unknown constant type: {value.GetType().Name}"),
        };

        private static Tnode Located<Tnode>(Tnode created, GeneratedAstNode from) where Tnode : GeneratedAstNode
        {
            created.LineNo = from.LineNo;
            created.ColOffset = from.ColOffset;
            created.EndLineNo = from.EndLineNo;
            created.EndColOffset = from.EndColOffset;
            return created;
        }

        private static GeneratedConstant MakeConst(GeneratedExpr node, PyObject value) =>
            Located(new GeneratedConstant { Value = new GeneratedPyConstantObject(value) }, node);

        // make_const with an expression that may fail
        private static GeneratedExpr TryMakeConst(GeneratedExpr node, Func<PyObject?> compute)
        {
            try
            {
                var value = compute();
                return value == null ? node : MakeConst(node, value);
            }
            catch (PythonException)
            {
                return node;
            }
        }

        private static PyObject Const(GeneratedExpr e) => Value(((GeneratedConstant)e).Value);

        private static int Len(GeneratedSeq? seq) => seq?.Count ?? 0;

        private static bool HasStarred(GeneratedSeq? elts)
        {
            for (int i = 0; i < Len(elts); i++)
            {
                if (elts![i] is GeneratedStarred)
                    return true;
            }
            return false;
        }

        // unary_not and fold_unaryop
        private static GeneratedExpr FoldUnaryop(GeneratedUnaryOp node)
        {
            var arg = node.Operand;
            if (arg is not GeneratedConstant)
            {
                if (node.Op is GeneratedNot && arg is GeneratedCompare compare && Len(compare.Ops) == 1)
                {
                    GeneratedCmpop? op = compare.Ops[0] switch
                    {
                        GeneratedIs => GeneratedIsNot.Instance,
                        GeneratedIsNot => GeneratedIs.Instance,
                        GeneratedIn => GeneratedNotIn.Instance,
                        GeneratedNotIn => GeneratedIn.Instance,
                        _ => null,
                    };
                    if (op != null)
                    {
                        var ops = new GeneratedCmpopSeq();
                        ops.Add(op);
                        return Located(new GeneratedCompare { Left = compare.Left, Ops = ops, Comparators = compare.Comparators }, compare);
                    }
                }
                return node;
            }
            var value = Const(arg);
            return node.Op switch
            {
                GeneratedNot => MakeConst(node, value.IsTrue() ? PyBool.False : PyBool.True),
                GeneratedInvert => TryMakeConst(node, () => value.BitwiseNot()),
                GeneratedUSub => TryMakeConst(node, () => value.Negative()),
                _ => TryMakeConst(node, () => value.Positive()),
            };
        }

        private static PyObject[]? ConstantValues(GeneratedSeq elts)
        {
            var values = new PyObject[Len(elts)];
            for (int i = 0; i < values.Length; i++)
            {
                if (elts[i] is not GeneratedConstant constant)
                    return null;
                values[i] = Value(constant.Value);
            }
            return values;
        }

        // make_const_tuple and fold_tuple
        private static GeneratedExpr FoldTuple(GeneratedTuple node)
        {
            if (node.Ctx is not GeneratedLoad)
                return node;
            var values = ConstantValues(node.Elts);
            return values == null ? node : MakeConst(node, new PyTuple(values));
        }

        private static long CheckComplexity(PyObject obj, long limit)
        {
            if (obj is PyTuple tuple)
            {
                limit -= tuple.Items.Length;
                for (int i = 0; limit >= 0 && i < tuple.Items.Length; i++)
                    limit = CheckComplexity(tuple.Items[i], limit);
                return limit;
            }
            if (obj is PyFrozenSet set)
            {
                limit -= set.Length();
                foreach (var item in PySet.IterableItems(set))
                {
                    if (limit < 0)
                        break;
                    limit = CheckComplexity(item, limit);
                }
            }
            return limit;
        }

        private static bool IsInt(PyObject value) => value is PyInt or PyBool;

        private static BigInteger IntValue(PyObject value) =>
            value is PyBool truth ? (truth.Value ? BigInteger.One : BigInteger.Zero) : ((PyInt)value).Value;

        private static long NumBits(BigInteger value) => (long)BigInteger.Abs(value).GetBitLength();

        private static long SizeOf(PyObject value) => value switch
        {
            PyTuple tuple => tuple.Items.Length,
            PyFrozenSet set => set.Length(),
            PyString text => text.Length(),
            PyBytes bytes => bytes.Length(),
            _ => 0,
        };

        private static bool SafeMultiply(PyObject v, PyObject w)
        {
            if (IsInt(v) && IsInt(w))
            {
                var a = IntValue(v);
                var b = IntValue(w);
                return a.IsZero || b.IsZero || NumBits(a) + NumBits(b) <= MaxIntSize;
            }
            if (IsInt(v) && w is PyTuple or PyFrozenSet)
            {
                long size = SizeOf(w);
                if (size == 0)
                    return true;
                var n = IntValue(v);
                if (n.Sign < 0 || n > MaxCollectionSize / size)
                    return false;
                return n.IsZero || CheckComplexity(w, MaxTotalItems / (long)n) >= 0;
            }
            if (IsInt(v) && w is PyString or PyBytes)
            {
                long size = SizeOf(w);
                if (size == 0)
                    return true;
                var n = IntValue(v);
                return n.Sign >= 0 && n <= MaxStrSize / size;
            }
            if (IsInt(w) && v is PyTuple or PyFrozenSet or PyString or PyBytes)
                return SafeMultiply(w, v);
            return true;
        }

        private static bool SafePower(PyObject v, PyObject w)
        {
            if (IsInt(v) && IsInt(w))
            {
                var a = IntValue(v);
                var b = IntValue(w);
                if (!a.IsZero && b.Sign > 0)
                    return b <= long.MaxValue && NumBits(a) <= MaxIntSize / (long)b;
            }
            return true;
        }

        private static bool SafeLshift(PyObject v, PyObject w)
        {
            if (IsInt(v) && IsInt(w))
            {
                var a = IntValue(v);
                var b = IntValue(w);
                if (!a.IsZero && !b.IsZero)
                    return b.Sign > 0 && b <= MaxIntSize && NumBits(a) <= MaxIntSize - (long)b;
            }
            return true;
        }

        private static GeneratedExpr FoldBinop(GeneratedBinOp node)
        {
            var lhs = node.Left;
            var rhs = node.Right;
            if (lhs is not GeneratedConstant)
                return node;
            var lv = Const(lhs);
            if (node.Op is GeneratedMod_ && rhs is GeneratedTuple arguments && lv is PyString format &&
                !HasStarred(arguments.Elts))
                return OptimizeFormat(node, format.Value, arguments.Elts);
            if (rhs is not GeneratedConstant)
                return node;
            var rv = Const(rhs);
            BinaryOpType? op = node.Op switch
            {
                GeneratedAdd => BinaryOpType.ADD,
                GeneratedSub => BinaryOpType.SUBTRACT,
                GeneratedMult when SafeMultiply(lv, rv) => BinaryOpType.MULTIPLY,
                GeneratedDiv => BinaryOpType.TRUE_DIVIDE,
                GeneratedFloorDiv => BinaryOpType.FLOOR_DIVIDE,
                GeneratedMod_ when lv is not (PyString or PyBytes) => BinaryOpType.MODULO,
                GeneratedLShift when SafeLshift(lv, rv) => BinaryOpType.LSHIFT,
                GeneratedRShift => BinaryOpType.RSHIFT,
                GeneratedBitOr => BinaryOpType.OR,
                GeneratedBitXor => BinaryOpType.XOR,
                GeneratedBitAnd => BinaryOpType.AND,
                GeneratedPow when SafePower(lv, rv) => BinaryOpType.POWER,
                _ => null,
            };
            if (op == null)
                return node;
            return TryMakeConst(node, () => PyVM.Instance.ExecuteBinaryOpType(lv, rv, op.Value));
        }

        private static GeneratedConstant Unlocated(string text) => new()
        {
            Value = new GeneratedPyConstantString(text), LineNo = -1, ColOffset = -1, EndLineNo = -1, EndColOffset = -1,
        };

        // parse_literal
        private static GeneratedExpr? ParseLiteral(string format, ref int position)
        {
            int start = position;
            bool hasPercents = false;
            while (position < format.Length)
            {
                if (format[position] != '%')
                {
                    position++;
                }
                else if (position + 1 < format.Length && format[position + 1] == '%')
                {
                    hasPercents = true;
                    position += 2;
                }
                else
                {
                    break;
                }
            }
            if (position == start)
                return null;
            string text = format.Substring(start, position - start);
            return Unlocated(hasPercents ? text.Replace("%%", "%") : text);
        }

        private const int MaxDigits = 3;

        // simple_format_arg_parse
        private static bool SimpleFormatArgParse(string format, ref int position, out char spec, out bool leftJustify, out int width, out int precision)
        {
            spec = '\0';
            leftJustify = false;
            width = -1;
            precision = -1;
            int pos = position;
            bool Next(out char ch)
            {
                ch = '\0';
                if (pos >= format.Length)
                    return false;
                ch = format[pos++];
                return true;
            }

            char c;
            while (true)
            {
                if (!Next(out c))
                    return false;
                if (c == '-')
                    leftJustify = true;
                else if (c != '+' && c != ' ' && c != '#' && c != '0')
                    break;
            }
            if (c >= '0' && c <= '9')
            {
                width = 0;
                int digits = 0;
                while (c >= '0' && c <= '9')
                {
                    width = width * 10 + (c - '0');
                    if (!Next(out c))
                        return false;
                    if (++digits >= MaxDigits)
                        return false;
                }
            }
            if (c == '.')
            {
                if (!Next(out c))
                    return false;
                precision = 0;
                if (c >= '0' && c <= '9')
                {
                    int digits = 0;
                    while (c >= '0' && c <= '9')
                    {
                        precision = precision * 10 + (c - '0');
                        if (!Next(out c))
                            return false;
                        if (++digits >= MaxDigits)
                            return false;
                    }
                }
            }
            spec = c;
            position = pos;
            return true;
        }

        // parse_format
        private static GeneratedExpr? ParseFormat(string format, ref int position, GeneratedExpr arg)
        {
            if (!SimpleFormatArgParse(format, ref position, out char spec, out bool leftJustify, out int width, out int precision))
                return null;
            if (spec != 's' && spec != 'r' && spec != 'a')
                return null;
            string text = "";
            if (!leftJustify && width > 0)
                text += ">";
            if (width >= 0)
                text += width;
            if (precision >= 0)
                text += "." + precision;
            return Located(new GeneratedFormattedValue
            {
                Value = arg,
                Conversion = spec,
                FormatSpec = text.Length > 0 ? Unlocated(text) : null,
            }, arg);
        }

        // optimize_format
        private static GeneratedExpr OptimizeFormat(GeneratedExpr node, string format, GeneratedSeq elts)
        {
            int position = 0;
            int count = 0;
            var values = new GeneratedExprSeq();
            while (true)
            {
                var literal = ParseLiteral(format, ref position);
                if (literal != null)
                    values.Add(literal);
                if (position >= format.Length)
                    break;
                if (count >= elts.Count)
                    return node;
                position++;
                var formatted = ParseFormat(format, ref position, (GeneratedExpr)elts[count]);
                count++;
                if (formatted == null)
                    return node;
                values.Add(formatted);
            }
            if (count < elts.Count)
                return node;
            return Located(new GeneratedJoinedStr { Values = values }, node);
        }

        private static GeneratedExpr FoldSubscr(GeneratedSubscript node)
        {
            if (node.Ctx is not GeneratedLoad || node.Value is not GeneratedConstant || node.Slice is not GeneratedConstant)
                return node;
            var value = Const(node.Value);
            var index = Const(node.Slice);
            return TryMakeConst(node, () => value.GetItem(index));
        }

        // fold_iter
        private static GeneratedExpr FoldIter(GeneratedExpr node)
        {
            if (node is GeneratedList list)
            {
                if (HasStarred(list.Elts))
                    return node;
                var values = ConstantValues(list.Elts);
                if (values != null)
                    return MakeConst(node, new PyTuple(values));
                return Located(new GeneratedTuple { Elts = list.Elts, Ctx = list.Ctx }, node);
            }
            if (node is GeneratedSet set)
            {
                var values = ConstantValues(set.Elts);
                if (values != null)
                    return MakeConst(node, new PyFrozenSet(values));
            }
            return node;
        }

        // fold_compare
        private static GeneratedExpr FoldCompare(GeneratedCompare node)
        {
            int last = Len(node.Ops) - 1;
            if (node.Ops[last] is GeneratedIn or GeneratedNotIn)
                node.Comparators[last] = FoldIter((GeneratedExpr)node.Comparators[last]);
            return node;
        }

        private void Exprs(GeneratedSeq? seq)
        {
            for (int i = 0; i < Len(seq); i++)
            {
                if (seq![i] is GeneratedExpr e)
                    seq[i] = Expr(e);
            }
        }

        private GeneratedExpr? Opt(GeneratedExpr? e) => e == null ? null : Expr(e);

        private void Comprehensions(GeneratedSeq? seq)
        {
            for (int i = 0; i < Len(seq); i++)
            {
                var comp = (GeneratedComprehension)seq![i];
                comp.Target = Expr(comp.Target);
                comp.Iter = Expr(comp.Iter);
                Exprs(comp.Ifs);
                comp.Iter = FoldIter(comp.Iter);
            }
        }

        private void Keywords(GeneratedSeq? seq)
        {
            for (int i = 0; i < Len(seq); i++)
            {
                if (seq![i] is GeneratedKeyword keyword)
                    keyword.Value = Expr(keyword.Value);
            }
        }

        private void Arg(GeneratedArg? arg)
        {
            if (arg != null && !_futureAnnotations)
                arg.Annotation = Opt(arg.Annotation);
        }

        private void Args(GeneratedSeq? seq)
        {
            for (int i = 0; i < Len(seq); i++)
                Arg((GeneratedArg)seq![i]);
        }

        private void Arguments(GeneratedArguments node)
        {
            Args(node.Posonlyargs);
            Args(node.Args);
            Arg(node.Vararg);
            Args(node.Kwonlyargs);
            Exprs(node.KwDefaults);
            Arg(node.Kwarg);
            Exprs(node.Defaults);
        }

        // astfold_expr
        private GeneratedExpr Expr(GeneratedExpr node)
        {
            System.Runtime.CompilerServices.RuntimeHelpers.EnsureSufficientExecutionStack();
            switch (node)
            {
                case GeneratedBoolOp b:
                    Exprs(b.Values);
                    break;
                case GeneratedBinOp b:
                    b.Left = Expr(b.Left);
                    b.Right = Expr(b.Right);
                    return FoldBinop(b);
                case GeneratedUnaryOp u:
                    u.Operand = Expr(u.Operand);
                    return FoldUnaryop(u);
                case GeneratedLambda l:
                    Arguments(l.Args);
                    l.Body = Expr(l.Body);
                    break;
                case GeneratedIfExp i:
                    i.Test = Expr(i.Test);
                    i.Body = Expr(i.Body);
                    i.Orelse = Expr(i.Orelse);
                    break;
                case GeneratedDict d:
                    Exprs(d.Keys);
                    Exprs(d.Values);
                    break;
                case GeneratedSet s:
                    Exprs(s.Elts);
                    break;
                case GeneratedListComp c:
                    c.Elt = Expr(c.Elt);
                    Comprehensions(c.Generators);
                    break;
                case GeneratedSetComp c:
                    c.Elt = Expr(c.Elt);
                    Comprehensions(c.Generators);
                    break;
                case GeneratedDictComp c:
                    c.Key = Expr(c.Key);
                    c.Value = Expr(c.Value);
                    Comprehensions(c.Generators);
                    break;
                case GeneratedGeneratorExp c:
                    c.Elt = Expr(c.Elt);
                    Comprehensions(c.Generators);
                    break;
                case GeneratedAwait a:
                    a.Value = Expr(a.Value);
                    break;
                case GeneratedYield y:
                    y.Value = Opt(y.Value);
                    break;
                case GeneratedYieldFrom y:
                    y.Value = Expr(y.Value);
                    break;
                case GeneratedCompare c:
                    c.Left = Expr(c.Left);
                    Exprs(c.Comparators);
                    return FoldCompare(c);
                case GeneratedCall c:
                    c.Func = Expr(c.Func);
                    Exprs(c.Args);
                    Keywords(c.Keywords);
                    break;
                case GeneratedFormattedValue f:
                    f.Value = Expr(f.Value);
                    f.FormatSpec = Opt(f.FormatSpec);
                    break;
                case GeneratedJoinedStr j:
                    Exprs(j.Values);
                    break;
                case GeneratedAttribute a:
                    a.Value = Expr(a.Value);
                    break;
                case GeneratedSubscript s:
                    s.Value = Expr(s.Value);
                    s.Slice = Expr(s.Slice);
                    return FoldSubscr(s);
                case GeneratedStarred s:
                    s.Value = Expr(s.Value);
                    break;
                case GeneratedSlice s:
                    s.Lower = Opt(s.Lower);
                    s.Upper = Opt(s.Upper);
                    s.Step = Opt(s.Step);
                    break;
                case GeneratedList l:
                    Exprs(l.Elts);
                    break;
                case GeneratedTuple t:
                    Exprs(t.Elts);
                    return FoldTuple(t);
                case GeneratedName { Ctx: GeneratedLoad, Id.Value: "__debug__" }:
                    return MakeConst(node, _optimize == 0 ? PyBool.True : PyBool.False);
                case GeneratedNamedExpr n:
                    n.Value = Expr(n.Value);
                    break;
            }
            return node;
        }

        private void Stmts(GeneratedSeq? seq)
        {
            for (int i = 0; i < Len(seq); i++)
                Stmt((GeneratedStmt)seq![i]);
        }

        // astfold_body
        private void Body(GeneratedSeq? stmts)
        {
            bool docstring = PyFuture.GetDocString(stmts) != null;
            Stmts(stmts);
            if (!docstring && PyFuture.GetDocString(stmts) != null)
            {
                var st = (GeneratedExprStmt)stmts![0];
                var values = new GeneratedExprSeq();
                values.Add(st.Value);
                st.Value = Located(new GeneratedJoinedStr { Values = values }, st);
            }
        }

        private void TypeParams(GeneratedSeq? seq)
        {
            for (int i = 0; i < Len(seq); i++)
            {
                if (seq![i] is GeneratedTypeVar v)
                    v.Bound = Opt(v.Bound);
            }
        }

        private void Handlers(GeneratedSeq? seq)
        {
            for (int i = 0; i < Len(seq); i++)
            {
                var handler = (GeneratedExceptHandler)seq![i];
                handler.Type = Opt(handler.Type);
                Stmts(handler.Body);
            }
        }

        private void Withitems(GeneratedSeq? seq)
        {
            for (int i = 0; i < Len(seq); i++)
            {
                var item = (GeneratedWithitem)seq![i];
                item.ContextExpr = Expr(item.ContextExpr);
                item.OptionalVars = Opt(item.OptionalVars);
            }
        }

        // astfold_stmt
        private void Stmt(GeneratedStmt node)
        {
            System.Runtime.CompilerServices.RuntimeHelpers.EnsureSufficientExecutionStack();
            switch (node)
            {
                case GeneratedFunctionDef f:
                    TypeParams(f.TypeParams);
                    Arguments(f.Args);
                    Body(f.Body);
                    Exprs(f.DecoratorList);
                    if (!_futureAnnotations)
                        f.Returns = Opt(f.Returns);
                    break;
                case GeneratedAsyncFunctionDef f:
                    TypeParams(f.TypeParams);
                    Arguments(f.Args);
                    Body(f.Body);
                    Exprs(f.DecoratorList);
                    if (!_futureAnnotations)
                        f.Returns = Opt(f.Returns);
                    break;
                case GeneratedClassDef c:
                    TypeParams(c.TypeParams);
                    Exprs(c.Bases);
                    Keywords(c.Keywords);
                    Body(c.Body);
                    Exprs(c.DecoratorList);
                    break;
                case GeneratedReturn r:
                    r.Value = Opt(r.Value);
                    break;
                case GeneratedDelete d:
                    Exprs(d.Targets);
                    break;
                case GeneratedAssign a:
                    Exprs(a.Targets);
                    a.Value = Expr(a.Value);
                    break;
                case GeneratedAugAssign a:
                    a.Target = Expr(a.Target);
                    a.Value = Expr(a.Value);
                    break;
                case GeneratedAnnAssign a:
                    a.Target = Expr(a.Target);
                    if (!_futureAnnotations)
                        a.Annotation = Expr(a.Annotation);
                    a.Value = Opt(a.Value);
                    break;
                case GeneratedTypeAlias t:
                    t.Name = Expr(t.Name);
                    TypeParams(t.TypeParams);
                    t.Value = Expr(t.Value);
                    break;
                case GeneratedFor f:
                    f.Target = Expr(f.Target);
                    f.Iter = Expr(f.Iter);
                    Stmts(f.Body);
                    Stmts(f.Orelse);
                    f.Iter = FoldIter(f.Iter);
                    break;
                case GeneratedAsyncFor f:
                    f.Target = Expr(f.Target);
                    f.Iter = Expr(f.Iter);
                    Stmts(f.Body);
                    Stmts(f.Orelse);
                    break;
                case GeneratedWhile w:
                    w.Test = Expr(w.Test);
                    Stmts(w.Body);
                    Stmts(w.Orelse);
                    break;
                case GeneratedIf i:
                    i.Test = Expr(i.Test);
                    Stmts(i.Body);
                    Stmts(i.Orelse);
                    break;
                case GeneratedWith w:
                    Withitems(w.Items);
                    Stmts(w.Body);
                    break;
                case GeneratedAsyncWith w:
                    Withitems(w.Items);
                    Stmts(w.Body);
                    break;
                case GeneratedRaise r:
                    r.Exc = Opt(r.Exc);
                    r.Cause = Opt(r.Cause);
                    break;
                case GeneratedTry t:
                    Stmts(t.Body);
                    Handlers(t.Handlers);
                    Stmts(t.Orelse);
                    Stmts(t.Finalbody);
                    break;
                case GeneratedTryStar t:
                    Stmts(t.Body);
                    Handlers(t.Handlers);
                    Stmts(t.Orelse);
                    Stmts(t.Finalbody);
                    break;
                case GeneratedAssert a:
                    a.Test = Expr(a.Test);
                    a.Msg = Opt(a.Msg);
                    break;
                case GeneratedExprStmt e:
                    e.Value = Expr(e.Value);
                    break;
                case GeneratedMatch m:
                    m.Subject = Expr(m.Subject);
                    for (int i = 0; i < Len(m.Cases); i++)
                    {
                        var matchCase = (GeneratedMatchCase)m.Cases[i];
                        Pattern(matchCase.Pattern);
                        matchCase.Guard = Opt(matchCase.Guard);
                        Stmts(matchCase.Body);
                    }
                    break;
            }
        }

        private void Patterns(GeneratedSeq? seq)
        {
            for (int i = 0; i < Len(seq); i++)
                Pattern((GeneratedPattern)seq![i]);
        }

        // astfold_pattern
        private void Pattern(GeneratedPattern node)
        {
            switch (node)
            {
                case GeneratedMatchValue v:
                    v.Value = Expr(v.Value);
                    break;
                case GeneratedMatchSequence s:
                    Patterns(s.Patterns);
                    break;
                case GeneratedMatchMapping m:
                    Exprs(m.Keys);
                    Patterns(m.Patterns);
                    break;
                case GeneratedMatchClass c:
                    c.Cls = Expr(c.Cls);
                    Patterns(c.Patterns);
                    Patterns(c.KwdPatterns);
                    break;
                case GeneratedMatchAs a:
                    if (a.Pattern != null)
                        Pattern(a.Pattern);
                    break;
                case GeneratedMatchOr o:
                    Patterns(o.Patterns);
                    break;
            }
        }

        // _PyAST_Optimize
        public GeneratedMod Optimize(GeneratedMod mod)
        {
            switch (mod)
            {
                case GeneratedModule module:
                    Body(module.Body);
                    break;
                case GeneratedInteractive interactive:
                    Stmts(interactive.Body);
                    break;
                case GeneratedExpression expression:
                    expression.Body = Expr(expression.Body);
                    break;
            }
            return mod;
        }
    }
}
