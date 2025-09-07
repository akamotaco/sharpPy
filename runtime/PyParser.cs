using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpPy
{
    #region Parser Extension (Token -> AST)

    /// <summary>
    /// 토큰 기반 파서 (Token -> AST)
    /// </summary>
    public class PyParser
    {
        private readonly List<PyToken> _tokens;
        private int _current;
        private bool _inAsyncFunction = false; // CPython 3.12 await context validation
        private bool _inComprehension = false; // Comprehension parsing context
        private bool _inMatchPattern = false; // Match pattern parsing context
        
        // CPython-style precedence table
        private static readonly Dictionary<TokenType, int> OperatorPrecedence = new()
        {
            { TokenType.OR, 1 },           // or
            { TokenType.AND, 2 },          // and  
            // Comparison operators - all same precedence (CPython style)
            { TokenType.EQUAL_EQUAL, 3 },  // ==
            { TokenType.BANG_EQUAL, 3 },   // !=
            { TokenType.LESS, 3 },         // <
            { TokenType.GREATER, 3 },      // >
            { TokenType.LESS_EQUAL, 3 },   // <=
            { TokenType.GREATER_EQUAL, 3 }, // >=
            { TokenType.IN, 3 },           // in ✅ Added
            { TokenType.IS, 3 },           // is ✅ Added
            { TokenType.NOT_IN, 3 },       // not in ✅ CPython 3.12
            { TokenType.IS_NOT, 3 },       // is not ✅ CPython 3.12
            // Bitwise operators
            { TokenType.PIPE, 4 },         // |
            { TokenType.CARET, 5 },        // ^
            { TokenType.AMPERSAND, 6 },    // &
            { TokenType.LEFT_SHIFT, 7 },   // <<
            { TokenType.RIGHT_SHIFT, 7 },  // >>
            // Arithmetic operators
            { TokenType.PLUS, 8 },         // +
            { TokenType.MINUS, 8 },        // -
            { TokenType.STAR, 9 },         // *
            { TokenType.SLASH, 9 },        // /
            { TokenType.SLASH_SLASH, 9 },  // //
            { TokenType.PERCENT, 9 },      // %
            { TokenType.STAR_STAR, 10 },   // ** (highest)
        };
        
        private static readonly Dictionary<TokenType, bool> RightAssociative = new()
        {
            { TokenType.STAR_STAR, true }, // ** is right-associative
        };
        
        // Memoization cache for complex expressions
        private readonly Dictionary<(int position, string rule), (Expression? result, int newPosition)> _memoCache 
            = new();

        // Infinite loop prevention system
        private const int MAX_RECURSION_DEPTH = 1000;
        private const int MAX_PARSING_ITERATIONS = 50000;
        private int _recursionDepth = 0;
        private int _parsingIterations = 0;
        private readonly Dictionary<int, int> _positionVisitCount = new();

        public PyParser(List<PyToken> tokens)
        {
            _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            _current = 0;
        }

        /// <summary>
        /// 편의 메서드: 소스 코드에서 직접 파싱
        /// </summary>
        public static List<Statement> ParseSource(string source)
        {
            var lexer = new PyLexer(source);
            var tokens = lexer.Tokenize();
            var parser = new PyParser(tokens);
            return parser.Parse();
        }

        /// <summary>
        /// 토큰을 AST로 파싱
        /// </summary>
        public List<Statement> Parse()
        {
            var statements = new List<Statement>();
            
            if (!SharpPyConfig.DisassemblyOnlyMode)
            {
                Console.WriteLine($"\n📝 파싱: {_tokens.Count}개 토큰");
            }
            
            while (!IsAtEnd())
            {
                // Infinite loop prevention
                CheckParsingProgress();
                
                // Skip newlines at statement level
                if (Check(TokenType.NEWLINE))
                {
                    Advance();
                    continue;
                }

                // CPython 3.12: Skip semicolons as statement separators (like newlines)
                if (Check(TokenType.SEMICOLON))
                {
                    Advance();
                    continue;
                }

                try
                {
                    var statement = ParseStatement();
                    if (statement != null)
                    {
                        statements.Add(statement);
                        if (!SharpPyConfig.DisassemblyOnlyMode)
                        {
                            Console.WriteLine($"  → {statement}");
                        }
                        
                        // CPython 3.12: After parsing a statement, consume optional semicolon
                        if (Check(TokenType.SEMICOLON))
                        {
                            Advance(); // consume semicolon as statement terminator
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (!SharpPyConfig.DisassemblyOnlyMode)
                    {
                        Console.WriteLine($"  ❌ 파싱 에러: {ex.Message}");
                    }
                    // Skip to next statement
                    Synchronize();
                }
            }
            
            if (!SharpPyConfig.DisassemblyOnlyMode)
            {
                Console.WriteLine($"✅ 파싱 완료: {statements.Count}개 문장");
            }
            return statements;
        }

        private Statement? ParseStatement()
        {
            return WithRecursionProtection("ParseStatement", () =>
            {
                // CPython 3.12: Handle unexpected INDENT tokens that might be part of expressions
                if (Check(TokenType.INDENT))
                {
                    // This might be an INDENT within a multiline expression, not a block
                    // Skip it for now and try to continue parsing
                    if (!SharpPyConfig.DisassemblyOnlyMode)
                    {
                        Console.WriteLine("⚠️ Warning: Encountered INDENT in statement context - might be multiline expression");
                    }
                    SkipIndentationTokens();
                }
                
                // Check for decorators first
                if (Check(TokenType.AT))
                {
                    return ParseDecoratedStatement();
                }
            
            // Function definition
            if (Match(TokenType.DEF)) return ParseFunctionDef();
            if (Match(TokenType.ASYNC) && Check(TokenType.DEF)) return ParseAsyncFunctionDef();
            
            // Class definition
            if (Match(TokenType.CLASS)) return ParseClassDef();
            
            // Type alias (Python 3.12)
            if (Match(TokenType.TYPE)) return ParseTypeAlias();
            
            // Control flow
            if (Match(TokenType.IF)) return ParseIfStatement();
            if (Match(TokenType.WHILE)) return ParseWhileStatement();
            if (Match(TokenType.FOR)) return ParseForStatement();
            if (Match(TokenType.TRY)) return ParseTryStatement();
            if (Match(TokenType.WITH)) return ParseWithStatement();
            // CPython 3.12: match is a soft keyword, check context
            if (IsMatchStatementStart())
            {
                Advance(); // consume "match" identifier
                return ParseMatchStatement();
            }
            
            // Simple statements
            if (Match(TokenType.RETURN)) return ParseReturnStatement();
            if (Match(TokenType.YIELD)) return ParseYieldStatement();
            if (Match(TokenType.BREAK)) return ParseBreakStatement();
            if (Match(TokenType.CONTINUE)) return ParseContinueStatement();
            if (Match(TokenType.PASS)) return ParsePassStatement();
            if (Match(TokenType.ASSERT)) return ParseAssertStatement();
            if (Match(TokenType.RAISE)) return ParseRaiseStatement();
            if (Match(TokenType.DEL)) return ParseDeleteStatement();
            if (Match(TokenType.GLOBAL)) return ParseGlobalStatement();
            if (Match(TokenType.NONLOCAL)) return ParseNonlocalStatement();
            if (Match(TokenType.IMPORT)) return ParseImportStatement();
            if (Match(TokenType.FROM)) return ParseFromImportStatement();
            
                // Expression statement or assignment
                return ParseExpressionOrAssignment();
            });
        }

        private Statement ParseFunctionDef(List<DecoratorExpression>? decorators = null)
        {
            var name = Consume(TokenType.IDENTIFIER, "Expected function name").Lexeme;
            
            // Type parameters (Python 3.12)
            var typeParams = new List<string>();
            if (Match(TokenType.LEFT_BRACKET))
            {
                typeParams = ParseTypeParameters();
                Consume(TokenType.RIGHT_BRACKET, "Expected ']' after type parameters");
            }
            
            Consume(TokenType.LEFT_PAREN, "Expected '(' after function name");
            var parameters = ParseParameterList();
            Consume(TokenType.RIGHT_PAREN, "Expected ')' after parameters");
            
            // Check for return type annotation (->)
            if (Check(TokenType.MINUS) && CheckNext(TokenType.GREATER))
            {
                Advance(); // consume MINUS
                Advance(); // consume GREATER
                // Parse return type annotation (currently just skip it)
                var returnType = ParseExpression();
            }
            
            Consume(TokenType.COLON, "Expected ':' after function signature");
            
            var body = ParseBlockOrSingleStatement();
            
            return new FunctionDefStatement(name, parameters, body, typeParams, decorators);
        }

        private Statement ParseAsyncFunctionDef()
        {
            Consume(TokenType.DEF, "Expected 'def' after 'async'");
            
            var name = Consume(TokenType.IDENTIFIER, "Expected function name").Lexeme;
            
            // Type parameters (Python 3.12)
            var typeParams = new List<string>();
            if (Match(TokenType.LEFT_BRACKET))
            {
                typeParams = ParseTypeParameters();
                Consume(TokenType.RIGHT_BRACKET, "Expected ']' after type parameters");
            }
            
            Consume(TokenType.LEFT_PAREN, "Expected '(' after function name");
            var parameters = ParseParameterList();
            Consume(TokenType.RIGHT_PAREN, "Expected ')' after parameters");
            
            // Check for return type annotation (->)
            if (Check(TokenType.MINUS) && CheckNext(TokenType.GREATER))
            {
                Advance(); // consume MINUS
                Advance(); // consume GREATER
                // Parse return type annotation (currently just skip it)
                var returnType = ParseExpression();
            }
            
            Consume(TokenType.COLON, "Expected ':' after function signature");
            
            // CPython 3.12: async function 내부에서 await 사용 가능
            var previousAsyncContext = _inAsyncFunction;
            _inAsyncFunction = true;
            
            try
            {
                var body = ParseBlockOrSingleStatement();
                return new AsyncFunctionDefStatement(name, parameters, body, typeParams);
            }
            finally
            {
                _inAsyncFunction = previousAsyncContext; // 이전 컨텍스트 복원
            }
        }

        /// <summary>
        /// 데코레이터가 있는 문장을 파싱
        /// </summary>
        private Statement ParseDecoratedStatement()
        {
            var decorators = ParseDecorators();
            
            // 데코레이터 다음에는 함수나 클래스 정의만 올 수 있음
            if (Match(TokenType.DEF))
            {
                return ParseFunctionDef(decorators);
            }
            else if (Match(TokenType.ASYNC) && Check(TokenType.DEF))
            {
                Consume(TokenType.DEF, "Expected 'def' after 'async'");
                return ParseAsyncFunctionDef(); // TODO: async 함수도 데코레이터 지원 필요
            }
            else if (Match(TokenType.CLASS))
            {
                return ParseClassDef(); // TODO: 클래스도 데코레이터 지원 필요
            }
            else
            {
                throw new Exception("Decorators can only be applied to functions and classes");
            }
        }

        /// <summary>
        /// 데코레이터 목록 파싱 (@decorator1 @decorator2 ...)
        /// </summary>
        private List<DecoratorExpression> ParseDecorators()
        {
            var decorators = new List<DecoratorExpression>();
            
            while (Check(TokenType.AT))
            {
                decorators.Add(ParseSingleDecorator());
                SkipNewlines(); // 데코레이터 사이의 개행 허용
            }
            
            return decorators;
        }

        /// <summary>
        /// 단일 데코레이터 파싱 (@decorator or @decorator(args))
        /// </summary>
        private DecoratorExpression ParseSingleDecorator()
        {
            Consume(TokenType.AT, "Expected '@'");
            
            // 데코레이터 함수 이름 파싱 (dotted name 지원: @module.decorator)
            var decoratorFunc = ParseDottedName();
            
            // 데코레이터에 인수가 있는 경우
            List<Expression>? arguments = null;
            if (Match(TokenType.LEFT_PAREN))
            {
                arguments = new List<Expression>();
                if (!Check(TokenType.RIGHT_PAREN))
                {
                    do
                    {
                        arguments.Add(ParseExpression());
                    } while (Match(TokenType.COMMA));
                }
                Consume(TokenType.RIGHT_PAREN, "Expected ')' after decorator arguments");
            }
            
            return new DecoratorExpression(decoratorFunc, arguments);
        }

        /// <summary>
        /// Dotted name 파싱 (module.submodule.name)
        /// </summary>
        private Expression ParseDottedName()
        {
            var name = Consume(TokenType.IDENTIFIER, "Expected decorator name").Lexeme;
            Expression expr = new NameExpression(name);
            
            while (Match(TokenType.DOT))
            {
                var attrName = Consume(TokenType.IDENTIFIER, "Expected attribute name after '.'").Lexeme;
                expr = new AttributeExpression(expr, attrName);
            }
            
            return expr;
        }

        private Statement ParseClassDef()
        {
            var name = Consume(TokenType.IDENTIFIER, "Expected class name").Lexeme;
            
            // Type parameters (Python 3.12)
            var typeParams = new List<string>();
            if (Match(TokenType.LEFT_BRACKET))
            {
                typeParams = ParseTypeParameters();
                Consume(TokenType.RIGHT_BRACKET, "Expected ']' after type parameters");
            }
            
            // Base classes and metaclass
            var bases = new List<Expression>();
            Expression? metaclass = null;
            
            if (Match(TokenType.LEFT_PAREN))
            {
                if (!Check(TokenType.RIGHT_PAREN))
                {
                    do
                    {
                        // Check for metaclass= keyword
                        if (Check(TokenType.IDENTIFIER) && Peek().Lexeme == "metaclass" && PeekNext().Type == TokenType.EQUAL)
                        {
                            Advance(); // consume 'metaclass'
                            Advance(); // consume '='
                            metaclass = ParseExpression();
                        }
                        else
                        {
                            bases.Add(ParseExpression());
                        }
                    } while (Match(TokenType.COMMA));
                }
                Consume(TokenType.RIGHT_PAREN, "Expected ')' after base classes");
            }
            
            Consume(TokenType.COLON, "Expected ':' after class header");
            
            var body = ParseBlockOrSingleStatement();
            
            return new ClassDefStatement(name, bases, body, typeParams, metaclass);
        }

        private Statement ParseTypeAlias()
        {
            var name = Consume(TokenType.IDENTIFIER, "Expected type alias name").Lexeme;
            
            // Type parameters (Python 3.12)
            var typeParams = new List<string>();
            if (Match(TokenType.LEFT_BRACKET))
            {
                typeParams = ParseTypeParameters();
                Consume(TokenType.RIGHT_BRACKET, "Expected ']' after type parameters");
            }
            
            Consume(TokenType.EQUAL, "Expected '=' in type alias");
            var value = ParseExpression();
            
            return new TypeAliasStatement(name, value, typeParams);
        }

        private List<string> ParseTypeParameters()
        {
            return WithRecursionProtection("ParseTypeParameters", () =>
            {
                var typeParams = new List<string>();
                var paramCount = 0;
                const int MAX_TYPE_PARAMS = 100; // Safety limit for type parameters
                
                do
                {
                    CheckParsingProgress();
                    
                    // Safety check for too many type parameters
                    if (++paramCount > MAX_TYPE_PARAMS)
                    {
                        throw new Exception($"🚨 Too many type parameters (max {MAX_TYPE_PARAMS}): possible infinite loop");
                    }
                    
                    string param;
                    
                    // Handle *Ts (TypeVarTuple) and **P (ParamSpec)
                    if (Check(TokenType.STAR_STAR))
                    {
                        // **P (ParamSpec) - PEP 612
                        Advance(); // consume **
                        var paramName = Consume(TokenType.IDENTIFIER, "Expected type parameter name after **").Lexeme;
                        param = "**" + paramName;
                    }
                    else if (Check(TokenType.STAR))
                    {
                        // *Ts (TypeVarTuple) - PEP 646
                        Advance(); // consume *
                        var paramName = Consume(TokenType.IDENTIFIER, "Expected type parameter name after *").Lexeme;
                        param = "*" + paramName;
                    }
                    else
                    {
                        // Regular type parameter T
                        param = Consume(TokenType.IDENTIFIER, "Expected type parameter name").Lexeme;
                        
                        // Handle type constraints (T: int) - PEP 695
                        if (Check(TokenType.COLON))
                        {
                            Advance(); // consume :
                            try
                            {
                                // Parse constraint type but ignore for now (basic implementation)
                                var constraint = ParseExpression();
                                // TODO: Store constraint information for runtime type checking
                            }
                            catch (Exception ex)
                            {
                                if (!SharpPyConfig.DisassemblyOnlyMode)
                                {
                                    Console.WriteLine($"⚠️ Type constraint parsing failed: {ex.Message}");
                                }
                                // Continue parsing without constraint
                            }
                        }
                    }
                    
                    // Prevent duplicate type parameters
                    if (typeParams.Any(p => p.TrimStart('*') == param.TrimStart('*')))
                    {
                        throw new Exception($"Duplicate type parameter: {param}");
                    }
                    
                    typeParams.Add(param);
                } while (Match(TokenType.COMMA) && !IsAtEnd());
                
                return typeParams;
            });
        }

        private List<string> ParseParameterList()
        {
            var parameters = new List<string>();
            
            if (!Check(TokenType.RIGHT_PAREN))
            {
                do
                {
                    // Handle *args and **kwargs (CPython style)
                    if (Check(TokenType.STAR_STAR))
                    {
                        // **kwargs
                        Advance(); // consume **
                        var kwargsParam = Consume(TokenType.IDENTIFIER, "Expected parameter name after **").Lexeme;
                        
                        // PEP 692: **kwargs 타입 주석 처리
                        if (Match(TokenType.COLON))
                        {
                            // **kwargs: Unpack[TypedDict] 파싱
                            var typeAnnotation = ParseExpression();
                            // 향후 타입 검증을 위해 매개변수에 타입 정보 저장 (현재는 단순 저장)
                            parameters.Add("**" + kwargsParam + ":" + typeAnnotation?.ToString());
                        }
                        else
                        {
                            parameters.Add("**" + kwargsParam);
                        }
                    }
                    else if (Check(TokenType.STAR))
                    {
                        // *args
                        Advance(); // consume *
                        var argsParam = Consume(TokenType.IDENTIFIER, "Expected parameter name after *").Lexeme;
                        parameters.Add("*" + argsParam);
                    }
                    else
                    {
                        // Regular parameter (CPython 호환 방식)
                        var param = Consume(TokenType.IDENTIFIER, "Expected parameter name").Lexeme;
                        var paramString = param;
                        
                        // Type annotation 처리
                        if (Match(TokenType.COLON))
                        {
                            var typeAnnotation = ParseExpression();
                            paramString += ":" + typeAnnotation?.ToString();
                        }
                        
                        // Default value 처리 (CPython style)
                        if (Match(TokenType.EQUAL))
                        {
                            var defaultValue = ParseExpression();
                            paramString += "=" + defaultValue?.ToString();
                        }
                        
                        parameters.Add(paramString);
                    }
                } while (Match(TokenType.COMMA));
            }
            
            return parameters;
        }

        private List<Statement> ParseBlockOrSingleStatement()
        {
            var statements = new List<Statement>();
            
            // CPython 3.12 방식: 정확한 블록 파싱
            
            // NEWLINE 토큰이 있다면 소비 (블록이 시작됨을 의미)
            if (Match(TokenType.NEWLINE))
            {
                // 블록 구문 - 들여쓰기된 문장들
                
                // INDENT 토큰을 기대
                if (!Check(TokenType.INDENT))
                {
                    throw new Exception("Expected an indented block after ':'");
                }
                
                // INDENT 토큰 소비
                Advance();
                
                // DEDENT 토큰이 나올 때까지 문장들을 파싱
                while (!IsAtEnd() && !Check(TokenType.DEDENT) && !Check(TokenType.EOF))
                {
                    // 빈 줄은 건너뛰기
                    if (Match(TokenType.NEWLINE))
                    {
                        continue;
                    }
                    
                    var stmt = ParseStatement();
                    if (stmt != null)
                    {
                        statements.Add(stmt);
                    }
                }
                
                // DEDENT 토큰 소비
                if (Check(TokenType.DEDENT))
                {
                    Advance();
                }
            }
            else
            {
                // 한 줄 구문 - 같은 줄에 있는 단일 문장
                var stmt = ParseStatement();
                if (stmt != null)
                {
                    statements.Add(stmt);
                }
            }
            
            // 빈 블록이면 pass 문 추가 (CPython과 동일)
            if (statements.Count == 0)
            {
                statements.Add(new PassStatement());
            }
            
            return statements;
        }

        /// <summary>
        /// Check if the current token indicates the end of a block
        /// 이제 DEDENT 토큰을 사용하므로 단순화
        /// </summary>
        private bool IsBlockTerminator()
        {
            if (IsAtEnd()) return true;
            
            // INDENT/DEDENT 토큰을 사용하므로 DEDENT만 체크
            return Check(TokenType.DEDENT) || Check(TokenType.EOF);
        }

        private Statement ParseExpressionOrAssignment()
        {
            // CPython 3.12: 블록 구조 토큰들은 표현식이 아니므로 건너뛰기
            if (Check(TokenType.INDENT) || Check(TokenType.DEDENT) || 
                Check(TokenType.EXCEPT) || Check(TokenType.FINALLY) ||
                Check(TokenType.ELSE) || Check(TokenType.ELIF))
            {
                return null;
            }
            
            try
            {
                var expr = ParseExpression();
                
                // CPython 3.12: Check for comma-separated assignment targets (tuple unpacking)
                // This handles cases like: x, y = (1, 2)
                if (Check(TokenType.COMMA))
                {
                    // Look ahead to see if this is an assignment context
                    var savedPos = _current;
                    var targets = new List<Expression> { expr };
                    
                    // Collect all comma-separated expressions
                    while (Match(TokenType.COMMA))
                    {
                        targets.Add(ParseExpression());
                    }
                    
                    // Check if this is actually an assignment
                    if (Check(TokenType.EQUAL))
                    {
                        // This is tuple unpacking assignment: x, y = value
                        Advance(); // consume '='
                        
                        // Parse the right-hand side - could be a single value or comma-separated tuple
                        var firstValue = ParseExpression();
                        var value = firstValue;
                        
                        // Check if there are more comma-separated values on the right side
                        if (Check(TokenType.COMMA))
                        {
                            var rightValues = new List<Expression> { firstValue };
                            while (Match(TokenType.COMMA))
                            {
                                rightValues.Add(ParseExpression());
                            }
                            value = new TupleExpression(rightValues);
                        }
                        
                        var tupleTarget = new TupleExpression(targets);
                        return new AssignTargetStatement(tupleTarget, value);
                    }
                    else
                    {
                        // Not an assignment, backtrack and treat as expression
                        // This would be a case like: x, y  (standalone tuple expression)
                        _current = savedPos;
                        var tupleExpr = new TupleExpression(new List<Expression> { expr });
                        while (Match(TokenType.COMMA))
                        {
                            tupleExpr.Elements.Add(ParseExpression());
                        }
                        return new ExpressionStatement(tupleExpr);
                    }
                }
                
                // CPython 3.12: Check for annotated assignment (target: type = value)
                if (Match(TokenType.COLON))
                {
                    var annotation = ParseExpression();
                    
                    // Check if there's an assignment as well
                    if (Match(TokenType.EQUAL))
                    {
                        var value = ParseExpression();
                        
                        // Handle different annotation targets
                        if (expr is NameExpression nameExpr)
                        {
                            return new AnnAssignStatement(nameExpr.Name, annotation, value);
                        }
                        else
                        {
                            // For complex targets like self.attr, use general assignment
                            return new AssignTargetStatement(expr, value);
                        }
                    }
                    else
                    {
                        // Type annotation only, no assignment
                        if (expr is NameExpression nameExpr)
                        {
                            return new AnnAssignStatement(nameExpr.Name, annotation);
                        }
                        else
                        {
                            // Just ignore complex type annotations without assignment for now
                            return new ExpressionStatement(expr);
                        }
                    }
                }
                // Check for regular assignment
                else if (Match(TokenType.EQUAL))
                {
                    // CPython 3.12: Support various assignment targets
                    var value = ParseExpression();
                    
                    // Check if it's a simple name assignment (backward compatibility)
                    if (expr is NameExpression nameExpr)
                    {
                        return new AssignStatement(nameExpr.Name, value);
                    }
                    
                    // Use general assignment target for complex targets (attribute, subscript, etc.)
                    return new AssignTargetStatement(expr, value);
                }
                
                // Check for augmented assignment (CPython style - separate from expression parsing)
                if (IsAugmentedAssignmentOperator())
                {
                    return ParseAugmentedAssignment(expr);
                }
                
                // Check for walrus operator
                if (Match(TokenType.WALRUS))
                {
                    if (expr is NameExpression nameExpr)
                    {
                        var value = ParseExpression();
                        return new WalrusStatement(nameExpr.Name, value);
                    }
                    throw new Exception("Invalid walrus operator target");
                }
                
                return new ExpressionStatement(expr);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to parse expression or assignment at {Peek()}: {ex.Message}");
            }
        }

        private Expression ParseExpression()
        {
            // Lambda expressions have lowest precedence
            if (Check(TokenType.LAMBDA))
            {
                return ParseLambdaExpression();
            }
            
            return ParseConditionalExpression();
        }

        private Expression ParseConditionalExpression()
        {
            // Lambda expressions have lowest precedence, allow them in lambda bodies
            if (Check(TokenType.LAMBDA))
            {
                return ParseLambdaExpression();
            }
            
            // Use new binary expression parser for better precedence handling
            var expr = ParseBinaryExpression();
            
            // Check for conditional: expr if condition else alternative  
            // Skip conditional expression parsing when inside comprehension or match pattern
            if (Check(TokenType.IF) && !_inComprehension && !_inMatchPattern)
            {
                Advance(); // consume IF
                var condition = ParseBinaryExpression();
                
                if (!Check(TokenType.ELSE))
                {
                    // Better error handling - might be incomplete conditional
                    return expr; // Return partial expression instead of failing
                }
                
                Consume(TokenType.ELSE, "Expected 'else' in conditional expression");
                var alternative = ParseConditionalExpression(); // Right-associative
                
                return new ConditionalExpression(expr, condition, alternative);
            }
            
            return expr;
        }

        private Expression ParseOrExpression()
        {
            var expr = ParseAndExpression();
            
            while (Match(TokenType.OR))
            {
                var op = Previous().Lexeme;
                var right = ParseAndExpression();
                expr = new BinaryOpExpression(expr, op, right);
            }
            
            return expr;
        }

        private Expression ParseAndExpression()
        {
            var expr = ParseComparisonExpression();
            
            while (Match(TokenType.AND))
            {
                var op = Previous().Lexeme;
                var right = ParseComparisonExpression();
                expr = new BinaryOpExpression(expr, op, right);
            }
            
            return expr;
        }


        /// <summary>
        /// CPython Grammar-based comparison parsing
        /// Based on: comparison: bitwise_or compare_op_bitwise_or_pair*
        /// compare_op includes: notin_bitwise_or: 'not' 'in' bitwise_or
        /// </summary>
        private Expression ParseComparisonExpression()
        {
            // Parse left operand first - same as CPython's bitwise_or
            var expr = ParseUnaryExpression();
            
            // CPython 3.12 Grammar: comparison: expr (comp_op expr)*
            // Handle compound operators with PEG-style pattern matching
            while (true)
            {
                string op = TryMatchComparisonOperator();
                if (op == null) break;
                
                var right = ParseUnaryExpression();
                expr = new CompareExpression(expr, op, right);
            }
            
            return expr;
        }
        
        /// <summary>
        /// CPython PEG-style compound operator matching
        /// Follows exact Grammar/python.gram patterns
        /// </summary>
        private string TryMatchComparisonOperator()
        {
            // CPython Grammar order: compound operators first
            // notin_bitwise_or: 'not' 'in' bitwise_or
            if (IsCurrentNotInOperator())
            {
                Advance(); // consume 'not'
                Advance(); // consume 'in'  
                return "not in";
            }
            
            // isnot_bitwise_or: 'is' 'not' bitwise_or
            if (IsCurrentIsNotOperator())
            {
                Advance(); // consume 'is'
                Advance(); // consume 'not'
                return "is not";
            }
            
            // Single token operators
            if (Match(TokenType.EQUAL_EQUAL)) return "==";
            if (Match(TokenType.BANG_EQUAL)) return "!=";
            if (Match(TokenType.LESS_EQUAL)) return "<=";
            if (Match(TokenType.GREATER_EQUAL)) return ">=";
            if (Match(TokenType.LESS)) return "<";
            if (Match(TokenType.GREATER)) return ">";
            if (Match(TokenType.IN)) return "in";
            if (Match(TokenType.IS)) return "is";
            
            return null; // No comparison operator found
        }

        private Expression ParseBitwiseOrExpression()
        {
            var expr = ParseBitwiseXorExpression();
            
            while (Match(TokenType.PIPE))
            {
                var op = Previous().Lexeme;
                var right = ParseBitwiseXorExpression();
                expr = new BinaryOpExpression(expr, op, right);
            }
            
            return expr;
        }

        private Expression ParseBitwiseXorExpression()
        {
            var expr = ParseBitwiseAndExpression();
            
            while (Match(TokenType.CARET))
            {
                var op = Previous().Lexeme;
                var right = ParseBitwiseAndExpression();
                expr = new BinaryOpExpression(expr, op, right);
            }
            
            return expr;
        }

        private Expression ParseBitwiseAndExpression()
        {
            var expr = ParseShiftExpression();
            
            while (Match(TokenType.AMPERSAND))
            {
                var op = Previous().Lexeme;
                var right = ParseShiftExpression();
                expr = new BinaryOpExpression(expr, op, right);
            }
            
            return expr;
        }

        private Expression ParseShiftExpression()
        {
            var expr = ParseAdditionExpression();
            
            while (Match(TokenType.LEFT_SHIFT, TokenType.RIGHT_SHIFT))
            {
                var op = Previous().Lexeme;
                var right = ParseAdditionExpression();
                expr = new BinaryOpExpression(expr, op, right);
            }
            
            return expr;
        }

        private Expression ParseAdditionExpression()
        {
            var expr = ParseMultiplicationExpression();
            
            while (Match(TokenType.PLUS, TokenType.MINUS))
            {
                var op = Previous().Lexeme;
                var right = ParseMultiplicationExpression();
                expr = new BinaryOpExpression(expr, op, right);
            }
            
            return expr;
        }

        private Expression ParseMultiplicationExpression()
        {
            var expr = ParseUnaryExpression();
            
            while (Match(TokenType.STAR, TokenType.SLASH, TokenType.SLASH_SLASH, TokenType.PERCENT))
            {
                var op = Previous().Lexeme;
                var right = ParseUnaryExpression();
                expr = new BinaryOpExpression(expr, op, right);
            }
            
            return expr;
        }

        private Expression ParseUnaryExpression()
        {
            if (Match(TokenType.PLUS, TokenType.MINUS, TokenType.TILDE, TokenType.NOT))
            {
                var op = Previous().Lexeme;
                var expr = ParseUnaryExpression();
                return new UnaryOpExpression(op, expr);
            }
            
            if (Match(TokenType.AWAIT))
            {
                // CPython 3.12: await는 async def 내부에서만 사용 가능
                if (!_inAsyncFunction)
                {
                    throw CreateSyntaxError("'await' outside async function");
                }
                
                var expr = ParseUnaryExpression();
                return new AwaitExpression(expr);
            }
            
            return ParseBitwiseOrExpression();
        }

        private Expression ParsePowerExpression()
        {
            var expr = ParsePostfixExpression();
            
            if (Match(TokenType.STAR_STAR))
            {
                var op = Previous().Lexeme;
                var right = ParseUnaryExpression(); // Right-associative
                return new BinaryOpExpression(expr, op, right);
            }
            
            return expr;
        }
        
        private Expression ParsePostfixExpression()
        {
            var expr = ParsePrimaryExpression();
            
            while (true)
            {
                if (Match(TokenType.LEFT_PAREN))
                {
                    // Function call: func(args)
                    var args = new List<Expression>();
                    
                    if (!Check(TokenType.RIGHT_PAREN))
                    {
                        do
                        {
                            args.Add(ParseExpression());
                        } while (Match(TokenType.COMMA));
                    }
                    
                    Consume(TokenType.RIGHT_PAREN, "Expected ')' after function arguments");
                    expr = new CallExpression(expr, args);
                }
                else if (Match(TokenType.LEFT_BRACKET))
                {
                    // Parse slice or subscript
                    var sliceOrIndex = ParseSliceOrIndex();
                    
                    Consume(TokenType.RIGHT_BRACKET, "Expected ']' after subscript");
                    
                    expr = new SubscriptExpression(expr, sliceOrIndex);
                }
                else if (Match(TokenType.DOT))
                {
                    // Attribute access: obj.attr
                    var name = Consume(TokenType.IDENTIFIER, "Expected attribute name after '.'").Lexeme;
                    expr = new AttributeExpression(expr, name);
                }
                else
                {
                    // No more postfix operators
                    break;
                }
            }
            
            return expr;
        }

        private Expression ParsePrimaryExpression()
        {
            // Literals
            if (Match(TokenType.TRUE)) return new ConstantExpression(PyBool.True);
            if (Match(TokenType.FALSE)) return new ConstantExpression(PyBool.False);
            if (Match(TokenType.NONE)) return new ConstantExpression(PyNone.Instance);
            
            if (Match(TokenType.INTEGER))
            {
                var value = int.Parse(Previous().Lexeme);
                return new ConstantExpression(new PyInt(value));
            }
            
            if (Match(TokenType.FLOAT))
            {
                var value = double.Parse(Previous().Lexeme);
                return new ConstantExpression(new PyFloat(value));
            }
            
            if (Match(TokenType.STRING))
            {
                var value = Previous().Lexeme;
                return new ConstantExpression(new PyString(value));
            }
            
            if (Match(TokenType.RAW_STRING))
            {
                var value = Previous().Lexeme;
                // Raw strings don't process escape sequences
                return new ConstantExpression(new PyString(value));
            }
            
            // F-String support (PEP 701 enhanced)
            if (Match(TokenType.F_STRING))
            {
                var fstringContent = Previous().Lexeme;
                return ParseFString(fstringContent);
            }
            
            // CPython 3.12: Binary string literals (b'...')
            if (Match(TokenType.BYTES_STRING))
            {
                var bytesLexeme = Previous().Lexeme;
                // Convert string to byte array (CPython 3.12 compatible)
                var bytes = System.Text.Encoding.UTF8.GetBytes(bytesLexeme);
                return new ConstantExpression(new PyBytes(bytes));
            }
            
            if (Match(TokenType.IDENTIFIER))
            {
                var identifierName = Previous().Lexeme;
                
                // f-string (enhanced support)
                if (identifierName == "f" && Check(TokenType.STRING))
                {
                    Advance(); // consume the string part
                    var fstringContent = Previous().Lexeme;
                    return ParseFString(fstringContent);
                }
                
                // Regular identifier
                return new NameExpression(identifierName);
            }
            
            // Handle 'type' keyword as identifier in expressions (for metaclass usage)
            if (Match(TokenType.TYPE))
            {
                return new NameExpression("type");
            }
            
            // Parentheses, Tuples, or Generator Expressions
            if (Match(TokenType.LEFT_PAREN))
            {
                if (Check(TokenType.RIGHT_PAREN))
                {
                    Advance(); // empty tuple
                    return new TupleExpression(new List<Expression>());
                }
                
                var first = ParseExpression();
                
                // Check for walrus operator inside parentheses
                if (Check(TokenType.WALRUS))
                {
                    if (first is NameExpression nameExpr)
                    {
                        Advance(); // consume WALRUS
                        var value = ParseExpression();
                        Consume(TokenType.RIGHT_PAREN, "Expected ')' after walrus expression");
                        return new WalrusExpression(nameExpr.Name, value);
                    }
                    throw new Exception("Invalid walrus operator target in parentheses");
                }
                // Check if this is a generator expression
                else if (Check(TokenType.FOR))
                {
                    // This is a generator expression: (expr for ...)
                    var generators = ParseComprehensionGenerators();
                    Consume(TokenType.RIGHT_PAREN, "Expected ')' after generator expression");
                    return new GeneratorExpression(first, generators);
                }
                else if (Match(TokenType.COMMA))
                {
                    // This is a tuple
                    var elements = new List<Expression> { first };
                    
                    if (!Check(TokenType.RIGHT_PAREN)) // Handle trailing comma
                    {
                        do
                        {
                            elements.Add(ParseExpression());
                        } while (Match(TokenType.COMMA) && !Check(TokenType.RIGHT_PAREN));
                    }
                    
                    Consume(TokenType.RIGHT_PAREN, "Expected ')' after tuple elements");
                    return new TupleExpression(elements);
                }
                else
                {
                    // Just parentheses
                    Consume(TokenType.RIGHT_PAREN, "Expected ')' after expression");
                    return first;
                }
            }
            
            // Lists
            if (Match(TokenType.LEFT_BRACKET))
            {
                return ParseListExpression();
            }
            
            // Dictionaries
            if (Match(TokenType.LEFT_BRACE))
            {
                return ParseDictExpression();
            }
            
            throw new Exception($"Unexpected token in primary expression: {Peek().Type}({Peek().Lexeme}) at {Peek().Line}:{Peek().Column}");
        }

        private Expression ParseListExpression()
        {
            // Empty list
            if (Check(TokenType.RIGHT_BRACKET))
            {
                Consume(TokenType.RIGHT_BRACKET, "Expected ']'");
                return new ListExpression(new List<Expression>());
            }
            
            // Parse first element
            var firstElement = ParseExpression();
            
            // Check if this is a list comprehension
            if (Check(TokenType.FOR))
            {
                // This is a list comprehension: [expr for ...]
                var generators = ParseComprehensionGenerators();
                Consume(TokenType.RIGHT_BRACKET, "Expected ']' after list comprehension");
                return new ListComprehension(firstElement, generators);
            }
            else
            {
                // This is a regular list literal
                var elements = new List<Expression> { firstElement };
                
                while (Match(TokenType.COMMA) && !Check(TokenType.RIGHT_BRACKET))
                {
                    // CPython 3.12: Skip newlines and indentation after comma in multiline lists
                    SkipNewlines();
                    SkipIndentationTokens();
                    
                    if (Check(TokenType.RIGHT_BRACKET)) break; // trailing comma
                    
                    elements.Add(ParseExpression());
                    
                    // Skip any trailing whitespace/indentation
                    SkipNewlines();
                    SkipDedentationTokens();
                }
                
                Consume(TokenType.RIGHT_BRACKET, "Expected ']' after list elements");
                return new ListExpression(elements);
            }
        }

        private Expression ParseDictExpression()
        {
            // CPython 3.12: Handle indentation in multiline dict/set properly
            SkipNewlines();
            SkipIndentationTokens();
            
            // Empty dict/set
            if (Check(TokenType.RIGHT_BRACE))
            {
                Advance(); // empty dict
                return new DictExpression(new List<(Expression, Expression)>());
            }
            
            var firstExpr = ParseExpression();
            
            if (Match(TokenType.COLON))
            {
                // This might be a dictionary literal or dict comprehension
                var firstValue = ParseExpression();
                
                // Check if this is a dict comprehension
                if (Check(TokenType.FOR))
                {
                    // This is a dict comprehension: {key: value for ...}
                    var generators = ParseComprehensionGenerators();
                    while (Match(TokenType.DEDENT)) { }
                    SkipNewlines();
                    Consume(TokenType.RIGHT_BRACE, "Expected '}' after dict comprehension");
                    return new DictComprehension(firstExpr, firstValue, generators);
                }
                else
                {
                    // This is a regular dictionary literal
                    var items = new List<(Expression Key, Expression Value)>();
                    items.Add((firstExpr, firstValue));
                    
                    while (Match(TokenType.COMMA) && !Check(TokenType.RIGHT_BRACE))
                    {
                        // CPython 3.12: Skip newlines and indentation after comma in multiline dictionaries
                        SkipNewlines();
                        SkipIndentationTokens();
                        
                        if (Check(TokenType.RIGHT_BRACE)) break; // trailing comma
                        
                        var key = ParseExpression();
                        Consume(TokenType.COLON, "Expected ':' after dictionary key");
                        var value = ParseExpression();
                        items.Add((key, value));
                        
                        // Skip any trailing whitespace/indentation
                        SkipNewlines();
                        SkipDedentationTokens();
                    }
                    
                    // CPython 3.12: Skip trailing DEDENT before closing brace
                    SkipDedentationTokens();
                    SkipNewlines();
                    
                    Consume(TokenType.RIGHT_BRACE, "Expected '}' after dictionary items");
                    return new DictExpression(items);
                }
            }
            else
            {
                // This might be a set literal or set comprehension
                if (Check(TokenType.FOR))
                {
                    // This is a set comprehension: {expr for ...}
                    var generators = ParseComprehensionGenerators();
                    while (Match(TokenType.DEDENT)) { }
                    SkipNewlines();
                    Consume(TokenType.RIGHT_BRACE, "Expected '}' after set comprehension");
                    return new SetComprehension(firstExpr, generators);
                }
                else
                {
                    // This is a regular set literal
                    var elements = new List<Expression>();
                    elements.Add(firstExpr);
                    
                    while (Match(TokenType.COMMA) && !Check(TokenType.RIGHT_BRACE))
                    {
                        // CPython 3.12: Skip newlines and indentation after comma in multiline sets
                        SkipNewlines();
                        SkipIndentationTokens();
                        
                        if (Check(TokenType.RIGHT_BRACE)) break; // trailing comma
                        
                        elements.Add(ParseExpression());
                        
                        // Skip any trailing whitespace/indentation
                        SkipNewlines();
                        SkipDedentationTokens();
                    }
                    
                    // CPython 3.12: Skip trailing DEDENT before closing brace
                    SkipDedentationTokens();
                    SkipNewlines();
                    
                    Consume(TokenType.RIGHT_BRACE, "Expected '}' after set elements");
                    return new SetExpression(elements);
                }
            }
        }

        /// <summary>
        /// Parse comprehension generators: for target in iter [if condition] [for target2 in iter2 ...]
        /// </summary>
        private List<Comprehension> ParseComprehensionGenerators()
        {
            var generators = new List<Comprehension>();
            
            // Parse first generator (required)
            generators.Add(ParseSingleComprehension());
            
            // Parse additional generators (optional)
            while (Check(TokenType.FOR))
            {
                generators.Add(ParseSingleComprehension());
            }
            
            return generators;
        }
        
        /// <summary>
        /// Parse single comprehension: for target in iter [if condition1] [if condition2] ...
        /// </summary>
        private Comprehension ParseSingleComprehension()
        {
            var savedInComprehension = _inComprehension;
            _inComprehension = true;
            
            try
            {
                Consume(TokenType.FOR, "Expected 'for' in comprehension");
                
                // Parse target (can be simple name or tuple/list unpacking)
                var target = ParseComprehensionTarget();
                
                Consume(TokenType.IN, "Expected 'in' after comprehension target");
                
                // Parse iterable expression
                var iter = ParseExpression();
                
                // Parse optional if conditions
                var ifs = new List<Expression>();
                while (Match(TokenType.IF))
                {
                    var condition = ParseExpression();
                    ifs.Add(condition);
                }
                
                return new Comprehension(target, iter, ifs);
            }
            finally
            {
                _inComprehension = savedInComprehension;
            }
        }
        
        /// <summary>
        /// Parse f-string content and extract expressions
        /// Example: "Hello, {name}!" -> ["Hello, ", name, "!"]
        /// </summary>
        private Expression ParseFString(string content)
        {
            return WithRecursionProtection("ParseFString", () =>
            {
                var values = new List<Expression>();
                var currentPos = 0;
                var iterationCount = 0;
                const int MAX_FSTRING_PARTS = 1000; // Safety limit for f-string parts
                
                // Remove outer quotes if present
                if (content.StartsWith("\"") && content.EndsWith("\""))
                {
                    content = content.Substring(1, content.Length - 2);
                }
                else if (content.StartsWith("'") && content.EndsWith("'"))
                {
                    content = content.Substring(1, content.Length - 2);
                }
                
                while (currentPos < content.Length)
                {
                    CheckParsingProgress();
                    
                    // Safety check for f-string complexity
                    if (++iterationCount > MAX_FSTRING_PARTS)
                    {
                        throw new Exception($"🚨 F-string too complex (max {MAX_FSTRING_PARTS} parts): possible infinite loop");
                    }
                    
                    var openBrace = content.IndexOf('{', currentPos);
                
                if (openBrace == -1)
                {
                    // No more expressions, add remaining text
                    if (currentPos < content.Length)
                    {
                        var remainingText = content.Substring(currentPos);
                        values.Add(new ConstantExpression(new PyString(remainingText)));
                    }
                    break;
                }
                
                // Add text before the expression
                if (openBrace > currentPos)
                {
                    var textPart = content.Substring(currentPos, openBrace - currentPos);
                    values.Add(new ConstantExpression(new PyString(textPart)));
                }
                
                // Find matching closing brace
                var closeBrace = FindMatchingBrace(content, openBrace);
                if (closeBrace == -1)
                {
                    throw new Exception($"f-string: unterminated string (missing closing brace at position {openBrace})");
                }
                
                // Extract and parse expression
                var exprContent = content.Substring(openBrace + 1, closeBrace - openBrace - 1);
                if (!string.IsNullOrEmpty(exprContent))
                {
                    try
                    {
                        // PEP 701: Debug mode와 포맷 지정자 분리 (예: "value=:.2f" -> "value", "=", ".2f")
                        string actualExpr = exprContent;
                        string? formatSpec = null;
                        bool debugMode = false;
                        
                        // Debug mode 검사 (expr= 형태)
                        if (exprContent.EndsWith("="))
                        {
                            debugMode = true;
                            actualExpr = exprContent.Substring(0, exprContent.Length - 1);
                        }
                        else
                        {
                            // Debug mode + format spec (expr=:format 형태)
                            var equalColonIndex = exprContent.IndexOf("=:");
                            if (equalColonIndex != -1)
                            {
                                debugMode = true;
                                actualExpr = exprContent.Substring(0, equalColonIndex);
                                formatSpec = exprContent.Substring(equalColonIndex + 2);
                            }
                            else
                            {
                                // 기존 포맷 지정자 처리 (expr:format 형태)
                                var colonIndex = FindFormatSpecColon(exprContent);
                                if (colonIndex != -1)
                                {
                                    actualExpr = exprContent.Substring(0, colonIndex);
                                    formatSpec = exprContent.Substring(colonIndex + 1);
                                }
                            }
                        }
                        
                        Expression expr;
                        
                        // Check if the expression contains nested f-strings
                        if (actualExpr.Contains("f'") || actualExpr.Contains("f\""))
                        {
                            // Handle nested f-string: recursively parse the expression
                            var exprTokens = new PyLexer(actualExpr).Tokenize();
                            if (exprTokens.Count > 1) // Skip EOF
                            {
                                var exprParser = new PyParser(exprTokens);
                                expr = exprParser.ParseExpression();
                            }
                            else
                            {
                                expr = new ConstantExpression(new PyString(actualExpr));
                            }
                        }
                        else
                        {
                            // Regular expression parsing with PEP 701 multiline support
                            // Clean up whitespace while preserving meaningful content
                            var cleanExpr = StripCommentsFromFStringExpression(actualExpr.Trim());
                            
                            if (string.IsNullOrEmpty(cleanExpr))
                            {
                                expr = new ConstantExpression(new PyString(""));
                            }
                            else
                            {
                                try
                                {
                                    var exprTokens = new PyLexer(cleanExpr).Tokenize();
                                    if (exprTokens.Count > 1) // Skip EOF
                                    {
                                        var exprParser = new PyParser(exprTokens);
                                        expr = exprParser.ParseExpression();
                                    }
                                    else
                                    {
                                        // If tokenization fails, treat as literal string
                                        expr = new ConstantExpression(new PyString(cleanExpr));
                                    }
                                }
                                catch (Exception ex)
                                {
                                    // PEP 701: Better error handling for complex expressions
                                    throw new Exception($"f-string: invalid expression syntax '{cleanExpr}' - {ex.Message}");
                                }
                            }
                        }
                        
                        // Debug mode일 때 특별 처리
                        if (debugMode)
                        {
                            // "varname=" 부분을 먼저 추가
                            values.Add(new ConstantExpression(new PyString(actualExpr + "=")));
                            
                            // 값 부분 추가 (포맷 지정자 있으면 적용)
                            if (formatSpec != null)
                            {
                                values.Add(new FormattedValue(expr, formatSpec));
                            }
                            else
                            {
                                values.Add(expr);
                            }
                        }
                        else
                        {
                            // 기존 로직: 포맷 지정자가 있으면 FormattedValue 노드 생성
                            if (formatSpec != null)
                            {
                                values.Add(new FormattedValue(expr, formatSpec));
                            }
                            else
                            {
                                values.Add(expr);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // If parsing fails, treat as string literal
                        values.Add(new ConstantExpression(new PyString("{" + exprContent + "}")));
                    }
                }
                
                currentPos = closeBrace + 1;
            }
                
                return new FStringExpression(values);
            });
        }
        
        /// <summary>
        /// 포맷 지정자를 위한 콜론 찾기 (문자열 리터럴 내부는 제외)
        /// </summary>
        private int FindFormatSpecColon(string exprContent)
        {
            bool inString = false;
            char stringChar = '\0';
            int parenLevel = 0;
            int bracketLevel = 0;
            int braceLevel = 0;
            
            for (int i = 0; i < exprContent.Length; i++)
            {
                char c = exprContent[i];
                
                if (!inString)
                {
                    if (c == '\'' || c == '"')
                    {
                        inString = true;
                        stringChar = c;
                        continue;
                    }
                    
                    switch (c)
                    {
                        case '(': parenLevel++; break;
                        case ')': parenLevel--; break;
                        case '[': bracketLevel++; break;
                        case ']': bracketLevel--; break;
                        case '{': braceLevel++; break;
                        case '}': braceLevel--; break;
                        case ':':
                            // 모든 괄호가 닫혀있을 때만 포맷 지정자 콜론으로 인식
                            if (parenLevel == 0 && bracketLevel == 0 && braceLevel == 0)
                            {
                                return i;
                            }
                            break;
                    }
                }
                else
                {
                    if (c == stringChar && (i == 0 || exprContent[i - 1] != '\\'))
                    {
                        inString = false;
                    }
                }
            }
            
            return -1; // 포맷 지정자 콜론을 찾지 못함
        }
        
        /// <summary>
        /// Find matching closing brace for f-string expression with PEP 701 nested quote support
        /// </summary>
        /// <summary>
        /// PEP 701: Strip comments from f-string expressions while preserving string literals
        /// </summary>
        private string StripCommentsFromFStringExpression(string expression)
        {
            if (string.IsNullOrEmpty(expression))
                return expression;

            var result = new StringBuilder();
            bool inString = false;
            char stringDelimiter = '\0';
            bool inTripleQuote = false;
            int pos = 0;
            
            while (pos < expression.Length)
            {
                char c = expression[pos];
                
                // Handle escape sequences in strings
                if (inString && c == '\\' && pos + 1 < expression.Length)
                {
                    result.Append(c);
                    result.Append(expression[pos + 1]);
                    pos += 2;
                    continue;
                }
                
                // Handle string literals
                if (!inString)
                {
                    // Check for triple quotes
                    if ((c == '\'' || c == '"') && pos + 2 < expression.Length && 
                        expression[pos + 1] == c && expression[pos + 2] == c)
                    {
                        inString = true;
                        inTripleQuote = true;
                        stringDelimiter = c;
                        result.Append(c);
                        result.Append(c);
                        result.Append(c);
                        pos += 3;
                        continue;
                    }
                    // Check for single/double quotes
                    else if (c == '\'' || c == '"')
                    {
                        inString = true;
                        inTripleQuote = false;
                        stringDelimiter = c;
                        result.Append(c);
                        pos++;
                        continue;
                    }
                    // Handle comments - strip everything from # to end of line
                    else if (c == '#')
                    {
                        // Find end of line or end of string
                        int newlinePos = expression.IndexOf('\n', pos);
                        if (newlinePos == -1)
                        {
                            // Comment extends to end of expression
                            break;
                        }
                        else
                        {
                            // Skip to character after newline
                            pos = newlinePos + 1;
                            result.Append('\n'); // Preserve newline for multiline expressions
                            continue;
                        }
                    }
                }
                else // inString == true
                {
                    // Check for string end
                    if (inTripleQuote)
                    {
                        if (c == stringDelimiter && pos + 2 < expression.Length &&
                            expression[pos + 1] == stringDelimiter && expression[pos + 2] == stringDelimiter)
                        {
                            inString = false;
                            inTripleQuote = false;
                            stringDelimiter = '\0';
                            result.Append(c);
                            result.Append(c);
                            result.Append(c);
                            pos += 3;
                            continue;
                        }
                    }
                    else
                    {
                        if (c == stringDelimiter)
                        {
                            inString = false;
                            stringDelimiter = '\0';
                        }
                    }
                }
                
                result.Append(c);
                pos++;
            }
            
            return result.ToString().Trim();
        }

        private int FindMatchingBrace(string content, int openPos)
        {
            int braceCount = 1;
            int pos = openPos + 1;
            bool inString = false;
            char stringDelimiter = '\0';
            bool inTripleQuote = false;
            int parenLevel = 0;
            int bracketLevel = 0;
            
            while (pos < content.Length && braceCount > 0)
            {
                char c = content[pos];
                
                // Handle escape sequences in strings
                if (inString && c == '\\' && pos + 1 < content.Length)
                {
                    pos += 2; // Skip escaped character
                    continue;
                }
                
                // Handle string literals with PEP 701 improvements
                if (!inString)
                {
                    // Check for triple quotes
                    if ((c == '\'' || c == '"') && pos + 2 < content.Length && 
                        content[pos + 1] == c && content[pos + 2] == c)
                    {
                        inString = true;
                        inTripleQuote = true;
                        stringDelimiter = c;
                        pos += 2; // Skip the next two quote characters
                    }
                    // Check for single/double quotes
                    else if (c == '\'' || c == '"')
                    {
                        inString = true;
                        inTripleQuote = false;
                        stringDelimiter = c;
                    }
                    // Handle braces, parentheses, brackets outside strings
                    else if (c == '{')
                    {
                        braceCount++;
                    }
                    else if (c == '}')
                    {
                        braceCount--;
                    }
                    else if (c == '(')
                    {
                        parenLevel++;
                    }
                    else if (c == ')')
                    {
                        parenLevel--;
                    }
                    else if (c == '[')
                    {
                        bracketLevel++;
                    }
                    else if (c == ']')
                    {
                        bracketLevel--;
                    }
                }
                else // inString == true
                {
                    // Check for string end
                    if (inTripleQuote)
                    {
                        if (c == stringDelimiter && pos + 2 < content.Length &&
                            content[pos + 1] == stringDelimiter && content[pos + 2] == stringDelimiter)
                        {
                            inString = false;
                            inTripleQuote = false;
                            stringDelimiter = '\0';
                            pos += 2; // Skip the next two quote characters
                        }
                    }
                    else
                    {
                        if (c == stringDelimiter)
                        {
                            inString = false;
                            stringDelimiter = '\0';
                        }
                    }
                }
                
                pos++;
            }
            
            // Return position of closing brace, or -1 if not found
            return braceCount == 0 ? pos - 1 : -1;
        }

        /// <summary>
        /// Parse comprehension target (supports unpacking)
        /// Examples: x, (x, y), [a, b], etc.
        /// </summary>
        private Expression ParseComprehensionTarget()
        {
            if (Check(TokenType.LEFT_PAREN))
            {
                // Tuple unpacking: (x, y)
                Advance(); // consume '('
                var elements = new List<Expression>();
                
                if (!Check(TokenType.RIGHT_PAREN))
                {
                    do
                    {
                        elements.Add(ParseComprehensionTarget());
                    } while (Match(TokenType.COMMA));
                }
                
                Consume(TokenType.RIGHT_PAREN, "Expected ')' after tuple target");
                return new TupleExpression(elements);
            }
            else if (Check(TokenType.LEFT_BRACKET))
            {
                // List unpacking: [x, y]
                Advance(); // consume '['
                var elements = new List<Expression>();
                
                if (!Check(TokenType.RIGHT_BRACKET))
                {
                    do
                    {
                        elements.Add(ParseComprehensionTarget());
                    } while (Match(TokenType.COMMA));
                }
                
                Consume(TokenType.RIGHT_BRACKET, "Expected ']' after list target");
                return new ListExpression(elements);
            }
            else if (Check(TokenType.IDENTIFIER))
            {
                // Simple name or start of comma-separated tuple
                var name = Advance().Lexeme;
                var firstElement = new NameExpression(name);
                
                // Check if this is a comma-separated tuple (k, v without parentheses)
                if (Check(TokenType.COMMA))
                {
                    var elements = new List<Expression> { firstElement };
                    
                    while (Match(TokenType.COMMA))
                    {
                        if (Check(TokenType.IDENTIFIER))
                        {
                            var nextName = Advance().Lexeme;
                            elements.Add(new NameExpression(nextName));
                        }
                        else
                        {
                            elements.Add(ParseComprehensionTarget());
                        }
                    }
                    
                    return new TupleExpression(elements);
                }
                
                return firstElement;
            }
            else
            {
                throw new Exception($"Invalid comprehension target: {Peek()}");
            }
        }

        // Simple statement parsers (placeholder implementations)
        private Statement ParseIfStatement()
        {
            var condition = ParseExpression();
            Consume(TokenType.COLON, "Expected ':' after if condition");
            
            var body = ParseBlockOrSingleStatement();
            var orElse = new List<Statement>();
            
            // Handle elif and else clauses
            while (Match(TokenType.ELIF))
            {
                var elifCondition = ParseExpression();
                Consume(TokenType.COLON, "Expected ':' after elif condition");
                var elifBody = ParseBlockOrSingleStatement();
                
                // Convert elif to nested if-else structure (CPython approach)
                var nestedIf = new IfStatement(elifCondition, elifBody, new List<Statement>());
                orElse.Add(nestedIf);
            }
            
            if (Match(TokenType.ELSE))
            {
                Consume(TokenType.COLON, "Expected ':' after else");
                var elseBody = ParseBlockOrSingleStatement();
                
                if (orElse.Count > 0)
                {
                    // Add else body to the last elif
                    var lastElif = (IfStatement)orElse.Last();
                    orElse[orElse.Count - 1] = new IfStatement(lastElif.Test, lastElif.Body, elseBody);
                }
                else
                {
                    // Direct else clause
                    orElse.AddRange(elseBody);
                }
            }
            
            return new IfStatement(condition, body, orElse);
        }
        private Statement ParseWhileStatement()
        {
            var condition = ParseExpression();
            Consume(TokenType.COLON, "Expected ':' after while condition");
            
            var body = ParseBlockOrSingleStatement();
            
            // Check for optional else clause
            List<Statement>? elseClause = null;
            if (Check(TokenType.ELSE))
            {
                Advance(); // consume 'else'
                Consume(TokenType.COLON, "Expected ':' after 'else'");
                elseClause = ParseBlockOrSingleStatement();
            }
            
            return new WhileStatement(condition, body, elseClause);
        }
        private Statement ParseForStatement()
        {
            // Parse for target (left side of 'in') - support both simple names and tuple unpacking
            var target = ParseForTarget();
            Consume(TokenType.IN, "Expected 'in' in for statement");
            var iterable = ParseExpression(); // in iterable  
            Consume(TokenType.COLON, "Expected ':' after for clause");
            
            var body = ParseBlockOrSingleStatement();
            
            // Check for optional else clause
            List<Statement>? elseClause = null;
            if (Check(TokenType.ELSE))
            {
                Advance(); // consume 'else'
                Consume(TokenType.COLON, "Expected ':' after 'else'");
                elseClause = ParseBlockOrSingleStatement();
            }
            
            // Handle both single variable and tuple unpacking targets
            if (target is NameExpression nameExpr)
            {
                // Simple case: for x in items:
                return new ForStatement(nameExpr.Name, iterable, body, elseClause);
            }
            else if (target is TupleExpression tupleExpr)
            {
                // Tuple unpacking: for x, y in items:
                var targetNames = new List<string>();
                foreach (var element in tupleExpr.Elements)
                {
                    if (element is NameExpression elementName)
                    {
                        targetNames.Add(elementName.Name);
                    }
                    else
                    {
                        throw new Exception("For loop tuple unpacking targets must be variable names");
                    }
                }
                return new ForTupleStatement(targetNames, iterable, body, elseClause);
            }
            else
            {
                throw new Exception("For loop target must be a variable name or tuple unpacking");
            }
        }

        /// <summary>
        /// Parse for loop target - supports both simple names and tuple unpacking
        /// </summary>
        private Expression ParseForTarget()
        {
            // Check if this is a tuple unpacking (comma-separated identifiers)
            var targets = new List<Expression>();
            
            // Parse first target
            if (!Check(TokenType.IDENTIFIER))
            {
                throw new Exception("Expected variable name in for statement target");
            }
            
            targets.Add(new NameExpression(Advance().Lexeme));
            
            // Check for additional targets (comma-separated)
            while (Match(TokenType.COMMA))
            {
                if (!Check(TokenType.IDENTIFIER))
                {
                    throw new Exception("Expected variable name after comma in for statement target");
                }
                targets.Add(new NameExpression(Advance().Lexeme));
            }
            
            // Return single name or tuple
            if (targets.Count == 1)
            {
                return targets[0];
            }
            else
            {
                return new TupleExpression(targets);
            }
        }
        private Statement ParseTryStatement()
        {
            Consume(TokenType.COLON, "Expected ':' after try");
            
            // Parse try body
            var tryBody = ParseBlockOrSingleStatement();
            
            // CPython 3.12: try 문은 except 절들과 선택적 else 절, 또는 finally 절만 있을 수 있음
            
            // Parse except handlers
            var handlers = new List<ExceptHandler>();
            while (Check(TokenType.EXCEPT))
            {
                Advance(); // consume 'except'
                handlers.Add(ParseExceptHandler());
            }
            
            // Parse optional else clause (except 절이 있을 때만)
            List<Statement>? elseBody = null;
            if (handlers.Count > 0 && Check(TokenType.ELSE))
            {
                Advance(); // consume 'else'
                Consume(TokenType.COLON, "Expected ':' after else");
                elseBody = ParseBlockOrSingleStatement();
            }
            
            // Parse optional finally clause
            List<Statement>? finallyBody = null;
            if (Check(TokenType.FINALLY))
            {
                Advance(); // consume 'finally'
                Consume(TokenType.COLON, "Expected ':' after finally");
                finallyBody = ParseBlockOrSingleStatement();
            }
            
            // CPython 3.12: try 문은 except 절이 있거나 finally 절이 있어야 함
            if (handlers.Count == 0 && finallyBody == null)
            {
                throw new Exception("'try' statement must have either 'except' or 'finally' clause");
            }
            
            return new TryStatement(tryBody, handlers, elseBody, finallyBody);
        }
        
        private ExceptHandler ParseExceptHandler()
        {
            Expression? exceptionType = null;
            string? exceptionName = null;
            bool isStar = false;
            
            // Check for except* syntax (PEP 654)
            if (Check(TokenType.STAR))
            {
                Advance(); // consume the *
                isStar = true;
            }
            
            // Parse exception type (optional)
            if (!Check(TokenType.COLON))
            {
                exceptionType = ParseExpression();
                
                // Parse "as name" clause (optional)
                if (Match(TokenType.AS))
                {
                    if (!Check(TokenType.IDENTIFIER))
                    {
                        throw new Exception("Expected identifier after 'as'");
                    }
                    exceptionName = Advance().Lexeme;
                }
            }
            
            Consume(TokenType.COLON, "Expected ':' after except clause");
            
            var handlerBody = ParseBlockOrSingleStatement();
            
            return new ExceptHandler(exceptionType, exceptionName, handlerBody, isStar);
        }
        private Statement ParseWithStatement()
        {
            var items = new List<WithItem>();
            
            // Parse first with item: with expr [as var]
            var contextExpr = ParseExpression();
            Expression? optionalVars = null;
            
            if (Check(TokenType.AS))
            {
                Advance(); // consume AS
                optionalVars = ParseExpression(); // as target
            }
            
            items.Add(new WithItem(contextExpr, optionalVars));
            
            // Handle multiple context managers: with a, b as x, c:
            while (Check(TokenType.COMMA))
            {
                Advance(); // consume comma
                
                var nextContextExpr = ParseExpression();
                Expression? nextOptionalVars = null;
                
                if (Check(TokenType.AS))
                {
                    Advance(); // consume AS
                    nextOptionalVars = ParseExpression();
                }
                
                items.Add(new WithItem(nextContextExpr, nextOptionalVars));
            }
            
            Consume(TokenType.COLON, "Expected ':' after with statement");
            
            // Parse body
            var body = ParseBlockOrSingleStatement();
            
            return new WithStatement(items, body);
        }
        private Statement ParseMatchStatement()
        {
            // Console.WriteLine($"🔍 ParseMatchStatement called");
            var subject = ParseExpression(); // match subject
            Consume(TokenType.COLON, "Expected ':' after match subject");
            
            // match 문도 INDENT/DEDENT 구조를 사용
            if (!Check(TokenType.NEWLINE))
            {
                throw new Exception("Expected newline after match colon");
            }
            
            Advance(); // consume NEWLINE
            
            if (!Match(TokenType.INDENT))
            {
                throw new Exception("Expected indented block after match");
            }
            
            var cases = new List<MatchCase>();
            
            // Parse case blocks until DEDENT
            while (!Check(TokenType.DEDENT) && !IsAtEnd())
            {
                // Skip any additional newlines
                if (Check(TokenType.NEWLINE))
                {
                    Advance();
                    continue;
                }
                
                // CPython 3.12: case is a soft keyword
                if (!(Check(TokenType.IDENTIFIER) && Peek().Lexeme == "case"))
                {
                    throw new Exception("Expected 'case' in match statement");
                }
                
                cases.Add(ParseMatchCase());
            }
            
            // Consume final DEDENT
            if (Check(TokenType.DEDENT))
            {
                Advance();
            }
            
            if (cases.Count == 0)
            {
                throw new Exception("match statement must have at least one case");
            }
            
            return new MatchStatement(subject, cases);
        }

        /// <summary>
        /// Parse a single case block
        /// </summary>
        private MatchCase ParseMatchCase()
        {
            // CPython 3.12: case is a soft keyword, consume as identifier
            if (!(Check(TokenType.IDENTIFIER) && Peek().Lexeme == "case"))
            {
                throw new Exception("Expected 'case'");
            }
            Advance(); // consume "case" identifier
            
            // Parse pattern (may include or patterns)
            var pattern = ParseMatchPattern();
            
            // Parse optional guard (if condition)
            Expression? guard = null;
            // Console.WriteLine($"🔍 Parsing Guard: Current token = {Peek().Type} ({Peek().Lexeme})");
            if (Match(TokenType.IF))
            {
                // Console.WriteLine($"🔍 Guard IF token matched, parsing expression...");
                guard = ParseExpression();
                // Console.WriteLine($"🔍 Guard expression parsed: {guard?.GetType().Name} - {guard}");
            }
            
            Consume(TokenType.COLON, "Expected ':' after case pattern");
            
            // Parse case body
            var body = ParseBlockOrSingleStatement();
            
            return new MatchCase(pattern, body, guard);
        }
        
        /// <summary>
        /// CPython 3.12: Parse match patterns including or patterns (pattern1 | pattern2)
        /// </summary>
        private Expression ParseMatchPattern()
        {
            return WithRecursionProtection("ParseMatchPattern", () =>
            {
                var wasInMatchPattern = _inMatchPattern;
                _inMatchPattern = true;
                
                try
                {
                var patterns = new List<Expression>();
                var startPos = _current;  // Track token progress to prevent infinite loops
                
                patterns.Add(ParseSingleMatchPattern());
                
                // CPython 3.12: or pattern maximum count limit to prevent infinite loops
                const int MAX_OR_PATTERNS = 1000;
                
                // Check for or patterns (|)
                while (Match(TokenType.PIPE) && patterns.Count < MAX_OR_PATTERNS)
                {
                    var prevPos = _current;
                    
                    // CPython 3.12: Check for invalid syntax after '|'
                    if (IsAtEnd() || Check(TokenType.COLON) || Check(TokenType.IF) || Check(TokenType.NEWLINE))
                    {
                        throw PyRuntimeError.Create("invalid syntax: expected pattern after '|'");
                    }
                    
                    try
                    {
                        patterns.Add(ParseSingleMatchPattern());
                    }
                    catch (Exception ex)
                    {
                        throw PyRuntimeError.Create($"invalid pattern after '|': {ex.Message}");
                    }
                    
                    // CPython 3.12: Prevent infinite loop - ensure token progress
                    if (_current == prevPos)
                    {
                        throw PyRuntimeError.Create("invalid syntax in or pattern: no progress made");
                    }
                }
                
                // CPython 3.12: Limit number of or patterns
                if (patterns.Count >= MAX_OR_PATTERNS)
                {
                    throw PyRuntimeError.Create($"too many or patterns (max {MAX_OR_PATTERNS})");
                }
                
                // Create pattern from parsed patterns
                Expression basePattern;
                if (patterns.Count > 1)
                {
                    basePattern = new OrPattern(patterns);
                }
                else
                {
                    basePattern = patterns[0];
                }
                
                // CPython 3.12: Check for 'as' pattern (pattern as name)
                if (Match(TokenType.AS))
                {
                    var name = Consume(TokenType.IDENTIFIER, "Expected identifier after 'as' in pattern").Lexeme;
                    return new AsPattern(basePattern, name);
                }
                
                return basePattern;
                }
                finally
                {
                    _inMatchPattern = wasInMatchPattern;
                }
            });
        }
        
        /// <summary>
        /// Parse a single match pattern (supports star expressions)
        /// </summary>
        private Expression ParseSingleMatchPattern()
        {
            // Handle sequence patterns like [1, 2, *rest]
            if (Check(TokenType.LEFT_BRACKET))
            {
                return ParseSequencePattern();
            }
            
            // Handle mapping patterns like {"key": value}
            if (Check(TokenType.LEFT_BRACE))
            {
                return ParseMappingPattern();
            }
            
            // Handle star pattern in isolation
            if (Check(TokenType.STAR))
            {
                Advance(); // consume STAR
                var name = Consume(TokenType.IDENTIFIER, "Expected identifier after * in pattern");
                return new StarPattern(name.Lexeme);
            }
            
            // Regular expression pattern
            return ParseExpression();
        }
        
        /// <summary>
        /// Parse sequence pattern like [1, 2, *rest, 4]
        /// </summary>
        private Expression ParseSequencePattern()
        {
            Consume(TokenType.LEFT_BRACKET, "Expected '['");
            
            var patterns = new List<Expression>();
            bool hasStarPattern = false;
            
            if (!Check(TokenType.RIGHT_BRACKET))
            {
                do
                {
                    if (Check(TokenType.STAR))
                    {
                        if (hasStarPattern)
                        {
                            throw new Exception("multiple starred patterns in sequence pattern");
                        }
                        hasStarPattern = true;
                        
                        Advance(); // consume STAR
                        var name = Consume(TokenType.IDENTIFIER, "Expected identifier after * in pattern");
                        patterns.Add(new StarPattern(name.Lexeme));
                    }
                    else
                    {
                        patterns.Add(ParseSingleMatchPattern());
                    }
                } while (Match(TokenType.COMMA) && !Check(TokenType.RIGHT_BRACKET));
            }
            
            Consume(TokenType.RIGHT_BRACKET, "Expected ']'");
            return new SequencePattern(patterns);
        }
        
        /// <summary>
        /// Parse mapping pattern like {"key": value, "other": other_value}
        /// </summary>
        private Expression ParseMappingPattern()
        {
            Consume(TokenType.LEFT_BRACE, "Expected '{'");
            
            var patterns = new Dictionary<string, Expression>();
            
            if (!Check(TokenType.RIGHT_BRACE))
            {
                do
                {
                    // Parse string key
                    var key = Consume(TokenType.STRING, "Expected string key in mapping pattern").Lexeme;
                    Consume(TokenType.COLON, "Expected ':' after key in mapping pattern");
                    var valuePattern = ParseSingleMatchPattern();
                    patterns[key] = valuePattern;
                } while (Match(TokenType.COMMA) && !Check(TokenType.RIGHT_BRACE));
            }
            
            Consume(TokenType.RIGHT_BRACE, "Expected '}'");
            return new MappingPattern(patterns);
        }
        
        private Statement ParseReturnStatement()
        {
            // return 뒤에 표현식이 있으면 파싱, 없으면 None
            Expression? value = null;
            if (!Check(TokenType.NEWLINE) && !IsAtEnd() && !Check(TokenType.EOF))
            {
                value = ParseExpression();
            }
            return new ReturnStatement(value);
        }
        private Statement ParseYieldStatement()
        {
            // Check for 'yield from' (CPython style)
            if (Match(TokenType.FROM))
            {
                var value = ParseExpression();
                return new YieldFromStatement(value);
            }
            else
            {
                // Regular yield
                Expression? value = null;
                if (!Check(TokenType.NEWLINE) && !Check(TokenType.EOF))
                {
                    value = ParseExpression();
                }
                return new YieldStatement(value);
            }
        }
        private Statement ParseBreakStatement() => new BreakStatement();
        private Statement ParseContinueStatement() => new ContinueStatement();
        private Statement ParsePassStatement() => new PassStatement();
        private Statement ParseAssertStatement()
        {
            // CPython 3.12: assert test [, msg]
            var test = ParseExpression();
            
            Expression? msg = null;
            if (Match(TokenType.COMMA))
            {
                msg = ParseExpression();
            }
            
            return new AssertStatement(test, msg);
        }
        private Statement ParseRaiseStatement()
        {
            Expression? exc = null;
            Expression? cause = null;
            
            // raise 문 뒤에 표현식이 있는지 확인
            if (!Check(TokenType.NEWLINE) && !Check(TokenType.EOF) && !IsAtEnd())
            {
                exc = ParseExpression();
                
                // from 절이 있는지 확인 (raise ... from ...)
                if (Match(TokenType.FROM))
                {
                    cause = ParseExpression();
                }
            }
            
            return new RaiseStatement(exc, cause);
        }
        private Statement ParseDeleteStatement()
        {
            // CPython 3.12: del target1, target2, ...
            var targets = new List<Expression>();
            
            do
            {
                targets.Add(ParsePrimaryExpression());
            } while (Match(TokenType.COMMA));
            
            return new DeleteStatement(targets);
        }
        private Statement ParseGlobalStatement()
        {
            // CPython 3.12: global name1, name2, ...
            var names = new List<string>();
            
            do
            {
                var name = Consume(TokenType.IDENTIFIER, "Expected variable name after 'global'").Lexeme;
                names.Add(name);
            } while (Match(TokenType.COMMA));
            
            return new GlobalStatement(names);
        }
        private Statement ParseNonlocalStatement()
        {
            // CPython 3.12: nonlocal name1, name2, ...
            var names = new List<string>();
            
            do
            {
                var name = Consume(TokenType.IDENTIFIER, "Expected variable name after 'nonlocal'").Lexeme;
                names.Add(name);
            } while (Match(TokenType.COMMA));
            
            return new NonlocalStatement(names);
        }
        private Statement ParseImportStatement()
        {
            // Parse comma-separated module list (CPython style)
            var modules = new List<string>();
            
            do
            {
                // Parse dotted module name (package.submodule.etc)
                var moduleName = ParseDottedModuleName();
                
                // Handle "import module as alias"
                if (Check(TokenType.AS))
                {
                    Advance(); // consume AS
                    var alias = Consume(TokenType.IDENTIFIER, "Expected alias name").Lexeme;
                    modules.Add($"{moduleName} as {alias}");
                }
                else
                {
                    modules.Add(moduleName);
                }
                
            } while (Match(TokenType.COMMA));
            
            return new ImportStatement(modules);
        }
        
        /// <summary>
        /// Parse dotted module name like "package.submodule.module"
        /// </summary>
        private string ParseDottedModuleName()
        {
            var parts = new List<string>();
            
            // First part must be identifier
            parts.Add(Consume(TokenType.IDENTIFIER, "Expected module name").Lexeme);
            
            // Parse additional dotted parts
            while (Match(TokenType.DOT))
            {
                parts.Add(Consume(TokenType.IDENTIFIER, "Expected identifier after '.'").Lexeme);
            }
            
            return string.Join(".", parts);
        }
        private Statement ParseFromImportStatement()
        {
            var moduleName = Consume(TokenType.IDENTIFIER, "Expected module name after 'from'").Lexeme;
            Consume(TokenType.IMPORT, "Expected 'import' after module name");
            
            // Check for wildcard import: from module import *
            if (Check(TokenType.STAR))
            {
                Advance(); // consume *
                return new ImportFromStatement(moduleName, new List<string> { "*" });
            }
            
            // Parse comma-separated import list (CPython style)
            var imports = new List<string>();
            
            do
            {
                var itemName = Consume(TokenType.IDENTIFIER, "Expected import item name").Lexeme;
                
                // Handle "from module import item as alias"
                if (Check(TokenType.AS))
                {
                    Advance(); // consume AS
                    var alias = Consume(TokenType.IDENTIFIER, "Expected alias name").Lexeme;
                    imports.Add($"{itemName} as {alias}");
                }
                else
                {
                    imports.Add(itemName);
                }
                
            } while (Match(TokenType.COMMA));
            
            return new ImportFromStatement(moduleName, imports);
        }

        /// <summary>
        /// Parse slice notation (start:stop:step) or regular index
        /// </summary>
        private Expression ParseSliceOrIndex()
        {
            // Check if we have an empty slice [:end] or [::step]
            if (Check(TokenType.COLON))
            {
                // Empty start
                Advance(); // consume COLON
                var stop = Check(TokenType.COLON) || Check(TokenType.RIGHT_BRACKET) ? null : ParseExpression();
                
                // Check for step
                Expression? step = null;
                if (Match(TokenType.COLON))
                {
                    step = Check(TokenType.RIGHT_BRACKET) ? null : ParseExpression();
                }
                
                return new SliceExpression(null, stop, step);
            }
            
            // Parse first expression
            var first = ParseExpression();
            
            // Check if this is a slice
            if (Check(TokenType.COLON))
            {
                Advance(); // consume COLON
                
                var stop = Check(TokenType.COLON) || Check(TokenType.RIGHT_BRACKET) ? null : ParseExpression();
                
                // Check for step
                Expression? step = null;
                if (Match(TokenType.COLON))
                {
                    step = Check(TokenType.RIGHT_BRACKET) ? null : ParseExpression();
                }
                
                return new SliceExpression(first, stop, step);
            }
            
            // Check for multiple indices (comma-separated)
            if (Check(TokenType.COMMA))
            {
                var indices = new List<Expression> { first };
                
                while (Match(TokenType.COMMA))
                {
                    // Each element could also be a slice
                    indices.Add(ParseSliceOrIndex());
                }
                
                return new TupleExpression(indices);
            }
            
            // Regular single index
            return first;
        }

        /// <summary>
        /// Check if current position is an augmented assignment operator (CPython style)
        /// </summary>
        private bool IsAugmentedAssignmentOperator()
        {
            var currentToken = Peek().Type;
            
            return currentToken == TokenType.PLUS_EQUAL ||    // +=
                   currentToken == TokenType.MINUS_EQUAL ||   // -=
                   currentToken == TokenType.STAR_EQUAL ||    // *=
                   currentToken == TokenType.SLASH_EQUAL ||   // /=
                   currentToken == TokenType.PERCENT_EQUAL || // %=
                   currentToken == TokenType.SLASH_SLASH_EQUAL || // //=
                   currentToken == TokenType.STAR_STAR_EQUAL ||   // **=
                   currentToken == TokenType.PIPE_EQUAL ||        // |=
                   currentToken == TokenType.AMPERSAND_EQUAL ||   // &=
                   currentToken == TokenType.CARET_EQUAL ||       // ^=
                   currentToken == TokenType.LEFT_SHIFT_EQUAL ||  // <<=
                   currentToken == TokenType.RIGHT_SHIFT_EQUAL;   // >>=
        }
        
        /// <summary>
        /// Parse augmented assignment statement (CPython style)
        /// </summary>
        private Statement ParseAugmentedAssignment(Expression target)
        {
            if (!(target is NameExpression nameExpr))
            {
                throw new Exception("Invalid augmented assignment target");
            }
            
            var opToken = Advance(); // consume augmented assignment operator
            var value = ParseExpression();
            
            // Convert to equivalent binary operation: x += y becomes x = x + y
            var opString = opToken.Type switch
            {
                TokenType.PLUS_EQUAL => "+",
                TokenType.MINUS_EQUAL => "-", 
                TokenType.STAR_EQUAL => "*",
                TokenType.SLASH_EQUAL => "/",
                TokenType.PERCENT_EQUAL => "%",
                TokenType.SLASH_SLASH_EQUAL => "//",
                TokenType.STAR_STAR_EQUAL => "**",
                TokenType.PIPE_EQUAL => "|",
                TokenType.AMPERSAND_EQUAL => "&",
                TokenType.CARET_EQUAL => "^",
                TokenType.LEFT_SHIFT_EQUAL => "<<",
                TokenType.RIGHT_SHIFT_EQUAL => ">>",
                _ => throw new Exception($"Unknown augmented assignment operator: {opToken.Type}")
            };
            
            var binaryExpr = new BinaryOpExpression(nameExpr, opString, value);
            return new AssignStatement(nameExpr.Name, binaryExpr);
        }

        /// <summary>
        /// Parse lambda expression: lambda args: body
        /// </summary>
        private Expression ParseLambdaExpression()
        {
            Consume(TokenType.LAMBDA, "Expected 'lambda'");
            
            var args = new List<string>();
            
            // Parse parameter list (optional)
            if (!Check(TokenType.COLON))
            {
                // Parse first parameter
                if (Check(TokenType.IDENTIFIER))
                {
                    args.Add(Advance().Lexeme);
                    
                    // Parse remaining parameters
                    while (Match(TokenType.COMMA))
                    {
                        if (Check(TokenType.IDENTIFIER))
                        {
                            args.Add(Advance().Lexeme);
                        }
                        else
                        {
                            throw new Exception("Expected parameter name after comma in lambda");
                        }
                    }
                }
            }
            
            Consume(TokenType.COLON, "Expected ':' after lambda parameters");
            
            // Parse lambda body expression
            var body = ParseConditionalExpression(); // Allow nested lambdas in body
            
            return new LambdaExpression(args, body);
        }

        // PEG-style parsing helpers
        private T? TryParse<T>(Func<T> parseFunc) where T : class
        {
            var saved = _current;
            try
            {
                return parseFunc();
            }
            catch
            {
                _current = saved; // Backtrack on failure
                return null;
            }
        }
        
        private List<T> ParseDelimitedList<T>(Func<T> parseFunc, TokenType delimiter) where T : class
        {
            var items = new List<T>();
            var first = parseFunc();
            if (first != null)
            {
                items.Add(first);
                while (Match(delimiter))
                {
                    var item = parseFunc();
                    if (item != null) items.Add(item);
                }
            }
            return items;
        }
        
        private Expression ParseOptional(Func<Expression> parseFunc, Expression defaultValue)
        {
            var result = TryParse(parseFunc);
            return result ?? defaultValue;
        }
        
        /// <summary>
        /// Memoized parsing for expensive operations
        /// </summary>
        private Expression? TryMemoizedParse(string ruleName, Func<Expression?> parseFunc)
        {
            var key = (_current, ruleName);
            
            if (_memoCache.TryGetValue(key, out var cached))
            {
                _current = cached.newPosition;
                return cached.result;
            }
            
            var startPos = _current;
            var result = parseFunc();
            var endPos = _current;
            
            _memoCache[key] = (result, endPos);
            return result;
        }
        
        /// <summary>
        /// CPython-style precedence climbing for binary operators
        /// </summary>
        private Expression ParseBinaryExpression(int minPrecedence = 0)
        {
            var left = ParseUnaryOrAtom();
            
            while (!IsAtEnd() && GetPrecedence(Peek().Type) >= minPrecedence)
            {
                
                var opToken = Advance();
                var opType = opToken.Type;
                var precedence = GetPrecedence(opType);
                
                // Handle right associativity (like **)
                var nextMinPrec = RightAssociative.GetValueOrDefault(opType, false) 
                    ? precedence 
                    : precedence + 1;
                
                var right = ParseBinaryExpression(nextMinPrec);
                
                // Create appropriate expression type based on operator
                if (IsComparisonOperator(opType))
                {
                    left = new CompareExpression(left, opToken.Lexeme, right);
                }
                else if (IsBooleanOperator(opType))
                {
                    // CPython 3.12: Create BoolOpExpression for 'and' and 'or' operations
                    var values = new List<Expression> { left, right };
                    left = new BoolOpExpression(opToken.Lexeme, values);
                }
                else
                {
                    left = new BinaryOpExpression(left, opToken.Lexeme, right);
                }
            }
            
            return left;
        }
        
        private int GetPrecedence(TokenType type)
        {
            return OperatorPrecedence.GetValueOrDefault(type, -1);
        }
        
        private bool IsComparisonOperator(TokenType type)
        {
            return type == TokenType.EQUAL_EQUAL || type == TokenType.BANG_EQUAL ||
                   type == TokenType.LESS || type == TokenType.GREATER ||
                   type == TokenType.LESS_EQUAL || type == TokenType.GREATER_EQUAL ||
                   type == TokenType.IN || type == TokenType.IS ||
                   type == TokenType.NOT_IN || type == TokenType.IS_NOT; // CPython 3.12
        }
        
        private bool IsBooleanOperator(TokenType type)
        {
            // CPython 3.12: Boolean logical operators
            return type == TokenType.AND || type == TokenType.OR;
        }
        
        private Expression ParseUnaryOrAtom()
        {
            // Handle unary operators - CPython style
            if (Match(TokenType.NOT))
            {
                var expr = ParseUnaryOrAtom(); // Recursive for 'not not x'
                return new UnaryOpExpression("not", expr);
            }
            
            if (Match(TokenType.MINUS, TokenType.PLUS, TokenType.TILDE))
            {
                var op = Previous().Lexeme;
                var expr = ParseUnaryOrAtom();
                return new UnaryOpExpression(op, expr);
            }
            
            if (Match(TokenType.AWAIT))
            {
                // CPython 3.12: await는 async def 내부에서만 사용 가능
                if (!_inAsyncFunction)
                {
                    throw CreateSyntaxError("'await' outside async function");
                }
                var expr = ParseUnaryOrAtom();
                return new AwaitExpression(expr);
            }
            
            return ParseAtomExpression();
        }
        
        private Expression ParseAtomExpression()
        {
            var expr = ParsePrimaryExpression();
            
            // Handle postfix operations (calls, subscripts, attribute access)
            while (true)
            {
                if (Match(TokenType.LEFT_PAREN))
                {
                    // Function call - CPython 호환 키워드 인수 지원
                    var (args, keywords) = ParseFunctionCallArguments();
                    ConsumeEnhanced(TokenType.RIGHT_PAREN, "after function arguments");
                    expr = new CallExpression(expr, args, keywords);
                }
                else if (Match(TokenType.LEFT_BRACKET))
                {
                    // Subscript or slice
                    var sliceOrIndex = ParseSliceOrIndex();
                    ConsumeEnhanced(TokenType.RIGHT_BRACKET, "after subscript");
                    expr = new SubscriptExpression(expr, sliceOrIndex);
                }
                else if (Match(TokenType.DOT))
                {
                    // Attribute access
                    var attr = ConsumeEnhanced(TokenType.IDENTIFIER, "after '.'");
                    expr = new AttributeExpression(expr, attr.Lexeme);
                }
                else
                {
                    break;
                }
            }
            
            return expr;
        }

        /// <summary>
        /// CPython 호환: 키워드 인수를 포함한 함수 호출 인수 파싱
        /// </summary>
        private (List<Expression> args, List<KeywordExpression> keywords) ParseFunctionCallArguments()
        {
            var args = new List<Expression>();
            var keywords = new List<KeywordExpression>();
            
            if (!Check(TokenType.RIGHT_PAREN))
            {
                do
                {
                    // **kwargs 처리
                    if (Match(TokenType.STAR, TokenType.STAR))
                    {
                        var expr = ParseExpression();
                        if (expr != null)
                        {
                            keywords.Add(new KeywordExpression(null, expr)); // **kwargs
                        }
                    }
                    // 키워드 인수 체크: name=value
                    else if (Check(TokenType.IDENTIFIER) && CheckNext(TokenType.EQUAL))
                    {
                        var keywordName = Advance().Lexeme;
                        Consume(TokenType.EQUAL, "Expected '=' after keyword argument name");
                        var value = ParseExpression();
                        
                        if (value != null)
                        {
                            keywords.Add(new KeywordExpression(keywordName, value));
                        }
                    }
                    // *args 처리
                    else if (Match(TokenType.STAR))
                    {
                        var expr = ParseExpression();
                        if (expr != null)
                        {
                            args.Add(new StarredExpression(expr));
                        }
                    }
                    else
                    {
                        // 일반적인 positional 인수
                        var arg = ParseExpression();
                        if (arg != null)
                        {
                            args.Add(arg);
                        }
                    }
                } while (Match(TokenType.COMMA));
            }
            
            return (args, keywords);
        }

        // Helper methods
        private bool Match(params TokenType[] types)
        {
            foreach (var type in types)
            {
                if (Check(type))
                {
                    Advance();
                    return true;
                }
            }
            return false;
        }

        private bool Check(TokenType type)
        {
            if (IsAtEnd()) return false;
            return Peek().Type == type;
        }

        private bool CheckNext(TokenType type)
        {
            if (_current + 1 >= _tokens.Count) return false;
            return _tokens[_current + 1].Type == type;
        }
        
        /// <summary>
        /// CPython-style compound operator detection with proper lookahead
        /// Based on CPython's Grammar/python.gram PEG parser approach
        /// </summary>
        private bool IsNotInCompoundOperator(int startPos)
        {
            if (startPos >= _tokens.Count - 1) return false;
            
            if (_tokens[startPos].Type != TokenType.NOT) return false;
            
            // CPython approach: check immediate next token for 'in'
            int nextPos = startPos + 1;
            
            // In CPython, whitespace is handled at tokenizer level
            // Here we check the direct next token
            if (nextPos >= _tokens.Count) return false;
            
            return _tokens[nextPos].Type == TokenType.IN;
        }
        
        /// <summary>
        /// Check if current position has 'not in' compound operator
        /// CPython Grammar: notin_bitwise_or: 'not' 'in' bitwise_or
        /// </summary>
        private bool IsCurrentNotInOperator()
        {
            return IsNotInCompoundOperator(_current);
        }
        
        /// <summary>
        /// Similar check for 'is not' compound operator
        /// CPython Grammar: isnot_bitwise_or: 'is' 'not' bitwise_or  
        /// </summary>
        private bool IsCurrentIsNotOperator()
        {
            if (_current >= _tokens.Count - 1) return false;
            
            return _tokens[_current].Type == TokenType.IS && 
                   _tokens[_current + 1].Type == TokenType.NOT;
        }

        private PyToken Advance()
        {
            if (!IsAtEnd()) _current++;
            return Previous();
        }

        private bool IsAtEnd()
        {
            return Peek().Type == TokenType.EOF;
        }

        private PyToken Peek()
        {
            return _tokens[_current];
        }

        private PyToken PeekNext()
        {
            if (_current + 1 >= _tokens.Count) return _tokens[_tokens.Count - 1];
            return _tokens[_current + 1];
        }

        private PyToken Previous()
        {
            return _tokens[_current - 1];
        }
        
        // CPython-style error reporting
        private Exception CreateSyntaxError(string message)
        {
            var token = Peek();
            var position = $"at {token.Type}({token.Lexeme}) at 1:{token.Column}";
            return new Exception($"{message}. Got {token.Type}({token.Lexeme}) {position}");
        }
        
        private T ConsumeOrError<T>(TokenType expected, string context, Func<T> onSuccess)
        {
            if (Check(expected))
            {
                Advance();
                return onSuccess();
            }
            throw CreateSyntaxError($"Expected '{expected}' {context}");
        }
        
        // Enhanced Consume with better error messages
        private PyToken ConsumeEnhanced(TokenType type, string context)
        {
            if (Check(type))
            {
                return Advance();
            }
            
            var token = Peek();
            var message = $"Expected {type} {context}";
            throw CreateSyntaxError(message);
        }

        private PyToken Consume(TokenType type, string message)
        {
            if (Check(type)) return Advance();
            throw new Exception($"{message}. Got {Peek()}");
        }

        private void SkipNewlines()
        {
            while (Match(TokenType.NEWLINE)) { }
        }

        /// <summary>
        /// CPython 3.12: Safely skip INDENT tokens within expressions to prevent infinite loops
        /// </summary>
        private void SkipIndentationTokens()
        {
            int safetyCounter = 0;
            while (Check(TokenType.INDENT) && safetyCounter < 10)
            {
                Advance(); // consume INDENT
                safetyCounter++;
            }
            
            if (safetyCounter >= 10)
            {
                if (!SharpPyConfig.DisassemblyOnlyMode)
                {
                    Console.WriteLine("⚠️ Warning: Skipped too many INDENT tokens - possible infinite loop prevented");
                }
            }
        }

        /// <summary>
        /// CPython 3.12: Safely skip DEDENT tokens within expressions to prevent infinite loops
        /// </summary>
        private void SkipDedentationTokens()
        {
            int safetyCounter = 0;
            while (Check(TokenType.DEDENT) && safetyCounter < 10)
            {
                Advance(); // consume DEDENT
                safetyCounter++;
            }
            
            if (safetyCounter >= 10)
            {
                if (!SharpPyConfig.DisassemblyOnlyMode)
                {
                    Console.WriteLine("⚠️ Warning: Skipped too many DEDENT tokens - possible infinite loop prevented");
                }
            }
        }

        // Infinite loop prevention methods
        private void CheckParsingProgress()
        {
            _parsingIterations++;
            
            // Check for excessive iterations
            if (_parsingIterations > MAX_PARSING_ITERATIONS)
            {
                throw new Exception($"🚨 Parser infinite loop detected: exceeded {MAX_PARSING_ITERATIONS} iterations");
            }
            
            // Track position visits to detect loops
            var currentPos = _current;
            if (_positionVisitCount.ContainsKey(currentPos))
            {
                _positionVisitCount[currentPos]++;
                if (_positionVisitCount[currentPos] > 100) // Same position visited too many times
                {
                    throw new Exception($"🚨 Parser stuck at position {currentPos}: token={Peek()?.Type}");
                }
            }
            else
            {
                _positionVisitCount[currentPos] = 1;
            }
        }
        
        private void EnterRecursion(string methodName)
        {
            _recursionDepth++;
            if (_recursionDepth > MAX_RECURSION_DEPTH)
            {
                throw new Exception($"🚨 Parser recursion too deep in {methodName}: {_recursionDepth} levels");
            }
        }
        
        private void ExitRecursion()
        {
            _recursionDepth--;
        }
        
        /// <summary>
        /// CPython 3.12: Check if current position is the start of a match statement
        /// Soft keyword "match" should only be treated as keyword in match statement context
        /// </summary>
        private bool IsMatchStatementStart()
        {
            // Console.WriteLine($"🔍 IsMatchStatementStart: Current={Peek().Type} ({Peek().Lexeme})");
            if (!(Check(TokenType.IDENTIFIER) && Peek().Lexeme == "match"))
            {
                // Console.WriteLine($"🔍 IsMatchStatementStart: Not a match statement");
                return false;
            }
            
            // Save current position for backtracking
            var savedPosition = _current;
            try
            {
                Advance(); // Skip "match" identifier
                
                // Skip potential expressions until we find a colon or fail
                int parenDepth = 0;
                int bracketDepth = 0;
                int braceDepth = 0;
                
                while (!IsAtEnd())
                {
                    var token = Peek();
                    
                    // Track nested structures
                    if (token.Type == TokenType.LEFT_PAREN) parenDepth++;
                    else if (token.Type == TokenType.RIGHT_PAREN) parenDepth--;
                    else if (token.Type == TokenType.LEFT_BRACKET) bracketDepth++;
                    else if (token.Type == TokenType.RIGHT_BRACKET) bracketDepth--;
                    else if (token.Type == TokenType.LEFT_BRACE) braceDepth++;
                    else if (token.Type == TokenType.RIGHT_BRACE) braceDepth--;
                    
                    // If we're at top level and find colon, it's a match statement
                    if (parenDepth == 0 && bracketDepth == 0 && braceDepth == 0)
                    {
                        if (token.Type == TokenType.COLON)
                        {
                            return true; // match expr: found
                        }
                        if (token.Type == TokenType.NEWLINE || token.Type == TokenType.EQUAL)
                        {
                            return false; // Not a match statement
                        }
                    }
                    
                    Advance();
                    
                    // Safety check to prevent infinite loops
                    if (_current - savedPosition > 50)
                    {
                        return false; // Too complex, assume it's not a match statement
                    }
                }
                
                return false;
            }
            finally
            {
                _current = savedPosition; // Always restore position
            }
        }
        
        private T WithRecursionProtection<T>(string methodName, Func<T> parseFunc)
        {
            try
            {
                EnterRecursion(methodName);
                return parseFunc();
            }
            finally
            {
                ExitRecursion();
            }
        }

        private void Synchronize()
        {
            Advance();
            
            while (!IsAtEnd())
            {
                if (Previous().Type == TokenType.NEWLINE) return;
                
                switch (Peek().Type)
                {
                    case TokenType.CLASS:
                    case TokenType.DEF:
                    case TokenType.IF:
                    case TokenType.WHILE:
                    case TokenType.FOR:
                    case TokenType.RETURN:
                        return;
                }
                
                Advance();
            }
        }
    }

    /// <summary>
    /// 이전 SimpleParser와의 호환성을 위한 래퍼
    /// </summary>
    public class SimpleParser
    {
        public List<Statement> Parse(string source)
        {
            return PyParser.ParseSource(source);
        }
    }

    #endregion
}