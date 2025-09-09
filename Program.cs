using System;

namespace SharpPy
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // 명령줄 옵션 파싱
            var (parsedArgs, pythonFile) = ParseCommandLineArgs(args);
            
            // --dis 옵션 처리 (직접 바이트코드 출력)
            if (parsedArgs.ContainsKey("--dis"))
            {
                if (!string.IsNullOrEmpty(pythonFile))
                {
                    RunDirectDisassembly(pythonFile);
                }
                else
                {
                    Console.WriteLine("사용법: dotnet run --dis <python_file>");
                    Console.WriteLine("예제: dotnet run --dis test.py");
                    Environment.Exit(1);
                }
                return;
            }
            
            // -m 옵션 처리 (CPython 호환)
            if (parsedArgs.ContainsKey("-m"))
            {
                SharpPyConfig.ShowBytecode = parsedArgs["-m"] == "dis";
                RunModuleMode(args);
                return;
            }
            
            // 만약 Python 파일이 인수로 주어지면 실행
            if (!string.IsNullOrEmpty(pythonFile))
            {
                // 기본 모드 (Python-style): 깔끔한 출력
                if (SharpPyConfig.ShouldShowDebugInfo)
                {
                    Console.WriteLine("🐍 SharpPy - Python Interpreter in C#");
                    Console.WriteLine("=====================================\n");
                    Console.WriteLine($"📄 Python 파일 실행: {pythonFile}");
                }
                
                try
                {
                    string code = System.IO.File.ReadAllText(pythonFile);
                    var interpreter = new IntegratedPythonInterpreter();
                    interpreter.Execute(code, pythonFile);
                }
                catch (Exception ex)
                {
                    if (SharpPyConfig.ShouldShowErrors)
                    {
                        // Use ToString() to get full CPython-style error message with traceback
                        Console.WriteLine(ex.ToString());
                    }
                }
                return;
            }
            
            // 개발/테스트 모드들
            if (args.Length > 0)
            {
                switch (args[0])
                {
                    case "test-iteration":
                        // ManualIterationTest.RunTest(); // Class not found
                        Console.WriteLine("test-iteration 기능이 현재 비활성화되어 있습니다.");
                        return;
                        
                    case "test-try-except":
                        // TryExceptASTTest.RunTest(); // Class not found
                        Console.WriteLine("test-try-except 기능이 현재 비활성화되어 있습니다.");
                        return;
                        
                    case "demo":
                        RunAllDemos();
                        return;
                        
                    case "help":
                    case "--help":
                    case "-h":
                        ShowHelp();
                        return;
                }
            }

            // 기본 모드: REPL 실행
            var repl = new SharpPy.Core.SharpPyRepl();
            repl.Start();
        }

        private static void RunModuleMode(string[] args)
        {
            // args: ["-m", "module_name", ...additional_args]
            string moduleName = args[1];
            string[] moduleArgs = args.Length > 2 ? args[2..] : new string[0];

            try
            {
                switch (moduleName)
                {
                    case "dis":
                        RunDisModule(moduleArgs);
                        break;
                        
                    default:
                        Console.WriteLine($"❌ 모듈을 찾을 수 없습니다: {moduleName}");
                        Console.WriteLine("지원되는 모듈:");
                        Console.WriteLine("  dis - 바이트코드 디스어셈블러");
                        Environment.Exit(1);
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 모듈 실행 오류: {ex.Message}");
                Environment.Exit(1);
            }
        }

        private static void RunDirectDisassembly(string pythonFile)
        {
            if (!System.IO.File.Exists(pythonFile))
            {
                Console.WriteLine($"❌ 파일을 찾을 수 없습니다: {pythonFile}");
                Environment.Exit(1);
            }

            try
            {
                // 컴파일러에서 직접 바이트코드 출력 (PyDisModule 우회)
                Console.WriteLine($"Disassembly of {pythonFile}:");
                
                string code = System.IO.File.ReadAllText(pythonFile);
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

        private static void ShowActualBytecode(PyCodeObject codeObject)
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
                currentByteOffset += PythonCompiler.GetCPythonInstructionSize(instruction.OpCode, instruction.Argument);
            }
        }
        
        private static string FormatActualInstruction(int instructionIndex, int byteOffset, 
                                                    ByteCodeInstruction instruction,
                                                    List<PyObject> constants, 
                                                    List<string> names, 
                                                    List<string> varNames)
        {
            var sb = new System.Text.StringBuilder();
            
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
        
        private static string GetActualArgumentInfo(ByteCodeInstruction instruction, int currentByteOffset,
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
                    
                case ByteCodeOp.RETURN_CONST:
                    if (arg >= 0 && arg < constants.Count)
                    {
                        var constant = constants[arg];
                        var constRepr = constant?.ToString() ?? "None";
                        return $"{arg,15} ({constRepr})";
                    }
                    return $"{arg,15}";
                    
                default:
                    return arg > 0 ? $"{arg,15}" : "";
            }
        }

        private static void RunDisModule(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("사용법: dotnet run -m dis <python_file>");
                Console.WriteLine("예제: dotnet run -m dis test.py");
                Environment.Exit(1);
            }

            string pythonFile = args[0];
            
            if (!System.IO.File.Exists(pythonFile))
            {
                Console.WriteLine($"❌ 파일을 찾을 수 없습니다: {pythonFile}");
                Environment.Exit(1);
            }
        }

        private static void ShowHelp()
        {
            Console.WriteLine("🐍 SharpPy - Python Interpreter in C#");
            Console.WriteLine("=====================================\n");
            Console.WriteLine("사용법:");
            Console.WriteLine("  dotnet run                    - REPL 모드로 실행 (대화형)");
            Console.WriteLine("  dotnet run <file.py>          - Python 파일 실행");
            Console.WriteLine("  dotnet run --dis <file.py>    - 바이트코드 직접 출력 (정확한 오프셋)");
            Console.WriteLine("  dotnet run -m <module> <args> - 모듈 실행 (CPython 호환)");
            Console.WriteLine("  dotnet run demo               - 모든 데모 실행");
            Console.WriteLine("  dotnet run test-iteration     - 반복자 테스트");
            Console.WriteLine("  dotnet run test-try-except    - 예외 처리 테스트");
            Console.WriteLine("  dotnet run help               - 이 도움말 표시");
            Console.WriteLine("\n바이트코드 옵션:");
            Console.WriteLine("  dotnet run --dis <file.py>    - 실제 바이트코드 출력 (권장)");
            Console.WriteLine("  dotnet run -m dis <file.py>   - dis 모듈 사용 (참고용)");
            Console.WriteLine("\n예제:");
            Console.WriteLine("  dotnet run                    # REPL 시작");
            Console.WriteLine("  dotnet run hello.py           # hello.py 파일 실행");
            Console.WriteLine("  dotnet run -m dis test.py     # test.py 바이트코드 보기");
            Console.WriteLine("  dotnet run demo               # 모든 데모 보기");
        }

        private static void RunAllDemos()
        {
            Console.WriteLine("🐍 SharpPy - Python Interpreter in C#");
            Console.WriteLine("=====================================\n");
            
            // 기존 데모들
            Console.WriteLine("📋 기본 시스템 데모:");
            Demo.CompletePythonSystemDemo.Demo();
            
            Console.WriteLine("\n🖥️  VM 통합 데모:");
            Demo.VMIntegrationDemo.Demo();
            
            Console.WriteLine("\n🔍 Python 3.12 문법 검증:");
            var syntaxTest = new SharpPy.Verification.Python312SyntaxVerification();
            syntaxTest.VerifyAllSyntax();

            // 새로 개선된 타입들 테스트
            Console.WriteLine("\n🧪 개선된 타입 시스템 테스트:");
            Console.WriteLine("-----------------------------");
            
            // PyFloat 테스트
            Console.WriteLine("\n🔢 PyFloat 테스트:");
            // SharpPy.Tests.PyFloatDemo.RunDemo(); // Class not found
            Console.WriteLine("PyFloat 테스트 기능이 현재 비활성화되어 있습니다.");

            // 새로운 Python 타입들 테스트
            Console.WriteLine("\n🆕 새로운 Python 타입 테스트:");
            Console.WriteLine("------------------------------");
            // SharpPy.Tests.NewTypesDemo.RunDemo(); // Class not found
            Console.WriteLine("NewTypes 테스트 기능이 현재 비활성화되어 있습니다.");

            // 기본 파서 디버깅
            Console.WriteLine("\n🔧 기본 파서 디버깅:");
            Console.WriteLine("====================");
            // ParserDebug.TestBasicParsing();  // Commented out - class not found
            
            // 컴프리헨션 파싱 테스트
            Console.WriteLine("\n🧪 컴프리헨션 파싱 테스트:");
            Console.WriteLine("==========================");
            // ComprehensionParsingTest.TestComprehensionParsing(); // Class not found
            // ComprehensionParsingTest.TestFileBasedParsing(); // Class not found
            Console.WriteLine("컴프리헨션 파싱 테스트 기능이 현재 비활성화되어 있습니다.");

            Console.WriteLine("\n✅ 모든 테스트 완료!");
        }
        
        /// <summary>
        /// 명령줄 인수를 파싱하여 옵션과 Python 파일을 분리
        /// </summary>
        /// <param name="args">명령줄 인수 배열</param>
        /// <returns>(옵션 딕셔너리, Python 파일 경로)</returns>
        private static (System.Collections.Generic.Dictionary<string, string> options, string pythonFile) ParseCommandLineArgs(string[] args)
        {
            var options = new System.Collections.Generic.Dictionary<string, string>();
            string pythonFile = null;
            
            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                
                // 옵션 플래그들
                switch (arg)
                {
                    case "--verbose":
                    case "-v":
                        SharpPyConfig.VerboseMode = true;
                        break;
                        
                    case "--quiet":
                    case "-q":
                        SharpPyConfig.QuietMode = true;
                        break;
                        
                    case "--dis":
                        options["--dis"] = "true";
                        break;
                        
                    case "-m":
                        if (i + 1 < args.Length)
                        {
                            options["-m"] = args[i + 1];
                            i++; // 다음 인수 건너뛰기
                        }
                        break;
                        
                    case "help":
                    case "--help":
                    case "-h":
                        options["help"] = "true";
                        break;
                        
                    default:
                        // Python 파일이나 기타 명령어
                        if (arg.EndsWith(".py"))
                        {
                            pythonFile = arg;
                        }
                        else if (!arg.StartsWith("-") && pythonFile == null)
                        {
                            // 특별한 명령어들 (demo, test-iteration 등)
                            options["command"] = arg;
                        }
                        break;
                }
            }
            
            return (options, pythonFile);
        }
    }
}
