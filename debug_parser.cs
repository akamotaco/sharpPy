using System;

namespace SharpPy
{
    public static class ParserDebug
    {
        public static void TestBasicParsing()
        {
            Console.WriteLine("🔧 파서 디버깅 시작");
            Console.WriteLine("===================");
            
            var testCases = new[]
            {
                "x = 42",
                "y = 3.14", 
                "name = 'hello'",
                "42",
                "x + y",
                "print(x)"
            };
            
            foreach (var test in testCases)
            {
                Console.WriteLine($"\n🧪 테스트: {test}");
                try
                {
                    // 먼저 토큰화 확인
                    var lexer = new PyLexer(test);
                    var tokens = lexer.Tokenize();
                    
                    Console.WriteLine($"  📝 토큰들: {string.Join(", ", tokens.ConvertAll(t => $"{t.Type}({t.Lexeme})"))}");
                    
                    // 파싱 시도
                    var parser = new PyParser(tokens);
                    var statements = parser.Parse();
                    
                    if (statements.Count > 0)
                    {
                        Console.WriteLine($"  ✅ 파싱 성공: {statements[0].GetType().Name}");
                    }
                    else
                    {
                        Console.WriteLine($"  ❌ 파싱 실패: 문장 없음");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ❌ 예외: {ex.Message}");
                }
            }
        }
    }
}