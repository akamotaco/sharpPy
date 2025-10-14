namespace SharpPy.Generated
{
    public static class PyToken
    {
        /// <summary>
        /// CPython 3.12 compatible token types
        /// Explicit values to match CPython token indices
        /// </summary>
        public enum Type
        {
            ENDMARKER = 0,
            NAME = 1,
            NUMBER = 2,
            STRING = 3,
            NEWLINE = 4,
            INDENT = 5,
            DEDENT = 6,
            LPAR = 7,
            RPAR = 8,
            LSQB = 9,
            RSQB = 10,
            COLON = 11,
            COMMA = 12,
            SEMI = 13,
            PLUS = 14,
            MINUS = 15,
            STAR = 16,
            SLASH = 17,
            VBAR = 18,
            AMPER = 19,
            LESS = 20,
            GREATER = 21,
            EQUAL = 22,
            DOT = 23,
            PERCENT = 24,
            LBRACE = 25,
            RBRACE = 26,
            EQEQUAL = 27,
            NOTEQUAL = 28,
            LESSEQUAL = 29,
            GREATEREQUAL = 30,
            TILDE = 31,
            CIRCUMFLEX = 32,
            LEFTSHIFT = 33,
            RIGHTSHIFT = 34,
            DOUBLESTAR = 35,
            PLUSEQUAL = 36,
            MINEQUAL = 37,
            STAREQUAL = 38,
            SLASHEQUAL = 39,
            PERCENTEQUAL = 40,
            AMPEREQUAL = 41,
            VBAREQUAL = 42,
            CIRCUMFLEXEQUAL = 43,
            LEFTSHIFTEQUAL = 44,
            RIGHTSHIFTEQUAL = 45,
            DOUBLESTAREQUAL = 46,
            DOUBLESLASH = 47,
            DOUBLESLASHEQUAL = 48,
            AT = 49,
            ATEQUAL = 50,
            RARROW = 51,
            ELLIPSIS = 52,
            COLONEQUAL = 53,
            EXCLAMATION = 54,
            OP = 55,
            AWAIT = 56,
            ASYNC = 57,
            TYPE_IGNORE = 58,
            TYPE_COMMENT = 59,
            SOFT_KEYWORD = 60,
            FSTRING_START = 61,
            FSTRING_MIDDLE = 62,
            FSTRING_END = 63,
            COMMENT = 64,
            NL = 65,
            ERRORTOKEN = 66,
            ENCODING = 67,
        }

        public static readonly List<(string name, Type type)> Literals = new()
        {
            ( "(", Type.LPAR ),
            ( ")", Type.RPAR ),
            ( "[", Type.LSQB ),
            ( "]", Type.RSQB ),
            ( ":", Type.COLON ),
            ( ",", Type.COMMA ),
            ( ";", Type.SEMI ),
            ( "+", Type.PLUS ),
            ( "-", Type.MINUS ),
            ( "*", Type.STAR ),
            ( "/", Type.SLASH ),
            ( "|", Type.VBAR ),
            ( "&", Type.AMPER ),
            ( "<", Type.LESS ),
            ( ">", Type.GREATER ),
            ( "=", Type.EQUAL ),
            ( ".", Type.DOT ),
            ( "%", Type.PERCENT ),
            ( "{", Type.LBRACE ),
            ( "}", Type.RBRACE ),
            ( "==", Type.EQEQUAL ),
            ( "!=", Type.NOTEQUAL ),
            ( "<=", Type.LESSEQUAL ),
            ( ">=", Type.GREATEREQUAL ),
            ( "~", Type.TILDE ),
            ( "^", Type.CIRCUMFLEX ),
            ( "<<", Type.LEFTSHIFT ),
            ( ">>", Type.RIGHTSHIFT ),
            ( "**", Type.DOUBLESTAR ),
            ( "+=", Type.PLUSEQUAL ),
            ( "-=", Type.MINEQUAL ),
            ( "*=", Type.STAREQUAL ),
            ( "/=", Type.SLASHEQUAL ),
            ( "%=", Type.PERCENTEQUAL ),
            ( "&=", Type.AMPEREQUAL ),
            ( "|=", Type.VBAREQUAL ),
            ( "^=", Type.CIRCUMFLEXEQUAL ),
            ( "<<=", Type.LEFTSHIFTEQUAL ),
            ( ">>=", Type.RIGHTSHIFTEQUAL ),
            ( "**=", Type.DOUBLESTAREQUAL ),
            ( "//", Type.DOUBLESLASH ),
            ( "//=", Type.DOUBLESLASHEQUAL ),
            ( "@", Type.AT ),
            ( "@=", Type.ATEQUAL ),
            ( "->", Type.RARROW ),
            ( "...", Type.ELLIPSIS ),
            ( ":=", Type.COLONEQUAL ),
            ( "!", Type.EXCLAMATION ),
        };

    static public int GetLiteralIndex(string srcString, int srcPosition)
    {
        int index = -1;
        for(int i=0;i<Literals.Count;++i)
        {
            var lit = Literals[i];
            if (string.Compare(srcString, srcPosition, lit.name, 0, lit.name.Length) == 0)
                index = i;
        }
        return index;
    }

    }
}
