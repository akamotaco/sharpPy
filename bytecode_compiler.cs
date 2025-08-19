// bytecode_compiler.cs
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

        private int? currentLoopStart = null;
        private List<int> currentBreakJumps = null;

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

                case MultiForNode multiForNode:  // 추가!
                    CompileMultiFor(multiForNode);
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
                    if (currentBreakJumps == null)
                        throw new PythonException("SyntaxError", "'break' outside loop", node.Line, node.Column);
                    currentBreakJumps.Add(instructions.Count);
                    Emit(OpCode.JUMP_ABSOLUTE, 0, node.Line); // 나중에 패치
                    break;

                case ContinueNode:
                    if (currentLoopStart == null)
                        throw new PythonException("SyntaxError", "'continue' not properly in loop", node.Line, node.Column);
                    Emit(OpCode.JUMP_ABSOLUTE, currentLoopStart.Value, node.Line);
                    break;

                case BlockNode block:
                    foreach (var stmt in block.Statements)
                        CompileNode(stmt);
                    break;

                case AttributeNode attrNode:
                    CompileAttribute(attrNode);
                    break;

                case AttributeAssignmentNode attrAssign:
                    CompileAttributeAssignment(attrAssign);
                    break;

                case AttributeCompoundAssignmentNode attrCompound:
                    CompileAttributeCompoundAssignment(attrCompound);
                    break;

                case IndexNode indexNode:
                    CompileIndex(indexNode);
                    break;

                case IndexAssignmentNode indexAssign:
                    CompileIndexAssignment(indexAssign);
                    break;

                case IndexCompoundAssignmentNode indexCompound:
                    CompileIndexCompoundAssignment(indexCompound);
                    break;

                case SliceNode sliceNode:
                    CompileSlice(sliceNode);
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

                case ExpressionStatementNode exprStmt:
                    // Expression을 컴파일하고 결과를 스택에서 제거
                    CompileNode(exprStmt.Expression);
                    Emit(OpCode.POP_TOP, 0, node.Line);
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

            // 위치 인자 컴파일
            foreach (var arg in node.Arguments)
                CompileNode(arg);

            // 키워드 인자가 있으면
            if (node.KeywordArguments != null && node.KeywordArguments.Count > 0)
            {
                // 키워드 인자를 위한 딕셔너리 생성
                foreach (var kvp in node.KeywordArguments)
                {
                    EmitLoadConst(new PythonString(kvp.Key));
                    CompileNode(kvp.Value);
                }

                Emit(OpCode.BUILD_MAP, node.KeywordArguments.Count, node.Line);

                // 키워드 인자가 있는 함수 호출
                Emit(OpCode.CALL_FUNCTION_KW, node.Arguments.Count, node.Line);
            }
            else
            {
                // 일반 함수 호출
                Emit(OpCode.CALL_FUNCTION, node.Arguments.Count, node.Line);
            }
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
            var jumpTargets = new List<int>();

            // Compile if condition
            CompileNode(node.Condition);

            var ifFalseLabel = instructions.Count + 1;
            Emit(OpCode.JUMP_IF_FALSE, ifFalseLabel); // Will be patched

            // Compile if body
            foreach (var stmt in node.ThenBody)
                CompileNode(stmt);

            jumpTargets.Add(instructions.Count);
            Emit(OpCode.JUMP_ABSOLUTE, 0); // Will be patched to jump to end

            // Patch if false jump
            instructions[ifFalseLabel - 1] = new Instruction(OpCode.JUMP_IF_FALSE, instructions.Count, node.Line);

            // Compile elif clauses
            foreach (var (elifCondition, elifBody) in node.ElifClauses)
            {
                CompileNode(elifCondition);

                var elifFalseLabel = instructions.Count + 1;
                Emit(OpCode.JUMP_IF_FALSE, elifFalseLabel); // Will be patched

                // Compile elif body
                foreach (var stmt in elifBody)
                    CompileNode(stmt);

                jumpTargets.Add(instructions.Count);
                Emit(OpCode.JUMP_ABSOLUTE, 0); // Will be patched to jump to end

                // Patch elif false jump
                instructions[elifFalseLabel - 1] = new Instruction(OpCode.JUMP_IF_FALSE, instructions.Count, node.Line);
            }

            // Compile else body
            foreach (var stmt in node.ElseBody)
                CompileNode(stmt);

            // Patch all jump-to-end instructions
            var endLabel = instructions.Count;
            foreach (var jumpIndex in jumpTargets)
            {
                instructions[jumpIndex] = new Instruction(OpCode.JUMP_ABSOLUTE, endLabel, node.Line);
            }
        }

        private void CompileFor(ForNode node)
        {
            CompileNode(node.Iterable);
            Emit(OpCode.GET_ITER);

            var loopStart = instructions.Count;
            var breakJumps = new List<int>();  // break 점프들을 추적

            Emit(OpCode.FOR_ITER, 0); // 나중에 패치

            EmitStoreName(node.Variable);

            // Body 컴파일 전에 루프 컨텍스트 설정 (break/continue 처리용)
            var previousLoopStart = currentLoopStart;
            var previousBreakJumps = currentBreakJumps;
            currentLoopStart = loopStart;
            currentBreakJumps = breakJumps;

            foreach (var stmt in node.Body)
            {
                CompileNode(stmt);
            }

            Emit(OpCode.JUMP_ABSOLUTE, loopStart);

            // FOR_ITER 패치
            instructions[loopStart] = new Instruction(OpCode.FOR_ITER, instructions.Count);

            // break 점프들 패치
            foreach (var breakJump in breakJumps)
            {
                instructions[breakJump] = new Instruction(OpCode.JUMP_ABSOLUTE, instructions.Count);
            }

            // 이전 루프 컨텍스트 복원
            currentLoopStart = previousLoopStart;
            currentBreakJumps = previousBreakJumps;
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
            var funcCompiler = new BytecodeCompiler(filename);
            funcCompiler.currentFunctionName = node.Name;

            int normalArgCount = 0;
            int defaultCount = 0;
            bool hasVarArgs = false;
            bool hasKwArgs = false;
            string varArgsName = null;
            string kwArgsName = null;
            var paramNamesWithDefaults = new List<string>();

            // 파라미터 처리 - 순서 중요!
            // 1. 먼저 일반 파라미터들 처리
            foreach (var param in node.Parameters)
            {
                if (param.Kind == ParameterKind.Normal)
                {
                    normalArgCount++;
                    funcCompiler.AddVarName(param.Name);
                    // Names 리스트에도 추가 (LOAD_NAME을 위해)
                    funcCompiler.GetNameIndex(param.Name);

                    if (param.DefaultValue != null)
                    {
                        paramNamesWithDefaults.Add(param.Name);
                        defaultCount++;
                    }
                }
            }

            // 2. 그 다음 *args와 **kwargs 처리
            foreach (var param in node.Parameters)
            {
                if (param.Kind == ParameterKind.VarArgs)
                {
                    hasVarArgs = true;
                    varArgsName = param.Name;
                    // VarNames에 추가하지 않고, Names에만 추가
                    funcCompiler.GetNameIndex(param.Name);
                }
                else if (param.Kind == ParameterKind.KwArgs)
                {
                    hasKwArgs = true;
                    kwArgsName = param.Name;
                    // VarNames에 추가하지 않고, Names에만 추가
                    funcCompiler.GetNameIndex(param.Name);
                }
            }

            // 함수 본문 컴파일
            foreach (var stmt in node.Body)
                funcCompiler.CompileNode(stmt);

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
                normalArgCount,  // *args와 **kwargs는 포함하지 않음
                0,
                hasVarArgs,
                hasKwArgs,
                varArgsName,
                kwArgsName,
                paramNamesWithDefaults
            );

            // 기본값들을 스택에 푸시
            foreach (var param in node.Parameters)
            {
                if (param.Kind == ParameterKind.Normal && param.DefaultValue != null)
                {
                    CompileNode(param.DefaultValue);
                }
            }

            EmitLoadConst(new PythonCodeObject(funcCode));

            int makeArg = normalArgCount | (defaultCount << 8);
            Emit(OpCode.MAKE_FUNCTION, makeArg, node.Line);

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

            // import * 처리
            if (node.ImportItems.Count == 1 && node.ImportItems[0].Name == "*")
            {
                // 모듈을 스택에 남겨두고
                Emit(OpCode.DUP_TOP); // 스택 복사 (새로운 opcode 추가 필요)

                // 모든 public 속성을 가져와서 현재 네임스페이스에 추가
                // 이는 특별한 처리가 필요함
            }
            else
            {
                foreach (var (itemName, alias) in node.ImportItems)
                {
                    // 스택 최상단의 모듈을 복사
                    Emit(OpCode.DUP_TOP);
                    EmitLoadConst(new PythonString(itemName));
                    Emit(OpCode.IMPORT_FROM, 0, node.Line);

                    var storeName = alias ?? itemName;
                    EmitStoreName(storeName);
                }

                // 마지막에 모듈 제거
                Emit(OpCode.POP_TOP);
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
                    // 중요: 함수를 먼저 로드하고, 그 다음에 인자를 평가!
                    EmitLoadName("str");         // Stack: [str]
                    CompileNode(expr);           // Stack: [str, 10]
                    Emit(OpCode.CALL_FUNCTION, 1); // str(10) 호출
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

        private void CompileMultiFor(MultiForNode node)
        {
            // iterable 컴파일
            CompileNode(node.Iterable);
            Emit(OpCode.GET_ITER);

            var loopStart = instructions.Count;
            Emit(OpCode.FOR_ITER, 0); // 나중에 패치될 종료 주소

            // 언패킹을 위한 UNPACK_SEQUENCE 사용
            Emit(OpCode.UNPACK_SEQUENCE, node.Variables.Count);

            // 각 변수에 저장 (역순으로)
            for (int i = node.Variables.Count - 1; i >= 0; i--)
            {
                EmitStoreName(node.Variables[i]);
            }

            // 루프 본문 컴파일
            foreach (var stmt in node.Body)
            {
                CompileNode(stmt);
            }

            // 루프 시작으로 점프
            Emit(OpCode.JUMP_ABSOLUTE, loopStart);

            // FOR_ITER 패치 - 루프 종료 시 여기로 점프
            instructions[loopStart] = new Instruction(OpCode.FOR_ITER, instructions.Count);
        }
        
        private void CompileAttribute(AttributeNode node)
        {
            // 객체 컴파일
            CompileNode(node.Object);
            
            // 속성 이름을 names 리스트에 추가
            int nameIndex = GetNameIndex(node.Attribute);
            
            // LOAD_ATTR 명령어 생성
            Emit(OpCode.LOAD_ATTR, nameIndex, node.Line);
        }

        private void CompileAttributeAssignment(AttributeAssignmentNode node)
        {
            // 객체 컴파일
            CompileNode(node.Object);
            
            // 값 컴파일
            CompileNode(node.Value);
            
            // 속성 이름을 names 리스트에 추가
            int nameIndex = GetNameIndex(node.Attribute);
            
            // STORE_ATTR 명령어 생성
            Emit(OpCode.STORE_ATTR, nameIndex, node.Line);
        }

        private void CompileAttributeCompoundAssignment(AttributeCompoundAssignmentNode node)
        {
            // 객체를 두 번 로드 (한 번은 읽기용, 한 번은 쓰기용)
            CompileNode(node.Object);
            Emit(OpCode.DUP_TOP); // 객체 복제
            
            // 현재 속성 값 로드
            int nameIndex = GetNameIndex(node.Attribute);
            Emit(OpCode.LOAD_ATTR, nameIndex);
            
            // 새 값 컴파일
            CompileNode(node.Value);
            
            // 연산 수행
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
            
            // 결과를 속성에 저장
            Emit(OpCode.STORE_ATTR, nameIndex, node.Line);
        }

        private void CompileIndex(IndexNode node)
        {
            // 객체 컴파일
            CompileNode(node.Object);
            
            // 인덱스 컴파일
            CompileNode(node.Index);
            
            // LOAD_INDEX 명령어 생성
            Emit(OpCode.LOAD_INDEX, 0, node.Line);
        }

        private void CompileIndexAssignment(IndexAssignmentNode node)
        {
            // 객체 컴파일
            CompileNode(node.Object);
            
            // 인덱스 컴파일
            CompileNode(node.Index);
            
            // 값 컴파일
            CompileNode(node.Value);
            
            // STORE_INDEX 명령어 생성
            Emit(OpCode.STORE_INDEX, 0, node.Line);
        }

        private void CompileIndexCompoundAssignment(IndexCompoundAssignmentNode node)
        {
            // 객체와 인덱스를 복제
            CompileNode(node.Object);
            CompileNode(node.Index);
            
            // 스택: [obj, index]
            // 복제를 위해 두 번째 세트 생성
            Emit(OpCode.DUP_TOP_TWO); // 새로운 opcode 필요
            
            // 현재 값 로드
            Emit(OpCode.LOAD_INDEX);
            
            // 새 값 컴파일
            CompileNode(node.Value);
            
            // 연산 수행
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
            
            // 결과 저장
            Emit(OpCode.STORE_INDEX, 0, node.Line);
        }

        private void CompileSlice(SliceNode node)
        {
            // 객체 컴파일
            CompileNode(node.Object);
            
            // 슬라이스 인덱스들 컴파일
            if (node.Start != null)
                CompileNode(node.Start);
            else
                EmitLoadConst(PythonNone.Instance);
                
            if (node.Stop != null)
                CompileNode(node.Stop);
            else
                EmitLoadConst(PythonNone.Instance);
                
            if (node.Step != null)
                CompileNode(node.Step);
            else
                EmitLoadConst(PythonNone.Instance);
            
            // BUILD_SLICE opcode 생성
            Emit(OpCode.BUILD_SLICE, 3, node.Line);
            
            // 슬라이스 적용
            Emit(OpCode.LOAD_INDEX, 0, node.Line);
        }
    }
}