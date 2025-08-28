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
        
        // CPython-style exception handling support
        public Stack<int> ExceptionHandlers { get; } = new Stack<int>();
        public PyBaseException? LastException { get; set; }
        
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
        
        /// <summary>
        /// CPython-style exception handler management
        /// </summary>
        public void PushExceptionHandler(int handlerOffset)
        {
            ExceptionHandlers.Push(handlerOffset);
        }
        
        public void PopExceptionHandler()
        {
            if (ExceptionHandlers.Count > 0)
                ExceptionHandlers.Pop();
        }
        
        public int? GetExceptionHandler()
        {
            return ExceptionHandlers.Count > 0 ? ExceptionHandlers.Peek() : null;
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

                // CPython-style Control Flow Opcodes (Phase 1 - High Priority)
                case ByteCodeOp.POP_JUMP_IF_TRUE:
                    var truthValue = frame.ValueStack.Pop();
                    if (truthValue.PyBoolValue())
                    {
                        frame.InstructionPointer = instruction.Argument;
                        return null; // Continue execution from new position
                    }
                    break;
                    
                case ByteCodeOp.POP_JUMP_IF_FALSE:
                    var falseValue = frame.ValueStack.Pop();
                    if (!falseValue.PyBoolValue())
                    {
                        frame.InstructionPointer = instruction.Argument;
                        return null; // Continue execution from new position
                    }
                    break;
                    
                case ByteCodeOp.JUMP_FORWARD:
                    frame.InstructionPointer = instruction.Argument;
                    return null; // Continue execution from new position
                    
                case ByteCodeOp.JUMP_BACKWARD:
                    frame.InstructionPointer = instruction.Argument;
                    return null; // Continue execution from new position

                // CPython-style Container Building Opcodes (Phase 1)
                case ByteCodeOp.BUILD_LIST:
                    var listSize = instruction.Argument;
                    var listItems = new List<PyObject>();
                    for (int i = 0; i < listSize; i++)
                    {
                        listItems.Insert(0, frame.ValueStack.Pop()); // Reverse order
                    }
                    var pyList = new PyList();
                    foreach (var item in listItems)
                    {
                        // Add items to list - need to find proper method
                        pyList.Add(item);
                    }
                    frame.ValueStack.Push(pyList);
                    break;
                    
                case ByteCodeOp.BUILD_TUPLE:
                    var tupleSize = instruction.Argument;
                    var tupleItems = new PyObject[tupleSize];
                    for (int i = tupleSize - 1; i >= 0; i--)
                    {
                        tupleItems[i] = frame.ValueStack.Pop();
                    }
                    var pyTuple = new PyTuple(tupleItems);
                    frame.ValueStack.Push(pyTuple);
                    break;
                    
                case ByteCodeOp.BUILD_SET:
                    var setSize = instruction.Argument;
                    var pySet = new PySet();
                    for (int i = 0; i < setSize; i++)
                    {
                        pySet.Add(frame.ValueStack.Pop());
                    }
                    frame.ValueStack.Push(pySet);
                    break;
                    
                case ByteCodeOp.BUILD_MAP:
                    var mapSize = instruction.Argument;
                    var pyDict = new PyDict();
                    for (int i = 0; i < mapSize; i++)
                    {
                        var mapValue = frame.ValueStack.Pop();
                        var mapKey = frame.ValueStack.Pop();
                        pyDict.SetItem(mapKey, mapValue);
                    }
                    frame.ValueStack.Push(pyDict);
                    break;

                // CPython-style Iterator Opcodes
                case ByteCodeOp.GET_ITER:
                    var iterable = frame.ValueStack.Pop();
                    var iterator = iterable.GetIterator();
                    frame.ValueStack.Push(iterator);
                    break;
                    
                case ByteCodeOp.FOR_ITER:
                    var iter = frame.ValueStack.Peek(); // Don't pop yet
                    try
                    {
                        var nextItem = iter.Next();
                        frame.ValueStack.Push(nextItem);
                    }
                    catch (PythonException ex) when (ex.PyException is PyStopIteration)
                    {
                        frame.ValueStack.Pop(); // Remove iterator
                        frame.InstructionPointer = instruction.Argument; // Jump to end of loop
                        return null;
                    }
                    break;

                // CPython-style Comparison Operations
                case ByteCodeOp.COMPARE_OP:
                    var compareRight = frame.ValueStack.Pop();
                    var compareLeft = frame.ValueStack.Pop();
                    var compareResult = CompareOperation(compareLeft, compareRight, instruction.Argument);
                    frame.ValueStack.Push(compareResult);
                    break;

                // CPython-style Unary Operations
                case ByteCodeOp.UNARY_POSITIVE:
                    var posValue = frame.ValueStack.Pop();
                    frame.ValueStack.Push(posValue.Positive());
                    break;
                    
                case ByteCodeOp.UNARY_NEGATIVE:
                    var negValue = frame.ValueStack.Pop();
                    frame.ValueStack.Push(negValue.Negative());
                    break;
                    
                case ByteCodeOp.UNARY_NOT:
                    var notValue = frame.ValueStack.Pop();
                    var boolResult = notValue.PyBoolValue() ? PyBool.False : PyBool.True;
                    frame.ValueStack.Push(boolResult);
                    break;

                // CPython-style Exception Handling (Basic)
                case ByteCodeOp.SETUP_EXCEPT:
                    frame.PushExceptionHandler(instruction.Argument);
                    break;
                    
                case ByteCodeOp.POP_EXCEPT:
                    frame.PopExceptionHandler();
                    break;
                    
                case ByteCodeOp.RERAISE:
                    if (frame.LastException != null)
                        throw new PythonException(frame.LastException);
                    break;

                // CPython-style Sequence Operations  
                case ByteCodeOp.UNPACK_SEQUENCE:
                    var sequence = frame.ValueStack.Pop();
                    var count = instruction.Argument;
                    var items = UnpackSequence(sequence, count);
                    foreach (var item in items.Reverse())
                    {
                        frame.ValueStack.Push(item);
                    }
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

        /// <summary>
        /// CPython-style comparison operations
        /// </summary>
        private PyObject CompareOperation(PyObject left, PyObject right, int compareOp)
        {
            var operation = (CompareOp)compareOp;
            return operation switch
            {
                CompareOp.Eq => left.RichCompare(right, PyObject.CompareOp.EQ),
                CompareOp.NotEq => left.RichCompare(right, PyObject.CompareOp.NE),
                CompareOp.Lt => left.RichCompare(right, PyObject.CompareOp.LT),
                CompareOp.LtE => left.RichCompare(right, PyObject.CompareOp.LE),
                CompareOp.Gt => left.RichCompare(right, PyObject.CompareOp.GT),
                CompareOp.GtE => left.RichCompare(right, PyObject.CompareOp.GE),
                CompareOp.In => ((PyBool)right.Contains(left)),
                CompareOp.NotIn => ((PyBool)right.Contains(left)).Not(),
                CompareOp.Is => ReferenceEquals(left, right) ? PyBool.True : PyBool.False,
                CompareOp.IsNot => ReferenceEquals(left, right) ? PyBool.False : PyBool.True,
                _ => throw new NotImplementedException($"Compare operation {operation} not implemented")
            };
        }

        /// <summary>
        /// CPython-style sequence unpacking
        /// </summary>
        private PyObject[] UnpackSequence(PyObject sequence, int count)
        {
            if (sequence is PyList list)
            {
                if (list.Length() != count)
                    throw PyValueError.Create($"too many values to unpack (expected {count})");
                return list.Items.Take(count).ToArray();
            }
            else if (sequence is PyTuple tuple)
            {
                if (tuple.Items.Length != count)
                    throw PyValueError.Create($"too many values to unpack (expected {count})");
                return tuple.Items.Take(count).ToArray();
            }
            else if (sequence is PyString str)
            {
                if (str.Value.Length != count)
                    throw PyValueError.Create($"too many values to unpack (expected {count})");
                return str.Value.Select(c => new PyString(c.ToString())).Cast<PyObject>().ToArray();
            }
            else
            {
                // Try to get iterator and collect items
                var iterator = sequence.GetIterator();
                var items = new List<PyObject>();
                try
                {
                    while (items.Count < count)
                    {
                        items.Add(iterator.Next());
                    }
                    
                    // Check if there are more items (would indicate too many values)
                    try
                    {
                        iterator.Next();
                        throw PyValueError.Create($"too many values to unpack (expected {count})");
                    }
                    catch (PythonException ex) when (ex.PyException is PyStopIteration)
                    {
                        // This is expected - no more items
                    }
                }
                catch (PythonException ex) when (ex.PyException is PyStopIteration && items.Count < count)
                {
                    throw PyValueError.Create($"not enough values to unpack (expected {count}, got {items.Count})");
                }
                
                return items.ToArray();
            }
        }

        /// <summary>
        /// Compare operation enumeration matching CPython
        /// </summary>
        private enum CompareOp : int
        {
            Lt = 0,
            LtE = 1, 
            Eq = 2,
            NotEq = 3,
            Gt = 4,
            GtE = 5,
            In = 6,
            NotIn = 7,
            Is = 8,
            IsNot = 9
        }
    }

    #endregion
}