namespace SharpPy
{
    #region Parser Extension

    // 간단한 파서 (소스 → AST)
    public class SimpleParser
    {
        public List<Statement> Parse(string source)
        {
            var statements = new List<Statement>();
            var lines = source.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            Console.WriteLine($"\n📝 파싱: {lines.Length}줄");
            
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                    continue;
                    
                var statement = ParseLine(trimmed);
                if (statement != null)
                {
                    statements.Add(statement);
                    Console.WriteLine($"  → {statement}");
                }
            }
            
            Console.WriteLine($"✅ 파싱 완료: {statements.Count}개 문장");
            return statements;
        }
        
        private Statement ParseLine(string line)
        {
            // 할당문: x = 값
            if (line.Contains(" = ") && !line.Contains("=="))
            {
                var parts = line.Split('=', 2);
                var varName = parts[0].Trim();
                var valueStr = parts[1].Trim();
                
                var valueExpr = ParseExpression(valueStr);
                return new AssignStatement(varName, valueExpr);
            }
            
            // return 문
            if (line.StartsWith("return"))
            {
                var valueStr = line.Substring(6).Trim();
                if (string.IsNullOrEmpty(valueStr))
                    return new ReturnStatement();
                else
                    return new ReturnStatement(ParseExpression(valueStr));
            }
            
            // 표현식 문
            var expr = ParseExpression(line);
            return new ExpressionStatement(expr);
        }
        
        private Expression ParseExpression(string expr)
        {
            expr = expr.Trim();
            
            // 정수 상수
            if (int.TryParse(expr, out int intValue))
            {
                return new ConstantExpression(new PyInt(intValue));
            }
            
            // 문자열 상수
            if ((expr.StartsWith("\"") && expr.EndsWith("\"")) || 
                (expr.StartsWith("'") && expr.EndsWith("'")))
            {
                var strValue = expr.Substring(1, expr.Length - 2);
                return new ConstantExpression(new PyString(strValue));
            }
            
            // 함수 호출: func(args)
            if (expr.Contains("(") && expr.EndsWith(")"))
            {
                var parenIndex = expr.IndexOf('(');
                var funcName = expr.Substring(0, parenIndex).Trim();
                var argsStr = expr.Substring(parenIndex + 1, expr.Length - parenIndex - 2);
                
                var funcExpr = new NameExpression(funcName);
                var args = new List<Expression>();
                
                if (!string.IsNullOrEmpty(argsStr))
                {
                    var argStrs = argsStr.Split(',');
                    foreach (var argStr in argStrs)
                    {
                        args.Add(ParseExpression(argStr.Trim()));
                    }
                }
                
                return new CallExpression(funcExpr, args);
            }
            
            // 이항 연산 (단순한 구현)
            foreach (var op in new[] { " + ", " - ", " * ", " / " })
            {
                if (expr.Contains(op))
                {
                    var parts = expr.Split(new[] { op }, 2, StringSplitOptions.None);
                    return new BinaryOpExpression(
                        ParseExpression(parts[0]), 
                        op.Trim(), 
                        ParseExpression(parts[1])
                    );
                }
            }
            
            // 속성 접근: obj.attr
            if (expr.Contains("."))
            {
                var parts = expr.Split('.', 2);
                // 단순화를 위해 NameExpression으로 처리
                return new NameExpression(expr);
            }
            
            // 변수 이름
            return new NameExpression(expr);
        }
    }

    #endregion
}