using System.Text;

namespace SharpPy.PegGenerator.Readers;

/// <summary>
/// Grammar 토크나이저 (python_py.gram을 토큰으로 변환)
/// CPython pegen 방식: 토큰 기반 파싱
/// </summary>
public class GrammarTokenizer
{
    private string _source = "";
    private int _position = 0;
    private int _line = 1;
    private int _column = 0;
    private List<GrammarToken> _tokens = new();

    public enum TokenType
    {
        NAME,           // identifier (rule name, token name)
        NUMBER,         // numeric literal (e.g., 5, 6, 3.14)
        STRING,         // 'keyword' or "soft_keyword"
        OP,             // operators: : | * + ? & ! ~ . ( ) [ ]
        NEWLINE,
        INDENT,         // Not used in python_py.gram (no significant indentation)
        DEDENT,         // Not used in python_py.gram
        ENDMARKER,
        COMMENT,        // # comment (skipped)
    }

    public class GrammarToken
    {
        public TokenType Type { get; set; }
        public string Value { get; set; } = "";
        public int Line { get; set; }
        public int Column { get; set; }
    }

    public List<GrammarToken> Tokenize(string source)
    {
        _source = source;
        _position = 0;
        _line = 1;
        _column = 0;
        _tokens = new List<GrammarToken>();

        while (_position < _source.Length)
        {
            SkipWhitespace();
            if (_position >= _source.Length)
                break;

            char c = _source[_position];

            // Comment - skip but don't consume the newline
            if (c == '#')
            {
                // Skip until newline (but don't consume it)
                while (_position < _source.Length && _source[_position] != '\n')
                {
                    _position++;
                }
                // Don't skip the newline - let the next iteration handle it
                continue;
            }

            // Newline
            if (c == '\n')
            {
                AddToken(TokenType.NEWLINE, "\n");
                _position++;
                _line++;
                _column = 0;
                continue;
            }

            if (c == '\r')
            {
                _position++;
                if (_position < _source.Length && _source[_position] == '\n')
                {
                    AddToken(TokenType.NEWLINE, "\r\n");
                    _position++;
                    _line++;
                    _column = 0;
                }
                continue;
            }

            // String (keyword)
            if (c == '\'' || c == '"')
            {
                TokenizeString();
                continue;
            }

            // Operators
            if (":|*+?&!~.()[]{}=,;/<>-@".Contains(c))
            {
                AddToken(TokenType.OP, c.ToString());
                _position++;
                _column++;
                continue;
            }

            // Number (numeric literal)
            if (char.IsDigit(c))
            {
                TokenizeNumber();
                continue;
            }

            // Name (identifier)
            if (char.IsLetter(c) || c == '_')
            {
                TokenizeName();
                continue;
            }

            // Unknown character - skip
            _position++;
            _column++;
        }

        AddToken(TokenType.ENDMARKER, "");
        return _tokens;
    }

    private void SkipWhitespace()
    {
        while (_position < _source.Length)
        {
            char c = _source[_position];
            if (c == ' ' || c == '\t')
            {
                _position++;
                _column++;
            }
            else
            {
                break;
            }
        }
    }

    private void SkipComment()
    {
        // Skip until newline
        while (_position < _source.Length && _source[_position] != '\n')
        {
            _position++;
        }
    }

    private void TokenizeString()
    {
        char quoteChar = _source[_position];
        int startLine = _line;
        int startColumn = _column;

        // Check for triple-quoted string ('''...''' or """...""")
        bool isTripleQuoted = false;
        if (_position + 2 < _source.Length &&
            _source[_position + 1] == quoteChar &&
            _source[_position + 2] == quoteChar)
        {
            isTripleQuoted = true;
        }

        var sb = new StringBuilder();

        if (isTripleQuoted)
        {
            // Triple-quoted string: '''...''' or """..."""
            sb.Append(quoteChar);
            sb.Append(quoteChar);
            sb.Append(quoteChar);
            _position += 3;
            _column += 3;

            // Read until closing triple quotes
            while (_position < _source.Length)
            {
                // Check for closing triple quotes
                if (_position + 2 < _source.Length &&
                    _source[_position] == quoteChar &&
                    _source[_position + 1] == quoteChar &&
                    _source[_position + 2] == quoteChar)
                {
                    // Found closing triple quotes
                    sb.Append(quoteChar);
                    sb.Append(quoteChar);
                    sb.Append(quoteChar);
                    _position += 3;
                    _column += 3;
                    break;
                }

                // Handle newlines within triple-quoted strings
                if (_source[_position] == '\n')
                {
                    sb.Append(_source[_position]);
                    _position++;
                    _line++;
                    _column = 0;
                }
                else
                {
                    sb.Append(_source[_position]);
                    _position++;
                    _column++;
                }
            }
        }
        else
        {
            // Single-quoted string: '...' or "..."
            _position++;
            _column++;

            sb.Append(quoteChar);  // Include quote in value for SOFT/HARD detection

            while (_position < _source.Length && _source[_position] != quoteChar)
            {
                // Handle escape sequences: \", \\, \n, etc.
                if (_source[_position] == '\\' && _position + 1 < _source.Length)
                {
                    sb.Append(_source[_position]);  // Append '\'
                    _position++;
                    _column++;

                    if (_position < _source.Length)
                    {
                        sb.Append(_source[_position]);  // Append escaped character
                        _position++;
                        _column++;
                    }
                }
                else
                {
                    sb.Append(_source[_position]);
                    _position++;
                    _column++;
                }
            }

            if (_position < _source.Length && _source[_position] == quoteChar)
            {
                sb.Append(quoteChar);
                _position++;
                _column++;
            }
        }

        AddToken(TokenType.STRING, sb.ToString(), startLine, startColumn);
    }

    private void TokenizeName()
    {
        int startLine = _line;
        int startColumn = _column;

        var sb = new StringBuilder();
        while (_position < _source.Length)
        {
            char c = _source[_position];
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                sb.Append(c);
                _position++;
                _column++;
            }
            else
            {
                break;
            }
        }

        AddToken(TokenType.NAME, sb.ToString(), startLine, startColumn);
    }

    private void TokenizeNumber()
    {
        // Tokenize numeric literals (integers and floats)
        // Examples: 5, 6, 3.14, 1e10
        int startLine = _line;
        int startColumn = _column;

        var sb = new StringBuilder();

        // Read integer part
        while (_position < _source.Length && char.IsDigit(_source[_position]))
        {
            sb.Append(_source[_position]);
            _position++;
            _column++;
        }

        // Check for decimal point
        if (_position < _source.Length && _source[_position] == '.')
        {
            // Lookahead: only consume '.' if followed by digit (not for gather patterns like "5.foo")
            if (_position + 1 < _source.Length && char.IsDigit(_source[_position + 1]))
            {
                sb.Append(_source[_position]);
                _position++;
                _column++;

                // Read fractional part
                while (_position < _source.Length && char.IsDigit(_source[_position]))
                {
                    sb.Append(_source[_position]);
                    _position++;
                    _column++;
                }
            }
        }

        // Check for exponent (e or E)
        if (_position < _source.Length && (_source[_position] == 'e' || _source[_position] == 'E'))
        {
            sb.Append(_source[_position]);
            _position++;
            _column++;

            // Optional sign
            if (_position < _source.Length && (_source[_position] == '+' || _source[_position] == '-'))
            {
                sb.Append(_source[_position]);
                _position++;
                _column++;
            }

            // Exponent digits
            while (_position < _source.Length && char.IsDigit(_source[_position]))
            {
                sb.Append(_source[_position]);
                _position++;
                _column++;
            }
        }

        AddToken(TokenType.NUMBER, sb.ToString(), startLine, startColumn);
    }

    private void AddToken(TokenType type, string value, int? line = null, int? column = null)
    {
        _tokens.Add(new GrammarToken
        {
            Type = type,
            Value = value,
            Line = line ?? _line,
            Column = column ?? _column
        });
    }
}
