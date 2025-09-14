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
            _column = 0;  // CPython 3.12: 0-based column indexing
            
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
            // CPython behavior: ENDMARKER goes to the next line if file doesn't end with newline
            var endLine = _line;
            var endColumn = 0;

            // If file doesn't end with newline, CPython puts ENDMARKER on next line
            if (_position > 0 && _source[_position - 1] != '\n')
            {
                endLine++;
            }

            var eofToken = new PyToken(TokenType.ENDMARKER, "", endLine, endColumn);
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
                    return new PyToken(TokenType.OP, "(", line, column);
                case ')':
                    if (_bracketStack.Count > 0 && _bracketStack.Peek() == '(')
                        _bracketStack.Pop();
                    return new PyToken(TokenType.OP, ")", line, column);
                case '[':
                    _bracketStack.Push('[');
                    return new PyToken(TokenType.OP, "[", line, column);
                case ']':
                    if (_bracketStack.Count > 0 && _bracketStack.Peek() == '[')
                        _bracketStack.Pop();
                    return new PyToken(TokenType.OP, "]", line, column);
                case '{':
                    _bracketStack.Push('{');
                    return new PyToken(TokenType.OP, "{", line, column);
                case '}':
                    if (_bracketStack.Count > 0 && _bracketStack.Peek() == '{')
                        _bracketStack.Pop();
                    return new PyToken(TokenType.OP, "}", line, column);
                case ',': return new PyToken(TokenType.OP, ",", line, column);
                case '.':
                    // CPython 3.12: Check for ellipsis (...)
                    if (_position < _source.Length - 1 &&
                        _source[_position] == '.' && _source[_position + 1] == '.')
                    {
                        Advance(); // consume second .
                        Advance(); // consume third .
                        return new PyToken(TokenType.OP, "...", line, column);
                    }
                    return new PyToken(TokenType.OP, ".", line, column);
                case ';': return new PyToken(TokenType.OP, ";", line, column);
                case '+':
                    if (Match('=')) return new PyToken(TokenType.OP, "+=", line, column);
                    return new PyToken(TokenType.OP, "+", line, column);
                case '-':
                    if (Match('=')) return new PyToken(TokenType.OP, "-=", line, column);
                    if (Match('>')) return new PyToken(TokenType.OP, "->", line, column);
                    return new PyToken(TokenType.OP, "-", line, column);
                case '*':
                    if (Match('*'))
                    {
                        if (Match('=')) return new PyToken(TokenType.OP, "**=", line, column);
                        return new PyToken(TokenType.OP, "**", line, column);
                    }
                    if (Match('=')) return new PyToken(TokenType.OP, "*=", line, column);
                    return new PyToken(TokenType.OP, "*", line, column);
                case '/':
                    if (Match('/'))
                    {
                        if (Match('=')) return new PyToken(TokenType.OP, "//=", line, column);
                        return new PyToken(TokenType.OP, "//", line, column);
                    }
                    if (Match('=')) return new PyToken(TokenType.OP, "/=", line, column);
                    return new PyToken(TokenType.OP, "/", line, column);
                case '%':
                    if (Match('=')) return new PyToken(TokenType.OP, "%=", line, column);
                    return new PyToken(TokenType.OP, "%", line, column);
                case '&':
                    if (Match('=')) return new PyToken(TokenType.OP, "&=", line, column);
                    return new PyToken(TokenType.OP, "&", line, column);
                case '|':
                    if (Match('=')) return new PyToken(TokenType.OP, "|=", line, column);
                    return new PyToken(TokenType.OP, "|", line, column);
                case '^':
                    if (Match('=')) return new PyToken(TokenType.OP, "^=", line, column);
                    return new PyToken(TokenType.OP, "^", line, column);
                case '~': return new PyToken(TokenType.OP, "~", line, column);

                // Comparison operators
                case '<':
                    if (Match('<'))
                    {
                        if (Match('=')) return new PyToken(TokenType.OP, "<<=", line, column);
                        return new PyToken(TokenType.OP, "<<", line, column);
                    }
                    if (Match('=')) return new PyToken(TokenType.OP, "<=", line, column);
                    return new PyToken(TokenType.OP, "<", line, column);

                case '>':
                    if (Match('>'))
                    {
                        if (Match('=')) return new PyToken(TokenType.OP, ">>=", line, column);
                        return new PyToken(TokenType.OP, ">>", line, column);
                    }
                    if (Match('=')) return new PyToken(TokenType.OP, ">=", line, column);
                    return new PyToken(TokenType.OP, ">", line, column);

                case '=':
                    if (Match('=')) return new PyToken(TokenType.OP, "==", line, column);
                    return new PyToken(TokenType.OP, "=", line, column);

                case '!':
                    if (Match('=')) return new PyToken(TokenType.OP, "!=", line, column);
                    return new PyToken(TokenType.OP, "!", line, column);

                case ':':
                    if (Match('=')) return new PyToken(TokenType.OP, ":=", line, column);
                    return new PyToken(TokenType.OP, ":", line, column);

                case '@':
                    if (Match('=')) return new PyToken(TokenType.OP, "@=", line, column);
                    return new PyToken(TokenType.OP, "@", line, column);

                // Newline - CPython 3.12: NEWLINE vs NL distinction
                case '\n':
                    // Use position before advancing, like other tokens
                    var savedLine = line;
                    var savedColumn = column;
                    _line++;
                    _column = 0;  // CPython 3.12: 0-based column indexing

                    // CPython 3.12: In implicit continuation, generate NL tokens for physical lines
                    TokenType tokenType;
                    if (IsInImplicitContinuation)
                    {
                        // In multiline structures, always generate NL for physical line breaks
                        tokenType = TokenType.NL;
                    }
                    else
                    {
                        // NEWLINE for logical lines, NL for physical lines
                        tokenType = _lineHasTokens ? TokenType.NEWLINE : TokenType.NL;
                    }
                    _atLineStart = true;
                    _lineHasTokens = false; // 새 줄 시작

                    return new PyToken(tokenType, "\n", savedLine, savedColumn);

                // Comments
                case '#':
                    return ScanComment(line, column);

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
            var column = _column - 1;  // CPython 3.12: 토큰 시작 위치 (첫 quote 소비 전)
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
                    _column = 0;  // CPython 3.12: 0-based column indexing
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

            // CPython 3.12: Include quotes in the token lexeme
            var fullString = quote + stringValue.ToString() + quote;
            return new PyToken(TokenType.STRING, fullString, line, column);
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
            var column = _column - 1;  // CPython 3.12: 토큰 시작 위치 (첫 숫자 소비 전)
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
            var column = _column - 1;  // CPython 3.12: 토큰 시작 위치 (첫 문자 소비 전)
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

        private PyToken ScanComment(int line, int column)
        {

            // '#' 문자부터 줄 끝까지 모든 내용을 주석으로 처리
            // CPython 3.12: 주석 토큰은 '#' 문자를 포함해야 함
            var comment = new StringBuilder();
            comment.Append('#'); // # 문자 포함 (이미 NextToken에서 확인했지만 토큰에 포함해야 함)

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
            var column = _column;
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

            if (hasFormat)
            {
                // CPython 3.12 방식: f-string을 여러 토큰으로 분할 (triple-quoted 포함)
                return ScanFStringTokens(quote, isTripleQuoted);
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

            // CPython 3.12: Include opening triple quotes in token lexeme
            value.Append(quote);
            value.Append(quote);
            value.Append(quote);

            const int quoteSize = 3; // We know it's triple quotes
            int endQuoteSize = 0; // Track matching closing quotes
            
            while (!IsAtEnd())
            {
                char ch = Peek();
                
                if (ch == quote)
                {
                    endQuoteSize++;
                    ch = Advance();
                    value.Append(ch);

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
                        _column = 0;  // CPython 3.12: 0-based column indexing
                    }
                    
                    value.Append(ch);
                }
            }
            
            throw new Exception($"EOF while scanning triple-quoted string literal");
        }
        
        /// <summary>
        /// CPython 3.12 호환: f-string을 FSTRING_START, FSTRING_MIDDLE, FSTRING_END 토큰으로 분할
        /// </summary>
        private PyToken ScanFStringTokens(char quote, bool isTripleQuoted)
        {
            var startLine = _line;
            var startColumn = _column;

#if DEBUG
            Console.WriteLine($"[DEBUG] ScanFStringTokens called: quote='{quote}', isTripleQuoted={isTripleQuoted}");
#endif

            // f" 또는 f""" 부분을 FSTRING_START로 생성
            var prefix = isTripleQuoted ? $"f{quote}{quote}{quote}" : $"f{quote}";
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

            // f-string 내부 파싱 (triple-quoted 고려)
            while (!IsAtEnd() && !IsEndOfFString(quote, isTripleQuoted))
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
                    tokens.Add(new PyToken(TokenType.OP, "{", _line, _column));
                    Advance();

                    // {} 내부의 표현식을 재귀적으로 토큰화
                    var exprStartPos = _position;
                    var exprStartLine = _line;
                    var exprStartColumn = _column;
                    var expr = new StringBuilder();

                    // 표현식과 포맷 지시자 분리
                    var formatSpecStart = -1;
                    var braceDepth = 0;
                    var parenDepth = 0;
                    var bracketDepth = 0;
                    var exprContent = new StringBuilder();

                    // 표현식 부분과 포맷 지시자 부분 구분
                    while (!IsAtEnd() && Peek() != '}')
                    {
                        var ch = Peek();
                        if (ch == '{') braceDepth++;
                        else if (ch == '}') braceDepth--;
                        else if (ch == '(') parenDepth++;
                        else if (ch == ')') parenDepth--;
                        else if (ch == '[') bracketDepth++;
                        else if (ch == ']') bracketDepth--;
                        else if (ch == ':' && braceDepth == 0 && parenDepth == 0 && bracketDepth == 0 && formatSpecStart == -1)
                        {
                            formatSpecStart = exprContent.Length;
                        }
                        exprContent.Append(Advance());
                    }

                    if (exprContent.Length > 0)
                    {
                        string exprPart, formatPart = null;

                        if (formatSpecStart >= 0)
                        {
                            // 표현식과 포맷 지시자 분리
                            exprPart = exprContent.ToString().Substring(0, formatSpecStart);
                            formatPart = exprContent.ToString().Substring(formatSpecStart + 1); // ':' 제외
                        }
                        else
                        {
                            exprPart = exprContent.ToString();
                        }

                        // 표현식 부분 토큰화
                        if (!string.IsNullOrEmpty(exprPart))
                        {
                            var exprLexer = new PyLexer(exprPart);
                            var exprTokens = exprLexer.Tokenize();

                            foreach (var token in exprTokens)
                            {
                                if (token.Type != TokenType.ENDMARKER &&
                                    !(token.Type == TokenType.NEWLINE && string.IsNullOrEmpty(token.Lexeme)))
                                {
                                    var adjustedToken = new PyToken(
                                        token.Type,
                                        token.Lexeme,
                                        exprStartLine + token.Line - 1,
                                        exprStartColumn + token.Column
                                    );
                                    tokens.Add(adjustedToken);
                                }
                            }
                        }

                        // 포맷 지시자가 있으면 `:` 토큰과 FSTRING_MIDDLE 토큰 추가
                        if (formatPart != null)
                        {
                            // ':' 토큰 추가
                            tokens.Add(new PyToken(TokenType.OP, ":", exprStartLine,
                                exprStartColumn + exprPart.Length));

                            // 포맷 지시자를 FSTRING_MIDDLE로 추가 (CPython 방식)
                            if (!string.IsNullOrEmpty(formatPart))
                            {
                                tokens.Add(new PyToken(TokenType.FSTRING_MIDDLE, formatPart, exprStartLine,
                                    exprStartColumn + exprPart.Length + 1));
                            }
                        }
                    }

                    // } 토큰 추가
                    if (!IsAtEnd() && Peek() == '}')
                    {
                        tokens.Add(new PyToken(TokenType.OP, "}", _line, _column));
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

            // 마지막 인용부호를 FSTRING_END로 추가 (triple-quoted 고려)
            if (!IsAtEnd() && IsEndOfFString(quote, isTripleQuoted))
            {
                var endToken = isTripleQuoted ? $"{quote}{quote}{quote}" : quote.ToString();
#if DEBUG
                Console.WriteLine($"[DEBUG] Creating FSTRING_END: isTripleQuoted={isTripleQuoted}, endToken='{endToken}'");
#endif
                tokens.Add(new PyToken(TokenType.FSTRING_END, endToken, _line, _column));

                // 종료 인용부호들 소비
                if (isTripleQuoted)
                {
                    Advance(); // 첫 번째 quote
                    Advance(); // 두 번째 quote
                    Advance(); // 세 번째 quote
                }
                else
                {
                    Advance(); // single quote
                }
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
                    _column = 0;  // CPython 3.12: 0-based column indexing
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

        /// <summary>
        /// f-string 종료 조건 확인 (triple-quoted 고려)
        /// </summary>
        private bool IsEndOfFString(char quote, bool isTripleQuoted)
        {
            if (isTripleQuoted)
            {
                return _position + 2 <= _source.Length &&
                       _position < _source.Length && _source[_position] == quote &&
                       _position + 1 < _source.Length && _source[_position + 1] == quote &&
                       _position + 2 < _source.Length && _source[_position + 2] == quote;
            }
            else
            {
                return Peek() == quote;
            }
        }
    }

    #endregion
}