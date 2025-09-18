using System;

namespace SharpPy
{
    #region Token System

    /// <summary>
    /// CPython 3.12 compatible token type definitions - Unified OP Token System
    /// All operators and delimiters are represented as OP tokens with lexeme-based identification
    /// </summary>
    public enum TokenType
    {
        // Core tokens - CPython 3.12 standard
        ENDMARKER = 0,
        NAME = 1,
        NUMBER = 2,
        STRING = 3,
        BYTES = 4,         // Binary string literals (b'...', b"...")
        NEWLINE = 5,
        INDENT = 6,
        DEDENT = 7,

        // Unified operator token - CPython 3.12 style
        // All operators, delimiters, brackets are OP tokens identified by lexeme:
        // Operators: +, -, *, /, //, **, %, &, |, ^, <<, >>, ~, @
        // Comparisons: ==, !=, <, <=, >, >=
        // Assignments: =, :=, +=, -=, *=, /=, //=, %=, **=, &=, |=, ^=, <<=, >>=, @=
        // Delimiters: (, ), [, ], {, }, :, ,, ;, ., ->, ..., !
        OP = 55,

        // Python 3.12 async/await support
        AWAIT = 56,
        ASYNC = 57,

        // Type system support
        TYPE_IGNORE = 58,
        TYPE_COMMENT = 59,
        SOFT_KEYWORD = 60,

        // Enhanced F-strings (PEP 701)
        FSTRING_START = 61,
        FSTRING_MIDDLE = 62,
        FSTRING_END = 63,

        // Tokenize module compatibility
        COMMENT = 64,
        NL = 65,           // Non-logical newline
        ERRORTOKEN = 66,
        ENCODING = 67,
        N_TOKENS = 68,

        // Special constants
        NT_OFFSET = 256,

        // Python keywords are handled as NAME tokens with specific lexeme values:
        // Keywords: and, as, assert, async, await, break, case, class, continue,
        //          def, del, elif, else, except, False, finally, for, from,
        //          global, if, import, in, is, lambda, match, None, nonlocal,
        //          not, or, pass, raise, return, True, try, type, while, with, yield

        // Compound operators are handled as separate OP tokens:
        // NOT_IN => parsed as OP("not") + OP("in")
        // IS_NOT => parsed as OP("is") + OP("not")
    }

    /// <summary>
    /// 토큰 클래스
    /// </summary>
    public class PyToken
    {
        public TokenType Type { get; }
        public string Lexeme { get; }
        public int Line { get; }
        public int Column { get; }

        public PyToken(TokenType type, string lexeme, int line, int column)
        {
            Type = type;
            Lexeme = lexeme ?? throw new ArgumentNullException(nameof(lexeme));
            Line = line;
            Column = column;
        }

        public override string ToString()
        {
            return $"{Type}({Lexeme}) at {Line}:{Column}";
        }

        public override bool Equals(object? obj)
        {
            if (obj is PyToken other)
            {
                return Type == other.Type && Lexeme == other.Lexeme;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Type, Lexeme);
        }

        // Helper methods for token checking (CPython 3.12 compatible)
        public bool IsKeyword()
        {
            // In CPython 3.12, keywords are NAME tokens with specific lexeme values
            return Type == TokenType.NAME && IsKeywordLexeme(Lexeme);
        }

        public bool IsLiteral()
        {
            return Type == TokenType.NUMBER || Type == TokenType.STRING || IsKeywordLiteral();
        }

        private bool IsKeywordLiteral()
        {
            return Type == TokenType.NAME && (Lexeme == "True" || Lexeme == "False" || Lexeme == "None");
        }

        private static readonly HashSet<string> KeywordLexemes = new()
        {
            "and", "as", "assert", "async", "await", "break", "case", "class", "continue",
            "def", "del", "elif", "else", "except", "False", "finally", "for", "from",
            "global", "if", "import", "in", "is", "lambda", "match", "None", "nonlocal",
            "not", "or", "pass", "raise", "return", "True", "try", "type", "while", "with", "yield"
        };

        private static bool IsKeywordLexeme(string lexeme)
        {
            return KeywordLexemes.Contains(lexeme);
        }

        /// <summary>
        /// CPython 3.12: Check if token is an operator (all operators are OP tokens)
        /// </summary>
        public bool IsOperator()
        {
            return Type == TokenType.OP;
        }

        /// <summary>
        /// CPython 3.12: Check if OP token is a binary operator
        /// </summary>
        public bool IsBinaryOperator()
        {
            if (Type != TokenType.OP) return false;
            return Lexeme switch
            {
                "+" or "-" or "*" or "/" or "//" or "**" or "%" or
                "&" or "|" or "^" or "<<" or ">>" => true,
                _ => false
            };
        }

        /// <summary>
        /// CPython 3.12: Check if token is a comparison operator
        /// </summary>
        public bool IsComparisonOperator()
        {
            if (Type == TokenType.OP)
            {
                return Lexeme switch
                {
                    "==" or "!=" or "<" or "<=" or ">" or ">=" => true,
                    _ => false
                };
            }
            return Type == TokenType.NAME && (Lexeme == "is" || Lexeme == "in" || Lexeme == "not");
        }

        /// <summary>
        /// CPython 3.12: Check if token is a unary operator
        /// </summary>
        public bool IsUnaryOperator()
        {
            if (Type == TokenType.OP)
            {
                return Lexeme switch
                {
                    "+" or "-" or "~" => true,
                    _ => false
                };
            }
            return Type == TokenType.NAME && Lexeme == "not";
        }

        /// <summary>
        /// CPython 3.12: Check if token is an assignment operator
        /// </summary>
        public bool IsAssignmentOperator()
        {
            if (Type == TokenType.OP)
            {
                return Lexeme switch
                {
                    "=" or ":=" or "+=" or "-=" or "*=" or "/=" or "//=" or "%=" or
                    "**=" or "&=" or "|=" or "^=" or "<<=" or ">>=" or "@=" => true,
                    _ => false
                };
            }
            return false;
        }

        /// <summary>
        /// 연산자 우선순위를 반환 (높을수록 우선순위가 높음)
        /// CPython 3.12 compatible precedence - OP token based
        /// </summary>
        public int GetPrecedence()
        {
            // Handle keyword operators
            if (Type == TokenType.NAME)
            {
                return Lexeme switch
                {
                    "or" => 1,
                    "and" => 2,
                    "not" => 3,
                    "in" or "is" => 4,
                    _ => 0
                };
            }

            // CPython 3.12: All operators are OP tokens, distinguished by lexeme
            if (Type == TokenType.OP)
            {
                return Lexeme switch
                {
                    // Comparison operators
                    "<" or "<=" or ">" or ">=" or "==" or "!=" => 4,
                    // Bitwise OR
                    "|" => 5,
                    // Bitwise XOR
                    "^" => 6,
                    // Bitwise AND
                    "&" => 7,
                    // Shifts
                    "<<" or ">>" => 8,
                    // Addition and subtraction
                    "+" or "-" => 9,
                    // Multiplication, division, modulo
                    "*" or "/" or "//" or "%" => 10,
                    // Unary operators
                    "~" => 11,
                    // Exponentiation (right-associative)
                    "**" => 12,
                    _ => 0
                };
            }

            return 0; // Default precedence
        }

        /// <summary>
        /// CPython 3.12: 오른쪽 결합 연산자인지 확인 (OP token based)
        /// </summary>
        public bool IsRightAssociative()
        {
            return Type == TokenType.OP && Lexeme == "**";  // ** is right-associative in Python
        }
    }

    #endregion
}