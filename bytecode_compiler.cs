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
        private List<PythonTypeObject> constants;
        private List<string> names;
        private List<string> varNames;
        private Dictionary<PythonTypeObject, int> constantMap;
        private Dictionary<string, int> nameMap;
        private Dictionary<string, int> varNameMap;
        private string filename;
        private string currentFunctionName;

        public BytecodeCompiler(string filename = "<string>")
        {
            this.filename = filename;
            instructions = new List<Instruction>();
            constants = new List<PythonTypeObject>();
            names = new List<string>();
            varNames = new List<string>();
            constantMap = new Dictionary<PythonTypeObject, int>();
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
                EmitLoadConst(PythonNone.Instance);
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
                    var numValue = num.Value;
                    if (numValue is int i)
                        EmitLoadConst(new PythonInt(i));
                    else if (numValue is double d)
                        EmitLoadConst(new PythonFloat(d));
                    else
                        throw new PythonException("CompileError", $"Invalid number type: {numValue?.GetType()}");
                    break;

                case StringNode str:
                    EmitLoadConst(new PythonString(str.Value));
                    break;

                case BooleanNode boolean:
                    EmitLoadConst(new PythonBool(boolean.Value));
                    break;

                case NoneNode:
                    EmitLoadConst(PythonNone.Instance);
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

                case FunctionDefNode funcDef:
                    CompileFunctionDef(funcDef);
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

                case MultipleAssignmentNode multiAssign:
                    CompileMultipleAssignment(multiAssign);
                    break;

                case ImportNode import:
                    CompileImport(import);
                    break;

                case FromImportNode fromImport:
                    CompileFromImport(fromImport);
                    break;

                case ClassDefNode classDef:
                    CompileClassDef(classDef);
                    break;

                case TryNode tryNode:
                    CompileTry(tryNode);
                    break;

                case RaiseNode raiseNode:
                    CompileRaise(raiseNode);
                    break;

                case BreakNode:
                    Emit(OpCode.BREAK_LOOP, 0, node.Line);
                    break;

                case ContinueNode:
                    Emit(OpCode.CONTINUE_LOOP, 0, node.Line);
                    break;

                case BlockNode block:
                    foreach (var stmt in block.Statements)
                        CompileNode(stmt);
                    break;

                // NEW CASES for new features
                case FStringNode fString:
                    CompileFString(fString);
                    break;

                case ListComprehensionNode listComp:
                    CompileListComprehension(listComp);
                    break;

                case CompoundAssignmentNode compoundAssign:
                    CompileCompoundAssignment(compoundAssign);
                    break;

                case WithNode withNode:
                    CompileWith(withNode);
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
            EmitLoadConst(new PythonCodeObject(lambdaCode));
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
                EmitLoadConst(PythonNone.Instance);
            
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

        private void CompileFunctionDef(FunctionDefNode node)
        {
            // Create a nested compiler for the function
            var funcCompiler = new BytecodeCompiler(filename);
            funcCompiler.currentFunctionName = node.Name;

            // Add parameter names as local variables AND to names list
            foreach (var param in node.Parameters)
            {
                funcCompiler.AddVarName(param.Name);
                // IMPORTANT: Also add to names list so LOAD_NAME can find them
                funcCompiler.GetNameIndex(param.Name);
            }

            // Compile function body
            foreach (var stmt in node.Body)
                funcCompiler.CompileNode(stmt);

            // Add implicit return None if no explicit return
            if (funcCompiler.instructions.Count == 0 || 
                funcCompiler.instructions.Last().OpCode != OpCode.RETURN_VALUE)
            {
                funcCompiler.EmitLoadConst(PythonNone.Instance);
                funcCompiler.Emit(OpCode.RETURN_VALUE);
            }

            var funcCode = new CodeObject(
                node.Name,
                filename,
                funcCompiler.instructions,
                funcCompiler.constants,
                funcCompiler.names,
                funcCompiler.varNames,
                node.Parameters.Count
            );

            // Create function object at runtime
            EmitLoadConst(new PythonCodeObject(funcCode));
            Emit(OpCode.MAKE_FUNCTION, node.Parameters.Count, node.Line);
            EmitStoreName(node.Name);
        }

        private void CompileMultipleAssignment(MultipleAssignmentNode node)
        {
            CompileNode(node.Value);
            
            // For now, use a simplified approach
            // In a full implementation, we'd use UNPACK_SEQUENCE opcode
            for (int i = 0; i < node.VariableNames.Count; i++)
            {
                if (i < node.VariableNames.Count - 1)
                {
                    Emit(OpCode.LOAD_CONST, GetConstantIndex(new PythonInt(i))); // Load index
                    Emit(OpCode.LOAD_INDEX); // Custom opcode for indexing
                }
                else
                {
                    // Last item, just use the value directly
                    Emit(OpCode.LOAD_CONST, GetConstantIndex(new PythonInt(i)));
                    Emit(OpCode.LOAD_INDEX);
                }
                
                if (node.VariableNames[i] != "_") // Skip underscore variables
                    EmitStoreName(node.VariableNames[i]);
                else
                    Emit(OpCode.POP_TOP); // Discard underscore values
            }
        }

        private void CompileImport(ImportNode node)
        {
            EmitLoadConst(new PythonString(node.ModuleName));
            Emit(OpCode.IMPORT_NAME, 0, node.Line);
            
            var storeName = node.Alias ?? node.ModuleName;
            EmitStoreName(storeName);
        }

        private void CompileFromImport(FromImportNode node)
        {
            EmitLoadConst(new PythonString(node.ModuleName));
            Emit(OpCode.IMPORT_NAME, 0, node.Line);
            
            foreach (var (itemName, alias) in node.ImportItems)
            {
                EmitLoadConst(new PythonString(itemName));
                Emit(OpCode.IMPORT_FROM, 0, node.Line);
                
                var storeName = alias ?? itemName;
                EmitStoreName(storeName);
            }
        }

        private void CompileClassDef(ClassDefNode node)
        {
            // Simplified class compilation
            // In a full implementation, we'd create a proper class object
            EmitLoadConst(new PythonString($"<class {node.Name}>"));
            EmitStoreName(node.Name);
        }

        private void CompileTry(TryNode node)
        {
            // Simplified try/except compilation
            // In a full implementation, we'd use exception handling opcodes
            foreach (var stmt in node.TryBody)
                CompileNode(stmt);
        }

        private void CompileRaise(RaiseNode node)
        {
            CompileNode(node.Exception);
            Emit(OpCode.RAISE_VARARGS, 1, node.Line);
        }

        // NEW METHODS for new features
        private void CompileFString(FStringNode node)
        {
            // Build the f-string at runtime
            EmitLoadConst(new PythonString(""));  // Start with empty string
            
            foreach (var (text, expr) in node.Parts)
            {
                if (expr != null)
                {
                    // Evaluate expression and convert to string
                    CompileNode(expr);
                    // Call str() on the expression
                    EmitLoadName("str");
                    Emit(OpCode.CALL_FUNCTION, 1);
                }
                else if (!string.IsNullOrEmpty(text))
                {
                    EmitLoadConst(new PythonString(text));
                }
                else
                {
                    continue;
                }
                
                // Concatenate with previous string
                Emit(OpCode.BINARY_ADD);
            }
        }

        private void CompileListComprehension(ListComprehensionNode node)
        {
            // Create empty list
            Emit(OpCode.BUILD_LIST, 0);
            
            // Compile iterable
            CompileNode(node.Iterable);
            Emit(OpCode.GET_ITER);
            
            var loopStart = instructions.Count;
            Emit(OpCode.FOR_ITER, 0); // Will be patched with exit address
            
            // Store iterator value in loop variable
            EmitStoreName(node.Variable);
            
            // Check condition if exists
            if (node.Condition != null)
            {
                CompileNode(node.Condition);
                var skipLabel = instructions.Count + 1;
                Emit(OpCode.JUMP_IF_FALSE, skipLabel); // Will be patched
                
                // Evaluate expression and append to list
                CompileNode(node.Expression);
                // Note: In real implementation, we'd need a way to append to the list
                // For now, this is simplified
                
                // Patch skip jump
                instructions[skipLabel - 1] = new Instruction(OpCode.JUMP_IF_FALSE, instructions.Count);
            }
            else
            {
                // Evaluate expression and append to list
                CompileNode(node.Expression);
                // Simplified - in real implementation would append to list
            }
            
            Emit(OpCode.JUMP_ABSOLUTE, loopStart);
            
            // Patch FOR_ITER to jump here when done
            instructions[loopStart] = new Instruction(OpCode.FOR_ITER, instructions.Count);
        }

        private void CompileCompoundAssignment(CompoundAssignmentNode node)
        {
            // Load current value
            EmitLoadName(node.VariableName);
            
            // Load new value
            CompileNode(node.Value);
            
            // Apply operation
            var opCode = node.Operator switch
            {
                "+=" => OpCode.BINARY_ADD,
                "-=" => OpCode.BINARY_SUBTRACT,
                "*=" => OpCode.BINARY_MULTIPLY,
                "/=" => OpCode.BINARY_DIVIDE,
                "%=" => OpCode.BINARY_MODULO,
                "**=" => OpCode.BINARY_POWER,
                _ => throw new PythonException("CompileError", $"Unknown compound operator: {node.Operator}")
            };
            
            Emit(opCode);
            
            // Store result back
            EmitStoreName(node.VariableName);
        }

        private void CompileWith(WithNode node)
        {
            // Simplified with statement compilation
            // In a full implementation, this would be more complex
            
            // Compile context expression
            CompileNode(node.ContextExpression);
            
            // Store in temporary variable if 'as' clause is present
            if (!string.IsNullOrEmpty(node.Variable))
            {
                EmitStoreName(node.Variable);
            }
            else
            {
                Emit(OpCode.POP_TOP);  // Discard if no variable
            }
            
            // Compile body
            foreach (var stmt in node.Body)
            {
                CompileNode(stmt);
            }
        }

        // Helper methods for emitting instructions
        private void Emit(OpCode opCode, int arg = 0, int lineNumber = 0)
        {
            instructions.Add(new Instruction(opCode, arg, lineNumber));
        }

        private void EmitLoadConst(PythonTypeObject value)
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

        private int GetConstantIndex(PythonTypeObject value)
        {
            var key = value ?? PythonNone.Instance;
            
            // constantMap을 올바르게 체크
            if (constantMap.TryGetValue(key, out var index))
                return index;
            
            index = constants.Count;
            constants.Add(value);
            constantMap[key] = index;
            return index;
        }

        private int GetNameIndex(string name)
        {
            // nameMap을 체크해야 함 (varNameMap이 아님!)
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