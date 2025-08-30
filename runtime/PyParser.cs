using System;
using System.Collections.Generic;
using System.Linq;

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
            
            Console.WriteLine($"\n📝 파싱: {_tokens.Count}개 토큰");
            
            while (!IsAtEnd())
            {
                // Skip newlines at statement level
                if (Check(TokenType.NEWLINE))
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
                        Console.WriteLine($"  → {statement}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ❌ 파싱 에러: {ex.Message}");
                    // Skip to next statement
                    Synchronize();
                }
            }
            
            Console.WriteLine($"✅ 파싱 완료: {statements.Count}개 문장");
            return statements;
        }

        private Statement? ParseStatement()
        {
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
            if (Match(TokenType.MATCH)) return ParseMatchStatement();
            
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
            
            var body = ParseBlockOrSingleStatement();
            
            return new AsyncFunctionDefStatement(name, parameters, body, typeParams);
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
            
            // Base classes
            var bases = new List<Expression>();
            if (Match(TokenType.LEFT_PAREN))
            {
                if (!Check(TokenType.RIGHT_PAREN))
                {
                    do
                    {
                        bases.Add(ParseExpression());
                    } while (Match(TokenType.COMMA));
                }
                Consume(TokenType.RIGHT_PAREN, "Expected ')' after base classes");
            }
            
            Consume(TokenType.COLON, "Expected ':' after class header");
            
            var body = ParseBlockOrSingleStatement();
            
            return new ClassDefStatement(name, bases, body, typeParams);
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
            var typeParams = new List<string>();
            
            do
            {
                string param;
                
                // Handle *Ts (TypeVarTuple) and **P (ParamSpec)
                if (Check(TokenType.STAR_STAR))
                {
                    // **P (ParamSpec)
                    Advance(); // consume **
                    param = "**" + Consume(TokenType.IDENTIFIER, "Expected type parameter name after **").Lexeme;
                }
                else if (Check(TokenType.STAR))
                {
                    // *Ts (TypeVarTuple)
                    Advance(); // consume *
                    param = "*" + Consume(TokenType.IDENTIFIER, "Expected type parameter name after *").Lexeme;
                }
                else
                {
                    // Regular type parameter
                    param = Consume(TokenType.IDENTIFIER, "Expected type parameter name").Lexeme;
                    
                    // Handle type constraints (T: int)
                    if (Check(TokenType.COLON))
                    {
                        Advance(); // consume :
                        // Parse constraint type but ignore for now
                        ParseExpression(); // Just consume the constraint expression
                    }
                }
                
                typeParams.Add(param);
            } while (Match(TokenType.COMMA));
            
            return typeParams;
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
                        parameters.Add("**" + kwargsParam);
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
                        // Regular parameter
                        var param = Consume(TokenType.IDENTIFIER, "Expected parameter name").Lexeme;
                        parameters.Add(param);
                        
                        // Skip type annotation if present
                        if (Match(TokenType.COLON))
                        {
                            // Skip the type annotation
                            ParseExpression(); // Just consume the type, don't use it yet
                        }
                        
                        // Skip default value if present  
                        if (Match(TokenType.EQUAL))
                        {
                            ParseExpression(); // Just consume the default, don't use it yet
                        }
                    }
                } while (Match(TokenType.COMMA));
            }
            
            return parameters;
        }

        private List<Statement> ParseBlockOrSingleStatement()
        {
            var statements = new List<Statement>();
            
            // CPython 방식: INDENT/DEDENT 토큰을 사용한 정확한 블록 파싱
            
            if (!Check(TokenType.NEWLINE))
            {
                // Single statement on the same line
                var stmt = ParseStatement();
                if (stmt != null)
                    statements.Add(stmt);
            }
            else
            {
                // Block of statements (indented)
                SkipNewlines();
                
                // INDENT 토큰을 기대
                if (!Match(TokenType.INDENT))
                {
                    throw new Exception("Expected an indented block");
                }
                
                // DEDENT 토큰이 나올 때까지 문장들을 파싱
                while (!IsAtEnd() && !Check(TokenType.DEDENT) && !Check(TokenType.EOF))
                {
                    // NEWLINE은 건너뛰기
                    if (Check(TokenType.NEWLINE))
                    {
                        Advance();
                        continue;
                    }
                    
                    var stmt = ParseStatement();
                    if (stmt != null)
                    {
                        statements.Add(stmt);
                    }
                    else
                    {
                        break;
                    }
                }
                
                // DEDENT 토큰 소비
                if (Check(TokenType.DEDENT))
                {
                    Advance();
                }
            }
            
            // If no statements parsed, add a pass statement
            if (statements.Count == 0)
                statements.Add(new PassStatement());
            
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
            try
            {
                var expr = ParseExpression();
                
                // Check for assignment
                if (Match(TokenType.EQUAL))
                {
                    if (expr is NameExpression nameExpr)
                    {
                        var value = ParseExpression();
                        return new AssignStatement(nameExpr.Name, value);
                    }
                    throw new Exception("Invalid assignment target");
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
            // Use new binary expression parser for better precedence handling
            var expr = ParseBinaryExpression();
            
            // Check for conditional: expr if condition else alternative  
            if (Check(TokenType.IF))
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
            var expr = ParseNotExpression();
            
            while (Match(TokenType.AND))
            {
                var op = Previous().Lexeme;
                var right = ParseNotExpression();
                expr = new BinaryOpExpression(expr, op, right);
            }
            
            return expr;
        }

        private Expression ParseNotExpression()
        {
            if (Match(TokenType.NOT))
            {
                var op = Previous().Lexeme;
                var expr = ParseNotExpression();
                return new UnaryOpExpression(op, expr);
            }
            
            return ParseComparisonExpression();
        }

        private Expression ParseComparisonExpression()
        {
            var expr = ParseBitwiseOrExpression();
            
            while (Match(TokenType.EQUAL_EQUAL, TokenType.BANG_EQUAL, TokenType.LESS, 
                         TokenType.LESS_EQUAL, TokenType.GREATER, TokenType.GREATER_EQUAL,
                         TokenType.IN, TokenType.IS))
            {
                var op = Previous().Lexeme;
                var right = ParseBitwiseOrExpression();
                expr = new CompareExpression(expr, op, right);
            }
            
            return expr;
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
            if (Match(TokenType.PLUS, TokenType.MINUS, TokenType.TILDE))
            {
                var op = Previous().Lexeme;
                var expr = ParseUnaryExpression();
                return new UnaryOpExpression(op, expr);
            }
            
            if (Match(TokenType.AWAIT))
            {
                var expr = ParseUnaryExpression();
                return new AwaitExpression(expr);
            }
            
            return ParsePowerExpression();
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
            
            // F-String support (PEP 701 enhanced)
            if (Match(TokenType.F_STRING))
            {
                var fstringContent = Previous().Lexeme;
                return ParseFString(fstringContent);
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
                    elements.Add(ParseExpression());
                }
                
                Consume(TokenType.RIGHT_BRACKET, "Expected ']' after list elements");
                return new ListExpression(elements);
            }
        }

        private Expression ParseDictExpression()
        {
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
                        var key = ParseExpression();
                        Consume(TokenType.COLON, "Expected ':' after dictionary key");
                        var value = ParseExpression();
                        items.Add((key, value));
                    }
                    
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
                        elements.Add(ParseExpression());
                    }
                    
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
                ifs.Add(ParseExpression());
            }
            
            return new Comprehension(target, iter, ifs);
        }
        
        /// <summary>
        /// Parse f-string content and extract expressions
        /// Example: "Hello, {name}!" -> ["Hello, ", name, "!"]
        /// </summary>
        private Expression ParseFString(string content)
        {
            var values = new List<Expression>();
            var currentPos = 0;
            
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
                    throw new Exception("Unmatched '{' in f-string");
                }
                
                // Extract and parse expression
                var exprContent = content.Substring(openBrace + 1, closeBrace - openBrace - 1);
                if (!string.IsNullOrEmpty(exprContent))
                {
                    try
                    {
                        // 포맷 지정자 분리 (예: "value:.2f" -> "value", ".2f")
                        string actualExpr = exprContent;
                        string? formatSpec = null;
                        
                        var colonIndex = FindFormatSpecColon(exprContent);
                        if (colonIndex != -1)
                        {
                            actualExpr = exprContent.Substring(0, colonIndex);
                            formatSpec = exprContent.Substring(colonIndex + 1);
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
                            // Regular expression parsing
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
                        
                        // 포맷 지정자가 있으면 FormattedValue 노드 생성
                        if (formatSpec != null)
                        {
                            values.Add(new FormattedValue(expr, formatSpec));
                        }
                        else
                        {
                            values.Add(expr);
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
        /// Find matching closing brace for f-string expression with nested f-string support (PEP 701)
        /// </summary>
        private int FindMatchingBrace(string content, int openPos)
        {
            int braceCount = 1;
            int pos = openPos + 1;
            bool inString = false;
            char stringDelimiter = '\0';
            bool inFString = false;
            
            while (pos < content.Length && braceCount > 0)
            {
                char c = content[pos];
                
                // Handle string literals (including f-strings)
                if (!inString && (c == '\'' || c == '"'))
                {
                    // Check for f-string
                    if (pos > 0 && content[pos - 1] == 'f')
                    {
                        inFString = true;
                    }
                    inString = true;
                    stringDelimiter = c;
                }
                else if (inString && c == stringDelimiter)
                {
                    // Check for escape sequence
                    if (pos > 0 && content[pos - 1] != '\\')
                    {
                        inString = false;
                        inFString = false;
                        stringDelimiter = '\0';
                    }
                }
                else if (!inString)
                {
                    if (c == '{')
                    {
                        braceCount++;
                    }
                    else if (c == '}')
                    {
                        braceCount--;
                    }
                }
                
                pos++;
            }
            
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
            // Parse for target (left side of 'in')
            var target = ParseUnaryOrAtom(); // Use ParseUnaryOrAtom instead of ParseExpression for simple targets
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
            
            // Extract target name from target expression
            string targetName = "";
            if (target is NameExpression nameExpr)
            {
                targetName = nameExpr.Name;
            }
            else
            {
                throw new Exception("For loop target must be a simple variable name");
            }
            
            return new ForStatement(targetName, iterable, body, elseClause);
        }
        private Statement ParseTryStatement()
        {
            Consume(TokenType.COLON, "Expected ':' after try");
            
            // Parse try body
            var tryBody = ParseBlockOrSingleStatement();
            
            // Parse except handlers
            var handlers = new List<ExceptHandler>();
            while (Match(TokenType.EXCEPT))
            {
                handlers.Add(ParseExceptHandler());
            }
            
            if (handlers.Count == 0)
            {
                throw new Exception("try statement must have at least one except clause");
            }
            
            // Parse optional else clause
            List<Statement>? elseBody = null;
            if (Match(TokenType.ELSE))
            {
                Consume(TokenType.COLON, "Expected ':' after else");
                elseBody = ParseBlockOrSingleStatement();
            }
            
            // Parse optional finally clause
            List<Statement>? finallyBody = null;
            if (Match(TokenType.FINALLY))
            {
                Consume(TokenType.COLON, "Expected ':' after finally");
                finallyBody = ParseBlockOrSingleStatement();
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
            var contextExpr = ParseExpression(); // with context_manager
            
            // Check for "as" clause
            if (Check(TokenType.AS))
            {
                Advance(); // consume AS
                var target = ParseExpression(); // as target
            }
            
            Consume(TokenType.COLON, "Expected ':' after with statement");
            
            // Skip body tokens safely
            while (!IsAtEnd() && !Check(TokenType.EOF))
            {
                var token = Peek();
                if (token.Type == TokenType.IF || token.Type == TokenType.WHILE || 
                    token.Type == TokenType.FOR || token.Type == TokenType.CLASS || 
                    token.Type == TokenType.DEF || token.Type == TokenType.TRY ||
                    token.Type == TokenType.WITH || token.Type == TokenType.MATCH)
                {
                    break;
                }
                Advance();
            }
            
            return new ExpressionStatement(contextExpr); // Simplified
        }
        private Statement ParseMatchStatement()
        {
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
                
                if (!Check(TokenType.CASE))
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
            Consume(TokenType.CASE, "Expected 'case'");
            
            // Parse pattern
            var pattern = ParseExpression();
            
            // Parse optional guard (if condition)
            Expression? guard = null;
            if (Match(TokenType.IF))
            {
                guard = ParseExpression();
            }
            
            Consume(TokenType.COLON, "Expected ':' after case pattern");
            
            // Parse case body
            var body = ParseBlockOrSingleStatement();
            
            return new MatchCase(pattern, body, guard);
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
        private Statement ParseAssertStatement() => new PassStatement(); // TODO: Implement
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
        private Statement ParseDeleteStatement() => new PassStatement(); // TODO: Implement
        private Statement ParseGlobalStatement() => new PassStatement(); // TODO: Implement
        private Statement ParseNonlocalStatement() => new PassStatement(); // TODO: Implement
        private Statement ParseImportStatement()
        {
            // Parse comma-separated module list (CPython style)
            var modules = new List<string>();
            
            do
            {
                var moduleName = Consume(TokenType.IDENTIFIER, "Expected module name").Lexeme;
                
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
        private Statement ParseFromImportStatement()
        {
            var moduleName = Consume(TokenType.IDENTIFIER, "Expected module name after 'from'").Lexeme;
            Consume(TokenType.IMPORT, "Expected 'import' after module name");
            
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
            var body = ParseOrExpression(); // Use OrExpression to avoid infinite recursion
            
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
                   type == TokenType.IN || type == TokenType.IS;
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
                    // Function call
                    var args = ParseDelimitedList(() => TryParse(ParseExpression), TokenType.COMMA)
                        .Where(a => a != null).ToList();
                    ConsumeEnhanced(TokenType.RIGHT_PAREN, "after function arguments");
                    expr = new CallExpression(expr, args);
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