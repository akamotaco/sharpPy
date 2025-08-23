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

                case AnnotatedAttributeAssignmentNode annotatedAttrAssign:
                    CompileAnnotatedAttributeAssignment(annotatedAttrAssign);
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

                case StringConcatenationNode strConcat:
                    CompileStringConcatenation(strConcat);
                    break;

                case ExpressionStatementNode exprStmt:
                    // Expression을 컴파일하고 결과를 스택에서 제거
                    CompileNode(exprStmt.Expression);
                    Emit(OpCode.POP_TOP, 0, node.Line);
                    break;
                case PassNode:
                    // pass는 아무것도 하지 않음 - 명령어를 생성하지 않음
                    // 또는 NOP를 생성할 수도 있음: Emit(OpCode.NOP, 0, node.Line);
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
                "is" => OpCode.COMPARE_IS,           // 추가
                "is not" => OpCode.COMPARE_IS_NOT,    // 추가 (새로운 OpCode 필요)
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

            int normalArgCount = 0;
            int defaultCount = 0;
            var defaultValues = new List<ASTNode>();  // 기본값 AST 노드 저장

            // 파라미터 처리 (기본값 포함)
            foreach (var param in node.Parameters)
            {
                if (param.Kind == ParameterKind.Normal)
                {
                    normalArgCount++;
                    lambdaCompiler.AddVarName(param.Name);
                    lambdaCompiler.GetNameIndex(param.Name);

                    if (param.DefaultValue != null)
                    {
                        defaultValues.Add(param.DefaultValue);  // AST 노드 저장
                        defaultCount++;
                    }
                }
                else if (param.Kind == ParameterKind.VarArgs)
                {
                    lambdaCompiler.GetNameIndex(param.Name);
                }
                else if (param.Kind == ParameterKind.KwArgs)
                {
                    lambdaCompiler.GetNameIndex(param.Name);
                }
            }

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
                normalArgCount,
                0,
                node.Parameters.Any(p => p.Kind == ParameterKind.VarArgs),
                node.Parameters.Any(p => p.Kind == ParameterKind.KwArgs),
                node.Parameters.FirstOrDefault(p => p.Kind == ParameterKind.VarArgs)?.Name,
                node.Parameters.FirstOrDefault(p => p.Kind == ParameterKind.KwArgs)?.Name,
                node.Parameters.Where(p => p.DefaultValue != null).Select(p => p.Name).ToList()
            );

            // 기본값들을 스택에 푸시 (역순으로)
            foreach (var defaultValue in defaultValues)
            {
                CompileNode(defaultValue);  // 현재 환경에서 평가
            }

            // Create lambda function object at runtime
            EmitLoadConst(new PythonCodeObject(lambdaCode));

            // MAKE_FUNCTION에 기본값 정보 전달
            int makeArg = normalArgCount | (defaultCount << 8);
            Emit(OpCode.MAKE_FUNCTION, makeArg, node.Line);
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
            var defaultValues = new List<ASTNode>();  // 기본값 AST 노드 저장
            var parameterTypeHints = new List<TypeHint>();  // 추가

            // 파라미터 처리
            foreach (var param in node.Parameters)
            {
                if (param.Kind == ParameterKind.Normal)
                {
                    normalArgCount++;
                    funcCompiler.AddVarName(param.Name);
                    funcCompiler.GetNameIndex(param.Name);

                    parameterTypeHints.Add(param.TypeHint);  // 추가

                    if (param.DefaultValue != null)
                    {
                        defaultValues.Add(param.DefaultValue);  // AST 노드 저장
                        defaultCount++;
                    }
                }
                else if (param.Kind == ParameterKind.VarArgs)
                {
                    funcCompiler.GetNameIndex(param.Name);
                }
                else if (param.Kind == ParameterKind.KwArgs)
                {
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
                normalArgCount,
                0,
                node.Parameters.Any(p => p.Kind == ParameterKind.VarArgs),
                node.Parameters.Any(p => p.Kind == ParameterKind.KwArgs),
                node.Parameters.FirstOrDefault(p => p.Kind == ParameterKind.VarArgs)?.Name,
                node.Parameters.FirstOrDefault(p => p.Kind == ParameterKind.KwArgs)?.Name,
                node.Parameters.Where(p => p.DefaultValue != null).Select(p => p.Name).ToList(),
                parameterTypeHints,           // 추가
                node.ReturnTypeHint           // 추가
            );

            // 기본값들을 스택에 푸시 (역순으로)
            foreach (var defaultValue in defaultValues)
            {
                CompileNode(defaultValue);  // 현재 환경에서 평가
            }

            EmitLoadConst(new PythonCodeObject(funcCode));

            int makeArg = normalArgCount | (defaultCount << 8);
            Emit(OpCode.MAKE_FUNCTION, makeArg, node.Line);

            EmitStoreName(node.Name);
        }

        private void CompileMultipleAssignment(MultipleAssignmentNode node)
        {
            // 값 평가
            CompileNode(node.Value);

            // UNPACK_SEQUENCE 사용
            Emit(OpCode.UNPACK_SEQUENCE, node.VariableNames.Count, node.Line);

            // 각 변수에 할당 - 정순으로!
            for (int i = 0; i < node.VariableNames.Count; i++)
            {
                if (node.VariableNames[i] != "_")  // underscore는 무시
                {
                    EmitStoreName(node.VariableNames[i]);
                }
                else
                {
                    Emit(OpCode.POP_TOP);  // 값 버리기
                }
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
                // IMPORT_STAR opcode 사용
                Emit(OpCode.IMPORT_STAR, 0, node.Line);
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
            // 클래스 body를 별도의 CodeObject로 컴파일
            var classCompiler = new BytecodeCompiler(filename);
            classCompiler.currentFunctionName = $"<class {node.Name}>";

            // 클래스 body 컴파일 (메서드 정의 등)
            foreach (var stmt in node.Body)
            {
                classCompiler.CompileNode(stmt);
            }

            // 클래스 코드는 반드시 None을 반환해야 함
            if (classCompiler.instructions.Count == 0 ||
                classCompiler.instructions.Last().OpCode != OpCode.RETURN_VALUE)
            {
                classCompiler.EmitLoadConst(PythonNone.Instance);
                classCompiler.Emit(OpCode.RETURN_VALUE);
            }

            var classCode = new CodeObject(
                $"<class {node.Name}>",
                filename,
                classCompiler.instructions,
                classCompiler.constants,
                classCompiler.names,
                classCompiler.varNames
            );

            // 부모 클래스들을 스택에 push (역순으로)
            for (int i = node.BaseClasses.Count - 1; i >= 0; i--)
            {
                EmitLoadName(node.BaseClasses[i]);
            }

            // 클래스 이름을 스택에 push
            EmitLoadConst(new PythonString(node.Name));

            // 클래스 코드 객체를 스택에 push
            EmitLoadConst(new PythonCodeObject(classCode));

            // BUILD_CLASS opcode 실행 (부모 클래스 개수를 인자로)
            Emit(OpCode.BUILD_CLASS, node.BaseClasses.Count, node.Line);

            // 생성된 클래스를 변수에 저장
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
            // 빈 문자열로 시작
            EmitLoadConst(new PythonString(""));

            foreach (var (text, expr) in node.Parts)
            {
                if (expr != null)
                {
                    // 표현식 평가
                    CompileNode(expr);

                    // 문자열로 변환하는 내장 opcode 추가 필요
                    // 또는 ToPythonString을 호출하는 특별한 opcode
                    Emit(OpCode.FORMAT_VALUE, 0, node.Line); // 새로운 opcode 필요
                }
                else if (!string.IsNullOrEmpty(text))
                {
                    EmitLoadConst(new PythonString(text));
                }
                else
                {
                    continue;
                }

                // 이전 문자열과 연결
                Emit(OpCode.BINARY_ADD);
            }
        }

        private void CompileListComprehension(ListComprehensionNode node)
        {
            // 빈 리스트 생성
            Emit(OpCode.BUILD_LIST, 0, node.Line);

            // iterable 컴파일
            CompileNode(node.Iterable);
            Emit(OpCode.GET_ITER);

            var loopStart = instructions.Count;
            var forIterIndex = instructions.Count;
            Emit(OpCode.FOR_ITER, 0); // 나중에 패치

            // 루프 변수에 저장
            EmitStoreName(node.Variable);

            // 조건 체크 (있는 경우)
            int? jumpIfFalseIndex = null;
            if (node.Condition != null)
            {
                CompileNode(node.Condition);
                jumpIfFalseIndex = instructions.Count;
                Emit(OpCode.JUMP_IF_FALSE, 0); // 나중에 패치
            }

            // 표현식 평가
            CompileNode(node.Expression);

            // 리스트에 추가
            // LIST_APPEND는 TOS를 TOS-2(또는 지정된 위치)의 리스트에 추가
            Emit(OpCode.LIST_APPEND, 2, node.Line);  // 2는 스택에서 리스트의 위치

            // 조건이 false인 경우 여기로 점프
            if (jumpIfFalseIndex.HasValue)
            {
                instructions[jumpIfFalseIndex.Value] = new Instruction(
                    OpCode.JUMP_IF_FALSE,
                    instructions.Count,
                    node.Line
                );
            }

            // 루프 시작으로 점프
            Emit(OpCode.JUMP_ABSOLUTE, loopStart, node.Line);

            // FOR_ITER 패치 - 루프 종료 시 여기로
            instructions[forIterIndex] = new Instruction(
                OpCode.FOR_ITER,
                instructions.Count,
                node.Line
            );

            // 스택 정리 - 이터레이터 제거는 FOR_ITER가 자동으로 처리
            // 리스트는 스택에 남아있음
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
            // Context manager 평가
            CompileNode(node.ContextExpression);

            // WITH_SETUP: context manager 저장하고 __enter__ 호출
            Emit(OpCode.WITH_SETUP, 0, node.Line);

            // 'as' 절 처리
            if (!string.IsNullOrEmpty(node.Variable))
            {
                EmitStoreName(node.Variable);
            }
            else
            {
                Emit(OpCode.POP_TOP);
            }

            // with 블록 본문
            foreach (var stmt in node.Body)
            {
                CompileNode(stmt);
            }

            // WITH_CLEANUP_FINISH: __exit__ 호출
            Emit(OpCode.WITH_CLEANUP_FINISH, 0, node.Line);
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

        private void CompileStringConcatenation(StringConcatenationNode node)
        {
            // 첫 번째 부분을 스택에 로드
            CompileNode(node.Parts[0]);

            // 나머지 부분들을 순차적으로 연결
            for (int i = 1; i < node.Parts.Count; i++)
            {
                CompileNode(node.Parts[i]);
                Emit(OpCode.BINARY_ADD, 0, node.Line);
            }
        }

        private void CompileAnnotatedAttributeAssignment(AnnotatedAttributeAssignmentNode node)
        {
            // 타입 힌트는 런타임에 영향을 주지 않으므로 무시하고
            // 값이 있는 경우에만 할당 처리
            if (node.Value != null)
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
            // 값이 없으면 (타입 힌트만 있는 경우) 아무것도 하지 않음
        }
    }
}