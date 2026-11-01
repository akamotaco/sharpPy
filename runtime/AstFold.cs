using System.Collections.Generic;
using System.Numerics;
using SharpPy.Generated;

namespace SharpPy
{
    /// <summary>
    /// CPython 3.12 Python/ast_opt.c: the folds of astfold_expr. The compiler asks at each
    /// expression it visits, so a node is folded once, after its children, as astfold_expr does.
    /// </summary>
    internal sealed class AstFold
    {
        private const int MaxIntSize = 128;          // bits
        private const int MaxCollectionSize = 256;   // items
        private const int MaxStrSize = 4096;         // characters
        private const int MaxTotalItems = 1024;      // including nested collections

        private readonly Dictionary<Expression, Expression> _folded = new(ReferenceEqualityComparer.Instance);

        public Expression Fold(Expression node)
        {
            if (node == null)
                return null;
            if (_folded.TryGetValue(node, out var known))
                return known;
            var result = node switch
            {
                BinOpExpression binOp => FoldBinop(binOp),
                UnaryOpExpression unaryOp => FoldUnaryop(unaryOp),
                TupleExpression tuple => MakeConstTuple(tuple, tuple.Elements) ?? tuple,
                SubscriptExpression subscript => FoldSubscr(subscript),
                CompareExpression compare => FoldCompare(compare),
                NameExpression { Name: "__debug__" } name => Located(new ConstantExpression(PyBool.True), name),
                _ => node,
            };
            _folded[node] = result;
            return result;
        }

        /// <summary>fold_iter: a list or set of constants, for "in" and for "for".</summary>
        public Expression FoldIter(Expression node)
        {
            node = Fold(node);
            if (node is ListExpression list)
            {
                if (HasStarred(list.Elements))
                    return node;
                return MakeConstTuple(list, list.Elements) ?? Located(new TupleExpression(list.Elements), list);
            }
            if (node is SetExpression set)
            {
                var values = ConstantValues(set.Elements);
                if (values != null)
                    return Located(new ConstantExpression(new PyFrozenSet(values)), set);
            }
            return node;
        }

        private static Expression Located(Expression created, Expression from)
        {
            created.LineNo = from.LineNo;
            created.ColOffset = from.ColOffset;
            created.EndLineNo = from.EndLineNo;
            created.EndColOffset = from.EndColOffset;
            return created;
        }

        private static bool HasStarred(List<Expression> elts)
        {
            foreach (var elt in elts)
                if (elt is StarredExpression)
                    return true;
            return false;
        }

        // make_const_tuple
        private PyObject[]? ConstantValues(List<Expression> elts)
        {
            var values = new PyObject[elts.Count];
            for (int i = 0; i < values.Length; i++)
            {
                if (Fold(elts[i]) is not ConstantExpression constant)
                    return null;
                values[i] = constant.Value;
            }
            return values;
        }

        private Expression? MakeConstTuple(Expression node, List<Expression> elts)
        {
            var values = ConstantValues(elts);
            return values == null ? null : Located(new ConstantExpression(new PyTuple(values)), node);
        }

        // make_const: a failure leaves the expression to raise at run time.
        private static Expression MakeConst(Expression node, System.Func<PyObject?> compute)
        {
            try
            {
                var value = compute();
                return value == null ? node : Located(new ConstantExpression(value), node);
            }
            catch (PythonException)
            {
                return node;
            }
        }

        private Expression FoldUnaryop(UnaryOpExpression node)
        {
            var arg = Fold(node.Operand);
            if (arg is ConstantExpression constant)
            {
                if (node.OpNode is Not)
                    return Located(new ConstantExpression(constant.Value.IsTrue() ? PyBool.False : PyBool.True), node);
                return MakeConst(node, () => node.OpNode.Apply(constant.Value));
            }

            // Fold not into comparison. Eq and the orderings can't be safely inverted.
            if (node.OpNode is Not && arg is CompareExpression compare && compare.Ops.Count == 1)
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
                    return Located(new CompareExpression(compare.Left, new List<GeneratedCmpop> { op }, compare.Comparators), node);
            }
            return node;
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
            if (IsInt(v) && (w is PyTuple or PyFrozenSet))
            {
                long size = SizeOf(w);
                if (size == 0)
                    return true;
                var n = IntValue(v);
                if (n.Sign < 0 || n > MaxCollectionSize / size)
                    return false;
                return n.IsZero || CheckComplexity(w, MaxTotalItems / (long)n) >= 0;
            }
            if (IsInt(v) && (w is PyString or PyBytes))
            {
                long size = SizeOf(w);
                if (size == 0)
                    return true;
                var n = IntValue(v);
                return n.Sign >= 0 && n <= MaxStrSize / size;
            }
            if (IsInt(w) && (v is PyTuple or PyFrozenSet or PyString or PyBytes))
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

        private Expression FoldBinop(BinOpExpression node)
        {
            var lhs = Fold(node.Left);
            var rhs = Fold(node.Right);
            if (lhs is not ConstantExpression left)
                return node;
            if (node.OpNode.GetOpType() == BinaryOpType.MODULO && rhs is TupleExpression arguments &&
                left.Value is PyString format && !HasStarred(arguments.Elements))
                return OptimizeFormat(node, format.Value, arguments.Elements);
            if (rhs is not ConstantExpression right)
                return node;
            var lv = left.Value;
            var rv = right.Value;
            var op = node.OpNode.GetOpType();
            bool allowed = op switch
            {
                BinaryOpType.MATRIX_MULTIPLY => false,
                BinaryOpType.MULTIPLY => SafeMultiply(lv, rv),
                BinaryOpType.MODULO => lv is not (PyString or PyBytes),
                BinaryOpType.POWER => SafePower(lv, rv),
                BinaryOpType.LSHIFT => SafeLshift(lv, rv),
                _ => true,
            };
            return allowed ? MakeConst(node, () => PyVM.Instance.ExecuteBinaryOpType(lv, rv, op)) : node;
        }

        // parse_literal: up to the next conversion, with %% as %.
        private static Expression? ParseLiteral(string format, ref int position)
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
            return new ConstantExpression(new PyString(hasPercents ? text.Replace("%%", "%") : text));
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
        private static Expression? ParseFormat(string format, ref int position, Expression arg)
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
            var formatSpec = text.Length > 0 ? new ConstantExpression(new PyString(text)) : null;
            return Located(new FormattedValueExpression(arg, spec, formatSpec), arg);
        }

        // optimize_format: "%s" % (a,) is an f-string when every unit is simple.
        private static Expression OptimizeFormat(Expression node, string format, List<Expression> elts)
        {
            int position = 0;
            int count = 0;
            var values = new List<Expression>();
            while (true)
            {
                var literal = ParseLiteral(format, ref position);
                if (literal != null)
                    values.Add(literal);
                if (position >= format.Length)
                    break;
                if (count >= elts.Count)
                    return node; // More format units than items.
                position++;
                var formatted = ParseFormat(format, ref position, elts[count]);
                count++;
                if (formatted == null)
                    return node;
                values.Add(formatted);
            }
            if (count < elts.Count)
                return node; // More items than format units.
            return Located(new JoinedStrExpression(values), node);
        }

        private Expression FoldSubscr(SubscriptExpression node)
        {
            if (node.Ctx is Store or Del)
                return node;
            var arg = Fold(node.Value);
            var idx = Fold(node.Slice);
            if (arg is ConstantExpression value && idx is ConstantExpression index)
                return MakeConst(node, () => value.Value.GetItem(index.Value));
            return node;
        }

        // fold_compare: the right operand of the last "in" or "not in".
        private Expression FoldCompare(CompareExpression node)
        {
            int last = node.Ops.Count - 1;
            if (node.Ops[last] is not (GeneratedIn or GeneratedNotIn))
                return node;
            var folded = FoldIter(node.Comparators[last]);
            if (ReferenceEquals(folded, node.Comparators[last]))
                return node;
            var comparators = new List<Expression>(node.Comparators) { [last] = folded };
            return Located(new CompareExpression(node.Left, node.Ops, comparators), node);
        }
    }
}
