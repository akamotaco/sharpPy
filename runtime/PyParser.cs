using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SharpPy.Utils;

namespace SharpPy
{
    #region Parser Extension (Token -> AST)

    // CPython 3.12 스타일 SyntaxError 예외 클래스
    public class PySyntaxErrorException : Exception
    {
        public PySyntaxErrorException(string message) : base(message) { }
    }
    
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
        
        // CPython 3.12 스타일 에러 리포팅을 위한 정보
        private readonly string _filename;
        private readonly string _sourceCode;
        
        // CPython 3.12: All operators use OP tokens with lexeme-based precedence
        // Precedence handled by GetPrecedence() and IsRightAssociative() methods
        
        // Memoization cache for complex expressions
        private readonly Dictionary<(int position, string rule), (Expression? result, int newPosition)> _memoCache 
            = new();

        // Infinite loop prevention system
        private const int MAX_RECURSION_DEPTH = 1000;
        private const int MAX_PARSING_ITERATIONS = 50000;
        private int _recursionDepth = 0;
        private int _parsingIterations = 0;
        private readonly Dictionary<int, int> _positionVisitCount = new();

        public PyParser(List<PyToken> tokens, string filename = "<unknown>", string sourceCode = "")
        {
            _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            _current = 0;
            _filename = filename;
            _sourceCode = sourceCode;
        }

        /// <summary>
        /// CPython 3.12: Check if current token is a NAME token with specific keyword lexeme
        /// </summary>
        private bool CheckKeyword(string keyword)
        {
            return Check(TokenType.NAME) && Peek().Lexeme == keyword;
        }

        /// <summary>
        /// CPython 3.12: Match a NAME token with specific keyword lexeme
        /// </summary>
        private bool MatchKeyword(string keyword)
        {
            if (CheckKeyword(keyword))
            {
                Advance();
                return true;
            }
            return false;
        }

        /// <summary>
        /// CPython 3.12: Get next token without advancing
        /// </summary>
        private PyToken PeekNext()
        {
            if (_current + 1 >= _tokens.Count) return _tokens[_tokens.Count - 1];
            return _tokens[_current + 1];
        }

        /// <summary>
        /// CPython 3.12: Check if next token is of specific type
        /// </summary>
        private bool CheckNext(TokenType type)
        {
            if (_current + 1 >= _tokens.Count) return false;
            return _tokens[_current + 1].Type == type;
        }

        /// <summary>
        /// CPython 3.12: Consume a keyword (NAME token with specific lexeme) or throw error
        /// </summary>
        private PyToken ConsumeKeyword(string keyword, string message)
        {
            if (CheckKeyword(keyword))
            {
                return Advance();
            }
            throw new PySyntaxErrorException($"{message}. Got {Peek().Type}({Peek().Lexeme}) at line {Peek().Line}");
        }

        /// <summary>
        /// 편의 메서드: 소스 코드에서 직접 파싱
        /// </summary>
        public static List<Statement> ParseSource(string source, string filename = "<string>")
        {
            try
            {
                var lexer = new PyLexer(source);
                var tokens = lexer.Tokenize();
                var parser = new PyParser(tokens, filename, source);
                return parser.Parse();
            }
            catch (PySyntaxErrorException ex)
            {
                // CPython 3.12 스타일: SyntaxError 즉시 중단
                throw new Exception($"SyntaxError: {ex.Message}");
            }
        }

        /// <summary>
        /// 토큰을 AST로 파싱
        /// </summary>
        public List<Statement> Parse()
        {
            var statements = new List<Statement>();
            
            SharpPyConfig.DebugWriteInternal($"\n📝 파싱: {_tokens.Count}개 토큰");
            
            while (!IsAtEnd())
            {
                // Infinite loop prevention
                CheckParsingProgress();
                
                // Skip newlines at statement level (both NEWLINE and NL)
                if (Check(TokenType.NEWLINE) || Check(TokenType.NL))
                {
                    Advance();
                    continue;
                }

                // CPython 3.12: Skip comment tokens
                if (Check(TokenType.COMMENT))
                {
                    Advance();
                    continue;
                }

                // CPython 3.12: DEDENT tokens signal end of block - should be handled by block parsers
                // Don't skip them at statement level, let ParseStatement handle them properly
                if (Check(TokenType.DEDENT))
                {
                    // Let ParseStatement handle DEDENT and return null to end current block
                    var statement = ParseStatement();
                    if (statement == null)
                    {
                        // DEDENT was encountered, end current parsing context
                        break;
                    }
                    statements.Add(statement);
                    continue;
                }

                // CPython 3.12: Skip semicolons as statement separators (like newlines)
                if (CheckSemicolon())
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
                            #if DEBUG_LOG
                            Console.WriteLine($"  → {statement}");
                            #endif
                        }

                        // CPython 3.12: After parsing a statement, consume optional semicolon
                        if (CheckSemicolon())
                        {
                            Advance(); // consume semicolon as statement terminator
                        }
                    }
                    else
                    {
                        // If ParseStatement returns null, check if we've hit unexpected tokens
                        // that should be handled gracefully to avoid infinite loops
                        if (Check(TokenType.DEDENT))
                        {
                            // Skip unexpected DEDENT tokens at top level
                            if (!SharpPyConfig.DisassemblyOnlyMode)
                            {
                                #if DEBUG_LOG
                                Console.WriteLine("⚠️ Warning: Skipping unexpected DEDENT token at top level");
                                #endif
                            }
                            Advance();
                        }
                        else if (!IsAtEnd() && !Check(TokenType.ENDMARKER))
                        {
                            // For other unexpected tokens, advance to prevent infinite loop
                            if (!SharpPyConfig.DisassemblyOnlyMode)
                            {
                                #if DEBUG_LOG
                                Console.WriteLine($"⚠️ Warning: Skipping unexpected token {Peek()?.Type} at top level");
                                #endif
                            }
                            Advance();
                        }
                    }
                }
                catch (PySyntaxErrorException ex)
                {
                    // CPython 3.12 스타일: SyntaxError는 즉시 중단
                    if (!SharpPyConfig.DisassemblyOnlyMode)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"  ❌ SyntaxError: {ex.Message}");
                        #endif
                    }
                    throw; // 즉시 중단
                }
                catch (Exception ex)
                {
                    if (!SharpPyConfig.DisassemblyOnlyMode)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"  ❌ 파싱 에러: {ex.Message}");
                        #endif
                    }
                    // Skip to next statement
                    Synchronize();
                }
            }

            if (!SharpPyConfig.DisassemblyOnlyMode)
            {
                #if DEBUG_LOG
                Console.WriteLine($"✅ 파싱 완료: {statements.Count}개 문장");
                #endif
            }
            return statements;
        }

        private Statement? ParseStatement()
        {
            return WithRecursionProtection("ParseStatement", () =>
            {

                // CPython 3.12: Skip comment tokens
                SkipCommentTokens();

                // CPython 3.12: INDENT should be handled by block parsers, not statement parsers
                // If we encounter INDENT in statement parsing, it means we're not in proper block context
                if (Check(TokenType.INDENT))
                {
                    // CPython 3.12: INDENT in ParseStatement indicates improper block handling
                    if (!SharpPyConfig.DisassemblyOnlyMode)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"⚠️ ParseStatement: Found INDENT at position {_current} - should be handled by block parser");
                        #endif
                        #if DEBUG_LOG
                        Console.WriteLine($"   Previous token: {(_current > 0 ? _tokens[_current-1].Type.ToString() : "N/A")}");
                        #endif
                        #if DEBUG_LOG
                        Console.WriteLine($"   Current token: {_tokens[_current].Type} '{_tokens[_current].Lexeme}'");
                        #endif
                        #if DEBUG_LOG
                        Console.WriteLine($"   Next token: {(_current+1 < _tokens.Count ? _tokens[_current+1].Type.ToString() : "N/A")}");
                        #endif
                    }
                    // Don't consume INDENT here - let the caller handle it properly
                    return null;
                }

                if (Check(TokenType.DEDENT))
                {
                    // DEDENT signals end of current block - don't consume it here
                    if (!SharpPyConfig.DisassemblyOnlyMode)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine("⚠️ ParseStatement: Hit DEDENT - ending block");
                        #endif
                    }
                    return null; // Let block parser handle this
                }
                
                // CPython 3.12: Handle block-ending tokens gracefully
                if (CheckKeyword("except") || CheckKeyword("finally") || CheckKeyword("else"))
                {
                    // These tokens signal end of current block and should be handled by the parent parser
                    // Don't consume them here, just return null to let the caller handle it
                    if (!SharpPyConfig.DisassemblyOnlyMode)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"⚠️ Warning: Encountered block-ending token {Peek().Type} - returning control to block parser");
                        #endif
                    }
                    return null;
                }
                
                // Check for decorators first (specifically @ symbol)
                if (Check(TokenType.OP) && Peek().Lexeme == "@")
                {
                    return ParseDecoratedStatement();
                }
            
            // Function definition (CPython 3.12: keywords are NAME tokens)
            if (MatchKeyword("def")) return ParseFunctionDef();
            if (CheckKeyword("async") && CheckNext(TokenType.NAME) && PeekNext().Lexeme == "def")
            {
                return ParseAsyncFunctionDef();
            }

            // Class definition
            if (MatchKeyword("class")) return ParseClassDef();

            // Type alias (Python 3.12)
            if (MatchKeyword("type")) return ParseTypeAlias();

            // Control flow
            if (!SharpPyConfig.DisassemblyOnlyMode)
            {
            }
            if (MatchKeyword("if")) return ParseIfStatement();
            if (MatchKeyword("while")) return ParseWhileStatement();
            if (MatchKeyword("for")) return ParseForStatement();
            if (MatchKeyword("try")) return ParseTryStatement();
            if (MatchKeyword("with")) return ParseWithStatement();
            // CPython 3.12: match is a soft keyword, check context
            if (IsMatchStatementStart())
            {
                Advance(); // consume "match" identifier
                return ParseMatchStatement();
            }
            
            // Simple statements
            if (MatchKeyword("return")) return ParseReturnStatement();
            if (MatchKeyword("yield")) return ParseYieldStatement();
            if (MatchKeyword("break")) return ParseBreakStatement();
            if (MatchKeyword("continue")) return ParseContinueStatement();
            if (MatchKeyword("pass")) return ParsePassStatement();
            if (MatchKeyword("assert")) return ParseAssertStatement();
            if (MatchKeyword("raise")) return ParseRaiseStatement();
            if (MatchKeyword("del")) return ParseDeleteStatement();
            if (MatchKeyword("global")) return ParseGlobalStatement();
            if (MatchKeyword("nonlocal")) return ParseNonlocalStatement();
            if (MatchKeyword("import")) return ParseImportStatement();
            if (MatchKeyword("from")) return ParseFromImportStatement();
            
                // Expression statement or assignment
                if (!SharpPyConfig.DisassemblyOnlyMode)
                {
                }
                return ParseExpressionOrAssignment();
            });
        }

        private Statement ParseFunctionDef(List<DecoratorExpression>? decorators = null)
        {
            var name = Consume(TokenType.NAME, "Expected function name").Lexeme;

            // Type parameters (Python 3.12)
            var typeParams = new List<string>();
            if (MatchOp("["))
            {
                typeParams = ParseTypeParameters();
                ConsumeClosingBracket("Expected ']' after type parameters");
            }

            ConsumeOp("(", "Expected '(' after function name");
            var parameters = ParseParameterList();
            ConsumeOp(")", "Expected ')' after parameters");

            // Check for return type annotation (->)
            Expression? returnTypeAnnotation = null;
            if (CheckOp("->"))
            {
                Advance(); // consume ARROW
                // Parse return type annotation (Python 3.12 Type Union 지원)
                returnTypeAnnotation = ParseTypeAnnotation();
            }

            ConsumeColon("Expected ':' after function signature");

            var body = ParseBlockOrSingleStatement();

            return new FunctionDefStatement(name, parameters, body, typeParams, decorators, returnTypeAnnotation);
        }

        private Statement ParseAsyncFunctionDef()
        {
            ConsumeKeyword("async", "Expected 'async'");
            ConsumeKeyword("def", "Expected 'def' after 'async'");
            
            var name = Consume(TokenType.NAME, "Expected function name").Lexeme;
            
            // Type parameters (Python 3.12)
            var typeParams = new List<string>();
            if (MatchOp("["))
            {
                typeParams = ParseTypeParameters();
                ConsumeClosingBracket("Expected ']' after type parameters");
            }
            
            ConsumeOp("(", "Expected '(' after function name");
            var parameters = ParseParameterList();
            ConsumeOp(")", "Expected ')' after parameters");

            // Check for return type annotation (->)
            Expression? returnTypeAnnotation = null;
            if (CheckOp("->"))
            {
                Advance(); // consume ARROW
                // Parse return type annotation (Python 3.12 Type Union 지원)
                returnTypeAnnotation = ParseTypeAnnotation();
            }

            ConsumeColon("Expected ':' after function signature");
            
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
            if (MatchKeyword("def"))
            {
                return ParseFunctionDef(decorators);
            }
            else if (MatchKeyword("async") && CheckKeyword("def"))
            {
                ConsumeKeyword("def", "Expected 'def' after 'async'");
                return ParseAsyncFunctionDef(); // TODO: async 함수도 데코레이터 지원 필요
            }
            else if (MatchKeyword("class"))
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
            
            while (Check(TokenType.OP))
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
            Consume(TokenType.OP, "Expected '@'");
            
            // 데코레이터 함수 이름 파싱 (dotted name 지원: @module.decorator)
            var decoratorFunc = ParseDottedName();
            
            // 데코레이터에 인수가 있는 경우
            List<Expression>? arguments = null;
            if (MatchOp("("))
            {
                // 함수 호출과 동일한 방식으로 인자 파싱 (키워드 인자 지원)
                var (args, keywords) = ParseFunctionCallArguments();
                ConsumeEnhancedOp(")", "after decorator arguments");
                
                // 데코레이터의 경우 키워드 인자도 일반 arguments 리스트에 포함시킴
                arguments = new List<Expression>(args);
                if (keywords != null)
                {
                    arguments.AddRange(keywords);
                }
            }
            
            return new DecoratorExpression(decoratorFunc, arguments);
        }

        /// <summary>
        /// Dotted name 파싱 (module.submodule.name)
        /// </summary>
        private Expression ParseDottedName()
        {
            var name = Consume(TokenType.NAME, "Expected decorator name").Lexeme;
            Expression expr = new NameExpression(name);
            
            while (MatchOp("."))
            {
                var attrName = Consume(TokenType.NAME, "Expected attribute name after '.'").Lexeme;
                expr = new AttributeExpression(expr, attrName);
            }
            
            return expr;
        }

        private Statement ParseClassDef()
        {
            var name = Consume(TokenType.NAME, "Expected class name").Lexeme;
            
            // Type parameters (Python 3.12)
            var typeParams = new List<string>();
            if (MatchOp("["))
            {
                typeParams = ParseTypeParameters();
                ConsumeClosingBracket("Expected ']' after type parameters");
            }
            
            // Base classes and metaclass
            var bases = new List<Expression>();
            Expression? metaclass = null;
            
            if (MatchOp("("))
            {
                if (!CheckRParen())
                {
                    do
                    {
                        // Skip NEWLINE, NL, and COMMENT tokens for multi-line class definitions (CPython 3.12 compatibility)
                        while (Check(TokenType.NEWLINE) || Check(TokenType.NL) || Check(TokenType.COMMENT))
                        {
                            Advance();
                        }

                        // Check for trailing comma - if we see closing parenthesis, break
                        if (CheckOp(")"))
                        {
                            break;
                        }

                        // CPython 3.12: Check for keyword arguments like metaclass=
                        if (Check(TokenType.NAME) && CheckNextOp("="))
                        {
                            var keywordName = Peek().Lexeme;
                            Advance(); // consume keyword name
                            Advance(); // consume '='
                            var value = ParseConditionalExpression(); // 콤마 구분 표현식 피하기

                            if (keywordName == "metaclass")
                            {
                                metaclass = value;
                            }
                            else
                            {
                                // 다른 키워드 인수들은 향후 확장 가능
                                throw new Exception($"Unsupported class keyword argument: {keywordName}");
                            }
                        }
                        else
                        {
                            bases.Add(ParseConditionalExpression()); // 콤마 구분 표현식 피하기
                        }

                    } while (MatchOp(","));
                }
                ConsumeOp(")", "Expected ')' after base classes");
            }
            
            ConsumeColon( "Expected ':' after class header");

            var body = ParseBlock(isClassBody: true);

            return new ClassDefStatement(name, bases, body, typeParams, metaclass);
        }

        private Statement ParseTypeAlias()
        {
            var name = Consume(TokenType.NAME, "Expected type alias name").Lexeme;
            
            // Type parameters (Python 3.12)
            var typeParams = new List<string>();
            if (MatchOp("["))
            {
                typeParams = ParseTypeParameters();
                ConsumeClosingBracket("Expected ']' after type parameters");
            }
            
            ConsumeOp("=", "Expected '=' in type alias");
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
                    if (CheckOp("**"))
                    {
                        // **P (ParamSpec) - PEP 612
                        Advance(); // consume **
                        var paramName = Consume(TokenType.NAME, "Expected type parameter name after **").Lexeme;
                        param = "**" + paramName;
                    }
                    else if (CheckOp("*"))
                    {
                        // *Ts (TypeVarTuple) - PEP 646
                        Advance(); // consume *
                        var paramName = Consume(TokenType.NAME, "Expected type parameter name after *").Lexeme;
                        param = "*" + paramName;
                    }
                    else
                    {
                        // Regular type parameter T
                        param = Consume(TokenType.NAME, "Expected type parameter name").Lexeme;
                        
                        // Handle type constraints (T: int) - PEP 695
                        if (CheckColon())
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
                                    #if DEBUG_LOG
                                    Console.WriteLine($"⚠️ Type constraint parsing failed: {ex.Message}");
                                    #endif
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
                } while (MatchOp(",") && !IsAtEnd());
                
                return typeParams;
            });
        }

        private List<string> ParseParameterList()
        {
            var parameters = new List<string>();
            bool seenPositionalOnlySeparator = false;
            bool seenKeywordOnlyMarker = false;

            if (!CheckRParen())
            {
                do
                {
                    // Skip NEWLINE, NL, and COMMENT tokens for multi-line function definitions (CPython 3.12 compatibility)
                    while (Check(TokenType.NEWLINE) || Check(TokenType.NL) || Check(TokenType.COMMENT))
                    {
                        Advance();
                    }

                    // Check if we've reached the end of parameters after skipping newlines
                    if (CheckRParen())
                    {
                        break;
                    }

                    // Handle positional-only separator (/) - PEP 570
                    if (CheckOp("/"))
                    {
                        Advance(); // consume OP(/)
                        seenPositionalOnlySeparator = true;
                        parameters.Add("/"); // Mark positional-only separator
                        continue;
                    }
                    
                    // Handle *args and **kwargs (CPython style)
                    if (CheckOp("**"))
                    {
                        // **kwargs
                        Advance(); // consume **
                        var kwargsParam = Consume(TokenType.NAME, "Expected parameter name after **").Lexeme;
                        
                        // PEP 692: **kwargs 타입 주석 처리
                        if (MatchOp(":"))
                        {
                            // **kwargs: Unpack[TypedDict] 파싱 (Python 3.12 Type Union 지원)
                            var typeAnnotation = ParseBitwiseOrExpression();
                            // 향후 타입 검증을 위해 매개변수에 타입 정보 저장 (현재는 단순 저장)
                            parameters.Add("**" + kwargsParam + ":" + typeAnnotation?.ToString());
                        }
                        else
                        {
                            parameters.Add("**" + kwargsParam);
                        }
                    }
                    else if (CheckOp("*"))
                    {
                        // Check for bare * (keyword-only marker)
                        Advance(); // consume *
                        
                        if (CheckComma() || CheckRParen())
                        {
                            // Bare * - keyword-only marker (PEP 3102)
                            seenKeywordOnlyMarker = true;
                            parameters.Add("*"); // Mark keyword-only separator
                        }
                        else
                        {
                            // *args
                            var argsParam = Consume(TokenType.NAME, "Expected parameter name after *").Lexeme;
                            parameters.Add("*" + argsParam);
                            seenKeywordOnlyMarker = true;
                        }
                    }
                    else
                    {
                        // Regular parameter (CPython 호환 방식)
                        var param = Consume(TokenType.NAME, "Expected parameter name").Lexeme;
                        var paramString = param;
                        
                        // Type annotation 처리 (CPython 3.12: union syntax 지원)
                        if (MatchOp(":"))
                        {
                            var typeAnnotation = ParseTypeAnnotation(); // 타입 어노테이션 전용 파서
                            paramString += ":" + typeAnnotation?.ToString();
                        }
                        
                        // Default value 처리 (CPython style)
                        if (MatchOp("="))
                        {
                            var defaultValue = ParseConditionalExpression();
                            paramString += "=" + defaultValue?.ToString();
                        }
                        
                        parameters.Add(paramString);
                    }

                    // Skip NEWLINE, NL, and COMMENT tokens after processing a parameter
                    while (Check(TokenType.NEWLINE) || Check(TokenType.NL) || Check(TokenType.COMMENT))
                    {
                        Advance();
                    }

                } while (MatchOp(","));
            }

            return parameters;
        }

        // CPython PEG: block: NEWLINE INDENT statements DEDENT | simple_stmts
        private List<Statement> ParseBlock(bool isClassBody = false)
        {
            var statements = new List<Statement>();
            
            // CPython PEG: NEWLINE INDENT statements DEDENT
            if (Match(TokenType.NEWLINE))
            {
                // Expect INDENT token
                if (!Check(TokenType.INDENT))
                {
                    throw new Exception("Expected an indented block after ':'");
                }
                Advance(); // consume INDENT
                
                // Parse statements until final DEDENT (class body termination)
                while (!IsAtEnd() && !Check(TokenType.ENDMARKER))
                {
                    // Skip empty lines - Enhanced CPython 3.12 compatible handling
                    if (Match(TokenType.NEWLINE) || Match(TokenType.NL))
                    {
                        // Check if this is followed by meaningful content
                        if (Check(TokenType.NEWLINE) || Check(TokenType.NL))
                        {                        }
                        continue;
                    }
                    
                    var stmt = ParseStatement();
                    if (stmt != null)
                    {
                        statements.Add(stmt);
                    }
                    else
                    {
                        // Enhanced DEDENT handling - only for class bodies
                        if (Check(TokenType.DEDENT))
                        {
                            if (isClassBody)
                            {
                                // Enhanced lookahead to see if class body continues
                                var nextTokenIndex = _current + 1;

                                // Skip NEWLINE tokens to find the next meaningful token
                                while (nextTokenIndex < _tokens.Count &&
                                       (_tokens[nextTokenIndex].Type == TokenType.NEWLINE ||
                                        _tokens[nextTokenIndex].Type == TokenType.NL))
                                {
                                    nextTokenIndex++;
                                }

                                if (nextTokenIndex < _tokens.Count)
                                {
                                    var nextToken = _tokens[nextTokenIndex];

                                    // If next meaningful token is DEF, CLASS, or decorator (@), check indent level
                                    if ((nextToken.Type == TokenType.NAME && (nextToken.Lexeme == "def" || nextToken.Lexeme == "class")) || nextToken.Type == TokenType.OP)
                                    {
                                        // Check if token is at module level (Column 0) or class level (indented)
                                        if (nextToken.Column == 0)
                                        {
                                            break; // Module level token - class body is done
                                        }
                                        else
                                        {
                                            Advance(); // consume the DEDENT
                                            continue; // continue to parse the next method/class
                                        }
                                    }
                                    // If next token is INDENT, look further to see what comes after it
                                    else if (nextToken.Type == TokenType.INDENT)
                                    {
                                        var afterIndentIndex = nextTokenIndex + 1;
                                        if (afterIndentIndex < _tokens.Count)
                                        {
                                            var tokenAfterIndent = _tokens[afterIndentIndex];
                                            // Check if there's method content (statements, not DEF)
                                            if (tokenAfterIndent.Type == TokenType.NAME && tokenAfterIndent.Lexeme == "def")
                                            {
                                                Advance(); // consume the DEDENT
                                                continue; // continue to parse the next method
                                            }
                                            else if (tokenAfterIndent.Type == TokenType.NAME ||
                                                    (tokenAfterIndent.Type == TokenType.NAME && (tokenAfterIndent.Lexeme == "return" ||
                                                     tokenAfterIndent.Lexeme == "if" || tokenAfterIndent.Lexeme == "for" ||
                                                     tokenAfterIndent.Lexeme == "while" || tokenAfterIndent.Lexeme == "try")))
                                            {
                                                Advance(); // consume the DEDENT
                                                continue; // continue parsing method body
                                            }
                                        }
                                    }
                                    // Check if this is an IDENTIFIER at the same indent level as class body
                                    else if (nextToken.Type == TokenType.NAME)
                                    {
                                        // CPython 3.12: Check relative indentation level to determine if still in class body
                                        // Find the class definition's base indentation level
                                        var classIndentLevel = 0;
                                        for (int i = _current - 1; i >= 0; i--)
                                        {
                                            var token = _tokens[i];
                                            if (token.Type == TokenType.NAME && token.Lexeme == "class")
                                            {
                                                classIndentLevel = token.Column;
                                                break;
                                            }
                                        }

                                        // If next token is at the same level or less indented than class definition, class body ended
                                        if (nextToken.Column <= classIndentLevel)
                                        {
                                            break; // Class body is done
                                        }
                                        else
                                        {
                                            Advance(); // consume the DEDENT
                                            continue; // continue parsing method body
                                        }
                                    }
                                }

                            }
                            else
                            {
                            }
                            break;
                        }

                        // Check for other block-ending tokens
                        if (CheckKeyword("except") || CheckKeyword("finally") || CheckKeyword("else"))
                        {
                            break;
                        }

                        // Skip unexpected tokens
                        if (!IsAtEnd() && !Check(TokenType.ENDMARKER))
                        {
                            Advance();
                        }
                    }
                }
                
                // Consume DEDENT
                if (Check(TokenType.DEDENT))
                {
                    Advance();
                }
            }
            else
            {
                // CPython PEG: simple_stmts (single line)
                var stmt = ParseStatement();
                if (stmt != null)
                {
                    statements.Add(stmt);
                }
            }
            
            // Empty block gets a pass statement (like CPython)
            if (statements.Count == 0)
            {
                statements.Add(new PassStatement());
            }
            
            return statements;
        }

        private List<Statement> ParseBlockOrSingleStatement()
        {
            var statements = new List<Statement>();

            // CPython 3.12 방식: 정확한 블록 파싱

            // NEWLINE 토큰이 있다면 소비 (블록이 시작됨을 의미)
            if (Match(TokenType.NEWLINE))
            {
                // 블록 구문 - 들여쓰기된 문장들

                // 주석과 NL 토큰들을 건너뛰고 INDENT 토큰 찾기
                while (Match(TokenType.COMMENT) || Match(TokenType.NL))
                {
                    // 주석과 개행은 건너뛰기
                }

                // INDENT 토큰을 기대
                if (!Check(TokenType.INDENT))
                {
                    throw new Exception("Expected an indented block after ':'");
                }

                // INDENT 토큰 소비
                Advance();
                
                // DEDENT 토큰이 나올 때까지 문장들을 파싱
                while (!IsAtEnd() && !Check(TokenType.DEDENT) && !Check(TokenType.ENDMARKER))
                {
                    // 빈 줄은 건너뛰기 (both NEWLINE and NL)
                    if (Match(TokenType.NEWLINE) || Match(TokenType.NL))
                    {
                        continue;
                    }
                    
                    var stmt = ParseStatement();
                    if (stmt != null)
                    {
                        statements.Add(stmt);
                    }
                    else
                    {
                        // If ParseStatement returns null, check if we've hit a DEDENT or block-ending tokens
                        if (Check(TokenType.DEDENT) || CheckKeyword("except") || CheckKeyword("finally") || CheckKeyword("else"))
                        {
                            break; // These tokens signal end of current block
                        }
                        // If not a block-ending token, this might be another parsing issue
                        // Advance to avoid infinite loop on other unexpected tokens
                        if (!IsAtEnd() && !Check(TokenType.ENDMARKER))
                        {
                            Advance();
                        }
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
            return Check(TokenType.DEDENT) || Check(TokenType.ENDMARKER);
        }

        private Statement ParseExpressionOrAssignment()
        {
            // CPython 3.12: Skip INDENT tokens in expression parsing
            while (Check(TokenType.INDENT))
            {
                Advance(); // consume INDENT
            }

            // CPython 3.12: 블록 구조 토큰들은 표현식이 아니므로 건너뛰기
            if (Check(TokenType.DEDENT) ||
                CheckKeyword("except") || CheckKeyword("finally") ||
                CheckKeyword("else") || CheckKeyword("elif"))
            {
                return null;
            }

            // CPython 3.12: NL과 NEWLINE 토큰들은 단순히 무시하고 다음 토큰으로 진행
            while (Check(TokenType.NL) || Check(TokenType.NEWLINE))
            {
                Advance();

                // 만약 모든 토큰을 소모했다면 null 반환
                if (IsAtEnd())
                {
                    return null;
                }
            }

            try
            {
                var expr = ParseExpression();
                
                // CPython 3.12: Check for comma-separated assignment targets (tuple unpacking)
                // This handles cases like: x, y = (1, 2)
                if (CheckComma())
                {
                    // Look ahead to see if this is an assignment context
                    var savedPos = _current;
                    var targets = new List<Expression> { expr };
                    
                    // Collect all comma-separated expressions
                    while (MatchOp(","))
                    {
                        targets.Add(ParseExpression());
                    }
                    
                    // Check if this is actually an assignment
                    if (CheckEquals())
                    {
                        // This is tuple unpacking assignment: x, y = value
                        Advance(); // consume '='
                        
                        // Parse the right-hand side - could be a single value or comma-separated tuple
                        var firstValue = ParseExpression();
                        var value = firstValue;
                        
                        // Check if there are more comma-separated values on the right side
                        if (CheckComma())
                        {
                            var rightValues = new List<Expression> { firstValue };
                            while (MatchOp(","))
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
                        while (MatchOp(","))
                        {
                            tupleExpr.Elements.Add(ParseExpression());
                        }
                        return new ExpressionStatement(tupleExpr);
                    }
                }
                
                // CPython 3.12: Check for annotated assignment (target: type = value)
                if (MatchOp(":"))
                {
                    var annotation = ParseBitwiseOrExpression(); // Python 3.12 Type Union 지원
                    
                    // Check if there's an assignment as well
                    if (MatchOp("="))
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
                // Check for regular assignment (including chained assignment)
                else if (MatchOp("="))
                {
                    // Parse chained assignment: a = b = c + d
                    var targets = new List<Expression> { expr };
                    
                    // Collect all assignment targets from left to right
                    while (true)
                    {
                        var rightExpr = ParseExpression();
                        
                        // Check if there's another assignment operator (chained assignment)
                        if (MatchOp("="))
                        {
                            targets.Add(rightExpr);
                        }
                        else
                        {
                            // This is the final value expression
                            return CreateChainedAssignment(targets, rightExpr);
                        }
                    }
                }
                
                // Check for augmented assignment (CPython style - separate from expression parsing)
                if (IsAugmentedAssignmentOperator())
                {
                    return ParseAugmentedAssignment(expr);
                }
                
                // Check for walrus operator
                if (MatchOp(":="))
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
            if (CheckKeyword("lambda"))
            {
                return ParseLambdaExpression();
            }

            // Parse first expression
            var expr = ParseConditionalExpression();

            // Check for comma-separated expressions (tuple)
            if (CheckComma())
            {
                var elements = new List<Expression> { expr };

                while (MatchOp(","))
                {
                    // Handle trailing comma (e.g., "1, 2,")
                    if (Check(TokenType.NEWLINE) || Check(TokenType.ENDMARKER) ||
                        CheckRParen() || CheckOp("]") ||
                        CheckColon() || CheckSemicolon())
                    {
                        break;
                    }

                    elements.Add(ParseConditionalExpression());
                }

                // Only create tuple if we have more than one element
                if (elements.Count > 1)
                {
                    return new TupleExpression(elements);
                }
                else
                {
                    // Single element with trailing comma is still a tuple
                    return expr;
                }
            }

            return expr;
        }

        private Expression ParseConditionalExpression()
        {
            // Lambda expressions have lowest precedence, allow them in lambda bodies
            if (CheckKeyword("lambda"))
            {
                return ParseLambdaExpression();
            }
            
            // Use new binary expression parser for better precedence handling
            var expr = ParseBinaryExpression();
            
            // Check for conditional: expr if condition else alternative
            // Skip conditional expression parsing when inside comprehension or match pattern
            // Also skip if this looks like an if statement (has ':' before 'else')
            if (CheckKeyword("if") && !_inComprehension && !_inMatchPattern)
            {
                // Look ahead to see if this is a conditional expression or if statement
                var savePos = _current;
                Advance(); // consume IF

                // Parse condition to find what comes after
                var tempCondition = ParseBinaryExpression();

                // If we find ':' before 'else', this is an if statement, not a conditional expression
                if (CheckColon())
                {
                    // This is an if statement, not a conditional expression
                    // Restore position and return the original expression
                    _current = savePos;
                    return expr;
                }

                // If we find 'else', this is a conditional expression
                if (!CheckKeyword("else"))
                {
                    // This is likely an incomplete conditional or an if statement
                    // Restore position and return the original expression
                    _current = savePos;
                    return expr;
                }

                // This is a valid conditional expression: expr if condition else alternative
                ConsumeKeyword("else", "Expected 'else' in conditional expression");
                var alternative = ParseConditionalExpression(); // Right-associative

                return new ConditionalExpression(expr, tempCondition, alternative);
            }
            
            return expr;
        }

        private Expression ParseOrExpression()
        {
            var expr = ParseAndExpression();
            
            while (MatchKeyword("or"))
            {
                var op = Previous().Lexeme;
                var right = ParseAndExpression();
                // CPython 3.12: Create BoolOpExpression for proper short-circuit evaluation
                var values = new List<Expression> { expr, right };
                expr = new BoolOpExpression(op, values);
            }
            
            return expr;
        }

        private Expression ParseAndExpression()
        {
            var expr = ParseComparisonExpression();
            
            while (MatchKeyword("and"))
            {
                var op = Previous().Lexeme;
                var right = ParseComparisonExpression();
                // CPython 3.12: Create BoolOpExpression for proper short-circuit evaluation
                var values = new List<Expression> { expr, right };
                expr = new BoolOpExpression(op, values);
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
            // Parse left operand first - CPython 3.12: comparison should parse bitwise_or first
            var expr = ParseBitwiseOrExpression();
            
            // CPython 3.12 Grammar: comparison: expr (comp_op expr)*
            // Handle compound operators with PEG-style pattern matching
            while (true)
            {
                string op = TryMatchComparisonOperator();
                if (op == null) break;

                var right = ParseBitwiseOrExpression();
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
            
            // CPython 3.12: All comparison operators are OP tokens
            if (MatchOp("==")) return "==";
            if (MatchOp("!=")) return "!=";
            if (MatchOp("<=")) return "<=";
            if (MatchOp(">=")) return ">=";
            if (MatchOp("<")) return "<";
            if (MatchOp(">")) return ">";
            if (MatchKeyword("in")) return "in";
            if (MatchKeyword("is")) return "is";
            
            return null; // No comparison operator found
        }

        private Expression ParseBitwiseOrExpression()
        {
            var expr = ParseBitwiseXorExpression();

            while (MatchOp("|"))
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
            
            while (Match(TokenType.OP))
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
            
            while (MatchOp("&"))
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
            
            while (Match(TokenType.OP, TokenType.OP))
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
            
            while (MatchOp("+") || MatchOp("-"))
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
            
            while (MatchOp("*") || MatchOp("/") || MatchOp("//") || MatchOp("%"))
            {
                var op = Previous().Lexeme;
                var right = ParseUnaryExpression();
                expr = new BinaryOpExpression(expr, op, right);
            }
            
            return expr;
        }

        private Expression ParseUnaryExpression()
        {
            if (MatchOp("+") || MatchOp("-") || MatchOp("~") || MatchKeyword("not"))
            {
                var op = Previous().Lexeme;
                var expr = ParseUnaryExpression();
                return new UnaryOpExpression(op, expr);
            }
            
            if (MatchKeyword("await"))
            {
                // CPython 3.12: await는 async def 내부에서만 사용 가능
                if (!_inAsyncFunction)
                {
                    throw CreateSyntaxError("'await' outside function");
                }
                
                var expr = ParseUnaryExpression();
                return new AwaitExpression(expr);
            }
            
            return ParsePowerExpression();
        }

        private Expression ParsePowerExpression()
        {
            var expr = ParsePostfixExpression();
            
            if (MatchOp("**"))
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
                if (MatchOp("("))
                {
                    // Function call: func(args)
                    var args = new List<Expression>();
                    
                    if (!CheckRParen())
                    {
                        do
                        {
                            // Use ParseConditionalExpression to avoid tuple parsing in function arguments
                            args.Add(ParseConditionalExpression());
                        } while (MatchOp(","));
                    }
                    
                    ConsumeOp(")", "Expected ')' after function arguments");
                    expr = new CallExpression(expr, args);
                }
                else if (MatchOp("["))
                {
                    // Parse slice or subscript
                    var sliceOrIndex = ParseSliceOrIndex();
                    
                    ConsumeClosingBracket( "Expected ']' after subscript");
                    
                    expr = new SubscriptExpression(expr, sliceOrIndex);
                }
                else if (MatchOp("."))
                {
                    // Attribute access: obj.attr
                    var name = Consume(TokenType.NAME, "Expected attribute name after '.'").Lexeme;
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
            // CPython 3.12: Skip NL and INDENT tokens before parsing primary expressions
            SkipNewlines();

            // Skip INDENT tokens in primary expression parsing
            while (Check(TokenType.INDENT))
            {
                Advance();
            }

            // Starred expression: *variable (for unpacking)
            if (MatchOp("*"))
            {
                var expr = ParsePrimaryExpression();
                return new StarExpression(expr);
            }
            
            // CPython 3.12: True, False, None are NAME tokens
            if (Check(TokenType.NAME))
            {
                var token = Peek();
                if (token.Lexeme == "True")
                {
                    Advance();
                    return new ConstantExpression(PyBool.True);
                }
                if (token.Lexeme == "False")
                {
                    Advance();
                    return new ConstantExpression(PyBool.False);
                }
                if (token.Lexeme == "None")
                {
                    Advance();
                    return new ConstantExpression(PyNone.Instance);
                }
            }

            // CPython 3.12: All numbers are NUMBER tokens
            if (Match(TokenType.NUMBER))
            {
                var lexeme = Previous().Lexeme;

                // Parse different number types
                if (lexeme.Contains('.') || lexeme.ToLower().Contains('e'))
                {
                    var value = double.Parse(lexeme);
                    return new ConstantExpression(new PyFloat(value, lexeme));
                }
                else if (lexeme.ToLower().EndsWith('j'))
                {
                    // Complex number - for now, treat as string
                    return new ConstantExpression(new PyString(lexeme));
                }
                else
                {
                    var value = int.Parse(lexeme);
                    return new ConstantExpression(new PyInt(value));
                }
            }
            
            if (Match(TokenType.STRING))
            {
                var lexeme = Previous().Lexeme;
                // Strip quotes from string literals - CPython compatibility
                var value = lexeme.TrimStringLiteralQuotes();
                return new ConstantExpression(new PyString(value));
            }

            if (Match(TokenType.BYTES))
            {
                var lexeme = Previous().Lexeme;
                // Parse binary literal: b'...', b"..."
                var value = lexeme;

                // Strip prefix and quotes from binary literals
                if (lexeme.StartsWith("b'") && lexeme.EndsWith("'"))
                {
                    value = lexeme.Substring(2, lexeme.Length - 3);
                }
                else if (lexeme.StartsWith("b\"") && lexeme.EndsWith("\""))
                {
                    value = lexeme.Substring(2, lexeme.Length - 3);
                }
                else if (lexeme.StartsWith("b'''") && lexeme.EndsWith("'''"))
                {
                    value = lexeme.Substring(4, lexeme.Length - 7);
                }
                else if (lexeme.StartsWith("b\"\"\"") && lexeme.EndsWith("\"\"\""))
                {
                    value = lexeme.Substring(4, lexeme.Length - 7);
                }

                // Convert string to bytes
                var bytes = System.Text.Encoding.UTF8.GetBytes(value);
                return new ConstantExpression(new PyBytes(bytes));
            }

            // CPython 3.12: All string types are handled as STRING tokens
            // String prefix information (r, b, f, u) is preserved in the lexeme
            
            if (Match(TokenType.NAME))
            {
                var identifierToken = Previous();
                var identifierName = identifierToken.Lexeme;
                
                // f-string (enhanced support)
                if (identifierName == "f" && Check(TokenType.STRING))
                {
                    Advance(); // consume the string part
                    var fstringContent = Previous().Lexeme;
                    return ParseFString(fstringContent);
                }
                
                // Regular identifier
                var nameExpr = new NameExpression(identifierName);
                SetSourceLocation(nameExpr, identifierToken);
                return nameExpr;
            }
            
            // Handle 'type' keyword as identifier in expressions (for metaclass usage)
            if (MatchKeyword("type"))
            {
                return new NameExpression("type");
            }
            
            // Parentheses, Tuples, or Generator Expressions
            if (MatchOp("("))
            {
                if (CheckRParen())
                {
                    Advance(); // empty tuple
                    return new TupleExpression(new List<Expression>());
                }
                
                var first = ParseExpression();
                
                // Check for walrus operator inside parentheses
                if (CheckOp(":="))
                {
                    if (first is NameExpression nameExpr)
                    {
                        Advance(); // consume WALRUS
                        var value = ParseExpression();
                        ConsumeOp(")", "Expected ')' after walrus expression");
                        return new WalrusExpression(nameExpr.Name, value);
                    }
                    throw new Exception("Invalid walrus operator target in parentheses");
                }
                // Check if this is a generator expression
                else if (CheckKeyword("for"))
                {
                    // This is a generator expression: (expr for ...)
                    var generators = ParseComprehensionGenerators();
                    ConsumeOp(")", "Expected ')' after generator expression");
                    return new GeneratorExpression(first, generators);
                }
                else if (MatchOp(","))
                {
                    // This is a tuple
                    var elements = new List<Expression> { first };
                    
                    if (!CheckRParen()) // Handle trailing comma
                    {
                        do
                        {
                            elements.Add(ParseExpression());
                        } while (MatchOp(",") && !CheckRParen());
                    }
                    
                    ConsumeOp(")", "Expected ')' after tuple elements");
                    return new TupleExpression(elements);
                }
                else
                {
                    // Just parentheses
                    ConsumeOp(")", "Expected ')' after expression");
                    return first;
                }
            }
            
            // Lists - CPython 3.12 compatibility
            if (MatchOp("[") || MatchOp("["))
            {
                return ParseListExpression();
            }
            
            // Dictionaries
            if (MatchOp("{"))
            {
                return ParseDictExpression();
            }

            // F-strings (PEP 701 Enhanced F-strings)
            if (Check(TokenType.FSTRING_START))
            {
                return ParseFStringExpression();
            }

            // Handle DEDENT tokens gracefully - they indicate end of block, not primary expressions
            if (Check(TokenType.DEDENT))
            {
                // DEDENT should not be consumed here - let the block parser handle it
                return null;
            }

            throw new Exception($"Unexpected token in primary expression: {Peek().Type}({Peek().Lexeme}) at {Peek().Line}:{Peek().Column}");
        }

        private Expression ParseListExpression()
        {
            // Empty list - CPython 3.12 compatibility
            if (CheckOp("]") || CheckOp("]"))
            {
                ConsumeClosingBracket(); // Handle both RSQB and OP(])
                return new ListExpression(new List<Expression>());
            }
            
            // Parse first element - use ConditionalExpression to avoid tuple parsing
            var firstElement = ParseConditionalExpression();
            
            // Check if this is a list comprehension
            if (CheckKeyword("for"))
            {
                // This is a list comprehension: [expr for ...]
                var generators = ParseComprehensionGenerators();
                ConsumeClosingBracket("Expected ']' after list comprehension");
                return new ListComprehension(firstElement, generators);
            }
            else
            {
                // This is a regular list literal
                var elements = new List<Expression> { firstElement };

                while (MatchOp(",") && !(CheckOp("]") || CheckOp("]")))
                {
                    // CPython 3.12: Skip newlines and indentation after comma in multiline lists
                    SkipNewlines();
                    SkipIndentationTokens();

                    if (CheckOp("]") || CheckOp("]")) break; // trailing comma

                    elements.Add(ParseConditionalExpression());

                    // Skip any trailing whitespace/indentation
                    SkipNewlines();
                    SkipDedentationTokens();
                }

                ConsumeClosingBracket("Expected ']' after list elements");
                return new ListExpression(elements);
            }
        }

        private Expression ParseDictExpression()
        {
            // CPython 3.12: In match pattern context, parse as mapping pattern
            if (_inMatchPattern)
            {
                return ParseMappingPatternInDict();
            }

            // CPython 3.12: Handle indentation in multiline dict/set properly
            SkipNewlines();
            SkipIndentationTokens();

            // Empty dict/set
            if (CheckOp("}"))
            {
                Advance(); // empty dict
                return new DictExpression(new List<(Expression, Expression)>());
            }
            
            var firstExpr = ParseConditionalExpression();

            if (MatchOp(":"))
            {
                // This might be a dictionary literal or dict comprehension
                var firstValue = ParseConditionalExpression();
                
                // Check if this is a dict comprehension
                if (CheckKeyword("for"))
                {
                    // This is a dict comprehension: {key: value for ...}
                    var generators = ParseComprehensionGenerators();
                    while (Match(TokenType.DEDENT)) { }
                    SkipNewlines();
                    ConsumeOp("}", "Expected '}' after dict comprehension");
                    return new DictComprehension(firstExpr, firstValue, generators);
                }
                else
                {
                    // This is a regular dictionary literal
                    var items = new List<(Expression Key, Expression Value)>();
                    items.Add((firstExpr, firstValue));
                    
                    while (MatchOp(",") && !CheckOp("}"))
                    {
                        // CPython 3.12: Skip newlines and indentation after comma in multiline dictionaries
                        SkipNewlines();
                        SkipIndentationTokens();
                        
                        if (CheckOp("}")) break; // trailing comma
                        
                        var key = ParseConditionalExpression();
                        ConsumeColon( "Expected ':' after dictionary key");
                        var value = ParseConditionalExpression();
                        items.Add((key, value));
                        
                        // Skip any trailing whitespace/indentation
                        SkipNewlines();
                        SkipDedentationTokens();
                    }
                    
                    // CPython 3.12: Skip trailing DEDENT before closing brace
                    SkipDedentationTokens();
                    SkipNewlines();
                    
                    ConsumeOp("}", "Expected '}' after dictionary items");
                    return new DictExpression(items);
                }
            }
            else
            {
                // This might be a set literal or set comprehension
                if (CheckKeyword("for"))
                {
                    // This is a set comprehension: {expr for ...}
                    var generators = ParseComprehensionGenerators();
                    while (Match(TokenType.DEDENT)) { }
                    SkipNewlines();
                    ConsumeOp("}", "Expected '}' after set comprehension");
                    return new SetComprehension(firstExpr, generators);
                }
                else
                {
                    // This is a regular set literal
                    var elements = new List<Expression>();
                    elements.Add(firstExpr);
                    
                    while (MatchOp(",") && !CheckOp("}"))
                    {
                        // CPython 3.12: Skip newlines and indentation after comma in multiline sets
                        SkipNewlines();
                        SkipIndentationTokens();
                        
                        if (CheckOp("}")) break; // trailing comma
                        
                        elements.Add(ParseExpression());
                        
                        // Skip any trailing whitespace/indentation
                        SkipNewlines();
                        SkipDedentationTokens();
                    }
                    
                    // CPython 3.12: Skip trailing DEDENT before closing brace
                    SkipDedentationTokens();
                    SkipNewlines();
                    
                    ConsumeOp("}", "Expected '}' after set elements");
                    return new SetExpression(elements);
                }
            }
        }

        /// <summary>
        /// Parse PEP 701 Enhanced F-strings: f"text {expr} more text"
        /// CPython 3.12 compatible f-string parsing
        /// </summary>
        private Expression ParseFStringExpression()
        {
            var parts = new List<Expression>(); // FStringExpression expects List<Expression>

            // Consume FSTRING_START token
            Consume(TokenType.FSTRING_START, "Expected f-string start");

            while (!Check(TokenType.FSTRING_END) && !IsAtEnd())
            {
                if (Check(TokenType.FSTRING_MIDDLE))
                {
                    // String part between expressions
                    var stringPart = Advance().Lexeme;
                    parts.Add(new ConstantExpression(new PyString(stringPart)));
                }
                else if (Check(TokenType.OP) && Peek().Lexeme == "{")
                {
                    // Expression part: { expression }
                    Advance(); // consume '{'

                    // Parse the expression inside the braces
                    var expr = ParseExpression();

                    // Check for format specifier: : format_spec
                    Expression? formatSpecExpr = null;
                    if (Check(TokenType.OP) && Peek().Lexeme == ":")
                    {
                        Advance(); // consume ':'

                        // Parse format specifier as a series of expressions and literals
                        formatSpecExpr = ParseFormatSpecifier();
                    }

                    // Create formatted expression if format spec exists
                    if (formatSpecExpr != null)
                    {
                        // Create a format expression with the format specifier
                        parts.Add(new FormatExpressionWithSpec(expr, formatSpecExpr));
                    }
                    else
                    {
                        parts.Add(expr);
                    }

                    // Consume closing '}'
                    if (Check(TokenType.OP) && Peek().Lexeme == "}")
                    {
                        Advance();
                    }
                    else
                    {
                        throw new Exception($"Expected '}}' after f-string expression, got {Peek().Type}({Peek().Lexeme})");
                    }
                }
                else
                {
                    // Skip any unexpected tokens (defensive programming)
                    Advance();
                }
            }

            // Consume FSTRING_END token
            Consume(TokenType.FSTRING_END, "Expected f-string end");

            // Create f-string expression with all parts
            return new FStringExpression(parts);
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
            while (CheckKeyword("for"))
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
                ConsumeKeyword("for", "Expected 'for' in comprehension");
                
                // Parse target (can be simple name or tuple/list unpacking)
                var target = ParseComprehensionTarget();
                
                ConsumeKeyword("in", "Expected 'in' after comprehension target");
                
                // Parse iterable expression
                var iter = ParseExpression();
                
                // Parse optional if conditions
                var ifs = new List<Expression>();
                while (MatchKeyword("if"))
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
            if (CheckLParen())
            {
                // Tuple unpacking: (x, y)
                Advance(); // consume '('
                var elements = new List<Expression>();
                
                if (!CheckRParen())
                {
                    do
                    {
                        elements.Add(ParseComprehensionTarget());
                    } while (MatchOp(","));
                }
                
                ConsumeOp(")", "Expected ')' after tuple target");
                return new TupleExpression(elements);
            }
            else if (CheckOp("["))
            {
                // List unpacking: [x, y]
                Advance(); // consume OP([)
                var elements = new List<Expression>();
                
                if (!CheckOp("]"))
                {
                    do
                    {
                        elements.Add(ParseComprehensionTarget());
                    } while (MatchOp(","));
                }
                
                ConsumeClosingBracket( "Expected ']' after list target");
                return new ListExpression(elements);
            }
            else if (Check(TokenType.NAME))
            {
                // Simple name or start of comma-separated tuple
                var name = Advance().Lexeme;
                var firstElement = new NameExpression(name);
                
                // Check if this is a comma-separated tuple (k, v without parentheses)
                if (CheckComma())
                {
                    var elements = new List<Expression> { firstElement };
                    
                    while (MatchOp(","))
                    {
                        if (Check(TokenType.NAME))
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
            ConsumeColon( "Expected ':' after if condition");
            
            var body = ParseBlockOrSingleStatement();
            var orElse = new List<Statement>();
            
            // Handle elif and else clauses
            while (MatchKeyword("elif"))
            {
                var elifCondition = ParseExpression();
                ConsumeColon( "Expected ':' after elif condition");
                var elifBody = ParseBlockOrSingleStatement();
                
                // Convert elif to nested if-else structure (CPython approach)
                var nestedIf = new IfStatement(elifCondition, elifBody, new List<Statement>());
                orElse.Add(nestedIf);
            }
            
            if (MatchKeyword("else"))
            {
                ConsumeColon( "Expected ':' after else");
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
            ConsumeColon( "Expected ':' after while condition");
            
            var body = ParseBlockOrSingleStatement();
            
            // Check for optional else clause
            List<Statement>? elseClause = null;
            if (CheckKeyword("else"))
            {
                Advance(); // consume 'else'
                ConsumeColon( "Expected ':' after 'else'");
                elseClause = ParseBlockOrSingleStatement();
            }
            
            return new WhileStatement(condition, body, elseClause);
        }
        private Statement ParseForStatement()
        {
            // Parse for target (left side of 'in') - support both simple names and tuple unpacking
            var target = ParseForTarget();
            ConsumeKeyword("in", "Expected 'in' in for statement");
            var iterable = ParseExpression(); // in iterable  
            ConsumeColon( "Expected ':' after for clause");
            
            var body = ParseBlockOrSingleStatement();
            
            // Check for optional else clause
            List<Statement>? elseClause = null;
            if (CheckKeyword("else"))
            {
                Advance(); // consume 'else'
                ConsumeColon( "Expected ':' after 'else'");
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
                // Check if this is simple flat tuple unpacking: for x, y in items:
                var targetNames = new List<string>();
                bool isSimpleTuple = true;
                
                foreach (var element in tupleExpr.Elements)
                {
                    if (element is NameExpression elementName)
                    {
                        targetNames.Add(elementName.Name);
                    }
                    else
                    {
                        // Complex nested structure like (x, (y, z))
                        isSimpleTuple = false;
                        break;
                    }
                }
                
                if (isSimpleTuple)
                {
                    // Use existing ForTupleStatement for simple flat unpacking
                    return new ForTupleStatement(targetNames, iterable, body, elseClause);
                }
                else
                {
                    // Use new ForComplexStatement for nested unpacking
                    return new ForComplexStatement(target, iterable, body, elseClause);
                }
            }
            else
            {
                throw new Exception("For loop target must be a variable name or tuple unpacking");
            }
        }

        /// <summary>
        /// Parse for loop target - supports simple names, tuple unpacking, and nested parentheses
        /// </summary>
        private Expression ParseForTarget()
        {
            var targets = new List<Expression>();
            
            // Parse first target (can be parenthesized or simple identifier)
            targets.Add(ParseForTargetExpression());
            
            // Check for additional targets (comma-separated)
            while (MatchOp(","))
            {
                targets.Add(ParseForTargetExpression());
            }
            
            // Return single expression or tuple
            if (targets.Count == 1)
            {
                return targets[0];
            }
            else
            {
                return new TupleExpression(targets);
            }
        }
        
        /// <summary>
        /// Parse a single for target expression (can be nested)
        /// </summary>
        private Expression ParseForTargetExpression()
        {
            // Handle parenthesized expressions: (a, b) or ((a, b), c)
            if (CheckLParen())
            {
                Advance(); // consume '('
                
                if (CheckRParen())
                {
                    Advance(); // empty tuple
                    return new TupleExpression(new List<Expression>());
                }
                
                var elements = new List<Expression>();
                do
                {
                    elements.Add(ParseForTargetExpression());
                } while (MatchOp(",") && !CheckRParen());
                
                ConsumeOp(")", "Expected ')' after tuple unpacking target");
                
                return elements.Count == 1 ? elements[0] : new TupleExpression(elements);
            }
            
            // Handle simple identifiers
            if (Check(TokenType.NAME))
            {
                var name = Advance().Lexeme;
                return new NameExpression(name);
            }
            
            throw new Exception("Expected variable name or parenthesized tuple in for statement target");
        }
        private Statement ParseTryStatement()
        {
            // CPython PEG: 'try' ':' b=block ...
            ConsumeColon( "Expected ':' after try");
            
            // Parse try body
            var tryBody = ParseBlockOrSingleStatement();
            
            // CPython PEG: Two patterns:
            // Pattern 1: 'try' ':' b=block f=finally_block
            // Pattern 2: 'try' ':' b=block ex=except_block+ el=[else_block] f=[finally_block]
            
            // Try Pattern 1: Check for finally block first
            if (CheckKeyword("finally"))
            {
                Advance(); // consume 'finally'
                ConsumeColon( "Expected ':' after finally");
                var finallyStmts = ParseBlockOrSingleStatement();
                return new TryStatement(tryBody, new List<ExceptHandler>(), null, finallyStmts);
            }
            
            // Try Pattern 2: Parse except handlers
            var handlers = new List<ExceptHandler>();
            
            // Must have at least one except handler for this pattern
            if (!CheckKeyword("except"))
            {
                #if DEBUG_LOG
                Console.WriteLine($"⚠️  Expected EXCEPT but found {Peek().Type}. Available tokens:");
                #endif
                for (int i = 0; i < Math.Min(15, _tokens.Count - _current); i++)
                {
                    var token = _tokens[_current + i];
                    #if DEBUG_LOG
                    Console.WriteLine($"   [{i}] {token.Type}: '{token.Lexeme}' at {token.Line}:{token.Column}");
                    #endif
                }
                throw new Exception("'try' statement must have either 'except' or 'finally' clause");
            }
            
            // Parse except_block+
            while (CheckKeyword("except"))
            {
                Advance(); // consume 'except'
                handlers.Add(ParseExceptHandler());

                // Skip any whitespace, comments, indentation before checking for next EXCEPT
                while (Check(TokenType.NEWLINE) || Check(TokenType.COMMENT) ||
                       Check(TokenType.INDENT) || Check(TokenType.DEDENT))
                {
                    Advance();
                }

                // Also skip any non-keyword tokens (like 'pass') until we find except/else/finally/ENDMARKER
                while (!IsAtEnd() && !CheckKeyword("except") && !CheckKeyword("else") &&
                       !CheckKeyword("finally") && !Check(TokenType.ENDMARKER))
                {
                    if (Check(TokenType.NEWLINE) || Check(TokenType.COMMENT) ||
                        Check(TokenType.INDENT) || Check(TokenType.DEDENT))
                    {
                        Advance();
                    }
                    else if (Check(TokenType.NAME) && Peek().Lexeme == "pass")
                    {
                        Advance(); // skip 'pass' statements
                    }
                    else
                    {
                        break; // stop at unexpected tokens
                    }
                }

            }
            
            // Parse optional else_block
            List<Statement>? elseBody = null;
            if (CheckKeyword("else"))
            {
                Advance(); // consume 'else'
                ConsumeColon( "Expected ':' after else");
                elseBody = ParseBlockOrSingleStatement();
            }
            
            // Parse optional finally_block
            List<Statement>? finallyBody = null;
            if (CheckKeyword("finally"))
            {
                Advance(); // consume 'finally'
                ConsumeColon( "Expected ':' after finally");
                finallyBody = ParseBlockOrSingleStatement();
            }

            // CPython 3.12: Validate that except and except* handlers cannot be mixed in the same try block
            bool hasRegularExcept = handlers.Any(h => !h.IsStar);
            bool hasExceptStar = handlers.Any(h => h.IsStar);


            if (hasRegularExcept && hasExceptStar)
            {
                throw new Exception("cannot have both 'except' and 'except*' on the same 'try'");
            }

            return new TryStatement(tryBody, handlers, elseBody, finallyBody);
        }
        
        private ExceptHandler ParseExceptHandler()
        {
            Expression? exceptionType = null;
            string? exceptionName = null;
            bool isStar = false;

            // Check for except* syntax (PEP 654)
            if (CheckOp("*"))
            {
                Advance(); // consume the *
                isStar = true;
            }
            
            // Parse exception type (optional)
            if (!CheckColon())
            {
                exceptionType = ParseExpression();
                
                // Parse "as name" clause (optional)
                if (MatchKeyword("as"))
                {
                    if (!Check(TokenType.NAME))
                    {
                        throw new Exception("Expected identifier after 'as'");
                    }
                    exceptionName = Advance().Lexeme;
                }
            }
            
            ConsumeColon( "Expected ':' after except clause");
            
            var handlerBody = ParseBlockOrSingleStatement();

            return new ExceptHandler(exceptionType, exceptionName, handlerBody, isStar);
        }
        private Statement ParseWithStatement()
        {
            var items = new List<WithItem>();
            
            // Parse first with item: with expr [as var]
            // CPython 3.12: Use ConditionalExpression to avoid consuming commas
            var contextExpr = ParseConditionalExpression();
            Expression? optionalVars = null;

            if (CheckKeyword("as"))
            {
                Advance(); // consume AS
                optionalVars = ParseConditionalExpression(); // as target (avoid comma parsing)
            }
            
            items.Add(new WithItem(contextExpr, optionalVars));
            
            // Handle multiple context managers: with a, b as x, c:
            while (CheckComma())
            {
                Advance(); // consume comma
                
                // CPython 3.12: Each context manager parsed individually (avoid comma consumption)
                var nextContextExpr = ParseConditionalExpression();
                Expression? nextOptionalVars = null;

                if (CheckKeyword("as"))
                {
                    Advance(); // consume AS
                    nextOptionalVars = ParseConditionalExpression(); // as target (avoid comma parsing)
                }
                
                items.Add(new WithItem(nextContextExpr, nextOptionalVars));
            }
            
            ConsumeColon( "Expected ':' after with statement");
            
            // Parse body
            var body = ParseBlockOrSingleStatement();
            
            return new WithStatement(items, body);
        }
        /// <summary>
        /// Parse match subject expression that supports walrus operator (:=)
        /// CPython 3.12: match subject can be an assignment expression
        /// </summary>
        private Expression ParseMatchSubjectExpression()
        {
            var expr = ParseExpression();

            // Check for walrus operator in match subject
            if (CheckOp(":="))
            {
                if (expr is NameExpression nameExpr)
                {
                    Advance(); // consume WALRUS
                    var value = ParseExpression();
                    return new WalrusExpression(nameExpr.Name, value);
                }
                throw new Exception("Invalid walrus operator target in match subject");
            }

            return expr;
        }

        private Statement ParseMatchStatement()
        {
            var subject = ParseMatchSubjectExpression(); // match subject (supports walrus operator)
            ConsumeColon( "Expected ':' after match subject");
            
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
                if (!(Check(TokenType.NAME) && Peek().Lexeme == "case"))
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
            if (!(Check(TokenType.NAME) && Peek().Lexeme == "case"))
            {
                throw new Exception("Expected 'case'");
            }
            Advance(); // consume "case" identifier
            
            // Parse pattern (may include or patterns)
            var pattern = ParseMatchPattern();
            
            // Parse optional guard (if condition)
            Expression? guard = null;
            if (MatchKeyword("if"))
            {
                guard = ParseExpression();
            }
            
            ConsumeColon( "Expected ':' after case pattern");
            
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
                while (MatchOp("|") && patterns.Count < MAX_OR_PATTERNS)
                {
                    var prevPos = _current;
                    
                    // CPython 3.12: Check for invalid syntax after '|'
                    if (IsAtEnd() || CheckColon() || CheckKeyword("if") || Check(TokenType.NEWLINE))
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
                if (MatchKeyword("as"))
                {
                    var name = Consume(TokenType.NAME, "Expected identifier after 'as' in pattern").Lexeme;
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
            if (CheckOp("["))
            {
                return ParseSequencePattern();
            }
            
            // Handle mapping patterns like {"key": value}
            if (CheckOp("{"))
            {
                return ParseMappingPattern();
            }
            
            // Handle star pattern in isolation
            if (CheckOp("*"))
            {
                Advance(); // consume STAR
                var name = Consume(TokenType.NAME, "Expected identifier after * in pattern");
                return new StarPattern(name.Lexeme);
            }
            
            // Regular expression pattern
            // CPython 3.12: In match patterns, avoid parsing comma-separated expressions as tuples
            return ParseConditionalExpression();
        }
        
        /// <summary>
        /// Parse sequence pattern like [1, 2, *rest, 4]
        /// </summary>
        private Expression ParseSequencePattern()
        {
            ConsumeOp("[", "Expected '['");
            
            var patterns = new List<Expression>();
            bool hasStarPattern = false;
            
            if (!CheckOp("]"))
            {
                do
                {
                    if (CheckOp("*"))
                    {
                        if (hasStarPattern)
                        {
                            throw new Exception("multiple starred patterns in sequence pattern");
                        }
                        hasStarPattern = true;
                        
                        Advance(); // consume STAR
                        var name = Consume(TokenType.NAME, "Expected identifier after * in pattern");
                        patterns.Add(new StarPattern(name.Lexeme));
                    }
                    else
                    {
                        patterns.Add(ParseSingleMatchPattern());
                    }
                } while (MatchOp(",") && !CheckOp("]"));
            }
            
            ConsumeClosingBracket( "Expected ']'");
            return new SequencePattern(patterns);
        }
        
        /// <summary>
        /// Parse mapping pattern like {"key": value, "other": other_value}
        /// </summary>
        private Expression ParseMappingPattern()
        {
            ConsumeOp("{", "Expected '{'");

            var patterns = new Dictionary<string, Expression>();
            string? restVariable = null;

            if (!CheckOp("}"))
            {
                do
                {
                    // Check for **rest pattern
                    if (CheckOp("**"))
                    {
                        Advance(); // consume **
                        var restToken = Consume(TokenType.NAME, "Expected identifier after ** in mapping pattern");
                        restVariable = restToken.Lexeme;
                    }
                    else
                    {
                        // Parse string key
                        var keyToken = Consume(TokenType.STRING, "Expected string key in mapping pattern");
                        var key = keyToken.Lexeme.TrimStringLiteralQuotes(); // Remove quotes
                        ConsumeColon( "Expected ':' after key in mapping pattern");
                        var valuePattern = ParseSingleMatchPattern();
                        patterns[key] = valuePattern;
                    }
                } while (MatchOp(",") && !CheckOp("}"));
            }

            ConsumeOp("}", "Expected '}'");
            return new MappingPattern(patterns, restVariable);
        }

        /// <summary>
        /// CPython 3.12: Parse dict-style mapping pattern in match context
        /// Handles both string and identifier keys like {"name": name, age: age}
        /// </summary>
        private Expression ParseMappingPatternInDict()
        {
            // Skip any newlines and indentation
            SkipNewlines();
            SkipIndentationTokens();

            // Empty dict pattern
            if (CheckOp("}"))
            {
                Advance(); // consume '}'
                return new MappingPattern(new Dictionary<string, Expression>(), null);
            }

            var patterns = new Dictionary<string, Expression>();
            string? restVariable = null;

            if (!CheckOp("}"))
            {
                do
                {
                    // Check for **rest pattern
                    if (CheckOp("**"))
                    {
                        Advance(); // consume **
                        var restToken = Consume(TokenType.NAME, "Expected identifier after ** in mapping pattern");
                        restVariable = restToken.Lexeme;
                    }
                    else
                    {
                        // CPython 3.12: Parse key - can be string literal or identifier
                        string keyStr;

                        if (Check(TokenType.STRING))
                        {
                            var stringToken = Advance();
                            keyStr = stringToken.Lexeme.TrimStringLiteralQuotes(); // Remove quotes
                        }
                        else if (Check(TokenType.NAME))
                        {
                            var identToken = Advance();
                            keyStr = identToken.Lexeme;
                        }
                        else
                        {
                            throw new Exception($"Expected string or identifier for mapping pattern key, got {Peek().Type}");
                        }

                        ConsumeColon( "Expected ':' after mapping pattern key");

                        // CPython 3.12: Parse value pattern - usually an identifier for variable binding
                        Expression valuePattern;
                        if (Check(TokenType.NAME))
                        {
                            var varName = Advance().Lexeme;
                            valuePattern = new NameExpression(varName);
                        }
                        else
                        {
                            // Allow more complex patterns
                            valuePattern = ParseSingleMatchPattern();
                        }

                        patterns[keyStr] = valuePattern;
                    }

                    // Skip any trailing whitespace/indentation
                    SkipNewlines();
                    SkipIndentationTokens();

                } while (MatchOp(",") && !CheckOp("}"));
            }

            // Skip trailing DEDENT/newlines before closing brace
            SkipDedentationTokens();
            SkipNewlines();

            ConsumeOp("}", "Expected '}' after mapping pattern");
            return new MappingPattern(patterns, restVariable);
        }
        
        private Statement ParseReturnStatement()
        {
            // return 뒤에 표현식이 있으면 파싱, 없으면 None
            Expression? value = null;
            if (!Check(TokenType.NEWLINE) && !IsAtEnd() && !Check(TokenType.ENDMARKER))
            {
                value = ParseExpression();
            }
            return new ReturnStatement(value);
        }
        private Statement ParseYieldStatement()
        {
            // Check for 'yield from' (CPython style)
            if (MatchKeyword("from"))
            {
                var value = ParseExpression();
                return new YieldFromStatement(value);
            }
            else
            {
                // Regular yield
                Expression? value = null;
                if (!Check(TokenType.NEWLINE) && !Check(TokenType.ENDMARKER))
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
            // Parse test expression with precedence lower than comma to avoid tuple parsing
            var test = ParseConditionalExpression();

            Expression? msg = null;
            if (MatchOp(","))
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
            if (!Check(TokenType.NEWLINE) && !Check(TokenType.ENDMARKER) && !IsAtEnd())
            {
                exc = ParseExpression();
                
                // from 절이 있는지 확인 (raise ... from ...)
                if (MatchKeyword("from"))
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
                targets.Add(ParseExpression());
            } while (MatchOp(","));
            
            return new DeleteStatement(targets);
        }
        private Statement ParseGlobalStatement()
        {
            // CPython 3.12: global name1, name2, ...
            var names = new List<string>();
            
            do
            {
                var name = Consume(TokenType.NAME, "Expected variable name after 'global'").Lexeme;
                names.Add(name);
            } while (MatchOp(","));
            
            return new GlobalStatement(names);
        }
        private Statement ParseNonlocalStatement()
        {
            // CPython 3.12: nonlocal name1, name2, ...
            var names = new List<string>();
            
            do
            {
                var name = Consume(TokenType.NAME, "Expected variable name after 'nonlocal'").Lexeme;
                names.Add(name);
            } while (MatchOp(","));
            
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
                if (CheckKeyword("as"))
                {
                    Advance(); // consume AS
                    var alias = Consume(TokenType.NAME, "Expected alias name").Lexeme;
                    modules.Add($"{moduleName} as {alias}");
                }
                else
                {
                    modules.Add(moduleName);
                }
                
            } while (MatchOp(","));
            
            return new ImportStatement(modules);
        }
        
        /// <summary>
        /// Parse dotted module name like "package.submodule.module"
        /// </summary>
        private string ParseDottedModuleName()
        {
            var parts = new List<string>();
            
            // First part must be identifier
            parts.Add(Consume(TokenType.NAME, "Expected module name").Lexeme);
            
            // Parse additional dotted parts
            while (MatchOp("."))
            {
                parts.Add(Consume(TokenType.NAME, "Expected identifier after '.'").Lexeme);
            }
            
            return string.Join(".", parts);
        }
        private Statement ParseFromImportStatement()
        {
            var moduleName = Consume(TokenType.NAME, "Expected module name after 'from'").Lexeme;
            ConsumeKeyword("import", "Expected 'import' after module name");
            
            // Check for wildcard import: from module import *
            if (CheckOp("*"))
            {
                Advance(); // consume *
                return new ImportFromStatement(moduleName, new List<string> { "*" });
            }
            
            // Parse comma-separated import list (CPython style)
            var imports = new List<string>();
            
            do
            {
                var itemName = Consume(TokenType.NAME, "Expected import item name").Lexeme;
                
                // Handle "from module import item as alias"
                if (CheckKeyword("as"))
                {
                    Advance(); // consume AS
                    var alias = Consume(TokenType.NAME, "Expected alias name").Lexeme;
                    imports.Add($"{itemName} as {alias}");
                }
                else
                {
                    imports.Add(itemName);
                }
                
            } while (MatchOp(","));
            
            return new ImportFromStatement(moduleName, imports);
        }

        /// <summary>
        /// Parse slice notation (start:stop:step) or regular index
        /// </summary>
        private Expression ParseSliceOrIndex()
        {
            // Check if we have an empty slice [:end] or [::step]
            if (CheckColon())
            {
                // Empty start
                Advance(); // consume COLON
                var stop = CheckColon() || CheckOp("]") ? null : ParseExpression();
                
                // Check for step
                Expression? step = null;
                if (MatchOp(":"))
                {
                    step = CheckOp("]") ? null : ParseExpression();
                }
                
                return new SliceExpression(null, stop, step);
            }
            
            // Parse first expression
            var first = ParseExpression();
            
            // Check if this is a slice
            if (CheckColon())
            {
                Advance(); // consume COLON
                
                var stop = CheckColon() || CheckOp("]") ? null : ParseExpression();
                
                // Check for step
                Expression? step = null;
                if (MatchOp(":"))
                {
                    step = CheckOp("]") ? null : ParseExpression();
                }
                
                return new SliceExpression(first, stop, step);
            }
            
            // Check for multiple indices (comma-separated)
            if (CheckComma())
            {
                var indices = new List<Expression> { first };
                
                while (MatchOp(","))
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
            var currentToken = Peek();

            // CPython 3.12: All assignment operators are OP tokens
            if (currentToken.Type != TokenType.OP) return false;

            return currentToken.Lexeme switch
            {
                "+=" or "-=" or "*=" or "/=" or "%=" or "//=" or "**=" or
                "|=" or "&=" or "^=" or "<<=" or ">>=" or "@=" => true,
                _ => false
            };
        }
        
        /// <summary>
        /// Parse augmented assignment statement (CPython style)
        /// </summary>
        private Statement ParseAugmentedAssignment(Expression target)
        {
            // CPython 3.12: Allow name, attribute, and subscript expressions as targets
            if (!(target is NameExpression || target is AttributeExpression || target is SubscriptExpression))
            {
                throw new Exception("Invalid augmented assignment target");
            }
            
            var opToken = Advance(); // consume augmented assignment operator
            var value = ParseExpression();
            
            // CPython 3.12: Extract binary operator from augmented assignment OP token
            // Convert += to +, -= to -, etc.
            var opString = opToken.Lexeme switch
            {
                "+=" => "+",
                "-=" => "-",
                "*=" => "*",
                "/=" => "/",
                "%=" => "%",
                "//=" => "//",
                "**=" => "**",
                "|=" => "|",
                "&=" => "&",
                "^=" => "^",
                "<<=" => "<<",
                ">>=" => ">>",
                "@=" => "@",
                _ => throw new Exception($"Unknown augmented assignment operator: {opToken.Lexeme}")
            };
            
            // Create augmented assignment statement
            return new AugmentedAssignStatement(target, opString, value);
        }

        /// <summary>
        /// Parse lambda expression: lambda args: body
        /// </summary>
        private Expression ParseLambdaExpression()
        {
            ConsumeKeyword("lambda", "Expected 'lambda'");
            
            var args = new List<string>();
            
            // Parse parameter list (optional)
            if (!CheckColon())
            {
                // Parse first parameter
                if (Check(TokenType.NAME))
                {
                    var param = Advance().Lexeme;
                    var paramString = param;
                    
                    // Handle default value (CPython style: lambda x, y=2: x * y)
                    if (MatchOp("="))
                    {
                        var defaultValue = ParseConditionalExpression();
                        paramString += "=" + defaultValue?.ToString();
                    }
                    
                    args.Add(paramString);
                    
                    // Parse remaining parameters
                    while (MatchOp(","))
                    {
                        if (Check(TokenType.NAME))
                        {
                            param = Advance().Lexeme;
                            paramString = param;
                            
                            // Handle default value for each parameter
                            if (MatchOp("="))
                            {
                                var defaultValue = ParseConditionalExpression();
                                paramString += "=" + defaultValue?.ToString();
                            }
                            
                            args.Add(paramString);
                        }
                        else
                        {
                            throw new Exception("Expected parameter name after comma in lambda");
                        }
                    }
                }
            }
            
            ConsumeColon( "Expected ':' after lambda parameters");
            
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

            while (!IsAtEnd())
            {
                // Check for compound operators first (not in, is not)
                string compoundOp = TryMatchCompoundOperator();
                if (compoundOp != null)
                {
                    var compoundPrecedence = GetCompoundOperatorPrecedence(compoundOp);
                    if (compoundPrecedence < minPrecedence) break;

                    // Handle right associativity
                    var compoundNextMinPrec = compoundPrecedence + 1;
                    var compoundRight = ParseBinaryExpression(compoundNextMinPrec);

                    // Compound operators are always comparison operators
                    left = new CompareExpression(left, compoundOp, compoundRight);
                    continue;
                }

                // Handle single token operators
                if (GetPrecedence(Peek()) < minPrecedence) break;

                var opToken = Advance();
                var precedence = GetPrecedence(opToken);

                // Handle right associativity (like **)
                var nextMinPrec = opToken.IsRightAssociative()
                    ? precedence
                    : precedence + 1;

                var right = ParseBinaryExpression(nextMinPrec);

                // Create appropriate expression type based on operator
                if (IsComparisonOperator(opToken))
                {
                    left = new CompareExpression(left, opToken.Lexeme, right);
                }
                else if (IsBooleanOperator(opToken))
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

        /// <summary>
        /// Try to match compound operators like 'not in' and 'is not'
        /// Returns the compound operator string if found, null otherwise
        /// </summary>
        private string TryMatchCompoundOperator()
        {
            // Check for 'not in' compound operator
            if (IsCurrentNotInOperator())
            {
                Advance(); // consume 'not'
                Advance(); // consume 'in'
                return "not in";
            }

            // Check for 'is not' compound operator
            if (IsCurrentIsNotOperator())
            {
                Advance(); // consume 'is'
                Advance(); // consume 'not'
                return "is not";
            }

            return null;
        }

        /// <summary>
        /// Get precedence for compound operators
        /// </summary>
        private int GetCompoundOperatorPrecedence(string compoundOp)
        {
            return compoundOp switch
            {
                "not in" => 3,  // Same as comparison operators
                "is not" => 3,  // Same as comparison operators
                _ => -1
            };
        }

        private int GetPrecedence(PyToken token)
        {
            // CPython 3.12: All operators are OP tokens, distinguished by lexeme
            if (token.Type == TokenType.OP)
            {
                return token.Lexeme switch
                {
                    "+" => 8,         // Addition
                    "-" => 8,         // Subtraction
                    "*" => 9,         // Multiplication
                    "/" => 9,         // Division
                    "//" => 9,        // Floor division
                    "%" => 9,         // Modulo
                    "**" => 10,       // Exponentiation (right-associative)
                    "<<" => 7,        // Left shift
                    ">>" => 7,        // Right shift
                    "&" => 6,         // Bitwise AND
                    "^" => 5,         // Bitwise XOR
                    "|" => 4,         // Bitwise OR
                    "==" => 3,        // Equal
                    "!=" => 3,        // Not equal
                    "<" => 3,         // Less than
                    ">" => 3,         // Greater than
                    "<=" => 3,        // Less than or equal
                    ">=" => 3,        // Greater than or equal
                    "~" => 11,        // Bitwise NOT (unary)
                    _ => -1           // Unknown operator
                };
            }

            // Handle NAME tokens for keywords like 'and', 'or', 'in', 'is', 'not'
            if (token.Type == TokenType.NAME)
            {
                return token.Lexeme switch
                {
                    "or" => 1,        // Boolean OR (lowest precedence)
                    "and" => 2,       // Boolean AND
                    "not" => 11,      // Boolean NOT (unary, high precedence)
                    "in" => 3,        // Membership test
                    "is" => 3,        // Identity test
                    _ => -1           // Not an operator
                };
            }

            // CPython 3.12: All operators are OP tokens
            return -1;
        }
        
        private bool IsComparisonOperator(PyToken token)
        {
            // CPython 3.12: Comparison operators are OP tokens
            if (token.Type == TokenType.OP)
            {
                return token.Lexeme is "==" or "!=" or "<" or ">" or "<=" or ">=";
            }

            // Keyword-based comparison operators
            if (token.Type == TokenType.NAME)
            {
                return token.Lexeme is "in" or "is";
            }

            // All comparison operators are OP tokens in CPython 3.12
            return false;
        }

        private bool IsBooleanOperator(PyToken token)
        {
            // CPython 3.12: Boolean logical operators are NAME tokens
            if (token.Type == TokenType.NAME)
            {
                return token.Lexeme is "and" or "or";
            }
            return false;
        }
        
        private Expression ParseUnaryOrAtom()
        {
            // Handle unary operators - CPython style
            if (MatchKeyword("not"))
            {
                var expr = ParseUnaryOrAtom(); // Recursive for 'not not x'
                return new UnaryOpExpression("not", expr);
            }
            
            // CPython 3.12: All unary operators are OP tokens
            if (MatchOp("-") || MatchOp("+") || MatchOp("~"))
            {
                var op = Previous().Lexeme;
                var expr = ParseUnaryOrAtom();
                return new UnaryOpExpression(op, expr);
            }
            
            if (MatchKeyword("await"))
            {
                // CPython 3.12: await는 async def 내부에서만 사용 가능
                if (!_inAsyncFunction)
                {
                    throw CreateSyntaxError("'await' outside function");
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
                if (MatchOp("("))
                {
                    // Function call - CPython 호환 키워드 인수 지원
                    var (args, keywords) = ParseFunctionCallArguments();
                    ConsumeOp(")", "Expected ')' after function arguments");
                    expr = new CallExpression(expr, args, keywords);
                }
                else if (MatchOp("["))
                {
                    // Subscript or slice
                    var sliceOrIndex = ParseSliceOrIndex();
                    ConsumeClosingBracket("after subscript");
                    expr = new SubscriptExpression(expr, sliceOrIndex);
                }
                else if (MatchOp("."))
                {
                    // Attribute access
                    var attr = ConsumeEnhanced(TokenType.NAME, "after '.'");
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
            
            if (!CheckRParen())
            {
                do
                {
                    // Skip NEWLINE, NL, and COMMENT tokens for multi-line function calls (CPython 3.12 compatibility)
                    while (Check(TokenType.NEWLINE) || Check(TokenType.NL) || Check(TokenType.COMMENT))
                    {
                        Advance();
                    }

                    // Check if we've reached the end of arguments after skipping tokens
                    if (CheckRParen())
                    {
                        break;
                    }

                    // **kwargs 처리  
                    if (MatchOp("**"))
                    {
                        var expr = ParseExpression();
                        if (expr != null)
                        {
                            keywords.Add(new KeywordExpression(null, expr)); // **kwargs
                        }
                    }
                    // 키워드 인수 체크: name=value
                    else if (Check(TokenType.NAME) && CheckNextOp("="))
                    {
                        var keywordName = Advance().Lexeme;
                        ConsumeOp("=", "Expected '=' after keyword argument name");
                        var value = ParseConditionalExpression();
                        
                        if (value != null)
                        {
                            keywords.Add(new KeywordExpression(keywordName, value));
                        }
                    }
                    // *args 처리
                    else if (MatchOp("*"))
                    {
                        var expr = ParseConditionalExpression();
                        if (expr != null)
                        {
                            args.Add(new StarredExpression(expr));
                        }
                    }
                    else
                    {
                        // 일반적인 positional 인수 또는 generator expression
                        var arg = ParseFunctionArgument();
                        if (arg != null)
                        {
                            args.Add(arg);
                        }
                    }

                    // Skip NEWLINE, NL, and COMMENT tokens after processing an argument
                    while (Check(TokenType.NEWLINE) || Check(TokenType.NL) || Check(TokenType.COMMENT))
                    {
                        Advance();
                    }

                } while (MatchOp(","));
            }
            
            return (args, keywords);
        }

        // 함수 인수 파싱 - generator expression 지원
        private Expression ParseFunctionArgument()
        {
            // 첫 번째 expression 파싱 - avoid tuple parsing in function arguments
            var firstExpr = ParseConditionalExpression();

            // FOR 키워드가 오면 generator expression
            if (CheckKeyword("for"))
            {
                var generators = ParseComprehensionGenerators();
                return new GeneratorExpression(firstExpr, generators);
            }

            return firstExpr;
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

        /// <summary>
        /// CPython 3.12: Match and consume OP token with specific operator
        /// </summary>
        private bool MatchOp(string op)
        {
            // CPython 3.12: Skip COMMENT tokens before checking operators
            SkipNewlines();

            if (CheckOp(op))
            {
                Advance();
                return true;
            }
            return false;
        }

        /// <summary>
        /// CPython 3.12 compatibility: consume closing bracket (] or RSQB)
        /// </summary>
        private void ConsumeClosingBracket(string message = "Expected ']'")
        {
            if (CheckOp("]"))
            {
                Advance(); // consume OP(])
            }
            else
            {
                throw new Exception($"{message}. Got {Peek().Type}({Peek().Lexeme}) at {Peek().Line}:{Peek().Column}");
            }
        }

        /// <summary>
        /// CPython 3.12 compatibility: consume colon (: or COLON)
        /// </summary>
        private void ConsumeColon(string message = "Expected ':'")
        {
            if (CheckOp(":"))
            {
                Advance(); // consume OP(":")
            }
            else
            {
                throw new Exception($"{message}. Got {Peek().Type}({Peek().Lexeme}) at {Peek().Line}:{Peek().Column}");
            }
        }

        /// <summary>
        /// CPython 3.12: Consume OP token with specific operator string
        /// </summary>
        private PyToken ConsumeOp(string op, string message)
        {
            // CPython 3.12: Skip COMMENT tokens before consuming operators
            SkipNewlines();

            if (CheckOp(op))
            {
                return Advance();
            }
            throw new Exception($"{message} - Expected OP('{op}') but got {Peek()}");
        }

        private bool Check(TokenType type)
        {
            if (IsAtEnd()) return false;
            return Peek().Type == type;
        }

        /// <summary>
        /// CPython 3.12: Check for specific operator in OP tokens (since operators are unified)
        /// </summary>
        private bool CheckOp(string op)
        {
            if (IsAtEnd()) return false;
            var token = Peek();
            return token.Type == TokenType.OP && token.Lexeme == op;
        }

        /// <summary>
        /// CPython 3.12: Check for semicolon in OP tokens
        /// </summary>
        private bool CheckSemicolon() => CheckOp(";");

        /// <summary>
        /// CPython 3.12: Check for assignment operator in OP tokens
        /// </summary>
        private bool CheckEquals() => CheckOp("=");

        /// <summary>
        /// CPython 3.12: Check for left parenthesis in OP tokens
        /// </summary>
        private bool CheckLParen() => CheckOp("(");

        /// <summary>
        /// CPython 3.12: Check for right parenthesis in OP tokens
        /// </summary>
        private bool CheckRParen() => CheckOp(")");

        /// <summary>
        /// CPython 3.12: Check for comma in OP tokens
        /// </summary>
        private bool CheckComma() => CheckOp(",");

        /// <summary>
        /// CPython 3.12: Check for colon in OP tokens
        /// </summary>
        private bool CheckColon() => CheckOp(":");

        /// <summary>
        /// CPython 3.12: Check for dot in OP tokens
        /// </summary>
        private bool CheckDot() => CheckOp(".");

        /// <summary>
        /// CPython 3.12: Enhanced consume for OP tokens with context
        /// </summary>
        private PyToken ConsumeEnhancedOp(string op, string context)
        {
            if (!CheckOp(op))
            {
                throw PySyntaxError.Create($"Expected '{op}' {context}", "", Peek().Line);
            }
            return Advance();
        }

        /// <summary>
        /// CPython 3.12: Check next token for specific OP
        /// </summary>
        private bool CheckNextOp(string op)
        {
            if (_current + 1 >= _tokens.Count) return false;
            var nextToken = _tokens[_current + 1];
            return nextToken.Type == TokenType.OP && nextToken.Lexeme == op;
        }

        /// <summary>
        /// CPython-style compound operator detection with proper lookahead
        /// Based on CPython's Grammar/python.gram PEG parser approach
        /// </summary>
        private bool IsNotInCompoundOperator(int startPos)
        {
            if (startPos >= _tokens.Count - 1) return false;
            
            if (!(_tokens[startPos].Type == TokenType.NAME && _tokens[startPos].Lexeme == "not")) return false;
            
            // CPython approach: check immediate next token for 'in'
            int nextPos = startPos + 1;
            
            // In CPython, whitespace is handled at tokenizer level
            // Here we check the direct next token
            if (nextPos >= _tokens.Count) return false;
            
            return _tokens[nextPos].Type == TokenType.NAME && _tokens[nextPos].Lexeme == "in";
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
            
            return _tokens[_current].Type == TokenType.NAME && _tokens[_current].Lexeme == "is" &&
                   _tokens[_current + 1].Type == TokenType.NAME && _tokens[_current + 1].Lexeme == "not";
        }

        private PyToken Advance()
        {
            if (!IsAtEnd()) _current++;
            return Previous();
        }

        private bool IsAtEnd()
        {
            return Peek().Type == TokenType.ENDMARKER;
        }

        private PyToken Peek()
        {
            return _tokens[_current];
        }

        private PyToken Previous()
        {
            return _tokens[_current - 1];
        }
        
        /// <summary>
        /// Set source location information on AST node from current token
        /// </summary>
        private void SetSourceLocation(ASTNode node)
        {
            var currentToken = Peek();
            node.LineNo = currentToken.Line;
            node.ColOffset = currentToken.Column;
        }
        
        /// <summary>
        /// Set source location information on AST node from specific token
        /// </summary>
        private void SetSourceLocation(ASTNode node, PyToken token)
        {
            node.LineNo = token.Line;
            node.ColOffset = token.Column;
        }
        
        // CPython 3.12-style error reporting
        private Exception CreateSyntaxError(string message)
        {
            var token = Peek();
            var errorMessage = FormatSyntaxError(message, token);
            return new PySyntaxErrorException(errorMessage);
        }
        
        private string FormatSyntaxError(string message, PyToken token)
        {
            var lines = _sourceCode.Split('\n');
            var lineNumber = token.Line;
            var columnNumber = token.Column;
            
            var sb = new StringBuilder();
            
            // File and line info: File "filename", line N
            sb.AppendLine($"  File \"{_filename}\", line {lineNumber}");
            
            // Source code line (if available)
            if (lines.Length >= lineNumber && lineNumber > 0)
            {
                var sourceLine = lines[lineNumber - 1]; // 0-based indexing
                sb.AppendLine($"    {sourceLine}");
                
                // Pointer to error position
                if (columnNumber > 0)
                {
                    var spaces = new string(' ', Math.Max(0, columnNumber - 1 + 4)); // +4 for "    " prefix
                    var pointerLength = Math.Max(1, token.Lexeme.Length);
                    var pointer = new string('^', pointerLength);
                    sb.AppendLine($"{spaces}{pointer}");
                }
            }
            
            // Error message
            sb.Append($"SyntaxError: {message}");
            
            return sb.ToString();
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
            // CPython 3.12: Skip NEWLINE, NL, and COMMENT tokens for multi-line constructs
            while (Match(TokenType.NEWLINE) || Match(TokenType.NL) || Match(TokenType.COMMENT)) { }
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
                    #if DEBUG_LOG
                    Console.WriteLine("⚠️ Warning: Skipped too many INDENT tokens - possible infinite loop prevented");
                    #endif
                }
            }
        }

        /// <summary>
        /// CPython 3.12: Skip comment tokens
        /// </summary>
        private void SkipCommentTokens()
        {
            while (Check(TokenType.COMMENT))
            {
                Advance(); // consume COMMENT
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
                    #if DEBUG_LOG
                    Console.WriteLine("⚠️ Warning: Skipped too many DEDENT tokens - possible infinite loop prevented");
                    #endif
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
            if (!(Check(TokenType.NAME) && Peek().Lexeme == "match"))
            {
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
                    
                    // Track nested structures - CPython 3.12 OP tokens
                    if (token.Type == TokenType.OP && token.Lexeme == "(") parenDepth++;
                    else if (token.Type == TokenType.OP && token.Lexeme == ")") parenDepth--;
                    else if (token.Type == TokenType.OP && token.Lexeme == "[") bracketDepth++;
                    else if (token.Type == TokenType.OP && token.Lexeme == "]") bracketDepth--;
                    else if (token.Type == TokenType.OP && token.Lexeme == "{") braceDepth++;
                    else if (token.Type == TokenType.OP && token.Lexeme == "}") braceDepth--;
                    
                    // If we're at top level and find colon, it's a match statement
                    if (parenDepth == 0 && bracketDepth == 0 && braceDepth == 0)
                    {
                        if (token.Type == TokenType.OP && token.Lexeme == ":")
                        {
                            return true; // match expr: found
                        }
                        if (token.Type == TokenType.NEWLINE || (token.Type == TokenType.OP && token.Lexeme == "="))
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

        /// <summary>
        /// Create chained assignment statement: a = b = c + d
        /// Generates: a = value, b = value (right-to-left evaluation)
        /// </summary>
        private Statement CreateChainedAssignment(List<Expression> targets, Expression value)
        {
            if (targets.Count == 1)
            {
                // Single assignment - use existing logic
                var target = targets[0];
                if (target is NameExpression nameExpr)
                {
                    return new AssignStatement(nameExpr.Name, value);
                }
                return new AssignTargetStatement(target, value);
            }
            
            // Multiple targets - create chained assignment
            // CPython 3.12 uses COPY instruction for chained assignments
            return new ChainedAssignStatement(targets, value);
        }

        private void Synchronize()
        {
            Advance();
            
            while (!IsAtEnd())
            {
                if (Previous().Type == TokenType.NEWLINE) return;
                
                switch (Peek().Type)
                {
                    case TokenType.NAME when Peek().Lexeme == "class":
                    case TokenType.NAME when Peek().Lexeme == "def":
                    case TokenType.NAME when Peek().Lexeme == "if":
                    case TokenType.NAME when Peek().Lexeme == "while":
                    case TokenType.NAME when Peek().Lexeme == "for":
                    case TokenType.NAME when Peek().Lexeme == "return":
                        return;
                }
                
                Advance();
            }
        }

        /// <summary>
        /// Parse type annotation with proper termination conditions
        /// Stops at: ',', ')', '=', '->', ':' tokens
        /// </summary>
        private Expression? ParseTypeAnnotation()
        {
            // Simple type annotation parser that stops at parameter delimiters
            if (Check(TokenType.NAME))
            {
                var typeName = Advance().Lexeme;
                return new NameExpression(typeName);
            }

            return null;
        }

        /// <summary>
        /// CPython 3.12 호환: 중첩된 표현식을 포함한 포맷 지시자 파싱
        /// f"{value:{width}.{precision}f}" → 여러 표현식으로 구성된 포맷 지시자
        /// </summary>
        private Expression ParseFormatSpecifier()
        {
            var formatParts = new List<Expression>();

            // 포맷 지시자의 끝('}')까지 파싱
            while (!Check(TokenType.OP) || Peek().Lexeme != "}")
            {
                if (Check(TokenType.FSTRING_MIDDLE))
                {
                    // 리터럴 텍스트 부분 (예: '.', 'f')
                    var literal = Advance().Lexeme;
                    formatParts.Add(new ConstantExpression(new PyString(literal)));
                }
                else if (Check(TokenType.OP) && Peek().Lexeme == "{")
                {
                    // 중첩된 표현식 (예: {width}, {precision})
                    Advance(); // consume '{'
                    var nestedExpr = ParseExpression();

                    // Consume closing '}'
                    if (Check(TokenType.OP) && Peek().Lexeme == "}")
                    {
                        Advance();
                    }

                    formatParts.Add(nestedExpr);
                }
                else if (Check(TokenType.NAME) || Check(TokenType.NUMBER))
                {
                    // 직접적인 이름이나 숫자 (토큰화 단계에서 이미 분리된 경우)
                    var token = Advance();
                    if (token.Type == TokenType.NAME)
                    {
                        formatParts.Add(new NameExpression(token.Lexeme));
                    }
                    else if (token.Type == TokenType.NUMBER)
                    {
                        // 숫자 파싱
                        if (token.Lexeme.Contains('.'))
                        {
                            var value = double.Parse(token.Lexeme);
                            formatParts.Add(new ConstantExpression(new PyFloat(value, token.Lexeme)));
                        }
                        else
                        {
                            var value = int.Parse(token.Lexeme);
                            formatParts.Add(new ConstantExpression(new PyInt(value)));
                        }
                    }
                }
                else
                {
                    // 예상치 못한 토큰 - 건너뛰기 (방어적 프로그래밍)
                    Advance();
                }
            }

            // 단일 표현식이면 그대로 반환, 여러 개면 FStringExpression으로 래핑
            if (formatParts.Count == 1)
            {
                return formatParts[0];
            }
            else if (formatParts.Count > 1)
            {
                return new FStringExpression(formatParts);
            }
            else
            {
                // 빈 포맷 지시자
                return new ConstantExpression(new PyString(""));
            }
        }
    }

    /// <summary>
    /// 이전 SimpleParser와의 호환성을 위한 래퍼
    /// </summary>
    public class SimpleParser
    {
        public List<Statement> Parse(string source, string filename = "<string>")
        {
            return PyParser.ParseSource(source, filename);
        }
    }

    #endregion
}