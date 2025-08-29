using System;

namespace SharpPy
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("🐍 SharpPy - Python Interpreter in C#");
            Console.WriteLine("=====================================\n");

            // 만약 Python 파일이 인수로 주어지면 실행
            if (args.Length > 0 && args[0].EndsWith(".py"))
            {
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
            
            // Manual iteration test
            if (args.Length > 0 && args[0] == "test-iteration")
            {
                ManualIterationTest.RunTest();
                return;
            }
            
            // Try/Except AST test
            if (args.Length > 0 && args[0] == "test-try-except")
            {
                TryExceptASTTest.RunTest();
                return;
            }

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

            // Python 파이프라인 통합 테스트 (삭제됨)
            // Console.WriteLine("\n🔄 Python 파이프라인 통합 테스트:");
            // Console.WriteLine("=================================");
            // SharpPy.Tests.PipelineDemo.RunDemo();

            // Python 3.12 호환성 분석
            Console.WriteLine("\n🔍 Python 3.12 호환성 분석:");
            Console.WriteLine("===============================");

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
