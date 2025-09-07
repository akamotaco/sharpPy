using System;

namespace SharpPy
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // 명령줄 옵션 파싱
            var (parsedArgs, pythonFile) = ParseCommandLineArgs(args);
            
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
                        ManualIterationTest.RunTest();
                        return;
                        
                    case "test-try-except":
                        TryExceptASTTest.RunTest();
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

            try
            {
                // 디스어셈블리 전용 모드 설정 (디버그 출력 숨김)
                SharpPyConfig.DisassemblyOnlyMode = true;
                
                var disModule = new SharpPy.Modules.PyDisModule();
                disModule.DisassembleFile(pythonFile);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 디스어셈블리 오류: {ex.Message}");
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
            Console.WriteLine("  dotnet run -m <module> <args> - 모듈 실행 (CPython 호환)");
            Console.WriteLine("  dotnet run demo               - 모든 데모 실행");
            Console.WriteLine("  dotnet run test-iteration     - 반복자 테스트");
            Console.WriteLine("  dotnet run test-try-except    - 예외 처리 테스트");
            Console.WriteLine("  dotnet run help               - 이 도움말 표시");
            Console.WriteLine("\n-m 옵션 (모듈 실행):");
            Console.WriteLine("  dotnet run -m dis <file.py>   - 바이트코드 디스어셈블리");
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
            SharpPy.Tests.PyFloatDemo.RunDemo();

            // 새로운 Python 타입들 테스트
            Console.WriteLine("\n🆕 새로운 Python 타입 테스트:");
            Console.WriteLine("------------------------------");
            SharpPy.Tests.NewTypesDemo.RunDemo();

            // 기본 파서 디버깅
            Console.WriteLine("\n🔧 기본 파서 디버깅:");
            Console.WriteLine("====================");
            // ParserDebug.TestBasicParsing();  // Commented out - class not found
            
            // 컴프리헨션 파싱 테스트
            Console.WriteLine("\n🧪 컴프리헨션 파싱 테스트:");
            Console.WriteLine("==========================");
            ComprehensionParsingTest.TestComprehensionParsing();
            ComprehensionParsingTest.TestFileBasedParsing();

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
