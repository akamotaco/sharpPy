using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace SharpPy
{
    #region Lexer (Script -> Tokens)

    /// <summary>
    /// Python 소스 코드를 토큰으로 분해하는 렉서
    /// </summary>
    public class PyLexer
    {
        private readonly string _source;
        private int _position;
        private int _line;
        private int _column;
        
        // 들여쓰기 추적을 위한 스택 (CPython 방식)
        private Stack<int> _indentStack;
        private bool _atLineStart;
        private List<PyToken> _pendingTokens;
        
        public PyLexer(string source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _position = 0;
            _line = 1;
            _column = 1;
            
            // 들여쓰기 추적 초기화
            _indentStack = new Stack<int>();
            _indentStack.Push(0); // 기본 들여쓰기 레벨
            _atLineStart = true;
            _pendingTokens = new List<PyToken>();
        }

        /// <summary>
        /// 소스 코드를 토큰 리스트로 변환
        /// </summary>
        public List<PyToken> Tokenize()
        {
            var tokens = new List<PyToken>();
            
            while (!IsAtEnd())
            {
                // Pending 토큰이 있으면 먼저 처리
                if (_pendingTokens.Count > 0)
                {
                    tokens.Add(_pendingTokens[0]);
                    _pendingTokens.RemoveAt(0);
                    continue;
                }
                
                var token = NextToken();
                if (token != null)
                {
                    tokens.Add(token);
                }
            }
            
            // 파일 끝에서 남은 DEDENT 토큰들 생성
            while (_indentStack.Count > 1)
            {
                _indentStack.Pop();
                tokens.Add(new PyToken(TokenType.DEDENT, "", _line, _column));
            }
            
            // EOF 토큰 추가
            tokens.Add(new PyToken(TokenType.EOF, "", _line, _column));
            
            return tokens;
        }

        private PyToken? NextToken()
        {
            // 줄 시작에서 들여쓰기 처리
            if (_atLineStart)
            {
                _atLineStart = false;
                
                // 빈 줄이나 주석만 있는 줄은 건너뛰기
                if (IsAtEnd() || Peek() == '\n' || Peek() == '#')
                {
                    if (Peek() == '\n')
                    {
                        Advance();
                        _line++;
                        _column = 1;
                        _atLineStart = true;
                    }
                    else if (Peek() == '#')
                    {
                        SkipLineComment();
                        if (!IsAtEnd() && Peek() == '\n')
                        {
                            Advance();
                            _line++;
                            _column = 1;
                            _atLineStart = true;
                        }
                    }
                    return NextToken(); // 재귀 호출로 다음 토큰 처리
                }
                
                return HandleIndentation();
            }
            
            SkipWhitespace();
            
            if (IsAtEnd()) return null;
            
            var start = _position;
            var line = _line;
            var column = _column;
            var c = Advance();

            switch (c)
            {
                // Single character tokens
                case '(': return new PyToken(TokenType.LEFT_PAREN, "(", line, column);
                case ')': return new PyToken(TokenType.RIGHT_PAREN, ")", line, column);
                case '[': return new PyToken(TokenType.LEFT_BRACKET, "[", line, column);
                case ']': return new PyToken(TokenType.RIGHT_BRACKET, "]", line, column);
                case '{': return new PyToken(TokenType.LEFT_BRACE, "{", line, column);
                case '}': return new PyToken(TokenType.RIGHT_BRACE, "}", line, column);
                case ',': return new PyToken(TokenType.COMMA, ",", line, column);
                case '.': return new PyToken(TokenType.DOT, ".", line, column);
                case ';': return new PyToken(TokenType.SEMICOLON, ";", line, column);
                case '+': 
                    if (Match('=')) return new PyToken(TokenType.PLUS_EQUAL, "+=", line, column);
                    return new PyToken(TokenType.PLUS, "+", line, column);
                case '-': 
                    if (Match('=')) return new PyToken(TokenType.MINUS_EQUAL, "-=", line, column);
                    return new PyToken(TokenType.MINUS, "-", line, column);
                case '*': 
                    if (Match('*')) 
                    {
                        if (Match('=')) return new PyToken(TokenType.STAR_STAR_EQUAL, "**=", line, column);
                        return new PyToken(TokenType.STAR_STAR, "**", line, column);
                    }
                    if (Match('=')) return new PyToken(TokenType.STAR_EQUAL, "*=", line, column);
                    return new PyToken(TokenType.STAR, "*", line, column);
                case '/': 
                    if (Match('/')) 
                    {
                        if (Match('=')) return new PyToken(TokenType.SLASH_SLASH_EQUAL, "//=", line, column);
                        return new PyToken(TokenType.SLASH_SLASH, "//", line, column);
                    }
                    if (Match('=')) return new PyToken(TokenType.SLASH_EQUAL, "/=", line, column);
                    return new PyToken(TokenType.SLASH, "/", line, column);
                case '%': 
                    if (Match('=')) return new PyToken(TokenType.PERCENT_EQUAL, "%=", line, column);
                    return new PyToken(TokenType.PERCENT, "%", line, column);
                case '&': 
                    if (Match('=')) return new PyToken(TokenType.AMPERSAND_EQUAL, "&=", line, column);
                    return new PyToken(TokenType.AMPERSAND, "&", line, column);
                case '|': 
                    if (Match('=')) return new PyToken(TokenType.PIPE_EQUAL, "|=", line, column);
                    return new PyToken(TokenType.PIPE, "|", line, column);
                case '^': 
                    if (Match('=')) return new PyToken(TokenType.CARET_EQUAL, "^=", line, column);
                    return new PyToken(TokenType.CARET, "^", line, column);
                case '~': return new PyToken(TokenType.TILDE, "~", line, column);

                // Comparison operators
                case '<': 
                    if (Match('<')) 
                    {
                        if (Match('=')) return new PyToken(TokenType.LEFT_SHIFT_EQUAL, "<<=", line, column);
                        return new PyToken(TokenType.LEFT_SHIFT, "<<", line, column);
                    }
                    if (Match('=')) return new PyToken(TokenType.LESS_EQUAL, "<=", line, column);
                    return new PyToken(TokenType.LESS, "<", line, column);
                    
                case '>':
                    if (Match('>')) 
                    {
                        if (Match('=')) return new PyToken(TokenType.RIGHT_SHIFT_EQUAL, ">>=", line, column);
                        return new PyToken(TokenType.RIGHT_SHIFT, ">>", line, column);
                    }
                    if (Match('=')) return new PyToken(TokenType.GREATER_EQUAL, ">=", line, column);
                    return new PyToken(TokenType.GREATER, ">", line, column);

                case '=':
                    if (Match('=')) return new PyToken(TokenType.EQUAL_EQUAL, "==", line, column);
                    return new PyToken(TokenType.EQUAL, "=", line, column);

                case '!':
                    if (Match('=')) return new PyToken(TokenType.BANG_EQUAL, "!=", line, column);
                    return new PyToken(TokenType.BANG, "!", line, column);

                case ':':
                    if (Match('=')) return new PyToken(TokenType.WALRUS, ":=", line, column);
                    return new PyToken(TokenType.COLON, ":", line, column);

                case '@': return new PyToken(TokenType.AT, "@", line, column);

                // Newline
                case '\n':
                    _line++;
                    _column = 1;
                    _atLineStart = true;
                    return new PyToken(TokenType.NEWLINE, "\n", line, column);

                // Comments
                case '#':
                    SkipLineComment();
                    return NextToken(); // Skip comment and get next token

                // Strings
                case '"':
                case '\'':
                    return ScanString(c);
                    
                // String prefixes (r, b, f, u)
                case 'r':
                case 'R':
                case 'b': 
                case 'B':
                case 'f':
                case 'F':
                case 'u':
                case 'U':
                    return ScanPossibleString() ?? ScanIdentifier();

                default:
                    if (IsDigit(c))
                    {
                        return ScanNumber();
                    }
                    else if (IsAlpha(c))
                    {
                        return ScanIdentifier();
                    }
                    else
                    {
                        throw new Exception($"Unexpected character '{c}' at line {line}, column {column}");
                    }
            }
        }

        private PyToken ScanString(char quote)
        {
            var line = _line;
            var column = _column - 1;
            var pos = _position - 1; // Current position is at opening quote
            
            // Check for triple quotes
            bool isTripleQuoted = false;
            if (pos + 2 < _source.Length && 
                _source[pos + 1] == quote && 
                _source[pos + 2] == quote)
            {
                isTripleQuoted = true;
                _position += 2; // Skip the two additional opening quotes
                _column += 2;
                var value = ScanTripleQuotedString(quote, TokenType.STRING);
                return new PyToken(TokenType.STRING, value, line, column);
            }
            var stringValue = new StringBuilder();
            
            while (!IsAtEnd() && Peek() != quote)
            {
                if (Peek() == '\n')
                {
                    _line++;
                    _column = 1;
                }
                else if (Peek() == '\\')
                {
                    // Handle escape sequences
                    Advance(); // consume backslash
                    if (!IsAtEnd())
                    {
                        var escaped = Advance();
                        switch (escaped)
                        {
                            case 'n': stringValue.Append('\n'); break;
                            case 't': stringValue.Append('\t'); break;
                            case 'r': stringValue.Append('\r'); break;
                            case '\\': stringValue.Append('\\'); break;
                            case '\'': stringValue.Append('\''); break;
                            case '"': stringValue.Append('"'); break;
                            default: stringValue.Append(escaped); break;
                        }
                    }
                }
                else
                {
                    stringValue.Append(Advance());
                }
            }

            if (IsAtEnd())
            {
                throw new Exception($"Unterminated string at line {line}, column {column}");
            }

            // Consume closing quote
            Advance();

            return new PyToken(TokenType.STRING, stringValue.ToString(), line, column);
        }

        /// <summary>
        /// CPython-style number scanning with support for:
        /// - Hex (0x), Octal (0o), Binary (0b)  
        /// - Underscores in numbers (1_000_000)
        /// - Complex numbers (3+4j)
        /// - Scientific notation (1e10)
        /// </summary>
        private PyToken ScanNumber()
        {
            var line = _line;
            var column = _column - 1;
            var start = _position - 1;
            var firstChar = _source[start];
            
            // Handle special prefixes: 0x, 0o, 0b
            if (firstChar == '0' && !IsAtEnd() && _position < _source.Length)
            {
                var nextChar = char.ToLower(Peek());
                return nextChar switch
                {
                    'x' => ScanHexNumber(line, column),
                    'o' => ScanOctalNumber(line, column), 
                    'b' => ScanBinaryNumber(line, column),
                    _ => ScanDecimalNumber(line, column, start)
                };
            }
            
            return ScanDecimalNumber(line, column, start);
        }
        
        private PyToken ScanHexNumber(int line, int column)
        {
            var start = _position - 1;
            Advance(); // consume 'x'
            
            while (IsHexDigit(Peek()) || Peek() == '_')
            {
                if (Peek() != '_') // Skip underscores  
                    Advance();
                else
                    Advance();
            }
            
            var hexValue = _source.Substring(start, _position - start);
            return new PyToken(TokenType.INTEGER, hexValue, line, column);
        }
        
        private PyToken ScanOctalNumber(int line, int column)
        {
            var start = _position - 1;
            Advance(); // consume 'o'
            
            while (IsOctalDigit(Peek()) || Peek() == '_')
            {
                Advance();
            }
            
            var octalValue = _source.Substring(start, _position - start);
            return new PyToken(TokenType.INTEGER, octalValue, line, column);
        }
        
        private PyToken ScanBinaryNumber(int line, int column)
        {
            var start = _position - 1;
            Advance(); // consume 'b'
            
            while (IsBinaryDigit(Peek()) || Peek() == '_')
            {
                Advance();
            }
            
            var binaryValue = _source.Substring(start, _position - start);
            return new PyToken(TokenType.INTEGER, binaryValue, line, column);
        }
        
        private PyToken ScanDecimalNumber(int line, int column, int start)
        {
            // Scan integer part with underscores
            while (IsDigit(Peek()) || Peek() == '_')
            {
                Advance();
            }
            
            bool isFloat = false;
            
            // Look for decimal part
            if (Peek() == '.' && IsDigit(PeekNext()))
            {
                isFloat = true;
                Advance(); // consume .
                while (IsDigit(Peek()) || Peek() == '_') Advance();
            }
            
            // Look for scientific notation (e/E)
            if (char.ToLower(Peek()) == 'e')
            {
                isFloat = true;
                Advance(); // consume e/E
                
                if (Peek() == '+' || Peek() == '-')
                    Advance(); // consume sign
                    
                while (IsDigit(Peek()) || Peek() == '_') Advance();
            }
            
            // Look for complex number suffix (j/J)
            if (char.ToLower(Peek()) == 'j')
            {
                Advance(); // consume j/J
                var complexValue = _source.Substring(start, _position - start);
                return new PyToken(TokenType.COMPLEX, complexValue, line, column);
            }
            
            var numberValue = _source.Substring(start, _position - start);
            var tokenType = isFloat ? TokenType.FLOAT : TokenType.INTEGER;
            return new PyToken(tokenType, numberValue, line, column);
        }
        
        private bool IsHexDigit(char c) => IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        private bool IsOctalDigit(char c) => c >= '0' && c <= '7';
        private bool IsBinaryDigit(char c) => c == '0' || c == '1';

        private PyToken ScanIdentifier()
        {
            var line = _line;
            var column = _column - 1;
            var start = _position - 1;

            while (IsAlphaNumeric(Peek())) Advance();

            var text = _source.Substring(start, _position - start);
            var type = GetKeywordType(text);

            return new PyToken(type, text, line, column);
        }

        private TokenType GetKeywordType(string text)
        {
            return text switch
            {
                "and" => TokenType.AND,
                "as" => TokenType.AS,
                "assert" => TokenType.ASSERT,
                "async" => TokenType.ASYNC,
                "await" => TokenType.AWAIT,
                "break" => TokenType.BREAK,
                "case" => TokenType.CASE,
                "class" => TokenType.CLASS,
                "continue" => TokenType.CONTINUE,
                "def" => TokenType.DEF,
                "del" => TokenType.DEL,
                "elif" => TokenType.ELIF,
                "else" => TokenType.ELSE,
                "except" => TokenType.EXCEPT,
                "False" => TokenType.FALSE,
                "finally" => TokenType.FINALLY,
                "for" => TokenType.FOR,
                "from" => TokenType.FROM,
                "global" => TokenType.GLOBAL,
                "if" => TokenType.IF,
                "import" => TokenType.IMPORT,
                "in" => TokenType.IN,
                "is" => TokenType.IS,
                "lambda" => TokenType.LAMBDA,
                "match" => TokenType.MATCH,
                "None" => TokenType.NONE,
                "nonlocal" => TokenType.NONLOCAL,
                "not" => TokenType.NOT,
                "or" => TokenType.OR,
                "pass" => TokenType.PASS,
                "raise" => TokenType.RAISE,
                "return" => TokenType.RETURN,
                "True" => TokenType.TRUE,
                "try" => TokenType.TRY,
                "type" => TokenType.TYPE, // Python 3.12
                "while" => TokenType.WHILE,
                "with" => TokenType.WITH,
                "yield" => TokenType.YIELD,
                _ => TokenType.IDENTIFIER
            };
        }

        // Helper methods
        private bool IsAtEnd() => _position >= _source.Length;
        
        private char Advance()
        {
            _column++;
            return _source[_position++];
        }

        private bool Match(char expected)
        {
            if (IsAtEnd() || _source[_position] != expected) return false;
            _position++;
            _column++;
            return true;
        }

        private char Peek() => IsAtEnd() ? '\0' : _source[_position];
        private char PeekNext() => _position + 1 >= _source.Length ? '\0' : _source[_position + 1];

        private void SkipWhitespace()
        {
            while (!IsAtEnd())
            {
                var c = Peek();
                if (c == ' ' || c == '\r' || c == '\t')
                {
                    Advance();
                }
                else
                {
                    break;
                }
            }
        }

        private void SkipLineComment()
        {
            while (!IsAtEnd() && Peek() != '\n') Advance();
        }

        private bool IsDigit(char c) => c >= '0' && c <= '9';
        private bool IsAlpha(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
        private bool IsAlphaNumeric(char c) => IsAlpha(c) || IsDigit(c);
        
        /// <summary>
        /// CPython-style string prefix handling (r, b, f, u prefixes)
        /// </summary>
        private PyToken? ScanPossibleString()
        {
            var line = _line;
            var column = _column - 1;
            var startPos = _position - 1;
            
            // Collect potential string prefixes
            var prefixes = new List<char>();
            var pos = startPos;
            
            while (pos < _source.Length)
            {
                var c = char.ToLower(_source[pos]);
                if (c is 'r' or 'b' or 'f' or 'u')
                {
                    prefixes.Add(c);
                    pos++;
                }
                else
                {
                    break;
                }
            }
            
            // Check if followed by string quote
            if (pos >= _source.Length || (_source[pos] != '"' && _source[pos] != '\''))
            {
                // Reset position - this is just an identifier
                return null;
            }
            
            // Advance to quote position
            _position = pos;
            _column += (pos - startPos);
            
            var quote = _source[pos];
            var stringType = DetermineStringType(prefixes);
            
            // Handle triple quotes - fixed boundary condition
            bool isTripleQuoted = false;
            // Triple quotes detection logic
            
            if (pos + 2 <= _source.Length - 1 && 
                _source[pos + 1] == quote && 
                _source[pos + 2] == quote)
            {
                isTripleQuoted = true;
                _position += 3; // Skip all three opening quotes
                _column += 3;
                // Found triple quotes
            }
            else
            {
                Advance(); // consume single opening quote
                // Single quote detected
            }
            
            var value = isTripleQuoted ? 
                ScanTripleQuotedString(quote, stringType) : 
                (stringType == TokenType.F_STRING ? 
                    ScanFString(quote) : 
                    ScanRegularString(quote, stringType));
                
            return new PyToken(stringType, value, line, column);
        }
        
        private TokenType DetermineStringType(List<char> prefixes)
        {
            // CPython-style prefix combination handling
            bool hasRaw = prefixes.Contains('r');
            bool hasBytes = prefixes.Contains('b');
            bool hasFormat = prefixes.Contains('f');
            bool hasUnicode = prefixes.Contains('u');
            
            // Python 3.12 rules: b and f are mutually exclusive
            if (hasBytes && hasFormat)
                throw new Exception("Cannot combine 'b' and 'f' string prefixes");
                
            if (hasFormat) return TokenType.F_STRING;
            if (hasBytes) return TokenType.BYTES_STRING;  
            if (hasRaw) return TokenType.RAW_STRING;
            
            return TokenType.STRING;
        }
        
        private string ScanTripleQuotedString(char quote, TokenType stringType)
        {
            var value = new StringBuilder();
            const int quoteSize = 3; // We know it's triple quotes
            int endQuoteSize = 0; // Track matching closing quotes
            
            while (!IsAtEnd())
            {
                char ch = Peek();
                
                if (ch == quote)
                {
                    endQuoteSize++;
                    Advance();
                    
                    // Check if we have enough closing quotes
                    if (endQuoteSize == quoteSize)
                    {
                        return value.ToString();
                    }
                }
                else
                {
                    // Reset quote counter if we hit a non-quote character
                    if (endQuoteSize > 0)
                    {
                        // Add the quotes we collected so far to the value
                        for (int i = 0; i < endQuoteSize; i++)
                        {
                            value.Append(quote);
                        }
                        endQuoteSize = 0;
                    }
                    
                    // Process the current character
                    ch = Advance();
                    if (ch == '\n')
                    {
                        _line++;
                        _column = 1;
                    }
                    
                    value.Append(ch);
                }
            }
            
            throw new Exception($"EOF while scanning triple-quoted string literal");
        }
        
        /// <summary>
        /// PEP 701: Scan f-string with nested quote support
        /// </summary>
        private string ScanFString(char quote)
        {
            var value = new StringBuilder();
            int braceLevel = 0;
            bool inNestedString = false;
            char nestedStringQuote = '\0';
            
            while (!IsAtEnd())
            {
                char c = Peek();
                
                // Handle escape sequences
                if (c == '\\' && !inNestedString)
                {
                    value.Append(Advance()); // Add backslash
                    if (!IsAtEnd())
                    {
                        value.Append(Advance()); // Add escaped character
                    }
                    continue;
                }
                
                // Handle f-string end (only when not inside braces or nested strings)
                if (c == quote && braceLevel == 0 && !inNestedString)
                {
                    break; // End of f-string
                }
                
                // Handle braces (f-string expressions)
                if (!inNestedString)
                {
                    if (c == '{')
                    {
                        if (PeekNext() == '{') // Escaped brace {{
                        {
                            value.Append(Advance()); // Add first {
                            value.Append(Advance()); // Add second {
                            continue;
                        }
                        else
                        {
                            braceLevel++;
                        }
                    }
                    else if (c == '}')
                    {
                        if (PeekNext() == '}') // Escaped brace }}
                        {
                            value.Append(Advance()); // Add first }
                            value.Append(Advance()); // Add second }
                            continue;
                        }
                        else
                        {
                            braceLevel--;
                        }
                    }
                }
                
                // Handle nested strings inside f-string expressions
                if (braceLevel > 0)
                {
                    if (!inNestedString && (c == '"' || c == '\''))
                    {
                        // Start of nested string
                        inNestedString = true;
                        nestedStringQuote = c;
                    }
                    else if (inNestedString && c == nestedStringQuote)
                    {
                        // Check for escape
                        if (value.Length > 0 && value[value.Length - 1] != '\\')
                        {
                            // End of nested string
                            inNestedString = false;
                            nestedStringQuote = '\0';
                        }
                    }
                }
                
                // Handle newlines
                if (c == '\n')
                {
                    _line++;
                    _column = 1;
                }
                
                value.Append(Advance());
            }
            
            if (IsAtEnd())
            {
                throw new Exception("Unterminated f-string literal");
            }
            
            Advance(); // consume closing quote
            return value.ToString();
        }
        
        private string ScanRegularString(char quote, TokenType stringType)
        {
            var value = new StringBuilder();
            
            while (!IsAtEnd() && Peek() != quote)
            {
                if (Peek() == '\n' && stringType != TokenType.RAW_STRING)
                {
                    throw new Exception("Unterminated string literal");
                }
                
                // Handle escapes for non-raw strings  
                if (stringType != TokenType.RAW_STRING && Peek() == '\\')
                {
                    Advance(); // consume backslash
                    if (!IsAtEnd())
                    {
                        value.Append(ProcessEscapeSequence(Advance()));
                    }
                }
                else
                {
                    if (Peek() == '\n')
                    {
                        _line++;
                        _column = 1;
                    }
                    value.Append(Advance());
                }
            }
            
            if (IsAtEnd())
            {
                throw new Exception("Unterminated string literal");
            }
            
            Advance(); // consume closing quote
            return value.ToString();
        }
        
        /// <summary>
        /// 줄 시작에서 들여쓰기를 처리하여 INDENT/DEDENT 토큰 생성
        /// CPython 방식을 따름
        /// </summary>
        private PyToken HandleIndentation()
        {
            int indentLevel = 0;
            var startColumn = _column;
            
            // 현재 줄의 들여쓰기 레벨 계산
            while (!IsAtEnd() && (Peek() == ' ' || Peek() == '\t'))
            {
                if (Peek() == ' ')
                    indentLevel++;
                else if (Peek() == '\t')
                    indentLevel += 8; // 탭은 8칸으로 계산
                Advance();
            }
            
            var currentLevel = _indentStack.Peek();
            var line = _line;
            var column = startColumn;
            
            if (indentLevel > currentLevel)
            {
                // 들여쓰기 증가 - INDENT 토큰 생성
                _indentStack.Push(indentLevel);
                return new PyToken(TokenType.INDENT, new string(' ', indentLevel), line, column);
            }
            else if (indentLevel < currentLevel)
            {
                // 들여쓰기 감소 - DEDENT 토큰들 생성
                var dedentCount = 0;
                while (_indentStack.Count > 1 && _indentStack.Peek() > indentLevel)
                {
                    _indentStack.Pop();
                    dedentCount++;
                }
                
                // 첫 번째 DEDENT 토큰을 반환하고 나머지는 pending에 추가
                if (dedentCount > 0)
                {
                    for (int i = 1; i < dedentCount; i++)
                    {
                        _pendingTokens.Add(new PyToken(TokenType.DEDENT, "", line, column));
                    }
                    return new PyToken(TokenType.DEDENT, "", line, column);
                }
                
                // 들여쓰기 레벨이 스택에 없는 경우 - 에러
                if (_indentStack.Peek() != indentLevel)
                {
                    throw new Exception($"Indentation error: unexpected indent level {indentLevel}");
                }
            }
            
            // 같은 레벨 - 다음 토큰 처리
            return NextToken() ?? new PyToken(TokenType.EOF, "", line, column);
        }

        private char ProcessEscapeSequence(char escaped)
        {
            return escaped switch
            {
                'n' => '\n',
                't' => '\t', 
                'r' => '\r',
                '\\' => '\\',
                '\'' => '\'',
                '"' => '"',
                'a' => '\a',  // bell
                'b' => '\b',  // backspace
                'f' => '\f',  // form feed  
                'v' => '\v',  // vertical tab
                '0' => '\0',  // null
                _ => escaped  // literal character
            };
        }
    }

    #endregion
}