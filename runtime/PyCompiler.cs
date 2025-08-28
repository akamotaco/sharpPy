namespace SharpPy
{
    #region Compiler Extension (AST → Bytecode)

    // AST를 바이트코드로 컴파일 (기존 시스템과 연동)
    public class PythonCompiler
    {
        private List<ByteCodeInstruction> _instructions;
        private List<PyObject> _constants;
        private List<string> _names;
        private List<string> _varNames;
        
        public PyCodeObject Compile(List<Statement> statements, string name = "<module>")
        {
            _instructions = new List<ByteCodeInstruction>();
            _constants = new List<PyObject>();
            _names = new List<string>();
            _varNames = new List<string>();
            
            Console.WriteLine($"\n🔧 컴파일: {name}");
            
            foreach (var statement in statements)
            {
                CompileStatement(statement);
            }
            
            // 모듈은 None 반환
            EmitLoadConst(PyNone.Instance);
            EmitInstruction(ByteCodeOp.RETURN_VALUE);
            
            var codeObject = new PyCodeObject(name, _instructions, _constants, _names, _varNames);
            Console.WriteLine($"✅ 컴파일 완료: {_instructions.Count}개 명령어");
            return codeObject;
        }
        
        private void CompileStatement(Statement statement)
        {
            switch (statement)
            {
                case AssignStatement assign:
                    CompileExpression(assign.Value);
                    EmitStoreName(assign.VariableName);
                    break;
                    
                case AugAssignStatement augAssign:
                    CompileAugAssign(augAssign);
                    break;
                    
                case WalrusStatement walrus:
                    CompileExpression(walrus.Value);
                    EmitInstruction(ByteCodeOp.DUP_TOP);  // 값 복사
                    EmitStoreName(walrus.Target);         // 저장
                    break;
                    
                case ExpressionStatement expr:
                    CompileExpression(expr.Expression);
                    EmitInstruction(ByteCodeOp.POP_TOP);
                    break;
                    
                case ReturnStatement ret:
                    if (ret.Value != null)
                        CompileExpression(ret.Value);
                    else
                        EmitLoadConst(PyNone.Instance);
                    EmitInstruction(ByteCodeOp.RETURN_VALUE);
                    break;
                    
                case YieldStatement yield:
                    if (yield.Value != null)
                        CompileExpression(yield.Value);
                    else
                        EmitLoadConst(PyNone.Instance);
                    EmitInstruction(ByteCodeOp.YIELD_VALUE);
                    break;
                    
                case YieldFromStatement yieldFrom:
                    CompileExpression(yieldFrom.Value);
                    EmitInstruction(ByteCodeOp.GET_ITER);
                    EmitInstruction(ByteCodeOp.YIELD_FROM);
                    break;
                    
                case FunctionDefStatement func:
                    CompileFunction(func);
                    break;
                    
                case AsyncFunctionDefStatement asyncFunc:
                    CompileAsyncFunction(asyncFunc);
                    break;
                    
                case ClassDefStatement cls:
                    CompileClass(cls);
                    break;
                    
                case TypeAliasStatement typeAlias:
                    CompileTypeAlias(typeAlias);
                    break;
                    
                case ImportStatement import:
                    CompileImport(import);
                    break;
                    
                case ImportFromStatement importFrom:
                    CompileImportFrom(importFrom);
                    break;
                    
                case IfStatement ifStmt:
                    CompileIf(ifStmt);
                    break;
                    
                case WhileStatement whileStmt:
                    CompileWhile(whileStmt);
                    break;
                    
                case ForStatement forStmt:
                    CompileFor(forStmt);
                    break;
                    
                case TryStatement tryStmt:
                    CompileTry(tryStmt);
                    break;
                    
                case WithStatement withStmt:
                    CompileWith(withStmt);
                    break;
                    
                case MatchStatement matchStmt:
                    CompileMatch(matchStmt);
                    break;
                    
                case BreakStatement:
                    EmitInstruction(ByteCodeOp.BREAK_LOOP);
                    break;
                    
                case ContinueStatement:
                    EmitInstruction(ByteCodeOp.CONTINUE_LOOP);
                    break;
                    
                case PassStatement:
                    EmitInstruction(ByteCodeOp.NOP);
                    break;
                    
                case AssertStatement assert:
                    CompileAssert(assert);
                    break;
                    
                case RaiseStatement raise:
                    CompileRaise(raise);
                    break;
                    
                case DeleteStatement delete:
                    CompileDelete(delete);
                    break;
                    
                case GlobalStatement global:
                    CompileGlobal(global);
                    break;
                    
                case NonlocalStatement nonlocal:
                    CompileNonlocal(nonlocal);
                    break;
                    
                default:
                    throw new NotImplementedException($"Statement {statement.GetType().Name} not implemented");
            }
        }
        
        private void CompileExpression(Expression expression)
        {
            switch (expression)
            {
                case ConstantExpression constant:
                    EmitLoadConst(constant.Value);
                    break;
                    
                case NameExpression name:
                    EmitLoadName(name.Name);
                    break;
                    
                case BinaryOpExpression binOp:
                    CompileExpression(binOp.Left);
                    CompileExpression(binOp.Right);
                    EmitBinaryOp(binOp.Operator);
                    break;
                    
                case UnaryOpExpression unaryOp:
                    CompileExpression(unaryOp.Operand);
                    EmitUnaryOp(unaryOp.Op);
                    break;
                    
                case CompareExpression compare:
                    CompileExpression(compare.Left);
                    CompileExpression(compare.Right);
                    EmitCompareOp(compare.Op);
                    break;
                    
                case BoolOpExpression boolOp:
                    CompileBoolOp(boolOp);
                    break;
                    
                case CallExpression call:
                    CompileExpression(call.Function);
                    foreach (var arg in call.Arguments)
                    {
                        CompileExpression(arg);
                    }
                    EmitInstruction(ByteCodeOp.CALL_FUNCTION, call.Arguments.Count);
                    break;
                    
                case AttributeExpression attr:
                    CompileExpression(attr.Value);
                    EmitLoadAttr(attr.Attr);
                    break;
                    
                case SubscriptExpression subscript:
                    CompileExpression(subscript.Value);
                    CompileExpression(subscript.Slice);
                    EmitInstruction(ByteCodeOp.LOAD_SUBSCR);
                    break;
                    
                case ListExpression list:
                    foreach (var element in list.Elements)
                    {
                        CompileExpression(element);
                    }
                    EmitInstruction(ByteCodeOp.BUILD_LIST, list.Elements.Count);
                    break;
                    
                case TupleExpression tuple:
                    foreach (var element in tuple.Elements)
                    {
                        CompileExpression(element);
                    }
                    EmitInstruction(ByteCodeOp.BUILD_TUPLE, tuple.Elements.Count);
                    break;
                    
                case SetExpression set:
                    foreach (var element in set.Elements)
                    {
                        CompileExpression(element);
                    }
                    EmitInstruction(ByteCodeOp.BUILD_SET, set.Elements.Count);
                    break;
                    
                case DictExpression dict:
                    foreach (var kvp in dict.Items)
                    {
                        CompileExpression(kvp.Key);
                        CompileExpression(kvp.Value);
                    }
                    EmitInstruction(ByteCodeOp.BUILD_MAP, dict.Items.Count);
                    break;
                    
                case LambdaExpression lambda:
                    CompileLambda(lambda);
                    break;
                    
                case ConditionalExpression conditional:
                    CompileConditional(conditional);
                    break;
                    
                case AwaitExpression await:
                    CompileExpression(await.Value);
                    EmitInstruction(ByteCodeOp.GET_AWAITABLE);
                    break;
                    
                case FStringExpression fstring:
                    CompileFString(fstring);
                    break;
                    
                case StarredExpression starred:
                    CompileExpression(starred.Value);
                    // 별표 처리는 문맥에 따라 다름
                    break;
                    
                // Python 3.12 Type Parameters
                case TypeVarExpression typeVar:
                    EmitLoadConst(new PyString(typeVar.Name));
                    EmitInstruction(ByteCodeOp.CALL_INTRINSIC_1, (int)IntrinsicFunction.TYPEVAR);
                    break;
                    
                case ParamSpecExpression paramSpec:
                    EmitLoadConst(new PyString(paramSpec.Name));
                    EmitInstruction(ByteCodeOp.CALL_INTRINSIC_1, (int)IntrinsicFunction.PARAMSPEC);
                    break;
                    
                case TypeVarTupleExpression typeVarTuple:
                    EmitLoadConst(new PyString(typeVarTuple.Name));
                    EmitInstruction(ByteCodeOp.CALL_INTRINSIC_1, (int)IntrinsicFunction.TYPEVARTUPLE);
                    break;
                    
                default:
                    throw new NotImplementedException($"Expression {expression.GetType().Name} not implemented");
            }
        }
        
        private void CompileFunction(FunctionDefStatement func)
        {
            // 함수 바디 컴파일
            var compiler = new PythonCompiler();
            var funcCode = compiler.Compile(func.Body, func.Name);
            
            // 함수 코드 객체를 상수로 추가
            EmitLoadConst(funcCode);
            
            // 기존 PyFunction 시스템과 연동
            // MAKE_FUNCTION 대신 직접 PyFunction 생성하는 내장함수 사용
            EmitLoadName("__make_function__");
            EmitInstruction(ByteCodeOp.CALL_FUNCTION, 1);
            EmitStoreName(func.Name);
        }
        
        // 바이트코드 생성 도우미들
        private void EmitInstruction(ByteCodeOp opCode, int argument = 0)
        {
            _instructions.Add(new ByteCodeInstruction(opCode, argument));
        }
        
        private void EmitLoadConst(PyObject value)
        {
            var index = AddConstant(value);
            EmitInstruction(ByteCodeOp.LOAD_CONST, index);
        }
        
        private void EmitLoadName(string name)
        {
            var index = AddName(name);
            EmitInstruction(ByteCodeOp.LOAD_NAME, index);
        }
        
        private void EmitStoreName(string name)
        {
            var index = AddName(name);
            EmitInstruction(ByteCodeOp.STORE_NAME, index);
        }
        
        private void EmitBinaryOp(string op)
        {
            var opCode = op switch
            {
                "+" => ByteCodeOp.BINARY_ADD,
                "-" => ByteCodeOp.BINARY_SUBTRACT,
                "*" => ByteCodeOp.BINARY_MULTIPLY,
                "/" => ByteCodeOp.BINARY_DIVIDE,     // Python 3.x 에서 / 는 true division
                "//" => ByteCodeOp.BINARY_FLOOR_DIVIDE,   // 바닥 나눗셈
                "%" => ByteCodeOp.BINARY_MODULO,          // 모듈로 연산
                "**" => ByteCodeOp.BINARY_POWER,          // 거듭제곱
                "<<" => ByteCodeOp.BINARY_LSHIFT,         // 좌시프트
                ">>" => ByteCodeOp.BINARY_RSHIFT,         // 우시프트
                "&" => ByteCodeOp.BINARY_AND,             // 비트 AND
                "|" => ByteCodeOp.BINARY_OR,              // 비트 OR
                "^" => ByteCodeOp.BINARY_XOR,             // 비트 XOR
                "@" => ByteCodeOp.BINARY_MATRIX_MULTIPLY, // 행렬 곱셈
                _ => throw new NotImplementedException($"Binary operator '{op}' not implemented")
            };
            EmitInstruction(opCode);
        }
        
        private int AddConstant(PyObject value)
        {
            _constants.Add(value);
            return _constants.Count - 1;
        }
        
        private int AddName(string name)
        {
            if (!_names.Contains(name))
                _names.Add(name);
            return _names.IndexOf(name);
        }
        
        // 추가 컴파일 메서드들
        
        private void EmitUnaryOp(string op)
        {
            var opCode = op switch
            {
                "+" => ByteCodeOp.UNARY_POSITIVE,
                "-" => ByteCodeOp.UNARY_NEGATIVE,
                "not" => ByteCodeOp.UNARY_NOT,
                "~" => ByteCodeOp.UNARY_INVERT,
                _ => throw new NotImplementedException($"Unary operator '{op}' not implemented")
            };
            EmitInstruction(opCode);
        }
        
        private void EmitCompareOp(string op)
        {
            var compareOp = op switch
            {
                "<" => 0,  // LT
                "<=" => 1, // LE
                "==" => 2, // EQ
                "!=" => 3, // NE
                ">" => 4,  // GT
                ">=" => 5, // GE
                "in" => 6, // IN
                "not in" => 7, // NOT_IN
                "is" => 8, // IS
                "is not" => 9, // IS_NOT
                _ => throw new NotImplementedException($"Compare operator '{op}' not implemented")
            };
            EmitInstruction(ByteCodeOp.COMPARE_OP, compareOp);
        }
        
        private void EmitLoadAttr(string attrName)
        {
            var index = AddName(attrName);
            EmitInstruction(ByteCodeOp.LOAD_ATTR, index);
        }
        
        private void CompileAugAssign(AugAssignStatement augAssign)
        {
            // target += value -> LOAD target, LOAD value, INPLACE_ADD, STORE target
            EmitLoadName(augAssign.Target);
            CompileExpression(augAssign.Value);
            
            var opCode = augAssign.Op switch
            {
                "+=" => ByteCodeOp.INPLACE_ADD,
                "-=" => ByteCodeOp.INPLACE_SUBTRACT,
                "*=" => ByteCodeOp.INPLACE_MULTIPLY,
                "/=" => ByteCodeOp.INPLACE_DIVIDE,
                "//=" => ByteCodeOp.INPLACE_FLOOR_DIVIDE,
                "%=" => ByteCodeOp.INPLACE_MODULO,
                "**=" => ByteCodeOp.INPLACE_POWER,
                "&=" => ByteCodeOp.INPLACE_AND,
                "|=" => ByteCodeOp.INPLACE_OR,
                "^=" => ByteCodeOp.INPLACE_XOR,
                "<<=" => ByteCodeOp.INPLACE_LSHIFT,
                ">>=" => ByteCodeOp.INPLACE_RSHIFT,
                "@=" => ByteCodeOp.INPLACE_MATRIX_MULTIPLY,
                _ => throw new NotImplementedException($"Augment assign operator '{augAssign.Op}' not implemented")
            };
            
            EmitInstruction(opCode);
            EmitStoreName(augAssign.Target);
        }
        
        // 단순화된 구현 - 실제로는 더 복잡한 로직이 필요
        private void CompileAsyncFunction(AsyncFunctionDefStatement asyncFunc) { /* TODO */ }
        private void CompileClass(ClassDefStatement cls) { /* TODO */ }
        private void CompileTypeAlias(TypeAliasStatement typeAlias) { /* TODO */ }
        private void CompileImport(ImportStatement import) { /* TODO */ }
        private void CompileImportFrom(ImportFromStatement importFrom) { /* TODO */ }
        private void CompileIf(IfStatement ifStmt) { /* TODO */ }
        private void CompileWhile(WhileStatement whileStmt) { /* TODO */ }
        private void CompileFor(ForStatement forStmt) { /* TODO */ }
        private void CompileTry(TryStatement tryStmt) { /* TODO */ }
        private void CompileWith(WithStatement withStmt) { /* TODO */ }
        private void CompileMatch(MatchStatement matchStmt) { /* TODO */ }
        private void CompileAssert(AssertStatement assert) { /* TODO */ }
        private void CompileRaise(RaiseStatement raise) { /* TODO */ }
        private void CompileDelete(DeleteStatement delete) { /* TODO */ }
        private void CompileGlobal(GlobalStatement global) { /* TODO */ }
        private void CompileNonlocal(NonlocalStatement nonlocal) { /* TODO */ }
        private void CompileBoolOp(BoolOpExpression boolOp) { /* TODO */ }
        private void CompileLambda(LambdaExpression lambda) { /* TODO */ }
        private void CompileConditional(ConditionalExpression conditional) { /* TODO */ }
        private void CompileFString(FStringExpression fstring) { /* TODO */ }
        
        // Evaluate 메서드 - 나중에 구현
        public PyObject Evaluate(PyScope scope)
        {
            throw new NotImplementedException("PythonCompiler.Evaluate() - 나중에 구현예정");
        }
    }

    #endregion
}