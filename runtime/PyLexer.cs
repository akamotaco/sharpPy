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

        // CPython 3.12: 현재 줄에 의미있는 토큰이 있었는지 추적 (NEWLINE vs NL 구분용)
        private bool _lineHasTokens;
        
        // CPython 3.12: Bracket stack for implicit line joining
        private Stack<char> _bracketStack;
        private bool IsInImplicitContinuation => _bracketStack.Count > 0;
        
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
            _lineHasTokens = false;

            // CPython 3.12: Bracket stack 초기화
            _bracketStack = new Stack<char>();
        }

        /// <summary>
        /// 소스 코드를 토큰 리스트로 변환
        /// </summary>
        public List<PyToken> Tokenize(bool debugOutput = false)
        {
            var tokens = new List<PyToken>();
            
            while (!IsAtEnd())
            {
                // Pending 토큰이 있으면 먼저 처리
                if (_pendingTokens.Count > 0)
                {
                    var pendingToken = _pendingTokens[0];
                    tokens.Add(pendingToken);
                    _pendingTokens.RemoveAt(0);

                    // CPython 3.12: 의미있는 토큰이면 현재 줄에 토큰이 있다고 표시
                    if (IsLogicalToken(pendingToken.Type))
                    {
                        _lineHasTokens = true;
                    }

                    if (debugOutput)
                    {
                        Console.WriteLine($"{tokens.Count-1,2}: {(int)pendingToken.Type,2} {pendingToken.Type.ToString(),-12} {pendingToken.Lexeme.Replace("\n", "\\n").Replace("\t", "\\t"),-15} ({pendingToken.Line}, {pendingToken.Column})");
                    }
                    continue;
                }

                var token = NextToken();
                if (token != null)
                {
                    tokens.Add(token);

                    // CPython 3.12: 의미있는 토큰이면 현재 줄에 토큰이 있다고 표시
                    if (IsLogicalToken(token.Type))
                    {
                        _lineHasTokens = true;
                    }

                    if (debugOutput)
                    {
                        Console.WriteLine($"{tokens.Count-1,2}: {(int)token.Type,2} {token.Type.ToString(),-12} {token.Lexeme.Replace("\n", "\\n").Replace("\t", "\\t"),-15} ({token.Line}, {token.Column})");
                    }
                }
            }
            
            // 파일 끝에서 남은 DEDENT 토큰들 생성
            while (_indentStack.Count > 1)
            {
                _indentStack.Pop();
                var dedentToken = new PyToken(TokenType.DEDENT, "", _line, _column);
                tokens.Add(dedentToken);

                if (debugOutput)
                {
                    Console.WriteLine($"{tokens.Count-1,2}: {(int)dedentToken.Type,2} {dedentToken.Type.ToString(),-12} {dedentToken.Lexeme.Replace("\n", "\\n").Replace("\t", "\\t"),-15} ({dedentToken.Line}, {dedentToken.Column})");
                }
            }

            // EOF 토큰 추가
            var eofToken = new PyToken(TokenType.EOF, "", _line, _column);
            tokens.Add(eofToken);

            if (debugOutput)
            {
                Console.WriteLine($"{tokens.Count-1,2}: {(int)eofToken.Type,2} {eofToken.Type.ToString(),-12} {eofToken.Lexeme.Replace("\n", "\\n").Replace("\t", "\\t"),-15} ({eofToken.Line}, {eofToken.Column})");
            }
            
            // CPython 3.12: 복합 연산자 후처리 (not in, is not)
            return PostProcessCompoundOperators(tokens);
        }

        private PyToken? NextToken()
        {
            // 줄 시작에서 들여쓰기 처리
            if (_atLineStart)
            {
                var indentToken = HandleIndentation();
                if (indentToken != null)
                {
                    // INDENT 토큰은 의미 있는 토큰
                    if (indentToken.Type == TokenType.INDENT)
                        _lineHasTokens = true;
                    return indentToken;
                }
                // HandleIndentation이 null을 반환하면 계속 처리
            }

            SkipWhitespace();

            if (IsAtEnd()) return null;
            
            var start = _position;
            var line = _line;
            var column = _column;
            var c = Advance();

            switch (c)
            {
                // Single character tokens - CPython 3.12: bracket stack management
                case '(':
                    _bracketStack.Push('(');
                    return new PyToken(TokenType.LEFT_PAREN, "(", line, column);
                case ')':
                    if (_bracketStack.Count > 0 && _bracketStack.Peek() == '(')
                        _bracketStack.Pop();
                    return new PyToken(TokenType.RIGHT_PAREN, ")", line, column);
                case '[':
                    _bracketStack.Push('[');
                    return new PyToken(TokenType.LEFT_BRACKET, "[", line, column);
                case ']':
                    if (_bracketStack.Count > 0 && _bracketStack.Peek() == '[')
                        _bracketStack.Pop();
                    return new PyToken(TokenType.RIGHT_BRACKET, "]", line, column);
                case '{':
                    _bracketStack.Push('{');
                    return new PyToken(TokenType.LEFT_BRACE, "{", line, column);
                case '}':
                    if (_bracketStack.Count > 0 && _bracketStack.Peek() == '{')
                        _bracketStack.Pop();
                    return new PyToken(TokenType.RIGHT_BRACE, "}", line, column);
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

                // Newline - CPython 3.12: NEWLINE vs NL distinction
                case '\n':
                    var savedLine = _line;
                    var savedColumn = _column;
                    _line++;
                    _column = 1;

                    // CPython 3.12: No tokens in implicit continuation lines
                    if (IsInImplicitContinuation)
                    {
                        _atLineStart = true;
                        return HandleIndentation() ?? NextToken() ?? new PyToken(TokenType.EOF, "", savedLine, savedColumn);
                    }

                    // CPython 3.12: NEWLINE for logical lines, NL for physical lines
                    var tokenType = _lineHasTokens ? TokenType.NEWLINE : TokenType.NL;
                    _atLineStart = true;
                    _lineHasTokens = false; // 새 줄 시작

                    return new PyToken(tokenType, "\n", savedLine, savedColumn);

                // Comments
                case '#':
                    SkipLineComment();
                    // 주석 후에는 일반적으로 새 줄이므로 _atLineStart 유지
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
                // "case" => TokenType.CASE,  // Soft keyword - treated as identifier
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
                // "match" => TokenType.MATCH,  // Soft keyword - treated as identifier
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

        /// <summary>
        /// CPython 3.12: 토큰 시퀀스를 후처리하여 복합 연산자 생성
        /// NOT + IN → NOT_IN, IS + NOT → IS_NOT
        /// </summary>
        private List<PyToken> PostProcessCompoundOperators(List<PyToken> tokens)
        {
            var result = new List<PyToken>();
            
            for (int i = 0; i < tokens.Count; i++)
            {
                var current = tokens[i];
                
                // CPython 3.12: 'not in' 복합 연산자 처리
                if (current.Type == TokenType.NOT && 
                    i + 1 < tokens.Count && 
                    tokens[i + 1].Type == TokenType.IN)
                {
                    // NOT + IN → NOT_IN 복합 토큰 생성
                    result.Add(new PyToken(TokenType.NOT_IN, "not in", current.Line, current.Column));
                    i++; // IN 토큰 건너뛰기
                }
                // CPython 3.12: 'is not' 복합 연산자 처리
                else if (current.Type == TokenType.IS && 
                         i + 1 < tokens.Count && 
                         tokens[i + 1].Type == TokenType.NOT)
                {
                    // IS + NOT → IS_NOT 복합 토큰 생성
                    result.Add(new PyToken(TokenType.IS_NOT, "is not", current.Line, current.Column));
                    i++; // NOT 토큰 건너뛰기
                }
                else
                {
                    // 일반 토큰은 그대로 추가
                    result.Add(current);
                }
            }
            
            return result;
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
                        var escapeChar = Peek();
                        if (escapeChar == 'x')
                        {
                            // Handle hex escape sequence \xHH
                            Advance(); // consume 'x'
                            if (_position + 1 < _source.Length && 
                                IsHexDigit(_source[_position]) && 
                                IsHexDigit(_source[_position + 1]))
                            {
                                var hex1 = _source[_position];
                                var hex2 = _source[_position + 1];
                                _position += 2;
                                _column += 2;
                                
                                // Convert hex digits to byte value
                                var byteValue = Convert.ToByte($"{hex1}{hex2}", 16);
                                value.Append((char)byteValue);
                            }
                            else
                            {
                                throw new Exception($"Invalid hex escape sequence at line {_line}, column {_column}");
                            }
                        }
                        else
                        {
                            value.Append(ProcessEscapeSequence(Advance()));
                        }
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
        /// CPython 3.12: implicit continuation 중에는 들여쓰기 무시
        /// </summary>
        /// <summary>
        /// 토큰이 논리적 의미가 있는 토큰인지 확인 (NEWLINE vs NL 구분용)
        /// </summary>
        private bool IsLogicalToken(TokenType type)
        {
            return type switch
            {
                TokenType.NEWLINE or TokenType.NL or TokenType.INDENT or TokenType.DEDENT or TokenType.EOF => false,
                _ => true // 나머지는 모두 의미있는 토큰
            };
        }

        /// <summary>
        /// CPython 3.12 호환 들여쓰기 처리 - 재귀 호출 없이 구현
        /// </summary>
        private PyToken? HandleIndentation()
        {
            // CPython 3.12: implicit continuation에서는 들여쓰기 무시
            if (IsInImplicitContinuation)
            {
                SkipWhitespace();
                _atLineStart = false;
                // 재귀 호출 대신 null 반환하여 NextToken이 계속 처리하도록 함
                return null;
            }

            var startLine = _line;
            var startCol = _column;

            // 현재 줄의 들여쓰기 레벨 계산
            int indentLevel = 0;
            while (!IsAtEnd() && (Peek() == ' ' || Peek() == '\t'))
            {
                if (Peek() == ' ')
                    indentLevel++;
                else if (Peek() == '\t')
                    indentLevel += 8; // 탭 = 8칸
                Advance();
            }

            // CPython 3.12: 빈 줄이나 주석만 있는 줄은 들여쓰기 변경 무시
            if (IsAtEnd() || Peek() == '\n' || Peek() == '\r' || Peek() == '#')
            {
                _atLineStart = false;
                // 재귀 호출 대신 null 반환
                return null;
            }

            // 실제 내용이 있는 줄에서만 들여쓰기 처리
            var currentLevel = _indentStack.Peek();
            _atLineStart = false; // 들여쓰기 처리 완료

            if (indentLevel > currentLevel)
            {
                // 들여쓰기 증가
                _indentStack.Push(indentLevel);
                return new PyToken(TokenType.INDENT, new string(' ', indentLevel - currentLevel), startLine, startCol);
            }
            else if (indentLevel < currentLevel)
            {
                // 들여쓰기 감소
                var dedentTokens = new List<PyToken>();

                while (_indentStack.Count > 1 && _indentStack.Peek() > indentLevel)
                {
                    _indentStack.Pop();
                    dedentTokens.Add(new PyToken(TokenType.DEDENT, "", startLine, startCol));
                }

                if (_indentStack.Peek() != indentLevel)
                {
                    throw new Exception($"Indentation error: unindent does not match any outer indentation level (line {startLine})");
                }

                if (dedentTokens.Count > 0)
                {
                    for (int i = 1; i < dedentTokens.Count; i++)
                    {
                        _pendingTokens.Add(dedentTokens[i]);
                    }
                    return dedentTokens[0];
                }
            }

            // 같은 레벨이거나 처리할 들여쓰기가 없으면 null 반환
            return null;
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