using System;

namespace SharpPy
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // 만약 Python 파일이 인수로 주어지면 실행
            if (args.Length > 0 && args[0].EndsWith(".py"))
            {
                Console.WriteLine("🐍 SharpPy - Python Interpreter in C#");
                Console.WriteLine("=====================================\n");
                Console.WriteLine($"📄 Python 파일 실행: {args[0]}");
                try
                {
                    string code = System.IO.File.ReadAllText(args[0]);
                    var interpreter = new IntegratedPythonInterpreter();
                    interpreter.Execute(code);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ 실행 오류: {ex.Message}");
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
            var repl = new SharpPyRepl();
            repl.Start();
        }

        private static void ShowHelp()
        {
            Console.WriteLine("🐍 SharpPy - Python Interpreter in C#");
            Console.WriteLine("=====================================\n");
            Console.WriteLine("사용법:");
            Console.WriteLine("  dotnet run                    - REPL 모드로 실행 (대화형)");
            Console.WriteLine("  dotnet run <file.py>          - Python 파일 실행");
            Console.WriteLine("  dotnet run demo               - 모든 데모 실행");
            Console.WriteLine("  dotnet run test-iteration     - 반복자 테스트");
            Console.WriteLine("  dotnet run test-try-except    - 예외 처리 테스트");
            Console.WriteLine("  dotnet run help               - 이 도움말 표시");
            Console.WriteLine("\n예제:");
            Console.WriteLine("  dotnet run                    # REPL 시작");
            Console.WriteLine("  dotnet run hello.py           # hello.py 파일 실행");
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
            ParserDebug.TestBasicParsing();
            
            // 컴프리헨션 파싱 테스트
            Console.WriteLine("\n🧪 컴프리헨션 파싱 테스트:");
            Console.WriteLine("==========================");
            ComprehensionParsingTest.TestComprehensionParsing();
            ComprehensionParsingTest.TestFileBasedParsing();

            Console.WriteLine("\n✅ 모든 테스트 완료!");
        }
    }
}
