using System;

namespace SharpPy
{
    public class Program
    {
        public static void Main()
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
