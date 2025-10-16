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
                sb.Append(_source[_position]);
                _position++;
                _column++;
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
