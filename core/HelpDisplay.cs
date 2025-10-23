using System;

namespace SharpPy.Core
{
    /// <summary>
    /// 도움말 표시를 전담하는 클래스
    /// </summary>
    public class HelpDisplay
    {
        /// <summary>
        /// 프로그램 사용법 도움말을 표시
        /// </summary>
        public void ShowHelp()
        {
            Console.WriteLine("🐍 SharpPy - Python Interpreter in C#");
            Console.WriteLine("=====================================\n");
            Console.WriteLine("사용법:");
            Console.WriteLine("  dotnet run                       - REPL 모드로 실행 (대화형)");
            Console.WriteLine("  dotnet run <file.py>             - Python 파일 실행");
            Console.WriteLine("  dotnet run --dis <file.py>       - 바이트코드 직접 출력 (정확한 오프셋)");
            Console.WriteLine("  dotnet run --ast <file.py>       - AST 트리 출력 (CPython 3.12 호환)");
            Console.WriteLine("  dotnet run --tokens <file.py>    - 토큰 출력");
            Console.WriteLine("  dotnet run -c \"code\"             - 코드 문자열 직접 실행");
            Console.WriteLine("  dotnet run -m <module> <args>    - 모듈 실행 (CPython 호환)");
            Console.WriteLine("  dotnet run demo                  - 모든 데모 실행");
            Console.WriteLine("  dotnet run test-iteration        - 반복자 테스트");
            Console.WriteLine("  dotnet run test-try-except       - 예외 처리 테스트");
            Console.WriteLine("  dotnet run help                  - 이 도움말 표시");
            Console.WriteLine("\n바이트코드 옵션:");
            Console.WriteLine("  dotnet run --dis <file.py>    - 실제 바이트코드 출력 (권장)");
            Console.WriteLine("  dotnet run -m dis <file.py>   - dis 모듈 사용 (참고용)");
            Console.WriteLine("\n예제:");
            Console.WriteLine("  dotnet run                    # REPL 시작");
            Console.WriteLine("  dotnet run hello.py           # hello.py 파일 실행");
            Console.WriteLine("  dotnet run -m dis test.py     # test.py 바이트코드 보기");
            Console.WriteLine("  dotnet run demo               # 모든 데모 보기");
        }
    }
}