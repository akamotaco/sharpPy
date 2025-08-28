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
        
        public PyLexer(string source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _position = 0;
            _line = 1;
            _column = 1;
        }

        /// <summary>
        /// 소스 코드를 토큰 리스트로 변환
        /// </summary>
        public List<PyToken> Tokenize()
        {
            var tokens = new List<PyToken>();
            
            while (!IsAtEnd())
            {
                var token = NextToken();
                if (token != null)
                {
                    tokens.Add(token);
                }
            }
            
            // EOF 토큰 추가
            tokens.Add(new PyToken(TokenType.EOF, "", _line, _column));
            
            return tokens;
        }

        private PyToken? NextToken()
        {
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

                // Newline
                case '\n':
                    _line++;
                    _column = 1;
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
            var value = new StringBuilder();
            
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
                            case 'n': value.Append('\n'); break;
                            case 't': value.Append('\t'); break;
                            case 'r': value.Append('\r'); break;
                            case '\\': value.Append('\\'); break;
                            case '\'': value.Append('\''); break;
                            case '"': value.Append('"'); break;
                            default: value.Append(escaped); break;
                        }
                    }
                }
                else
                {
                    value.Append(Advance());
                }
            }

            if (IsAtEnd())
            {
                throw new Exception($"Unterminated string at line {line}, column {column}");
            }

            // Consume closing quote
            Advance();

            return new PyToken(TokenType.STRING, value.ToString(), line, column);
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
            
            // Handle triple quotes
            bool isTripleQuoted = false;
            if (pos + 2 < _source.Length && 
                _source[pos + 1] == quote && 
                _source[pos + 2] == quote)
            {
                isTripleQuoted = true;
                _position += 2; // Skip the two extra quotes
                _column += 2;
            }
            
            Advance(); // consume opening quote
            
            var value = isTripleQuoted ? 
                ScanTripleQuotedString(quote, stringType) : 
                ScanRegularString(quote, stringType);
                
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
            var quoteString = new string(quote, 3);
            
            while (!IsAtEnd())
            {
                // Check for closing triple quote
                if (_position + 2 < _source.Length &&
                    _source[_position] == quote &&
                    _source[_position + 1] == quote &&
                    _source[_position + 2] == quote)
                {
                    _position += 3;
                    _column += 3;
                    break;
                }
                
                if (Peek() == '\n')
                {
                    _line++;
                    _column = 1;
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
                    value.Append(Advance());
                }
            }
            
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