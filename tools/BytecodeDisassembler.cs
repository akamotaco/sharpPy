using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SharpPy.Core;

namespace SharpPy.Tools
{
    /// <summary>
    /// 바이트코드 디스어셈블리 기능을 전담하는 클래스
    /// </summary>
    public class BytecodeDisassembler
    {
        /// <summary>
        /// Python 파일을 직접 디스어셈블하여 바이트코드 출력
        /// </summary>
        /// <param name="pythonFile">Python 파일 경로</param>
        public void RunDirectDisassembly(string pythonFile)
        {
            if (string.IsNullOrEmpty(pythonFile))
            {
                Console.WriteLine("사용법: dotnet run --dis <python_file>");
                Console.WriteLine("예제: dotnet run --dis test.py");
                Environment.Exit(1);
                return;
            }

            if (!File.Exists(pythonFile))
            {
                Console.WriteLine($"❌ 파일을 찾을 수 없습니다: {pythonFile}");
                Environment.Exit(1);
                return;
            }

            try
            {
                // 컴파일러에서 직접 바이트코드 출력 (PyDisModule 우회)
                Console.WriteLine($"Disassembly of {pythonFile}:");
                
                string code = File.ReadAllText(pythonFile);
                // Use GeneratedParserBridge to get CPython 3.12 compatible PEG parser support
                var ast = GeneratedParserBridge.ParseSource(code, pythonFile);
                
                var compiler = new PythonCompiler();
                var codeObject = compiler.Compile(ast, "<module>", new List<string>(), pythonFile);
                
                // 실제 컴파일된 바이트코드 직접 출력
                ShowActualBytecode(codeObject);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 컴파일 오류: {ex.Message}");
                Environment.Exit(1);
            }
        }

        /// <summary>
        /// 바이트코드 객체를 사람이 읽기 쉬운 형태로 출력
        /// </summary>
        /// <param name="codeObject">바이트코드 객체</param>
        private void ShowActualBytecode(PyCodeObject codeObject)
        {
            var instructions = codeObject.Instructions;
            var constants = codeObject.Constants;
            var names = codeObject.Names;
            var varNames = codeObject.VarNames;

            // CPython 3.12 정확한 바이트 오프셋 누적 계산
            int currentByteOffset = 0;

            for (int i = 0; i < instructions.Count; i++)
            {
                var instruction = instructions[i];

                // CPython 호환 형식 출력
                string line = FormatActualInstruction(i, currentByteOffset, instruction, constants, names, varNames);
                Console.WriteLine(line);

                // 다음 명령어를 위한 바이트 오프셋 누적 계산
                currentByteOffset += PyJumpBackwardUtil.GetCPythonInstructionSize(instruction.OpCode, instruction.Argument);
            }

            // Display Exception Table if present (CPython 3.12 compatible format)
            if (codeObject.ExceptionTable.Count > 0)
            {
                Console.WriteLine("ExceptionTable:");
                foreach (var entry in codeObject.ExceptionTable)
                {
                    var lastiFlag = entry.Lasti ? " lasti" : "";
                    Console.WriteLine($"  {entry.StartOffset} to {entry.EndOffset} -> {entry.HandlerOffset} [{entry.Depth}]{lastiFlag}");
                }
            }

            // CPython style: 중첩된 코드 객체들도 표시 (제너레이터, 함수 등)
            ShowNestedCodeObjects(constants);
        }

        /// <summary>
        /// 상수 배열에서 중첩된 코드 객체들을 찾아서 디스어셈블
        /// </summary>
        /// <param name="constants">상수 배열</param>
        private void ShowNestedCodeObjects(List<PyObject> constants)
        {
            foreach (var constant in constants)
            {
                if (constant is PyCodeObject nestedCode)
                {
                    Console.WriteLine(); // 빈 줄로 구분
                    Console.WriteLine($"Disassembly of {nestedCode}:");
                    ShowActualBytecode(nestedCode);
                }
            }
        }
        
        /// <summary>
        /// 단일 명령어를 CPython 호환 형식으로 포맷팅
        /// </summary>
        private string FormatActualInstruction(int instructionIndex, int byteOffset, 
                                            ByteCodeInstruction instruction,
                                            List<PyObject> constants, 
                                            List<string> names, 
                                            List<string> varNames)
        {
            var sb = new StringBuilder();
            
            // CPython 3.12 호환: 실제 AST 라인 번호 사용
            if (instruction.LineNumber > 0)
            {
                sb.AppendFormat("{0,3}", instruction.LineNumber);
            }
            else if (instructionIndex == 0) // RESUME은 항상 라인 0
            {
                sb.AppendFormat("{0,3}", 0);
            }
            else
            {
                sb.Append("   ");
            }
            
            // 바이트 오프셋
            sb.AppendFormat("{0,12}", byteOffset);
            
            // 점프 타겟 표시 (간단 구현)
            if (instruction.OpCode == ByteCodeOp.FOR_ITER || instruction.OpCode == ByteCodeOp.END_FOR)
                sb.Append(" >> ");
            else
                sb.Append("    ");
                
            // 명령어 이름
            sb.AppendFormat("{0,-20}", instruction.OpCode.ToString());
            
            // 인수 정보
            string argInfo = GetActualArgumentInfo(instruction, byteOffset, constants, names, varNames);
            if (!string.IsNullOrEmpty(argInfo))
                sb.Append(argInfo);
                
            return sb.ToString();
        }

        /// <summary>
        /// CPython 3.12 호환 상수 표시 형식
        /// </summary>
        private string FormatConstantForDisplay(PyObject constant)
        {
            if (constant == null)
                return "None";

            switch (constant)
            {
                case PyString pyStr:
                    // 문자열은 따옴표로 감싸기 (CPython 3.12 스타일)
                    return $"'{pyStr.Value}'";

                case PyInt pyInt:
                    return pyInt.Value.ToString();

                case PyFloat pyFloat:
                    return pyFloat.Value.ToString();

                case PyBool pyBool:
                    return pyBool.Value ? "True" : "False";

                case PyNone:
                    return "None";

                case PyList pyList:
                    return "[...]"; // 간략히 표시

                case PyDict pyDict:
                    return "{...}"; // 간략히 표시

                case PyTuple pyTuple:
                    return "(...)"; // 간략히 표시

                default:
                    // 기타 객체들 (함수, 클래스 등)
                    return constant.ToString();
            }
        }

        /// <summary>
        /// 명령어 인수 정보를 형식에 맞게 생성
        /// </summary>
        private string GetActualArgumentInfo(ByteCodeInstruction instruction, int currentByteOffset,
                                          List<PyObject> constants, List<string> names, List<string> varNames)
        {
            var op = instruction.OpCode;
            var arg = instruction.Argument;
            
            switch (op)
            {
                case ByteCodeOp.LOAD_CONST:
                    if (arg >= 0 && arg < constants.Count)
                    {
                        var constant = constants[arg];
                        var constRepr = FormatConstantForDisplay(constant);
                        return $"{arg,15} ({constRepr})";
                    }
                    return $"{arg,15}";
                    
                case ByteCodeOp.LOAD_NAME:
                case ByteCodeOp.STORE_NAME:
                    if (arg >= 0 && arg < names.Count)
                        return $"{arg,15} ({names[arg]})";
                    return $"{arg,15}";

                case ByteCodeOp.LOAD_ATTR:
                case ByteCodeOp.STORE_ATTR:
                    if (arg >= 0 && arg < names.Count)
                        return $"{arg,15} ({names[arg]})";
                    return $"{arg,15}";

                case ByteCodeOp.LOAD_GLOBAL:
                case ByteCodeOp.STORE_GLOBAL:
                case ByteCodeOp.LOAD_GLOBAL_BUILTIN:
                    if (arg >= 0 && arg < names.Count)
                        return $"{arg,15} ({names[arg]})";
                    return $"{arg,15}";

                case ByteCodeOp.LOAD_FAST:
                case ByteCodeOp.STORE_FAST:
                    if (arg >= 0 && arg < varNames.Count)
                        return $"{arg,15} ({varNames[arg]})";
                    return $"{arg,15}";
                    
                case ByteCodeOp.CALL:
                    return $"{arg,15}";

                case ByteCodeOp.BINARY_OP:
                    // CPython 3.12: BINARY_OP argument shows operation type
                    var binaryOpType = (BinaryOpType)arg;
                    var opSymbol = GetBinaryOpSymbol(binaryOpType);
                    return $"{arg,15} ({opSymbol})";

                case ByteCodeOp.COMPARE_OP:
                    // CPython 3.12: COMPARE_OP argument shows comparison type
                    var compareOp = GetCompareOpSymbol(arg);
                    return $"{arg,15} ({compareOp})";

                case ByteCodeOp.CONTAINS_OP:
                    // CPython 3.12: CONTAINS_OP for membership tests
                    var containsOp = arg == 0 ? "in" : "not in";
                    return $"{arg,15} ({containsOp})";

                case ByteCodeOp.IS_OP:
                    // CPython 3.12: IS_OP for identity tests
                    var isOp = arg == 0 ? "is" : "is not";
                    return $"{arg,15} ({isOp})";

                case ByteCodeOp.FOR_ITER:
                    var forIterTarget = currentByteOffset + 2 + (arg * 2);
                    return $"{arg,15} (to {forIterTarget})";
                    
                case ByteCodeOp.JUMP_BACKWARD:
                    // CPython 3.12 exact formula from dis.py:
                    // argval = offset + 2 + (-arg * 2)
                    var jumpBackwardTarget = currentByteOffset + 2 + (-arg * 2);
                    return $"{arg,15} (to {jumpBackwardTarget})";
                    
                case ByteCodeOp.JUMP_FORWARD:
                    // CPython 3.12: Jump target = offset + 2 + arg*2
                    var jumpForwardTarget = currentByteOffset + 2 + arg * 2;
                    return $"{arg,15} (to {jumpForwardTarget})";
                    
                case ByteCodeOp.POP_JUMP_IF_FALSE:
                case ByteCodeOp.POP_JUMP_IF_TRUE:
                    // CPython 3.12: Jump target = offset + 2 + arg*2 
                    var jumpTarget = currentByteOffset + 2 + arg * 2;
                    return $"{arg,15} (to {jumpTarget})";
                    
                case ByteCodeOp.RETURN_CONST:
                    if (arg >= 0 && arg < constants.Count)
                    {
                        var constant = constants[arg];
                        var constRepr = constant?.ToString() ?? "None";
                        return $"{arg,15} ({constRepr})";
                    }
                    return $"{arg,15}";
                    
                case ByteCodeOp.FORMAT_VALUE:
                    return $"{arg,15}";
                    
                default:
                    return arg > 0 ? $"{arg,15}" : "";
            }
        }

        /// <summary>
        /// Binary operation argument를 symbol로 변환
        /// </summary>
        private string GetBinaryOpSymbol(BinaryOpType binaryOpType)
        {
            return binaryOpType switch
            {
                BinaryOpType.ADD => "+",
                BinaryOpType.SUBTRACT => "-",
                BinaryOpType.MULTIPLY => "*",
                BinaryOpType.TRUE_DIVIDE => "/",
                BinaryOpType.FLOOR_DIVIDE => "//",
                BinaryOpType.MODULO => "%",
                BinaryOpType.POWER => "**",
                BinaryOpType.LSHIFT => "<<",
                BinaryOpType.RSHIFT => ">>",
                BinaryOpType.OR => "|",
                BinaryOpType.XOR => "^",
                BinaryOpType.AND => "&",
                BinaryOpType.MATRIX_MULTIPLY => "@",
                _ => binaryOpType.ToString()
            };
        }

        /// <summary>
        /// Compare operation argument를 symbol로 변환
        /// </summary>
        private string GetCompareOpSymbol(int compareOp)
        {
            return compareOp switch
            {
                // CPython 3.12 실제 enum 값들
                2 => "<",          // CompareOp.LT
                26 => "<=",        // CompareOp.LE
                40 => "==",        // CompareOp.EQ
                55 => "!=",        // CompareOp.NE
                68 => ">",         // CompareOp.GT
                92 => ">=",        // CompareOp.GE
                8 => "exception match", // CompareOp.EXC_MATCH
                _ => compareOp.ToString()
            };
        }
    }
}