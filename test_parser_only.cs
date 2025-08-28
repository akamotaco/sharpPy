using System;

namespace SharpPy
{
    public class TestParserOnly
    {
        public static void TestMain()
        {
            Console.WriteLine("=== 간단한 파서 테스트 ===");
            
            var test = "x = 42";
            Console.WriteLine($"테스트: {test}");
            
            try
            {
                // 토큰화
                var lexer = new PyLexer(test);
                var tokens = lexer.Tokenize();
                
                Console.WriteLine($"토큰들: {string.Join(", ", tokens.ConvertAll(t => $"{t.Type}({t.Lexeme})"))}");
                
                // 파싱
                var parser = new PyParser(tokens);
                var statements = parser.Parse();
                
                Console.WriteLine($"파싱 결과: {statements.Count}개 문장");
                if (statements.Count > 0)
                {
                    Console.WriteLine($"첫 번째 문장: {statements[0].GetType().Name}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"에러: {ex.Message}");
                Console.WriteLine($"스택 트레이스: {ex.StackTrace}");
            }
        }
    }
}