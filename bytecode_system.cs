// bytecode_system.cs
using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    // Bytecode Operation Codes (inspired by Python's opcodes)
    public enum OpCode : byte
    {
        // Stack operations
        LOAD_CONST = 1,     // Load constant onto stack
        LOAD_NAME = 2,      // Load variable onto stack
        STORE_NAME = 3,     // Store top of stack in variable
        POP_TOP = 4,        // Remove top of stack
        DUP_TOP = 5,    // Duplicate top of stack
        DUP_TOP_TWO = 6,    // 스택 상위 2개 항목 복제
        ROT_THREE = 7,       // 스택 상위 3개 항목 회전

        // Arithmetic operations
        BINARY_ADD = 10,
        BINARY_SUBTRACT = 11,
        BINARY_MULTIPLY = 12,
        BINARY_DIVIDE = 13,
        BINARY_MODULO = 14,
        BINARY_POWER = 15,
        UNARY_NEGATIVE = 16,
        UNARY_NOT = 17,

        // Comparison operations
        COMPARE_EQ = 20,
        COMPARE_NE = 21,
        COMPARE_LT = 22,
        COMPARE_GT = 23,
        COMPARE_LE = 24,
        COMPARE_GE = 25,
        COMPARE_IN = 26,
        COMPARE_IS = 27,

        // Logical operations
        LOGICAL_AND = 30,
        LOGICAL_OR = 31,

        // Control flow
        JUMP_FORWARD = 40,
        JUMP_IF_FALSE = 41,
        JUMP_IF_TRUE = 42,
        JUMP_ABSOLUTE = 43,

        // Function operations
        CALL_FUNCTION = 50,
        RETURN_VALUE = 51,
        MAKE_FUNCTION = 52,
        CALL_FUNCTION_KW = 53,     // 키워드 인자가 있는 함수 호출
        CALL_FUNCTION_VAR = 54,    // 가변 인자가 있는 함수 호출
        BUILD_MAP = 55,             // 딕셔너리 생성 (키워드 인자용)

        // Collection operations
        BUILD_LIST = 60,
        BUILD_TUPLE = 61,
        BUILD_DICT = 62,
        LOAD_INDEX = 63,
        STORE_INDEX = 64,
        LOAD_ATTR = 65,
        STORE_ATTR = 66,
        UNPACK_SEQUENCE = 67,
        BUILD_SLICE = 68,    // 슬라이스 객체 생성

        // Loop operations
        GET_ITER = 70,
        FOR_ITER = 71,
        BREAK_LOOP = 72,
        CONTINUE_LOOP = 73,

        // Exception handling
        SETUP_EXCEPT = 80,
        POP_EXCEPT = 81,
        RAISE_VARARGS = 82,

        // Import operations
        IMPORT_NAME = 90,
        IMPORT_FROM = 91,

        // Special
        NOP = 255           // No operation
    }

    // Single bytecode instruction
    public struct Instruction
    {
        public OpCode OpCode { get; }
        public int Argument { get; }
        public int LineNumber { get; }

        public Instruction(OpCode opCode, int argument = 0, int lineNumber = 0)
        {
            OpCode = opCode;
            Argument = argument;
            LineNumber = lineNumber;
        }

        public override string ToString()
        {
            return Argument != 0 ? $"{OpCode} {Argument}" : $"{OpCode}";
        }
    }

    // Code object containing bytecode and metadata
    public class CodeObject
    {
        public string Name { get; }
        public string Filename { get; }
        public List<Instruction> Instructions { get; }
        public List<PythonTypeObject> Constants { get; }
        public List<string> Names { get; }
        public List<string> VarNames { get; }
        public int ArgumentCount { get; }
        public Dictionary<int, int> LineNumberTable { get; } // bytecode offset -> line number

        public int KwOnlyArgCount { get; }
        public bool HasVarArgs { get; }
        public bool HasKwArgs { get; }
        public string VarArgsName { get; }
        public string KwArgsName { get; }
        public List<string> DefaultValues { get; }  // 기본값이 있는 파라미터 이름들

        public CodeObject(string name, string filename, List<Instruction> instructions,
                     List<PythonTypeObject> constants, List<string> names, List<string> varNames,
                     int argumentCount = 0, int kwOnlyArgCount = 0,
                     bool hasVarArgs = false, bool hasKwArgs = false,
                     string varArgsName = null, string kwArgsName = null,
                     List<string> defaultValues = null)
        {
            Name = name;
            Filename = filename;
            Instructions = instructions ?? new List<Instruction>();
            Constants = constants ?? new List<PythonTypeObject>();
            Names = names ?? new List<string>();
            VarNames = varNames ?? new List<string>();
            ArgumentCount = argumentCount;
            KwOnlyArgCount = kwOnlyArgCount;
            HasVarArgs = hasVarArgs;
            HasKwArgs = hasKwArgs;
            VarArgsName = varArgsName;
            KwArgsName = kwArgsName;
            DefaultValues = defaultValues ?? new List<string>();
            LineNumberTable = new Dictionary<int, int>();

            for (int i = 0; i < Instructions.Count; i++)
            {
                if (Instructions[i].LineNumber > 0)
                    LineNumberTable[i] = Instructions[i].LineNumber;
            }
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Code Object: {Name} ({Filename})");
            sb.AppendLine($"Arguments: {ArgumentCount}");
            sb.AppendLine($"Constants: [{string.Join(", ", Constants.Select(c => c?.ToPythonString() ?? "None"))}]");
            sb.AppendLine($"Names: [{string.Join(", ", Names)}]");
            sb.AppendLine($"Variables: [{string.Join(", ", VarNames)}]");
            sb.AppendLine("Bytecode:");

            for (int i = 0; i < Instructions.Count; i++)
            {
                var inst = Instructions[i];
                var lineInfo = LineNumberTable.ContainsKey(i) ? $"L{LineNumberTable[i]:D3}" : "   ";
                sb.AppendLine($"  {i:D3} {lineInfo} {inst}");
            }

            return sb.ToString();
        }
    }

    // Wrapper for CodeObject as PythonTypeObject
    public class PythonCodeObject : PythonTypeObject
    {
        public CodeObject Code { get; }

        public PythonCodeObject(CodeObject code)
        {
            Code = code;
        }

        public override PythonType Type => PythonType.Instance;
        public override bool IsTrue() => true;
        public override string ToPythonString() => $"<code object {Code.Name} at {GetHashCode():X}>";
        public override object GetRawValue() => Code;
        public override bool Equals(PythonTypeObject other) => ReferenceEquals(this, other);
    }

    // Stack frame for function calls
    public class Frame
    {
        public CodeObject Code { get; }
        public Environment Locals { get; }
        public Environment Globals { get; }
        public Stack<PythonTypeObject> Stack { get; }
        public int InstructionPointer { get; set; }
        public Frame Previous { get; }

        public Frame(CodeObject code, Environment locals, Environment globals, Frame previous = null)
        {
            Code = code;
            Locals = locals;
            Globals = globals;
            Stack = new Stack<PythonTypeObject>();
            InstructionPointer = 0;
            Previous = previous;
        }
    }

    // Virtual Machine for executing bytecode
    public class VirtualMachine
    {
        private Frame currentFrame;
        private Stack<Frame> frameStack;
        private Environment globalEnv;

        public VirtualMachine(Environment globalEnv)
        {
            this.globalEnv = globalEnv;
            // globalEnv에 builtin이 없으면 추가
            // if (!globalEnv.HasVariable("print"))
            // {
            //     Environment.SetupBuiltins(globalEnv);
            // }
            frameStack = new Stack<Frame>();
        }

        public PythonTypeObject Execute(Environment locals, CodeObject code, string filename = null)
        {
            // 파일명이 제공되면 code object의 filename 오버라이드
            if (!string.IsNullOrEmpty(filename) && filename != "<string>")
            {
                // CodeObject의 Filename을 임시로 변경하거나
                // 에러 발생시 사용할 수 있도록 저장
                var originalFilename = code.Filename;

                // 새로운 CodeObject 생성 (filename만 변경)
                code = new CodeObject(
                    code.Name,
                    filename,  // 새 파일명 사용
                    code.Instructions,
                    code.Constants,
                    code.Names,
                    code.VarNames,
                    code.ArgumentCount
                );
            }

            currentFrame = new Frame(code, locals, globalEnv);
            frameStack.Push(currentFrame);

            try
            {
                return ExecuteFrame();
            }
            finally
            {
                frameStack.Pop();
                currentFrame = frameStack.Count > 0 ? frameStack.Peek() : null;
            }
        }

        private PythonTypeObject ExecuteFrame()
        {
            var frame = currentFrame;
            var code = frame.Code;
            var stack = frame.Stack;

            while (frame.InstructionPointer < code.Instructions.Count)
            {
                var instruction = code.Instructions[frame.InstructionPointer];
                var opCode = instruction.OpCode;
                var arg = instruction.Argument;

                try
                {
                    switch (opCode)
                    {
                        case OpCode.LOAD_CONST:
                            stack.Push(code.Constants[arg]);
                            break;

                        case OpCode.LOAD_NAME:
                            var name = code.Names[arg];
                            PythonTypeObject value = null;

                            // First try locals
                            try
                            {
                                value = frame.Locals.GetVariable(name);
                            }
                            catch (PythonException)
                            {
                                // Then try globals
                                try
                                {
                                    value = frame.Globals.GetVariable(name);
                                }
                                catch (PythonException)
                                {
                                    throw new PythonException("NameError", $"name '{name}' is not defined");
                                }
                            }

                            stack.Push(value);
                            break;

                        case OpCode.STORE_NAME:
                            var storeName = code.Names[arg];
                            frame.Locals.SetVariable(storeName, stack.Pop());
                            break;

                        case OpCode.POP_TOP:
                            stack.Pop();
                            break;

                        case OpCode.DUP_TOP:
                            if (stack.Count > 0)
                            {
                                var top = stack.Peek();
                                stack.Push(top);
                            }
                            break;

                        case OpCode.BINARY_ADD:
                            ExecuteBinaryOp("+");
                            break;
                        case OpCode.BINARY_SUBTRACT:
                            ExecuteBinaryOp("-");
                            break;
                        case OpCode.BINARY_MULTIPLY:
                            ExecuteBinaryOp("*");
                            break;
                        case OpCode.BINARY_DIVIDE:
                            ExecuteBinaryOp("/");
                            break;
                        case OpCode.BINARY_MODULO:
                            ExecuteBinaryOp("%");
                            break;
                        case OpCode.BINARY_POWER:
                            ExecuteBinaryOp("**");
                            break;

                        case OpCode.UNARY_NEGATIVE:
                            var val = stack.Pop();
                            if (NumberHelper.IsNumber(val))
                                stack.Push(NumberHelper.Negate(val));
                            else
                                throw new PythonException("TypeError", "Cannot negate non-number");
                            break;

                        case OpCode.UNARY_NOT:
                            stack.Push(new PythonBool(!stack.Pop().IsTrue()));
                            break;

                        case OpCode.UNPACK_SEQUENCE:
                            ExecuteUnpackSequence(arg);
                            break;

                        case OpCode.COMPARE_EQ:
                            ExecuteCompare("==");
                            break;
                        case OpCode.COMPARE_NE:
                            ExecuteCompare("!=");
                            break;
                        case OpCode.COMPARE_LT:
                            ExecuteCompare("<");
                            break;
                        case OpCode.COMPARE_GT:
                            ExecuteCompare(">");
                            break;
                        case OpCode.COMPARE_LE:
                            ExecuteCompare("<=");
                            break;
                        case OpCode.COMPARE_GE:
                            ExecuteCompare(">=");
                            break;

                        case OpCode.JUMP_FORWARD:
                            frame.InstructionPointer += arg;
                            continue;

                        case OpCode.JUMP_IF_FALSE:
                            if (!stack.Pop().IsTrue())
                            {
                                frame.InstructionPointer = arg;
                                continue;
                            }
                            break;

                        case OpCode.JUMP_IF_TRUE:
                            if (stack.Pop().IsTrue())
                            {
                                frame.InstructionPointer = arg;
                                continue;
                            }
                            break;

                        case OpCode.JUMP_ABSOLUTE:
                            frame.InstructionPointer = arg;
                            continue;

                        case OpCode.CALL_FUNCTION:
                            ExecuteFunctionCall(arg, false);
                            break;

                        case OpCode.CALL_FUNCTION_KW:
                            ExecuteFunctionCallWithKeywords(arg);
                            break;

                        case OpCode.BUILD_MAP:
                            ExecuteBuildMap(arg);
                            break;

                        case OpCode.RETURN_VALUE:
                            return stack.Count > 0 ? stack.Pop() : PythonNone.Instance;

                        case OpCode.BUILD_LIST:
                            ExecuteBuildList(arg);
                            break;

                        case OpCode.BUILD_TUPLE:
                            ExecuteBuildTuple(arg);
                            break;

                        case OpCode.BUILD_DICT:
                            ExecuteBuildDict(arg);
                            break;

                        case OpCode.MAKE_FUNCTION:
                            ExecuteMakeFunction(arg);
                            break;

                        case OpCode.GET_ITER:
                            ExecuteGetIter();
                            break;

                        case OpCode.FOR_ITER:
                            if (!ExecuteForIter(arg))
                            {
                                frame.InstructionPointer = arg;
                                continue;
                            }
                            break;

                        case OpCode.LOAD_ATTR:
                            ExecuteLoadAttr(arg);
                            break;

                        case OpCode.STORE_ATTR:
                            ExecuteStoreAttr(arg);
                            break;

                        case OpCode.STORE_INDEX:
                            ExecuteStoreIndex();
                            break;

                        case OpCode.DUP_TOP_TWO:
                            ExecuteDupTopTwo();
                            break;

                        case OpCode.BUILD_SLICE:
                            ExecuteBuildSlice(arg);
                            break;
                        case OpCode.IMPORT_NAME:
                            ExecuteImportName();
                            break;

                        case OpCode.IMPORT_FROM:
                            ExecuteImportFrom();
                            break;

                        case OpCode.LOAD_INDEX:
                            ExecuteLoadIndex();
                            break;

                        case OpCode.RAISE_VARARGS:
                            ExecuteRaise(arg);
                            break;

                        case OpCode.BREAK_LOOP:
                        case OpCode.CONTINUE_LOOP:
                            // These would be handled by loop compilation
                            break;

                        case OpCode.NOP:
                            break;

                        default:
                            throw new PythonException("RuntimeError", $"Unknown opcode: {opCode}");
                    }

                    frame.InstructionPointer++;
                }
                catch (PythonException ex)
                {
                    // Add line number information if not present
                    if (ex.Line == 0 && code.LineNumberTable.ContainsKey(frame.InstructionPointer))
                    {
                        var lineNum = code.LineNumberTable[frame.InstructionPointer];
                        throw new PythonException(ex.Type, ex.Message, lineNum, 0, code.Filename);
                    }
                    throw;
                }
            }

            return stack.Count > 0 ? stack.Pop() : PythonNone.Instance;
        }

        private void ExecuteBinaryOp(string op)
        {
            var right = currentFrame.Stack.Pop();
            var left = currentFrame.Stack.Pop();

            PythonTypeObject result = op switch
            {
                "+" => AddValues(left, right),
                "-" => SubtractValues(left, right),
                "*" => MultiplyValues(left, right),
                "/" => DivideValues(left, right),
                "%" => ModuloValues(left, right),
                "**" => PowerValues(left, right),
                _ => throw new PythonException("RuntimeError", $"Unknown binary operator: {op}")
            };

            currentFrame.Stack.Push(result);
        }

        private PythonTypeObject AddValues(PythonTypeObject left, PythonTypeObject right)
        {
            if (left is PythonInt li) return li.Add(right);
            if (left is PythonFloat lf) return lf.Add(right);
            if (left is PythonString ls) return ls.Add(right);
            if (left is PythonList ll && right is PythonList rl)
            {
                var newList = new PythonList();
                newList.Items.AddRange(ll.Items);
                newList.Items.AddRange(rl.Items);
                return newList;
            }
            throw new PythonException("TypeError", "Unsupported operand types for +");
        }

        private PythonTypeObject SubtractValues(PythonTypeObject left, PythonTypeObject right)
        {
            if (left is PythonInt li) return li.Subtract(right);
            if (left is PythonFloat lf) return lf.Subtract(right);
            throw new PythonException("TypeError", "Unsupported operand types for -");
        }

        private PythonTypeObject MultiplyValues(PythonTypeObject left, PythonTypeObject right)
        {
            if (left is PythonInt li) return li.Multiply(right);
            if (left is PythonFloat lf) return lf.Multiply(right);
            if (left is PythonString ls && NumberHelper.IsNumber(right))
                return ls.Repeat(NumberHelper.ToInt(right));
            throw new PythonException("TypeError", "Unsupported operand types for *");
        }

        private PythonTypeObject DivideValues(PythonTypeObject left, PythonTypeObject right)
        {
            if (left is PythonInt li) return li.Divide(right);
            if (left is PythonFloat lf) return lf.Divide(right);
            throw new PythonException("TypeError", "Unsupported operand types for /");
        }

        private PythonTypeObject ModuloValues(PythonTypeObject left, PythonTypeObject right)
        {
            if (left is PythonInt li) return li.Modulo(right);
            if (left is PythonFloat lf) return lf.Modulo(right);
            throw new PythonException("TypeError", "Unsupported operand types for %");
        }

        private PythonTypeObject PowerValues(PythonTypeObject left, PythonTypeObject right)
        {
            if (left is PythonInt li) return li.Power(right);
            if (left is PythonFloat lf) return lf.Power(right);
            throw new PythonException("TypeError", "Unsupported operand types for **");
        }

        private void ExecuteCompare(string op)
        {
            var right = currentFrame.Stack.Pop();
            var left = currentFrame.Stack.Pop();

            bool result = op switch
            {
                "==" => left.Equals(right),
                "!=" => !left.Equals(right),
                "<" => CompareValues(left, right) < 0,
                ">" => CompareValues(left, right) > 0,
                "<=" => CompareValues(left, right) <= 0,
                ">=" => CompareValues(left, right) >= 0,
                _ => throw new PythonException("RuntimeError", $"Unknown comparison operator: {op}")
            };

            currentFrame.Stack.Push(new PythonBool(result));
        }

        private int CompareValues(PythonTypeObject left, PythonTypeObject right)
        {
            if (NumberHelper.IsNumber(left) && NumberHelper.IsNumber(right))
                return NumberHelper.ToDouble(left).CompareTo(NumberHelper.ToDouble(right));
            if (left is PythonString ls && right is PythonString rs)
                return string.Compare(ls.Value, rs.Value);

            // 타입 정보를 제대로 출력하도록 수정
            string leftType = left?.Type.ToString() ?? "None";
            string rightType = right?.Type.ToString() ?? "None";
            throw new PythonException("TypeError", $"'<' not supported between instances of '{leftType}' and '{rightType}'");
        }

        private void ExecuteFunctionCall(int argCount, bool hasKeywords = false)
        {
            var args = new List<PythonTypeObject>();
            Dictionary<string, PythonTypeObject> kwargs = null;

            if (hasKeywords)
            {
                // 스택에서 키워드 인자 딕셔너리 가져오기
                var kwDict = currentFrame.Stack.Pop() as PythonDict;
                if (kwDict != null)
                {
                    kwargs = new Dictionary<string, PythonTypeObject>();
                    foreach (var kvp in kwDict.Items)
                    {
                        if (kvp.Key is PythonString key)
                            kwargs[key.Value] = kvp.Value;
                    }
                }
            }

            // 위치 인자 가져오기
            for (int i = 0; i < argCount; i++)
                args.Insert(0, currentFrame.Stack.Pop());

            var function = currentFrame.Stack.Pop();

            PythonTypeObject result = function switch
            {
                BytecodeFunction bytecodeFunc => bytecodeFunc.CallWithKeywords(args, kwargs),
                BuiltinFunction builtinFunc => builtinFunc.Call(args),
                PythonClass pythonClass => pythonClass.CreateInstance(args),
                _ => throw new PythonException("TypeError", $"'{function?.Type}' object is not callable")
            };

            currentFrame.Stack.Push(result);
        }

        private void ExecuteFunctionCallWithKeywords(int argCount)
        {
            ExecuteFunctionCall(argCount, true);
        }

        private void ExecuteBuildMap(int count)
        {
            var dict = new PythonDict();
            for (int i = 0; i < count; i++)
            {
                var value = currentFrame.Stack.Pop();
                var key = currentFrame.Stack.Pop();
                dict.Items[key] = value;
            }
            currentFrame.Stack.Push(dict);
        }

        private void ExecuteBuildList(int count)
        {
            var list = new PythonList();
            for (int i = 0; i < count; i++)
                list.Items.Insert(0, currentFrame.Stack.Pop());
            currentFrame.Stack.Push(list);
        }

        private void ExecuteBuildTuple(int count)
        {
            var tuple = new PythonTuple();
            for (int i = 0; i < count; i++)
                tuple.Items.Insert(0, currentFrame.Stack.Pop());
            currentFrame.Stack.Push(tuple);
        }

        private void ExecuteBuildDict(int count)
        {
            var dict = new PythonDict();
            for (int i = 0; i < count; i++)
            {
                var value = currentFrame.Stack.Pop();
                var key = currentFrame.Stack.Pop();
                dict.Items[key] = value;
            }
            currentFrame.Stack.Push(dict);
        }

        private void ExecuteMakeFunction(int arg)
        {
            // arg의 하위 8비트는 일반 인자 수, 상위 8비트는 기본값 수
            int argCount = arg & 0xFF;
            int defaultCount = (arg >> 8) & 0xFF;

            // 스택에서 CodeObject 가져오기 (기본값보다 먼저)
            var codeObjectWrapper = currentFrame.Stack.Pop() as PythonCodeObject;
            if (codeObjectWrapper == null)
                throw new PythonException("TypeError", "MAKE_FUNCTION expects CodeObject");

            var codeObject = codeObjectWrapper.Code;

            // 스택에서 기본값들 가져오기 (역순)
            var defaultValues = new List<PythonTypeObject>();
            for (int i = 0; i < defaultCount; i++)
            {
                defaultValues.Insert(0, currentFrame.Stack.Pop());
            }

            // 기본값이 있으면 BytecodeFunctionWithDefaults, 없으면 BytecodeFunction
            PythonTypeObject function;
            if (defaultCount > 0)
            {
                function = new BytecodeFunctionWithDefaults(codeObject, currentFrame.Locals, defaultValues);
            }
            else
            {
                function = new BytecodeFunction(codeObject, currentFrame.Locals);
            }

            currentFrame.Stack.Push(function);
        }

        private void ExecuteGetIter()
        {
            var obj = currentFrame.Stack.Pop();

            // Convert object to iterator (simplified)
            if (obj is PythonList list)
            {
                currentFrame.Stack.Push(new ListIterator(list));
            }
            else if (obj is PythonTuple tuple)
            {
                currentFrame.Stack.Push(new TupleIterator(tuple));
            }
            else if (obj is PythonString str)
            {
                currentFrame.Stack.Push(new StringIterator(str));
            }
            else
            {
                throw new PythonException("TypeError", $"'{obj?.Type}' object is not iterable");
            }
        }

        private bool ExecuteForIter(int jumpTarget)
        {
            var iterator = currentFrame.Stack.Peek();

            if (iterator is IIterator iter)
            {
                if (iter.HasNext())
                {
                    var nextValue = iter.Next();
                    currentFrame.Stack.Push(nextValue);
                    return true; // Continue loop
                }
                else
                {
                    currentFrame.Stack.Pop(); // Remove iterator
                    return false; // Exit loop, jump to target
                }
            }

            throw new PythonException("TypeError", "FOR_ITER expects iterator");
        }

        private void ExecuteImportName()
        {
            var moduleName = currentFrame.Stack.Pop() as PythonString;
            if (moduleName == null)
                throw new PythonException("TypeError", "IMPORT_NAME expects string");

            try
            {
                // 현재 프레임의 Globals 환경과 SearchPaths를 전달
                var searchPaths = currentFrame.Globals.SearchPaths ?? new List<string> { "." };
                var module = ModuleSystem.ImportModule(currentFrame.Globals, moduleName.Value, searchPaths);
                currentFrame.Stack.Push(module);
            }
            catch (Exception ex)
            {
                throw new PythonException("ImportError", $"Failed to import module '{moduleName.Value}': {ex.Message}");
            }
        }

        private void ExecuteImportFrom()
        {
            var itemName = currentFrame.Stack.Pop() as PythonString;
            var module = currentFrame.Stack.Pop() as PythonModule;

            if (itemName == null || module == null)
                throw new PythonException("TypeError", "IMPORT_FROM expects string and module");

            try
            {
                var value = module.GetAttribute(itemName.Value);
                currentFrame.Stack.Push(value);
            }
            catch (Exception)
            {
                throw new PythonException("ImportError", $"cannot import name '{itemName.Value}' from module '{module.Name}'");
            }
        }

        private void ExecuteLoadIndex()
        {
            var index = currentFrame.Stack.Pop();
            var obj = currentFrame.Stack.Pop();

            if (obj is PythonList list && NumberHelper.IsNumber(index))
            {
                int i = NumberHelper.ToInt(index);
                currentFrame.Stack.Push(list.GetItem(i));
                return;
            }
            else if (obj is PythonTuple tuple && NumberHelper.IsNumber(index))
            {
                int i = NumberHelper.ToInt(index);
                currentFrame.Stack.Push(tuple.GetItem(i));
                return;
            }
            else if (obj is PythonDict dict)
            {
                currentFrame.Stack.Push(dict.GetItem(index));
                return;
            }
            else if (obj is PythonString str && NumberHelper.IsNumber(index))
            {
                int i = NumberHelper.ToInt(index);
                currentFrame.Stack.Push(str.GetItem(i));
                return;
            }

            throw new PythonException("TypeError", "object is not subscriptable");
        }

        private void ExecuteRaise(int argCount)
        {
            if (argCount == 1)
            {
                var exception = currentFrame.Stack.Pop();
                if (exception is PythonString message)
                    throw new PythonException("Exception", message.Value);
                throw new PythonException("Exception", exception?.ToPythonString() ?? "");
            }
            else
            {
                throw new PythonException("Exception", "Exception raised");
            }
        }

        private void ExecuteUnpackSequence(int count)
        {
            var sequence = currentFrame.Stack.Pop();

            List<PythonTypeObject> items = sequence switch
            {
                PythonList list => list.Items,
                PythonTuple tuple => tuple.Items,
                PythonString str => str.Value.Select(c => new PythonString(c.ToString()) as PythonTypeObject).ToList(),
                _ => throw new PythonException("TypeError", $"cannot unpack non-sequence {sequence?.Type}")
            };

            if (items.Count != count)
            {
                throw new PythonException("ValueError",
                    $"too many values to unpack (expected {count}, got {items.Count})");
            }

            // 스택에 역순으로 푸시 (왼쪽부터 오른쪽 순서로)
            foreach (var item in items)
            {
                currentFrame.Stack.Push(item);
            }
        }

        private void ExecuteLoadAttr(int nameIndex)
        {
            var obj = currentFrame.Stack.Pop();
            var attrName = currentFrame.Code.Names[nameIndex];

            PythonTypeObject result = obj switch
            {
                PythonInstance instance => instance.GetAttribute(attrName),
                PythonModule module => module.GetAttribute(attrName),
                PythonClass cls => cls.GetAttribute(attrName),
                PythonList list => list.GetMethod(attrName),
                PythonDict dict => dict.GetMethod(attrName),
                PythonString str => str.GetMethod(attrName),
                FileObject file => file.GetMethod(attrName),
                Function func => func.GetAttribute(attrName),
                _ => throw new PythonException("AttributeError",
                    $"'{obj?.Type}' object has no attribute '{attrName}'")
            };

            currentFrame.Stack.Push(result);
        }

        private void ExecuteStoreAttr(int nameIndex)
        {
            var value = currentFrame.Stack.Pop();
            var obj = currentFrame.Stack.Pop();
            var attrName = currentFrame.Code.Names[nameIndex];

            if (obj is PythonInstance instance)
            {
                instance.SetAttribute(attrName, value);
            }
            else
            {
                throw new PythonException("AttributeError",
                    $"'{obj?.Type}' object has no attribute '{attrName}'");
            }
        }

        private void ExecuteStoreIndex()
        {
            var value = currentFrame.Stack.Pop();
            var index = currentFrame.Stack.Pop();
            var obj = currentFrame.Stack.Pop();

            switch (obj)
            {
                case PythonList list when NumberHelper.IsNumber(index):
                    list.SetItem(NumberHelper.ToInt(index), value);
                    break;
                case PythonDict dict:
                    dict.SetItem(index, value);
                    break;
                default:
                    throw new PythonException("TypeError",
                        $"'{obj?.Type}' object does not support item assignment");
            }
        }

        private void ExecuteDupTopTwo()
        {
            if (currentFrame.Stack.Count >= 2)
            {
                var top = currentFrame.Stack.Pop();
                var second = currentFrame.Stack.Pop();

                // Push back in order: original pair, then duplicate
                currentFrame.Stack.Push(second);
                currentFrame.Stack.Push(top);
                currentFrame.Stack.Push(second);
                currentFrame.Stack.Push(top);
            }
            else
            {
                throw new PythonException("RuntimeError", "Stack underflow in DUP_TOP_TWO");
            }
        }

        private void ExecuteBuildSlice(int count)
        {
            PythonTypeObject step = null;
            PythonTypeObject stop = null;
            PythonTypeObject start = null;

            if (count >= 3)
                step = currentFrame.Stack.Pop();
            if (count >= 2)
                stop = currentFrame.Stack.Pop();
            if (count >= 1)
                start = currentFrame.Stack.Pop();

            // 슬라이스 객체 생성 (간단한 튜플로 대체)
            var slice = new PythonTuple();
            slice.Items.Add(start ?? PythonNone.Instance);
            slice.Items.Add(stop ?? PythonNone.Instance);
            slice.Items.Add(step ?? PythonNone.Instance);

            currentFrame.Stack.Push(slice);
        }
    }

    // Simple iterator interface for bytecode VM
    public interface IIterator
    {
        bool HasNext();
        PythonTypeObject Next();
    }

    // Base iterator class that inherits from PythonTypeObject
    public abstract class BaseIterator : PythonTypeObject, IIterator
    {
        public override PythonType Type => PythonType.Instance;
        public override bool IsTrue() => true;
        public override string ToPythonString() => "<iterator>";
        public override object GetRawValue() => this;
        public override bool Equals(PythonTypeObject other) => ReferenceEquals(this, other);

        public abstract bool HasNext();
        public abstract PythonTypeObject Next();
    }

    public class ListIterator : BaseIterator
    {
        private readonly PythonList list;
        private int index = 0;

        public ListIterator(PythonList list) => this.list = list;

        public override bool HasNext() => index < list.Items.Count;
        public override PythonTypeObject Next() => HasNext() ? list.Items[index++] : PythonNone.Instance;
    }

    public class TupleIterator : BaseIterator
    {
        private readonly PythonTuple tuple;
        private int index = 0;

        public TupleIterator(PythonTuple tuple) => this.tuple = tuple;

        public override bool HasNext() => index < tuple.Items.Count;
        public override PythonTypeObject Next() => HasNext() ? tuple.Items[index++] : PythonNone.Instance;
    }

    public class StringIterator : BaseIterator
    {
        private readonly PythonString str;
        private int index = 0;

        public StringIterator(PythonString str) => this.str = str;

        public override bool HasNext() => index < str.Value.Length;
        public override PythonTypeObject Next() => HasNext() ? new PythonString(str.Value[index++].ToString()) : PythonNone.Instance;
    }

    // Bytecode-based function (for lambda compiled to bytecode)
    // bytecode_system.cs의 BytecodeFunction 클래스 수정

    public class BytecodeFunction : Function
    {
        protected readonly CodeObject code;
        protected readonly Environment closure;

        public BytecodeFunction(CodeObject code, Environment closure) : base(code.Name)
        {
            this.code = code;
            this.closure = closure;
        }

        public virtual PythonTypeObject CallWithKeywords(List<PythonTypeObject> args, Dictionary<string, PythonTypeObject> kwargs)
        {
            var funcEnv = new Environment(closure);
            int normalArgCount = code.ArgumentCount;
            int providedPosArgs = args.Count;

            // 파라미터별 값 할당
            var paramValues = new PythonTypeObject[normalArgCount];
            var paramAssigned = new bool[normalArgCount];

            // 위치 인자 할당
            int posArgsToAssign = Math.Min(providedPosArgs, normalArgCount);
            for (int i = 0; i < posArgsToAssign; i++)
            {
                paramValues[i] = args[i];
                paramAssigned[i] = true;
            }

            // 키워드 인자 할당
            if (kwargs != null)
            {
                foreach (var kvp in kwargs)
                {
                    // VarNames에서 파라미터 찾기 (일반 파라미터만)
                    int paramIndex = -1;
                    for (int i = 0; i < normalArgCount && i < code.VarNames.Count; i++)
                    {
                        if (code.VarNames[i] == kvp.Key)
                        {
                            paramIndex = i;
                            break;
                        }
                    }

                    if (paramIndex >= 0)
                    {
                        if (paramAssigned[paramIndex])
                        {
                            throw new PythonException("TypeError",
                                $"{code.Name}() got multiple values for argument '{kvp.Key}'");
                        }
                        paramValues[paramIndex] = kvp.Value;
                        paramAssigned[paramIndex] = true;
                    }
                    else if (!code.HasKwArgs)
                    {
                        throw new PythonException("TypeError",
                            $"{code.Name}() got an unexpected keyword argument '{kvp.Key}'");
                    }
                }
            }

            // 모든 필수 파라미터가 할당되었는지 확인
            for (int i = 0; i < normalArgCount; i++)
            {
                if (!paramAssigned[i])
                {
                    throw new PythonException("TypeError",
                        $"{code.Name}() missing required positional argument: '{code.VarNames[i]}'");
                }
                funcEnv.SetVariable(code.VarNames[i], paramValues[i]);
            }

            // *args 처리 - 중요: VarArgsName을 사용
            if (code.HasVarArgs && !string.IsNullOrEmpty(code.VarArgsName))
            {
                var varArgsList = new PythonList();
                for (int i = normalArgCount; i < providedPosArgs; i++)
                {
                    varArgsList.Items.Add(args[i]);
                }
                // VarArgsName으로 환경에 설정
                funcEnv.SetVariable(code.VarArgsName, varArgsList);
            }
            else if (providedPosArgs > normalArgCount)
            {
                throw new PythonException("TypeError",
                    $"{code.Name}() takes {normalArgCount} positional arguments but {providedPosArgs} were given");
            }

            // **kwargs 처리
            if (code.HasKwArgs && !string.IsNullOrEmpty(code.KwArgsName))
            {
                var kwArgsDict = new PythonDict();
                if (kwargs != null)
                {
                    foreach (var kvp in kwargs)
                    {
                        bool isNormalParam = false;
                        for (int i = 0; i < normalArgCount && i < code.VarNames.Count; i++)
                        {
                            if (code.VarNames[i] == kvp.Key)
                            {
                                isNormalParam = true;
                                break;
                            }
                        }

                        if (!isNormalParam)
                        {
                            kwArgsDict.Items[new PythonString(kvp.Key)] = kvp.Value;
                        }
                    }
                }
                // KwArgsName으로 환경에 설정
                funcEnv.SetVariable(code.KwArgsName, kwArgsDict);
            }

            var globalEnv = closure;
            while (globalEnv.parent != null)
                globalEnv = globalEnv.parent;

            var vm = new VirtualMachine(globalEnv);
            return vm.Execute(funcEnv, code);
        }

        public override PythonTypeObject Call(List<PythonTypeObject> arguments)
        {
            return CallWithKeywords(arguments, null);
        }
    }

    public class BytecodeFunctionWithDefaults : BytecodeFunction
    {
        private readonly List<PythonTypeObject> defaultValues;
        private readonly CodeObject code;
        private readonly Environment closure;

        public BytecodeFunctionWithDefaults(CodeObject code, Environment closure, List<PythonTypeObject> defaults)
            : base(code, closure)
        {
            this.code = code;
            this.closure = closure;
            this.defaultValues = defaults ?? new List<PythonTypeObject>();
        }

        public override PythonTypeObject CallWithKeywords(List<PythonTypeObject> args, Dictionary<string, PythonTypeObject> kwargs)
        {
            var funcEnv = new Environment(closure);

            int normalArgCount = code.ArgumentCount;
            int providedPosArgs = args.Count;
            int requiredArgs = normalArgCount - defaultValues.Count;

            // 파라미터별 값 할당 추적
            var paramValues = new PythonTypeObject[normalArgCount];
            var paramAssigned = new bool[normalArgCount];

            // 1. 위치 인자 먼저 할당 (normalArgCount까지만, 나머지는 *args로)
            int posArgsToAssign = Math.Min(providedPosArgs, normalArgCount);
            for (int i = 0; i < posArgsToAssign; i++)
            {
                paramValues[i] = args[i];
                paramAssigned[i] = true;
            }

            // 2. 키워드 인자로 나머지 파라미터 채우기
            if (kwargs != null)
            {
                foreach (var kvp in kwargs)
                {
                    // 파라미터 이름으로 인덱스 찾기
                    int paramIndex = -1;
                    for (int i = 0; i < code.VarNames.Count && i < normalArgCount; i++)
                    {
                        if (code.VarNames[i] == kvp.Key)
                        {
                            paramIndex = i;
                            break;
                        }
                    }

                    if (paramIndex >= 0 && paramIndex < normalArgCount)
                    {
                        if (paramAssigned[paramIndex])
                        {
                            throw new PythonException("TypeError",
                                $"{code.Name}() got multiple values for argument '{kvp.Key}'");
                        }
                        paramValues[paramIndex] = kvp.Value;
                        paramAssigned[paramIndex] = true;
                    }
                    else if (!code.HasKwArgs)
                    {
                        throw new PythonException("TypeError",
                            $"{code.Name}() got an unexpected keyword argument '{kvp.Key}'");
                    }
                    // HasKwArgs가 true면 나중에 **kwargs로 처리
                }
            }

            // 3. 기본값으로 나머지 채우기
            for (int i = 0; i < normalArgCount; i++)
            {
                if (!paramAssigned[i])
                {
                    // 기본값이 있는지 확인
                    int defaultIndex = i - requiredArgs;
                    if (defaultIndex >= 0 && defaultIndex < defaultValues.Count)
                    {
                        paramValues[i] = defaultValues[defaultIndex];
                        paramAssigned[i] = true;
                    }
                }
            }

            // 4. 모든 필수 인자가 채워졌는지 확인
            var missingParams = new List<string>();
            for (int i = 0; i < requiredArgs; i++)  // 필수 인자만 체크
            {
                if (!paramAssigned[i])
                {
                    missingParams.Add(code.VarNames[i]);
                }
            }

            if (missingParams.Count > 0)
            {
                string missingList = string.Join(", ", missingParams.Select(p => $"'{p}'"));
                throw new PythonException("TypeError",
                    $"{code.Name}() missing {missingParams.Count} required positional argument(s): {missingList}");
            }

            // 5. 환경에 파라미터 값 설정
            for (int i = 0; i < normalArgCount; i++)
            {
                if (paramAssigned[i])  // 할당된 것만 설정
                {
                    funcEnv.SetVariable(code.VarNames[i], paramValues[i]);
                }
            }

            // 6. *args 처리
            if (code.HasVarArgs && !string.IsNullOrEmpty(code.VarArgsName))
            {
                var varArgsList = new PythonList();
                // normalArgCount 이후의 모든 위치 인자를 *args에 추가
                for (int i = normalArgCount; i < providedPosArgs; i++)
                {
                    varArgsList.Items.Add(args[i]);
                }
                funcEnv.SetVariable(code.VarArgsName, varArgsList);
            }
            else if (providedPosArgs > normalArgCount)
            {
                // *args가 없는데 추가 위치 인자가 있으면 에러
                throw new PythonException("TypeError",
                    $"{code.Name}() takes {normalArgCount} positional arguments but {providedPosArgs} were given");
            }

            // 7. **kwargs 처리
            if (code.HasKwArgs && !string.IsNullOrEmpty(code.KwArgsName))
            {
                var kwArgsDict = new PythonDict();
                if (kwargs != null)
                {
                    foreach (var kvp in kwargs)
                    {
                        // 일반 파라미터가 아닌 것만 kwargs에 추가
                        bool isNormalParam = false;
                        for (int i = 0; i < normalArgCount; i++)
                        {
                            if (code.VarNames[i] == kvp.Key)
                            {
                                isNormalParam = true;
                                break;
                            }
                        }

                        if (!isNormalParam)
                        {
                            kwArgsDict.Items[new PythonString(kvp.Key)] = kvp.Value;
                        }
                    }
                }
                funcEnv.SetVariable(code.KwArgsName, kwArgsDict);
            }

            // 전역 환경 찾기
            var globalEnv = closure;
            while (globalEnv.parent != null)
                globalEnv = globalEnv.parent;

            var vm = new VirtualMachine(globalEnv);
            return vm.Execute(funcEnv, code);
        }
    }
}