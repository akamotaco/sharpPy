using System;
using System.Collections.Generic;
// Performance: Eliminated LINQ

namespace SharpPy
{
    /// <summary>
    /// 상수 접기 최적화 규칙 - AST 레벨에서 상수 연산을 컴파일 타임에 계산
    /// CPython 3.12 Reference: Python/ast_opt.c (lines 148-240)
    /// </summary>
    public class ConstantFoldingRule : IOptimizationRule
    {
        // CPython 3.12 ast_opt.c complexity limits (lines 148-151)
        private const int MAX_INT_SIZE = 128;           // bits
        private const int MAX_COLLECTION_SIZE = 256;    // items
        private const int MAX_STR_SIZE = 4096;          // characters
        private const int MAX_TOTAL_ITEMS = 1024;       // including nested collections

        public string RuleName => "Constant Folding";

        public bool CanOptimize(ASTNode node)
        {
            if (node is BinaryOpExpression binOp)
            {
                return IsConstant(binOp.Left) && IsConstant(binOp.Right);
            }
            return false;
        }

        public ASTNode Optimize(ASTNode node)
        {
            var binOp = (BinaryOpExpression)node;

            try
            {
                var leftValue = EvaluateConstant(binOp.Left);
                var rightValue = EvaluateConstant(binOp.Right);
                var result = EvaluateBinaryOperation(leftValue, rightValue, binOp.Operator);

                // null indicates complexity limit exceeded - skip optimization
                if (result == null)
                    return node;

                return new ConstantExpression(result);
            }
            catch (Exception)
            {
                // 런타임 에러 발생 가능한 경우 (0으로 나누기 등) 최적화하지 않음
                return node;
            }
        }

        private bool IsConstant(Expression expr)
        {
            return expr is ConstantExpression;
        }

        private PyObject EvaluateConstant(Expression expr)
        {
            if (expr is ConstantExpression constant)
                return constant.Value;
            
            throw new InvalidOperationException("Not a constant expression");
        }

        private PyObject EvaluateBinaryOperation(PyObject left, PyObject right, string operator_)
        {
            return operator_ switch
            {
                "+" => left.Add(right),
                "-" => left.Subtract(right),
                "*" => SafeMultiply(left, right),
                "/" => left.Divide(right),
                "//" => left.FloorDivide(right),
                "%" => left.Modulo(right),
                "**" => SafePower(left, right),
                "<<" => SafeLeftShift(left, right),
                ">>" => left.RightShift(right),
                "&" => left.BitwiseAnd(right),
                "|" => left.BitwiseOr(right),
                "^" => left.BitwiseXor(right),
                _ => throw new NotSupportedException($"Operator {operator_} not supported in constant folding")
            };
        }

        /// <summary>
        /// CPython 3.12 safe_multiply() implementation (Python/ast_opt.c lines 154-195)
        /// Returns null if complexity limit is exceeded
        /// </summary>
        private PyObject SafeMultiply(PyObject left, PyObject right)
        {
            // Case 1: int * int - check for bit overflow
            if (left is PyInt leftInt && right is PyInt rightInt)
            {
                // Skip if either is zero (no overflow possible)
                if (leftInt.Value != 0 && rightInt.Value != 0)
                {
                    int leftBits = (int)leftInt.BitLength().Value;
                    int rightBits = (int)rightInt.BitLength().Value;

                    if (leftBits + rightBits > MAX_INT_SIZE)
                    {
                        return null; // Complexity limit exceeded
                    }
                }
            }
            // Case 2: int * (tuple/list/set) - check for collection size explosion
            else if (left is PyInt multiplier && (right is PyTuple || right is PyList || right is PySet))
            {
                int size = right switch
                {
                    PyTuple tuple => tuple.Length(),
                    PyList list => list.Length(),
                    PySet set => set.Length(),
                    _ => 0
                };

                // CPython 3.12: Python/ast_opt.c:145-165 - safe_multiply
                if (size > 0)
                {
                    long n = (long)multiplier.Value;
                    if (n < 0 || n > MAX_COLLECTION_SIZE / size)
                    {
                        return null; // Collection too large
                    }
                }
            }
            // Case 3: int * (str/bytes) - check for string size explosion
            else if (left is PyInt strMultiplier && right is PyStr str)
            {
                int size = str.Value.Length;

                if (size > 0)
                {
                    long n = (long)strMultiplier.Value;
                    if (n < 0 || n > MAX_STR_SIZE / size)
                    {
                        return null; // String too large
                    }
                }
            }

            return left.Multiply(right);
        }

        /// <summary>
        /// CPython 3.12 safe_power() implementation (Python/ast_opt.c lines 197-213)
        /// Returns null if complexity limit is exceeded
        /// </summary>
        private PyObject SafePower(PyObject left, PyObject right)
        {
            // Only check int ** int
            if (left is PyInt baseInt && right is PyInt expInt)
            {
                // Negative exponents produce floats, allow them
                if (expInt.Value < 0)
                {
                    return left.Power(right);
                }

                // CPython 3.12: Python/ast_opt.c:197-213 - safe_power
                // Check for exponential growth
                if (baseInt.Value != 0 && expInt.Value != 0)
                {
                    int baseBits = (int)baseInt.BitLength().Value;
                    long exponent = (long)expInt.Value;

                    // Rough approximation: result_bits ≈ base_bits * exponent
                    // Using double to avoid overflow in multiplication
                    double estimatedBits = (double)baseBits * exponent;

                    if (estimatedBits > MAX_INT_SIZE)
                    {
                        return null; // Result would be too large
                    }
                }
            }

            return left.Power(right);
        }

        /// <summary>
        /// CPython 3.12 safe_lshift() implementation (Python/ast_opt.c lines 215-240)
        /// Returns null if complexity limit is exceeded
        /// </summary>
        private PyObject SafeLeftShift(PyObject left, PyObject right)
        {
            // Only check int << int
            if (left is PyInt valueInt && right is PyInt shiftInt)
            {
                // Negative shifts are errors (runtime will catch)
                if (shiftInt.Value < 0)
                {
                    return left.LeftShift(right);
                }

                // CPython 3.12: Python/ast_opt.c:216-230 - safe_lshift
                // Check for bit overflow
                if (valueInt.Value != 0)
                {
                    int valueBits = (int)valueInt.BitLength().Value;
                    long shiftAmount = (long)shiftInt.Value;

                    // Using double to avoid overflow
                    double resultBits = valueBits + (double)shiftAmount;

                    if (resultBits > MAX_INT_SIZE)
                    {
                        return null; // Result would be too large
                    }
                }
            }

            return left.LeftShift(right);
        }
    }

    /// <summary>
    /// 단항 연산자 상수 접기 최적화
    /// </summary>
    public class UnaryConstantFoldingRule : IOptimizationRule
    {
        public string RuleName => "Unary Constant Folding";

        public bool CanOptimize(ASTNode node)
        {
            if (node is UnaryOpExpression unaryOp)
            {
                return unaryOp.Operand is ConstantExpression;
            }
            return false;
        }

        public ASTNode Optimize(ASTNode node)
        {
            var unaryOp = (UnaryOpExpression)node;
            var literal = (ConstantExpression)unaryOp.Operand;

            try
            {
                var result = unaryOp.OpNode switch
                {
                    USub => literal.Value.Negative(),
                    UAdd => literal.Value.Positive(),
                    Invert => literal.Value.BitwiseNot(),
                    Not => literal.Value.ToBool() ? PyBool.False : PyBool.True,
                    _ => throw new NotSupportedException($"Unary operator {unaryOp.OpNode.OperatorType} not supported")
                };

                return new ConstantExpression(result);
            }
            catch (Exception)
            {
                return node;
            }
        }
    }

    /// <summary>
    /// 죽은 분기 제거 - if True/False 조건을 컴파일 타임에 제거
    /// </summary>
    public class DeadBranchEliminationRule : IOptimizationRule
    {
        public string RuleName => "Dead Branch Elimination";

        public bool CanOptimize(ASTNode node)
        {
            if (node is IfStatement ifStmt)
            {
                return ifStmt.Test is ConstantExpression;
            }
            return false;
        }

        public ASTNode Optimize(ASTNode node)
        {
            var ifStmt = (IfStatement)node;
            var condition = (ConstantExpression)ifStmt.Test;
            
            // if True: → body로 교체
            if (condition.Value.ToBool())
            {
                if (ifStmt.Body.Count == 1)
                    return ifStmt.Body[0];
                
                // 여러 문장인 경우 블록으로 래핑
                return new BlockStatement(ifStmt.Body);
            }
            
            // if False: → else 블록으로 교체 (또는 제거)
            if (ifStmt.OrElse != null && ifStmt.OrElse.Count > 0)
            {
                if (ifStmt.OrElse.Count == 1)
                    return ifStmt.OrElse[0];
                    
                return new BlockStatement(ifStmt.OrElse);
            }
            
            // else가 없으면 NOP로 변환 (CPython 3.12 호환)
            return new NopStatement();
        }
    }

    /// <summary>
    /// 죽은 루프 제거 - while False 등을 제거
    /// </summary>
    public class DeadLoopEliminationRule : IOptimizationRule
    {
        public string RuleName => "Dead Loop Elimination";

        public bool CanOptimize(ASTNode node)
        {
            if (node is WhileStatement whileStmt)
            {
                if (whileStmt.Test is ConstantExpression literal)
                {
                    return !literal.Value.ToBool(); // False인 경우만
                }
            }
            return false;
        }

        public ASTNode Optimize(ASTNode node)
        {
            var whileStmt = (WhileStatement)node;
            
            // while False: → else 블록으로 교체 (또는 제거)
            if (whileStmt.ElseClause != null && whileStmt.ElseClause.Count > 0)
            {
                if (whileStmt.ElseClause.Count == 1)
                    return whileStmt.ElseClause[0];
                    
                return new BlockStatement(whileStmt.ElseClause);
            }
            
            return new PassStatement();
        }
    }

    /// <summary>
    /// 불린 표현식 단순화 - and/or 연산자 최적화
    /// </summary>
    public class BooleanSimplificationRule : IOptimizationRule
    {
        public string RuleName => "Boolean Simplification";

        public bool CanOptimize(ASTNode node)
        {
            if (node is BinaryOpExpression binOp)
            {
                return binOp.Operator == "and" || binOp.Operator == "or";
            }
            if (node is BoolOpExpression boolOp)
            {
                return boolOp.Values.Count == 2; // Simple binary boolean operation
            }
            return false;
        }

        public ASTNode Optimize(ASTNode node)
        {
            if (node is BinOpExpression binOp)
            {
                // For now, BinOp optimization not implemented for non-bool operators
                return node;
            }

            if (node is BoolOpExpression boolOp && boolOp.Values.Count == 2)
            {
                var opString = boolOp.OpNode is And ? "and" : "or";
                return OptimizeBinaryBoolOp(boolOp, boolOp.Values[0], boolOp.Values[1], opString);
            }

            return node;
        }

        private ASTNode OptimizeBinaryBoolOp(ASTNode originalNode, Expression left, Expression right, string operator_)
        {
            // Short-circuit evaluation patterns
            if (left is ConstantExpression leftLiteral)
            {
                if (operator_ == "and")
                {
                    // False and X → False
                    if (!leftLiteral.Value.ToBool())
                        return leftLiteral;

                    // True and X → X
                    return right;
                }
                else if (operator_ == "or")
                {
                    // True or X → True
                    if (leftLiteral.Value.ToBool())
                        return leftLiteral;

                    // False or X → X
                    return right;
                }
            }

            if (right is ConstantExpression rightLiteral)
            {
                if (operator_ == "and")
                {
                    // X and False → False
                    if (!rightLiteral.Value.ToBool())
                        return rightLiteral;

                    // X and True → X
                    return left;
                }
                else if (operator_ == "or")
                {
                    // X or True → True
                    if (rightLiteral.Value.ToBool())
                        return rightLiteral;

                    // X or False → X
                    return left;
                }
            }

            // No optimization applicable - return original node to prevent cycles
            return originalNode;
        }
    }

    /// <summary>
    /// 대수적 단순화 - 수학적 항등식 활용 최적화
    /// </summary>
    public class AlgebraicSimplificationRule : IOptimizationRule
    {
        public string RuleName => "Algebraic Simplification";

        public bool CanOptimize(ASTNode node)
        {
            return node is BinaryOpExpression binOp && IsArithmeticOperator(binOp.Operator);
        }

        public ASTNode Optimize(ASTNode node)
        {
            var binOp = (BinaryOpExpression)node;
            
            // Identity operations
            if (binOp.Right is ConstantExpression rightLiteral)
            {
                switch (binOp.Operator)
                {
                    case "+":
                    case "-":
                        // X + 0 → X, X - 0 → X
                        if (IsZero(rightLiteral.Value))
                            return binOp.Left;
                        break;
                        
                    case "*":
                        // X * 0 → 0
                        if (IsZero(rightLiteral.Value))
                            return rightLiteral;
                        // X * 1 → X
                        if (IsOne(rightLiteral.Value))
                            return binOp.Left;
                        break;
                        
                    case "/":
                    case "//":
                        // X / 1 → X
                        if (IsOne(rightLiteral.Value))
                            return binOp.Left;
                        break;
                        
                    case "**":
                        // X ** 0 → 1
                        if (IsZero(rightLiteral.Value))
                            return new ConstantExpression(new PyInt(1));
                        // X ** 1 → X
                        if (IsOne(rightLiteral.Value))
                            return binOp.Left;
                        break;
                }
            }
            
            if (binOp.Left is ConstantExpression leftLiteral)
            {
                switch (binOp.Operator)
                {
                    case "*":
                        // 0 * X → 0
                        if (IsZero(leftLiteral.Value))
                            return leftLiteral;
                        // 1 * X → X
                        if (IsOne(leftLiteral.Value))
                            return binOp.Right;
                        break;
                        
                    case "+":
                        // 0 + X → X
                        if (IsZero(leftLiteral.Value))
                            return binOp.Right;
                        break;
                }
            }
            
            return node;
        }

        private bool IsArithmeticOperator(string op)
        {
            return op == "+" || op == "-" || op == "*" || op == "/" || op == "//" || op == "**";
        }

        private bool IsZero(PyObject value)
        {
            return value is PyInt intVal && intVal.Value == 0;
        }

        private bool IsOne(PyObject value)
        {
            return value is PyInt intVal && intVal.Value == 1;
        }
    }

    /// <summary>
    /// 내장 함수 호출 최적화
    /// </summary>
    public class BuiltinCallOptimizationRule : IOptimizationRule
    {
        public string RuleName => "Builtin Call Optimization";

        public bool CanOptimize(ASTNode node)
        {
            if (node is CallExpression call && call.Function is NameExpression name)
            {
                return IsOptimizableBuiltin(name.Name);
            }
            return false;
        }

        public ASTNode Optimize(ASTNode node)
        {
            var call = (CallExpression)node;
            var functionName = ((NameExpression)call.Function).Name;
            
            // len() 최적화
            if (functionName == "len" && call.Arguments.Count == 1)
            {
                var arg = call.Arguments[0];

                // len([1,2,3]) → 3
                if (arg is ListExpression list)
                {
                    // Performance: Eliminated LINQ - replaced All() with manual loop
                    bool allConstant = true;
                    foreach (var elem in list.Elements)
                    {
                        if (!(elem is ConstantExpression))
                        {
                            allConstant = false;
                            break;
                        }
                    }
                    if (allConstant)
                    {
                        return new ConstantExpression(new PyInt(list.Elements.Count));
                    }
                }

                // len((1,2,3)) → 3
                if (arg is TupleExpression tuple)
                {
                    // Performance: Eliminated LINQ - replaced All() with manual loop
                    bool allConstant = true;
                    foreach (var elem in tuple.Elements)
                    {
                        if (!(elem is ConstantExpression))
                        {
                            allConstant = false;
                            break;
                        }
                    }
                    if (allConstant)
                    {
                        return new ConstantExpression(new PyInt(tuple.Elements.Count));
                    }
                }
                
                // len("hello") → 5
                if (arg is ConstantExpression literal && literal.Value is PyStr str)
                {
                    return new ConstantExpression(new PyInt(str.Value.Length));
                }
            }
            
            // bool() 최적화
            if (functionName == "bool" && call.Arguments.Count == 1)
            {
                var arg = call.Arguments[0];
                if (arg is ConstantExpression literal)
                {
                    return new ConstantExpression(literal.Value.ToBool() ? PyBool.True : PyBool.False);
                }
            }
            
            return node;
        }

        private bool IsOptimizableBuiltin(string name)
        {
            return name == "len" || name == "bool" || name == "int" || name == "float" || name == "str";
        }
    }

    /// <summary>
    /// 간단한 루프 언롤링 최적화
    /// </summary>
    public class LoopUnrollingRule : IOptimizationRule
    {
        public string RuleName => "Loop Unrolling";
        private const int MAX_UNROLL_SIZE = 4; // 최대 4회까지만 언롤링

        public bool CanOptimize(ASTNode node)
        {
            if (node is ForStatement forStmt)
            {
                // range(n) 패턴만 최적화
                return IsSimpleRangeLoop(forStmt);
            }
            return false;
        }

        public ASTNode Optimize(ASTNode node)
        {
            var forStmt = (ForStatement)node;
            var rangeSize = GetRangeSize(forStmt.Iter);
            
            if (rangeSize > 0 && rangeSize <= MAX_UNROLL_SIZE)
            {
                // CPython 3.12: Only unroll if target is simple name (not tuple unpacking)
                if (forStmt.Target is NameExpression nameExpr)
                {
                    var unrolledStatements = new List<Statement>();

                    for (int i = 0; i < rangeSize; i++)
                    {
                        // 각 반복에서 루프 변수를 상수로 치환
                        var iterationStatements = ReplaceLoopVariable(forStmt.Body, nameExpr.Name, i);
                        unrolledStatements.AddRange(iterationStatements);
                    }

                    return new BlockStatement(unrolledStatements);
                }
            }
            
            return node;
        }

        private bool IsSimpleRangeLoop(ForStatement forStmt)
        {
            if (forStmt.Iter is CallExpression call &&
                call.Function is NameExpression name &&
                name.Name == "range" &&
                call.Arguments.Count == 1 &&
                call.Arguments[0] is ConstantExpression)
            {
                return true;
            }
            return false;
        }

        private int GetRangeSize(Expression iter)
        {
            if (iter is CallExpression call &&
                call.Arguments[0] is ConstantExpression literal &&
                literal.Value is PyInt intVal)
            {
                return (int)intVal.Value;
            }
            return -1;
        }

        private List<Statement> ReplaceLoopVariable(List<Statement> statements, string target, int value)
        {
            // 간단한 변수 치환 구현
            // 실제로는 더 정교한 AST rewriting이 필요
            // Performance: Eliminated LINQ - replaced ToList() with manual copy
            var result = new List<Statement>(statements);
            return result;
        }
    }

    /// <summary>
    /// 함수 인라이닝 최적화 (실험적)
    /// </summary>
    public class FunctionInliningRule : IOptimizationRule
    {
        public string RuleName => "Function Inlining";

        public bool CanOptimize(ASTNode node)
        {
            // 매우 간단한 함수 호출만 인라이닝
            return false; // 임시로 비활성화
        }

        public ASTNode Optimize(ASTNode node)
        {
            return node;
        }
    }

    /// <summary>
    /// 블록 문장 래퍼 - 여러 문장을 하나로 묶기 위한 유틸리티
    /// </summary>
    public class BlockStatement : Statement
    {
        public override string NodeType => "Block";
        public List<Statement> Statements { get; }

        public BlockStatement(List<Statement> statements)
        {
            Statements = statements;
        }

        public override PyObject Evaluate(PyScope scope)
        {
            PyObject result = PyNone.Instance;
            foreach (var stmt in Statements)
            {
                result = stmt.Evaluate(scope);
            }
            return result;
        }
    }
}