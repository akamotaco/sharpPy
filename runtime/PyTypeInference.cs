using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    /// <summary>
    /// CPython 3.12 스타일 타입 추론 엔진
    /// AST 분석을 통해 변수/표현식의 타입을 추론하고 최적화에 활용
    /// </summary>
    public class PyTypeInference
    {
        private readonly Dictionary<string, PyTypeInfo> _variableTypes;
        private readonly Dictionary<Expression, PyTypeInfo> _expressionTypes;
        private readonly Dictionary<string, PyTypeSignature> _functionSignatures;
        private readonly Stack<Dictionary<string, PyTypeInfo>> _scopeStack;
        
        public PyTypeInference()
        {
            _variableTypes = new Dictionary<string, PyTypeInfo>();
            _expressionTypes = new Dictionary<Expression, PyTypeInfo>();
            _functionSignatures = new Dictionary<string, PyTypeSignature>();
            _scopeStack = new Stack<Dictionary<string, PyTypeInfo>>();
            
            InitializeBuiltinTypes();
        }

        /// <summary>
        /// AST statements 분석하여 타입 정보 추론
        /// </summary>
        public void AnalyzeStatements(List<Statement> statements)
        {
            #if DEBUG_LOG
            Console.WriteLine("🔍 Type Inference 분석 시작");
            #endif
            
            // Create global scope
            PushScope();
            
            foreach (var stmt in statements)
            {
                AnalyzeStatement(stmt);
            }
            
            #if DEBUG_LOG
            Console.WriteLine($"✅ Type Inference 완료: {_variableTypes.Count}개 변수, {_expressionTypes.Count}개 표현식");
            #endif
        }

        /// <summary>
        /// 개별 Statement 분석
        /// </summary>
        private void AnalyzeStatement(Statement stmt)
        {
            switch (stmt)
            {
                case AssignStatement assign:
                    var valueType = AnalyzeExpression(assign.Value);
                    // CPython 3.12: Extract names from targets
                    foreach (var target in assign.Targets)
                    {
                        if (target is NameExpression nameExpr)
                            SetVariableType(nameExpr.Name, valueType);
                    }
                    break;
                    
                case ExpressionStatement exprStmt:
                    AnalyzeExpression(exprStmt.Expression);
                    break;
                    
                case IfStatement ifStmt:
                    AnalyzeExpression(ifStmt.Test);
                    foreach (var s in ifStmt.Body)
                        AnalyzeStatement(s);
                    if (ifStmt.OrElse != null)
                    {
                        foreach (var s in ifStmt.OrElse)
                            AnalyzeStatement(s);
                    }
                    break;
                    
                case ForStatement forStmt:
                    var iterType = AnalyzeExpression(forStmt.Iter);
                    var elementType = InferIterableElementType(iterType);

                    // CPython 3.12: Only set type for simple name targets
                    if (forStmt.Target is NameExpression forTarget)
                    {
                        SetVariableType(forTarget.Name, elementType);
                    }
                    // For tuple unpacking, skip type inference (would need more complex logic)

                    foreach (var s in forStmt.Body)
                        AnalyzeStatement(s);
                    break;
                    
                case WhileStatement whileStmt:
                    AnalyzeExpression(whileStmt.Test);
                    foreach (var s in whileStmt.Body)
                        AnalyzeStatement(s);
                    break;
                    
                case BlockStatement block:
                    foreach (var s in block.Statements)
                        AnalyzeStatement(s);
                    break;
            }
        }

        /// <summary>
        /// Expression 타입 분석 및 추론
        /// </summary>
        private PyTypeInfo AnalyzeExpression(Expression expr)
        {
            if (_expressionTypes.ContainsKey(expr))
                return _expressionTypes[expr];

            PyTypeInfo inferredType = expr switch
            {
                ConstantExpression constant => InferConstantType(constant.Value),
                NameExpression name => GetVariableType(name.Name),
                BinaryOpExpression binOp => InferBinaryOpType(binOp),
                UnaryOpExpression unaryOp => InferUnaryOpType(unaryOp),
                CallExpression call => InferCallType(call),
                ListExpression list => InferListType(list),
                TupleExpression tuple => InferTupleType(tuple),
                AttributeExpression attr => InferAttributeType(attr),
                CompareExpression compare => PyTypeInfo.Bool,
                _ => PyTypeInfo.Unknown
            };

            _expressionTypes[expr] = inferredType;
            return inferredType;
        }

        /// <summary>
        /// 상수 타입 추론
        /// </summary>
        private PyTypeInfo InferConstantType(PyObject value)
        {
            return value switch
            {
                PyInt _ => PyTypeInfo.Int,
                PyFloat _ => PyTypeInfo.Float,
                PyString _ => PyTypeInfo.String,
                PyBool _ => PyTypeInfo.Bool,
                PyNone _ => PyTypeInfo.None,
                PyList _ => PyTypeInfo.List,
                PyTuple _ => PyTypeInfo.Tuple,
                PyDict _ => PyTypeInfo.Dict,
                _ => PyTypeInfo.Unknown
            };
        }

        /// <summary>
        /// 이진 연산자 타입 추론
        /// </summary>
        private PyTypeInfo InferBinaryOpType(BinaryOpExpression binOp)
        {
            var leftType = AnalyzeExpression(binOp.Left);
            var rightType = AnalyzeExpression(binOp.Right);

            return binOp.Operator switch
            {
                // 나누기: 항상 float 반환
                "/" => PyTypeInfo.Float,
                
                // 문자열/리스트 연산 (특수한 경우를 먼저 처리)
                "+" when leftType.IsString && rightType.IsString => PyTypeInfo.String,
                "+" when leftType.IsList && rightType.IsList => PyTypeInfo.List,
                "*" when (leftType.IsString && rightType.IsInt) || (leftType.IsInt && rightType.IsString) => PyTypeInfo.String,
                "*" when (leftType.IsList && rightType.IsInt) || (leftType.IsInt && rightType.IsList) => PyTypeInfo.List,
                
                // 수치 연산: int + int = int, float + anything = float
                "+" or "-" or "*" or "//" or "%" or "**" => InferArithmeticType(leftType, rightType, binOp.Operator),
                
                // 비교 연산
                "==" or "!=" or "<" or ">" or "<=" or ">=" => PyTypeInfo.Bool,
                
                // 논리 연산
                "and" => rightType, // Python의 and는 두 번째 operand 반환
                "or" => InferLogicalOrType(leftType, rightType),
                
                // 비트 연산
                "&" or "|" or "^" or "<<" or ">>" => InferBitwiseType(leftType, rightType),
                
                _ => PyTypeInfo.Unknown
            };
        }

        /// <summary>
        /// 산술 연산 타입 추론
        /// </summary>
        private PyTypeInfo InferArithmeticType(PyTypeInfo left, PyTypeInfo right, string op)
        {
            // Float이 포함되면 결과는 Float
            if (left.IsFloat || right.IsFloat)
                return PyTypeInfo.Float;
            
            // Int + Int = Int
            if (left.IsInt && right.IsInt)
                return PyTypeInfo.Int;
                
            // 기타 경우
            return PyTypeInfo.Unknown;
        }

        /// <summary>
        /// 논리 OR 타입 추론 (Python의 or는 첫 번째 truthy 값 반환)
        /// </summary>
        private PyTypeInfo InferLogicalOrType(PyTypeInfo left, PyTypeInfo right)
        {
            // 두 타입이 같으면 해당 타입
            if (left.Equals(right))
                return left;
                
            // Union type 생성 (간단한 구현)
            return PyTypeInfo.CreateUnion(left, right);
        }

        /// <summary>
        /// 비트 연산 타입 추론
        /// </summary>
        private PyTypeInfo InferBitwiseType(PyTypeInfo left, PyTypeInfo right)
        {
            if (left.IsInt && right.IsInt)
                return PyTypeInfo.Int;
                
            return PyTypeInfo.Unknown;
        }

        /// <summary>
        /// 단항 연산자 타입 추론
        /// </summary>
        private PyTypeInfo InferUnaryOpType(UnaryOpExpression unaryOp)
        {
            var operandType = AnalyzeExpression(unaryOp.Operand);

            return unaryOp.OpNode switch
            {
                UAdd or USub => operandType.IsNumeric ? operandType : PyTypeInfo.Unknown,
                Invert => operandType.IsInt ? PyTypeInfo.Int : PyTypeInfo.Unknown,
                Not => PyTypeInfo.Bool,
                _ => PyTypeInfo.Unknown
            };
        }

        /// <summary>
        /// 함수 호출 타입 추론
        /// </summary>
        private PyTypeInfo InferCallType(CallExpression call)
        {
            if (call.Function is NameExpression name)
            {
                // 내장 함수 타입 추론
                var builtinType = InferBuiltinCallType(name.Name, call.Arguments);
                if (!builtinType.IsUnknown)
                    return builtinType;
                
                // 사용자 정의 함수
                if (_functionSignatures.TryGetValue(name.Name, out var signature))
                {
                    return signature.ReturnType;
                }
            }
            
            return PyTypeInfo.Unknown;
        }

        /// <summary>
        /// 내장 함수 호출 타입 추론
        /// </summary>
        private PyTypeInfo InferBuiltinCallType(string functionName, List<Expression> args)
        {
            return functionName switch
            {
                "len" => PyTypeInfo.Int,
                "str" => PyTypeInfo.String,
                "int" => PyTypeInfo.Int,
                "float" => PyTypeInfo.Float,
                "bool" => PyTypeInfo.Bool,
                "list" => PyTypeInfo.List,
                "tuple" => PyTypeInfo.Tuple,
                "dict" => PyTypeInfo.Dict,
                "type" => PyTypeInfo.Type,
                "range" => PyTypeInfo.Range,
                "enumerate" => PyTypeInfo.Enumerate,
                "zip" => PyTypeInfo.Zip,
                "map" => PyTypeInfo.Map,
                "filter" => PyTypeInfo.Filter,
                _ => PyTypeInfo.Unknown
            };
        }

        /// <summary>
        /// 리스트 표현식 타입 추론
        /// </summary>
        private PyTypeInfo InferListType(ListExpression list)
        {
            if (list.Elements.Count == 0)
                return PyTypeInfo.List;

            // 모든 원소의 타입을 분석하여 공통 타입 찾기
            var elementTypes = list.Elements.Select(AnalyzeExpression).ToList();
            var commonType = FindCommonType(elementTypes);
            
            return PyTypeInfo.CreateList(commonType);
        }

        /// <summary>
        /// 튜플 표현식 타입 추론
        /// </summary>
        private PyTypeInfo InferTupleType(TupleExpression tuple)
        {
            var elementTypes = tuple.Elements.Select(AnalyzeExpression).ToList();
            return PyTypeInfo.CreateTuple(elementTypes);
        }

        /// <summary>
        /// 속성 접근 타입 추론
        /// </summary>
        private PyTypeInfo InferAttributeType(AttributeExpression attr)
        {
            var objectType = AnalyzeExpression(attr.Value);
            
            // 타입별 속성 추론
            return (objectType.TypeName, attr.Attr) switch
            {
                ("str", "upper") => PyTypeInfo.BuiltinMethod,
                ("str", "lower") => PyTypeInfo.BuiltinMethod,
                ("str", "split") => PyTypeInfo.BuiltinMethod,
                ("list", "append") => PyTypeInfo.BuiltinMethod,
                ("list", "extend") => PyTypeInfo.BuiltinMethod,
                ("dict", "get") => PyTypeInfo.BuiltinMethod,
                ("dict", "keys") => PyTypeInfo.DictKeys,
                ("dict", "values") => PyTypeInfo.DictValues,
                _ => PyTypeInfo.Unknown
            };
        }

        /// <summary>
        /// 반복 가능한 객체의 원소 타입 추론
        /// </summary>
        private PyTypeInfo InferIterableElementType(PyTypeInfo iterableType)
        {
            return iterableType.TypeName switch
            {
                "list" => iterableType.ElementType ?? PyTypeInfo.Unknown,
                "tuple" => iterableType.ElementType ?? PyTypeInfo.Unknown,
                "str" => PyTypeInfo.String, // 문자열의 각 문자는 문자열
                "range" => PyTypeInfo.Int,
                "dict" => PyTypeInfo.String, // 딕셔너리 반복은 키 반환
                _ => PyTypeInfo.Unknown
            };
        }

        /// <summary>
        /// 공통 타입 찾기
        /// </summary>
        private PyTypeInfo FindCommonType(List<PyTypeInfo> types)
        {
            if (types.Count == 0)
                return PyTypeInfo.Unknown;
                
            if (types.Count == 1)
                return types[0];

            var first = types[0];
            if (types.All(t => t.Equals(first)))
                return first;

            // 숫자 타입 통합
            if (types.All(t => t.IsNumeric))
            {
                if (types.Any(t => t.IsFloat))
                    return PyTypeInfo.Float;
                return PyTypeInfo.Int;
            }

            return PyTypeInfo.Unknown;
        }

        /// <summary>
        /// 변수 타입 설정
        /// </summary>
        private void SetVariableType(string variableName, PyTypeInfo type)
        {
            _variableTypes[variableName] = type;
        }

        /// <summary>
        /// 변수 타입 조회
        /// </summary>
        public PyTypeInfo GetVariableType(string variableName)
        {
            if (_variableTypes.TryGetValue(variableName, out var type))
                return type;
                
            return PyTypeInfo.Unknown;
        }

        /// <summary>
        /// 표현식 타입 조회
        /// </summary>
        public PyTypeInfo GetExpressionType(Expression expr)
        {
            return _expressionTypes.TryGetValue(expr, out var type) ? type : PyTypeInfo.Unknown;
        }

        /// <summary>
        /// 스코프 관리
        /// </summary>
        private void PushScope()
        {
            _scopeStack.Push(new Dictionary<string, PyTypeInfo>(_variableTypes));
        }

        private void PopScope()
        {
            if (_scopeStack.Count > 0)
            {
                _variableTypes.Clear();
                var parentScope = _scopeStack.Pop();
                foreach (var kvp in parentScope)
                {
                    _variableTypes[kvp.Key] = kvp.Value;
                }
            }
        }

        /// <summary>
        /// 내장 타입 초기화
        /// </summary>
        private void InitializeBuiltinTypes()
        {
            // 내장 함수들의 시그니처 등록
            _functionSignatures["len"] = new PyTypeSignature(PyTypeInfo.Int, new[] { PyTypeInfo.Unknown });
            _functionSignatures["str"] = new PyTypeSignature(PyTypeInfo.String, new[] { PyTypeInfo.Unknown });
            _functionSignatures["int"] = new PyTypeSignature(PyTypeInfo.Int, new[] { PyTypeInfo.Unknown });
            _functionSignatures["float"] = new PyTypeSignature(PyTypeInfo.Float, new[] { PyTypeInfo.Unknown });
            _functionSignatures["bool"] = new PyTypeSignature(PyTypeInfo.Bool, new[] { PyTypeInfo.Unknown });
        }
    }

    /// <summary>
    /// 타입 정보를 담는 클래스
    /// </summary>
    public class PyTypeInfo
    {
        public string TypeName { get; }
        public PyTypeInfo? ElementType { get; }
        public List<PyTypeInfo>? UnionTypes { get; }
        public List<PyTypeInfo>? TupleTypes { get; }

        private PyTypeInfo(string typeName, PyTypeInfo? elementType = null, 
                          List<PyTypeInfo>? unionTypes = null, List<PyTypeInfo>? tupleTypes = null)
        {
            TypeName = typeName;
            ElementType = elementType;
            UnionTypes = unionTypes;
            TupleTypes = tupleTypes;
        }

        // 기본 타입들
        public static readonly PyTypeInfo Unknown = new("unknown");
        public static readonly PyTypeInfo Int = new("int");
        public static readonly PyTypeInfo Float = new("float");
        public static readonly PyTypeInfo String = new("str");
        public static readonly PyTypeInfo Bool = new("bool");
        public static readonly PyTypeInfo None = new("None");
        public static readonly PyTypeInfo List = new("list");
        public static readonly PyTypeInfo Tuple = new("tuple");
        public static readonly PyTypeInfo Dict = new("dict");
        public static readonly PyTypeInfo Type = new("type");
        public static readonly PyTypeInfo Range = new("range");
        public static readonly PyTypeInfo Enumerate = new("enumerate");
        public static readonly PyTypeInfo Zip = new("zip");
        public static readonly PyTypeInfo Map = new("map");
        public static readonly PyTypeInfo Filter = new("filter");
        public static readonly PyTypeInfo BuiltinMethod = new("builtin_method");
        public static readonly PyTypeInfo DictKeys = new("dict_keys");
        public static readonly PyTypeInfo DictValues = new("dict_values");

        // 타입 생성 메서드들
        public static PyTypeInfo CreateList(PyTypeInfo elementType) => new("list", elementType);
        public static PyTypeInfo CreateTuple(List<PyTypeInfo> elementTypes) => new("tuple", tupleTypes: elementTypes);
        public static PyTypeInfo CreateUnion(PyTypeInfo left, PyTypeInfo right) => 
            new("union", unionTypes: new List<PyTypeInfo> { left, right });

        // 타입 검사 속성들
        public bool IsUnknown => TypeName == "unknown";
        public bool IsInt => TypeName == "int";
        public bool IsFloat => TypeName == "float";
        public bool IsString => TypeName == "str";
        public bool IsBool => TypeName == "bool";
        public bool IsList => TypeName == "list";
        public bool IsTuple => TypeName == "tuple";
        public bool IsDict => TypeName == "dict";
        public bool IsNone => TypeName == "None";
        public bool IsNumeric => IsInt || IsFloat;

        public override bool Equals(object? obj)
        {
            return obj is PyTypeInfo other && TypeName == other.TypeName;
        }

        public override int GetHashCode() => TypeName.GetHashCode();
        public override string ToString() => TypeName;
    }

    /// <summary>
    /// 타입 추론용 함수 시그니처 정보
    /// </summary>
    public class PyTypeSignature
    {
        public PyTypeInfo ReturnType { get; }
        public PyTypeInfo[] ParameterTypes { get; }

        public PyTypeSignature(PyTypeInfo returnType, PyTypeInfo[] parameterTypes)
        {
            ReturnType = returnType;
            ParameterTypes = parameterTypes;
        }
    }
}