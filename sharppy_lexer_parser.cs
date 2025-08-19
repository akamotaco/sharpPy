// enhanced_lexer_parser.cs
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Runtime.CompilerServices;

namespace SharpPy
{
    public enum TokenType : byte
    {
        // Literals
        NUMBER, STRING, BOOLEAN, NONE, IDENTIFIER, FSTRING,
        // Keywords
        DEF, CLASS, IF, ELSE, ELIF, FOR, WHILE, IN, IS, BREAK, CONTINUE,
        TRY, EXCEPT, FINALLY, RAISE, IMPORT, FROM, AS, RETURN, AND, OR, NOT, LAMBDA,
        WITH, DEL, PASS, GLOBAL, NONLOCAL,
        // Additional keywords (NEW)
        YIELD, ASSERT, ASYNC, AWAIT, MATCH, CASE,
        // Operators
        OPERATOR, ASSIGN, COMPOUND_ASSIGN, WALRUS, // Added WALRUS for :=
        // Delimiters
        LPAREN, RPAREN, LBRACKET, RBRACKET, LBRACE, RBRACE, COLON, COMMA, DOT,
        // Special
        NEWLINE, EOF, INDENT, DEDENT,
        // Decorators (NEW)
        AT,
    }

    public sealed class Token
    {
        public TokenType Type { get; }
        public string Value { get; }
        public int Line { get; }
        public int Column { get; }

        public Token(TokenType type, string value, int line = 1, int column = 1)
        {
            Type = type;
            Value = value;
            Line = line;
            Column = column;
        }

        public override string ToString() => $"Token({Type}, {Value}) at {Line}:{Column}";
    }

    public sealed class Lexer
    {
        private static readonly bool DEBUG_MODE = false;
        private const int TAB_SIZE = 8;
        
        // Consolidated keyword sets
        private static readonly HashSet<string> ContinuationKeywords = new HashSet<string>
        {
            "elif", "else", "except", "finally", "case"
        };

        private static readonly HashSet<string> Keywords = new HashSet<string>
        {
            "def", "class", "if", "else", "elif", "for", "while", "in", "is",
            "break", "continue", "try", "except", "finally", "raise", "import",
            "from", "as", "return", "and", "or", "not", "lambda", "with", "del", 
            "pass", "global", "nonlocal", "yield", "assert", "async", "await",
            "match", "case"
        };

        private static readonly HashSet<string> TwoCharOperators = new HashSet<string>
        {
            "+=", "-=", "*=", "/=", "%=", "==", "!=", "<=", ">=", "**", "->", 
            "//", "<<", ">>", "&=", "|=", "^=", ":=" // Added walrus and floor division
        };

        private static readonly HashSet<string> ThreeCharOperators = new HashSet<string>
        {
            "**=", "//=", "<<=", ">>="
        };

        private readonly string input;
        private readonly int inputLength;
        private int position;
        private char currentChar;
        private int line;
        private int column;
        private readonly Stack<int> indentStack;
        private bool atLineStart;
        private bool useTabs = false;
        private bool useSpaces = false;
        private bool mixedIndentWarning = false;

        public Lexer(string input)
        {
            this.input = input ?? "";
            this.inputLength = this.input.Length;
            position = 0;
            line = 1;
            column = 1;
            indentStack = new Stack<int>();
            indentStack.Push(0);
            atLineStart = true;
            currentChar = position < inputLength ? this.input[position] : '\0';
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Advance()
        {
            if (currentChar == '\n')
            {
                line++;
                column = 1;
                atLineStart = true;
            }
            else
            {
                column++;
            }

            position++;
            currentChar = position < inputLength ? input[position] : '\0';
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private char Peek(int offset = 1)
        {
            int pos = position + offset;
            return pos < inputLength ? input[pos] : '\0';
        }

        // Consolidated skip methods
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SkipWhitespace()
        {
            while (currentChar != '\0' && char.IsWhiteSpace(currentChar) && currentChar != '\n')
                Advance();
        }

        private void SkipIndentChars()
        {
            while (currentChar == ' ' || currentChar == '\t')
                Advance();
        }

        // Helper method to check if line contains significant code
        private bool IsSignificantLine()
        {
            int tempPos = position;
            while (tempPos < inputLength && (input[tempPos] == ' ' || input[tempPos] == '\t'))
                tempPos++;

            if (tempPos >= inputLength || input[tempPos] == '\n' || input[tempPos] == '#')
                return false;

            return true;
        }

        private string PeekNextKeyword()
        {
            int tempPos = position;
            while (tempPos < inputLength && (input[tempPos] == ' ' || input[tempPos] == '\t'))
                tempPos++;

            if (tempPos < inputLength && (char.IsLetter(input[tempPos]) || input[tempPos] == '_'))
            {
                var sb = new StringBuilder();
                while (tempPos < inputLength && (char.IsLetterOrDigit(input[tempPos]) || input[tempPos] == '_'))
                {
                    sb.Append(input[tempPos]);
                    tempPos++;
                }
                return sb.ToString();
            }
            return "";
        }

        private (int level, bool hasTab, bool hasSpace) CalculateIndentLevel()
        {
            int level = 0;
            int tempPos = position;
            bool hasTab = false;
            bool hasSpace = false;

            while (tempPos < inputLength)
            {
                char ch = input[tempPos];
                if (ch == ' ')
                {
                    level += 1;
                    hasSpace = true;
                }
                else if (ch == '\t')
                {
                    level = ((level / TAB_SIZE) + 1) * TAB_SIZE;
                    hasTab = true;
                }
                else
                {
                    break;
                }
                tempPos++;
            }

            return (level, hasTab, hasSpace);
        }

        // Consolidated indent handling
        private void HandleIndentation(List<Token> tokens, ref bool lastTokenWasNewline)
        {
            int tempPos = position;
            while (tempPos < inputLength && (input[tempPos] == ' ' || input[tempPos] == '\t'))
                tempPos++;

            if (tempPos >= inputLength || input[tempPos] == '\n' || input[tempPos] == '\r' || input[tempPos] == '#')
            {
                while (currentChar != '\0' && currentChar != '\n')
                    Advance();
                if (currentChar == '\n')
                    Advance();
                return;
            }

            var (indentLevel, hasTab, hasSpace) = CalculateIndentLevel();
            string nextKeyword = PeekNextKeyword();
            bool isContinuation = ContinuationKeywords.Contains(nextKeyword);

            if (hasTab && hasSpace && !mixedIndentWarning)
            {
                mixedIndentWarning = true;
            }

            SkipIndentChars();

            int currentIndent = indentStack.Peek();
            int tokenLine = line;
            int tokenColumn = column;

            if (isContinuation)
            {
                ProcessContinuationIndent(tokens, indentLevel, tokenLine, tokenColumn);
            }
            else
            {
                ProcessRegularIndent(tokens, indentLevel, currentIndent, tokenLine, tokenColumn);
            }

            atLineStart = false;
            lastTokenWasNewline = false;
        }

        private void ProcessContinuationIndent(List<Token> tokens, int indentLevel, int tokenLine, int tokenColumn)
        {
            bool foundLevel = indentStack.Any(level => level == indentLevel);

            if (!foundLevel)
            {
                var expected = string.Join(", ", indentStack.Reverse());
                throw new PythonException("IndentationError",
                    $"Unindent does not match any outer indentation level (expected one of: {expected}, got: {indentLevel})",
                    tokenLine, tokenColumn);
            }

            while (indentStack.Count > 0 && indentStack.Peek() > indentLevel)
            {
                indentStack.Pop();
                tokens.Add(new Token(TokenType.DEDENT, "", tokenLine, tokenColumn));
            }
        }

        private void ProcessRegularIndent(List<Token> tokens, int indentLevel, int currentIndent, int tokenLine, int tokenColumn)
        {
            if (indentLevel > currentIndent)
            {
                indentStack.Push(indentLevel);
                tokens.Add(new Token(TokenType.INDENT, "", tokenLine, tokenColumn));
            }
            else if (indentLevel < currentIndent)
            {
                bool foundLevel = indentStack.Any(level => level == indentLevel);

                if (!foundLevel)
                {
                    var expected = string.Join(", ", indentStack.Reverse());
                    throw new PythonException("IndentationError",
                        $"Unindent does not match any outer indentation level (expected one of: {expected}, got: {indentLevel})",
                        tokenLine, tokenColumn);
                }

                while (indentStack.Count > 1 && indentStack.Peek() > indentLevel)
                {
                    indentStack.Pop();
                    tokens.Add(new Token(TokenType.DEDENT, "", tokenLine, tokenColumn));
                }
            }
        }

        public List<Token> Tokenize()
        {
            var tokens = new List<Token>(256);
            bool lastTokenWasNewline = true;

            while (currentChar != '\0')
            {
                int tokenLine = line;
                int tokenColumn = column;

                // Handle line start
                if (atLineStart && currentChar != '\n')
                {
                    HandleIndentation(tokens, ref lastTokenWasNewline);
                    continue;
                }

                // Skip whitespace in middle of line
                if (!atLineStart && char.IsWhiteSpace(currentChar) && currentChar != '\n')
                {
                    SkipWhitespace();
                    continue;
                }

                // Handle comments
                if (currentChar == '#')
                {
                    while (currentChar != '\0' && currentChar != '\n')
                        Advance();
                    continue;
                }

                // Handle newlines
                if (currentChar == '\n')
                {
                    if (!lastTokenWasNewline && tokens.Count > 0 &&
                        tokens[tokens.Count - 1].Type != TokenType.INDENT &&
                        tokens[tokens.Count - 1].Type != TokenType.DEDENT)
                    {
                        tokens.Add(new Token(TokenType.NEWLINE, "\n", tokenLine, tokenColumn));
                        lastTokenWasNewline = true;
                    }
                    Advance();
                    continue;
                }

                lastTokenWasNewline = false;

                // Handle decorator @ (NEW)
                if (currentChar == '@')
                {
                    tokens.Add(new Token(TokenType.AT, "@", tokenLine, tokenColumn));
                    Advance();
                    continue;
                }

                // Handle numbers
                if (char.IsDigit(currentChar) || (currentChar == '.' && char.IsDigit(Peek())))
                {
                    tokens.Add(new Token(TokenType.NUMBER, ReadNumber(), tokenLine, tokenColumn));
                    continue;
                }

                // Handle strings (including triple quotes and f-strings)
                if (HandleString(tokens, tokenLine, tokenColumn))
                    continue;

                // Handle identifiers and keywords
                if (char.IsLetter(currentChar) || currentChar == '_')
                {
                    string identifier = ReadIdentifier();
                    TokenType tokenType = GetTokenType(identifier);
                    tokens.Add(new Token(tokenType, identifier, tokenLine, tokenColumn));
                    continue;
                }

                // Handle operators
                if (HandleOperator(tokens, tokenLine, tokenColumn))
                    continue;

                // Handle single character tokens
                HandleSingleCharToken(tokens, tokenLine, tokenColumn);
            }

            // Add remaining DEDENT tokens
            while (indentStack.Count > 1)
            {
                indentStack.Pop();
                tokens.Add(new Token(TokenType.DEDENT, "", line, column));
            }

            tokens.Add(new Token(TokenType.EOF, "", line, column));
            return tokens;
        }

        private bool HandleString(List<Token> tokens, int tokenLine, int tokenColumn)
        {
            // Check for triple quotes
            if (position + 2 < inputLength)
            {
                string threeChars = input.Substring(position, 3);
                if (threeChars == "'''" || threeChars == "\"\"\"")
                {
                    string content = ReadTripleQuotedString(threeChars.Substring(0, 1));
                    tokens.Add(new Token(TokenType.STRING, content, tokenLine, tokenColumn));
                    return true;
                }
            }

            // Check for f-strings
            if (currentChar == 'f' && position + 1 < inputLength &&
                (input[position + 1] == '"' || input[position + 1] == '\''))
            {
                if (position + 3 < inputLength)
                {
                    char quoteChar = input[position + 1];
                    if (input[position + 2] == quoteChar && input[position + 3] == quoteChar)
                    {
                        Advance();
                        string content = ReadTripleQuotedString(quoteChar.ToString());
                        tokens.Add(new Token(TokenType.FSTRING, content, tokenLine, tokenColumn));
                        return true;
                    }
                }

                Advance();
                char quote = currentChar;
                tokens.Add(new Token(TokenType.FSTRING, ReadString(quote), tokenLine, tokenColumn));
                return true;
            }

            // Regular strings
            if (currentChar == '"' || currentChar == '\'')
            {
                char quote = currentChar;
                tokens.Add(new Token(TokenType.STRING, ReadString(quote), tokenLine, tokenColumn));
                return true;
            }

            return false;
        }

        private bool HandleOperator(List<Token> tokens, int tokenLine, int tokenColumn)
        {
            // Check for three-character operators
            if (position + 2 < inputLength)
            {
                string threeChar = input.Substring(position, 3);
                if (ThreeCharOperators.Contains(threeChar))
                {
                    tokens.Add(new Token(TokenType.COMPOUND_ASSIGN, threeChar, tokenLine, tokenColumn));
                    Advance();
                    Advance();
                    Advance();
                    return true;
                }
            }

            // Check for two-character operators
            if (position + 1 < inputLength)
            {
                string twoChar = input.Substring(position, 2);
                
                // Special case for walrus operator
                if (twoChar == ":=")
                {
                    tokens.Add(new Token(TokenType.WALRUS, ":=", tokenLine, tokenColumn));
                    Advance();
                    Advance();
                    return true;
                }
                
                if (TwoCharOperators.Contains(twoChar))
                {
                    TokenType type = twoChar.Contains('=') && twoChar != "==" && twoChar != "!=" && 
                                   twoChar != "<=" && twoChar != ">=" && twoChar != "->"
                        ? TokenType.COMPOUND_ASSIGN
                        : TokenType.OPERATOR;
                    tokens.Add(new Token(type, twoChar, tokenLine, tokenColumn));
                    Advance();
                    Advance();
                    return true;
                }
            }

            // Single character operators
            if ("+-*/%<>&|^~".Contains(currentChar))
            {
                tokens.Add(new Token(TokenType.OPERATOR, currentChar.ToString(), tokenLine, tokenColumn));
                Advance();
                return true;
            }

            return false;
        }

        private void HandleSingleCharToken(List<Token> tokens, int tokenLine, int tokenColumn)
        {
            var tokenInfo = currentChar switch
            {
                '=' => (TokenType.ASSIGN, "="),
                '(' => (TokenType.LPAREN, "("),
                ')' => (TokenType.RPAREN, ")"),
                '[' => (TokenType.LBRACKET, "["),
                ']' => (TokenType.RBRACKET, "]"),
                '{' => (TokenType.LBRACE, "{"),
                '}' => (TokenType.RBRACE, "}"),
                ':' => (TokenType.COLON, ":"),
                ',' => (TokenType.COMMA, ","),
                '.' => (TokenType.DOT, "."),
                _ => throw new PythonException("SyntaxError", $"Unexpected character: {currentChar}", tokenLine, tokenColumn)
            };

            tokens.Add(new Token(tokenInfo.Item1, tokenInfo.Item2, tokenLine, tokenColumn));
            Advance();
        }

        private string ReadNumber()
        {
            var sb = new StringBuilder(16);
            bool hasDot = false;
            bool hasE = false;

            // Support for hex, octal, binary
            if (currentChar == '0' && position + 1 < inputLength)
            {
                char nextChar = char.ToLower(input[position + 1]);
                if (nextChar == 'x' || nextChar == 'o' || nextChar == 'b')
                {
                    sb.Append(currentChar);
                    Advance();
                    sb.Append(currentChar);
                    Advance();
                    
                    while (currentChar != '\0' && 
                           (char.IsLetterOrDigit(currentChar) || currentChar == '_'))
                    {
                        if (currentChar != '_') // Python allows _ in numbers for readability
                            sb.Append(currentChar);
                        Advance();
                    }
                    return sb.ToString();
                }
            }

            while (currentChar != '\0' && 
                   (char.IsDigit(currentChar) || currentChar == '.' || 
                    currentChar == 'e' || currentChar == 'E' || 
                    currentChar == '_' || (currentChar == '-' && (sb[sb.Length - 1] == 'e' || sb[sb.Length - 1] == 'E'))))
            {
                if (currentChar == '.')
                {
                    if (hasDot || hasE) break;
                    hasDot = true;
                }
                else if (currentChar == 'e' || currentChar == 'E')
                {
                    if (hasE) break;
                    hasE = true;
                }
                
                if (currentChar != '_')
                    sb.Append(currentChar);
                Advance();
            }
            
            return sb.ToString();
        }

        private string ReadString(char quote)
        {
            var sb = new StringBuilder();
            int startLine = line;
            int startColumn = column;
            Advance(); // Skip opening quote

            while (currentChar != '\0' && currentChar != quote)
            {
                if (currentChar == '\\')
                {
                    Advance();
                    sb.Append(currentChar switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        'r' => '\r',
                        '\\' => '\\',
                        '\'' => '\'',
                        '"' => '"',
                        '0' => '\0',
                        'a' => '\a',
                        'b' => '\b',
                        'f' => '\f',
                        'v' => '\v',
                        _ => currentChar
                    });
                }
                else
                {
                    sb.Append(currentChar);
                }
                Advance();
            }

            if (currentChar == quote)
                Advance();
            else
                throw new PythonException("SyntaxError", "Unterminated string literal", startLine, startColumn);

            return sb.ToString();
        }

        private string ReadTripleQuotedString(string quoteType)
        {
            var sb = new StringBuilder();
            int startLine = line;
            int startColumn = column;

            for (int i = 0; i < 3; i++)
                Advance();

            while (position + 2 < inputLength)
            {
                if (currentChar == quoteType[0] &&
                    position + 1 < inputLength && input[position + 1] == quoteType[0] &&
                    position + 2 < inputLength && input[position + 2] == quoteType[0])
                {
                    for (int i = 0; i < 3; i++)
                        Advance();
                    return sb.ToString();
                }

                sb.Append(currentChar);
                Advance();
            }

            while (currentChar != '\0')
            {
                if (currentChar == quoteType[0] &&
                    position + 1 < inputLength && input[position + 1] == quoteType[0] &&
                    position + 2 < inputLength && input[position + 2] == quoteType[0])
                {
                    for (int i = 0; i < 3; i++)
                        Advance();
                    return sb.ToString();
                }
                sb.Append(currentChar);
                Advance();
            }

            throw new PythonException("SyntaxError", "Unterminated triple-quoted string literal", startLine, startColumn);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private string ReadIdentifier()
        {
            var sb = new StringBuilder();
            while (currentChar != '\0' && (char.IsLetterOrDigit(currentChar) || currentChar == '_'))
            {
                sb.Append(currentChar);
                Advance();
            }
            return sb.ToString();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static TokenType GetTokenType(string identifier) => identifier switch
        {
            "True" or "False" => TokenType.BOOLEAN,
            "None" => TokenType.NONE,
            "def" => TokenType.DEF,
            "class" => TokenType.CLASS,
            "if" => TokenType.IF,
            "else" => TokenType.ELSE,
            "elif" => TokenType.ELIF,
            "for" => TokenType.FOR,
            "while" => TokenType.WHILE,
            "in" => TokenType.IN,
            "is" => TokenType.IS,
            "break" => TokenType.BREAK,
            "continue" => TokenType.CONTINUE,
            "try" => TokenType.TRY,
            "except" => TokenType.EXCEPT,
            "finally" => TokenType.FINALLY,
            "raise" => TokenType.RAISE,
            "import" => TokenType.IMPORT,
            "from" => TokenType.FROM,
            "as" => TokenType.AS,
            "return" => TokenType.RETURN,
            "and" => TokenType.AND,
            "or" => TokenType.OR,
            "not" => TokenType.NOT,
            "lambda" => TokenType.LAMBDA,
            "with" => TokenType.WITH,
            "del" => TokenType.DEL,
            "pass" => TokenType.PASS,
            "global" => TokenType.GLOBAL,
            "nonlocal" => TokenType.NONLOCAL,
            "yield" => TokenType.YIELD,
            "assert" => TokenType.ASSERT,
            "async" => TokenType.ASYNC,
            "await" => TokenType.AWAIT,
            "match" => TokenType.MATCH,
            "case" => TokenType.CASE,
            _ => TokenType.IDENTIFIER
        };
    }

    public class Parser
    {
        private readonly List<Token> tokens;
        private readonly int tokenCount;
        private int position;
        private Token currentToken;

        // Consolidated token sets
        private static readonly HashSet<TokenType> BlockStatementTokens = new HashSet<TokenType>
        {
            TokenType.DEF, TokenType.CLASS, TokenType.IDENTIFIER, TokenType.NUMBER,
            TokenType.STRING, TokenType.FSTRING, TokenType.BOOLEAN, TokenType.NONE,
            TokenType.LBRACKET, TokenType.LBRACE, TokenType.LPAREN, TokenType.IF,
            TokenType.FOR, TokenType.WHILE, TokenType.TRY, TokenType.WITH,
            TokenType.RETURN, TokenType.BREAK, TokenType.CONTINUE, TokenType.RAISE,
            TokenType.IMPORT, TokenType.FROM, TokenType.NOT, TokenType.DEL,
            TokenType.PASS, TokenType.LAMBDA, TokenType.GLOBAL, TokenType.NONLOCAL,
            TokenType.YIELD, TokenType.ASSERT, TokenType.ASYNC, TokenType.MATCH,
            TokenType.AT // For decorators
        };

        private static readonly HashSet<TokenType> ContinuationTokens = new HashSet<TokenType>
        {
            TokenType.ELIF, TokenType.ELSE, TokenType.EXCEPT, TokenType.FINALLY, TokenType.CASE
        };

        private static readonly HashSet<TokenType> EndOfStatementTokens = new HashSet<TokenType>
        {
            TokenType.NEWLINE, TokenType.EOF, TokenType.RBRACE, TokenType.RBRACKET,
            TokenType.RPAREN, TokenType.COLON, TokenType.ELSE, TokenType.ELIF,
            TokenType.EXCEPT, TokenType.FINALLY, TokenType.CASE
        };

        public Parser(List<Token> tokens)
        {
            this.tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            this.tokenCount = this.tokens.Count;
            position = 0;
            currentToken = tokenCount > 0 ? tokens[0] : new Token(TokenType.EOF, "", 1, 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Advance()
        {
            position++;
            currentToken = position < tokenCount ? tokens[position] : new Token(TokenType.EOF, "", currentToken?.Line ?? 1, currentToken?.Column ?? 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Token Peek(int offset = 1)
        {
            int pos = position + offset;
            return pos < tokenCount ? tokens[pos] : new Token(TokenType.EOF, "", currentToken?.Line ?? 1, currentToken?.Column ?? 1);
        }

        // Consolidated skip methods
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SkipNewlines()
        {
            while (currentToken.Type == TokenType.NEWLINE)
                Advance();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SkipNewlinesAndIndents()
        {
            while (currentToken.Type == TokenType.NEWLINE ||
                   currentToken.Type == TokenType.INDENT ||
                   currentToken.Type == TokenType.DEDENT)
                Advance();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Expect(TokenType tokenType)
        {
            if (currentToken.Type != tokenType)
                throw new PythonException("SyntaxError", $"Expected {tokenType}, got {currentToken.Type}", currentToken.Line, currentToken.Column);
            Advance();
        }

        public List<ASTNode> Parse()
        {
            var statements = new List<ASTNode>(32);
            SkipNewlinesAndIndents();

            while (currentToken.Type != TokenType.EOF)
            {
                SkipNewlinesAndIndents();
                if (currentToken.Type == TokenType.EOF) break;

                // Check for unexpected continuation tokens
                if (ContinuationTokens.Contains(currentToken.Type))
                {
                    throw new PythonException("SyntaxError",
                        $"'{currentToken.Value}' outside of appropriate statement",
                        currentToken.Line, currentToken.Column);
                }

                var statement = ParseStatement();
                statements.Add(statement);
                SkipNewlinesAndIndents();
            }

            return statements;
        }

        private ASTNode ParseStatement()
        {
            // Check for decorators (NEW)
            if (currentToken.Type == TokenType.AT)
            {
                return ParseDecorated();
            }

            return currentToken.Type switch
            {
                TokenType.DEF => ParseFunctionDef(),
                TokenType.CLASS => ParseClassDef(),
                TokenType.IF => ParseIf(),
                TokenType.FOR => ParseFor(),
                TokenType.WHILE => ParseWhile(),
                TokenType.TRY => ParseTry(),
                TokenType.WITH => ParseWith(),
                TokenType.DEL => ParseDel(),
                TokenType.IMPORT or TokenType.FROM => ParseImport(),
                TokenType.RETURN => ParseReturn(),
                TokenType.BREAK => ParseBreak(),
                TokenType.CONTINUE => ParseContinue(),
                TokenType.RAISE => ParseRaise(),
                TokenType.PASS => ParsePass(),
                TokenType.GLOBAL => ParseGlobal(),
                TokenType.NONLOCAL => ParseNonlocal(),
                // TokenType.YIELD => ParseYield(),
                // TokenType.ASSERT => ParseAssert(),
                // TokenType.ASYNC => ParseAsync(),
                // TokenType.MATCH => ParseMatch(),
                _ => ParseExpressionStatement()
            };
        }

        // NEW: Parse decorators
        private ASTNode ParseDecorated()
        {
            var decorators = new List<ASTNode>();
            
            while (currentToken.Type == TokenType.AT)
            {
                int line = currentToken.Line;
                int column = currentToken.Column;
                
                Advance(); // Skip @
                var decorator = ParseExpression();
                decorators.Add(decorator);
                SkipNewlines();
            }

            // After decorators, must be class or function def
            // if (currentToken.Type == TokenType.DEF)
            // {
            //     var funcDef = ParseFunctionDef();
            //     if (funcDef is FunctionDefNode funcNode)
            //     {
            //         return new DecoratedFunctionNode(funcNode, decorators, funcNode.Line, funcNode.Column);
            //     }
            // }
            // else if (currentToken.Type == TokenType.CLASS)
            // {
            //     var classDef = ParseClassDef();
            //     if (classDef is ClassDefNode classNode)
            //     {
            //         return new DecoratedClassNode(classNode, decorators, classNode.Line, classNode.Column);
            //     }
            // }
            // else if (currentToken.Type == TokenType.ASYNC)
            // {
            //     var asyncDef = ParseAsync();
            //     if (asyncDef is AsyncFunctionDefNode asyncNode)
            //     {
            //         return new DecoratedAsyncFunctionNode(asyncNode, decorators, asyncNode.Line, asyncNode.Column);
            //     }
            // }
            // else
            // {
            //     throw new PythonException("SyntaxError", 
            //         "Decorators can only be applied to class, function, or async function definitions",
            //         currentToken.Line, currentToken.Column);
            // }

            return null; // Should never reach here
        }

        // NEW: Parse yield statement
        // private ASTNode ParseYield()
        // {
        //     int line = currentToken.Line;
        //     int column = currentToken.Column;

        //     Expect(TokenType.YIELD);
            
        //     // Check for yield from
        //     bool isYieldFrom = false;
        //     if (currentToken.Type == TokenType.FROM)
        //     {
        //         Advance();
        //         isYieldFrom = true;
        //     }

        //     ASTNode value = null;
        //     if (!IsEndOfStatement())
        //     {
        //         value = ParseExpressionOrTuple();
        //     }

        //     return isYieldFrom 
        //         ? new YieldFromNode(value, line, column)
        //         : new YieldNode(value, line, column);
        // }

        // NEW: Parse assert statement
        // private ASTNode ParseAssert()
        // {
        //     int line = currentToken.Line;
        //     int column = currentToken.Column;

        //     Expect(TokenType.ASSERT);
            
        //     var condition = ParseExpression();
            
        //     ASTNode message = null;
        //     if (currentToken.Type == TokenType.COMMA)
        //     {
        //         Advance();
        //         message = ParseExpression();
        //     }

        //     return new AssertNode(condition, message, line, column);
        // }

        // NEW: Parse async statements
        // private ASTNode ParseAsync()
        // {
        //     int line = currentToken.Line;
        //     int column = currentToken.Column;

        //     Expect(TokenType.ASYNC);

        //     if (currentToken.Type == TokenType.DEF)
        //     {
        //         // async def
        //         Advance();
        //         string name = currentToken.Value;
        //         Expect(TokenType.IDENTIFIER);
                
        //         // Parse parameters (reuse existing logic)
        //         Expect(TokenType.LPAREN);
        //         var parameters = ParseParameters();
        //         Expect(TokenType.RPAREN);
                
        //         TypeHint returnTypeHint = null;
        //         if (currentToken.Type == TokenType.OPERATOR && currentToken.Value == "->")
        //         {
        //             Advance();
        //             returnTypeHint = ParseTypeHint();
        //         }
                
        //         Expect(TokenType.COLON);
        //         SkipNewlines();
        //         var body = ParseBlock();
                
        //         return new AsyncFunctionDefNode(name, parameters, body, returnTypeHint, line, column);
        //     }
        //     else if (currentToken.Type == TokenType.FOR)
        //     {
        //         // async for
        //         return ParseAsyncFor(line, column);
        //     }
        //     else if (currentToken.Type == TokenType.WITH)
        //     {
        //         // async with
        //         return ParseAsyncWith(line, column);
        //     }
        //     else
        //     {
        //         throw new PythonException("SyntaxError", 
        //             "Expected 'def', 'for', or 'with' after 'async'",
        //             currentToken.Line, currentToken.Column);
        //     }
        // }

        // NEW: Parse async for
        // private ASTNode ParseAsyncFor(int line, int column)
        // {
        //     Expect(TokenType.FOR);
            
        //     var variables = new List<string>();
        //     variables.Add(currentToken.Value);
        //     Expect(TokenType.IDENTIFIER);

        //     while (currentToken.Type == TokenType.COMMA)
        //     {
        //         Advance();
        //         variables.Add(currentToken.Value);
        //         Expect(TokenType.IDENTIFIER);
        //     }

        //     Expect(TokenType.IN);
        //     var iterable = ParseExpression();
        //     Expect(TokenType.COLON);
        //     SkipNewlines();
        //     var body = ParseBlock();

        //     return variables.Count == 1
        //         ? new AsyncForNode(variables[0], iterable, body, line, column)
        //         : new AsyncMultiForNode(variables, iterable, body, line, column);
        // }

        // NEW: Parse async with
        // private ASTNode ParseAsyncWith(int line, int column)
        // {
        //     Expect(TokenType.WITH);
            
        //     var contextExpr = ParseExpression();
            
        //     string variable = null;
        //     if (currentToken.Type == TokenType.AS)
        //     {
        //         Advance();
        //         variable = currentToken.Value;
        //         Expect(TokenType.IDENTIFIER);
        //     }

        //     Expect(TokenType.COLON);
        //     SkipNewlines();
        //     var body = ParseBlock();

        //     return new AsyncWithNode(contextExpr, variable, body, line, column);
        // }

        // NEW: Parse match statement (Python 3.10+)
        // private ASTNode ParseMatch()
        // {
        //     int line = currentToken.Line;
        //     int column = currentToken.Column;

        //     Expect(TokenType.MATCH);
        //     var subject = ParseExpression();
        //     Expect(TokenType.COLON);
        //     SkipNewlines();
            
        //     var cases = new List<(ASTNode pattern, ASTNode guard, List<ASTNode> body)>();
            
        //     Expect(TokenType.INDENT);
        //     SkipNewlines();
            
        //     while (currentToken.Type == TokenType.CASE)
        //     {
        //         Advance(); // Skip 'case'
                
        //         var pattern = ParsePattern();
                
        //         ASTNode guard = null;
        //         if (currentToken.Type == TokenType.IF)
        //         {
        //             Advance();
        //             guard = ParseExpression();
        //         }
                
        //         Expect(TokenType.COLON);
        //         SkipNewlines();
        //         var body = ParseBlock();
                
        //         cases.Add((pattern, guard, body));
                
        //         SkipNewlinesAndIndents();
        //     }
            
        //     if (currentToken.Type == TokenType.DEDENT)
        //     {
        //         Advance();
        //     }
            
        //     return new MatchNode(subject, cases, line, column);
        // }

        // NEW: Parse pattern for match statement
        private ASTNode ParsePattern()
        {
            // Simplified pattern parsing - can be extended
            // if (currentToken.Type == TokenType.IDENTIFIER && currentToken.Value == "_")
            // {
            //     Advance();
            //     return new WildcardPatternNode(currentToken.Line, currentToken.Column);
            // }
            //else
            if (currentToken.Type == TokenType.NUMBER || 
                     currentToken.Type == TokenType.STRING ||
                     currentToken.Type == TokenType.BOOLEAN ||
                     currentToken.Type == TokenType.NONE)
            {
                return ParsePrimary();
            }
            else
            {
                return ParseExpression();
            }
        }

        // Extracted common parameter parsing logic
        private List<Parameter> ParseParameters()
        {
            SkipNewlinesAndIndents();
            
            var parameters = new List<Parameter>();
            bool hasSeenDefault = false;
            bool hasSeenVarArgs = false;
            bool hasSeenKwArgs = false;

            while (currentToken.Type != TokenType.RPAREN)
            {
                if (currentToken.Type == TokenType.OPERATOR && currentToken.Value == "*")
                {
                    if (hasSeenKwArgs)
                        throw new PythonException("SyntaxError", "**kwargs must come after *args", currentToken.Line, currentToken.Column);

                    Advance();
                    
                    if (currentToken.Type == TokenType.COMMA || currentToken.Type == TokenType.RPAREN)
                    {
                        // Keyword-only separator
                    }
                    else
                    {
                        string varArgsName = currentToken.Value;
                        Expect(TokenType.IDENTIFIER);
                        parameters.Add(new Parameter(varArgsName, null, null, ParameterKind.VarArgs));
                        hasSeenVarArgs = true;
                    }
                }
                else if (currentToken.Type == TokenType.OPERATOR && currentToken.Value == "**")
                {
                    Advance();
                    string kwargsName = currentToken.Value;
                    Expect(TokenType.IDENTIFIER);
                    parameters.Add(new Parameter(kwargsName, null, null, ParameterKind.KwArgs));
                    hasSeenKwArgs = true;
                }
                else
                {
                    if (hasSeenVarArgs && !hasSeenKwArgs)
                        throw new PythonException("SyntaxError", "non-default argument follows *args", currentToken.Line, currentToken.Column);

                    string paramName = currentToken.Value;
                    Expect(TokenType.IDENTIFIER);

                    TypeHint typeHint = null;
                    if (currentToken.Type == TokenType.COLON)
                    {
                        Advance();
                        typeHint = ParseTypeHint();
                    }

                    ASTNode defaultValue = null;
                    if (currentToken.Type == TokenType.ASSIGN)
                    {
                        Advance();
                        defaultValue = ParseExpression();
                        hasSeenDefault = true;
                    }
                    else if (hasSeenDefault && !hasSeenVarArgs)
                    {
                        throw new PythonException("SyntaxError",
                            "non-default argument follows default argument",
                            currentToken.Line, currentToken.Column);
                    }

                    parameters.Add(new Parameter(paramName, typeHint, defaultValue, ParameterKind.Normal));
                }

                SkipNewlinesAndIndents();

                if (currentToken.Type == TokenType.COMMA)
                {
                    Advance();
                    SkipNewlinesAndIndents();
                    if (currentToken.Type == TokenType.RPAREN)
                        break;
                }
                else if (currentToken.Type != TokenType.RPAREN)
                {
                    throw new PythonException("SyntaxError",
                        "Expected ',' or ')' in parameter list",
                        currentToken.Line, currentToken.Column);
                }
            }

            return parameters;
        }

        private ASTNode ParsePass()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;
            Expect(TokenType.PASS);
            return new PassNode(line, column);
        }

        private ASTNode ParseGlobal()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;
            Expect(TokenType.GLOBAL);

            var names = new List<string>();
            names.Add(currentToken.Value);
            Expect(TokenType.IDENTIFIER);

            while (currentToken.Type == TokenType.COMMA)
            {
                Advance();
                names.Add(currentToken.Value);
                Expect(TokenType.IDENTIFIER);
            }

            return new GlobalNode(names, line, column);
        }

        private ASTNode ParseNonlocal()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;
            Expect(TokenType.NONLOCAL);

            var names = new List<string>();
            names.Add(currentToken.Value);
            Expect(TokenType.IDENTIFIER);

            while (currentToken.Type == TokenType.COMMA)
            {
                Advance();
                names.Add(currentToken.Value);
                Expect(TokenType.IDENTIFIER);
            }

            return new NonlocalNode(names, line, column);
        }

        private ASTNode ParseFunctionDef()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            Expect(TokenType.DEF);
            string name = currentToken.Value;
            Expect(TokenType.IDENTIFIER);
            Expect(TokenType.LPAREN);

            var parameters = ParseParameters();
            
            Expect(TokenType.RPAREN);

            TypeHint returnTypeHint = null;
            if (currentToken.Type == TokenType.OPERATOR && currentToken.Value == "->")
            {
                Advance();
                returnTypeHint = ParseTypeHint();
            }

            Expect(TokenType.COLON);
            SkipNewlines();

            var body = ParseBlock();

            return new FunctionDefNode(name, parameters, body, returnTypeHint, line, column);
        }

        private TypeHint ParseTypeHint()
        {
            if (currentToken.Type == TokenType.STRING)
            {
                string className = currentToken.Value;
                Advance();
                return new ClassTypeHint(className);
            }

            if (currentToken.Type != TokenType.IDENTIFIER)
                throw new PythonException("SyntaxError", "Expected type hint", currentToken.Line, currentToken.Column);

            string typeName = currentToken.Value;
            Advance();

            // Handle special type hints
            if (typeName == "Optional")
            {
                if (currentToken.Type == TokenType.LBRACKET)
                {
                    Advance();
                    var innerType = ParseTypeHint();
                    Expect(TokenType.RBRACKET);
                    return new UnionTypeHint(new List<TypeHint>
                    {
                        innerType,
                        SimpleTypeHint.Create(PythonType.None)
                    });
                }
            }

            if (typeName == "Union")
            {
                if (currentToken.Type == TokenType.LBRACKET)
                {
                    Advance();
                    var types = new List<TypeHint>();

                    do
                    {
                        types.Add(ParseTypeHint());
                        if (currentToken.Type == TokenType.COMMA)
                        {
                            Advance();
                            SkipNewlinesAndIndents();
                        }
                        else
                        {
                            break;
                        }
                    } while (currentToken.Type != TokenType.RBRACKET);

                    Expect(TokenType.RBRACKET);
                    return new UnionTypeHint(types);
                }
            }

            var pythonType = typeName switch
            {
                "int" => PythonType.Int,
                "float" => PythonType.Float,
                "str" => PythonType.String,
                "bool" => PythonType.Boolean,
                "list" => PythonType.List,
                "dict" => PythonType.Dict,
                "tuple" => PythonType.Tuple,
                "set" => PythonType.Set,
                "None" => PythonType.None,
                _ => PythonType.Instance
            };

            if (pythonType == PythonType.Instance && typeName != "Any")
            {
                if (currentToken.Type != TokenType.LBRACKET)
                {
                    return new ClassTypeHint(typeName);
                }
            }

            if (currentToken.Type == TokenType.LBRACKET)
            {
                Advance();
                SkipNewlinesAndIndents();

                var genericArgs = new List<TypeHint>();

                while (currentToken.Type != TokenType.RBRACKET)
                {
                    genericArgs.Add(ParseTypeHint());
                    SkipNewlinesAndIndents();

                    if (currentToken.Type == TokenType.COMMA)
                    {
                        Advance();
                        SkipNewlinesAndIndents();

                        if (currentToken.Type == TokenType.RBRACKET)
                            break;
                    }
                    else if (currentToken.Type != TokenType.RBRACKET)
                    {
                        throw new PythonException("SyntaxError", "Expected ',' or ']' in generic type", currentToken.Line, currentToken.Column);
                    }
                }

                Expect(TokenType.RBRACKET);
                return new GenericTypeHint(pythonType, genericArgs);
            }

            if (typeName == "Any")
            {
                return new AnyTypeHint();
            }

            return SimpleTypeHint.Create(pythonType);
        }

        // Consolidated block parsing with better continuation handling
        private List<ASTNode> ParseBlock()
        {
            var statements = new List<ASTNode>();

            if (currentToken.Type == TokenType.INDENT)
            {
                Advance();
                SkipNewlines();

                while (currentToken.Type != TokenType.DEDENT && currentToken.Type != TokenType.EOF)
                {
                    if (currentToken.Type == TokenType.NEWLINE)
                    {
                        Advance();
                        continue;
                    }

                    if (IsBlockStatement())
                    {
                        statements.Add(ParseStatement());
                        SkipNewlines();
                    }
                    else if (currentToken.Type == TokenType.DEDENT)
                    {
                        break;
                    }
                    else if (ContinuationTokens.Contains(currentToken.Type))
                    {
                        break;
                    }
                    else
                    {
                        throw new PythonException("SyntaxError",
                            $"Unexpected token in block: {currentToken.Type}",
                            currentToken.Line, currentToken.Column);
                    }
                }

                HandleBlockEnd();
            }
            else
            {
                throw new PythonException("IndentationError",
                    "expected an indented block",
                    currentToken.Line, currentToken.Column);
            }

            return statements;
        }

        // Helper to handle block ending
        private void HandleBlockEnd()
        {
            if (currentToken.Type == TokenType.DEDENT)
            {
                if (position + 1 < tokenCount)
                {
                    var nextToken = tokens[position + 1];
                    if (!ContinuationTokens.Contains(nextToken.Type))
                    {
                        Advance();
                    }
                }
                else
                {
                    Advance();
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsBlockStatement() => BlockStatementTokens.Contains(currentToken.Type);

        private ASTNode ParseClassDef()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            Expect(TokenType.CLASS);
            string name = currentToken.Value;
            Expect(TokenType.IDENTIFIER);

            var baseClasses = new List<string>();
            if (currentToken.Type == TokenType.LPAREN)
            {
                Advance();
                
                while (currentToken.Type != TokenType.RPAREN)
                {
                    if (currentToken.Type == TokenType.IDENTIFIER)
                    {
                        baseClasses.Add(currentToken.Value);
                        Advance();
                    }
                    
                    if (currentToken.Type == TokenType.COMMA)
                    {
                        Advance();
                        SkipNewlinesAndIndents();
                    }
                    else if (currentToken.Type != TokenType.RPAREN)
                    {
                        break;
                    }
                }
                
                Expect(TokenType.RPAREN);
            }

            Expect(TokenType.COLON);
            SkipNewlines();

            var body = ParseBlock();
            return new ClassDefNode(name, body, baseClasses.FirstOrDefault(), line, column);
        }

        private ASTNode ParseIf()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            Expect(TokenType.IF);
            var condition = ParseExpression();
            Expect(TokenType.COLON);
            SkipNewlines();
            var thenBody = ParseBlock();

            HandleBlockEnd();
            SkipNewlines();

            var elifClauses = new List<(ASTNode, List<ASTNode>)>();
            while (currentToken.Type == TokenType.ELIF)
            {
                Advance();
                var elifCondition = ParseExpression();
                Expect(TokenType.COLON);
                SkipNewlines();
                var elifBody = ParseBlock();

                HandleBlockEnd();
                SkipNewlines();
                elifClauses.Add((elifCondition, elifBody));
            }

            List<ASTNode> elseBody = null;
            if (currentToken.Type == TokenType.ELSE)
            {
                Advance();
                Expect(TokenType.COLON);
                SkipNewlines();
                elseBody = ParseBlock();
            }

            return new IfNode(condition, thenBody, elifClauses, elseBody, line, column);
        }

        private ASTNode ParseFor()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            Expect(TokenType.FOR);

            var variables = new List<string>();
            variables.Add(currentToken.Value);
            Expect(TokenType.IDENTIFIER);

            while (currentToken.Type == TokenType.COMMA)
            {
                Advance();
                variables.Add(currentToken.Value);
                Expect(TokenType.IDENTIFIER);
            }

            Expect(TokenType.IN);
            var iterable = ParseExpression();
            Expect(TokenType.COLON);
            SkipNewlines();

            var body = ParseBlock();

            if (variables.Count == 1)
            {
                return new ForNode(variables[0], iterable, body, line, column);
            }
            else
            {
                return new MultiForNode(variables, iterable, body, line, column);
            }
        }

        private ASTNode ParseWhile()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            Expect(TokenType.WHILE);
            var condition = ParseExpression();
            Expect(TokenType.COLON);
            SkipNewlines();

            var body = ParseBlock();
            return new WhileNode(condition, body, line, column);
        }

        private ASTNode ParseTry()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            Expect(TokenType.TRY);
            Expect(TokenType.COLON);
            SkipNewlines();

            var tryBody = ParseBlock();

            var exceptClauses = new List<(string, string, List<ASTNode>)>();
            List<ASTNode> elseBody = null;
            List<ASTNode> finallyBody = null;

            // Clean up tokens after try block
            while (currentToken.Type == TokenType.NEWLINE ||
                currentToken.Type == TokenType.DEDENT ||
                currentToken.Type == TokenType.INDENT)
            {
                Advance();
            }

            while (currentToken.Type == TokenType.EXCEPT)
            {
                Advance();

                string exceptionType = null;
                string variable = null;

                if (currentToken.Type == TokenType.IDENTIFIER)
                {
                    exceptionType = currentToken.Value;
                    Advance();

                    if (currentToken.Type == TokenType.AS)
                    {
                        Advance();
                        variable = currentToken.Value;
                        Expect(TokenType.IDENTIFIER);
                    }
                }

                Expect(TokenType.COLON);
                SkipNewlines();

                var exceptBody = ParseBlock();
                exceptClauses.Add((exceptionType, variable, exceptBody));

                while (currentToken.Type == TokenType.NEWLINE ||
                    currentToken.Type == TokenType.DEDENT ||
                    currentToken.Type == TokenType.INDENT)
                {
                    Advance();
                }
            }

            if (currentToken.Type == TokenType.ELSE)
            {
                Advance();
                Expect(TokenType.COLON);
                SkipNewlines();
                elseBody = ParseBlock();

                while (currentToken.Type == TokenType.NEWLINE ||
                    currentToken.Type == TokenType.DEDENT ||
                    currentToken.Type == TokenType.INDENT)
                {
                    Advance();
                }
            }

            if (currentToken.Type == TokenType.FINALLY)
            {
                Advance();
                Expect(TokenType.COLON);
                SkipNewlines();
                finallyBody = ParseBlock();
            }

            return new TryNode(tryBody, exceptClauses, elseBody, finallyBody, line, column);
        }

        private ASTNode ParseWith()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            Expect(TokenType.WITH);

            var contextExpr = ParseExpression();

            string variable = null;
            if (currentToken.Type == TokenType.AS)
            {
                Advance();
                variable = currentToken.Value;
                Expect(TokenType.IDENTIFIER);
            }

            Expect(TokenType.COLON);
            SkipNewlines();

            var body = ParseBlock();

            return new WithNode(contextExpr, variable, body, line, column);
        }

        private ASTNode ParseDel()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            Expect(TokenType.DEL);

            string variableName = currentToken.Value;
            Expect(TokenType.IDENTIFIER);

            return new DelNode(variableName, line, column);
        }

        private ASTNode ParseImport()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            if (currentToken.Type == TokenType.FROM)
            {
                Expect(TokenType.FROM);

                string moduleName = currentToken.Value;
                Expect(TokenType.IDENTIFIER);

                while (currentToken.Type == TokenType.DOT)
                {
                    Advance();
                    moduleName += "." + currentToken.Value;
                    Expect(TokenType.IDENTIFIER);
                }

                Expect(TokenType.IMPORT);

                if (currentToken.Type == TokenType.OPERATOR && currentToken.Value == "*")
                {
                    Advance();
                    var allItems = new List<(string, string)> { ("*", null) };
                    return new FromImportNode(moduleName, allItems, line, column);
                }

                var importItems = new List<(string, string)>();

                do
                {
                    string itemName = currentToken.Value;
                    Expect(TokenType.IDENTIFIER);

                    string alias = null;
                    if (currentToken.Type == TokenType.AS)
                    {
                        Advance();
                        alias = currentToken.Value;
                        Expect(TokenType.IDENTIFIER);
                    }

                    importItems.Add((itemName, alias));

                    if (currentToken.Type == TokenType.COMMA)
                    {
                        Advance();
                    }
                    else
                    {
                        break;
                    }
                } while (currentToken.Type == TokenType.IDENTIFIER);

                return new FromImportNode(moduleName, importItems, line, column);
            }
            else
            {
                Expect(TokenType.IMPORT);

                string moduleName = currentToken.Value;
                Expect(TokenType.IDENTIFIER);

                while (currentToken.Type == TokenType.DOT)
                {
                    Advance();
                    moduleName += "." + currentToken.Value;
                    Expect(TokenType.IDENTIFIER);
                }

                string alias = null;
                if (currentToken.Type == TokenType.AS)
                {
                    Advance();
                    alias = currentToken.Value;
                    Expect(TokenType.IDENTIFIER);
                }

                return new ImportNode(moduleName, alias, line, column);
            }
        }

        private ASTNode ParseReturn()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            Expect(TokenType.RETURN);
            ASTNode value = null;
            if (!IsEndOfStatement())
                value = ParseExpressionOrTuple();
            return new ReturnNode(value, line, column);
        }

        private ASTNode ParseBreak()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;
            Expect(TokenType.BREAK);
            return new BreakNode(line, column);
        }

        private ASTNode ParseContinue()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;
            Expect(TokenType.CONTINUE);
            return new ContinueNode(line, column);
        }

        private ASTNode ParseRaise()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;
            Expect(TokenType.RAISE);
            
            ASTNode exception = null;
            if (!IsEndOfStatement())
                exception = ParseExpression();
                
            return new RaiseNode(exception, line, column);
        }

        private ASTNode ParseExpressionStatement()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            var expr = ParseExpression();

            // Handle multiple assignment (tuple unpacking)
            if (currentToken.Type == TokenType.COMMA)
            {
                var elements = new List<ASTNode> { expr };
                while (currentToken.Type == TokenType.COMMA)
                {
                    Advance();
                    if (currentToken.Type == TokenType.ASSIGN || currentToken.Type == TokenType.COLON)
                        break;
                    elements.Add(ParseExpression());
                }

                if (currentToken.Type == TokenType.ASSIGN)
                {
                    var names = new List<string>();
                    foreach (var element in elements)
                    {
                        if (element is VariableNode varElement)
                            names.Add(varElement.Name);
                        else
                            throw new PythonException("SyntaxError", $"Invalid assignment target", currentToken.Line, currentToken.Column);
                    }
                    Advance();
                    var value = ParseExpressionOrTuple();
                    return new MultipleAssignmentNode(names, value, line, column);
                }
                else
                {
                    return new TupleNode(elements, line, column);
                }
            }

            // Handle type annotations
            if (currentToken.Type == TokenType.COLON)
            {
                Advance();
                var typeHint = ParseTypeHint();

                ASTNode value = null;
                if (currentToken.Type == TokenType.ASSIGN)
                {
                    Advance();
                    value = ParseExpressionOrTuple();
                }

                if (expr is VariableNode varNode)
                {
                    return new AnnotatedAssignmentNode(varNode.Name, typeHint, value, line, column);
                }
                else if (expr is AttributeNode attrNode)
                {
                    return new AnnotatedAttributeAssignmentNode(attrNode.Object, attrNode.Attribute, typeHint, value, line, column);
                }
                else
                {
                    throw new PythonException("SyntaxError",
                        "Invalid target for annotated assignment",
                        currentToken.Line, currentToken.Column);
                }
            }

            // Handle walrus operator (NEW)
            // if (currentToken.Type == TokenType.WALRUS)
            // {
            //     if (expr is VariableNode varNode)
            //     {
            //         Advance();
            //         var value = ParseExpression();
            //         return new WalrusNode(varNode.Name, value, line, column);
            //     }
            //     else
            //     {
            //         throw new PythonException("SyntaxError",
            //             "Invalid target for walrus operator",
            //             currentToken.Line, currentToken.Column);
            //     }
            // }

            // Handle compound assignments
            if (currentToken.Type == TokenType.COMPOUND_ASSIGN)
            {
                string op = currentToken.Value;
                Advance();
                var value = ParseExpressionOrTuple();

                if (expr is VariableNode varNode)
                {
                    return new CompoundAssignmentNode(varNode.Name, op, value, line, column);
                }
                else if (expr is AttributeNode attrNode)
                {
                    return new AttributeCompoundAssignmentNode(attrNode.Object, attrNode.Attribute, op, value, line, column);
                }
                else if (expr is IndexNode indexNode)
                {
                    return new IndexCompoundAssignmentNode(indexNode.Object, indexNode.Index, op, value, line, column);
                }
                else
                {
                    throw new PythonException("SyntaxError",
                        $"Invalid target for compound assignment",
                        currentToken.Line, currentToken.Column);
                }
            }

            // Handle regular assignment
            if (currentToken.Type == TokenType.ASSIGN)
            {
                if (expr is VariableNode varNode)
                {
                    Advance();
                    var value = ParseExpressionOrTuple();
                    return new AssignmentNode(varNode.Name, value, line, column);
                }
                else if (expr is IndexNode indexNode)
                {
                    Advance();
                    var value = ParseExpressionOrTuple();
                    return new IndexAssignmentNode(indexNode.Object, indexNode.Index, value, line, column);
                }
                else if (expr is AttributeNode attrNode)
                {
                    Advance();
                    var value = ParseExpressionOrTuple();
                    return new AttributeAssignmentNode(attrNode.Object, attrNode.Attribute, value, line, column);
                }
                else
                {
                    throw new PythonException("SyntaxError", $"Invalid assignment target", currentToken.Line, currentToken.Column);
                }
            }

            return new ExpressionStatementNode(expr, line, column);
        }

        private ASTNode ParseExpressionOrTuple()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            var expressions = new List<ASTNode>();
            expressions.Add(ParseExpression());

            while (currentToken.Type == TokenType.COMMA)
            {
                Advance();
                if (IsEndOfStatement())
                    break;
                expressions.Add(ParseExpression());
            }

            if (expressions.Count == 1)
                return expressions[0];
            else
                return new TupleNode(expressions, line, column);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsEndOfStatement()
        {
            return EndOfStatementTokens.Contains(currentToken.Type);
        }

        internal ASTNode ParseExpression() => ParseConditionalExpression();

        private ASTNode ParseConditionalExpression()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            var node = ParseOrExpression();

            if (currentToken.Type == TokenType.IF)
            {
                Advance();
                var condition = ParseOrExpression();

                if (currentToken.Type != TokenType.ELSE)
                {
                    throw new PythonException("SyntaxError", $"Expected 'else' in conditional expression", currentToken.Line, currentToken.Column);
                }

                Advance();
                var falseValue = ParseConditionalExpression();

                return new ConditionalExpressionNode(node, condition, falseValue, line, column);
            }

            return node;
        }

        private ASTNode ParseOrExpression()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            var node = ParseAndExpression();

            while (currentToken.Type == TokenType.OR)
            {
                Advance();
                node = new BinaryOpNode(node, "or", ParseAndExpression(), line, column);
            }

            return node;
        }

        private ASTNode ParseAndExpression()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            var node = ParseNotExpression();

            while (currentToken.Type == TokenType.AND)
            {
                Advance();
                node = new BinaryOpNode(node, "and", ParseNotExpression(), line, column);
            }

            return node;
        }

        private ASTNode ParseNotExpression()
        {
            if (currentToken.Type == TokenType.NOT)
            {
                if (position + 1 < tokens.Count && tokens[position + 1].Type == TokenType.IN)
                {
                    return ParseInExpression();
                }

                int line = currentToken.Line;
                int column = currentToken.Column;
                Advance();
                return new UnaryOpNode("not", ParseNotExpression(), line, column);
            }

            return ParseInExpression();
        }

        private ASTNode ParseInExpression()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            var node = ParseIsExpression();

            while (currentToken.Type == TokenType.IN ||
                (currentToken.Type == TokenType.NOT &&
                    position + 1 < tokens.Count &&
                    tokens[position + 1].Type == TokenType.IN))
            {
                if (currentToken.Type == TokenType.NOT)
                {
                    Advance();
                    Advance();
                    node = new BinaryOpNode(node, "not in", ParseIsExpression(), line, column);
                }
                else
                {
                    Advance();
                    node = new BinaryOpNode(node, "in", ParseIsExpression(), line, column);
                }
            }

            return node;
        }

        private ASTNode ParseIsExpression()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            var node = ParseComparison();

            while (currentToken.Type == TokenType.IS)
            {
                Advance();

                if (currentToken.Type == TokenType.NOT)
                {
                    Advance();
                    node = new BinaryOpNode(node, "is not", ParseComparison(), line, column);
                }
                else
                {
                    node = new BinaryOpNode(node, "is", ParseComparison(), line, column);
                }
            }

            return node;
        }

        private ASTNode ParseComparison()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            var node = ParseBitwiseOr();

            while (currentToken.Type == TokenType.OPERATOR &&
                   new[] { "==", "!=", "<", ">", "<=", ">=" }.Contains(currentToken.Value))
            {
                string op = currentToken.Value;
                Advance();
                node = new BinaryOpNode(node, op, ParseBitwiseOr(), line, column);
            }

            return node;
        }

        // NEW: Add bitwise operations
        private ASTNode ParseBitwiseOr()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            var node = ParseBitwiseXor();

            while (currentToken.Type == TokenType.OPERATOR && currentToken.Value == "|")
            {
                Advance();
                node = new BinaryOpNode(node, "|", ParseBitwiseXor(), line, column);
            }

            return node;
        }

        private ASTNode ParseBitwiseXor()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            var node = ParseBitwiseAnd();

            while (currentToken.Type == TokenType.OPERATOR && currentToken.Value == "^")
            {
                Advance();
                node = new BinaryOpNode(node, "^", ParseBitwiseAnd(), line, column);
            }

            return node;
        }

        private ASTNode ParseBitwiseAnd()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            var node = ParseShift();

            while (currentToken.Type == TokenType.OPERATOR && currentToken.Value == "&")
            {
                Advance();
                node = new BinaryOpNode(node, "&", ParseShift(), line, column);
            }

            return node;
        }

        private ASTNode ParseShift()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            var node = ParseArithmetic();

            while (currentToken.Type == TokenType.OPERATOR &&
                   new[] { "<<", ">>" }.Contains(currentToken.Value))
            {
                string op = currentToken.Value;
                Advance();
                node = new BinaryOpNode(node, op, ParseArithmetic(), line, column);
            }

            return node;
        }

        private ASTNode ParseArithmetic()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            var node = ParseTerm();

            while (currentToken.Type == TokenType.OPERATOR &&
                   new[] { "+", "-" }.Contains(currentToken.Value))
            {
                string op = currentToken.Value;
                Advance();
                node = new BinaryOpNode(node, op, ParseTerm(), line, column);
            }

            return node;
        }

        private ASTNode ParseTerm()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            var node = ParsePower();

            while (currentToken.Type == TokenType.OPERATOR &&
                   new[] { "*", "/", "%", "//" }.Contains(currentToken.Value))
            {
                string op = currentToken.Value;
                Advance();
                node = new BinaryOpNode(node, op, ParsePower(), line, column);
            }

            return node;
        }

        private ASTNode ParsePower()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            var node = ParseFactor();

            if (currentToken.Type == TokenType.OPERATOR && currentToken.Value == "**")
            {
                Advance();
                node = new BinaryOpNode(node, "**", ParsePower(), line, column);
            }

            return node;
        }

        private ASTNode ParseFactor()
        {
            if (currentToken.Type == TokenType.OPERATOR)
            {
                int line = currentToken.Line;
                int column = currentToken.Column;
                
                if (currentToken.Value == "-" || currentToken.Value == "+" || currentToken.Value == "~")
                {
                    string op = currentToken.Value;
                    Advance();
                    return new UnaryOpNode(op, ParseFactor(), line, column);
                }
            }

            // if (currentToken.Type == TokenType.AWAIT)
            // {
            //     int line = currentToken.Line;
            //     int column = currentToken.Column;
            //     Advance();
            //     return new AwaitNode(ParseFactor(), line, column);
            // }

            return ParsePostfix();
        }

        private ASTNode ParsePostfix()
        {
            var node = ParsePrimary();

            while (true)
            {
                int line = currentToken.Line;
                int column = currentToken.Column;

                if (currentToken.Type == TokenType.LPAREN)
                {
                    node = ParseFunctionCall(node, line, column);
                }
                else if (currentToken.Type == TokenType.LBRACKET)
                {
                    node = ParseIndexOrSlice(node, line, column);
                }
                else if (currentToken.Type == TokenType.DOT)
                {
                    Advance();
                    string attribute = currentToken.Value;
                    Expect(TokenType.IDENTIFIER);
                    node = new AttributeNode(node, attribute, line, column);
                }
                else
                {
                    break;
                }
            }

            return node;
        }

        // Extracted function call parsing
        private ASTNode ParseFunctionCall(ASTNode node, int line, int column)
        {
            Advance(); // Skip '('
            SkipNewlinesAndIndents();

            var arguments = new List<ASTNode>();
            var keywordArguments = new Dictionary<string, ASTNode>();
            bool seenKeyword = false;

            while (currentToken.Type != TokenType.RPAREN)
            {
                if (currentToken.Type == TokenType.IDENTIFIER)
                {
                    int savePos = position;
                    string possibleKeyword = currentToken.Value;
                    Advance();

                    if (currentToken.Type == TokenType.ASSIGN)
                    {
                        Advance();
                        var value = ParseArgumentExpression();
                        keywordArguments[possibleKeyword] = value;
                        seenKeyword = true;
                    }
                    else
                    {
                        position = savePos;
                        currentToken = tokens[position];

                        if (seenKeyword)
                        {
                            throw new PythonException("SyntaxError",
                                "positional argument follows keyword argument",
                                currentToken.Line, currentToken.Column);
                        }

                        arguments.Add(ParseArgumentExpression());
                    }
                }
                else
                {
                    if (seenKeyword)
                    {
                        throw new PythonException("SyntaxError",
                            "positional argument follows keyword argument",
                            currentToken.Line, currentToken.Column);
                    }
                    arguments.Add(ParseArgumentExpression());
                }

                SkipNewlinesAndIndents();

                if (currentToken.Type == TokenType.COMMA)
                {
                    Advance();
                    SkipNewlinesAndIndents();
                    if (currentToken.Type == TokenType.RPAREN)
                        break;
                }
                else if (currentToken.Type != TokenType.RPAREN)
                {
                    throw new PythonException("SyntaxError",
                        $"Expected ',' or ')' in function call",
                        currentToken.Line, currentToken.Column);
                }
            }

            Expect(TokenType.RPAREN);
            return new FunctionCallNode(node, arguments, keywordArguments, line, column);
        }

        // Extracted index/slice parsing
        private ASTNode ParseIndexOrSlice(ASTNode node, int line, int column)
        {
            Advance();
            var indices = new List<ASTNode>();
            var hasColon = false;

            if (currentToken.Type != TokenType.COLON && currentToken.Type != TokenType.RBRACKET)
                indices.Add(ParseExpression());
            else
                indices.Add(null);

            if (currentToken.Type == TokenType.COLON)
            {
                hasColon = true;
                Advance();
                if (currentToken.Type != TokenType.COLON && currentToken.Type != TokenType.RBRACKET)
                    indices.Add(ParseExpression());
                else
                    indices.Add(null);

                if (currentToken.Type == TokenType.COLON)
                {
                    Advance();
                    if (currentToken.Type != TokenType.RBRACKET)
                        indices.Add(ParseExpression());
                    else
                        indices.Add(null);
                }
                else
                {
                    indices.Add(null);
                }
            }

            Expect(TokenType.RBRACKET);

            if (hasColon)
                return new SliceNode(node, indices[0], indices[1], indices.Count > 2 ? indices[2] : null, line, column);
            else
                return new IndexNode(node, indices[0], line, column);
        }

        private ASTNode ParsePrimary()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            switch (currentToken.Type)
            {
                case TokenType.NUMBER:
                    return ParseNumber(line, column);

                case TokenType.STRING:
                case TokenType.FSTRING:
                    return ParseStringLiteralConcatenation();

                case TokenType.BOOLEAN:
                    bool boolValue = currentToken.Value == "True";
                    Advance();
                    return new BooleanNode(boolValue, line, column);

                case TokenType.NONE:
                    Advance();
                    return new NoneNode(line, column);

                case TokenType.IDENTIFIER:
                    string name = currentToken.Value;
                    Advance();
                    return new VariableNode(name, line, column);

                case TokenType.LAMBDA:
                    return ParseLambda();

                case TokenType.LBRACKET:
                    return ParseListOrComprehension();

                case TokenType.LBRACE:
                    return ParseDictOrSetOrComprehension();

                case TokenType.LPAREN:
                    return ParseParenthesizedOrTupleOrGenerator();

                default:
                    throw new PythonException("SyntaxError", $"Unexpected token: {currentToken.Type}", currentToken.Line, currentToken.Column);
            }
        }

        private ASTNode ParseNumber(int line, int column)
        {
            string numberStr = currentToken.Value;
            object numberValue;

            // Handle different number formats
            if (numberStr.StartsWith("0x") || numberStr.StartsWith("0X"))
            {
                // Hexadecimal
                numberValue = Convert.ToInt32(numberStr.Substring(2), 16);
            }
            else if (numberStr.StartsWith("0o") || numberStr.StartsWith("0O"))
            {
                // Octal
                numberValue = Convert.ToInt32(numberStr.Substring(2), 8);
            }
            else if (numberStr.StartsWith("0b") || numberStr.StartsWith("0B"))
            {
                // Binary
                numberValue = Convert.ToInt32(numberStr.Substring(2), 2);
            }
            else if (numberStr.Contains('.') || numberStr.Contains('e') || numberStr.Contains('E'))
            {
                // Float
                numberValue = double.Parse(numberStr);
            }
            else
            {
                // Integer
                if (int.TryParse(numberStr, out int intValue))
                    numberValue = intValue;
                else
                    numberValue = double.Parse(numberStr);
            }

            Advance();
            return new NumberNode(numberValue, line, column);
        }

        private ASTNode ParseStringLiteralConcatenation()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            var parts = new List<ASTNode>();

            while (currentToken.Type == TokenType.STRING || currentToken.Type == TokenType.FSTRING)
            {
                if (currentToken.Type == TokenType.STRING)
                {
                    parts.Add(new StringNode(currentToken.Value, currentToken.Line, currentToken.Column));
                    Advance();
                }
                else if (currentToken.Type == TokenType.FSTRING)
                {
                    parts.Add(new FStringNode(currentToken.Value, currentToken.Line, currentToken.Column));
                    Advance();
                }

                if (currentToken.Type != TokenType.STRING && currentToken.Type != TokenType.FSTRING)
                {
                    break;
                }
            }

            if (parts.Count == 1)
            {
                return parts[0];
            }

            return new StringConcatenationNode(parts, line, column);
        }

        private ASTNode ParseLambda()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            Expect(TokenType.LAMBDA);

            var parameters = new List<Parameter>();
            bool hasSeenDefault = false;

            if (currentToken.Type != TokenType.COLON)
            {
                do
                {
                    if (currentToken.Type != TokenType.IDENTIFIER)
                    {
                        throw new PythonException("SyntaxError",
                            $"Expected parameter name in lambda",
                            currentToken.Line, currentToken.Column);
                    }

                    string paramName = currentToken.Value;
                    Advance();

                    ASTNode defaultValue = null;
                    if (currentToken.Type == TokenType.ASSIGN)
                    {
                        Advance();
                        defaultValue = ParseOrExpression();
                        hasSeenDefault = true;
                    }
                    else if (hasSeenDefault)
                    {
                        throw new PythonException("SyntaxError",
                            "non-default argument follows default argument",
                            currentToken.Line, currentToken.Column);
                    }

                    parameters.Add(new Parameter(paramName, null, defaultValue));

                    if (currentToken.Type == TokenType.COMMA)
                    {
                        Advance();
                        if (currentToken.Type == TokenType.COLON)
                        {
                            throw new PythonException("SyntaxError",
                                $"Expected parameter after ',' in lambda",
                                currentToken.Line, currentToken.Column);
                        }
                    }
                    else
                    {
                        break;
                    }
                } while (currentToken.Type != TokenType.COLON);
            }

            Expect(TokenType.COLON);

            var body = ParseExpression();

            return new LambdaNode(parameters, body, line, column);
        }

        // Enhanced to support list comprehensions
        private ASTNode ParseListOrComprehension()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            Expect(TokenType.LBRACKET);
            SkipNewlinesAndIndents();

            if (currentToken.Type == TokenType.RBRACKET)
            {
                Advance();
                return new ListNode(new List<ASTNode>(), line, column);
            }

            var firstExpr = ParseOrExpression();
            SkipNewlinesAndIndents();

            if (currentToken.Type == TokenType.FOR)
            {
                return ParseComprehension(firstExpr, line, column, ComprehensionType.List);
            }

            var elements = new List<ASTNode> { firstExpr };

            while (currentToken.Type == TokenType.COMMA)
            {
                Advance();
                SkipNewlinesAndIndents();

                if (currentToken.Type == TokenType.RBRACKET)
                    break;

                elements.Add(ParseExpression());
                SkipNewlinesAndIndents();
            }

            Expect(TokenType.RBRACKET);
            return new ListNode(elements, line, column);
        }

        // NEW: Support dict/set comprehensions
        private ASTNode ParseDictOrSetOrComprehension()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            Expect(TokenType.LBRACE);
            SkipNewlinesAndIndents();

            // Empty dict
            if (currentToken.Type == TokenType.RBRACE)
            {
                Advance();
                return new DictNode(new List<(ASTNode, ASTNode)>(), line, column);
            }

            var firstExpr = ParseExpression();
            SkipNewlinesAndIndents();

            // Check if it's a dict
            if (currentToken.Type == TokenType.COLON)
            {
                Advance();
                SkipNewlinesAndIndents();
                var value = ParseExpression();
                SkipNewlinesAndIndents();

                // Check for dict comprehension
                // if (currentToken.Type == TokenType.FOR)
                // {
                //     return ParseDictComprehension(firstExpr, value, line, column);
                // }

                // Regular dict
                var pairs = new List<(ASTNode, ASTNode)> { (firstExpr, value) };

                while (currentToken.Type == TokenType.COMMA)
                {
                    Advance();
                    SkipNewlinesAndIndents();

                    if (currentToken.Type == TokenType.RBRACE)
                        break;

                    var key = ParseExpression();
                    SkipNewlinesAndIndents();
                    Expect(TokenType.COLON);
                    SkipNewlinesAndIndents();
                    var val = ParseExpression();
                    pairs.Add((key, val));
                    SkipNewlinesAndIndents();
                }

                Expect(TokenType.RBRACE);
                return new DictNode(pairs, line, column);
            }
            else
            {
                // Set or set comprehension
                if (currentToken.Type == TokenType.FOR)
                {
                    return ParseComprehension(firstExpr, line, column, ComprehensionType.Set);
                }

                // Regular set
                var elements = new List<ASTNode> { firstExpr };

                while (currentToken.Type == TokenType.COMMA)
                {
                    Advance();
                    SkipNewlinesAndIndents();

                    if (currentToken.Type == TokenType.RBRACE)
                        break;

                    elements.Add(ParseExpression());
                    SkipNewlinesAndIndents();
                }

                Expect(TokenType.RBRACE);
                return new SetNode(elements, line, column);
            }
        }

        // NEW: Parse generator expressions
        private ASTNode ParseParenthesizedOrTupleOrGenerator()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            Advance(); // Skip (
            SkipNewlinesAndIndents();

            if (currentToken.Type == TokenType.RPAREN)
            {
                Advance();
                return new TupleNode(new List<ASTNode>(), line, column);
            }

            var firstExpr = ParseExpression();
            SkipNewlinesAndIndents();

            // Check for generator expression
            if (currentToken.Type == TokenType.FOR)
            {
                return ParseComprehension(firstExpr, line, column, ComprehensionType.Generator);
            }

            // Check for tuple
            if (currentToken.Type == TokenType.COMMA)
            {
                var elements = new List<ASTNode> { firstExpr };
                while (currentToken.Type == TokenType.COMMA)
                {
                    Advance();
                    SkipNewlinesAndIndents();

                    if (currentToken.Type == TokenType.RPAREN)
                        break;
                    elements.Add(ParseExpression());
                    SkipNewlinesAndIndents();
                }
                Expect(TokenType.RPAREN);
                return new TupleNode(elements, line, column);
            }
            else
            {
                Expect(TokenType.RPAREN);
                return firstExpr;
            }
        }

        // Unified comprehension parsing
        private ASTNode ParseComprehension(ASTNode expr, int line, int column, ComprehensionType type)
        {
            var comprehensions = new List<ComprehensionClause>();

            while (currentToken.Type == TokenType.FOR)
            {
                Advance(); // Skip 'for'

                var variables = new List<string>();
                variables.Add(currentToken.Value);
                Expect(TokenType.IDENTIFIER);

                while (currentToken.Type == TokenType.COMMA)
                {
                    Advance();
                    variables.Add(currentToken.Value);
                    Expect(TokenType.IDENTIFIER);
                }

                Expect(TokenType.IN);
                var iterable = ParseOrExpression();

                var conditions = new List<ASTNode>();
                while (currentToken.Type == TokenType.IF)
                {
                    Advance();
                    conditions.Add(ParseOrExpression());
                }

                comprehensions.Add(new ComprehensionClause(variables, iterable, conditions));
            }

            // Close the comprehension
            switch (type)
            {
                case ComprehensionType.List:
                    Expect(TokenType.RBRACKET);
                    return new ListComprehensionNode(expr, comprehensions[0].Variables[0], 
                        comprehensions[0].Iterable, comprehensions[0].Conditions.FirstOrDefault(), line, column);
                
                // case ComprehensionType.Set:
                //     Expect(TokenType.RBRACE);
                //     return new SetComprehensionNode(expr, comprehensions[0].Variables[0], 
                //         comprehensions[0].Iterable, comprehensions[0].Conditions.FirstOrDefault(), line, column);
                
                // case ComprehensionType.Generator:
                //     Expect(TokenType.RPAREN);
                //     return new GeneratorExpressionNode(expr, comprehensions[0].Variables[0], 
                //         comprehensions[0].Iterable, comprehensions[0].Conditions.FirstOrDefault(), line, column);
                
                default:
                    throw new PythonException("SyntaxError", "Invalid comprehension type", line, column);
            }
        }

        // NEW: Parse dict comprehension
        // private ASTNode ParseDictComprehension(ASTNode key, ASTNode value, int line, int column)
        // {
        //     Advance(); // Skip 'for'

        //     string variable = currentToken.Value;
        //     Expect(TokenType.IDENTIFIER);

        //     Expect(TokenType.IN);
        //     var iterable = ParseOrExpression();

        //     ASTNode condition = null;
        //     if (currentToken.Type == TokenType.IF)
        //     {
        //         Advance();
        //         condition = ParseOrExpression();
        //     }

        //     Expect(TokenType.RBRACE);

        //     return new DictComprehensionNode(key, value, variable, iterable, condition, line, column);
        // }

        private ASTNode ParseArgumentExpression()
        {
            var firstExpr = ParseSingleArgumentExpression();

            if (IsStringLiteral(firstExpr))
            {
                var parts = new List<ASTNode> { firstExpr };

                while (true)
                {
                    int savePos = position;
                    var saveToken = currentToken;

                    SkipNewlinesAndIndents();

                    if (currentToken.Type == TokenType.STRING || currentToken.Type == TokenType.FSTRING)
                    {
                        if (currentToken.Type == TokenType.STRING)
                        {
                            parts.Add(new StringNode(currentToken.Value, currentToken.Line, currentToken.Column));
                            Advance();
                        }
                        else if (currentToken.Type == TokenType.FSTRING)
                        {
                            parts.Add(new FStringNode(currentToken.Value, currentToken.Line, currentToken.Column));
                            Advance();
                        }
                    }
                    else
                    {
                        position = savePos;
                        currentToken = saveToken;
                        break;
                    }
                }

                if (parts.Count > 1)
                {
                    return new StringConcatenationNode(parts, firstExpr.Line, firstExpr.Column);
                }
                else
                {
                    return firstExpr;
                }
            }

            return firstExpr;
        }

        private ASTNode ParseSingleArgumentExpression()
        {
            return ParseConditionalExpression();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsStringLiteral(ASTNode node)
        {
            return node is StringNode || node is FStringNode;
        }
    }

    // Enum for comprehension types
    internal enum ComprehensionType
    {
        List,
        Set,
        Dict,
        Generator
    }

    // Helper class for comprehension clauses
    internal class ComprehensionClause
    {
        public List<string> Variables { get; }
        public ASTNode Iterable { get; }
        public List<ASTNode> Conditions { get; }

        public ComprehensionClause(List<string> variables, ASTNode iterable, List<ASTNode> conditions)
        {
            Variables = variables;
            Iterable = iterable;
            Conditions = conditions;
        }
    }
}