using System;

namespace SharpPy
{
    #region Token System

    /// <summary>
    /// 토큰 타입 열거형
    /// </summary>
    public enum TokenType
    {
        // Literals
        INTEGER,
        FLOAT,
        COMPLEX,      // 3+4j
        STRING,       // "regular"
        RAW_STRING,   // r"raw"
        BYTES_STRING, // b"bytes"
        F_STRING,     // f"formatted"
        IDENTIFIER,

        // Keywords
        AND, AS, ASSERT, ASYNC, AWAIT, BREAK, CASE, CLASS, CONTINUE,
        DEF, DEL, ELIF, ELSE, EXCEPT, FALSE, FINALLY, FOR, FROM,
        GLOBAL, IF, IMPORT, IN, IS, LAMBDA, MATCH, NONE, NONLOCAL,
        NOT, OR, PASS, RAISE, RETURN, TRUE, TRY, TYPE, WHILE, WITH, YIELD,
        
        // CPython 3.12: Compound operators
        NOT_IN,     // not in
        IS_NOT,     // is not

        // Single character tokens
        LEFT_PAREN,    // (
        RIGHT_PAREN,   // )
        LEFT_BRACKET,  // [
        RIGHT_BRACKET, // ]
        LEFT_BRACE,    // {
        RIGHT_BRACE,   // }
        COMMA,         // ,
        DOT,           // .
        SEMICOLON,     // ;
        COLON,         // :
        AT,            // @

        // Operators
        PLUS,          // +
        MINUS,         // -
        STAR,          // *
        STAR_STAR,     // **
        SLASH,         // /
        SLASH_SLASH,   // //
        PERCENT,       // %
        AMPERSAND,     // &
        PIPE,          // |
        CARET,         // ^
        TILDE,         // ~
        LEFT_SHIFT,    // <<
        RIGHT_SHIFT,   // >>

        // Comparison
        EQUAL,         // =
        EQUAL_EQUAL,   // ==
        BANG,          // !
        BANG_EQUAL,    // !=
        LESS,          // <
        LESS_EQUAL,    // <=
        GREATER,       // >
        GREATER_EQUAL, // >=
        WALRUS,        // := (Python 3.8+)

        // Augmented Assignment (CPython style)
        PLUS_EQUAL,    // +=
        MINUS_EQUAL,   // -=
        STAR_EQUAL,    // *=
        SLASH_EQUAL,   // /=
        PERCENT_EQUAL, // %=
        STAR_STAR_EQUAL, // **=
        SLASH_SLASH_EQUAL, // //=
        AMPERSAND_EQUAL, // &=
        PIPE_EQUAL,    // |=
        CARET_EQUAL,   // ^=
        LEFT_SHIFT_EQUAL,  // <<=
        RIGHT_SHIFT_EQUAL, // >>=

        // Special
        NEWLINE,
        NL,          // Non-logical newline (empty lines)
        INDENT,
        DEDENT,
        EOF
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

        // Helper methods for token checking
        public bool IsKeyword()
        {
            return Type >= TokenType.AND && Type <= TokenType.YIELD;
        }

        public bool IsLiteral()
        {
            return Type == TokenType.INTEGER || Type == TokenType.FLOAT || 
                   Type == TokenType.STRING || Type == TokenType.TRUE || 
                   Type == TokenType.FALSE || Type == TokenType.NONE;
        }

        public bool IsOperator()
        {
            return Type >= TokenType.PLUS && Type <= TokenType.WALRUS;
        }

        public bool IsBinaryOperator()
        {
            return Type == TokenType.PLUS || Type == TokenType.MINUS ||
                   Type == TokenType.STAR || Type == TokenType.SLASH ||
                   Type == TokenType.SLASH_SLASH || Type == TokenType.STAR_STAR ||
                   Type == TokenType.PERCENT || Type == TokenType.AMPERSAND ||
                   Type == TokenType.PIPE || Type == TokenType.CARET ||
                   Type == TokenType.LEFT_SHIFT || Type == TokenType.RIGHT_SHIFT;
        }

        public bool IsComparisonOperator()
        {
            return Type == TokenType.EQUAL_EQUAL || Type == TokenType.BANG_EQUAL ||
                   Type == TokenType.LESS || Type == TokenType.LESS_EQUAL ||
                   Type == TokenType.GREATER || Type == TokenType.GREATER_EQUAL ||
                   Type == TokenType.IS || Type == TokenType.IN;
        }

        public bool IsUnaryOperator()
        {
            return Type == TokenType.PLUS || Type == TokenType.MINUS ||
                   Type == TokenType.NOT || Type == TokenType.TILDE;
        }

        public bool IsAssignmentOperator()
        {
            return Type == TokenType.EQUAL || Type == TokenType.WALRUS;
        }

        /// <summary>
        /// 연산자 우선순위를 반환 (높을수록 우선순위가 높음)
        /// </summary>
        public int GetPrecedence()
        {
            return Type switch
            {
                TokenType.OR => 1,
                TokenType.AND => 2,
                TokenType.NOT => 3,
                TokenType.IN or TokenType.IS or 
                TokenType.LESS or TokenType.LESS_EQUAL or
                TokenType.GREATER or TokenType.GREATER_EQUAL or
                TokenType.EQUAL_EQUAL or TokenType.BANG_EQUAL => 4,
                TokenType.PIPE => 5,
                TokenType.CARET => 6,
                TokenType.AMPERSAND => 7,
                TokenType.LEFT_SHIFT or TokenType.RIGHT_SHIFT => 8,
                TokenType.PLUS or TokenType.MINUS => 9,
                TokenType.STAR or TokenType.SLASH or 
                TokenType.SLASH_SLASH or TokenType.PERCENT => 10,
                TokenType.TILDE => 11,  // Unary operators
                TokenType.STAR_STAR => 12,  // Exponentiation (right-associative)
                _ => 0
            };
        }

        /// <summary>
        /// 오른쪽 결합 연산자인지 확인
        /// </summary>
        public bool IsRightAssociative()
        {
            return Type == TokenType.STAR_STAR;  // ** is right-associative in Python
        }
    }

    #endregion
}