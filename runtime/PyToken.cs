using System;

namespace SharpPy
{
    #region Token System

    /// <summary>
    /// CPython 3.12 compatible token type definitions
    /// Auto-generated equivalent to CPython's Include/internal/pycore_token.h
    /// </summary>
    public enum TokenType
    {
        // Basic tokens (0-26)
        ENDMARKER = 0,
        NAME = 1,
        NUMBER = 2,
        STRING = 3,
        NEWLINE = 4,
        INDENT = 5,
        DEDENT = 6,
        LPAR = 7,           // (
        RPAR = 8,           // )
        LSQB = 9,           // [
        RSQB = 10,          // ]
        COLON = 11,         // :
        COMMA = 12,         // ,
        SEMI = 13,          // ;
        PLUS = 14,          // +
        MINUS = 15,         // -
        STAR = 16,          // *
        SLASH = 17,         // /
        VBAR = 18,          // |
        AMPER = 19,         // &
        LESS = 20,          // <
        GREATER = 21,       // >
        EQUAL = 22,         // =
        DOT = 23,           // .
        PERCENT = 24,       // %
        LBRACE = 25,        // {
        RBRACE = 26,        // }

        // Comparison and assignment operators (27-46)
        EQEQUAL = 27,       // ==
        NOTEQUAL = 28,      // !=
        LESSEQUAL = 29,     // <=
        GREATEREQUAL = 30,  // >=
        TILDE = 31,         // ~
        CIRCUMFLEX = 32,    // ^
        LEFTSHIFT = 33,     // <<
        RIGHTSHIFT = 34,    // >>
        DOUBLESTAR = 35,    // **
        PLUSEQUAL = 36,     // +=
        MINEQUAL = 37,      // -=
        STAREQUAL = 38,     // *=
        SLASHEQUAL = 39,    // /=
        PERCENTEQUAL = 40,  // %=
        AMPEREQUAL = 41,    // &=
        VBAREQUAL = 42,     // |=
        CIRCUMFLEXEQUAL = 43, // ^=
        LEFTSHIFTEQUAL = 44,  // <<=
        RIGHTSHIFTEQUAL = 45, // >>=
        DOUBLESTAREQUAL = 46, // **=

        // Additional operators and special tokens (47-63)
        DOUBLESLASH = 47,   // //
        DOUBLESLASHEQUAL = 48, // //=
        AT = 49,            // @
        ATEQUAL = 50,       // @=
        RARROW = 51,        // ->
        ELLIPSIS = 52,      // ...
        COLONEQUAL = 53,    // :=
        EXCLAMATION = 54,   // !
        OP = 55,
        AWAIT = 56,
        ASYNC = 57,
        TYPE_IGNORE = 58,
        TYPE_COMMENT = 59,
        SOFT_KEYWORD = 60,
        FSTRING_START = 61,
        FSTRING_MIDDLE = 62,
        FSTRING_END = 63,

        // Additional tokens for tokenize module compatibility
        COMMENT = 64,
        NL = 65,           // Non-logical newline
        ERRORTOKEN = 66,
        ENCODING = 67,
        N_TOKENS = 68,

        // Special constants
        NT_OFFSET = 256,

        // Python keywords (handled as NAME tokens with special lexeme values)
        // These will be identified by lexeme content, not separate token types
        // Keywords: and, as, assert, async, await, break, case, class, continue,
        //          def, del, elif, else, except, False, finally, for, from,
        //          global, if, import, in, is, lambda, match, None, nonlocal,
        //          not, or, pass, raise, return, True, try, type, while, with, yield

        // Compound operators (handled as combinations)
        // NOT_IN => parsed as NOT + IN
        // IS_NOT => parsed as IS + NOT
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

        public bool IsOperator()
        {
            return Type >= TokenType.PLUS && Type <= TokenType.DOUBLESTAREQUAL ||
                   Type == TokenType.DOUBLESLASH || Type == TokenType.DOUBLESLASHEQUAL ||
                   Type == TokenType.AT || Type == TokenType.ATEQUAL ||
                   Type == TokenType.RARROW || Type == TokenType.COLONEQUAL ||
                   Type == TokenType.EXCLAMATION;
        }

        public bool IsBinaryOperator()
        {
            return Type == TokenType.PLUS || Type == TokenType.MINUS ||
                   Type == TokenType.STAR || Type == TokenType.SLASH ||
                   Type == TokenType.DOUBLESLASH || Type == TokenType.DOUBLESTAR ||
                   Type == TokenType.PERCENT || Type == TokenType.AMPER ||
                   Type == TokenType.VBAR || Type == TokenType.CIRCUMFLEX ||
                   Type == TokenType.LEFTSHIFT || Type == TokenType.RIGHTSHIFT;
        }

        public bool IsComparisonOperator()
        {
            return Type == TokenType.EQEQUAL || Type == TokenType.NOTEQUAL ||
                   Type == TokenType.LESS || Type == TokenType.LESSEQUAL ||
                   Type == TokenType.GREATER || Type == TokenType.GREATEREQUAL ||
                   (Type == TokenType.NAME && (Lexeme == "is" || Lexeme == "in"));
        }

        public bool IsUnaryOperator()
        {
            return Type == TokenType.PLUS || Type == TokenType.MINUS ||
                   Type == TokenType.TILDE || (Type == TokenType.NAME && Lexeme == "not");
        }

        public bool IsAssignmentOperator()
        {
            return Type == TokenType.EQUAL || Type == TokenType.COLONEQUAL;
        }

        /// <summary>
        /// 연산자 우선순위를 반환 (높을수록 우선순위가 높음)
        /// CPython 3.12 compatible precedence
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

            return Type switch
            {
                TokenType.LESS or TokenType.LESSEQUAL or
                TokenType.GREATER or TokenType.GREATEREQUAL or
                TokenType.EQEQUAL or TokenType.NOTEQUAL => 4,
                TokenType.VBAR => 5,
                TokenType.CIRCUMFLEX => 6,
                TokenType.AMPER => 7,
                TokenType.LEFTSHIFT or TokenType.RIGHTSHIFT => 8,
                TokenType.PLUS or TokenType.MINUS => 9,
                TokenType.STAR or TokenType.SLASH or
                TokenType.DOUBLESLASH or TokenType.PERCENT => 10,
                TokenType.TILDE => 11,  // Unary operators
                TokenType.DOUBLESTAR => 12,  // Exponentiation (right-associative)
                _ => 0
            };
        }

        /// <summary>
        /// 오른쪽 결합 연산자인지 확인
        /// </summary>
        public bool IsRightAssociative()
        {
            return Type == TokenType.DOUBLESTAR;  // ** is right-associative in Python
        }
    }

    #endregion
}