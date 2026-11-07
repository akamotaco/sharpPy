// Python/ast_unparse.c of CPython v3.12.15: an annotation as the text "from __future__ import annotations" stores.
using System;
using System.Text;
using SharpPy.Generated;

namespace SharpPy
{
    public static class AstUnparse
    {
        private const int PR_TUPLE = 0, PR_TEST = 1, PR_OR = 2, PR_AND = 3, PR_NOT = 4, PR_CMP = 5, PR_EXPR = 6,
            PR_BOR = PR_EXPR, PR_BXOR = 7, PR_BAND = 8, PR_SHIFT = 9, PR_ARITH = 10, PR_TERM = 11, PR_FACTOR = 12,
            PR_POWER = 13, PR_AWAIT = 14, PR_ATOM = 15;

        // _PyAST_ExprAsUnicode
        public static string ExprAsUnicode(GeneratedExpr e) => AsUnicode(e, PR_TEST);

        private static string AsUnicode(GeneratedExpr e, int level)
        {
            var writer = new StringBuilder();
            Expr(writer, e, level);
            return writer.ToString();
        }

        private static int Len(GeneratedSeq? seq) => seq?.Count ?? 0;

        private static string Repr(PyObject obj)
        {
            string repr = obj.ToRepr().Value;
            if ((obj is PyFloat real && double.IsInfinity(real.Value)) || obj is PyComplex)
                repr = repr.Replace("inf", "1e309");
            return repr;
        }

        private static void Boolop(StringBuilder w, GeneratedBoolOp e, int level)
        {
            string op = e.Op is GeneratedAnd ? " and " : " or ";
            int pr = e.Op is GeneratedAnd ? PR_AND : PR_OR;
            if (level > pr) w.Append('(');
            for (int i = 0; i < Len(e.Values); i++)
            {
                if (i > 0) w.Append(op);
                Expr(w, (GeneratedExpr)e.Values[i], pr + 1);
            }
            if (level > pr) w.Append(')');
        }

        private static void Binop(StringBuilder w, GeneratedBinOp e, int level)
        {
            bool rassoc = false;
            string op;
            int pr;
            switch (e.Op)
            {
                case GeneratedAdd: op = " + "; pr = PR_ARITH; break;
                case GeneratedSub: op = " - "; pr = PR_ARITH; break;
                case GeneratedMult: op = " * "; pr = PR_TERM; break;
                case GeneratedMatMult: op = " @ "; pr = PR_TERM; break;
                case GeneratedDiv: op = " / "; pr = PR_TERM; break;
                case GeneratedMod_: op = " % "; pr = PR_TERM; break;
                case GeneratedLShift: op = " << "; pr = PR_SHIFT; break;
                case GeneratedRShift: op = " >> "; pr = PR_SHIFT; break;
                case GeneratedBitOr: op = " | "; pr = PR_BOR; break;
                case GeneratedBitXor: op = " ^ "; pr = PR_BXOR; break;
                case GeneratedBitAnd: op = " & "; pr = PR_BAND; break;
                case GeneratedFloorDiv: op = " // "; pr = PR_TERM; break;
                case GeneratedPow: op = " ** "; pr = PR_POWER; rassoc = true; break;
                default: throw new InvalidOperationException("unknown binary operator");
            }
            if (level > pr) w.Append('(');
            Expr(w, e.Left, pr + (rassoc ? 1 : 0));
            w.Append(op);
            Expr(w, e.Right, pr + (rassoc ? 0 : 1));
            if (level > pr) w.Append(')');
        }

        private static void Unaryop(StringBuilder w, GeneratedUnaryOp e, int level)
        {
            string op;
            int pr;
            switch (e.Op)
            {
                case GeneratedInvert: op = "~"; pr = PR_FACTOR; break;
                case GeneratedNot: op = "not "; pr = PR_NOT; break;
                case GeneratedUAdd: op = "+"; pr = PR_FACTOR; break;
                case GeneratedUSub: op = "-"; pr = PR_FACTOR; break;
                default: throw new InvalidOperationException("unknown unary operator");
            }
            if (level > pr) w.Append('(');
            w.Append(op);
            Expr(w, e.Operand, pr);
            if (level > pr) w.Append(')');
        }

        private static void Arg(StringBuilder w, GeneratedArg arg)
        {
            w.Append(arg.Arg.Value);
            if (arg.Annotation != null)
            {
                w.Append(": ");
                Expr(w, arg.Annotation, PR_TEST);
            }
        }

        private static void Args(StringBuilder w, GeneratedArguments args)
        {
            bool first = true;
            void Comma()
            {
                if (!first) w.Append(", ");
                first = false;
            }
            int posonly = Len(args.Posonlyargs);
            int argCount = Len(args.Args);
            int defaultCount = Len(args.Defaults);
            for (int i = 0; i < posonly + argCount; i++)
            {
                Comma();
                Arg(w, (GeneratedArg)(i < posonly ? args.Posonlyargs[i] : args.Args[i - posonly]));
                int di = i - posonly - argCount + defaultCount;
                if (di >= 0)
                {
                    w.Append('=');
                    Expr(w, (GeneratedExpr)args.Defaults[di], PR_TEST);
                }
                if (posonly != 0 && i + 1 == posonly)
                    w.Append(", /");
            }
            if (args.Vararg != null || Len(args.Kwonlyargs) != 0)
            {
                Comma();
                w.Append('*');
                if (args.Vararg != null)
                    Arg(w, args.Vararg);
            }
            argCount = Len(args.Kwonlyargs);
            defaultCount = Len(args.KwDefaults);
            for (int i = 0; i < argCount; i++)
            {
                Comma();
                Arg(w, (GeneratedArg)args.Kwonlyargs[i]);
                int di = i - argCount + defaultCount;
                if (di >= 0 && args.KwDefaults[di] is GeneratedExpr value)
                {
                    w.Append('=');
                    Expr(w, value, PR_TEST);
                }
            }
            if (args.Kwarg != null)
            {
                Comma();
                w.Append("**");
                Arg(w, args.Kwarg);
            }
        }

        private static void Elements(StringBuilder w, GeneratedSeq? elts)
        {
            for (int i = 0; i < Len(elts); i++)
            {
                if (i > 0) w.Append(", ");
                Expr(w, (GeneratedExpr)elts![i], PR_TEST);
            }
        }

        private static void Comprehensions(StringBuilder w, GeneratedSeq? generators)
        {
            for (int i = 0; i < Len(generators); i++)
            {
                var gen = (GeneratedComprehension)generators![i];
                w.Append(gen.IsAsync != 0 ? " async for " : " for ");
                Expr(w, gen.Target, PR_TUPLE);
                w.Append(" in ");
                Expr(w, gen.Iter, PR_TEST + 1);
                for (int j = 0; j < Len(gen.Ifs); j++)
                {
                    w.Append(" if ");
                    Expr(w, (GeneratedExpr)gen.Ifs[j], PR_TEST + 1);
                }
            }
        }

        private static void Compare(StringBuilder w, GeneratedCompare e, int level)
        {
            if (level > PR_CMP) w.Append('(');
            Expr(w, e.Left, PR_CMP + 1);
            for (int i = 0; i < Len(e.Comparators); i++)
            {
                w.Append(e.Ops[i] switch
                {
                    GeneratedEq => " == ",
                    GeneratedNotEq => " != ",
                    GeneratedLt => " < ",
                    GeneratedLtE => " <= ",
                    GeneratedGt => " > ",
                    GeneratedGtE => " >= ",
                    GeneratedIs => " is ",
                    GeneratedIsNot => " is not ",
                    GeneratedIn => " in ",
                    GeneratedNotIn => " not in ",
                    _ => throw new InvalidOperationException("unexpected comparison kind"),
                });
                Expr(w, (GeneratedExpr)e.Comparators[i], PR_CMP + 1);
            }
            if (level > PR_CMP) w.Append(')');
        }

        private static void Call(StringBuilder w, GeneratedCall e)
        {
            Expr(w, e.Func, PR_ATOM);
            int argCount = Len(e.Args);
            int kwCount = Len(e.Keywords);
            if (argCount == 1 && kwCount == 0 && e.Args[0] is GeneratedGeneratorExp genexp)
            {
                Expr(w, genexp, PR_TUPLE);
                return;
            }
            w.Append('(');
            bool first = true;
            for (int i = 0; i < argCount; i++)
            {
                if (!first) w.Append(", ");
                first = false;
                Expr(w, (GeneratedExpr)e.Args[i], PR_TEST);
            }
            for (int i = 0; i < kwCount; i++)
            {
                if (!first) w.Append(", ");
                first = false;
                var kw = (GeneratedKeyword)e.Keywords[i];
                if (kw.Arg == null)
                    w.Append("**");
                else
                    w.Append(kw.Arg.Value).Append('=');
                Expr(w, kw.Value, PR_TEST);
            }
            w.Append(')');
        }

        private static void FstringElement(StringBuilder w, GeneratedExpr e, bool isFormatSpec)
        {
            switch (e)
            {
                case GeneratedConstant c:
                    w.Append(AstOptimizer.Value(c.Value).ToString()!.Replace("{", "{{").Replace("}", "}}"));
                    break;
                case GeneratedJoinedStr j:
                    Joinedstr(w, j, isFormatSpec);
                    break;
                case GeneratedFormattedValue f:
                    Formattedvalue(w, f);
                    break;
                default:
                    throw new InvalidOperationException("unknown expression kind inside f-string");
            }
        }

        private static void Joinedstr(StringBuilder w, GeneratedJoinedStr e, bool isFormatSpec)
        {
            var body = new StringBuilder();
            for (int i = 0; i < Len(e.Values); i++)
                FstringElement(body, (GeneratedExpr)e.Values[i], isFormatSpec);
            if (!isFormatSpec)
                w.Append('f').Append(PyString.From(body.ToString()).ToRepr().Value);
            else
                w.Append(body);
        }

        private static void Formattedvalue(StringBuilder w, GeneratedFormattedValue e)
        {
            string value = AsUnicode(e.Value, PR_TEST + 1);
            w.Append(value.StartsWith('{') ? "{ " : "{").Append(value);
            if (e.Conversion > 0)
                w.Append('!').Append((char)e.Conversion);
            if (e.FormatSpec != null)
            {
                w.Append(':');
                FstringElement(w, e.FormatSpec, true);
            }
            w.Append('}');
        }

        private static void Constant(StringBuilder w, PyObject constant)
        {
            if (constant is PyTuple tuple)
            {
                w.Append('(');
                for (int i = 0; i < tuple.Items.Length; i++)
                {
                    if (i > 0) w.Append(", ");
                    Constant(w, tuple.Items[i]);
                }
                if (tuple.Items.Length == 1) w.Append(',');
                w.Append(')');
                return;
            }
            w.Append(Repr(constant));
        }

        private static void Expr(StringBuilder w, GeneratedExpr e, int level)
        {
            System.Runtime.CompilerServices.RuntimeHelpers.EnsureSufficientExecutionStack();
            switch (e)
            {
                case GeneratedBoolOp b:
                    Boolop(w, b, level);
                    break;
                case GeneratedBinOp b:
                    Binop(w, b, level);
                    break;
                case GeneratedUnaryOp u:
                    Unaryop(w, u, level);
                    break;
                case GeneratedLambda l:
                    if (level > PR_TEST) w.Append('(');
                    w.Append(Len(l.Args.Args) + Len(l.Args.Posonlyargs) != 0 ? "lambda " : "lambda");
                    Args(w, l.Args);
                    w.Append(": ");
                    Expr(w, l.Body, PR_TEST);
                    if (level > PR_TEST) w.Append(')');
                    break;
                case GeneratedIfExp i:
                    if (level > PR_TEST) w.Append('(');
                    Expr(w, i.Body, PR_TEST + 1);
                    w.Append(" if ");
                    Expr(w, i.Test, PR_TEST + 1);
                    w.Append(" else ");
                    Expr(w, i.Orelse, PR_TEST);
                    if (level > PR_TEST) w.Append(')');
                    break;
                case GeneratedDict d:
                    w.Append('{');
                    for (int i = 0; i < Len(d.Values); i++)
                    {
                        if (i > 0) w.Append(", ");
                        if (d.Keys[i] is GeneratedExpr key)
                        {
                            Expr(w, key, PR_TEST);
                            w.Append(": ");
                            Expr(w, (GeneratedExpr)d.Values[i], PR_TEST);
                        }
                        else
                        {
                            w.Append("**");
                            Expr(w, (GeneratedExpr)d.Values[i], PR_EXPR);
                        }
                    }
                    w.Append('}');
                    break;
                case GeneratedSet s:
                    w.Append('{');
                    Elements(w, s.Elts);
                    w.Append('}');
                    break;
                case GeneratedGeneratorExp g:
                    w.Append('(');
                    Expr(w, g.Elt, PR_TEST);
                    Comprehensions(w, g.Generators);
                    w.Append(')');
                    break;
                case GeneratedListComp l:
                    w.Append('[');
                    Expr(w, l.Elt, PR_TEST);
                    Comprehensions(w, l.Generators);
                    w.Append(']');
                    break;
                case GeneratedSetComp s:
                    w.Append('{');
                    Expr(w, s.Elt, PR_TEST);
                    Comprehensions(w, s.Generators);
                    w.Append('}');
                    break;
                case GeneratedDictComp d:
                    w.Append('{');
                    Expr(w, d.Key, PR_TEST);
                    w.Append(": ");
                    Expr(w, d.Value, PR_TEST);
                    Comprehensions(w, d.Generators);
                    w.Append('}');
                    break;
                case GeneratedYield y:
                    if (y.Value == null)
                    {
                        w.Append("(yield)");
                        break;
                    }
                    w.Append("(yield ");
                    Expr(w, y.Value, PR_TEST);
                    w.Append(')');
                    break;
                case GeneratedYieldFrom y:
                    w.Append("(yield from ");
                    Expr(w, y.Value, PR_TEST);
                    w.Append(')');
                    break;
                case GeneratedAwait a:
                    if (level > PR_AWAIT) w.Append('(');
                    w.Append("await ");
                    Expr(w, a.Value, PR_ATOM);
                    if (level > PR_AWAIT) w.Append(')');
                    break;
                case GeneratedCompare c:
                    Compare(w, c, level);
                    break;
                case GeneratedCall c:
                    Call(w, c);
                    break;
                case GeneratedConstant c:
                    if (c.Value is GeneratedPyConstantEllipsis)
                    {
                        w.Append("...");
                        break;
                    }
                    if (c.Kind != null)
                        w.Append(c.Kind);
                    Constant(w, AstOptimizer.Value(c.Value));
                    break;
                case GeneratedJoinedStr j:
                    Joinedstr(w, j, false);
                    break;
                case GeneratedFormattedValue f:
                    Formattedvalue(w, f);
                    break;
                case GeneratedAttribute a:
                    Expr(w, a.Value, PR_ATOM);
                    w.Append(a.Value is GeneratedConstant { Value: GeneratedPyConstantInt } ? " ." : ".");
                    w.Append(a.Attr.Value);
                    break;
                case GeneratedSubscript s:
                    Expr(w, s.Value, PR_ATOM);
                    w.Append('[');
                    Expr(w, s.Slice, PR_TUPLE);
                    w.Append(']');
                    break;
                case GeneratedStarred s:
                    w.Append('*');
                    Expr(w, s.Value, PR_EXPR);
                    break;
                case GeneratedSlice s:
                    if (s.Lower != null)
                        Expr(w, s.Lower, PR_TEST);
                    w.Append(':');
                    if (s.Upper != null)
                        Expr(w, s.Upper, PR_TEST);
                    if (s.Step != null)
                    {
                        w.Append(':');
                        Expr(w, s.Step, PR_TEST);
                    }
                    break;
                case GeneratedName n:
                    w.Append(n.Id.Value);
                    break;
                case GeneratedList l:
                    w.Append('[');
                    Elements(w, l.Elts);
                    w.Append(']');
                    break;
                case GeneratedTuple t:
                    if (Len(t.Elts) == 0)
                    {
                        w.Append("()");
                        break;
                    }
                    if (level > PR_TUPLE) w.Append('(');
                    Elements(w, t.Elts);
                    if (Len(t.Elts) == 1) w.Append(',');
                    if (level > PR_TUPLE) w.Append(')');
                    break;
                case GeneratedNamedExpr n:
                    if (level > PR_TUPLE) w.Append('(');
                    Expr(w, n.Target, PR_ATOM);
                    w.Append(" := ");
                    Expr(w, n.Value, PR_ATOM);
                    if (level > PR_TUPLE) w.Append(')');
                    break;
                default:
                    throw new InvalidOperationException("unknown expression kind");
            }
        }
    }
}
