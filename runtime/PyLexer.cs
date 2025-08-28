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

        private PyToken ScanNumber()
        {
            var line = _line;
            var column = _column - 1;
            var start = _position - 1;

            while (IsDigit(Peek())) Advance();

            // Look for decimal part
            if (Peek() == '.' && IsDigit(PeekNext()))
            {
                Advance(); // consume .
                while (IsDigit(Peek())) Advance();
                
                var floatValue = _source.Substring(start, _position - start);
                return new PyToken(TokenType.FLOAT, floatValue, line, column);
            }

            var intValue = _source.Substring(start, _position - start);
            return new PyToken(TokenType.INTEGER, intValue, line, column);
        }

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
    }

    #endregion
}