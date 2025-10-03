using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace SharpPy.PegGenerator.Grammar
{
    /// <summary>
    /// Token types for PEG grammar files
    /// </summary>
    public enum GrammarTokenType
    {
        // Literals and identifiers
        NAME,           // rule_name, identifier
        STRING,         // 'string literal'
        ACTION,         // { action code }

        // Operators
        COLON,          // :
        PIPE,           // |
        STAR,           // *
        PLUS,           // +
        QUESTION,       // ?
        AMPERSAND,      // &
        EXCLAMATION,    // !
        TILDE,          // ~ (cut operator)
        EQUAL,          // =
        DOT,            // . (gather separator)

        // Delimiters
        LPAR,           // (
        RPAR,           // )
        LSQB,           // [
        RSQB,           // ]

        // Special
        NEWLINE,
        INDENT,
        DEDENT,
        ENDMARKER,
        COMMENT,

        // Invalid
        INVALID
    }

    /// <summary>
    /// A token in the grammar file
    /// </summary>
    public class GrammarToken
    {
        public GrammarTokenType Type { get; set; }
        public string Value { get; set; } = "";
        public int Line { get; set; }
        public int Column { get; set; }

        public override string ToString()
        {
            return $"{Type}({Value}) at {Line}:{Column}";
        }
    }

    /// <summary>
    /// Lexer for PEG grammar files (.gram) - Python-style tokenization
    /// </summary>
    public class GrammarLexer
    {
        private readonly string _input;
        private int _position;
        private int _line;
        private int _column;
        private readonly Stack<int> _indentStack = new();
        private bool _atLineStart = true;
        private readonly Queue<GrammarToken> _pendingTokens = new();

        public GrammarLexer(string input)
        {
            _input = input;
            _position = 0;
            _line = 1;
            _column = 1;
            _indentStack.Push(0); // Initial indent level
        }

        /// <summary>
        /// Tokenize the entire input
        /// </summary>
        public List<GrammarToken> Tokenize()
        {
            var tokens = new List<GrammarToken>();

            while (_position < _input.Length)
            {
                var token = NextToken();
                if (token != null)
                {
                    tokens.Add(token);
                }
            }

            // Add final ENDMARKER
            tokens.Add(CreateToken(GrammarTokenType.ENDMARKER, ""));

            return tokens;
        }

        /// <summary>
        /// Get the next token - Python-style tokenization
        /// </summary>
        public GrammarToken? NextToken()
        {
            // Return pending tokens first (for INDENT/DEDENT)
            if (_pendingTokens.Count > 0)
            {
                return _pendingTokens.Dequeue();
            }

            if (_position >= _input.Length)
            {
                // Generate DEDENT tokens for remaining indentation levels
                if (_indentStack.Count > 1)
                {
                    _indentStack.Pop();
                    return CreateToken(GrammarTokenType.DEDENT, "");
                }
                return null;
            }

            var ch = _input[_position];

            // Handle line start (indentation)
            if (_atLineStart)
            {
                _atLineStart = false;
                return HandleIndentation();
            }

            // Skip whitespace (except newlines)
            while (_position < _input.Length && (ch == ' ' || ch == '\t'))
            {
                Advance(1);
                if (_position < _input.Length)
                    ch = _input[_position];
                else
                    break;
            }

            if (_position >= _input.Length)
                return null;

            // Handle newlines
            if (ch == '\n' || ch == '\r')
            {
                return HandleNewline();
            }

            // Handle comments
            if (ch == '#')
            {
                return HandleComment();
            }

            // Handle string literals (single or double quoted)
            if (ch == '\'' || ch == '"')
            {
                return HandleStringLiteral();
            }

            // Handle action blocks
            if (ch == '{')
            {
                return HandleActionBlock();
            }

            // Handle names (identifiers)
            if (char.IsLetter(ch) || ch == '_')
            {
                return HandleName();
            }

            // Handle single-character operators
            var tokenType = ch switch
            {
                ':' => GrammarTokenType.COLON,
                '|' => GrammarTokenType.PIPE,
                '*' => GrammarTokenType.STAR,
                '+' => GrammarTokenType.PLUS,
                '?' => GrammarTokenType.QUESTION,
                '&' => GrammarTokenType.AMPERSAND,
                '!' => GrammarTokenType.EXCLAMATION,
                '=' => GrammarTokenType.EQUAL,
                '(' => GrammarTokenType.LPAR,
                ')' => GrammarTokenType.RPAR,
                '[' => GrammarTokenType.LSQB,
                ']' => GrammarTokenType.RSQB,
                '~' => GrammarTokenType.TILDE, // Cut operator in PEG
                '$' => GrammarTokenType.COLON, // End marker in PEG (map to colon for now)
                '.' => GrammarTokenType.DOT, // Separator in gather pattern (e.g., ','.expression+)
                '@' => GrammarTokenType.NAME, // Directive marker (treat as name for now)
                '/' => GrammarTokenType.PIPE, // Alternative operator in some grammars
                ',' => GrammarTokenType.PIPE, // Comma separator (treat as pipe for now)
                ';' => GrammarTokenType.PIPE, // Semicolon separator (treat as pipe for now)
                '}' => GrammarTokenType.RPAR, // Closing brace (treat as right paren for now)
                _ => GrammarTokenType.INVALID
            };

            if (tokenType != GrammarTokenType.INVALID)
            {
                var token = CreateToken(tokenType, ch.ToString());
                Advance(1);
                return token;
            }

            // Unknown character
            Console.WriteLine($"[DEBUG] INVALID token at position {_position}: '{ch}' (char code: {(int)ch})");
            var invalidToken = CreateToken(GrammarTokenType.INVALID, ch.ToString());
            Advance(1);
            return invalidToken;
        }

        private GrammarToken HandleIndentation()
        {
            var startPos = _position;
            var indent = 0;

            // Count leading whitespace
            while (_position < _input.Length && (_input[_position] == ' ' || _input[_position] == '\t'))
            {
                if (_input[_position] == ' ')
                    indent++;
                else if (_input[_position] == '\t')
                    indent += 8; // Tab = 8 spaces
                _position++;
                _column++;
            }

            // Skip empty lines and comments
            if (_position < _input.Length && (_input[_position] == '\n' || _input[_position] == '\r' || _input[_position] == '#'))
            {
                return NextToken()!; // Skip this line and continue
            }

            var currentIndent = _indentStack.Peek();

            if (indent > currentIndent)
            {
                // Increased indentation
                _indentStack.Push(indent);
                return CreateToken(GrammarTokenType.INDENT, _input[startPos.._position]);
            }
            else if (indent < currentIndent)
            {
                // Decreased indentation - generate DEDENT tokens
                while (_indentStack.Count > 1 && _indentStack.Peek() > indent)
                {
                    _indentStack.Pop();
                    _pendingTokens.Enqueue(CreateToken(GrammarTokenType.DEDENT, ""));
                }

                if (_indentStack.Peek() != indent)
                {
                    // Indentation error
                    Console.WriteLine($"[ERROR] Indentation mismatch at line {_line}");
                    return CreateToken(GrammarTokenType.INVALID, "");
                }

                // Return first DEDENT, others are queued
                if (_pendingTokens.Count > 0)
                    return _pendingTokens.Dequeue();
            }

            // Same indentation level - continue with next token
            return NextToken()!;
        }

        private GrammarToken HandleNewline()
        {
            var startPos = _position;

            // Skip newline characters
            while (_position < _input.Length && (_input[_position] == '\n' || _input[_position] == '\r'))
            {
                if (_input[_position] == '\n')
                {
                    _line++;
                    _column = 1;
                    _atLineStart = true;
                }
                _position++;
            }

            return CreateToken(GrammarTokenType.NEWLINE, _input[startPos.._position]);
        }

        private GrammarToken HandleComment()
        {
            var startPos = _position;

            // Read until end of line
            while (_position < _input.Length && _input[_position] != '\n' && _input[_position] != '\r')
            {
                _position++;
                _column++;
            }

            return CreateToken(GrammarTokenType.COMMENT, _input[startPos.._position]);
        }

        private GrammarToken HandleStringLiteral()
        {
            var startPos = _position;
            var quoteChar = _input[_position]; // Remember if it's ' or "

            // Check for triple quotes
            bool isTripleQuote = false;
            if (_position + 2 < _input.Length &&
                _input[_position + 1] == quoteChar &&
                _input[_position + 2] == quoteChar)
            {
                isTripleQuote = true;
                _position += 3; // Skip opening triple quote
                _column += 3;
            }
            else
            {
                _position++; // Skip opening quote
                _column++;
            }

            if (isTripleQuote)
            {
                // Handle triple quoted strings - read until closing triple quote
                while (_position + 2 < _input.Length)
                {
                    if (_input[_position] == quoteChar &&
                        _input[_position + 1] == quoteChar &&
                        _input[_position + 2] == quoteChar)
                    {
                        _position += 3; // Skip closing triple quote
                        _column += 3;
                        break;
                    }

                    if (_input[_position] == '\n')
                    {
                        _line++;
                        _column = 1;
                    }
                    else
                    {
                        _column++;
                    }
                    _position++;
                }
            }
            else
            {
                // Handle regular quoted strings
                while (_position < _input.Length && _input[_position] != quoteChar)
                {
                    if (_input[_position] == '\\' && _position + 1 < _input.Length)
                    {
                        // Handle escape sequences
                        _position += 2;
                        _column += 2;
                    }
                    else if (_input[_position] == '\n')
                    {
                        // Single quoted strings shouldn't span lines unless escaped
                        Console.WriteLine($"[ERROR] Unterminated string literal at line {_line}");
                        return CreateToken(GrammarTokenType.INVALID, _input[startPos.._position]);
                    }
                    else
                    {
                        _position++;
                        _column++;
                    }
                }

                if (_position < _input.Length && _input[_position] == quoteChar)
                {
                    _position++; // Skip closing quote
                    _column++;
                }
                else
                {
                    Console.WriteLine($"[ERROR] Unterminated string literal at line {_line}");
                    return CreateToken(GrammarTokenType.INVALID, _input[startPos.._position]);
                }
            }

            return CreateToken(GrammarTokenType.STRING, _input[startPos.._position]);
        }

        private GrammarToken HandleActionBlock()
        {
            var startPos = _position;
            var braceCount = 0;

            do
            {
                if (_input[_position] == '{')
                {
                    braceCount++;
                }
                else if (_input[_position] == '}')
                {
                    braceCount--;
                }
                else if (_input[_position] == '"' || _input[_position] == '\'')
                {
                    // Skip over string literals within action blocks
                    var quoteChar = _input[_position];
                    _position++;
                    _column++;

                    while (_position < _input.Length && _input[_position] != quoteChar)
                    {
                        if (_input[_position] == '\\' && _position + 1 < _input.Length)
                        {
                            // Skip escape sequence
                            _position += 2;
                            _column += 2;
                        }
                        else if (_input[_position] == '\n')
                        {
                            _line++;
                            _column = 1;
                            _position++;
                        }
                        else
                        {
                            _position++;
                            _column++;
                        }
                    }

                    if (_position < _input.Length && _input[_position] == quoteChar)
                    {
                        _position++; // Skip closing quote
                        _column++;
                    }
                    continue;
                }
                else if (_input[_position] == '\n')
                {
                    _line++;
                    _column = 1;
                    _position++;
                    continue;
                }

                _position++;
                _column++;
            }
            while (_position < _input.Length && braceCount > 0);

            if (braceCount > 0)
            {
                Console.WriteLine($"[ERROR] Unterminated action block at line {_line}");
                return CreateToken(GrammarTokenType.INVALID, _input[startPos.._position]);
            }

            return CreateToken(GrammarTokenType.ACTION, _input[startPos.._position]);
        }

        private GrammarToken HandleName()
        {
            var startPos = _position;

            // Read identifier characters
            while (_position < _input.Length &&
                   (char.IsLetterOrDigit(_input[_position]) || _input[_position] == '_'))
            {
                _position++;
                _column++;
            }

            var value = _input[startPos.._position];
            return CreateToken(GrammarTokenType.NAME, value);
        }

        private GrammarToken CreateToken(GrammarTokenType type, string value)
        {
            return new GrammarToken
            {
                Type = type,
                Value = value,
                Line = _line,
                Column = _column
            };
        }

        private void Advance(int count)
        {
            for (int i = 0; i < count && _position < _input.Length; i++)
            {
                if (_input[_position] == '\n')
                {
                    _line++;
                    _column = 1;
                }
                else
                {
                    _column++;
                }
                _position++;
            }
        }

        private char CurrentChar => _position < _input.Length ? _input[_position] : '\0';
        private char PeekChar(int offset = 1) => _position + offset < _input.Length ? _input[_position + offset] : '\0';
    }
}