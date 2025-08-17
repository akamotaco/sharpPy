// enhanced_lexer_parser.cs
using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.CompilerServices;

namespace SharpPy
{
    // Optimized Lexer with improved performance
    public sealed class Lexer
    {
        private readonly string input;
        private readonly int inputLength;
        private int position;
        private char currentChar;
        private int line;
        private int column;
        private readonly Stack<int> indentStack;
        private bool atLineStart;
        private int pendingIndent = -1;  // 대기 중인 들여쓰기 레벨
        
        // 탭 크기 설정 (Python 표준은 8)
        private const int TAB_SIZE = 8;
        
        // Pre-computed lookup tables for better performance
        private static readonly HashSet<string> Keywords = new HashSet<string>
        {
            "def", "class", "if", "else", "elif", "for", "while", "in", "is",
            "break", "continue", "try", "except", "finally", "raise", "import",
            "from", "as", "return", "and", "or", "not", "lambda", "with", "del"
        };
        
        private static readonly HashSet<string> TwoCharOperators = new HashSet<string>
        {
            "+=", "-=", "*=", "/=", "%=", "==", "!=", "<=", ">=", "**", "->"
        };

        public Lexer(string input)
        {
            this.input = input ?? "";
            this.inputLength = this.input.Length;
            position = 0;
            line = 1;
            column = 1;
            indentStack = new Stack<int>();
            indentStack.Push(0);
            atLineStart = true;
            currentChar = position < inputLength ? this.input[position] : '\0';
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Advance()
        {
            if (currentChar == '\n')
            {
                line++;
                column = 1;
                atLineStart = true;
            }
            else
            {
                column++;
            }

            position++;
            currentChar = position < inputLength ? input[position] : '\0';
        }

        private string ReadNumber()
        {
            var sb = new StringBuilder(16); // Pre-allocate reasonable size
            bool hasDot = false;

            while (currentChar != '\0' && (char.IsDigit(currentChar) || (currentChar == '.' && !hasDot)))
            {
                if (currentChar == '.') hasDot = true;
                sb.Append(currentChar);
                Advance();
            }
            return sb.ToString();
        }

        private string ReadString(char quote)
        {
            var sb = new StringBuilder();
            int startLine = line;
            int startColumn = column;
            Advance(); // Skip opening quote

            while (currentChar != '\0' && currentChar != quote)
            {
                if (currentChar == '\\')
                {
                    Advance();
                    sb.Append(currentChar switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        'r' => '\r',
                        '\\' => '\\',
                        '\'' => '\'',
                        '"' => '"',
                        '0' => '\0',
                        _ => currentChar
                    });
                }
                else
                {
                    sb.Append(currentChar);
                }
                Advance();
            }

            if (currentChar == quote)
                Advance();
            else
                throw new PythonException("SyntaxError", "Unterminated string literal", startLine, startColumn);

            return sb.ToString();
        }

        private string ReadTripleQuotedString(string quoteType)
        {
            var sb = new StringBuilder();
            int startLine = line;
            int startColumn = column;
            
            // Skip the opening triple quotes
            for (int i = 0; i < 3; i++)
                Advance();

            // Read until we find the closing triple quotes
            while (position + 2 < inputLength)
            {
                if (currentChar == quoteType[0] && 
                    position + 1 < inputLength && input[position + 1] == quoteType[0] &&
                    position + 2 < inputLength && input[position + 2] == quoteType[0])
                {
                    for (int i = 0; i < 3; i++)
                        Advance();
                    return sb.ToString();
                }
                
                sb.Append(currentChar);
                Advance();
            }

            while (currentChar != '\0')
            {
                if (currentChar == quoteType[0] && 
                    position + 1 < inputLength && input[position + 1] == quoteType[0] &&
                    position + 2 < inputLength && input[position + 2] == quoteType[0])
                {
                    for (int i = 0; i < 3; i++)
                        Advance();
                    return sb.ToString();
                }
                sb.Append(currentChar);
                Advance();
            }

            throw new PythonException("SyntaxError", "Unterminated triple-quoted string literal", startLine, startColumn);
        }

        private string ReadFString(char quote)
        {
            var sb = new StringBuilder();
            int startLine = line;
            int startColumn = column;
            Advance(); // Skip opening quote

            while (currentChar != '\0' && currentChar != quote)
            {
                if (currentChar == '\\')
                {
                    Advance();
                    sb.Append(currentChar switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        'r' => '\r',
                        '\\' => '\\',
                        '\'' => '\'',
                        '"' => '"',
                        '0' => '\0',
                        _ => currentChar
                    });
                }
                else
                {
                    sb.Append(currentChar);
                }
                Advance();
            }

            if (currentChar == quote)
                Advance();
            else
                throw new PythonException("SyntaxError", "Unterminated f-string literal", startLine, startColumn);

            return sb.ToString();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private string ReadIdentifier()
        {
            var sb = new StringBuilder();
            while (currentChar != '\0' && (char.IsLetterOrDigit(currentChar) || currentChar == '_'))
            {
                sb.Append(currentChar);
                Advance();
            }
            return sb.ToString();
        }

        // 줄이 비어있는지 확인 (공백, 탭, 주석만 있는 경우)
        private bool IsBlankLine()
        {
            int tempPos = position;
            char tempChar = currentChar;

            // 현재 위치부터 줄 끝까지 확인
            while (tempChar != '\0' && tempChar != '\n')
            {
                if (tempChar == '#')
                    return true;  // 주석이면 빈 줄로 취급
                if (tempChar != ' ' && tempChar != '\t')
                    return false;  // 공백이 아닌 문자가 있으면 빈 줄이 아님

                tempPos++;
                tempChar = tempPos < inputLength ? input[tempPos] : '\0';
            }

            return true;  // 줄 끝까지 공백만 있음
        }

        // 들여쓰기 레벨 계산 (탭과 공백 혼용 처리)
        private int CalculateIndentLevel()
        {
            int level = 0;
            int tempPos = position;
            char tempChar = currentChar;
            
            while (tempChar == ' ' || tempChar == '\t')
            {
                if (tempChar == ' ')
                    level += 1;
                else if (tempChar == '\t')
                    level = ((level / TAB_SIZE) + 1) * TAB_SIZE;  // 다음 탭 위치로 이동
                
                tempPos++;
                tempChar = tempPos < inputLength ? input[tempPos] : '\0';
            }
            
            return level;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SkipWhitespace()
        {
            while (currentChar != '\0' && char.IsWhiteSpace(currentChar) && currentChar != '\n')
                Advance();
        }

        public List<Token> Tokenize()
        {
            var tokens = new List<Token>(256); // Pre-allocate reasonable capacity
            bool previousWasNewline = true;  // 파일 시작은 새 줄로 취급

            while (currentChar != '\0')
            {
                int tokenLine = line;
                int tokenColumn = column;

                // 줄의 시작에서 들여쓰기 처리
                if (atLineStart && currentChar != '\n')
                {
                    // 빈 줄인지 확인
                    if (IsBlankLine())
                    {
                        // 빈 줄은 건너뛰기 (들여쓰기 변경 없음)
                        while (currentChar != '\0' && currentChar != '\n')
                        {
                            if (currentChar == '#')
                            {
                                // 주석 끝까지 건너뛰기
                                while (currentChar != '\0' && currentChar != '\n')
                                    Advance();
                            }
                            else
                            {
                                Advance();
                            }
                        }
                        
                        if (currentChar == '\n')
                        {
                            Advance();
                        }
                        continue;
                    }

                    // 실제 코드가 있는 줄의 들여쓰기 처리
                    int indentLevel = CalculateIndentLevel();
                    
                    // 들여쓰기 공백 건너뛰기
                    while (currentChar == ' ' || currentChar == '\t')
                        Advance();

                    // 들여쓰기 토큰 생성
                    int currentIndent = indentStack.Peek();
                    if (indentLevel > currentIndent)
                    {
                        indentStack.Push(indentLevel);
                        tokens.Add(new Token(TokenType.INDENT, "", tokenLine, tokenColumn));
                    }
                    else if (indentLevel < currentIndent)
                    {
                        while (indentStack.Count > 1 && indentStack.Peek() > indentLevel)
                        {
                            indentStack.Pop();
                            tokens.Add(new Token(TokenType.DEDENT, "", tokenLine, tokenColumn));
                        }

                        if (indentStack.Peek() != indentLevel)
                        {
                            throw new PythonException("IndentationError", 
                                $"Unindent does not match any outer indentation level", 
                                tokenLine, tokenColumn);
                        }
                    }

                    atLineStart = false;
                    previousWasNewline = false;
                }

                // 줄 중간의 공백 처리
                if (!atLineStart && char.IsWhiteSpace(currentChar) && currentChar != '\n')
                {
                    SkipWhitespace();
                    continue;
                }

                // 주석 처리
                if (currentChar == '#')
                {
                    while (currentChar != '\0' && currentChar != '\n')
                        Advance();
                    continue;
                }

                // 개행 문자 처리
                if (currentChar == '\n')
                {
                    // 연속된 개행은 하나만 토큰으로 만들기
                    if (!previousWasNewline)
                    {
                        tokens.Add(new Token(TokenType.NEWLINE, "\n", tokenLine, tokenColumn));
                        previousWasNewline = true;
                    }
                    Advance();
                    continue;
                }

                previousWasNewline = false;

                // 숫자 처리
                if (char.IsDigit(currentChar))
                {
                    tokens.Add(new Token(TokenType.NUMBER, ReadNumber(), tokenLine, tokenColumn));
                    continue;
                }

                // Check for triple-quoted strings first
                if (position + 2 < inputLength)
                {
                    string threeChars = input.Substring(position, 3);
                    if (threeChars == "'''" || threeChars == "\"\"\"")
                    {
                        string content = ReadTripleQuotedString(threeChars.Substring(0, 1));
                        tokens.Add(new Token(TokenType.STRING, content, tokenLine, tokenColumn));
                        continue;
                    }
                }

                // Check for f-strings
                if (currentChar == 'f' && position + 1 < inputLength && 
                    (input[position + 1] == '"' || input[position + 1] == '\''))
                {
                    // Check if it's a triple-quoted f-string
                    if (position + 3 < inputLength)
                    {
                        char quoteChar = input[position + 1];
                        if (input[position + 2] == quoteChar && input[position + 3] == quoteChar)
                        {
                            Advance(); // Skip 'f'
                            string content = ReadTripleQuotedString(quoteChar.ToString());
                            tokens.Add(new Token(TokenType.FSTRING, content, tokenLine, tokenColumn));
                            continue;
                        }
                    }
                    
                    Advance(); // Skip 'f'
                    char quote = currentChar;
                    tokens.Add(new Token(TokenType.FSTRING, ReadFString(quote), tokenLine, tokenColumn));
                    continue;
                }

                // 문자열 처리
                if (currentChar == '"' || currentChar == '\'')
                {
                    char quote = currentChar;
                    tokens.Add(new Token(TokenType.STRING, ReadString(quote), tokenLine, tokenColumn));
                    continue;
                }

                // 식별자 및 키워드 처리
                if (char.IsLetter(currentChar) || currentChar == '_')
                {
                    string identifier = ReadIdentifier();
                    
                    // Optimized keyword lookup
                    TokenType tokenType = identifier switch
                    {
                        "True" or "False" => TokenType.BOOLEAN,
                        "None" => TokenType.NONE,
                        _ when Keywords.Contains(identifier) => GetKeywordToken(identifier),
                        _ => TokenType.IDENTIFIER
                    };
                    
                    tokens.Add(new Token(tokenType, identifier, tokenLine, tokenColumn));
                    continue;
                }

                // Check for compound assignment operators first
                if (position + 1 < inputLength)
                {
                    // Check for **=
                    if (position + 2 < inputLength && input.Substring(position, 3) == "**=")
                    {
                        tokens.Add(new Token(TokenType.COMPOUND_ASSIGN, "**=", tokenLine, tokenColumn));
                        Advance();
                        Advance();
                        Advance();
                        continue;
                    }

                    string twoChar = input.Substring(position, 2);
                    
                    if (TwoCharOperators.Contains(twoChar))
                    {
                        tokens.Add(new Token(
                            twoChar.Contains('=') && twoChar != "==" && twoChar != "!=" && twoChar != "<=" && twoChar != ">=" 
                                ? TokenType.COMPOUND_ASSIGN 
                                : TokenType.OPERATOR, 
                            twoChar, tokenLine, tokenColumn));
                        Advance();
                        Advance();
                        continue;
                    }
                }

                // Single-character tokens
                var tokenInfo = currentChar switch
                {
                    '+' or '-' or '*' or '/' or '%' or '<' or '>' => (TokenType.OPERATOR, currentChar.ToString()),
                    '=' => (TokenType.ASSIGN, "="),
                    '(' => (TokenType.LPAREN, "("),
                    ')' => (TokenType.RPAREN, ")"),
                    '[' => (TokenType.LBRACKET, "["),
                    ']' => (TokenType.RBRACKET, "]"),
                    '{' => (TokenType.LBRACE, "{"),
                    '}' => (TokenType.RBRACE, "}"),
                    ':' => (TokenType.COLON, ":"),
                    ',' => (TokenType.COMMA, ","),
                    '.' => (TokenType.DOT, "."),
                    _ => throw new PythonException("SyntaxError", $"Unexpected character: {currentChar}", tokenLine, tokenColumn)
                };
                
                tokens.Add(new Token(tokenInfo.Item1, tokenInfo.Item2, tokenLine, tokenColumn));
                Advance();
            }

            // Add DEDENT tokens for remaining indentation levels
            while (indentStack.Count > 1)
            {
                indentStack.Pop();
                tokens.Add(new Token(TokenType.DEDENT, "", line, column));
            }

            tokens.Add(new Token(TokenType.EOF, "", line, column));
            return tokens;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static TokenType GetKeywordToken(string keyword) => keyword switch
        {
            "def" => TokenType.DEF,
            "class" => TokenType.CLASS,
            "if" => TokenType.IF,
            "else" => TokenType.ELSE,
            "elif" => TokenType.ELIF,
            "for" => TokenType.FOR,
            "while" => TokenType.WHILE,
            "in" => TokenType.IN,
            "is" => TokenType.IS,
            "break" => TokenType.BREAK,
            "continue" => TokenType.CONTINUE,
            "try" => TokenType.TRY,
            "except" => TokenType.EXCEPT,
            "finally" => TokenType.FINALLY,
            "raise" => TokenType.RAISE,
            "import" => TokenType.IMPORT,
            "from" => TokenType.FROM,
            "as" => TokenType.AS,
            "return" => TokenType.RETURN,
            "and" => TokenType.AND,
            "or" => TokenType.OR,
            "not" => TokenType.NOT,
            "lambda" => TokenType.LAMBDA,
            "with" => TokenType.WITH,
            "del" => TokenType.DEL,
            _ => TokenType.IDENTIFIER
        };
    }

    // Optimized Parser with improved performance
    public partial class Parser
    {
        private readonly List<Token> tokens;
        private readonly int tokenCount;
        private int position;
        private Token currentToken;
        
        // Pre-computed sets for faster lookups
        private static readonly HashSet<TokenType> BlockStatementTokens = new HashSet<TokenType>
        {
            TokenType.DEF, TokenType.CLASS, TokenType.IDENTIFIER, TokenType.NUMBER,
            TokenType.STRING, TokenType.FSTRING, TokenType.BOOLEAN, TokenType.NONE,
            TokenType.LBRACKET, TokenType.LBRACE, TokenType.LPAREN, TokenType.IF,
            TokenType.FOR, TokenType.WHILE, TokenType.TRY, TokenType.WITH,
            TokenType.RETURN, TokenType.BREAK, TokenType.CONTINUE, TokenType.RAISE,
            TokenType.IMPORT, TokenType.FROM, TokenType.NOT
        };
        
        private static readonly HashSet<TokenType> EndOfStatementTokens = new HashSet<TokenType>
        {
            TokenType.NEWLINE, TokenType.EOF, TokenType.RBRACE, TokenType.RBRACKET,
            TokenType.RPAREN, TokenType.COLON, TokenType.ELSE, TokenType.ELIF,
            TokenType.EXCEPT, TokenType.FINALLY
        };
        
        private static readonly HashSet<string> ComparisonOperators = new HashSet<string>
        {
            "==", "!=", "<", ">", "<=", ">="
        };
        
        private static readonly HashSet<string> ArithmeticOperators = new HashSet<string>
        {
            "+", "-"
        };
        
        private static readonly HashSet<string> TermOperators = new HashSet<string>
        {
            "*", "/", "%"
        };

        public Parser(List<Token> tokens)
        {
            this.tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            this.tokenCount = this.tokens.Count;
            position = 0;
            currentToken = tokenCount > 0 ? tokens[0] : new Token(TokenType.EOF, "", 1, 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Advance()
        {
            position++;
            currentToken = position < tokenCount ? tokens[position] : new Token(TokenType.EOF, "", currentToken?.Line ?? 1, currentToken?.Column ?? 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SkipNewlines()
        {
            while (currentToken.Type == TokenType.NEWLINE)
                Advance();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SkipNewlinesAndIndents()
        {
            while (currentToken.Type == TokenType.NEWLINE ||
                   currentToken.Type == TokenType.INDENT ||
                   currentToken.Type == TokenType.DEDENT)
                Advance();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Expect(TokenType tokenType)
        {
            if (currentToken.Type != tokenType)
                throw new PythonException("SyntaxError", $"Expected {tokenType}, got {currentToken.Type}", currentToken.Line, currentToken.Column);
            Advance();
        }

        public List<ASTNode> Parse()
        {
            var statements = new List<ASTNode>(32); // Pre-allocate reasonable capacity
            SkipNewlines();

            while (currentToken.Type != TokenType.EOF)
            {
                if (currentToken.Type == TokenType.NEWLINE)
                {
                    Advance();
                    continue;
                }

                try
                {
                    var statement = ParseStatement();
                    statements.Add(statement);
                    SkipNewlines();
                }
                catch (PythonException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new PythonException("SyntaxError", $"Parse error: {ex.Message}", currentToken.Line, currentToken.Column);
                }
            }

            return statements;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ASTNode ParseStatement() => currentToken.Type switch
        {
            TokenType.DEF => ParseFunctionDef(),
            TokenType.CLASS => ParseClassDef(),
            TokenType.IF => ParseIf(),
            TokenType.FOR => ParseFor(),
            TokenType.WHILE => ParseWhile(),
            TokenType.TRY => ParseTry(),
            TokenType.WITH => ParseWith(),
            TokenType.DEL => ParseDel(),
            TokenType.IMPORT or TokenType.FROM => ParseImport(),
            TokenType.RETURN => ParseReturn(),
            TokenType.BREAK => ParseBreak(),
            TokenType.CONTINUE => ParseContinue(),
            TokenType.RAISE => ParseRaise(),
            _ => ParseExpressionStatement()
        };

        private ASTNode ParseFunctionDef()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;
            
            Expect(TokenType.DEF);
            string name = currentToken.Value;
            Expect(TokenType.IDENTIFIER);
            Expect(TokenType.LPAREN);

            SkipNewlinesAndIndents();

            var parameters = new List<Parameter>();
            bool hasSeenDefault = false;  // 기본값이 있는 매개변수를 봤는지 추적
            
            while (currentToken.Type != TokenType.RPAREN)
            {
                string paramName = currentToken.Value;
                Expect(TokenType.IDENTIFIER);

                TypeHint typeHint = null;
                if (currentToken.Type == TokenType.COLON)
                {
                    Advance();
                    typeHint = ParseTypeHint();
                }

                ASTNode defaultValue = null;
                if (currentToken.Type == TokenType.ASSIGN)
                {
                    Advance(); // Skip '='
                    defaultValue = ParseExpression();  // 기본값 표현식 파싱
                    hasSeenDefault = true;
                }
                else if (hasSeenDefault)
                {
                    // 기본값이 있는 매개변수 다음에 기본값이 없는 매개변수가 오면 에러
                    throw new PythonException("SyntaxError", 
                        "non-default argument follows default argument", 
                        currentToken.Line, currentToken.Column);
                }

                parameters.Add(new Parameter(paramName, typeHint, defaultValue));

                SkipNewlinesAndIndents();

                if (currentToken.Type == TokenType.COMMA)
                {
                    Advance();
                    SkipNewlinesAndIndents();

                    if (currentToken.Type == TokenType.RPAREN)
                        break;
                }
                else if (currentToken.Type != TokenType.RPAREN)
                {
                    throw new PythonException("SyntaxError", 
                        "Expected ',' or ')' in parameter list", 
                        currentToken.Line, currentToken.Column);
                }
            }
            
            Expect(TokenType.RPAREN);

            TypeHint returnTypeHint = null;
            if (currentToken.Type == TokenType.OPERATOR && currentToken.Value == "->")
            {
                Advance();
                returnTypeHint = ParseTypeHint();
            }

            Expect(TokenType.COLON);
            SkipNewlines();

            var body = ParseBlock();

            return new FunctionDefNode(name, parameters, body, returnTypeHint, line, column);
        }

        private TypeHint ParseTypeHint()
        {
            // 문자열 타입 힌트 처리 (클래스 forward reference)
            if (currentToken.Type == TokenType.STRING)
            {
                string className = currentToken.Value;
                Advance();
                return new ClassTypeHint(className);
            }
            
            if (currentToken.Type != TokenType.IDENTIFIER)
                throw new PythonException("SyntaxError", "Expected type hint", currentToken.Line, currentToken.Column);

            string typeName = currentToken.Value;
            Advance();

            // Optional 처리
            if (typeName == "Optional")
            {
                if (currentToken.Type == TokenType.LBRACKET)
                {
                    Advance();
                    var innerType = ParseTypeHint();
                    Expect(TokenType.RBRACKET);
                    
                    // Optional[T] = Union[T, None]
                    return new UnionTypeHint(new List<TypeHint> 
                    { 
                        innerType, 
                        SimpleTypeHint.Create(PythonType.None) 
                    });
                }
            }
            
            // Union 처리
            if (typeName == "Union")
            {
                if (currentToken.Type == TokenType.LBRACKET)
                {
                    Advance();
                    var types = new List<TypeHint>();
                    
                    do
                    {
                        types.Add(ParseTypeHint());
                        if (currentToken.Type == TokenType.COMMA)
                        {
                            Advance();
                            SkipNewlinesAndIndents();
                        }
                        else
                        {
                            break;
                        }
                    } while (currentToken.Type != TokenType.RBRACKET);
                    
                    Expect(TokenType.RBRACKET);
                    return new UnionTypeHint(types);
                }
            }

            // 기본 타입들
            var pythonType = typeName switch
            {
                "int" => PythonType.Int,
                "float" => PythonType.Float,
                "str" => PythonType.String,
                "bool" => PythonType.Boolean,
                "list" => PythonType.List,
                "dict" => PythonType.Dict,
                "tuple" => PythonType.Tuple,
                "None" => PythonType.None,
                _ => PythonType.Instance  // 클래스 이름일 가능성
            };

            // 클래스 이름인 경우
            if (pythonType == PythonType.Instance && typeName != "Any")
            {
                // Generic 처리 전에 클래스 타입 힌트로 처리
                if (currentToken.Type != TokenType.LBRACKET)
                {
                    return new ClassTypeHint(typeName);
                }
            }

            // Generic 타입 처리 (List[int] 등)
            if (currentToken.Type == TokenType.LBRACKET)
            {
                Advance();
                SkipNewlinesAndIndents();

                var genericArgs = new List<TypeHint>();

                while (currentToken.Type != TokenType.RBRACKET)
                {
                    genericArgs.Add(ParseTypeHint());
                    SkipNewlinesAndIndents();

                    if (currentToken.Type == TokenType.COMMA)
                    {
                        Advance();
                        SkipNewlinesAndIndents();

                        if (currentToken.Type == TokenType.RBRACKET)
                            break;
                    }
                    else if (currentToken.Type != TokenType.RBRACKET)
                    {
                        throw new PythonException("SyntaxError", "Expected ',' or ']' in generic type", currentToken.Line, currentToken.Column);
                    }
                }

                Expect(TokenType.RBRACKET);
                return new GenericTypeHint(pythonType, genericArgs);
            }

            // Any 타입 처리
            if (typeName == "Any")
            {
                return new AnyTypeHint();
            }

            return SimpleTypeHint.Create(pythonType);
        }

        private List<ASTNode> ParseBlock()
        {
            var statements = new List<ASTNode>();

            if (currentToken.Type == TokenType.INDENT)
            {
                Advance();
                SkipNewlines();

                while (currentToken.Type != TokenType.DEDENT && currentToken.Type != TokenType.EOF)
                {
                    if (currentToken.Type == TokenType.NEWLINE)
                    {
                        Advance();
                        continue;
                    }

                    if (IsBlockStatement())
                    {
                        statements.Add(ParseStatement());
                        SkipNewlines();
                    }
                    else
                    {
                        break;
                    }
                }

                if (currentToken.Type == TokenType.DEDENT)
                {
                    Advance();
                }
            }
            else
            {
                throw new PythonException("IndentationError", "expected an indented block", currentToken.Line, currentToken.Column);
            }

            return statements;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsBlockStatement() => BlockStatementTokens.Contains(currentToken.Type);
    }
}

// enhanced_lexer_parser.cs