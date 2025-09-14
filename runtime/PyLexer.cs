// #define DEBUG  // Disable debug output

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

            // 파일 끝에 도달한 후 남은 pending 토큰들을 모두 처리
            while (_pendingTokens.Count > 0)
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
            }

            // CPython 3.12: 파일 끝에 논리적 토큰이 있었다면 마지막 NEWLINE 추가
            if (_lineHasTokens)
            {
                var finalNewline = new PyToken(TokenType.NEWLINE, "", _line, _column);
                tokens.Add(finalNewline);

                if (debugOutput)
                {
                    Console.WriteLine($"{tokens.Count-1,2}: {(int)finalNewline.Type,2} {finalNewline.Type.ToString(),-12} {finalNewline.Lexeme.Replace("\n", "\\n").Replace("\t", "\\t"),-15} ({finalNewline.Line}, {finalNewline.Column})");
                }
            }

            // ENDMARKER 토큰 추가 (CPython 3.12)
            var eofToken = new PyToken(TokenType.ENDMARKER, "", _line, _column);
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
                    return new PyToken(TokenType.LPAR, "(", line, column);
                case ')':
                    if (_bracketStack.Count > 0 && _bracketStack.Peek() == '(')
                        _bracketStack.Pop();
                    return new PyToken(TokenType.RPAR, ")", line, column);
                case '[':
                    _bracketStack.Push('[');
                    return new PyToken(TokenType.LSQB, "[", line, column);
                case ']':
                    if (_bracketStack.Count > 0 && _bracketStack.Peek() == '[')
                        _bracketStack.Pop();
                    return new PyToken(TokenType.RSQB, "]", line, column);
                case '{':
                    _bracketStack.Push('{');
                    return new PyToken(TokenType.LBRACE, "{", line, column);
                case '}':
                    if (_bracketStack.Count > 0 && _bracketStack.Peek() == '{')
                        _bracketStack.Pop();
                    return new PyToken(TokenType.RBRACE, "}", line, column);
                case ',': return new PyToken(TokenType.COMMA, ",", line, column);
                case '.': return new PyToken(TokenType.DOT, ".", line, column);
                case ';': return new PyToken(TokenType.SEMI, ";", line, column);
                case '+':
                    if (Match('=')) return new PyToken(TokenType.PLUSEQUAL, "+=", line, column);
                    return new PyToken(TokenType.PLUS, "+", line, column);
                case '-':
                    if (Match('=')) return new PyToken(TokenType.MINEQUAL, "-=", line, column);
                    return new PyToken(TokenType.MINUS, "-", line, column);
                case '*':
                    if (Match('*'))
                    {
                        if (Match('=')) return new PyToken(TokenType.DOUBLESTAREQUAL, "**=", line, column);
                        return new PyToken(TokenType.DOUBLESTAR, "**", line, column);
                    }
                    if (Match('=')) return new PyToken(TokenType.STAREQUAL, "*=", line, column);
                    return new PyToken(TokenType.STAR, "*", line, column);
                case '/':
                    if (Match('/'))
                    {
                        if (Match('=')) return new PyToken(TokenType.DOUBLESLASHEQUAL, "//=", line, column);
                        return new PyToken(TokenType.DOUBLESLASH, "//", line, column);
                    }
                    if (Match('=')) return new PyToken(TokenType.SLASHEQUAL, "/=", line, column);
                    return new PyToken(TokenType.SLASH, "/", line, column);
                case '%':
                    if (Match('=')) return new PyToken(TokenType.PERCENTEQUAL, "%=", line, column);
                    return new PyToken(TokenType.PERCENT, "%", line, column);
                case '&':
                    if (Match('=')) return new PyToken(TokenType.AMPEREQUAL, "&=", line, column);
                    return new PyToken(TokenType.AMPER, "&", line, column);
                case '|':
                    if (Match('=')) return new PyToken(TokenType.VBAREQUAL, "|=", line, column);
                    return new PyToken(TokenType.VBAR, "|", line, column);
                case '^':
                    if (Match('=')) return new PyToken(TokenType.CIRCUMFLEXEQUAL, "^=", line, column);
                    return new PyToken(TokenType.CIRCUMFLEX, "^", line, column);
                case '~': return new PyToken(TokenType.TILDE, "~", line, column);

                // Comparison operators
                case '<':
                    if (Match('<'))
                    {
                        if (Match('=')) return new PyToken(TokenType.LEFTSHIFTEQUAL, "<<=", line, column);
                        return new PyToken(TokenType.LEFTSHIFT, "<<", line, column);
                    }
                    if (Match('=')) return new PyToken(TokenType.LESSEQUAL, "<=", line, column);
                    return new PyToken(TokenType.LESS, "<", line, column);

                case '>':
                    if (Match('>'))
                    {
                        if (Match('=')) return new PyToken(TokenType.RIGHTSHIFTEQUAL, ">>=", line, column);
                        return new PyToken(TokenType.RIGHTSHIFT, ">>", line, column);
                    }
                    if (Match('=')) return new PyToken(TokenType.GREATEREQUAL, ">=", line, column);
                    return new PyToken(TokenType.GREATER, ">", line, column);

                case '=':
                    if (Match('=')) return new PyToken(TokenType.EQEQUAL, "==", line, column);
                    return new PyToken(TokenType.EQUAL, "=", line, column);

                case '!':
                    if (Match('=')) return new PyToken(TokenType.NOTEQUAL, "!=", line, column);
                    return new PyToken(TokenType.EXCLAMATION, "!", line, column);

                case ':':
                    if (Match('=')) return new PyToken(TokenType.COLONEQUAL, ":=", line, column);
                    return new PyToken(TokenType.COLON, ":", line, column);

                case '@':
                    if (Match('=')) return new PyToken(TokenType.ATEQUAL, "@=", line, column);
                    return new PyToken(TokenType.AT, "@", line, column);

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
                        return HandleIndentation() ?? NextToken() ?? new PyToken(TokenType.ENDMARKER, "", savedLine, savedColumn);
                    }

                    // CPython 3.12: NEWLINE for logical lines, NL for physical lines
                    var tokenType = _lineHasTokens ? TokenType.NEWLINE : TokenType.NL;
                    _atLineStart = true;
                    _lineHasTokens = false; // 새 줄 시작

                    return new PyToken(tokenType, "\n", savedLine, savedColumn);

                // Comments
                case '#':
                    return ScanComment();

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
                var value = ScanTripleQuotedString(quote, false);
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
            return new PyToken(TokenType.NUMBER, hexValue, line, column);
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
            return new PyToken(TokenType.NUMBER, octalValue, line, column);
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
            return new PyToken(TokenType.NUMBER, binaryValue, line, column);
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
                return new PyToken(TokenType.NUMBER, complexValue, line, column); // Complex numbers are also NUMBER tokens in CPython 3.12
            }
            
            var numberValue = _source.Substring(start, _position - start);
            var tokenType = TokenType.NUMBER; // CPython 3.12: All numbers are NUMBER tokens
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
            // CPython 3.12: All keywords are NAME tokens, not separate token types
            return TokenType.NAME;
        }

        /// <summary>
        /// CPython 3.12: 토큰 시퀀스를 후처리하여 복합 연산자 생성
        /// NOT + IN → NOT_IN, IS + NOT → IS_NOT
        /// </summary>
        private List<PyToken> PostProcessCompoundOperators(List<PyToken> tokens)
        {
            // CPython 3.12: No compound operator tokens at lexer level
            // 'not in' and 'is not' are parsed as separate tokens: NAME(not) + NAME(in) or NAME(is) + NAME(not)
            return tokens;
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

        private PyToken ScanComment()
        {
            var line = _line;
            var column = _column;

            // '#' 문자부터 줄 끝까지 모든 내용을 주석으로 처리
            var comment = new StringBuilder();
            while (!IsAtEnd() && Peek() != '\n')
            {
                comment.Append(Advance());
            }

            // 주석은 논리적 토큰이 아니므로 _lineHasTokens를 설정하지 않음
            // CPython 3.12: 주석 후 줄바꿈은 NL 토큰이 됨

            return new PyToken(TokenType.COMMENT, comment.ToString(), line, column);
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
            
            // Extract prefix flags for scanning
            bool hasRaw = prefixes.Contains('r');
            bool hasFormat = prefixes.Contains('f');

            if (hasFormat && !isTripleQuoted)
            {
                // CPython 3.12 방식: f-string을 여러 토큰으로 분할
                return ScanFStringTokens(quote);
            }

            var value = isTripleQuoted ?
                ScanTripleQuotedString(quote, hasRaw) :
                ScanRegularString(quote, hasRaw);
                
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
                
            // CPython 3.12: All strings are STRING tokens regardless of prefixes
            // String prefix information is preserved in the lexeme, not the token type
            return TokenType.STRING;
        }
        
        private string ScanTripleQuotedString(char quote, bool hasRaw)
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
        /// CPython 3.12 호환: f-string을 FSTRING_START, FSTRING_MIDDLE, FSTRING_END 토큰으로 분할
        /// </summary>
        private PyToken ScanFStringTokens(char quote)
        {
            var startLine = _line;
            var startColumn = _column;

            // f" 또는 f' 부분을 FSTRING_START로 생성 (f 프리픽스는 이미 소비됨)
            var prefix = "f" + quote;
            var fstringStart = new PyToken(TokenType.FSTRING_START, prefix, startLine, startColumn - 1); // f까지 포함

#if DEBUG
            Console.WriteLine($"[DEBUG] Starting f-string parsing at position {_position}, line {_line}, column {_column}");
            Console.WriteLine($"[DEBUG] Looking for closing quote: '{quote}'");
#endif

            // f-string 내부를 파싱하여 토큰들을 생성
            var tokens = new List<PyToken>();
            tokens.Add(fstringStart);

            var currentText = new StringBuilder();
            var textStartLine = _line;
            var textStartColumn = _column;

            // f-string 내부 파싱
            while (!IsAtEnd() && Peek() != quote)
            {
                char c = Peek();
#if DEBUG
                Console.WriteLine($"[DEBUG] Processing char '{c}' at position {_position}");
#endif

                if (c == '{')
                {
                    // 현재까지의 텍스트를 FSTRING_MIDDLE로 추가
                    if (currentText.Length > 0)
                    {
                        tokens.Add(new PyToken(TokenType.FSTRING_MIDDLE, currentText.ToString(), textStartLine, textStartColumn));
                        currentText.Clear();
                    }

                    // { 토큰 추가 (CPython은 LBRACE=25가 아닌 OP=55 사용)
                    tokens.Add(new PyToken(TokenType.LBRACE, "{", _line, _column));
                    Advance();

                    // {} 내부의 표현식을 파싱
                    var exprStartLine = _line;
                    var exprStartColumn = _column;
                    var expr = new StringBuilder();

                    while (!IsAtEnd() && Peek() != '}')
                    {
                        expr.Append(Advance());
                    }

                    if (expr.Length > 0)
                    {
                        // 식별자를 NAME 토큰으로 추가
                        tokens.Add(new PyToken(TokenType.NAME, expr.ToString(), exprStartLine, exprStartColumn));
                    }

                    // } 토큰 추가
                    if (!IsAtEnd() && Peek() == '}')
                    {
                        tokens.Add(new PyToken(TokenType.RBRACE, "}", _line, _column));
                        Advance();
                    }

                    // 다음 텍스트 시작점 업데이트
                    textStartLine = _line;
                    textStartColumn = _column;
                }
                else
                {
                    // 일반 텍스트 누적
                    if (currentText.Length == 0)
                    {
                        textStartLine = _line;
                        textStartColumn = _column;
                    }
                    currentText.Append(Advance());
                }
            }

            // 마지막 텍스트 부분을 FSTRING_MIDDLE로 추가
            if (currentText.Length > 0)
            {
                tokens.Add(new PyToken(TokenType.FSTRING_MIDDLE, currentText.ToString(), textStartLine, textStartColumn));
            }

            // 마지막 인용부호를 FSTRING_END로 추가
            if (!IsAtEnd() && Peek() == quote)
            {
                tokens.Add(new PyToken(TokenType.FSTRING_END, quote.ToString(), _line, _column));
                Advance();
            }

            // 첫 번째 토큰을 제외한 나머지는 pending tokens에 추가
            for (int i = 1; i < tokens.Count; i++)
            {
                _pendingTokens.Add(tokens[i]);
            }

#if DEBUG
            Console.WriteLine($"[DEBUG] Generated {tokens.Count} f-string tokens, {tokens.Count-1} added to pending queue");
            foreach (var token in tokens)
            {
                Console.WriteLine($"[DEBUG]   {token.Type}: '{token.Lexeme}'");
            }
#endif

            return fstringStart;
        }

        /// <summary>
        /// PEP 701: Scan f-string with nested quote support (Legacy method - 사용 안 함)
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
        
        private string ScanRegularString(char quote, bool hasRaw)
        {
            var value = new StringBuilder();
            
            while (!IsAtEnd() && Peek() != quote)
            {
                if (Peek() == '\n' && !hasRaw) // Use raw flag instead of token type
                {
                    throw new Exception("Unterminated string literal");
                }
                
                // Handle escapes for non-raw strings  
                if (!hasRaw && Peek() == '\\') // Use raw flag instead of token type
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
                TokenType.NEWLINE or TokenType.NL or TokenType.INDENT or TokenType.DEDENT or TokenType.ENDMARKER or TokenType.COMMENT => false,
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