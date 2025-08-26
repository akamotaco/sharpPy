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
                    
                case FunctionDefStatement func:
                    CompileFunction(func);
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
                    
                case CallExpression call:
                    CompileExpression(call.Function);
                    foreach (var arg in call.Arguments)
                    {
                        CompileExpression(arg);
                    }
                    EmitInstruction(ByteCodeOp.CALL_FUNCTION, call.Arguments.Count);
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
                "/" => ByteCodeOp.BINARY_DIVIDE,
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
    }

    #endregion
}