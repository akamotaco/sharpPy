using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    public class BinaryOpExpression : Expression
    {
        public override string NodeType => "BinOp";
        public Expression Left { get; }
        public string Operator { get; }
        public Expression Right { get; }

        public BinaryOpExpression(Expression left, string op, Expression right)
        {
            Left = left;
            Operator = op;
            Right = right;
        }

        public override PyObject Evaluate(PyScope scope)
        {
            var left = Left.Evaluate(scope);
            var right = Right.Evaluate(scope);

            return Operator switch
            {
                "+" => left.Add(right),
                "Add" => left.Add(right),
                "-" => left.Subtract(right),
                "Sub" => left.Subtract(right),
                "*" => left.Multiply(right),
                "Mult" => left.Multiply(right),
                "/" => left.Divide(right),
                "Div" => left.Divide(right),
                "//" => left.FloorDivide(right),
                "FloorDiv" => left.FloorDivide(right),
                "%" => left.Modulo(right),
                "Mod" => left.Modulo(right),
                "**" => left.Power(right),
                "Pow" => left.Power(right),
                "&" => left.BitwiseAnd(right),
                "BitAnd" => left.BitwiseAnd(right),
                "|" => left.BitwiseOr(right),
                "BitOr" => left.BitwiseOr(right),
                "^" => left.BitwiseXor(right),
                "BitXor" => left.BitwiseXor(right),
                "<<" => left.LeftShift(right),
                "LShift" => left.LeftShift(right),
                ">>" => left.RightShift(right),
                "RShift" => left.RightShift(right),
                // Comparison operators - NEW!
                "==" => left.RichCompare(right, PyObject.CompareOp.EQ),
                "!=" => left.RichCompare(right, PyObject.CompareOp.NE),
                "<" => left.RichCompare(right, PyObject.CompareOp.LT),
                "<=" => left.RichCompare(right, PyObject.CompareOp.LE),
                ">" => left.RichCompare(right, PyObject.CompareOp.GT),
                ">=" => left.RichCompare(right, PyObject.CompareOp.GE),
                // "@"   => left.MatrixMultiply(right),  // Not implemented yet
                // "MatMult" => left.MatrixMultiply(right),  // Not implemented yet
                _ => throw new NotImplementedException($"Binary operator {Operator} not implemented")
            };
        }

        public override string ToString() => $"({Left} {Operator} {Right})";
    }
}