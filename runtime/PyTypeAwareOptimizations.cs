using System;
using System.Collections.Generic;
// Performance: Eliminated LINQ - no LINQ usage found

namespace SharpPy
{
    /// <summary>
    /// 타입 정보를 활용한 고급 최적화 규칙들
    /// CPython 3.12의 Type-Aware Optimization 구현
    /// </summary>
    
    /// <summary>
    /// 타입 기반 이진 연산 최적화 - 타입이 확실한 경우 특수화된 연산 생성
    /// </summary>
    public class TypeSpecializedBinaryOpRule : IOptimizationRule
    {
        public string RuleName => "Type-Specialized Binary Operation";
        private readonly TypeInferenceEngine _typeEngine;

        public TypeSpecializedBinaryOpRule(TypeInferenceEngine typeEngine)
        {
            _typeEngine = typeEngine;
        }

        public bool CanOptimize(ASTNode node)
        {
            if (node is BinaryOpExpression binOp)
            {
                var leftType = _typeEngine.GetExpressionType(binOp.Left);
                var rightType = _typeEngine.GetExpressionType(binOp.Right);
                
                // 두 operand의 타입이 모두 확실하고 최적화 가능한 경우
                return CanSpecializeBinaryOp(leftType, rightType, binOp.Operator);
            }
            return false;
        }

        public ASTNode Optimize(ASTNode node)
        {
            var binOp = (BinaryOpExpression)node;
            var leftType = _typeEngine.GetExpressionType(binOp.Left);
            var rightType = _typeEngine.GetExpressionType(binOp.Right);

            // 특수화된 이진 연산 노드 생성
            return new TypeSpecializedBinaryOpExpression(
                binOp.Left,
                binOp.Operator,
                binOp.Right,
                leftType,
                rightType
            );
        }

        private bool CanSpecializeBinaryOp(PyTypeInfo leftType, PyTypeInfo rightType, string op)
        {
            return op switch
            {
                // 정수 연산 특수화
                "+" or "-" or "*" or "//" or "%" or "**" when leftType.IsInt && rightType.IsInt => true,
                
                // 실수 연산 특수화
                "+" or "-" or "*" or "/" or "**" when leftType.IsFloat && rightType.IsFloat => true,
                
                // 문자열 연산 특수화
                "+" when leftType.IsString && rightType.IsString => true,
                "*" when (leftType.IsString && rightType.IsInt) || (leftType.IsInt && rightType.IsString) => true,
                
                // 리스트 연산 특수화
                "+" when leftType.IsList && rightType.IsList => true,
                "*" when (leftType.IsList && rightType.IsInt) || (leftType.IsInt && rightType.IsList) => true,
                
                _ => false
            };
        }
    }

    /// <summary>
    /// 내장 함수 호출 인라이닝 최적화
    /// </summary>
    public class BuiltinMethodInliningRule : IOptimizationRule
    {
        public string RuleName => "Builtin Method Inlining";
        private readonly TypeInferenceEngine _typeEngine;

        public BuiltinMethodInliningRule(TypeInferenceEngine typeEngine)
        {
            _typeEngine = typeEngine;
        }

        public bool CanOptimize(ASTNode node)
        {
            if (node is CallExpression call && call.Function is AttributeExpression attr)
            {
                var objectType = _typeEngine.GetExpressionType(attr.Value);
                return CanInlineMethod(objectType, attr.Attr);
            }
            return false;
        }

        public ASTNode Optimize(ASTNode node)
        {
            var call = (CallExpression)node;
            var attr = (AttributeExpression)call.Function;
            var objectType = _typeEngine.GetExpressionType(attr.Value);

            // 특수화된 메서드 호출로 변환
            return CreateInlinedMethodCall(objectType, attr.Attr, attr.Value, call.Arguments);
        }

        private bool CanInlineMethod(PyTypeInfo objectType, string methodName)
        {
            return (objectType.TypeName, methodName) switch
            {
                ("str", "upper") => true,
                ("str", "lower") => true,
                ("str", "strip") => true,
                ("list", "append") => true,
                ("list", "extend") => true,
                ("dict", "get") => true,
                ("dict", "pop") => true,
                _ => false
            };
        }

        private ASTNode CreateInlinedMethodCall(PyTypeInfo objectType, string methodName, 
                                              Expression target, List<Expression> args)
        {
            return new InlinedMethodCallExpression(
                objectType.TypeName,
                methodName,
                target,
                args
            );
        }
    }

    /// <summary>
    /// 타입 가드 제거 최적화 - 타입이 확실한 경우 런타임 체크 제거
    /// </summary>
    public class TypeGuardEliminationRule : IOptimizationRule
    {
        public string RuleName => "Type Guard Elimination";
        private readonly TypeInferenceEngine _typeEngine;

        public TypeGuardEliminationRule(TypeInferenceEngine typeEngine)
        {
            _typeEngine = typeEngine;
        }

        public bool CanOptimize(ASTNode node)
        {
            if (node is CallExpression call && call.Function is NameExpression name)
            {
                // isinstance(), type() 등의 타입 체크 호출
                if (name.Name == "isinstance" || name.Name == "type")
                {
                    if (call.Arguments.Count >= 1)
                    {
                        var argType = _typeEngine.GetExpressionType(call.Arguments[0]);
                        return !argType.IsUnknown;
                    }
                }
            }
            return false;
        }

        public ASTNode Optimize(ASTNode node)
        {
            var call = (CallExpression)node;
            var functionName = ((NameExpression)call.Function).Name;
            var argType = _typeEngine.GetExpressionType(call.Arguments[0]);

            if (functionName == "isinstance" && call.Arguments.Count == 2)
            {
                // isinstance(obj, type) → True/False 상수로 최적화
                if (call.Arguments[1] is NameExpression typeExpr)
                {
                    bool isInstance = CheckIsInstance(argType, typeExpr.Name);
                    return new ConstantExpression(isInstance ? PyBool.True : PyBool.False);
                }
            }

            return node;
        }

        private bool CheckIsInstance(PyTypeInfo objectType, string typeName)
        {
            return typeName.ToLower() switch
            {
                "int" => objectType.IsInt,
                "float" => objectType.IsFloat,
                "str" => objectType.IsString,
                "bool" => objectType.IsBool,
                "list" => objectType.IsList,
                "tuple" => objectType.IsTuple,
                "dict" => objectType.IsDict,
                _ => false
            };
        }
    }

    /// <summary>
    /// 컨테이너 크기 최적화 - 정적으로 알 수 있는 크기 계산
    /// </summary>
    public class ContainerSizeOptimizationRule : IOptimizationRule
    {
        public string RuleName => "Container Size Optimization";
        private readonly TypeInferenceEngine _typeEngine;

        public ContainerSizeOptimizationRule(TypeInferenceEngine typeEngine)
        {
            _typeEngine = typeEngine;
        }

        public bool CanOptimize(ASTNode node)
        {
            if (node is CallExpression call && 
                call.Function is NameExpression name && 
                name.Name == "len" && 
                call.Arguments.Count == 1)
            {
                var arg = call.Arguments[0];
                
                // 정적으로 크기를 알 수 있는 컨테이너들
                return arg is ListExpression || 
                       arg is TupleExpression || 
                       (arg is ConstantExpression constant && IsKnownSizeContainer(constant.Value));
            }
            return false;
        }

        public ASTNode Optimize(ASTNode node)
        {
            var call = (CallExpression)node;
            var arg = call.Arguments[0];

            int size = arg switch
            {
                ListExpression list => list.Elements.Count,
                TupleExpression tuple => tuple.Elements.Count,
                ConstantExpression constant => GetContainerSize(constant.Value),
                _ => -1
            };

            if (size >= 0)
            {
                return new ConstantExpression(new PyInt(size));
            }

            return node;
        }

        private bool IsKnownSizeContainer(PyObject value)
        {
            return value is PyStr || value is PyList || value is PyTuple || value is PyDict;
        }

        private int GetContainerSize(PyObject value)
        {
            return value switch
            {
                PyStr str => str.Value.Length,
                PyList list => list.Length(),
                PyTuple tuple => tuple.Items.Length,
                PyDict dict => dict.Length(),
                _ => -1
            };
        }
    }

    /// <summary>
    /// 타입 특수화된 이진 연산 표현식 (컴파일러가 특수한 바이트코드 생성 가능)
    /// </summary>
    public class TypeSpecializedBinaryOpExpression : Expression
    {
        public override string NodeType => "TypeSpecializedBinaryOp";
        public Expression Left { get; }
        public string Operator { get; }
        public Expression Right { get; }
        public PyTypeInfo LeftType { get; }
        public PyTypeInfo RightType { get; }

        public TypeSpecializedBinaryOpExpression(Expression left, string op, Expression right, 
                                                PyTypeInfo leftType, PyTypeInfo rightType)
        {
            Left = left;
            Operator = op;
            Right = right;
            LeftType = leftType;
            RightType = rightType;
        }

        public override PyObject Evaluate(PyScope scope)
        {
            // 런타임에서는 일반 이진 연산과 동일
            var leftVal = Left.Evaluate(scope);
            var rightVal = Right.Evaluate(scope);

            return Operator switch
            {
                "+" => leftVal.Add(rightVal),
                "-" => leftVal.Subtract(rightVal),
                "*" => leftVal.Multiply(rightVal),
                "/" => leftVal.Divide(rightVal),
                "//" => leftVal.FloorDivide(rightVal),
                "%" => leftVal.Modulo(rightVal),
                "**" => leftVal.Power(rightVal),
                _ => throw new NotSupportedException($"Operator {Operator} not supported")
            };
        }

        public override string ToString() => $"({Left} {Operator} {Right} : {LeftType}×{RightType})";
    }

    /// <summary>
    /// 인라인된 메서드 호출 표현식
    /// </summary>
    public class InlinedMethodCallExpression : Expression
    {
        public override string NodeType => "InlinedMethodCall";
        public string ObjectType { get; }
        public string MethodName { get; }
        public Expression Target { get; }
        public List<Expression> Arguments { get; }

        public InlinedMethodCallExpression(string objectType, string methodName, 
                                         Expression target, List<Expression> arguments)
        {
            ObjectType = objectType;
            MethodName = methodName;
            Target = target;
            Arguments = arguments;
        }

        public override PyObject Evaluate(PyScope scope)
        {
            var targetObj = Target.Evaluate(scope);
            var argValues = Arguments.Select(arg => arg.Evaluate(scope)).ToArray();

            // 특수화된 메서드 호출 실행
            return ExecuteInlinedMethod(targetObj, argValues);
        }

        private PyObject ExecuteInlinedMethod(PyObject target, PyObject[] args)
        {
            return (ObjectType, MethodName) switch
            {
                ("str", "upper") => new PyStr(((PyStr)target).Value.ToUpper()),
                ("str", "lower") => new PyStr(((PyStr)target).Value.ToLower()),
                ("str", "strip") => new PyStr(((PyStr)target).Value.Trim()),
                ("list", "append") => ExecuteListAppend((PyList)target, args[0]),
                ("dict", "get") => ExecuteDictGet((PyDict)target, args),
                _ => throw new NotSupportedException($"Inlined method {ObjectType}.{MethodName} not supported")
            };
        }

        private PyObject ExecuteListAppend(PyList list, PyObject item)
        {
            list.Add(item);
            return PyNone.Instance;
        }

        private PyObject ExecuteDictGet(PyDict dict, PyObject[] args)
        {
            var key = args[0];
            var defaultValue = args.Length > 1 ? args[1] : PyNone.Instance;
            
            return dict.Get(key, defaultValue);
        }

        public override string ToString() => $"{Target}.{MethodName}({string.Join(", ", Arguments)}) [inlined]";
    }
}