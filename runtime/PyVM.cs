namespace SharpPy
{
    #region Virtual Machine (기존 LEGB 시스템 활용)

    // VM 실행 프레임 (기존 PyScopeChain과 연동)
    public class PyFrame
    {
        public PyCodeObject Code { get; }
        public Stack<PyObject> ValueStack { get; }
        public PyScopeChain ScopeChain { get; }       // 기존 LEGB 시스템 활용!
        public Dictionary<string, PyObject> FastLocals { get; } // 빠른 지역변수 접근
        public int InstructionPointer { get; set; }
        
        public PyFrame(PyCodeObject code, PyObject[] args, PyScopeChain parentScope = null)
        {
            Code = code;
            ValueStack = new Stack<PyObject>();
            ScopeChain = new PyScopeChain(); // 새로운 스코프 체인 생성
            FastLocals = new Dictionary<string, PyObject>();
            InstructionPointer = 0;
            
            // 함수 스코프 생성
            ScopeChain.PushScope(ScopeType.Local, code.Name);
            
            // 함수 인자를 지역 변수로 바인딩
            for (int i = 0; i < args.Length && i < code.ArgCount; i++)
            {
                var paramName = code.VarNames[i];
                FastLocals[paramName] = args[i];
                ScopeChain.AssignVariable(paramName, args[i]);
            }
        }
        
        public override string ToString() => $"<frame for {Code.Name}>";
    }

    // Python 가상 머신 (기존 객체 시스템과 완전 통합)
    public class PythonVM
    {
        public static PythonVM Instance { get; } = new PythonVM();
        
        private readonly Stack<PyFrame> _frameStack;
        private readonly PyScopeChain _globalScope;
        
        private PythonVM()
        {
            _frameStack = new Stack<PyFrame>();
            _globalScope = new PyScopeChain(); // 기존 LEGB 시스템 사용!
        }
        
        // 메인 모듈 실행
        public PyObject ExecuteModule(PyCodeObject codeObject)
        {
            var frame = new PyFrame(codeObject, new PyObject[0], _globalScope);
            return ExecuteFrame(frame);
        }
        
        // 프레임 실행 (바이트코드 해석)
        public PyObject ExecuteFrame(PyFrame frame)
        {
            _frameStack.Push(frame);
            
            Console.WriteLine($"\n🚀 VM 실행: {frame}");
            
            try
            {
                while (frame.InstructionPointer < frame.Code.Instructions.Count)
                {
                    var instruction = frame.Code.Instructions[frame.InstructionPointer];
                    
                    if (frame.ValueStack.Count <= 10) // 스택이 너무 크지 않을 때만 출력
                    {
                        var stackContents = string.Join(", ", frame.ValueStack.Reverse().Take(5));
                        Console.WriteLine($"  {frame.InstructionPointer,3}: {instruction,-25} 스택:[{stackContents}]");
                    }
                    
                    var result = ExecuteInstruction(frame, instruction);
                    
                    // RETURN_VALUE인 경우 함수 종료
                    if (result != null)
                    {
                        Console.WriteLine($"✅ VM 완료: {result}");
                        return result;
                    }
                    
                    frame.InstructionPointer++;
                }
                
                // 명시적 return이 없으면 None 반환
                Console.WriteLine($"✅ VM 완료: None (암시적)");
                return PyNone.Instance;
            }
            finally
            {
                _frameStack.Pop();
            }
        }
        
        // 개별 명령어 실행 (기존 시스템과 연동)
        private PyObject ExecuteInstruction(PyFrame frame, ByteCodeInstruction instruction)
        {
            switch (instruction.OpCode)
            {
                case ByteCodeOp.LOAD_CONST:
                    var constant = frame.Code.Constants[instruction.Argument];
                    frame.ValueStack.Push(constant);
                    break;
                    
                case ByteCodeOp.LOAD_NAME:
                    var name = frame.Code.Names[instruction.Argument];
                    // 기존 LEGB 시스템 사용!
                    var value = frame.ScopeChain.LookupVariable(name);
                    frame.ValueStack.Push(value);
                    break;
                    
                case ByteCodeOp.STORE_NAME:
                    var storeName = frame.Code.Names[instruction.Argument];
                    var storeValue = frame.ValueStack.Pop();
                    // 기존 LEGB 시스템 사용!
                    frame.ScopeChain.AssignVariable(storeName, storeValue);
                    break;
                    
                case ByteCodeOp.LOAD_GLOBAL:
                    var globalName = frame.Code.Names[instruction.Argument];
                    var globalValue = frame.ScopeChain.GlobalScope.GetVariable(globalName) ?? 
                                    frame.ScopeChain.BuiltinModule.GetBuiltin(globalName);
                    if (globalValue == null)
                        throw PyNameError.Create($"name '{globalName}' is not defined");
                    frame.ValueStack.Push(globalValue);
                    break;
                    
                case ByteCodeOp.BINARY_ADD:
                    var rightAdd = frame.ValueStack.Pop();
                    var leftAdd = frame.ValueStack.Pop();
                    var addResult = BinaryOperation(leftAdd, rightAdd, "+");
                    frame.ValueStack.Push(addResult);
                    break;
                    
                case ByteCodeOp.BINARY_MULTIPLY:
                    var rightMul = frame.ValueStack.Pop();
                    var leftMul = frame.ValueStack.Pop();
                    var mulResult = BinaryOperation(leftMul, rightMul, "*");
                    frame.ValueStack.Push(mulResult);
                    break;
                    
                case ByteCodeOp.BINARY_SUBTRACT:
                    var rightSub = frame.ValueStack.Pop();
                    var leftSub = frame.ValueStack.Pop();
                    var subResult = BinaryOperation(leftSub, rightSub, "-");
                    frame.ValueStack.Push(subResult);
                    break;
                    
                case ByteCodeOp.CALL_FUNCTION:
                    var argCount = instruction.Argument;
                    var args = new PyObject[argCount];
                    for (int i = argCount - 1; i >= 0; i--)
                    {
                        args[i] = frame.ValueStack.Pop();
                    }
                    var function = frame.ValueStack.Pop();
                    
                    // 기존 PyObject.Call() 시스템 사용!
                    var callResult = function.Call(args);
                    frame.ValueStack.Push(callResult);
                    break;
                    
                case ByteCodeOp.LOAD_ATTR:
                    var attrName = frame.Code.Names[instruction.Argument];
                    var obj = frame.ValueStack.Pop();
                    // 기존 Attribute 시스템 사용!
                    var attr = obj.GetAttribute(attrName);
                    frame.ValueStack.Push(attr);
                    break;
                    
                case ByteCodeOp.STORE_ATTR:
                    var setAttrName = frame.Code.Names[instruction.Argument];
                    var setObj = frame.ValueStack.Pop();
                    var setAttrValue = frame.ValueStack.Pop();
                    // 기존 Attribute 시스템 사용!
                    setObj.SetAttribute(setAttrName, setAttrValue);
                    break;
                    
                case ByteCodeOp.RETURN_VALUE:
                    var returnValue = frame.ValueStack.Count > 0 ? frame.ValueStack.Pop() : PyNone.Instance;
                    return returnValue;
                    
                case ByteCodeOp.POP_TOP:
                    if (frame.ValueStack.Count > 0)
                        frame.ValueStack.Pop();
                    break;
                    
                case ByteCodeOp.DUP_TOP:
                    if (frame.ValueStack.Count > 0)
                    {
                        var top = frame.ValueStack.Peek();
                        frame.ValueStack.Push(top);
                    }
                    break;
                    
                case ByteCodeOp.NOP:
                    // 아무것도 안 함
                    break;
                    
                default:
                    throw new NotImplementedException($"OpCode {instruction.OpCode} not implemented");
            }
            
            return null;
        }
        
        // 이항 연산 (기존 타입 시스템 활용)
        private PyObject BinaryOperation(PyObject left, PyObject right, string op)
        {
            if (left is PyInt leftInt && right is PyInt rightInt)
            {
                var result = op switch
                {
                    "+" => new PyInt(leftInt.Value + rightInt.Value),
                    "-" => new PyInt(leftInt.Value - rightInt.Value),
                    "*" => new PyInt(leftInt.Value * rightInt.Value),
                    "/" => new PyInt(leftInt.Value / rightInt.Value),
                    _ => throw PyTypeError.Create($"unsupported operator: {op}")
                };
                
                Console.WriteLine($"    → {left} {op} {right} = {result}");
                return result;
            }
            
            throw PyTypeError.Create($"unsupported operand type(s) for {op}: '{left.GetTypeName()}' and '{right.GetTypeName()}'");
        }
    }

    #endregion
}