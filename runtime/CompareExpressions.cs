using System;
using System.Collections.Generic;
using System.Linq;
using SharpPy.Generated;

namespace SharpPy
{
    /// <summary>
    /// Single comparison expression (a < b)
    /// CPython 3.12: Uses GeneratedCmpop from ASDL (not hardcoded)
    /// </summary>
    public class CompareExpression : Expression
    {
        public override string NodeType => "Compare";
        public Expression Left { get; }
        public GeneratedCmpop Op { get; }
        public Expression Right { get; }

        public CompareExpression(Expression left, GeneratedCmpop op, Expression right)
        {
            Left = left;
            Op = op;
            Right = right;
        }

        public override PyObject Evaluate(PyScope scope)
        {
            var left = Left.Evaluate(scope);
            var right = Right.Evaluate(scope);

            // CPython 3.12: Pattern match on GeneratedCmpop types from ASDL
            return Op switch
            {
                // CPython: PyObject_RichCompare(left, right, oparg>>4)
                GeneratedEq _    => left.RichCompare(right, PyObject.CompareOp.EQ),
                GeneratedNotEq _ => left.RichCompare(right, PyObject.CompareOp.NE),
                GeneratedLt _    => left.RichCompare(right, PyObject.CompareOp.LT),
                GeneratedLtE _   => left.RichCompare(right, PyObject.CompareOp.LE),
                GeneratedGt _    => left.RichCompare(right, PyObject.CompareOp.GT),
                GeneratedGtE _   => left.RichCompare(right, PyObject.CompareOp.GE),

                // CPython: IS_OP - int res = Py_Is(left, right) ^ oparg
                GeneratedIs _    => ReferenceEquals(left, right) ? PyBool.True : PyBool.False,
                GeneratedIsNot _ => ReferenceEquals(left, right) ? PyBool.False : PyBool.True,

                // CPython: CONTAINS_OP - int res = PySequence_Contains(right, left)
                // Note: PySequence_Contains(right, left) - 순서 반대!
                GeneratedIn _    => right.Contains(left).ToBool() ? PyBool.True : PyBool.False,
                GeneratedNotIn _ => right.Contains(left).ToBool() ? PyBool.False : PyBool.True,

                _ => throw new NotImplementedException($"Compare operator {Op.GetType().Name} not implemented")
            };
        }

        public override string ToString() => $"({Left} {Op} {Right})";
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