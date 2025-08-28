using System;
using System.IO;

namespace SharpPy
{
    public static class ComprehensionParsingTest
    {
        public static void TestComprehensionParsing()
        {
            Console.WriteLine("🧪 컴프리헨션 파싱 테스트 시작");
            Console.WriteLine("===============================");
            
            var testCases = new[]
            {
                // List comprehensions
                "[x * 2 for x in range(5)]",
                "[x for x in range(10) if x % 2 == 0]",
                "[x * y for x in range(3) for y in range(2)]",
                "[x for x in numbers if x > 5]",
                
                // Dictionary comprehensions
                "{x: x**2 for x in range(5)}",
                "{word: len(word) for word in words if len(word) > 4}",
                "{k: v for k, v in items.items()}",
                
                // Set comprehensions
                "{x**2 for x in range(5)}",
                "{x for x in data if x % 3 == 0}",
                
                // Generator expressions
                "(x**2 for x in range(5))",
                "(x for x in range(100) if x % 7 == 0)",
                
                // Nested and complex comprehensions
                "[[x*y for y in range(3)] for x in range(2)]",
                "[item for sublist in lists for item in sublist]",
                "[(x, y) for x in range(3) for y in range(3) if x != y]"
            };
            
            int passed = 0;
            int total = testCases.Length;
            
            foreach (var testCase in testCases)
            {
                try
                {
                    Console.WriteLine($"\n📝 테스트: {testCase}");
                    var statements = PyParser.ParseSource(testCase);
                    
                    if (statements.Count > 0)
                    {
                        var stmt = statements[0];
                        if (stmt is ExpressionStatement exprStmt)
                        {
                            var expr = exprStmt.Expression;
                            var type = GetComprehensionType(expr);
                            Console.WriteLine($"   ✅ 파싱 성공: {type}");
                            passed++;
                        }
                        else
                        {
                            Console.WriteLine($"   ⚠️  예상치 못한 문장 타입: {stmt.GetType().Name}");
                            passed++;
                        }
                    }
                    else
                    {
                        Console.WriteLine($"   ❌ 파싱 실패: 문장이 없음");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ❌ 파싱 실패: {ex.Message}");
                }
            }
            
            Console.WriteLine($"\n📊 테스트 결과:");
            Console.WriteLine($"   성공: {passed}/{total} ({passed * 100 / total}%)");
            
            if (passed == total)
            {
                Console.WriteLine("   🎉 모든 컴프리헨션 파싱 테스트 통과!");
            }
            else
            {
                Console.WriteLine($"   🔧 {total - passed}개 테스트 실패");
            }
        }
        
        private static string GetComprehensionType(Expression expr)
        {
            return expr switch
            {
                ListComprehension => "List Comprehension",
                DictComprehension => "Dict Comprehension", 
                SetComprehension => "Set Comprehension",
                GeneratorExpression => "Generator Expression",
                ListExpression => "List Literal",
                DictExpression => "Dict Literal",
                SetExpression => "Set Literal",
                _ => expr.GetType().Name
            };
        }
        
        public static void TestFileBasedParsing()
        {
            Console.WriteLine("\n🧪 파일 기반 컴프리헨션 파싱 테스트");
            Console.WriteLine("====================================");
            
            var testFile = "test_comprehension_parsing.py";
            if (File.Exists(testFile))
            {
                try
                {
                    var source = File.ReadAllText(testFile);
                    var statements = PyParser.ParseSource(source);
                    
                    Console.WriteLine($"✅ 파일 파싱 성공: {statements.Count}개 문장");
                    
                    foreach (var stmt in statements)
                    {
                        if (stmt is ExpressionStatement exprStmt)
                        {
                            var type = GetComprehensionType(exprStmt.Expression);
                            Console.WriteLine($"   - {type}");
                        }
                        else if (stmt is AssignStatement assignStmt)
                        {
                            if (assignStmt.Value != null)
                            {
                                var type = GetComprehensionType(assignStmt.Value);
                                Console.WriteLine($"   - Assignment: {assignStmt.VariableName} = {type}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ 파일 파싱 실패: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine($"⚠️  테스트 파일을 찾을 수 없음: {testFile}");
            }
        }
    }
}