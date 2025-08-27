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
            // def 문 (함수 정의)
            if (line.StartsWith("def "))
            {
                return ParseFunctionDef(line);
            }
            
            // if 문
            if (line.StartsWith("if "))
            {
                return ParseIfStatement(line);
            }
            
            // while 문
            if (line.StartsWith("while "))
            {
                return ParseWhileStatement(line);
            }
            
            // for 문
            if (line.StartsWith("for "))
            {
                return ParseForStatement(line);
            }
            
            // try 문
            if (line.StartsWith("try:"))
            {
                return ParseTryStatement(line);
            }
            
            // class 문
            if (line.StartsWith("class "))
            {
                return ParseClassDef(line);
            }
            
            // type 문 (Python 3.12 PEP 695)
            if (line.StartsWith("type "))
            {
                return ParseTypeAlias(line);
            }
            
            // with 문
            if (line.StartsWith("with "))
            {
                return ParseWithStatement(line);
            }
            
            // async def 문
            if (line.StartsWith("async def "))
            {
                return ParseAsyncFunctionDef(line);
            }
            
            // assert 문
            if (line.StartsWith("assert "))
            {
                return ParseAssertStatement(line);
            }
            
            // del 문
            if (line.StartsWith("del "))
            {
                return ParseDelStatement(line);
            }
            
            // global 문
            if (line.StartsWith("global "))
            {
                return ParseGlobalStatement(line);
            }
            
            // nonlocal 문
            if (line.StartsWith("nonlocal "))
            {
                return ParseNonlocalStatement(line);
            }
            
            // match 문 (Python 3.10+)
            if (line.StartsWith("match "))
            {
                return ParseMatchStatement(line);
            }
            
            // import 문
            if (line.StartsWith("import ") || line.StartsWith("from "))
            {
                return ParseImportStatement(line);
            }
            
            // 할당문: x = 값 (연산자 확장)
            if (line.Contains(" = ") && !line.Contains("==") && !line.Contains(":="))
            {
                var parts = line.Split('=', 2);
                var varName = parts[0].Trim();
                var valueStr = parts[1].Trim();
                
                var valueExpr = ParseExpression(valueStr);
                return new AssignStatement(varName, valueExpr);
            }
            
            // 복합 할당문: +=, -=, *=, /=, //=, %=, **=, &=, |=, ^=, >>=, <<=
            var compoundOps = new[] { "+=", "-=", "*=", "/=", "//=", "%=", "**=", "&=", "|=", "^=", ">>=", "<<="};
            foreach (var op in compoundOps)
            {
                if (line.Contains($" {op} "))
                {
                    var parts = line.Split(new[] { $" {op} " }, 2, StringSplitOptions.None);
                    var varName = parts[0].Trim();
                    var valueStr = parts[1].Trim();
                    return new AugAssignStatement(varName, op, ParseExpression(valueStr));
                }
            }
            
            // Walrus operator (:=)
            if (line.Contains(":="))
            {
                var parts = line.Split(new[] { ":=" }, 2, StringSplitOptions.None);
                var varName = parts[0].Trim();
                var valueStr = parts[1].Trim();
                return new WalrusStatement(varName, ParseExpression(valueStr));
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
            
            // yield 문
            if (line.StartsWith("yield"))
            {
                // yield from 처리
                if (line.StartsWith("yield from"))
                {
                    var valueStr = line.Substring(10).Trim();
                    return new YieldFromStatement(ParseExpression(valueStr));
                }
                else
                {
                    var valueStr = line.Substring(5).Trim();
                    if (string.IsNullOrEmpty(valueStr))
                        return new YieldStatement();
                    else
                        return new YieldStatement(ParseExpression(valueStr));
                }
            }
            
            // await 표현식
            if (line.StartsWith("await "))
            {
                var valueStr = line.Substring(6).Trim();
                return new ExpressionStatement(new AwaitExpression(ParseExpression(valueStr)));
            }
            
            // break, continue, pass 문
            if (line == "break") return new BreakStatement();
            if (line == "continue") return new ContinueStatement();
            if (line == "pass") return new PassStatement();
            
            // raise 문
            if (line.StartsWith("raise"))
            {
                var valueStr = line.Substring(5).Trim();
                if (string.IsNullOrEmpty(valueStr))
                    return new RaiseStatement();
                else
                    return new RaiseStatement(ParseExpression(valueStr));
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
            
            // 리스트 리터럴: [1, 2, 3]
            if (expr.StartsWith("[") && expr.EndsWith("]"))
            {
                var content = expr.Substring(1, expr.Length - 2).Trim();
                var elements = new List<Expression>();
                if (!string.IsNullOrEmpty(content))
                {
                    var parts = content.Split(',');
                    foreach (var part in parts)
                    {
                        elements.Add(ParseExpression(part.Trim()));
                    }
                }
                return new ListExpression(elements);
            }
            
            // 튜플 리터럴: (1, 2, 3)
            if (expr.StartsWith("(") && expr.EndsWith(")") && expr.Contains(","))
            {
                var content = expr.Substring(1, expr.Length - 2).Trim();
                var elements = new List<Expression>();
                var parts = content.Split(',');
                foreach (var part in parts)
                {
                    elements.Add(ParseExpression(part.Trim()));
                }
                return new TupleExpression(elements);
            }
            
            // 딕셔너리 리터럴: {"key": "value"}
            if (expr.StartsWith("{") && expr.EndsWith("}"))
            {
                return new DictExpression(new Dictionary<Expression, Expression>());
            }
            
            // 세트 리터럴: {1, 2, 3}
            if (expr.StartsWith("{") && expr.EndsWith("}") && !expr.Contains(":"))
            {
                return new SetExpression(new List<Expression>());
            }
            
            // f-string: f"hello {name}"
            if (expr.StartsWith("f\"") || expr.StartsWith("f'"))
            {
                return new FStringExpression(expr);
            }
            
            // 람다: lambda x: x + 1
            if (expr.StartsWith("lambda "))
            {
                return ParseLambda(expr);
            }
            
            // 비교 연산자 확장
            var compOps = new[] { " == ", " != ", " < ", " > ", " <= ", " >= ", " is ", " is not ", " in ", " not in " };
            foreach (var op in compOps)
            {
                if (expr.Contains(op))
                {
                    var parts = expr.Split(new[] { op }, 2, StringSplitOptions.None);
                    return new CompareExpression(ParseExpression(parts[0]), op.Trim(), ParseExpression(parts[1]));
                }
            }
            
            // 논리 연산자: and, or, not
            if (expr.Contains(" and "))
            {
                var parts = expr.Split(new[] { " and " }, 2, StringSplitOptions.None);
                return new BoolOpExpression("and", new List<Expression> { ParseExpression(parts[0]), ParseExpression(parts[1]) });
            }
            if (expr.Contains(" or "))
            {
                var parts = expr.Split(new[] { " or " }, 2, StringSplitOptions.None);
                return new BoolOpExpression("or", new List<Expression> { ParseExpression(parts[0]), ParseExpression(parts[1]) });
            }
            if (expr.StartsWith("not "))
            {
                return new UnaryOpExpression("not", ParseExpression(expr.Substring(4)));
            }
            
            // 인덱싱: obj[key]
            if (expr.Contains("[") && expr.EndsWith("]"))
            {
                var bracketIndex = expr.IndexOf('[');
                var objExpr = ParseExpression(expr.Substring(0, bracketIndex));
                var keyExpr = ParseExpression(expr.Substring(bracketIndex + 1, expr.Length - bracketIndex - 2));
                return new SubscriptExpression(objExpr, keyExpr);
            }
            
            // 슬라이싱: obj[start:end:step]
            if (expr.Contains(":") && expr.Contains("["))
            {
                // 간단한 슬라이싱 파싱
                return new SliceExpression(expr);
            }
            
            // 조건식 (삼항 연산자): a if condition else b
            if (expr.Contains(" if ") && expr.Contains(" else "))
            {
                return ParseConditionalExpression(expr);
            }
            
            // await 표현식
            if (expr.StartsWith("await "))
            {
                var valueStr = expr.Substring(6).Trim();
                return new AwaitExpression(ParseExpression(valueStr));
            }
            
            // type parameter expressions: T, *Args, **Kwargs
            if (expr.StartsWith("**"))
            {
                return new ParamSpecExpression(expr.Substring(2));
            }
            if (expr.StartsWith("*") && !expr.Contains(" "))
            {
                return new TypeVarTupleExpression(expr.Substring(1));
            }
            
            // 변수 이름
            return new NameExpression(expr);
        }
        
        // 추가 파싱 메서드들
        private Statement ParseFunctionDef(string line)
        {
            // def func[T, U](x: T) -> U: ...
            var name = "function";
            var parameters = new List<string>();
            var body = new List<Statement>();
            var typeParams = new List<string>(); // Python 3.12 type parameters
            
            // Type parameters 처리 [T, U]
            if (line.Contains("[") && line.Contains("]") && line.IndexOf("[") < line.IndexOf("("))
            {
                var bracketStart = line.IndexOf('[');
                var bracketEnd = line.IndexOf(']');
                var typeParamStr = line.Substring(bracketStart + 1, bracketEnd - bracketStart - 1);
                typeParams = ParseTypeParameters(typeParamStr);
            }
            
            return new FunctionDefStatement(name, parameters, body, typeParams);
        }
        
        private Statement ParseIfStatement(string line)
        {
            var condition = ParseExpression("True");
            var body = new List<Statement>();
            var orelse = new List<Statement>();
            return new IfStatement(condition, body, orelse);
        }
        
        private Statement ParseWhileStatement(string line)
        {
            var condition = ParseExpression("True");
            var body = new List<Statement>();
            return new WhileStatement(condition, body);
        }
        
        private Statement ParseForStatement(string line)
        {
            var target = "item";
            var iter = ParseExpression("[]");
            var body = new List<Statement>();
            return new ForStatement(target, iter, body);
        }
        
        private Statement ParseTryStatement(string line)
        {
            var body = new List<Statement>();
            var handlers = new List<ExceptHandler>();
            return new TryStatement(body, handlers);
        }
        
        private Statement ParseClassDef(string line)
        {
            var name = "Class";
            var bases = new List<Expression>();
            var body = new List<Statement>();
            var typeParams = new List<string>(); // Python 3.12 type parameters
            return new ClassDefStatement(name, bases, body, typeParams);
        }
        
        private Statement ParseTypeAlias(string line)
        {
            // type MyType[T] = SomeType
            var parts = line.Split('=', 2);
            var leftPart = parts[0].Substring(5).Trim(); // "type " 제거
            var valuePart = parts.Length > 1 ? parts[1].Trim() : "object";
            
            var name = "TypeAlias";
            var typeParams = new List<string>();
            
            // type parameters [T, U] 처리
            if (leftPart.Contains("["))
            {
                var bracketStart = leftPart.IndexOf('[');
                name = leftPart.Substring(0, bracketStart).Trim();
                var paramsPart = leftPart.Substring(bracketStart + 1, leftPart.LastIndexOf(']') - bracketStart - 1);
                typeParams = paramsPart.Split(',').Select(p => p.Trim()).ToList();
            }
            else
            {
                name = leftPart;
            }
            
            return new TypeAliasStatement(name, typeParams, ParseExpression(valuePart));
        }
        
        private Statement ParseWithStatement(string line)
        {
            var items = new List<WithItem>();
            var body = new List<Statement>();
            return new WithStatement(items, body);
        }
        
        private Statement ParseAsyncFunctionDef(string line)
        {
            // async def 제거 후 일반 함수와 동일하게 처리
            var funcLine = line.Substring(6); // "async " 제거
            var name = "async_function";
            var parameters = new List<string>();
            var body = new List<Statement>();
            var typeParams = new List<string>();
            return new AsyncFunctionDefStatement(name, parameters, body, typeParams);
        }
        
        private Statement ParseAssertStatement(string line)
        {
            var condition = ParseExpression("True");
            Expression msg = null;
            return new AssertStatement(condition, msg);
        }
        
        private Statement ParseDelStatement(string line)
        {
            var targets = new List<Expression>();
            return new DeleteStatement(targets);
        }
        
        private Statement ParseGlobalStatement(string line)
        {
            var names = new List<string> { "global_var" };
            return new GlobalStatement(names);
        }
        
        private Statement ParseNonlocalStatement(string line)
        {
            var names = new List<string> { "nonlocal_var" };
            return new NonlocalStatement(names);
        }
        
        private Statement ParseMatchStatement(string line)
        {
            var subject = ParseExpression("value");
            var cases = new List<MatchCase>();
            return new MatchStatement(subject, cases);
        }
        
        private Statement ParseImportStatement(string line)
        {
            var names = new List<string> { "module" };
            if (line.StartsWith("from "))
                return new ImportFromStatement("module", names);
            else
                return new ImportStatement(names);
        }
        
        private Expression ParseLambda(string expr)
        {
            // lambda x, y: x + y
            var parts = expr.Split(new[] { ':' }, 2);
            var paramsPart = parts[0].Substring(7).Trim(); // "lambda " 제거
            var bodyPart = parts[1].Trim();
            
            var parameters = new List<string>();
            if (!string.IsNullOrEmpty(paramsPart))
            {
                parameters = paramsPart.Split(',').Select(p => p.Trim()).ToList();
            }
            
            return new LambdaExpression(parameters, ParseExpression(bodyPart));
        }
        
        private Expression ParseConditionalExpression(string expr)
        {
            // a if condition else b
            var ifIndex = expr.IndexOf(" if ");
            var elseIndex = expr.IndexOf(" else ");
            
            var body = expr.Substring(0, ifIndex).Trim();
            var test = expr.Substring(ifIndex + 4, elseIndex - ifIndex - 4).Trim();
            var orelse = expr.Substring(elseIndex + 5).Trim();
            
            return new ConditionalExpression(
                ParseExpression(test),
                ParseExpression(body),
                ParseExpression(orelse)
            );
        }
        
        // Type parameter 파싱 (Python 3.12)
        private List<string> ParseTypeParameters(string typeParamStr)
        {
            var typeParams = new List<string>();
            if (string.IsNullOrEmpty(typeParamStr)) return typeParams;
            
            var parts = typeParamStr.Split(',');
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                // T, *Args, **Kwargs 처리
                if (trimmed.StartsWith("**"))
                    typeParams.Add(trimmed); // ParamSpec
                else if (trimmed.StartsWith("*"))
                    typeParams.Add(trimmed); // TypeVarTuple
                else
                    typeParams.Add(trimmed); // TypeVar
            }
            
            return typeParams;
        }
    }

    #endregion
}