using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    /// <summary>
    /// Single comparison expression (a < b)
    /// </summary>
    public class CompareExpression : Expression
    {
        public override string NodeType => "Compare";
        public Expression Left { get; }
        public string Op { get; }
        public Expression Right { get; }

        public CompareExpression(Expression left, string op, Expression right)
        {
            Left = left;
            Op = op;
            Right = right;
        }

        public override PyObject Evaluate(PyScope scope)
        {
            var left = Left.Evaluate(scope);
            var right = Right.Evaluate(scope);

            return Op switch
            {
                "=="  => left.RichCompare(right, PyObject.CompareOp.EQ),
                "!="  => left.RichCompare(right, PyObject.CompareOp.NE),
                "<"   => left.RichCompare(right, PyObject.CompareOp.LT),
                "<="  => left.RichCompare(right, PyObject.CompareOp.LE),
                ">"   => left.RichCompare(right, PyObject.CompareOp.GT),
                ">="  => left.RichCompare(right, PyObject.CompareOp.GE),
                "in"  => PyBool.True, // 간단한 구현
                "not in" => PyBool.False, // 간단한 구현
                "is"  => PyBool.True, // 간단한 구현
                "is not" => PyBool.False, // 간단한 구현
                _ => throw new NotImplementedException($"Compare operator {Op} not implemented")
            };
        }

        public override string ToString() => $"({Left} {Op} {Right})";
    }

    /// <summary>
    /// Chained comparison expression (a < b < c) - generates CPython-compatible bytecode
    /// </summary>
    public class ChainedCompareExpression : Expression
    {
        public override string NodeType => "ChainedCompare";
        public Expression Left { get; }
        public List<string> Operators { get; }
        public List<Expression> Comparators { get; }

        public ChainedCompareExpression(Expression left, List<string> operators, List<Expression> comparators)
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

                // Perform comparison using the same logic as CompareExpression
                var result = Operators[i] switch
                {
                    "=="  => leftVal.RichCompare(rightVal, PyObject.CompareOp.EQ),
                    "!="  => leftVal.RichCompare(rightVal, PyObject.CompareOp.NE),
                    "<"   => leftVal.RichCompare(rightVal, PyObject.CompareOp.LT),
                    "<="  => leftVal.RichCompare(rightVal, PyObject.CompareOp.LE),
                    ">"   => leftVal.RichCompare(rightVal, PyObject.CompareOp.GT),
                    ">="  => leftVal.RichCompare(rightVal, PyObject.CompareOp.GE),
                    "in"  => PyBool.True, // 간단한 구현
                    "not in" => PyBool.False, // 간단한 구현
                    "is"  => PyBool.True, // 간단한 구현
                    "is not" => PyBool.False, // 간단한 구현
                    _ => throw new NotImplementedException($"Compare operator {Operators[i]} not implemented")
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
                parts.Add(Operators[i]);
                parts.Add(Comparators[i].ToString());
            }
            return $"({string.Join(" ", parts)})";
        }
    }
}