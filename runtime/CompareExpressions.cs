using System;
using System.Collections.Generic;
using System.Linq;
using SharpPy.Generated;

namespace SharpPy
{
    /// <summary>
    /// Comparison expression - supports both single and chained comparisons
    /// CPython 3.12: Compare(expr left, cmpop* ops, expr* comparators)
    /// Examples: a < b  OR  a < b < c
    /// </summary>
    public class CompareExpression : Expression
    {
        public override string NodeType => "Compare";
        public Expression Left { get; }
        public List<GeneratedCmpop> Ops { get; }
        public List<Expression> Comparators { get; }

        public CompareExpression(Expression left, List<GeneratedCmpop> ops, List<Expression> comparators)
        {
            Left = left ?? throw new ArgumentNullException(nameof(left));
            Ops = ops ?? throw new ArgumentNullException(nameof(ops));
            Comparators = comparators ?? throw new ArgumentNullException(nameof(comparators));

            if (ops.Count == 0)
                throw new ArgumentException("ops cannot be empty", nameof(ops));
            if (ops.Count != comparators.Count)
                throw new ArgumentException("Number of operators must equal number of comparators");
        }

        public override PyObject Evaluate(PyScope scope)
        {
            // CPython 3.12: Evaluate chained comparison with short-circuit
            // Example: 1 < x < 10 evaluates as (1 < x) and (x < 10)

            var leftVal = Left.Evaluate(scope);

            for (int i = 0; i < Ops.Count; i++)
            {
                var rightVal = Comparators[i].Evaluate(scope);

                // CPython 3.12: Pattern match on GeneratedCmpop types from ASDL
                var result = Ops[i] switch
                {
                    // CPython: PyObject_RichCompare(left, right, oparg>>4)
                    GeneratedEq _    => leftVal.RichCompare(rightVal, PyObject.CompareOp.EQ),
                    GeneratedNotEq _ => leftVal.RichCompare(rightVal, PyObject.CompareOp.NE),
                    GeneratedLt _    => leftVal.RichCompare(rightVal, PyObject.CompareOp.LT),
                    GeneratedLtE _   => leftVal.RichCompare(rightVal, PyObject.CompareOp.LE),
                    GeneratedGt _    => leftVal.RichCompare(rightVal, PyObject.CompareOp.GT),
                    GeneratedGtE _   => leftVal.RichCompare(rightVal, PyObject.CompareOp.GE),

                    // CPython: IS_OP - int res = Py_Is(left, right) ^ oparg
                    GeneratedIs _    => ReferenceEquals(leftVal, rightVal) ? PyBool.True : PyBool.False,
                    GeneratedIsNot _ => ReferenceEquals(leftVal, rightVal) ? PyBool.False : PyBool.True,

                    // CPython: CONTAINS_OP - int res = PySequence_Contains(right, left)
                    GeneratedIn _    => rightVal.Contains(leftVal).ToBool() ? PyBool.True : PyBool.False,
                    GeneratedNotIn _ => rightVal.Contains(leftVal).ToBool() ? PyBool.False : PyBool.True,

                    _ => throw new NotImplementedException($"Compare operator {Ops[i].GetType().Name} not implemented")
                };

                // Short-circuit: if this comparison is false, return false immediately
                if (!result.ToBool())
                    return PyBool.False;

                // For next iteration, the right value becomes the left value
                leftVal = rightVal;
            }

            // All comparisons were true
            return PyBool.True;
        }

        public override string ToString()
        {
            if (Ops.Count == 1)
                return $"({Left} {Ops[0].GetType().Name.Replace("Generated", "")} {Comparators[0]})";

            var parts = new List<string> { Left.ToString() };
            for (int i = 0; i < Ops.Count; i++)
            {
                parts.Add(Ops[i].GetType().Name.Replace("Generated", ""));
                parts.Add(Comparators[i].ToString());
            }
            return $"({string.Join(" ", parts)})";
        }
    }

    /// <summary>
    /// Chained comparison expression (a < b < c) - generates CPython-compatible bytecode
    /// CPython 3.12: Uses GeneratedCmpop from ASDL (not hardcoded)
    /// </summary>
    public class ChainedCompareExpression : Expression
    {
        public override string NodeType => "ChainedCompare";
        public Expression Left { get; }
        public List<GeneratedCmpop> Operators { get; }
        public List<Expression> Comparators { get; }

        public ChainedCompareExpression(Expression left, List<GeneratedCmpop> operators, List<Expression> comparators)
        {
            Left = left;
            Operators = operators ?? throw new ArgumentNullException(nameof(operators));
            Comparators = comparators ?? throw new ArgumentNullException(nameof(comparators));

            if (operators.Count != comparators.Count)
            {
                throw new ArgumentException("Number of operators must equal number of comparators");
            }
        }

        public override PyObject Evaluate(PyScope scope)
        {
            // Evaluate chained comparison: left op1 comp1 op2 comp2 ...
            // Short-circuit evaluation: if any comparison is false, return false

            var leftVal = Left.Evaluate(scope);

            for (int i = 0; i < Operators.Count; i++)
            {
                var rightVal = Comparators[i].Evaluate(scope);

                // CPython 3.12: Pattern match on GeneratedCmpop types from ASDL
                var result = Operators[i] switch
                {
                    // CPython: PyObject_RichCompare(left, right, oparg>>4)
                    GeneratedEq _    => leftVal.RichCompare(rightVal, PyObject.CompareOp.EQ),
                    GeneratedNotEq _ => leftVal.RichCompare(rightVal, PyObject.CompareOp.NE),
                    GeneratedLt _    => leftVal.RichCompare(rightVal, PyObject.CompareOp.LT),
                    GeneratedLtE _   => leftVal.RichCompare(rightVal, PyObject.CompareOp.LE),
                    GeneratedGt _    => leftVal.RichCompare(rightVal, PyObject.CompareOp.GT),
                    GeneratedGtE _   => leftVal.RichCompare(rightVal, PyObject.CompareOp.GE),

                    // CPython: IS_OP - int res = Py_Is(left, right) ^ oparg
                    GeneratedIs _    => ReferenceEquals(leftVal, rightVal) ? PyBool.True : PyBool.False,
                    GeneratedIsNot _ => ReferenceEquals(leftVal, rightVal) ? PyBool.False : PyBool.True,

                    // CPython: CONTAINS_OP - int res = PySequence_Contains(right, left)
                    GeneratedIn _    => rightVal.Contains(leftVal).ToBool() ? PyBool.True : PyBool.False,
                    GeneratedNotIn _ => rightVal.Contains(leftVal).ToBool() ? PyBool.False : PyBool.True,

                    _ => throw new NotImplementedException($"Compare operator {Operators[i].GetType().Name} not implemented")
                };

                // Short-circuit: if this comparison is false, return false
                if (!result.ToBool())
                {
                    return PyBool.False;
                }

                // For next iteration, the right value becomes the left value
                leftVal = rightVal;
            }

            // All comparisons were true
            return PyBool.True;
        }

        public override string ToString()
        {
            var parts = new List<string> { Left.ToString() };
            for (int i = 0; i < Operators.Count; i++)
            {
                parts.Add(Operators[i].GetType().Name.Replace("Generated", ""));
                parts.Add(Comparators[i].ToString());
            }
            return $"({string.Join(" ", parts)})";
        }
    }
}