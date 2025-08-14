// bytecode_compiler.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    // Compiler that converts AST to bytecode
    public class BytecodeCompiler
    {
        private List<Instruction> instructions;
        private List<object> constants;
        private List<string> names;
        private List<string> varNames;
        private Dictionary<object, int> constantMap;
        private Dictionary<string, int> nameMap;
        private Dictionary<string, int> varNameMap;
        private string filename;
        private string currentFunctionName;

        public BytecodeCompiler(string filename = "<string>")
        {
            this.filename = filename;
            instructions = new List<Instruction>();
            constants = new List<object>();
            names = new List<string>();
            varNames = new List<string>();
            constantMap = new Dictionary<object, int>();
            nameMap = new Dictionary<string, int>();
            varNameMap = new Dictionary<string, int>();
            currentFunctionName = "<module>";
        }

        public CodeObject Compile(List<ASTNode> statements)
        {
            foreach (var stmt in statements)
            {
                CompileNode(stmt);
            }

            // Add final return None if no explicit return
            if (instructions.Count == 0 || instructions.Last().OpCode != OpCode.RETURN_VALUE)
            {
                EmitLoadConst(null);
                Emit(OpCode.RETURN_VALUE);
            }

            return new CodeObject(
                currentFunctionName,
                filename,
                instructions,
                constants,
                names,
                varNames
            );
        }

        public CodeObject CompileExpression(ASTNode expression)
        {
            CompileNode(expression);
            Emit(OpCode.RETURN_VALUE);

            return new CodeObject(
                "<expression>",
                filename,
                instructions,
                constants,
                names,
                varNames
            );
        }

        private void CompileNode(ASTNode node)
        {
            switch (node)
            {
                case NumberNode num:
                    EmitLoadConst(num.Value);
                    break;

                case StringNode str:
                    EmitLoadConst(str.Value);
                    break;

                case BooleanNode boolean:
                    EmitLoadConst(boolean.Value);
                    break;

                case NoneNode:
                    EmitLoadConst(null);
                    break;

                case VariableNode var:
                    EmitLoadName(var.Name);
                    break;

                case AssignmentNode assign:
                    CompileNode(assign.Value);
                    EmitStoreName(assign.VariableName);
                    break;

                case BinaryOpNode binOp:
                    CompileBinaryOp(binOp);
                    break;

                case UnaryOpNode unaryOp:
                    CompileUnaryOp(unaryOp);
                    break;

                case ListNode list:
                    CompileList(list);
                    break;

                case TupleNode tuple:
                    CompileTuple(tuple);
                    break;

                case DictNode dict:
                    CompileDict(dict);
                    break;

                case FunctionCallNode call:
                    CompileFunctionCall(call);
                    break;

                case LambdaNode lambda:
                    CompileLambda(lambda);
                    break;

                case IfNode ifNode:
                    CompileIf(ifNode);
                    break;

                case ForNode forNode:
                    CompileFor(forNode);
                    break;

                case WhileNode whileNode:
                    CompileWhile(whileNode);
                    break;

                case ReturnNode returnNode:
                    CompileReturn(returnNode);
                    break;

                case ConditionalExpressionNode condExpr:
                    CompileConditionalExpression(condExpr);
                    break;

                default:
                    throw new PythonException("CompileError", $"Cannot compile node type: {node.GetType().Name}");
            }
        }

        private void CompileBinaryOp(BinaryOpNode node)
        {
            CompileNode(node.Left);
            CompileNode(node.Right);

            var opCode = node.Operator switch
            {
                "+" => OpCode.BINARY_ADD,
                "-" => OpCode.BINARY_SUBTRACT,
                "*" => OpCode.BINARY_MULTIPLY,
                "/" => OpCode.BINARY_DIVIDE,
                "%" => OpCode.BINARY_MODULO,
                "**" => OpCode.BINARY_POWER,
                "==" => OpCode.COMPARE_EQ,
                "!=" => OpCode.COMPARE_NE,
                "<" => OpCode.COMPARE_LT,
                ">" => OpCode.COMPARE_GT,
                "<=" => OpCode.COMPARE_LE,
                ">=" => OpCode.COMPARE_GE,
                "and" => OpCode.LOGICAL_AND,
                "or" => OpCode.LOGICAL_OR,
                _ => throw new PythonException("CompileError", $"Unknown binary operator: {node.Operator}")
            };

            Emit(opCode, 0, node.Line);
        }

        private void CompileUnaryOp(UnaryOpNode node)
        {
            CompileNode(node.Operand);

            var opCode = node.Operator switch
            {
                "-" => OpCode.UNARY_NEGATIVE,
                "not" => OpCode.UNARY_NOT,
                _ => throw new PythonException("CompileError", $"Unknown unary operator: {node.Operator}")
            };

            Emit(opCode, 0, node.Line);
        }

        private void CompileList(ListNode node)
        {
            foreach (var element in node.Elements)
                CompileNode(element);
            
            Emit(OpCode.BUILD_LIST, node.Elements.Count, node.Line);
        }

        private void CompileTuple(TupleNode node)
        {
            foreach (var element in node.Elements)
                CompileNode(element);
            
            Emit(OpCode.BUILD_TUPLE, node.Elements.Count, node.Line);
        }

        private void CompileDict(DictNode node)
        {
            foreach (var (key, value) in node.Pairs)
            {
                CompileNode(key);
                CompileNode(value);
            }
            
            Emit(OpCode.BUILD_DICT, node.Pairs.Count, node.Line);
        }

        private void CompileFunctionCall(FunctionCallNode node)
        {
            CompileNode(node.Function);
            
            foreach (var arg in node.Arguments)
                CompileNode(arg);
            
            Emit(OpCode.CALL_FUNCTION, node.Arguments.Count, node.Line);
        }

        private void CompileLambda(LambdaNode node)
        {
            // Create a nested compiler for the lambda
            var lambdaCompiler = new BytecodeCompiler(filename);
            lambdaCompiler.currentFunctionName = "<lambda>";

            // Add parameter names
            foreach (var param in node.Parameters)
                lambdaCompiler.AddVarName(param.Name);

            // Compile lambda body
            lambdaCompiler.CompileNode(node.Body);
            lambdaCompiler.Emit(OpCode.RETURN_VALUE);

            var lambdaCode = new CodeObject(
                "<lambda>",
                filename,
                lambdaCompiler.instructions,
                lambdaCompiler.constants,
                lambdaCompiler.names,
                lambdaCompiler.varNames,
                node.Parameters.Count
            );

            // Create lambda function object at runtime
            EmitLoadConst(lambdaCode);
            Emit(OpCode.MAKE_FUNCTION, node.Parameters.Count, node.Line);
        }

        private void CompileIf(IfNode node)
        {
            CompileNode(node.Condition);
            
            var elseLabel = instructions.Count + 1;
            Emit(OpCode.JUMP_IF_FALSE, elseLabel); // Will be patched
            
            foreach (var stmt in node.ThenBody)
                CompileNode(stmt);
            
            var endLabel = instructions.Count + 1;
            Emit(OpCode.JUMP_ABSOLUTE, endLabel); // Will be patched
            
            // Patch else jump
            instructions[elseLabel - 1] = new Instruction(OpCode.JUMP_IF_FALSE, instructions.Count, node.Line);
            
            foreach (var stmt in node.ElseBody)
                CompileNode(stmt);
            
            // Patch end jump
            instructions[endLabel - 1] = new Instruction(OpCode.JUMP_ABSOLUTE, instructions.Count, node.Line);
        }

        private void CompileFor(ForNode node)
        {
            CompileNode(node.Iterable);
            Emit(OpCode.GET_ITER);
            
            var loopStart = instructions.Count;
            Emit(OpCode.FOR_ITER, 0); // Will be patched with exit address
            
            // Store iterator value in loop variable
            EmitStoreName(node.Variable);
            
            foreach (var stmt in node.Body)
                CompileNode(stmt);
            
            Emit(OpCode.JUMP_ABSOLUTE, loopStart);
            
            // Patch FOR_ITER to jump here when done
            instructions[loopStart] = new Instruction(OpCode.FOR_ITER, instructions.Count);
        }

        private void CompileWhile(WhileNode node)
        {
            var loopStart = instructions.Count;
            
            CompileNode(node.Condition);
            Emit(OpCode.JUMP_IF_FALSE, 0); // Will be patched
            var exitJump = instructions.Count - 1;
            
            foreach (var stmt in node.Body)
                CompileNode(stmt);
            
            Emit(OpCode.JUMP_ABSOLUTE, loopStart);
            
            // Patch exit jump
            instructions[exitJump] = new Instruction(OpCode.JUMP_IF_FALSE, instructions.Count, node.Line);
        }

        private void CompileReturn(ReturnNode node)
        {
            if (node.Value != null)
                CompileNode(node.Value);
            else
                EmitLoadConst(null);
            
            Emit(OpCode.RETURN_VALUE, 0, node.Line);
        }

        private void CompileConditionalExpression(ConditionalExpressionNode node)
        {
            CompileNode(node.Condition);
            
            var falseLabel = instructions.Count + 1;
            Emit(OpCode.JUMP_IF_FALSE, falseLabel); // Will be patched
            
            CompileNode(node.TrueValue);
            
            var endLabel = instructions.Count + 1;
            Emit(OpCode.JUMP_ABSOLUTE, endLabel); // Will be patched
            
            // Patch false jump
            instructions[falseLabel - 1] = new Instruction(OpCode.JUMP_IF_FALSE, instructions.Count, node.Line);
            
            CompileNode(node.FalseValue);
            
            // Patch end jump
            instructions[endLabel - 1] = new Instruction(OpCode.JUMP_ABSOLUTE, instructions.Count, node.Line);
        }

        // Helper methods for emitting instructions
        private void Emit(OpCode opCode, int arg = 0, int lineNumber = 0)
        {
            instructions.Add(new Instruction(opCode, arg, lineNumber));
        }

        private void EmitLoadConst(object value)
        {
            var index = GetConstantIndex(value);
            Emit(OpCode.LOAD_CONST, index);
        }

        private void EmitLoadName(string name)
        {
            var index = GetNameIndex(name);
            Emit(OpCode.LOAD_NAME, index);
        }

        private void EmitStoreName(string name)
        {
            var index = GetNameIndex(name);
            Emit(OpCode.STORE_NAME, index);
        }

        private int GetConstantIndex(object value)
        {
            if (constantMap.TryGetValue(value ?? "None", out var index))
                return index;
            
            index = constants.Count;
            constants.Add(value);
            constantMap[value ?? "None"] = index;
            return index;
        }

        private int GetNameIndex(string name)
        {
            if (nameMap.TryGetValue(name, out var index))
                return index;
            
            index = names.Count;
            names.Add(name);
            nameMap[name] = index;
            return index;
        }

        private int AddVarName(string name)
        {
            if (varNameMap.TryGetValue(name, out var index))
                return index;
            
            index = varNames.Count;
            varNames.Add(name);
            varNameMap[name] = index;
            return index;
        }
    }
}