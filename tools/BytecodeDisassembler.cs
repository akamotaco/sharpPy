using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

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
                var lexer = new PyLexer(code);
                var tokens = lexer.Tokenize();
                var parser = new PyParser(tokens);
                var ast = parser.Parse();
                
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
                        var constRepr = constant?.ToString() ?? "None";
                        return $"{arg,15} ({constRepr})";
                    }
                    return $"{arg,15}";
                    
                case ByteCodeOp.LOAD_NAME:
                case ByteCodeOp.STORE_NAME:
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
    }
}