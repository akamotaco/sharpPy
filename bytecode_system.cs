// bytecode_system.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

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

        // Collection operations
        BUILD_LIST = 60,
        BUILD_TUPLE = 61,
        BUILD_DICT = 62,
        LOAD_INDEX = 63,
        STORE_INDEX = 64,
        LOAD_ATTR = 65,
        STORE_ATTR = 66,

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
        public List<object> Constants { get; }
        public List<string> Names { get; }
        public List<string> VarNames { get; }
        public int ArgumentCount { get; }
        public Dictionary<int, int> LineNumberTable { get; } // bytecode offset -> line number

        public CodeObject(string name, string filename, List<Instruction> instructions, 
                         List<object> constants, List<string> names, List<string> varNames,
                         int argumentCount = 0)
        {
            Name = name;
            Filename = filename;
            Instructions = instructions ?? new List<Instruction>();
            Constants = constants ?? new List<object>();
            Names = names ?? new List<string>();
            VarNames = varNames ?? new List<string>();
            ArgumentCount = argumentCount;
            LineNumberTable = new Dictionary<int, int>();

            // Build line number table
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
            sb.AppendLine($"Constants: [{string.Join(", ", Constants.Select(c => c?.ToString() ?? "None"))}]");
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

    // Stack frame for function calls
    public class Frame
    {
        public CodeObject Code { get; }
        public Environment Locals { get; }
        public Environment Globals { get; }
        public Stack<object> Stack { get; }
        public int InstructionPointer { get; set; }
        public Frame Previous { get; }

        public Frame(CodeObject code, Environment locals, Environment globals, Frame previous = null)
        {
            Code = code;
            Locals = locals;
            Globals = globals;
            Stack = new Stack<object>();
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
            frameStack = new Stack<Frame>();
        }

        public object Execute(CodeObject code, Environment locals = null)
        {
            locals = locals ?? new Environment(globalEnv);
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

        private object ExecuteFrame()
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
                            try
                            {
                                stack.Push(frame.Locals.GetVariable(name));
                            }
                            catch (PythonException)
                            {
                                stack.Push(frame.Globals.GetVariable(name));
                            }
                            break;

                        case OpCode.STORE_NAME:
                            var storeName = code.Names[arg];
                            frame.Locals.SetVariable(storeName, stack.Pop());
                            break;

                        case OpCode.POP_TOP:
                            stack.Pop();
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
                            stack.Push(!IsTrue(stack.Pop()));
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
                            if (!IsTrue(stack.Pop()))
                            {
                                frame.InstructionPointer = arg;
                                continue;
                            }
                            break;

                        case OpCode.JUMP_IF_TRUE:
                            if (IsTrue(stack.Pop()))
                            {
                                frame.InstructionPointer = arg;
                                continue;
                            }
                            break;

                        case OpCode.JUMP_ABSOLUTE:
                            frame.InstructionPointer = arg;
                            continue;

                        case OpCode.CALL_FUNCTION:
                            ExecuteFunctionCall(arg);
                            break;

                        case OpCode.RETURN_VALUE:
                            return stack.Count > 0 ? stack.Pop() : null;

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

            return stack.Count > 0 ? stack.Pop() : null;
        }

        private void ExecuteBinaryOp(string op)
        {
            var right = currentFrame.Stack.Pop();
            var left = currentFrame.Stack.Pop();
            
            var binOpNode = new BinaryOpNode(null, op, null);
            // Use reflection to call the private methods or implement the logic here
            object result = op switch
            {
                "+" => AddValues(left, right),
                "-" => NumberHelper.IsNumber(left) && NumberHelper.IsNumber(right) ? NumberHelper.Subtract(left, right) : throw new PythonException("TypeError", "Unsupported operand types"),
                "*" => MultiplyValues(left, right),
                "/" => NumberHelper.IsNumber(left) && NumberHelper.IsNumber(right) ? NumberHelper.Divide(left, right) : throw new PythonException("TypeError", "Unsupported operand types"),
                "%" => NumberHelper.IsNumber(left) && NumberHelper.IsNumber(right) ? NumberHelper.Modulo(left, right) : throw new PythonException("TypeError", "Unsupported operand types"),
                "**" => NumberHelper.IsNumber(left) && NumberHelper.IsNumber(right) ? NumberHelper.Power(left, right) : throw new PythonException("TypeError", "Unsupported operand types"),
                _ => throw new PythonException("RuntimeError", $"Unknown binary operator: {op}")
            };
            
            currentFrame.Stack.Push(result);
        }

        private object AddValues(object left, object right)
        {
            if (NumberHelper.IsNumber(left) && NumberHelper.IsNumber(right))
                return NumberHelper.Add(left, right);
            if (left is string || right is string) 
                return left?.ToString() + right?.ToString();
            if (left is PythonList ll && right is PythonList rl)
            {
                var newList = new PythonList();
                newList.Items.AddRange(ll.Items);
                newList.Items.AddRange(rl.Items);
                return newList;
            }
            throw new PythonException("TypeError", "Unsupported operand types for +");
        }

        private object MultiplyValues(object left, object right)
        {
            if (NumberHelper.IsNumber(left) && NumberHelper.IsNumber(right))
                return NumberHelper.Multiply(left, right);
            if (left is string s && NumberHelper.IsNumber(right))
                return string.Concat(Enumerable.Repeat(s, NumberHelper.ToInt(right)));
            if (NumberHelper.IsNumber(left) && right is string s2)
                return string.Concat(Enumerable.Repeat(s2, NumberHelper.ToInt(left)));
            throw new PythonException("TypeError", "Unsupported operand types for *");
        }

        private void ExecuteCompare(string op)
        {
            var right = currentFrame.Stack.Pop();
            var left = currentFrame.Stack.Pop();
            
            bool result = op switch
            {
                "==" => Equals(left, right),
                "!=" => !Equals(left, right),
                "<" => CompareValues(left, right) < 0,
                ">" => CompareValues(left, right) > 0,
                "<=" => CompareValues(left, right) <= 0,
                ">=" => CompareValues(left, right) >= 0,
                _ => throw new PythonException("RuntimeError", $"Unknown comparison operator: {op}")
            };
            
            currentFrame.Stack.Push(result);
        }

        private int CompareValues(object left, object right)
        {
            if (NumberHelper.IsNumber(left) && NumberHelper.IsNumber(right))
                return NumberHelper.ToDouble(left).CompareTo(NumberHelper.ToDouble(right));
            if (left is string ls && right is string rs) 
                return string.Compare(ls, rs);
            throw new PythonException("TypeError", $"'<' not supported between instances");
        }

        private void ExecuteFunctionCall(int argCount)
        {
            var args = new List<object>();
            for (int i = 0; i < argCount; i++)
                args.Insert(0, currentFrame.Stack.Pop());
            
            var function = currentFrame.Stack.Pop();
            
            object result = function switch
            {
                UserFunction userFunc => userFunc.Call(args),
                LambdaFunction lambdaFunc => lambdaFunc.Call(args),
                BytecodeFunction bytecodeFunc => bytecodeFunc.Call(args),
                BuiltinFunction builtinFunc => builtinFunc.Call(args),
                BoundMethod boundMethod => boundMethod.Call(args),
                PythonClass pythonClass => pythonClass.CreateInstance(args),
                _ => throw new PythonException("TypeError", $"'{function?.GetType()?.Name ?? "null"}' object is not callable")
            };
            
            currentFrame.Stack.Push(result);
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

        private void ExecuteMakeFunction(int argCount)
        {
            var codeObject = currentFrame.Stack.Pop() as CodeObject;
            if (codeObject == null)
                throw new PythonException("TypeError", "MAKE_FUNCTION expects CodeObject");

            // For now, create a simple function wrapper
            // In a full implementation, we'd handle defaults, closure, etc.
            var function = new BytecodeFunction(codeObject, currentFrame.Locals);
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
            else if (obj is string str)
            {
                currentFrame.Stack.Push(new StringIterator(str));
            }
            else
            {
                throw new PythonException("TypeError", $"'{obj?.GetType()}' object is not iterable");
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

        private bool IsTrue(object obj)
        {
            if (obj == null) return false;
            if (obj is bool b) return b;
            if (obj is int i) return i != 0;
            if (obj is double d) return d != 0;
            if (obj is string s) return !string.IsNullOrEmpty(s);
            if (obj is PythonList l) return l.Items.Count > 0;
            if (obj is PythonTuple t) return t.Items.Count > 0;
            if (obj is PythonDict dict) return dict.Items.Count > 0;
            return true;
        }
    }

    // Simple iterator interface for bytecode VM
    public interface IIterator
    {
        bool HasNext();
        object Next();
    }

    public class ListIterator : IIterator
    {
        private readonly PythonList list;
        private int index = 0;

        public ListIterator(PythonList list) => this.list = list;

        public bool HasNext() => index < list.Items.Count;
        public object Next() => HasNext() ? list.Items[index++] : null;
    }

    public class TupleIterator : IIterator
    {
        private readonly PythonTuple tuple;
        private int index = 0;

        public TupleIterator(PythonTuple tuple) => this.tuple = tuple;

        public bool HasNext() => index < tuple.Items.Count;
        public object Next() => HasNext() ? tuple.Items[index++] : null;
    }

    public class StringIterator : IIterator
    {
        private readonly string str;
        private int index = 0;

        public StringIterator(string str) => this.str = str;

        public bool HasNext() => index < str.Length;
        public object Next() => HasNext() ? str[index++].ToString() : null;
    }

    // Bytecode-based function (for lambda compiled to bytecode)
    public class BytecodeFunction : Function
    {
        private readonly CodeObject code;
        private readonly Environment closure;

        public BytecodeFunction(CodeObject code, Environment closure) : base(code.Name)
        {
            this.code = code;
            this.closure = closure;
        }

        public override object Call(List<object> arguments)
        {
            if (arguments.Count != code.ArgumentCount)
                throw new PythonException("TypeError", $"Function expects {code.ArgumentCount} arguments, got {arguments.Count}");

            var funcEnv = new Environment(closure);
            
            // Bind arguments to parameter names
            for (int i = 0; i < code.VarNames.Count && i < arguments.Count; i++)
            {
                funcEnv.SetVariable(code.VarNames[i], arguments[i]);
            }

            var vm = new VirtualMachine(closure);
            return vm.Execute(code, funcEnv);
        }
    }
}